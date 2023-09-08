using CATHODE.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;

namespace CathodeLib
{
    public class Utilities
    {
        //Read a single templated object
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

        //Read a templated array
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

        //Align the stream
        public static void Align(BinaryReader reader, int val = 4)
        {
            while (reader.BaseStream.Position % val != 0) reader.ReadByte();
        }
        public static void Align(BinaryWriter writer, int val = 4, byte fillWith = 0x00)
        {
            while (writer.BaseStream.Position % val != 0) writer.Write(fillWith);
        }

        //Reads a string with alternating nulls up to a trailing 0x00 byte
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

        //Reads a string up to a trailing 0x00 byte
        public static string ReadString(byte[] bytes, int position)
        {
            string to_return = "";
            for (int i = 0; i < int.MaxValue; i++)
            {
                byte this_byte = bytes[position + i];
                if (this_byte == 0x00) break;
                to_return += (char)this_byte;
            }
            return to_return;
        }
        public static string ReadString(BinaryReader reader)
        {
            string to_return = "";
            for (int i = 0; i < int.MaxValue; i++)
            {
                byte this_byte = reader.ReadByte();
                if (this_byte == 0x00) break;
                to_return += (char)this_byte;
            }
            return to_return;
        }
        public static string ReadString(byte[] bytes)
        {
            string to_return = "";
            for (int i = 0; i < bytes.Length; i++)
            {
                byte this_byte = bytes[i];
                if (this_byte == 0x00) break;
                to_return += (char)this_byte;
            }
            return to_return;
        }

        //Writes a string without a leading length value
        public static void WriteString(string string_to_write, BinaryWriter writer, bool trailingByte = false)
        {
            foreach (char character in string_to_write)
                writer.Write(character);

            if (trailingByte) writer.Write((char)0x00);
        }


        //Gets a string from a byte array (at position) by reading chars until a null is hit
        public static string GetStringFromByteArray(byte[] byte_array, int position)
        {
            string to_return = "";
            for (int i = 0; i < 999999999; i++)
            {
                byte this_byte = byte_array[position + i];
                if (this_byte == 0x00)
                {
                    break;
                }
                to_return += (char)this_byte;
            }
            return to_return;
        }

        //Removes the leading nulls from a byte array, useful for cleaning byte-aligned file extracts
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

        //Write a templated type
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

        //Clones an object (slow!)
        public static T CloneObject<T>(T obj)
        {
            //A somewhat hacky an inefficient way of deep cloning an object (TODO: optimise this as we use it a lot!)
            MemoryStream ms = new MemoryStream();
            new BinaryFormatter().Serialize(ms, obj);
            ms.Position = 0;
            T obj2 = (T)new BinaryFormatter().Deserialize(ms);
            ms.Close();
            return obj2;
            //obj.MemberwiseClone();
        }

        //Generate a hashed string for use in the animation system (FNV hash)
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
    }

    public static class BigEndianUtils
    {
        public static Int64 ReadInt64(BinaryReader Reader)
        {
            var data = Reader.ReadBytes(8);
            Array.Reverse(data);
            return BitConverter.ToInt64(data, 0);
        }
        public static UInt64 ReadUInt64(BinaryReader Reader)
        {
            var data = Reader.ReadBytes(8);
            Array.Reverse(data);
            return BitConverter.ToUInt64(data, 0);
        }

        public static Int32 ReadInt32(BinaryReader Reader)
        {
            byte[] data = Reader.ReadBytes(4);
            Array.Reverse(data);
            return BitConverter.ToInt32(data, 0);
        }
        public static UInt32 ReadUInt32(BinaryReader Reader)
        {
            var data = Reader.ReadBytes(4);
            Array.Reverse(data);
            return BitConverter.ToUInt32(data, 0);
        }

        public static Int16 ReadInt16(BinaryReader Reader)
        {
            var data = Reader.ReadBytes(2);
            Array.Reverse(data);
            return BitConverter.ToInt16(data, 0);
        }
        public static UInt16 ReadUInt16(BinaryReader Reader)
        {
            var data = Reader.ReadBytes(2);
            Array.Reverse(data);
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
    public class CommandsEntityReference
    {
        public ShortGuid entity_id = ShortGuid.Invalid;             //The ID of the entity within its written composite
        public ShortGuid composite_instance_id = ShortGuid.Invalid; //The instance of the composite this entity is in when created via hierarchy

        public CommandsEntityReference() { }
        public CommandsEntityReference(EntityPath hierarchy)
        {
            entity_id = hierarchy.GetPointedEntityID();
            composite_instance_id = hierarchy.GenerateInstance();
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
    }
}