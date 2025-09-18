using CATHODE.Enums;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /// <summary>
    /// DATA/CHR_INFO/CUSTOMCHARACTERINFO.BIN
    /// </summary>
    public class CustomCharacterInfo : CathodeFile
    {
        public Dictionary<CUSTOM_CHARACTER_MODEL, Dictionary<CUSTOM_CHARACTER_GENDER, string>> Skeletons = new Dictionary<CUSTOM_CHARACTER_MODEL, Dictionary<CUSTOM_CHARACTER_GENDER, string>>();
        public Dictionary<CUSTOM_CHARACTER_COMPONENT, List<Accessory>> DefaultAccessories = new Dictionary<CUSTOM_CHARACTER_COMPONENT, List<Accessory>>();
        public Dictionary<CUSTOM_CHARACTER_ACCESSORY_OVERRIDE, Dictionary<CUSTOM_CHARACTER_COMPONENT, List<Accessory>>> AccessoryOverrides = new Dictionary<CUSTOM_CHARACTER_ACCESSORY_OVERRIDE, Dictionary<CUSTOM_CHARACTER_COMPONENT, List<Accessory>>>();
        public List<CharacterDefinition> CharacterDefinitions = new List<CharacterDefinition>();
        public Dictionary<CUSTOM_CHARACTER_POPULATION, List<Preset>> Presets = new Dictionary<CUSTOM_CHARACTER_POPULATION, List<Preset>>();

        public static new Implementation Implementation = Implementation.CREATE | Implementation.SAVE | Implementation.LOAD;

        public CustomCharacterInfo(string path) : base(path) { }
        public CustomCharacterInfo(MemoryStream stream, string path = "") : base(stream, path) { }
        public CustomCharacterInfo(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position = 4; //version
                foreach (CUSTOM_CHARACTER_MODEL CUSTOM_CHARACTER_MODEL in Enum.GetValues(typeof(CUSTOM_CHARACTER_MODEL)))
                {
                    Dictionary<CUSTOM_CHARACTER_GENDER, string> skeletons = new Dictionary<CUSTOM_CHARACTER_GENDER, string>();
                    foreach (CUSTOM_CHARACTER_GENDER CUSTOM_CHARACTER_GENDER in Enum.GetValues(typeof(CUSTOM_CHARACTER_GENDER)))
                    {
                        skeletons.Add(CUSTOM_CHARACTER_GENDER, ReadString(reader));
                    }
                    Skeletons.Add(CUSTOM_CHARACTER_MODEL, skeletons);
                }
                foreach (CUSTOM_CHARACTER_COMPONENT CUSTOM_CHARACTER_COMPONENT in Enum.GetValues(typeof(CUSTOM_CHARACTER_COMPONENT)))
                {
                    DefaultAccessories.Add(CUSTOM_CHARACTER_COMPONENT, ReadAccessories(reader));
                }
                foreach (CUSTOM_CHARACTER_ACCESSORY_OVERRIDE CUSTOM_CHARACTER_ACCESSORY_OVERRIDE in Enum.GetValues(typeof(CUSTOM_CHARACTER_ACCESSORY_OVERRIDE)))
                {
                    Dictionary<CUSTOM_CHARACTER_COMPONENT, List<Accessory>> components = new Dictionary<CUSTOM_CHARACTER_COMPONENT, List<Accessory>>();
                    foreach (CUSTOM_CHARACTER_COMPONENT CUSTOM_CHARACTER_COMPONENT in Enum.GetValues(typeof(CUSTOM_CHARACTER_COMPONENT)))
                    {
                        components.Add(CUSTOM_CHARACTER_COMPONENT, ReadAccessories(reader));
                    }
                    AccessoryOverrides.Add(CUSTOM_CHARACTER_ACCESSORY_OVERRIDE, components);
                }
                int definitionsCount = reader.ReadInt32();
                for (int i = 0; i < definitionsCount; i++)
                {
                    CharacterDefinition definition = new CharacterDefinition();
                    definition.Name = ReadString(reader);
                    definition.DIALOGUE_VOICE_ACTOR = (DIALOGUE_VOICE_ACTOR)reader.ReadInt32();
                    definition.AssetType = (CUSTOM_CHARACTER_ASSETS)reader.ReadInt32();
                    definition.Model = (CUSTOM_CHARACTER_MODEL)reader.ReadInt32();
                    definition.Gender = (CUSTOM_CHARACTER_GENDER)reader.ReadInt32();
                    definition.Ethnicity = (CUSTOM_CHARACTER_ETHNICITY)reader.ReadInt32();
                    definition.Build = (CUSTOM_CHARACTER_BUILD)reader.ReadInt32();
                    definition.Sleeve = (CUSTOM_CHARACTER_SLEEVETYPE)reader.ReadInt32();
                    definition.Sound = (CHARACTER_FOLEY_SOUND)reader.ReadInt32();
                    foreach (CUSTOM_CHARACTER_COMPONENT CUSTOM_CHARACTER_COMPONENT in Enum.GetValues(typeof(CUSTOM_CHARACTER_COMPONENT)))
                    {
                        definition.DefaultAccessories.Add(CUSTOM_CHARACTER_COMPONENT, new Tuple<bool, List<Accessory>>(reader.ReadInt32() == 1, new List<Accessory>()));
                    }
                    foreach (CUSTOM_CHARACTER_COMPONENT CUSTOM_CHARACTER_COMPONENT in Enum.GetValues(typeof(CUSTOM_CHARACTER_COMPONENT)))
                    {
                        definition.DefaultAccessories[CUSTOM_CHARACTER_COMPONENT].Item2.AddRange(ReadAccessories(reader));
                    }
                    int componentsCount = reader.ReadInt32();
                    for (int x = 0; x < componentsCount; x++)
                    {
                        CharacterDefinition.Component component = new CharacterDefinition.Component();
                        component.Type = (CUSTOM_CHARACTER_COMPONENT)reader.ReadInt32();
                        component.Model = (CUSTOM_CHARACTER_MODEL)reader.ReadInt32();
                        component.Gender = (CUSTOM_CHARACTER_GENDER)reader.ReadInt32();
                        component.Ethnicity = (CUSTOM_CHARACTER_ETHNICITY)reader.ReadInt32();
                        component.Build = (CUSTOM_CHARACTER_BUILD)reader.ReadInt32();
                        component.Sleeve = (CUSTOM_CHARACTER_SLEEVETYPE)reader.ReadInt32();
                        component.Sound = (CHARACTER_FOLEY_SOUND)reader.ReadInt32();
                        component.ModelName = ReadString(reader);
                        component.Accessories = ReadAccessories(reader);
                        definition.Components.Add(component);
                    }
                    CharacterDefinitions.Add(definition);
                }
                foreach (CUSTOM_CHARACTER_POPULATION CUSTOM_CHARACTER_POPULATION in Enum.GetValues(typeof(CUSTOM_CHARACTER_POPULATION)))
                {
                    List<Preset> presets = new List<Preset>();
                    int presetCount = reader.ReadInt32();
                    for (int x = 0; x < presetCount; x++)
                    {
                        Preset preset = new Preset();
                        preset.Name = ReadString(reader);
                        preset.Frequency = reader.ReadSingle();
                        preset.AssetType = (CUSTOM_CHARACTER_ASSETS)reader.ReadInt32();
                        presets.Add(preset);
                    }
                    Presets.Add(CUSTOM_CHARACTER_POPULATION, presets);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(_filepath, FileMode.Create)))
            {
                writer.Write(20);
                foreach (CUSTOM_CHARACTER_MODEL CUSTOM_CHARACTER_MODEL in Enum.GetValues(typeof(CUSTOM_CHARACTER_MODEL)))
                {
                    foreach (CUSTOM_CHARACTER_GENDER CUSTOM_CHARACTER_GENDER in Enum.GetValues(typeof(CUSTOM_CHARACTER_GENDER)))
                    {
                        WriteString(writer, Skeletons[CUSTOM_CHARACTER_MODEL][CUSTOM_CHARACTER_GENDER]);
                    }
                }
                foreach (CUSTOM_CHARACTER_COMPONENT CUSTOM_CHARACTER_COMPONENT in Enum.GetValues(typeof(CUSTOM_CHARACTER_COMPONENT)))
                {
                    WriteAccessories(writer, DefaultAccessories[CUSTOM_CHARACTER_COMPONENT]);
                }
                foreach (CUSTOM_CHARACTER_ACCESSORY_OVERRIDE CUSTOM_CHARACTER_ACCESSORY_OVERRIDE in Enum.GetValues(typeof(CUSTOM_CHARACTER_ACCESSORY_OVERRIDE)))
                {
                    foreach (CUSTOM_CHARACTER_COMPONENT CUSTOM_CHARACTER_COMPONENT in Enum.GetValues(typeof(CUSTOM_CHARACTER_COMPONENT)))
                    {
                        WriteAccessories(writer, AccessoryOverrides[CUSTOM_CHARACTER_ACCESSORY_OVERRIDE][CUSTOM_CHARACTER_COMPONENT]);
                    }
                }
                writer.Write(CharacterDefinitions.Count);
                foreach (CharacterDefinition definition in CharacterDefinitions)
                {
                    WriteString(writer, definition.Name);
                    writer.Write((int)definition.DIALOGUE_VOICE_ACTOR);
                    writer.Write((int)definition.AssetType);
                    writer.Write((int)definition.Model);
                    writer.Write((int)definition.Gender);
                    writer.Write((int)definition.Ethnicity);
                    writer.Write((int)definition.Build);
                    writer.Write((int)definition.Sleeve);
                    writer.Write((int)definition.Sound);
                    foreach (CUSTOM_CHARACTER_COMPONENT CUSTOM_CHARACTER_COMPONENT in Enum.GetValues(typeof(CUSTOM_CHARACTER_COMPONENT)))
                    {
                        writer.Write(definition.DefaultAccessories[CUSTOM_CHARACTER_COMPONENT].Item1 ? 1 : 0);
                    }
                    foreach (CUSTOM_CHARACTER_COMPONENT CUSTOM_CHARACTER_COMPONENT in Enum.GetValues(typeof(CUSTOM_CHARACTER_COMPONENT)))
                    {
                        List<Accessory> accessories = definition.DefaultAccessories[CUSTOM_CHARACTER_COMPONENT].Item2;
                        WriteAccessories(writer, accessories);
                    }
                    writer.Write(definition.Components.Count);
                    foreach (CharacterDefinition.Component component in definition.Components)
                    {
                        writer.Write((int)component.Type);
                        writer.Write((int)component.Model);
                        writer.Write((int)component.Gender);
                        writer.Write((int)component.Ethnicity);
                        writer.Write((int)component.Build);
                        writer.Write((int)component.Sleeve);
                        writer.Write((int)component.Sound);
                        WriteString(writer, component.ModelName);
                        WriteAccessories(writer, component.Accessories);
                    }
                }
                foreach (CUSTOM_CHARACTER_POPULATION CUSTOM_CHARACTER_POPULATION in Enum.GetValues(typeof(CUSTOM_CHARACTER_POPULATION)))
                {
                    List<Preset> presets = Presets[CUSTOM_CHARACTER_POPULATION];
                    writer.Write(presets.Count);
                    foreach (Preset preset in presets)
                    {
                        WriteString(writer, preset.Name);
                        writer.Write(preset.Frequency);
                        writer.Write((int)preset.AssetType);
                    }
                }
            }
            return true;
        }
        #endregion

        #region HELPERS
        private static List<Accessory> ReadAccessories(BinaryReader reader)
        {
            List<Accessory> accessories = new List<Accessory>();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                Accessory accessory = new Accessory();
                accessory.Name = ReadString(reader);
                accessory.Slot = reader.ReadInt32();
                accessory.Frequency = reader.ReadSingle();
                accessories.Add(accessory);
            }
            return accessories;
        }

        private static string ReadString(BinaryReader reader)
        {
            byte[] stringBlock = reader.ReadBytes(64);
            return Utilities.ReadString(stringBlock);
        }

        private static void WriteAccessories(BinaryWriter writer, List<Accessory> accessories)
        {
            writer.Write(accessories.Count);
            foreach (Accessory accessory in accessories)
            {
                WriteString(writer, accessory.Name);
                writer.Write(accessory.Slot);
                writer.Write(accessory.Frequency);
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            Utilities.WriteString(value, writer);
            writer.Write(new byte[64 - value.Length]);
        }
        #endregion

        #region STRUCTURES
        public class CharacterDefinition
        {
            public string Name;
            public DIALOGUE_VOICE_ACTOR DIALOGUE_VOICE_ACTOR;
            public CUSTOM_CHARACTER_ASSETS AssetType;
            public CUSTOM_CHARACTER_MODEL Model;
            public CUSTOM_CHARACTER_GENDER Gender;
            public CUSTOM_CHARACTER_ETHNICITY Ethnicity;
            public CUSTOM_CHARACTER_BUILD Build;
            public CUSTOM_CHARACTER_SLEEVETYPE Sleeve;
            public CHARACTER_FOLEY_SOUND Sound;
            public Dictionary<CUSTOM_CHARACTER_COMPONENT, Tuple<bool, List<Accessory>>> DefaultAccessories = new Dictionary<CUSTOM_CHARACTER_COMPONENT, Tuple<bool, List<Accessory>>>();
            public List<Component> Components = new List<Component>();

            public class Component
            {
                public CUSTOM_CHARACTER_COMPONENT Type;
                public CUSTOM_CHARACTER_MODEL Model;
                public CUSTOM_CHARACTER_GENDER Gender;
                public CUSTOM_CHARACTER_ETHNICITY Ethnicity;
                public CUSTOM_CHARACTER_BUILD Build;
                public CUSTOM_CHARACTER_SLEEVETYPE Sleeve;
                public CHARACTER_FOLEY_SOUND Sound;
                public string ModelName;
                public List<Accessory> Accessories = new List<Accessory>();
            }
        };
        public class Accessory
        {
            public string Name;
            public int Slot;
            public float Frequency;
        }
        public class Preset
        {
            public string Name;
            public float Frequency;
            public CUSTOM_CHARACTER_ASSETS AssetType;
        }
        #endregion
    }
}