//#define APPEND_DDS

using CATHODE.LEGACY.Assets;
using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* Handles Cathode LEVEL_TEXTURES.*.PAK files, when in the same folder as a corresponding LEVEL_TEXTURE_HEADERS.*.BIN */
    public class Textures : CathodeFile
    {
        public List<TEX4> Entries = new List<TEX4>();

        private string _filepathBIN;

        public Textures(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        override protected bool LoadInternal()
        {
            if (Path.GetFileName(_filepath).Substring(0, 5).ToUpper() == "LEVEL")
                _filepathBIN = _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + "LEVEL_TEXTURE_HEADERS.ALL.BIN";
            else
                _filepathBIN = _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + "GLOBAL_TEXTURES_HEADERS.ALL.BIN";

            if (!File.Exists(_filepathBIN)) return false;

            /* First, parse the BIN and pull ALL info from it */
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
                    TextureEntry.FileName = Utilities.ReadString(bin);
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
                    Entries[i].tex_HighRes.Length = bin.ReadInt32();
                    Entries[i].tex_LowRes.Length = bin.ReadInt32();
                    Entries[i].tex_LowRes.Width = bin.ReadInt16();
                    Entries[i].tex_LowRes.Height = bin.ReadInt16();
                    Entries[i].tex_LowRes.Depth = bin.ReadInt16();
                    Entries[i].tex_HighRes.Width = bin.ReadInt16();
                    Entries[i].tex_HighRes.Height = bin.ReadInt16();
                    Entries[i].tex_HighRes.Depth = bin.ReadInt16();
                    Entries[i].tex_LowRes.MipLevels = bin.ReadInt16();
                    Entries[i].tex_HighRes.MipLevels = bin.ReadInt16();
                    Entries[i].Type = bin.ReadInt32();
                    Entries[i].UnknownTexThing = (AlienUnknownTextureThing)bin.ReadInt16();
                    bin.BaseStream.Position += 2; //Always 2048
                    bin.BaseStream.Position += 4; //Skip filename offset value
                    bin.BaseStream.Position += 4; //Skip unused
                }
            }

            /* Second, parse the PAK and pull ONLY header info from it - we'll pull textures when requested (to save memory) */
            using (BinaryReader pak = new BinaryReader(File.OpenRead(_filepath)))
            {
                //Read & check the header info from the PAK
                pak.BaseStream.Position += 4; //Skip unused
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.ASSET_FILE) return false;
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.TEXTURE_DATA) return false;
                int entryCount = BigEndianUtils.ReadInt32(pak);
                if (BigEndianUtils.ReadInt32(pak) != entryCount) return false;
                pak.BaseStream.Position += 12; //Skip unused

                //Read the texture headers from the PAK
                for (int i = 0; i < entryCount; i++)
                {
                    pak.BaseStream.Position += 8; //Skip unused

                    int length = BigEndianUtils.ReadInt32(pak);
                    if (length != BigEndianUtils.ReadInt32(pak)) return false;

                    int offset = BigEndianUtils.ReadInt32(pak);

                    pak.BaseStream.Position += 2; //Skip unused

                    int isHighRes = BigEndianUtils.ReadInt16(pak);

                    pak.BaseStream.Position += 2; //Skip unused

                    int val256 = BigEndianUtils.ReadInt16(pak); //always 256
                    if (val256 != 256) return false;

                    UInt32 unk1 = BigEndianUtils.ReadUInt32(pak);
                    UInt16 unk2 = BigEndianUtils.ReadUInt16(pak);

                    int index = BigEndianUtils.ReadInt16(pak);

                    pak.BaseStream.Position += 4; //Skip unused

                    UInt32 unk3 = BigEndianUtils.ReadUInt32(pak);
                    UInt32 unk4 = BigEndianUtils.ReadUInt32(pak);

                    //Find the entry
                    TEX4_Part texFullRes = (isHighRes == 1) ? Entries[index].tex_HighRes : Entries[index].tex_LowRes;
                    if (length != texFullRes.Length) return false;

                    //Write out the info
                    texFullRes.Offset = offset;
                    texFullRes.unk1 = unk1;
                    texFullRes.unk2 = unk2;
                    texFullRes.unk3 = unk3;
                    texFullRes.unk4 = unk4;
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
                    Utilities.WriteString(Entries[i].FileName, bin, true); 
                }
                Utilities.Align(bin, 4);
                bin.Write(new byte[12]);
                int headerListBegin = (int)bin.BaseStream.Position - 12;
                int entryCount = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.WriteString("tex4", bin);
                    bin.Write((Int32)Entries[i].Format);
                    bin.Write((Int32)Entries[i].tex_HighRes.Length);
                    bin.Write((Int32)Entries[i].tex_LowRes.Length);
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
                pak.BaseStream.SetLength(32);
                pak.BaseStream.Position = 32;
                int pakEntryCount = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        TEX4_Part currentRes = (x == 0) ? Entries[i].tex_LowRes : Entries[i].tex_HighRes;
                        if (currentRes.Length == 0) continue;
                        pak.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.Length));
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.Length));
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.Offset)); //TODO: need to calculate this
                        pak.Write(new byte[] { 0x00, 0x00 });
                        pak.Write((Int16)x); //isHighRes
                        pak.Write(new byte[] { 0x00, 0x00 });
                        pak.Write((Int16)256); //TODO: derive this from the actual texture
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.unk1));
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.unk2));
                        pak.Write(BigEndianUtils.FlipEndian((Int16)i));
                        pak.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.unk3));
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.unk4));
                        pakEntryCount++;
                    }
                }
                //TODO: Pull all PAK content for textures & then rewrite properly using new info
                pak.BaseStream.Position = 0;
                pak.Write(0);
                pak.Write((int)FileIdentifiers.ASSET_FILE);
                pak.Write((int)FileIdentifiers.TEXTURE_DATA);
                pak.Write(pakEntryCount);
                pak.Write(pakEntryCount);
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
            public string FileName = "";

            public TextureFormat Format;
            public int Type = -1; //AlienTextureType
            public AlienUnknownTextureThing UnknownTexThing;

            public TEX4_Part tex_LowRes = new TEX4_Part();
            public TEX4_Part tex_HighRes = new TEX4_Part(); //We don't always have this
        }

        public class TEX4_Part
        {
            public Int16 Width = 0;
            public Int16 Height = 0;

            public Int16 Depth = 0;
            public Int16 MipLevels = 0;

            //TODO: just store the byte array
            public int Offset = 0;
            public int Length = 0;

            //Saving these so we can re-write without issue
            public UInt32 unk1 = 0;
            public UInt16 unk2 = 0;
            public UInt32 unk3 = 0;
            public UInt32 unk4 = 0;
        }
        #endregion
    }
}