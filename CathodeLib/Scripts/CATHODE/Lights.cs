using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CATHODE.Enums;

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
    /// DATA/ENV/x/WORLD/LIGHTS.BIN
    /// </summary>
    public class Lights : CathodeFile
    {
        public List<int> Indexes = new List<int>();
        public List<Node> Values = new List<Node>();
        public DirectionalLight Sun = new DirectionalLight();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;

        public Lights(string path) : base(path) { }
        public Lights(MemoryStream stream, string path = "") : base(stream, path) { }
        public Lights(byte[] data, string path = "") : base(data, path) { }

        ~Lights()
        {
            Indexes.Clear();
            Values.Clear();
            Sun = null;
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 8;

                int numInstances = reader.ReadInt32();
                for (int i = 0; i < numInstances; i++)
                {
                    Indexes.Add(reader.ReadInt32()); // MVR index
                }

                int numNodes = reader.ReadInt16();
                for (int i = 0; i < numNodes; i++)
                {
                    Node node = new Node();
                    node.min = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    node.max = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    node.childA = reader.ReadInt16();
                    node.childB = reader.ReadInt16();
                    node.first = reader.ReadInt16();
                    node.count = reader.ReadInt16();
                    node.is_leaf = reader.ReadBoolean();
                    reader.BaseStream.Position += 3;
                    Values.Add(node);
                }

                Sun.enabled = reader.ReadBoolean();
                Sun.colour = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Sun.direction = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Sun.feature_flags = (LightFeature)reader.ReadUInt16();
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                Utilities.WriteString("ligt", writer);
                writer.Write(4);

                writer.Write(Indexes.Count);
                for (int i = 0; i < Indexes.Count; i++)
                    writer.Write(Indexes[i]);

                writer.Write((Int16)Values.Count);
                for (int i = 0; i < Values.Count; i++)
                {
                    Utilities.Write<Vector3>(writer, Values[i].min);
                    Utilities.Write<Vector3>(writer, Values[i].max);
                    writer.Write((Int16)Values[i].childA);
                    writer.Write((Int16)Values[i].childB);
                    writer.Write((Int16)Values[i].first);
                    writer.Write((Int16)Values[i].count);
                    writer.Write(Values[i].is_leaf);
                    writer.Write(new byte[3]);
                }

                writer.Write(Sun.enabled);
                Utilities.Write<Vector3>(writer, Sun.colour);
                Utilities.Write<Vector3>(writer, Sun.direction);
                writer.Write((ushort)Sun.feature_flags);
            }
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Rebuild Indexes/Values with the current data from Movers.
        /// </summary>
        public void RebuildFromMovers(Movers movers)
        {
            Indexes.Clear();
            Values.Clear();

            if (movers?.Entries == null || movers.Entries.Count == 0)
                return;

            var lights = new List<LightBounds>(256);
            for (int i = 0; i < movers.Entries.Count; i++)
            {
                Movers.MOVER_DESCRIPTOR mvr = movers.Entries[i];
                if (mvr?.RenderableElements == null || mvr.RenderableElements.Count == 0)
                    continue;
                Materials.Material material = mvr.RenderableElements[0]?.Material;
                if (material?.OfflineLightFeatures == null)
                    continue;

                if (!TryComputeBounds(mvr, out Vector3 min, out Vector3 max))
                    continue;

                lights.Add(new LightBounds
                {
                    MoverIndex = i,
                    Min = min,
                    Max = max,
                    Center = (min + max) * 0.5f
                });
            }

            if (lights.Count == 0)
                return;

            if (lights.Count > short.MaxValue)
                throw new InvalidOperationException("LIGHTS.BIN cannot represent more than " + short.MaxValue + " lights.");

            BuildNode(lights, 0, lights.Count);
        }

        private int BuildNode(List<LightBounds> lights, int start, int count)
        {
            int nodeIndex = Values.Count;
            Values.Add(new Node());

            if (count == 1)
            {
                LightBounds light = lights[start];
                short first = (short)Indexes.Count;
                Indexes.Add(light.MoverIndex);
                Values[nodeIndex] = new Node
                {
                    min = light.Min,
                    max = light.Max,
                    childA = -6004,
                    childB = -6004,
                    first = first,
                    count = 1,
                    is_leaf = true
                };
                return nodeIndex;
            }

            Vector3 unionMin = lights[start].Min;
            Vector3 unionMax = lights[start].Max;
            for (int i = 1; i < count; i++)
            {
                unionMin = Vector3.Min(unionMin, lights[start + i].Min);
                unionMax = Vector3.Max(unionMax, lights[start + i].Max);
            }

            Vector3 extent = unionMax - unionMin;
            int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : (extent.Y >= extent.Z ? 1 : 2);

            lights.Sort(start, count, Comparer<LightBounds>.Create((a, b) =>
            {
                float ca = axis == 0 ? a.Center.X : (axis == 1 ? a.Center.Y : a.Center.Z);
                float cb = axis == 0 ? b.Center.X : (axis == 1 ? b.Center.Y : b.Center.Z);
                int cmp = ca.CompareTo(cb);
                return cmp != 0 ? cmp : a.MoverIndex.CompareTo(b.MoverIndex);
            }));

            int leftCount = count / 2;
            if (leftCount <= 0) leftCount = 1;
            if (leftCount >= count) leftCount = count - 1;

            int childA = BuildNode(lights, start, leftCount);
            int childB = BuildNode(lights, start + leftCount, count - leftCount);

            Node left = Values[childA];
            Node right = Values[childB];
            Values[nodeIndex] = new Node
            {
                min = Vector3.Min(left.min, right.min),
                max = Vector3.Max(left.max, right.max),
                childA = (short)childA,
                childB = (short)childB,
                first = left.first,
                count = (short)(left.count + right.count),
                is_leaf = false
            };
            return nodeIndex;
        }

        private static bool TryComputeBounds(Movers.MOVER_DESCRIPTOR mvr, out Vector3 min, out Vector3 max)
        {
            min = default;
            max = default;
            Movers.MOVER_DESCRIPTOR.GPU_CONSTANTS.DEFERRED_GPU_CONSTANTS gpu;
            Movers.MOVER_DESCRIPTOR.RENDER_CONSTANTS.DEFERRED_PARAMS cpu;
            try
            {
                gpu = mvr.GPUConstants.GetAs<Movers.MOVER_DESCRIPTOR.GPU_CONSTANTS.DEFERRED_GPU_CONSTANTS>();
                cpu = mvr.RenderConstants.GetAs<Movers.MOVER_DESCRIPTOR.RENDER_CONSTANTS.DEFERRED_PARAMS>();
            }
            catch
            {
                return false;
            }
            if (gpu == null || cpu == null)
                return false;

            float radius = Math.Max(gpu.AttenuationEnd, 0.00001f);
            Vector3 position = mvr.Transform.Translation;

            if (cpu.Type == LightType.Strip)
            {
                float halfLength = Math.Max(gpu.OuterAngle, 0.0f);
                Vector3 endA = Vector3.Transform(new Vector3(0.0f, 0.0f, -halfLength), mvr.Transform);
                Vector3 endB = Vector3.Transform(new Vector3(0.0f, 0.0f, halfLength), mvr.Transform);
                Vector3 pad = new Vector3(radius);
                min = Vector3.Min(endA, endB) - pad;
                max = Vector3.Max(endA, endB) + pad;
                return true;
            }

            Vector3 extent = new Vector3(radius);
            min = position - extent;
            max = position + extent;
            return true;
        }

        private struct LightBounds
        {
            public int MoverIndex;
            public Vector3 Min;
            public Vector3 Max;
            public Vector3 Center;
        }
        #endregion

        #region STRUCTURES
        [Flags]
        public enum LightFeature : ushort
        {
            SoftDiffuse = 1 << 0,
            Specular = 1 << 1,
            Shadow = 1 << 2,
            Gobo = 1 << 3,
            Animated = 1 << 4,
            LensFlare = 1 << 5,
            NoClip = 1 << 6,
            DiffuseBias = 1 << 7,
            AreaLight = 1 << 8,
            SquareLight = 1 << 9,
            Flashlight = 1 << 10,
            PhysicalAttenuation = 1 << 11,
            DistanceMipSelectionGobo = 1 << 12,
            Volume = 1 << 13,
            NoAlphaLight = 1 << 14,
            HorizontalGoboFlip = 1 << 15
        };

        public enum LightType
        {
            Ambient,
            Strip,
            Point,
            Spot,
            Directional
        };

        public enum LightFadeType
        {
            None,
            Shadow,
            Light,
        };

        public class Node
        {
            public Vector3 min;
            public Vector3 max;
            public Int16 childA;
            public Int16 childB;
            public Int16 first;
            public Int16 count;
            public bool is_leaf;
        }

        public class DirectionalLight
        {
            public bool enabled;
            public Vector3 colour;
            public Vector3 direction;
            public LightFeature feature_flags;
        }
        #endregion
    }

    public static class LightTypeUtils
    {
        public static Lights.LightType AsLightType(this LIGHT_TYPE type)
        {
            switch (type)
            {
                case LIGHT_TYPE.OMNI:
                    return Lights.LightType.Point;
                case LIGHT_TYPE.SPOT:
                    return Lights.LightType.Spot;
                case LIGHT_TYPE.STRIP:
                    return Lights.LightType.Strip;
            }
            throw new Exception("Unexpected!");
        }
    }
}