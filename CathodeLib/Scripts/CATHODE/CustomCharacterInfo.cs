using CATHODE.Scripting;
using CathodeLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* DATA/CHR_INFO/CUSTOMCHARACTERINFO.BIN */
    public class CustomCharacterInfo : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.NONE;
        public CustomCharacterInfo(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position = 4;
                int entryCount = 99;
                for (int i = 0; i < entryCount; i++)
                {
                    byte[] stringBlock = reader.ReadBytes(64);
                    Console.WriteLine(Utilities.ReadString(stringBlock));
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(20);
                for (int i = 0; i < Entries.Count; i++)
                {

                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {

        };
        #endregion
    }
}