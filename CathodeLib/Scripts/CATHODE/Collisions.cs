using CathodeLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices;
#endif

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/COLLISION.BIN */
    public class Collisions : CathodeFile
    {
        public List<WeightedCollision> Entries = new List<WeightedCollision>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public Collisions(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (var reader = new BinaryReader(File.OpenRead(_filepath)))
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

                    vertWrite.Position[0] = (short)(vert.Position.X * 32767.0f);
                    vertWrite.Position[1] = (short)(vert.Position.Y * 32767.0f);
                    vertWrite.Position[2] = (short)(vert.Position.Z * 32767.0f);
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
            return true;
        }
        #endregion

        #region STRUCTURES
        public class WeightedCollision
        {
            public float Scale { get; set; }

            public List<Bone> Bones { get; set; } = new List<Bone>();
            public List<Vertex> Vertices { get; set; } = new List<Vertex>();

            public class Bone
            {
                public Matrix4x4 InverseModelBounds { get; set; }
                public int BoneId { get; set; }
                public List<ushort> TriangleIndices { get; set; } = new List<ushort>();
                public List<ushort> VertexIndices { get; set; } = new List<ushort>();
            }
            public class Vertex
            {
                public Vector3 Position { get; set; }
                public byte[] BoneIndices { get; set; } = new byte[4];
                public float[] BoneWeights { get; set; } = new float[4];
                public float Armour { get; set; }
                public float Damage { get; set; }
                public bool ImpactEffectMask { get; set; }
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