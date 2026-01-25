using CATHODE.Enums;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

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
            byte[][] entryBuffers = new byte[Entries.Count][];
            Parallel.For(0, Entries.Count, i =>
            {
                entryBuffers[i] = SerializeEntry(Entries[i]);
            });
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write((Int32)59);
                writer.Write(Entries.Count);
                for (int i = 0; i < entryBuffers.Length; i++)
                    writer.Write(entryBuffers[i]);
            }
            return true;
        }

        private byte[] SerializeEntry(NAV_MESH_BARRIER_RESOURCE entry)
        {
            using (MemoryStream stream = new MemoryStream(10)) 
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(_resources.GetWriteIndex(entry.Resource));
                writer.Write((Int16)entry.area_id);
                writer.Write((int)entry.allowed_character_classes);
                return stream.ToArray();
            }
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
