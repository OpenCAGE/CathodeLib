#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Xml;
using CATHODE;

namespace CathodeLib.NavMesh
{
    public sealed class CoverBakeSettings
    {
        /// <summary>A designer-placed CoverExclusionArea: an oriented box that carries no cover.</summary>
        public struct ExclusionBox
        {
            public Vector3 Centre;
            /// <summary>Rotation that takes a world offset into the box's own axes.</summary>
            public Quaternion Inverse;
            public Vector3 HalfExtents;

            public bool Contains(Vector3 world)
            {
                Vector3 local = Vector3.Transform(world - Centre, Inverse);
                return Math.Abs(local.X) <= HalfExtents.X && Math.Abs(local.Y) <= HalfExtents.Y && Math.Abs(local.Z) <= HalfExtents.Z;
            }
        }

        /// <summary>
        /// Boxes the level's CoverExclusionArea entities mark out; samples inside one are dropped.
        /// Filled by <see cref="CoverBaker.BakeLevel"/> from the instancing pass. Null or empty = none.
        /// </summary>
        public List<ExclusionBox> ExclusionBoxes;

        public float DistanceFromGeometry = 0.15f;
        public float MinimumHeight = 0.75f;
        public float MaximumInclineDegrees = 65f;
        public float MinimumLength = 0.65f;
        public float LowHeight = 0.9f;
        public float StandingHeight = 1.6f;
        public float LowHighDividingLine = 1.5f;

        /// <summary>
        /// Where LOW stops and STANDING starts when writing the segment's own height. Separate from
        /// <see cref="LowHighDividingLine"/>, which gates acceptance: the obstacle field measures a
        /// top from half-metre slabs marked by triangle bounding box, so it reads high, and the two
        /// jobs want different corrections. Retail ships only 0.9 and 1.6, 56% of segments low
        /// against our 38%.
        /// </summary>
        public float HeightClassificationLine = 0f;

        /// <summary>
        /// Clear run demanded in front of the cover, measured at chest height. 0 disables the test.
        /// </summary>
        public float MinFrontClearance = 0f;

        /// <summary>
        /// Measure the obstacle height by ray instead of from the voxel occupancy field.
        /// </summary>
        public bool UseRayObstacleTop = true;

        /// <summary>Vertical increment of the ray scan. Matches the rim-feature survey.</summary>
        public float RayTopStep = 0.15f;

        /// <summary>How far in front of the rim the scan looks for the obstacle.</summary>
        public float RayTopReach = 0.8f;

        /// <summary>
        /// Bisection steps used to close the last <see cref="RayTopStep"/> of the obstacle-top
        /// scan. 0 keeps the old behaviour, which reports the last height that HIT.
        /// </summary>
        public int RayTopRefineSteps = 4;

        /// <summary>
        /// The obstacle-top scan follows ONE surface: once the lowest ray has hit at some distance, a
        /// higher ray that hits more than this much further away has found a different object (the
        /// wall behind a railing, the shelf behind a crate) and counts as a miss. 0 = old behaviour,
        /// which read the tallest thing within reach and classed a rail in front of a wall as high.
        /// </summary>
        public float RayTopSameSurface = 0f;
        public float SamplingSizeXZ = 0.05f;

        public float RequiredClearanceDistance = 0.76f;
        public float RequiredClearanceGraceDistance = 0.41f;
        public float HeightSamplingDistanceAlongNormal = 0.75f;
        public float SupportingFloorHeightTolerance = 0.1875f;

        public float OccupancyMinSlotDistanceFromEdge = 0.75f;
        public float OccupancyDistanceBetweenSlots = 1.0f;

        /// <summary>
        /// A corner link joins two segments whose normals turn by between these angles, measured
        /// UNSIGNED in the XZ plane.
        /// </summary>
        public float LinkMinCornerAngle = 5f;
        public float LinkMaxCornerAngle = 135f;

        /// <summary>
        /// Furthest a corner-linked endpoint may be moved so that it lands on the crossing of the
        /// two segments' lines. 0 disables the snap.
        /// </summary>
        public float MaxCornerEndSnapDistance = 0.75f;

