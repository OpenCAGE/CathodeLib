#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.IO;
using System.Xml;
using CATHODE;

namespace CathodeLib.NavMesh
{
    public sealed class CoverBakeSettings
    {
        public float DistanceFromGeometry = 0.15f;
        public float MinimumHeight = 0.65f;
        public float MaximumInclineDegrees = 65f;
        public float MinimumLength = 0.9f;
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
        /// <remarks>
        /// Pooled over 22,286 rim samples on five levels, retail covers rim with under a metre of
        /// clear run in front of it 3.4-7.1% of the time against 24.0% overall.
        /// </remarks>
        public float MinFrontClearance = 0f;

        /// <summary>
        /// Measure the obstacle height by ray instead of from the voxel occupancy field.
        /// </summary>
        /// <remarks>
        /// The voxel field marks a triangle into every half-metre slab its BOUNDING BOX touches, so a
        /// waist-high desk reads as something much taller, and the whole generator was tuned against
        /// a blurred copy of the one feature retail keys on hardest - rim behind a 0.5-1.0 m obstacle
        /// carries retail cover 36.9% of the time against 15.6% behind a 3.5 m one. Measuring by ray
        /// and retuning <see cref="MinimumHeight"/> to 0.65 moves cover +5.4 on SCI_Hub, +1.0 on
        /// ChallengeMap5, +19.5 on ENG_ReactorCore and -0.3 on Tech_Hub, and independently pulls the
        /// low/high height mix onto retail's: ChallengeMap5 23% low -> 48% against retail's 50%,
        /// ENG_ReactorCore 54% -> 76% against 72%. Same failure, same file, as the tall-thin wall
        /// rule measuring flat until its thickness test was ray cast.
        /// </remarks>
        public bool UseRayObstacleTop = true;

        /// <summary>Vertical increment of the ray scan. Matches the rim-feature survey.</summary>
        public float RayTopStep = 0.15f;

        /// <summary>How far in front of the rim the scan looks for the obstacle.</summary>
        public float RayTopReach = 0.8f;
        public float SamplingSizeXZ = 0.05f;

        public float RequiredClearanceDistance = 0.76f;
        public float RequiredClearanceGraceDistance = 0.41f;
        public float HeightSamplingDistanceAlongNormal = 0.75f;
        public float SupportingFloorHeightTolerance = 0.1875f;

        public float OccupancyMinSlotDistanceFromEdge = 0.75f;
        public float OccupancyDistanceBetweenSlots = 1.0f;

        public float LinkMinExternalCornerAngle = 140f;
        public float LinkMaxExternalCornerAngle = 285f;
        public float LinkMaxDistanceForColinear = 4f;
        public float LinkColinearDotProductThreshold = 0.9961947f; // cos(5°)
        public float LinkDistanceForCornerOrAutoLink = 0.5f;
        public float LinkMaxDotProductForCorner = -0.7071068f; // cos(135°)

        public float ConnectingDistanceBetweenSegmentEnds = 0.1f;
        public float ColinearMergeMaxHeightDifference = 0.2f;
        public float ColinearMergeMaxAngleDifferenceDegrees = 12f;
        public float ColinearMergeMaxMovement = 0.4f;
        public float ColinearMergeMaxMovementY = 0.1f;

        public float TraversalUnitSize = 4f;

        public bool IncludeSmallPropCollision = true;

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
        /// <remarks>
        /// Not a guess: retail's segments sit 0.292 m outside the rim on every level measured, with
        /// the 10th and 90th percentiles at 0.29 and 0.31. The mesh is eroded by the walkable radius
        /// (0.3125), which puts the face 0.02 m off the collision surface.
        /// </remarks>
        public float RimOffset = 0.2925f;

        /// <summary>
        /// Only build cover where the approach side is standing-height floor. Retail places none at
        /// all against crouch or deep-crouch navmesh.
        /// </summary>
        public bool RequireStandingApproach = true;

        /// <summary>
        /// Walkable floor (m2) that must lie within <see cref="OpenAreaRadius"/> of the spot an NPC
        /// would stand. A corridor wall is not cover; the same wall facing a room is.
        /// </summary>
        /// <remarks>
        /// The relationship is graded rather than a cliff - on SCI_Hub 5% of rim with under 3 m2 is
        /// covered against 58% of rim with over 15 m2, and Tech_Hub runs 13% to 41% - so this is set
        /// where it buys precision without throwing away the tail.
        /// </remarks>
        public float MinOpenFloorArea = 2.0f;

        /// <summary>See <see cref="MinOpenFloorArea"/>.</summary>
        public float OpenAreaRadius = 2.5f;

        /// <summary>How far in from the rim the open-floor sample is taken.</summary>
        public float OpenAreaProbeInset = 0.4f;

        /// <summary>How far outside the rim to look for the obstacle the cover is against.</summary>
        public float ObstacleProbeDistance = 0.45f;

