﻿using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Misc
{
    /* Handles CATHODE PHYSICS.MAP files */
    public class PhysicsMAP
    {
        private string filepath;
        public alien_collision_map_header header;
        public alien_collision_map_entry[] entries;

        /* Load the file */
        public PhysicsMAP(string path)
        {
            filepath = path;

            BinaryReader stream = new BinaryReader(File.OpenRead(path));
            header = Utilities.Consume<alien_collision_map_header>(stream);
            entries = Utilities.ConsumeArray<alien_collision_map_entry>(stream, header.EntryCount);
            stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<alien_collision_map_header>(stream, header);
            Utilities.Write<alien_collision_map_entry>(stream, entries);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return entries.Length; } }
        public alien_collision_map_entry[] Entries { get { return entries; } }
        public alien_collision_map_entry GetEntry(int i)
        {
            return entries[i];
        }

        /* Data setters */
        public void SetEntry(int i, alien_collision_map_entry content)
        {
            entries[i] = content;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_physics_map_header
    {
        public int FileSizeExcludingThis;
        public int EntryCount;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct alien_physics_map_entry
    {
        public int UnknownNotableValue_;
        public int UnknownZero;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public int[] IDs; //4
        public Vector4 Row0; // NOTE: This is a 3x4 matrix, seems to have rotation data on the leftmost 3x3 matrix, and position
        public Vector4 Row1; //   on the rightmost 3x1 matrix.
        public Vector4 Row2;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public int[] UnknownZeros_; //2
    };
}