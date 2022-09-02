using System;
using System.Collections.Generic;
using System.IO;
using CathodeLib;

namespace CATHODE.Assets
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
        private List<TEX4> _entries = new List<TEX4>();
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
                int versionNumBIN = bin.ReadInt32();
                if (versionNumBIN != 45) { return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE; } 
                NumberOfEntriesBIN = bin.ReadInt32();
                HeaderListBeginBIN = bin.ReadInt32();

                //Read all file names from BIN and create texture entry
                for (int i = 0; i < NumberOfEntriesBIN; i++)
                {
                    TEX4 TextureEntry = new TEX4();
                    TextureEntry.FileName = CATHODE.Utilities.ReadString(bin);
                    if (Path.GetExtension(TextureEntry.FileName).ToUpper() != ".DDS")
                        TextureEntry.FileName += ".dds";
                    _entries.Add(TextureEntry);
                }

                //Read the texture headers from the BIN
                bin.BaseStream.Position = HeaderListBeginBIN + 12;
                for (int i = 0; i < NumberOfEntriesBIN; i++)
                {
                    Console.WriteLine(i);
                    _entries[i].HeaderPos = (int)bin.BaseStream.Position;
                    for (int x = 0; x < 4; x++) { _entries[i].Magic += bin.ReadChar(); }
                    _entries[i].Format = (TextureFormat)bin.ReadInt32();
                    _entries[i].tex_HighRes.Length = bin.ReadInt32(); //is this defo not 1?
                    _entries[i].tex_LowRes.Length = bin.ReadInt32(); //is this defo not 2?
                    _entries[i].tex_LowRes.Width = bin.ReadInt16();
                    _entries[i].tex_LowRes.Height = bin.ReadInt16();
                    _entries[i].tex_HighRes.Bit = bin.ReadInt16();
                    _entries[i].tex_HighRes.Width = bin.ReadInt16();
                    _entries[i].tex_HighRes.Height = bin.ReadInt16();
                    _entries[i].tex_HighRes.Bit = bin.ReadInt16();
                    _entries[i].tex_LowRes.MipLevels = bin.ReadInt16();
                    _entries[i].tex_HighRes.MipLevels = bin.ReadInt16();
                    _entries[i].Type = bin.ReadInt32();
                    _entries[i].UnknownTexThing = (AlienUnknownTextureThing)bin.ReadInt16();
                    bin.BaseStream.Position += 2; //Always 2048
                    _entries[i].FileNameOffset = bin.ReadInt32();
                    bin.BaseStream.Position += 4;
                }

                /* Second, parse the PAK and pull ONLY header info from it - we'll pull textures when requested (to save memory) */
                bin.Close();
                #endregion

                #region TEXTURE_PAK
                BinaryReader pak = new BinaryReader(File.OpenRead(_filePathPAK));

                //Read the header info from the PAK
                pak.BaseStream.Position += 4; //Skip unused
                int versionNumPAK = BigEndianUtils.ReadInt32(pak);
                if (versionNumPAK != 14) { return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE; } 
                if (BigEndianUtils.ReadInt32(pak) != versionNumBIN) { return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; }
                NumberOfEntriesPAK = BigEndianUtils.ReadInt32(pak);
                if (BigEndianUtils.ReadInt32(pak) != NumberOfEntriesPAK) { return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; }
                pak.BaseStream.Position += 12; //Skip unused

                List<string> debug_dump = new List<string>();
            
                //Read the texture headers from the PAK
                int offsetTracker = /*(NumberOfEntriesPAK * 48) + 32*/0;
                for (int i = 0; i < NumberOfEntriesPAK; i++)
                {
                    //Header indexes are out of order, so optimise replacements by saving position
                    int position = (int)pak.BaseStream.Position;

                    //Pull the entry info
                    pak.BaseStream.Position += 8; //Skip unused

                    int length = BigEndianUtils.ReadInt32(pak);
                    if (length != BigEndianUtils.ReadInt32(pak)) { return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; }
                    
                    int offset = BigEndianUtils.ReadInt32(pak);

                    pak.BaseStream.Position += 2; //Skip unused

                    int isHighRes = BigEndianUtils.ReadInt16(pak);

                    pak.BaseStream.Position += 2; //Skip unused

                    int two_five_six = BigEndianUtils.ReadInt16(pak); //always 256
                    if (two_five_six != 256) { return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; }

                    UInt32 unk1 = BigEndianUtils.ReadUInt32(pak);
                    UInt16 unk2 = BigEndianUtils.ReadUInt16(pak); //this is zero unless isHighRes = 1

                    int index = BigEndianUtils.ReadInt16(pak);

                    pak.BaseStream.Position += 4; //Skip unused

                    UInt32 unk3 = BigEndianUtils.ReadUInt32(pak);
                    UInt32 unk4 = BigEndianUtils.ReadUInt32(pak);

                    //Find the entry
                    TEX4 TextureEntry = _entries[index];
                    TEX4_Part TexturePart = (isHighRes == 1) ? TextureEntry.tex_HighRes : TextureEntry.tex_LowRes;
                    if (length != TexturePart.Length) { return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; }

                    //Write out the info
                    TexturePart.HeaderPos = position;
                    TexturePart.Offset = offset;
                    TexturePart.unk1 = unk1;
                    TexturePart.unk2 = unk2;
                    TexturePart.unk3 = unk3;
                    TexturePart.unk4 = unk4;
                }
                File.WriteAllLines("out.csv", debug_dump);

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
            foreach (TEX4 ArchiveFile in _entries)
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
        public override PAKReturnType ReplaceFile(string PathToNewFile, string FileName)
        {
            try
            {
                //Get the texture entry & parse new DDS
                int EntryIndex = GetFileIndex(FileName);
                if (EntryIndex == -1) return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; //CHANGED FOR OPENCAGE
                TEX4 TextureEntry = _entries[EntryIndex];
                DDSReader NewTexture = new DDSReader(PathToNewFile);

                //Currently we only apply the new texture to the "biggest", some have lower mips that we don't edit (TODO)
                TEX4_Part BiggestPart = TextureEntry.tex_HighRes;
                if (BiggestPart.HeaderPos == -1)
                    BiggestPart = TextureEntry.tex_LowRes;
                if (BiggestPart.HeaderPos == -1)
                    return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR; 

                //CATHODE seems to ignore texture header information regarding size, so as default, resize any imported textures to the original size.
                //An option is provided in the toolkit to write size information to the header (done above) however, so don't resize if that's the case.
                //More work needs to be done to figure out why CATHODE doesn't honour the header's size value.
                int OriginalLength = BiggestPart.Length;
                Array.Resize(ref NewTexture.DataBlock, OriginalLength);

                //Update our internal knowledge of the textures
                BiggestPart.Length = (int)NewTexture.DataBlock.Length;
                BiggestPart.Width = (Int16)NewTexture.Width;
                BiggestPart.Height = (Int16)NewTexture.Height;
                TextureEntry.Format = NewTexture.Format;
                //TODO: Update smallest here too if it exists!
                //Will need to be written into the PAK at "Pull PAK sections before/after V2" too - headers are handled already.

                //Load the BIN and write out updated BIN texture header
                BinaryWriter bin = new BinaryWriter(File.OpenWrite(_filePathBIN));
                bin.BaseStream.Position = TextureEntry.HeaderPos;
                ExtraBinaryUtils.WriteString(TextureEntry.Magic, bin);
                bin.Write(BitConverter.GetBytes((int)TextureEntry.Format));
                bin.Write((TextureEntry.tex_HighRes.Length == -1) ? 0 : TextureEntry.tex_HighRes.Length);
                bin.Write(TextureEntry.tex_LowRes.Length);
                bin.Write(TextureEntry.tex_LowRes.Width);
                bin.Write(TextureEntry.tex_LowRes.Height);
                bin.Write(TextureEntry.tex_LowRes.Bit);
                bin.Write(TextureEntry.tex_HighRes.Width);
                bin.Write(TextureEntry.tex_HighRes.Height);
                bin.Write(TextureEntry.tex_HighRes.Bit);
                bin.Write(TextureEntry.Type);
                bin.Write(2048); //TODO: derive this from the actual texture
                bin.Write(TextureEntry.FileNameOffset); //TODO: gen this from how we write
                bin.Write(new byte[] {0x00, 0x00, 0x00, 0x00}); //Padding
                bin.Close();

                //Update headers for V1+2 in PAK if they exist
                BinaryWriter pak = new BinaryWriter(File.OpenWrite(_filePathPAK));
                if (TextureEntry.tex_LowRes.HeaderPos != -1)
                {
                    pak.BaseStream.Position = TextureEntry.tex_LowRes.HeaderPos;
                    pak.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_LowRes.Length));
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_LowRes.Length));
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_LowRes.Offset));
                    pak.Write(new byte[] { 0x00, 0x00 });
                    pak.Write((Int16)0); //isHighRes
                    pak.Write(new byte[] { 0x00, 0x00 });
                    pak.Write((Int16)256); //TODO: derive this from the actual texture
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_LowRes.unk1));
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_LowRes.unk2));
                    pak.Write(BigEndianUtils.FlipEndian((Int16)EntryIndex));
                    pak.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_LowRes.unk3));
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_LowRes.unk4));
                }
                if (TextureEntry.tex_HighRes.HeaderPos != -1)
                {
                    pak.BaseStream.Position = TextureEntry.tex_HighRes.HeaderPos;
                    pak.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_HighRes.Length));
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_HighRes.Length));
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_HighRes.Offset));
                    pak.Write(new byte[] { 0x00, 0x00 });
                    pak.Write((Int16)1); //isHighRes
                    pak.Write(new byte[] { 0x00, 0x00 });
                    pak.Write((Int16)256); //TODO: derive this from the actual texture
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_HighRes.unk1));
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_HighRes.unk2));
                    pak.Write(BigEndianUtils.FlipEndian((Int16)EntryIndex));
                    pak.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_HighRes.unk3));
                    pak.Write(BigEndianUtils.FlipEndian(TextureEntry.tex_HighRes.unk4));
                }
                pak.Close();

                //Pull PAK sections before/after V2
                BinaryReader ArchiveFile = new BinaryReader(File.OpenRead(_filePathPAK));
                byte[] PAK_Pt1 = ArchiveFile.ReadBytes((NumberOfEntriesPAK * 48) + 32 + BiggestPart.Offset);
                ArchiveFile.BaseStream.Position += OriginalLength;
                byte[] PAK_Pt2 = ArchiveFile.ReadBytes((int)ArchiveFile.BaseStream.Length - (int)ArchiveFile.BaseStream.Position);
                ArchiveFile.Close();

                //Write the PAK back out with new content
                pak = new BinaryWriter(File.OpenWrite(_filePathPAK));
                pak.BaseStream.SetLength(0);
                pak.Write(PAK_Pt1);
                pak.Write(NewTexture.DataBlock);
                pak.Write(PAK_Pt2);
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
                TEX4_Part TexturePart = _entries[FileIndex].tex_HighRes;
                if (TexturePart.HeaderPos == -1)
                    TexturePart = _entries[FileIndex].tex_LowRes;
                if (TexturePart.HeaderPos == -1)
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
