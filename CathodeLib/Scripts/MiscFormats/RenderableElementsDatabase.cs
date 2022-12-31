using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    /* Handles reading/creating/writing Cathode REDS.BIN files */
    public class RenderableElementsDatabase : CathodeFile
    {
        private List<RenderableElement> entries = new List<RenderableElement>();
        public List<RenderableElement> RenderableElements { get { return entries; } }

        /* Load the file */
        public RenderableElementsDatabase(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        protected override bool Load()
        {
            if (!File.Exists(_filepath)) return false;

            BinaryReader reader = new BinaryReader(File.OpenRead(_filepath));
            try
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
            catch
            {
                reader.Close();
                return false;
            }
            reader.Close();
            return true;
        }

        /* Save the file */
        override public bool Save()
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath));
            try
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
            catch
            {
                writer.Close();
                return false;
            }
            writer.Close();
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