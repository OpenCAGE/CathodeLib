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

        /* Every ShortGuid that is already spoken for, so GenerateRandom never hands out one that is
         * in use. This used to be the name cache itself, which meant every random id ever minted was
         * written into the SHORT_GUIDS table - and since a save mints one per new entity, composite,
         * resource and link, an instanced save of Solace added ~7,300 entries to COMMANDS.PAK every
         * time, without bound. Nothing looks a random id up by name, and FindString falls back to the
         * byte string. Seeded instead from what a loaded script graph actually uses (ReserveInUse),
         * which covers more than the old table did - vanilla ids were never in it at all. */
        private static readonly HashSet<ShortGuid> _reserved = new HashSet<ShortGuid>();

        /* One hasher per thread rather than one per call. Generate is the library's name-to-id
         * function and gets called once per entry of several tables at load - a level with 1,379
         * material mappings spent 1.3s of its load in here, nearly all of it constructing a fresh
         * SHA1 for each name. */
        [ThreadStatic] private static SHA1 _hasher;

        private static readonly char[] HexDigits = "0123456789ABCDEF".ToCharArray();

        /// <summary>
        /// The ShortGuid of a string: SHA1, the first four words byte-reversed, then SHA1 again over
        /// the uppercase hex of that, and the first four bytes of the result.
        /// </summary>
        private static ShortGuid HashToShortGuid(string value)
        {
            SHA1 sha1 = _hasher;
            if (sha1 == null) sha1 = _hasher = SHA1.Create();

            byte[] hash1 = sha1.ComputeHash(Encoding.UTF8.GetBytes(value));
            byte[] arrangedHash = new byte[] {
                hash1[3], hash1[2], hash1[1], hash1[0],
                hash1[7], hash1[6], hash1[5], hash1[4],
                hash1[11], hash1[10], hash1[9], hash1[8],
                hash1[15], hash1[14], hash1[13], hash1[12]
            };

            //Same characters BitConverter.ToString(...).Replace("-", "") produced, without the two
            //throwaway strings it took to get there.
            char[] hex = new char[arrangedHash.Length * 2];
            for (int i = 0; i < arrangedHash.Length; i++)
            {
                hex[i * 2] = HexDigits[arrangedHash[i] >> 4];
                hex[i * 2 + 1] = HexDigits[arrangedHash[i] & 0xF];
            }

            byte[] hash2 = sha1.ComputeHash(Encoding.UTF8.GetBytes(new string(hex)));
            return new ShortGuid(new byte[] { hash2[0], hash2[1], hash2[2], hash2[3] });
        }
        // Instancing Parallel.For touches Generate/GenerateRandom concurrently on net8+.
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Generate a ShortGuid to interface with the Cathode scripting system
        /// </summary>
        public static ShortGuid Generate(string value, bool cache = true)
        {
            //A null name and an empty one are the same thing here: both get the guid of "", and both
            //cache under it. This is the only place a null is allowed in - everything downstream
            //(the cache, the tables that persist it) can then assume it has a string.
            if (value == null)
                value = string.Empty;

            lock (_cacheLock)
            {
                if (_custom.cache.TryGetValue(value, out ShortGuid customVal))
                    return customVal;
                if (CustomTable.Vanilla.ShortGuids.cache.TryGetValue(value, out ShortGuid vanillaVal))
                    return vanillaVal;
            }

            ShortGuid guid = HashToShortGuid(value);
            if (cache)
            {
                lock (_cacheLock)
                    CacheUnlocked(guid, value);
            }
            return guid;
        }

        /// <summary>
        /// Attempts to look up the string for a given ShortGuid
        /// </summary>
        public static string FindString(ShortGuid guid)
        {
            lock (_cacheLock)
            {
                if (_custom.cacheReversed.TryGetValue(guid, out string customVal))
                    return customVal;
                if (CustomTable.Vanilla.ShortGuids.cacheReversed.TryGetValue(guid, out string vanillaVal))
                    return vanillaVal;
            }

            return guid.ToByteString();
        }

        /// <summary>
        /// Generate a random unique ShortGuid
        /// </summary>
        public static ShortGuid GenerateRandom()
        {
            lock (_cacheLock)
            {
                string str = Guid.NewGuid().ToString();
                ShortGuid guid = GenerateUnlocked(str, cache: false);
                int s = 0;
                while (IsTakenUnlocked(guid))
                {
                    str = $"{str}_{s++}";
                    guid = GenerateUnlocked(str, cache: false);

                    if (s > 10000) throw new Exception("Failed to generate unique ShortGuid after many attempts.");
                }
                _reserved.Add(guid);
                return guid;
            }
        }

        /// <summary>
        /// Is this ShortGuid already in use? A guid derived from a name belongs to that name, so the
        /// name caches count as reservations too. Caller must hold <see cref="_cacheLock"/>.
        /// </summary>
        private static bool IsTakenUnlocked(ShortGuid guid)
        {
            return _reserved.Contains(guid)
                || _custom.cacheReversed.ContainsKey(guid)
                || CustomTable.Vanilla.ShortGuids.cacheReversed.ContainsKey(guid);
        }

        /// <summary>
        /// Mark ShortGuids as in use so <see cref="GenerateRandom"/> will not mint them again.
        /// Call this with everything a freshly loaded script graph already refers to.
        /// </summary>
        public static void ReserveInUse(IEnumerable<ShortGuid> guids)
        {
            if (guids == null)
                return;

            lock (_cacheLock)
            {
                foreach (ShortGuid guid in guids)
                    _reserved.Add(guid);
            }
        }

        /// <summary>
        /// Cache a pre-generated ShortGuid. Caller must hold <see cref="_cacheLock"/>.
        /// </summary>
        private static bool CacheUnlocked(ShortGuid guid, string value)
        {
            if (_custom.cache.ContainsKey(value)) return false;
            _custom.cache.Add(value, guid);
            //TODO: need to fix this for BSPNOSTROMO_RIPLEY_PATCH (?)
            //Two names can hash to one guid, and the second one just does not get a reverse entry.
            //This used to be an Add in a swallowing try/catch, and the exception it threw on every
            //collision cost more than the hashing did - 1,379 material mappings spent most of their
            //load being thrown.
            if (!_custom.cacheReversed.ContainsKey(guid))
                _custom.cacheReversed.Add(guid, value);
            return true;
        }

        /// <summary>
        /// Generate without taking <see cref="_cacheLock"/> (caller must hold it when cache lookups/writes are needed).
        /// Used by <see cref="GenerateRandom"/> which already holds the lock.
        /// </summary>
        private static ShortGuid GenerateUnlocked(string value, bool cache)
        {
            if (value == null)
                value = string.Empty;

            if (_custom.cache.TryGetValue(value, out ShortGuid customVal))
                return customVal;
            if (CustomTable.Vanilla.ShortGuids.cache.TryGetValue(value, out ShortGuid vanillaVal))
                return vanillaVal;

            ShortGuid guid = HashToShortGuid(value);
            if (cache)
                CacheUnlocked(guid, value);
            return guid;
        }

        #region Commands Linking
        /// <summary>
        /// Load/save custom shortguids
        /// </summary>
        internal static void LoadCustomNames(string filepath)
        {
            GuidNameTable guids = (GuidNameTable)CustomTable.ReadTable(filepath, CustomTableType.SHORT_GUIDS);
            if (guids == null)
                return;

            int added = 0;
            lock (_cacheLock)
            {
                foreach (KeyValuePair<string, ShortGuid> str in guids.cache)
                {
                    if (CacheUnlocked(str.Value, str.Key))
                        added++;
                }
            }
            Console.WriteLine("Loaded " + added + " ShortGuids!");
        }
        internal static void SaveCustomNames(string filepath)
        {
            lock (_cacheLock)
            {
                CustomTable.WriteTable(filepath, CustomTableType.SHORT_GUIDS, _custom);
                Console.WriteLine("Saved " + _custom.cache.Count + " ShortGuids!");
            }
        }
        #endregion
    }
}
