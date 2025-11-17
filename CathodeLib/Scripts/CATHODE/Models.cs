using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CathodeLib.ObjectExtensions;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/RENDERABLE/LEVEL_MODELS.PAK & MODELS_LEVEL.BIN
    /// </summary>
    public class Models : CathodeFile
    {
        public List<CS2> Entries = new List<CS2>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        protected override bool HandlesLoadingManually => true;
        private Materials _materials;
        private Collisions _collisions;
        private MorphTargets _morphTargets;

        public Models(string path, Materials materials, Collisions weightedCollisions, MorphTargets morphTargets) : base (path)
        {
            _materials = materials;
            _collisions = weightedCollisions;
            _morphTargets = morphTargets;

            _loaded = Load();
        }

        private List<CS2.Component.LOD.Submesh> _writeList = new List<CS2.Component.LOD.Submesh>();

        ~Models()
        {
            Entries.Clear();
            _writeList.Clear();
        }

        #region FILE_IO
        /// <summary>
        /// Load the file
        /// </summary>
        override protected bool LoadInternal(MemoryStream stream)
        {
            //NOTE: Loading via byte[] or MemoryStream is not currently supported. Must be loaded via disk from a filepath!
            if (_filepath == "")
                return false;

            string filepathBIN = GetBinPath();
            if (!File.Exists(filepathBIN)) 
                return false;

            List<string> filenameList = new List<string>();
            List<string> meshNameList = new List<string>();
            List<int> LODs = new List<int>();
            using (BinaryReader bin = new BinaryReader(File.OpenRead(filepathBIN)))
            {
                bin.BaseStream.Position += 4; //Magic
                int modelCount = bin.ReadInt32();
                if ((FileIdentifiers)bin.ReadInt16() != FileIdentifiers.HEADER_FILE) return false;
                if ((FileIdentifiers)bin.ReadInt16() != FileIdentifiers.MODEL_DATA) return false;
                int vbfCount = bin.ReadInt32();

                //Read vertex buffer formats
                List<VertexFormat> vertexFormats = new List<VertexFormat>(vbfCount);
                for (int i = 0; i < vbfCount; ++i)
                {
                    VertexFormat vertexFormat = new VertexFormat();
                    while (true)
                    {
                        int Index = bin.ReadInt32();
                        bin.BaseStream.Position += 4;

                        VertexFormat.Attribute attribute = new VertexFormat.Attribute();
                        attribute.Type = (VertexFormat.Type)bin.ReadInt32();
                        attribute.Usage = (VertexFormat.Usage)bin.ReadInt32();
                        attribute.Index = bin.ReadInt32();
                        bin.BaseStream.Position += 4;

                        //This is always last, used to define indices
                        if (Index == 0xFF)
                        {
                            vertexFormat.Attributes.Add(new List<VertexFormat.Attribute>() { attribute });
                            break;
                        }

                        while (vertexFormat.Attributes.Count - 1 < Index)
                            vertexFormat.Attributes.Add(new List<VertexFormat.Attribute>());
                        vertexFormat.Attributes[Index].Add(attribute);
                    }
                    vertexFormats.Add(vertexFormat);
                }

                //Read filenames and submesh names
                Dictionary<int, string> stringOffsets = new Dictionary<int, string>();
                byte[] filenames = bin.ReadBytes(bin.ReadInt32());
                using (BinaryReader reader = new BinaryReader(new MemoryStream(filenames)))
                {
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                        stringOffsets.Add((int)reader.BaseStream.Position, Utilities.ReadString(reader));
                }

                //Read model metadata
                Dictionary<CS2.Component.LOD.Submesh, int> boneOffsets = new Dictionary<CS2.Component.LOD.Submesh, int>();
                for (int i = 0; i < modelCount; i++)
                {
                    filenameList.Add(stringOffsets[bin.ReadInt32()]);
                    bin.BaseStream.Position += 4;
                    meshNameList.Add(stringOffsets[bin.ReadInt32()]);
                    bin.BaseStream.Position += 4;

                    CS2.Component.LOD.Submesh submesh = new CS2.Component.LOD.Submesh();
                    submesh.MinBounds = Utilities.Consume<Vector3>(bin);
                    submesh.MinLODRange = bin.ReadSingle();
                    submesh.MaxBounds = Utilities.Consume<Vector3>(bin);
                    submesh.MaxLODRange = bin.ReadSingle();

                    int childModelIndex = bin.ReadInt32(); //todo - need to use a ref for this and probably store it. might mean we can avoid the CS2 structure?
                    int childLODIndex = bin.ReadInt32();
                    if (childLODIndex != -1) LODs.Add(childLODIndex);
                    submesh.Material = _materials.GetAtWriteIndex(bin.ReadInt32());

                    submesh.RenderFlags = (CS2.Component.LOD.RenderingFlag)bin.ReadUInt32();
                    submesh.CollisionProxyIndex = bin.ReadInt32();
                    bin.BaseStream.Position += 4;
                    submesh.WeightedCollision = _collisions.GetAtWriteIndex(bin.ReadInt32());
                    int boneArrayOffset = bin.ReadInt32();

                    submesh.VertexFormatFull = vertexFormats[bin.ReadUInt16()];
                    submesh.VertexFormatPartial = vertexFormats[bin.ReadUInt16()];
                    bin.BaseStream.Position += 2;
                    submesh.VertexScale = bin.ReadUInt16();
                    submesh.MorphAnimSet = _morphTargets.GetAtWriteIndex(bin.ReadInt16());

                    submesh.VertexCount = bin.ReadUInt16();
                    submesh.IndexCount = bin.ReadUInt16();
                    submesh.Bones.Capacity = bin.ReadUInt16();
                    if (submesh.Bones.Capacity != 0)
                        boneOffsets.Add(submesh, boneArrayOffset);

                    _writeList.Add(submesh);
                }

                //Read bone data
                byte[] bones = bin.ReadBytes(bin.ReadInt32());
                using (BinaryReader reader = new BinaryReader(new MemoryStream(bones)))
                {
                    foreach (KeyValuePair<CS2.Component.LOD.Submesh, int> offset in boneOffsets)
                    {
                        reader.BaseStream.Position = offset.Value;
                        for (int i = 0; i < offset.Key.Bones.Capacity; i++)
                            offset.Key.Bones.Add((int)reader.ReadByte());
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
                    //Read submesh header info [48]
                    pak.BaseStream.Position += 8;
                    int length = BigEndianUtils.ReadInt32(pak);
                    pak.BaseStream.Position += 4; //length again 
                    int offset = BigEndianUtils.ReadInt32(pak);
                    int sort = BigEndianUtils.ReadInt32(pak);
                    pak.BaseStream.Position += 8;
                    int binIndex = BigEndianUtils.ReadInt32(pak);
                    pak.BaseStream.Position += 12;

                    //Create a new model instance to add these submeshes to
                    CS2 cs2 = Entries.FirstOrDefault(o => o.Name == filenameList[binIndex]);
                    if (cs2 == null)
                    {
                        cs2 = new CS2();
                        cs2.Name = filenameList[binIndex];
                        Entries.Add(cs2);
                    }
                    CS2.Component component = new CS2.Component();
                    cs2.Components.Add(component);

                    //Read submesh content and add to appropriate model
                    int offsetToReturnTo = (int)pak.BaseStream.Position;
                    pak.BaseStream.Position = endOfHeaders + offset;
                    byte[] content = pak.ReadBytes(length);
                    using (BinaryReader reader = new BinaryReader(new MemoryStream(content)))
                    {
                        int firstIndex = BigEndianUtils.ReadInt32(reader);
                        int submeshCount = BigEndianUtils.ReadInt32(reader);

                        reader.BaseStream.Position += 16;
                        List<Tuple<int, int[]>> entryOffsets = new List<Tuple<int, int[]>>(); //bin index, [start,length]
                        for (int x = 0; x < submeshCount; x++)
                        {
                            entryOffsets.Add(new Tuple<int, int[]>(BigEndianUtils.ReadInt32(reader), new int[2] { BigEndianUtils.ReadInt32(reader), BigEndianUtils.ReadInt32(reader) }));
                            reader.BaseStream.Position += 4;
                        }
                        reader.BaseStream.Position += 8;

                        int lodIndex = component.LODs.Count - 1;
                        foreach (Tuple<int, int[]> offsetData in entryOffsets)
                        {
                            CS2.Component.LOD.Submesh submesh = _writeList[offsetData.Item1];
                            reader.BaseStream.Position = offsetData.Item2[0];
                            submesh.Data = reader.ReadBytes(offsetData.Item2[1]);

                            if (LODs.Contains(offsetData.Item1) || lodIndex == -1)
                            {
                                component.LODs.Add(new CS2.Component.LOD(meshNameList[offsetData.Item1]));
                                lodIndex++;
                            }
                            component.LODs[lodIndex].Submeshes.Add(submesh);
                        }
                    }
                    pak.BaseStream.Position = offsetToReturnTo;
                }
            }
            return true;
        }

        /// <summary>
        /// Save the file
        /// </summary>
        override protected bool SaveInternal()
        {
            int submeshCount = 0;
            for (int i = 0; i < Entries.Count; i++)
                for (int z = 0; z < Entries[i].Components.Count; z++)
                    for (int x = 0; x < Entries[i].Components[z].LODs.Count; x++)
                        submeshCount += Entries[i].Components[z].LODs[x].Submeshes.Count;

            List<VertexFormat> vertexFormats = new List<VertexFormat>();
            for (int i = 0; i < Entries.Count; i++)
            {
                for (int z = 0; z < Entries[i].Components.Count; z++)
                {
                    for (int x = 0; x < Entries[i].Components[z].LODs.Count; x++)
                    {
                        for (int y = 0; y < Entries[i].Components[z].LODs[x].Submeshes.Count; y++)
                        {
                            if (Entries[i].Components[z].LODs[x].Submeshes[y].VertexFormatFull != null && !vertexFormats.Contains(Entries[i].Components[z].LODs[x].Submeshes[y].VertexFormatFull))
                                vertexFormats.Add(Entries[i].Components[z].LODs[x].Submeshes[y].VertexFormatFull);
                            if (Entries[i].Components[z].LODs[x].Submeshes[y].VertexFormatPartial != null && !vertexFormats.Contains(Entries[i].Components[z].LODs[x].Submeshes[y].VertexFormatPartial))
                                vertexFormats.Add(Entries[i].Components[z].LODs[x].Submeshes[y].VertexFormatPartial);
                        }
                    }
                }
            }

            using (BinaryWriter bin = new BinaryWriter(File.OpenWrite(GetBinPath())))
            {
                bin.BaseStream.SetLength(0);
                _writeList.Clear();

                //Write header
                Utilities.WriteString("XMDB", bin);
                bin.Write(submeshCount);
                bin.Write((Int16)FileIdentifiers.HEADER_FILE);
                bin.Write((Int16)FileIdentifiers.MODEL_DATA);
                bin.Write(vertexFormats.Count);

                //Write vertex buffers
                for (int i = 0; i < vertexFormats.Count; i++)
                {
                    for (int x = 0; x < vertexFormats[i].Attributes.Count; x++)
                    {
                        int offset = 0;
                        for (int y = 0; y < vertexFormats[i].Attributes[x].Count; y++)
                        {
                            bin.Write((x == vertexFormats[i].Attributes.Count - 1) ? 255 : x);
                            bin.Write(offset);
                            bin.Write((Int32)vertexFormats[i].Attributes[x][y].Type);
                            bin.Write((Int32)vertexFormats[i].Attributes[x][y].Usage);
                            bin.Write((Int32)vertexFormats[i].Attributes[x][y].Index);
                            bin.Write((Int32)2);

                            switch (vertexFormats[i].Attributes[x][y].Type)
                            {
                                case VertexFormat.Type.FP32_1:
                                case VertexFormat.Type.Color:
                                case VertexFormat.Type.U8_4:
                                case VertexFormat.Type.S16_2:
                                case VertexFormat.Type.U8_4N:
                                case VertexFormat.Type.S16_2N:
                                case VertexFormat.Type.U16_2N:
                                case VertexFormat.Type.UDec3:
                                case VertexFormat.Type.Dec3N:
                                case VertexFormat.Type.FP16_2:
                                    offset += 4;
                                    break;
                                case VertexFormat.Type.FP32_2:
                                case VertexFormat.Type.S16_4:
                                case VertexFormat.Type.S16_4N:
                                case VertexFormat.Type.U16_4N:
                                case VertexFormat.Type.FP16_4:
                                    offset += 8;
                                    break;
                                case VertexFormat.Type.FP32_3:
                                    offset += 12;
                                    break;
                                case VertexFormat.Type.FP32_4:
                                    offset += 16;
                                    break;
                            }
                        }
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
                    for (int z = 0; z < Entries[i].Components.Count; z++)
                    {
                        for (int x = 0; x < Entries[i].Components[z].LODs.Count; x++)
                        {
                            if (!stringOffsets.ContainsKey(Entries[i].Components[z].LODs[x].Name))
                            {
                                stringOffsets.Add(Entries[i].Components[z].LODs[x].Name, (int)bin.BaseStream.Position - startPos);
                                Utilities.WriteString(Entries[i].Components[z].LODs[x].Name, bin, true);
                            }
                        }
                    }
                }
                bin.Write((char)0x00);
                Utilities.Align(bin, 16);
                int stringLength = (int)bin.BaseStream.Position - startPos;
                bin.BaseStream.Position = startPos - 4;
                bin.Write(stringLength);
                bin.BaseStream.Position += stringLength;

                //Get all submeshes to write
                List<(CS2 entry, CS2.Component component, CS2.Component.LOD lod, CS2.Component.LOD.Submesh submesh, int entryIndex, int componentIndex, int lodIndex, int submeshIndex)> submeshList = new List<(CS2, CS2.Component, CS2.Component.LOD, CS2.Component.LOD.Submesh, int, int, int, int)>();
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int z = 0; z < Entries[i].Components.Count; z++)
                    {
                        for (int x = 0; x < Entries[i].Components[z].LODs.Count; x++)
                        {
                            for (int y = 0; y < Entries[i].Components[z].LODs[x].Submeshes.Count; y++)
                            {
                                submeshList.Add((Entries[i], Entries[i].Components[z], Entries[i].Components[z].LODs[x], Entries[i].Components[z].LODs[x].Submeshes[y], i, z, x, y));
                            }
                        }
                    }
                }

                //Write model data
                byte[][] metadataBuffers = new byte[submeshList.Count][];
                Parallel.For(0, submeshList.Count, idx =>
                {
                    var (entry, component, lod, mesh, entryIdx, componentIdx, lodIdx, submeshIdx) = submeshList[idx];
                    metadataBuffers[idx] = SerializeSubmeshMetadata(mesh, entry, lod, stringOffsets, vertexFormats, submeshList, idx);
                });
                int boneOffset = 0;
                for (int idx = 0; idx < submeshList.Count; idx++)
                {
                    var (entry, component, lod, mesh, entryIdx, componentIdx, lodIdx, submeshIdx) = submeshList[idx];
                    
                    byte[] buffer = metadataBuffers[idx];
                    
                    int nextSubmeshIndex = (submeshIdx < lod.Submeshes.Count - 1) ? _writeList.Count + 1 : -1;
                    byte[] nextSubmeshBytes = BitConverter.GetBytes(nextSubmeshIndex);
                    Array.Copy(nextSubmeshBytes, 0, buffer, 48, 4);
                    
                    int nextLODIndex = (submeshIdx == 0 && lodIdx < component.LODs.Count - 1) ? _writeList.Count + lod.Submeshes.Count : -1;
                    byte[] nextLODBytes = BitConverter.GetBytes(nextLODIndex);
                    Array.Copy(nextLODBytes, 0, buffer, 52, 4);
                    
                    byte[] boneOffsetBytes = BitConverter.GetBytes(boneOffset);
                    Array.Copy(boneOffsetBytes, 0, buffer, 76, 4);
                    
                    bin.Write(buffer);
                    boneOffset += mesh.Bones.Count;
                    _writeList.Add(mesh);
                }

                //Write bone data
                byte[][] boneBuffers = new byte[submeshList.Count][];
                Parallel.For(0, submeshList.Count, idx =>
                {
                    var (entry, component, lod, mesh, entryIdx, componentIdx, lodIdx, submeshIdx) = submeshList[idx];
                    boneBuffers[idx] = SerializeBoneData(mesh);
                });
                bin.Write(boneOffset);
                for (int idx = 0; idx < boneBuffers.Length; idx++)
                {
                    if (boneBuffers[idx] != null && boneBuffers[idx].Length > 0)
                        bin.Write(boneBuffers[idx]);
                }
            }

            using (BinaryWriter pak = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                int componentCount = 0;
                for (int i = 0; i < Entries.Count; i++)
                    componentCount += Entries[i].Components.Count;

                //Get all components
                List<(CS2 entry, CS2.Component component, int entryIndex, int componentIndex)> componentList = new List<(CS2, CS2.Component, int, int)>();
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int p = 0; p < Entries[i].Components.Count; p++)
                    {
                        componentList.Add((Entries[i], Entries[i].Components[p], i, p));
                    }
                }

                //Write components
                (byte[] buffer, int length)[] componentBuffers = new (byte[], int)[componentList.Count];
                Parallel.For(0, componentList.Count, idx =>
                {
                    var (entry, component, entryIdx, componentIdx) = componentList[idx];
                    componentBuffers[idx] = SerializeComponentContent(entry, component, componentList, idx);
                });
                int contentOffset = 32 + (componentCount * 48);
                pak.BaseStream.SetLength(contentOffset);
                pak.BaseStream.Position = contentOffset;
                List<int> offsets = new List<int>();
                List<int> lengths = new List<int>();
                for (int idx = 0; idx < componentBuffers.Length; idx++)
                {
                    offsets.Add((int)pak.BaseStream.Position - contentOffset);
                    pak.Write(componentBuffers[idx].buffer);
                    lengths.Add(componentBuffers[idx].length);
                }

                //Write model headers
                pak.BaseStream.Position = 32;
                int c = 0;
                for (int i = 0; i < Entries.Count; i++)
                {
                    for (int x = 0; x < Entries[i].Components.Count; x++)
                    {
                        pak.Write(new byte[8]);
                        pak.Write(BigEndianUtils.FlipEndian((Int32)lengths[c]));
                        pak.Write(BigEndianUtils.FlipEndian((Int32)lengths[c]));
                        pak.Write(BigEndianUtils.FlipEndian((Int32)offsets[c]));
                        pak.Write(0);
                        pak.Write(new byte[4] { 0x00, 0x01, 0x01, 0x00 });
                        pak.Write(0);

                        pak.Write(BigEndianUtils.FlipEndian((Int32)GetWriteIndex(Entries[i].Components[x].LODs[0].Submeshes[0])));
                        pak.Write(new byte[12]);

                        c++;
                    }
                }

                //Write header
                pak.BaseStream.Position = 0;
                pak.Write(new byte[4]);
                pak.Write(BigEndianUtils.FlipEndian((Int32)FileIdentifiers.ASSET_FILE));
                pak.Write(BigEndianUtils.FlipEndian((Int32)FileIdentifiers.MODEL_DATA));
                pak.Write(BigEndianUtils.FlipEndian((Int32)componentCount));
                pak.Write(BigEndianUtils.FlipEndian((Int32)componentCount));
                pak.Write(BigEndianUtils.FlipEndian((Int32)16));
                pak.Write(BigEndianUtils.FlipEndian((Int32)1));
                pak.Write(BigEndianUtils.FlipEndian((Int32)1));
            }
            return true;
        }

        private byte[] SerializeSubmeshMetadata(CS2.Component.LOD.Submesh mesh, CS2 entry, CS2.Component.LOD lod, Dictionary<string, int> stringOffsets, List<VertexFormat> vertexFormats, List<(CS2 entry, CS2.Component component, CS2.Component.LOD lod, CS2.Component.LOD.Submesh submesh, int entryIndex, int componentIndex, int lodIndex, int submeshIndex)> submeshList, int idx)
        {
            using (MemoryStream stream = new MemoryStream(80))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                var (currentEntry, currentComponent, currentLod, currentMesh, entryIdx, componentIdx, lodIdx, submeshIdx) = submeshList[idx];
                
                writer.Write((Int32)stringOffsets[entry.Name]);
                writer.Write(new byte[4]);
                writer.Write((Int32)stringOffsets[lod.Name]);
                writer.Write(new byte[4]);
                Utilities.Write<Vector3>(writer, mesh.MinBounds);
                writer.Write((float)mesh.MinLODRange);
                Utilities.Write<Vector3>(writer, mesh.MaxBounds);
                writer.Write((float)mesh.MaxLODRange);
                
                writer.Write((Int32)0); //Placeholder for next submesh index
                writer.Write((Int32)0); //Placeholder for next LOD index
                
                writer.Write(_materials.GetWriteIndex(mesh.Material));
                writer.Write((Int32)mesh.RenderFlags);
                writer.Write((Int32)mesh.CollisionProxyIndex);
                writer.Write((Int32)mesh.Data.Length);
                writer.Write(_collisions.GetWriteIndex(mesh.WeightedCollision));
                writer.Write((Int32)0); //boneOffset placeholder
                writer.Write((Int16)vertexFormats.IndexOf(mesh.VertexFormatFull));
                writer.Write((Int16)vertexFormats.IndexOf(mesh.VertexFormatPartial));
                writer.Write((Int16)vertexFormats.IndexOf(mesh.VertexFormatFull));
                writer.Write((Int16)mesh.VertexScale);
                writer.Write((Int16)_morphTargets.GetWriteIndex(mesh.MorphAnimSet));
                writer.Write((Int16)mesh.VertexCount);
                writer.Write((Int16)mesh.IndexCount);
                writer.Write((Int16)mesh.Bones.Count);

                return stream.ToArray();
            }
        }

        private byte[] SerializeBoneData(CS2.Component.LOD.Submesh mesh)
        {
            if (mesh.Bones.Count == 0) return null;
            byte[] boneData = new byte[mesh.Bones.Count];
            for (int p = 0; p < mesh.Bones.Count; p++)
                boneData[p] = (byte)mesh.Bones[p];
            return boneData;
        }

        private (byte[] buffer, int length) SerializeComponentContent(CS2 entry, CS2.Component component, List<(CS2 entry, CS2.Component component, int entryIndex, int componentIndex)> componentList, int idx)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int countOfAllSubmeshes = 0;
                for (int x = 0; x < component.LODs.Count; x++)
                    countOfAllSubmeshes += component.LODs[x].Submeshes.Count;

                writer.Write(BigEndianUtils.FlipEndian((Int32)GetWriteIndex(component.LODs[0].Submeshes[0])));
                writer.Write(BigEndianUtils.FlipEndian((Int32)countOfAllSubmeshes));
                writer.Write(new byte[16]);
                
                List<byte> content = new List<byte>();
                for (int y = 0; y < component.LODs.Count; y++)
                {
                    for (int z = 0; z < component.LODs[y].Submeshes.Count; z++)
                    {
                        writer.Write(BigEndianUtils.FlipEndian((Int32)GetWriteIndex(component.LODs[y].Submeshes[z])));
                        writer.Write(BigEndianUtils.FlipEndian((Int32)(24 + (countOfAllSubmeshes * 16) + content.Count + 8)));
                        writer.Write(BigEndianUtils.FlipEndian((Int32)component.LODs[y].Submeshes[z].Data.Length));
                        writer.Write(new byte[4]);
                        content.AddRange(component.LODs[y].Submeshes[z].Data);
                    }
                }
                writer.Write(new byte[8]);
                writer.Write(content.ToArray());

                byte[] buffer = stream.ToArray();
                return (buffer, buffer.Length);
            }
        }

        private string GetBinPath()
        {
            return _filepath.Substring(0, _filepath.Length - Path.GetFileName(_filepath).Length) + "MODELS_" + Path.GetFileName(_filepath).Substring(0, Path.GetFileName(_filepath).Length - 11) + ".BIN";
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Find a model that contains a given submesh
        /// </summary>
        public CS2 FindModelForSubmesh(CS2.Component.LOD.Submesh submesh)
        {
            for (int i = 0; i < Entries.Count; i++)
                for (int z = 0; z < Entries[i].Components.Count; z++)
                    for (int x = 0; x < Entries[i].Components[z].LODs.Count; x++)
                        if (Entries[i].Components[z].LODs[x].Submeshes.Contains(submesh))
                            return Entries[i];
            return null;
        }
        /// <summary>
        /// Find a component of a model that contains a submesh
        /// </summary>
        public CS2.Component FindModelComponentForSubmesh(CS2.Component.LOD.Submesh submesh)
        {
            for (int i = 0; i < Entries.Count; i++)
                for (int z = 0; z < Entries[i].Components.Count; z++)
                    for (int x = 0; x < Entries[i].Components[z].LODs.Count; x++)
                        if (Entries[i].Components[z].LODs[x].Submeshes.Contains(submesh))
                            return Entries[i].Components[z];
            return null;
        }
        /// <summary>
        /// Find a LOD that contains a given submesh
        /// </summary>
        public CS2.Component.LOD FindModelLODForSubmesh(CS2.Component.LOD.Submesh submesh)
        {
            for (int i = 0; i < Entries.Count; i++)
                for (int z = 0; z < Entries[i].Components.Count; z++)
                    for (int x = 0; x < Entries[i].Components[z].LODs.Count; x++)
                        if (Entries[i].Components[z].LODs[x].Submeshes.Contains(submesh))
                            return Entries[i].Components[z].LODs[x];
            return null;
        }

        /// <summary>
        /// Get the current BIN index for a submesh (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(CS2.Component.LOD.Submesh submesh)
        {
            if (submesh == null) return -1;
            if (!_writeList.Contains(submesh)) return -1;
            return _writeList.IndexOf(submesh);
        }
        public int GetWriteIndex(CS2.Component mesh)
        {
            if (mesh == null) return -1;
            if (!_writeList.Contains(mesh.LODs[0].Submeshes[0])) return -1;
            return _writeList.IndexOf(mesh.LODs[0].Submeshes[0]);
        }

        /// <summary>
        /// Get a submesh by its current BIN index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public CS2.Component.LOD.Submesh GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        /// 
        public CS2 AddEntry(CS2 model)
        {
            if (model == null)
                return null;

            CS2 newModel = model.Copy();
            foreach (CS2.Component component in newModel.Components)
            {
                foreach (CS2.Component.LOD lod in component.LODs)
                {
                    foreach (CS2.Component.LOD.Submesh submesh in lod.Submeshes)
                    {
                        submesh.Material = _materials.AddEntry(submesh.Material);
                        submesh.WeightedCollision = _collisions.AddEntry(submesh.WeightedCollision);
                        submesh.MorphAnimSet = _morphTargets.AddEntry(submesh.MorphAnimSet); 
                    }
                }
            }

            var existing = Entries.FirstOrDefault(o => o == newModel);
            if (existing != null)
                return existing;

            Entries.Add(model);
            return newModel;
        }
        #endregion

        #region STRUCTURES
        public class VertexFormat : IComparable<VertexFormat>
        {
            public List<List<Attribute>> Attributes = new List<List<Attribute>>();

            public class Attribute : IComparable<Attribute>, IEquatable<Attribute>
            {
                public Attribute() { }
                public Attribute(Type type, Usage slot = Usage.Position, int index = 0)
                {
                    Type = type;
                    Usage = slot;
                    Index = index;
                }

                public Type Type;
                public Usage Usage;
                public int Index;

                public static bool operator ==(Attribute x, Attribute y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                    if (x.Type != y.Type) return false;
                    if (x.Usage != y.Usage) return false;
                    if (x.Index != y.Index) return false;
                    return true;
                }

                public static bool operator !=(Attribute x, Attribute y)
                {
                    return !(x == y);
                }

                public bool Equals(Attribute other)
                {
                    return this == other;
                }

                public override bool Equals(object obj)
                {
                    return obj is Attribute attr && this == attr;
                }

                public override int GetHashCode()
                {
                    int hashCode = -1234567890;
                    hashCode = hashCode * -1521134295 + Type.GetHashCode();
                    hashCode = hashCode * -1521134295 + Usage.GetHashCode();
                    hashCode = hashCode * -1521134295 + Index.GetHashCode();
                    return hashCode;
                }

                public int CompareTo(Attribute other)
                {
                    if (other == null) return 1;

                    int result = Type.CompareTo(other.Type);
                    if (result != 0) return result;

                    result = Usage.CompareTo(other.Usage);
                    if (result != 0) return result;

                    return Index.CompareTo(other.Index);
                }
            };

            public enum Type
            {
                Unknown,
                FP32_1,
                FP32_2,
                FP32_3,
                FP32_4,
                Color,
                U8_4,
                S16_2,
                S16_4,
                U8_4N,
                S16_2N,
                S16_4N,
                U16_2N,
                U16_4N,
                UDec3,
                Dec3N,
                FP16_2,
                FP16_4,
                S8_4N,
                Unused,
            };

            public enum Usage
            {
                Unknown,
                Position,
                BlendWeight,
                BlendIndices,
                Normal,
                PSize,
                TexCoord,
                Tangent,
                Binormal,
                TessFactor,
                Color,
                Fog,
                Depth,
                Sample,
            };

            public override bool Equals(object obj)
            {
                if (!(obj is VertexFormat)) return false;
                if ((VertexFormat)obj == null) return this == null;
                if (this == null) return (VertexFormat)obj == null;
                return ((VertexFormat)obj).CompareTo(this) == 0;
            }
            public static bool operator ==(VertexFormat x, VertexFormat y)
            {
                return x.Equals(y);
            }
            public static bool operator !=(VertexFormat x, VertexFormat y)
            {
                return !x.Equals(y);
            }

            public override int GetHashCode()
            {
                return 1573927372 + EqualityComparer<List<List<Attribute>>>.Default.GetHashCode(Attributes);
            }

            public int CompareTo(VertexFormat other)
            {
                if (other == null) return 1;

                int result = Attributes.Count.CompareTo(other.Attributes.Count);
                if (result != 0) return result;

                for (int i = 0; i < Attributes.Count; i++)
                {
                    result = CompareElementLists(Attributes[i], other.Attributes[i]);
                    if (result != 0) return result;
                }
                return 0;
            }

            private static int CompareElementLists(List<Attribute> list1, List<Attribute> list2)
            {
                int result = list1.Count.CompareTo(list2.Count);
                if (result != 0) return result;

                for (int i = 0; i < list1.Count; i++)
                {
                    result = list1[i].CompareTo(list2[i]);
                    if (result != 0) return result;
                }
                return 0;
            }
        }

        public class CS2 : IEquatable<CS2>
        {
            public string Name;
            public List<Component> Components = new List<Component>();

            public CS2.Component.LOD.Submesh GetSubmesh(CS2.Component.LOD.Submesh submesh)
            {
                for (int i = 0; i < Components.Count; i++)
                {
                    for (int x = 0; x < Components[i].LODs.Count; x++)
                    {
                        for (int z = 0; z < Components[i].LODs[x].Submeshes.Count; z++)
                        {
                            if (Components[i].LODs[x].Submeshes[z] == submesh)
                            {
                                return Components[i].LODs[x].Submeshes[z];
                            }
                        }
                    }
                }
                return null;
            }

            public static bool operator ==(CS2 x, CS2 y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.Name != y.Name) return false;
                if (!ListsEqual(x.Components, y.Components)) return false;
                return true;
            }

            public static bool operator !=(CS2 x, CS2 y)
            {
                return !(x == y);
            }

            public bool Equals(CS2 other)
            {
                return this == other;
            }

            public override bool Equals(object obj)
            {
                return obj is CS2 cs2 && this == cs2;
            }

            public override int GetHashCode()
            {
                int hashCode = -1234567890;
                hashCode = hashCode * -1521134295 + (Name?.GetHashCode() ?? 0);
                hashCode = hashCode * -1521134295 + (Components?.GetHashCode() ?? 0);
                return hashCode;
            }

            private static bool ListsEqual(List<Component> x, List<Component> y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                if (x.Count != y.Count) return false;
                for (int i = 0; i < x.Count; i++)
                {
                    if (x[i] != y[i]) return false;
                }
                return true;
            }

            ~CS2()
            {
                Components.Clear();
            }

            public class Component : IEquatable<Component>
            {
                public List<LOD> LODs = new List<LOD>();

                public static bool operator ==(Component x, Component y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                    if (!ListsEqual(x.LODs, y.LODs)) return false;
                    return true;
                }

                public static bool operator !=(Component x, Component y)
                {
                    return !(x == y);
                }

                public bool Equals(Component other)
                {
                    return this == other;
                }

                public override bool Equals(object obj)
                {
                    return obj is Component component && this == component;
                }

                public override int GetHashCode()
                {
                    int hashCode = -1234567890;
                    hashCode = hashCode * -1521134295 + (LODs?.GetHashCode() ?? 0);
                    return hashCode;
                }

                private static bool ListsEqual(List<LOD> x, List<LOD> y)
                {
                    if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                    if (ReferenceEquals(y, null)) return false;
                    if (x.Count != y.Count) return false;
                    for (int i = 0; i < x.Count; i++)
                    {
                        if (x[i] != y[i]) return false;
                    }
                    return true;
                }

                ~Component()
                {
                    LODs.Clear();
                }

                public class LOD : IEquatable<LOD>
                {
                    public LOD(string name)
                    {
                        Name = name;
                    }

                    public string Name;
                    public List<Submesh> Submeshes = new List<Submesh>();

                    public static bool operator ==(LOD x, LOD y)
                    {
                        if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                        if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                        if (x.Name != y.Name) return false;
                        if (!ListsEqual(x.Submeshes, y.Submeshes)) return false;
                        return true;
                    }

                    public static bool operator !=(LOD x, LOD y)
                    {
                        return !(x == y);
                    }

                    public bool Equals(LOD other)
                    {
                        return this == other;
                    }

                    public override bool Equals(object obj)
                    {
                        return obj is LOD lod && this == lod;
                    }

                    public override int GetHashCode()
                    {
                        int hashCode = -1234567890;
                        hashCode = hashCode * -1521134295 + (Name?.GetHashCode() ?? 0);
                        hashCode = hashCode * -1521134295 + (Submeshes?.GetHashCode() ?? 0);
                        return hashCode;
                    }

                    private static bool ListsEqual(List<Submesh> x, List<Submesh> y)
                    {
                        if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                        if (ReferenceEquals(y, null)) return false;
                        if (x.Count != y.Count) return false;
                        for (int i = 0; i < x.Count; i++)
                        {
                            if (x[i] != y[i]) return false;
                        }
                        return true;
                    }

                    ~LOD()
                    {
                        Submeshes.Clear();
                    }

                    [Flags]
                    public enum RenderingFlag
                    {
                        // The "HAS" ones are for parent models to avoid checking children

                        DYNAMIC_GEOM = 1 << 0,
                        HELPER_NODE = 1 << 1,
                        IS_LOD = 1 << 2,
                        TOP_LOD_OVERRIDE = 1 << 3,
                        IS_LOD_SUB_MESH = 1 << 4,
                        IS_BALLISTIC_LOD = 1 << 5,
                        IS_SHADOW_LOD = 1 << 6,
                        HAS_SHADOW_LOD = 1 << 7,
                        NEVER_LOADED = 1 << 8,
                        IS_OCCLUSION_VOLUME = 1 << 9,

                        IS_FIRST_PERSON_LOD = 1 << 10, // only used if first person camera is enabled
                        HAS_FIRST_PERSON_LOD = 1 << 11,

                        IS_THIRD_PERSON_LOD = 1 << 12,
                        HAS_THIRD_PERSON_LOD = 1 << 13,

                        IS_SHADOW_CASTING = 1 << 14,
                        HAS_SHADOW_CASTING = 1 << 15,

                        INTERACTIONS_ONLY = 1 << 16,
                        NO_INTERACTIONS = 1 << 17, // when not interacting the game swaps to a noclip FP model

                        IS_REFLECTION_LOD = 1 << 18,
                        HAS_REFLECTION_LOD = 1 << 19,

                        IS_CHARACTER_HEAD = 1 << 20,

                        IS_LEVEL_PACK = 1 << (27 + PakLocation.LEVEL),
                        IS_GLOBAL_PACK = 1 << (27 + PakLocation.GLOBAL)
                    };

                    public class Submesh : IEquatable<Submesh>
                    {
                        public Vector3 MinBounds = new Vector3(-1, -1, -1);
                        public Vector3 MaxBounds = new Vector3(1, 1, 1);

                        public float MinLODRange = 0;
                        public float MaxLODRange = 10000;

                        public RenderingFlag RenderFlags;

                        public Materials.Material Material = null; // Index in MODELS.MTL
                        public int CollisionProxyIndex = -1; // Index in COLLISION.HKX
                        public Collisions.WeightedCollision WeightedCollision = null; // Index in COLLISION.BIN
                        public MorphTargets.MorphTarget MorphAnimSet = null; // Index in MORPH_TARGET_DB.BIN

                        public VertexFormat VertexFormatFull;
                        public VertexFormat VertexFormatPartial;

                        public int VertexScale = 1;
                        public int VertexCount = 0;
                        public int IndexCount = 0;

                        public List<int> Bones = new List<int>();

                        public byte[] Data = new byte[0];

                        public static bool operator ==(Submesh x, Submesh y)
                        {
                            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                            if (ReferenceEquals(x, y)) return true;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                            if (x.MinBounds != y.MinBounds) return false;
                            if (x.MaxBounds != y.MaxBounds) return false;
#else
                            if (x.MinBounds.X != y.MinBounds.X || x.MinBounds.Y != y.MinBounds.Y || x.MinBounds.Z != y.MinBounds.Z) return false;
                            if (x.MaxBounds.X != y.MaxBounds.X || x.MaxBounds.Y != y.MaxBounds.Y || x.MaxBounds.Z != y.MaxBounds.Z) return false;
#endif
                            if (x.MinLODRange != y.MinLODRange) return false;
                            if (x.MaxLODRange != y.MaxLODRange) return false;
                            if (x.RenderFlags != y.RenderFlags) return false;
                            if (x.Material != y.Material) return false;
                            if (x.CollisionProxyIndex != y.CollisionProxyIndex) return false;
                            if (x.WeightedCollision != y.WeightedCollision) return false;
                            if (x.MorphAnimSet != y.MorphAnimSet) return false;
                            if (x.VertexFormatFull != y.VertexFormatFull) return false;
                            if (x.VertexFormatPartial != y.VertexFormatPartial) return false;
                            if (x.VertexScale != y.VertexScale) return false;
                            if (x.VertexCount != y.VertexCount) return false;
                            if (x.IndexCount != y.IndexCount) return false;
                            if (!ListsEqual(x.Bones, y.Bones)) return false;
                            if (!ArraysEqual(x.Data, y.Data)) return false;
                            return true;
                        }

                        public static bool operator !=(Submesh x, Submesh y)
                        {
                            return !(x == y);
                        }

                        public bool Equals(Submesh other)
                        {
                            return this == other;
                        }

                        public override bool Equals(object obj)
                        {
                            return obj is Submesh submesh && this == submesh;
                        }

                        public override int GetHashCode()
                        {
                            int hashCode = -1234567890;
                            hashCode = hashCode * -1521134295 + MinBounds.GetHashCode();
                            hashCode = hashCode * -1521134295 + MaxBounds.GetHashCode();
                            hashCode = hashCode * -1521134295 + MinLODRange.GetHashCode();
                            hashCode = hashCode * -1521134295 + MaxLODRange.GetHashCode();
                            hashCode = hashCode * -1521134295 + RenderFlags.GetHashCode();
                            hashCode = hashCode * -1521134295 + (Material?.GetHashCode() ?? 0);
                            hashCode = hashCode * -1521134295 + CollisionProxyIndex.GetHashCode();
                            hashCode = hashCode * -1521134295 + (WeightedCollision?.GetHashCode() ?? 0);
                            hashCode = hashCode * -1521134295 + (MorphAnimSet?.GetHashCode() ?? 0);
                            hashCode = hashCode * -1521134295 + VertexFormatFull.GetHashCode();
                            hashCode = hashCode * -1521134295 + VertexFormatPartial.GetHashCode();
                            hashCode = hashCode * -1521134295 + VertexScale.GetHashCode();
                            hashCode = hashCode * -1521134295 + VertexCount.GetHashCode();
                            hashCode = hashCode * -1521134295 + IndexCount.GetHashCode();
                            hashCode = hashCode * -1521134295 + (Bones?.GetHashCode() ?? 0);
                            hashCode = hashCode * -1521134295 + (Data?.GetHashCode() ?? 0);
                            return hashCode;
                        }

                        private static bool ListsEqual(List<int> x, List<int> y)
                        {
                            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                            if (ReferenceEquals(y, null)) return false;
                            if (x.Count != y.Count) return false;
                            for (int i = 0; i < x.Count; i++)
                            {
                                if (x[i] != y[i]) return false;
                            }
                            return true;
                        }

                        private static bool ArraysEqual(byte[] x, byte[] y)
                        {
                            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                            if (ReferenceEquals(y, null)) return false;
                            if (x.Length != y.Length) return false;
                            for (int i = 0; i < x.Length; i++)
                            {
                                if (x[i] != y[i]) return false;
                            }
                            return true;
                        }

                        ~Submesh()
                        {
                            Bones.Clear();
                            Data = null;
                        }
                    }

                    public override string ToString()
                    {
                        return Name;
                    }
                }
            }

            public override string ToString()
            {
                return Name;
            }
        }
        #endregion
    }
}