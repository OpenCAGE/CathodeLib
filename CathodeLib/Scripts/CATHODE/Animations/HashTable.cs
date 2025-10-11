using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CATHODE.Animations
{
    public static class HashTable
    {
        public static List<T> Read<T>(BinaryReader reader, Func<BinaryReader, string, T> itemReader, AnimationStrings strings)
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
            }

            for (int i = 0; i < hashTableSize; i++)
            {
                result.Add(itemReader(reader, names[i]));
            }

            return result;
        }

        //temp!!!
        public static void Write<T>(BinaryWriter writer, List<T> data, Action<BinaryWriter, T> itemWriter, AnimationStrings strings)
        {
            writer.Write(data.Count);
            writer.Write(data.Count);

            // Write hash table entries
            for (int i = 0; i < data.Count; i++)
            {
                writer.Write(Utilities.AnimationHashedString($"item_{i}")); // Generate hash
                writer.Write(i);
            }

            // Write data entries
            foreach (var item in data)
            {
                itemWriter(writer, item);
            }
        }
    }
}
