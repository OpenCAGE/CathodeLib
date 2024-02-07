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
        public Data Entries = new Data();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE | Implementation.SAVE;

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

            Entries = new Data();

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

                Entries.Skeletons = skeletons.ToList();
                Entries.Mappings = mappings.ToList();
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            //TODO: we should write these anim strings to the db
            
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                writer.Write(Entries.Skeletons.Count);
                writer.Write(Entries.Skeletons.Count);

                for (int i = 0; i < Entries.Skeletons.Count; i++)
                {
                    writer.Write(Utilities.AnimationHashedString(Entries.Skeletons[i].SkeletonName));
                    writer.Write(i);
                }
                for (int i = 0; i < Entries.Skeletons.Count; i++)
                {
                    writer.Write(Utilities.AnimationHashedString(Entries.Skeletons[i].Filename));
                }

                writer.Write(Entries.Mappings.Count);
                writer.Write(Entries.Mappings.Count);

                for (int i = 0; i < Entries.Mappings.Count; i++)
                {
                    writer.Write(Utilities.AnimationHashedString(Entries.Mappings[i].Skeleton1));
                    writer.Write(Utilities.AnimationHashedString(Entries.Mappings[i].Skeleton2));
                    writer.Write(i);
                    writer.Write(0);
                }
                for (int i = 0; i < Entries.Mappings.Count; i++)
                {
                    writer.Write(Utilities.AnimationHashedString(Entries.Mappings[i].Name));
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Data
        {
            public List<Skeletons> Skeletons = new List<Skeletons>();
            public List<SkeletonMapping> Mappings = new List<SkeletonMapping>();
        }
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
