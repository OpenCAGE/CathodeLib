using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Enums
{
    //TODO: Should deprecate the enum utility and just have all enums here.

    public enum FoleySound // Cathode scripting CHARACTER_FOLEY_SOUND enum
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
    public enum CharacterAsset // Cathode scripting CUSTOM_CHARACTER_ASSETS enum
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
    public enum CharacterAccessoryOverride // Cathode scripting CUSTOM_CHARACTER_ACCESSORY_OVERRIDE enum
    {
        ACCESSORY_OVERRIDE_01 = 0,
        ACCESSORY_OVERRIDE_02 = 1,
        ACCESSORY_OVERRIDE_03 = 2,
        ACCESSORY_OVERRIDE_04 = 3,
        ACCESSORY_OVERRIDE_05 = 4,
        ACCESSORY_OVERRIDE_06 = 5,
        ACCESSORY_OVERRIDE_07 = 6,
        ACCESSORY_OVERRIDE_08 = 7,
        ACCESSORY_OVERRIDE_09 = 8,
        ACCESSORY_OVERRIDE_10 = 9,
    }
    public enum CharacterPopulation // Cathode scripting CUSTOM_CHARACTER_POPULATION enum
    {
        POPULATION_01, 
        POPULATION_02, 
        POPULATION_03, 
        POPULATION_04, 
        POPULATION_05, 
        POPULATION_06, 
        POPULATION_07, 
        POPULATION_08, 
        POPULATION_09, 
        POPULATION_10, 
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
    public enum CharacterGender // Cathode scripting CUSTOM_CHARACTER_GENDER enum
    {
        MALE,
        FEMALE,
    }
    public enum CharacterEthnicity // Cathode scripting CUSTOM_CHARACTER_ETHNICITY enum
    {
        AFRICAN,
        CAUCASIAN,
        ASIAN,
    }
    public enum CharacterBuild // Cathode scripting CUSTOM_CHARACTER_BUILD enum
    {
        STANDARD,
        HEAVY,
    }
    public enum CharacterModel // Cathode scripting CUSTOM_CHARACTER_MODEL enum
    {
        NPC,
        ANDROID,
        CORPSE,
    }
    public enum CharacterSleeve // Cathode scripting CUSTOM_CHARACTER_SLEEVETYPE enum
    {
        LONG,
        MEDIUM,
        SHORT,
    }
    public enum CharacterComponent // Cathode scripting CUSTOM_CHARACTER_COMPONENT enum
    {
        TORSO, 
        LEGS, 
        SHOES, 
        HEAD, 
        ARMS,
        COLLISION,
    }
}
