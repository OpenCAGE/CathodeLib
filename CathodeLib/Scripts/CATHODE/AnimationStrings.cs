using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CATHODE.Scripting;
using CathodeLib;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> ANIM_STRING_DB.BIN, ANIM_STRING_DB_DEBUG.BIN
    /// </summary>
    public class AnimationStrings : CathodeFile
    {
        public Dictionary<uint, string> Entries = new Dictionary<uint, string>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public AnimationStrings(string path) : base(path) { }
        public AnimationStrings(MemoryStream stream, string path = "") : base(stream, path) { }
        public AnimationStrings(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int entryCount = reader.ReadInt32();
                int stringCount = reader.ReadInt32();
                Entry[] entries = Utilities.ConsumeArray<Entry>(reader, entryCount);
                int[] stringOffsets = Utilities.ConsumeArray<int>(reader, stringCount);

                int baseline = (entryCount * 4 * 2) + 8 + (stringCount * 4);

                List<string> strings = new List<string>();
                for (int i = 0; i < stringCount; i++)
                    strings.Add(Utilities.ReadString(reader, stringOffsets[i] + baseline, false));
                for (int i = 0; i < entries.Length; i++) 
                    Entries.Add(entries[i].StringID, strings[entries[i].StringIndex]);
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count);
                writer.Write(Entries.Count);
                int count = 0;
                foreach (KeyValuePair<uint, string> value in Entries)
                {
                    writer.Write(value.Key);
                    writer.Write(count);
                    count++;
                }
                int baseline = (Entries.Count * 4 * 2) + 8 + (Entries.Count * 4);
                writer.BaseStream.Position = baseline;
                List<int> stringOffsets = new List<int>();
                foreach (KeyValuePair<uint, string> value in Entries)
                {
                    stringOffsets.Add((int)writer.BaseStream.Position - baseline);
                    Utilities.WriteString(value.Value, writer, true);
                }
                writer.BaseStream.Position = (Entries.Count * 4 * 2) + 8;
                for (int i = 0; i < stringOffsets.Count; i++)
                {
                    writer.Write(stringOffsets[i]);
                }
            }
            return true;
        }
        #endregion

        #region ACCESSORS
        /// <summary>
        /// Add a string to the DB
        /// </summary>
        public void AddString(string str)
        {
            uint id = Utilities.AnimationHashedString(str);
            if (Entries.ContainsKey(id)) return;
            Entries.Add(id, str);
        }

        /// <summary>
        /// Remove a string from the DB
        /// </summary>
        public void RemoveString(string str)
        {
            uint id = Utilities.AnimationHashedString(str);
            Entries.Remove(id);
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Entry
        {
            public uint StringID;
            public int StringIndex;
        };
        #endregion
    }
}
