using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/RENDERABLE/RADIOSITY_RUNTIME.BIN
    /// </summary>
    public class RadiosityRuntime : CathodeFile
    {
        public static new Implementation Implementation = Implementation.NONE;

        public RadiosityRuntime(string path) : base(path) { }
        public RadiosityRuntime(MemoryStream stream, string path = "") : base(stream, path) { }
        public RadiosityRuntime(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {

            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

            }
            return true;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class placeholder
        {

        }
        #endregion
    }
}