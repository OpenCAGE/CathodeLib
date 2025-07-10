using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
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
        private static GuidNameTable _custom = new GuidNameTable();

        public static Commands LinkedCommands => _commands;
        private static Commands _commands;

        /* Optionally, link a Commands file which can be used to save custom ShortGuids to */
        public static void LinkCommands(Commands commands)
        {
            if (_commands != null)
                _commands.OnSaveSuccess -= SaveCustomNames;

            _commands = commands;
            if (_commands == null) return;

            _commands.OnSaveSuccess += SaveCustomNames;

            LoadCustomNames(commands.Filepath);
        }

        /* Generate a ShortGuid to interface with the Cathode scripting system */
        public static ShortGuid Generate(string value, bool cache = true)
        {
            if (_custom.cache.TryGetValue(value, out ShortGuid customVal))
                return customVal;
            if (CustomTable.Vanilla.ShortGuids.cache.TryGetValue(value, out ShortGuid vanillaVal)) 
                return vanillaVal;

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
            if (_custom.cacheReversed.TryGetValue(guid, out string customVal))
                return customVal;
            if (CustomTable.Vanilla.ShortGuids.cacheReversed.TryGetValue(guid, out string vanillaVal))
                return vanillaVal;

            return guid.ToByteString();
        }

        /* Generate a random unique ShortGuid */
        public static ShortGuid GenerateRandom()
        {
            string str = Guid.NewGuid().ToString();
            ShortGuid guid = Generate(str, false);
            int s = 0;
            while (CustomTable.Vanilla.ShortGuids.cache.ContainsKey(str) || _custom.cache.ContainsKey(str) || 
                   CustomTable.Vanilla.ShortGuids.cacheReversed.ContainsKey(guid) || _custom.cacheReversed.ContainsKey(guid))
            {
                str = $"{str}_{s++}";
                guid = Generate(str, false);

                if (s > 10000) throw new Exception("Failed to generate unique ShortGuid after many attempts.");
            }
            Cache(guid, str);
            return guid;
        }

        /* Cache a pre-generated ShortGuid */
        private static void Cache(ShortGuid guid, string value)
        {
            //TODO: need to fix this for BSPNOSTROMO_RIPLEY_PATCH (?)
            if (_custom.cache.ContainsKey(value)) return;
            _custom.cache.Add(value, guid);
            try
            {
                _custom.cacheReversed.Add(guid, value);
            }
            catch { }
        }

        /* Pull non-vanilla ShortGuid from the CommandsPAK */
        private static void LoadCustomNames(string filepath)
        {
            _custom = (GuidNameTable)CustomTable.ReadTable(filepath, CustomTableType.SHORT_GUIDS);
            if (_custom == null) _custom = new GuidNameTable();
            Console.WriteLine("Loaded " + _custom.cache.Count + " custom ShortGuids!");
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private static void SaveCustomNames(string filepath)
        {
            CustomTable.WriteTable(filepath, CustomTableType.SHORT_GUIDS, _custom);
            Console.WriteLine("Saved " + _custom.cache.Count + " custom ShortGuids!");
        }
    }
}
