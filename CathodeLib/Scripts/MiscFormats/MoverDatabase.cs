using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
#endif
using CATHODE.Commands;

namespace CATHODE.Misc
{
    /* Handles Cathode MODELS.MVR files */
    public class MoverDatabase
    {
        public string FilePath { get { return filePath; } }
        private string filePath = "";
        private int fileSize = 32;
        private int entryCount = 0;
        private int entrySize = 320;
        private int entryCountUnknown = 0;

        public List<MOVER_DESCRIPTOR> Movers = new List<MOVER_DESCRIPTOR>();

        /* Load the file */
        public MoverDatabase() { }
        public MoverDatabase(string pathToFile)
        {
            if (!File.Exists(pathToFile)) return;

            filePath = pathToFile;

            BinaryReader stream = new BinaryReader(File.OpenRead(filePath));
            fileSize = stream.ReadInt32();
            entryCount = stream.ReadInt32();
            entryCountUnknown = stream.ReadInt32(); //a count of something - not sure what
            stream.BaseStream.Position += 4; 
            entrySize = stream.ReadInt32();
            stream.BaseStream.Position += 12;
            Movers = new List<MOVER_DESCRIPTOR>(Utilities.ConsumeArray<MOVER_DESCRIPTOR>(stream, entryCount));
            stream.Close();
        }

