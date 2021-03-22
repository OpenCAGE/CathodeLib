using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestProject.File_Handlers.Models
{
    class ModelsMTL
    {
        public static alien_mtl Load(string FullFilePath, byte[] CST = null)
        {
            alien_mtl Result = new alien_mtl();
            BinaryReader Stream = new BinaryReader(File.OpenRead(FullFilePath));

            alien_mtl_header Header = Utilities.Consume<alien_mtl_header>(Stream);

            Result.MaterialNames = new List<string>(Header.MaterialCount);
            for (int MaterialIndex = 0; MaterialIndex < Header.MaterialCount; ++MaterialIndex)
            {
                Result.MaterialNames.Add(Utilities.ReadString(Stream));
            }

            Stream.BaseStream.Position += Header.FirstMaterialOffset - Marshal.SizeOf(Header.BytesRemainingAfterThis);

            Result.Header = Header;
            Result.Materials = Utilities.ConsumeArray<alien_mtl_material>(Stream, Header.MaterialCount);

            //load csv and parse this
            //Result.Datas1 = (f32*)(CST.Data + Header.Unknown1_[0]);
            //Result.Datas2 = (f32*)(CST.Data + Header.Unknown1_[1]);
            //Result.Datas3 = (f32*)(CST.Data + Header.Unknown1_[2]);
            //Result.Datas4 = (f32*)(CST.Data + Header.Unknown1_[3]);
            //Result.Datas5 = (f32*)(CST.Data + Header.Unknown1_[4]);

            Result.TextureReferenceCounts = new List<int>(Result.Header.MaterialCount);
            for (int MaterialIndex = 0; MaterialIndex < Header.MaterialCount; ++MaterialIndex)
            {
                alien_mtl_material Material = Result.Materials[MaterialIndex];

                int count = 0;
                for (int I = 0; I < Material.TextureReferences.Length; ++I)
                {
                    alien_mtl_texture_reference Pair = Material.TextureReferences[I];
                    if (Pair.TextureTableIndex != -1) count++;
                }
                Result.TextureReferenceCounts.Add(count);
            }

            return Result;
        }
    }
}

//Length: 44
struct alien_mtl_header
{
    public int BytesRemainingAfterThis; // TODO: Weird, is there any case where this is not true? Assert!
    public int Unknown0_;
    public int FirstMaterialOffset;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
    public int[] _Unknown; //7
    public Int16 MaterialCount;
    public Int16 Unknown2_;
};

struct alien_mtl_texture_reference
{
    public Int16 TextureIndex; // NOTE: Entry index in texture BIN file. Max value seen matches the BIN file entry count;
    public Int16 TextureTableIndex; // NOTE: Only seen as -1, 0 or 2 so far.
};

//Length: 296
struct alien_mtl_material
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
    public alien_mtl_texture_reference[] TextureReferences; //12
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
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public int[] UnknownZeros2_; //3
};

struct alien_mtl
{
    public alien_mtl_header Header;
    public List<alien_mtl_material> Materials;
    public List<int> TextureReferenceCounts;
    public List<string> MaterialNames;

    public float Datas1;
    public float Datas2;
    public float Datas3;
    public float Datas4;
    public float Datas5;
};