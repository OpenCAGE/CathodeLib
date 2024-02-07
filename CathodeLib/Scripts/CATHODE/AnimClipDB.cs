using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using CATHODE.Scripting;
using CathodeLib;

namespace CATHODE
{
    /* DATA/GLOBAL/ANIMATION.PAK -> ANIM_CLIP_DB.BIN */
    public class AnimClipDB : CathodeFile
    {
        public Dictionary<uint, string> Entries = new Dictionary<uint, string>();
        public static new Implementation Implementation = Implementation.LOAD;
        public AnimClipDB(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                int EntryCount1 = reader.ReadInt32();
                int EntryCount2 = reader.ReadInt32();
                IndexPair[] Entries1 = Utilities.ConsumeArray<IndexPair>(reader, EntryCount1);
                IndexPair[] Entries2 = Utilities.ConsumeArray<IndexPair>(reader, EntryCount2);

                int Count0 = reader.ReadInt32();
                int Count1 = reader.ReadInt32();
                IndexPair[] Stuff0 = Utilities.ConsumeArray<IndexPair>(reader, Count0);
                OffsetPair[] Stuff1 = Utilities.ConsumeArray<OffsetPair>(reader, Count1);

                int Count2 = reader.ReadInt32();
                int[] Stuff2 = Utilities.ConsumeArray<int>(reader, Count2);

                int Count4 = reader.ReadInt32();
                int Count5 = reader.ReadInt32();
                int Count6 = reader.ReadInt32();
                IndexPair[] Stuff5 = Utilities.ConsumeArray<IndexPair>(reader, Count5);
                int[] Stuff6 = Utilities.ConsumeArray<int>(reader, Count6);

                int Count7 = reader.ReadInt32();
                int[] Stuff7 = Utilities.ConsumeArray<int>(reader, Count7);

                byte[] HeaderCounts0 = Utilities.ConsumeArray<byte>(reader, 5);
                float[] HeaderFloats0 = Utilities.ConsumeArray<float>(reader, 6); // TODO: Is this HKX min/max floats for compression?
                int[] HeaderStuff0 = Utilities.ConsumeArray<int>(reader, 4);

                int[] ContentStuff0 = Utilities.ConsumeArray<int>(reader, HeaderCounts0[1] * 4);
                Vector2[] ContentStuff1 = Utilities.ConsumeArray<Vector2>(reader, HeaderCounts0[2]);

                bone_entry[] BoneEntries = Utilities.ConsumeArray<bone_entry>(reader, HeaderCounts0[3]);

                // NOTE: Following content seems to be 4 unknown u8s followed by 4 u8s of which the 0th is ff and 1, 2 and 3 seem to
                //  sum to 255. I would guess those are bone weights? Bone weights tend to sum to 1.
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct IndexPair
        {
            public uint id;
            public int index;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct bone_entry
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            byte[] Joints;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            byte[] Weights;
        };
        #endregion
    }
}
