using System.IO;
using System.Runtime.InteropServices;
using CathodeLib;
using System.Collections.Generic;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif 

namespace CATHODE
{
    /* Handles Cathode PHYSICS.MAP files */
    public class PhysicsMapDatabase : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Impl Implementation = Impl.CREATE | Impl.LOAD | Impl.SAVE;
        public PhysicsMapDatabase(string path) : base(path) { }

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
                    entry.UnknownNotableValue_ = reader.ReadInt32();
                    reader.BaseStream.Position += 4;
                    for (int x = 0; x < 4; x++) entry.IDs[x] = reader.ReadInt32();
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
                    writer.Write(Entries[i].UnknownNotableValue_);
                    writer.Write(new byte[4]);
                    for (int x = 0; x < 4; x++) writer.Write(Entries[i].IDs[x]);
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
            public int UnknownNotableValue_;
            public int[] IDs = new int[4];
            public Vector4 Row0; // NOTE: This is a 3x4 matrix, seems to have rotation data on the leftmost 3x3 matrix, and position
            public Vector4 Row1; //   on the rightmost 3x1 matrix.
            public Vector4 Row2;
        };
        #endregion
    }
}