using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CATHODE.EXPERIMENTAL
{
    /* DATA/ENV/PRODUCTION/x/WORLD/SNDNODENETWORK.DAT & SOUNDEVENTDATA.DAT & SOUNDENVIRONMENTDATA.DAT & SOUNDFLASHMODELS.DAT & SOUNDLOADZONES.DAT & SOUNDDIALOGUELOOKUPS.DAT */
    public class SoundNodeNetwork : CathodeFile
    {
        public List<Light> Entries = new List<Light>();
        public static new Implementation Implementation = Implementation.NONE;
        public SoundNodeNetwork(string path) : base(path) { }

        private string _filepathEventData;
        private string _filepathEnvironmentData;
        private string _filepathFlashModels;
        private string _filepathLoadZones;
        private string _filepathDialogueLookups;

        #region FILE_IO
        override protected bool LoadInternal()
        {
            string basePath = _filepath.Substring(0, _filepath.Length - ("SNDNODENETWORK.DAT").Length);
            _filepathEventData = basePath + "SOUNDEVENTDATA.DAT";
            _filepathEnvironmentData = basePath + "SOUNDENVIRONMENTDATA.DAT";
            _filepathFlashModels = basePath + "SOUNDFLASHMODELS.DAT";
            _filepathLoadZones = basePath + "SOUNDLOADZONES.DAT";
            _filepathDialogueLookups = basePath + "SOUNDDIALOGUELOOKUPS.DAT";

            //SoundEventData.dat
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepathEventData)))
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
                    int unk = reader.ReadInt16();
                    ShortGuid id = Utilities.Consume<ShortGuid>(reader);
                    Console.WriteLine("[" + id.ToByteString() + "] " + name + " -> " + args);
                }
            }

            //SoundEnvironmentData.dat
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepathEnvironmentData)))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    byte[] content = reader.ReadBytes(100);
                    using (BinaryReader contentReader = new BinaryReader(new MemoryStream(content)))
                    {
                        string name = Utilities.ReadString(contentReader);
                        Console.WriteLine(name);
                    }
                }
            }

            //SoundFlashModels.dat
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepathFlashModels)))
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

            //SoundLoadZones.dat
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepathLoadZones)))
            {
                reader.BaseStream.Position += 4;
                int entryCount = reader.ReadInt32();
                reader.BaseStream.Position += 8;
                for (int i = 0; i < entryCount; i++)
                {
                    byte[] content = reader.ReadBytes(68);
                    using (BinaryReader contentReader = new BinaryReader(new MemoryStream(content)))
                    {
                        string name = Utilities.ReadString(contentReader);
                        Console.WriteLine(name);
                    }
                }
            }

            //SoundDialogueLookups.dat
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepathDialogueLookups)))
            {
                reader.BaseStream.Position += 16; //All unknowns
                int entryCount = ((int)reader.BaseStream.Length / 8) - 2; //We can probably work this out from the previous unknowns
                for (int i = 0; i < entryCount; i++)
                {
                    int soundID = reader.ReadInt32();
                    string soundName = SoundUtils.GetSoundName(soundID);
                    ShortGuid unk = Utilities.Consume<ShortGuid>(reader);
                    Console.WriteLine(soundName);
                }

            }

            //SndNodeNetwork.dat
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