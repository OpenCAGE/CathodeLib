using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
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

        private MaterialPropertyIndex GetMaterialPropertyIndexes(Materials.Material InMaterial)
        {
            ShaderEntry Shader = Shaders[InMaterial.UberShaderIndex];

            MaterialPropertyIndex toReturn = new MaterialPropertyIndex();

            switch ((ShaderCategory)Shader.Header2.ShaderCategory)
            {
                case ShaderCategory.CA_ENVIRONMENT:
                    toReturn.Unknown3_Index = 3;
                    toReturn.OpacityUVMultiplierIndex = 5;
                    toReturn.DiffuseUVMultiplierIndex = 6;
                    toReturn.DiffuseIndex = 7;
                    toReturn.SecondaryDiffuseUVMultiplierIndex = 8;
                    toReturn.SecondaryDiffuseIndex = 9;
                    toReturn.NormalUVMultiplierIndex = 10;
                    toReturn.NormalMapStrength0Index = 11;
                    toReturn.SecondaryNormalUVMultiplierIndex = 12;
                    toReturn.NormalMapStrength1Index = 13;
                    toReturn.SpecularFactorIndex = 14;
                    toReturn.SpecularUVMultiplierIndex = 15;
                    toReturn.MetallicFactorIndex = 16;
                    toReturn.SecondarySpecularFactorIndex = 17;
                    toReturn.SecondarySpecularUVMultiplierIndex = 18;
                    toReturn.SecondaryMetallicFactorIndex = 19;
                    toReturn.EnvironmentMapStrength2Index = 24;
                    toReturn.EnvironmentMapStrengthIndex = 25;
                    toReturn.DirtDiffuseIndex = -1; // TODO: ...
                    toReturn.OcclusionUVMultiplierIndex = 27;
                    toReturn.OcclusionTintIndex = 28;
                    toReturn.EmissiveFactorIndex = 29;
                    toReturn.EmissiveIndex = 30;
                    toReturn.ParallaxUVMultiplierIndex = 35;
                    toReturn.ParallaxFactorIndex = 36;
                    toReturn.ParallaxOffsetIndex = 37;
                    toReturn.IsTransparentIndex = 38;
                    toReturn.OpacityNoiseUVMultiplierIndex1 = 39;
                    toReturn.OpacityNoiseAmplitudeIndex = 40;
                    toReturn.DirtMapUVMultiplier0Index = 47;
                    toReturn.DirtMapUVMultiplier1Index = 48;
                    toReturn.DirtStrengthIndex = 49;
                    break;

                case ShaderCategory.CA_CHARACTER:
                    toReturn.OpacityNoiseUVMultiplierIndex1 = 12;
                    toReturn.DiffuseUVMultiplierIndex = 15;
                    toReturn.DiffuseIndex = 16;
                    toReturn.SecondaryDiffuseUVMultiplierIndex = 17;
                    toReturn.SecondaryDiffuseIndex = 18;
                    toReturn.NormalUVMultiplierIndex = 19;
                    toReturn.SecondaryNormalUVMultiplierIndex = 21;
                    toReturn.SpecularUVMultiplierIndex = 24;
                    toReturn.SpecularFactorIndex = 25;
                    break;

                case ShaderCategory.CA_SKIN:
                    toReturn.DiffuseUVMultiplierIndex = 4;
                    toReturn.DiffuseIndex = 5;
                    toReturn.NormalUVMultiplierIndex = 8;
                    toReturn.NormalUVMultiplierOfMultiplierIndex = 10;
                    toReturn.SecondaryNormalUVMultiplierIndex = 11;
                    break;

                case ShaderCategory.CA_HAIR:
                    toReturn.DiffuseIndex = 2;
                    break;

                case ShaderCategory.CA_EYE:
                    toReturn.DiffuseUVAdderIndex = 3;
                    // TODO: These three determine the iris color. They map to rgb channels of the iris map.
                    //  I am using the middle color for now for everything but we should not do that.
                    //toReturn.ColorIndex = 7;
                    toReturn.DiffuseIndex = 8;
                    //toReturn.ColorIndex = 9;
                    toReturn.DiffuseUVMultiplierIndex = 10;

                    // TODO: This info is available in 'Shader->TextureEntries[CorrectIndex].TextureAddressMode'.
                    toReturn.DiffuseSamplerIndex = 0;
                    break;

                case ShaderCategory.CA_DECAL:
                    //toReturn.ColorIndex = 3;
                    //Material->BaseColor = {};
                    break;

                case ShaderCategory.CA_FOGPLANE:
                    //toReturn.DiffuseIndex = 8;
                    //Material.BaseColor = { };
                    break;

                case ShaderCategory.CA_REFRACTION:
                    toReturn.DiffuseUVMultiplierIndex = 3;
                    break;

                case ShaderCategory.CA_TERRAIN:
                    toReturn.DiffuseIndex = 4;
                    break;

                case ShaderCategory.CA_LIGHTMAP_ENVIRONMENT:
                    toReturn.DiffuseIndex = 12;
                    break;

                case ShaderCategory.CA_CAMERA_MAP:
                    //DiffuseFallback = V4(1);
                    break;

                case ShaderCategory.CA_PLANET:
                    //DiffuseFallback = V4(1);
                    break;
            }

            return toReturn;
        }

        public ShaderMaterialMetadata GetMaterialMetadataFromShader(Materials.Material InMaterial, IDXRemap idx)
        {
            int RemappedIndex = idx.Datas[InMaterial.UberShaderIndex].Index;
            ShaderEntry Shader = Shaders[RemappedIndex];
            ShaderMaterialMetadata metadata = new ShaderMaterialMetadata();
            metadata.shaderCategory = (ShaderCategory)Shader.Header2.ShaderCategory;
            switch (metadata.shaderCategory)
            {
                case ShaderCategory.CA_PARTICLE:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });      //TODO: is it really?
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.COLOR_RAMP_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.FLOW_MAP });         //TODO: unsure
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.FLOW_TEXTURE_MAP }); //TODO: unsure
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    break;

                case ShaderCategory.CA_RIBBON:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.COLOR_RAMP_MAP });
                    break;

                case ShaderCategory.CA_ENVIRONMENT:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OPACITY });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ENVIRONMENT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OCCLUSION });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.FRESNEL_LUT });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.PARALLAX_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OPACITY_NOISE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIRT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.WETNESS_NOISE });
                    break;

                case ShaderCategory.CA_DECAL_ENVIRONMENT:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.PARALLAX_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ALPHA_THRESHOLD });
                    break;

                case ShaderCategory.CA_CHARACTER:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIRT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OPACITY_NOISE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OPACITY });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ENVIRONMENT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OCCLUSION });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.IRRADIANCE_MAP });
                    break;

                case ShaderCategory.CA_SKIN:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.CONVOLVED_DIFFUSE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.WRINKLE_MASK });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.WRINKLE_NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ENVIRONMENT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.IRRADIANCE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIRT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OPACITY_NOISE_MAP });
                    break;

                case ShaderCategory.CA_HAIR:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.FLOW_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.IRRADIANCE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    break;

                case ShaderCategory.CA_EYE:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.CONVOLVED_DIFFUSE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });//IrisMap
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_DIFFUSE_MAP });//VeinsMap
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SCATTER_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ENVIRONMENT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.IRRADIANCE_MAP });
                    break;

                case ShaderCategory.CA_SKIN_OCCLUSION:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    break;

                case ShaderCategory.CA_DECAL:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.EMISSIVE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.PARALLAX_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.BURN_THROUGH });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.LIQUIFY });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ALPHA_THRESHOLD });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.LIQUIFY2 });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ENVIRONMENT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.COLOR_RAMP });
                    break;

                case ShaderCategory.CA_FOGPLANE:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_DIFFUSE_MAP });
                    // TODO: Should be 'DiffuseMapStatic' - but I am not using that yet.  In order to keep the light cones
                    //  visually appealing and not slabs of solid white, I am using normal diffuse for now.
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP_STATIC });
                    break;

                case ShaderCategory.CA_REFRACTION:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ALPHA_MASK });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.FLOW_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ALPHA_THRESHOLD });
                    break;

                case ShaderCategory.CA_NONINTERACTIVE_WATER:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ALPHA_MASK });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.FLOW_MAP });
                    break;

                case ShaderCategory.CA_LOW_LOD_CHARACTER:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.LOW_LOD_CHARACTER_MASK });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.IRRADIANCE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ENVIRONMENT_MAP });
                    break;

                case ShaderCategory.CA_LIGHT_DECAL:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.EMISSIVE });
                    break;

                case ShaderCategory.CA_SPACESUIT_VISOR:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ENVIRONMENT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.MASKING_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.FACE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.UNSCALED_DIRT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIRT_MAP });
                    break;

                case ShaderCategory.CA_PLANET:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });          // TODO: This is the AtmosphereMap.
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DETAIL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });           // TODO: This is the AtmosphereNormalMap.
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_DIFFUSE_MAP });// TODO: This is the TerrainMap.
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_NORMAL_MAP }); // TODO: This is the TerrainNormalMap.
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.FLOW_MAP });
                    break;

                case ShaderCategory.CA_LIGHTMAP_ENVIRONMENT:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.LIGHT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIRT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OPACITY_NOISE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OPACITY });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ENVIRONMENT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OCCLUSION });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.PARALLAX_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    break;

                case ShaderCategory.CA_TERRAIN:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_DIFFUSE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_NORMAL_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.SECONDARY_SPECULAR_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.NONE });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.OPACITY_NOISE_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.ENVIRONMENT_MAP });
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.LIGHT_MAP });
                    break;

                case ShaderCategory.CA_CAMERA_MAP:
                    metadata.textures.Add(new MaterialTextureContext() { Type = ShaderSlot.DIFFUSE_MAP });
                    break;

                case ShaderCategory.CA_SHADOWCASTER:
                    break;

                case ShaderCategory.CA_DEFERRED:
                    break;

                case ShaderCategory.CA_DEBUG:
                    break;

                case ShaderCategory.CA_OCCLUSION_CULLING:
                    break;

                default:
                    Console.WriteLine("Unhandled shader category: " + metadata.shaderCategory);
                    break;
            }

            //if (Shader.Header.TextureLinkCount != metadata.textures.Count) throw new Exception("bruh");

            for (int i = 0; i < Shader.Header.TextureLinkCount; ++i) 
            {
                int PairIndex = Shader.TextureLinks[i];
                // NOTE: PairIndex == 255 means no index.
                if (PairIndex < InMaterial.TextureReferences.Count)
                {
                    metadata.textures[i].TextureInfo = InMaterial.TextureReferences[PairIndex];
                }
            }

            metadata.cstIndexes = GetMaterialPropertyIndexes(InMaterial);
            return metadata;
        }

        public class ShaderMaterialMetadata
        {
            public ShaderCategory shaderCategory;
            public MaterialPropertyIndex cstIndexes = new MaterialPropertyIndex();
            public List<MaterialTextureContext> textures = new List<MaterialTextureContext>();
        }

        public class MaterialTextureContext
        {
            public ShaderSlot Type = ShaderSlot.NONE;
            public Materials.Material.Texture TextureInfo = null;
        }

        public class MaterialPropertyIndex
        {
            public UInt16 DiffuseSamplerIndex = 1;
            public int OpacityUVMultiplierIndex = -1;
            public int DiffuseUVMultiplierIndex = -1;
            public int DiffuseUVAdderIndex = -1;
            public int SpecularFactorIndex = -1;
            public int MetallicFactorIndex = -1;
            public int SecondaryDiffuseUVMultiplierIndex = -1;
            public int NormalUVMultiplierIndex = -1;
            public int NormalUVMultiplierOfMultiplierIndex = -1;
            public int NormalMapStrength0Index = -1;
            public int NormalMapStrength1Index = -1;
            public int SecondaryNormalUVMultiplierIndex = -1;
            public int SpecularUVMultiplierIndex = -1;
            public int SecondarySpecularUVMultiplierIndex = -1;
            public int SecondarySpecularFactorIndex = -1;
            public int SecondaryMetallicFactorIndex = -1;
            public int DirtMapUVMultiplier0Index = -1;
            public int DirtMapUVMultiplier1Index = -1;
            public int DirtDiffuseIndex = -1;
            public int DirtStrengthIndex = -1;
            public int EmissiveFactorIndex = -1;
            public int EmissiveIndex = -1;
            public int EnvironmentMapStrengthIndex = -1;
            public int OpacityNoiseUVMultiplierIndex1 = -1;
            public int OpacityNoiseAmplitudeIndex = -1;
            public int DiffuseIndex = -1;
            public int SecondaryDiffuseIndex = -1;
            public int OcclusionUVMultiplierIndex = -1;
            public int OcclusionTintIndex = -1;
            public int IsTransparentIndex = -1;
            public int EnvironmentMapStrength2Index = -1;
            public int Unknown3_Index = -1;
            public int ParallaxUVMultiplierIndex = -1;
            public int ParallaxFactorIndex = -1;
            public int ParallaxOffsetIndex = -1;
        }

        public enum ShaderSlot
        {
            NONE = -1,
            DIFFUSE_MAP,
            COLOR_RAMP_MAP,
            SECONDARY_DIFFUSE_MAP,
            DIFFUSE_MAP_STATIC,
            OPACITY,
            NORMAL_MAP,
            SECONDARY_NORMAL_MAP,
            SPECULAR_MAP,
            SECONDARY_SPECULAR_MAP,
            ENVIRONMENT_MAP,
            OCCLUSION,
            FRESNEL_LUT,
            PARALLAX_MAP,
            OPACITY_NOISE_MAP,
            DIRT_MAP,
            WETNESS_NOISE,
            ALPHA_THRESHOLD,
            IRRADIANCE_MAP,
            CONVOLVED_DIFFUSE,
            WRINKLE_MASK,
            WRINKLE_NORMAL_MAP,
            SCATTER_MAP,
            EMISSIVE,
            BURN_THROUGH,
            LIQUIFY,
            LIQUIFY2,
            COLOR_RAMP,
            FLOW_MAP,
            FLOW_TEXTURE_MAP,
            ALPHA_MASK,
            LOW_LOD_CHARACTER_MASK,
            UNSCALED_DIRT_MAP,
            FACE_MAP,
            MASKING_MAP,
            ATMOSPHERE_MAP,
            DETAIL_MAP,
            LIGHT_MAP
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

        public class ShaderEntry
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