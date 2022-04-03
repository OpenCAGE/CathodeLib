using CATHODE.Generic;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE.Models
{
    public class CathodeModels : CathodePAK
    {
        public List<ModelData> Models;

        private alien_model_bin modelBIN;

        private string pathToPAK, pathToBIN;

        public CathodeModels(string binFilepath, string pakFilepath)
        {
            pathToBIN = binFilepath;
            LoadModelBIN();
            pathToPAK = pakFilepath;
            LoadModelPAK();
        }

        private void LoadModelPAK()
        {
            LoadPAK(pathToPAK, true);

            Models = new List<ModelData>(PAKHeader.EntryCount);
            for (int i = 0; i < PAKHeader.EntryCount; ++i)
            {
                BinaryReader modelBuffer = new BinaryReader(new MemoryStream(EntryDatas[i]));

                ModelData Model = new ModelData();
                Model.Header = Utilities.Consume<ModelHeader>(modelBuffer);
                Model.Header.FirstChunkIndex = BinaryPrimitives.ReverseEndianness(Model.Header.FirstChunkIndex);
                Model.Header.SubmeshCount = BinaryPrimitives.ReverseEndianness(Model.Header.SubmeshCount);

                ModelChunkInfo[] SubmeshInfo = Utilities.ConsumeArray<ModelChunkInfo>(modelBuffer, Model.Header.SubmeshCount);
                Utilities.Align(modelBuffer, 16);

                Model.Submeshes = new List<ModelDataEntry>(Model.Header.SubmeshCount);
                for (int x = 0; x < SubmeshInfo.Length; ++x)
                {
                    SubmeshInfo[x].BINIndex = BinaryPrimitives.ReverseEndianness(SubmeshInfo[x].BINIndex);
                    SubmeshInfo[x].Offset = BinaryPrimitives.ReverseEndianness(SubmeshInfo[x].Offset);
                    SubmeshInfo[x].Size = BinaryPrimitives.ReverseEndianness(SubmeshInfo[x].Size);

                    ModelDataEntry newChunk = new ModelDataEntry();
                    modelBuffer.BaseStream.Position = SubmeshInfo[x].Offset;
                    newChunk.content = modelBuffer.ReadBytes(SubmeshInfo[x].Size);
                    newChunk.binIndex = SubmeshInfo[x].BINIndex;
                    Model.Submeshes.Add(newChunk);
                }
                Models.Add(Model);

                modelBuffer.Close();
            }
        }

        private void LoadModelBIN()
        {
            modelBIN = new alien_model_bin();
            BinaryReader reader = new BinaryReader(File.OpenRead(pathToBIN));

            modelBIN.Header = Utilities.Consume<alien_model_bin_header>(reader);

            modelBIN.VertexBufferFormats = new List<alien_vertex_buffer_format>(modelBIN.Header.VertexInputCount);
            for (int i = 0; i < modelBIN.Header.VertexInputCount; ++i)
            {
                long startPos = reader.BaseStream.Position;
                int count = 1;
                while (reader.ReadByte() != 0xFF)
                {
                    reader.BaseStream.Position += Marshal.SizeOf(typeof(alien_vertex_buffer_format_element)) - 1;
                    count++;
                }
                reader.BaseStream.Position = startPos;

                alien_vertex_buffer_format VertexInput = new alien_vertex_buffer_format();
                VertexInput.ElementCount = count;
                VertexInput.Elements = Utilities.ConsumeArray<alien_vertex_buffer_format_element>(reader, VertexInput.ElementCount);
                modelBIN.VertexBufferFormats.Add(VertexInput);
            }

            byte[] filenameContent = reader.ReadBytes(reader.ReadInt32());
            modelBIN.Models = Utilities.ConsumeArray<alien_model_bin_model_info>(reader, modelBIN.Header.ModelCount);

            modelBIN.ModelFilePaths = new List<string>(modelBIN.Header.ModelCount);
            modelBIN.ModelLODPartNames = new List<string>(modelBIN.Header.ModelCount);
            for (int EntryIndex = 0; EntryIndex < modelBIN.Header.ModelCount; ++EntryIndex)
            {
                modelBIN.ModelFilePaths.Add(Utilities.ReadString(filenameContent, modelBIN.Models[EntryIndex].FileNameOffset).Replace('\\', '/'));
                modelBIN.ModelLODPartNames.Add(Utilities.ReadString(filenameContent, modelBIN.Models[EntryIndex].ModelPartNameOffset).Replace('\\', '/'));
            }

            int BoneBufferCount = reader.ReadInt32();
            byte[] BoneBuffer = Utilities.ConsumeArray<byte>(reader, BoneBufferCount);
            //TODO: implement bone parsing

            reader.Close();
        }

        public enum alien_vertex_input_type
        {
            AlienVertexInputType_v3 = 0x03,
            // TODO: Present at 'bsp_torrens' but I haven't seen models that contain that being rendered yet.
            AlienVertexInputType_Unknown0_ = 0x04,
            AlienVertexInputType_u32_C = 0x05,
            AlienVertexInputType_v4u8_i = 0x06,
            AlienVertexInputType_v4u8_f = 0x09,
            AlienVertexInputType_v2s16_UV = 0x0A,
            AlienVertexInputType_v4s16_f = 0x0B,
            AlienVertexInputType_v4u8_NTB = 0x0F,
            AlienVertexInputType_u16 = 0x13,
        };

        public enum alien_vertex_input_slot
        {
            AlienVertexInputSlot_P = 0x01,
            AlienVertexInputSlot_BW = 0x02, // NOTE: Bone Weights
            AlienVertexInputSlot_BI = 0x03, // NOTE: Bone Indices
            AlienVertexInputSlot_N = 0x04,
            AlienVertexInputSlot_UV = 0x06,
            AlienVertexInputSlot_T = 0x07, // NOTE: Tangent
            AlienVertexInputSlot_B = 0x08, // NOTE: Bitangent
            AlienVertexInputSlot_C = 0x0A, // NOTE: Color? Specular? What is this?
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct alien_model_bin_header
        {
            public fourcc FourCC;
            public int ModelCount;
            public Int16 Unknown0_;
            public Int16 Version;
            public int VertexInputCount;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct alien_model_bin_model_info
        {
            public int FileNameOffset;
            public int UnknownZero_; // NOTE: Always 0 on starting area.
            public int ModelPartNameOffset;
            public float UnknownValue0_; // NOTE: Always 0 on starting area.
            public Vector3 AABBMin;
            public float LODMinDistance_;
            public Vector3 AABBMax;
            public float LODMaxDistance_;
            public int NextInLODGroup_;
            public int FirstModelInGroupForNextLOD_;
            public int MaterialLibraryIndex;
            public uint Unknown2_; // NOTE: Flags?
            public int UnknownIndex; // NOTE: -1 means no index. Seems to be related to Next/Parent.
            public uint BlockSize;
            public int CollisionIndex_; // NODE: If this is not -1, model piece name starts with "COL_" and are always character models.
            public int BoneArrayOffset_; // TODO: This indexes on the leftover data and points to an array of linearly growing indices.
                                         //  And the array count is BoneCount.
            public UInt16 VertexFormatIndex;
            public UInt16 VertexFormatIndexLowDetail;
            public UInt16 VertexFormatIndexDefault_; // TODO: This is always equals to the VertexFormatIndex
            public UInt16 ScaleFactor;
            public Int16 HeadRelated_; // NOTE: Seems to be valid on some 'HEAD' models, otherwise -1. Maybe morphing related???
            public UInt16 VertexCount;
            public UInt16 IndexCount;
            public UInt16 BoneCount;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct alien_vertex_buffer_format_element
        {
            public int ArrayIndex;
            public int Offset; // NOTE: Offset within data structure, generally not important.
            public alien_vertex_input_type VariableType; //(int)
            public alien_vertex_input_slot ShaderSlot; //(int)
            public int VariantIndex; // NOTE: Variant index such as UVs: (UV0, UV1, UV2...)
            public int Unknown_; // NOTE: Seems to be always 2?
        };

        public struct alien_vertex_buffer_format
        {
            public int ElementCount;
            public alien_vertex_buffer_format_element[] Elements;
        };

        public class alien_model_bin
        {
            public byte[] Buffer;
            public alien_model_bin_header Header;

            public List<alien_vertex_buffer_format> VertexBufferFormats;
            public alien_model_bin_model_info[] Models;
            public List<string> ModelFilePaths;
            public List<string> ModelLODPartNames;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ModelHeader
        {
            public int FirstChunkIndex;
            public int SubmeshCount;
            // NOTE: Apparently always zeroes below here
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public int[] Unknown; //4
        };

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ModelChunkInfo
        {
            public int BINIndex;
            public int Offset;
            public int Size;
            public int _Unknown1; // NOTE: Probably struct padding
        };

        public class ModelDataEntry
        {
            public int binIndex; //todo: just hold this bin info internally
            public byte[] content;
        }

        public class ModelData
        {
            public ModelHeader Header;
            public List<ModelDataEntry> Submeshes;
        };
    }
}