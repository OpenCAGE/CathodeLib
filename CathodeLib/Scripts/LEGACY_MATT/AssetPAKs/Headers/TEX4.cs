using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.LEGACY.Assets
{
    public enum TextureFormat : int
    {
        DXGI_FORMAT_B8G8R8A8_UNORM = 0x2,
        SIGNED_DISTANCE_FIELD = 0x4,
        DXGI_FORMAT_B8G8R8_UNORM = 0x5,
        DXGI_FORMAT_BC1_UNORM = 0x6,
        DXGI_FORMAT_BC3_UNORM = 0x9,
        DXGI_FORMAT_BC5_UNORM = 0x8,
        DXGI_FORMAT_BC7_UNORM = 0xD
    }

    //The Tex4 Entry
    class TEX4
    {
        public string FileName = "";

        public TextureFormat Format;
        public int Type = -1; //AlienTextureType
        public AlienUnknownTextureThing UnknownTexThing;

        public TEX4_Part tex_LowRes = new TEX4_Part();
        public TEX4_Part tex_HighRes = new TEX4_Part(); //We don't always have this
    }

    public enum AlienTextureType
    {
        SPECULAR_OR_NORMAL = 0,
        DIFFUSE = 1,
        LUT = 21,

        DECAL = 5,
        ENVIRONMENT_MAP = 7,

    }

    public enum AlienUnknownTextureThing
    {
        REGULAR_TEXTURE = 0,
        SOME_SPECIAL_TEXTURE = 9,
    }

    //The Tex4 Sub-Parts
    class TEX4_Part
    {
        public Int16 Width = 0;
        public Int16 Height = 0;

        public Int16 Depth = 0;
        public Int16 MipLevels = 0;

        public int Offset = 0;
        public int Length = 0;

        //Saving these so we can re-write without issue
        public UInt32 unk1 = 0;
        public UInt16 unk2 = 0;
        public UInt32 unk3 = 0;
        public UInt32 unk4 = 0;
    }
}
