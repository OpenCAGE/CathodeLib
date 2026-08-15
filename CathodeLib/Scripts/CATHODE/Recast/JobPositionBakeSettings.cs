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
        /// the navmesh rim is this less the walkable radius the mesh is already eroded by -
        /// 0.5 - 0.3125 = 0.1875, against the 0.208 median retail actually ships.
        /// </summary>
        public float AssaultDistanceFromGeometry = 0.5f;

        /// <summary>A cover run shorter than this produces no assault position at all.</summary>
        public float AssaultCoverLengthToGenerateOnePoint = 3.0f;

        /// <summary>At or above this length a run gets a position at both ends rather than one.</summary>
        public float AssaultCoverLengthToGenerateAtBothEnds = 4.0f;

        /// <summary>Longer runs get intermediate positions so no gap exceeds this.</summary>
        public float AssaultMaxDistanceBetweenPositionsOnSameCover = 10.0f;

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
        public float CrawlMinDistanceInsideDeepCrouchForSpotPosition = 0.5f;

        /// <summary>Spot and path have to end up further apart than this.</summary>
        public float CrawlMinSpotToPathDistance = 1.0f;

        /// <summary>Two crawl-space jobs are never placed closer together than this.</summary>
        public float CrawlMinSeparation = 0.5f;

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
