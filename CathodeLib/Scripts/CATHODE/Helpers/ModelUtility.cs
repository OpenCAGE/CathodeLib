using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Text;
using System.Windows;
using CATHODE;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
#endif

namespace CathodeLib
{
    public class Mesh : IEquatable<Mesh>
    {
        public List<UInt16> Indices = new List<UInt16>();
        public List<Vector3> Vertices = new List<Vector3>();
        public List<Vector3> Normals = new List<Vector3>();
        public List<Vector4> BiNormals = new List<Vector4>();
        public List<Vector4> Tangents = new List<Vector4>();
        public List<Color> Colours = new List<Color>();
        public List<Vector2>[] UVs = new List<Vector2>[0];

        public List<Vector4> BoneIndexes = new List<Vector4>();
        public List<Vector4> BoneWeights = new List<Vector4>();

        public static bool operator ==(Mesh x, Mesh y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return false;
            if (ReferenceEquals(x, y)) return true;
            if (!ListsEqual(x.Indices, y.Indices)) return false;
            if (!ListsEqual(x.Vertices, y.Vertices)) return false;
            if (!ListsEqual(x.Normals, y.Normals)) return false;
            if (!ListsEqual(x.BiNormals, y.BiNormals)) return false;
            if (!ListsEqual(x.Tangents, y.Tangents)) return false;
            if (!ListsEqual(x.Colours, y.Colours)) return false;
            if (!UVArraysEqual(x.UVs, y.UVs)) return false;
            if (!ListsEqual(x.BoneIndexes, y.BoneIndexes)) return false;
            if (!ListsEqual(x.BoneWeights, y.BoneWeights)) return false;
            return true;
        }

        public static bool operator !=(Mesh x, Mesh y)
        {
            return !(x == y);
        }

        public bool Equals(Mesh other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is Mesh mesh && this.Equals(mesh);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (Indices?.Count ?? 0);
                foreach (var index in Indices ?? new List<UInt16>())
                {
                    hash = hash * 23 + index.GetHashCode();
                }
                hash = hash * 23 + (Vertices?.Count ?? 0);
                foreach (var vertex in Vertices ?? new List<Vector3>())
                {
                    hash = hash * 23 + vertex.GetHashCode();
                }
                hash = hash * 23 + (Normals?.Count ?? 0);
                foreach (var normal in Normals ?? new List<Vector3>())
                {
                    hash = hash * 23 + normal.GetHashCode();
                }
                hash = hash * 23 + (BiNormals?.Count ?? 0);
                foreach (var binormal in BiNormals ?? new List<Vector4>())
                {
                    hash = hash * 23 + binormal.GetHashCode();
                }
                hash = hash * 23 + (Tangents?.Count ?? 0);
                foreach (var tangent in Tangents ?? new List<Vector4>())
                {
                    hash = hash * 23 + tangent.GetHashCode();
                }
                hash = hash * 23 + (Colours?.Count ?? 0);
                foreach (var colour in Colours ?? new List<Color>())
                {
                    hash = hash * 23 + colour.GetHashCode();
                }
                hash = hash * 23 + (UVs?.Length ?? 0);
                if (UVs != null)
                {
                    for (int i = 0; i < UVs.Length; i++)
                    {
                        hash = hash * 23 + (UVs[i]?.Count ?? 0);
                        if (UVs[i] != null)
                        {
                            foreach (var uv in UVs[i])
                            {
                                hash = hash * 23 + uv.GetHashCode();
                            }
                        }
                    }
                }
                hash = hash * 23 + (BoneIndexes?.Count ?? 0);
                foreach (var boneIndex in BoneIndexes ?? new List<Vector4>())
                {
                    hash = hash * 23 + boneIndex.GetHashCode();
                }
                hash = hash * 23 + (BoneWeights?.Count ?? 0);
                foreach (var boneWeight in BoneWeights ?? new List<Vector4>())
                {
                    hash = hash * 23 + boneWeight.GetHashCode();
                }
                return hash;
            }
        }

