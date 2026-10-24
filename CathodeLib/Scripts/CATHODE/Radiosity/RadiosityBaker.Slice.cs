#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;

namespace CathodeLib.Radiosity
{
    public static partial class RadiosityBaker
    {
        #region PER-SLICE BAKE

        /// <summary>
        /// Wall-clock phase times for the "timing" log lines (log only: nothing the bake writes reads it).
        /// Each <see cref="Lap"/> closes the phase that ran since the previous one, so the phases add up to the total.
        /// </summary>
        internal sealed class PhaseTimer
        {
            private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
            private readonly System.Text.StringBuilder _phases = new System.Text.StringBuilder();

            /// <summary>Seconds from the timer's start to the latest lap.</summary>
            public double Total { get; private set; }

            public void Lap(string phase, string detail = null)
            {
                double now = _clock.Elapsed.TotalSeconds;
                _phases.Append(phase).Append(' ').Append(Format(now - Total));
                if (detail != null) _phases.Append(" (").Append(detail).Append(')');
                _phases.Append(' ');
                Total = now;
            }

            /// <summary>The phases so far and their total, e.g. "collect 1.20s build 0.31s total 1.51s".</summary>
            public override string ToString() => _phases + "total " + Format(Total);

            public static string Format(double seconds) =>
                seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s";
        }

        /// <summary>
        /// What differs between the three kinds of slice bake: BakeLevel's full regeneration (<see cref="Full"/>) and the
        /// delta path's lightmap slices (<see cref="DeltaLightmap"/>, AppendDeltaSlices) and probe-only slices
        /// (<see cref="DeltaProbeOnly"/>, AppendProbeOnlySlice). Everything else is a <see cref="RadiosityTuning"/> constant.
        /// </summary>
        private sealed class BakeSliceOptions
        {
            /// <summary>
            /// The full path, whose last step (RebuildLinksRetailCut) rewrites every slice's clusters, links and scatter:
            /// the slice builds coarse clusters (<see cref="BuildCoarseClusters"/>), runs the solve as an existence test (no
            /// links written) and leaves its scatter empty. Off for a delta slice, which keeps its solve's links and gets the
            /// local scatter.
            /// </summary>
            public readonly bool FullBake;

            /// <summary>
            /// Input probes placed ON live atlas texels, Poisson-thinned (<see cref="TexelInputProbes"/>), instead of
            /// scattered over the surfaces (<see cref="ScatterInputProbes"/>). Retail is texel-coincident: ~90% of its input
            /// probes sit exactly on a cluster texel, each with a zero-distance scatter self-pair to that cluster. Delta
            /// slices only; the full bake keeps scattered probes (not re-validated against the campaign baselines).
            /// </summary>
            public readonly bool InputProbesOnTexels;

            /// <summary>Volume probe hash cell size in metres (<see cref="RadiosityTuning.VolumeProbeCellSize"/>, or
            /// <see cref="RadiosityTuning.DeltaVolumeProbeCellSize"/> on a probe-only slice).</summary>
            public readonly float VolumeProbeCellSize;

            /// <summary>
            /// The lost-emitter rescues (BuildLostEmitterLights, AddUnbakedEmitterLights) skip movers whose MVR Visible flag
            /// is off. ChallengeMap16 (17 Sep 2026): 40 invisible decal template quads took a real fixture's prior through
            /// the loose resource_id lookup and carried 1.4x the level's light table; retail lit only the visible one. On
            /// for the full bake (the validated profile's value); delta slices keep the old default, off.
            /// </summary>
            public readonly bool LostEmittersMustBeVisible;

            private BakeSliceOptions(bool fullBake, bool inputProbesOnTexels, float volumeProbeCellSize, bool lostEmittersMustBeVisible)
            {
                FullBake = fullBake;
                InputProbesOnTexels = inputProbesOnTexels;
                VolumeProbeCellSize = volumeProbeCellSize;
                LostEmittersMustBeVisible = lostEmittersMustBeVisible;
            }

            public static readonly BakeSliceOptions Full = new BakeSliceOptions(
                fullBake: true, inputProbesOnTexels: false, RadiosityTuning.VolumeProbeCellSize, lostEmittersMustBeVisible: true);
            public static readonly BakeSliceOptions DeltaLightmap = new BakeSliceOptions(
                fullBake: false, inputProbesOnTexels: true, RadiosityTuning.VolumeProbeCellSize, lostEmittersMustBeVisible: false);
            public static readonly BakeSliceOptions DeltaProbeOnly = new BakeSliceOptions(
                fullBake: false, inputProbesOnTexels: true, RadiosityTuning.DeltaVolumeProbeCellSize, lostEmittersMustBeVisible: false);
        }

        private sealed class SliceBake
        {
            public RadiosityRuntime.RuntimeDataSlice Slice;
            public int SurfaceProbeCount;
            public int InputProbeCount;
            public int ClusterCount;

            /// <summary>Traced 8x8 face grids, six per volume probe, in probe order.</summary>
            public List<byte[]> VisFaceGrids;

            /// <summary>Palette index per entry of <see cref="VisFaceGrids"/>, filled after dedup.</summary>
            public byte[] VisFaceIndices;

            /// <summary>Retained so the cross-slice pass can see this slice's probes.</summary>
            public SurfaceTexel[] Texels;

            /// <summary>
            /// Influence slots already used, keyed by surface probe slot so a delta slice's fixups fill the rest. A full
            /// bake's holds 1 or 0 (SolveInfluences' existence test) and nothing reads it.
            /// </summary>
            public byte[] UsedInfluenceSlots;

            /// <summary>Atlas texel -> input probe ordinal, or -1. Needed by the door pass.</summary>
            public int[] InputProbeForTexel;

            /// <summary>Atlas texel -> surface probe slot in the 256x64 probe texture, or -1.</summary>
            public int[] SurfaceSlotForTexel;

            /// <summary>Phase times of the bake (<see cref="PhaseTimer"/>, log only) and their total in seconds.</summary>
            public string Timing;
            public double Seconds;
        }

        /// <summary>One atlas texel that an instance claimed, with the surface it samples.</summary>
        private struct SurfaceTexel
        {
            public Vector3 Position;
            public Vector3 Normal;
            /// <summary>Averaged over the texel's footprint once <see cref="FoldAlbedo"/> has run.</summary>
            public Vector3 Albedo;
            public Vector3 AlbedoSum;
            public int AlbedoTaps;
            public Vector3 Emissive;
            public int MoverIndex;
            public bool Live;

            /// <summary>
            /// A live texel of a donor rect, or a corner cell filled by <see cref="FillAtlasGutters"/>:
            /// a cluster (emitter) and an input-probe binding site but never a surface probe, matching
            /// retail, whose live-cell counts exceed its rect sums by ~12% while its surface probes
            /// stay below them.
            /// </summary>
            public bool ClusterOnly;
            /// <summary>Where visibility rays start; see RadiosityGeometry.VisibilityOrigin.</summary>
            public Vector3 RayOrigin;
        }

        /// <summary>
        /// Lift every live texel's ray origin clear of the occluder shell it sits inside. Done once
        /// for the slice so the influence solve, which tests every receiver against every nearby
        /// emitter, does not repeat the same projection ray millions of times.
        /// </summary>
        private static void ResolveRayOrigins(RadiosityGeometry geometry, SurfaceTexel[] texels)
        {
            for (int i = 0; i < AtlasTexels; i++)
            {
                if (!texels[i].Live) continue;
                texels[i].RayOrigin = geometry.VisibilityOrigin(
                    texels[i].Position, texels[i].Normal,
                    RadiosityTuning.OccluderProjectionRange, RadiosityTuning.ProbeSurfaceOffset);
            }
        }

        /// <summary>Bake one slice from its instances.</summary>
        /// <param name="options">The kind of slice: BakeLevel's (<see cref="BakeSliceOptions.Full"/>) or a delta slice's.</param>
        private static SliceBake BakeSlice(
            Level level,
            RadiosityGeometry geometry,
            List<RadiosityGeometry.Instance> instances,
            int sliceIndex,
            BakeSliceOptions options,
            Dictionary<int, float> emissiveAreas,
            RetailLightPriors lightPriors,
            Action<string> log)
        {
            var timer = new PhaseTimer();
            var slice = new RadiosityRuntime.RuntimeDataSlice();
            var texels = new SurfaceTexel[AtlasTexels];

            // ---- 1. Rasterise each instance's geometry into its atlas rect -------------------
            int frontmostTakeovers = 0;
            foreach (RadiosityGeometry.Instance instance in instances)
                RasteriseInstance(geometry, instance, texels, ref frontmostTakeovers);
            log?.Invoke("    stacked charts: " + frontmostTakeovers + " texels re-claimed by a nearer parallel skin");

            // Donor rects join the cluster/emitter field only: live for input probes, scatter and
            // light injection, but no surface probes and no diets - nothing renders from them.
            foreach (RadiosityGeometry.Instance instance in instances)
            {
                if (!instance.DonorOnly) continue;
                for (int y = instance.AtlasY; y < instance.AtlasY + instance.AtlasHeight; y++)
                    for (int x = instance.AtlasX; x < instance.AtlasX + instance.AtlasWidth; x++)
                    {
                        int t = y * AtlasSize + x;
                        if (texels[t].Live) texels[t].ClusterOnly = true;
                    }
            }
            FoldAlbedo(texels);
            ApplyEmissiveAlbedoConvention(level, texels, lightPriors, log);
            ResolveRayOrigins(geometry, texels);

            // The engine-owned 16x16 corner must be fully live (see FillAtlasGutters). Retail's atlases have almost no dead
            // cells (ChallengeMap4 slice 0 is a full 16384-cell grid) where our packer leaves 12-45% dead gutter between
            // rects; filling that gutter too was built and left off.
            FillAtlasGutters(texels, log);

            int liveCount = 0;
            for (int i = 0; i < AtlasTexels; i++) if (texels[i].Live) liveCount++;
            timer.Lap("raster");

            // ---- 2. Surface probes: live texels compacted into 16x16 tiles --------------------
            // Surface probes are NOT atlas-indexed. They are a compacted list packed into the same
            // 256x64 tiled texture as the input probes, which is why the surface probe tree's leaf
            // rects predict the live set exactly in all 128 retail slices, and why the influence
            // maps key on a probe slot rather than an atlas texel.
            var surfaceOrder = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && !texels[i].ClusterOnly) surfaceOrder.Add(i);
            SpatialSort(surfaceOrder, i => texels[i].Position, pow2Cuts: true);
            if (surfaceOrder.Count > MaxSurfaceProbes)
                surfaceOrder.RemoveRange(MaxSurfaceProbes, surfaceOrder.Count - MaxSurfaceProbes);

