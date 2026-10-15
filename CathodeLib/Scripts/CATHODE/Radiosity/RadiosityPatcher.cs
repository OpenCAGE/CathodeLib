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
    /// <para>For edited RETAIL levels the robust route is not to regenerate unchanged content at all: the full bake is
    /// sensitive to structural detail, and the game accepts a loaded retail runtime selectively modified and saved.</para>
    /// <para>The pipeline, in this order. Keep every shipped slice verbatim. Re-bind the
    /// surface-light slices' entity references and the instance-map rows so they survive the
    /// resource renumbering an instanced save performs (a retail-loaded runtime otherwise writes
    /// its stale raw indices back); zero the lights of entities the edit deleted and drop the rows
    /// of resources that no longer have a mover. Census the movers the edit ADDED or MOVED (the
    /// delta). Extend the volume probe field over delta content outside it
    /// (<see cref="RadiosityBaker.AppendProbeOnlySlice"/>), then force the delta movers onto the
    /// dynamic radiosity path (<see cref="DynamicRadiosityConverter"/>); whatever the converter
    /// cannot convert is baked into appended lightmap slices
    /// (<see cref="RadiosityBaker.AppendDeltaSlices"/>). Append the probe slices' scheduling rows
    /// (rendering follows a resource's first map row, so they go last), and finally restore the
    /// pristine MODEL_PARAMS of every mover outside the delta. Deleted geometry's baked bounce (a
    /// removed wall's shadow) intentionally stays: un-baking is a full-bake problem by
    /// definition.</para>
    /// </remarks>
    internal static class RadiosityPatcher
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
            // The census and the MODEL_PARAMS restore need the pristine snapshots, which Instancing
            // harvests (PatchRetailRuntime on) before it rebuilds the movers.
            if (settings.RetailTransforms == null || settings.RetailModelParams == null)
                throw new InvalidOperationException("Patching needs the pristine RetailTransforms and RetailModelParams snapshots - Instancing harvests them when PatchRetailRuntime is on.");
            var state = new DeltaBakeState(settings.RetailModelParams, settings.RetailTransforms);
            var timer = new RadiosityBaker.PhaseTimer();

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

                // Resolving by GUID does not catch a stale index: instancing re-emits every purged retail resource as a
                // dangling placeholder (so the runtime's positional EntityInstanceIndex values stay aligned), so the GUID
                // of a retail-lit entity that no longer produces a mover still "resolves". Such rows must be dropped, as
                // must unresolved ones (saved, they keep their stale retail index); the engine crashes on load otherwise.
                var moverResources = new HashSet<(uint, uint)>();
                foreach (Movers.MOVER_DESCRIPTOR mover in level.Movers.Entries)
                    if (mover.Resource != null)
                        moverResources.Add((mover.Resource.composite_instance_id.AsUInt32, mover.Resource.resource_id.AsUInt32));
                int mapDropped = level.RadiosityInstanceMap.Entries.RemoveAll(entry =>
                    entry.Resource == null ||
                    !moverResources.Contains((entry.Resource.composite_instance_id.AsUInt32, entry.Resource.resource_id.AsUInt32)));
                if (mapDropped > 0)
                    log?.Invoke("Radiosity patch: dropped " + mapDropped + " instance-map row(s) whose resource no longer has a mover - the engine cannot bind them and crashes on load if they are kept");
            }
            timer.Lap("rebind");

            // Census: the delta is every mover the edit MOVED (its resource has an island in the
            // shipped map, but its transform differs from the pristine one) or ADDED (no island,
            // and absent from the pristine MVR). Everything else keeps its shipped lighting.
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
                    ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                    if (settings.RetailTransforms.TryGetValue(key, out System.Numerics.Matrix4x4 pristineT) &&
                        Differs(pristineT, mover.Transform))
                    {
                        // Stale-identity carries: a few FX-family movers snapshot a pristine transform of EXACTLY the
                        // origin while sitting far from it. They are not moves.
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
                else
                {
                    unmappedMovers++;

                    // "New" means absent from the PRISTINE MVR, not merely from the retail island map: instancing
                    // manufactures thousands of movers whose GUIDs never join the map, but they are pre-existing content
                    // carrying pristine MODEL_PARAMS.
                    if (mover.RenderableElements == null || mover.RenderableElements.Count == 0)
                        continue;
                    ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                    if (!settings.RetailTransforms.ContainsKey(key))
                        deltaMovers.Add(i);
                }
            }

            log?.Invoke("Radiosity patch census: deltaMovers=" + deltaMovers.Count + " (moved=" + movedMovers + ")");

            // Rigid group move: when the moved movers overwhelmingly share one translation (a shifted room or level),
            // delta probes reference the retail lighting of the place the group CAME FROM. The minimum cohort guards
            // against the 2-3 PHANTOM "moves" a duplicated environment manufactures (added movers whose GUID pair
            // collides with a pristine mover's), whose consensus once pointed 30 m off and whited out a whole duplicate.
            if (movedDeltas.Count >= 8)
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
                    state.CalibrationOffset = -median;
                    log?.Invoke("Radiosity patch census: rigid group move " + median.Length().ToString("0.0") +
                                "m shared by " + agree + "/" + movedDeltas.Count +
                                " moved movers - delta calibration references the original position");
                }
            }

            // Added-group provenance: a duplicated environment's movers are NEW (fresh composite instance GUIDs) but each
            // shares its resource_id with the pristine mover it was copied from. Where the pristine level places that
            // resource exactly once, the added mover votes for the translation it was copied across; genuinely new
            // content scatters its votes and leaves the offset at zero.
            if (state.CalibrationOffset == System.Numerics.Vector3.Zero && deltaMovers.Count > 0)
            {
                var ridCount = new Dictionary<uint, int>();
                var ridPos = new Dictionary<uint, System.Numerics.Vector3>();
                foreach (KeyValuePair<ulong, System.Numerics.Matrix4x4> kv in settings.RetailTransforms)
                {
                    uint rid = (uint)kv.Key;
                    ridCount[rid] = ridCount.TryGetValue(rid, out int c) ? c + 1 : 1;
                    ridPos[rid] = new System.Numerics.Vector3(kv.Value.M41, kv.Value.M42, kv.Value.M43);
                }
                var votes = new List<System.Numerics.Vector3>();
                foreach (int mi in deltaMovers)
                {
                    Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[mi];
                    if (mv.Resource == null) continue;
                    uint rid = mv.Resource.resource_id.AsUInt32;
                    if (!ridCount.TryGetValue(rid, out int c) || c != 1) continue;
                    // ADDED movers only: a full-key hit means the census matched this mover as
                    // moved (or a phantom collision) - either way it is not provenance evidence.
                    ulong key = ((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | rid;
                    if (settings.RetailTransforms.ContainsKey(key)) continue;
                    votes.Add(new System.Numerics.Vector3(mv.Transform.M41, mv.Transform.M42, mv.Transform.M43) - ridPos[rid]);
                }
                if (votes.Count >= 24)
                {
                    var vx = new List<float>(); var vy = new List<float>(); var vz = new List<float>();
                    foreach (System.Numerics.Vector3 d in votes) { vx.Add(d.X); vy.Add(d.Y); vz.Add(d.Z); }
                    vx.Sort(); vy.Sort(); vz.Sort();
                    var median = new System.Numerics.Vector3(vx[vx.Count / 2], vy[vy.Count / 2], vz[vz.Count / 2]);
                    int agree = 0;
                    foreach (System.Numerics.Vector3 d in votes)
                        if ((d - median).Length() < 0.25f) agree++;
                    if (median.Length() > 1.0f && agree >= votes.Count * 8 / 10)
                    {
                        state.CalibrationOffset = -median;
                        log?.Invoke("Radiosity patch census: added group copied " + median.Length().ToString("0.0") +
                                    "m shared by " + agree + "/" + votes.Count +
                                    " added movers - delta calibration references the copied-from position");
                    }
                }
            }
            timer.Lap("census");
            int deltaIslands = 0, dynConverted = 0, probeIslands = 0;
            if (deltaMovers.Count > 0)
            {
                // Probe field extension FIRST: it must precede the converter, because geometry collection excludes
                // dynamic-class movers, so the content is baked while its materials are still static-class.
                probeIslands = RadiosityBaker.AppendProbeOnlySlice(level, state, deltaMovers, log);
                timer.Lap("probeSlice");

                // Then force the delta movers onto the DYNAMIC radiosity path; whatever the converter cannot convert falls
                // back to the lightmap delta bake. Converted movers STAY in deltaMovers: the pristine-params restore below
                // must skip them, or it would re-apply a retail rect over the zeroed dynamic convention.
                DynamicRadiosityConverter.Result conv = DynamicRadiosityConverter.Convert(level, deltaMovers, log);
                dynConverted = conv.ConvertedMovers.Count;
                HashSet<int> lightmapMovers = new HashSet<int>(deltaMovers);
                lightmapMovers.ExceptWith(conv.ConvertedMovers);
                timer.Lap("convert");
                if (lightmapMovers.Count > 0)
                {
                    // AppendDeltaSlices bakes ONE slice per call, so a large delta (a whole added environment) is
                    // chunked by atlas demand, one slice per chunk; the zone chunking below is the fallback when that
                    // returns null.
                    List<HashSet<int>> demandChunks = RadiosityBaker.ChunkDeltaByAtlasDemand(level, lightmapMovers, log);
                    timer.Lap("chunk");
                    int appended = 0;
                    if (demandChunks != null)
                    {
                        // Each call only sees its own chunk; tell it the whole delta so another
                        // chunk's geometry is not mistaken for donor material (allMovers).
                        foreach (HashSet<int> chunk in demandChunks)
                        {
                            deltaIslands += RadiosityBaker.AppendDeltaSlices(level, state, chunk, lightmapMovers, log);
                            timer.Lap("append[" + appended++ + "]");
                        }
                    }
                    else if (lightmapMovers.Count > RadiosityTuning.DeltaLightmapChunkMovers)
                    {
                        // Chunked by ZONE. Grouping by island id was REVERTED (H28): the delta movers have no
                        // instance-map rows yet at this point, so an island-coherent scheme has to run inside the baker.
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
                                if (c.Count + zl.Count <= RadiosityTuning.DeltaLightmapChunkMovers) { target = c; break; }
                            if (target == null) { target = new System.Collections.Generic.List<int>(); chunks.Add(target); }
                            target.AddRange(zl);
                        }
                        log?.Invoke("Radiosity delta lightmap: " + lightmapMovers.Count + " movers in " + byZone.Count +
                                    " zones -> " + chunks.Count + " slice chunks (cap " + RadiosityTuning.DeltaLightmapChunkMovers + ")");
                        timer.Lap("zoneChunk");
                        foreach (var chunk in chunks)
                        {
                            deltaIslands += RadiosityBaker.AppendDeltaSlices(level, state, new HashSet<int>(chunk), lightmapMovers, log);
                            timer.Lap("append[" + appended++ + "]");
                        }
                    }
                    else
                    {
                        deltaIslands = RadiosityBaker.AppendDeltaSlices(level, state, lightmapMovers, log: log);
                        timer.Lap("append");
                    }
                }

                // Scheduling rows LAST: rendering follows a resource's FIRST map row, so they go after the lightmap
                // delta's real rect rows (first, they hijacked their bound movers into dead-texel black: H16's doorway),
                // and after the converter, whose row drop would remove them (their seeds are converted movers; H5).
                if (state.PendingScheduleRows.Count > 0 && level.RadiosityInstanceMap?.Entries != null)
                {
                    foreach ((int island, Resources.Resource resource) in state.PendingScheduleRows)
                    {
                        level.RadiosityInstanceMap.Entries.Add(new RadiosityInstanceMap.Entry
                        {
                            lightmap_transform = island,
                            Resource = resource
                        });
                    }
                    log?.Invoke("Radiosity patch: " + state.PendingScheduleRows.Count +
                                " scheduling rows appended after the lightmap delta");
                }
                timer.Lap("rows");
            }

            // Restore pristine MODEL_PARAMS on matched movers: instancing rebuilds some movers without their lightmap
            // transform, and a rect-less mover samples a wrong atlas region. Delta movers are EXCLUDED: the delta bake
            // just wrote their fresh rects, and the pristine address on top made a moved wall sample dead texels (black).
            int paramsRestored = 0;
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
            timer.Lap("restore");
            log?.Invoke("Radiosity patch timing: " + timer);

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
                          (unmappedMovers > 0 ? "  [unmapped movers that predate the edit keep their shipped lighting; added ones are lit by the delta path]" : "")
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

    /// <summary>
    /// The state of one delta bake: created by <see cref="RadiosityPatcher.PatchLevel"/> for each call and handed to
    /// the append passes, never kept on the caller's settings object (which may be shared between saves).
    /// </summary>
    internal sealed class DeltaBakeState
    {
        /// <summary>The caller's pristine snapshots (<see cref="RadiosityBakeSettings.RetailModelParams"/>,
        /// <see cref="RadiosityBakeSettings.RetailTransforms"/>); PatchLevel throws before creating the state without them.</summary>
        public readonly Dictionary<ulong, byte[]> RetailModelParams;
        public readonly Dictionary<ulong, System.Numerics.Matrix4x4> RetailTransforms;

        /// <summary>
        /// Added to a delta probe's position before querying RETAIL probes in the energy calibration
        /// (RadiosityBaker.CalibrateDeltaEnergy): a rigidly-moved group (a shifted room, a shifted level) or a copied one
        /// references the retail lighting of the place it CAME FROM instead of the flat bias fallback. Set by the census
        /// when the moved or added movers share one translation, before any slice is appended.
        /// </summary>
        public System.Numerics.Vector3 CalibrationOffset = System.Numerics.Vector3.Zero;

        /// <summary>
        /// (island id, resource) scheduling rows the probe-only slices want in the instance map. The patcher adds them
        /// AFTER the lightmap delta writes its real rows, so a resource's first row - the one rendering follows - stays its
        /// real rect binding, and the scheduling row only feeds the relight walk. Adding them first blackened whatever the
        /// row bound (H16's doorway).
        /// </summary>
        public readonly List<(int island, Resources.Resource resource)> PendingScheduleRows =
            new List<(int island, Resources.Resource resource)>();

        public DeltaBakeState(Dictionary<ulong, byte[]> retailModelParams,
                              Dictionary<ulong, System.Numerics.Matrix4x4> retailTransforms)
        {
            RetailModelParams = retailModelParams;
            RetailTransforms = retailTransforms;
        }
    }
}
#endif
