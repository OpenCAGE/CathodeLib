using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    /* Handles Cathode ENVIRONMENTMAP.BIN files */
    public class CathodeEnvironmentMap
    {
        private string filepath;
        private EnvironmentMapHeader header;
        private EnvironmentMapEntry[] entries;

        /* Load the file */
        public CathodeEnvironmentMap(string path)
        {
            filepath = path;

            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));
            header = Utilities.Consume<EnvironmentMapHeader>(Stream);
            entries = Utilities.ConsumeArray<EnvironmentMapEntry>(Stream, (int)header.EntryCount);
            Stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<EnvironmentMapHeader>(stream, header);
            Utilities.Write<EnvironmentMapEntry>(stream, entries);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return entries.Length; } }
        public EnvironmentMapEntry[] Entries { get { return entries; } }
        public EnvironmentMapEntry GetEntry(int i)
        {
            return entries[i];
        }

        /* Data setters */
        public void SetEntry(int i, EnvironmentMapEntry content)
        {
            entries[i] = content;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EnvironmentMapEntry
    {
        public int EnvironmentMapIndex; //Environment map index within ?
        public uint MoverIndex; //Mover index within MVR file
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EnvironmentMapHeader
    {
        public fourcc FourCC;
        public uint Unknown0_;
        public int EntryCount;
        public uint Unknown1_;
    };
}