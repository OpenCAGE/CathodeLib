using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CathodeLib;

namespace CATHODE
{
    /* Handles Cathode MODELS.MTL files */
    public class Materials : CathodeFile
    {
        public List<Material> Entries = new List<Material>();
        public static new Impl Implementation = Impl.LOAD;
        public Materials(string path) : base(path) { }

        private int[] _cstOffsets;       //TODO: what and why?
        private int[] _unknownOffsets;   //

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position = 8;
                int firstMaterialOffset = reader.ReadInt32();
                _cstOffsets = Utilities.ConsumeArray<int>(reader, 5);
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
                        texRef.Index = reader.ReadInt16();
                        int texTableIndex = reader.ReadInt16();
                        if (texTableIndex == -1) continue;
                        texRef.Source = (Material.Texture.TextureSource)texTableIndex;
                        material.TextureReferences.Add(texRef);
                    }
                    reader.BaseStream.Position += 8;
                    for (int x = 0; x < 5; x++)
                    {
                        material.CSTOffsets[x] = reader.ReadInt32();
                    }
                    for (int x = 0; x < 5; x++)
                    {
                        material.CSTCounts[x] = reader.ReadByte();
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
                }
            }
            return true;
        }


        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                //Write header
                writer.Write(0); //placeholder file length (-4)
                writer.Write(40);
                writer.Write(0); //placeholder material offset
                for (int i = 0; i < 5; i++) writer.Write(_cstOffsets[i]); 
                for (int i = 0; i < 2; i++) writer.Write(_unknownOffsets[i]);
                writer.Write((Int16)Entries.Count);
                writer.Write((Int16)296);

                //Write material names
                for (int i = 0; i < Entries.Count; ++i) Utilities.WriteString(Entries[i].Name, writer, true);
                Utilities.Align(writer, 8);

                //Write material data
                int materialOffset = (int)writer.BaseStream.Position;
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (Entries[i].TextureReferences.Count > 12) throw new Exception("Too many texture references!");
                    for (int x = 0; x < 12; x++)
                    {
                        if (x >= Entries[i].TextureReferences.Count)
                        {
                            writer.Write((Int16)(-1));
                            writer.Write((Int16)(-1));
                        }
                        else
                        {
                            writer.Write((Int16)Entries[i].TextureReferences[x].Index);
                            writer.Write((Int16)Entries[i].TextureReferences[x].Source);
                        }
                    }
                    writer.Write(new byte[4]);
                    writer.Write((Int32)i);
                    if (Entries[i].CSTOffsets.Length != 5) throw new Exception("Wrong CST offset count!");
                    for (int x = 0; x < 5; x++) writer.Write((Int32)Entries[i].CSTOffsets[x]);
                    if (Entries[i].CSTCounts.Length != 5) throw new Exception("Wrong CST count count!");
                    for (int x = 0; x < 5; x++) writer.Write((byte)Entries[i].CSTCounts[x]);
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

        #region STRUCTURES
        public class Material
        {
            public string Name;

            public List<Texture> TextureReferences = new List<Texture>(); //Max of 12

            public int[] CSTOffsets = new int[5];
            public byte[] CSTCounts = new byte[5];

            public int UnknownValue0;
            public int UberShaderIndex;

            public int UnknownValue1;

            public int Unknown4_;
            public int Color; // TODO: This is not really color AFAIK.
            public int UnknownValue2;

            public class Texture
            {
                public int Index; // Entry index in texture BIN file.
                public TextureSource Source;

                public enum TextureSource
                {
                    GLOBAL, //Texture comes from ENV/GLOBAL
                    LEVEL,  //Texture comes from the level (in ENV/PRODUCTION)
                }
            };
        };
        #endregion
    }
}