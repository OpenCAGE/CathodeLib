using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CATHODE.Commands;
using CathodeLib;

namespace CATHODE.Misc
{
    /* Handles Cathode animation string DB files (ANIM_STRING_DB.BIN, ANIM_STRING_DB_DEBUG.BIN) */
    public class AnimationStringDB
    {
        public string Filepath { get { return filepath; } }
        private string filepath;

        private Dictionary<uint, string> cachedStrings = new Dictionary<uint, string>();

        /* Load the file */
        public AnimationStringDB(string path)
        {
            filepath = path;

            //Read all data in
            BinaryReader stream = new BinaryReader(File.OpenRead(path));
            int EntryCount = stream.ReadInt32();
            int StringCount = stream.ReadInt32();
            Entry[] entries = Utilities.ConsumeArray<Entry>(stream, EntryCount);
            int[] stringOffsets = Utilities.ConsumeArray<int>(stream, StringCount);
            List<string> strings = new List<string>();
            for (int i = 0; i < StringCount; i++) strings.Add(Utilities.ReadString(stream));
            stream.Close();

            //Parse
            for (int i = 0; i < entries.Length; i++) cachedStrings.Add(entries[i].StringID, strings[entries[i].StringIndex]);
            //TODO: encoding on a couple strings here is wrong
        }

        /* Add a string to the DB */
        public void AddString(string str)
        {
            uint id = Utilities.AnimationHashedString(str);
            if (cachedStrings.ContainsKey(id)) return;
            cachedStrings.Add(id, str);
        }
        
        /* Remove a string from the DB */
        public void RemoveString(string str)
        {
            uint id = Utilities.AnimationHashedString(str);
            cachedStrings.Remove(id);
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(filepath));
            writer.Write(cachedStrings.Count); 
            writer.Write(cachedStrings.Count);
            int count = 0;
            foreach (KeyValuePair<uint, string> value in cachedStrings)
            {
                writer.Write(value.Key);
                writer.Write(count);
                count++;
            }
            int baseline = (cachedStrings.Count * 4 * 2) + 8 + (cachedStrings.Count * 4);
            writer.BaseStream.Position = baseline;
            List<int> stringOffsets = new List<int>();
            foreach (KeyValuePair<uint, string> value in cachedStrings)
            {
                stringOffsets.Add((int)writer.BaseStream.Position - baseline);
                ExtraBinaryUtils.WriteString(value.Value, writer);
                writer.Write((char)0x00);
            }
            writer.BaseStream.Position = (cachedStrings.Count * 4 * 2) + 8;
            for (int i = 0; i < stringOffsets.Count; i++)
            {
                writer.Write(stringOffsets[i]);
            }
            writer.Close();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Entry
        {
            public uint StringID;
            public int StringIndex;
        };
    }
}
