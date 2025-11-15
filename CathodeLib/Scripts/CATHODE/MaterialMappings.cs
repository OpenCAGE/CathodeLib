using CATHODE.Scripting;
using CathodeLib;
using System.Collections.Generic;
using System.IO;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/MATERIAL_MAPPINGS.PAK
    /// </summary>
    public class MaterialMappings : CathodeFile
    {
        public List<MaterialMapping> Entries = new List<MaterialMapping>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public MaterialMappings(string path) : base(path) { }
        public MaterialMappings(MemoryStream stream, string path = "") : base(stream, path) { }
        public MaterialMappings(byte[] data, string path = "") : base(data, path) { }

        //This is always the start of the mapping filepath - remove it for ease when adding new ones
        private const string _path = "n:/content/build/library/_material_libraries_/mappings/";

        //NOTE: REDS/MVR is written by remapping the materials via these defs, from the original model values off Commands data.
        //      But it's not only used offline. At runtime it's used to remap Havok physics materials.

        private List<MaterialMapping> _writeList = new List<MaterialMapping>();

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 8; //magic, version
                int entryCount = reader.ReadInt32();

                for (int x = 0; x < entryCount; x++)
                {
                    MaterialMapping entry = new MaterialMapping();
                    reader.BaseStream.Position += 4; //shortguid hash of filename (useful?)
                    int count = reader.ReadInt32();
                    reader.BaseStream.Position += 4; //this is to->from id count, stored last, but always empty
                    int strLength = reader.ReadInt32();
                    entry.Name = Utilities.ReadString(reader.ReadBytes(strLength));
                    entry.Name = entry.Name.Substring(_path.Length, entry.Name.Length - 4 - _path.Length);
                    for (int p = 0; p < count; p++)
                    {
                        MaterialMapping.Mapping mapping = new MaterialMapping.Mapping();
                        strLength = reader.ReadInt32();
                        mapping.from = Utilities.ReadString(reader.ReadBytes(strLength));
                        strLength = reader.ReadInt32();
                        mapping.to = Utilities.ReadString(reader.ReadBytes(strLength));
                        entry.Mappings.Add(mapping);
                    }
                    Entries.Add(entry);
                }
            }
            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(new byte[4] { 0xAE, 0xB0, 0xEB, 0xDE });
                writer.Write(4);
                writer.Write(Entries.Count);
                foreach (MaterialMapping entry in Entries)
                {
                    string fullPath = _path + entry.Name + ".xml";
                    Utilities.Write(writer, ShortGuidUtils.Generate(fullPath, false));
                    writer.Write(entry.Mappings.Count);
                    writer.Write(0);
                    writer.Write(fullPath.Length);
                    Utilities.WriteString(fullPath, writer);
                    foreach (MaterialMapping.Mapping mapping in entry.Mappings)
                    {
                        writer.Write(mapping.from.Length);
                        Utilities.WriteString(mapping.from, writer);
                        writer.Write(mapping.to.Length);
                        Utilities.WriteString(mapping.to, writer);
                    }
                }
            }
            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(MaterialMapping map)
        {
            if (!_writeList.Contains(map)) return -1;
            return _writeList.IndexOf(map);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public MaterialMapping GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }
        #endregion

        #region STRUCTURES
        public class MaterialMapping
        {
            public string Name;
            public List<Mapping> Mappings = new List<Mapping>();

            public class Mapping
            {
                public string from;
                public string to;

                public override string ToString()
                {
                    return from + "->" + to;
                }
            }

            public override string ToString()
            {
                return Name + " [" + Mappings.Count + "]";
            }
        }
        #endregion
    }
}