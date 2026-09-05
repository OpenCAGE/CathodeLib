#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Sound
{
    /// <summary>
    /// Settings for the sound node network bake. Passing null instead of an instance skips the bake
    /// entirely and leaves SNDNODENETWORK.DAT as it was found on disk, the same way the radiosity
    /// bake is opted into.
    /// </summary>
    /// <remarks>
    /// These were static fields on <see cref="SoundNodeNetworkGenerator"/> until the four agent
    /// bakes were made opt-in; the defaults below are the values those statics shipped with, and
    /// every measurement quoted was taken with them.
    /// </remarks>
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
        /// <remarks>
        /// Retail does this and we never did. `diag authcensus SCI_HOSPITALLOWER`: the level places
        /// 907 SoundNetworkNode entities, retail's file holds 823, and of the 84 it drops **77% have a
        /// kept authored node within 0.50 m against 1% of the kept ones** (kept nearest-kept p10 0.80,
        /// p50 1.21 m). The share is flat from 0.50 to 0.75 m, so the radius is about half a metre -
        /// not the level's 1.5 m minimum spacing, which most kept pairs sit inside. The dropped ones
        /// are whole prefabs' worth: every `Hiding_Cupboard_Freestanding` node (its neighbour is the
        /// door_audio pair), ladders beside landings, vent-prefab nodes beside the duct's own.
        /// <para>Bake-free at 0.5 m on six levels: node recall held on every one (worst HzdLab 95.1 ->
        /// 94.9), node F1 up on all six (HzdLab 85.2 -> 91.4, its nodes 1,046 -> 914 against retail.s
        /// 849), per-network count agreement up on five, links and paths identical everywhere. 0.75 m
        /// starts costing recall (CM11 81.4, HzdLab 86.2). Replicates on CM11 (63% of dropped within
        /// 0.5 m of a kept node, 0% of kept) and Tech_Hub (84% vs 1%).</para>
        /// <para>**Shipped at 0.5 m** (run `soundE` against `soundB`): dev four sound 91.7 -> 91.9, overall
        /// 79.2 -> 79.3; guards TORRENS 96.9 -> 97.1, Solace unchanged, HzdLab 89.9 -> 91.0. Positive on
        /// four of seven levels, neutral on two, -0.2 on CM9.</para>
        /// </remarks>
        public float AuthoredDedupDistance = 0.5f;

        /// <summary>
        /// Offer fill candidates from STANDING navmesh polygons only. Off by default while measured.
        /// </summary>
        /// <remarks>
        /// `diag fillclass SCI_HOSPITALLOWER soundB`: of retail's 562 fill nodes 98% stand on standing
        /// floor, 0% on crouch, 2% on deep crouch and none off the mesh; of our 755, 79% / 2% / 7% /
        /// 12%. Retail's fill does not go under tables or into ducts - those spaces get only the
        /// hand-placed nodes - and its density on standing floor alone is 0.207 nodes per m2 against
        /// our 0.242.
        /// </remarks>
        public bool FillStandingOnly = false;

        /// <summary>
        /// A fill candidate must be visible from some SoundEnvironmentMarker within the
        /// initialiser's <c>network_node_max_visibility</c>. Only ever active on a level that has a
        /// SoundLevelInitialiser; off by default while measured.
        /// </summary>
        /// <remarks>
        /// The two DLC maps with no initialiser land within 2% of retail's node count at the 1.4 m
        /// default spacing; every campaign level with one is 20-35% over, at authored spacings of 1.0
        /// to 1.7 - so it is the initialiser's presence that changes retail's fill, not its spacing.
        /// Its other parameter is `network_node_max_visibility` (15 on SCI_HospitalLower). If retail
        /// only fills where a marker can see within that range, levels without an initialiser get no
        /// cap, which is the split observed.
        /// </remarks>
        public bool FillRequiresMarkerSight = false;

        /// <summary>
        /// Which points on a navmesh polygon are offered to the fill, and in what order.
        /// 0 - corners, centroid, edge midpoints and edge-to-centre midpoints, in that order.
        /// 1 - centroid only. 2 - centroid and corners. 3 - a uniform lattice at
        /// <see cref="CandidateLatticeStep"/>. 4 (default) - as 0, but the centroid is appended last
        /// and the fill walks the pending list backwards, so the middle of a piece of floor is taken
        /// before its edges.
        /// </summary>
        /// <remarks>
        /// The fill takes every candidate that clears the spacing, so the candidate set sets the
        /// density. Mode 4 is better on every level measured. Centroids alone (1) helps only where
        /// we overshoot retail's node count and hurts where it is already right, which makes it a
        /// fit rather than a rule; a uniform lattice (3) loses to geometry-derived candidates at
        /// matched node counts, which is itself the finding - retail's node positions follow the
        /// navmesh, they are not a grid.
        /// </remarks>
        public int CandidateMode = 4;

        /// <summary>Lattice step for CandidateMode 3, as a multiple of the authored spacing.</summary>
        public float CandidateLatticeStep = 0.5f;

        /// <summary>
        /// Scales the per-class cap on how far a marker's network may reach. 0 disables the cap.
        /// </summary>
        public float RoomExtentScale = 1.0f;

        /// <summary>
        /// How close two networks' nodes must come for the networks to count as adjoining.
        /// </summary>
        /// <remarks>
        /// <para>Every one of retail's network links is stored against a node pair about 1.5 m apart - the
        /// spacing of the pair the door_audio prefab puts either side of a doorway - but the tail
        /// matters, because NetworkPaths are laid over the connected components of this graph and a
        /// missed boundary strands a whole network. Sweeping shows a clean cliff on SCI_Hub: 2.0 and
        /// 2.5 give 54 boundaries and 276 paths, 3.0 gives 58 and 300, and 3.5 gives 64 boundaries
        /// with all 378 paths - the last value that keeps the component structure whole while
        /// shedding redundant chords. Nothing else moves with it; boundaries do not scale with node
        /// count.</para>
        /// <para>That 3.5 was fitted with proximity as the ONLY gate. Under
        /// <see cref="BarrierBoundaryTest"/> 3 the opening is the gate and the range is only a
        /// search radius, so it relaxes to 5.0: best or tied on all four dev levels there, and 6.5
        /// starts admitting false pairs again (CM11 boundary F1 86 -> 84).</para>
        /// </remarks>
        public float AdjoinDistance = 5.0f;

        /// <summary>
        /// How a candidate boundary must answer for a sound barrier. 0 not at all (default - the
        /// barrier is still looked up for the guid we write, and a miss tolerated), 1 a barrier
        /// pivot within <see cref="BarrierSearchRadius"/> of the crossing's midpoint, 2 the crossing
        /// must pass THROUGH barrier collision geometry, 3 the two networks must see each other
        /// with every barrier taken OUT of the occluders - they adjoin through an opening.
        /// </summary>
        /// <remarks>
        /// <para>A boundary IS a doorway: across all 32 shipped files every one of retail's 3,050
        /// network links carries a non-zero BarrierInstanceGuid. <see cref="AdjoinDistance"/> alone
        /// cannot express that, because two rooms are near each other along every shared wall as
        /// well as through the door that joins them. Measured as a boundary SET rather than a count
        /// (pair our networks to retail's by position, then ask which of retail's adjoining pairs we
        /// also declare), the shipped rule has 87-91% recall at 42-59% precision - we find nearly
        /// all the real ones and declare about 1.7x too many, so precision is the whole defect.</para>
        /// <para>Mode 1 buys precision only by losing more recall, at every radius from 0.75 to
        /// 3.0 m. Mode 2 is refuted by retail's own file: only 39% of stored crossings pass through
        /// any barrier, because the barrier sits about 0.90 m to one SIDE of the midpoint - it is a
        /// LABEL retail attaches to a boundary, not a solid the boundary pierces. **Mode 3 is the
        /// rule and ships** (2 Sep 2026): it improves the boundary set on ten levels of fourteen and
        /// regresses on none, keeps BSP_TORRENS exact, and once the sound hulls are skipped
        /// (<see cref="OpeningSkipsSoundCollision"/>) it recovers all of the recall the first version
        /// lost - Tech_Hub 176 links against retail's 184 from the old rule's 224, with paths
        /// unchanged.</para>
        /// <para>Under mode 3 the run is tried at <c>SoundNodeNetworkGenerator.OpeningHeights</c>
        /// above each node, because a node sits on the navmesh and a single floor-level run is
        /// stopped by any threshold or door sill.</para>
        /// <para>On its own it still loses on the harness, because the links it removes were
        /// standing in for the sealed door networks we lacked. Paired with
        /// <see cref="SealedNetworkLinking"/> 1 and <see cref="OpeningExemptsSealed"/> false - a door
        /// node must itself SEE OUT, through the soup that keeps the hulls - it is the net win the
        /// diagnosis had been predicting: dev four sound 89.6 -> 91.7, overall 78.7 -> 79.2 (run
        /// `soundB`), TORRENS exact, Tech_Hub 74 / 184 / 2147 against retail's 72 / 184 / 2151.
        /// Every earlier attempt to admit sealed networks broke TORRENS because it admitted by size
        /// or by exemption; the opening test is the discriminator those lacked. Guarded end to end
        /// on three more levels (TORRENS exact, Solace -0.6, TECH_RnD_HzdLab -3.8, the one real
        /// loss) and bake-free on thirteen more: better on seven, level on one, worse by at most
        /// 0.14 of summed link+path ratio on five, with LV426_Pt01 and ENG_Alien_Nest landing
        /// exactly on retail's structure.</para>
        /// <para>Measured and rejected on top of mode 3 (`diag openmiss` names each miss): a longer
        /// adjoin (6.5 loses 2 on CM11), a join-width cap (our correct joins are as wide as the
        /// false ones), a per-vent boundary cap (retail's vents hold two), and a barrier requirement
        /// on named pairs (inert at 3 m, harmful at 2).</para>
        /// </remarks>
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
        /// <remarks>
        /// The opening test asks whether two rooms are physically joined. A level's authored sound
        /// occlusion hulls (`Audio_Sock_COL`, collision type SOUND) are laid across doorways to
        /// attenuate what passes through them - they describe how sound travels through an opening,
        /// not whether there is one. `diag openmiss` names them as the blocker on half of Tech_Hub's
        /// and a third of Tech_RnD_HzdLab's missed retail boundaries, each within a metre of a
        /// barrier pivot, i.e. at a doorway.
        /// </remarks>
        public bool OpeningSkipsSoundCollision = true;

        /// <summary>
        /// Under <see cref="BarrierBoundaryTest"/> 3, whether a sealed network's crossing is exempt
        /// from the opening test (true) or must pass it like any other (false).
        /// </summary>
        /// <remarks>
        /// Exempting them was the first reading - a door node stands IN the doorway, so gating it on
        /// seeing through that doorway looked circular - and it is what let sealed admission break
        /// BSP_TORRENS. Not exempting them is the discriminator sealed admission never had: TORRENS'
        /// fake-door pockets sit behind solid geometry and cannot see out, while a real door node
        /// sees both rooms. Only meaningful with <see cref="SealedNetworkLinking"/> on.
        /// </remarks>
        public bool OpeningExemptsSealed = false;

        /// <summary>
        /// Under <see cref="BarrierBoundaryTest"/> 3, the widest join (metres) two networks may have
        /// and still adjoin; 0 disables. Width is the XZ extent of every candidate crossing's
        /// midpoint between the two networks.
        /// </summary>
        /// <remarks>
        /// A doorway is narrow - retail's boundary endpoints are 1.50 m apart, the door_audio pair -
        /// while the false boundaries the opening rule still declares are overwhelmingly WIDE joins,
        /// two markers meeting across open floor with dozens of candidate pairs (`diag openmiss`).
        /// </remarks>
        public float OpeningMaxWidth = 0f;

        /// <summary>
        /// Under <see cref="BarrierBoundaryTest"/> 3, also require a barrier pivot within
        /// <see cref="BarrierSearchRadius"/> of the kept crossing for a NAMED-to-named boundary.
        /// </summary>
        /// <remarks>
        /// Mode 1 asked this on its own and lost recall, because the crossing it kept was the
        /// shortest one anywhere - along the shared wall, metres from the door. Under mode 3 the
        /// kept crossing is the shortest one that SEES through, so the same question is asked of a
        /// point that is far likelier to be in the doorway. The false boundaries this targets are
        /// the open joins with no door at all - two markers meeting across floor with no barrier
        /// within 2-3 m (`diag openmiss`: 3 of CM11's 10, 4 of Tech_Hub's 18).
        /// </remarks>
        public bool OpeningRequiresBarrier = false;

        /// <summary>
        /// Under <see cref="BarrierBoundaryTest"/> 3 with <see cref="SealedNetworkLinking"/> on, a
        /// sealed network's crossing must also have a barrier pivot within
        /// <see cref="BarrierSearchRadius"/>. A door node has a door.
        /// </summary>
        /// <remarks>
        /// Retail's linked one-node sealed networks are the door_audio prefab's node, and its
        /// barrier sits about 0.9 m from the crossing. Once the opening test is made clearer - sound
        /// hulls skipped, three heights - sealed admission starts admitting pockets that merely see
        /// out (Tech_Hub 78 networks against retail's 72), and this is the test that says which of
        /// them are doors.
        /// </remarks>
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
        /// <remarks>
        /// The authored sound hulls play two parts. Across a doorway they wrongly block the run
        /// between two rooms that plainly adjoin, which is why the named-to-named test skips them.
        /// Around a sealed pocket they are the very thing that seals it: on BSP_TORRENS the two
        /// fake-door pockets retail leaves unlinked are closed by hulls, and sealed admission is
        /// exact there with the hulls kept (18 / 26 / 78) and one pocket over with them skipped
        /// (19 / 28 / 91). A door node sees out through a real opening; a pocket sees out only once
        /// its hull is taken away.
        /// </remarks>
        public bool SealedOpeningKeepsSoundHulls = true;

        /// <summary>
        /// Whether a sealed network - one no marker reached, so written with no name and no reverb -
        /// may hold a boundary. 0 never (default), 1 only when it holds at most
        /// <see cref="SealedLinkMaxNodes"/> nodes, 2 always, 3 only when a barrier sits at the
        /// crossing, 4 only when it is that small AND made entirely of AUTHORED nodes.
        /// </summary>
        /// <remarks>
        /// Retail is exact at one end: every one of the 100 one-node sealed networks across the 32
        /// shipped files holds a boundary, and the share falls away immediately above that - 37% at
        /// two nodes, 26% at three, 0% at six. A one-node sealed network is the lone door node in a
        /// doorway no marker claimed; larger ones are sealed pockets. The rule is about retail's
        /// sealed networks and ours are not the same population - we generate more of them - so
        /// admitting them by size alone adds boundaries retail does not have, and mode 3 barely
        /// filters anything. **On by default from 2 Sep 2026, but only under
        /// <see cref="BarrierBoundaryTest"/> 3 with <see cref="OpeningExemptsSealed"/> false**: a
        /// one-node sealed network is admitted when its crossing can see out through the opening,
        /// tested against the soup that keeps the sound hulls. That is what separates a door node
        /// from a hull-sealed pocket, and it is why BSP_TORRENS stays exact where every earlier
        /// admission rule broke it. With the opening test off this should go back to 0.
        /// </remarks>
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
        /// <remarks>
        /// The hulls are what the opening test already skips for named-to-named boundaries. Here
        /// the question is ownership: `diag netsurplus <level> soundB extra` finds whole chunks of
        /// our nodes forming sealed pockets that sit exactly on retail's NAMED networks - 19 nodes
        /// of SCI_HospitalLower's main corridor, 8 of Tech_RnD_HzdLab's circular corridor - which
        /// means retail's flood reached them through something ours could not see past.
        /// </remarks>
        public bool FloodSkipsSoundHulls = false;

        /// <summary>
        /// Drop every generated (non-authored) node that the flood assigns to a network whose
        /// marker is <c>room_size</c> Vent. Off by default while it is measured.
        /// </summary>
        /// <remarks>
        /// Retail's fill never enters a vent: `diag fillcensus` finds 0 fill nodes in every one of
        /// Tech_Hub's four and Tech_RnD_HzdLab's three Vent-class networks - their nodes are all
        /// hand-placed - where ours carries up to 31 fill nodes in one duct. Corridors get almost
        /// none either (Tech_Hub p50 1 fill node across ten), but that one is not a clean rule.
        /// </remarks>
        public bool NoFillInVents = false;

        /// <summary>
        /// Metres of head start a bigger room gets over a smaller one when their floods contest a
        /// node: a marker's seed cost is this times its class rank (large room 0, medium 1,
        /// corridor 2, small room 3, vent 4). 0 disables (default while measured).
        /// </summary>
        /// <remarks>
        /// On Tech_Hub the networks we over-fill are small ones beside big ones: "Vent Of Vent" 32
        /// nodes against retail's 3, "Server Corridor" 44 against 3, "This one is for Cain" 46
        /// against 7, and the extra nodes are mostly AUTHORED ones that retail gives to the room next
        /// door - Tech_Support - ServerRoom 220 against our 147, Transit - Platform 144 against 88.
        /// The nearest-marker flood lets the passage win what retail's room claims (`diag fillcensus`,
        /// `diag netsurplus ... nodes`).
        /// <para>**Measured end to end at 2 m and left OFF** (run `soundC` against `soundB`): the
        /// boundary set improves on every level that is not already exact (CM11 80 -> 81, HospitalLower
        /// 61 -> 62, Tech_Hub 80 -> 83, HzdLab 62 -> 76) and the harness does not move - dev four sound
        /// 91.7 -> 91.5 (CM9 -0.8, CM11 -0.4, HospitalLower +0.3, Tech_Hub +0.1), guards TORRENS and
        /// Solace unchanged, HzdLab +0.4. Larger values trade Tech_Hub for HzdLab, and a strict class
        /// hierarchy (50 m) starves the small rooms outright.</para>
        /// </remarks>
        public float ClassSeedBias = 0f;

        /// <summary>
        /// Fold a sealed pocket whose nodes stand on STANDING navmesh into the named network one of
        /// them can see through the hull-free soup - the flood's occluders with the door barriers
        /// and the authored sound hulls removed. Off by default while measured.
        /// </summary>
        /// <remarks>
        /// `diag netsurplus <level> soundB extra` finds whole chunks of room as sealed pockets sitting
        /// on retail's NAMED networks: 19 nodes of SCI_HospitalLower's Treatment - Main Corridor, 5 of
        /// its Reception Hub, 8 of Tech_RnD_HzdLab's circular corridor. They are passages, so
        /// <see cref="EnclosedPocketExtent"/> does not fold them, and they are sealed by the authored
        /// hulls: removing the hulls from the whole flood claims them (paths land exactly on both
        /// levels) but also lets named rooms run into each other. This does only the pocket half.
        /// The standing-floor test is what keeps a vent duct or a ladder shaft - crouch floor behind
        /// a grille - sealed the way retail keeps BSP_TORRENS' five.
        /// <para>**Measured end to end at a three-node floor and left OFF** (`soundD` against `soundB`):
        /// Tech_Hub lands on 72 networks against retail.s 72 and still loses 0.4, CM9 loses 0.4 (37 -> 36
        /// of 47), HospitalLower +0.1, Solace +0.6 (29 -> 27 of 33), TORRENS and HzdLab unchanged - mean
        /// -0.01 over seven levels. It also removes networks on levels already under retail.s count.
        /// Structurally right on the chunks it folds; the harness does not pay for it.</para>
        /// </remarks>
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
        /// <remarks>
        /// Off, because it does not generalise. It is exact on BSP_TORRENS - the two nodes it drops
        /// are precisely the two retail omits - but DLC/ChallengeMap12 has seven nodes that are both
        /// blind and floorless and retail KEEPS FOUR of them, giving them up to 50 links of their
        /// own. No refinement separates them: neither distance to navmesh nor solid collision
        /// beneath. Our "sees nothing" is partly a measure of our own occlusion rather than of the
        /// level, since retail links these nodes freely and records the obstruction.
        /// </remarks>
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
        /// <remarks>
        /// Off, because it does not work. The idea was that a vent duct runs away from you and the
        /// inside of a hiding cupboard does not, so free space would tell a container from a
        /// passage. BSP_TORRENS refutes it: its vents are floor STUBS, and `Vent_Floor_Filler`
        /// measures the same 1.9 m a cupboard does. It gets the wrong answer at every threshold.
        /// See rule 19 in the notes - the discriminator is prefab identity, not shape.
        /// </remarks>
        public float EnclosedPocketExtent = 0.0f;

        /// <summary>
        /// The most boundaries a SEALED network may hold. 0 lets it hold as many as
        /// <see cref="AdjoinDistance"/> finds, which is how the marker networks work.
        /// </summary>
        /// <remarks>
        /// A different rule from which sealed networks get linked at all
        /// (<see cref="SealedNetworkLinking"/>), and retail is emphatic: of the 100 one-node sealed
        /// networks across the 32 shipped files, 97 hold exactly one link. A door node hangs off the
        /// room it opens onto as a LEAF - not, as you would expect, a bridge between two rooms - and
        /// the paths it takes part in are the ones it inherits from the component it attaches to.
        /// Without the cap, every admitted sealed network takes every neighbour within AdjoinDistance
        /// and the boundary count overshoots the moment SealedNetworkLinking is turned on at all.
        /// </remarks>
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
