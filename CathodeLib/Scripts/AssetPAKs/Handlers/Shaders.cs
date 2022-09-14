using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;

namespace CATHODE.Assets
{
    /*
     *
     * Shader PAK handler.
     * Currently doesn't support import/export. WIP!
     * Also doesn't parse much info out, although the basic file structure is there for the initial PAK.
     * Work needs to be done on parsing the _BIN and how that links to the initial PAK.
     * 
    */
    public class Shaders : AssetPAK
    {
        List<CathodeShaderHeader> _header = new List<CathodeShaderHeader>();
        private string _filePathIdxRemap = "";

        /* Initialise the ShaderPAK class with the intended location (existing or not) */
        public Shaders(string PathToPAK)
        {
            _filePathPAK = PathToPAK;
            string trimmed = PathToPAK.Substring(0, PathToPAK.Length - Path.GetFileName(PathToPAK).Length) + Path.GetFileNameWithoutExtension(_filePathPAK);
            _filePathBIN = trimmed + "_BIN.PAK";
            _filePathIdxRemap = trimmed + "_IDX_REMAP.PAK";
        }

        /* Load the contents of an existing ShaderPAK set (massive WIP) */
        public override PAKReturnType Load()
        {
            if (!File.Exists(_filePathPAK))
            {
                return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE;
            }
            
            try
            {
                //The main PAK is unhandled for now, as I can't work out how the main (initial) PAK relates to the _BIN.PAK. Entry counts are different, and no noticeable indexes!
                //Open PAK
                BinaryReader pak = new BinaryReader(File.OpenRead(_filePathPAK));

                //Read & check the header info from the PAK
                pak.BaseStream.Position += 4; //Skip unused
                if ((FileIdentifiers)pak.ReadInt32() != FileIdentifiers.ASSET_FILE)
                    return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE;
                if ((FileIdentifiers)pak.ReadInt32() != FileIdentifiers.SHADER_DATA)
                    return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE;
                int pakEntryCount = pak.ReadInt32();
                if (BigEndianUtils.ReadInt32(pak) != pakEntryCount)
                    return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR;

                /*
                //TODO: usually we skip 12 bytes here as this bit is unused, but in shader PAKs it seems 8 bytes are used

                pak.BaseStream.Position += 12; //Skip unused


                //Read shader headers
                for (int i = 0; i < NumOfStringPairs; i++)
                {
                    CathodeShaderHeader newHeaderEntry = new CathodeShaderHeader();

                    //Get the name (or type) of this header
                    pak.BaseStream.Position += 32;
                    byte[] entryNameOrType = pak.ReadBytes(40);
                    for (int x = 0; x < entryNameOrType.Length; x++)
                    {
                        if (entryNameOrType[x] != 0x00)
                        {
                            newHeaderEntry.ShaderType += (char)entryNameOrType[x];
                            continue;
                        }
                        break;
                    }

                    //Skip over the rest for now just so I can scrape all the names (this needs parsing obvs)
                    for (int x = 0; x < 999999; x++)
                    {
                        if (pak.ReadBytes(4).SequenceEqual(new byte[] { 0xA4, 0xBB, 0x25, 0x77 }))
                        {
                            pak.BaseStream.Position -= 4;
                            break;
                        }
                    }

                    HeaderDump.Add(newHeaderEntry);
                }

                //Done!
                */
                pak.Close();

                BinaryReader bin = new BinaryReader(File.OpenRead(_filePathBIN));

                bin.BaseStream.Position = 4; //skip magic
                if ((FileIdentifiers)pak.ReadInt32() != FileIdentifiers.ASSET_FILE)
                    return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE;
                if ((FileIdentifiers)pak.ReadInt32() != FileIdentifiers.SHADER_DATA)
                    return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE;

                //Read entry count from header
                int binEntryCount = bin.ReadInt32();
                if (BigEndianUtils.ReadInt32(pak) != binEntryCount)
                    return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR;

                //TODO: the other info here seems to mirror the extra data used in the other shader PAK which is usually unused

                //Skip rest of the main header
                bin.BaseStream.Position = 32;

                //Pull each entry's individual header
                for (int i = 0; i < binEntryCount; i++)
                {
                    CathodeShaderHeader newStringEntry = new CathodeShaderHeader();
                    bin.BaseStream.Position += 8; //skip blanks
                    
                    newStringEntry.FileLength = bin.ReadInt32();
                    newStringEntry.FileLengthWithPadding = bin.ReadInt32();
                    newStringEntry.FileOffset = bin.ReadInt32(); 

                    bin.BaseStream.Position += 8; //skip blanks

                    newStringEntry.StringPart1 = bin.ReadBytes(4);
                    newStringEntry.FileIndex = bin.ReadInt32(); //potentially actually int8 or int16 not 32

                    bin.BaseStream.Position += 8; //skip blanks

                    newStringEntry.StringPart2 = bin.ReadBytes(4);

                    //TEMP: For now I'm just setting the filename to be the index... need to work out how the _BIN relates to the initial .PAK to get names, etc
                    newStringEntry.FileName = newStringEntry.FileIndex + ".DXBC";
                    //END OF TEMP

                    _header.Add(newStringEntry);
                }
                int endOfHeaders = (int)bin.BaseStream.Position;

                //Pull each entry's file content
                foreach (CathodeShaderHeader shaderEntry in _header)
                {
                    bin.BaseStream.Position = shaderEntry.FileOffset + endOfHeaders;
                    shaderEntry.FileContent = bin.ReadBytes(shaderEntry.FileLength);
                }

                bin.Close();
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }

        /* Return a list of filenames for shaders in the ShaderPAK archive (massive WIP) */
        public override List<string> GetFileNames()
        {
            List<string> FileNameList = new List<string>();
            foreach (CathodeShaderHeader shaderEntry in _header)
            {
                FileNameList.Add(shaderEntry.FileName);
            }
            return FileNameList;
        }

        /* Get the size of the requested shader (not yet implemented) */
        public override int GetFilesize(string FileName)
        {
            return _header[GetFileIndex(FileName)].FileLength;
        }

        /* Find the shader entry object by name (not yet implemented) */
        public override int GetFileIndex(string FileName)
        {
            for (int i = 0; i < _header.Count; i++)
            {
                if (_header[i].FileName == FileName)
                {
                    return i;
                }
            }
            throw new Exception("Could not find the requested file in ShaderPAK!");
        }

        /* Export a file from the ShaderPAK */
        public override PAKReturnType ExportFile(string PathToExport, string FileName)
        {
            try
            {
                File.WriteAllBytes(PathToExport, _header[GetFileIndex(FileName)].FileContent);
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }
    }
}
