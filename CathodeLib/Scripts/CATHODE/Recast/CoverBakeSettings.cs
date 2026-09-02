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
        public float MinimumHeight = 0.8f;
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

        /// <summary>
        /// Bisection steps used to close the last <see cref="RayTopStep"/> of the obstacle-top
        /// scan. 0 keeps the old behaviour, which reports the last height that HIT.
        /// </summary>
        /// <remarks>
        /// The unrefined scan under-reports the top by up to a whole step, and that bias is the
        /// reason our fitted <see cref="MinimumHeight"/> of 0.65 beat the original value of
        /// 0.8 by 22 points of cover F1 on Tech_MuthrCore: 0.65 was compensating for a broken
        /// measurement. Four steps close 0.15 m to under 0.01.
        /// </remarks>
        public int RayTopRefineSteps = 4;
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
        /// <remarks>
        /// This replaced a signed-turn window of 140..285 degrees, which was wrong in a way that
        /// cost half our corner links. Retail names every corner on BOTH segments - 100.0% of its
        /// 4,088 corner links across the campaign have the reverse present - but a signed turn is
        /// antisymmetric, turn(a,b) = 360 - turn(b,a), so a single signed window can only ever
        /// admit one direction of a pair. Ours came out 13.6% reciprocal, and only 22.3% of our
        /// segments carried a corner link against retail's 46.2%.
        ///
        /// The bounds are retail's own, measured over those 4,088 links with `diag coverlinks all`:
        /// the unsigned angle runs p10 45.0, p50 89.1, p90 93.1, with 99.9% at or under 105 degrees
        /// and a hard maximum of 105.8. Below, 0.8% sit under 15 degrees. So [15, 110] keeps about
        /// 99% of what retail links and excludes the 135-180 degree pairs the old window admitted -
        /// two walls facing opposite ways, which were 23.9% of the corner links we wrote.
        /// </remarks>
        public float LinkMinCornerAngle = 5f;
        public float LinkMaxCornerAngle = 135f;

        /// <summary>
        /// Furthest a corner-linked endpoint may be moved so that it lands on the crossing of the
        /// two segments' lines. 0 disables the snap.
        /// </summary>
        /// <remarks>
        /// Retail's corner-linked segments genuinely MEET: measured over all 4,088 corner links in
        /// the campaign, the joining endpoint sits 0.00 m from the crossing of the two lines at both
        /// p10 and p50, and 0.07 at p90, with the two endpoints 0.02 m apart at p50. Ours stopped
        /// 0.12 m short of the crossing at p50 and 0.20 m apart from each other, because a rim run
        /// ends where its own run ends rather than where the next wall begins.
        ///
        /// The cap keeps the snap honest. Two segments meeting at a shallow angle cross a long way
        /// off, and dragging an end out to a crossing metres away would invent cover that is not
        /// there; 0.75 m is comfortably past retail's p90 and well short of that.
        /// </remarks>
        public float MaxCornerEndSnapDistance = 0.75f;

        /// <summary>
        /// The caps on squaring a corner, applied alongside
        /// <see cref="MaxCornerEndSnapDistance"/>: how far past its own end a segment may be
        /// extended, as a fraction of its length, and the floor area the square may add or remove.
        /// </summary>
        /// <remarks>
        /// <c>The area cap is the tighter of the two in the common case - a right
        /// angle squared off with equal legs hits 0.065 m2 at 0.36 m per leg, well inside the
        /// 0.75 m distance cap - so it stops a shallow crossing from inventing cover the way a
        /// pure distance test cannot.
        /// </remarks>
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
        /// <remarks>
        /// The handedness was measured, not guessed:
        /// bucketing retail's 2,044 left corner links by signed turn (`diag coversigned all`) puts
        /// every EXTERNAL one in [150, 300] and every non-EXTERNAL one in [74, 140], with the
        /// highest clear case at 139.6 - landing on the 140 boundary from below. AUTO splits the
        /// same population at 230 just as exactly: 100% set below it, 0.0% set in the 240-270 band.
        /// </remarks>
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
        /// <remarks>
        /// That max() conflates two different settings:
        /// <c>ConnectingDistanceBetweenSegmentEnds</c> = 0.1 is the join distance,
        /// while <c>ColinearMergeMaxMovement</c> = 0.4 caps how far the merge may MOVE a vertex.
        /// Using 0.4 to decide whether to join at all swallows every contiguous colinear pair, and
        /// retail keeps those as two segments joined by an AUTO colinear link: on SCI_Hub it has 6
        /// touching pairs and 11 distant ones, where we produce 27 links and not one touching.
        /// </remarks>
        public float ColinearMergeJoinDistance = 0f;

        public float ColinearMergeMaxMovement = 0.4f;
        public float ColinearMergeMaxMovementY = 0.1f;

        /// <summary>
        /// Largest gap two segments may be merged across, as a fraction of the shorter one's
        /// length. 0 disables, leaving only the absolute caps.
        /// </summary>
        /// <remarks>
        /// <c>ColinearMergeMaxProportionalGap</c> = 0.2, with the SHORTER of the two segments as
        /// the denominator. It bites well inside the absolute limit: the join distance is
        /// max(0.1, 0.4) = 0.4 m, but a minimum-length 0.9 m segment may only reach 0.18 m.
        /// <para>OFF, because the denominator is a guess and the guess measures worse. Against the
        /// caps alone it trades recall for precision and comes out net negative: Tech_MuthrCore
        /// 45.8 -> 45.2 (recall 46.8 -> 45.5, precision 44.7 -> 44.9) and SCI_Hub 60.5 -> 60.6.
        /// The constant is real; which length it is proportional TO is not settled, and the
        /// looser readings (combined or longer length) trend back towards having no cap at all.
        /// Resolve the denominator before turning this on.</para>
        /// </remarks>
        public float ColinearMergeMaxProportionalGap = 0f;

        /// <summary>
        /// How far sideways off the other segment's line a colinear partner's endpoint may sit.
        /// 0 disables the test.
        /// </summary>
        /// <remarks>
        /// <c>LinkColinearCrossLengthThreshold</c> = 0.35, read as a LENGTH - the lateral
        /// offset between the two lines - which is what the name says and what the existing
        /// <see cref="LinkColinearDotProductThreshold"/> does not already cover. Two walls either
        /// side of a doorway are parallel and facing the same way, so nothing else rejects them;
        /// they are not one run of cover an NPC can slide along.
        /// <para>The other reading, 0.35 as the sine of an angle (20.5 degrees), would duplicate
        /// the 5-degree dot-product test at a looser bound, so it is the less likely one.</para>
        /// <para>Measured a no-op on SCI_Hub and Tech_MuthrCore - identical segments, length
        /// and F1 with it on or off - so no colinear link we currently make is offset that far.
        /// </para>
        /// </remarks>
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
        /// <para>2.0 -&gt; 3.0 (29 Aug 2026), jointly with <see cref="SpanGapTolerance"/>. The
        /// feature search wanted 5.2, and 5.2 is indeed best on SCI_Hub and Tech_MuthrCore, but it
        /// costs BSP_Torrens 6.8 points - an open-floor gate is hostile to a corridor level - and
        /// comes out net negative across five levels. 3.0 is the value that gains everywhere it
        /// gains and loses nothing; the top six combinations span only 0.16 F1, so this is a broad
        /// optimum rather than a fitted point.</para>
        public float MinOpenFloorArea = 3.0f;

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
        /// <para>**0.3 m (29 Aug 2026), was 0.0 = off.** "Width of cover" is one of Matt's criteria
        /// and it is the one that both separates cross-level in the rim table (covered obstacles are
        /// deeper than uncovered rim on 12 of 13 levels, ratio 1.09-4.60) and survives in the real
        /// generator. `diag coveriter &lt;level&gt; depth2` sweeps the low and tall axes together;
        /// 0.3/0.3 is best or near-best on all three levels tested and negative on none:</para>
        /// <para>SCI_Hub 58.2 -> 58.8, Tech_MuthrCore 41.0 -> 44.7, BSP_TORRENS 25.6 -> 29.7. It is
        /// the right SHAPE of gain too - recall barely moves while precision rises (MuthrCore 50.3
        /// -> 48.4 recall against 34.6 -> 41.6 precision), so it cuts the surplus rather than the
        /// cover. 0.5 is better on Torrens alone and worse on SCI_Hub; 0.8 is worse everywhere.</para>
        /// <para><b>0.3 -&gt; 0.0 (29 Aug 2026).</b> The thickness gate existed to cut F1
        /// over-production. Scored on USABLE cover instead it is purely harmful, and removing it
        /// wins on all six levels measured: ENG_TowPlatform 96.3 -&gt; 97.5, SCI_Hub 98.5 -&gt;
        /// 99.1, HAB_Airport 98.3 -&gt; 98.8, Tech_MuthrCore 96.0 -&gt; 96.5, BSP_Torrens 88.8 -&gt;
        /// 90.1, Tech_Hub 98.9 -&gt; 99.0.</para>
        /// <para>Matt spotted the symptom by eye first - the main platform on ENG_TowPlatform is
        /// thick with retail cover where we placed fragments. `diag covermiss` attributed it: we
        /// produced 31.6% of retail's cover length there and **41.4% of the shortfall died on this
        /// one gate**, against 3.5% that had no navmesh rim at all. A railing or a thin panel on an
        /// exterior platform is real cover; demanding 0.3 m of obstacle behind it threw the level
        /// away. Segments 142 -&gt; 192 and slots 201 -&gt; 348 against retail's 227 / 382.</para>
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
        /// <para>Also 0.3 -&gt; 0.0 - see <see cref="MinObstacleDepth"/>. The tall-thin case is a
        /// railing or a wall, and retail covers both.</para>
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
        /// <para><b>1.0 -&gt; 1.5 (29 Aug 2026).</b> Scored on usable cover this is the single
        /// biggest remaining gain, and it is what Matt saw by eye: the gate says cover belongs near
        /// where a wall ENDS, so on a long run it keeps the ends and rejects the middle, leaving
        /// the fragments he spotted along the catwalk structures on ENG_TowPlatform. `diag
        /// covermiss` attributed 16.9% of the remaining shortfall there to this gate.</para>
        /// <para>Quality score, ours against retail: ENG_TowPlatform 97.5 -&gt; 97.8 (94.8),
        /// Tech_Hub 99.2 -&gt; 99.4 (98.4), SCI_Hub 99.2 -&gt; 99.4 (98.2), HAB_Airport 98.7 -&gt;
        /// 99.1 (90.0), Tech_MuthrCore 96.7 -&gt; 99.0 (94.9), BSP_Torrens 89.4 -&gt; 94.2 (92.7).
        /// Mean 96.8 -&gt; 98.2 against retail's 94.8 - we now beat retail on ALL SIX. Torrens is
        /// the one that mattered most: its severe coverage gap goes 3.4% -&gt; 0.0%, so the
        /// stranded-NPC case is gone. 2.0 and 3.0 were also tested and 1.5 is best on the mean.</para>
        /// <para>Watch the `room` column if pushing this further - at 2.0 Torrens drops to 75%
        /// room, meaning cover in cramped places, which is the other failure mode.</para>
        public float MaxWallEndDistance = 1.5f;

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
        /// <remarks>
        /// 8.0 m, and it must be re-fitted whenever <see cref="AimDownRange"/> changes - the two
        /// interact hard, and the history here is a warning about fitting one constant while another
        /// is broken.
        ///
        /// This was a hard-coded 8 m. It was cut to 2.0 and later 4.0 because a longer range looked
        /// like it destroyed low cover: at 8 m the shoot-over-the-top arc went 13.2% dead against
        /// retail's 0.2% and its median fell from 180 degrees to 120. That reasoning was wrong. The
        /// rays doing the damage were the DOWNWARD ones, hitting the FLOOR a few metres out and
        /// marking the whole yaw blocked - a line-of-fire test failing on the ground the NPC is
        /// standing over. With descending rays capped by AimDownRange, every horizontal statistic is
        /// flat across a pitch x range grid: low over-top 0.0% dead with a median of 180, lean dead
        /// 21.0/31.0%, high widest 108, identical at every range from 0.5 m to 8 m.
        ///
        /// So the range is now a purely VERTICAL lever, and longer is better on both levels
        /// measured, because real obstacles rather than the floor do the narrowing. Scoring the six
        /// vertical width statistics against retail: SCI_Hub 84 at 4 m against 60 at 8 m,
        /// Tech_MuthrCore 108 against 72, where its low over-top lands on retail's 24/48 exactly.
        /// `diag coveriter &lt;level&gt; pitch` runs the grid.
        /// </remarks>
        public float AimClearRange = 8.0f;

        /// <summary>
        /// Half-width of the vertical aim sweep, in degrees, and how many samples span it.
        /// </summary>
        /// <remarks>
        /// This was hard-coded at +-0.4 radians - **+-23 degrees** - over 5 samples, which put a
        /// hard ceiling of about 46 degrees on any vertical arc we could express. Retail's vertical
        /// arcs reach 96 to 108 degrees at p90 (`diag coverslots`), so more than half its range was
        /// unreachable, and that is why we wrote 12 distinct packed vertical words on SCI_Hub
        /// against retail's 114.
        ///
        /// 16 samples over +-90 degrees puts every sample on the 12 degree nibble grid the format
        /// stores, so a sampled elevation lands exactly on a representable value instead of being
        /// rounded onto one.
        /// </remarks>
        public float AimPitchLimitDegrees = 90f;
        public int AimPitchSamples = 16;
        /// <summary>
        /// Range used for a DESCENDING aim ray. 0 uses <see cref="AimClearRange"/> for every ray.
        /// </summary>
        /// <remarks>
        /// The bottom of a vertical arc is set by where the ray meets the floor, and the floor is
        /// much nearer than the aim range: from a chest eye 0.675 m up, a ray reaches the floor at
        /// -9.7 degrees at 4 m, which is exactly the -6 we wrote once it lands on the 12-degree
        /// nibble grid. Retail's bottoms are -18 to -30. Sweeping this parameter walks the bottom
        /// straight down the grid - 4 m gives -6, 3.0 and 2.2 give -18, 1.6 gives -30, 1.2 gives
        /// -54 - so the mechanism is confirmed, not guessed.
        ///
        /// 2.2 m is the best value on BOTH levels tested, scoring the vertical widths and bottoms
        /// against retail: SCI_Hub total absolute error 96 against 132 at 4 m, Tech_MuthrCore 108
        /// against 120. It also fixes a defect it was not aimed at - Tech_MuthrCore's low over-top
        /// arcs were 6.8% dead against retail's 0.2%, and the deep downward rays were the cause;
        /// at 2.2 m it is 0.0%. `diag coveriter &lt;level&gt; down` runs the sweep.
        /// </remarks>
        public float AimDownRange = 2.2f;

        /// <summary>
        /// Range used for an ASCENDING aim ray. 0 uses <see cref="AimClearRange"/> for every ray.
        /// </summary>
        /// <remarks>
        /// The mirror of <see cref="AimDownRange"/>. The top of a vertical arc is set by where the
        /// ray meets the CEILING, and at 8 m a ray climbing 30 degrees from a 1.05 m over-the-top
        /// eye would be 5 m up - through the roof of any normal room - so the direction is marked
        /// blocked and the arc stops short. Retail's low over-top tops sit at +30 where ours reach
        /// +18, which is the whole of that 36-against-48 width gap.
        /// </remarks>
        public float AimUpRange = 0f;

        /// <summary>
        /// Build the clear-aim firing positions.
        /// </summary>
        /// <remarks>
        /// <para> Ours put the lean eye on the cover line at <c>min(coverHeight, 1.2) * 0.75</c> 
        /// and the over-the-top eye at <c>coverHeight + 0.15</c>, which is why our high-cover eye 
        /// ended up BELOW our low-cover one. Retail's arcs are consistent with fixed heights and fixed offsets,
        /// and evaluates the arc from TWO positions - where the NPC shoots from, and the position it
        /// moves from.</para>
        /// <para>The sign convention is the one that makes the numbers coherent: forward is measured
        /// TOWARDS the cover, against our segment normal. The shoot position then sits 0.1 m on the
        /// far side of the cover plane and 0.5 m past the edge - a head leaning around a corner -
        /// and the move-from position sits 0.5 m out on the walkable side, tucked 0.2 m back from
        /// the edge, which is exactly our own fitted <see cref="SlotStandOffset"/> of 0.5.</para>
        /// <para>Two of our fitted hacks fall out of this geometry for free, which is the evidence
        /// the reading is right. <see cref="LeanNeedsAnEnd"/> becomes unnecessary because a lean eye
        /// 0.1 m behind a wall that CARRIES ON is inside solid geometry and every ray from it is
        /// blocked; and the over-the-top eye at a fixed 1.5 m is below the 1.6 m standing cover it
        /// would have to see over, which is retail's 100% dead high over-top arc.</para>
        /// </remarks>
        public bool UseTwoPositionAim = true;

        public float AimShootHeightCrouchedSide = 0.8f;
        public float AimShootHeightStandingSide = 1.4f;
        /// <summary>
        /// How far to the side the lean shoot position sits. 
        /// </summary>
        /// <remarks>
        /// Fitted with `diag coveriter <level> gridext`, scored by PEEK-BIT AGREEMENT ON CO-LOCATED
        /// SLOTS - aggregate rates cannot tell a placement difference from a calculation error.
        /// Validate on the big levels: Tech_Hub gives ~400 matched slots and HAB_Airport ~300, where
        /// SCI_Hub gives 103 and Tech_MuthrCore 76. That mattered - the small levels ranked
        /// "fwd 0.30 lat 1.00, no reachability" top, and it collapses to 64.7% on HAB_Airport.
        /// <para>The ridge is real and peaks at 2.5: on Tech_Hub, 79.1 / 81.3 / 81.8 / 81.6 / 84.5 /
        /// 80.6 / 70.3 at lateral 1.25 / 1.5 / 1.75 / 2.0 / 2.5 / 3.0 / 4.0, and HAB_Airport agrees
        /// (83.5 at 2.5). Live lean counts land on retail there too - 198 against 179 and 146
        /// against 136. Deeper forward offsets need more lateral, which is the shape you would
        /// expect if what matters is clearing the corner.</para>
        /// </remarks>
        public float AimShootLateralOffsetSide = 2.5f;

        /// <summary>
        /// Measure the side lateral offset from the SEGMENT END rather than from the slot.
        /// </summary>
        /// <remarks>
        /// Leaning around a corner puts your head past the CORNER, and the corner is at the end of
        /// the segment, not beside you. Offsetting from the slot cannot work with the suspected 0.5:
        /// slots sit 0.6 to 0.75 m in from the edge, so the eye lands short of the end every time
        /// and stays behind the cover plane. Paired against retail on SCI_Hub the slot-relative
        /// form needs a fitted 1.0 to peak (peek agreement 70.9/73.8% against 51.5/52.4% at 0.5),
        /// which is exactly the amount that clears a typical slot inset - a strong hint that the
        /// reference point is wrong rather than the distance.
        /// </remarks>
        public bool AimShootLateralFromSegmentEnd = false;

        /// <summary>
        /// Decide whether a lean EXISTS with an explicit lateral clearance test at the cover end,
        /// and leave the shoot position free to compute the ARC.
        /// </summary>
        /// <remarks>
        /// Two independently fitted forms converged on the same answer. Offsetting from the slot
        /// wants 2.5 m and offsetting from the segment END wants 2.0 - and since a slot sits 0.7 m
        /// from its end, those are the same distance. Neither is a head position; what they are
        /// both measuring is whether there are about two metres of clear space past the corner,
        /// which is what an NPC needs in order to step out and shoot at all.
        /// <para>Saying that directly separates the two jobs the proxy had been doing at once:
        /// this test decides liveness, and the shoot position goes back to the original 0.5
        /// lateral / 0.1 forward so the ARC is measured from the right place. That is the only way
        /// the arc VALUES can come right - a sweep taken 2.5 m to the side describes somewhere the
        /// NPC never stands.</para>
        /// </remarks>
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
        /// <remarks>
        /// This is what the move-from position is for, and it is the fix for the defect
        /// that made our lean arcs meaningless. The side shoot position sits 0.1 m behind the cover
        /// plane; when the wall carries on past the slot that point is INSIDE the wall, but no
        /// per-direction ray test notices, because the rays at the extremes of the sweep run
        /// parallel to the wall and never cross it. The arc came back live on grazing rays alone.
        /// Measured against retail by distance to the end, our lean-live rate ran 37.5 / 43.1 /
        /// 50.0 / 80.0% where retail runs 75.0 / 75.0 / 25.0 / 0.0 - backwards, and worst exactly
        /// where the head is deepest inside the wall.
        /// <para>A head inside a wall cannot be reached from a position out in the room, so one
        /// segment test between the two points removes the whole class.</para>
        /// </remarks>
        public bool RequireShootPositionReachable = true;

        /// <summary>
        /// How far a direction must be unobstructed to count as clear, in metres, and the separate
        /// distance used for the vertical cone.
        /// </summary>
        /// <remarks>
        /// <c>clear_aim_angle_distance_to_consider_clear</c> = 1.5 and
        /// <c>..._for_vertical_cone</c> = 2.5. These replace <see cref="AimClearRange"/>,
        /// <see cref="AimDownRange"/> and <see cref="AimUpRange"/>, all three of which were fitted
        /// against a broken eye position - and the fitted 2.2 m down range was groping directly at
        /// this 2.5.
        /// </remarks>
        public float AimClearDistance = 1.5f;

        /// <summary>
        /// Distance a PITCHED ray must be open for.
        /// </summary>
        /// <remarks>
        /// At 2.5 every vertical arc we write is too WIDE - a one-directional bias, so a real
        /// defect rather than noise. Retail medians against ours by cone distance (`diag coveriter
        /// &lt;level&gt; vert2`), Tech_Hub lean 72 over 48, SCI_Hub lean 60 over 48:
        /// 2.50 gives 84/72 and 84/72, 2.75 gives 72/60 and 72/60, and 3.00 gives 60/48 and 60/60.
        /// Total absolute error over the four medians falls from 84 to 24, and peek agreement holds
        /// or improves (84.5 -&gt; 84.6 on Tech_Hub).
        /// <para>The evidence for 3.0 that does NOT depend on our proxy shoot position: the
        /// over-the-top eye is the one firing position we place correctly, and its vertical arc
        /// lands EXACTLY on retail at 3.0 (48 against 48) where 2.5 leaves it 24 degrees wide. The
        /// lean verticals inherit the proxy and should be re-fitted once that is solved.</para>
        /// </remarks>
        public float AimClearDistanceVerticalCone = 3.0f;

        /// <summary>
        /// Narrowest arc a firing position may have and still be written as live.
        /// </summary>
        /// <remarks>
        /// <para>These are the peek angles -
        /// <c>angle_for_peek_flag_left</c> = -30 ("60 degree arc for peeking"),
        /// <c>arc_for_peek_flag_over</c> = 60 ("30 either side") and
        /// <c>vertical_arc_for_peek_flag</c> = 11 - and the natural reading was that they set a
        /// bit in the slot's Flags. They do not. Cross-tabbed over retail's 9,085 slots with
        /// `diag coverslots all`, no bit behaves like a peek flag, but every LIVE firing position
        /// already clears all three: bit 0x1 implies a lean-left arc reaching -30 on 100.0% of
        /// 3,480 slots and a vertical arc of at least 11 degrees on 100.0% of them, 0x2 the same
        /// for 3,459 lean-rights and 0x4 for 4,984 over-the-tops. Retail ships not one arc below
        /// these widths, so they are the ADMISSION test for a firing position, not a label on it.
        /// </para>
        /// <para>All three land exactly on the 12-degree nibble grid the format stores (-90 + 12k
        /// puts a sample on -30 and +30, and 11 rounds to one step), which is what a threshold
        /// meant to be representable looks like.</para>
        /// </remarks>
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
        /// <remarks>
        /// The cover you lean around blocks the far half of your sweep, and the obstacle probe does
        /// not model it - so our lean arcs ran to the sweep limit and wrote a full 180 degrees.
        /// Retail's live lean arcs, measured on SCI_Hub with `diag coverslots`, have an inner edge
        /// of +18 degrees on the left and -18 on the right at the median, on BOTH cover classes,
        /// p10 -30/-42 and p90 +42, with only 2-7% of left leans reaching +90 and 0% of right leans
        /// reaching -90. Ours were pinned at the limit on 65-86% of live leans.
        ///
        /// This is a CAP, not an assignment: a lean the probe already found blocked short of 18
        /// degrees keeps its narrower arc, which is where the spread below the median comes from.
        /// The aim clear range is NOT the lever here - sweeping it from 0.5 m to 8 m leaves the
        /// high-cover widest arc at 180 degrees throughout and only damages low cover.
        /// </remarks>
        /// <summary>
        /// A lean arc runs CONTIGUOUSLY inward from its outer edge, stopping at the first blocked
        /// direction, instead of spanning every clear direction found anywhere in the sweep.
        /// </summary>
        /// <remarks>
        /// The sweep took min/max over all clear yaws, so a single clear direction at the far end
        /// set the whole arc - and a grazing ray parallel to the wall is always clear, which pinned
        /// our raw inner edge at the +90 sweep limit. Retail sits at +18. That 72 degree gap is
        /// exactly the median arc error we have been carrying all along.
        /// <para>The spread is not the problem: with the cap off our pin rate is 45.1% at a 3 m
        /// clear distance against retail 40.8%, so the geometry already varies about as much as
        /// retail. Only the centre is wrong, and a cap cannot fix a centre - it collapses
        /// everything above it onto one value and takes the pin rate to 92%.</para>
        /// <para>OFF, because it fixes the spread without fixing the centre. Contiguous runs bring
        /// the pin rate from 91.9% to 47.0% on Tech_Hub against retail 40.8% - the shape becomes
        /// right - but the values still spread around ~90 rather than ~18, so the median arc error
        /// goes UP (12 to 60) and peek agreement down (84.5 to 83.1). A distribution with the right
        /// width in the wrong place is not progress. Kept because the mechanism is sound and it is
        /// the correct half of the fix - turn it on the day the eye position is solved, which is
        /// what actually sets the centre.</para>
        /// </remarks>
        public bool ContiguousLeanArc = false;

        public float LeanInnerLimitDegrees = 18f;

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
        /// <remarks>
        /// <para>OFF. It was a proxy for the buried-head defect that
        /// <see cref="RequireShootPositionReachable"/> now removes properly, and once that is in
        /// place this gate only destroys good leans: peek agreement falls from 74.8/75.7% to
        /// 51.5/52.4% and the live counts from 67/67 to 35/23 against retail 62/65.</para>
        /// </remarks>
        public bool LeanNeedsAnEnd = false;

        /// <summary>
        /// How near an end a slot must be for a lean past it to exist. 0 uses
        /// <see cref="HeightSamplingDistanceAlongNormal"/>.
        /// </summary>
        /// <remarks>
        /// Measured straight off retail with `diag coverpair`, binning its own slots by distance to
        /// the left end and asking how often the lean-left arc is live: 75.0% at 0.25 m, 75.0% at
        /// 0.75 m, 25.0% at 1.25 m and 0.0% at 2 m. A clean cutoff around a metre.
        /// <para>This is NOT something the aim geometry produces on its own, which is what I
        /// assumed when first turning <see cref="LeanNeedsAnEnd"/> off for the two-position model.
        /// With the eye 0.1 m behind the cover plane, the rays at the extremes of the sweep run
        /// PARALLEL to the wall and never cross it, so they return clear and the arc is declared
        /// live on grazing rays alone - most often exactly where the slot is FURTHEST from an end.
        /// Ours ran 37.5 / 43.1 / 50.0 / 80.0% across those same bins, backwards. The
        /// <see cref="LeanInnerLimitDegrees"/> cap then clamped the meaningless inner edge onto
        /// retail's median and made it look right; turning the cap off moves the median arc error
        /// from 12 to 72 degrees, which is the tell.</para>
        /// </remarks>
        public float LeanMaxDistanceToEnd = 0f;

        /// <summary>
        /// A slot may only lean past the end of the segment it is NEARER to.
        /// </summary>
        /// <remarks>
        /// Retail's rule is structural, not geometric. Bucketing its slots by position along the
        /// segment (`diag coverwind all`), the lean-right arc is live on 0.0% of slots in the left
        /// 40% of a segment and the lean-left arc on 0.0% of slots in the right 40%, on every level
        /// measured - HAB_Airport, SCI_Hub and Tech_Hub all show the same hard zero. Only slots at
        /// the midpoint, which is where a single-slot segment puts its one slot, carry both.
        /// <para>It also settled the FIELD assignment question it was written for. Leans go live at
        /// the end they are near, so <c>LeftEdge</c> names the segment's own Left endpoint - and
        /// running the same table over our bake gives retail's pattern rather than its mirror
        /// (100.0 / 83.3% lean-left at Pct 0.1 / 0.3 and 0.0% lean-right, 0.0 / 62.5% the other way
        /// at 0.7 / 0.9), so the solver and the winding convention were both already right.</para>
        /// <para>OFF because the aim geometry already produces the rule and enforcing it on top only
        /// over-prunes. Our own slots reproduce retail's hard zeros unaided; the explicit gate then
        /// bites on the mid-segment slots that sit near but not exactly at Pct 0.5, and pushes the
        /// lean-left dead rate from 47.7% to 50.0% against retail's 38.0%. Kept because the retail
        /// measurement is unambiguous and it is the right gate to have if the geometry ever stops
        /// delivering the pattern by itself.</para>
        /// </remarks>
        public bool LeanOnlyTowardNearerEnd = false;

        /// <summary>Step along the rim at which cover is tested.</summary>
        public float RimSampleStep = 0.25f;

        /// <summary>How far consecutive rim edges may turn and still be one run.</summary>
        public float RimRunMaxTurnDegrees = 20.0f;

        /// <summary>
        /// Rejected rim shorter than this inside an otherwise continuous span is bridged rather than
        /// splitting the segment - a doorframe, a pipe or a seam in the rasterised solid interrupts
        /// the probes without interrupting the cover.
        /// </summary>
        /// <remarks>
        /// <para>0.75 -&gt; 2.5, and it is the largest placement gain measured: mean cover F1 over
        /// Tech_Hub, HAB_Airport, SCI_Hub, Tech_MuthrCore and BSP_Torrens goes 47.2 -&gt; 51.0.
        /// BSP_Torrens, a corridor level and our worst, goes 28.7 -&gt; 42.9.</para>
        /// <para>The tell that these really are fragments of one run rather than new cover: the
        /// SEGMENT COUNT barely moves while coverage climbs - HAB_Airport 586 -&gt; 599 for +2.7 F1,
        /// Tech_Hub 568 -&gt; 589. Compare the wrong fix, lowering MinimumLength to 0.75, which
        /// scores similarly by inflating the count to 756 and 787; retail ships nothing under 0.9
        /// (cover_minimum_length is a cull and its median segment is 1.17-1.77 m), so that is the
        /// metric being gamed rather than the data getting closer.</para>
        /// <para>Found by re-running the feature search on tables regenerated against the corrected
        /// obstacle top: a six-term conjunction reached per-edge F1 52.6% where the generator sat at
        /// 45.1%, which is what said the headroom was in the gates rather than the candidates.</para>
        /// </remarks>
        public float SpanGapTolerance = 2.5f;

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
        /// <remarks>
        /// OFF. Retail ships NO unusable slot - 100% of its slots carry a live firing position on
        /// every level measured - and a slot an NPC can occupy but never fire from is worse than no
        /// slot, because it still gets chosen. Keeping them scored well on F1 (they cover rim) and
        /// badly on everything that matters: `diag coveriter &lt;level&gt; qsweep` puts usable slots
        /// at 61.5% on Tech_MuthrCore with these kept, and the quality score 90.4 against retail
        /// 94.9. Dropping them gives 95.9% usable and a score of 95.5, ahead of retail, with the
        /// segment count falling 196 -&gt; 120 against retail's 113.
        /// </remarks>
        public bool KeepSegmentsWithoutClearAim = false;

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
