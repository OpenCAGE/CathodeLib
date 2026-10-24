using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/RADIOSITY_COLLISION_MAPPING.BIN
    /// </summary>
    public class RadiosityCollisionMap : CathodeFile
    {
        public List<Tuple<int, int>> Entries = new List<Tuple<int, int>>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public RadiosityCollisionMap(string path) : base(path) { }
        public RadiosityCollisionMap(MemoryStream stream, string path = "") : base(stream, path) { }
        public RadiosityCollisionMap(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int count = reader.ReadInt32();
                int[] first = Utilities.ConsumeArray<int>(reader, count);
                int[] second = Utilities.ConsumeArray<int>(reader, count);
                for (int i = 0; i < count; i++)
                {
                    Entries.Add(new Tuple<int, int>(first[i], second[i]));
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            List<Tuple<int, int>> entries = Entries.OrderBy(o => o.Item1).ToList();
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(entries.Count);
                for (int i = 0; i < entries.Count; i++)
                {
                    writer.Write(entries[i].Item1);
                }
                for (int i = 0; i < entries.Count; i++)
                {
                    writer.Write(entries[i].Item2);
                }
            }
            return true;
        }
        #endregion
    }
}