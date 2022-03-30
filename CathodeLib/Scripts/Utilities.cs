using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
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
        public static void Write<T>(BinaryWriter stream, List<T> aux)
        {
            Write<T>(stream, aux.ToArray<T>());
        }

        private static Dictionary<string, cGUID> guidCache = new Dictionary<string, cGUID>();
        public static cGUID GenerateGUID(string _s)
        {
            if (guidCache.ContainsKey(_s)) return guidCache[_s];

            SHA1Managed sha1 = new SHA1Managed();
            byte[] hash1 = sha1.ComputeHash(Encoding.UTF8.GetBytes(_s));
            byte[] arrangedHash = new byte[] {
                hash1[3], hash1[2], hash1[1], hash1[0], 
                hash1[7], hash1[6], hash1[5], hash1[4],
                hash1[11], hash1[10], hash1[9], hash1[8],
                hash1[15], hash1[14], hash1[13], hash1[12] 
            };
            byte[] hash2 = sha1.ComputeHash(Encoding.UTF8.GetBytes(BitConverter.ToString(arrangedHash).Replace("-", string.Empty)));
            cGUID finalGUID = new cGUID(new byte[] { hash2[0], hash2[1], hash2[2], hash2[3] });
            guidCache.Add(_s, finalGUID);
            return finalGUID;
        }

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
    }

#if !(UNITY_EDITOR || UNITY_STANDALONE)
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
#endif

    /* A unique id assigned to CATHODE objects - actually ShortGuid */
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cGUID : IComparable<cGUID>
    {
        public cGUID(float num)
        {
            val = BitConverter.GetBytes(num);
        }
        public cGUID(int num)
        {
            val = BitConverter.GetBytes(num);
        }
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
            if (((cGUID)obj).val == null) return this.val == null;
            if (this.val == null) return ((cGUID)obj).val == null;
            return ((cGUID)obj).val.SequenceEqual(this.val);
        }
        public static bool operator ==(cGUID x, cGUID y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (x.val == null) return y.val == null;
            if (y.val == null) return x.val == null;
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

        public int CompareTo(cGUID x)
        {
            if (x == null) return 0;
            if (x.val == null && val != null) return 0;
            if (x.val != null && val == null) return 0;
            if (x.val.Length != val.Length) return 0;

            int comp = 0;
            for (int i = 0; i < x.val.Length; i++)
            {
                comp += x.val[i].CompareTo(val[i]);
            }
            comp /= x.val.Length;

            return comp;
        }

        public override string ToString()
        {
            return BitConverter.ToString(val);
        }
        public uint ToUInt32()
        {
            return BitConverter.ToUInt32(val, 0);
        }
    }
}