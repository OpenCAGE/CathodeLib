using CathodeLib;
using CATHODE.ShaderTypes;
using System;
using System.Collections.Generic;
using System.IO;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#elif GODOT
using Godot;
using System.Numerics;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;
using Vector4 = Godot.Vector4;
using Color = Godot.Color;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/OCCLUDER_TRIANGLE_BVH.BIN
    /// </summary>
    public class OccluderTriangleBVH : CathodeFile
    {
        public List<Triangle> Triangles = new List<Triangle>();
        public List<Node> Nodes = new List<Node>();

        public int NodesInUse;
        public int StackDepth;

        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public OccluderTriangleBVH(string path) : base(path) { }
        public OccluderTriangleBVH(MemoryStream stream, string path = "") : base(stream, path) { }
        public OccluderTriangleBVH(byte[] data, string path = "") : base(data, path) { }

        ~OccluderTriangleBVH()
        {
            Triangles?.Clear();
            Nodes?.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int triangleCount = reader.ReadInt32();
                int nodeCount = reader.ReadInt32();
                NodesInUse = reader.ReadInt32();
                StackDepth = reader.ReadInt32();

                for (int i = 0; i < triangleCount; i++)
                {
                    Triangle t = new Triangle();
                    t.Vertex1 = Utilities.Consume<Vector3>(reader);
                    reader.BaseStream.Position += 4;
                    t.Vertex2 = Utilities.Consume<Vector3>(reader);
                    reader.BaseStream.Position += 4;
                    t.Vertex3 = Utilities.Consume<Vector3>(reader);
                    reader.BaseStream.Position += 4;
                    Triangles.Add(t);
                }

                for (int i = 0; i < nodeCount; i++)
                {
                    Node n = new Node();
                    n.MinBounds = Utilities.Consume<Vector4>(reader);
                    n.MaxBounds = Utilities.Consume<Vector4>(reader);
                    n.ObjectIndex = reader.ReadInt32();
                    n.ObjectCount = reader.ReadInt32();
                    reader.BaseStream.Position += 8;
                    Nodes.Add(n);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Triangles.Count);
                writer.Write(Nodes.Count);
                writer.Write(NodesInUse > 0 ? NodesInUse : Nodes.Count);
                writer.Write(StackDepth);

                for (int i = 0; i < Triangles.Count; i++)
                {
                    Utilities.Write<Vector3>(writer, Triangles[i].Vertex1);
                    writer.Write(0);
                    Utilities.Write<Vector3>(writer, Triangles[i].Vertex2);
                    writer.Write(0);
                    Utilities.Write<Vector3>(writer, Triangles[i].Vertex3);
                    writer.Write(0);
                }

                for (int i = 0; i < Nodes.Count; i++)
                {
                    Utilities.Write<Vector4>(writer, Nodes[i].MinBounds);
                    Utilities.Write<Vector4>(writer, Nodes[i].MaxBounds);
                    writer.Write(Nodes[i].ObjectIndex);
                    writer.Write(Nodes[i].ObjectCount);
                    writer.Write(new byte[8]);
                }
            }
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Rebuild triangle list + BVH from current Mover data.
        /// </summary>
        public void RebuildFromMovers(Movers movers)
        {
            Triangles.Clear();
            Nodes.Clear();
            NodesInUse = 0;
            StackDepth = 0;

            if (movers?.Entries == null || movers.Entries.Count == 0)
                return;

            var items = new List<TriItem>(256);
            for (int i = 0; i < movers.Entries.Count; i++)
            {
                Movers.MOVER_DESCRIPTOR mvr = movers.Entries[i];
                if (mvr?.RenderableElements == null)
                    continue;

                for (int r = 0; r < mvr.RenderableElements.Count; r++)
                {
                    RenderableElements.Element re = mvr.RenderableElements[r];
                    if (re?.Material?.Shader == null || re.Model == null)
                        continue;
                    if (re.Material.Shader.Ubershader != SHADER_LIST.CA_OCCLUSION_CULLING)
                        continue;

                    cMesh mesh = re.Model.ToMesh();
                    if (mesh?.Indices == null || mesh.Vertices == null || mesh.Indices.Count < 3)
                        continue;

                    int groupStart = items.Count;
                    for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                    {
                        Vector3 a = TransformPoint(mesh.Vertices[mesh.Indices[t]], mvr.Transform);
                        Vector3 b = TransformPoint(mesh.Vertices[mesh.Indices[t + 1]], mvr.Transform);
                        Vector3 c = TransformPoint(mesh.Vertices[mesh.Indices[t + 2]], mvr.Transform);
                        Vector3 min = ComponentMin(a, ComponentMin(b, c));
                        Vector3 max = ComponentMax(a, ComponentMax(b, c));
                        items.Add(new TriItem
                        {
                            V1 = a,
                            V2 = b,
                            V3 = c,
                            Min = min,
                            Max = max,
                            Center = (min + max) * 0.5f,
                            MeshGroup = groupStart
                        });
                    }
                    int groupId = groupStart;
                    for (int t = groupStart; t < items.Count; t++)
                    {
                        TriItem item = items[t];
                        item.MeshGroup = groupId;
                        items[t] = item;
                    }
                }
            }

            if (items.Count == 0)
                return;

            Nodes.Add(new Node());
            int measuredDepth = 1;
            BuildInto(0, items, 0, items.Count, 8, 1, ref measuredDepth);
            StackDepth = measuredDepth;
            NodesInUse = Nodes.Count;

            int capacity = items.Count * 2 - 1;
            while (Nodes.Count < capacity)
                Nodes.Add(new Node());
        }

        private void BuildInto(int nodeIndex, List<TriItem> items, int start, int count, int maxTrisPerLeaf, int depth, ref int measuredDepth)
        {
            if (depth > measuredDepth)
                measuredDepth = depth;

            Vector3 unionMin = items[start].Min;
            Vector3 unionMax = items[start].Max;
            for (int i = 1; i < count; i++)
            {
                unionMin = ComponentMin(unionMin, items[start + i].Min);
                unionMax = ComponentMax(unionMax, items[start + i].Max);
            }

            bool singleMeshGroup = true;
            int group = items[start].MeshGroup;
            for (int i = 1; i < count; i++)
            {
                if (items[start + i].MeshGroup != group)
                {
                    singleMeshGroup = false;
                    break;
                }
            }

            if (count <= maxTrisPerLeaf || (singleMeshGroup && count <= 16))
            {
                int first = Triangles.Count;
                for (int i = 0; i < count; i++)
                {
                    TriItem item = items[start + i];
                    Triangles.Add(new Triangle
                    {
                        Vertex1 = item.V1,
                        Vertex2 = item.V2,
                        Vertex3 = item.V3
                    });
                }
                Nodes[nodeIndex] = new Node
                {
                    MinBounds = new Vector4(unionMin.X, unionMin.Y, unionMin.Z, 1.0f),
                    MaxBounds = new Vector4(unionMax.X, unionMax.Y, unionMax.Z, 1.0f),
                    ObjectIndex = first,
                    ObjectCount = count + 3
                };
                return;
            }

            Vector3 extent = unionMax - unionMin;
            int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : (extent.Y >= extent.Z ? 1 : 2);

            items.Sort(start, count, Comparer<TriItem>.Create((a, b) =>
            {
                float ca = axis == 0 ? a.Center.X : (axis == 1 ? a.Center.Y : a.Center.Z);
                float cb = axis == 0 ? b.Center.X : (axis == 1 ? b.Center.Y : b.Center.Z);
                int cmp = ca.CompareTo(cb);
                if (cmp != 0) return cmp;
                cmp = a.MeshGroup.CompareTo(b.MeshGroup);
                if (cmp != 0) return cmp;
                return a.Center.X.CompareTo(b.Center.X);
            }));

            int leftCount = count / 2;
            if (leftCount <= 0) leftCount = 1;
            if (leftCount >= count) leftCount = count - 1;

            int leftIndex = Nodes.Count;
            Nodes.Add(new Node());
            Nodes.Add(new Node());

            BuildInto(leftIndex, items, start, leftCount, maxTrisPerLeaf, depth + 1, ref measuredDepth);
            BuildInto(leftIndex + 1, items, start + leftCount, count - leftCount, maxTrisPerLeaf, depth + 1, ref measuredDepth);

            Node left = Nodes[leftIndex];
            Node right = Nodes[leftIndex + 1];
            Nodes[nodeIndex] = new Node
            {
                MinBounds = new Vector4(
                    Math.Min(left.MinBounds.X, right.MinBounds.X),
                    Math.Min(left.MinBounds.Y, right.MinBounds.Y),
                    Math.Min(left.MinBounds.Z, right.MinBounds.Z),
                    1.0f),
                MaxBounds = new Vector4(
                    Math.Max(left.MaxBounds.X, right.MaxBounds.X),
                    Math.Max(left.MaxBounds.Y, right.MaxBounds.Y),
                    Math.Max(left.MaxBounds.Z, right.MaxBounds.Z),
                    1.0f),
                ObjectIndex = leftIndex,
                ObjectCount = 0
            };
        }

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
        private static Vector3 ComponentMin(Vector3 a, Vector3 b) => Vector3.Min(a, b);
        private static Vector3 ComponentMax(Vector3 a, Vector3 b) => Vector3.Max(a, b);
        private static Vector3 TransformPoint(Vector3 point, Matrix4x4 matrix) => matrix.MultiplyPoint3x4(point);
#elif GODOT
        private static Vector3 ComponentMin(Vector3 a, Vector3 b) => new Vector3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        private static Vector3 ComponentMax(Vector3 a, Vector3 b) => new Vector3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        private static Vector3 TransformPoint(Vector3 point, Matrix4x4 matrix)
        {
            System.Numerics.Vector3 n = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(point.X, point.Y, point.Z), matrix);
            return new Vector3(n.X, n.Y, n.Z);
        }
#else
        private static Vector3 ComponentMin(Vector3 a, Vector3 b) => Vector3.Min(a, b);
        private static Vector3 ComponentMax(Vector3 a, Vector3 b) => Vector3.Max(a, b);
        private static Vector3 TransformPoint(Vector3 point, Matrix4x4 matrix) => Vector3.Transform(point, matrix);
#endif

        private struct TriItem
        {
            public Vector3 V1;
            public Vector3 V2;
            public Vector3 V3;
            public Vector3 Min;
            public Vector3 Max;
            public Vector3 Center;
            public int MeshGroup;
        }
        #endregion

        #region STRUCTURES
        public class Triangle
        {
            public Vector3 Vertex1;
            public Vector3 Vertex2;
            public Vector3 Vertex3;
        }

        public class Node
        {
            public Vector4 MinBounds;
            public Vector4 MaxBounds;

            public int ObjectIndex;
            public int ObjectCount;

            public bool IsLeaf => ObjectCount > 0;
            public int TriangleCount => IsLeaf ? ObjectCount - 3 : 0;
        }
        #endregion
    }
}