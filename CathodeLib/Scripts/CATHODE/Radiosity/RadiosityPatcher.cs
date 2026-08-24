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
                    // Probe field extension FIRST: content outside the retail volume field gets
                    // its own appended radiance slice + fine-celled hash. This must precede the
                    // converter - geometry collection excludes dynamic-class movers, so the
                    // content is baked while its materials are still static-class.
                    if (settings.DeltaProbeOnlySlice)
                        probeIslands = RadiosityBaker.AppendProbeOnlySlice(level, settings, deltaMovers, log);

                    DynamicRadiosityConverter.Result conv = DynamicRadiosityConverter.Convert(level, deltaMovers, log, settings.DeltaDropInstanceMapRows);
                    dynConverted = conv.ConvertedMovers.Count;
                    lightmapMovers = new HashSet<int>(deltaMovers);
                    lightmapMovers.ExceptWith(conv.ConvertedMovers);
                }
                if (settings.PatchBakeDelta && lightmapMovers.Count > 0)
                    deltaIslands = RadiosityBaker.AppendDeltaSlices(level, settings, lightmapMovers, log);
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
