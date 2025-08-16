using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/PRODUCTION/x/WORLD/PATH_BARRIER_RESOURCES
    /// </summary>
    public class PathBarrierResources : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public PathBarrierResources(string path) : base(path) { }
        public PathBarrierResources(MemoryStream stream, string path = "") : base(stream, path) { }
        public PathBarrierResources(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position = 4; //59
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Entry entry = new Entry();
                    entry.resourcesBinIndex = reader.ReadInt32();
                    int index = reader.ReadInt16();
                    if (index != i+1) throw new Exception();
                    entry.unk1 = reader.ReadInt16();
                    entry.unk2 = reader.ReadInt16();
                    Entries.Add(entry);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter reader = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                reader.BaseStream.SetLength(0);
                reader.Write((Int32)59);
                reader.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    reader.Write((Int32)Entries[i].resourcesBinIndex);
                    reader.Write((Int32)(i + 1));
                    reader.Write((Int16)Entries[i].unk1); 
                    reader.Write((Int16)Entries[i].unk2);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public int resourcesBinIndex;
            public int unk1; //todo: perhaps this is a ShortGuid instance thing?
            public int unk2;
        }
        #endregion
    }
}
