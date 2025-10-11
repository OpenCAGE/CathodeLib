using CATHODE.Animations;
using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using static System.Collections.Specialized.BitVector32;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> SKELE/RAGS
    /// </summary>
    public class Ragdoll : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE;

        public Ragdoll(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public Ragdoll(MemoryStream stream, AnimationStrings strings, string path) : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public Ragdoll(byte[] data, AnimationStrings strings, string path) : base(data, path)
        {
            _strings = strings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private AnimationStrings _strings;

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null || _filepath == null || _filepath == "")
                return false;

            using (BinaryReader reader = new BinaryReader(stream))
            {
                //this is straight up a Havok HKT file

                return true;
            }
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);



                return true;
            }
        }
        #endregion

        #region STRUCTURES

        #endregion
    }
}
