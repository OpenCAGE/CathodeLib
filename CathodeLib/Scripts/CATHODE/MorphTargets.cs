using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/MORPH_TARGET_DB.BIN */
    public class MorphTargets : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.NONE;

        public MorphTargets(string path) : base(path) { }
        public MorphTargets(MemoryStream stream, string path = "") : base(stream, path) { }
        public MorphTargets(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int namesCount = reader.ReadInt32();
                reader.BaseStream.Position += 4;
                for (int i = 0; i < namesCount; i++)
                {
                    Entries.Add(new Entry { Name = reader.ReadChars(reader.ReadInt32()).ToString() });
                }
                // todo: there are more counts here
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            int stringLength = Entries.Count;
            for (int i = 0; i < Entries.Count; i++)
                stringLength += Entries[i].Name.Length;

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count);
                writer.Write(stringLength);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].Name.Length);
                    Utilities.WriteString(Entries[i].Name, writer);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Entry
        {
            public string Name;
        }
        #endregion
    }
}