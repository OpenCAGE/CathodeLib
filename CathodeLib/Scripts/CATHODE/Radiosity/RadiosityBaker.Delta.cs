#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CATHODE;

namespace CathodeLib.Radiosity
{
    public static partial class RadiosityBaker
    {
        /// <summary>
        /// Slide a purely-translated island's retail radiosity data to its new position, in place, inside its own retail
        /// slice: cluster positions, surface probe positions and texel-coincident input probes move by the island's world
        /// delta; diets, scatter, mangle map, surface lights, rect and instance-map rows stay retail bytes. Movers
        /// translated here are REMOVED from <paramref name="deltaMovers"/>; islands that rotate, scale, split their deltas
        /// or cannot be identified fall through to the append-slice path.
        /// </summary>
        private static void TranslateMovedIslands(
            Level level, RadiosityRuntime runtime, DeltaBakeState state,
            HashSet<int> deltaMovers, Action<string> log)
        {
            if (level.RadiosityInstanceMap?.Entries == null)
                return;

            // Retail island id per resource key, from the (still retail) instance map - and the
            // reverse: every resource an island binds, for the whole-island coverage check below.
            var islandForKey = new Dictionary<ulong, int>();
            var keysForIsland = new Dictionary<int, HashSet<ulong>>();
            foreach (RadiosityInstanceMap.Entry e in level.RadiosityInstanceMap.Entries)
            {
                Resources.Resource r = e.Resource ?? level.Resources.GetAtWriteIndex(e.resource_index);
                if (r == null) continue;
                ulong k = ((ulong)r.composite_instance_id.AsUInt32 << 32) | r.resource_id.AsUInt32;
                if (!islandForKey.ContainsKey(k))
                    islandForKey[k] = e.lightmap_transform;
                if (!keysForIsland.TryGetValue(e.lightmap_transform, out HashSet<ulong> ks))
                    keysForIsland[e.lightmap_transform] = ks = new HashSet<ulong>();
                ks.Add(k);
            }

            // First mover per resource key, for verifying island-mates that are not in the census.
            var moverForKey = new Dictionary<ulong, int>();
            for (int m = 0; m < level.Movers.Entries.Count; m++)
            {
                Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[m];
                if (mv.Resource == null) continue;
                ulong k = ((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | mv.Resource.resource_id.AsUInt32;
                if (!moverForKey.ContainsKey(k))
                    moverForKey[k] = m;
            }

            // Group the delta movers by retail island, keeping only pure translations.
            var groups = new Dictionary<int, List<(int mover, Vector3 delta)>>();
            var unqualified = new HashSet<int>();   //islands with any non-translated/rotated member
            foreach (int m in deltaMovers)
            {
                if (m < 0 || m >= level.Movers.Entries.Count) continue;
                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[m];
                if (mover.Resource == null) continue;
                ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                if (!islandForKey.TryGetValue(key, out int islandId)) continue;   //added content
                if (!state.RetailTransforms.TryGetValue(key, out System.Numerics.Matrix4x4 pristineT))
                {
                    unqualified.Add(islandId);
                    continue;
                }
                //Rotation/scale must be unchanged for a slide to be valid.
                System.Numerics.Matrix4x4 cur = mover.Transform;
                float rotDiff =
                    Math.Abs(cur.M11 - pristineT.M11) + Math.Abs(cur.M12 - pristineT.M12) + Math.Abs(cur.M13 - pristineT.M13) +
                    Math.Abs(cur.M21 - pristineT.M21) + Math.Abs(cur.M22 - pristineT.M22) + Math.Abs(cur.M23 - pristineT.M23) +
                    Math.Abs(cur.M31 - pristineT.M31) + Math.Abs(cur.M32 - pristineT.M32) + Math.Abs(cur.M33 - pristineT.M33);
                if (rotDiff > 0.01f)
                {
                    unqualified.Add(islandId);
                    continue;
                }
                var delta = new Vector3(cur.M41 - pristineT.M41, cur.M42 - pristineT.M42, cur.M43 - pristineT.M43);
                if (!groups.TryGetValue(islandId, out List<(int, Vector3)> list))
                    groups[islandId] = list = new List<(int, Vector3)>();
                list.Add((m, delta));
            }

            foreach (KeyValuePair<int, List<(int mover, Vector3 delta)>> group in groups)
            {
                int islandId = group.Key;
                if (unqualified.Contains(islandId))
                    continue;
                //Uniform world delta across the island's movers - a composite instance moves as
                //one body; disagreeing deltas mean per-entity edits and need a re-bake.
                Vector3 d = group.Value[0].delta;
                bool uniform = true;
                foreach ((int _, Vector3 dd) in group.Value)
                    if ((dd - d).Length() > 0.005f) { uniform = false; break; }
                if (!uniform || d.Length() < 1e-4f)
                    continue;
                // A slide keeps the island's retail diets, scatter and LIGHT INJECTION - all of
                // which describe the OLD surroundings. Valid for nudges; a cross-room relocation
                // must re-light from its destination instead (the graft/appended paths).
                if (d.Length() > 2.0f)
                {
                    log?.Invoke("    delta translate: island " + islandId + " moved " +
                                d.Length().ToString("0.0") + "m - too far to slide, re-lighting instead");
                    continue;
                }
                if (islandId < 0 || islandId >= runtime.InstanceSliceIndices.Count)
                    continue;

                // WHOLE-ISLAND coverage: a slide moves the lighting of EVERY resource the instance map binds to this rect,
                // so every one must have moved by the same delta, censused or not (an entity-level move slid an island out
                // from under its unmoved mates and left the moved mover on its old lightmap). Partial moves re-bake.
                if (keysForIsland.TryGetValue(islandId, out HashSet<ulong> islandKeys))
                {
                    var censusKeys = new HashSet<ulong>();
                    foreach ((int m, Vector3 _) in group.Value)
                    {
                        Movers.MOVER_DESCRIPTOR mm = level.Movers.Entries[m];
                        censusKeys.Add(((ulong)mm.Resource.composite_instance_id.AsUInt32 << 32) | mm.Resource.resource_id.AsUInt32);
                    }
                    bool covered = true;
                    foreach (ulong k in islandKeys)
                    {
                        if (censusKeys.Contains(k)) continue;
                        // Not in the census - verify it moved with the group anyway (a mover can
                        // sit out the census for reasons other than not moving). Unverifiable
                        // mates block the slide: safe, since the re-bake path still works.
                        if (!moverForKey.TryGetValue(k, out int mateIndex) ||
                            !state.RetailTransforms.TryGetValue(k, out System.Numerics.Matrix4x4 matePristine))
                        { covered = false; break; }
                        System.Numerics.Matrix4x4 mateCur = level.Movers.Entries[mateIndex].Transform;
                        float mateRot =
                            Math.Abs(mateCur.M11 - matePristine.M11) + Math.Abs(mateCur.M12 - matePristine.M12) + Math.Abs(mateCur.M13 - matePristine.M13) +
                            Math.Abs(mateCur.M21 - matePristine.M21) + Math.Abs(mateCur.M22 - matePristine.M22) + Math.Abs(mateCur.M23 - matePristine.M23) +
                            Math.Abs(mateCur.M31 - matePristine.M31) + Math.Abs(mateCur.M32 - matePristine.M32) + Math.Abs(mateCur.M33 - matePristine.M33);
                        var mateDelta = new Vector3(mateCur.M41 - matePristine.M41, mateCur.M42 - matePristine.M42, mateCur.M43 - matePristine.M43);
                        if (mateRot > 0.01f || (mateDelta - d).Length() > 0.005f)
                        { covered = false; break; }
                    }
                    if (!covered)
                    {
                        log?.Invoke("    delta translate: island " + islandId + " moved PARTIALLY (" +
                                    censusKeys.Count + " of " + islandKeys.Count + " bound resources) - re-baking instead");
                        continue;
                    }
                }
                int sliceIndex = runtime.InstanceSliceIndices[islandId];
                if (sliceIndex < 0 || sliceIndex >= runtime.Slices.Count)
                    continue;
                RadiosityRuntime.RuntimeDataSlice slice = runtime.Slices[sliceIndex];

                //The island's rect, from any member's pristine MODEL_PARAMS.
                int rx = -1, ry = -1, rw = 0, rh = 0;
                foreach ((int m, Vector3 _) in group.Value)
                {
                    Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[m];
                    ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                    if (!state.RetailModelParams.TryGetValue(key, out byte[] mp) || mp.Length < 16) continue;
                    int w = (int)Math.Round(BitConverter.ToSingle(mp, 0) + 0.5f);
                    int h = (int)Math.Round(BitConverter.ToSingle(mp, 4) + 0.5f);
                    int x = (int)Math.Round(BitConverter.ToSingle(mp, 8));
                    int y = (int)Math.Round(BitConverter.ToSingle(mp, 12));
                    if (w >= 1 && w <= 128 && h >= 1 && h <= 128 && x >= 0 && y >= 0 && x + w <= AtlasSize && y + h <= AtlasSize)
                    { rx = x; ry = y; rw = w; rh = h; break; }
                }
                if (rx < 0)
                    continue;

                //1a. The island's OLD region, from its rect's live clusters - read-only pass.
                var oldClusterPositions = new List<Vector3>();
                Vector3 bMin = new Vector3(float.MaxValue), bMax = new Vector3(float.MinValue);
                for (int y = ry; y < ry + rh; y++)
                    for (int x = rx; x < rx + rw; x++)
                    {
                        int t = y * AtlasSize + x;
                        if (t >= slice.ClusterPositions.Count) continue;
                        Vector4u16 cp = slice.ClusterPositions[t];
                        if (cp.W == 0) continue;
                        var pos = new Vector3(FromHalf(cp.X), FromHalf(cp.Y), FromHalf(cp.Z));
                        oldClusterPositions.Add(pos);
                        bMin = Vector3.Min(bMin, pos);
                        bMax = Vector3.Max(bMax, pos);
                    }
                if (oldClusterPositions.Count == 0)
                    continue;

                //1b. Sanity: every mover's PRISTINE pivot must sit at this island (within a metre
                //of its cluster bounds). Without this, movers whose RetailTransforms entry is a
                //stale identity matrix (single-instance FX families) computed 20m "deltas" and
                //slid two unrelated islands across the level.
                bool pivotsMatch = true;
                foreach ((int m, Vector3 delta) in group.Value)
                {
                    System.Numerics.Matrix4x4 cur = level.Movers.Entries[m].Transform;
                    var pristinePos = new Vector3(cur.M41 - delta.X, cur.M42 - delta.Y, cur.M43 - delta.Z);
                    const float pivotMargin = 1.0f;
                    if (pristinePos.X < bMin.X - pivotMargin || pristinePos.X > bMax.X + pivotMargin ||
                        pristinePos.Y < bMin.Y - pivotMargin || pristinePos.Y > bMax.Y + pivotMargin ||
                        pristinePos.Z < bMin.Z - pivotMargin || pristinePos.Z > bMax.Z + pivotMargin)
                    { pivotsMatch = false; break; }
                }
                if (!pivotsMatch)
                    continue;

                //1c. Slide the clusters.
                for (int y = ry; y < ry + rh; y++)
                    for (int x = rx; x < rx + rw; x++)
                    {
                        int t = y * AtlasSize + x;
                        if (t >= slice.ClusterPositions.Count) continue;
                        Vector4u16 cp = slice.ClusterPositions[t];
                        if (cp.W == 0) continue;
                        var pos = new Vector3(FromHalf(cp.X), FromHalf(cp.Y), FromHalf(cp.Z));
                        Vector3 np = pos + d;
                        slice.ClusterPositions[t] = new Vector4u16 { X = ToHalf(np.X), Y = ToHalf(np.Y), Z = ToHalf(np.Z), W = cp.W };
                    }
                const float margin = 0.15f;
                bMin -= new Vector3(margin); bMax += new Vector3(margin);

                //2. Surface probes: the rect's mangle slots, position-guarded against the
                //dead-texel fallback pointing outside the island.
                var movedSlots = new HashSet<int>();
                int probesMoved = 0;
                for (int y = ry; y < ry + rh; y++)
                    for (int x = rx; x < rx + rw; x++)
                    {
                        int t = y * AtlasSize + x;
                        if (t >= slice.MangleMap.Count) continue;
                        ColourRGBA8 mm = slice.MangleMap[t];
                        int slot = mm.G * ProbeTexWidth + mm.R;
                        if (slot >= slice.SurfaceProbePositions.Count || !movedSlots.Add(slot)) continue;
                        Vector4 sp = slice.SurfaceProbePositions[slot];
                        if (sp.W == 0) continue;
                        var pos = new Vector3(sp.X, sp.Y, sp.Z);
                        if (pos.X < bMin.X || pos.X > bMax.X || pos.Y < bMin.Y || pos.Y > bMax.Y || pos.Z < bMin.Z || pos.Z > bMax.Z)
                            continue;
                        slice.SurfaceProbePositions[slot] = new Vector4(pos + d, sp.W);
                        probesMoved++;
                    }

                //3. Input probes: only the ones texel-coincident with the island's own clusters
                //(retail's are, ~90%) - a bounds test alone would drag the floor's probes along
                //under any furniture.
                int inputMoved = 0;
                for (int i = 0; i < slice.InputProbePositions.Count; i++)
                {
                    Vector4u16 ip = slice.InputProbePositions[i];
                    if (ip.W == 0) continue;
                    var pos = new Vector3(FromHalf(ip.X), FromHalf(ip.Y), FromHalf(ip.Z));
                    if (pos.X < bMin.X || pos.X > bMax.X || pos.Y < bMin.Y || pos.Y > bMax.Y || pos.Z < bMin.Z || pos.Z > bMax.Z)
                        continue;
                    bool coincident = false;
                    foreach (Vector3 cp in oldClusterPositions)
                        if (Vector3.DistanceSquared(cp, pos) < (0.02f + RadiosityTuning.InputProbeLift) * (0.02f + RadiosityTuning.InputProbeLift)) { coincident = true; break; }   // stored input probes are lifted off the texel
                    if (!coincident) continue;
                    Vector3 np = pos + d;
                    slice.InputProbePositions[i] = new Vector4u16 { X = ToHalf(np.X), Y = ToHalf(np.Y), Z = ToHalf(np.Z), W = ip.W };
                    inputMoved++;
                }

                foreach ((int m, Vector3 _) in group.Value)
                    deltaMovers.Remove(m);

                log?.Invoke("    delta translate: island " + islandId + " (slice " + sliceIndex + ", rect " +
                            rw + "x" + rh + "@" + rx + "," + ry + ") slid by (" +
                            d.X.ToString("0.###") + "," + d.Y.ToString("0.###") + "," + d.Z.ToString("0.###") + "): " +
                            oldClusterPositions.Count + " clusters, " + probesMoved + " surface probes, " +
                            inputMoved + " input probes, " + group.Value.Count + " movers keep retail rects");
            }
        }

        /// <summary>
        /// The PROBE-ONLY delta path: light added or moved DYNAMIC content by extending the volume probe field. The
        /// out-of-field delta content is packed into groups by zone (one 128x128 atlas cannot hold a large edit), and each
        /// group is baked, with a donor shell of nearby non-delta geometry, into an appended slice (clusters, input probes,
        /// diets, scatter, surface lights) topped with a volume probe hash at
        /// <see cref="RadiosityTuning.DeltaVolumeProbeCellSize"/>. No mover gets a MODEL_PARAMS rect. The engine relights
        /// only slices an island references, so each slice is scheduled through minted island ids, one per zone (in-range
        /// ids no retail row uses); their rows are queued in <see cref="DeltaBakeState.PendingScheduleRows"/> for the
        /// patcher to append after the lightmap delta. Movers whose pivot lies INSIDE the retail volume field are skipped.
        /// MUST run BEFORE <see cref="DynamicRadiosityConverter"/>: geometry collection excludes dynamic-class movers.
        /// </summary>
        internal static int AppendProbeOnlySlice(
            Level level,
            DeltaBakeState state,
            HashSet<int> deltaMovers,
            Action<string> log = null)
        {
            if (deltaMovers == null || deltaMovers.Count == 0)
                return 0;
            RadiosityRuntime runtime = level.RadiosityRuntime
                ?? throw new InvalidOperationException("No runtime to append to.");

            // Out-of-field census: pivots outside every retail volume hash AABB, with one
            // cell of margin so content at a hash edge keeps using retail's field.
            var outOfField = new HashSet<int>();
            foreach (int mi in deltaMovers)
            {
                if (mi < 0 || mi >= level.Movers.Entries.Count)
                    continue;
                Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[mi];
                var p = new Vector3(mv.Transform.M41, mv.Transform.M42, mv.Transform.M43);
                bool inside = false;
                foreach (RadiosityRuntime.RuntimeDataSlice s in runtime.Slices)
                {
                    RadiosityRuntime.VolumeProbeHash h = s.VolumeProbeHash;
                    if (h == null || h.Dims.X == 0)
                        continue;
                    const float margin = 2.0f;
                    if (p.X >= h.AabbMin.X - margin && p.X <= h.AabbMax.X + margin &&
                        p.Y >= h.AabbMin.Y - margin && p.Y <= h.AabbMax.Y + margin &&
                        p.Z >= h.AabbMin.Z - margin && p.Z <= h.AabbMax.Z + margin)
                    { inside = true; break; }
                }
                if (!inside)
                    outOfField.Add(mi);
            }
            if (outOfField.Count == 0)
            {
                log?.Invoke("Radiosity probe slice: every delta mover sits inside the retail volume field - nothing to extend");
                return 0;
            }

            level.Resources.RefreshWriteList();

            // No static-composites whitelist (see AppendDeltaSlices): added content is never on it.
            RadiosityGeometry geometry = RadiosityGeometry.CollectFromLevel(level, staticCompositesOnly: false, splitTransparentIslands: false, log);
            if (geometry.TriangleCount == 0)
                return 0;
            geometry.Build(log);
            BuildCollisionOccluders(level, geometry, log);

            var deltaInstances = new List<RadiosityGeometry.Instance>();
            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
                if (instance.Movers.Any(outOfField.Contains))
                    deltaInstances.Add(instance);

            // Junk filter: zone-less template/particle movers sprawl outside playable space (H1: a ~3.7k-mover "zone 0"
            // bucket packed two full slices whose hashes blew the engine's ~6.5k item budget). They light nothing playable.
            int junk = 0;
            for (int i = deltaInstances.Count - 1; i >= 0; i--)
            {
                bool zoned = false;
                foreach (int mi in deltaInstances[i].Movers)
                    if (level.Movers.Entries[mi].PrimaryZoneID != CATHODE.Scripting.ShortGuid.Invalid)
                    { zoned = true; break; }
                if (!zoned) { deltaInstances.RemoveAt(i); junk++; }
            }
            if (junk > 0)
                log?.Invoke("Radiosity probe slices: " + junk + " zone-less instances dropped (template/particle junk)");

            // Exterior-shell filter: hull/skybox meshes hang far outside playable space (RadiosityTuning.DeltaMaxInstanceSpan).
            int shell = 0;
            for (int i = deltaInstances.Count - 1; i >= 0; i--)
            {
                Vector3 span = deltaInstances[i].BoundsMax - deltaInstances[i].BoundsMin;
                if (Math.Max(span.X, Math.Max(span.Y, span.Z)) > RadiosityTuning.DeltaMaxInstanceSpan)
                { deltaInstances.RemoveAt(i); shell++; }
            }
            if (shell > 0)
                log?.Invoke("Radiosity probe slices: " + shell + " oversized instances dropped (exterior shell, span > " +
                            RadiosityTuning.DeltaMaxInstanceSpan.ToString("0") + "m)");

            if (deltaInstances.Count == 0)
            {
                log?.Invoke("Radiosity probe slice: no bakeable geometry among the out-of-field movers");
                return 0;
            }

            // Every surface is sampled on one world-space grid, so probe spacing is uniform (retail's lattice look) and no
            // probe is a dilation clone; the rect is just a slot allocation. Rasterising the lightmap UVs inherited the
            // charts' packing (F5: per-2m-cell density p10 0.38x retail, 21.5% of probes stacked dilation clones).
            float spacing = RadiosityTuning.DeltaProbeSpacing;
            long totalSamples = 0;
            foreach (RadiosityGeometry.Instance instance in deltaInstances)
            {
                instance.UniformSamples = GridSampleInstance(geometry, instance, spacing);
                int n = Math.Max(1, instance.UniformSamples.Count);
                int h2 = Math.Min(AtlasSize, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n))));
                int w2 = Math.Min(AtlasSize, Math.Max(1, (n + h2 - 1) / h2));
                instance.AtlasWidth = w2;
                instance.AtlasHeight = h2;
                totalSamples += instance.UniformSamples.Count;
            }
            log?.Invoke("Radiosity probe slices: uniform grid placement - " + totalSamples +
                        " samples at " + spacing.ToString("0.0#") + "m over " + deltaInstances.Count + " islands");