        /* Save the file */
        public void Save(string pathToFile = "")
        {
            if (pathToFile != "") filePath = pathToFile;

            fileSize = (Movers.Count * entrySize) + 32;
            entryCount = Movers.Count;
            entryCountUnknown = 0;

            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filePath));
            stream.BaseStream.SetLength(0);
            stream.Write(fileSize);
            stream.Write(entryCount);
            stream.Write(entryCountUnknown);
            stream.Write(0);
            stream.Write(entrySize);
            stream.Write(0); stream.Write(0); stream.Write(0);
            Utilities.Write<MOVER_DESCRIPTOR>(stream, Movers);
            stream.Close();
        }
    }

    //Pulled from the iOS decomp
    public enum RENDERABLE_INSTANCE_Type
    {
        RenderableLightInstance = 0,
        RenderableDynamicFXInstance = 1,
        RenderableDynamicTempFXInstance = 2,
        RenderableEnvironmentInstance = 3,
        RenderableCharacterInstance = 4,
        RenderableMiscInstance = 5,
        RenderablePlanetInstance = 6,
        RenderableEnvironmentExtraInstance = 7,
        RenderableFogSphereInstance = 8,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MOVER_DESCRIPTOR
    {
        /*
         
         RenderableScene::calculate_renderable_instance_type is called on RenderableElementSet which seems to define a type (RENDERABLE_INSTANCE::Type?) 
         It can have values 1-9, which I think are one of (as per RenderableScene::InstanceManager):
            - RenderableCharacterInstance
            - RenderableDynamicFXInstance
            - RenderableDynamicTempFXInstance
            - RenderableEnvironmentExtraInstance
            - RenderableEnvironmentInstance
            - RenderableFogSphereInstance
            - RenderableLightInstance
            - RenderableMiscInstance
            - RenderablePlanetInstance
         Logic is applied based on a numeric range, rather than per individual number (although maybe specific logic is applied within these ranges)
         The ranges are:
            - 1-9
            - 3-9
            - 5-9
            - 7-9
         These ranges are found using RenderableScene::ForEachInstanceType<min,max>
         MVR and REDS data is used per Type, being pulled from MOVER_DESCRIPTOR and RenderableElementSet respectively
         
         RenderableElementSet is always paired with a MOVER_DESCRIPTOR (see RenderableScene::create_instance)

         RenderableEnvironmentInstance::set_constants is what uses RENDER_CONSTANTS - look there to define that struct
         RenderableEnvironmentInstance::set_gpu_constants is what uses GPU_CONSTANTS - look there to define that struct

         RenderableScene::initialize passes MOVER_DESCRIPTOR to create_instance and defines its length as 320
         RenderableScene::create_instance takes MOVER_DESCRIPTOR and grabs two values:
            296: (uint) uVar1 - used as first parameter in call to RenderableScene::add_new_zone, which passes it to g_zone_ids
            300: (uint) uVar3 - used as second parameter in call to RenderableScene::add_new_zone, which does some conditional check to call Zone::activate

         INSTANCE_DATABASE::initialise_emissive_surfaces uses MOVER_DESCRIPTOR:
            284: dunno what this is used for, but it goes +4, +8 - so 3 offsets?

         RENDERABLE_INSTANCE::TYPE values RenderableLightInstance/RenderableDynamicFxInstance/RenderableDynamicTempFXInstance/etc do this...
            RenderableScene::InstanceManager<>::reserve_light_light_master_sets takes takes MOVER_DESCRIPTOR and grabs two values:
               304: uint* appears if this is 0 then light or w/e isn't initialised, then some CONCAT44 operation is applied with its value, stacking with previous lights in a while loop
            RenderableScene::InstanceManager<>::add_instance_to_light_master_set also grabs this same value (304) and checks to see if its 0, then if its less than or equal to another value.

         For personal reference of offset conversions:
            0x40 = 64
            0xa0 = 160
            0x10c = 268
            0x118 = 280
            0x130 = 304
            0x136 = 310
            0x140 = 320

         RenderableCharacterInstance:
            0: MATRIX_44
            64: GPU_CONSTANTS
            160: RENDER_CONSTANTS
            268: uint* (visibility)
            310: ushort* which is used for a couple things...
               RenderableCharacterInstance + 0x34 (ushort*) - (ushort)((uVar1 & 4) << 2)
               if ((uVar1 & 1) != 0) then RenderableCharacterInstance::activate

         RenderableEnvironmentInstance:
            0: MATRIX_44
            64: GPU_CONSTANTS
            160: RENDER_CONSTANTS
            268: uint* (visibility)
            280: undefined4* which sets RenderableEnvironmentInstance + 0xa0 as (short)*
            310: ushort* which is used for a couple things...
               RenderableEnvironmentInstance + 0x34 (ushort*) - (ushort)((uVar1 & 4) << 2)
               if ((uVar1 & 1) != 0) then RenderableEnvironmentInstance::activate

         RenderableDynamicTempFXInstance:
            0: MATRIX_44
            64: GPU_CONSTANTS
            160: RENDER_CONSTANTS
            268: uint* (visibility)
            310: if ((uVar3 & 1) != 0) then RenderableDynamicFxInstance::activate

         RenderableDynamicFxInstance:
            0: MATRIX_44
            64: GPU_CONSTANTS 
            160: RENDER_CONSTANTS
            268: uint* (visibility)
            310: *something* 
            
         RenderableLightInstance:
            0: MATRIX_44
            64: GPU_CONSTANTS
            160: RENDER_CONSTANTS
            268: uint* (visibility)
            310: ushort* (logic checks on this value releated to activating RenderableLightInstance and calling LIGHT_MANAGER::add_dynamic_light)

         */

        public Matrix4x4 transform;
        //64
        public GPU_CONSTANTS gpuConstants;
        //144
        public UInt64 fogsphere_val1; // 0xa0 in RenderableFogSphereInstance
        public UInt64 fogsphere_val2; // 0xa8 in RenderableFogSphereInstance
        //160
        public RENDER_CONSTANTS renderConstants;
        //244
        public UInt32 renderableElementIndex; //reds.bin index
        public UInt32 renderableElementCount; //reds.bin count

        public UInt32 resourcesIndex;
        //256
        public Vector3 Unknowns5_;
        public UInt32 visibility; // pulled from iOS dump - should be visibility var?
        //272
        public ShortGuid commandsNodeID; // this is the ID of the node inside the composite, not the instanced composite node
        public ShortGuid resourcesEntryID; // NOTE: This is 'IDFromMVREntry' field on 'alien_resources_bin_entry'.
        //280
        public UInt32 environmentMapIndex; //environment_map.bin index - converted to short in code
        //284
        public float emissive_val1; //emissive surface val1
        public float emissive_val2; //emissive surface val2
        public float emissive_val3; //emissive surface val3
        //296
        public UInt32 zoneID; //zone id? RenderableScene::create_instance, RenderableScene::initialize
        public UInt32 zoneActivator;  //zone activator? RenderableScene::create_instance, RenderableScene::initialize
        //304
        public UInt32 Unknowns61_; //uVar3 in reserve_light_light_master_sets, val of LightMasterSet, or an incrementer
        public UInt16 Unknown17_;   // TODO: It is -1 most of the time, but some times it isn't.
        //310
        public UInt16 instanceTypeFlags; //ushort - used for bitwise flags depending on mover RENDERABLE_INSTANCE::Type. Environment types seem to use first bit to decide if its position comes from MVR.
        //312
        public UInt32 Unknowns70_;
        public UInt32 Unknowns71_;
        //320
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GPU_CONSTANTS //As best I can tell, this is 80 bytes long
    {
        /*
         
        As per RenderableEnvironmentInstance::set_gpu_constants, values are at:
        (undefined 8) 0
        (undefined 8) 8
        (undefined 8) 16
        (undefined 8) 24
        (undefined 8) 32
        (undefined 8) 40
        (undefined 8) 48
        (undefined 8) 56
        (undefined 8) 64
        (undefined 8) 72
         
        */


        //64
        public Vector4 LightColour;
        public Vector4 MaterialTint;
        //96
        public float lightVolumeIntensity; //todo: idk if this is right, but editing this value seems to increase/decrease the brightness of the light volume meshes
        public float particleIntensity; //0 = black particle
        public float particleSystemOffset; //todo: not sure entirely, but increasing this value seems to apply an offset to particle systems
        //108
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] blankSpace1;
        //116
        public float lightRadius;
        public Vector2 textureTile; //x = horizontal tile, y = vertical tile
        //128
        public float UnknownValue1_;
        public float UnknownValue2_;
        public float UnknownValue3_;
        public float UnknownValue4_;
        //144
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RENDER_CONSTANTS //appears to be 84 long
    {
        /*
         
        used in
          RenderableEnvironmentInstance::set_constants
          RenderableMiscInstance::set_constants 
          RenderablePlanetInstance::set_constants
        etc
         
        */

        //160
        public Vector4 Unknown3_;
        //176
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] Unknowns2_;
        //184
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public Vector3[] UnknownMinMax_; // NOTE: Sometimes I see 'nan's here too.
        //208
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
        public byte[] blankSpace3;
        //244
    }
}