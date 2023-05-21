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
                    entry.character_entity = Utilities.Consume<ShortGuid>(reader);
                    entry.unk = Utilities.Consume<ShortGuid>(reader);
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
                    entry.unk7 = reader.ReadInt32();
                    entry.unk8 = reader.ReadInt32();
                    entry.unk9 = reader.ReadInt32();
                    entry.unk10 = reader.ReadInt32();
                    entry.unk11 = reader.ReadInt32();

                    byte[] stringBlock = reader.ReadBytes(260);
                    entry.body_type = Utilities.ReadString(stringBlock);
                    stringBlock = reader.ReadBytes(260);
                    entry.skeleton = Utilities.ReadString(stringBlock);

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
                    Utilities.Write(writer, Entries[i].character_entity);
                    Utilities.Write(writer, Entries[i].unk);
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
                    writer.Write(Entries[i].unk7);
                    writer.Write(Entries[i].unk8);
                    writer.Write(Entries[i].unk9);
                    writer.Write(Entries[i].unk10);
                    writer.Write(Entries[i].unk11);

                    writer.Write(new byte[260]);
                    writer.BaseStream.Position -= 260;
                    Utilities.WriteString(Entries[i].body_type, writer, false);
                    writer.BaseStream.Position += 260 - Entries[i].body_type.Length;
                    writer.Write(new byte[260]);
                    writer.BaseStream.Position -= 260;
                    Utilities.WriteString(Entries[i].skeleton, writer, false);
                    writer.BaseStream.Position += 260 - Entries[i].skeleton.Length;

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
            public ShortGuid character_entity;
            public ShortGuid unk; //This points to the specific character instance - but how?
            public ShortGuid shirt_composite;
            public ShortGuid trousers_composite;
            public ShortGuid shoes_composite;
            public ShortGuid head_composite;
            public ShortGuid arms_composite;
            public ShortGuid collision_composite;

            public int unk1;
            public int unk2;
            public int unk3;
            public int unk4;
            public int unk5;
            public int unk6;
            public int unk7;
            public int unk8;
            public int unk9;
            public int unk10;
            public int unk11;

            public string body_type;
            public string skeleton;

            public int unk12;
            public int unk13;
            public int unk14;
        };
        #endregion
    }
}