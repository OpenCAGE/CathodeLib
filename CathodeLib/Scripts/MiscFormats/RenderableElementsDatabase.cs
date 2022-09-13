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
                element.ModelLODPrimitiveCount = reds.ReadByte();
            }
            reds.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter reds = new BinaryWriter(File.OpenWrite(filepath));
            reds.BaseStream.SetLength(0);
            reds.Write(entries.Count);
            Utilities.Write<RenderableElement>(reds, entries);
            reds.Close();
        }

        /* Definition of a Renderable Element in CATHODE */
        public class RenderableElement
        {
            public int ModelIndex;
            public int MaterialLibraryIndex;

            public int ModelLODIndex; // NOTE: Not sure, looks like it.
            public byte ModelLODPrimitiveCount; // NOTE: Sure it is primitive count, not sure about the ModelLOD part.
        }
    }
}