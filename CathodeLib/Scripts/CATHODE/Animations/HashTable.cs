using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CATHODE.Animations
{
    /// <summary>
    /// The lookup table the animation system uses everywhere in ANIMATION.PAK.
    ///
    /// Layout is: count, count, then that many (nameHash, dataIndex) pairs sorted by hash descending
    /// (the engine binary-searches them), then the data records themselves in dataIndex order.
    /// </summary>
    public static class HashTable
    {
        /// <summary>
        /// Read a table. Pass <paramref name="slotOrder"/> to capture the order the lookup pairs were
        /// stored in - a few tables list the same name twice, and the tie order isn't derivable.
        /// </summary>
        public static List<T> Read<T>(BinaryReader reader, Func<BinaryReader, string, T> itemReader, AnimationStrings strings, List<int> slotOrder = null)
        {
            var result = new List<T>();

            int hashTableSize = reader.ReadInt32();
            int usedSize = reader.ReadInt32();

            if (hashTableSize != usedSize)
                throw new Exception("Unexpected");

            string[] names = new string[hashTableSize];
            for (int i = 0; i < hashTableSize; i++)
            {
                uint hash = reader.ReadUInt32();
                int index = reader.ReadInt32();
                names[index] = strings.GetString(hash);
                slotOrder?.Add(index);
            }

            for (int i = 0; i < hashTableSize; i++)
            {
                result.Add(itemReader(reader, names[i]));
            }

            return result;
        }

        /// <summary>
        /// Write a table, hashing each entry's name via <paramref name="nameSelector"/>. The data records
        /// keep the order of <paramref name="data"/>, and the lookup pairs are sorted to match the engine.
        /// </summary>
        public static void Write<T>(BinaryWriter writer, List<T> data, Func<T, string> nameSelector, Action<BinaryWriter, T> itemWriter, AnimationStrings strings, List<int> slotOrder = null)
        {
            writer.Write(data.Count);
            writer.Write(data.Count);

            WriteLookup(writer, data, nameSelector, strings, slotOrder);

            for (int i = 0; i < data.Count; i++)
                itemWriter(writer, data[i]);
        }

        /// <summary>
        /// Write just the (nameHash, dataIndex) pairs, for the few tables whose header or record layout differs.
        /// </summary>
        public static void WriteLookup<T>(BinaryWriter writer, List<T> data, Func<T, string> nameSelector, AnimationStrings strings, List<int> slotOrder = null)
        {
            //Reuse the load order when it still describes this data, so an untouched file saves back exactly
            List<int> order = slotOrder != null && slotOrder.Count == data.Count ? slotOrder : null;
            if (order == null)
            {
                var pairs = new List<KeyValuePair<uint, int>>(data.Count);
                for (int i = 0; i < data.Count; i++)
                    pairs.Add(new KeyValuePair<uint, int>(HashOf(nameSelector(data[i]), strings), i));
                pairs.Sort((a, b) => b.Key.CompareTo(a.Key));
                order = pairs.Select(x => x.Value).ToList();
            }

            for (int i = 0; i < order.Count; i++)
            {
                writer.Write(order[i] >= 0 && order[i] < data.Count ? HashOf(nameSelector(data[order[i]]), strings) : 0u);
                writer.Write(order[i]);
            }
        }

        /// <summary>
        /// Hash a name for the table, tolerating the nulls that come out of a partly built table.
        /// </summary>
        public static uint HashOf(string name, AnimationStrings strings)
        {
            if (name == null) return 0;
            return strings == null ? Utilities.AnimationHashedString(name) : strings.GetID(name);
        }
    }
}
