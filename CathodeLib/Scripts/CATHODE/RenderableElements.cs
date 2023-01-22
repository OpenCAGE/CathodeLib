using CathodeLib;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /* Handles reading/creating/writing Cathode REDS.BIN files */
    public class RenderableElements : CathodeFile
    {
        public List<Element> Entries = new List<Element>();
        public static new Impl Implementation = Impl.CREATE | Impl.LOAD | Impl.SAVE;
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
                    element.MaterialLibraryIndex = reader.ReadInt32();
                    reader.BaseStream.Position += 1;
                    element.ModelLODIndex = reader.ReadInt32();
                    element.ModelLODPrimitiveCount = reader.ReadByte(); //TODO: convert to int for ease of use?
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
                    writer.Write(Entries[i].MaterialLibraryIndex);
                    writer.Write((byte)0x00);
                    writer.Write(Entries[i].ModelLODIndex);
                    writer.Write((byte)Entries[i].ModelLODPrimitiveCount);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Element
        {
            public int ModelIndex;
            public int MaterialLibraryIndex;

            public int ModelLODIndex = -1; // NOTE: Not sure, looks like it.
            public byte ModelLODPrimitiveCount = 0; // NOTE: Sure it is primitive count, not sure about the ModelLOD part.
        }
        #endregion
    }
}