using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Text;

namespace CATHODE.Scripting
{
    public static class ParameterUtils
    {
        private static byte[] _functionInfo;
        private static Dictionary<FunctionType, Dictionary<ParameterVariant, int>> _functionVariantOffsets = new Dictionary<FunctionType, Dictionary<ParameterVariant, int>>();
        private static Dictionary<FunctionType, FunctionType?> _functionBaseClasses = new Dictionary<FunctionType, FunctionType?>();

        //this really needs deprecating
        public static Commands LinkedCommands => _commands;
        private static Commands _commands;

        /* Load all FunctionEntity metadata from our offline DB */
        static ParameterUtils()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            _functionInfo = File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_entity_lut.bin");
#else
            _functionInfo = CathodeLib.Properties.Resources.cathode_entity_lut;
            if (File.Exists("LocalDB/cathode_entity_lut.bin"))
                _functionInfo = File.ReadAllBytes("LocalDB/cathode_entity_lut.bin");
#endif
            using (BinaryReader reader = new BinaryReader(new MemoryStream(_functionInfo)))
            {
                int functionTypeCount = reader.ReadInt32();
                for (int i = 0; i < functionTypeCount; i++)
                {
                    FunctionType function = (FunctionType)reader.ReadUInt32();

                    uint baseClass = reader.ReadUInt32();
                    _functionBaseClasses.Add(function, baseClass == 0 ? (FunctionType?)null : (FunctionType)baseClass);

                    int numberOfVariants = reader.ReadInt32();
                    Dictionary<ParameterVariant, int> variantOffsets = new Dictionary<ParameterVariant, int>(numberOfVariants);
                    _functionVariantOffsets.Add(function, variantOffsets);
                    for (int x = 0; x < numberOfVariants; x++)
                    {
                        variantOffsets.Add((ParameterVariant)reader.ReadInt32(), reader.ReadInt32());
                    }
                }
            }
        }

        public static void LinkCommands(Commands commands)
        {
            _commands = commands;
        }

        /* Get the inherited function type for a given function type (returns null if it doesn't inherit) */
        public static FunctionType? GetInheritedFunction(FunctionType function)
        {
            return _functionBaseClasses[function];
        }

