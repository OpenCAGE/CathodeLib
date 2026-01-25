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
    /// DATA/GLOBAL/ANIMATION.PAK -> DATA/ANIM_SYS/SKELE/DB.BIN
    /// </summary>
    public class SkeletonDB : CathodeFile
    {
        public List<Tuple<string, string>> Skeletons = new List<Tuple<string, string>>();
        public List<Tuple<Tuple<string, string>, string>> Mappings = new List<Tuple<Tuple<string, string>, string>>();

        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE;

        public SkeletonDB(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public SkeletonDB(MemoryStream stream, AnimationStrings strings, string path) : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public SkeletonDB(byte[] data, AnimationStrings strings, string path) : base(data, path)
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
                Skeletons = HashTable.Read(reader, (r, n) =>
                    new Tuple<string, string>(n, _strings.GetString(r.ReadUInt32()))
                , _strings);

                //this is a hash table as above, but slightly different
                int hashTableSize = reader.ReadInt32();
                int usedSize = reader.ReadInt32();
                if (hashTableSize != usedSize)
                    throw new Exception("Unexpected");
                Tuple<string, string>[] names = new Tuple<string, string>[hashTableSize];
                for (int i = 0; i < hashTableSize; i++)
                {
                    uint SkeletonA = reader.ReadUInt32();
                    uint SkeletonB = reader.ReadUInt32();
                    int index = reader.ReadInt32();
                    reader.BaseStream.Position += 4;
                    names[index] = new Tuple<string, string>(_strings.GetString(SkeletonA), _strings.GetString(SkeletonB));
                }
                for (int i = 0; i < hashTableSize; i++)
                    Mappings.Add(new Tuple<Tuple<string, string>, string>(names[i], _strings.GetString(reader.ReadUInt32())));

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
