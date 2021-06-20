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
        private List<alien_environment_map_bin_entry> entries;

        /* Load the file */
        public EnvironmentMapBIN(string path)
        {
            filepath = path;

            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));
            header = Utilities.Consume<alien_environment_map_bin_header>(ref Stream);
            entries = Utilities.ConsumeArray<alien_environment_map_bin_entry>(ref Stream, (int)header.EntryCount);
            Stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            FileStream stream = new FileStream(filepath, FileMode.Create);
            Utilities.Write<alien_environment_map_bin_header>(ref stream, header);
            for (int i = 0; i < entries.Count; i++) Utilities.Write<alien_environment_map_bin_entry>(ref stream, entries[i]);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return entries.Count; } }
        public List<alien_environment_map_bin_entry> Entries { get { return entries; } }
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