        /// <summary>
        /// The caps on squaring a corner, applied alongside
        /// <see cref="MaxCornerEndSnapDistance"/>: how far past its own end a segment may be
        /// extended, as a fraction of its length, and the floor area the square may add or remove.
        /// </summary>
        public float CornerSquaringMaxT = 1.7f;
        public float CornerSquaringMaxAreaToChopOff = 0.065f;
        public float CornerSquaringMaxHeightDifference = 0.2f;
        public float LinkMaxDistanceForColinear = 4f;
        public float LinkColinearDotProductThreshold = 0.9961947f; // cos(5°)
        public float LinkDistanceForCornerOrAutoLink = 0.5f;
        public float LinkMaxDotProductForCorner = -0.7071068f; // cos(135°)

        /// <summary>
        /// Signed-angle windows that classify a corner link as EXTERNAL and as an AUTO transition.
        /// </summary>
        public float LinkMinExternalCornerAngle = 140f;
        public float LinkMaxExternalCornerAngle = 285f;
        public float LinkMaxAutoTransitionAngle = 230f;

        public float ConnectingDistanceBetweenSegmentEnds = 0.1f;
        public float ColinearMergeMaxHeightDifference = 0.2f;
        public float ColinearMergeMaxAngleDifferenceDegrees = 20f;
        /// <summary>
        /// How close two segment ends must be before a colinear merge joins them. 0 keeps the old
        /// behaviour, <c>max(ConnectingDistanceBetweenSegmentEnds, ColinearMergeMaxMovement)</c>.
        /// </summary>
        public float ColinearMergeJoinDistance = 0f;

        public float ColinearMergeMaxMovement = 0.4f;
        public float ColinearMergeMaxMovementY = 0.1f;

        /// <summary>
        /// Largest gap two segments may be merged across, as a fraction of the shorter one's
        /// length. 0 disables, leaving only the absolute caps.
        /// </summary>
        public float ColinearMergeMaxProportionalGap = 0f;

        /// <summary>
        /// How far sideways off the other segment's line a colinear partner's endpoint may sit.
        /// 0 disables the test.
        /// </summary>
        public float LinkColinearCrossLengthThreshold = 0.35f;

        public float TraversalUnitSize = 4f;

        public bool IncludeSmallPropCollision = true;

        /// <summary>
        /// Keep glass out of the cover soup. See
        /// <see cref="NavMeshBakeSettings.SkipTransparentCollision"/>.
        /// </summary>
        public bool SkipGlass = true;

        public float MaximumObstacleHeight = 2.5f;
        public float MaximumObstacleDepth = 2.0f;

        public float MinimumIslandArea = 0.15f;
        public float MaximumIslandArea = 14f;
        public float MaximumIslandExtent = 5.5f;

        /// <summary>
        /// Cull oversized solid footprints. When enabled, long thin walls/rails are still kept.
        /// </summary>
        public bool FilterSolidIslands = false;

        /// <summary>Include diagonal probe directions (8-dir). Retail cover is mostly axis-aligned.</summary>
        public bool AllowDiagonalSampling = false;

        /// <summary>Cover heightfield cell size. 0 = use SamplingSizeXZ.</summary>
        public float CoverGridCellSize = 0f;

        /// <summary>
        /// Optional final cull: drop segments farther than this from a navmesh boundary edge.
        /// 0 disables (default).
        /// </summary>
        public float MaxDistanceToNavBoundary = 0f;

        /// <summary>Drop segments far from navmesh verts. Off by default.</summary>
        public bool RequireNearNavMesh = false;
        public float NavMeshProximity = 0.75f;

        /// <summary>
        /// Sample along navmesh boundary edges when true; otherwise floor-grid sampling only.
        /// </summary>
        public bool PreferNavMeshBoundaryEdges = true;

        /// <summary>
        /// When PreferNavMeshBoundaryEdges is on, also add floor-grid segments that are not
        /// already near a nav-edge segment (gap fill).
        /// </summary>
        public bool FloorGapFillNavEdges = true;

        /// <summary>Gap-fill segments must lie this close to a raw navmesh boundary edge.</summary>
        public float GapFillMaxBoundaryDistance = 0.55f;

        /// <summary>
        /// Skip floor gap-fill if a nav-edge cover segment already lies within this distance.
        /// </summary>
        public float GapFillSkipIfNearExisting = 1.75f;

        /// <summary>
        /// Generate cover by walking the navmesh rim (<see cref="RimCoverGenerator"/>) instead of
        /// rasterising the world and merging samples. Retail's segments all lie on the rim, so this
        /// starts from the structure they are actually built on.
        /// </summary>
        public bool UseRimGenerator = true;

