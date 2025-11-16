using CATHODE.ShaderTypes;
using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static CATHODE.RenderableElements;
using static CATHODE.Shaders;
using static CATHODE.TexturePtr;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/RENDERABLE/LEVEL_SHADERS_DX11.PAK & LEVEL_SHADERS_DX11_BIN.PAK & LEVEL_SHADERS_DX11_IDX_REMAP.PAK
    /// </summary>
    public class Shaders : CathodeFile
    {
        public List<Shader> Entries = new List<Shader>();

        private List<Shader> _writeList = new List<Shader>();

        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public Shaders(string path) : base(path) { }

        ~Shaders()
        {
            Entries.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream) 
        {
            //NOTE: Loading via byte[] or MemoryStream is not currently supported. Must be loaded via disk from a filepath!
            if (_filepath == "")
                return false;

            string filepathBIN = _filepath.Substring(0, _filepath.Length - 4) + "_BIN.PAK";
            if (!File.Exists(filepathBIN)) return false;

            List<byte[]> VertexShaders = new List<byte[]>();
            List<byte[]> PixelShaders = new List<byte[]>();
            List<byte[]> HullShaders = new List<byte[]>();
            List<byte[]> DomainShaders = new List<byte[]>();
            List<byte[]> GeometryShaders = new List<byte[]>();
            List<byte[]> ComputeShaders = new List<byte[]>();

            //This is all the raw shader data
            List<Utilities.PAKContent> content = Utilities.ReadPAK(filepathBIN, FileIdentifiers.SHADER_DATA);
            {
                int[] counts = new int[6];
                using (BinaryReader reader = new BinaryReader(new MemoryStream(content[0].Data)))
                    for (int i = 0; i < 6; i++)
                        counts[i] = reader.ReadInt32();
                int z = 1;
                for (int i = 0; i < 6; i++)
                {
                    for (int x = 0; x < counts[i]; x++)
                    {
                        if (content[z].BinIndex != z) return false;
                        switch (i)
                        {
                            case 0:
                                VertexShaders.Add(content[z].Data);
                                break;
                            case 1:
                                PixelShaders.Add(content[z].Data);
                                break;
                            case 2:
                                HullShaders.Add(content[z].Data);
                                break;
                            case 3:
                                DomainShaders.Add(content[z].Data);
                                break;
                            case 4:
                                GeometryShaders.Add(content[z].Data);
                                break;
                            case 5:
                                ComputeShaders.Add(content[z].Data);
                                break;
                        }
                        z++;
                    }
                }
            }

            //This is additional metadata
            content = Utilities.ReadPAK(_filepath, FileIdentifiers.SHADER_DATA);
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i].BinIndex != i) return false;

                using (BinaryReader reader = new BinaryReader(new MemoryStream(content[i].Data)))
                {
                    reader.BaseStream.Position = 8;

                    Shader shader = new Shader();
                    int samplerCount = reader.ReadInt16(); //max 16
                    int[] parameterRemapCount = Utilities.ConsumeArray<int>(reader, 5);
                    int samplerRemapCount = reader.ReadInt16();

                    reader.BaseStream.Position += 40; 
                    shader.Ubershader = (SHADER_LIST)reader.ReadInt16();
                    shader.UbershaderFeatureFlags = reader.ReadInt64();
                    shader.UbershaderRequirementFlags = reader.ReadInt64();
                    shader.RequiredShaderModel = (SHADER_MODEL)reader.ReadByte();
                    shader.CycleCount = reader.ReadInt16();
                    shader.RegisterCount = reader.ReadByte();
                    shader.PermutationHash = reader.ReadInt32();
                    shader.RenderStates = new StateBlock(reader);

                    for (int x = 0; x < samplerCount; x++)
                        shader.Samplers.Add(new StateBlock(reader));
                    for (int x = 0; x < samplerCount; x++)
                        shader.SamplerStageBindings.Add(reader.ReadByte());
                    for (int x = 0; x < parameterRemapCount[0]; x++)
                        shader.EngineParameterRemaps.Add(reader.ReadByte());
                    for (int x = 0; x < parameterRemapCount[1]; x++)
                        shader.VertexShaderParameterRemaps.Add(reader.ReadByte());
                    for (int x = 0; x < parameterRemapCount[2]; x++)
                        shader.PixelShaderParameterRemaps.Add(reader.ReadByte());
                    for (int x = 0; x < parameterRemapCount[3]; x++)
                        shader.HullShaderParameterRemaps.Add(reader.ReadByte());
                    for (int x = 0; x < parameterRemapCount[4]; x++)
                        shader.DomainShaderParameterRemaps.Add(reader.ReadByte());
                    for (int x = 0; x < samplerRemapCount; x++)
                        shader.SamplerRemaps.Add(reader.ReadByte());

                    int vertexShaderIdx = reader.ReadInt32();
                    shader.VertexShader = vertexShaderIdx == -1 ? null : VertexShaders[vertexShaderIdx];
                    int pixelShaderIdx = reader.ReadInt32();
                    shader.PixelShader = pixelShaderIdx == -1 ? null : PixelShaders[pixelShaderIdx];
                    int hullShaderIdx = reader.ReadInt32();
                    shader.HullShader = hullShaderIdx == -1 ? null : HullShaders[hullShaderIdx];
                    int domainShaderIdx = reader.ReadInt32();
                    shader.DomainShader = domainShaderIdx == -1 ? null : DomainShaders[domainShaderIdx];
                    int geometryShaderIdx = reader.ReadInt32();
                    shader.GeometryShader = geometryShaderIdx == -1 ? null : GeometryShaders[geometryShaderIdx];
                    int computeShaderIdx = reader.ReadInt32();
                    shader.ComputeShader = computeShaderIdx == -1 ? null : ComputeShaders[computeShaderIdx];

                    Entries.Add(shader);
                }
            }

            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            string filepathBIN = _filepath.Substring(0, _filepath.Length - 4) + "_BIN.PAK";
            string filepathIDX = _filepath.Substring(0, _filepath.Length - 4) + "_IDX_REMAP.PAK";

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
            Utilities.WritePAK(filepathBIN, FileIdentifiers.SHADER_DATA, content);

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
            Utilities.WritePAK(filepathIDX, FileIdentifiers.SHADER_DATA, content);

            //Write out metadata
            byte[][] shaderBuffers = new byte[Entries.Count][];
            Parallel.For(0, Entries.Count, i =>
            {
                shaderBuffers[i] = SerializeShaderMetadata(Entries[i], i, VertexShaders, PixelShaders, HullShaders, DomainShaders, GeometryShaders, ComputeShaders);
            });
            content = new List<Utilities.PAKContent>();
            for (int i = 0; i < shaderBuffers.Length; i++)
            {
                content.Add(new Utilities.PAKContent()
                {
                    BinIndex = i,
                    Data = shaderBuffers[i]
                });
            }
            Utilities.WritePAK(_filepath, FileIdentifiers.SHADER_DATA, content);

            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }

        private byte[] SerializeShaderMetadata(Shader shader, int index, List<byte[]> vertexShaders, List<byte[]> pixelShaders, List<byte[]> hullShaders, List<byte[]> domainShaders, List<byte[]> geometryShaders, List<byte[]> computeShaders)
        {
            using (MemoryStream data = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(data))
            {
                writer.Write(0x7725BBA4);
                writer.Write(0x00010024);

                writer.Write((Int16)shader.Samplers.Count);
                writer.Write(shader.EngineParameterRemaps.Count);
                writer.Write(shader.VertexShaderParameterRemaps.Count);
                writer.Write(shader.PixelShaderParameterRemaps.Count);
                writer.Write(shader.HullShaderParameterRemaps.Count);
                writer.Write(shader.DomainShaderParameterRemaps.Count);
                writer.Write((Int16)shader.SamplerRemaps.Count);

                Utilities.WriteString(shader.Ubershader.ToString(), writer);
                writer.Write(new byte[40 - shader.Ubershader.ToString().Length]);
                writer.Write((Int16)shader.Ubershader);
                writer.Write(shader.UbershaderFeatureFlags);
                writer.Write(shader.UbershaderRequirementFlags);
                writer.Write((byte)shader.RequiredShaderModel);
                writer.Write((Int16)shader.CycleCount);
                writer.Write((byte)shader.RegisterCount);
                writer.Write(shader.PermutationHash);
                shader.RenderStates.Write(writer);

                for (int x = 0; x < shader.Samplers.Count; x++)
                    shader.Samplers[x].Write(writer);
                for (int x = 0; x < shader.Samplers.Count; x++)
                    writer.Write((byte)shader.SamplerStageBindings[x]);
                for (int x = 0; x < shader.EngineParameterRemaps.Count; x++)
                    writer.Write((byte)shader.EngineParameterRemaps[x]);
                for (int x = 0; x < shader.VertexShaderParameterRemaps.Count; x++)
                    writer.Write((byte)shader.VertexShaderParameterRemaps[x]);
                for (int x = 0; x < shader.PixelShaderParameterRemaps.Count; x++)
                    writer.Write((byte)shader.PixelShaderParameterRemaps[x]);
                for (int x = 0; x < shader.HullShaderParameterRemaps.Count; x++)
                    writer.Write((byte)shader.HullShaderParameterRemaps[x]);
                for (int x = 0; x < shader.DomainShaderParameterRemaps.Count; x++)
                    writer.Write((byte)shader.DomainShaderParameterRemaps[x]);
                for (int x = 0; x < shader.SamplerRemaps.Count; x++)
                    writer.Write((byte)shader.SamplerRemaps[x]);

                writer.Write(shader.VertexShader == null ? -1 : vertexShaders.IndexOf(shader.VertexShader));
                writer.Write(shader.PixelShader == null ? -1 : pixelShaders.IndexOf(shader.PixelShader));
                writer.Write(shader.HullShader == null ? -1 : hullShaders.IndexOf(shader.HullShader));
                writer.Write(shader.DomainShader == null ? -1 : domainShaders.IndexOf(shader.DomainShader));
                writer.Write(shader.GeometryShader == null ? -1 : geometryShaders.IndexOf(shader.GeometryShader));
                writer.Write(shader.ComputeShader == null ? -1 : computeShaders.IndexOf(shader.ComputeShader));

                return data.ToArray();
            }
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(Shader shader)
        {
            if (!_writeList.Contains(shader)) return -1;
            return _writeList.IndexOf(shader);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public Shader GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public Shader AddEntry(Shader shader)
        {
            if (shader == null)
                return null;

            Shader newShader = shader.Copy();
            Entries.Add(newShader);
            return newShader;
        }
        #endregion

        #region STRUCTURES

        public class Shader
        {
            public SHADER_LIST Ubershader;
            public SHADER_MODEL RequiredShaderModel;

            public long UbershaderFeatureFlags;
            public long UbershaderRequirementFlags;

            public int CycleCount;
            public int RegisterCount;
            public int PermutationHash;

            public List<StateBlock> Samplers = new List<StateBlock>();
            public List<int> SamplerStageBindings = new List<int>();
            public List<int> SamplerRemaps = new List<int>();

            public List<int> EngineParameterRemaps = new List<int>();
            public List<int> VertexShaderParameterRemaps = new List<int>();
            public List<int> PixelShaderParameterRemaps = new List<int>();
            public List<int> HullShaderParameterRemaps = new List<int>();
            public List<int> DomainShaderParameterRemaps = new List<int>();

            public StateBlock RenderStates;

            public byte[] VertexShader;
            public byte[] PixelShader;
            public byte[] HullShader;
            public byte[] DomainShader;
            public byte[] GeometryShader;
            public byte[] ComputeShader;

            ~Shader()
            {
                VertexShader = null;
                PixelShader = null;
                HullShader = null;
                DomainShader = null;
                GeometryShader = null;
                ComputeShader = null;
            }
        }

        public class StateBlock
        {
            public int Index;
            public List<Entry> Entries = new List<Entry>();

            public StateBlock() { }
            public StateBlock(BinaryReader reader) => Read(reader);

            public void Read(BinaryReader reader)
            {
                int count = reader.ReadByte();
                Index = reader.ReadByte();
                for (int x = 0; x < count; x++)
                    Entries.Add(new Entry { StateId = reader.ReadInt16(), Value = reader.ReadInt32() });
            }

            public void Write(BinaryWriter writer)
            {
                writer.Write((byte)Entries.Count);
                writer.Write((byte)Index);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write((Int16)Entries[i].StateId);
                    writer.Write(Entries[i].Value);
                }
            }

            public class Entry
            {
                public int StateId; //this is RenderState or SamplerState depending on state use
                public int Value; 
            }
        }

        public enum SamplerState
        {
            AddressU,
            AddressV,
            AddressW,
            BorderColor,
            MagFilter,
            MinFilter,
            MipFilter,
            MipmapLodBias,
            MaxMipLevel,
            MaxAnisotrophy,
            MagFilterZ,
            MinFilterZ,
            SeparateZFilterEnable,
            MinMipLevel,
            TrilinearThreshold,
            AnisotrophyBias,
            HGradientExpBias,
            VGradientExpBias,
            WhiteBorderColor,
            PointBorderEnable,
            SRGBTexture,
        };

        public enum RenderState
        {
            ZEnable,
            ZFunc,
            ZWriteEnable,
            FillMode,
            CullMode,
            AlphaBlendEnable,
            SeparateAlphaBlendEnable,
            BlendFactor,
            SrcBlend,
            DestBlend,
            AlphaTestEnable,
            AlphaRef,
            AlphaFunc,
            DepthBias,
            SlopeScaleDepthBias,
            PointSpriteEnable,
            SrcBlendAlpha,
            DestBlendAlpha,
            PointSize,
            ScissorTestEnable,
            BlendOp,
            ColorWriteEnable,
            ColorWriteEnable1,
            ColorWriteEnable2,
            ColorWriteEnable3,
            StencilRef,
            StencilEnable,
            StencilFunc,
            StencilWriteMask,
            StencilTestMask,
            StencilPass,
            StencilFail,
            StencilZFail,
            ClipPlaneEnable,
            CCW_StencilFunc,
            CCW_StencilPass,
            CCW_StencilRef,
            CCW_StencilWriteMask,
            HiZEnable,
            HalfPixelOffset,
            HighPrecisionBlendEnable,
            LineWidth,
            PresentImmediateThreshold,
            PrimitiveResetEnable,
            PrimitiveResetIndex,
            ViewportEnable,
            ShadeMode,
            FogEnable,
            SRGBWriteEnable,
            BlendEnableMRT,
            DitherEnable,
            BlendOpAlpha,
        };

        public enum SHADER_MODEL
        {
            PLATFORM_DEFAULT = 0,
            SHADER_MODEL_3,
            SHADER_MODEL_4,
            SHADER_MODEL_5,
        };
        #endregion
    }
}