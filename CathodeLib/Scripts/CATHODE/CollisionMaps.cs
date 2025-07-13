using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CATHODE
{
    //This file defines additional info for entities with COLLISION_MAPPING resources.

    /* DATA/ENV/PRODUCTION/x/WORLD/COLLISION.MAP */
    public class CollisionMaps : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public CollisionMaps(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            int minUnk1 = 0;
            int minUnk2 = 0;
            int minColIn = 0;

            List<int> flags = new List<int>();
            Dictionary<string, List<string>> dictest = new Dictionary<string, List<string>>();

            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
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
                    Entry entry = new Entry();
                    entry.Flags = (CollisionFlags)reader.ReadInt32();
                    entry.Index = reader.ReadInt32();
                    entry.ID = Utilities.Consume<ShortGuid>(reader);
                    entry.Entity = Utilities.Consume<EntityHandle>(reader);
                    entry.MaterialIndex = reader.ReadInt32();
                    entry.CollisionProxyIndex = reader.ReadInt16();
                    entry.MappingIndex = reader.ReadInt16();
                    entry.ZoneID = Utilities.Consume<ShortGuid>(reader);
                    reader.BaseStream.Position += 16;
                    Entries.Add(entry);
                }
            }


            return true;
        }

        override protected bool SaveInternal()
        {
            //composite_instance_id defo has something to do with the ordering as all the zeros are first


            //Entries = Entries.OrderBy(o => o.entity.entity_id.ToUInt32() + o.id.ToUInt32()).ThenBy(o => o.entity.composite_instance_id.ToUInt32()).ThenBy(o => o.zone_id.ToUInt32()).ToList();

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write((Entries.Count) * 48);
                writer.Write(Entries.Count);

                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write((int)Entries[i].Flags);
                    writer.Write(Entries[i].Index);
                    Utilities.Write<ShortGuid>(writer, Entries[i].ID);
                    Utilities.Write<EntityHandle>(writer, Entries[i].Entity);
                    writer.Write((Int32)Entries[i].MaterialIndex);
                    writer.Write((Int16)Entries[i].CollisionProxyIndex);
                    writer.Write((Int16)Entries[i].MappingIndex);
                    Utilities.Write<ShortGuid>(writer, Entries[i].ZoneID);
                    writer.Write(new byte[16]);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public ShortGuid ID = ShortGuid.Invalid; //This is the name of the entity hashed via ShortGuid
            public EntityHandle Entity = new EntityHandle();
            public ShortGuid ZoneID = ShortGuid.Invalid; //this maps the entity to a zone ID. interestingly, this seems to be the point of truth for the zone rendering

            public int CollisionProxyIndex = -1; // Index in COLLISION.HKX
            public int MaterialIndex = -1; // Index in LEVEL_MODELS.MTL

            public CollisionFlags Flags = 0;
            public int Index = -1; //Compound shape index for static and ballistic collision 
            public int MappingIndex = -1;

            public static bool operator ==(Entry x, Entry y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.ID != y.ID) return false;
                if (x.ZoneID != y.ZoneID) return false;
                if (x.Entity != y.Entity) return false;
                return true;
            }
            public static bool operator !=(Entry x, Entry y)
            {
                return !(x == y);
            }

            public override bool Equals(object obj)
            {
                return obj is Entry entry &&
                       EqualityComparer<ShortGuid>.Default.Equals(ID, entry.ID) &&
                       EqualityComparer<EntityHandle>.Default.Equals(Entity, entry.Entity) &&
                       EqualityComparer<ShortGuid>.Default.Equals(ZoneID, entry.ZoneID);
            }

            public override int GetHashCode()
            {
                int hashCode = 1001543423;
                hashCode = hashCode * -1521134295 + ID.GetHashCode();
                hashCode = hashCode * -1521134295 + EqualityComparer<EntityHandle>.Default.GetHashCode(Entity);
                hashCode = hashCode * -1521134295 + ZoneID.GetHashCode();
                return hashCode;
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