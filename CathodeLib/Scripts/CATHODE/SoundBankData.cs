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
    /// <summary>
    /// DATA/ENV/x/WORLD/SOUNDBANKDATA.DAT
    /// </summary>
    public class SoundBankData : CathodeFile
    {
        public List<SoundBank> Entries = new List<SoundBank>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public SoundBankData(string path) : base(path) { }
        public SoundBankData(MemoryStream stream, string path = "") : base(stream, path) { }
        public SoundBankData(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    int length = reader.ReadInt32();
                    string name = "";
                    for (int x = 0; x < length; x++) name += reader.ReadChar();

                    SoundBank soundbank = new SoundBank();
                    soundbank.Name = name;
                    soundbank.Localised = reader.ReadBoolean();
                    soundbank.UsesRSX = reader.ReadBoolean();
                    Entries.Add(soundbank);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(1);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].Name.Length);
                    Utilities.WriteString(Entries[i].Name, writer);
                    writer.Write(Entries[i].Localised);
                    writer.Write(Entries[i].UsesRSX);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class SoundBank
        {
            public string Name;
            public bool Localised;
            public bool UsesRSX;
        }
        #endregion
    }
}