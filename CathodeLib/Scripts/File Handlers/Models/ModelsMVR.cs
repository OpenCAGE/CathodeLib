﻿using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Models
{
    /* Handles CATHODE MVR files */
    public class ModelsMVR
    {
        private string FilePath = "";
        private int FileSize = 32;
        private int EntryCount = 0;
        private int EntrySize = 320;
        private int UnknownNumber = 0;

        public List<CathodeMover> Movers = new List<CathodeMover>();

        /* Load the file */
        public ModelsMVR() { }
        public ModelsMVR(string pathToFile)
        {
            FilePath = pathToFile;

            BinaryReader stream = new BinaryReader(File.OpenRead(FilePath));
            FileSize = stream.ReadInt32();
            EntryCount = stream.ReadInt32();
            UnknownNumber = stream.ReadInt32();
            stream.BaseStream.Position += 4;
            EntrySize = stream.ReadInt32();
            stream.BaseStream.Position += 12;
            Movers = new List<CathodeMover>(Utilities.ConsumeArray<CathodeMover>(stream, EntryCount));
            stream.Close();
        }

        /* Save the file */
        public void Save(string pathToFile = "")
        {
            if (pathToFile != "") FilePath = pathToFile;

            BinaryWriter stream = new BinaryWriter(File.OpenWrite(FilePath));
            stream.BaseStream.SetLength(0);
            stream.Write(FileSize);
            stream.Write(EntryCount);
            stream.Write(UnknownNumber);
            stream.Write(0);
            stream.Write(EntrySize);
            stream.Write(0); stream.Write(0); stream.Write(0);
            Utilities.Write<CathodeMover>(stream, Movers);
            stream.Close();
        }
    }

    public enum MoverType : short
    {
        PARTICLE_EMITTER_1 = 1,
        LIGHT = 2,
        STATIC_MODEL = 3,
        PARTICLE_EMITTER_2 = 4, //these seem to usually be from AYZ/FX_LIBRARY
        PARTICLE_EMITTER_3 = 5, //these seem to usually be from AYZ/LEVELS
        LIGHT_AND_PHYSICS = 6,
        DYNAMIC_MODEL = 7,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CathodeMover
    {
        public Matrix4x4 Transform;

        public Vector4 LightColour;
        public Vector4 MaterialTint;

        public float lightVolumeIntensity; //todo: idk if this is right, but editing this value seems to increase/decrease the brightness of the light volume meshes
        public float particleIntensity; //0 = black particle
        public float particleSystemOffset; //todo: not sure entirely, but increasing this value seems to apply an offset to particle systems

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] blankSpace1;

        public float lightRadius;
        public Vector2 textureTile; //x = horizontal tile, y = vertical tile

        public float UnknownValue1_;
        public float UnknownValue2_;
        public float UnknownValue3_;
        public float UnknownValue4_;

        public cGUID UnknownID; // TODO: Is this an ID or two u16s?
        public float UnknownValue5_;
        public float UnknownValue6_;
        public Int32 Unknown2_;

        public Vector4 Unknown3_;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] Unknowns2_;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public Vector3[] UnknownMinMax_; // NOTE: Sometimes I see 'nan's here too.

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 36)]
        public byte[] blankSpace3;

        public UInt32 REDSIndex; // Index 45
        public UInt32 ModelCount;
        public UInt32 ResourcesBINIndex; // NOTE: This is actually 'IndexFromMVREntry' field on 'alien_resources_bin_entry'

        public Vector4 Unknowns5_;
        public cGUID NodeID; // Index 52
        public cGUID ResourcesBINID; // NOTE: This is 'IDFromMVREntry' field on 'alien_resources_bin_entry'.
        public UInt32 EnvironmentMapBINIndex; // NOTE: Tells me which Environment Map texture to use.
        public UInt32 UnknownValue1;

        public float UnknownValue;
        public UInt32 Unknown5_;
        public cGUID CollisionMapThingID;
        public UInt32 Unknowns60_;
        public UInt32 Unknowns61_;
        public UInt16 Unknown17_;   // TODO: It is -1 most of the time, but some times it isn't.
        public MoverType IsThisTypeID; // TODO: So far 3: Static, 6: Physics (Cone, Box) 7: Scripted (Door, Camera).
        public UInt32 Unknowns70_;
        public UInt32 Unknowns71_;
    };
}