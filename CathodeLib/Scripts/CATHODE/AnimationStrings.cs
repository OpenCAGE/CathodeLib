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
    /* DATA/GLOBAL/ANIMATION.PAK -> ANIM_STRING_DB.BIN, ANIM_STRING_DB_DEBUG.BIN */
    public class AnimationStrings : CathodeFile
    {
        public Dictionary<uint, string> Entries = new Dictionary<uint, string>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public AnimationStrings(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader stream = new BinaryReader(File.OpenRead(_filepath)))
            {
                //Read all data in
                int EntryCount = stream.ReadInt32();
                int StringCount = stream.ReadInt32();
                Entry[] entries = Utilities.ConsumeArray<Entry>(stream, EntryCount);
                int[] stringOffsets = Utilities.ConsumeArray<int>(stream, StringCount);
                List<string> strings = new List<string>();
                for (int i = 0; i < StringCount; i++) strings.Add(Utilities.ReadString(stream));

                //Parse
                for (int i = 0; i < entries.Length; i++) Entries.Add(entries[i].StringID, strings[entries[i].StringIndex]);
                //TODO: encoding on a couple strings here is wrong
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
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
        /* Add a string to the DB */
        public void AddString(string str)
        {
            uint id = Utilities.AnimationHashedString(str);
            if (Entries.ContainsKey(id)) return;
            Entries.Add(id, str);
        }

        /* Remove a string from the DB */
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
