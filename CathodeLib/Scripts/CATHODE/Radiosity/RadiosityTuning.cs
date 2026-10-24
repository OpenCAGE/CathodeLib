#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Radiosity
{
    /// <summary>
    /// The baker's tuning constants, each at its product value with the reason for it. A value only a full bake reads is
    /// the validated full-bake profile's. What differs between a full bake and the two kinds of delta slice is in
    /// RadiosityBaker.BakeSliceOptions, and the state of one delta bake in <see cref="DeltaBakeState"/>. Changing a value
    /// changes the output: re-run the gate and the parity captures.
    /// </summary>
    internal static class RadiosityTuning
    {
        // ---- Rects and slices ----------------------------------------------------------------

        /// <summary>World area (m2) one atlas texel covers before the UV-coverage divide. With that divide, rect totals land
        /// on retail's: Solace ships 28,469 surface probes, and overshooting forces an extra slice.</summary>
        public const float MetresSquaredPerTexel = 1.27f;

        /// <summary>Smallest instance rect in texels. Retail never goes below 2: a 1-texel rect collapses the shader's
        /// half-texel inset to zero width.</summary>
        public const int MinInstanceRect = 2;

        /// <summary>Largest instance rect in texels: retail's MODEL_PARAMS rect edges run 2-40 (Solace, BSP_TORRENS).</summary>
        public const int MaxInstanceRect = 40;

        /// <summary>
        /// Atlas texels a spatially split slice (and a probe-only delta slice group) may hold, of the 128x128 = 16,384. Each
        /// slice thins its own input probes, so every extra slice adds a boundary where two probe sets overlap and the
        /// densities add (Solace at 12,000: 4 slices, 215 doubly covered cells; retail: 3 and 85). 8000 leaves the delta
        /// donor shell headroom (H58).
        /// </summary>
        public const int MaxTexelsPerSlice = 8000;

        /// <summary>Backstop on the spatial split's slice count (retail ranges from 1 to 15); a slice forced past
        /// <see cref="MaxTexelsPerSlice"/> cannot give all its texels input probes.</summary>
        public const int MaxSlices = 32;

        /// <summary>
        /// Texel ceiling of a group taken from retail's own slice grouping before it is split anyway: retail already fitted
        /// that group into one atlas, and splitting it separates islands retail lit together (SCI_Hub's first group asks for
        /// 13,809). Also the atlas space a delta group and its donor shell may fill.
        /// </summary>
        public const int MaxTexelsPerRetailSlice = 15800;

        /// <summary>Transparent movers with less lightmapped area (m2) than this stay in their composite's island on a full
        /// bake (RadiosityGeometry.SplitTransparentIslands). Retail splits off even 0.01-0.02 m2 glass (VDU screens, door
        /// lenses) but barely probes it; split off, it brightened rooms (ChallengeMap3 cabins, ChallengeMap4 cam18).</summary>
        public const float IslandSplitMinArea = 0.05f;

        /// <summary>The share of its lightmap-UV cells a transparent mover may overlap with an island of transparent movers
        /// and still join it (64x64 raster); retail's rule.</summary>
        public const float IslandSplitMaxOverlap = 0.05f;

        // ---- Probes and albedo ---------------------------------------------------------------

        /// <summary>Probe and ray-end offset (m) off the surface along the normal, against self-hits. Larger offsets put
        /// probes behind Cathode's thin panels (0.06 only moved Solace's no-influence probes 26.5% -> 24.8%: those are
        /// enclosed, not self-hitting).</summary>
        public const float ProbeSurfaceOffset = 0.02f;

        /// <summary>Stored input-probe and surface-probe positions are lifted this far (m) along their normals: retail's sit
        /// ~3 cm and ~8 cm in front of the surface (18 Sep 2026). On the mesh, the runtime depth test against the geometry
        /// that owns them is a coin toss.</summary>
        public const float InputProbeLift = 0.03f;
        public const float SurfaceProbeLift = 0.08f;

        /// <summary>
        /// Poisson-disc spacing (m) of the input probes scattered over a full bake's surfaces, and of the texel-placed ones.
        /// Retail's are Poisson-disc (Solace nearest-neighbour mean 0.455 m, cv 0.37). 0.52 lands on retail's count on
        /// ChallengeMap3/4 (18k/23k) and measured best over seven levels (21 Aug); denser couples into loop gain, so match
        /// retail's count and do not exceed it.
        /// </summary>
        public const float InputProbeSpacing = 0.52f;

        /// <summary>Dart-throwing candidates per m2 before the thinning pass (retail covers a level in hundreds of thousands);
        /// the cost is linear and the survivors are bounded by <see cref="InputProbeSpacing"/>.</summary>
        public const float InputProbeCandidatesPerSquareMetre = 60.0f;

        /// <summary>Stacked-chart rasterisation (nested skins): how far (m) along a live texel's normal a later near-parallel
        /// triangle may sit in front of it and still take the texel (RadiosityBaker.RasteriseByUv).</summary>
        public const float PreferFrontmostSkinMaxDepth = 3.0f;

        /// <summary>Albedo stored for an emissive surface the bake leaves dark (its glow art is not a reflectance): retail
        /// stores 5-11 of 255 (RadiosityBaker.ApplyEmissiveAlbedoConvention).</summary>
        public const float EmissiveNoPriorAlbedo = 0.04f;

        /// <summary>Linear albedo of a material whose diffuse map cannot be decoded: retail's albedo table averages 0.36 on
        /// Solace and 0.40 on BSP_TORRENS.</summary>
        public const float FallbackAlbedo = 0.4f;

        /// <summary>Albedo cap, so a surface cannot amplify energy across bounces: retail's albedo table reaches exactly 1.0
        /// and never exceeds it (130,280 BSP_TORRENS samples).</summary>
        public const float MaxAlbedo = 1.0f;

        /// <summary>Longest edge (texels) of the diffuse mip the sampler decodes: a level references ~220 diffuse maps with a
        /// median persistent size of 128, so 256 keeps nearly every map whole in ~13 MB of cache.</summary>
        public const int AlbedoTextureMaxEdge = 256;

        /// <summary>Triangles with an edge longer than this (m) are dropped as decode outliers that would blow out the BVH
        /// bounds, as the navmesh soup does.</summary>
        public const float MaxTriangleEdge = 256.0f;

        // ---- Visibility (against the collision shell; see RadiosityBaker.BuildCollisionOccluders) ----

        /// <summary>Ray origin nudge (m) against re-hitting the source triangle.</summary>
        public const float RayEpsilon = 0.001f;

        /// <summary>How far (m) a surface point may be lifted along its normal to clear the collision hull it starts inside:
        /// without it 59% of probes found no influence against 46% (1.5 m measured no better).</summary>
        public const float OccluderProjectionRange = 0.5f;

        /// <summary>How far (m) a visibility ray is pulled in at each end against the collision proxy. Both ends lie on render
        /// meshes, so the target's own hull blocked the ray just before it arrived: of Solace's facing pairs, 4.1% were
        /// visible through the render meshes but 1.3% through collision end to end.</summary>
        public const float OccluderEndpointSlack = 0.35f;

        /// <summary>Largest share of a link, per end, the endpoint slack may skip. A flat slack left a 1 m link tested over its
        /// middle 0.30 m only, and 68.1% of our 0-1 m links passed through render geometry against retail's 34.2%.</summary>
        public const float OccluderSlackFraction = 0.15f;

        // ---- Influences ----------------------------------------------------------------------

        /// <summary>Influence and cross-slice fixup reach (m): retail's longest influence on Solace is 30.6 m, and 0.20% of
        /// its links are beyond 20 m. Coarse clusters reach twice this.</summary>
        public const float MaxInfluenceDistance = 32.0f;

        /// <summary>
        /// A delta slice's influence weight curve, W0 x (D0 / (D0 + d))^K (RadiosityBaker.InfluenceWeight; a full bake's
        /// bytes are the cut's): retail's own calibration, within +-3 bytes of its per-distance-band means. W0 is a loop
        /// gain near the transport's knee, not a brightness slider: on ChallengeMap4, W0 188/200/240/262 moved the fit's
        /// intercept -10/-4/+39/+221.
        /// </summary>
        public const float InfluenceCurveW0 = 227.0f;
        public const float InfluenceCurveD0 = 2.43f;
        public const float InfluenceCurveK = 0.46f;

        /// <summary>Fewest probes a coarse-cluster bucket needs (RadiosityBaker.BuildCoarseClusters).</summary>
        public const int CoarseClusterMinMembers = 3;

        /// <summary>
        /// Coarse-cluster cell sizes (m), largest first so the far-carrying patches take slots first: the profile's ladder
        /// "2,4,8,16" as its parse sorted it. Retail nests each probe in up to 8 patches of doubling size, and a room gathers
        /// from ~1000 distinct patches (Solace corridor) where two rungs give ~350.
        /// </summary>
        public static readonly float[] CoarseClusterRungs = { 16.0f, 8.0f, 4.0f, 2.0f };

        // ---- Delta scatter (RadiosityBaker.BuildScatterListLocal; a full bake's scatter is the cut's) ----

        /// <summary>Radius (m) of a probe's local scatter neighbourhood. Retail's per-probe source counts run p10 3 / p50 6 /
        /// p90 8; swept 1.0 / 1.25 / 1.5 / 2.5 over three levels (2.5 saturates every probe at the cap).</summary>
        public const float LocalScatterRadius = 1.25f;

        /// <summary>Outer radius (m) of the mid scatter band, and the floor of the long-link tail. Retail's source distances
        /// on ChallengeMap4 run p50 0.73 m, p90 2.5 m, where a flat ball gave the same median and no tail.</summary>
        public const float LocalScatterReachRadius = 3.0f;

        /// <summary>Share of destination probes, by a per-probe dither, given a long (&gt; 3 m) link: retail 25-32% per slice
        /// (Solace, CM3). Those minority links make the scatter graph percolate level-wide.</summary>
        public const float ScatterFarBandFraction = 0.27f;

        /// <summary>A second per-probe dither, independent of the far one, that also draws a link from the long-link tail
        /// (capped at twice ScatterMaxLinkDistance, 12 m): ~1% of retail's links exceed 6 m, and around Solace's cold cam13
        /// room those import light across the room boundary.</summary>
        public const float ScatterUltraFarFraction = 0.06f;

        /// <summary>Mean excess (m) over the reach floor of the long-link tail: retail's long links are one distribution,
        /// floor + Exp(1.4) (12 retail slices: p10 3.1, p50 3.8-4.2, p90 5.9-7.8).</summary>
        public const float ScatterLongLinkMean = 1.4f;

        /// <summary>Source clusters kept per input probe: retail averages about six and never exceeds eight.</summary>
        public const int MaxScatterTargetsPerProbe = 8;

        // ---- Volume probes -------------------------------------------------------------------

        /// <summary>Volume probe hash cell size (m) of full-bake and lightmap-delta slices, retail's (BSP_TORRENS slice 0 is
        /// 37x4x22 cells over a 74x8x44 m box).</summary>
        public const float VolumeProbeCellSize = 2.0f;

        /// <summary>
        /// Volume probe hash cell size (m) of a probe-only delta slice. The engine derives cell size from the hash's own
        /// AABB and dims (proven in game), and a finer grid shrinks the world span of its 8-cell probe blend: a dynamic crate
        /// read grey in a pitch-black room at 2 m cells and correctly black at 1 m.
        /// </summary>
        public const float DeltaVolumeProbeCellSize = 1.0f;

        /// <summary>Hash subdivisions per level: retail's value on every populated hash.</summary>
        public const uint VolumeProbeSubdivsPerLevel = 3;

        /// <summary>
        /// A hash cell no probe falls inside borrows the nearest (preferably visible) probe within this reach (m). The
        /// engine's 8-cell blend does not renormalise around missing items, so a mid-air cell otherwise blends black;
        /// retail's items resolve probes up to ~2.6 m away. At 3.0 the item count exploded 10x and lit rooms went black:
        /// keep it at or under ~1.2.
        /// </summary>
        public const float VolumeProbeFillReach = 1.20f;

        /// <summary>Rays per cell of a volume probe's 8x8 visibility face. The stored byte is floor(n x 255 / 27), so 27 is
        /// the count that reaches all 28 levels.</summary>
        public const int VolumeProbeVisSamplesPerCell = 27;

        /// <summary>How far (m) a visibility ray looks before counting its direction as open.</summary>
        public const float VolumeProbeVisRange = 12.0f;

        // ---- Lights --------------------------------------------------------------------------

        /// <summary>Fewest input probes an emitter with no retail prior is sampled at: 53% of retail's light slices hold two
        /// items and 1-2% one (60% held one before this).</summary>
        public const int MinProbesPerEmitter = 2;

        /// <summary>Radius (m) around an emitter within which its input probes sample it, past the minimum count: reproduces
        /// retail's spread (53% two items, 25% three, 11% four).</summary>
        public const float EmitterSampleRadius = 0.64f;

        /// <summary>
        /// How far (m) an emitter may be from a slice's live texels and still be injected into that slice by the
        /// unbaked-emitter rescue. It must span a room: a delta chunk held one duplicated room's walls while its fixtures
        /// landed 20 m away in the next chunk, and the old 3 m reach left that room pitch black (H58, -0.73).
        /// </summary>
        public const float UnbakedEmitterReach = 20.0f;

        /// <summary>Strength of an emitter whose material does not remap EMISSIVE_MULT and whose mover multiplier is zero:
        /// retail stores Scale 15 (exactly 1.0) on 69.2% of such lights.</summary>
        public const float DefaultEmissiveMultiplier = 1.0f;

        /// <summary>Clamp on that fallback mover multiplier, which is not retail's Scale source (SCI_Hub stores 4-10x movers
        /// at Scale 7-15). A floor of 0.5 blew out SCI_Hub's dim ceiling fixtures (cam9 rmse 26.2 -> 40.3).</summary>
        public const float EmissiveMultiplierFloor = 0.0f;
        public const float EmissiveMultiplierCeiling = 1.5f;

        /// <summary>Multiplies every delta slice surface light's Weight (a float multiply, then Math.Round): the delta path's
        /// measured scale (H58). A full bake applies none.</summary>
        public const float DeltaLightWeightScale = 0.80f;

        /// <summary>
        /// Full bakes: each light slice's items move onto the probes of its slice that score best by -(distance to the
        /// emitter's nearest emissive triangle) + this x (probe normal . emissive normal), with no visibility test
        /// (RadiosityBaker.ApplyEmitterSurfacePlacement). Retail's rule is "the K probes nearest the emissive triangles";
        /// 0.5 reproduces 48.8% of retail placement's probes against our sampler's 32% (1 Oct 2026).
        /// </summary>
        public const float EmitterSurfacePlacement = 0.5f;

        // ---- Doors ---------------------------------------------------------------------------

        /// <summary>How far (m) either side of a barrier door transfer pairs are looked for.</summary>
        public const float DoorTransferRadius = 3.0f;

        /// <summary>Transfers per door; retail averages about six.</summary>
        public const int MaxTransfersPerDoor = 10;

        /// <summary>
        /// Door transfers per slice; a door that would pass it is dropped whole. The engine's door buffer overruns past
        /// retail's envelope (at most 161 in any slice of five levels): our ChallengeMap3 bake shipped 440/731 and
        /// heap-corrupted in RADIOSITY::destroy at every level close (28 Aug 2026).
        /// </summary>
        public const int DoorTransfersPerSliceCeiling = 160;

        // ---- Delta bake ----------------------------------------------------------------------

        /// <summary>World grid spacing (m) on which probe-only delta slices sample their surfaces
        /// (RadiosityBaker.AppendProbeOnlySlice).</summary>
        public const float DeltaProbeSpacing = 0.5f;

        /// <summary>Probe-only instances spanning more than this (m) on any axis are dropped: the exterior hull and skybox
        /// meshes sprawl 50-100 m outside playable space and blow the slice hashes past the engine's item budget.</summary>
        public const float DeltaMaxInstanceSpan = 45.0f;

        /// <summary>Reach (m) around the delta content within which retail islands join its slice as donors: bounce is
        /// dominated by nearby surfaces (form factor ~ 1/d^2), and scatter links cap at 6 m (H58).</summary>
        public const float DeltaDonorShellRadius = 25.0f;

        /// <summary>Atlas texels the donor shell may spend (delta islands allocate first and nearest donors win).</summary>
        public const int DeltaDonorTexelBudget = 8192;

        /// <summary>Per-donor rect edge clamp: donors only feed the cluster field, so they can run coarser than render
        /// resolution, and a level-spanning floor island cannot eat the budget.</summary>
        public const int DeltaDonorMaxRectDim = 24;

        /// <summary>Calibration shift (bytes, exponent domain: +24 is about x1.7 rendered) for a delta probe with fewer than
        /// four retail probes within 4 m (RadiosityBaker.CalibrateDeltaEnergy). From the CM7 moved shelving, which rendered
        /// ~0.6x retail uncalibrated.</summary>
        public const int DeltaInfluenceWeightBias = 18;

        /// <summary>When the atlas-demand chunker returns null, a lightmap delta of more movers than this is chunked by zone
        /// into several appended slices: one AppendDeltaSlices call bakes one slice, and a whole added environment overflows
        /// it (H12: 2,507 islands parked unmapped-dark).</summary>
        public const int DeltaLightmapChunkMovers = 3000;

        /// <summary>Texels per atlas-demand chunk: 75% of the 16,384-texel atlas (RadiosityBaker.ChunkDeltaByAtlasDemand).
        /// At 95% the skyline packer shrinks real islands to fit.</summary>
        public const int DeltaChunkTexelBudget = 12288;

        /// <summary>
        /// Per-probe cap for a delta slice's fixups into retail clusters: the full 32. A delta probe cut out of a
        /// room-coherent slice gets all its bounce from the room - a flat island (a moved wall, a shelving face) has no
        /// in-slice links, so the fixups are its entire diet. On the CM3 moved vent wall, 24 links carried gain 2324 against
        /// the room's 3110 (the 24/32 ratio) and 4 links rendered 0.64x retail: link count is the knob, not the weight curve.
        /// </summary>
        public const int DeltaCrossSliceFixupsPerProbe = 32;

        // ---- The retail-rule cut (RadiosityBaker.RebuildLinksRetailCut; full bakes) ----------

        /// <summary>Largest cut-tree patch (members): retail's is 128, and it keeps a probe's memberships at eight.</summary>
        public const int CutMaxMembers = 128;

        /// <summary>Reach (m) of the cut: a patch or member farther than this from the receiver is not linked.</summary>
        public const float CutMaxDistance = 24.0f;

        /// <summary>
        /// Members-weighted per-probe gain (the engine decode, RadiosityBaker.ReadInfluenceWeight): each slice's median is
        /// shifted to CutGainMedian and each probe capped at CutGainCap. Retail's per-probe sum of gain x members is p50
        /// 0.64-0.78 with a hard cap at 0.957 on every level measured (Solace, CM1, CM4, CM16, SCI_Hub, Torrens); a probe past
        /// 1.0 is a local amplifier in the runtime relaxation (an uncapped solve reached 2.96 on ChallengeMap7, which flipped
        /// from dim to saturated white on a 4% perturbation). Both are floats, widened to double where they are read.
        /// </summary>
        public const float CutGainMedian = 0.74f;
        public const float CutGainCap = 0.957f;

        /// <summary>A member is rejected when it lies more than this (as a cosine) behind the receiver's plane: retail's
        /// single-probe links put 23% of members behind it and 13% below -0.2 (recessed lamps above a ceiling).</summary>
        public const float CutReceiverTolerance = 0.1f;

        /// <summary>Distance floor (m) of the byte law only: a nearer link gets the byte of a link at this distance, so a
        /// cut's many near single-probe links do not put most of a probe's gain on the adjacent surfaces.</summary>
        public const float CutByteNearFloor = 2.0f;

        /// <summary>After the trim to 32 links, the highest-priority patch is opened this many times (its children replace
        /// it, the weakest links drop): a partial step toward retail's finer near field.</summary>
        public const int CutForcedOpenings = 8;

        /// <summary>
        /// Hybrid visibility: a probe keeps its no-visibility cut unless the occluded share of its W/d^2 gain is above
        /// CutHybridLeak and below CutHybridLeakMax (a probe beside a wall with a lit room behind it); only those probes are
        /// cut again with visibility, which keeps the blotchiness visibility brings to open levels away. A probe nearly all
        /// occluded is usually enclosed by clutter, and re-cutting it starves it (27 Sep: TECH_MUTHRCORE cam18 21 -> 6 luma,
        /// retail 23). CutHybridLeak is a float widened to double where it is read.
        /// </summary>
        public const float CutHybridLeak = 0.2f;
        public const double CutHybridLeakMax = 0.9;

        /// <summary>The cut's visibility ray endpoints: the receiver and the member input probe are each lifted this far (m)
        /// along their own normals (0.02 before 25 Sep 2026).</summary>
        public const float CutVisOffset = 0.15f;

        /// <summary>The cut's visibility test traces the render meshes and is softened: a link is visible when its centre ray,
        /// or any of CutSoftVisRays rays with both ends jittered up to CutSoftVisJitter (m), is clear (deterministic per
        /// endpoint pair). 28 Sep: Solace 11.49 (collision-occluder centre ray) -> 10.55.</summary>
        public const int CutSoftVisRays = 3;
        public const float CutSoftVisJitter = 0.22f;

        /// <summary>The cut tree's merge cost is the rms radius of the union x (1 + this x (1 - dot of the mean normals)).</summary>
        public const float CutMergeNormalWeight = 4.0f;
    }
}
#endif
