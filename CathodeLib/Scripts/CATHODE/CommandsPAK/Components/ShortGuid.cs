#if DEBUG
using Newtonsoft.Json;
#endif
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CATHODE.Scripting
{
    /* A unique id assigned to CATHODE objects */
    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
#if DEBUG
    [JsonConverter(typeof(ShortGuidConverter))]
#endif
    public struct ShortGuid : IComparable<ShortGuid>
    {
        public static readonly ShortGuid Invalid = new ShortGuid(0);
        public static readonly ShortGuid InitialiserBase = new ShortGuid(1257266174); //"FE-5B-F0-4A"
        public static readonly ShortGuid Max = new ShortGuid(4294967295); //"FF-FF-FF-FF"

        public bool IsInvalid => val == Invalid.val;

        private UInt32 val;

        public ShortGuid(BinaryReader reader)
        {
            val = reader.ReadUInt32();
        }
        public ShortGuid(float num)
        {
            val = Convert.ToUInt32(num);
        }
        public ShortGuid(uint num)
        {
            val = num;
        }

        public ShortGuid(byte[] id)
        {
            val = BitConverter.ToUInt32(id, 0);
        }

        public ShortGuid(string id)
        {
            System.String[] arr = id.Split('-');
            if (arr.Length != 4) throw new Exception("Tried to initialise ShortGuid without 4-byte ID string.");
            byte[] array = new byte[arr.Length];
            for (int i = 0; i < arr.Length; i++) array[i] = Convert.ToByte(arr[i], 16);
            val = BitConverter.ToUInt32(array, 0);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is ShortGuid)) return false;
            return ((ShortGuid)obj).val == this.val;
        }
        public static bool operator ==(ShortGuid x, ShortGuid y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            return x.val == y.val;
        }
        public static bool operator !=(ShortGuid x, ShortGuid y)
        {
            return !(x.val == y.val);
        }

        public static bool operator ==(ShortGuid x, string y)
        {
            return x.ToByteString() == y;
        }

        public static bool operator !=(ShortGuid x, string y)
        {
            return x.ToByteString() != y;
        }

        public static bool operator ==(ShortGuid x, uint y)
        {
            return x.ToUInt32() == y;
        }
        public static bool operator !=(ShortGuid x, uint y)
        {
            return x.ToUInt32() != y;
        }

        public override int GetHashCode()
        {
            return 1835847388 + val.GetHashCode();
        }

        public int CompareTo(ShortGuid x)
        {
            if (x == null) return 1;

            if (this.val > x.val)
                return 1;
            else if (this.val < x.val)
                return -1;

            return 0;
        }

        public override string ToString()
        {
            return ShortGuidUtils.FindString(this);
        }
        public uint ToUInt32()
        {
            return val;
        }

        public string ToByteString()
        {
            return BitConverter.ToString(BitConverter.GetBytes(val));
        }

        public byte[] ToBytes()
        {
            return BitConverter.GetBytes(val);
        }
    }

#if DEBUG
    public class ShortGuidConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            ShortGuid user = (ShortGuid)value;
            writer.WriteValue(user.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return new ShortGuid((byte[])reader.Value);
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ShortGuid);
        }
    }
#endif
}
