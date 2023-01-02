using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CATHODE.Scripting
{
    /* A unique id assigned to CATHODE objects */
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ShortGuid : IComparable<ShortGuid>
    {
        public ShortGuid(float num)
        {
            val = BitConverter.GetBytes(num);
        }
        public ShortGuid(int num)
        {
            val = BitConverter.GetBytes(num);
        }
        public ShortGuid(byte[] id)
        {
            val = id;
        }
        public ShortGuid(BinaryReader reader)
        {
            val = reader.ReadBytes(4);
        }
        public ShortGuid(string id)
        {
            System.String[] arr = id.Split('-');
            if (arr.Length != 4) throw new Exception("Tried to initialise ShortGuid without 4-byte ID string.");
            byte[] array = new byte[arr.Length];
            for (int i = 0; i < arr.Length; i++) array[i] = Convert.ToByte(arr[i], 16);
            val = array;
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] val;

        public override bool Equals(object obj)
        {
            if (!(obj is ShortGuid)) return false;
            if (((ShortGuid)obj).val == null) return this.val == null;
            if (this.val == null) return ((ShortGuid)obj).val == null;
            return ((ShortGuid)obj).val.SequenceEqual(this.val);
        }
        public static bool operator ==(ShortGuid x, ShortGuid y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (x.val == null) return y.val == null;
            if (y.val == null) return x.val == null;
            return x.val.SequenceEqual(y.val);
        }
        public static bool operator !=(ShortGuid x, ShortGuid y)
        {
            return !x.val.SequenceEqual(y.val);
        }
        public static bool operator ==(ShortGuid x, string y)
        {
            return x.ToString() == y;
        }
        public static bool operator !=(ShortGuid x, string y)
        {
            return x.ToString() != y;
        }
        public override int GetHashCode()
        {
            return BitConverter.ToInt32(val, 0);
        }

        public int CompareTo(ShortGuid x)
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
