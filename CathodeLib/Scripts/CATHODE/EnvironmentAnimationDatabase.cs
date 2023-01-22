using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CATHODE.Scripting;
using CathodeLib;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /* Handles Cathode ENVIRONMENT_ANIMATION.DAT files */
    public class EnvironmentAnimationDatabase : CathodeFile
    {
        public List<EnvironmentAnimation> Entries = new List<EnvironmentAnimation>();
        public static new Impl Implementation = Impl.LOAD;
        public EnvironmentAnimationDatabase(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                //Read header
                reader.BaseStream.Position += 8; //Skip version and filesize
                OffsetPair matrix0 = Utilities.Consume<OffsetPair>(reader);
                OffsetPair matrix1 = Utilities.Consume<OffsetPair>(reader);
                OffsetPair entries1 = Utilities.Consume<OffsetPair>(reader);
                OffsetPair entries0 = Utilities.Consume<OffsetPair>(reader);
                OffsetPair ids0 = Utilities.Consume<OffsetPair>(reader);
                OffsetPair ids1 = Utilities.Consume<OffsetPair>(reader);
                reader.BaseStream.Position += 8; //Skip unknown

                //Jump down and read all content we'll consume into our EnvironmentAnimation
                reader.BaseStream.Position = matrix0.GlobalOffset;
                Matrix4x4[] Matrices0 = Utilities.ConsumeArray<Matrix4x4>(reader, matrix0.EntryCount);
                Matrix4x4[] Matrices1 = Utilities.ConsumeArray<Matrix4x4>(reader, matrix1.EntryCount);
                int[] IDs0 = Utilities.ConsumeArray<int>(reader, ids0.EntryCount);
                int[] IDs1 = Utilities.ConsumeArray<int>(reader, ids1.EntryCount);
                EnvironmentAnimationInfo[] Entries1 = Utilities.ConsumeArray<EnvironmentAnimationInfo>(reader, entries1.EntryCount);

                //Jump back to our main definition and read all additional content in
                reader.BaseStream.Position = entries0.GlobalOffset;
                for (int i = 0; i < entries0.EntryCount; i++)
                {
                    EnvironmentAnimation anim = new EnvironmentAnimation();
                    anim.Matrix = Utilities.Consume<Matrix4x4>(reader);
                    anim.ID = Utilities.Consume<ShortGuid>(reader);
                    reader.BaseStream.Position += 4;
                    anim.ResourceIndex = reader.ReadInt32();

                    anim.Indexes0 = PopulateArray<int>(reader, IDs0);
                    anim.Indexes1 = PopulateArray<int>(reader, IDs1);

                    int matrix_count = reader.ReadInt32();
                    int matrix_index = reader.ReadInt32();
                    anim.Matrices0 = PopulateArray<Matrix4x4>(matrix_count, matrix_index, Matrices0);
                    anim.Matrices1 = PopulateArray<Matrix4x4>(matrix_count, matrix_index, Matrices1);

                    anim.Data0 = PopulateArray<EnvironmentAnimationInfo>(reader, Entries1);

                    reader.BaseStream.Position += 4; //TODO: i think this might be a flag - it's usually zero but has been 1 on hab_airport
                    Entries.Add(anim);
                }
            }
            return true;
        }
        #endregion

        #region HELPERS
        private List<T> PopulateArray<T>(BinaryReader reader, T[] array)
        {
            List<T> arr = new List<T>();
            int count = reader.ReadInt32();
            int index = reader.ReadInt32(); 
            if (typeof(T) == typeof(EnvironmentAnimationInfo)) 
            {
                //Hacky fix for EnvironmentAnimationInfo pointers count/index order being inverted
                for (int x = 0; x < index; x++)
                    arr.Add(array[count + x]);
            }
            else
            {
                for (int x = 0; x < count; x++)
                    arr.Add(array[index + x]);
            }
            return arr;
        }
        private List<T> PopulateArray<T>(int count, int index, T[] array)
        {
            List<T> arr = new List<T>();
            for (int x = 0; x < count; x++)
                arr.Add(array[index + x]);
            return arr;
        }
        #endregion

        #region STRUCTURES
        public class EnvironmentAnimation
        {
            public Matrix4x4 Matrix;
            public ShortGuid ID;
            public int ResourceIndex; //This matches the ANIMATED_MODEL resource reference

            public List<int> Indexes0;
            public List<int> Indexes1;

            public List<Matrix4x4> Matrices0;
            public List<Matrix4x4> Matrices1;

            public List<EnvironmentAnimationInfo> Data0;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct EnvironmentAnimationInfo
        {
            public ShortGuid ID;
            public Vector3 P;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public float[] V;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Unknown_;
        };
        #endregion
    }
}