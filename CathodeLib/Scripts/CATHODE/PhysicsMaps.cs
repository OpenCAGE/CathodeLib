using System.IO;
using System.Runtime.InteropServices;
using CathodeLib;
using System.Collections.Generic;
using CATHODE.Scripting;
using System;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif 

namespace CATHODE
{
    //This file defines additional info for entities with DYNAMIC_PHYSICS_SYSTEM resources.

    /* DATA/ENV/PRODUCTION/x/WORLD/PHYSICS.MAP */
    public class PhysicsMaps : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public PhysicsMaps(string path) : base(path) { }

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
                    entry.physics_system_index = reader.ReadInt32();
                    reader.BaseStream.Position += 4;
                    entry.resource_type = Utilities.Consume<ShortGuid>(reader); 
                    entry.composite_instance_id = Utilities.Consume<ShortGuid>(reader); 
                    entry.entity = Utilities.Consume<CommandsEntityReference>(reader);
                    entry.Row0 = Utilities.Consume<Vector4>(reader);
                    entry.Row1 = Utilities.Consume<Vector4>(reader);
                    entry.Row2 = Utilities.Consume<Vector4>(reader);

                    reader.BaseStream.Position += 8;
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
                    writer.Write(Entries[i].physics_system_index);
                    writer.Write(new byte[4]);
                    Utilities.Write(writer, Entries[i].resource_type);
                    Utilities.Write(writer, Entries[i].composite_instance_id);
                    Utilities.Write(writer, Entries[i].entity);
                    Utilities.Write<Vector4>(writer, Entries[i].Row0);
                    Utilities.Write<Vector4>(writer, Entries[i].Row1);
                    Utilities.Write<Vector4>(writer, Entries[i].Row2);
                    writer.Write(new byte[8]);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            //Should match system_index on the PhysicsSystem entity.
            public int physics_system_index;

            //DYNAMIC_PHYSICS_SYSTEM
            public ShortGuid resource_type;

            //This is the instance ID for the composite containing the PhysicsSystem.
            //We do not need to worry about the entity ID for the PhysicsSystem as the resources are written to the composite that contains it.
            public ShortGuid composite_instance_id;

            //This is the entity ID and instance ID for the actual instanced composite entity (basically, a step down from the instance above).
            public CommandsEntityReference entity;

            public Vector4 Row0; // NOTE: This is a 3x4 matrix, seems to have rotation data on the leftmost 3x3 matrix, and position
            public Vector4 Row1; //   on the rightmost 3x1 matrix.
            public Vector4 Row2;
        };
        #endregion
    }
}