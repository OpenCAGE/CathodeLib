using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CATHODE.EXPERIMENTAL
{
    /* DATA/ENV/PRODUCTION/x/WORLD/SNDNODENETWORK.DAT & SOUNDEVENTDATA.DAT */
    public class SoundNodeNetwork : CathodeFile
    {
        public List<Light> Entries = new List<Light>();
        public static new Implementation Implementation = Implementation.NONE;
        public SoundNodeNetwork(string path) : base(path) { }

        private string _filepathSoundEventData;

        #region FILE_IO
        override protected bool LoadInternal()
        {
            _filepathSoundEventData = _filepath.Substring(0, _filepath.Length - ("SNDNODENETWORK.DAT").Length) + "SOUNDEVENTDATA.DAT";

            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepathSoundEventData)))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    int length = reader.ReadInt32();
                    string name = "";
                    for (int x = 0; x < length; x++)
                        name += reader.ReadChar();
                    length = reader.ReadInt32();
                    string args = "";
                    for (int x = 0; x < length; x++)
                        args += reader.ReadChar();
                    reader.BaseStream.Position += 2;
                    ShortGuid id = Utilities.Consume<ShortGuid>(reader);
                    int unk = reader.ReadInt16();
                    Console.WriteLine("[" + unk + "] " + name + " -> " + args);
                }
                string sdfds = "";
            }

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
                writer.Write(14);
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Light
        {
            public int MoverIndex; //Index of the mover in the MODELS.MVR file

            public float unk1;
            public float unk2;
            public float unk3;
            public float unk4;
            public float unk5;
            public float unk6;

            public int OffsetOrIndex;

            public int LightIndex0;
            public int unk7;
            public int LightIndex1;
            public int unk8;
        };
        #endregion
    }
}