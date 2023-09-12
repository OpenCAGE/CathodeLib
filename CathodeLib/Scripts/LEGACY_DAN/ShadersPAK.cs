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
                    toReturn.Unknown3_ = 3;
                    toReturn.OpacityMapUVMultiplier = 5;
                    toReturn.DiffuseMap0UVMultiplier = 6;
                    toReturn.Diffuse0 = 7;
                    toReturn.DiffuseMap1UVMultiplier = 8;
                    toReturn.Diffuse1 = 9;
                    toReturn.NormalMap0UVMultiplier = 10;
                    toReturn.NormalMap0Strength = 11;
                    toReturn.NormalMap1UVMultiplier = 12;
                    toReturn.NormalMap1Strength = 13;

                    toReturn.SpecularFactor0 = 14;
                    toReturn.SpecularMap0UVMultiplier = 15;
                    toReturn.MetallicFactor0 = 16;

                    toReturn.SpecularFactor1 = 17;
                    toReturn.SpecularMap1UVMultiplier = 18;
                    toReturn.MetallicFactor1 = 19;

                    toReturn.EnvironmentMapEmission = 24;
                    toReturn.EnvironmentMapStrength = 25;

                    toReturn.OcclusionMapUVMultiplier = 27;
                    toReturn.OcclusionTint = 28;

                    // NOTE: We should multiply 'StabilityHack' here instead, but we do it after the probe step
                    //  to make the lighting more stable. Not sure if this is physically correct, but the result looks similar.
                    toReturn.EmissiveFactor = 29;
                    toReturn.Emission = 30;

                    toReturn.ParallaxMapUVMultiplier = 35;
                    toReturn.ParallaxFactor = 36;
                    toReturn.ParallaxOffset = 37;

                    toReturn.IsTransparent = 38;

                    toReturn.OpacityNoiseMapUVMultiplier = 39;
                    toReturn.OpacityNoiseAmplitude = 40;

                    toReturn.DirtPower = 47;
                    toReturn.DirtUVMultiplier = 48;
                    toReturn.DirtStrength = 49;
                    break;

                case ShaderCategory.CA_CHARACTER:
                    toReturn.OpacityNoiseMapUVMultiplier = 12;
                    toReturn.DiffuseMap0UVMultiplier = 15;
                    toReturn.Diffuse0 = 16;

                    toReturn.DiffuseMap1UVMultiplier = 17;
                    toReturn.Diffuse1 = 18;

                    toReturn.NormalMap0UVMultiplier = 19;
                    toReturn.NormalMap0Strength = 20;
                    toReturn.NormalMap1UVMultiplier = 21;
                    toReturn.NormalMap1Strength = 22;

                    toReturn.SpecularMap0UVMultiplier = 24;
                    toReturn.SpecularFactor0 = 25;

                    // TODO: Find out about these?
                    //toReturn.MetallicFactor0 = 1;
                    //toReturn.OcclusionTint =
                    //toReturn.OpacityNoiseMapUVMultiplier = 1;
                    break;

                case ShaderCategory.CA_SKIN:
                    toReturn.DiffuseMap0UVMultiplier = 4;
                    toReturn.Diffuse0 = 5;

                    toReturn.NormalMap0UVMultiplier = 8;
                    toReturn.NormalMap0Strength = 9;
                    toReturn.NormalMap0UVMultiplierOfMultiplier = 10;
                    toReturn.NormalMap1UVMultiplier = 11;
                    break;

                case ShaderCategory.CA_HAIR:
                    toReturn.DiffuseMap0UVMultiplier = 1;
                    toReturn.Diffuse0 = 2;

                    toReturn.NormalMap0Strength = 9;
                    toReturn.NormalMap0UVMultiplier = 8;
                    toReturn.SpecularMap0UVMultiplier = 7;
                    break;

                case ShaderCategory.CA_EYE:
                    toReturn.Unknown0_ = 0;
                    toReturn.RetinaRadius = 1;
                    toReturn.IrisParallaxDisplacement = 2;
                    toReturn.LimbalSmoothRadius = 3;
                    toReturn.RetinaIndexOfRefraction = 4; // I think this is the index of refraction of a retina.
                    //Assert(toReturn.RetinaIndexOfRefraction >= 1;
                    toReturn.PupilDilation = 5;
                    toReturn.ScatterMapMultiplier = 6;

                    toReturn.Iris0 = 7;
                    toReturn.Iris1 = 8;
                    toReturn.Iris2 = 9;
                    toReturn.NormalMapUVMultiplier = 10;
                    toReturn.NormalMapStrength = 11;
                    toReturn.EnvironmentMapStrength = 12;
                    toReturn.Unknown13_ = 13;
                    //toReturn.ScatterMap.SamplerIndex = ADDRESS_MODE_CLAMP_TO_BORDER;
                    break;

                case ShaderCategory.CA_LIGHTMAP_ENVIRONMENT:
                    toReturn.Diffuse0 = 12;
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
                if (i >= metadata.textures.Count) break; //This should no longer be an issue when the shader categories are completed above.

                int PairIndex = Shader.TextureLinks[i];
                // NOTE: PairIndex == 255 means no index.
                if (PairIndex < InMaterial.TextureReferences.Length)
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
            public int Diffuse0;
            public int Diffuse1;
            public int DiffuseMap0;
            public int DiffuseMap0UVMultiplier;
            public int DiffuseMap1;
            public int DiffuseMap1UVMultiplier;
            public int DirtMap;
            public int DirtPower;
            public int DirtStrength;
            public int DirtUVMultiplier;
            public int Emission;
            public int EmissiveFactor;
            public int EnvironmentMap;
            public int EnvironmentMapEmission;
            public int EnvironmentMapStrength;
            public int FresnelLUT;
            public int Iris0;
            public int Iris1;
            public int Iris2;
            public int IrisParallaxDisplacement;
            public int IsTransparent;
            public int LimbalSmoothRadius;
            public int Metallic;
            public int MetallicFactor0;
            public int MetallicFactor1;
            public int NormalMap0;
            public int NormalMap0Strength;
            public int NormalMap0UVMultiplier;
            public int NormalMap0UVMultiplierOfMultiplier;
            public int NormalMap1;
            public int NormalMap1Strength;
            public int NormalMap1UVMultiplier;
            public int NormalMapStrength;
            public int NormalMapUVMultiplier;
            public int OcclusionMap;
            public int OcclusionMapUVMultiplier;
            public int OcclusionTint;
            public int OpacityMap;
            public int OpacityMapUVMultiplier;
            public int OpacityNoiseAmplitude;
            public int OpacityNoiseMap;
            public int OpacityNoiseMapUVMultiplier;
            public int ParallaxFactor;
            public int ParallaxMap;
            public int ParallaxMapUVMultiplier;
            public int ParallaxOffset;
            public int PupilDilation;
            public int RetinaIndexOfRefraction;
            public int RetinaRadius;
            public int ScatterMapMultiplier;
            public int SpecularFactor0;
            public int SpecularFactor1;
            public int SpecularMap0;
            public int SpecularMap0UVMultiplier;
            public int SpecularMap1;
            public int SpecularMap1UVMultiplier;
            public int Unknown0_;
            public int Unknown13_;
            public int Unknown1_;
            public int Unknown3_;
            public int WetnessNoiseMap;
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