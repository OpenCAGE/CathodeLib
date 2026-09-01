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

        //Lookups over Entries and _writeList. Both lists are only ever appended to or rebuilt
        //wholesale, so a count change is enough to spot one going stale; the rebuild sites null
        //the caches as well, for the case where a list is cleared and refilled to the same size.
        private Dictionary<(uint, uint), Resource> _byId = null;
        private int _byIdCount = -1;
        private Dictionary<Resource, int> _writeIndex = null;
        private int _writeIndexCount = -1;
        //Movers serialise in parallel and each one resolves its resource here.
        private readonly object _writeIndexLock = new object();

        ~Resources()
        {
            Entries.Clear();
            _writeList.Clear();
        }

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
            _writeIndex = null;
            return true;
        }

        override protected bool SaveInternal()
        {
            List<Resource> orderedEntries = Entries.OrderBy(o => o.composite_instance_id.AsUInt32).ThenBy(o => o.resource_id.AsUInt32).ToList();

            //The on-disk order is sorted but each entry stores its index in Entries, so work that
            //out once for the whole table - Entries.IndexOf per entry made saving quadratic (a
            //level with 18,480 resources spent 1.3s of its save here alone).
            Dictionary<Resource, int> indexInEntries = new Dictionary<Resource, int>(Entries.Count);
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i] != null && !indexInEntries.ContainsKey(Entries[i]))
                    indexInEntries[Entries[i]] = i;

            byte[][] entryBuffers = new byte[orderedEntries.Count][];
            Parallel.For(0, orderedEntries.Count, i =>
            {
                entryBuffers[i] = SerializeResourceEntry(orderedEntries[i], indexInEntries);
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
            _writeList.AddRange(Entries);
            _writeIndex = null;
            return true;
        }

        private byte[] SerializeResourceEntry(Resource resource, Dictionary<Resource, int> indexInEntries)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                Utilities.Write(writer, resource.composite_instance_id);
                Utilities.Write(writer, resource.resource_id);
                int index;
                writer.Write(indexInEntries.TryGetValue(resource, out index) ? index : -1);
                return stream.ToArray();
            }
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Rebuild the write-index lookup from the current entry list. The lookup is otherwise
        /// only refreshed at load and save, so resources appended mid-pipeline (instancing adds
        /// one per new renderable entity) resolve to -1 until the next save - which silently
        /// dropped every newly added composite instance from radiosity geometry collection.
        /// </summary>
        public void RefreshWriteList()
        {
            _writeList.Clear();
            _writeList.AddRange(Entries);
            _writeIndex = null;
        }

        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(Resource resource)
        {
            if (resource == null) return -1;
            lock (_writeIndexLock)
            {
                if (_writeIndex == null || _writeIndexCount != _writeList.Count)
                {
                    Dictionary<Resource, int> map = new Dictionary<Resource, int>(_writeList.Count);
                    for (int i = 0; i < _writeList.Count; i++)
                        if (_writeList[i] != null && !map.ContainsKey(_writeList[i]))
                            map[_writeList[i]] = i;
                    _writeIndex = map;
                    _writeIndexCount = _writeList.Count;
                }
                int index;
                return _writeIndex.TryGetValue(resource, out index) ? index : -1;
            }
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
            //Instancing calls this once per renderable entity and the table grows to tens of
            //thousands of rows, so the old scan of Entries was quadratic - 1.2s of Solace's pass.
            if (_byId == null || _byIdCount != Entries.Count)
            {
                Dictionary<(uint, uint), Resource> map = new Dictionary<(uint, uint), Resource>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    Resource existing = Entries[i];
                    if (existing == null) continue;
                    (uint, uint) existingKey = (existing.composite_instance_id.AsUInt32, existing.resource_id.AsUInt32);
                    if (!map.ContainsKey(existingKey))
                        map[existingKey] = existing;
                }
                _byId = map;
                _byIdCount = Entries.Count;
            }

            (uint, uint) key = (composite_instance_id.AsUInt32, resource_id.AsUInt32);
            Resource resource;
            if (_byId.TryGetValue(key, out resource))
                return resource;

            resource = new Resource()
            {
                composite_instance_id = composite_instance_id,
                resource_id = resource_id
            };
            Entries.Add(resource);
            _byId[key] = resource;
            _byIdCount = Entries.Count;
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