        /// <summary>
        /// How far outward from the navmesh rim - onto the wall side - the cover face is written.
        /// </summary>
        public float RimOffset = 0.2925f;

        /// <summary>
        /// Sit the cover face this far off the COLLISION surface instead of a fixed
        /// <see cref="RimOffset"/> from the navmesh rim. 0 = keep the fixed offset.
        /// </summary>
        public float FlushDistanceFromEdge = 0f;

        /// <summary>Most the flush may move a face. See <see cref="FlushDistanceFromEdge"/>.</summary>
        public float FlushMaxAdjustment = 0.5f;

        /// <summary>Also start the view probes from the flushed face. See <see cref="FlushDistanceFromEdge"/>.</summary>
        public bool FlushProbeOrigins = true;

        /// <summary>
        /// Path of a learned cover selector (a boosted-tree model written by <c>diag coverml
        /// trainall</c>). When set and the file exists, the per-sample gates in
        /// <see cref="RimCoverGenerator"/> (minimum height, wall end, open floor, firing arc) are
        /// replaced by the model's probability read off <see cref="LearnedCoverFeatures"/> - the raw
        /// ray description of the station - thresholded at <see cref="LearnedSelectorThreshold"/>
        /// (or the threshold stored in the model when that is 0). Null falls back to the
        /// embedded selector (<see cref="UseEmbeddedLearnedSelector"/>), then to the hand gates. Held out level by
        /// level on retail navmeshes the model reaches 60 station F1 where the gates sit near 52.
        /// </summary>
        public string LearnedSelectorPath = null;

        /// <summary>Probability at or above which a rim sample is cover under the learned selector; 0 uses the model file's own threshold. The end-to-end sweep (cover length F1 against retail, held out over the campaign) prefers 0.40 to the models' station-optimal 0.42, so it is fixed here.</summary>
        public float LearnedSelectorThreshold = 0.40f;

        /// <summary>
        /// With no <see cref="LearnedSelectorPath"/>, use the selector
        /// CathodeLib ships embedded (<see cref="LearnedCover.EmbeddedCover"/>). Set false - or set
        /// the path to "none" - for the hand-written gates. Campaign-wide (5 Sep 2026, learned1 vs
        /// campaign1): cover 59.2 -> 65.3, up on 27 of 32 levels, down on Solace / HzdLab / Torrens /
        /// CM5 / CM12. <see cref="LearnedSelectorThreshold"/> stays at 0 = the model's own 0.33: a
        /// fixed 0.25 recovers Solace (+7) and CM12 (+10) but costs the twenty levels the model never
        /// saw 0.54 of cover on average (LV426_Pt01 -4.7, Alien_Nest -7.1), and unseen levels are
        /// what users bake.
        /// </summary>
        public bool UseEmbeddedLearnedSelector = true;

        /// <summary>
        /// Only build cover where the approach side is standing-height floor. Retail places none at
        /// all against crouch or deep-crouch navmesh.
        /// </summary>
        public bool RequireStandingApproach = true;

        /// <summary>
        /// Walkable floor (m2) that must lie within <see cref="OpenAreaRadius"/> of the spot an NPC
        /// would stand. A corridor wall is not cover; the same wall facing a room is.
        /// </summary>
        public float MinOpenFloorArea = 0f;

        /// <summary>See <see cref="MinOpenFloorArea"/>.</summary>
        public float OpenAreaRadius = 2.5f;

        /// <summary>How far in from the rim the open-floor sample is taken.</summary>
        public float OpenAreaProbeInset = 0.4f;

        /// <summary>
        /// Least share of the floor within <see cref="ReachableRadius"/> that must be REACHABLE by
        /// walking from the stand point, rather than merely present. 0 = off.
        /// </summary>
        public float MinReachableShare = 0f;

        /// <summary>Radius the reachable share is measured over. See <see cref="MinReachableShare"/>.</summary>
        public float ReachableRadius = 6f;

        /// <summary>
        /// Apply <see cref="MinReachableShare"/> to whole merged SEGMENTS rather than to samples.
        /// Per sample it fragments the run, which costs more length than the cull saves.
        /// </summary>
        public bool ReachableGateOnSegments = true;

