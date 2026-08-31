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
        /// Spacing between two generated nodes, as a multiple of network_node_min_spacing.
        /// </summary>
        /// <remarks>
        /// 1.00 - the authored spacing, used directly. The old 1.30 was fitted on BSP_TORRENS, which
        /// is one of only six levels with no backstage sheet, at a time when the scatter was also
        /// filling the alien's ceiling; the surplus those ceiling nodes created is what a wider
        /// spacing was compensating for. With the sheet excluded, node F1 over five levels reads
        /// 79.2% at 1.00 against 77.6% at 1.30, and the level that gains most is the one that had
        /// most ceiling - ENG_Alien_Nest, 64.9% to 73.6%, where 268 nodes against retail's 259 and a
        /// nearest-neighbour median of 1.58 m against 1.63 both land. BSP_TORRENS still prefers 1.30
        /// by 2.6 points and is the only level tested that does.
        /// </remarks>
        public float AutoSpacingScale = 1.00f;

        /// <summary>
        /// Which points on a navmesh polygon are offered to the fill, and in what order.
        /// </summary>
        /// <remarks>
        /// <para>4 (default) - as 0, but every polygon's centroid is appended after the rest, and
        /// the fill walks the pending list backwards, so the middle of a piece of floor is taken
        /// before its edges. Nothing else changes and it is better on every level measured: node F1
        /// +2.2 on BSP_TORRENS, +1.2 on Tech_RnD_HzdLab, +1.1 on ENG_Alien_Nest, +1.0 on
        /// DLC/SalvageMode2, +0.6 on DLC/ChallengeMap12, with the node count moving toward retail's
        /// every time and per-network count agreement never falling. Edge midpoints were only
        /// winning because of the order they happened to be appended in.</para>
        /// <para>0 - corners, centroid, edge midpoints and edge-to-centre midpoints, in that order.
        /// 1 - centroid only. 2 - centroid and corners. 3 - a uniform lattice over the polygon at
        /// <see cref="CandidateLatticeStep"/>.</para>
        /// <para>The fill takes every candidate that clears the spacing, so the candidate set sets
        /// the density. Two alternatives were measured and rejected. Centroids alone (1) is a big
        /// win exactly where we overshoot retail's node count (+4.1 on DLC/SalvageMode2) and a big
        /// loss where it is already right (-6.3 on ENG_Alien_Nest, -5.6 on DLC/ChallengeMap12) - a
        /// fit, not a rule. A uniform lattice (3) is worse than geometry-derived candidates at
        /// matched node counts on four levels of five, which is itself the finding: retail's node
        /// positions follow the navmesh, they are not a grid.</para>
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
        /// <para>Every one of retail's 26 network links on BSP_TORRENS is stored against a node pair
        /// 1.50 or 1.61 m apart - the spacing of the pair the door_audio prefab puts either side of
        /// a doorway - and the same 1.5 m spike dominates every level. But the tail matters: fitted
        /// at two metres this found only 38 of SCI_Hub's 62 boundaries, and because NetworkPaths are
        /// laid over the connected components of this graph, the missing dozen left 12 networks
        /// isolated and gave 92 paths against retail's 378.</para>
        /// <para>Four metres closes it: SCI_Hub comes out at 66 boundaries against 62 and 378 paths
        /// against 378 exactly, while BSP_TORRENS and ENG_Alien_Nest do not move at all. Solace is
        /// the one that overshoots, 86 against 56.</para>
        /// <para>**3.5 m, not 4.0** (29 Aug 2026). 4.0 was fitted when it gave SCI_Hub 66 boundaries
        /// against retail's 62; the node set has moved since and it now gives 76. Sweeping shows a
        /// clean cliff: 2.0 and 2.5 give 54 boundaries and only 276 paths, 3.0 gives 58 and 300, and
        /// 3.5 gives **64 boundaries with all 378 paths** - the last value that keeps the component
        /// structure whole while shedding the redundant chords. Nothing else moves: nodes, recall,
        /// node F1 and per-network count agreement are identical at every adjoin distance, because
        /// boundaries do not scale with node count. Worth about 1.9 points of the sound score.</para>
        /// </remarks>
        public float AdjoinDistance = 3.5f;

        /// <summary>
        /// How a candidate boundary must answer for a sound barrier. 0 not at all (default - the
        /// barrier is still looked up for the guid we write, and a miss tolerated), 1 a barrier
        /// pivot within <see cref="BarrierSearchRadius"/> of the crossing's midpoint, 2 the crossing
        /// must pass THROUGH barrier collision geometry, 3 the two networks must see each other
        /// with every barrier taken OUT of the occluders - they adjoin through an opening.
        /// </summary>
        /// <remarks>
        /// <para>A boundary IS a doorway. Retail leaves no room for doubt: across all 32 shipped
        /// files, **every one of its 3,050 network links carries a non-zero BarrierInstanceGuid** -
        /// 2,682 named-to-named and 368 touching a sealed network, with not one exception on any
        /// level (`diag barrshare`). <see cref="AdjoinDistance"/> alone cannot express that, because
        /// two rooms are near each other along every shared wall as well as through the door that
        /// joins them, so proximity declares boundaries that no door backs.</para>
        /// <para>Our barrier set is good enough to gate on: taking the node pair retail stored for
        /// each of its links and asking for the nearest barrier WE can see from the midpoint, one is
        /// within 2 m of 95-100% of them, median 0.90 to 1.08 m over seven levels
        /// (`diag barrcover`). What none of these modes has yet produced is a NET WIN, and the
        /// three failures are each worth keeping.</para>
        /// <para>**The instrument that made this measurable** is the boundary SET rather than the
        /// boundary count: pair our networks to retail's by position the way the harness does, then
        /// ask which of retail's adjoining pairs we also declare (`diag sounditer`, want/have/tp).
        /// A count cannot tell a redundant chord from a bridge. It says the shipped rule already has
        /// **87-91% recall of retail's boundaries at 42-59% precision** - we find nearly all the
        /// real ones and declare about 1.7x too many, so precision is the whole defect.</para>
        /// <para>**Mode 1 measured and rejected** (31 Aug 2026). Requiring a pivot within a radius
        /// buys precision only by losing more recall: on ChallengeMap11 the set goes from 87% / 57%
        /// to 76% / 62% at two metres, and on Tech_Hub 91% / 59% to 68% / 70%. F1 never beats the
        /// shipped rule at any radius from 0.75 to 3.0 m. At the shipped 4.0 m the gate is inert -
        /// with 76 to 194 barriers per level, everything finds one.</para>
        /// <para>**Mode 2 refuted by retail's own file.** Only **39%** of ChallengeMap11's 132
        /// stored crossings pass through any barrier's collision geometry, and 39% again when the
        /// segment is extended a metre past each end. The barrier sits about 0.90 m to one SIDE of
        /// the midpoint of a pair 1.50 m apart. A barrier is a LABEL retail attaches to a boundary,
        /// not a solid the boundary pierces (`diag barrcross`).</para>
        /// <para>**Mode 3 is the correct rule and still does not win on the harness.** Two rooms
        /// adjoin when you can get from one to the other, and what stands between them at a doorway
        /// is the door - so the test is line of sight with the barriers subtracted from the
        /// occluders. Plain line of sight was tried early and found no boundaries at all on
        /// BSP_TORRENS, but that was measured against the full soup in which the closed door leaf is
        /// itself an occluder: it was asking whether the rooms are joined by a HOLE. Over 14 levels
        /// the boundary set improves on ten, is unchanged on four and regresses on none - Tech_Hub
        /// 72 to 83, TECH_MuthrCore 71 to 90, HAB_CorporatePent 79 to 93, ChallengeMap11 69 to 80 -
        /// while BSP_TORRENS stays exact at 100% and SCI_Hub at 92%. But the harness scores count
        /// RATIOS, and our remaining missing boundaries fragment the graph where the false ones we
        /// ship today were holding it together: NetworkPaths fall, and the dev four go from an
        /// overall 78.5 to 78.2. **Closing the recall gap is what would make this shippable**, and
        /// it is worth roughly 0.25 of the sound score in the links and paths terms alone.</para>
        /// <para>Under mode 3 the run is tried at <c>SoundNodeNetworkGenerator.OpeningHeights</c>
        /// above each node, because a node sits on the navmesh and a single floor-level run is
        /// stopped by any threshold or door sill. It is worth a lot of connectivity - TECH_MuthrCore
        /// goes from 302 paths to 596 against retail's 703, and its links from 80 to 90 against
        /// retail's 88 - for a few points of precision.</para>
        /// <para>**Do not pair mode 3 with <see cref="SealedNetworkLinking"/> 1 to recover the
        /// difference.** It is the only combination that beats the shipped score (78.6 against
        /// 78.5, sound 89.0 against 88.6) and it buys that by breaking the one level we reproduce
        /// exactly: BSP_TORRENS goes from 18 networks, 26 links and 78 paths - retail's numbers to
        /// the digit - to 20, 30 and 105, and SCI_Hub from 92% to 89%. Retail gives all five of
        /// TORRENS' sealed networks no link and no path deliberately. Sealed mode 3 does not
        /// separate them either: it is worse again on TORRENS, 36 links against retail's 26.</para>
        /// </remarks>
        public int BarrierBoundaryTest = 0;

        /// <summary>
        /// How far from a boundary a barrier may sit and still count as the barrier for it.
        /// </summary>
        /// <remarks>
        /// <para>4.0 m was fine while this only chose which guid to write, but it is far too loose
        /// to gate on: our levels carry 76 to 194 barriers each, so at four metres almost any two
        /// rooms that pass near each other find one and <see cref="RequireBarrierForBoundary"/>
        /// rejects nothing at all - measured on ChallengeMap11, the gate left all 140 links
        /// standing.</para>
        /// <para>Retail's own boundaries are much tighter than that. Taking the node pair it stored
        /// for each of its 3,050 links and measuring to the nearest barrier we can see, the median
        /// is 0.90 to 1.08 m and the 90th percentile 1.08 to 1.79 m across seven levels
        /// (`diag barrcover`), with 95-100% inside two metres. So the doorway a boundary sits in is
        /// within about a metre of it, and the barriers two to four metres away belong to some
        /// other door.</para>
        /// </remarks>
        public float BarrierSearchRadius = 4.0f;

        /// <summary>
        /// Whether a sealed network - one no marker reached, so written with no name and no reverb -
        /// may hold a boundary. 0 never (default), 1 only when it holds at most
        /// <see cref="SealedLinkMaxNodes"/> nodes, 2 always, 3 only when a barrier sits at the
        /// crossing, 4 only when it is that small AND made entirely of AUTHORED nodes.
        /// </summary>
        /// <remarks>
        /// <para>Retail is not uniform about this and the pattern is exact at one end: across all 32
        /// shipped files, **every one of the 100 one-node sealed networks holds a boundary**, and
        /// the share falls away immediately above that - 37% at two nodes, 26% at three, 0% at six.
        /// A one-node sealed network is the lone door node in a doorway that no marker claimed;
        /// larger ones are sealed pockets. That is why BSP_TORRENS gives all five of its sealed
        /// networks no link and no path while DLC/ChallengeMap12 links ten of its eleven.
        /// `diag sealedlinks` prints the table.</para>
        /// <para>**It is still off by default, because the rule is about retail's sealed networks
        /// and ours are not the same population.** We generate more of them (BSP_TORRENS 9 against
        /// retail's 5, DLC/SalvageMode2 7 against 4), so admitting them adds boundaries retail does
        /// not have. Measured over five levels, paths against retail: mode 0 gives 210/438, 78/78,
        /// 780/781, 55/66, 254/276 - total absolute error 262. Mode 1 gives 351/438, 105/78,
        /// 946/781, 66/66 and 326/276 - error 329. It halves ChallengeMap12's deficit and lands
        /// ENG_Alien_Nest exactly, and pays for it by breaking SalvageMode2, which mode 0 gets to
        /// within one path. Turning this on is worth revisiting once our sealed-network count
        /// matches retail's; that, not the linking rule, is the defect.</para>
        /// <para>Mode 3 was tried on the theory that the door is the qualification: it barely
        /// filters anything (ChallengeMap12 comes out identical to mode 2) and is worse than 1.</para>
        /// </remarks>
        public int SealedNetworkLinking = 0;

        /// <summary>
        /// How a marker claims its first nodes. 0 - the nearest node it can see, seeded at cost zero.
        /// 1 - every node it can see, each seeded at its straight-line distance.
        /// </summary>
        public int MarkerSeedMode = 0;

        /// <summary>
        /// Leave the PATH_CLOSED collider of a door out of the sound occluder soup, so a doorway
        /// does not seal a node off from the room it stands in. The radiosity bake already treats
        /// doors as open for the same reason.
        /// </summary>
        public bool SkipDoorBarriers = false;

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
        /// **OFF, because it does not generalise.** It is exact on BSP_TORRENS - the two nodes it
        /// drops are precisely the two retail omits, taking that level to 18 networks against
        /// retail's 18 - and it moves DLC/SalvageMode2 one closer. But on DLC/ChallengeMap12 seven
        /// nodes are both blind and floorless and retail KEEPS FOUR of them, giving them 8, 23 and
        /// even 50 links of its own; the level goes from 32 networks to 26 against retail's 34.
        /// ENG_Alien_Nest loses its exact 13. Every refinement tried fails too: distance to navmesh
        /// does not separate them (ChallengeMap12 drops one at 1.28 m and keeps one at 1.90 m), nor
        /// does solid collision beneath (it keeps two with nothing below and drops two with floor at
        /// 0.43 m). Our "sees nothing" is partly a measure of our own occlusion rather than of the
        /// level, since retail links these nodes freely and records the obstruction.
        /// </remarks>
        public bool DiscardOrphanNodes = false;

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
        /// **Off, because it does not work.** The idea was that a vent duct or ladder shaft runs
        /// away from you and the inside of a hiding cupboard does not, so free space would tell a
        /// container from a passage. On BSP_TORRENS it does not: its vents are floor STUBS, and
        /// `Vent_Floor_Filler` measures 1.9 m of free space - exactly what a hiding cupboard
        /// measures. At 2.0 m the rule folds five pockets, three of them vents retail keeps sealed;
        /// at 1.5 m it folds one, and that one is a vent as well, while both cupboards stay out.
        /// It gets the wrong answer at every threshold. See rule 19 in the notes: the discriminator
        /// is prefab identity, not shape.
        /// </remarks>
        public float EnclosedPocketExtent = 0.0f;

        /// <summary>
        /// The most boundaries a SEALED network may hold. 0 lets it hold as many as
        /// <see cref="AdjoinDistance"/> finds, which is how the marker networks work.
        /// </summary>
        /// <remarks>
        /// Retail is emphatic about this, and it is a different rule from which sealed networks get
        /// linked at all (<see cref="SealedNetworkLinking"/>). Of the 100 one-node sealed networks
        /// across all 32 shipped files, **97 hold exactly one link**; two hold two and one holds
        /// three (`diag sealedlinks` prints the degree table). A door node hangs off the room it
        /// opens onto as a LEAF - it is not a bridge between two rooms, which is what you would
        /// expect it to be - and the paths it takes part in are the ones it inherits from the
        /// component it is attached to.
        /// <para>Without the cap, admitting sealed networks hands each one every neighbour within
        /// AdjoinDistance, and that is what makes the boundary count overshoot the moment
        /// <see cref="SealedNetworkLinking"/> is turned on at all: on ChallengeMap9 boundaries go
        /// 26 -> 40 against retail's 28, and on ChallengeMap11 70 -> 86 against retail's 66.</para>
        /// </remarks>
        public int SealedMaxBoundaries = 1;

        /// <summary>Largest sealed network that may hold a boundary under SealedNetworkLinking 1.</summary>
        public int SealedLinkMaxNodes = 1;
    }
}
#endif
