using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/SOUNDFLASHMODELS.DAT */
    public class SoundFlashModels : CathodeFile
    {
        public List<FlashModel> Entries = new List<FlashModel>();
        public static new Implementation Implementation = Implementation.NONE;
        public SoundFlashModels(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    FlashModel f = new FlashModel();
                    f.unk1 = reader.ReadInt16();
                    f.unk2 = reader.ReadInt16();
                    int count = reader.ReadInt32();
                    for (int x = 0; x < count; x++)
                        f.unk3.Add(reader.ReadInt32());
                    Entries.Add(f);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(1);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write((Int16)Entries[i].unk1);
                    writer.Write((Int16)Entries[i].unk2);
                    for (int x = 0; x < Entries[i].unk3.Count; x++)
                        writer.Write(Entries[i].unk3[x]);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class FlashModel
        {
            public int unk1;
            public int unk2;
            public List<int> unk3 = new List<int>();
        }
        #endregion
    }
}