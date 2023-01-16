using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE.LEGACY.Assets
{
    /*
     *
     * Texture PAK handler.
     * Allows import/export of CATHODE TEX4 texture files.
     * More work needs to be done to understand the broken formats and to allow importing for files with only V1.
     * 
    */
    public class Textures : AssetPAK
    {
        private List<CATHODE.Textures.TEX4> _entries = new List<CATHODE.Textures.TEX4>();
        private int HeaderListBeginBIN = -1;
        private int NumberOfEntriesPAK = -1;
        private int NumberOfEntriesBIN = -1;

        /* Initialise the TexturePAK class with the intended location (existing or not) */
        public Textures(string PathToPAK)
        {
            _filePathPAK = PathToPAK;

            if (Path.GetFileName(_filePathPAK).Substring(0, 5).ToUpper() == "LEVEL")
            {
                _filePathBIN = _filePathPAK.Substring(0, _filePathPAK.Length - Path.GetFileName(_filePathPAK).Length) + "LEVEL_TEXTURE_HEADERS.ALL.BIN";
            }
            else
            {
                _filePathBIN = _filePathPAK.Substring(0, _filePathPAK.Length - Path.GetFileName(_filePathPAK).Length) + "GLOBAL_TEXTURES_HEADERS.ALL.BIN";
            }
        }

        /* Load the contents of an existing TexturePAK */
        public override PAKReturnType Load()
        {
            if (!File.Exists(_filePathPAK))
            {
                return PAKReturnType.FAIL_TRIED_TO_LOAD_VIRTUAL_ARCHIVE;
            }
            
            try
            {
                #region TEXTURE_BIN
                /* First, parse the BIN and pull ALL info from it */
                BinaryReader bin = new BinaryReader(File.OpenRead(_filePathBIN));

                //Read the header info from the BIN
                if ((FileIdentifiers)bin.ReadInt32() != FileIdentifiers.TEXTURE_DATA) 
                    return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE;
                NumberOfEntriesBIN = bin.ReadInt32();
                HeaderListBeginBIN = bin.ReadInt32();

                //Read all file names from BIN and create texture entry
                for (int i = 0; i < NumberOfEntriesBIN; i++)
                {
                    CATHODE.Textures.TEX4 TextureEntry = new CATHODE.Textures.TEX4();
                    TextureEntry.FileName = CathodeLib.Utilities.ReadString(bin);
                    //TODO: maybe we should stop doing this & just do in AlienPAK instead
                    if (Path.GetExtension(TextureEntry.FileName).ToUpper() != ".DDS")
                        TextureEntry.FileName += ".dds";
                    _entries.Add(TextureEntry);
                }

                //Read the texture headers from the BIN
                bin.BaseStream.Position = HeaderListBeginBIN + 12;
                for (int i = 0; i < NumberOfEntriesBIN; i++)
                {
                    bin.BaseStream.Position += 4; //TEX4 magic
                    _entries[i].Format = (TextureFormat)bin.ReadInt32();
                    _entries[i].tex_HighRes.Length = bin.ReadInt32();
                    _entries[i].tex_LowRes.Length = bin.ReadInt32();
                    _entries[i].tex_LowRes.Width = bin.ReadInt16();
                    _entries[i].tex_LowRes.Height = bin.ReadInt16();
                    _entries[i].tex_HighRes.Depth = bin.ReadInt16();
                    _entries[i].tex_HighRes.Width = bin.ReadInt16();
                    _entries[i].tex_HighRes.Height = bin.ReadInt16();
                    _entries[i].tex_HighRes.Depth = bin.ReadInt16();
                    _entries[i].tex_LowRes.MipLevels = bin.ReadInt16();
                    _entries[i].tex_HighRes.MipLevels = bin.ReadInt16();
                    _entries[i].Type = bin.ReadInt32();
                    _entries[i].UnknownTexThing = (AlienUnknownTextureThing)bin.ReadInt16();
                    bin.BaseStream.Position += 2; //Always 2048
                    bin.BaseStream.Position += 4; //Skip filename offset value
                    bin.BaseStream.Position += 4; //Skip unused
                }
                bin.Close();
                #endregion

                #region TEXTURE_PAK
                /* Second, parse the PAK and pull ONLY header info from it - we'll pull textures when requested (to save memory) */
                BinaryReader pak = new BinaryReader(File.OpenRead(_filePathPAK));

                //Read & check the header info from the PAK
                pak.BaseStream.Position += 4; //Skip unused
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.ASSET_FILE) 
                    return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE;
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.TEXTURE_DATA) 
                    return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE;
                NumberOfEntriesPAK = BigEndianUtils.ReadInt32(pak);
                if (BigEndianUtils.ReadInt32(pak) != NumberOfEntriesPAK) 
                    return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR;
                pak.BaseStream.Position += 12; //Skip unused

                //Read the texture headers from the PAK
                for (int i = 0; i < NumberOfEntriesPAK; i++)
                {
                    pak.BaseStream.Position += 8; //Skip unused

                    int length = BigEndianUtils.ReadInt32(pak);
                    if (length != BigEndianUtils.ReadInt32(pak)) { return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; }
                    
                    int offset = BigEndianUtils.ReadInt32(pak);

                    pak.BaseStream.Position += 2; //Skip unused

                    int isHighRes = BigEndianUtils.ReadInt16(pak);

                    pak.BaseStream.Position += 2; //Skip unused

                    int val256 = BigEndianUtils.ReadInt16(pak); //always 256
                    if (val256 != 256) { return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; }

                    UInt32 unk1 = BigEndianUtils.ReadUInt32(pak);
                    UInt16 unk2 = BigEndianUtils.ReadUInt16(pak); 

                    int index = BigEndianUtils.ReadInt16(pak);

                    pak.BaseStream.Position += 4; //Skip unused

                    UInt32 unk3 = BigEndianUtils.ReadUInt32(pak);
                    UInt32 unk4 = BigEndianUtils.ReadUInt32(pak);

                    //Find the entry
                    CATHODE.Textures.TEX4_Part texFullRes = (isHighRes == 1) ? _entries[index].tex_HighRes : _entries[index].tex_LowRes;
                    if (length != texFullRes.Length) { return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; }

                    //Write out the info
                    texFullRes.Offset = offset;
                    texFullRes.unk1 = unk1;
                    texFullRes.unk2 = unk2;
                    texFullRes.unk3 = unk3;
                    texFullRes.unk4 = unk4;
                }

                //Close PAK
                pak.Close();
                #endregion
                
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { 
                return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE;
            }
            catch (Exception e) {
                Console.WriteLine(e.ToString());
                return PAKReturnType.FAIL_UNKNOWN; 
            }
        }

        /* Return a list of filenames for files in the TexturePAK archive */
        public override List<string> GetFileNames()
        {
            List<string> FileNameList = new List<string>();
            foreach (CATHODE.Textures.TEX4 ArchiveFile in _entries)
            {
                FileNameList.Add(ArchiveFile.FileName);
            }
            return FileNameList;
        }

        /* Get the file size of an archive entry */
        public override int GetFilesize(string FileName)
        {
            int FileIndex = GetFileIndex(FileName);
            if (FileIndex == -1) return -1; //CHANGED FOR OPENCAGE
            if (_entries[FileIndex].tex_HighRes.Length != -1)
            {
                return _entries[FileIndex].tex_HighRes.Length + 148;
            }
            return _entries[FileIndex].tex_LowRes.Length + 148;
            throw new Exception("Texture has no size! Fatal logic error.");
        }

        /* Find a file entry object by name */
        public override int GetFileIndex(string FileName)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].FileName == FileName || _entries[i].FileName == FileName.Replace('/', '\\'))
                {
                    return i;
                }
            }
            return -1; //CHANGED FOR OPENCAGE
        }

        /* Replace an existing file in the TexturePAK archive */
        //TODO: THIS FUNCTION DOES NOT CURRENTLY WORK!
        //      I NEED TO IMPLEMENT PULLING ALL PAK TEXTURE CONTENT, UPDATING WITH NEW, THEN SAVING IT BACK OUT
        //      CURRENTLY IT ONLY UPDATES HEADERS!
        public override PAKReturnType ReplaceFile(string PathToNewFile, string FileName)
        {
            try
            {
                int index = GetFileIndex(FileName);
                if (index == -1) return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR;

                //Update our internal knowledge of the textures
                //TODO: update low and high res - take lowest mip of high?
                DDSReader newTexture = new DDSReader(PathToNewFile);
                CATHODE.Textures.TEX4_Part texFullRes = _entries[index].tex_HighRes;
                if (texFullRes.Length == 0)
                    texFullRes = _entries[index].tex_LowRes;
                if (texFullRes.Length == 0)
                    return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; 
                texFullRes.Length = (int)newTexture.DataBlock.Length;
                texFullRes.Width = (Int16)newTexture.Width;
                texFullRes.Height = (Int16)newTexture.Height;
                _entries[index].Format = newTexture.Format;

                //Write BIN file
                BinaryWriter bin = new BinaryWriter(File.OpenWrite(_filePathBIN));
                bin.BaseStream.SetLength(0);
                bin.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
                for (int i = 0; i < _entries.Count; i++)
                {
                    CathodeLib.Utilities.Write<string>(bin, _entries[i].FileName); //TODO: does this need converting to char array?
                    bin.Write((byte)0x00);
                }
                int binHeaderStart = (int)bin.BaseStream.Position - 12;
                int binEntryCount = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    Utilities.WriteString("tex4", bin);
                    bin.Write(BitConverter.GetBytes((int)_entries[i].Format));
                    bin.Write(_entries[i].tex_HighRes.Length);
                    bin.Write(_entries[i].tex_LowRes.Length);
                    bin.Write(_entries[i].tex_LowRes.Width);
                    bin.Write(_entries[i].tex_LowRes.Height);
                    bin.Write(_entries[i].tex_LowRes.Depth);
                    bin.Write(_entries[i].tex_HighRes.Width);
                    bin.Write(_entries[i].tex_HighRes.Height);
                    bin.Write(_entries[i].tex_HighRes.Depth);
                    bin.Write(_entries[i].Type);
                    bin.Write((Int16)_entries[i].UnknownTexThing);
                    bin.Write((Int16)2048); //TODO: derive this from the actual texture
                    bin.Write(0); //TODO: this is filename offset, gen this from how we write!
                    bin.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                    binEntryCount++;
                }
                bin.BaseStream.Position = 0;
                bin.Write((int)FileIdentifiers.TEXTURE_DATA);
                bin.Write(binEntryCount);
                bin.Write(binHeaderStart); 
                bin.Close();

                //Update headers in PAK for all entries
                BinaryWriter pak = new BinaryWriter(File.OpenWrite(_filePathPAK));
                pak.BaseStream.Position = 32;
                int pakEntryCount = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        CATHODE.Textures.TEX4_Part currentRes = (x == 0) ? _entries[i].tex_LowRes : _entries[i].tex_HighRes;
                        if (currentRes.Length == 0) continue;
                        pak.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.Length));
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.Length));
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.Offset));
                        pak.Write(new byte[] { 0x00, 0x00 });
                        pak.Write((Int16)x); //isHighRes
                        pak.Write(new byte[] { 0x00, 0x00 });
                        pak.Write((Int16)256); //TODO: derive this from the actual texture
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.unk1));
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.unk2));
                        pak.Write(BigEndianUtils.FlipEndian((Int16)index));
                        pak.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.unk3));
                        pak.Write(BigEndianUtils.FlipEndian(currentRes.unk4));
                        pakEntryCount++;
                    }
                }
                //TODO: Pull all PAK content for textures & then rewrite properly using new info
                pak.Write(0);
                pak.Write((int)FileIdentifiers.ASSET_FILE);
                pak.Write((int)FileIdentifiers.TEXTURE_DATA);
                pak.Write(pakEntryCount);
                pak.Write(pakEntryCount);
                pak.Close();

                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }

        /* Export an existing file from the TexturePAK archive */
        public override PAKReturnType ExportFile(string PathToExport, string FileName)
        {
            try
            {
                //Get the texture index
                int FileIndex = GetFileIndex(FileName);
                if (FileIndex == -1) return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; //CHANGED FOR OPENCAGE

                //Get the biggest texture part stored
                CATHODE.Textures.TEX4_Part TexturePart = _entries[FileIndex].tex_HighRes;
                if (TexturePart.Length == 0)
                    TexturePart = _entries[FileIndex].tex_LowRes;
                if (TexturePart.Length == 0)
                    return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR;

                //Pull the texture part content from the PAK
                BinaryReader ArchiveFile = new BinaryReader(File.OpenRead(_filePathPAK));
                ArchiveFile.BaseStream.Position = (NumberOfEntriesPAK * 48) + 32 + TexturePart.Offset;
                byte[] TexturePartContent = ArchiveFile.ReadBytes(TexturePart.Length);
                ArchiveFile.Close();

                //Generate a DDS header based on the tex4's information
                DDSWriter TextureOutput;
                bool FailsafeSave = false;
                switch (_entries[FileIndex].Format)
                {
                    case TextureFormat.DXGI_FORMAT_BC5_UNORM:
                        TextureOutput = new DDSWriter(TexturePartContent, TexturePart.Width, TexturePart.Height, 32, 0, TextureType.ATI2N);
                        break;
                    case TextureFormat.DXGI_FORMAT_BC1_UNORM:
                        TextureOutput = new DDSWriter(TexturePartContent, TexturePart.Width, TexturePart.Height, 32, 0, TextureType.Dxt1);
                        break;
                    case TextureFormat.DXGI_FORMAT_BC3_UNORM:
                        TextureOutput = new DDSWriter(TexturePartContent, TexturePart.Width, TexturePart.Height, 32, 0, TextureType.Dxt5);
                        break;
                    case TextureFormat.DXGI_FORMAT_B8G8R8A8_UNORM:
                        TextureOutput = new DDSWriter(TexturePartContent, TexturePart.Width, TexturePart.Height, 32, 0, TextureType.UNCOMPRESSED_GENERAL);
                        break;
                    case TextureFormat.DXGI_FORMAT_BC7_UNORM:
                    default:
                        TextureOutput = new DDSWriter(TexturePartContent, TexturePart.Width, TexturePart.Height);
                        FailsafeSave = true;
                        break;
                }

                //Try and save out the part
                if (FailsafeSave)
                {
                    TextureOutput.SaveCrude(PathToExport);
                    return PAKReturnType.SUCCESS_WITH_WARNINGS;
                }
                TextureOutput.Save(PathToExport);
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }
    }
}
