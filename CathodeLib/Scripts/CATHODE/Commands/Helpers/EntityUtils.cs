//#define DO_DEBUG_DUMP

using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

namespace CATHODE.Scripting
{
    //This serves as a helpful extension to manage entity names, and get FunctionEntity metadata
    public static class EntityUtils
    {
        private static EntityNameTable _custom = new EntityNameTable();

        //TODO: this should be moved to a central location which passes it to all utils.
        public static Commands LinkedCommands => _commands;
        private static Commands _commands;

        //For testing
        public static List<string> GetAllVanillaNames()
        {
            List<string> names = new List<string>();
            foreach (var entry in CustomTable.Vanilla.EntityNames.names)
            {
                foreach (var entry2 in entry.Value)
                {
                    names.Add(entry2.Value);
                }
            }
            return names;
        }

        /* Optionally, link a Commands file which can be used to save custom entity names to */
        public static void LinkCommands(Commands commands)
        {
            if (_commands != null)
            {
                _commands.OnLoadSuccess -= LoadCustomNames;
                _commands.OnSaveSuccess -= SaveCustomNames;
            }

            _commands = commands;
            if (_commands == null) return;

            _commands.OnLoadSuccess += LoadCustomNames;
            _commands.OnSaveSuccess += SaveCustomNames;

            LoadCustomNames(_commands.Filepath);
        }

        /* Get the name of an entity contained within a composite */
        public static string GetName(Composite composite, Entity entity)
        {
            return GetName(composite.shortGUID, entity.shortGUID);
        }
        public static string GetName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (_custom.names.TryGetValue(compositeID, out Dictionary<ShortGuid, string> customComposite))
                if (customComposite.TryGetValue(entityID, out string customName))
                    return customName;
            if (CustomTable.Vanilla.EntityNames.names.TryGetValue(compositeID, out Dictionary<ShortGuid, string> vanillaComposite))
                if (vanillaComposite.TryGetValue(entityID, out string vanillaName))
                    return vanillaName;
            return entityID.ToByteString();
        }

        /* Set the name of an entity contained within a composite */
        public static void SetName(Composite composite, Entity entity, string name)
        {
            SetName(composite.shortGUID, entity.shortGUID, name);
        }
        public static void SetName(ShortGuid compositeID, ShortGuid entityID, string name)
        {
            if (!_custom.names.ContainsKey(compositeID))
                _custom.names.Add(compositeID, new Dictionary<ShortGuid, string>());

            if (!_custom.names[compositeID].ContainsKey(entityID))
                _custom.names[compositeID].Add(entityID, name);
            else
                _custom.names[compositeID][entityID] = name;
        }

        /* Clear the name of an entity contained within a composite */
        public static void ClearName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (_custom.names.ContainsKey(compositeID))
                _custom.names[compositeID].Remove(entityID);
        }

        /* Pull non-vanilla entity names from the CommandsPAK */
        private static void LoadCustomNames(string filepath)
        {
            _custom = (EntityNameTable)CustomTable.ReadTable(filepath, CustomEndTables.ENTITY_NAMES);
            if (_custom == null) _custom = new EntityNameTable();
            Console.WriteLine("Loaded " + _custom.names.Count + " custom entity names!");
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private static void SaveCustomNames(string filepath)
        {
            CustomTable.WriteTable(filepath, CustomEndTables.ENTITY_NAMES, _custom);
            Console.WriteLine("Saved " + _custom.names.Count + " custom entity names!");
        }
    }
}
