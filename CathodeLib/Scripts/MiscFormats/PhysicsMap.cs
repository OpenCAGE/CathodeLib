using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Misc
{
    /* Handles Cathode PHYSICS.MAP files */
    public class PhysicsMap
    {
        private string filepath;
        public PhysicsMapHeader header;
        public PhysicsMapEntry[] entries;

        /* Load the file */
        public PhysicsMap(string path)
        {
            filepath = path;

            BinaryReader stream = new BinaryReader(File.OpenRead(path));
            header = Utilities.Consume<PhysicsMapHeader>(stream);
            entries = Utilities.ConsumeArray<PhysicsMapEntry>(stream, header.EntryCount);
            stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<PhysicsMapHeader>(stream, header);
            Utilities.Write<PhysicsMapEntry>(stream, entries);
            stream.Close();
        }

        /* Data accessors */
        public int EntryCount { get { return entries.Length; } }
        public PhysicsMapEntry[] Entries { get { return entries; } }
        public PhysicsMapEntry GetEntry(int i)
        {
            return entries[i];
        }

        /* Data setters */
        public void SetEntry(int i, PhysicsMapEntry content)
        {
            entries[i] = content;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PhysicsMapHeader
    {
        public int FileSizeExcludingThis;
        public int EntryCount;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PhysicsMapEntry
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