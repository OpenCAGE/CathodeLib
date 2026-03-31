using CATHODE;
using CATHODE.Scripting;
using CathodeLib.ArrayExtensions;
using Extensions.Data;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Internal;
using K4os.Compression.LZ4.Streams;
using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace CathodeLib
{
    public static class Utilities
    {
        /// <summary>
        /// Read a single templated object
        /// </summary>
        public static T Consume<T>(BinaryReader reader, int offset)
        {
            reader.BaseStream.Position = offset;
            return Consume<T>(reader);
        }
        public static T Consume<T>(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(Marshal.SizeOf(typeof(T)));
            return Consume<T>(bytes);
        }
        public static T Consume<T>(byte[] bytes)
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            T theStructure = (T)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(T));
            handle.Free();
            return theStructure;
        }

        /// <summary>
        /// Read a templated array
        /// </summary>
        public static T[] ConsumeArray<T>(BinaryReader reader, int count, int offset)
        {
            reader.BaseStream.Position = offset;
            return ConsumeArray<T>(reader, count);
        }
        public static T[] ConsumeArray<T>(BinaryReader reader, int count)
        {
            T[] toReturn = new T[count];
            for (int i = 0; i < count; i++) toReturn[i] = Consume<T>(reader);
            return toReturn;
        }
        public static T[] ConsumeArray<T>(byte[] bytes, int count)
        {
            T[] toReturn = new T[count];
            for (int i = 0; i < count; i++) toReturn[i] = Consume<T>(bytes);
            return toReturn;
        }

        /// <summary>
        /// Align the stream
        /// </summary>
        public static void Align(BinaryReader reader, int val = 4)
        {
            reader.BaseStream.Seek((val - (reader.BaseStream.Position % val)) % val, SeekOrigin.Current);
        }
        public static void Align(BinaryWriter writer, int val = 4, byte fillWith = 0x00)
        {
            while (writer.BaseStream.Position % val != 0) writer.Write(fillWith);
        }

        /// <summary>
        /// Reads a string with alternating nulls up to a trailing 0x00 byte
        /// </summary>
        public static string ReadStringAlternating(byte[] bytes)
        {
            byte[] trimmed = new byte[bytes.Length / 2];
            int x = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i % 2 != 0) continue;
                trimmed[x] = bytes[i];
                if (bytes[i] == 0x00) break;
                x++;
            }
            return ReadString(trimmed);
        }

        /// <summary>
        /// Reads a string up to a trailing 0x00 byte
        /// </summary>
        public static string ReadString(BinaryReader reader, int position, bool resetPosition = true)
        {
            long startPos = reader.BaseStream.Position;
            reader.BaseStream.Position = position;
            List<byte> bytes = new List<byte>();
            while (true)
            {
                byte b = reader.ReadByte();
                if (b == 0x00)
                    break;
                bytes.Add(b);
            }
            if (resetPosition) reader.BaseStream.Position = startPos;
            return Encoding.ASCII.GetString(bytes.ToArray());
        }
        public static string ReadString(byte[] byteArray, int position)
        {
            List<byte> bytes = new List<byte>();
            for (int i = 0; i < byteArray.Length; i++)
            {
                if (byteArray[position + i] == 0x00)
                    break;
                bytes.Add(byteArray[position + i]);
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }
        public static string ReadString(BinaryReader reader)
        {
            List<byte> bytes = new List<byte>();
            while (true)
            {
                byte b = reader.ReadByte();
                if (b == 0x00)
                    break;
                bytes.Add(b);
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }
        public static string ReadString(byte[] byteArray)
        {
            List<byte> bytes = new List<byte>();
            for (int i = 0; i < byteArray.Length; i++)
            {
                if (byteArray[i] == 0x00)
                    break;
                bytes.Add(byteArray[i]);
            }
            return Encoding.ASCII.GetString(bytes.ToArray());
        }

        /// <summary>
        /// Writes a string without a leading length value
        /// </summary>
        public static void WriteString(string string_to_write, BinaryWriter writer, bool trailingByte = false)
        {
            byte[] content = Encoding.ASCII.GetBytes(string_to_write);
            writer.Write(content);
            if (trailingByte) writer.Write((byte)0x00);
        }

        /// <summary>
        /// Removes the leading nulls from a byte array, useful for cleaning byte-aligned file extracts
        /// </summary>
        public static byte[] RemoveLeadingNulls(byte[] extracted_file)
        {
            //Remove from leading
            int start_offset = 0;
            for (int i = 0; i < 4; i++)
            {
                if (extracted_file[i] == 0x00)
                {
                    start_offset = i + 1;
                    continue;
                }
                break;
            }
            byte[] to_return = new byte[extracted_file.Length - start_offset];
            Array.Copy(extracted_file, start_offset, to_return, 0, to_return.Length);
            return to_return;
        }

        /// <summary>
        /// Write a templated type
        /// </summary>
        public static void Write<T>(BinaryWriter stream, T aux)
        {
            int length = Marshal.SizeOf(aux);
            IntPtr ptr = Marshal.AllocHGlobal(length);
            byte[] myBuffer = new byte[length];

            Marshal.StructureToPtr(aux, ptr, true);
            Marshal.Copy(ptr, myBuffer, 0, length);
            Marshal.FreeHGlobal(ptr);

            stream.Write(myBuffer);
        }
        public static void Write<T>(BinaryWriter stream, T[] aux)
        {
            for (int i = 0; i < aux.Length; i++) Write<T>(stream, aux[i]);
        }
        public static void Write<T>(BinaryWriter stream, List<T> aux)
        {
            Write<T>(stream, aux.ToArray<T>());
        }

        /// <summary>
        /// Generate a hashed string for use in the animation system (FNV hash)
        /// </summary>
        public static uint AnimationHashedString(string str)
        {
            uint hash = 0x811c9dc5;
            uint prime = 0x1000193;

            for (int i = 0; i < str.Length; ++i)
            {
                char value = str[i];
                hash = hash ^ value;
                hash *= prime;
            }

            return hash;
        }

        /// <summary>
        /// Generate a hashed string for use in sound system (wwise FNV-1)
        /// </summary>
        public static uint SoundHashedString(string str)
        {
            byte[] namebytes = Encoding.UTF8.GetBytes(str.ToLower());
            uint hash = 2166136261;
            for (int i = 0; i < namebytes.Length; i++)
            {
                byte namebyte = namebytes[i];
                hash = hash * 16777619;
                hash = hash ^ namebyte;
                hash = hash & 0xFFFFFFFF;
            }
            return hash;
        }

        /// <summary>
        /// Compress a PAK file using FZIP
        /// </summary>
        public static void FZipCompressPAK(string filepath, bool appendExtension = false)
        {
            int origUnpackedSize;
            List<byte[]> uncompressedContent = new List<byte[]>();
            {
                using (BinaryReader reader = new BinaryReader(File.OpenRead(filepath)))
                {
                    origUnpackedSize = (int)reader.BaseStream.Length;

                    reader.BaseStream.Position += 16;
                    int MaxEntryCount = BinaryPrimitives.ReverseEndianness(reader.ReadInt32());
                    reader.BaseStream.Position += 12;

                    List<Tuple<int, int>> offsetAndLengths = new List<Tuple<int, int>>();
                    for (int i = 0; i < MaxEntryCount; i++)
                    {
                        reader.BaseStream.Position += 12;
                        int length = BinaryPrimitives.ReverseEndianness(reader.ReadInt32());
                        int offset = BinaryPrimitives.ReverseEndianness(reader.ReadInt32());
                        reader.BaseStream.Position += 28;

                        if (length > 0)
                            offsetAndLengths.Add(new Tuple<int, int>(offset, length));
                    }

                    long origHeaderSize = reader.BaseStream.Position;
                    reader.BaseStream.Position = 0;
                    uncompressedContent.Insert(0, reader.ReadBytes((int)origHeaderSize));

                    int endOfHeaders = 32 + (MaxEntryCount * 48);

                    foreach (Tuple<int, int> offsetAndLength in offsetAndLengths)
                    {
                        reader.BaseStream.Position = offsetAndLength.Item1 + endOfHeaders;
                        uncompressedContent.Add(reader.ReadBytes(offsetAndLength.Item2));
                    }
                }
            }

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(filepath + (appendExtension ? ".fzip" : ""))))
            {
                writer.BaseStream.SetLength(0);

                writer.Write(new char[] { 'F', 'Z', 'I', 'P' });
                writer.Write((Int16)2);
                writer.Write((Int16)0);
                writer.Write(uncompressedContent.Count);
                writer.Write(origUnpackedSize);

                int newPakHeaderSize = 16 + (20 * uncompressedContent.Count);
                int offsetTrackerCompressed = newPakHeaderSize;
                int offsetTrackerOriginal = 0;

                List<byte[]> compressedContent = new List<byte[]>();
                for (int i = 0; i < uncompressedContent.Count; i++)
                {
                    int uncompressedSize = uncompressedContent[i].Length;
                    if (uncompressedSize < 0) uncompressedSize = 0;
                    int compressedSize = 0;

                    if (uncompressedSize != 0)
                    {
                        using (var output = new MemoryStream())
                        {
                            var settings = new LZ4EncoderSettings
                            {
                                BlockSize = uncompressedSize <= 64 * 1024 ? Mem.K64 : uncompressedSize <= 256 * 1024 ? Mem.K256 : uncompressedSize <= 1024 * 1024 ? Mem.M1 : Mem.M4,
                                ContentChecksum = true,
                                BlockChecksum = false,
                                ChainBlocks = true,
                                CompressionLevel = LZ4Level.L12_MAX
                            };

                            using (var lz4 = LZ4Stream.Encode(output, settings, leaveOpen: true))
                            {
                                lz4.Write(uncompressedContent[i], 0, uncompressedContent[i].Length);
                            }

                            byte[] bytes = output.ToArray();
                            compressedSize = bytes.Length;
                            compressedContent.Add(bytes);
                        }
                    }
                    else
                    {
                        compressedContent.Add(new byte[] { });
                    }

                    writer.Write(offsetTrackerCompressed);
                    writer.Write(compressedSize);
                    writer.Write(offsetTrackerOriginal);
                    writer.Write(uncompressedSize);
                    writer.Write((Int16)4);
                    writer.Write((Int16)0);

                    offsetTrackerCompressed += compressedSize;
                    offsetTrackerOriginal += uncompressedSize;
                }
                for (int i = 0; i < uncompressedContent.Count; i++)
                {
                    writer.Write(compressedContent[i]);
                }
            }
        }

        /// <summary>
        /// Convert a model component to a renderable element using its default materials
        /// </summary>
        public static List<RenderableElements.Element> ToRenderableElements(this Models.CS2.Component model)
        {
            List<RenderableElements.Element> reds = new List<RenderableElements.Element>();
            List<RenderableElements.Element> lod = new List<RenderableElements.Element>();
            for (int i = model.LODs.Count - 1; i >= 0; --i)
            {
                for (int x = 0; x < model.LODs[i].Submeshes.Count; x++)
                {
                    RenderableElements.Element element = new RenderableElements.Element()
                    {
                        Model = model.LODs[i].Submeshes[x],
                        Material = model.LODs[i].Submeshes[x].Material,
                    };
                    if (x == 0)
                    {
                        element.LODs.AddRange(lod);
                        lod.Clear();
                    }
                    if (i != 0)
                    {
                        lod.Add(element);
                    }
                    else
                    {
                        reds.Add(element);
                    }
                }
            }
            return reds;
        }

        /// <summary>
        /// Load a Level by passing AI.exe's folder, and the name of the level in ENV (e.g. Production/Frontend)
        /// </summary>
        public static Level LoadLevel(string pathToAI, string level)
        {
            PAK2 animPAK = new PAK2(pathToAI + "\\DATA\\GLOBAL\\ANIMATION.PAK");
            Global global = new Global(pathToAI + "\\DATA\\ENV\\GLOBAL\\", animPAK);
            return new Level(pathToAI + "\\DATA\\ENV\\" + level, global);
        }

        /// <summary>
        /// Read a PAK
        /// </summary>
        public static List<PAKContent> ReadPAK(string path, FileIdentifiers type)
        {
            List<PAKContent> content = new List<PAKContent>();
            bool e = type == FileIdentifiers.MODEL_DATA;

            using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
            {
                reader.BaseStream.Position += 4;
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(reader, e) != FileIdentifiers.ASSET_FILE) return null;
                if ((FileIdentifiers)BigEndianUtils.ReadInt32(reader, e) != type) return null;
                int entryCount = BigEndianUtils.ReadInt32(reader, e);
                int entryCountActual = BigEndianUtils.ReadInt32(reader, e);
                reader.BaseStream.Position += 12;

                int endOfHeaders = 32 + (entryCountActual * 48);

                List<OffsetPair> info = new List<OffsetPair>();
                for (int i = 0; i < entryCount; i++)
                {
                    reader.BaseStream.Position += 8;
                    int length = BigEndianUtils.ReadInt32(reader, e);
                    reader.BaseStream.Position += 4;
                    int offset = BigEndianUtils.ReadInt32(reader, e);
                    reader.BaseStream.Position += 12;
                    int binIndex = BigEndianUtils.ReadInt32(reader, e);
                    reader.BaseStream.Position += 12;

                    info.Add(new OffsetPair() { GlobalOffset = offset + endOfHeaders, EntryCount = length });
                    content.Add(new PAKContent() { BinIndex = binIndex });
                }

                for (int i = 0; i < entryCount; i++)
                {
                    reader.BaseStream.Position = info[i].GlobalOffset;
                    content[i].Data = reader.ReadBytes(info[i].EntryCount);
                }
            }

            return content;
        }

        /// <summary>
        /// Write a PAK
        /// </summary>
        public static void WritePAK(string path, FileIdentifiers type, List<PAKContent> content)
        {
            bool e = type == FileIdentifiers.MODEL_DATA;

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(path)))
            {
                //Write content
                int contentOffset = 32 + (content.Count * 48);
                writer.BaseStream.SetLength(contentOffset);
                writer.BaseStream.Position = contentOffset;
                List<int> offsets = new List<int>();
                List<int> lengths = new List<int>();
                List<int> lengthsAligned = new List<int>();
                for (int i = 0; i < content.Count; i++)
                {
                    offsets.Add((int)writer.BaseStream.Position - contentOffset);
                    writer.Write(content[i].Data);
                    lengths.Add((int)writer.BaseStream.Position - contentOffset - offsets[offsets.Count - 1]);
                    Align(writer, 16);
                    lengthsAligned.Add((int)writer.BaseStream.Position - contentOffset - offsets[offsets.Count - 1]);
                }

                //Write model headers
                writer.BaseStream.Position = 32;
                for (int i = 0; i < content.Count; i++)
                {
                    writer.Write(new byte[8]);
                    writer.Write(e ? BigEndianUtils.FlipEndian((Int32)lengths[i]) : BitConverter.GetBytes((Int32)lengths[i]));
                    writer.Write(e ? BigEndianUtils.FlipEndian((Int32)lengthsAligned[i]) : BitConverter.GetBytes((Int32)lengthsAligned[i]));
                    writer.Write(e ? BigEndianUtils.FlipEndian((Int32)offsets[i]) : BitConverter.GetBytes((Int32)offsets[i]));

                    writer.Write(new byte[5]);
                    writer.Write(type == FileIdentifiers.MODEL_DATA ? new byte[2] { 0x01, 0x01 } : new byte[2] { 0x00, 0x01 });
                    writer.Write(new byte[5]);

                    writer.Write(e ? BigEndianUtils.FlipEndian((Int32)content[i].BinIndex) : BitConverter.GetBytes((Int32)content[i].BinIndex));
                    writer.Write(new byte[12]);
                }

                //Write header
                writer.BaseStream.Position = 0;
                writer.Write(new byte[4]);
                writer.Write(e ? BigEndianUtils.FlipEndian((Int32)FileIdentifiers.ASSET_FILE) : BitConverter.GetBytes((Int32)FileIdentifiers.ASSET_FILE));
                writer.Write(e ? BigEndianUtils.FlipEndian((Int32)type) : BitConverter.GetBytes((Int32)type));
                writer.Write(e ? BigEndianUtils.FlipEndian((Int32)content.Count) : BitConverter.GetBytes((Int32)content.Count));
                writer.Write(e ? BigEndianUtils.FlipEndian((Int32)content.Count) : BitConverter.GetBytes((Int32)content.Count));
                writer.Write(e ? BigEndianUtils.FlipEndian((Int32)16) : BitConverter.GetBytes((Int32)16));
                writer.Write(e ? BigEndianUtils.FlipEndian((Int32)1) : BitConverter.GetBytes((Int32)1));

                int unk = type == FileIdentifiers.MODEL_DATA ? 1 : 0;
                writer.Write(e ? BigEndianUtils.FlipEndian((Int32)unk) : BitConverter.GetBytes((Int32)unk));
            }
        }

        public class PAKContent
        {
            public int BinIndex = 0;
            public byte[] Data;
        }
    }

    public static class MathsUtils
    {
        public static (decimal, decimal, decimal) ToYawPitchRoll(this Quaternion q)
        {
            // Helper function to safely convert double to decimal
            decimal SafeConvertToDecimal(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                    return 0m;

                // Clamp to decimal range to prevent overflow
                const double maxDecimal = (double)decimal.MaxValue;
                const double minDecimal = (double)decimal.MinValue;

                if (value > maxDecimal) value = maxDecimal;
                if (value < minDecimal) value = minDecimal;

                try
                {
                    return Convert.ToDecimal(value);
                }
                catch (OverflowException)
                {
                    return 0m;
                }
            }

            // Check for invalid quaternion
            if (float.IsNaN(q.X) || float.IsNaN(q.Y) || float.IsNaN(q.Z) || float.IsNaN(q.W) ||
                float.IsInfinity(q.X) || float.IsInfinity(q.Y) || float.IsInfinity(q.Z) || float.IsInfinity(q.W))
            {
                return (0m, 0m, 0m);
            }

            // Calculate yaw, pitch, roll with safe conversion
            double yawRad = Math.Atan2(2 * (q.Y * q.W + q.X * q.Z), 1 - 2 * (q.Y * q.Y + q.X * q.X));
            double pitchRad = Math.Asin(Math.Max(-1, Math.Min(1, 2 * (q.X * q.W - q.Z * q.Y)))); // Clamp to [-1, 1] to prevent domain error
            double rollRad = Math.Atan2(2 * (q.Z * q.W + q.X * q.Y), 1 - 2 * (q.X * q.X + q.Z * q.Z));

            decimal yaw = SafeConvertToDecimal(yawRad * (180 / Math.PI));
            decimal pitch = SafeConvertToDecimal(pitchRad * (180 / Math.PI));
            decimal roll = SafeConvertToDecimal(rollRad * (180 / Math.PI));

            return (yaw, pitch, roll);
        }

        public static Vector3 AddEulerAngles(this Vector3 euler1, Vector3 euler2)
        {
            Vector3 result = euler1 + euler2;
            result = NormalizeEulerAngles(result);
            return result;
        }
        private static Vector3 NormalizeEulerAngles(Vector3 angles)
        {
            angles.X = NormalizeAngle(angles.X);
            angles.Y = NormalizeAngle(angles.Y);
            angles.Z = NormalizeAngle(angles.Z);
            return angles;
        }
        private static float NormalizeAngle(float angle)
        {
            angle = angle % 360;
            if (angle > 180)
            {
                angle -= 360;
            }
            if (angle < -180)
            {
                angle += 360;
            }
            return angle;
        }
    }

    public static class BigEndianUtils
    {
        public static Int64 ReadInt64(BinaryReader Reader, bool bigEndian = true)
        {
            var data = Reader.ReadBytes(8);
            if (bigEndian) Array.Reverse(data);
            return BitConverter.ToInt64(data, 0);
        }
        public static UInt64 ReadUInt64(BinaryReader Reader, bool bigEndian = true)
        {
            var data = Reader.ReadBytes(8);
            if (bigEndian) Array.Reverse(data);
            return BitConverter.ToUInt64(data, 0);
        }

        public static Int32 ReadInt32(BinaryReader Reader, bool bigEndian = true)
        {
            byte[] data = Reader.ReadBytes(4);
            if (bigEndian) Array.Reverse(data);
            return BitConverter.ToInt32(data, 0);
        }
        public static UInt32 ReadUInt32(BinaryReader Reader, bool bigEndian = true)
        {
            var data = Reader.ReadBytes(4);
            if (bigEndian) Array.Reverse(data);
            return BitConverter.ToUInt32(data, 0);
        }

        public static Int16 ReadInt16(BinaryReader Reader, bool bigEndian = true)
        {
            var data = Reader.ReadBytes(2);
            if (bigEndian) Array.Reverse(data);
            return BitConverter.ToInt16(data, 0);
        }
        public static UInt16 ReadUInt16(BinaryReader Reader, bool bigEndian = true)
        {
            var data = Reader.ReadBytes(2);
            if (bigEndian) Array.Reverse(data);
            return BitConverter.ToUInt16(data, 0);
        }

        public static byte[] FlipEndian(byte[] ThisEndian)
        {
            Array.Reverse(ThisEndian);
            return ThisEndian;
        }
        public static byte[] FlipEndian(Int64 ThisEndian)
        {
            return FlipEndian(BitConverter.GetBytes(ThisEndian));
        }
        public static byte[] FlipEndian(UInt64 ThisEndian)
        {
            return FlipEndian(BitConverter.GetBytes(ThisEndian));
        }
        public static byte[] FlipEndian(Int32 ThisEndian)
        {
            return FlipEndian(BitConverter.GetBytes(ThisEndian));
        }
        public static byte[] FlipEndian(UInt32 ThisEndian)
        {
            return FlipEndian(BitConverter.GetBytes(ThisEndian));
        }
        public static byte[] FlipEndian(Int16 ThisEndian)
        {
            return FlipEndian(BitConverter.GetBytes(ThisEndian));
        }
        public static byte[] FlipEndian(UInt16 ThisEndian)
        {
            return FlipEndian(BitConverter.GetBytes(ThisEndian));
        }
    }

    public enum PakLocation
    {
        LEVEL,
        GLOBAL
    }

    public enum FileIdentifiers
    {
        HEADER_FILE = 96,
        ASSET_FILE = 14,

        SHADER_DATA = 3,
        MODEL_DATA = 19,
        TEXTURE_DATA = 45,

        //From ABOUT.TXT (unsure where used)
        STRING_FILE_VERSION = 6,
        ENTITY_FILE_VERSION = 171,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct OffsetPair
    {
        public int GlobalOffset;
        public int EntryCount;

        public OffsetPair(int _go, int _ec)
        {
            GlobalOffset = _go;
            EntryCount = _ec;
        }
        public OffsetPair(long _go, int _ec)
        {
            GlobalOffset = (int)_go;
            EntryCount = _ec;
        }
    }

    /// <summary>
    /// This acts as a helper class for the link between legacy data and Commands. MVR, resources, and character assets use this link.
    /// It defines the ID of the entity we're interested in and the instance ID of the composite that contains it.
    /// The instance ID identifies the hierarchy the composite was created at. This can be generated with GenerateInstance.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class EntityHandle
    {
        public ShortGuid entity_id = ShortGuid.Invalid;             //The ID of the entity within its written composite
        public ShortGuid composite_instance_id = ShortGuid.Invalid; //The instance of the composite this entity is in when created via hierarchy

        public EntityHandle() { }
        public EntityHandle(EntityPath hierarchy)
        {
            entity_id = hierarchy.GetPointedEntityID();
            composite_instance_id = hierarchy.GenerateCompositeInstanceID();
        }

        public override bool Equals(object obj)
        {
            if (!(obj is EntityHandle)) return false;
            return (EntityHandle)obj == this;
        }

        public static bool operator ==(EntityHandle x, EntityHandle y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
            if (x.entity_id != y.entity_id) return false;
            if (x.composite_instance_id != y.composite_instance_id) return false;
            return true;
        }
        public static bool operator !=(EntityHandle x, EntityHandle y)
        {
            return !(x == y);
        }

        public override int GetHashCode()
        {
            int hashCode = -539839184;
            hashCode = hashCode * -1521134295 + entity_id.GetHashCode();
            hashCode = hashCode * -1521134295 + composite_instance_id.GetHashCode();
            return hashCode;
        }
    }

    /*
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Vector3 : ICloneable
    {
        public Vector3(float _x, float _y, float _z)
        {
            vals = new float[3] { 0, 0, 0 };
            x = _x;
            y = _y;
            z = _z;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Vector3)) return false;
            return (Vector3)obj == this;
        }
        public static bool operator ==(Vector3 x, Vector3 y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
            if (x.x != y.x) return false;
            if (x.y != y.y) return false;
            if (x.z != y.z) return false;
            return true;
        }
        public static Vector3 operator +(Vector3 x, Vector3 y)
        {
            return new Vector3(x.x + y.x, x.y + y.y, x.z + y.z);
        }
        public static bool operator !=(Vector3 x, Vector3 y)
        {
            return !(x == y);
        }
        public override int GetHashCode()
        {
            return (int)(x * y * z * 100);
        }

        public object Clone()
        {
            return new Vector3(this.x, this.y, this.z);
        }

        public override string ToString()
        {
            return "(" + x + ", " + y + ", " + z + ")";
        }

        public float x { 
            get { return (vals == null) ? 0.0f : vals[0]; } 
            set
            {
                if (vals == null) vals = new float[3] { value, 0, 0 };
                else vals[0] = value;
            }
        }
        public float y
        {
            get { return (vals == null) ? 0.0f : vals[1]; }
            set
            {
                if (vals == null) vals = new float[3] { 0, value, 0 };
                else vals[1] = value;
            }
        }
        public float z
        {
            get { return (vals == null) ? 0.0f : vals[2]; }
            set
            {
                if (vals == null) vals = new float[3] { 0, 0, value };
                else vals[2] = value;
            }
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        private float[] vals;
    }
    */

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct fourcc
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public char[] V;

        public override string ToString()
        {
            return new string(V);
        }
    }

}

