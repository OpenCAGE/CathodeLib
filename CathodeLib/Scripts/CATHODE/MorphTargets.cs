using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
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

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int morphCount = reader.ReadInt32();
                reader.BaseStream.Position += 4;
                List<string> names = new List<string>();
                for (int i = 0; i < morphCount; i++)
                {
                    names.Add(new string(reader.ReadChars(reader.ReadInt32())));
                }

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
                            target.Points.Add(Utilities.Consume<MorphTarget.Target.Point>(reader));

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

                List<string> names;
                int namesLength = 0;
                {
                    HashSet<string> namesHashSet = new HashSet<string>();
                    for (int i = 0; i < Entries.Count; i++)
                        for (int x = 0; x < Entries[i].Targets.Count; x++)
                            if (namesHashSet.Add(Entries[i].Targets[x].Name))
                                namesLength += Entries[i].Targets[x].Name.Length + 1;
                    names = namesHashSet.ToList();
                }
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
                        writer.Write(names.IndexOf(Entries[i].Targets[x].Name));
                        writer.Write(Entries[i].Targets[x].Points.Count);
                        for (int z = 0; z < Entries[i].Targets[x].Points.Count; z++)
                        {
                            Utilities.Write(writer, Entries[i].Targets[x].Points[z]);
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
        public MorphTarget AddEntry(MorphTarget morphTarget)
        {
            if (morphTarget == null)
                return null;

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

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class Point : IEquatable<Point>
                {
                    //TODO: convert this into indices, pos_offsets, normal_offsets
                    //See how this is calculated in mint_rigid.cpp -> MorphTargetArray& morphs = m_morph_targets;

                    public byte u, v, nx, ny;
                    public byte x, y, z, nz;

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

                        if (u != other.u) return false;
                        if (v != other.v) return false;
                        if (nx != other.nx) return false;
                        if (ny != other.ny) return false;
                        if (x != other.x) return false;
                        if (y != other.y) return false;
                        if (z != other.z) return false;
                        if (nz != other.nz) return false;

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
                            hash = hash * 23 + u.GetHashCode();
                            hash = hash * 23 + v.GetHashCode();
                            hash = hash * 23 + nx.GetHashCode();
                            hash = hash * 23 + ny.GetHashCode();
                            hash = hash * 23 + x.GetHashCode();
                            hash = hash * 23 + y.GetHashCode();
                            hash = hash * 23 + z.GetHashCode();
                            hash = hash * 23 + nz.GetHashCode();
                            return hash;
                        }
                    }

                    /*
                        for( card32 i = 0; i < num_verts; ++i ) {
				            Point pt;
				            memset( &pt, 0, sizeof( pt ) );
				            pt.u = card8( morph.indices[ i ] % 256 );
					        pt.v = card8( morph.indices[ i ] / 256 );
				            const auto pos_off = morph.pos_offsets[ i ];
				            const auto normal_off = morph.normal_offsets[ i ];

				            const float pos_extents = 0.04f;
				            pt.x = f32_to_u8( pos_off.x, -pos_extents, pos_extents );
				            pt.y = f32_to_u8( pos_off.y, -pos_extents, pos_extents );
				            pt.z = f32_to_u8( pos_off.z, -pos_extents, pos_extents );

				            const float normal_extents = 0.5f;
				            pt.nx = f32_to_u8( normal_off.x, -normal_extents, normal_extents );
				            pt.ny = f32_to_u8( normal_off.y, -normal_extents, normal_extents );
				            pt.nz = f32_to_u8( normal_off.z, -normal_extents, normal_extents );

				            file_write( &pt, sizeof( pt ), "cccccccc", file );
			            } 
                    */
                }
            }
        }
        #endregion
    }
}