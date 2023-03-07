using CATHODE.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

namespace CathodeLib
{
    public static class SoundUtils
    {
        private static Dictionary<uint, string> nameLookup;

        static SoundUtils()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            BinaryReader reader = new BinaryReader(File.OpenRead(Application.streamingAssetsPath + "/sound_names.bin"));
#else
            BinaryReader reader = new BinaryReader(new MemoryStream(CathodeLib.Properties.Resources.sound_names));
#endif
            int count = reader.ReadInt32();
            nameLookup = new Dictionary<uint, string>(count);
            for (int i = 0; i < count; i++)
                nameLookup.Add(reader.ReadUInt32(), Utilities.ReadString(reader));
        }

        public static string GetSoundName(uint id)
        {
            return nameLookup.ContainsKey(id) ? nameLookup[id] : id.ToString();
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
