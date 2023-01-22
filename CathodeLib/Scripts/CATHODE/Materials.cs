using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CathodeLib;

namespace CATHODE
{
    /* Handles Cathode MODELS.MTL files */
    public class Materials : CathodeFile
    {
        //TODO: tidy how we access these
        public Header _header;
        public Entry[] _materials;
        public List<int> _textureReferenceCounts;
        public List<string> _materialNames;

        public Materials(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                _header = Utilities.Consume<Header>(reader);

                _materialNames = new List<string>(_header.MaterialCount);
                for (int MaterialIndex = 0; MaterialIndex < _header.MaterialCount; ++MaterialIndex)
                {
                    _materialNames.Add(Utilities.ReadString(reader));
                }

                reader.BaseStream.Position = _header.FirstMaterialOffset + Marshal.SizeOf(_header.BytesRemainingAfterThis);

                _materials = Utilities.ConsumeArray<Entry>(reader, _header.MaterialCount);
                _textureReferenceCounts = new List<int>(_header.MaterialCount);
                for (int MaterialIndex = 0; MaterialIndex < _header.MaterialCount; ++MaterialIndex)
                {
                    Entry Material = _materials[MaterialIndex];

                    int count = 0;
                    for (int I = 0; I < Material.TextureReferences.Length; ++I)
                    {
                        TextureReference Pair = Material.TextureReferences[I];
                        if (Pair.TextureTableIndex == 2 || Pair.TextureTableIndex == 0) count++;
                    }
                    _textureReferenceCounts.Add(count);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Header
        {
            public int BytesRemainingAfterThis; // TODO: Weird, is there any case where this is not true? Assert!
            public int Unknown0_;
            public int FirstMaterialOffset;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public int[] CSTOffsets; //5
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public int[] UnknownOffsets; //2
            public Int16 MaterialCount;
            public Int16 Unknown2_;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TextureReference
        {
            public Int16 TextureIndex; // NOTE: Entry index in texture BIN file. Max value seen matches the BIN file entry count;
            public Int16 TextureTableIndex; // NOTE: Only seen as -1, 0 or 2 so far.
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct Entry
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public TextureReference[] TextureReferences; //12
            public int UnknownZero0_; // NOTE: Only seen as 0 so far.
            public int MaterialIndex;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public int[] CSTOffsets; //5
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public byte[] CSTCounts; //5
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] ZeroPad; //3
            public int UnknownZero1_;
            public int UnknownValue0;
            public int UberShaderIndex;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public int[] UnknownZeros0_; //32
            public int UnknownValue1;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            public int[] UnknownZeros1_; //12
            public int Unknown4_;
            public int Color; // TODO: This is not really color AFAIK.
            public int UnknownValue2;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public int[] UnknownZeros2_; //2
        };
        #endregion
    }
}