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

        public float ClassifyCoverHeight(float obstacleHeightAboveFloor)
        {
            return obstacleHeightAboveFloor < LowHighDividingLine ? LowHeight : StandingHeight;
        }
    }
}
#endif
