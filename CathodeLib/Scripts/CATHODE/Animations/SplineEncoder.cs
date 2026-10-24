
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace CATHODE
{
    /// <summary>
    /// Writes a <c>hkaSplineCompressedAnimation</c>: hand it a pose per frame per track and it
    /// produces the packfile CATHODE stores a clip in.
    ///
    /// Every layout choice here is the inverse of <see cref="HavokPackfile"/>.s reader, so the reader
    /// is the check on it. Degree one splines with a control point on every frame reproduce what was
    /// sampled exactly; translations and scales are quantised to two bytes and rotations to
    /// THREECOMP40, which is what retail uses throughout.
    /// </summary>
    public class SplineEncoder
    {
        public const int FramesPerBlock = 256;

        //two byte control points and THREECOMP40 rotations, which is what retail overwhelmingly uses
        const int ScalarWidth = 2;
        const byte QuantizationByte = 0x45;   //translation 2 bytes, rotation THREECOMP40, scale 2 bytes
        const int RotationWidth = 5;

        public string SkeletonName = "";

        /// <summary>Layer this clip over whatever else is playing rather than replacing it.</summary>
        public bool Additive = false;
        public List<short> TrackToBone = new List<short>();
        public float FrameDuration = 1f / 30f;

        /// <summary>[frame][track] - every frame must carry a pose for every track.</summary>
        public List<List<HavokPackfile.SampledTransform>> Frames = new List<List<HavokPackfile.SampledTransform>>();

        public int FrameCount { get { return Frames.Count; } }
        public int TrackCount { get { return TrackToBone.Count; } }

        #region STREAM
        /// <summary>
        /// The compressed stream, plus the two offset tables that index into it.
        /// </summary>
        public byte[] BuildStream(out List<uint> blockOffsets, out List<uint> floatBlockOffsets)
        {
            blockOffsets = new List<uint>();
            floatBlockOffsets = new List<uint>();

            MemoryStream output = new MemoryStream();
            int blocks = Math.Max(1, (FrameCount + FramesPerBlock - 1) / FramesPerBlock);
            for (int block = 0; block < blocks; block++)
            {
                //whatever follows a block starts on a sixteen byte boundary
                Align(output, 16);
                blockOffsets.Add((uint)output.Length);

                int first = block * FramesPerBlock;
                int count = Math.Min(FramesPerBlock, FrameCount - first);

                //the masks come first, one four byte group per transform track, filled in as we go
                MemoryStream stream = new MemoryStream();
                byte[] masks = new byte[TrackCount * 4];
                stream.Write(masks, 0, masks.Length);

                for (int track = 0; track < TrackCount; track++)
                {
                    Align(stream, 4);
                    masks[(track * 4) + 1] = WriteVector(stream, track, first, count, Channel.Translation);
                    Align(stream, 4);
                    masks[(track * 4) + 2] = WriteRotation(stream, track, first, count);
                    Align(stream, 4);
                    masks[(track * 4) + 3] = WriteVector(stream, track, first, count, Channel.Scale);
                    Align(stream, 4);
                    masks[(track * 4) + 0] = QuantizationByte;
                }

                byte[] body = stream.ToArray();
                masks.CopyTo(body, 0);

                //the transform data ends here; float tracks would start at this offset
                floatBlockOffsets.Add((uint)body.Length);
                output.Write(body, 0, body.Length);
            }

            Align(output, 16);
            return output.ToArray();
        }

        public int MaskAndQuantizationSize { get { return (TrackCount * 4) + 0; } }

        enum Channel { Translation, Scale }

        /* A vector channel: a curve for the components that move, a plain float for the rest. */
        byte WriteVector(MemoryStream stream, int track, int first, int count, Channel channel)
        {
            Vector3[] values = new Vector3[count];
            bool carried = false;
            for (int i = 0; i < count; i++)
            {
                HavokPackfile.SampledTransform pose = Frames[first + i][track];
                values[i] = channel == Channel.Translation ? pose.Translation : pose.Scale;
                carried |= channel == Channel.Translation ? pose.HasTranslation : pose.HasScale;
            }
            if (!carried) return 0;

            int splined = 0, statics = 0;
            float[] minimum = new float[3], maximum = new float[3];
            for (int c = 0; c < 3; c++)
            {
                float low = float.MaxValue, high = float.MinValue;
                for (int i = 0; i < count; i++)
                {
                    float value = Component(values[i], c);
                    low = Math.Min(low, value);
                    high = Math.Max(high, value);
                }
                minimum[c] = low;
                maximum[c] = high;
                if (high - low <= 1e-7f) statics |= 1 << c; else splined |= 1 << c;
            }

            int lanes = Lanes(splined);
            int items = count - 1;
            if (lanes != 0) WriteNurbs(stream, items);
            Align(stream, 4);

            //floats first in X Y Z order - a range for a curved component, the value for a held one
            for (int c = 0; c < 3; c++)
            {
                if ((splined & (1 << c)) != 0) { Write(stream, minimum[c]); Write(stream, maximum[c]); }
                else if ((statics & (1 << c)) != 0) Write(stream, minimum[c]);
            }

            //then the control points, a point at a time rather than an axis at a time
            for (int i = 0; i <= items && lanes != 0; i++)
                for (int c = 0; c < 3; c++)
                {
                    if ((splined & (1 << c)) == 0) continue;
                    float value = Component(values[Math.Min(i, count - 1)], c);
                    WriteQuantized(stream, value, minimum[c], maximum[c]);
                }

            return (byte)((splined << 4) | statics);
        }

        /* A rotation is stored whole rather than per component, so it is one value or one curve. */
        byte WriteRotation(MemoryStream stream, int track, int first, int count)
        {
            Quaternion[] values = new Quaternion[count];
            bool carried = false;
            for (int i = 0; i < count; i++)
            {
                HavokPackfile.SampledTransform pose = Frames[first + i][track];
                values[i] = pose.Rotation;
                carried |= pose.HasRotation;
            }
            if (!carried) return 0;

            bool moves = false;
            for (int i = 1; i < count; i++)
                if (Math.Abs(Quaternion.Dot(values[0], values[i])) < 0.9999999f) { moves = true; break; }

            if (!moves)
            {
                WritePacked(stream, values[0]);
                return 0x0F;
            }

            int items = count - 1;
            WriteNurbs(stream, items);

            /* Line each control point up with the one before it, the same way the sampler does -
             * a quaternion and its negative are the same rotation, and a curve that flips between
             * them takes the long way round. */
            Quaternion previous = values[0];
            for (int i = 0; i <= items; i++)
            {
                Quaternion value = values[Math.Min(i, count - 1)];
                if (Quaternion.Dot(previous, value) < 0) value = new Quaternion(-value.X, -value.Y, -value.Z, -value.W);
                WritePacked(stream, value);
                previous = value;
            }
            return 0xF0;
        }

        /* uint16 item count, byte degree, then count + degree + 2 knots. Degree one puts a control
         * point on every frame, so the curve passes exactly through what was sampled. */
        void WriteNurbs(MemoryStream stream, int items)
        {
            stream.WriteByte((byte)(items & 0xFF));
            stream.WriteByte((byte)((items >> 8) & 0xFF));
            stream.WriteByte(1);

            //clamped: 0, 0, 1, 2, ... items-1, items-1
            stream.WriteByte(0);
            for (int i = 0; i <= items; i++) stream.WriteByte((byte)Math.Min(255, i));
            stream.WriteByte((byte)Math.Min(255, items));
        }

        static int Lanes(int mask)
        {
            int lanes = 0;
            for (int c = 0; c < 3; c++) if ((mask & (1 << c)) != 0) lanes++;
            return lanes;
        }

        static float Component(Vector3 value, int c) { return c == 0 ? value.X : c == 1 ? value.Y : value.Z; }

        static void WriteQuantized(MemoryStream stream, float value, float minimum, float maximum)
        {
            float span = maximum - minimum;
            int raw = span <= 0 ? 0 : (int)Math.Round((value - minimum) / span * 65535.0);
            raw = Math.Max(0, Math.Min(65535, raw));
            stream.WriteByte((byte)(raw & 0xFF));
            stream.WriteByte((byte)((raw >> 8) & 0xFF));
        }

        /* THREECOMP40: the three smallest components at twelve bits each, two bits naming the one
         * left out and a bit for its sign. */
        static void WritePacked(MemoryStream stream, Quaternion value)
        {
            float[] q = { value.X, value.Y, value.Z, value.W };
            int missing = 0;
            for (int i = 1; i < 4; i++) if (Math.Abs(q[i]) > Math.Abs(q[missing])) missing = i;

            const double range = 1.4142135623730951;
            const double offset = -0.7071067811865476;

            ulong packed = 0;
            int next = 0;
            for (int i = 0; i < 4; i++)
            {
                if (i == missing) continue;
                int raw = (int)Math.Round((q[i] - offset) / (range / 4095.0));
                raw = Math.Max(0, Math.Min(4095, raw));
                packed |= (ulong)(uint)raw << (12 * next);
                next++;
            }
            packed |= (ulong)(uint)missing << 36;
            if (q[missing] < 0) packed |= 1UL << 38;

            for (int i = 0; i < RotationWidth; i++) stream.WriteByte((byte)((packed >> (8 * i)) & 0xFF));
        }

        static void Write(MemoryStream stream, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        static void Align(MemoryStream stream, int to)
        {
            while (stream.Length % to != 0) stream.WriteByte(0);
        }

        static void Pad(MemoryStream stream, int to) { Align(stream, to); }
        #endregion

        #region PACKFILE
        /// <summary>
        /// Lay the animation out into <paramref name="target"/>, which supplies the packfile header
        /// and class name table - both are the same for every animation section in the game, so any
        /// of them will do, and its pointer size decides whether a 32 or 64 bit copy comes out.
        ///
        /// A tagfile template (the mobile and Switch builds) keeps its type table and gets a new
        /// object graph written against it - see <see cref="CanBuildInto"/> for which will do.
        /// </summary>
        public void BuildInto(HavokPackfile target)
        {
            /* The mobile and Switch builds hold the same stream in a Havok 2018 tagfile, whose object
             * graph shares nothing with a 2012 packfile's but the bytes of the stream itself. */
            if (target.IsTagfile)
            {
                BuildIntoTagfile(target);
                return;
            }

            /* The 32 and 64 bit copies of a section hold the same stream; only the object graph
             * around it changes size. Follow whatever the template we were handed is. */
            int pointer = target.Header.PointerSize == 8 ? 8 : 4;
            int header = pointer == 8 ? 16 : 8;
            int array = pointer + 8;

            Layout layout = new Layout();
            int container = layout.Object(header + (array * 5));
            int bindingList = layout.Object(pointer);                //the container's one binding
            Objects objects = Reserve(layout, pointer, header, array);

            byte[] payload = new byte[layout.Length];
            List<HavokPackfile.LocalFixup> local = new List<HavokPackfile.LocalFixup>();
            List<HavokPackfile.GlobalFixup> global = new List<HavokPackfile.GlobalFixup>();

            //hkaAnimationContainer: skeletons, animations, bindings, attachments, skins
            EmptyArray(payload, container + header, pointer);
            EmptyArray(payload, container + header + array, pointer);
            Array(payload, container + header + (array * 2), bindingList, 1, local, pointer);
            EmptyArray(payload, container + header + (array * 3), pointer);
            EmptyArray(payload, container + header + (array * 4), pointer);
            global.Add(new HavokPackfile.GlobalFixup { Src = (uint)bindingList, DstSectionIndex = 2, Dst = (uint)objects.Binding });

            Emit(payload, objects, local, global, pointer, header, array);

            target.DataPayload = payload;
            target.LocalFixups = local;
            target.GlobalFixups = global;
            /* A virtual fixup is what names an object's class, so each one has to keep the class name
             * offset the template used and move to where we put that object. */
            target.VirtualFixups = new List<HavokPackfile.VirtualFixup>
            {
                NameFixup(target, "hkaAnimationContainer", container),
                NameFixup(target, "hkaAnimationBinding", objects.Binding),
                NameFixup(target, "hkaSplineCompressedAnimation", objects.Animation),
            };
            target.Objects = new List<HavokPackfile.PackfileObject>
            {
                new HavokPackfile.PackfileObject { DataOffset = (uint)container, ClassName = "hkaAnimationContainer" },
                new HavokPackfile.PackfileObject { DataOffset = (uint)objects.Binding, ClassName = "hkaAnimationBinding" },
                new HavokPackfile.PackfileObject { DataOffset = (uint)objects.Animation, ClassName = "hkaSplineCompressedAnimation" },
            };
        }

        /* Where the animation.s objects sit once reserved. Laid out and written in two passes because
           the payload cannot be allocated until every object has claimed its space. */
        private sealed class Objects
        {
            public int Binding, SkeletonName, TrackToBone, Animation, Annotation, AnnotationName, BlockTable, FloatTable, Data;
            public byte[] Stream;
            public List<uint> BlockOffsets = new List<uint>();
            public List<uint> FloatBlockOffsets = new List<uint>();
            public int Blocks { get { return BlockOffsets.Count; } }
        }

        /// <summary>Claim space for the animation in a layout that may already hold other objects.</summary>
        private Objects Reserve(Layout layout, int pointer, int header, int array)
        {
            Objects objects = new Objects();
            objects.Stream = BuildStream(out List<uint> blockOffsets, out List<uint> floatBlockOffsets);
            objects.BlockOffsets = blockOffsets;
            objects.FloatBlockOffsets = floatBlockOffsets;

            objects.Binding = layout.Object(header + (pointer * 2) + (array * 3) + 8);
            objects.SkeletonName = layout.Object(SkeletonName.Length + 1);
            objects.TrackToBone = layout.Object(Math.Max(1, TrackCount * 2));
            objects.Animation = layout.Object(header + 16 + pointer + array + 32 + (array * 5) + 8);
            objects.Annotation = layout.Object(pointer + array);
            objects.AnnotationName = layout.Object(1);
            objects.BlockTable = layout.Object(objects.Blocks * 4);
            objects.FloatTable = layout.Object(objects.Blocks * 4);
            objects.Data = layout.Object(objects.Stream.Length);
            return objects;
        }

        /// <summary>Write the reserved animation objects, and the fixups that tie them together.</summary>
        private void Emit(byte[] payload, Objects objects, List<HavokPackfile.LocalFixup> local,
                           List<HavokPackfile.GlobalFixup> global, int pointer, int header, int array)
        {
            int binding = objects.Binding, animation = objects.Animation, blocks = objects.Blocks;

            //hkaAnimationBinding: skeleton name, the animation, then three arrays and the blend hint
            local.Add(new HavokPackfile.LocalFixup { Src = (uint)(binding + header), Dst = (uint)objects.SkeletonName });
            global.Add(new HavokPackfile.GlobalFixup { Src = (uint)(binding + header + pointer), DstSectionIndex = 2, Dst = (uint)animation });
            int bindingArrays = binding + header + (pointer * 2);
            Array(payload, bindingArrays, objects.TrackToBone, TrackCount, local, pointer);
            EmptyArray(payload, bindingArrays + array, pointer);
            EmptyArray(payload, bindingArrays + (array * 2), pointer);
            payload[bindingArrays + (array * 3)] = (byte)(Additive ? 1 : 0);   //the blend hint

            Encoding.ASCII.GetBytes(SkeletonName).CopyTo(payload, objects.SkeletonName);
            for (int i = 0; i < TrackCount; i++)
                BitConverter.GetBytes(TrackToBone[i]).CopyTo(payload, objects.TrackToBone + (i * 2));

            //hkaSplineCompressedAnimation - the hkaAnimation base first
            float duration = FrameCount > 1 ? (FrameCount - 1) * FrameDuration : FrameDuration;
            int animationBase = animation + header;
            Int(payload, animationBase + 0, 3);               //SPLINE_COMPRESSED
            Float(payload, animationBase + 4, duration);
            Int(payload, animationBase + 8, TrackCount);
            Int(payload, animationBase + 12, 0);              //no float tracks
            Int(payload, animationBase + 16, 0);              //no extracted motion
            Array(payload, animationBase + 16 + pointer, objects.Annotation, 1, local, pointer);

            int spline = animationBase + 16 + pointer + array;
            Int(payload, spline + 0, FrameCount);
            Int(payload, spline + 4, blocks);
            Int(payload, spline + 8, FramesPerBlock);
            Int(payload, spline + 12, MaskAndQuantizationSize);
            Float(payload, spline + 16, FramesPerBlock * FrameDuration);
            Float(payload, spline + 20, 1f / (FramesPerBlock * FrameDuration));
            Float(payload, spline + 24, FrameDuration);

            //on a 64 bit packfile the five arrays are pushed out to an eight byte boundary
            int arrays = spline + 28;
            if (pointer == 8) arrays = (arrays + 7) & ~7;
            Array(payload, arrays, objects.BlockTable, blocks, local, pointer);
            Array(payload, arrays + array, objects.FloatTable, blocks, local, pointer);
            EmptyArray(payload, arrays + (array * 2), pointer);   //transform offsets
            EmptyArray(payload, arrays + (array * 3), pointer);   //float offsets
            Array(payload, arrays + (array * 4), objects.Data, objects.Stream.Length, local, pointer);
            Int(payload, arrays + (array * 5), 0);                //little endian

            //one empty annotation track, which is what every shipped clip carries
            local.Add(new HavokPackfile.LocalFixup { Src = (uint)objects.Annotation, Dst = (uint)objects.AnnotationName });
            EmptyArray(payload, objects.Annotation + pointer, pointer);

            for (int i = 0; i < blocks; i++)
            {
                Int(payload, objects.BlockTable + (i * 4), (int)objects.BlockOffsets[i]);
                Int(payload, objects.FloatTable + (i * 4), (int)objects.FloatBlockOffsets[i]);
            }
            objects.Stream.CopyTo(payload, objects.Data);
        }

        private static HavokPackfile.VirtualFixup NameFixup(HavokPackfile template, string className, int at)
        {
            HavokPackfile.PackfileObject original = template.Objects.FirstOrDefault(x => x.ClassName == className);
            if (original == null) throw new Exception("template has no " + className);
            foreach (HavokPackfile.VirtualFixup fixup in template.VirtualFixups)
                if (fixup.Src == original.DataOffset)
                    return new HavokPackfile.VirtualFixup { Src = (uint)at, SectionIndex = fixup.SectionIndex, NameOffset = fixup.NameOffset };
            throw new Exception("template names no class for " + className);
        }

        /* Objects sit on sixteen byte boundaries, which is what the reader's block accounting expects */
        private class Layout
        {
            public int Length;
            public int Object(int size)
            {
                int at = Length;
                Length = (at + Math.Max(size, 1) + 15) & ~15;
                return at;
            }
        }

        private static void Int(byte[] payload, int at, int value) { BitConverter.GetBytes(value).CopyTo(payload, at); }
        private static void Float(byte[] payload, int at, float value) { BitConverter.GetBytes(value).CopyTo(payload, at); }

        /* An hkArray is a pointer, then its size and capacity - the top bit of the capacity is the
         * flag saying the memory isn't the array's to free. */
        private static void EmptyArray(byte[] payload, int at, int pointer)
        {
            Int(payload, at + pointer + 4, unchecked((int)0x80000000));
        }

        private static void Array(byte[] payload, int at, int data, int count, List<HavokPackfile.LocalFixup> local, int pointer)
        {
            local.Add(new HavokPackfile.LocalFixup { Src = (uint)at, Dst = (uint)data });
            Int(payload, at + pointer, count);
            Int(payload, at + pointer + 4, unchecked((int)0x80000000) | count);
        }
        #endregion

        #region TAGFILE
        /* A Havok 2018 tagfile describes its own schema, so nothing here is laid out by hand: every
         * size, offset, alignment and type index comes out of the template's type table, and only the
         * three chunks describing the data - DATA, ITEM and PTCH - are written new. The rules are the
         * ones Havok's own writer follows, measured over all 11,657 iOS sections and proven by
         * re-emitting 9,739 of 9,739 retail one-clip sections byte for byte:
         *   - item 0 is null; then container, bindings[], binding, animation, trackToBone[],
         *     floatSlots[] (if any), skeleton name, annotationTracks[], blockOffsets[],
         *     floatBlockOffsets[], data[], and the one track name every annotation track shares;
         *   - the data runs container, bindings[], binding, the binding's arrays, the name, the
         *     animation, its arrays, the track name;
         *   - objects sit at their type's alignment, arrays at 16, strings at 2, and DATA is padded
         *     to 16;
         *   - a pointer is a u64 item index plus a PTCH entry under the member's declared type, and
         *     an hkArray's m_size and m_capacityAndFlags stay 0 - the count lives on the item.
         * The iOS and Switch animation PAKs are the same file, so one writer covers both. */

        private const string ContainerType = "hkaAnimationContainer";
        private const string BindingType = "hkaAnimationBinding";
        private const string AnimationType = "hkaAnimation";
        private const string SplineType = "hkaSplineCompressedAnimation";
        private const string TrackType = "hkaAnnotationTrack";

        //a tagfile pointer is an eight byte item index whatever the platform
        private const int TagfilePointer = 8;

        /// <summary>
        /// Whether <paramref name="target"/> can have a clip laid out into it. Any packfile will do. A
        /// tagfile needs the spline class in its type table, which 39 of the iOS streamed sections -
        /// the interleaved-only ones - were written without.
        /// </summary>
        internal static bool CanBuildInto(HavokPackfile target)
        {
            if (target == null || !target.Loaded) return false;
            return !target.IsTagfile || MissingTagfileType(target.Tagfile) == null;
        }

        /* The first type the writer needs that the table does not describe, or null if none is missing */
        private static string MissingTagfileType(HavokTagfile tags)
        {
            foreach (string type in new[] { ContainerType, BindingType, AnimationType, SplineType, TrackType })
                if (tags.SizeOf(type) <= 0) return type;
            return tags.TypeIndex("char") <= 0 ? "char" : null;
        }

        /// <summary>
        /// Everything a one-clip tagfile section says about its clip, the stream included. The
        /// encoder fills one in from its frames; the layout test fills one in from a retail section,
        /// which is how the writer is checked against Havok's own output.
        /// </summary>
        internal sealed class TagfileClip
        {
            public string SkeletonName = "";
            public short[] TrackToBone = new short[0];
            public short[] FloatSlots = new short[0];
            public byte BlendHint;

            public int Type = 3;        //SPLINE_COMPRESSED
            public float Duration;
            public int TransformTracks, FloatTracks;

            public int FrameCount, BlockCount, MaxFramesPerBlock, MaskAndQuantizationSize, Endian;
            public float BlockDuration, BlockInverseDuration, FrameDuration;
            public uint[] BlockOffsets = new uint[0];
            public uint[] FloatBlockOffsets = new uint[0];
            public byte[] Stream = new byte[0];

            /// <summary>Retail carries one per transform track, every one of them empty.</summary>
            public int AnnotationTracks;
        }

        /* The encoder's header on a tagfile is the packfile's, value for value - the sampler reads
         * both the same way - but with an annotation track per transform track, as all 14,975 retail
         * clips carry. */
        private void BuildIntoTagfile(HavokPackfile target)
        {
            byte[] stream = BuildStream(out List<uint> blockOffsets, out List<uint> floatBlockOffsets);
            WriteTagfile(target, new TagfileClip
            {
                SkeletonName = SkeletonName ?? "",
                TrackToBone = TrackToBone.ToArray(),
                BlendHint = (byte)(Additive ? 1 : 0),
                Duration = FrameCount > 1 ? (FrameCount - 1) * FrameDuration : FrameDuration,
                TransformTracks = TrackCount,
                FrameCount = FrameCount,
                BlockCount = blockOffsets.Count,
                MaxFramesPerBlock = FramesPerBlock,
                MaskAndQuantizationSize = MaskAndQuantizationSize,
                BlockDuration = FramesPerBlock * FrameDuration,
                BlockInverseDuration = 1f / (FramesPerBlock * FrameDuration),
                FrameDuration = FrameDuration,
                BlockOffsets = blockOffsets.ToArray(),
                FloatBlockOffsets = floatBlockOffsets.ToArray(),
                Stream = stream,
                AnnotationTracks = TrackCount,
            });
        }

        /// <summary>
        /// Replace <paramref name="target"/>'s whole object graph with one clip, keeping its schema.
        /// Refuses a template whose type table lacks a class the clip needs, before touching it.
        /// </summary>
        internal static void WriteTagfile(HavokPackfile target, TagfileClip clip)
        {
            HavokTagfile tags = target?.Tagfile;
            if (tags == null) throw new InvalidOperationException("The template section is not a Havok tagfile.");

            string missing = MissingTagfileType(tags);
            if (missing != null)
                throw new InvalidOperationException("The template section's Havok type table does not describe " + missing +
                    ", so a clip cannot be written against it. Use a streamed section holding a spline compressed clip as the template.");

            //hkaAnimationContainer, and the binding its one bindings[] entry names
            int bindings = tags.OffsetOf(ContainerType, "bindings");
            int bindingElement = tags.ElementTypeOf(ContainerType, "bindings");
            int skeletonName = tags.OffsetOf(BindingType, "originalSkeletonName");
            int animationField = tags.OffsetOf(BindingType, "animation");
            int trackToBoneField = tags.OffsetOf(BindingType, "transformTrackToBoneIndices");
            int floatSlotsField = tags.OffsetOf(BindingType, "floatTrackToFloatSlotIndices");
            int blendHint = tags.OffsetOf(BindingType, "blendHint");

            //hkaAnimation, then what the spline class adds to it
            int type = tags.OffsetOf(AnimationType, "type");
            int duration = tags.OffsetOf(AnimationType, "duration");
            int transformTracks = tags.OffsetOf(AnimationType, "numberOfTransformTracks");
            int floatTracks = tags.OffsetOf(AnimationType, "numberOfFloatTracks");
            int annotationsField = tags.OffsetOf(AnimationType, "annotationTracks");
            int numFrames = tags.OffsetOf(SplineType, "numFrames");
            int numBlocks = tags.OffsetOf(SplineType, "numBlocks");
            int maxFrames = tags.OffsetOf(SplineType, "maxFramesPerBlock");
            int maskSize = tags.OffsetOf(SplineType, "maskAndQuantizationSize");
            int blockDuration = tags.OffsetOf(SplineType, "blockDuration");
            int blockInverse = tags.OffsetOf(SplineType, "blockInverseDuration");
            int frameDuration = tags.OffsetOf(SplineType, "frameDuration");
            int blocksField = tags.OffsetOf(SplineType, "blockOffsets");
            int floatBlocksField = tags.OffsetOf(SplineType, "floatBlockOffsets");
            int streamField = tags.OffsetOf(SplineType, "data");
            int endian = tags.OffsetOf(SplineType, "endian");
            int trackName = tags.OffsetOf(TrackType, "trackName");
            int trackSize = tags.SizeOf(TrackType);

            int[] required = { bindings, bindingElement, skeletonName, animationField, trackToBoneField, floatSlotsField, blendHint,
                               type, duration, transformTracks, floatTracks, annotationsField, numFrames, numBlocks, maxFrames,
                               maskSize, blockDuration, blockInverse, frameDuration, blocksField, floatBlocksField, streamField,
                               endian, trackName };
            if (required.Any(x => x < 0))
                throw new InvalidOperationException("The template section's Havok type table is missing a member the animation classes need.");

            short[] trackToBone = clip.TrackToBone ?? new short[0];
            short[] floatSlots = clip.FloatSlots ?? new short[0];
            uint[] blockTable = clip.BlockOffsets ?? new uint[0];
            uint[] floatTable = clip.FloatBlockOffsets ?? new uint[0];
            byte[] stream = clip.Stream ?? new byte[0];
            byte[] name = Encoding.ASCII.GetBytes((clip.SkeletonName ?? "") + "\0");
            int annotationCount = Math.Max(0, clip.AnnotationTracks);

            /* Claim the data in the order Havok writes it. An empty array claims nothing and gets no
             * item: its word stays null, which is how retail writes one. */
            int length = 0;
            int Place(int align, int size)
            {
                align = Math.Max(1, align);
                int at = (length + align - 1) / align * align;
                length = at + size;
                return at;
            }
            int container = Place(tags.AlignOf(ContainerType), tags.SizeOf(ContainerType));
            int bindingList = Place(16, TagfilePointer);
            int binding = Place(tags.AlignOf(BindingType), tags.SizeOf(BindingType));
            int trackToBoneAt = trackToBone.Length != 0 ? Place(16, 2 * trackToBone.Length) : -1;
            int floatSlotsAt = floatSlots.Length != 0 ? Place(16, 2 * floatSlots.Length) : -1;
            int nameAt = Place(2, name.Length);
            int animation = Place(tags.AlignOf(SplineType), tags.SizeOf(SplineType));
            int annotationsAt = annotationCount != 0 ? Place(16, trackSize * annotationCount) : -1;
            int blockTableAt = blockTable.Length != 0 ? Place(16, 4 * blockTable.Length) : -1;
            int floatTableAt = floatTable.Length != 0 ? Place(16, 4 * floatTable.Length) : -1;
            int streamAt = stream.Length != 0 ? Place(16, stream.Length) : -1;
            int trackNameAt = annotationCount != 0 ? Place(2, 1) : -1;
            byte[] data = new byte[(length + 15) & ~15];

            /* From here the template's graph is gone. The payload goes on first, because it is the
             * live copy the item index writes land in. */
            tags.ResetIndex();
            target.DataPayload = data;

            int Items(uint word, int at, int count) { return count != 0 ? tags.AddItem(word, at, count) : 0; }
            tags.AddItem(tags.ObjectWord(ContainerType), container, 1);     //the root, which nothing points at
            int bindingListItem = tags.AddItem(tags.ArrayWord(ContainerType, "bindings"), bindingList, 1);
            int bindingItem = tags.AddItem(tags.ObjectWord(BindingType), binding, 1);
            int animationItem = tags.AddItem(tags.ObjectWord(SplineType), animation, 1);
            int trackToBoneItem = Items(tags.ArrayWord(BindingType, "transformTrackToBoneIndices"), trackToBoneAt, trackToBone.Length);
            int floatSlotsItem = Items(tags.ArrayWord(BindingType, "floatTrackToFloatSlotIndices"), floatSlotsAt, floatSlots.Length);
            int nameItem = tags.AddItem(tags.StringWord(), nameAt, name.Length);
            int annotationsItem = Items(tags.ArrayWord(AnimationType, "annotationTracks"), annotationsAt, annotationCount);
            int blockTableItem = Items(tags.ArrayWord(SplineType, "blockOffsets"), blockTableAt, blockTable.Length);
            int floatTableItem = Items(tags.ArrayWord(SplineType, "floatBlockOffsets"), floatTableAt, floatTable.Length);
            int streamItem = Items(tags.ArrayWord(SplineType, "data"), streamAt, stream.Length);
            int trackNameItem = annotationCount != 0 ? tags.AddItem(tags.StringWord(), trackNameAt, 1) : 0;

            void Point(int field, int item, int group)
            {
                if (item <= 0) return;
                if (group <= 0 || !tags.WriteIndexAt(field, item))
                    throw new InvalidOperationException("Could not write the pointer at " + field + " into the new animation section.");
                tags.SetPatch(group, field);
            }

            //hkaAnimationContainer: of its five arrays only bindings is used, and it holds the one binding
            Point(container + bindings, bindingListItem, tags.PatchGroupOf(ContainerType, "bindings"));
            //no member declares an element of a pointer array, so that pointer is filed under the element type
            Point(bindingList, bindingItem, bindingElement);

            //hkaAnimationBinding
            Point(binding + skeletonName, nameItem, tags.PatchGroupOf(BindingType, "originalSkeletonName"));
            Point(binding + animationField, animationItem, tags.PatchGroupOf(BindingType, "animation"));
            Point(binding + trackToBoneField, trackToBoneItem, tags.PatchGroupOf(BindingType, "transformTrackToBoneIndices"));
            Point(binding + floatSlotsField, floatSlotsItem, tags.PatchGroupOf(BindingType, "floatTrackToFloatSlotIndices"));
            data[binding + blendHint] = clip.BlendHint;
            for (int i = 0; i < trackToBone.Length; i++) BitConverter.GetBytes(trackToBone[i]).CopyTo(data, trackToBoneAt + (i * 2));
            for (int i = 0; i < floatSlots.Length; i++) BitConverter.GetBytes(floatSlots[i]).CopyTo(data, floatSlotsAt + (i * 2));
            name.CopyTo(data, nameAt);

            //hkaSplineCompressedAnimation - the hkaAnimation base first
            Int(data, animation + type, clip.Type);
            Float(data, animation + duration, clip.Duration);
            Int(data, animation + transformTracks, clip.TransformTracks);
            Int(data, animation + floatTracks, clip.FloatTracks);
            Point(animation + annotationsField, annotationsItem, tags.PatchGroupOf(AnimationType, "annotationTracks"));
            Int(data, animation + numFrames, clip.FrameCount);
            Int(data, animation + numBlocks, clip.BlockCount);
            Int(data, animation + maxFrames, clip.MaxFramesPerBlock);
            Int(data, animation + maskSize, clip.MaskAndQuantizationSize);
            Float(data, animation + blockDuration, clip.BlockDuration);
            Float(data, animation + blockInverse, clip.BlockInverseDuration);
            Float(data, animation + frameDuration, clip.FrameDuration);
            Point(animation + blocksField, blockTableItem, tags.PatchGroupOf(SplineType, "blockOffsets"));
            Point(animation + floatBlocksField, floatTableItem, tags.PatchGroupOf(SplineType, "floatBlockOffsets"));
            Point(animation + streamField, streamItem, tags.PatchGroupOf(SplineType, "data"));
            Int(data, animation + endian, clip.Endian);

            //every annotation track is empty, and all of them name the same empty string
            int trackNameGroup = tags.PatchGroupOf(TrackType, "trackName");
            for (int i = 0; i < annotationCount; i++)
                Point(annotationsAt + (i * trackSize) + trackName, trackNameItem, trackNameGroup);

            for (int i = 0; i < blockTable.Length; i++) BitConverter.GetBytes(blockTable[i]).CopyTo(data, blockTableAt + (i * 4));
            for (int i = 0; i < floatTable.Length; i++) BitConverter.GetBytes(floatTable[i]).CopyTo(data, floatTableAt + (i * 4));
            stream.CopyTo(data, Math.Max(0, streamAt));

            /* A tagfile names classes and pointers through its items and PTCH, which the packfile's
             * fixup lists only mirror - so those are rebuilt from what was just written. */
            target.LocalFixups.Clear();
            target.VirtualFixups.Clear();
            tags.RereadTypedViews(target);

            /* THSH hashes exactly the types a section uses. A template with extracted motion or
             * annotation text hashes types this clip no longer has, so those go - with the usual
             * template, whose clip has neither, nothing changes and TYPE is copied as it was. */
            tags.DropUnusedHashes();
        }
        #endregion
    }
}
