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
                writer.Write((short)Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
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

                int numSlots = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    numSlots += Entries[i].OccupancySlots.Count;
                }
                writer.Write(numSlots);

                for (int i = 0; i < Entries.Count; i++)
                {
                    var slots = Entries[i].OccupancySlots;
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

                public float LeftEdgeRightmostHorizontal
                {
                    get => GetHorizontalAngle(0);
                    set => ClearAimAnglesHorizontal = SetHorizontalAngle(ClearAimAnglesHorizontal, 0, value);
                }
                public float OverTopLeftmostHorizontal
                {
                    get => GetHorizontalAngle(1);
                    set => ClearAimAnglesHorizontal = SetHorizontalAngle(ClearAimAnglesHorizontal, 1, value);
                }
                public float OverTopRightmostHorizontal
                {
                    get => GetHorizontalAngle(2);
                    set => ClearAimAnglesHorizontal = SetHorizontalAngle(ClearAimAnglesHorizontal, 2, value);
                }

                public float RightEdgeLeftmostHorizontal
                {
                    get => GetHorizontalAngle(3);
                    set => ClearAimAnglesHorizontal = SetHorizontalAngle(ClearAimAnglesHorizontal, 3, value);
                }

                public float LeftEdgeBottomVertical
                {
                    get => GetVerticalAngle(0);
                    set => ClearAimAnglesVertical = SetVerticalAngle(ClearAimAnglesVertical, 0, value);
                }
                public float LeftEdgeTopVertical
                {
                    get => GetVerticalAngle(1);
                    set => ClearAimAnglesVertical = SetVerticalAngle(ClearAimAnglesVertical, 1, value);
                }
                public float OverTopBottomVertical
                {
                    get => GetVerticalAngle(2);
                    set => ClearAimAnglesVertical = SetVerticalAngle(ClearAimAnglesVertical, 2, value);
                }
                public float OverTopTopVertical
                {
                    get => GetVerticalAngle(3);
                    set => ClearAimAnglesVertical = SetVerticalAngle(ClearAimAnglesVertical, 3, value);
                }
                public float RightEdgeBottomVertical
                {
                    get => GetVerticalAngle(4);
                    set => ClearAimAnglesVertical = SetVerticalAngle(ClearAimAnglesVertical, 4, value);
                }
                public float RightEdgeTopVertical
                {
                    get => GetVerticalAngle(5);
                    set => ClearAimAnglesVertical = SetVerticalAngle(ClearAimAnglesVertical, 5, value);
                }

                private readonly float _halfPi = (float)(Math.PI / 2.0);
                private float DecodeAngleNibble(int nibble)
                {
                    if (nibble < 0) nibble = 0;
                    if (nibble > 15) nibble = 15;
                    return -_halfPi + (nibble * (float)Math.PI / 15f);
                }
                private int EncodeAngleNibble(float angle)
                {
                    if (angle < -_halfPi) angle = -_halfPi;
                    if (angle > _halfPi) angle = _halfPi;
                    float t = (angle + _halfPi) / (2 * _halfPi); 
                    int nibble = (int)Math.Round(t * 15f);
                    if (nibble < 0) nibble = 0;
                    if (nibble > 15) nibble = 15;
                    return nibble;
                }
                private float GetHorizontalAngle(int indexFromMsb)
                {
                    int shift = (3 - indexFromMsb) * 4;
                    int nibble = (ClearAimAnglesHorizontal >> shift) & 0xF;
                    return DecodeAngleNibble(nibble);
                }
                private int SetHorizontalAngle(int packed, int indexFromMsb, float angle)
                {
                    int shift = (3 - indexFromMsb) * 4;
                    int nibble = EncodeAngleNibble(angle);
                    packed &= ~(0xF << shift);
                    packed |= (nibble & 0xF) << shift;
                    return packed;
                }
                private float GetVerticalAngle(int indexFromMsb)
                {
                    int shift = (5 - indexFromMsb) * 4;
                    int nibble = (ClearAimAnglesVertical >> shift) & 0xF;
                    return DecodeAngleNibble(nibble);
                }
                private int SetVerticalAngle(int packed, int indexFromMsb, float angle)
                {
                    int shift = (5 - indexFromMsb) * 4;
                    int nibble = EncodeAngleNibble(angle);
                    packed &= ~(0xF << shift);
                    packed |= (nibble & 0xF) << shift;
                    return packed;
                }
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