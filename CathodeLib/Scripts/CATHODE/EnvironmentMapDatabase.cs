using CathodeLib;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* Loads and/or creates Cathode ENVIRONMENTMAP.BIN files */
    public class EnvironmentMapDatabase : CathodeFile
    {
        private int _unknownValue = 12;

        private List<EnvironmentMapEntry> Entries = new List<EnvironmentMapEntry>();

        public EnvironmentMapDatabase(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 8;
                int entryCount = reader.ReadInt32();
                _unknownValue = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    EnvironmentMapEntry entry = new EnvironmentMapEntry();
                    entry.envMapIndex = reader.ReadInt32();
                    entry.mvrIndex = reader.ReadInt32();
                    Entries.Add(entry);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(new char[] { 'e', 'n', 'v', 'm' });
                writer.Write(1);
                writer.Write(Entries.Count);
                writer.Write(_unknownValue); //TODO: what is this value? need to know for making new files.
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].envMapIndex);
                    writer.Write(Entries[i].mvrIndex);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
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
        #endregion
    }
}