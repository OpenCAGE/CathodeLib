using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using static System.Collections.Specialized.BitVector32;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> ANIM_CLIP_DB.BIN
    /// </summary>
    public class GlobalAnimClipDB : CathodeFile
    {
        // Global ANIM_CLIP_DB.BIN structure
        public List<ClipDbSectionTuple> ClipDbSections = new List<ClipDbSectionTuple>();
        public List<DependencyMapTuple> DependencyMap = new List<DependencyMapTuple>();
        public List<uint> SectionDependencyList = new List<uint>();
        public List<BlendSetEntry> BlendSets = new List<BlendSetEntry>();
        public List<ParentDataEntry> ParentData = new List<ParentDataEntry>();
        
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;

        public GlobalAnimClipDB(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public GlobalAnimClipDB(MemoryStream stream, AnimationStrings strings, string path) : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public GlobalAnimClipDB(byte[] data, AnimationStrings strings, string path) : base(data, path)
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
            if (_strings == null || _filepath == null || _filepath == "")
                return false;

            using (BinaryReader reader = new BinaryReader(stream))
            {
                // Read clip database sections hash table
                ClipDbSections = ReadHashTable<ClipDbSectionTuple>(reader, (r, n) => new ClipDbSectionTuple
                {
                    Name = n,
                    SectionName = _strings.GetString(r.ReadUInt32()),
                    SectionIndex = r.ReadInt32()
                });

                // Read dependency map hash table
                DependencyMap = ReadHashTable<DependencyMapTuple>(reader, (r, n) => new DependencyMapTuple
                {
                    Name = n,
                    FirstEntryIndex = r.ReadUInt32(),
                    EntryCount = r.ReadUInt32()
                });

                // Read section dependency list
                uint totalDependencies = 0;
                foreach (var dep in DependencyMap)
                {
                    totalDependencies += dep.EntryCount;
                }

                for (uint i = 0; i < totalDependencies; i++)
                {
                    SectionDependencyList.Add(reader.ReadUInt32());
                }

                // Read blend sets hash table
                BlendSets = ReadHashTable<BlendSetEntry>(reader, (r, n) => new BlendSetEntry
                {
                    Name = n,
                    Filename = _strings.GetString(r.ReadUInt32())
                });

                // Read parent data hash table
                ParentData = ReadHashTable<ParentDataEntry>(reader, (r, n) => new ParentDataEntry
                {
                    Name = n,
                    Child = _strings.GetString(r.ReadUInt32()),
                    Parent = _strings.GetString(r.ReadUInt32())
                });

                return true;
            }
        }

        private List<T> ReadHashTable<T>(BinaryReader reader, Func<BinaryReader, string, T> itemReader)
        {
            var result = new List<T>();
            
            int hashTableSize = reader.ReadInt32();
            int usedSize = reader.ReadInt32();
            
            if (hashTableSize != usedSize)
                return result;

            string[] names = new string[hashTableSize];
            for (int i = 0; i < hashTableSize; i++)
            {
                uint hash = reader.ReadUInt32(); 
                int index = reader.ReadInt32(); 
                names[index] = _strings.GetString(hash);
            }

            for (int i = 0; i < hashTableSize; i++)
            {
                result.Add(itemReader(reader, names[i]));
            }

            return result;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                // Write clip database sections hash table
                WriteHashTable(writer, ClipDbSections, (w, item) => {
                    w.Write(_strings.GetID(item.SectionName));
                    w.Write(item.SectionIndex);
                });

                // Write dependency map hash table
                WriteHashTable(writer, DependencyMap, (w, item) => {
                    w.Write(item.FirstEntryIndex);
                    w.Write(item.EntryCount);
                });

                // Write section dependency list
                foreach (var dep in SectionDependencyList)
                {
                    writer.Write(dep);
                }

                // Write blend sets hash table
                WriteHashTable(writer, BlendSets, (w, item) => {
                    w.Write(_strings.GetID(item.Filename));
                });

                // Write parent data hash table
                WriteHashTable(writer, ParentData, (w, item) => {
                    w.Write(_strings.GetID(item.Child));
                    w.Write(_strings.GetID(item.Parent));
                });

                return true;
            }
        }

        private void WriteHashTable<T>(BinaryWriter writer, List<T> data, Action<BinaryWriter, T> itemWriter)
        {
            writer.Write(data.Count);
            writer.Write(data.Count);

            // Write hash table entries
            for (int i = 0; i < data.Count; i++)
            {
                writer.Write(Utilities.AnimationHashedString($"item_{i}")); // Generate hash
                writer.Write(i);
            }

            // Write data entries
            foreach (var item in data)
            {
                itemWriter(writer, item);
            }
        }
        #endregion

        #region STRUCTURES
        public struct ClipDbSectionTuple
        {
            public string Name;
            public string SectionName;
            public int SectionIndex;
        }

        public struct DependencyMapTuple
        {
            public string Name;
            public uint FirstEntryIndex;
            public uint EntryCount;
        }

        public struct BlendSetEntry
        {
            public string Name;
            public string Filename;
        }

        public struct ParentDataEntry
        {
            public string Name;
            public string Child;
            public string Parent;
        }
        #endregion
    }
}
