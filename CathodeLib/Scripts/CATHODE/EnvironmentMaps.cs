using CATHODE.Scripting;
using CathodeLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/ENVIRONMENTMAP.BIN
    /// </summary>
    public class EnvironmentMaps : CathodeFile
    {
        public List<Mapping> Entries = new List<Mapping>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        protected override bool HandlesLoadingManually => true;
        private Movers _movers;

        public EnvironmentMaps(string path, Movers movers) : base(path)
        {
            _movers = movers;

            _loaded = Load();
        }

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
                    entry.Mover = _movers.GetAtWriteIndex(reader.ReadInt32());
                    entry.EnvMapIndex = reader.ReadInt32();
                    Entries.Add(entry);
                }
                EnvironmentMapCount = reader.ReadInt32();
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            List<Mapping> orderedEntries = Entries.OrderBy(o => _movers.GetWriteIndex(o.Mover)).ToList();

            byte[][] entryBuffers = new byte[orderedEntries.Count][];
            Parallel.For(0, orderedEntries.Count, i =>
            {
                entryBuffers[i] = SerializeMapping(orderedEntries[i]);
            });

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                Utilities.WriteString("envm", writer);
                writer.Write(1);
                writer.Write(Entries.Count);
                for (int i = 0; i < entryBuffers.Length; i++)
                    writer.Write(entryBuffers[i]);
                writer.Write(EnvironmentMapCount);
            }
            return true;
        }

        private byte[] SerializeMapping(Mapping mapping)
        {
            using (MemoryStream stream = new MemoryStream(8)) 
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(_movers.GetWriteIndex(mapping.Mover));
                writer.Write(mapping.EnvMapIndex);
                return stream.ToArray();
            }
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Returns the environment map script entity for the given mover index.
        /// </summary>
        public FunctionEntity GetEnvironmentMapForMover(int moverIndex, Commands commands)
        {
            Mapping m = Entries.FirstOrDefault(e => _movers.GetWriteIndex(e.Mover) == moverIndex);
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
            return Entries.Where(e => e.EnvMapIndex == ((cInteger)p.content).value).Select(e => _movers.GetWriteIndex(e.Mover)).ToList();
        }
        #endregion

        #region STRUCTURES
        public class Mapping
        {
            public int EnvMapIndex;
            public Movers.MOVER_DESCRIPTOR Mover;
        };
        #endregion
    }
}