        /// <summary>How far outside the rim to look for the obstacle the cover is against.</summary>
        public float ObstacleProbeDistance = 0.45f;

        /// <summary>
        /// How thick the obstacle behind a rim point has to be when it is waist-high. Zero disables
        /// the test, which is the default: a crate or a desk is legitimately thin.
        /// </summary>
        public float MinObstacleDepth = 0.0f;

        /// <summary>
        /// Thickness demanded of a TALL obstacle - one at least <see cref="LowHighDividingLine"/>
        /// high. A waist-high crate may be thin and still be cover; a tall thin thing with open
        /// space behind it is a panel or a railing, and retail does not cover those.
        /// </summary>
        public float MinObstacleDepthHighCover = 0.0f;

        /// <summary>
        /// A tall obstacle reaching at least this high is taken to be a wall and is exempt from
        /// <see cref="MinObstacleDepthHighCover"/>. 0 disables the exemption.
        /// </summary>
        public float HighCoverWallTop = 0.0f;

        /// <summary>
        /// Reject rim further than this along a wall from where the wall stops. 0 disables it.
        /// See <see cref="RimCoverGenerator.DepthProbe.WallEndDistance"/> for the measurement.
        /// </summary>
        public float MaxWallEndDistance = 1.5f;

        /// <summary>
        /// Apply <see cref="MaxWallEndDistance"/> to low cover as well as tall. Retail's own slot
        /// layout says it should not: a high segment keeps only the slots within 1.25 m of an end,
        /// because leaning past the end is the only way to shoot, while a low segment keeps its slots
        /// the whole way along - you shoot over it.
        /// </summary>
        public bool WallEndAppliesToLowCover = false;

        /// <summary>Separate <see cref="MaxWallEndDistance"/> for tall cover. 0 uses the shared value.</summary>
        public float MaxWallEndDistanceHigh = 1.0f;

        /// <summary>
        /// Apply the tall-cover wall-end trim only on room-shell rim, leaving cover whole on anything
        /// an NPC can walk around. 0 = trim everywhere. Uses the same loop class as
        /// <see cref="ShellViewScale"/>, so <see cref="ShellLoopPerimeter"/> decides what a shell is.
        /// </summary>
        public bool WallEndShellOnly = false;

        /// <summary>
        /// Share of the RUN's length that counts as "near the end" for tall cover, on top of the flat
        /// <see cref="MaxWallEndDistanceHigh"/>. 0 = flat distance only.
        /// </summary>
        public float WallEndFraction = 0.35f;

        /// <summary>
        /// How to choose between the two arms of a right-angled corner, where retail almost always
        /// keeps one and not both. 0 = keep both (old behaviour); 1 = keep the arm that SEES further
        /// over its own top; 2 = keep the arm with LESS open floor in front of it; 3 = keep the LOWER
        /// arm; 4 = keep the LONGER arm.
        /// </summary>
        public int CornerArmRule = 0;

        /// <summary>How close two span ends must be to count as the same corner.</summary>
        public float CornerArmJoinDistance = 0.9f;

        /// <summary>An arm shorter than this is dropped at a corner without a contest.</summary>
        public float CornerArmMinLength = 0f;

        /// <summary>
        /// Line of sight OVER low cover: a low sample is kept only if a head on the cover line 1.5 m
        /// up can see at least this far in some direction within 60 degrees of outward. 0 = off.
        /// </summary>
        /// <summary>
        /// Depths along the outward normal (metres from the rim) at which the obstacle's height is
        /// also sampled from above; the candidate height is the largest of the front-face top and
        /// these. Empty = front face only.
        /// </summary>
        public float[] DepthTopDistances = new[] { 0.5f, 0.75f };


        public float MinOverTopView = 2.5f;

        /// <summary>
        /// Line of sight FROM high cover: a tall sample is kept only if its run has an end from which
        /// a leaning head (0.5 m past the end, 0.1 m into the obstacle side, 1.4 m up) sees at least
        /// this far within 60 degrees of the direction past the end. 0 = off.
        /// </summary>
        public float MinLeanEndView = 2f;

        /// <summary>
        /// Visible WALKABLE floor from the better lean head of a tall run: the count of points on a
        /// polar grid (1 to 12 m by 1 m, 60 degrees either side of the direction past the end, 15
        /// degree steps, 45 points in all) that lie on navmesh at the run's height and are seen from
        /// the head at 1.0 m above the floor. A tall sample needs its run to reach this count. 0 = off.
        /// </summary>
        public float MinLeanEndWalkable = 0f;

