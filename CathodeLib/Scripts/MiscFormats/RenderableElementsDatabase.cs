using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    /* Handles Cathode REDS.BIN files */
    public class RenderableElementsDatabase
    {
        private string filepath;

        private List<RenderableElement> entries;
        public List<RenderableElement> RenderableElements { get { return entries; } }

        /* Load the file */
        public RenderableElementsDatabase(string path)
        {
            filepath = path;
            entries = new List<RenderableElement>();

            //Don't try and read a REDS that doesn't exist, we will make one when saving.
            if (!File.Exists(path)) return;

            BinaryReader reds = new BinaryReader(File.OpenRead(path));
            int entryCount = reds.ReadInt32();
            for (int i = 0; i < entryCount; i++)
            {
                RenderableElement element = new RenderableElement();
                reds.BaseStream.Position += 4;
                element.ModelIndex = reds.ReadInt32();
                reds.BaseStream.Position += 5;
                element.MaterialLibraryIndex = reds.ReadInt32();
                reds.BaseStream.Position += 1;
                element.ModelLODIndex = reds.ReadInt32();
                element.ModelLODPrimitiveCount = reds.ReadByte(); //TODO: convert to int for ease of use?
                entries.Add(element);
            }
            reds.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter reds = new BinaryWriter(File.OpenWrite(filepath));
            reds.BaseStream.SetLength(0);
            reds.Write(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                reds.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                reds.Write(entries[i].ModelIndex);
                reds.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 });
                reds.Write(entries[i].MaterialLibraryIndex);
                reds.Write((byte)0x00);
                reds.Write(entries[i].ModelLODIndex);
                reds.Write((byte)entries[i].ModelLODPrimitiveCount);
            }
            reds.Close();
        }

        /* Definition of a Renderable Element in CATHODE */
        public class RenderableElement
        {
            public int ModelIndex;
            public int MaterialLibraryIndex;

            public int ModelLODIndex = -1; // NOTE: Not sure, looks like it.
            public byte ModelLODPrimitiveCount = 0; // NOTE: Sure it is primitive count, not sure about the ModelLOD part.
        }
    }
}