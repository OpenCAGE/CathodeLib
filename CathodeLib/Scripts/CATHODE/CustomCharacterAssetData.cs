using CATHODE.Enums;
using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* DATA/CHR_INFO/CUSTOMCHARACTERASSETDATA.BIN */
    public class CustomCharacterAssetData : CathodeFile
    {
        public List<AssetDefinition> Entries = new List<AssetDefinition>();
        public static new Implementation Implementation = Implementation.NONE;
        public CustomCharacterAssetData(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4; //version
                int count = reader.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    AssetDefinition assetDef = new AssetDefinition();
                    assetDef.AssetType = (CharacterAsset)reader.ReadInt32();
                    Dictionary<ColourType, int> colourCounts = new Dictionary<ColourType, int>();
                    foreach (ColourType colour in Enum.GetValues(typeof(ColourType)))
                    {
                        colourCounts.Add(colour, reader.ReadInt32());
                    }
                    foreach (ColourType colour in Enum.GetValues(typeof(ColourType)))
                    {
                        assetDef.Tints.Add(colour, Utilities.ConsumeArray<Vector3>(reader, colourCounts[colour]).ToList());
                    }
                    int decalCount = reader.ReadInt32();
                    for (int x = 0; x < decalCount; x++)
                    {
                        byte[] stringBlock = reader.ReadBytes(260);
                        assetDef.Decals.Add(Utilities.ReadString(stringBlock));
                    }
                    Entries.Add(assetDef);
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.Write(1);
                writer.Write(Entries.Count);
                foreach (AssetDefinition assetDef in Entries)
                {
                    writer.Write((int)assetDef.AssetType);
                    foreach (ColourType colour in Enum.GetValues(typeof(ColourType)))
                    {
                        writer.Write(assetDef.Tints[colour].Count);
                    }
                    foreach (ColourType colour in Enum.GetValues(typeof(ColourType)))
                    {
                        Utilities.Write(writer, assetDef.Tints[colour]);
                    }
                    writer.Write(assetDef.Decals.Count);
                    foreach (string decal in assetDef.Decals)
                    {
                        Utilities.WriteString(decal, writer);
                        writer.Write(new byte[260 - decal.Length]);
                    }
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class AssetDefinition
        {
            public CharacterAsset AssetType;
            public Dictionary<ColourType, List<Vector3>> Tints = new Dictionary<ColourType, List<Vector3>>();
            public List<string> Decals = new List<string>();
        };

        public enum ColourType
        {
            PRIMARY,
            SECONDARY,
            TERTIARY,
        };
        #endregion
    }
}