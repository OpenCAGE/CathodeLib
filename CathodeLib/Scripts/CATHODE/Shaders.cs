using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using static CATHODE.LEGACY.ShadersPAK;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/RENDERABLE/LEVEL_SHADERS_DX11.PAK & LEVEL_SHADERS_DX11_BIN.PAK & LEVEL_SHADERS_DX11_IDX_REMAP.PAK */
    public class Shaders : CathodeFile
    {
        public List<Shader> Entries = new List<Shader>();
        public static new Implementation Implementation = Implementation.NONE;
        public Shaders(string path) : base(path) { }

        private string _filepathBIN;
        private string _filepathIDX;

        #region FILE_IO
        override protected bool LoadInternal()
        {
            string trimmed = _filepath.Substring(0, _filepath.Length - 4);
            _filepathBIN = trimmed + "_BIN.PAK";
            _filepathIDX = trimmed + "_IDX_REMAP.PAK";

            if (!File.Exists(_filepathBIN)) return false;
            if (!File.Exists(_filepathIDX)) return false;

            List<Utilities.PAKContent> content = Utilities.ReadPAK(_filepathBIN, FileIdentifiers.SHADER_DATA);
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i].BinIndex != i) return false;

                using (BinaryReader reader = new BinaryReader(new MemoryStream(content[i].Data)))
                {

                }
            }

            //Read the index remapping pak (this is actually just an incrementing count lol)
            content = Utilities.ReadPAK(_filepathIDX, FileIdentifiers.SHADER_DATA);
            for (int i = 0; i < content.Count; i++)
            {
                if (content[i].BinIndex != i) return false;

                using (BinaryReader reader = new BinaryReader(new MemoryStream(content[i].Data)))
                {
                    int index = reader.ReadInt32();
                    if (index != i) return false;
                }
            }

            content = Utilities.ReadPAK(_filepath, FileIdentifiers.SHADER_DATA);
            for (int i = 0; i < content.Count; i++)
            {
                using (BinaryReader reader = new BinaryReader(new MemoryStream(content[i].Data)))
                {
                    Entries.Add(new Shader());

                    reader.BaseStream.Position = 8; //0x7725BBA4, 36, 1

                    int textureCount = reader.ReadInt16();
                    int[] cstCounts = new int[5];
                    for (int x = 0; x < 5; x++) cstCounts[x] = reader.ReadInt32();
                    int textureLinkCount = reader.ReadInt16();
                    string name = Utilities.ReadString(reader.ReadBytes(40));
                    ShaderCategory category = (ShaderCategory)reader.ReadInt16();

                    // Index 0: 0x04: seems to be metal. 0x01: seems to be non-metal. But it has many exceptions so maybe check renderdoc.
                    // Index 1: ParallaxMap? It seems to be either 0x30 and 0x80 for the most part, sometimes it combines both into 0xB0.
                    // Index 2: Lower part of this byte is NormalMap0 related! 0x04 means it has normal map 0. No hits for 1, 2 or 8 yet.
                    //          0x03 is a thing, but not normal map. What is it? Can be 0x07 where it has normal map and both the 0x03 thing.
                    //          0x0C is also a thing (8 and 4), so it has normal map and something else?
                    //          I'm gonna say 0x03 means DiffuseMap1.
                    // Index 3: 0x04 Not OcclusionTint. (NormalMap0 + NormalMap0UVMultiplier + NormalMap0Strength?)
                    // Index 4: Seems to tell me about AO. If 4, then it has AO tint. If 1 it has AO texture.
                    // Index 6: Seems to tell me about Dirt/OpacityNoise.
                    //      0x19 (0001 1001): has opacity noise and dirt maps.
                    //      0x38 (0011 1000): has dirt map.
                    //      0x39 (0011 1001): has opacity noise and dirt maps.
                    // Index 10: First half, only found 4, and it seems to be something included in all (Diffuse0 as well?).
                    // Seems like the least significant bit enables/disables opacity noise?
                    int[] flags = new int[20];
                    for (int x = 0; x < 20; x++) flags[x] = reader.ReadByte();

                    int unk = reader.ReadInt32();

                    int entryCount = reader.ReadInt16();
                    for (int x = 0; x < entryCount; x++)
                    {
                        int unk1 = reader.ReadInt16();
                        int unk2 = reader.ReadInt32();
                    }

                    for (int x = 0; x < textureCount; x++)
                    {
                        int unk1 = reader.ReadByte();
                        int unk2 = reader.ReadByte();
                        int[] unk3 = new int[16];
                        for (int z = 0; z < 16; z++) unk3[z] = reader.ReadInt16();
                        float unk4 = reader.ReadSingle();
                        int unk5 = reader.ReadInt16();
                        float unk6 = reader.ReadSingle();
                    }

                    for (int x = 0; x < textureCount; x++)
                    {
                        int unk1 = reader.ReadByte();
                    }

                    int[][] cstLinks = new int[5][];
                    for (int x = 0; x < 5; x++)
                    {
                        cstLinks[x] = new int[cstCounts[x]];
                        for (int z = 0; z < cstCounts[x]; z++)
                        {
                            cstLinks[x][z] = reader.ReadByte();
                        }
                    }

                    int[] textureLinks = new int[textureLinkCount];
                    for (int x = 0; x < textureLinkCount; x++)
                    {
                        textureLinks[x] = reader.ReadByte();
                    }

                    int vertexShader = reader.ReadInt32();
                    int pixelShader = reader.ReadInt32();
                    int hullShader = reader.ReadInt32();
                    int domainShader = reader.ReadInt32();

                    //Interestingly, seems like neither of these are used
                    int geometryShader = reader.ReadInt32();
                    int computeShader = reader.ReadInt32();
                }
            }

            return true;
        }

        override protected bool SaveInternal()
        {
            List<Utilities.PAKContent> content = new List<Utilities.PAKContent>();
            for (int i = 0; i < Entries.Count; i++)
            {

            }
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepathBIN)))
            {

            }

            content = new List<Utilities.PAKContent>();
            for (int i = 0; i < Entries.Count; i++)
            {
                content.Add(new Utilities.PAKContent()
                {
                    BinIndex = i,
                    Data = BitConverter.GetBytes((Int32)i)
                });
            }
            Utilities.WritePAK(_filepathIDX, FileIdentifiers.SHADER_DATA, content);

            content = new List<Utilities.PAKContent>();
            for (int i = 0; i < Entries.Count; i++)
            {

            }
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {

            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Shader
        {
            public string FileName = ""; //The name of the file in the shader archive (unsure how to get this right now with the weird _BIN/PAK way of working)

            public int FileLength = 0; //The length of the file in the archive for this header
            public int FileLengthWithPadding = 0; //The length of the file in the archive for this header, with any padding at the end of the file included

            public int FileOffset = 0; //Position in archive from end of header list
            public int FileIndex = 0; //The index of the file

            public byte[] FileContent; //The content for the file

            //I think this is just garbage that got dumped by accident
            public byte[] StringPart1; //4 bytes that look like they're part of a filepath
            public byte[] StringPart2; //4 bytes that look like they're part of a filepath
        }
        #endregion
    }
}