        /// <summary>
        /// Shortest closed navmesh loop that can carry cover, in metres round. 0 = off.
        /// </summary>
        public float MinLoopPerimeter = 0f;

        /// <summary>
        /// A loop longer than this round counts as a room shell rather than an obstacle, and its
        /// samples must clear <see cref="ShellViewScale"/> times the usual view gates.
        /// </summary>
        public float ShellViewScale = 2f;

        /// <summary>Loop perimeter at or above which rim counts as a room shell. See <see cref="ShellViewScale"/>.</summary>
        public float ShellLoopPerimeter = 40f;

        /// <summary>
        /// Front-to-back clearance between two cover segments that face the same way and overlap
        /// along the wall: closer than this and only the one nearer the room survives. 0 = off.
        /// </summary>
        public float SameFacingClearance = 0f;

        /// <summary>How far outward the depth test looks before giving up.</summary>
        public float ObstacleDepthSearchDistance = 6.0f;

        /// <summary>
        /// Restate each segment's corner and colinear links in its Flags field, as retail does.
        /// </summary>
        public bool WriteLinkFlags = true;

        /// <summary>
        /// Decide cover one whole wall at a time rather than metre by metre.
        /// </summary>
        public bool DecidePerRun = false;

        /// <summary>
        /// How the learned selector's per-station probabilities decide a whole run.
        /// 0 leaves every station to its own threshold; 1 takes the run's MEAN probability against
        /// <see cref="LearnedRunThreshold"/>; 2 takes its MAXIMUM; 3 takes the share of stations
        /// over <see cref="LearnedSelectorThreshold"/> against <see cref="LearnedRunThreshold"/>;
        /// 4 is a hybrid - a station keeps its own verdict, but a run whose mean clears the
        /// threshold also admits any station at or above <see cref="LearnedRunFloor"/>.
        /// </summary>
        /// <summary>
        /// Sample a run's stations the way the TRAINING TABLES do - round(length/step) of them, each
        /// at the middle of its slice - rather than ceil(length/step) taken at the leading edge.
        /// </summary>
        /// <summary>
        /// A SECOND-stage cover selector, read from this path. It decides a station from the shape
        /// of the whole run's first-stage probabilities - its own, its neighbours' and the run's
        /// summaries - rather than from its own probability alone. Empty or "none" leaves each
        /// station to <see cref="LearnedSelectorThreshold"/>.
        /// </summary>
        public string LearnedCoverStage2Path = null;

        /// <summary>Threshold for <see cref="LearnedCoverStage2Path"/>; 0 uses the model's own.</summary>
        public float LearnedStage2Threshold = 0f;

        public bool LearnedStationAlignment = false;

        /// <summary>
        /// Extend every accepted stretch of stations by this many metres at each end before the
        /// spans are cut. Negative pulls the ends in instead. Zero leaves them where the selector
        /// put them.
        /// </summary>
        public float SpanEndExtension = 0f;

        public int LearnedRunAggregate = 0;

        /// <summary>Threshold the run statistic named by <see cref="LearnedRunAggregate"/> is held to.</summary>
        public float LearnedRunThreshold = 0.40f;

        /// <summary>Floor a station must still reach to be admitted by mode 4's run mean.</summary>
        public float LearnedRunFloor = 0.20f;

        /// <summary>
        /// Share of a run's samples that must pass the gates for the whole run to become cover.
        /// See <see cref="DecidePerRun"/>.
        /// </summary>
        public float RunAcceptFraction = 0.5f;

        /// <summary>
        /// Reject rim from which no useful firing arc exists, in degrees. 0 disables the test.
        /// </summary>
        public float MinFiringArcDegrees = 2.5f;

        /// <summary>How far the firing-arc rays reach. See <see cref="MinFiringArcDegrees"/>.</summary>
        public float FiringArcRange = 6f;

        /// <summary>Angular step of the firing-arc sweep, in degrees.</summary>
        public float FiringArcStepDegrees = 5f;

        /// <summary>How far in front of the cover face the sweep is taken from.</summary>
        public float FiringArcStandOffset = 0.5f;

