#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.IO;
using CATHODE;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Delta-bake: keep the level's shipped radiosity wholesale and patch only what an edit
    /// invalidated, instead of regenerating everything.
    /// </summary>
    /// <remarks>
    /// <para>The full synthetic bake is chaotically sensitive to structural detail (a one-percent
    /// rect reallocation swings ChallengeMap3's whole-level fit by 14 luma, reproducibly), so for
    /// edited RETAIL levels the robust route is to not regenerate unchanged content at all: the
    /// instonly control runs prove retail's runtime renders correctly under an instanced level,
    /// and the retailstrip experiments prove a loaded retail runtime can be selectively modified
    /// and saved with the game accepting the result.</para>
    /// <para>Version 1 scope: keep every slice verbatim; re-bind the surface-light slices'
    /// entity references so they survive the resource renumbering an instanced save performs
    /// (a retail-loaded runtime otherwise writes its stale raw indices back); zero the lights of
    /// entities the edit deleted; log - but do not yet light - new geometry. Deleted geometry's
    /// baked bounce (a removed wall's shadow) intentionally stays: un-baking is a full-bake
    /// problem by definition.</para>
    /// </remarks>
    public static class RadiosityPatcher
    {
        public static RadiosityBaker.BakeResult PatchLevel(
            Level level,
            RadiosityBakeSettings settings,
            Action<string> log = null)
        {
            RadiosityRuntime runtime = level.RadiosityRuntime
                ?? throw new InvalidOperationException("Level has no RADIOSITY_RUNTIME.BIN to patch.");
            if (runtime.Slices.Count == 0)
                throw new InvalidOperationException("RADIOSITY_RUNTIME.BIN carries no slices - nothing to patch; run a full bake.");

            // The disk still holds the pre-instancing files at this point (Level.Save writes
            // after the bakes), so the ORIGINAL resource ordering - the one the runtime's raw
            // EntityInstanceIndex values refer to - is recoverable regardless of what instancing
            // has done to the in-memory Resources since.
            string resourcesPath = Path.Combine(level.Filepath, "WORLD", "RESOURCES.BIN");
            var originalResources = new Resources(resourcesPath);

            // Current resources by GUID pair, for re-binding original references onto the
            // regenerated collection. Instancing preserves matched GUIDs.
            var currentByGuid = new Dictionary<(uint, uint), Resources.Resource>();
            foreach (Resources.Resource r in level.Resources.Entries)
                currentByGuid[(r.composite_instance_id.AsUInt32, r.resource_id.AsUInt32)] = r;

            int resolved = 0, orphaned = 0, zeroed = 0;
            foreach (RadiosityRuntime.RuntimeDataSlice slice in runtime.Slices)
            {
                Rebind(slice.SurfaceLights?.LightSlices, e => slice.SurfaceLights.LightSliceEntities = e, slice.SurfaceLights?.Lights);
                Rebind(slice.LiveSurfaceLights, e => slice.LiveSurfaceLightEntities = e, null);

                void Rebind(List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice> lightSlices,
                            Action<List<Resources.Resource>> store,
                            List<RadiosityRuntime.RuntimeSurfaceLights.Light> lights)
                {
                    if (lightSlices == null)
                        return;
                    var entities = new List<Resources.Resource>(lightSlices.Count);
                    for (int i = 0; i < lightSlices.Count; i++)
                    {
                        Resources.Resource original = originalResources.GetAtWriteIndex(lightSlices[i].EntityInstanceIndex);
                        Resources.Resource current = null;
                        if (original != null)
                            currentByGuid.TryGetValue((original.composite_instance_id.AsUInt32, original.resource_id.AsUInt32), out current);
                        entities.Add(current);

                        if (current != null)
                        {
                            resolved++;
                            continue;
                        }
                        orphaned++;

                        // The entity no longer exists: its light must not shine. The slice entry
                        // stays (indices into the light array are position-dependent) but its
                        // lights' weights go to zero.
                        if (lights == null)
                            continue;
                        RadiosityRuntime.RuntimeSurfaceLights.LightSlice ls = lightSlices[i];
                        for (int l = 0; l < ls.NumItems && ls.FirstItem + l < lights.Count; l++)
                        {
                            RadiosityRuntime.RuntimeSurfaceLights.Light light = lights[(int)ls.FirstItem + l];
                            if (light.Weight == 0)
                                continue;
                            light.Weight = 0;
                            lights[(int)ls.FirstItem + l] = light;
                            zeroed++;
                        }
                    }
                    store(entities);
                }
            }

            // Instance map: entries resolved at load keep their Resource references; any that
            // failed to resolve are re-bound through the original ordering the same way.
            int mapFixed = 0, mapOrphaned = 0;
            if (level.RadiosityInstanceMap != null)
            {
                foreach (RadiosityInstanceMap.Entry entry in level.RadiosityInstanceMap.Entries)
                {
                    if (entry.Resource != null)
                        continue;
                    Resources.Resource original = originalResources.GetAtWriteIndex(entry.resource_index);
                    if (original != null &&
                        currentByGuid.TryGetValue((original.composite_instance_id.AsUInt32, original.resource_id.AsUInt32), out Resources.Resource current))
                    {
                        entry.Resource = current;
                        mapFixed++;
                    }
                    else
                    {
                        mapOrphaned++;
                    }
                }
            }

            // New content census: movers whose resource GUID has no island in the shipped map
            // would need lighting of their own. Version 1 only reports them - they render with
            // whatever MODEL_PARAMS instancing gave them.
            var mappedGuids = new HashSet<(uint, uint)>();
            foreach (RadiosityInstanceMap.Entry entry in level.RadiosityInstanceMap?.Entries ?? new List<RadiosityInstanceMap.Entry>())
            {
                Resources.Resource r = entry.Resource ?? originalResources.GetAtWriteIndex(entry.resource_index);
                if (r != null)
                    mappedGuids.Add((r.composite_instance_id.AsUInt32, r.resource_id.AsUInt32));
            }
            int matchedMovers = 0, unmappedMovers = 0, movedMovers = 0;
            var movedDeltas = new List<System.Numerics.Vector3>();
            var deltaMovers = new HashSet<int>();
            for (int i = 0; i < level.Movers.Entries.Count; i++)
            {
                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[i];
                if (mover.Resource == null)
                    continue;
                if (mappedGuids.Contains((mover.Resource.composite_instance_id.AsUInt32, mover.Resource.resource_id.AsUInt32)))
                {
                    matchedMovers++;

                    // Moved since the pristine bake? Then its carried lightmap address would
                    // light it as it stood at the old location - rebake it.
                    if (settings.RetailTransforms != null)
                    {
                        ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                        if (settings.RetailTransforms.TryGetValue(key, out System.Numerics.Matrix4x4 pristineT) &&
                            Differs(pristineT, mover.Transform))
                        {
                            // Stale-identity carries: a handful of FX-family movers snapshot a
                            // pristine transform of EXACTLY the origin while sitting far from it.
                            // They are not moves - they polluted every census (CM9 and CM3 both
                            // carry ~4), invented 20-35m "deltas", and burned the level's few
                            // orphaned island ids on invisible junk.
                            bool staleIdentity = pristineT.M41 == 0 && pristineT.M42 == 0 && pristineT.M43 == 0 &&
                                (mover.Transform.M41 != 0 || mover.Transform.M42 != 0 || mover.Transform.M43 != 0);
                            if (!staleIdentity)
                            {
                                deltaMovers.Add(i);
                                movedMovers++;
                                movedDeltas.Add(new System.Numerics.Vector3(
                                    mover.Transform.M41 - pristineT.M41,
                                    mover.Transform.M42 - pristineT.M42,
                                    mover.Transform.M43 - pristineT.M43));
                            }
                        }
                    }
                }
                else
                {
                    unmappedMovers++;

                    // "New" means absent from the PRISTINE MVR, not merely absent from the retail
                    // island map: instancing manufactures thousands of movers whose GUIDs never
                    // join the map (Solace ships 11,907 of them before any edit), but those are
                    // pre-existing content carrying pristine MODEL_PARAMS - rebaking them is both
                    // wasteful and wrong. Only geometry the edit genuinely added qualifies.
                    if (mover.RenderableElements == null || mover.RenderableElements.Count == 0)
                        continue;
                    ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                    if (settings.RetailTransforms == null || !settings.RetailTransforms.ContainsKey(key))
                        deltaMovers.Add(i);
                }
            }

            log?.Invoke("Radiosity patch census: deltaMovers=" + deltaMovers.Count + " (moved=" + movedMovers + ")");

            // Rigid group move detection: when the moved movers overwhelmingly share one
            // translation (a shifted room or a shifted level), delta probes should reference
            // the retail lighting of the place the group CAME FROM - set the calibration
            // offset to map their positions back.
            if (movedDeltas.Count > 0)
            {
                var sx = new List<float>(); var sy = new List<float>(); var sz = new List<float>();
                foreach (System.Numerics.Vector3 d in movedDeltas) { sx.Add(d.X); sy.Add(d.Y); sz.Add(d.Z); }
                sx.Sort(); sy.Sort(); sz.Sort();
                var median = new System.Numerics.Vector3(sx[sx.Count / 2], sy[sy.Count / 2], sz[sz.Count / 2]);
                int agree = 0;
                foreach (System.Numerics.Vector3 d in movedDeltas)
                    if ((d - median).Length() < 0.25f) agree++;
                if (median.Length() > 1.0f && agree >= movedDeltas.Count * 8 / 10)
                {
                    settings.DeltaCalibrationOffset = -median;
                    log?.Invoke("Radiosity patch census: rigid group move " + median.Length().ToString("0.0") +
                                "m shared by " + agree + "/" + movedDeltas.Count +
                                " moved movers - delta calibration references the original position");
                }
            }
            // A rigid whole-level move keeps every lightmap valid (they are UV-addressed):
            // nothing needs converting, the field is translated instead (hash + probe
            // position tables + probe BVHs). The caller sets this for level relocation.
            if (settings.DeltaIgnoreMoves && deltaMovers.Count > 0)
            {
                log?.Invoke("Radiosity patch census: DeltaIgnoreMoves - " + deltaMovers.Count +
                            " delta movers left untouched (rigid-move mode, retail radiosity kept)");
                deltaMovers.Clear();
            }
            int retailSliceCount = runtime.Slices.Count;
            int deltaIslands = 0, dynConverted = 0, probeIslands = 0;
            if (deltaMovers.Count > 0)
            {
                // Default route: force the delta movers onto the DYNAMIC radiosity path (no
                // atlas allocation needed at all); whatever the converter cannot fully convert
                // falls back to the lightmap delta bake. Converted movers deliberately STAY in
                // deltaMovers - the pristine-params restore below must skip them, or it would
                // re-apply a retail rect over the zeroed dynamic convention.
                HashSet<int> lightmapMovers = deltaMovers;
                if (settings.DeltaDynamicProps)
                {
                    // Hybrid split (H12/H13): the lightmap delta path lights everything its
                    // geometry collector can bake - including rooms the probe path never lit -
                    // so only the movers it CANNOT bake (dynamic-class props) go down the
                    // convert+probe route; the rest keep the lightmap path below.
                    HashSet<int> convertSet = deltaMovers;
                    if (settings.DeltaHybridSplit)
                    {
                        convertSet = new HashSet<int>();
                        foreach (int mi in deltaMovers)
                            if (!RadiosityGeometry.IsBakeable(level.Movers.Entries[mi], settings))
                                convertSet.Add(mi);
                        log?.Invoke("Radiosity patch hybrid: " + convertSet.Count + " of " + deltaMovers.Count +
                                    " delta movers to the dynamic path, rest to the lightmap delta bake");
                    }

                    // Probe field extension FIRST: content outside the retail volume field gets
                    // its own appended radiance slice + fine-celled hash. This must precede the
                    // converter - geometry collection excludes dynamic-class movers, so the
                    // content is baked while its materials are still static-class. The FULL
                    // delta set builds the field even in hybrid mode: the probes come from the
                    // static surfaces, and the dynamic props (which contribute no bakeable
                    // geometry of their own - H13 baked zero slices from the props alone) sample
                    // that field at their bounds centres.
                    if (settings.DeltaProbeOnlySlice && convertSet.Count > 0)
                        probeIslands = RadiosityBaker.AppendProbeOnlySlice(level, settings, deltaMovers, log);

                    DynamicRadiosityConverter.Result conv = DynamicRadiosityConverter.Convert(level, convertSet, log, settings.DeltaDropInstanceMapRows,
                        settings.DeltaMintedIslands);
                    dynConverted = conv.ConvertedMovers.Count;
                    lightmapMovers = new HashSet<int>(deltaMovers);
                    lightmapMovers.ExceptWith(conv.ConvertedMovers);
                }
                bool ranLightmapDelta = settings.PatchBakeDelta && lightmapMovers.Count > 0;
                if (ranLightmapDelta)
                {
                    // AppendDeltaSlices bakes ONE slice per call - built for bounded edits.
                    // A whole added environment overflows it (H12/H13: 2,507 islands parked on
                    // 1x1 and rendered unmapped-dark), so large deltas are chunked by zone and
                    // appended one slice per chunk.
                    List<HashSet<int>> demandChunks = settings.DeltaAtlasFillTarget > 0.0f
                        ? RadiosityBaker.ChunkDeltaByAtlasDemand(level, settings, lightmapMovers, log)
                        : null;
                    if (demandChunks != null)
                    {
                        // Each call only sees its own chunk; tell it the whole delta so another
                        // chunk's geometry is not mistaken for donor material (DeltaAllMovers).
                        HashSet<int> priorAll = settings.DeltaAllMovers;
                        settings.DeltaAllMovers = lightmapMovers;
                        try
                        {
                            foreach (HashSet<int> chunk in demandChunks)
                                deltaIslands += RadiosityBaker.AppendDeltaSlices(level, settings, chunk, log);
                        }
                        finally { settings.DeltaAllMovers = priorAll; }
                    }
                    else if (settings.DeltaLightmapChunkMovers > 0 && lightmapMovers.Count > settings.DeltaLightmapChunkMovers)
                    {
                        // Chunked by ZONE. Grouping by island id instead was tried and REVERTED
                        // (H28): at this point the delta movers have no instance-map rows yet -
                        // the baker assigns their islands during each chunk's own bake - so 6249
                        // of 6252 movers had no island to group on, the packing came out all but
                        // identical, and the run measured slightly worse (cam16 0.77 -> 0.55,
                        // cam3 0.74 -> 0.60). Any island-coherent scheme has to run inside the
                        // baker where the assignment happens, not here.
                        var byZone = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<int>>();
                        foreach (int mi in lightmapMovers)
                        {
                            var z = level.Movers.Entries[mi].PrimaryZoneID;
                            uint zk = z == CATHODE.Scripting.ShortGuid.Invalid ? 0u : z.AsUInt32;
                            if (!byZone.TryGetValue(zk, out var zl)) byZone[zk] = zl = new System.Collections.Generic.List<int>();
                            zl.Add(mi);
                        }
                        var chunks = new System.Collections.Generic.List<System.Collections.Generic.List<int>>();
                        foreach (var zl in System.Linq.Enumerable.OrderByDescending(byZone.Values, l => l.Count))
                        {
                            System.Collections.Generic.List<int> target = null;
                            foreach (var c in chunks)
                                if (c.Count + zl.Count <= settings.DeltaLightmapChunkMovers) { target = c; break; }
                            if (target == null) { target = new System.Collections.Generic.List<int>(); chunks.Add(target); }
                            target.AddRange(zl);
                        }
                        log?.Invoke("Radiosity delta lightmap: " + lightmapMovers.Count + " movers in " + byZone.Count +
                                    " zones -> " + chunks.Count + " slice chunks (cap " + settings.DeltaLightmapChunkMovers + ")");
                        HashSet<int> priorAllZ = settings.DeltaAllMovers;
                        settings.DeltaAllMovers = lightmapMovers;
                        try
                        {
                            foreach (var chunk in chunks)
                                deltaIslands += RadiosityBaker.AppendDeltaSlices(level, settings, new HashSet<int>(chunk), log);
                        }
                        finally { settings.DeltaAllMovers = priorAllZ; }
                    }
                    else
                        deltaIslands = RadiosityBaker.AppendDeltaSlices(level, settings, lightmapMovers, log);
                }

                // Scheduling rows LAST: rendering follows a resource's FIRST map row, so these
                // must land after the lightmap delta's real rect rows or they hijack their
                // bound movers into dead-texel black (H16's doorway).
                if (settings.DeltaPendingScheduleRows != null && settings.DeltaPendingScheduleRows.Count > 0 &&
                    level.RadiosityInstanceMap?.Entries != null)
                {
                    foreach ((int island, object resource) in settings.DeltaPendingScheduleRows)
                    {
                        level.RadiosityInstanceMap.Entries.Add(new RadiosityInstanceMap.Entry
                        {
                            lightmap_transform = island,
                            Resource = (Resources.Resource)resource
                        });
                        settings.DeltaMintedIslands?.Add(island);
                    }
                    log?.Invoke("Radiosity patch: " + settings.DeltaPendingScheduleRows.Count +
                                " scheduling rows appended after the lightmap delta");
                }
            }

            // Densify the KEPT retail slices' volume hashes: same probes, finer grid, so the
            // engine's 8-cell blend spans half the distance per doubling - the measured fix for
            // dynamic props reading over-bright in dark rooms. Appended slices (probe-only or
            // lightmap delta) are born fine-celled and are never touched.
            if (settings.VolumeHashUpsampleFactor >= 2)
                VolumeHashUtils.UpsampleSlices(runtime, retailSliceCount, settings.VolumeHashUpsampleFactor, settings.VolumeHashRebind, log);

            // Restore pristine MODEL_PARAMS on matched movers: instancing rebuilds a subset of
            // movers without carrying the lightmap transform, and a rect-less mover samples a
            // wrong atlas region (uniform wrong-colour walls) or degenerates entirely. Delta
            // movers are EXCLUDED: the delta bake just wrote them fresh rects in the appended
            // slice, and restoring the pristine address on top made a moved wall sample dead
            // texels of the small delta atlas - it rendered black through three otherwise
            // correct bakes before this order dependency was found.
            int paramsRestored = 0;
            if (settings.RetailModelParams != null)
            {
                for (int i = 0; i < level.Movers.Entries.Count; i++)
                {
                    Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[i];
                    if (deltaMovers.Contains(i))
                        continue;
                    if (mover.Resource == null || mover.RenderConstants == null)
                        continue;
                    ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                    if (!settings.RetailModelParams.TryGetValue(key, out byte[] pristine))
                        continue;
                    byte[] raw = mover.RenderConstants.RawBytes;
                    if (raw == null || raw.Length < 16)
                        continue;
                    bool differs = false;
                    for (int b = 0; b < 16; b++)
                        if (raw[b] != pristine[b]) { differs = true; break; }
                    if (!differs)
                        continue;
                    Array.Copy(pristine, raw, 16);
                    mover.RenderConstants.SetRawBytes(raw);
                    paramsRestored++;
                }
            }

            var result = new RadiosityBaker.BakeResult
            {
                Slices = runtime.Slices.Count,
                Instances = runtime.InstanceSliceIndices.Count,
                Message = "Radiosity PATCH: slices kept=" + runtime.Slices.Count +
                          " lightRefs resolved=" + resolved + " orphaned=" + orphaned +
                          " (lights zeroed=" + zeroed + ")" +
                          " map rebound=" + mapFixed + " mapOrphaned=" + mapOrphaned +
                          " movers matched=" + matchedMovers + " unmapped=" + unmappedMovers +
                          " moved=" + movedMovers + " forcedDynamic=" + dynConverted +
                          " probeSliceIslands=" + probeIslands +
                          " deltaIslandsBaked=" + deltaIslands +
                          " modelParamsRestored=" + paramsRestored +
                          (unmappedMovers > 0 ? "  [unmapped movers keep instancing's MODEL_PARAMS and get no new lighting yet]" : "")
            };
            log?.Invoke(result.Message);
            return result;
        static bool Differs(System.Numerics.Matrix4x4 a, System.Numerics.Matrix4x4 b)
        {
            // Any element off by more than a hair counts: rotation and scale changes need the
            // rebake exactly as translations do.
            const float eps = 0.005f;
            return Math.Abs(a.M11 - b.M11) > eps || Math.Abs(a.M12 - b.M12) > eps || Math.Abs(a.M13 - b.M13) > eps ||
                   Math.Abs(a.M21 - b.M21) > eps || Math.Abs(a.M22 - b.M22) > eps || Math.Abs(a.M23 - b.M23) > eps ||
                   Math.Abs(a.M31 - b.M31) > eps || Math.Abs(a.M32 - b.M32) > eps || Math.Abs(a.M33 - b.M33) > eps ||
                   Math.Abs(a.M41 - b.M41) > eps || Math.Abs(a.M42 - b.M42) > eps || Math.Abs(a.M43 - b.M43) > eps;
        }

        }
    }
}
#endif
