
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
        /// </summary>
        public void BuildInto(HavokPackfile target)
        {
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
    }
}
