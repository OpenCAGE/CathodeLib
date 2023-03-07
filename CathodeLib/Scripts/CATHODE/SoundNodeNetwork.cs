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
    /* DATA/ENV/PRODUCTION/x/WORLD/SNDNODENETWORK.DAT */
    public class SoundNodeNetwork : CathodeFile
    {
        private List<string> Entries = new List<string>();
        public static new Implementation Implementation = Implementation.NONE;
        public SoundNodeNetwork(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4;
                int unk1 = reader.ReadInt16();
                int unk2 = reader.ReadInt16();
                int strLength = reader.ReadInt16();
                string str = "";
                for (int i = 0; i < strLength; i++) str += reader.ReadChar();
                int a = reader.ReadInt16();
                int b = reader.ReadInt16();
                int c = reader.ReadInt16();
                ShortGuid d = Utilities.Consume<ShortGuid>(reader);
                int e = reader.ReadInt32();


                for (int i = 0; i < 999; i++)
                {
                    UInt16 x_index = reader.ReadUInt16();
                    UInt16 y_index = reader.ReadUInt16();
                    UInt16 z_index = reader.ReadUInt16();
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
            }
            return true;
        }
        #endregion
    }
}