            // Partition into as many slices as the content needs, leaving atlas headroom for each group's donor shell.
            // Retail keeps a zone's rooms wholly in one slice (with their probes, scatter and door transfers), so whole
            // zones are packed first-fit-decreasing; only a zone too big for one slice is split spatially.
            var groups = new List<List<RadiosityGeometry.Instance>>();
            int texelCap = RadiosityTuning.MaxTexelsPerSlice;
            int Tex(List<RadiosityGeometry.Instance> l) { int t = 0; foreach (var i2 in l) t += i2.AtlasWidth * i2.AtlasHeight; return t; }
            var byZone = new Dictionary<uint, List<RadiosityGeometry.Instance>>();
            foreach (RadiosityGeometry.Instance inst in deltaInstances)
            {
                uint zone = 0;
                foreach (int mi in inst.Movers)
                {
                    var z = level.Movers.Entries[mi].PrimaryZoneID;
                    if (z != CATHODE.Scripting.ShortGuid.Invalid) { zone = z.AsUInt32; break; }
                }
                if (!byZone.TryGetValue(zone, out var zl)) byZone[zone] = zl = new List<RadiosityGeometry.Instance>();
                zl.Add(inst);
            }
            foreach (var zoneEntry in byZone.OrderByDescending(kv => Tex(kv.Value)))
            {
                List<RadiosityGeometry.Instance> zoneGroup = zoneEntry.Value;
                if (Tex(zoneGroup) > texelCap)
                {
                    SplitSpatially(zoneGroup, groups, log);
                    log?.Invoke("      zone " + zoneEntry.Key.ToString("X8") + " (" + Tex(zoneGroup) + " texels) -> spatially split");
                    continue;
                }
                List<RadiosityGeometry.Instance> target = null;
                foreach (var g in groups)
                    if (Tex(g) + Tex(zoneGroup) <= texelCap) { target = g; break; }
                if (target == null) { target = new List<RadiosityGeometry.Instance>(); groups.Add(target); }
                target.AddRange(zoneGroup);
                log?.Invoke("      zone " + zoneEntry.Key.ToString("X8") + " (" + Tex(zoneGroup) + " texels, " +
                            zoneGroup.Count + " islands) -> slice group " + groups.IndexOf(target));
            }
            log?.Invoke("Radiosity probe slices: zone packing - " + byZone.Count + " zones -> " + groups.Count + " slices (budget " + texelCap + ")");

            var deltaSet = new HashSet<RadiosityGeometry.Instance>(deltaInstances);
            int slicesBaked = 0, donorsTotal = 0;
            RadiosityRuntime.VolumeProbeHash lastHash = null;
            var doorBakes = new List<SliceBake>();
            foreach (List<RadiosityGeometry.Instance> group in groups)
            {
                // Donor shell per group: nearby geometry that is NOT part of the delta at all
                // (unmoved retail stays lightmapped and joins here cluster-only, so the
                // group's probes gather the surroundings' bounce).
                var bakeInstances = new List<RadiosityGeometry.Instance>(group);
                float reach = RadiosityTuning.DeltaDonorShellRadius;
                float BoxDistance(RadiosityGeometry.Instance inst)
                {
                    float best = float.MaxValue;
                    foreach (RadiosityGeometry.Instance d in group)
                    {
                        float bx = Math.Max(0, Math.Max(inst.BoundsMin.X - d.BoundsMax.X, d.BoundsMin.X - inst.BoundsMax.X));
                        float by = Math.Max(0, Math.Max(inst.BoundsMin.Y - d.BoundsMax.Y, d.BoundsMin.Y - inst.BoundsMax.Y));
                        float bz = Math.Max(0, Math.Max(inst.BoundsMin.Z - d.BoundsMax.Z, d.BoundsMin.Z - inst.BoundsMax.Z));
                        float dd = bx * bx + by * by + bz * bz;
                        if (dd < best) best = dd;
                    }
                    return (float)Math.Sqrt(best);
                }
                var donors = new List<(RadiosityGeometry.Instance inst, float dist)>();
                foreach (RadiosityGeometry.Instance inst in geometry.Instances)
                {
                    if (deltaSet.Contains(inst)) continue;
                    float d = BoxDistance(inst);
                    if (d <= reach) donors.Add((inst, d));
                }
                donors.Sort((a, b) => a.dist.CompareTo(b.dist));
                int budget = RadiosityTuning.DeltaDonorTexelBudget;
                int groupTexels = 0;
                foreach (RadiosityGeometry.Instance inst in group) groupTexels += inst.AtlasWidth * inst.AtlasHeight;
                budget = Math.Min(budget, Math.Max(0, RadiosityTuning.MaxTexelsPerRetailSlice - groupTexels));
                int maxDim = RadiosityTuning.DeltaDonorMaxRectDim;
                int taken = 0, spent = 0, dupes = 0;
                var seenFootprints = new HashSet<(int, int, int, int, int, int)>();
                (int, int, int, int, int, int) Footprint(RadiosityGeometry.Instance inst) =>
                    ((int)Math.Round(inst.BoundsMin.X * 20), (int)Math.Round(inst.BoundsMin.Y * 20), (int)Math.Round(inst.BoundsMin.Z * 20),
                     (int)Math.Round(inst.BoundsMax.X * 20), (int)Math.Round(inst.BoundsMax.Y * 20), (int)Math.Round(inst.BoundsMax.Z * 20));
                foreach ((RadiosityGeometry.Instance inst, float dist) in donors)
                {
                    if (!seenFootprints.Add(Footprint(inst))) { dupes++; continue; }
                    RadiosityAtlas.RectSizeForBounds(inst.SurfaceArea, inst.UvCoverage, out int w, out int h, inst.UvAspect);
                    if (w > maxDim || h > maxDim)
                    {
                        float scale = Math.Min((float)maxDim / w, (float)maxDim / h);
                        w = Math.Max(2, (int)Math.Round(w * scale));
                        h = Math.Max(2, (int)Math.Round(h * scale));
                    }
                    if (spent + w * h > budget) continue;
                    inst.DonorOnly = true;
                    inst.AtlasWidth = w;
                    inst.AtlasHeight = h;
                    bakeInstances.Add(inst);
                    spent += w * h;
                    taken++;
                }
                donorsTotal += taken;

                var probeOnlySlices = new List<List<RadiosityGeometry.Instance>> { bakeInstances };
                AllocateAtlases(probeOnlySlices, log);

                Dictionary<int, float> emissiveAreas = ComputeEmissiveAreas(geometry);
                RetailLightPriors lightPriors = CalibrateWeightCoefficient(level, emissiveAreas, log);

                SliceBake bake = BakeSlice(level, geometry, bakeInstances, runtime.Slices.Count, BakeSliceOptions.DeltaProbeOnly, emissiveAreas, lightPriors, log);
                log?.Invoke("  slice " + runtime.Slices.Count + " timing: " + bake.Timing);

                AddUnbakedEmitterLights(level, geometry, new[] { bake }, BakeSliceOptions.DeltaProbeOnly.LostEmittersMustBeVisible, lightPriors, log);
                ExpandSiblingChains(new[] { bake }, lightPriors, log);

                if (bake?.Slice?.SurfaceLights?.Lights != null)
                {
                    List<RadiosityRuntime.RuntimeSurfaceLights.Light> lights = bake.Slice.SurfaceLights.Lights;
                    for (int i = 0; i < lights.Count; i++)
                    {
                        RadiosityRuntime.RuntimeSurfaceLights.Light l = lights[i];
                        l.Weight = l.Weight == 0 ? (byte)0 : (byte)Math.Max(1, Math.Min(191, (int)Math.Round(l.Weight * RadiosityTuning.DeltaLightWeightScale)));
                        lights[i] = l;
                    }
                }

                FoldVisPaletteIntoRuntime(runtime, bake, log);

                int newSliceIndex = runtime.Slices.Count;
                runtime.Slices.Add(bake.Slice);
                AppendDeltaFixups(runtime, bake, newSliceIndex, geometry, log);
                CalibrateDeltaEnergy(runtime, bake, newSliceIndex, state, log);

                // Schedule the slice. The engine only relights a slice that some island's InstanceSliceIndices entry
                // points at (H3: an appended slice with none rendered its content completely unlit), and the scheduling
                // walk runs over INSTANCE MAP rows, not the raw island table (H4: a minted id with no row did nothing).
                // Added content has no islands, so ids are minted: one PER ZONE (one per slice lit only some zones), each
                // row bound to the first mover found in that zone (the walk seeds the region around the bound mover; H20:
                // more seeds measured worse). The rows are added AFTER the lightmap delta's real rows, because rendering
                // follows a resource's first row (H16: a seed row added first blackened a doorway).
                var zoneSeeds = new Dictionary<uint, List<Resources.Resource>>();
                foreach (RadiosityGeometry.Instance inst in group)
                {
                    uint z = 0;
                    foreach (int mi in inst.Movers)
                    {
                        var zid = level.Movers.Entries[mi].PrimaryZoneID;
                        if (zid != CATHODE.Scripting.ShortGuid.Invalid) { z = zid.AsUInt32; break; }
                    }
                    if (!zoneSeeds.TryGetValue(z, out var seeds))
                        zoneSeeds[z] = seeds = new List<Resources.Resource>();
                    foreach (int mi in inst.Movers)
                    {
                        // Seeds must STAND IN the slice's content: instancing emits movers whose positions never
                        // inherited the delta offset, and rows bound to them 250 m away made per-zone lighting a lottery.
                        if (!outOfField.Contains(mi)) continue;
                        Resources.Resource r = level.Movers.Entries[mi].Resource;
                        if (r != null) seeds.Add(r);
                    }
                }
                var zoneSeed = new Dictionary<uint, Resources.Resource>();
                foreach (var kv in zoneSeeds)
                    if (kv.Value.Count > 0) zoneSeed[kv.Key] = kv.Value[0];

                // GAP IDS ONLY: InstanceSliceIndices entries past retail's count are never visited (see
                // AppendDeltaSlices; H4/H9's appended ids did nothing or poisoned the walk). Retail leaves in-range ids no
                // map row uses; take those. With no gap left, a zone stays unscheduled.
                var mintUsed = new HashSet<int>();
                if (level.RadiosityInstanceMap?.Entries != null)
                    foreach (RadiosityInstanceMap.Entry e in level.RadiosityInstanceMap.Entries)
                        mintUsed.Add(e.lightmap_transform);
                var mintGaps = new Queue<int>();
                for (int id = 0; id < runtime.InstanceSliceIndices.Count && mintGaps.Count < zoneSeed.Count; id++)
                    if (!mintUsed.Contains(id)) mintGaps.Enqueue(id);

                int mintedCount = 0;
                foreach (Resources.Resource seed in zoneSeed.Values)
                {
                    if (mintGaps.Count == 0) break;
                    int mintedIsland = mintGaps.Dequeue();
                    runtime.InstanceSliceIndices[mintedIsland] = newSliceIndex;
                    state.PendingScheduleRows.Add((mintedIsland, seed));
                    mintedCount++;
                }
                log?.Invoke("    probe slice " + newSliceIndex + ": scheduled via " + mintedCount +
                            " islands (per zone, rows deferred until after the lightmap delta)" +
                            (mintedCount < zoneSeed.Count ? " - ID POOL EXHAUSTED for " + (zoneSeed.Count - mintedCount) + " zones" : ""));

                doorBakes.Add(bake);
                lastHash = bake.Slice.VolumeProbeHash;
                slicesBaked++;
            }

