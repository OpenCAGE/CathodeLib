using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

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
using System.Runtime.InteropServices;
#else
using System.Numerics;
using System.Runtime.InteropServices;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/MORPH_TARGET_DB.BIN
    /// </summary>
    public class MorphTargets : CathodeFile
    {
        public List<MorphTarget> Entries = new List<MorphTarget>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.SAVE | Implementation.LOAD;

        public MorphTargets(string path) : base(path) { }
        public MorphTargets(MemoryStream stream, string path = "") : base(stream, path) { }
        public MorphTargets(byte[] data, string path = "") : base(data, path) { }

        private List<MorphTarget> _writeList = new List<MorphTarget>();

        /* The name table is a dictionary in its own right, not a projection of the models. Retail
         * ships levels carrying names and NO models at all - 19 of the 33 production levels are that
         * shape - so rebuilding it from Entries wrote those back as an empty 12 byte file and lost
         * the table. Keep what was loaded, in its original order, and append only what is new. */
        private List<string> _names = new List<string>();

        ~MorphTargets()
        {
            Entries.Clear();
            _writeList.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int morphCount = reader.ReadInt32();
                reader.BaseStream.Position += 4;
                List<string> names = new List<string>(morphCount);
                for (int i = 0; i < morphCount; i++)
                {
                    names.Add(new string(reader.ReadChars(reader.ReadInt32())));
                }
                _names.Clear();
                _names.AddRange(names);

                int modelCount = reader.ReadInt32();
                for (int i = 0; i < modelCount; i++)
                {
                    MorphTarget model = new MorphTarget();
                    int targetCount = reader.ReadInt32();
                    for (int x = 0; x < targetCount; x++)
                    {
                        MorphTarget.Target target = new MorphTarget.Target(); 
                        target.Name = names[reader.ReadInt32()];

                        int vertCount = reader.ReadInt32();
                        for (int z = 0; z < vertCount; z++)
                            target.Points.Add(MorphTarget.Target.Point.Read(reader));

                        model.Targets.Add(target);
                    }
                    Entries.Add(model);
                }
            }
            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                List<string> names = new List<string>(_names);
                Dictionary<string, int> nameIndex = new Dictionary<string, int>(names.Count);
                for (int i = 0; i < names.Count; i++)
                    if (!nameIndex.ContainsKey(names[i])) nameIndex[names[i]] = i;
                for (int i = 0; i < Entries.Count; i++)
                    for (int x = 0; x < Entries[i].Targets.Count; x++)
                    {
                        string name = Entries[i].Targets[x].Name ?? "";
                        if (nameIndex.ContainsKey(name)) continue;
                        nameIndex[name] = names.Count;
                        names.Add(name);
                    }

                //The header counts a terminator per name that the payload does not actually write.
                int namesLength = 0;
                for (int i = 0; i < names.Count; i++)
                    namesLength += names[i].Length + 1;
                writer.Write(names.Count);
                writer.Write(namesLength);
                for (int i = 0; i < names.Count; i++)
                {
                    writer.Write(names[i].Length);
                    Utilities.WriteString(names[i], writer);
                }

                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].Targets.Count);
                    for (int x = 0; x < Entries[i].Targets.Count; x++)
                    {
                        writer.Write(nameIndex[Entries[i].Targets[x].Name ?? ""]);
                        writer.Write(Entries[i].Targets[x].Points.Count);
                        for (int z = 0; z < Entries[i].Targets[x].Points.Count; z++)
                        {
                            Entries[i].Targets[x].Points[z].Write(writer);
                        }
                    }
                }
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
        public int GetWriteIndex(MorphTarget morphTarget)
        {
            if (!_writeList.Contains(morphTarget)) return -1;
            return _writeList.IndexOf(morphTarget);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public MorphTarget GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public MorphTarget ImportEntry(MorphTarget morphTarget)
        {
            if (morphTarget == null)
                return null;

            var existing = Entries.FirstOrDefault(o => o == morphTarget);
            if (existing != null)
                return existing;

            MorphTarget newMorphTarget = morphTarget.Copy();
            Entries.Add(newMorphTarget);
            return newMorphTarget;
        }
        #endregion

        #region STRUCTURES
        public class MorphTarget : IEquatable<MorphTarget>
        {
            public List<Target> Targets = new List<Target>();

            public static bool operator ==(MorphTarget x, MorphTarget y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                return x.Equals(y);
            }

            public static bool operator !=(MorphTarget x, MorphTarget y)
            {
                return !(x == y);
            }

            public bool Equals(MorphTarget other)
            {
                if (other == null) return false;
                if (ReferenceEquals(this, other)) return true;

                if (Targets.Count != other.Targets.Count) return false;

                for (int i = 0; i < Targets.Count; i++)
                {
                    if (!Targets[i].Equals(other.Targets[i])) return false;
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as MorphTarget);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + Targets.Count.GetHashCode();
                    foreach (var target in Targets)
                    {
                        hash = hash * 23 + (target?.GetHashCode() ?? 0);
                    }
                    return hash;
                }
            }

            public class Target : IEquatable<Target>
            {
                public string Name;
                public List<Point> Points = new List<Point>();

                public static bool operator ==(Target x, Target y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return false;
                    return x.Equals(y);
                }

                public static bool operator !=(Target x, Target y)
                {
                    return !(x == y);
                }

                public bool Equals(Target other)
                {
                    if (other == null) return false;
                    if (ReferenceEquals(this, other)) return true;

                    if (Name != other.Name) return false;
                    if (Points.Count != other.Points.Count) return false;

                    for (int i = 0; i < Points.Count; i++)
                    {
                        if (!Points[i].Equals(other.Points[i])) return false;
                    }

                    return true;
                }

                public override bool Equals(object obj)
                {
                    return Equals(obj as Target);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int hash = 17;
                        hash = hash * 23 + (Name?.GetHashCode() ?? 0);
                        hash = hash * 23 + Points.Count.GetHashCode();
                        foreach (var point in Points)
                        {
                            hash = hash * 23 + (point?.GetHashCode() ?? 0);
                        }
                        return hash;
                    }
                }

                public class Point : IEquatable<Point>
                {
                    private const float POS_EXTENTS = 0.04f;
                    private const float NORMAL_EXTENTS = 0.5f;

                    public int Index;
                    public Vector3 PositionOffset;
                    public Vector3 NormalOffset;

                    [StructLayout(LayoutKind.Sequential, Pack = 1)]
                    private struct Packed
                    {
                        public byte u, v, nx, ny;
                        public byte x, y, z, nz;
                    }

                    public static Point Read(BinaryReader reader)
                    {
                        Packed packed = Utilities.Consume<Packed>(reader);
                        return new Point
                        {
                            Index = packed.u + (packed.v * 256),
                            PositionOffset = new Vector3(
                                U8ToF32(packed.x, -POS_EXTENTS, POS_EXTENTS),
                                U8ToF32(packed.y, -POS_EXTENTS, POS_EXTENTS),
                                U8ToF32(packed.z, -POS_EXTENTS, POS_EXTENTS)),
                            NormalOffset = new Vector3(
                                U8ToF32(packed.nx, -NORMAL_EXTENTS, NORMAL_EXTENTS),
                                U8ToF32(packed.ny, -NORMAL_EXTENTS, NORMAL_EXTENTS),
                                U8ToF32(packed.nz, -NORMAL_EXTENTS, NORMAL_EXTENTS))
                        };
                    }

                    public void Write(BinaryWriter writer)
                    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                        float px = PositionOffset.x, py = PositionOffset.y, pz = PositionOffset.z;
                        float nx = NormalOffset.x, ny = NormalOffset.y, nz = NormalOffset.z;
#else
                        float px = PositionOffset.X, py = PositionOffset.Y, pz = PositionOffset.Z;
                        float nx = NormalOffset.X, ny = NormalOffset.Y, nz = NormalOffset.Z;
#endif
                        Utilities.Write(writer, new Packed
                        {
                            u = (byte)(Index % 256),
                            v = (byte)(Index / 256),
                            x = F32ToU8(px, -POS_EXTENTS, POS_EXTENTS),
                            y = F32ToU8(py, -POS_EXTENTS, POS_EXTENTS),
                            z = F32ToU8(pz, -POS_EXTENTS, POS_EXTENTS),
                            nx = F32ToU8(nx, -NORMAL_EXTENTS, NORMAL_EXTENTS),
                            ny = F32ToU8(ny, -NORMAL_EXTENTS, NORMAL_EXTENTS),
                            nz = F32ToU8(nz, -NORMAL_EXTENTS, NORMAL_EXTENTS)
                        });
                    }

                    private static float U8ToF32(byte value, float min, float max)
                    {
                        return min + (value / 255.0f) * (max - min);
                    }

                    private static byte F32ToU8(float value, float min, float max)
                    {
                        float t = (value - min) / (max - min) * 255.0f;
                        if (t <= 0.0f) return 0;
                        if (t >= 255.0f) return 255;
                        return (byte)(t + 0.5f);
                    }

                    public static bool operator ==(Point x, Point y)
                    {
                        if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                        if (ReferenceEquals(y, null)) return false;
                        return x.Equals(y);
                    }

                    public static bool operator !=(Point x, Point y)
                    {
                        return !(x == y);
                    }

                    public bool Equals(Point other)
                    {
                        if (other == null) return false;
                        if (ReferenceEquals(this, other)) return true;

                        if (Index != other.Index) return false;
                        if (PositionOffset != other.PositionOffset) return false;
                        if (NormalOffset != other.NormalOffset) return false;

                        return true;
                    }

                    public override bool Equals(object obj)
                    {
                        return Equals(obj as Point);
                    }

                    public override int GetHashCode()
                    {
                        unchecked
                        {
                            int hash = 17;
                            hash = hash * 23 + Index.GetHashCode();
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                            hash = hash * 23 + PositionOffset.x.GetHashCode();
                            hash = hash * 23 + PositionOffset.y.GetHashCode();
                            hash = hash * 23 + PositionOffset.z.GetHashCode();
                            hash = hash * 23 + NormalOffset.x.GetHashCode();
                            hash = hash * 23 + NormalOffset.y.GetHashCode();
                            hash = hash * 23 + NormalOffset.z.GetHashCode();
#else
                            hash = hash * 23 + PositionOffset.X.GetHashCode();
                            hash = hash * 23 + PositionOffset.Y.GetHashCode();
                            hash = hash * 23 + PositionOffset.Z.GetHashCode();
                            hash = hash * 23 + NormalOffset.X.GetHashCode();
                            hash = hash * 23 + NormalOffset.Y.GetHashCode();
                            hash = hash * 23 + NormalOffset.Z.GetHashCode();
#endif
                            return hash;
                        }
                    }
                }
            }
        }
        #endregion
    }
}