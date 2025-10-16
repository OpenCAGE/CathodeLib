using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/PRODUCTION/x/WORLD/SOUNDFLASHMODELS.DAT
    /// </summary>
    public class SoundFlashModels : CathodeFile
    {
        //This stores the models which use flash textures, along with the flash texture ID

        public List<FlashModel> Entries = new List<FlashModel>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public SoundFlashModels(string path) : base(path) { }
        public SoundFlashModels(MemoryStream stream, string path = "") : base(stream, path) { }
        public SoundFlashModels(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    FlashModel f = new FlashModel();
                    f.Texture = new TexturePtr(reader);
                    int modelCount = reader.ReadInt32();
                    for (int x = 0; x < modelCount; x++)
                        f.ModelIndexes.Add(reader.ReadInt32());
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
                    Entries[i].Texture.Write(writer);
                    writer.Write(Entries[i].ModelIndexes.Count);
                    for (int x = 0; x < Entries[i].ModelIndexes.Count; x++)
                        writer.Write(Entries[i].ModelIndexes[x]);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class FlashModel
        {
            public TexturePtr Texture;
            public List<int> ModelIndexes = new List<int>();
        }
        #endregion
    }
}