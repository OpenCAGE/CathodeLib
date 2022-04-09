using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

/*
namespace CATHODE.LEGACY
{
    public class ShadersPAK : CathodePAK
    {
        public List<ShaderEntry> Shaders;
        public void Load(string FullFilePath)
        {
            LoadPAK(FullFilePath, false);

            Shaders = new List<ShaderEntry>(PAKHeader.EntryCount);
            for (int EntryIndex = 0; EntryIndex < PAKHeader.EntryCount; EntryIndex++)
            {
                GenericPAKEntry Entry = PAKEntries[EntryIndex];

                byte[] Buffer = EntryDatas[EntryIndex];
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
            AlienShaderCategory_None = 0,
            AlienShaderCategory_Particle = 12,
            AlienShaderCategory_Ribbon = 13,
            AlienShaderCategory_Environment = 17,
            AlienShaderCategory_ShadowCaster = 18,
            AlienShaderCategory_DecalEnvironment = 19,
            AlienShaderCategory_Character = 20,
            AlienShaderCategory_Skin = 21,
            AlienShaderCategory_Hair = 22,
            AlienShaderCategory_Eye = 23,
            AlienShaderCategory_SkinOcclusion = 24,
            AlienShaderCategory_Deferred = 27,
            AlienShaderCategory_Decal = 30,
            AlienShaderCategory_FogPlane = 31,
            AlienShaderCategory_FogSphere = 32,
            AlienShaderCategory_Debug = 33,
            AlienShaderCategory_PostProcessing = 35,
            AlienShaderCategory_Filters = 37,
            AlienShaderCategory_LensFlare = 38,
            AlienShaderCategory_LiquidEnvironment = 39,
            AlienShaderCategory_OcclusionCulling = 42,
            AlienShaderCategory_Refraction = 43,
            AlienShaderCategory_SimpleRefraction = 44,
            AlienShaderCategory_DistortionOverlay = 45,
            AlienShaderCategory_SurfaceEffects = 50,
            AlienShaderCategory_EffectOverlay = 51,
            AlienShaderCategory_Terrain = 52,
            AlienShaderCategory_NonInteractiveWater = 53,
            AlienShaderCategory_SimpleWater = 54,
            AlienShaderCategory_Planet = 55,
            AlienShaderCategory_LightMapEnvironment = 58,
            AlienShaderCategory_LowLODCharacter = 60,
            AlienShaderCategory_LightDecal = 61,
            AlienShaderCategory_VolumeLight = 62,
            AlienShaderCategory_WaterCausticsOverlay = 63,
            AlienShaderCategory_SpaceSuitVisor = 64,
            AlienShaderCategory_CameraMap = 65,
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct alien_shader_pak_shader_header
        {
            public int ResourceID; // TODO: Is it? It seems to be an ID/Hash.
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
*/