using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    /* Handles CATHODE ENVIRONMENTMAP.BIN files */
    public class EnvironmentMapBIN
    {
        private string filepath;
        private alien_environment_map_bin_header header;
        private alien_environment_map_bin_entry[] entries;

        /* Load the file */
        public EnvironmentMapBIN(string path)
        {
            filepath = path;

            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));
            header = Utilities.Consume<alien_environment_map_bin_header>(Stream);
            entries = Utilities.ConsumeArray<alien_environment_map_bin_entry>(Stream, (int)header.EntryCount);
            Stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<alien_environment_map_bin_header>(stream, header);
            Utilities.Write<alien_environment_map_bin_entry>(stream, entries);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return entries.Length; } }
        public alien_environment_map_bin_entry[] Entries { get { return entries; } }
        public alien_environment_map_bin_entry GetEntry(int i)
        {
            return entries[i];
        }

        /* Data setters */
        public void SetEntry(int i, alien_environment_map_bin_entry content)
        {
            entries[i] = content;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_environment_map_bin_entry
    {
        public int EnvironmentMapIndex; //Environment map index within ?
        public uint MoverIndex; //Mover index within MVR file
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_environment_map_bin_header
    {
        public fourcc FourCC;
        public uint Unknown0_;
        public int EntryCount;
        public uint Unknown1_;
    };
}