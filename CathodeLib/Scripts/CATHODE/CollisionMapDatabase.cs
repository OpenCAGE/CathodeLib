using CathodeLib;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* Handles Cathode COLLISION.MAP files - TODO: lots of unknown values here */
    public class CollisionMapDatabase : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Impl Implementation = Impl.CREATE | Impl.LOAD | Impl.SAVE;
        public CollisionMapDatabase(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position = 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Entry entry = new Entry();
                    for (int x = 0; x < 4; x++) entry.Unknowns1[x] = reader.ReadInt32();
                    entry.ID = reader.ReadInt32();
                    for (int x = 0; x < 7; x++) entry.Unknowns2[x] = reader.ReadInt32();
                    Entries.Add(entry);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count * 80);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < 4; x++) writer.Write(Entries[i].Unknowns1[x]);
                    writer.Write(Entries[i].ID);
                    for (int x = 0; x < 7; x++) writer.Write(Entries[i].Unknowns2[x]);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public int ID;

            public int[] Unknowns1 = new int[4];
            public int[] Unknowns2 = new int[7];
        };
        #endregion
    }
}