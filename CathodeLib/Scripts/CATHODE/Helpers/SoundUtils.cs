using CATHODE.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using static CATHODE.AnimClipDB;
namespace CathodeLib
{
    public static class SoundUtils
    {
        private static Dictionary<uint, string> nameLookup;

        static SoundUtils()
        {
            byte[] content = null;
            if (File.Exists(Paths.CustomSoundBin))
                content = File.ReadAllBytes(Paths.CustomSoundBin);

            if (content == null)
                return;

            using (MemoryStream stream = new MemoryStream())
            using (GZipStream compressedStream = new GZipStream(new MemoryStream(content), CompressionMode.Decompress))
            {
                compressedStream.CopyTo(stream);
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    reader.BaseStream.Position = 0;
                    int count = reader.ReadInt32();
                    nameLookup = new Dictionary<uint, string>(count);
                    for (int i = 0; i < count; i++)
                        nameLookup.Add(reader.ReadUInt32(), Utilities.ReadString(reader));
                }
            }
        }

        public static string GetSoundName(uint id)
        {
            if (nameLookup.TryGetValue(id, out string name))
                return name;
            return id.ToString();
        }

        public static uint GetSoundID(string name)
        {
            foreach (var entry in nameLookup)
                if (entry.Value == name)
                    return entry.Key;
            return 0;
        }
    }
}
