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
                int unk = reader.ReadInt32();
                int strLength = reader.ReadInt16();
                string str = "";
                for (int i = 0; i < strLength; i++)
                    str += reader.ReadChar();
                reader.BaseStream.Position += 26;
                Vector3 position = Utilities.Consume<Vector3>(reader);
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