#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CATHODE;
using CathodeLib;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Offline lighting bake. Regenerates RADIOSITY_RUNTIME.BIN and RADIOSITY_INSTANCE_MAP.TXT from
    /// instanced level geometry, and writes the matching lightmap atlas transform into every
    /// participating mover's MODEL_PARAMS.
    /// </summary>
    public static partial class RadiosityBaker
    {
        /// <summary>Atlas / probe textures are a fixed 128x128 per slice.</summary>
        private const int AtlasSize = 128;
        private const int AtlasTexels = AtlasSize * AtlasSize;

        /// <summary>
        /// Input probes live in their own texture, 256 wide and 64 tall, addressed <c>y * 256 + x</c> - not in the
        /// 128x128 atlas. Both arrays hold 16384 elements, so the difference is invisible to the parser and fatal to the
        /// engine (on all 128 retail slices every live input probe falls in a declared tile under stride 256, none under 128).
        /// </summary>
        private const int ProbeTexWidth = 256;

        /// <summary>
        /// Probes pack into 16x16 tiles, 4 rows deep, filling columns left to right. Surface probes
        /// and input probes are separate compacted lists sharing this layout.
        /// </summary>
        private const int TileSize = 16;
        private const int TileRows = 4;

        /// <summary>
        /// Input probe tiles use the full width: sixteen columns of four rows over the 256x64 slice, 64 tiles. Retail never
        /// passes x = 192, but Solace's busiest slice already uses 38 tiles, and a cap would cut off a spatial region.
        /// </summary>
        private const int InputTileColumns = 16;
        private const int MaxInputProbes = TileSize * TileSize * TileRows * InputTileColumns;

        /// <summary>Input probe tiles and the tree nodes over them.</summary>
        private const int MaxInputProbeTiles = 64;
        private const int MaxInputProbeTreeNodes = 128;
        private const int MaxInputQuadVerts = 512;

        /// <summary>Output-side tree sizes, which are far looser than the input side's.</summary>
        private const int MaxOutputProbeTreeNodes = 2048;
        private const int MaxOutputQuadVerts = 2048;

        /// <summary>Surface light groups and the probe samples they reference, per slice.</summary>
        private const int MaxSurfaceLightSlices = 1024;
        private const int MaxSurfaceLightProbes = 8192;

        /// <summary>Doors and their transfer pairs, per slice.</summary>
        private const int MaxDoorsPerSlice = 64;
        private const int MaxDoorTransfersPerSlice = 512;

        /// <summary>Surface probe tiles use the full width - retail reaches column 15.</summary>
        private const int SurfaceTileColumns = 16;
        private const int MaxSurfaceProbes = TileSize * TileSize * TileRows * SurfaceTileColumns;

        /// <summary>Each surface probe stores exactly this many (cluster, weight) influences.</summary>
        private const int InfluencesPerProbe = 32;

        /// <summary>Most surface lights one emissive instance may contribute, matching retail's cap.</summary>
        private const int MaxLightsPerEmitter = 81;

        /// <summary>
        /// Longest scatter link, source cluster to destination input probe, in metres. Scatter has
        /// no visibility term at runtime, so distance is the only wall between areas that should
        /// not exchange light. Retail's p99 is 5.4 m.
        /// </summary>
        private const float ScatterMaxLinkDistance = 6.0f;

        /// <summary>Marks an atlas texel that no instance claimed.</summary>
        private static readonly Vector4 UnusedSurfaceProbe = new Vector4(-100000.0f, 0.0f, 0.0f, 0.0f);

        /// <summary>Retail writes 1/sqrt(pi) into the w of every live probe position.</summary>
        private const float ProbeNormalisation = 0.56418955f;

        public sealed class BakeResult
        {
            /// <summary>
            /// The level held no static geometry to light, so the bake did nothing and the radiosity
            /// files on disk were cleared. The in-memory radiosity objects describe nothing and must
            /// not be saved over that.
            /// </summary>
            public bool NothingToBake;

            public int Slices;
            public int Instances;
            public int SurfaceProbes;
            public int InputProbes;
            public int Clusters;
            public int Influences;
            public int SurfaceLights;
            public int MoversTagged;
            public int StaleRectsCleared;
            public int CrossSliceFixups;
            public int DoorTransfers;

            /// <summary>
            /// The bake was skipped (<see cref="RadiosityBakeSettings.KeepExisting"/>): the level kept the radiosity it had,
            /// re-bound to the rebuilt movers - or, with <see cref="NothingToBake"/>, it had none to keep.
            /// </summary>
            public bool Kept;
            /// <summary>Movers left waiting for a bake (<see cref="RadiosityRuntime.PendingDeltaMovers"/>) after a <see cref="Kept"/> save.</summary>
            public int PendingMovers;

            public string Message;
        }

        /// <summary>
        /// Bake lighting for the level and hand the results back through <paramref name="level"/>:
        /// <see cref="Level.RadiosityRuntime"/> and <see cref="Level.RadiosityInstanceMap"/> are
        /// replaced and MODEL_PARAMS are rewritten. Nothing is written to disk here -
        /// <see cref="Level.Save"/> persists it alongside the rest of the level.
        /// </summary>
        public static BakeResult BakeLevel(
            Level level,
            Instancing instancing = null,
            RadiosityBakeSettings settings = null,
            Action<string> log = null)
        {
            if (level == null)
                throw new ArgumentNullException(nameof(level));
            if (level.Movers == null || level.Resources == null)
                throw new InvalidOperationException("Level is missing MODELS.MVR or RESOURCES.BIN.");

            settings ??= RadiosityBakeSettings.CreateDefault();

            /* No bake: what the level has is kept and re-bound to the movers instancing rebuilt, so the game can still read
             * it (the patch without its delta). A level with nothing to keep is left with no radiosity, as a level with no
             * geometry is - never baked behind the caller's back. */
            if (settings.KeepExisting)
            {
                if (level.RadiosityRuntime == null || level.RadiosityRuntime.Slices == null ||
                    level.RadiosityRuntime.Slices.Count == 0)
                {
                    log?.Invoke("Radiosity: bake skipped, and the level has no radiosity to keep - it is saved without any");
                    if (!Instancing.SkipRadiosityClear)
                        Utilities.ClearRadiosityOnDisk(level);
                    return new BakeResult() { NothingToBake = true, Kept = true, Message = "Radiosity bake skipped: the level had no radiosity to keep." };
                }
                return RadiosityPatcher.PatchLevel(level, settings, log, keepOnly: true);
            }

            /* Delta mode: keep the shipped radiosity and patch only what the edit invalidated.
             * It is worth doing only while there is a retail bake underneath worth protecting -
             * so two cases fall through to a full regeneration instead. */
            if (settings.PatchRetailRuntime)
            {
                string regenerateReason = null;
                if (level.RadiosityRuntime == null || level.RadiosityRuntime.Slices == null ||
                    level.RadiosityRuntime.Slices.Count == 0)
                {
                    // Nothing to preserve: the level either never shipped radiosity, or had it
                    // wiped (older library versions cleared it on every instanced save). Leaving
                    // it alone would leave the level permanently unlit with no way back.
                    regenerateReason = "RADIOSITY_RUNTIME.BIN is missing or empty";
                }
                else if (level.RadiosityRuntime.FullyRegenerated)
                {
                    // Our own output rather than CA's, so there is nothing left to preserve and
                    // patching would only stack the delta path's approximations on top of a bake
                    // that can simply be redone. See RadiosityRuntime.FullyRegenerated.
                    regenerateReason = "this level's radiosity was fully regenerated by a previous bake";
                }

                if (regenerateReason == null)
                    return RadiosityPatcher.PatchLevel(level, settings, log);

                // The full bake rewrites the runtime in place, so it needs one to rewrite into.
                // A null here means the level was never loaded with its radiosity files at all.
                if (level.RadiosityRuntime == null)
                {
                    log?.Invoke("Radiosity patch: " + regenerateReason +
                                ", and the level has no RADIOSITY_RUNTIME.BIN to write into - radiosity left as-is");
                    return new BakeResult();
                }

                log?.Invoke("Radiosity patch: " + regenerateReason + " - regenerating the whole level instead");
            }

            // A full regeneration bakes with the validated profile: the RadiosityTuning constants (a value only a full bake
            // reads is the profile's) and BakeSliceOptions.Full.
            log?.Invoke("Radiosity: full bake with the validated profile");
            BakeSliceOptions options = BakeSliceOptions.Full;

            // Instancing has just rebuilt the resource table, and the collector drops any mover whose
            // resource has no write index yet. Until the next save that is every resource appended
            // this pass - on a level built from nothing, all of them - so refresh before collecting.
            var timer = new PhaseTimer();
            level.Resources.RefreshWriteList();

            RadiosityGeometry geometry = RadiosityGeometry.CollectFromLevel(level, staticCompositesOnly: true, splitTransparentIslands: true, log);
            if (geometry.TriangleCount == 0)
            {
                // Nothing to light yet - a level built before anything is placed in it. Leave it in the
                // unlit state a build without radiosity produces (the harness flag that keeps read-only
                // instancing off the disk is honoured), and tell the caller not to write the empty
                // in-memory radiosity over that.
                log?.Invoke("Radiosity: no renderable geometry to bake - radiosity files cleared");
                if (!Instancing.SkipRadiosityClear)
                    Utilities.ClearRadiosityOnDisk(level);
                return new BakeResult() { NothingToBake = true, Message = "No renderable geometry to bake." };
            }
            timer.Lap("collect");
            geometry.Build(log);
            timer.Lap("build");
            BuildCollisionOccluders(level, geometry, log);
            timer.Lap("occluders");

            List<List<RadiosityGeometry.Instance>> slices = PartitionIntoSlices(geometry, level.RadiosityRuntime, log);
            AllocateAtlases(slices, log);
            timer.Lap("partition");

            // Per-mover emissive surface area, which a surface light's Weight encodes. A full bake reuses no retail light
            // values (priors are the delta path's): over our own earlier output they fed each save's light table into the
            // next (a regenerated ChallengeMap4's light energy moved +5% then +9% over three saves). With an empty prior set
            // Lookup returns null, SuppressedByRetail admits every emitter and K stays at DefaultWeightK.
            Dictionary<int, float> emissiveAreas = ComputeEmissiveAreas(geometry);
            RetailLightPriors lightPriors = new RetailLightPriors();
            log?.Invoke("Radiosity light priors: none on a full bake - every light derived, none reused from retail");

            // Rewrite the level's existing instance in place so it keeps its filepath and
            // Level.Save persists it in the normal pass.
            RadiosityRuntime runtime = level.RadiosityRuntime
                ?? throw new InvalidOperationException("Level has no RADIOSITY_RUNTIME.BIN to rewrite.");
            // Everything below replaces CA's bake wholesale, so from here the data is ours: later
            // saves regenerate it rather than patching a bake with nothing retail left in it.
            runtime.FullyRegenerated = true;
            // ...and it lights every mover, so none is left waiting for a bake a save skipped.
            runtime.PendingDeltaMovers.Clear();
            runtime.Slices.Clear();
            runtime.InstanceSliceIndices.Clear();
            runtime.InfluenceFixups.Clear();
            runtime.FlattenedFixupRanges.Clear();
            timer.Lap("priors");

            var result = new BakeResult { Slices = slices.Count };
            var sliceData = new SliceBake[slices.Count];

            void BakeOne(int i)
            {
                sliceData[i] = BakeSlice(level, geometry, slices[i], i, options, emissiveAreas, lightPriors, log);
            }

            // One slice bakes on this thread, so a BakeSlice exception reaches the caller unwrapped
            // (Parallel.For would wrap it in an AggregateException).
            if (slices.Count > 1)
                Parallel.For(0, slices.Count, BakeOne);
            else
                for (int i = 0; i < slices.Count; i++) BakeOne(i);
            // The slices bake in parallel, so their phase times are logged here, in slice order.
            double slowestSlice = 0;
            for (int s = 0; s < sliceData.Length; s++)
            {
                slowestSlice = Math.Max(slowestSlice, sliceData[s].Seconds);
                log?.Invoke("  slice " + s + " timing: " + sliceData[s].Timing);
            }
            timer.Lap("slices", "slowest " + PhaseTimer.Format(slowestSlice));

            // The visibility palette is shared by every slice, so it can only be folded together
            // once all of them have traced their grids.
            runtime.VolumeProbeVisPalette = BuildVisPalette(sliceData);
            ApplyVisPaletteIndices(sliceData);

            // Instance numbering preserves retail's island ids: the index (the map's
            // lightmap_transform) is what the runtime state system addresses when a script
            // toggles a RadiosityIsland, so a matched island must keep the id retail gave it.
            // Islands retail never baked take ids after retail's highest; ids retail used for
            // geometry we exclude stay as gaps pointing at slice 0, addressing nothing.
            runtime.InstanceSliceIndices.Clear();
            var instanceMapEntries = new List<RadiosityInstanceMap.Entry>();
            var taggedMovers = new HashSet<int>();

            int maxRetailId = -1;
            foreach (List<RadiosityGeometry.Instance> sliceInstances in slices)
                foreach (RadiosityGeometry.Instance instance in sliceInstances)
                    if (instance.RetailIslandId > maxRetailId) maxRetailId = instance.RetailIslandId;
            int nextNewId = maxRetailId + 1;

            var sliceForInstanceId = new Dictionary<int, int>();
            for (int s = 0; s < slices.Count; s++)
            {
                runtime.Slices.Add(sliceData[s].Slice);
                foreach (RadiosityGeometry.Instance instance in slices[s])
                {
                    int instanceIndex = instance.RetailIslandId >= 0 ? instance.RetailIslandId : nextNewId++;
                    sliceForInstanceId[instanceIndex] = s;

                    for (int m = 0; m < instance.Movers.Count; m++)
                    {
                        Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[instance.Movers[m]];

                        // One row per renderable element, retail's convention (CM3 ships 11,247 rows over 6,101
                        // resources, each resource's row count equal to its element count; reproducing it changed
                        // nothing visible). Carry the Resource, not an index: Resources.Save renumbers RESOURCES.BIN
                        // after this runs.
                        int rows = Math.Max(1, mover.RenderableElements?.Count ?? 1);
                        for (int e = 0; e < rows; e++)
                            instanceMapEntries.Add(new RadiosityInstanceMap.Entry
                            {
                                lightmap_transform = instanceIndex,
                                Resource = mover.Resource,
                                resource_index = -1
                            });
                        WriteModelParams(mover, instance);
                        taggedMovers.Add(instance.Movers[m]);
                        result.MoversTagged++;
                    }
                }

                result.SurfaceProbes += sliceData[s].SurfaceProbeCount;
                result.InputProbes += sliceData[s].InputProbeCount;
                result.Clusters += sliceData[s].ClusterCount;
                result.SurfaceLights += sliceData[s].Slice.SurfaceLights.Lights.Count;
            }

            int instanceIdCeiling = Math.Max(nextNewId, maxRetailId + 1);
            for (int i = 0; i < instanceIdCeiling; i++)
                runtime.InstanceSliceIndices.Add(sliceForInstanceId.TryGetValue(i, out int s2) ? s2 : 0);
            timer.Lap("islands");

            AddUnbakedEmitterLights(level, geometry, sliceData, options.LostEmittersMustBeVisible, lightPriors, log);
            ExpandSiblingChains(sliceData, lightPriors, log);
            timer.Lap("lights");

            result.StaleRectsCleared = ClearStaleModelParams(level, taggedMovers);

            BuildSliceNeighbours(runtime);
            timer.Lap("neighbours");

            // The cross-slice fixups are the cut's (below): it writes them with the rest of each probe's links and fills
            // in every range, so until then each neighbour entry only needs its empty range.
            ClearCrossSliceFixups(runtime);
            timer.Lap("fixups");
            result.DoorTransfers = BuildDoors(level, geometry, sliceData, log);
            timer.Lap("doors");

            // Last: the cluster stage and every link rebuilt as retail's disjoint hierarchical cut (it reads only
            // the finished probes, mangle map and neighbour/fixup-range tables, so everything above is unaffected). Its
            // visibility test is the soft one against the render meshes. Its hundreds of millions of rays (nearly all
            // blocked) read the render BVH's leaf-ordered triangle copy while it runs: same answers, 36 bytes a triangle,
            // released after.
            bool occlusionCache = geometry.Bvh?.BuildOcclusionCache() ?? false;
            try
            {
                result.Influences = RebuildLinksRetailCut(runtime, (a, b) => geometry.VisibleRenderSoft(a, b, RadiosityTuning.RayEpsilon, RadiosityTuning.CutSoftVisRays, RadiosityTuning.CutSoftVisJitter), log);
            }
            finally
            {
                if (occlusionCache) geometry.Bvh.ReleaseOcclusionCache();
            }
            result.CrossSliceFixups = runtime.InfluenceFixups.Count;
            timer.Lap("cut");

            // Put each light's samples where retail's own rule puts them: on the probes nearest the emitter's emissive
            // surface (see ApplyEmitterSurfacePlacement).
            ApplyEmitterSurfacePlacement(runtime, geometry, level, RadiosityTuning.EmitterSurfacePlacement, log);
            timer.Lap("placement");
            log?.Invoke("Radiosity timing: " + timer);

            if (level.RadiosityInstanceMap != null)
            {
                level.RadiosityInstanceMap.Entries.Clear();
                level.RadiosityInstanceMap.Entries.AddRange(instanceMapEntries);
            }

            // TODO: RADIOSITY_COLLISION_MAPPING pairs radiosity doors with collision instances so
            // light transfer follows moving geometry. Empty is valid - most retail levels ship it
            // empty - and matches the empty door sets emitted per slice.
            level.RadiosityCollisionMap?.Entries.Clear();

            result.Instances = runtime.InstanceSliceIndices.Count;
            result.Message = "Radiosity: slices=" + result.Slices +
                             " instances=" + result.Instances +
                             " surfaceProbes=" + result.SurfaceProbes +
                             " inputProbes=" + result.InputProbes +
                             " clusters=" + result.Clusters +
                             " influences=" + result.Influences +
                             " surfaceLights=" + result.SurfaceLights +
                             " moversTagged=" + result.MoversTagged +
                             " staleRectsCleared=" + result.StaleRectsCleared +
                             " crossSliceFixups=" + result.CrossSliceFixups +
                             " doorTransfers=" + result.DoorTransfers;
            log?.Invoke(result.Message);
            return result;
        }

        /// <summary>
        /// Visibility occludes against the level's collision shell, not its render meshes, wherever it can be collected:
        /// render meshes carry interior submeshes, back faces and double-sided surfaces that buried a quarter of our surface
        /// probes. The shell needs the origin lift (<see cref="RadiosityTuning.OccluderProjectionRange"/>) and the endpoint
        /// slack set here. Doorway barriers (PATH_CLOSED) are left out, so every doorway bakes open: door transfers describe
        /// door-open light paths, and a solid barrier walled off rooms that borrow light through their doorways in retail
        /// (18 Aug 2026: Solace rmse 13.86 -> 13.55, BSP_TORRENS 19.83 -> 19.35).
        /// </summary>
        private static void BuildCollisionOccluders(Level level, RadiosityGeometry geometry, Action<string> log)
        {
            if (RadiosityOccluders.TryCollect(level, geometry, out float[] occluderVerts, out int[] occluderTris, log,
                                              skipDoorBarriers: true))
            {
                geometry.OccluderEndpointSlack = RadiosityTuning.OccluderEndpointSlack;
                geometry.OccluderSlackFraction = RadiosityTuning.OccluderSlackFraction;
                geometry.BuildOccluders(occluderVerts, occluderTris, log);
            }
        }

        #region SLICING

        /// <summary>
        /// Split instances into slices. Retail's own grouping is used where the level still carries
        /// it (see <see cref="PartitionByRetailSlices"/>); otherwise instances are
        /// split by recursive median split on their centroids until each slice's atlas demand fits.
        /// Spatial coherence matters for the fallback: influences are only gathered within a slice,
        /// so neighbours should share one.
        /// </summary>
        private static List<List<RadiosityGeometry.Instance>> PartitionIntoSlices(
            RadiosityGeometry geometry, RadiosityRuntime retail, Action<string> log)
        {
            int uvRuleRects = 0, uvRuleTexels = 0, formulaTexels = 0;
            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
            {
                // The mesh itself says what rect retail gave this geometry - see
                // RadiosityGeometry.TryRectFromUvConvention. Takes precedence over the formula, and
                // keeps retail's invariant that every placement of one model gets the same rect
                // (CM3: 191 of 191 multi-instance models; RADIOSITY_LEVEL.BIN stores ProbeCount once
                // per model, a reusable geometry chunk rather than a single prop mesh).
                if (RadiosityGeometry.TryRectFromUvConvention(instance, out int uw, out int uh))
                {
                    RadiosityAtlas.RectSizeForBounds(instance.SurfaceArea, instance.UvCoverage, out int fw, out int fh, instance.UvAspect);
                    uvRuleRects++; uvRuleTexels += uw * uh; formulaTexels += fw * fh;
                    instance.AtlasWidth = Math.Min(AtlasSize, uw);
                    instance.AtlasHeight = Math.Min(AtlasSize, uh);
                    continue;
                }
                RadiosityAtlas.RectSizeForBounds(instance.SurfaceArea, instance.UvCoverage, out int w, out int h, instance.UvAspect);
                instance.AtlasWidth = w;
                instance.AtlasHeight = h;
            }
            log?.Invoke("Radiosity rects: " + uvRuleRects + " of " + geometry.Instances.Count + " islands sized from the mesh UV convention (" +
                        uvRuleTexels + " texels where the area formula would have given " + formulaTexels + "), the rest by formula");

            List<List<RadiosityGeometry.Instance>> retailGrouped =
                PartitionByRetailSlices(geometry, retail, log);
            if (retailGrouped != null)
                return retailGrouped;

            var slices = new List<List<RadiosityGeometry.Instance>>();
            SplitSpatially(geometry.Instances.ToList(), slices, log);
            log?.Invoke("Radiosity slices: " + slices.Count + " (" +
                        string.Join(", ", slices.Select(o => o.Count + " inst / " + o.Sum(i => i.AtlasWidth * i.AtlasHeight) + " texels")) + ")");
            return slices;
        }

        /// <summary>
        /// Group instances into the slices retail's own bake used, resolved through each instance's
        /// retail island id. Returns null when there is not enough retail data to do it, so the
        /// caller falls back to the spatial split.
        /// </summary>
        private static List<List<RadiosityGeometry.Instance>> PartitionByRetailSlices(
            RadiosityGeometry geometry, RadiosityRuntime retail, Action<string> log)
        {
            if (retail == null || retail.InstanceSliceIndices.Count == 0)
                return null;

            var byRetailSlice = new Dictionary<int, List<RadiosityGeometry.Instance>>();
            var unmatched = new List<RadiosityGeometry.Instance>();
            var matched = new List<RadiosityGeometry.Instance>();
            var matchedSlice = new List<int>();

            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
            {
                int island = instance.RetailIslandId;
                if (island < 0 || island >= retail.InstanceSliceIndices.Count)
                {
                    unmatched.Add(instance);
                    continue;
                }
                int s = retail.InstanceSliceIndices[island];
                if (s < 0)
                {
                    unmatched.Add(instance);
                    continue;
                }
                if (!byRetailSlice.TryGetValue(s, out List<RadiosityGeometry.Instance> list))
                    byRetailSlice[s] = list = new List<RadiosityGeometry.Instance>();
                list.Add(instance);
                matched.Add(instance);
                matchedSlice.Add(s);
            }

            // Too little overlap with retail's bake to trust the grouping: a scratch bake, or a
            // level whose geometry has been replaced wholesale.
            if (matched.Count * 2 < geometry.Instances.Count || byRetailSlice.Count == 0)
            {
                log?.Invoke("Radiosity slices: only " + matched.Count + " of " + geometry.Instances.Count +
                            " instances carry a retail island; falling back to the spatial split.");
                return null;
            }

            // Islands retail never baked join their nearest matched neighbour, so new content lands
            // in the slice that already lights the space around it.
            foreach (RadiosityGeometry.Instance instance in unmatched)
            {
                int best = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < matched.Count; i++)
                {
                    float d = Vector3.DistanceSquared(instance.Centre, matched[i].Centre);
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                byRetailSlice[best >= 0 ? matchedSlice[best] : byRetailSlice.Keys.First()].Add(instance);
            }

            // A retail group whose rects do not fit our atlas still has to be split, or the packer
            // shrinks every instance in it. Sub-groups get their own InstanceSliceIndices entries,
            // so the island -> slice mapping stays correct either way. No group is empty (one is
            // only created to take an instance), so no slice is: a slice carrying retail's
            // subdivision count with no items is the combination render_object_probes faults on.
            var final = new List<List<RadiosityGeometry.Instance>>();
            foreach (int key in byRetailSlice.Keys.OrderBy(k => k))
            {
                List<RadiosityGeometry.Instance> group = byRetailSlice[key];
                int texels = group.Sum(o => o.AtlasWidth * o.AtlasHeight);
                if (texels <= RadiosityTuning.MaxTexelsPerRetailSlice || group.Count == 1)
                {
                    final.Add(group);
                    continue;
                }
                log?.Invoke("Radiosity slices: retail group " + key + " of " + group.Count +
                            " instances needs " + texels + " texels; splitting it spatially.");
                SplitSpatially(group, final, log);
            }

            log?.Invoke("Radiosity slices: " + final.Count + " from retail's grouping (" +
                        matched.Count + "/" + geometry.Instances.Count + " instances matched an island) (" +
                        string.Join(", ", final.Select(o => o.Count + " inst / " + o.Sum(i => i.AtlasWidth * i.AtlasHeight) + " texels")) + ")");
            return final;
        }

        /// <summary>
        /// Recursive median split on instance centroids, appending each fitting group to
        /// <paramref name="output"/>.
        /// </summary>
        private static void SplitSpatially(
            List<RadiosityGeometry.Instance> group, List<List<RadiosityGeometry.Instance>> output, Action<string> log)
        {
            if (group.Count == 0)
                return;

            // Splitting must keep going until the group fits: a slice holding more live texels
            // than MaxInputProbes cannot give every texel an input probe, and a cluster with no
            // probe to scatter to breaks the "every live cluster is a scatter source" invariant.
            int texels = group.Sum(o => o.AtlasWidth * o.AtlasHeight);
            if (texels <= RadiosityTuning.MaxTexelsPerSlice || group.Count == 1)
            {
                output.Add(group);
                return;
            }
            if (output.Count >= RadiosityTuning.MaxSlices)
            {
                log?.Invoke("  WARNING: slice budget (" + RadiosityTuning.MaxSlices + ") reached with " +
                            texels + " texels left to place; the last slice will overflow its atlas.");
                output.Add(group);
                return;
            }

            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            foreach (RadiosityGeometry.Instance o in group)
            {
                min = Vector3.Min(min, o.Centre);
                max = Vector3.Max(max, o.Centre);
            }

            Vector3 extent = max - min;
            int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
            group.Sort((a, b) => Axis(a.Centre, axis).CompareTo(Axis(b.Centre, axis)));

            // Cut into the fewest parts that fit, by cumulative texels: halving can only yield 2, 4, 8... slices, and every
            // extra slice adds boundary regions where two slices both place input probes and the densities add.
            int parts = Math.Max(2, (int)Math.Ceiling(texels / (double)RadiosityTuning.MaxTexelsPerSlice));
            parts = Math.Min(parts, group.Count);

            int target = (int)Math.Ceiling(texels / (double)parts);
            int start = 0, running = 0;
            for (int i = 0; i < group.Count; i++)
            {
                running += group[i].AtlasWidth * group[i].AtlasHeight;
                bool last = i == group.Count - 1;
                if (!last && running < target)
                    continue;

                SplitSpatially(group.GetRange(start, i - start + 1), output, log);
                start = i + 1;
                running = 0;
                if (start >= group.Count)
                    break;
            }
            if (start < group.Count)
                SplitSpatially(group.GetRange(start, group.Count - start), output, log);
        }

        /// <summary>
        /// Give every instance a disjoint rect in its slice's atlas, shrinking rather than
        /// dropping when a slice fills up.
        /// </summary>
        private static void AllocateAtlases(
            List<List<RadiosityGeometry.Instance>> slices, Action<string> log)
        {
            for (int s = 0; s < slices.Count; s++)
            {
                var atlas = new RadiosityAtlas(AtlasSize);

                // The atlas's top-left 16x16 corner is RESERVED: the engine owns it. Texel (0,0) is the "no lightmap"
                // sentinel (a MODEL_PARAMS rect at the origin renders unmapped-dark), and no retail rect intersects
                // x<16 && y<16 (0 of 16,511 across SCI_Hub/CM3/Solace; rects at y=0 or x=0 outside it are common). Every
                // island we packed there rendered black or flickered with otherwise retail-parity bake data.
                atlas.TryAllocate(16, 16, out _, out _);

                // Largest first (skyline packers waste far less that way). Donors pack after every real island and are
                // DROPPED on overflow, never parked: a donor squeezed onto the shared 1x1 would rasterise its whole island
                // into one texel, and packing donors first (H62) shrank 2,541 islands to save 1,430 donors. The delta path
                // selects donors to fit (MaxTexelsPerRetailSlice - groupTexels), so drops are rare.
                slices[s].Sort((a, b) => a.DonorOnly != b.DonorOnly
                    ? (a.DonorOnly ? 1 : -1)
                    : (b.AtlasWidth * b.AtlasHeight).CompareTo(a.AtlasWidth * a.AtlasHeight));

                int shrunk = 0, failed = 0;
                var droppedDonors = new List<RadiosityGeometry.Instance>();
                foreach (RadiosityGeometry.Instance instance in slices[s])
                {
                    int w = instance.AtlasWidth;
                    int h = instance.AtlasHeight;
                    bool placed = false;

                    while (w >= 1 && h >= 1)
                    {
                        if (instance.DonorOnly && (w < 2 || h < 2))
                            break;
                        if (atlas.TryAllocate(w, h, out int x, out int y))
                        {
                            instance.SliceIndex = s;
                            instance.AtlasX = x;
                            instance.AtlasY = y;
                            instance.AtlasWidth = w;
                            instance.AtlasHeight = h;
                            placed = true;
                            break;
                        }
                        if (w == 1 && h == 1)
                            break;
                        if (w >= h) w = Math.Max(1, w - 1); else h = Math.Max(1, h - 1);
                        shrunk++;
                    }

                    if (!placed)
                    {
                        if (instance.DonorOnly)
                        {
                            droppedDonors.Add(instance);
                            continue;
                        }
                        // Nothing left. Park it on a 1x1 at (16,0), just outside the reserved corner: the island still
                        // resolves to a valid texel and shares lighting with whatever else is there.
                        instance.SliceIndex = s;
                        instance.AtlasX = 16;
                        instance.AtlasY = 0;
                        instance.AtlasWidth = 1;
                        instance.AtlasHeight = 1;
                        failed++;
                    }
                }
                foreach (RadiosityGeometry.Instance dropped in droppedDonors)
                {
                    dropped.DonorOnly = false;
                    slices[s].Remove(dropped);
                }

                log?.Invoke("  slice " + s + " atlas: " + atlas.UsedTexels + "/" + AtlasTexels +
                            " texels used, " + shrunk + " shrunk, " + failed + " overflowed" +
                            (droppedDonors.Count > 0 ? ", " + droppedDonors.Count + " donors dropped" : ""));
            }
        }

        #endregion
    }
}
#endif
