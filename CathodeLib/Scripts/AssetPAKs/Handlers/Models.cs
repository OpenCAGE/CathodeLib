using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CathodeLib;

namespace CATHODE.Assets
{
    /*
     *
     * Model PAK handler.
     * Currently doesn't support import/export. WIP!
     * Also needs to verify the PAK version number, etc.
     * 
    */
    public class Models : AssetPAK
    {
        List<CS2> _metadata = new List<CS2>();
        List<AlienVBF> _vertexFormats = new List<AlienVBF>();

        public List<string> _filePaths;
        public List<string> _partNames;

        /* Initialise the ModelPAK class with the intended location (existing or not) */
        public Models(string PathToPAK)
        {
            _filePathPAK = PathToPAK;
            _filePathBIN = _filePathPAK.Substring(0, _filePathPAK.Length - Path.GetFileName(_filePathPAK).Length) + "MODELS_" + Path.GetFileName(_filePathPAK).Substring(0, Path.GetFileName(_filePathPAK).Length - 11) + ".BIN";
        }

        /* Load the contents of an existing ModelPAK */
        public override PAKReturnType Load()
        {
            if (!File.Exists(_filePathPAK))
            {
                return PAKReturnType.FAIL_TRIED_TO_LOAD_VIRTUAL_ARCHIVE;
            }

            /* TODO: Verify the PAK loading is a ModelPAK by BIN version number */

            try
            {
                #region MATERIAL
                //First, parse the MTL file to find material info
                string PathToMTL = _filePathPAK.Substring(0, _filePathPAK.Length - 3) + "MTL";
                BinaryReader ArchiveFileMtl = new BinaryReader(File.OpenRead(PathToMTL));

                //Header
                ArchiveFileMtl.BaseStream.Position += 40; //There are some knowns here, just not required for us yet
                int MaterialEntryCount = ArchiveFileMtl.ReadInt16();
                ArchiveFileMtl.BaseStream.Position += 2; //Skip unknown

                //Strings - more work will be done on materials eventually, 
                //but taking their names for now is good enough for model export
                List<string> MaterialEntries = new List<string>();
                string ThisMaterialString = "";
                for (int i = 0; i < MaterialEntryCount; i++)
                {
                    while (true)
                    {
                        byte ThisByte = ArchiveFileMtl.ReadByte();
                        if (ThisByte == 0x00)
                        {
                            MaterialEntries.Add(ThisMaterialString);
                            ThisMaterialString = "";
                            break;
                        }
                        ThisMaterialString += (char)ThisByte;
                    }
                }
                ArchiveFileMtl.Close();
                #endregion

                #region MODEL_BIN
                //Read the header info from BIN
                BinaryReader bin = new BinaryReader(File.OpenRead(_filePathBIN));
                bin.BaseStream.Position += 4; //Magic
                int modelCount = bin.ReadInt32();
                bin.BaseStream.Position += 4; //Unknown
                int vbfCount = bin.ReadInt32();

                //Read all vertex buffer formats
                _vertexFormats = new List<AlienVBF>(vbfCount);
                for (int EntryIndex = 0; EntryIndex < vbfCount; ++EntryIndex)
                {
                    long startPos = bin.BaseStream.Position;
                    int count = 1;
                    while (bin.ReadByte() != 0xFF)
                    {
                        bin.BaseStream.Position += Marshal.SizeOf(typeof(AlienVBFE)) - 1;
                        count++;
                    }
                    bin.BaseStream.Position = startPos;

                    AlienVBF VertexInput = new AlienVBF();
                    VertexInput.ElementCount = count;
                    VertexInput.Elements = CATHODE.Utilities.ConsumeArray<AlienVBFE>(bin, VertexInput.ElementCount).ToList();
                    _vertexFormats.Add(VertexInput);
                }

                //Read filename chunk
                byte[] filenames = bin.ReadBytes(bin.ReadInt32());

                //Read all model metadata
                _metadata = CATHODE.Utilities.ConsumeArray<CS2>(bin, modelCount).ToList();

                //Fetch filenames from chunk
                _filePaths = new List<string>();
                _partNames = new List<string>();
                for (int i = 0; i < _metadata.Count; ++i)
                {
                    _filePaths.Add(CATHODE.Utilities.ReadString(filenames, _metadata[i].FileNameOffset).Replace('\\', '/'));
                    _partNames.Add(CATHODE.Utilities.ReadString(filenames, _metadata[i].ModelPartNameOffset).Replace('\\', '/'));
                }

                //Read bone chunk
                byte[] BoneBuffer = bin.ReadBytes(bin.ReadInt32());
                //TODO: Parse bone chunk!

                bin.Close();
                #endregion

                #region MODEL_PAK
                BinaryReader pak = new BinaryReader(File.OpenRead(_filePathPAK));

                //Read & check the header info from the PAK
                pak.BaseStream.Position += 4; //Skip unused
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.ASSET_FILE)
                    return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE;
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.MODEL_DATA)
                    return PAKReturnType.FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE;
                int EntryCount = BinaryPrimitives.ReverseEndianness(pak.ReadInt32());
                if (BigEndianUtils.ReadInt32(pak) != EntryCount)
                    return PAKReturnType.FAIL_GENERAL_LOGIC_ERROR;
                pak.BaseStream.Position += 12; //Skip unused

