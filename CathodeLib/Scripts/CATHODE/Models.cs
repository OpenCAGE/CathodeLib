using CATHODE.LEGACY.Assets;
using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using static CATHODE.Models.CS2;
using Newtonsoft.Json;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.TextFormatting;
#endif

namespace CATHODE
{
    /* Handles Cathode LEVEL_MODELS.PAK/MODELS_LEVEL.BIN files */
    public class Models : CathodeFile
    {
        public List<CS2> Entries = new List<CS2>();

        public int SubmeshCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Entries.Count; i++)
                    count += Entries[i].Submeshes.Count;
                return count;
            }
        }

        private string _filepathBIN;

        public Models(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        override protected bool LoadInternal()
        {
            _filepathBIN = _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + "MODELS_" + Path.GetFileName(_filepath).Substring(0, Path.GetFileName(_filepath).Length - 11) + ".BIN";

            if (!File.Exists(_filepathBIN)) return false;

            Dictionary<int, CS2.Submesh> submeshBinIndexes = new Dictionary<int, CS2.Submesh>();
            using (BinaryReader bin = new BinaryReader(File.OpenRead(_filepathBIN)))
            {
                bin.BaseStream.Position += 4; //Magic
                int modelCount = bin.ReadInt32();
                if ((FileIdentifiers)bin.ReadInt16() != FileIdentifiers.HEADER_FILE) return false;
                if ((FileIdentifiers)bin.ReadInt16() != FileIdentifiers.MODEL_DATA) return false;
                int vbfCount = bin.ReadInt32();

                //Read vertex buffer formats
                List<AlienVBF> vertexFormats = new List<AlienVBF>(vbfCount);
                for (int EntryIndex = 0; EntryIndex < vbfCount; ++EntryIndex)
                {
                    long startPos = bin.BaseStream.Position;
                    int count = 1;
                    while (bin.ReadByte() != 0xFF)
                    {
                        bin.BaseStream.Position += Marshal.SizeOf(typeof(AlienVBF.Element)) - 1;
                        count++;
                    }
                    bin.BaseStream.Position = startPos;

                    AlienVBF VertexInput = new AlienVBF();
                    VertexInput.ElementCount = count;
                    VertexInput.Elements = Utilities.ConsumeArray<AlienVBF.Element>(bin, VertexInput.ElementCount).ToList();
                    vertexFormats.Add(VertexInput);
                }

                //Read filenames and submesh names
                Dictionary<int, string> stringOffsets = new Dictionary<int, string>();
                byte[] filenames = bin.ReadBytes(bin.ReadInt32());
                using (BinaryReader reader = new BinaryReader(new MemoryStream(filenames)))
                {
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                        stringOffsets.Add((int)reader.BaseStream.Position, Utilities.ReadString(reader)/*.Replace('\\', '/')*/);
                }

                //Read model metadata
                Dictionary<CS2.Submesh, int> boneOffsets = new Dictionary<CS2.Submesh, int>();
                for (int i = 0; i < modelCount; i++)
                {
                    int FileNameOffset = bin.ReadInt32();
                    bin.BaseStream.Position += 4; //skip unused
                    string filename = stringOffsets[FileNameOffset];

                    CS2 cs2 = Entries.FirstOrDefault(o => o.Name == filename);
                    if (cs2 == null)
                    {
                        cs2 = new CS2();
                        cs2.Name = filename;
                        Entries.Add(cs2);
                    }

                    CS2.Submesh submesh = new CS2.Submesh();
                    int ModelPartNameOffset = bin.ReadInt32();
                    submesh.Name = stringOffsets[ModelPartNameOffset];
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
                    bin.BaseStream.Position += 4; //length
                    submesh.CollisionIndex_ = bin.ReadInt32(); // NODE: If this is not -1, model piece name starts with "COL_" and are always character models.
                    int boneArrayOffset = bin.ReadInt32();

                    int vbfIndex = bin.ReadUInt16();
                    int vbfIndexLowDetail = bin.ReadUInt16();
                    submesh.VertexFormat = vertexFormats[vbfIndex];
                    submesh.VertexFormatLowDetail = vertexFormats[vbfIndexLowDetail];

                    bin.BaseStream.Position += 2; //this is VertexFormatIndex again
                    submesh.ScaleFactor = bin.ReadUInt16();
                    submesh.HeadRelated_ = bin.ReadInt16(); // NOTE: Seems to be valid on some 'HEAD' models, otherwise -1. Maybe morphing related???
                    submesh.VertexCount = bin.ReadUInt16();
                    submesh.IndexCount = bin.ReadUInt16();
                    submesh.boneIndices.Capacity = bin.ReadUInt16();
                    cs2.Submeshes.Add(submesh);

                    submeshBinIndexes.Add(i, submesh);

                    if (submesh.boneIndices.Capacity != 0)
                        boneOffsets.Add(submesh, boneArrayOffset);
                }

                //Read bone data
                byte[] bones = bin.ReadBytes(bin.ReadInt32());
                using (BinaryReader reader = new BinaryReader(new MemoryStream(bones)))
                {
                    foreach (KeyValuePair<CS2.Submesh, int> offset in boneOffsets)
                    {
                        reader.BaseStream.Position = offset.Value;
                        for (int i = 0; i < offset.Key.boneIndices.Capacity; i++)
                            offset.Key.boneIndices.Add((int)reader.ReadByte());
                    }
                }
            }

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

                    //Read submesh content
                    int offsetToReturnTo = (int)pak.BaseStream.Position;
                    pak.BaseStream.Position = endOfHeaders + offset;
                    byte[] content = pak.ReadBytes(length);
                    using (BinaryReader reader = new BinaryReader(new MemoryStream(content)))
                    {
                        int firstIndex = BigEndianUtils.ReadInt32(reader);
                        int submeshCount = BigEndianUtils.ReadInt32(reader);

                        reader.BaseStream.Position += 16;
                        Dictionary<int, int[]> entryOffsets = new Dictionary<int, int[]>(); //bin index, [start,length]
                        for (int x = 0; x < submeshCount; x++)
                        {
                            entryOffsets.Add(BigEndianUtils.ReadInt32(reader), new int[2] { BigEndianUtils.ReadInt32(reader), BigEndianUtils.ReadInt32(reader) });
                            reader.BaseStream.Position += 4;
                        }
                        reader.BaseStream.Position += 8;

                        foreach (KeyValuePair<int, int[]> offsetData in entryOffsets)
                        {
                            CS2.Submesh submesh = GetSubmeshForWriteIndex(offsetData.Key);
                            reader.BaseStream.Position = offsetData.Value[0];
                            submesh.content = reader.ReadBytes(offsetData.Value[1]);
                        }

                    }
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
                submeshCount += Entries[i].Submeshes.Count;

            int countWithContent = 0;
            for (int i = 0; i < Entries.Count; i++)
                for (int x = 0; x < Entries[i].Submeshes.Count; x++)
                    if (Entries[i].Submeshes[x].content.Length != 0)
                        countWithContent++;

            List<AlienVBF> vertexFormats = new List<AlienVBF>();
            for (int i = 0; i < Entries.Count; i++)
            {
                for (int x = 0; x < Entries[i].Submeshes.Count; x++)
                {
                    if (!vertexFormats.Contains(Entries[i].Submeshes[x].VertexFormat))
                        vertexFormats.Add(Entries[i].Submeshes[x].VertexFormat);
                    if (!vertexFormats.Contains(Entries[i].Submeshes[x].VertexFormatLowDetail))
                        vertexFormats.Add(Entries[i].Submeshes[x].VertexFormatLowDetail);
                }
            }

            using (BinaryWriter bin = new BinaryWriter(File.OpenWrite(_filepathBIN)))
            {
                bin.BaseStream.SetLength(0);

                //Write header
                Utilities.WriteString("XMDB", bin);
                bin.Write(submeshCount);
                bin.Write((Int16)FileIdentifiers.HEADER_FILE);
                bin.Write((Int16)FileIdentifiers.MODEL_DATA);
                bin.Write(vertexFormats.Count);

                //Write vertex buffers
                for (int i = 0; i < vertexFormats.Count; i++)
                {
                    for (int x = 0; x < vertexFormats[i].Elements.Count; x++)
                    {
                        Utilities.Write<AlienVBF.Element>(bin, vertexFormats[i].Elements[x]);
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
                    if (!stringOffsets.ContainsKey(Entries[i].Name))
                    {
                        stringOffsets.Add(Entries[i].Name, (int)bin.BaseStream.Position - startPos);
                        Utilities.WriteString(Entries[i].Name, bin, true);
                    }
                    for (int x = 0; x < Entries[i].Submeshes.Count; x++)
                    {
                        if (!stringOffsets.ContainsKey(Entries[i].Submeshes[x].Name))
                        {
                            stringOffsets.Add(Entries[i].Submeshes[x].Name, (int)bin.BaseStream.Position - startPos);
                            Utilities.WriteString(Entries[i].Submeshes[x].Name, bin, true);
                        }
                    }
                }
                bin.Write((char)0x00);
                Utilities.Align(bin, 16);
                int stringLength = (int)bin.BaseStream.Position - startPos;
                bin.BaseStream.Position = startPos - 4;
                bin.Write(stringLength);
                bin.BaseStream.Position += stringLength;

                //Write model metadata
                int boneOffset = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < Entries[i].Submeshes.Count; x++)
                    {
                        bin.Write((Int32)stringOffsets[Entries[i].Name]);
                        bin.Write(new byte[4]);
                        bin.Write((Int32)stringOffsets[Entries[i].Submeshes[x].Name]);
                        bin.Write(new byte[4]);
                        Utilities.Write<Vector3>(bin, Entries[i].Submeshes[x].AABBMin);
                        bin.Write((float)Entries[i].Submeshes[x].LODMinDistance_);
                        Utilities.Write<Vector3>(bin, Entries[i].Submeshes[x].AABBMax);
                        bin.Write((float)Entries[i].Submeshes[x].LODMaxDistance_);
                        bin.Write((Int32)Entries[i].Submeshes[x].NextInLODGroup_);
                        bin.Write((Int32)Entries[i].Submeshes[x].FirstModelInGroupForNextLOD_);
                        bin.Write((Int32)Entries[i].Submeshes[x].MaterialLibraryIndex);
                        bin.Write((Int32)Entries[i].Submeshes[x].Unknown2_);
                        bin.Write((Int32)Entries[i].Submeshes[x].UnknownIndex);
                        bin.Write((Int32)Entries[i].Submeshes[x].content.Length);
                        bin.Write((Int32)Entries[i].Submeshes[x].CollisionIndex_);
                        bin.Write((Int32)boneOffset);
                        bin.Write((Int16)vertexFormats.IndexOf(Entries[i].Submeshes[x].VertexFormat));
                        bin.Write((Int16)vertexFormats.IndexOf(Entries[i].Submeshes[x].VertexFormatLowDetail));
                        bin.Write((Int16)vertexFormats.IndexOf(Entries[i].Submeshes[x].VertexFormat));
                        bin.Write((Int16)Entries[i].Submeshes[x].ScaleFactor);
                        bin.Write((Int16)Entries[i].Submeshes[x].HeadRelated_);
                        bin.Write((Int16)Entries[i].Submeshes[x].VertexCount);
                        bin.Write((Int16)Entries[i].Submeshes[x].IndexCount);
                        bin.Write((Int16)Entries[i].Submeshes[x].boneIndices.Count);

                        boneOffset += Entries[i].Submeshes[x].boneIndices.Count;
                    }
                }

                //Bone data
                bin.Write(boneOffset);
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < Entries[i].Submeshes.Count; x++)
                    {
                        if (Entries[i].Submeshes[x].boneIndices.Count == 0) continue;
                        for (int z = 0; z < Entries[i].Submeshes[x].boneIndices.Count; z++)
                            bin.Write((byte)Entries[i].Submeshes[x].boneIndices[z]);
                    }
                }
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
                    for (int x = 0; x < Entries[i].Submeshes.Count; x++)
                    {
                        if (Entries[i].Submeshes[x].content.Length == 0) continue;
                        offsets.Add((int)pak.BaseStream.Position - contentOffset);

                        pak.Write(BigEndianUtils.FlipEndian((Int32)GetWriteIndexForSubmesh(Entries[i].Submeshes[x])));
                        pak.Write(BigEndianUtils.FlipEndian((Int32)1));
                        pak.Write(new byte[16]);
                        pak.Write(BigEndianUtils.FlipEndian((Int32)GetWriteIndexForSubmesh(Entries[i].Submeshes[x])));
                        pak.Write(BigEndianUtils.FlipEndian((Int32)48));
                        pak.Write(BigEndianUtils.FlipEndian((Int32)Entries[i].Submeshes[x].content.Length));
                        pak.Write(new byte[12]);
                        pak.Write(Entries[i].Submeshes[x].content);
                    }
                }

                //Write model headers
                pak.BaseStream.Position = 32;
                int y = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < Entries[i].Submeshes.Count; x++)
                    {
                        if (Entries[i].Submeshes[x].content.Length == 0) continue;
                        pak.Write(new byte[8]);
                        pak.Write(BigEndianUtils.FlipEndian((Int32)Entries[i].Submeshes[x].content.Length + 48));
                        pak.Write(BigEndianUtils.FlipEndian((Int32)Entries[i].Submeshes[x].content.Length + 48));
                        pak.Write(BigEndianUtils.FlipEndian((Int32)offsets[y]));

                        pak.Write(new byte[5]);
                        pak.Write(new byte[2] { 0x01, 0x01 });
                        pak.Write(new byte[3]);

                        pak.Write(new byte[2]); //used to store some info on BSP_LV426_PT01
                        pak.Write(new byte[2]);
                        pak.Write(BigEndianUtils.FlipEndian((Int16)GetWriteIndexForSubmesh(Entries[i].Submeshes[x])));
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
        /* Get a mesh from the models PAK as a usable mesh format */
#if UNITY_EDITOR || UNITY_STANDALONE
        public Mesh GetMesh(CS2.Submesh submesh)
#else
        public Model3DGroup GetMesh(CS2.Submesh submesh)
#endif
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            Mesh mesh = new Mesh();
#else
            Model3DGroup mesh = new Model3DGroup();
#endif

            List<List<AlienVBF.Element>> formats = new List<List<AlienVBF.Element>>();
            foreach (AlienVBF.Element formatElement in submesh.VertexFormat.Elements)
            {
                if (formatElement.ArrayIndex == 0xFF)
                {
                    formats.Add(new List<AlienVBF.Element>() { formatElement });
                    continue;
                }

                while (formats.Count - 1 < formatElement.ArrayIndex)
                    formats.Add(new List<AlienVBF.Element>());

                formats[formatElement.ArrayIndex].Add(formatElement);
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            List<UInt16> indices = new List<UInt16>();
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> tangents = new List<Vector4>();
            List<Vector2> uv0 = new List<Vector2>();
            List<Vector2> uv1 = new List<Vector2>();
            List<Vector2> uv2 = new List<Vector2>();
            List<Vector2> uv3 = new List<Vector2>();
            List<Vector2> uv7 = new List<Vector2>();
#else
            Int32Collection indices = new Int32Collection();
            Point3DCollection vertices = new Point3DCollection();
            Vector3DCollection normals = new Vector3DCollection();
            List<Vector4> tangents = new List<Vector4>();
            PointCollection uv0 = new PointCollection();
            PointCollection uv1 = new PointCollection();
            PointCollection uv2 = new PointCollection();
            PointCollection uv3 = new PointCollection();
            PointCollection uv7 = new PointCollection();
#endif

            //TODO: implement skeleton lookup for the indexes
            List<Vector4> boneIndex = new List<Vector4>(); //The indexes of 4 bones that affect each vertex
            List<Vector4> boneWeight = new List<Vector4>(); //The weights for each bone

            if (submesh.content.Length == 0)
                return mesh;

            using (BinaryReader reader = new BinaryReader(new MemoryStream(submesh.content)))
            {
                for (int i = 0; i < formats.Count; ++i)
                {
                    if (formats[i][0].ArrayIndex == 0xFF)
                    {
                        for (int x = 0; x < submesh.IndexCount; x++)
                            indices.Add(reader.ReadUInt16());

                        continue;
                    }

                    for (int x = 0; x < submesh.VertexCount; ++x)
                    {
                        for (int y = 0; y < formats[i].Count; ++y)
                        {
                            AlienVBF.Element format = formats[i][y];
                            switch (format.VariableType)
                            {
                                case VBFE_InputType.VECTOR3:
                                    {
#if UNITY_EDITOR || UNITY_STANDALONE
                                        Vector3 v = new Vector3(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
#else
                                        Vector3D v = new Vector3D(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
#endif
                                        switch (format.ShaderSlot)
                                        {
                                            case VBFE_InputSlot.NORMAL:
                                                normals.Add(v);
                                                break;
                                            case VBFE_InputSlot.TANGENT:
#if UNITY_EDITOR || UNITY_STANDALONE
                                                tangents.Add(new Vector4((float)v.x, (float)v.y, (float)v.z, 0));
#else
                                                tangents.Add(new Vector4((float)v.X, (float)v.Y, (float)v.Z, 0));
#endif
                                                break;
                                            case VBFE_InputSlot.UV:
                                                //TODO: 3D UVW
                                                break;
                                        };
                                        break;
                                    }
                                case VBFE_InputType.INT32:
                                    {
                                        int v = reader.ReadInt32();
                                        switch (format.ShaderSlot)
                                        {
                                            case VBFE_InputSlot.COLOUR:
                                                //??
                                                break;
                                        }
                                        break;
                                    }
                                case VBFE_InputType.VECTOR4_BYTE:
                                    {
                                        Vector4 v = new Vector4(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                                        switch (format.ShaderSlot)
                                        {
                                            case VBFE_InputSlot.BONE_INDICES:
                                                boneIndex.Add(v);
                                                break;
                                        }
                                        break;
                                    }
                                case VBFE_InputType.VECTOR4_BYTE_DIV255:
                                    {
                                        Vector4 v = new Vector4(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                                        v /= 255.0f;
                                        switch (format.ShaderSlot)
                                        {
#if UNITY_EDITOR || UNITY_STANDALONE
                                            case VBFE_InputSlot.BONE_WEIGHTS:
                                                boneWeight.Add(v / (v.x + v.y + v.z + v.w));
                                                break;
                                            case VBFE_InputSlot.UV:
                                                uv2.Add(new Vector2(v.x, v.y));
                                                uv3.Add(new Vector2(v.z, v.w));
                                                break;
#else
                                            case VBFE_InputSlot.BONE_WEIGHTS:
                                                boneWeight.Add(v / (v.X + v.Y + v.Z + v.W));
                                                break;
                                            case VBFE_InputSlot.UV:
                                                uv2.Add(new System.Windows.Point(v.X, v.Y));
                                                uv3.Add(new System.Windows.Point(v.Z, v.W));
                                                break;
#endif
                                        }
                                        break;
                                    }
                                case VBFE_InputType.VECTOR2_INT16_DIV2048:
                                    {
#if UNITY_EDITOR || UNITY_STANDALONE
                                        Vector2 v = new Vector2(reader.ReadInt16(), reader.ReadInt16());
                                        v /= 2048.0f;
#else
                                        System.Windows.Point v = new System.Windows.Point(reader.ReadInt16() / 2048.0f, reader.ReadInt16() / 2048.0f);
#endif
                                        switch (format.ShaderSlot)
                                        {
                                            case VBFE_InputSlot.UV:
                                                if (format.VariantIndex == 0) uv0.Add(v);
                                                else if (format.VariantIndex == 1)
                                                {
                                                    // TODO: We can figure this out based on AlienVBFE.
                                                    //Material->Material.Flags |= Material_HasTexCoord1;
                                                    uv1.Add(v);
                                                }
                                                else if (format.VariantIndex == 2) uv2.Add(v);
                                                else if (format.VariantIndex == 3) uv3.Add(v);
                                                else if (format.VariantIndex == 7) uv7.Add(v);
                                                break;
                                        }
                                        break;
                                    }
                                case VBFE_InputType.VECTOR4_INT16_DIVMAX:
                                    {
                                        Vector4 v = new Vector4(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16());
                                        v /= (float)Int16.MaxValue;
                                        switch (format.ShaderSlot)
                                        {
                                            case VBFE_InputSlot.VERTEX:
#if UNITY_EDITOR || UNITY_STANDALONE
                                                vertices.Add(v);
#else
                                                vertices.Add(new Point3D(v.X, v.Y, v.Z));
#endif
                                                break;
                                        }
                                        break;
                                    }
                                case VBFE_InputType.VECTOR4_BYTE_NORM:
                                    {
                                        Vector4 v = new Vector4(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                                        v /= (float)byte.MaxValue - 0.5f;
                                        v = Vector4.Normalize(v);
                                        switch (format.ShaderSlot)
                                        {
                                            case VBFE_InputSlot.NORMAL:
#if UNITY_EDITOR || UNITY_STANDALONE
                                                normals.Add(v);
#else
                                                normals.Add(new Vector3D(v.X, v.Y, v.Z));
#endif
                                                break;
                                            case VBFE_InputSlot.TANGENT:
                                                break;
                                            case VBFE_InputSlot.BITANGENT:
                                                break;
                                        }
                                        break;
                                    }
                            }
                        }
                    }
                    Utilities.Align(reader, 16);
                }
            }

            if (vertices.Count == 0) return mesh;

#if UNITY_EDITOR || UNITY_STANDALONE
            mesh.name = submesh.Name;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0); //0??
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetUVs(2, uv2);
            mesh.SetUVs(3, uv3);
            mesh.SetUVs(7, uv7);
            //mesh.SetBoneWeights(InBoneWeights.ToArray());
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
#else
            mesh.Children.Add(new GeometryModel3D
            {
                Geometry = new MeshGeometry3D
                {
                    Positions = vertices,
                    TriangleIndices = indices,
                    Normals = normals,
                    TextureCoordinates = uv0
                }
            });
#endif
            return mesh;
        }
        #endregion

        #region HELPERS
        /* Find a model that contains a given submesh */
        public CS2 FindModelForSubmesh(CS2.Submesh submesh)
        {
            return Entries.FirstOrDefault(o => o.Submeshes.Contains(submesh));
        }

        /* Get the current BIN index for a submesh (useful for cross-ref'ing with compiled binaries)
         * Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk */
        public int GetWriteIndexForSubmesh(CS2.Submesh submesh)
        {
            return GetAllSubmeshes().IndexOf(submesh);
        }

        /* Get a submesh by its current BIN index (useful for cross-ref'ing with compiled binaries)
         * Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk */
        public CS2.Submesh GetSubmeshForWriteIndex(int index)
        {
            return GetAllSubmeshes()[index];
        }

        /* Get all submeshes within this models archive */
        private List<CS2.Submesh> GetAllSubmeshes()
        {
            List<CS2.Submesh> submeshes = new List<Submesh>();
            for (int i = 0; i < Entries.Count; i++)
                submeshes.AddRange(Entries[i].Submeshes);
            return submeshes;
        }
        #endregion

        #region STRUCTURES
        public class AlienVBF
        {
            public int ElementCount;
            public List<Element> Elements;

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public struct Element
            {
                public int ArrayIndex;
                public int Offset; // NOTE: Offset within data structure, generally not important.
                public VBFE_InputType VariableType; //(int)
                public VBFE_InputSlot ShaderSlot; //(int)
                public int VariantIndex; // NOTE: Variant index such as UVs: (UV0, UV1, UV2...)
                public int Unknown_; // NOTE: Seems to be always 2?
            };
        }

        public enum VBFE_InputType
        {
            VECTOR3 = 0x03,
            // TODO: Present at 'bsp_torrens' but I haven't seen models that contain that being rendered yet.
            AlienVertexInputType_Unknown0_ = 0x04,
            INT32 = 0x05,
            VECTOR4_BYTE = 0x06,
            VECTOR4_BYTE_DIV255 = 0x09,
            VECTOR2_INT16_DIV2048 = 0x0A,
            VECTOR4_INT16_DIVMAX = 0x0B,
            VECTOR4_BYTE_NORM = 0x0F,
            AlienVertexInputType_u16 = 0x13,
        };

        public enum VBFE_InputSlot
        {
            VERTEX = 0x01,
            BONE_WEIGHTS = 0x02, // NOTE: Bone Weights
            BONE_INDICES = 0x03, // NOTE: Bone Indices
            NORMAL = 0x04,
            UV = 0x06,
            TANGENT = 0x07, // NOTE: Tangent
            BITANGENT = 0x08, // NOTE: Bitangent
            COLOUR = 0x0A, // NOTE: Color? Specular? What is this?
        };

        public class CS2
        {
            public string Name;
            public List<Submesh> Submeshes = new List<Submesh>();

            public override string ToString()
            {
                return Name;
            }

            public class Submesh
            {
                public string Name;

                public Vector3 AABBMin;
                public Vector3 AABBMax;

                public float LODMinDistance_;
                public float LODMaxDistance_;
                public int NextInLODGroup_;
                public int FirstModelInGroupForNextLOD_;

                public int MaterialLibraryIndex;

                public uint Unknown2_; // NOTE: Flags?
                // - 134239524 = not in pak??
                // - 134239233 = dynamic_mesh
                // - 134282240 = first entry for regular
                // - 134239232 = subsequent entry for regular (aisde from "Global\\" stuff which is first entry)
                // - 134239236 = first entry for LOD
                // - 134239248 = subsequent entries for LOD

                public int UnknownIndex; // NOTE: -1 means no index. Seems to be related to Next/Parent.
                public int CollisionIndex_; // NODE: If this is not -1, model piece name starts with "COL_" and are always character models.

                public AlienVBF VertexFormat;
                public AlienVBF VertexFormatLowDetail;

                public UInt16 ScaleFactor;
                public Int16 HeadRelated_; // NOTE: Seems to be valid on some 'HEAD' models, otherwise -1. Maybe morphing related???

                public UInt16 VertexCount;
                public UInt16 IndexCount;

                public List<int> boneIndices = new List<int>();

                public byte[] content = new byte[0];

                public override string ToString()
                {
                    return Name;
                }
            }
        }
        #endregion
    }
}