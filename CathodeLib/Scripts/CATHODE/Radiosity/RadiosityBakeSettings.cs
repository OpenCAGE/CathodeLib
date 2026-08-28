#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Tunables for <see cref="RadiosityBaker"/>. Defaults are derived from measurements of the
    /// retail BSP_TORRENS bake (2 slices, ~7k input probes in the larger one, 1377 instances).
    /// </summary>
    public sealed class RadiosityBakeSettings
    {
        // ---- Atlas -------------------------------------------------------------------------

        /// <summary>
        /// Edge length of the per-slice probe textures. The engine's slice format is fixed at
        /// 128, so this is effectively a constant; it exists so the packing maths reads clearly.
        /// </summary>
        public int AtlasSize = 128;

        /// <summary>
        /// World-space area (m2) one atlas texel is expected to cover, before UvCoverageCompensation
        /// scales it up for whatever part of the UV square an instance leaves empty.
        ///
        /// Set so the two together land on retail's total: Solace ships 28469 surface probes, and
        /// overshooting that forces an extra slice, which in turn creates extra slice boundaries -
        /// each slice thins its own input probes, so wherever two overlap the densities add.
        /// </summary>
        public float MetresSquaredPerTexel = 1.27f;

        /// <summary>
        /// Per-island atlas rect sizes taken from retail's own MODEL_PARAMS, keyed by retail
        /// island id: value is [width, height] in texels. When set, an instance whose retail
        /// island id is present uses retail's rect verbatim and the area formula is only a
        /// fallback for islands retail never baked.
        /// </summary>
        /// <remarks>
        /// The area formula's rect totals are wrong PER LEVEL in both directions - measured
        /// against retail's shipped rects (shared islands, texel sums): CM3 -11.5%, CM4 -20%,
        /// Torrens -18%, CM9 -12%, CM14 +5%, Solace +27%. Emitter mass follows rect mass, and
        /// the transport's loop gain follows emitter mass, so no single W0 fits levels whose
        /// emitter mass sits 25% under retail's on one map and 27% over on the next. Rect
        /// under-allocation was also directly the dim-floor mechanism: rendering with bigger
        /// rects (LargeInstanceTexelBoost 0.25) moved ChallengeMap3's intercept -12.6 -> +8.6,
        /// the first lever ever measured to move a deficit level's floor. Copying retail's rect
        /// per island normalises emitter mass to retail's everywhere at once.
        /// The caller harvests these from the pristine level's movers BEFORE instancing rewrites
        /// MODEL_PARAMS (first four floats: w-0.5, h-0.5, x, y).
        /// </remarks>
        public System.Collections.Generic.Dictionary<int, int[]> RetailRectSizes = null;

        /// <summary>
        /// Fill dead atlas cells between rects with cluster-only clones of the nearest live
        /// cell, the way retail's bakes do (retail's live-cluster counts exceed its rect sums by
        /// ~12%, and ChallengeMap4 slice 0 ships a completely full 16384-cell grid).
        /// </summary>
        /// <remarks>
        /// Our skyline packer leaves 12-45% of the grid dead between rects, so what sits beside
        /// any rect's edge is an arbitrary function of packing order. Renders proved sensitive to
        /// exactly that: ChallengeMap3's whole-level fit swings 14 luma between boost exponents
        /// 0.25/0.30/0.35 (intercept +0.0 / -14.5 / -9.6, deterministic) whose only effect is a
        /// one-percent cascade of rect relocations. Filled cells become emitters and input-probe
        /// binding sites but never surface probes, matching retail's counts.
        /// </remarks>
        public bool FillAtlasGutters = false;

        /// <summary>
        /// Delta-bake: keep the level's shipped radiosity wholesale and patch only what the edit
        /// invalidated, instead of regenerating everything. See <see cref="RadiosityPatcher"/>.
        /// Requires the level's RADIOSITY_RUNTIME.BIN to still be a real bake (retail's or a
        /// previous full bake); throws when it carries no slices.
        /// </summary>
        public bool PatchRetailRuntime = false;

        /// <summary>
        /// Pristine MODEL_PARAMS lightmap transforms (first 16 bytes of RENDER_CONSTANTS), keyed
        /// by the mover's resource GUID pair packed as (composite &lt;&lt; 32 | resource). Harvested
        /// by the caller BEFORE instancing and written back by <see cref="RadiosityPatcher"/>:
        /// instancing rebuilds some movers without carrying their lightmap rect, and a rect-less
        /// mover samples a wrong atlas region - ChallengeMap3's vent wall rendered its
        /// neighbouring tube-lights' yellow, a post rendered bunk-light blue, and ceiling pieces
        /// degenerated entirely.
        /// </summary>
        public System.Collections.Generic.Dictionary<ulong, byte[]> RetailModelParams = null;

        /// <summary>
        /// Pristine mover transforms keyed like <see cref="RetailModelParams"/>, harvested by the
        /// caller before instancing. The patcher uses them to detect MOVED movers - which must be
        /// routed through the delta rebake, because their carried MODEL_PARAMS would light them
        /// as they stood at their old location.
        /// </summary>
        public System.Collections.Generic.Dictionary<ulong, System.Numerics.Matrix4x4> RetailTransforms = null;

        /// <summary>
        /// In patch mode, bake added/moved geometry into an appended slice (see
        /// <see cref="RadiosityBaker.AppendDeltaSlices"/>). Off leaves new geometry unlit -
        /// black architecture, fullbright props - as the v1 behaviour did.
        /// </summary>
        public bool PatchBakeDelta = true;

        /// <summary>
        /// In patch mode, light added/moved movers by forcing them onto the engine's DYNAMIC
        /// radiosity path (materials cloned onto real dynamic shader permutations - shipped
        /// twins where they exist, bytecode-patched otherwise - rect zeroed, instance-map rows
        /// dropped; see <see cref="DynamicRadiosityConverter"/>) instead of baking them into a
        /// lightmap delta slice. They then sample the level's live volume probe field at their
        /// pivot, which needs no atlas allocation, no island ids and no transforms records.
        /// Movers the converter cannot fully convert (a static-class element whose pixel shader
        /// lacks the radiosity sampling idiom) fall back to the lightmap delta when
        /// <see cref="PatchBakeDelta"/> is on. Set false to route everything through the
        /// lightmap delta bake instead - that whole path is kept intact.
        /// </summary>
        public bool DeltaDynamicProps = true;

        /// <summary>
        /// With <see cref="DeltaDynamicProps"/>, extend the volume probe field over dynamic delta
        /// content whose pivot sits OUTSIDE every retail volume hash (new rooms, content beyond
        /// the shell): bake it - plus a donor shell - into one appended slice carrying clusters,
        /// lights and its own fine-celled volume hash, and nothing else. No rects, no island ids,
        /// no instance-map rows, no transforms records. See
        /// <see cref="RadiosityBaker.AppendProbeOnlySlice"/>. Content inside the retail field is
        /// never included - retail's own probes light it better than anything we bake.
        /// </summary>
        /// <remarks>
        /// PROVEN DELIVERING (2026-08-24, the shifted-level experiment): with the whole level
        /// moved outside its original probe volumes and every mapped mover forced dynamic, the
        /// appended probe slices light the level in game - the A/B control without them turns a
        /// probe-dependent room pitch black (cam4: luma 14.7 with slices, 0.0 without) while
        /// deferred-dominated rooms are unchanged. The earlier all-black Solace room was the
        /// CONTENT, not the pipeline: a raw-dropped composite's light entities are unpowered,
        /// and a slice whose only energy is entity-gated lights that never come on stays dark
        /// under every path. A properly authored room with wired, powered lights gets lit.
        /// Ambient is dim on the first cut (0.3-0.5x in bounce-dominated rooms; calibration
        /// falls back to the global bias where no retail probes are near) - quality tuning on a
        /// working pipeline.
        /// </remarks>
        public bool DeltaProbeOnlySlice = true;

        /// <summary>
        /// Invoked once per island id the lightmap delta path assigns: (islandId, atlasX,
        /// atlasY, foreignId). The engine samples the atlas through per-island transform
        /// records (RADIOSITY_TRANSFORMS.BIN - Windows Store) and an id at or past that table's 
        /// count misrenders - but the file is not a CathodeLib concern, so the caller owns it and
        /// must persist a record carrying the island's rect origin for every callback
        /// (resetting the grouping fields on foreign ids - see the delta bake tooling).
        /// </summary>
        public System.Action<int, int, int, bool> DeltaIslandRecord = null;

        /// <summary>
        /// Treat the patch census as empty: convert nothing and keep the shipped radiosity
        /// exactly as-is. For rigid whole-level moves, where every lightmap stays valid and
        /// the volume field is translated by the caller instead.
        /// </summary>
        public bool DeltaIgnoreMoves = false;

        /// <summary>
        /// Group probe-only delta slices by the movers' PrimaryZoneID and bin-pack whole
        /// zones into slices, the way retail's own slices keep every room in exactly one
        /// slice. Off falls back to the spatial-band splitter (which can cut rooms across
        /// slices and starves each band at the same texel cap).
        /// </summary>
        public bool DeltaZoneSlices = true;

        /// <summary>
        /// Place probe-only slice surfaces on a world-space grid at
        /// <see cref="DeltaProbeSpacing"/> instead of rasterising the authored lightmap UVs
        /// into area-sized rects. The UV route inherits the charts' packing, whose world
        /// density is nothing like uniform (measured on the F5 whole-level bake: per-2m-cell
        /// density against retail p10 0.38, 136 cells empty or below a third, and 21.5% of
        /// probes were dilation clones stacked at one position). The grid gives every surface
        /// the same spacing - retail's own lattice look - and needs no dilation, so every
        /// probe is a distinct point. Rects become plain slot allocations sized to fit.
        /// </summary>
        public bool DeltaUniformProbes = true;

        /// <summary>World grid spacing in metres for <see cref="DeltaUniformProbes"/>.</summary>
        public float DeltaProbeSpacing = 0.5f;

        /// <summary>
        /// Drop probe-path instances none of whose movers carry a PrimaryZoneID. Instancing
        /// emits zone-less template/particle movers sprawled outside playable space; they
        /// bucket into a fake "zone 0" and build oversized slices whose hash exceeds the
        /// engine's item budget (~6.5k/slice), which rejects the slice wholesale.
        /// </summary>
        public bool DeltaRequireZone = true;

        /// <summary>
        /// Mint one fresh island id per appended probe slice and point its
        /// InstanceSliceIndices entry at the slice. The engine only relights slices that at
        /// least one island references (H3: an unreferenced appended slice rendered its
        /// content completely unlit - lights placed, hash resolving, radiance never computed;
        /// one referencing island lit it, cam7 0.195 -> 0.982). The caller persists a
        /// RADIOSITY_TRANSFORMS record per minted id via <see cref="DeltaIslandRecord"/>.
        /// </summary>
        public bool DeltaMintScheduleIsland = true;

        /// <summary>
        /// Drop probe-path instances whose bounds span more than this many metres on any
        /// axis (0 disables). The exterior hull/skybox meshes sprawl 50-100 m outside
        /// playable space and explode the slice hashes past the engine's item budget;
        /// no playable island approaches this size.
        /// </summary>
        public float DeltaMaxInstanceSpan = 45.0f;

        /// <summary>
        /// Let a mover with no exact light prior inherit the priors of retail movers that share
        /// its resource_id. Duplicated retail content carries a NEW composite_instance_id, so the
        /// exact (instance, resource) key misses for every dupe mover and the whole light stack
        /// falls back to scratch heuristics: colour from the authored EmissiveTint (screens are
        /// blue - CM3's dupe light census read 33% cool against retail's 15%), Weight from the
        /// fitted area model, and retail's per-entity suppression/force-lit truth lost entirely.
        /// resource_id survives duplication; where several retail instances of one fixture share
        /// it their priors are merged (Items-weighted colour, mean flux).
        /// </summary>
        public bool DeltaLoosePriors = true;   //H35 baseline default

        /// <summary>
        /// Treat the absence of a light prior on a fixture whose retail twin we can identify as
        /// retail's DECISION to leave it dark, rather than as missing data. Only meaningful with
        /// <see cref="DeltaLoosePriors"/>, which is what supplies the twin.
        /// </summary>
        /// <remarks>
        /// Measured on the duplicate: retail lights one bunk area with 30 white, 23 grey and 22
        /// warm sources plus red and green status LEDs; we light the same volume with 58 warm and
        /// almost nothing else. The colours are right - every one occurs in retail's own palette -
        /// but we light fixtures retail left dark, and those resolve to the nearest warm fixture
        /// of their class. Note the standing warning in SuppressedByRetail: on SCI_Hub, suppressing
        /// emitters merely because they lack a prior dimmed the whole level. This rule is narrower -
        /// it fires only when a specific retail twin can be located and that twin is dark.
        /// </remarks>
        public bool DeltaTwinSuppression = false;

        /// <summary>
        /// How far an emitter may be from a slice's live texels and still be injected into that
        /// slice by the unbaked-emitter rescue pass. 0 keeps the legacy
        /// <c>max(EmitterSampleRadius x 4, 3)</c>, which is THREE METRES.
        /// </summary>
        /// <remarks>
        /// That default is fine when a slice is a whole room, because the room's emitters are
        /// inside it. It fails on appended delta slices, which are chunked by mover budget rather
        /// than by room: the chunker put one duplicated room's walls in slice 8 and its ceiling
        /// fixtures in slice 9, the fixtures were 20 m from slice 8's texels, nothing was rescued
        /// past 3 m, and the room rendered pitch black while the identical room next door - whose
        /// fixtures happened to land in the same chunk - rendered at parity. A slice cannot be lit
        /// by another slice's lights, so the reach has to cover the room, not the fixture.
        /// </remarks>
        public float UnbakedEmitterReach = 20.0f;   //H58 default: was ~3m, the single biggest win (-0.73)

        /// <summary>
        /// Directory to write one CSV per appended delta slice listing the movers actually in its
        /// bake set, with positions and island ids. Null disables. Diagnostic only: which slice a
        /// room's geometry really landed in cannot be inferred from the instance map, because the
        /// map records the island a mover is BOUND to, not the slice it was RASTERISED into - and
        /// the black-room bug is precisely a disagreement between those two.
        /// </summary>
        public string DeltaSliceMemberDir = null;

        /// <summary>
        /// Target atlas occupancy for a delta lightmap chunk, 0..1. Chunks are sized by the atlas
        /// TEXELS their islands actually need rather than by mover count, and balanced so no chunk
        /// runs hot. 0 keeps the legacy mover-count chunking.
        /// </summary>
        /// <remarks>
        /// Mover count is a poor proxy for texel demand - it varies about 2x per zone between
        /// room geometry and prop clutter - so a mover-count cap produced wildly uneven atlases:
        /// 95%, 95%, 88%, 54%, 26% full across five chunks whose total demand would have fitted
        /// at ~71% each. At 95% the skyline packer cannot find a contiguous 24x24 even with ~860
        /// texels free, so it shrinks a real island one texel at a time; the duplicated reception
        /// room was ground from 24x24 down to 7x7 that way and rendered black. Donors already
        /// yield to real islands and are dropped first, so this is purely real-island pressure.
        /// </remarks>
        public float DeltaAtlasFillTarget = 0.75f;   //H35 baseline default: demand-balanced chunking

        /// <summary>
        /// Apply retail's albedo convention on emitting surfaces at the texel level: a mover with
        /// a light prior stores the LIGHT's colour as albedo over its whole fixture (retail
        /// CEILING_HZDLAB ~(180,175,164) = the room's lights, our diffuse read (23,22,22)), and an
        /// emissive surface with NO prior stores near-black, not its glow art - retail's probes on
        /// CM3's welder access panels read (5..11) flat where our diffuse sample of WELDER_BLUE
        /// read ~(4,52,128); those panels alone were 560 of the dupe's 1299 blue-shifted probes
        /// and the visible blue-green cast on everything near them.
        /// </summary>
        public bool EmissiveAlbedoConvention = true;   //H35 baseline default

        /// <summary>Albedo (0..1) stored for emissive surfaces with no light prior when
        /// <see cref="EmissiveAlbedoConvention"/> is on. Retail stores 5..11 of 255.</summary>
        public float EmissiveNoPriorAlbedo = 0.04f;

        /// <summary>
        /// How close an unlit mover's origin must be to a lit one's to inherit its light colour
        /// under <see cref="LightColourProbeAlbedoSiblings"/>. A fixture's housing is a separate
        /// mover from its emissive panel and carries no prior of its own.
        /// </summary>
        public float LightColourSiblingRadius = 0.5f;

        /// <summary>
        /// Split delta movers between the two delta paths by bakeability: movers the lightmap
        /// geometry collector can bake go down the LIGHTMAP delta route (appended lightmap
        /// slices render their pages directly and lit rooms the probe path never could - H12
        /// was cam9's first light in any configuration); only unbakeable dynamic-class movers
        /// are converted and probe-sliced. Off = the old behaviour (everything forced dynamic).
        /// </summary>
        public bool DeltaHybridSplit = false;

        /// <summary>
        /// When the lightmap delta has more movers than this, chunk it by zone into multiple
        /// appended slices (AppendDeltaSlices bakes one slice per call and a whole added
        /// environment overflows it - H12: 2,507 islands parked unmapped-dark). 0 disables.
        /// </summary>
        public int DeltaLightmapChunkMovers = 3000;

        /// <summary>
        /// In-range retail island ids the delta paths may REPOINT when the id well runs dry
        /// (CM3: 0 gaps, 53 harvestable duplicates against ~2,900 delta islands). Consumed by
        /// the probe path's slice-scheduling islands and as each lightmap chunk's guaranteed
        /// shared-overflow id - without one, a chunk's islands go beyond-range, which the
        /// engine provably reads as garbage. Fill with ids of islands invisible from
        /// anywhere that matters; their retail movers will sample wrong data.
        /// </summary>
        public System.Collections.Generic.Queue<int> DeltaSacrificialIslands = new System.Collections.Generic.Queue<int>();

        /// <summary>
        /// Mint every delta island a FRESH id by growing InstanceSliceIndices, bypassing the
        /// whole scavenger (gaps / steal / duplicate-twin harvest / shared overflow).
        ///
        /// The scavenger exists to satisfy one claim: that the engine sizes its per-island
        /// state from RADIOSITY_TRANSFORMS.BIN's count and ignores appended records. That file
        /// is DEBUG SPEW - the game never opens it (confirmed 2026-08-25) - so no such sizing
        /// can happen. The engine's per-island rect comes from the mover's own MODEL_PARAMS
        /// (the VS LightmapTransform mad at cb offset 64), and the only island-indexed table it
        /// reads is InstanceSliceIndices here, which is count-prefixed and written by us.
        ///
        /// Growing it is therefore the natural allocation, and it also stops the harvester
        /// repointing RETAIL instance-map rows and overwriting RETAIL movers' rects - collateral
        /// damage that lands on geometry the delta never touched.
        /// </summary>
        public bool DeltaGrowIslandIds = false;

        /// <summary>
        /// OUT: (island id, resource) scheduling rows the probe path wants in the instance
        /// map. The patcher adds them AFTER the lightmap delta writes its real rows, so a
        /// resource's first row - the one rendering follows - stays its real rect binding,
        /// and the scheduling row only feeds the relight walk. Adding them first blackened
        /// whatever the row bound (H16's doorway).
        /// </summary>
        public System.Collections.Generic.List<(int island, object resource)> DeltaPendingScheduleRows =
            new System.Collections.Generic.List<(int, object)>();

        /// <summary>
        /// OUT: island ids minted by <see cref="DeltaMintScheduleIsland"/> during this bake.
        /// The dynamic converter preserves these islands' instance-map rows through row
        /// dropping - without them the schedule rows die before reaching disk (H5).
        /// </summary>
        public System.Collections.Generic.List<int> DeltaMintedIslands = new System.Collections.Generic.List<int>();

        /// <summary>
        /// Volume hash cell size (m) for the probe-only delta slice. Retail bakes 2.0 everywhere,
        /// but the engine derives cell size from the hash's own AABB and dims (proven in-game by
        /// re-encoding retail's hashes at 2x), and a finer grid shrinks the world-space span of
        /// the engine's 8-cell probe blend - the mechanism that made a dynamic crate read grey in
        /// a pitch-black room at 2 m cells and correctly black at 1 m.
        /// </summary>
        public float DeltaVolumeProbeCellSize = 1.0f;

        /// <summary>
        /// In patch mode, re-encode the KEPT retail slices' volume hashes at this multiple of
        /// their grid resolution (same AABB, each fine cell inheriting its parent's probe).
        /// 0 or 1 disables. Appended delta slices are never touched - they are born fine-celled.
        /// </summary>
        /// <remarks>
        /// OFF by default, and the reason is a real lesson: inheritance upsampling does not add
        /// probes, it REMOVES blend diversity. The engine averages the 8 cells around an object's
        /// pivot; at 2 m those are 8 distinct probes, but the 8 fine 1 m cells mostly inherit
        /// from one or two parents, so a mid-room prop reads a single probe's value raw. That is
        /// what fixed the pitch-black-room crate (ratio 1.141 -> 0.989 - it stopped blending in
        /// lit texels from 4 m away) and EXACTLY what darkened lit-room crates (measured on the
        /// CM3 16-crate round: several crates near-silhouette under 2x where the 2 m grid lit
        /// them warmly; rebind picks nearest-by-distance and collapses the same way). Genuine
        /// densification needs per-fine-cell probe SELECTION - nearest VISIBLE texel with the
        /// baker's anti-self-shadow preference, correct per-cell vis entries - i.e. re-running
        /// the cell binding at the fine grid, not inheriting or nearest-snapping. The format and
        /// the engine are proven ready for it (a 2x re-encode renders, MEAN 4.88-4.97 vs retail).
        /// </remarks>
        public int VolumeHashUpsampleFactor = 0;

        /// <summary>
        /// When upsampling, REBIND each fine cell to whichever probe of its 3x3x3 coarse
        /// neighbourhood sits nearest the fine cell's centre (probe positions resolved through
        /// the slice's mangle map), instead of inheriting the parent's probe verbatim - genuine
        /// densification from the existing visibility-vetted probe set. Occupancy is unchanged
        /// either way. Set false for the pure grid subdivision.
        /// </summary>
        public bool VolumeHashRebind = true;

        /// <summary>
        /// Drop converted movers' RADIOSITY_INSTANCE_MAP rows (retail's dynamic convention).
        /// Rows on a dynamic mover are a benign mixed state (the wholesale experiments kept the
        /// whole map and rendered fine); set false for whole-level conversions, where dropping
        /// nearly every row is equivalent to the catastrophic map clear that collapses the
        /// retail slices' relight (MEAN 28.87).
        /// </summary>
        public bool DeltaDropInstanceMapRows = true;

        /// <summary>
        /// Register ONE anchor island per appended probe-only slice: repoint an in-range island
        /// id (one that a converted mover's kept instance-map rows already bind) at the new
        /// slice via InstanceSliceIndices. Tests / satisfies the hypothesis that the engine only
        /// schedules a slice's relight when at least one mapped island belongs to it. Requires
        /// DeltaDropInstanceMapRows=false so the anchor's rows exist.
        /// </summary>
        public bool DeltaProbeAnchorIslands = false;

        /// <summary>
        /// Added to a delta probe's position before querying RETAIL probes during exp-mass
        /// calibration: a rigidly-moved group (a shifted room, a shifted level) references the
        /// retail lighting of the place it CAME FROM instead of the flat bias fallback. The
        /// patcher sets this automatically when the moved census shares one common translation.
        /// </summary>
        public System.Numerics.Vector3 DeltaCalibrationOffset = System.Numerics.Vector3.Zero;

        /// <summary>
        /// Scale on the probe-only slices' atlas rects. Probe-slice content is never sampled
        /// through rects - the rects only allocate CLUSTER texels for the standing field - so
        /// large edits can run coarser to fit fewer slices (0.5 quarters the texel spend).
        /// </summary>
        public float DeltaProbeRectScale = 1.0f;

        /// <summary>
        /// Use <see cref="RetailRectSizes"/> as a per-dimension FLOOR under the formula instead
        /// of verbatim: an island's rect is never smaller than retail's, but the formula (and
        /// <see cref="LargeInstanceTexelBoost"/>) may size it larger. Measured because verbatim
        /// retail rects gave Torrens its best result while returning ChallengeMap3's dim floor -
        /// the boost-fixed levels need certain rects larger than retail's own.
        /// </summary>
        public bool RetailRectSizesAsFloor = false;

        /// <summary>
        /// Place input probes ON live atlas texels (Poisson-thinned) instead of scattering them
        /// freely over the surfaces. Retail is texel-coincident: ~90% of its input probes sit at
        /// exactly a cluster texel's position, each carrying a zero-distance scatter self-pair
        /// with that cluster. The delta patch path turns this on; the full-bake default stays
        /// off until it is re-validated against the campaign baselines.
        /// </summary>
        public bool InputProbesOnTexels = false;

        /// <summary>
        /// Multiplier on a DELTA slice's influence weights - base links and cross-slice fixups
        /// both - applied after the bake. The weight byte is EXPONENT-domain: a x1.35 multiplier
        /// TRIPLED the rendered output (each +32 bytes is roughly a doubling), so calibration
        /// lives in the additive <see cref="DeltaInfluenceWeightBias"/>; this multiplier stays
        /// at 1 outside experiments.
        /// </summary>
        public float DeltaInfluenceWeightScale = 1.0f;

        /// <summary>
        /// Additive byte bias on a delta slice's influence and fixup weights. Exponent domain:
        /// +24 is roughly x1.7 rendered. Calibration from the CM7 moved shelving, whose computed
        /// weights rendered the moved family at ~0.6x retail while its static diet measured at
        /// or above retail on every file-side metric.
        /// </summary>
        public int DeltaInfluenceWeightBias = 18;

        /// <summary>
        /// Per-probe calibration: match each delta probe's exponent-domain weight mass to the
        /// median of the retail surface probes within 4m, falling back to the global bias where
        /// no retail neighbours exist. Off = apply the global bias/scale uniformly instead.
        /// </summary>
        public bool DeltaMatchRetailExpMass = true;

        /// <summary>Slide a purely-translated island's retail radiosity data to its new position
        /// in place (keeping its retail slice, rect, diets, scatter and instance-map rows) instead
        /// of re-baking it into the appended slice, whose fixup-fed probes saturate at about half
        /// a native diet. Rotated, scaled or partially-moved islands still re-bake.</summary>
        public bool DeltaTranslateMovedIslands = true;

        /// <summary>
        /// Graft edited retail-bound islands into a BYTE-CLONE of their room's retail slice
        /// instead of the appended bake. The clone's radiance field is retail's own (an appended
        /// byte-clone relights at 0.96x parity - the slicedup control), so the island's fresh
        /// diets gather real retail energy in-slice: no fixups, no donor field to calibrate.
        /// Only the island's own rect region is re-rasterised inside the clone; the island keeps
        /// its retail id, rect coordinates and instance-map rows, so its movers leave the delta
        /// census and the patcher restores their retail MODEL_PARAMS untouched. Content with no
        /// retail island (genuinely new) falls through to the appended-slice path.
        /// </summary>
        public bool DeltaGraftRetailSlices = true;

        /// <summary>
        /// Bake a shell of the surrounding RETAIL geometry into the appended delta slice as
        /// cluster-only radiance donors, so the delta islands' probes gather the room's bounce
        /// from native in-slice diets instead of cross-slice fixups. The fixup path is the delta
        /// bake's ceiling: the engine's fixup gather saturates at roughly half a native diet
        /// (CM9 rack: 0.38x retail with retail's own cloned weights, 0.53x with every weight
        /// byte at 255), and no weight calibration can close it. Donor movers are never written
        /// to - no MODEL_PARAMS, no instance-map rows - they exist only as emitters inside the
        /// delta slice's data.
        /// </summary>
        public bool DeltaDonorShell = true;

        /// <summary>World-space reach (m) around the delta content's bounds from which retail
        /// islands are pulled in as donors. Bounce energy is dominated by nearby surfaces
        /// (form factor ~ 1/d²); scatter links cap at 6m.</summary>
        public float DeltaDonorShellRadius = 25.0f;   //H58 default

        /// <summary>
        /// Every mover in the WHOLE lightmap delta, when that delta is appended in more than one
        /// chunk. Null falls back to the per-call chunk, which is correct for a single-chunk delta.
        /// </summary>
        /// <remarks>
        /// <see cref="RadiosityBaker.AppendDeltaSlices"/> bakes ONE slice per call and only sees its
        /// own chunk's movers, so it cannot otherwise tell another chunk's delta geometry from the
        /// surrounding retail. It scored the former as DONOR material - and a donor is live for
        /// light injection while nothing renders from it - so a duplicated room's emitters deposited
        /// their light into a slice holding only a cluster-only copy of that room. Measured on CM3:
        /// the cam9 locker door is a 2x3 MEMBER in slice 11 carrying 1-2 lights (38.8 wLuma) while
        /// 5-6 lights (~110 wLuma) landed on its 24x24 DONOR copy in slice 12; under zone chunking
        /// cam3's slice 11 held 15 lights and ZERO probes. It also spends the donor budget, which is
        /// meant for the surrounding level, on the delta's own geometry.
        /// </remarks>
        public System.Collections.Generic.HashSet<int> DeltaAllMovers = null;

        /// <summary>
        /// Resources retail's own RADIOSITY_INSTANCE_MAP lists, keyed
        /// <c>(composite_instance_id &lt;&lt; 32) | resource_id</c>. When set, nothing outside it is
        /// lightmapped. Null disables the check (required for genuinely new content, which retail
        /// obviously never mapped).
        /// </summary>
        /// <remarks>
        /// Retail is the authority on which resources may carry a lightmap, and no predicate we own
        /// reproduces it. <see cref="RadiosityGeometry"/>'s composite filter admits a whole
        /// composite once any one mover asks for RADIOSITY_STATIC; on CM3 that surplus was 28
        /// resources retail never maps - REQUIRED_ASSETS weapons, _PROPS_\PHYSICS templates and
        /// MHQ_LIGHTS_B, i.e. pickups, physics props and light fittings. 22 of the 28 carry
        /// RADIOSITY_STATIC themselves and all 28 are Stationary, so neither flag separates them.
        /// The damage was not confined to those props: the extra map rows cost unrelated ceilings
        /// and walls their lightmap binding and dropped them onto dynamic volume probes (a single
        /// probe lookup per instance, which renders flat and smeared). Removing exactly those 28
        /// rows reproduced retail's classification to the decimal on every camera measured.
        /// </remarks>
        public System.Collections.Generic.HashSet<ulong> RetailMappedResources = null;

        /// <summary>
        /// After the influence solve, re-point any texel whose surface probe gathered NOTHING at the
        /// nearest texel whose probe did.
        /// </summary>
        /// <remarks>
        /// A claimed texel with zero influence links renders black, and the mangle map's bilinear
        /// read smears it over the rect - a soft-edged dark bar rather than a hard texel step. The
        /// mangle map is built before the solve, so it can only route around texels that were never
        /// claimed, not ones that were claimed and came back empty. Measured on CM3 cam16's wall
        /// terminal (island 2279, 4x4): retail leaves 2 of 16 texels unresolved and ships NO
        /// zero-influence probes; we claimed all 16 and two gathered nothing (links p10 0 against
        /// retail's 17, weight-sum mean 2878 against 4019), which is the dark bar down that panel.
        /// </remarks>
        public bool RepointZeroInfluenceTexels = true;

        /// <summary>
        /// Emit surface lights for emissive movers whose emissive triangles never entered the
        /// bake geometry at all, placing their samples on the input probes nearest the emissive
        /// mesh centroid, in the ONE slice that owns the nearest bake instance.
        /// </summary>
        /// <remarks>
        /// The texel pass and the lost-emitter pass both require the emissive geometry to be in
        /// an instance; small fixture meshes (the *_DISPLAY family) routinely are not, and ship
        /// no light at all: 219 of the 1,342 emitters CA's own bake lit on Solace
        /// (RADIOSITY_LEVEL.BIN, 2026-08-26), including 94 of 95 SPOTS_01 and 82 of 84
        /// WARNING_LIGHTs. Retail's per-instance scale for these families equals what
        /// ResolveMoverEmissiveStrength already recovers, so only the sample placement was
        /// missing. The nearest-instance slice election is load-bearing: slices interleave
        /// spatially, and a proximity-scoped version of this pass emitted each fixture's full
        /// flux into 2-3 slices (1,502 groups from ~560 emitters; the table rendered 2.23x
        /// retail through retail's own scaffold).
        /// </remarks>
        public bool EmitTexellessEmitters = true;

        /// <summary>
        /// Harvest the retail bake's own shipped light values and reuse them for entities we can
        /// match ("priors"). <b>TEMPORARILY DEFAULTED OFF (2026-08-26) - restore to true before
        /// shipping.</b>
        /// </summary>
        /// <remarks>
        /// <para>When on, a matched entity's light takes retail's colour, Scale, Weight AND sample
        /// count wholesale, and <c>SuppressedByRetail</c> drops any emitter that has no match. On a
        /// retail level that is total: instrumented on Solace, all three slices reported
        /// <b>100% scavenged, 0 lights derived</b> (322/409/699 from priors). Our own emissive
        /// derivation therefore never ships anything there, and a rebake-vs-retail comparison is
        /// largely retail compared against itself.</para>
        /// <para>Matching is two-tier: exact on (composite_instance_id, resource_id), then - with
        /// <see cref="DeltaLoosePriors"/> - a loose tier on resource_id alone that merges every
        /// retail instance sharing it, i.e. "the same fixture in a different spot".</para>
        /// <para>It is also STALE by construction: a prior is retail's OUTPUT, so if a material or
        /// emissive is edited the reused light still describes the old one, silently.</para>
        /// <para>Off, every light is derived from mover/material data and only authored
        /// radiosity_multiplier = 0 suppresses an emitter - which is exactly how added content is
        /// treated, so it is the honest setting for validating our own logic.</para>
        /// </remarks>
        public bool UseRetailLightPriors = false;

        /// <summary>
        /// Replace the ENTIRE derived surface-light table with retail's shipped one - every light
        /// re-addressed to our nearest live input probe by world position, group structure,
        /// weights, colours, anim channels and entity bindings carried verbatim (bindings are
        /// positional indices, valid because instancing restores retail's resource-row order with
        /// purged rows padded in place).
        /// </summary>
        /// <remarks>
        /// <para>Measured motivation (2026-08-27): the derived table reaches 0.88x retail's
        /// ungated energy, but distributes weight across ENTITIES differently - flat per-emitter
        /// sample counts over-weight always-on fixtures where retail concentrates weight on big
        /// scripted, runtime-gated-off ones. The gate amplifies that into SCI_Hub rendering 2.5x
        /// retail from a weaker table, and CM9's black cam13 ceiling. Retail's table through our
        /// transport renders at the transport ceiling on every level tried (Solace 0.678 vs our
        /// 0.666; CM9 cam13 1.018 vs our 0.83), so on retail levels verbatim is strictly better
        /// than deriving until the per-entity weight distribution is decoded.</para>
        /// <para>No effect on levels with no retail RADIOSITY_RUNTIME (added content) - those
        /// keep the derived path. Unlike <see cref="UseRetailLightPriors"/> scavenging this does
        /// not blend the two sources per emitter; the table is one or the other.</para>
        /// </remarks>
        public bool RetailLightTableVerbatim = false;

        /// <summary>
        /// Fraction of destination probes whose scatter link set includes a FAR-band (3-6m) link.
        /// Measured from retail (fartail): 25.6/27.3/30.4% per slice on Solace, 25.3/31.6% on
        /// CM3 - a universal minority, mean ~27%. The minority long links are what make the
        /// scatter graph percolate level-wide; without them (far as a mid-empty fallback only)
        /// every link caps at ~2.8m, the graph fragments at doorways, and rooms whose own lights
        /// are runtime-gated-off render near black (Solace cam13 at 0.16x with retail's own
        /// table). Selection is a deterministic per-probe dither; the link itself is the
        /// middle-outward pick, matching retail's long-link length p50 of 3.9m.
        /// </summary>
        public float ScatterFarBandFraction = 0.27f;

        /// <summary>
        /// Fraction of destination probes carrying an ULTRA-far scatter link, in the band from
        /// ScatterMaxLinkDistance to twice it (6-12m). Retail's tail is not capped at 6m: ~1% of
        /// its links exceed 6m (up to 11.2m measured), and around Solace's cold cam13 room those
        /// are the links that IMPORT light across the room boundary - retail carries 142 crossing
        /// links (p50 5.0m, max 11.2m) where the 6m-capped builder managed 76 (max 5.8m) and the
        /// room rendered 4x dark. Selection is an independent per-probe dither; the link is the
        /// middle-outward pick in the 6-12m band with the same facing + visibility tests.
        /// </summary>
        public float ScatterUltraFarFraction = 0.06f;

        /// <summary>
        /// Mean excess length (metres past the 3m reach floor) of the long-link tail. fartail
        /// across 12 retail slices (Solace/CM3/Torrens/SCI_Hub, 2026-08-28) shows retail's long
        /// links are ONE distribution decaying from the floor - p10 pinned at 3.1m on every
        /// slice, p50 3.8-4.2, p90 5.9-7.8, with no structure at the 6m "band edge" - which
        /// length = 3m + Exp(1.4m) reproduces (p10 3.15 / p50 3.97 / p90 6.3; mean derived as
        /// (pooled p50 - floor)/ln2). The previous far+ultra middle-outward picks piled our
        /// links at 4.5m and 9m: p10 4.0-4.3, p50 4.7-4.8, p90 8.7-10.4, and a >4m rate DOUBLE
        /// retail's on every level measured - the same excess-coupling class that rendered CM9
        /// 1.25x hot with a healthy light table. When &gt; 0, probes selected by either dither
        /// draw a per-probe hashed target length from this distribution and take the candidate
        /// nearest it (facing + visibility still enforced); &lt;= 0 restores the legacy two-band
        /// pick for A/B.
        /// </summary>
        public float ScatterLongLinkMean = 1.4f;

        /// <summary>
        /// Pad every destination probe's scatter group up to the solve cap when its bands come
        /// up short. Retail does NOT pad: its per-dest degree spreads with local cluster
        /// availability (p10 3 / p50 6 / p90 8, mean 6.1 on CM3) where the padding holds ours
        /// flat at 7 (mean 6.9) and ships +14-28% total entries over retail. Since the engine's
        /// gather makes source count an energy multiplier, uniform-degree padding is a
        /// candidate for the universal brightness excess on low-slice-count levels. False =
        /// degree follows availability, retail-style.
        /// DEFAULT FALSE (2026-08-28): with the decoded long-link tail the padding's removal
        /// brings CM3's entries to retail's exact envelope (51k vs retail 53.5k, degree spread
        /// open) and the pair renders neutral vs the legacy graph (1.348 vs 1.325, within
        /// noise) - both retail-decoded behaviours ship together.
        /// </summary>
        public bool ScatterRollDownFill = false;

        /// <summary>
        /// Rescue passes that guarantee every scatter destination a minimum feed (the
        /// normal-disjoint visibility fallback and the any-terms last-resort feed for unfed
        /// probes). Retail LETS ITS BOTTOM DECILE STARVE - measured on every axis: scatter
        /// degree p10 3, 8-10% of probes influence-empty per room, per-room gather weight p10
        /// 0-1180 vs our 2141-2691, and its shipped light table's two-hop delivered weight p10
        /// 1.3-2.7k vs our 11-12k after re-addressing (lightcoup2, CM3). With the engine's
        /// compressive response (~exponent 0.3), floor-lifting is a level-wide brightness pump
        /// invisible to mean-based parity - the CM3 light-strip decomposition put the verbatim
        /// table's delivered luma at 1.63x retail's through our graph. False = starved probes
        /// stay starved, retail-style.
        /// </summary>
        public bool ScatterStarvationRescue = true;

        /// <summary>
        /// Fold the DIRT_MAP overlay into a material's sampled albedo, on the environment
        /// shaders. Without this the _DTY/_RST material family sampled at its clean white BASE -
        /// 4-10x retail's stored albedo (albmat, SCI_Hub) - and the over-unity region diverged
        /// the runtime relaxation into a full-frame whiteout. The fold is the runtime shader's
        /// own dirt math, read off the byte-identical CA_ENVIRONMENT master: MULTIPLY mode
        /// scales the diffuse by the dirt colour, lerp mode mixes toward it by
        /// dirt.alpha * saturate(DIRT_BLEND_MULT_SPEC_POWER), both in linear space. See
        /// RadiosityMaterialSampler.Build.
        /// </summary>
        public bool SampleSecondaryDiffuse = false;   //OFF until the decoded fold validates against albmat on SCI_Hub (the earlier flat mean-fold guess measurably hurt; this one is the shader's real math but unproven against retail's compiler output).

        /// <summary>
        /// Diagnostic arm of the engine-corner carry (see CarryRetailCorners): also carry
        /// retail's corner cluster positions and re-point the corner mangle at our probes.
        /// Measured to REDISTRIBUTE per-room brightness (SCI_Hub cam4 fell to 0.33x while the
        /// aggregate improved), so the shipping configuration carries the scatter bytes only.
        /// </summary>
        public bool CarryCornerPositions = false;

        /// <summary>Atlas texels the donor shell may spend (the slice atlas is 128x128 = 16,384;
        /// delta islands allocate first and donors never displace them). Nearest donors win.</summary>
        public int DeltaDonorTexelBudget = 8192;

        /// <summary>Per-donor rect dimension clamp. Donors only feed the cluster field, so they
        /// can run coarser than render resolution; this stops a level-spanning floor island from
        /// eating the whole budget.</summary>
        public int DeltaDonorMaxRectDim = 24;

        /// <summary>
        /// Exponent on a progressive texel boost for larger instances. 0 spends a flat
        /// <see cref="MetresSquaredPerTexel"/> everywhere; 0.25 roughly reproduces retail's
        /// allocation curve.
        /// </summary>
        /// <remarks>
        /// Retail does not spend a constant area per texel. Measured against ChallengeMap4's
        /// shipped MODEL_PARAMS (7745 movers with a real rect in both bakes), our rect area against
        /// retail's runs p50 1.00 for instances retail gives under 16 texels, 0.75 at "small",
        /// 0.71 at "medium" and 0.52 for the 73 largest - so retail gives its big surfaces nearly
        /// twice the lightmap resolution we do, and takes it back on small props. Total area is
        /// close (ours 118385 against 131228), so this is distribution, not budget. Off by default
        /// until it is measured to help: the atlas has only ~30% headroom and a boost that
        /// overflows makes the packer shrink rects, which is worse than the flat spend.
        /// </remarks>
        public float LargeInstanceTexelBoost = 0.0f;

        /// <summary>
        /// Take an atlas rect's aspect ratio from the instance's UV occupancy rather than from its
        /// world bounding box.
        /// </summary>
        /// <remarks>
        /// On. A rect maps the unit lightmap UV square, so its shape is a property of how the UVs
        /// are laid out, not of the object's shape in the world. Scored against retail's own rects
        /// on ChallengeMap4 (1066 islands), the world box misses by |log(pred/retail)| 0.77 on
        /// average where UV occupancy misses by 0.51, and the gap is widest on the islands that
        /// carry a room: retail gives the executive lounge 41x42, near square, while the world box
        /// asks for 1.95:1 and produced a 40x27 that halved V resolution again. Squashing one axis
        /// crowds thirteen movers' UV islands together in a rect they already share, and a mover
        /// whose texels are lost reads its atlas neighbour's lighting - a different wall.
        /// </remarks>
        public bool UvShapedRects = true;

        /// <summary>
        /// Smallest rect an instance can be given, in texels. Retail never goes below 2 - a
        /// 1-texel rect would collapse the shader's half-texel inset to zero width.
        /// </summary>
        public int MinInstanceRect = 2;

        /// <summary>
        /// Largest rect an instance can be given, in texels. Measured from retail's MODEL_PARAMS
        /// across Solace and BSP_TORRENS, rect edges run from 2 to 40.
        /// </summary>
        public int MaxInstanceRect = 40;

        /// <summary>
        /// How strongly rect size compensates for an instance's UV square being partly empty,
        /// as an exponent: 0 ignores it, 1 divides the area through by the coverage fraction.
        /// </summary>
        /// <remarks>
        /// <para>The rect spans the whole 0..1 lightmap square, so an instance whose authored UVs
        /// use only part of it wastes the rest of its texels. Ignoring that left only 47.8% of
        /// Solace's rects within 25% of retail's, spread from 0.04x to 78x, and the starved end of
        /// that spread is what left 38% of the cells retail fills empty in ours.</para>
        /// <para>Full compensation overshoots the other way - it cut rects under half retail's from
        /// 11.0% to 5.6% but pushed those over double from 11.0% to 23.4% - so this sits between
        /// the two. An oversized rect only costs atlas space, whereas an undersized one loses the
        /// surface its probes.</para>
        /// </remarks>
        public float UvCoverageCompensation = 1.0f;

        /// <summary>
        /// Measure an instance's UV occupancy by actual triangle coverage rather than by each
        /// triangle's UV bounding box.
        /// </summary>
        /// <remarks>
        /// <para>The bounding box measure is simply wrong - a triangle laid diagonally marks up to
        /// twice the cells it covers, and an instance with thousands of them saturates the 16x16
        /// grid. ChallengeMap4's island 602 (238 m2, 14228 triangles) measures coverage ~1.0 that
        /// way, so <see cref="UvCoverageCompensation"/> does nothing and the rect is sized on world
        /// area alone: 14x14 = 196 texels, of which its UVs reach only 80. Retail gives that island
        /// 1024. Measured properly its coverage is ~0.41 and it gets the rect it needs.</para>
        /// <para>OFF BY DEFAULT, and the reason is worth reading before flipping it. On
        /// ChallengeMap4 it is the first change ever measured that moves the two error families
        /// SEPARATELY. The chronically over-bright rooms - cam21 1.366 -> 1.088, cam22 1.185 ->
        /// 1.023, cam23 1.227 -> 0.986 - land at parity, having resisted every previous lever. But
        /// cam16 goes 0.736 -> 0.396 and cam13 1.113 -> 1.176, and mean rmse is a wash: 12.48
        /// against 12.90. Light counts and unlit islands are identical across both bakes, so this
        /// is not lost lights.</para>
        /// <para>The mechanism is that it raises surface probes 27845 -> 30649 (+10%) and OUR
        /// TRANSPORT GAIN DEPENDS ON PROBE DENSITY - more probes means each gathers less and the
        /// level dims roughly in proportion. That is itself a bug: subdividing a surface should not
        /// change how much light it receives per unit area. Fixing the normalisation is what would
        /// let this ship, and would probably unblock the dim rooms at the same time.</para>
        /// <para><see cref="UvCoverageCompensation"/> trades the two families directly against each
        /// other on top of this: with precise coverage, exponent 0.5 puts cam13 at 1.033 and cam16
        /// at 0.737 but sends cam21 back to 1.610 (mean 14.69).</para>
        /// </remarks>
        public bool PreciseUvCoverage = false;

        /// <summary>
        /// How strongly to correct a surface probe's influence weights for the world area its
        /// chosen clusters actually represent. 0 disables it; 1 fully normalises.
        /// </summary>
        /// <remarks>
        /// <para>A receiver gathers from at most 32 clusters, and a cluster is one live atlas
        /// texel. Where texels are dense those 32 span a small patch of the world, where they are
        /// sparse they span a large one - so the same room, subdivided more finely, delivers less
        /// light. That is measurable: raising ChallengeMap4's surface probes by 10% dims the level
        /// roughly in proportion, which is why <see cref="PreciseUvCoverage"/> fixes the
        /// over-bright rooms and breaks the dim one at the same time.</para>
        /// <para>Physically the missing term is the patch area in the form factor
        /// (cos.cos.A / pi.d^2); our weight curve has cos and d but no A, so it silently assumes
        /// every cluster covers <see cref="MetresSquaredPerTexel"/>. This scales each probe's
        /// weights by <c>(MetresSquaredPerTexel / mean chosen cluster area)^this</c>, clamped, so
        /// an island's brightness stops depending on how many texels its rect happened to get.</para>
        /// <para>The MEAN cluster area is used rather than the sum on purpose: normalising by the
        /// sum would also boost probes that legitimately found fewer than 32 links, washing out
        /// enclosed corners that are correctly dark. This isolates density from link count.</para>
        /// <para>MEASURED AND REJECTED at exponent 1 on ChallengeMap4: mean rmse 12.92 -> 40.02.
        /// The direction of the failure is informative - it brightened EVERY brightness band
        /// (dim 1.28 -> 1.56, mid 1.00 -> 1.36, bright 0.96 -> 1.35), so it is not redistributing
        /// as intended. Taking the slice MEDIAN as the reference is the flaw: cluster areas are
        /// heavily right-skewed, so most probes sit below the median and are scaled UP. A gain
        /// normalised over the actual probe population, not the median texel, would be needed
        /// before this is worth retrying.</para>
        /// </remarks>
        public float InfluenceClusterAreaNormalisation = 0.0f;

        /// <summary>Bounds on the correction above, so one pathological rect cannot dominate.</summary>
        public float InfluenceClusterAreaClamp = 2.5f;

        /// <summary>
        /// Texel area (m2) treated as the norm by the correction above. Zero uses the slice's own
        /// median, which makes the correction purely redistributive - it moves light between
        /// islands of different density without changing the level's overall brightness. Setting a
        /// value turns it into a global gain as well, which is rarely what you want.
        /// </summary>
        public float InfluenceClusterAreaReference = 0.0f;

        // ---- Slicing -----------------------------------------------------------------------

        /// <summary>
        /// Maximum atlas texels a single slice may allocate before the baker opens a new one.
        /// Below the hard 128x128 = 16384 ceiling so the packer is never forced into a corner.
        /// </summary>
        /// <remarks>
        /// <para>Retail's busiest Solace slice holds 12799, so a lower cap than that splits a level
        /// into more slices than retail uses. That matters beyond tidiness: each slice thins its own
        /// input probes independently, so every extra slice adds boundary regions where two sets
        /// overlap and the densities add. Solace at a cap of 12000 came out at 4 slices with 215
        /// cells covered by two of them, against retail's 3 slices and 85 such cells.</para>
        /// <para>This used to be held at or below <see cref="MaxInputProbesPerSlice"/> because one
        /// input probe was placed per live texel. Input probes are scattered over the geometry now,
        /// so the two are independent and only the 128x128 atlas bounds this.</para>
        /// </remarks>
        public int MaxTexelsPerSlice = 8000;   //H58 default: leaves donor headroom (-0.14)

        /// <summary>
        /// Upper bound on slice count. Retail ranges from 1 (Frontend) to 15 (HAB_AIRPORT). This is
        /// a backstop, not a target - <see cref="MaxTexelsPerSlice"/> is what decides the split, and
        /// a slice forced past it cannot give all its texels input probes.
        /// </summary>
        public int MaxSlices = 32;

        /// <summary>
        /// Partition instances into slices by the slice retail's own bake put them in, rather than
        /// by a spatial median split.
        /// </summary>
        /// <remarks>
        /// <para>Retail's slices are not a spatial partition. On ChallengeMap4 all three of them
        /// span most of the level and overlap heavily (slice 0 covers world X -62.5..15.3, slice 1
        /// -32.6..29.3, slice 2 -62.5..17.3), where a median split produces three disjoint strips.
        /// That matters because a slice is what the runtime consults for an object's lighting: the
        /// slice index comes from the object's island (RADIOSITY_RUNTIME's InstanceSliceIndices,
        /// keyed by the island id in RADIOSITY_INSTANCE_MAP), not from where the object is. Put an
        /// island in a different slice than retail did and its MODEL_PARAMS rect indexes into a
        /// different slice's atlas page, so the objects it lights render black. Measured on
        /// ChallengeMap4: this is a lightmap effect, not a volume probe one - stripping the volume
        /// section from retail's own runtime there is near-noise (mean rmse 4.74).</para>
        /// <para>Islands retail never baked have no answer here; they join the slice of their
        /// nearest matched neighbour. With no retail data at all (a scratch bake) this falls back
        /// to the median split.</para>
        /// </remarks>
        public bool MatchRetailSlices = true;

        /// <summary>
        /// Texel ceiling applied to a group taken from retail's own slice grouping, before it is
        /// split spatially anyway.
        /// </summary>
        /// <remarks>
        /// <see cref="MaxTexelsPerSlice"/> is deliberately conservative because it decides how many
        /// slices a level is cut into from scratch. A retail group is different: retail's bake
        /// already fitted that exact set of instances into one 128x128 atlas, so the only question
        /// is whether our rect sizing needs a little more room than theirs did - and splitting the
        /// group is the worse answer, because it puts islands retail lit together into different
        /// slices and that is the mapping this whole path exists to preserve. Sci_Hub's first
        /// retail group asks for 13809 texels and was being split at 13000 for the sake of 5%.
        /// </remarks>
        public int MaxTexelsPerRetailSlice = 15800;

        // ---- Probes ------------------------------------------------------------------------

        /// <summary>
        /// Hard cap on input probes per slice.
        /// </summary>
        /// <remarks>
        /// The engine addresses 64 input probe tiles of 16x16 over a 256x64 slice, so 16384.
        /// This was 12288, inferred from retail never placing a tile past x = 192 - but that is
        /// just how far its levels happen to fill, and Solace peaks at 38 tiles of the 64. Since
        /// probes are spatially sorted before truncation, a cap set too low removes a contiguous
        /// region of the map rather than thinning evenly.
        /// </remarks>
        public int MaxInputProbesPerSlice = 16384;

        /// <summary>
        /// Push probes this far off the surface along the normal to avoid self-hits.
        /// </summary>
        /// <remarks>
        /// Not the cause of the probes that solve to no influence: raising it to 0.06 moved those
        /// from 26.5% to 24.8% on Solace, so they are genuinely enclosed rather than hitting their
        /// own surface. Left at 0.02 because a larger offset on Cathode's thin panel geometry
        /// starts placing probes on the far side of the surface they belong to.
        /// </remarks>
        public float ProbeSurfaceOffset = 0.02f;

        /// <summary>
        /// How many texels to grow an instance's rasterised UV islands into the rest of its rect.
        /// </summary>
        /// <remarks>
        /// The lightmap is filtered bilinearly, so the ring of texels immediately outside an island
        /// is read when the shader samples the island's edge. Two passes cover that with margin.
        /// Texels beyond the dilation are never sampled and are left dead, which is the point:
        /// filling the whole rect with scattered surface samples put probes inside closed props and
        /// behind panels, where they solved to no light and fringed every island with black.
        /// </remarks>
        public int AtlasDilationPasses = 2;

        /// <summary>
        /// Minimum world distance, in metres, between two input probes.
        /// </summary>
        /// <remarks>
        /// <para>Input probes are scattered over the triangles themselves and then thinned to this
        /// spacing, which is how retail builds them - a very large number of candidates over every
        /// surface, then a uniform exclusion pass. Retail's result is clearly Poisson-disc: across
        /// Solace their nearest-neighbour distances average 0.455 m with a coefficient of variation
        /// of 0.37 and no pair closer than a centimetre.</para>
        /// <para>Deriving them from atlas texels instead - one per live texel, or a thinned subset
        /// of those - ties emitter placement to how evenly the authored UVs happen to pack, and
        /// they do not: texel density varies by orders of magnitude between instances. That gave
        /// cv 0.79 with 6% of probes effectively coincident, and it is what made our bounce
        /// lighting patchy where retail's is smooth. Sampling the geometry directly gives cv 0.14,
        /// tighter than retail's own.</para>
        /// <para>Scattering candidates over the surfaces and thinning to this spacing yields a mean
        /// nearest-neighbour distance of roughly 1.1x it. Retail's density is per-level: 0.46
        /// reproduces Solace's count but overshoots ChallengeMap3's by 34%, while 0.52 lands on
        /// retail's count for ChallengeMap3/4 (18k/23k input probes). The old 0.56 default
        /// undershot retail everywhere measured (-12% to -18%).</para>
        /// <para>0.52 measured across seven levels (2026-08-21, W0 200 calibration): CM4
        /// 12.31->11.78 rmse with fit r2 0.77->0.80 and the LINE unchanged - matched density
        /// reduces per-mover scatter, it does not shift brightness. CM5 19.05->18.28, Torrens
        /// 18.64->18.03 (slope 1.109->1.062), CM3/CM9/Solace neutral, no regressions. Denser
        /// still (0.46 where that overshoots) couples into loop gain instead: CM4's intercept
        /// drifted +1.6->+4.4. So match retail's count, do not exceed it.</para>
        /// </remarks>
        public float InputProbeSpacing = 0.52f;

        /// <summary>
        /// Candidate points scattered per square metre of surface before the thinning pass runs.
        /// </summary>
        /// <remarks>
        /// Retail covers a level's surfaces in a very large number of candidates - of the order of
        /// hundreds of thousands - and then uniformly excludes down to an even spacing. Denser
        /// candidates give the dart-throwing more to choose from and so a more even result; the
        /// cost is linear and the survivors are bounded by
        /// <see cref="InputProbeSpacing"/> regardless. At 60 per square metre a level with 11000 m2
        /// of surface throws about 660000 darts.
        /// </remarks>
        public float InputProbeCandidatesPerSquareMetre = 60.0f;

        // ---- Visibility solve --------------------------------------------------------------

        /// <summary>
        /// Influences kept per surface probe. The runtime format stores exactly 32 (cluster,
        /// weight) pairs per probe, so this should not exceed 32.
        /// </summary>
        public int InfluencesPerSurfaceProbe = 32;

        /// <summary>
        /// Ignore clusters beyond this distance (metres) when gathering influence.
        /// </summary>
        /// <remarks>
        /// Retail's longest influence on Solace reaches 30.6 m and 0.20% of its links are beyond
        /// 20 m, so a 20 m cap clipped the tail: our furthest link measured exactly 20.2 m, the
        /// cap itself. The cost is quadratic in candidates offered per probe, which is why this is
        /// not simply set very high.
        /// </remarks>
        public float MaxInfluenceDistance = 32.0f;

        /// <summary>How far either side of a barrier to look for door transfer probe pairs.</summary>
        public float DoorTransferRadius = 3.0f;

        /// <summary>Transfers emitted per door. Retail averages around six.</summary>
        public int MaxTransfersPerDoor = 10;

        /// <summary>
        /// Hard ceiling on door transfers per SLICE. The engine's door buffer overruns past
        /// retail's envelope: retail never ships more than 161 in any slice measured (five
        /// levels), our CM3 bake shipped 440/731 and the game heap-corrupted in
        /// RADIOSITY::destroy at level close, every run - doors off, no crash (2026-08-28).
        /// Doors that would push a slice past the ceiling are dropped whole.
        /// </summary>
        public int MaxDoorTransfersPerSlice = 160;

        /// <summary>Cross-slice influence patches emitted per surface probe.</summary>
        public int MaxCrossSliceFixupsPerProbe = 4;

        /// <summary>
        /// Treat the influence solve as ONE candidate pool across slices: cross-slice candidates
        /// compete for a boundary probe's 32 slots on the same soft-visibility test and weight
        /// curve as the in-slice solve, each win overlaying (displacing) an in-slice link. This
        /// is retail's measured structure on CM3: 48,419 fixups (6.3 per target vs our 2.8),
        /// boundary probes' raw in-slice sums at half/near-zero (p10 0-1031, rising to 3371 with
        /// the overlay applied), and the ablation verdict - retail minus fixups renders 0.813
        /// (fixups carry 19% of the scene) while ours minus ours renders unchanged (inert,
        /// because they replaced weakest in-slice links with similar values). Our in-slice
        /// serving of boundary probes concentrates gather gain inside each slice's own feedback
        /// loop - the measured mechanism of the 2-slice-level overshoot (CM3 1.33/Torrens 1.41
        /// hottest, fragmented CM7/Solace dim). Raise MaxCrossSliceFixupsPerProbe (retail mean
        /// 6.3/target) when enabling.
        /// </summary>
        public bool CrossSliceOnePool = false;

        /// <summary>
        /// On retail levels, overlay retail's own stored input-probe albedo by world position
        /// (unmatched probes keep the derived value, so added content is unaffected). CA's
        /// compiler sampled albedo BEFORE command-driven material remapping - on Torrens the
        /// cockpit/corridor ceilings authored as TEC_Metal_Grey/Plastic_Black (tint 0.03-0.04)
        /// but remapped to white plastic store ~6 in retail where our post-remap sampling
        /// stores ~145 - and retail's engine look is calibrated around its own stale bounce.
        /// Splice-validated: Torrens 1.355 -> 1.203 (stable rmse 44.9 -> 32.7, best ever),
        /// SCI_Hub 1.120 -> 1.087, CM3 unchanged. Same reuse philosophy as
        /// RetailLightTableVerbatim; our post-remap derivation stays for new content per
        /// Matt's ruling. CARRIES NORMALS TOO: albedo alone rendered SCI_Hub 1.173 where
        /// albedo+normals renders 1.087 == the splice (reproduced) - the normals payload is
        /// load-bearing there.
        /// </summary>
        public bool RetailAlbedoVerbatim = false;

        /// <summary>Match radius for RetailAlbedoVerbatim (splice mean match distance 0.33m).</summary>
        public float RetailAlbedoMatchRadius = 0.75f;

        /// <summary>
        /// Solve surface-probe influences by HEMISPHERE SAMPLING - cast cosine-weighted rays
        /// from each probe and link the clusters the rays actually hit (weights still from the
        /// decoded distance curve) - instead of the proximity-candidate solve. The working
        /// decode of retail's room-scoped gather: selection-by-sight reproduces retail's
        /// cluster-read concentration, shared per-room read sets, bimodal empty-or-full link
        /// populations and genuinely starved boundary probes (the cross-slice fixup pass then
        /// serves them, as retail's 48k CM3 fixups do). The proximity solve's flat mixed-
        /// provenance gather measured as the overshoot mechanism on interleaved levels and the
        /// carrier of both colour-cast defects. See SolveInfluencesHemisphere.
        /// </summary>
        public bool InfluenceHemisphereSolve = false;

        /// <summary>
        /// Copy retail's corner-region Scatter entries verbatim (the dirt8/dirt10-era carry).
        /// Scatter is a LINK LIST, so this overwrites ~256 of the first ~1,900 entries per
        /// slice - the first ~280 probes' links - with foreign-layout pairs that decode as
        /// random 14-17m cross-level links (measured: the MU-TH-UR room's input probes take
        /// 9.7% of their scatter sources out-of-room at p50 14.5m vs retail's 1.4% at 4m -
        /// the cool-white import that kills the room's orange). False skips only the scatter
        /// copy; the corner reservation, gutter fill and mangle handling stay.
        /// DEFAULT FALSE (2026-08-28 evening): cross-level validation - SCI_Hub 1.036 (best
        /// ever, the dirt6/7-era blowout did NOT return on the current stack), Torrens 1.140,
        /// Solace stable rmse 15.35 and CM7 16.42 (both best ever, corner-off neutral vs
        /// overlay-only), CM3 neutral, CM9 a small ambiguous cost (+0.7 stable, confounded
        /// with the overlay). The copy was pure list-head corruption; the blowouts that
        /// originally forced it were that era's other corner defects.
        /// </summary>
        public bool CarryCornerScatter = false;

        /// <summary>Rays per surface probe for the hemisphere solve.</summary>
        public int HemisphereRays = 256;

        /// <summary>A ray hit attributes to the nearest live cluster within this radius; hits
        /// with no cluster in range produce no link (absorbed - the natural starvation class).</summary>
        public float HemisphereAttributeRadius = 0.75f;

        /// <summary>When the hemisphere's visible pool holds fewer candidates than this, top it
        /// up with the default builder's soft-vis proximity candidates (strongest curve byte
        /// first) before the quantile cut. The pure-sight pool starves 40-66% of probes to
        /// 10-12 links where retail carries full 32-link sets at 27-30.5 links/probe, and that
        /// starved class broke the cross-level runs: SCI_Hub whiteout (all-sight cliques
        /// concentrate absolute-gain weight inside mutually visible rooms; retail routes ~34%
        /// of its links through walls, de-concentrating them) and the Solace/CM7 dark-room
        /// lifts (a 10-link in-room set over-represents the room's one bright fixture where
        /// retail's full set dilutes it). 0 disables augmentation.</summary>
        public int HemispherePoolTarget = 48;

        /// <summary>
        /// Per-probe cap for a DELTA slice's fixups into retail clusters. The full 32: a retail
        /// probe near a slice boundary needs a few patched slots, but a delta probe cut out of a
        /// room-coherent slice gets ALL its bounce from the room - a flat island (a moved wall,
        /// a shelving face) has zero in-slice links because its own surfaces are coplanar, so
        /// the fixups are its entire gather diet. Measured on the CM3 moved vent wall
        /// (islandgain): at 24 links the wall's probes carried gain 2324 against 3110 for the
        /// surrounding room's retail probes - almost exactly the 24/32 ratio - and at 4 links it
        /// rendered 0.64x retail while a weight-255 crank of those 4 brought the dark tail to
        /// exact parity, so link COUNT is the knob, not the weight curve.
        /// </summary>
        public int DeltaCrossSliceFixupsPerProbe = 32;

        /// <summary>Nudge ray origins by this much to avoid re-hitting the source triangle.</summary>
        public float RayEpsilon = 0.001f;

        /// <summary>
        /// Test visibility against the level's collision shell instead of its render meshes.
        /// </summary>
        /// <remarks>
        /// <para>On. This targets the quarter of our surface probes that found every direction
        /// blocked when occluding against render meshes, which carry interior submeshes, panel back
        /// faces and coincident double-sided surfaces that a probe gets buried in.</para>
        /// <para>It first measured far worse - probes with no influence at all went from 26% against
        /// the render meshes to 43-59% against collision - and that looked like the hulls being more
        /// occlusive. They are not: sampled directly, collision blocks less than render geometry
        /// (0.929 of directions against 0.985). The fault was at the ray ends. Neither endpoint sits
        /// on the proxy surface, so the target's own shell blocked the ray just before it arrived.
        /// For facing pairs known to be mutually visible: 4.1% survived against render meshes, 1.3%
        /// against collision end-to-end, and 4.0% against collision once the ends were pulled in.</para>
        /// <para>So this needs both <see cref="OccluderProjectionRange"/> to lift the origin clear of
        /// the hull it starts inside and <see cref="OccluderEndpointSlack"/> to pull the ends in.
        /// With those, probes with no influence land at 9-10%.</para>
        /// </remarks>
        public bool UseCollisionForVisibility = true;

        /// <summary>
        /// How far along its normal a surface point may be lifted to clear the occluder shell it
        /// sits inside, in metres. 0 disables the projection. Only used when
        /// <see cref="UseCollisionForVisibility"/> is on.
        /// </summary>
        /// <remarks>
        /// A collision hull encloses its object, so a render-surface point starts inside it and
        /// every ray is blocked at once - occluding against collision without this was markedly
        /// worse (59% of probes with no influence against 46% with it). 0.5 m covers the gap
        /// between a render surface and the hull around it; 1.5 m measured no better.
        /// </remarks>
        public float OccluderProjectionRange = 0.5f;

        /// <summary>
        /// How far to pull a visibility ray in at each end when occluding against the proxy, in
        /// metres. Only used when <see cref="UseCollisionForVisibility"/> is on.
        /// </summary>
        /// <remarks>
        /// Both ends of a visibility ray are points on the render meshes, not on the proxy, so
        /// without slack the shell around the target blocks the ray just before it lands. This is
        /// the whole reason occluding against collision first measured worse: of facing surface
        /// point pairs on Solace, 4.1% are mutually visible through the render meshes but only 1.3%
        /// through collision run end to end. 0.2 m restores 4.0% - it is chosen to reproduce what
        /// the render meshes say rather than to maximise visibility, since 0.4 m gives 6.8% and
        /// that is light passing through walls.
        /// </remarks>
        public float OccluderEndpointSlack = 0.35f;

        /// <summary>
        /// Largest share of a link, per end, that <see cref="OccluderEndpointSlack"/> may skip.
        /// </summary>
        /// <remarks>
        /// A flat slack leaves a short ray barely tested - 0.35 m at each end of a 1 m link examines
        /// only the middle 0.30 m - and that is where our light leaked. Re-tested against the render
        /// meshes, 68.1% of our 0-1 m links passed through geometry against retail's 34.2%, with the
        /// excess worst at short range. 0.15 holds the untested portion to 30% at any distance.
        /// </remarks>
        public float OccluderSlackFraction = 0.15f;

        /// <summary>
        /// Source clusters kept per input probe in the scatter point list. Retail averages about
        /// six and never exceeds eight (44126 entries over 7170 probes in BSP_TORRENS slice 0), so
        /// the baker clamps this to eight.
        /// </summary>
        public int MaxScatterTargetsPerProbe = 8;

        /// <summary>
        /// Ceiling on scatter entries in one slice.
        /// </summary>
        /// <remarks>
        /// The engine allows 131072 scatter verts. This was held at 62000 because the busiest
        /// retail slice holds 62661 and none crosses 65535, which looked like a 16-bit index -
        /// it is not, and staying under it was throwing away indirect light for nothing.
        /// </remarks>
        public int MaxScatterEntriesPerSlice = 131072;

        /// <summary>
        /// Emit the scatter point list consumed by CA_RADIOSITY_INDIRECT_SCATTER.
        /// </summary>
        /// <remarks>
        /// No retail slice ships this empty - the smallest across all 19 levels holds 502 entries -
        /// so leave it on. It is not, however, fatal to omit: our Frontend bake produces an empty
        /// list and the level still loads and renders. The crash that looked like it came from here
        /// was the probe tree's leaf indexing, not the scatter list.
        /// </remarks>
        public bool EmitScatter = true;

        /// <summary>
        /// Emit the volume probe hash used to light dynamic objects
        /// (CA_RADIOSITY_OBJECT_PROBE_INTERP).
        /// </summary>
        /// <remarks>
        /// Without this, dynamic objects - characters, movable props - receive no bounced light.
        /// The structure is a tree over the cell grid; see <c>RadiosityBaker.BuildVolumeProbeHash</c>
        /// for the layout, which reproduces all 126 populated retail hashes exactly.
        /// </remarks>
        public bool EmitVolumeProbes = true;

        /// <summary>Emit direct surface lights (CA_RADIOSITY_DIRECT_*).</summary>
        public bool EmitSurfaceLights = true;

        /// <summary>
        /// Also sample scripted LIGHT movers (deferred light entities) into the surface light set.
        /// </summary>
        /// <remarks>
        /// Off. Joining every SCI_Hub light slice back to its RESOURCES.BIN entity shows retail
        /// attaches none of them to a LIGHT mover, across every UsesRadiosity / fraction / colour
        /// bucket - its surface lights are all emissive-material movers. Deferred lights already
        /// light the scene directly at runtime, so injecting them into radiosity as well
        /// double-counted them (446 extra light slices, 17% of our direct energy).
        /// </remarks>
        public bool EmitLightEntitySamples = false;

        /// <summary>
        /// Emissive atlas texels per surface light. One light per texel over-samples an emitter.
        /// </summary>
        /// <remarks>
        /// Retail averages 2.91 lights per emissive instance on Solace with a median of 2 and a
        /// hard maximum of 81. Almost all of our excess turned out to come from scripted light
        /// entities sampling four probes each rather than two, so 1 here (cap only, no thinning) is
        /// what lands closest; thinning on top of the sample fix undershot by a quarter.
        /// </remarks>
        public int EmissiveTexelsPerLight = 1;

        /// <summary>
        /// Fewest input probes a single emitter may be sampled at.
        /// </summary>
        /// <remarks>
        /// An emitter smaller than <see cref="InputProbeSpacing"/> resolves every one of its texels
        /// to the same probe and would otherwise cast from a single point. Retail almost never does
        /// that: 53% of its light slices hold exactly two items and only 1-2% hold one, against 60%
        /// holding one before this existed.
        /// </remarks>
        public int MinProbesPerEmitter = 2;

        /// <summary>
        /// How far around an emitter to gather input probes that sample it, in metres.
        /// </summary>
        /// <remarks>
        /// Retail's item count per light slice is a property of the probes near an emitter, not of
        /// the emitter itself - across 1834 slices it correlates with log(emissive area) at only
        /// 0.099. This is what reproduces the spread it does have (53% two items, 25% three, 11%
        /// four) rather than pinning every emitter at the floor.
        /// </remarks>
        public float EmitterSampleRadius = 0.64f;

        /// <summary>
        /// Let an emissive triangle claim atlas texels even once its mover is over its share of
        /// the rect, so an emitter is never dropped for lack of budget (#35).
        /// </summary>
        /// <remarks>
        /// Off. It sounds right - a mover with no live emissive texel contributes no surface light
        /// at all, since BuildEmissiveSurfaceLights groups by mover - but measured on SCI_Hub it
        /// does not deliver: light slices moved 1416 to 1386, away from retail's 1818, and the
        /// render scored slightly worse. Kept so the idea is not lost and can be retested once the
        /// slice-count difference below is understood.
        /// </remarks>
        public bool ExemptEmissiveFromRectQuota = false;

        /// <summary>
        /// Ration a shared atlas rect between an island's movers by each mover's UV FOOTPRINT
        /// rather than by its share of the island's world area.
        /// </summary>
        /// <remarks>
        /// On. What a mover needs from the rect is however many texels its lightmap UVs land on,
        /// and that is only loosely related to how much world surface it has. Across
        /// ChallengeMap4's 1968 multi-mover islands the movers' footprints overlap by a median of
        /// 12.7%, so they are mostly disjoint and no rationing should be needed at all - but under
        /// area shares the quota bound constantly and in both directions, starving movers with a
        /// big footprint and a small area while reserving unusable texels for the reverse. The
        /// starved movers' texels stayed dead and FillUnclaimed handed them to a neighbour, so
        /// individual walls of one room read another wall's brightness: on ChallengeMap4's cam7
        /// two walls of the same island landed 31 luma under retail and 38 over.
        /// Set false to go back to area shares.
        /// </remarks>
        public bool FootprintRectQuota = true;

        /// <summary>Emit door transfer sets.</summary>
        public bool EmitDoors = true;

        /// <summary>Emit cross-slice influence fixups.</summary>
        public bool EmitCrossSliceFixups = true;

        // ---- Volume probes -----------------------------------------------------------------

        /// <summary>
        /// World size (metres) of one cell in the volume probe hash used to light dynamic
        /// objects. Retail BSP_TORRENS slice 0 is 37x4x22 cells over a 74x8x44 m box, i.e. 2 m.
        /// </summary>
        public float VolumeProbeCellSize = 2.0f;

        /// <summary>Subdivisions per hash level, matching the retail value.</summary>
        public uint VolumeProbeSubdivsPerLevel = 3;

        /// <summary>
        /// Hash cells no probe falls inside borrow the nearest (preferably visible) probe
        /// within this many metres; 0 disables and leaves them as no-probe items. The
        /// engine's 8-cell blend does not renormalise around missing items, so a converted
        /// mover whose bounds centre floats in mid-air - a ceiling panel, a room-sized wall
        /// piece - otherwise reads a mostly-black blend even with lit probes under a metre
        /// away. Retail's own items resolve probes up to ~2.6 m from their cell.
        /// OPT-IN: at 3.0 the item count exploded 10x (23-53k/slice against retail's ~2.4k)
        /// and every F6-lit room went black at F3-identical ratios - ~180k extra vis-face
        /// grids folding into the shared 256-entry palette is the prime suspect. Keep the
        /// reach at or under ~1.2 (one ring at 1 m cells) if enabling.
        /// </summary>
        public float VolumeProbeFillReach = 1.20f;   //H58 default (harness ran RADBAKE_HASHFILL=1.2 throughout)

        /// <summary>
        /// Rays per cell of a volume probe's 8x8 visibility face. The stored byte is
        /// <c>floor(n * 255 / 27)</c>, so 27 is the value that can actually reach all 28 levels -
        /// anything lower quantises the output coarsely (4 samples yields only 5 distinct values).
        /// </summary>
        public int VolumeProbeVisSamplesPerCell = 27;

        /// <summary>How far a visibility ray looks before counting the direction as open (metres).</summary>
        public float VolumeProbeVisRange = 12.0f;

        // ---- Emissive ----------------------------------------------------------------------

        /// <summary>
        /// Scales <c>MOVER_DESCRIPTOR.EmissiveRadiosityMultiplier</c> into the radiance injected
        /// into the probe set.
        /// </summary>
        public float EmissiveScale = 1.0f;

        /// <summary>
        /// Multiplies every surface light's Weight - the energy injected into the probe set -
        /// after retail's priors have been applied.
        /// </summary>
        /// <remarks>
        /// <para>Fitting our rendered luma against retail's per mover gives a straight line with
        /// two independent errors, <c>ours = slope * retail + intercept</c>. The intercept is a bed
        /// of light our transport adds where retail has none; the slope is how much light reaches
        /// a surface once that bed is removed. <see cref="InfluenceCurveW0"/> moves BOTH, and
        /// steeply - it is a loop gain, not a one-shot one, and the transport sits near the knee:
        /// on ChallengeMap4, W0 188/200/212/240/262 gives intercept -10.2/-4.1/+5.0/+39.3/+221.3.
        /// So the influence curve cannot fix both, and this is the second knob.</para>
        /// <para>Use W0 to put the intercept at zero, then this to put the slope at one. Swept on
        /// ChallengeMap4 at W0 200 with soft visibility:</para>
        /// <code>
        ///   scale   slope   intercept   rmse    nrmse
        ///   1.00    0.966     -4.14     12.66   12.76
        ///   1.10    0.950     -0.79     12.33   12.23
        ///   1.20    0.916     +1.54     12.00   11.72   (default)
        ///   1.35    0.917     +3.18     12.51   12.08
        /// </code>
        /// <para>Note it raises the intercept more than the slope - injected energy feeds the same
        /// loop - so it cannot recover the slope on its own. At the (200, 1.20) optimum the fit is
        /// 0.916 x retail + 1.54 against the old default's 0.872 x retail + 7.27, and the residual
        /// 8% slope deficit is a separate defect still open.</para>
        /// </remarks>
        public float SurfaceLightWeightScale = 0.80f;   //H58 default (harness ran RADBAKE_LIGHTSCALE=0.8 throughout)

        /// <summary>
        /// Bounds applied to a mover's <c>EmissiveRadiosityMultiplier</c> before it reaches a
        /// surface light's Scale and Weight.
        /// </summary>
        /// <remarks>
        /// Joining our lights to retail's per RESOURCES.BIN entity on SCI_Hub: emitters whose
        /// mover multiplier reads 4-10x are stored by retail at Scale 7-15 - the 0.5x-1.0x range -
        /// and retail's whole distribution is 69% inside Scale 7-15 with nothing between 47 and
        /// 255. The multiplier is evidently not retail's Scale source (the mapping scatters in both
        /// directions), so until the real source is decoded, clamping keeps any single emitter from
        /// blowing out its surroundings the way our unclamped 4-10x lights did.
        /// </remarks>
        /// <remarks>
        /// The floor is 0 - i.e. no floor. A floor of 0.5 was tried and regressed the render:
        /// retail runs 433 of SCI_Hub's lights at Scale 0-1 (a sixteenth of unit strength), and
        /// raising our correspondingly dim emitters to 0.5x blew out the ceiling fixtures they sit
        /// on (cam9 rmse 26.2 -> 40.3).
        /// </remarks>
        public float EmissiveMultiplierFloor = 0.0f;
        public float EmissiveMultiplierCeiling = 1.5f;

        /// <summary>
        /// Strength used for an emitter whose <c>EmissiveRadiosityMultiplier</c> is zero.
        /// </summary>
        /// <remarks>
        /// Most of retail's emitters are picked out by their material, not by that field, and it
        /// reads zero on them. Retail's own Scale bytes for those lights are 15 - the encoding of
        /// exactly 1.0 - on 69.2% of them, well clear of the next value.
        /// </remarks>
        public float DefaultEmissiveMultiplier = 1.0f;

        /// <summary>
        /// How much of a surface light's colour survives into its RGB, against its own luminance.
        /// 1 keeps the emitter's tint exactly; 0 stores a neutral grey.
        /// </summary>
        /// <remarks>
        /// <para>Retail's surface lights are overwhelmingly neutral: of 3915 on Solace, the three
        /// commonest RGB values are (255,255,255), (174,174,174) and (177,177,177), covering 70%
        /// between them, and the energy-weighted mean colour is (136,134,129) - a chroma of about
        /// 7. Writing the mover's EmissiveTint verbatim gives a chroma near 29, skewed green by
        /// tints like (219,255,221), and that is what put a green cast over the whole render.</para>
        /// <para>SUPERSEDED (2026-08-25) by <see cref="SurfaceLightGammaEncode"/>: the light RGB
        /// IS the emissive tint, gamma-2.0 encoded. Only 1 of Solace's 76 tints appeared in
        /// retail's set because a square root leaves only white and the pure primaries where
        /// they were - and the two Solace greys cited above are exactly it: 174 = sqrt(255*119)
        /// and 177 = sqrt(255*124), the encodes of the 0.467 and 0.486 greys CA authored. This
        /// knob only applies while the gamma encode is off.</para>
        /// </remarks>
        public float SurfaceLightSaturation = 0.25f;

        /// <summary>
        /// Store a surface light's colour as <c>floor(sqrt(255 * tintByte))</c> - the gamma-2.0
        /// encode retail uses - instead of desaturating the raw tint.
        /// </summary>
        /// <remarks>
        /// Decoded 2026-08-25 by joining RADIOSITY_LEVEL.BIN's authored per-instance emissive
        /// colours to the shipped RADIOSITY_RUNTIME light palette on CM3. All 12 colours checked
        /// match exactly under truncation, across both levels tested:
        ///   (0.929,0.839,0.643) -> 245,233,204   (0.557,0.792,0.929) -> 190,226,245
        ///   (0.902,0.561,0.192) -> 242,190,111   (0.741,0.945,0.957) -> 219,247,249
        ///   0.467 grey -> 174 (Solace's 2nd commonest)   0.486 grey -> 177 (its 3rd)
        /// Truncation matters: 164 -> 204.5 stores 204, and rounding it to 205 is where our
        /// "off-palette" light colours came from.
        /// </remarks>
        public bool SurfaceLightGammaEncode = true;   //H58 default: decoded, 2488/2488 CM3 lights

        /// <summary>
        /// Take a surface light's colour from the emissive material's DIFFUSE-map mean rather
        /// than the mover's EmissiveTint. Retail's own source; pairs with
        /// <see cref="SurfaceLightGammaEncode"/>, which encodes it.
        /// </summary>
        /// <remarks>
        /// Decoded 2026-08-25 against RADIOSITY_LEVEL.BIN's authored colours. CA_ENVIRONMENT has
        /// no emissive texture sampler - EMISSIVE is a shader FEATURE bit - so an emissive surface
        /// is lit from its diffuse map, and the tint-graded linear mean of that map is what CA's
        /// compiler recorded: STRIP_05M_DISPLAY (0.929,0.839,0.643) and BASE_DISPLAY (0.439,0,0)
        /// both match to three places. Because the texture is shared per fixture family, so is
        /// the colour - which is why retail records ONE value per family while our per-mover
        /// EmissiveTint varies from instance to instance, giving identical fixtures different
        /// light colours. End to end: mean 0.929,0.839,0.643 -> sqrt -> x255 -> 245,233,204,
        /// exactly what retail ships on all 1,172 STRIP_05M lights in ChallengeMap3.
        /// Only 29 of 1,527 emitters sit on a remapped material, so the CA pre-remap sampling bug
        /// does NOT account for this - it is a real difference in source.
        /// </remarks>
        public bool SurfaceLightColourFromDiffuseMean = true;   //fallback when the tint constant is unresolvable

        /// <summary>
        /// Surface light colour = the emissive material's DIFFUSE_TINT constant. THE decoded
        /// source: it equals RADIOSITY_LEVEL.BIN's authored colour on 93.5% of Solace's and
        /// 95.7% of ChallengeMap3's emitters at a &lt;0.02 exact threshold (2026-08-27), with the
        /// residual matching the pre-remap sampling class where our value is the correct one.
        /// Takes precedence over <see cref="SurfaceLightColourFromDiffuseMean"/>, which remains
        /// as the fallback for materials whose shader does not remap the constant.
        /// </summary>
        public bool SurfaceLightColourFromDiffuseTint = true;

        /// <summary>
        /// Force islands built from the same geometry to share one rect size, as retail does.
        /// See <c>RadiosityBaker.ApplyPerModelRects</c> for the measurement and the mechanism.
        /// </summary>
        public bool PerModelRectSizes = false;

        /// <summary>
        /// Rigid translation applied to duplicated content, used to match each copied mover to
        /// its TRUE retail twin when looking up light priors (position minus this offset).
        /// Zero disables offset-aware matching.
        /// </summary>
        /// <remarks>
        /// Nearest-in-space matching alone measured worse (24.73 vs 24.00) because a copy offset
        /// 250 m sits 250 m from its own twin but only ~210 m from an unrelated instance of the
        /// same fixture. It did return retail's EXACT byte where the merged fallback was one unit
        /// off, so the pick - not the idea - was the problem. Removing the offset first makes the
        /// twin exact, which matters because the merge flattens both colour and flux: on CM3's
        /// cam9 room retail's dominant light is w=195 warm amber while ours is w=46 grey.
        /// </remarks>
        public System.Numerics.Vector3 DeltaPriorOffset = System.Numerics.Vector3.Zero;

        /// <summary>
        /// Snap every slice's volume-probe hash box out to a grid anchored at the world origin,
        /// so all slices share one lattice instead of each deriving a grid from its own bounds.
        /// </summary>
        /// <remarks>
        /// Retail's slices line up: their lattices coincide and their origins sit a whole number
        /// of cells apart. Ours phase each grid off its own bounding box, so neighbouring slices
        /// cut space differently and a dynamic object crossing a slice boundary jumps between two
        /// misaligned fields - visible as ragged volume cell borders where retail's are clean.
        /// Costs at most one extra cell per axis per slice.
        /// </remarks>
        public bool SharedVolumeLattice = false;

        /// <summary>
        /// Chunk the LIGHTMAP delta by ZONE rather than balancing island fill, so a room's
        /// islands stay in one slice. Mirrors what the probe path already does.
        /// </summary>
        /// <remarks>
        /// The default worst-fit chunker balances fill by sending each island to the emptiest
        /// chunk, which necessarily scatters a room's islands across slices - and a slice cannot
        /// be lit by another slice's lights. That is the failure
        /// <see cref="UnbakedEmitterReach"/>'s remarks describe (walls in one slice, ceiling
        /// fixtures in another, room renders black) and the reason that rescue reach has to span
        /// a whole room. Retail's own slices follow zones: a zone's rooms live wholly in one
        /// slice. Chunking by zone removes the cause rather than compensating for it.
        /// </remarks>
        public bool DeltaZoneChunks = false;

        /// <summary>
        /// Pack donor islands BEFORE members, so the donor shell keeps the space it was allocated
        /// instead of being dropped when the slice overfills.
        /// </summary>
        /// <remarks>
        /// Donor selection is clamped to (15800 - ESTIMATED groupTexels), but the demand estimator
        /// runs ~26% under what the allocator really places, so the slice fills and the donors
        /// chosen on that optimistic budget get dropped at pack time: 618 / 638 / 807 across three
        /// CM3 runs, each time correlating with rooms losing bounce light (cam3 lost 187 donors
        /// and 31% of its light when zone chunking made its slice fuller). Packing donors first
        /// makes MEMBERS shrink instead, which is the better trade - a member with a smaller rect
        /// still gathers, whereas a dropped donor removes a bounce source outright.
        /// </remarks>
        public bool DonorsPackFirst = false;

        /// <summary>
        /// Materials whose average albedo could not be decoded fall back to this grey level, as a
        /// linear reflectance - not an sRGB value, since the sampler linearises everything it
        /// decodes. Retail's own albedo table averages 0.36 on Solace and 0.40 on BSP_TORRENS.
        /// </summary>
        public float FallbackAlbedo = 0.4f;

        /// <summary>
        /// Cap on albedo so a bright surface cannot amplify energy across bounces.
        /// </summary>
        /// <remarks>
        /// Retail's own albedo table reaches exactly 1.0 on all 130280 BSP_TORRENS samples and
        /// never exceeds it, so 1.0 is used as the cap here.
        /// </remarks>
        public float MaxAlbedo = 1.0f;

        /// <summary>
        /// Overall scale on sampled albedo, calibrated against retail.
        /// </summary>
        /// <remarks>
        /// <para>This once cancelled a BC block-endpoint averaging bias; per-texel sampling removed
        /// that at its source and this sat at 1.0.</para>
        /// <para>It is back as a real calibration because the stored input probe albedo measured
        /// 1.5x retail's on SCI_Hub - ours (81.6, 87.7, 91.7) against retail's (54.3, 58.5, 60.9),
        /// identical channel ratios, pure magnitude - and albedo is the per-bounce gain of the
        /// whole indirect solve, so the render sat 1.2-1.5x too bright everywhere the bounce
        /// dominates. Whatever darkens retail's runtime albedo relative to a plain diffuse-map mean
        /// (an AO term folded in, or a different tint path) is not decoded yet; until it is, this
        /// holds the per-bounce energy at retail's level.</para>
        /// <para>Raising this to 0.72 to compensate the remaining bounce deficit was tried and
        /// measured slightly worse (mean rmse 16.7 -> 17.2): the direct-lit cameras overshoot
        /// before the bounce-lit ones catch up, so it stays on retail's measured value.</para>
        /// <para>At 1.0 the sampler's bias against retail's own compiler albedo is per-material
        /// and swings per level (stored probe albedo 1.5x retail's on SCI_Hub, 0.71x on
        /// BSP_TORRENS with identical code), so no global constant cancels it - the sampler
        /// itself has to close that gap.</para>
        /// </remarks>
        public float AlbedoScale = 1.0f;

        /// <summary>
        /// Store the LIGHT's colour, not the diffuse texture, as input-probe albedo on movers that
        /// carry a retail surface-light prior.
        /// </summary>
        /// <remarks>
        /// <para>Measured on ChallengeMap4 (harness `albmat --mover 7706`): the luminous ceiling
        /// CEILING_HZDLAB is 216 m2 of HAB_Plastic_Matte_GreyDark mesh, and our sampler faithfully
        /// stores its dark diffuse (~RGB 23,22,22). Retail's probes at the same positions store
        /// ~(180,175,164) - exactly the room's surface-light colour, R174 G174 B174. Retail treats
        /// a lit panel as bouncing its glow colour, not the plastic it is moulded from.</para>
        /// <para>The same dark-plastic families carry the broad albedo deficits gaincmp measures
        /// (ours/retail 0.74 on CM4, 0.78 on CM3, at parity on CM1/CM5), so this is the candidate
        /// mechanism for closing them.</para>
        /// <para>MEASURED, NOW THE DEFAULT: ChallengeMap5 (the panel-dense science lab, our worst
        /// level) goes rmse 24.93 -> 19.05 and fitted intercept -28.7 -> -17.0; ChallengeMap4
        /// (already at parity) is unchanged within noise (12.05 -> 12.06, fit identical). Helps
        /// where luminous fixtures dominate, costs nothing where they do not.</para>
        /// </remarks>
        public bool LightColourProbeAlbedo = true;

        /// <summary>
        /// Extend <see cref="LightColourProbeAlbedo"/> to a lit mover's coincident state-variant
        /// siblings: movers in the same island within half a metre of a prior-carrying mover
        /// inherit its light colour.
        /// </summary>
        /// <remarks>
        /// Lit fixtures ship as several coincident movers with the light slice on only one.
        /// ChallengeMap4's mover 2511 (dark plastic, no prior) sits on lit twin 2512 (prior colour
        /// 245,233,204) and retail's probe albedo on BOTH is that glow, not the plastic.
        /// Experimental - measure per level before defaulting on.
        /// </remarks>
        public bool LightColourProbeAlbedoSiblings = false;



        /// <summary>
        /// Constrain the texel-to-input-probe binding (scatter destinations, light-sample
        /// attribution, volume hash) to probes that are visible from the texel and roughly agree
        /// in normal, falling back to plain nearest. Off binds by raw distance only.
        /// </summary>
        public bool ProbeBindingTiers = true;

        /// <summary>
        /// Accept an influence link when any of a few patch-jittered rays connects, rather than
        /// only the centre ray. Introduced when starved probes rendered visibly dark - but that
        /// symptom largely came from the mangle map misdecode, so the leniency is A/B-testable
        /// again: retail leaves 7.4% of Solace's probes with no influence where we leave 1%.
        /// </summary>
        public bool SoftInfluenceVisibility = true;

        /// <summary>
        /// Spread each probe's influence slots over distance bands matched to retail's link
        /// length histogram, instead of keeping the strongest form factors outright. Also tuned
        /// during the mangle misdecode era, so A/B-testable: the quota backfill always fills a
        /// probe to 32 links, which in tight spaces gathers the same nearby surfaces several
        /// times over and reads as an over-bright small room.
        /// </summary>
        public bool StratifyInfluencesByDistance = true;

        /// <summary>
        /// Where a retail prior exists, target its per-entity sample count in light placement and
        /// share its Weight sum over the samples actually placed, instead of carrying the retail
        /// mean at our own count.
        /// </summary>
        /// <remarks>
        /// <para>On by default because it is what makes our emitted light energy match retail's.
        /// Measured on ChallengeMap4: off, we emit 3871 lights summing to weight 70039 against
        /// retail's 4414 / 83521 - 88% of the count and 84% of the energy. On, 4381 lights summing
        /// to 83547, within 0.03% of retail on energy and 0.7% on count.</para>
        /// <para>It is also the only lever measured to move the dim rooms, which sit on a flat
        /// additive floor our transport does not produce (cam7 0.755x -> 0.895x, cam9 0.791x ->
        /// 0.844x, best mean |luma diff| 4.28). Mean rmse goes 12.49 -> 12.74 and exposure-
        /// normalised rmse is flat (12.28 -> 12.33): the raw cost is one room already 1.30x over
        /// before this was touched, so the flag scales a pre-existing error rather than causing
        /// one.</para>
        /// <para>This was off for a long time because it amplified Solace's cam4 corridor from
        /// 2.3x to 3.8x. That corridor's excess was later traced to long scatter links and removed
        /// by LocalScatter, so the original objection no longer applies.</para>
        /// </remarks>
        public bool MatchRetailSampleCounts = true;

        /// <summary>
        /// Give input probes with no influence-derived scatter sources a nearest-facing-cluster
        /// fallback, matching retail's 100% destination coverage. Off on measurement: see
        /// EnsureDestinationCoverage.
        /// </summary>
        public bool CoverScatterDestinations = false;

        /// <summary>
        /// Leave PATH_CLOSED doorway barrier boxes out of the occluder soup, baking every doorway
        /// as open. The door transfer sets describe door-open light paths, so the field they
        /// modulate is the doors-open one; a barrier baked solid also walls off rooms that in
        /// retail borrow light through their doorways.
        /// </summary>
        /// <remarks>
        /// On by measurement (2026-08-18): Solace mean rmse 13.86 -> 13.55 (a dim room behind a
        /// doorway, cam13, recovered 0.66 -> 0.84 of retail's brightness), BSP_TORRENS
        /// 19.83 -> 19.35, SCI_Hub unchanged. Stripping the doors section from retail's own
        /// runtime changes nothing visually, so the engine's runtime door modulation is a no-op
        /// in practice - the doorway state that matters is the one baked into the field.
        /// </remarks>
        public bool OpenDoorwaysForBake = true;

        /// <summary>
        /// Build the scatter list from each input probe's local cluster neighbourhood instead of
        /// deriving it from influence transfers. Matches retail's measured shape (~6 sources per
        /// probe at sub-metre range, both coverage invariants held); the influence-derived list
        /// managed 2.5 per probe with a third of probes unfed, which starves everything that reads
        /// input probe radiance - most visibly RADIOSITY_DYNAMIC props.
        /// </summary>
        /// <remarks>
        /// On by measurement (2026-08-18): Solace mean rmse 13.50 -> 12.99 with the two rooms
        /// that had been invariant under every other transport change finally moving (cam11
        /// 1.36x -> 1.06x, cam4 1.76x -> 0.54x); BSP_TORRENS 19.33 -> 17.31 (cockpit 1.66x ->
        /// 1.37x); SCI_Hub best-ever exposure-normalised error.
        /// </remarks>
        public bool LocalScatter = true;

        /// <summary>Search radius, in metres, for a probe's local scatter neighbourhood.</summary>
        /// <remarks>
        /// The count-vs-energy balance: the engine's gather makes source count an energy
        /// multiplier, and retail's per-probe counts run p10 3 / p50 6 / p90 8 - a spread that
        /// falls out of local cluster availability inside a roughly metre-scale ball. Swept 1.0 /
        /// 1.25 / 1.5 / 2.5 across three levels: 2.5 saturates every probe at the cap and
        /// over-feeds bright levels; 1.25 is the best aggregate.
        /// </remarks>
        public float LocalScatterRadius = 1.25f;

        /// <summary>
        /// Outer radius, in metres, a probe may reach to when its own neighbourhood cannot fill it.
        /// </summary>
        /// <remarks>
        /// Measured against retail on ChallengeMap4: retail's scatter source distances run
        /// p50 0.73 m with a p90 of 2.5 m, where a flat <see cref="LocalScatterRadius"/> ball
        /// produced p50 0.74 m and a hard p90 of 1.12 m - the same median with no tail. Sweeping
        /// the base radius up to reach that tail is what over-fed bright levels, because it also
        /// moves the sources every well-served probe takes. Keeping the base radius and letting
        /// only the probes that come up short reach further reproduces retail's spread and adds
        /// energy nowhere else: dense neighbourhoods still fill from within a metre, and the
        /// probes in sparse ones stop coming back empty (95% of ours were fed against retail's
        /// 99.8%, and an unfed probe leaves a dead patch in the standing field).
        /// </remarks>
        public float LocalScatterReachRadius = 3.0f;

        /// <summary>
        /// Give EVERY input probe scatter sources from three distance bands - near (its own
        /// surface, within <see cref="LocalScatterRadius"/>), mid (the room, to
        /// <see cref="LocalScatterReachRadius"/>) and far (to the 6 m link ceiling) - with
        /// quotas 4/2/1 at the 7-source cap, instead of an all-local ball.
        /// </summary>
        /// <remarks>
        /// The Phase H ablation proved scatter is the ONLY structure coupling surface lights
        /// into the standing field (emptying retail's list collapses its ungated render
        /// 3.008 -> 0.359; doors, fixups and LiveSurfaceLights are all inert), and the two-hop
        /// census put our all-local links at ~0.6x retail's delivered gather mass per light.
        /// Retail's link-length distribution (p50 0.75 / p90 2.45 / p99 6.1) has a 3.3x p90/p50
        /// ratio no single ball produces; the 4/2/1 band quotas reproduce all three percentiles
        /// at once. Mid/far sources must FACE the probe (not share its normal) and pass a
        /// visibility ray - a scatter link carries radiance with no runtime occlusion.
        /// </remarks>
        public bool ScatterBandStratify = true;

        /// <summary>
        /// Influence falloff curve: weight at zero distance before facing modulation.
        /// </summary>
        /// <remarks>
        /// <para>This is a LOOP gain, not a one-shot one, and the transport sits close to its
        /// knee - so it moves the result far more than its size suggests. Swept on ChallengeMap4
        /// (strict visibility, no light scaling, everything else default), fitting our rendered
        /// luma against retail's over 1094 mover sightings as
        /// <c>ours = slope * retail + intercept</c>:</para>
        /// <code>
        ///   W0    slope   intercept   mean rmse
        ///   188   1.014     -10.17      14.95
        ///   200   0.966      -4.14      12.66
        ///   240   0.586     +39.28      30.10
        ///   262  -0.010    +221.34     182.49   (saturated - the level is white)
        /// </code>
        /// <para>A 13% increase multiplies the delivered light several times over, so this is not
        /// a brightness slider. The old default of 212 with soft visibility fitted
        /// <c>0.872 x retail + 7.27</c> at rmse 12.92.</para>
        /// <para>That intercept is a bed of light our transport lays down where retail has none,
        /// and it is what made dim surfaces read 28% too bright while mid and bright ones looked
        /// like parity - the two "error families" chased for weeks are ONE line with a slope and
        /// an intercept, and they cancel at retail luma ~57. Dropping W0 to 200 removes nearly all
        /// of it; the level is then too dark overall, which
        /// <see cref="SurfaceLightWeightScale"/> puts back. Use W0 to set the intercept to zero,
        /// then that to set the slope. Together (200, 1.20) they measure
        /// <c>0.916 x retail + 1.54</c> at rmse 12.00 / nrmse 11.72.</para>
        /// <para>HISTORY: the retail-calibrated trio (227, 2.43, 0.46) once measured WORSE here
        /// (14.83, dim surfaces to 1.32) and was rejected - but that was judged against the
        /// pre-2026-08-27 light table, which was missing 85% of its emitters at 2x weight.
        /// Re-tested cleanly against the decoded table (AuthoredOff admission fix, texel-less
        /// emitters, K=250), the retail trio wins or holds on both levels tried: ChallengeMap3
        /// 0.763 -&gt; 0.854 luma AND stable rmse 16.28 -&gt; 14.53; Solace 0.562 -&gt; 0.640 luma at
        /// flat rmse (17.93 -&gt; 18.03). It is now the default - it is retail's own calibration,
        /// and every measurement that ever spoke against it is known to be confounded.</para>
        /// </remarks>
        public float InfluenceCurveW0 = 227.0f;

        /// <summary>Influence falloff curve: distance at which falloff reaches (1/2)^k.</summary>
        public float InfluenceCurveD0 = 2.43f;

        /// <summary>Influence falloff curve: how hard the curve compresses.</summary>
        public float InfluenceCurveK = 0.46f;

        /// <summary>
        /// Multiply sampled albedo by the material's DIFFUSE_TINT.
        /// </summary>
        /// <remarks>
        /// <para>On. The tint is not decoration - it carries most of a material's brightness.
        /// Cathode's environment materials share a handful of near-white base textures and colour
        /// them per material: BSP_TORRENS' orange panels sample <c>plastic_base[d]</c>, whose mean
        /// is RGB (232,232,230), and get their colour entirely from a tint of
        /// (0.761, 0.29, 0.082).</para>
        /// <para>Retail applies it the same way, and multiplicatively against the <em>linearised</em>
        /// texture: dividing retail's per-material albedo through by the tint recovers the
        /// linearised mean of its diffuse map to within a unit in 255 across the levels' biggest
        /// materials. One cluster diverges - the 26-parameter CA_ENVIRONMENT permutation, where
        /// retail's albedo ignores the tint entirely - but that reads as a quirk of their compiler
        /// rather than a rule, since honouring it would make dark plastics bounce like white paint.
        /// </para>
        /// </remarks>
        public bool ApplyDiffuseTint = true;

        /// <summary>
        /// Ignore DIFFUSE_TINT for the 26-remap CA_ENVIRONMENT shader permutation, as retail's
        /// compiler does.
        /// </summary>
        /// <remarks>
        /// <para>Verified per mover on ChallengeMap4: for materials on this permutation the ratio
        /// of our stored input-probe albedo to retail's equals the material's DIFFUSE_TINT exactly
        /// (mover 2511: tint 0.09, ratio 0.098; mover 2372: tint 0.43, ratio 0.43). Retail stores
        /// the untinted linearised diffuse map; with the tint applied we under-drove every
        /// dark-tinted plastic's bounce by 2-10x. This is where the "luminous ceiling" cases came
        /// from too - CEILING_HZDLAB's dark look is a 0.09 tint over a near-white base texture,
        /// and retail bounces the base (~175), which also matches the EMI panel diffusers.</para>
        /// <para>MEASURED AND REFUTED as a blanket rule, so OFF: with it on, CM4's whole-file
        /// albedo overshoots to 3.24x retail (CM3 1.22x, Torrens 1.37x), Torrens' render regresses
        /// 17.05 -> 19.56 (slope 0.997 -> 1.137), and classifying every 26-remap mover on CM4
        /// against retail shows the SAME material+tint+flags is tinted on 222 movers and untinted
        /// on only 25. The untinted minority is a per-MOVER phenomenon - the leading explanation is
        /// coincident state-variant movers whose lit sibling uses the same base texture with a
        /// white tint (e.g. HAB_Grid_White over plastic_base), which reproduces the exact
        /// ratio-equals-tint signature without any compiler rule. See the memory note
        /// radiosity-albedo-decode.</para>
        /// </remarks>
        public bool UntintedEnvironment26 = false;


        /// <summary>
        /// Longest edge, in texels, of the diffuse mip the albedo sampler decodes and keeps.
        /// </summary>
        /// <remarks>
        /// The decoder takes the largest mip inside this bound, so the texture's own prefiltering
        /// does the downsampling. A retail level references about 220 diffuse maps with a median
        /// persistent size of 128, so 256 costs roughly 13 MB of decoded cache and leaves nearly
        /// every map at its full persistent resolution.
        /// </remarks>
        public int AlbedoTextureMaxEdge = 256;

        // ---- Geometry filtering ------------------------------------------------------------

        /// <summary>
        /// Only bake composites that contain at least one mover requiring <c>RADIOSITY_STATIC</c>.
        /// </summary>
        /// <remarks>
        /// This is what keeps the slice count near retail's. Without it Solace wants 23 slices
        /// against retail's 3, because roughly two thirds of its composites get a lightmap rect they
        /// have no shader to sample.
        /// </remarks>
        public bool StaticRadiosityCompositesOnly = true;

        /// <summary>
        /// Renderable elements baked per mover, or 0 for all of them.
        /// </summary>
        /// <remarks>
        /// <para>A mover's elements are its separate submesh and material pairs, all of which
        /// draw, so baking only the first discards most of the model - on Solace it drops 15433 m2
        /// of 26436, including 3751 of the level's 6163 m2 of floor. Raising this to take them all
        /// closes most of the floor coverage gap: cells where retail has floor probes and we have
        /// only walls fall from 9.3% to 5.2%, and the overall cell gap from 36.3% to 21.7%.</para>
        /// <para>0 takes them all, which is the correct behaviour; the cap exists only as an escape
        /// hatch for a level with pathological movers - Solace has a few carrying 29 and even 143
        /// elements.</para>
        /// </remarks>
        public int MaxElementsPerMover = 0;

        /// <summary>Skip movers flagged <c>NO_RENDER</c>.</summary>
        public bool SkipNonRendered = true;

        /// <summary>
        /// Skip movers that are not <c>Stationary</c>. Radiosity is a static bake; moving
        /// geometry is lit through the volume probes instead.
        /// </summary>
        public bool StaticGeometryOnly = true;

        /// <summary>
        /// Drop triangles with an edge longer than this (metres). Guards against decode
        /// outliers blowing out the BVH bounds, mirroring the navmesh soup's filter.
        /// </summary>
        public float MaxTriangleEdge = 256.0f;

        /// <summary>Run the per-slice solve across all cores.</summary>
        public bool Parallel = true;

        public static RadiosityBakeSettings CreateDefault() => new RadiosityBakeSettings();
    }
}
#endif
