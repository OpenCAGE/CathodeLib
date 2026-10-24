using CATHODE.Animations;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> ANIM_SYS/SKELE/DB.BIN
    ///
    /// The index of every skeleton in the game, and every retargeting mapping between them.
    /// Both point at files in SKELE/SK and SKELE/MAPS by hashed name.
    /// </summary>
    public class SkeletonDB : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE | Implementation.SAVE;

        public List<SkeletonEntry> Skeletons = new List<SkeletonEntry>();
        public List<MappingEntry> Mappings = new List<MappingEntry>();

        public SkeletonDB(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public SkeletonDB(MemoryStream stream, AnimationStrings strings, string path = "") : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public SkeletonDB(byte[] data, AnimationStrings strings, string path = "") : base(data, path)
        {
            _strings = strings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private AnimationStrings _strings;

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null)
                return false;

            Skeletons.Clear();
            Mappings.Clear();

            using (BinaryReader reader = new BinaryReader(stream))
            {
                Skeletons = HashTable.Read(reader, (r, n) => new SkeletonEntry
                {
                    Name = n,
                    SourcePath = _strings.GetString(r.ReadUInt32()),
                }, _strings);

                /* Same shape as a normal hash table, but keyed on a pair of skeleton names
                 * rather than one, so it can't go through the shared helper. */
                int tableSize = reader.ReadInt32();
                int usedSize = reader.ReadInt32();
                if (tableSize != usedSize)
                    return false;

                MappingEntry[] mappings = new MappingEntry[tableSize];
                for (int i = 0; i < tableSize; i++)
                {
                    string skeletonA = _strings.GetString(reader.ReadUInt32());
                    string skeletonB = _strings.GetString(reader.ReadUInt32());
                    int index = reader.ReadInt32();
                    reader.BaseStream.Position += 4; //unused - always zero in retail data
                    if (index < 0 || index >= tableSize)
                        return false;
                    mappings[index] = new MappingEntry { SkeletonA = skeletonA, SkeletonB = skeletonB };
                }
                for (int i = 0; i < tableSize; i++)
                    mappings[i].Filename = _strings.GetString(reader.ReadUInt32());

                Mappings = mappings.ToList();

                return true;
            }
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
                HashTable.Write(writer, Skeletons, x => x.Name, (w, x) => w.Write(_strings.GetID(x.SourcePath)), _strings);

                writer.Write(Mappings.Count);
                writer.Write(Mappings.Count);

                //Keyed on the target skeleton first, then the source - both descending
                var pairs = new List<Tuple<uint, uint, int>>(Mappings.Count);
                for (int i = 0; i < Mappings.Count; i++)
                    pairs.Add(new Tuple<uint, uint, int>(_strings.GetID(Mappings[i].SkeletonA), _strings.GetID(Mappings[i].SkeletonB), i));
                pairs.Sort((a, b) => a.Item2 != b.Item2 ? b.Item2.CompareTo(a.Item2) : b.Item1.CompareTo(a.Item1));

                for (int i = 0; i < pairs.Count; i++)
                {
                    writer.Write(pairs[i].Item1);
                    writer.Write(pairs[i].Item2);
                    writer.Write(pairs[i].Item3);
                    writer.Write(0);
                }
                for (int i = 0; i < Mappings.Count; i++)
                    writer.Write(_strings.GetID(Mappings[i].Filename));

                return stream.ToArray();
            }
        }
        #endregion

        #region ACCESSORS
        /// <summary>
        /// The PAK entry name holding a skeleton's Havok data, e.g. "DATA\ANIM_SYS\SKELE\SK\4284333439".
        /// The entry is named after the hash of the skeleton's name, not its source path.
        /// </summary>
        public string GetSkeletonPath(SkeletonEntry skeleton, bool sixtyFourBit = false)
        {
            if (skeleton == null) return null;
            return @"DATA\ANIM_SYS\SKELE\" + (sixtyFourBit ? "SK64" : "SK") + @"\" + _strings.GetID(skeleton.Name);
        }

        /// <summary>
        /// The PAK entry name holding a mapping's Havok data.
        /// </summary>
        public string GetMappingPath(MappingEntry mapping, bool sixtyFourBit = false)
        {
            if (mapping == null) return null;
            return @"DATA\ANIM_SYS\SKELE\" + (sixtyFourBit ? "MAPS64" : "MAPS") + @"\" + _strings.GetID(mapping.Filename);
        }

        public SkeletonEntry GetSkeleton(string name)
        {
            return Skeletons.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        #endregion

        #region STRUCTURES
        public class SkeletonEntry
        {
            /// <summary>Skeleton name, e.g. "MALE" or "ALIEN". Its hash names the SKELE/SK entry.</summary>
            public string Name = "";

            /// <summary>Where the skeleton was authored, on CA's build machine. Informational only.</summary>
            public string SourcePath = "";

            public override string ToString() => Name;
        }

        public class MappingEntry
        {
            public string SkeletonA = "";
            public string SkeletonB = "";

            /// <summary>Name whose hash is the SKELE/MAPS entry holding the Havok data.</summary>
            public string Filename = "";

            public override string ToString() => SkeletonA + " -> " + SkeletonB;
        }
        #endregion
    }
}
