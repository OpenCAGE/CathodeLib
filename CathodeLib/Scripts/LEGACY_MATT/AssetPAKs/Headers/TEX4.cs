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
}
