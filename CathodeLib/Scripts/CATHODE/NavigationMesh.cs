using System;
using System.IO;
using System.Runtime.InteropServices;
using CathodeLib;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.EXPERIMENTAL
{
    /* CATHODE uses a slightly modified version of Detour - this handler is heavily WIP! */
    public class NavigationMesh : CathodeFile
    {
        dtMeshHeader Header;

        public Vector3[] Vertices;
        public dtPoly[] Polygons;
        public dtLink[] Links;
        public dtPolyDetail[] DetailMeshes;
        public Vector3[] DetailVertices;
        public byte[] DetailIndices;
        public dtBVNode[] BoundingVolumeTree;
        public dtOffMeshConnection[] OffMeshConnections;

        public static new Impl Implementation = Impl.LOAD;
        public NavigationMesh(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                Header = Utilities.Consume<dtMeshHeader>(reader);
                Vertices = Utilities.ConsumeArray<Vector3>(reader, Header.vertCount);
                Polygons = Utilities.ConsumeArray<dtPoly>(reader, Header.polyCount);
                Links = Utilities.ConsumeArray<dtLink>(reader, Header.maxLinkCount);
                DetailMeshes = Utilities.ConsumeArray<dtPolyDetail>(reader, Header.detailMeshCount);
                DetailVertices = Utilities.ConsumeArray<Vector3>(reader, Header.vertCount);
                DetailIndices = Utilities.ConsumeArray<byte>(reader, Header.detailTriCount * 4);
                BoundingVolumeTree = Utilities.ConsumeArray<dtBVNode>(reader, Header.bvNodeCount);
                OffMeshConnections = Utilities.ConsumeArray<dtOffMeshConnection>(reader, Header.offMeshConCount);
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct dtMeshHeader
        {
            public int ayz_Version;
            public int ayz_FileSize;

            /// Tile magic number. (Used to identify the data format.)
            public fourcc FourCC;
            /// Tile data format version number.
            public int version;
            /// The x-position of the tile within the dtNavMesh tile grid. (x, y, layer)
            public int x;
            /// The y-position of the tile within the dtNavMesh tile grid. (x, y, layer)
            public int y;
            /// The layer of the tile within the dtNavMesh tile grid. (x, y, layer)
            public int layer;
            /// The user defined id of the tile.
            public int userId;
            /// The number of polygons in the tile.
            public int polyCount;
            /// The number of vertices in the tile.
            public int vertCount;
            /// The number of allocated links.
            public int maxLinkCount;
            /// The number of sub-meshes in the detail mesh.
            public int detailMeshCount;
            /// The number of unique vertices in the detail mesh. (In addition to the polygon vertices.)
            public int detailVertCount;
            /// The number of triangles in the detail mesh.
            public int detailTriCount;
            /// The number of bounding volume nodes. (Zero if bounding volumes are disabled.)
            public int bvNodeCount;
            /// The number of off-mesh connections.
            public int offMeshConCount;
            /// The index of the first polygon which is an off-mesh connection.
            public int offMeshBase;
            /// The height of the agents using the tile.
            public float walkableHeight;
            /// The radius of the agents using the tile.
            public float walkableRadius;
            /// The maximum climb height of the agents using the tile.
            public float walkableClimb;        
            /// The minimum bounds of the tile's AABB. [(x, y, z)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] bMin;
            /// The maximum bounds of the tile's AABB. [(x, y, z)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] bMax;
            /// The bounding volume quantization factor. 
            public float bvQuantFactor;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct dtPoly
        {
            /// Index to first link in linked list. (Or #DT_NULL_LINK if there is no link.)
            public int firstLink;
            /// The indices of the polygon's vertices.
            /// The actual vertices are located in dtMeshTile::verts.
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public UInt16[] verts;
            /// Packed data representing neighbor polygons references and flags for each edge.
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public UInt16[] neis;

            // TODO: Stuff below this point is uncertain. This is not standard detour stuff.
            public int VertexCount;
            public UInt16 Flags;
            public byte Type;// : 6;
            public byte Area;// : 2;
            public byte PaddingZero_;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct dtLink
        {
            /// Neighbour reference. (The neighbor that is linked to.)
            public int polygonRef;
            /// Index of the next link.
            public int next;
            /// Index of the polygon edge that owns this link.
            public char edge;
            /// If a boundary link, defines on which side the link is.
            public char side;
            /// If a boundary link, defines the minimum sub-edge area.
            public char bmin;
            /// If a boundary link, defines the maximum sub-edge area.
            public char bmax;				
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct dtPolyDetail
        {
            /// The offset of the vertices in the dtMeshTile::detailVerts array.
            public int vertBase;
            /// The offset of the triangles in the dtMeshTile::detailTris array.
            public int triBase;
            /// The number of vertices in the sub-mesh.
            public char vertCount;
            /// The number of triangles in the sub-mesh.
            public char triCount;			
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public byte[] _padding; // this isn't in base dt!
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct dtBVNode
        {
            /// Minimum bounds of the node's AABB. [(x, y, z)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public short[] bmin;
            /// Maximum bounds of the node's AABB. [(x, y, z)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public short[] bmax;
            /// The node's index. (Negative for escape sequence.)
            public int i;							
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct dtOffMeshConnection
        {
            /// The endpoints of the connection. [(ax, ay, az, bx, by, bz)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public float[] pos;
            /// The radius of the endpoints. [Limit: >= 0]
            public float rad;
            /// The polygon reference of the connection within the tile.
            public short poly;
            /// Link flags. 
            /// @note These are not the connection's user defined flags. Those are assigned via the 
            /// connection's dtPoly definition. These are link flags used for internal purposes.
            public char flags;
            /// End point side.
            public char side;
            /// The id of the offmesh connection. (User assigned when the navigation mesh is built.)
            public int userId;
        };
        #endregion
    }
}
