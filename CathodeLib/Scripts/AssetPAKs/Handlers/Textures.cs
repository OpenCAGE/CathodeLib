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
                if (versionNumBIN != 45) { return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE; } //BIN version number is 45 for textures
                NumberOfEntriesBIN = bin.ReadInt32();
                HeaderListBeginBIN = bin.ReadInt32();

                //Read all file names from BIN
                string fileName = "";
                for (int i = 0; i < NumberOfEntriesBIN; i++)
                {
                    fileName = "";
                    for (byte b; (b = bin.ReadByte()) != 0x00;)
                    {
                        fileName += (char)b;
                    }
                    if (Path.GetExtension(fileName).ToUpper() != ".DDS")
                    {
                        fileName += ".dds";
                    }
                    //Create texture entry and add filename
                    TEX4 TextureEntry = new TEX4();
                    TextureEntry.FileName = fileName;
                    _entries.Add(TextureEntry);
                }

                //Read the texture headers from the BIN
                bin.BaseStream.Position = HeaderListBeginBIN + 12;
                for (int i = 0; i < NumberOfEntriesBIN; i++)
                {
                    _entries[i].HeaderPos = (int)bin.BaseStream.Position;
                    for (int x = 0; x < 4; x++) { _entries[i].Magic += bin.ReadChar(); }
                    _entries[i].Format = (TextureFormat)bin.ReadInt32();
                    _entries[i].Length_V2 = bin.ReadInt32();
                    _entries[i].Length_V1 = bin.ReadInt32();
                    _entries[i].Texture_V1.Width = bin.ReadInt16();
                    _entries[i].Texture_V1.Height = bin.ReadInt16();
                    _entries[i].Unk_V1 = bin.ReadInt16();
                    _entries[i].Texture_V2.Width = bin.ReadInt16();
                    _entries[i].Texture_V2.Height = bin.ReadInt16();
                    _entries[i].Unk_V2 = bin.ReadInt16();
                    _entries[i].UnknownHeaderBytes = bin.ReadBytes(20);
                }

                /* Second, parse the PAK and pull ONLY header info from it - we'll pull textures when requested (to save memory) */
                bin.Close();
                #endregion

                #region TEXTURE_PAK
                BinaryReader pak = new BinaryReader(File.OpenRead(_filePathPAK));

                //Read the header info from the PAK
                pak.BaseStream.Position += 4; //Skip nulls
                int versionNumPAK = BigEndianUtils.ReadInt32(pak);
                if (BigEndianUtils.ReadInt32(pak) != versionNumBIN) { throw new Exception("Archive version mismatch!"); }
                NumberOfEntriesPAK = BigEndianUtils.ReadInt32(pak);
                if (BigEndianUtils.ReadInt32(pak) != NumberOfEntriesPAK) { throw new Exception("PAK entry count mismatch!"); }
                pak.BaseStream.Position += 12; //Skip unknowns (1,1,1)

                //Read the texture headers from the PAK
                int OffsetTracker = (NumberOfEntriesPAK * 48) + 32;
                for (int i = 0; i < NumberOfEntriesPAK; i++)
                {
                    //Header indexes are out of order, so optimise replacements by saving position
                    int HeaderPosition = (int)pak.BaseStream.Position;

                    //Pull the entry info
                    byte[] UnknownHeaderLead = pak.ReadBytes(8);
                    int PartLength = BigEndianUtils.ReadInt32(pak);
                    if (PartLength != BigEndianUtils.ReadInt32(pak)) { continue; }
                    byte[] UnknownHeaderTrail_1 = pak.ReadBytes(18);

                    //Find the entry
                    TEX4 TextureEntry = _entries[BigEndianUtils.ReadInt16(pak)];
                    TEX4_Part TexturePart = (!TextureEntry.Texture_V1.Saved) ? TextureEntry.Texture_V1 : TextureEntry.Texture_V2;

                    //Write out the info
                    TexturePart.HeaderPos = HeaderPosition;
                    TexturePart.StartPos = OffsetTracker;
                    TexturePart.UnknownHeaderLead = UnknownHeaderLead;
                    TexturePart.Length = PartLength;
                    TexturePart.Saved = true;
                    TexturePart.UnknownHeaderTrail_1 = UnknownHeaderTrail_1;
                    TexturePart.UnknownHeaderTrail_2 = pak.ReadBytes(12);

                    //Keep file offset updated
                    OffsetTracker += TexturePart.Length;
                }

                //Close PAK
                pak.Close();
                #endregion

                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
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
            if (_entries[FileIndex].Texture_V2.Saved)
            {
                return _entries[FileIndex].Texture_V2.Length + 148;
            }
            //Fallback to V1 if this texture has no V2
            else if (_entries[FileIndex].Texture_V1.Saved)
            {
                return _entries[FileIndex].Texture_V1.Length + 148;
            }
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
                TEX4_Part BiggestPart = TextureEntry.Texture_V2;
                if (BiggestPart.HeaderPos == -1 || !BiggestPart.Saved)
                {
                    BiggestPart = TextureEntry.Texture_V1;
                }
                if (BiggestPart.HeaderPos == -1 || !BiggestPart.Saved)
                {
                    return PAKReturnType.FAIL_REQUEST_IS_UNSUPPORTED; //Shouldn't reach this.
                }

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
                BinaryWriter ArchiveFileBinWriter = new BinaryWriter(File.OpenWrite(_filePathBIN));
                ArchiveFileBinWriter.BaseStream.Position = TextureEntry.HeaderPos;
                ExtraBinaryUtils.WriteString(TextureEntry.Magic, ArchiveFileBinWriter);
                ArchiveFileBinWriter.Write(BitConverter.GetBytes((int)TextureEntry.Format));
                ArchiveFileBinWriter.Write((TextureEntry.Texture_V2.Length == -1) ? 0 : TextureEntry.Texture_V2.Length);
                ArchiveFileBinWriter.Write(TextureEntry.Texture_V1.Length);
                ArchiveFileBinWriter.Write(TextureEntry.Texture_V1.Width);
                ArchiveFileBinWriter.Write(TextureEntry.Texture_V1.Height);
                ArchiveFileBinWriter.Write(TextureEntry.Unk_V1);
                ArchiveFileBinWriter.Write(TextureEntry.Texture_V2.Width);
                ArchiveFileBinWriter.Write(TextureEntry.Texture_V2.Height);
                ArchiveFileBinWriter.Write(TextureEntry.Unk_V2);
                ArchiveFileBinWriter.Write(TextureEntry.UnknownHeaderBytes);
                ArchiveFileBinWriter.Close();

                //Update headers for V1+2 in PAK if they exist
                BinaryWriter ArchiveFileWriter = new BinaryWriter(File.OpenWrite(_filePathPAK));
                if (TextureEntry.Texture_V1.HeaderPos != -1)
                {
                    ArchiveFileWriter.BaseStream.Position = TextureEntry.Texture_V1.HeaderPos;
                    ArchiveFileWriter.Write(TextureEntry.Texture_V1.UnknownHeaderLead);
                    ArchiveFileWriter.Write(BigEndianUtils.FlipEndian(BitConverter.GetBytes(TextureEntry.Texture_V1.Length)));
                    ArchiveFileWriter.Write(BigEndianUtils.FlipEndian(BitConverter.GetBytes(TextureEntry.Texture_V1.Length)));
                    ArchiveFileWriter.Write(TextureEntry.Texture_V1.UnknownHeaderTrail_1);
                    ArchiveFileWriter.Write(BigEndianUtils.FlipEndian(BitConverter.GetBytes((Int16)EntryIndex)));
                    ArchiveFileWriter.Write(TextureEntry.Texture_V1.UnknownHeaderTrail_2);
                }
                if (TextureEntry.Texture_V2.HeaderPos != -1)
                {
                    ArchiveFileWriter.BaseStream.Position = TextureEntry.Texture_V2.HeaderPos;
                    ArchiveFileWriter.Write(TextureEntry.Texture_V2.UnknownHeaderLead);
                    ArchiveFileWriter.Write(BigEndianUtils.FlipEndian(BitConverter.GetBytes(TextureEntry.Texture_V2.Length)));
                    ArchiveFileWriter.Write(BigEndianUtils.FlipEndian(BitConverter.GetBytes(TextureEntry.Texture_V2.Length)));
                    ArchiveFileWriter.Write(TextureEntry.Texture_V2.UnknownHeaderTrail_1);
                    ArchiveFileWriter.Write(BigEndianUtils.FlipEndian(BitConverter.GetBytes((Int16)EntryIndex)));
                    ArchiveFileWriter.Write(TextureEntry.Texture_V2.UnknownHeaderTrail_2);
                }
                ArchiveFileWriter.Close();

                //Pull PAK sections before/after V2
                BinaryReader ArchiveFile = new BinaryReader(File.OpenRead(_filePathPAK));
                byte[] PAK_Pt1 = ArchiveFile.ReadBytes(BiggestPart.StartPos);
                ArchiveFile.BaseStream.Position += OriginalLength;
                byte[] PAK_Pt2 = ArchiveFile.ReadBytes((int)ArchiveFile.BaseStream.Length - (int)ArchiveFile.BaseStream.Position);
                ArchiveFile.Close();

                //Write the PAK back out with new content
                ArchiveFileWriter = new BinaryWriter(File.OpenWrite(_filePathPAK));
                ArchiveFileWriter.BaseStream.SetLength(0);
                ArchiveFileWriter.Write(PAK_Pt1);
                ArchiveFileWriter.Write(NewTexture.DataBlock);
                ArchiveFileWriter.Write(PAK_Pt2);
                ArchiveFileWriter.Close();

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
                TEX4_Part TexturePart;
                if (_entries[FileIndex].Texture_V2.Saved)
                {
                    TexturePart = _entries[FileIndex].Texture_V2;
                }
                else if (_entries[FileIndex].Texture_V1.Saved)
                {
                    TexturePart = _entries[FileIndex].Texture_V1;
                }
                else
                {
                    return PAKReturnType.FAIL_REQUEST_IS_UNSUPPORTED;
                }

                //Pull the texture part content from the PAK
                BinaryReader ArchiveFile = new BinaryReader(File.OpenRead(_filePathPAK));
                ArchiveFile.BaseStream.Position = TexturePart.StartPos;
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
