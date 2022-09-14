using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Misc
{
    /* Handles Cathode MODELS.MTL files */
    public class MaterialDatabase
    {
        public MaterialHeader Header;
        public MaterialEntry[] Materials;
        public List<int> TextureReferenceCounts;
        public List<string> MaterialNames;

        //TODO: THIS NEEDS UPDATING TO WORK NICELY!

        public MaterialDatabase(string FullFilePath)
        {
            BinaryReader Stream = new BinaryReader(File.OpenRead(FullFilePath));

            Header = Utilities.Consume<MaterialHeader>(Stream);

            MaterialNames = new List<string>(Header.MaterialCount);
            for (int MaterialIndex = 0; MaterialIndex < Header.MaterialCount; ++MaterialIndex)
            {
                MaterialNames.Add(Utilities.ReadString(Stream));
            }

            Stream.BaseStream.Position = Header.FirstMaterialOffset + Marshal.SizeOf(Header.BytesRemainingAfterThis);

            Materials = Utilities.ConsumeArray<MaterialEntry>(Stream, Header.MaterialCount);
            TextureReferenceCounts = new List<int>(Header.MaterialCount);
            for (int MaterialIndex = 0; MaterialIndex < Header.MaterialCount; ++MaterialIndex)
            {
                MaterialEntry Material = Materials[MaterialIndex];

                int count = 0;
                for (int I = 0; I < Material.TextureReferences.Length; ++I)
                {
                    MaterialTextureReference Pair = Material.TextureReferences[I];
                    if (Pair.TextureTableIndex == 2 || Pair.TextureTableIndex == 0) count++;
                }
                TextureReferenceCounts.Add(count);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MaterialHeader
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
    public struct MaterialTextureReference
    {
        public Int16 TextureIndex; // NOTE: Entry index in texture BIN file. Max value seen matches the BIN file entry count;
        public Int16 TextureTableIndex; // NOTE: Only seen as -1, 0 or 2 so far.
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MaterialEntry
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public MaterialTextureReference[] TextureReferences; //12
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
}