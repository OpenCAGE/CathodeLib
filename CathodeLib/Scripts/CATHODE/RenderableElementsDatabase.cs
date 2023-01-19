using CathodeLib;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /* Handles reading/creating/writing Cathode REDS.BIN files */
    public class RenderableElementsDatabase : CathodeFile
    {
        private List<RenderableElement> entries = new List<RenderableElement>();
        public List<RenderableElement> Entries { get { return entries; } }

        /* Load the file */
        public RenderableElementsDatabase(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    RenderableElement element = new RenderableElement();
                    reader.BaseStream.Position += 4;
                    element.ModelIndex = reader.ReadInt32();
                    reader.BaseStream.Position += 5;
                    element.MaterialLibraryIndex = reader.ReadInt32();
                    reader.BaseStream.Position += 1;
                    element.ModelLODIndex = reader.ReadInt32();
                    element.ModelLODPrimitiveCount = reader.ReadByte(); //TODO: convert to int for ease of use?
                    entries.Add(element);
                }
            }
            return true;
        }

        /* Save the file */
        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                    writer.Write(entries[i].ModelIndex);
                    writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 });
                    writer.Write(entries[i].MaterialLibraryIndex);
                    writer.Write((byte)0x00);
                    writer.Write(entries[i].ModelLODIndex);
                    writer.Write((byte)entries[i].ModelLODPrimitiveCount);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        /* Definition of a Renderable Element in CATHODE */
        public class RenderableElement
        {
            public int ModelIndex;
            public int MaterialLibraryIndex;

            public int ModelLODIndex = -1; // NOTE: Not sure, looks like it.
            public byte ModelLODPrimitiveCount = 0; // NOTE: Sure it is primitive count, not sure about the ModelLOD part.
        }
        #endregion
    }
}