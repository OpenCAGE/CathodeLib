﻿using CathodeLib;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/MATERIAL_MAPPINGS.PAK */
    public class MaterialMappings : CathodeFile
    {
        public List<Mapping> Entries = new List<Mapping>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;
        public MaterialMappings(string path) : base(path) { }
        
        private byte[] _headerJunk = new byte[8];

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                //Parse header
                _headerJunk = reader.ReadBytes(8); //TODO: Work out what this contains
                int entryCount = reader.ReadInt32();

                //Parse entries (XML is broken in the build files - doesn't get shipped)
                for (int x = 0; x < entryCount; x++)
                {
                    //This entry
                    Mapping entry = new Mapping();
                    entry.MapHeader = reader.ReadBytes(4); //TODO: Work out the significance of this value, to be able to construct new PAKs from scratch.
                    entry.MapEntryCoupleCount = reader.ReadInt32();
                    entry.MapJunk = reader.ReadBytes(4); //TODO: Work out if this is always null.
                    for (int p = 0; p < (entry.MapEntryCoupleCount * 2) + 1; p++)
                    {
                        //String
                        int length = reader.ReadInt32();
                        string materialString = "";
                        for (int i = 0; i < length; i++)
                        {
                            materialString += reader.ReadChar();
                        }

                        //First string is filename, others are materials
                        if (p == 0)
                        {
                            entry.MapFilename = materialString;
                        }
                        else
                        {
                            entry.MapMatEntries.Add(materialString);
                        }
                    }
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
                writer.Write(_headerJunk);
                writer.Write(Entries.Count);
                foreach (Mapping entry in Entries)
                {
                    writer.Write(entry.MapHeader);
                    writer.Write(entry.MapEntryCoupleCount);
                    writer.Write(entry.MapJunk);
                    writer.Write(entry.MapFilename.Length);
                    Utilities.WriteString(entry.MapFilename, writer);
                    foreach (string name in entry.MapMatEntries)
                    {
                        writer.Write(name.Length);
                        Utilities.WriteString(name, writer);
                    }
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Mapping
        {
            public byte[] MapHeader = new byte[4];
            public byte[] MapJunk = new byte[4]; //I think this is always null
            public string MapFilename = "";
            public int MapEntryCoupleCount = 0; //materials will be 2* this number
            public List<string> MapMatEntries = new List<string>();
        }
        #endregion
    }
}