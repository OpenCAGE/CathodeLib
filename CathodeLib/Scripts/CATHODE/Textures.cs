//#define APPEND_DDS

using CATHODE.LEGACY.Assets;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /* Handles Cathode LEVEL_TEXTURES.*.PAK files, when in the same folder as a corresponding LEVEL_TEXTURE_HEADERS.*.BIN */
    public class Textures : CathodeFile, IDisposable
    {
        public List<TEX4> Entries = new List<TEX4>();

        private string _filepathBIN;

        public Textures(string path) : base(path) { }

        public void Dispose()
        {
            Entries.Clear();
        }

        #region FILE_IO
        /* Load the file */
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
                    Entries[i].tex_LowRes.Width = bin.ReadInt16();
                    Entries[i].tex_LowRes.Height = bin.ReadInt16();
                    Entries[i].tex_LowRes.Depth = bin.ReadInt16();
                    Entries[i].tex_HighRes.Width = bin.ReadInt16();
                    Entries[i].tex_HighRes.Height = bin.ReadInt16();
                    Entries[i].tex_HighRes.Depth = bin.ReadInt16();
                    Entries[i].tex_LowRes.MipLevels = bin.ReadInt16();
                    Entries[i].tex_HighRes.MipLevels = bin.ReadInt16();
                    Entries[i].Type = (AlienTextureType)bin.ReadInt32();
                    Entries[i].UnknownTexThing = (AlienUnknownTextureThing)bin.ReadInt16();
                    bin.BaseStream.Position += 2; //Always 2048
                    bin.BaseStream.Position += 4; //Skip filename offset value
                    bin.BaseStream.Position += 4; //Skip unused
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
                    //Read texture info
                    pak.BaseStream.Position += 8; //Skip unused
                    int length = BigEndianUtils.ReadInt32(pak);
                    pak.BaseStream.Position += 4; //Skip length check
                    int offset = BigEndianUtils.ReadInt32(pak);
                    pak.BaseStream.Position += 2; //Skip unused
                    bool isHighRes = BigEndianUtils.ReadInt16(pak) == 1;
                    pak.BaseStream.Position += 4; //Skip unused + 256
                    UInt32 unk1 = BigEndianUtils.ReadUInt32(pak);
                    UInt16 unk2 = BigEndianUtils.ReadUInt16(pak);
                    int textureIndex = BigEndianUtils.ReadInt16(pak);
                    pak.BaseStream.Position += 4; //Skip unused
                    UInt32 unk3 = BigEndianUtils.ReadUInt32(pak);
                    UInt32 unk4 = BigEndianUtils.ReadUInt32(pak);

                    //Find the entry
                    TEX4_Part tex = (isHighRes) ? Entries[textureIndex].tex_HighRes : Entries[textureIndex].tex_LowRes;

                    //Write out the unknown info
                    tex.unk1 = unk1;
                    tex.unk2 = unk2;
                    tex.unk3 = unk3;
                    tex.unk4 = unk4;

                    //Pull in texture content
                    int offsetToReturnTo = (int)pak.BaseStream.Position;
                    pak.BaseStream.Position = endOfHeaders + offset;
                    tex.Content = pak.ReadBytes(length);
                    pak.BaseStream.Position = offsetToReturnTo;
                }
            }
            return true;
        }

        /* Save the file */
        override protected bool SaveInternal()
        {
            //Write BIN file
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
                    bin.Write(Entries[i].tex_HighRes.Content == null ? 0 : Entries[i].tex_HighRes.Content.Length);
                    bin.Write(Entries[i].tex_LowRes.Content == null ? 0 : Entries[i].tex_LowRes.Content.Length);
                    bin.Write((Int16)Entries[i].tex_LowRes.Width);
                    bin.Write((Int16)Entries[i].tex_LowRes.Height);
                    bin.Write((Int16)Entries[i].tex_LowRes.Depth);
                    bin.Write((Int16)Entries[i].tex_HighRes.Width);
                    bin.Write((Int16)Entries[i].tex_HighRes.Height);
                    bin.Write((Int16)Entries[i].tex_HighRes.Depth);
                    bin.Write((Int16)Entries[i].tex_LowRes.MipLevels);
                    bin.Write((Int16)Entries[i].tex_HighRes.MipLevels);
                    bin.Write((Int32)Entries[i].Type);
                    bin.Write((Int16)Entries[i].UnknownTexThing);
                    bin.Write((Int16)2048); //TODO: derive this from the actual texture
                    bin.Write((Int32)filenameOffsets[i]);
                    bin.Write(new byte[4]);
                    entryCount++;
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
                        TEX4_Part tex = (x == 0) ? Entries[i].tex_LowRes : Entries[i].tex_HighRes;
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
                        TEX4_Part tex = (x == 0) ? Entries[i].tex_LowRes : Entries[i].tex_HighRes;
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
                for (int x = 0; x < 2; x++)
                {
                    for (int i = 0; i < Entries.Count; i++)
                    {
                        TEX4_Part tex = (x == 0) ? Entries[i].tex_LowRes : Entries[i].tex_HighRes;
                        if (tex.Content != null)
                        {
                            pak.Write(new byte[8]);
                            pak.Write(BigEndianUtils.FlipEndian(tex.Content.Length));
                            pak.Write(BigEndianUtils.FlipEndian(tex.Content.Length));
                            pak.Write(BigEndianUtils.FlipEndian((Int32)offsets[y]));
                            pak.Write(new byte[2]);
                            pak.Write(BigEndianUtils.FlipEndian((Int16)x)); //isHighRes
                            pak.Write(new byte[2]);
                            pak.Write(BigEndianUtils.FlipEndian((Int16)256)); //TODO: derive this from the actual texture?
                            pak.Write(BigEndianUtils.FlipEndian((Int32)tex.unk1));
                            pak.Write(BigEndianUtils.FlipEndian((Int16)tex.unk2));
                            pak.Write(BigEndianUtils.FlipEndian((Int16)i));
                            pak.Write(new byte[4]);
                            pak.Write(BigEndianUtils.FlipEndian((Int32)tex.unk3));
                            pak.Write(BigEndianUtils.FlipEndian((Int32)tex.unk4));
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

        #region ACCESSORS

        #endregion

        #region HELPERS

        #endregion

        #region STRUCTURES
        public class TEX4
        {
            public string Name = "";

            public TextureFormat Format;
            public AlienTextureType Type;
            public AlienUnknownTextureThing UnknownTexThing;

            public TEX4_Part tex_LowRes = new TEX4_Part();
            public TEX4_Part tex_HighRes = new TEX4_Part();
        }

        public class TEX4_Part
        {
            public Int16 Width = 0;
            public Int16 Height = 0;

            public Int16 Depth = 0;
            public Int16 MipLevels = 0;

            public byte[] Content = null;

            //Saving these so we can re-write without issue (TODO: figure them out)
            public UInt32 unk1 = 0;
            public UInt16 unk2 = 0;
            public UInt32 unk3 = 0;
            public UInt32 unk4 = 0;
        }

        public enum TextureFormat : int
        {
            DXGI_FORMAT_B8G8R8A8_UNORM = 0x2,
            SIGNED_DISTANCE_FIELD = 0x4,
            DXGI_FORMAT_B8G8R8_UNORM = 0x5,
            DXGI_FORMAT_BC1_UNORM = 0x6,
            DXGI_FORMAT_BC3_UNORM = 0x9,
            DXGI_FORMAT_BC5_UNORM = 0x8,
            DXGI_FORMAT_BC7_UNORM = 0xD
        }

        public enum AlienTextureType
        {
            SPECULAR_OR_NORMAL = 0,
            DIFFUSE = 1,
            LUT = 21,

            DECAL = 5,
            ENVIRONMENT_MAP = 7,
        }

        public enum AlienUnknownTextureThing
        {
            REGULAR_TEXTURE = 0,
            SOME_SPECIAL_TEXTURE = 9,
        }
        #endregion
    }
}