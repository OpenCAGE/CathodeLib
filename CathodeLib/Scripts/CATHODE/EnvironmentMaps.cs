using CathodeLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/ENVIRONMENTMAP.BIN */
    public class EnvironmentMaps : CathodeFile
    {
        public List<Mapping> Entries = new List<Mapping>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;
        public EnvironmentMaps(string path) : base(path) { }

        //This is the number of environment maps in the level. We should never reference an index higher than this.
        public int EnvironmentMapCount = 0;

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
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
            List<Mapping> orderedEntries = Entries.OrderByDescending(o => o.MoverIndex).ToList();

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

        #region STRUCTURES
        public class Mapping
        {
            public int EnvMapIndex; //Sequential index of the env map in texture BIN, when only parsing entries of type CUBEMAP - NOT WRITE INDEX
            public int MoverIndex; //Index of the mover in the MODELS.MVR file to apply the env map to
        };
        #endregion
    }
}