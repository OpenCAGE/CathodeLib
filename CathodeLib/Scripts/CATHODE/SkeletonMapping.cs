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
using System.Security.Cryptography;
using System.Text;
using static System.Collections.Specialized.BitVector32;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> SKELE/MAPS
    /// </summary>
    public class SkeletonMapping : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE;

        public SkeletonMapping(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public SkeletonMapping(MemoryStream stream, AnimationStrings strings, string path) : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public SkeletonMapping(byte[] data, AnimationStrings strings, string path) : base(data, path)
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

            ShortGuid skeletonAName;
            ShortGuid skeletonBName;
            byte[] remainingContent;

            using (BinaryReader reader = new BinaryReader(stream))
            {
                skeletonAName = Utilities.Consume<ShortGuid>(reader);
                skeletonBName = Utilities.Consume<ShortGuid>(reader);
                int hkt_length = reader.ReadInt32();
                byte[] hkt = reader.ReadBytes(hkt_length);

                remainingContent = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position)); // I think this is index remappings (?)

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
