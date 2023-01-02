using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using CathodeLib;

namespace CATHODE.Assets
{
    /*
     *
     * Material Mapping PAK handler.
     * Supports the ability to edit the material remapping PAK file full of stripped XMLs. We produce our own XML to handle the remapping data.
     * Works similar to the PAK2 class with the Save() method, however the format still requires understanding of a couple odd binary tags to be able to reconstruct from scratch fully.
     * TODO comments mark the binary tags that still need to be figured out. I'm guessing they work similar to the unique ID tags in the COMMANDS.PAK.
     * 
    */
    public class MaterialMapping : AssetPAK
    {
        List<EntryMaterialMappingsPAK> _entries = new List<EntryMaterialMappingsPAK>();
        byte[] _headerJunk = new byte[8];

        /* Initialise the MaterialMapPAK class with the intended location (existing or not) */
        public MaterialMapping(string PathToPAK)
        {
            _filePathPAK = PathToPAK;
        }

        /* Load the contents of an existing MaterialMapPAK */
        public override PAKReturnType Load()
        {
            if (!File.Exists(_filePathPAK))
            {
                return PAKReturnType.FAIL_TRIED_TO_LOAD_VIRTUAL_ARCHIVE;
            }

            try
            {
                //Open PAK
                BinaryReader pak = new BinaryReader(File.OpenRead(_filePathPAK));

                //Parse header
                _headerJunk = pak.ReadBytes(8); //TODO: Work out what this contains
                int entryCount = pak.ReadInt32();

                //Parse entries (XML is broken in the build files - doesn't get shipped)
                for (int x = 0; x < entryCount; x++)
                {
                    //This entry
                    EntryMaterialMappingsPAK entry = new EntryMaterialMappingsPAK();
                    entry.MapHeader = pak.ReadBytes(4); //TODO: Work out the significance of this value, to be able to construct new PAKs from scratch.
                    entry.MapEntryCoupleCount = pak.ReadInt32();
                    entry.MapJunk = pak.ReadBytes(4); //TODO: Work out if this is always null.
                    for (int p = 0; p < (entry.MapEntryCoupleCount * 2) + 1; p++)
                    {
                        //String
                        int length = pak.ReadInt32();
                        string materialString = "";
                        for (int i = 0; i < length; i++)
                        {
                            materialString += pak.ReadChar();
                        }

                        //First string is filename, others are materials
                        if (p == 0)
                        {
                            entry.MapFilename = materialString;
                        }
                        else
                        {
                            entry.MapMatEntries.Add(materialString);
                        }
                    }
                    _entries.Add(entry);
                }

                //Done!
                pak.Close();
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }

        /* Return a list of filenames for files in the MaterialMapPAK archive */
        public override List<string> GetFileNames()
        {
            List<string> FileNameList = new List<string>();
            foreach (EntryMaterialMappingsPAK MapEntry in _entries)
            {
                FileNameList.Add(MapEntry.MapFilename);
            }
            return FileNameList;
        }

        /* Get the rough size of a material mapping entry based on its entries */
        public override int GetFilesize(string FileName)
        {
            int size = 0;
            foreach (string MatMap in _entries[GetFileIndex(FileName)].MapMatEntries)
            {
                size += MatMap.Length;
            }
            return size;
        }

        /* Find the entry object by name */
        public override int GetFileIndex(string FileName)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].MapFilename == FileName || _entries[i].MapFilename == FileName.Replace('/', '\\'))
                {
                    return i;
                }
            }
            throw new Exception("Could not find the requested file in CommandPAK!");
        }

        /* Deconstruct a given XML and update material override info accordingly */
        public override PAKReturnType ReplaceFile(string PathToNewFile, string FileName)
        {
            try
            { 
                //Pull the new mapping info from the import XML
                XDocument InputFile = XDocument.Load(PathToNewFile);
                EntryMaterialMappingsPAK MaterialMapping = _entries[GetFileIndex(FileName)];
                List<string> NewOverrides = new List<string>();
                foreach (XElement ThisMap in InputFile.Element("material_mappings").Elements())
                {
                    for (int i = 0; i < 2; i++)
                    {
                        XElement ThisElement = ThisMap.Elements().ElementAt(i);
                        string ThisMaterial = ThisElement.Value;
                        if (ThisElement.Attribute("arrow").Value == "true") { ThisMaterial = ThisMaterial + "->" + ThisMaterial; }
                        NewOverrides.Add(ThisMaterial);
                    }
                }

                //Apply the pulled info
                MaterialMapping.MapMatEntries = NewOverrides;
                MaterialMapping.MapEntryCoupleCount = MaterialMapping.MapMatEntries.Count / 2; //Should probably error if this is different.
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }

        /* Construct an XML with given material info, and export it */
        public override PAKReturnType ExportFile(string PathToExport, string FileName)
        {
            try
            {
                XDocument OutputFile = XDocument.Parse("<material_mappings></material_mappings>");
                XElement MaterialPair = XElement.Parse("<map><original arrow='false'></original><override arrow='false'></override></map>");

                int ThisEntryNum = 0;
                EntryMaterialMappingsPAK ThisEntry = _entries[GetFileIndex(FileName)];
                for (int i = 0; i < ThisEntry.MapEntryCoupleCount; i++)
                {
                    for (int x = 0; x < 2; x++)
                    {
                        string ThisMaterial = ThisEntry.MapMatEntries[ThisEntryNum]; ThisEntryNum++;
                        XElement ThisElement = MaterialPair.Elements().ElementAt(x);
                        if (ThisMaterial.Contains("->"))
                        {
                            //Remove the arrow split format as both sides are the same, and XML will web-ify the ">"
                            ThisMaterial = ThisMaterial.Split(new string[] { "->" }, StringSplitOptions.None)[0];
                            ThisElement.Attribute("arrow").Value = "true";
                        }
                        else
                        {
                            //Don't think there are any without the arrow format, but just in-case.
                            ThisElement.Attribute("arrow").Value = "false";
                        }
                        ThisElement.Value = ThisMaterial;
                    }
                    OutputFile.Element("material_mappings").Add(MaterialPair);
                }

                OutputFile.Save(PathToExport);
                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }

        /* Save out our material mappings archive */
        public override PAKReturnType Save()
        {
            try
            {
                //Re-write out to the PAK
                BinaryWriter pak = new BinaryWriter(File.OpenWrite(_filePathPAK));
                pak.BaseStream.SetLength(0);
                pak.Write(_headerJunk);
                pak.Write(_entries.Count);
                foreach (EntryMaterialMappingsPAK entry in _entries)
                {
                    pak.Write(entry.MapHeader);
                    pak.Write(entry.MapEntryCoupleCount);
                    pak.Write(entry.MapJunk);
                    pak.Write(entry.MapFilename.Length);
                    ExtraBinaryUtils.WriteString(entry.MapFilename, pak);
                    foreach (string name in entry.MapMatEntries)
                    {
                        pak.Write(name.Length);
                        ExtraBinaryUtils.WriteString(name, pak);
                    }
                }
                pak.Close();

                return PAKReturnType.SUCCESS;
            }
            catch (IOException) { return PAKReturnType.FAIL_COULD_NOT_ACCESS_FILE; }
            catch (Exception) { return PAKReturnType.FAIL_UNKNOWN; }
        }
    }
}
