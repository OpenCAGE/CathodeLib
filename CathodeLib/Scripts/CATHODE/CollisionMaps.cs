using CATHODE.Scripting;
using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static CATHODE.Movers;

namespace CATHODE
{
    //This file defines additional info for entities with COLLISION_MAPPING resources.

    /// <summary>
    /// DATA/ENV/x/WORLD/COLLISION.MAP
    /// </summary>
    public class CollisionMaps : CathodeFile
    {
        public List<COLLISION_MAPPING> Entries = new List<COLLISION_MAPPING>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        protected override bool HandlesLoadingManually => true;
        private Materials _materials;
        private MaterialMappings _materialMaps;

        private List<COLLISION_MAPPING> _writeList = new List<COLLISION_MAPPING>();

        public CollisionMaps(string path, Materials materials, MaterialMappings materialMaps) : base(path)
        {
            _materials = materials;
            _materialMaps = materialMaps;

            _loaded = Load();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                //The way this works:
                // - First 18 entries are empty
                // - Next set of entries are all the COLLISION_MAPPING resources referenced by COMMANDS.PAK (hence they have no composite_instance_id, as the composites aren't instanced - but they do have entity_ids)
                // - There are then a few entries that have composite_instance_ids set but I can't resolve them - perhaps these are things from GLOBAL?
                // - Then there's all the instanced entities with resolvable composite_instance_ids

                reader.BaseStream.Position = 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    COLLISION_MAPPING entry = new COLLISION_MAPPING();
                    entry.Flags = (CollisionFlags)reader.ReadInt32();
                    entry.Index = reader.ReadInt32();
                    entry.ResourceGUID = Utilities.Consume<ShortGuid>(reader);
                    entry.Entity = Utilities.Consume<EntityHandle>(reader);
                    entry.Material = _materials.GetAtWriteIndex(reader.ReadInt32());
                    entry.CollisionProxyIndex = reader.ReadInt16();
                    entry.MaterialMapping = _materialMaps.GetAtWriteIndex(reader.ReadInt16());
                    entry.ZoneID = Utilities.Consume<ShortGuid>(reader);
                    reader.BaseStream.Position += 16;
                    Entries.Add(entry);
                }
            }
            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            //composite_instance_id defo has something to do with the ordering as all the zeros are first

            //Entries = Entries.OrderBy(o => o.entity.entity_id.ToUInt32() + o.id.ToUInt32()).ThenBy(o => o.entity.composite_instance_id.ToUInt32()).ThenBy(o => o.zone_id.ToUInt32()).ToList();

