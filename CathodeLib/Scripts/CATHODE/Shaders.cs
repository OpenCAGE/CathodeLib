using CathodeLib;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static CATHODE.LEGACY.ShadersPAK;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/RENDERABLE/LEVEL_SHADERS_DX11.PAK & LEVEL_SHADERS_DX11_BIN.PAK & LEVEL_SHADERS_DX11_IDX_REMAP.PAK */
    public class Shaders : CathodeFile
    {
        public List<Shader> Entries = new List<Shader>();
        public static new Implementation Implementation = Implementation.NONE;
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

            int vertexShaderCount = 0;
            int pixelShaderCount = 0;
            int hullShaderCount = 0;
            int domainShaderCount = 0;
            int geometryShaderCount = 0;
            int computeShaderCount = 0;

            List<Utilities.PAKContent> content = Utilities.ReadPAK(_filepathBIN, FileIdentifiers.SHADER_DATA);
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i].BinIndex != i) return false;

                using (BinaryReader reader = new BinaryReader(new MemoryStream(content[i].Data)))
                {
                    //The first entry here acts as an additional header
                    if (i == 0)
                    {
                        vertexShaderCount = reader.ReadInt32();
                        pixelShaderCount = reader.ReadInt32();
                        hullShaderCount = reader.ReadInt32();
                        domainShaderCount = reader.ReadInt32();
                        geometryShaderCount = reader.ReadInt32();
                        computeShaderCount = reader.ReadInt32();
                        continue;
                    }

                    fourcc fourcc = Utilities.Consume<fourcc>(reader);
                    if (fourcc.ToString() != "DXBC")
                    {
                        throw new Exception("Unexpected");
                    }

                    int[] checksums = Utilities.ConsumeArray<int>(reader, 4);

                    int one = reader.ReadInt32();
                    if (one != 1)
                    {
                        throw new Exception("Unexpected");
                    }

                    int size = reader.ReadInt32();
                    int chunkCount = reader.ReadInt32();
                    int[] chunkOffsets = Utilities.ConsumeArray<int>(reader, chunkCount);

                    for (int x = 0; x < chunkCount; x++)
                    {
                        if (reader.BaseStream.Position != chunkOffsets[x])
                        {
                            string sdffsd = "";
                        }
                        reader.BaseStream.Position = chunkOffsets[x];

                        fourcc chunkFourcc = Utilities.Consume<fourcc>(reader);
                        int chunkSize = reader.ReadInt32();

                        //TODO: should actually parse this eventually
                        byte[] chunkContent = reader.ReadBytes(chunkSize);
                        using (BinaryReader chunkReader = new BinaryReader(new MemoryStream(chunkContent)))
                        {
                            switch (chunkFourcc.ToString())
                            {
                                case "RDEF":
                                    {
                                        int constantBufferCount = chunkReader.ReadInt32();
                                        int constantBufferOffset = chunkReader.ReadInt32();
                                        int resourceBindingCount = chunkReader.ReadInt32();
                                        int resourceBindingOffset = chunkReader.ReadInt32();

                                        chunkReader.BaseStream.Position += 2; //0, 5

                                        int type = chunkReader.ReadInt16();
                                        if (type != -2 && type != -1)
                                        {
                                            //Console.WriteLine(type);
                                            string sdffd = "";
                                        }
                                        else
                                        {
                                            string sfgsdfsf = "";
                                        }
                                        //DXBCType programType = (DXBCType)chunkReader.ReadInt32();

                                        int flags = chunkReader.ReadInt32();
                                        int creatorStringOffset = chunkReader.ReadInt32();

                                        chunkReader.BaseStream.Position += 28; //RD11, 60, 24, 32, 40, 36, 12

                                        int interfaceSlotCount = chunkReader.ReadInt32();

                                        if (resourceBindingCount != 0 && chunkReader.BaseStream.Position != resourceBindingOffset)
                                            throw new Exception("Unexpected");

                                        int[] resourceNameOffsets = new int[resourceBindingCount];
                                        for (int z = 0; z < resourceBindingCount; z++)
                                        {
                                            resourceNameOffsets[z] = chunkReader.ReadInt32();
                                            int ShaderInputType = chunkReader.ReadInt32();
                                            int ResourceReturnType = chunkReader.ReadInt32();
                                            int ResourceViewDimension = chunkReader.ReadInt32();
                                            int SampleCount = chunkReader.ReadInt32();
                                            int BindPoint = chunkReader.ReadInt32();
                                            int BindCount = chunkReader.ReadInt32();
                                            int ShaderInputFlags = chunkReader.ReadInt32();
                                        }
                                        for (int z = 0; z < resourceBindingCount; z++)
                                        {
                                            if (chunkReader.BaseStream.Position != resourceNameOffsets[z])
                                                throw new Exception("Unexpected");

                                            string name = Utilities.ReadString(chunkReader);
                                            //Console.WriteLine("Resource Binding Name: " + name);
                                        }

                                        Utilities.Align(chunkReader);

                                        //chunkReader.BaseStream.Position = constantBufferOffset;
                                        if (constantBufferCount != 0 && chunkReader.BaseStream.Position != constantBufferOffset)
                                            throw new Exception("Unexpected");

                                        int[] cbNameOffsets = new int[constantBufferCount];
                                        int[] cbVariableCounts = new int[constantBufferCount];
                                        int[] cbVariableOffsets = new int[constantBufferCount];
                                        int[][] cbVariableNameOffsets = new int[constantBufferCount][];
                                        for (int z = 0; z < constantBufferCount; z++)
                                        {
                                            cbNameOffsets[z] = chunkReader.ReadInt32();
                                            cbVariableCounts[z] = chunkReader.ReadInt32();
                                            cbVariableOffsets[z] = chunkReader.ReadInt32();
                                            int SizeInBytes = chunkReader.ReadInt32();
                                            int Flags = chunkReader.ReadInt32();
                                            int Type = chunkReader.ReadInt32();

                                            if (Type != 0 || Flags != 0)
                                            {
                                                string sdfsdf = "";
                                            }
                                        }
                                        for (int z = 0; z < constantBufferCount; z++)
                                        {
                                            //string name = Utilities.ReadString(chunkReader, cbNameOffsets[z]);
                                            //Console.WriteLine("Constant Bufffer Name: " + name);

                                            chunkReader.BaseStream.Position = cbVariableOffsets[z];
                                            //if (chunkReader.BaseStream.Position != cbVariableOffsets[z])
                                            //    throw new Exception("Unexpected");

                                            cbVariableNameOffsets[z] = new int[cbVariableCounts[z]];
                                            int[] typeOffsets = new int[cbVariableCounts[z]];
                                            for (int p = 0; p < cbVariableCounts[z]; p++)
                                            {
                                                cbVariableNameOffsets[z][p] = chunkReader.ReadInt32();
                                                int DataOffset = chunkReader.ReadInt32();
                                                int DataSize = chunkReader.ReadInt32();
                                                int Flags = chunkReader.ReadInt32();
                                                typeOffsets[p] = chunkReader.ReadInt32();
                                                int DefaultValueOffset = chunkReader.ReadInt32();

                                                if (DefaultValueOffset != 0)
                                                {
                                                    string sdfsdf = "";
                                                }

                                                // TODO: I think the version 5 is different than 4. This is not there on Version 4.
                                                int[] unk = Utilities.ConsumeArray<int>(chunkReader, 4);
                                            }
                                            for (int p = 0; p < cbVariableCounts[z]; p++)
                                            {
                                                chunkReader.BaseStream.Position = typeOffsets[p];
                                                //if (chunkReader.BaseStream.Position != typeOffsets[p])
                                                //    throw new Exception("Unexpected");

                                                int Class = chunkReader.ReadInt16();
                                                int Type = chunkReader.ReadInt16();
                                                int RowCount = chunkReader.ReadInt16();
                                                int ColumnCount = chunkReader.ReadInt16();
                                                int ArrayCount = chunkReader.ReadInt16();
                                                int MemberCount = chunkReader.ReadInt16();
                                                int MembersOffset = chunkReader.ReadInt16();

                                                if (MembersOffset != 0)
                                                {
                                                    string sdfsdf = "";
                                                }

                                                // TODO: I think the version 5 is different than 4. This is not there on Version 4.
                                                short[] Unknown_ = Utilities.ConsumeArray<Int16>(chunkReader, 9);
                                                int NameStringOffset = chunkReader.ReadInt32();
                                            }
                                        }
                                        break;
                                    }

                                case "PCSG":
                                case "ISGN":
                                case "OSGN":
                                    {
                                        break;
                                    }

                                case "SHEX":
                                    {
                                        chunkReader.BaseStream.Position += 2; //80

                                        dxbc_chunk_shex_type type = (dxbc_chunk_shex_type)chunkReader.ReadInt16();
                                        int count = chunkReader.ReadInt32();
                                        byte[] contentBytes = chunkReader.ReadBytes((count - 2) * 4);

                                        if (chunkReader.BaseStream.Length != chunkReader.BaseStream.Position)
                                            throw new Exception("");
                                        break;
                                    }


                                case "STAT":
                                    {
                                        dxbc_chunk_stat stat = Utilities.Consume<dxbc_chunk_stat>(chunkReader);

                                        if (chunkReader.BaseStream.Length != chunkReader.BaseStream.Position)
                                            throw new Exception("");
                                        break;
                                    }

                                default:
                                    throw new Exception("Unexpected");
                            }
                        }
                    }
                }
            }

            //I don't think we need to read this really, it's just a count.
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

            content = Utilities.ReadPAK(_filepath, FileIdentifiers.SHADER_DATA);
            for (int i = 0; i < content.Count; i++)
            {
                //Console.WriteLine(content[i].BinIndex);

                if (content[i].BinIndex != i)
                {
                    string sdfsdf = "";
                }

                using (BinaryReader reader = new BinaryReader(new MemoryStream(content[i].Data)))
                {
                    Entries.Add(new Shader());

                    reader.BaseStream.Position = 8; //0x7725BBA4, 36, 1

                    int textureCount = reader.ReadInt16();
                    int[] cstCounts = new int[5];
                    for (int x = 0; x < 5; x++) cstCounts[x] = reader.ReadInt32();
                    int textureLinkCount = reader.ReadInt16();
                    string name = Utilities.ReadString(reader.ReadBytes(40));
                    ShaderCategory category = (ShaderCategory)reader.ReadInt16();

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
                    int[] flags = new int[20];
                    for (int x = 0; x < 20; x++) flags[x] = reader.ReadByte();

                    int unk = reader.ReadInt32();

                    int entryCount = reader.ReadInt16();
                    for (int x = 0; x < entryCount; x++)
                    {
                        int unk1 = reader.ReadInt16();
                        int unk2 = reader.ReadInt32();
                    }

                    for (int x = 0; x < textureCount; x++)
                    {
                        int unk1 = reader.ReadByte();
                        int unk2 = reader.ReadByte();
                        int[] unk3 = new int[16];
                        for (int z = 0; z < 16; z++) unk3[z] = reader.ReadInt16();
                        float unk4 = reader.ReadSingle();
                        int unk5 = reader.ReadInt16();
                        float unk6 = reader.ReadSingle();
                    }

                    for (int x = 0; x < textureCount; x++)
                    {
                        int unk1 = reader.ReadByte();
                    }

                    int[][] cstLinks = new int[5][];
                    for (int x = 0; x < 5; x++)
                    {
                        cstLinks[x] = new int[cstCounts[x]];
                        for (int z = 0; z < cstCounts[x]; z++)
                        {
                            cstLinks[x][z] = reader.ReadByte();
                        }
                    }

                    int[] textureLinks = new int[textureLinkCount];
                    for (int x = 0; x < textureLinkCount; x++)
                    {
                        textureLinks[x] = reader.ReadByte();
                    }

                    int vertexShader = reader.ReadInt32();
                    int pixelShader = reader.ReadInt32();
                    int hullShader = reader.ReadInt32();
                    int domainShader = reader.ReadInt32();

                    //Interestingly, seems like neither of these are used
                    int geometryShader = reader.ReadInt32();
                    int computeShader = reader.ReadInt32();
                }
            }

            return true;
        }

        override protected bool SaveInternal()
        {
            List<Utilities.PAKContent> content = new List<Utilities.PAKContent>();
            for (int i = 0; i < Entries.Count; i++)
            {

            }
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepathBIN)))
            {

            }

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

            content = new List<Utilities.PAKContent>();
            for (int i = 0; i < Entries.Count; i++)
            {

            }
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {

            }
            return true;
        }
        #endregion

        #region STRUCTURES

        public enum DXBCType
        {
            VERTEX = -2,
            PIXEL = -1,
        }

        enum dxbc_chunk_shex_type
        {
            SHEXShader_Pixel,
            SHEXShader_Vertex,
            SHEXShader_Geometry,
            SHEXShader_Hull,
            SHEXShader_Domain,
            SHEXShader_Compute,
        };

        struct dxbc_chunk_stat
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
        };

        public class Shader
        {

        }

        public class VertexShader : Shader
        {

        }
        public class PixelShader : Shader
        {

        }
        public class HullShader : Shader
        {

        }
        public class DomainShader : Shader
        {

        }
        #endregion
    }
}