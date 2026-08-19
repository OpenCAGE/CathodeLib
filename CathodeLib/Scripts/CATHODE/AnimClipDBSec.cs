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

        /// <summary>
        /// Metadata tags per clip, in clip order. Empty when the file is one of the handful whose
        /// image this parser cannot follow - see <see cref="MetadataParsed"/>.
        /// </summary>
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

        /* The raw metadata image, held only while loading - everything in it is parsed out into
         * Metadata and rebuilt from there on save. */
        private byte[] _metadata;

        /// <summary>
        /// Whether a save will write <see cref="Metadata"/> back out. False for the few files whose
        /// image this does not reproduce byte for byte - those still read, but are saved verbatim.
        /// </summary>
        public bool MetadataParsed { get { return _metadata == null; } }

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
                /* Parse the metadata out, then check we can put it back exactly as we found it.
                 * A handful of retail files lay theirs out in a way this doesn't reproduce; those
                 * keep their image and write it back untouched rather than risk losing data. They
                 * still read normally - it's only saving that ignores edits, which MetadataParsed
                 * reports. Files that don't parse at all get their sets dropped as well. */
                byte[] image = reader.ReadBytes(metadataLength);
                _metadata = image;
                bool parsed = ReadMetadata();
                _metadata = null;

                if (!parsed) Metadata.Clear();
                if (!parsed || !Reproduces(WriteMetadata(), image)) _metadata = image;
                return true;
            }
        }

        /* The DB is a memory image: "MDDB", a count, then that many absolute offsets to metadata
         * sets. A set's header holds absolute offsets to its own arrays. */
        private bool ReadMetadata()
        {
            if (_metadata.Length == 0) return true;
            if (_metadata.Length < 8 || Encoding.ASCII.GetString(_metadata, 0, 4) != "MDDB")
                return false;

            int count = BitConverter.ToInt32(_metadata, 4);
            if (count < 0 || (long)count * 8 + 8 > _metadata.Length) return false;

            for (int i = 0; i < count; i++)
            {
                long offset = BitConverter.ToInt64(_metadata, 8 + (i * 8));
                if (offset <= 0 || offset + SetHeaderSize > _metadata.Length) return false;

                //a set runs up to wherever the next one starts, and the last one to the end
                long limit = i + 1 < count ? BitConverter.ToInt64(_metadata, 8 + ((i + 1) * 8)) : _metadata.Length;
                if (limit <= offset || limit > _metadata.Length) return false;

                MetadataSet set = new MetadataSet();
                Metadata.Add(set);
                if (!ReadMetadataSet(offset, set, limit)) return false;
            }

            //the index is padded out to sixteen before the first set
            long first = count == 0 ? _metadata.Length : BitConverter.ToInt64(_metadata, 8);
            return first == Align(8 + (count * 8), 16);
        }

        private static bool Reproduces(byte[] rebuilt, byte[] original)
        {
            if (rebuilt.Length != original.Length) return false;
            for (int i = 0; i < rebuilt.Length; i++) if (rebuilt[i] != original[i]) return false;
            return true;
        }

        private static long Align(long value, int to)
        {
            long remainder = value % to;
            return remainder == 0 ? value : value + (to - remainder);
        }

        /* A set is a 32 byte header - eight unknown bytes, a pointer to the set body, a pointer to
         * the instance area, then the instance count - with the common block inlined at +32. */
        private bool ReadMetadataSet(long offset, MetadataSet set, long limit)
        {
            set.Unknown = BitConverter.ToInt64(_metadata, (int)offset);
            set.Slack = BitConverter.ToUInt32(_metadata, (int)offset + 28);

            set.Common = ReadBlock(offset + 32, out long end);
            if (set.Common == null) return false;
            MetadataBlock previous = set.Common;

            uint instances = BitConverter.ToUInt32(_metadata, (int)offset + 24);
            if (instances > 512) return false;

            /* Each instance carries its own block. They follow the common one back to back, but
             * with slack between them that no field predicts, so find each by its own signature
             * and remember how far it was pushed along. */
            for (int i = 0; i < instances; i++)
            {
                /* A block can start just inside the slack of the one before it. The signature is
                 * strong but not unique, so keep looking until one parses and fits the set. */
                long at = -1, next = -1;
                MetadataBlock block = null;
                for (long probe = end - 8; probe < limit; probe += 4)
                {
                    if (!IsBlock(probe)) continue;
                    MetadataBlock candidate = ReadBlock(probe, out long candidateEnd);
                    if (candidate == null || candidateEnd > limit + 8) continue;
                    at = probe; next = candidateEnd; block = candidate;
                    break;
                }
                if (block == null) return false;

                //the instance pointer normally names the first block less sixteen, but not always
                if (i == 0)
                    set.InstanceBias = (int)((at - 16) - BitConverter.ToInt64(_metadata, (int)offset + 16));

                previous.TrailingPadding = (int)(at - end);
                end = next;
                set.Instances.Add(block);
                previous = block;
            }

            /* A set's last time can spill into the next set's first field, which is why that field
             * sometimes reads as a stray float - so the padding here is allowed to go slightly negative. */
            previous.TrailingPadding = (int)(limit - end);
            return previous.TrailingPadding >= -8;
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
            if (arguments != at + 56 || arguments + (argumentCount * ArgumentSize) > _metadata.Length) return null;

            MetadataBlock block = new MetadataBlock
            {
                Unknown0 = BitConverter.ToInt64(_metadata, (int)at + 24),
                Unknown1 = BitConverter.ToInt64(_metadata, (int)at + 32),
            };
            for (int i = 0; i < argumentCount; i++)
                block.Arguments.Add(ReadArgument((int)(arguments + (i * ArgumentSize))));
            end = arguments + (argumentCount * ArgumentSize);

            //only the final argument's trailing eight bytes are ever used, and not by the argument
            if (argumentCount != 0)
                block.ArgumentsTrailing = BitConverter.ToUInt64(_metadata, (int)(end - 8));

            block.HasProperties = properties != 0;
            if (properties == 0 || propertyCount == 0) return block;
            properties += 16;
            if (properties != end || properties + (propertyCount * PropertySize) > _metadata.Length) return null;
            end = properties + (propertyCount * PropertySize);

            for (int i = 0; i < propertyCount; i++)
            {
                long entry = properties + (i * PropertySize);
                MetadataProperty property = new MetadataProperty
                {
                    Name = Name(BitConverter.ToUInt32(_metadata, (int)entry)),
                };
                block.Properties.Add(property);

                /* The times pointer names the last event header's type field, so the times
                 * themselves land eight bytes past it - not sixteen like every other array. */
                long times = BitConverter.ToInt64(_metadata, (int)entry + 16) + 8;
                long count = BitConverter.ToUInt32(_metadata, (int)entry + 32);
                if (count > 4096 || times < 0 || times + (count * 4) > _metadata.Length) return null;
                for (int t = 0; t < count; t++)
                    property.Times.Add(BitConverter.ToSingle(_metadata, (int)(times + (t * 4))));
                end = times + (count * 4);

                /* A property may also name what fires at each time - one 32 byte header per
                 * time, with the times overlaying the last header's trailing eight bytes. */
                long headers = BitConverter.ToInt64(_metadata, (int)entry + 8);
                if (headers == 0) continue;
                property.HasEvents = true;
                headers += 16;
                if (headers + (count * EventHeaderSize) > _metadata.Length) return null;
                if (times != headers + ((count - 1) * EventHeaderSize) + 24) return null;
                for (int t = 0; t < count; t++)
                {
                    long header = headers + (t * EventHeaderSize);
                    property.Events.Add(new MetadataEvent
                    {
                        Name = Name(BitConverter.ToUInt32(_metadata, (int)header)),
                        Type = (MetadataValueType)BitConverter.ToUInt32(_metadata, (int)header + 16),
                    });
                }

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
        private const int EventHeaderSize = 32;

        /* Rebuild the memory image. Everything in it is reached by absolute offset, so the layout
         * is ours to choose - we just lay each set out end to end the way the game's allocator did,
         * carrying the slack it left behind so an untouched file writes back unchanged. */
        private byte[] WriteMetadata()
        {
            if (_metadata != null) return _metadata;
            if (Metadata.Count == 0) return new byte[0];

            long start = Align(8 + (Metadata.Count * 8), 16);
            long[] offsets = new long[Metadata.Count];
            long at = start;
            for (int i = 0; i < Metadata.Count; i++)
            {
                offsets[i] = at;
                at = MeasureSet(Metadata[i], at);
            }

            byte[] image = new byte[at];
            Encoding.ASCII.GetBytes("MDDB").CopyTo(image, 0);
            BitConverter.GetBytes(Metadata.Count).CopyTo(image, 4);
            for (int i = 0; i < Metadata.Count; i++)
                BitConverter.GetBytes(offsets[i]).CopyTo(image, 8 + (i * 8));

            for (int i = 0; i < Metadata.Count; i++)
                WriteSet(image, Metadata[i], offsets[i]);
            return image;
        }

        /* Where a set ends if it starts at the given offset. */
        private static long MeasureSet(MetadataSet set, long at)
        {
            long end = MeasureBlock(set.Common, at + 32) + set.Common.TrailingPadding;
            for (int i = 0; i < set.Instances.Count; i++)
                end = MeasureBlock(set.Instances[i], end) + set.Instances[i].TrailingPadding;
            return end;
        }

        /* Where a block's payload ends. The header is 40 bytes, then the sixteen its argument
         * pointer aims at, then the arguments, the property array and each property's data. */
        private static long MeasureBlock(MetadataBlock block, long at)
        {
            long end = at + 56 + (block.Arguments.Count * ArgumentSize);
            if (block.Properties.Count == 0) return end;

            long cursor = end + (block.Properties.Count * PropertySize);
            if (!block.Properties[0].HasEvents) cursor -= 8;
            for (int i = 0; i < block.Properties.Count; i++)
            {
                MetadataProperty property = block.Properties[i];
                int count = property.Times.Count;
                //a property that named its events leaves eight bytes before the next one starts
                if (i != 0 && block.Properties[i - 1].HasEvents) cursor += 8;
                cursor = (property.HasEvents ? TimesOf(cursor, count) : cursor) + (count * 4);
            }
            return cursor;
        }

        /* Where a property's times sit given where its event headers start. An empty array still
         * has a pointer, aimed eight bytes back from where the first header would have been. */
        private static long TimesOf(long headers, int count)
        {
            return count == 0 ? headers - 8 : headers + ((count - 1) * EventHeaderSize) + 24;
        }

        private void WriteSet(byte[] image, MetadataSet set, long at)
        {
            long common = MeasureBlock(set.Common, at + 32);
            long instances = common + set.Common.TrailingPadding;

            BitConverter.GetBytes(set.Unknown).CopyTo(image, (int)at);
            BitConverter.GetBytes(at + 24).CopyTo(image, (int)at + 8);
            BitConverter.GetBytes(set.Instances.Count == 0 ? 0 : instances - 16 - set.InstanceBias).CopyTo(image, (int)at + 16);
            BitConverter.GetBytes(set.Instances.Count).CopyTo(image, (int)at + 24);
            BitConverter.GetBytes(set.Slack).CopyTo(image, (int)at + 28);

            WriteBlock(image, set.Common, at + 32);
            long[] positions = new long[set.Instances.Count];
            bool[] slack = new bool[set.Instances.Count];
            long cursor = instances, previous = common;
            for (int i = 0; i < set.Instances.Count; i++)
            {
                positions[i] = cursor;
                slack[i] = cursor - previous >= 8;
                WriteBlock(image, set.Instances[i], cursor);
                previous = MeasureBlock(set.Instances[i], cursor);
                cursor = previous + set.Instances[i].TrailingPadding;
            }

            /* The instance blocks are threaded together by a link in the eight bytes ahead of each
             * one, naming the next block's link - or itself, on the last. It only appears where the
             * allocator left room for it; otherwise those bytes belong to the block before. */
            for (int i = 0; i < positions.Length; i++)
            {
                if (!slack[i]) continue;
                long link = (i + 1 < positions.Length ? positions[i + 1] : positions[i]) - 8;
                BitConverter.GetBytes(link).CopyTo(image, (int)positions[i] - 8);
            }
        }

        private void WriteBlock(byte[] image, MetadataBlock block, long at)
        {
            long arguments = at + 56;
            long argumentsEnd = arguments + (block.Arguments.Count * ArgumentSize);
            bool properties = block.HasProperties;

            BitConverter.GetBytes(at + 40).CopyTo(image, (int)at);
            BitConverter.GetBytes(properties ? argumentsEnd - 16 : 0).CopyTo(image, (int)at + 8);
            BitConverter.GetBytes(block.Arguments.Count).CopyTo(image, (int)at + 16);
            BitConverter.GetBytes(block.Properties.Count).CopyTo(image, (int)at + 20);
            BitConverter.GetBytes(block.Unknown0).CopyTo(image, (int)at + 24);
            BitConverter.GetBytes(block.Unknown1).CopyTo(image, (int)at + 32);

            for (int i = 0; i < block.Arguments.Count; i++)
                WriteArgument(image, block.Arguments[i], arguments + (i * ArgumentSize));
            if (block.Arguments.Count != 0)
                BitConverter.GetBytes(block.ArgumentsTrailing).CopyTo(image, (int)argumentsEnd - 8);
            if (!properties || block.Properties.Count == 0) return;

            long cursor = argumentsEnd + (block.Properties.Count * PropertySize);
            if (!block.Properties[0].HasEvents) cursor -= 8;

            for (int i = 0; i < block.Properties.Count; i++)
            {
                MetadataProperty property = block.Properties[i];
                long entry = argumentsEnd + (i * PropertySize);
                int count = property.Times.Count;
                BitConverter.GetBytes(_strings.GetID(property.Name)).CopyTo(image, (int)entry);
                BitConverter.GetBytes(count).CopyTo(image, (int)entry + 32);

                //a property that named its events leaves eight bytes before the next one starts
                if (i != 0 && block.Properties[i - 1].HasEvents) cursor += 8;

                long times = cursor;
                if (property.HasEvents)
                {
                    long headers = cursor;
                    for (int t = 0; t < count && t < property.Events.Count; t++)
                    {
                        long header = headers + (t * EventHeaderSize);
                        BitConverter.GetBytes(_strings.GetID(property.Events[t].Name)).CopyTo(image, (int)header);
                        BitConverter.GetBytes((uint)property.Events[t].Type).CopyTo(image, (int)header + 16);
                        BitConverter.GetBytes(property.Events[t].Type == MetadataValueType.PROPERTY_REFERENCE ? 1u : 0u).CopyTo(image, (int)header + 20);
                    }
                    BitConverter.GetBytes(headers - 16).CopyTo(image, (int)entry + 8);
                    times = TimesOf(headers, count);
                }
                cursor = times + (count * 4);

                BitConverter.GetBytes(times - 8).CopyTo(image, (int)entry + 16);
                for (int t = 0; t < count; t++)
                    BitConverter.GetBytes(property.Times[t]).CopyTo(image, (int)(times + (t * 4)));
            }
        }

        private void WriteArgument(byte[] image, MetadataArgument argument, long at)
        {
            BitConverter.GetBytes(_strings.GetID(argument.Name)).CopyTo(image, (int)at);
            BitConverter.GetBytes((uint)argument.Type).CopyTo(image, (int)at + 32);
            BitConverter.GetBytes(argument.RequiresConvert).CopyTo(image, (int)at + 36);
            image[at + 38] = (byte)(argument.CanMirror ? 1 : 0);
            image[at + 39] = (byte)(argument.CanModulateByPlayspeed ? 1 : 0);

            long value = at + 16;
            switch (argument.Type)
            {
                case MetadataValueType.BOOL:
                    BitConverter.GetBytes(argument.Value is bool b && b ? uint.MaxValue : 0u).CopyTo(image, (int)value);
                    break;
                case MetadataValueType.UINT32:
                    BitConverter.GetBytes(Convert.ToUInt32(argument.Value ?? 0u)).CopyTo(image, (int)value);
                    break;
                case MetadataValueType.INT32:
                    BitConverter.GetBytes(Convert.ToInt32(argument.Value ?? 0)).CopyTo(image, (int)value);
                    break;
                case MetadataValueType.FLOAT32:
                    BitConverter.GetBytes(Convert.ToSingle(argument.Value ?? 0f)).CopyTo(image, (int)value);
                    break;
                case MetadataValueType.STRING:
                case MetadataValueType.AUDIO:
                case MetadataValueType.PROPERTY_REFERENCE:
                    string text = argument.Value as string;
                    BitConverter.GetBytes(string.IsNullOrEmpty(text) ? uint.MaxValue : _strings.GetID(text)).CopyTo(image, (int)value);
                    break;
                case MetadataValueType.VECTOR:
                    System.Numerics.Vector3 vector = argument.Value is System.Numerics.Vector3 v ? v : default;
                    BitConverter.GetBytes(vector.X).CopyTo(image, (int)value);
                    BitConverter.GetBytes(vector.Y).CopyTo(image, (int)value + 4);
                    BitConverter.GetBytes(vector.Z).CopyTo(image, (int)value + 8);
                    break;
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
                writer.Write(SkeletonDependencies.Count);
                for (int i = 0; i < SkeletonDependencies.Count; i++)
                    writer.Write(_strings.GetID(SkeletonDependencies[i]));

                writer.Write(_havok.Length);
                writer.Write(_havok);

                byte[] metadata = WriteMetadata();
                writer.Write(metadata.Length);
                writer.Write(metadata);

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
            /* Walking the packfile costs the same whether one clip is wanted or all of them, and the
             * big resident sections hold nearly six hundred - so hold on to the result. */
            if (_animations != null) return _animations;
            return _animations = Havok == null ? new List<HavokPackfile.AnimationClip>() : Havok.GetAnimations();
        }
        private List<HavokPackfile.AnimationClip> _animations;
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

            /// <summary>Engine scratch, kept so an untouched file writes back unchanged.</summary>
            public long Unknown;

            /// <summary>Engine scratch, kept so an untouched file writes back unchanged.</summary>
            public uint Slack;

            /// <summary>How far the instance pointer sits from the first instance block, normally zero.</summary>
            public int InstanceBias;

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

            /// <summary>Engine scratch, kept so an untouched file writes back unchanged.</summary>
            public long Unknown0, Unknown1;

            /// <summary>Whether the block points at a property array, which may still be empty.</summary>
            public bool HasProperties;

            /// <summary>Engine scratch sitting past the last argument, kept for the same reason.</summary>
            public ulong ArgumentsTrailing;

            /// <summary>
            /// Bytes the game's allocator left after this block. Nothing in the format predicts it
            /// and nothing reads it - every reference is an absolute offset - but carrying it makes
            /// a save byte for byte identical to the original.
            /// </summary>
            public int TrailingPadding;

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

            /// <summary>
            /// What fires at each time, one per entry in <see cref="Times"/>. Empty when the
            /// property is a bare marker - foot strikes name nothing, audio properties do.
            /// </summary>
            public List<MetadataEvent> Events = new List<MetadataEvent>();

            /// <summary>Whether the property carries an event array at all - it may be empty.</summary>
            public bool HasEvents;

            public override string ToString() => Name + " x" + Times.Count;
        }

        /// <summary>
        /// One occurrence of a property - names an argument of the same block, normally the audio
        /// event to play at the matching entry in <see cref="MetadataProperty.Times"/>.
        /// </summary>
        public class MetadataEvent
        {
            public string Name = "";

            /// <summary>
            /// Almost always PROPERTY_REFERENCE, meaning <see cref="Name"/> points at an argument
            /// of the same block. The other types carry a raw value in that field instead.
            /// </summary>
            public MetadataValueType Type = MetadataValueType.PROPERTY_REFERENCE;

            public override string ToString() => Name;
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
