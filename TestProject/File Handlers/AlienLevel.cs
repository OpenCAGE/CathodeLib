using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public class AlienModel
{
    public List<AlienModelPart> parts = new List<AlienModelPart>();
}

public class AlienModelPart
{
    public List<int> indicies = new List<int>();

    public List<V3> vertices = new List<V3>();
    public List<V3> normals = new List<V3>();
    public List<V2> uvs = new List<V2>();
    public List<V2> uvs1 = new List<V2>();
    public List<V2> uvs2 = new List<V2>();
    public List<V2> uvs3 = new List<V2>();
    public List<V2> uvs7 = new List<V2>();
    public List<V4> colours = new List<V4>();
}

namespace TestProject.File_Handlers
{
    public class AlienLevel
    {
        public static alien_level Load(string levelPath)
        {
            alien_level Result = new alien_level();

            Result.RenderableREDS = File_Handlers.Misc.RenderableElementsBIN.Load(levelPath + "/RENDERABLE/REDS.BIN");

            Result.LevelTextures = File_Handlers.Textures.TexturePAK.Load(levelPath + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK", levelPath + "/RENDERABLE/LEVEL_TEXTURE_HEADERS.ALL.BIN");
            Result.GlobalTextures = File_Handlers.Textures.TexturePAK.Load(levelPath + "/../../GLOBAL/WORLD/GLOBAL_TEXTURES.ALL.PAK", levelPath + "/../../GLOBAL/WORLD/GLOBAL_TEXTURES_HEADERS.ALL.BIN");

            Result.ModelsCST = File.ReadAllBytes(levelPath + "/RENDERABLE/LEVEL_MODELS.CST");
            Result.ModelsMTL = File_Handlers.Models.ModelsMTL.Load(levelPath + "/RENDERABLE/LEVEL_MODELS.MTL", Result.ModelsCST);
            Result.ModelsBIN = File_Handlers.Models.ModelBIN.Load(levelPath + "/RENDERABLE/MODELS_LEVEL.BIN");
            Result.ModelsPAK = File_Handlers.Models.ModelPAK.Load(levelPath + "/RENDERABLE/LEVEL_MODELS.PAK");

            Result.ShadersPAK = File_Handlers.Shaders.ShadersPAK.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11.PAK");
            //Result.ShadersBIN = File_Handlers.Shaders.ShadersBIN.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11_BIN.PAK");
            Result.ShadersIDXRemap = File_Handlers.Shaders.IDXRemap.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11_IDX_REMAP.PAK");

            Result.ResourcesBIN = File_Handlers.Misc.ResourcesBIN.Load(levelPath + "/WORLD/RESOURCES.BIN");

            Result.PhysicsMap = File_Handlers.Misc.PhysicsMAP.Load(levelPath + "/WORLD/PHYSICS.MAP");

            return Result;


            //Result.ModelsPAK.PAK.Header.EntryCount =  Mesh Count
            //Result.ModelsMTL.Header.MaterialCount  =  Material Count

            List<AlienModel> allModels = new List<AlienModel>();

            for (int EntryIndex = 0; EntryIndex < Result.ModelsPAK.PAK.Header.EntryCount; EntryIndex++)
            {
                alien_pak_model_entry ChunkArray = Result.ModelsPAK.Models[EntryIndex];
                alien_pak_entry FirstEntry = Result.ModelsPAK.PAK.Entries[EntryIndex];

                AlienModel thisModel = new AlienModel();
                thisModel.parts = new List<AlienModelPart>(ChunkArray.Header.ChunkCount);

                for (int ChunkIndex = 0; ChunkIndex < ChunkArray.Header.ChunkCount; ++ChunkIndex)
                {
                    int BINIndex = ChunkArray.ChunkInfos[ChunkIndex].BINIndex;
                    alien_model_bin_model_info Model = Result.ModelsBIN.Models[BINIndex];
                    if (Model.BlockSize == 0) continue;

                    alien_mtl_material InMaterial = Result.ModelsMTL.Materials[Model.MaterialLibraryIndex];
                    //int RemappedIndex = Result.ShadersIDXRemap.Datas[InMaterial.UberShaderIndex].Index;
                    //alien_shader_pak_shader Shader = Result.ShadersPAK.Shaders[RemappedIndex];
                    //int TextureReferenceCount = Result.ModelsMTL.TextureReferenceCounts[Model.MaterialLibraryIndex];

                    alien_vertex_buffer_format VertexInput = Result.ModelsBIN.VertexBufferFormats[Model.VertexFormatIndex];
                    alien_vertex_buffer_format VertexInputLowDetail = Result.ModelsBIN.VertexBufferFormats[Model.VertexFormatIndexLowDetail];

                    BinaryReader Stream = new BinaryReader(new MemoryStream(ChunkArray.Chunks[ChunkIndex]));
                    AlienModelPart thisPart = new AlienModelPart();

                    int VertexArrayCount = 1;
                    List<alien_vertex_buffer_format_element> Elements = VertexInput.Elements; //todomattf: is this correct?
                    List<int> ElementCounts = new List<int>(new int[VertexInput.Elements.Count]);

                    for (int ElementIndex = 0; ElementIndex < VertexInput.ElementCount; ++ElementIndex)
                    {
                        alien_vertex_buffer_format_element Element = VertexInput.Elements[ElementIndex];
                        if (VertexArrayCount - 1 != Element.ArrayIndex)
                        {
                            Elements[VertexArrayCount++] = Element;
                        }
                        ElementCounts[VertexArrayCount - 1]++;
                    }

                    List<Int16> InIndices = new List<Int16>();

                    for (int VertexArrayIndex = 0; VertexArrayIndex < VertexArrayCount; ++VertexArrayIndex)
                    {
                        int ElementCount = ElementCounts[VertexArrayIndex];
                        alien_vertex_buffer_format_element Inputs = Elements[VertexArrayIndex];
                        if (Inputs.ArrayIndex == 0xFF) InIndices = Utilities.ConsumeArray<Int16>(ref Stream, Model.IndexCount);
                        else
                        {
                            for (int VertexIndex = 0; VertexIndex < Model.VertexCount; ++VertexIndex)
                            {
                                for (int ElementIndex = 0; ElementIndex < ElementCount; ++ElementIndex)
                                {
                                    alien_vertex_buffer_format_element Input = Elements[VertexArrayIndex + ElementIndex];
                                    switch (Input.VariableType)
                                    {
                                        case alien_vertex_input_type.AlienVertexInputType_v3:
                                        {
                                            V3 Value = Utilities.Consume<V3>(ref Stream);
                                            // TODO: Check variant index.
                                            switch (Input.ShaderSlot)
                                            {
                                                case alien_vertex_input_slot.AlienVertexInputSlot_N:
                                                    thisPart.normals.Add(Value);
                                                    break;
                                                case alien_vertex_input_slot.AlienVertexInputSlot_T:
                                                    //TANGENTS
                                                    //T.x = Value.x;
                                                    //T.y = Value.y;
                                                    //T.z = Value.z;
                                                    break;
                                                case alien_vertex_input_slot.AlienVertexInputSlot_UV:
                                                    // TODO: 3D UVW.
                                                    break;
                                            };
                                            break;
                                        }

                                        case alien_vertex_input_type.AlienVertexInputType_u32_C:
                                        {
                                            int Value = Stream.ReadInt32();
                                            switch (Input.ShaderSlot)
                                            {
                                                case alien_vertex_input_slot.AlienVertexInputSlot_C:
                                                    //Seems to be material properties values, not color nor indices.
                                                    if (Input.VariantIndex == 0)
                                                    {
                                                        thisPart.colours.Add(new V4(Value)); //In this case, XYZW is RGBA
                                                    }
                                                    break;
                                            }
                                            break;
                                        }

                                        case alien_vertex_input_type.AlienVertexInputType_v4u8_i:
                                        {
                                            V4 Value = new V4(Stream.ReadBytes(4));
                                            switch (Input.ShaderSlot)
                                            {
                                                case alien_vertex_input_slot.AlienVertexInputSlot_BI:
                                                    //SkinnedMesh.Joints[VertexIndex] = Value;
                                                    break;
                                            }
                                            break;
                                        }

                                        case alien_vertex_input_type.AlienVertexInputType_v4u8_f:
                                        {
                                            V4 Value = new V4(Stream.ReadBytes(4));
                                            Value = Value / 255.0f;

                                            switch (Input.ShaderSlot)
                                            {
                                                case alien_vertex_input_slot.AlienVertexInputSlot_BW:
                                                    // NOTE: My skinning shader assumes the weights will always sum up to 1.
                                                    float Sum = Value.x + Value.y + Value.z + Value.w;
                                                    V4 calculated = Value / Sum;
                                                    //SkinnedMesh.Weights[VertexIndex] = calculated;
                                                    break;
                                                case alien_vertex_input_slot.AlienVertexInputSlot_UV:
                                                    thisPart.uvs2.Add(new V2(Value.x, Value.y));
                                                    thisPart.uvs3.Add(new V2(Value.z, Value.w));
                                                    break;
                                            }
                                            break;
                                        }

                                        case alien_vertex_input_type.AlienVertexInputType_v2s16_UV:
                                        {
                                            List<Int16> Values = Utilities.ConsumeArray<Int16>(ref Stream, 2);
                                            V2 Value = new V2(Values[0], Values[1]);
                                            Value = Value / 2048.0f;

                                            switch (Input.ShaderSlot)
                                            {
                                                case alien_vertex_input_slot.AlienVertexInputSlot_UV:
                                                    if (Input.VariantIndex == 0) thisPart.uvs.Add(Value);
                                                    else if (Input.VariantIndex == 1)
                                                    {
                                                        // TODO: We can figure this out based on alien_vertex_buffer_format_element.
                                                        ///Material.Material.Flags |= Material_HasTexCoord1;
                                                        thisPart.uvs1.Add(Value);
                                                    }
                                                    else if (Input.VariantIndex == 2) thisPart.uvs2.Add(Value);
                                                    else if (Input.VariantIndex == 3) thisPart.uvs3.Add(Value);
                                                    else if (Input.VariantIndex == 7) thisPart.uvs7.Add(Value);
                                                    break;
                                            }
                                            break;
                                        }

                                        case alien_vertex_input_type.AlienVertexInputType_v4s16_f:
                                        {
                                            List<Int16> Values = Utilities.ConsumeArray<Int16>(ref Stream, 4);
                                            V4 Value = new V4(Values[0], Values[1], Values[2], Values[3]);
                                            Value = Value / (float)Int16.MaxValue;

                                            // TODO: Check variant index.
                                            switch (Input.ShaderSlot)
                                            {
                                                case alien_vertex_input_slot.AlienVertexInputSlot_P:
                                                    thisPart.vertices.Add(new V3(Value));
                                                    //T.w = Value.w;
                                                    break;
                                            }
                                            break;
                                        }

                                        case alien_vertex_input_type.AlienVertexInputType_v2s16_f:
                                            {
                                                V4 Value = new V4(Stream.ReadBytes(4));
                                                Value = Value / (float)Byte.MaxValue - 0.5f;
                                                Value.Normalise();

                                                switch (Input.ShaderSlot)
                                                {
                                                    case alien_vertex_input_slot.AlienVertexInputSlot_N:
                                                        thisPart.normals.Add(new V3(Value));
                                                        break;
                                                    case alien_vertex_input_slot.AlienVertexInputSlot_T:
                                                        //T = Value;
                                                        break;
                                                    case alien_vertex_input_slot.AlienVertexInputSlot_B:
                                                        // TODO: What is this? Seems to be Bitangent.
                                                        break;
                                                }
                                            }
                                            break;
                                    }
                                }
                            }
                        }
                        Utilities.Align(ref Stream, 16);
                    }

                    //probably a better way to do this
                    for (int i = 0; i < InIndices.Count; i++) thisPart.indicies.Add(InIndices[i]);

                    thisModel.parts.Add(thisPart);
                }
                allModels.Add(thisModel);
            }


            return Result;
        }
    }
}

public struct alien_level
{
    public byte[] ModelsCST;
    public alien_mtl ModelsMTL;
    public alien_pak_model ModelsPAK;
    public alien_model_bin ModelsBIN;

    public alien_textures GlobalTextures;
    public alien_textures LevelTextures;

    public alien_shader_pak ShadersPAK;
    public alien_shader_bin_pak ShadersBIN;
    public alien_shader_idx_remap ShadersIDXRemap;

    public alien_reds_bin RenderableREDS;

    //alien_mvr ModelsMVR;
    //alien_type_infos TypeInfos;
    //alien_commands_bin CommandsBIN;
    //alien_commands_pak CommandsPAK;
    //alien_commands Commands;
    //public alien_reds_bin WorldREDS; - this doesnt exist on win32
    public alien_resources_bin ResourcesBIN;

    public int MeshCount;

    public alien_physics_map PhysicsMap;

    //alien_animation_dat EnvironmentAnimation;

    //alien_pak2 GlobalAnimations;
    //alien_anim_string_db GlobalAnimationsStrings;
};