using CATHODE.Assets.Utilities;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib;
using CathodeLib.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.SymbolStore;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace CATHODE.Scripting.Internal
{
    /* An entity in a composite */
    [Serializable]
    public class Entity : IComparable<Entity>
    {
        public Entity(EntityVariant variant)
        {
            this.shortGUID = ShortGuidUtils.GenerateRandom();
            this.variant = variant;
        }
        public Entity(ShortGuid shortGUID, EntityVariant variant)
        {
            this.shortGUID = shortGUID;
            this.variant = variant;
        }

        public ShortGuid shortGUID; //Translates to string via EntityNameLookup.GetEntityName
        public EntityVariant variant;

        public List<EntityLink> childLinks = new List<EntityLink>();
        public List<Parameter> parameters = new List<Parameter>();

        /* Implements IComparable for searching */
        public int CompareTo(Entity other)
        {
            int TotalThis = shortGUID.val[0] + shortGUID.val[1] + shortGUID.val[2] + shortGUID.val[3];
            int TotalOther = other.shortGUID.val[0] + other.shortGUID.val[1] + other.shortGUID.val[2] + other.shortGUID.val[3];
            if (TotalThis > TotalOther) return 1;
            else if (TotalThis == TotalOther) return 0;
            return -1;
        }

        /* Get parameter by string name or ShortGuid */
        public Parameter GetParameter(string name)
        {
            ShortGuid id = ShortGuidUtils.Generate(name);
            return GetParameter(id);
        }
        public Parameter GetParameter(ShortGuid id)
        {
            return parameters.FirstOrDefault(o => o.shortGUID == id);
        }

        /* Add a data-supplying parameter to the entity */
        /*
        public Parameter AddParameter<T>(string name, T data, ParameterVariant variant = ParameterVariant.PARAMETER)
        {
            ShortGuid id = ShortGuidUtils.Generate(name);
            switch (Type.GetTypeCode(typeof(T)))
            {
                case TypeCode.String:
                    return AddParameter(id, new cString((string)(object)data), variant);
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    return AddParameter(id, new cInteger((int)(object)data), variant);
                case TypeCode.Boolean:
                    return AddParameter(id, new cBool((bool)(object)data), variant);
                case TypeCode.Double:
                    return AddParameter(id, new cFloat((float)((double)(object)data)), variant);
                case TypeCode.Single:
                    return AddParameter(id, new cFloat((float)(object)data), variant);
            }
            throw new Exception("Tried to AddParameter using templated function, but type is not supported.");
        }
        */
        public Parameter AddParameter(string name, ParameterData data, ParameterVariant variant = ParameterVariant.PARAMETER)
        {
            return AddParameter(ShortGuidUtils.Generate(name), data, variant);
        }
        public Parameter AddParameter(ShortGuid id, ParameterData data, ParameterVariant variant = ParameterVariant.PARAMETER)
        {
            //TODO: we are limiting data-supplying params to ONE per entity here - is this correct? I think links are the only place where you can have multiple of the same.
            Parameter param = GetParameter(id);
            if (param == null)
            {
                param = new Parameter(id, data, variant);
                parameters.Add(param);
            }
            else
            {
                Console.WriteLine("WARNING: Updating data and variant type in parameter " + id);
                param.content = data;
                param.variant = variant;
            }
            return param;
        }

        /* Remove a parameter from the entity */
        public void RemoveParameter(string name)
        {
            ShortGuid name_id = ShortGuidUtils.Generate(name);
            parameters.RemoveAll(o => o.shortGUID == name_id);
        }

        /* Add a link from a parameter on us out to a parameter on another entity */
        public void AddParameterLink(string parameter, Entity childEntity, string childParameter)
        {
            childLinks.Add(new EntityLink(childEntity.shortGUID, ShortGuidUtils.Generate(parameter), ShortGuidUtils.Generate(childParameter)));
        }

        /* Remove a link to another entity */
        public void RemoveParameterLink(string parameter, Entity childEntity, string childParameter)
        {
            ShortGuid parameter_id = ShortGuidUtils.Generate(parameter);
            ShortGuid childParameter_id = ShortGuidUtils.Generate(childParameter);
            //TODO: do we want to do RemoveAll? should probably just remove the first
            childLinks.RemoveAll(o => o.parentParamID == parameter_id && o.childID == childEntity.shortGUID && o.childParamID == childParameter_id);
        }
    }
}
namespace CATHODE.Scripting
{ 
    [Serializable]
    public class VariableEntity : Entity
    {
        public VariableEntity(bool addDefaultParam = false) : base(EntityVariant.DATATYPE) { if (addDefaultParam) AddDefaultParam(); }
        public VariableEntity(ShortGuid shortGUID, bool addDefaultParam = false) : base(shortGUID, EntityVariant.DATATYPE) { if (addDefaultParam) AddDefaultParam(); }

