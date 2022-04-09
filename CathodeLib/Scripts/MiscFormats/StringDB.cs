using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CATHODE.Commands;

namespace CATHODE.Misc
{
    /* Handles Cathode string DB files (ANIM_STRING_DB.BIN, etc) */
    public class StringDB
    {
        private string filepath;
        public List<StringDBEntry> dbEntries = new List<StringDBEntry>();

        /* Load the file */
        public StringDB(string path)
        {
            filepath = path;

            //Read all data in
            BinaryReader stream = new BinaryReader(File.OpenRead(path));
            Header header = Utilities.Consume<Header>(stream);
            Entry[] entries = Utilities.ConsumeArray<Entry>(stream, header.EntryCount);
            int[] stringOffsets = Utilities.ConsumeArray<int>(stream, header.StringCount);
            List<string> strings = new List<string>();
            for (int i = 0; i < header.StringCount; i++) strings.Add(Utilities.ReadString(stream));
            stream.Close();

            //Parse
            for (int i = 0; i < entries.Length; i++)
                dbEntries.Add(new StringDBEntry(entries[i].StringID, strings[entries[i].StringIndex]));
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(filepath));
            writer.Write(dbEntries.Count); writer.Write(dbEntries.Count);
            for (int i = 0; i < dbEntries.Count; i++)
            {
                Utilities.Write<ShortGuid>(writer, dbEntries[i].ID);
                writer.Write(i);
            }
            for (int i = 0; i < dbEntries.Count; i++)
            {
                for (int x = 0; x < dbEntries[i].content.Length; x++) writer.Write(dbEntries[i].content[x]);
                writer.Write((char)0x00);
            }
            writer.Close();
        }

        public class StringDBEntry
        {
            public StringDBEntry(ShortGuid _id, string _str)
            {
                ID = _id;
                content = _str;
            }
            public ShortGuid ID; //For the animation string DB, this cGUID is converted to UInt32 and used as the filename for the CLIP_DB
            public string content;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Header
        {
            public int EntryCount;
            public int StringCount;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Entry
        {
            public ShortGuid StringID;
            public int StringIndex;
        };
    }
}
