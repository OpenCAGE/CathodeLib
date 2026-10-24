#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.NavMesh
{
    public sealed class NavMeshBakeSettings
    {
        public float CellSize = 0.0625f;
        public float CellHeight = 0.0625f;
        public float WalkableClimb = 0.3125f;
        /* Seven cells, not eight. Retail keeps DeepCrouch polys under trolleys, sofas, workbenches and
         * server units that 0.5 m (8 cells) threw away; 0.4375 recovers them with almost no new area
         * elsewhere. Measured against the shipped navmesh, small frontstage holes: TECH_HUB 88 -> 72,
         * SCI_HUB 30 -> 13, ChallengeMap4 36 -> 29, ENG_Alien_Nest 32 -> 18, Solace 16 -> 12; anything
         * below 7 cells recovers nothing more and starts over-generating. */
        public float LowestNavigableHeight = 0.4375f;
        /// <summary>
        /// The <c>walkableHeight</c> written into the tile header, which is NOT the height the mesh was
        /// built at.
        /// </summary>
        public float HeaderWalkableHeight = 0.5f;
        public float DeepCrouchHeight = 0.875f;
        public float CrouchHeight = 1.625f;
        public float WalkableRadius = 0.3125f;
        /// <summary>
        /// Steepest surface Recast will call walkable.
        /// </summary>
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
        /// <summary>
        /// How many times to double <see cref="CellHeight"/> and retry when Recast cannot encode a
        /// column (more than 62 walkable spans stacked in one cell). Only reached by levels that
        /// would otherwise bake no navmesh at all; SCI_AndroidLab needs one retry.
        /// </summary>
        public int CellHeightRetries = 2;

        public int HeightLimitedAreaModeFilterPasses = 0;

        /// <summary>
        /// How far each height class grows outwards over the heightfield once the classes are
        /// marked, in cells. Only used with <see cref="HeightLimitedMarkedBeforePolygons"/>.
        /// </summary>
        public int HeightLimitedAreaSpread = 2;

        /// <summary>
        /// Cells of erosion applied to DEEP-CROUCH floor when the height classes are marked before
        /// the polygons; 0 keeps the walkable radius for every class. Standing and crouch floor is
        /// still eroded by the full walkable radius (5 cells at 0.3125 m); the difference is applied
        /// as extra erosion of non-deep spans after the classes are marked. Retail meshes a 0.3 m
        /// wide deep-crouch strip between a packaging box and a wall on ChallengeMap9 that our
        /// 5-cell erosion removes, and a third of that level hangs off it (`diag navislands`); retail's
        /// deep-crouch classes spread about 4 cells against a 5-cell radius, which is the hint.
        /// </summary>
        public int DeepCrouchErodeCells = 0;

        /// <summary>
        /// Crouch grows by <see cref="HeightLimitedAreaSpread"/> plus this; deep crouch grows by the
        /// spread alone, and wins any overlap.
        /// </summary>
        public int HeightLimitedAreaSpreadExtraForNonDeepCrouch = 1;
        public float HeightLimitedClearanceBias = 0.0f;

        /// <summary>
        /// Classify a polygon's height by the share of its own surface that is low, rather than by
        /// the single worst reading anywhere on it.
        /// </summary>
        public bool HeightLimitedInteriorSampling = true;

        /// <summary>Share of a polygon that must read deep-crouch for the polygon to be deep-crouch.</summary>
        public float HeightLimitedDeepShare = 0.15f;

        /// <summary>Share of a polygon that must read crouch or lower for the polygon to be crouch.</summary>
        public float HeightLimitedCrouchShare = 0.15f;

        /// <summary>
        /// Grow deep-crouch onto a neighbouring polygon that is partly low, in the spirit of the
        /// 4-cell spread retail's classes show. Zero disables it.
        /// </summary>
        public float HeightLimitedDeepNeighbourShare = 0f;

        /// <summary>How many times <see cref="HeightLimitedDeepNeighbourShare"/> may grow outwards.</summary>
        public int HeightLimitedDeepNeighbourPasses = 1;

        /// <summary>
        /// Fold the height class into the Recast area id BEFORE the regions are built, so the
        /// contour is cut along the class boundary and a crawl space becomes its own polygons.
        /// </summary>
        public bool HeightLimitedMarkedBeforePolygons = true;

        /// <summary>
        /// Minimum region area (m2) to use when <see cref="HeightLimitedMarkedBeforePolygons"/> is
        /// on. Negative keeps <see cref="MinRegionArea"/>.
        /// </summary>
        public float HeightLimitedRegionArea = -1f;

        public bool FilterUnreachable = true;

        /// <summary>
        /// Let the reachability flood cross off-mesh links (ladders, vents, drops) and backstage
        /// networks, not just polygon adjacency. Off by default in the flood's original form, which
        /// ran before those connections were stitched in and so culled any room retail reaches
        /// only by one of them.
        /// </summary>
        public bool SeedFloodFollowsOffMeshLinks = true;
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
        public bool SkipGhostedCollision = false;

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
        public bool SkipPlayerOnlyCollision = true;

        /// <summary>
        /// Skip CAMERA-typed collision when building the Recast soup - CollisionBarrier volumes
        /// placed to keep the camera out of geometry, which are not floor and not walls to an NPC.
        /// </summary>
        public bool SkipCameraCollision = false;

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
        // 0.375 culled real floor: retail keeps the strips this dropped on TECH_HUB (a stairwell landing,
        // the floor beside ramps and spacers). Measured against the shipped navmesh, 0.75 recovers them
        // with no new area anywhere - small frontstage holes TECH_HUB 72 -> 63, SCI_HUB 13 -> 10,
        // ChallengeMap4 29 -> 27, unchanged on ENG_Alien_Nest and Solace; the knee is at 0.625.
                public float ElevatedPolyStripAboveFloor = 0.75f;

        /// <summary>
        /// Vertical gap (metres) above which a poly with surface beneath it is treated as a
        /// separate storey and kept. Below this it is a shelf, crate lid or duct top.
        /// </summary>
        public float ElevatedPolyStoreySeparation = 1.9f;

        /// <summary>
        /// Run the elevated-strip cull at all. OFF: measured against the shipped navmesh on nine
        /// levels (2 Sep 2026, `diag navvariant`), the cull never bought precision and cost recall
        /// wherever a real floor sits 0.75-1.9 m above another surface - TECH_RND_HZDLAB's
        /// hazardous-lab rooms a metre above their duct floor went from 89.0% recall to 99.0% with
        /// precision unchanged (F1 93.0 -> 98.2), ChallengeMap11 97.4 -> 99.4, SCI_HospitalLower
        /// 98.1 -> 98.8, ChallengeMap9 96.0 -> 96.5, HAB_Airport 98.4 -> 98.6, SCI_Hub +0.1;
        /// Tech_Hub, BSP_Torrens and Solace did not move. The small-prop skip already keeps crate
        /// lids out of the soup, which is what this was for. Thresholds kept for re-enabling.
        /// </summary>
        public bool CullElevatedPolyStrips = false;

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

        /// <summary>
        /// Leave glass out of the soup. Off for the navmesh - a window still stops you walking
        /// through it - and ON for the cover bake.
        /// </summary>
        public bool SkipTransparentCollision = false;

        public static NavMeshBakeSettings CreateDefault() => new NavMeshBakeSettings();
    }
}
#endif