using CATHODE.Scripting;
using CathodeLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/CHARACTERACCESSORYSETS.BIN */
    public class CharacterAccessorySets : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public CharacterAccessorySets(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position = 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Entry entry = new Entry(); 
                    entry.character = Utilities.Consume<EntityHandle>(reader);

                    entry.shirt_composite = Utilities.Consume<ShortGuid>(reader);
                    entry.trousers_composite = Utilities.Consume<ShortGuid>(reader);
                    entry.shoes_composite = Utilities.Consume<ShortGuid>(reader);
                    entry.head_composite = Utilities.Consume<ShortGuid>(reader);
                    entry.arms_composite = Utilities.Consume<ShortGuid>(reader);
                    entry.collision_composite = Utilities.Consume<ShortGuid>(reader);

                    entry.unk1 = reader.ReadInt32();

                    entry.unk2 = reader.ReadInt32();
                    entry.unk3 = reader.ReadInt32();
                    entry.unk4 = reader.ReadInt32();
                    entry.unk5 = reader.ReadInt32();
                    entry.unk6 = reader.ReadInt32();
                    entry.decal = (Entry.Decal)reader.ReadInt32();
                    entry.unk8 = reader.ReadInt32();
                    entry.unk9 = reader.ReadInt32();
                    entry.unk10 = reader.ReadInt32();
                    entry.unk11 = reader.ReadInt32();

                    byte[] stringBlock = reader.ReadBytes(260);
                    entry.face_skeleton = Utilities.ReadString(stringBlock);
                    stringBlock = reader.ReadBytes(260);
                    entry.body_skeleton = Utilities.ReadString(stringBlock);

                    entry.unk12 = reader.ReadInt32();
                    entry.unk13 = reader.ReadInt32();
                    entry.unk14 = reader.ReadInt32();
                    Entries.Add(entry);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(20);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].character);

                    Utilities.Write(writer, Entries[i].shirt_composite);
                    Utilities.Write(writer, Entries[i].trousers_composite);
                    Utilities.Write(writer, Entries[i].shoes_composite);
                    Utilities.Write(writer, Entries[i].head_composite);
                    Utilities.Write(writer, Entries[i].arms_composite);
                    Utilities.Write(writer, Entries[i].collision_composite);

                    writer.Write(Entries[i].unk1);
                    writer.Write(Entries[i].unk2);
                    writer.Write(Entries[i].unk3);
                    writer.Write(Entries[i].unk4);
                    writer.Write(Entries[i].unk5);
                    writer.Write(Entries[i].unk6);
                    writer.Write((Int32)Entries[i].decal);
                    writer.Write(Entries[i].unk8);
                    writer.Write(Entries[i].unk9);
                    writer.Write(Entries[i].unk10);
                    writer.Write(Entries[i].unk11);

                    writer.Write(new byte[260]);
                    writer.BaseStream.Position -= 260;
                    Utilities.WriteString(Entries[i].face_skeleton, writer, false);
                    writer.BaseStream.Position += 260 - Entries[i].face_skeleton.Length;
                    writer.Write(new byte[260]);
                    writer.BaseStream.Position -= 260;
                    Utilities.WriteString(Entries[i].body_skeleton, writer, false);
                    writer.BaseStream.Position += 260 - Entries[i].body_skeleton.Length;

                    writer.Write(Entries[i].unk12);
                    writer.Write(Entries[i].unk13);
                    writer.Write(Entries[i].unk14);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public EntityHandle character = new EntityHandle();

            public ShortGuid shirt_composite = ShortGuid.Invalid;
            public ShortGuid trousers_composite = ShortGuid.Invalid;
            public ShortGuid shoes_composite = ShortGuid.Invalid;
            public ShortGuid head_composite = ShortGuid.Invalid;
            public ShortGuid arms_composite = ShortGuid.Invalid;
            public ShortGuid collision_composite = ShortGuid.Invalid;

            public int unk1 = 0;
            public int unk2 = 1;
            public int unk3 = 2;
            public int unk4 = 3; //This is often odd values
            public int unk5 = 4;
            public int unk6 = 5;

            public Decal decal = Decal.MEDICAL; //TODO: Is this decal texture defined by CUSTOMCHARACTERASSETDATA.BIN?

            public int unk8 = 0;
            public int unk9 = 0;
            public int unk10 = 1;
            public int unk11 = 0;

            public string face_skeleton = "AL";
            public string body_skeleton = "MALE";

            public int unk12 = 3;
            public int unk13 = 6;
            public int unk14 = 9;

            public enum Decal
            {
                MEDICAL,
                ENGINEERING,
                GENERIC,
                TECHNICAL,
            }
        };
        #endregion
    }
}