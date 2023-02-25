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
    /* DATA/ENV/PRODUCTION/x/WORLD/SOUNDFLASHMODELS.DAT */
    public class SoundFlashModels : CathodeFile
    {
        private List<string> Entries = new List<string>();
        public static new Implementation Implementation = Implementation.NONE;
        public SoundFlashModels(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    int unk1 = reader.ReadInt16();
                    int unk2 = reader.ReadInt16();
                    int count = reader.ReadInt32();
                    for (int x = 0; x < count; x++)
                    {
                        int unk3 = reader.ReadInt32();
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
            }
            return true;
        }
        #endregion
    }
}