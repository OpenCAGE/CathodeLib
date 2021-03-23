using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestProject.File_Handlers.Shaders
{
    class ShadersBIN
    {
        public static alien_shader_bin_pak Load(string FullFilePath)
        {
            alien_shader_bin_pak Result = new alien_shader_bin_pak();

            Result.PAK = File_Handlers.PAK.PAK.Load(FullFilePath, false);
            Result.DSOs = new List<dso_file>(Result.PAK.Header.EntryCount);

            BinaryReader HeaderStream = new BinaryReader(new MemoryStream(Result.PAK.DataStart));
            int TotalVertexShaderCount = HeaderStream.ReadInt32();
            int TotalPixelShaderCount = HeaderStream.ReadInt32();
            int TotalHullShaderCount = HeaderStream.ReadInt32();
            int TotalDomainShaderCount = HeaderStream.ReadInt32();
            Result.VertexShaders = new List<dso_file>(TotalVertexShaderCount);
            Result.PixelShaders = new List<dso_file>(TotalPixelShaderCount);

            for (int EntryIndex = 0; EntryIndex < Result.PAK.Header.EntryCount; EntryIndex++)
            {
                BinaryReader Stream = new BinaryReader(new MemoryStream(Result.PAK.EntryDatas[EntryIndex]));

                dso_file DSO = new dso_file();
                DSO.Header = Utilities.Consume<dso_header>(Stream);
                DSO.ChunkOffsets = Utilities.ConsumeArray<int>(Stream, DSO.Header.ChunkCount);

                for (int ChunkIndex = 0; ChunkIndex < DSO.Header.ChunkCount; ++ChunkIndex)
                {
                    Stream.BaseStream.Position = DSO.ChunkOffsets[ChunkIndex];
                    byte[] ChunkBuffer = Stream.ReadBytes((int)Stream.BaseStream.Length - (int)Stream.BaseStream.Position);
                    BinaryReader Stream2 = new BinaryReader(new MemoryStream(ChunkBuffer));

                    dso_chunk_header ChunkHeader = Utilities.Consume<dso_chunk_header>(Stream2);
                    ChunkBuffer = Stream.ReadBytes((int)Stream.BaseStream.Length - (int)Stream.BaseStream.Position);

                    switch (new string(ChunkHeader.FourCC.V))
                    {
                        case "RDEF":
                            {
                                DSO.RDEF = Utilities.Consume<dso_chunk_rdef>(Stream2);
                                DSO.RDEF_RD11 = Utilities.Consume<dso_chunk_rdef_rd11>(Stream2);

                                Stream2.BaseStream.Position = DSO.RDEF.ConstantBufferOffset;
                                DSO.ConstantBuffers = Utilities.ConsumeArray<dso_constant_buffer>(Stream2, DSO.RDEF.ConstantBufferCount);
                                DSO.ConstantBufferNames = new List<string>(DSO.RDEF.ConstantBufferCount);
                                for (int BufferIndex = 0; BufferIndex < DSO.RDEF.ConstantBufferCount; ++BufferIndex)
                                {
                                    dso_constant_buffer ConstantBuffer = DSO.ConstantBuffers[BufferIndex];
                                    string BufferName = Utilities.ReadString(ChunkBuffer, ConstantBuffer.NameStringOffset);
                                    DSO.ConstantBufferNames.Add(BufferName);
                                }

                                Stream2.BaseStream.Position = DSO.RDEF.ResourceBindingOffset;
                                DSO.ResourceBindings = Utilities.ConsumeArray<dso_resource_binding>(Stream2, DSO.RDEF.ResourceBindingCount);
                                DSO.ResourceBindingNames = new List<string>(DSO.RDEF.ResourceBindingCount);
                                for (int BindingIndex = 0; BindingIndex < DSO.RDEF.ResourceBindingCount; ++BindingIndex)
                                {
                                    dso_resource_binding Binding = DSO.ResourceBindings[BindingIndex];
                                    string Name = Utilities.ReadString(ChunkBuffer, Binding.NameStringOffset);
                                    DSO.ResourceBindingNames.Add(Name);
                                }

                                if ((int)DSO.RDEF.ProgramType == 0xFFFE)
                                {
                                    Result.VertexShaders.Add(DSO);
                                }
                                else if ((int)DSO.RDEF.ProgramType == 0xFFFF)
                                {
                                    Result.PixelShaders.Add(DSO);
                                }
                            }
                            break;

                        case "ISGN":
                            {
                                DSO.ISGN = Utilities.Consume<dso_chunk_isgn>(Stream2);
                                DSO.InputSignatures = Utilities.ConsumeArray<dso_input_signature>(Stream2, DSO.ISGN.EntryCount);

                                for (int InputIndex = 0; InputIndex < DSO.ISGN.EntryCount; ++InputIndex)
                                {
                                    dso_input_signature Input = DSO.InputSignatures[InputIndex];
                                }

                                DSO.InputSignatureNames = new List<string>(DSO.ISGN.EntryCount);
                                for (int InputIndex = 0; InputIndex < DSO.ISGN.EntryCount; ++InputIndex)
                                {
                                    dso_input_signature Input = DSO.InputSignatures[InputIndex];
                                    string Name = Utilities.ReadString(ChunkBuffer, Input.NameStringOffset);
                                    DSO.InputSignatureNames.Add(Name);
                                }
                            }
                            break;

                        case "PCSG":
                            {

                            }
                            break;

                        case "OSGN":
                            {

                            }
                            break;

                        case "SHEX":
                            {

                            }
                            break;

                        case "STAT":
                            {

                            }
                            break;

                        default:
                            {

                            }
                            break;
                    }
                }
                Result.DSOs.Add(DSO);
            }

            return Result;
        }
    }
}

