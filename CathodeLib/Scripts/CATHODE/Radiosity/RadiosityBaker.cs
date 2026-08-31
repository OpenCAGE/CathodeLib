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
        private static byte InfluenceWeight(float distance, float cosProduct, RadiosityBakeSettings settings, float gain = 1.0f)
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
            float weight = baseWeight * (0.85f + 0.30f * facing) * gain;

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
        /// <summary>
        /// The emitter colour a surface light stores, decoded from retail (2026-08-25).
        ///
        /// RADIOSITY_LEVEL.BIN records the colour CA's compiler was fed per emissive instance,
        /// and every one of CM3's shipped light colours is that colour under a SQUARE ROOT -
        /// a gamma-2.0 encode - truncated to a byte:
        ///     (0.929,0.839,0.643) -> 245,233,204      (0.557,0.792,0.929) -> 190,226,245
        ///     (0.902,0.561,0.192) -> 242,190,111      0.467 grey          -> 174
        /// i.e. <c>floor(sqrt(255 * tintByte))</c>, matching on all 12 colours checked.
        ///
        /// This replaces <see cref="RadiosityBakeSettings.SurfaceLightSaturation"/>, whose own
        /// remarks called it "a calibration, not a decode" after finding only 1 of Solace's 76
        /// emissive tints in retail's light values. Under a square root the ONLY fixed points
        /// are white and the pure primaries - which is exactly the one that matched. Pulling a
        /// colour toward luminance grey is not the same operation as a per-channel sqrt, so the
        /// old stopgap mis-corrected every hue by a different amount, tinting the render.
        /// </summary>
        /// <summary>
        /// The emitter colour for a surface light, taken from the emissive material's DIFFUSE-map
        /// mean where one is available (retail's own source - see
        /// <see cref="RadiosityGeometry.ResolveMoverEmissiveMaterial"/>) and gamma-encoded.
        /// STRIP_05M: mean (0.929,0.839,0.643) -> sqrt -> x255 -> 245,233,204, which is exactly
        /// what retail ships on all 1,172 of that fixture's lights in ChallengeMap3.
        /// </summary>
        private static Vector3 EmissiveLightColour(
            Movers.MOVER_DESCRIPTOR mover, RadiosityGeometry geometry, RadiosityBakeSettings settings)
        {
            // THE COLOUR SOURCE, decoded 2026-08-27: CA's authored emissive colour IS the
            // material's DIFFUSE_TINT constant. Scored against RADIOSITY_LEVEL.BIN's authored
            // records at a <0.02 exact threshold: 1,255/1,342 (93.5%) on Solace and 1,425/1,489
            // (95.7%) on ChallengeMap3 - one rule, two levels, no fitting. Every earlier
            // candidate is a decode trap on this data: EMISSIVE_TINT is a near-constant
            // (0.808,0.988,0.992) across unrelated families, the whole-texture diffuse mean
            // scores 17-54% because fixture textures are mostly housing, and the footprint mean
            // fooled a session by sitting at ~1.5x the tint on grey plastics (which read as the
            // engine's 0.665 albedo grade). The residual ~5% matches the pre-remap sampling
            // class ([[ca-pre-remap-sampling-bug]]): CA read the tint before command-driven
            // material remapping, we read it after, so on a remapped mover OUR value is correct.
            if (settings.SurfaceLightColourFromDiffuseTint)
            {
                Materials.Material tintMat = RadiosityGeometry.ResolveMoverEmissiveMaterial(mover, settings);
                if (tintMat != null && RadiosityGeometry.TryMaterialConstantPublic(
                        tintMat, (int)CATHODE.ShaderTypes.CA_ENVIRONMENT.PARAMETERS.DIFFUSE_TINT, 3, out int tintRemap))
                {
                    float r = tintMat.PixelShaderConstants[tintRemap];
                    float g = tintMat.PixelShaderConstants[tintRemap + 1];
                    float b = tintMat.PixelShaderConstants[tintRemap + 2];
                    if (!float.IsNaN(r) && !float.IsNaN(g) && !float.IsNaN(b))
                        return EmissiveLightColour(new Vector3(
                            Math.Max(0f, Math.Min(1f, r)),
                            Math.Max(0f, Math.Min(1f, g)),
                            Math.Max(0f, Math.Min(1f, b))) * 255.0f, settings);
                }
            }

            Vector3 tintBytes = mover.EmissiveTint;
            if (settings.SurfaceLightColourFromDiffuseMean && geometry?.MaterialSampler != null)
            {
                Materials.Material mat = RadiosityGeometry.ResolveMoverEmissiveMaterial(mover, settings);
                if (mat != null)
                {
                    Vector3 mean = geometry.MaterialSampler.Mean(geometry.MaterialSampler.Register(mat));
                    // A material with no decodable diffuse map means back to a flat fallback;
                    // only take a mean that actually carries colour information.
                    if (mean.X > 0.0f || mean.Y > 0.0f || mean.Z > 0.0f)
                        tintBytes = mean * 255.0f;
                }
            }
            return EmissiveLightColour(tintBytes, settings);
        }

        private static Vector3 EmissiveLightColour(Vector3 tintBytes, RadiosityBakeSettings settings)
        {
            if (!settings.SurfaceLightGammaEncode)
                return Desaturate(tintBytes / 255.0f, settings.SurfaceLightSaturation);
            // Truncation, not rounding: retail stores 204 for 164 (sqrt gives 204.5) and 85 for
            // 29 (85.99). Rounding here is what produced our 205-for-204 colour mismatches.
            return new Vector3(
                (float)Math.Floor(Math.Sqrt(255.0 * Math.Max(0.0f, Math.Min(255.0f, tintBytes.X)))) / 255.0f,
                (float)Math.Floor(Math.Sqrt(255.0 * Math.Max(0.0f, Math.Min(255.0f, tintBytes.Y)))) / 255.0f,
                (float)Math.Floor(Math.Sqrt(255.0 * Math.Max(0.0f, Math.Min(255.0f, tintBytes.Z)))) / 255.0f);
        }

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
        /// <summary>
        /// The universal flux constant in <c>sample Weight = K x sqrt(area / samples)</c> - see
        /// <see cref="EmissiveWeightByte"/> for the decode. Fitted over every retail emitter on
        /// two independent levels (RADIOSITY_LEVEL.BIN entity joins, not the ~10% GUID subsample
        /// the old per-level fits saw): medians 169.9 / 175.6 with the implied constant piling
        /// against the same 176-178 ceiling on both - the downward tail reads as area-measurement
        /// error, so the ceiling is the constant. Supersedes 250 (this law at the modal n=2:
        /// 250 = 176.8 x sqrt 2) and the original 500 (2x hot, rendered as 2.007x through
        /// retail's scaffold). The per-level fitted Ks (CM3 352 ... Solace 1199) were sample
        /// bias, not level physics.
        /// </summary>
        private const float DefaultWeightK = 177.0f;

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
                /// Where the retail entity this prior came from actually sits, so a loose match can
                /// pick the RIGHT instance of a fixture class rather than an average of all of
                /// them. Averaging flattens a room's palette: retail lights one bunk area with 30
                /// white, 23 grey and 22 warm sources plus red and green status LEDs, and the
                /// merged prior turned our copy of it into 63 warm sources and nothing else.
                /// </summary>
                public Vector3 Position;
                public bool HasPosition;

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

            /// <summary>
            /// Fall back to a resource_id-only match when the exact (instance, resource) key
            /// misses. Duplicated retail content keeps its resource_ids under a new
            /// composite_instance_id, so this is how a dupe mover finds its retail sibling's
            /// light truth. See <see cref="RadiosityBakeSettings.DeltaLoosePriors"/>.
            /// </summary>
            public bool LooseLookup;

            /// <summary>
            /// Treat "retail's geometrically corresponding fixture was NOT lit" as a decision
            /// rather than as missing data. Requires <see cref="Instances"/>.
            /// </summary>
            public bool TwinSuppression;

            /// <summary>
            /// Every retail instance of every fixture, lit or not, so the nearest one to a query
            /// position can be asked whether retail lit it. Without this an unlit retail fixture
            /// is indistinguishable from an absent one - unlit entities never enter
            /// <see cref="Priors"/>, so absence alone carries no information.
            /// </summary>
            public readonly Dictionary<uint, List<(Vector3 pos, uint instance, bool lit)>> Instances =
                new Dictionary<uint, List<(Vector3, uint, bool)>>();

            /// <summary>
            /// True when the nearest retail instance of this fixture class - excluding the query's
            /// own composite instance - carries no light of its own. Retail saw that fixture and
            /// chose to leave it dark, so a copy of it should stay dark too. This is what stops us
            /// lighting 58 warm sources in a bunk area where retail lights 22.
            /// </summary>
            public bool NearestTwinWasDark(Resources.Resource resource, Vector3 at)
            {
                if (resource == null || !TwinSuppression) return false;
                if (!Instances.TryGetValue(resource.resource_id.AsUInt32, out var list)) return false;
                uint self = resource.composite_instance_id.AsUInt32;
                bool found = false, nearestLit = false;
                float bestD = float.MaxValue;
                foreach ((Vector3 pos, uint instance, bool lit) in list)
                {
                    if (instance == self) continue;
                    float d = Vector3.DistanceSquared(pos, at);
                    if (d < bestD) { bestD = d; nearestLit = lit; found = true; }
                }
                return found && !nearestLit;
            }

            private Dictionary<uint, Prior> _byResourceId;
            private HashSet<uint> _ambiguousResourceIds;
            private Dictionary<uint, List<Prior>> _instancesByResourceId;

            /// <summary>
            /// The prior for an entity whose light decision we can trust as this entity's own:
            /// the exact (instance, resource) match, or a loose match on a resource_id that
            /// exactly ONE retail entity carries - which is what a duplicated composite produces,
            /// since duplication mints a new composite instance id but keeps every resource_id.
            /// </summary>
            /// <remarks>
            /// This is what the lit/unlit decision must use. A resource_id shared by several
            /// retail instances only says the fixture CLASS is lit somewhere: a fixture placed 30
            /// times with 5 of them lit gives all 30 a loose prior, and treating that as ground
            /// truth force-lights the 25 retail deliberately left dark. H24 did exactly that and
            /// took the duplicate's light slices to 1199 against the engine's 1024 cap, which
            /// blacked out the whole area (mean rmse 23.4 -> 36.2).
            /// </remarks>
            /// <summary>
            /// Values for an entity from the retail instance NEAREST the query position, rather
            /// than an average over every instance sharing the resource id.
            /// </summary>
            /// <remarks>
            /// NOT CURRENTLY WIRED UP - it measured worse (24.73 against 24.00), and the reason is
            /// that nearest-in-space does not find the twin of a TRANSLATED copy: a duplicate
            /// offset 250 m sits 250 m from its true twin but only ~210 m from an unrelated
            /// instance of the same fixture elsewhere in the level, so the pick is usually wrong.
            /// It did prove the merge flattens colour - our warm value came out one unit off
            /// retail's (245,233,205 against 245,233,204) and this returns retail's exact byte -
            /// so it is kept for an offset-aware version: take the rigid translation the delta
            /// census already measures, and match on position MINUS that offset.
            /// </remarks>
            public Prior LookupNear(Resources.Resource resource, Vector3 at)
            {
                if (resource == null)
                    return null;
                if (Priors.TryGetValue(
                        (resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32),
                        out Prior exact))
                    return exact;
                if (!LooseLookup)
                    return null;
                BuildLooseIndex();
                if (!_instancesByResourceId.TryGetValue(resource.resource_id.AsUInt32, out List<Prior> group))
                    return null;
                Prior best = null;
                float bestD = float.MaxValue;
                foreach (Prior p in group)
                {
                    if (!p.HasPosition) continue;
                    float d = Vector3.DistanceSquared(p.Position, at);
                    if (d < bestD) { bestD = d; best = p; }
                }
                // No positions recorded (a scratch bake, or lights we could not place) - fall back
                // to the merged value rather than nothing.
                return best ?? Lookup(resource);
            }

            /// <summary>
            /// The prior for a mover that is a TRANSLATED COPY of retail content: match on the
            /// mover's position with the rigid translation removed, so it lands on its true twin.
            /// </summary>
            /// <remarks>
            /// This is the offset-aware form <see cref="LookupNear"/> was kept for. Nearest-in-
            /// space alone picks the wrong twin (a 250m copy is 250m from its own twin but ~210m
            /// from an unrelated instance of the same fixture), which is why that measured worse.
            /// Subtracting the offset first makes the pick exact, so a duplicated fixture inherits
            /// its twin's own colour AND flux instead of an average flattened over every instance
            /// sharing the resource id.
            /// </remarks>
            public Prior LookupOffset(Resources.Resource resource, Vector3 at, Vector3 offset)
            {
                if (resource == null)
                    return null;
                if (Priors.TryGetValue(
                        (resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32),
                        out Prior exact))
                    return exact;
                Prior near = LookupNear(resource, at - offset);
                return near ?? Lookup(resource);
            }

            public Prior LookupOwn(Resources.Resource resource)
            {
                if (resource == null)
                    return null;
                if (Priors.TryGetValue(
                        (resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32),
                        out Prior exact))
                    return exact;
                if (!LooseLookup)
                    return null;
                BuildLooseIndex();
                uint id = resource.resource_id.AsUInt32;
                return _ambiguousResourceIds.Contains(id) ? null
                     : (_byResourceId.TryGetValue(id, out Prior prior) ? prior : null);
            }

            /// <summary>
            /// As <see cref="LookupOwn"/>, but an ambiguous resource_id still yields the merged
            /// prior. Only for VALUES - colour, Scale, flux, sample count - where the fixture
            /// class is a far better answer than the scratch fallbacks (whose light colour comes
            /// from the authored emissive tint, which is why the duplicate read blue).
            /// </summary>
            public Prior Lookup(Resources.Resource resource)
            {
                if (resource == null)
                    return null;
                if (Priors.TryGetValue(
                        (resource.composite_instance_id.AsUInt32, resource.resource_id.AsUInt32),
                        out Prior prior))
                    return prior;
                if (!LooseLookup)
                    return null;
                BuildLooseIndex();
                return _byResourceId.TryGetValue(resource.resource_id.AsUInt32, out prior) ? prior : null;
            }

            private void BuildLooseIndex()
            {
                if (_byResourceId != null)
                    return;
                // Where several retail instances of one fixture share a resource_id there is no
                // way to pair a mover with one of them in particular, so merge: colour and Scale
                // weighted by sample count, flux and count as plain means - the same fixture
                // class lights the same way wherever it hangs.
                var groups = new Dictionary<uint, List<Prior>>();
                foreach (KeyValuePair<(uint, uint), Prior> kv in Priors)
                {
                    if (!groups.TryGetValue(kv.Key.Item2, out List<Prior> list))
                        groups[kv.Key.Item2] = list = new List<Prior>();
                    list.Add(kv.Value);
                }
                _byResourceId = new Dictionary<uint, Prior>();
                _ambiguousResourceIds = new HashSet<uint>();
                _instancesByResourceId = groups;
                foreach (KeyValuePair<uint, List<Prior>> g in groups)
                {
                    if (g.Value.Count == 1) { _byResourceId[g.Key] = g.Value[0]; continue; }
                    _ambiguousResourceIds.Add(g.Key);
                    double items = 0, weight = 0, r = 0, gg = 0, b = 0, scale = 0, n = 0;
                    foreach (Prior p in g.Value)
                    {
                        int w = Math.Max(1, p.Items);
                        items += p.Items; weight += p.SumWeight;
                        r += p.R * w; gg += p.G * w; b += p.B * w; scale += p.Scale * w; n += w;
                    }
                    _byResourceId[g.Key] = new Prior
                    {
                        Items = Math.Max(1, (int)Math.Round(items / g.Value.Count)),
                        SumWeight = (float)(weight / g.Value.Count),
                        R = (byte)Math.Round(r / n),
                        G = (byte)Math.Round(gg / n),
                        B = (byte)Math.Round(b / n),
                        Scale = (byte)Math.Round(scale / n)
                    };
                }
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
            // (mean rmse 16.7 -> 20.7). This entity's own prior only - see LookupOwn.
            if (priors.LookupOwn(mover.Resource) != null)
                return false;

            // No prior of its own. Where we can identify the retail fixture this one copies, its
            // darkness is retail's decision and not missing data - see NearestTwinWasDark.
            if (priors.NearestTwinWasDark(mover.Resource,
                    new Vector3(mover.Transform.M41, mover.Transform.M42, mover.Transform.M43)))
                return true;
            if (priors.Priors.Count > 0)
            {
                // With a retail bake to compare against, an emitter with no prior is usually a
                // GUID join failure rather than content retail chose not to light - suppressing
                // the authored-0 subset of them also measured worse (SCI_Hub 16.7 -> 17.3) - so
                // only the established MVR-multiplier rule applies here.
                return mover.EmissiveRadiosityMultiplier <= 0.0f;
            }
            // Scratch bake, no retail reference: ADMIT EVERYTHING with a resolvable emissive.
            // The authored radiosity_multiplier was honoured here as "the author's own exclusion
            // flag" and that reading is measurably wrong: on Solace, 927 of the 1,148 emitters
            // CA's own bake lit (RADIOSITY_LEVEL.BIN, 2026-08-26) carry multiplier 0, and CA lit
            // them at exactly the strength ResolveMoverEmissiveStrength recovers. SCI_Hub earlier
            // measured the same way (suppressing authored-0 dimmed the level, 16.7 -> 20.7). The
            // multiplier is a runtime dynamic-light input, not a bake admission flag; honouring it
            // here was the single largest cause of missing emitters (Solace lit 194 of 1,342).
            return false;
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
            Level level, Dictionary<int, float> emissiveAreas, RadiosityBakeSettings settings, Action<string> log)
        {
            var result = new RetailLightPriors();

            // Prior harvesting disabled: derive every light from mover/material data rather than
            // reusing retail's shipped values. An empty prior set is already a supported state -
            // Lookup returns null, SuppressedByRetail falls through to its authored-off rule, and
            // K stays at DefaultWeightK - so no other branch is needed.
            // See RadiosityBakeSettings.UseRetailLightPriors (TEMPORARILY OFF for validation).
            if (settings != null && !settings.UseRetailLightPriors)
            {
                log?.Invoke("Radiosity light priors: DISABLED - every light derived, none reused from retail");
                return result;
            }

            RadiosityRuntime retail = level.RadiosityRuntime;
            if (retail == null || retail.Slices.Count == 0 || level.Resources == null)
                return result;

            // Where each lit entity physically is, so a loose (resource_id) match can pick the
            // nearest real instance instead of averaging a fixture class - see Prior.Position.
            var moverPos = new Dictionary<(uint, uint), Vector3>();
            if (level.Movers?.Entries != null)
                foreach (Movers.MOVER_DESCRIPTOR mv in level.Movers.Entries)
                {
                    if (mv?.Resource == null) continue;
                    var mk = (mv.Resource.composite_instance_id.AsUInt32, mv.Resource.resource_id.AsUInt32);
                    if (!moverPos.ContainsKey(mk))
                        moverPos[mk] = new Vector3(mv.Transform.M41, mv.Transform.M42, mv.Transform.M43);
                }

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
                        if (moverPos.TryGetValue(key, out Vector3 pp))
                        { prior.Position = pp; prior.HasPosition = true; }
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

            // Every instance of every fixture, lit or not, keyed by resource id - the population
            // NearestTwinWasDark asks. Built after Priors so "lit" is already known.
            if (level.Movers?.Entries != null)
                foreach (Movers.MOVER_DESCRIPTOR mv in level.Movers.Entries)
                {
                    if (mv?.Resource == null) continue;
                    uint rid = mv.Resource.resource_id.AsUInt32;
                    uint inst = mv.Resource.composite_instance_id.AsUInt32;
                    bool lit = result.Priors.ContainsKey((inst, rid));
                    if (!result.Instances.TryGetValue(rid, out var list))
                        result.Instances[rid] = list = new List<(Vector3, uint, bool)>();
                    list.Add((new Vector3(mv.Transform.M41, mv.Transform.M42, mv.Transform.M43), inst, lit));
                }

            log?.Invoke("Radiosity light priors: " + result.Priors.Count + " retail entities, K = " +
                        result.K.ToString("0") + " from " + joined + " joined emitters");
            return result;
        }

        /// <summary>
        /// A sample's Weight: <paramref name="weightK"/> x sqrt(area / samples) - each sample
        /// carries the sqrt-flux of its own AREA SHARE, so the per-entity sum is
        /// K x sqrt(samples x area) and grows with sample count.
        /// </summary>
        /// <remarks>
        /// Decoded 2026-08-27 in two steps. First, per-emitter ratios of our flux-split table
        /// against retail fell as n^-0.5 in retail's sample count (0.83 / 0.70 / 0.64 / 0.55 for
        /// n=2..5) - flux-split predicts flat, per-light-full-flux predicts 1/n; only the area
        /// SHARE form gives the square root. Second, adding SumW = C*sqrt(n*area) to the
        /// every-emitter fit beats every other form on both levels - sd(lnC) 0.35 / 0.25 against
        /// the old form's 0.39 / 0.29 - with C piling up against the same ceiling on both:
        /// median 169.9 / 175.6, p90 176.4 / 178.3. The old "SumW = 250*sqrt(area)" was this law
        /// seen at the modal n=2: 250 = 176.8 x sqrt(2).
        /// </remarks>
        private static byte EmissiveWeightByte(float weightK, float emissiveArea, int samples)
        {
            if (emissiveArea <= 0.0f || samples <= 0) return 1;
            int v = (int)Math.Round(weightK * Math.Sqrt(emissiveArea / samples));
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
            RetailLightPriors lightPriors = CalibrateWeightCoefficient(level, emissiveAreas, settings, log);
            lightPriors.LooseLookup = settings.DeltaLoosePriors;
            lightPriors.TwinSuppression = settings.DeltaTwinSuppression;
            lightPriors.AuthoredOff = instancing?.RadiosityAuthoredOff;
            if (lightPriors.AuthoredOff != null && lightPriors.AuthoredOff.Count > 0)
                log?.Invoke("Radiosity: " + lightPriors.AuthoredOff.Count + " entities excluded by authored radiosity_multiplier = 0");

            // Rewrite the level's existing instance in place so it keeps its filepath and
            // Level.Save persists it in the normal pass.
            RadiosityRuntime runtime = level.RadiosityRuntime
                ?? throw new InvalidOperationException("Level has no RADIOSITY_RUNTIME.BIN to rewrite.");
            // Everything below replaces CA's bake wholesale, so from here the data is ours: later
            // saves regenerate it rather than patching a bake with nothing retail left in it.
            runtime.FullyRegenerated = true;
            // The retail slices must be captured BEFORE the in-place rewrite discards them -
            // reading level.RadiosityRuntime at import time returns the freshly-baked table
            // instead (the first verbatim validation silently imported our own 3,131 CM9
            // lights back onto themselves with -1 bindings and rendered ungated). The snapshot
            // also feeds the engine-corner carry below, so it is taken unconditionally.
            List<RadiosityRuntime.RuntimeDataSlice> retailSlices =
                new List<RadiosityRuntime.RuntimeDataSlice>(runtime.Slices);
            List<RadiosityRuntime.RuntimeDataSlice> retailSlicesForVerbatim =
                settings.EmitSurfaceLights && settings.RetailLightTableVerbatim
                    ? retailSlices
                    : null;
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

                        // ONE ROW PER RENDERABLE ELEMENT, matching retail: CM3 ships 11,247 rows
                        // over 6,101 resources and every resource's row count equals its element
                        // count (resource 1021 has 6 elements and 6 identical rows, 509 has 8 and
                        // 8, 5775 has 7 and 7; the histogram sums to 11,247 exactly). The rows are
                        // identical (island, resource) pairs.
                        //
                        // This is convention, not correctness: reproducing it was measured to
                        // change NOTHING in the engine's texcoord debug view, and so was sorting
                        // the map island-ascending as retail ships it. instonly renders correctly
                        // on 6,101 single rows. What actually decides whether an element keeps its
                        // lightmap is WHICH resources appear at all - see instance-map extra rows.
                        //
                        // Carry the Resource itself: this runs inside Instancing, long before
                        // Resources.Save renumbers RESOURCES.BIN, so an index captured now is stale
                        // by the time the file is written.
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
                result.Influences += sliceData[s].InfluenceCount;
                result.SurfaceLights += sliceData[s].Slice.SurfaceLights.Lights.Count;
            }

            int instanceIdCeiling = Math.Max(nextNewId, maxRetailId + 1);
            for (int i = 0; i < instanceIdCeiling; i++)
                runtime.InstanceSliceIndices.Add(sliceForInstanceId.TryGetValue(i, out int s2) ? s2 : 0);

            // The engine-owned 16x16 atlas corner: island rects are barred from it
            // (AllocateAtlases) and its CONTENT is carried from retail's corresponding slice -
            // real cluster positions and scatter, with the mangle re-pointed at OUR nearest
            // surface probe. A nearest-live fill was not enough: a reserved-but-fill-grade
            // corner deterministically exploded whole frames to 4-14x luma, while an island's
            // real texels there only broke that island. Retail authors this block on every
            // slice; whatever the engine does with it needs those values.
            CarryRetailCorners(runtime, retailSlices, settings, log);

            if (settings.EmitSurfaceLights)
                AddUnbakedEmitterLights(level, geometry, sliceData, settings, lightPriors, log);


            //Direct light and bounced light are two separate defects with two separate sizes: the
            //slope of ours-against-retail is set by how much energy enters the level, the
            //intercept by how much the influence loop recirculates. One gain cannot fix both, so
            //this scales the injected energy independently of InfluenceCurveW0.
            if (settings.SurfaceLightWeightScale != 1.0f)
            {
                int scaled = 0;
                foreach (SliceBake sb in sliceData)
                {
                    List<RadiosityRuntime.RuntimeSurfaceLights.Light> lights = sb?.Slice?.SurfaceLights?.Lights;
                    if (lights == null) continue;
                    for (int i = 0; i < lights.Count; i++)
                    {
                        RadiosityRuntime.RuntimeSurfaceLights.Light l = lights[i];
                        int w = (int)Math.Round(l.Weight * settings.SurfaceLightWeightScale);
                        l.Weight = (byte)Math.Max(1, Math.Min(191, w));
                        lights[i] = l;
                        scaled++;
                    }
                }
                log?.Invoke("Surface light weights scaled by " + settings.SurfaceLightWeightScale.ToString("0.###") +
                            " over " + scaled + " lights");
            }

            // After every derived-light pass (including the weight scale, which must not touch
            // retail's bytes): on retail levels the shipped table replaces the derived one.
            if (settings.EmitSurfaceLights && settings.RetailLightTableVerbatim)
            {
                ImportRetailLightTable(level, retailSlicesForVerbatim, sliceData, log);
                result.SurfaceLights = 0;
                foreach (SliceBake sb in sliceData)
                    if (sb?.Slice?.SurfaceLights?.Lights != null)
                        result.SurfaceLights += sb.Slice.SurfaceLights.Lights.Count;
            }

            // On retail levels, overlay retail's own stored input-probe albedo by world position.
            // CA's compiler sampled albedo BEFORE command-driven material remapping, so on
            // remapped movers retail's stored value is the AUTHORED material's - measured on
            // BSP_Torrens: TEC_Plastic_Smooth_White_DTY movers store ~6 (the authored
            // TEC_Metal_Smooth_Grey/Plastic_Black materials at tint 0.03-0.04; 36 mapping rows
            // name the family) where our post-remap sampling stores the visible white at ~145.
            // Our derivation is the "correct" one and stays (Matt's ruling), but retail's look
            // is calibrated around its own stale bounce: splicing retail's albedo rendered
            // Torrens 1.355 -> 1.203 (stable rmse 44.9 -> 32.7, best ever), SCI_Hub 1.120 ->
            // 1.087, CM3 unchanged. Same philosophy as RetailLightTableVerbatim - reuse the
            // shipped record on retail levels, derive for new content (unmatched probes keep
            // our value).
            if (settings.RetailAlbedoVerbatim)
                ImportRetailAlbedo(runtime, retailSlices, settings, log);

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

        /// <summary>
        /// Slide a purely-translated island's retail radiosity data to its new position, in
        /// place, inside its own retail slice: cluster positions, surface probe positions and
        /// texel-coincident input probes all move by the island's world delta; the diets,
        /// scatter links, mangle map, surface lights (they sample via input-probe U/V), rect and
        /// instance-map rows stay retail bytes. Movers translated here are REMOVED from
        /// <paramref name="deltaMovers"/>. Islands that rotate, scale, split their deltas, or
        /// cannot be identified fall through to the append-slice path untouched.
        /// </summary>
        /// <remarks>
        /// v1 limits: probe TREE bounds and the volume-probe hash are left stale (loose by the
        /// move distance - fine for prop nudges, wrong for cross-room relocations), and the ~10%
        /// of retail input probes that are not texel-coincident stay behind at the old spot.
        /// </remarks>
        private static void TranslateMovedIslands(
            Level level, RadiosityRuntime runtime, RadiosityBakeSettings settings,
            HashSet<int> deltaMovers, Action<string> log)
        {
            if (settings.RetailTransforms == null || settings.RetailModelParams == null ||
                level.RadiosityInstanceMap?.Entries == null)
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
                if (!settings.RetailTransforms.TryGetValue(key, out System.Numerics.Matrix4x4 pristineT))
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

                // WHOLE-ISLAND coverage: sliding moves the lighting of EVERY resource the
                // instance map binds to this rect, so every one of them must have moved by the
                // same delta - censused or not. An entity-level move (one mover of a five-mover
                // composite instance) passed the uniform-delta test trivially, slid the island
                // out from under its unmoved mates, and - worse - dropped the moved mover from
                // the census, leaving it on retail's lightmap for its OLD position. Partial
                // moves take the re-bake path, which is what the donor shell exists for.
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
                            !settings.RetailTransforms.TryGetValue(k, out System.Numerics.Matrix4x4 matePristine))
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
                    if (!settings.RetailModelParams.TryGetValue(key, out byte[] mp) || mp.Length < 16) continue;
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
                        if (Vector3.DistanceSquared(cp, pos) < 0.02f * 0.02f) { coincident = true; break; }
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
        /// The PROBE-ONLY delta path: light added or moved DYNAMIC content by extending the
        /// volume probe field, without touching any lightmap mechanism. The out-of-field delta
        /// content is partitioned spatially (one 128x128 atlas cannot hold a large edit) and
        /// each group is baked - with a donor shell of nearby non-delta geometry - into an
        /// appended slice: clusters, input probes, diets, scatter, surface lights, topped with
        /// a volume probe hash over its bounds at
        /// <see cref="RadiosityBakeSettings.DeltaVolumeProbeCellSize"/>. Nothing is written to
        /// any mover: no MODEL_PARAMS rects, no island ids, no instance-map rows, no
        /// transforms records. Movers whose pivot lies INSIDE the retail volume field are
        /// skipped - retail's own probes already light them.
        /// MUST run BEFORE <see cref="DynamicRadiosityConverter"/>: geometry collection
        /// excludes dynamic-class movers, so the content has to be baked while its materials
        /// are still static-class. See the DeltaProbeOnlySlice setting remarks for why this
        /// is currently opt-in (fresh appended slices' own radiance delivery).
        /// </summary>
        public static int AppendProbeOnlySlice(
            Level level,
            RadiosityBakeSettings settings,
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

            bool staticOnly = settings.StaticRadiosityCompositesOnly;
            settings.StaticRadiosityCompositesOnly = false;
            RadiosityGeometry geometry;
            try { geometry = RadiosityGeometry.CollectFromLevel(level, settings, log); }
            finally { settings.StaticRadiosityCompositesOnly = staticOnly; }
            if (geometry.TriangleCount == 0)
                return 0;
            geometry.Build(log);
            if (settings.UseCollisionForVisibility &&
                RadiosityOccluders.TryCollect(level, geometry, out float[] occluderVerts, out int[] occluderTris, log,
                                              skipDoorBarriers: settings.OpenDoorwaysForBake))
            {
                geometry.OccluderEndpointSlack = settings.OccluderEndpointSlack;
                geometry.OccluderSlackFraction = settings.OccluderSlackFraction;
                geometry.BuildOccluders(occluderVerts, occluderTris, log);
            }

            var deltaInstances = new List<RadiosityGeometry.Instance>();
            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
                if (instance.Movers.Any(outOfField.Contains))
                    deltaInstances.Add(instance);

            // Junk filter: instancing emits zone-less template/particle movers whose positions
            // sprawl outside any playable space (H1 measured a "zone 0" bucket of ~3.7k movers
            // spanning 100 m that packed into TWO full 16k-texel slices with 21-24k hash items -
            // over the engine's ~6.5k budget, so the slices are rejected wholesale and any real
            // zone near them suffers). An instance with no zoned mover contributes no playable
            // surface; drop it from the probe path entirely.
            if (settings.DeltaRequireZone)
            {
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
            }

            // Exterior-shell filter: the hull/skybox meshes hang far outside playable space
            // (H1: shell instances spanning 100 m packed into two full slices whose hashes hit
            // 21-24k items - over the engine's ~6.5k budget, so both were rejected wholesale).
            // No playable room island approaches this span; nothing samples probes out there.
            if (settings.DeltaMaxInstanceSpan > 0)
            {
                int shell = 0;
                for (int i = deltaInstances.Count - 1; i >= 0; i--)
                {
                    Vector3 span = deltaInstances[i].BoundsMax - deltaInstances[i].BoundsMin;
                    if (Math.Max(span.X, Math.Max(span.Y, span.Z)) > settings.DeltaMaxInstanceSpan)
                    { deltaInstances.RemoveAt(i); shell++; }
                }
                if (shell > 0)
                    log?.Invoke("Radiosity probe slices: " + shell + " oversized instances dropped (exterior shell, span > " +
                                settings.DeltaMaxInstanceSpan.ToString("0") + "m)");
            }

            if (deltaInstances.Count == 0)
            {
                log?.Invoke("Radiosity probe slice: no bakeable geometry among the out-of-field movers");
                return 0;
            }
            if (settings.DeltaUniformProbes)
            {
                // Every surface sampled on one world-space grid, so probe spacing is the same
                // everywhere - the rect is just a slot allocation sized to fit the samples.
                // The UV route below inherits the authored charts' packing instead, and its
                // world density is nothing like uniform: measured on the F5 whole-level bake,
                // per-2m-cell density against retail ran p10 0.38 with 136 cells empty or
                // below a third of retail's, while 21.5% of the probes were dilation clones
                // stacked at one position.
                float spacing = Math.Max(0.2f, settings.DeltaProbeSpacing);
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
            }
            else
            {
                // Scale may go above 1: the F1 bald-spot audit showed the area model allotting big
                // architecture far fewer texels than retail gave the same islands, and the old <=1
                // clamp silently ate every attempt to densify (F4: PROBERECT=1.5 changed nothing).
                float rectScale = Math.Max(0.2f, Math.Min(4.0f, settings.DeltaProbeRectScale));
                foreach (RadiosityGeometry.Instance instance in deltaInstances)
                {
                    RadiosityAtlas.RectSizeForBounds(instance.SurfaceArea, instance.BoundsMax - instance.BoundsMin,
                        instance.UvCoverage, settings, out int w, out int h, instance.UvAspect);
                    if (rectScale != 1.0f)
                    {
                        w = Math.Max(2, Math.Min(AtlasSize, (int)Math.Round(w * rectScale)));
                        h = Math.Max(2, Math.Min(AtlasSize, (int)Math.Round(h * rectScale)));
                    }
                    // Retail floor: never allocate an island fewer texels than retail shipped it -
                    // per-surface probe density then cannot fall below the shipped bake's.
                    if (settings.RetailRectSizes != null && instance.RetailIslandId >= 0 &&
                        settings.RetailRectSizes.TryGetValue(instance.RetailIslandId, out int[] probeFloor) &&
                        probeFloor != null && probeFloor.Length >= 2)
                    {
                        w = Math.Max(w, Math.Min(AtlasSize, probeFloor[0]));
                        h = Math.Max(h, Math.Min(AtlasSize, probeFloor[1]));
                    }
                    instance.AtlasWidth = w;
                    instance.AtlasHeight = h;
                }
            }

            // Partition into as many slices as the content needs (a whole shifted level is a
            // legal edit), leaving atlas headroom per slice for each group's donor shell.
            var groups = new List<List<RadiosityGeometry.Instance>>();
            int texelCap = settings.MaxTexelsPerSlice;
            if (settings.DeltaZoneSlices)
            {
                // Retail's slices follow ZONES: a zone's rooms always live wholly in one slice
                // (measured on the CM3 visualiser), which keeps a room's probes, scatter links
                // and door transfers together. The spatial-band split cut rooms across slices
                // and pinned every band at the same starved texel cap. Pack whole zones into
                // slices first-fit-decreasing; only a zone too big for one slice still gets
                // the spatial splitter.
                int budget = Math.Min(texelCap, 15000);
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
                    if (Tex(zoneGroup) > budget)
                    {
                        settings.MaxTexelsPerSlice = budget;
                        try { SplitSpatially(zoneGroup, settings, groups, log); }
                        finally { settings.MaxTexelsPerSlice = texelCap; }
                        log?.Invoke("      zone " + zoneEntry.Key.ToString("X8") + " (" + Tex(zoneGroup) + " texels) -> spatially split");
                        continue;
                    }
                    List<RadiosityGeometry.Instance> target = null;
                    foreach (var g in groups)
                        if (Tex(g) + Tex(zoneGroup) <= budget) { target = g; break; }
                    if (target == null) { target = new List<RadiosityGeometry.Instance>(); groups.Add(target); }
                    target.AddRange(zoneGroup);
                    log?.Invoke("      zone " + zoneEntry.Key.ToString("X8") + " (" + Tex(zoneGroup) + " texels, " +
                                zoneGroup.Count + " islands) -> slice group " + groups.IndexOf(target));
                }
                log?.Invoke("Radiosity probe slices: zone packing - " + byZone.Count + " zones -> " + groups.Count + " slices (budget " + budget + ")");
            }
            else
            {
                settings.MaxTexelsPerSlice = Math.Min(texelCap, 11000);
                try { SplitSpatially(deltaInstances, settings, groups, log); }
                finally { settings.MaxTexelsPerSlice = texelCap; }
            }

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
                if (settings.DeltaDonorShell)
                {
                    float reach = Math.Max(1.0f, settings.DeltaDonorShellRadius);
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
                    int budget = Math.Max(0, settings.DeltaDonorTexelBudget);
                    int groupTexels = 0;
                    foreach (RadiosityGeometry.Instance inst in group) groupTexels += inst.AtlasWidth * inst.AtlasHeight;
                    budget = Math.Min(budget, Math.Max(0, 15800 - groupTexels));
                    int maxDim = Math.Max(2, settings.DeltaDonorMaxRectDim);
                    int taken = 0, spent = 0, dupes = 0;
                    var seenFootprints = new HashSet<(int, int, int, int, int, int)>();
                    (int, int, int, int, int, int) Footprint(RadiosityGeometry.Instance inst) =>
                        ((int)Math.Round(inst.BoundsMin.X * 20), (int)Math.Round(inst.BoundsMin.Y * 20), (int)Math.Round(inst.BoundsMin.Z * 20),
                         (int)Math.Round(inst.BoundsMax.X * 20), (int)Math.Round(inst.BoundsMax.Y * 20), (int)Math.Round(inst.BoundsMax.Z * 20));
                    foreach ((RadiosityGeometry.Instance inst, float dist) in donors)
                    {
                        if (!seenFootprints.Add(Footprint(inst))) { dupes++; continue; }
                        RadiosityAtlas.RectSizeForBounds(inst.SurfaceArea, inst.BoundsMax - inst.BoundsMin,
                            inst.UvCoverage, settings, out int w, out int h, inst.UvAspect);
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
                }

                var probeOnlySlices = new List<List<RadiosityGeometry.Instance>> { bakeInstances };
                AllocateAtlases(probeOnlySlices, settings, log);

                Dictionary<int, float> emissiveAreas = ComputeEmissiveAreas(geometry);
                RetailLightPriors lightPriors = CalibrateWeightCoefficient(level, emissiveAreas, settings, log);
                lightPriors.LooseLookup = settings.DeltaLoosePriors;
                lightPriors.TwinSuppression = settings.DeltaTwinSuppression;
            lightPriors.TwinSuppression = settings.DeltaTwinSuppression;

                bool probesOnTexels = settings.InputProbesOnTexels;
                float volumeCell = settings.VolumeProbeCellSize;
                settings.InputProbesOnTexels = true;
                settings.VolumeProbeCellSize = Math.Max(0.25f, settings.DeltaVolumeProbeCellSize);
                SliceBake bake;
                try { bake = BakeSlice(level, geometry, bakeInstances, runtime.Slices.Count, settings, emissiveAreas, lightPriors, log); }
                finally { settings.InputProbesOnTexels = probesOnTexels; settings.VolumeProbeCellSize = volumeCell; }

                if (settings.EmitSurfaceLights)
                    AddUnbakedEmitterLights(level, geometry, new[] { bake }, settings, lightPriors, log);

                if (settings.SurfaceLightWeightScale != 1.0f && bake?.Slice?.SurfaceLights?.Lights != null)
                {
                    List<RadiosityRuntime.RuntimeSurfaceLights.Light> lights = bake.Slice.SurfaceLights.Lights;
                    for (int i = 0; i < lights.Count; i++)
                    {
                        RadiosityRuntime.RuntimeSurfaceLights.Light l = lights[i];
                        l.Weight = (byte)Math.Max(1, Math.Min(191, (int)Math.Round(l.Weight * settings.SurfaceLightWeightScale)));
                        lights[i] = l;
                    }
                }

                FoldVisPaletteIntoRuntime(runtime, bake, log);

                int newSliceIndex = runtime.Slices.Count;
                runtime.Slices.Add(bake.Slice);
                AppendDeltaFixups(level, runtime, bake, newSliceIndex, geometry, settings, log);
                CalibrateDeltaEnergy(level, runtime, bake, newSliceIndex, settings, log);

                // Schedule the slice. The engine only runs relight over a slice that at least
                // one island's InstanceSliceIndices entry points at (H3 proof: an appended
                // slice with none rendered its content COMPLETELY unlit - lights placed, hash
                // resolving, radiance never computed - and re-pointing a single island lit it,
                // cam7 0.195 -> 0.982). Added content carries no islands, so mint one fresh id
                // per slice; the caller's DeltaIslandRecord persists its transforms record
                // (0,0 rect origin - nothing renders through it - with the foreign group reset).
                if (settings.DeltaMintScheduleIsland)
                {
                    // The scheduling walk is over INSTANCE MAP rows, not the raw island table
                    // (H4: a minted id with no row did nothing; H3's repointed retail islands -
                    // which have rows - scheduled their slices; H5: rows minted pre-conversion
                    // must survive the converter's row drop). One island per slice lit SOME of
                    // its zones but not all (H6: cam11 1.03 while cam3 stayed 0.36 in the same
                    // bake), so mint one island PER ZONE, each bound to a mover of that zone -
                    // zeroed MODEL_PARAMS keep every bound mover rendering dynamic.
                    // Per zone, bind the row to the LARGEST-area mover's resource: the relight
                    // walk seeds the region around the bound mover (H15/16 vs H18 - the same
                    // zones lit or died purely on which mover the row bound; tiny corner props
                    // seed nothing). The hijack this used to cause (H16's black doorway) is
                    // neutralised by adding these rows AFTER the lightmap path's real rows -
                    // rendering follows a resource's first row.
                    var zoneSeeds = new Dictionary<uint, List<(Resources.Resource r, float area)>>();
                    foreach (RadiosityGeometry.Instance inst in group)
                    {
                        uint z = 0;
                        foreach (int mi in inst.Movers)
                        {
                            var zid = level.Movers.Entries[mi].PrimaryZoneID;
                            if (zid != CATHODE.Scripting.ShortGuid.Invalid) { z = zid.AsUInt32; break; }
                        }
                        if (!zoneSeeds.TryGetValue(z, out var seeds))
                            zoneSeeds[z] = seeds = new List<(Resources.Resource, float)>();
                        for (int m = 0; m < inst.Movers.Count; m++)
                        {
                            // Seeds must STAND IN the slice's content: instancing emits movers
                            // with absolute positions that never inherited the delta offset, and
                            // largest-mover picks landed on them - scheduling rows bound 250 m
                            // from their slice, which is what made per-zone lighting a lottery.
                            if (!outOfField.Contains(inst.Movers[m])) continue;
                            Resources.Resource r = level.Movers.Entries[inst.Movers[m]].Resource;
                            if (r == null) continue;
                            float area = m < inst.MoverAreas.Count ? inst.MoverAreas[m] : 0f;
                            seeds.Add((r, area));
                        }
                    }
                    var zoneResource = new Dictionary<uint, List<Resources.Resource>>();
                    foreach (var kv in zoneSeeds)
                    {
                        // (sort removed for H23: first-found order)
                        // ONE seed per zone (H20: 3 seeds measured worse). H23 matrix cell:
                        // FIRST-FOUND binding (H16's pick, the only config that ever lit cam12)
                        // with deferred rows - isolating binding choice from row timing.
                        var top = new List<Resources.Resource>();
                        foreach (var s in kv.Value)
                        {
                            if (!top.Contains(s.r)) top.Add(s.r);
                            if (top.Count >= 1) break;
                        }
                        if (top.Count > 0) zoneResource[kv.Key] = top;
                    }
                    // GAP IDS ONLY: a beyond-range id is broken - the engine sizes its
                    // per-island state from retail's transforms table once and ignores appended
                    // records (the cam7 stolen-id door proof; H4/H9's appended-id mints did
                    // nothing or poisoned the walk). Retail leaves in-range ids no map row uses;
                    // take those.
                    var mintUsed = new HashSet<int>();
                    if (level.RadiosityInstanceMap?.Entries != null)
                        foreach (RadiosityInstanceMap.Entry e in level.RadiosityInstanceMap.Entries)
                            mintUsed.Add(e.lightmap_transform);
                    var mintGaps = new Queue<int>();
                    for (int id = 0; id < runtime.InstanceSliceIndices.Count && mintGaps.Count < zoneResource.Count; id++)
                        if (!mintUsed.Contains(id)) mintGaps.Enqueue(id);

                    int mintedCount = 0;
                    foreach (var zr in zoneResource)
                    {
                        // Gaps are free (retail binds no row to them). Beyond that, grow mode
                        // extends the table rather than sacrificing a live retail island.
                        int mintedIsland = mintGaps.Count > 0 ? mintGaps.Dequeue() : -1;
                        if (mintedIsland < 0 && settings.DeltaGrowIslandIds)
                        {
                            mintedIsland = runtime.InstanceSliceIndices.Count;
                            runtime.InstanceSliceIndices.Add(newSliceIndex);
                        }
                        else if (mintedIsland < 0 && settings.DeltaSacrificialIslands != null &&
                                 settings.DeltaSacrificialIslands.Count > 0)
                            mintedIsland = settings.DeltaSacrificialIslands.Dequeue();
                        if (mintedIsland < 0 || mintedIsland >= runtime.InstanceSliceIndices.Count) break;
                        runtime.InstanceSliceIndices[mintedIsland] = newSliceIndex;
                        settings.DeltaIslandRecord?.Invoke(mintedIsland, 0, 0, true);
                        // Several rows may share one island id (retail's own many-to-one rows):
                        // the zone's top-3 movers all seed the walk at no extra id cost.
                        foreach (Resources.Resource seed in zr.Value)
                            settings.DeltaPendingScheduleRows?.Add((mintedIsland, seed));
                        mintedCount++;
                    }
                    log?.Invoke("    probe slice " + newSliceIndex + ": scheduled via " + mintedCount +
                                " islands (per zone, rows deferred until after the lightmap delta)" +
                                (mintedCount < zoneResource.Count ? " - ID POOL EXHAUSTED for " + (zoneResource.Count - mintedCount) + " zones" : ""));
                }

                // Anchor islands (see DeltaProbeAnchorIslands): repoint EVERY in-range island
                // that a group mover's kept map rows bind at this slice. B2 proved influence
                // weights are invisible without this - the appended slice contributes only its
                // direct injection, because the instance map is what schedules the gather; a
                // slice with no mapped islands never bounces. The islands' movers are all
                // dynamic (zeroed rects), so nothing samples the repointed pages through rects.
                if (settings.DeltaProbeAnchorIslands && level.RadiosityInstanceMap?.Entries != null)
                {
                    var groupResources = new HashSet<Resources.Resource>();
                    foreach (RadiosityGeometry.Instance inst in group)
                        foreach (int moverIndex in inst.Movers)
                            if (level.Movers.Entries[moverIndex].Resource != null)
                                groupResources.Add(level.Movers.Entries[moverIndex].Resource);
                    var anchoredIds = new HashSet<int>();
                    foreach (RadiosityInstanceMap.Entry e in level.RadiosityInstanceMap.Entries)
                    {
                        Resources.Resource r = e.Resource ?? level.Resources.GetAtWriteIndex(e.resource_index);
                        if (r == null || !groupResources.Contains(r))
                            continue;
                        int id = e.lightmap_transform;
                        if (id < 0 || id >= runtime.InstanceSliceIndices.Count || anchoredIds.Contains(id))
                            continue;
                        runtime.InstanceSliceIndices[id] = newSliceIndex;
                        anchoredIds.Add(id);
                    }
                    log?.Invoke("    probe slice " + newSliceIndex + ": " + anchoredIds.Count +
                                " islands anchored (gather scheduling)");
                }

                doorBakes.Add(bake);
                lastHash = bake.Slice.VolumeProbeHash;
                slicesBaked++;
            }

            // The engine's relight propagation runs on the per-slice DOOR tables: stripping
            // retail slice 1's doors reproduced the appended-slice black-room family almost
            // decimal-for-decimal (E10 0.367/0.318/0.194 vs F1 0.366/0.313/0.204, including
            // the cam5 overshoot). A door-less slice never gathers, whatever its influence
            // weights say - so probe slices need the same door build as the full bake.
            if (settings.EmitDoors && doorBakes.Count > 0)
            {
                int doorTransfers = BuildDoors(level, geometry, doorBakes.ToArray(), settings, log);
                log?.Invoke("Radiosity probe slices: " + doorTransfers + " door transfers built");
            }

            log?.Invoke("Radiosity probe slices: " + deltaInstances.Count + " islands (" + outOfField.Count +
                        " out-of-field movers) across " + slicesBaked + " appended slices + " + donorsTotal +
                        " donors; " + (lastHash != null && lastHash.Dims.X > 0 ? "last hash " + lastHash.Dims.X + "x" +
                        lastHash.Dims.Y + "x" + lastHash.Dims.Z + " at " +
                        settings.DeltaVolumeProbeCellSize.ToString("0.0#") + "m cells" : "no hash") +
                        "; no rects, no ids, no map rows written");
            return deltaInstances.Count;
        }

        /// <summary>Fold an appended slice bake's visibility grids into the runtime's shared
        /// 256-entry palette: match by content, take over zero-padding entries from the end for
        /// new grids, snap to entry 0 (fully open) once the slots are truly full.</summary>
        private static void FoldVisPaletteIntoRuntime(RadiosityRuntime runtime, SliceBake bake, Action<string> log = null)
        {
            // Fold the delta slice.s visibility grids into the EXISTING palette: match by
            // content, take over zero-padding entries from the end for new grids, and snap to
            // the closest entry once the 256 slots are truly full.
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
                    // The 256 slots are full - retail fills every one on a shipped level, so this
                    // is the NORMAL case for appended slices, not an edge case. Falling back to
                    // entry 0 (which this did) pinned every delta face to one flat grid: measured
                    // on CM3, retail's own slices referenced entry 0 on 3/4908 and 8/5358 faces
                    // while ours did on 11207/11208, at openness 46 against retail's p50 ~105.
                    // Dynamic props sample this field, so they rendered flat and dark. Snap to the
                    // nearest real grid instead, which is what the remark above always claimed.
                    bake.VisFaceIndices[g] = (byte)ClosestPaletteEntry(raw, grid);
                    snapped++;
                }
                log?.Invoke("    delta vis palette: " + matched + " matched, " + placed +
                            " took free slots, " + snapped + " snapped to nearest");
                ApplyVisPaletteIndices(new[] { bake });
            }
        }

        /// <summary>Per-probe energy calibration for an appended delta slice - the weight byte is
        /// exponent-domain, so each probe's exp mass is matched to the median of the retail
        /// surface probes near it (or the global bias where none exist). Shared by the lightmap
        /// delta path and the probe-only path.</summary>
        private static void CalibrateDeltaEnergy(Level level, RadiosityRuntime runtime, SliceBake bake, int newSliceIndex, RadiosityBakeSettings settings, Action<string> log)
        {
            // Delta energy calibration, per probe: the weight byte is exponent-domain (~+32 =
            // x2 rendered), so a probe's delivered gather tracks its "exp mass" sum(2^(w/32)),
            // not its byte sum - which is why a single global bias fit CM3 (0.97) yet overshot
            // CM5's shelving family to 1.14: per-island diets differ too much for one shift.
            // Instead each delta probe's exp mass is matched to the MEDIAN exp mass of the
            // retail surface probes near it - retail's own answer for how much gather a probe
            // in that room carries. Probes with no retail neighbours (void content, new rooms)
            // fall back to the global DeltaInfluenceWeightBias.
            if (!settings.DeltaMatchRetailExpMass &&
                (settings.DeltaInfluenceWeightScale != 1.0f || settings.DeltaInfluenceWeightBias != 0) &&
                bake?.Slice?.SurfaceProbeWeights != null)
            {
                // Uniform fallback path: global bias/scale on every weight, no local matching.
                float weightScale = settings.DeltaInfluenceWeightScale;
                int weightBias = settings.DeltaInfluenceWeightBias;
                byte ScaleW(byte w) => w == 0 ? (byte)0 : (byte)Math.Max(1, Math.Min(254, (int)Math.Round(w * weightScale) + weightBias));
                for (int i = 0; i < bake.Slice.SurfaceProbeWeights.Count; i++)
                {
                    Vector4u8 v = bake.Slice.SurfaceProbeWeights[i];
                    v.X = ScaleW(v.X); v.Y = ScaleW(v.Y); v.Z = ScaleW(v.Z); v.W = ScaleW(v.W);
                    bake.Slice.SurfaceProbeWeights[i] = v;
                }
                int gOffset = runtime.SliceNeighbourArrayOffsets[newSliceIndex];
                for (int n = 0; n < runtime.SliceNeighbourCounts[newSliceIndex]; n++)
                {
                    RadiosityRuntime.FixupRange range = runtime.FlattenedFixupRanges[gOffset + n];
                    for (int i = range.First; i < range.First + range.Num; i++)
                    {
                        RadiosityRuntime.RuntimeInfluenceFixup fx = runtime.InfluenceFixups[i];
                        fx.Weight = ScaleW(fx.Weight);
                        runtime.InfluenceFixups[i] = fx;
                    }
                }
            }
            else if (settings.DeltaMatchRetailExpMass &&
                bake?.Slice?.SurfaceProbeWeights != null && bake.Slice.SurfaceProbePositions != null)
            {
                const float matchRadius = 4.0f;
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
                    // DeltaCalibrationOffset maps a rigidly-moved group back onto its ORIGINAL
                    // location, so its probes match the retail exp masses of the room they came
                    // from instead of falling back to the flat bias (a shifted level has no
                    // retail probes anywhere near its new position).
                    var pos = new Vector3(p.X, p.Y, p.Z) + settings.DeltaCalibrationOffset;

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
                        shift = settings.DeltaInfluenceWeightBias;
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
                            fellBack + " fell back to bias " + settings.DeltaInfluenceWeightBias +
                            (shifts.Count > 0 ? "  shift p10/50/90 = " + shifts[shifts.Count / 10] + "/" +
                             shifts[shifts.Count / 2] + "/" + shifts[shifts.Count * 9 / 10] : ""));
            }
        }

        /// <summary>
        /// The delta-bake's second half: bake the movers an edit ADDED or MOVED into one new
        /// slice appended to the kept retail runtime, so new geometry lights itself instead of
        /// rendering black (architecture) or fullbright (props).
        /// </summary>
        /// <remarks>
        /// <para>The delta unit is the geometry INSTANCE: any instance containing at least one
        /// delta mover is rebaked whole (retail islands never span composites, so this never
        /// splits a retail island - a moved mover pulls its island-mates along, which is right:
        /// their old rect would otherwise light them as if nothing had moved).</para>
        /// <para>Version limits, deliberate: the new slice has no volume-probe hash (dynamic
        /// objects inside brand-new rooms get no probe lighting yet), no cross-slice fixups
        /// (a new room lights itself from its own emissives; embedded new props will want links
        /// into retail clusters later), and no door transfers. It gets zero slice neighbours,
        /// which the fixup-range table shape permits by construction.</para>
        /// </remarks>
        public static int AppendDeltaSlices(
            Level level,
            RadiosityBakeSettings settings,
            HashSet<int> deltaMovers,
            Action<string> log = null)
        {
            if (deltaMovers == null || deltaMovers.Count == 0)
                return 0;
            RadiosityRuntime runtime = level.RadiosityRuntime
                ?? throw new InvalidOperationException("No runtime to append to.");

            // Translation-only moves keep their RETAIL island and get its data slid over:
            // re-baking a moved island into an appended slice fed the probes through cross-slice
            // fixups alone, and that path SATURATES at roughly half a native diet (CM9's server
            // rack: 0.38x retail at cloned weights, 0.53x with every slot at byte 255 - no weight
            // can close it). The retail island's own probes, diets, scatter, lights and rect are
            // all still valid after a translation; they just describe the old position. Movers
            // handled here leave the delta census entirely, so the patcher restores their retail
            // MODEL_PARAMS and the instance map keeps its retail rows.
            if (settings.DeltaTranslateMovedIslands)
                TranslateMovedIslands(level, runtime, settings, deltaMovers, log);
            if (deltaMovers.Count == 0)
                return 0;

            // Instancing appended resources for the new content; without a refresh they resolve
            // to write index -1 and geometry collection drops every new mover.
            level.Resources.RefreshWriteList();

            // The static-composites whitelist is derived from what RETAIL lightmapped - a freshly
            // added composite instance can never be on it, which silently skipped every mover of
            // a newly placed room. The delta census has already vetted these movers, and the
            // per-mover IsBakeable filter still applies, so the whitelist is dropped here.
            bool staticOnly = settings.StaticRadiosityCompositesOnly;
            settings.StaticRadiosityCompositesOnly = false;
            RadiosityGeometry geometry;
            try { geometry = RadiosityGeometry.CollectFromLevel(level, settings, log); }
            finally { settings.StaticRadiosityCompositesOnly = staticOnly; }
            if (geometry.TriangleCount == 0)
                return 0;
            geometry.Build(log);
            if (settings.UseCollisionForVisibility &&
                RadiosityOccluders.TryCollect(level, geometry, out float[] occluderVerts, out int[] occluderTris, log,
                                              skipDoorBarriers: settings.OpenDoorwaysForBake))
            {
                geometry.OccluderEndpointSlack = settings.OccluderEndpointSlack;
                geometry.OccluderSlackFraction = settings.OccluderSlackFraction;
                geometry.BuildOccluders(occluderVerts, occluderTris, log);
            }

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

            // GRAFT retail-bound islands into byte-clones of their retail slices first (see
            // GraftDeltaIslands): retail's field relights at parity where anything we bake
            // delivers about half, so edits inside existing rooms ride the clone. Whatever
            // remains (genuinely new content, uncovered islands) takes the appended-slice path.
            int graftedIslands = 0;
            if (settings.DeltaGraftRetailSlices)
                graftedIslands = GraftDeltaIslands(level, runtime, geometry, deltaInstances, settings, deltaMovers, log);
            if (deltaInstances.Count == 0)
            {
                log?.Invoke("Radiosity delta: " + graftedIslands + " islands grafted into retail-slice clones, nothing left to append");
                return graftedIslands;
            }

            foreach (RadiosityGeometry.Instance instance in deltaInstances)
            {
                RadiosityAtlas.RectSizeForBounds(instance.SurfaceArea, instance.BoundsMax - instance.BoundsMin,
                    instance.UvCoverage, settings, out int w, out int h, instance.UvAspect);

                // A MOVED island's pristine MODEL_PARAMS carry retail's own rect size for this
                // exact geometry - use it as a floor. The formula underquotes complex furniture
                // (CM7's moved shelving got 14x14 against retail's 18x16, a third fewer probes).
                if (settings.RetailModelParams != null)
                {
                    foreach (int moverIndex in instance.Movers)
                    {
                        Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                        if (mover.Resource == null)
                            continue;
                        ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                        if (!settings.RetailModelParams.TryGetValue(key, out byte[] pristine) || pristine.Length < 16)
                            continue;
                        int rw = (int)Math.Round(BitConverter.ToSingle(pristine, 0) + 0.5f);
                        int rh = (int)Math.Round(BitConverter.ToSingle(pristine, 4) + 0.5f);
                        if (rw >= 1 && rw <= 128 && rh >= 1 && rh <= 128)
                        {
                            w = Math.Max(w, rw);
                            h = Math.Max(h, rh);
                        }
                    }
                }
                instance.AtlasWidth = w;
                instance.AtlasHeight = h;
            }
            ApplyPerModelRects(deltaInstances, settings, log);
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
            // Donor shell (see RadiosityBakeSettings.DeltaDonorShell): everything the delta
            // probes should gather bounce from must live IN this slice - influence indices cannot
            // cross slices except through fixups, and the engine's fixup gather saturates at
            // about half a native diet. The surrounding retail islands are baked in as
            // cluster-only donors: their movers are never written to, they exist here purely as
            // lit surfaces for the real delta islands' diets, scatter and light injection.
            var bakeInstances = new List<RadiosityGeometry.Instance>(deltaInstances);
            if (settings.DeltaDonorShell)
            {
                var deltaSet = new HashSet<RadiosityGeometry.Instance>(deltaInstances);
                float reach = Math.Max(1.0f, settings.DeltaDonorShellRadius);

                // A donor must be geometry OUTSIDE the delta entirely. This call only knows its own
                // CHUNK's movers, so without the level-wide set every OTHER chunk's delta geometry
                // scores as a donor - and a donor is live for light injection. Measured on CM3:
                // the cam9 locker door is a 2x3 MEMBER in slice 11 (1-2 lights, 38.8 wLuma) and a
                // 24x24 DONOR in slice 12, where the 5-6 lights carrying ~110 wLuma actually landed
                // - lighting nothing, because nothing renders from a donor. The same duplication is
                // what left cam3's slice 11 holding 15 lights and ZERO probes under zone chunking.
                // It also spends the donor budget on the delta's own geometry.
                HashSet<int> excluded = settings.DeltaAllMovers ?? deltaMovers;

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
                    // Another chunk's delta geometry is not a donor - see the remark above.
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

                int budget = Math.Max(0, settings.DeltaDonorTexelBudget);
                // Clamp to the atlas space the group's OWN islands will not use. Without this the
                // selection spent the full budget (8,192 texels against ~4,100 free) and the
                // packer then dropped 618 donors - and it drops by AREA, not distance, so the
                // small NEAR donors were discarded while large far ones were kept. Selecting to
                // fit keeps the nearest instead, and is not the H62 mistake: that reserved donor
                // space in the PACKER and shrank 2,541 islands to save 1,430 donors.
                int groupTexels = 0;
                foreach (RadiosityGeometry.Instance inst in deltaInstances)
                    groupTexels += inst.AtlasWidth * inst.AtlasHeight;
                budget = Math.Min(budget, Math.Max(0, 15800 - groupTexels));
                log?.Invoke("    delta donor budget: group " + groupTexels + " texels -> " + budget + " for donors");
                int maxDim = Math.Max(2, settings.DeltaDonorMaxRectDim);
                int taken = 0, spent = 0, dupes = 0;
                // CM9 ships stacks of coincident duplicate composite instances (seven instance
                // ids on one records-room prop). Duplicates waste the budget AND poison the bake:
                // their texels sit at identical positions, so the input-probe Poisson pass blocks
                // all copies down to one object's worth - one run spent 8k texels on stacks and
                // came out with 128 input probes and 18 surface lights. One donor per footprint.
                var seenFootprints = new HashSet<(int, int, int, int, int, int)>();
                (int, int, int, int, int, int) Footprint(RadiosityGeometry.Instance inst) =>
                    ((int)Math.Round(inst.BoundsMin.X * 20), (int)Math.Round(inst.BoundsMin.Y * 20), (int)Math.Round(inst.BoundsMin.Z * 20),
                     (int)Math.Round(inst.BoundsMax.X * 20), (int)Math.Round(inst.BoundsMax.Y * 20), (int)Math.Round(inst.BoundsMax.Z * 20));
                foreach ((RadiosityGeometry.Instance inst, float dist) in donors)
                {
                    if (!seenFootprints.Add(Footprint(inst))) { dupes++; continue; }
                    RadiosityAtlas.RectSizeForBounds(inst.SurfaceArea, inst.BoundsMax - inst.BoundsMin,
                        inst.UvCoverage, settings, out int w, out int h, inst.UvAspect);
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
            }

            var deltaSlices = new List<List<RadiosityGeometry.Instance>> { bakeInstances };
            AllocateAtlases(deltaSlices, settings, log);

            Dictionary<int, float> emissiveAreas = ComputeEmissiveAreas(geometry);
            RetailLightPriors lightPriors = CalibrateWeightCoefficient(level, emissiveAreas, settings, log);
            lightPriors.LooseLookup = settings.DeltaLoosePriors;
            lightPriors.TwinSuppression = settings.DeltaTwinSuppression;

            // The volume hash is REQUIRED here, not optional: materials carrying the
            // RADIOSITY_DYNAMIC shader bit may not be lightmapped (the engine asserts on a rect
            // plus the dynamic bit), so everything dynamic inside the new content lights from
            // these probes or not at all.
            // Texel-coincident input probes for delta slices (see InputProbesOnTexels): without
            // the zero-distance cluster/probe self-pairs, the slice's cluster field never picks
            // up injected energy the way retail's does.
            DumpDeltaSliceMembers(level, bakeInstances, runtime.Slices.Count, settings, log);

            bool probesOnTexels = settings.InputProbesOnTexels;
            settings.InputProbesOnTexels = true;
            SliceBake bake;
            try { bake = BakeSlice(level, geometry, bakeInstances, runtime.Slices.Count, settings, emissiveAreas, lightPriors, log); }
            finally { settings.InputProbesOnTexels = probesOnTexels; }

            // A delta slice cut out of a room-coherent retail slice starts with almost no direct
            // light of its own: its islands' emissive texels are often dynamic movers (excluded
            // from the lightmap) and the room's emitters sample the ROOM slice's probes, never
            // ours. The unbaked-emitter pass fixes both - dynamic emitters are not "baked", and
            // static ones are rescued through their retail light priors - sampling each at our
            // probes with retail's own Scale/Weight. Without it the moved cam9 vent wall baked
            // 0 surface lights and rendered black beside its own glowing tubes.
            if (settings.EmitSurfaceLights)
                AddUnbakedEmitterLights(level, geometry, new[] { bake }, settings, lightPriors, log);

            if (settings.SurfaceLightWeightScale != 1.0f && bake?.Slice?.SurfaceLights?.Lights != null)
            {
                List<RadiosityRuntime.RuntimeSurfaceLights.Light> lights = bake.Slice.SurfaceLights.Lights;
                for (int i = 0; i < lights.Count; i++)
                {
                    RadiosityRuntime.RuntimeSurfaceLights.Light l = lights[i];
                    l.Weight = (byte)Math.Max(1, Math.Min(191, (int)Math.Round(l.Weight * settings.SurfaceLightWeightScale)));
                    lights[i] = l;
                }
            }

            FoldVisPaletteIntoRuntime(runtime, bake, log);

            int newSliceIndex = runtime.Slices.Count;
            runtime.Slices.Add(bake.Slice);

            int deltaFixups = AppendDeltaFixups(level, runtime, bake, newSliceIndex, geometry, settings, log);

            CalibrateDeltaEnergy(level, runtime, bake, newSliceIndex, settings, log);

            // Island ids: prefer the GAPS retail left in its own id space (ids inside
            // InstanceSliceIndices range that no map entry uses - retail assigned them to
            // geometry it excluded). A beyond-range id has no state anywhere; a gap id sits
            // inside whatever default state retail ships, which is the difference between the
            // runtime treating the island as powered and ignoring it entirely.
            var usedIds = new HashSet<int>();
            if (level.RadiosityInstanceMap != null)
                foreach (RadiosityInstanceMap.Entry e in level.RadiosityInstanceMap.Entries)
                    usedIds.Add(e.lightmap_transform);
            var gapIds = new Queue<int>();
            for (int id = 0; id < runtime.InstanceSliceIndices.Count; id++)
                if (!usedIds.Contains(id)) gapIds.Enqueue(id);

            // DIAGNOSTIC (env RADBAKE_STEAL_IDS="1,2,3"): deliberately sacrifice named retail
            // islands and hand their in-range ids to delta islands - the discriminator for
            // whether the engine honours EXTENDED transforms-table records or sizes its
            // per-island state from retail's count. The sacrificed islands' movers will sample
            // wrong data; only use ids of invisible junk.
            var stealIds = new Queue<int>();
            string stealEnv = Environment.GetEnvironmentVariable("RADBAKE_STEAL_IDS");
            if (!string.IsNullOrEmpty(stealEnv))
                foreach (string s in stealEnv.Split(','))
                    if (int.TryParse(s.Trim(), out int sid) && sid >= 0 && sid < runtime.InstanceSliceIndices.Count)
                        stealIds.Enqueue(sid);
            // HARVEST in-range ids from coincident-duplicate islands: a beyond-range id is
            // BROKEN - the engine sizes its per-island state from retail's transforms table
            // once and ignores appended records (proven by the cam7 stolen-id door: black with
            // an extended record, fully lit with an in-range id). Two mapped islands whose
            // movers sit at identical positions render identical pixels; repointing one twin's
            // rows onto the other (and copying its lightmap rect params) frees an id at no
            // visual cost. Harvested lazily, only as needed.
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
            int harvestedCount = 0, grownCount = 0;
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
                    harvestedCount++;
                    return freeId;
                }
                return -1;
            }
            bool islandForKeyGuard(int id) => id >= 0 && id < runtime.InstanceSliceIndices.Count && !usedIdsTaken.Contains(id);

            log?.Invoke("    delta island ids: " + gapIds.Count + " retail gaps, " + harvestable.Count +
                        " duplicate-island ids harvestable, for " + deltaInstances.Count + " islands");
            int sharedOverflowId = -1, sharedOverflowCount = 0;

            // Guarantee this slice a shareable in-range id up front: chunked bakes drained the
            // harvest well (H14 chunk 3: 0 harvestable for 95 islands, all emitted beyond-range
            // and rendered black - cam12). A sacrificial retail id repointed here serves any
            // number of islands through per-mover MODEL_PARAMS, exactly retail's own multi-rect
            // island convention.
            // KEPT IN GROW MODE (H39 measurement): growing every id beyond retail's count is
            // legal - nothing crashes and nothing renders black - but the delta came out
            // UNIFORMLY DIMMER on almost every camera (cam3 0.906 -> 0.632, cam5 1.021 -> 0.818,
            // cam11 1.108 -> 0.874) with a byte-for-byte identical bake otherwise. The reading:
            // the engine walks its own model instances and reads InstanceSliceIndices[i] for
            // each, so entries past that count are never visited and the slice they name is
            // never scheduled for relight. One in-range entry naming the slice is what H35 had
            // for free (1,213 islands crammed onto sacrificial id 905). So: grow the ids, but
            // still anchor each slice with one in-range entry.
            if (settings.DeltaSacrificialIslands != null && settings.DeltaSacrificialIslands.Count > 0)
            {
                int reserve = settings.DeltaSacrificialIslands.Dequeue();
                if (reserve >= 0 && reserve < runtime.InstanceSliceIndices.Count)
                {
                    sharedOverflowId = reserve;
                    runtime.InstanceSliceIndices[reserve] = newSliceIndex;
                    log?.Invoke("    delta island ids: sacrificial id " + reserve + " reserved as this slice's shared overflow");
                }
            }
            var recordWritten = new HashSet<int>();
            int nextId = runtime.InstanceSliceIndices.Count;
            // ALL rows per key: a resource has one map row PER SUBMESH of its model (the CM9
            // server rack's resource carries four rows, all pointing at the same island), and the
            // engine binds each submesh through its own row. Collapsing them into a single-entry
            // dictionary meant the delta update repointed ONE row and left the rest on the retail
            // island - three of the moved rack's four submeshes rendered from the stale binding
            // and the cabinet stayed black no matter how healthy the delta probes were.
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
                // A delta island whose movers ALL repoint away from one retail island orphans
                // that retail id - so reuse it. A retail id is in range of every per-island
                // state table the runtime keeps; a beyond-range id has no state anywhere, and
                // the appended-slice saturation (~0.5x however the diets arrive - cloned fixup
                // weights 0.38x, all-255 0.53x, full native donor diets 0.35x on the CM9 rack)
                // tracks the id, not the gather path, if the id-state hypothesis holds.
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
                bool foreignId = false;   //an id taken over from unrelated retail content
                if (settings.DeltaGrowIslandIds)
                {
                    // Grow InstanceSliceIndices instead of scavenging. Its own reused id still
                    // wins when the island genuinely orphaned one (free, and keeps the island's
                    // authored identity); everything else gets a fresh id.
                    if (retailIdReuse >= 0)
                    {
                        islandId = retailIdReuse;
                        runtime.InstanceSliceIndices[islandId] = newSliceIndex;
                    }
                    else
                    {
                        islandId = nextId++;
                        runtime.InstanceSliceIndices.Add(newSliceIndex);
                        grownCount++;
                    }
                }
                else if (retailIdReuse >= 0)
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
                else if (stealIds.Count > 0)
                {
                    islandId = stealIds.Dequeue();
                    runtime.InstanceSliceIndices[islandId] = newSliceIndex;
                    foreignId = true;
                    log?.Invoke("    delta island ids: STEALING in-range id " + islandId + " (diagnostic)");
                }
                else if ((islandId = HarvestId()) >= 0)
                {
                    runtime.InstanceSliceIndices[islandId] = newSliceIndex;
                    foreignId = true;
                    log?.Invoke("    delta island ids: harvested duplicate-island id " + islandId);
                    // The LAST harvestable id becomes the shared overflow id: retail itself
                    // ships multi-rect islands (CM9 island 1322 carries 5x5@119,2 AND 8x8@114,7
                    // under one id, its record holding just the first rect), so per-mover
                    // MODEL_PARAMS drives the sampling and any number of remaining delta
                    // islands can share one valid in-range id. A shared id beats a
                    // beyond-range one, which the engine provably reads as garbage.
                    if (harvestable.Count == 0 && sharedOverflowId < 0)
                        sharedOverflowId = islandId;
                }
                else if (sharedOverflowId >= 0)
                {
                    islandId = sharedOverflowId;
                    foreignId = true;
                    sharedOverflowCount++;
                }
                else
                {
                    // BROKEN: the engine sizes its per-island state from retail's transforms
                    // table and ignores appended records - this island will render black or
                    // flicker (the cam7 door proof). Emitted only when every in-range source
                    // is exhausted and nothing is shareable.
                    islandId = nextId++;
                    runtime.InstanceSliceIndices.Add(newSliceIndex);
                    log?.Invoke("    delta island ids: WARNING - no in-range id left, island " + islandId +
                                " is beyond retail's transform table and will misrender");
                }
                usedIdsTaken.Add(islandId);

                // The per-island lightmap-transform record (RADIOSITY_TRANSFORMS.BIN - Windows
                // Store build) is what the engine samples the atlas through: an island id at
                // or past that table's count reads GARBAGE - black on some cameras, flickering
                // on others (the CM3 door round: cam1 lit / cam12 black) - and a reused id would
                // sample the new slice at its OLD rect origin. CathodeLib does not own that file:
                // the caller persists a record for every id handed out here (first writer wins on
                // a shared id, matching retail's own multi-rect island convention).
                if (settings.DeltaIslandRecord != null && recordWritten.Add(islandId))
                    settings.DeltaIslandRecord(islandId, instance.AtlasX, instance.AtlasY, foreignId);

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
                        (sharedOverflowCount > 0 ? ", " + sharedOverflowCount + " islands sharing overflow id " + sharedOverflowId : "") +
                        (grownCount > 0 ? ", " + grownCount + " fresh ids grown (InstanceSliceIndices now " +
                                          runtime.InstanceSliceIndices.Count + ")" : ""));
            return graftedIslands + deltaInstances.Count;
        }

        #region SLICING

        /// <summary>
        /// Split instances into slices. Retail's own grouping is used where the level still carries
        /// it (see <see cref="RadiosityBakeSettings.MatchRetailSlices"/>); otherwise instances are
        /// split by recursive median split on their centroids until each slice's atlas demand fits.
        /// Spatial coherence matters for the fallback: influences are only gathered within a slice,
        /// so neighbours should share one.
        /// </summary>
        /// <summary>
        /// Force every island built from the SAME GEOMETRY to the same rect size - retail's own
        /// invariant.
        /// </summary>
        /// <remarks>
        /// <para>Measured on CM3 (2026-08-25, the <c>rectmodel</c> tool): <b>191 of 191</b>
        /// multi-instance models have an identical rect on every one of their instances - 100%,
        /// categorical rather than a tendency. RADIOSITY_LEVEL.BIN agrees from the authoring
        /// side, storing <c>ProbeCount</c> once per MODEL: CA lays a probe set out in model space
        /// and every placement reuses it. (Their "model" is a reusable geometry chunk - 285
        /// models over 2,639 placements covering ~14,279 movers - not a single prop mesh.)</para>
        /// <para>We size per island from instance-level area and UV coverage, so two placements
        /// of one chunk get different rects. Worse, it feeds back: chunking SPLITS instances, a
        /// split piece covers less of the UV square, and <c>RectSizeForBounds</c> divides area by
        /// <c>pow(uvCoverage, UvCoverageCompensation)</c> - so splitting INFLATES the rect. The
        /// demand chunker measures unsplit instances and cannot see it, which is why a 75% fill
        /// target landed at 94/95% and shrank 1,426 islands, leaving ~75% of delta islands with a
        /// rect 2 texels or less on a side.</para>
        /// <para>Grouping is by a geometry signature - the island's movers' resources and their
        /// positions relative to the island centroid, quantised - so repeated level chunks land
        /// in one group wherever they are placed. Each group takes the rect of its member with
        /// the HIGHEST UV coverage: low coverage is the split artifact, so the most complete
        /// member carries the truest measurement.</para>
        /// </remarks>
        private static void ApplyPerModelRects(
            IEnumerable<RadiosityGeometry.Instance> instances,
            RadiosityBakeSettings settings, Action<string> log)
        {
            if (!settings.PerModelRectSizes)
                return;

            var groups = new Dictionary<string, List<RadiosityGeometry.Instance>>();
            foreach (RadiosityGeometry.Instance inst in instances)
            {
                string sig = GeometrySignature(inst);
                if (sig == null) continue;
                if (!groups.TryGetValue(sig, out List<RadiosityGeometry.Instance> g))
                    groups[sig] = g = new List<RadiosityGeometry.Instance>();
                g.Add(inst);
            }

            int changed = 0, multi = 0;
            foreach (List<RadiosityGeometry.Instance> g in groups.Values)
            {
                if (g.Count < 2) continue;
                multi++;
                RadiosityGeometry.Instance best = g[0];
                foreach (RadiosityGeometry.Instance inst in g)
                    if (inst.UvCoverage > best.UvCoverage) best = inst;
                foreach (RadiosityGeometry.Instance inst in g)
                {
                    if (inst.AtlasWidth == best.AtlasWidth && inst.AtlasHeight == best.AtlasHeight) continue;
                    inst.AtlasWidth = best.AtlasWidth;
                    inst.AtlasHeight = best.AtlasHeight;
                    changed++;
                }
            }
            log?.Invoke("    per-model rects: " + groups.Count + " distinct geometries, " + multi +
                        " repeated, " + changed + " islands resized to their group's rect");
        }

        /// <summary>
        /// Identity of an island's GEOMETRY, independent of where it is placed: how many movers
        /// it has, its bounding extents sorted (so rotations of one chunk agree) and its surface
        /// area, both quantised.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT keyed on resources: each placement of a chunk carries its own
        /// resources, so those never match across placements. Area plus extents is what actually
        /// identifies repeated geometry - and it is also exactly the input pair
        /// <see cref="RadiosityAtlas.RectSizeForBounds"/> uses, so two islands sharing a signature
        /// would already receive the same rect were it not for the UV-coverage term. That makes a
        /// signature collision harmless: the members would have been sized alike anyway.
        /// </remarks>
        private static string GeometrySignature(RadiosityGeometry.Instance inst)
        {
            if (inst?.Movers == null || inst.Movers.Count == 0 || inst.SurfaceArea <= 0.0f)
                return null;
            Vector3 size = inst.BoundsMax - inst.BoundsMin;
            if (!(size.X >= 0.0f) || !(size.Y >= 0.0f) || !(size.Z >= 0.0f))
                return null;
            var ext = new[] { size.X, size.Y, size.Z };
            Array.Sort(ext);
            // 1cm on extents, 0.1% on area: tight enough that distinct chunks stay apart, loose
            // enough that float drift between placements does not split a group.
            return inst.Movers.Count + "|" +
                   (int)Math.Round(ext[0] * 100) + "," + (int)Math.Round(ext[1] * 100) + "," +
                   (int)Math.Round(ext[2] * 100) + "|" +
                   (long)Math.Round(inst.SurfaceArea * 1000.0);
        }

        private static List<List<RadiosityGeometry.Instance>> PartitionIntoSlices(
            RadiosityGeometry geometry, RadiosityRuntime retail, RadiosityBakeSettings settings, Action<string> log)
        {
            foreach (RadiosityGeometry.Instance instance in geometry.Instances)
            {
                // Retail's own rect for this island: verbatim, or as a per-dimension FLOOR under
                // the formula (never smaller than retail; the formula and its boost may exceed).
                // The floor exists because verbatim retail rects fixed Torrens but returned CM3's
                // dim floor - the levels the boost fixed need certain rects LARGER than retail's.
                int[] retailRect = null;
                if (settings.RetailRectSizes != null && instance.RetailIslandId >= 0)
                    settings.RetailRectSizes.TryGetValue(instance.RetailIslandId, out retailRect);
                if (retailRect != null && !settings.RetailRectSizesAsFloor)
                {
                    instance.AtlasWidth = Math.Max(1, Math.Min(AtlasSize, retailRect[0]));
                    instance.AtlasHeight = Math.Max(1, Math.Min(AtlasSize, retailRect[1]));
                    continue;
                }
                RadiosityAtlas.RectSizeForBounds(instance.SurfaceArea, instance.BoundsMax - instance.BoundsMin,
                    instance.UvCoverage, settings, out int w, out int h, instance.UvAspect);
                if (retailRect != null)
                {
                    w = Math.Max(w, Math.Min(AtlasSize, retailRect[0]));
                    h = Math.Max(h, Math.Min(AtlasSize, retailRect[1]));
                }
                instance.AtlasWidth = w;
                instance.AtlasHeight = h;
            }

            ApplyPerModelRects(geometry.Instances, settings, log);

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

                // The atlas's top-left 16x16 corner is RESERVED - the engine owns it. Texel (0,0)
                // alone is the "no lightmap" sentinel (a MODEL_PARAMS rect at the exact origin
                // renders unmapped-dark, which buried ChallengeMap7's moved shelving), but the
                // reservation is the whole corner: retail ships rects at y=0 (x>=16) and x=0
                // (y>=16) freely, yet NO rect intersecting x<16 && y<16 exists in any measured
                // level (0 of 16,511 rects across SCI_Hub/CM3/Solace) - and every island we
                // packed there rendered BLACK (SCI_Hub cam7/9/12 ceilings) or FLICKERED
                // (cam2/8/16) with bake data that passed every instrument: map rows, rects,
                // slices, mangle, links and light diet all at retail parity.
                atlas.TryAllocate(16, 16, out _, out _);

                // Largest first: skyline packers waste far less space that way. Donors pack
                // after every real island (they must never displace or shrink one) and are
                // DROPPED on overflow rather than parked - a donor squeezed onto the shared
                // 1x1 would rasterise its whole island into one arbitrary texel.
                // Donors last, which H62 settled: packing them FIRST reserved their space but made
                // members shrink instead, 2,541 islands shrunk against 1,430 donors saved, and the
                // score fell 3.5. Shrinking a member costs more than dropping a donor.
                //
                // The donor DROPS themselves (618/638/807 across runs) were not an estimator
                // error: the demand chunker predicts 12,275 texels per chunk and the packer places
                // 12,275 and 12,276 - exact, measured off the slice-member dump. They came from
                // the lightmap path selecting donors against the raw budget with no group clamp at
                // all; it now clamps to (15800 - groupTexels) at selection, so they fit.
                bool donorsFirst = settings.DonorsPackFirst;
                slices[s].Sort((a, b) => a.DonorOnly != b.DonorOnly
                    ? (a.DonorOnly ? (donorsFirst ? -1 : 1) : (donorsFirst ? 1 : -1))
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
                        // Nothing left. Park it on a 1x1 just outside the reserved corner: the
                        // island still resolves to a valid texel, it just shares lighting with
                        // whatever else is there. Never inside x<16 && y<16 - the engine owns
                        // that corner (and (0,0) is the "no lightmap" sentinel).
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

            /// <summary>
            /// Optional per-texel brightness proxy for THIS slice acting as a fixup emitter
            /// (injected surface-light energy near the texel). When set, fixup candidate
            /// selection ranks by formFactor x proxy instead of pure geometry - without it the
            /// close dim floor crowds the far bright ceiling out of the cap, which measured as
            /// UP-weight share 27% ours vs 41% retail on the CM7 shelving.
            /// </summary>
            public float[] TexelRadianceProxy;
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
            /// A gutter cell filled by <see cref="FillAtlasGutters"/>: it is a cluster (emitter)
            /// and an input-probe binding site but never a surface probe, matching retail, whose
            /// live-cell counts exceed its rect sums by ~12% while its surface probes stay below
            /// them.
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

        /// <summary>
        /// World area each live atlas texel represents, per instance: the instance's surface area
        /// shared over the texels its rect actually claimed.
        /// </summary>
        /// <remarks>
        /// This is what a cluster stands for when a receiver gathers from it, and it varies by
        /// orders of magnitude between instances because rect sizes do. See
        /// <see cref="RadiosityBakeSettings.InfluenceClusterAreaNormalisation"/>.
        /// </remarks>
        private static float[] MeasureTexelAreas(List<RadiosityGeometry.Instance> instances, SurfaceTexel[] texels, out float median)
        {
            var area = new float[AtlasTexels];
            median = 0.0f;
            foreach (RadiosityGeometry.Instance instance in instances)
            {
                int live = 0;
                for (int y = instance.AtlasY; y < instance.AtlasY + instance.AtlasHeight && y < AtlasSize; y++)
                    for (int x = instance.AtlasX; x < instance.AtlasX + instance.AtlasWidth && x < AtlasSize; x++)
                        if (y >= 0 && x >= 0 && texels[y * AtlasSize + x].Live) live++;
                if (live == 0 || instance.SurfaceArea <= 0.0f) continue;

                float per = instance.SurfaceArea / live;
                for (int y = instance.AtlasY; y < instance.AtlasY + instance.AtlasHeight && y < AtlasSize; y++)
                    for (int x = instance.AtlasX; x < instance.AtlasX + instance.AtlasWidth && x < AtlasSize; x++)
                        if (y >= 0 && x >= 0 && texels[y * AtlasSize + x].Live) area[y * AtlasSize + x] = per;
            }

            //The correction that uses this is meant to be redistributive rather than a global
            //brightness knob, so it is measured against this slice's own typical texel rather than
            //the nominal MetresSquaredPerTexel - an island denser than its neighbours loses light,
            //and that difference is the part worth correcting.
            var liveAreas = new List<float>();
            for (int i = 0; i < AtlasTexels; i++) if (area[i] > 0.0f) liveAreas.Add(area[i]);
            if (liveAreas.Count != 0)
            {
                liveAreas.Sort();
                median = liveAreas[liveAreas.Count / 2];
            }

            return area;
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
            ApplyEmissiveAlbedoConvention(level, texels, lightPriors, settings, log);
            ResolveRayOrigins(geometry, texels, settings);

            // Retail's atlases have (almost) no dead cells: every slice's live-cluster count
            // exceeds its MODEL_PARAMS rect sum, and ChallengeMap4 slice 0 is a completely full
            // 16384-cell grid. Our packer leaves 12-45% dead gutter between rects, which makes
            // what sits next to a rect's edge - and therefore anything that reads across it - an
            // arbitrary function of packing order. Filling the gutter with cluster-only clones of
            // the nearest live cell reproduces retail's observable state and removes the layout
            // lottery from the emitter field.
            // The engine-owned 16x16 corner must be fully live even when gutters are not filled,
            // so the fill always runs; without the setting it touches only the corner.
            FillAtlasGutters(texels, !settings.FillAtlasGutters, log);

            int liveCount = 0;
            for (int i = 0; i < AtlasTexels; i++) if (texels[i].Live) liveCount++;

            // ---- 2. Surface probes: live texels compacted into 16x16 tiles --------------------
            // Surface probes are NOT atlas-indexed. They are a compacted list packed into the same
            // 256x64 tiled texture as the input probes, which is why the surface probe tree's leaf
            // rects predict the live set exactly in all 128 retail slices, and why the influence
            // maps key on a probe slot rather than an atlas texel.
            var surfaceOrder = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && !texels[i].ClusterOnly) surfaceOrder.Add(i);
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
            // Retail's input probes are TEXEL-COINCIDENT: ~90% sit at exactly a live cluster
            // texel's position (measured on every CM7 retail slice), and virtually every such
            // probe carries a zero-distance scatter self-pair with its cluster - the coupling
            // that lets injected probe energy enter the cluster field in one hop. Free surface
            // scattering never reproduces that (4 coincident of 2,231 on the CM7 delta slice),
            // so delta slices place probes ON live texels instead.
            List<ProbePoint> inputProbes = settings.InputProbesOnTexels
                ? TexelInputProbes(texels, liveTexels, settings)
                : ScatterInputProbes(geometry, instances, settings, level, lightPriors);

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
            float[] texelArea = MeasureTexelAreas(instances, texels, out float medianTexelArea);
            int influenceCount = settings.InfluenceHemisphereSolve
                ? SolveInfluencesHemisphere(geometry, texels, surfaceSlotForTexel, nearestProbeForTexel, slice, settings, out var transfers, out byte[] usedSlots, log)
                : SolveInfluences(geometry, texels, surfaceSlotForTexel, nearestProbeForTexel, slice, settings, texelArea, medianTexelArea, out transfers, out usedSlots, log);

            // A texel whose probe came out of the solve with ZERO influences renders BLACK, and the
            // mangle map's bilinear read smears that across the whole rect. The map above is built
            // BEFORE the solve, so it cannot know which probes end up empty - it only routes around
            // texels that were never claimed. Measured on CM3 cam16's wall terminal (island 2279,
            // a 4x4 rect): retail leaves 2 of its 16 texels unresolved and has NO zero-influence
            // probes, while we claimed all 16 and left 2 gathering nothing - link count p10 0
            // against retail's 17 - which is the dark bar down that panel. Re-point those texels at
            // the nearest neighbour that did gather, exactly as unclaimed texels are handled.
            if (settings.RepointZeroInfluenceTexels && usedSlots != null)
            {
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
            }
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

            // The hash box must cover the slice's CONSUMER space, not just its probe sources:
            // retail's boxes extend well above the probes (CM3 slice 0 reaches y 12.8 with no
            // probe above 6.8; CM9's reach 97.8 up its shafts), because dynamic movers sample
            // the hash from the air of every room. Our probe-hugging box cut 6 m off CM3 slice
            // 0 and sent every elevated dynamic instance out of bounds. The slice's instance
            // geometry bounds are the available proxy for that consumer space.
            float geoMinY = float.MaxValue, geoMaxY = float.MinValue;
            foreach (RadiosityGeometry.Instance inst in instances)
            {
                if (inst.BoundsMin.Y < geoMinY) geoMinY = inst.BoundsMin.Y;
                if (inst.BoundsMax.Y > geoMaxY) geoMaxY = inst.BoundsMax.Y;
            }

            slice.VolumeProbeHash = settings.EmitVolumeProbes

                ? BuildVolumeProbeHash(geometry, texels, nearestProbeForTexel, surfaceSlotForTexel, settings, geoMinY, geoMaxY, out visGrids)

                // An absent hash is signalled by NumSubdivsPerLevel = 0, with a zero AABB and zero
                // dims - that is exactly what BSP_LV426_PT01 slice 0 ships, the one retail slice of
                // 128 with no volume probes. Every populated hash uses 3. Leaving the subdivision
                // count at 3 while writing no items, nodes or offsets is a combination that occurs
                // in no retail level: it tells the object probe pass there is a hash to walk and
                // then hands it nothing, which is where render_object_probes faults.
                : new RadiosityRuntime.VolumeProbeHash();

            // ---- 9. Emissive geometry becomes surface lights ---------------------------------
            slice.SurfaceLights = settings.EmitSurfaceLights

                ? BuildSurfaceLights(level, geometry, instances, texels, nearestProbeForTexel, settings, emissiveAreas, lightPriors, log)

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
                // Real surface probes (donor/gutter cluster-only texels excluded): liveCount here
                // read "7951 surface probes" for a 4-tiny-island delta slice once donors joined.
                SurfaceProbeCount = surfaceOrder.Count,
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
        /// Input probes placed ON live atlas texels, Poisson-thinned to the configured spacing.
        /// Because the positions are the texel positions verbatim, the half-float encodes come
        /// out identical to ClusterPositions and the local scatter pass emits the zero-distance
        /// cluster/probe self-pairs retail ships (see the call site).
        /// </summary>
        private static List<ProbePoint> TexelInputProbes(SurfaceTexel[] texels, List<int> liveTexels, RadiosityBakeSettings settings)
        {
            float spacing = Math.Max(0.05f, settings.InputProbeSpacing);
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
            RadiosityBakeSettings settings,
            Level level = null,
            RetailLightPriors lightPriors = null)
        {
            float spacing = Math.Max(0.01f, settings.InputProbeSpacing);
            float perSquareMetre = Math.Max(1.0f, settings.InputProbeCandidatesPerSquareMetre);

            // Dart-throwing needs a scrambled visit order, so candidates are accumulated per
            // triangle and then walked in a hashed sequence rather than in geometry order.
            //Per-mover light colour, where retail attached a surface light to the entity. Retail's
            //own input-probe albedo on luminous panel surfaces stores the LIGHT's colour, not the
            //fixture's housing texture (CEILING_HZDLAB: retail ~(180,175,164) = the room's R174
            //G174 B174 lights, our diffuse sample ~(23,22,22)) - so a lit fixture should bounce
            //its glow colour, not the dark plastic it is moulded from.
            var lightColour = new Dictionary<int, Vector3>();
            if (settings.LightColourProbeAlbedo && level != null && lightPriors != null)
            {
                foreach (RadiosityGeometry.Instance inst2 in instances)
                    foreach (int m in inst2.Movers)
                    {
                        if (lightColour.ContainsKey(m)) continue;
                        Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[m];
                        if (mv?.Resource == null) continue;
                        RetailLightPriors.Prior prior = lightPriors.Lookup(mv.Resource);
                        if (prior != null)
                            lightColour[m] = new Vector3(prior.R / 255.0f, prior.G / 255.0f, prior.B / 255.0f);
                    }

                //State-variant siblings: a lit fixture ships as several coincident movers, and the
                //light slice sits on only one of them. The unlit twin has the same geometry and
                //renders lit whenever the fixture is on, so its bounce should carry the glow too -
                //retail's probe albedo on ChallengeMap4's twin walls (mover 2511 next to lit 2512)
                //is the sibling's light colour, not the dark plastic. Same island only, within
                //half a metre of a prior-carrying mover's origin.
                if (settings.LightColourProbeAlbedoSiblings)
                    foreach (RadiosityGeometry.Instance inst3 in instances)
                    {
                        var lit = inst3.Movers.Where(m => lightColour.ContainsKey(m)).ToList();
                        if (lit.Count == 0) continue;
                        foreach (int m in inst3.Movers)
                        {
                            if (lightColour.ContainsKey(m)) continue;
                            Movers.MOVER_DESCRIPTOR mv2 = level.Movers.Entries[m];
                            Vector3 at = new Vector3(mv2.Transform.M41, mv2.Transform.M42, mv2.Transform.M43);
                            foreach (int litMover in lit)
                            {
                                Movers.MOVER_DESCRIPTOR lm = level.Movers.Entries[litMover];
                                Vector3 lp = new Vector3(lm.Transform.M41, lm.Transform.M42, lm.Transform.M43);
                                if (Vector3.DistanceSquared(lp, at) > 0.5f * 0.5f) continue;
                                lightColour[m] = lightColour[litMover];
                                break;
                            }
                        }
                    }
            }

            var candidates = new List<ProbePoint>();
            foreach (RadiosityGeometry.Instance instance in instances)
            {
                foreach (int tri in instance.Triangles)
                {
                    float area = geometry.TriangleArea(tri);
                    if (area <= 1e-7f)
                        continue;

                    Vector3 lit = Vector3.Zero;
                    bool hasLit = false;
                    if (lightColour.Count != 0)
                    {
                        int slot2 = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                        if (slot2 >= 0 && slot2 < instance.Movers.Count)
                            hasLit = lightColour.TryGetValue(instance.Movers[slot2], out lit);
                    }

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
                            Albedo = hasLit ? lit : geometry.SampleAlbedo(tri, diffuseUv),
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
            // 0..1 lightmap UVs. Letting the first arrival keep every texel it touches hands the
            // whole rect to one mover wherever those footprints DO overlap, and leaves the rest of
            // the composite with no probes at all - which is why our coverage missed 38.7% of the
            // cells retail fills while running up to 20x its density in others, and why a
            // corridor's floor could vanish while its walls stayed dense. So each mover gets a
            // quota, and triangles past it are dropped.
            //
            // The quota is the mover's own UV FOOTPRINT, not its share of the composite's world
            // area. Area was the wrong currency: what a mover needs from the rect is however many
            // texels its lightmap UVs actually land on, and the two are only loosely related - an
            // authored lightmap set gives a big flat wall a small dense patch and a fiddly prop a
            // sprawling one. Measured across ChallengeMap4's 1968 multi-mover islands, mover
            // footprints overlap by a median of just 12.7%, so they are mostly disjoint and the
            // rationing should not bind at all; under area shares it bound constantly and in both
            // directions. On island 1549 (the executive lounge, cam7/cam9) mover 3166 drew 31.3%
            // of the rect for a 19% footprint while mover 3156 was capped at 15.4% for a 22% one,
            // so 3156 lost triangles, its texels stayed dead, and FillUnclaimed handed them to a
            // neighbour - the room's average stayed about right while individual walls landed
            // 31 luma under retail and others 38 over.
            //
            // With footprint quotas the disjoint majority is never rationed (the quotas simply sum
            // to the footprint total), and only genuinely contended rects - repeated meshes sharing
            // one UV layout, which do exist at 82-96% overlap - are shared out, proportionally.
            int rectTexels = Math.Max(1, instance.AtlasWidth * instance.AtlasHeight);
            var quota = new int[instance.Movers.Count];
            var claimed = new int[instance.Movers.Count];

            if (!settings.FootprintRectQuota)
            {
                float totalArea = Math.Max(1e-6f, instance.SurfaceArea);
                for (int m = 0; m < quota.Length; m++)
                {
                    float share = m < instance.MoverAreas.Count ? instance.MoverAreas[m] : 0.0f;
                    quota[m] = share <= 0.0f ? 0 : Math.Max(1, (int)Math.Round(rectTexels * share / totalArea));
                }
            }
            else
            {
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
            }

            //RADBAKE_RASTDUMP diagnostics: which triangles the quota drops and which raster visits
            //an earlier winner blocks, per mover.
            string rastDump = Environment.GetEnvironmentVariable("RADBAKE_RASTDUMP");
            var dbgDroppedQuota = rastDump != null ? new int[quota.Length] : null;
            var dbgBlockedLive = rastDump != null ? new int[quota.Length] : null;
            var dbgTris = rastDump != null ? new int[quota.Length] : null;


            foreach (int tri in instance.Triangles)
            {
                int moverSlot = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                if (moverSlot < 0 || moverSlot >= quota.Length)
                    moverSlot = 0;
                if (dbgTris != null) dbgTris[moverSlot]++;

                // Optionally let an emitter through regardless of budget. The reasoning was that a
                // light is not an area claim - it is the only way that surface enters the solve, so
                // losing it to the quota removes light from the level rather than just moving a
                // probe (#35). Measured on SCI_Hub it does not do what it was meant to: light
                // slices went 1416 to 1386 rather than up towards retail's 1818, and the render
                // scored slightly worse (rmse 22.97 to 23.88). Off until something explains that.
                bool exempt = settings.ExemptEmissiveFromRectQuota
                              && tri < geometry.TriangleEmissive.Length
                              && geometry.TriangleEmissive[tri] != Vector3.Zero;
                //The quota no longer drops whole triangles here: that starved late-processed
                //faces out of the reservoir below AND threw away their albedo taps. It now gates
                //only the dead-texel claim inside the loop, which is the thing it rations.

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

                        //First-covering-triangle wins, as before. A coverage-weighted reservoir
                        //re-draw was tried here for the stacked-chart problem (several faces
                        //aliasing one texel) and REGRESSED the well-behaved islands - CM9's
                        //RecordsRoom island went 1.77 -> 8.40 rmse while barely helping the dark
                        //rack, so first-come stays and the quota alone was softened: it now gates
                        //only the dead-texel claim rather than dropping whole triangles, which
                        //keeps late faces' albedo taps and lets them claim any texel the earlier
                        //faces left dead.
                        if (texels[index].Live)
                        {
                            if (dbgBlockedLive != null && coveredTaps > 0 && texels[index].MoverIndex != moverIndex)
                                dbgBlockedLive[moverSlot]++;
                            continue;
                        }
                        if (!exempt && quota.Length > 0 && claimed[moverSlot] >= quota[moverSlot])
                        {
                            if (dbgDroppedQuota != null) dbgDroppedQuota[moverSlot]++;
                            continue;
                        }
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

            if (rastDump != null)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(rastDump);
                    using (var w = new System.IO.StreamWriter(System.IO.Path.Combine(rastDump,
                        "rast_" + instance.AtlasX + "_" + instance.AtlasY + ".csv")))
                    {
                        w.WriteLine("# instance rect " + instance.AtlasWidth + "x" + instance.AtlasHeight + " @" + instance.AtlasX + "," + instance.AtlasY);
                        w.WriteLine("moverSlot,moverIndex,quota,claimed,tris,droppedQuota,blockedByOtherLive,uvMin,uvMax,worldXMin,worldXMax");
                        var uvMin = new Vector2[quota.Length]; var uvMax = new Vector2[quota.Length];
                        var wxMin = new float[quota.Length]; var wxMax = new float[quota.Length];
                        for (int m = 0; m < quota.Length; m++) { uvMin[m] = new Vector2(9e9f); uvMax[m] = new Vector2(-9e9f); wxMin[m] = 9e9f; wxMax[m] = -9e9f; }
                        foreach (int tri in instance.Triangles)
                        {
                            int ms = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                            if (ms < 0 || ms >= quota.Length) ms = 0;
                            for (int v = 0; v < 3; v++)
                            {
                                int vi = geometry.Tris[tri * 3 + v];
                                Vector2 uv = geometry.LightmapUVs[vi];
                                uvMin[ms] = Vector2.Min(uvMin[ms], uv); uvMax[ms] = Vector2.Max(uvMax[ms], uv);
                                float wx = geometry.Verts[vi * 3];
                                if (wx < wxMin[ms]) wxMin[ms] = wx;
                                if (wx > wxMax[ms]) wxMax[ms] = wx;
                            }
                        }
                        for (int m = 0; m < quota.Length; m++)
                            w.WriteLine(m + "," + (m < instance.Movers.Count ? instance.Movers[m] : -1) + "," +
                                quota[m] + "," + claimed[m] + "," + dbgTris[m] + "," + dbgDroppedQuota[m] + "," + dbgBlockedLive[m] + "," +
                                "(" + uvMin[m].X.ToString("0.00") + ";" + uvMin[m].Y.ToString("0.00") + "),(" + uvMax[m].X.ToString("0.00") + ";" + uvMax[m].Y.ToString("0.00") + ")," +
                                wxMin[m].ToString("0.00") + "," + wxMax[m].ToString("0.00"));
                        w.WriteLine("x,y,moverIndex,px,py,pz,nx,ny,nz");
                        for (int y = instance.AtlasY; y < instance.AtlasY + instance.AtlasHeight; y++)
                            for (int x = instance.AtlasX; x < instance.AtlasX + instance.AtlasWidth; x++)
                            {
                                SurfaceTexel t = texels[y * AtlasSize + x];
                                if (!t.Live) { w.WriteLine((x - instance.AtlasX) + "," + (y - instance.AtlasY) + ",DEAD"); continue; }
                                w.WriteLine((x - instance.AtlasX) + "," + (y - instance.AtlasY) + "," + t.MoverIndex + "," +
                                    t.Position.X.ToString("0.###") + "," + t.Position.Y.ToString("0.###") + "," + t.Position.Z.ToString("0.###") + "," +
                                    t.Normal.X.ToString("0.##") + "," + t.Normal.Y.ToString("0.##") + "," + t.Normal.Z.ToString("0.##"));
                            }
                    }
                }
                catch (Exception e) { Console.WriteLine("RASTDUMP failed: " + e.Message); }
            }
        }

        /// <summary>
        /// How many of the instance's rect texels each mover's lightmap UVs actually reach.
        /// </summary>
        /// <remarks>
        /// This is the currency <see cref="RasteriseByUv"/> rations the rect in. It measures demand
        /// rather than contention: a texel counts for every mover whose triangles cover it, so the
        /// totals exceed the rect exactly to the extent that the movers' UV footprints overlap, and
        /// the rationing only binds when they genuinely do. The coverage rule is the rasteriser's
        /// own - the texel centre with the same seam bias, or failing that any sub-tap - so a
        /// mover's quota is counted the same way as its claims.
        /// </remarks>
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

            // Hidden-face note: the authored lightmap charts are a hidden-face oracle (CA
            // leaves the inside of a closed prop, the top of a ceiling panel under a plenum,
            // the back of a flush panel UNMAPPED), and F8's cam10 ceiling read 52% of its
            // blend from a dark plenum-side probe. Skipping degenerate-UV triangles was tried
            // in F9 together with an above-surface hash fill and the pair regressed several
            // rooms to the raw-bed state - reverted; isolate before re-attempting.
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

                // NOTE(F9): skipping degenerate-lightmap-UV triangles here (the authored
                // hidden-face oracle) was tried together with an above-surface hash fill and
                // the pair regressed several rooms to the raw-bed state; both were reverted
                // to the F8 baseline. Isolate before re-attempting either.

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
        /// Fill dead atlas cells with cluster-only clones of the nearest live cell, within a
        /// bounded ring search. Cloned cells carry the neighbour's surface (position, normal,
        /// resolved albedo) but no emissive - the surface-light pass must not gain emitter area -
        /// and never become surface probes.
        /// </summary>
        private static void FillAtlasGutters(SurfaceTexel[] texels, bool cornerOnly, Action<string> log)
        {
            // Retail leaves a few percent dead on some slices, so the search is bounded: a dead
            // cell more than MaxRing cells from any live one stays dead. EXCEPT the engine-owned
            // 16x16 corner: island rects are barred from it (see AllocateAtlases) but it must
            // never hold a dead texel - retail ships it 256/256 live on every slice measured,
            // and leaving 100 of its cells dead exploded whole frames to 10-14x (SCI_Hub dirt6:
            // the engine's own corner consumers read the dead cells as garbage). Corner cells
            // therefore search as far as it takes.
            const int MaxRing = 6;
            int filled = 0, stillDead = 0;
            var fills = new List<(int cell, int source)>();
            for (int y = 0; y < AtlasSize; y++)
            {
                for (int x = 0; x < AtlasSize; x++)
                {
                    int i = y * AtlasSize + x;
                    if (texels[i].Live)
                        continue;
                    bool corner = x < 16 && y < 16;
                    if (cornerOnly && !corner)
                        continue;
                    int maxRing = corner ? AtlasSize : MaxRing;
                    int source = -1;
                    for (int r = 1; r <= maxRing && source < 0; r++)
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
        /// Split the delta lightmap movers into chunks sized by the atlas TEXELS their islands
        /// need, balanced so no chunk's atlas runs hot enough to fragment. Islands are kept whole,
        /// which a mover-count split does not guarantee either.
        /// See <see cref="RadiosityBakeSettings.DeltaAtlasFillTarget"/> for why.
        /// </summary>
        internal static List<HashSet<int>> ChunkDeltaByAtlasDemand(
            Level level, RadiosityBakeSettings settings, HashSet<int> lightmapMovers, Action<string> log)
        {
            RadiosityGeometry geometry;
            try { geometry = RadiosityGeometry.CollectFromLevel(level, settings, null); }
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
                RadiosityAtlas.RectSizeForBounds(inst.SurfaceArea, inst.BoundsMax - inst.BoundsMin,
                    inst.UvCoverage, settings, out int w, out int h, inst.UvAspect);
                int texels = Math.Max(1, w * h);
                demand.Add((mine, texels));
                total += texels;
            }
            if (demand.Count == 0) return null;

            int budget = Math.Max(1024, (int)(AtlasTexels * Math.Min(0.95f, settings.DeltaAtlasFillTarget)));
            int chunkCount = Math.Max(1, (int)Math.Ceiling(total / (double)budget));

            var chunks = new List<HashSet<int>>();
            var fill = new List<long>();

            if (settings.DeltaZoneChunks)
            {
                // ZONE-COHERENT, mirroring the probe path's proven scheme: keep a room's islands
                // in ONE chunk, take the largest zones first, and first-fit so the space a big
                // zone leaves behind is filled by smaller ones.
                //
                // The worst-fit path below balances FILL, which necessarily scatters a room's
                // islands across chunks - and a slice cannot be lit by another slice's lights.
                // That is the failure UnbakedEmitterReach's own remarks describe ("the chunker put
                // one duplicated room's walls in slice 8 and its ceiling fixtures in slice 9...
                // the room rendered pitch black"), and why that rescue reach has to be so long.
                // Chunking by room removes the cause instead of compensating for it.
                var byZone = new Dictionary<uint, (List<(List<int> movers, int texels)> islands, long texels)>();
                foreach ((List<int> movers, int texels) in demand)
                {
                    uint zone = 0;
                    foreach (int m in movers)
                    {
                        var z = level.Movers.Entries[m].PrimaryZoneID;
                        if (z != CATHODE.Scripting.ShortGuid.Invalid) { zone = z.AsUInt32; break; }
                    }
                    if (!byZone.TryGetValue(zone, out var acc)) acc = (new List<(List<int>, int)>(), 0);
                    acc.islands.Add((movers, texels));
                    acc.texels += texels;
                    byZone[zone] = acc;
                }

                // An island's centroid, so a zone too big for one atlas splits along space rather
                // than at an arbitrary point in the island list.
                Vector3 Centroid(List<int> movers)
                {
                    var sum = Vector3.Zero;
                    int n = 0;
                    foreach (int m in movers)
                    {
                        Matrix4x4 t = level.Movers.Entries[m].Transform;
                        sum += new Vector3(t.M41, t.M42, t.M43);
                        n++;
                    }
                    return n > 0 ? sum / n : Vector3.Zero;
                }

                int split = 0;
                foreach (var ze in byZone.OrderByDescending(kv => kv.Value.texels))
                {
                    // A zone whose own demand exceeds one atlas cannot be kept whole. H60 gave it
                    // a fresh chunk and added it REGARDLESS of size, so the chunk filled to
                    // 15,743/16,384 and the donor clamp (15800 - groupTexels) left 57 texels: the
                    // room kept its geometry and lost 187 donors and 31% of its light, which is
                    // what blew cam3 up by 14.21. Split the zone itself instead, so every chunk
                    // still honours the budget and its donor headroom survives.
                    if (ze.Value.texels > budget)
                    {
                        split++;
                        var islands = ze.Value.islands.Select(i => (i.movers, i.texels, c: Centroid(i.movers))).ToList();

                        // Cut along the zone's longest axis so each piece stays a contiguous part
                        // of the room rather than a scatter of it.
                        Vector3 lo = new Vector3(float.MaxValue), hi = new Vector3(float.MinValue);
                        foreach (var i in islands) { lo = Vector3.Min(lo, i.c); hi = Vector3.Max(hi, i.c); }
                        Vector3 span = hi - lo;
                        Comparison<(List<int> movers, int texels, Vector3 c)> along =
                            span.X >= span.Y && span.X >= span.Z ? (a, b) => a.c.X.CompareTo(b.c.X)
                            : span.Y >= span.Z ? (a, b) => a.c.Y.CompareTo(b.c.Y)
                                               : (a, b) => a.c.Z.CompareTo(b.c.Z);
                        islands.Sort(along);

                        int parts = Math.Max(2, (int)Math.Ceiling(ze.Value.texels / (double)budget));
                        long perPart = (long)Math.Ceiling(ze.Value.texels / (double)parts);
                        chunks.Add(new HashSet<int>()); fill.Add(0);
                        int cur = chunks.Count - 1;
                        foreach (var isl in islands)
                        {
                            // Start a new piece once this one has taken its share, but never leave
                            // an island homeless: an island alone bigger than a part still lands.
                            if (fill[cur] > 0 && fill[cur] + isl.texels > perPart && fill[cur] + isl.texels > budget)
                            {
                                chunks.Add(new HashSet<int>()); fill.Add(0);
                                cur = chunks.Count - 1;
                            }
                            foreach (int m in isl.movers) chunks[cur].Add(m);
                            fill[cur] += isl.texels;
                        }
                        log?.Invoke("      zone " + ze.Key.ToString("X8") + " (" + ze.Value.texels +
                                    " texels) too big for one atlas -> split " + parts + " ways");
                        continue;
                    }

                    int target = -1;
                    for (int i = 0; i < fill.Count; i++)
                        if (fill[i] + ze.Value.texels <= budget) { target = i; break; }
                    if (target < 0)
                    {
                        chunks.Add(new HashSet<int>()); fill.Add(0);
                        target = chunks.Count - 1;
                    }
                    foreach (var island in ze.Value.islands)
                        foreach (int m in island.movers) chunks[target].Add(m);
                    fill[target] += ze.Value.texels;
                }
                log?.Invoke("    zone chunking: " + byZone.Count + " zones -> " + chunks.Count +
                            " chunks (" + split + " zones too big to keep whole), budget " + budget +
                            ", peak fill " + (fill.Count > 0 ? fill.Max() : 0));
            }
            else
            {
                // Worst-fit: each island goes to the emptiest chunk, so the fills stay level instead
                // of the first chunk taking every large island and fragmenting at 95%.
                for (int i = 0; i < chunkCount; i++) { chunks.Add(new HashSet<int>()); fill.Add(0); }
                foreach ((List<int> movers, int texels) in demand.OrderByDescending(d => d.texels))
                {
                    int best = 0;
                    for (int i = 1; i < fill.Count; i++) if (fill[i] < fill[best]) best = i;
                    foreach (int m in movers) chunks[best].Add(m);
                    fill[best] += texels;
                }
            }

            var kept = new List<HashSet<int>>();
            for (int i = 0; i < chunks.Count; i++) if (chunks[i].Count > 0) kept.Add(chunks[i]);
            log?.Invoke("    demand chunking: " + demand.Count + " islands, " + total + " texels wanted -> " +
                        kept.Count + " chunks at target " + settings.DeltaAtlasFillTarget.ToString("0.00") +
                        " (fills " + string.Join("/", fill.Select(f => (100.0 * f / AtlasTexels).ToString("0"))) + "%)");
            return kept;
        }

        /// <summary>
        /// Write the movers actually rasterised into one appended delta slice, so the slice a
        /// room's geometry landed in can be READ rather than inferred from its island binding.
        /// See <see cref="RadiosityBakeSettings.DeltaSliceMemberDir"/>.
        /// </summary>
        private static void DumpDeltaSliceMembers(
            Level level, List<RadiosityGeometry.Instance> bakeInstances, int sliceIndex,
            RadiosityBakeSettings settings, Action<string> log)
        {
            if (string.IsNullOrEmpty(settings.DeltaSliceMemberDir))
                return;
            try
            {
                System.IO.Directory.CreateDirectory(settings.DeltaSliceMemberDir);
                string path = System.IO.Path.Combine(settings.DeltaSliceMemberDir, "deltaslice_" + sliceIndex + ".csv");
                using (var w = new System.IO.StreamWriter(path))
                {
                    w.WriteLine("mover,x,y,z,instanceIndex,atlasX,atlasY,atlasW,atlasH,donorOnly");
                    for (int i = 0; i < bakeInstances.Count; i++)
                    {
                        RadiosityGeometry.Instance inst = bakeInstances[i];
                        foreach (int m in inst.Movers)
                        {
                            if (m < 0 || m >= level.Movers.Entries.Count) continue;
                            Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[m];
                            w.WriteLine(m + "," + mv.Transform.M41.ToString("0.0") + "," +
                                        mv.Transform.M42.ToString("0.0") + "," + mv.Transform.M43.ToString("0.0") + "," +
                                        i + "," + inst.AtlasX + "," + inst.AtlasY + "," +
                                        inst.AtlasWidth + "," + inst.AtlasHeight + "," +
                                        (inst.DonorOnly ? 1 : 0));
                        }
                    }
                }
                log?.Invoke("    delta slice members written: " + path);
            }
            catch (Exception e) { log?.Invoke("    delta slice member dump failed: " + e.Message); }
        }

        /// <summary>
        /// Retail's albedo convention on emitting surfaces, applied over the folded texel
        /// albedos. An emitting texel of a fixture this bake LIGHTS stores the light's colour
        /// (the scatter probe path has done this per-mover since the CEILING_HZDLAB measurement;
        /// this brings the texel/uniform path to parity, though only over the luminous texels
        /// themselves rather than the fixture's whole surface). An emissive surface the bake leaves
        /// dark stores near-black instead - its glow art is not a reflectance: CM3's WELDER_BLUE
        /// access panels read (5..11) flat in retail's own probe table where our diffuse sample
        /// read ~(4,52,128), and those panels alone were 560 of the dupe experiment's 1299
        /// blue-shifted probes.
        /// </summary>
        /// <remarks>
        /// The discriminator is the bake's own lit decision, not merely whether a prior exists.
        /// Under loose priors every fixture class resolves a colour, so keying on prior existence
        /// would paint the light's colour over exactly the dark panels this is meant to darken.
        /// See <see cref="RadiosityBakeSettings.EmissiveAlbedoConvention"/>.
        /// </remarks>
        private static void ApplyEmissiveAlbedoConvention(
            Level level, SurfaceTexel[] texels, RetailLightPriors lightPriors, RadiosityBakeSettings settings,
            Action<string> log)
        {
            if (!settings.EmissiveAlbedoConvention || level == null || lightPriors == null)
                return;
            var perMover = new Dictionary<int, Vector3?>();
            int stamped = 0, darkened = 0, siblings = 0;

            // A lit fixture ships as SEVERAL movers: the light slice sits on the emissive panel,
            // and the housing beside it is its own mover with no prior at all. Stamping only
            // prior-carrying movers therefore leaves every housing on its dark plastic texture -
            // measured on the duplicate as 82 probes of HAB_Plastic_Gloss_GreyDark storing 14
            // where retail stores 165, alongside the SPOT_SPECULAR and Strip_Light surfaces.
            // Retail stores the light's colour across the whole fixture (the ChallengeMap4 twin
            // walls, mover 2511 beside lit 2512), so a coincident unlit mover inherits it.
            var siblingColour = new Dictionary<int, Vector3>();
            if (settings.LightColourProbeAlbedoSiblings)
            {
                var lit = new List<(int mover, Vector3 pos, Vector3 colour)>();
                var unlit = new List<int>();
                var seen = new HashSet<int>();
                for (int i = 0; i < texels.Length; i++)
                {
                    if (!texels[i].Live) continue;
                    int m = texels[i].MoverIndex;
                    if (m < 0 || m >= level.Movers.Entries.Count || !seen.Add(m)) continue;
                    Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[m];
                    RetailLightPriors.Prior p = lightPriors.Lookup(mv.Resource);
                    var at = new Vector3(mv.Transform.M41, mv.Transform.M42, mv.Transform.M43);
                    if (p != null && !SuppressedByRetail(lightPriors, mv))
                        lit.Add((m, at, new Vector3(p.R / 255.0f, p.G / 255.0f, p.B / 255.0f)));
                    else
                        unlit.Add(m);
                }
                float r = Math.Max(0.01f, settings.LightColourSiblingRadius);
                float rSq = r * r;
                foreach (int m in unlit)
                {
                    Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[m];
                    var at = new Vector3(mv.Transform.M41, mv.Transform.M42, mv.Transform.M43);
                    float best = rSq; int bi = -1;
                    for (int k = 0; k < lit.Count; k++)
                    {
                        float d = Vector3.DistanceSquared(lit[k].pos, at);
                        if (d < best) { best = d; bi = k; }
                    }
                    if (bi >= 0) siblingColour[m] = lit[bi].colour;
                }
            }
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
                    if (colour == null && siblingColour.TryGetValue(m, out Vector3 sc))
                    { colour = sc; siblings++; }
                    perMover[m] = colour;
                }
                if (colour.HasValue)
                {
                    // The WHOLE fixture, not just the texels the shader flags emissive. Retail's
                    // probes on a lit fixture's dark plastic housing store the light's colour too:
                    // measured on the duplicate, 82 probes on HAB_Plastic_Gloss_GreyDark read 165
                    // in retail against our 14, and the same for the SPOT_SPECULAR and
                    // Strip_Light surfaces beside them - 599 probes where retail is above 120 and
                    // we stored under 25. Stamping only the emissive texels left every housing
                    // dark and is what made the luminous bucket average 0.62x of retail's.
                    texels[i].Albedo = colour.Value;
                    stamped++;
                }
                else if (texels[i].Emissive != Vector3.Zero)
                {
                    texels[i].Albedo = new Vector3(settings.EmissiveNoPriorAlbedo);
                    darkened++;
                }
            }
            if (stamped + darkened > 0)
                log?.Invoke("    emissive albedo convention: " + stamped + " texels stamped with prior light colour (" +
                            siblings + " movers via coincident-sibling inheritance), " +
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
        /// <summary>
        /// Hemisphere-sampled influence solve: cast cosine-weighted rays from every surface
        /// probe and link the clusters its rays actually HIT, weighted by the existing distance
        /// curve. Selection by what the probe SEES rather than by candidate proximity is the
        /// working decode of retail's room-scoped gather: it reproduces, by construction, the
        /// structures the proximity solve cannot - cluster-read concentration on each room's
        /// dominant surfaces (retail top-decile Wshare 40-41% vs our 28), shared read sets
        /// between a room's probes, bimodal empty-or-full link populations (rays that escape or
        /// land on clusterless geometry produce NO link - retail leaves 8-12% of probes empty
        /// where the proximity solve leaves ~1%), and genuinely starved boundary probes for the
        /// cross-slice fixup pass to serve (retail ships 48k CM3 fixups carrying 19% of its
        /// render; our proximity solve left fixups inert). The flat proximity gather measured as
        /// THE overshoot mechanism: same totals, mixed provenance - CM3 1.33/Torrens 1.41 on the
        /// interleaved 2-slice levels, dim on fragmented ones, colour casts wearing the import's
        /// provenance (MU-TH-UR's orange diluted to white, SCI_Hub's yellow-green excess with
        /// blue at exactly retail level).
        /// </summary>
        private static int SolveInfluencesHemisphere(
            RadiosityGeometry geometry, SurfaceTexel[] texels, int[] surfaceSlotForTexel,
            int[] inputProbeForTexel,
            RadiosityRuntime.RuntimeDataSlice slice,
            RadiosityBakeSettings settings, out List<(int emitter, int receiver, float weight)> transfers,
            out byte[] usedSlots, Action<string> log)
        {
            transfers = new List<(int, int, float)>();
            usedSlots = new byte[AtlasTexels];
            byte[] used = usedSlots;
            slice.SurfaceProbeInfluences = NewList<ColourRGBA8>(AtlasTexels * InfluencesPerProbe / 2);
            slice.SurfaceProbeWeights = NewList<Vector4u8>(AtlasTexels * InfluencesPerProbe / 4);

            var live = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && surfaceSlotForTexel[i] >= 0) live.Add(i);
            if (live.Count == 0)
                return 0;

            var clusters = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && inputProbeForTexel[i] >= 0) clusters.Add(i);
            if (clusters.Count == 0)
                return 0;

            float attrRadius = Math.Max(0.1f, settings.HemisphereAttributeRadius);
            var grid = new ProbeGrid(texels, clusters, attrRadius);
            int rays = Math.Max(16, settings.HemisphereRays);
            float maxDist = settings.MaxInfluenceDistance;

            // Pool augmentation: the pure-sight pool starves 40-66% of probes down to 10-12
            // links (retail: full 32-link sets at 27-30.5 links/probe on every level), and the
            // starved class is what broke the cross-level runs - SCI_Hub whiteout (all-sight
            // cliques concentrate absolute-gain weight inside mutually visible bright rooms
            // where retail spends ~34% of its links through walls), Solace dark-room lift and
            // CM7 overshoot (small in-room sets amplify a room's one bright fixture where
            // retail's full sets spend slots on soft/far candidates). Fill the pool to
            // HemispherePoolTarget with the default builder's soft-vis proximity candidates -
            // strongest curve byte first - before the quantile cut.
            int poolTarget = settings.HemispherePoolTarget;
            var candGrid = poolTarget > 0 ? new ProbeGrid(texels, clusters, maxDist) : null;

            int total = 0;
            long attributedAll = 0, escapedAll = 0, orphanAll = 0;
            long[] poolBandAll = new long[3], keptBandAll = new long[3];
            long augProbes = 0, augLinks = 0;
            int emptyProbes = 0;
            object statLock = new object();

            void Solve(int liveIndex)
            {
                int probeTexel = live[liveIndex];
                SurfaceTexel probe = texels[probeTexel];
                Vector3 origin = probe.RayOrigin;
                Vector3 n = probe.Normal;
                Vector3 t1 = Tangent(n), t2 = Vector3.Cross(n, t1);

                // hit accumulation per cluster: count, distance sum, receiver-cos sum
                var acc = new Dictionary<int, (int hits, float distSum, float cosSum)>();
                int attributed = 0, escaped = 0, orphan = 0;

                uint seed = (uint)(probeTexel * 747796405) ^ 0x9E3779B9u;
                for (int r = 0; r < rays; r++)
                {
                    seed = seed * 747796405u + 2891336453u;
                    float u1 = ((seed >> 8) & 0xFFFFFF) / 16777216.0f;
                    seed = seed * 747796405u + 2891336453u;
                    float u2 = ((seed >> 8) & 0xFFFFFF) / 16777216.0f;

                    // cosine-weighted hemisphere about the probe normal
                    float rad = (float)Math.Sqrt(u1);
                    double theta = 2.0 * Math.PI * u2;
                    float lx = rad * (float)Math.Cos(theta);
                    float ly = rad * (float)Math.Sin(theta);
                    float lz = (float)Math.Sqrt(Math.Max(0.0f, 1.0f - u1));
                    Vector3 dir = t1 * lx + t2 * ly + n * lz;

                    float t = geometry.TraceClosest(origin, dir, maxDist, out Vector3 hitPos);
                    if (t <= 0.0f) { escaped++; continue; }

                    // Nearest live cluster to the hit point that FACES the incoming ray. Without
                    // the facing test a hit snaps to the nearest cluster even when that cluster
                    // sits on the far side of a thin wall or floor (well within the attribute
                    // radius), which both links energy through geometry and steals reads from
                    // the surface actually hit - V1 shipped it and read 30-48% of Torrens'
                    // clusters never at all while retail reads 99%.
                    int best = -1;
                    float bestD2 = attrRadius * attrRadius;
                    foreach (int cluster in grid.Neighbours(hitPos))
                    {
                        if (Vector3.Dot(texels[cluster].Normal, dir) >= -0.1f)
                            continue;
                        float d2 = Vector3.DistanceSquared(texels[cluster].Position, hitPos);
                        if (d2 < bestD2) { bestD2 = d2; best = cluster; }
                    }
                    if (best < 0) { orphan++; continue; }

                    attributed++;
                    acc.TryGetValue(best, out (int hits, float distSum, float cosSum) a);
                    acc[best] = (a.hits + 1, a.distSum + t, a.cosSum + lz);
                }

                int augAdded = 0;
                if (candGrid != null && acc.Count < poolTarget)
                {
                    var extra = new List<(int cluster, float dist, float cosR, byte weightKey)>();
                    foreach (int otherTexel in candGrid.Neighbours(probe.Position))
                    {
                        if (otherTexel == probeTexel || acc.ContainsKey(otherTexel))
                            continue;
                        SurfaceTexel other = texels[otherTexel];
                        Vector3 delta = other.Position - origin;
                        float distanceSq = delta.LengthSquared();
                        if (distanceSq < 1e-6f || distanceSq > maxDist * maxDist)
                            continue;
                        float distance = (float)Math.Sqrt(distanceSq);
                        Vector3 direction = delta / distance;
                        float cosReceiver = Vector3.Dot(n, direction);
                        if (cosReceiver <= 0.02f)
                            continue;
                        float cosEmitter = Vector3.Dot(other.Normal, -direction);
                        if (cosEmitter <= 0.02f)
                            continue;
                        extra.Add((otherTexel, distance, cosReceiver,
                                   InfluenceWeight(distance, cosReceiver * cosEmitter, settings)));
                    }
                    extra.Sort((a, b) => a.weightKey != b.weightKey
                        ? b.weightKey.CompareTo(a.weightKey)
                        : a.dist.CompareTo(b.dist));
                    foreach ((int cluster, float dist, float cosR, byte _) in extra)
                    {
                        if (acc.Count >= poolTarget) break;
                        SurfaceTexel other = texels[cluster];
                        if (!VisibleSoft(geometry, origin, n, other.RayOrigin, other.Normal,
                                         settings, probeTexel, cluster))
                            continue;
                        acc[cluster] = (1, dist, cosR);
                        augAdded++;
                    }
                }

                int keep = 0;
                var poolBand = new long[3];
                var keptBand = new long[3];
                if (acc.Count > 0)
                {
                    // Rank candidates by the byte the link would carry, not by hit count. The
                    // wlaw regression (retail Torrens/CM3/Solace files) measured retail's
                    // per-link byte as a pure function of distance - residual 1.00 in every
                    // context x distance cell, brightness tilt 1.00, facing worth at most +7% -
                    // and its link sets keep a far tail hit ranking destroys: >4m is 25-30% of
                    // retail's links (10m+ 2.3-4.1%) where hemi3 kept 11%, because near
                    // clusters soak hundreds of rays and crowd single-hit far clusters out of
                    // the top 32. That locality was hemi3's whole 1.44x: sub-metre links at
                    // DOUBLE retail's share re-read the adjacent lit surfaces and pump the
                    // loop. A cluster is a candidate because ANY ray saw it; hit count is
                    // solid angle, and retail's bytes carry no solid-angle term at all.
                    var scored = new List<(int cluster, float meanDist, byte weight, int hits)>(acc.Count);
                    foreach (KeyValuePair<int, (int hits, float distSum, float cosSum)> kv in acc)
                    {
                        int cluster = kv.Key;
                        (int hits, float distSum, float cosSum) = kv.Value;
                        float meanDist = distSum / hits;
                        Vector3 toProbe = origin - texels[cluster].Position;
                        float len = toProbe.Length();
                        float cosEmitter = len > 1e-5f
                            ? Math.Max(0.02f, Vector3.Dot(texels[cluster].Normal, toProbe / len))
                            : 0.02f;
                        float cosProduct = (cosSum / hits) * cosEmitter;
                        scored.Add((cluster, meanDist, InfluenceWeight(meanDist, cosProduct, settings), hits));
                        poolBand[meanDist <= 2f ? 0 : meanDist <= 4f ? 1 : 2]++;
                    }
                    scored.Sort((a, b) => a.weight != b.weight
                        ? b.weight.CompareTo(a.weight)
                        : a.meanDist.CompareTo(b.meanDist));
                    // Composition-preserving cut, dominance-ranked within strata. hemi4's pool
                    // instrumentation measured the visible pool at 26-31/44/24-30% (<=2m/2-4m/
                    // >4m) - ALREADY retail's kept composition (~34/41/25) - while a straight
                    // top-32 cut collapses the far tail to 4-5% whether ranked by hits or by
                    // byte (both decay with distance), so the cut must be distance-neutral:
                    // partition the weight-sorted pool into 32 contiguous strata (hemi5 - this
                    // alone took Torrens to 1.022 and CM3 to 1.035). But taking one arbitrary
                    // member per stratum decorrelates picks between neighbouring probes and
                    // FLATTENS cluster reads: SCI_Hub clusterreads measured our median cluster
                    // taking 2x retail's incoming weight at top-decile Wshare 25-28% vs
                    // retail's 42-49%, and the flat read field is the whiteout - the absorptive
                    // barely-read tail retail leaves is gone. Within each stratum keep the
                    // MOST-HIT candidate: solid-angle dominance makes a room's probes converge
                    // on its dominant clusters (retail's shared read sets, readN median 8-12
                    // vs our flat 19-23) while the strata preserve the distance histogram.
                    // Within-stratum rank: raw hit count (hemi9). The area estimate hits*d^2
                    // was tried (hemi12) on the coreid read-from-far signature and FALSIFIED
                    // on all three corners - CM3 stayed broken (1.259), Solace worsened, and
                    // SCI_Hub re-whiteouted (4.83): the raw solid-angle concentration is
                    // specifically what damps the knee there, area concentration is not.
                    // Raw hits remain the best-known dominance; note the CM3 tension (quantile
                    // 1.035 vs any dominance ~1.25) is still unresolved - retail's one rule
                    // satisfies both and its key is still undecoded.
                    if (scored.Count > InfluencesPerProbe)
                    {
                        var thinned = new List<(int cluster, float meanDist, byte weight, int hits)>(InfluencesPerProbe);
                        for (int k = 0; k < InfluencesPerProbe; k++)
                        {
                            int lo = (int)((long)k * scored.Count / InfluencesPerProbe);
                            int hi = (int)((long)(k + 1) * scored.Count / InfluencesPerProbe);
                            int bestIdx = lo;
                            for (int i = lo + 1; i < hi; i++)
                                if (scored[i].hits > scored[bestIdx].hits) bestIdx = i;
                            thinned.Add(scored[bestIdx]);
                        }
                        scored = thinned;
                    }
                    keep = scored.Count;
                    for (int k = 0; k < keep; k++)
                    {
                        ClusterRef(scored[k].cluster, out byte cx, out byte cy);
                        int influenceSlot = surfaceSlotForTexel[probeTexel] * InfluencesPerProbe + k;
                        WriteInfluence(slice, influenceSlot, cx, cy, scored[k].weight);
                        keptBand[scored[k].meanDist <= 2f ? 0 : scored[k].meanDist <= 4f ? 1 : 2]++;
                    }
                }

                used[surfaceSlotForTexel[probeTexel]] = (byte)keep;
                lock (statLock)
                {
                    total += keep;
                    attributedAll += attributed; escapedAll += escaped; orphanAll += orphan;
                    if (keep == 0) emptyProbes++;
                    if (augAdded > 0) { augProbes++; augLinks += augAdded; }
                    for (int g = 0; g < 3; g++)
                    {
                        poolBandAll[g] += poolBand[g];
                        keptBandAll[g] += keptBand[g];
                    }
                }
            }

            if (settings.Parallel)
                Parallel.For(0, live.Count, Solve);
            else
                for (int i = 0; i < live.Count; i++) Solve(i);

            // Coverage fringe: retail reads 99% of its clusters (zeroRd 0.5-1.1% on Torrens)
            // while pure hemisphere selection left 30-48% never read - its reads pile onto the
            // visible dominant core. Retail's shape is that core PLUS a universal thin fringe.
            // Source-side pass, same invariant as the scatter builder's: every cluster no
            // probe's core links read is appended to the nearest visible same-facing probe's
            // free slot at the curve weight.
            {
                // Track reads directly from the written influence arrays.
                var readClusters = new HashSet<int>();
                foreach (int t in live)
                {
                    int slot = surfaceSlotForTexel[t];
                    for (int k = 0; k < used[slot]; k++)
                    {
                        int islot = slot * InfluencesPerProbe + k;
                        ColourRGBA8 idx = slice.SurfaceProbeInfluences[islot / 2];
                        int cx = (islot & 1) == 0 ? idx.R : idx.B;
                        int cy = (islot & 1) == 0 ? idx.G : idx.A;
                        readClusters.Add(cy * ProbeTexWidth + cx);
                    }
                }

                var receiverGrid = new ProbeGrid(texels, live, 2.0f);
                int fringed = 0, uncovered = 0;
                foreach (int cluster in clusters)
                {
                    ClusterRef(cluster, out byte ccx, out byte ccy);
                    if (readClusters.Contains(ccy * ProbeTexWidth + ccx))
                        continue;
                    // nearest receiver with a free slot that faces the cluster
                    int bestProbe = -1;
                    float bestD2 = 2.0f * 2.0f;
                    Vector3 cpos = texels[cluster].Position;
                    foreach (int rt in receiverGrid.Neighbours(cpos))
                    {
                        int slot = surfaceSlotForTexel[rt];
                        if (used[slot] >= InfluencesPerProbe) continue;
                        Vector3 toProbe = texels[rt].Position - cpos;
                        float d2 = toProbe.LengthSquared();
                        if (d2 >= bestD2 || d2 < 1e-6f) continue;
                        if (Vector3.Dot(texels[cluster].Normal, toProbe) <= 0.0f) continue;
                        bestD2 = d2; bestProbe = rt;
                    }
                    if (bestProbe < 0) { uncovered++; continue; }
                    int pslot = surfaceSlotForTexel[bestProbe];
                    float dist = (float)Math.Sqrt(bestD2);
                    Vector3 dirToProbe = (texels[bestProbe].Position - cpos) / Math.Max(1e-5f, dist);
                    float cosProduct = Math.Max(0.02f, Vector3.Dot(texels[cluster].Normal, dirToProbe)) *
                                       Math.Max(0.02f, Vector3.Dot(texels[bestProbe].Normal, -dirToProbe));
                    byte w = InfluenceWeight(dist, cosProduct, settings);
                    int islot = pslot * InfluencesPerProbe + used[pslot];
                    WriteInfluence(slice, islot, ccx, ccy, w);
                    used[pslot]++;
                    total++;
                    fringed++;
                }
                log?.Invoke("Hemisphere coverage fringe: " + fringed + " unread clusters homed, " +
                            uncovered + " uncoverable");
            }

            long castAll = (long)live.Count * rays;
            log?.Invoke("Hemisphere influence solve: " + live.Count + " probes x " + rays +
                        " rays; hits attributed " + (100.0 * attributedAll / Math.Max(1, castAll)).ToString("0.0") +
                        "% escaped " + (100.0 * escapedAll / Math.Max(1, castAll)).ToString("0.0") +
                        "% clusterless " + (100.0 * orphanAll / Math.Max(1, castAll)).ToString("0.0") +
                        "%; links/probe " + ((double)total / live.Count).ToString("0.0") +
                        "; empty probes " + (100.0 * emptyProbes / live.Count).ToString("0.0") + "%");
            long poolSum = Math.Max(1, poolBandAll[0] + poolBandAll[1] + poolBandAll[2]);
            long keptSum = Math.Max(1, keptBandAll[0] + keptBandAll[1] + keptBandAll[2]);
            log?.Invoke("Hemisphere candidate pool <=2m/2-4m/>4m: " +
                        string.Join("/", poolBandAll) + " (" +
                        (100.0 * poolBandAll[0] / poolSum).ToString("0") + "/" +
                        (100.0 * poolBandAll[1] / poolSum).ToString("0") + "/" +
                        (100.0 * poolBandAll[2] / poolSum).ToString("0") + "%), kept " +
                        string.Join("/", keptBandAll) + " (" +
                        (100.0 * keptBandAll[0] / keptSum).ToString("0") + "/" +
                        (100.0 * keptBandAll[1] / keptSum).ToString("0") + "/" +
                        (100.0 * keptBandAll[2] / keptSum).ToString("0") + "%)  [retail keeps ~34/41/25]");
            if (poolTarget > 0)
                log?.Invoke("Hemisphere pool augmentation (target " + poolTarget + "): " +
                            augProbes + " probes topped up with " + augLinks + " soft-vis links");
            return total;
        }

        private static int SolveInfluences(
            RadiosityGeometry geometry, SurfaceTexel[] texels, int[] surfaceSlotForTexel,
            int[] inputProbeForTexel,
            RadiosityRuntime.RuntimeDataSlice slice,
            RadiosityBakeSettings settings, float[] texelArea, float medianTexelArea, out List<(int emitter, int receiver, float weight)> transfers,
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

                // Correct for how much world area this probe's chosen clusters actually stand for.
                // The weight curve carries cos and distance but no patch area, so it assumes every
                // cluster covers MetresSquaredPerTexel; where a rect is denser than that, the 32
                // links it can hold span less surface and the probe is starved of light purely
                // because of how its island was packed. See InfluenceClusterAreaNormalisation.
                float areaGain = 1.0f;
                if (settings.InfluenceClusterAreaNormalisation > 0.0f && texelArea != null)
                {
                    double sum = 0; int n = 0;
                    for (int k = 0; k < keep; k++)
                    {
                        float a = texelArea[candidates[k].texel];
                        if (a > 0.0f) { sum += a; n++; }
                    }
                    if (n > 0)
                    {
                        double mean = sum / n;
                        double reference = settings.InfluenceClusterAreaReference > 0.0f
                            ? settings.InfluenceClusterAreaReference
                            : (medianTexelArea > 0.0f ? medianTexelArea : settings.MetresSquaredPerTexel);
                        double raw = reference / Math.Max(1e-4, mean);
                        areaGain = (float)Math.Pow(raw, settings.InfluenceClusterAreaNormalisation);
                        float hi = Math.Max(1.0f, settings.InfluenceClusterAreaClamp);
                        areaGain = Math.Max(1.0f / hi, Math.Min(hi, areaGain));
                    }
                }

                for (int k = 0; k < keep; k++)
                {
                    int otherTexel = candidates[k].texel;
                    ClusterRef(otherTexel, out byte cx, out byte cy);
                    byte weight = InfluenceWeight(candidates[k].distance, candidates[k].cosProduct, settings, areaGain);

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
            // Band stratification adds a far band out to ScatterMaxLinkDistance and an ultra-far
            // tier to twice that (retail ships links to 11.2m - farlinks measured its cam13-room
            // crossing links at p50 5.0 / max 11.2 where a 6m cap tops out at 5.8), so both the
            // grid and the candidate sweep must extend there.
            float candidateReach = settings.ScatterBandStratify
                ? (settings.ScatterUltraFarFraction > 0 ? ScatterMaxLinkDistance * 2 : ScatterMaxLinkDistance)
                : reach;
            var grid = new ProbeGrid(texels, clusterTexels, candidateReach);
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
                    if (d2 > candidateReach * candidateReach)
                        continue;
                    bool agrees = Vector3.Dot(texels[texel].Normal, normal) > 0.2f;
                    near.Add((texel, d2, agrees));
                }
                near.Sort((a, b) => a.d2.CompareTo(b.d2));

                float baseRadiusSq = radius * radius;
                var chosen = new List<(int cluster, float weight)>();

                if (settings.ScatterBandStratify)
                {
                    // Band-stratified selection: EVERY probe gets near, mid and far sources, not
                    // just the starved ones. Decoded from retail's link-length distribution
                    // (p50 0.75 / p90 2.45 / p99 6.1 - a 3.3x p90/p50 ratio no single ball can
                    // produce) after the ablation series proved scatter is the ONLY structure
                    // coupling surface lights into the standing field (emptying retail's list
                    // collapses its ungated render 3.008 -> 0.359) and the two-hop census put
                    // our all-local links at ~0.6x retail's delivered gather mass per light: a
                    // ball's links all land in clusters the same nearby probes gather, while
                    // retail's tail spreads each probe's radiance into distinct gather sets.
                    // Quotas 4/2/1 at cap 7 reproduce all three retail percentiles at once.
                    // 5/1/1, with the mid and far links picked DEEP in their bands: retail's
                    // percentiles decompose exactly as this mixture - with 7 links, p90 IS the
                    // 6th (the mid link, 2.45m = deep in the 1.25-3m band) and p99 IS the far
                    // link (~6m). The first cut (4/2/1, nearest-in-band) measured +16% brightness
                    // but dragged p50 to 1.05 and stopped the tail at 3.8 - right direction,
                    // wrong mixture.
                    float midSq = reach * reach;
                    float farSq = ScatterMaxLinkDistance * ScatterMaxLinkDistance;
                    int farQ = 1, midQ = 1;
                    int nearQ = Math.Max(1, solveCap - midQ - farQ);

                    // NEAR: the probe's own surface neighbourhood - same-facing, nearest-first.
                    foreach ((int cluster, float d2, bool agrees) in near)
                    {
                        if (chosen.Count >= nearQ)
                            break;
                        if (agrees && d2 <= baseRadiusSq)
                            chosen.Add((cluster, 1.0f / (1.0f + d2)));
                    }
                    // Normal-disjoint neighbourhood (a probe on a lone strut): take what is visible.
                    if (settings.ScatterStarvationRescue && chosen.Count < Math.Min(3, nearQ))
                    {
                        foreach ((int cluster, float d2, bool agrees) in near)
                        {
                            if (chosen.Count >= nearQ)
                                break;
                            if (agrees || d2 > baseRadiusSq || chosen.Exists(c => c.cluster == cluster))
                                continue;
                            if (geometry.Visible(origin + normal * settings.ProbeSurfaceOffset,
                                                 texels[cluster].Position + texels[cluster].Normal * settings.ProbeSurfaceOffset,
                                                 settings.RayEpsilon))
                                chosen.Add((cluster, 1.0f / (1.0f + d2)));
                        }
                    }

                    // MID and FAR: the room, not the surface. A far source FACES the probe (a
                    // floor probe receiving from a wall shares no normal), and must be visible -
                    // a link carries radiance with no runtime occlusion, so an unchecked one
                    // lights the neighbouring room through the wall.
                    // Pick each band's link nearest the band's MIDDLE. Retail's link-length shape
                    // is the same on every level measured (p50 0.73-0.78, p90 2.45-2.74, p99
                    // 5-6.1 on Solace, CM3 and CM9 alike) and its p90 sits mid-band, not at a
                    // ceiling. The furthest-first variant pinned links at the band tops (p90 5.6)
                    // - tolerated on Solace/CM3, but it over-coupled ChallengeMap9 to 1.25x
                    // retail, which is the falsifier: the shape must be matched, not exceeded.
                    // Returns how many links it added: the FAR band fires on a dithered MINORITY
                    // of probes (see below) plus any probe whose mid band came up empty. Giving
                    // EVERY probe a far link put the aggregate p90 at 4.5 and left CM9 1.17x hot;
                    // giving NONE (fallback-only) capped every link at ~2.8m and fragmented the
                    // graph at doorways.
                    // targetSq >= 0 centres the outward walk on the candidate nearest that
                    // length instead of the band's index middle (the long-tail pick below).
                    int Band(float loSq, float hiSq, int quota, float targetSq = -1f)
                    {
                        int lo = -1, hi = -1;   // candidate index range inside the band
                        for (int i = 0; i < near.Count; i++)
                        {
                            if (near[i].d2 <= loSq) continue;
                            if (near[i].d2 > hiSq) break;
                            if (lo < 0) lo = i;
                            hi = i;
                        }
                        if (lo < 0)
                            return 0;

                        int added = 0;
                        int target = Math.Min(solveCap, chosen.Count + quota);
                        int mid = (lo + hi) / 2;
                        if (targetSq >= 0f)
                        {
                            mid = lo;
                            for (int i = lo + 1; i <= hi; i++)
                                if (Math.Abs(near[i].d2 - targetSq) < Math.Abs(near[mid].d2 - targetSq))
                                    mid = i;
                        }
                        for (int step = 0; step <= hi - lo && chosen.Count < target; step++)
                        {
                            // middle outward: mid, mid+1, mid-1, mid+2, ...
                            int i = mid + (step % 2 == 1 ? (step + 1) / 2 : -(step / 2));
                            if (i < lo || i > hi)
                                continue;
                            (int cluster, float d2, bool _) = near[i];
                            if (chosen.Exists(c => c.cluster == cluster))
                                continue;
                            Vector3 toProbe = origin - texels[cluster].Position;
                            if (Vector3.Dot(texels[cluster].Normal, toProbe) <= 0.0f)
                                continue;
                            if (!geometry.Visible(origin + normal * settings.ProbeSurfaceOffset,
                                                  texels[cluster].Position + texels[cluster].Normal * settings.ProbeSurfaceOffset,
                                                  settings.RayEpsilon))
                                continue;
                            chosen.Add((cluster, 1.0f / (1.0f + d2)));
                            added++;
                        }
                        return added;
                    }
                    int midAdded = Band(baseRadiusSq, midSq, midQ);
                    // FAR is a MINORITY QUOTA, not just a fallback. Retail puts a >3m link on
                    // 25-31% of destination probes (measured on Solace and CM3 alike; the long
                    // links' length p50 is 3.9m = the 3-6m band's middle, so the middle-outward
                    // pick reproduces their shape). Those minority links are what stitch the
                    // scatter graph into ONE component: with far as a pure fallback every link
                    // capped at ~2.8m and the graph fragmented at doorways - BFS reach from a
                    // room saturated by hop 4 with 0.39x retail's reachable light (reachbox,
                    // Solace cam13's room, the 0.16x cold-room transport case) while retail's
                    // kept growing. A deterministic per-probe dither picks the minority so the
                    // aggregate p90 stays mid-band (the ChallengeMap9 overshoot lesson);
                    // mid-starved probes keep the fallback regardless of the dither.
                    bool farFires = ((uint)p * 2654435761u) % 100u <
                                    (uint)Math.Round(settings.ScatterFarBandFraction * 100.0f);
                    // ULTRA-FAR: the thin tail beyond ScatterMaxLinkDistance. Retail's cross-room
                    // spill links measure p50 5.0 / max 11.2m into a cold room where our 6m cap
                    // tops out at 5.8 and carries HALF retail's crossing count (76 vs 142) - the
                    // 6-12m minority is what actually imports light through doorways. ~1% of
                    // retail's links exceed 6m (~6% of dests at ~7 links each). A different hash
                    // constant so this minority is independent of the far dither.
                    bool ultraFires = settings.ScatterUltraFarFraction > 0 &&
                                      ((uint)p * 2246822519u) % 100u <
                                      (uint)Math.Round(settings.ScatterUltraFarFraction * 100.0f);
                    // ONE decaying tail, not two banded picks. fartail across 12 retail slices
                    // (Solace/CM3/Torrens/SCI_Hub, 2026-08-28): long-link length p10 is pinned
                    // at 3.1m on EVERY slice, p50 3.8-4.2, p90 5.9-7.8 - a single distribution
                    // decaying from the 3m reach floor with no structure at the 6m "band edge"
                    // (several slices' p90 EXCEEDS 6m). It fits length = floor + Exp(mean 1.4m):
                    // p10 3.15 / p50 3.97 / p90 6.3. The middle-outward two-band pick instead
                    // piled links at 4.5m and 9m (our p10 4.0-4.3, p50 4.7-4.8, p90 8.7-10.4,
                    // >4m rate DOUBLE retail's on every level) - the same excess-coupling class
                    // that rendered CM9 1.25x hot. Per-probe deterministic hash draws the
                    // target length; the outward walk from the nearest candidate keeps the
                    // facing + visibility discipline.
                    if (settings.ScatterLongLinkMean > 0.0f)
                    {
                        if (farFires || ultraFires || midAdded == 0)
                        {
                            uint th = (uint)p * 3266489917u;
                            float u = ((th >> 8) % 10000u + 0.5f) / 10000.0f;
                            float tail = reach + settings.ScatterLongLinkMean * (0.0f - (float)Math.Log(u));
                            float tailCap = ScatterMaxLinkDistance * 2.0f;
                            if (tail > tailCap) tail = tailCap;
                            Band(midSq, farSq * 4.0f, farQ, tail * tail);
                        }
                    }
                    else if (ultraFires)
                        Band(farSq, farSq * 4.0f, 1);
                    else if (farFires || midAdded == 0)
                        Band(midSq, farSq, farQ);

                    // Roll-down: a closet probe with no visible mid/far cluster spends the unused
                    // quota nearer in, so degree stays ~solveCap and no probe is left short.
                    // RETAIL DOES NOT DO THIS (scatstat/radvol): its per-dest degree SPREADS with
                    // local availability - p10 3 / p50 6 / p90 8, mean 6.1 - where the padding
                    // holds ours flat at 7 (mean 6.9, +14-28% total entries over retail on CM3).
                    // ScatterRollDownFill=false drops the padding so degree follows availability.
                    if (settings.ScatterRollDownFill && chosen.Count < solveCap)
                    {
                        foreach ((int cluster, float d2, bool agrees) in near)
                        {
                            if (chosen.Count >= solveCap)
                                break;
                            if (chosen.Exists(c => c.cluster == cluster))
                                continue;
                            if (agrees && d2 <= baseRadiusSq)
                                chosen.Add((cluster, 1.0f / (1.0f + d2)));
                            else if (d2 <= midSq &&
                                     Vector3.Dot(texels[cluster].Normal, origin - texels[cluster].Position) > 0.0f &&
                                     geometry.Visible(origin + normal * settings.ProbeSurfaceOffset,
                                                      texels[cluster].Position + texels[cluster].Normal * settings.ProbeSurfaceOffset,
                                                      settings.RayEpsilon))
                                chosen.Add((cluster, 1.0f / (1.0f + d2)));
                        }
                    }
                }
                else
                {
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
                }

                // Last resort: an unfed probe is a hole in the field, so take the nearest clusters
                // on any terms rather than leave it dark. Retail leaves 0.2% of its probes unfed;
                // a plain radius ball left 5% of ours.
                if (settings.ScatterStarvationRescue && chosen.Count == 0)
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

        /// <summary>Rewrite only the weight byte of an influence slot, keeping its cluster index.</summary>
        private static void WriteInfluenceWeight(RadiosityRuntime.RuntimeDataSlice slice, int influenceSlot, byte weight)
        {
            Vector4u8 weights = slice.SurfaceProbeWeights[influenceSlot / 4];
            switch (influenceSlot & 3)
            {
                case 0: weights.X = weight; break;
                case 1: weights.Y = weight; break;
                case 2: weights.Z = weight; break;
                default: weights.W = weight; break;
            }
            slice.SurfaceProbeWeights[influenceSlot / 4] = weights;
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
            RadiosityGeometry geometry, SurfaceTexel[] texels, int[] inputProbeForTexel, int[] surfaceSlotForTexel,
            RadiosityBakeSettings settings, float consumerMinY, float consumerMaxY, out List<byte[]> visFaceGrids)
        {
            visFaceGrids = new List<byte[]>();
            var hash = new RadiosityRuntime.VolumeProbeHash { NumSubdivsPerLevel = settings.VolumeProbeSubdivsPerLevel };

            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            var sources = new List<int>();
            int probeless = 0;
            for (int i = 0; i < AtlasTexels; i++)
            {
                if (!texels[i].Live || inputProbeForTexel[i] < 0)
                    continue;
                // A volume item is resolved by the ENGINE as atlas texel -> mangle map ->
                // surface probe slot, so a cluster-only texel (donor shell, gutter fill - live
                // in the atlas but with no surface probe, surfaceSlotForTexel -1) resolves to a
                // DEAD slot and any prop blending that item reads black. This was 27.8% of our
                // shipped items against retail's 1.9% (the tgamask 0.56x dynamic-prop deficit),
                // and RADBAKE_FILL_GUTTERS multiplies exactly this texel class.
                if (surfaceSlotForTexel != null && surfaceSlotForTexel[i] < 0)
                {
                    probeless++;
                    continue;
                }
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

            // Vertical consumer-space extension (see the call site): the box grows to the slice's
            // instance geometry range so elevated dynamic movers stay in bounds. X/Z stay
            // probe-driven - the measured retail boxes match our probe footprints there.
            if (consumerMinY < float.MaxValue && consumerMinY < min.Y) min.Y = consumerMinY;
            if (consumerMaxY > float.MinValue && consumerMaxY > max.Y) max.Y = consumerMaxY;

            float cell = Math.Max(0.25f, settings.VolumeProbeCellSize);

            // SHARED LATTICE: snap the slice's box out to the global grid anchored at the world
            // origin, so every slice's cell boundaries coincide and their origins differ by a
            // whole number of cells.
            //
            // Retail's slices line up this way. Ours derives each hash from that slice's OWN
            // bounding box, so neighbouring slices cut space on unrelated grids and a dynamic
            // object crossing a boundary jumps between two differently-phased fields - which is
            // what makes our volume cell borders visibly messy against retail's.
            //
            // Snapping to a world-anchored grid costs at most one extra cell per axis and needs
            // no level-wide pre-pass: alignment is inherent once every box starts on a multiple
            // of the cell size.
            if (settings.SharedVolumeLattice)
            {
                min = new Vector3((float)Math.Floor(min.X / cell) * cell,
                                  (float)Math.Floor(min.Y / cell) * cell,
                                  (float)Math.Floor(min.Z / cell) * cell);
                max = new Vector3((float)Math.Ceiling(max.X / cell) * cell,
                                  (float)Math.Ceiling(max.Y / cell) * cell,
                                  (float)Math.Ceiling(max.Z / cell) * cell);
            }

            hash.AabbMin = min;
            hash.AabbMax = max;

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

            // Mid-air fill. A cell no probe falls inside would stay a (255,255) no-probe item,
            // but the engine's 8-cell blend does NOT renormalise around missing items - a
            // converted mover whose bounds centre floats between the floor and ceiling probe
            // layers reads a mostly-black blend. That is what turned F6's ceilings and wall
            // panels black: 73% of the blend weight at one traced centre sat on no-probe cells
            // while lit probes waited 0.8 m away, and finer cells (1 m against retail's 2 m)
            // manufacture exactly such a layer in every room. Retail's own items resolve probes
            // up to ~2.6 m from their cell, so empty cells here borrow the nearest probe within
            // reach, preferring one visible from the cell centre.
            float reach = settings.VolumeProbeFillReach;
            if (reach > 0)
            {
                int ring = Math.Max(1, (int)Math.Ceiling(reach / cell));
                float reachSq = reach * reach;

                // Dilate outward from occupied cells so far-out empties (a stretched hash can
                // cover most of the level) are never even visited.
                var fillTargets = new HashSet<int>();
                foreach (int key in cellCandidates.Keys)
                {
                    int cz2 = key / (gx * gy), cy2 = (key / gx) % gy, cx2 = key % gx;
                    for (int dz2 = -ring; dz2 <= ring; dz2++)
                        for (int dy2 = -ring; dy2 <= ring; dy2++)
                            for (int dx2 = -ring; dx2 <= ring; dx2++)
                            {
                                int nx = cx2 + dx2, ny = cy2 + dy2, nz = cz2 + dz2;
                                if (nx < 0 || ny < 0 || nz < 0 || nx >= gx || ny >= gy || nz >= gz)
                                    continue;
                                int nk = (nz * gy + ny) * gx + nx;
                                if (probeForCell[nk] < 0)
                                    fillTargets.Add(nk);
                            }
                }

                bool OccupiedAt(int x, int y, int z) =>
                    x >= 0 && y >= 0 && z >= 0 && x < gx && y < gy && z < gz &&
                    cellCandidates.ContainsKey((z * gy + y) * gx + x);

                var scratch = new List<int>();
                foreach (int key in fillTargets)
                {
                    int cz2 = key / (gx * gy), cy2 = (key / gx) % gy, cx2 = key % gx;

                    // Interior air only: surfaces on OPPOSITE sides along some axis within the
                    // ring (the floor-below-ceiling-above layer where mover bounds centres sit).
                    // Filling the outward shell too - outside walls, above ceilings - was the F7
                    // item explosion, and nothing ever samples out there.
                    bool interior = false;
                    for (int axis = 0; axis < 3 && !interior; axis++)
                    {
                        bool neg = false, pos = false;
                        for (int step = 1; step <= ring && !(neg && pos); step++)
                        {
                            int sx = axis == 0 ? step : 0, sy = axis == 1 ? step : 0, sz = axis == 2 ? step : 0;
                            neg = neg || OccupiedAt(cx2 - sx, cy2 - sy, cz2 - sz);
                            pos = pos || OccupiedAt(cx2 + sx, cy2 + sy, cz2 + sz);
                        }
                        interior = neg && pos;
                    }
                    if (!interior)
                        continue;

                    Vector3 centre = min + new Vector3((cx2 + 0.5f) * cell, (cy2 + 0.5f) * cell, (cz2 + 0.5f) * cell);

                    scratch.Clear();
                    for (int dz2 = -ring; dz2 <= ring; dz2++)
                        for (int dy2 = -ring; dy2 <= ring; dy2++)
                            for (int dx2 = -ring; dx2 <= ring; dx2++)
                            {
                                int nx = cx2 + dx2, ny = cy2 + dy2, nz = cz2 + dz2;
                                if (nx < 0 || ny < 0 || nz < 0 || nx >= gx || ny >= gy || nz >= gz)
                                    continue;
                                if (cellCandidates.TryGetValue((nz * gy + ny) * gx + nx, out List<int> near))
                                    foreach (int texel in near)
                                        if (Vector3.DistanceSquared(centre, texels[texel].Position) <= reachSq)
                                            scratch.Add(texel);
                            }
                    if (scratch.Count == 0)
                        continue;

                    scratch.Sort((a, b) =>
                        Vector3.DistanceSquared(centre, texels[a].Position)
                            .CompareTo(Vector3.DistanceSquared(centre, texels[b].Position)));

                    int chosen = -1;
                    int tried = 0;
                    foreach (int texel in scratch)
                    {
                        if (tried++ >= 12) break;
                        if (geometry.Visible(centre,
                                             texels[texel].Position + texels[texel].Normal * settings.ProbeSurfaceOffset,
                                             settings.RayEpsilon))
                        {
                            chosen = texel;
                            break;
                        }
                    }
                    probeForCell[key] = chosen >= 0 ? chosen : scratch[0];
                }
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

            // Retail's invariant, measured on every level and slice: a REFERENCED vis grid is
            // never fully occluded (openness p10 15-22, minimum never 0). Ours shipped all-zero
            // grids wherever a cell centre sat embedded in geometry (every ray occluded), and
            // the engine's response to an all-black visibility grid is catastrophic - SCI_Hub's
            // island -1 exterior sampled such cells and rendered a full-frame WHITEOUT (cam4/1/
            // 7/12 at uniform 255, +1.1x on the level aggregate). Two-step repair: a fully
            // embedded item (all six faces zero) re-traces from its elected surface texel
            // nudged along the normal - open air by construction; any face still all-zero is
            // floored to one visible sub-sample, the smallest value retail's encoder emits.
            byte floorVis = EncodeVisibility(1, VisSubSamples);
            int retraced = 0, floored = 0;
            for (int i = 0; i < itemCells.Count; i++)
            {
                if (!hasProbe[i]) continue;
                bool allZero = true;
                for (int f = 0; f < 6 && allZero; f++)
                    foreach (byte b in grids[i * 6 + f]) if (b != 0) { allZero = false; break; }
                if (allZero)
                {
                    int texel = probeForCell[itemCells[i]];
                    Vector3 fallback = texels[texel].Position + texels[texel].Normal * Math.Max(0.05f, settings.ProbeSurfaceOffset);
                    for (int f = 0; f < 6; f++)
                        grids[i * 6 + f] = TraceVisFace(geometry, fallback, f, settings);
                    retraced++;
                }
                for (int f = 0; f < 6; f++)
                {
                    byte[] g = grids[i * 6 + f];
                    bool zero = true;
                    foreach (byte b in g) if (b != 0) { zero = false; break; }
                    if (zero)
                    {
                        for (int c2 = 0; c2 < g.Length; c2++) g[c2] = floorVis;
                        floored++;
                    }
                }
            }

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

        /// <summary>
        /// Replace every slice's derived surface-light table with retail's shipped table: each
        /// light re-addressed to our nearest live input probe by world position, donor groups
        /// kept contiguous, bindings carried both as the raw positional EntityInstanceIndex
        /// (valid because instancing restores retail's row order with purged rows padded) and as
        /// the resolved Resource object so Save re-derives the index.
        ///
        /// Why: the derived table reaches 0.88x retail's energy but distributes weight across
        /// ENTITIES differently - flat per-emitter sample counts over-weight always-on fixtures
        /// where retail's weight concentrates on big scripted (runtime-gated-off) ones - and the
        /// gate turns that into SCI_Hub's 2.5x over-brightness and CM9's black cam13 ceiling.
        /// Retail's own table through our transport renders at the transport ceiling on every
        /// level measured, so on retail levels it is strictly better than deriving. Derived
        /// lights remain the only path for levels without a retail runtime (added content).
        /// </summary>
        private static void ImportRetailLightTable(Level level, List<RadiosityRuntime.RuntimeDataSlice> retailSlices,
            SliceBake[] sliceData, Action<string> log)
        {
            if (retailSlices == null || retailSlices.Count == 0)
            {
                log?.Invoke("Radiosity VERBATIM light table: no retail runtime available - derived lights kept");
                return;
            }

            // Spatial grid over our live input probes, all slices.
            const float cell = 2.0f;
            var grid = new Dictionary<(int, int, int), List<int>>();
            var pts = new List<Vector3>();
            var src = new List<(int s, int t)>();
            for (int s = 0; s < sliceData.Length; s++)
            {
                RadiosityRuntime.RuntimeDataSlice sl = sliceData[s]?.Slice;
                if (sl == null) continue;
                for (int t = 0; t < sl.InputProbePositions.Count; t++)
                {
                    if (sl.InputProbePositions[t].W == 0) continue;
                    Vector4u16 q = sl.InputProbePositions[t];
                    var p = new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z));
                    (int, int, int) key = ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell), (int)Math.Floor(p.Z / cell));
                    if (!grid.TryGetValue(key, out List<int> cellList)) grid[key] = cellList = new List<int>();
                    cellList.Add(pts.Count);
                    pts.Add(p);
                    src.Add((s, t));
                }
            }
            if (pts.Count == 0)
            {
                log?.Invoke("Radiosity VERBATIM light table: no live input probes - derived lights kept");
                return;
            }

            bool Nearest(Vector3 p, out int bs, out int bt)
            {
                bs = bt = -1;
                float bd = float.MaxValue;
                int cx = (int)Math.Floor(p.X / cell), cy = (int)Math.Floor(p.Y / cell), cz = (int)Math.Floor(p.Z / cell);
                int firstHit = -1;
                for (int ring = 0; ring <= 4; ring++)
                {
                    if (firstHit >= 0 && ring > firstHit + 1) break;
                    for (int dx = -ring; dx <= ring; dx++)
                        for (int dy = -ring; dy <= ring; dy++)
                            for (int dz = -ring; dz <= ring; dz++)
                            {
                                if (Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz))) != ring) continue;
                                if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int> l)) continue;
                                foreach (int i in l)
                                {
                                    float d = Vector3.DistanceSquared(p, pts[i]);
                                    if (d < bd) { bd = d; bs = src[i].s; bt = src[i].t; }
                                }
                            }
                    if (bs >= 0 && firstHit < 0) firstHit = ring;
                }
                return bs >= 0;
            }

            var perSlice = new List<List<(int gid, int rawIdx, Resources.Resource ent, ushort sibling,
                                          RadiosityRuntime.RuntimeSurfaceLights.Light light)>>();
            for (int i = 0; i < sliceData.Length; i++)
                perSlice.Add(new List<(int, int, Resources.Resource, ushort,
                                       RadiosityRuntime.RuntimeSurfaceLights.Light)>());

            int total = 0, placed = 0, dropped = 0, gidNext = 0, resolvedEnts = 0;

            void Place(RadiosityRuntime.RuntimeDataSlice ds, RadiosityRuntime.RuntimeSurfaceLights.Light L,
                       int gid, int rawIdx, Resources.Resource ent, ushort sibling)
            {
                total++;
                int t = L.V * ProbeTexWidth + L.U;
                if (t < 0 || t >= ds.InputProbePositions.Count || ds.InputProbePositions[t].W == 0) { dropped++; return; }
                Vector4u16 q = ds.InputProbePositions[t];
                var p = new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z));
                if (!Nearest(p, out int bs, out int bt)) { dropped++; return; }
                RadiosityRuntime.RuntimeSurfaceLights.Light moved = L;
                moved.U = (byte)(bt % ProbeTexWidth);
                moved.V = (byte)(bt / ProbeTexWidth);
                perSlice[bs].Add((gid, rawIdx, ent, sibling, moved));
                placed++;
            }

            // Donor identity per gid, for the sibling remap below. SiblingIndex is an index into
            // the donor SLICE's own group list; carrying it verbatim across the reorganised
            // output CRASHES the engine on load wherever the referenced pair no longer sits at
            // that index in the same slice - CM7 and SCI_Hub died on it (the sx1/sx2/sx3 field
            // bisect: bindings-nulled loads, siblings-zeroed loads, Live-emptied still crashes).
            var gidMeta = new Dictionary<int, (int dSlice, int dLocal, ushort sib)>();

            int dSliceIdx = -1;
            foreach (RadiosityRuntime.RuntimeDataSlice ds in retailSlices)
            {
                dSliceIdx++;
                List<RadiosityRuntime.RuntimeSurfaceLights.Light> lights = ds.SurfaceLights?.Lights;
                List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice> groups = ds.SurfaceLights?.LightSlices;
                if (lights == null || groups == null) continue;
                var covered = new bool[lights.Count];
                for (int gLocal = 0; gLocal < groups.Count; gLocal++)
                {
                    RadiosityRuntime.RuntimeSurfaceLights.LightSlice grp = groups[gLocal];
                    int gid = gidNext++;
                    gidMeta[gid] = (dSliceIdx, gLocal, grp.SiblingIndex);
                    Resources.Resource ent = grp.EntityInstanceIndex >= 0
                        ? level.Resources?.GetAtWriteIndex(grp.EntityInstanceIndex) : null;
                    if (ent != null) resolvedEnts++;
                    for (int k = 0; k < grp.NumItems; k++)
                    {
                        int li = (int)grp.FirstItem + k;
                        if (li < 0 || li >= lights.Count) continue;
                        covered[li] = true;
                        Place(ds, lights[li], gid, grp.EntityInstanceIndex, ent, 0);
                    }
                }
                for (int li = 0; li < lights.Count; li++)
                    if (!covered[li]) Place(ds, lights[li], -1, -1, null, 0);
            }

            // Emit, recording where each donor group landed (a group split across output slices
            // records its LARGEST output as canonical - the sibling either points there or is
            // dropped).
            var canonical = new Dictionary<(int dSlice, int dLocal), (int s, int idx, int n)>();
            var outGidsPerSlice = new List<int>[sliceData.Length];
            for (int s = 0; s < sliceData.Length; s++)
            {
                RadiosityRuntime.RuntimeDataSlice sl = sliceData[s]?.Slice;
                if (sl == null) continue;
                var outLights = new List<RadiosityRuntime.RuntimeSurfaceLights.Light>();
                var outGroups = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>();
                var outEnts = new List<Resources.Resource>();
                var outGids = new List<int>();
                foreach (var run in perSlice[s].Where(e => e.gid >= 0).GroupBy(e => e.gid))
                {
                    var f = run.First();
                    int n = run.Count();
                    (int dSlice, int dLocal, ushort _) = gidMeta[f.gid];
                    if (!canonical.TryGetValue((dSlice, dLocal), out (int s, int idx, int n) prev) || n > prev.n)
                        canonical[(dSlice, dLocal)] = (s, outGroups.Count, n);
                    outGroups.Add(new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                    {
                        FirstItem = (uint)outLights.Count,
                        NumItems = (ushort)n,
                        EntityInstanceIndex = f.rawIdx,
                        SiblingIndex = 0
                    });
                    // null is allowed: Save skips null entities and the raw index carries through
                    outEnts.Add(f.ent);
                    outGids.Add(f.gid);
                    foreach (var e in run) outLights.Add(e.light);
                }
                foreach (var e in perSlice[s].Where(e => e.gid < 0)) outLights.Add(e.light);
                sl.SurfaceLights.Lights = outLights;
                sl.SurfaceLights.LightSlices = outGroups;
                sl.SurfaceLights.LightSliceEntities = outEnts;
                outGidsPerSlice[s] = outGids;
            }

            // Sibling fix-up: rewrite each donor sibling reference to the pair's canonical output
            // index IF it landed in the same output slice; otherwise it stays 0 (SiblingIndex 0 is
            // the no-sibling sentinel - group 0 is never a target in any retail file measured).
            int sibKept = 0, sibDropped = 0;
            for (int s = 0; s < sliceData.Length; s++)
            {
                RadiosityRuntime.RuntimeDataSlice sl = sliceData[s]?.Slice;
                if (sl == null || outGidsPerSlice[s] == null) continue;
                List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice> outGroups = sl.SurfaceLights.LightSlices;
                for (int i = 0; i < outGroups.Count; i++)
                {
                    (int dSlice, int _, ushort sib) = gidMeta[outGidsPerSlice[s][i]];
                    if (sib == 0) continue;
                    if (canonical.TryGetValue((dSlice, sib), out (int s, int idx, int n) tgt) && tgt.s == s && tgt.idx != i)
                    {
                        RadiosityRuntime.RuntimeSurfaceLights.LightSlice g = outGroups[i];
                        g.SiblingIndex = (ushort)tgt.idx;
                        outGroups[i] = g;
                        sibKept++;
                    }
                    else
                        sibDropped++;
                }
                sl.LiveSurfaceLights = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>(outGroups);
                sl.LiveSurfaceLightEntities = new List<Resources.Resource>(sl.SurfaceLights.LightSliceEntities);
            }

            log?.Invoke("Radiosity VERBATIM light table: " + placed + " of " + total +
                        " retail lights re-addressed (" + dropped + " dropped: dead donor probe or nothing within reach), " +
                        resolvedEnts + " of " + gidNext + " groups entity-resolved, siblings " +
                        sibKept + " remapped / " + sibDropped + " dropped");
        }

        private static RadiosityRuntime.RuntimeSurfaceLights BuildSurfaceLights(
            Level level, RadiosityGeometry geometry, List<RadiosityGeometry.Instance> instances,
            SurfaceTexel[] texels, int[] inputProbeForTexel, RadiosityBakeSettings settings,
            Dictionary<int, float> emissiveAreas, RetailLightPriors lightPriors, Action<string> log)
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

            BuildEmissiveSurfaceLights(level, geometry, texels, inputProbeForTexel, grid, lights, settings, emissiveAreas, lightPriors, log);
            BuildLostEmitterLights(level, geometry, instances, texels, inputProbeForTexel, grid, lights, settings, lightPriors, log);
            if (settings.EmitLightEntitySamples)
                BuildMoverLights(level, texels, inputProbeForTexel, grid, settings, lights);
            TrimLightSlices(lights, log);
            return lights;
        }

        /// <summary>
        /// Hold the slice's light table to the engine's limit, keeping the brightest emitters.
        /// </summary>
        /// <remarks>
        /// Overflowing the light-slice table is not a soft failure: the bake reports success and
        /// the slice renders BLACK in game. H24 put 1199 and 1092 slices on two appended delta
        /// slices and lost the entire duplicated environment (mean rmse 23.4 -> 36.2) with nothing
        /// in the log but a warning. Retail never approaches the limit because its slices each
        /// cover one room; ours cover whole zones, so a legitimate bake can reach it. This runs
        /// after every pass that can add a slice - the emissive pass, the lost-emitter pass and
        /// the light-entity pass each append independently, so no single pass can police it.
        /// </remarks>
        private static void TrimLightSlices(RadiosityRuntime.RuntimeSurfaceLights lights, Action<string> log)
        {
            if (lights?.LightSlices == null || lights.LightSlices.Count <= MaxSurfaceLightSlices)
                return;

            int before = lights.LightSlices.Count;
            // Rank by the flux the slice actually delivers - Weight is an absolute per-sample
            // gain, so the sum over its items is the entity's contribution to the room.
            var ranked = new List<(int index, long flux)>(before);
            for (int i = 0; i < before; i++)
            {
                RadiosityRuntime.RuntimeSurfaceLights.LightSlice ls = lights.LightSlices[i];
                long flux = 0;
                for (uint k = ls.FirstItem; k < ls.FirstItem + ls.NumItems && k < lights.Lights.Count; k++)
                    flux += lights.Lights[(int)k].Weight;
                ranked.Add((i, flux));
            }
            ranked.Sort((a, b) => b.flux != a.flux ? b.flux.CompareTo(a.flux) : a.index.CompareTo(b.index));

            // Keep the strongest, then restore source order so the table stays deterministic.
            var keep = ranked.Take(MaxSurfaceLightSlices).Select(r => r.index).ToList();
            keep.Sort();

            var newLights = new List<RadiosityRuntime.RuntimeSurfaceLights.Light>(lights.Lights.Count);
            var newSlices = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>(keep.Count);
            List<Resources.Resource> newEntities =
                lights.LightSliceEntities != null && lights.LightSliceEntities.Count == before
                    ? new List<Resources.Resource>(keep.Count) : null;
            foreach (int i in keep)
            {
                RadiosityRuntime.RuntimeSurfaceLights.LightSlice ls = lights.LightSlices[i];
                uint first = (uint)newLights.Count;
                int items = 0;
                for (uint k = ls.FirstItem; k < ls.FirstItem + ls.NumItems && k < lights.Lights.Count; k++)
                {
                    newLights.Add(lights.Lights[(int)k]);
                    items++;
                }
                ls.FirstItem = first;
                ls.NumItems = (ushort)items;
                newSlices.Add(ls);
                newEntities?.Add(lights.LightSliceEntities[i]);
            }

            lights.Lights = newLights;
            lights.LightSlices = newSlices;
            if (newEntities != null)
                lights.LightSliceEntities = newEntities;
            log?.Invoke("  WARNING: " + before + " light slices exceed the engine limit of " + MaxSurfaceLightSlices +
                        " - kept the " + newSlices.Count + " brightest, dropped " + (before - newSlices.Count) +
                        " (an overflowed slice renders black)");
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

            float reach = settings.UnbakedEmitterReach > 0.0f
                ? settings.UnbakedEmitterReach
                : Math.Max(settings.EmitterSampleRadius * 4.0f, 3.0f);
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
                RetailLightPriors.Prior prior = settings.DeltaPriorOffset != Vector3.Zero
                    ? lightPriors?.LookupOffset(mover.Resource,
                          new Vector3(mover.Transform.M41, mover.Transform.M42, mover.Transform.M43),
                          settings.DeltaPriorOffset)
                    : lightPriors?.Lookup(mover.Resource);
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

                // NOTE (2026-08-26): a coverage-based rule was tried here - pick the slice with the
                // MOST live texels within reach rather than the single nearest - to stop zone
                // chunking stranding a room's lights in a slice holding none of its probes. It
                // changed NOTHING: h68's light diet in the cam3 room came out identical to h66's
                // (slice 11: 0 probes, 15 lights, 11.4 direct, to the decimal), and the pass added
                // the same 202/267 movers. Those stranded lights do not come from THIS pass at all
                // - they are per-slice lights from BuildSurfaceLights. Fix the attribution there,
                // not here.
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

                Vector3 tint = EmissiveLightColour(mover, geometry, settings);
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
            RetailLightPriors lightPriors, Action<string> log = null)
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
                    RetailLightPriors.Prior prior = settings.DeltaPriorOffset != Vector3.Zero
                    ? lightPriors?.LookupOffset(mover.Resource,
                          new Vector3(mover.Transform.M41, mover.Transform.M42, mover.Transform.M43),
                          settings.DeltaPriorOffset)
                    : lightPriors?.Lookup(mover.Resource);
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
                    {
                        // Same buried-centroid failure as the off-geometry pass below: a centroid
                        // inside the fixture's shell fails every ray and the emitter ships
                        // nothing. Bounded blind fallback - nearest probes without the ray test,
                        // tight enough to stay on the fixture's own wall.
                        float blindSq = 1.5f * 1.5f;
                        foreach (int texel in grid.Neighbours(centre))
                        {
                            float d = (texels[texel].Position - centre).LengthSquared();
                            if (d <= blindSq)
                                nearest.Add((texel, d));
                        }
                        if (nearest.Count == 0)
                            continue;
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
                        if (!seen.Add(inputProbeForTexel[texel]))
                            continue;
                        chosen.Add(texel);
                    }
                    if (chosen.Count == 0)
                        continue;

                    Vector3 tint = EmissiveLightColour(mover, geometry, settings);
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

            // ---- emitters with no emissive TRIANGLES in the bake geometry at all -----------
            // The instance loop above recovers movers that lost the texel race but whose
            // emissive triangles made it into an instance. A second class never gets that far:
            // the emissive submesh was excluded from the bake geometry entirely - 219 of the
            // 1,342 emitters CA lit on Solace (RADIOSITY_LEVEL.BIN, 2026-08-26), including 94 of
            // 95 SPOTS_01 and 82 of 84 WARNING_LIGHTs. The mesh itself is the only record of
            // those, and the SLICE ELECTION matters: slices follow islands, not space, so
            // admitting a mover into "any slice with a probe nearby" ships its full flux into
            // two or three interleaved slices (measured: 1,502 groups from ~560 emitters, and
            // the spliced table rendered 2.23x retail). A mover is owned by the slice holding
            // the nearest bake instance to its emissive centroid - computable identically from
            // every slice, so exactly one slice emits it.
            if (settings.EmitTexellessEmitters)
            {
                var sliceInstances = new HashSet<RadiosityGeometry.Instance>(instances);
                var meshCache = new Dictionary<Models.CS2.Component.LOD.Submesh, cMesh>();
                int fallbackEmitters = 0, fallbackLights = 0;
                // Skip-reason census for the residual coverage tail: 141 authored emitters still
                // shipped nothing after this pass first landed (whole families - CONDUIT_PIPES,
                // LIFEBOAT_DOCK_CEILING - with healthy strength), and the suspects are all here.
                int skipNoCentroid = 0, skipElection = 0, skipNoVisible = 0;

                for (int moverIndex = 0; moverIndex < level.Movers.Entries.Count; moverIndex++)
                {
                    if (covered.Contains(moverIndex))
                        continue;
                    Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[moverIndex];
                    if (mover?.RenderableElements == null || SuppressedByRetail(lightPriors, mover))
                        continue;
                    float multiplier = RadiosityGeometry.ResolveMoverEmissiveStrength(mover, settings);
                    if (multiplier <= 0.0f)
                        continue;
                    if (!RadiosityGeometry.TryMeasureEmissiveCentroid(mover, meshCache, out Vector3 centre, out float area))
                    { skipNoCentroid++; continue; }

                    RadiosityGeometry.Instance nearestInstance = null;
                    float bestDistSq = float.MaxValue;
                    foreach (RadiosityGeometry.Instance inst in geometry.Instances)
                    {
                        if (inst.DonorOnly)
                            continue;
                        float d = (inst.Centre - centre).LengthSquared();
                        if (d < bestDistSq) { bestDistSq = d; nearestInstance = inst; }
                    }
                    if (nearestInstance == null || !sliceInstances.Contains(nearestInstance))
                    { skipElection++; continue; }

                    var nearest = new List<(int texel, float distanceSq)>();
                    foreach (int texel in grid.Neighbours(centre))
                    {
                        if (!geometry.Visible(centre, texels[texel].RayOrigin, settings.RayEpsilon))
                            continue;
                        nearest.Add((texel, (texels[texel].Position - centre).LengthSquared()));
                    }
                    if (nearest.Count == 0)
                    {
                        // A centroid buried inside the fixture's own shell fails every visibility
                        // ray, and the emitter ships nothing however healthy its strength - the
                        // shape of the whole-family residual misses. Retail lights these, so fall
                        // back to the nearest probes WITHOUT the ray test, but keep the reach
                        // tight: a bounded blind injection stays on the fixture's own wall, where
                        // the unbounded version would light the neighbouring room through it.
                        float blindSq = 1.5f * 1.5f;
                        foreach (int texel in grid.Neighbours(centre))
                        {
                            float d = (texels[texel].Position - centre).LengthSquared();
                            if (d <= blindSq)
                                nearest.Add((texel, d));
                        }
                        if (nearest.Count == 0)
                        { skipNoVisible++; continue; }
                    }
                    nearest.Sort((a, b) => a.distanceSq.CompareTo(b.distanceSq));

                    RetailLightPriors.Prior prior = lightPriors?.Lookup(mover.Resource);
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
                    covered.Add(moverIndex);

                    Vector3 tint = EmissiveLightColour(mover, geometry, settings);
                    byte scale = prior != null ? prior.Scale : EmissiveScaleByte(multiplier * settings.EmissiveScale);
                    byte weight = prior != null
                        ? (settings.MatchRetailSampleCounts ? prior.WeightFor(chosen.Count) : prior.MeanWeight)
                        : EmissiveWeightByte(lightPriors.K, area, chosen.Count);

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
                            Weight = weight,
                            TintR = 255,
                            TintG = 255,
                            TintB = 255,
                            Flags = 0
                        });
                    }
                    fallbackEmitters++;
                    fallbackLights += (int)(lights.Lights.Count - first);

                    lights.LightSlices.Add(new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                    {
                        FirstItem = first,
                        NumItems = (ushort)(lights.Lights.Count - first),
                        EntityInstanceIndex = -1,
                        SiblingIndex = 0
                    });
                    lights.LightSliceEntities.Add(mover.Resource);
                }

                if (fallbackEmitters > 0 || skipNoVisible > 0)
                    log?.Invoke("    off-geometry emitters: " + fallbackEmitters +
                                " movers with no emissive triangles in geometry emitted " + fallbackLights +
                                " lights (nearest-instance slice election; skipped: " +
                                skipNoCentroid + " no-centroid, " + skipElection + " other-slice, " +
                                skipNoVisible + " no-probe-even-blind)");
            }
        }

        private static void BuildEmissiveSurfaceLights(
            Level level, RadiosityGeometry geometry, SurfaceTexel[] texels, int[] inputProbeForTexel, ProbeGrid grid,
            RadiosityRuntime.RuntimeSurfaceLights lights, RadiosityBakeSettings settings,
            Dictionary<int, float> emissiveAreas, RetailLightPriors lightPriors, Action<string> log)
        {
            int colourChecked = 0, colourExact = 0, colourNear = 0;
            // How many LIGHTS ship retail's prior values versus our own derivation? The emitter-level
            // prior count does not answer this - one emitter becomes several lights.
            int lightsFromPrior = 0, lightsDerived = 0, emittersPrior = 0, emittersDerived = 0;
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
                RetailLightPriors.Prior prior = settings.DeltaPriorOffset != Vector3.Zero
                    ? lightPriors?.LookupOffset(mover.Resource,
                          new Vector3(mover.Transform.M41, mover.Transform.M42, mover.Transform.M43),
                          settings.DeltaPriorOffset)
                    : lightPriors?.Lookup(mover.Resource);
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
                Vector3 emissiveTint = EmissiveLightColour(mover, geometry, settings);
                // SELF-CHECK on the gamma decode: where a mover HAS a retail prior we know the
                // colour retail shipped for it, so our derivation can be scored against thousands
                // of real emitters instead of against a hand-checked palette. Agreement here means
                // the prior table is redundant for colour and added content can be derived.
                if (prior != null)
                {
                    colourChecked++;
                    if (prior.R == ToByte(emissiveTint.X) && prior.G == ToByte(emissiveTint.Y) &&
                        prior.B == ToByte(emissiveTint.Z)) colourExact++;
                    else if (Math.Abs(prior.R - ToByte(emissiveTint.X)) <= 2 &&
                             Math.Abs(prior.G - ToByte(emissiveTint.Y)) <= 2 &&
                             Math.Abs(prior.B - ToByte(emissiveTint.Z)) <= 2) colourNear++;
                }
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
                        // counted below
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
                if (prior != null) { lightsFromPrior += count; if (count > 0) emittersPrior++; }
                else               { lightsDerived  += count; if (count > 0) emittersDerived++; }
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

            if (colourChecked > 0)
                log?.Invoke("    light colour vs retail priors: " + colourExact + " exact, " + colourNear +
                            " within 2, of " + colourChecked + " emitters with a prior (" +
                            (100.0 * colourExact / colourChecked).ToString("0.0") + "% exact)" +
                            (settings.SurfaceLightGammaEncode ? "  [gamma encode ON]" : "  [saturation " + settings.SurfaceLightSaturation + "]"));
            log?.Invoke("    LIGHT SOURCE: " + lightsFromPrior + " lights from retail priors, " +
                        lightsDerived + " derived by us  (" +
                        (lightsFromPrior + lightsDerived > 0 ? (100.0 * lightsFromPrior / (lightsFromPrior + lightsDerived)).ToString("0.0") : "-") +
                        "% scavenged);  emitters: " + emittersPrior + " prior, " + emittersDerived + " derived");
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
                // A LIGHT ENTITY's colour takes the SAME gamma-2.0 encode as an emissive's, and
                // is NOT peak-normalised. Verified against the light dominating ChallengeMap3's
                // cam9 room, which retail ships as rgb(242,190,111):
                //   authored (230,143,49) -> floor(sqrt(255*c)) -> (242,190,111)   EXACT
                //   peak-normalised first -> (255,159,54) -> (255,201,117)         WRONG
                // The old path normalised and then desaturated toward luminance grey, which is
                // why our copy of that room lit grey (177,177,177 at weight 46) where retail lit
                // warm amber at weight 195 - the single largest error in that frame.
                Vector3 normalised = settings.SurfaceLightGammaEncode
                    ? EmissiveLightColour(colour * 255.0f, settings)
                    : Desaturate(colour / peak, settings.SurfaceLightSaturation);
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
                        // Cap raised from 191 to 255 under the gamma flag: retail ships light
                        // weights ABOVE the old cap - 195 on cam9's dominant amber and 205 on
                        // Solace's - so 191 clipped exactly the brightest lights in a room.
                        Weight = (byte)Math.Max(1, Math.Min(settings.SurfaceLightGammaEncode ? 255 : 191,
                                                            (int)Math.Round(32.0 * Math.Sqrt(energy)))),
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
                    // Per-slice ceiling: transfers past retail's envelope (<=161 per slice on
                    // every level measured) overrun an engine buffer - CM3 shipped 440/731 and
                    // heap-corrupted in RADIOSITY::destroy on every level close. Whole doors are
                    // dropped rather than partially emitted.
                    if (info.Transfers.Count + settings.MaxTransfersPerDoor > settings.MaxDoorTransfersPerSlice)
                        break;

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
            RadiosityBakeSettings settings, float maxDist, float maxDistSq, HashSet<int> skipProbeSlots = null)
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
                if (skipProbeSlots != null && skipProbeSlots.Contains(probeSlot)) continue;
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

                    // Selection is GEOMETRIC only. Radiance-aware ranking (formFactor x injected-
                    // energy proxy) was tried and REVERTED: in dim corridors it reaches for the
                    // brightest clusters in range while retail's links sample the local dim field,
                    // and with per-probe exp-mass matching that renders dim rooms blown bright
                    // (CM5 cam11 hit rmse 41.7) - importance sampling without dividing out the
                    // importance. Geometry-ranked links inherit the local field like retail's.
                    float rank = formFactor;

                    // Strict visibility here, unlike the soft test the in-slice solve uses. Relaxing
                    // it to VisibleSoft was measured on ChallengeMap4: fixups 46769 -> 59405
                    // (retail 78294) but mean rmse 12.49 -> 13.17, because the extra energy lands
                    // in rooms that were already at parity rather than the dim ones (cam13 1.06x
                    // -> 1.18x). The cross-boundary path is not what retail's dim-room wash rides.
                    // CAVEAT (2026-08-28): that measurement was pure ADDITION - extra fixups on top
                    // of full in-slice gather, in the corner-contaminated scoring era. Under
                    // CrossSliceOnePool the semantics change to fair DISPLACEMENT: cross-slice
                    // candidates compete on the same soft test and same curve as the in-slice
                    // solve, and each win overlays (removes) an in-slice link, so total gather is
                    // conserved and only its slice split moves. Retail's boundary probes are
                    // overlay-served (fixups carry 19% of CM3's render; raw in-slice p10 0-1031
                    // rising to 3371 with the overlay) while ours were in-slice-served with inert
                    // fixups - the measured mechanism of the 2-slice-level overshoot.
                    if (settings.CrossSliceOnePool)
                    {
                        Vector3 emitterOrigin = other.Position + other.Normal * settings.ProbeSurfaceOffset;
                        if (!VisibleSoft(geometry, origin, probe.Normal, emitterOrigin, other.Normal,
                                         settings, probeTexel, otherTexel))
                            continue;
                    }
                    else if (!geometry.Visible(origin, other.Position + other.Normal * settings.ProbeSurfaceOffset, settings.RayEpsilon))
                        continue;

                    candidates.Add((otherTexel, rank, distance, cosReceiver * cosEmitter));
                }

                if (candidates.Count == 0)
                    return;

                // One-pool competition sorts by the BYTE the candidate would carry, so the
                // early-stop below ("no candidate can beat the current weakest") is sound.
                if (settings.CrossSliceOnePool)
                    candidates.Sort((a, b) => InfluenceWeight(b.distance, b.cosProduct, settings)
                        .CompareTo(InfluenceWeight(a.distance, a.cosProduct, settings)));
                else
                    candidates.Sort((a, b) => b.weight.CompareTo(a.weight));
                var emittedList = new List<RadiosityRuntime.RuntimeInfluenceFixup>();

                // Working copy of the probe's slot weights, updated as fixups claim slots. The
                // base entries stay as written (a fixup rewrites its slot only when applied),
                // but WITHOUT the overlay every replacement candidate re-picked the same weakest
                // base slot, and the engine - applying the range in order - left that slot
                // holding the WEAKEST candidate instead of the strongest.
                var slotWeights = new byte[InfluencesPerProbe];
                for (int k = 0; k < InfluencesPerProbe; k++)
                    slotWeights[k] = ReadInfluenceWeight(receiver.Slice, probeSlot * InfluencesPerProbe + k);

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

        /// <summary>
        /// Neighbour entries and cross-slice fixups for a freshly appended delta slice, so its
        /// probes can gather bounce from the retail clusters around them.
        /// </summary>
        /// <remarks>
        /// A delta slice is self-contained for DIRECT light - every level light in reach of its
        /// probes got a light slice at bake time - but the surrounding room's bounce lives in the
        /// clusters of whichever retail slice owns it, and influence indices cannot cross slices
        /// except through fixups. Without these, geometry moved into or added inside an existing
        /// room renders with direct light only, which for a mostly indirectly-lit wall is
        /// near-black (the CM3 cam9 moved-vent-wall test). Only the delta slice receives: its
        /// neighbour table is appended at the tail of the flattened arrays, so nothing retail
        /// shipped moves.
        /// </remarks>
        private static int AppendDeltaFixups(
            Level level, RadiosityRuntime runtime, SliceBake bake, int newSliceIndex,
            RadiosityGeometry geometry, RadiosityBakeSettings settings, Action<string> log)
        {
            if (!settings.EmitCrossSliceFixups || bake?.Texels == null || newSliceIndex == 0)
            {
                runtime.SliceNeighbourArrayOffsets.Add((short)runtime.FlattenedOtherSliceIndices.Count);
                runtime.SliceNeighbourCounts.Add(0);
                return 0;
            }

            // TEMPLATE CLONING for MOVED content: a delta probe whose mover simply translated
            // copies the ENTIRE influence list - clusters and weights verbatim - of the nearest
            // retail surface probe at its pre-move position. That is retail's own solved answer
            // for that exact spot: every weight-level calibration scheme tried (global bias,
            // per-probe exp-mass matching, radiance-aware ranking) fixed one room while breaking
            // another, because the same composite needs opposite corrections in a bright records
            // room and a dim corridor - the difference is WHICH clusters retail linked, not how
            // hard. Cloned probes need no calibration at all; the geometric path below covers
            // probes with no retail predecessor (genuinely new content).
            var claimed = new HashSet<int>();
            var clonesBySlice = new Dictionary<int, List<RadiosityRuntime.RuntimeInfluenceFixup>>();
            int cloned = 0;
            // With the donor shell on, cloning is OFF: a cloned diet routes ALL of a probe's
            // gather through the saturating fixup path (0.38x on the CM9 rack even with retail's
            // own weights), which is exactly what the in-slice donors exist to avoid.
            if (settings.RetailTransforms != null && !settings.DeltaDonorShell)
            {
                // Per-mover translation since pristine.
                var moveDelta = new Dictionary<int, Vector3>();
                for (int m = 0; m < level.Movers.Entries.Count; m++)
                {
                    Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[m];
                    if (mover.Resource == null) continue;
                    ulong key = ((ulong)mover.Resource.composite_instance_id.AsUInt32 << 32) | mover.Resource.resource_id.AsUInt32;
                    if (!settings.RetailTransforms.TryGetValue(key, out System.Numerics.Matrix4x4 pristineT)) continue;
                    var d = new Vector3(mover.Transform.M41 - pristineT.M41,
                                        mover.Transform.M42 - pristineT.M42,
                                        mover.Transform.M43 - pristineT.M43);
                    if (d.LengthSquared() > 1e-6f)
                        moveDelta[m] = d;
                }

                if (moveDelta.Count > 0)
                {
                    // Retail probe lookup grid: position -> (slice, slot).
                    const float cloneRadius = 1.0f;
                    var probeGrid = new Dictionary<(int, int, int), List<(Vector3 pos, int slice, int slot)>>();
                    (int, int, int) PCell(Vector3 v) =>
                        ((int)Math.Floor(v.X / cloneRadius), (int)Math.Floor(v.Y / cloneRadius), (int)Math.Floor(v.Z / cloneRadius));
                    for (int s = 0; s < newSliceIndex; s++)
                    {
                        RadiosityRuntime.RuntimeDataSlice retail = runtime.Slices[s];
                        for (int slot = 0; slot < retail.SurfaceProbePositions.Count; slot++)
                        {
                            Vector4 p = retail.SurfaceProbePositions[slot];
                            if (p.W == 0) continue;
                            var pos = new Vector3(p.X, p.Y, p.Z);
                            (int, int, int) key = PCell(pos);
                            if (!probeGrid.TryGetValue(key, out List<(Vector3, int, int)> list))
                                probeGrid[key] = list = new List<(Vector3, int, int)>();
                            list.Add((pos, s, slot));
                        }
                    }

                    // A template is only usable if it actually EATS something: the probe arrays
                    // carry padding slots with live positions but all-zero (or out-of-range)
                    // influence lists, and cloning one of those stamped 32 zero-weights onto the
                    // delta probe AND claimed it away from the geometric fallback - 13 of the
                    // moved CM9 server rack's 45 probes went pitch black exactly this way.
                    var dietCache = new Dictionary<(int, int), int>();
                    int DietSlots((int slice, int slot) key)
                    {
                        if (dietCache.TryGetValue(key, out int cached)) return cached;
                        RadiosityRuntime.RuntimeDataSlice sl = runtime.Slices[key.slice];
                        int nonZero = 0;
                        for (int k = 0; k < InfluencesPerProbe; k++)
                            if (ReadInfluenceWeight(sl, key.slot * InfluencesPerProbe + k) > 0)
                                nonZero++;
                        dietCache[key] = nonZero;
                        return nonZero;
                    }

                    for (int t = 0; t < AtlasTexels; t++)
                    {
                        if (!bake.Texels[t].Live) continue;
                        int probeSlot = bake.SurfaceSlotForTexel[t];
                        if (probeSlot < 0 || claimed.Contains(probeSlot)) continue;
                        if (!moveDelta.TryGetValue(bake.Texels[t].MoverIndex, out Vector3 delta)) continue;

                        Vector3 target = bake.Texels[t].Position - delta;
                        //Two tiers: nearest healthy template (at least half a diet), else nearest
                        //with ANY diet. A probe with no live-diet template within radius stays
                        //unclaimed for the geometric path.
                        int bestSlice = -1, bestSlot = -1, fallSlice = -1, fallSlot = -1;
                        float bestD = cloneRadius * cloneRadius, fallD = cloneRadius * cloneRadius;
                        (int cx, int cy, int cz) = PCell(target);
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dy = -1; dy <= 1; dy++)
                                for (int dz = -1; dz <= 1; dz++)
                                {
                                    if (!probeGrid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<(Vector3 pos, int slice, int slot)> list))
                                        continue;
                                    foreach ((Vector3 pp, int ps, int pslot) in list)
                                    {
                                        float d2 = Vector3.DistanceSquared(pp, target);
                                        if (d2 >= bestD && d2 >= fallD) continue;
                                        int diet = DietSlots((ps, pslot));
                                        if (diet >= InfluencesPerProbe / 2)
                                        {
                                            if (d2 < bestD) { bestD = d2; bestSlice = ps; bestSlot = pslot; }
                                        }
                                        else if (diet > 0)
                                        {
                                            if (d2 < fallD) { fallD = d2; fallSlice = ps; fallSlot = pslot; }
                                        }
                                    }
                                }
                        if (bestSlice < 0) { bestSlice = fallSlice; bestSlot = fallSlot; }
                        if (bestSlice < 0) continue;

                        RadiosityRuntime.RuntimeDataSlice template = runtime.Slices[bestSlice];
                        if (!clonesBySlice.TryGetValue(bestSlice, out List<RadiosityRuntime.RuntimeInfluenceFixup> clones))
                            clonesBySlice[bestSlice] = clones = new List<RadiosityRuntime.RuntimeInfluenceFixup>();
                        // ALL 32 slots are cloned, zero weights included, so the probe's diet is
                        // a full replacement - a base link left live under an uncloned slot would
                        // re-mix our approximation into retail's answer.
                        for (int k = 0; k < InfluencesPerProbe; k++)
                        {
                            int srcSlot = bestSlot * InfluencesPerProbe + k;
                            byte w = ReadInfluenceWeight(template, srcSlot);
                            ColourRGBA8 idx = template.SurfaceProbeInfluences[srcSlot / 2];
                            byte ix = (srcSlot & 1) == 0 ? idx.R : idx.B;
                            byte iy = (srcSlot & 1) == 0 ? idx.G : idx.A;
                            int dstOffset = probeSlot * InfluencesPerProbe + k;
                            clones.Add(new RadiosityRuntime.RuntimeInfluenceFixup
                            {
                                WeightTexOffset = dstOffset,
                                InflTexOffset = dstOffset * 2,
                                Weight = w,
                                Padding = 0,
                                ClusterTex = new Vector2u8 { X = ix, Y = iy }
                            });
                        }
                        claimed.Add(probeSlot);
                        cloned++;
                    }
                }
            }
            if (cloned > 0)
                log?.Invoke("    delta fixups: " + cloned + " moved probes cloned retail influence lists verbatim");

            // Donor-fed probes with an (almost) full native diet need no cross-slice links at
            // all; the geometric pass below only tops up the starved ones (occluded corners,
            // shell-edge probes). A saturated cross-slice link that replaces a native link is a
            // strict loss even at a higher weight byte.
            if (settings.DeltaDonorShell && bake.UsedInfluenceSlots != null)
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
            float maxDist = settings.MaxInfluenceDistance;
            int total = 0;
            var ourEntries = new List<(byte neighbour, RadiosityRuntime.FixupRange range)>();
            int fullBakeCap = settings.MaxCrossSliceFixupsPerProbe;
            settings.MaxCrossSliceFixupsPerProbe = Math.Max(fullBakeCap, settings.DeltaCrossSliceFixupsPerProbe);
            try
            {
                for (int s = 0; s < newSliceIndex; s++)
                {
                    SliceBake emitter = RetailEmitterBake(runtime.Slices[s]);
                    if (emitter == null)
                        continue;
                    int first = runtime.InfluenceFixups.Count;
                    if (clonesBySlice.TryGetValue(s, out List<RadiosityRuntime.RuntimeInfluenceFixup> clones))
                        runtime.InfluenceFixups.AddRange(clones);
                    EmitPairFixups(bake, emitter, runtime, geometry, settings, maxDist, maxDist * maxDist, claimed);
                    int emitted = runtime.InfluenceFixups.Count - first;
                    ourEntries.Add(((byte)s, new RadiosityRuntime.FixupRange { First = first, Num = emitted }));
                    total += emitted;
                    if (emitted > 0)
                        log?.Invoke("    delta fixups: " + emitted + " gathering from retail slice " + s);
                }
            }
            finally
            {
                settings.MaxCrossSliceFixupsPerProbe = fullBakeCap;
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

            // Retail's convention, measured on every CM9 slice (52,668 fixups, 100.0%): the BASE
            // weight byte under a fixup slot is ZERO - the zero is the "this slot is externally
            // fed" marker. We left our geometric in-slice weights standing under 1,176 of the
            // delta fixups, and slots where base and fixup disagree are exactly where the engine
            // can pick the wrong side. Zero every base byte a fixup overrides.
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
        /// <remarks>
        /// Cluster world positions decode from the half4 ClusterPositions array, which is indexed
        /// by atlas texel exactly as a fixup's ClusterTex expects. Cluster normals are not stored;
        /// each takes the normal of the nearest live input probe - input probes sit on the same
        /// surfaces, but their tiled layout makes a direct texel join impossible.
        /// </remarks>
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
                var normal = new Vector3(enc.R / 127.5f - 1.0f, enc.G / 127.5f - 1.0f, enc.B / 127.5f - 1.0f);
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

            // Brightness proxy per cluster texel: the slice's own surface-light injections
            // (Weight x Scale at each sampled input probe) summed within a short radius, plus a
            // floor so unlit-but-bounced areas keep a nonzero score. Selection-only - the
            // emitted fixup weight byte still comes from the geometric curve.
            var proxy = new float[AtlasTexels];
            const float ambientFloor = 0.05f;
            var lights = retail.SurfaceLights?.Lights;
            if (lights != null && lights.Count > 0)
            {
                var injections = new List<(Vector3 pos, float energy)>();
                foreach (RadiosityRuntime.RuntimeSurfaceLights.Light l in lights)
                {
                    int texel = l.V * ProbeTexWidth + l.U;
                    if (texel < 0 || texel >= retail.InputProbePositions.Count)
                        continue;
                    Vector4u16 ip = retail.InputProbePositions[texel];
                    if (ip.W == 0)
                        continue;
                    injections.Add((new Vector3(FromHalf(ip.X), FromHalf(ip.Y), FromHalf(ip.Z)),
                                    l.Weight * (l.Scale / 255.0f)));
                }
                const float reach = 2.5f;
                var injGrid = new Dictionary<(int, int, int), List<int>>();
                (int, int, int) InjCell(Vector3 v) =>
                    ((int)Math.Floor(v.X / reach), (int)Math.Floor(v.Y / reach), (int)Math.Floor(v.Z / reach));
                for (int i = 0; i < injections.Count; i++)
                {
                    (int, int, int) key = InjCell(injections[i].pos);
                    if (!injGrid.TryGetValue(key, out List<int> list))
                        injGrid[key] = list = new List<int>();
                    list.Add(i);
                }
                for (int t = 0; t < AtlasTexels; t++)
                {
                    if (!texels[t].Live) continue;
                    float sum = ambientFloor;
                    (int gx, int gy, int gz) = InjCell(texels[t].Position);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                if (!injGrid.TryGetValue((gx + dx, gy + dy, gz + dz), out List<int> list))
                                    continue;
                                foreach (int i in list)
                                    if (Vector3.DistanceSquared(texels[t].Position, injections[i].pos) < reach * reach)
                                        sum += injections[i].energy;
                            }
                    proxy[t] = sum;
                }
            }
            else
            {
                for (int t = 0; t < AtlasTexels; t++) proxy[t] = ambientFloor;
            }

            return new SliceBake { Texels = texels, InputProbeForTexel = inputProbeForTexel, TexelRadianceProxy = proxy };
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
        /// <summary>
        /// Ceiling on stored input-probe albedo LUMA, decoded from retail: across SCI_Hub,
        /// Solace, CM3 and CM9 no retail probe ever reaches pure white (max channel byte 252,
        /// luma p99 pinned at 0.90-0.91 on every slice) while ours shipped 1.0 clusters. An
        /// over-unity albedo region makes the runtime relaxation loop diverge - the standing
        /// field saturates and the level renders a bloom WHITEOUT (SCI_Hub cam4/1/7/12 at
        /// uniform 255, +1.1x on its aggregate; every transport improvement made it WORSE
        /// because better coupling amplifies a divergent loop). The cap is colour-preserving:
        /// channels stay high for saturated colours, only the luma is scaled down, which is
        /// exactly the shape of retail's data.
        /// </summary>
        private const float MaxAlbedoLuma = 232.0f / 255.0f;

        private static ColourRGBA8 EncodeAlbedo(Vector3 colour, byte alpha)
        {
            // colour arrives as (B, G, R) - see the swizzle below - so Z is the red channel.
            float luma = 0.2126f * colour.Z + 0.7152f * colour.Y + 0.0722f * colour.X;
            if (luma > MaxAlbedoLuma)
                colour *= MaxAlbedoLuma / luma;
            return new ColourRGBA8
            {
                R = ToByte(colour.Z),
                G = ToByte(colour.Y),
                B = ToByte(colour.X),
                A = alpha
            };
        }

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

        /// <summary>
        /// Copy retail's engine-owned 16x16 atlas corner into each of our slices: cluster
        /// positions and scatter verbatim, mangle re-pointed to our nearest live surface probe
        /// so the engine's reads resolve inside OUR probe layout. Slices correspond by index -
        /// island grouping follows retail, so slice s covers the same rooms in both bakes.
        /// </summary>
        /// <summary>
        /// Overlay retail's stored input-probe albedo onto ours, matched by world position
        /// (nearest live retail probe within <see cref="RadiosityBakeSettings.RetailAlbedoMatchRadius"/>;
        /// the probeswap splice this ports matched 100% of probes at mean 0.33m on Torrens,
        /// SCI_Hub and CM3). Unmatched probes keep the derived value, so added content is
        /// unaffected. See the call site for the pre-remap rationale and render evidence.
        /// </summary>
        private static void ImportRetailAlbedo(RadiosityRuntime runtime,
            List<RadiosityRuntime.RuntimeDataSlice> retailSlices, RadiosityBakeSettings settings, Action<string> log)
        {
            if (retailSlices == null || retailSlices.Count == 0)
                return;

            float cell = Math.Max(0.25f, settings.RetailAlbedoMatchRadius);
            var donors = new Dictionary<(int, int, int), List<(Vector3 pos, ColourRGBA8 albedo, ColourRGBA8 normal, bool hasNormal)>>();
            foreach (RadiosityRuntime.RuntimeDataSlice rs in retailSlices)
            {
                if (rs?.InputProbePositions == null || rs.InputProbeAlbedo == null)
                    continue;
                int n = Math.Min(rs.InputProbePositions.Count, rs.InputProbeAlbedo.Count);
                for (int i = 0; i < n; i++)
                {
                    Vector4u16 q = rs.InputProbePositions[i];
                    if (q.W == 0) continue;
                    var p = new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z));
                    (int, int, int) key = ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell), (int)Math.Floor(p.Z / cell));
                    if (!donors.TryGetValue(key, out List<(Vector3 pos, ColourRGBA8 albedo, ColourRGBA8 normal, bool hasNormal)> list))
                        donors[key] = list = new List<(Vector3 pos, ColourRGBA8 albedo, ColourRGBA8 normal, bool hasNormal)>();
                    bool hasNormal = rs.InputProbeNormals != null && i < rs.InputProbeNormals.Count;
                    list.Add((p, rs.InputProbeAlbedo[i], hasNormal ? rs.InputProbeNormals[i] : default, hasNormal));
                }
            }
            if (donors.Count == 0)
                return;

            float maxDistSq = settings.RetailAlbedoMatchRadius * settings.RetailAlbedoMatchRadius;
            int matched = 0, total = 0;
            foreach (RadiosityRuntime.RuntimeDataSlice slice in runtime.Slices)
            {
                if (slice?.InputProbePositions == null || slice.InputProbeAlbedo == null)
                    continue;
                int n = Math.Min(slice.InputProbePositions.Count, slice.InputProbeAlbedo.Count);
                for (int i = 0; i < n; i++)
                {
                    Vector4u16 q = slice.InputProbePositions[i];
                    if (q.W == 0) continue;
                    total++;
                    var p = new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z));
                    int cx = (int)Math.Floor(p.X / cell), cy = (int)Math.Floor(p.Y / cell), cz = (int)Math.Floor(p.Z / cell);
                    float best = maxDistSq;
                    (ColourRGBA8 albedo, ColourRGBA8 normal, bool hasNormal) bestDonor = default;
                    bool found = false;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                if (!donors.TryGetValue((cx + dx, cy + dy, cz + dz), out List<(Vector3 pos, ColourRGBA8 albedo, ColourRGBA8 normal, bool hasNormal)> list))
                                    continue;
                                foreach ((Vector3 pos, ColourRGBA8 albedo, ColourRGBA8 normal, bool hasNormal) in list)
                                {
                                    float d2 = Vector3.DistanceSquared(pos, p);
                                    if (d2 < best) { best = d2; bestDonor = (albedo, normal, hasNormal); found = true; }
                                }
                            }
                    if (!found) continue;
                    slice.InputProbeAlbedo[i] = bestDonor.albedo;
                    // The normals payload is load-bearing: SCI_Hub with retail albedo alone
                    // rendered 1.173 where albedo+normals renders 1.087 == the validating
                    // splice, reproduced across recaptures - the whole gap was the normals.
                    if (bestDonor.hasNormal && slice.InputProbeNormals != null && i < slice.InputProbeNormals.Count)
                        slice.InputProbeNormals[i] = bestDonor.normal;
                    matched++;
                }
            }
            log?.Invoke("Retail albedo+normals verbatim: " + matched + " of " + total +
                        " input probes matched within " + settings.RetailAlbedoMatchRadius.ToString("0.##") + "m");
        }

        private static void CarryRetailCorners(RadiosityRuntime runtime,
            List<RadiosityRuntime.RuntimeDataSlice> retailSlices, RadiosityBakeSettings settings, Action<string> log)
        {
            if (retailSlices == null || retailSlices.Count == 0)
                return;
            bool carryPositions = settings.CarryCornerPositions;

            int carried = 0, slicesDone = 0;
            for (int s = 0; s < runtime.Slices.Count && s < retailSlices.Count; s++)
            {
                RadiosityRuntime.RuntimeDataSlice ours = runtime.Slices[s];
                RadiosityRuntime.RuntimeDataSlice retail = retailSlices[s];
                if (ours.ClusterPositions.Count < AtlasTexels || retail.ClusterPositions.Count < AtlasTexels ||
                    ours.MangleMap.Count < AtlasTexels)
                    continue;

                var probes = new List<(Vector3 p, int slot)>();
                for (int i = 0; i < ours.SurfaceProbePositions.Count; i++)
                {
                    Vector4 sp = ours.SurfaceProbePositions[i];
                    if (sp.X == 0 && sp.Y == 0 && sp.Z == 0 && sp.W == 0)
                        continue;
                    probes.Add((new Vector3(sp.X, sp.Y, sp.Z), i));
                }
                if (probes.Count == 0)
                    continue;

                for (int ty = 0; ty < 16; ty++)
                {
                    for (int tx = 0; tx < 16; tx++)
                    {
                        int i = ty * AtlasSize + tx;
                        Vector4u16 rc = retail.ClusterPositions[i];
                        if (rc.X == 0 && rc.Y == 0 && rc.Z == 0 && rc.W == 0)
                            continue;

                        // Retail's corner SCATTER bytes are carried whole. The A channel is
                        // load-bearing everywhere: it packs (group<<4 | index)-shaped values our
                        // writer does not produce, and shipping our own A pattern there
                        // deterministically explodes the render 4-5x (dirt7/dirt9,
                        // camera-for-camera identical blowouts). The RGB matters on some levels
                        // and not others - measured: swapping retail RGB for our fill RGB left
                        // Torrens/CM3 IDENTICAL to three decimals but cost SCI_Hub 1.131 ->
                        // 1.384 (cora run) - so the full copy is strictly the better default.
                        // The Torrens/CM3 post-fix overshoot is insensitive to every corner
                        // value tested and remains an open engine-side question.
                        // REINTERPRETED (2026-08-28 evening, scatjoin): Scatter is a LINK LIST,
                        // so indexing it by corner texel index overwrites ~256 of the first
                        // ~1,900 list entries - the first ~280 probes' links - with foreign-
                        // layout pairs that decode in our file as random 14-17m cross-level
                        // links (the MU-TH-UR room measured 9.7% out-of-room scatter sources at
                        // p50 14.5m vs retail's 1.4% at 4m; retail's own corner region needs no
                        // such entries because in ITS file these positions hold its real first
                        // islands' links). CarryCornerScatter=false skips only this copy,
                        // keeping the corner reservation, positions and mangle handling intact.
                        // SCI_Hub is the falsifier: the dirt6/7-era 4.4x blowouts were what
                        // originally forced the full copy.
                        if (settings.CarryCornerScatter && i < ours.Scatter.Count && i < retail.Scatter.Count)
                            ours.Scatter[i] = retail.Scatter[i];
                        if (carryPositions)
                        {
                            ours.ClusterPositions[i] = rc;
                            var p = new Vector3(FromHalf(rc.X), FromHalf(rc.Y), FromHalf(rc.Z));
                            int bestSlot = probes[0].slot;
                            float bestD = float.MaxValue;
                            foreach ((Vector3 pp, int slot) in probes)
                            {
                                float d = Vector3.DistanceSquared(pp, p);
                                if (d < bestD) { bestD = d; bestSlot = slot; }
                            }
                            ours.MangleMap[i] = new ColourRGBA8
                            {
                                R = (byte)(bestSlot % ProbeTexWidth),
                                G = (byte)(bestSlot / ProbeTexWidth),
                                B = 255,
                                A = 63
                            };
                        }
                        carried++;
                    }
                }
                slicesDone++;
            }
            log?.Invoke("    engine corner: " + carried + " texels carried from retail over " + slicesDone +
                        " slices (retail scatter" +
                        (settings.CarryCornerPositions ? " + positions, mangle re-pointed" : "") + ")");
        }

        /// <summary>
        /// Deep-copy a slice through its own serialiser: complete by construction, and exactly
        /// what Save would emit for it right now. Entity indices stay raw;
        /// <see cref="RadiosityRuntime"/>'s save-time resolution walks every slice, clones
        /// included, so a clone's light references track the source's.
        /// </summary>
        private static RadiosityRuntime.RuntimeDataSlice CloneSlice(RadiosityRuntime.RuntimeDataSlice slice)
        {
            using (var ms = new System.IO.MemoryStream())
            {
                using (var w = new System.IO.BinaryWriter(ms, System.Text.Encoding.UTF8, true))
                    slice.Write(w);
                ms.Position = 0;
                using (var r = new System.IO.BinaryReader(ms))
                    return new RadiosityRuntime.RuntimeDataSlice(r);
            }
        }

        /// <summary>
        /// Append symmetric neighbour-table entries for the slice just added to
        /// <see cref="RadiosityRuntime.Slices"/>: every pair involving it gets an EMPTY fixup
        /// range (retail's tables list every slice from every other), and every existing range
        /// survives untouched - ranges index the global fixup array and are order-independent.
        /// </summary>
        private static void AppendEmptyNeighbourEntries(RadiosityRuntime runtime)
        {
            int newIndex = runtime.Slices.Count - 1;
            var lists = new List<(byte n, RadiosityRuntime.FixupRange r)>[newIndex + 1];
            for (int s = 0; s < newIndex; s++)
            {
                lists[s] = new List<(byte, RadiosityRuntime.FixupRange)>();
                int offset = runtime.SliceNeighbourArrayOffsets[s];
                int count = runtime.SliceNeighbourCounts[s];
                for (int n = 0; n < count; n++)
                    lists[s].Add((runtime.FlattenedOtherSliceIndices[offset + n], runtime.FlattenedFixupRanges[offset + n]));
                lists[s].Add(((byte)newIndex, new RadiosityRuntime.FixupRange { First = 0, Num = 0 }));
            }
            lists[newIndex] = new List<(byte, RadiosityRuntime.FixupRange)>();
            for (int s = 0; s < newIndex; s++)
                lists[newIndex].Add(((byte)s, new RadiosityRuntime.FixupRange { First = 0, Num = 0 }));

            runtime.SliceNeighbourCounts.Clear();
            runtime.SliceNeighbourArrayOffsets.Clear();
            runtime.FlattenedOtherSliceIndices.Clear();
            runtime.FlattenedFixupRanges.Clear();
            foreach (List<(byte n, RadiosityRuntime.FixupRange r)> list in lists)
            {
                runtime.SliceNeighbourArrayOffsets.Add((short)runtime.FlattenedOtherSliceIndices.Count);
                runtime.SliceNeighbourCounts.Add((byte)list.Count);
                foreach ((byte n, RadiosityRuntime.FixupRange r) in list)
                {
                    runtime.FlattenedOtherSliceIndices.Add(n);
                    runtime.FlattenedFixupRanges.Add(r);
                }
            }
        }

        /// <summary>Closest point on triangle (a,b,c) to p - the standard region walk.</summary>
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1.0f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        /// <summary>
        /// Graft edited retail-bound islands into byte-clones of their retail slices.
        /// </summary>
        /// <remarks>
        /// <para>The parity finding this rests on (the slicedup control): an APPENDED slice
        /// relights at 0.96x retail when it carries retail bytes - while every slice we bake
        /// ourselves delivers roughly half of retail's field however the diets reach it (cloned
        /// fixups 0.38x, native donor-fed diets 0.35x on the CM9 rack; injection, connectivity
        /// and weights all measured retail-grade in isolation - equilibrium fields resist
        /// term-by-term matching). So: keep retail's field, byte-cloned, and surgically replace
        /// only the edited island's own rect inside it.</para>
        /// <para>The island keeps its retail id (in range of every per-island state table - a
        /// beyond-range id measured 0.35x vs 0.47x alone), its rect coordinates, and its
        /// instance-map rows. Its movers leave the delta census, so the patcher restores their
        /// retail MODEL_PARAMS; the only visible change is InstanceSliceIndices[id] -> clone.
        /// Within the clone: the rect's clusters and probe slots are re-fed from a fresh
        /// rasterisation of the CURRENT geometry, texel-coincident input probes follow their
        /// texels (untouched mates keep retail albedo bytes), and the island's surface probes
        /// get fresh diets solved against the clone's retail cluster field - in-slice, no
        /// fixups - each self-calibrated to the exp mass its own slot carried in retail.</para>
        /// <para>v1 limits: retail Scale/Weight light injection onto the island's own probes
        /// describes the pre-move position (fine for nudges); texels newly live where retail
        /// was dead get no scatter self-pairs; islands whose movers do not cover the whole
        /// retail island fall through to the appended-slice path.</para>
        /// </remarks>
        /// <summary>
        /// A mover that purely TRANSLATED (rotation unchanged, shift under maxDelta) keeps its
        /// texels' RETAIL diets and probe bytes in a graft: the cluster set a diet gathers is
        /// the ROOM, which did not move. Isolated on the CM9 partial-move test: the unmoved
        /// mates' texels changed nothing but their diets, and the mates halved (0.50x) exactly
        /// like the moved rack - fresh diets on a byte-retail field deliver ~half of retail's
        /// own diets even exp-mass-matched, while slid retail diets render 0.86x.
        /// </summary>
        private static bool MoverPurelyTranslated(Level level, RadiosityBakeSettings settings, int moverIndex, float maxDelta)
        {
            if (settings.RetailTransforms == null || moverIndex < 0 || moverIndex >= level.Movers.Entries.Count)
                return false;
            Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[moverIndex];
            if (mv.Resource == null)
                return false;
            ulong k = ((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | mv.Resource.resource_id.AsUInt32;
            if (!settings.RetailTransforms.TryGetValue(k, out System.Numerics.Matrix4x4 pristine))
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

        private static int GraftDeltaIslands(
            Level level, RadiosityRuntime runtime, RadiosityGeometry geometry,
            List<RadiosityGeometry.Instance> deltaInstances, RadiosityBakeSettings settings,
            HashSet<int> deltaMovers, Action<string> log)
        {
            if (level.RadiosityInstanceMap?.Entries == null || settings.RetailModelParams == null)
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

            var cloneForSlice = new Dictionary<int, int>();
            var work = new List<(RadiosityGeometry.Instance instance, int islandId, int cloneIndex,
                                 int rx, int ry, int rw, int rh, SurfaceTexel[] texels,
                                 int[] slotForTexel, Dictionary<int, double> retailMass)>();

            // ---- pass 1: clone slices, rasterise, transplant clusters/mangle/input probes ----
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

                // RELOCATIONS (any mover translated beyond 2m) cannot graft: the island's
                // retail light-slice injection (Scale/Weight per input probe) describes the OLD
                // position, and an in-place graft keeps it. The appended path builds native
                // lights at the destination instead. Stale-identity RetailTransforms entries
                // (pristine at the exact origin - the CM9 FX family) are carries, not moves.
                bool movedFar = false;
                foreach (int m in instance.Movers)
                {
                    Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[m];
                    if (mv.Resource == null || settings.RetailTransforms == null) continue;
                    ulong k = ((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | mv.Resource.resource_id.AsUInt32;
                    if (!settings.RetailTransforms.TryGetValue(k, out System.Numerics.Matrix4x4 pristine)) continue;
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
                    if (!settings.RetailModelParams.TryGetValue(k, out byte[] mp) || mp.Length < 16) continue;
                    int w = (int)Math.Round(BitConverter.ToSingle(mp, 0) + 0.5f);
                    int h = (int)Math.Round(BitConverter.ToSingle(mp, 4) + 0.5f);
                    int x = (int)Math.Round(BitConverter.ToSingle(mp, 8));
                    int y = (int)Math.Round(BitConverter.ToSingle(mp, 12));
                    if (w >= 1 && w <= 128 && h >= 1 && h <= 128 && x >= 0 && y >= 0 && x + w <= AtlasSize && y + h <= AtlasSize)
                    { rx = x; ry = y; rw = w; rh = h; break; }
                }
                if (rx < 0)
                    continue;

                // IN PLACE, no clone: the graft only touches this island's own rect - every
                // other island's data in the slice stays byte-identical - so the retail slice
                // itself is the host. That keeps the slice count at retail's (the five-slice
                // clone experiment dimmed the WHOLE level, mean |luma| 3.7 -> 5.7: extra slices
                // are not free), and the island keeps its binding untouched. CloneSlice /
                // AppendEmptyNeighbourEntries remain for the future sacrificial-rect path, where
                // new content must steal a rect that a live island still renders from.
                int cloneIndex = retailSlice;
                cloneForSlice[retailSlice] = retailSlice;
                RadiosityRuntime.RuntimeDataSlice clone = runtime.Slices[retailSlice];

                // Snapshot the rect's retail state before any overwrite.
                var retailPos = new Dictionary<int, Vector3>();
                var retailW = new Dictionary<int, ushort>();
                var retailSlot = new Dictionary<int, int>();
                var retailMass = new Dictionary<int, double>();
                for (int y = ry; y < ry + rh; y++)
                    for (int x = rx; x < rx + rw; x++)
                    {
                        int t = y * AtlasSize + x;
                        Vector4u16 cp = clone.ClusterPositions[t];
                        if (cp.W == 0) continue;
                        retailPos[t] = FromHalf3(cp);
                        retailW[t] = cp.W;
                        ColourRGBA8 mm = clone.MangleMap[t];
                        int slot = mm.G * ProbeTexWidth + mm.R;
                        if (slot >= 0 && slot < clone.SurfaceProbePositions.Count &&
                            clone.SurfaceProbePositions[slot].W != 0)
                        {
                            retailSlot[t] = slot;
                            if (!retailMass.ContainsKey(slot))
                            {
                                double mass = 0;
                                for (int kk = 0; kk < InfluencesPerProbe; kk++)
                                {
                                    byte wgt = ReadInfluenceWeight(clone, slot * InfluencesPerProbe + kk);
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
                RasteriseInstance(geometry, instance, texels, settings);
                FoldAlbedo(texels);
                ResolveRayOrigins(geometry, texels, settings);

                // Borrow a live W for texels retail never lit.
                ushort borrowW = ToHalf(1.0f);
                foreach (ushort wv in retailW.Values) { borrowW = wv; break; }

                // Per-mover translation, for the retail-byte-preserving paths: a purely
                // translated mover's texels SLIDE retail's stored sample positions by the
                // mover's exact delta (v3's proven mechanism - zero movement for unmoved
                // mates) instead of snapping to our raster winners, which land anywhere
                // within the texel's footprint.
                var moverShift = new Dictionary<int, (bool pure, Vector3 delta)>();
                (bool pure, Vector3 delta) Shift(int moverIndex)
                {
                    if (moverShift.TryGetValue(moverIndex, out (bool, Vector3) cached)) return cached;
                    (bool, Vector3) result = (false, Vector3.Zero);
                    if (MoverPurelyTranslated(level, settings, moverIndex, 2.0f))
                    {
                        Movers.MOVER_DESCRIPTOR mv = level.Movers.Entries[moverIndex];
                        ulong mk = ((ulong)mv.Resource.composite_instance_id.AsUInt32 << 32) | mv.Resource.resource_id.AsUInt32;
                        settings.RetailTransforms.TryGetValue(mk, out System.Numerics.Matrix4x4 pristine);
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
                        clone.ClusterPositions[t] = new Vector4u16
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
                                Vector4 rsp = clone.SurfaceProbePositions[slot];
                                clone.SurfaceProbePositions[slot] = pure
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
                            clone.ClusterPositions[t] = new Vector4u16
                            {
                                X = ToHalf(texels[near].Position.X),
                                Y = ToHalf(texels[near].Position.Y),
                                Z = ToHalf(texels[near].Position.Z),
                                W = retailW[t]
                            };
                            ghostTexels++;
                            if (retailSlot.TryGetValue(t, out int orphan) && orphan != slotForTexel[near])
                            {
                                clone.SurfaceProbePositions[orphan] = UnusedSurfaceProbe;
                                for (int kk = 0; kk < InfluencesPerProbe; kk++)
                                    WriteInfluenceWeight(clone, orphan * InfluencesPerProbe + kk, 0);
                                orphanSlots++;
                            }
                        }
                        else if (!oursIsLive)
                        {
                            continue;   //dead in both: leave retail's dead-texel mangle alone
                        }
                        int nSlot = slotForTexel[near];
                        int npx = nSlot % ProbeTexWidth, npy = nSlot / ProbeTexWidth;
                        clone.MangleMap[t] = new ColourRGBA8 { R = (byte)npx, G = (byte)npy, B = 255, A = 63 };
                        mangleRewrites++;
                    }

                // Texel-coincident input probes follow their texels. Only probes that actually
                // MOVED get their normal/albedo refreshed - untouched mates keep retail bytes.
                int probesMoved = 0;
                for (int i = 0; i < clone.InputProbePositions.Count; i++)
                {
                    Vector4u16 ip = clone.InputProbePositions[i];
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
                            clone.InputProbePositions[i] = new Vector4u16
                            { X = ToHalf(np.X), Y = ToHalf(np.Y), Z = ToHalf(np.Z), W = ip.W };
                            // A purely-translated mover keeps its RETAIL normal/albedo bytes -
                            // our sampler's albedo is position-scrambled against retail's
                            // (donorcheck p10 0.14 / p90 14.6), and a slid probe's surface is
                            // the same surface.
                            if (!MoverPurelyTranslated(level, settings, texels[t].MoverIndex, 2.0f))
                            {
                                clone.InputProbeNormals[i] = EncodeNormal(texels[t].Normal);
                                clone.InputProbeAlbedo[i] = EncodeAlbedo(texels[t].Albedo, 255);
                            }
                            probesMoved++;
                        }
                        break;
                    }
                }

                log?.Invoke("    delta graft: island " + islandId + " (slice " + retailSlice + " -> clone " + cloneIndex +
                            ", rect " + rw + "x" + rh + "@" + rx + "," + ry + "): " + ourLive.Count + " live texels, " +
                            retailSlot.Count + " slots reused, " + mangleRewrites + " mangle rewrites, " +
                            ghostTexels + " ghosts, " + orphanSlots + " slots orphaned, " + probesMoved + " input probes moved");

                work.Add((instance, islandId, cloneIndex, rx, ry, rw, rh, texels, slotForTexel, retailMass));
            }

            if (work.Count == 0)
                return 0;

            // ---- pass 2: fresh diets against each clone's (post-transplant) cluster field ----
            // Runs after EVERY transplant so islands grafted into one clone see each other.
            const float candidateRange = 16.0f;   //retail's measured diet reach tops out ~14.3m
            foreach (int cloneIndex in cloneForSlice.Values)
            {
                RadiosityRuntime.RuntimeDataSlice clone = runtime.Slices[cloneIndex];

                var clusterGrid = new Dictionary<(int, int, int), List<int>>();
                (int, int, int) CCell(Vector3 v) => ((int)Math.Floor(v.X / candidateRange),
                                                     (int)Math.Floor(v.Y / candidateRange),
                                                     (int)Math.Floor(v.Z / candidateRange));
                var clusterPos = new Vector3[AtlasTexels];
                for (int t = 0; t < AtlasTexels && t < clone.ClusterPositions.Count; t++)
                {
                    Vector4u16 cp = clone.ClusterPositions[t];
                    if (cp.W == 0) continue;
                    clusterPos[t] = FromHalf3(cp);
                    (int, int, int) key = CCell(clusterPos[t]);
                    if (!clusterGrid.TryGetValue(key, out List<int> l)) clusterGrid[key] = l = new List<int>();
                    l.Add(t);
                }

                foreach ((RadiosityGeometry.Instance instance, int islandId, int wCloneIndex,
                          int rx, int ry, int rw, int rh, SurfaceTexel[] texels,
                          int[] slotForTexel, Dictionary<int, double> retailMass) in work)
                {
                    if (wCloneIndex != cloneIndex) continue;

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
                            if (MoverPurelyTranslated(level, settings, texels[t].MoverIndex, 2.0f))
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
                                                             clusterPos[t2] + n2 * settings.ProbeSurfaceOffset, n2,
                                                             settings, t, t2))
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
                            int keep = Math.Min(InfluencesPerProbe, Math.Min(candidates.Count, settings.InfluencesPerSurfaceProbe));
                            StratifyByDistance(candidates, keep, t, settings);

                            for (int k = 0; k < keep; k++)
                            {
                                ClusterRef(candidates[k].texel, out byte cx2, out byte cy2);
                                byte wgt = InfluenceWeight(candidates[k].distance, candidates[k].cosProduct, settings);
                                WriteInfluence(clone, slot * InfluencesPerProbe + k, cx2, cy2, wgt);
                            }
                            for (int k = keep; k < InfluencesPerProbe; k++)
                                WriteInfluenceWeight(clone, slot * InfluencesPerProbe + k, 0);

                            // Self-calibration: this very slot's retail exp mass IS the energy
                            // budget retail assigned this surface - match it.
                            if (retailMass.TryGetValue(slot, out double target) && target > 0)
                            {
                                double ours = 0;
                                for (int k = 0; k < keep; k++)
                                {
                                    byte wgt = ReadInfluenceWeight(clone, slot * InfluencesPerProbe + k);
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
                                            byte wgt = ReadInfluenceWeight(clone, slot * InfluencesPerProbe + k);
                                            if (wgt != 0)
                                                WriteInfluenceWeight(clone, slot * InfluencesPerProbe + k,
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

            // ---- pass 3: rebind and leave the census ---------------------------------------
            foreach ((RadiosityGeometry.Instance instance, int islandId, int cloneIndex,
                      int rx, int ry, int rw, int rh, SurfaceTexel[] texels,
                      int[] slotForTexel, Dictionary<int, double> retailMass) in work)
            {
                runtime.InstanceSliceIndices[islandId] = cloneIndex;
                foreach (int m in instance.Movers)
                    deltaMovers.Remove(m);
                deltaInstances.Remove(instance);
            }
            return work.Count;
        }

        private static Vector3 FromHalf3(Vector4u16 v) => new Vector3(FromHalf(v.X), FromHalf(v.Y), FromHalf(v.Z));

        /// <summary>IEEE 754 binary16 decode, the inverse of <see cref="ToHalf"/>.</summary>
        private static float FromHalf(ushort h)
        {
            uint sign = (uint)(h >> 15) & 1;
            int exponent = (h >> 10) & 0x1F;
            uint mantissa = (uint)h & 0x3FF;
            uint bits;
            if (exponent == 0)
            {
                if (mantissa == 0)
                {
                    bits = sign << 31;
                }
                else
                {
                    // Subnormal half: renormalise into the float exponent range.
                    exponent = 127 - 15 + 1;
                    while ((mantissa & 0x400) == 0) { mantissa <<= 1; exponent--; }
                    bits = (sign << 31) | ((uint)exponent << 23) | ((mantissa & 0x3FF) << 13);
                }
            }
            else if (exponent == 0x1F)
            {
                bits = (sign << 31) | 0x7F800000 | (mantissa << 13);
            }
            else
            {
                bits = (sign << 31) | ((uint)(exponent - 15 + 127) << 23) | (mantissa << 13);
            }
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

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