        /// <summary>
        /// How far a firing direction must be unobstructed before it counts as aimable, in metres.
        /// </summary>
        public float AimClearRange = 8.0f;

        /// <summary>
        /// Half-width of the vertical aim sweep, in degrees, and how many samples span it.
        /// </summary>
        public float AimPitchLimitDegrees = 90f;
        public int AimPitchSamples = 16;
        /// <summary>
        /// Range used for a DESCENDING aim ray. 0 uses <see cref="AimClearRange"/> for every ray.
        /// </summary>
        public float AimDownRange = 2.2f;

        /// <summary>
        /// Range used for an ASCENDING aim ray. 0 uses <see cref="AimClearRange"/> for every ray.
        /// </summary>
        public float AimUpRange = 0f;

        /// <summary>
        /// Build the clear-aim firing positions.
        /// </summary>
        public bool UseTwoPositionAim = true;

        public float AimShootHeightCrouchedSide = 0.8f;
        public float AimShootHeightStandingSide = 1.4f;
        /// <summary>
        /// How far to the side the lean shoot position sits. 
        /// </summary>
        public float AimShootLateralOffsetSide = 2.5f;

        /// <summary>
        /// Measure the side lateral offset from the SEGMENT END rather than from the slot.
        /// </summary>
        public bool AimShootLateralFromSegmentEnd = false;

        /// <summary>
        /// Decide whether a lean EXISTS with an explicit lateral clearance test at the cover end,
        /// and leave the shoot position free to compute the ARC.
        /// </summary>
        public bool UseLeanClearanceTest = false;
        public float LeanClearanceDistance = 2.0f;
        public float AimShootForwardOffsetSide = 0.4f;
        public float AimShootHeightOver = 1.5f;
        public float AimShootForwardOffsetOver = 0.0f;

        public float AimMoveFromHeightCrouchedSide = 0.8f;
        public float AimMoveFromHeightStandingSide = 1.4f;
        public float AimMoveFromLateralOffsetSide = -0.2f;
        public float AimMoveFromForwardOffsetSide = -0.5f;
        public float AimMoveFromHeightOver = 1.2f;
        public float AimMoveFromForwardOffsetOver = -0.5f;

        /// <summary>
        /// A direction only counts as aimable when it is clear from the move-from position as well
        /// as from the shoot position. See <see cref="UseTwoPositionAim"/>.
        /// </summary>
        public bool RequireClearFromMoveFrom = true;

        /// <summary>
        /// A firing position only exists if the NPC can actually GET to it from the position it
        /// moves from - a clear line between the two points.
        /// </summary>
        /// <summary>
        /// Centre the clear-aim sweeps on the OUTWARD normal - over the top of the cover and past
        /// its ends, where whatever the occupant is hiding from is - rather than on the inward
        /// normal, into the room behind them. Measurement option; see the retail census in the
        /// diag: retail ships no two same-facing overlapping segments closer than 1.4 m, which is
        /// what a 1.5 m clear distance swept OUTWARD produces and an inward sweep cannot.
        /// </summary>
        public bool AimSweepOutward = false;

        public bool RequireShootPositionReachable = true;

        /// <summary>
        /// How far a direction must be unobstructed to count as clear, in metres, and the separate
        /// distance used for the vertical cone.
        /// </summary>
        public float AimClearDistance = 1.5f;

        /// <summary>
        /// Distance a PITCHED ray must be open for.
        /// </summary>
        public float AimClearDistanceVerticalCone = 3.0f;

        /// <summary>
        /// Narrowest arc a firing position may have and still be written as live.
        /// </summary>
        public bool ApplyPeekThresholds = true;

        /// <summary>
        /// Drop a slot that has no peek bit at all - a firing position too narrow to shoot from is
        /// not a firing position, and retail ships none.
        /// </summary>
        public bool RequireUsableSlot = true;
        public float PeekInnerAngleSideDegrees = -30f;
        public float PeekArcOverDegrees = 60f;
        public float PeekVerticalArcDegrees = 11f;

        /// <summary>
        /// How far past straight ahead a lean may reach, in degrees. 0 disables the cap.
        /// </summary>
        /// <summary>
        /// A lean arc runs CONTIGUOUSLY inward from its outer edge, stopping at the first blocked
        /// direction, instead of spanning every clear direction found anywhere in the sweep.
        /// </summary>
        public bool ContiguousLeanArc = false;