            var surfaceSlotForTexel = new int[AtlasTexels];
            for (int i = 0; i < AtlasTexels; i++) surfaceSlotForTexel[i] = -1;

            slice.SurfaceProbePositions = new List<Vector4>(AtlasTexels);
            for (int i = 0; i < AtlasTexels; i++)
                slice.SurfaceProbePositions.Add(UnusedSurfaceProbe);

            for (int p = 0; p < surfaceOrder.Count; p++)
            {
                int slot = ProbeSlot(p);
                surfaceSlotForTexel[surfaceOrder[p]] = slot;
                slice.SurfaceProbePositions[slot] = new Vector4(texels[surfaceOrder[p]].Position + texels[surfaceOrder[p]].Normal * RadiosityTuning.SurfaceProbeLift, ProbeNormalisation);
            }

            // ---- 3. Input probes: emitters repacked into 16x16 tiles -------------------------
            var liveTexels = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live) liveTexels.Add(i);

            // Input probes are retail-style: a very large number of candidate points over every surface, thinned to a
            // Poisson-disc spacing (ScatterInputProbes), so emitter coverage does not depend on how evenly the authored UV
            // packing spreads atlas texels (it left a third of the cells retail fills with no probe of ours). Retail's
            // input probes are also TEXEL-COINCIDENT (~90% sit on a live cluster texel, each with a zero-distance scatter
            // self-pair), which free scattering never reproduces (4 of 2,231 on the CM7 delta slice), so delta slices
            // place them ON live texels instead (TexelInputProbes).
            List<ProbePoint> inputProbes = options.InputProbesOnTexels
                ? TexelInputProbes(texels, liveTexels)
                : ScatterInputProbes(geometry, instances);

            // Order spatially before assigning tile slots, so a 16x16 tile holds probes that are
            // near each other in the world. The probe tree's leaves are those same tiles, so their
            // bounds stay tight - which is what retail's tree looks like.
            SpatialSortPoints(inputProbes);

            if (inputProbes.Count > MaxInputProbes)
                inputProbes.RemoveRange(MaxInputProbes, inputProbes.Count - MaxInputProbes);
            timer.Lap("probes");

            // Every live texel reads bounced light from, and scatters into, its nearest probe.
            int[] nearestProbeForTexel = BuildNearestProbeMap(geometry, texels, liveTexels, inputProbes);
            timer.Lap("nearest");

            slice.InputProbePositions = NewList<Vector4u16>(AtlasTexels);
            slice.InputProbeNormals = NewList<ColourRGBA8>(AtlasTexels);
            slice.InputProbeAlbedo = NewList<ColourRGBA8>(AtlasTexels);

            slice.InputProbeTiles = new List<RadiosityRuntime.ProbeTileDims>();
            for (int p = 0; p < inputProbes.Count; p++)
            {
                InputProbeTexel(p, out int px, out int py);
                int dest = py * ProbeTexWidth + px;
                ProbePoint src = inputProbes[p];
                slice.InputProbePositions[dest] = ToHalf4(src.Position + src.Normal * RadiosityTuning.InputProbeLift, ProbeNormalisation);
                slice.InputProbeNormals[dest] = EncodeNormal(src.Normal);
                slice.InputProbeAlbedo[dest] = EncodeAlbedo(src.Albedo, 255);
            }
            BuildInputProbeTiles(inputProbes.Count, slice.InputProbeTiles);

            // ---- 4. Clusters -----------------------------------------------------------------
            // Clusters are the emitters, indexed by atlas texel, and every live texel is one (thinning them would throw away
            // emitted light; retail carries more clusters than either probe set). A cluster with no input probe of its own
            // scatters into its nearest one.
            slice.ClusterPositions = new List<Vector4u16>(AtlasTexels);
            for (int i = 0; i < AtlasTexels; i++)
            {
                bool isCluster = texels[i].Live && nearestProbeForTexel[i] >= 0;
                if (!isCluster) { slice.ClusterPositions.Add(new Vector4u16()); continue; }
                slice.ClusterPositions.Add(ToHalf4(texels[i].Position, 1.0f));
            }

            // ---- 5. Mangle map: atlas texel -> SURFACE PROBE SLOT ----------------------------
            // The lightmap reconstruction map: the engine builds the 128x128 lightmap by sampling the compacted 256x64
            // surface-probe radiance texture at the slot each entry names (decoded by adjacency: neighbouring retail texels
            // land on surface probes a median 1.06 m apart under the slot reading). Every entry resolves to a source, as in
            // all 128 retail slices: a "no source" dead texel rendered black wherever UVs strayed past the island and its
            // dilation. Dead texels take the nearest live texel's source, within their own instance's rect first.
            int[] mangleSource = BuildMangleSources(surfaceSlotForTexel, instances);
            slice.MangleMap = NewList<ColourRGBA8>(AtlasTexels);
            for (int i = 0; i < AtlasTexels; i++)
            {
                int sourceTexel = mangleSource[i];
                if (sourceTexel < 0)
                {
                    // Only reachable when the slice has no live texels at all.
                    slice.MangleMap[i] = new ColourRGBA8 { R = 255, G = 63, B = 255, A = 63 };
                    continue;
                }
                int slot = surfaceSlotForTexel[sourceTexel];
                int px = slot % ProbeTexWidth, py = slot / ProbeTexWidth;
                // (B, A) is NOT a second source: indexed by input-probe slot it is the probe's feedback
                // address (the atlas texel whose lightmap value feeds back into the probe), written
                // last, in the input-probe feedback block below.
                slice.MangleMap[i] = new ColourRGBA8 { R = (byte)px, G = (byte)py, B = 255, A = 63 };
            }

            // ---- 5b. Coarse clusters (retail's hierarchy, first rung) ------------------------
            List<CoarseCluster> coarseClusters = options.FullBake
                ? BuildCoarseClusters(texels, inputProbes, slice, log)
                : null;
            timer.Lap("clusters");

            // ---- 6. Visibility solve: influences per surface probe ---------------------------
            // On a full bake the cut rewrites every link under its own gain envelope, so of the solve only its zero-link set
            // (the repoint below) survives: the full bake runs it as an existence test and writes no links.
            int influenceCount = SolveInfluences(geometry, texels, surfaceSlotForTexel, nearestProbeForTexel, slice, out byte[] usedSlots, log, coarseClusters,
                                                 existenceOnly: options.FullBake);
            timer.Lap("solve");

            // A texel whose probe came out of the solve with ZERO influences renders BLACK, and the mangle map's bilinear
            // read smears that across the rect (CM3 cam16's wall terminal: 2 of 16 texels gathering nothing drew a dark
            // bar; retail has no zero-influence probes). The map was built before the solve, so re-point those texels at
            // the nearest neighbour that did gather, as unclaimed texels are.
            var litSlotForTexel = (int[])surfaceSlotForTexel.Clone();
            int dead = 0;
            for (int i = 0; i < AtlasTexels; i++)
            {
                int slot = surfaceSlotForTexel[i];
                if (slot >= 0 && slot < usedSlots.Length && usedSlots[slot] == 0)
                {
                    litSlotForTexel[i] = -1;
                    dead++;
                }
            }
            if (dead > 0)
            {
                int[] litSource = BuildMangleSources(litSlotForTexel, instances);
                int repointed = 0;
                for (int i = 0; i < AtlasTexels; i++)
                {
                    if (litSlotForTexel[i] >= 0) continue;   //still lit, leave it alone
                    int src = litSource[i];
                    if (src < 0) continue;                   //nothing lit anywhere to borrow from
                    int slot = litSlotForTexel[src];
                    if (slot < 0) continue;
                    int px2 = slot % ProbeTexWidth, py2 = slot / ProbeTexWidth;
                    slice.MangleMap[i] = new ColourRGBA8 { R = (byte)px2, G = (byte)py2, B = 255, A = 63 };
                    repointed++;
                }
                log?.Invoke("    dead texels: " + dead + " zero-influence probes, " + repointed +
                            " texels repointed to the nearest lit neighbour");
            }
            timer.Lap("gain");

