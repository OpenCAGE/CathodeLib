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
        public List<string> Entries = new List<string>();
        public static new Implementation Implementation = Implementation.NONE;
        public SoundLoadZones(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                reader.BaseStream.Position += 8;
                for (int i = 0; i < entryCount; i++)
                {
                    byte[] content = reader.ReadBytes(68);
                    using (BinaryReader contentReader = new BinaryReader(new MemoryStream(content)))
                    {
                        Entries.Add(Utilities.ReadString(contentReader));
                    }
                }
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
                writer.Write(new byte[8]);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(new byte[68]);
                    long resetPos = writer.BaseStream.Position;
                    writer.BaseStream.Position -= 68;
                    Utilities.WriteString(Entries[i], writer);
                    writer.BaseStream.Position = resetPos;
                }
            }
            return true;
        }
        #endregion
    }
}