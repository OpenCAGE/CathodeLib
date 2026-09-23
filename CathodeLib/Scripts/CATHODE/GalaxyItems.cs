using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#elif GODOT
using Godot;
using System.Numerics;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;
using Vector4 = Godot.Vector4;
using Color = Godot.Color;
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

        ~GalaxyItems()
        {
            Entries.Clear();
        }

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
        /// <summary>
        /// Regenerate the stars from a definition the way the game's own generator does
        /// (GALAXY_INFO::generate_galaxy_items): each star picks a template by frequency, points along a
        /// random point of the unit cube normalised, takes a size whose reciprocal is uniform between the
        /// template's bounds - so small stars are the common ones, as in every shipped galaxy - and an
        /// intensity uniform between its bounds. Only the random sequence differs from the game's.
        /// </summary>
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
            
            var rng = new System.Random();
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

                //The game normalises whatever point it drew, so the corners of the cube are a little denser
                float px, py, pz, length;
                do
                {
                    px = (float)(rng.NextDouble() * 2.0 - 1.0);
                    py = (float)(rng.NextDouble() * 2.0 - 1.0);
                    pz = (float)(rng.NextDouble() * 2.0 - 1.0);
                    length = (float)Math.Sqrt(px * px + py * py + pz * pz);
                }
                while (length < 1e-6f);

                float size = RandomSize(rng, template.MinSize, template.MaxSize);
                float intensityRange = template.MaxIntensity - template.MinIntensity;
                float intensity = template.MinIntensity + (intensityRange <= 0f ? 0f : (float)rng.NextDouble() * intensityRange);

                Entries.Add(new Star
                {
                    Size = size,
                    Intensity = intensity,
                    Colour = template.Colour,
                    Position = new Vector3(px / length, py / length, pz / length)
                });
            }
            return true;
        }

        /* 1 / uniform(1 / max, 1 / min), as the game draws it: the mean of the shipped DefaultGalaxy's
           0.0005-0.005 range comes out at 0.00128, where a uniform size would give 0.00275 - stars twice
           the size and four times as bright. A bound of zero has no reciprocal (the game would make every
           such star zero-sized, and invisible), so a template with one gets a uniform size instead. */
        private static float RandomSize(System.Random rng, float min, float max)
        {
            if (max < min)
            {
                float swap = min;
                min = max;
                max = swap;
            }
            if (min <= 0f)
                return Math.Max(0f, min + (float)rng.NextDouble() * (max - min));

            float lo = 1f / max;
            float hi = 1f / min;
            return 1f / (lo + (float)rng.NextDouble() * (hi - lo));
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