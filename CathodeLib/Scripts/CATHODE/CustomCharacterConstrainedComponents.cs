using CATHODE.Enums;
using CATHODE.Scripting;
using CathodeLib;
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

                        writer.Write((int)Entries[i].Components[x].Model);
                        writer.Write((int)Entries[i].Components[x].Gender);
                        writer.Write((int)Entries[i].Components[x].Ethnicity);
                        writer.Write((int)Entries[i].Components[x].Build);
                        writer.Write((int)Entries[i].Components[x].SleeveType);
                        writer.Write((int)Entries[i].Components[x].SoundType);
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
            Entry entry = new Entry() { Type = type };
            for (int i = 0; i < entryCount; i++)
            {
                Entry.Component component = new Entry.Component();
                byte[] stringBlock = reader.ReadBytes(64);
                component.Name = Utilities.ReadString(stringBlock);
                component.Model = (CharacterModel)reader.ReadInt32();
                component.Gender = (CharacterGender)reader.ReadInt32();
                component.Ethnicity = (CharacterEthnicity)reader.ReadInt32();
                component.Build = (CharacterBuild)reader.ReadInt32();
                component.SleeveType = (CharacterSleeve)reader.ReadInt32();
                component.SoundType = (FoleySound)reader.ReadInt32();
                entry.Components.Add(component);
            }
            Entries.Add(entry);
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

                public CharacterModel Model;
                public CharacterGender Gender;
                public CharacterEthnicity Ethnicity;
                public CharacterBuild Build;
                public CharacterSleeve SleeveType;
                public FoleySound SoundType;
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