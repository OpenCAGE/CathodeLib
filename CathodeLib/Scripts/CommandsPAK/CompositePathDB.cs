using CATHODE.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CathodeLib
{
    //Has a store of all composite paths in the vanilla game: used for prettifying the all-caps Windows strings
    public static class CompositePathDB
    {
        private static Dictionary<ShortGuid, string> pathLookup;

        static CompositePathDB()
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(Properties.Resources.composite_paths));
            int compositeCount = reader.ReadInt32();
            pathLookup = new Dictionary<ShortGuid, string>(compositeCount);
            for (int i = 0; i < compositeCount; i++)
                pathLookup.Add(CATHODE.Utilities.Consume<ShortGuid>(reader), reader.ReadString());
        }

        public static string GetFullPathForComposite(ShortGuid guid)
        {
            return pathLookup.ContainsKey(guid) ? pathLookup[guid] : "";
        }

        public static string GetPrettyPathForComposite(ShortGuid guid)
        {
            string fullPath = GetFullPathForComposite(guid);
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

        private struct CompositePath
        {
            public ShortGuid id;
            public string path;
        }
    }
}
