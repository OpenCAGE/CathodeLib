using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    /* Handles CATHODE RESOURCES.BIN files */
    public class ResourcesBIN
    {
        private string filepath;
        public alien_resources_bin_header header;
        public alien_resources_bin_entry[] entries;

        /* Load the file */
        public ResourcesBIN(string path)
        {
            filepath = path;

            BinaryReader Stream = new BinaryReader(File.OpenRead(path));
            header = Utilities.Consume<alien_resources_bin_header>(Stream);
            entries = Utilities.ConsumeArray<alien_resources_bin_entry>(Stream, header.EntryCount);
            Stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<alien_resources_bin_header>(stream, header);
            Utilities.Write<alien_resources_bin_entry>(stream, entries);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return entries.Length; } }
        public alien_resources_bin_entry[] Entries { get { return entries; } }
        public alien_resources_bin_entry GetEntry(int i)
        {
            return entries[i];
        }

        /* Data setters */
        public void SetEntry(int i, alien_resources_bin_entry content)
        {
            entries[i] = content;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_resources_bin_header
    {
        public fourcc Magic;
        public int UnknownOne_;
        public int EntryCount;
        public int UnknownZero_;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_resources_bin_entry
    {
        public int Unknown0_;
        public int ResourceID;
        public int UnknownResourceIndex; // NOTE: This is an entry index in this file itself.
    };
}