            // The engine's relight propagation runs on the per-slice DOOR tables (stripping retail slice 1's doors
            // reproduced the appended-slice black rooms), so probe slices need the same door build as the full bake.
            if (doorBakes.Count > 0)
            {
                int doorTransfers = BuildDoors(level, geometry, doorBakes.ToArray(), log);
                log?.Invoke("Radiosity probe slices: " + doorTransfers + " door transfers built");
            }

            log?.Invoke("Radiosity probe slices: " + deltaInstances.Count + " islands (" + outOfField.Count +
                        " out-of-field movers) across " + slicesBaked + " appended slices + " + donorsTotal +
                        " donors; " + (lastHash != null && lastHash.Dims.X > 0 ? "last hash " + lastHash.Dims.X + "x" +
                        lastHash.Dims.Y + "x" + lastHash.Dims.Z + " at " +
                        BakeSliceOptions.DeltaProbeOnly.VolumeProbeCellSize.ToString("0.0#") + "m cells" : "no hash") +
                        "; no mover rects written, scheduling rows deferred");
            return deltaInstances.Count;
        }

        /// <summary>Fold an appended slice bake's visibility grids into the runtime's shared 256-entry palette: match by
        /// content, take over zero-padding entries from the end for new grids, and snap to the nearest entry once the
        /// slots are full.</summary>
        private static void FoldVisPaletteIntoRuntime(RadiosityRuntime runtime, SliceBake bake, Action<string> log = null)
        {
            if (bake.VisFaceGrids != null && bake.VisFaceGrids.Count > 0 && runtime.VolumeProbeVisPalette != null)
            {
                List<RadiosityRuntime.VolumeProbeVisSlice> palette = runtime.VolumeProbeVisPalette;
                var byContent = new Dictionary<string, int>();
                for (int i = 0; i < palette.Count; i++)
                    if (palette[i]?.Grid != null && !byContent.ContainsKey(Convert.ToBase64String(palette[i].Grid)))
                        byContent[Convert.ToBase64String(palette[i].Grid)] = i;
                int nextFree = palette.Count - 1;
                bool IsZero(byte[] g) { foreach (byte b in g) if (b != 0) return false; return true; }

                // Snapping needs the raw grids; build the view once rather than per grid.
                var raw = new List<byte[]>(palette.Count);
                foreach (RadiosityRuntime.VolumeProbeVisSlice e in palette)
                    raw.Add(e?.Grid ?? new byte[VisFaceCells]);

                int matched = 0, placed = 0, snapped = 0;
                for (int g = 0; g < bake.VisFaceGrids.Count; g++)
                {
                    byte[] grid = bake.VisFaceGrids[g];
                    string key = Convert.ToBase64String(grid);
                    if (byContent.TryGetValue(key, out int existing)) { bake.VisFaceIndices[g] = (byte)existing; matched++; continue; }
                    while (nextFree > 0 && (palette[nextFree]?.Grid == null || !IsZero(palette[nextFree].Grid))) nextFree--;
                    if (nextFree > 0)
                    {
                        palette[nextFree] = new RadiosityRuntime.VolumeProbeVisSlice { Grid = grid };
                        raw[nextFree] = grid;
                        byContent[key] = nextFree;
                        bake.VisFaceIndices[g] = (byte)nextFree;
                        nextFree--;
                        placed++;
                        continue;
                    }
                    // The slots are full, which is the NORMAL case (retail fills all 256 on a shipped level). Snap to
                    // the nearest real grid: entry 0, which this used to take, pinned CM3's delta faces to one flat dark grid.
                    bake.VisFaceIndices[g] = (byte)ClosestPaletteEntry(raw, grid);
                    snapped++;
                }
                log?.Invoke("    delta vis palette: " + matched + " matched, " + placed +
                            " took free slots, " + snapped + " snapped to nearest");
                ApplyVisPaletteIndices(new[] { bake });
            }
        }

