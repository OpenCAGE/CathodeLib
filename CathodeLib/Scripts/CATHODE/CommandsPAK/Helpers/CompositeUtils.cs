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
using static CathodeLib.CompositePinInfoTable;
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

        //Has metadata for the composite pins, to let you figure if they're in or out (you should update this with any you add, if you're using this info)
        private static CompositePinInfoTable _pinInfoCustom;
        private static CompositePinInfoTable _pinInfoVanilla;

        public static Commands LinkedCommands => _commands;
        private static Commands _commands;

        static CompositeUtils()
        {
            _pinInfoVanilla = new CompositePinInfoTable();
        }

        public static void LinkCommands(Commands commands)
        {
            if (_commands != null)
            {
                _commands.OnLoadSuccess -= LoadInfo;
                _commands.OnSaveSuccess -= SaveInfo;
            }

            _commands = commands;
            if (_commands == null) return;

            _commands.OnLoadSuccess += LoadInfo;
            _commands.OnSaveSuccess += SaveInfo;

            LoadInfo(_commands.Filepath);
        }

        private static void LoadInfo(string filepath)
        {
            _modificationInfo = (CompositeModificationInfoTable)CustomTable.ReadTable(filepath, CustomEndTables.COMPOSITE_MODIFICATION_INFO);
            if (_modificationInfo == null) _modificationInfo = new CompositeModificationInfoTable();
            Console.WriteLine("Loaded modification info for " + _modificationInfo.modification_info.Count + " composites!");

            _pinInfoCustom = (CompositePinInfoTable)CustomTable.ReadTable(filepath, CustomEndTables.COMPOSITE_PIN_INFO);
            if (_pinInfoCustom == null) _pinInfoCustom = new CompositePinInfoTable();
            Console.WriteLine("Loaded custom pin info for " + _pinInfoCustom.composite_pin_infos.Count + " composites!");
        }

        private static void SaveInfo(string filepath)
        {
            CustomTable.WriteTable(filepath, CustomEndTables.COMPOSITE_MODIFICATION_INFO, _modificationInfo);
            Console.WriteLine("Saved modification info for " + _modificationInfo.modification_info.Count + " composites!");

            CustomTable.WriteTable(filepath, CustomEndTables.COMPOSITE_PIN_INFO, _pinInfoCustom);
            Console.WriteLine("Saved custom pin info for " + _pinInfoCustom.composite_pin_infos.Count + " composites!");
        }

        /* Gets a pretty Composite name */
        public static string GetFullPath(ShortGuid guid)
        {
            if (_pathLookup.TryGetValue(guid, out string toReturn))
                return toReturn;
            return "";
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
            return GetModificationInfo(composite.shortGUID);
        }
        public static CompositeModificationInfoTable.ModificationInfo GetModificationInfo(ShortGuid composite)
        {
            return _modificationInfo.modification_info.FirstOrDefault(o => o.composite_id == composite);
        }

        /* Set/update the pin info for a composite VariableEntity */
        public static void SetParameterInfo(Composite composite, CompositePinInfoTable.PinInfo info)
        {
            SetParameterInfo(composite.shortGUID, info);
        }
        public static void SetParameterInfo(ShortGuid composite, CompositePinInfoTable.PinInfo info)
        {
            List<CompositePinInfoTable.PinInfo> infos;
            if (!_pinInfoCustom.composite_pin_infos.TryGetValue(composite, out infos))
            {
                infos = new List<CompositePinInfoTable.PinInfo>();
                _pinInfoCustom.composite_pin_infos.Add(composite, infos);
            }

            infos.RemoveAll(o => o.VariableGUID == info.VariableGUID);
            infos.Add(info);
        }

        /* Get the pin info for a composite VariableEntity */
        //TODO: move this to ParameterUtils
        public static CompositePinInfoTable.PinInfo GetParameterInfo(Composite composite, VariableEntity variableEnt)
        {
            return GetParameterInfo(composite.shortGUID, variableEnt.shortGUID);
        }
        public static CompositePinInfoTable.PinInfo GetParameterInfo(ShortGuid composite, ShortGuid variableEnt)
        {
            CompositePinInfoTable.PinInfo info = null;
            if (_pinInfoCustom.composite_pin_infos.TryGetValue(composite, out List<CompositePinInfoTable.PinInfo> customInfos))
                info = customInfos.FirstOrDefault(o => o.VariableGUID == variableEnt);
            if (info != null)
                return info;
            if (_pinInfoVanilla.composite_pin_infos.TryGetValue(composite, out List<CompositePinInfoTable.PinInfo> vanillaInfos))
                info = vanillaInfos.FirstOrDefault(o => o.VariableGUID == variableEnt);
            return info;
        }

        public static ParameterVariant PinTypeToParameterVariant(ShortGuid type)
        {
            return PinTypeToParameterVariant((CompositePinType)type.ToUInt32());
        }
        public static ParameterVariant PinTypeToParameterVariant(CompositePinType type)
        {
            switch (type)
            {

                case CompositePinType.CompositeInputAnimationInfoVariablePin:
                case CompositePinType.CompositeInputBoolVariablePin:
                case CompositePinType.CompositeInputDirectionVariablePin:
                case CompositePinType.CompositeInputFloatVariablePin:
                case CompositePinType.CompositeInputIntVariablePin:
                case CompositePinType.CompositeInputObjectVariablePin:
                case CompositePinType.CompositeInputPositionVariablePin:
                case CompositePinType.CompositeInputStringVariablePin:
                case CompositePinType.CompositeInputVariablePin:
                case CompositePinType.CompositeInputZoneLinkPtrVariablePin:
                case CompositePinType.CompositeInputZonePtrVariablePin:
                case CompositePinType.CompositeInputEnumVariablePin:
                case CompositePinType.CompositeInputEnumStringVariablePin:
                    return ParameterVariant.INPUT_PIN;
                    break;
                case CompositePinType.CompositeOutputAnimationInfoVariablePin:
                case CompositePinType.CompositeOutputBoolVariablePin:
                case CompositePinType.CompositeOutputDirectionVariablePin:
                case CompositePinType.CompositeOutputFloatVariablePin:
                case CompositePinType.CompositeOutputIntVariablePin:
                case CompositePinType.CompositeOutputObjectVariablePin:
                case CompositePinType.CompositeOutputPositionVariablePin:
                case CompositePinType.CompositeOutputStringVariablePin:
                case CompositePinType.CompositeOutputVariablePin:
                case CompositePinType.CompositeOutputZoneLinkPtrVariablePin:
                case CompositePinType.CompositeOutputZonePtrVariablePin:
                case CompositePinType.CompositeOutputEnumVariablePin:
                case CompositePinType.CompositeOutputEnumStringVariablePin:
                    return ParameterVariant.OUTPUT_PIN;
                case CompositePinType.CompositeMethodPin:
                    return ParameterVariant.METHOD_PIN;
                    break;
                case CompositePinType.CompositeTargetPin:
                    return ParameterVariant.TARGET_PIN;
                case CompositePinType.CompositeReferencePin:
                    return ParameterVariant.REFERENCE_PIN;
                default:
                    throw new Exception("Unexpected type!");
            }
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
