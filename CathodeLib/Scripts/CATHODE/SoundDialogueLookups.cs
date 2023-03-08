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
    /* DATA/ENV/PRODUCTION/x/WORLD/SOUNDDIALOGUELOOKUPS.DAT */
    public class SoundDialogueLookups : CathodeFile
    {
        public List<Sound> Entries = new List<Sound>();
        public static new Implementation Implementation = Implementation.LOAD;
        public SoundDialogueLookups(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 16; //All unknowns
                int entryCount = ((int)reader.BaseStream.Length / 8) - 2; //We can probably work this out from the previous unknowns
                for (int i = 0; i < entryCount; i++)
                {
                    Sound s = new Sound();
                    s.id = reader.ReadUInt32();
                    s.unk = Utilities.Consume<ShortGuid>(reader);
                    Entries.Add(s);
                }

            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(new byte[16]);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].id);
                    Utilities.Write<ShortGuid>(writer, Entries[i].unk);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Sound
        {
            public uint id;
            public ShortGuid unk;

            override public string ToString()
            {
                return SoundUtils.GetSoundName(id);
            }
        };
        #endregion
    }
}