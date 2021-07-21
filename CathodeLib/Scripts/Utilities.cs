using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CATHODE
{
    public class Utilities
    {
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

        public static void Align(BinaryReader reader, int val)
        {
            while (reader.BaseStream.Position % val != 0)
            {
                reader.ReadByte();
            }
        }
        public static void Align(BinaryWriter writer, int val)
        {
            while (writer.BaseStream.Position % val != 0)
            {
                writer.Write((char)0x00);
            }
        }

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
    }

    /* A unique id assigned to CATHODE objects */
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cGUID
    {
        public cGUID(byte[] id)
        {
            val = id;
        }
        public cGUID(BinaryReader reader)
        {
            val = reader.ReadBytes(4);
        }
        public cGUID(string id)
        {
            String[] arr = id.Split('-');
            if (arr.Length != 4) throw new Exception("Tried to initialise cGUID without 4-byte ID string.");
            byte[] array = new byte[arr.Length];
            for (int i = 0; i < arr.Length; i++) array[i] = Convert.ToByte(arr[i], 16);
            val = array;
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] val;

        public override bool Equals(object obj)
        {
            if (!(obj is cGUID)) return false;
            return ((cGUID)obj).val.SequenceEqual(this.val);
        }
        public static bool operator ==(cGUID x, cGUID y)
        {
            return x.val.SequenceEqual(y.val);
        }
        public static bool operator !=(cGUID x, cGUID y)
        {
            return !x.val.SequenceEqual(y.val);
        }
        public static bool operator ==(cGUID x, string y)
        {
            return x.ToString() == y;
        }
        public static bool operator !=(cGUID x, string y)
        {
            return x.ToString() != y;
        }
        public override int GetHashCode()
        {
            return BitConverter.ToInt32(val, 0);
        }

        public override string ToString()
        {
            return BitConverter.ToString(val);
        }
    }
}