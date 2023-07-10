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
