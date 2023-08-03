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
                reader.BaseStream.Position = 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Entry entry = new Entry();
                    entry.Unknown1_ = reader.ReadInt32();
                    entry.entity = Utilities.Consume<CommandsEntityReference>(reader);
                    entry.ResourcesBINID = reader.ReadInt32();
                    entry.Unknown2_ = reader.ReadInt32();
                    entry.CollisionHKXEntryIndex = reader.ReadInt16();
                    entry.Unknown3_ = reader.ReadInt16();
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
                    Utilities.Write<CommandsEntityReference>(writer, Entries[i].entity);
                    writer.Write(Entries[i].ResourcesBINID);
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
            public int Unknown1_;      // Is this tree node id?

            public CommandsEntityReference entity;

            public int ResourcesBINID; // NOTE: This might not be the correct name. It seems to correspond to the similarly named variable at alien_resources_bin_entry.
            public int Unknown2_;      // NOTE: Is sometimes -1 and other times a small positive integer. Is this tree node parent?

            public Int16 CollisionHKXEntryIndex;      // NOTE: Most of the time is a positive integer, sometimes -1.

            public Int16 Unknown3_;    // NOTE: Most of the time it is -1.
            public int MVRZoneIDThing; // NOTE: This is CollisionMapThingIDs[0] from alien_mvr_entry

            public int Unknown4_;
            public int Unknown5_;
            public int Unknown6_;
            public int Unknown7_;
        };
        #endregion
    }
}