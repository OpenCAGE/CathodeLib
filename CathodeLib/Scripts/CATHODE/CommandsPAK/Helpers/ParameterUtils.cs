using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;

namespace CATHODE.Scripting
{
    public static class ParameterUtils
    {
        private static byte[] _functionInfo;
        private static Dictionary<FunctionType, Dictionary<ParameterVariant, int>> _functionVariantOffsets = new Dictionary<FunctionType, Dictionary<ParameterVariant, int>>();
        private static Dictionary<FunctionType, FunctionType?> _functionBaseClasses = new Dictionary<FunctionType, FunctionType?>();
        private static Tuple<int, int> _relayInfoOffset;

        //this really needs deprecating
        public static Commands LinkedCommands => _commands;
        private static Commands _commands;

        private static uint _nameID; //We remove the "name" param on every entity except Zone, since that is handled by EntityUtils.

        /* Load all FunctionEntity metadata from our offline DB */
        static ParameterUtils()
        {
            _nameID = ShortGuidUtils.Generate("name").ToUInt32();

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

                _relayInfoOffset = new Tuple<int, int>(reader.ReadInt32(), reader.ReadInt32());
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
        public static void AddAllDefaultParameters(Entity entity, Composite composite, bool overwrite = true, ParameterVariant variants = ParameterVariant.STATE_PARAMETER | ParameterVariant.INPUT_PIN | ParameterVariant.PARAMETER, bool includeInherited = true)
        {
            switch (entity.variant)
            {
                case EntityVariant.VARIABLE:
                    ApplyDefaultVariable((VariableEntity)entity, entity, composite, variants, overwrite);
                    break;
                case EntityVariant.FUNCTION:
                    ApplyDefaultFunction((FunctionEntity)entity, entity, composite, variants, overwrite, includeInherited);
                    break;
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
            ParameterData defaultValue = variableEntity.GetParameter(variableEntity.name)?.content;
            if (defaultValue != null)
            {
                if (pinInfo == null)
                {
                    if (variants.HasFlag(ParameterVariant.PARAMETER))
                        entity.AddParameter(variableEntity.name, defaultValue, ParameterVariant.PARAMETER, overwrite);
                }
                else
                {
                    CompositePinType pinType = (CompositePinType)pinInfo.PinTypeGUID.ToUInt32();
                    ParameterVariant paramVariant = CompositeUtils.PinTypeToParameterVariant(pinType);
                    switch (pinType)
                    {
                        case CompositePinType.CompositeMethodPin:
                            if (variants.HasFlag(ParameterVariant.METHOD_PIN) || variants.HasFlag(ParameterVariant.METHOD_FUNCTION))
                                entity.AddParameter(variableEntity.name, defaultValue, paramVariant, overwrite);
                            break;
                        default:
                            if (variants.HasFlag(paramVariant))
                                entity.AddParameter(variableEntity.name, defaultValue, paramVariant, overwrite);
                            break;
                    }
                }
            }
            else
            {
                if (pinInfo == null)
                {
                    if (variants.HasFlag(ParameterVariant.PARAMETER))
                        entity.AddParameter(variableEntity.name, variableEntity.type, ParameterVariant.PARAMETER, overwrite);
                }
                else
                {
                    CompositePinType pinType = (CompositePinType)pinInfo.PinTypeGUID.ToUInt32();
                    ParameterVariant paramVariant = CompositeUtils.PinTypeToParameterVariant(pinType);
                    switch (pinType)
                    {
                        case CompositePinType.CompositeMethodPin:
                            if (variants.HasFlag(ParameterVariant.METHOD_PIN) || variants.HasFlag(ParameterVariant.METHOD_FUNCTION))
                                entity.AddParameter(variableEntity.name, variableEntity.type, paramVariant, overwrite);
                            break;
                        default:
                            if (variants.HasFlag(paramVariant))
                                entity.AddParameter(variableEntity.name, variableEntity.type, paramVariant, overwrite);
                            break;
                    }
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
                    ApplyDefaultVariable(variable, entity, compositeInstance, variants, overwrite);
                }
            }
        }

        /* Get all possible parameters for a given entity */
        public static List<(ShortGuid, ParameterVariant, DataType)> GetAllParameters(Entity entity, Composite composite, bool includeInherited = true)
        {
            List<(ShortGuid, ParameterVariant, DataType)> parameters = new List<(ShortGuid, ParameterVariant, DataType)>();
            switch (entity.variant)
            {
                case EntityVariant.FUNCTION:
                    FunctionEntity functionEntity = (FunctionEntity)entity;
                    if (CommandsUtils.FunctionTypeExists(functionEntity.function))
                    {
                        FunctionType? functionType = (FunctionType)functionEntity.function.ToUInt32();
                        while (true)
                        {
                            parameters.AddRange(GetAllParameters(functionType.Value));
                            if (!includeInherited) break;
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                    }
                    else
                    {
                        if (includeInherited)
                            parameters.AddRange(GetAllParameters(FunctionType.CompositeInterface));
                        Composite compositeInstance = _commands.GetComposite((functionEntity).function);
                        foreach (VariableEntity variable in compositeInstance.variables)
                        {
                            parameters.AddRange(GetAllParameters(variable, compositeInstance, includeInherited));
                        }
                    }
                    break;
                case EntityVariant.PROXY:
                    if (includeInherited)
                        parameters.AddRange(GetAllParameters(FunctionType.ProxyInterface));
                    ProxyEntity proxyEntity = (ProxyEntity)entity;
                    Entity proxiedEntity = proxyEntity.proxy.GetPointedEntity(_commands);
                    if (proxiedEntity != null)
                        parameters.AddRange(GetAllParameters(proxiedEntity, composite));
                    break;
                case EntityVariant.ALIAS:
                    AliasEntity aliasEntity = (AliasEntity)entity;
                    Entity aliasedEntity = aliasEntity.alias.GetPointedEntity(_commands);
                    if (aliasedEntity != null)
                        parameters.AddRange(GetAllParameters(aliasedEntity, composite));
                    break;
                case EntityVariant.VARIABLE:
                    VariableEntity variableEntity = (VariableEntity)entity;
                    CompositePinInfoTable.PinInfo info = CompositeUtils.GetParameterInfo(composite, variableEntity);
                    if (info == null)
                        parameters.Add((variableEntity.name, ParameterVariant.PARAMETER, variableEntity.type));
                    else
                    {
                        parameters.Add((variableEntity.name, CompositeUtils.PinTypeToParameterVariant(info.PinTypeGUID), variableEntity.type));
                    }

                    break;
            }
            return parameters;
        }

        /* Get all possible parameters for a given function type (not including inherited) */
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
                            case ParameterVariant.REFERENCE_PIN:
                            case ParameterVariant.METHOD_FUNCTION:
                            case ParameterVariant.METHOD_PIN:
                                parameters.Add((new ShortGuid(paramID), entry.Key, DataType.FLOAT));
                                break;
                            default:
                                DataType dataType = (DataType)reader.ReadInt32();
                                if (!(function != FunctionType.Zone && paramID == _nameID))
                                {
                                    if (dataType == DataType.NONE) //This only applies to TARGET_PIN, sometimes it has a value, other times it doesn't. If it doesn't, fall back to FLOAT for now.
                                        dataType = DataType.FLOAT;
                                    parameters.Add((new ShortGuid(paramID), entry.Key, dataType));
                                }
                                if (entry.Key == ParameterVariant.TARGET_PIN) continue; //TargetPin can have a type, but doesn't have data.
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

        /* Get metadata about a parameter on an entity: variant, type, and function/composite that implements (if applicable) */
        public static (ParameterVariant?, DataType?, ShortGuid) GetParameterMetadata(Entity entity, string parameter)
        {
            return GetParameterMetadata(entity, ShortGuidUtils.Generate(parameter));
        }
        public static (ParameterVariant?, DataType?, ShortGuid) GetParameterMetadata(Entity entity, ShortGuid parameter)
        {
            switch (entity.variant)
            {
                case EntityVariant.VARIABLE:
                    if (parameter == ((VariableEntity)entity).name)
                        return (ParameterVariant.PARAMETER, ((VariableEntity)entity).type, ShortGuid.Invalid);
                    break;
                case EntityVariant.FUNCTION:
                    FunctionEntity functionEntity = (FunctionEntity)entity;
                    if (CommandsUtils.FunctionTypeExists(functionEntity.function))
                    {
                        FunctionType? functionType = (FunctionType)functionEntity.function.ToUInt32();
                        while (true)
                        {
                            var metadata = GetParameterMetadata(functionType.Value, parameter);
                            if (metadata.Item1 != null)
                                return (metadata.Item1, metadata.Item2, metadata.Item3 == null ? ShortGuid.Invalid : new ShortGuid((UInt32)metadata.Item3));
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                    }
                    else
                    {
                        var metadata = GetParameterMetadata(FunctionType.CompositeInterface, parameter);
                        if (metadata.Item1 != null)
                            return (metadata.Item1, metadata.Item2, metadata.Item3 == null ? ShortGuid.Invalid : new ShortGuid((UInt32)metadata.Item3));
                        Composite composite = _commands.GetComposite(functionEntity.function);
                        if (composite != null)
                        {
                            VariableEntity var = composite.variables.FirstOrDefault(o => o.name == parameter);
                            if (var != null)
                            {
                                CompositePinInfoTable.PinInfo info = CompositeUtils.GetParameterInfo(composite, var);
                                if (info == null)
                                    return (ParameterVariant.PARAMETER, var.type, composite.shortGUID);
                                else
                                {
                                    return (CompositeUtils.PinTypeToParameterVariant(info.PinTypeGUID), var.type, composite.shortGUID);
                                }
                            }
                        }
                    }
                    break;
                case EntityVariant.PROXY:
                    {
                        var metadata = GetParameterMetadata(FunctionType.ProxyInterface, parameter);
                        if (metadata.Item1 != null)
                            return (metadata.Item1, metadata.Item2, metadata.Item3 == null ? ShortGuid.Invalid : new ShortGuid((UInt32)metadata.Item3));
                        ProxyEntity proxyEntity = (ProxyEntity)entity;
                        Entity proxiedEntity = proxyEntity.proxy.GetPointedEntity(_commands);
                        if (proxiedEntity != null)
                            return GetParameterMetadata(proxiedEntity, parameter);
                        break;
                    }
                case EntityVariant.ALIAS:
                    AliasEntity aliasEntity = (AliasEntity)entity;
                    Entity aliasedEntity = aliasEntity.alias.GetPointedEntity(_commands);
                    if (aliasedEntity != null)
                        return GetParameterMetadata(aliasedEntity, parameter);
                    break;
            }
            return (null, null, ShortGuid.Invalid);
        }

        /* Get metadata about a parameter on a function: variant, type, and function that implements */
        public static (ParameterVariant?, DataType?, FunctionType?) GetParameterMetadata(FunctionType function, string parameter)
        {
            return GetParameterMetadata(function, ShortGuidUtils.Generate(parameter));
        }
        public static (ParameterVariant?, DataType?, FunctionType?) GetParameterMetadata(FunctionType function, ShortGuid parameter)
        {
            Dictionary<ParameterVariant, int> offsets = _functionVariantOffsets[function];
            foreach (KeyValuePair<ParameterVariant, int> entry in offsets)
            {
                (ParameterVariant?, DataType?, FunctionType?) data = GetParameterMetadata(function, parameter, entry.Key);
                if (data.Item1 != null && data.Item2 != null && data.Item3 != null)
                    return data;
            }
            return (null, null, null);
        }
        public static (ParameterVariant?, DataType?, FunctionType?) GetParameterMetadata(FunctionType function, ShortGuid parameter, ParameterVariant variant)
        {
            List<(ShortGuid, ParameterVariant, DataType)> parameters = new List<(ShortGuid, ParameterVariant, DataType)>();
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
                        case ParameterVariant.REFERENCE_PIN:
                        case ParameterVariant.METHOD_FUNCTION:
                        case ParameterVariant.METHOD_PIN:
                            if (isCorrectParam)
                                return (variant, DataType.FLOAT, function);
                            break;
                        default:
                            DataType dataType = (DataType)reader.ReadInt32();
                            if (dataType == DataType.NONE) //This only applies to TARGET_PIN, sometimes it has a value, other times it doesn't. If it doesn't, fall back to FLOAT for now.
                                dataType = DataType.FLOAT;
                            if (isCorrectParam)
                                return (variant, dataType, function);
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
            return (null, null, null);
        }

        /* Create ParameterData with default values for the given entity's parameter */
        public static ParameterData CreateDefaultParameterData(Entity entity, Composite composite, string parameter)
        {
            return CreateDefaultParameterData(entity, composite, ShortGuidUtils.Generate(parameter));
        }
        public static ParameterData CreateDefaultParameterData(Entity entity, Composite composite, ShortGuid parameter)
        {
            switch (entity.variant)
            {
                case EntityVariant.VARIABLE:
                    VariableEntity variableEntity = (VariableEntity)entity;
                    if (parameter == variableEntity.name)
                    {
                        ParameterData defaultVal = variableEntity.GetParameter(parameter)?.content;
                        if (defaultVal != null)
                            return (ParameterData)defaultVal.Clone();
                        CompositePinInfoTable.PinInfo info = CompositeUtils.GetParameterInfo(composite, variableEntity);
                        switch (variableEntity.type)
                        {
                            case DataType.FLOAT:
                                return new cFloat();
                            case DataType.INTEGER:
                                return new cInteger();
                            case DataType.BOOL:
                                return new cBool();
                            case DataType.RESOURCE:
                                return new cResource();
                            case DataType.ENUM:
                                if (info == null)
                                    return new cEnum();
                                else
                                    return new cEnum(info.PinEnumTypeGUID, -1);
                            case DataType.ENUM_STRING:
                                if (info == null)
                                    return new cEnumString();
                                else
                                    return new cEnumString(info.PinEnumTypeGUID, "");
                            case DataType.STRING:
                            case DataType.FILEPATH:
                                return new cString();
                            case DataType.SPLINE:
                                return new cSpline();
                            default:
                                return new cString(); //string, or float?
                        }
                    }
                    break;
                case EntityVariant.FUNCTION:
                    FunctionEntity functionEntity = (FunctionEntity)entity;
                    if (CommandsUtils.FunctionTypeExists(functionEntity.function))
                    {
                        FunctionType? functionType = (FunctionType)functionEntity.function.ToUInt32();
                        while (true)
                        {
                            var data = CreateDefaultParameterData(functionType.Value, parameter);
                            if (data != null)
                                return data;
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                    }
                    else
                    {
                        var data = CreateDefaultParameterData(FunctionType.CompositeInterface, parameter);
                        if (data != null)
                            return data;
                        Composite comp = _commands.GetComposite(functionEntity.function);
                        if (composite != null)
                        {
                            VariableEntity var = comp.variables.FirstOrDefault(o => o.name == parameter);
                            if (var != null)
                            {
                                data = CreateDefaultParameterData(var, comp, parameter);
                                if (data != null) 
                                    return data;
                            }
                        }
                    }
                    break;
                case EntityVariant.PROXY:
                    {
                        var data = CreateDefaultParameterData(FunctionType.ProxyInterface, parameter);
                        if (data != null)
                            return data;
                        ProxyEntity proxyEntity = (ProxyEntity)entity;
                        Entity proxiedEntity = proxyEntity.proxy.GetPointedEntity(_commands, out Composite proxiedComposite);
                        if (proxiedEntity != null)
                            return CreateDefaultParameterData(proxiedEntity, proxiedComposite, parameter);
                        break;
                    }
                case EntityVariant.ALIAS:
                    AliasEntity aliasEntity = (AliasEntity)entity;
                    Entity aliasedEntity = aliasEntity.alias.GetPointedEntity(_commands, out Composite aliasedComposite);
                    if (aliasedEntity != null)
                        return CreateDefaultParameterData(aliasedEntity, aliasedComposite, parameter);
                    break;
            }
            return null;
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
                            if (isCorrectParam)
                                return new cFloat();
                            break;
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
                                    if (isCorrectParam && !(function != FunctionType.Zone && paramID == _nameID))
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
                                            if (isCorrectParam)
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
                                        return new cEnumString((EnumStringType)Enum.Parse(typeof(EnumStringType), reader.ReadString()), reader.ReadString());
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
                                    if (isCorrectParam)
                                        return new cFloat(); //Any other types have no default values.
                                    break;
                            }
                            break;
                    }
                }
            }
            return null;
        }

        /* Get the relay pin for a given method pin */
        public static ShortGuid GetRelay(ShortGuid guid)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(_functionInfo)))
            {
                reader.BaseStream.Position = _relayInfoOffset.Item1;
                for (int i = 0; i < _relayInfoOffset.Item2; i++)
                {
                    UInt32 method = reader.ReadUInt32();
                    UInt32 relay = reader.ReadUInt32();
                    if (method == guid.ToUInt32())
                        return new ShortGuid(relay);
                }
            }
            return ShortGuid.Invalid;
        }
    }
}
