using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE.EXPERIMENTAL
{
    /* DATA/ENV/PRODUCTION/x/WORLD/ALPHALIGHT_LEVEL.BIN */
    public class AlphaLightLevel : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.NONE;
        public AlphaLightLevel(string path) : base(path) { }

        // Lighting information for objects with alpha (e.g. glass). Levels can load without this file, but look worse.

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 8;

                //NOTE: these values are always 64/128/256 i think
                int count = reader.ReadInt32();
                int length = reader.ReadInt32() * 8;

                for (int i = 0; i < count; i++)
                {
                    Entries.Add(new Entry()
                    {
                        content = reader.ReadBytes(length)
                    });
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                Utilities.WriteString("alph", writer);
                writer.Write(0);
                writer.Write(Entries.Count);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].content);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public byte[] content;
        };
        #endregion
    }
}