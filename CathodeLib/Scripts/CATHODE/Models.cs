using CATHODE.LEGACY.Assets;
using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* Handles Cathode LEVEL_MODELS.PAK/MODELS_LEVEL.BIN files */
    public class Models : CathodeFile
    {
        public List<CS2> Entries = new List<CS2>();

        List<AlienVBF> _vertexFormats = new List<AlienVBF>();
        byte[] _boneBuffer;

        private string _filepathBIN;

        public Models(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        override protected bool LoadInternal()
        {
            _filepathBIN = _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + "MODELS_" + Path.GetFileName(_filepath).Substring(0, Path.GetFileName(_filepath).Length - 11) + ".BIN";
            
            if (!File.Exists(_filepathBIN)) return false;

            using (BinaryReader bin = new BinaryReader(File.OpenRead(_filepathBIN)))
            {
                bin.BaseStream.Position += 4; //Magic
                int modelCount = bin.ReadInt32();
                if ((FileIdentifiers)BigEndianUtils.ReadInt16(bin) != FileIdentifiers.HEADER_FILE) return false;
                if ((FileIdentifiers)BigEndianUtils.ReadInt16(bin) != FileIdentifiers.MODEL_DATA) return false;
                int vbfCount = bin.ReadInt32();

                //Read vertex buffer formats
                _vertexFormats = new List<AlienVBF>(vbfCount);
                for (int EntryIndex = 0; EntryIndex < vbfCount; ++EntryIndex)
                {
                    long startPos = bin.BaseStream.Position;
                    int count = 1;
                    while (bin.ReadByte() != 0xFF)
                    {
                        bin.BaseStream.Position += Marshal.SizeOf(typeof(AlienVBFE)) - 1;
                        count++;
                    }
                    bin.BaseStream.Position = startPos;

                    AlienVBF VertexInput = new AlienVBF();
                    VertexInput.ElementCount = count;
                    VertexInput.Elements = Utilities.ConsumeArray<AlienVBFE>(bin, VertexInput.ElementCount).ToList();
                    _vertexFormats.Add(VertexInput);
                }

                //Read filenames and fileparts
                Dictionary<int, string> stringOffsets = new Dictionary<int, string>();
                {
                    byte[] filenames = bin.ReadBytes(bin.ReadInt32());
                    using (BinaryReader reader = new BinaryReader(new MemoryStream(filenames)))
                    {
                        while (reader.BaseStream.Position < reader.BaseStream.Length)
                            stringOffsets.Add((int)reader.BaseStream.Position, Utilities.ReadString(reader)/*.Replace('\\', '/')*/);
                    }
                }

                //Read model metadata
                for (int i = 0; i < modelCount; i++)
                {
                    CS2 cs2 = new CS2();
                    int FileNameOffset = bin.ReadInt32();
                    cs2.filename = stringOffsets[FileNameOffset];
                    bin.BaseStream.Position += 4; //skip unused
                    int ModelPartNameOffset = bin.ReadInt32();
                    cs2.modelPartName = stringOffsets[ModelPartNameOffset];
                    bin.BaseStream.Position += 4; //skip unused
                    cs2.AABBMin = Utilities.Consume<Vector3>(bin);
                    cs2.LODMinDistance_ = bin.ReadSingle();
                    cs2.AABBMax = Utilities.Consume<Vector3>(bin);
                    cs2.LODMaxDistance_ = bin.ReadSingle();
                    cs2.NextInLODGroup_ = bin.ReadInt32();
                    cs2.FirstModelInGroupForNextLOD_ = bin.ReadInt32();
                    cs2.MaterialLibraryIndex = bin.ReadInt32();
                    cs2.Unknown2_ = bin.ReadUInt32(); // NOTE: Flags?
                    cs2.UnknownIndex = bin.ReadInt32(); // NOTE: -1 means no index. Seems to be related to Next/Parent.
                    cs2.BlockSize = bin.ReadUInt32();
                    cs2.CollisionIndex_ = bin.ReadInt32(); // NODE: If this is not -1, model piece name starts with "COL_" and are always character models.
                    cs2.BoneArrayOffset_ = bin.ReadInt32(); // TODO: This indexes on the leftover data and points to an array of linearly growing indices.
                                          //  And the array count is BoneCount.
                    cs2.VertexFormatIndex = bin.ReadUInt16();
                    cs2.VertexFormatIndexLowDetail = bin.ReadUInt16();
                    bin.BaseStream.Position += 2; //this is VertexFormatIndex again
                    cs2.ScaleFactor = bin.ReadUInt16();
                    cs2.HeadRelated_ = bin.ReadInt16(); // NOTE: Seems to be valid on some 'HEAD' models, otherwise -1. Maybe morphing related???
                    cs2.VertexCount = bin.ReadUInt16();
                    cs2.IndexCount = bin.ReadUInt16();
                    cs2.BoneCount = bin.ReadUInt16();
                    Entries.Add(cs2);
                }

                //Read bone chunk (TODO: parse)
                _boneBuffer = bin.ReadBytes(bin.ReadInt32());
            }

            for (int i = 0;i < Entries.Count; i++)
            {
                //Console.WriteLine(_metadata[i].Unknown2_);
            }

            using (BinaryReader pak = new BinaryReader(File.OpenRead(_filepath)))
            {
                //Read & check the header info from the PAK
                pak.BaseStream.Position += 4; //Skip unused
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.ASSET_FILE) return false;
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.MODEL_DATA) return false;
                int EntryCount = BigEndianUtils.ReadInt32(pak);
                int EntryCount2 = BigEndianUtils.ReadInt32(pak);
                //if (EntryCount2 != EntryCount) return false;
                pak.BaseStream.Position += 12; //Skip unused

                //Get PAK entry information
                List<int> lengths = new List<int>();
                for (int i = 0; i < EntryCount; i++)
                {
                    pak.BaseStream.Position += 8;
                    int Length = BigEndianUtils.ReadInt32(pak);
                    int DataLength = BigEndianUtils.ReadInt32(pak); //TODO: Seems to be the aligned version of Length, aligned to 16 bytes.
                    int Offset = BigEndianUtils.ReadInt32(pak);
                    pak.BaseStream.Position += 12;
                    int UnknownIndex = BigEndianUtils.ReadInt16(pak);
                    int BINIndex = BigEndianUtils.ReadInt16(pak);
                    pak.BaseStream.Position += 12;

                    lengths.Add(DataLength);
                }

                //Pull all content from PAK
                List<byte[]> content = new List<byte[]>();
                foreach (int length in lengths)
                {
                    if (length == -1)
                        content.Add(new byte[] { });
                    else
                        content.Add(pak.ReadBytes(length));
                }
            }

            return true;
        }

        /* Save the file */
        override protected bool SaveInternal()
        {
            using (BinaryWriter bin = new BinaryWriter(File.OpenWrite(_filepathBIN)))
            {
                bin.BaseStream.SetLength(0);

                //Write header
                Utilities.WriteString("XMDB", bin);
                bin.Write(Entries.Count);
                bin.Write((Int16)FileIdentifiers.HEADER_FILE);
                bin.Write((Int16)FileIdentifiers.MODEL_DATA);
                bin.Write(_vertexFormats.Count);

                //Write vertex buffers
                for (int i = 0; i < _vertexFormats.Count; i++)
                {
                    for (int y = 0; y < _vertexFormats[i].Elements.Count; y++)
                    {
                        Utilities.Write<AlienVBFE>(bin, _vertexFormats[i].Elements[y]);
                        //TODO: last element must always have ArrayIndex = 255
                    }
                }

                //Write filenames/fileparts
                bin.Write(0); //placeholder
                Dictionary<string, int> stringOffsets = new Dictionary<string, int>();
                int startPos = (int)bin.BaseStream.Position;
                stringOffsets.Add("\\content\\build\\library\\", 0);
                Utilities.WriteString("\\content\\build\\library\\", bin, true);
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (!stringOffsets.ContainsKey(Entries[i].filename))
                    {
                        stringOffsets.Add(Entries[i].filename, (int)bin.BaseStream.Position - startPos);
                        Utilities.WriteString(Entries[i].filename, bin, true);
                    }
                    if (!stringOffsets.ContainsKey(Entries[i].modelPartName))
                    {
                        stringOffsets.Add(Entries[i].modelPartName, (int)bin.BaseStream.Position - startPos);
                        Utilities.WriteString(Entries[i].modelPartName, bin, true);
                    }
                }
                Utilities.Align(bin, 16);
                int stringLength = (int)bin.BaseStream.Position - startPos;
                bin.BaseStream.Position = startPos - 4;
                bin.Write(stringLength);
                bin.BaseStream.Position += stringLength;

                //Write model metadata
                for (int i = 0; i < Entries.Count; i++)
                {
                    bin.Write(stringOffsets[Entries[i].filename]);
                    bin.Write(new byte[4]);
                    bin.Write(stringOffsets[Entries[i].modelPartName]);
                    bin.Write(new byte[4]);
                    Utilities.Write<Vector3>(bin, Entries[i].AABBMin);
                    bin.Write(Entries[i].LODMinDistance_);
                    Utilities.Write<Vector3>(bin, Entries[i].AABBMax);
                    bin.Write(Entries[i].LODMaxDistance_);
                    bin.Write(Entries[i].NextInLODGroup_);
                    bin.Write(Entries[i].FirstModelInGroupForNextLOD_);
                    bin.Write(Entries[i].MaterialLibraryIndex);
                    bin.Write(Entries[i].Unknown2_);
                    bin.Write(Entries[i].UnknownIndex);
                    bin.Write(Entries[i].BlockSize);
                    bin.Write(Entries[i].CollisionIndex_);
                    bin.Write(Entries[i].BoneArrayOffset_);
                    bin.Write(Entries[i].VertexFormatIndex);
                    bin.Write(Entries[i].VertexFormatIndexLowDetail);
                    bin.Write(Entries[i].VertexFormatIndex);
                    bin.Write(Entries[i].ScaleFactor);
                    bin.Write(Entries[i].HeadRelated_);
                    bin.Write(Entries[i].VertexCount);
                    bin.Write(Entries[i].IndexCount);
                    bin.Write(Entries[i].BoneCount);
                }

                //Bone buffer (TODO)
                bin.Write(_boneBuffer.Length);
                bin.Write(_boneBuffer);
            }
            return true;
        }
        #endregion

        #region ACCESSORS

        #endregion

        #region HELPERS

        #endregion

        #region STRUCTURES
        public class AlienVBF
        {
            public int ElementCount;
            public List<AlienVBFE> Elements;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct AlienVBFE
        {
            public int ArrayIndex;
            public int Offset; // NOTE: Offset within data structure, generally not important.
            public VBFE_InputType VariableType; //(int)
            public VBFE_InputSlot ShaderSlot; //(int)
            public int VariantIndex; // NOTE: Variant index such as UVs: (UV0, UV1, UV2...)
            public int Unknown_; // NOTE: Seems to be always 2?
        };

        public enum VBFE_InputType
        {
            AlienVertexInputType_v3 = 0x03,
            // TODO: Present at 'bsp_torrens' but I haven't seen models that contain that being rendered yet.
            AlienVertexInputType_Unknown0_ = 0x04,
            AlienVertexInputType_u32_C = 0x05,
            AlienVertexInputType_v4u8_i = 0x06,
            AlienVertexInputType_v4u8_f = 0x09,
            AlienVertexInputType_v2s16_UV = 0x0A,
            AlienVertexInputType_v4s16_f = 0x0B,
            AlienVertexInputType_v4u8_NTB = 0x0F,
            AlienVertexInputType_u16 = 0x13,
        };

        public enum VBFE_InputSlot
        {
            AlienVertexInputSlot_P = 0x01,
            AlienVertexInputSlot_BW = 0x02, // NOTE: Bone Weights
            AlienVertexInputSlot_BI = 0x03, // NOTE: Bone Indices
            AlienVertexInputSlot_N = 0x04,
            AlienVertexInputSlot_UV = 0x06,
            AlienVertexInputSlot_T = 0x07, // NOTE: Tangent
            AlienVertexInputSlot_B = 0x08, // NOTE: Bitangent
            AlienVertexInputSlot_C = 0x0A, // NOTE: Color? Specular? What is this?
        };

        public class CS2
        {
            public string filename;
            public string modelPartName;

            public Vector3 AABBMin;
            public Vector3 AABBMax;

            public float LODMinDistance_;
            public float LODMaxDistance_;
            public int NextInLODGroup_;
            public int FirstModelInGroupForNextLOD_;

            public int MaterialLibraryIndex;

            public uint Unknown2_; // NOTE: Flags?
            public int UnknownIndex; // NOTE: -1 means no index. Seems to be related to Next/Parent.
            public uint BlockSize;
            public int CollisionIndex_; // NODE: If this is not -1, model piece name starts with "COL_" and are always character models.
            public int BoneArrayOffset_; // TODO: This indexes on the leftover data and points to an array of linearly growing indices. And the array count is BoneCount.

            public UInt16 VertexFormatIndex;
            public UInt16 VertexFormatIndexLowDetail;
            public UInt16 VertexFormatIndexDefault_; // TODO: This is always equals to the VertexFormatIndex

            public UInt16 ScaleFactor;
            public Int16 HeadRelated_; // NOTE: Seems to be valid on some 'HEAD' models, otherwise -1. Maybe morphing related???

            public UInt16 VertexCount;
            public UInt16 IndexCount;
            public UInt16 BoneCount;

            public override string ToString()
            {
                return filename + " -> " + modelPartName;
            }
        }       
        #endregion
    }
}