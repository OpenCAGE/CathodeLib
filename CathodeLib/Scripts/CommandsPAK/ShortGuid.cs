using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CATHODE.Commands
{
    public class ShortGuidUtils
    {
        private static Dictionary<string, ShortGuid> guidCache = new Dictionary<string, ShortGuid>();
        private static Dictionary<ShortGuid, string> guidCacheReversed = new Dictionary<ShortGuid, string>();

        /* Pull in strings we know are cached as ShortGuid in Cathode */
        public ShortGuidUtils()
        {
            //TODO: re-gen this file with content from the iOS dump
            BinaryReader reader = new BinaryReader(new MemoryStream(CathodeLib.Properties.Resources.cathode_generic_lut));
            reader.BaseStream.Position += 1;
            while (reader.BaseStream.Position < reader.BaseStream.Length)
                Cache(new ShortGuid(reader.ReadBytes(4)), reader.ReadString());
            reader.Close();
        }

        /* Generate a ShortGuid to interface with the Cathode scripting system */
        public static ShortGuid Generate(string value)
        {
            if (guidCache.ContainsKey(value)) return guidCache[value];

            SHA1Managed sha1 = new SHA1Managed();
            byte[] hash1 = sha1.ComputeHash(Encoding.UTF8.GetBytes(value));
            byte[] arrangedHash = new byte[] {
                hash1[3], hash1[2], hash1[1], hash1[0],
                hash1[7], hash1[6], hash1[5], hash1[4],
                hash1[11], hash1[10], hash1[9], hash1[8],
                hash1[15], hash1[14], hash1[13], hash1[12]
            };
            byte[] hash2 = sha1.ComputeHash(Encoding.UTF8.GetBytes(BitConverter.ToString(arrangedHash).Replace("-", string.Empty)));
            ShortGuid guid = new ShortGuid(new byte[] { hash2[0], hash2[1], hash2[2], hash2[3] });
            Cache(guid, value);
            return guid;
        }

        /* Attempts to look up the string for a given ShortGuid */
        public static bool FindString(ShortGuid guid, out string value)
        {
            if (!guidCacheReversed.ContainsKey(guid))
            {
                value = guid.ToString();
                return false;
            }
            value = guidCacheReversed[guid];
            return true;
        }

        /* Cache a pre-generated ShortGuid */
        private static void Cache(ShortGuid guid, string value)
        {
            if (guidCache.ContainsKey(value)) return;
            guidCache.Add(value, guid);
            guidCacheReversed.Add(guid, value);
        }
    }

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
