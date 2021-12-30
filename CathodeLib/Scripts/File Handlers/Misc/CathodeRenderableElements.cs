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
    public class CathodeRenderableElements
    {
        private string filepath;
        public RenderableElementHeader header;
        public RenderableElementEntry[] entries;

        /* Load the file */
        public CathodeRenderableElements(string path)
        {
            filepath = path;

            BinaryReader stream = new BinaryReader(File.OpenRead(path));
            header = Utilities.Consume<RenderableElementHeader>(stream);
            entries = Utilities.ConsumeArray<RenderableElementEntry>(stream, header.EntryCount);
            stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<RenderableElementHeader>(stream, header);
            Utilities.Write<RenderableElementEntry>(stream, entries);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return entries.Length; } }
        public RenderableElementEntry[] Entries { get { return entries; } }
        public RenderableElementEntry GetEntry(int i)
        {
            return entries[i];
        }

        /* Data setters */
        public void SetEntry(int i, RenderableElementEntry content)
        {
            entries[i] = content;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RenderableElementHeader
    {
        public int EntryCount;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RenderableElementEntry
    {
        public int UnknownZeros0_;
        public int ModelIndex;
        public byte UnknownZeroByte0_;
        public int UnknownZeros1_;
        public int MaterialLibraryIndex;
        public byte UnknownZeroByte1_;
        public int ModelLODIndex; // NOTE: Not sure, looks like it.
        public byte ModelLODPrimitiveCount; // NOTE: Sure it is primitive count, not sure about the ModelLOD part.
    };
}