                //Get PAK entry information
                List<int> lengths = new List<int>();
                for (int i = 0; i < EntryCount; i++)
                {
                    pak.BaseStream.Position += 8;
                    int Length = BinaryPrimitives.ReverseEndianness(pak.ReadInt32());
                    int DataLength = BinaryPrimitives.ReverseEndianness(pak.ReadInt32()); //TODO: Seems to be the aligned version of Length, aligned to 16 bytes.
                    int Offset = BinaryPrimitives.ReverseEndianness(pak.ReadInt32());
                    pak.BaseStream.Position += 12;
                    int UnknownIndex = BinaryPrimitives.ReverseEndianness(pak.ReadInt16());
                    int BINIndex = BinaryPrimitives.ReverseEndianness(pak.ReadInt16());
                    pak.BaseStream.Position += 12;

                    lengths.Add(DataLength);
                }

                //Pull all content from PAK
                List<byte[]> content = new List<byte[]>();
                foreach (int length in lengths)
                {
                    if (length == -1)
                        content.Add(new byte[] { });
                    else
                        content.Add(pak.ReadBytes(length));
                }
                pak.Close();
                #endregion

                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }

        /* Return a list of filenames for files in the ModelPAK archive */
        public override List<string> GetFileNames()
        {
            List<string> combinedFilenames = new List<string>();
            for (int i = 0; i < _filePaths.Count; i++)
            {
                if (combinedFilenames.Contains(_filePaths[i])) continue;
                combinedFilenames.Add(_filePaths[i]);
            }
            return combinedFilenames;
        }

        /* Get all CS2s (added for cross-ref support in OpenCAGE with CommandsPAK) */
        //public List<CS2> GetCS2s()
        //{
        //    return _metadata;
        //}

        /* Get entry by index (added for cross-ref support in OpenCAGE with CommandsPAK) */
        //public CS2 GetModelByIndex(int index)
        //{
        //    if (index < 0 || index >= _metadata.Count) return null;
        //    return _metadata[index];
        //}

        /* Get the selected model's submeshes and add up their sizes */
        public override int GetFilesize(string FileName)
        {
            //TODO: Need to look up by filepath and part name!

            //int TotalSize = 0;
            //foreach (CS2 ThisModel in _metadata)
            //{
            //    if (ThisModel.Filename == FileName.Replace("/", "\\"))
            //    {
            //        TotalSize += ThisModel.PakSize;
            //    }
            //}
            //return TotalSize;

            return -1;
        }

        /* Find the model entry object by name */
        public override int GetFileIndex(string FileName)
        {
            //TODO: Need to look up by filepath and part name!

            //for (int i = 0; i < _metadata.Count; i++)
            //{
            //    if (_metadata[i].Filename == FileName || _metadata[i].Filename == FileName.Replace('/', '\\'))
            //    {
            //        return i;
            //    }
            //}
            //throw new Exception("Could not find the requested file in ModelPAK!");

            return -1;
        }

        /* Export an existing file from the ModelPAK archive */
        public override PAKReturnType ExportFile(string PathToExport, string FileName)
        {
            return PAKReturnType.FAIL_FEATURE_IS_COMING_SOON; //Disabling export for main branch

            try
            {
                /*
                //Get the selected model's submeshes
                List<CS2> ModelSubmeshes = new List<CS2>();
                foreach (CS2 ThisModel in _metadata)
                {
                    if (ThisModel.Filename == FileName.Replace("/", "\\"))
                    {
                        ModelSubmeshes.Add(ThisModel);
                    }
                }

                //Extract each submesh into a CS2 folder by material and submesh name
                Directory.CreateDirectory(PathToExport);
                BinaryReader ArchiveFile = new BinaryReader(File.OpenRead(_filePathPAK));
                foreach (CS2 Submesh in ModelSubmeshes)
                {
                    ArchiveFile.BaseStream.Position = HeaderListEnd + Submesh.PakOffset;

                    string ThisExportPath = PathToExport;
                    if (Submesh.ModelPartName != "")
                    {
                        ThisExportPath = PathToExport + "/" + Submesh.ModelPartName;
                        Directory.CreateDirectory(ThisExportPath);
                    }
                    File.WriteAllBytes(ThisExportPath + "/" + Submesh.MaterialName, ArchiveFile.ReadBytes(Submesh.PakSize));
                }
                ArchiveFile.Close();
                */
                //Done!
                return PAKReturnType.SUCCESS;
            }
            catch
            {
                //Failed
                return PAKReturnType.FAIL_UNKNOWN;
            }
        }
    }
}
