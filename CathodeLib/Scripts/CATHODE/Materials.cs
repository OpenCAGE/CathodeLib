using CathodeLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using static CATHODE.Models;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/PRODUCTION/x/RENDERABLE/LEVEL_MODELS.MTL & LEVEL_MODELS.CST
    /// </summary>
    public class Materials : CathodeFile
    {
        public List<Material> Entries = new List<Material>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public Materials(string path) : base(path) { }

        private List<Material> _writeList = new List<Material>();

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            //NOTE: Loading via byte[] or MemoryStream is not currently supported. Must be loaded via disk from a filepath!
            if (_filepath == "")
                return false;

            string filepathCST = _filepath.Substring(0, _filepath.Length - 3) + "CST";
            if (!File.Exists(filepathCST)) return false;

            using (BinaryReader readerCST = new BinaryReader(File.OpenRead(filepathCST)))
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 8;
                int materialOffset = reader.ReadInt32();

                //base CST offsets
                int engineConstantsOffset = reader.ReadInt32();
                int vertexConstantsOffset = reader.ReadInt32();
                int pixelConstantsOffset = reader.ReadInt32();
                int hullConstantsOffset = reader.ReadInt32();
                int domainConstantsOffset = reader.ReadInt32();

                reader.BaseStream.Position += 8;
                int materialCount = reader.ReadInt16();
                reader.BaseStream.Position += 2;

                List<string> materialNames = new List<string>(materialCount);
                for (int i = 0; i < materialCount; ++i) materialNames.Add(Utilities.ReadString(reader));

                reader.BaseStream.Position = materialOffset + 4;
                for (int i = 0; i < materialCount; i++)
                {
                    Material material = new Material();
                    for (int x = 0; x < 12; x++)
                    {
                        TexturePtr texRef = new TexturePtr(reader);
                        if (texRef.Location == TexturePtr.Source.NONE)
                            continue;
                        material.TextureReferences.Add(texRef);
                    }
                    reader.BaseStream.Position += 8;

                    int[] ubershaderConstantOffset = Utilities.ConsumeArray<int>(reader, 5);
                    byte[] ubershaderConstantCount = Utilities.ConsumeArray<byte>(reader, 5);
                    material.EngineConstants = ReadCSTData(engineConstantsOffset + (ubershaderConstantOffset[0] * 4), ubershaderConstantCount[0], readerCST);
                    material.VertexShaderConstants = ReadCSTData(vertexConstantsOffset + (ubershaderConstantOffset[1] * 4), ubershaderConstantCount[1], readerCST);
                    material.PixelShaderConstants = ReadCSTData(pixelConstantsOffset + (ubershaderConstantOffset[2] * 4), ubershaderConstantCount[2], readerCST);
                    material.HullShaderConstants = ReadCSTData(hullConstantsOffset + (ubershaderConstantOffset[3] * 4), ubershaderConstantCount[3], readerCST);
                    material.DomainShaderConstants = ReadCSTData(domainConstantsOffset + (ubershaderConstantOffset[4] * 4), ubershaderConstantCount[4], readerCST);

                    reader.BaseStream.Position += 11;
                    material.Name = materialNames[i]; 
                    material.ShaderIndex = reader.ReadInt32();
                    reader.BaseStream.Position += 128;
                    int lightFlags = reader.ReadInt32();
                    if (lightFlags != 0) material.OfflineLightFeatures = new LightFlags(lightFlags);
                    reader.BaseStream.Position += 54;
                    material.PhysicalMaterialIndex = reader.ReadByte(); 
                    material.EnvironmentMapIndex = reader.ReadByte(); 
                    material.Priority = reader.ReadByte();


                    reader.BaseStream.Position += 11;

                    Entries.Add(material);
                    _writeList.Add(material);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            //Write constants
            int[] offsets = new int[5];
            List<int>[] matOffsets = new List<int>[5];
            List<int>[] matCounts = new List<int>[5];
            for (int i = 0; i < 5; i++)
            {
                matOffsets[i] = new List<int>();
                matCounts[i] = new List<int>();
            }
            using (BinaryWriter writerCST = new BinaryWriter(File.OpenWrite(_filepath.Substring(0, _filepath.Length - 3) + "CST")))
            {
                writerCST.BaseStream.SetLength(0);
                for (int i = 0; i < 5; i++)
                {
                    offsets[i] = (int)writerCST.BaseStream.Position;
                    
                    for (int x = 0; x < Entries.Count; x++)
                    {
                        List<float> constants = null;
                        switch (i)
                        {
                            case 0: constants = Entries[x].EngineConstants; break;
                            case 1: constants = Entries[x].VertexShaderConstants; break;
                            case 2: constants = Entries[x].PixelShaderConstants; break;
                            case 3: constants = Entries[x].HullShaderConstants; break;
                            case 4: constants = Entries[x].DomainShaderConstants; break;
                        }
                        matOffsets[i].Add(constants.Count == 0 ? 0 : ((int)writerCST.BaseStream.Position - offsets[i]) / 4);
                        matCounts[i].Add(constants.Count);
                        
                        for (int z = 0; z < constants.Count; z++)
                            writerCST.Write(constants[z]);
                    }
                }
            }

            _writeList.Clear();
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                //Write header
                writer.Write(0); //placeholder file length (-4)
                writer.Write(40);
                writer.Write(0); //placeholder material offset
                writer.Write(offsets[0]);
                writer.Write(offsets[1]);
                writer.Write(offsets[2]);
                writer.Write(offsets[3]);
                writer.Write(offsets[4]);
                writer.Write(44);
                writer.Write(0); //placeholder material name size
                writer.Write((Int16)Entries.Count);
                writer.Write((Int16)296);

                //Write material names
                int preNamesLength = (int)writer.BaseStream.Position;
                for (int i = 0; i < Entries.Count; ++i) Utilities.WriteString(Entries[i].Name, writer, true);
                int namesLength = (int)writer.BaseStream.Position - preNamesLength;
                int misalignment = 16 - (((int)writer.BaseStream.Position - 4) & 0xf);
                if (misalignment != 16)
                    writer.Write(new byte[misalignment]);

                bool isGlobal = Path.GetFileName(_filepath).ToLower() == "global_models.mtl";

                //Write material data
                int materialOffset = (int)writer.BaseStream.Position;
                int nameOffset = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < 12; x++)
                    {
                        TexturePtr tex = x >= Entries[i].TextureReferences.Count ? null : Entries[i].TextureReferences[x];
                        if (tex == null || tex.Location == TexturePtr.Source.NONE)
                        {
                            writer.Write((Int16)(-1));
                            writer.Write((Int16)(-1));
                            continue;
                        }
                        writer.Write((Int16)tex.Index);
                        writer.Write((Int16)tex.Location);
                    }

                    writer.Write(isGlobal ? 1 : 0);
                    writer.Write(i);

                    for (int x = 0; x < 5; x++)
                        writer.Write(matOffsets[x][i]);
                    for (int x = 0; x < 5; x++)
                        writer.Write((byte)matCounts[x][i]);
                    writer.Write(new byte[7]);
                    writer.Write(nameOffset);
                    nameOffset += Entries[i].Name.Length + 1;
                    writer.Write(Entries[i].ShaderIndex);
                    writer.Write(new byte[128]);
                    if (Entries[i].OfflineLightFeatures != null) 
                        Entries[i].OfflineLightFeatures.Write(writer);
                    else 
                        writer.Write(0);
                    writer.Write(new byte[49]);
                    writer.Write((byte)(Entries[i].TextureReferences.Count > 12 ? 12 : Entries[i].TextureReferences.Count));
                    writer.Write(new byte[2]);
                    writer.Write((short)-1);
                    writer.Write((byte)Entries[i].PhysicalMaterialIndex);
                    writer.Write((byte)Entries[i].EnvironmentMapIndex);
                    writer.Write((byte)Entries[i].Priority);

                    writer.Write(new byte[11]);

                    _writeList.Add(Entries[i]);
                }

                //Correct placeholders
                writer.BaseStream.Position = 0;
                writer.Write((int)writer.BaseStream.Length - 4);
                writer.BaseStream.Position = 8;
                writer.Write(materialOffset - 4);
                writer.BaseStream.Position = 36;
                writer.Write(namesLength);
            }
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the current index for a material (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(Material material)
        {
            if (!_writeList.Contains(material)) return -1;
            return _writeList.IndexOf(material);
        }

        /// <summary>
        /// Get a material by its current index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public Material GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        private List<float> ReadCSTData(int offset, int count, BinaryReader reader)
        {
            reader.BaseStream.Position = offset;
            List<float> data = new List<float>();
            for (int i = 0; i < count; i++)
                data.Add(reader.ReadSingle());
            return data;
        }
        #endregion

        #region STRUCTURES
        public class Material
        {
            public string Name;

            public List<TexturePtr> TextureReferences = new List<TexturePtr>(); //Max of 12

            public List<float> EngineConstants = new List<float>();
            public List<float> VertexShaderConstants = new List<float>();
            public List<float> PixelShaderConstants = new List<float>();
            public List<float> HullShaderConstants = new List<float>();
            public List<float> DomainShaderConstants = new List<float>();

            public LightFlags OfflineLightFeatures = null; //Null if we have none (not a light material)

            public int ShaderIndex; //Index into shader pak
            public int PhysicalMaterialIndex; //255 i assume means none -> this is an index into the Havok physics materials database (at path data/material_data/materials.xml/bml).
            public int EnvironmentMapIndex; //255 means 'any' -> this is an index into our TextureReferences array. it's optionally overridden per renderable instance by MVR.

            public int Priority; 

            public override string ToString()
            {
                return Name;
            }
        }

        public class LightFlags
        {
            //Light type
            public LightType Type
            {
                get { return (LightType)(flags & 0xFF); }
                set { flags = (flags & ~0xFF) | ((int)value & 0xFF); }
            }
            public enum LightType
            {
                Ambient = 0,
                Strip = 1,
                Point = 2,
                Spot = 3,
                Directional = 4
            };

            //Light features
            public bool SoftDiffuse
            {
                get { return ((flags >> 8) & (1 << 0)) != 0; }
                set { SetFeatureFlag(0, value); }
            }
            public bool Specular
            {
                get { return ((flags >> 8) & (1 << 1)) != 0; }
                set { SetFeatureFlag(1, value); }
            }
            public bool Shadow
            {
                get { return ((flags >> 8) & (1 << 2)) != 0; }
                set { SetFeatureFlag(2, value); }
            }
            public bool Gobo
            {
                get { return ((flags >> 8) & (1 << 3)) != 0; }
                set { SetFeatureFlag(3, value); }
            }
            public bool Animated
            {
                get { return ((flags >> 8) & (1 << 4)) != 0; }
                set { SetFeatureFlag(4, value); }
            }
            public bool LensFlare
            {
                get { return ((flags >> 8) & (1 << 5)) != 0; }
                set { SetFeatureFlag(5, value); }
            }
            public bool NoClip
            {
                get { return ((flags >> 8) & (1 << 6)) != 0; }
                set { SetFeatureFlag(6, value); }
            }
            public bool DiffuseBias
            {
                get { return ((flags >> 8) & (1 << 7)) != 0; }
                set { SetFeatureFlag(7, value); }
            }
            public bool AreaLight
            {
                get { return ((flags >> 8) & (1 << 8)) != 0; }
                set { SetFeatureFlag(8, value); }
            }
            public bool SquareLight
            {
                get { return ((flags >> 8) & (1 << 9)) != 0; }
                set { SetFeatureFlag(9, value); }
            }
            public bool Flashlight
            {
                get { return ((flags >> 8) & (1 << 10)) != 0; }
                set { SetFeatureFlag(10, value); }
            }
            public bool PhysicalAttenuation
            {
                get { return ((flags >> 8) & (1 << 11)) != 0; }
                set { SetFeatureFlag(11, value); }
            }
            public bool DistanceMipSelectionGobo
            {
                get { return ((flags >> 8) & (1 << 12)) != 0; }
                set { SetFeatureFlag(12, value); }
            }
            public bool Volume
            {
                get { return ((flags >> 8) & (1 << 13)) != 0; }
                set { SetFeatureFlag(13, value); }
            }
            public bool NoAlphaLight
            {
                get { return ((flags >> 8) & (1 << 14)) != 0; }
                set { SetFeatureFlag(14, value); }
            }
            public bool HorizontalGoboFlip
            {
                get { return ((flags >> 8) & (1 << 15)) != 0; }
                set { SetFeatureFlag(15, value); }
            }

            public void Read(BinaryReader reader) => flags = reader.ReadInt32();
            public void Write(BinaryWriter writer) => writer.Write(flags);

            public LightFlags() { }
            public LightFlags(int f) => flags = f;

            private void SetFeatureFlag(int position, bool value)
            {
                int mask = 1 << (position + 8);
                if (value) flags |= mask;
                else flags &= ~mask;
            }

            private int flags = 0;
        }
        #endregion
    }
}