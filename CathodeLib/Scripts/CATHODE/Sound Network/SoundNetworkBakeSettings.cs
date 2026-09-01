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
        /// Every one of retail's network links is stored against a node pair about 1.5 m apart - the
        /// spacing of the pair the door_audio prefab puts either side of a doorway - but the tail
        /// matters, because NetworkPaths are laid over the connected components of this graph and a
        /// missed boundary strands a whole network. Sweeping shows a clean cliff on SCI_Hub: 2.0 and
        /// 2.5 give 54 boundaries and 276 paths, 3.0 gives 58 and 300, and 3.5 gives 64 boundaries
        /// with all 378 paths - the last value that keeps the component structure whole while
        /// shedding redundant chords. Nothing else moves with it; boundaries do not scale with node
        /// count.
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
        /// <para>A boundary IS a doorway: across all 32 shipped files every one of retail's 3,050
        /// network links carries a non-zero BarrierInstanceGuid. <see cref="AdjoinDistance"/> alone
        /// cannot express that, because two rooms are near each other along every shared wall as
        /// well as through the door that joins them. Measured as a boundary SET rather than a count
        /// (pair our networks to retail's by position, then ask which of retail's adjoining pairs we
        /// also declare), the shipped rule has 87-91% recall at 42-59% precision - we find nearly
        /// all the real ones and declare about 1.7x too many, so precision is the whole defect.</para>
        /// <para>None of the modes has yet produced a net win. Mode 1 buys precision only by losing
        /// more recall, at every radius from 0.75 to 3.0 m. Mode 2 is refuted by retail's own file:
        /// only 39% of stored crossings pass through any barrier, because the barrier sits about
        /// 0.90 m to one SIDE of the midpoint - it is a LABEL retail attaches to a boundary, not a
        /// solid the boundary pierces. Mode 3 is the correct rule and improves the boundary set on
        /// ten levels of fourteen while regressing on none, but the harness scores count RATIOS, and
        /// the boundaries it still misses fragment the graph where today's false ones hold it
        /// together. Closing the recall gap is what would make it shippable.</para>
        /// <para>Under mode 3 the run is tried at <c>SoundNodeNetworkGenerator.OpeningHeights</c>
        /// above each node, because a node sits on the navmesh and a single floor-level run is
        /// stopped by any threshold or door sill.</para>
        /// <para>Do not pair mode 3 with <see cref="SealedNetworkLinking"/> 1 to recover the
        /// difference. It is the only combination that beats the shipped score, and it buys that by
        /// breaking the one level we reproduce exactly - BSP_TORRENS goes from retail's 18 networks,
        /// 26 links and 78 paths to 20, 30 and 105.</para>
        /// </remarks>
        public int BarrierBoundaryTest = 0;

        /// <summary>
        /// How far from a boundary a barrier may sit and still count as the barrier for it. Fine at
        /// 4.0 m while it only chooses which guid to write, but far too loose to gate on: our levels
        /// carry 76 to 194 barriers each, so almost any two rooms that pass near each other find
        /// one. Retail's own boundaries sit within about a metre of their barrier (median 0.90 to
        /// 1.08 m over seven levels), so anything further away belongs to some other door.
        /// </summary>
        public float BarrierSearchRadius = 4.0f;

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
        /// doorway no marker claimed; larger ones are sealed pockets. Still off by default, because
        /// the rule is about retail's sealed networks and ours are not the same population - we
        /// generate more of them, so admitting them adds boundaries retail does not have. Worth
        /// revisiting once our sealed-network count matches retail's; that, not the linking rule, is
        /// the defect. Mode 3 barely filters anything and is worse than 1.
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
        /// Off, because it does not generalise. It is exact on BSP_TORRENS - the two nodes it drops
        /// are precisely the two retail omits - but DLC/ChallengeMap12 has seven nodes that are both
        /// blind and floorless and retail KEEPS FOUR of them, giving them up to 50 links of their
        /// own. No refinement separates them: neither distance to navmesh nor solid collision
        /// beneath. Our "sees nothing" is partly a measure of our own occlusion rather than of the
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
        public int SealedLinkMaxNodes = 1;
    }
}
#endif
