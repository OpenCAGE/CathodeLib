using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace TestProject
{
    public class Utilities
    {
        public static T Consume<T>(ref BinaryReader reader)
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

        public static List<T> ConsumeArray<T>(ref BinaryReader reader, int count)
        {
            List<T> toReturn = new List<T>();
            for (int i = 0; i < count; i++) toReturn.Add(Consume<T>(ref reader));
            return toReturn;
        }
        public static List<T> ConsumeArray<T>(byte[] bytes, int count)
        {
            List<T> toReturn = new List<T>();
            for (int i = 0; i < count; i++) toReturn.Add(Consume<T>(bytes));
            return toReturn;
        }

        public static void Align(ref BinaryReader reader, int val)
        {
            while (reader.BaseStream.Position % val != 0)
            {
                reader.ReadByte();
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
        public static string ReadString(ref BinaryReader reader)
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
    }
}