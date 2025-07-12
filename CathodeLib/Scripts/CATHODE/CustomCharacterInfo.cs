using CATHODE.Enums;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /* DATA/CHR_INFO/CUSTOMCHARACTERINFO.BIN */
    public class CustomCharacterInfo : CathodeFile
    {
        public Dictionary<CharacterModel, Dictionary<CharacterGender, string>> Skeletons = new Dictionary<CharacterModel, Dictionary<CharacterGender, string>>();
        public Dictionary<CharacterComponent, List<Accessory>> DefaultAccessories = new Dictionary<CharacterComponent, List<Accessory>>();
        public Dictionary<CharacterAccessoryOverride, Dictionary<CharacterComponent, List<Accessory>>> AccessoryOverrides = new Dictionary<CharacterAccessoryOverride, Dictionary<CharacterComponent, List<Accessory>>>();
        public List<CharacterDefinition> CharacterDefinitions = new List<CharacterDefinition>();
        public Dictionary<CharacterPopulation, List<Preset>> Presets = new Dictionary<CharacterPopulation, List<Preset>>();

        public static new Implementation Implementation = Implementation.CREATE | Implementation.SAVE | Implementation.LOAD;
        public CustomCharacterInfo(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position = 4; //version
                foreach (CharacterModel characterModel in Enum.GetValues(typeof(CharacterModel)))
                {
                    Dictionary<CharacterGender, string> skeletons = new Dictionary<CharacterGender, string>();
                    foreach (CharacterGender characterGender in Enum.GetValues(typeof(CharacterGender)))
                    {
                        skeletons.Add(characterGender, ReadString(reader));
                    }
                    Skeletons.Add(characterModel, skeletons);
                }
                foreach (CharacterComponent characterComponent in Enum.GetValues(typeof(CharacterComponent)))
                {
                    DefaultAccessories.Add(characterComponent, ReadAccessories(reader));
                }
                foreach (CharacterAccessoryOverride characterAccessoryOverride in Enum.GetValues(typeof(CharacterAccessoryOverride)))
                {
                    Dictionary<CharacterComponent, List<Accessory>> components = new Dictionary<CharacterComponent, List<Accessory>>();
                    foreach (CharacterComponent characterComponent in Enum.GetValues(typeof(CharacterComponent)))
                    {
                        components.Add(characterComponent, ReadAccessories(reader));
                    }
                    AccessoryOverrides.Add(characterAccessoryOverride, components);
                }
                int definitionsCount = reader.ReadInt32();
                for (int i = 0; i < definitionsCount; i++)
                {
                    CharacterDefinition definition = new CharacterDefinition();
                    definition.Name = ReadString(reader);
                    definition.VoiceActor = (VoiceActor)reader.ReadInt32();
                    definition.AssetType = (CharacterAsset)reader.ReadInt32();
                    definition.Model = (CharacterModel)reader.ReadInt32();
                    definition.Gender = (CharacterGender)reader.ReadInt32();
                    definition.Ethnicity = (CharacterEthnicity)reader.ReadInt32();
                    definition.Build = (CharacterBuild)reader.ReadInt32();
                    definition.Sleeve = (CharacterSleeve)reader.ReadInt32();
                    definition.Sound = (FoleySound)reader.ReadInt32();
                    foreach (CharacterComponent characterComponent in Enum.GetValues(typeof(CharacterComponent)))
                    {
                        definition.DefaultAccessories.Add(characterComponent, new Tuple<bool, List<Accessory>>(reader.ReadInt32() == 1, new List<Accessory>()));
                    }
                    foreach (CharacterComponent characterComponent in Enum.GetValues(typeof(CharacterComponent)))
                    {
                        definition.DefaultAccessories[characterComponent].Item2.AddRange(ReadAccessories(reader));
                    }
                    int componentsCount = reader.ReadInt32();
                    for (int x = 0; x < componentsCount; x++)
                    {
                        CharacterDefinition.Component component = new CharacterDefinition.Component();
                        component.Type = (CharacterComponent)reader.ReadInt32();
                        component.Model = (CharacterModel)reader.ReadInt32();
                        component.Gender = (CharacterGender)reader.ReadInt32();
                        component.Ethnicity = (CharacterEthnicity)reader.ReadInt32();
                        component.Build = (CharacterBuild)reader.ReadInt32();
                        component.Sleeve = (CharacterSleeve)reader.ReadInt32();
                        component.Sound = (FoleySound)reader.ReadInt32();
                        component.ModelName = ReadString(reader);
                        component.Accessories = ReadAccessories(reader);
                        definition.Components.Add(component);
                    }
                    CharacterDefinitions.Add(definition);
                }
                foreach (CharacterPopulation characterPopulation in Enum.GetValues(typeof(CharacterPopulation)))
                {
                    List<Preset> presets = new List<Preset>();
                    int presetCount = reader.ReadInt32();
                    for (int x = 0; x < presetCount; x++)
                    {
                        Preset preset = new Preset();
                        preset.Name = ReadString(reader);
                        preset.Frequency = reader.ReadSingle();
                        preset.AssetType = (CharacterAsset)reader.ReadInt32();
                        presets.Add(preset);
                    }
                    Presets.Add(characterPopulation, presets);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(_filepath, FileMode.Create)))
            {
                writer.Write(20);
                foreach (CharacterModel characterModel in Enum.GetValues(typeof(CharacterModel)))
                {
                    foreach (CharacterGender characterGender in Enum.GetValues(typeof(CharacterGender)))
                    {
                        WriteString(writer, Skeletons[characterModel][characterGender]);
                    }
                }
                foreach (CharacterComponent characterComponent in Enum.GetValues(typeof(CharacterComponent)))
                {
                    WriteAccessories(writer, DefaultAccessories[characterComponent]);
                }
                foreach (CharacterAccessoryOverride characterAccessoryOverride in Enum.GetValues(typeof(CharacterAccessoryOverride)))
                {
                    foreach (CharacterComponent characterComponent in Enum.GetValues(typeof(CharacterComponent)))
                    {
                        WriteAccessories(writer, AccessoryOverrides[characterAccessoryOverride][characterComponent]);
                    }
                }
                writer.Write(CharacterDefinitions.Count);
                foreach (CharacterDefinition definition in CharacterDefinitions)
                {
                    WriteString(writer, definition.Name);
                    writer.Write((int)definition.VoiceActor);
                    writer.Write((int)definition.AssetType);
                    writer.Write((int)definition.Model);
                    writer.Write((int)definition.Gender);
                    writer.Write((int)definition.Ethnicity);
                    writer.Write((int)definition.Build);
                    writer.Write((int)definition.Sleeve);
                    writer.Write((int)definition.Sound);
                    foreach (CharacterComponent characterComponent in Enum.GetValues(typeof(CharacterComponent)))
                    {
                        writer.Write(definition.DefaultAccessories[characterComponent].Item1 ? 1 : 0);
                    }
                    foreach (CharacterComponent characterComponent in Enum.GetValues(typeof(CharacterComponent)))
                    {
                        List<Accessory> accessories = definition.DefaultAccessories[characterComponent].Item2;
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
                foreach (CharacterPopulation characterPopulation in Enum.GetValues(typeof(CharacterPopulation)))
                {
                    List<Preset> presets = Presets[characterPopulation];
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
            public VoiceActor VoiceActor;
            public CharacterAsset AssetType;
            public CharacterModel Model;
            public CharacterGender Gender;
            public CharacterEthnicity Ethnicity;
            public CharacterBuild Build;
            public CharacterSleeve Sleeve;
            public FoleySound Sound;
            public Dictionary<CharacterComponent, Tuple<bool, List<Accessory>>> DefaultAccessories = new Dictionary<CharacterComponent, Tuple<bool, List<Accessory>>>();
            public List<Component> Components = new List<Component>();

            public class Component
            {
                public CharacterComponent Type;
                public CharacterModel Model;
                public CharacterGender Gender;
                public CharacterEthnicity Ethnicity;
                public CharacterBuild Build;
                public CharacterSleeve Sleeve;
                public FoleySound Sound;
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
            public CharacterAsset AssetType;
        }
        #endregion
    }
}