        public VariableEntity(string parameter, DataType type, bool addDefaultParam = false) : base(EntityVariant.DATATYPE)
        {
            this.parameter = ShortGuidUtils.Generate(parameter);
            this.type = type;
            if (addDefaultParam) AddDefaultParam();
        }

        public VariableEntity(ShortGuid shortGUID, ShortGuid parameter, DataType type, bool addDefaultParam = false) : base(shortGUID, EntityVariant.DATATYPE)
        {
            this.parameter = parameter;
            this.type = type;
            if (addDefaultParam) AddDefaultParam();
        }
        public VariableEntity(ShortGuid shortGUID, string parameter, DataType type, bool addDefaultParam = false) : base(shortGUID, EntityVariant.DATATYPE)
        {
            this.parameter = ShortGuidUtils.Generate(parameter);
            this.type = type;
            if (addDefaultParam) AddDefaultParam();
        }
        
        /* Add a default parameter on us when created, to provide a value from */
        private void AddDefaultParam()
        {
            ParameterData thisParam = null;
            switch (type)
            {
                case DataType.STRING:
                    thisParam = new cString("");
                    break;
                case DataType.FLOAT:
                    thisParam = new cFloat(0.0f);
                    break;
                case DataType.INTEGER:
                    thisParam = new cInteger(0);
                    break;
                case DataType.BOOL:
                    thisParam = new cBool(true);
                    break;
                case DataType.VECTOR:
                    thisParam = new cVector3(new Vector3(0, 0, 0));
                    break;
                case DataType.TRANSFORM:
                    thisParam = new cTransform(new Vector3(0, 0, 0), new Vector3(0, 0, 0));
                    break;
                case DataType.ENUM:
                    thisParam = new cEnum(EnumType.ALERTNESS_STATE, 0);
                    break;
                case DataType.SPLINE:
                    thisParam = new cSpline();
                    break;
            }
            parameters.Add(new Parameter(parameter, thisParam));
        }

        public ShortGuid parameter; //Translates to string via ShortGuidUtils.FindString
        public DataType type = DataType.NONE;

        public override string ToString()
        {
            return parameter.ToString();
        }
    }
    [Serializable]
    public class FunctionEntity : Entity
    {
        public FunctionEntity() : base(EntityVariant.FUNCTION) { }
        public FunctionEntity(ShortGuid shortGUID) : base(shortGUID, EntityVariant.FUNCTION) { }

        public FunctionEntity(string function, bool autoGenerateParameters = false) : base(EntityVariant.FUNCTION)
        {
            this.function = ShortGuidUtils.Generate(function);
            if (autoGenerateParameters) EntityUtils.ApplyDefaults(this);
        }
        public FunctionEntity(ShortGuid function, bool autoGenerateParameters = false) : base(EntityVariant.FUNCTION)
        {
            this.function = function;
            if (autoGenerateParameters) EntityUtils.ApplyDefaults(this);
        }
        public FunctionEntity(FunctionType function, bool autoGenerateParameters = false) : base(EntityVariant.FUNCTION)
        {
            this.function = CommandsUtils.GetFunctionTypeGUID(function);
            if (autoGenerateParameters) EntityUtils.ApplyDefaults(this);
        }

        public FunctionEntity(ShortGuid shortGUID, ShortGuid function, bool autoGenerateParameters = false) : base(shortGUID, EntityVariant.FUNCTION)
        {
            this.function = function;
            if (autoGenerateParameters) EntityUtils.ApplyDefaults(this);
        }
        public FunctionEntity(ShortGuid shortGUID, string function, bool autoGenerateParameters = false) : base(shortGUID, EntityVariant.FUNCTION)
        {
            this.function = ShortGuidUtils.Generate(function);
            if (autoGenerateParameters) EntityUtils.ApplyDefaults(this);
        }
        public FunctionEntity(ShortGuid shortGUID, FunctionType function, bool autoGenerateParameters = false) : base(shortGUID, EntityVariant.FUNCTION)
        {
            this.function = CommandsUtils.GetFunctionTypeGUID(function);
            if (autoGenerateParameters) EntityUtils.ApplyDefaults(this);
        }

