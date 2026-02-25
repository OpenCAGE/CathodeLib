using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/BEHAVIOR_TREE.DB
    /// </summary>
    public class BehaviorTreeDB : CathodeFile
    {
        public List<string> Entries = new List<string>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public BehaviorTreeDB(string path) : base(path) { }
        public BehaviorTreeDB(MemoryStream stream, string path = "") : base(stream, path) { }
        public BehaviorTreeDB(byte[] data, string path = "") : base(data, path) { }

        ~BehaviorTreeDB()
        {
            Entries.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int count = reader.ReadInt32();
                reader.BaseStream.Position += (count * 8) + 4;
                for (int i = 0; i < count; i++)
                {
                    Entries.Add(Utilities.ReadString(reader));
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write((Int64)Entries.Count);
                writer.Write(new byte[8 * Entries.Count]);
                List<int> offsets = new List<int>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    offsets.Add((int)writer.BaseStream.Position);
                    Utilities.WriteString(Entries[i], writer, true);
                }
                writer.BaseStream.Position = 8;
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write((Int64)offsets[i]);
                }
            }
            return true;
        }
        #endregion
    }
}