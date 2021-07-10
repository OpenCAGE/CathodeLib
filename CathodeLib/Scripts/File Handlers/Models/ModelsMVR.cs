using System.Collections.Generic;
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
        private string filepath;
        private alien_mvr_header header;
        private alien_mvr_entry[] movers;

        /* Load the file */
        public ModelsMVR(string pathToFile)
        {
            filepath = pathToFile;

            BinaryReader stream = new BinaryReader(File.OpenRead(filepath));
            header = Utilities.Consume<alien_mvr_header>(stream);
            movers = Utilities.ConsumeArray<alien_mvr_entry>(stream, (int)header.EntryCount);
            stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<alien_mvr_header>(stream, header);
            Utilities.Write<alien_mvr_entry>(stream, movers);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return movers.Length; } }
        public alien_mvr_entry[] Entries { get { return movers; } }
        public alien_mvr_entry GetEntry(int i)
        {
            return movers[i];
        }

        /* Data setters */
        public void SetEntry(int i, alien_mvr_entry content)
        {
            movers[i] = content;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_mvr_header
    {
        public uint Unknown0_;
        public uint EntryCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public uint[] Unknown1_; //6
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_mvr_entry
    {
        public Matrix4x4 Transform;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public Vector4[] InstanceState;
        public UInt32 UnknownID; // TODO: Is this an ID or two u16s?
        public float UnknownValue3_;
        public float UnknownValue4_;
        public Int32 Unknown2_;

        public Vector4 Unknown3_;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] Unknowns2_;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public Vector3[] UnknownMinMax_; // NOTE: Sometimes I see 'nan's here too.
        public Vector4 Zeros0_; // NOTE: Not verified by an assert. Verified by looking at MaxValues at the end of dump.

        public Vector4 Zeros1_; // NOTE: Not verified by an assert. Verified by looking at MaxValues at the end of dump.
        public UInt32 Zeros2_; // NOTE: Not verified by an assert. Verified by looking at MaxValues at the end of dump.
        public UInt32 REDSIndex; // Index 45
        public UInt32 ModelCount;
        public UInt32 ResourcesBINIndex; // NOTE: This is actually 'IndexFromMVREntry' field on 'alien_resources_bin_entry'

        public Vector4 Unknowns5_;
        public UInt32 NodeID; // Index 52
        public UInt32 ResourcesBINID; // NOTE: This is 'IDFromMVREntry' field on 'alien_resources_bin_entry'.
        public UInt32 EnvironmentMapBINIndex; // NOTE: Tells me which Environment Map texture to use.
        public UInt32 UnknownValue1;

        public float UnknownValue;
        public UInt32 Unknown5_;
        public UInt32 CollisionMapThingID;
        public UInt32 Unknowns60_;
        public UInt32 Unknowns61_;
        public UInt16 Unknown17_;   // TODO: It is -1 most of the time, but some times it isn't.
        public UInt16 IsThisTypeID; // TODO: So far 3: Static, 6: Physics (Cone, Box) 7: Scripted (Door, Camera).
        public UInt32 Unknowns70_;
        public UInt32 Unknowns71_;
    };
}