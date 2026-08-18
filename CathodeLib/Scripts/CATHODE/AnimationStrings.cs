using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CATHODE.Scripting;
using CathodeLib;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> ANIM_STRING_DB.BIN, ANIM_STRING_DB_DEBUG.BIN
    /// </summary>
    public class AnimationStrings : CathodeFile
    {
        public Dictionary<uint, string> Entries = new Dictionary<uint, string>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public AnimationStrings(string path) : base(path) { }
        public AnimationStrings(MemoryStream stream, string path = "") : base(stream, path) { }
        public AnimationStrings(byte[] data, string path = "") : base(data, path) { }

        ~AnimationStrings()
        {
            Entries.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            Entries.Clear();
            _stringOrder.Clear();
            _stringIndexByID.Clear();

            using (BinaryReader reader = new BinaryReader(stream))
            {
                int entryCount = reader.ReadInt32();
                int stringCount = reader.ReadInt32();
                Entry[] entries = Utilities.ConsumeArray<Entry>(reader, entryCount);
                int[] stringOffsets = Utilities.ConsumeArray<int>(reader, stringCount);

                int baseline = (entryCount * 4 * 2) + 8 + (stringCount * 4);

                for (int i = 0; i < stringCount; i++)
                    _stringOrder.Add(ReadRawString(reader, stringOffsets[i] + baseline));
                for (int i = 0; i < entries.Length; i++)
                {
                    Entries.Add(entries[i].StringID, _stringOrder[entries[i].StringIndex]);
                    _stringIndexByID.Add(entries[i].StringID, entries[i].StringIndex);
                }

            }
            return true;
        }

        override protected bool SaveInternal()
        {
            File.WriteAllBytes(_filepath, ToBytes());
            return true;
        }

        /// <summary>
        /// Serialise back to the format stored in ANIMATION.PAK.
        /// </summary>
        public byte[] ToBytes()
        {
            /* IDs are listed in descending order so the engine can binary search them, while the
             * strings themselves sit in their own order - preserved from load where possible so
             * an untouched DB saves back byte for byte. */
            List<string> strings = new List<string>(_stringOrder);
            var lookup = new List<KeyValuePair<uint, int>>(Entries.Count);
            foreach (KeyValuePair<uint, string> entry in Entries)
            {
                if (!_stringIndexByID.TryGetValue(entry.Key, out int index) || index >= strings.Count || strings[index] != entry.Value)
                {
                    index = strings.IndexOf(entry.Value);
                    if (index == -1) { index = strings.Count; strings.Add(entry.Value); }
                }
                lookup.Add(new KeyValuePair<uint, int>(entry.Key, index));
            }
            lookup.Sort((a, b) => b.Key.CompareTo(a.Key));

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(lookup.Count);
                writer.Write(strings.Count);
                for (int i = 0; i < lookup.Count; i++)
                {
                    writer.Write(lookup[i].Key);
                    writer.Write(lookup[i].Value);
                }

                int baseline = (lookup.Count * 4 * 2) + 8 + (strings.Count * 4);
                writer.BaseStream.Position = baseline;
                List<int> stringOffsets = new List<int>(strings.Count);
                for (int i = 0; i < strings.Count; i++)
                {
                    stringOffsets.Add((int)writer.BaseStream.Position - baseline);
                    WriteRawString(strings[i], writer);
                }

                writer.BaseStream.Position = (lookup.Count * 4 * 2) + 8;
                for (int i = 0; i < stringOffsets.Count; i++)
                    writer.Write(stringOffsets[i]);

                return stream.ToArray();
            }
        }

        /* A handful of entries hold bytes above 0x7F (mangled asset paths). Reading those as ASCII
         * turns them into '?' and loses the original bytes, so map byte <-> char directly. */
        private static string ReadRawString(BinaryReader reader, int position)
        {
            reader.BaseStream.Position = position;
            StringBuilder value = new StringBuilder();
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                byte b = reader.ReadByte();
                if (b == 0x00) break;
                value.Append((char)b);
            }
            return value.ToString();
        }

        private static void WriteRawString(string value, BinaryWriter writer)
        {
            for (int i = 0; i < value.Length; i++)
                writer.Write((byte)(value[i] & 0xFF));
            writer.Write((byte)0x00);
        }

        /* Kept from load so an unmodified DB round trips exactly - the string table isn't ordered
         * the same way as the ID table, and neither order is derivable from the other. */
        private List<string> _stringOrder = new List<string>();
        private Dictionary<uint, int> _stringIndexByID = new Dictionary<uint, int>();
        #endregion

        #region ACCESSORS
        /// <summary>
        /// Add a string to the DB (generates an ID)
        /// </summary>
        public void AddString(string str)
        {
            uint id = Utilities.AnimationHashedString(str);
            if (Entries.ContainsKey(id)) return;
            Entries.Add(id, str);
            _idsByString = null;
        }

        /// <summary>
        /// Remove a string from the DB
        /// </summary>
        public void RemoveString(string str)
        {
            uint id = Utilities.AnimationHashedString(str);
            Entries.Remove(id);
            _idsByString = null;
        }

        /// <summary>
        /// Get the string value for a given ID (if it doesn't exist, returns the ID as a string)
        /// </summary>
        public string GetString(uint id)
        {
            if (Entries.TryGetValue(id, out string s))
                return s;
            return id.ToString(); //Warn?
        }

        /// <summary>
        /// Get the ID for a given string, and caches it if it's new
        /// </summary>
        public uint GetID(string str)
        {
            uint id = Utilities.AnimationHashedString(str);
            if (Entries.TryGetValue(id, out string match) && match == str)
                return id;

            /* Some entries hold bytes that don't survive being read back as text, so hashing the
             * string we handed out won't find them again - look those up by value instead. */
            if (_idsByString == null)
            {
                _idsByString = new Dictionary<string, uint>();
                foreach (KeyValuePair<uint, string> entry in Entries)
                    if (entry.Value != null && !_idsByString.ContainsKey(entry.Value))
                        _idsByString.Add(entry.Value, entry.Key);
            }
            if (_idsByString.TryGetValue(str, out uint existing))
                return existing;

            /* GetString hands back the raw ID as text when it doesn't know a hash, so turn that
             * back into the ID rather than hashing the digits. */
            if (uint.TryParse(str, out uint literal) && !Entries.ContainsKey(literal))
                return literal;

            /* The hash is what actually gets written, so an unknown name still round-trips. Adding
             * it here would quietly grow the table every time a file is serialised - anything that
             * wants a new name kept has to call AddString for it. */
            return id;
        }

        private Dictionary<string, uint> _idsByString = null;
        #endregion

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Entry
        {
            public uint StringID;
            public int StringIndex;
        };
        #endregion
    }
}
