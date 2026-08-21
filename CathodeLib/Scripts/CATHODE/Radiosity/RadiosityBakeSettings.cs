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
        public int MaxTexelsPerSlice = 13000;

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
        /// nearest-neighbour distance of roughly 1.1x it. 0.46 puts our probe count and spacing on
        /// retail's - 22743 probes at 0.455 mean on Solace - while leaving headroom under the
        /// 12288 per-slice ceiling.</para>
        /// </remarks>
        public float InputProbeSpacing = 0.56f;

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

        /// <summary>Cross-slice influence patches emitted per surface probe.</summary>
        public int MaxCrossSliceFixupsPerProbe = 4;

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
        public float SurfaceLightWeightScale = 1.20f;

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
        /// <para>This is a calibration, not a decode. Retail's light RGB is not the emissive tint
        /// at all - only 1 of Solace's 76 distinct tints appears anywhere in retail's 28-value set -
        /// and the likeliest true source is the emitter's emissive map rather than its tint
        /// constant. Until that is decoded, matching the measured chroma is the closest we get.</para>
        /// </remarks>
        public float SurfaceLightSaturation = 0.25f;

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
        /// <para>The retail-calibrated trio (227, 2.43, 0.46) measures WORSE here (14.83, and dim
        /// surfaces go to 1.32) - see InfluenceWeight for the history.</para>
        /// </remarks>
        public float InfluenceCurveW0 = 200.0f;

        /// <summary>Influence falloff curve: distance at which falloff reaches (1/2)^k.</summary>
        public float InfluenceCurveD0 = 2.0f;

        /// <summary>Influence falloff curve: how hard the curve compresses.</summary>
        public float InfluenceCurveK = 0.32f;

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
