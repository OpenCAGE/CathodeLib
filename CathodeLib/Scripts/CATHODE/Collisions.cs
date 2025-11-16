using CathodeLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices;
using static CATHODE.Movers;
using static CATHODE.Models;
using CathodeLib.ObjectExtensions;


#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/COLLISION.BIN
    /// </summary>
    public class Collisions : CathodeFile
    {
        public List<WeightedCollision> Entries = new List<WeightedCollision>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public Collisions(string path) : base(path) { }
        public Collisions(MemoryStream stream, string path = "") : base(stream, path) { }
        public Collisions(byte[] data, string path = "") : base(data, path) { }

        private List<WeightedCollision> _writeList = new List<WeightedCollision>();

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (var reader = new BinaryReader(stream))
            {
                //remove these if exceptions dont throw
                byte[] magic = reader.ReadBytes(4);
                if (!magic.SequenceEqual(new byte[4] { 0x0C, 0xA0, 0xFE, 0xEF })) throw new Exception();
                int ver = reader.ReadInt32();
                if (ver != 2) throw new Exception();
                int zer = reader.ReadInt32();
                if (zer != 0) throw new Exception();

                int dataSize = reader.ReadInt32();
                int entryCount = reader.ReadInt32();
                int boneCount = reader.ReadInt32();
                int vertexCount = reader.ReadInt32();
                int indexCount = reader.ReadInt32();

                var collisionData = Utilities.ConsumeArray<WeightedCollisionData>(reader, entryCount);
                var boneData = Utilities.ConsumeArray<BoneData>(reader, boneCount);
                var vertexData = Utilities.ConsumeArray<VertexData>(reader, vertexCount);
                var indexData = Enumerable.Range(0, indexCount).Select(i => reader.ReadUInt16()).ToArray();

                foreach (var pData in collisionData)
                {
                    var collision = new WeightedCollision();
                    Entries.Add(collision);

                    for (ulong i = pData.BonesOffset; i < pData.BonesOffset + pData.NumBones; i++)
                    {
                        var pBone = boneData[i];
                        var wcb = new WeightedCollision.Bone
                        {
                            InverseModelBounds = pBone.InvModelBounds,
                            BoneId = pBone.Bone
                        };
                        collision.Scale = pBone.Scale; //todo; does this really need to be handled per bone?

                        for (ulong j = pBone.TriangleIndicesOffset; j < pBone.TriangleIndicesOffset + pBone.NumTriangleIndices; j++)
                            wcb.TriangleIndices.Add(indexData[j]);

                        for (ulong j = pBone.VertexIndicesOffset; j < pBone.VertexIndicesOffset + pBone.NumVertexIndices; j++)
                            wcb.VertexIndices.Add(indexData[j]);

                        collision.Bones.Add(wcb);
                    }

                    for (ulong i = pData.VerticesOffset; i < pData.VerticesOffset + pData.NumVertices; i++)
                    {
                        var pVert = vertexData[i];
                        var wcv = new WeightedCollision.Vertex
                        {
                            Armour = pVert.Armour / 255.0f,
                            Damage = pVert.Damage / 127.0f,
                            ImpactEffectMask = pVert.ImpactEffectMask
                        };

                        wcv.Position = new Vector3(
                            pVert.Position[0] / 32767.0f,
                            pVert.Position[1] / 32767.0f,
                            pVert.Position[2] / 32767.0f
                        );
                        for (int j = 0; j < 4; j++) wcv.BoneIndices[j] = pVert.Indices[j];
                        for (int j = 0; j < 4; j++) wcv.BoneWeights[j] = pVert.Weights[j] / 255.0f;
                        collision.Vertices.Add(wcv);
                    }
                }
            }
            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            var collisionData = new List<WeightedCollisionData>();
            var boneData = new List<BoneData>();
            var vertexData = new List<VertexData>();
            var indexData = new List<ushort>();

            foreach (var data in Entries)
            {
                var dataWrite = new WeightedCollisionData
                {
                    NumBones = (uint)data.Bones.Count,
                    NumVertices = (uint)data.Vertices.Count,
                    BonesOffset = (ulong)boneData.Count,
                    VerticesOffset = (ulong)vertexData.Count
                };
                collisionData.Add(dataWrite);

                foreach (var bone in data.Bones)
                {
                    var boneWrite = new BoneData
                    {
                        InvModelBounds = bone.InverseModelBounds,
                        Bone = bone.BoneId,
                        Scale = data.Scale,
                        NumTriangleIndices = (uint)bone.TriangleIndices.Count,
                        NumVertexIndices = (uint)bone.VertexIndices.Count,
                        TriangleIndicesOffset = (ulong)indexData.Count
                    };
                    indexData.AddRange(bone.TriangleIndices);

                    boneWrite.VertexIndicesOffset = (ulong)indexData.Count;
                    indexData.AddRange(bone.VertexIndices);

                    boneData.Add(boneWrite);
                }

                foreach (var vert in data.Vertices)
                {
                    var vertWrite = new VertexData
                    {
                        Armour = (byte)(vert.Armour * 255.0f),
                        DamageData = (byte)(((byte)(vert.Damage * 127.0f) & 0x7F) | (vert.ImpactEffectMask ? 0x80 : 0x00)),
                        Position = new short[3],
                        Indices = new byte[4],
                        Weights = new byte[4]
                    };

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    vertWrite.Position[0] = (short)(vert.Position.x * 32767.0f);
                    vertWrite.Position[1] = (short)(vert.Position.y * 32767.0f);
                    vertWrite.Position[2] = (short)(vert.Position.z * 32767.0f);
#else
                    vertWrite.Position[0] = (short)(vert.Position.X * 32767.0f);
                    vertWrite.Position[1] = (short)(vert.Position.Y * 32767.0f);
                    vertWrite.Position[2] = (short)(vert.Position.Z * 32767.0f);
#endif
                    for (int i = 0; i < 4; i++) vertWrite.Indices[i] = vert.BoneIndices[i];
                    for (int i = 0; i < 4; i++) vertWrite.Weights[i] = (byte)(vert.BoneWeights[i] * 255.0f);

                    vertexData.Add(vertWrite);
                }
            }

            using (var writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(new byte[4] { 0x0C, 0xA0, 0xFE, 0xEF });
                writer.Write(2);
                writer.Write(0);

                writer.Write(
                    collisionData.Count * Marshal.SizeOf<WeightedCollisionData>() +
                    boneData.Count * Marshal.SizeOf<BoneData>() +
                    vertexData.Count * Marshal.SizeOf<VertexData>() +
                    indexData.Count * sizeof(ushort)
                );
                writer.Write(collisionData.Count);
                writer.Write(boneData.Count);
                writer.Write(vertexData.Count);
                writer.Write(indexData.Count);

                collisionData.ForEach(d => Utilities.Write(writer, d));
                boneData.ForEach(b => Utilities.Write(writer, b));
                vertexData.ForEach(v => Utilities.Write(writer, v));
                indexData.ForEach(i => writer.Write(i));
            }
            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(WeightedCollision collision)
        {
            if (!_writeList.Contains(collision)) return -1;
            return _writeList.IndexOf(collision);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public WeightedCollision GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public WeightedCollision AddEntry(WeightedCollision collision)
        {
            if (collision == null)
                return null;

            WeightedCollision newCollision = collision.Copy();
            Entries.Add(newCollision);
            return newCollision;
        }
        #endregion

        #region STRUCTURES
        public class WeightedCollision : IEquatable<WeightedCollision>
        {
            public float Scale { get; set; }

            public List<Bone> Bones { get; set; } = new List<Bone>();
            public List<Vertex> Vertices { get; set; } = new List<Vertex>();

            public static bool operator ==(WeightedCollision x, WeightedCollision y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                return x.Equals(y);
            }

            public static bool operator !=(WeightedCollision x, WeightedCollision y)
            {
                return !(x == y);
            }

            public bool Equals(WeightedCollision other)
            {
                if (other == null) return false;
                if (ReferenceEquals(this, other)) return true;

                if (Math.Abs(Scale - other.Scale) > float.Epsilon) return false;
                if (Bones.Count != other.Bones.Count) return false;
                if (Vertices.Count != other.Vertices.Count) return false;

                for (int i = 0; i < Bones.Count; i++)
                {
                    if (!Bones[i].Equals(other.Bones[i])) return false;
                }

                for (int i = 0; i < Vertices.Count; i++)
                {
                    if (!Vertices[i].Equals(other.Vertices[i])) return false;
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as WeightedCollision);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + Scale.GetHashCode();
                    hash = hash * 23 + Bones.Count.GetHashCode();
                    hash = hash * 23 + Vertices.Count.GetHashCode();
                    foreach (var bone in Bones)
                    {
                        hash = hash * 23 + bone.GetHashCode();
                    }
                    foreach (var vertex in Vertices)
                    {
                        hash = hash * 23 + vertex.GetHashCode();
                    }
                    return hash;
                }
            }

            public class Bone : IEquatable<Bone>
            {
                public Matrix4x4 InverseModelBounds { get; set; }
                public int BoneId { get; set; }
                public List<ushort> TriangleIndices { get; set; } = new List<ushort>();
                public List<ushort> VertexIndices { get; set; } = new List<ushort>();

                public static bool operator ==(Bone x, Bone y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return false;
                    return x.Equals(y);
                }

                public static bool operator !=(Bone x, Bone y)
                {
                    return !(x == y);
                }

                public bool Equals(Bone other)
                {
                    if (other == null) return false;
                    if (ReferenceEquals(this, other)) return true;

                    if (BoneId != other.BoneId) return false;
                    if (TriangleIndices.Count != other.TriangleIndices.Count) return false;
                    if (VertexIndices.Count != other.VertexIndices.Count) return false;

                    // Compare Matrix4x4 component-wise
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    for (int i = 0; i < 16; i++)
                    {
                        if (Math.Abs(InverseModelBounds[i] - other.InverseModelBounds[i]) > float.Epsilon)
                            return false;
                    }
#else
                    if (InverseModelBounds != other.InverseModelBounds) return false;
#endif

                    for (int i = 0; i < TriangleIndices.Count; i++)
                    {
                        if (TriangleIndices[i] != other.TriangleIndices[i]) return false;
                    }

                    for (int i = 0; i < VertexIndices.Count; i++)
                    {
                        if (VertexIndices[i] != other.VertexIndices[i]) return false;
                    }

                    return true;
                }

                public override bool Equals(object obj)
                {
                    return Equals(obj as Bone);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int hash = 17;
                        hash = hash * 23 + BoneId.GetHashCode();
                        hash = hash * 23 + TriangleIndices.Count.GetHashCode();
                        hash = hash * 23 + VertexIndices.Count.GetHashCode();
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                        for (int i = 0; i < 16; i++)
                        {
                            hash = hash * 23 + InverseModelBounds[i].GetHashCode();
                        }
#else
                        hash = hash * 23 + InverseModelBounds.GetHashCode();
#endif
                        foreach (var idx in TriangleIndices)
                        {
                            hash = hash * 23 + idx.GetHashCode();
                        }
                        foreach (var idx in VertexIndices)
                        {
                            hash = hash * 23 + idx.GetHashCode();
                        }
                        return hash;
                    }
                }
            }

            public class Vertex : IEquatable<Vertex>
            {
                public Vector3 Position { get; set; }
                public byte[] BoneIndices { get; set; } = new byte[4];
                public float[] BoneWeights { get; set; } = new float[4];
                public float Armour { get; set; }
                public float Damage { get; set; }
                public bool ImpactEffectMask { get; set; }

                public static bool operator ==(Vertex x, Vertex y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return false;
                    return x.Equals(y);
                }

                public static bool operator !=(Vertex x, Vertex y)
                {
                    return !(x == y);
                }

                public bool Equals(Vertex other)
                {
                    if (other == null) return false;
                    if (ReferenceEquals(this, other)) return true;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    if (Position != other.Position) return false;
#else
                    if (Position != other.Position) return false;
#endif
                    if (Math.Abs(Armour - other.Armour) > float.Epsilon) return false;
                    if (Math.Abs(Damage - other.Damage) > float.Epsilon) return false;
                    if (ImpactEffectMask != other.ImpactEffectMask) return false;

                    for (int i = 0; i < 4; i++)
                    {
                        if (BoneIndices[i] != other.BoneIndices[i]) return false;
                        if (Math.Abs(BoneWeights[i] - other.BoneWeights[i]) > float.Epsilon) return false;
                    }

                    return true;
                }

                public override bool Equals(object obj)
                {
                    return Equals(obj as Vertex);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int hash = 17;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                        hash = hash * 23 + Position.x.GetHashCode();
                        hash = hash * 23 + Position.y.GetHashCode();
                        hash = hash * 23 + Position.z.GetHashCode();
#else
                        hash = hash * 23 + Position.X.GetHashCode();
                        hash = hash * 23 + Position.Y.GetHashCode();
                        hash = hash * 23 + Position.Z.GetHashCode();
#endif
                        hash = hash * 23 + Armour.GetHashCode();
                        hash = hash * 23 + Damage.GetHashCode();
                        hash = hash * 23 + ImpactEffectMask.GetHashCode();
                        for (int i = 0; i < 4; i++)
                        {
                            hash = hash * 23 + BoneIndices[i].GetHashCode();
                            hash = hash * 23 + BoneWeights[i].GetHashCode();
                        }
                        return hash;
                    }
                }
            }
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct VertexData
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public short[] Position; 
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] Indices;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] Weights;

            public byte Armour;
            public byte DamageData;

            public byte Damage => (byte)(DamageData & 0x7F);
            public bool ImpactEffectMask => (DamageData & 0x80) != 0;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BoneData
        {
            public Matrix4x4 InvModelBounds;
            public float Scale;
            public int Bone;
            public uint NumTriangleIndices;
            public uint NumVertexIndices;
            public ulong TriangleIndicesOffset; 
            public ulong VertexIndicesOffset; 
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct WeightedCollisionData
        {
            public uint NumVertices;
            public uint NumBones;
            public ulong VerticesOffset;
            public ulong BonesOffset;
            public ulong Padding;
        }
        #endregion
    }
}