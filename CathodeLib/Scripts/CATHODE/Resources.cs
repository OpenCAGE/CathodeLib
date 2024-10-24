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

                Resource[] entries = new Resource[entryCount];
                for (int i = 0; i < entryCount; i++)
                {
                    Resource resource = new Resource();
                    resource.composite_instance_id = Utilities.Consume<ShortGuid>(reader);
                    resource.resource_id = Utilities.Consume<ShortGuid>(reader); //this is the id that's used in commands.pak, frequently translates to Door/AnimatedModel/Light/DYNAMIC_PHYSICS_SYSTEM
                    int index = reader.ReadInt32();
                    entries[index] = resource;
                }
                Entries = entries.ToList();
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            List<Resource> orderedEntries = Entries.OrderBy(o => o.composite_instance_id).ThenBy(o => o.resource_id).ToList();

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(new byte[4] { 0xCC, 0xBA, 0xED, 0xFE });
                writer.Write((Int32)1);
                writer.Write(orderedEntries.Count);
                writer.Write((Int32)0);

                for (int i = 0; i < orderedEntries.Count; i++)
                {
                    Utilities.Write(writer, orderedEntries[i].composite_instance_id);
                    Utilities.Write(writer, orderedEntries[i].resource_id);
                    writer.Write(Entries.IndexOf(orderedEntries[i]));
                }
            }
            return true;
        }
        #endregion

        public void AddUniqueResource(ShortGuid composite_instance_id, ShortGuid resource_id)
        {
            if (Entries.FirstOrDefault(o => o.composite_instance_id == composite_instance_id && o.resource_id == resource_id) != null)
                return;

            Entries.Add(new Resource()
            {
                composite_instance_id = composite_instance_id,
                resource_id = resource_id
            });
        }

        #region STRUCTURES
        public class Resource
        {
            public ShortGuid composite_instance_id;
            public ShortGuid resource_id;
        };
        #endregion
    }
}