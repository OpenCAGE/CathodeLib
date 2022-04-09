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
    /* Handles Cathode ENVIRONMENT_ANIMATION.DAT files */
    public class EnvironmentAnimationDatabase
    {
        private string filepath;
        private EnvironmentAnimationHeader Header;
        private EnvironmentAnimationEntry1[] Entries0;
        private Matrix4x4[] Matrices0;
        private Matrix4x4[] Matrices1;
        private int[] IDs0;
        private int[] IDs1;
        private EnvironmentAnimationEntry2[] Entries1;

        /* Load the file */
        public EnvironmentAnimationDatabase(string path)
        {
            filepath = path;

            BinaryReader Stream = new BinaryReader(File.OpenRead(filepath));
            Header = Utilities.Consume<EnvironmentAnimationHeader>(Stream);
            Entries0 = Utilities.ConsumeArray<EnvironmentAnimationEntry1>(Stream, (int)Header.EntryCount0);
            Matrices0 = Utilities.ConsumeArray<Matrix4x4>(Stream, (int)Header.EntryCount0);
            Matrices1 = Utilities.ConsumeArray<Matrix4x4>(Stream, (int)Header.EntryCount0);
            IDs0 = Utilities.ConsumeArray<int>(Stream, (int)Header.EntryCount0);
            IDs1 = Utilities.ConsumeArray<int>(Stream, (int)Header.EntryCount0);
            Entries1 = Utilities.ConsumeArray<EnvironmentAnimationEntry2>( Stream, (int)Header.EntryCount0);
            Stream.Close();
        }

        /* Save the file */
        public void Save()
        {
            BinaryWriter stream = new BinaryWriter(File.OpenWrite(filepath));
            stream.BaseStream.SetLength(0);
            Utilities.Write<EnvironmentAnimationHeader>(stream, Header);
            Utilities.Write<EnvironmentAnimationEntry1>(stream, Entries0);
            Utilities.Write<Matrix4x4>(stream, Matrices0);
            Utilities.Write<Matrix4x4>(stream, Matrices1);
            Utilities.Write<int>(stream, IDs0);
            Utilities.Write<int>(stream, IDs1);
            Utilities.Write<EnvironmentAnimationEntry2>(stream, Entries1);
            stream.Close();
        }
        

        //TODO: edit functions
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EnvironmentAnimationHeader
    {
        public int Version;
        public int TotalFileSize;

        public int MatricesOffset0;
        public int MatrixCount0;

        public int MatricesOffset1;
        public int MatrixCount1;

        public int EntriesOffset1;
        public int EntryCount1;

        public int EntriesOffset0;
        public int EntryCount0;

        public int IDsOffset0;
        public int IDCount0;

        public int IDsOffset1;
        public int IDCount1;

        public int Unknown0_;
        public int Unknown1_;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EnvironmentAnimationEntry1
    {
        public Matrix4x4 Matrix;
        public int ID;
        public int UnknownZero0_;
        public int ResourceIndex;
        public int IDCount0;
        public int IDIndex0;
        public int IDCount1;
        public int IDIndex1;
        public int MatrixCount;
        public int MatrixIndex;
        public int EntryIndex1;
        public int EntryCount1;
        public int UnknownZero1_;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EnvironmentAnimationEntry2
    {
        public int ID;
        public Vector3 P;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public int[] V;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Unknown_;
    };
}