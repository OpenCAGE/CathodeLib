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
                //It seems typically in this file at the start there are a bunch of empty entries, and then there are a bunch of unresolvable ones, and then a bunch that can be resolved.

                //first 18 are always null

                //always first 247 are the same? 18 null and the rest in required assets?

                //note: some of the things we skip here actually contain useful info, but the game doesn't read it so there's no point us bothering with it 



                //NOTE: skipping first 18 as they're always empty, at least, for what we parse
                reader.BaseStream.Position = 4;
                int entryCount = reader.ReadInt32();
                reader.BaseStream.Position += (48 * 18);
                for (int i = 0; i < entryCount - 18; i++)
                {
                    Entry entry = new Entry();

                    entry.UnknownFlag = reader.ReadInt32(); //NOTE: if you filter by this value, all the UnknownIndex1s increment, UnknownIndex2s/collision_index don't increment but are grouped by -1s and non -1s, 

                    //todo: compare flag value across levels

                    entry.UnknownIndex1= reader.ReadInt32();
                    entry.id = Utilities.Consume<ShortGuid>(reader);
                    entry.entity = Utilities.Consume<EntityHandle>(reader);
                    entry.UnknownIndex2 = reader.ReadInt32();
                    entry.collision_index = reader.ReadInt16();
                    entry.UnknownValue = reader.ReadInt16();
                    entry.zone_id = Utilities.Consume<ShortGuid>(reader);
                    reader.BaseStream.Position += 16;
                    Entries.Add(entry);

                    if (minUnk1 < entry.UnknownIndex1)
                        minUnk1 = entry.UnknownIndex1;
                    if (minUnk2 < entry.UnknownIndex2)
                        minUnk2 = entry.UnknownIndex2;
                    if (minColIn < entry.collision_index)
                        minColIn = entry.collision_index;

                    if (!flags.Contains(entry.UnknownFlag))
                        flags.Add(entry.UnknownFlag);

                    if (entry.collision_index != -1 && entry.UnknownIndex1 == -1 && entry.UnknownIndex2 == -1 && entry.UnknownValue == -1)
                    {
                        string sdfsdf = "";
                    }

                    if (entry.UnknownIndex1 == -1 && entry.UnknownIndex2 == -1 && entry.UnknownValue == -1)
                    {
                        string sdfsdf = "";
                    }

                    string flagBin = BitConverter.ToString(BitConverter.GetBytes(entry.UnknownFlag));
                    if (!dictest.ContainsKey(flagBin))
                        dictest.Add(flagBin, new List<string>());

                    dictest[flagBin].Add(entry.UnknownIndex1 + " -> " + entry.UnknownIndex2 + " -> " + entry.collision_index);

                   //if (entry.UnknownFlag == -1073737335)
                   //    Console.WriteLine(entry.UnknownIndex1);

                    //if (entry.UnknownFlag == 4429)
                    //    Console.WriteLine(entry.UnknownIndex1);

                    //if (entry.UnknownFlag == -1073737405)
                    //    Console.WriteLine(entry.UnknownIndex1);

                    //Console.WriteLine(entry.UnknownFlag + " -> " + entry.UnknownIndex1 + " -> " + entry.UnknownIndex2 + " -> " + entry.collision_index + " -> " + entry.UnknownValue);
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
                writer.Write((Entries.Count + 18) * 48);
                writer.Write(Entries.Count + 18);

                writer.Write(new byte[18 * 48]);

                for (int i = 0; i < Entries.Count; i++)
                {
                    //writer.Write(-268427008);
                    //writer.Write(-1);

                    writer.Write(Entries[i].UnknownFlag);
                    writer.Write(Entries[i].UnknownIndex1);

                    Utilities.Write<ShortGuid>(writer, Entries[i].id);
                    Utilities.Write<EntityHandle>(writer, Entries[i].entity);

                    writer.Write(-1);
                    //writer.Write(Entries[i].UnknownIndex2);

                    writer.Write((Int16)Entries[i].collision_index);

                    writer.Write((short)-1);
                    //writer.Write((Int16)Entries[i].UnknownValue);

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
            public EntityHandle entity = new EntityHandle();
            public ShortGuid zone_id = ShortGuid.Invalid; //this maps the entity to a zone ID. interestingly, this seems to be the point of truth for the zone rendering

            public int collision_index = -1; //maps to havok hkx entry

            public int UnknownFlag = 0;
            public int UnknownIndex1 = -1;
            public int UnknownIndex2 = -1;
            public int UnknownValue = -1;

            public static bool operator ==(Entry x, Entry y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.id != y.id) return false;
                if (x.zone_id != y.zone_id) return false;
                if (x.entity != y.entity) return false;
                return true;
            }
            public static bool operator !=(Entry x, Entry y)
            {
                return !(x == y);
            }

            public override bool Equals(object obj)
            {
                return obj is Entry entry &&
                       EqualityComparer<ShortGuid>.Default.Equals(id, entry.id) &&
                       EqualityComparer<EntityHandle>.Default.Equals(entity, entry.entity) &&
                       EqualityComparer<ShortGuid>.Default.Equals(zone_id, entry.zone_id);
            }

            public override int GetHashCode()
            {
                int hashCode = 1001543423;
                hashCode = hashCode * -1521134295 + id.GetHashCode();
                hashCode = hashCode * -1521134295 + EqualityComparer<EntityHandle>.Default.GetHashCode(entity);
                hashCode = hashCode * -1521134295 + zone_id.GetHashCode();
                return hashCode;
            }
        };
        #endregion
    }
}