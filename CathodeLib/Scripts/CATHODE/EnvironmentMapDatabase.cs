using CathodeLib;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* Loads and/or creates Cathode ENVIRONMENTMAP.BIN files */
    public class EnvironmentMapDatabase : CathodeFile
    {
        public List<Mapping> Entries = new List<Mapping>();
        public static new Impl Implementation = Impl.LOAD | Impl.SAVE;
        public EnvironmentMapDatabase(string path) : base(path) { }

        private int _unknownValue = 12; //TODO: need to figure out what this val is to be able to create file from scratch

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 8;
                int entryCount = reader.ReadInt32();
                _unknownValue = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Mapping entry = new Mapping();
                    entry.EnvMapIndex = reader.ReadInt32();
                    entry.MoverIndex = reader.ReadInt32();
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
                    writer.Write(Entries[i].EnvMapIndex);
                    writer.Write(Entries[i].MoverIndex);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Mapping
        {
            public int EnvMapIndex; //Index of the environment map
            public int MoverIndex; //Index of the mover in the MODELS.MVR file to apply the env map to
        };
        #endregion
    }
}