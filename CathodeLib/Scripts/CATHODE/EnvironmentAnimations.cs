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
#elif GODOT
using Godot;
using System.Numerics;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;
using Vector4 = Godot.Vector4;
using Color = Godot.Color;
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

        private AnimationStrings _strings;
        private List<EnvironmentAnimation> _writeList = new List<EnvironmentAnimation>();

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

        public void ClearReferences()
        {
            _strings = null;
        }

        ~EnvironmentAnimations()
        {
            ClearReferences();
            _writeList.Clear();
            Entries.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null)
                return false;

            using (BinaryReader reader = new BinaryReader(stream))
            {
                //Read header
                reader.BaseStream.Position += 8; //Skip version and filesize
                OffsetPair inverseBindPoseTablePtr = Utilities.Consume<OffsetPair>(reader);
                OffsetPair hkxToCs2MappingPtr = Utilities.Consume<OffsetPair>(reader);
                OffsetPair helperMatrixTablePtr = Utilities.Consume<OffsetPair>(reader);
                OffsetPair modelsTablePtr = Utilities.Consume<OffsetPair>(reader);
                OffsetPair boneMappingTablePtr = Utilities.Consume<OffsetPair>(reader);
                OffsetPair meshMappingTablePtr = Utilities.Consume<OffsetPair>(reader);

                //Jump down and read all content we'll consume into our EnvironmentAnimation
                reader.BaseStream.Position = inverseBindPoseTablePtr.GlobalOffset;
                Matrix4x4[] inverseBindPoseTable = Utilities.ConsumeArray<Matrix4x4>(reader, inverseBindPoseTablePtr.EntryCount);
                reader.BaseStream.Position = hkxToCs2MappingPtr.GlobalOffset;
                Matrix4x4[] hkxToCs2Mapping = Utilities.ConsumeArray<Matrix4x4>(reader, hkxToCs2MappingPtr.EntryCount);
                reader.BaseStream.Position = helperMatrixTablePtr.GlobalOffset;
                WeightedHelperData[] helperMatrixTable = Utilities.ConsumeArray<WeightedHelperData>(reader, helperMatrixTablePtr.EntryCount);
                reader.BaseStream.Position = boneMappingTablePtr.GlobalOffset;
                ShortGuid[] boneMappingTable = Utilities.ConsumeArray<ShortGuid>(reader, boneMappingTablePtr.EntryCount);
                reader.BaseStream.Position = meshMappingTablePtr.GlobalOffset;
                ShortGuid[] meshMappingTable = Utilities.ConsumeArray<ShortGuid>(reader, meshMappingTablePtr.EntryCount);

                //Jump back to our main definition and read all additional content in
                reader.BaseStream.Position = modelsTablePtr.GlobalOffset;
                for (int i = 0; i < modelsTablePtr.EntryCount; i++)
                {
                    EnvironmentAnimation anim = new EnvironmentAnimation();
                    anim.Matrix = Utilities.Consume<Matrix4x4>(reader);
                    uint skeletonNameID = reader.ReadUInt32();
                    if (_strings.Entries.TryGetValue(skeletonNameID, out string name))
                    {
                        anim.SkeletonName = name;
                    }
                    else
                    {
                        Console.WriteLine("WARNING: Skeleton ID " + skeletonNameID + " could not look up a name!");
                        anim.SkeletonName = skeletonNameID.ToString();
                    }
                    anim.AnimationSet = reader.ReadUInt32();  // probably should look this up too?
                    anim.ID = reader.ReadInt32(); //the index which links through to the resource reference in COMMANDS

                    anim.BoneMappings = PopulateArray<ShortGuid>(reader, boneMappingTable); // ShortGuids of bone/node names and/or RENDERABLE_INSTANCE resource ids (see MeshMappings)
                    anim.MeshMappings = PopulateArray<ShortGuid>(reader, meshMappingTable); // Often empty; when present, ShortGuids for RENDERABLE_INSTANCE resource refs in the composite

                    int matrix_count = reader.ReadInt32();
                    int matrix_index = reader.ReadInt32();
                    anim.InverseBindPoses = PopulateArray<Matrix4x4>(matrix_count, matrix_index, inverseBindPoseTable);
                    anim.HavokToCathodeMappings = PopulateArray<Matrix4x4>(matrix_count, matrix_index, hkxToCs2Mapping);

                    anim.HelperMatrices = PopulateArray<WeightedHelperData>(reader, helperMatrixTable);

                    reader.BaseStream.Position += 4;
                    Entries.Add(anim);
                }
            }
            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            int[] resourceIndexes = Entries.Select(e => e.ID).ToArray();
            int[] boneMappingOffsets = BuildSharedShortGuidPack(Entries.Select(e => e.BoneMappings).ToList(), resourceIndexes, out List<ShortGuid> packedBoneMappings);
            int[] meshMappingOffsets = BuildSharedShortGuidPack(Entries.Select(e => e.MeshMappings).ToList(), resourceIndexes, out List<ShortGuid> packedMeshMappings);

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write((Int32)4);
                writer.Write((Int32)0);
                writer.Write(new byte[56]);
                writer.Write(new byte[112 * Entries.Count]);

                OffsetPair inverseBindPoseTable = new OffsetPair() { GlobalOffset = (int)writer.BaseStream.Position };
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].InverseBindPoses);
                    inverseBindPoseTable.EntryCount += Entries[i].InverseBindPoses.Count;
                }
                OffsetPair hkxToCs2Mapping = new OffsetPair() { GlobalOffset = (int)writer.BaseStream.Position };
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].HavokToCathodeMappings);
                    hkxToCs2Mapping.EntryCount += Entries[i].HavokToCathodeMappings.Count;
                }
                OffsetPair boneMappingTable = new OffsetPair() { GlobalOffset = (int)writer.BaseStream.Position, EntryCount = packedBoneMappings.Count };
                Utilities.Write(writer, packedBoneMappings);
                OffsetPair meshMappingTable = new OffsetPair() { GlobalOffset = (int)writer.BaseStream.Position, EntryCount = packedMeshMappings.Count };
                Utilities.Write(writer, packedMeshMappings);
                OffsetPair helperMatrixTable = new OffsetPair() { GlobalOffset = (int)writer.BaseStream.Position };
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].HelperMatrices);
                    helperMatrixTable.EntryCount += Entries[i].HelperMatrices.Count;
                }

                writer.BaseStream.Position = 4;
                writer.Write((Int32)writer.BaseStream.Length);
                Utilities.Write(writer, inverseBindPoseTable);
                Utilities.Write(writer, hkxToCs2Mapping);
                Utilities.Write(writer, helperMatrixTable);
                writer.Write((Int32)64);
                writer.Write((Int32)Entries.Count);
                Utilities.Write(writer, boneMappingTable);
                Utilities.Write(writer, meshMappingTable);
                writer.Write((Int32)112);
                writer.Write((Int32)1);

                int stacked_Matrices = 0;
                int stacked_Helpers = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write(writer, Entries[i].Matrix);
                    Utilities.Write(writer, Utilities.AnimationHashedString(Entries[i].SkeletonName));
                    writer.Write(Entries[i].AnimationSet);
                    Utilities.Write(writer, Entries[i].ID);

                    writer.Write(Entries[i].BoneMappings.Count);
                    writer.Write(boneMappingOffsets[i]);
                    writer.Write(Entries[i].MeshMappings.Count);
                    writer.Write(meshMappingOffsets[i]);
                    writer.Write(Entries[i].InverseBindPoses.Count);
                    writer.Write((Int32)stacked_Matrices);
                    stacked_Matrices += Entries[i].InverseBindPoses.Count;
                    writer.Write((Int32)stacked_Helpers);
                    writer.Write(Entries[i].HelperMatrices.Count);
                    stacked_Helpers += Entries[i].HelperMatrices.Count;

                    writer.Write(0);
                }
            }
            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }

        /// <summary>
        /// Pack ShortGuid lists, sharing packed ranges only for adjacent entries that already
        /// share an ID and identical list content (retail twin-skeleton pattern).
        /// Broader content-addressed dedupe shrinks the table past retail and is unnecessary.
        /// </summary>
        private static int[] BuildSharedShortGuidPack(List<List<ShortGuid>> lists, int[] resourceIndexes, out List<ShortGuid> packed)
        {
            packed = new List<ShortGuid>();
            int[] offsets = new int[lists.Count];
            for (int i = 0; i < lists.Count; i++)
            {
                List<ShortGuid> list = lists[i] ?? new List<ShortGuid>();
                if (i > 0
                    && resourceIndexes[i] == resourceIndexes[i - 1]
                    && ShortGuidListsEqual(lists[i - 1], list))
                {
                    offsets[i] = offsets[i - 1];
                    continue;
                }
                offsets[i] = packed.Count;
                packed.AddRange(list);
            }
            return offsets;
        }

        private static bool ShortGuidListsEqual(List<ShortGuid> a, List<ShortGuid> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
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
        /// Dedupes by content (ignoring <see cref="EnvironmentAnimation.ID"/>); assigns a unique ID to new entries.
        /// </summary>
        public EnvironmentAnimation ImportEntry(EnvironmentAnimation envAnim)
        {
            if (envAnim == null)
                return null;

            var existing = Entries.FirstOrDefault(o => o != null && o.ContentEquals(envAnim));
            if (existing != null)
                return existing;

            EnvironmentAnimation newEnvAnim = envAnim.Copy();
            newEnvAnim.ID = AllocateUniqueId();
            Entries.Add(newEnvAnim);
            return newEnvAnim;
        }

        /// <summary>
        /// Next free ANIMATED_MODEL / EnvironmentAnimation ID for this level.
        /// </summary>
        public int AllocateUniqueId()
        {
            int max = -1;
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].ID > max)
                    max = Entries[i].ID;
            }
            return max + 1;
        }

        private List<T> PopulateArray<T>(BinaryReader reader, T[] array)
        {
            List<T> arr = new List<T>();
            int count = reader.ReadInt32();
            int index = reader.ReadInt32(); 
            if (typeof(T) == typeof(WeightedHelperData)) 
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
            public Matrix4x4 Matrix; //This is always identity
            public string SkeletonName; //we write this using AnimationHashedString
            public uint AnimationSet; //maybe also anim hashed string?
            public int ID; //This matches the ANIMATED_MODEL resource reference

            public List<ShortGuid> BoneMappings; // Skinning targets: bone/node name hashes and/or RENDERABLE_INSTANCE resource ids (CONTROLS_HIDING_DOOR uses resource ids here; MeshMappings empty)
            public List<ShortGuid> MeshMappings; // Optional RENDERABLE_INSTANCE resource ids; unused on many skeletons including CONTROLS_HIDING_DOOR

            public List<Matrix4x4> InverseBindPoses;
            public List<Matrix4x4> HavokToCathodeMappings;

            public List<WeightedHelperData> HelperMatrices;

            public static bool operator ==(EnvironmentAnimation x, EnvironmentAnimation y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.ID != y.ID) return false;
                return x.ContentEquals(y);
            }

            public static bool operator !=(EnvironmentAnimation x, EnvironmentAnimation y)
            {
                return !(x == y);
            }

            /// <summary>
            /// Compare payload fields used for port/import dedupe. Excludes <see cref="ID"/> (per-level Commands link).
            /// </summary>
            public bool ContentEquals(EnvironmentAnimation other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (Matrix != other.Matrix) return false;
                if (SkeletonName != other.SkeletonName) return false;
                if (AnimationSet != other.AnimationSet) return false;
                if (!ListsEqual(BoneMappings, other.BoneMappings)) return false;
                if (!ListsEqual(MeshMappings, other.MeshMappings)) return false;
                if (!ListsEqual(InverseBindPoses, other.InverseBindPoses)) return false;
                if (!ListsEqual(HavokToCathodeMappings, other.HavokToCathodeMappings)) return false;
                if (!WeightedHelperListsEqual(HelperMatrices, other.HelperMatrices)) return false;
                return true;
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
                hashCode = hashCode * -1521134295 + Matrix.GetHashCode();
                hashCode = hashCode * -1521134295 + (SkeletonName?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + AnimationSet.GetHashCode();
                hashCode = hashCode * -1521134295 + ID.GetHashCode();
                hashCode = hashCode * -1521134295 + ListContentHash(BoneMappings);
                hashCode = hashCode * -1521134295 + ListContentHash(MeshMappings);
                hashCode = hashCode * -1521134295 + ListContentHash(InverseBindPoses);
                hashCode = hashCode * -1521134295 + ListContentHash(HavokToCathodeMappings);
                hashCode = hashCode * -1521134295 + WeightedHelperListHash(HelperMatrices);
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

            private static int ListContentHash<T>(List<T> list)
            {
                if (list == null) return 0;
                int h = list.Count;
                for (int i = 0; i < list.Count; i++)
                    h = h * -1521134295 + (list[i]?.GetHashCode() ?? 0);
                return h;
            }

            private static bool WeightedHelperListsEqual(List<WeightedHelperData> x, List<WeightedHelperData> y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                if (x.Count != y.Count) return false;
                for (int i = 0; i < x.Count; i++)
                {
                    if (x[i] != y[i]) return false;
                }
                return true;
            }

            private static int WeightedHelperListHash(List<WeightedHelperData> list)
            {
                if (list == null) return 0;
                int h = list.Count;
                for (int i = 0; i < list.Count; i++)
                    h = h * -1521134295 + list[i].GetHashCode();
                return h;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct WeightedHelperData : IEquatable<WeightedHelperData>
        {
            public uint HelperName; //anim hashed string (look it up)
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] BindPosition;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] BindNormal;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public float[] BindTangent;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] Indicies;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] Weights;

            public static bool operator ==(WeightedHelperData x, WeightedHelperData y)
            {
                if (x.HelperName != y.HelperName) return false;
                if (!FloatArraysEqual(x.BindPosition, y.BindPosition)) return false;
                if (!FloatArraysEqual(x.BindNormal, y.BindNormal)) return false;
                if (!FloatArraysEqual(x.BindTangent, y.BindTangent)) return false;
                if (!ByteArraysEqual(x.Indicies, y.Indicies)) return false;
                if (!ByteArraysEqual(x.Weights, y.Weights)) return false;
                return true;
            }

            public static bool operator !=(WeightedHelperData x, WeightedHelperData y)
            {
                return !(x == y);
            }

            public bool Equals(WeightedHelperData other)
            {
                return this == other;
            }

            public override bool Equals(object obj)
            {
                return obj is WeightedHelperData other && this == other;
            }

            public override int GetHashCode()
            {
                int hashCode = (int)HelperName;
                hashCode = hashCode * -1521134295 + ArrayHash(BindPosition);
                hashCode = hashCode * -1521134295 + ArrayHash(BindNormal);
                hashCode = hashCode * -1521134295 + ArrayHash(BindTangent);
                hashCode = hashCode * -1521134295 + ArrayHash(Indicies);
                hashCode = hashCode * -1521134295 + ArrayHash(Weights);
                return hashCode;
            }

            private static bool FloatArraysEqual(float[] a, float[] b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) return false;
                return true;
            }

            private static bool ByteArraysEqual(byte[] a, byte[] b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) return false;
                return true;
            }

            private static int ArrayHash(float[] a)
            {
                if (a == null) return 0;
                int h = a.Length;
                for (int i = 0; i < a.Length; i++)
                    h = h * -1521134295 + a[i].GetHashCode();
                return h;
            }

            private static int ArrayHash(byte[] a)
            {
                if (a == null) return 0;
                int h = a.Length;
                for (int i = 0; i < a.Length; i++)
                    h = h * -1521134295 + a[i];
                return h;
            }
        };
        #endregion
    }
}