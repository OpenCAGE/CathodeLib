using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    /* Loads and/or creates Cathode ENVIRONMENTMAP.BIN files */
    public class EnvironmentMapDatabase
    {
        private int unkVal = 12;

        private string filepath;
        public string FilePath { get { return filepath; } }

        private List<EnvironmentMapEntry> entries = new List<EnvironmentMapEntry>();
        public List<EnvironmentMapEntry> EnvMaps { get { return entries; } }

        public EnvironmentMapDatabase(string path)
        {
            filepath = path;
            if (!File.Exists(path)) return;

            BinaryReader bin = new BinaryReader(File.OpenRead(filepath));
            bin.BaseStream.Position += 8;
            int entryCount = bin.ReadInt32();
            unkVal = bin.ReadInt32();
            for (int i = 0; i < entryCount; i++)
            {
                EnvironmentMapEntry entry = new EnvironmentMapEntry();
                entry.envMapIndex = bin.ReadInt32();
                entry.mvrIndex = bin.ReadInt32();
                entries.Add(entry);
            }
            bin.Close();
        }

        public void Save()
        {
            BinaryWriter bin = new BinaryWriter(File.OpenWrite(filepath));
            bin.BaseStream.SetLength(0);
            bin.Write(new char[] { 'e', 'n', 'v', 'm' });
            bin.Write(1);
            bin.Write(entries.Count);
            bin.Write(unkVal); //TODO: what is this value? need to know for making new files.
            for (int i = 0; i < entries.Count; i++)
            {
                bin.Write(entries[i].envMapIndex);
                bin.Write(entries[i].mvrIndex);
            }
            bin.Close();
        }
    }

    public class EnvironmentMapEntry
    {
        public int envMapIndex;
        public int mvrIndex; //huh?
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