using CATHODE.Generic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Textures
{
    /* Handles parsing Cathode texture PAK/BIN files */
    public class CathodeTextures : CathodePAK
    {
        public TextureHeader Header;
        public TextureEntry[] Textures;
        public List<string> TextureFilePaths;

        public CathodeTextures(string PAKFileName, string BINFileName)
        {
            LoadPAK(PAKFileName, true);

            BinaryReader Stream = new BinaryReader(File.OpenRead(BINFileName));
            Header = Utilities.Consume<TextureHeader>(Stream);
            int StringsStartCount = Stream.ReadInt32();
            byte[] StringsStart = Stream.ReadBytes(StringsStartCount);
            Textures = Utilities.ConsumeArray<TextureEntry>(Stream, Header.EntryCount);
            TextureFilePaths = new List<string>(Header.EntryCount);
            for (int EntryIndex = 0; EntryIndex < Header.EntryCount; ++EntryIndex)
            {
                TextureFilePaths.Add(Utilities.ReadString(StringsStart, Textures[EntryIndex].FileNameOffset).Replace('\\', '/'));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TextureHeader
    {
        public int Version;
        public int EntryCount;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TextureEntry
    {
        public fourcc FourCC;
        public TextureFormat Format;
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

    public enum TextureFormat
    {
        R32G32B32A32_SFLOAT = 0,

        // TODO: What are those really? What is the difference between them? sRGB?
        R8G8B8A8_UNORM = 2,
        R8G8B8A8_UNORM_0 = 3,

        // TODO: What are those really? What is the difference between them? sRGB?
        SIGNED_DISTANCE_FIELD = 4,
        R8 = 5, //cra0kalo source says B8G8R8 (check)

        DDS_BC1 = 6,
        DDS_BC2 = 7,
        DDS_BC5 = 8,
        DDS_BC3 = 9,
        DDS_BC7 = 13,

        R8G8 = 14,
    };
}