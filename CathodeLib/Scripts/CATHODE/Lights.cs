using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE.EXPERIMENTAL
{
    /* DATA/ENV/PRODUCTION/x/WORLD/LIGHTS.BIN */
    public class Lights : CathodeFile
    {
        public List<Light> Entries = new List<Light>();
        public static new Implementation Implementation = Implementation.NONE;
        public Lights(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 8;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Light entry = new Light();
                    entry.MoverIndex = reader.ReadInt32();
                    Entries.Add(entry);
                }
                for (int i = 0; i < entryCount; i++)
                {
                    //TODO: Really not sure on almost all of this structure yet
                    Entries[i].unk1 = reader.ReadSingle();
                    Entries[i].unk2 = reader.ReadSingle();
                    Entries[i].unk3 = reader.ReadSingle();
                    Entries[i].unk4 = reader.ReadSingle();
                    Entries[i].unk5 = reader.ReadSingle();
                    Entries[i].unk6 = reader.ReadSingle();
                    Entries[i].OffsetOrIndex = reader.ReadInt32();
                    Entries[i].LightIndex0 = reader.ReadInt16();
                    Entries[i].unk7 = reader.ReadInt16();
                    Entries[i].LightIndex1 = reader.ReadInt16();
                    Entries[i].unk8 = reader.ReadInt16();
                }
                //TODO: we also leave some data behind here
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                //writer.BaseStream.SetLength(0);
                Utilities.WriteString("ligt", writer);
                writer.Write(4);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write((Int32)Entries[i].MoverIndex);
                }
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].unk1);
                    writer.Write(Entries[i].unk2);
                    writer.Write(Entries[i].unk3);
                    writer.Write(Entries[i].unk4);
                    writer.Write(Entries[i].unk5);
                    writer.Write(Entries[i].unk6);
                    writer.Write((Int32)Entries[i].OffsetOrIndex);
                    writer.Write((Int16)Entries[i].LightIndex0);
                    writer.Write((Int16)Entries[i].unk7);
                    writer.Write((Int16)Entries[i].LightIndex1);
                    writer.Write((Int16)Entries[i].unk8);
                }
                //TODO: another block here i don't know
                //writer.Write(new byte[27]); //it seems like you have a 27-byte buffer at the end?
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