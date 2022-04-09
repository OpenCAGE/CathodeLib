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

        private static Dictionary<ShortGuid, CathodeFunctionType> _functionTypeLUT = new Dictionary<ShortGuid, CathodeFunctionType>();
        private static void SetupFunctionTypeLUT()
        {
            if (_functionTypeLUT.Count != 0) return;

            foreach (CathodeFunctionType functionType in Enum.GetValues(typeof(CathodeFunctionType)))
                _functionTypeLUT.Add(Utilities.GenerateGUID(functionType.ToString()), functionType);
        }
        public static CathodeFunctionType GetFunctionType(byte[] tag)
        {
            return GetFunctionType(new ShortGuid(tag));
        }
        public static CathodeFunctionType GetFunctionType(ShortGuid tag)
        {
            SetupFunctionTypeLUT();
            return _functionTypeLUT[tag];
        }
        public static ShortGuid GetFunctionTypeGUID(CathodeFunctionType type)
        {
            SetupFunctionTypeLUT();
            return _functionTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        public static bool FunctionTypeExists(ShortGuid tag)
        {
            return _functionTypeLUT.ContainsKey(tag);
        }

        private static Dictionary<ShortGuid, CathodeDataType> _dataTypeLUT = new Dictionary<ShortGuid, CathodeDataType>();
        private static void SetupDataTypeLUT()
        {
            if (_dataTypeLUT.Count != 0) return;

            _dataTypeLUT.Add(Utilities.GenerateGUID("bool"), CathodeDataType.BOOL);
            _dataTypeLUT.Add(Utilities.GenerateGUID("int"), CathodeDataType.INTEGER);
            _dataTypeLUT.Add(Utilities.GenerateGUID("float"), CathodeDataType.FLOAT);
            _dataTypeLUT.Add(Utilities.GenerateGUID("String"), CathodeDataType.STRING);
            _dataTypeLUT.Add(Utilities.GenerateGUID("FilePath"), CathodeDataType.FILEPATH);
            _dataTypeLUT.Add(Utilities.GenerateGUID("SplineData"), CathodeDataType.SPLINE_DATA);
            _dataTypeLUT.Add(Utilities.GenerateGUID("Direction"), CathodeDataType.DIRECTION);
            _dataTypeLUT.Add(Utilities.GenerateGUID("Position"), CathodeDataType.POSITION);
            _dataTypeLUT.Add(Utilities.GenerateGUID("Enum"), CathodeDataType.ENUM);
            _dataTypeLUT.Add(Utilities.GenerateGUID("ShortGuid"), CathodeDataType.SHORT_GUID);
            _dataTypeLUT.Add(Utilities.GenerateGUID("Object"), CathodeDataType.OBJECT);
            _dataTypeLUT.Add(Utilities.GenerateGUID("ZonePtr"), CathodeDataType.ZONE_PTR);
            _dataTypeLUT.Add(Utilities.GenerateGUID("ZoneLinkPtr"), CathodeDataType.ZONE_LINK_PTR);
            _dataTypeLUT.Add(Utilities.GenerateGUID(""), CathodeDataType.NO_TYPE);
            _dataTypeLUT.Add(Utilities.GenerateGUID("Marker"), CathodeDataType.MARKER);
            _dataTypeLUT.Add(Utilities.GenerateGUID("Character"), CathodeDataType.CHARACTER);
            _dataTypeLUT.Add(Utilities.GenerateGUID("Camera"), CathodeDataType.CAMERA);
        }
        public static CathodeDataType GetDataType(byte[] tag)
        {
            return GetDataType(new ShortGuid(tag));
        }
        public static CathodeDataType GetDataType(ShortGuid tag)
        {
            SetupDataTypeLUT();
            return _dataTypeLUT[tag];
        }
        public static ShortGuid GetDataTypeGUID(CathodeDataType type)
        {
            SetupDataTypeLUT();
            return _dataTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        public static bool DataTypeExists(ShortGuid tag)
        {
            return _dataTypeLUT.ContainsKey(tag);
        }

        private static Dictionary<ShortGuid, CathodeResourceReferenceType> _resourceReferenceTypeLUT = new Dictionary<ShortGuid, CathodeResourceReferenceType>();
        private static void SetupResourceEntryTypeLUT()
        {
            if (_resourceReferenceTypeLUT.Count != 0) return;

            foreach (CathodeResourceReferenceType referenceType in Enum.GetValues(typeof(CathodeResourceReferenceType)))
                _resourceReferenceTypeLUT.Add(Utilities.GenerateGUID(referenceType.ToString()), referenceType);
        }
        public static CathodeResourceReferenceType GetResourceEntryType(byte[] tag)
        {
            return GetResourceEntryType(new ShortGuid(tag));
        }
        public static CathodeResourceReferenceType GetResourceEntryType(ShortGuid tag)
        {
            SetupResourceEntryTypeLUT();
            return _resourceReferenceTypeLUT[tag];
        }
        public static ShortGuid GetResourceEntryTypeGUID(CathodeResourceReferenceType type)
        {
            SetupResourceEntryTypeLUT();
            return _resourceReferenceTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
    }
}
