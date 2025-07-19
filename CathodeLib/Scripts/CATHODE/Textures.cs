//#define APPEND_DDS

using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/RENDERABLE/LEVEL_TEXTURES.*.PAK & LEVEL_TEXTURE_HEADERS.*.BIN */
    public class Textures : CathodeFile
    {
        public List<TEX4> Entries = new List<TEX4>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public Textures(string path) : base(path) { }

        public List<TEX4> _writeList = new List<TEX4>();
        private string _filepathBIN;

        ~Textures()
        {
            Entries.Clear();
            _writeList.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            if (Path.GetFileName(_filepath).Substring(0, 5).ToUpper() != "LEVEL")
                _filepathBIN = _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + "GLOBAL_TEXTURES_HEADERS.ALL.BIN";
            else
                _filepathBIN = _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + "LEVEL_TEXTURE_HEADERS.ALL.BIN";

            if (!File.Exists(_filepathBIN)) return false;

            using (BinaryReader bin = new BinaryReader(File.OpenRead(_filepathBIN)))
            {
                //Read the header info from the BIN
                if ((FileIdentifiers)bin.ReadInt32() != FileIdentifiers.TEXTURE_DATA)
                    return false;
                int entryCount = bin.ReadInt32();
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
            using (BinaryWriter bin = new BinaryWriter(File.OpenWrite(_filepathBIN)))
            {
                bin.BaseStream.SetLength(0);
                bin.Write(new byte[12]);
                List<int> filenameOffsets = new List<int>();
                for (int i = 0; i < Entries.Count; i++)
                {
                    filenameOffsets.Add((int)bin.BaseStream.Position - 12);
                    Utilities.WriteString(Entries[i].Name, bin, true); 
                }
                Utilities.Align(bin, 8);
                int headerListBegin = (int)bin.BaseStream.Position - 12;
                int entryCount = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.WriteString("tex4", bin);
                    bin.Write((Int32)Entries[i].Format);
                    bin.Write(Entries[i].TextureStreamed.Content == null ? 0 : Entries[i].TextureStreamed.Content.Length);
                    bin.Write(Entries[i].TexturePersistent.Content == null ? 0 : Entries[i].TexturePersistent.Content.Length);
                    bin.Write((Int16)Entries[i].TexturePersistent.Width);
                    bin.Write((Int16)Entries[i].TexturePersistent.Height);
                    bin.Write((Int16)Entries[i].TexturePersistent.Depth);
                    bin.Write((Int16)Entries[i].TextureStreamed.Width);
                    bin.Write((Int16)Entries[i].TextureStreamed.Height);
                    bin.Write((Int16)Entries[i].TextureStreamed.Depth);
                    bin.Write((Int16)Entries[i].TexturePersistent.MipLevels);
                    bin.Write((Int16)Entries[i].TextureStreamed.MipLevels);
                    bin.Write((Int32)Entries[i].StateFlags);
                    bin.Write((Int32)Entries[i].UsageFlags);
                    bin.Write((Int32)filenameOffsets[i]);
                    bin.Write(new byte[4]);
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

                //Write texture headers
                pak.BaseStream.Position = 32;
                int y = 0;
                for (int sort = 0; sort < 2; sort++)
                {
                    for (int i = 0; i < Entries.Count; i++)
                    {
                        TEX4.Texture tex = (sort == 0) ? Entries[i].TexturePersistent : Entries[i].TextureStreamed;
                        if (tex.Content != null)
                        {
                            pak.Write(new byte[8]);
                            pak.Write(BigEndianUtils.FlipEndian(tex.Content.Length));
                            pak.Write(BigEndianUtils.FlipEndian(tex.Content.Length));
                            pak.Write(BigEndianUtils.FlipEndian((Int32)offsets[y]));
                            pak.Write(BigEndianUtils.FlipEndian(sort));
                            pak.Write(BigEndianUtils.FlipEndian(256));
                            pak.Write(new byte[4]);
                            pak.Write(BigEndianUtils.FlipEndian((Int16)(sort == 0 ? 32768 : 0)));
                            pak.Write(BigEndianUtils.FlipEndian((Int16)i));
                            pak.Write(new byte[12]);
                        }
                        y++;
                    }
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
        #endregion

        #region HELPERS
        /* Get the current index for a texture (useful for cross-ref'ing with compiled binaries)
         * Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk */
        public int GetWriteIndex(TEX4 texture)
        {
            if (!_writeList.Contains(texture)) return -1;
            return _writeList.IndexOf(texture);
        }

        /* Get a texture by its current index (useful for cross-ref'ing with compiled binaries)
         * Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk */
        public TEX4 GetAtWriteIndex(int index)
        {
            if (index < 0 || _writeList.Count <= index) return null;
            return _writeList[index];
        }
        #endregion

        #region STRUCTURES
        public class TEX4
        {
            public string Name = "";

            public TextureFormat Format;
            public TextureStateFlag StateFlags;
            public TextureUsageFlag UsageFlags;

            public Texture TexturePersistent = new Texture(); // A lower res version of the texture kept loaded forever
            public Texture TextureStreamed = new Texture();   // A higher res version of the texture which is streamed when required (not all have this)

            ~TEX4()
            {
                TexturePersistent = null;
                TextureStreamed = null;
            }

            public class Texture
            {
                public Int16 Width = 0;
                public Int16 Height = 0;

                public Int16 Depth = 0;
                public Int16 MipLevels = 0;

                public byte[] Content = null;

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