﻿using CATHODE.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

namespace CathodeLib
{
    //Has a store of all composite paths in the vanilla game: used for prettifying the all-caps Windows strings
    public static class CompositeUtils
    {
        private static Dictionary<ShortGuid, string> pathLookup;

        static CompositeUtils()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            BinaryReader reader = new BinaryReader(File.OpenRead(Application.streamingAssetsPath + "/NodeDBs/composite_paths.bin"));
#else
            BinaryReader reader = new BinaryReader(new MemoryStream(CathodeLib.Properties.Resources.composite_paths));
#endif
            int compositeCount = reader.ReadInt32();
            pathLookup = new Dictionary<ShortGuid, string>(compositeCount);
            for (int i = 0; i < compositeCount; i++)
                pathLookup.Add(Utilities.Consume<ShortGuid>(reader), reader.ReadString());
        }

        public static string GetFullPath(ShortGuid guid)
        {
            return pathLookup.ContainsKey(guid) ? pathLookup[guid] : "";
        }

        public static string GetPrettyPath(ShortGuid guid)
        {
            string fullPath = GetFullPath(guid);
            if (fullPath.Length < 1) return "";
            string first25 = fullPath.Substring(0, 25);
            switch (first25)
            {
                case @"N:\Content\Build\Library\":
                    return fullPath.Substring(25);
                case @"N:\Content\Build\Levels\P":
                    return fullPath.Substring(17);
            }
            return fullPath;
        }
    }
}
