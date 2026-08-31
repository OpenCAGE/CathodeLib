#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Constants for <see cref="JobPositionBaker"/>. The offsets are not guesses: every one of
    /// them was read back off retail's own files and holds to three decimal places on
    /// BSP_TORRENS, Solace, SCI_Hub and Tech_Hub. See the remarks on the baker.
    /// </summary>
    public sealed class JobPositionBakeSettings
    {
        // Spotting positions are laid out along a run of cover exactly as assault positions are,
        // with their own set of the engine's constants. The pair is written relative to the wall:
        // the job sits ExtraDistanceFromCollision out from the collision surface - so just outside
        // the eroded navmesh - and the task PathPositionDistanceOffset further in from the job.

        /// <summary>How far in from each end of a cover run the outermost spotting jobs sit.</summary>
        public float SpottingMinDistanceFromEdgeOfCover = 0.75f;

        /// <summary>Nominal stand-off of the spotting job from the cover. Retail ships zero.</summary>
        public float SpottingPositionDistanceOffset = 0.0f;

        /// <summary>Clearance kept from the collision surface itself, on top of the offset.</summary>
        public float SpottingExtraDistanceFromCollision = 0.03f;

        /// <summary>
        /// How far the task position sits from the job position, along the inward normal. Measured
        /// from the job, not from the geometry, which is what makes retail's pairs exactly 1 m
        /// apart - 147 of BSP_TORRENS' 169.
        /// </summary>
        public float SpottingPathPositionDistanceOffset = 1.0f;

        /// <summary>A cover run shorter than this produces no spotting position at all.</summary>
        public float SpottingCoverLengthToGenerateOnePoint = 0.8f;

        /// <summary>At or above this length a run gets a position at both ends rather than one.</summary>
        public float SpottingCoverLengthToGenerateAtBothEnds = 4.0f;

        /// <summary>Longer runs get intermediate positions so no gap exceeds this.</summary>
        public float SpottingMaxDistanceBetweenPositionsOnSameCover = 10.0f;

        /// <summary>Spotting jobs closer together than this collapse into one.</summary>
        public float SpottingMergeDistance = 0.25f;

        // Assault positions are laid out along a run of cover, and these five carry the engine's
        // own names and values for that. A run shorter than OnePoint gets nothing; up to BothEnds
        // it gets a single point at its middle; beyond that one point MinDistanceFromEdgeOfCover
        // in from each end, with more spaced evenly between them so no gap exceeds
        // MaxDistanceBetweenPositionsOnSameCover.

        /// <summary>How far in from each end of a cover run the outermost positions sit.</summary>
        public float AssaultMinDistanceFromEdgeOfCover = 1.5f;

        /// <summary>
        /// How far the position stands off the wall. Measured from the geometry, so the inset from
        /// the navmesh rim is this less <see cref="AssaultRimToCollision"/> - 0.5 - 0.2925 = 0.2075,
        /// against the 0.21 median retail ships (p25, p50 and p75 all 0.21 over 1,445 positions).
        /// </summary>
        public float AssaultDistanceFromGeometry = 0.5f;

        /// <summary>A cover run shorter than this produces no assault position at all.</summary>
        public float AssaultCoverLengthToGenerateOnePoint = 3.0f;

        /// <summary>At or above this length a run gets a position at both ends rather than one.</summary>
        public float AssaultCoverLengthToGenerateAtBothEnds = 4.0f;

        /// <summary>Longer runs get intermediate positions so no gap exceeds this.</summary>
        public float AssaultMaxDistanceBetweenPositionsOnSameCover = 10.0f;

        /// <summary>
        /// Build assault runs only from the rim of standing-height floor.
        /// </summary>
        /// <remarks>
        /// An assault position is somewhere an NPC stands and fights from, and retail never puts one
        /// on crouch or deep-crouch navmesh: 46 of 46 on SCI_Hub and 44 of 44 on BSP_TORRENS stand
        /// on standing-height floor, against 69% of ours.
        /// </remarks>
        public bool AssaultRequireStandingFloor = true;

        /// <summary>
        /// Build the spotting runs only from the rim of standing-height floor.
        /// </summary>
        /// <remarks>
        /// This was off, on the reading that retail's spotting jobs are only 83-97% on standing
        /// floor so the crouch ones must be intended. That reading was wrong: a spotting job sits
        /// 0.293 m OUTSIDE the rim, off the navmesh, so asking which polygon it lands on is
        /// ambiguous at exactly the crouch/standing boundary - which is where all the disagreement
        /// sits. The direct test settles it. Placing jobs by the rule on retail's own navmesh, with
        /// and without the filter, over all 31 levels: RECALL IS UNCHANGED on every one (identical
        /// to a tenth of a point, occasionally higher) while precision rises everywhere. Crouch rim
        /// contributes nothing but false positives. Mean spotting F1 +4.5 points, better on 31 of
        /// 31: ChallengeMap9 55.8 -> 68.4, ENG_TowPlatform 56.8 -> 66.2, ChallengeMap7 66.6 -> 75.2,
        /// Tech_RnD_HzdLab 62.5 -> 71.1, ChallengeMap11 62.8 -> 69.9, SCI_AndroidLab 65.7 -> 72.4,
        /// Tech_Hub 69.2 -> 73.1, SCI_Hub 71.2 -> 72.2.
        /// </remarks>
        public bool SpottingRequireStandingFloor = true;

        /// <summary>
        /// Build the assault runs from the baked COVER rather than from raw navmesh rim.
        /// </summary>
        /// <remarks>
        /// The engine's own parameter names for this pass all say "cover" - one point per run of
        /// cover, a minimum distance from the edge of cover - and it runs over cover volumes. Bare
        /// rim stands in for those badly: 35% of the positions we produce are more than 5 m from
        /// any retail one, because a stretch of wall with nothing to hide behind still counts as a
        /// run. Cover carries the obstacle, thickness and open-floor gates already.
        /// </remarks>
        public bool AssaultFromCover = false;

        /// <summary>
        /// How far outside the navmesh rim a cover segment's face sits, so the run can be put back
        /// on the rim before the usual inset is applied. Matches CoverBakeSettings.RimOffset.
        /// </summary>
        public float AssaultCoverRimOffset = 0.2925f;

        /// <summary>
        /// Reject an assault position with nothing tall enough in front of it to fight from behind.
        /// </summary>
        /// <remarks>
        /// Measured on SCI_Hub: 0 of retail's 46 assault positions face open floor and their tenth
        /// percentile obstacle stands 1.50 m, while 31% of ours face nothing at all. Of the 85 we
        /// produce that retail does not, 59% fail this test; of the 29 that land on a retail one,
        /// none do.
        /// </remarks>
        public bool AssaultRequireObstacle = true;

        /// <summary>See <see cref="AssaultRequireObstacle"/>.</summary>
        public float AssaultMinObstacleHeight = 1.8f;

        /// <summary>
        /// Reject a whole RUN whose mean obstacle height is below this, rather than testing each
        /// position separately. OFF by default - see the remarks.
        /// </summary>
        /// <remarks>
        /// Retail's assault selection is a per-wall decision and, unlike cover's, it is learnable.
        /// Labelling every rim run of 3 m or more on twelve levels with whether retail put an assault
        /// position on it (1,525 runs, 52.9% positive) and searching twenty aggregated features
        /// exhaustively, the mean obstacle top over the RUN reaches F1 **83.5% on its own** at 1.63
        /// (P 76.8 / R 91.4); the best six-term conjunction only reaches 84.3. Mean wall-end distance
        /// is close behind at 83.4, and note its direction - assault wants walls FAR from an end,
        /// the opposite of cover, consistent with the two being anti-correlated.
        /// <para>**It does not transfer**, and the reason is worth remembering: 83.5% is a RUN-level
        /// score - it measures which walls get chosen - while the harness matches POSITIONS within
        /// 0.75 m. Our wall selection is evidently already close to retail's, because at the learned
        /// 1.63 this gate never fires at all (the per-position test at 1.8 has removed those runs
        /// already), and at 1.8 it is worth +1.2 on SCI_Hub and +0.5 on ChallengeMap9 - about +0.1 of
        /// job score for a ray probe every 0.5 m of every run. The assault loss is in WHERE along a
        /// chosen wall the positions land, and in run construction, not in which wall is chosen.</para>
        /// </remarks>
        public bool AssaultRequireRunObstacle = false;
        /// <summary>
        /// Face an assault position along the rim edge it actually stands on, rather than along the
        /// length-weighted mean normal of the whole run.
        /// </summary>
        /// <remarks>
        /// **OFF - measured, and it is not the cause.** The yaw is the only orientation we write in
        /// any of the four systems and it is 7.7 degrees out against retail on SCI_Hub's matched
        /// assault positions. The obvious suspect was the run's mean normal diverging from the local
        /// wall on a curve, but switching to the local edge moved the error from 7.697 to 7.706
        /// degrees - nothing. On the positions that match, the run is straight enough that the two
        /// normals agree, so the 7.7 comes from somewhere else. Kept as a knob so the hypothesis is
        /// not retried.
        /// </remarks>
        public bool AssaultYawFromLocalEdge = false;

        /// <summary>See <see cref="AssaultRequireRunObstacle"/>.</summary>
        public float AssaultRunMeanObstacleHeight = 1.8f;


        /// <summary>
        /// Reject a whole run whose wall is too short to fight from, measured along the WALL rather
        /// than along the navmesh rim.
        /// </summary>
        /// <remarks>
        /// Assault is a which-wall problem, unlike spotting, which is a how-many problem. Splitting
        /// the error with an oracle - keep our runs and our along-run layout but take the ANSWER
        /// from retail - gives 61.5 shipped, 80.6 for perfect binary run selection and 90.4 for a
        /// perfect count per run, so the walls we choose are most of what is wrong.
        /// <para>A greedy search over thirty aggregated rim features, scored directly at POSITION
        /// level over thirteen levels, takes exactly one term: <c>max over the run of
        /// WallEndDistance</c>. Nothing else adds more than 0.3, and held-out refits match the
        /// fitted rule on every level, which is what you expect of one threshold on one feature.
        /// </para>
        /// <para>What it means physically: the run has to contain at least one point with a metre
        /// of unbroken wall on BOTH sides of it. This is not run length - a 4 m rim run tracing
        /// three sides of a pillar fails it, and a 3 m run along a corridor wall passes.</para>
        /// <para>**Read the size of this honestly.** On retail's own navmesh with no other gate it
        /// is worth 61.5 -> 70.6, but the per-position obstacle test we already ship
        /// (<see cref="AssaultRequireObstacle"/>) reaches 68.6 on its own and the two are
        /// SUBSTITUTES - ANDing them scores 68.5, below either. What it is actually worth in the
        /// baker, on our navmesh and on top of every gate already there, is about a point, and the
        /// threshold that carries it is 1.00 rather than the 1.25 the bake-free search picks: our
        /// rim and the collision set this probe reads are both different from that search's.
        /// Per level, shipped -> this plus <see cref="AssaultMergeDistance"/> and
        /// <see cref="AssaultRimToCollision"/>: ChallengeMap9 53.1 -> 55.9, TECH_HUB 61.6 -> 62.9,
        /// SCI_Hub 55.6 -> 58.8, ChallengeMap11 61.6 -> 61.9. Positive on all four, mean +1.9.
        /// Dropping the obstacle gate in its favour is NOT safe - it wins on ChallengeMap9 (57.9)
        /// and loses on the other three.</para>
        /// </remarks>
        public bool AssaultRequireWallLength = true;

        /// <summary>
        /// See <see cref="AssaultRequireWallLength"/>. The probe steps in 0.25 m, so this is
        /// effectively quantised; swept at 0.75 / 1.00 / 1.25 / 1.50 / 1.75 / 2.00 in the baker.
        /// </summary>
        public float AssaultMinWallEndDistance = 1.0f;


        /// <summary>
        /// How far inside the collision surface the eroded navmesh rim sits, so
        /// <see cref="AssaultDistanceFromGeometry"/> can be turned into an inset from the rim.
        /// </summary>
        /// <remarks>
        /// This was the walkable radius, 0.3125, which put the position 0.1875 in from the rim
        /// against the 0.208 median retail ships. It is the wrong number: the rim sits 0.2925 inside
        /// the collision - the same offset the cover baker writes its faces at, and the same 0.29
        /// that retail's spotting jobs sit OUTSIDE the rim (p25, p50 and p75 all 0.29 over 20,470
        /// of them). 0.5 - 0.2925 = 0.2075 against a measured 0.21 at p25, p50 and p75 of retail's
        /// 1,445 assault positions. Worth +0.2.
        /// </remarks>
        public float AssaultRimToCollision = 0.2925f;

        /// <summary>Two assault positions closer together than this collapse into one.</summary>
        /// <remarks>
        /// There was no merge at all on this pass. Retail's own along-run gap between consecutive
        /// assault positions has p5 1.08 and p10 1.18 over 478 pairs, so it never ships a pair on
        /// top of another; ours does, wherever two runs meet at a corner. Swept at 0.25 / 0.40 /
        /// 0.50 / 0.60 / 0.80 / 1.00 both bake-free and in the baker: 0.50 is best or tied
        /// everywhere, and past 0.80 it costs recall faster than it buys precision. On its own in
        /// the baker it is worth +0.8 on TECH_HUB, +1.0 on ChallengeMap9 and +0.3 on ChallengeMap11.
        /// </remarks>
        public float AssaultMergeDistance = 0.5f;

        /// <summary>How far in front of the position to look for that obstacle.</summary>
        public float AssaultObstacleProbeDistance = 1.0f;

        /// <summary>Stop scanning upwards here - anything this tall is cover enough.</summary>
        public float AssaultObstacleMaxHeight = 3.0f;

        /// <summary>Vertical step of the obstacle scan.</summary>
        public float AssaultObstacleHeightStep = 0.125f;

        /// <summary>
        /// The same test for spotting positions, at a much lower bar - a spotting position is a
        /// place to look from, not a place to fight from, so a knee-high edge counts.
        /// </summary>
        /// <remarks>
        /// Swept on four levels: 0.7 m is the best value everywhere and 0.9 collapses recall, which
        /// puts the cliff on <see cref="NavMeshBakeSettings.DeepCrouchHeight"/> at 0.875 - above it
        /// the test starts throwing away the crawl-height ledges retail spots from.
        /// </remarks>
        public bool SpottingRequireObstacle = true;

        /// <summary>See <see cref="SpottingRequireObstacle"/>.</summary>
        public float SpottingMinObstacleHeight = 0.7f;

        /// <summary>
        /// How far consecutive rim edges may turn and still count as one continuous run of cover.
        /// Ours, not the engine's - the engine works from cover volumes, we have to rebuild the
        /// runs from the navmesh rim. Swept at 5 / 15 / 30 degrees against retail; 15 is the best
        /// balance of match against over-production.
        /// </summary>
        public float RunMaxTurnDegrees = 15.0f;

        // Crawl-space positions work off the edges where a deep-crouch region meets ordinary
        // floor. The spot is pushed back into the crawl space and the path position out of it.

        /// <summary>How far back into the crawl space the spotting position is pushed.</summary>
        public float CrawlSpottingPositionDistanceOffset = 1.5f;

        /// <summary>How far out of the crawl space the path position sits.</summary>
        public float CrawlPathPositionDistanceOffset = 0.5f;

        /// <summary>
        /// The spot has to get at least this far into the deep-crouch region or the edge is
        /// dropped - a crawl space too shallow to hide in is not worth a job.
        /// </summary>
        /// <remarks>
        /// These three were all set to reject a shallow spot, and together they were throwing away
        /// half of retail's crawl jobs. Retail places 0.86 jobs per mouth on a one-mouth deep-crouch
        /// region and 4.0 on a six-mouth one, over 1,000 regions on 31 levels; we were managing
        /// about 0.42. The gates are not wrong in principle - retail's own job-to-task spans run
        /// p10 1.06 / p50 1.33 / p90 2.00, so its spots really are 0.5-1.5 m in - but the DEPTH
        /// PROBE is fragile: it walks inward from the middle of a mouth edge in 0.0625 m steps and
        /// stops at the first sample outside the region, so a vent entered from its side reads as
        /// shallow and the job is dropped even though retail puts one in that vent. Relaxing the
        /// three gates recovers those without costing precision. Crawl F1: ChallengeMap11
        /// 36.1 -> 49.7, Tech_Hub 44.2 -> 57.3, ChallengeMap9 59.2 -> 71.2, SCI_HospitalLower
        /// 34.1 -> 35.4. Better on all four, mean +10.0. The cause itself is addressed separately by
        /// <see cref="CrawlProbeFanDegrees"/>, which fans the probe out instead of trusting one normal.
        /// </remarks>
        public float CrawlMinDistanceInsideDeepCrouchForSpotPosition = 0.25f;

        /// <summary>Spot and path have to end up further apart than this. See the remarks above.</summary>
        public float CrawlMinSpotToPathDistance = 0.5f;

        /// <summary>
        /// Directions the crawl-space depth probe tries, in degrees off the mouth edge's normal.
        /// </summary>
        /// <remarks>
        /// The probe walks inward from the middle of a mouth edge and stops at the first sample
        /// outside the deep-crouch region, so along the normal alone a vent entered from its SIDE
        /// reads as shallow and the job is dropped even though retail puts one there. Fanning out
        /// and keeping the deepest direction that stays inside is the fix for the cause rather than
        /// the symptom. Crawl F1 against the single ray: ChallengeMap16 57.4 -> 64.1, ChallengeMap12
        /// 41.4 -> 43.5, Tech_RnD 57.1 -> 59.3. WIDER IS NOT BETTER - a +-20/40/60/80 fan wins the
        /// first two outright (68.6 and 46.3) but collapses Tech_RnD to 50.4, and +-30/55 does the
        /// same, so +-30 is the only setting that is positive on all three. Set to a single 0 to get
        /// the old single-ray behaviour back.
        /// </remarks>
        public float[] CrawlProbeFanDegrees = { 0f, -30f, 30f };

        /// <summary>
        /// Judge the crawl space as a whole before placing in it: drop every mouth of a
        /// deep-crouch region whose own outer wall never gets <see cref="CrawlMinRegionDepth"/>
        /// away from a way in.
        /// </summary>
        /// <remarks>
        /// The other depth test is per MOUTH - it probes inward from one edge and stops at the spot
        /// offset - so a region with one deep pocket and one shallow entrance gets judged twice and
        /// inconsistently. Retail decides per region. Splitting the crawl error with an oracle on
        /// retail's own navmesh gives 72.1 shipped, 78.9 for perfect binary region selection and
        /// 86.3 for a perfect count per region, and a greedy search over sixteen region features
        /// scored at position level takes one term, region depth at 0.48, worth 72.1 -> 76.6.
        /// Nothing else adds more than 0.2.
        /// <para>**OFF, because it does not transfer to our navmesh.** Swept in the baker on
        /// SCI_HospitalLower, crawl F1 against 55.0 shipped: 46.8 at 0.25, 41.8 at 0.375, 35.6 at
        /// 0.50, 22.1 at 0.75. It throws away 78 of 140 jobs at 0.50 and recall falls from 60.2%
        /// to 27.1%. The reason is what the depth is measured FROM: a region's depth here is how
        /// far its own outer wall - edges with no neighbour at all - gets from a way in, and our
        /// deep-crouch regions mostly have no such wall. They sit under furniture with walkable
        /// floor continuing on every side, so every edge is a mouth, the wall list is empty and
        /// the depth reads zero. Retail's vents are bounded by real geometry. Kept as a knob so
        /// the hypothesis is not retried without fixing the measurement first.</para>
        /// </remarks>
        public bool CrawlRequireRegionDepth = false;

        /// <summary>See <see cref="CrawlRequireRegionDepth"/>.</summary>
        public float CrawlMinRegionDepth = 0.5f;

        /// <summary>Two crawl-space jobs are never placed closer together than this.</summary>
        public float CrawlMinSeparation = 0.25f;

        // The glass wall test. A ray is swept through the cover at chest height, from
        // StartDistance on the walkable side to EndDistance on the far side (negative, so it
        // passes through), and the position is dropped if it finds glass. Standing behind a pane
        // is not cover, and there is nothing to peer round.

        public bool GlassWallTest = true;
        public float AssaultGlassTestStartDistance = 0.2f;
        public float AssaultGlassTestEndDistance = -1.5f;
        public float AssaultGlassTestRayHeightOffset = 1.3f;
        public float AssaultGlassTestRayRadius = 0.15f;

        public float SpottingGlassTestStartDistance = 0.2f;
        public float SpottingGlassTestEndDistance = -0.8f;
        public float SpottingGlassTestRayHeightOffset = 0.5f;
        public float SpottingGlassTestRayRadius = 0.1f;

        /// <summary>Cell size of the lookup grid the three files are binned into.</summary>
        public float GridUnitSize = 10.0f;

        /// <summary>
        /// The radius the navmesh was eroded by, so distances measured from wall geometry can be
        /// converted to distances from the rim. Must match NavMeshBakeSettings.WalkableRadius.
        /// </summary>
        public float WalkableRadius = 0.3125f;

        public static JobPositionBakeSettings CreateDefault() => new JobPositionBakeSettings();
    }
}
#endif
