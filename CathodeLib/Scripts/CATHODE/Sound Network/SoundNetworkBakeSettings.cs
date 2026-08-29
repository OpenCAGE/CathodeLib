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
        /// </remarks>
        public float AdjoinDistance = 4.0f;

        /// <summary>
        /// Whether a sealed network - one no marker reached, so written with no name and no reverb -
        /// may hold a boundary. 0 never (default), 1 only when it holds at most
        /// <see cref="SealedLinkMaxNodes"/> nodes, 2 always, 3 only when a barrier sits at the
        /// crossing.
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

        /// <summary>Largest sealed network that may hold a boundary under SealedNetworkLinking 1.</summary>
        public int SealedLinkMaxNodes = 1;
    }
}
#endif
