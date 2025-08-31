using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using CATHODE.Scripting;
using CathodeLib;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> ANIM_CLIP_DB.BIN
    /// </summary>
    public class AnimClipDB : CathodeFile
    {
        public Dictionary<uint, string> Entries = new Dictionary<uint, string>();
        public static new Implementation Implementation = Implementation.LOAD;

        public AnimClipDB(string path) : base(path) { }
        public AnimClipDB(MemoryStream stream, string path = "") : base(stream, path) { }
        public AnimClipDB(byte[] data, string path = "") : base(data, path) { }

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

        #endregion
    }
}
