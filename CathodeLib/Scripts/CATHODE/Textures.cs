//#define APPEND_DDS

using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CATHODE.Models;
using static CATHODE.Shaders;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/RENDERABLE/LEVEL_TEXTURES.*.PAK & LEVEL_TEXTURE_HEADERS.*.BIN
    /// </summary>
    public class Textures : CathodeFile
    {
        public List<TEX4> Entries = new List<TEX4>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public Textures(string path) : base(path) { }

        public List<TEX4> _writeList = new List<TEX4>();

        ~Textures()
        {
            Entries.Clear();
            _writeList.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            //NOTE: Loading via byte[] or MemoryStream is not currently supported. Must be loaded via disk from a filepath!
            if (_filepath == "")
                return false;

            string filepathBIN = GetBinPath();
            if (!File.Exists(filepathBIN)) 
                return false;

            using (BinaryReader bin = new BinaryReader(File.OpenRead(filepathBIN)))
            {
                //Read the header info from the BIN
                if ((FileIdentifiers)bin.ReadInt32() != FileIdentifiers.TEXTURE_DATA)
                    return false;
                int entryCount = bin.ReadInt32();
                if (entryCount == 0)
                    return true;
                int headerListBegin = bin.ReadInt32();

                //Read all file names from BIN and create texture entry
                for (int i = 0; i < entryCount; i++)
                {
                    TEX4 TextureEntry = new TEX4();
                    TextureEntry.Name = Utilities.ReadString(bin);
#if APPEND_DDS
                    //TODO: maybe we should stop doing this
                    if (Path.GetExtension(TextureEntry.FileName).ToUpper() != ".DDS")
                        TextureEntry.FileName += ".dds";
#endif
                    Entries.Add(TextureEntry);
                }

                //Read the texture headers from the BIN
                bin.BaseStream.Position = headerListBegin + 12;
                for (int i = 0; i < entryCount; i++)
                {
                    bin.BaseStream.Position += 4; //fourcc
                    Entries[i].Format = (TextureFormat)bin.ReadInt32();
                    bin.BaseStream.Position += 8; //Lengths
                    Entries[i].TexturePersistent.Width = bin.ReadInt16();
                    Entries[i].TexturePersistent.Height = bin.ReadInt16();
                    Entries[i].TexturePersistent.Depth = bin.ReadInt16();
                    Entries[i].TextureStreamed.Width = bin.ReadInt16();
                    Entries[i].TextureStreamed.Height = bin.ReadInt16();
                    Entries[i].TextureStreamed.Depth = bin.ReadInt16();
                    Entries[i].TexturePersistent.MipLevels = bin.ReadInt16();
                    Entries[i].TextureStreamed.MipLevels = bin.ReadInt16();
                    Entries[i].StateFlags = (TextureStateFlag)bin.ReadInt32();
                    Entries[i].UsageFlags = (TextureUsageFlag)bin.ReadInt32();
                    bin.BaseStream.Position += 8; //Skip filename offset value
                    _writeList.Add(Entries[i]);
                }
            }

            using (BinaryReader pak = new BinaryReader(File.OpenRead(_filepath)))
            {
                //Read & check the header info from the PAK
                pak.BaseStream.Position += 4; //Skip unused
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.ASSET_FILE) return false;
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.TEXTURE_DATA) return false;
                int entryCount = BigEndianUtils.ReadInt32(pak);
                pak.BaseStream.Position += 16; //Skip count actual (since doesn't matter) and unused

                int endOfHeaders = 32 + (entryCount * 48);
                for (int i = 0; i < entryCount; i++)
                {
                    //Read texture header info [48]
                    pak.BaseStream.Position += 8;
                    int length = BigEndianUtils.ReadInt32(pak);
                    pak.BaseStream.Position += 4; //length again
                    int offset = BigEndianUtils.ReadInt32(pak);
                    int sort = BigEndianUtils.ReadInt32(pak);
                    pak.BaseStream.Position += 10; //last 2 bytes here are a flag for low mip (sort tells us this already)
                    int textureIndex = BigEndianUtils.ReadInt16(pak);
                    pak.BaseStream.Position += 12;

                    //Store texture content
                    TEX4.Texture tex = (sort == 1) ? Entries[textureIndex].TextureStreamed : Entries[textureIndex].TexturePersistent;
                    int offsetToReturnTo = (int)pak.BaseStream.Position;
                    pak.BaseStream.Position = endOfHeaders + offset;
                    tex.Content = pak.ReadBytes(length);
                    pak.BaseStream.Position = offsetToReturnTo;
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            //Write BIN file
            _writeList.Clear();
            using (BinaryWriter bin = new BinaryWriter(File.OpenWrite(GetBinPath())))
            {
                bin.BaseStream.SetLength(0);
                bin.Write(new byte[12]);
                List<int> filenameOffsets = new List<int>();
                for (int i = 0; i < Entries.Count; i++)
                {
                    filenameOffsets.Add((int)bin.BaseStream.Position - 12);
                    Utilities.WriteString(Entries[i].Name, bin, true);
                }
                Utilities.Align(bin, 16);
                int headerListBegin = (int)bin.BaseStream.Position - 12;
                
                byte[][] headerBuffers = new byte[Entries.Count][];
                Parallel.For(0, Entries.Count, i =>
                {
                    headerBuffers[i] = SerializeTextureHeader(Entries[i], filenameOffsets[i]);
                });
                int entryCount = 0;
                for (int i = 0; i < headerBuffers.Length; i++)
                {
                    bin.Write(headerBuffers[i]);
                    entryCount++;
                    _writeList.Add(Entries[i]);
                }
                bin.BaseStream.Position = 0;
                bin.Write((int)FileIdentifiers.TEXTURE_DATA);
                bin.Write(entryCount);
                bin.Write(headerListBegin);
            }

            //Update headers in PAK for all entries
            using (BinaryWriter pak = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                //Figure out number of non-null contents
                int writeCount = 0;
                for (int x = 0; x < 2; x++)
                {
                    for (int i = 0; i < Entries.Count; i++)
                    {
                        TEX4.Texture tex = (x == 0) ? Entries[i].TexturePersistent : Entries[i].TextureStreamed;
                        if (tex.Content == null) continue;
                        writeCount++;
                    }
                }
                int contentOffset = 32 + (writeCount * 48);

                //Write texture content
                pak.BaseStream.SetLength(contentOffset);
                pak.BaseStream.Position = contentOffset;
                List<int> offsets = new List<int>();
                for (int x = 0; x < 2; x++)
                {
                    for (int i = 0; i < Entries.Count; i++)
                    {
                        TEX4.Texture tex = (x == 0) ? Entries[i].TexturePersistent : Entries[i].TextureStreamed;
                        offsets.Add((int)pak.BaseStream.Position - contentOffset);
                        if (tex.Content != null)
                        {
                            pak.Write(tex.Content);
                        }
                    }
                }

                //Get all textures
                List<(TEX4.Texture tex, int entryIndex, int sort, int offsetIndex)> textureList = new List<(TEX4.Texture, int, int, int)>();
                int y = 0;
                for (int sort = 0; sort < 2; sort++)
                {
                    for (int i = 0; i < Entries.Count; i++)
                    {
                        TEX4.Texture tex = (sort == 0) ? Entries[i].TexturePersistent : Entries[i].TextureStreamed;
                        if (tex.Content != null)
                        {
                            textureList.Add((tex, i, sort, y));
                        }
                        y++;
                    }
                }
                
                //Write textures
                byte[][] pakHeaderBuffers = new byte[textureList.Count][];
                Parallel.For(0, textureList.Count, idx =>
                {
                    var (tex, entryIdx, sort, offsetIdx) = textureList[idx];
                    pakHeaderBuffers[idx] = SerializePakTextureHeader(tex, entryIdx, sort, offsets[offsetIdx]);
                });
                pak.BaseStream.Position = 32;
                for (int idx = 0; idx < pakHeaderBuffers.Length; idx++)
                {
                    pak.Write(pakHeaderBuffers[idx]);
                }

                //Write main header
                pak.BaseStream.Position = 0;
                pak.Write(0);
                pak.Write(BigEndianUtils.FlipEndian((int)FileIdentifiers.ASSET_FILE));
                pak.Write(BigEndianUtils.FlipEndian((int)FileIdentifiers.TEXTURE_DATA));
                pak.Write(BigEndianUtils.FlipEndian(writeCount));
                pak.Write(BigEndianUtils.FlipEndian(writeCount));
            }
            return true;
        }

        private byte[] SerializeTextureHeader(TEX4 entry, int filenameOffset)
        {
            using (MemoryStream stream = new MemoryStream(48))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                Utilities.WriteString("tex4", writer);
                writer.Write((Int32)entry.Format);
                writer.Write(entry.TextureStreamed.Content == null ? 0 : entry.TextureStreamed.Content.Length);
                writer.Write(entry.TexturePersistent.Content == null ? 0 : entry.TexturePersistent.Content.Length);
                writer.Write((Int16)entry.TexturePersistent.Width);
                writer.Write((Int16)entry.TexturePersistent.Height);
                writer.Write((Int16)entry.TexturePersistent.Depth);
                writer.Write((Int16)entry.TextureStreamed.Width);
                writer.Write((Int16)entry.TextureStreamed.Height);
                writer.Write((Int16)entry.TextureStreamed.Depth);
                writer.Write((Int16)entry.TexturePersistent.MipLevels);
                writer.Write((Int16)entry.TextureStreamed.MipLevels);
                writer.Write((Int32)entry.StateFlags);
                writer.Write((Int32)entry.UsageFlags);
                writer.Write((Int32)filenameOffset);
                writer.Write(new byte[4]);
                return stream.ToArray();
            }
        }

        private byte[] SerializePakTextureHeader(TEX4.Texture tex, int entryIndex, int sort, int offset)
        {
            using (MemoryStream stream = new MemoryStream(48))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(new byte[8]);
                writer.Write(BigEndianUtils.FlipEndian(tex.Content == null ? 0 : tex.Content.Length));
                writer.Write(BigEndianUtils.FlipEndian(tex.Content == null ? 0 : tex.Content.Length));
                writer.Write(BigEndianUtils.FlipEndian((Int32)offset));
                writer.Write(BigEndianUtils.FlipEndian(sort));
                writer.Write(BigEndianUtils.FlipEndian(256));
                writer.Write(new byte[4]);
                writer.Write(BigEndianUtils.FlipEndian((Int16)(sort == 0 ? 32768 : 0)));
                writer.Write(BigEndianUtils.FlipEndian((Int16)entryIndex));
                writer.Write(new byte[12]);
                return stream.ToArray();
            }
        }

        private string GetBinPath()
        {
            if (Path.GetFileName(_filepath).Substring(0, 5).ToUpper() != "LEVEL")
                return _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + "GLOBAL_TEXTURES_HEADERS.ALL.BIN";
            else
                return _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + "LEVEL_TEXTURE_HEADERS.ALL.BIN";
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the current index for a texture (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(TEX4 texture)
        {
            if (!_writeList.Contains(texture)) return -1;
            return _writeList.IndexOf(texture);
        }

        /// <summary>
        /// Get a texture by its current index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public TEX4 GetAtWriteIndex(int index)
        {
            if (index < 0 || _writeList.Count <= index) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public TEX4 AddEntry(TEX4 texture)
        {
            if (texture == null)
                return null;

            var existing = Entries.FirstOrDefault(o => o == texture);
            if (existing != null)
                return existing;

            TEX4 newTexture = texture.Copy();
            Entries.Add(newTexture);
            return newTexture;
        }
        #endregion

        #region STRUCTURES
        public class TEX4 : IEquatable<TEX4>
        {
            public string Name = "";

            public TextureFormat Format;
            public TextureStateFlag StateFlags;
            public TextureUsageFlag UsageFlags;

            public Texture TexturePersistent = new Texture(); // A lower res version of the texture kept loaded forever
            public Texture TextureStreamed = new Texture();   // A higher res version of the texture which is streamed when required (not all have this)

            public static bool operator ==(TEX4 x, TEX4 y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                return x.Equals(y);
            }

            public static bool operator !=(TEX4 x, TEX4 y)
            {
                return !(x == y);
            }

            public bool Equals(TEX4 other)
            {
                if (other == null) return false;
                if (ReferenceEquals(this, other)) return true;

                if (Name != other.Name) return false;
                if (Format != other.Format) return false;
                if (StateFlags != other.StateFlags) return false;
                if (UsageFlags != other.UsageFlags) return false;
                if (!TexturePersistent.Equals(other.TexturePersistent)) return false;
                if (!TextureStreamed.Equals(other.TextureStreamed)) return false;

                return true;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as TEX4);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + (Name?.GetHashCode() ?? 0);
                    hash = hash * 23 + Format.GetHashCode();
                    hash = hash * 23 + StateFlags.GetHashCode();
                    hash = hash * 23 + UsageFlags.GetHashCode();
                    hash = hash * 23 + (TexturePersistent?.GetHashCode() ?? 0);
                    hash = hash * 23 + (TextureStreamed?.GetHashCode() ?? 0);
                    return hash;
                }
            }

            ~TEX4()
            {
                TexturePersistent = null;
                TextureStreamed = null;
            }

            public class Texture : IEquatable<Texture>
            {
                public Int16 Width = 0;
                public Int16 Height = 0;

                public Int16 Depth = 0;
                public Int16 MipLevels = 0;

                public byte[] Content = null;

                public static bool operator ==(Texture x, Texture y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return false;
                    return x.Equals(y);
                }

                public static bool operator !=(Texture x, Texture y)
                {
                    return !(x == y);
                }

                public bool Equals(Texture other)
                {
                    if (other == null) return false;
                    if (ReferenceEquals(this, other)) return true;

                    if (Width != other.Width) return false;
                    if (Height != other.Height) return false;
                    if (Depth != other.Depth) return false;
                    if (MipLevels != other.MipLevels) return false;

                    // Compare Content byte arrays
                    if (Content == null && other.Content != null) return false;
                    if (Content != null && other.Content == null) return false;
                    if (Content != null && other.Content != null)
                    {
                        if (Content.Length != other.Content.Length) return false;
                        for (int i = 0; i < Content.Length; i++)
                        {
                            if (Content[i] != other.Content[i]) return false;
                        }
                    }

                    return true;
                }

                public override bool Equals(object obj)
                {
                    return Equals(obj as Texture);
                }

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int hash = 17;
                        hash = hash * 23 + Width.GetHashCode();
                        hash = hash * 23 + Height.GetHashCode();
                        hash = hash * 23 + Depth.GetHashCode();
                        hash = hash * 23 + MipLevels.GetHashCode();
                        if (Content != null)
                        {
                            hash = hash * 23 + Content.Length.GetHashCode();
                            for (int i = 0; i < Content.Length && i < 16; i++) // Limit to first 16 bytes for hash
                            {
                                hash = hash * 23 + Content[i].GetHashCode();
                            }
                        }
                        return hash;
                    }
                }

                ~Texture()
                {
                    Content = null;
                }
            }
        }

        public enum TextureFormat
        {
            A32R32G32B32F,
            A16R16G16B16,
            A8R8G8B8,
            X8R8G8B8,
            A8,
            L8,
            DXT1,
            DXT3,
            DXN,
            DXT5,
            A4R4G4B4,
            CTX1,
            BC6H,
            BC7,
            R16F,
            AUTO,

            ASTC4X4,   //ASTC is only supported by the Feral Interactive mobile ports!
            ASTC8X8,   //ASTC is only supported by the Feral Interactive mobile ports!
            ASTC12X12, //ASTC is only supported by the Feral Interactive mobile ports!
        }

        [Flags]
        public enum TextureStateFlag
        {
            ALLOW_SRGB = 1 << 0,
            CUBE = 1 << 1,
            NON_SOLID = 1 << 2,
            SWIZZLED = 1 << 3,
            VOLUME = 1 << 4,
            EXPORTED_WITH_WARNINGS = 1 << 5,
            FLASH_TEXTURE = 1 << 6
        }

        [Flags]
        public enum TextureUsageFlag
        {
            DEFAULT = 0,
            POST_PROCESSING = 1 << 0,
            LENS_FLARE = 1 << 1,
            GALAXY = 1 << 2,
            LENS_DUST = 1 << 3,

            //NOTE: Weirdly, the GLOBAL PAK still seems to report as LEVEL, so I think it's safe to always just set that
            IS_LEVEL_PACK = 1 << 27,
            IS_GLOBAL_PACK = 1 << 28,
        }
        #endregion
    }
}