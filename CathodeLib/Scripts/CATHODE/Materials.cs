using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static CATHODE.Models;
using static CATHODE.Movers;
using static CATHODE.TexturePtr;
using static CATHODE.Textures.TEX4;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/RENDERABLE/LEVEL_MODELS.MTL & LEVEL_MODELS.CST
    /// </summary>
    public class Materials : CathodeFile
    {
        public List<Material> Entries = new List<Material>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        protected override bool HandlesLoadingManually => true;
        private Textures _globalTextures;
        private Textures _levelTextures;
        private Shaders _shaders;

        public Materials(string path, Textures globalTextures, Textures levelTextures, Shaders shaders) : base(path)
        {
            _globalTextures = globalTextures;
            _levelTextures = levelTextures;
            _shaders = shaders;

            _loaded = Load();
        }

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
                        TexturePtr texRef = new TexturePtr(reader, _globalTextures, _levelTextures);
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
                    material.Shader = _shaders.GetAtWriteIndex(reader.ReadInt32());
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

                //Calculate name offsets
                int[] nameOffsets = new int[Entries.Count];
                int nameOffset = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    nameOffsets[i] = nameOffset;
                    nameOffset += Entries[i].Name.Length + 1;
                }

                //Serialize all material entries
                byte[][] materialBuffers = new byte[Entries.Count][];
                Parallel.For(0, Entries.Count, i =>
                {
                    materialBuffers[i] = SerializeMaterial(Entries[i], i, isGlobal, matOffsets, matCounts, nameOffsets[i]);
                });

                //Write material data
                int materialOffset = (int)writer.BaseStream.Position;
                for (int i = 0; i < materialBuffers.Length; i++)
                {
                    writer.Write(materialBuffers[i]);
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

        private byte[] SerializeMaterial(Material material, int index, bool isGlobal, List<int>[] matOffsets, List<int>[] matCounts, int nameOffset)
        {
            using (MemoryStream stream = new MemoryStream(296))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                for (int x = 0; x < 12; x++)
                {
                    TexturePtr tex = x >= material.TextureReferences.Count ? null : material.TextureReferences[x];
                    if (tex == null || tex.Location == TexturePtr.Source.NONE)
                    {
                        writer.Write((Int16)(-1));
                        writer.Write((Int16)(-1));
                        continue;
                    }
                    writer.Write((Int16)(tex.Location == Source.LEVEL ? _levelTextures.GetWriteIndex(tex.Texture) : _globalTextures.GetWriteIndex(tex.Texture)));
                    writer.Write((Int16)tex.Location);
                }

                writer.Write(isGlobal ? 1 : 0);
                writer.Write(index);

                for (int x = 0; x < 5; x++)
                    writer.Write(matOffsets[x][index]);
                for (int x = 0; x < 5; x++)
                    writer.Write((byte)matCounts[x][index]);
                writer.Write(new byte[7]);
                writer.Write(nameOffset);
                writer.Write(_shaders.GetWriteIndex(material.Shader));
                writer.Write(new byte[128]);
                if (material.OfflineLightFeatures != null)
                    material.OfflineLightFeatures.Write(writer);
                else
                    writer.Write(0);
                writer.Write(new byte[49]);
                writer.Write((byte)(material.TextureReferences.Count > 12 ? 12 : material.TextureReferences.Count));
                writer.Write(new byte[2]);
                writer.Write((short)-1);
                writer.Write((byte)material.PhysicalMaterialIndex);
                writer.Write((byte)material.EnvironmentMapIndex);
                writer.Write((byte)material.Priority);
                writer.Write(new byte[11]);

                return stream.ToArray();
            }
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

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public Material AddEntry(Material material)
        {
            if (material == null)
                return null;

            Material newMaterial = material.Copy();

            for (int i = 0; i < newMaterial.TextureReferences.Count; i++)
            {
                //We don't need to add global textures again, since other levels will be pointing to the same.
                if (newMaterial.TextureReferences[i].Location == Source.GLOBAL)
                    continue;

                newMaterial.TextureReferences[i].Texture = _levelTextures.AddEntry(newMaterial.TextureReferences[i].Texture);
            }
            if (newMaterial.Shader != null)
            {
                newMaterial.Shader = _shaders.AddEntry(newMaterial.Shader);
            }
            newMaterial.EnvironmentMapIndex = 255; //TEMP! should remap

            Entries.Add(newMaterial);
            return material;
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
        public class Material : IEquatable<Material>
        {
            public string Name;

            public List<TexturePtr> TextureReferences = new List<TexturePtr>(); //Max of 12

            public List<float> EngineConstants = new List<float>();
            public List<float> VertexShaderConstants = new List<float>();
            public List<float> PixelShaderConstants = new List<float>();
            public List<float> HullShaderConstants = new List<float>();
            public List<float> DomainShaderConstants = new List<float>();

            public LightFlags OfflineLightFeatures = null; //Null if we have none (not a light material)

            public Shaders.Shader Shader; //Index into shader pak
            public int PhysicalMaterialIndex; //255 i assume means none -> this is an index into the Havok physics materials database (at path data/material_data/materials.xml/bml).
            public int EnvironmentMapIndex; //255 means 'any' -> this is an index into our TextureReferences array. it's optionally overridden per renderable instance by MVR.

            public int Priority; 

            public static bool operator ==(Material x, Material y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.Name != y.Name) return false;
                if (!ListsEqual(x.TextureReferences, y.TextureReferences)) return false;
                if (!ListsEqual(x.EngineConstants, y.EngineConstants)) return false;
                if (!ListsEqual(x.VertexShaderConstants, y.VertexShaderConstants)) return false;
                if (!ListsEqual(x.PixelShaderConstants, y.PixelShaderConstants)) return false;
                if (!ListsEqual(x.HullShaderConstants, y.HullShaderConstants)) return false;
                if (!ListsEqual(x.DomainShaderConstants, y.DomainShaderConstants)) return false;
                if (!ReferenceEquals(x.OfflineLightFeatures, y.OfflineLightFeatures))
                {
                    if (x.OfflineLightFeatures == null || y.OfflineLightFeatures == null) return false;
                    if (x.OfflineLightFeatures != y.OfflineLightFeatures) return false;
                }
                if (!ReferenceEquals(x.Shader, y.Shader)) return false;
                if (x.PhysicalMaterialIndex != y.PhysicalMaterialIndex) return false;
                if (x.EnvironmentMapIndex != y.EnvironmentMapIndex) return false;
                if (x.Priority != y.Priority) return false;
                return true;
            }

            public static bool operator !=(Material x, Material y)
            {
                return !(x == y);
            }

            public bool Equals(Material other)
            {
                return this == other;
            }

            public override bool Equals(object obj)
            {
                return obj is Material material && this == material;
            }

            public override int GetHashCode()
            {
                int hashCode = -1234567890;
                hashCode = hashCode * -1521134295 + (Name?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (TextureReferences?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (EngineConstants?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (VertexShaderConstants?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (PixelShaderConstants?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (HullShaderConstants?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (DomainShaderConstants?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (OfflineLightFeatures?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (Shader?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + PhysicalMaterialIndex.GetHashCode();
                hashCode = hashCode * -1521134295 + EnvironmentMapIndex.GetHashCode();
                hashCode = hashCode * -1521134295 + Priority.GetHashCode();
                return hashCode;
            }

            private static bool ListsEqual<T>(List<T> x, List<T> y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                if (x.Count != y.Count) return false;
                for (int i = 0; i < x.Count; i++)
                {
                    if (!EqualityComparer<T>.Default.Equals(x[i], y[i])) return false;
                }
                return true;
            }

            public override string ToString()
            {
                return Name;
            }
        }

        public class LightFlags : IEquatable<LightFlags>
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

            public static bool operator ==(LightFlags x, LightFlags y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                return x.flags == y.flags;
            }

            public static bool operator !=(LightFlags x, LightFlags y)
            {
                return !(x == y);
            }

            public bool Equals(LightFlags other)
            {
                return this == other;
            }

            public override bool Equals(object obj)
            {
                return obj is LightFlags flags && this == flags;
            }

            public override int GetHashCode()
            {
                return flags.GetHashCode();
            }

            private int flags = 0;
        }
        #endregion
    }
}