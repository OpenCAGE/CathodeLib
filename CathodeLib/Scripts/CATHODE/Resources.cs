using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using CATHODE.Scripting;
using CathodeLib;
using static CATHODE.Resources;

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
                    resource.composite_instance_id = Utilities.Consume<ShortGuid>(reader);
                    resource.resource_id = Utilities.Consume<ShortGuid>(reader); //this is the id that's used in commands.pak, frequently translates to Door/AnimatedModel/Light/DYNAMIC_PHYSICS_SYSTEM
                    resource.index = reader.ReadInt32();
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
                    Utilities.Write(writer, Entries[i].composite_instance_id);
                    Utilities.Write(writer, Entries[i].resource_id);
                    writer.Write(Entries[i].index);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Resource
        {
            public ShortGuid composite_instance_id;
            public ShortGuid resource_id;

            public int index;
        };
        #endregion
    }
}