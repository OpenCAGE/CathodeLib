using CathodeLib;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
using System.Runtime.InteropServices;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/MORPH_TARGET_DB.BIN
    /// </summary>
    public class MorphTargets : CathodeFile
    {
        public List<MorphTarget> Entries = new List<MorphTarget>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.SAVE | Implementation.LOAD;

        public MorphTargets(string path) : base(path) { }
        public MorphTargets(MemoryStream stream, string path = "") : base(stream, path) { }
        public MorphTargets(byte[] data, string path = "") : base(data, path) { }

        private List<MorphTarget> _writeList = new List<MorphTarget>();

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                int morphCount = reader.ReadInt32();
                reader.BaseStream.Position += 4;
                List<string> names = new List<string>();
                for (int i = 0; i < morphCount; i++)
                {
                    names.Add(new string(reader.ReadChars(reader.ReadInt32())));
                }

                int modelCount = reader.ReadInt32();
                for (int i = 0; i < modelCount; i++)
                {
                    MorphTarget model = new MorphTarget();
                    int targetCount = reader.ReadInt32();
                    for (int x = 0; x < targetCount; x++)
                    {
                        MorphTarget.Target target = new MorphTarget.Target(); 
                        target.Name = names[reader.ReadInt32()];

                        int vertCount = reader.ReadInt32();
                        for (int z = 0; z < vertCount; z++)
                            target.Points.Add(Utilities.Consume<MorphTarget.Target.Point>(reader));

                        model.Targets.Add(target);
                    }
                    Entries.Add(model);
                }
            }
            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                List<string> names;
                int namesLength = 0;
                {
                    HashSet<string> namesHashSet = new HashSet<string>();
                    for (int i = 0; i < Entries.Count; i++)
                        for (int x = 0; x < Entries[i].Targets.Count; x++)
                            if (namesHashSet.Add(Entries[i].Targets[x].Name))
                                namesLength += Entries[i].Targets[x].Name.Length + 1;
                    names = namesHashSet.ToList();
                }
                writer.Write(names.Count);
                writer.Write(namesLength);
                for (int i = 0; i < names.Count; i++)
                {
                    writer.Write(names[i].Length);
                    Utilities.WriteString(names[i], writer);
                }

                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].Targets.Count);
                    for (int x = 0; x < Entries[i].Targets.Count; x++)
                    {
                        writer.Write(names.IndexOf(Entries[i].Targets[x].Name));
                        writer.Write(Entries[i].Targets[x].Points.Count);
                        for (int z = 0; z < Entries[i].Targets[x].Points.Count; z++)
                        {
                            Utilities.Write(writer, Entries[i].Targets[x].Points[z]);
                        }
                    }
                }
            }
            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(MorphTarget morphTarget)
        {
            if (!_writeList.Contains(morphTarget)) return -1;
            return _writeList.IndexOf(morphTarget);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public MorphTarget GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public MorphTarget AddEntry(MorphTarget morphTarget)
        {
            MorphTarget newMorphTarget = morphTarget.Copy();
            Entries.Add(newMorphTarget);
            return newMorphTarget;
        }
        #endregion

        #region STRUCTURES
        public class MorphTarget
        {
            public List<Target> Targets = new List<Target>();

            public class Target
            {
                public string Name;
                public List<Point> Points = new List<Point>();

                [StructLayout(LayoutKind.Sequential, Pack = 1)]
                public class Point
                {
                    public byte u, v, nx, ny;
                    public byte x, y, z, nz;
                }
            }
        }
        #endregion
    }
}