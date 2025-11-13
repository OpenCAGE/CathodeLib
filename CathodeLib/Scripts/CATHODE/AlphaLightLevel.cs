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
    /// <summary>
    /// DATA/ENV/x/WORLD/ALPHALIGHT_LEVEL.BIN
    /// </summary>
    public class AlphaLightLevel : CathodeFile
    {
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public AlphaLightLevel(string path) : base(path) { }
        public AlphaLightLevel(MemoryStream stream, string path = "") : base(stream, path) { }
        public AlphaLightLevel(byte[] data, string path = "") : base(data, path) { }

        public Vector2 Resolution;
        public byte[] ImageData; // this is in A16B16G16R16F format

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 8;
                Resolution = new Vector2(reader.ReadInt32(), reader.ReadInt32());
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                ImageData = reader.ReadBytes((int)Resolution.x * (int)Resolution.y * 8);
#else
                ImageData = reader.ReadBytes((int)Resolution.X * (int)Resolution.Y * 8);
#endif
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
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                writer.Write(Resolution.x);
                writer.Write(Resolution.y);
#else
                writer.Write(Resolution.X);
                writer.Write(Resolution.Y);
#endif
                writer.Write(ImageData);
            }
            return true;
        }
        #endregion
    }
}