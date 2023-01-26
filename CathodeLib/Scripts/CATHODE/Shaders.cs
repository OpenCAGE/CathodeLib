using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/RENDERABLE/LEVEL_SHADERS_DX11.PAK & LEVEL_SHADERS_DX11_BIN.PAK & LEVEL_SHADERS_DX11_IDX_REMAP.PAK */
    public class Shaders : CathodeFile
    {
        public static new Implementation Implementation = Implementation.NONE;
        public Shaders(string path) : base(path) { }

        private string _filepathBIN;
        private string _filepathIDX;

        #region FILE_IO
        override protected bool LoadInternal()
        {
            string trimmed = _filepath.Substring(0, _filepath.Length - 4);
            _filepathBIN = trimmed + "_BIN.PAK";
            _filepathIDX = trimmed + "_IDX_REMAP.PAK";

            if (!File.Exists(_filepathBIN)) return false;
            if (!File.Exists(_filepathIDX)) return false;

            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepathBIN)))
            {
                reader.BaseStream.Position = 4; //skip magic
                if ((FileIdentifiers)reader.ReadInt32() != FileIdentifiers.ASSET_FILE) return false;
                if ((FileIdentifiers)reader.ReadInt32() != FileIdentifiers.SHADER_DATA) return false;
                int binEntryCount = reader.ReadInt32();
                if (BigEndianUtils.ReadInt32(reader) != binEntryCount) return false;

                //Skip rest of the main header
                reader.BaseStream.Position = 32;

                //Pull each entry's individual header
                List<CathodeShaderHeader> _header = new List<CathodeShaderHeader>();
                for (int i = 0; i < binEntryCount; i++)
                {
                    CathodeShaderHeader newStringEntry = new CathodeShaderHeader();
                    reader.BaseStream.Position += 8; //skip blanks

                    newStringEntry.FileLength = reader.ReadInt32();
                    newStringEntry.FileLengthWithPadding = reader.ReadInt32();
                    newStringEntry.FileOffset = reader.ReadInt32();

                    reader.BaseStream.Position += 8; //skip blanks

                    newStringEntry.StringPart1 = reader.ReadBytes(4);
                    newStringEntry.FileIndex = reader.ReadInt32(); //potentially actually int8 or int16 not 32

                    reader.BaseStream.Position += 8; //skip blanks

                    newStringEntry.StringPart2 = reader.ReadBytes(4);

                    //TEMP: For now I'm just setting the filename to be the index... need to work out how the _BIN relates to the initial .PAK to get names, etc
                    newStringEntry.FileName = newStringEntry.FileIndex + ".DXBC";
                    //END OF TEMP

                    _header.Add(newStringEntry);
                }
                int endOfHeaders = (int)reader.BaseStream.Position;

                //Pull each entry's file content
                foreach (CathodeShaderHeader shaderEntry in _header)
                {
                    reader.BaseStream.Position = shaderEntry.FileOffset + endOfHeaders;
                    shaderEntry.FileContent = reader.ReadBytes(shaderEntry.FileLength);
                }
            }

            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepathIDX)))
            {

            }

            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4; //Skip unused
                if ((FileIdentifiers)reader.ReadInt32() != FileIdentifiers.ASSET_FILE) return false;
                if ((FileIdentifiers)reader.ReadInt32() != FileIdentifiers.SHADER_DATA) return false;
                int pakEntryCount = reader.ReadInt32();
                if (BigEndianUtils.ReadInt32(reader) != pakEntryCount) return false;
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {

            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class CathodeShaderHeader
        {
            public string FileName = ""; //The name of the file in the shader archive (unsure how to get this right now with the weird _BIN/PAK way of working)

            public int FileLength = 0; //The length of the file in the archive for this header
            public int FileLengthWithPadding = 0; //The length of the file in the archive for this header, with any padding at the end of the file included

            public int FileOffset = 0; //Position in archive from end of header list
            public int FileIndex = 0; //The index of the file

            public byte[] FileContent; //The content for the file

            public byte[] StringPart1; //4 bytes that look like they're part of a filepath
            public byte[] StringPart2; //4 bytes that look like they're part of a filepath
        }
        #endregion
    }
}