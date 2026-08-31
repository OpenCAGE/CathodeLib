using CATHODE.Scripting;
using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CATHODE.CollisionMaps;

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

        //Where each mapping sits in the file, for the per-row lookups CollisionMaps does when it
        //saves. MaterialMapping hashes its Mappings list by reference while == compares it by value,
        //so the comparer below hashes the name and the count instead.
        private Dictionary<MaterialMapping, int> _writeIndex = null;
        private int _writeIndexCount = 0;
        private readonly object _writeIndexLock = new object();

        private sealed class MappingIdentity : IEqualityComparer<MaterialMapping>
        {
            public static readonly MappingIdentity Instance = new MappingIdentity();
            public bool Equals(MaterialMapping x, MaterialMapping y) { return x == y; }
            public int GetHashCode(MaterialMapping mapping)
            {
                if (mapping == null) return 0;
                int hash = mapping.Name == null ? 0 : mapping.Name.GetHashCode();
                hash = hash * 31 + (mapping.Mappings == null ? 0 : mapping.Mappings.Count);
                return hash;
            }
        }

        ~MaterialMappings()         
        {
            Entries.Clear();
            _writeList.Clear();
            _writeIndex = null;
        }

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
                    reader.BaseStream.Position += 4; //shortguid hash of filename - we use our own, calculated below
                    int count = reader.ReadInt32();
                    reader.BaseStream.Position += 4; //this is to->from id count, stored last, but always empty
                    int strLength = reader.ReadInt32();
                    entry.Name = TrimMappingString(Utilities.ReadString(reader.ReadBytes(strLength)));
                    entry.ID = GenerateMappingID(entry.Name, true);
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
            _writeIndex = null;
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
            _writeIndex = null;
            _writeList.AddRange(Entries);
            _writeIndex = null;
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
            if (map == null) return -1;

            /* The lookup is built under the lock but read outside it: the Commands writers resolve
             * rows to indices from a Parallel.For, and holding a lock for every one of those was
             * slower than the scan it replaced. The dictionary is built complete and then published
             * in one assignment, so a reader always sees a finished one. */
            Dictionary<MaterialMapping, int> index = _writeIndex;
            if (index == null || _writeIndexCount != _writeList.Count)
            {
                lock (_writeIndexLock)
                {
                    if (_writeIndex == null || _writeIndexCount != _writeList.Count)
                    {
                        Dictionary<MaterialMapping, int> built = new Dictionary<MaterialMapping, int>(_writeList.Count, MappingIdentity.Instance);
                        for (int i = 0; i < _writeList.Count; i++)
                        {
                            MaterialMapping entry = _writeList[i];
                            if (entry != null && !built.ContainsKey(entry))
                                built[entry] = i;
                        }
                        _writeIndexCount = _writeList.Count;
                        _writeIndex = built;
                    }
                    index = _writeIndex;
                }
            }

            int found;
            return index.TryGetValue(map, out found) ? found : -1;
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

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// Dedupes by <see cref="MaterialMapping.Name"/>. When <paramref name="overwriteExisting"/> is true, replaces any existing entry with the same name.
        /// </summary>
        public MaterialMapping ImportEntry(MaterialMapping matMap, bool overwriteExisting = false)
        {
            if (matMap == null)
                return null;

            MaterialMapping existingByName = Entries.FirstOrDefault(o => o.Name == matMap.Name);
            if (existingByName != null && !overwriteExisting)
                return existingByName;

            MaterialMapping newMatMap = matMap.Copy();
            if (existingByName != null)
            {
                Entries[Entries.IndexOf(existingByName)] = newMatMap;
                return newMatMap;
            }

            Entries.Add(newMatMap);
            return newMatMap;
        }

        /// <summary>
        /// Trims a material mapping name to remove the absolute path and extension
        /// </summary>
        public static string TrimMappingString(string str)
        {
            if (str.ToLower().Replace("\\", "/").StartsWith(_path.ToLower()) && str.ToLower().EndsWith(".xml"))
                return str.Substring(_path.Length, str.Length - 4 - _path.Length);
            return str;
        }

        /// <summary>
        /// Generates a material mapping ID from the mapping name (this logic differs to the game's id generation, but works better for us)
        /// </summary>
        public static ShortGuid GenerateMappingID(string str, bool alreadyTrimmed = false)
        {
            return ShortGuidUtils.Generate((alreadyTrimmed ? str : TrimMappingString(str)).Replace("/", "\\").ToUpper());
        }
        #endregion

        #region STRUCTURES
        public class MaterialMapping : IEquatable<MaterialMapping>
        {
            public ShortGuid ID;
            public string Name;
            public List<Mapping> Mappings = new List<Mapping>();

            public static bool operator ==(MaterialMapping x, MaterialMapping y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.Name != y.Name) return false;
                if (!ListsEqual(x.Mappings, y.Mappings)) return false;
                return true;
            }

            public static bool operator !=(MaterialMapping x, MaterialMapping y)
            {
                return !(x == y);
            }

            public bool Equals(MaterialMapping other)
            {
                return this == other;
            }

            public override bool Equals(object obj)
            {
                return obj is MaterialMapping mapping && this == mapping;
            }

            public override int GetHashCode()
            {
                int hashCode = -1234567890;
                hashCode = hashCode * -1521134295 + (Name?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (Mappings?.GetHashCode() ?? 0);
                return hashCode;
            }

            private static bool ListsEqual(List<Mapping> x, List<Mapping> y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                if (x.Count != y.Count) return false;
                for (int i = 0; i < x.Count; i++)
                {
                    if (x[i] != y[i]) return false;
                }
                return true;
            }

            public class Mapping : IEquatable<Mapping>
            {
                public string from;
                public string to;

                public static bool operator ==(Mapping x, Mapping y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                    if (x.from != y.from) return false;
                    if (x.to != y.to) return false;
                    return true;
                }

                public static bool operator !=(Mapping x, Mapping y)
                {
                    return !(x == y);
                }

                public bool Equals(Mapping other)
                {
                    return this == other;
                }

                public override bool Equals(object obj)
                {
                    return obj is Mapping mapping && this == mapping;
                }

                public override int GetHashCode()
                {
                    int hashCode = -1234567890;
                    hashCode = hashCode * -1521134295 + (from?.GetHashCode() ?? 0);
                    hashCode = hashCode * -1521134295 + (to?.GetHashCode() ?? 0);
                    return hashCode;
                }

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