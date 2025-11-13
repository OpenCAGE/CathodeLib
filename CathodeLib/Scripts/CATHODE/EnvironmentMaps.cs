using CATHODE.Scripting;
using CathodeLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/ENVIRONMENTMAP.BIN
    /// </summary>
    public class EnvironmentMaps : CathodeFile
    {
        public List<Mapping> Entries = new List<Mapping>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public EnvironmentMaps(string path) : base(path) { }
        public EnvironmentMaps(MemoryStream stream, string path = "") : base(stream, path) { }
        public EnvironmentMaps(byte[] data, string path = "") : base(data, path) { }

        /// <summary>
        /// This is the number of environment maps in the level. We should never reference an index higher than this.
        /// </summary>
        public int EnvironmentMapCount = 0;

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 8;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Mapping entry = new Mapping();
                    entry.MoverIndex = reader.ReadInt32();
                    entry.EnvMapIndex = reader.ReadInt32();
                    Entries.Add(entry);
                }
                EnvironmentMapCount = reader.ReadInt32();
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            List<Mapping> orderedEntries = Entries.OrderBy(o => o.MoverIndex).ToList();

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                Utilities.WriteString("envm", writer);
                writer.Write(1);
                writer.Write(Entries.Count);
                for (int i = 0; i < orderedEntries.Count; i++)
                {
                    writer.Write(orderedEntries[i].MoverIndex);
                    writer.Write(orderedEntries[i].EnvMapIndex);
                }
                writer.Write(EnvironmentMapCount);
            }
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Returns the environment map script entity for the given mover index.
        /// </summary>
        public FunctionEntity GetEnvironmentMapForMover(int moverIndex, Commands commands)
        {
            Mapping m = Entries.FirstOrDefault(e => e.MoverIndex == moverIndex);
            if (m != null)
            {
                foreach (Composite c in commands.Entries)
                {
                    foreach (FunctionEntity e in c.GetFunctionEntitiesOfType(FunctionType.EnvironmentMap))
                    {
                        Parameter p = e.GetParameter("environmentmap_index");
                        if (p?.content == null || p.content.dataType != DataType.INTEGER)
                            continue;
                        if (((cInteger)p.content).value == m.EnvMapIndex)
                            return e;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Returns the texture index used by the given environment map entity.
        /// </summary>
        public int GetTextureIndexForEnvironmentMap(FunctionEntity envMap)
        {
            Parameter p = envMap.GetParameter("Texture_Index");
            if (p?.content == null || p.content.dataType != DataType.INTEGER)
                return -1;
            return ((cInteger)p.content).value;
        }

        /// <summary>
        /// Returns all mover indices that use the given environment map entity.
        /// </summary>
        public List<int> GetMoverIndexesForEnvironmentMap(FunctionEntity envMap)
        {
            Parameter p = envMap.GetParameter("environmentmap_index");
            if (p?.content == null || p.content.dataType != DataType.INTEGER)
                return null;
            return Entries.Where(e => e.EnvMapIndex == ((cInteger)p.content).value).Select(e => e.MoverIndex).ToList();
        }
        #endregion

        #region STRUCTURES
        public class Mapping
        {
            public int EnvMapIndex;
            public int MoverIndex;
        };
        #endregion
    }
}