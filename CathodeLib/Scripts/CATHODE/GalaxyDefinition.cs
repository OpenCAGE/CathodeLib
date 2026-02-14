using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using static CATHODE.GalaxyItems;
using CATHODE.ShaderTypes;



#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/GALAXY/GALAXY.DEFINITION_BIN
    /// </summary>
    public class GalaxyDefinition : CathodeFile
    {
        public string Name;
        public int StarCount;
        public List<StarTemplate> Entries = new List<StarTemplate>();

        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public GalaxyDefinition(string path) : base(path) { }
        public GalaxyDefinition(MemoryStream stream, string path = "") : base(stream, path) { }
        public GalaxyDefinition(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 4;
                byte[] stringBlock = reader.ReadBytes(128);
                Name = Utilities.ReadString(stringBlock);
                StarCount = reader.ReadInt32();
                int count = reader.ReadInt32();
                Entries = Utilities.ConsumeArray<StarTemplate>(reader, count).ToList();
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            if (StarCount > 16 * 1024)
                return false;

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(5);
                writer.Write(new byte[128]);
                writer.BaseStream.Position -= 128;
                Utilities.WriteString(Name, writer, false);
                writer.BaseStream.Position += 128 - Name.Length;
                writer.Write(StarCount);
                writer.Write(Entries.Count);
                Utilities.Write<StarTemplate>(writer, Entries.ToArray());
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class StarTemplate
        {
            public float Frequency; //Frequency of occurance relative to other galaxy items
            public float MinSize; //Angular size in radians
            public float MaxSize; //Angular size in radians
            public float MinIntensity; //0->1
            public float MaxIntensity; //0->1
            public Vector3 Colour; //R,G,B (0->1)
        }
        #endregion
    }
}