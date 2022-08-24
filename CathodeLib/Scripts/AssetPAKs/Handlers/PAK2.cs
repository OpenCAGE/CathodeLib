using System;
using System.Collections.Generic;
using System.IO;
using CathodeLib;

namespace CATHODE.Assets
{
    /*
     *
     * PAK2 handler.
     * Allows write/read of a PAK2 archive. Completed!
     * 
    */
    public class PAK2 : AssetPAK
    {
        private List<EntryPAK2> _entries = new List<EntryPAK2>();

        /* Initialise the PAK2 class with the intended PAK2 location (existing or not) */
        public PAK2(string PathToPAK)
        {
            _filePathPAK = PathToPAK;
        }

        /* Load the contents of an existing PAK2 */
        public override PAKReturnType Load()
        {
            if (!File.Exists(_filePathPAK))
            {
                return PAKReturnType.FAIL_TRIED_TO_LOAD_VIRTUAL_ARCHIVE;
            }

            try
            {
                //Open PAK
                BinaryReader ArchiveFile = new BinaryReader(File.OpenRead(_filePathPAK));

                //Read the header info
                string MagicValidation = "";
                for (int i = 0; i < 4; i++) { MagicValidation += ArchiveFile.ReadChar(); }
                if (MagicValidation != "PAK2") { ArchiveFile.Close(); return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE; }
                int offsetListBegin = ArchiveFile.ReadInt32() + 16;
                int entryCount = ArchiveFile.ReadInt32();
                ArchiveFile.BaseStream.Position += 4; //Skip "4"

                //Read all file names and create entries
                for (int i = 0; i < entryCount; i++)
                {
                    string ThisFileName = "";
                    for (byte b; (b = ArchiveFile.ReadByte()) != 0x00;)
                    {
                        ThisFileName += (char)b;
                    }

                    EntryPAK2 NewPakFile = new EntryPAK2();
                    NewPakFile.Filename = ThisFileName;
                    _entries.Add(NewPakFile);
                }

                //Read all file offsets
                ArchiveFile.BaseStream.Position = offsetListBegin;
                List<int> FileOffsets = new List<int>();
                FileOffsets.Add(offsetListBegin + (entryCount * 4));
                for (int i = 0; i < entryCount; i++)
                {
                    FileOffsets.Add(ArchiveFile.ReadInt32());
                    _entries[i].Offset = FileOffsets[i];
                }

                //Read in the files to entries
                for (int i = 0; i < entryCount; i++)
                {
                    //Must pass to RemoveLeadingNulls as each file starts with 0-3 null bytes to align files to a 4-byte block reader
                    _entries[i].Content = ExtraBinaryUtils.RemoveLeadingNulls(ArchiveFile.ReadBytes(FileOffsets[i + 1] - FileOffsets[i]));
                }

                //Close PAK
                ArchiveFile.Close();
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }

        /* Return a list of filenames for files in the PAK2 archive */
        public override List<string> GetFileNames()
        {
            List<string> FileNameList = new List<string>();
            foreach (EntryPAK2 ArchiveFile in _entries)
            {
                FileNameList.Add(ArchiveFile.Filename);
            }
            return FileNameList;
        }

        /* Get the file size of an archive entry */
        public override int GetFilesize(string FileName)
        {
            return _entries[GetFileIndex(FileName)].Content.Length;
        }

        /* Find the a file entry object by name */
        public override int GetFileIndex(string FileName)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Filename == FileName || _entries[i].Filename == FileName.Replace('/', '\\'))
                {
                    return i;
                }
            }
            throw new Exception("Could not find the requested file in PAK2! Fatal logic error.");
        }

        /* Add a file to the PAK2 */
        public override PAKReturnType AddFile(string PathToNewFile, int TrimFromPath = 0) //TrimFromPath is available to leave some directory trace in the filename
        {
            try
            {
                EntryPAK2 NewFile = new EntryPAK2();
                if (TrimFromPath == 0) { NewFile.Filename = Path.GetFileName(PathToNewFile).ToUpper(); } //Virtual directory support here would be nice too
                else { NewFile.Filename = PathToNewFile.Substring(TrimFromPath).ToUpper(); } //Easy to fail here, so be careful on function usage!
                NewFile.Content = File.ReadAllBytes(PathToNewFile);
                _entries.Add(NewFile);
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }

        /* Delete a file from the PAK2 */
        public override PAKReturnType DeleteFile(string FileName)
        {
            try
            {
                _entries.RemoveAt(GetFileIndex(FileName));
                return PAKReturnType.SUCCESS;
            }
            catch
            {
                return PAKReturnType.FAIL_UNKNOWN;
            }
        }

        /* Replace an existing file in the PAK2 archive */
        public override PAKReturnType ReplaceFile(string PathToNewFile, string FileName)
        {
            try
            {
                _entries[GetFileIndex(FileName)].Content = File.ReadAllBytes(PathToNewFile);
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }

        /* Export an existing file from the PAK2 archive */
        public override PAKReturnType ExportFile(string PathToExport, string FileName)
        {
            try
            {
                File.WriteAllBytes(PathToExport, _entries[GetFileIndex(FileName)].Content);
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }

        /* Save out our PAK2 archive */
        public override PAKReturnType Save()
        {
            try
            {
                //Open/create PAK2 for writing
                BinaryWriter ArchiveFileWrite;
                if (File.Exists(_filePathPAK))
                {
                    ArchiveFileWrite = new BinaryWriter(File.OpenWrite(_filePathPAK));
                    ArchiveFileWrite.BaseStream.SetLength(0);
                }
                else
                {
                    ArchiveFileWrite = new BinaryWriter(File.Create(_filePathPAK));
                }

                //Write header
                ExtraBinaryUtils.WriteString("PAK2", ArchiveFileWrite);
                int OffsetListBegin_New = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    OffsetListBegin_New += _entries[i].Filename.Length + 1;
                }
                ArchiveFileWrite.Write(OffsetListBegin_New);
                ArchiveFileWrite.Write(_entries.Count);
                ArchiveFileWrite.Write(4);

                //Write filenames
                for (int i = 0; i < _entries.Count; i++)
                {
                    ExtraBinaryUtils.WriteString(_entries[i].Filename.Replace("\\", "/"), ArchiveFileWrite);
                    ArchiveFileWrite.Write((byte)0x00);
                }

                //Write placeholder offsets for now, we'll correct them after writing the content
                int offsetListBegin = (int)ArchiveFileWrite.BaseStream.Position;
                for (int i = 0; i < _entries.Count; i++)
                {
                    ArchiveFileWrite.Write(0);
                }

                //Write files
                for (int i = 0; i < _entries.Count; i++)
                {
                    while (ArchiveFileWrite.BaseStream.Position % 4 != 0)
                    {
                        ArchiveFileWrite.Write((byte)0x00);
                    }
                    ArchiveFileWrite.Write(_entries[i].Content);
                    _entries[i].Offset = (int)ArchiveFileWrite.BaseStream.Position;
                }

                //Re-write offsets with correct values
                ArchiveFileWrite.BaseStream.Position = offsetListBegin;
                for (int i = 0; i < _entries.Count; i++)
                {
                    ArchiveFileWrite.Write(_entries[i].Offset);
                }

                ArchiveFileWrite.Close();
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }
    }
}
