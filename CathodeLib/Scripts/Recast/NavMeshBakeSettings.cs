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
        public float WalkableSlopeAngle = 40.0f;
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
        /// Half-height (metres) around the primary floor component median Y used by
        /// <see cref="CullUnseededIslands"/>.
        /// </summary>
        public float IslandFloorYBand = 0.75f;

        /// <summary>
        /// Skip COLLISION.MAP rows flagged GHOSTED / PRE_GHOSTED when building the Recast soup.
        /// </summary>
        public bool SkipGhostedCollision = true;

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
        /// Drop polys inside the kept floor set whose centroid Y exceeds
        /// primaryMedianY + this value (metres). Defaults to WalkableClimb + CellHeight.
        /// </summary>
        public float ElevatedPolyStripAboveFloor = 0.375f;

        public static NavMeshBakeSettings CreateDefault() => new NavMeshBakeSettings();
    }
}
#endif