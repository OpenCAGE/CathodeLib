using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/SOUNDFLASHMODELS.DAT */
    public class SoundFlashModels : CathodeFile
    {
        //This stores the models which use flash textures, along with the flash texture ID

        public List<FlashModel> Entries = new List<FlashModel>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
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
                    f.flash_texture_id = reader.ReadInt32(); //flash textures end in [FLASH]
                    int modelCount = reader.ReadInt32();
                    for (int x = 0; x < modelCount; x++)
                        f.model_indexes.Add(reader.ReadInt32());
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
                    writer.Write(Entries[i].flash_texture_id);
                    for (int x = 0; x < Entries[i].model_indexes.Count; x++)
                        writer.Write(Entries[i].model_indexes[x]);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class FlashModel
        {
            public int flash_texture_id;
            public List<int> model_indexes = new List<int>();
        }
        #endregion
    }
}