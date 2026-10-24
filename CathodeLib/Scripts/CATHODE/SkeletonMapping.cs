using CATHODE.Animations;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> SKELE/MAPS (and SKELE/MAPS64)
    ///
    /// Retargeting data that lets an animation authored for one skeleton play on another.
    /// Names the two skeletons, then a Havok packfile holding a pair of hkaSkeletonMapper
    /// objects (one per direction), then CATHODE's own bone index tables.
    /// </summary>
    public class SkeletonMapping : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE | Implementation.SAVE;

        /// <summary>The skeleton being mapped from.</summary>
        public string SkeletonA = "";

        /// <summary>The skeleton being mapped to.</summary>
        public string SkeletonB = "";

        /// <summary>Indices of the bones this mapping actually touches.</summary>
        public List<int> MappedBones = new List<int>();

        /// <summary>Bone index -> position in <see cref="MappedBones"/>, or -1 if the bone isn't mapped.</summary>
        public List<int> BoneLookup = new List<int>();

        /// <summary>The parsed Havok packfile holding the mappers.</summary>
        public HavokPackfile Havok = null;

        public SkeletonMapping(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public SkeletonMapping(MemoryStream stream, AnimationStrings strings, string path = "") : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public SkeletonMapping(byte[] data, AnimationStrings strings, string path = "") : base(data, path)
        {
            _strings = strings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private AnimationStrings _strings;
        private byte[] _havok = new byte[0];

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null)
                return false;

            MappedBones.Clear();
            BoneLookup.Clear();

            using (BinaryReader reader = new BinaryReader(stream))
            {
                SkeletonA = _strings.GetString(reader.ReadUInt32());
                SkeletonB = _strings.GetString(reader.ReadUInt32());

                int havokLength = reader.ReadInt32();
                if (havokLength < 0 || havokLength > reader.BaseStream.Length - reader.BaseStream.Position)
                    return false;
                _havok = reader.ReadBytes(havokLength);
                Havok = new HavokPackfile(_havok);

                int mappedCount = reader.ReadInt32();
                if (mappedCount < 0 || (mappedCount * 4) > reader.BaseStream.Length - reader.BaseStream.Position)
                    return false;
                for (int i = 0; i < mappedCount; i++)
                    MappedBones.Add(reader.ReadInt32());

                //The lookup runs to the end of the file - its length is the source skeleton's bone count
                while (reader.BaseStream.Position + 4 <= reader.BaseStream.Length)
                    BoneLookup.Add(reader.ReadInt32());

                return true;
            }
        }

        override protected bool SaveInternal()
        {
            byte[] content = ToBytes();
            if (content == null) return false;
            File.WriteAllBytes(_filepath, content);
            return true;
        }

        /// <summary>
        /// Serialise back to the format stored in ANIMATION.PAK.
        /// </summary>
        public byte[] ToBytes()
        {
            if (_havok.Length == 0) return null;

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(_strings.GetID(SkeletonA));
                writer.Write(_strings.GetID(SkeletonB));

                writer.Write(_havok.Length);
                writer.Write(_havok);

                writer.Write(MappedBones.Count);
                for (int i = 0; i < MappedBones.Count; i++)
                    writer.Write(MappedBones[i]);
                for (int i = 0; i < BoneLookup.Count; i++)
                    writer.Write(BoneLookup[i]);

                return stream.ToArray();
            }
        }
        #endregion

        #region ACCESSORS
        /// <summary>
        /// The bone-to-bone mappings inside the Havok data. There are normally two - one for each
        /// direction between <see cref="SkeletonA"/> and <see cref="SkeletonB"/>.
        /// </summary>
        public List<HavokPackfile.SkeletonMapper> GetMappers()
        {
            return Havok == null ? new List<HavokPackfile.SkeletonMapper>() : Havok.GetSkeletonMappers();
        }
        #endregion
    }
}