            byte[][] entryBuffers = new byte[Entries.Count][];
            Parallel.For(0, Entries.Count, i =>
            {
                entryBuffers[i] = SerializeEntry(Entries[i]);
            });

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write((Entries.Count) * 48);
                writer.Write(Entries.Count);
                for (int i = 0; i < entryBuffers.Length; i++)
                    writer.Write(entryBuffers[i]);
            }
            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }

        private byte[] SerializeEntry(COLLISION_MAPPING entry)
        {
            using (MemoryStream stream = new MemoryStream(48)) 
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((int)entry.Flags);
                writer.Write(entry.Index);
                Utilities.Write<ShortGuid>(writer, entry.ResourceGUID);
                Utilities.Write<EntityHandle>(writer, entry.Entity);
                writer.Write(_materials.GetWriteIndex(entry.Material));
                writer.Write((Int16)entry.CollisionProxyIndex);
                writer.Write((Int16)_materialMaps.GetWriteIndex(entry.MaterialMapping));
                Utilities.Write<ShortGuid>(writer, entry.ZoneID);
                writer.Write(new byte[16]);
                return stream.ToArray();
            }
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(COLLISION_MAPPING colMap)
        {
            if (!_writeList.Contains(colMap)) return -1;
            return _writeList.IndexOf(colMap);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public COLLISION_MAPPING GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public COLLISION_MAPPING ImportEntry(COLLISION_MAPPING colMap)
        {
            if (colMap == null)
                return null;

            COLLISION_MAPPING newColMap = colMap.Copy();

            newColMap.Material = _materials.ImportEntry(newColMap.Material);
            newColMap.MaterialMapping = _materialMaps.ImportEntry(newColMap.MaterialMapping);

            //todo: set zone to global?

            var existing = Entries.FirstOrDefault(o => o == newColMap);
            if (existing != null)
                return existing;

            Entries.Add(newColMap);
            return newColMap;
        }
        #endregion

        #region STRUCTURES
        public class COLLISION_MAPPING : IEquatable<COLLISION_MAPPING>
        {
            public CollisionFlags Flags = 0;
            public int Index = -1; //Compound shape index for static and ballistic collision 

            public ShortGuid ResourceGUID = ShortGuid.Invalid; //This is the name of the entity hashed via ShortGuid
            public EntityHandle Entity = new EntityHandle();

            public Materials.Material Material = null;

            public int CollisionProxyIndex = -1; // Index in COLLISION.HKX (hkpStaticCompoundShape)
            public MaterialMappings.MaterialMapping MaterialMapping = null; //This remaps the material to the physics material for Havok

            public ShortGuid ZoneID = ShortGuid.Invalid; //this maps the entity to a zone ID. interestingly, this seems to be the point of truth for the zone rendering

            public static bool operator ==(COLLISION_MAPPING x, COLLISION_MAPPING y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.Flags != y.Flags) return false;
                if (x.Index != y.Index) return false;
                if (x.ResourceGUID != y.ResourceGUID) return false;
                if (x.Entity != y.Entity) return false;
                if (x.Material != y.Material) return false;
                if (x.CollisionProxyIndex != y.CollisionProxyIndex) return false;
                if (x.MaterialMapping != y.MaterialMapping) return false;
                if (x.ZoneID != y.ZoneID) return false;
                return true;
            }
            public static bool operator !=(COLLISION_MAPPING x, COLLISION_MAPPING y)
            {
                return !(x == y);
            }

            public bool Equals(COLLISION_MAPPING other)
            {
                return this == other;
            }

            public override bool Equals(object obj)
            {
                return obj is COLLISION_MAPPING entry && this == entry;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 1001543423;
                    hashCode = hashCode * -1521134295 + Flags.GetHashCode();
                    hashCode = hashCode * -1521134295 + Index.GetHashCode();
                    hashCode = hashCode * -1521134295 + ResourceGUID.GetHashCode();
                    hashCode = hashCode * -1521134295 + (Entity?.GetHashCode() ?? 0);
                    hashCode = hashCode * -1521134295 + (Material?.GetHashCode() ?? 0);
                    hashCode = hashCode * -1521134295 + CollisionProxyIndex.GetHashCode();
                    hashCode = hashCode * -1521134295 + (MaterialMapping?.GetHashCode() ?? 0);
                    hashCode = hashCode * -1521134295 + ZoneID.GetHashCode();
                    return hashCode;
                }
            }
        };

        [Flags]
        public enum CollisionFlags : uint
        {
            //Type of collider
            STANDARD = 0x00000010,
            PHANTOM = 0x00000020, //trigger volume
            DYNAMIC = 0x00000030,
            PATHFINDING = 0x00000040,
            CAMERA = 0x00000050,
            SOUND = 0x00000060,
            USER_INTERFACE = 0x00000070,
            PLAYER = 0x00000080,
            COLLISION_TYPE_MASK = 0x0000001F,

            //Way the collider is stored 
            LANDSCAPE = 0x00000020,  //landscapeShape
            WORLD = 0x00000040,  //compoundShape
            BALLISTIC = 0x00000080,  //ballisticShape
            STORAGE_TYPE_MASK = 0x000000E0,

            //Way the collider moves
            FIXED = 0x00000100, //static
            KEYFRAMED = 0x00000200, //by animation
            SIMULATING = 0x00000400, //by physics
            MOTION_TYPE_MASK = 0x00000F00,

            //Where the collider comes from
            PREBUILT = 0x00001000, //baked from level compile
            RESOURCE = 0x00002000, //temporary from a resource
            SYSTEM = 0x00004000, //part of a physics system
            SCRIPT = 0x00008000, //temporary from a script entity
            SOURCE_TYPE_MASK = 0x0000F000,

            //The collider's state
            GHOSTED = 0x10000000, //no collision (cannot simulate either)
            PRE_GHOSTED = 0x20000000, //ghosted on start
            FROZEN = 0x40000000, //cannot simulate
            PRE_FROZEN = 0x80000000, //frozen on start
            REMOVED = 0x01000000,
            FORCE_KEYFRAMED = 0x02000000, //never simulates
            BALLISTIC_ONLY = 0x04000000,
            STANDARD_ONLY = 0x08000000,
            PRE_ZERO_GRAVITY = 0x00100000, //reports sliding events
            SOFT_COLLISION = 0x00200000, //reports sliding events
            REPORT_SLIDING = 0x00400000, //reports sliding events
            IS_SUBMERGED = 0x00800000,
            ZERO_GRAVITY = 0x00010000, //gravity has been modified
            REPORTING = 0x00020000,  //animated trigger has moved/toggled
            FORCE_TRANSPARENT = 0x00040000,
            HIGH_PRIORITY = 0x00080000, //priority ui collision
            STATE_MASK = 0xFFFF0000,
        };
        #endregion
    }
}