        public ShortGuid function; //Translates to string via ShortGuidUtils.FindString
        public List<ResourceReference> resources = new List<ResourceReference>(); //TODO: can we replace this with a cResource to save duplicating functionality?

        /* Add a new resource reference of type */
        public ResourceReference AddResource(ResourceType type)
        {
            //We can only have one type of resource reference per function entity, so if it already exists, we just return the existing one.
            ResourceReference rr = GetResource(type);
            if (rr == null)
            {
                rr = new ResourceReference(type);
                rr.resourceID = shortGUID;
                switch (rr.entryType)
                {
                    case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                    case ResourceType.RENDERABLE_INSTANCE:
                    case ResourceType.ANIMATED_MODEL:
                        rr.startIndex = 0;
                        break;
                }
                resources.Add(rr);
            }
            return rr;
        }

        /* Find a resource reference of type */
        public ResourceReference GetResource(ResourceType type)
        {
            return resources.FirstOrDefault(o => o.entryType == type);
        }

        public override string ToString()
        {
            return function.ToString();
        }
    }
    [Serializable]
    public class ProxyEntity : Entity
    {
        public ProxyEntity(ShortGuid shortGUID) : base(shortGUID, EntityVariant.PROXY) { }

        public ShortGuid extraId; //TODO: I'm unsure if this is actually used by the game - we might not need to store it and just make up something when we write.
        public List<ShortGuid> hierarchy = new List<ShortGuid>();
    }
    [Serializable]
    public class OverrideEntity : Entity
    {
        public OverrideEntity(ShortGuid shortGUID) : base(shortGUID, EntityVariant.OVERRIDE) { }

        public ShortGuid checksum; //TODO: This value is apparently a hash of the hierarchy GUIDs, but need to verify that, and work out the salt.
        public List<ShortGuid> hierarchy = new List<ShortGuid>();
    }

    #region SPECIAL FUNCTION ENTITIES
    [Serializable]
    public class CAGEAnimation : FunctionEntity
    {
        public CAGEAnimation(bool autoGenerateParameters = false) : base(FunctionType.CAGEAnimation, autoGenerateParameters) { }
        public CAGEAnimation(ShortGuid id, bool autoGenerateParameters = false) : base(id, FunctionType.CAGEAnimation, autoGenerateParameters) { }

        public List<CathodeParameterKeyframeHeader> keyframeHeaders = new List<CathodeParameterKeyframeHeader>();
        public List<CathodeParameterKeyframe> keyframeData = new List<CathodeParameterKeyframe>();
        public List<TEMP_CAGEAnimationExtraDataHolder3> paramsData3 = new List<TEMP_CAGEAnimationExtraDataHolder3>(); //events?
    }
    [Serializable]
    public class TriggerSequence : FunctionEntity
    {
        public TriggerSequence(bool autoGenerateParameters = false) : base(FunctionType.TriggerSequence, autoGenerateParameters) { }
        public TriggerSequence(ShortGuid id, bool autoGenerateParameters = false) : base(id, FunctionType.TriggerSequence, autoGenerateParameters) { }

        public List<CathodeTriggerSequenceTrigger> triggers = new List<CathodeTriggerSequenceTrigger>();
        public List<CathodeTriggerSequenceEvent> events = new List<CathodeTriggerSequenceEvent>();
    }
    #endregion

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EntityLink
    {
        public EntityLink(ShortGuid childEntityID, ShortGuid parentParam, ShortGuid childParam)
        {
            connectionID = ShortGuidUtils.GenerateRandom();
            parentParamID = parentParam;
            childParamID = childParam;
            childID = childEntityID;
        }

        public ShortGuid connectionID;  //The unique ID for this connection
        public ShortGuid parentParamID; //The ID of the parameter we're providing out 
        public ShortGuid childParamID;  //The ID of the parameter we're providing into the child
        public ShortGuid childID;       //The ID of the entity we're linking to to provide the value for
    }
}
