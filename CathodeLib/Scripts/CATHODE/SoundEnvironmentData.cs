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
    /* DATA/ENV/PRODUCTION/x/WORLD/SOUNDENVIRONMENTDATA.DAT */
    public class SoundEnvironmentData : CathodeFile
    {
        //This stores the reverbs that are used within the level. Similar to SoundLoadZones, these can be stored with spatial zones, but this feature is unused.

        public List<string> Entries = new List<string>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public SoundEnvironmentData(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4; //version
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    byte[] content = reader.ReadBytes(100);
                    using (BinaryReader contentReader = new BinaryReader(new MemoryStream(content)))
                    {
                        Entries.Add(Utilities.ReadString(contentReader));
                    }
                }
                reader.BaseStream.Position += 4; //zone count - always zero
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(2);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.WriteString(Entries[i], writer);
                    writer.Write(new byte[100 - Entries[i].Length]);
                }
                writer.Write(0);
            }
            return true;
        }
        #endregion
    }
}