            // ---- Input-probe feedback address (decoded 18 Sep 2026) ---------------------------
            // The mangle map is two tables in one: indexed by ATLAS TEXEL, (R, G) is the surface probe slot the texel reads
            // (built above); indexed by INPUT-PROBE SLOT, (B, A) is the 128-wide atlas texel the probe sits on (retail: a
            // surface probe 0.30 m p50 from the probe, i.e. its own texel). The engine feeds the lightmap back through it:
            // probe radiance = albedo x lightmap(B, A) + injected light. The constant (255, 63) we used to write gave no
            // probe a bounce, and it is not an inert value (its uv reads outside the probe range). Written last, after
            // every (R, G) rewrite.
            if (inputProbes.Count > 0)
            {
                var texelForProbe = new int[inputProbes.Count];
                var bestD = new float[inputProbes.Count];
                for (int p = 0; p < inputProbes.Count; p++) { texelForProbe[p] = -1; bestD[p] = float.MaxValue; }
                foreach (int t in liveTexels)
                {
                    int p = nearestProbeForTexel[t];
                    // Only texels with a SURFACE probe carry a lightmap value to feed back; a donor-shell
                    // (ClusterOnly) texel's (R,G) resolves through the atlas-nearest live texel of some other
                    // island, so a probe elected only by donor texels would bounce a stranger's lightmap.
                    if (surfaceSlotForTexel[t] < 0) continue;
                    if (p < 0 || p >= inputProbes.Count) continue;
                    float d = Vector3.DistanceSquared(texels[t].Position, inputProbes[p].Position);
                    if (d < bestD[p]) { bestD[p] = d; texelForProbe[p] = t; }
                }
                int written = 0, fallback = 0; double sumD = 0;
                for (int p = 0; p < inputProbes.Count; p++)
                {
                    int t = texelForProbe[p];
                    bool isFallback = false;
                    if (t < 0)
                    {
                        // No texel elected this probe as its nearest: take the nearest live texel.
                        float bd = float.MaxValue;
                        foreach (int lt in liveTexels)
                        {
                            if (surfaceSlotForTexel[lt] < 0) continue;
                            float d = Vector3.DistanceSquared(texels[lt].Position, inputProbes[p].Position);
                            if (d < bd) { bd = d; t = lt; }
                        }
                        if (t < 0) continue;
                        bestD[p] = bd; isFallback = true;
                    }
                    if (isFallback) fallback++;
                    InputProbeTexel(p, out int px, out int py);
                    int slot = py * ProbeTexWidth + px;
                    if (slot < 0 || slot >= slice.MangleMap.Count) continue;
                    ColourRGBA8 mm = slice.MangleMap[slot];
                    mm.B = (byte)(t % AtlasSize);
                    mm.A = (byte)(t / AtlasSize);
                    slice.MangleMap[slot] = mm;
                    written++; sumD += Math.Sqrt(bestD[p]);
                }
                log?.Invoke("    input-probe feedback addresses: " + written + " of " + inputProbes.Count + " probes written (" + (written - fallback) + " elected by their own texels, " + fallback + " by the nearest surface texel; mean " + (written > 0 ? sumD / written : 0).ToString("0.00") + " m)");
            }
            timer.Lap("feedback");
            // Name the instances that came out of the solve with no lit texel at all: a wholly unlinked rect renders solid
            // black however healthy the rest of the slice is.
            if (log != null)
            {
                int unlitInstances = 0, unlitTexels = 0;
                var worst = new List<(int area, int live, string what)>();
                foreach (RadiosityGeometry.Instance inst in instances)
                {
                    // Donors have no surface probes by design - "unlit" is their normal state.
                    if (inst.DonorOnly) continue;
                    int live2 = 0, lit = 0;
                    for (int y = inst.AtlasY; y < inst.AtlasY + inst.AtlasHeight; y++)
                    {
                        for (int x = inst.AtlasX; x < inst.AtlasX + inst.AtlasWidth; x++)
                        {
                            int t = y * AtlasSize + x;
                            if (t < 0 || t >= AtlasTexels || !texels[t].Live) continue;
                            live2++;
                            int slot = surfaceSlotForTexel[t];
                            if (slot >= 0 && usedSlots[slot] > 0) lit++;
                        }
                    }
                    if (live2 == 0 || lit > 0) continue;
                    unlitInstances++;
                    unlitTexels += live2;
                    worst.Add((inst.AtlasWidth * inst.AtlasHeight, live2,
                               "island " + inst.RetailIslandId + " composite " + inst.CompositeInstanceID +
                               " rect " + inst.AtlasWidth + "x" + inst.AtlasHeight +
                               " at (" + inst.Centre.X.ToString("0.0") + "," + inst.Centre.Y.ToString("0.0") +
                               "," + inst.Centre.Z.ToString("0.0") + ")"));
                }
                if (unlitInstances > 0)
                {
                    worst.Sort((a, b) => b.area.CompareTo(a.area));
                    log("  slice " + sliceIndex + ": " + unlitInstances + " of " + instances.Count +
                        " instances have no lit texel (" + unlitTexels + " live texels dark)");
                    for (int i = 0; i < Math.Min(8, worst.Count); i++)
                        log("      UNLIT " + worst[i].what + " live=" + worst[i].live);
                }
            }

            // The scatter list is the same data viewed from the input probe's side. A full bake leaves it empty: the cut
            // writes every slice's scatter as the membership of its own clusters.
            if (!options.FullBake)
                slice.Scatter = BuildScatterListLocal(geometry, inputProbes, texels, nearestProbeForTexel);
            timer.Lap("scatter");

            // ---- 7. Probe trees --------------------------------------------------------------
            // The input tree's leaf quads are what the engine draws to run its per-probe passes, so
            // they define the region of the 256x64 textures that is cleared and updated.
            // Tree bounds enclose the STORED (lifted) positions, or a lifted probe can sit outside its leaf.
            slice.InputProbeTreeNodes = BuildProbeTree(inputProbes.Count,
                i => inputProbes[i].Position + inputProbes[i].Normal * RadiosityTuning.InputProbeLift, out List<uint> inputQuads);
            slice.InputProbeTreeQuads = inputQuads;

            // The surface tree's leaves are the surface probe tiles, in the same order the probes
            // were packed in step 2.
            slice.SurfaceProbeTreeNodes = BuildProbeTree(surfaceOrder.Count,
                i => texels[surfaceOrder[i]].Position + texels[surfaceOrder[i]].Normal * RadiosityTuning.SurfaceProbeLift, out List<uint> surfaceQuads);
            slice.SurfaceProbeTreeQuads = surfaceQuads;
            timer.Lap("trees");

            // ---- 8. Volume probe hash for dynamic objects ------------------------------------
            // The hash box must cover the slice's CONSUMER space, not just its probe sources: dynamic movers sample it from
            // the air of every room, and retail's boxes reach well above the probes (CM3 slice 0 to y 12.8 with no probe
            // above 6.8). The slice's instance geometry bounds are the available proxy.
            float geoMinY = float.MaxValue, geoMaxY = float.MinValue;
            foreach (RadiosityGeometry.Instance inst in instances)
            {
                if (inst.BoundsMin.Y < geoMinY) geoMinY = inst.BoundsMin.Y;
                if (inst.BoundsMax.Y > geoMaxY) geoMaxY = inst.BoundsMax.Y;
            }

            // Absent hash = subdivisions 0, never 3: the engine faults on subdivisions 3 with no items
            // (RadiosityRuntime.VolumeProbeHash).
            slice.VolumeProbeHash = BuildVolumeProbeHash(geometry, texels, nearestProbeForTexel, surfaceSlotForTexel, options.VolumeProbeCellSize, geoMinY, geoMaxY, out List<byte[]> visGrids);
            timer.Lap("volume");

            // ---- 9. Emissive geometry becomes surface lights ---------------------------------
            slice.SurfaceLights = BuildSurfaceLights(level, geometry, instances, texels, nearestProbeForTexel, options.LostEmittersMustBeVisible, emissiveAreas, lightPriors, log);
            slice.LiveSurfaceLights = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>(slice.SurfaceLights.LightSlices);
            slice.LiveSurfaceLightEntities = new List<Resources.Resource>(slice.SurfaceLights.LightSliceEntities);

            // The tiled variants of scatter, surface lights and doors are not decoded; left empty (valid).
            slice.TiledScatter = new RadiosityRuntime.TiledScatterData();
            slice.TiledSurfaceLights = new RadiosityRuntime.TiledSurfaceLights();
            // Doors are filled in by BuildDoors once every slice exists.
            slice.Doors = new RadiosityRuntime.DoorInfo();
            slice.TiledDoors = new RadiosityRuntime.TiledDoorInfo();

            log?.Invoke("  slice " + sliceIndex + ": surfaceProbes=" + liveCount +
                        " inputProbes=" + inputProbes.Count +
                        (options.FullBake ? " linkedProbes=" : " influences=") + influenceCount +
                        " surfaceLights=" + slice.SurfaceLights.Lights.Count +
                        " lightSlices=" + slice.SurfaceLights.LightSlices.Count);
            WarnOverEngineLimits(slice, sliceIndex, log);
            timer.Lap("lights");

