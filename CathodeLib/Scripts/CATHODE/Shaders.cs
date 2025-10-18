using CathodeLib;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/PRODUCTION/x/RENDERABLE/LEVEL_SHADERS_DX11.PAK & LEVEL_SHADERS_DX11_BIN.PAK & LEVEL_SHADERS_DX11_IDX_REMAP.PAK
    /// </summary>
    public class Shaders : CathodeFile
    {
        public List<Shader> Entries = new List<Shader>();

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

                    shader.Samplers = new List<StateBlock>();
                    for (int x = 0; x < samplerCount; x++)
                    {
                        shader.Samplers.Add(new StateBlock(reader));
                    }
                    shader.SamplerStageBindings = new List<int>();
                    for (int x = 0; x < samplerCount; x++)
                    {
                        shader.SamplerStageBindings.Add(reader.ReadByte());
                    }
                    shader.ParameterRemaps = new List<int>[5];
                    for (int x = 0; x < 5; x++)
                    {
                        shader.ParameterRemaps[x] = new List<int>();
                        for (int z = 0; z < parameterRemapCount[x]; z++)
                        {
                            shader.ParameterRemaps[x].Add(reader.ReadByte());
                        }
                    }
                    shader.SamplerRemaps = new List<int>();
                    for (int x = 0; x < samplerRemapCount; x++)
                    {
                        shader.SamplerRemaps.Add(reader.ReadByte());
                    }

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
            content = new List<Utilities.PAKContent>();
            for (int i = 0; i < Entries.Count; i++)
            {
                MemoryStream data = new MemoryStream();
                using (BinaryWriter writer = new BinaryWriter(data))
                {
                    writer.Write(0x7725BBA4);
                    writer.Write(0x00010024);

                    writer.Write((Int16)Entries[i].Samplers.Count);
                    for (int x = 0; x < 5; x++)
                        writer.Write(Entries[i].ParameterRemaps[x].Count);
                    writer.Write((Int16)Entries[i].SamplerRemaps.Count);

                    Utilities.WriteString(Entries[i].Ubershader.ToString(), writer);
                    writer.Write(new byte[40 - Entries[i].Ubershader.ToString().Length]);
                    writer.Write((Int16)Entries[i].Ubershader);
                    writer.Write(Entries[i].UbershaderFeatureFlags);
                    writer.Write(Entries[i].UbershaderRequirementFlags);
                    writer.Write((byte)Entries[i].RequiredShaderModel);
                    writer.Write((Int16)Entries[i].CycleCount);
                    writer.Write((byte)Entries[i].RegisterCount);
                    writer.Write(Entries[i].PermutationHash);
                    Entries[i].RenderStates.Write(writer);

                    for (int x = 0; x < Entries[i].Samplers.Count; x++)
                    {
                        Entries[i].Samplers[x].Write(writer);
                    }
                    for (int x = 0; x < Entries[i].Samplers.Count; x++)
                    {
                        writer.Write((byte)Entries[i].SamplerStageBindings[x]);
                    }
                    for (int x = 0; x < 5; x++)
                    {
                        for (int z = 0; z < Entries[i].ParameterRemaps[x].Count; z++)
                        {
                            writer.Write((byte)Entries[i].ParameterRemaps[x][z]);
                        }
                    }
                    for (int x = 0; x < Entries[i].SamplerRemaps.Count; x++)
                    {
                        writer.Write((byte)Entries[i].SamplerRemaps[x]);
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

            public List<StateBlock> Samplers;
            public List<int> SamplerStageBindings;
            public List<int> SamplerRemaps;
            public List<int>[] ParameterRemaps; // Count of 5

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
                public int StateId; 
                public int Value; 
            }
        }

        public enum SHADER_MODEL
        {
            SM_PLATFORM_DEFAULT = 0,
            SM_3,
            SM_4,
            SM_5,
        };

        public enum SHADER_LIST
        {
            CA_RADIOSITY_INDIRECT = 0,
            CA_RADIOSITY_INDIRECT_BOUNCE = 1,
            CA_RADIOSITY_INDIRECT_BLUR = 2,
            CA_RADIOSITY_INDIRECT_SCATTER = 3,
            CA_RADIOSITY_OBJECT_PROBE_INTERP = 4,
            CA_RADIOSITY_DIRECT_SPOT = 5,
            CA_RADIOSITY_DIRECT_SURFACE = 6,
            CA_RADIOSITY_DIRECT_STRIP = 7,
            CA_RADIOSITY_RENDER = 8,
            CA_RADIOSITY_UNMANGLE = 9,
            CA_RADIOSITY_INDIRECT_RESTORE = 10,
            CA_RADIOSITY_DOOR_TRANSFER = 11,
            CA_PARTICLE = 12,
            CA_RIBBON = 13,
            CA_DAMAGE_RENDER_LOCATIONS = 14,
            CA_DAMAGE_DILATE_LOCATIONS = 15,
            CA_DAMAGE_RENDER_DAMAGE = 16,
            CA_ENVIRONMENT = 17,
            CA_SHADOWCASTER = 18,
            CA_DECAL_ENVIRONMENT = 19,
            CA_CHARACTER = 20,
            CA_SKIN = 21,
            CA_HAIR = 22,
            CA_EYE = 23,
            CA_SKIN_OCCLUSION = 24,
            CA_VELOCITY = 25,
            CA_LIGHTPROBE = 26,
            CA_DEFERRED = 27,
            CA_DEFERRED_DEPTH = 28,
            CA_DEFERRED_CONST = 29,
            CA_DECAL = 30,
            CA_FOGPLANE = 31,
            CA_FOGSPHERE = 32,
            CA_DEBUG = 33,
            CA_EFFECT = 34,
            CA_POST_PROCESSING = 35,
            CA_MOTION_BLUR_HI_SPEC = 36,
            CA_FILTERS = 37,
            CA_LENS_FLARE = 38,
            CA_LIQUID_ENVIRONMENT = 39,
            CA_LIQUID_CHARACTER = 40,
            CA_OCCLUSION_TEST = 41,
            CA_OCCLUSION_CULLING = 42,
            CA_REFRACTION = 43,
            CA_SIMPLE_REFRACTION = 44,
            CA_DISTORTION_OVERLAY = 45,
            CA_SKYDOME = 46,
            CA_ALPHALIGHT_POSITION = 47,
            CA_ALPHALIGHT_CLEAR = 48,
            CA_ALPHALIGHT_LIGHT = 49,
            CA_SURFACE_EFFECTS = 50,
            CA_EFFECT_OVERLAY = 51,
            CA_TERRAIN = 52,
            CA_NONINTERACTIVE_WATER = 53,
            CA_SIMPLEWATER = 54,
            CA_PLANET = 55,
            CA_GALAXY = 56,
            CA_DIRECTIONAL_DEFERRED = 57,
            CA_LIGHTMAP_ENVIRONMENT = 58,
            CA_STREAMER = 59,
            CA_LOW_LOD_CHARACTER = 60,
            CA_LIGHT_DECAL = 61,
            CA_VOLUME_LIGHT = 62,
            CA_WATER_CAUSTICS_OVERLAY = 63,
            CA_SPACESUIT_VISOR = 64,
            CA_CAMERA_MAP = 65,
        };
        #endregion
    }
}