using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CATHODE.Scripting;
using CathodeLib;
using System;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/ENVIRONMENT_ANIMATION.DAT */
    public class EnvironmentAnimations : CathodeFile
    {
        public List<EnvironmentAnimation> Entries = new List<EnvironmentAnimation>();
        public static new Implementation Implementation = Implementation.LOAD;
        public EnvironmentAnimations(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {

            //TODO: this is a mapping of ModelReference entities within the composite with EnvironmentModelReference in
            //      the IDs should line up.

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
                //Here there's always 112, 1

                //Jump down and read all content we'll consume into our EnvironmentAnimation
                reader.BaseStream.Position = matrix0.GlobalOffset;
                Matrix4x4[] Matrices0 = Utilities.ConsumeArray<Matrix4x4>(reader, matrix0.EntryCount);
                Matrix4x4[] Matrices1 = Utilities.ConsumeArray<Matrix4x4>(reader, matrix1.EntryCount);
                ShortGuid[] IDs0 = Utilities.ConsumeArray<ShortGuid>(reader, ids0.EntryCount);
                ShortGuid[] IDs1 = Utilities.ConsumeArray<ShortGuid>(reader, ids1.EntryCount);
                EnvironmentAnimationInfo[] Entries1 = Utilities.ConsumeArray<EnvironmentAnimationInfo>(reader, entries1.EntryCount);

                //Jump back to our main definition and read all additional content in
                reader.BaseStream.Position = entries0.GlobalOffset;
                for (int i = 0; i < entries0.EntryCount; i++)
                {
                    //Each entry here defines info for a Composite which has a EnvironmentModelReference entity

                    EnvironmentAnimation anim = new EnvironmentAnimation();
                    anim.Matrix = Utilities.Consume<Matrix4x4>(reader); //This is always identity
                    anim.ID = Utilities.Consume<ShortGuid>(reader); //This ID is not unique... Is it defo an ID? It doesn't show up in COMMANDS
                    reader.BaseStream.Position += 4;
                    anim.ResourceIndex = reader.ReadInt32(); //the index which links through to the resource reference in COMMANDS

                    anim.Indexes0 = PopulateArray<ShortGuid>(reader, IDs0); 
                    anim.Indexes1 = PopulateArray<ShortGuid>(reader, IDs1); //ShortGuids for all RENDERABLE_INSTANCE resource references in the composite

                    int matrix_count = reader.ReadInt32();
                    int matrix_index = reader.ReadInt32();
                    anim.Matrices0 = PopulateArray<Matrix4x4>(matrix_count, matrix_index, Matrices0); //matches length of Indexes0
                    anim.Matrices1 = PopulateArray<Matrix4x4>(matrix_count, matrix_index, Matrices1); //matches length of Indexes0

                    anim.Data0 = PopulateArray<EnvironmentAnimationInfo>(reader, Entries1);

                    anim.unk1 = reader.ReadInt32(); //This is always zero, but is 1 for some HAB_AIRPORT entries
                    Entries.Add(anim);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write((Int32)4);
                writer.Write((Int32)0);
                writer.Write(new byte[56]);
                writer.Write(new byte[112 * Entries.Count]);

                OffsetPair Matrices0 = new OffsetPair() { GlobalOffset = (int)writer.BaseStream.Position };
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].Matrices0);
                    Matrices0.EntryCount += Entries[i].Matrices0.Count;
                }
                OffsetPair Matrices1 = new OffsetPair() { GlobalOffset = (int)writer.BaseStream.Position };
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].Matrices1);
                    Matrices1.EntryCount += Entries[i].Matrices1.Count;
                }
                OffsetPair IDs0 = new OffsetPair() { GlobalOffset = (int)writer.BaseStream.Position };
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].Indexes0);
                    IDs0.EntryCount += Entries[i].Indexes0.Count;
                }
                OffsetPair IDs1 = new OffsetPair() { GlobalOffset = (int)writer.BaseStream.Position };
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].Indexes1);
                    IDs1.EntryCount += Entries[i].Indexes1.Count;
                }
                OffsetPair Entries1 = new OffsetPair() { GlobalOffset = (int)writer.BaseStream.Position };
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].Data0);
                    Entries1.EntryCount += Entries[i].Data0.Count;
                }

                writer.BaseStream.Position = 4;
                writer.Write((Int32)writer.BaseStream.Length);
                Utilities.Write(writer, Matrices0);
                Utilities.Write(writer, Matrices1);
                Utilities.Write(writer, Entries1);
                writer.Write((Int32)64);
                writer.Write((Int32)Entries.Count);
                Utilities.Write(writer, IDs0);
                Utilities.Write(writer, IDs1);
                writer.Write((Int32)112);
                writer.Write((Int32)1);

                int stacked_Matrices = 0;
                int stacked_IDs0 = 0;
                int stacked_IDs1 = 0;
                int stacked_Entries1 = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].Matrix);
                    Utilities.Write(writer, Entries[i].ID);
                    writer.Write((Int32)0);
                    Utilities.Write(writer, Entries[i].ResourceIndex);

                    writer.Write(Entries[i].Indexes0.Count);
                    writer.Write((Int32)stacked_IDs0);
                    stacked_IDs0 += Entries[i].Indexes0.Count;
                    writer.Write(Entries[i].Indexes1.Count);
                    writer.Write((Int32)stacked_IDs1);
                    stacked_IDs1 += Entries[i].Indexes1.Count;
                    writer.Write(Entries[i].Matrices0.Count);
                    writer.Write((Int32)stacked_Matrices);
                    stacked_Matrices += Entries[i].Matrices0.Count;
                    writer.Write((Int32)stacked_Entries1);
                    writer.Write(Entries[i].Data0.Count);
                    stacked_Entries1 += Entries[i].Data0.Count;

                    writer.Write(Entries[i].unk1);
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

            //There are two types of EnvironmentAnimation:
            // - Skinned - usually referenced by DisplayModel (a composite defining a skinned mesh, used for a character, etc)
            // - Non-Skinned (a composite defining a mesh like a weapon, etc)

            //If the composite is skinned:
            // - Indexes0 is used to define skinning data (unsure what)
            // - Indexes1 is used to define RENDERABLE_INSTANCE IDs (which it turns out are ShortGuids of the model submesh name most the time). Can also include the node name for EnvironmentModelReference??

            //If the composite is static:
            // - Indexes0 is used to define RENDERABLE_INSTANCE IDs (see above)
            // - Indexes1 is unused

            public List<ShortGuid> Indexes0;
            public List<ShortGuid> Indexes1;

            public List<Matrix4x4> Matrices0;
            public List<Matrix4x4> Matrices1;

            public List<EnvironmentAnimationInfo> Data0;

            public int unk1 = 0;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct EnvironmentAnimationInfo
        {
            public ShortGuid ID; //id is only found in this file
            public Vector3 P;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public float[] V;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] Unknown_;
        };
        #endregion
    }
}