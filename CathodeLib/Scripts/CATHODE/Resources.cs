using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using CATHODE.Scripting;
using CathodeLib;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/RESOURCES.BIN */
    public class Resources : CathodeFile
    {
        public List<Resource> Entries = new List<Resource>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public Resources(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position = 8;
                int entryCount = reader.ReadInt32();
                reader.BaseStream.Position += 4;

                for (int i = 0; i < entryCount; i++)
                {
                    Resource resource = new Resource();

                    //TODO: I don't think this is as it seems... the composite_instance_id value often translates to a ShortGuid string, frequently Door/AnimatedModel/Light/DYNAMIC_PHYSICS_SYSTEM...
                    //      ... and notably the number of entries that translate to DYNAMIC_PHYSICS_SYSTEM match the number of entries in PHYSICS.MAP (which defines the systems)

                    resource.Entity = Utilities.Consume<CommandsEntityReference>(reader);
                    resource.IndexFromMVREntry = reader.ReadInt32();
                    Entries.Add(resource);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(new byte[4] { 0xCC, 0xBA, 0xED, 0xFE });
                writer.Write((Int32)1);
                writer.Write(Entries.Count);
                writer.Write((Int32)0);

                for (int i = 0; i < Entries.Count; i++)
                {
                    Utilities.Write<CommandsEntityReference>(writer, Entries[i].Entity);
                    writer.Write(Entries[i].IndexFromMVREntry);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Resource
        {
            public CommandsEntityReference Entity;
            public int IndexFromMVREntry; // NOTE: This is an entry index in this file itself.
        };
        #endregion
    }
}