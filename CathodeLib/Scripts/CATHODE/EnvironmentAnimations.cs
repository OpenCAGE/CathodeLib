using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Linq;
using static CATHODE.CollisionMaps;
using CathodeLib.ObjectExtensions;


#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/ENVIRONMENT_ANIMATION.DAT
    /// </summary>
    public class EnvironmentAnimations : CathodeFile
    {
        public List<EnvironmentAnimation> Entries = new List<EnvironmentAnimation>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE | Implementation.SAVE;

        public EnvironmentAnimations(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public EnvironmentAnimations(MemoryStream stream, AnimationStrings strings, string path = "") : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public EnvironmentAnimations(byte[] data, AnimationStrings strings, string path = "") : base(data, path)
        {
            _strings = strings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private List<EnvironmentAnimation> _writeList = new List<EnvironmentAnimation>();

        private AnimationStrings _strings;

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null)
                return false;


            //TODO: this is a mapping of ModelReference entities within the composite with EnvironmentModelReference in
            //      the IDs should line up.

            using (BinaryReader reader = new BinaryReader(stream))
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
                EnvironmentAnimation.Info[] Entries1 = Utilities.ConsumeArray<EnvironmentAnimation.Info>(reader, entries1.EntryCount);

                //Jump back to our main definition and read all additional content in
                reader.BaseStream.Position = entries0.GlobalOffset;
                for (int i = 0; i < entries0.EntryCount; i++)
                {
                    //Each entry here defines info for a Composite which has a EnvironmentModelReference entity

                    EnvironmentAnimation anim = new EnvironmentAnimation();

                    Matrix4x4 Matrix = Utilities.Consume<Matrix4x4>(reader); //This is always identity

                    uint id = reader.ReadUInt32();
                    if (_strings.Entries.TryGetValue(id, out string name))
                    {
                        anim.SkeletonName = name;
                    }
                    else
                    {
                        Console.WriteLine("WARNING: Skeleton ID " + id + " could not look up a name!");
                        anim.SkeletonName = id.ToString();
                    }
                    reader.BaseStream.Position += 4;
                    anim.ResourceIndex = reader.ReadInt32(); //the index which links through to the resource reference in COMMANDS

                    anim.Indexes0 = PopulateArray<ShortGuid>(reader, IDs0); 
                    anim.Indexes1 = PopulateArray<ShortGuid>(reader, IDs1); //ShortGuids for all RENDERABLE_INSTANCE resource references in the composite

                    int matrix_count = reader.ReadInt32();
                    int matrix_index = reader.ReadInt32();
                    anim.Matrices0 = PopulateArray<Matrix4x4>(matrix_count, matrix_index, Matrices0); //matches length of Indexes0
                    anim.Matrices1 = PopulateArray<Matrix4x4>(matrix_count, matrix_index, Matrices1); //matches length of Indexes0

                    anim.Data0 = PopulateArray<EnvironmentAnimation.Info>(reader, Entries1);

                    anim.unk1 = reader.ReadInt32(); //This is always zero, but is 1 for some HAB_AIRPORT entries
                    Entries.Add(anim);
                }
            }
            _writeList.AddRange(Entries);
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
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    Utilities.Write(writer, Matrix4x4.identity);
#else
                    Utilities.Write(writer, Matrix4x4.Identity);
#endif
                    Utilities.Write(writer, Utilities.AnimationHashedString(Entries[i].SkeletonName));
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
            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(EnvironmentAnimation envAnim)
        {
            if (!_writeList.Contains(envAnim)) return -1;
            return _writeList.IndexOf(envAnim);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public EnvironmentAnimation GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public EnvironmentAnimation AddEntry(EnvironmentAnimation envAnim)
        {
            if (envAnim == null)
                return null;

            var existing = Entries.FirstOrDefault(o => o == envAnim);
            if (existing != null)
                return existing;

            EnvironmentAnimation newEnvAnim = envAnim.Copy();
            Entries.Add(newEnvAnim);
            return newEnvAnim;
        }

        private List<T> PopulateArray<T>(BinaryReader reader, T[] array)
        {
            List<T> arr = new List<T>();
            int count = reader.ReadInt32();
            int index = reader.ReadInt32(); 
            if (typeof(T) == typeof(EnvironmentAnimation.Info)) 
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
        public class EnvironmentAnimation : IEquatable<EnvironmentAnimation>
        {
            public string SkeletonName; //we write this using AnimationHashedString
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

            public List<Info> Data0;

            public int unk1 = 0;

            public static bool operator ==(EnvironmentAnimation x, EnvironmentAnimation y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.SkeletonName != y.SkeletonName) return false;
                if (x.ResourceIndex != y.ResourceIndex) return false;
                if (x.unk1 != y.unk1) return false;
                if (!ListsEqual(x.Indexes0, y.Indexes0)) return false;
                if (!ListsEqual(x.Indexes1, y.Indexes1)) return false;
                if (!ListsEqual(x.Matrices0, y.Matrices0)) return false;
                if (!ListsEqual(x.Matrices1, y.Matrices1)) return false;
                if (!ListsEqual(x.Data0, y.Data0)) return false;
                return true;
            }

            public static bool operator !=(EnvironmentAnimation x, EnvironmentAnimation y)
            {
                return !(x == y);
            }

            public bool Equals(EnvironmentAnimation other)
            {
                return this == other;
            }

            public override bool Equals(object obj)
            {
                return obj is EnvironmentAnimation anim && this == anim;
            }

            public override int GetHashCode()
            {
                int hashCode = -1234567890;
                hashCode = hashCode * -1521134295 + (SkeletonName?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + ResourceIndex.GetHashCode();
                hashCode = hashCode * -1521134295 + unk1.GetHashCode();
                hashCode = hashCode * -1521134295 + (Indexes0?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (Indexes1?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (Matrices0?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (Matrices1?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (Data0?.GetHashCode() ?? 0);
                return hashCode;
            }

            private static bool ListsEqual<T>(List<T> x, List<T> y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                if (x.Count != y.Count) return false;
                for (int i = 0; i < x.Count; i++)
                {
                    if (!EqualityComparer<T>.Default.Equals(x[i], y[i])) return false;
                }
                return true;
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct Info : IEquatable<Info>
            {
                public ShortGuid ID; //id is only found in this file
                public Vector3 P;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
                public float[] V;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
                public byte[] Unknown_;

                public static bool operator ==(Info x, Info y)
                {
                    if (x.ID != y.ID) return false;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    if (x.P != y.P) return false;
#else
                    if (x.P.X != y.P.X || x.P.Y != y.P.Y || x.P.Z != y.P.Z) return false;
#endif
                    if (!ArraysEqual(x.V, y.V)) return false;
                    if (!ArraysEqual(x.Unknown_, y.Unknown_)) return false;
                    return true;
                }

                public static bool operator !=(Info x, Info y)
                {
                    return !(x == y);
                }

                public bool Equals(Info other)
                {
                    return this == other;
                }

                public override bool Equals(object obj)
                {
                    return obj is Info info && this == info;
                }

                public override int GetHashCode()
                {
                    int hashCode = -1234567890;
                    hashCode = hashCode * -1521134295 + ID.GetHashCode();
                    hashCode = hashCode * -1521134295 + P.GetHashCode();
                    hashCode = hashCode * -1521134295 + (V?.GetHashCode() ?? 0);
                    hashCode = hashCode * -1521134295 + (Unknown_?.GetHashCode() ?? 0);
                    return hashCode;
                }

                private static bool ArraysEqual(byte[] x, byte[] y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return false;
                    if (x.Length != y.Length) return false;
                    for (int i = 0; i < x.Length; i++)
                    {
                        if (x[i] != y[i]) return false;
                    }
                    return true;
                }

                private static bool ArraysEqual(float[] x, float[] y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return false;
                    if (x.Length != y.Length) return false;
                    for (int i = 0; i < x.Length; i++)
                    {
                        if (x[i] != y[i]) return false;
                    }
                    return true;
                }
            };
        }
        #endregion
    }
}