            return new SliceBake
            {
                Slice = slice,
                Timing = timer.ToString(),
                Seconds = timer.Total,
                // Real surface probes (donor/gutter cluster-only texels excluded): liveCount here
                // read "7951 surface probes" for a 4-tiny-island delta slice once donors joined.
                SurfaceProbeCount = surfaceOrder.Count,
                InputProbeCount = inputProbes.Count,
                ClusterCount = liveCount,
                VisFaceGrids = visGrids,
                VisFaceIndices = new byte[visGrids.Count],
                Texels = texels,
                UsedInfluenceSlots = usedSlots,
                InputProbeForTexel = nearestProbeForTexel,
                SurfaceSlotForTexel = surfaceSlotForTexel
            };
        }

        #endregion

        #region PROBE PLACEMENT

        /// <summary>
        /// Report any slice array that exceeds the size its structure allows.
        /// </summary>
        private static void WarnOverEngineLimits(RadiosityRuntime.RuntimeDataSlice slice, int sliceIndex, Action<string> log)
        {
            if (log == null)
                return;

            void Check(string what, int value, int max)
            {
                if (value > max)
                    log("  WARNING: slice " + sliceIndex + " has " + value + " " + what +
                        ", over the maximum of " + max);
            }

            Check("input probe tiles", slice.InputProbeTiles.Count, MaxInputProbeTiles);
            Check("input probe tree nodes", slice.InputProbeTreeNodes.Count, MaxInputProbeTreeNodes);
            Check("input quad verts", slice.InputProbeTreeQuads.Count, MaxInputQuadVerts);
            Check("output probe tree nodes", slice.SurfaceProbeTreeNodes.Count, MaxOutputProbeTreeNodes);
            Check("output quad verts", slice.SurfaceProbeTreeQuads.Count, MaxOutputQuadVerts);
            Check("surface light slices", slice.SurfaceLights.LightSlices.Count, MaxSurfaceLightSlices);
            Check("surface light probes", slice.SurfaceLights.Lights.Count, MaxSurfaceLightProbes);
            Check("doors", slice.Doors.Doors.Count, MaxDoorsPerSlice);
            Check("door transfers", slice.Doors.Transfers.Count, MaxDoorTransfersPerSlice);
        }

        /// <summary>An input probe: a point on a surface, with what that surface reflects.</summary>
        private struct ProbePoint
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector3 Albedo;
        }

        /// <summary>
        /// Input probes placed ON live atlas texels, Poisson-thinned to the configured spacing.
        /// Because the positions are the texel positions verbatim, the half-float encodes come
        /// out identical to ClusterPositions and the local scatter pass emits the zero-distance
        /// cluster/probe self-pairs retail ships (see the call site).
        /// </summary>
        private static List<ProbePoint> TexelInputProbes(SurfaceTexel[] texels, List<int> liveTexels)
        {
            float spacing = RadiosityTuning.InputProbeSpacing;
            float spacingSq = spacing * spacing;
            float cell = spacing;
            var accepted = new Dictionary<(int, int, int), List<Vector3>>();
            var probes = new List<ProbePoint>();
            foreach (int i in liveTexels)
            {
                Vector3 p = texels[i].Position;
                int cx = (int)Math.Floor(p.X / cell), cy = (int)Math.Floor(p.Y / cell), cz = (int)Math.Floor(p.Z / cell);
                bool blocked = false;
                for (int dx = -1; dx <= 1 && !blocked; dx++)
                    for (int dy = -1; dy <= 1 && !blocked; dy++)
                        for (int dz = -1; dz <= 1 && !blocked; dz++)
                        {
                            if (!accepted.TryGetValue((cx + dx, cy + dy, cz + dz), out List<Vector3> list))
                                continue;
                            foreach (Vector3 q in list)
                                if (Vector3.DistanceSquared(p, q) < spacingSq) { blocked = true; break; }
                        }
                if (blocked)
                    continue;
                if (!accepted.TryGetValue((cx, cy, cz), out List<Vector3> mine))
                    accepted[(cx, cy, cz)] = mine = new List<Vector3>();
                mine.Add(p);
                probes.Add(new ProbePoint { Position = p, Normal = texels[i].Normal, Albedo = texels[i].Albedo });
            }
            return probes;
        }

        /// <summary>
        /// Scatter candidate probes over every triangle of a slice, then thin them to a
        /// Poisson-disc spacing.
        /// </summary>
        private static List<ProbePoint> ScatterInputProbes(
            RadiosityGeometry geometry,
            List<RadiosityGeometry.Instance> instances)
        {
            float spacing = RadiosityTuning.InputProbeSpacing;
            float perSquareMetre = RadiosityTuning.InputProbeCandidatesPerSquareMetre;

            // Dart-throwing needs a scrambled visit order, so candidates are accumulated per
            // triangle and then walked in a hashed sequence rather than in geometry order. Only the
            // candidates that win a probe are sampled for albedo (dart throwing reads positions only),
            // so each keeps its triangle and diffuse UV until then.
            var candidates = new List<ProbePoint>();
            var candidateTri = new List<int>();
            var candidateUv = new List<Vector2>();
            foreach (RadiosityGeometry.Instance instance in instances)
            {
                foreach (int tri in instance.Triangles)
                {
                    float area = geometry.TriangleArea(tri);
                    if (area <= 1e-7f)
                        continue;

                    // At least one candidate per triangle, so a small face is still represented
                    // and can win a probe if nothing nearby has taken the space.
                    int count = Math.Max(1, (int)Math.Round(area * perSquareMetre));
                    for (int s = 0; s < count; s++)
                    {
                        // Deterministic low-discrepancy point in the triangle.
                        float u = Fract((tri * 0.7548776662f) + s * 0.7548776662f);
                        float v = Fract((tri * 0.5698402910f) + s * 0.5698402910f);
                        if (u + v > 1.0f) { u = 1.0f - u; v = 1.0f - v; }

                        geometry.SamplePoint(tri, u, v, out Vector3 position, out Vector3 normal,
                                             out _, out Vector2 diffuseUv);
                        candidates.Add(new ProbePoint
                        {
                            Position = position,
                            Normal = normal,
                        });
                        candidateTri.Add(tri);
                        candidateUv.Add(diffuseUv);
                    }
                }
            }
            if (candidates.Count == 0)
                return new List<ProbePoint>();

            // Visit order: candidate indices by Scramble. Scramble is a bijection on uint32, so the keys are all different
            // and any correct sort, keyed or by comparison, gives this one order.
            var order = new int[candidates.Count];
            var scrambled = new uint[candidates.Count];
            for (int i = 0; i < order.Length; i++) { order[i] = i; scrambled[i] = Scramble(i); }
            Array.Sort(scrambled, order);

            float radiusSq = spacing * spacing;
            var grid = new Dictionary<(int, int, int), List<int>>();
            var accepted = new List<ProbePoint>();

            foreach (int index in order)
            {
                Vector3 p = candidates[index].Position;
                int cx = (int)Math.Floor(p.X / spacing), cy = (int)Math.Floor(p.Y / spacing), cz = (int)Math.Floor(p.Z / spacing);

                bool tooClose = false;
                for (int dx = -1; dx <= 1 && !tooClose; dx++)
                    for (int dy = -1; dy <= 1 && !tooClose; dy++)
                        for (int dz = -1; dz <= 1 && !tooClose; dz++)
                        {
                            if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int> bucket))
                                continue;
                            foreach (int other in bucket)
                                if (Vector3.DistanceSquared(accepted[other].Position, p) < radiusSq) { tooClose = true; break; }
                        }
                if (tooClose)
                    continue;

                var key = (cx, cy, cz);
                if (!grid.TryGetValue(key, out List<int> own)) grid[key] = own = new List<int>();
                own.Add(accepted.Count);
                ProbePoint won = candidates[index];
                won.Albedo = geometry.SampleAlbedo(candidateTri[index], candidateUv[index]);
                accepted.Add(won);
            }

            // Each probe keeps its own candidate's point albedo. CA stores the area-weighted mean of its samples within
            // ~0.25 m (p50 7-12/255 from ours, no bias); a footprint mean was built and left off.
            return accepted;
        }

        /// <summary>Order probes so neighbours in the list are neighbours in the world.</summary>
        private static void SpatialSortPoints(List<ProbePoint> probes)
        {
            if (probes.Count < 2)
                return;
            var index = new int[probes.Count];
            for (int i = 0; i < index.Length; i++) index[i] = i;
            var keys = new List<Vector3>(probes.Count);
            foreach (ProbePoint p in probes) keys.Add(p.Position);

            var order = new List<int>(index);
            SpatialSort(order, i => keys[i], pow2Cuts: true);

            var sorted = new List<ProbePoint>(probes.Count);
            foreach (int i in order) sorted.Add(probes[i]);
            probes.Clear();
            probes.AddRange(sorted);
        }

        /// <summary>Deterministic integer scramble, for a stable pseudo-random visit order.</summary>
        private static uint Scramble(int value)
        {
            uint x = (uint)value * 2654435761u;
            x ^= x >> 15;
            x *= 2246822519u;
            x ^= x >> 13;
            return x;
        }

        /// <summary>
        /// A mangle-map source texel for every atlas texel. Live texels source themselves; dead
        /// texels flood-fill from the nearest live texel, staying inside their instance's rect
        /// while any of its texels are live so islands do not bleed into each other, then filling
        /// from anywhere so no texel is left unresolved.
        /// </summary>
        private static int[] BuildMangleSources(
            int[] surfaceSlotForTexel, List<RadiosityGeometry.Instance> instances)
        {
            var source = new int[AtlasTexels];
            var owner = new int[AtlasTexels];
            for (int i = 0; i < AtlasTexels; i++)
            {
                // A live texel sources itself; dead texels inherit a live neighbour's texel via
                // the BFS below, and the writer maps the source texel to its surface probe slot.
                source[i] = surfaceSlotForTexel[i] >= 0 ? i : -1;
                owner[i] = -1;
            }
            for (int inst = 0; inst < instances.Count; inst++)
            {
                RadiosityGeometry.Instance instance = instances[inst];
                for (int y = instance.AtlasY; y < Math.Min(AtlasSize, instance.AtlasY + instance.AtlasHeight); y++)
                    for (int x = instance.AtlasX; x < Math.Min(AtlasSize, instance.AtlasX + instance.AtlasWidth); x++)
                        owner[y * AtlasSize + x] = inst;
            }

            // Multi-source BFS from the live texels. First pass propagates only within the owning
            // instance's rect; the second unconstrained pass mops up rects with no live texels at
            // all and the atlas gaps between rects.
            var queue = new Queue<int>();
            for (int pass = 0; pass < 2; pass++)
            {
                queue.Clear();
                for (int i = 0; i < AtlasTexels; i++)
                    if (source[i] >= 0)
                        queue.Enqueue(i);

                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    int x = i % AtlasSize, y = i / AtlasSize;
                    foreach ((int dx, int dy) in DilationNeighbours)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || nx >= AtlasSize || ny < 0 || ny >= AtlasSize)
                            continue;
                        int n = ny * AtlasSize + nx;
                        if (source[n] >= 0)
                            continue;
                        if (pass == 0 && owner[n] != owner[i])
                            continue;
                        source[n] = source[i];
                        queue.Enqueue(n);
                    }
                }
            }
            return source;
        }

        /// <summary>
        /// Bind every live texel to an input probe. Scatter destinations, light-sample attribution
        /// and the volume hash key on this binding, so it must respect surface identity: the
        /// nearest probe by raw distance is routinely on the far side of a thin wall or floor.
        /// Candidates are therefore taken nearest-first but must be visible from the texel and
        /// roughly agree in normal; visible-only is the first fallback and plain nearest the last,
        /// so no texel is ever left unbound.
        /// </summary>
        private static int[] BuildNearestProbeMap(RadiosityGeometry geometry, SurfaceTexel[] texels,
            List<int> liveTexels, List<ProbePoint> probes)
        {
            var map = new int[AtlasTexels];
            for (int i = 0; i < AtlasTexels; i++) map[i] = -1;
            if (probes.Count == 0)
                return map;

            // Grid over the probes, sized so a handful land in each cell.
            Vector3 lo = probes[0].Position, hi = lo;
            foreach (ProbePoint t in probes)
            {
                lo = Vector3.Min(lo, t.Position);
                hi = Vector3.Max(hi, t.Position);
            }
            Vector3 extent = hi - lo;
            float cell = Math.Max(0.25f, (Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) + 1e-3f) /
                                          Math.Max(1, (float)Math.Pow(probes.Count, 1.0 / 3.0)));

            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) Key(Vector3 v) =>
                ((int)Math.Floor(v.X / cell), (int)Math.Floor(v.Y / cell), (int)Math.Floor(v.Z / cell));

            for (int p = 0; p < probes.Count; p++)
            {
                var k = Key(probes[p].Position);
                if (!grid.TryGetValue(k, out List<int> bucket)) grid[k] = bucket = new List<int>();
                bucket.Add(p);
            }

            // How many nearest candidates to consider for the visibility/normal tiers. Past this
            // the texel takes the plain nearest rather than paying for more rays.
            const int MaxBindCandidates = 12;

            // Visibility rays to a probe must start clear of the surface the probe sits on, for the
            // same reason texel ray origins are lifted: a raw epsilon offset leaves the ray grazing
            // its own wall, and trim geometry falsely blocks bindings within a single room.
            var probeOrigins = new Vector3[probes.Count];
            for (int p = 0; p < probes.Count; p++)
                probeOrigins[p] = geometry.VisibilityOrigin(probes[p].Position, probes[p].Normal,
                    RadiosityTuning.OccluderProjectionRange, RadiosityTuning.ProbeSurfaceOffset);

            var found = new List<(int probe, float distSq)>();
            var bind = new int[MaxBindCandidates]; var bindDist = new float[MaxBindCandidates];
            foreach (int texel in liveTexels)
            {
                Vector3 p = texels[texel].Position;
                var c = Key(p);
                found.Clear();
                float nearestSq = float.MaxValue;

                // Expand the search ring until something is found, then one ring further so a
                // closer probe just outside the first hit's ring cannot be missed.
                int stopRing = int.MaxValue;
                for (int ring = 1; ring <= 24 && ring <= stopRing; ring++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                        for (int dy = -ring; dy <= ring; dy++)
                            for (int dz = -ring; dz <= ring; dz++)
                            {
                                // Only the shell, since the interior was covered by earlier rings.
                                if (ring > 1 && Math.Abs(dx) != ring && Math.Abs(dy) != ring && Math.Abs(dz) != ring)
                                    continue;
                                if (!grid.TryGetValue((c.Item1 + dx, c.Item2 + dy, c.Item3 + dz), out List<int> bucket))
                                    continue;
                                foreach (int probe in bucket)
                                {
                                    float d = Vector3.DistanceSquared(probes[probe].Position, p);
                                    found.Add((probe, d));
                                    if (d < nearestSq) nearestSq = d;
                                }
                            }
                    if (stopRing == int.MaxValue && found.Count > 0 && nearestSq <= (ring * cell) * (ring * cell))
                        stopRing = ring + 1;
                }

                // A texel whose slice holds probes but none within 24 cells is pathological; fall
                // back to a linear scan rather than leaving it unlit.
                if (found.Count == 0)
                {
                    for (int probe = 0; probe < probes.Count; probe++)
                        found.Add((probe, Vector3.DistanceSquared(probes[probe].Position, p)));
                }

                // Only the nearest `tested` candidates are read, so they are selected without sorting the rest whenever
                // that gives what the sort would: see SelectNearest. A tie there falls back to the sort (unstable, so it
                // alone decides the order of equal distances), on the same list as before.
                int tested = Math.Min(found.Count, MaxBindCandidates);
                if (!SelectNearest(found, tested, bind, bindDist))
                {
                    found.Sort((a, b) => a.distSq.CompareTo(b.distSq));
                    for (int i = 0; i < tested; i++) bind[i] = found[i].probe;
                }

                Vector3 texelNormal = texels[texel].Normal;
                Vector3 rayOrigin = texels[texel].RayOrigin;
                int best = -1, firstNormal = -1, firstVisible = -1;
                for (int i = 0; i < tested; i++)
                {
                    ProbePoint candidate = probes[bind[i]];
                    bool normalAgrees = Vector3.Dot(texelNormal, candidate.Normal) > 0.3f;
                    if (normalAgrees && firstNormal < 0)
                        firstNormal = bind[i];
                    if (!geometry.Visible(rayOrigin, probeOrigins[bind[i]], RadiosityTuning.RayEpsilon))
                        continue;
                    if (firstVisible < 0)
                        firstVisible = bind[i];
                    if (normalAgrees)
                    {
                        best = bind[i];
                        break;
                    }
                }
                // A normal-agreeing probe whose ray happens to be blocked (clutter, concave trim)
                // still belongs to this surface; binding across to a visible but opposing-normal
                // probe is what actually bleeds light between rooms.
                if (best < 0) best = firstNormal;
                if (best < 0) best = firstVisible;
                if (best < 0) best = bind[0];

                map[texel] = best;
            }

            return map;
        }

        /// <summary>
        /// The <paramref name="k"/> nearest of <paramref name="found"/> into <paramref name="probe"/> (nearest first), without
        /// sorting the list, when that is exactly what sorting it by distance would put first: the k smallest distances are
        /// all different and every other candidate is strictly farther. Then every correct sort, stable or not, puts those k
        /// first in that order. Otherwise (a tie among them or at the k-th, or a NaN) it returns false and the caller sorts.
        /// </summary>
        private static bool SelectNearest(List<(int probe, float distSq)> found, int k, int[] probe, float[] dist)
        {
            int n = 0;   // dist[0..n) ascending
            for (int j = 0; j < found.Count; j++)
            {
                float d = found[j].distSq;
                if (float.IsNaN(d)) return false;
                if (n == k && d >= dist[k - 1]) continue;
                int at = n < k ? n++ : k - 1;
                for (; at > 0 && dist[at - 1] > d; at--) { dist[at] = dist[at - 1]; probe[at] = probe[at - 1]; }
                dist[at] = d; probe[at] = found[j].probe;
            }
            for (int i = 1; i < k; i++)
                if (!(dist[i - 1] < dist[i])) return false;
            if (found.Count > k)
            {
                int within = 0;
                for (int j = 0; j < found.Count; j++)
                    if (found[j].distSq <= dist[k - 1]) within++;
                if (within != k) return false;
            }
            return true;
        }

        #endregion

        #region RASTERISATION

        /// <summary>
        /// Fill an instance's atlas rect by rasterising its triangles in UV1 space. Meshes with no
        /// lightmap UVs fall back to scattering samples over the rect by triangle area.
        /// <paramref name="frontmostTakeovers"/> counts the texels a nearer parallel skin re-claimed
        /// (the stacked-chart rule in <see cref="RasteriseByUv"/>), for the "stacked charts" log line.
        /// </summary>
        private static void RasteriseInstance(RadiosityGeometry geometry, RadiosityGeometry.Instance instance,
                                              SurfaceTexel[] texels, ref int frontmostTakeovers)
        {
            // Uniform world-grid placement (probe-only slices): the samples ARE the texels, one
            // each, no dilation - dilation exists for bilinear lightmap edge reads, which these
            // slices never get, and its position-clones were 21.5% of the F5 probe count.
            if (instance.UniformSamples != null)
            {
                PlaceUniformSamples(geometry, instance, texels);
                return;
            }

            bool anyUv = false;
            foreach (int tri in instance.Triangles)
            {
                Vector2 a = geometry.LightmapUVs[geometry.Tris[tri * 3 + 0]];
                Vector2 b = geometry.LightmapUVs[geometry.Tris[tri * 3 + 1]];
                Vector2 c = geometry.LightmapUVs[geometry.Tris[tri * 3 + 2]];
                if (a != Vector2.Zero || b != Vector2.Zero || c != Vector2.Zero)
                {
                    anyUv = true;
                    break;
                }
            }

            if (anyUv)
            {
                RasteriseByUv(geometry, instance, texels, ref frontmostTakeovers);
                DilateIntoUnclaimed(instance, texels);
                return;
            }

            // With no lightmap UVs at all there is no island to grow out of, so the rect is filled
            // by area-weighted sampling instead.
            FillUnclaimed(geometry, instance, texels);
        }

        /// <summary>Orthogonal neighbours before diagonal ones, so a donor shares an edge if it can.</summary>
        private static readonly (int dx, int dy)[] DilationNeighbours =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)
        };

        /// <summary>
        /// How many texels <see cref="DilateIntoUnclaimed"/> grows an island. The bilinear filter reads
        /// only the ring immediately outside it, and two passes cover that with margin.
        /// </summary>
        private const int DilationPasses = 2;

        /// <summary>
        /// Grow the rasterised UV islands a few texels into the rect around them.
        /// </summary>
        private static void DilateIntoUnclaimed(RadiosityGeometry.Instance instance, SurfaceTexel[] texels)
        {
            if (instance.AtlasWidth <= 0 || instance.AtlasHeight <= 0)
                return;

            int x0 = instance.AtlasX, y0 = instance.AtlasY;
            int x1 = Math.Min(AtlasSize, x0 + instance.AtlasWidth);
            int y1 = Math.Min(AtlasSize, y0 + instance.AtlasHeight);

            for (int pass = 0; pass < DilationPasses; pass++)
            {
                var grown = new List<(int index, SurfaceTexel texel)>();

                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        int index = y * AtlasSize + x;
                        if (texels[index].Live)
                            continue;

                        // One donor, copied whole, not an average of the neighbours: a rect holds both faces of a thin
                        // panel, and an average across an island edge lands inside the geometry (1412 extra empty probes
                        // on Solace).
                        int donor = -1;
                        foreach ((int dx, int dy) in DilationNeighbours)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < x0 || nx >= x1 || ny < y0 || ny >= y1) continue;
                            int n = ny * AtlasSize + nx;
                            if (!texels[n].Live) continue;
                            donor = n;
                            break;
                        }

                        if (donor < 0)
                            continue;

                        SurfaceTexel source = texels[donor];
                        grown.Add((index, new SurfaceTexel
                        {
                            Position = source.Position,
                            Normal = source.Normal,
                            AlbedoSum = source.AlbedoSum,
                            AlbedoTaps = source.AlbedoTaps,
                            Emissive = source.Emissive,
                            MoverIndex = source.MoverIndex,
                            Live = true
                        }));
                    }
                }

                if (grown.Count == 0)
                    return;

                // Applied after the sweep so a pass grows by exactly one texel rather than running
                // away across the rect in whichever direction it happens to scan.
                foreach ((int index, SurfaceTexel texel) in grown)
                    texels[index] = texel;
            }
        }

        private static void RasteriseByUv(RadiosityGeometry geometry, RadiosityGeometry.Instance instance, SurfaceTexel[] texels,
                                          ref int frontmostTakeovers)
        {
            // All of a composite's movers share one atlas rect, each addressing it with its own 0..1 lightmap UVs, so the
            // first arrival must not keep every texel it touches: each mover gets a quota of dead texels it may claim. The
            // quota is the mover's own UV FOOTPRINT (MeasureFootprints), not its share of world area: footprints overlap
            // by a median 12.7% (ChallengeMap4), so the disjoint majority is never rationed and only genuinely contended
            // rects (repeated meshes sharing one UV layout) are shared out, proportionally. Area shares rationed
            // constantly and left individual walls 31 luma under retail and others 38 over.
            int rectTexels = Math.Max(1, instance.AtlasWidth * instance.AtlasHeight);
            var quota = new int[instance.Movers.Count];
            var claimed = new int[instance.Movers.Count];

            int[] footprint = MeasureFootprints(geometry, instance, quota.Length);
            long footprintTotal = 0;
            for (int m = 0; m < footprint.Length; m++) footprintTotal += footprint[m];

            for (int m = 0; m < quota.Length; m++)
            {
                if (footprint[m] <= 0)
                {
                    // No footprint measured but the mover does have surface: give it a texel
                    // rather than round it away, which is what the area rule did for small props.
                    float area = m < instance.MoverAreas.Count ? instance.MoverAreas[m] : 0.0f;
                    quota[m] = area > 0.0f ? 1 : 0;
                    continue;
                }
                quota[m] = footprintTotal <= rectTexels
                    ? footprint[m]
                    : Math.Max(1, (int)Math.Round((double)rectTexels * footprint[m] / footprintTotal));
            }

            foreach (int tri in instance.Triangles)
            {
                int moverSlot = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                if (moverSlot < 0 || moverSlot >= quota.Length)
                    moverSlot = 0;

                // The quota gates only the dead-texel claim below; dropping whole triangles starved late faces of texels
                // and threw away their albedo taps.

                int moverIndex = moverSlot < instance.Movers.Count ? instance.Movers[moverSlot] : -1;

                int i0 = geometry.Tris[tri * 3 + 0], i1 = geometry.Tris[tri * 3 + 1], i2 = geometry.Tris[tri * 3 + 2];
                Vector2 uv0 = ToRect(geometry.LightmapUVs[i0], instance);
                Vector2 uv1 = ToRect(geometry.LightmapUVs[i1], instance);
                Vector2 uv2 = ToRect(geometry.LightmapUVs[i2], instance);

                int minX = Math.Max(instance.AtlasX, (int)Math.Floor(Math.Min(uv0.X, Math.Min(uv1.X, uv2.X))));
                int maxX = Math.Min(instance.AtlasX + instance.AtlasWidth - 1, (int)Math.Ceiling(Math.Max(uv0.X, Math.Max(uv1.X, uv2.X))));
                int minY = Math.Max(instance.AtlasY, (int)Math.Floor(Math.Min(uv0.Y, Math.Min(uv1.Y, uv2.Y))));
                int maxY = Math.Min(instance.AtlasY + instance.AtlasHeight - 1, (int)Math.Ceiling(Math.Max(uv0.Y, Math.Max(uv1.Y, uv2.Y))));

                float denominator = (uv1.Y - uv2.Y) * (uv0.X - uv2.X) + (uv2.X - uv1.X) * (uv0.Y - uv2.Y);
                if (Math.Abs(denominator) < 1e-9f)
                    continue;

                // Barycentrics are affine in atlas space, so the sub-tap grid below only needs
                // these four gradients rather than a fresh solve per tap.
                float invDenominator = 1.0f / denominator;
                float a0 = (uv1.Y - uv2.Y) * invDenominator, b0 = (uv2.X - uv1.X) * invDenominator;
                float a1 = (uv2.Y - uv0.Y) * invDenominator, b1 = (uv0.X - uv2.X) * invDenominator;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        int index = y * AtlasSize + x;

                        // Albedo integrates over the texel on a sub-grid: a texel covers about half a square metre
                        // (Solace averages 53 triangles across one), and retail's table carries 16.5 samples per probe
                        // (33.7% of its probe albedos unique, against 5.7% from one tap). The taps also decide coverage
                        // below, and their barycentrics give a representative point inside the covered part.
                        float coveredS0 = 0.0f, coveredS1 = 0.0f;
                        int coveredTaps = 0;

                        for (int sy = 0; sy < AlbedoSubTaps; sy++)
                        {
                            float qy = y + (sy + 0.5f) / AlbedoSubTaps - uv2.Y;
                            for (int sx = 0; sx < AlbedoSubTaps; sx++)
                            {
                                float qx = x + (sx + 0.5f) / AlbedoSubTaps - uv2.X;
                                float s0 = a0 * qx + b0 * qy;
                                float s1 = a1 * qx + b1 * qy;
                                if (s0 < 0f || s1 < 0f || s0 + s1 > 1f)
                                    continue;
                                texels[index].AlbedoSum += geometry.SampleAlbedo(tri, geometry.DiffuseUvAt(tri, s1, 1f - s0 - s1));
                                texels[index].AlbedoTaps++;
                                coveredS0 += s0;
                                coveredS1 += s1;
                                coveredTaps++;
                            }
                        }

                        float px = x + 0.5f, py = y + 0.5f;
                        float l0 = a0 * (px - uv2.X) + b0 * (py - uv2.Y);
                        float l1 = a1 * (px - uv2.X) + b1 * (py - uv2.Y);
                        float l2 = 1.0f - l0 - l1;

                        // A small negative tolerance keeps seams between adjacent triangles filled.
                        const float bias = -0.02f;
                        if (l0 < bias || l1 < bias || l2 < bias)
                        {
                            // Conservative coverage: the centre is outside, but a sub-tap landed inside, so the triangle
                            // crosses this texel. Centres alone turned sub-texel islands into one live texel that
                            // dilation copied across the rect (half of BSP_TORRENS' models had every probe at one spot).
                            if (coveredTaps == 0)
                                continue;
                            l0 = coveredS0 / coveredTaps;
                            l1 = coveredS1 / coveredTaps;
                            l2 = 1.0f - l0 - l1;
                        }

                        l0 = Math.Max(0, l0); l1 = Math.Max(0, l1); l2 = Math.Max(0, l2);
                        float sum = l0 + l1 + l2;
                        if (sum <= 0) continue;
                        l0 /= sum; l1 /= sum; l2 /= sum;

                        // First covering triangle wins. A coverage-weighted reservoir re-draw for stacked charts
                        // regressed well-behaved islands (CM9 RecordsRoom 1.77 -> 8.40 rmse); only the nearer-skin rule
                        // below may take a live texel.
                        if (texels[index].Live)
                        {
                            // Stacked charts, nested skins (18 Sep 2026, Solace's Anesidora core: six movers share one
                            // rect, retail's texels sit on the visible inner liner, ours sat 0.6-5 m out on the hull skin).
                            // A later triangle with a near-parallel normal whose surface point lies IN FRONT of the current
                            // one along that normal (the skin an observer in the room sees) takes the texel. Won on every
                            // level A/B'd (e.g. ChallengeMap5 14.94 -> 13.88); a cross-mover-only form regressed.
                            if (coveredTaps > 0)   // same mover too: liner and hull are often elements of one mesh
                            {
                                geometry.SamplePoint(tri, l1, l2, out Vector3 fpos, out Vector3 fnormal, out _, out Vector2 fuv);
                                float facing = Vector3.Dot(fnormal, texels[index].Normal);
                                float advance = Vector3.Dot(fpos - texels[index].Position, texels[index].Normal);
                                if (facing > 0.8f && advance > 0.05f && advance < RadiosityTuning.PreferFrontmostSkinMaxDepth)
                                {
                                    texels[index].Position = fpos;
                                    texels[index].Normal = fnormal;
                                    texels[index].Emissive = geometry.TriangleEmissive[tri];
                                    texels[index].MoverIndex = moverIndex;
                                    texels[index].AlbedoSum = geometry.SampleAlbedo(tri, fuv);
                                    texels[index].AlbedoTaps = 1;
                                    frontmostTakeovers++;
                                    continue;
                                }
                            }
                            continue;
                        }
                        if (quota.Length > 0 && claimed[moverSlot] >= quota[moverSlot])
                            continue;
                        claimed[moverSlot]++;

                        geometry.SamplePoint(tri, l1, l2, out Vector3 position, out Vector3 normal, out _, out Vector2 diffuseUv);
                        texels[index].Position = position;
                        texels[index].Normal = normal;
                        texels[index].Emissive = geometry.TriangleEmissive[tri];
                        texels[index].MoverIndex = moverIndex;
                        texels[index].Live = true;

                        // Guarantee at least one tap, for a texel whose centre is covered but
                        // whose sub-grid all fell outside this triangle.
                        if (texels[index].AlbedoTaps == 0)
                        {
                            texels[index].AlbedoSum = geometry.SampleAlbedo(tri, diffuseUv);
                            texels[index].AlbedoTaps = 1;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// How many of the instance's rect texels each mover's lightmap UVs actually reach.
        /// </summary>
        private static int[] MeasureFootprints(RadiosityGeometry geometry, RadiosityGeometry.Instance instance, int slots)
        {
            var counts = new int[Math.Max(1, slots)];
            if (slots <= 0)
                return counts;

            int w = Math.Max(1, instance.AtlasWidth), h = Math.Max(1, instance.AtlasHeight);
            var seen = new bool[slots * w * h];

            foreach (int tri in instance.Triangles)
            {
                int slot = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                if (slot < 0 || slot >= slots)
                    slot = 0;

                int i0 = geometry.Tris[tri * 3 + 0], i1 = geometry.Tris[tri * 3 + 1], i2 = geometry.Tris[tri * 3 + 2];
                Vector2 uv0 = ToRect(geometry.LightmapUVs[i0], instance);
                Vector2 uv1 = ToRect(geometry.LightmapUVs[i1], instance);
                Vector2 uv2 = ToRect(geometry.LightmapUVs[i2], instance);

                int minX = Math.Max(instance.AtlasX, (int)Math.Floor(Math.Min(uv0.X, Math.Min(uv1.X, uv2.X))));
                int maxX = Math.Min(instance.AtlasX + w - 1, (int)Math.Ceiling(Math.Max(uv0.X, Math.Max(uv1.X, uv2.X))));
                int minY = Math.Max(instance.AtlasY, (int)Math.Floor(Math.Min(uv0.Y, Math.Min(uv1.Y, uv2.Y))));
                int maxY = Math.Min(instance.AtlasY + h - 1, (int)Math.Ceiling(Math.Max(uv0.Y, Math.Max(uv1.Y, uv2.Y))));

                float denominator = (uv1.Y - uv2.Y) * (uv0.X - uv2.X) + (uv2.X - uv1.X) * (uv0.Y - uv2.Y);
                if (Math.Abs(denominator) < 1e-9f)
                    continue;
                float invDenominator = 1.0f / denominator;
                float a0 = (uv1.Y - uv2.Y) * invDenominator, b0 = (uv2.X - uv1.X) * invDenominator;
                float a1 = (uv2.Y - uv0.Y) * invDenominator, b1 = (uv0.X - uv2.X) * invDenominator;

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        int cell = slot * w * h + (y - instance.AtlasY) * w + (x - instance.AtlasX);
                        if (cell < 0 || cell >= seen.Length || seen[cell])
                            continue;

                        float px = x + 0.5f, py = y + 0.5f;
                        float l0 = a0 * (px - uv2.X) + b0 * (py - uv2.Y);
                        float l1 = a1 * (px - uv2.X) + b1 * (py - uv2.Y);
                        float l2 = 1.0f - l0 - l1;
                        const float bias = -0.02f;
                        bool covered = l0 >= bias && l1 >= bias && l2 >= bias;

                        if (!covered)
                        {
                            for (int sy = 0; sy < AlbedoSubTaps && !covered; sy++)
                            {
                                float qy = y + (sy + 0.5f) / AlbedoSubTaps - uv2.Y;
                                for (int sx = 0; sx < AlbedoSubTaps; sx++)
                                {
                                    float qx = x + (sx + 0.5f) / AlbedoSubTaps - uv2.X;
                                    float s0 = a0 * qx + b0 * qy;
                                    float s1 = a1 * qx + b1 * qy;
                                    if (s0 < 0f || s1 < 0f || s0 + s1 > 1f)
                                        continue;
                                    covered = true;
                                    break;
                                }
                            }
                        }

                        if (!covered)
                            continue;
                        seen[cell] = true;
                        counts[slot]++;
                    }
                }
            }
            return counts;
        }

        /// <summary>
        /// Sample an island's triangles where they cross a world-axis-aligned grid at
        /// <paramref name="spacing"/>. Each triangle is walked on the grid of its normal's two
        /// non-dominant axes, so flat surfaces come out as literal lattices - which is what
        /// retail's probe fields look like - and the spacing is identical for every island.
        /// Coplanar neighbours sharing grid points dedupe on the grid key; the two faces of a
        /// thin panel stay separate on the normal-direction bit.
        /// </summary>
        private static List<RadiosityGeometry.UniformSurfaceSample> GridSampleInstance(
            RadiosityGeometry geometry, RadiosityGeometry.Instance instance, float spacing)
        {
            var samples = new List<RadiosityGeometry.UniformSurfaceSample>();
            var seen = new HashSet<(int, int, int, int)>();
            int moverCount = Math.Max(1, instance.Movers.Count);

            // Hidden faces: the authored lightmap charts leave a closed prop's inside, a plenum-side ceiling and a flush
            // panel's back UNMAPPED; skipping those triangles was tried with an above-surface hash fill (F9) and the pair
            // regressed several rooms, so both were reverted. Isolate before re-attempting either.
            var moverHasSample = new bool[moverCount];
            var moverHasEmissiveSample = new bool[moverCount];
            var moverBestTri = new int[moverCount];
            var moverBestArea = new float[moverCount];
            List<(int tri, float area)>[] emissiveTris = null;
            for (int m = 0; m < moverCount; m++) moverBestTri[m] = -1;

            float Coord(Vector3 p, int a) => a == 0 ? p.X : a == 1 ? p.Y : p.Z;

            void AddCentroid(int tri, int slot, bool emissive)
            {
                geometry.SamplePoint(tri, 1.0f / 3.0f, 1.0f / 3.0f,
                    out Vector3 pos, out Vector3 n, out _, out Vector2 duv);
                samples.Add(new RadiosityGeometry.UniformSurfaceSample
                { Tri = tri, Position = pos, Normal = n, DiffuseUv = duv });
                moverHasSample[slot] = true;
                if (emissive) moverHasEmissiveSample[slot] = true;
            }

            foreach (int tri in instance.Triangles)
            {
                int i0 = geometry.Tris[tri * 3 + 0], i1 = geometry.Tris[tri * 3 + 1], i2 = geometry.Tris[tri * 3 + 2];

                int slot = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                if (slot < 0 || slot >= moverCount) slot = 0;
                float area = geometry.TriangleArea(tri);
                if (area > moverBestArea[slot]) { moverBestArea[slot] = area; moverBestTri[slot] = tri; }
                bool emissive = tri < geometry.TriangleEmissive.Length &&
                                geometry.TriangleEmissive[tri] != Vector3.Zero;
                if (emissive)
                {
                    emissiveTris = emissiveTris ?? new List<(int, float)>[moverCount];
                    (emissiveTris[slot] = emissiveTris[slot] ?? new List<(int, float)>()).Add((tri, area));
                }

                var v0 = new Vector3(geometry.Verts[i0 * 3], geometry.Verts[i0 * 3 + 1], geometry.Verts[i0 * 3 + 2]);
                var v1 = new Vector3(geometry.Verts[i1 * 3], geometry.Verts[i1 * 3 + 1], geometry.Verts[i1 * 3 + 2]);
                var v2 = new Vector3(geometry.Verts[i2 * 3], geometry.Verts[i2 * 3 + 1], geometry.Verts[i2 * 3 + 2]);
                Vector3 face = Vector3.Cross(v1 - v0, v2 - v0);
                float faceLen = face.Length();
                if (faceLen < 1e-9f)
                    continue;
                Vector3 fn = face / faceLen;

                // Grid axes: the two the face is most parallel to. Projecting along the dominant
                // normal axis keeps the projected area within 1/sqrt(3) of the true one, so the
                // 2D point-in-triangle test never degenerates.
                float ax = Math.Abs(fn.X), ay = Math.Abs(fn.Y), az = Math.Abs(fn.Z);
                int axis = ax >= ay && ax >= az ? 0 : (ay >= az ? 1 : 2);
                int ua = axis == 0 ? 1 : 0, va = axis == 2 ? 1 : 2;

                float p0u = Coord(v0, ua), p0v = Coord(v0, va);
                float p1u = Coord(v1, ua), p1v = Coord(v1, va);
                float p2u = Coord(v2, ua), p2v = Coord(v2, va);
                float det = (p1u - p0u) * (p2v - p0v) - (p2u - p0u) * (p1v - p0v);
                if (Math.Abs(det) < 1e-9f)
                    continue;
                float inv = 1.0f / det;

                int minIu = (int)Math.Ceiling(Math.Min(p0u, Math.Min(p1u, p2u)) / spacing);
                int maxIu = (int)Math.Floor(Math.Max(p0u, Math.Max(p1u, p2u)) / spacing);
                int minIv = (int)Math.Ceiling(Math.Min(p0v, Math.Min(p1v, p2v)) / spacing);
                int maxIv = (int)Math.Floor(Math.Max(p0v, Math.Max(p1v, p2v)) / spacing);

                for (int iu = minIu; iu <= maxIu; iu++)
                {
                    for (int iv = minIv; iv <= maxIv; iv++)
                    {
                        float du = iu * spacing - p0u, dv = iv * spacing - p0v;
                        float bu = (du * (p2v - p0v) - (p2u - p0u) * dv) * inv;
                        float bv = ((p1u - p0u) * dv - du * (p1v - p0v)) * inv;
                        // A small tolerance keeps a point on a shared edge from being rejected
                        // by both triangles; the grid key dedupes the double-accept.
                        const float eps = 1e-4f;
                        if (bu < -eps || bv < -eps || bu + bv > 1.0f + eps)
                            continue;
                        bu = Math.Max(0.0f, Math.Min(1.0f, bu));
                        bv = Math.Max(0.0f, Math.Min(1.0f - bu, bv));

                        geometry.SamplePoint(tri, bu, bv,
                            out Vector3 pos, out Vector3 n, out _, out Vector2 duv);
                        var key = (axis * 2 + (Coord(fn, axis) < 0 ? 1 : 0), iu, iv,
                                   (int)Math.Round(Coord(pos, axis) / (spacing * 0.5f)));
                        if (!seen.Add(key))
                            continue;
                        samples.Add(new RadiosityGeometry.UniformSurfaceSample
                        { Tri = tri, Position = pos, Normal = n, DiffuseUv = duv });
                        moverHasSample[slot] = true;
                        if (emissive) moverHasEmissiveSample[slot] = true;
                    }
                }
            }

            // Coverage guarantees the grid cannot make: every mover with any surface gets at
            // least one probe (the volume hash resolves a converted mover from its own probes
            // first), and a mover whose emissive fixtures all fell between grid lines still
            // gets emitter samples - a missed emitter is missing light, not a missing probe.
            for (int slot = 0; slot < moverCount; slot++)
            {
                if (!moverHasSample[slot] && moverBestTri[slot] >= 0)
                    AddCentroid(moverBestTri[slot], slot, false);
                if (emissiveTris != null && emissiveTris[slot] != null && !moverHasEmissiveSample[slot])
                {
                    emissiveTris[slot].Sort((a, b) => b.area.CompareTo(a.area));
                    for (int e = 0; e < emissiveTris[slot].Count && e < 4; e++)
                        AddCentroid(emissiveTris[slot][e].tri, slot, true);
                }
            }
            return samples;
        }

        /// <summary>
        /// Place an island's uniform grid samples into its atlas rect, one texel per sample.
        /// Spatially sorted first so a rect row holds world-neighbours, which keeps the probe
        /// tree leaves tight; overflow is thinned along that order rather than truncated, so
        /// losing slots never shaves one contiguous region off the island.
        /// </summary>
        private static void PlaceUniformSamples(RadiosityGeometry geometry, RadiosityGeometry.Instance instance,
                                                SurfaceTexel[] texels)
        {
            List<RadiosityGeometry.UniformSurfaceSample> samples = instance.UniformSamples;
            if (samples == null || samples.Count == 0 || instance.AtlasWidth <= 0 || instance.AtlasHeight <= 0)
                return;

            var order = new List<int>(samples.Count);
            for (int i = 0; i < samples.Count; i++) order.Add(i);
            SpatialSort(order, i => samples[i].Position);

            int capacity = instance.AtlasWidth * instance.AtlasHeight;
            if (order.Count > capacity)
            {
                var kept = new List<int>(capacity);
                double stride = (double)order.Count / capacity;
                for (int k = 0; k < capacity; k++) kept.Add(order[(int)(k * stride)]);
                order = kept;
            }

            for (int k = 0; k < order.Count; k++)
            {
                RadiosityGeometry.UniformSurfaceSample s = samples[order[k]];
                int x = instance.AtlasX + k % instance.AtlasWidth;
                int y = instance.AtlasY + k / instance.AtlasWidth;
                if (x >= AtlasSize || y >= AtlasSize)
                    break;
                int index = y * AtlasSize + x;
                if (texels[index].Live)
                    continue;

                int moverSlot = s.Tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[s.Tri] : 0;
                int moverIndex = moverSlot >= 0 && moverSlot < instance.Movers.Count ? instance.Movers[moverSlot] : -1;

                var texel = new SurfaceTexel
                {
                    Position = s.Position,
                    Normal = s.Normal,
                    AlbedoSum = geometry.SampleAlbedo(s.Tri, s.DiffuseUv),
                    AlbedoTaps = 1,
                    Emissive = geometry.TriangleEmissive[s.Tri],
                    MoverIndex = moverIndex,
                    Live = true
                };
                // No texel footprint to integrate over - spread a few extra taps across the
                // sample's triangle, as the scattered fill does.
                for (int tap = 1; tap < FillAlbedoTaps; tap++)
                {
                    float ju = Fract((k + tap * 0.37f + 0.5f) * 0.7548776662f);
                    float jv = Fract((k + tap * 0.71f + 0.5f) * 0.5698402910f);
                    if (ju + jv > 1.0f) { ju = 1.0f - ju; jv = 1.0f - jv; }
                    texel.AlbedoSum += geometry.SampleAlbedo(s.Tri, geometry.DiffuseUvAt(s.Tri, ju, jv));
                    texel.AlbedoTaps++;
                }
                texels[index] = texel;
            }
        }

        /// <summary>
        /// Scatter samples over any texels in the rect the UV pass did not reach, picking
        /// triangles in proportion to their area so large faces get more probes.
        /// </summary>
        private static void FillUnclaimed(RadiosityGeometry geometry, RadiosityGeometry.Instance instance, SurfaceTexel[] texels)
        {
            var unclaimed = new List<int>();
            for (int y = instance.AtlasY; y < instance.AtlasY + instance.AtlasHeight; y++)
            {
                for (int x = instance.AtlasX; x < instance.AtlasX + instance.AtlasWidth; x++)
                {
                    int index = y * AtlasSize + x;
                    if (!texels[index].Live)
                        unclaimed.Add(index);
                }
            }
            if (unclaimed.Count == 0 || instance.Triangles.Count == 0)
                return;

            int moverIndex = instance.Movers.Count > 0 ? instance.Movers[0] : -1;

            // Cumulative area so a texel index maps to a triangle without a per-sample search.
            var cumulative = new float[instance.Triangles.Count];
            float total = 0;
            for (int i = 0; i < instance.Triangles.Count; i++)
            {
                total += geometry.TriangleArea(instance.Triangles[i]);
                cumulative[i] = total;
            }
            if (total <= 0)
                return;

            // Deterministic low-discrepancy sequence: same input always bakes the same output.
            for (int i = 0; i < unclaimed.Count; i++)
            {
                float pick = total * Fract(0.5f + (i + 0.5f) * 0.6180339887f);
                int triSlot = Array.BinarySearch(cumulative, pick);
                if (triSlot < 0) triSlot = ~triSlot;
                if (triSlot >= instance.Triangles.Count) triSlot = instance.Triangles.Count - 1;
                int tri = instance.Triangles[triSlot];

                float su = Fract((i + 0.5f) * 0.7548776662f);
                float sv = Fract((i + 0.5f) * 0.5698402910f);
                if (su + sv > 1.0f) { su = 1.0f - su; sv = 1.0f - sv; }

                geometry.SamplePoint(tri, su, sv, out Vector3 position, out Vector3 normal, out _, out Vector2 diffuseUv);

                // Keep whatever the UV pass already gathered here: those taps are real surface
                // samples even though no triangle covered the texel centre.
                Vector3 albedoSum = texels[unclaimed[i]].AlbedoSum + geometry.SampleAlbedo(tri, diffuseUv);
                int albedoTaps = texels[unclaimed[i]].AlbedoTaps + 1;

                texels[unclaimed[i]] = new SurfaceTexel
                {
                    Position = position,
                    Normal = normal,
                    AlbedoSum = albedoSum,
                    AlbedoTaps = albedoTaps,
                    Emissive = geometry.TriangleEmissive[tri],
                    MoverIndex = moverIndex,
                    Live = true
                };

                // No texel footprint to integrate over (these samples are scattered, not rasterised): spread a few
                // extra taps across the triangle instead.
                for (int tap = 1; tap < FillAlbedoTaps; tap++)
                {
                    float ju = Fract((i + tap * 0.37f + 0.5f) * 0.7548776662f);
                    float jv = Fract((i + tap * 0.71f + 0.5f) * 0.5698402910f);
                    if (ju + jv > 1.0f) { ju = 1.0f - ju; jv = 1.0f - jv; }
                    Vector2 tapUv = geometry.DiffuseUvAt(tri, ju, jv);
                    texels[unclaimed[i]].AlbedoSum += geometry.SampleAlbedo(tri, tapUv);
                    texels[unclaimed[i]].AlbedoTaps++;
                }
            }
        }

        /// <summary>
        /// Fill the dead cells of the atlas's engine-owned 16x16 corner with cluster-only clones of
        /// the nearest live cell. Cloned cells carry the neighbour's surface (position, normal,
        /// resolved albedo) but no emissive - the surface-light pass must not gain emitter area -
        /// and never become surface probes.
        /// </summary>
        private static void FillAtlasGutters(SurfaceTexel[] texels, Action<string> log)
        {
            // Island rects are barred from the corner (see AllocateAtlases) but it must never hold
            // a dead texel - retail ships it 256/256 live on every slice measured, and leaving 100
            // of its cells dead exploded whole frames to 10-14x (SCI_Hub dirt6: the engine's own
            // corner consumers read the dead cells as garbage). So the search goes as far as it takes.
            int filled = 0, stillDead = 0;
            var fills = new List<(int cell, int source)>();
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int i = y * AtlasSize + x;
                    if (texels[i].Live)
                        continue;
                    int source = -1;
                    for (int r = 1; r <= AtlasSize && source < 0; r++)
                    {
                        for (int dy = -r; dy <= r && source < 0; dy++)
                        {
                            for (int dx = -r; dx <= r; dx++)
                            {
                                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                                    continue;
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= AtlasSize || ny >= AtlasSize)
                                    continue;
                                int n = ny * AtlasSize + nx;
                                if (texels[n].Live && !texels[n].ClusterOnly) { source = n; break; }
                            }
                        }
                    }
                    if (source < 0) { stillDead++; continue; }
                    fills.Add((i, source));
                }
            }
            foreach ((int cell, int source) in fills)
            {
                SurfaceTexel s = texels[source];
                texels[cell] = new SurfaceTexel
                {
                    Position = s.Position,
                    Normal = s.Normal,
                    Albedo = s.Albedo,
                    AlbedoTaps = s.AlbedoTaps,
                    Emissive = Vector3.Zero,
                    MoverIndex = s.MoverIndex,
                    Live = true,
                    ClusterOnly = true
                };
                filled++;
            }
            log?.Invoke("    atlas gutters: " + filled + " cells filled cluster-only, " + stillDead + " left dead");
        }

        /// <summary>
        /// Retail's albedo convention on emitting surfaces, applied over the folded texel albedos. Every texel of a fixture
        /// this bake LIGHTS stores the light's colour (ChallengeMap4's CEILING_HZDLAB: retail ~(180,175,164) under its
        /// 174-grey lights, our dark plastic sample ~(23,22,22)); the colour comes from a retail prior, so only delta
        /// slices stamp. An emissive surface the bake leaves dark stores near-black instead (its glow art is not a
        /// reflectance: CM3's WELDER_BLUE panels read 5-11 in retail's probe table against our ~(4,52,128)).
        /// </summary>
        private static void ApplyEmissiveAlbedoConvention(
            Level level, SurfaceTexel[] texels, RetailLightPriors lightPriors, Action<string> log)
        {
            if (level == null)
                return;
            var perMover = new Dictionary<int, Vector3?>();
            int stamped = 0, darkened = 0;

            for (int i = 0; i < texels.Length; i++)
            {
                if (!texels[i].Live)
                    continue;
                int m = texels[i].MoverIndex;
                if (m < 0 || m >= level.Movers.Entries.Count)
                    continue;
                if (!perMover.TryGetValue(m, out Vector3? colour))
                {
                    Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[m];
                    RetailLightPriors.Prior prior = lightPriors.Lookup(mv.Resource);
                    colour = prior != null && !SuppressedByRetail(lightPriors, mv)
                        ? new Vector3(prior.R / 255.0f, prior.G / 255.0f, prior.B / 255.0f)
                        : (Vector3?)null;
                    perMover[m] = colour;
                }
                if (colour.HasValue)
                {
                    // The WHOLE fixture, not just the texels the shader flags emissive. Retail does not store light
                    // colour as albedo (23 Sep 2026 decode: 0 of 12,961 probes of prior-carrying movers); the stamp stays
                    // because it renders BSP_Torrens better (about 0.6 worse without it, 12 of 13 other levels neutral).
                    texels[i].Albedo = colour.Value;
                    stamped++;
                }
                else if (texels[i].Emissive != Vector3.Zero)
                {
                    texels[i].Albedo = new Vector3(RadiosityTuning.EmissiveNoPriorAlbedo);
                    darkened++;
                }
            }
            if (stamped + darkened > 0)
                log?.Invoke("    emissive albedo convention: " + stamped + " texels stamped with prior light colour, " +
                            darkened + " no-prior emissive texels darkened");
        }

        /// <summary>
        /// Albedo taps per axis within one atlas texel, so 4 means a 4x4 grid of 16 - which is
        /// what retail's albedo table averages per probe (374375 samples over 22743 Solace probes).
        /// </summary>
        private const int AlbedoSubTaps = 4;

        /// <summary>Taps spread across a triangle for texels the UV rasteriser never reached.</summary>
        private const int FillAlbedoTaps = 4;

        /// <summary>Resolve each texel's accumulated albedo taps into the mean the probes carry.</summary>
        private static void FoldAlbedo(SurfaceTexel[] texels)
        {
            for (int i = 0; i < texels.Length; i++)
            {
                if (texels[i].AlbedoTaps > 0)
                    texels[i].Albedo = texels[i].AlbedoSum / texels[i].AlbedoTaps;
            }
        }

        private static Vector2 ToRect(Vector2 uv, RadiosityGeometry.Instance instance)
        {
            // Matches the MODEL_PARAMS transform: origin + uv * rectSize.
            float u = uv.X - (float)Math.Floor(uv.X);
            float v = uv.Y - (float)Math.Floor(uv.Y);
            return new Vector2(instance.AtlasX + u * instance.AtlasWidth, instance.AtlasY + v * instance.AtlasHeight);
        }

        #endregion
    }
}
#endif
