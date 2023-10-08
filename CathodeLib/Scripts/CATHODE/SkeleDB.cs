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
    /* DATA/GLOBAL/ANIMATION.PAK -> ANIM_SYS/SKELE/DB.BIN */
    public class SkeleDB : CathodeFile
    {
        public Dictionary<uint, string> Entries = new Dictionary<uint, string>();
        public static new Implementation Implementation = Implementation.LOAD;

        public SkeleDB(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;

            //TEMP
            OnLoadBegin?.Invoke(_filepath);
            if (LoadInternal())
            {
                _loaded = true;
                OnLoadSuccess?.Invoke(_filepath);
            }
        }

        private AnimationStrings _strings;

        #region FILE_IO
        override protected bool LoadInternal()
        {
            if (_strings == null)
                return false;

            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                int pairCount = reader.ReadInt32();
                int idCount = reader.ReadInt32();

                if (pairCount != idCount)
                    return false;

                Skeletons[] skeletons = new Skeletons[pairCount];
                for (int i = 0; i < pairCount; i++)
                {
                    string name = _strings.Entries[reader.ReadUInt32()];
                    skeletons[reader.ReadInt32()] = new Skeletons() { SkeletonName = name };
                }
                for (int i = 0; i < idCount; i++)
                {
                    skeletons[i].Filename = _strings.Entries[reader.ReadUInt32()];
                }

                int mappingIDCount = reader.ReadInt32();
                int mappingCount = reader.ReadInt32();

                if (mappingIDCount != mappingCount)
                    return false;

                SkeletonMapping[] mappings = new SkeletonMapping[mappingIDCount];
                for (int i = 0; i < mappingCount; i++)
                {
                    string skele1 = _strings.Entries[reader.ReadUInt32()];
                    string skele2 = _strings.Entries[reader.ReadUInt32()];

                    mappings[reader.ReadInt32()] = new SkeletonMapping()
                    {
                        Skeleton1 = skele1,
                        Skeleton2 = skele2,
                    };
                    reader.BaseStream.Position += 4;
                }
                for (int i = 0; i < mappingIDCount; i++)
                {
                    mappings[i].Name = _strings.Entries[reader.ReadUInt32()];
                }

                string sdfsdf = "";
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                writer.Write(Entries.Count);
                writer.Write(Entries.Count);
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class Skeletons
        {
            public string Filename;
            public string SkeletonName;
        }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SkeletonMapping
        {
            public string Name;
            public string Skeleton1;
            public string Skeleton2;
        };
        #endregion
    }
}
