using CATHODE.Enums;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/PATH_BARRIER_RESOURCES
    /// </summary>
    public class PathBarrierResources : CathodeFile
    {
        public List<NAV_MESH_BARRIER_RESOURCE> Entries = new List<NAV_MESH_BARRIER_RESOURCE>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        protected override bool HandlesLoadingManually => true;
        private Resources _resources;

        public PathBarrierResources(string path, Resources resources) : base(path)
        {
            _resources = resources;

            _loaded = Load();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position = 4; //59
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    NAV_MESH_BARRIER_RESOURCE entry = new NAV_MESH_BARRIER_RESOURCE();
                    entry.Resource = _resources.GetAtWriteIndex(reader.ReadInt32());
                    entry.area_id = reader.ReadInt16();
                    entry.allowed_character_classes = (NAVIGATION_CHARACTER_CLASS_COMBINATION)reader.ReadInt32();
                    Entries.Add(entry);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter reader = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                reader.BaseStream.SetLength(0);
                reader.Write((Int32)59);
                reader.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    reader.Write(_resources.GetWriteIndex(Entries[i].Resource));
                    reader.Write((Int16)Entries[i].area_id);
                    reader.Write((int)Entries[i].allowed_character_classes); 
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class NAV_MESH_BARRIER_RESOURCE
        {
            public Resources.Resource Resource;

            public int area_id; //dt_area_id_t
            public NAVIGATION_CHARACTER_CLASS_COMBINATION allowed_character_classes;
        }
        #endregion
    }
}
