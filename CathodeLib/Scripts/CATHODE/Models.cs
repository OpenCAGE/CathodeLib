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
using System.Runtime.ExceptionServices;
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

            Dictionary<int, CS2_submesh> submeshBinIndexes = new Dictionary<int, CS2_submesh>();
            using (BinaryReader bin = new BinaryReader(File.OpenRead(_filepathBIN)))
            {
                bin.BaseStream.Position += 4; //Magic
                int modelCount = bin.ReadInt32();
                if ((FileIdentifiers)bin.ReadInt16() != FileIdentifiers.HEADER_FILE) return false;
                if ((FileIdentifiers)bin.ReadInt16() != FileIdentifiers.MODEL_DATA) return false;
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

                //Read filenames and submesh names
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
                    int FileNameOffset = bin.ReadInt32();
                    bin.BaseStream.Position += 4; //skip unused
                    string filename = stringOffsets[FileNameOffset];

                    CS2 cs2 = Entries.FirstOrDefault(o => o.name == filename);
                    if (cs2 == null)
                    {
                        cs2 = new CS2();
                        cs2.name = filename;
                        Entries.Add(cs2);
                    }

                    CS2_submesh submesh = new CS2_submesh();
                    int ModelPartNameOffset = bin.ReadInt32();
                    submesh.name = stringOffsets[ModelPartNameOffset];
                    bin.BaseStream.Position += 4; //skip unused
                    submesh.AABBMin = Utilities.Consume<Vector3>(bin);
                    submesh.LODMinDistance_ = bin.ReadSingle();
                    submesh.AABBMax = Utilities.Consume<Vector3>(bin);
                    submesh.LODMaxDistance_ = bin.ReadSingle();
                    submesh.NextInLODGroup_ = bin.ReadInt32();
                    submesh.FirstModelInGroupForNextLOD_ = bin.ReadInt32();
                    submesh.MaterialLibraryIndex = bin.ReadInt32();
                    submesh.Unknown2_ = bin.ReadUInt32(); // NOTE: Flags?
                    submesh.UnknownIndex = bin.ReadInt32(); // NOTE: -1 means no index. Seems to be related to Next/Parent.
                    submesh.BlockSize = bin.ReadUInt32();
                    submesh.CollisionIndex_ = bin.ReadInt32(); // NODE: If this is not -1, model piece name starts with "COL_" and are always character models.
                    submesh.BoneArrayOffset_ = bin.ReadInt32(); // TODO: This indexes on the leftover data and points to an array of linearly growing indices.
                                          //  And the array count is BoneCount.
                    submesh.VertexFormatIndex = bin.ReadUInt16();
                    submesh.VertexFormatIndexLowDetail = bin.ReadUInt16();
                    bin.BaseStream.Position += 2; //this is VertexFormatIndex again
                    submesh.ScaleFactor = bin.ReadUInt16();
                    submesh.HeadRelated_ = bin.ReadInt16(); // NOTE: Seems to be valid on some 'HEAD' models, otherwise -1. Maybe morphing related???
                    submesh.VertexCount = bin.ReadUInt16();
                    submesh.IndexCount = bin.ReadUInt16();
                    submesh.BoneCount = bin.ReadUInt16();
                    cs2.submeshes.Add(submesh);

                    submeshBinIndexes.Add(i, submesh);
                }

                //Read bone chunk (TODO: parse)
                _boneBuffer = bin.ReadBytes(bin.ReadInt32());
            }

            /*
            for (int i = 0; i < Entries.Count; i++)
            {
                for (int y = 0; y < Entries[i].submeshes.Count; y++)
                {
                    if (Entries[i].submeshes[y].LODMaxDistance_ != 10000)
                    {
                        string sdff = "";
                    }
                    if (Entries[i].submeshes[y].LODMinDistance_ != 0)
                    {
                        string sdff = "";
                    }
                }
            }
            */

            using (BinaryReader pak = new BinaryReader(File.OpenRead(_filepath)))
            {
                //Read & check the header info from the PAK
                pak.BaseStream.Position += 4; //Skip unused
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.ASSET_FILE) return false;
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(pak) != FileIdentifiers.MODEL_DATA) return false;
                int entryCount = BigEndianUtils.ReadInt32(pak);       // Number of non-empty entries
                int entryCountActual = BigEndianUtils.ReadInt32(pak); // Total number of entries including empty ones
                pak.BaseStream.Position += 12; //Skip unused

                int endOfHeaders = 32 + (entryCountActual * 48);
                for (int i = 0; i < entryCount; i++)
                {
                    //Read model info
                    pak.BaseStream.Position += 8;
                    int length = BigEndianUtils.ReadInt32(pak);
                    pak.BaseStream.Position += 4; //length again 
                    int offset = BigEndianUtils.ReadInt32(pak);
                    pak.BaseStream.Position += 10;
                    int unk1 = BigEndianUtils.ReadInt16(pak);  //used to store some info on BSP_LV426_PT01
                    pak.BaseStream.Position += 2;
                    int binIndex = BigEndianUtils.ReadInt16(pak);
                    pak.BaseStream.Position += 8;
                    int unk2 = BigEndianUtils.ReadInt16(pak); //used to store info on BSP_LV426_PT02
                    int unk3 = BigEndianUtils.ReadInt16(pak); //used to store info on BSP_LV426_PT02

                    //Read content
                    int offsetToReturnTo = (int)pak.BaseStream.Position;
                    pak.BaseStream.Position = endOfHeaders + offset;
                    submeshBinIndexes[binIndex].content = pak.ReadBytes(length);
                    pak.BaseStream.Position = offsetToReturnTo;
                }
            }

            return true;
        }

        /* Save the file */
        override protected bool SaveInternal()
        {
            int submeshCount = 0;
            for (int i = 0; i < Entries.Count; i++)
                submeshCount += Entries[i].submeshes.Count;

            int countWithContent = 0;
            for (int i = 0; i < Entries.Count; i++)
                for (int x = 0; x < Entries[i].submeshes.Count; x++)
                    if (Entries[i].submeshes[x].content != null)
                        countWithContent++;

            Dictionary<CS2_submesh, int> binIndexes = new Dictionary<CS2_submesh, int>();
            using (BinaryWriter bin = new BinaryWriter(File.OpenWrite(_filepathBIN)))
            {
                bin.BaseStream.SetLength(0);

                //Write header
                Utilities.WriteString("XMDB", bin);
                bin.Write(submeshCount);
                bin.Write((Int16)FileIdentifiers.HEADER_FILE);
                bin.Write((Int16)FileIdentifiers.MODEL_DATA);
                bin.Write(_vertexFormats.Count);

                //Write vertex buffers
                for (int i = 0; i < _vertexFormats.Count; i++)
                {
                    for (int x = 0; x < _vertexFormats[i].Elements.Count; x++)
                    {
                        Utilities.Write<AlienVBFE>(bin, _vertexFormats[i].Elements[x]);
                        //TODO: last element must always have ArrayIndex = 255
                    }
                }

                //Write filenames and submesh names
                bin.Write(0); //placeholder
                Dictionary<string, int> stringOffsets = new Dictionary<string, int>();
                int startPos = (int)bin.BaseStream.Position;
                stringOffsets.Add("\\content\\build\\library\\", 0);
                Utilities.WriteString("\\content\\build\\library\\", bin, true);
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (!stringOffsets.ContainsKey(Entries[i].name))
                    {
                        stringOffsets.Add(Entries[i].name, (int)bin.BaseStream.Position - startPos);
                        Utilities.WriteString(Entries[i].name, bin, true);
                    }
                    for (int x = 0; x < Entries[i].submeshes.Count; x++)
                    {
                        if (!stringOffsets.ContainsKey(Entries[i].submeshes[x].name))
                        {
                            stringOffsets.Add(Entries[i].submeshes[x].name, (int)bin.BaseStream.Position - startPos);
                            Utilities.WriteString(Entries[i].submeshes[x].name, bin, true);
                        }
                    }
                }
                Utilities.Align(bin, 16);
                int stringLength = (int)bin.BaseStream.Position - startPos;
                bin.BaseStream.Position = startPos - 4;
                bin.Write(stringLength);
                bin.BaseStream.Position += stringLength;

                //Write model metadata
                int y = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < Entries[i].submeshes.Count; x++)
                    {
                        bin.Write(stringOffsets[Entries[i].name]);
                        bin.Write(new byte[4]);
                        bin.Write(stringOffsets[Entries[i].submeshes[x].name]);
                        bin.Write(new byte[4]);
                        Utilities.Write<Vector3>(bin, Entries[i].submeshes[x].AABBMin);
                        bin.Write(Entries[i].submeshes[x].LODMinDistance_);
                        Utilities.Write<Vector3>(bin, Entries[i].submeshes[x].AABBMax);
                        bin.Write(Entries[i].submeshes[x].LODMaxDistance_);
                        bin.Write(Entries[i].submeshes[x].NextInLODGroup_);
                        bin.Write(Entries[i].submeshes[x].FirstModelInGroupForNextLOD_);
                        bin.Write(Entries[i].submeshes[x].MaterialLibraryIndex);
                        bin.Write(Entries[i].submeshes[x].Unknown2_);
                        bin.Write(Entries[i].submeshes[x].UnknownIndex);
                        bin.Write(Entries[i].submeshes[x].BlockSize);
                        bin.Write(Entries[i].submeshes[x].CollisionIndex_);
                        bin.Write(Entries[i].submeshes[x].BoneArrayOffset_);
                        bin.Write(Entries[i].submeshes[x].VertexFormatIndex);
                        bin.Write(Entries[i].submeshes[x].VertexFormatIndexLowDetail);
                        bin.Write(Entries[i].submeshes[x].VertexFormatIndex);
                        bin.Write(Entries[i].submeshes[x].ScaleFactor);
                        bin.Write(Entries[i].submeshes[x].HeadRelated_);
                        bin.Write(Entries[i].submeshes[x].VertexCount);
                        bin.Write(Entries[i].submeshes[x].IndexCount);
                        bin.Write(Entries[i].submeshes[x].BoneCount);

                        binIndexes.Add(Entries[i].submeshes[x], y);
                        y++;
                    }
                }

                //Bone buffer (TODO)
                bin.Write(_boneBuffer.Length);
                bin.Write(_boneBuffer);
            }

            using (BinaryWriter pak = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                //Write model content
                int contentOffset = 32 + (countWithContent * 48);
                pak.BaseStream.SetLength(contentOffset);
                pak.BaseStream.Position = contentOffset;
                List<int> offsets = new List<int>();
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < Entries[i].submeshes.Count; x++)
                    {
                        if (Entries[i].submeshes[x].content == null) continue;
                        offsets.Add((int)pak.BaseStream.Position - contentOffset);
                        pak.Write(Entries[i].submeshes[x].content);
                    }
                }

                //Write model headers
                pak.BaseStream.Position = 32;
                int y = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < Entries[i].submeshes.Count; x++)
                    {
                        if (Entries[i].submeshes[x].content == null) continue;
                        pak.Write(new byte[8]);
                        pak.Write(BigEndianUtils.FlipEndian((Int32)Entries[i].submeshes[x].content.Length));
                        pak.Write(BigEndianUtils.FlipEndian((Int32)Entries[i].submeshes[x].content.Length));
                        pak.Write(BigEndianUtils.FlipEndian((Int32)offsets[y]));

                        pak.Write(new byte[5]);
                        pak.Write(new byte[2] { 0x01, 0x01 });
                        pak.Write(new byte[3]);

                        pak.Write(new byte[2]); //used to store some info on BSP_LV426_PT01
                        pak.Write(new byte[2]);
                        pak.Write(BigEndianUtils.FlipEndian((Int16)binIndexes[Entries[i].submeshes[x]]));
                        pak.Write(new byte[8]);
                        pak.Write(new byte[2]); //used to store info on BSP_LV426_PT02
                        pak.Write(new byte[2]); //used to store info on BSP_LV426_PT02
                        y++;
                    }
                }

                //Write header
                pak.BaseStream.Position = 0;
                pak.Write(new byte[4]);
                pak.Write(BigEndianUtils.FlipEndian((Int32)FileIdentifiers.ASSET_FILE));
                pak.Write(BigEndianUtils.FlipEndian((Int32)FileIdentifiers.MODEL_DATA));
                pak.Write(BigEndianUtils.FlipEndian((Int32)y));
                pak.Write(BigEndianUtils.FlipEndian((Int32)y)); //we don't bother writing empty entries
                pak.Write(BigEndianUtils.FlipEndian((Int32)16));
                pak.Write(BigEndianUtils.FlipEndian((Int32)1));
                pak.Write(BigEndianUtils.FlipEndian((Int32)1));
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
            public string name;
            public List<CS2_submesh> submeshes = new List<CS2_submesh>();

            public override string ToString()
            {
                return name;
            }
        }

        public class CS2_submesh
        { 
            public string name;

            /* FROM BIN */

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

            public UInt16 ScaleFactor;
            public Int16 HeadRelated_; // NOTE: Seems to be valid on some 'HEAD' models, otherwise -1. Maybe morphing related???

            public UInt16 VertexCount;
            public UInt16 IndexCount;
            public UInt16 BoneCount;

            /* FROM PAK */

            public byte[] content = null;

            public override string ToString()
            {
                return name;
            }
        }       
        #endregion
    }
}