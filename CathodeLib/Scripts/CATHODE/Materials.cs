using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static CATHODE.Models;
using static CATHODE.Movers;
using static CATHODE.Shaders;
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

        public bool Compressed { get { return _compressed; } set { _compressed = value; } }
        private bool _compressed = false;

        private List<Material> _writeList = new List<Material>();

        public Materials(string path, Textures globalTextures, Textures levelTextures, Shaders shaders) : base(path)
        {
            _globalTextures = globalTextures;
            _levelTextures = levelTextures;
            _shaders = shaders;

            _loaded = Load();
        }

        public void ClearReferences()
        {
            _globalTextures = null;
            _levelTextures = null;
            _shaders = null;
        }

        ~Materials()
        {
            ClearReferences();
            Entries.Clear();
            _writeList.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            //NOTE: Loading via byte[] or MemoryStream is not currently supported. Must be loaded via disk from a filepath!
            if (_filepath == "")
                return false;

            _compressed = Path.GetExtension(_filepath).ToLower() == ".gz";

            if (!File.Exists(GetCstPath())) 
                return false;

            using (MemoryStream cstStream = new MemoryStream(File.ReadAllBytes(GetCstPath())))
            using (BinaryReader cst = new BinaryReader(_compressed ? Utilities.GZIPDecompress(cstStream) : cstStream))
            using (BinaryReader mtl = new BinaryReader(_compressed ? Utilities.GZIPDecompress(stream) : stream))
            {
                mtl.BaseStream.Position += 8;
                int materialOffset = mtl.ReadInt32();

                //base CST offsets
                int engineConstantsOffset = mtl.ReadInt32();
                int vertexConstantsOffset = mtl.ReadInt32();
                int pixelConstantsOffset = mtl.ReadInt32();
                int hullConstantsOffset = mtl.ReadInt32();
                int domainConstantsOffset = mtl.ReadInt32();

                mtl.BaseStream.Position += 8;
                int materialCount = mtl.ReadInt16();
                mtl.BaseStream.Position += 2;

                List<string> materialNames = new List<string>(materialCount);
                for (int i = 0; i < materialCount; ++i) materialNames.Add(Utilities.ReadString(mtl));

                mtl.BaseStream.Position = materialOffset + 4;
                for (int i = 0; i < materialCount; i++)
                {
                    Material material = new Material();
                    for (int x = 0; x < 12; x++)
                    {
                        TexturePtr texRef = new TexturePtr(mtl, _globalTextures, _levelTextures);
                        if (texRef.Location == TexturePtr.Source.NONE)
                            continue;
                        material.TextureReferences.Add(texRef);
                    }
                    mtl.BaseStream.Position += 8;

                    int[] ubershaderConstantOffset = Utilities.ConsumeArray<int>(mtl, 5);
                    byte[] ubershaderConstantCount = Utilities.ConsumeArray<byte>(mtl, 5);
                    material.EngineConstants = ReadCSTData(engineConstantsOffset + (ubershaderConstantOffset[0] * 4), ubershaderConstantCount[0], cst);
                    material.VertexShaderConstants = ReadCSTData(vertexConstantsOffset + (ubershaderConstantOffset[1] * 4), ubershaderConstantCount[1], cst);
                    material.PixelShaderConstants = ReadCSTData(pixelConstantsOffset + (ubershaderConstantOffset[2] * 4), ubershaderConstantCount[2], cst);
                    material.HullShaderConstants = ReadCSTData(hullConstantsOffset + (ubershaderConstantOffset[3] * 4), ubershaderConstantCount[3], cst);
                    material.DomainShaderConstants = ReadCSTData(domainConstantsOffset + (ubershaderConstantOffset[4] * 4), ubershaderConstantCount[4], cst);

                    mtl.BaseStream.Position += 11;
                    material.Name = materialNames[i]; 
                    material.Shader = _shaders.GetAtWriteIndex(mtl.ReadInt32());
                    mtl.BaseStream.Position += 128;
                    int lightFlags = mtl.ReadInt32();
                    if (lightFlags != 0) material.OfflineLightFeatures = new LightFlags(lightFlags);
                    mtl.BaseStream.Position += 54;
                    material.PhysicalMaterialIndex = mtl.ReadByte(); 
                    material.EnvironmentMapIndex = mtl.ReadByte(); 
                    material.Priority = mtl.ReadByte();


                    mtl.BaseStream.Position += 11;

                    Entries.Add(material);
                    _writeList.Add(material);
                }
            }

            return true;
        }

        override protected bool SaveInternal()
        {
            if (_compressed && Path.GetExtension(_filepath).ToLower() != ".gz")
                _filepath += ".gz";
            else if (!_compressed && Path.GetExtension(_filepath).ToLower() == ".gz")
                _filepath = _filepath.Substring(0, _filepath.Length - 3);

            //Write constants
            int[] offsets = new int[5];
            List<int>[] matOffsets = new List<int>[5];
            List<int>[] matCounts = new List<int>[5];
            for (int i = 0; i < 5; i++)
            {
                matOffsets[i] = new List<int>();
                matCounts[i] = new List<int>();
            }
            using (Stream cstStream = File.OpenWrite(GetCstPath()))
            using (BinaryWriter cst = new BinaryWriter(cstStream))
            {
                cst.BaseStream.SetLength(0);
                for (int i = 0; i < 5; i++)
                {
                    offsets[i] = (int)cst.BaseStream.Position;

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
                        matOffsets[i].Add(constants.Count == 0 ? 0 : ((int)cst.BaseStream.Position - offsets[i]) / 4);
                        matCounts[i].Add(constants.Count);

                        for (int z = 0; z < constants.Count; z++)
                            cst.Write(constants[z]);
                    }
                }
            }

            if (_compressed)
                Utilities.GZIPCompress(GetCstPath());

            _writeList.Clear();
            using (Stream mtlStream = File.OpenWrite(_filepath))
            using (BinaryWriter mtl = new BinaryWriter(mtlStream))
            {
                mtl.BaseStream.SetLength(0);

                //Write header
                mtl.Write(0); //placeholder file length (-4)
                mtl.Write(40);
                mtl.Write(0); //placeholder material offset
                mtl.Write(offsets[0]);
                mtl.Write(offsets[1]);
                mtl.Write(offsets[2]);
                mtl.Write(offsets[3]);
                mtl.Write(offsets[4]);
                mtl.Write(44);
                mtl.Write(0); //placeholder material name size
                mtl.Write((Int16)Entries.Count);
                mtl.Write((Int16)296);

                //Write material names
                int preNamesLength = (int)mtl.BaseStream.Position;
                for (int i = 0; i < Entries.Count; ++i) Utilities.WriteString(Entries[i].Name, mtl, true);
                int namesLength = (int)mtl.BaseStream.Position - preNamesLength;
                int misalignment = 16 - (((int)mtl.BaseStream.Position - 4) & 0xf);
                if (misalignment != 16)
                    mtl.Write(new byte[misalignment]);

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
                int materialOffset = (int)mtl.BaseStream.Position;
                for (int i = 0; i < materialBuffers.Length; i++)
                {
                    mtl.Write(materialBuffers[i]);
                    _writeList.Add(Entries[i]);
                }

                //Correct placeholders
                mtl.BaseStream.Position = 0;
                mtl.Write((int)mtl.BaseStream.Length - 4);
                mtl.BaseStream.Position = 8;
                mtl.Write(materialOffset - 4);
                mtl.BaseStream.Position = 36;
                mtl.Write(namesLength);
            }

            if (_compressed)
                Utilities.GZIPCompress(_filepath);

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

        private string GetCstPath()
        {
            return _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + Path.GetFileName(_filepath).Split('.')[0] + ".CST" + (_compressed ? ".GZ" : "");
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
        public Material ImportEntry(Material material)
        {
            if (material == null)
                return null;

            Material newMaterial = material.Copy();

            for (int i = 0; i < newMaterial.TextureReferences.Count; i++)
            {
                //We don't need to add global textures again, since other levels will be pointing to the same.
                if (newMaterial.TextureReferences[i].Location == Source.GLOBAL)
                    continue;

                newMaterial.TextureReferences[i].Texture = _levelTextures.ImportEntry(newMaterial.TextureReferences[i].Texture);
            }
            newMaterial.Shader = _shaders.ImportEntry(newMaterial.Shader);
            //newMaterial.EnvironmentMapIndex = 255; //TEMP! should remap

            var existing = Entries.FirstOrDefault(o => o == newMaterial);
            if (existing != null)
                return existing;

            Entries.Add(newMaterial);
            return newMaterial;
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

            public Shaders.Shader Shader;
            public int PhysicalMaterialIndex; //255 i assume means none -> this is an index into the Havok physics materials database (at path data/material_data/materials.xml/bml).
            public int EnvironmentMapIndex; //255 means 'any' -> this is an index into our TextureReferences array. it's optionally overridden per renderable instance by MVR.

            public int Priority; 

            public static bool operator ==(Material x, Material y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.Name != y.Name) return false;

                if (x.TextureReferences?.Count != y.TextureReferences?.Count) return false;
                for (int i = 0; i < x.TextureReferences.Count; i++)
                    if (x.TextureReferences[i] != y.TextureReferences[i]) return false;
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
                if (x.Shader != y.Shader) return false;
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

            private static bool ListsEqual(List<float> x, List<float> y)
            {
                if (x?.Count != y?.Count) return false;
                for (int i = 0; i < x.Count; i++)
                    if (x[i] != y[i])
                        return false;
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

            public static bool operator ==(LightFlags x, int y)
            {
                if (x == null) return false;
                return x.flags == y;
            }

            public static bool operator !=(LightFlags x, LightFlags y)
            {
                return !(x == y);
            }

            public static bool operator !=(LightFlags x, int y)
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