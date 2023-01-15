using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

namespace CATHODE.Scripting
{
    public static class ShortGuidUtils
    {
        private static GuidNameTable _vanilla = null;
        private static GuidNameTable _custom = null;

        private static Commands _commands;

        /* Pull in strings we know are cached as ShortGuid in Cathode */
        static ShortGuidUtils()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            BinaryReader reader = new BinaryReader(File.OpenRead(Application.streamingAssetsPath + "/NodeDBs/entity_parameter_names.bin"));
#else
            BinaryReader reader = new BinaryReader(new MemoryStream(CathodeLib.Properties.Resources.entity_parameter_names));
#endif
            _vanilla = new GuidNameTable();
            _custom = new GuidNameTable();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
                Cache(new ShortGuid(reader), reader.ReadString(), true);
            reader.Close();
        }

        /* Optionally, link a Commands file which can be used to save custom ShortGuids to */
        public static void LinkCommands(Commands commands)
        {
            if (_commands != null)
                _commands.OnSaved -= SaveCustomNames;

            _commands = commands;
            if (_commands != null)
                _commands.OnSaved += SaveCustomNames;

            LoadCustomNames(commands.Filepath);
        }

        /* Generate a ShortGuid to interface with the Cathode scripting system */
        public static ShortGuid Generate(string value)
        {
            return Generate(value, true);
        }
        private static ShortGuid Generate(string value, bool cache = true)
        {
            if (_vanilla.cache.ContainsKey(value)) return _vanilla.cache[value];

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
            if (cache) Cache(guid, value);
            return guid;
        }

        /* Attempts to look up the string for a given ShortGuid */
        public static string FindString(ShortGuid guid)
        {
            if (_custom.cacheReversed.ContainsKey(guid))
                return _custom.cacheReversed[guid];
            if (_vanilla.cacheReversed.ContainsKey(guid))
                return _vanilla.cacheReversed[guid];

            return guid.ToByteString();
        }

        /* Generate a random unique ShortGuid */
        public static ShortGuid GenerateRandom()
        {
            string str = DateTime.Now.ToString("G");
            ShortGuid guid = Generate(str, false);
            int i = 0;
            int c_i = 0;
            while (_vanilla.cache.ContainsKey(str) || _custom.cache.ContainsKey(str) || _vanilla.cacheReversed.ContainsKey(guid) || _custom.cacheReversed.ContainsKey(guid))
            {
                str = guid.ToByteString();
                if (i > 1000)
                {
                    str += (char)c_i;
                    c_i++;
                    i = 0;
                }
                guid = Generate(str, false);
                i++;
            }
            Cache(guid, str);
            return guid;
        }

        /* Cache a pre-generated ShortGuid */
        private static void Cache(ShortGuid guid, string value, bool isVanilla = false)
        {
            if (isVanilla)
            {
                if (_vanilla.cache.ContainsKey(value)) return;
                _vanilla.cache.Add(value, guid);
                _vanilla.cacheReversed.Add(guid, value);
            }
            else
            {
                if (_custom.cache.ContainsKey(value)) return;
                _custom.cache.Add(value, guid);
                _custom.cacheReversed.Add(guid, value);
            }
        }

        /* Pull non-vanilla ShortGuid from the CommandsPAK */
        private static void LoadCustomNames(string filepath)
        {
            _custom = (GuidNameTable)CustomTable.ReadTable(filepath, CustomEndTables.SHORT_GUIDS);
            if (_custom == null) _custom = new GuidNameTable();
            Console.WriteLine("Loaded " + _custom.cache.Count + " custom ShortGuids!");
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private static void SaveCustomNames(string filepath)
        {
            CustomTable.WriteTable(filepath, CustomEndTables.SHORT_GUIDS, _custom);
            Console.WriteLine("Saved " + _custom.cache.Count + " custom ShortGuids!");
        }
    }
}
