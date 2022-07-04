using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CATHODE.Commands
{
    public static class ShortGuidUtils
    {
        private static ShortGuidTable vanilla = new ShortGuidTable();
        private static ShortGuidTable custom = new ShortGuidTable();

        private static CommandsPAK commandsPAK;

        /* Pull in strings we know are cached as ShortGuid in Cathode */
        static ShortGuidUtils(/*CommandsPAK pak = null*/)
        {
            /*
            commandsPAK = pak;
            commandsPAK.OnSaved += SaveCustomNames;
            */

            BinaryReader reader = new BinaryReader(new MemoryStream(CathodeLib.Properties.Resources.entity_parameter_names));
            while (reader.BaseStream.Position < reader.BaseStream.Length)
                Cache(new ShortGuid(reader), reader.ReadString(), true);
            reader.Close();

            LoadCustomNames();
        }

        /* Generate a ShortGuid to interface with the Cathode scripting system */
        public static ShortGuid Generate(string value)
        {
            if (vanilla.cache.ContainsKey(value)) return vanilla.cache[value];

            SHA1Managed sha1 = new SHA1Managed();
            byte[] hash1 = sha1.ComputeHash(Encoding.UTF8.GetBytes(value));
            //This is referred to as LongGuid - should we make a thing for it?
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
        public static string FindString(ShortGuid guid)
        {
            if (!vanilla.cacheReversed.ContainsKey(guid))
                return guid.ToString();

            //TODO: check custom cache

            return vanilla.cacheReversed[guid];
        }

        /* Cache a pre-generated ShortGuid */
        private static void Cache(ShortGuid guid, string value, bool isVanilla = false)
        {
            if (isVanilla)
            {
                if (vanilla.cache.ContainsKey(value)) return;
                vanilla.cache.Add(value, guid);
                vanilla.cacheReversed.Add(guid, value);
            }
            else
            {
                if (custom.cache.ContainsKey(value)) return;
                custom.cache.Add(value, guid);
                custom.cacheReversed.Add(guid, value);
            }
        }

        /* Pull non-vanilla ShortGuid from the CommandsPAK */
        private static void LoadCustomNames()
        {
            /*
            if (commandsPAK == null) return;
            int endPos = GetEndOfCommands();
            BinaryReader reader = new BinaryReader(File.OpenRead(commandsPAK.Filepath));
            reader.BaseStream.Position = endPos;
            if ((int)reader.BaseStream.Length - endPos == 0 || reader.ReadByte() != (byte)254)
            {
                custom_composites = new Dictionary<ShortGuid, Dictionary<ShortGuid, string>>();
                reader.Close();
                return;
            }
            reader.BaseStream.Position += 4;
            int compCount = reader.ReadInt32();
            reader.BaseStream.Position += 8;
            custom_composites = ConsumeDatabase(reader, compCount);
            reader.Close();
            */
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private static void SaveCustomNames()
        {
            /*
            if (commandsPAK == null) return;
            int endPos = GetEndOfCommands();
            //TODO: move this writing functionality into its own thing
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(commandsPAK.Filepath));
            bool hasAlreadyWritten = (int)writer.BaseStream.Length - endPos != 0;
            int posToJumpBackTo = 0;
            writer.BaseStream.Position = endPos;
            writer.Write((byte)254);
            if (hasAlreadyWritten)
            {
                //Get position here
                writer.BaseStream.Position += 8;
            }
            else
            {
                writer.Write(0);
                writer.Write(0);
            }
            posToJumpBackTo = (int)writer.BaseStream.Position;
            writer.Write(0);
            writer.Write(0);
            //Jump to got position here
            int posToWrite = (int)writer.BaseStream.Position;
            writer.BaseStream.Position = posToJumpBackTo;
            writer.Write(posToWrite);
            writer.Close();
            */
        }

        /* Get the position where we start writing our own data */
        private static int GetEndOfCommands()
        {
            BinaryReader reader = new BinaryReader(File.OpenRead(commandsPAK.Filepath));
            reader.BaseStream.Position = 20;
            int end_of_pak = (reader.ReadInt32() * 4) + (reader.ReadInt32() * 4);
            reader.Close();
            return end_of_pak;
        }

        private class ShortGuidTable
        {
            public Dictionary<string, ShortGuid> cache = new Dictionary<string, ShortGuid>();
            public Dictionary<ShortGuid, string> cacheReversed = new Dictionary<ShortGuid, string>();
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
