using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CathodeLib;
using static CATHODE.Models;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/RENDERABLE/LEVEL_MODELS.MTL & LEVEL_MODELS.CST */
    public class Materials : CathodeFile
    {
        public List<Material> Entries = new List<Material>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;
        public Materials(string path) : base(path) { }

        public List<byte[]> CSTData { get { return _cstData; } } //WIP
        private List<byte[]> _cstData = new List<byte[]>();

        public int[] CSTOffsets { get { return _unknownOffsets; } } //WIP
        private int[] _unknownOffsets;

        private List<Material> _writeList = new List<Material>();

        private string _filepathCST;

        #region FILE_IO
        override protected bool LoadInternal()
        {
            _filepathCST = _filepath.Substring(0, _filepath.Length - 3) + "CST";
            if (!File.Exists(_filepathCST)) return false;

            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position = 8;
                int firstMaterialOffset = reader.ReadInt32();

                List<int> cstOffsets = new List<int>();
                for (int x = 0; x < 5; x++) cstOffsets.Add(reader.ReadInt32());
                using (BinaryReader readerCST = new BinaryReader(File.OpenRead(_filepathCST)))
                {
                    cstOffsets.Add((int)readerCST.BaseStream.Length/* - 4*/);
                    //readerCST.BaseStream.Position = 4;
                    for (int x = 0; x < cstOffsets.Count - 1; x++)
                        _cstData.Add(readerCST.ReadBytes(cstOffsets[x+1] - cstOffsets[x]));
                }

                _unknownOffsets = Utilities.ConsumeArray<int>(reader, 2);
                int entryCount = reader.ReadInt16();
                reader.BaseStream.Position += 2;

                List<string> materialNames = new List<string>(entryCount);
                for (int i = 0; i < entryCount; ++i) materialNames.Add(Utilities.ReadString(reader));

                reader.BaseStream.Position = firstMaterialOffset + 4;
                for (int i = 0; i < entryCount; i++)
                {
                    Material material = new Material();
                    material.Name = materialNames[i];
                    for (int x = 0; x < 12; x++)
                    {
                        Material.Texture texRef = new Material.Texture();
                        texRef.BinIndex = reader.ReadInt16();
                        int texTableIndex = reader.ReadInt16();
                        if (texTableIndex == -1) continue;
                        texRef.Source = (Material.Texture.TextureSource)texTableIndex;
                        material.TextureReferences[x] = texRef;
                    }
                    reader.BaseStream.Position += 8;
                    List<int> cstIndexes = new List<int>();
                    for (int x = 0; x < 5; x++) cstIndexes.Add(reader.ReadInt32());
                    for (int x = 0; x < 5; x++)
                    {
                        int cstCount = reader.ReadByte();
                        //TODO: just read the CST data into the material
                        material.ConstantBuffers[x] = new Material.ConstantBuffer() { Offset = cstIndexes[x], Length = cstCount };
                    }
                    reader.BaseStream.Position += 7;
                    material.UnknownValue0 = reader.ReadInt32();
                    material.UberShaderIndex = reader.ReadInt32();
                    reader.BaseStream.Position += 128;
                    material.UnknownValue1 = reader.ReadInt32();
                    reader.BaseStream.Position += 48;
                    material.Unknown4_ = reader.ReadInt32();
                    material.Color = reader.ReadInt32();
                    material.UnknownValue2 = reader.ReadInt32();
                    reader.BaseStream.Position += 8;
                    Entries.Add(material);
                    _writeList.Add(material);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            //TODO: actually compute this
            using (BinaryWriter writerCST = new BinaryWriter(File.OpenWrite(_filepathCST)))
            {
                writerCST.BaseStream.SetLength(0);
                //writerCST.Write(new byte[4] { 0x0B, 0xD7, 0x23, 0x3C });
                for (int i = 0; i < _cstData.Count; i++)
                    writerCST.Write(_cstData[i]);
            }

            _writeList.Clear();
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                //Write header
                writer.Write(0); //placeholder file length (-4)
                writer.Write(40);
                writer.Write(0); //placeholder material offset
                if (_cstData.Count != 5) throw new Exception("CST data is not formatted as expected!");
                int cstOffset = 0;
                for (int x = 0; x < 5; x++)
                {
                    writer.Write(cstOffset);
                    cstOffset += _cstData[x].Length;
                }
                for (int i = 0; i < 2; i++) writer.Write(_unknownOffsets[i]);
                writer.Write((Int16)Entries.Count);
                writer.Write((Int16)296);

                //Write material names
                for (int i = 0; i < Entries.Count; ++i) Utilities.WriteString(Entries[i].Name, writer, true);
                Utilities.Align(writer, 4, 0x20);

                //Write material data
                int materialOffset = (int)writer.BaseStream.Position;
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (Entries[i].TextureReferences.Length != 12) throw new Exception("Incorrect texture references!");
                    for (int x = 0; x < 12; x++)
                    {
                        Material.Texture tex = Entries[i].TextureReferences[x];
                        if (tex == null)
                        {
                            writer.Write((Int16)(-1));
                            writer.Write((Int16)(-1));
                        }
                        else
                        {
                            writer.Write((Int16)tex.BinIndex);
                            writer.Write((Int16)tex.Source);
                        }
                    }
                    writer.Write(new byte[4]);
                    writer.Write((Int32)i);
                    if (Entries[i].ConstantBuffers.Length != 5) throw new Exception("Incorrect constant buffer definitions!");
                    for (int x = 0; x < 5; x++)
                    {
                        writer.Write(Entries[i].ConstantBuffers[x].Offset);
                    }
                    for (int x = 0; x < 5; x++)
                    {
                        writer.Write((byte)Entries[i].ConstantBuffers[x].Length);
                    }
                    writer.Write(new byte[7]);
                    writer.Write(Entries[i].UnknownValue0);
                    writer.Write(Entries[i].UberShaderIndex);
                    writer.Write(new byte[128]);
                    writer.Write(Entries[i].UnknownValue1);
                    writer.Write(new byte[48]);
                    writer.Write(Entries[i].Unknown4_);
                    writer.Write(Entries[i].Color);
                    writer.Write(Entries[i].UnknownValue2);
                    writer.Write(new byte[8]);

                    _writeList.Add(Entries[i]);
                }

                //Correct placeholders
                writer.BaseStream.Position = 0;
                writer.Write((int)writer.BaseStream.Length - 4);
                writer.BaseStream.Position = 8;
                writer.Write(materialOffset - 4);
            }
            return true;
        }
        #endregion

        #region HELPERS
        /* Get the current index for a material (useful for cross-ref'ing with compiled binaries)
         * Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk */
        public int GetWriteIndex(Material material)
        {
            if (!_writeList.Contains(material)) return -1;
            return _writeList.IndexOf(material);
        }

        /* Get a material by its current index (useful for cross-ref'ing with compiled binaries)
         * Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk */
        public Material GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index) return null;
            return _writeList[index];
        }
        #endregion

        #region STRUCTURES
        public class Material
        {
            public string Name;

            public Texture[] TextureReferences = new Texture[12]; //Max of 12
            public ConstantBuffer[] ConstantBuffers = new ConstantBuffer[5]; //Should always be 5 (1 per CST block) - TODO: maybe just change this to 5 variables?

            public int UnknownValue0;
            public int UberShaderIndex;

            public int UnknownValue1;

            public int Unknown4_;
            public int Color; // TODO: This is not color
            public int UnknownValue2;

            public override string ToString()
            {
                return "[" + Color + "] " + Name;
            }

            //Offset and length within the CST file
            public class ConstantBuffer
            {
                public int Offset = 0;
                public int Length = 0;
            }

            public class Texture
            {
                public int BinIndex;    // Entry index in texture BIN file.

                public TextureSource Source;

                public enum TextureSource
                {
                    GLOBAL = 2, //Texture comes from ENV/GLOBAL
                    LEVEL = 0,  //Texture comes from the level (in ENV/PRODUCTION)
                }
            };
        }        
        #endregion
    }
}