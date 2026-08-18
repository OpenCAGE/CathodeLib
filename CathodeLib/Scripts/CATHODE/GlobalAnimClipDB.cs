using CATHODE.Animations;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> ANIM_CLIP_DB.BIN
    ///
    /// The top level index over the per-character clip databases: which section holds which
    /// character's animations, what each section needs loaded alongside it, the parametric blend
    /// sets (aim, look-at, locomotion) shared across characters, and which clip DBs depend on
    /// which others.
    /// </summary>
    public class GlobalAnimClipDB : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE | Implementation.SAVE;

        public List<ClipDbSection> ClipDbSections = new List<ClipDbSection>();
        public List<DependencyRange> DependencyMap = new List<DependencyRange>();
        public List<uint> SectionDependencyList = new List<uint>();

        /// <summary>Clip DBs that must be loaded before another one.</summary>
        public List<ParentDependency> ParentDependencies = new List<ParentDependency>();

        /// <summary>Parametric blend sets, in the order they are stored.</summary>
        public List<BlendSet> BlendSets = new List<BlendSet>();

        /* Blend set names only exist in the debug string DB, so pass it in to get readable names
         * (and to spot where one blend set record ends and the next begins). */
        public GlobalAnimClipDB(string path, AnimationStrings strings, AnimationStrings debugStrings = null) : base(path)
        {
            _strings = strings;
            _debug = debugStrings;
            _loaded = Load();
        }
        public GlobalAnimClipDB(MemoryStream stream, AnimationStrings strings, string path = "", AnimationStrings debugStrings = null) : base(stream, path)
        {
            _strings = strings;
            _debug = debugStrings;
            _loaded = Load(stream);
        }
        public GlobalAnimClipDB(byte[] data, AnimationStrings strings, string path = "", AnimationStrings debugStrings = null) : base(data, path)
        {
            _strings = strings;
            _debug = debugStrings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private AnimationStrings _strings;
        private AnimationStrings _debug;

        /* Both DBs hash the same way, so a name found in either writes back to the same ID */
        private string Name(uint id)
        {
            if (_strings.Entries.TryGetValue(id, out string value)) return value;
            if (_debug != null && _debug.Entries.TryGetValue(id, out string debug)) return debug;
            return id.ToString();
        }

        private bool IsKnown(uint id)
        {
            return id != 0 && (_strings.Entries.ContainsKey(id) || (_debug != null && _debug.Entries.ContainsKey(id)));
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null)
                return false;

            ClipDbSections.Clear();
            DependencyMap.Clear();
            SectionDependencyList.Clear();
            BlendSets.Clear();
            ParentDependencies.Clear();
            _parentOrder.Clear();

            using (BinaryReader reader = new BinaryReader(stream))
            {
                ClipDbSections = HashTable.Read(reader, (r, n) => new ClipDbSection
                {
                    Name = n,
                    SectionName = _strings.GetString(r.ReadUInt32()),
                    SectionIndex = r.ReadInt32(),
                }, _strings);

                DependencyMap = HashTable.Read(reader, (r, n) => new DependencyRange
                {
                    Name = n,
                    FirstEntryIndex = r.ReadUInt32(),
                    EntryCount = r.ReadUInt32(),
                }, _strings);

                //One flat list the ranges above index into, with its own count
                int dependencyCount = reader.ReadInt32();
                if (dependencyCount < 0 || (long)dependencyCount * 4 > reader.BaseStream.Length - reader.BaseStream.Position)
                    return false;
                for (int i = 0; i < dependencyCount; i++)
                    SectionDependencyList.Add(reader.ReadUInt32());

                /* Blend sets: a lookup keyed on "CHARACTER_VARIANT\NAME" pointing at records that
                 * follow, then the records themselves back to back. */
                _blendSetLookupSize = reader.ReadInt32();
                _blendSetOrder = new List<int>();
                _blendSetNames = HashTable.Read(reader, (r, n) => new KeyValuePair<string, int>(n, r.ReadInt32()), _strings, _blendSetOrder);

                for (int i = 0; i < _blendSetNames.Count; i++)
                    ReadBlendSet(reader);

                //One last lookup, one entry per character clip DB
                ParentDependencies = HashTable.Read(reader, (r, n) => new ParentDependency
                {
                    Name = n,
                    Parent = Name(r.ReadUInt32()),
                }, _strings, _parentOrder);

                return true;
            }
        }

        /* Records are variable length and carry no size field, but everything needed to work the
         * size out is in the fixed 65 byte header. */
        private void ReadBlendSet(BinaryReader reader)
        {
            BlendSet set = new BlendSet();
            set.Version = reader.ReadInt64();
            set.Name = Name(reader.ReadUInt32());
            set.AnimSet = Name(reader.ReadUInt32());
            set.AnimSetContext = Name(reader.ReadUInt32());

            set.Dimensions = reader.ReadByte();
            int clipCount = reader.ReadByte();
            int instanceCount = reader.ReadByte();
            set.CellCount = reader.ReadInt16();

            set.CellOrigin = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            set.CellUnit = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            set.CellStride = reader.ReadInt16();
            set.CellDepth = reader.ReadInt16();

            set.BlendPropertyX = Name(reader.ReadUInt32());
            set.BlendPropertyY = Name(reader.ReadUInt32());
            set.BlendPropertyZ = Name(reader.ReadUInt32());

            for (int i = 0; i < clipCount; i++)
                set.Clips.Add(new BlendClip
                {
                    Leading = reader.ReadBytes(8),
                    Name = Name(reader.ReadUInt32()),
                    Trailing = reader.ReadBytes(4),
                });

            set.InstanceProperties = new float[instanceCount * set.Dimensions];
            for (int i = 0; i < set.InstanceProperties.Length; i++)
                set.InstanceProperties[i] = reader.ReadSingle();

            int influences = set.InfluencesPerVertex;
            for (int i = 0; i < set.VertexCount; i++)
            {
                BlendVertex vertex = new BlendVertex
                {
                    Instances = reader.ReadBytes(influences),
                    Weights = reader.ReadBytes(influences),
                };
                set.Vertices.Add(vertex);
            }

            set.PlaySpeeds = new float[instanceCount];
            for (int i = 0; i < instanceCount; i++) set.PlaySpeeds[i] = reader.ReadSingle();
            set.InstanceToClip = reader.ReadBytes(instanceCount);

            set.Durations = new float[clipCount];
            for (int i = 0; i < clipCount; i++) set.Durations[i] = reader.ReadSingle();
            set.Mirrored = new bool[clipCount];
            for (int i = 0; i < clipCount; i++) set.Mirrored[i] = reader.ReadByte() != 0;

            BlendSets.Add(set);
        }

        private void WriteBlendSet(BinaryWriter writer, BlendSet set)
        {
            writer.Write(set.Version);
            writer.Write(_strings.GetID(set.Name));
            writer.Write(_strings.GetID(set.AnimSet));
            writer.Write(_strings.GetID(set.AnimSetContext));

            writer.Write(set.Dimensions);
            writer.Write((byte)set.Clips.Count);
            writer.Write((byte)set.PlaySpeeds.Length);
            writer.Write(set.CellCount);

            writer.Write(set.CellOrigin.X); writer.Write(set.CellOrigin.Y); writer.Write(set.CellOrigin.Z);
            writer.Write(set.CellUnit.X); writer.Write(set.CellUnit.Y); writer.Write(set.CellUnit.Z);
            writer.Write(set.CellStride);
            writer.Write(set.CellDepth);

            writer.Write(_strings.GetID(set.BlendPropertyX));
            writer.Write(_strings.GetID(set.BlendPropertyY));
            writer.Write(_strings.GetID(set.BlendPropertyZ));

            for (int i = 0; i < set.Clips.Count; i++)
            {
                writer.Write(set.Clips[i].Leading);
                writer.Write(_strings.GetID(set.Clips[i].Name));
                writer.Write(set.Clips[i].Trailing);
            }
            for (int i = 0; i < set.InstanceProperties.Length; i++) writer.Write(set.InstanceProperties[i]);

            for (int i = 0; i < set.Vertices.Count; i++)
            {
                writer.Write(set.Vertices[i].Instances);
                writer.Write(set.Vertices[i].Weights);
            }

            for (int i = 0; i < set.PlaySpeeds.Length; i++) writer.Write(set.PlaySpeeds[i]);
            writer.Write(set.InstanceToClip);
            for (int i = 0; i < set.Durations.Length; i++) writer.Write(set.Durations[i]);
            for (int i = 0; i < set.Mirrored.Length; i++) writer.Write((byte)(set.Mirrored[i] ? 1 : 0));
        }

        override protected bool SaveInternal()
        {
            byte[] content = ToBytes();
            if (content == null) return false;
            File.WriteAllBytes(_filepath, content);
            return true;
        }

        /// <summary>
        /// Serialise back to the format stored in ANIMATION.PAK.
        /// </summary>
        public byte[] ToBytes()
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                HashTable.Write(writer, ClipDbSections, x => x.Name, (w, x) =>
                {
                    w.Write(_strings.GetID(x.SectionName));
                    w.Write(x.SectionIndex);
                }, _strings);

                HashTable.Write(writer, DependencyMap, x => x.Name, (w, x) =>
                {
                    w.Write(x.FirstEntryIndex);
                    w.Write(x.EntryCount);
                }, _strings);

                writer.Write(SectionDependencyList.Count);
                for (int i = 0; i < SectionDependencyList.Count; i++)
                    writer.Write(SectionDependencyList[i]);

                writer.Write(_blendSetLookupSize);
                HashTable.Write(writer, _blendSetNames, x => x.Key, (w, x) => w.Write(x.Value), _strings, _blendSetOrder);

                for (int i = 0; i < BlendSets.Count; i++)
                    WriteBlendSet(writer, BlendSets[i]);

                HashTable.Write(writer, ParentDependencies, x => x.Name, (w, x) => w.Write(_strings.GetID(x.Parent)), _strings, _parentOrder);

                return stream.ToArray();
            }
        }
        #endregion

        #region ACCESSORS
        /// <summary>
        /// The sections a given section needs loaded alongside it.
        /// </summary>
        public List<uint> GetDependencies(DependencyRange range)
        {
            List<uint> result = new List<uint>();
            if (range == null) return result;
            for (uint i = 0; i < range.EntryCount; i++)
            {
                int at = (int)(range.FirstEntryIndex + i);
                if (at >= 0 && at < SectionDependencyList.Count) result.Add(SectionDependencyList[at]);
            }
            return result;
        }
        #endregion

        #region STRUCTURES
        public class ClipDbSection
        {
            public string Name = "";
            public string SectionName = "";
            public int SectionIndex;

            public override string ToString() => Name;
        }

        public class DependencyRange
        {
            public string Name = "";

            /// <summary>Offset into <see cref="SectionDependencyList"/>.</summary>
            public uint FirstEntryIndex;
            public uint EntryCount;

            public override string ToString() => Name;
        }

        /// <summary>
        /// A parametric blend set: an animation blended from several clips by one to three
        /// driving parameters (aim angles, movement speed, and so on).
        /// </summary>
        public class BlendSet
        {
            public long Version;

            /// <summary>Blend name, e.g. "aim_sway" or "locomotion_cycle_blend".</summary>
            public string Name = "";

            /// <summary>Anim set the blend belongs to, e.g. "HUMAN" or "ALIEN".</summary>
            public string AnimSet = "";

            /// <summary>Context within the anim set, e.g. a weapon, or "none".</summary>
            public string AnimSetContext = "";

            /// <summary>How many properties drive the blend: 1, 2 or 3.</summary>
            public byte Dimensions = 2;

            /// <summary>The clips being blended between.</summary>
            public List<BlendClip> Clips = new List<BlendClip>();

            public string BlendPropertyX = "";
            public string BlendPropertyY = "";
            public string BlendPropertyZ = "";

            /// <summary>Corner of the sampled space, in blend property units.</summary>
            public Vector3 CellOrigin;

            /// <summary>Size of one cell, in blend property units.</summary>
            public Vector3 CellUnit;

            public short CellCount;
            public short CellStride;
            public short CellDepth;

            /// <summary>Each instance's position in the blend space - <see cref="Dimensions"/> values per instance.</summary>
            public float[] InstanceProperties = new float[0];

            /// <summary>Baked lookup, one entry per grid vertex, in x then y then z order.</summary>
            public List<BlendVertex> Vertices = new List<BlendVertex>();

            /// <summary>Playback rate per instance.</summary>
            public float[] PlaySpeeds = new float[0];

            /// <summary>Which clip each instance plays.</summary>
            public byte[] InstanceToClip = new byte[0];

            /// <summary>Length of each clip.</summary>
            public float[] Durations = new float[0];

            /// <summary>Whether each clip plays mirrored.</summary>
            public bool[] Mirrored = new bool[0];

            public int XCellCount => CellStride;
            public int ZCellCount => CellDepth;
            public int YCellCount => Dimensions <= 1 || CellStride == 0 ? 0
                : (ZCellCount != 0 ? (CellCount / CellStride) / CellDepth : CellCount / CellStride);

            /// <summary>Grid vertices are one more than cells along each axis.</summary>
            public int VertexCount => (XCellCount + 1) * (YCellCount + 1) * (ZCellCount + 1);

            /// <summary>Corners of a cell: 2 for 1D, 4 for 2D, 8 for 3D.</summary>
            public int InfluencesPerVertex => 1 << Dimensions;

            public override string ToString() => AnimSet + (AnimSetContext == "none" ? "" : "_" + AnimSetContext) + "\\" + Name;
        }

        /// <summary>
        /// One clip taking part in a blend set.
        /// </summary>
        public class BlendClip
        {
            /// <summary>Clip name, matching an entry in the owning character's x_ANIM_CLIP_DB.</summary>
            public string Name = "";

            /// <summary>Eight bytes before the name.</summary>
            public byte[] Leading = new byte[8];

            /// <summary>Four bytes after the name.</summary>
            public byte[] Trailing = new byte[4];

            public override string ToString() => Name;
        }

        /// <summary>
        /// One sample of the baked blend: which instances contribute at this point in the space,
        /// and how much. An instance index of 255 means the slot is unused.
        /// </summary>
        public class BlendVertex
        {
            public byte[] Instances = new byte[0];
            public byte[] Weights = new byte[0];

            public override string ToString() => string.Join(", ", Instances.Select((x, i) => x == 255 ? "-" : x + ":" + Weights[i]));
        }

        /// <summary>
        /// A clip DB that has to be loaded before another one can be.
        /// </summary>
        public class ParentDependency
        {
            public string Name = "";
            public string Parent = "";

            public override string ToString() => Name + " <- " + Parent;
        }
        #endregion

        /* The lookup keys blend sets as "ANIMSET_CONTEXT\NAME" and its value is the record ordinal.
         * Kept as loaded so a save reproduces the file exactly. */
        private int _blendSetLookupSize;
        private List<KeyValuePair<string, int>> _blendSetNames = new List<KeyValuePair<string, int>>();
        private List<int> _blendSetOrder = new List<int>();
        private List<int> _parentOrder = new List<int>();
    }
}
