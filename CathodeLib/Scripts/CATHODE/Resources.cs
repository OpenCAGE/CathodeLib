using CATHODE.Scripting;
using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using static CATHODE.Movers;
using static CATHODE.Resources;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/RESOURCES.BIN
    /// </summary>
    public class Resources : CathodeFile
    {
        public List<Resource> Entries = new List<Resource>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public Resources(string path) : base(path) { }
        public Resources(MemoryStream stream, string path = "") : base(stream, path) { }
        public Resources(byte[] data, string path = "") : base(data, path) { }

        private List<Resource> _writeList = new List<Resource>(); 

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position = 8;
                int entryCount = reader.ReadInt32();
                reader.BaseStream.Position += 4;

                Resource[] entries = new Resource[entryCount];
                for (int i = 0; i < entryCount; i++)
                {
                    Resource resource = new Resource();
                    resource.composite_instance_id = Utilities.Consume<ShortGuid>(reader);
                    resource.resource_id = Utilities.Consume<ShortGuid>(reader); 
                    int index = reader.ReadInt32();
                    entries[index] = resource;
                }
                Entries = entries.ToList();
            }
            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            List<Resource> orderedEntries = Entries.OrderBy(o => o.composite_instance_id.AsUInt32).ThenBy(o => o.resource_id.AsUInt32).ToList();

            byte[][] entryBuffers = new byte[orderedEntries.Count][];
            Parallel.For(0, orderedEntries.Count, i =>
            {
                entryBuffers[i] = SerializeResourceEntry(orderedEntries[i]);
            });
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(new byte[4] { 0xCC, 0xBA, 0xED, 0xFE });
                writer.Write((Int32)1);
                writer.Write(orderedEntries.Count);
                writer.Write((Int32)0);
                for (int i = 0; i < entryBuffers.Length; i++)
                    writer.Write(entryBuffers[i]);
            }
            _writeList.Clear();
            _writeList.AddRange(orderedEntries);
            return true;
        }

        private byte[] SerializeResourceEntry(Resource resource)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                Utilities.Write(writer, resource.composite_instance_id);
                Utilities.Write(writer, resource.resource_id);
                writer.Write(Entries.IndexOf(resource));
                return stream.ToArray();
            }
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(Resource resource)
        {
            if (!_writeList.Contains(resource)) return -1;
            return _writeList.IndexOf(resource);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public Resource GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public Resource ImportEntry(Resource resource)
        {
            if (resource == null)
                return null;
            return AddUniqueResource(resource.resource_id, resource.composite_instance_id);
        }

        public Resource AddUniqueResource(ShortGuid resource_id, ShortGuid composite_instance_id)
        {
            Resource resource = Entries.FirstOrDefault(o => o.composite_instance_id == composite_instance_id && o.resource_id == resource_id);
            if (resource != null)
                return resource;

            resource = new Resource()
            {
                composite_instance_id = composite_instance_id,
                resource_id = resource_id
            };
            Entries.Add(resource);
            return resource;
        }
        #endregion

        #region STRUCTURES
        public class Resource : IComparable<Resource>, IEquatable<Resource>
        {
            public ShortGuid composite_instance_id;
            public ShortGuid resource_id;

            public int CompareTo(Resource other)
            {
                if (other == null) return 1;

                int compositeComparison = composite_instance_id.CompareTo(other.composite_instance_id);
                if (compositeComparison != 0)
                    return compositeComparison;

                return resource_id.CompareTo(other.resource_id);
            }

            public bool Equals(Resource other)
            {
                if (other == null) return false;
                return composite_instance_id.Equals(other.composite_instance_id) && 
                       resource_id.Equals(other.resource_id);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as Resource);
            }

            public override int GetHashCode()
            {
                int hash = 17;
                hash = hash * 31 + composite_instance_id.GetHashCode();
                hash = hash * 31 + resource_id.GetHashCode();
                return hash;
            }
        };
        #endregion
    }
}