        /// <summary>
        /// How thick the obstacle behind a rim point has to be when it is waist-high. Zero disables
        /// the test, which is the default: a crate or a desk is legitimately thin.
        /// </summary>
        /// <remarks>
        /// Demanding any thickness of LOW cover measures worse on every level tried - Tech_Hub falls
        /// from 53.3 to 48.3 at half a metre, SCI_Hub from 48.8 to 48.4 - so the test applies only to
        /// tall obstacles, through <see cref="MinObstacleDepthHighCover"/>.
        /// </remarks>
        public float MinObstacleDepth = 0.0f;

        /// <summary>
        /// Thickness demanded of a TALL obstacle - one at least <see cref="LowHighDividingLine"/>
        /// high. A waist-high crate may be thin and still be cover; a tall thin thing with open
        /// space behind it is a panel or a railing, and retail does not cover those.
        /// </summary>
        /// <remarks>
        /// <para>This is where the over-production lives. SCI_Hub comes out 102 low / 314 high
        /// against retail's 97 / 63 - the low-cover count is already right and it is walls we
        /// invent.</para>
        /// <para>Three metres measures best across the campaign, and it helps most where we were
        /// worst: cover F1 goes 7.8 -> 26.5 on BSP_LV426_Pt01, 22.1 -> 32.8 on BSP_Torrens,
        /// 41.2 -> 48.8 on SCI_Hub and 51.1 -> 53.3 on Tech_Hub. Four metres is better still on the
        /// two big levels but collapses LV426_Pt01 back to 16.6, whose own covered rim has a median
        /// thickness of 3.2 m.</para>
        /// </remarks>
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
        /// <remarks>
        /// This REPLACES the tall-obstacle thickness gate, which was a proxy for it:
        /// <see cref="MinObstacleDepthHighCover"/> is now 0. Thickness rejected tall thin things
        /// wholesale, which also threw away retail's own thin walls; what retail actually avoids is
        /// the MIDDLE of a long run, whatever it is made of. Cover F1 with both changes, against the
        /// thickness gate alone: Testbed_CoverMovement 45.9 -> 72.3 at 100% precision, Tech_Hub
        /// 52.7 -> 58.9, DLC/SalvageMode2 39.1 -> 47.0, Tech_MuthrCore 33.4 -> 39.9, SCI_Hub
        /// 54.2 -> 58.1; DLC/ChallengeMap12 40.1 -> 38.8, BSP_TORRENS 31.8 -> 26.0 and
        /// BSP_LV426_Pt01 23.1 -> 16.7 go the other way. Five of eight up, mean +4.7.
        /// The two corridor levels prefer 0.75 (LV426_Pt01 28.1, and six of eight up) - long
        /// continuous walls make every point far from an end - so this constant is the one to
        /// revisit if the corridor levels become the priority.
        /// </remarks>
        public float MaxWallEndDistance = 1.0f;

        /// <summary>How far outward the depth test looks before giving up.</summary>
        public float ObstacleDepthSearchDistance = 6.0f;

        /// <summary>
        /// Restate each segment's corner and colinear links in its Flags field, as retail does.
        /// </summary>
        public bool WriteLinkFlags = true;

        /// <summary>
        /// Decide cover one whole wall at a time rather than metre by metre.
        /// </summary>
        /// <remarks>
        /// Retail's own data is all-or-nothing. Chaining the navmesh rim into runs - edges that
        /// continue nearly straight with the same inward normal, one wall each - and asking how much
        /// of a run retail covers gives a median of 100% at EVERY run length up to 14 m, in ONE
        /// contiguous stretch (mean 1.00 stretches per covered run), over 26,397 runs on twelve
        /// levels. A wall is either cover or it is not; there is no such thing as covering half of
        /// one. Testing sample by sample leaves dashes down a wall instead, which costs precision
        /// where retail left the rim bare and recall over the rest of a wall retail took whole.
        /// Only runs shorter than a metre behave differently, and those are rim slivers: retail
        /// covers 5.8% of them against 27% of everything longer.
        /// <para>OFF by default because it does not pay on the score. Aggregating the gates over a
        /// run only removes noise if the gates are right, and they are not: an exhaustive threshold
        /// search over sixteen geometric features reaches F1 52.4% per run against the current
        /// generator's own 52.4%, so there is no information left to sharpen. Measured against
        /// retail: SCI_Hub 58.1 -> 57.7, Tech_MuthrCore 39.9 -> 38.4, Tech_Hub 58.9 -> 57.8 at
        /// <see cref="RunAcceptFraction"/> 0.5. The mechanism is kept because the FINDING is solid
        /// and it is the right shape to build on the day the gates improve.</para>
        /// </remarks>
        public bool DecidePerRun = false;

        /// <summary>
        /// Share of a run's samples that must pass the gates for the whole run to become cover.
        /// See <see cref="DecidePerRun"/>.
        /// </summary>
        public float RunAcceptFraction = 0.5f;

