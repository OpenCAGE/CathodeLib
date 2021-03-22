using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestProject.File_Handlers.Textures
{
    class TexturePAK
    {
        public static alien_textures Load(string PAKFileName, string BINFileName)
        {
            alien_textures Result = new alien_textures();

            Result.PAK = File_Handlers.PAK.PAK.Load(PAKFileName, true);
            Result.BIN = File_Handlers.Textures.TextureBIN.Load(BINFileName);

            List<int> VersionTracker = new List<int>(new int[Result.BIN.Header.EntryCount]);

            Result.TextureCount = Result.BIN.Header.EntryCount;
            Result.TextureEntryIndices = new List<int>(Result.TextureCount);
            Result.TextureFilePaths = new List<string>(Result.TextureCount);

            for (int EntryIndex = 0; EntryIndex < Result.PAK.Header.EntryCount; ++EntryIndex)
            {
                alien_pak_entry Entry = Result.PAK.Entries[EntryIndex];

                alien_texture_bin_texture InTexture = Result.BIN.Textures[Entry.BINIndex];
                string TextureFilePath = Result.BIN.TextureFilePaths[Entry.BINIndex];

                alien_texture_format AlienFormat = (alien_texture_format)InTexture.Format;

                // TODO: Sort out this SRGB situation, this is bad code, plus, I'm not sure it is correct.
                texture_format Format = texture_format.UNKNOWN;
                switch (AlienFormat)
                {
                    case alien_texture_format.Alien_FORMAT_BC1:
                        {
                            if (InTexture.Type == 1)
                            {
                                Format = texture_format.BC1_RGB_SRGB_BLOCK;
                            }
                            else
                            {
                                Format = texture_format.BC1_RGB_UNORM_BLOCK;
                            }
                        }
                        break;

                    case alien_texture_format.Alien_FORMAT_BC2:
                        {
                            if (InTexture.Type == 1)
                            {
                                Format = texture_format.BC2_SRGB_BLOCK;
                            }
                            else
                            {
                                Format = texture_format.BC2_UNORM_BLOCK;
                            }
                        }
                        break;

                    case alien_texture_format.Alien_FORMAT_BC3:
                        {
                            if (InTexture.Type == 1)
                            {
                                Format = texture_format.BC3_SRGB_BLOCK;
                            }
                            else
                            {
                                Format = texture_format.BC3_UNORM_BLOCK;
                            }
                        }
                        break;

                    case alien_texture_format.Alien_FORMAT_BC5:
                        {
                            // TODO: Is this SNORM?
                            Format = texture_format.BC5_UNORM_BLOCK;
                        }
                        break;

                    case alien_texture_format.Alien_FORMAT_BC7:
                        {
                            if (InTexture.Type == 1)
                            {
                                Format = texture_format.BC7_SRGB_BLOCK;
                            }
                            else
                            {
                                Format = texture_format.BC7_UNORM_BLOCK;
                            }
                        }
                        break;

                    // TODO: What are those really? What is the difference between them?
                    case alien_texture_format.Alien_FORMAT_R8G8B8A8_UNORM:
                    case alien_texture_format.Alien_FORMAT_R8G8B8A8_UNORM_0:
                        {
                            if (InTexture.Type == 1)
                            {
                                Format = texture_format.R8G8B8A8_SRGB;
                            }
                            else
                            {
                                Format = texture_format.R8G8B8A8_UNORM;
                            }
                        }
                        break;

                    // TODO: What are those really? What is the difference between them?
                    case alien_texture_format.Alien_FORMAT_SIGNED_DISTANCE_FIELD:
                    case alien_texture_format.Alien_FORMAT_R8:
                        {
                            if (InTexture.Type == 1)
                            {
                                Format = texture_format.R8_SRGB;
                            }
                            else
                            {
                                Format = texture_format.R8_UNORM;
                            }
                        }
                        break;

                    case alien_texture_format.Alien_FORMAT_R8G8:
                        {
                            if (InTexture.Type == 1)
                            {
                                Format = texture_format.R8G8_SRGB;
                            }
                            else
                            {
                                Format = texture_format.R8G8_UNORM;
                            }
                        }
                        break;

                    case alien_texture_format.Alien_R32G32B32A32_SFLOAT:
                        {
                            // TODO: Is it RGBA or BGRA or ARGB? Seems like RGBA.
                            Format = texture_format.R32G32B32A32_SFLOAT;
                        }
                        break;
                }


                int MipLevels;
                int TextureLength;
                Vector3 TextureDim = new Vector3();
                if (VersionTracker[Entry.BINIndex] == 0)
                {
                    MipLevels = InTexture.MipLevelsV1;
                    TextureLength = InTexture.Length_V1;
                    TextureDim.x = InTexture.Size_V1[0];
                    TextureDim.y = InTexture.Size_V1[1];
                    TextureDim.z = InTexture.Size_V1[2];
                }
                else
                {
                    MipLevels = InTexture.MipLevelsV2;
                    TextureLength = InTexture.Length_V2;
                    TextureDim.x = InTexture.Size_V2[0];
                    TextureDim.y = InTexture.Size_V2[1];
                    TextureDim.z = InTexture.Size_V2[2];
                }
                VersionTracker[Entry.BINIndex]++;

                if (TextureLength == 0)
                {
                    // NOTE: Assume invalid texture, don't add it.
                    // TODO: Does this have adverse effects?
                    continue;
                }

                texture_image Image = new texture_image();
                Image.Info = new texture_info();

                if (InTexture.Type == 7)
                {
                    Image.Info.IsCubeMap = true;
                }

                Image.Info.Dim = TextureDim;
                Image.Info.Format = Format;

                //mattf hotfix - todo: improve this, is there an index to use on Result.PAK.EntryDatas?
                BinaryReader tempReader = new BinaryReader(new MemoryStream(Result.PAK.DataStart));
                tempReader.BaseStream.Position = Entry.Offset;
                Image.Data = tempReader.ReadBytes(TextureLength);
                tempReader.Close();

                int TotalSizeWithMips = 0;
                Image.Info.MipLevels = MipLevels;
                int ArrayLayerCount = Image.Info.IsCubeMap ? 6 : 1;
                for (int J = 0; J < ArrayLayerCount; ++J)
                {
                    for (int I = 0; I < Image.Info.MipLevels; ++I)
                    {
                        //TotalSizeWithMips += GetSizeInBytes(&Info, I);
                    }
                }
                // TODO: Handle all texture formats. See 'default' switch case. Then we can remove the 'None' part.

                //byte[] ImageFileBuffer = BufferFromTextureImage(&Image, Arena);

                // TODO: Save if checksum differs?
                if (!File.Exists(TextureFilePath))
                {
                    Directory.CreateDirectory(TextureFilePath);
                    //File.WriteAllBytes(TextureFilePath, ImageFileBuffer);
                }

                Result.TextureFilePaths.Add(TextureFilePath);
                Result.TextureEntryIndices.Add(EntryIndex);
            }

            return Result;
        }
    }
}

enum alien_texture_format
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

struct alien_texture_bin_header
{
    public int Version;
    public int EntryCount;
};

struct alien_texture_bin_texture
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

struct alien_texture_bin
{
    public alien_texture_bin_header Header;
    public List<alien_texture_bin_texture> Textures;
    public List<string> TextureFilePaths;
};

struct alien_textures
{
    public int TextureCount;
    public List<int> TextureEntryIndices;
    public List<string> TextureFilePaths;
    public alien_pak PAK;
    public alien_texture_bin BIN;
};

struct asset_id
{

}


/* Below here is all shit from dan's renderer */
enum texture_format
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

struct texture_info
{
    public bool IsCubeMap;
    public Vector3 Dim;
    public texture_format Format;
    public int MipLevels;
}

struct texture_image
{
    public byte[] Data;
    public texture_info Info;
}