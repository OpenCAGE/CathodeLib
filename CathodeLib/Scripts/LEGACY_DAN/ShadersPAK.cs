using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.LEGACY
{
    public class ShadersPAK : CathodePAK
    {
        public List<ShaderEntry> Shaders;
        public ShadersPAK(string FullFilePath)
        {
            LoadPAK(FullFilePath, false);

            Shaders = new List<ShaderEntry>(entryContents.Count);
            for (int EntryIndex = 0; EntryIndex < entryContents.Count; EntryIndex++)
            {
                GenericPAKEntry Entry = entryHeaders[EntryIndex];

                byte[] Buffer = entryContents[EntryIndex];
                BinaryReader Stream = new BinaryReader(new MemoryStream(Buffer));

                ShaderEntry Shader = new ShaderEntry();
                Shader.Index = Entry.UnknownIndex;
                Shader.Header = Utilities.Consume<alien_shader_pak_shader_header>(Stream);
                byte[] Name = Stream.ReadBytes(40);
                Shader.Name = Utilities.ReadString(Name);
                Shader.Header2 = Utilities.Consume<alien_shader_pak_shader_header2>(Stream);
                Shader.Entry0Count = Stream.ReadUInt16();
                Shader.Entries0 = Utilities.ConsumeArray<alien_shader_pak_shader_unknown_entry>(Stream, Shader.Entry0Count);

                Shader.TextureEntries = Utilities.ConsumeArray<alien_shader_pak_shader_texture_entry>(Stream, Shader.Header.TextureCount);
                Shader.TextureThings = Stream.ReadBytes(Shader.Header.TextureCount);

                byte[][] CSTLinks = new byte[5][];
                for (int TableIndex = 0; TableIndex < Shader.Header.CSTCounts.Length; ++TableIndex)
                {
                    CSTLinks[TableIndex] = Stream.ReadBytes(Shader.Header.CSTCounts[TableIndex]);
                }
                Shader.CSTLinks = CSTLinks;

                Shader.TextureLinks = Stream.ReadBytes(Shader.Header.TextureLinkCount);
                Shader.Indices = Utilities.Consume<alien_shader_pak_shader_indices>(Stream);

                Shaders.Add(Shader);

                Utilities.Align(Stream, 16);
            }
        }

        public enum ShaderCategory
        {
            NONE = 0,

            CA_PARTICLE = 12,
            CA_RIBBON = 13,
            CA_ENVIRONMENT = 17,
            CA_SHADOWCASTER = 18,
            CA_DECAL_ENVIRONMENT = 19,
            CA_CHARACTER = 20,
            CA_SKIN = 21,
            CA_HAIR = 22,
            CA_EYE = 23,
            CA_SKIN_OCCLUSION = 24,
            CA_DEFERRED = 27,
            CA_DECAL = 30,
            CA_FOGPLANE = 31,
            CA_FOGSPHERE = 32,
            CA_DEBUG = 33,
            CA_POST_PROCESSING = 35,
            CA_FILTERS = 37,
            CA_LENS_FLARE = 38,
            CA_LIQUID_ENVIRONMENT = 39,
            CA_OCCLUSION_CULLING = 42,
            CA_REFRACTION = 43,
            CA_SIMPLE_REFRACTION = 44,
            CA_DISTORTION_OVERLAY = 45,
            CA_SURFACE_EFFECTS = 50,
            CA_EFFECT_OVERLAY = 51,
            CA_TERRAIN = 52,
            CA_NONINTERACTIVE_WATER = 53,
            CA_SIMPLEWATER = 54,
            CA_PLANET = 55,
            CA_LIGHTMAP_ENVIRONMENT = 58,
            CA_LOW_LOD_CHARACTER = 60,
            CA_LIGHT_DECAL = 61,
            CA_VOLUME_LIGHT = 62,
            CA_WATER_CAUSTICS_OVERLAY = 63,
            CA_SPACESUIT_VISOR = 64,
            CA_CAMERA_MAP = 65,
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct alien_shader_pak_shader_header
        {
            public int ResourceID; // NOTE: Always 0x7725BBA4?
            public Int16 Unknown0_; // NOTE: Always 36.
            public Int16 Unknown1_; // NOTE: Always 1.
            public Int16 TextureCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public int[] CSTCounts;//5
            public Int16 TextureLinkCount;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct alien_shader_pak_shader_header2
        {
            public Int16 ShaderCategory;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
            public byte[] Unknown; //24
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct alien_shader_pak_shader_unknown_entry
        {
            public Int16 Unknown0_;
            public int Unknown1_;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct alien_shader_pak_shader_texture_property
        {
            public Int16 Type;
            public float F32;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct alien_shader_pak_shader_texture_entry
        {
            public byte PropertyCount;
            public byte TextureSlot;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public alien_shader_pak_shader_texture_property[] Properties; //7
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct alien_shader_pak_shader_indices
        {
            public int VertexShader;
            public int PixelShader;

            // TODO: These are uncertain.
            public int HullShader;
            public int DomainShader;
            public int ComputeShader;
            public int GeometryShader;
        };

        public struct ShaderEntry
        {
            public int Index;
            public alien_shader_pak_shader_header Header;
            public string Name;
            public alien_shader_pak_shader_header2 Header2;

            public int Entry0Count;
            public alien_shader_pak_shader_unknown_entry[] Entries0;
            public alien_shader_pak_shader_texture_entry[] TextureEntries;

            public byte[] TextureThings;
            public byte[][] CSTLinks;//always 5 parents
            public byte[] TextureLinks;

            public alien_shader_pak_shader_indices Indices;
        };
    }
}