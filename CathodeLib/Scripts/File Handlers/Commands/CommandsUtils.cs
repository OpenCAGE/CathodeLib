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

        private static Dictionary<cGUID, CathodeFunctionType> _functionTypeLUT = new Dictionary<cGUID, CathodeFunctionType>();
        private static void SetupFunctionTypeLUT()
        {
            if (_functionTypeLUT.Count != 0) return;

            foreach (CathodeFunctionType functionType in Enum.GetValues(typeof(CathodeFunctionType)))
                _functionTypeLUT.Add(Utilities.GenerateGUID(functionType.ToString()), functionType);
        }
        public static CathodeFunctionType GetFunctionType(byte[] tag)
        {
            return GetFunctionType(new cGUID(tag));
        }
        public static CathodeFunctionType GetFunctionType(cGUID tag)
        {
            SetupFunctionTypeLUT();
            return _functionTypeLUT[tag];
        }
        public static cGUID GetFunctionTypeGUID(CathodeFunctionType type)
        {
            SetupFunctionTypeLUT();
            return _functionTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        public static bool FunctionTypeExists(cGUID tag)
        {
            return _functionTypeLUT.ContainsKey(tag);
        }

        private static Dictionary<cGUID, CathodeDataType> _dataTypeLUT = new Dictionary<cGUID, CathodeDataType>();
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
            return GetDataType(new cGUID(tag));
        }
        public static CathodeDataType GetDataType(cGUID tag)
        {
            SetupDataTypeLUT();
            return _dataTypeLUT[tag];
        }
        public static cGUID GetDataTypeGUID(CathodeDataType type)
        {
            SetupDataTypeLUT();
            return _dataTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        public static bool DataTypeExists(cGUID tag)
        {
            return _dataTypeLUT.ContainsKey(tag);
        }

        private static Dictionary<cGUID, CathodeResourceReferenceType> _resourceReferenceTypeLUT = new Dictionary<cGUID, CathodeResourceReferenceType>();
        private static void SetupResourceEntryTypeLUT()
        {
            if (_resourceReferenceTypeLUT.Count != 0) return;

            foreach (CathodeResourceReferenceType referenceType in Enum.GetValues(typeof(CathodeResourceReferenceType)))
                _resourceReferenceTypeLUT.Add(Utilities.GenerateGUID(referenceType.ToString()), referenceType);
        }
        public static CathodeResourceReferenceType GetResourceEntryType(byte[] tag)
        {
            return GetResourceEntryType(new cGUID(tag));
        }
        public static CathodeResourceReferenceType GetResourceEntryType(cGUID tag)
        {
            SetupResourceEntryTypeLUT();
            return _resourceReferenceTypeLUT[tag];
        }
        public static cGUID GetResourceEntryTypeGUID(CathodeResourceReferenceType type)
        {
            SetupResourceEntryTypeLUT();
            return _resourceReferenceTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
    }
}