namespace CathodeLib.ObjectExtensions
{
    public static class ObjectExtensions
    {
        private static readonly MethodInfo CloneMethod = typeof(Object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

        public static bool IsPrimitive(this Type type)
        {
            if (type == typeof(String)) return true;
            return (type.IsValueType & type.IsPrimitive);
        }

        public static Object Copy(this Object originalObject)
        {
            return InternalCopy(originalObject, new Dictionary<Object, Object>(new ReferenceEqualityComparer()));
        }
        private static Object InternalCopy(Object originalObject, IDictionary<Object, Object> visited)
        {
            if (originalObject == null) return null;
            var typeToReflect = originalObject.GetType();
            if (IsPrimitive(typeToReflect)) return originalObject;
            if (visited.TryGetValue(originalObject, out object obj)) return obj;
            if (typeof(Delegate).IsAssignableFrom(typeToReflect)) return null;
            var cloneObject = CloneMethod.Invoke(originalObject, null);
            if (typeToReflect.IsArray)
            {
                var arrayType = typeToReflect.GetElementType();
                if (IsPrimitive(arrayType) == false)
                {
                    Array clonedArray = (Array)cloneObject;
                    clonedArray.ForEach((array, indices) => array.SetValue(InternalCopy(clonedArray.GetValue(indices), visited), indices));
                }

            }
            visited.Add(originalObject, cloneObject);
            CopyFields(originalObject, visited, cloneObject, typeToReflect);
            RecursiveCopyBaseTypePrivateFields(originalObject, visited, cloneObject, typeToReflect);
            return cloneObject;
        }

        private static void RecursiveCopyBaseTypePrivateFields(object originalObject, IDictionary<object, object> visited, object cloneObject, Type typeToReflect)
        {
            if (typeToReflect.BaseType != null)
            {
                RecursiveCopyBaseTypePrivateFields(originalObject, visited, cloneObject, typeToReflect.BaseType);
                CopyFields(originalObject, visited, cloneObject, typeToReflect.BaseType, BindingFlags.Instance | BindingFlags.NonPublic, info => info.IsPrivate);
            }
        }

        private static void CopyFields(object originalObject, IDictionary<object, object> visited, object cloneObject, Type typeToReflect, BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.FlattenHierarchy, Func<FieldInfo, bool> filter = null)
        {
            foreach (FieldInfo fieldInfo in typeToReflect.GetFields(bindingFlags))
            {
                if (filter != null && filter(fieldInfo) == false) continue;
                if (IsPrimitive(fieldInfo.FieldType)) continue;
                var originalFieldValue = fieldInfo.GetValue(originalObject);
                var clonedFieldValue = InternalCopy(originalFieldValue, visited);
                fieldInfo.SetValue(cloneObject, clonedFieldValue);
            }
        }
        public static T Copy<T>(this T original)
        {
            return (T)Copy((Object)original);
        }
    }

    public class ReferenceEqualityComparer : EqualityComparer<Object>
    {
        public override bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }
        public override int GetHashCode(object obj)
        {
            if (obj == null) return 0;
            return obj.GetHashCode();
        }
    }
}

namespace CathodeLib.ArrayExtensions
{ 
    public static class ArrayExtensions
    {
        public static void ForEach(this Array array, Action<Array, int[]> action)
        {
            if (array.LongLength == 0) return;
            ArrayTraverse walker = new ArrayTraverse(array);
            do action(array, walker.Position);
            while (walker.Step());
        }
    }

    internal class ArrayTraverse
    {
        public int[] Position;
        private int[] maxLengths;

        public ArrayTraverse(Array array)
        {
            maxLengths = new int[array.Rank];
            for (int i = 0; i < array.Rank; ++i)
            {
                maxLengths[i] = array.GetLength(i) - 1;
            }
            Position = new int[array.Rank];
        }

        public bool Step()
        {
            for (int i = 0; i < Position.Length; ++i)
            {
                if (Position[i] < maxLengths[i])
                {
                    Position[i]++;
                    for (int j = 0; j < i; j++)
                    {
                        Position[j] = 0;
                    }
                    return true;
                }
            }
            return false;
        }
    }
}