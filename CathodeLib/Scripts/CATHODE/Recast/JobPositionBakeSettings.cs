#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Constants for <see cref="JobPositionBaker"/>. The offsets are not guesses: every one of
    /// them was read back off retail's own files and holds to three decimal places on
    /// BSP_TORRENS, Solace, SCI_Hub and Tech_Hub.
    /// </summary>
    public sealed class JobPositionBakeSettings
    {
        // Spotting positions are laid out along a run of cover exactly as assault positions are,
        // with their own set of constants. The pair is written relative to the wall:
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

        /// <summary>
        /// Separate minimum run length for runs whose obstacle can be seen OVER - below the cover
        /// dividing line. 0 uses <see cref="SpottingCoverLengthToGenerateOnePoint"/> for both.
        /// </summary>
        public float SpottingCoverLengthToGenerateOnePointLow = 0f;

        /// <summary>Obstacle height at or above which a run counts as a tall wall. See <see cref="SpottingCoverLengthToGenerateOnePointLow"/>.</summary>
        public float SpottingLowHighDividingLine = 1.5f;

        /// <summary>At or above this length a run gets a position at both ends rather than one.</summary>
        public float SpottingCoverLengthToGenerateAtBothEnds = 4.0f;

        /// <summary>Longer runs get intermediate positions so no gap exceeds this.</summary>
        public float SpottingMaxDistanceBetweenPositionsOnSameCover = 10.0f;

        /// <summary>Spotting jobs closer together than this collapse into one.</summary>
        public float SpottingMergeDistance = 0.25f;

        // Assault positions are laid out along a run of cover, and these five carry the
        // names and values for that. A run shorter than OnePoint gets nothing; up to BothEnds
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
        public bool AssaultRequireStandingFloor = true;

        /// <summary>
        /// Build the spotting runs only from the rim of standing-height floor.
        /// </summary>
        public bool SpottingRequireStandingFloor = true;

        /// <summary>
        /// Path of a learned SPOTTING selector (a boosted-tree model from <c>diag coverml trainall
        /// spot</c>). When set and readable, spotting jobs
        /// are laid at the peaks of the model's probability along every standing rim run, read
        /// off <see cref="LearnedCoverFeatures"/> every 0.25 m, greedily highest first with
        /// <see cref="LearnedSpotSeparation"/> between jobs, instead of the length rule. Held out
        /// level by level on retail navmeshes this scores 78.6 position F1 against the rule's 72.7.
        /// </summary>
        public string LearnedSpotModelPath = null;

        /// <summary>Probability at or above which a station may carry a job; 0 uses the model file's threshold. The end-to-end sweep (position F1 against retail, held out over the campaign) prefers 0.45 to the models' station-optimal 0.38-0.42, trading a little recall for precision.</summary>
        public float LearnedSpotThreshold = 0.45f;

        /// <summary>
        /// With no model path, use the spotting and
        /// assault selectors CathodeLib ships embedded (<see cref="LearnedCover.EmbeddedSpot"/>,
        /// <see cref="LearnedCover.EmbeddedAssault"/>). Set false - or a path of "none" - for the
        /// length rules. Campaign-wide (5 Sep 2026) jobs 71.1 -> 75.9, up on every level. Crawl has
        /// no embedded model: the learned one over-produced, the mouth rule stays.
        /// </summary>
        public bool UseEmbeddedLearnedSelectors = true;

        /// <summary>Minimum distance between two learned spotting jobs, metres. Retail puts two within 0.75 m 5.6% of the time.</summary>
        public float LearnedSpotSeparation = 0.80f;

        /// <summary>
        /// Where in each station interval a learned job pass samples, as a fraction. 0.5 is the
        /// midpoint. The station COUNT and spacing do not change; only the phase does.
        /// </summary>
        public float LearnedStationPhase = 0.5f;

        /// <summary>
        /// Path of a learned ASSAULT selector (<c>diag coverml trainall assault</c>).
        /// Assault positions then go to the peaks of the model's
        /// probability along every standing run, highest first with
        /// <see cref="LearnedAssaultSeparation"/> between them, instead of the length rule and
        /// its obstacle and wall-length gates. Held out level by level on retail navmeshes: 75.2
        /// position F1 against the rule's 61-69.
        /// </summary>
        public string LearnedAssaultModelPath = null;

        /// <summary>Probability at or above which a station may carry an assault position; 0 uses the model file's threshold. The end-to-end sweep (held out over the campaign) sits flat between 0.40 and 0.45 with 0.45 a shade ahead, so it is fixed here.</summary>
        public float LearnedAssaultThreshold = 0.45f;

        /// <summary>Minimum distance between two learned assault positions, metres. Retail's along-run gap is p5 1.08.</summary>
        public float LearnedAssaultSeparation = 1.00f;

        /// <summary>
        /// Path of a learned CRAWL-SPACE selector (<c>diag coverml trainall covered ... crawlml_</c>).
        /// Crawl jobs then go to the peaks of the model's probability along
        /// the OUTER rim of deep-crouch floor (where 85% of retail's sit), highest first with
        /// <see cref="LearnedCrawlSeparation"/> between them; the task position runs out through the
        /// nearest mouth as the rule's does.
        /// </summary>
        public string LearnedCrawlModelPath = null;

        /// <summary>Probability at or above which a deep-rim station may carry a crawl job; 0 uses the model file's threshold.</summary>
        public float LearnedCrawlThreshold = 0f;

        /// <summary>Minimum distance between two learned crawl jobs, metres.</summary>
        public float LearnedCrawlSeparation = 0.5f;

        /// <summary>
        /// Build the assault runs from the baked COVER rather than from raw navmesh rim.
        /// </summary>
        public bool AssaultFromCover = false;

        /// <summary>
        /// How far outside the navmesh rim a cover segment's face sits, so the run can be put back
        /// on the rim before the usual inset is applied. Matches CoverBakeSettings.RimOffset.
        /// </summary>
        public float AssaultCoverRimOffset = 0.2925f;

        /// <summary>
        /// Reject an assault position with nothing tall enough in front of it to fight from behind.
        /// </summary>
        public bool AssaultRequireObstacle = true;

        /// <summary>See <see cref="AssaultRequireObstacle"/>.</summary>
        public float AssaultMinObstacleHeight = 1.8f;

        /// <summary>
        /// Reject a whole RUN whose mean obstacle height is below this, rather than testing each
        /// position separately. OFF by default.
        /// </summary>
        public bool AssaultRequireRunObstacle = false;
        /// <summary>
        /// Face an assault position along the rim edge it actually stands on, rather than along the
        /// length-weighted mean normal of the whole run.
        /// </summary>
        public bool AssaultYawFromLocalEdge = false;

        /// <summary>See <see cref="AssaultRequireRunObstacle"/>.</summary>
        public float AssaultRunMeanObstacleHeight = 1.8f;


        /// <summary>
        /// Reject a whole run whose wall is too short to fight from, measured along the WALL rather
        /// than along the navmesh rim.
        /// </summary>
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
        public float AssaultRimToCollision = 0.2925f;

        /// <summary>Two assault positions closer together than this collapse into one.</summary>
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
        public bool SpottingRequireObstacle = true;

        /// <summary>See <see cref="SpottingRequireObstacle"/>.</summary>
        public float SpottingMinObstacleHeight = 0.7f;

        /// <summary>
        /// A spotting run at least this long keeps its positions even where the per-position obstacle
        /// test fails; 0 applies the test to every run. Bake-free on retail's mesh (`diag spotsweep all
        /// exempt`, 13 levels, 2 Sep 2026): a leave-one-level-out tree over the run profile says that
        /// with an obstacle a run needs ~0.9 m, and WITHOUT one retail still places from ~2.4 m, which
        /// the gate refused outright. Gate alone F1 73.9; exempting runs of 1.5 / 2.0 / 2.4 m: 74.4 /
        /// 74.4 / 74.4 (recall +2.4, precision -0.9). In the baker (`diag jobiter`) it is noise - CM11
        /// 70.2 -> 70.2 at 2.4 and worse below, Tech_Hub 70.1 -> 70.5 - because the collision-read
        /// obstacle probe already passes most of what the navmesh-soup probe refused. Left off.
        /// </summary>
        public float SpottingObstacleExemptLength = 0f;

        /// <summary>
        /// How far consecutive rim edges may turn and still count as one continuous run of cover.
        /// Ours alone - retail's positions follow cover volumes, we have to rebuild the
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
        public float CrawlMinDistanceInsideDeepCrouchForSpotPosition = 0.25f;

        /// <summary>Spot and path have to end up further apart than this.</summary>
        public float CrawlMinSpotToPathDistance = 0.5f;

        /// <summary>
        /// Directions the crawl-space depth probe tries, in degrees off the mouth edge's normal.
        /// </summary>
        public float[] CrawlProbeFanDegrees = { 0f, -30f, 30f };

        /// <summary>
        /// Judge the crawl space as a whole before placing in it: drop every mouth of a
        /// deep-crouch region whose own outer wall never gets <see cref="CrawlMinRegionDepth"/>
        /// away from a way in.
        /// </summary>
        public bool CrawlRequireRegionDepth = false;

        /// <summary>See <see cref="CrawlRequireRegionDepth"/>.</summary>
        public float CrawlMinRegionDepth = 0.5f;

        /// <summary>Two crawl-space jobs are never placed closer together than this.</summary>
        public float CrawlMinSeparation = 0.50f;

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
