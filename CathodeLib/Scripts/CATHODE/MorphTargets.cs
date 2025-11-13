using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
using static CATHODE.Collisions;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/MORPH_TARGET_DB.BIN
    /// </summary>
    public class MorphTargets : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.NONE;

        public MorphTargets(string path) : base(path) { }
        public MorphTargets(MemoryStream stream, string path = "") : base(stream, path) { }
        public MorphTargets(byte[] data, string path = "") : base(data, path) { }

        private List<Entry> _writeList = new List<Entry>();

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int namesCount = reader.ReadInt32();
                reader.BaseStream.Position += 4;
                for (int i = 0; i < namesCount; i++)
                {
                    Entries.Add(new Entry { Name = reader.ReadChars(reader.ReadInt32()).ToString() });
                }

                //TODO: need to actually save this data so we can rewrite it 
                int animSetCount = reader.ReadInt32();
                for (int i = 0; i < animSetCount; i++)
                {
                    int morphCount = reader.ReadInt32();
                    for (int x = 0; x < morphCount; x++)
                    {
                        int morphNameID = reader.ReadInt32();
                        int vertCount = reader.ReadInt32();
                        for (int z = 0; z < vertCount * 8; z++) //8 is the size of the vertex
                            reader.ReadByte();
                    }
                }
            }
            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            int stringLength = Entries.Count;
            for (int i = 0; i < Entries.Count; i++)
                stringLength += Entries[i].Name.Length;

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count);
                writer.Write(stringLength);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].Name.Length);
                    Utilities.WriteString(Entries[i].Name, writer);
                }
            }
            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the current BIN index for a submesh (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(Entry morphTarget)
        {
            if (!_writeList.Contains(morphTarget)) return -1;
            return _writeList.IndexOf(morphTarget);
        }

        /// <summary>
        /// Get a submesh by its current BIN index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public Entry GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public string Name;
        }
        #endregion
    }
}