struct dso_chunk_header
{
    public fourcc FourCC;
    public int SizeInBytes;
};

struct dso_header
{
    public fourcc FourCC;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public int[] Checksum; //4
    public int Unknown0_; // NOTE: Always 1?
    public int TotalSizeInBytes;
    public int ChunkCount;
};

struct dso_chunk_rdef
{
    public int ConstantBufferCount;
    public int ConstantBufferOffset;
    public int ResourceBindingCount;
    public int ResourceBindingOffset;
    public byte MinorVersion;
    public byte MajorVersion;
    public Int16 ProgramType;
    public int Flags;
    public int CreatorStringOffset;
};

struct dso_chunk_rdef_rd11
{
    public fourcc FourCC;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public int[] Unknown0_; //6
    public int InterfaceSlotCount;
};

struct dso_constant_buffer
{
    public int NameStringOffset;
    public int VariableCount;
    public int VariablesOffset;
    public int SizeInBytes;
    public int Flags;
    public int Type;
};

struct dso_variable
{
    public int NameStringOffset;
    public int DataOffset;
    public int DataSize;
    public int Flags;
    public int TypeOffset;
    public int DefaultValueOffset;
    // TODO: I think the version 5 is different than 4. This is not there on Version 4.
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public int[] Unknown_; //4
};

struct dso_variable_type
{
    public Int16 Class;
    public Int16 Type;
    public Int16 RowCount;
    public Int16 ColumnCount;
    public Int16 ArrayCount;
    public Int16 MemberCount;
    public Int16 MembersOffset;
    // TODO: I think the version 5 is different than 4. This is not there on Version 4.
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
    public Int16[] Unknown_; //9
    public int NameStringOffset;
};

struct dso_resource_binding
{
    public int NameStringOffset;
    public int ShaderInputType;
    public int ResourceReturnType;
    public int ResourceViewDimension;
    public int SampleCount;
    public int BindPoint;
    public int BindCount;
    public int ShaderInputFlags;
};

struct dso_chunk_isgn
{
    public int EntryCount;
    public int Unknown0_;
};

struct dso_input_signature
{
    public int NameStringOffset;
    public int SemanticIndex;
    public int SystemValueType;
    public int ComponentType;
    public int Register;
    public byte Mask; // NOTE: Bitmask, each element means one vector element. 0 -> x, 1 -> y and so forth.
    public byte ReadWriteMask; // NOTE: Same as above, but it is possible that not all elements are used by the shader.
    public Int16 Unknown0_; // NOTE: Seems unused.
};

struct dso_file
{
    public dso_header Header;
    public List<int> ChunkOffsets;

    public dso_chunk_rdef RDEF;
    public dso_chunk_rdef_rd11 RDEF_RD11;

    public List<string> ConstantBufferNames;
    public List<dso_constant_buffer> ConstantBuffers;

    public List<string> ResourceBindingNames;
    public List<dso_resource_binding> ResourceBindings;

    public dso_chunk_isgn ISGN;

    public List<string> InputSignatureNames;
    public List<dso_input_signature> InputSignatures;
};

struct alien_shader_bin_pak
{
    public alien_pak PAK;

    public List<dso_file> DSOs;
    public List<dso_file> VertexShaders;
    public List<dso_file> PixelShaders;
};