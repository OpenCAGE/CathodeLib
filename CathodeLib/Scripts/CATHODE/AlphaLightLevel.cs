using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/ALPHALIGHT_LEVEL.BIN */
    public class AlphaLightLevel : CathodeFile
    {
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public AlphaLightLevel(string path) : base(path) { }

        public Vector2 Resolution;
        public byte[] ImageData; // this is in A16B16G16R16F format

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 8;
                Resolution = new Vector2(reader.ReadInt32(), reader.ReadInt32());
                ImageData = reader.ReadBytes((int)Resolution.X * (int)Resolution.Y * 8);
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                Utilities.WriteString("alph", writer);
                writer.Write(0);
                writer.Write(Resolution.X);
                writer.Write(Resolution.Y);
                writer.Write(ImageData);
            }
            return true;
        }
        #endregion
    }
}