        /// <summary>
        /// Per-probe energy calibration for an appended delta slice (lightmap and probe-only paths). The weight byte is
        /// exponent-domain, so a probe's delivered gather tracks its exp mass sum(2^(w/32)), not its byte sum, and diets
        /// differ too much between islands for one global shift (it fit CM3 at 0.97 but put CM5's shelving at 1.14). Each
        /// delta probe's exp mass is matched to the MEDIAN of the retail surface probes near it, or shifted by
        /// <see cref="RadiosityTuning.DeltaInfluenceWeightBias"/> where none are near (void content, new rooms).
        /// </summary>
        private static void CalibrateDeltaEnergy(RadiosityRuntime runtime, SliceBake bake, int newSliceIndex, DeltaBakeState state, Action<string> log)
        {
            if (bake?.Slice?.SurfaceProbeWeights == null || bake.Slice.SurfaceProbePositions == null)
                return;

            const float matchRadius = 4.0f;
            // The 2^(w/32) form predates the shader decode, which doubles the gain every 11.28 bytes
            // (see ReadInfluenceWeight): under it a 2x mass mismatch asks for +32 bytes,
            // which the engine reads as x7.2 - the delta path's recurring overshoot. Moving both
            // the mass and the shift to the exact curve was built but never validated on a delta
            // fixture.
            double ExpOf(byte w) => Math.Pow(2.0, w / 32.0);

            // Retail probe exp masses, gridded for the neighbourhood query.
            var retailGrid = new Dictionary<(int, int, int), List<(Vector3 pos, double mass)>>();
            (int, int, int) Cell(Vector3 v) =>
                ((int)Math.Floor(v.X / matchRadius), (int)Math.Floor(v.Y / matchRadius), (int)Math.Floor(v.Z / matchRadius));
            for (int s = 0; s < newSliceIndex; s++)
            {
                RadiosityRuntime.RuntimeDataSlice retail = runtime.Slices[s];
                for (int slot = 0; slot < retail.SurfaceProbePositions.Count; slot++)
                {
                    Vector4 p = retail.SurfaceProbePositions[slot];
                    if (p.W == 0) continue;
                    double mass = 0;
                    for (int k = 0; k < InfluencesPerProbe; k++)
                    {
                        byte w = ReadInfluenceWeight(retail, slot * InfluencesPerProbe + k);
                        if (w != 0) mass += ExpOf(w);
                    }
                    if (mass <= 0) continue;
                    var pos = new Vector3(p.X, p.Y, p.Z);
                    (int, int, int) key = Cell(pos);
                    if (!retailGrid.TryGetValue(key, out List<(Vector3, double)> list))
                        retailGrid[key] = list = new List<(Vector3, double)>();
                    list.Add((pos, mass));
                }
            }

            // Our probes' fixups grouped by probe slot, so the shift covers both weight sets.
            var fixupsBySlot = new Dictionary<int, List<int>>();
            int nbOffset = runtime.SliceNeighbourArrayOffsets[newSliceIndex];
            for (int n = 0; n < runtime.SliceNeighbourCounts[newSliceIndex]; n++)
            {
                RadiosityRuntime.FixupRange range = runtime.FlattenedFixupRanges[nbOffset + n];
                for (int i = range.First; i < range.First + range.Num; i++)
                {
                    int slot = runtime.InfluenceFixups[i].WeightTexOffset / InfluencesPerProbe;
                    if (!fixupsBySlot.TryGetValue(slot, out List<int> list))
                        fixupsBySlot[slot] = list = new List<int>();
                    list.Add(i);
                }
            }

            int matched2 = 0, fellBack = 0;
            var shifts = new List<int>();
            for (int slot = 0; slot < bake.Slice.SurfaceProbePositions.Count; slot++)
            {
                Vector4 p = bake.Slice.SurfaceProbePositions[slot];
                if (p.W == 0) continue;
                // The calibration offset maps a rigidly-moved or copied group back onto its ORIGINAL location, so its
                // probes match the retail exp masses of the room they came from (a shifted level has none nearby).
                var pos = new Vector3(p.X, p.Y, p.Z) + state.CalibrationOffset;

                // Our probe's current exp mass, fixup overlay included.
                var slotWeights = new byte[InfluencesPerProbe];
                for (int k = 0; k < InfluencesPerProbe; k++)
                    slotWeights[k] = ReadInfluenceWeight(bake.Slice, slot * InfluencesPerProbe + k);
                if (fixupsBySlot.TryGetValue(slot, out List<int> myFixups))
                    foreach (int i in myFixups)
                        slotWeights[runtime.InfluenceFixups[i].WeightTexOffset % InfluencesPerProbe] = runtime.InfluenceFixups[i].Weight;
                double ourMass = 0;
                foreach (byte w in slotWeights)
                    if (w != 0) ourMass += ExpOf(w);
                if (ourMass <= 0) continue;

                // Median retail exp mass within reach.
                var near = new List<double>();
                (int cx, int cy, int cz) = Cell(pos);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!retailGrid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<(Vector3 pos, double mass)> list))
                                continue;
                            foreach ((Vector3 rp, double mass) in list)
                                if (Vector3.DistanceSquared(rp, pos) < matchRadius * matchRadius)
                                    near.Add(mass);
                        }

                int shift;
                if (near.Count >= 4)
                {
                    near.Sort();
                    double target = near[near.Count / 2];
                    shift = (int)Math.Round(32.0 * Math.Log(target / ourMass, 2.0));
                    shift = Math.Max(-48, Math.Min(48, shift));
                    matched2++;
                }
                else
                {
                    shift = RadiosityTuning.DeltaInfluenceWeightBias;
                    fellBack++;
                }
                if (shift == 0) continue;
                shifts.Add(shift);

                byte Shifted(byte w) => w == 0 ? (byte)0 : (byte)Math.Max(1, Math.Min(254, w + shift));
                for (int k = 0; k < InfluencesPerProbe; k++)
                {
                    byte w = ReadInfluenceWeight(bake.Slice, slot * InfluencesPerProbe + k);
                    if (w != 0)
                        WriteInfluenceWeight(bake.Slice, slot * InfluencesPerProbe + k, Shifted(w));
                }
                if (myFixups != null)
                    foreach (int i in myFixups)
                    {
                        RadiosityRuntime.RuntimeInfluenceFixup fx = runtime.InfluenceFixups[i];
                        fx.Weight = Shifted(fx.Weight);
                        runtime.InfluenceFixups[i] = fx;
                    }
            }
            shifts.Sort();
            log?.Invoke("    delta calibration: " + matched2 + " probes matched to local retail exp mass, " +
                        fellBack + " fell back to bias " + RadiosityTuning.DeltaInfluenceWeightBias +
                        (shifts.Count > 0 ? "  shift p10/50/90 = " + shifts[shifts.Count / 10] + "/" +
                         shifts[shifts.Count / 2] + "/" + shifts[shifts.Count * 9 / 10] : ""));
        }

        /// <summary>
        /// The delta bake's lightmap half: bake the movers an edit ADDED or MOVED into one new
        /// slice appended to the kept retail runtime, so new geometry lights itself instead of
        /// rendering black (architecture) or fullbright (props). Returns the islands grafted
        /// plus the islands baked into the new slice.
        /// </summary>
        /// <param name="allMovers">Every mover of the WHOLE lightmap delta when the patcher appends it in more than one
        /// chunk; null for a single chunk (<paramref name="deltaMovers"/> stands in). Each call only sees its own chunk, so
        /// without it another chunk's delta geometry scores as donor material.</param>
        internal static int AppendDeltaSlices(
            Level level,
            DeltaBakeState state,
            HashSet<int> deltaMovers,
            HashSet<int> allMovers = null,
            Action<string> log = null)
        {
            if (deltaMovers == null || deltaMovers.Count == 0)
                return 0;
            RadiosityRuntime runtime = level.RadiosityRuntime
                ?? throw new InvalidOperationException("No runtime to append to.");

            // Translation-only moves keep their RETAIL island and get its data slid over: re-baking a moved island into an
            // appended slice fed its probes through cross-slice fixups alone, which saturate at about half a native diet
            // (CM9's server rack: 0.38x retail, 0.53x with every slot at byte 255). Their movers leave the delta census, so
            // the patcher restores their retail MODEL_PARAMS and the instance map keeps their rows.
            TranslateMovedIslands(level, runtime, state, deltaMovers, log);
            if (deltaMovers.Count == 0)
                return 0;

            // Instancing appended resources for the new content; without a refresh they resolve
            // to write index -1 and geometry collection drops every new mover.
            level.Resources.RefreshWriteList();

            // No static-composites whitelist: it is derived from what RETAIL lightmapped, so a freshly added composite is
            // never on it (it silently skipped every mover of a newly placed room). The census and IsBakeable still apply.
            RadiosityGeometry geometry = RadiosityGeometry.CollectFromLevel(level, staticCompositesOnly: false, splitTransparentIslands: false, log);
            if (geometry.TriangleCount == 0)
                return 0;
            geometry.Build(log);
            BuildCollisionOccluders(level, geometry, log);

            log?.Invoke("    delta geometry: " + geometry.Instances.Count + " instances, skipped=" + geometry.MoversSkipped + " (noResource=" + geometry.SkippedNoResource + ")");
            var inInstances = new HashSet<int>();
            foreach (RadiosityGeometry.Instance inst in geometry.Instances)
                foreach (int mv in inst.Movers)
                    if (deltaMovers.Contains(mv)) inInstances.Add(mv);
            log?.Invoke("    delta coverage: " + inInstances.Count + "/" + deltaMovers.Count + " delta movers present in geometry");
            int missingShown = 0;
            foreach (int mv in deltaMovers)
            {
                if (inInstances.Contains(mv) || missingShown >= 6) continue;
                Movers.MOVER_DESCRIPTOR mm = level.Movers.Entries[mv];
                string mtype; try { mtype = mm.GetRenderableType().ToString(); } catch { mtype = "?"; }
                log?.Invoke("       missing mover " + mv + ": type=" + mtype + " els=" + (mm.RenderableElements?.Count ?? -1) +
                            " res=" + (mm.Resource != null) + " wIdx=" + level.Resources.GetWriteIndex(mm.Resource) +
                            " dyn=" + RadiosityGeometry.RequiresDynamicRadiosity(mm) + " stationary=" + (mm.Flags?.Stationary.ToString() ?? "?"));
                missingShown++;
            }
            var deltaInstances = new List<RadiosityGeometry.Instance>();
            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
                if (instance.Movers.Any(deltaMovers.Contains))
                    deltaInstances.Add(instance);
            if (deltaInstances.Count == 0)
            {
                log?.Invoke("Radiosity delta: no bakeable geometry among the delta movers");
                return 0;
            }

            // GRAFT retail-bound islands in place into their own retail slices first (GraftDeltaIslands): retail's field
            // relights at parity where anything we bake delivers about half. The rest takes the appended-slice path.
            int graftedIslands = GraftDeltaIslands(level, runtime, geometry, deltaInstances, state, deltaMovers, log);
            if (deltaInstances.Count == 0)
            {
                log?.Invoke("Radiosity delta: " + graftedIslands + " islands grafted into their retail slices, nothing left to append");
                return graftedIslands;
            }

            foreach (RadiosityGeometry.Instance instance in deltaInstances)
            {
                RadiosityAtlas.RectSizeForBounds(instance.SurfaceArea, instance.UvCoverage, out int w, out int h, instance.UvAspect);

                // A MOVED island's pristine MODEL_PARAMS carry retail's own rect size for this
                // exact geometry - use it as a floor. The formula underquotes complex furniture
                // (CM7's moved shelving got 14x14 against retail's 18x16, a third fewer probes).
                foreach (int moverIndex in instance.Movers)
                {
                    Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                    if (mover.Resource == null)
                        continue;
                    ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                    if (!state.RetailModelParams.TryGetValue(key, out byte[] pristine) || pristine.Length < 16)
                        continue;
                    int rw = (int)Math.Round(BitConverter.ToSingle(pristine, 0) + 0.5f);
                    int rh = (int)Math.Round(BitConverter.ToSingle(pristine, 4) + 0.5f);
                    if (rw >= 1 && rw <= 128 && rh >= 1 && rh <= 128)
                    {
                        w = Math.Max(w, rw);
                        h = Math.Max(h, rh);
                    }
                }
                instance.AtlasWidth = w;
                instance.AtlasHeight = h;
            }
            foreach (RadiosityGeometry.Instance instance in deltaInstances.OrderByDescending(i => i.Movers.Count).Take(3))
            {
                log?.Invoke("    delta island: " + instance.Movers.Count + " movers  area " +
                            instance.SurfaceArea.ToString("0.0") + " m2  uvCov " + instance.UvCoverage.ToString("0.00") +
                            "  tris " + instance.Triangles.Count + "  rect " + instance.AtlasWidth + "x" + instance.AtlasHeight);
                for (int m = 0; m < instance.Movers.Count && m < 12; m++)
                {
                    Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[instance.Movers[m]];
                    string mat = mv.RenderableElements != null && mv.RenderableElements.Count > 0
                        ? (mv.RenderableElements[0]?.Material?.Name ?? "?") : "(none)";
                    log?.Invoke("       mover " + instance.Movers[m] + "  " + mv.RenderableElements?.Count + " els  " +
                                instance.MoverAreas[m].ToString("0.00") + " m2  " + mat);
                }
            }
            // Donor shell: everything the delta probes gather bounce from must live IN this slice - influences cannot
            // cross slices except through fixups, which saturate at about half a native diet (see above). The surrounding
            // retail islands are baked in as cluster-only donors: never written to (no MODEL_PARAMS, no instance-map
            // rows), only lit surfaces for the delta islands' diets, scatter and light injection.
            var bakeInstances = new List<RadiosityGeometry.Instance>(deltaInstances);
            var deltaSet = new HashSet<RadiosityGeometry.Instance>(deltaInstances);
            float reach = RadiosityTuning.DeltaDonorShellRadius;

            // A donor must be geometry OUTSIDE the delta entirely. This call only knows its own CHUNK's movers; without the
            // level-wide set another chunk's delta geometry scores as a donor, which is live for light injection (on CM3 the
            // cam9 locker door's lights landed on its donor copy in another slice and lit nothing) and wastes the budget.
            HashSet<int> excluded = allMovers ?? deltaMovers;

            // Distance to the NEAREST delta island, not to their union box: delta islands can
            // be instances of one composite in different rooms across the level, and the
            // union box scores everything in the dead space between them as distance zero.
            float BoxDistance(RadiosityGeometry.Instance inst)
            {
                float best = float.MaxValue;
                foreach (RadiosityGeometry.Instance d in deltaInstances)
                {
                    float bx = Math.Max(0, Math.Max(inst.BoundsMin.X - d.BoundsMax.X, d.BoundsMin.X - inst.BoundsMax.X));
                    float by = Math.Max(0, Math.Max(inst.BoundsMin.Y - d.BoundsMax.Y, d.BoundsMin.Y - inst.BoundsMax.Y));
                    float bz = Math.Max(0, Math.Max(inst.BoundsMin.Z - d.BoundsMax.Z, d.BoundsMin.Z - inst.BoundsMax.Z));
                    float dd = bx * bx + by * by + bz * bz;
                    if (dd < best) best = dd;
                }
                return (float)Math.Sqrt(best);
            }

            var donors = new List<(RadiosityGeometry.Instance inst, float dist)>();
            int selfDelta = 0;
            foreach (RadiosityGeometry.Instance inst in geometry.Instances)
            {
                if (deltaSet.Contains(inst)) continue;
                // Another chunk's delta geometry is not a donor.
                bool isDelta = false;
                foreach (int mv in inst.Movers)
                    if (excluded.Contains(mv)) { isDelta = true; break; }
                if (isDelta) { selfDelta++; continue; }
                float d = BoxDistance(inst);
                if (d <= reach) donors.Add((inst, d));
            }
            donors.Sort((a, b) => a.dist.CompareTo(b.dist));
            if (selfDelta > 0)
                log?.Invoke("    delta donors: " + selfDelta + " other-chunk delta islands excluded from the donor pool");

            int budget = RadiosityTuning.DeltaDonorTexelBudget;
            // Clamp to the atlas space the group's OWN islands will not use: spending the full budget made the packer drop
            // donors by AREA, discarding the small NEAR ones (618 on one bake). Selecting to fit keeps the nearest (unlike
            // H62, which reserved donor space in the PACKER and shrank 2,541 islands).
            int groupTexels = 0;
            foreach (RadiosityGeometry.Instance inst in deltaInstances)
                groupTexels += inst.AtlasWidth * inst.AtlasHeight;
            budget = Math.Min(budget, Math.Max(0, RadiosityTuning.MaxTexelsPerRetailSlice - groupTexels));
            log?.Invoke("    delta donor budget: group " + groupTexels + " texels -> " + budget + " for donors");
            int maxDim = RadiosityTuning.DeltaDonorMaxRectDim;
            int taken = 0, spent = 0, dupes = 0;
            // One donor per footprint: CM9 ships stacks of coincident duplicate composite instances, whose texels share
            // positions, so the input-probe Poisson pass thins all copies to one object's worth (one run spent 8k texels
            // on stacks and got 128 input probes).
            var seenFootprints = new HashSet<(int, int, int, int, int, int)>();
            (int, int, int, int, int, int) Footprint(RadiosityGeometry.Instance inst) =>
                ((int)Math.Round(inst.BoundsMin.X * 20), (int)Math.Round(inst.BoundsMin.Y * 20), (int)Math.Round(inst.BoundsMin.Z * 20),
                 (int)Math.Round(inst.BoundsMax.X * 20), (int)Math.Round(inst.BoundsMax.Y * 20), (int)Math.Round(inst.BoundsMax.Z * 20));
            foreach ((RadiosityGeometry.Instance inst, float dist) in donors)
            {
                if (!seenFootprints.Add(Footprint(inst))) { dupes++; continue; }
                RadiosityAtlas.RectSizeForBounds(inst.SurfaceArea, inst.UvCoverage, out int w, out int h, inst.UvAspect);
                // Coarse is fine - donors only feed the cluster field - but preserve aspect
                // under the clamp so wide islands keep their shape.
                if (w > maxDim || h > maxDim)
                {
                    float scale = Math.Min((float)maxDim / w, (float)maxDim / h);
                    w = Math.Max(2, (int)Math.Round(w * scale));
                    h = Math.Max(2, (int)Math.Round(h * scale));
                }
                if (spent + w * h > budget) continue;
                inst.DonorOnly = true;
                inst.AtlasWidth = w;
                inst.AtlasHeight = h;
                bakeInstances.Add(inst);
                spent += w * h;
                taken++;
            }
            log?.Invoke("    delta donors: " + taken + "/" + donors.Count + " nearby retail islands baked cluster-only (" +
                        spent + " texels, " + dupes + " coincident dupes skipped, reach " + reach.ToString("0.0") + "m)");
            foreach (RadiosityGeometry.Instance inst in bakeInstances)
                if (inst.DonorOnly && inst.AtlasWidth * inst.AtlasHeight >= 200)
                    log?.Invoke("       donor " + inst.AtlasWidth + "x" + inst.AtlasHeight + " at (" +
                                inst.Centre.X.ToString("0.0") + "," + inst.Centre.Y.ToString("0.0") + "," +
                                inst.Centre.Z.ToString("0.0") + ")  area " + inst.SurfaceArea.ToString("0.0") +
                                " m2  tris " + inst.Triangles.Count);

            var deltaSlices = new List<List<RadiosityGeometry.Instance>> { bakeInstances };
            AllocateAtlases(deltaSlices, log);

            Dictionary<int, float> emissiveAreas = ComputeEmissiveAreas(geometry);
            RetailLightPriors lightPriors = CalibrateWeightCoefficient(level, emissiveAreas, log);

            // The volume hash is REQUIRED here: materials with the RADIOSITY_DYNAMIC shader bit may not be lightmapped (the
            // engine asserts on a rect plus the dynamic bit), so everything dynamic in the new content lights from these
            // probes or not at all. Delta slices place input probes on texels (BakeSliceOptions.InputProbesOnTexels):
            // without the zero-distance cluster/probe self-pairs the slice's cluster field never picks up injected energy.
            SliceBake bake = BakeSlice(level, geometry, bakeInstances, runtime.Slices.Count, BakeSliceOptions.DeltaLightmap, emissiveAreas, lightPriors, log);
            log?.Invoke("  slice " + runtime.Slices.Count + " timing: " + bake.Timing);

            // A delta slice starts with almost no direct light of its own: its emissive texels are often dynamic movers and
            // the room's emitters sample the ROOM slice's probes. The unbaked-emitter pass samples both at our probes with
            // retail's own Scale/Weight (without it the moved cam9 vent wall rendered black beside its glowing tubes).
            AddUnbakedEmitterLights(level, geometry, new[] { bake }, BakeSliceOptions.DeltaLightmap.LostEmittersMustBeVisible, lightPriors, log);
            ExpandSiblingChains(new[] { bake }, lightPriors, log);

            if (bake?.Slice?.SurfaceLights?.Lights != null)
            {
                List<RadiosityRuntime.RuntimeSurfaceLights.Light> lights = bake.Slice.SurfaceLights.Lights;
                for (int i = 0; i < lights.Count; i++)
                {
                    RadiosityRuntime.RuntimeSurfaceLights.Light l = lights[i];
                    l.Weight = l.Weight == 0 ? (byte)0 : (byte)Math.Max(1, Math.Min(191, (int)Math.Round(l.Weight * RadiosityTuning.DeltaLightWeightScale)));
                    lights[i] = l;
                }
            }

            FoldVisPaletteIntoRuntime(runtime, bake, log);

            int newSliceIndex = runtime.Slices.Count;
            runtime.Slices.Add(bake.Slice);

            int deltaFixups = AppendDeltaFixups(runtime, bake, newSliceIndex, geometry, log);

            CalibrateDeltaEnergy(runtime, bake, newSliceIndex, state, log);

            // Island ids: prefer the GAPS retail left in its own id space (in-range ids no map entry uses: geometry retail
            // excluded). A gap id sits inside retail's default island state; a beyond-range id has no state at all.
            var usedIds = new HashSet<int>();
            if (level.RadiosityInstanceMap != null)
                foreach (RadiosityInstanceMap.Entry e in level.RadiosityInstanceMap.Entries)
                    usedIds.Add(e.lightmap_transform);
            var gapIds = new Queue<int>();
            for (int id = 0; id < runtime.InstanceSliceIndices.Count; id++)
                if (!usedIds.Contains(id)) gapIds.Enqueue(id);

            // HARVEST in-range ids from coincident-duplicate islands: a beyond-range id is BROKEN, because
            // InstanceSliceIndices entries past retail's count are never visited (the cam7 stolen-id door: black with an
            // appended id, fully lit with an in-range one). Two mapped islands whose movers sit at identical positions
            // render identical pixels, so repointing one twin's rows onto the other (with its rect params) frees an id at
            // no visual cost. Harvested lazily, only as needed.
            var harvestable = new Queue<(int freeId, int keepId)>();
            List<RadiosityInstanceMap.Entry> mapEntriesH = level.RadiosityInstanceMap?.Entries;
            // Ids that must never be freed: anything a delta mover's own resource binds (the
            // reuse path may claim it), plus everything assigned during this bake.
            var usedIdsTaken = new HashSet<int>();
            foreach (int dm in deltaMovers)
            {
                Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[dm];
                if (mv.Resource == null || mapEntriesH == null) continue;
                foreach (RadiosityInstanceMap.Entry e in mapEntriesH)
                {
                    Resources.Resource r = e.Resource ?? level.Resources.GetAtWriteIndex(e.resource_index);
                    if (r == mv.Resource) usedIdsTaken.Add(e.lightmap_transform);
                }
            }
            if (mapEntriesH != null)
            {
                var moverForKeyH = new Dictionary<ulong, int>();
                for (int m = 0; m < level.Movers.Entries.Count; m++)
                {
                    Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[m];
                    if (mv.Resource == null) continue;
                    ulong k = ((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | mv.Resource.resource_id.AsUInt32;
                    if (!moverForKeyH.ContainsKey(k)) moverForKeyH[k] = m;
                }
                var islandKeysH = new Dictionary<int, List<ulong>>();
                foreach (RadiosityInstanceMap.Entry e in mapEntriesH)
                {
                    Resources.Resource r = e.Resource ?? level.Resources.GetAtWriteIndex(e.resource_index);
                    if (r == null) continue;
                    ulong k = ((ulong)r.composite_instance_id.AsUInt32 << 32) | r.resource_id.AsUInt32;
                    if (!islandKeysH.TryGetValue(e.lightmap_transform, out List<ulong> ks))
                        islandKeysH[e.lightmap_transform] = ks = new List<ulong>();
                    if (!ks.Contains(k)) ks.Add(k);
                }
                var bySignature = new Dictionary<string, List<int>>();
                foreach (KeyValuePair<int, List<ulong>> kv in islandKeysH)
                {
                    var parts = new List<string>();
                    bool ok = true;
                    foreach (ulong k in kv.Value)
                    {
                        if (!moverForKeyH.TryGetValue(k, out int m)) { ok = false; break; }
                        System.Numerics.Matrix4x4 tr = level.Movers.Entries[m].Transform;
                        parts.Add(((int)Math.Round(tr.M41 * 100)) + "," + ((int)Math.Round(tr.M42 * 100)) + "," + ((int)Math.Round(tr.M43 * 100)));
                    }
                    if (!ok || parts.Count == 0) continue;
                    parts.Sort();
                    string sig = parts.Count + "|" + string.Join(";", parts);
                    if (!bySignature.TryGetValue(sig, out List<int> ids)) bySignature[sig] = ids = new List<int>();
                    ids.Add(kv.Key);
                }
                foreach (List<int> group in bySignature.Values)
                {
                    if (group.Count < 2) continue;
                    group.Sort();
                    for (int g = 1; g < group.Count; g++)
                        harvestable.Enqueue((group[g], group[0]));
                }
            }
            int HarvestId()
            {
                while (harvestable.Count > 0)
                {
                    (int freeId, int keepId) = harvestable.Dequeue();
                    if (!islandForKeyGuard(freeId) || !islandForKeyGuard(keepId)) continue;
                    // Repoint every row of the freed island onto its twin, and copy the twin's
                    // lightmap rect params onto the freed island's movers so they sample the
                    // twin's rect (identical geometry, identical pixels).
                    byte[] twinParams = null;
                    foreach (RadiosityInstanceMap.Entry e in mapEntriesH)
                    {
                        if (e.lightmap_transform != keepId) continue;
                        Resources.Resource r = e.Resource ?? level.Resources.GetAtWriteIndex(e.resource_index);
                        if (r == null) continue;
                        foreach (Movers.MOVER_DESCRIPTOR mv in level.Movers.Entries)
                            if (mv.Resource == r && mv.RenderConstants?.RawBytes != null && mv.RenderConstants.RawBytes.Length >= 16)
                            { twinParams = mv.RenderConstants.RawBytes; break; }
                        if (twinParams != null) break;
                    }
                    foreach (RadiosityInstanceMap.Entry e in mapEntriesH)
                    {
                        if (e.lightmap_transform != freeId) continue;
                        e.lightmap_transform = keepId;
                        if (twinParams == null) continue;
                        Resources.Resource r = e.Resource ?? level.Resources.GetAtWriteIndex(e.resource_index);
                        if (r == null) continue;
                        foreach (Movers.MOVER_DESCRIPTOR mv in level.Movers.Entries)
                        {
                            if (mv.Resource != r || mv.RenderConstants?.RawBytes == null || mv.RenderConstants.RawBytes.Length < 16) continue;
                            byte[] raw = mv.RenderConstants.RawBytes;
                            Array.Copy(twinParams, 0, raw, 0, 16);
                            mv.RenderConstants.SetRawBytes(raw);
                        }
                    }
                    return freeId;
                }
                return -1;
            }
            bool islandForKeyGuard(int id) => id >= 0 && id < runtime.InstanceSliceIndices.Count && !usedIdsTaken.Contains(id);

            log?.Invoke("    delta island ids: " + gapIds.Count + " retail gaps, " + harvestable.Count +
                        " duplicate-island ids harvestable, for " + deltaInstances.Count + " islands");
            int sharedOverflowId = -1, sharedOverflowCount = 0;

            // ALL rows per key: a resource has one map row PER SUBMESH, and the engine binds each submesh through its own
            // row. Repointing only one left three of the moved CM9 rack's four submeshes on the stale retail binding, and
            // the cabinet stayed black.
            var mapEntries = level.RadiosityInstanceMap?.Entries;
            var existingByKey = new Dictionary<ulong, List<RadiosityInstanceMap.Entry>>();
            if (mapEntries != null)
                foreach (RadiosityInstanceMap.Entry e in mapEntries)
                {
                    Resources.Resource r = e.Resource ?? level.Resources.GetAtWriteIndex(e.resource_index);
                    if (r == null)
                        continue;
                    ulong k = ((ulong)r.composite_instance_id.AsUInt32 << 32) | r.resource_id.AsUInt32;
                    if (!existingByKey.TryGetValue(k, out List<RadiosityInstanceMap.Entry> rows))
                        existingByKey[k] = rows = new List<RadiosityInstanceMap.Entry>();
                    rows.Add(e);
                }

            int lightsBuilt = bake.Slice.SurfaceLights?.Lights?.Count ?? 0;
            foreach (RadiosityGeometry.Instance instance in deltaInstances)
            {
                // A delta island whose movers ALL repoint away from one retail island orphans that retail id, so reuse it:
                // a retail id is in range of every per-island state table the runtime keeps.
                int retailIdReuse = -1;
                var instanceKeys = new HashSet<ulong>();
                foreach (int moverIndex in instance.Movers)
                {
                    Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[moverIndex];
                    if (mv.Resource != null)
                        instanceKeys.Add(((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | mv.Resource.resource_id.AsUInt32);
                }
                foreach (ulong k in instanceKeys)
                {
                    if (!existingByKey.TryGetValue(k, out List<RadiosityInstanceMap.Entry> rows) || rows.Count == 0)
                        continue;
                    int candidate = rows[0].lightmap_transform;
                    if (candidate < 0 || candidate >= runtime.InstanceSliceIndices.Count)
                        continue;
                    // Only truly ORPHANED ids: every resource the map binds to this id must be in
                    // this instance, or a partial overlap would strand the leftovers on a slice
                    // binding that no longer matches their rects.
                    bool fullyCovered = true;
                    foreach (RadiosityInstanceMap.Entry e in mapEntries)
                    {
                        if (e.lightmap_transform != candidate) continue;
                        Resources.Resource r = e.Resource ?? level.Resources.GetAtWriteIndex(e.resource_index);
                        if (r == null) { fullyCovered = false; break; }
                        ulong rk = ((ulong)r.composite_instance_id.AsUInt32 << 32) | r.resource_id.AsUInt32;
                        if (!instanceKeys.Contains(rk)) { fullyCovered = false; break; }
                    }
                    if (fullyCovered)
                    {
                        retailIdReuse = candidate;
                        break;
                    }
                }

                int islandId;
                if (retailIdReuse >= 0)
                {
                    islandId = retailIdReuse;
                    runtime.InstanceSliceIndices[islandId] = newSliceIndex;
                    log?.Invoke("    delta island ids: reusing orphaned retail id " + islandId);
                }
                else if (gapIds.Count > 0)
                {
                    islandId = gapIds.Dequeue();
                    runtime.InstanceSliceIndices[islandId] = newSliceIndex;
                }
                else if ((islandId = HarvestId()) >= 0)
                {
                    runtime.InstanceSliceIndices[islandId] = newSliceIndex;
                    log?.Invoke("    delta island ids: harvested duplicate-island id " + islandId);
                    // The LAST harvestable id becomes the shared overflow id: retail itself ships multi-rect islands
                    // (CM9 island 1322 carries two rects under one id), so per-mover MODEL_PARAMS drives the sampling and
                    // any number of remaining delta islands can share one in-range id - better than a beyond-range one.
                    if (harvestable.Count == 0 && sharedOverflowId < 0)
                        sharedOverflowId = islandId;
                }
                else if (sharedOverflowId >= 0)
                {
                    islandId = sharedOverflowId;
                    sharedOverflowCount++;
                }
                else
                {
                    // BROKEN: an id past retail's count misrenders. The engine walks its own model instances and reads
                    // InstanceSliceIndices[i] for each, so entries past retail's count are never visited and never
                    // schedule their slice for relight (the cam7 door: black with an appended id, lit with an in-range one;
                    // H39). Emitted only when every in-range source is exhausted and nothing is shareable.
                    islandId = runtime.InstanceSliceIndices.Count;
                    runtime.InstanceSliceIndices.Add(newSliceIndex);
                    log?.Invoke("    delta island ids: WARNING - no in-range id left, island " + islandId +
                                " is past retail's island count and will misrender");
                }
                usedIdsTaken.Add(islandId);

                foreach (int moverIndex in instance.Movers)
                {
                    Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                    WriteModelParams(mover, instance);
                    // Every rect-assigned mover joins the delta set - islands carry unmoved
                    // instance-mates beyond the census movers, and the patcher's pristine
                    // MODEL_PARAMS restore must skip them all or their fresh rects are clobbered.
                    deltaMovers.Add(moverIndex);
                    if (mover.Resource == null || mapEntries == null)
                        continue;
                    ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                    if (existingByKey.TryGetValue(key, out List<RadiosityInstanceMap.Entry> existingRows))
                    {
                        // A MOVED mover: repoint EVERY row (one per submesh) at the new slice's
                        // bake - retail keeps all of a resource's rows on one island.
                        foreach (RadiosityInstanceMap.Entry existing in existingRows)
                        {
                            existing.lightmap_transform = islandId;
                            existing.Resource = mover.Resource;
                        }
                    }
                    else
                    {
                        mapEntries.Add(new RadiosityInstanceMap.Entry
                        {
                            lightmap_transform = islandId,
                            Resource = mover.Resource,
                            resource_index = -1
                        });
                    }
                }
            }

            log?.Invoke("Radiosity delta: slice " + newSliceIndex + " appended - " + deltaInstances.Count +
                        " islands, " + deltaInstances.Sum(i => i.Movers.Count) + " movers, " +
                        bake.SurfaceProbeCount + " surface probes, " + bake.InputProbeCount + " input probes, " +
                        lightsBuilt + " surface lights, " + deltaFixups + " cross-slice fixups" +
                        (sharedOverflowCount > 0 ? ", " + sharedOverflowCount + " islands sharing overflow id " + sharedOverflowId : ""));
            return graftedIslands + deltaInstances.Count;
        }

        /// <summary>
        /// Split the delta lightmap movers into chunks sized by the atlas TEXELS their islands
        /// need, balanced so no chunk's atlas runs hot enough to fragment. Islands are kept whole,
        /// which a mover-count split does not guarantee either. Returns null when the geometry
        /// collect fails or no island owns a delta mover; the patcher then falls back to its
        /// mover-count zone chunking.
        /// </summary>
        internal static List<HashSet<int>> ChunkDeltaByAtlasDemand(
            Level level, HashSet<int> lightmapMovers, Action<string> log)
        {
            RadiosityGeometry geometry;
            try { geometry = RadiosityGeometry.CollectFromLevel(level, staticCompositesOnly: true, splitTransparentIslands: false, null); }
            catch (Exception e) { log?.Invoke("    demand chunking: geometry collect failed (" + e.Message + ") - falling back"); return null; }

            // One entry per island that owns any of our movers, with the rect it will ask for.
            var demand = new List<(List<int> movers, int texels)>();
            long total = 0;
            foreach (RadiosityGeometry.Instance inst in geometry.Instances)
            {
                var mine = new List<int>();
                foreach (int m in inst.Movers)
                    if (lightmapMovers.Contains(m)) mine.Add(m);
                if (mine.Count == 0) continue;
                RadiosityAtlas.RectSizeForBounds(inst.SurfaceArea, inst.UvCoverage, out int w, out int h, inst.UvAspect);
                int texels = Math.Max(1, w * h);
                demand.Add((mine, texels));
                total += texels;
            }
            if (demand.Count == 0) return null;

            int budget = RadiosityTuning.DeltaChunkTexelBudget;   // 75% atlas occupancy
            int chunkCount = Math.Max(1, (int)Math.Ceiling(total / (double)budget));

            // Worst-fit: each island goes to the emptiest chunk, so the fills stay level instead
            // of the first chunk taking every large island and fragmenting at 95%.
            var chunks = new List<HashSet<int>>();
            var fill = new List<long>();
            for (int i = 0; i < chunkCount; i++) { chunks.Add(new HashSet<int>()); fill.Add(0); }
            foreach ((List<int> movers, int texels) in demand.OrderByDescending(d => d.texels))
            {
                int best = 0;
                for (int i = 1; i < fill.Count; i++) if (fill[i] < fill[best]) best = i;
                foreach (int m in movers) chunks[best].Add(m);
                fill[best] += texels;
            }

            var kept = new List<HashSet<int>>();
            for (int i = 0; i < chunks.Count; i++) if (chunks[i].Count > 0) kept.Add(chunks[i]);
            log?.Invoke("    demand chunking: " + demand.Count + " islands, " + total + " texels wanted -> " +
                        kept.Count + " chunks at target 0.75 (fills " +
                        string.Join("/", fill.Select(f => (100.0 * f / AtlasTexels).ToString("0"))) + "%)");
            return kept;
        }

        /// <summary>
        /// Append cross-slice fixups that let <paramref name="receiver"/>'s probes gather from
        /// <paramref name="emitter"/>'s clusters, at most <paramref name="perProbeCap"/> per probe (the delta path: a
        /// delta slice gathering from a retail slice).
        /// </summary>
        private static void EmitPairFixups(
            SliceBake receiver, SliceBake emitter, RadiosityRuntime runtime, RadiosityGeometry geometry,
            float maxDist, float maxDistSq, int perProbeCap, HashSet<int> skipProbeSlots)
        {
            if (receiver?.Texels == null || emitter?.Texels == null)
                return;

            // Emitters from the other slice, bucketed so each receiver only tests what is close.
            var emitterTexels = new List<int>();
            Vector3 emitterMin = new Vector3(float.MaxValue), emitterMax = new Vector3(float.MinValue);
            for (int i = 0; i < AtlasTexels; i++)
            {
                // ClusterTex must name a live cluster in the neighbour, so only its clusters qualify.
                if (!emitter.Texels[i].Live || emitter.InputProbeForTexel[i] < 0) continue;
                emitterTexels.Add(i);
                emitterMin = Vector3.Min(emitterMin, emitter.Texels[i].Position);
                emitterMax = Vector3.Max(emitterMax, emitter.Texels[i].Position);
            }
            if (emitterTexels.Count == 0)
                return;

            // Only probes near the other slice can transfer into it. Without this the pass is
            // every-probe-against-every-probe across each slice pair, which does not finish.
            emitterMin -= new Vector3(maxDist);
            emitterMax += new Vector3(maxDist);

            var grid = new ProbeGrid(emitter.Texels, emitterTexels, maxDist);

            var candidateProbes = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
            {
                if (!receiver.Texels[i].Live) continue;
                int probeSlot = receiver.SurfaceSlotForTexel[i];
                // Full probes stay candidates: a fixup may replace their weakest in-slice link (appending to free slots
                // only starved every full probe near a slice boundary).
                if (probeSlot < 0) continue;
                if (skipProbeSlots.Contains(probeSlot)) continue;
                Vector3 p = receiver.Texels[i].Position;
                if (p.X < emitterMin.X || p.Y < emitterMin.Y || p.Z < emitterMin.Z ||
                    p.X > emitterMax.X || p.Y > emitterMax.Y || p.Z > emitterMax.Z) continue;
                candidateProbes.Add(i);
            }
            if (candidateProbes.Count == 0)
                return;

            var perProbe = new List<RadiosityRuntime.RuntimeInfluenceFixup>[candidateProbes.Count];

            void Solve(int ci)
            {
                int probeTexel = candidateProbes[ci];
                int probeSlot = receiver.SurfaceSlotForTexel[probeTexel];
                int slot = receiver.UsedInfluenceSlots[probeSlot];
                SurfaceTexel probe = receiver.Texels[probeTexel];
                Vector3 origin = probe.Position + probe.Normal * RadiosityTuning.ProbeSurfaceOffset;

                var candidates = new List<(int texel, float weight, float distance, float cosProduct)>();
                foreach (int otherTexel in grid.Neighbours(probe.Position))
                {
                    SurfaceTexel other = emitter.Texels[otherTexel];
                    Vector3 delta = other.Position - origin;
                    float distanceSq = delta.LengthSquared();
                    if (distanceSq < 1e-6f || distanceSq > maxDistSq)
                        continue;

                    float distance = (float)Math.Sqrt(distanceSq);
                    Vector3 direction = delta / distance;
                    float cosReceiver = Vector3.Dot(probe.Normal, direction);
                    if (cosReceiver <= 0.02f) continue;
                    float cosEmitter = Vector3.Dot(other.Normal, -direction);
                    if (cosEmitter <= 0.02f) continue;

                    float formFactor = cosReceiver * cosEmitter / (float)(Math.PI * distanceSq);
                    if (formFactor <= 1e-5f) continue;

                    // Selection is GEOMETRIC only. Radiance-aware ranking was REVERTED: in dim corridors it reached for the
                    // brightest clusters in range where retail's links sample the local dim field (CM5 cam11 rmse 41.7).
                    float rank = formFactor;

                    // Strict visibility here, unlike the solve's soft test: relaxing it put the extra energy into rooms
                    // already at parity rather than the dim ones (ChallengeMap4 rmse 12.49 -> 13.17).
                    if (!geometry.Visible(origin, other.Position + other.Normal * RadiosityTuning.ProbeSurfaceOffset, RadiosityTuning.RayEpsilon))
                        continue;

                    candidates.Add((otherTexel, rank, distance, cosReceiver * cosEmitter));
                }

                if (candidates.Count == 0)
                    return;

                candidates.Sort((a, b) => b.weight.CompareTo(a.weight));
                var emittedList = new List<RadiosityRuntime.RuntimeInfluenceFixup>();

                // Working copy of the probe's slot weights, updated as fixups claim slots: without it every replacement
                // candidate re-picked the same weakest base slot, and the engine (applying the range in order) left that
                // slot holding the WEAKEST candidate.
                var slotWeights = new byte[InfluencesPerProbe];
                for (int k = 0; k < InfluencesPerProbe; k++)
                    slotWeights[k] = ReadInfluenceWeight(receiver.Slice, probeSlot * InfluencesPerProbe + k);

                foreach ((int texel, float weight, float distance, float cosProduct) in candidates)
                {
                    if (emittedList.Count >= perProbeCap)
                        break;

                    byte fixupWeight = InfluenceWeight(distance, cosProduct);
                    int targetSlot;
                    if (slot < InfluencesPerProbe)
                    {
                        targetSlot = slot;
                        slot++;
                    }
                    else
                    {
                        // No free slot: replace the weakest influence, but only when this link is
                        // genuinely stronger. Both sides use the same weight curve, so the bytes
                        // compare directly.
                        targetSlot = -1;
                        byte weakest = 255;
                        for (int k = 0; k < InfluencesPerProbe; k++)
                        {
                            if (slotWeights[k] < weakest) { weakest = slotWeights[k]; targetSlot = k; }
                        }
                        if (targetSlot < 0 || fixupWeight <= weakest)
                            break;
                    }
                    slotWeights[targetSlot] = fixupWeight;

                    int weightOffset = probeSlot * InfluencesPerProbe + targetSlot;
                    ClusterRef(texel, out byte clusterX, out byte clusterY);
                    emittedList.Add(new RadiosityRuntime.RuntimeInfluenceFixup
                    {
                        WeightTexOffset = weightOffset,
                        InflTexOffset = weightOffset * 2,
                        // The in-slice curve, so a link across a slice boundary carries the strength its geometry earns.
                        Weight = fixupWeight,
                        Padding = 0,
                        ClusterTex = new Vector2u8 { X = clusterX, Y = clusterY }
                    });
                }

                receiver.UsedInfluenceSlots[probeSlot] = (byte)slot;
                perProbe[ci] = emittedList;
            }

            Parallel.For(0, candidateProbes.Count, Solve);

            // Append serially so the fixup array stays grouped by (slice, neighbour) pair.
            foreach (List<RadiosityRuntime.RuntimeInfluenceFixup> list in perProbe)
                if (list != null) runtime.InfluenceFixups.AddRange(list);
        }

        /// <summary>
        /// Neighbour entries and cross-slice fixups for a freshly appended delta slice, so its
        /// probes can gather bounce from the retail clusters around them.
        /// </summary>
        private static int AppendDeltaFixups(
            RadiosityRuntime runtime, SliceBake bake, int newSliceIndex,
            RadiosityGeometry geometry, Action<string> log)
        {
            if (bake?.Texels == null || newSliceIndex == 0)
            {
                runtime.SliceNeighbourArrayOffsets.Add((short)runtime.FlattenedOtherSliceIndices.Count);
                runtime.SliceNeighbourCounts.Add(0);
                return 0;
            }

            // Probe slots the geometric pass below leaves alone.
            var claimed = new HashSet<int>();

            // Donor-fed probes with an (almost) full native diet need no cross-slice links at
            // all; the geometric pass below only tops up the starved ones (occluded corners,
            // shell-edge probes). A saturated cross-slice link that replaces a native link is a
            // strict loss even at a higher weight byte.
            if (bake.UsedInfluenceSlots != null)
            {
                int wellFed = 0;
                for (int slot = 0; slot < bake.UsedInfluenceSlots.Length; slot++)
                    if (bake.UsedInfluenceSlots[slot] >= InfluencesPerProbe * 3 / 4 && claimed.Add(slot))
                        wellFed++;
                if (wellFed > 0)
                    log?.Invoke("    delta fixups: " + wellFed + " donor-fed probes keep native diets");
            }

            // Geometric fixups for the remaining (new-content) probes: gathering from each retail
            // slice's clusters under the delta-specific per-probe cap.
            float maxDist = RadiosityTuning.MaxInfluenceDistance;
            int total = 0;
            var ourEntries = new List<(byte neighbour, RadiosityRuntime.FixupRange range)>();
            for (int s = 0; s < newSliceIndex; s++)
            {
                SliceBake emitter = RetailEmitterBake(runtime.Slices[s]);
                if (emitter == null)
                    continue;
                int first = runtime.InfluenceFixups.Count;
                EmitPairFixups(bake, emitter, runtime, geometry, maxDist, maxDist * maxDist,
                               RadiosityTuning.DeltaCrossSliceFixupsPerProbe, claimed);
                int emitted = runtime.InfluenceFixups.Count - first;
                ourEntries.Add(((byte)s, new RadiosityRuntime.FixupRange { First = first, Num = emitted }));
                total += emitted;
                if (emitted > 0)
                    log?.Invoke("    delta fixups: " + emitted + " gathering from retail slice " + s);
            }

            // Retail neighbour tables are SYMMETRIC on every level (each slice lists every other),
            // so the retail slices must list the delta slice back - with empty ranges, since
            // nothing retail gathers from us yet - or the engine may never walk our pair. The
            // flattened arrays are rebuilt with shifted offsets; the ranges themselves index the
            // global fixup array and survive the reorder untouched.
            var lists = new List<(byte neighbour, RadiosityRuntime.FixupRange range)>[newSliceIndex + 1];
            for (int s = 0; s < newSliceIndex; s++)
            {
                lists[s] = new List<(byte, RadiosityRuntime.FixupRange)>();
                int offset = runtime.SliceNeighbourArrayOffsets[s];
                int count = runtime.SliceNeighbourCounts[s];
                for (int n = 0; n < count; n++)
                    lists[s].Add((runtime.FlattenedOtherSliceIndices[offset + n], runtime.FlattenedFixupRanges[offset + n]));
                lists[s].Add(((byte)newSliceIndex, new RadiosityRuntime.FixupRange { First = 0, Num = 0 }));
            }
            lists[newSliceIndex] = ourEntries;

            // Retail's convention (every CM9 slice, 52,668 fixups): the BASE weight byte under a fixup slot is ZERO, the
            // "externally fed" marker; a slot where base and fixup disagree is where the engine can pick the wrong side.
            // Zero every base byte a fixup overrides.
            {
                RadiosityRuntime.RuntimeDataSlice deltaSlice = runtime.Slices[newSliceIndex];
                int zeroed = 0;
                foreach ((byte neighbour, RadiosityRuntime.FixupRange range) in ourEntries)
                    for (int i = range.First; i < range.First + range.Num && i < runtime.InfluenceFixups.Count; i++)
                    {
                        int slot = runtime.InfluenceFixups[i].WeightTexOffset;
                        if (ReadInfluenceWeight(deltaSlice, slot) != 0)
                        {
                            WriteInfluenceWeight(deltaSlice, slot, 0);
                            zeroed++;
                        }
                    }
                if (zeroed > 0)
                    log?.Invoke("    delta fixups: zeroed " + zeroed + " base weights under fixup slots (retail convention)");
            }

            runtime.SliceNeighbourCounts.Clear();
            runtime.SliceNeighbourArrayOffsets.Clear();
            runtime.FlattenedOtherSliceIndices.Clear();
            runtime.FlattenedFixupRanges.Clear();
            foreach (List<(byte neighbour, RadiosityRuntime.FixupRange range)> list in lists)
            {
                runtime.SliceNeighbourArrayOffsets.Add((short)runtime.FlattenedOtherSliceIndices.Count);
                runtime.SliceNeighbourCounts.Add((byte)list.Count);
                foreach ((byte neighbour, RadiosityRuntime.FixupRange range) in list)
                {
                    runtime.FlattenedOtherSliceIndices.Add(neighbour);
                    runtime.FlattenedFixupRanges.Add(range);
                }
            }
            return total;
        }

        /// <summary>
        /// Wrap a shipped slice's clusters as an emitter <see cref="EmitPairFixups"/> can read.
        /// </summary>
        private static SliceBake RetailEmitterBake(RadiosityRuntime.RuntimeDataSlice retail)
        {
            if (retail?.ClusterPositions == null || retail.ClusterPositions.Count < AtlasTexels)
                return null;

            var probePositions = new List<Vector3>();
            var probeNormals = new List<Vector3>();
            int probeEntries = Math.Min(retail.InputProbePositions?.Count ?? 0, retail.InputProbeNormals?.Count ?? 0);
            for (int i = 0; i < probeEntries; i++)
            {
                Vector4u16 p = retail.InputProbePositions[i];
                if (p.W == 0)
                    continue;
                ColourRGBA8 enc = retail.InputProbeNormals[i];
                // Retail stores input-probe normals as bytes (z, y, x) - see EncodeNormal.
                var normal = new Vector3(enc.B / 127.5f - 1.0f, enc.G / 127.5f - 1.0f, enc.R / 127.5f - 1.0f);
                float length = normal.Length();
                probePositions.Add(FromHalf3(p));
                probeNormals.Add(length > 1e-3f ? normal / length : Vector3.UnitY);
            }
            if (probePositions.Count == 0)
                return null;

            const float cellSize = 2.0f;
            (int, int, int) Cell(Vector3 v) =>
                ((int)Math.Floor(v.X / cellSize), (int)Math.Floor(v.Y / cellSize), (int)Math.Floor(v.Z / cellSize));
            var cells = new Dictionary<(int, int, int), List<int>>();
            for (int i = 0; i < probePositions.Count; i++)
            {
                (int, int, int) key = Cell(probePositions[i]);
                if (!cells.TryGetValue(key, out List<int> list))
                    cells[key] = list = new List<int>();
                list.Add(i);
            }

            var texels = new SurfaceTexel[AtlasTexels];
            var inputProbeForTexel = new int[AtlasTexels];
            for (int t = 0; t < AtlasTexels; t++)
            {
                inputProbeForTexel[t] = -1;
                Vector4u16 c = retail.ClusterPositions[t];
                if (c.W == 0)
                    continue;
                Vector3 position = FromHalf3(c);

                int best = -1;
                float bestDistSq = float.MaxValue;
                (int cx, int cy, int cz) = Cell(position);
                for (int ring = 1; ring <= 3 && best < 0; ring++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                        for (int dy = -ring; dy <= ring; dy++)
                            for (int dz = -ring; dz <= ring; dz++)
                            {
                                if (!cells.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int> list))
                                    continue;
                                foreach (int i in list)
                                {
                                    float d = Vector3.DistanceSquared(position, probePositions[i]);
                                    if (d < bestDistSq) { bestDistSq = d; best = i; }
                                }
                            }
                }
                if (best < 0)
                    continue;
                texels[t].Live = true;
                texels[t].Position = position;
                texels[t].Normal = probeNormals[best];
                inputProbeForTexel[t] = best;
            }

            return new SliceBake { Texels = texels, InputProbeForTexel = inputProbeForTexel };
        }

        /// <summary>
        /// True when a mover purely TRANSLATED (rotation unchanged, shift under <paramref name="maxDelta"/>): a graft keeps
        /// its texels' RETAIL diets and probe bytes, since the cluster set a diet gathers is the ROOM, which did not move
        /// (CM9 partial move: fresh diets on a byte-retail field delivered ~half, slid retail diets 0.86x).
        /// </summary>
        private static bool MoverPurelyTranslated(Level level, DeltaBakeState state, int moverIndex, float maxDelta)
        {
            if (moverIndex < 0 || moverIndex >= level.Movers.Entries.Count)
                return false;
            Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[moverIndex];
            if (mv.Resource == null)
                return false;
            ulong k = ((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | mv.Resource.resource_id.AsUInt32;
            if (!state.RetailTransforms.TryGetValue(k, out System.Numerics.Matrix4x4 pristine))
                return false;
            System.Numerics.Matrix4x4 cur = mv.Transform;
            float rotDiff =
                Math.Abs(cur.M11 - pristine.M11) + Math.Abs(cur.M12 - pristine.M12) + Math.Abs(cur.M13 - pristine.M13) +
                Math.Abs(cur.M21 - pristine.M21) + Math.Abs(cur.M22 - pristine.M22) + Math.Abs(cur.M23 - pristine.M23) +
                Math.Abs(cur.M31 - pristine.M31) + Math.Abs(cur.M32 - pristine.M32) + Math.Abs(cur.M33 - pristine.M33);
            if (rotDiff > 0.01f)
                return false;
            var delta = new Vector3(cur.M41 - pristine.M41, cur.M42 - pristine.M42, cur.M43 - pristine.M43);
            return delta.Length() < maxDelta;
        }

        /// <summary>
        /// Graft edited retail-bound islands IN PLACE into their own retail slices: each island is
        /// re-rasterised inside its retail rect and its probes get fresh in-slice diets, while every
        /// other byte of the slice stays retail's. Grafted islands leave <paramref name="deltaInstances"/>
        /// and their movers leave <paramref name="deltaMovers"/>; returns how many were grafted.
        /// </summary>
        private static int GraftDeltaIslands(
            Level level, RadiosityRuntime runtime, RadiosityGeometry geometry,
            List<RadiosityGeometry.Instance> deltaInstances, DeltaBakeState state,
            HashSet<int> deltaMovers, Action<string> log)
        {
            if (level.RadiosityInstanceMap?.Entries == null)
                return 0;
            int retailSliceCount = runtime.Slices.Count;

            // Every resource key each island id binds, for the whole-island coverage gate.
            var keysForIsland = new Dictionary<int, HashSet<ulong>>();
            foreach (RadiosityInstanceMap.Entry e in level.RadiosityInstanceMap.Entries)
            {
                Resources.Resource r = e.Resource ?? level.Resources.GetAtWriteIndex(e.resource_index);
                if (r == null) continue;
                ulong k = ((ulong)r.composite_instance_id.AsUInt32 << 32) | r.resource_id.AsUInt32;
                if (!keysForIsland.TryGetValue(e.lightmap_transform, out HashSet<ulong> ks))
                    keysForIsland[e.lightmap_transform] = ks = new HashSet<ulong>();
                ks.Add(k);
            }

            // Slices that host a graft, in first-graft order (pass 2 walks them in this order).
            var graftedSlices = new List<int>();
            var work = new List<(RadiosityGeometry.Instance instance, int islandId, int slice,
                                 int rx, int ry, int rw, int rh, SurfaceTexel[] texels,
                                 int[] slotForTexel, Dictionary<int, double> retailMass)>();

            // ---- pass 1: rasterise and transplant clusters/mangle/input probes in place ----
            foreach (RadiosityGeometry.Instance instance in deltaInstances)
            {
                int islandId = instance.RetailIslandId;
                if (islandId < 0 || islandId >= runtime.InstanceSliceIndices.Count) continue;
                int retailSlice = runtime.InstanceSliceIndices[islandId];
                if (retailSlice < 0 || retailSlice >= retailSliceCount) continue;

                var instanceKeys = new HashSet<ulong>();
                foreach (int m in instance.Movers)
                {
                    Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[m];
                    if (mv.Resource != null)
                        instanceKeys.Add(((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | mv.Resource.resource_id.AsUInt32);
                }
                if (!keysForIsland.TryGetValue(islandId, out HashSet<ulong> islandKeys) ||
                    islandKeys.Any(k => !instanceKeys.Contains(k)))
                {
                    log?.Invoke("    delta graft: island " + islandId + " not fully covered by the instance - appended path");
                    continue;
                }

                // RELOCATIONS (any mover translated beyond 2 m) cannot graft: the island's retail light injection
                // describes the OLD position; the appended path builds native lights at the destination instead.
                // Stale-identity RetailTransforms entries (pristine at the exact origin, the CM9 FX family) are carries.
                bool movedFar = false;
                foreach (int m in instance.Movers)
                {
                    Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[m];
                    if (mv.Resource == null) continue;
                    ulong k = ((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | mv.Resource.resource_id.AsUInt32;
                    if (!state.RetailTransforms.TryGetValue(k, out System.Numerics.Matrix4x4 pristine)) continue;
                    if (pristine.M41 == 0 && pristine.M42 == 0 && pristine.M43 == 0) continue;
                    var d3 = new Vector3(mv.Transform.M41 - pristine.M41,
                                         mv.Transform.M42 - pristine.M42,
                                         mv.Transform.M43 - pristine.M43);
                    if (d3.Length() > 2.0f) { movedFar = true; break; }
                }
                if (movedFar)
                {
                    log?.Invoke("    delta graft: island " + islandId + " relocated beyond graft reach - appended path (native lights at the destination)");
                    continue;
                }

                // Retail rect from any member's pristine MODEL_PARAMS.
                int rx = -1, ry = -1, rw = 0, rh = 0;
                foreach (ulong k in instanceKeys)
                {
                    if (!state.RetailModelParams.TryGetValue(k, out byte[] mp) || mp.Length < 16) continue;
                    int w = (int)Math.Round(BitConverter.ToSingle(mp, 0) + 0.5f);
                    int h = (int)Math.Round(BitConverter.ToSingle(mp, 4) + 0.5f);
                    int x = (int)Math.Round(BitConverter.ToSingle(mp, 8));
                    int y = (int)Math.Round(BitConverter.ToSingle(mp, 12));
                    if (w >= 1 && w <= 128 && h >= 1 && h <= 128 && x >= 0 && y >= 0 && x + w <= AtlasSize && y + h <= AtlasSize)
                    { rx = x; ry = y; rw = w; rh = h; break; }
                }
                if (rx < 0)
                    continue;

                // IN PLACE, no clone: the graft only touches this island's own rect, so the retail slice itself is the
                // host. That keeps the slice count at retail's (a five-slice clone experiment dimmed the WHOLE level:
                // extra slices are not free), and the island keeps its binding untouched.
                if (!graftedSlices.Contains(retailSlice))
                    graftedSlices.Add(retailSlice);
                RadiosityRuntime.RuntimeDataSlice host = runtime.Slices[retailSlice];

                // Snapshot the rect's retail state before any overwrite.
                var retailPos = new Dictionary<int, Vector3>();
                var retailW = new Dictionary<int, ushort>();
                var retailSlot = new Dictionary<int, int>();
                var retailMass = new Dictionary<int, double>();
                for (int y = ry; y < ry + rh; y++)
                    for (int x = rx; x < rx + rw; x++)
                    {
                        int t = y * AtlasSize + x;
                        Vector4u16 cp = host.ClusterPositions[t];
                        if (cp.W == 0) continue;
                        retailPos[t] = FromHalf3(cp);
                        retailW[t] = cp.W;
                        ColourRGBA8 mm = host.MangleMap[t];
                        int slot = mm.G * ProbeTexWidth + mm.R;
                        if (slot >= 0 && slot < host.SurfaceProbePositions.Count &&
                            host.SurfaceProbePositions[slot].W != 0)
                        {
                            retailSlot[t] = slot;
                            if (!retailMass.ContainsKey(slot))
                            {
                                double mass = 0;
                                for (int kk = 0; kk < InfluencesPerProbe; kk++)
                                {
                                    byte wgt = ReadInfluenceWeight(host, slot * InfluencesPerProbe + kk);
                                    if (wgt != 0) mass += Math.Pow(2.0, wgt / 32.0);
                                }
                                retailMass[slot] = mass;
                            }
                        }
                    }

                // Rasterise the CURRENT geometry into the retail rect.
                var texels = new SurfaceTexel[AtlasTexels];
                instance.SliceIndex = retailSlice;
                instance.AtlasX = rx; instance.AtlasY = ry;
                instance.AtlasWidth = rw; instance.AtlasHeight = rh;
                int graftTakeovers = 0;   // the stacked-charts count is only logged for slice bakes
                RasteriseInstance(geometry, instance, texels, ref graftTakeovers);
                FoldAlbedo(texels);
                ResolveRayOrigins(geometry, texels);

                // Borrow a live W for texels retail never lit.
                ushort borrowW = ToHalf(1.0f);
                foreach (ushort wv in retailW.Values) { borrowW = wv; break; }

                // Per-mover translation: a purely translated mover's texels SLIDE retail's stored sample positions by the
                // mover's exact delta (zero for unmoved mates) instead of snapping to our raster winners.
                var moverShift = new Dictionary<int, (bool pure, Vector3 delta)>();
                (bool pure, Vector3 delta) Shift(int moverIndex)
                {
                    if (moverShift.TryGetValue(moverIndex, out (bool, Vector3) cached)) return cached;
                    (bool, Vector3) result = (false, Vector3.Zero);
                    if (MoverPurelyTranslated(level, state, moverIndex, 2.0f))
                    {
                        Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[moverIndex];
                        ulong mk = ((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | mv.Resource.resource_id.AsUInt32;
                        state.RetailTransforms.TryGetValue(mk, out System.Numerics.Matrix4x4 pristine);
                        result = (true, new Vector3(mv.Transform.M41 - pristine.M41,
                                                    mv.Transform.M42 - pristine.M42,
                                                    mv.Transform.M43 - pristine.M43));
                    }
                    moverShift[moverIndex] = result;
                    return result;
                }

                // Transplant: clusters + probe-slot reuse.
                var slotForTexel = new int[AtlasTexels];
                for (int i = 0; i < AtlasTexels; i++) slotForTexel[i] = -1;
                var slotTouched = new HashSet<int>();
                var ourLive = new List<int>();
                for (int y = ry; y < ry + rh; y++)
                    for (int x = rx; x < rx + rw; x++)
                    {
                        int t = y * AtlasSize + x;
                        if (!texels[t].Live) continue;
                        ourLive.Add(t);
                        (bool pure, Vector3 mdelta) = Shift(texels[t].MoverIndex);
                        Vector3 cpos = pure && retailPos.TryGetValue(t, out Vector3 rp)
                            ? rp + mdelta
                            : texels[t].Position;
                        host.ClusterPositions[t] = new Vector4u16
                        {
                            X = ToHalf(cpos.X),
                            Y = ToHalf(cpos.Y),
                            Z = ToHalf(cpos.Z),
                            W = retailW.TryGetValue(t, out ushort wv) ? wv : borrowW
                        };
                        if (retailSlot.TryGetValue(t, out int slot))
                        {
                            // The texel keeps its own retail probe slot; the position follows.
                            // slotTouched guards the slide: two texels sharing one slot through
                            // dilation must not shift it twice.
                            slotForTexel[t] = slot;
                            if (slotTouched.Add(slot))
                            {
                                Vector4 rsp = host.SurfaceProbePositions[slot];
                                host.SurfaceProbePositions[slot] = pure
                                    ? new Vector4(rsp.X + mdelta.X, rsp.Y + mdelta.Y, rsp.Z + mdelta.Z, rsp.W)
                                    : new Vector4(texels[t].Position, ProbeNormalisation);
                            }
                        }
                    }
                if (ourLive.Count == 0)
                {
                    log?.Invoke("    delta graft: island " + islandId + " rasterised no live texels - appended path");
                    continue;
                }

                int NearestOwn(int t)
                {
                    int tx = t % AtlasSize, ty = t / AtlasSize, best = -1; float bestD = float.MaxValue;
                    foreach (int o in ourLive)
                    {
                        if (slotForTexel[o] < 0) continue;
                        int ox = o % AtlasSize, oy = o / AtlasSize;
                        float d = (ox - tx) * (ox - tx) + (oy - ty) * (oy - ty);
                        if (d < bestD) { bestD = d; best = o; }
                    }
                    return best;
                }

                int mangleRewrites = 0, ghostTexels = 0, orphanSlots = 0;
                for (int y = ry; y < ry + rh; y++)
                    for (int x = rx; x < rx + rw; x++)
                    {
                        int t = y * AtlasSize + x;
                        bool oursIsLive = texels[t].Live;
                        bool hasOwnSlot = slotForTexel[t] >= 0;
                        if (oursIsLive && hasOwnSlot)
                            continue;   //retail mangle entry already points at the reused slot
                        int near = NearestOwn(t);
                        if (near < 0) continue;
                        if (!oursIsLive && retailPos.ContainsKey(t))
                        {
                            // Retail had a cluster here; our content no longer covers it. Keep
                            // the texel live as a clone of the nearest own texel so no scatter
                            // entry dangles, and orphan its retail probe slot.
                            host.ClusterPositions[t] = new Vector4u16
                            {
                                X = ToHalf(texels[near].Position.X),
                                Y = ToHalf(texels[near].Position.Y),
                                Z = ToHalf(texels[near].Position.Z),
                                W = retailW[t]
                            };
                            ghostTexels++;
                            if (retailSlot.TryGetValue(t, out int orphan) && orphan != slotForTexel[near])
                            {
                                host.SurfaceProbePositions[orphan] = UnusedSurfaceProbe;
                                for (int kk = 0; kk < InfluencesPerProbe; kk++)
                                    WriteInfluenceWeight(host, orphan * InfluencesPerProbe + kk, 0);
                                orphanSlots++;
                            }
                        }
                        else if (!oursIsLive)
                        {
                            continue;   //dead in both: leave retail's dead-texel mangle alone
                        }
                        int nSlot = slotForTexel[near];
                        int npx = nSlot % ProbeTexWidth, npy = nSlot / ProbeTexWidth;
                        { ColourRGBA8 mm = host.MangleMap[t]; mm.R = (byte)npx; mm.G = (byte)npy; host.MangleMap[t] = mm; }   // keep (B,A): the feedback address of whichever input probe aliases this index
                        mangleRewrites++;
                    }

                // Texel-coincident input probes follow their texels. Only probes that actually
                // MOVED get their normal/albedo refreshed - untouched mates keep retail bytes.
                int probesMoved = 0;
                for (int i = 0; i < host.InputProbePositions.Count; i++)
                {
                    Vector4u16 ip = host.InputProbePositions[i];
                    if (ip.W == 0) continue;
                    Vector3 p = FromHalf3(ip);
                    foreach (KeyValuePair<int, Vector3> kv in retailPos)
                    {
                        if (Vector3.DistanceSquared(p, kv.Value) >= 0.02f * 0.02f) continue;
                        int t = kv.Key;
                        if (!texels[t].Live) break;
                        (bool tPure, Vector3 tDelta) = Shift(texels[t].MoverIndex);
                        // Purely-translated movers slide their probes by the exact delta
                        // (nothing at all for unmoved mates); everything else snaps to the
                        // re-rasterised texel position.
                        Vector3 np = tPure ? p + tDelta : texels[t].Position;
                        if (Vector3.DistanceSquared(np, p) > 0.01f * 0.01f)
                        {
                            host.InputProbePositions[i] = new Vector4u16
                            { X = ToHalf(np.X), Y = ToHalf(np.Y), Z = ToHalf(np.Z), W = ip.W };
                            // A purely-translated mover keeps its RETAIL normal/albedo bytes: a slid probe's surface is
                            // the same surface, and our sampler's albedo is position-scrambled against retail's.
                            if (!MoverPurelyTranslated(level, state, texels[t].MoverIndex, 2.0f))
                            {
                                host.InputProbeNormals[i] = EncodeNormal(texels[t].Normal);
                                host.InputProbeAlbedo[i] = EncodeAlbedo(texels[t].Albedo, 255);
                            }
                            probesMoved++;
                        }
                        break;
                    }
                }

                log?.Invoke("    delta graft: island " + islandId + " (slice " + retailSlice +
                            ", rect " + rw + "x" + rh + "@" + rx + "," + ry + "): " + ourLive.Count + " live texels, " +
                            retailSlot.Count + " slots reused, " + mangleRewrites + " mangle rewrites, " +
                            ghostTexels + " ghosts, " + orphanSlots + " slots orphaned, " + probesMoved + " input probes moved");

                work.Add((instance, islandId, retailSlice, rx, ry, rw, rh, texels, slotForTexel, retailMass));
            }

            if (work.Count == 0)
                return 0;

            // ---- pass 2: fresh diets against each grafted slice's (post-transplant) cluster field ----
            // Runs after EVERY transplant so islands grafted into one slice see each other.
            const float candidateRange = 16.0f;   //retail's measured diet reach tops out ~14.3m
            foreach (int sliceIndex in graftedSlices)
            {
                RadiosityRuntime.RuntimeDataSlice host = runtime.Slices[sliceIndex];

                var clusterGrid = new Dictionary<(int, int, int), List<int>>();
                (int, int, int) CCell(Vector3 v) => ((int)Math.Floor(v.X / candidateRange),
                                                     (int)Math.Floor(v.Y / candidateRange),
                                                     (int)Math.Floor(v.Z / candidateRange));
                var clusterPos = new Vector3[AtlasTexels];
                for (int t = 0; t < AtlasTexels && t < host.ClusterPositions.Count; t++)
                {
                    Vector4u16 cp = host.ClusterPositions[t];
                    if (cp.W == 0) continue;
                    clusterPos[t] = FromHalf3(cp);
                    (int, int, int) key = CCell(clusterPos[t]);
                    if (!clusterGrid.TryGetValue(key, out List<int> l)) clusterGrid[key] = l = new List<int>();
                    l.Add(t);
                }

                foreach ((RadiosityGeometry.Instance instance, int islandId, int wSlice,
                          int rx, int ry, int rw, int rh, SurfaceTexel[] texels,
                          int[] slotForTexel, Dictionary<int, double> retailMass) in work)
                {
                    if (wSlice != sliceIndex) continue;

                    // Emitter normals: nearest level-soup triangle, gridded around the island's
                    // candidate reach.
                    Vector3 bMin = instance.BoundsMin - new Vector3(candidateRange + 1);
                    Vector3 bMax = instance.BoundsMax + new Vector3(candidateRange + 1);
                    const float triCell = 1.0f;
                    var triGrid = new Dictionary<(int, int, int), List<int>>();
                    (int, int, int) TCell(Vector3 v) => ((int)Math.Floor(v.X / triCell),
                                                         (int)Math.Floor(v.Y / triCell),
                                                         (int)Math.Floor(v.Z / triCell));
                    int triCount = geometry.Tris.Length / 3;
                    for (int tri = 0; tri < triCount; tri++)
                    {
                        int i0 = geometry.Tris[tri * 3] * 3, i1 = geometry.Tris[tri * 3 + 1] * 3, i2 = geometry.Tris[tri * 3 + 2] * 3;
                        var centroid = new Vector3(
                            (geometry.Verts[i0] + geometry.Verts[i1] + geometry.Verts[i2]) / 3.0f,
                            (geometry.Verts[i0 + 1] + geometry.Verts[i1 + 1] + geometry.Verts[i2 + 1]) / 3.0f,
                            (geometry.Verts[i0 + 2] + geometry.Verts[i1 + 2] + geometry.Verts[i2 + 2]) / 3.0f);
                        if (centroid.X < bMin.X || centroid.Y < bMin.Y || centroid.Z < bMin.Z ||
                            centroid.X > bMax.X || centroid.Y > bMax.Y || centroid.Z > bMax.Z) continue;
                        (int, int, int) key = TCell(centroid);
                        if (!triGrid.TryGetValue(key, out List<int> l)) triGrid[key] = l = new List<int>();
                        l.Add(tri);
                    }

                    Vector3 TriPoint(int idx) => new Vector3(geometry.Verts[idx], geometry.Verts[idx + 1], geometry.Verts[idx + 2]);
                    bool EmitterNormal(Vector3 p, out Vector3 normal)
                    {
                        normal = Vector3.Zero;
                        float bestD = 1.0f;   //no surface within 1m: not a believable emitter
                        (int cx, int cy, int cz) = TCell(p);
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    if (!triGrid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int> l)) continue;
                                    foreach (int tri in l)
                                    {
                                        Vector3 a = TriPoint(geometry.Tris[tri * 3] * 3);
                                        Vector3 b = TriPoint(geometry.Tris[tri * 3 + 1] * 3);
                                        Vector3 c = TriPoint(geometry.Tris[tri * 3 + 2] * 3);
                                        float d = Vector3.DistanceSquared(p, ClosestOnTriangle(p, a, b, c));
                                        if (d >= bestD) continue;
                                        Vector3 n = Vector3.Cross(b - a, c - a);
                                        float len = n.Length();
                                        if (len < 1e-8f) continue;
                                        bestD = d;
                                        normal = n / len;
                                    }
                                }
                        return normal != Vector3.Zero;
                    }

                    int solved = 0, keptRetailDiet = 0;
                    var shifts = new List<int>();
                    for (int y = ry; y < ry + rh; y++)
                        for (int x = rx; x < rx + rw; x++)
                        {
                            int t = y * AtlasSize + x;
                            int slot = slotForTexel[t];
                            if (slot < 0 || !texels[t].Live) continue;
                            // Purely-translated movers keep their retail diets (see
                            // MoverPurelyTranslated) - the slot position already slid in pass 1.
                            if (MoverPurelyTranslated(level, state, texels[t].MoverIndex, 2.0f))
                            {
                                keptRetailDiet++;
                                continue;
                            }
                            Vector3 origin = texels[t].RayOrigin;
                            Vector3 n = texels[t].Normal;

                            var candidates = new List<(int texel, float weight, float distance, float cosProduct)>();
                            (int gx, int gy, int gz) = CCell(texels[t].Position);
                            for (int dx = -1; dx <= 1; dx++)
                                for (int dy = -1; dy <= 1; dy++)
                                    for (int dz = -1; dz <= 1; dz++)
                                    {
                                        if (!clusterGrid.TryGetValue((gx + dx, gy + dy, gz + dz), out List<int> cell)) continue;
                                        foreach (int t2 in cell)
                                        {
                                            if (t2 == t) continue;
                                            Vector3 delta = clusterPos[t2] - origin;
                                            float distSq = delta.LengthSquared();
                                            if (distSq < 1e-6f || distSq > candidateRange * candidateRange) continue;
                                            float dist = (float)Math.Sqrt(distSq);
                                            Vector3 dir = delta / dist;
                                            float cosR = Vector3.Dot(n, dir);
                                            if (cosR <= 0.02f) continue;
                                            if (!EmitterNormal(clusterPos[t2], out Vector3 n2)) continue;
                                            float cosE = Vector3.Dot(n2, -dir);
                                            if (cosE <= 0.02f) continue;
                                            float formFactor = cosR * cosE / (float)(Math.PI * distSq);
                                            if (formFactor <= 1e-5f) continue;
                                            if (!VisibleSoft(geometry, origin, n,
                                                             clusterPos[t2] + n2 * RadiosityTuning.ProbeSurfaceOffset, n2,
                                                             t, t2))
                                                continue;
                                            candidates.Add((t2, formFactor, dist, cosR * cosE));
                                        }
                                    }

                            if (candidates.Count == 0)
                            {
                                // Keep the slot's retail diet: it described this spot before the
                                // edit, which beats a dead probe.
                                keptRetailDiet++;
                                continue;
                            }
                            candidates.Sort((a, b) => b.weight.CompareTo(a.weight));
                            int keep = Math.Min(InfluencesPerProbe, candidates.Count);
                            StratifyByDistance(candidates, keep, t);

                            for (int k = 0; k < keep; k++)
                            {
                                ClusterRef(candidates[k].texel, out byte cx2, out byte cy2);
                                byte wgt = InfluenceWeight(candidates[k].distance, candidates[k].cosProduct);
                                WriteInfluence(host, slot * InfluencesPerProbe + k, cx2, cy2, wgt);
                            }
                            for (int k = keep; k < InfluencesPerProbe; k++)
                                WriteInfluenceWeight(host, slot * InfluencesPerProbe + k, 0);

                            // Self-calibration: this very slot's retail exp mass IS the energy
                            // budget retail assigned this surface - match it.
                            if (retailMass.TryGetValue(slot, out double target) && target > 0)
                            {
                                double ours = 0;
                                for (int k = 0; k < keep; k++)
                                {
                                    byte wgt = ReadInfluenceWeight(host, slot * InfluencesPerProbe + k);
                                    if (wgt != 0) ours += Math.Pow(2.0, wgt / 32.0);
                                }
                                if (ours > 0)
                                {
                                    int shift = (int)Math.Round(32.0 * Math.Log(target / ours, 2.0));
                                    shift = Math.Max(-48, Math.Min(48, shift));
                                    if (shift != 0)
                                    {
                                        shifts.Add(shift);
                                        for (int k = 0; k < keep; k++)
                                        {
                                            byte wgt = ReadInfluenceWeight(host, slot * InfluencesPerProbe + k);
                                            if (wgt != 0)
                                                WriteInfluenceWeight(host, slot * InfluencesPerProbe + k,
                                                    (byte)Math.Max(1, Math.Min(254, wgt + shift)));
                                        }
                                    }
                                }
                            }
                            solved++;
                        }

                    shifts.Sort();
                    log?.Invoke("    delta graft: island " + islandId + " diets: " + solved + " solved, " +
                                keptRetailDiet + " kept retail" +
                                (shifts.Count > 0 ? ", calibration shift p10/50/90 = " + shifts[shifts.Count / 10] + "/" +
                                 shifts[shifts.Count / 2] + "/" + shifts[shifts.Count * 9 / 10] : ""));
                }
            }

            // ---- pass 3: leave the census (the island keeps its retail id and slice binding) ----
            foreach (var w in work)
            {
                foreach (int m in w.instance.Movers)
                    deltaMovers.Remove(m);
                deltaInstances.Remove(w.instance);
            }
            return work.Count;
        }
    }
}
#endif
