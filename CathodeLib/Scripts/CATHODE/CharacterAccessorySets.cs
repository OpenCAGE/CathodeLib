using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/CHARACTERACCESSORYSETS.BIN */
    public class CharacterAccessorySets : CathodeFile
    {
        public List<CharacterAttributes> Entries = new List<CharacterAttributes>();
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
                    CharacterAttributes entry = new CharacterAttributes(); 
                    entry.character = Utilities.Consume<EntityHandle>(reader);

                    entry.components.Torso.Composite = Utilities.Consume<ShortGuid>(reader);
                    entry.components.Legs.Composite = Utilities.Consume<ShortGuid>(reader);
                    entry.components.Shoes.Composite = Utilities.Consume<ShortGuid>(reader);
                    entry.components.Head.Composite = Utilities.Consume<ShortGuid>(reader);
                    entry.components.Arms.Composite = Utilities.Consume<ShortGuid>(reader);
                    entry.components.Collision.Composite = Utilities.Consume<ShortGuid>(reader);

                    entry.components.Torso.AccessoryIndex = reader.ReadInt32();
                    entry.components.Legs.AccessoryIndex = reader.ReadInt32();
                    entry.components.Shoes.AccessoryIndex = reader.ReadInt32();
                    entry.components.Head.AccessoryIndex = reader.ReadInt32();
                    entry.components.Arms.AccessoryIndex = reader.ReadInt32();
                    entry.components.Collision.AccessoryIndex = reader.ReadInt32();

                    entry.asset_type = (CharacterAttributes.AssetType)reader.ReadInt32(); 
                    entry.voice_actor = (CharacterAttributes.VoiceActor)reader.ReadInt32();
                    entry.gender = (CharacterAttributes.Gender)reader.ReadInt32();
                    entry.ethnicity = (CharacterAttributes.Ethnicity)reader.ReadInt32();
                    entry.build = (CharacterAttributes.Build)reader.ReadInt32();

                    byte[] stringBlock = reader.ReadBytes(260);
                    entry.face_skeleton = Utilities.ReadString(stringBlock);
                    stringBlock = reader.ReadBytes(260);
                    entry.gender_skeleton = Utilities.ReadString(stringBlock);

                    entry.foley.Torso = (CharacterAttributes.FoleySounds.Type)reader.ReadInt32();
                    entry.foley.Leg = (CharacterAttributes.FoleySounds.Type)reader.ReadInt32();
                    entry.foley.Footwear = (CharacterAttributes.FoleySounds.Type)reader.ReadInt32();
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

                    Utilities.Write(writer, Entries[i].components.Torso.Composite);
                    Utilities.Write(writer, Entries[i].components.Legs.Composite);
                    Utilities.Write(writer, Entries[i].components.Shoes.Composite);
                    Utilities.Write(writer, Entries[i].components.Head.Composite);
                    Utilities.Write(writer, Entries[i].components.Arms.Composite);
                    Utilities.Write(writer, Entries[i].components.Collision.Composite);

                    writer.Write(Entries[i].components.Torso.AccessoryIndex);
                    writer.Write(Entries[i].components.Legs.AccessoryIndex);
                    writer.Write(Entries[i].components.Shoes.AccessoryIndex);
                    writer.Write(Entries[i].components.Head.AccessoryIndex);
                    writer.Write(Entries[i].components.Arms.AccessoryIndex);
                    writer.Write(Entries[i].components.Collision.AccessoryIndex);

                    writer.Write((Int32)Entries[i].asset_type);
                    writer.Write((Int32)Entries[i].voice_actor);
                    writer.Write((Int32)Entries[i].gender);
                    writer.Write((Int32)Entries[i].ethnicity);
                    writer.Write((Int32)Entries[i].build);

                    writer.Write(new byte[260]);
                    writer.BaseStream.Position -= 260;
                    Utilities.WriteString(Entries[i].face_skeleton, writer, false);
                    writer.BaseStream.Position += 260 - Entries[i].face_skeleton.Length;
                    writer.Write(new byte[260]);
                    writer.BaseStream.Position -= 260;
                    Utilities.WriteString(Entries[i].gender_skeleton, writer, false);
                    writer.BaseStream.Position += 260 - Entries[i].gender_skeleton.Length;

                    writer.Write((Int32)Entries[i].foley.Torso);
                    writer.Write((Int32)Entries[i].foley.Leg);
                    writer.Write((Int32)Entries[i].foley.Footwear);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class CharacterAttributes
        {
            public EntityHandle character = new EntityHandle();
            public Components components = new Components();

            public AssetType asset_type = AssetType.ASSETSET_01; //TODO: Is this defined by CUSTOMCHARACTERASSETDATA.BIN?
            public VoiceActor voice_actor = VoiceActor.CV1;
            public Gender gender = Gender.MALE;
            public Ethnicity ethnicity = Ethnicity.CAUCASIAN;
            public Build build = Build.STANDARD;

            public string face_skeleton = "AL";
            public string gender_skeleton = "MALE";

            public FoleySounds foley = new FoleySounds();

            public class Components
            {
                public Component Torso = new Component();
                public Component Legs = new Component();
                public Component Shoes = new Component();
                public Component Head = new Component();
                public Component Arms = new Component();
                public Component Collision = new Component();

                public class Component
                {
                    public ShortGuid Composite = ShortGuid.Invalid;
                    public int AccessoryIndex = -1;
                }
            }

            public class FoleySounds
            {
                public Type Torso = Type.HEAVY_OVERALLS;
                public Type Leg = Type.HEAVY_OVERALLS;
                public Type Footwear = Type.BOOTS;

                public enum Type // Cathode scripting CHARACTER_FOLEY_SOUND enum
                {
                    LEATHER = 0,
                    HEAVY_JACKET = 1, 
                    HEAVY_OVERALLS = 2, 
                    SHIRT = 3, 
                    SUIT_JACKET = 4, 
                    SUIT_TROUSERS = 5, 
                    JEANS = 6, 
                    BOOTS = 7, 
                    FLATS = 8, 
                    TRAINERS = 9,
                }
            }

            public enum AssetType // Cathode scripting CUSTOM_CHARACTER_ASSETS enum
            {
                ASSETSET_01, //Medical
                ASSETSET_02, //Engineering
                ASSETSET_03, //Generic
                ASSETSET_04, //Technical
                ASSETSET_05, // ?
                ASSETSET_06, // ?
                ASSETSET_07, // ?
                ASSETSET_08, // ?
                ASSETSET_09, // ?
                ASSETSET_10, // ?
            }
            public enum VoiceActor // Cathode scripting DIALOGUE_VOICE_ACTOR enum
            {
                AUTO, 
                CV1, 
                CV2, 
                CV3, 
                CV4, 
                CV5, 
                CV6, 
                RT1, 
                RT2, 
                RT3, 
                RT4, 
                AN1, 
                AN2, 
                AN3, 
                ANH, 
            }
            public enum Gender // Cathode scripting CUSTOM_CHARACTER_GENDER enum
            {
                MALE, 
                FEMALE, 
            }
            public enum Ethnicity // Cathode scripting CUSTOM_CHARACTER_ETHNICITY enum
            {
                AFRICAN, 
                CAUCASIAN, 
                ASIAN, 
            }
            public enum Build // Cathode scripting CUSTOM_CHARACTER_BUILD enum
            {
                STANDARD, 
                HEAVY, 
            }
        };
        #endregion
    }
}