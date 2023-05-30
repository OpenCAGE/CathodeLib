using CATHODE.Scripting;
using CathodeLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using static CATHODE.EXPERIMENTAL.MissionSave;

namespace CATHODE
{
    /* DATA/CHR_INFO/CUSTOMCHARACTERCONSTRAINEDCOMPONENTS.BIN */
    public class CustomCharacterConstrainedComponents : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public CustomCharacterConstrainedComponents(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position = 4;
                Read(ComponentType.ARMS, reader);
                Read(ComponentType.HEADS, reader);
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(20);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].Components.Count);
                    for (int x = 0; x < Entries[i].Components.Count; x++)
                    {
                        writer.Write(new byte[64]);
                        writer.BaseStream.Position -= 64;
                        Utilities.WriteString(Entries[i].Components[x].Name, writer, false);
                        writer.BaseStream.Position += 64 - Entries[i].Components[x].Name.Length;

                        writer.Write(Entries[i].Components[x].unk1);
                        writer.Write(Entries[i].Components[x].unk2);
                        writer.Write(Entries[i].Components[x].unk3);
                        writer.Write(Entries[i].Components[x].unk4);
                        writer.Write(Entries[i].Components[x].unk5);
                        writer.Write(Entries[i].Components[x].unk6);
                    }
                }
            }
            return true;
        }
        #endregion

        #region HELPERS
        private void Read(ComponentType type, BinaryReader reader)
        {
            int entryCount = reader.ReadInt32();
            Entry arms = new Entry() { Type = ComponentType.ARMS };
            for (int i = 0; i < entryCount; i++)
            {
                Entry.Component component = new Entry.Component();
                byte[] stringBlock = reader.ReadBytes(64);
                component.Name = Utilities.ReadString(stringBlock);

                //TODO: these seem to get concatenated in code
                component.unk1 = reader.ReadInt32();
                component.unk2 = reader.ReadInt32();
                component.unk3 = reader.ReadInt32();
                component.unk4 = reader.ReadInt32();
                component.unk5 = reader.ReadInt32();
                component.unk6 = reader.ReadInt32();

                arms.Components.Add(component);
            }
            Entries.Add(arms);
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public ComponentType Type;
            public List<Component> Components = new List<Component>();

            public class Component
            {
                public string Name;

                public int unk1;
                public int unk2;
                public int unk3;
                public int unk4;
                public int unk5;
                public int unk6;
            }
        };

        public enum ComponentType
        {
            ARMS,
            HEADS,
        }
        #endregion
    }
}