        /* Add all parameters to a given entity with default values (NOTE: you only need to pass in composite if Entity is an Alias or Variable, otherwise feel free to pass null) */
        public static void AddAllDefaultParameters(Entity entity, Composite composite, bool overwrite = true, ParameterVariant variants = ParameterVariant.INPUT_PIN | ParameterVariant.INTERNAL | /*ParameterVariant.METHOD_FUNCTION | */ParameterVariant.METHOD_PIN | ParameterVariant.OUTPUT_PIN | ParameterVariant.PARAMETER | ParameterVariant.REFERENCE_PIN | ParameterVariant.STATE_PARAMETER | ParameterVariant.TARGET_PIN, bool includeInherited = true)
        {
            switch (entity.variant)
            {
                case EntityVariant.VARIABLE:
                    ApplyDefaultVariable((VariableEntity)entity, entity, composite, variants, overwrite);
                    break;
                case EntityVariant.FUNCTION:
                    ApplyDefaultFunction((FunctionEntity)entity, entity, composite, variants, overwrite, includeInherited);
                    break;
                //TODO: the proxy and alias logic here can be written better, but i'm tired and lazy right now
                case EntityVariant.PROXY:
                    {
                        if (includeInherited)
                            ApplyDefaults(entity, overwrite, variants, FunctionType.ProxyInterface);
                        Entity proxiedEntity = ((ProxyEntity)entity).proxy.GetPointedEntity(_commands, out Composite proxiedComposite);
                        if (proxiedEntity != null && proxiedComposite != null)
                        {
                            switch (proxiedEntity.variant)
                            {
                                case EntityVariant.VARIABLE:
                                    ApplyDefaultVariable((VariableEntity)proxiedEntity, entity, proxiedComposite, variants, overwrite);
                                    break;
                                case EntityVariant.FUNCTION:
                                    ApplyDefaultFunction((FunctionEntity)proxiedEntity, entity, proxiedComposite, variants, overwrite, includeInherited);
                                    break;
                                default:
                                    throw new Exception("Unexpected!"); //we can't proxy to proxies or aliases
                            }
                        }
                    }
                    break;
                case EntityVariant.ALIAS:
                    {
                        Entity aliasedEntity = ((AliasEntity)entity).alias.GetPointedEntity(_commands, composite, out Composite aliasedComposite);
                        if (aliasedEntity != null && aliasedComposite != null)
                        {
                            switch (aliasedEntity.variant)
                            {
                                case EntityVariant.VARIABLE:
                                    ApplyDefaultVariable((VariableEntity)aliasedEntity, entity, aliasedComposite, variants, overwrite);
                                    break;
                                case EntityVariant.FUNCTION:
                                    ApplyDefaultFunction((FunctionEntity)aliasedEntity, entity, aliasedComposite, variants, overwrite, includeInherited);
                                    break;
                                case EntityVariant.PROXY:
                                    if (includeInherited)
                                        ApplyDefaults(aliasedEntity, overwrite, variants, FunctionType.ProxyInterface);
                                    Entity proxiedEntity = ((ProxyEntity)aliasedEntity).proxy.GetPointedEntity(_commands, out Composite proxiedComposite);
                                    if (proxiedEntity != null && proxiedComposite != null)
                                    {
                                        switch (proxiedEntity.variant)
                                        {
                                            case EntityVariant.VARIABLE:
                                                ApplyDefaultVariable((VariableEntity)proxiedEntity, entity, proxiedComposite, variants, overwrite);
                                                break;
                                            case EntityVariant.FUNCTION:
                                                ApplyDefaultFunction((FunctionEntity)proxiedEntity, entity, proxiedComposite, variants, overwrite, includeInherited);
                                                break;
                                            default:
                                                throw new Exception("Unexpected!"); //we can't proxy to proxies or aliases
                                        }
                                    }
                                    break;
                                default:
                                    throw new Exception("Unexpected!"); //we can't alias to aliases
                            }
                        }
                    }
                    break;
            }
        }
        private static void ApplyDefaults(Entity entity, bool overwrite, ParameterVariant variants, FunctionType function)
        {
            List<(ShortGuid, ParameterVariant, DataType)> parameters = GetAllParameters(function);
            foreach ((ShortGuid guid, ParameterVariant variant, DataType type) in parameters)
            {
                if (!variants.HasFlag(variant)) continue;
                entity.AddParameter(guid, CreateDefaultParameterData(function, guid, variant), variant, overwrite);
            }
        }
        private static void ApplyDefaultVariable(VariableEntity variableEntity, Entity entity, Composite composite, ParameterVariant variants, bool overwrite)
        {
            var pinInfo = CompositeUtils.GetParameterInfo(composite, variableEntity);
            if (pinInfo == null)
            {
                entity.AddParameter(variableEntity.name, variableEntity.type);
            }
            else
            {
                switch ((CompositePinType)pinInfo.PinTypeGUID.ToUInt32())
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
                        if (variants.HasFlag(ParameterVariant.INPUT_PIN))
                            entity.AddParameter(variableEntity.name, variableEntity.type, ParameterVariant.INPUT_PIN, overwrite);
                        break;
                    case CompositePinType.CompositeInputEnumVariablePin:
                        if (variants.HasFlag(ParameterVariant.PARAMETER))
                            entity.AddParameter(variableEntity.name, new cEnum(pinInfo.PinEnumTypeGUID, -1), ParameterVariant.INPUT_PIN, overwrite);
                        break;
                    case CompositePinType.CompositeInputEnumStringVariablePin:
                        if (variants.HasFlag(ParameterVariant.PARAMETER))
                            entity.AddParameter(variableEntity.name, new cEnumString(pinInfo.PinEnumTypeGUID, ""), ParameterVariant.INPUT_PIN, overwrite);
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
                        if (variants.HasFlag(ParameterVariant.OUTPUT_PIN))
                            entity.AddParameter(variableEntity.name, variableEntity.type, ParameterVariant.OUTPUT_PIN, overwrite);
                        break;
                    case CompositePinType.CompositeOutputEnumVariablePin:
                        if (variants.HasFlag(ParameterVariant.PARAMETER))
                            entity.AddParameter(variableEntity.name, new cEnum(pinInfo.PinEnumTypeGUID, -1), ParameterVariant.OUTPUT_PIN, overwrite);
                        break;
                    case CompositePinType.CompositeOutputEnumStringVariablePin:
                        if (variants.HasFlag(ParameterVariant.PARAMETER))
                            entity.AddParameter(variableEntity.name, new cEnumString(pinInfo.PinEnumTypeGUID, ""), ParameterVariant.OUTPUT_PIN, overwrite);
                        break;
                    case CompositePinType.CompositeMethodPin:
                        if (variants.HasFlag(ParameterVariant.METHOD_PIN) || variants.HasFlag(ParameterVariant.METHOD_FUNCTION))
                            entity.AddParameter(variableEntity.name, variableEntity.type, ParameterVariant.METHOD_PIN, overwrite);
                        break;
                    case CompositePinType.CompositeTargetPin:
                        if (variants.HasFlag(ParameterVariant.TARGET_PIN))
                            entity.AddParameter(variableEntity.name, variableEntity.type, ParameterVariant.TARGET_PIN, overwrite);
                        break;
                    case CompositePinType.CompositeReferencePin:
                        if (variants.HasFlag(ParameterVariant.REFERENCE_PIN))
                            entity.AddParameter(variableEntity.name, variableEntity.type, ParameterVariant.REFERENCE_PIN, overwrite);
                        break;
                    default:
                        throw new Exception("Unexpected type!");
                }
            }
        }
        private static void ApplyDefaultFunction(FunctionEntity functionEntity, Entity entity, Composite composite, ParameterVariant variants, bool overwrite, bool includeInherited)
        {
            if (CommandsUtils.FunctionTypeExists((functionEntity).function))
            {
                FunctionType? functionType = (FunctionType)(functionEntity).function.ToUInt32();
                while (true)
                {
                    ApplyDefaults(entity, overwrite, variants, functionType.Value);
                    if (!includeInherited) break;
                    functionType = GetInheritedFunction(functionType.Value);
                    if (functionType == null) break;
                }
            }
            else
            {
                if (includeInherited)
                    ApplyDefaults(entity, overwrite, variants, FunctionType.CompositeInterface);
                Composite compositeInstance = _commands.GetComposite((functionEntity).function);
                foreach (VariableEntity variable in compositeInstance.variables)
                {
                    ApplyDefaultVariable(variable, entity, composite, variants, overwrite);
                }
            }
        }

        /* Get all possible parameters for a given function type */
        public static List<(ShortGuid, ParameterVariant, DataType)> GetAllParameters(FunctionType function)
        {
            List<(ShortGuid, ParameterVariant, DataType)> parameters = new List<(ShortGuid, ParameterVariant, DataType)>();
            using (BinaryReader reader = new BinaryReader(new MemoryStream(_functionInfo)))
            {
                Dictionary<ParameterVariant, int> offsets = _functionVariantOffsets[function];
                foreach (KeyValuePair<ParameterVariant, int> entry in offsets)
                {
                    reader.BaseStream.Position = entry.Value;
                    int paramCount = reader.ReadInt32();
                    for (int i = 0; i < paramCount; i++)
                    {
                        uint paramID = reader.ReadUInt32();
                        switch (entry.Key)
                        {
                            case ParameterVariant.TARGET_PIN:
                            case ParameterVariant.REFERENCE_PIN:
                            case ParameterVariant.METHOD_FUNCTION:
                            case ParameterVariant.METHOD_PIN:
                                parameters.Add((new ShortGuid(paramID), entry.Key, DataType.FLOAT));
                                break;
                            default:
                                DataType dataType = (DataType)reader.ReadInt32();
                                parameters.Add((new ShortGuid(paramID), entry.Key, dataType));
                                switch (dataType)
                                {
                                    case DataType.BOOL:
                                        reader.BaseStream.Position += 1;
                                        break;
                                    case DataType.INTEGER:
                                        reader.BaseStream.Position += 4;
                                        break;
                                    case DataType.FLOAT:
                                        reader.BaseStream.Position += 4;
                                        break;
                                    case DataType.STRING:
                                    case DataType.FILEPATH:
                                        {
                                            int seek = reader.ReadByte();
                                            reader.BaseStream.Position += seek;
                                        }
                                        break;
                                    case DataType.ENUM:
                                        {
                                            int enumType = reader.ReadInt32();
                                            if (enumType != -1)
                                            {
                                                reader.BaseStream.Position += 4;
                                            }
                                        }
                                        break;
                                    case DataType.ENUM_STRING:
                                        {
                                            int seek = reader.ReadByte();
                                            reader.BaseStream.Position += seek;
                                            seek = reader.ReadByte();
                                            reader.BaseStream.Position += seek;
                                        }
                                        break;
                                    case DataType.VECTOR:
                                        reader.BaseStream.Position += 12;
                                        break;
                                    case DataType.RESOURCE:
                                        reader.BaseStream.Position += 4;
                                        break;
                                    default:
                                        //Any other types have no default values.
                                        break;
                                }
                                break;
                        }
                    }
                }
            }
            return parameters;
        }

        /* Create ParameterData with default values for the given function type's parameter */
        public static ParameterData CreateDefaultParameterData(FunctionType function, string parameter)
        {
            return CreateDefaultParameterData(function, ShortGuidUtils.Generate(parameter));
        }
        public static ParameterData CreateDefaultParameterData(FunctionType function, ShortGuid parameter)
        {
            ParameterData data = null;
            Dictionary<ParameterVariant, int> offsets = _functionVariantOffsets[function];
            foreach (KeyValuePair<ParameterVariant, int> entry in offsets)
            {
                data = CreateDefaultParameterData(function, parameter, entry.Key);
                if (data != null) break;
            }
            return data;
        }
        public static ParameterData CreateDefaultParameterData(FunctionType function, ShortGuid parameter, ParameterVariant variant)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(_functionInfo)))
            {
                reader.BaseStream.Position = _functionVariantOffsets[function][variant];
                int paramCount = reader.ReadInt32();
                for (int i = 0; i < paramCount; i++)
                {
                    uint paramID = reader.ReadUInt32();
                    bool isCorrectParam = paramID == parameter.ToUInt32();
                    switch (variant)
                    {
                        case ParameterVariant.TARGET_PIN:
                        case ParameterVariant.REFERENCE_PIN:
                        case ParameterVariant.METHOD_FUNCTION:
                        case ParameterVariant.METHOD_PIN:
                            return new cFloat();
                        default:
                            DataType dataType = (DataType)reader.ReadInt32();
                            switch (dataType)
                            {
                                case DataType.BOOL:
                                    if (isCorrectParam)
                                        return new cBool(reader.ReadBoolean());
                                    else
                                        reader.BaseStream.Position += 1;
                                    break;
                                case DataType.INTEGER:
                                    if (isCorrectParam)
                                        return new cInteger(reader.ReadInt32());
                                    else
                                        reader.BaseStream.Position += 4;
                                    break;
                                case DataType.FLOAT:
                                    if (isCorrectParam)
                                        return new cFloat(reader.ReadSingle());
                                    else
                                        reader.BaseStream.Position += 4;
                                    break;
                                case DataType.STRING:
                                case DataType.FILEPATH:
                                    if (isCorrectParam)
                                        return new cString(reader.ReadString());
                                    else
                                    {
                                        int seek = reader.ReadByte();
                                        reader.BaseStream.Position += seek;
                                    }
                                    break;
                                case DataType.ENUM:
                                    {
                                        int enumType = reader.ReadInt32();
                                        if (enumType == -1)
                                        {
                                            return new cEnum();
                                        }
                                        else
                                        {
                                            if (isCorrectParam)
                                                return new cEnum((EnumType)enumType, reader.ReadInt32());
                                            else
                                                reader.BaseStream.Position += 4;
                                        }
                                    }
                                    break;
                                case DataType.ENUM_STRING:
                                    if (isCorrectParam)
                                        new cEnumString((EnumStringType)Enum.Parse(typeof(EnumStringType), reader.ReadString()), reader.ReadString());
                                    else
                                    {
                                        int seek = reader.ReadByte();
                                        reader.BaseStream.Position += seek;
                                        seek = reader.ReadByte();
                                        reader.BaseStream.Position += seek;
                                    }
                                    break;
                                case DataType.VECTOR:
                                    if (isCorrectParam)
                                        return new cVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                    else
                                        reader.BaseStream.Position += 12;
                                    break;
                                case DataType.RESOURCE:
                                    if (isCorrectParam)
                                        return new cResource((ResourceType)reader.ReadInt32());
                                    else
                                        reader.BaseStream.Position += 4;
                                    break;
                                default:
                                    return new cFloat();
                                    //Any other types have no default values.
                                    break;
                            }
                            break;
                    }
                }
            }
            return null;
        }
    }
}