        public float LeanInnerLimitDegrees = 18f;

        /// <summary>
        /// A lean arc only exists on a side where the cover actually ENDS within reach.
        /// </summary>
        public bool LeanNeedsAnEnd = false;

        /// <summary>
        /// How near an end a slot must be for a lean past it to exist. 0 uses
        /// <see cref="HeightSamplingDistanceAlongNormal"/>.
        /// </summary>
        public float LeanMaxDistanceToEnd = 0f;

        /// <summary>
        /// A slot may only lean past the end of the segment it is NEARER to.
        /// </summary>
        public bool LeanOnlyTowardNearerEnd = false;

        /// <summary>Step along the rim at which cover is tested.</summary>
        public float RimSampleStep = 0.25f;

        /// <summary>
        /// Where in each rim sample interval the cover pass samples, as a fraction. 0.5 is the
        /// midpoint. The sample COUNT and step do not change; only the phase does.
        /// </summary>
        public float RimSamplePhase = 0.5f;

        /// <summary>How far consecutive rim edges may turn and still be one run.</summary>
        public float RimRunMaxTurnDegrees = 20.0f;

        /// <summary>
        /// Rejected rim shorter than this inside an otherwise continuous span is bridged rather than
        /// splitting the segment - a doorframe, a pipe or a seam in the rasterised solid interrupts
        /// the probes without interrupting the cover.
        /// </summary>
        public float SpanGapTolerance = 5.0f;

        /// <summary>Radius of the majority filter applied to the low/high classification.</summary>
        /// <summary>
        /// Half-width, in metres along the rim, of the majority filter that decides a sample's
        /// low/high class from its neighbours.
        /// </summary>
        public float HeightSmoothingDistance = 1.5f;

        /// <summary>
        /// Ground deviation, in metres, that cuts a cover run in two. 0 = never cut on the ground.
        /// </summary>
        public float GroundHeightSplitDeviation = 0f;

        /// <summary>How far either side of a sample the ground is compared. See <see cref="GroundHeightSplitDeviation"/>.</summary>
        public float GroundHeightSplitCheckDistance = 0.5f;

        /// <summary>A run whose ends differ by less than this is never split. See <see cref="GroundHeightSplitDeviation"/>.</summary>
        public float GroundHeightSplitEarlyOut = 0.05f;

        /// <summary>Most ground samples taken per run. See <see cref="GroundHeightSplitDeviation"/>.</summary>
        public int GroundHeightSplitMaxSamples = 20;

        /// <summary>
        /// How far onto the walkable side of the cover face the occupant stands. Sight lines are
        /// traced from here, not from the face, which sits against the wall.
        /// </summary>
        public float SlotStandOffset = 0.5f;

        /// <summary>
        /// Keep a segment even when no firing position found a clear line, giving it one slot with
        /// the default cones. Retail ships at least one slot on every segment.
        /// </summary>
        public bool KeepSegmentsWithoutClearAim = false;

        /// <summary>
        /// Accept only obstacles you can shoot over or lean past, rejecting the band in between.
        /// </summary>
        public bool UseObstacleHeightBands = false;

        /// <summary>Tallest obstacle still counted as something to shoot over.</summary>
        public float LowCoverMaxTop = 1.3f;

        /// <summary>Shortest obstacle counted as a wall to lean around.</summary>
        public float HighCoverMinTop = 3.0f;

        /// <summary>
        /// Longest segment emitted; longer spans are cut into equal pieces. Retail's segments have a
        /// median length of 1.2-1.8 m and a 90th percentile of 2.2-6.5 m across the campaign.
        /// </summary>
        /// <summary>
        /// Spans are cut into pieces no longer than this before merging. 0 or a large value leaves
        /// them whole; retail ships segments past 8 m, so the 4 m cut costs a little on the levels
        /// with long walls (Tech_Hub +0.7, CM9 +0.3 with it removed) and nothing anywhere else.
        /// </summary>
        public float MaximumSegmentLength = 0f;

        public float ClassifyCoverHeight(float obstacleHeightAboveFloor)
        {
            float line = HeightClassificationLine > 0f ? HeightClassificationLine : LowHighDividingLine;
            return obstacleHeightAboveFloor < line ? LowHeight : StandingHeight;
        }
    }
}
#endif
