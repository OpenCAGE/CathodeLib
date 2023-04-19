using CathodeLib;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/REDS.BIN */
    public class RenderableElements : CathodeFile
    {
        public List<Element> Entries = new List<Element>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public RenderableElements(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Element element = new Element();
                    reader.BaseStream.Position += 4;
                    element.ModelIndex = reader.ReadInt32();
                    reader.BaseStream.Position += 5;
                    element.MaterialIndex = reader.ReadInt32();
                    reader.BaseStream.Position += 1;
                    element.LODIndex = reader.ReadInt32();
                    element.LODCount = reader.ReadByte(); //TODO: convert to int for ease of use?
                    Entries.Add(element);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                    writer.Write(Entries[i].ModelIndex);
                    writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 });
                    writer.Write(Entries[i].MaterialIndex);
                    writer.Write((byte)0x00);
                    writer.Write(Entries[i].LODIndex);
                    writer.Write((byte)Entries[i].LODCount);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Element
        {
            public int ModelIndex;
            public int MaterialIndex;

            public int LODIndex = -1; //This is the index of the REDS entry that we use for our LOD model/material
            public byte LODCount = 0; //This is the number of entries sequentially from the LODIndex to use for the LOD from REDS
        }
        #endregion
    }
}