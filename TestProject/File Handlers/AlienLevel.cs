using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

struct pbr_object
{
    public int MeshCount;
    public List<pbr_mesh> Meshes;
    public int MaterialCount;
    public List<pbr_material> Materials;
}

struct pbr_mesh
{
    public int PrimitiveCount;
    public List<pbr_primitive> Primitives;
    public List<pbr_skinned_mesh> SkinsCurrent;
}

struct pbr_material
{

}

struct pbr_primitive
{
    public int MaterialIndex;
    public int IndexCount;

    public List<Vector3> Positions;
    public List<Vector2> Texcoords;
    public List<Vector2> Texcoords1;
    public List<Vector2> Texcoords2;
    public List<Vector2> Texcoords3;
    public List<Vector2> Texcoords7;
    public List<packed_color> Colors;
    public List<Vector3> Normals;
    public List<Vector4> Tangents;
}

struct packed_color
{
    public int R;
    public int G;
    public int B;
    public int A;
}

struct pbr_skinned_mesh
{
    public List<Vector4> Joints;
    public List<Vector4> Weights;
}

namespace TestProject.File_Handlers
{
    class AlienLevel
    {
        public static alien_level Load(string levelPath)
        {
            alien_level Result = new alien_level();

            Result.GlobalTextures = File_Handlers.Textures.TexturePAK.Load(levelPath + "/RENDERABLE/LEVEL_TEXTURES.ALL.PAK", levelPath + "/RENDERABLE/LEVEL_TEXTURE_HEADERS.ALL.BIN");

            Result.ModelsCST = File.ReadAllBytes(levelPath + "/RENDERABLE/LEVEL_MODELS.CST");
            Result.ModelsMTL = File_Handlers.Models.ModelsMTL.Load(levelPath + "/RENDERABLE/LEVEL_MODELS.MTL", Result.ModelsCST);
            Result.ModelsBIN = File_Handlers.Models.ModelBIN.Load(levelPath + "/RENDERABLE/MODELS_LEVEL.BIN");
            Result.ModelsPAK = File_Handlers.PAK.PAK.Load(levelPath + "/RENDERABLE/LEVEL_MODELS.PAK", true);
            Result.ModelsPAKChunks = File_Handlers.Models.ModelPAK.Load(Result.ModelsPAK);

            Result.ShadersPAK = File_Handlers.Shaders.ShadersPAK.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11.PAK");
            Result.ShadersIDXRemap = File_Handlers.Shaders.IDXRemap.Load(levelPath + "/RENDERABLE/LEVEL_SHADERS_DX11_IDX_REMAP.PAK");


            Result.Object = new pbr_object();

            Result.MeshCount = Result.ModelsBIN.Header.ModelCount;
            Result.Meshes = new List<pbr_mesh>(Result.MeshCount);

            Result.Object.MeshCount = Result.ModelsPAK.Header.EntryCount;
            Result.Object.Meshes = new List<pbr_mesh>(Result.Object.MeshCount);


            Result.Object.MaterialCount = Result.ModelsMTL.Header.MaterialCount;
            Result.Object.Materials = new List<pbr_material>(Result.Object.MaterialCount);



            /*
            int PrevBINIndex = -1;
            for (int EntryIndex = 0; EntryIndex < Result.ModelsPAK.Header.EntryCount; EntryIndex++)
            {
                alien_pak_model ChunkArray = Result.ModelsPAKChunks[EntryIndex];

                alien_pak_entry FirstEntry = Result.ModelsPAK.Entries[EntryIndex];
                PrevBINIndex = FirstEntry.BINIndex;

                pbr_mesh Mesh = Result.Object.Meshes[EntryIndex];
                Mesh.PrimitiveCount = ChunkArray.Header.ChunkCount;
                Mesh.Primitives = new List<pbr_primitive>(Mesh.PrimitiveCount);

                for (int ChunkIndex = 0; ChunkIndex < ChunkArray.Header.ChunkCount; ++ChunkIndex)
                {
                    alien_pak_model_chunk_info ChunkInfo = ChunkArray.ChunkInfos[ChunkIndex];
                    byte[] Chunk = ChunkArray.Chunks[ChunkIndex];

                    int BINIndex = ChunkInfo.BINIndex;

                    alien_model_bin_model_info Model = Result.ModelsBIN.Models[BINIndex];

                    pbr_material Material = Result.Object.Materials[Model.MaterialLibraryIndex];
                    alien_mtl_material InMaterial = Result.ModelsMTL.Materials[Model.MaterialLibraryIndex];

                    int RemappedIndex = Result.ShadersIDXRemap.Datas[InMaterial.UberShaderIndex].Index;

                    alien_shader_pak_shader Shader = Result.ShadersPAK.Shaders[RemappedIndex];

                    int TextureReferenceCount = Result.ModelsMTL.TextureReferenceCounts[Model.MaterialLibraryIndex];
                    ///Material = PBRMaterialFromAlienMaterial(Shader, TextureReferenceCount, InMaterial, Assets, &Result);

                    BinaryReader Stream = new BinaryReader(new MemoryStream(Chunk));

                    //render_memory *Memory = &App->Renderer->Memory;
                    //pbr_primitive *Primitive = Mesh.Primitives + ChunkIndex;
                    //memory_arena *VertexArena = &Memory->RetainedArena;
                    //u32 Alignment = Max(Memory->Alignment.StorageBufferOffset, Memory->Alignment.VertexArray);
                    pbr_primitive Primitive = new pbr_primitive();

                    alien_vertex_buffer_format VertexInput = Result.ModelsBIN.VertexBufferFormats[Model.VertexFormatIndex];
                    alien_vertex_buffer_format VertexInputLowDetail = Result.ModelsBIN.VertexBufferFormats[Model.VertexFormatIndexLowDetail];

                    pbr_skinned_mesh SkinnedMesh = new pbr_skinned_mesh();

                    // TODO: Should be true when we actually do consume them on every 'if' case.
                    if (Model.BlockSize == 0)
                    {
                        // NOTE: Not loadable.
                    }
                    else
                    {
                        int VertexArrayCount = 1;
                        List<int> ElementCounts = new List<int>(256);
                        List<alien_vertex_buffer_format_element> Elements = VertexInput.Elements; //todomattf: is this correct?

                        for (int ElementIndex = 0; ElementIndex < VertexInput.ElementCount; ++ElementIndex)
                        {
                            alien_vertex_buffer_format_element Element = VertexInput.Elements[ElementIndex];
                            if (VertexArrayCount - 1 != Element.ArrayIndex)
                            {
                                Elements[VertexArrayCount++] = Element;
                                if (Element.ArrayIndex == 0xFF)
                                {
                                    // TODO: Check something?
                                }
                                else
                                {

                                }
                            }
                            ElementCounts[VertexArrayCount - 1]++;

                            if (Element.ShaderSlot == alien_vertex_input_slot.AlienVertexInputSlot_BW)
                            {
                                if (Mesh.SkinsCurrent == null)
                                {
                                    Mesh.SkinsCurrent = new List<pbr_skinned_mesh>(Mesh.PrimitiveCount);
                                }
                                ///SkinnedMesh = MakePBRSkinnedMesh(Memory, Model.VertexCount, 0, RenderMemory_Retained);
                                Mesh.SkinsCurrent.Add(SkinnedMesh);
                            }
                        }

                        List<Int16> InIndices = new List<Int16>();

                        ///PushMesh(Primitive, Model.VertexCount, Model.IndexCount, VertexArena, Alignment);
                        Primitive.MaterialIndex = Model.MaterialLibraryIndex;

                        for (int VertexArrayIndex = 0; VertexArrayIndex < VertexArrayCount; ++VertexArrayIndex)
                        {
                            int ElementCount = ElementCounts[VertexArrayIndex];
                            alien_vertex_buffer_format_element Inputs = Elements[VertexArrayIndex];
                            if (Inputs.ArrayIndex == 0xFF)
                            {
                                alien_vertex_buffer_format_element Input = Inputs;
                                InIndices = Utilities.ConsumeArray<Int16>(Stream, Model.IndexCount);
                            }
                            else
                            {
                                for (int VertexIndex = 0; VertexIndex < Model.VertexCount; ++VertexIndex)
                                {
                                    Vector3 P = Primitive.Positions[VertexIndex];
                                    Vector2 UV = Primitive.Texcoords[VertexIndex];
                                    Vector2 UV1 = Primitive.Texcoords1[VertexIndex];
                                    Vector2 UV2 = Primitive.Texcoords2[VertexIndex];
                                    Vector2 UV3 = Primitive.Texcoords3[VertexIndex];
                                    Vector2 UV7 = Primitive.Texcoords7[VertexIndex];
                                    packed_color C = Primitive.Colors[VertexIndex];
                                    Vector3 N = Primitive.Normals[VertexIndex];
                                    Vector4 T = Primitive.Tangents[VertexIndex];

                                    for (int ElementIndex = 0; ElementIndex < ElementCount; ++ElementIndex)
                                    {
                                        alien_vertex_buffer_format_element Input = Elements[VertexArrayIndex + ElementIndex];
                                        switch (Input.VariableType)
                                        {
                                            case alien_vertex_input_type.AlienVertexInputType_v3:
                                            {
                                                Vector3 Value = Utilities.Consume<Vector3>(Stream);

                                                // TODO: Check variant index.

                                                switch (Input.ShaderSlot)
                                                {
                                                    case alien_vertex_input_slot.AlienVertexInputSlot_N:
                                                    {
                                                        N = Value;
                                                    } break;

                                                    case alien_vertex_input_slot.AlienVertexInputSlot_T:
                                                    {
                                                        T.x = Value.x;
                                                        T.y = Value.y;
                                                        T.z = Value.z;
                                                    } break;

                                                    case alien_vertex_input_slot.AlienVertexInputSlot_UV:
                                                    {
                                                        // TODO: 3D UVW.
                                                    } break;

                                                    default:
                                                    {

                                                    } break;
                                                };
                                            } break;

                                            case alien_vertex_input_type.AlienVertexInputType_u32_C:
                                            {
                                                int Value = Stream.ReadInt32();

                                                switch (Input.ShaderSlot)
                                                {
                                                    case alien_vertex_input_slot.AlienVertexInputSlot_C:
                                                    {
                                                        // TODO: Use it. Is this really color? Maybe it is some color indices.
                                                        //  I saw a color ramp texture so maybe it is several indices in that
                                                        //  texture maybe several colors at once, like emissive, diffuse, specular,
                                                        //  and ???
                                                        // EDIT: Seems to be material properties values, not color nor indices.

                                                        if (Input.VariantIndex == 0)
                                                        {
                                                            C.R = Value;
                                                            C.G = Value;
                                                            C.B = Value;
                                                            C.A = Value; //mattf - unsure if this is correct
                                                        }
                                                    } break;

                                                    default:
                                                    {

                                                    } break;
                                                }
                                            } break;

                                            case alien_vertex_input_type.AlienVertexInputType_v4u8_i:
                                            {
                                                byte[] Values = Stream.ReadBytes(4);
                                                Vector4 Value = new Vector4();
                                                Value.x = Values[0];
                                                Value.y = Values[1];
                                                Value.z = Values[2];
                                                Value.w = Values[3];

                                                switch (Input.ShaderSlot)
                                                {
                                                    case alien_vertex_input_slot.AlienVertexInputSlot_BI:
                                                    {
                                                        SkinnedMesh.Joints[VertexIndex] = Value;
                                                    } break;

                                                    default:
                                                    {

                                                    } break;
                                                }
                                            } break;

                                            case alien_vertex_input_type.AlienVertexInputType_v4u8_f:
                                            {
                                                byte[] Values = Stream.ReadBytes(4);
                                                Vector4 Value = new Vector4();
                                                Value.x = Values[0] / 255.0f;
                                                Value.y = Values[1] / 255.0f;
                                                Value.z = Values[2] / 255.0f;
                                                Value.w = Values[3] / 255.0f;

                                                switch (Input.ShaderSlot)
                                                {
                                                    case alien_vertex_input_slot.AlienVertexInputSlot_BW:
                                                    {
                                                        // NOTE: My skinning shader assumes the weights will always sum up to 1.
                                                        float Sum = Value.x + Value.y + Value.z + Value.w;
                                                        Vector4 calculated = new Vector4();
                                                        calculated.x = Value.x / Sum;
                                                        calculated.y = Value.y / Sum;
                                                        calculated.z = Value.z / Sum;
                                                        calculated.w = Value.w / Sum;
                                                        SkinnedMesh.Weights[VertexIndex] = calculated;
                                                    } break;

                                                    case alien_vertex_input_slot.AlienVertexInputSlot_UV:
                                                    {
                                                        UV2.x = Value.x;
                                                        UV2.y = Value.y;

                                                        UV3.x = Value.z;
                                                        UV3.y = Value.w;
                                                    } break;

                                                    default:
                                                    {

                                                    } break;
                                                }
                                            } break;

                                            case alien_vertex_input_type.AlienVertexInputType_v2s16_UV:
                                            {
                                                List<Int16> Values = Utilities.ConsumeArray<Int16>(Stream, 2);
                                                Vector2 Value = new Vector2();
                                                Value.x = Values[0] / 2048.0f;
                                                Value.y = Values[1] / 2048.0f;

                                                switch (Input.ShaderSlot)
                                                {
                                                    case alien_vertex_input_slot.AlienVertexInputSlot_UV:
                                                    {
                                                        if (Input.VariantIndex == 0)
                                                        {
                                                            UV = Value;
                                                        }
                                                        else if (Input.VariantIndex == 1)
                                                        {
                                                            // TODO: We can figure this out based on
                                                            //  alien_vertex_buffer_format_element.
                                                            ///Material.Material.Flags |= Material_HasTexCoord1;
                                                            UV1 = Value;
                                                        }
                                                        else if (Input.VariantIndex == 2)
                                                        {
                                                            UV2 = Value;
                                                        }
                                                        else if (Input.VariantIndex == 3)
                                                        {
                                                            UV3 = Value;
                                                        }
                                                        else if (Input.VariantIndex == 7)
                                                        {
                                                            // TODO: Wat? Why are those always zero?
                                                            // TODO: They are not. In 'hab_airport' they have non-zero values.
                                                            UV7 = Value;
                                                        }
                                                        else
                                                        {

                                                        }
                                                    } break;

                                                    default:
                                                    {

                                                    } break;
                                                }
                                            } break;

                                            case alien_vertex_input_type.AlienVertexInputType_v4s16_f:
                                            {
                                                List<Int16> Values = Utilities.ConsumeArray<Int16>(Stream, 2);
                                                Vector4 Value = new Vector4();
                                                Value.x = Values[0] / (float)Int16.MaxValue;
                                                Value.y = Values[1] / (float)Int16.MaxValue;
                                                Value.w = Values[2] / (float)Int16.MaxValue;
                                                Value.z = Values[3] / (float)Int16.MaxValue;

                                                // TODO: Check variant index.

                                                switch (Input.ShaderSlot)
                                                {
                                                    case alien_vertex_input_slot.AlienVertexInputSlot_P:
                                                    {
                                                        P.x = Value.x;
                                                        P.y = Value.y;
                                                        P.z = Value.z;
                                                        T.w = Value.w;
                                                    } break;

                                                    default:
                                                    {

                                                    } break;
                                                }
                                            } break;

                                            case alien_vertex_input_type.AlienVertexInputType_v2s16_f:
                                            {
                                                byte[] Values = Stream.ReadBytes(4);

                                                Vector3 Value = new Vector3();
                                                Value.x = Values[0] / (float)Byte.MaxValue - 0.5f; 
                                                Value.y = Values[1] / (float)Byte.MaxValue - 0.5f;
                                                Value.z = Values[2] / (float)Byte.MaxValue - 0.5f;
                                                Value = Normalize(Value);

                                                switch (Input.ShaderSlot)
                                                {
                                                    case alien_vertex_input_slot.AlienVertexInputSlot_N:
                                                    {
                                                        N = Value;
                                                    } break;

                                                    case alien_vertex_input_slot.AlienVertexInputSlot_T:
                                                    {
                                                        T.x = Value.x;
                                                        T.y = Value.y;
                                                        T.z = Value.z;
                                                    } break;

                                                    case alien_vertex_input_slot.AlienVertexInputSlot_B:
                                                    {
                                                        // TODO: What is this? Seems to be Bitangent.
                                                    } break;

                                                    default:
                                                    {

                                                    } break;
                                                }
                                            } break;

                                            default:
                                            {

                                            } break;
                                        }
                                    }
                                }
                            }
                            Utilities.Align(Stream, 16);
                        }

                        Array.Copy(Primitive.Indices, InIndices, Model.IndexCount);
                        //CopyArray(Primitive->Indices, InIndices, Model->IndexCount);
                    }

                    for (int VertexIndex = 0; VertexIndex < Primitive.VertexCount; ++VertexIndex)
                    {
                        Vector3 P = Primitive.Positions[VertexIndex];
                        P.x *= Model.ScaleFactor;
                        P.y *= Model.ScaleFactor;
                        P.z *= Model.ScaleFactor;
                        //P.y = -P.y;
                        Primitive.Positions[VertexIndex] = P; //Rotate(P, QuaternionAxisAngle(V3(1,0,0), Pi * 0.5f));

        //                packed_color C = Primitive->Colors[VertexIndex];
        //                Primitive->Colors[VertexIndex] = {C.r, C.g, C.b, C.a};

        //                Primitive->Colors[VertexIndex] = PackedColor_White;

                        // TODO: I am flipping the normals here because of a particularity of my rendering engine.
                        //  This geometry is all Left Handed. After converting it to Right Handed, we end up with inverted
                        //  triangle winding order. My lighting shader flips the normal if the wrong winding order is detected.
                        //  It does that to handle double-sided geometry.
                        //  So then I end up with correct normals in the lighting shader. This is all a bit weird and
                        //  I would like instead to preprocess all this data and convert it to Right Handed and CCW winding
                        //  before handing it over to to the renderer. At that point we will be able to delete this.
                        Vector3 invert = Primitive.Normals[VertexIndex];
                        invert.x = -invert.x;
                        invert.y = -invert.y;
                        invert.z = -invert.z;
                        Primitive.Normals[VertexIndex] = invert;

                        if (IsDegenerate(Primitive.Tangents[VertexIndex].xyz))
                        {
                            Primitive.Tangents[VertexIndex].w = 0;
                        }
                    }



                    pbr_mesh LMesh = new pbr_mesh();
                    //if (Primitive.IndexCount)
                    //{
                        LMesh.PrimitiveCount = 1;
                        LMesh.Primitives = PushCopyArray(Arena, 1, Primitive);
                        if (SkinnedMesh)
                        {
                            LMesh.SkinsCurrent = PushCopyArray(Arena, 1, SkinnedMesh);
                        }
                    //}
                    Result.Meshes[BINIndex] = LMesh;

                    //mattf
                    Mesh.Primitives.Add(Primitive);
                }
            }




            */
            return Result;
        }
    }
}

struct alien_level
{
    public List<alien_pak_model> ModelsPAKChunks;
    public alien_pak ModelsPAK;
    public alien_mtl ModelsMTL;
    public byte[] ModelsCST;
    public alien_textures GlobalTextures;
    public alien_textures LevelTextures;
    public alien_model_bin ModelsBIN;
    public alien_shader_pak ShadersPAK;
    //alien_shader_bin_pak ShadersBIN;
    public alien_shader_idx_remap ShadersIDXRemap;
    //alien_reds_bin RenderableREDS;

    //alien_mvr ModelsMVR;
    //alien_type_infos TypeInfos;
    //alien_commands_bin CommandsBIN;
    //alien_commands_pak CommandsPAK;
    //alien_commands Commands;
    //alien_reds_bin WorldREDS;
    //alien_resources_bin ResourcesBIN;

    public pbr_object Object;
    public int MeshCount;
    public List<pbr_mesh> Meshes;

    //alien_physics_map PhysicsMap;

    //alien_animation_dat EnvironmentAnimation;

    //alien_pak2 GlobalAnimations;
    //alien_anim_string_db GlobalAnimationsStrings;
};