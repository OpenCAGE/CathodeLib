using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
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

                reader.BaseStream.Position = 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Entry entry = new Entry();
                    entry.unk0 = reader.ReadInt32(); //flag?
                    entry.Unknown1_ = reader.ReadInt32(); //some sort of index ?
                    entry.ID = Utilities.Consume<ShortGuid>(reader);
                    entry.entity = Utilities.Consume<CommandsEntityReference>(reader);
                    entry.Unknown2_ = reader.ReadInt32(); //Is sometimes -1 and other times a small positive integer. Is this tree node parent?
                    entry.CollisionHKXEntryIndex = reader.ReadInt16();
                    entry.Unknown3_ = reader.ReadInt16(); //Most of the time it is -1.
                    entry.MVRZoneIDThing = reader.ReadInt32();
                    entry.Unknown4_ = reader.ReadInt32();
                    entry.Unknown5_ = reader.ReadInt32();
                    entry.Unknown6_ = reader.ReadInt32();
                    entry.Unknown7_ = reader.ReadInt32();
                    Entries.Add(entry);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count * 80);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].Unknown1_);
                    Utilities.Write<ShortGuid>(writer, Entries[i].ID);
                    Utilities.Write<CommandsEntityReference>(writer, Entries[i].entity);
                    writer.Write(Entries[i].CollisionHKXEntryIndex);
                    writer.Write(Entries[i].Unknown3_);
                    writer.Write(Entries[i].MVRZoneIDThing);
                    writer.Write(Entries[i].Unknown4_);
                    writer.Write(Entries[i].Unknown5_);
                    writer.Write(Entries[i].Unknown6_);
                    writer.Write(Entries[i].Unknown7_);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public int unk0 = 0;    //flags? 
            public int Unknown1_ = -1;      // Is this tree node id?

            public ShortGuid ID = ShortGuid.Invalid; //This is the name of the entity hashed via ShortGuid, as a result, we can't resolve a lot of them. Does the game care about the value? I doubt it. We definitely don't.

            public CommandsEntityReference entity = new CommandsEntityReference();

            public int Unknown2_= -1;      // NOTE: Is sometimes -1 and other times a small positive integer. Is this tree node parent?

            public Int16 CollisionHKXEntryIndex = -1;      // NOTE: Most of the time is a positive integer, sometimes -1.

            public Int16 Unknown3_ = -1;    // NOTE: Most of the time it is -1.
            public int MVRZoneIDThing = 0; // NOTE: This is CollisionMapThingIDs[0] from alien_mvr_entry

            //TODO: are these values ever not zero? need to assert.
            public int Unknown4_ = 0;
            public int Unknown5_ = 0;
            public int Unknown6_ = 0;
            public int Unknown7_ = 0;
        };
        #endregion
    }
}