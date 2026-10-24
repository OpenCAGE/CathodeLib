using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#elif GODOT
using Godot;
using System.Numerics;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;
using Vector4 = Godot.Vector4;
using Color = Godot.Color;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/STATE_x/ASSAULT_POSITIONS
    /// </summary>
    public class AssaultPositions : CathodeFile
    {
        public int XCells;
        public int ZCells;
        public float MinX;
        public float MinZ;
        public float UnitSize;
        public List<List<JobInfo>> Cells = new List<List<JobInfo>>();

        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public AssaultPositions(string path) : base(path) { }
        public AssaultPositions(MemoryStream stream, string path = "") : base(stream, path) { }
        public AssaultPositions(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 4; // version
                int positionCount = reader.ReadInt32();
                XCells = reader.ReadInt32();
                ZCells = reader.ReadInt32();
                MinX = reader.ReadSingle();
                MinZ = reader.ReadSingle();
                UnitSize = reader.ReadSingle();

                Cells.Clear();
                int cellCount = XCells * ZCells;
                int jobsRead = 0;
                for (int i = 0; i < cellCount; i++)
                {
                    List<JobInfo> cell = Utilities.ConsumeArray<JobInfo>(reader, reader.ReadInt32()).ToList();
                    jobsRead += cell.Count;
                    Cells.Add(cell);
                }

                if (positionCount != jobsRead)
                    throw new Exception("Unexpected position count!");
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(3);
                writer.Write(Cells.Sum(c => c.Count));
                writer.Write(XCells);
                writer.Write(ZCells);
                writer.Write(MinX);
                writer.Write(MinZ);
                writer.Write(UnitSize);

                int cellCount = XCells * ZCells;
                for (int i = 0; i < cellCount; i++)
                {
                    List<JobInfo> cell = i < Cells.Count ? Cells[i] : new List<JobInfo>();
                    writer.Write(cell.Count);
                    Utilities.Write(writer, cell);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class JobInfo
        {
            public Vector3 Position;
            public float Yaw;
        }
        #endregion
    }
}
