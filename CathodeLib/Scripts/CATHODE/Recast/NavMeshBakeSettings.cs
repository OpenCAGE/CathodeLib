#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.NavMesh
{
    public sealed class NavMeshBakeSettings
    {
        public float CellSize = 0.0625f;
        public float CellHeight = 0.0625f;
        public float WalkableClimb = 0.3125f;
        public float LowestNavigableHeight = 0.5f;
        public float DeepCrouchHeight = 0.875f;
        public float CrouchHeight = 1.625f;
        public float WalkableRadius = 0.3125f;
        /// <summary>
        /// Steepest surface Recast will call walkable.
        /// </summary>
        /// <remarks>
        /// 38, not the 40 this used to be. ENG_TowPlatform's exterior hull plating sits at about
        /// 38.2 degrees: at 40 Recast covers it and the level bakes 19,198 m2 of navmesh against
        /// retail's 9,413, at 38 it bakes 9,058 and the match goes from 63.1% to 97.7%. The cliff is
        /// between 38.0 and 38.5 and nothing else in the campaign is near it - swept over all 32
        /// levels, 38 costs BSP_LV426_Pt02 1.1 points and BSP_Torrens 0.3, and moves no other level
        /// by more than 0.2.
        /// </remarks>
        public float WalkableSlopeAngle = 38.0f;
        public float MaxContourError = 1.3f;
        public float MaxEdgeLength = 10.0f;
        public int MaxVertsInPolyMeshTriangle = 6;
        public float DetailSampleDist = 0.25f;
        public float MaxDetailError = 0.25f;
        public float MinRegionArea = 0.25f;
        public float MergeRegionArea = 16.0f;
        public float RecastMaxBoundsSize = 1024.0f;
        public float RecastMaxBoundsSizeY => (1 << 13) * CellHeight;
        public int HeightLimitedAreaModeFilterPasses = 0;
        public int HeightLimitedAreaSpread = 4;
        public int HeightLimitedAreaSpreadExtraForNonDeepCrouch = 1;
        public float HeightLimitedClearanceBias = 0.0f;
        public bool FilterUnreachable = true;
        public float ReachabilitySeedHeightToleranceAbove = 0.1875f;
        public float ReachabilitySeedHeightToleranceBelow = 0.3125f;

        /// <summary>
        /// When true and no reachability seeds exist, drop disconnected Recast islands whose
        /// median poly height is outside <see cref="IslandFloorYBand"/> of the largest component.
        /// Removes ceiling-beam / duct-top scrap that Recast marks walkable.
        /// </summary>
        public bool CullUnseededIslands = true;

        /// <summary>
        /// The seed flood is only trusted if it reaches at least this many polys, or this fraction
        /// of the mesh, whichever is larger. Below that the level is assumed to be seeded for a
        /// sub-region only (ExclusiveMaster levels do this) and the filter is skipped. Set both to
        /// zero to always trust the seeds.
        /// </summary>
        public int SeedFilterMinKeepPolys = 50;

        /// <summary>See <see cref="SeedFilterMinKeepPolys"/>.</summary>
        public float SeedFilterMinKeepFraction = 0.1f;

        /// <summary>
        /// Half-height (metres) around the primary floor component median Y used by
        /// <see cref="CullUnseededIslands"/>.
        /// </summary>
        public float IslandFloorYBand = 0.75f;

        /// <summary>
        /// Skip COLLISION.MAP rows flagged GHOSTED / PRE_GHOSTED when building the Recast soup.
        /// </summary>
        public bool SkipGhostedCollision = true;

        /// <summary>
        /// Skip the collision instances of PATH_CLOSED NavMeshBarriers, which are carved as area ids
        /// instead. Diagnostic switch: turning it off shows whether the barrier-to-instance
        /// resolution is taking real floor with it.
        /// </summary>
        public bool SkipBarrierCollision = true;

        /// <summary>
        /// Skip collision instances typed SOUND as well as SOUND_BARRIER. Both exist only for sound
        /// node network occlusion and neither is navigation geometry, so both stay out of the soup.
        /// Including SOUND was measured and is wrong: it costs Solace 2.2 points of navmesh because
        /// those instances are room-shaped occlusion hulls that carve real floor away (786 polys down
        /// to 721).
        /// </summary>
        public bool SkipSoundFlaggedCollision = true;

        /// <summary>
        /// Skip PLAYER_ONLY collision when building the Recast soup - the invisible surfaces that
        /// exist to keep Ripley somewhere, which no AI ever touches.
        /// </summary>
        public bool SkipPlayerOnlyCollision = false;

        /// <summary>
        /// Skip small bake-host <c>hkpBoxShape</c> colliders (crate-scale props) from the Recast
        /// soup so their tops are not walkable and their solids do not carve floor holes.
        /// Mesh / compound floors are never skipped by size (tiling would underfill).
        /// </summary>
        public bool SkipSmallPropCollision = true;

        /// <summary>Max horizontal (XZ) full extent (metres) for a box to count as a small prop.</summary>
        public float SmallPropMaxXZExtent = 0.85f;

        /// <summary>Max vertical full extent (metres) for a box to count as a small prop.</summary>
        public float SmallPropMaxYExtent = 1.25f;

        /// <summary>
        /// Drop soup tris whose longest edge exceeds this (metres). Catches rare BvCompressed
        /// / domain decode outliers (e.g. 10 km floor quads) that blow Recast bounds.
        /// Real level pieces are far smaller; keep high enough for long walls.
        /// </summary>
        public bool CullAbsurdSoupTris = true;

        /// <summary>See <see cref="CullAbsurdSoupTris"/>.</summary>
        public float MaxAbsurdSoupEdge = 256.0f;

        /// <summary>
        /// Drop non-primary island components smaller than
        /// <c>max(IslandMinSecondaryPolys, primaryCount * IslandMinSecondaryFraction)</c>
        /// even when they share the floor Y band. This is the absolute floor for a secondary keep
        /// (true 1-2 poly speckles); real rooms are larger.
        /// </summary>
        public int IslandMinSecondaryPolys = 3;

        /// <summary>See <see cref="IslandMinSecondaryPolys"/>.</summary>
        public float IslandMinSecondaryFraction = 0.005f;

        /// <summary>
        /// Minimum height (metres) a kept poly must stand above walkable surface directly
        /// beneath it before it counts as a prop / duct top rather than floor.
        /// Defaults to WalkableClimb + CellHeight, so anything a character could simply step
        /// onto stays part of the floor.
        /// </summary>
        public float ElevatedPolyStripAboveFloor = 0.375f;

        /// <summary>
        /// Vertical gap (metres) above which a poly with surface beneath it is treated as a
        /// separate storey and kept. Below this it is a shelf, crate lid or duct top.
        /// </summary>
        /// <remarks>
        /// This is what stops the strip decapitating multi-storey levels: SCI_Hub's upper deck
        /// sits ~8 m over the main floor, so it is never mistaken for a prop.
        /// </remarks>
        public float ElevatedPolyStoreySeparation = 1.9f;

        /// <summary>
        /// Clearance (metres) two barriers need between them before they may share a Recast area
        /// id. Recast only has 62 usable ids and a level can carry more barriers than that, so
        /// distant ones reuse a value; they must be far enough apart never to land in one region.
        /// </summary>
        public float BarrierAreaIdSeparation = 4.0f;

        /// <summary>
        /// Edge length (metres) above which backstage triangulation edges are recursively
        /// halved. Retail's subdivision points all sit on halving fractions with segments
        /// stopping once at or below 15 m (SCI_Hub's longest surviving segment is 14.65 m).
        /// </summary>
        public float BackstageMaxEdgeLength = 15.0f;

        /// <summary>
        /// Half-width (metres) of the fallback strip built when every backstage node sits on
        /// one line and a Delaunay sheet cannot exist.
        /// </summary>
        public float BackstageColinearStripHalfWidth = 0.5f;

        /// <summary>
        /// Height (metres) of the backstage sheet above each node. Retail hardcodes 6 m: every
        /// one of the 275 shipped Backstage connections rises exactly 6.0, regardless of the
        /// vent composite's own length (the 9 m marker and the 4.25 m hospital vents included) -
        /// the node's TopMarker entity is ignored by the generator.
        /// </summary>
        public float BackstageNodeHeight = 6.0f;

        public static NavMeshBakeSettings CreateDefault() => new NavMeshBakeSettings();
    }
}
#endif