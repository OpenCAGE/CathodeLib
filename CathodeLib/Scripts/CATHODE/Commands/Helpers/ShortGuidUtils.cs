using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

[assembly: InternalsVisibleTo("CATHODE.Scripting")]
namespace CATHODE.Scripting
{
    public static class ShortGuidUtils
    {
        private static GuidNameTable _custom = new GuidNameTable();

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
        private static bool Cache(ShortGuid guid, string value)
        {
            if (_custom.cache.ContainsKey(value)) return false;
            _custom.cache.Add(value, guid);
            try
            {
                //TODO: need to fix this for BSPNOSTROMO_RIPLEY_PATCH (?)
                _custom.cacheReversed.Add(guid, value);
            }
            catch { }
            return true;
        }

        #region Commands Linking
        /* Load/save custom shortguids */
        internal static void LoadCustomNames(string filepath)
        {
            GuidNameTable guids = (GuidNameTable)CustomTable.ReadTable(filepath, CustomTableType.SHORT_GUIDS);
            if (guids == null)
                return;

            int added = 0;
            foreach (KeyValuePair<string, ShortGuid> str in guids.cache)
            {
                if (Cache(str.Value, str.Key))
                    added++;
            }
            Console.WriteLine("Loaded " + added + " ShortGuids!");
        }
        internal static void SaveCustomNames(string filepath)
        {
            CustomTable.WriteTable(filepath, CustomTableType.SHORT_GUIDS, _custom);
            Console.WriteLine("Saved " + _custom.cache.Count + " ShortGuids!");
        }
        #endregion
    }
}
