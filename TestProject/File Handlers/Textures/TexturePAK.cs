using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestProject.File_Handlers.Textures
{
    public class TexturePAK
    {
        public static alien_textures Load(string PAKFileName, string BINFileName)
        {
            alien_textures Result = new alien_textures();

            Result.PAK = File_Handlers.PAK.PAK.Load(PAKFileName, true);
            Result.BIN = File_Handlers.Textures.TextureBIN.Load(BINFileName);

            return Result;
        }
    }
}

public enum alien_texture_format
{
    Alien_R32G32B32A32_SFLOAT = 0,

    // TODO: What are those really? What is the difference between them? sRGB?
    Alien_FORMAT_R8G8B8A8_UNORM = 2,
    Alien_FORMAT_R8G8B8A8_UNORM_0 = 3,

    // TODO: What are those really? What is the difference between them? sRGB?
    Alien_FORMAT_SIGNED_DISTANCE_FIELD = 4,
    Alien_FORMAT_R8 = 5,

    Alien_FORMAT_BC1 = 6,
    Alien_FORMAT_BC2 = 7,
    Alien_FORMAT_BC5 = 8,
    Alien_FORMAT_BC3 = 9,
    Alien_FORMAT_BC7 = 13,
    Alien_FORMAT_R8G8 = 14,
};

public struct alien_texture_bin_header
{
    public int Version;
    public int EntryCount;
};

public struct alien_texture_bin_texture
{
    public fourcc FourCC;
    public alien_texture_format Format;
    public int Length_V2;
    public int Length_V1;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public Int16[] Size_V1; //this is an int16 vector3
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public Int16[] Size_V2; //this is an int16 vector3
    public Int16 MipLevelsV1;
    public Int16 MipLevelsV2;
    public int Type; // NOTE: 64 seems to be 'animated', 1 seems to be 'sRGB' (if not set, then 'linear'), 4 is unknown.
    public int Unknown1_;
    public int FileNameOffset; // NOTE: Offset after 'alien_bin_header'.
    public int Unknown2_;
};

public struct alien_texture_bin
{
    public alien_texture_bin_header Header;
    public List<alien_texture_bin_texture> Textures;
    public List<string> TextureFilePaths;
};

public struct alien_textures
{
    public alien_pak PAK;
    public alien_texture_bin BIN;
};

public struct asset_id
{

}


/* Below here is all shit from dan's renderer */
public enum texture_format
{
    UNKNOWN,

    BC1_RGB_SRGB_BLOCK,
    BC1_RGB_UNORM_BLOCK,
    BC2_SRGB_BLOCK,
    BC2_UNORM_BLOCK,
    BC3_SRGB_BLOCK,
    BC3_UNORM_BLOCK,
    BC5_UNORM_BLOCK,
    BC7_SRGB_BLOCK,
    BC7_UNORM_BLOCK,
    R8G8B8A8_SRGB,
    R8G8B8A8_UNORM,
    R8_SRGB,
    R8_UNORM,
    R8G8_SRGB,
    R8G8_UNORM,
    R32G32B32A32_SFLOAT,

}

public struct texture_info
{
    public bool IsCubeMap;
    public V3 Dim;
    public texture_format Format;
    public int MipLevels;
}

public struct texture_image
{
    public byte[] Data;
    public texture_info Info;
}