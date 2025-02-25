//#define DO_DEBUG_DUMP

using CATHODE;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

namespace CathodeLib
{
    public static class CompositeUtils
    {
        //Has a store of all composite paths in the vanilla game: can be used for prettifying the all-caps Windows strings
        private static Dictionary<ShortGuid, string> _pathLookup;

        //Has a store of Composite modification info for the currently linked Commands
        private static CompositeModificationInfoTable _modificationInfo;

        public static Commands LinkedCommands => _commands;
        private static Commands _commands;

        static CompositeUtils()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            byte[] dbContent = File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/composite_paths.bin");
#else
            byte[] dbContent = CathodeLib.Properties.Resources.composite_paths;
            if (File.Exists("LocalDB/composite_paths.bin"))
                dbContent = File.ReadAllBytes("LocalDB/composite_paths.bin");
#endif
            BinaryReader reader = new BinaryReader(new MemoryStream(dbContent));
            int compositeCount = reader.ReadInt32();
            _pathLookup = new Dictionary<ShortGuid, string>(compositeCount);
            for (int i = 0; i < compositeCount; i++)
                _pathLookup.Add(Utilities.Consume<ShortGuid>(reader), reader.ReadString());

#if DO_DEBUG_DUMP
            Directory.CreateDirectory("DebugDump");
            List<string> paths = new List<string>();
            foreach (KeyValuePair<ShortGuid, string> value in _pathLookup)
            {
                paths.Add(value.Value);
            }
            paths.Sort();
            File.WriteAllLines("DebugDump/paths.txt", paths);
#endif

            _modificationInfo = new CompositeModificationInfoTable();
        }

        public static void LinkCommands(Commands commands)
        {
            if (_commands != null)
            {
                _commands.OnLoadSuccess -= LoadModificationInfo;
                _commands.OnSaveSuccess -= SaveModificationInfo;
            }

            _commands = commands;
            if (_commands == null) return;

            _commands.OnLoadSuccess += LoadModificationInfo;
            _commands.OnSaveSuccess += SaveModificationInfo;

            LoadModificationInfo(_commands.Filepath);
        }

        private static void LoadModificationInfo(string filepath)
        {
            _modificationInfo = (CompositeModificationInfoTable)CustomTable.ReadTable(filepath, CustomEndTables.COMPOSITE_MODIFICATION_INFO);
            if (_modificationInfo == null) _modificationInfo = new CompositeModificationInfoTable();
            Console.WriteLine("Loaded modification info for " + _modificationInfo.modification_info.Count + " composites!");
        }

        private static void SaveModificationInfo(string filepath)
        {
            CustomTable.WriteTable(filepath, CustomEndTables.COMPOSITE_MODIFICATION_INFO, _modificationInfo);
            Console.WriteLine("Saved modification info for " + _modificationInfo.modification_info.Count + " composites!");
        }

        /* Gets a pretty Composite name */
        public static string GetFullPath(ShortGuid guid)
        {
            return _pathLookup.ContainsKey(guid) ? _pathLookup[guid] : "";
        }

        /* Gets a pretty Composite name, including trimming direct paths */
        public static string GetPrettyPath(ShortGuid guid)
        {
            string fullPath = GetFullPath(guid);
            if (fullPath.Length < 1) return "";
            string first25 = fullPath.Substring(0, 25).ToUpper();
            switch (first25)
            {
                case @"N:\CONTENT\BUILD\LIBRARY\":
                    return fullPath.Substring(25);
                case @"N:\CONTENT\BUILD\LEVELS\P":
                    return fullPath.Substring(17);
            }
            return fullPath;
        }

        /* Set/update the modification metadata for a composite */
        public static void SetModificationInfo(CompositeModificationInfoTable.ModificationInfo info)
        {
            _modificationInfo.modification_info.RemoveAll(o => o.composite_id == info.composite_id);
            _modificationInfo.modification_info.Add(info);
        }

        /* Get the modification metadata for a composite (if it exists) */
        public static CompositeModificationInfoTable.ModificationInfo GetModificationInfo(Composite composite)
        {
            return _modificationInfo.modification_info.FirstOrDefault(o => o.composite_id == composite.shortGUID);
        }

        /* Generate a checksum for a Composite object */
        public static byte[] GenerateChecksum(Composite composite)
        {
            int size = Marshal.SizeOf(composite);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size); 
            try
            {
                Marshal.StructureToPtr(composite, ptr, true);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(arr);
                return hash;
            }
        }

        /* Remove all links between Entities within the Composite */
        public static void ClearAllLinks(Composite composite)
        {
            composite.GetEntities().ForEach(o => o.childLinks.Clear());
        }

        /* Count the number of links in the Composite */
        public static int CountLinks(Composite composite)
        {
            int count = 0;
            List<Entity> entities = composite.GetEntities();
            foreach (Entity ent in entities) 
                count += ent.childLinks.Count;
            return count;
        }
    }
}
