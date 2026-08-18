using CATHODE.Animations;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> ANIM_CLIP_DB_SEC_*.BIN
    ///
    /// One loadable section of animation: the skeletons it needs, a Havok packfile of the
    /// compressed clips themselves, and a metadata DB tagging moments in those clips.
    /// </summary>
    public class AnimClipDBSec : CathodeFile
    {
        public static new Implementation Implementation = Implementation.LOAD | Implementation.CREATE | Implementation.SAVE;

        /// <summary>Skeletons the clips in this section are authored against.</summary>
        public List<string> SkeletonDependencies = new List<string>();

        /// <summary>The Havok packfile holding this section's hkaAnimationContainer.</summary>
        public HavokPackfile Havok = null;

        /// <summary>Metadata tags per clip, in clip order. Empty entries are clips with no tags.</summary>
        public List<MetadataSet> Metadata = new List<MetadataSet>();

        public AnimClipDBSec(string path, AnimationStrings strings, AnimationStrings debugStrings = null) : base(path)
        {
            _strings = strings;
            _debug = debugStrings;
            _loaded = Load();
        }
        public AnimClipDBSec(MemoryStream stream, AnimationStrings strings, string path = "", AnimationStrings debugStrings = null) : base(stream, path)
        {
            _strings = strings;
            _debug = debugStrings;
            _loaded = Load(stream);
        }
        public AnimClipDBSec(byte[] data, AnimationStrings strings, string path = "", AnimationStrings debugStrings = null) : base(data, path)
        {
            _strings = strings;
            _debug = debugStrings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private AnimationStrings _strings;
        private AnimationStrings _debug;

        /* Argument and clip names live in the debug string DB, so fall back to it */
        private string Name(uint id)
        {
            if (_strings.Entries.TryGetValue(id, out string value)) return value;
            if (_debug != null && _debug.Entries.TryGetValue(id, out string debug)) return debug;
            return id.ToString();
        }
        private byte[] _havok = new byte[0];

        /* The metadata DB is a memory image with internal offsets - we read the tags out of it but
         * don't rebuild it, so it's carried through a save as it came in. */
        private byte[] _metadata = new byte[0];

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null)
                return false;

            SkeletonDependencies.Clear();
            Metadata.Clear();

            using (BinaryReader reader = new BinaryReader(stream))
            {
                int dependsCount = reader.ReadInt32();
                if (dependsCount < 0 || (long)dependsCount * 4 > reader.BaseStream.Length - reader.BaseStream.Position)
                    return false;
                for (int i = 0; i < dependsCount; i++)
                    SkeletonDependencies.Add(_strings.GetString(reader.ReadUInt32()));

                int havokLength = reader.ReadInt32();
                if (havokLength < 0 || havokLength > reader.BaseStream.Length - reader.BaseStream.Position)
                    return false;
                _havok = reader.ReadBytes(havokLength);
                Havok = new HavokPackfile(_havok);

                int metadataLength = reader.ReadInt32();
                if (metadataLength < 0 || metadataLength > reader.BaseStream.Length - reader.BaseStream.Position)
                    return false;
                _metadata = reader.ReadBytes(metadataLength);
                ReadMetadata();

                return true;
            }
        }

        /* The DB is a memory image: "MDDB", a count, then that many absolute offsets to metadata
         * sets. A set's header holds absolute offsets to its own arrays. */
        private void ReadMetadata()
        {
            if (_metadata.Length < 8 || Encoding.ASCII.GetString(_metadata, 0, 4) != "MDDB")
                return;

            int count = BitConverter.ToInt32(_metadata, 4);
            if (count < 0 || (long)count * 8 + 8 > _metadata.Length) return;

            for (int i = 0; i < count; i++)
            {
                MetadataSet set = new MetadataSet();
                Metadata.Add(set);

                long offset = BitConverter.ToInt64(_metadata, 8 + (i * 8));
                if (offset <= 0 || offset + SetHeaderSize > _metadata.Length) continue;
                ReadMetadataSet(offset, set);
            }
        }

        /* A set is a 32 byte header - eight unknown bytes, a pointer to the set body, a pointer to
         * the instance area, then the instance count - with the common block inlined at +32. */
        private void ReadMetadataSet(long offset, MetadataSet set)
        {
            set.Common = ReadBlock(offset + 32, out long end);
            uint instances = BitConverter.ToUInt32(_metadata, (int)offset + 24);
            if (instances == 0 || instances > 512 || end < 0) return;

            /* Each instance carries its own block. They follow the common one back to back, but
             * with some slack between them, so find each by its own signature. */
            for (int i = 0; i < instances; i++)
            {
                long at = -1;
                for (long probe = end; probe <= end + 256; probe += 4)
                    if (IsBlock(probe)) { at = probe; break; }
                if (at < 0) return;

                MetadataBlock block = ReadBlock(at, out end);
                if (block == null || end < 0) return;
                set.Instances.Add(block);
            }
        }

        /* A block's argument pointer always names its own base + 40, which is enough to pick the
         * block out of the surrounding slack. */
        private bool IsBlock(long at)
        {
            return at >= 0 && at + BlockSize <= _metadata.Length && BitConverter.ToInt64(_metadata, (int)at) == at + 40;
        }

        /* 40 bytes: the argument and property arrays, then a count for each. Both pointers name a
         * position 16 bytes before the array they point at. */
        private MetadataBlock ReadBlock(long at, out long end)
        {
            end = -1;
            if (at < 0 || at + BlockSize > _metadata.Length) return null;

            long arguments = BitConverter.ToInt64(_metadata, (int)at) + 16;
            long properties = BitConverter.ToInt64(_metadata, (int)at + 8);
            uint argumentCount = BitConverter.ToUInt32(_metadata, (int)at + 16);
            uint propertyCount = BitConverter.ToUInt32(_metadata, (int)at + 20);
            if (argumentCount > 4096 || propertyCount > 4096) return null;
            if (arguments + (argumentCount * ArgumentSize) > _metadata.Length) return null;

            MetadataBlock block = new MetadataBlock();
            for (int i = 0; i < argumentCount; i++)
                block.Arguments.Add(ReadArgument((int)(arguments + (i * ArgumentSize))));
            end = arguments + (argumentCount * ArgumentSize);

            if (properties == 0 || propertyCount == 0) return block;
            properties += 16;
            if (properties + (propertyCount * PropertySize) > _metadata.Length) return block;
            end = properties + (propertyCount * PropertySize);

            for (int i = 0; i < propertyCount; i++)
            {
                long entry = properties + (i * PropertySize);
                MetadataProperty property = new MetadataProperty
                {
                    Name = Name(BitConverter.ToUInt32(_metadata, (int)entry)),
                };
                block.Properties.Add(property);

                //the times sit eight bytes past the pointer, not sixteen like the arrays do
                long times = BitConverter.ToInt64(_metadata, (int)entry + 16) + 8;
                uint count = BitConverter.ToUInt32(_metadata, (int)entry + 32);
                if (count > 4096 || times < 0 || times + (count * 4) > _metadata.Length) continue;
                for (int t = 0; t < count; t++)
                    property.Times.Add(BitConverter.ToSingle(_metadata, (int)(times + (t * 4))));
                if (times + (count * 4) > end) end = times + (count * 4);
            }
            return block;
        }

        /* 48 bytes: the name, then the value, then the type and its flags. */
        private MetadataArgument ReadArgument(int at)
        {
            MetadataArgument argument = new MetadataArgument
            {
                Name = Name(BitConverter.ToUInt32(_metadata, at)),
                Type = (MetadataValueType)BitConverter.ToUInt32(_metadata, at + 32),
                RequiresConvert = BitConverter.ToInt16(_metadata, at + 36),
                CanMirror = _metadata[at + 38] != 0,
                CanModulateByPlayspeed = _metadata[at + 39] != 0,
            };

            int value = at + 16;
            switch (argument.Type)
            {
                case MetadataValueType.BOOL:
                    argument.Value = BitConverter.ToUInt32(_metadata, value) != 0;
                    break;
                case MetadataValueType.UINT32:
                    argument.Value = BitConverter.ToUInt32(_metadata, value);
                    break;
                case MetadataValueType.INT32:
                    argument.Value = BitConverter.ToInt32(_metadata, value);
                    break;
                case MetadataValueType.FLOAT32:
                    argument.Value = BitConverter.ToSingle(_metadata, value);
                    break;
                //audio events and property references are named the same way strings are
                case MetadataValueType.STRING:
                case MetadataValueType.AUDIO:
                case MetadataValueType.PROPERTY_REFERENCE:
                    uint id = BitConverter.ToUInt32(_metadata, value);
                    argument.Value = (int)id == -1 ? "" : Name(id);
                    break;
                case MetadataValueType.VECTOR:
                    argument.Value = new System.Numerics.Vector3(
                        BitConverter.ToSingle(_metadata, value),
                        BitConverter.ToSingle(_metadata, value + 4),
                        BitConverter.ToSingle(_metadata, value + 8));
                    break;
            }
            return argument;
        }

        private const int SetHeaderSize = 72;
        private const int BlockSize = 40;
        private const int ArgumentSize = 48;
        private const int PropertySize = 48;

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
                writer.Write(SkeletonDependencies.Count);
                for (int i = 0; i < SkeletonDependencies.Count; i++)
                    writer.Write(_strings.GetID(SkeletonDependencies[i]));

                writer.Write(_havok.Length);
                writer.Write(_havok);

                writer.Write(_metadata.Length);
                writer.Write(_metadata);

                return stream.ToArray();
            }
        }
        #endregion

        #region ACCESSORS
        /// <summary>
        /// The animation clips in this section: how long each is, which skeleton it was authored
        /// against, and which bone every transform track drives.
        /// </summary>
        public List<HavokPackfile.AnimationClip> GetAnimations()
        {
            return Havok == null ? new List<HavokPackfile.AnimationClip>() : Havok.GetAnimations();
        }
        #endregion

        #region STRUCTURES
        /// <summary>
        /// Metadata attached to one animation clip: the values shared by every use of the clip,
        /// plus a block per instance of it.
        /// </summary>
        public class MetadataSet
        {
            /// <summary>Values that hold for the clip however it is played - its label, length and so on.</summary>
            public MetadataBlock Common = new MetadataBlock();

            /// <summary>One block per use of the clip, carrying that use's own tags.</summary>
            public List<MetadataBlock> Instances = new List<MetadataBlock>();

            public override string ToString() =>
                Common.Arguments.Count + " argument(s), " + Instances.Count + " instance(s)";
        }

        /// <summary>
        /// A bag of named values, plus any properties timing events against the clip.
        /// </summary>
        public class MetadataBlock
        {
            public List<MetadataArgument> Arguments = new List<MetadataArgument>();
            public List<MetadataProperty> Properties = new List<MetadataProperty>();

            public override string ToString() =>
                Arguments.Count + " argument(s), " + Properties.Count + " property/ies";
        }

        /// <summary>
        /// A named event on the clip's timeline - a footstep, say - and the times it fires at.
        /// </summary>
        public class MetadataProperty
        {
            public string Name = "";

            /// <summary>Times through the clip, in seconds, that this property fires at.</summary>
            public List<float> Times = new List<float>();

            public override string ToString() => Name + " x" + Times.Count;
        }

        /// <summary>
        /// One named value on a clip - a footstep sound, whether it can be mirrored, and so on.
        /// </summary>
        public class MetadataArgument
        {
            public string Name = "";
            public MetadataValueType Type;

            /// <summary>A bool, int, float, string or Vector3 depending on <see cref="Type"/>.</summary>
            public object Value;

            public short RequiresConvert;
            public bool CanMirror;
            public bool CanModulateByPlayspeed;

            public override string ToString() => Name + " = " + Value;
        }
        #endregion
    }
}