        /// <summary>
        /// Reject rim from which no useful firing arc exists, in degrees. 0 disables the test.
        /// </summary>
        /// <remarks>
        /// Retail's own COVER files carry this measurement per slot and we never read it. Over the
        /// 9,085 shipped slots the widest clear arc is **never below 60 degrees** (p10 84), and the
        /// two height classes are defined by WHICH arc exists: low cover's shoot-over-the-top arc has
        /// a median of 180 degrees and is zero on 0.2% of slots, high cover's is zero on 100% of them
        /// and it leans past an edge instead. See <c>diag coverslots</c>.
        /// <para>This is a visibility test, which is the one class of feature an exhaustive search
        /// over rim geometry never had - and the reason <see cref="MaxWallEndDistance"/> works as far
        /// as it does, since a wall you can lean past is one that ENDS near you.</para>
        /// </remarks>
        public float MinFiringArcDegrees = 0f;

        /// <summary>How far the firing-arc rays reach. See <see cref="MinFiringArcDegrees"/>.</summary>
        public float FiringArcRange = 12f;

        /// <summary>Angular step of the firing-arc sweep, in degrees.</summary>
        public float FiringArcStepDegrees = 5f;

        /// <summary>How far in front of the cover face the sweep is taken from.</summary>
        public float FiringArcStandOffset = 0.5f;

        /// <summary>
        /// How far a firing direction must be unobstructed before it counts as aimable, in metres.
        /// </summary>
        /// <remarks>
        /// This was a hard-coded 8 m, which is a line-of-FIRE test - "is there eight metres of open
        /// space that way" - when retail's arcs are a line-of-SIGHT one. Its low cover has a
        /// shoot-over-the-top arc of 180 degrees at the median and a dead arc on only 0.2% of 5,815
        /// slots, i.e. essentially every direction counts; ours was dead on 21.6% and medianed 96,
        /// because in an interior most directions meet a wall inside 8 m. Measured with
        /// `diag coverslots`.
        /// </remarks>
        public float AimClearRange = 2.0f;

        /// <summary>
        /// A lean arc only exists on a side where the cover actually ENDS within reach.
        /// </summary>
        /// <remarks>
        /// Retail's lean-left and lean-right arcs are dead on 38.0% and 37.3% of its 9,085 slots -
        /// a slot in the middle of a long segment cannot lean either way - against 4.2% and 10.1%
        /// of ours. Clamping the lean eye to the segment end stops it walking through the wall but
        /// still leaves it flat against a wall that carries on past it, and the sweep from there
        /// reports an arc that is not really available.
        /// </remarks>
        public bool LeanNeedsAnEnd = true;

        /// <summary>Step along the rim at which cover is tested.</summary>
        public float RimSampleStep = 0.25f;

        /// <summary>How far consecutive rim edges may turn and still be one run.</summary>
        public float RimRunMaxTurnDegrees = 20.0f;

        /// <summary>
        /// Rejected rim shorter than this inside an otherwise continuous span is bridged rather than
        /// splitting the segment - a doorframe, a pipe or a seam in the rasterised solid interrupts
        /// the probes without interrupting the cover.
        /// </summary>
        public float SpanGapTolerance = 0.75f;

        /// <summary>Radius of the majority filter applied to the low/high classification.</summary>
        public float HeightSmoothingDistance = 0.5f;

        /// <summary>
        /// How far onto the walkable side of the cover face the occupant stands. Sight lines are
        /// traced from here, not from the face, which sits against the wall.
        /// </summary>
        public float SlotStandOffset = 0.5f;

        /// <summary>
        /// Keep a segment even when no firing position found a clear line, giving it one slot with
        /// the default cones. Retail ships at least one slot on every segment.
        /// </summary>
        public bool KeepSegmentsWithoutClearAim = true;

        /// <summary>
        /// Accept only obstacles you can shoot over or lean past, rejecting the band in between.
        /// </summary>
        /// <remarks>
        /// SCI_Hub wants this badly - rim with a ~1 m obstacle is covered 34% of the time and rim
        /// with a 3.5 m one 58%, against 9-17% for everything between - but Tech_Hub is far flatter
        /// and the same gate costs it more recall than it buys precision, so it is off by default.
        /// </remarks>
        public bool UseObstacleHeightBands = false;

        /// <summary>Tallest obstacle still counted as something to shoot over.</summary>
        public float LowCoverMaxTop = 1.3f;

        /// <summary>Shortest obstacle counted as a wall to lean around.</summary>
        public float HighCoverMinTop = 3.0f;

        /// <summary>
        /// Longest segment emitted; longer spans are cut into equal pieces. Retail's segments have a
        /// median length of 1.2-1.8 m and a 90th percentile of 2.2-6.5 m across the campaign.
        /// </summary>
        public float MaximumSegmentLength = 4.0f;

        public float ClassifyCoverHeight(float obstacleHeightAboveFloor)
        {
            float line = HeightClassificationLine > 0f ? HeightClassificationLine : LowHighDividingLine;
            return obstacleHeightAboveFloor < line ? LowHeight : StandingHeight;
        }
    }
}
#endif
