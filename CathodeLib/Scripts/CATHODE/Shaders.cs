using CathodeLib;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
using static CATHODE.EXPERIMENTAL.MissionSave;
using static CATHODE.LEGACY.ShadersPAK;
using static CATHODE.Shaders;
using static CATHODE.Shaders.Shader;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/RENDERABLE/LEVEL_SHADERS_DX11.PAK & LEVEL_SHADERS_DX11_BIN.PAK & LEVEL_SHADERS_DX11_IDX_REMAP.PAK */
    public class Shaders : CathodeFile
    {
        public List<Shader> Entries = new List<Shader>();

        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public Shaders(string path) : base(path) { }

        private string _filepathBIN;
        private string _filepathIDX;

        #region FILE_IO
        override protected bool LoadInternal()
        {
            string trimmed = _filepath.Substring(0, _filepath.Length - 4);
            _filepathBIN = trimmed + "_BIN.PAK";
            _filepathIDX = trimmed + "_IDX_REMAP.PAK";

            if (!File.Exists(_filepathBIN)) return false;
            if (!File.Exists(_filepathIDX)) return false;

            List<byte[]> VertexShaders = new List<byte[]>();
            List<byte[]> PixelShaders = new List<byte[]>();
            List<byte[]> HullShaders = new List<byte[]>();
            List<byte[]> DomainShaders = new List<byte[]>();
            List<byte[]> GeometryShaders = new List<byte[]>();
            List<byte[]> ComputeShaders = new List<byte[]>();

            //This is all the raw DXBC shader data
            List<Utilities.PAKContent> content = Utilities.ReadPAK(_filepathBIN, FileIdentifiers.SHADER_DATA);
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i].BinIndex != i) return false;

                using (BinaryReader reader = new BinaryReader(new MemoryStream(content[i].Data)))
                {
                    //The first entry acts as an additional header
                    if (i == 0)
                    {
                        VertexShaders.Capacity = reader.ReadInt32();
                        PixelShaders.Capacity = reader.ReadInt32();
                        HullShaders.Capacity = reader.ReadInt32();
                        DomainShaders.Capacity = reader.ReadInt32();
                        GeometryShaders.Capacity = reader.ReadInt32();
                        ComputeShaders.Capacity = reader.ReadInt32();
                        continue;
                    }

                    ShaderType type = GetTypeFromDXBC(content[i].Data);
                    switch (type)
                    {
                        case ShaderType.VERTEX:
                            VertexShaders.Add(content[i].Data);
                            break;
                        case ShaderType.PIXEL:
                            PixelShaders.Add(content[i].Data);
                            break;
                        case ShaderType.HULL:
                            HullShaders.Add(content[i].Data);
                            break;
                        case ShaderType.DOMAIN:
                            DomainShaders.Add(content[i].Data);
                            break;
                        case ShaderType.GEOMETRY:
                            GeometryShaders.Add(content[i].Data);
                            break;
                        case ShaderType.COMPUTE:
                            ComputeShaders.Add(content[i].Data);
                            break;
                    }
                }
            }

            //I don't think we need to read this really, it's just a count
            content = Utilities.ReadPAK(_filepathIDX, FileIdentifiers.SHADER_DATA);
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i].BinIndex != i) return false;

                using (BinaryReader reader = new BinaryReader(new MemoryStream(content[i].Data)))
                {
                    int index = reader.ReadInt32();
                    if (index != i) return false;
                }
            }

            //This is additional metadata
            content = Utilities.ReadPAK(_filepath, FileIdentifiers.SHADER_DATA);
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i].BinIndex != i) return false;

                using (BinaryReader reader = new BinaryReader(new MemoryStream(content[i].Data)))
                {
                    Shader shader = new Shader();

                    reader.BaseStream.Position = 8; //0x7725BBA4, 36, 1

                    int textureCount = reader.ReadInt16();
                    int[] cstCounts = Utilities.ConsumeArray<int>(reader, 5);
                    int textureLinkCount = reader.ReadInt16();
                    reader.BaseStream.Position += 40; //String version of Category
                    shader.Category = (ShaderCategory)reader.ReadInt16();

                    // Index 0: 0x04: seems to be metal. 0x01: seems to be non-metal. But it has many exceptions so maybe check renderdoc.
                    // Index 1: ParallaxMap? It seems to be either 0x30 and 0x80 for the most part, sometimes it combines both into 0xB0.
                    // Index 2: Lower part of this byte is NormalMap0 related! 0x04 means it has normal map 0. No hits for 1, 2 or 8 yet.
                    //          0x03 is a thing, but not normal map. What is it? Can be 0x07 where it has normal map and both the 0x03 thing.
                    //          0x0C is also a thing (8 and 4), so it has normal map and something else?
                    //          I'm gonna say 0x03 means DiffuseMap1.
                    // Index 3: 0x04 Not OcclusionTint. (NormalMap0 + NormalMap0UVMultiplier + NormalMap0Strength?)
                    // Index 4: Seems to tell me about AO. If 4, then it has AO tint. If 1 it has AO texture.
                    // Index 6: Seems to tell me about Dirt/OpacityNoise.
                    //      0x19 (0001 1001): has opacity noise and dirt maps.
                    //      0x38 (0011 1000): has dirt map.
                    //      0x39 (0011 1001): has opacity noise and dirt maps.
                    // Index 10: First half, only found 4, and it seems to be something included in all (Diffuse0 as well?).
                    // Seems like the least significant bit enables/disables opacity noise?
                    for (int x = 0; x < 20; x++) 
                        shader.Flags[x] = reader.ReadByte();

                    shader.Unknown1 = reader.ReadInt32();

                    int entryCount = reader.ReadInt16();
                    for (int x = 0; x < entryCount; x++)
                    {
                        shader.Unknown2.Add(new Shader.UnknownPair()
                        {
                            unk1 = reader.ReadInt16(),
                            unk2 = reader.ReadInt32()
                        });
                    }

                    for (int x = 0; x < textureCount; x++)
                    {
                        UnknownTextureThing unk = new UnknownTextureThing();
                        unk.unk1 = reader.ReadByte();
                        unk.unk2 = reader.ReadByte();
                        unk.unk3 = new short[16];
                        for (int z = 0; z < 16; z++)
                            unk.unk3[z] = reader.ReadInt16();
                        unk.unk4 = reader.ReadSingle();
                        unk.unk5 = reader.ReadInt16();
                        unk.unk6 = reader.ReadSingle();
                        shader.Unknown3.Add(unk);
                    }
                    for (int x = 0; x < textureCount; x++)
                    {
                        shader.Unknown3[x].unk7 = reader.ReadByte();
                    }

                    shader.CSTLinks = new int[5][];
                    for (int x = 0; x < 5; x++)
                    {
                        shader.CSTLinks[x] = new int[cstCounts[x]];
                        for (int z = 0; z < cstCounts[x]; z++)
                        {
                            shader.CSTLinks[x][z] = reader.ReadByte();
                        }
                    }

                    shader.TextureLinks = new int[textureLinkCount];
                    for (int x = 0; x < textureLinkCount; x++)
                    {
                        shader.TextureLinks[x] = reader.ReadByte();
                    }

                    int vertexShaderIdx = reader.ReadInt32();
                    int pixelShaderIdx = reader.ReadInt32();
                    int hullShaderIdx = reader.ReadInt32();
                    int domainShaderIdx = reader.ReadInt32();
                    int geometryShaderIdx = reader.ReadInt32();
                    int computeShaderIdx = reader.ReadInt32();

                    shader.VertexShader = vertexShaderIdx == -1 ? null : VertexShaders[vertexShaderIdx];
                    shader.PixelShader = pixelShaderIdx == -1 ? null : PixelShaders[pixelShaderIdx];
                    shader.HullShader = hullShaderIdx == -1 ? null : HullShaders[hullShaderIdx];
                    shader.DomainShader = domainShaderIdx == -1 ? null : DomainShaders[domainShaderIdx];
                    shader.GeometryShader = geometryShaderIdx == -1 ? null : GeometryShaders[geometryShaderIdx];
                    shader.ComputeShader = computeShaderIdx == -1 ? null : ComputeShaders[computeShaderIdx];

                    int pos = (int)reader.BaseStream.Position;
                    int len = (int)reader.BaseStream.Length;

                    Entries.Add(shader);
                }
            }

            return true;
        }

        override protected bool SaveInternal()
        {
            //Compile all shader data
            List<byte[]> VertexShaders = new List<byte[]>();
            List<byte[]> PixelShaders = new List<byte[]>();
            List<byte[]> HullShaders = new List<byte[]>();
            List<byte[]> DomainShaders = new List<byte[]>();
            List<byte[]> GeometryShaders = new List<byte[]>();
            List<byte[]> ComputeShaders = new List<byte[]>();
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].VertexShader != null && !VertexShaders.Contains(Entries[i].VertexShader))
                    VertexShaders.Add(Entries[i].VertexShader);
                if (Entries[i].PixelShader != null && !PixelShaders.Contains(Entries[i].PixelShader))
                    PixelShaders.Add(Entries[i].PixelShader);
                if (Entries[i].HullShader != null && !HullShaders.Contains(Entries[i].HullShader))
                    HullShaders.Add(Entries[i].HullShader);
                if (Entries[i].DomainShader != null && !DomainShaders.Contains(Entries[i].DomainShader))
                    DomainShaders.Add(Entries[i].DomainShader);
                if (Entries[i].GeometryShader != null && !GeometryShaders.Contains(Entries[i].GeometryShader))
                    GeometryShaders.Add(Entries[i].GeometryShader);
                if (Entries[i].ComputeShader != null && !ComputeShaders.Contains(Entries[i].ComputeShader))
                    ComputeShaders.Add(Entries[i].ComputeShader);
            }

            //Write out all shader data
            List<Utilities.PAKContent> content = new List<Utilities.PAKContent>();
            {
                List<byte> bytes = new List<byte>();
                bytes.AddRange(BitConverter.GetBytes(VertexShaders.Count));
                bytes.AddRange(BitConverter.GetBytes(PixelShaders.Count));
                bytes.AddRange(BitConverter.GetBytes(HullShaders.Count));
                bytes.AddRange(BitConverter.GetBytes(DomainShaders.Count));
                bytes.AddRange(BitConverter.GetBytes(GeometryShaders.Count));
                bytes.AddRange(BitConverter.GetBytes(ComputeShaders.Count));
                content.Add(new Utilities.PAKContent() { Data = bytes.ToArray() });
            }
            List<byte[]> AllShaders = new List<byte[]>();
            AllShaders.AddRange(VertexShaders);
            AllShaders.AddRange(PixelShaders);
            AllShaders.AddRange(HullShaders);
            AllShaders.AddRange(DomainShaders);
            AllShaders.AddRange(GeometryShaders);
            AllShaders.AddRange(ComputeShaders);
            for (int i = 0; i < AllShaders.Count; i++)
            {
                content.Add(new Utilities.PAKContent()
                {
                    BinIndex = i + 1,
                    Data = AllShaders[i]
                });
            }
            Utilities.WritePAK(_filepathBIN, FileIdentifiers.SHADER_DATA, content);

            //Write out indexes
            content = new List<Utilities.PAKContent>();
            for (int i = 0; i < Entries.Count; i++)
            {
                content.Add(new Utilities.PAKContent()
                {
                    BinIndex = i,
                    Data = BitConverter.GetBytes((Int32)i)
                });
            }
            Utilities.WritePAK(_filepathIDX, FileIdentifiers.SHADER_DATA, content);

            //Write out metadata
            content = new List<Utilities.PAKContent>();
            for (int i = 0; i < Entries.Count; i++)
            {
                MemoryStream data = new MemoryStream();
                using (BinaryWriter writer = new BinaryWriter(data))
                {
                    writer.Write(0x7725BBA4);
                    writer.Write((Int16)36);
                    writer.Write((Int16)1);
                    writer.Write((Int16)Entries[i].Unknown3.Count);
                    for (int z = 0; z < 5; z++)
                        writer.Write(Entries[i].CSTLinks[z].Length);
                    writer.Write((Int16)Entries[i].TextureLinks.Length);
                    Utilities.WriteString(Entries[i].Category.ToString(), writer);
                    writer.Write(new byte[40 - Entries[i].Category.ToString().Length]);
                    writer.Write((Int16)Entries[i].Category);
                    for (int z = 0; z < 20; z++)
                        writer.Write((byte)Entries[i].Flags[z]);
                    writer.Write(Entries[i].Unknown1);
                    writer.Write((Int16)Entries[i].Unknown2.Count);
                    for (int z = 0; z < Entries[i].Unknown2.Count; z++)
                    {
                        writer.Write((Int16)Entries[i].Unknown2[z].unk1);
                        writer.Write(Entries[i].Unknown2[z].unk2);
                    }
                    for (int z = 0; z < Entries[i].Unknown3.Count; z++)
                    {
                        writer.Write((byte)Entries[i].Unknown3[z].unk1);
                        writer.Write((byte)Entries[i].Unknown3[z].unk2);
                        for (int p = 0; p < 16; p++)
                            writer.Write((Int16)Entries[i].Unknown3[z].unk3[p]);
                        writer.Write(Entries[i].Unknown3[z].unk4);
                        writer.Write((Int16)Entries[i].Unknown3[z].unk5);
                        writer.Write(Entries[i].Unknown3[z].unk6);
                    }
                    for (int z = 0; z < Entries[i].Unknown3.Count; z++)
                    {
                        writer.Write((byte)Entries[i].Unknown3[z].unk7);
                    }
                    for (int z = 0; z < 5; z++)
                    {
                        for (int p = 0; p < Entries[i].CSTLinks[z].Length; p++)
                        {
                            writer.Write((byte)Entries[i].CSTLinks[z][p]);
                        }
                    }
                    for (int z = 0; z < Entries[i].TextureLinks.Length; z++)
                    {
                        writer.Write((byte)Entries[i].TextureLinks[z]);
                    }
                    writer.Write(Entries[i].VertexShader == null ? -1 : VertexShaders.IndexOf(Entries[i].VertexShader));
                    writer.Write(Entries[i].PixelShader == null ? -1 : PixelShaders.IndexOf(Entries[i].PixelShader));
                    writer.Write(Entries[i].HullShader == null ? -1 : HullShaders.IndexOf(Entries[i].HullShader));
                    writer.Write(Entries[i].DomainShader == null ? -1 : DomainShaders.IndexOf(Entries[i].DomainShader));
                    writer.Write(Entries[i].GeometryShader == null ? -1 : GeometryShaders.IndexOf(Entries[i].GeometryShader));
                    writer.Write(Entries[i].ComputeShader == null ? -1 : ComputeShaders.IndexOf(Entries[i].ComputeShader));
                }
                content.Add(new Utilities.PAKContent()
                {
                    BinIndex = i,
                    Data = data.ToArray()
                });
            }
            Utilities.WritePAK(_filepath, FileIdentifiers.SHADER_DATA, content);
            return true;
        }
        #endregion

        private ShaderType GetTypeFromDXBC(byte[] dxbc)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(dxbc)))
            {
                reader.BaseStream.Position += 4; //DXBC
                int[] checksums = Utilities.ConsumeArray<int>(reader, 4);
                reader.BaseStream.Position += 4; //1

                int size = reader.ReadInt32();
                int chunkCount = reader.ReadInt32();
                int[] chunkOffsets = Utilities.ConsumeArray<int>(reader, chunkCount);

                for (int x = 0; x < chunkCount; x++)
                {
                    reader.BaseStream.Position = chunkOffsets[x];

                    fourcc chunkFourcc = Utilities.Consume<fourcc>(reader);
                    Console.WriteLine(chunkFourcc.ToString());
                    int chunkSize = reader.ReadInt32();
                    byte[] chunkContent = reader.ReadBytes(chunkSize);
                    using (BinaryReader chunkReader = new BinaryReader(new MemoryStream(chunkContent)))
                    {
                        switch (chunkFourcc.ToString())
                        {
                            /*
                            case "RDEF": //Resource definition. Describes constant buffers and resource bindings.
                                {
                                    int constantBufferCount = chunkReader.ReadInt32();
                                    int constantBufferOffset = chunkReader.ReadInt32();
                                    int resourceBindingCount = chunkReader.ReadInt32();
                                    int resourceBindingOffset = chunkReader.ReadInt32();

                                    chunkReader.BaseStream.Position += 2; //0, 5

                                    shader.Type = chunkReader.ReadInt16();
                                    shader.Flags = chunkReader.ReadInt32();

                                    int creatorStringOffset = chunkReader.ReadInt32();

                                    chunkReader.BaseStream.Position += 28; //RD11, 60, 24, 32, 40, 36, 12

                                    shader.InterfaceSlotCount = chunkReader.ReadInt32();

                                    chunkReader.BaseStream.Position = resourceBindingOffset;
                                    for (int z = 0; z < resourceBindingCount; z++)
                                    {
                                        int nameOffset = chunkReader.ReadInt32();
                                        shader.ResourceBindings.Add(new Shader.ResourceBinding()
                                        {
                                            Name = Utilities.ReadString(chunkReader, nameOffset),
                                            ShaderInputType = chunkReader.ReadInt32(),
                                            ResourceReturnType = chunkReader.ReadInt32(),
                                            ResourceViewDimension = chunkReader.ReadInt32(),
                                            SampleCount = chunkReader.ReadInt32(),
                                            BindPoint = chunkReader.ReadInt32(),
                                            BindCount = chunkReader.ReadInt32(),
                                            ShaderInputFlags = chunkReader.ReadInt32()
                                        });
                                    }

                                    //in consecutive order in the write: the string table is here, then byte aligned to 4

                                    chunkReader.BaseStream.Position = constantBufferOffset;
                                    int[] cbVariableCounts = new int[constantBufferCount];
                                    int[] cbVariableOffsets = new int[constantBufferCount];
                                    for (int z = 0; z < constantBufferCount; z++)
                                    {
                                        int nameOffset = chunkReader.ReadInt32();
                                        cbVariableCounts[z] = chunkReader.ReadInt32();
                                        cbVariableOffsets[z] = chunkReader.ReadInt32();
                                        int SizeInBytes = chunkReader.ReadInt32();
                                        chunkReader.BaseStream.Position += 8; //0,0

                                        shader.ConstantBuffers.Add(new Shader.ConstantBuffer()
                                        {
                                            Name = Utilities.ReadString(chunkReader, nameOffset)
                                        });
                                    }
                                    for (int z = 0; z < constantBufferCount; z++)
                                    {
                                        chunkReader.BaseStream.Position = cbVariableOffsets[z];

                                        int[] dataOffsets = new int[cbVariableCounts[z]];
                                        int[] dataLengths = new int[cbVariableCounts[z]];
                                        int[] typeOffsets = new int[cbVariableCounts[z]];
                                        for (int p = 0; p < cbVariableCounts[z]; p++)
                                        {
                                            int nameOffset = chunkReader.ReadInt32();
                                            dataOffsets[p] = chunkReader.ReadInt32();
                                            dataLengths[p] = chunkReader.ReadInt32();
                                            int flags = chunkReader.ReadInt32();
                                            typeOffsets[p] = chunkReader.ReadInt32();

                                            chunkReader.BaseStream.Position += 20; //0,-1,0,-1,0

                                            shader.ConstantBuffers[z].Variables.Add(new Shader.ConstantBuffer.Variable()
                                            {
                                                Name = Utilities.ReadString(chunkReader, nameOffset),
                                                Flags = flags
                                            });
                                        }
                                        for (int p = 0; p < cbVariableCounts[z]; p++)
                                        {
                                            chunkReader.BaseStream.Position = typeOffsets[p];

                                            int Class = chunkReader.ReadInt16();
                                            int Type = chunkReader.ReadInt16();
                                            int RowCount = chunkReader.ReadInt16();
                                            int ColumnCount = chunkReader.ReadInt16();
                                            int ArrayCount = chunkReader.ReadInt16();

                                            short[] Unknown_ = Utilities.ConsumeArray<Int16>(chunkReader, 11);
                                            int nameOffset = chunkReader.ReadInt32();

                                            shader.ConstantBuffers[z].Variables[p].TypeName = Utilities.ReadString(chunkReader, nameOffset);
                                        }
                                    }

                                    chunkReader.BaseStream.Position = creatorStringOffset;
                                    shader.Creator = Utilities.ReadString(chunkReader);
                                    break;
                                }

                            case "PCSG": //Patch constant signature
                            case "ISGN": //Input signature
                            case "OSGN": //Output signature
                                {
                                    int entryCount = chunkReader.ReadInt32();
                                    chunkReader.BaseStream.Position += 4; //8

                                    for (int p = 0; p < entryCount; p++)
                                    {
                                        int nameOffset = chunkReader.ReadInt32();
                                        int SemanticIndex = chunkReader.ReadInt32();
                                        int SystemValueType = chunkReader.ReadInt32();
                                        SignatureComponentType ComponentType = (SignatureComponentType)chunkReader.ReadInt32();

                                        int Register = chunkReader.ReadInt32();
                                        byte Mask = chunkReader.ReadByte(); // NOTE: Bitmask, each element means one vector element. 0 -> x, 1 -> y and so forth.
                                        byte ReadWriteMask = chunkReader.ReadByte(); // NOTE: Same as above, but it is possible that not all elements are used by the shader.

                                        chunkReader.BaseStream.Position += 2;
                                    }
                                    break;
                                }
                            */
                            case "SHEX": //Shader (SM5)
                                {
                                    chunkReader.BaseStream.Position += 2; //80

                                    ShaderType type = (ShaderType)chunkReader.ReadInt16();

                                    return type;

                                    int count = chunkReader.ReadInt32();
                                    byte[] contentBytes = chunkReader.ReadBytes((count - 2) * 4);

                                    if (chunkReader.BaseStream.Length != chunkReader.BaseStream.Position)
                                        throw new Exception("");
                                    break;
                                }

                            /*
                            case "STAT": //Statistics. Useful statistics about the shader, such as instruction count, declaration count, etc.
                                {
                                    shader.Stats = Utilities.Consume<Shader.STAT>(chunkReader);
                                    break;
                                }
                            */
                        }
                    }
                }
                throw new Exception("Invalid DXBC");
            }
        }

        #region STRUCTURES

        public class Shader
        {
            public ShaderCategory Category;
            public int[] Flags = new int[20];

            public int Unknown1;
            public List<UnknownPair> Unknown2 = new List<UnknownPair>();
            public List<UnknownTextureThing> Unknown3 = new List<UnknownTextureThing>();

            //TODO: what are these actually pointing to
            public int[][] CSTLinks = new int[5][]; 
            public int[] TextureLinks;

            public byte[] VertexShader;
            public byte[] PixelShader;
            public byte[] HullShader;
            public byte[] DomainShader;
            public byte[] GeometryShader;
            public byte[] ComputeShader;

            public class UnknownPair
            {
                public int unk1;
                public int unk2;
            }

            public class UnknownTextureThing
            {
                public byte unk1;
                public byte unk2;
                public Int16[] unk3; //16
                public float unk4;
                public Int16 unk5;
                public float unk6;

                public byte unk7;
            }
        }

        enum ShaderType
        {
            PIXEL,
            VERTEX,
            GEOMETRY,
            HULL,
            DOMAIN,
            COMPUTE,
        };

        /*
        public enum DXBCType
        {
            VERTEX = -2,
            PIXEL = -1,
        }

        enum SignatureComponentType
        {
            UNSIGNED_INTEGER = 1,
            SIGNED_INTEGER = 2,
            FLOATING_POINT = 3,
        }

        public class Shader
        {
            public STAT Stats;

            public string Creator;

            public int Type = 0; //DXBCType
            public int Flags = 0;

            public int InterfaceSlotCount = 0;

            public List<ResourceBinding> ResourceBindings = new List<ResourceBinding>();
            public List<ConstantBuffer> ConstantBuffers = new List<ConstantBuffer>();
        
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public class STAT
            {
                public int InstructionCount;
                public int TempRegisterCount;
                public int DefineCount;
                public int DeclarationCount;
                public int FloatInstructionCount;
                public int IntInstructionCount;
                public int UIntInstructionCount;
                public int StaticFlowControlCount;
                public int DynamicFlowControlCount;
                public int MacroInstructionCount; // Not sure.
                public int TempArrayCount;
                public int ArrayInstructionCount;
                public int CutInstructionCount;
                public int EmitInstructionCount;
                public int TextureNormalInstructionCount;
                public int TextureLoadInstructionCount;
                public int TextureComparisonInstructionCount;
                public int TextureBiasInstructionCount;
                public int TextureGradientInstructionCount;
                public int MovInstructionCount;
                public int MovCInstructionCount;
                public int Unknown0_;
                public int InputPrimitiveForGeometryShaders;
                public int PrimitiveTopologyForGeometryShaders;
                public int MaxOutputVertexCountForGeometryShaders;

                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
                public int[] unk0; //zeros

                public int IsSampleFrequencyShader; // 1 for sample frequency shadeer, otherwise 0.

                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
                public int[] unk1;
            }

            public class ResourceBinding
            {
                public string Name;

                public int ShaderInputType;
                public int ResourceReturnType;
                public int ResourceViewDimension;
                public int SampleCount;
                public int BindPoint;
                public int BindCount;
                public int ShaderInputFlags;
            }

            public class ConstantBuffer
            {
                public string Name;

                public List<Variable> Variables = new List<Variable>();

                public class Variable
                {
                    public string Name;
                    public string TypeName;

                    public int Flags;
                }
            }
        }
        */
        #endregion
    }
}