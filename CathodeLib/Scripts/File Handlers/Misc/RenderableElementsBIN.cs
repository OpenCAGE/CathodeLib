using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    /* Handles CATHODE REDS.BIN files */
    public class RenderableElementsBIN
    {
        private string filepath;
        public alien_reds_header header;
        public List<alien_reds_entry> entries;

        /* Load the file */
        public RenderableElementsBIN(string path)
        {
            filepath = path;

            BinaryReader stream = new BinaryReader(File.OpenRead(path));
            header = Utilities.Consume<alien_reds_header>(ref stream);
            entries = Utilities.ConsumeArray<alien_reds_entry>(ref stream, header.EntryCount);
            stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            FileStream stream = new FileStream(filepath, FileMode.Create);
            Utilities.Write<alien_reds_header>(ref stream, header);
            for (int i = 0; i < entries.Count; i++) Utilities.Write<alien_reds_entry>(ref stream, entries[i]);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return entries.Count; } }
        public List<alien_reds_entry> Entries { get { return entries; } }
        public alien_reds_entry GetEntry(int i)
        {
            return entries[i];
        }

        /* Data setters */
        public void SetEntry(int i, alien_reds_entry content)
        {
            entries[i] = content;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_reds_header
    {
        public int EntryCount;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_reds_entry
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