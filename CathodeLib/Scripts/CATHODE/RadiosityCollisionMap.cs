using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;

namespace CATHODE
{
    /// <summary>
    /// DATA/ENV/x/WORLD/RADIOSITY_COLLISION_MAPPING.BIN
    /// </summary>
    public class RadiosityCollisionMap : CathodeFile
    {
        public static new Implementation Implementation = Implementation.NONE;

        public RadiosityCollisionMap(string path) : base(path) { }
        public RadiosityCollisionMap(MemoryStream stream, string path = "") : base(stream, path) { }
        public RadiosityCollisionMap(byte[] data, string path = "") : base(data, path) { }

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