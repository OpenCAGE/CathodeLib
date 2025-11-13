using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/PATH_BARRIER_RESOURCES
    /// </summary>
    public class PathBarrierResources : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        protected override bool HandlesLoadingManually => true;
        private Resources _resources;

        public PathBarrierResources(string path, Resources resources) : base(path)
        {
            _resources = resources;

            _loaded = Load();
        }

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
                    entry.Resource = _resources.GetAtWriteIndex(reader.ReadInt32());
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
                    reader.Write(_resources.GetWriteIndex(Entries[i].Resource));
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
            public Resources.Resource Resource;
            public int unk1; //todo: perhaps this is a ShortGuid instance thing?
            public int unk2;
        }
        #endregion
    }
}
