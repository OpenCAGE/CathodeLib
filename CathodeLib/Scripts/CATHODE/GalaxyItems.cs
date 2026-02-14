using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/GALAXY/GALAXY.ITEMS_BIN
    /// </summary>
    public class GalaxyItems : CathodeFile
    {
        public List<Star> Entries = new List<Star>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE | Implementation.CREATE;

        public GalaxyItems(string path) : base(path) { }
        public GalaxyItems(MemoryStream stream, string path = "") : base(stream, path) { }
        public GalaxyItems(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 4;
                int count = reader.ReadInt32();
                Entries = Utilities.ConsumeArray<Star>(reader, count).ToList();
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            if (Entries.Count > 16 * 1024)
                return false;

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(5);
                writer.Write(Entries.Count);
                Utilities.Write<Star>(writer, Entries.ToArray());
            }
            return true;
        }
        #endregion

        #region HELPERS
        public bool Generate(GalaxyDefinition definition)
        {
            Entries.Clear();
            if (definition == null || definition.StarCount > 16 * 1024)
                return false;
            if (definition.StarCount == 0 || definition.Entries == null || definition.Entries.Count == 0)
                return true;

            float totalFrequency = 0f;
            foreach (var t in definition.Entries)
                totalFrequency += Math.Max(0f, t.Frequency);
            if (totalFrequency <= 0f)
                totalFrequency = 1f;

            var rng = new Random();
            for (int i = 0; i < definition.StarCount; i++)
            {
                float roll = (float)rng.NextDouble() * totalFrequency;
                GalaxyDefinition.StarTemplate template = definition.Entries[0];
                foreach (var t in definition.Entries)
                {
                    float f = Math.Max(0f, t.Frequency);
                    if (roll < f) { template = t; break; }
                    roll -= f;
                }

                float sizeRange = template.MaxSize - template.MinSize;
                float size = template.MinSize + (sizeRange <= 0f ? 0f : (float)rng.NextDouble() * sizeRange);
                float intensityRange = template.MaxIntensity - template.MinIntensity;
                float intensity = template.MinIntensity + (intensityRange <= 0f ? 0f : (float)rng.NextDouble() * intensityRange);

                float px = (float)(rng.NextDouble() * 2.0 - 1.0);
                float py = (float)(rng.NextDouble() * 2.0 - 1.0);
                float pz = (float)(rng.NextDouble() * 2.0 - 1.0);

                Entries.Add(new Star
                {
                    Size = size,
                    Intensity = intensity,
                    Colour = template.Colour,
                    Position = new Vector3(px, py, pz)
                });
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class Star
        {
            public float Size; //0->1
            public float Intensity; //0->1
            public Vector3 Colour; //R,G,B (0->1)
            public Vector3 Position; //X,Y,Z (-1->1)
        }
        #endregion
    }
}