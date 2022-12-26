using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CATHODE.Commands
{
    public static class CommandsUtils
    {
        static CommandsUtils()
        {
            SetupFunctionTypeLUT();
            SetupDataTypeLUT();
            SetupResourceEntryTypeLUT();
        }

        private static Dictionary<ShortGuid, FunctionType> _functionTypeLUT = new Dictionary<ShortGuid, FunctionType>();
        private static void SetupFunctionTypeLUT()
        {
            if (_functionTypeLUT.Count != 0) return;

            foreach (FunctionType functionType in Enum.GetValues(typeof(FunctionType)))
            {
                string shortGuidString = functionType.ToString();
                if (functionType == FunctionType.GCIP_WorldPickup) 
                    shortGuidString = "n:\\content\\build\\library\\archetypes\\gameplay\\gcip_worldpickup";
                if (functionType == FunctionType.PlayForMinDuration)
                    shortGuidString = "n:\\content\\build\\library\\ayz\\animation\\logichelpers\\playforminduration";
                if (functionType == FunctionType.Torch_Control)
                    shortGuidString = "n:\\content\\build\\library\\archetypes\\script\\gameplay\\torch_control";

                _functionTypeLUT.Add(ShortGuidUtils.Generate(shortGuidString), functionType);
            }
        }
        public static FunctionType GetFunctionType(byte[] tag)
        {
            return GetFunctionType(new ShortGuid(tag));
        }
        public static FunctionType GetFunctionType(ShortGuid tag)
        {
            SetupFunctionTypeLUT();
            return _functionTypeLUT[tag];
        }
        public static ShortGuid GetFunctionTypeGUID(FunctionType type)
        {
            SetupFunctionTypeLUT();
            return _functionTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        public static bool FunctionTypeExists(ShortGuid tag)
        {
            return _functionTypeLUT.ContainsKey(tag);
        }

        private static Dictionary<ShortGuid, DataType> _dataTypeLUT = new Dictionary<ShortGuid, DataType>();
        private static void SetupDataTypeLUT()
        {
            if (_dataTypeLUT.Count != 0) return;

            _dataTypeLUT.Add(ShortGuidUtils.Generate("bool"), DataType.BOOL);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("int"), DataType.INTEGER);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("float"), DataType.FLOAT);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("String"), DataType.STRING);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("FilePath"), DataType.FILEPATH);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("SplineData"), DataType.SPLINE_DATA);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Direction"), DataType.DIRECTION);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Position"), DataType.POSITION);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Enum"), DataType.ENUM);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("ShortGuid"), DataType.RESOURCE);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Object"), DataType.OBJECT);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("ZonePtr"), DataType.ZONE_PTR);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("ZoneLinkPtr"), DataType.ZONE_LINK_PTR);
            _dataTypeLUT.Add(ShortGuidUtils.Generate(""), DataType.NO_TYPE);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Marker"), DataType.MARKER);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Character"), DataType.CHARACTER);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Camera"), DataType.CAMERA);
        }
        public static DataType GetDataType(byte[] tag)
        {
            return GetDataType(new ShortGuid(tag));
        }
        public static DataType GetDataType(ShortGuid tag)
        {
            SetupDataTypeLUT();
            return _dataTypeLUT[tag];
        }
        public static ShortGuid GetDataTypeGUID(DataType type)
        {
            SetupDataTypeLUT();
            return _dataTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        public static bool DataTypeExists(ShortGuid tag)
        {
            return _dataTypeLUT.ContainsKey(tag);
        }

        private static Dictionary<ShortGuid, ResourceType> _resourceReferenceTypeLUT = new Dictionary<ShortGuid, ResourceType>();
        private static void SetupResourceEntryTypeLUT()
        {
            if (_resourceReferenceTypeLUT.Count != 0) return;

            foreach (ResourceType referenceType in Enum.GetValues(typeof(ResourceType)))
                _resourceReferenceTypeLUT.Add(ShortGuidUtils.Generate(referenceType.ToString()), referenceType);
        }
        public static ResourceType GetResourceEntryType(byte[] tag)
        {
            return GetResourceEntryType(new ShortGuid(tag));
        }
        public static ResourceType GetResourceEntryType(ShortGuid tag)
        {
            SetupResourceEntryTypeLUT();
            return _resourceReferenceTypeLUT[tag];
        }
        public static ShortGuid GetResourceEntryTypeGUID(ResourceType type)
        {
            SetupResourceEntryTypeLUT();
            return _resourceReferenceTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
    }
}
