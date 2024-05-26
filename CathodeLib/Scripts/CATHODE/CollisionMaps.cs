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
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                //It seems typically in this file at the start there are a bunch of empty entries, and then there are a bunch of unresolvable ones, and then a bunch that can be resolved.

                //first 18 are always null?
                //always first 247 are the same? 18 null and the rest in required assets?

                //note: some of the things we skip here actually contain useful info, but the game doesn't read it so there's no point us bothering with it 

                reader.BaseStream.Position = 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Entry entry = new Entry();
                    reader.BaseStream.Position += 8;
                    entry.id = Utilities.Consume<ShortGuid>(reader);
                    entry.entity = Utilities.Consume<CommandsEntityReference>(reader);
                    reader.BaseStream.Position += 8;
                    entry.zone_id = Utilities.Consume<ShortGuid>(reader);
                    reader.BaseStream.Position += 16;
                    Entries.Add(entry);

                    //uint test1 = entry.entity.entity_id.ToUInt32() + entry.id.ToUInt32() + entry.entity.composite_instance_id.ToUInt32() + entry.zone_id.ToUInt32();

                    //Console.WriteLine(test1 + " -> " + entry.entity.entity_id.ToUInt32() + " -> " + entry.id.ToUInt32() + " -> " + entry.entity.composite_instance_id.ToUInt32() + " -> " + entry.zone_id.ToUInt32());
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            //Entries = Entries.OrderBy(o => o.entity.entity_id.ToUInt32() + o.id.ToUInt32()).ThenBy(o => o.entity.composite_instance_id.ToUInt32()).ThenBy(o => o.zone_id.ToUInt32()).ToList();

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count * 80);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(new byte[8]);
                    Utilities.Write<ShortGuid>(writer, Entries[i].id);
                    Utilities.Write<CommandsEntityReference>(writer, Entries[i].entity);
                    writer.Write(new byte[8]);
                    Utilities.Write<ShortGuid>(writer, Entries[i].zone_id);
                    writer.Write(new byte[16]);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public ShortGuid id = ShortGuid.Invalid; //This is the name of the entity hashed via ShortGuid
            public CommandsEntityReference entity = new CommandsEntityReference();
            public ShortGuid zone_id = ShortGuid.Invalid; //this maps the entity to a zone ID. interestingly, this seems to be the point of truth for the zone rendering

            public override bool Equals(object obj)
            {
                return obj is Entry entry &&
                       EqualityComparer<ShortGuid>.Default.Equals(id, entry.id) &&
                       EqualityComparer<CommandsEntityReference>.Default.Equals(entity, entry.entity) &&
                       EqualityComparer<ShortGuid>.Default.Equals(zone_id, entry.zone_id);
            }

            public override int GetHashCode()
            {
                int hashCode = 1001543423;
                hashCode = hashCode * -1521134295 + id.GetHashCode();
                hashCode = hashCode * -1521134295 + EqualityComparer<CommandsEntityReference>.Default.GetHashCode(entity);
                hashCode = hashCode * -1521134295 + zone_id.GetHashCode();
                return hashCode;
            }
        };
        #endregion
    }
}