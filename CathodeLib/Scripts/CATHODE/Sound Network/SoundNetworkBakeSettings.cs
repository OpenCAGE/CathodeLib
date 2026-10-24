#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Sound
{
    /// <summary>
    /// Settings for the sound node network bake. Passing null instead of an instance skips the bake
    /// entirely and leaves SNDNODENETWORK.DAT as it was found on disk, the same way the radiosity
    /// bake is opted into.
    /// </summary>
    public sealed class SoundNetworkBakeSettings
    {
        /// <summary>
        /// Spacing between two generated nodes, as a multiple of network_node_min_spacing. 1.00 is
        /// the authored spacing used directly; the old 1.30 was fitted on BSP_TORRENS at a time when
        /// the scatter also filled the alien's ceiling, and BSP_TORRENS is still the only level that
        /// prefers it.
        /// </summary>
        public float AutoSpacingScale = 1.00f;

        /// <summary>
        /// Two hand-placed SoundNetworkNode entities closer than this come through as one; the
        /// first in entity order is kept. 0 disables.
        /// </summary>
        public float AuthoredDedupDistance = 0.5f;

        /// <summary>
        /// When a level ships more than one SoundLevelInitialiser, take the LAST one rather than the
        /// first. Four campaign levels ship two, and on all four retail's fill follows the last.
        /// </summary>
        public bool UseLastInitialiser = false;

        /// <summary>
        /// network_node_min_spacing to assume when a level ships no SoundLevelInitialiser at all.
        /// </summary>
        public float NoInitialiserSpacing = 1.4f;

        /// <summary>
        /// Keep a fill candidate only where there is a ceiling above it, within the level's
        /// <c>network_node_ceiling_height</c>. Off by default while measured.
        /// </summary>
        public bool FillRequiresCeiling = false;

        /// <summary>
        /// Divisor taking <c>network_node_ceiling_height</c> to metres. 300 -> 3.0 m at the default.
        /// </summary>
        public float CeilingHeightScale = 100.0f;

        /// <summary>
        /// Offer fill candidates from STANDING navmesh polygons only. Off by default while measured.
        /// </summary>
        public bool FillStandingOnly = false;

        /// <summary>
        /// A fill candidate must be visible from some SoundEnvironmentMarker within the
        /// initialiser's <c>network_node_max_visibility</c>. Only ever active on a level that has a
        /// SoundLevelInitialiser; off by default while measured.
        /// </summary>
        public bool FillRequiresMarkerSight = false;

        /// <summary>
        /// Which points on a navmesh polygon are offered to the fill, and in what order.
        /// 0 - corners, centroid, edge midpoints and edge-to-centre midpoints, in that order.
        /// 1 - centroid only. 2 - centroid and corners. 3 - a uniform lattice at
        /// <see cref="CandidateLatticeStep"/>. 4 (default) - as 0, but the centroid is appended last
        /// and the fill walks the pending list backwards, so the middle of a piece of floor is taken
        /// before its edges.
        /// </summary>
        public int CandidateMode = 4;

        /// <summary>
        /// Visit fill candidates in list order rather than reversed. The acceptance test is unchanged;
        /// only which of two candidates competing for the same floor wins moves.
        /// </summary>
        public bool FillForwardOrder = false;

        /// <summary>Lattice step for CandidateMode 3, as a multiple of the authored spacing.</summary>
        public float CandidateLatticeStep = 0.5f;

        /// <summary>
        /// Scales the per-class cap on how far a marker's network may reach. 0 disables the cap.
        /// </summary>
        public float RoomExtentScale = 1.0f;

        /// <summary>
        /// How close two networks' nodes must come for the networks to count as adjoining.
        /// </summary>
        public float AdjoinDistance = 5.0f;

        /// <summary>
        /// How a candidate boundary must answer for a sound barrier. 0 not at all (default - the
        /// barrier is still looked up for the guid we write, and a miss tolerated), 1 a barrier
        /// pivot within <see cref="BarrierSearchRadius"/> of the crossing's midpoint, 2 the crossing
        /// must pass THROUGH barrier collision geometry, 3 the two networks must see each other
        /// with every barrier taken OUT of the occluders - they adjoin through an opening.
        /// </summary>
        public int BarrierBoundaryTest = 3;

        /// <summary>
        /// How far from a boundary a barrier may sit and still count as the barrier for it. Fine at
        /// 4.0 m while it only chooses which guid to write, but far too loose to gate on: our levels
        /// carry 76 to 194 barriers each, so almost any two rooms that pass near each other find
        /// one. Retail's own boundaries sit within about a metre of their barrier (median 0.90 to
        /// 1.08 m over seven levels), so anything further away belongs to some other door.
        /// </summary>
        public float BarrierSearchRadius = 4.0f;

        /// <summary>
        /// Under <see cref="BarrierBoundaryTest"/> 3, also leave SOUND and SOUND_BARRIER typed
        /// collision out of the opening soup, not just the barrier entities.
        /// </summary>
        public bool OpeningSkipsSoundCollision = true;

        /// <summary>
        /// Under <see cref="BarrierBoundaryTest"/> 3, whether a sealed network's crossing is exempt
        /// from the opening test (true) or must pass it like any other (false).
        /// </summary>
        public bool OpeningExemptsSealed = false;

        /// <summary>
        /// Under <see cref="BarrierBoundaryTest"/> 3, the widest join (metres) two networks may have
        /// and still adjoin; 0 disables. Width is the XZ extent of every candidate crossing's
        /// midpoint between the two networks.
        /// </summary>
        public float OpeningMaxWidth = 0f;

        /// <summary>
        /// Under <see cref="BarrierBoundaryTest"/> 3, also require a barrier pivot within
        /// <see cref="BarrierSearchRadius"/> of the kept crossing for a NAMED-to-named boundary.
        /// </summary>
        /// <para><b>Measured over the whole campaign and left OFF (7 Sep 2026, run `c16`).</b> On a
        /// seven-level sweep it looked free - CorporatePent +0.71, Solace +0.38, ChallengeMap9 +0.43,
        /// zero on the other four - but those levels were all ones we OVER-link, where removing any
        /// boundary flatters the count ratio. Over 32 levels it is a net loss, and the reason is not
        /// the metric: it removes TRUE boundaries. BSP_LV426_PT02 -5.93 with boundary RECALL 83% ->
        /// 67% (precision stays 100%), ChallengeMap4 -0.35 with recall 97% -> 94%, TECH_RnD_HzdLab
        /// -0.32 with recall 85% -> 81%, plus CM1, ENG_ReactorCore and TECH_RnD.
        /// Retail's own files do say every one of its 3,050 boundaries carries a barrier
        /// (`diag barrshare`), so the rule is right and our BARRIER COLLECTION is what is short -
        /// `CollectBarriers` only sees SoundBarrier and NavMeshBarrier entities, and retail's
        /// BarrierInstanceGuid indexes something we have never decoded (`diag barrguid` matches it
        /// on 0% of boundaries). Do not turn this on until that is solved.</para>
        public bool OpeningRequiresBarrier = false;

        /// <summary>
        /// Under <see cref="BarrierBoundaryTest"/> 3 with <see cref="SealedNetworkLinking"/> on, a
        /// sealed network's crossing must also have a barrier pivot within
        /// <see cref="BarrierSearchRadius"/>. A door node has a door.
        /// </summary>
        /// <para><b>false -> true (2 Sep 2026).</b> Once sealed networks of up to three authored nodes may hold a
        /// boundary (<see cref="SealedNetworkLinking"/> 4), the ones they gain without a barrier are all
        /// false: BSP_TORRENS 26 -> 30 links and 78 -> 92 paths against retail's exact 26 / 78, back to exact
        /// with this on; ChallengeMap9 loses 8 links and 48 paths that were every one of them wrong (boundary
        /// true positives unchanged at 12), which the harness's count ratios reward and the boundary set does
        /// not. Retail's 3,050 boundaries all carry a barrier. Measured with `diag sounditer`.</para>
        public bool SealedRequiresBarrier = true;

        /// <summary>
        /// Under <see cref="BarrierBoundaryTest"/> 3 with <see cref="OpeningSkipsSoundCollision"/>,
        /// test a SEALED network's crossing against the soup that still holds the SOUND hulls.
        /// </summary>
        public bool SealedOpeningKeepsSoundHulls = true;

        /// <summary>
        /// Whether a sealed network - one no marker reached, so written with no name and no reverb -
        /// may hold a boundary. 0 never (default), 1 only when it holds at most
        /// <see cref="SealedLinkMaxNodes"/> nodes, 2 always, 3 only when a barrier sits at the
        /// crossing, 4 only when it is that small AND made entirely of AUTHORED nodes.
        /// </summary>
        /// <para><b>1 -> 4 with <see cref="SealedLinkMaxNodes"/> 3 (2 Sep 2026).</b> A lift's door nodes are
        /// authored and come in twos and threes (a shaft pair, a front group with the door package beyond), and
        /// retail links them to the car at each stop. Bake-free: ChallengeMap9 links 50 -> 56 (retail 56) and
        /// paths 232 -> 301 (304), SCI_HospitalLower paths 820 -> 947 (947); with <see cref="SealedRequiresBarrier"/>
        /// on so the door is real. Mode 4 = authored-only sealed networks, fill pockets stay unlinked.</para>
        public int SealedNetworkLinking = 4;

        /// <summary>
        /// How a marker claims its first nodes. 0 - the nearest node it can see, seeded at cost zero.
        /// 1 - every node it can see, each seeded at its straight-line distance.
        /// </summary>
        public int MarkerSeedMode = 0;

        /// <summary>
        /// Which line a sight test between two nodes uses. 0: the raised line (+0.5 m) first and
        /// the node-height line as a fallback, clear if either is (as shipped 2 Sep 2026). 1: the
        /// node-height line only. 2: the raised line only. 3: both must be clear.
        /// Retail node pairs on ChallengeMap9 whose raised line is clear but whose node-height line
        /// is blocked are the pairs retail links as OBSTRUCTED or not at all, and every link our
        /// marker flood used to walk down the stairwell into the lower floor retail leaves sealed
        /// was one of them (`diag linkcross`, `diag netbridge`).
        /// </summary>
        public int SightTestMode = 0;

        /// <summary>
        /// What a marker floods THROUGH to claim nodes. 0: the node-to-node sight graph (as
        /// shipped). 1: the navmesh - polygon to polygon across shared edges and off-mesh links,
        /// never into a barrier area (a doorway), and a node belongs to the marker whose walk
        /// reaches the polygon under it most cheaply; a node on no reached polygon stays unowned.
        /// Retail's StartingAreaStairway on ChallengeMap9 is exactly the 41 polygons its marker
        /// reaches this way (one floor plus the vent mouth), while the sight flood runs down the
        /// stair into a mezzanine retail leaves as a 54-node sealed network (`diag navflood`).
        /// </summary>
        public int FloodMedium = 0;

        /// <summary>
        /// Leave the PATH_CLOSED collider of a door out of the sound occluder soup, so a doorway
        /// does not seal a node off from the room it stands in. The radiosity bake already treats
        /// doors as open for the same reason.
        /// </summary>
        public bool SkipDoorBarriers = false;

        /// <summary>
        /// Leave SOUND and SOUND_BARRIER typed collision - the authored occlusion hulls - out of the
        /// occluder soup the marker FLOOD and the node links see through. Off by default.
        /// </summary>
        public bool FloodSkipsSoundHulls = false;

        /// <summary>
        /// Drop every generated (non-authored) node that the flood assigns to a network whose
        /// marker is <c>room_size</c> Vent. Off by default while it is measured.
        /// </summary>
        public bool NoFillInVents = false;

        /// <summary>
        /// Metres of head start a bigger room gets over a smaller one when their floods contest a
        /// node: a marker's seed cost is this times its class rank (large room 0, medium 1,
        /// corridor 2, small room 3, vent 4). 0 disables (default while measured).
        /// </summary>
        public float ClassSeedBias = 0f;

        /// <summary>
        /// Fold a sealed pocket whose nodes stand on STANDING navmesh into the named network one of
        /// them can see through the hull-free soup - the flood's occluders with the door barriers
        /// and the authored sound hulls removed. Off by default while measured.
        /// </summary>
        public bool AbsorbStandingPocketsThroughHulls = false;

        /// <summary>How far a standing pocket may look through the hull-free soup for an owned node.</summary>
        public float StandingPocketSeeRange = 8.0f;

        /// <summary>
        /// Smallest pocket the standing-pocket fold touches. A single node in a doorway is a door
        /// node - <see cref="SealedNetworkLinking"/> decides it - and folding those undid the leaf
        /// admission (HospitalLower paths 903 -> 820, HzdLab 300 -> 231 with a floor of 1).
        /// </summary>
        public int StandingPocketMinNodes = 3;

        /// <summary>
        /// Leave CollisionBarrier volumes out of the sound occluder soup - see
        /// <c>SoundNodeNetworkGenerator.GameplayBarrierInstances</c>.
        /// </summary>
        public bool SkipGameplayBarriers = true;

        /// <summary>
        /// Drop an authored node that can see no other node AND has no navmesh beneath it - see
        /// <c>SoundNodeNetworkGenerator.DiscardOrphanedManualNodes</c>.
        /// </summary>
        /// <para><b>false -> true (2 Sep 2026)</b>, with <see cref="OrphanSightRange"/> 12 and
        /// <see cref="OrphanSightThroughDoors"/>. The rule that did not generalise judged sight against the full
        /// soup with no range: a void node forty metres from anything counted as seeing another void node, and a
        /// lift's door node counted as blind because the car it links to is behind the lift door. Campaign census
        /// (`diag authfeat`, six levels, 4,351 authored nodes): floorless nodes with a visible node within 12 m,
        /// doors transparent, are kept 653 of 655; with none, dropped 62 of 73.</para>
        public bool DiscardOrphanNodes = true;

        /// <summary>
        /// How near another node has to be for the orphan test to count it as seen; 0 means any of
        /// the 32 nearest, however far. Campaign census of authored nodes (`diag authfeat`,
        /// 2 Sep 2026, 25 levels): among floorless nodes not explained by the 0.5 m dedup, those
        /// with a visible authored neighbour within 12 m are kept 99.5% of the time (1,232 of
        /// 1,238) and those without are dropped 58% of the time (91 of 158) - a door package or a
        /// cupboard node backing into the void sees other void nodes forty metres away and nothing
        /// else. Floored nodes are kept regardless (1.3% missing).
        /// </summary>
        public float OrphanSightRange = 12.0f;

        /// <summary>
        /// Judge the orphan test's sight with door barriers removed from the soup. Same census with
        /// the nearest visible RETAIL node (fill included) measured doors-transparent, CM11 + CM9:
        /// floorless non-dedup nodes with one within 3 m are kept 62 of 62, within 6-12 m 7 of 7,
        /// none within 12 m kept 6 and dropped 18; with doors solid the same split was 12 kept
        /// against 15 dropped, the difference being lift door nodes whose only company is the car
        /// behind the lift door. Retail's own links cross a door with obstruction 0.
        /// </summary>
        public bool OrphanSightThroughDoors = true;

        /// <summary>
        /// Judge the orphan test's sight with the authored SOUND / SOUND_BARRIER hulls removed from
        /// the soup as well as the doors. Retail's node links pass through both with obstruction 0
        /// (the CM9 link census agrees 98.9% at node height once PATH_CLOSED, SOUND and
        /// SOUND_BARRIER are ignored), and on BSP_LV426_Pt01 the two door-package nodes retail keeps
        /// - one of them the whole of the named network 'Narrow Pass Start', carrying 24 and 121
        /// links - stand INSIDE a SoundBarrier volume, so with the hulls solid they saw nothing at
        /// all and were dropped, costing the level two of its seven networks (sound 86.4 -> 77.1).
        /// </summary>
        public bool OrphanSightThroughSoundHulls = true;

        /// <summary>
        /// Two authored nodes of the same door package - a composite whose name contains "door",
        /// holding exactly two SoundNetworkNodes 1-2.5 m apart, one either side of its doorway -
        /// that end up in different networks are a boundary by construction, whether or not the
        /// opening sight test can see between them: the door leaf is exactly what blocks that line.
        /// TECH_COMMS lost 5 component-joining boundaries to the sight test (paths 486 against
        /// retail's 2,145) on pairs 1.3-1.6 m apart blocked by the leaf, a window typed STANDARD or
        /// a vent grille. Between marker networks only (a sealed door-leaf network attaches to its
        /// nearest room alone). Measured a NO-OP on Torrens, CM9, CM12, CM11, Solace and TECH_COMMS
        /// itself - the lost boundaries there are stair, window and vent-grille pairs - so it ships
        /// off until the opening test can use it.
        /// </summary>
        public bool DoorPairIsBoundary = false;

        /// <summary>
        /// The orphan test's "floor beneath" means BENEATH: the navmesh triangle under the node's XZ
        /// must sit within 0.15 m below to 3 m below it. Without the height check a node buried under
        /// a floor counted as floored - CHALLENGEMAP16's seven Vent_Floor_Filler nodes sit 0.66 m
        /// under the vent floor, retail's file has none of them, and ours became sealed pockets that
        /// bridged the vents.
        /// </summary>
        public bool OrphanFloorMustBeBelow = true;

        /// <summary>
        /// Glass (TRANSPARENT / DYNAMIC_TRANSPARENT collision) is removed from the opening test's
        /// soup: two rooms that face each other through a window adjoin acoustically, and retail
        /// declares the boundary - CHALLENGEMAP5's 'meeting room of doom' is two networks split by
        /// windows (85 and 94 nodes, paths 211 against 351 without the link), TECH_COMMS's Main Comms
        /// Right / Transmission pair sees only window glass. Glass stays solid for the flood.
        /// </summary>
        public bool OpeningSkipsGlass = true;

        /// <summary>
        /// A sealed network's crossing that PIERCES a door barrier box is tested against the hull-free
        /// opening soup rather than the strict one (<see cref="SealedOpeningKeepsSoundHulls"/>): the
        /// authored SOUND hull laid over the doorway is what blocked CHALLENGEMAP1's 'First Corridor'
        /// and CHALLENGEMAP16's 'Top Lobby Fans' door leaves that retail links. Letting every sealed
        /// crossing through the hulls was wrong on CM9 (links 52 -> 66 against 56); piercing the door
        /// is the narrower question.
        /// </summary>
        public bool SealedSeesThroughHullsAtDoor = true;

        /// <summary>
        /// <summary>
        /// Furthest a node link may reach when its two nodes ended up in DIFFERENT networks. A link
        /// that leaves its room is a doorway crossing in retail's files and is short; ours ran the
        /// length of the level, because links are built before anything knows which room a node is
        /// in. Zero keeps every link.
        /// </summary>
        public float CrossNetworkLinkDistance = 0.0f;

        /// <summary>
        /// A node link may only leave its own network between two networks that declare a boundary
        /// with each other. Retail's do: sound network rule 69 read its cross-network links as
        /// existing only between networks that share one, and the shape of the file agrees.
        /// </summary>
        public bool CrossNetworkLinksNeedBoundary = true;

        /// <summary>
        /// Put back a candidate boundary the gates refused when, without it, a named network can be
        /// reached from nowhere else. Nearest crossing first, and only ever enough of them to make
        /// the named graph connected.
        /// </summary>
        /// <para><b>OFF until 7 Sep 2026, because on its own it is a WASH.</b> It lifts the levels whose
        /// graph really had fallen apart - TECH_COMMS sound 82.7 -> 87.4, SCI_HospitalUpper 86.9 -> 90.1 -
        /// and costs the levels whose connectivity was already AHEAD of retail's: Solace -1.2 (we wrote
        /// 303 paths against retail's 279 before it), CHALLENGEMAP11 -1.7, HospitalLower -0.7. Five up,
        /// five down. Restricting it to networks with no boundary at all
        /// (<see cref="ReconnectIsolatedOnly"/>) throws the gains away and keeps the losses.</para>
        /// <para><b>What made it pay is <see cref="ReconnectMinComponent"/>.</b> A level's rooms are NOT
        /// simply one connected space - retail ships a dominant piece and a tail of tiny ones, and the
        /// losses above were all this rule joining a two- or three-network piece retail keeps apart. With
        /// a size floor of 4 it only ever joins pieces too big to be genuinely cut off, and with
        /// <see cref="ReconnectSearchDistance"/> it can reach the ones no refused candidate covers.
        /// Together with <see cref="OpeningRequiresBarrier"/>, over seven levels: TECH_COMMS 82.7 -> 88.7,
        /// HospitalUpper 86.9 -> 88.7, CorporatePent 87.1 -> 87.8, Solace 91.1 -> 91.5, CM9 90.6 -> 91.0,
        /// CM16 and Tech_Hub unmoved - mean +1.33 and **no level regresses**.</para>
        public bool ReconnectNetworkGraph = true;

        /// <summary>
        /// Restrict <see cref="ReconnectNetworkGraph"/> to networks that hold NO boundary at all,
        /// rather than merging any two components.
        /// </summary>
        public bool ReconnectIsolatedOnly = false;

        /// <summary>
        /// How far <see cref="ReconnectNetworkGraph"/> may look for a crossing of its own when no
        /// refused candidate exists between two parts of the graph. 0 uses refused candidates only.
        /// </summary>
        /// <para><b>Measured over twelve balanced levels and left OFF (7 Sep 2026).</b> At 15 m with a
        /// component floor of 4 it is the best thing that has happened to TECH_COMMS - paths 737 ->
        /// 1892, sound 86.97 -> 88.68 - and it pays every point of it straight back on
        /// ENG_TowPlatform, where the boundary it adds is CORRECT (recall 96% -> 100%) but the path
        /// count goes 247 -> 436 against retail's 304, because our other boundaries there are wrong.
        /// Mean over the twelve is identical with it on at 15/4 and off (+0.55 either way), and a
        /// floor of 6 or more simply stops it firing at all. It is off because it earns nothing, not
        /// because it is wrong: it should pay once the network PARTITION improves - see rule 75's
        /// ceiling, where six levels are negative for want of networks we never paired.</para>
        public float ReconnectSearchDistance = 0f;

        /// <summary>
        /// The fewest networks a piece of the graph may hold for <see cref="ReconnectNetworkGraph"/>
        /// to join it to another - both when putting a refused boundary back and when searching within
        /// <see cref="ReconnectSearchDistance"/>. 0 or 1 joins everything, lone networks included.
        /// </summary>
        public int ReconnectMinComponent = 4;

        /// Two MARKER networks whose closest crossing pierces a door barrier box, with the two nodes
        /// within <see cref="DoorCrossingMaxDistance"/>, adjoin at that door whatever the opening
        /// sight test says - the closed leaf is what it cannot see through. Retail's boundary node
        /// pairs spike at 1.5-1.6 m (the door_audio prefab straddling a door), and the campaign's
        /// largest path deficits were each one or two such pairs refused: HAB_AIRPORT's 'Baggage'
        /// / 'I link floors' (1.6 m, paths 1954 against 3570), TECH_COMMS's reception corridor pairs
        /// (1.5 m, 497 against 2145), SCI_HOSPITALUPPER, SCI_ANDROIDLAB. Sealed networks keep their
        /// own narrower rule (<see cref="SealedSeesThroughHullsAtDoor"/>).
        /// </summary>
        public bool DoorCrossingIsBoundary = true;

        /// <summary>Furthest apart the two nodes of a door crossing may be for <see cref="DoorCrossingIsBoundary"/>.</summary>
        public float DoorCrossingMaxDistance = 2.0f;

        /// <summary>
        /// Two MARKER networks whose nearest nodes are within this distance adjoin whatever the
        /// opening sight test says. 0 disables it.
        /// </summary>
        /// <para><b>Measured and left OFF (7 Sep 2026).</b> Over six levels at 1.50, 1.75, 2.00 and
        /// 2.50 m, with and without the barrier: mean +0.02 at best, and negative above 2 m (Solace
        /// -1.56 at 2.00). The reason is in `diag bridges`: the boundaries we WRONGLY HOLD and the
        /// ones we WRONGLY REFUSE have the same distance distribution - both p50 1.5 m, both ~84%
        /// under 2 m - so no threshold separates them. Retail's 1.5 m pairs are 95% joined because
        /// retail's node-to-network assignment is right; ours is not, and distance is a property of
        /// that assignment rather than a signal independent of it. The rule that DID pay is
        /// <see cref="ReconnectMinComponent"/>, which reads the shape of the graph instead.</para>
        public float NearCrossingDistance = 0f;

        /// <summary>
        /// <see cref="NearCrossingDistance"/> additionally requires a barrier pivot within
        /// <see cref="BarrierSearchRadius"/> of the crossing.
        /// </summary>
        public bool NearCrossingNeedsBarrier = false;

        /// <summary>
        /// Nearest crossings CLOSER than this are not admitted by <see cref="NearCrossingDistance"/>.
        /// </summary>
        public float NearCrossingMinDistance = 0f;

        /// <summary>
        /// <see cref="SealedSeesThroughHullsAtDoor"/> applies to sealed networks of at least this many
        /// nodes. A lone authored node behind a FAKE door (BSP_TORRENS, a door package whose far side
        /// is the abyss) is absent from retail; with the rule unguarded it gained its link and was
        /// kept (18/26/78 exact -> 19/28/91). The door leaves retail links are two or three nodes.
        /// </summary>
        public int SealedAtDoorMinNodes = 2;

        /// <summary>
        /// <see cref="SealedSeesThroughHullsAtDoor"/> additionally requires the sealed node and its
        /// partner to be the two authored nodes of one door package (see
        /// <see cref="DoorPairIsBoundary"/> for the grouping): the leaf sees across ITS door and no
        /// other. With the pierce test alone CM9 gained four false boundaries (links 54 -> 62 against
        /// 56, paths 303 -> 435 against 304).
        /// </summary>
        public bool SealedAtDoorRequiresDoorPair = true;

        /// <summary>
        /// Keep a group of nodes the marker flood never reached, as a nameless network with no
        /// reverb. False discards them outright.
        /// </summary>
        public bool KeepUnreachedNodes = true;

        /// <summary>
        /// Drop a network that links to nothing and holds fewer than two nodes. Retail ships none:
        /// 0 of 1,364 networks across all 32 levels.
        /// </summary>
        public bool DropLinklessSingletons = true;

        /// <summary>
        /// Free space, in metres, under which a sealed pocket counts as a container standing in a
        /// room rather than a passage of its own, and is folded into the room. 0 disables it.
        /// </summary>
        public float EnclosedPocketExtent = 0.0f;

        /// <summary>
        /// The most boundaries a SEALED network may hold. 0 lets it hold as many as
        /// <see cref="AdjoinDistance"/> finds, which is how the marker networks work.
        /// </summary>
        public int SealedMaxBoundaries = 1;

        /// <summary>Largest sealed network that may hold a boundary under SealedNetworkLinking 1.</summary>
        public int SealedLinkMaxNodes = 3;

        /// <summary>
        /// Longest clear-sight link a sealed (marker-less) group may grow along; 0 means any link.
        /// On ChallengeMap11 our sealed grouping produced one ten-node network spanning the whole
        /// level - five void nodes plus the door nodes of three different lifts - because nodes
        /// with nothing around them see each other across the void, while retail keeps each lift's
        /// door nodes in their own one- to three-node networks. Retail's own sealed groups there
        /// join nodes up to 10.4 m apart (a lift's bottom and top door nodes, straight up the
        /// shaft) and keep the next lift's door nodes, 11.9 m away with nothing between, apart.
        /// </summary>
        public float SealedGroupMaxLink = 11.0f;

        /// <summary>
        /// Scale <see cref="SealedMaxBoundaries"/> by the sealed network's node count. Retail's
        /// one-node door leaves hold one boundary (97 of 100), but on ChallengeMap11 a lift's
        /// two-node shaft pair (bottom and top rear door nodes) links to BOTH car stops and the
        /// three-node front group to both stops and the door package beyond - with a flat cap of
        /// one we kept only the nearest and cut the upper stop off (`diag bridges`: two CUTs).
        /// </summary>
        public bool SealedBoundariesPerNode = true;
    }
}
#endif
