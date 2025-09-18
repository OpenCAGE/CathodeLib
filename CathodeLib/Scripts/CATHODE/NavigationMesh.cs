using System;
using System.IO;
using System.Runtime.InteropServices;
using CathodeLib;
using CATHODE.Scripting;
using CATHODE.Enums;


#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/PRODUCTION/x/WORLD/STATE_x/NAV_MESH
    /// </summary>
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

        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE | Implementation.SAVE;

        public NavigationMesh(string path) : base(path) { }
        public NavigationMesh(MemoryStream stream, string path = "") : base(stream, path) { }
        public NavigationMesh(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 8;

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

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(59);
                writer.Write(0); 
                
                Utilities.Write(writer, Header);

                Utilities.Write(writer, Vertices);
                Utilities.Write(writer, Polygons);
                Utilities.Write(writer, Links);
                Utilities.Write(writer, DetailMeshes);
                Utilities.Write(writer, DetailVertices);
                Utilities.Write(writer, DetailIndices);
                Utilities.Write(writer, BoundingVolumeTree);
                Utilities.Write(writer, OffMeshConnections);
                
                writer.BaseStream.Position = 4;
                writer.Write((int)writer.BaseStream.Length - 8);
                
                return true;
            }
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct dtMeshHeader
        {
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
            /// The number of vertices in the polygon.
            public byte vertCount;
            /// The bit packed type/id/flags.
            public dt_area_t area;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct dt_area_t
        {
            private uint _value;

            private const uint DT_AREA_TYPE_BITS = 1;
            private const uint DT_AREA_ID_BITS = 9;
            private const uint DT_AREA_FLAG_CHARACTER_ADMITTANCE_BITS = 5;
            private const uint DT_AREA_FLAG_LINK_TYPE_BITS = 4;
            private const uint DT_AREA_FLAG_HEIGHT_BITS = 2;
            private const uint DT_AREA_FLAG_MARKUP_BITS = 3;

            private const uint TYPE_MASK = (1u << (int)DT_AREA_TYPE_BITS) - 1;
            private const uint ID_MASK = (1u << (int)DT_AREA_ID_BITS) - 1;
            private const uint ADMITTANCE_FLAGS_MASK = (1u << (int)DT_AREA_FLAG_CHARACTER_ADMITTANCE_BITS) - 1;
            private const uint LINK_TYPE_MASK = (1u << (int)DT_AREA_FLAG_LINK_TYPE_BITS) - 1;
            private const uint HEIGHT_LIMITED_AMOUNT_MASK = (1u << (int)DT_AREA_FLAG_HEIGHT_BITS) - 1;
            private const uint MARKUP_FLAGS_MASK = (1u << (int)DT_AREA_FLAG_MARKUP_BITS) - 1;

            private const uint TYPE_SHIFT = 0;
            private const uint ID_SHIFT = DT_AREA_TYPE_BITS;
            private const uint ENABLED_SHIFT = DT_AREA_TYPE_BITS + DT_AREA_ID_BITS;
            private const uint ADMITTANCE_FLAGS_SHIFT = ENABLED_SHIFT + 1;
            private const uint LINK_TYPE_SHIFT = ADMITTANCE_FLAGS_SHIFT + DT_AREA_FLAG_CHARACTER_ADMITTANCE_BITS;
            private const uint HEIGHT_LIMITED_AMOUNT_SHIFT = LINK_TYPE_SHIFT + DT_AREA_FLAG_LINK_TYPE_BITS;
            private const uint MARKUP_FLAGS_SHIFT = HEIGHT_LIMITED_AMOUNT_SHIFT + DT_AREA_FLAG_HEIGHT_BITS;

            public dt_area_t(bool enabled_)
            {
                _value = 0;
                SetIsEnabled(enabled_);
            }

            public dtPolyTypes GetPolyType()
            {
                return (dtPolyTypes)((_value >> (int)TYPE_SHIFT) & TYPE_MASK);
            }
            public void SetPolyType(dtPolyTypes type_)
            {
                _value = (_value & ~(TYPE_MASK << (int)TYPE_SHIFT)) | ((uint)type_ << (int)TYPE_SHIFT);
            }

            public ushort GetId()
            {
                return (ushort)((_value >> (int)ID_SHIFT) & ID_MASK);
            }
            public void SetId(ushort id_)
            {
                _value = (_value & ~(ID_MASK << (int)ID_SHIFT)) | ((uint)id_ << (int)ID_SHIFT);
            }

            public bool GetIsEnabled()
            {
                return ((_value >> (int)ENABLED_SHIFT) & 1) != 0;
            }
            public void SetIsEnabled(bool enabled_)
            {
                if (enabled_)
                    _value |= (1u << (int)ENABLED_SHIFT);
                else
                    _value &= ~(1u << (int)ENABLED_SHIFT);
            }

            public NAVIGATION_CHARACTER_CLASS_COMBINATION GetAdmittanceFlags()
            {
                return (NAVIGATION_CHARACTER_CLASS_COMBINATION)((_value >> (int)ADMITTANCE_FLAGS_SHIFT) & ADMITTANCE_FLAGS_MASK);
            }
            public void SetAdmittanceFlags(NAVIGATION_CHARACTER_CLASS_COMBINATION admittance_flags_)
            {
                _value = (_value & ~(ADMITTANCE_FLAGS_MASK << (int)ADMITTANCE_FLAGS_SHIFT)) | ((uint)admittance_flags_ << (int)ADMITTANCE_FLAGS_SHIFT);
            }

            public OffMeshLinkType GetLinkType()
            {
                return (OffMeshLinkType)((_value >> (int)LINK_TYPE_SHIFT) & LINK_TYPE_MASK);
            }
            public void SetLinkType(OffMeshLinkType link_type_)
            {
                _value = (_value & ~(LINK_TYPE_MASK << (int)LINK_TYPE_SHIFT)) | ((uint)link_type_ << (int)LINK_TYPE_SHIFT);
            }

            public AreaHeight GetHeightLimitedAmount()
            {
                return (AreaHeight)((_value >> (int)HEIGHT_LIMITED_AMOUNT_SHIFT) & HEIGHT_LIMITED_AMOUNT_MASK);
            }
            public void SetHeightLimitedAmount(AreaHeight height_limited_amount_)
            {
                _value = (_value & ~(HEIGHT_LIMITED_AMOUNT_MASK << (int)HEIGHT_LIMITED_AMOUNT_SHIFT)) | ((uint)height_limited_amount_ << (int)HEIGHT_LIMITED_AMOUNT_SHIFT);
            }

            public NavMeshAreaType GetMarkupFlags()
            {
                return (NavMeshAreaType)((_value >> (int)MARKUP_FLAGS_SHIFT) & MARKUP_FLAGS_MASK);
            }
            public void SetMarkupFlags(NavMeshAreaType markup_flags_)
            {
                _value = (_value & ~(MARKUP_FLAGS_MASK << (int)MARKUP_FLAGS_SHIFT)) | ((uint)markup_flags_ << (int)MARKUP_FLAGS_SHIFT);
            }

            public static implicit operator uint(dt_area_t area)
            {
                return area._value;
            }
            public static implicit operator dt_area_t(uint value)
            {
                dt_area_t area = default(dt_area_t);
                area._value = value;
                return area;
            }

            public static bool operator ==(dt_area_t lhs, dt_area_t rhs)
            {
                return lhs._value == rhs._value;
            }
            public static bool operator !=(dt_area_t lhs, dt_area_t rhs)
            {
                return lhs._value != rhs._value;
            }
            public override bool Equals(object obj)
            {
                if (obj is dt_area_t)
                    return this == (dt_area_t)obj;
                return false;
            }

            public override int GetHashCode()
            {
                return _value.GetHashCode();
            }
        }

        public enum dtPolyTypes
        {
            /// The polygon is a standard convex polygon that is part of the surface of the mesh.
            DT_POLYTYPE_GROUND = 0,
            /// The polygon is an off-mesh connection consisting of two vertices.
            DT_POLYTYPE_OFFMESH_CONNECTION = 1,
        };

        public enum OffMeshLinkType
        {
            Manual,
            Teleport,
            Traversal,
            Wait,
            Backstage,
            INVALID
        }

        public enum AreaHeight
        {
            Standing,
            Crouch,
            DeepCrouch
        }

        public enum NavMeshAreaType
        {
            Normal,
            Backstage,
            Expensive
        }

        public struct NavMeshAreaTypeFlags
        {
            private uint _value;

            public NavMeshAreaTypeFlags(uint value)
            {
                _value = value;
            }

            public static readonly NavMeshAreaTypeFlags NormalFlag = new NavMeshAreaTypeFlags(1u << (int)NavMeshAreaType.Normal);
            public static readonly NavMeshAreaTypeFlags BackstageFlag = new NavMeshAreaTypeFlags(1u << (int)NavMeshAreaType.Backstage);
            public static readonly NavMeshAreaTypeFlags ExpensiveFlag = new NavMeshAreaTypeFlags(1u << (int)NavMeshAreaType.Expensive);
            public static readonly NavMeshAreaTypeFlags All = NormalFlag | BackstageFlag | ExpensiveFlag;
            public static readonly NavMeshAreaTypeFlags None = new NavMeshAreaTypeFlags(0);

            public static NavMeshAreaTypeFlags operator |(NavMeshAreaTypeFlags lhs, NavMeshAreaTypeFlags rhs)
            {
                return new NavMeshAreaTypeFlags(lhs._value | rhs._value);
            }
            public static NavMeshAreaTypeFlags operator &(NavMeshAreaTypeFlags lhs, NavMeshAreaTypeFlags rhs)
            {
                return new NavMeshAreaTypeFlags(lhs._value & rhs._value);
            }
            public static NavMeshAreaTypeFlags operator ^(NavMeshAreaTypeFlags lhs, NavMeshAreaTypeFlags rhs)
            {
                return new NavMeshAreaTypeFlags(lhs._value ^ rhs._value);
            }
            public static NavMeshAreaTypeFlags operator ~(NavMeshAreaTypeFlags flags)
            {
                return new NavMeshAreaTypeFlags(~flags._value);
            }

            public static bool operator ==(NavMeshAreaTypeFlags lhs, NavMeshAreaTypeFlags rhs)
            {
                return lhs._value == rhs._value;
            }
            public static bool operator !=(NavMeshAreaTypeFlags lhs, NavMeshAreaTypeFlags rhs)
            {
                return lhs._value != rhs._value;
            }

            public static implicit operator uint(NavMeshAreaTypeFlags flags)
            {
                return flags._value;
            }
            public static implicit operator NavMeshAreaTypeFlags(uint value)
            {
                return new NavMeshAreaTypeFlags(value);
            }

            public override bool Equals(object obj)
            {
                if (obj is NavMeshAreaTypeFlags)
                    return this == (NavMeshAreaTypeFlags)obj;
                return false;
            }
            public override int GetHashCode()
            {
                return _value.GetHashCode();
            }
            public override string ToString()
            {
                return _value.ToString();
            }
        }

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
            /// UID of connected traversal, if any. Use this to look up traversal data from elsewhere.
            public ShortGuid traversal_uid;
            /// The associated entity, for wait/manual nodes.
            public EntityHandle entity;
            /// Extra cost for using this link.
        	public float extra_cost;
            /// Link should only be used by characters travelling at or above this speed.
        	public LOCOMOTION_TARGET_SPEED min_speed;
            /// Link should only be used by characters travelling at or below this speed.
        	public LOCOMOTION_TARGET_SPEED max_speed;
        };
        #endregion
    }
}