        private static bool ListsEqual<T>(List<T> x, List<T> y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return false;
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(x[i], y[i])) return false;
            }
            return true;
        }

        private static bool UVArraysEqual(List<Vector2>[] x, List<Vector2>[] y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return false;
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
            {
                if (!ListsEqual(x[i], y[i])) return false;
            }
            return true;
        }
    }

    public static class ModelUtility
    {
        /// <summary>
        /// Convert a CS2 submesh to a Mesh object with easier to handle data
        /// </summary>
        public static Mesh ToMesh(this Models.CS2.Component.LOD.Submesh submesh)
        {
            Mesh mesh = new Mesh();

            if (submesh == null || submesh.Data.Length == 0)
                return mesh;

            using (BinaryReader reader = new BinaryReader(new MemoryStream(submesh.Data)))
            {
                for (int i = 0; i < submesh.VertexFormatFull.Attributes.Count; ++i)
                {
                    if (i == submesh.VertexFormatFull.Attributes.Count - 1)
                    {
                        for (int x = 0; x < submesh.IndexCount; x++)
                            mesh.Indices.Add(reader.ReadUInt16());
                        continue;
                    }

                    for (int x = 0; x < submesh.VertexCount; ++x)
                    {
                        for (int y = 0; y < submesh.VertexFormatFull.Attributes[i].Count; ++y)
                        {
                            Models.VertexFormat.Attribute attr = submesh.VertexFormatFull.Attributes[i][y];
                            Vector4 v = ReadVertexData(reader, attr.Type);

                            switch (attr.Usage)
                            {
                                case Models.VertexFormat.Usage.Position:
                                    mesh.Vertices.Add(new Vector3(v.X * submesh.VertexScale, v.Y * submesh.VertexScale, v.Z * submesh.VertexScale));
                                    break;
                                case Models.VertexFormat.Usage.BlendWeight:
                                    mesh.BoneWeights.Add(v);
                                    break;
                                case Models.VertexFormat.Usage.BlendIndices:
                                    mesh.BoneIndexes.Add(v);
                                    break;
                                case Models.VertexFormat.Usage.Normal:
                                    mesh.Normals.Add(new Vector3(v.X, v.Y, v.Z));
                                    break;
                                case Models.VertexFormat.Usage.TexCoord:
                                    if (attr.Index >= mesh.UVs.Length)
                                        Array.Resize(ref mesh.UVs, attr.Index + 1);
                                    if (mesh.UVs[attr.Index] == null)
                                        mesh.UVs[attr.Index] = new List<Vector2>();
                                    mesh.UVs[attr.Index].Add(new Vector2(v.X * 16.0f, v.Y * 16.0f)); // TODO: is Z/W used?
                                    break;
                                case Models.VertexFormat.Usage.Tangent:
                                    mesh.Tangents.Add(v);
                                    break;
                                case Models.VertexFormat.Usage.Binormal:
                                    mesh.BiNormals.Add(v);
                                    break;
                                case Models.VertexFormat.Usage.Color:
                                    mesh.Colours.Add(Color.FromArgb((int)(v.W * 255.0f), (int)(v.X * 255.0f), (int)(v.Y * 255.0f), (int)(v.Z * 255.0f))); // TODO: is this correct?
                                    break;
                            }
                        }
                    }
                    Utilities.Align(reader, 16);
                }
            }
            return mesh;
        }

        /// <summary>
        /// Convert a Mesh object to CS2 submesh data and update the relevant submesh parameters to match
        /// </summary>
        public static void ToSubmeshData(this Mesh mesh, Models.CS2.Component.LOD.Submesh submesh)
        {
            if (mesh == null || submesh == null || submesh.VertexFormatFull == null)
            {
                submesh.Data = new byte[0];
                return;
            }

            submesh.VertexCount = mesh.Vertices.Count;
            submesh.IndexCount = mesh.Indices.Count;

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                for (int i = 0; i < submesh.VertexFormatFull.Attributes.Count; ++i)
                {
                    if (i == submesh.VertexFormatFull.Attributes.Count - 1)
                    {
                        for (int x = 0; x < mesh.Indices.Count; x++)
                            writer.Write(mesh.Indices[x]);
                        continue;
                    }

                    for (int x = 0; x < mesh.Vertices.Count; ++x)
                    {
                        for (int y = 0; y < submesh.VertexFormatFull.Attributes[i].Count; ++y)
                        {
                            Models.VertexFormat.Attribute attr = submesh.VertexFormatFull.Attributes[i][y];
                            Vector4 v = GetVertexDataForAttribute(mesh, attr, x, submesh.VertexScale);
                            WriteVertexData(writer, v, attr.Type);
                        }
                    }
                    Utilities.Align(writer, 16);
                }
                Utilities.Align(writer, 16);
                submesh.Data = stream.ToArray();
            }
        }

        private static Vector4 ReadVertexData(BinaryReader reader, Models.VertexFormat.Type type)
        {
            switch (type)
            {
                case Models.VertexFormat.Type.FP32_1:
                    return new Vector4(reader.ReadSingle(), 0, 0, 0);
                case Models.VertexFormat.Type.FP32_2:
                    return new Vector4(reader.ReadSingle(), reader.ReadSingle(), 0, 0);
                case Models.VertexFormat.Type.FP32_3:
                    return new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), 0);
                case Models.VertexFormat.Type.FP32_4:
                    return new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                case Models.VertexFormat.Type.Color:
                    uint data = reader.ReadUInt32();
                    return new Vector4((float)((data & 0xFF000000) >> 24) / 255.0f, (float)((data & 0x00FF0000) >> 16) / 255.0f, (float)((data & 0x0000FF00) >> 8) / 255.0f, (float)((data & 0x000000FF) >> 0) / 255.0f);
                case Models.VertexFormat.Type.U8_4:
                    return new Vector4(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                case Models.VertexFormat.Type.S16_2:
                    return new Vector4(reader.ReadInt16(), reader.ReadInt16(), 0, 0);
                case Models.VertexFormat.Type.S16_4:
                    return new Vector4(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
                case Models.VertexFormat.Type.U8_4N:
                    return new Vector4((float)reader.ReadByte() / 255.0f, (float)reader.ReadByte() / 255.0f, (float)reader.ReadByte() / 255.0f, (float)reader.ReadByte() / 255.0f);
                case Models.VertexFormat.Type.S16_2N:
                    return new Vector4((float)reader.ReadInt16() / (float)Int16.MaxValue, (float)reader.ReadInt16() / (float)Int16.MaxValue, 0, 0);
                case Models.VertexFormat.Type.S16_4N:
                    return new Vector4((float)reader.ReadInt16() / (float)Int16.MaxValue, (float)reader.ReadInt16() / (float)Int16.MaxValue, (float)reader.ReadInt16() / (float)Int16.MaxValue, (float)reader.ReadInt16() / (float)Int16.MaxValue);
                case Models.VertexFormat.Type.U16_2N:
                    return new Vector4((float)reader.ReadUInt16() / (float)UInt16.MaxValue, (float)reader.ReadUInt16() / (float)UInt16.MaxValue, 0, 0);
                case Models.VertexFormat.Type.U16_4N:
                    return new Vector4((float)reader.ReadUInt16() / (float)UInt16.MaxValue, (float)reader.ReadUInt16() / (float)UInt16.MaxValue, (float)reader.ReadUInt16() / (float)UInt16.MaxValue, (float)reader.ReadUInt16() / (float)UInt16.MaxValue);
                case Models.VertexFormat.Type.Dec3N:
                    uint val = reader.ReadUInt32();
                    short sx = (short)((val >> 20) & 0x3ff);
                    short sy = (short)((val >> 10) & 0x3ff);
                    short sz = (short)((val) & 0x3ff);
                    return new Vector4(((sx < 512) ? sx : (sx - 1024)) / 511.0f, ((sy < 512) ? sy : (sy - 1024)) / 511.0f, ((sz < 512) ? sz : (sz - 1024)) / 511.0f, 0);
            }
            throw new Exception("Unsupported VertexFormatType");
        }

        private static Vector4 GetVertexDataForAttribute(Mesh mesh, Models.VertexFormat.Attribute attr, int vertexIndex, int vertexScale)
        {
            switch (attr.Usage)
            {
                case Models.VertexFormat.Usage.Position:
                    if (vertexIndex < mesh.Vertices.Count)
                    {
                        Vector3 pos = mesh.Vertices[vertexIndex];
                        return new Vector4(pos.X / vertexScale, pos.Y / vertexScale, pos.Z / vertexScale, 0);
                    }
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.BlendWeight:
                    if (vertexIndex < mesh.BoneWeights.Count)
                        return mesh.BoneWeights[vertexIndex];
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.BlendIndices:
                    if (vertexIndex < mesh.BoneIndexes.Count)
                        return mesh.BoneIndexes[vertexIndex];
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.Normal:
                    if (vertexIndex < mesh.Normals.Count)
                    {
                        Vector3 normal = mesh.Normals[vertexIndex];
                        return new Vector4(normal.X, normal.Y, normal.Z, 0);
                    }
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.TexCoord:
                    if (attr.Index < mesh.UVs.Length && mesh.UVs[attr.Index] != null && vertexIndex < mesh.UVs[attr.Index].Count)
                    {
                        Vector2 uv = mesh.UVs[attr.Index][vertexIndex];
                        return new Vector4(uv.X / 16.0f, uv.Y / 16.0f, 0, 0);
                    }
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.Tangent:
                    if (vertexIndex < mesh.Tangents.Count)
                        return mesh.Tangents[vertexIndex];
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.Binormal:
                    if (vertexIndex < mesh.BiNormals.Count)
                        return mesh.BiNormals[vertexIndex];
                    return new Vector4(0, 0, 0, 0);
                case Models.VertexFormat.Usage.Color:
                    if (vertexIndex < mesh.Colours.Count)
                    {
                        Color c = mesh.Colours[vertexIndex];
                        return new Vector4(c.R / 255.0f, c.G / 255.0f, c.B / 255.0f, c.A / 255.0f);
                    }
                    return new Vector4(0, 0, 0, 0);
                default:
                    return new Vector4(0, 0, 0, 0);
            }
        }

        private static void WriteVertexData(BinaryWriter writer, Vector4 v, Models.VertexFormat.Type type)
        {
            switch (type)
            {
                case Models.VertexFormat.Type.FP32_1:
                    writer.Write(v.X);
                    break;
                case Models.VertexFormat.Type.FP32_2:
                    writer.Write(v.X);
                    writer.Write(v.Y);
                    break;
                case Models.VertexFormat.Type.FP32_3:
                    writer.Write(v.X);
                    writer.Write(v.Y);
                    writer.Write(v.Z);
                    break;
                case Models.VertexFormat.Type.FP32_4:
                    writer.Write(v.X);
                    writer.Write(v.Y);
                    writer.Write(v.Z);
                    writer.Write(v.W);
                    break;
                case Models.VertexFormat.Type.Color:
                    uint data = ((uint)(v.X * 255.0f) << 24) | ((uint)(v.Y * 255.0f) << 16) | ((uint)(v.Z * 255.0f) << 8) | (uint)(v.W * 255.0f);
                    writer.Write(data);
                    break;
                case Models.VertexFormat.Type.U8_4:
                    writer.Write((byte)v.X);
                    writer.Write((byte)v.Y);
                    writer.Write((byte)v.Z);
                    writer.Write((byte)v.W);
                    break;
                case Models.VertexFormat.Type.S16_2:
                    writer.Write((Int16)v.X);
                    writer.Write((Int16)v.Y);
                    break;
                case Models.VertexFormat.Type.S16_4:
                    writer.Write((Int16)v.X);
                    writer.Write((Int16)v.Y);
                    writer.Write((Int16)v.Z);
                    writer.Write((Int16)v.W);
                    break;
                case Models.VertexFormat.Type.U8_4N:
                    writer.Write((byte)(v.X * 255.0f));
                    writer.Write((byte)(v.Y * 255.0f));
                    writer.Write((byte)(v.Z * 255.0f));
                    writer.Write((byte)(v.W * 255.0f));
                    break;
                case Models.VertexFormat.Type.S16_2N:
                    writer.Write((Int16)(v.X * Int16.MaxValue));
                    writer.Write((Int16)(v.Y * Int16.MaxValue));
                    break;
                case Models.VertexFormat.Type.S16_4N:
                    writer.Write((Int16)(v.X * Int16.MaxValue));
                    writer.Write((Int16)(v.Y * Int16.MaxValue));
                    writer.Write((Int16)(v.Z * Int16.MaxValue));
                    writer.Write((Int16)(v.W * Int16.MaxValue));
                    break;
                case Models.VertexFormat.Type.U16_2N:
                    writer.Write((UInt16)(v.X * UInt16.MaxValue));
                    writer.Write((UInt16)(v.Y * UInt16.MaxValue));
                    break;
                case Models.VertexFormat.Type.U16_4N:
                    writer.Write((UInt16)(v.X * UInt16.MaxValue));
                    writer.Write((UInt16)(v.Y * UInt16.MaxValue));
                    writer.Write((UInt16)(v.Z * UInt16.MaxValue));
                    writer.Write((UInt16)(v.W * UInt16.MaxValue));
                    break;
                case Models.VertexFormat.Type.Dec3N:
                    short sx = (short)(Math.Max(-511, Math.Min(511, v.X * 511.0f)));
                    short sy = (short)(Math.Max(-511, Math.Min(511, v.Y * 511.0f)));
                    short sz = (short)(Math.Max(-511, Math.Min(511, v.Z * 511.0f)));
                    if (sx < 0) sx += 1024;
                    if (sy < 0) sy += 1024;
                    if (sz < 0) sz += 1024;
                    uint val = ((uint)(sx & 0x3ff) << 20) | ((uint)(sy & 0x3ff) << 10) | ((uint)(sz & 0x3ff));
                    writer.Write(val);
                    break;
                default:
                    throw new Exception("Unsupported VertexFormatType");
            }
        }
    }
}
