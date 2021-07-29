using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

        public static cGUID GenerateGUID(string _s)
        {
            SHA1Managed sha1 = new SHA1Managed();
            byte[] hash1 = sha1.ComputeHash(Encoding.UTF8.GetBytes(_s));
            byte[] arrangedHash = new byte[] {
                hash1[3], hash1[2], hash1[1], hash1[0], 
                hash1[7], hash1[6], hash1[5], hash1[4],
                hash1[11], hash1[10], hash1[9], hash1[8],
                hash1[15], hash1[14], hash1[13], hash1[12] 
            };
            byte[] hash2 = sha1.ComputeHash(Encoding.UTF8.GetBytes(BitConverter.ToString(arrangedHash).Replace("-", string.Empty)));
            return new cGUID(new byte[] { hash2[0], hash2[1], hash2[2], hash2[3] });
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