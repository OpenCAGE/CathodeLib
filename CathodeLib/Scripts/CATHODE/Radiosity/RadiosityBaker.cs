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
    /// <remarks>
    /// <para>How the runtime data is laid out, as far as it has been decoded:</para>
    /// <list type="bullet">
    /// <item>The level is split into <b>slices</b>. Each owns a set of fixed-size textures: a
    /// 128x128 lightmap atlas, a 256x64 probe texture, a 512x512 influence index map and a 512x256
    /// influence weight map. All the 16384-element arrays hold the same number of entries but are
    /// <i>not</i> in the same space - the cluster array and mangle map are indexed by atlas texel
    /// (<c>y * 128 + x</c>), while surface probes and input probes are compacted lists addressed
    /// <c>y * 256 + x</c>. Byte-pair references to any of them split the index as
    /// <c>(index % 256, index / 256)</c>.</item>
    /// <item>A <b>radiosity instance</b> is one lightmap island, keyed by a RESOURCES.BIN index.
    /// It owns a disjoint rect of its slice's atlas. RADIOSITY_INSTANCE_MAP.TXT lists
    /// <c>instanceIndex resourceIndex</c> once per mover in the island, and
    /// <c>InstanceSliceIndices[instanceIndex]</c> says which slice it lives in.</item>
    /// <item>A mover's MODEL_PARAMS holds <c>(rectWidth - 0.5, rectHeight - 0.5, rectX, rectY)</c>
    /// so the shader can map the mesh's lightmap UVs into that rect. Verified against retail:
    /// slice 1 of BSP_TORRENS packs 174 instances with zero overlapping texels.</item>
    /// <item><b>Clusters</b> are the emitters, one per atlas texel.</item>
    /// <item><b>Surface probes</b> are the receivers: the live atlas texels compacted into 16x16
    /// tiles. Each gathers from up to 32 clusters, stored as 32 (clusterXY, weight) pairs keyed by
    /// the probe's <i>slot</i> - two bytes per influence in the index map, one in the weight map.
    /// The surface probe tree's leaves are exactly those tiles.</item>
    /// <item><b>Input probes</b> are a second, smaller compacted list in the same tiled space. They
    /// are where direct light is injected (surface lights address input probe texels) and where the
    /// scatter pass accumulates. The <b>mangle map</b> maps an atlas texel back to an input probe
    /// texel; the engine's CA_RADIOSITY_UNMANGLE pass undoes the repack.</item>
    /// <item>The <b>scatter list</b> is a point list: one entry per (input probe, cluster) pair,
    /// grouped by probe, at most eight per group, and covering every live cluster.</item>
    /// </list>
    /// <para>Not decoded, and therefore emitted empty or conservatively: the volume probe hash
    /// (retail builds a real hierarchy; we would emit a flat list, so it is gated off by default),
    /// the tiled variants of scatter / surface lights / doors, and the mangle map's second
    /// reference and blend bits. See the TODOs inline.</para>
    /// </remarks>
    public static class RadiosityBaker
    {
        /// <summary>Atlas / probe textures are a fixed 128x128 per slice.</summary>
        private const int AtlasSize = 128;
        private const int AtlasTexels = AtlasSize * AtlasSize;

        /// <summary>
        /// Input probes live in their own texture, 256 wide and 64 tall, addressed
        /// <c>y * 256 + x</c> - not in the 128x128 atlas. Both arrays hold 16384 elements, so the
        /// difference is invisible to the parser and fatal to the engine.
        /// </summary>
        /// <remarks>
        /// Measured: across all 128 retail slices, every live entry in InputProbePositions falls
        /// inside a declared tile under a stride of 256 and none does under 128, tiles never reach
        /// past x = 192 or y = 64, and every scatter destination and surface light lands on a live
        /// probe under the same stride.
        /// </remarks>
        private const int ProbeTexWidth = 256;

        /// <summary>
        /// Probes pack into 16x16 tiles, 4 rows deep, filling columns left to right. Surface probes
        /// and input probes are separate compacted lists sharing this layout.
        /// </summary>
        private const int TileSize = 16;
        private const int TileRows = 4;

        /// <summary>
        /// Input probe tiles use the full width: sixteen columns of four rows over the 256x64
        /// slice, which is 64 tiles.
        /// </summary>
        /// <remarks>
        /// Twelve was inferred from retail never placing a tile past x = 192, but that is just how
        /// far its levels happen to fill - Solace's busiest slice uses 38 tiles. Capping at twelve
        /// would silently truncate a denser level's probe set, and since probes are spatially
        /// sorted the truncated tail is a contiguous region of the map.
        /// </remarks>
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

        /// <summary>Longest fallback placement for coverage of clusters the solve dropped.</summary>
        private const float ScatterMaxFallbackDistance = 4.0f;

        /// <summary>Marks an atlas texel that no instance claimed.</summary>
        private static readonly Vector4 UnusedSurfaceProbe = new Vector4(-100000.0f, 0.0f, 0.0f, 0.0f);

        /// <summary>Retail writes 1/sqrt(pi) into the w of every live probe position.</summary>
        private const float ProbeNormalisation = 0.56418955f;

        /// <summary>
        /// The weight an influence carries, from how far the emitter is and how squarely the two
        /// surfaces face each other.
        /// </summary>
        /// <remarks>
        /// <para>Retail's weights are heavily compressed - the strongest is only about twice the
        /// weakest - but they are emphatically not flat. Measured over Solace's 794570 links they
        /// fall monotonically with distance and correlate with it at -0.707:</para>
        /// <code>
        ///   0-0.5m 205 | 0.5-1m 194 | 1-2m 179 | 2-3m 170 | 3-5m 151
        ///   5-8m   131 | 8-12m  118 | 12-20m 104 | 20-35m  63
        /// </code>
        /// <para><c>W0 * (d0/(d0+d))^k</c> with the constants below reproduces that to within a few
        /// units everywhere inside 16 m, which is 99.8% of retail's links.</para>
        /// <para>This replaces a fixed weight-by-rank table. That table did fix the earlier bug -
        /// normalising the raw form factor against each probe's own peak pinned the nearest cluster
        /// at 255 everywhere and caused bright patches - but it overcorrected into a constant:
        /// because every rank had a fixed weight, every probe that filled all 32 slots received an
        /// identical total of exactly 4993, whatever its geometry. 70% of Solace's probes sat on
        /// that one value, against retail's smooth 4392-6162 spread. All of our lighting variation
        /// then came from which clusters were picked, a discrete choice that flips between
        /// neighbouring probes, which is what made the result patchy where retail is smooth. It
        /// also let distant emitters keep full strength: past 5 m our weights stopped falling and
        /// began to rise, since a sparse probe's 32 best candidates are far away but still rank
        /// 0-31.</para>
        /// </remarks>
        private static byte InfluenceWeight(float distance, float cosProduct, RadiosityBakeSettings settings)
        {
            // The default curve is flatter than retail's (~9 bytes under its per-band means below
            // half a metre, ~8 over past 8 m; retail: 205 at 0.35 m, 170 at 2.5 m, 141 at 5 m,
            // 118 at 10 m). A curve calibrated to retail's measured bands within +-3 bytes exists -
            // (227, 2.43, 0.46), found by bake-and-measure iteration - and it brings lit rooms to
            // 0.96-1.04x retail, but two rooms that overshoot under every transport increase
            // (Solace cam11 storage / cam4 corridor) get worse and cost more mean error than the
            // lit rooms gain. Until whatever dims those rooms in retail is found (door transfers
            // exporting energy is the leading guess), the flat curve measures best overall.
            float W0 = settings.InfluenceCurveW0;
            float D0 = settings.InfluenceCurveD0;
            float K = settings.InfluenceCurveK;

            float d = Math.Max(0.0f, distance);
            float baseWeight = W0 * (float)Math.Pow(D0 / (D0 + d), K);

            // Retail spreads about +/-14% either side of each distance band's mean (at 2-3 m the
            // mean is 170 with p10 144 and p90 191). The facing term is what varies within a band.
            float facing = (float)Math.Sqrt(Math.Max(0.0f, Math.Min(1.0f, cosProduct)));
            float weight = baseWeight * (0.85f + 0.30f * facing);

            int v = (int)Math.Round(weight);
            return (byte)Math.Max(1, Math.Min(255, v));
        }

        /// <summary>
        /// Encode an emissive radiosity multiplier into a surface light's Scale byte.
        /// </summary>
        /// <remarks>
        /// <para>Scale is fixed point in sixteenths, biased by one: the engine reads
        /// <c>(Scale + 1) / 16</c>, spanning 0.0625 to 2.0. Retail's BSP_TORRENS lights carry only
        /// seven distinct values - {0, 1, 3, 7, 15, 23, 31} - and a unit multiplier lands on 15,
        /// which is 63% of them. Encoding <c>ceil(16 * multiplier) - 1</c> reproduces that set for
        /// all but one of the level's 376 emissive movers.</para>
        /// <para>The square-root curve this replaces fell outside retail's value set 58% of the
        /// time and pushed 27% of our lights onto the 31 ceiling, making each emitter about 2.2x
        /// as strong as retail's. That is what blew out lit ceilings.</para>
        /// <para>Feed this the multiplier alone. Folding the emissive tint's brightness in here as
        /// well takes the value off the sixteenths grid, and it belongs in the light's RGB.</para>
        /// <para>The field is a full byte, not five bits. BSP_TORRENS happens to top out at 31,
        /// but Solace carries 160 lights at Scale 159 and 132 at 255 - multipliers of 10 and 16 -
        /// and between them they account for 88% of that level's total light energy. Clamping to
        /// 31 discarded all of it, which is part of why our lighting reads flat and evenly spread
        /// where retail has strong pools.</para>
        /// </remarks>
        /// <summary>
        /// Pull a colour towards its own luminance, so a surface light keeps its brightness but
        /// only <paramref name="saturation"/> of its chroma.
        /// </summary>
        private static Vector3 Desaturate(Vector3 colour, float saturation)
        {
            if (saturation >= 1.0f)
                return colour;
            float luminance = 0.299f * colour.X + 0.587f * colour.Y + 0.114f * colour.Z;
            var grey = new Vector3(luminance);
            return grey + (colour - grey) * Math.Max(0.0f, saturation);
        }

        private static byte EmissiveScaleByte(float multiplier)
        {
            if (multiplier <= 0.0f) return 0;
            int v = (int)Math.Ceiling(16.0 * multiplier) - 1;
            return (byte)Math.Max(0, Math.Min(255, v));
        }

        /// <summary>
        /// A surface light's Weight from its emitter's emissive surface area, in square metres.
        /// </summary>
        /// <remarks>
        /// <para>Decoded on BSP_TORRENS by joining every retail light slice to its mover's
        /// emissive geometry: retail's mean Weight per slice is 100x the square root of the
        /// emissive area, with the implied coefficient stable at 96-107 from 0.007 m2 up to 3 m2
        /// (881 emitters, correlation 0.82) and the material EMISSIVE_MULT wholly absent from it
        /// (correlation 0.000). Strength rides in Scale, area rides in Weight, and together they
        /// are the fixture's flux.</para>
        /// <para>This replaces a coefficient on sqrt(emissive radiance). A flat per-level
        /// coefficient could match one level's energy total but never the per-fixture spread,
        /// which is why every calibration of it traded hot rooms against dark ones - SCI_Hub
        /// wanted 32 where BSP_TORRENS wanted 17.</para>
        /// </remarks>
        /// <summary>Fallback flux coefficient when no retail bake exists to calibrate against.</summary>
        private const float DefaultWeightK = 500.0f;

        /// <summary>
        /// What the retail bake said about each emitting entity, plus the per-level flux
        /// coefficient fitted from it for entities it does not cover.
        /// </summary>
        /// <remarks>
        /// The retail bake is the ground truth for its own emitters in ways no formula recovers:
        /// its Weights encode level state (Solace's dark sections carry emitters whose fixtures
        /// exist but whose flux is written near zero - a state-aware bake), and its RGB carries
        /// the true light colours our tint desaturation only approximates. Entities present in
        /// the retail data take its values verbatim; the fitted area model covers emitters the
        /// table has never seen, which is exactly new or modified content.
        /// </remarks>
        private sealed class RetailLightPriors
        {
            public sealed class Prior
            {
                public float SumWeight;
                public int Items;
                public byte Scale;
                public byte R, G, B;

                /// <summary>
                /// Retail's mean per-sample Weight. Each of our samples carries this rather than
                /// a share of the sum: dividing the sum over our (usually higher) sample count
                /// dimmed every fixture's local effect - the engine reads Weight per sample.
                /// </summary>
                public byte MeanWeight => (byte)Math.Max(1, Math.Min(191,
                    (int)Math.Round(SumWeight / Math.Max(1, Items))));

                /// <summary>
                /// Weight for one of <paramref name="samples"/> samples such that the entity's
                /// TOTAL flux matches retail's. Weights are absolute per-sample gains, so carrying
                /// the retail mean at a different sample count scales the fixture's whole output
                /// by ours/retail - measured on Solace as 0.3x to 3x per entity in both
                /// directions. Sharing the sum makes the count a placement detail, as it should
                /// be. The 191 cap can still under-deliver when we place far fewer samples than
                /// retail; the placement passes should get close to <see cref="Items"/>.
                /// </summary>
                public byte WeightFor(int samples) => (byte)Math.Max(1, Math.Min(191,
                    (int)Math.Round(SumWeight / Math.Max(1, samples))));
            }

            public float K = DefaultWeightK;
            public readonly Dictionary<(uint, uint), Prior> Priors = new Dictionary<(uint, uint), Prior>();

            /// <summary>
            /// Entities whose radiosity_multiplier is authored 0 in Commands, from the instancing
            /// pass; null when baking without one. Unlike the priors-based suppression this needs
            /// no retail bake to compare against, so it covers new content.
            /// </summary>
            public HashSet<(uint, uint)> AuthoredOff;

            public Prior Lookup(Resources.Resource resource)
            {
                if (resource == null)
                    return null;
                return Priors.TryGetValue(
                    (resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32),
                    out Prior prior) ? prior : null;
            }
        }

        /// <summary>
        /// True when an emitter must produce no surface light: the retail bake being replaced knew
        /// the fixture and attached none, and the mover's EmissiveRadiosityMultiplier is authored
        /// zero. The multiplier is not the general strength gate - 900 of Solace's 1269 lit
        /// entities also store 0 - but every emitter retail leaves unlit at full material strength
        /// stores 0 while its lit same-model siblings store more, and the four on Solace were the
        /// only lights in sections retail renders black. Firing only when the entity has no prior
        /// keeps a from-scratch bake of new content unaffected.
        /// </summary>
        private static bool SuppressedByRetail(RetailLightPriors priors, Movers.MOVER_DESCRIPTOR mover)
        {
            if (priors == null || mover == null)
                return false;
            // A retail prior is ground truth: whatever the parameters say, the retail bake lit
            // this entity, so we must too. SCI_Hub lights thousands of entities whose
            // radiosity_multiplier is authored 0 - suppressing them dimmed the whole level
            // (mean rmse 16.7 -> 20.7).
            if (priors.Lookup(mover.Resource) != null)
                return false;
            if (priors.Priors.Count > 0)
            {
                // With a retail bake to compare against, an emitter with no prior is usually a
                // GUID join failure rather than content retail chose not to light - suppressing
                // the authored-0 subset of them also measured worse (SCI_Hub 16.7 -> 17.3) - so
                // only the established MVR-multiplier rule applies here.
                return mover.EmissiveRadiosityMultiplier <= 0.0f;
            }
            // Scratch bake, no retail reference: an authored radiosity_multiplier of 0 is the
            // author's own exclusion flag and the best signal available.
            return priors.AuthoredOff != null && mover.Entity != null &&
                   priors.AuthoredOff.Contains((mover.Entity.composite_instance_id.AsUInt32, mover.Entity.entity_id.AsUInt32));
        }

        /// <summary>
        /// Per-level flux coefficient K in <c>per-entity Weight sum = K x sqrt(emissive area)</c>,
        /// fitted against the retail bake being replaced.
        /// </summary>
        /// <remarks>
        /// <para>Retail's per-entity Weight sum tracks the square root of the emissive area on
        /// every level measured - correlation 0.88 on BSP_TORRENS, 0.92 on Solace - but the
        /// coefficient is level-dependent (567 against 1081 for those two) and its source is not
        /// decoded. It does not need to be: the retail RADIOSITY_RUNTIME is still loaded when the
        /// bake starts, so the coefficient is fitted from its own light slices joined to our
        /// measured areas through RESOURCES.BIN. This also absorbs any uniform bias in our area
        /// measurement.</para>
        /// <para>A flat radiance-based Weight was tried instead and could never fit more than one
        /// level at a time: Solace's per-entity energies came out at a median 2.7x retail with a
        /// p90 of 16x, because retail's spread is the area spread.</para>
        /// </remarks>
        private static RetailLightPriors CalibrateWeightCoefficient(
            Level level, Dictionary<int, float> emissiveAreas, Action<string> log)
        {
            var result = new RetailLightPriors();
            RadiosityRuntime retail = level.RadiosityRuntime;
            if (retail == null || retail.Slices.Count == 0 || level.Resources == null)
                return result;

            // Retail per-entity light data, keyed by resource GUID pair. EntityInstanceIndex is
            // an index into the load-time RESOURCES order, which GetAtWriteIndex still reflects.
            foreach (RadiosityRuntime.RuntimeDataSlice slice in retail.Slices)
            {
                var lights = slice.SurfaceLights;
                if (lights?.LightSlices == null)
                    continue;
                foreach (RadiosityRuntime.RuntimeSurfaceLights.LightSlice ls in lights.LightSlices)
                {
                    Resources.Resource resource = level.Resources.GetAtWriteIndex(ls.EntityInstanceIndex);
                    if (resource == null || ls.NumItems == 0 || ls.FirstItem >= lights.Lights.Count)
                        continue;

                    var key = (resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32);
                    if (!result.Priors.TryGetValue(key, out RetailLightPriors.Prior prior))
                    {
                        RadiosityRuntime.RuntimeSurfaceLights.Light first = lights.Lights[(int)ls.FirstItem];
                        result.Priors[key] = prior = new RetailLightPriors.Prior
                        {
                            Scale = first.Scale,
                            R = first.R,
                            G = first.G,
                            B = first.B
                        };
                    }
                    for (uint i = ls.FirstItem; i < ls.FirstItem + ls.NumItems && i < lights.Lights.Count; i++)
                    {
                        prior.SumWeight += lights.Lights[(int)i].Weight;
                        prior.Items++;
                    }
                }
            }
            if (result.Priors.Count == 0)
                return result;

            // Fit the area model over the joined emitters for whatever the priors do not cover.
            double sxx = 0, sxy = 0;
            int joined = 0;
            foreach (KeyValuePair<int, float> pair in emissiveAreas)
            {
                if (pair.Value <= 0 || pair.Key >= level.Movers.Entries.Count)
                    continue;
                RetailLightPriors.Prior prior = result.Lookup(level.Movers.Entries[pair.Key].Resource);
                if (prior == null || prior.SumWeight <= 0)
                    continue;
                double x = Math.Sqrt(pair.Value);
                sxx += x * x;
                sxy += x * prior.SumWeight;
                joined++;
            }
            if (joined >= 20 && sxx > 0)
                result.K = (float)(sxy / sxx);

            log?.Invoke("Radiosity light priors: " + result.Priors.Count + " retail entities, K = " +
                        result.K.ToString("0") + " from " + joined + " joined emitters");
            return result;
        }

        /// <summary>
        /// A sample's Weight: the entity's flux (<paramref name="weightK"/> x sqrt(area)) shared
        /// over its <paramref name="samples"/>. Retail's per-sample weights vary within a slice
        /// but their per-entity sum is the tracked quantity.
        /// </summary>
        private static byte EmissiveWeightByte(float weightK, float emissiveArea, int samples)
        {
            if (emissiveArea <= 0.0f || samples <= 0) return 1;
            int v = (int)Math.Round(weightK * Math.Sqrt(emissiveArea) / samples);
            return (byte)Math.Max(1, Math.Min(191, v));
        }

        public sealed class BakeResult
        {
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

            RadiosityGeometry geometry = RadiosityGeometry.CollectFromLevel(level, settings, log);
            if (geometry.TriangleCount == 0)
                throw new InvalidOperationException("No renderable geometry to bake.");
            geometry.Build(log);

            // Occlude against the collision shell rather than the render meshes where we can: it
            // stops probes being buried inside a mesh's own interior detail.
            if (settings.UseCollisionForVisibility &&
                RadiosityOccluders.TryCollect(level, geometry, out float[] occluderVerts, out int[] occluderTris, log,
                                              skipDoorBarriers: settings.OpenDoorwaysForBake))
            {
                geometry.OccluderEndpointSlack = settings.OccluderEndpointSlack;
                geometry.OccluderSlackFraction = settings.OccluderSlackFraction;
                geometry.BuildOccluders(occluderVerts, occluderTris, log);
            }

            List<List<RadiosityGeometry.Instance>> slices = PartitionIntoSlices(geometry, level.RadiosityRuntime, settings, log);
            AllocateAtlases(slices, settings, log);

            // Per-mover emissive surface area, which is what a surface light's Weight encodes,
            // and the per-level flux coefficient calibrated from the retail bake being replaced.
            Dictionary<int, float> emissiveAreas = ComputeEmissiveAreas(geometry);
            RetailLightPriors lightPriors = CalibrateWeightCoefficient(level, emissiveAreas, log);
            lightPriors.AuthoredOff = instancing?.RadiosityAuthoredOff;
            if (lightPriors.AuthoredOff != null && lightPriors.AuthoredOff.Count > 0)
                log?.Invoke("Radiosity: " + lightPriors.AuthoredOff.Count + " entities excluded by authored radiosity_multiplier = 0");

            // Rewrite the level's existing instance in place so it keeps its filepath and
            // Level.Save persists it in the normal pass.
            RadiosityRuntime runtime = level.RadiosityRuntime
                ?? throw new InvalidOperationException("Level has no RADIOSITY_RUNTIME.BIN to rewrite.");
            runtime.Slices.Clear();
            runtime.InstanceSliceIndices.Clear();
            runtime.InfluenceFixups.Clear();
            runtime.FlattenedFixupRanges.Clear();

            var result = new BakeResult { Slices = slices.Count };
            var sliceData = new SliceBake[slices.Count];

            void BakeOne(int i)
            {
                sliceData[i] = BakeSlice(level, geometry, slices[i], i, settings, emissiveAreas, lightPriors, log);
            }

            if (settings.Parallel && slices.Count > 1)
                Parallel.For(0, slices.Count, BakeOne);
            else
                for (int i = 0; i < slices.Count; i++) BakeOne(i);

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

                        // Carry the Resource itself: this runs inside Instancing, long before
                        // Resources.Save renumbers RESOURCES.BIN, so an index captured now is stale
                        // by the time the file is written.
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
                result.Influences += sliceData[s].InfluenceCount;
                result.SurfaceLights += sliceData[s].Slice.SurfaceLights.Lights.Count;
            }

            int instanceIdCeiling = Math.Max(nextNewId, maxRetailId + 1);
            for (int i = 0; i < instanceIdCeiling; i++)
                runtime.InstanceSliceIndices.Add(sliceForInstanceId.TryGetValue(i, out int s2) ? s2 : 0);

            if (settings.EmitSurfaceLights)
                AddUnbakedEmitterLights(level, geometry, sliceData, settings, lightPriors, log);

            result.StaleRectsCleared = ClearStaleModelParams(level, taggedMovers);

            BuildSliceNeighbours(runtime);

            result.CrossSliceFixups = settings.EmitCrossSliceFixups


                ? BuildCrossSliceFixups(geometry, sliceData, runtime, settings)


                : ClearCrossSliceFixups(runtime);
            result.DoorTransfers = settings.EmitDoors ? BuildDoors(level, geometry, sliceData, settings, log) : 0;

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

        #region SLICING

        /// <summary>
        /// Split instances into slices. Retail's own grouping is used where the level still carries
        /// it (see <see cref="RadiosityBakeSettings.MatchRetailSlices"/>); otherwise instances are
        /// split by recursive median split on their centroids until each slice's atlas demand fits.
        /// Spatial coherence matters for the fallback: influences are only gathered within a slice,
        /// so neighbours should share one.
        /// </summary>
        private static List<List<RadiosityGeometry.Instance>> PartitionIntoSlices(
            RadiosityGeometry geometry, RadiosityRuntime retail, RadiosityBakeSettings settings, Action<string> log)
        {
            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
            {
                RadiosityAtlas.RectSizeForBounds(instance.SurfaceArea, instance.BoundsMax - instance.BoundsMin,
                    instance.UvCoverage, settings, out int w, out int h);
                instance.AtlasWidth = w;
                instance.AtlasHeight = h;
            }

            if (settings.MatchRetailSlices)
            {
                List<List<RadiosityGeometry.Instance>> retailGrouped =
                    PartitionByRetailSlices(geometry, retail, settings, log);
                if (retailGrouped != null)
                    return retailGrouped;
            }

            var slices = new List<List<RadiosityGeometry.Instance>>();
            SplitSpatially(geometry.Instances.ToList(), settings, slices, log);
            log?.Invoke("Radiosity slices: " + slices.Count + " (" +
                        string.Join(", ", slices.Select(o => o.Count + " inst / " + o.Sum(i => i.AtlasWidth * i.AtlasHeight) + " texels")) + ")");
            return slices;
        }

        /// <summary>
        /// Group instances into the slices retail's own bake used, resolved through each instance's
        /// retail island id. Returns null when there is not enough retail data to do it, so the
        /// caller falls back to the spatial split.
        /// </summary>
        /// <remarks>
        /// The runtime picks the slice for an object from its island, not from where it stands
        /// (RADIOSITY_RUNTIME's InstanceSliceIndices is keyed by the island id that
        /// RADIOSITY_INSTANCE_MAP assigns). Retail's slices are correspondingly not a spatial
        /// partition - on ChallengeMap4 all three span most of the level and overlap - so a median
        /// split can put an island in a slice whose volume probe hash does not reach the objects
        /// that island lights.
        /// </remarks>
        private static List<List<RadiosityGeometry.Instance>> PartitionByRetailSlices(
            RadiosityGeometry geometry, RadiosityRuntime retail, RadiosityBakeSettings settings, Action<string> log)
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
            // so the island -> slice mapping stays correct either way. Empty groups are dropped: a
            // slice carrying retail's subdivision count with no items is the combination
            // render_object_probes faults on.
            var final = new List<List<RadiosityGeometry.Instance>>();
            foreach (int key in byRetailSlice.Keys.OrderBy(k => k))
            {
                List<RadiosityGeometry.Instance> group = byRetailSlice[key];
                if (group.Count == 0)
                    continue;

                int texels = group.Sum(o => o.AtlasWidth * o.AtlasHeight);
                if (texels <= settings.MaxTexelsPerRetailSlice || group.Count == 1)
                {
                    final.Add(group);
                    continue;
                }
                log?.Invoke("Radiosity slices: retail group " + key + " of " + group.Count +
                            " instances needs " + texels + " texels; splitting it spatially.");
                SplitSpatially(group, settings, final, log);
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
            List<RadiosityGeometry.Instance> group, RadiosityBakeSettings settings,
            List<List<RadiosityGeometry.Instance>> output, Action<string> log)
        {
            if (group.Count == 0)
                return;

            // Splitting must keep going until the group fits: a slice holding more live texels
            // than MaxInputProbes cannot give every texel an input probe, and a cluster with no
            // probe to scatter to breaks the "every live cluster is a scatter source" invariant.
            int texels = group.Sum(o => o.AtlasWidth * o.AtlasHeight);
            if (texels <= settings.MaxTexelsPerSlice || group.Count == 1)
            {
                output.Add(group);
                return;
            }
            if (output.Count >= settings.MaxSlices)
            {
                log?.Invoke("  WARNING: slice budget (" + settings.MaxSlices + ") reached with " +
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

            // Cut into the fewest parts that fit, by cumulative texels rather than by instance
            // count. Halving repeatedly can only ever yield 2, 4, 8..., so a level needing three
            // slices got four - and every extra slice adds boundary regions where two slices both
            // place input probes and the densities add.
            int parts = Math.Max(2, (int)Math.Ceiling(texels / (double)settings.MaxTexelsPerSlice));
            parts = Math.Min(parts, group.Count);

            int target = (int)Math.Ceiling(texels / (double)parts);
            int start = 0, running = 0;
            for (int i = 0; i < group.Count; i++)
            {
                running += group[i].AtlasWidth * group[i].AtlasHeight;
                bool last = i == group.Count - 1;
                // Leave at least one instance for each part still to come.
                if (!last && running < target)
                    continue;
                if (!last && group.Count - (i + 1) < 1)
                    continue;

                SplitSpatially(group.GetRange(start, i - start + 1), settings, output, log);
                start = i + 1;
                running = 0;
                if (start >= group.Count)
                    break;
            }
            if (start < group.Count)
                SplitSpatially(group.GetRange(start, group.Count - start), settings, output, log);
        }

        /// <summary>
        /// Give every instance a disjoint rect in its slice's atlas, shrinking rather than
        /// dropping when a slice fills up.
        /// </summary>
        private static void AllocateAtlases(
            List<List<RadiosityGeometry.Instance>> slices, RadiosityBakeSettings settings, Action<string> log)
        {
            for (int s = 0; s < slices.Count; s++)
            {
                var atlas = new RadiosityAtlas(AtlasSize);

                // Largest first: skyline packers waste far less space that way.
                slices[s].Sort((a, b) => (b.AtlasWidth * b.AtlasHeight).CompareTo(a.AtlasWidth * a.AtlasHeight));

                int shrunk = 0, failed = 0;
                foreach (RadiosityGeometry.Instance instance in slices[s])
                {
                    int w = instance.AtlasWidth;
                    int h = instance.AtlasHeight;
                    bool placed = false;

                    while (w >= 1 && h >= 1)
                    {
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
                        // Nothing left. Park it on a 1x1 at the origin: the island still resolves
                        // to a valid texel, it just shares lighting with whatever else is there.
                        instance.SliceIndex = s;
                        instance.AtlasX = 0;
                        instance.AtlasY = 0;
                        instance.AtlasWidth = 1;
                        instance.AtlasHeight = 1;
                        failed++;
                    }
                }

                log?.Invoke("  slice " + s + " atlas: " + atlas.UsedTexels + "/" + AtlasTexels +
                            " texels used, " + shrunk + " shrunk, " + failed + " overflowed");
            }
        }

        #endregion

        #region PER-SLICE BAKE

        private sealed class SliceBake
        {
            public RadiosityRuntime.RuntimeDataSlice Slice;
            public int SurfaceProbeCount;
            public int InputProbeCount;
            public int ClusterCount;
            public int InfluenceCount;

            /// <summary>Traced 8x8 face grids, six per volume probe, in probe order.</summary>
            public List<byte[]> VisFaceGrids;

            /// <summary>Palette index per entry of <see cref="VisFaceGrids"/>, filled after dedup.</summary>
            public byte[] VisFaceIndices;

            /// <summary>Retained so the cross-slice pass can see this slice's probes.</summary>
            public SurfaceTexel[] Texels;

            /// <summary>
            /// Influence slots already used, keyed by surface probe slot so fixups fill the rest.
            /// </summary>
            public byte[] UsedInfluenceSlots;

            /// <summary>Atlas texel -> input probe ordinal, or -1. Needed by the door pass.</summary>
            public int[] InputProbeForTexel;

            /// <summary>Atlas texel -> surface probe slot in the 256x64 probe texture, or -1.</summary>
            public int[] SurfaceSlotForTexel;
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
            /// <summary>Where visibility rays start; see RadiosityGeometry.VisibilityOrigin.</summary>
            public Vector3 RayOrigin;
        }

        /// <summary>
        /// Lift every live texel's ray origin clear of the occluder shell it sits inside. Done once
        /// for the slice so the influence solve, which tests every receiver against every nearby
        /// emitter, does not repeat the same projection ray millions of times.
        /// </summary>
        private static void ResolveRayOrigins(RadiosityGeometry geometry, SurfaceTexel[] texels,
                                              RadiosityBakeSettings settings)
        {
            for (int i = 0; i < AtlasTexels; i++)
            {
                if (!texels[i].Live) continue;
                texels[i].RayOrigin = geometry.VisibilityOrigin(
                    texels[i].Position, texels[i].Normal,
                    settings.OccluderProjectionRange, settings.ProbeSurfaceOffset);
            }
        }

        private static SliceBake BakeSlice(
            Level level,
            RadiosityGeometry geometry,
            List<RadiosityGeometry.Instance> instances,
            int sliceIndex,
            RadiosityBakeSettings settings,
            Dictionary<int, float> emissiveAreas,
            RetailLightPriors lightPriors,
            Action<string> log)
        {
            var slice = new RadiosityRuntime.RuntimeDataSlice();
            var texels = new SurfaceTexel[AtlasTexels];

            // ---- 1. Rasterise each instance's geometry into its atlas rect -------------------
            foreach (RadiosityGeometry.Instance instance in instances)
                RasteriseInstance(geometry, instance, texels, settings);
            FoldAlbedo(texels);
            ResolveRayOrigins(geometry, texels, settings);

            int liveCount = 0;
            for (int i = 0; i < AtlasTexels; i++) if (texels[i].Live) liveCount++;

            // ---- 2. Surface probes: live texels compacted into 16x16 tiles --------------------
            // Surface probes are NOT atlas-indexed. They are a compacted list packed into the same
            // 256x64 tiled texture as the input probes, which is why the surface probe tree's leaf
            // rects predict the live set exactly in all 128 retail slices, and why the influence
            // maps key on a probe slot rather than an atlas texel.
            var surfaceOrder = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live) surfaceOrder.Add(i);
            SpatialSort(surfaceOrder, i => texels[i].Position);
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
                slice.SurfaceProbePositions[slot] = new Vector4(texels[surfaceOrder[p]].Position, ProbeNormalisation);
            }

            // ---- 3. Input probes: emitters repacked into 16x16 tiles -------------------------
            var liveTexels = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live) liveTexels.Add(i);

            // Input probes are scattered over the surfaces themselves and thinned, not taken from
            // atlas texels. That is how retail builds them: a very large number of candidate points
            // over every surface, then a uniform exclusion pass down to a Poisson-disc spacing.
            //
            // The distinction matters because atlas texels are only as evenly spread in the world
            // as the authored UV packing happens to be, and it is not: sizing rects from surface
            // area leaves texel density varying by orders of magnitude between instances, which
            // left a third of the cells retail fills with no probes of ours at all. Sampling the
            // triangles directly makes emitter coverage independent of the atlas entirely.
            List<ProbePoint> inputProbes = ScatterInputProbes(geometry, instances, settings);

            // Order spatially before assigning tile slots, so a 16x16 tile holds probes that are
            // near each other in the world. The probe tree's leaves are those same tiles, so their
            // bounds stay tight - which is what retail's tree looks like.
            SpatialSortPoints(inputProbes);

            int probeBudget = Math.Min(MaxInputProbes, settings.MaxInputProbesPerSlice);
            if (inputProbes.Count > probeBudget)
                inputProbes.RemoveRange(probeBudget, inputProbes.Count - probeBudget);

            // Every live texel reads bounced light from, and scatters into, its nearest probe.
            int[] nearestProbeForTexel = BuildNearestProbeMap(geometry, texels, liveTexels, inputProbes, settings);

            slice.InputProbePositions = NewList<Vector4u16>(AtlasTexels);
            slice.InputProbeNormals = NewList<ColourRGBA8>(AtlasTexels);
            slice.InputProbeAlbedo = NewList<ColourRGBA8>(AtlasTexels);

            slice.InputProbeTiles = new List<RadiosityRuntime.ProbeTileDims>();
            for (int p = 0; p < inputProbes.Count; p++)
            {
                InputProbeTexel(p, out int px, out int py);
                int dest = py * ProbeTexWidth + px;
                ProbePoint src = inputProbes[p];
                slice.InputProbePositions[dest] = ToHalf4(src.Position, ProbeNormalisation);
                slice.InputProbeNormals[dest] = EncodeNormal(src.Normal);
                slice.InputProbeAlbedo[dest] = EncodeAlbedo(src.Albedo, 255);
            }
            BuildInputProbeTiles(inputProbes.Count, slice.InputProbeTiles);

            // ---- 4. Clusters -----------------------------------------------------------------
            // Clusters are the emitters, indexed by atlas texel, and every live texel is one -
            // they are the surface elements that radiate, so thinning them would throw away
            // emitted light rather than just sampling positions. Retail carries more clusters than
            // either probe set (16384 against 9553 input and 12799 surface on Solace slice 0).
            // A cluster with no input probe of its own scatters into its nearest one.
            slice.ClusterPositions = new List<Vector4u16>(AtlasTexels);
            for (int i = 0; i < AtlasTexels; i++)
            {
                slice.ClusterPositions.Add(texels[i].Live && nearestProbeForTexel[i] >= 0
                    ? ToHalf4(texels[i].Position, 1.0f)
                    : new Vector4u16());
            }

            // ---- 5. Mangle map: atlas texel -> SURFACE PROBE SLOT ----------------------------
            // This is the lightmap reconstruction map: the engine builds the 128x128 lightmap by
            // sampling the compacted 256x64 surface-probe radiance texture at the slot each entry
            // names. Decoded from retail by adjacency: neighbouring atlas texels land on surface
            // probes a median 1.06 m apart (one texel pitch) under the slot interpretation, against
            // 2.6+ m and 45% dead addresses under the input-probe interpretation this used to
            // write. Writing input-probe texels here made every lightmap texel display a wrong -
            // usually nearby, occasionally distant - probe's radiance, which was the whole family
            // of "random model picks up light from elsewhere" leaks.
            // Every texel resolves to a source, exactly as retail's mangle maps do - all 16384
            // entries of all 128 retail slices resolve. Writing "no source" for dead texels was
            // what rendered whole surfaces black: any mesh whose lightmap UVs stray outside the
            // rasterised island plus its two dilation texels sampled an unlit address, and large
            // rects with small islands showed it as contiguous black panels (ceilings especially).
            // Dead texels take the source of the nearest live texel: within their own instance's
            // rect first, so nothing bleeds between islands, then anywhere as a last resort.
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
                // TODO: retail stores a second source slot in (B, A) with 2 blend bits in the
                // high bits of A, used to filter across tile seams. A single source is valid but
                // slightly harder-edged at tile boundaries.
                slice.MangleMap[i] = new ColourRGBA8 { R = (byte)px, G = (byte)py, B = 255, A = 63 };
            }

            // ---- 6. Visibility solve: influences per surface probe ---------------------------
            int influenceCount = SolveInfluences(geometry, texels, surfaceSlotForTexel, nearestProbeForTexel, slice, settings, out var transfers, out byte[] usedSlots, log);
            // Which instances came out of the solve with no lit texel at all? An instance whose
            // whole rect is unlinked renders solid black however healthy the rest of the slice is,
            // and that is the shape of the remaining "randomly dark model" reports - so name them
            // rather than infer them from screenshots.
            if (log != null)
            {
                int unlitInstances = 0, unlitTexels = 0;
                var worst = new List<(int area, int live, string what)>();
                foreach (RadiosityGeometry.Instance inst in instances)
                {
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

            // The scatter list is the same data viewed from the input probe's side.
            slice.Scatter = settings.EmitScatter
                ? (settings.LocalScatter
                    ? BuildScatterListLocal(geometry, inputProbes, texels, nearestProbeForTexel, settings)
                    : BuildScatterList(transfers, nearestProbeForTexel, inputProbes, texels, settings))
                : new List<ColourRGBA8>();

            // ---- 7. Probe trees --------------------------------------------------------------
            slice.InputProbeTreeNodes = BuildProbeTree(inputProbes.Count,
                i => inputProbes[i].Position, out List<uint> inputQuads);
            slice.InputProbeTreeQuads = inputQuads;

            // The surface tree's leaves are the surface probe tiles, in the same order the probes
            // were packed in step 2.
            slice.SurfaceProbeTreeNodes = BuildProbeTree(surfaceOrder.Count,
                i => texels[surfaceOrder[i]].Position, out List<uint> surfaceQuads);
            slice.SurfaceProbeTreeQuads = surfaceQuads;

            // ---- 8. Volume probe hash for dynamic objects ------------------------------------
            List<byte[]> visGrids = new List<byte[]>();

            slice.VolumeProbeHash = settings.EmitVolumeProbes

                ? BuildVolumeProbeHash(geometry, texels, nearestProbeForTexel, settings, out visGrids)

                // An absent hash is signalled by NumSubdivsPerLevel = 0, with a zero AABB and zero
                // dims - that is exactly what BSP_LV426_PT01 slice 0 ships, the one retail slice of
                // 128 with no volume probes. Every populated hash uses 3. Leaving the subdivision
                // count at 3 while writing no items, nodes or offsets is a combination that occurs
                // in no retail level: it tells the object probe pass there is a hash to walk and
                // then hands it nothing, which is where render_object_probes faults.
                : new RadiosityRuntime.VolumeProbeHash();

            // ---- 9. Emissive geometry becomes surface lights ---------------------------------
            slice.SurfaceLights = settings.EmitSurfaceLights

                ? BuildSurfaceLights(level, geometry, instances, texels, nearestProbeForTexel, settings, emissiveAreas, lightPriors)

                : new RadiosityRuntime.RuntimeSurfaceLights { LightSliceEntities = new List<Resources.Resource>() };
            slice.LiveSurfaceLights = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>(slice.SurfaceLights.LightSlices);
            slice.LiveSurfaceLightEntities = new List<Resources.Resource>(slice.SurfaceLights.LightSliceEntities);

            // TODO: the tiled variants of scatter / surface lights / doors, and door transfers are
            // not decoded yet. They are optimisation and door-propagation paths; leaving them
            // empty keeps the file valid.
            slice.TiledScatter = new RadiosityRuntime.TiledScatterData();
            slice.TiledSurfaceLights = new RadiosityRuntime.TiledSurfaceLights();
            // Doors are filled in by BuildDoors once every slice exists.
            slice.Doors = new RadiosityRuntime.DoorInfo();
            slice.TiledDoors = new RadiosityRuntime.TiledDoorInfo();

            log?.Invoke("  slice " + sliceIndex + ": surfaceProbes=" + liveCount +
                        " inputProbes=" + inputProbes.Count +
                        " influences=" + influenceCount +
                        " surfaceLights=" + slice.SurfaceLights.Lights.Count +
                        " lightSlices=" + slice.SurfaceLights.LightSlices.Count);
            WarnOverEngineLimits(slice, sliceIndex, log);

            return new SliceBake
            {
                Slice = slice,
                SurfaceProbeCount = liveCount,
                InputProbeCount = inputProbes.Count,
                ClusterCount = liveCount,
                InfluenceCount = influenceCount,
                VisFaceGrids = visGrids,
                VisFaceIndices = new byte[visGrids.Count],
                Texels = texels,
                UsedInfluenceSlots = usedSlots,
                InputProbeForTexel = nearestProbeForTexel,
                SurfaceSlotForTexel = surfaceSlotForTexel
            };
        }

        #endregion

        /// <summary>
        /// How far, in metres, the endpoints of a visibility ray are jittered across their surface
        /// patches when the centre ray is blocked. An influence link joins two texel-sized patches
        /// (roughly 0.5 m2 each), so testing only centre-to-centre kills every link that a thin
        /// edge, prop, or panel lip happens to cross.
        /// </summary>
        /// <remarks>
        /// The visual cost of that harshness was measured directly: 27% of our probes ended with
        /// 1-29 links against retail's ~5%, and those probes render dark - influence weights are
        /// absolute gains, so a probe with a third of the links gathers a third of the light.
        /// Retail's own links are visibly soft-tested: 34.2% of its 0-1 m links pass through
        /// render geometry. Accepting a link when any patch-jittered ray connects reproduces that
        /// behaviour without renormalising anything.
        /// </remarks>
        private const float SoftVisibilityJitter = 0.22f;

        /// <summary>Extra jittered rays tried after a blocked centre ray.</summary>
        private const int SoftVisibilityRays = 3;

        /// <summary>
        /// Area-to-area visibility: the centre ray first, then a few rays between points jittered
        /// across each endpoint's surface patch. A link is visible when any ray connects.
        /// </summary>
        private static bool VisibleSoft(
            RadiosityGeometry geometry, Vector3 from, Vector3 fromNormal, Vector3 to, Vector3 toNormal,
            RadiosityBakeSettings settings, int fromTexel, int toTexel)
        {
            if (geometry.Visible(from, to, settings.RayEpsilon))
                return true;
            if (!settings.SoftInfluenceVisibility)
                return false;

            // Tangent bases for the two patches, so jitter stays in each surface's plane.
            Vector3 fromT1 = Tangent(fromNormal), fromT2 = Vector3.Cross(fromNormal, fromT1);
            Vector3 toT1 = Tangent(toNormal), toT2 = Vector3.Cross(toNormal, toT1);

            uint seed = (uint)(fromTexel * 92837111) ^ (uint)(toTexel * 689287499);
            for (int i = 0; i < SoftVisibilityRays; i++)
            {
                // Two hashed offsets in -1..1 per endpoint, deterministic for the pair.
                seed = seed * 747796405u + 2891336453u;
                float a = ((seed >> 9) & 0x3FF) / 511.5f - 1.0f;
                seed = seed * 747796405u + 2891336453u;
                float b = ((seed >> 9) & 0x3FF) / 511.5f - 1.0f;
                seed = seed * 747796405u + 2891336453u;
                float c = ((seed >> 9) & 0x3FF) / 511.5f - 1.0f;
                seed = seed * 747796405u + 2891336453u;
                float d = ((seed >> 9) & 0x3FF) / 511.5f - 1.0f;

                Vector3 jFrom = from + (fromT1 * a + fromT2 * b) * SoftVisibilityJitter;
                Vector3 jTo = to + (toT1 * c + toT2 * d) * SoftVisibilityJitter;
                if (geometry.Visible(jFrom, jTo, settings.RayEpsilon))
                    return true;
            }
            return false;
        }

        private static Vector3 Tangent(Vector3 normal)
        {
            Vector3 t = Math.Abs(normal.Y) < 0.9f
                ? Vector3.Cross(normal, Vector3.UnitY)
                : Vector3.Cross(normal, Vector3.UnitX);
            float len = t.Length();
            return len > 1e-6f ? t / len : Vector3.UnitX;
        }

        /// <summary>Upper edge, in metres, of each influence distance band.</summary>
        private static readonly float[] InfluenceBandEdges = { 1.0f, 2.0f, 3.0f, 5.0f, 8.0f, 12.0f, 20.0f, float.MaxValue };

        /// <summary>Which band a distance falls in.</summary>
        private static int BandOf(float distance)
        {
            for (int b = 0; b < InfluenceBandEdges.Length - 1; b++)
                if (distance < InfluenceBandEdges[b]) return b;
            return InfluenceBandEdges.Length - 1;
        }

        /// <summary>
        /// A band's integer slot count for one probe, spending the fractional part as a share of
        /// probes rather than rounding it away.
        /// </summary>
        /// <remarks>
        /// A quota of 0.5 means half of all probes should get one slot in that band, not that every
        /// probe should get zero. The dither is a hash of the probe's atlas texel and the band, so
        /// it is deterministic - the same level always bakes the same output - and uncorrelated
        /// between bands.
        /// </remarks>
        private static int DitheredQuota(float quota, int probeTexel, int band)
        {
            int whole = (int)quota;
            float fraction = quota - whole;
            if (fraction <= 0.0f)
                return whole;

            // 32-bit integer hash (Wang), so neighbouring texels land on unrelated values.
            uint h = (uint)(probeTexel * 73856093) ^ (uint)(band * 19349663);
            h = (h ^ 61) ^ (h >> 16);
            h += h << 3;
            h ^= h >> 4;
            h *= 0x27d4eb2d;
            h ^= h >> 15;

            return whole + ((h & 0xFFFFFF) < (uint)(fraction * 0x1000000) ? 1 : 0);
        }

        /// <summary>
        /// How many of a probe's 32 influence slots retail gives to each distance band, measured
        /// over Solace's 794570 links (27.9 of 32 slots used on average).
        /// </summary>
        private static readonly float[] InfluenceBandQuota = { 2.6f, 6.1f, 6.7f, 6.7f, 3.8f, 1.4f, 0.5f, 0.1f };

        /// <summary>
        /// Reorder a probe's candidates so the kept prefix spans a range of distances, rather than
        /// being whatever happened to rank highest.
        /// </summary>
        /// <remarks>
        /// <para>Ranking purely by form factor means ranking by proximity: at 1/d^2 a candidate
        /// 0.3 m away outranks one at 10 m by a thousand to one, so the top 32 are simply the
        /// nearest 32. That is fine until a probe sits in open space without 32 near neighbours,
        /// at which point it takes a fistful of distant emitters all at once - an all-or-nothing
        /// behaviour that showed up clearly against retail: retail gives a long-range link to 37.8%
        /// of its probes at an average of 2.8 each, while we gave them to 11.0% at 9.1 each. The
        /// totals were nearly the same; the spread was not, and a few probes each fanning out
        /// across a room is what reads as stray influence lines.</para>
        /// <para>Filling per-band quotas first and then backfilling from what is left keeps the
        /// strongest near contributors while reserving room for the far ones every probe should
        /// have. Bands with no candidates simply give their slots back to the backfill.</para>
        /// <para>A band's quota is fractional and has to stay that way. Rounding it per probe sent
        /// the 12-20 m band (0.5) and the 20 m+ band (0.1) to zero on every probe, so a long link
        /// could only ever arrive through the backfill - which happens exactly when a probe is
        /// short of near candidates. That reproduced the very clumping the stratification exists to
        /// prevent: 14.9% of probes carried a long link at 7.4 each against retail's 37.8% at 2.8.
        /// Dithering on the probe's own identity spends the fraction as a share of probes instead,
        /// deterministically, so the totals are unchanged but the spread is retail's.</para>
        /// </remarks>
        private static void StratifyByDistance(List<(int texel, float weight, float distance, float cosProduct)> candidates,
                                               int keep, int probeTexel, RadiosityBakeSettings settings)
        {
            if (candidates.Count <= keep)
                return;
            // Natural selection: candidates arrive strongest-first, so leaving them untouched
            // keeps the top form factors and lets sparse spaces stay sparse.
            if (!settings.StratifyInfluencesByDistance)
                return;

            var chosen = new List<int>(keep);
            var taken = new bool[candidates.Count];

            for (int band = 0; band < InfluenceBandEdges.Length && chosen.Count < keep; band++)
            {
                float low = band == 0 ? 0.0f : InfluenceBandEdges[band - 1];
                float high = InfluenceBandEdges[band];
                int quota = DitheredQuota(InfluenceBandQuota[band], probeTexel, band);
                if (quota <= 0)
                    continue;

                // Candidates are already in descending form factor, so the first match in a band
                // is that band's strongest.
                for (int i = 0; i < candidates.Count && quota > 0 && chosen.Count < keep; i++)
                {
                    if (taken[i]) continue;
                    float d = candidates[i].distance;
                    if (d < low || d >= high) continue;
                    taken[i] = true;
                    chosen.Add(i);
                    quota--;
                }
            }

            // Bands that were empty leave slots over; give them to the strongest remaining.
            for (int i = 0; i < candidates.Count && chosen.Count < keep; i++)
            {
                if (taken[i]) continue;
                taken[i] = true;
                chosen.Add(i);
            }

            // Move the selection to the front, strongest first, so the caller's prefix is it.
            var selected = new List<(int texel, float weight, float distance, float cosProduct)>(chosen.Count);
            foreach (int i in chosen) selected.Add(candidates[i]);
            selected.Sort((a, b) => b.weight.CompareTo(a.weight));
            for (int i = 0; i < selected.Count; i++) candidates[i] = selected[i];
        }

        #region PROBE PLACEMENT

        /// <summary>
        /// Report any slice array that exceeds the size its structure allows.
        /// </summary>
        /// <remarks>
        /// Retail sits comfortably under every one of these - Solace's worst case is 38 of 64 input
        /// probe tiles and 75 of 128 input tree nodes. They are checked because a level denser than
        /// Solace could quietly cross one, and the failure would show up as corrupt lighting or a
        /// fault in the render pass rather than anything obvious at bake time.
        /// </remarks>
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
        /// Scatter candidate probes over every triangle of a slice, then thin them to a
        /// Poisson-disc spacing.
        /// </summary>
        /// <remarks>
        /// <para>This is retail's approach: cover the surfaces in far more points than are wanted,
        /// then uniformly exclude down to an even density. Candidates are laid down in proportion
        /// to triangle area at <see cref="RadiosityBakeSettings.InputProbeCandidatesPerSquareMetre"/>
        /// per square metre, which at the default gives a few hundred thousand over a level, and
        /// the survivors are whatever the dart-throwing keeps.</para>
        /// <para>Sampling the geometry rather than the atlas is the whole point: it makes emitter
        /// placement independent of how evenly the authored UVs happen to pack, which varies by
        /// orders of magnitude between instances.</para>
        /// </remarks>
        private static List<ProbePoint> ScatterInputProbes(
            RadiosityGeometry geometry,
            List<RadiosityGeometry.Instance> instances,
            RadiosityBakeSettings settings)
        {
            float spacing = Math.Max(0.01f, settings.InputProbeSpacing);
            float perSquareMetre = Math.Max(1.0f, settings.InputProbeCandidatesPerSquareMetre);

            // Dart-throwing needs a scrambled visit order, so candidates are accumulated per
            // triangle and then walked in a hashed sequence rather than in geometry order.
            var candidates = new List<ProbePoint>();
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
                            Albedo = geometry.SampleAlbedo(tri, diffuseUv),
                        });
                    }
                }
            }
            if (candidates.Count == 0)
                return new List<ProbePoint>();

            var order = new int[candidates.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => Scramble(a).CompareTo(Scramble(b)));

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
                accepted.Add(candidates[index]);
            }

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
            SpatialSort(order, i => keys[i]);

            var sorted = new List<ProbePoint>(probes.Count);
            foreach (int i in order) sorted.Add(probes[i]);
            probes.Clear();
            probes.AddRange(sorted);
        }

        /// <summary>
        /// Thin a set of live atlas texels down to a blue-noise subset, keeping no two probes
        /// closer than <paramref name="spacing"/> metres.
        /// </summary>
        /// <remarks>
        /// <para>Dart-throwing over the existing candidates, which is the cheap equivalent of
        /// retail's approach of scattering far too many probes over the surfaces and then
        /// discarding from the dense regions until an even density is left.</para>
        /// <para>Candidates are visited in a scrambled order rather than atlas order. Sweeping the
        /// atlas row by row would accept a regular lattice instead of blue noise, and would
        /// systematically favour whichever instances happen to be packed into the low corner of
        /// the atlas.</para>
        /// </remarks>
        private static List<int> PoissonThin(List<int> candidates, SurfaceTexel[] texels, float spacing)
        {
            if (spacing <= 0.0f || candidates.Count == 0)
                return new List<int>(candidates);

            // Visit order: sort by a hash of the texel index, so it is scrambled but deterministic.
            var order = new List<int>(candidates);
            order.Sort((a, b) => Scramble(a).CompareTo(Scramble(b)));

            float radiusSq = spacing * spacing;
            float cell = spacing;
            var grid = new Dictionary<(int, int, int), List<int>>();
            var accepted = new List<int>();

            foreach (int texel in order)
            {
                Vector3 p = texels[texel].Position;
                int cx = (int)Math.Floor(p.X / cell), cy = (int)Math.Floor(p.Y / cell), cz = (int)Math.Floor(p.Z / cell);

                bool tooClose = false;
                for (int dx = -1; dx <= 1 && !tooClose; dx++)
                    for (int dy = -1; dy <= 1 && !tooClose; dy++)
                        for (int dz = -1; dz <= 1 && !tooClose; dz++)
                        {
                            if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int> bucket))
                                continue;
                            foreach (int other in bucket)
                                if (Vector3.DistanceSquared(texels[other].Position, p) < radiusSq) { tooClose = true; break; }
                        }
                if (tooClose)
                    continue;

                accepted.Add(texel);
                var key = (cx, cy, cz);
                if (!grid.TryGetValue(key, out List<int> own)) grid[key] = own = new List<int>();
                own.Add(texel);
            }

            return accepted;
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
            List<int> liveTexels, List<ProbePoint> probes, RadiosityBakeSettings settings)
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
                    settings.OccluderProjectionRange, settings.ProbeSurfaceOffset);

            var found = new List<(int probe, float distSq)>();
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

                found.Sort((a, b) => a.distSq.CompareTo(b.distSq));
                if (!settings.ProbeBindingTiers)
                {
                    map[texel] = found[0].probe;
                    continue;
                }
                int tested = Math.Min(found.Count, MaxBindCandidates);

                Vector3 texelNormal = texels[texel].Normal;
                Vector3 rayOrigin = texels[texel].RayOrigin;
                int best = -1, firstNormal = -1, firstVisible = -1;
                for (int i = 0; i < tested; i++)
                {
                    ProbePoint candidate = probes[found[i].probe];
                    bool normalAgrees = Vector3.Dot(texelNormal, candidate.Normal) > 0.3f;
                    if (normalAgrees && firstNormal < 0)
                        firstNormal = found[i].probe;
                    if (!geometry.Visible(rayOrigin, probeOrigins[found[i].probe], settings.RayEpsilon))
                        continue;
                    if (firstVisible < 0)
                        firstVisible = found[i].probe;
                    if (normalAgrees)
                    {
                        best = found[i].probe;
                        break;
                    }
                }
                // A normal-agreeing probe whose ray happens to be blocked (clutter, concave trim)
                // still belongs to this surface; binding across to a visible but opposing-normal
                // probe is what actually bleeds light between rooms.
                if (best < 0) best = firstNormal;
                if (best < 0) best = firstVisible;
                if (best < 0) best = found[0].probe;

                map[texel] = best;
            }

            return map;
        }

        #endregion

        #region RASTERISATION

        /// <summary>
        /// Fill an instance's atlas rect by rasterising its triangles in UV1 space. Meshes with no
        /// lightmap UVs fall back to scattering samples over the rect by triangle area.
        /// </summary>
        private static void RasteriseInstance(RadiosityGeometry geometry, RadiosityGeometry.Instance instance,
                                              SurfaceTexel[] texels, RadiosityBakeSettings settings)
        {
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
                RasteriseByUv(geometry, instance, texels, settings);
                DilateIntoUnclaimed(instance, texels, settings);
                return;
            }

            // With no lightmap UVs at all there is no island to grow out of, so the rect is filled
            // by area-weighted sampling instead.
            FillUnclaimed(geometry, instance, texels);
        }

        /// <summary>
        /// Grow the rasterised UV islands a few texels into the rect around them.
        /// </summary>
        /// <remarks>
        /// <para>The texels an instance's UV islands do not reach used to be filled by scattering
        /// samples over its triangles, on the reasoning that a rect should contain no dead texels.
        /// That put probes on surfaces the islands never map - the inside of a closed prop, the back
        /// of a panel flat against a wall - where nothing is visible, so they solved to no influence
        /// at all and rendered black. It is most of the difference between our 22.1% of surface
        /// probes with an empty influence list and retail's 7.4%, and 9.5% with fewer than four
        /// against retail's 1.3%.</para>
        /// <para>Those texels are never sampled directly, since no triangle maps to them - but the
        /// shader filters bilinearly, so the ring immediately outside an island is read at the
        /// island's edge. A black ring there is a dark fringe around every UV island, which is what
        /// the hard borders in the render are. Dilation is the standard answer: the border texels
        /// take their neighbours' surface, and everything further out stays dead rather than
        /// inventing a probe somewhere the light cannot reach.</para>
        /// </remarks>
        /// <summary>Orthogonal neighbours before diagonal ones, so a donor shares an edge if it can.</summary>
        private static readonly (int dx, int dy)[] DilationNeighbours =
        {
            (-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)
        };

        private static void DilateIntoUnclaimed(RadiosityGeometry.Instance instance, SurfaceTexel[] texels,
                                                RadiosityBakeSettings settings)
        {
            int passes = settings.AtlasDilationPasses;
            if (passes <= 0 || instance.AtlasWidth <= 0 || instance.AtlasHeight <= 0)
                return;

            int x0 = instance.AtlasX, y0 = instance.AtlasY;
            int x1 = Math.Min(AtlasSize, x0 + instance.AtlasWidth);
            int y1 = Math.Min(AtlasSize, y0 + instance.AtlasHeight);

            for (int pass = 0; pass < passes; pass++)
            {
                var grown = new List<(int index, SurfaceTexel texel)>();

                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        int index = y * AtlasSize + x;
                        if (texels[index].Live)
                            continue;

                        // One donor, copied whole, rather than an average of the neighbours. A rect
                        // holds both faces of a thin panel, so averaging across an island edge puts
                        // the point between them - inside the geometry, where every visibility ray
                        // is blocked and the probe solves to nothing. Averaging cost 1412 extra
                        // empty probes on Solace where copying costs none.
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

        private static void RasteriseByUv(RadiosityGeometry geometry, RadiosityGeometry.Instance instance, SurfaceTexel[] texels, RadiosityBakeSettings settings)
        {
            // All of a composite's movers share one atlas rect and each addresses it with its own
            // 0..1 lightmap UVs, so their footprints overlap: on Solace, 61.6% of mover pairs in a
            // composite overlap by more than half, and the top deciles overlap completely. Letting
            // the first arrival keep every texel it touches therefore hands the whole rect to one
            // mover and leaves the rest of the composite with no probes at all - which is why our
            // coverage missed 38.7% of the cells retail fills while running up to 20x its density
            // in others, and why a corridor's floor could vanish while its walls stayed dense.
            //
            // Instead each mover may claim only its area's share of the rect. Whatever is left
            // over goes to FillUnclaimed, which already spreads by triangle area.
            int rectTexels = Math.Max(1, instance.AtlasWidth * instance.AtlasHeight);
            var quota = new int[instance.Movers.Count];
            var claimed = new int[instance.Movers.Count];
            float totalArea = Math.Max(1e-6f, instance.SurfaceArea);
            for (int m = 0; m < quota.Length; m++)
            {
                float share = m < instance.MoverAreas.Count ? instance.MoverAreas[m] : 0.0f;
                // At least one texel for any mover that has surface at all, so a small prop inside
                // a large composite is represented rather than rounded away.
                quota[m] = share <= 0.0f ? 0 : Math.Max(1, (int)Math.Round(rectTexels * share / totalArea));
            }

            foreach (int tri in instance.Triangles)
            {
                int moverSlot = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                if (moverSlot < 0 || moverSlot >= quota.Length)
                    moverSlot = 0;

                // Optionally let an emitter through regardless of budget. The reasoning was that a
                // light is not an area claim - it is the only way that surface enters the solve, so
                // losing it to the quota removes light from the level rather than just moving a
                // probe (#35). Measured on SCI_Hub it does not do what it was meant to: light
                // slices went 1416 to 1386 rather than up towards retail's 1818, and the render
                // scored slightly worse (rmse 22.97 to 23.88). Off until something explains that.
                bool exempt = settings.ExemptEmissiveFromRectQuota
                              && tri < geometry.TriangleEmissive.Length
                              && geometry.TriangleEmissive[tri] != Vector3.Zero;
                if (!exempt && quota.Length > 0 && claimed[moverSlot] >= quota[moverSlot])
                    continue;

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

                        // Albedo integrates over the texel on a sub-grid. A texel covers about
                        // half a square metre and Solace averages 53 triangles across one, so a
                        // single tap at the centre reports one facet's colour as the whole
                        // footprint's - and since most triangles are smaller than a texel, it is
                        // usually the only tap that lands at all. Retail's own table carries 16.5
                        // samples per probe for the same reason, and it is that averaging which
                        // gives its probe albedos their continuum of values: 33.7% of them are
                        // unique, against 5.7% when each probe takes a single tap.
                        // The same taps also say whether this triangle touches the texel's footprint
                        // at all, which is what decides coverage below, so their barycentrics are
                        // accumulated to give a representative point inside the covered part.
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

                        if (texels[index].Live)
                            continue;

                        float px = x + 0.5f, py = y + 0.5f;
                        float l0 = a0 * (px - uv2.X) + b0 * (py - uv2.Y);
                        float l1 = a1 * (px - uv2.X) + b1 * (py - uv2.Y);
                        float l2 = 1.0f - l0 - l1;

                        // A small negative tolerance keeps seams between adjacent triangles filled.
                        const float bias = -0.02f;
                        if (l0 < bias || l1 < bias || l2 < bias)
                        {
                            // Conservative coverage: the centre is outside, but if any sub-tap
                            // landed inside then the triangle does cross this texel's footprint and
                            // the texel is part of the surface. Testing centres alone gave a UV
                            // island narrower than a texel a single live texel - or none - and
                            // dilation then copied that one point across the whole rect, so half of
                            // BSP_TORRENS' models ended up with every probe at the same world
                            // position and took their entire lighting from one sample. Retail's
                            // median model spans 1.0 m of probe positions where ours spanned 0.
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

                        geometry.SamplePoint(tri, l1, l2, out Vector3 position, out Vector3 normal, out _, out Vector2 diffuseUv);
                        texels[index].Position = position;
                        texels[index].Normal = normal;
                        texels[index].Emissive = geometry.TriangleEmissive[tri];
                        texels[index].MoverIndex = moverIndex;
                        texels[index].Live = true;
                        claimed[moverSlot]++;

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

                // There is no texel footprint to integrate over here - these samples are scattered
                // rather than rasterised - so spread a few extra taps across the triangle instead,
                // which at least stops one point on one facet standing for the whole texel.
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

        #region VISIBILITY SOLVE

        /// <summary>
        /// For every live surface probe, find the clusters that can see it and store the strongest
        /// <see cref="InfluencesPerProbe"/> of them as (clusterXY, weight) pairs.
        /// </summary>
        /// <param name="surfaceSlotForTexel">
        /// Atlas texel to surface probe slot. The influence maps are keyed by slot, not by texel -
        /// every one of the 1195115 populated retail probe records sits at a live surface probe.
        /// </param>
        /// <remarks>
        /// <paramref name="usedSlots"/> comes back keyed by surface probe slot so the cross-slice
        /// pass can carry on filling the same records.
        /// </remarks>
        private static int SolveInfluences(
            RadiosityGeometry geometry, SurfaceTexel[] texels, int[] surfaceSlotForTexel,
            int[] inputProbeForTexel,
            RadiosityRuntime.RuntimeDataSlice slice,
            RadiosityBakeSettings settings, out List<(int emitter, int receiver, float weight)> transfers,
            out byte[] usedSlots, Action<string> log)
        {
            var collected = new System.Collections.Concurrent.ConcurrentBag<(int, int, float)>();
            usedSlots = new byte[AtlasTexels];
            byte[] used = usedSlots;
            slice.SurfaceProbeInfluences = NewList<ColourRGBA8>(AtlasTexels * InfluencesPerProbe / 2);
            slice.SurfaceProbeWeights = NewList<Vector4u8>(AtlasTexels * InfluencesPerProbe / 4);

            var live = new List<int>();
            for (int i = 0; i < AtlasTexels; i++) if (texels[i].Live && surfaceSlotForTexel[i] >= 0) live.Add(i);
            if (live.Count == 0)
            {
                transfers = new List<(int, int, float)>();
                return 0;
            }

            // Only a texel that became a cluster can be named as an influence, so the emitter grid
            // is the cluster set - not the receiver set.
            var emitters = new List<int>();
            for (int i = 0; i < AtlasTexels; i++) if (texels[i].Live && inputProbeForTexel[i] >= 0) emitters.Add(i);

            // Uniform grid over the emitters so each receiver only tests nearby candidates.
            var grid = new ProbeGrid(texels, emitters, settings.MaxInfluenceDistance);

            int total = 0;
            object totalLock = new object();

            // A probe that ends up with no influence renders with no bounced light, and next to a
            // lit one that is a hard edge. Counting why they fail separates "nothing faces it" from
            // "everything that faces it is occluded", which want different fixes.
            int noCandidates = 0, allOccluded = 0, noneFacing = 0;

            // Visibility rays cast per receiver, and how far they reached. One ray is cast per
            // candidate that survives the distance and mutual-facing tests, so this is the solve's
            // real cost and the honest answer to "how many rays per probe".
            var raysPerProbe = new int[live.Count];
            var rayLengthSum = new double[live.Count];
            var rayLengthMax = new float[live.Count];

            // Retail's influence count per probe is bimodal - a probe is either empty or full at
            // 30-32 - where ours ramps through every value in between. To tell whether the thin
            // ones are starved of candidates or drowning in occlusion, record for each probe how
            // many candidates reached the ray test and how many survived it.
            var facedPerProbe = new int[live.Count];
            var keptPerProbe = new int[live.Count];
            var nearestBlockedPerProbe = new float[live.Count];

            // Supply against spend, per distance band. The near bands are the ones that carry the
            // energy, so a shortfall there is not cosmetic: whatever a band cannot fill is handed
            // to the backfill, which spends it further out. Telling "no candidates were offered"
            // apart from "candidates were offered and passed over" needs both halves counted.
            var bandOffered = new long[InfluenceBandEdges.Length];
            var bandTaken = new long[InfluenceBandEdges.Length];

            void Solve(int liveIndex)
            {
                int probeTexel = live[liveIndex];
                SurfaceTexel probe = texels[probeTexel];
                Vector3 origin = probe.RayOrigin;

                int facing = 0, occluded = 0;
                var candidates = new List<(int texel, float weight, float distance, float cosProduct)>();
                foreach (int otherTexel in grid.Neighbours(probe.Position))
                {
                    if (otherTexel == probeTexel)
                        continue;

                    SurfaceTexel other = texels[otherTexel];
                    Vector3 delta = other.Position - origin;
                    float distanceSq = delta.LengthSquared();
                    if (distanceSq < 1e-6f || distanceSq > settings.MaxInfluenceDistance * settings.MaxInfluenceDistance)
                        continue;

                    float distance = (float)Math.Sqrt(distanceSq);
                    Vector3 direction = delta / distance;

                    // Diffuse form factor: both surfaces must face each other.
                    float cosReceiver = Vector3.Dot(probe.Normal, direction);
                    if (cosReceiver <= 0.02f)
                        continue;
                    float cosEmitter = Vector3.Dot(other.Normal, -direction);
                    if (cosEmitter <= 0.02f)
                        continue;

                    float formFactor = cosReceiver * cosEmitter / (float)(Math.PI * distanceSq);
                    if (formFactor <= 1e-5f)
                        continue;

                    facing++;
                    raysPerProbe[liveIndex]++;
                    rayLengthSum[liveIndex] += distance;
                    if (distance > rayLengthMax[liveIndex]) rayLengthMax[liveIndex] = distance;

                    Vector3 emitterOrigin = other.RayOrigin;
                    if (!VisibleSoft(geometry, origin, probe.Normal, emitterOrigin, other.Normal,
                                     settings, probeTexel, otherTexel))
                    {
                        occluded++;
                        if (nearestBlockedPerProbe[liveIndex] == 0f || distance < nearestBlockedPerProbe[liveIndex])
                            nearestBlockedPerProbe[liveIndex] = distance;
                        continue;
                    }

                    candidates.Add((otherTexel, formFactor, distance, cosReceiver * cosEmitter));
                }

                facedPerProbe[liveIndex] = facing;
                keptPerProbe[liveIndex] = candidates.Count;

                if (candidates.Count == 0)
                {
                    lock (totalLock)
                    {
                        noCandidates++;
                        if (facing == 0) noneFacing++;
                        else if (occluded == facing) allOccluded++;
                    }
                    return;
                }

                candidates.Sort((a, b) => b.weight.CompareTo(a.weight));
                if (candidates[0].weight <= 0)
                    return;

                int keep = Math.Min(InfluencesPerProbe, Math.Min(candidates.Count, settings.InfluencesPerSurfaceProbe));
                StratifyByDistance(candidates, keep, probeTexel, settings);

                // NOTE: influence weights are absolute gains, not a normalised kernel - scaling a
                // probe's weights up to retail's per-probe total (~4900) was tested and brightened
                // the whole render by ~1.7x (mean rmse 30.8 -> 63.0). Retail's tight weight-sum
                // band falls out of nearly every probe carrying ~32 links; parity comes from
                // finding that many links, not from renormalising.
                for (int k = 0; k < keep; k++)
                {
                    int otherTexel = candidates[k].texel;
                    ClusterRef(otherTexel, out byte cx, out byte cy);
                    byte weight = InfluenceWeight(candidates[k].distance, candidates[k].cosProduct, settings);

                    int influenceSlot = surfaceSlotForTexel[probeTexel] * InfluencesPerProbe + k;
                    WriteInfluence(slice, influenceSlot, cx, cy, weight);
                    collected.Add((otherTexel, probeTexel, candidates[k].weight));
                }

                used[surfaceSlotForTexel[probeTexel]] = (byte)keep;

                if (log != null)
                {
                    var offered = new int[InfluenceBandEdges.Length];
                    var taken = new int[InfluenceBandEdges.Length];
                    for (int i = 0; i < candidates.Count; i++)
                        offered[BandOf(candidates[i].distance)]++;
                    for (int k = 0; k < keep; k++)
                        taken[BandOf(candidates[k].distance)]++;
                    lock (totalLock)
                    {
                        total += keep;
                        for (int b = 0; b < InfluenceBandEdges.Length; b++)
                        {
                            bandOffered[b] += offered[b];
                            bandTaken[b] += taken[b];
                        }
                    }
                    return;
                }

                lock (totalLock) total += keep;
            }

            if (settings.Parallel)
                Parallel.For(0, live.Count, Solve);
            else
                for (int i = 0; i < live.Count; i++) Solve(i);

            if (log != null)
            {
                long totalRays = 0;
                double totalLength = 0;
                float longest = 0;
                var sorted = new int[live.Count];
                for (int i = 0; i < live.Count; i++)
                {
                    totalRays += raysPerProbe[i];
                    totalLength += rayLengthSum[i];
                    if (rayLengthMax[i] > longest) longest = rayLengthMax[i];
                    sorted[i] = raysPerProbe[i];
                }
                Array.Sort(sorted);
                var deciles = new string[11];
                for (int i = 0; i < 11; i++)
                    deciles[i] = sorted[Math.Min(sorted.Length - 1, (int)((long)i * sorted.Length / 10))].ToString();

                // Bucket probes by how many influences they ended with, and show what the ray test
                // was given to work with in each bucket. Starved candidates and heavy occlusion
                // want opposite fixes.
                int[] edges = { 1, 4, 8, 16, 32, int.MaxValue };
                string[] names = { "0", "1-3", "4-7", "8-15", "16-31", "32" };
                var bucketProbes = new int[edges.Length];
                var bucketFaced = new double[edges.Length];
                var bucketBlocked = new double[edges.Length];
                for (int i = 0; i < live.Count; i++)
                {
                    int kept = keptPerProbe[i];
                    int b = 0;
                    while (b < edges.Length - 1 && kept >= edges[b]) b++;
                    if (kept == 0) b = 0;
                    bucketProbes[b]++;
                    bucketFaced[b] += facedPerProbe[i];
                    bucketBlocked[b] += nearestBlockedPerProbe[i];
                }
                for (int b = 0; b < edges.Length; b++)
                {
                    if (bucketProbes[b] == 0) continue;
                    log?.Invoke("      kept " + names[b].PadRight(6) +
                                bucketProbes[b].ToString().PadLeft(6) + " probes  (" +
                                (100.0 * bucketProbes[b] / live.Count).ToString("0.0").PadLeft(5) + "%)" +
                                "   candidates offered " + (bucketFaced[b] / bucketProbes[b]).ToString("0").PadLeft(5) +
                                "   nearest blocked at " + (bucketBlocked[b] / bucketProbes[b]).ToString("0.00") + " m");
                }

                long offeredAll = 0, takenAll = 0;
                for (int b = 0; b < InfluenceBandEdges.Length; b++) { offeredAll += bandOffered[b]; takenAll += bandTaken[b]; }
                log?.Invoke("    influence bands   quota   offered   taken   share of slots   retail share");
                float quotaSum = 0;
                for (int b = 0; b < InfluenceBandEdges.Length; b++) quotaSum += InfluenceBandQuota[b];
                for (int b = 0; b < InfluenceBandEdges.Length; b++)
                {
                    string range = (b == 0 ? "0" : InfluenceBandEdges[b - 1].ToString("0")) + "-" +
                                   (InfluenceBandEdges[b] == float.MaxValue ? "inf" : InfluenceBandEdges[b].ToString("0"));
                    log?.Invoke("      " + range.PadRight(10) +
                                InfluenceBandQuota[b].ToString("0.0").PadLeft(9) +
                                bandOffered[b].ToString().PadLeft(10) +
                                bandTaken[b].ToString().PadLeft(9) +
                                (100.0 * bandTaken[b] / Math.Max(1, takenAll)).ToString("0.0").PadLeft(14) + "%" +
                                (100.0 * InfluenceBandQuota[b] / quotaSum).ToString("0.0").PadLeft(14) + "%");
                }

                log?.Invoke("    visibility rays: " + totalRays + " total, " +
                            (totalRays / (double)live.Count).ToString("0") + " per probe" +
                            "   deciles " + string.Join(" ", deciles) +
                            "   mean length " + (totalRays == 0 ? 0 : totalLength / totalRays).ToString("0.00") +
                            " m, longest " + longest.ToString("0.0") + " m of " +
                            settings.MaxInfluenceDistance.ToString("0") + " m allowed");
            }

            if (noCandidates > 0)
                log?.Invoke("    probes left with no influence: " + noCandidates + " of " + live.Count +
                            " (" + (100.0 * noCandidates / live.Count).ToString("0.0") + "%)" +
                            "   nothing facing them: " + noneFacing +
                            "   everything facing them occluded: " + allOccluded);

            transfers = collected.ToList();
            return total;
        }

        /// <summary>
        /// Build the scatter point list consumed by CA_RADIOSITY_INDIRECT_SCATTER: for each input
        /// probe, the clusters whose radiance it gathers.
        /// </summary>
        /// <remarks>
        /// <para>Each entry is <c>(R, G, B, A)</c> where <c>(R, G)</c> is the source cluster,
        /// recombined as <c>G * 256 + R</c>, and <c>(B, A)</c> is the destination input probe texel,
        /// recombined as <c>A * 256 + B</c>. Entries are grouped so one probe's sources are
        /// contiguous, at most eight per group.</para>
        /// <para>Measured over all 128 retail slices: every source resolves to a live cluster and
        /// every destination to a live input probe inside a declared tile, both 100%, and the set of
        /// sources equals the set of live clusters exactly in every slice. That last property is why
        /// <see cref="CoverUnusedClusters"/> exists - a cluster the engine has positions for but
        /// which never appears here does not occur in any retail level.</para>
        /// <para>The smallest retail slice still ships 502 entries, so an empty list is off the
        /// beaten path, though it is survivable - our Frontend bake emits none and the level loads.</para>
        /// </remarks>
        private static List<ColourRGBA8> BuildScatterList(
            List<(int emitter, int receiver, float weight)> transfers,
            int[] probeForTexel,
            List<ProbePoint> probes,
            SurfaceTexel[] texels,
            RadiosityBakeSettings settings)
        {
            var scatter = new List<ColourRGBA8>();
            if (transfers == null || transfers.Count == 0)
                return scatter;

            int cap = Math.Max(1, Math.Min(8, settings.MaxScatterTargetsPerProbe));

            // Group by destination input probe, not by receiver texel. Since input probes became a
            // thinned subset of the live texels, several receivers share one probe, and the engine's
            // eight-entry limit applies per probe - grouping by texel silently overran it.
            var byProbe = new Dictionary<int, List<(int cluster, float weight)>>();
            foreach ((int emitter, int receiver, float weight) in transfers)
            {
                if (receiver < 0 || receiver >= probeForTexel.Length || probeForTexel[receiver] < 0)
                    continue;
                int probe = probeForTexel[receiver];
                if (!byProbe.TryGetValue(probe, out List<(int, float)> list))
                    byProbe[probe] = list = new List<(int, float)>();
                list.Add((emitter, weight));
            }

            // Keep one slot per group in reserve. Every cluster resolves to an input probe, so it
            // always has a group to fall back into; reserving the last slot is what makes that
            // fallback guaranteed rather than best-effort, and full coverage is a hard invariant.
            int solveCap = Math.Max(1, cap - 1);

            var groups = new Dictionary<int, List<(int cluster, float weight)>>(byProbe.Count);
            foreach (KeyValuePair<int, List<(int cluster, float weight)>> pair in byProbe)
            {
                // One probe can now collect the same cluster from several receivers. Sources
                // beyond ScatterMaxLinkDistance are dropped outright: a scatter link carries the
                // cluster's radiance to the probe with no runtime visibility term, so a long link
                // is light through however many walls lie between. Retail's links measure p50
                // 0.75 m / p99 5.4 m on Solace where ours ran p50 1.64 / p99 8.6 with 6x as many
                // past 10 m - and the excess is exactly what lit its blacked-out sections.
                Vector3 destPosition = probes[pair.Key].Position;
                List<(int cluster, float weight)> sources = pair.Value
                    .GroupBy(s => s.cluster)
                    .Select(g => (cluster: g.Key, weight: g.Max(x => x.weight)))
                    .Where(s => Vector3.DistanceSquared(texels[s.cluster].Position, destPosition) <=
                                ScatterMaxLinkDistance * ScatterMaxLinkDistance)
                    .ToList();
                sources.Sort((a, b) => b.weight.CompareTo(a.weight));
                if (sources.Count > solveCap)
                    sources.RemoveRange(solveCap, sources.Count - solveCap);
                if (sources.Count > 0)
                    groups[pair.Key] = sources;
            }

            CoverUnusedClusters(groups, byProbe, texels, probeForTexel, probes, settings, cap, solveCap);
            // Retail's invariant is 100% of input probes being scatter destinations (ours: 63% on
            // Solace), but blanket nearest-cluster fallback coverage measured net worse (mean rmse
            // 13.81 -> 14.28): it adds gathered energy in already-bright rooms and cannot reach
            // the dark ones, whose nearby clusters are equally dark. Off pending a redistribution
            // that keeps the total energy budget.
            if (settings.CoverScatterDestinations)
                EnsureDestinationCoverage(groups, texels, probeForTexel, probes, solveCap);
            TrimToEntryCeiling(groups, settings.MaxScatterEntriesPerSlice);

            // Emit in input probe order so each destination's sources stay contiguous.
            foreach (int probe in groups.Keys.OrderBy(k => k))
            {
                InputProbeTexel(probe, out int dx, out int dy);
                foreach ((int cluster, float _) in groups[probe])
                {
                    ClusterRef(cluster, out byte cx, out byte cy);
                    scatter.Add(new ColourRGBA8 { R = cx, G = cy, B = (byte)dx, A = (byte)dy });
                }
            }
            return scatter;
        }

        /// <summary>
        /// Retail-shaped scatter: every input probe gathers from its local cluster neighbourhood
        /// directly, rather than from whichever clusters the influence solve happened to route
        /// through it.
        /// </summary>
        /// <remarks>
        /// <para>Retail's measured shape on Solace is ~6 sources per probe at p50 0.75 m / p99
        /// 5.4 m with 100% of input probes covered and every live cluster appearing as a source.
        /// Deriving the list from influence transfers gave 2.5 sources per probe at p50 1.6 m
        /// with a third of probes getting nothing - and this scatter hop is the loop's feed for
        /// everything downstream of the lightmap itself (input probe radiance, volume probes and
        /// therefore every RADIOSITY_DYNAMIC prop).</para>
        /// <para>Sources prefer same-facing nearby clusters (a floor probe averaging its own
        /// floor neighbourhood), falling back to a visibility test when the neighbourhood is
        /// normal-disjoint. Every live cluster is then guaranteed a destination via its own
        /// bound input probe, matching retail's source-side invariant.</para>
        /// </remarks>
        private static List<ColourRGBA8> BuildScatterListLocal(
            RadiosityGeometry geometry,
            List<ProbePoint> probes,
            SurfaceTexel[] texels,
            int[] probeForTexel,
            RadiosityBakeSettings settings)
        {
            var scatter = new List<ColourRGBA8>();
            int cap = Math.Max(1, Math.Min(8, settings.MaxScatterTargetsPerProbe));
            int solveCap = Math.Max(1, cap - 1);
            float radius = Math.Max(0.5f, settings.LocalScatterRadius);
            float reach = Math.Max(radius, settings.LocalScatterReachRadius);

            var clusterTexels = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && probeForTexel[i] >= 0) clusterTexels.Add(i);
            if (clusterTexels.Count == 0)
                return scatter;

            // The grid is built at the outer radius so the top-up passes can query it too; the
            // base neighbourhood is still bounded by `radius` when candidates are filtered.
            var grid = new ProbeGrid(texels, clusterTexels, reach);
            var groups = new Dictionary<int, List<(int cluster, float weight)>>(probes.Count);
            var coveredClusters = new HashSet<int>();

            for (int p = 0; p < probes.Count; p++)
            {
                Vector3 origin = probes[p].Position;
                Vector3 normal = probes[p].Normal;

                var near = new List<(int cluster, float d2, bool agrees)>();
                foreach (int texel in grid.Neighbours(origin))
                {
                    float d2 = Vector3.DistanceSquared(texels[texel].Position, origin);
                    if (d2 > reach * reach)
                        continue;
                    bool agrees = Vector3.Dot(texels[texel].Normal, normal) > 0.2f;
                    near.Add((texel, d2, agrees));
                }
                near.Sort((a, b) => a.d2.CompareTo(b.d2));

                float baseRadiusSq = radius * radius;
                var chosen = new List<(int cluster, float weight)>();
                foreach ((int cluster, float d2, bool agrees) in near)
                {
                    if (chosen.Count >= solveCap)
                        break;
                    if (agrees && d2 <= baseRadiusSq)
                        chosen.Add((cluster, 1.0f / (1.0f + d2)));
                }
                // Normal-disjoint neighbourhood (a probe on a lone strut): take what is visible.
                if (chosen.Count < 3)
                {
                    foreach ((int cluster, float d2, bool agrees) in near)
                    {
                        if (chosen.Count >= solveCap)
                            break;
                        if (agrees || d2 > baseRadiusSq || chosen.Exists(c => c.cluster == cluster))
                            continue;
                        if (geometry.Visible(origin + normal * settings.ProbeSurfaceOffset,
                                             texels[cluster].Position + texels[cluster].Normal * settings.ProbeSurfaceOffset,
                                             settings.RayEpsilon))
                            chosen.Add((cluster, 1.0f / (1.0f + d2)));
                    }
                }

                // Reach pass: only for probes their own metre-scale ball could not fill. This is
                // what puts retail's tail (p90 2.5 m) on the distribution without moving the
                // sources of the probes that were already served, and it is what stops a probe in
                // a sparse neighbourhood ending up with no radiance at all.
                if (chosen.Count < solveCap)
                {
                    foreach ((int cluster, float d2, bool agrees) in near)
                    {
                        if (chosen.Count >= solveCap)
                            break;
                        if (d2 <= baseRadiusSq || !agrees || chosen.Exists(c => c.cluster == cluster))
                            continue;
                        if (geometry.Visible(origin + normal * settings.ProbeSurfaceOffset,
                                             texels[cluster].Position + texels[cluster].Normal * settings.ProbeSurfaceOffset,
                                             settings.RayEpsilon))
                            chosen.Add((cluster, 1.0f / (1.0f + d2)));
                    }
                }

                // Last resort: an unfed probe is a hole in the field, so take the nearest clusters
                // on any terms rather than leave it dark. Retail leaves 0.2% of its probes unfed;
                // a plain radius ball left 5% of ours.
                if (chosen.Count == 0)
                {
                    foreach ((int cluster, float d2, bool agrees) in near)
                    {
                        if (chosen.Count >= Math.Min(3, solveCap))
                            break;
                        chosen.Add((cluster, 1.0f / (1.0f + d2)));
                    }
                }

                if (chosen.Count == 0)
                    continue;
                groups[p] = chosen;
                foreach ((int cluster, float _) in chosen)
                    coveredClusters.Add(cluster);
            }

            // Source-side invariant: every live cluster appears somewhere. Its own bound input
            // probe is the natural home.
            foreach (int cluster in clusterTexels)
            {
                if (coveredClusters.Contains(cluster))
                    continue;
                int probe = probeForTexel[cluster];
                if (probe < 0 || probe >= probes.Count)
                    continue;
                if (!groups.TryGetValue(probe, out List<(int cluster, float weight)> group))
                    groups[probe] = group = new List<(int cluster, float weight)>();
                if (group.Count < cap)
                    group.Add((cluster, 0.0f));
            }

            TrimToEntryCeiling(groups, settings.MaxScatterEntriesPerSlice);

            foreach (int probe in groups.Keys.OrderBy(k => k))
            {
                InputProbeTexel(probe, out int dx, out int dy);
                foreach ((int cluster, float _) in groups[probe])
                {
                    ClusterRef(cluster, out byte cx, out byte cy);
                    scatter.Add(new ColourRGBA8 { R = cx, G = cy, B = (byte)dx, A = (byte)dy });
                }
            }
            return scatter;
        }

        /// <summary>
        /// Give every input probe at least a few scatter sources. In every retail slice 100% of
        /// input probes are scatter destinations, but our influence-derived groups covered only
        /// 63% on Solace: a probe that no receiver texel binds to gets no indirect light at all,
        /// which renders black anywhere direct light does not reach - retail's powered-off rooms
        /// keep a dim ambient wash we lost. Uncovered probes take their nearest facing clusters
        /// within the fallback distance.
        /// </summary>
        private static void EnsureDestinationCoverage(
            Dictionary<int, List<(int cluster, float weight)>> groups,
            SurfaceTexel[] texels,
            int[] probeForTexel,
            List<ProbePoint> probes,
            int solveCap)
        {
            var clusterTexels = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && probeForTexel[i] >= 0) clusterTexels.Add(i);
            if (clusterTexels.Count == 0)
                return;
            var grid = new ProbeGrid(texels, clusterTexels, ScatterMaxFallbackDistance);

            var nearby = new List<(int cluster, float distanceSq)>();
            for (int probe = 0; probe < probes.Count; probe++)
            {
                if (groups.ContainsKey(probe))
                    continue;

                Vector3 position = probes[probe].Position;
                nearby.Clear();
                foreach (int texel in grid.Neighbours(position))
                {
                    float d = Vector3.DistanceSquared(texels[texel].Position, position);
                    if (d > ScatterMaxFallbackDistance * ScatterMaxFallbackDistance)
                        continue;
                    // The cluster's surface must face the probe for its radiance to arrive there;
                    // this is also what keeps the fallback from gathering the far side of a wall.
                    if (d > 1e-6f && Vector3.Dot(texels[texel].Normal, position - texels[texel].Position) <= 0.0f)
                        continue;
                    nearby.Add((texel, d));
                }
                if (nearby.Count == 0)
                    continue;
                nearby.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

                var sources = new List<(int cluster, float weight)>();
                for (int i = 0; i < nearby.Count && sources.Count < solveCap; i++)
                    sources.Add((nearby[i].cluster, 0.0f));
                groups[probe] = sources;
            }
        }

        /// <summary>
        /// Make every live cluster appear as a source at least once, which is true of all 128 retail
        /// slices. A cluster dropped by the per-probe cut goes to the receiver it contributes most
        /// to that still has an unreserved slot; anything left over falls into the group of its own
        /// input probe, whose reserved slot cannot have been taken by anyone else.
        /// </summary>
        private static void CoverUnusedClusters(
            Dictionary<int, List<(int cluster, float weight)>> groups,
            Dictionary<int, List<(int cluster, float weight)>> byProbe,
            SurfaceTexel[] texels,
            int[] probeForTexel,
            List<ProbePoint> probes,
            RadiosityBakeSettings settings,
            int cap,
            int solveCap)
        {
            var covered = new HashSet<int>();
            foreach (List<(int cluster, float weight)> group in groups.Values)
                foreach ((int cluster, float _) in group)
                    covered.Add(cluster);

            // Every candidate placement for a dropped cluster, strongest first.
            var placements = new Dictionary<int, List<(int probe, float weight)>>();
            foreach (KeyValuePair<int, List<(int cluster, float weight)>> pair in byProbe)
                foreach ((int cluster, float weight) in pair.Value)
                {
                    if (covered.Contains(cluster))
                        continue;
                    if (!placements.TryGetValue(cluster, out List<(int, float)> list))
                        placements[cluster] = list = new List<(int, float)>();
                    list.Add((pair.Key, weight));
                }

            foreach (int cluster in placements.Keys.OrderBy(k => k))
            {
                List<(int probe, float weight)> options = placements[cluster];
                options.Sort((a, b) => b.weight.CompareTo(a.weight));

                foreach ((int probe, float weight) in options)
                {
                    // Stop at solveCap, never cap: the last slot is held for a cluster that has
                    // nowhere else to go, and taking it here could leave that cluster homeless.
                    if (!groups.TryGetValue(probe, out List<(int cluster, float weight)> group))
                        groups[probe] = group = new List<(int, float)>();
                    if (group.Count >= solveCap)
                        continue;
                    // Coverage must not create the long links the per-probe cut just avoided.
                    if (Vector3.DistanceSquared(texels[cluster].Position, probes[probe].Position) >
                        ScatterMaxFallbackDistance * ScatterMaxFallbackDistance)
                        continue;
                    if (group.Any(g => g.cluster == cluster))
                        break;
                    group.Add((cluster, weight));
                    covered.Add(cluster);
                    break;
                }
            }

            PlaceOrphanClusters(groups, texels, probeForTexel, probes, settings, cap, solveCap, covered);
        }

        /// <summary>How many groups currently name a given cluster as a source.</summary>
        private static int Occurrences(Dictionary<int, List<(int cluster, float weight)>> groups, int cluster)
        {
            int n = 0;
            foreach (List<(int cluster, float weight)> group in groups.Values)
                foreach ((int c, float _) in group)
                    if (c == cluster) n++;
            return n;
        }

        /// <summary>
        /// Shrink the slice's scatter set to <paramref name="ceiling"/> entries by repeatedly
        /// dropping the weakest source from whichever group is currently largest, never taking a
        /// group below one entry and never dropping a cluster's last appearance.
        /// </summary>
        private static void TrimToEntryCeiling(Dictionary<int, List<(int cluster, float weight)>> groups, int ceiling)
        {
            if (ceiling <= 0)
                return;

            int total = 0;
            foreach (List<(int cluster, float weight)> group in groups.Values)
                total += group.Count;
            if (total <= ceiling)
                return;

            var occurrences = new Dictionary<int, int>();
            foreach (List<(int cluster, float weight)> group in groups.Values)
                foreach ((int cluster, float _) in group)
                {
                    occurrences.TryGetValue(cluster, out int n);
                    occurrences[cluster] = n + 1;
                }

            // Largest groups first: they are the ones over-representing well-lit probes.
            List<List<(int cluster, float weight)>> ordered = groups.Values
                .Where(g => g.Count > 1)
                .OrderByDescending(g => g.Count)
                .ToList();

            bool progress = true;
            while (total > ceiling && progress)
            {
                progress = false;
                foreach (List<(int cluster, float weight)> group in ordered)
                {
                    if (total <= ceiling)
                        break;
                    if (group.Count <= 1)
                        continue;

                    int victim = -1;
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (occurrences[group[i].cluster] < 2)
                            continue;
                        if (victim < 0 || group[i].weight < group[victim].weight)
                            victim = i;
                    }
                    if (victim < 0)
                        continue;

                    occurrences[group[victim].cluster]--;
                    group.RemoveAt(victim);
                    total--;
                    progress = true;
                }
            }
        }

        /// <summary>
        /// Clusters the visibility solve never produced a transfer for - enclosed or back-facing
        /// texels that made nobody's top 32 - still have to appear as a source. Prefer the nearest
        /// receiver with an unreserved slot; otherwise take the cluster's own reserved slot.
        /// </summary>
        /// <remarks>
        /// The own-probe fallback cannot fail. A cluster only exists where an input probe does, that
        /// probe's group is filled to at most <paramref name="solveCap"/> by the solve and to at most
        /// <paramref name="solveCap"/> by other orphans, and <paramref name="cap"/> is one larger.
        /// A texel scattering to itself is a small self-illumination error, which is the least wrong
        /// answer available for a texel nothing else can see.
        /// </remarks>
        private static void PlaceOrphanClusters(
            Dictionary<int, List<(int cluster, float weight)>> groups,
            SurfaceTexel[] texels,
            int[] probeForTexel,
            List<ProbePoint> probes,
            RadiosityBakeSettings settings,
            int cap,
            int solveCap,
            HashSet<int> covered)
        {
            var orphans = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && probeForTexel[i] >= 0 && !covered.Contains(i))
                    orphans.Add(i);
            if (orphans.Count == 0)
                return;

            // Probes are scattered over the surfaces rather than sitting on atlas texels, so the
            // search runs over a grid of their own positions. The cell bounds the search radius,
            // which caps orphan placements at the fallback distance rather than a quarter of the
            // influence radius - an orphan placed rooms away is a through-wall radiance path.
            float cell = ScatterMaxFallbackDistance;
            var grid = new Dictionary<(int, int, int), List<int>>();
            (int, int, int) Key(Vector3 v) =>
                ((int)Math.Floor(v.X / cell), (int)Math.Floor(v.Y / cell), (int)Math.Floor(v.Z / cell));
            for (int p = 0; p < probes.Count; p++)
            {
                var k = Key(probes[p].Position);
                if (!grid.TryGetValue(k, out List<int> bucket)) grid[k] = bucket = new List<int>();
                bucket.Add(p);
            }

            foreach (int cluster in orphans)
            {
                Vector3 position = texels[cluster].Position;
                int best = -1;
                float bestDistance = float.MaxValue;
                var c = Key(position);
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (!grid.TryGetValue((c.Item1 + dx, c.Item2 + dy, c.Item3 + dz), out List<int> bucket))
                                continue;
                            foreach (int probe in bucket)
                            {
                                if (groups.TryGetValue(probe, out List<(int cluster, float weight)> existing) && existing.Count >= solveCap)
                                    continue;
                                float d = Vector3.DistanceSquared(position, probes[probe].Position);
                                if (d >= bestDistance ||
                                    d > ScatterMaxFallbackDistance * ScatterMaxFallbackDistance)
                                    continue;
                                bestDistance = d;
                                best = probe;
                            }
                        }

                // Fall back to the cluster's own probe: a texel nothing else can see scatters into
                // the probe it reads from.
                if (best < 0)
                    best = probeForTexel[cluster];
                if (best < 0)
                    continue;

                if (!groups.TryGetValue(best, out List<(int cluster, float weight)> group))
                    groups[best] = group = new List<(int, float)>();

                if (group.Count >= cap)
                {
                    // Reserving one slot per group used to guarantee a home, because every cluster
                    // owned an input probe outright. Probes are shared now, so a full group has to
                    // give up its weakest member - but only one that still appears elsewhere, so
                    // covering this cluster cannot uncover another.
                    int victim = -1;
                    for (int i = 0; i < group.Count; i++)
                    {
                        if (Occurrences(groups, group[i].cluster) < 2)
                            continue;
                        if (victim < 0 || group[i].weight < group[victim].weight)
                            victim = i;
                    }
                    if (victim >= 0)
                    {
                        group.RemoveAt(victim);
                    }
                    else
                    {
                        // Nothing in that group is redundant. The nearest probe with room takes
                        // it - but only within the fallback distance. A cluster with no probe
                        // within a room's reach stays uncovered: it is enclosed geometry with
                        // nothing to give, and a long link would carry another area's light
                        // through the walls between. (Full coverage was retail's observed
                        // property, but retail achieves it locally; matching the invariant by
                        // creating links retail would never contain is the wrong trade.)
                        int fallback = -1;
                        float fallbackDistance = ScatterMaxFallbackDistance * ScatterMaxFallbackDistance;
                        for (int p = 0; p < probes.Count; p++)
                        {
                            if (groups.TryGetValue(p, out List<(int cluster, float weight)> g) && g.Count >= cap)
                                continue;
                            float d = Vector3.DistanceSquared(position, probes[p].Position);
                            if (d < fallbackDistance) { fallbackDistance = d; fallback = p; }
                        }
                        if (fallback < 0)
                            continue;
                        if (!groups.TryGetValue(fallback, out group))
                            groups[fallback] = group = new List<(int, float)>();
                    }
                }

                group.Add((cluster, 0.0f));
                covered.Add(cluster);
            }
        }

        /// <summary>Weight byte previously written for an influence slot.</summary>
        private static byte ReadInfluenceWeight(RadiosityRuntime.RuntimeDataSlice slice, int influenceSlot)
        {
            Vector4u8 weights = slice.SurfaceProbeWeights[influenceSlot / 4];
            switch (influenceSlot & 3)
            {
                case 0: return weights.X;
                case 1: return weights.Y;
                case 2: return weights.Z;
                default: return weights.W;
            }
        }

        /// <summary>
        /// The index map holds two bytes per influence (cluster x, y) and the weight map one, so
        /// influence <c>n</c> lands in index-map element <c>n/2</c> and weight element <c>n/4</c>.
        /// </summary>
        private static void WriteInfluence(RadiosityRuntime.RuntimeDataSlice slice, int influenceSlot, byte x, byte y, byte weight)
        {
            ColourRGBA8 index = slice.SurfaceProbeInfluences[influenceSlot / 2];
            if ((influenceSlot & 1) == 0) { index.R = x; index.G = y; }
            else { index.B = x; index.A = y; }

            Vector4u8 weights = slice.SurfaceProbeWeights[influenceSlot / 4];
            switch (influenceSlot & 3)
            {
                case 0: weights.X = weight; break;
                case 1: weights.Y = weight; break;
                case 2: weights.Z = weight; break;
                default: weights.W = weight; break;
            }
        }

        /// <summary>Uniform grid over live probe texels, for neighbour queries during the solve.</summary>
        private sealed class ProbeGrid
        {
            private readonly Dictionary<(int, int, int), List<int>> _cells = new Dictionary<(int, int, int), List<int>>();
            private readonly float _cellSize;

            public ProbeGrid(SurfaceTexel[] texels, List<int> live, float radius)
            {
                _cellSize = Math.Max(0.5f, radius);
                foreach (int texel in live)
                {
                    (int, int, int) key = Key(texels[texel].Position);
                    if (!_cells.TryGetValue(key, out List<int> bucket))
                        _cells[key] = bucket = new List<int>();
                    bucket.Add(texel);
                }
            }

            public IEnumerable<int> Neighbours(Vector3 position)
            {
                (int cx, int cy, int cz) = Key(position);
                for (int z = -1; z <= 1; z++)
                    for (int y = -1; y <= 1; y++)
                        for (int x = -1; x <= 1; x++)
                        {
                            if (!_cells.TryGetValue((cx + x, cy + y, cz + z), out List<int> bucket))
                                continue;
                            foreach (int texel in bucket)
                                yield return texel;
                        }
            }

            private (int, int, int) Key(Vector3 p) => (
                (int)Math.Floor(p.X / _cellSize),
                (int)Math.Floor(p.Y / _cellSize),
                (int)Math.Floor(p.Z / _cellSize));
        }

        #endregion

        #region PROBE TREES

        /// <summary>
        /// Median-split BVH over probes, with leaves sized to one 16x16 tile so the leaf count
        /// matches the tile count - which is how retail data is arranged.
        /// </summary>
        private static List<RadiosityRuntime.ProbeTreeNode> BuildProbeTree(
            int probeCount, Func<int, Vector3> positionOf, out List<uint> quads)
        {
            var nodes = new List<RadiosityRuntime.ProbeTreeNode>();
            var leafQuads = new List<uint>();
            quads = leafQuads;
            if (probeCount == 0)
                return nodes;

            const int leafSize = TileSize * TileSize;

            // Probes arrive already spatially ordered, so the tree only has to bisect the range -
            // no re-sorting, which keeps leaf n covering exactly tile n.
            int Build(int first, int count)
            {
                int index = nodes.Count;
                var node = new RadiosityRuntime.ProbeTreeNode
                {
                    MinBounds = new Vector3(float.MaxValue),
                    MaxBounds = new Vector3(float.MinValue),
                    IdxFirst = (ushort)Math.Min(ushort.MaxValue, first),
                    IdxCount = (ushort)Math.Min(ushort.MaxValue, count)
                };
                nodes.Add(node);

                for (int i = first; i < first + count; i++)
                {
                    Vector3 p = positionOf(i);
                    node.MinBounds = Vector3.Min(node.MinBounds, p);
                    node.MaxBounds = Vector3.Max(node.MaxBounds, p);
                }

                if (count <= leafSize)
                {
                    node.ChildA = 0;
                    node.ChildB = 0;

                    // A leaf addresses the QUAD list, not the probe list: IdxFirst is the index of
                    // its first of four corner vertices and IdxCount is one, counting tiles rather
                    // than probes. Internal nodes keep the probe range they were built from.
                    //
                    // Every one of the 9208 leaves across all 19 retail levels obeys this, with
                    // IdxFirst running 0, 4, 8, ... in leaf order. Writing the probe range here
                    // instead tells the engine to draw 256 quads out of a buffer holding about 120,
                    // which is the out-of-bounds read that faults inside d3d11.
                    node.IdxFirst = (ushort)leafQuads.Count;
                    node.IdxCount = 1;

                    EmitLeafQuad(leafQuads, first / leafSize, count);
                    return index;
                }

                // Split on a leaf-size boundary so leaves stay tile-aligned.
                int half = Math.Max(leafSize, (count / 2 / leafSize) * leafSize);
                if (half >= count) half = count - leafSize;

                node.ChildA = (ushort)Build(first, half);
                node.ChildB = (ushort)Build(first + half, count - half);
                return index;
            }

            Build(0, probeCount);
            return nodes;
        }

        /// <summary>
        /// Reorder in place by recursive median split on the longest axis, giving a spatially
        /// coherent sequence that tiles and tree leaves can both be cut from.
        /// </summary>
        private static void SpatialSort(List<int> items, Func<int, Vector3> positionOf)
        {
            if (items.Count <= 1)
                return;

            var scratch = items.ToArray();
            Sort(0, scratch.Length);
            items.Clear();
            items.AddRange(scratch);

            void Sort(int first, int count)
            {
                if (count <= TileSize)
                    return;

                Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
                for (int i = first; i < first + count; i++)
                {
                    Vector3 p = positionOf(scratch[i]);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }

                Vector3 extent = max - min;
                int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
                Array.Sort(scratch, first, count, Comparer<int>.Create((a, b) =>
                    Axis(positionOf(a), axis).CompareTo(Axis(positionOf(b), axis))));

                int half = count / 2;
                Sort(first, half);
                Sort(first + half, count - half);
            }
        }

        /// <summary>
        /// The four packed corners of a leaf's tile rect, as bytes <c>(x, y, cornerU, cornerV)</c>
        /// covering <c>(x, y)</c> to <c>(x + width - 1, y + height - 1)</c> inclusive.
        /// </summary>
        /// <remarks>
        /// Retail's first leaf on BSP_TORRENS slice 0 is (0,0,0,0) (15,0,1,0) (0,15,0,1) (15,15,1,1)
        /// for a 16x16 tile at the origin, and across all 128 slices the y byte never exceeds 63 -
        /// the probe texture is only 64 rows tall.
        /// </remarks>
        private static void EmitLeafQuad(List<uint> quads, int tileIndex, int probeCount)
        {
            TileRect(tileIndex, probeCount, out int x, out int y, out int width, out int height);
            int right = x + Math.Max(0, width - 1);
            int bottom = y + Math.Max(0, height - 1);

            quads.Add(Pack(x, y, 0, 0));
            quads.Add(Pack(right, y, 1, 0));
            quads.Add(Pack(x, bottom, 0, 1));
            quads.Add(Pack(right, bottom, 1, 1));
        }

        private static uint Pack(int b0, int b1, int b2, int b3) =>
            (uint)((b0 & 0xFF) | ((b1 & 0xFF) << 8) | ((b2 & 0xFF) << 16) | ((b3 & 0xFF) << 24));

        #endregion

        #region INPUT PROBE TILING

        /// <summary>
        /// Split an atlas texel index into the byte pair the engine uses to reference a cluster.
        /// </summary>
        /// <remarks>
        /// The cluster array holds 16384 entries and every reference to it - the influence index
        /// map, the scatter list's source, and a fixup's ClusterTex - stores the index as
        /// <c>(index % 256, index / 256)</c>, recombined as <c>y * 256 + x</c>. Splitting on the
        /// atlas width of 128 instead looks plausible (both fields stay inside a byte) but resolves
        /// to a different cluster for every texel outside the first row, and runs off the end of
        /// the array entirely past y = 64. Measured: 34382335 of 34382335 used influence slots in
        /// retail resolve to a live cluster under this split.
        /// </remarks>
        private static void ClusterRef(int atlasTexel, out byte x, out byte y)
        {
            x = (byte)(atlasTexel % ProbeTexWidth);
            y = (byte)(atlasTexel / ProbeTexWidth);
        }

        /// <summary>
        /// Input probes fill 16x16 tiles in column-major order: tiles 0-3 are the four rows of the
        /// leftmost column, then the next column, matching the retail tile list.
        /// </summary>
        private static void InputProbeTexel(int probeIndex, out int x, out int y)
        {
            int tile = probeIndex / (TileSize * TileSize);
            int within = probeIndex % (TileSize * TileSize);
            int tileX = (tile / TileRows) * TileSize;
            int tileY = (tile % TileRows) * TileSize;
            x = tileX + within % TileSize;
            y = tileY + within / TileSize;
        }

        /// <summary>
        /// Slot in the 256x64 probe texture for the nth probe of a compacted list. Surface probes
        /// and input probes use identical tiling, so this serves both.
        /// </summary>
        private static int ProbeSlot(int probeIndex)
        {
            InputProbeTexel(probeIndex, out int x, out int y);
            return y * ProbeTexWidth + x;
        }

        private static void BuildInputProbeTiles(int probeCount, List<RadiosityRuntime.ProbeTileDims> tiles)
        {
            tiles.Clear();
            if (probeCount == 0)
                return;

            int tileCount = (probeCount + TileSize * TileSize - 1) / (TileSize * TileSize);
            for (int t = 0; t < tileCount; t++)
            {
                int remaining = probeCount - t * TileSize * TileSize;
                TileRect(t, remaining, out int x, out int y, out int width, out int height);
                tiles.Add(new RadiosityRuntime.ProbeTileDims
                {
                    X = (byte)x,
                    Y = (byte)y,
                    Width = (byte)width,
                    Height = (byte)height
                });
            }
        }

        /// <summary>
        /// Rect of a tile. The final tile is clipped to the rows actually used, which is what
        /// retail does - BSP_TORRENS slice 0 ends with a 16x1 tile for its last 5 probes.
        /// </summary>
        private static void TileRect(int tileIndex, int probesInTile, out int x, out int y, out int width, out int height)
        {
            x = (tileIndex / TileRows) * TileSize;
            y = (tileIndex % TileRows) * TileSize;
            width = TileSize;
            int rows = (Math.Max(0, Math.Min(probesInTile, TileSize * TileSize)) + TileSize - 1) / TileSize;
            height = Math.Max(1, Math.Min(TileSize, rows));
        }

        #endregion

        #region VOLUME PROBES

        /// <summary>
        /// Uniform grid over the slice's bounds mapping a world position to the nearest input
        /// probe, used by CA_RADIOSITY_OBJECT_PROBE_INTERP to light dynamic objects.
        /// </summary>
        private static RadiosityRuntime.VolumeProbeHash BuildVolumeProbeHash(
            RadiosityGeometry geometry, SurfaceTexel[] texels, int[] inputProbeForTexel, RadiosityBakeSettings settings,
            out List<byte[]> visFaceGrids)
        {
            visFaceGrids = new List<byte[]>();
            var hash = new RadiosityRuntime.VolumeProbeHash { NumSubdivsPerLevel = settings.VolumeProbeSubdivsPerLevel };

            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            var sources = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
            {
                if (!texels[i].Live || inputProbeForTexel[i] < 0)
                    continue;
                min = Vector3.Min(min, texels[i].Position);
                max = Vector3.Max(max, texels[i].Position);
                sources.Add(i);
            }

            if (sources.Count == 0)
            {
                hash.AabbMin = Vector3.Zero;
                hash.AabbMax = Vector3.Zero;
                hash.Dims = new Vector3u32 { X = 0, Y = 0, Z = 0 };
                return hash;
            }

            hash.AabbMin = min;
            hash.AabbMax = max;

            float cell = Math.Max(0.25f, settings.VolumeProbeCellSize);
            Vector3 extent = max - min;
            uint dx = (uint)Math.Max(1, Math.Ceiling(extent.X / cell));
            uint dy = (uint)Math.Max(1, Math.Ceiling(extent.Y / cell));
            uint dz = (uint)Math.Max(1, Math.Ceiling(extent.Z / cell));
            hash.Dims = new Vector3u32 { X = dx, Y = dy, Z = dz };

            int gx = (int)dx, gy = (int)dy, gz = (int)dz;

            // Representative texel per cell, or -1. Candidates are every source in the cell; the
            // pick prefers the nearest one VISIBLE from the cell centre. A prop's own cell often
            // has the floor directly beneath it as its nearest texel, and that texel sits in the
            // prop's baked shadow - sampling it renders the prop black in a lit room. The open
            // floor beside it is the texel that carries the light (this mirrors retail's
            // least-backface candidate fixup).
            var cellCandidates = new Dictionary<int, List<int>>();
            foreach (int texel in sources)
            {
                Vector3 local = texels[texel].Position - min;
                int cx = Math.Min(gx - 1, Math.Max(0, (int)(local.X / cell)));
                int cy = Math.Min(gy - 1, Math.Max(0, (int)(local.Y / cell)));
                int cz = Math.Min(gz - 1, Math.Max(0, (int)(local.Z / cell)));
                int key = (cz * gy + cy) * gx + cx;
                if (!cellCandidates.TryGetValue(key, out List<int> list))
                    cellCandidates[key] = list = new List<int>();
                list.Add(texel);
            }

            var probeForCell = new int[gx * gy * gz];
            for (int i = 0; i < probeForCell.Length; i++) probeForCell[i] = -1;

            foreach (KeyValuePair<int, List<int>> pair in cellCandidates)
            {
                int key = pair.Key;
                int cz2 = key / (gx * gy), cy2 = (key / gx) % gy, cx2 = key % gx;
                Vector3 centre = min + new Vector3((cx2 + 0.5f) * cell, (cy2 + 0.5f) * cell, (cz2 + 0.5f) * cell);

                pair.Value.Sort((a, b) =>
                    Vector3.DistanceSquared(centre, texels[a].Position)
                        .CompareTo(Vector3.DistanceSquared(centre, texels[b].Position)));

                int chosen = -1;
                int tried = 0;
                foreach (int texel in pair.Value)
                {
                    if (tried++ >= 12) break;   // visibility rays are not free; nearest few suffice
                    if (geometry.Visible(centre,
                                         texels[texel].Position + texels[texel].Normal * settings.ProbeSurfaceOffset,
                                         settings.RayEpsilon))
                    {
                        chosen = texel;
                        break;
                    }
                }
                probeForCell[key] = chosen >= 0 ? chosen : pair.Value[0];
            }

            // Summed-area table over occupancy so "does this box hold a probe" is O(1) and empty
            // subtrees can be pruned, which is what keeps the item count near retail's.
            var occupancy = BuildOccupancySat(probeForCell, gx, gy, gz);

            var groups = new List<List<int>>();          // per node, in node index order
            var itemCells = new List<int>();             // linear cell index per item, in item order
            int subdiv = Math.Max(2, (int)settings.VolumeProbeSubdivsPerLevel);

            // Leaves are recorded as -(itemStart + 1) because their encoded value depends on the
            // final node count, which is not known until the whole tree exists.
            int Build(int ox, int oy, int oz, int ex, int ey, int ez)
            {
                int index = groups.Count;
                var group = new List<int>();
                groups.Add(group);

                int nx = ex > subdiv ? subdiv : 1;
                int ny = ey > subdiv ? subdiv : 1;
                int nz = ez > subdiv ? subdiv : 1;

                for (int zi = 0; zi < nz; zi++)
                    for (int yi = 0; yi < ny; yi++)
                        for (int xi = 0; xi < nx; xi++)
                        {
                            int cox = ox + SplitOrigin(ex, nx, xi), cex = SplitPart(ex, nx, xi);
                            int coy = oy + SplitOrigin(ey, ny, yi), cey = SplitPart(ey, ny, yi);
                            int coz = oz + SplitOrigin(ez, nz, zi), cez = SplitPart(ez, nz, zi);

                            if (SatCount(occupancy, gx, gy, gz, cox, coy, coz, cex, cey, cez) == 0)
                            {
                                group.Add(0);
                                continue;
                            }

                            if (cex > subdiv || cey > subdiv || cez > subdiv)
                            {
                                group.Add(Build(cox, coy, coz, cex, cey, cez));
                                continue;
                            }

                            // Leaf: it owns one item per cell, laid out x fastest.
                            group.Add(-(itemCells.Count + 1));
                            for (int z = coz; z < coz + cez; z++)
                                for (int y = coy; y < coy + cey; y++)
                                    for (int x = cox; x < cox + cex; x++)
                                        itemCells.Add((z * gy + y) * gx + x);
                        }
                return index;
            }

            Build(0, 0, 0, gx, gy, gz);

            // Flatten. A value is 0 for empty, the child's node index while below Nodes.Count, and
            // Nodes.Count + itemStart for a leaf - one value space, which is how the reader tells
            // the two apart without a flag.
            int nodeCount = groups.Count;
            hash.Nodes = new List<ushort>(nodeCount);
            hash.Offsets = new List<ushort>();
            foreach (List<int> group in groups)
            {
                hash.Nodes.Add((ushort)hash.Offsets.Count);
                foreach (int v in group)
                    hash.Offsets.Add((ushort)(v < 0 ? nodeCount + (-v - 1) : v));
            }

            hash.Items = new List<RadiosityRuntime.VolumeProbeHash.Probe>(itemCells.Count);
            var probeOrigins = new Vector3[itemCells.Count];
            var hasProbe = new bool[itemCells.Count];

            for (int i = 0; i < itemCells.Count; i++)
            {
                int texel = probeForCell[itemCells[i]];
                if (texel < 0)
                {
                    // No geometry near this cell. (255, 255) is retail's "no probe here" UV, and
                    // just over half of all retail items carry it.
                    hash.Items.Add(new RadiosityRuntime.VolumeProbeHash.Probe
                    {
                        UV = new Vector2u8 { X = 255, Y = 255 },
                        VisPaletteEntries = new byte[6]
                    });
                    continue;
                }

                // The UV is a LIGHTMAP ATLAS texel, not an input probe reference: the engine
                // samples the reconstructed lightmap there (through the mangle map to a surface
                // probe slot), which is how dynamic props read the baked field. Decoded from
                // retail by resolving every volume item's UV through the mangle map - the surface
                // probe it lands on sits p50 0.7 m from the item's grid cell on every slice.
                // Writing input-probe texture coords here (a 256-wide layout) into this 128-wide
                // atlas lookup was why every RADIOSITY_DYNAMIC prop rendered black.
                hash.Items.Add(new RadiosityRuntime.VolumeProbeHash.Probe
                {
                    UV = new Vector2u8 { X = (byte)(texel % AtlasSize), Y = (byte)(texel / AtlasSize) },
                    // Filled in once every slice's grids have been folded into the shared palette.
                    VisPaletteEntries = new byte[6]
                });

                // Trace from the cell centre rather than the surface probe, so the sample sits in
                // open air instead of buried in the geometry the probe is attached to.
                int c = itemCells[i];
                int cz2 = c / (gx * gy), cy2 = (c / gx) % gy, cx2 = c % gx;
                probeOrigins[i] = min + new Vector3((cx2 + 0.5f) * cell, (cy2 + 0.5f) * cell, (cz2 + 0.5f) * cell);
                hasProbe[i] = true;
            }

            // 27 samples x 64 cells x 6 faces is a lot of rays per probe, so fan them out. Cells
            // with no probe never get sampled and keep an all-zero grid.
            var grids = new byte[itemCells.Count * 6][];
            void TraceOne(int flat)
            {
                grids[flat] = hasProbe[flat / 6]
                    ? TraceVisFace(geometry, probeOrigins[flat / 6], flat % 6, settings)
                    : new byte[VisFaceCells];
            }
            if (settings.Parallel)
                System.Threading.Tasks.Parallel.For(0, grids.Length, TraceOne);
            else
                for (int i = 0; i < grids.Length; i++) TraceOne(i);
            visFaceGrids.AddRange(grids);

            return hash;
        }

        /// <summary>Size of part <paramref name="i"/> when <paramref name="extent"/> splits into
        /// <paramref name="parts"/>. Retail hands the remainder to the trailing parts.</summary>
        private static int SplitPart(int extent, int parts, int i)
        {
            if (parts <= 1) return extent;
            int size = extent / parts, remainder = extent % parts;
            return size + (i >= parts - remainder ? 1 : 0);
        }

        private static int SplitOrigin(int extent, int parts, int i)
        {
            int origin = 0;
            for (int k = 0; k < i; k++) origin += SplitPart(extent, parts, k);
            return origin;
        }

        /// <summary>Inclusive summed-area table of "cell holds a probe", padded by one on each axis.</summary>
        private static int[] BuildOccupancySat(int[] probeForCell, int gx, int gy, int gz)
        {
            int sx = gx + 1, sy = gy + 1;
            var sat = new int[sx * sy * (gz + 1)];
            for (int z = 1; z <= gz; z++)
                for (int y = 1; y <= gy; y++)
                    for (int x = 1; x <= gx; x++)
                    {
                        int here = probeForCell[((z - 1) * gy + (y - 1)) * gx + (x - 1)] >= 0 ? 1 : 0;
                        sat[(z * sy + y) * sx + x] = here
                            + sat[(z * sy + y) * sx + (x - 1)]
                            + sat[(z * sy + (y - 1)) * sx + x]
                            + sat[((z - 1) * sy + y) * sx + x]
                            - sat[(z * sy + (y - 1)) * sx + (x - 1)]
                            - sat[((z - 1) * sy + y) * sx + (x - 1)]
                            - sat[((z - 1) * sy + (y - 1)) * sx + x]
                            + sat[((z - 1) * sy + (y - 1)) * sx + (x - 1)];
                    }
            return sat;
        }

        private static int SatCount(int[] sat, int gx, int gy, int gz, int ox, int oy, int oz, int ex, int ey, int ez)
        {
            int sx = gx + 1, sy = gy + 1;
            int x0 = ox, x1 = ox + ex, y0 = oy, y1 = oy + ey, z0 = oz, z1 = oz + ez;
            int At(int x, int y, int z) => sat[(z * sy + y) * sx + x];
            return At(x1, y1, z1) - At(x0, y1, z1) - At(x1, y0, z1) - At(x1, y1, z0)
                 + At(x0, y0, z1) + At(x0, y1, z0) + At(x1, y0, z0) - At(x0, y0, z0);
        }

        /// <summary>Number of visibility sub-samples a palette cell aggregates.</summary>
        /// <remarks>
        /// Decoded from retail: across a whole level's palette only 28 distinct byte values ever
        /// appear, and every one is exactly <c>floor(n * 255 / 27)</c> for n in 0..27. 27 is
        /// 3x3x3, matching <see cref="RadiosityRuntime.VolumeProbeHash.NumSubdivsPerLevel"/> of 3 -
        /// so a cell stores how many of its 27 sub-samples were unoccluded.
        /// </remarks>
        private const int VisSubSamples = 27;

        /// <summary>Palette grids are 8x8 per cube face, six faces per volume probe.</summary>
        private const int VisFaceSize = 8;
        private const int VisFaceCells = VisFaceSize * VisFaceSize;

        private static byte EncodeVisibility(int visibleSamples, int totalSamples)
        {
            if (totalSamples <= 0) return 0;
            int n = (int)Math.Round((double)visibleSamples * VisSubSamples / totalSamples);
            if (n < 0) n = 0;
            if (n > VisSubSamples) n = VisSubSamples;
            return (byte)(n * 255 / VisSubSamples);
        }

        /// <summary>
        /// Trace an 8x8 visibility grid for one cube face of a volume probe.
        /// </summary>
        private static byte[] TraceVisFace(
            RadiosityGeometry geometry, Vector3 origin, int face, RadiosityBakeSettings settings)
        {
            var grid = new byte[VisFaceCells];
            int samples = Math.Max(1, settings.VolumeProbeVisSamplesPerCell);
            float range = settings.VolumeProbeVisRange;

            Vector3 forward = FaceDirection(face, out Vector3 right, out Vector3 up);

            for (int y = 0; y < VisFaceSize; y++)
            {
                for (int x = 0; x < VisFaceSize; x++)
                {
                    int visible = 0;
                    for (int s = 0; s < samples; s++)
                    {
                        // Stratify inside the cell so a cell straddling an edge reads partial.
                        float jx = samples == 1 ? 0.5f : Fract(0.5f + s * 0.7548776662f);
                        float jy = samples == 1 ? 0.5f : Fract(0.5f + s * 0.5698402910f);
                        float u = ((x + jx) / VisFaceSize) * 2f - 1f;
                        float v = ((y + jy) / VisFaceSize) * 2f - 1f;

                        Vector3 dir = Vector3.Normalize(forward + right * u + up * v);
                        var ray = new NanoRT.Ray(origin, dir, settings.RayEpsilon, range);
                        if (!geometry.Bvh.Occluded(ref ray))
                            visible++;
                    }
                    grid[y * VisFaceSize + x] = EncodeVisibility(visible, samples);
                }
            }
            return grid;
        }

        /// <summary>Cube face basis: +X, -X, +Y, -Y, +Z, -Z.</summary>
        private static Vector3 FaceDirection(int face, out Vector3 right, out Vector3 up)
        {
            switch (face)
            {
                case 0: right = -Vector3.UnitZ; up = Vector3.UnitY; return Vector3.UnitX;
                case 1: right = Vector3.UnitZ; up = Vector3.UnitY; return -Vector3.UnitX;
                case 2: right = Vector3.UnitX; up = Vector3.UnitZ; return Vector3.UnitY;
                case 3: right = Vector3.UnitX; up = -Vector3.UnitZ; return -Vector3.UnitY;
                case 4: right = Vector3.UnitX; up = Vector3.UnitY; return Vector3.UnitZ;
                default: right = -Vector3.UnitX; up = Vector3.UnitY; return -Vector3.UnitZ;
            }
        }

        /// <summary>
        /// Collapse every traced face grid into the shared 256-entry palette, assigning each probe
        /// face its index. Exact duplicates share an entry first; once the palette is full the
        /// remaining grids snap to their closest existing entry.
        /// </summary>
        private static List<RadiosityRuntime.VolumeProbeVisSlice> BuildVisPalette(IEnumerable<SliceBake> slices)
        {
            var palette = new List<byte[]>(256);
            var index = new Dictionary<string, int>(256);

            // Entry 0 is fully visible, as in retail - it is also the safe fallback.
            var open = new byte[VisFaceCells];
            for (int i = 0; i < VisFaceCells; i++) open[i] = 255;
            palette.Add(open);
            index[Key(open)] = 0;

            foreach (SliceBake slice in slices)
            {
                if (slice?.VisFaceGrids == null)
                    continue;

                for (int g = 0; g < slice.VisFaceGrids.Count; g++)
                {
                    byte[] grid = slice.VisFaceGrids[g];
                    string key = Key(grid);
                    if (index.TryGetValue(key, out int existing))
                    {
                        slice.VisFaceIndices[g] = (byte)existing;
                        continue;
                    }

                    if (palette.Count < 256)
                    {
                        index[key] = palette.Count;
                        slice.VisFaceIndices[g] = (byte)palette.Count;
                        palette.Add(grid);
                        continue;
                    }

                    slice.VisFaceIndices[g] = (byte)ClosestPaletteEntry(palette, grid);
                }
            }

            while (palette.Count < 256)
                palette.Add(new byte[VisFaceCells]);

            var result = new List<RadiosityRuntime.VolumeProbeVisSlice>(256);
            foreach (byte[] grid in palette)
                result.Add(new RadiosityRuntime.VolumeProbeVisSlice { Grid = grid });
            return result;

            string Key(byte[] g) => Convert.ToBase64String(g);
        }

        /// <summary>Copy the resolved palette indices back onto each slice's volume probes.</summary>
        private static void ApplyVisPaletteIndices(IEnumerable<SliceBake> slices)
        {
            foreach (SliceBake slice in slices)
            {
                if (slice?.VisFaceIndices == null)
                    continue;

                List<RadiosityRuntime.VolumeProbeHash.Probe> probes = slice.Slice.VolumeProbeHash.Items;
                for (int p = 0; p < probes.Count; p++)
                {
                    for (int face = 0; face < 6; face++)
                    {
                        int flat = p * 6 + face;
                        if (flat < slice.VisFaceIndices.Length)
                            probes[p].VisPaletteEntries[face] = slice.VisFaceIndices[flat];
                    }
                }
            }
        }

        private static int ClosestPaletteEntry(List<byte[]> palette, byte[] grid)
        {
            int best = 0;
            long bestCost = long.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                byte[] candidate = palette[i];
                long cost = 0;
                for (int c = 0; c < VisFaceCells; c++)
                {
                    int d = candidate[c] - grid[c];
                    cost += d * d;
                    if (cost >= bestCost) break;
                }
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }
            return best;
        }

        #endregion

        #region SURFACE LIGHTS

        /// <summary>
        /// Light sources that feed the engine's direct passes (CA_RADIOSITY_DIRECT_*): both
        /// emissive geometry and the level's actual light movers. One LightSlice per source,
        /// holding the input probes that sample it.
        /// </summary>
        /// <summary>
        /// World-space emissive surface area per mover index, summed over every emissive triangle
        /// the geometry pass collected for it. This is what a surface light's Weight encodes.
        /// </summary>
        private static Dictionary<int, float> ComputeEmissiveAreas(RadiosityGeometry geometry)
        {
            var areas = new Dictionary<int, float>();
            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
            {
                foreach (int tri in instance.Triangles)
                {
                    if (tri >= geometry.TriangleEmissive.Length || geometry.TriangleEmissive[tri] == Vector3.Zero)
                        continue;
                    int slot = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                    if (slot < 0 || slot >= instance.Movers.Count)
                        continue;
                    int moverIndex = instance.Movers[slot];
                    areas.TryGetValue(moverIndex, out float a);
                    areas[moverIndex] = a + geometry.TriangleArea(tri);
                }
            }
            return areas;
        }

        private static RadiosityRuntime.RuntimeSurfaceLights BuildSurfaceLights(
            Level level, RadiosityGeometry geometry, List<RadiosityGeometry.Instance> instances,
            SurfaceTexel[] texels, int[] inputProbeForTexel, RadiosityBakeSettings settings,
            Dictionary<int, float> emissiveAreas, RetailLightPriors lightPriors)
        {
            var lights = new RadiosityRuntime.RuntimeSurfaceLights
            {
                LightSliceEntities = new List<Resources.Resource>()
            };

            // Only probes this slice actually owns can sample a light. Both passes need the same
            // set, and the emissive pass needs to be able to search it by position when an emitter
            // is too small to reach the probes it should.
            var candidates = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && inputProbeForTexel[i] >= 0) candidates.Add(i);
            ProbeGrid grid = candidates.Count == 0
                ? null : new ProbeGrid(texels, candidates, settings.MaxInfluenceDistance);

            BuildEmissiveSurfaceLights(level, geometry, texels, inputProbeForTexel, grid, lights, settings, emissiveAreas, lightPriors);
            BuildLostEmitterLights(level, geometry, instances, texels, inputProbeForTexel, grid, lights, settings, lightPriors);
            if (settings.EmitLightEntitySamples)
                BuildMoverLights(level, texels, inputProbeForTexel, grid, settings, lights);
            return lights;
        }

        /// <summary>
        /// Lights for emissive movers that never entered the bake geometry at all - dynamic
        /// radiosity movers and members of composites the static gate excluded.
        /// </summary>
        /// <remarks>
        /// Joining light slices to retail's per RESOURCES.BIN entity on SCI_Hub: 532 of the 544
        /// emitters retail lights and we did not are stationary RADIOSITY_DYNAMIC movers carrying
        /// the emissive material feature. They are rightly excluded from the lightmap - they are
        /// lit through the object probes - but their own emission still falls on the static
        /// surfaces around them, and it is 23% of retail's total direct energy: broad room fill,
        /// not accents. Each gets sampled at the nearest input probes of whichever slice owns the
        /// probes closest to it.
        /// </remarks>
        private static void AddUnbakedEmitterLights(
            Level level, RadiosityGeometry geometry, SliceBake[] slices,
            RadiosityBakeSettings settings, RetailLightPriors lightPriors, Action<string> log)
        {
            // Movers already part of the bake geometry are covered by the per-slice passes.
            var baked = new HashSet<int>();
            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
                foreach (int moverIndex in instance.Movers)
                    baked.Add(moverIndex);

            // Per-slice live texels, for the nearest-probe searches.
            var sliceTexels = new List<int>[slices.Length];
            for (int s = 0; s < slices.Length; s++)
            {
                sliceTexels[s] = new List<int>();
                if (slices[s]?.Texels == null) continue;
                for (int i = 0; i < AtlasTexels; i++)
                    if (slices[s].Texels[i].Live && slices[s].InputProbeForTexel[i] >= 0)
                        sliceTexels[s].Add(i);
            }

            float reach = Math.Max(settings.EmitterSampleRadius * 4.0f, 3.0f);
            float reachSq = reach * reach;
            int added = 0;

            // Entities that already carry a light slice from the per-slice passes. Needed both to
            // avoid double-lighting and because this pass also rescues baked movers below.
            var alreadyLit = new HashSet<(uint, uint)>();
            foreach (SliceBake sb in slices)
            {
                if (sb?.Slice?.SurfaceLights?.LightSliceEntities == null) continue;
                foreach (Resources.Resource r in sb.Slice.SurfaceLights.LightSliceEntities)
                    if (r != null) alreadyLit.Add((r.composite_instance_id.AsUInt32, r.resource_id.AsUInt32));
            }

            // These movers never entered the geometry pass, so their emissive area has to be
            // measured from the meshes directly. The decode is cached per submesh.
            var meshCache = new Dictionary<Models.CS2.Component.LOD.Submesh, cMesh>();

            for (int moverIndex = 0; moverIndex < level.Movers.Entries.Count; moverIndex++)
            {
                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                if (mover?.Resource == null || mover.RenderableElements == null || mover.RenderableElements.Count == 0)
                    continue;
                if (alreadyLit.Contains((mover.Resource.composite_instance_id.AsUInt32, mover.Resource.resource_id.AsUInt32)))
                    continue;

                // A mover already in the bake geometry is normally the per-slice passes' business;
                // it is only rescued here when the retail bake attached a light to it and every
                // texel/lost-emitter path came up empty (e.g. our emissive strength resolves to 0
                // for a fixture retail lights dimly).
                RetailLightPriors.Prior prior = lightPriors?.Lookup(mover.Resource);
                if (baked.Contains(moverIndex) && prior == null)
                    continue;
                if (mover.Flags != null && !mover.Flags.Stationary)
                    continue;
                if (mover.CullFlags.HasFlag(Movers.CullFlag.NO_RENDER))
                    continue;
                switch (mover.GetRenderableType())
                {
                    case RenderableInstanceType.ENVIRONMENT:
                    case RenderableInstanceType.ENVIRONMENT_EXTRA:
                    case RenderableInstanceType.MISC:
                        break;
                    default:
                        continue;
                }

                if (SuppressedByRetail(lightPriors, mover))
                    continue;

                Vector3 emissive = RadiosityGeometry.ResolveMoverEmissive(mover, settings);
                float peak = Math.Max(emissive.X, Math.Max(emissive.Y, emissive.Z));
                if (peak <= 0 && prior == null)
                    continue;

                Vector3 position = new Vector3(mover.Transform.M41, mover.Transform.M42, mover.Transform.M43);

                // The slice whose probes sit closest to the emitter samples it, like retail's
                // single-slice attribution.
                int bestSlice = -1;
                float bestDistance = float.MaxValue;
                for (int s = 0; s < slices.Length; s++)
                {
                    foreach (int texel in sliceTexels[s])
                    {
                        float d = Vector3.DistanceSquared(slices[s].Texels[texel].Position, position);
                        if (d < bestDistance) { bestDistance = d; bestSlice = s; }
                    }
                }
                if (bestSlice < 0 || bestDistance > reachSq)
                    continue;

                SliceBake slice = slices[bestSlice];
                var nearest = new List<(int texel, float distanceSq)>();
                foreach (int texel in sliceTexels[bestSlice])
                {
                    float d = Vector3.DistanceSquared(slice.Texels[texel].Position, position);
                    if (d > reachSq) continue;
                    // No injection through walls: the sample carries the light's full energy
                    // with no runtime visibility term.
                    if (!geometry.Visible(position, slice.Texels[texel].RayOrigin, settings.RayEpsilon))
                        continue;
                    nearest.Add((texel, d));
                }
                nearest.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

                float radiusSq = settings.EmitterSampleRadius * settings.EmitterSampleRadius;
                bool matchCount = prior != null && settings.MatchRetailSampleCounts;
                int wantProbes = matchCount
                    ? Math.Max(1, Math.Min(MaxLightsPerEmitter, prior.Items))
                    : settings.MinProbesPerEmitter;
                var chosen = new List<int>();
                var seen = new HashSet<int>();
                foreach ((int texel, float distanceSq) in nearest)
                {
                    bool needed = seen.Count < wantProbes;
                    if (!needed && distanceSq > radiusSq)
                        break;
                    if (seen.Count >= (matchCount ? wantProbes : MaxLightsPerEmitter))
                        break;
                    if (!seen.Add(slice.InputProbeForTexel[texel]))
                        continue;
                    chosen.Add(texel);
                }
                if (chosen.Count == 0)
                    continue;

                RadiosityRuntime.RuntimeSurfaceLights lights = slice.Slice.SurfaceLights;
                if (lights.Lights.Count + chosen.Count > MaxSurfaceLightProbes ||
                    lights.LightSlices.Count >= MaxSurfaceLightSlices)
                    continue;

                Vector3 tint = Desaturate(mover.EmissiveTint / 255.0f, settings.SurfaceLightSaturation);
                float multiplier = RadiosityGeometry.ResolveMoverEmissiveStrength(mover, settings);
                byte scale = prior != null ? prior.Scale : EmissiveScaleByte(multiplier * settings.EmissiveScale);
                byte weight = prior != null
                    ? (settings.MatchRetailSampleCounts ? prior.WeightFor(chosen.Count) : prior.MeanWeight)
                    : EmissiveWeightByte(lightPriors.K,
                        RadiosityGeometry.MeasureEmissiveArea(mover, meshCache), chosen.Count);

                uint first = (uint)lights.Lights.Count;
                foreach (int texel in chosen)
                {
                    InputProbeTexel(slice.InputProbeForTexel[texel], out int px, out int py);
                    lights.Lights.Add(new RadiosityRuntime.RuntimeSurfaceLights.Light
                    {
                        U = (byte)px,
                        V = (byte)py,
                        AnimHi = 4,
                        AnimLo = 0,
                        R = prior != null ? prior.R : ToByte(tint.X),
                        G = prior != null ? prior.G : ToByte(tint.Y),
                        B = prior != null ? prior.B : ToByte(tint.Z),
                        Scale = scale,
                        Weight = weight,
                        TintR = 255,
                        TintG = 255,
                        TintB = 255,
                        Flags = 0
                    });
                }

                var lightSlice = new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                {
                    FirstItem = first,
                    NumItems = (ushort)(lights.Lights.Count - first),
                    EntityInstanceIndex = -1,
                    SiblingIndex = 0
                };
                lights.LightSlices.Add(lightSlice);
                lights.LightSliceEntities.Add(mover.Resource);
                slice.Slice.LiveSurfaceLights.Add(lightSlice);
                slice.Slice.LiveSurfaceLightEntities.Add(mover.Resource);
                added++;
            }

            log?.Invoke("Radiosity unbaked-emitter lights: " + added + " movers");
        }

        /// <summary>
        /// Lights for emissive movers that ended the rasterise pass with no live emissive texel,
        /// sampled at the input probes nearest their emissive surface.
        /// </summary>
        /// <remarks>
        /// An emitter only reaches <see cref="BuildEmissiveSurfaceLights"/> through its live atlas
        /// texels, and plenty of emitters never get one: their mover's share of a shared rect is a
        /// couple of texels and the emissive submesh loses the race for them. Retail ships 1818
        /// light slices on SCI_Hub; texel-derived emitters alone gave us 1211, and the shortfall
        /// is uneven emitter-to-emitter, which reads exactly as "emissive surfaces contribute
        /// unevenly". The emissive geometry itself is the reliable record of what emits, so any
        /// mover with emissive triangles and no texel-derived light gets one here.
        /// </remarks>
        private static void BuildLostEmitterLights(
            Level level, RadiosityGeometry geometry, List<RadiosityGeometry.Instance> instances,
            SurfaceTexel[] texels, int[] inputProbeForTexel, ProbeGrid grid,
            RadiosityRuntime.RuntimeSurfaceLights lights, RadiosityBakeSettings settings,
            RetailLightPriors lightPriors)
        {
            if (grid == null)
                return;

            // Movers already covered by the texel pass (or by an earlier lost-emitter entry).
            var covered = new HashSet<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && inputProbeForTexel[i] >= 0 && texels[i].Emissive != Vector3.Zero)
                    covered.Add(texels[i].MoverIndex);

            foreach (RadiosityGeometry.Instance instance in instances)
            {
                // Area-weighted emissive centroid and mean radiance per mover slot.
                var centroid = new Vector3[instance.Movers.Count];
                var areaSum = new float[instance.Movers.Count];
                var radiance = new Vector3[instance.Movers.Count];
                foreach (int tri in instance.Triangles)
                {
                    if (tri >= geometry.TriangleEmissive.Length || geometry.TriangleEmissive[tri] == Vector3.Zero)
                        continue;
                    int slot = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                    if (slot < 0 || slot >= instance.Movers.Count)
                        continue;
                    float area = geometry.TriangleArea(tri);
                    centroid[slot] += geometry.TriangleCentroid(tri) * area;
                    radiance[slot] += geometry.TriangleEmissive[tri] * area;
                    areaSum[slot] += area;
                }

                for (int slot = 0; slot < instance.Movers.Count; slot++)
                {
                    if (areaSum[slot] <= 0)
                        continue;
                    int moverIndex = instance.Movers[slot];
                    if (moverIndex < 0 || moverIndex >= level.Movers.Entries.Count || !covered.Add(moverIndex))
                        continue;

                    Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                    if (SuppressedByRetail(lightPriors, mover))
                        continue;
                    RetailLightPriors.Prior prior = lightPriors?.Lookup(mover.Resource);
                    Vector3 centre = centroid[slot] / areaSum[slot];
                    Vector3 emissive = radiance[slot] / areaSum[slot];
                    float peak = Math.Max(emissive.X, Math.Max(emissive.Y, emissive.Z));
                    if (peak <= 0)
                        continue;

                    // Nearest distinct input probes, mirroring the texel pass's sampling. Probes
                    // the emitter cannot see are skipped - injection through a wall lights the
                    // wrong room regardless of the light's runtime state.
                    var nearest = new List<(int texel, float distanceSq)>();
                    foreach (int texel in grid.Neighbours(centre))
                    {
                        if (!geometry.Visible(centre, texels[texel].RayOrigin, settings.RayEpsilon))
                            continue;
                        nearest.Add((texel, (texels[texel].Position - centre).LengthSquared()));
                    }
                    if (nearest.Count == 0)
                        continue;
                    nearest.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

                    float radiusSq = settings.EmitterSampleRadius * settings.EmitterSampleRadius;
                    bool matchCount = prior != null && settings.MatchRetailSampleCounts;
                    int wantProbes = matchCount
                        ? Math.Max(1, Math.Min(MaxLightsPerEmitter, prior.Items))
                        : settings.MinProbesPerEmitter;
                    var chosen = new List<int>();
                    var seen = new HashSet<int>();
                    foreach ((int texel, float distanceSq) in nearest)
                    {
                        bool needed = seen.Count < wantProbes;
                        if (!needed && distanceSq > radiusSq)
                            break;
                        if (seen.Count >= (matchCount ? wantProbes : MaxLightsPerEmitter))
                            break;
                        if (!seen.Add(inputProbeForTexel[texel]))
                            continue;
                        chosen.Add(texel);
                    }
                    if (chosen.Count == 0)
                        continue;

                    Vector3 tint = Desaturate(mover.EmissiveTint / 255.0f, settings.SurfaceLightSaturation);
                    float multiplier = RadiosityGeometry.ResolveMoverEmissiveStrength(mover, settings);
                    byte scale = prior != null ? prior.Scale : EmissiveScaleByte(multiplier * settings.EmissiveScale);
                    byte lostWeight = prior != null
                        ? (settings.MatchRetailSampleCounts ? prior.WeightFor(chosen.Count) : prior.MeanWeight)
                        : EmissiveWeightByte(lightPriors.K, areaSum[slot], chosen.Count);

                    uint first = (uint)lights.Lights.Count;
                    foreach (int texel in chosen)
                    {
                        InputProbeTexel(inputProbeForTexel[texel], out int px, out int py);
                        lights.Lights.Add(new RadiosityRuntime.RuntimeSurfaceLights.Light
                        {
                            U = (byte)px,
                            V = (byte)py,
                            AnimHi = 4,
                            AnimLo = 0,
                            R = prior != null ? prior.R : ToByte(tint.X),
                            G = prior != null ? prior.G : ToByte(tint.Y),
                            B = prior != null ? prior.B : ToByte(tint.Z),
                            Scale = scale,
                            Weight = lostWeight,
                            TintR = 255,
                            TintG = 255,
                            TintB = 255,
                            Flags = 0
                        });
                    }

                    lights.LightSlices.Add(new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                    {
                        FirstItem = first,
                        NumItems = (ushort)(lights.Lights.Count - first),
                        EntityInstanceIndex = -1,
                        SiblingIndex = 0
                    });
                    lights.LightSliceEntities.Add(mover.Resource);
                }
            }
        }

        private static void BuildEmissiveSurfaceLights(
            Level level, RadiosityGeometry geometry, SurfaceTexel[] texels, int[] inputProbeForTexel, ProbeGrid grid,
            RadiosityRuntime.RuntimeSurfaceLights lights, RadiosityBakeSettings settings,
            Dictionary<int, float> emissiveAreas, RetailLightPriors lightPriors)
        {
            var byMover = new Dictionary<int, List<int>>();
            for (int i = 0; i < AtlasTexels; i++)
            {
                if (!texels[i].Live || inputProbeForTexel[i] < 0)
                    continue;
                if (texels[i].Emissive == Vector3.Zero)
                    continue;
                if (!byMover.TryGetValue(texels[i].MoverIndex, out List<int> bucket))
                    byMover[texels[i].MoverIndex] = bucket = new List<int>();
                bucket.Add(i);
            }

            // NOTE: our lights sit on 20-25% fewer distinct input probes than retail's for the same
            // count, Scale and Weight (ChallengeMap4 cam21 197 against 247, cam7 275 against 356).
            // Preferring probes no other emitter had claimed was tried and REJECTED: it moved
            // cam21 to 211 probes but made the over-bright rooms brighter still (1.40x -> 1.45x,
            // mean rmse 12.74 -> 12.98) even though the region's total light Weight was unchanged.
            // Spreading one entity's flux over more probes delivers MORE light, not a diluted
            // pool - so the probe-count difference is not what makes retail's dim rooms dim.
            foreach (KeyValuePair<int, List<int>> pair in byMover.OrderBy(o => o.Key))
            {
                if (pair.Key < 0 || pair.Key >= level.Movers.Entries.Count)
                    continue;

                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[pair.Key];
                if (SuppressedByRetail(lightPriors, mover))
                    continue;
                RetailLightPriors.Prior prior = lightPriors?.Lookup(mover.Resource);
                uint first = (uint)lights.Lights.Count;

                // One light per emissive texel over-samples a large emitter. Retail averages 2.91
                // lights per slice with a median of 2 and never exceeds 81; ours averaged 4.40 with
                // a tail reaching 224, so a big glowing panel contributed several times the direct
                // energy it should. Thin evenly - the texels are spread over the emitter, so taking
                // every nth keeps the coverage and drops the density.
                List<int> emissiveTexels = pair.Value;
                int target = Math.Min(MaxLightsPerEmitter,
                    Math.Max(1, (emissiveTexels.Count + settings.EmissiveTexelsPerLight - 1) / settings.EmissiveTexelsPerLight));
                if (emissiveTexels.Count > target)
                {
                    var thinned = new List<int>(target);
                    double step = emissiveTexels.Count / (double)target;
                    for (int i = 0; i < target; i++)
                        thinned.Add(emissiveTexels[Math.Min(emissiveTexels.Count - 1, (int)(i * step))]);
                    emissiveTexels = thinned;
                }

                // Colour and strength are stored separately: RGB keeps the mover's emissive tint at
                // its own brightness, and Scale carries the multiplier. Retail's light RGB reaches
                // 255 on only 47% of BSP_TORRENS lights (mean max channel 227), so normalising the
                // colour to a peak of 255 - as this used to - brightened every tinted emitter and
                // pushed the brightness it displaced into Scale, off its sixteenths grid.
                Vector3 emissiveTint = Desaturate(mover.EmissiveTint / 255.0f, settings.SurfaceLightSaturation);
                // Mirrors RadiosityGeometry.ResolveEmissive: most emitters are picked out by their
                // material and read zero here, and retail stores 1.0 for those.
                // The material's EMISSIVE_MULT, which is retail's Scale source - see
                // RadiosityGeometry.ResolveEmissiveStrength for the decode.
                float multiplier = RadiosityGeometry.ResolveMoverEmissiveStrength(mover, settings);
                byte emissiveScale = EmissiveScaleByte(multiplier * settings.EmissiveScale);

                // Two texels of one emitter that resolve to the same input probe would stack two
                // lights at one position. That was already possible and became the norm once input
                // probes were thinned: it took our lights from 42% coincident to 77%, against
                // retail's 59%, and is what makes them read as clumps rather than a spread.
                var probesUsed = new HashSet<int>();

                // An emitter's own texels are a poor sample set. Its atlas rect is small, so most
                // emitters resolve every texel to one or two input probes and cast from a point.
                // Retail does not: 53% of its light slices hold exactly two items, 25% three and
                // 11% four, and that spread has almost nothing to do with how big the emitter is -
                // item count correlates with log(emissive area) at 0.099 across 1834 retail slices,
                // and a 0.05 m2 emitter averages 2.94 items just as a 1 m2 one averages 3.89. What
                // it looks like instead is the probes that happen to lie near the emitter, so that
                // is what this gathers: everything within a short radius of it, floored so nothing
                // ships as a single point light.
                // Where the retail bake carries this entity, its sample count is the placement
                // target: Weight is an absolute per-sample gain, so both the total flux and its
                // spatial spread depend on matching the count, not just the values. Our counts
                // against retail's ranged 0.3x-3x per entity before this.
                bool matchCount = prior != null && settings.MatchRetailSampleCounts;
                int wantProbes = matchCount
                    ? Math.Max(1, Math.Min(MaxLightsPerEmitter, prior.Items))
                    : settings.MinProbesPerEmitter;

                var extra = new List<int>();
                if (grid != null)
                {
                    var distinct = new HashSet<int>();
                    foreach (int texel in emissiveTexels)
                        if (inputProbeForTexel[texel] >= 0) distinct.Add(inputProbeForTexel[texel]);

                    Vector3 centre = Vector3.Zero;
                    foreach (int texel in emissiveTexels) centre += texels[texel].Position;
                    centre /= Math.Max(1, emissiveTexels.Count);

                    var nearby = new List<(int texel, float distanceSq)>();
                    foreach (int texel in grid.Neighbours(centre))
                    {
                        int probe = inputProbeForTexel[texel];
                        if (probe < 0 || distinct.Contains(probe))
                            continue;
                        // A sample injects the light's full energy at the probe with no runtime
                        // visibility term, so an unchecked nearest-by-distance pick can put a
                        // fixture's injection on the far side of the wall it hangs on - which
                        // lights the neighbouring room through the wall, state be damned.
                        if (!geometry.Visible(centre, texels[texel].RayOrigin, settings.RayEpsilon))
                            continue;
                        nearby.Add((texel, (texels[texel].Position - centre).LengthSquared()));
                    }
                    nearby.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

                    float radiusSq = settings.EmitterSampleRadius * settings.EmitterSampleRadius;
                    foreach ((int texel, float distanceSq) in nearby)
                    {
                        bool needed = distinct.Count < wantProbes;
                        if (!needed && distanceSq > radiusSq)
                            break;
                        if (distinct.Count >= MaxLightsPerEmitter)
                            break;
                        if (!distinct.Add(inputProbeForTexel[texel]))
                            continue;
                        extra.Add(texel);
                    }
                }

                // The topped-up texels carry the emitter's radiance, not their own - they are
                // standing in for the emitter at a probe it could not reach, and most of them are
                // not emissive surfaces themselves.
                Vector3 emitterRadiance = Vector3.Zero;
                foreach (int texel in emissiveTexels) emitterRadiance += texels[texel].Emissive;
                emitterRadiance /= Math.Max(1, emissiveTexels.Count);

                foreach (int texel in emissiveTexels.Concat(extra))
                {
                    // Matching retail's count is a cap as well as a floor: an over-count scales
                    // the entity's flux up by the same per-sample-gain arithmetic.
                    if (probesUsed.Count >= (matchCount ? wantProbes : MaxLightsPerEmitter))
                        break;
                    int probe = inputProbeForTexel[texel];
                    if (probe < 0 || !probesUsed.Add(probe))
                        continue;

                    InputProbeTexel(probe, out int px, out int py);
                    Vector3 emissive = texels[texel].Emissive;
                    if (emissive == Vector3.Zero)
                        emissive = emitterRadiance;
                    float peak = Math.Max(emissive.X, Math.Max(emissive.Y, emissive.Z));
                    if (peak <= 0)
                        continue;

                    lights.Lights.Add(new RadiosityRuntime.RuntimeSurfaceLights.Light
                    {
                        U = (byte)px,
                        V = (byte)py,
                        // TODO: AnimHi / AnimLo drive flicker animation. Retail writes (4, 0) for
                        // steady lights, so that is what a static bake emits.
                        AnimHi = 4,
                        AnimLo = 0,
                        R = prior != null ? prior.R : ToByte(emissiveTint.X),
                        G = prior != null ? prior.G : ToByte(emissiveTint.Y),
                        B = prior != null ? prior.B : ToByte(emissiveTint.Z),
                        Scale = prior != null ? prior.Scale : emissiveScale,
                        // Patched once the slice's sample count is known.
                        Weight = 1,
                        // Retail leaves the tint neutral on every single light - 3915 of 3915 on
                        // Solace are 255,255,255. Folding the mover's EmissiveTint in here applied
                        // a colour multiplier the engine does not expect and skewed the direct
                        // light; the emissive colour already rides in R/G/B above.
                        TintR = 255,
                        TintG = 255,
                        TintB = 255,
                        Flags = 0
                    });
                }

                int count = lights.Lights.Count - (int)first;
                if (count == 0)
                    continue;

                // The flux is per entity, shared over however many samples it ended up with -
                // retail's own flux where the entity exists in the retail bake, the fitted area
                // model otherwise.
                byte flux = prior != null
                    ? (settings.MatchRetailSampleCounts ? prior.WeightFor(count) : prior.MeanWeight)
                    : EmissiveWeightByte(lightPriors.K,
                        emissiveAreas.TryGetValue(pair.Key, out float a) ? a : 0.0f, count);
                for (int k = (int)first; k < lights.Lights.Count; k++)
                {
                    RadiosityRuntime.RuntimeSurfaceLights.Light light = lights.Lights[k];
                    light.Weight = flux;
                    lights.Lights[k] = light;
                }

                // Retail stores a RESOURCES.BIN index here, which is how the runtime turns a light
                // entity on and off. The index is resolved from the Resource on save, because this
                // runs before RESOURCES.BIN has been renumbered.
                lights.LightSlices.Add(new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                {
                    FirstItem = first,
                    NumItems = (ushort)Math.Min(ushort.MaxValue, count),
                    EntityInstanceIndex = -1,
                    SiblingIndex = 0
                });
                lights.LightSliceEntities.Add(mover.Resource);
            }
        }

        /// <summary>
        /// Inject the level's LIGHT movers. Each becomes a LightSlice sampled at the few input
        /// probes nearest its position, which is how retail data reads - BSP_TORRENS averages
        /// under three samples per light entity.
        /// </summary>
        private static void BuildMoverLights(
            Level level,
            SurfaceTexel[] texels,
            int[] inputProbeForTexel,
            ProbeGrid grid,
            RadiosityBakeSettings settings,
            RadiosityRuntime.RuntimeSurfaceLights lights)
        {
            if (grid == null)
                return;

            for (int moverIndex = 0; moverIndex < level.Movers.Entries.Count; moverIndex++)
            {
                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                if (mover.GetRenderableType() != RenderableInstanceType.LIGHT)
                    continue;

                var parameters = mover.RenderConstants.GetAs<Movers.MOVER_DESCRIPTOR.RENDER_CONSTANTS.DEFERRED_PARAMS>();
                if (parameters == null || !parameters.UsesRadiosity)
                    continue;

                var constants = mover.GPUConstants.GetAs<Movers.MOVER_DESCRIPTOR.GPU_CONSTANTS.DEFERRED_GPU_CONSTANTS>();
                if (constants == null)
                    continue;

                Vector3 colour = constants.Colour;
                float peak = Math.Max(colour.X, Math.Max(colour.Y, colour.Z));
                if (peak <= 0)
                    continue;

                Vector3 position = new Vector3(mover.Transform.M41, mover.Transform.M42, mover.Transform.M43);
                float range = constants.AttenuationEnd > 0 ? constants.AttenuationEnd : settings.MaxInfluenceDistance;

                // Pick the probes closest to the light, so its samples sit on the surface it lights.
                var nearest = new List<(int texel, float distanceSq)>();
                foreach (int texel in grid.Neighbours(position))
                {
                    float distanceSq = (texels[texel].Position - position).LengthSquared();
                    if (distanceSq > range * range)
                        continue;
                    nearest.Add((texel, distanceSq));
                }
                if (nearest.Count == 0)
                    continue;

                nearest.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

                // Two probes per light entity, not four. This is where most of the surface lights
                // come from, and a fixed four is what pinned our median NumItems at 4 against
                // retail's 2 - i.e. every scripted light was writing twice the direct energy.
                // The two have to be distinct input probes: nearest is a list of texels, and since
                // thinning several of them resolve to the same probe, which would stack both
                // samples on one position.
                var chosen = new List<int>(2);
                var seenProbes = new HashSet<int>();
                foreach ((int texel, float distanceSq) in nearest)
                {
                    int probe = inputProbeForTexel[texel];
                    if (probe < 0 || !seenProbes.Add(probe))
                        continue;
                    chosen.Add(texel);
                    if (chosen.Count == 2)
                        break;
                }
                if (chosen.Count == 0)
                    continue;
                int samples = chosen.Count;

                uint first = (uint)lights.Lights.Count;
                float radiosityFraction = parameters.RadiosityFraction;

                // For a light entity it is the radiosity fraction, not the colour, that lands on
                // the sixteenths grid. Its deciles cluster hard on 0.992, which encodes to exactly
                // 15 - retail's mode on both BSP_TORRENS and Solace - and its other common values
                // fall into place too: 0.498 gives 7, 0.244 gives 3, 0.02 gives 0. The colour
                // magnitude cannot be the source; 69% of BSP_TORRENS light peaks sit below 0.0625
                // and would encode to zero. Colour is therefore normalised into RGB, and the
                // per-sample falloff stays in Weight.
                Vector3 normalised = Desaturate(colour / peak, settings.SurfaceLightSaturation);
                byte lightScale = EmissiveScaleByte(parameters.RadiosityFraction);

                for (int i = 0; i < samples; i++)
                {
                    InputProbeTexel(inputProbeForTexel[chosen[i]], out int px, out int py);
                    float falloff = 1.0f / (1.0f + Vector3.DistanceSquared(texels[chosen[i]].Position, position));
                    float energy = peak * radiosityFraction * falloff;

                    lights.Lights.Add(new RadiosityRuntime.RuntimeSurfaceLights.Light
                    {
                        U = (byte)px,
                        V = (byte)py,
                        AnimHi = 4,
                        AnimLo = 0,
                        R = ToByte(normalised.X),
                        G = ToByte(normalised.Y),
                        B = ToByte(normalised.Z),
                        Scale = lightScale,
                        // Legacy path (EmitLightEntitySamples, off by default).
                        Weight = (byte)Math.Max(1, Math.Min(191, (int)Math.Round(32.0 * Math.Sqrt(energy)))),
                        TintR = 255,
                        TintG = 255,
                        TintB = 255,
                        Flags = 0
                    });
                }

                lights.LightSlices.Add(new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                {
                    FirstItem = first,
                    NumItems = (ushort)samples,
                    EntityInstanceIndex = -1,
                    SiblingIndex = 0
                });
                lights.LightSliceEntities.Add(mover.Resource);
            }
        }

        #endregion

        #region OUTPUT

        /// <summary>
        /// Write the lightmap atlas transform as <c>(width - 0.5, height - 0.5, x, y)</c>.
        /// </summary>
        /// <remarks>
        /// The half-texel bias is what makes the shader's <c>x + 0.5 + uv * (param - 0.5)</c> land
        /// on the centre of the first and last texel of the rect. Confirmed against retail by
        /// packing: reading the rects back as <c>param + 0.5</c> texels covers 12989 of slice 0's
        /// atlas, against 12891 populated cluster texels and a 12565-probe surface tree, whereas
        /// <c>param - 0.5</c> covers only 7242.
        /// </remarks>
        private static void WriteModelParams(Movers.MOVER_DESCRIPTOR mover, RadiosityGeometry.Instance instance)
        {
            // Only the first four floats of the 84-byte block are the lightmap transform; the rest
            // is per-renderable-type data that must survive untouched.
            byte[] raw = mover.RenderConstants.RawBytes;
            WriteFloat(raw, 0, instance.AtlasWidth - 0.5f);
            WriteFloat(raw, 4, instance.AtlasHeight - 0.5f);
            WriteFloat(raw, 8, instance.AtlasX);
            WriteFloat(raw, 12, instance.AtlasY);
            mover.RenderConstants.SetRawBytes(raw);
        }

        /// <summary>
        /// Zero the lightmap transform on movers that use MODEL_PARAMS but did not end up in the
        /// bake. Without this they keep the rect from whatever bake produced the level last, which
        /// points into an atlas that no longer exists.
        /// </summary>
        /// <remarks>
        /// Only renderable types whose RENDER_CONSTANTS really are MODEL_PARAMS are touched. On a
        /// LIGHT the same bytes are DEFERRED_PARAMS (visibility, flare scale, radiosity fraction,
        /// light type) and zeroing them would silently break the light.
        /// </remarks>
        private static int ClearStaleModelParams(Level level, HashSet<int> taggedMovers)
        {
            int cleared = 0;
            for (int i = 0; i < level.Movers.Entries.Count; i++)
            {
                if (taggedMovers.Contains(i))
                    continue;

                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[i];
                if (mover.RenderableElements == null || mover.RenderableElements.Count == 0)
                    continue;

                RenderableInstanceType type;
                try { type = mover.GetRenderableType(); }
                catch { continue; }

                if (type != RenderableInstanceType.ENVIRONMENT &&
                    type != RenderableInstanceType.ENVIRONMENT_EXTRA &&
                    type != RenderableInstanceType.MISC)
                    continue;

                byte[] raw = mover.RenderConstants.RawBytes;
                if (BitConverter.ToSingle(raw, 0) == 0 && BitConverter.ToSingle(raw, 4) == 0 &&
                    BitConverter.ToSingle(raw, 8) == 0 && BitConverter.ToSingle(raw, 12) == 0)
                    continue;

                WriteFloat(raw, 0, 0f);
                WriteFloat(raw, 4, 0f);
                WriteFloat(raw, 8, 0f);
                WriteFloat(raw, 12, 0f);
                mover.RenderConstants.SetRawBytes(raw);
                cleared++;
            }
            return cleared;
        }

        private static void WriteFloat(byte[] buffer, int offset, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
        }

        /// <summary>
        /// Every slice lists all the others as neighbours, which is what retail does at these slice
        /// counts (BSP_TORRENS 2 slices / 1 neighbour each, Solace 3 slices / 2 neighbours each).
        /// </summary>
        private static void BuildSliceNeighbours(RadiosityRuntime runtime)
        {
            runtime.SliceNeighbourCounts.Clear();
            runtime.SliceNeighbourArrayOffsets.Clear();
            runtime.FlattenedOtherSliceIndices.Clear();

            int count = runtime.Slices.Count;
            for (int i = 0; i < count; i++)
            {
                runtime.SliceNeighbourArrayOffsets.Add((short)runtime.FlattenedOtherSliceIndices.Count);
                int neighbours = 0;
                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    runtime.FlattenedOtherSliceIndices.Add((byte)j);
                    neighbours++;
                }
                runtime.SliceNeighbourCounts.Add((byte)neighbours);
            }
        }

        /// <summary>
        /// Patch light transfer across slice boundaries.
        /// </summary>
        /// <remarks>
        /// <para>Each fixup rewrites one influence slot of one surface probe so it references a
        /// cluster in a <i>neighbouring</i> slice, which is how a room lit through a doorway picks
        /// up bounce from the far side. Verified against retail: a fixup's ClusterTex is a live
        /// cluster in the neighbour slice 100% of the time (10909/10909 on Solace slice 0) but only
        /// sometimes in its own, and WeightTexOffset always lands inside the owning slice's weight
        /// map.</para>
        /// <para><see cref="RadiosityRuntime.FlattenedFixupRanges"/> is indexed in lockstep with
        /// <see cref="RadiosityRuntime.FlattenedOtherSliceIndices"/> - one range per ordered
        /// (slice, neighbour) pair, not one per slice. Solace ships 3 slices x 2 neighbours = 6
        /// ranges.</para>
        /// </remarks>
        private static int BuildCrossSliceFixups(
            RadiosityGeometry geometry, SliceBake[] slices, RadiosityRuntime runtime, RadiosityBakeSettings settings)
        {
            runtime.InfluenceFixups.Clear();
            runtime.FlattenedFixupRanges.Clear();

            if (slices.Length < 2)
            {
                // Single slice: one empty range per neighbour entry, of which there are none.
                for (int i = 0; i < runtime.FlattenedOtherSliceIndices.Count; i++)
                    runtime.FlattenedFixupRanges.Add(new RadiosityRuntime.FixupRange { First = 0, Num = 0 });
                return 0;
            }

            float maxDist = settings.MaxInfluenceDistance;
            float maxDistSq = maxDist * maxDist;

            // Walk the neighbour table in its stored order so ranges line up index-for-index.
            for (int s = 0; s < runtime.Slices.Count; s++)
            {
                int nbOffset = runtime.SliceNeighbourArrayOffsets[s];
                int nbCount = runtime.SliceNeighbourCounts[s];

                for (int n = 0; n < nbCount; n++)
                {
                    int neighbour = runtime.FlattenedOtherSliceIndices[nbOffset + n];
                    int first = runtime.InfluenceFixups.Count;

                    if (s < slices.Length && neighbour < slices.Length)
                        EmitPairFixups(slices[s], slices[neighbour], runtime, geometry, settings, maxDist, maxDistSq);

                    runtime.FlattenedFixupRanges.Add(new RadiosityRuntime.FixupRange
                    {
                        First = first,
                        Num = runtime.InfluenceFixups.Count - first
                    });
                }
            }

            return runtime.InfluenceFixups.Count;
        }

        /// <summary>
        /// Emit each slice's door transfer set: the probe pairs whose light path passes through a
        /// NavMeshBarrier, so the runtime can switch that transfer on and off with the door.
        /// </summary>
        /// <remarks>
        /// A transfer pairs an emitting input probe with a receiving surface probe on the far side
        /// of the barrier. Retail writes -1 into every transfer's Weight on every level, so it is
        /// not a weight and we match it rather than inventing a value.
        /// </remarks>
        private static int BuildDoors(
            Level level, RadiosityGeometry geometry, SliceBake[] slices, RadiosityBakeSettings settings, Action<string> log)
        {
            RadiosityDoors doors = RadiosityDoors.CollectFromLevel(level, log);
            if (doors.Barriers.Count == 0)
                return 0;

            int total = 0;
            foreach (SliceBake slice in slices)
            {
                var info = new RadiosityRuntime.DoorInfo();
                if (slice?.Texels == null)
                {
                    slice.Slice.Doors = info;
                    continue;
                }

                var live = new List<int>();
                for (int i = 0; i < AtlasTexels; i++)
                    if (slice.Texels[i].Live) live.Add(i);
                if (live.Count == 0)
                {
                    slice.Slice.Doors = info;
                    continue;
                }

                var grid = new ProbeGrid(slice.Texels, live, settings.DoorTransferRadius);

                foreach (RadiosityDoors.Barrier barrier in doors.Barriers)
                {
                    float reach = settings.DoorTransferRadius + barrier.Radius;
                    float reachSq = reach * reach;

                    // Probes either side of the barrier, within reach of it.
                    var nearby = new List<int>();
                    foreach (int texel in grid.Neighbours(barrier.Centre))
                    {
                        if ((slice.Texels[texel].Position - barrier.Centre).LengthSquared() <= reachSq)
                            nearby.Add(texel);
                    }
                    if (nearby.Count < 2)
                        continue;

                    int first = info.Transfers.Count;
                    int emitted = 0;

                    foreach (int receiverTexel in nearby)
                    {
                        if (emitted >= settings.MaxTransfersPerDoor)
                            break;

                        SurfaceTexel receiver = slice.Texels[receiverTexel];
                        Vector3 toReceiver = receiver.Position - barrier.Centre;

                        foreach (int emitterTexel in nearby)
                        {
                            if (emitterTexel == receiverTexel)
                                continue;
                            int inputProbe = slice.InputProbeForTexel[emitterTexel];
                            if (inputProbe < 0)
                                continue;

                            SurfaceTexel emitter = slice.Texels[emitterTexel];

                            // Opposite sides of the barrier, or the pair is not going through it.
                            if (Vector3.Dot(toReceiver, emitter.Position - barrier.Centre) >= 0)
                                continue;

                            // With the door open the path has to be clear; that is the state the
                            // transfer describes.
                            if (!geometry.Visible(
                                    emitter.Position + emitter.Normal * settings.ProbeSurfaceOffset,
                                    receiver.Position + receiver.Normal * settings.ProbeSurfaceOffset,
                                    settings.RayEpsilon))
                                continue;

                            InputProbeTexel(inputProbe, out int ex, out int ey);
                            info.Transfers.Add(new RadiosityRuntime.DoorTransfer
                            {
                                InputProbe = new Vector2u8 { X = (byte)ex, Y = (byte)ey },
                                SurfaceProbe = new Vector2u8
                                {
                                    X = (byte)(receiverTexel % AtlasSize),
                                    Y = (byte)(receiverTexel / AtlasSize)
                                },
                                Weight = -1f
                            });
                            emitted++;
                            break;
                        }
                    }

                    if (emitted == 0)
                        continue;

                    info.Doors.Add(new SliceU16 { Offset = (ushort)first, Count = (ushort)emitted });
                    info.NavmeshBarrierCathodeInstanceIndex.Add(barrier.CollisionInstanceIndex);
                    total += emitted;
                }

                slice.Slice.Doors = info;
            }
            return total;
        }

        /// <summary>Emit empty fixup ranges, one per neighbour entry, and no fixups.</summary>
        private static int ClearCrossSliceFixups(RadiosityRuntime runtime)
        {
            runtime.InfluenceFixups.Clear();
            runtime.FlattenedFixupRanges.Clear();
            for (int i = 0; i < runtime.FlattenedOtherSliceIndices.Count; i++)
                runtime.FlattenedFixupRanges.Add(new RadiosityRuntime.FixupRange { First = 0, Num = 0 });
            return 0;
        }

        private static void EmitPairFixups(
            SliceBake receiver, SliceBake emitter, RadiosityRuntime runtime, RadiosityGeometry geometry,
            RadiosityBakeSettings settings, float maxDist, float maxDistSq)
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
            int perProbeCap = Math.Max(1, settings.MaxCrossSliceFixupsPerProbe);

            var candidateProbes = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
            {
                if (!receiver.Texels[i].Live) continue;
                int probeSlot = receiver.SurfaceSlotForTexel[i];
                // Full probes stay candidates: a fixup may replace their weakest in-slice link.
                // Only appending to free slots starved every full probe near a slice boundary of
                // cross-boundary light - and with the soft visibility pass, most probes are full.
                if (probeSlot < 0) continue;
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
                Vector3 origin = probe.Position + probe.Normal * settings.ProbeSurfaceOffset;

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

                    // Strict visibility here, unlike the soft test the in-slice solve uses. Relaxing
                    // it to VisibleSoft was measured on ChallengeMap4: fixups 46769 -> 59405
                    // (retail 78294) but mean rmse 12.49 -> 13.17, because the extra energy lands
                    // in rooms that were already at parity rather than the dim ones (cam13 1.06x
                    // -> 1.18x). The cross-boundary path is not what retail's dim-room wash rides.
                    if (!geometry.Visible(origin, other.Position + other.Normal * settings.ProbeSurfaceOffset, settings.RayEpsilon))
                        continue;

                    candidates.Add((otherTexel, formFactor, distance, cosReceiver * cosEmitter));
                }

                if (candidates.Count == 0)
                    return;

                candidates.Sort((a, b) => b.weight.CompareTo(a.weight));
                var emittedList = new List<RadiosityRuntime.RuntimeInfluenceFixup>();

                foreach ((int texel, float weight, float distance, float cosProduct) in candidates)
                {
                    if (emittedList.Count >= perProbeCap)
                        break;

                    byte fixupWeight = InfluenceWeight(distance, cosProduct, settings);
                    int targetSlot;
                    if (slot < InfluencesPerProbe)
                    {
                        targetSlot = slot;
                        slot++;
                    }
                    else
                    {
                        // No free slot: replace the weakest in-slice influence, but only when this
                        // link is genuinely stronger. Both sides use the same weight curve, so the
                        // bytes compare directly.
                        targetSlot = -1;
                        byte weakest = 255;
                        for (int k = 0; k < InfluencesPerProbe; k++)
                        {
                            byte existing = ReadInfluenceWeight(receiver.Slice, probeSlot * InfluencesPerProbe + k);
                            if (existing < weakest) { weakest = existing; targetSlot = k; }
                        }
                        if (targetSlot < 0 || fixupWeight <= weakest)
                            break;
                        // The base entry stays as written: a fixup rewrites the slot when it is
                        // applied, and until then the in-slice link remains a valid fallback.
                    }

                    int weightOffset = probeSlot * InfluencesPerProbe + targetSlot;
                    ClusterRef(texel, out byte clusterX, out byte clusterY);
                    emittedList.Add(new RadiosityRuntime.RuntimeInfluenceFixup
                    {
                        WeightTexOffset = weightOffset,
                        InflTexOffset = weightOffset * 2,
                        // Weighted by the same distance and facing curve as the in-slice solve, so
                        // a patch from across a slice boundary carries the strength its geometry
                        // earns rather than one derived from the slot it happens to land in.
                        Weight = fixupWeight,
                        Padding = 0,
                        ClusterTex = new Vector2u8 { X = clusterX, Y = clusterY }
                    });
                }

                receiver.UsedInfluenceSlots[probeSlot] = (byte)slot;
                perProbe[ci] = emittedList;
            }

            if (settings.Parallel)
                Parallel.For(0, candidateProbes.Count, Solve);
            else
                for (int i = 0; i < candidateProbes.Count; i++) Solve(i);

            // Append serially so the fixup array stays grouped by (slice, neighbour) pair.
            foreach (List<RadiosityRuntime.RuntimeInfluenceFixup> list in perProbe)
                if (list != null) runtime.InfluenceFixups.AddRange(list);
        }

        #endregion

        #region HELPERS

        private static List<T> NewList<T>(int count) where T : new()
        {
            var list = new List<T>(count);
            for (int i = 0; i < count; i++) list.Add(new T());
            return list;
        }

        private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        private static float Fract(float v) => v - (float)Math.Floor(v);

        private static byte ToByte(float unitValue) =>
            (byte)Math.Max(0, Math.Min(255, (int)Math.Round(unitValue * 255.0f)));

        private static ColourRGBA8 EncodeColour(Vector3 colour, byte alpha) => new ColourRGBA8
        {
            R = ToByte(colour.X),
            G = ToByte(colour.Y),
            B = ToByte(colour.Z),
            A = alpha
        };

        /// <summary>
        /// Write an albedo into the input probe albedo texture, which the engine reads as BGRA.
        /// </summary>
        /// <remarks>
        /// Measured on Solace: retail's stored bytes average R 91.0, G 97.6, B 103.3 - rising
        /// towards the blue end - while ours averaged R 82.9, G 80.7, B 77.1, falling. Swapping ours
        /// lines the ordering up with retail. In game the difference showed as a green-warm cast
        /// over every surface where retail is cool and blue, because every bounce carried the
        /// mirrored colour.
        /// </remarks>
        private static ColourRGBA8 EncodeAlbedo(Vector3 colour, byte alpha) => new ColourRGBA8
        {
            R = ToByte(colour.Z),
            G = ToByte(colour.Y),
            B = ToByte(colour.X),
            A = alpha
        };

        /// <summary>Normals are stored biased into 0..255, so 127 is zero and 255 is +1.</summary>
        private static ColourRGBA8 EncodeNormal(Vector3 normal) => new ColourRGBA8
        {
            R = ToByte(normal.X * 0.5f + 0.5f),
            G = ToByte(normal.Y * 0.5f + 0.5f),
            B = ToByte(normal.Z * 0.5f + 0.5f),
            A = 255
        };

        private static Vector4u16 ToHalf4(Vector3 v, float w) => new Vector4u16
        {
            X = ToHalf(v.X),
            Y = ToHalf(v.Y),
            Z = ToHalf(v.Z),
            W = ToHalf(w)
        };

        /// <summary>IEEE 754 binary16 encode, round-to-nearest-even.</summary>
        private static ushort ToHalf(float value)
        {
            uint bits = (uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            uint sign = (bits >> 16) & 0x8000;
            int exponent = (int)((bits >> 23) & 0xFF) - 127 + 15;
            uint mantissa = bits & 0x7FFFFF;

            if (exponent >= 0x1F)
                return (ushort)(sign | 0x7BFF); // Clamp to the largest finite half.
            if (exponent <= 0)
            {
                if (exponent < -10)
                    return (ushort)sign;
                mantissa |= 0x800000;
                int shift = 14 - exponent;
                uint sub = mantissa >> shift;
                if (((mantissa >> (shift - 1)) & 1) != 0) sub++;
                return (ushort)(sign | sub);
            }

            uint half = (uint)(sign | ((uint)exponent << 10) | (mantissa >> 13));
            if ((mantissa & 0x1000) != 0) half++;
            return (ushort)half;
        }

        #endregion
    }
}
#endif
