using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/SOUNDLOADZONES.DAT */
    public class SoundLoadZones : CathodeFile
    {
        //This seems to specify all the sound banks that are loaded within the level. They can be specified via spatial zones, but this feature is never used.

        public List<string> Entries = new List<string>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public SoundLoadZones(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4; //version - zero
                int soundPackCount = reader.ReadInt32();
                reader.BaseStream.Position += 4; //count of zones - always zero (unused)
                for (int i = 0; i < soundPackCount; i++)
                {
                    reader.BaseStream.Position += 4; //ref count - always zero (set at runtime?)
                    byte[] bankName = reader.ReadBytes(64);
                    using (BinaryReader contentReader = new BinaryReader(new MemoryStream(bankName)))
                    {
                        Entries.Add(Utilities.ReadString(contentReader));
                    }
                }
                //zones are here, but they're always unused, so can skip
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(0);
                writer.Write(Entries.Count);
                writer.Write(0);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(0);
                    Utilities.WriteString(Entries[i], writer);
                    writer.Write(new byte[64 - Entries[i].Length]);
                }
            }
            return true;
        }
        #endregion
    }
}