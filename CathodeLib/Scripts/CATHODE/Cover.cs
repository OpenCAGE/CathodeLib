using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using CATHODE.Scripting;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/STATE_x/COVER
    /// </summary>
    public class Cover : CathodeFile
    {
        public List<CoverSegment> Entries = new List<CoverSegment>();
        public TraversalGrid Traversal = new TraversalGrid();

        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public Cover(string path) : base(path) { }
        public Cover(MemoryStream stream, string path = "") : base(stream, path) { }
        public Cover(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 8;
                int count = reader.ReadInt16();
                for (int i = 0; i < count; i++)
                {
                    CoverSegment segment = new CoverSegment();
                    segment.Left = Utilities.Consume<Vector3>(reader);
                    segment.Right = Utilities.Consume<Vector3>(reader);
                    segment.Normal = new Vector3(reader.ReadSingle(), 0, reader.ReadSingle());
                    segment.Height = reader.ReadSingle();
                    segment.Flags = reader.ReadInt32();
                    segment.UID = reader.ReadInt16();
                    segment.LeftCornerUID = reader.ReadInt16();
                    segment.RightCornerUID = reader.ReadInt16();
                    segment.LeftColinearUID = reader.ReadInt16();
                    segment.RightColinearUID = reader.ReadInt16();
                    segment.TraversalUID = reader.ReadInt16();
                    segment.CathodeIndex = reader.ReadInt32();
                    segment.CathodeEnt = Utilities.Consume<ShortGuid>(reader);
                    segment.CathodeParent = Utilities.Consume<ShortGuid>(reader);
                    Entries.Add(segment);
                }

                reader.BaseStream.Position += 4;
                for (int i = 0; i < count; i++)
                {
                    int slotsInSegment = reader.ReadInt16();
                    for (int x = 0; x < slotsInSegment; x++)
                    {
                        CoverSegment.CoverSlot slot = new CoverSegment.CoverSlot();
                        slot.UID = reader.ReadInt32();
                        slot.PctAlongCoverSegment = reader.ReadSingle();
                        slot.Flags = reader.ReadInt32();
                        slot.ClearAimAnglesHorizontal = reader.ReadInt16();
                        slot.ClearAimAnglesVertical = reader.ReadInt32();
                        Entries[i].OccupancySlots.Add(slot);
                    }
                }

                Traversal.XCells = reader.ReadInt16();
                Traversal.ZCells = reader.ReadInt16();
                Traversal.MinX = reader.ReadSingle();
                Traversal.MinZ = reader.ReadSingle();
                Traversal.UnitSize = reader.ReadSingle();
                int cellCount = Traversal.XCells * Traversal.ZCells;
                for (int i = 0; i < cellCount; i++)
                {
                    int itemsInCell = reader.ReadInt16();
                    List<short> cell = new List<short>();
                    for (int j = 0; j < itemsInCell; j++)
                        cell.Add(reader.ReadInt16());
                    Traversal.Cells.Add(cell);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(846362211); 
                writer.Write(7); 

                short count = (short)Entries.Count;
                writer.Write(count);

                for (int i = 0; i < count; i++)
                {
                    CoverSegment segment = Entries[i];

                    Utilities.Write(writer, segment.Left);
                    Utilities.Write(writer, segment.Right);
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    writer.Write(segment.Normal.x);
                    writer.Write(segment.Normal.z);
#else
                    writer.Write(segment.Normal.X);
                    writer.Write(segment.Normal.Z);
#endif
                    writer.Write(segment.Height);
                    writer.Write(segment.Flags);
                    writer.Write((short)segment.UID);
                    writer.Write((short)segment.LeftCornerUID);
                    writer.Write((short)segment.RightCornerUID);
                    writer.Write((short)segment.LeftColinearUID);
                    writer.Write((short)segment.RightColinearUID);
                    writer.Write((short)segment.TraversalUID);
                    writer.Write(segment.CathodeIndex);
                    Utilities.Write(writer, segment.CathodeEnt);
                    Utilities.Write(writer, segment.CathodeParent);
                }

                writer.Write(0);
                for (int i = 0; i < count; i++)
                {
                    var slots = Entries[i].OccupancySlots ?? new List<CoverSegment.CoverSlot>();
                    short slotsCount = (short)slots.Count;
                    writer.Write(slotsCount);

                    for (int x = 0; x < slots.Count; x++)
                    {
                        var slot = slots[x];
                        writer.Write(slot.UID);
                        writer.Write(slot.PctAlongCoverSegment);
                        writer.Write(slot.Flags);
                        writer.Write((short)slot.ClearAimAnglesHorizontal);
                        writer.Write(slot.ClearAimAnglesVertical);
                    }
                }

                writer.Write((short)Traversal.XCells);
                writer.Write((short)Traversal.ZCells);
                writer.Write(Traversal.MinX);
                writer.Write(Traversal.MinZ);
                writer.Write(Traversal.UnitSize);
                for (int i = 0; i < Traversal.Cells.Count; i++)
                {
                    var cell = Traversal.Cells[i];
                    writer.Write((short)cell.Count);
                    for (int j = 0; j < cell.Count; j++)
                        writer.Write(cell[j]);
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class CoverSegment
        {
            public Vector3 Left;
            public Vector3 Right;
            public Vector3 Normal; 
            public float Height;
            public int Flags;
            public int UID;
            public int LeftCornerUID;
            public int RightCornerUID;
            public int LeftColinearUID;
            public int RightColinearUID;
            public int TraversalUID;
            public int CathodeIndex;
            public ShortGuid CathodeEnt;
            public ShortGuid CathodeParent;

            public List<CoverSlot> OccupancySlots = new List<CoverSlot>();
            public class CoverSlot
            {
                public int UID;
                public float PctAlongCoverSegment;
                public int Flags;
                public int ClearAimAnglesHorizontal;
                public int ClearAimAnglesVertical;
            }
        }

        public class TraversalGrid
        {
            public short XCells;
            public short ZCells;
            public float MinX;
            public float MinZ;
            public float UnitSize;
            public List<List<short>> Cells = new List<List<short>>();
        }
        #endregion
    }
}