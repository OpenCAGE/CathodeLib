using CATHODE.Scripting.Internal;
#if DEBUG
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
#endif
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.InteropServices;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
using System.IO;
using CathodeLib;
#endif

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
#if DEBUG
        [JsonConverter(typeof(StringEnumConverter))]
#endif
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
            return parameters.FirstOrDefault(o => o.name == id);
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
        public Parameter AddParameter(string name, DataType type, ParameterVariant variant = ParameterVariant.PARAMETER)
        {
            return AddParameter(ShortGuidUtils.Generate(name), type, variant);
        }
        public Parameter AddParameter(ShortGuid id, DataType type, ParameterVariant variant = ParameterVariant.PARAMETER)
        {
            ParameterData data = null;
            switch (type)
            {
                case DataType.STRING:
                    data = new cString();
                    break;
                case DataType.FLOAT:
                    data = new cFloat();
                    break;
                case DataType.INTEGER:
                    data = new cInteger();
                    break;
                case DataType.BOOL:
                    data = new cBool();
                    break;
                case DataType.VECTOR:
                    data = new cVector3();
                    break;
                case DataType.TRANSFORM:
                    data = new cTransform();
                    break;
                case DataType.ENUM:
                    data = new cEnum();
                    break;
                case DataType.SPLINE:
                    data = new cSpline();
                    break;
                case DataType.RESOURCE:
                    data = new cResource(shortGUID);
                    break;
                default:
                    Console.WriteLine("WARNING: Tried to add parameter of type which is currently unsupported by CathodeLib (" + type + ")");
                    return null;
            }
            return AddParameter(id, data, variant);
        }
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
            parameters.RemoveAll(o => o.name == name_id);
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
        public VariableEntity(bool addDefaultParam = false) : base(EntityVariant.VARIABLE) { if (addDefaultParam) AddParameter(name, type); }
        public VariableEntity(ShortGuid shortGUID, bool addDefaultParam = false) : base(shortGUID, EntityVariant.VARIABLE) { if (addDefaultParam) AddParameter(name, type); }

        public VariableEntity(string parameter, DataType type, bool addDefaultParam = false) : base(EntityVariant.VARIABLE)
        {
            this.name = ShortGuidUtils.Generate(parameter);
            this.type = type;
            if (addDefaultParam) AddParameter(name, type);
        }

        public VariableEntity(ShortGuid shortGUID, ShortGuid parameter, DataType type, bool addDefaultParam = false) : base(shortGUID, EntityVariant.VARIABLE)
        {
            this.name = parameter;
            this.type = type;
            if (addDefaultParam) AddParameter(name, type);
        }
        public VariableEntity(ShortGuid shortGUID, string parameter, DataType type, bool addDefaultParam = false) : base(shortGUID, EntityVariant.VARIABLE)
        {
            this.name = ShortGuidUtils.Generate(parameter);
            this.type = type;
            if (addDefaultParam) AddParameter(name, type);
        }

        public ShortGuid name;
#if DEBUG
        [JsonConverter(typeof(StringEnumConverter))]
#endif
        public DataType type = DataType.NONE;

        public override string ToString()
        {
            return name.ToString();
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

        public ShortGuid function;
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
                        rr.index = 0;
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
        public ProxyEntity() : base(EntityVariant.PROXY) { }
        public ProxyEntity(ShortGuid shortGUID) : base(shortGUID, EntityVariant.PROXY) { }

        public ProxyEntity(List<ShortGuid> hierarchy = null, ShortGuid targetType = new ShortGuid(), bool autoGenerateParameters = false) : base(EntityVariant.PROXY)
        {
            this.targetType = targetType;
            if (hierarchy != null) this.connectedEntity.hierarchy = hierarchy;
            if (autoGenerateParameters) EntityUtils.ApplyDefaults(this);
        }
        public ProxyEntity(ShortGuid shortGUID, List<ShortGuid> hierarchy = null, ShortGuid targetType = new ShortGuid(), bool autoGenerateParameters = false) : base(shortGUID, EntityVariant.PROXY)
        {
            this.shortGUID = shortGUID;
            this.targetType = targetType; 
            if (hierarchy != null) this.connectedEntity.hierarchy = hierarchy;
            if (autoGenerateParameters) EntityUtils.ApplyDefaults(this);
        }

        public ShortGuid targetType; //The "function" value on the entity we're pointing to
        public EntityHierarchy connectedEntity = new EntityHierarchy();
    }
    [Serializable]
    public class OverrideEntity : Entity // Known as "alias" entities in-code
    {
        public OverrideEntity() : base(EntityVariant.OVERRIDE) { }
        public OverrideEntity(ShortGuid shortGUID) : base(shortGUID, EntityVariant.OVERRIDE) { }

        public OverrideEntity(List<ShortGuid> hierarchy = null) : base(EntityVariant.OVERRIDE)
        {
            if (hierarchy != null) this.connectedEntity.hierarchy = hierarchy;
        }
        public OverrideEntity(ShortGuid shortGUID, List<ShortGuid> hierarchy = null) : base(shortGUID, EntityVariant.OVERRIDE)
        {
            this.shortGUID = shortGUID;
            if (hierarchy != null) this.connectedEntity.hierarchy = hierarchy;
        }

        public EntityHierarchy connectedEntity = new EntityHierarchy();
    }

    #region SPECIAL FUNCTION ENTITIES
    [Serializable]
    public class CAGEAnimation : FunctionEntity
    {
        public CAGEAnimation(bool autoGenerateParameters = false) : base(FunctionType.CAGEAnimation, autoGenerateParameters) { }
        public CAGEAnimation(ShortGuid id, bool autoGenerateParameters = false) : base(id, FunctionType.CAGEAnimation, autoGenerateParameters) { }

        public List<Connection> connections = new List<Connection>();
        public List<Animation> animations = new List<Animation>();
        public List<Event> events = new List<Event>();

        [Serializable]
        public class Connection
        {
            public ShortGuid shortGUID; //Unique ID - TODO: can we just generate this?
            public ShortGuid keyframeID; //The keyframe ID we're pointing to

#if DEBUG
            [JsonConverter(typeof(StringEnumConverter))]
#endif
            public ObjectType objectType; //The type of object at the connected entity

            //Specifics for the parameter we're connected to
            public ShortGuid parameterID;
#if DEBUG
            [JsonConverter(typeof(StringEnumConverter))]
#endif
            public DataType parameterDataType; 
            public ShortGuid parameterSubID; //if parameterID is position, this might be x for example

            //The path to the connected entity which has the above parameter
            public EntityHierarchy connectedEntity = new EntityHierarchy(); 
        }

        [Serializable]
        public class Animation
        {
            public ShortGuid shortGUID;
            public List<Keyframe> keyframes = new List<Keyframe>();

            [Serializable]
            public class Keyframe
            {
                public float secondsSinceStart = 0.0f;
                public float paramValue = 0.0f;

                public Vector2 startVelocity = new Vector2(1,0);
                public Vector2 endVelocity = new Vector2(1, 0);
            }
        }

        [Serializable]
        public class Event
        {
            public ShortGuid shortGUID;
            public List<Keyframe> keyframes = new List<Keyframe>();

            [Serializable]
            public class Keyframe
            {
                public float secondsSinceStart = 0.0f;
                public ShortGuid start;

                public ShortGuid unk3; //this never translates to a string, but is a param on the node -> do we trigger it?
            }
        }
    }
    [Serializable]
    public class TriggerSequence : FunctionEntity
    {
        public TriggerSequence(bool autoGenerateParameters = false) : base(FunctionType.TriggerSequence, autoGenerateParameters) { }
        public TriggerSequence(ShortGuid id, bool autoGenerateParameters = false) : base(id, FunctionType.TriggerSequence, autoGenerateParameters) { }

        public List<Entity> entities = new List<Entity>();
        public List<Event> events = new List<Event>();

        [Serializable]
        public class Entity
        {
            public float timing = 0.0f;
            public EntityHierarchy connectedEntity = new EntityHierarchy();
        }
        [Serializable]
        public class Event
        {
            public Event() { }
            public Event(ShortGuid start, ShortGuid end) 
            {
                this.start = start;
                this.end = end;
                shortGUID = ShortGuidUtils.GenerateRandom();
            }

            public ShortGuid start;
            public ShortGuid shortGUID; 
            public ShortGuid end;
        }
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

    /// <summary>
    /// This is a class to handle hierarchies pointing to entities in Commands.
    /// Provides useful functionality for generating checksums (used for overrides in Commands), as well as composite instance IDs (used for legacy systems).
    /// Also has methods of capturing the entity pointed to and writing the hierarchies neatly.
    /// The hierarchy should always be written to Commands with a trailing ShortGuid.Invalid.
    /// </summary>
    [Serializable]
#if DEBUG
    [JsonConverter(typeof(EntityHierarchyConverter))]
#endif
    public class EntityHierarchy
    {
        public EntityHierarchy() { }
        public EntityHierarchy(List<ShortGuid> _hierarchy)
        {
            hierarchy = _hierarchy;

            if (hierarchy[hierarchy.Count - 1] != ShortGuid.Invalid)
                hierarchy.Add(ShortGuid.Invalid);
        }
        public List<ShortGuid> hierarchy = new List<ShortGuid>();

        public static bool operator ==(EntityHierarchy x, EntityHierarchy y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
            if (x.hierarchy.Count != y.hierarchy.Count) return false;
            for (int i = 0; i < x.hierarchy.Count; i++)
            {
                if (x.hierarchy[i].ToByteString() != y.hierarchy[i].ToByteString())
                    return false;
            }
            return true;
        }
        public static bool operator !=(EntityHierarchy x, EntityHierarchy y)
        {
            return !(x == y);
        }

        public override bool Equals(object obj)
        {
            return obj is EntityHierarchy hierarchy &&
                   EqualityComparer<List<ShortGuid>>.Default.Equals(this.hierarchy, hierarchy.hierarchy);
        }

        public override int GetHashCode()
        {
            return 218564712 + EqualityComparer<List<ShortGuid>>.Default.GetHashCode(hierarchy);
        }

        /* Get this hierarchy as a string */
        public string GetHierarchyAsString()
        {
            string val = "";
            for (int i = 0; i < hierarchy.Count; i++)
            {
                val += hierarchy[i].ToByteString();
                if (i != hierarchy.Count - 1) val += " -> ";
            }
            return val;
        }
        public string GetHierarchyAsString(Commands commands, Composite composite, bool withIDs = true)
        {
            CommandsUtils.ResolveHierarchy(commands, composite, hierarchy, out Composite comp, out string str, withIDs);
            return str;
        }

        public UInt32 ToUInt32()
        {
            UInt32 val = 0;
            for (int i = 0; i < hierarchy.Count; i++) val += hierarchy[i].ToUInt32();
            return val;
        }

        /* Get the entity this hierarchy points to: FROM THE ENTRY POINT OF THE COMMANDS */
        public Entity GetPointedEntity(Commands commands)
        {
            return CommandsUtils.ResolveHierarchy(commands, commands.EntryPoints[0], hierarchy, out Composite comp, out string str);
        }

        /* Get the entity this hierarchy points to: FROM THE ENTRY POINT OF THE COMMANDS, RETURNING THE CONTAINED COMPOSITE */
        public Entity GetPointedEntity(Commands commands, out Composite containedComposite)
        {
            Entity ent = CommandsUtils.ResolveHierarchy(commands, commands.EntryPoints[0], hierarchy, out Composite comp, out string str);
            containedComposite = comp;
            return ent;
        }

        /* Get the entity this hierarchy points to: FROM A SPECIFIED COMPOSITE */
        public Entity GetPointedEntity(Commands commands, Composite startComposite)
        {
            return CommandsUtils.ResolveHierarchy(commands, startComposite, hierarchy, out Composite comp, out string str);
        }

        /* Get the entity this hierarchy points to: FROM A SPECIFIED COMPOSITE, RETURNING THE CONTAINED COMPOSITE */
        public Entity GetPointedEntity(Commands commands, Composite startComposite, out Composite containedComposite)
        {
            Entity ent = CommandsUtils.ResolveHierarchy(commands, startComposite, hierarchy, out Composite comp, out string str);
            containedComposite = comp;
            return ent;
        }

        /* Get the ID of the entity that this hierarchy points to */
        public ShortGuid GetPointedEntityID()
        {
            hierarchy.Reverse();
            ShortGuid id = ShortGuid.Invalid;
            for (int i = 0; i < hierarchy.Count; i++)
            {
                if (hierarchy[i] == ShortGuid.Invalid) continue;
                id = hierarchy[i];
                break;
            }
            hierarchy.Reverse();
            return id;
        }

        /* Does this hierarchy point to a valid entity? */
        public bool IsHierarchyValid(Commands commands, Composite composite)
        {
            return GetPointedEntity(commands, composite) != null;
        }

        /* Generate the checksum used identify the hierarchy */
        public ShortGuid GenerateChecksum()
        {
            if (hierarchy.Count == 0) return ShortGuid.Invalid;
            if (hierarchy[hierarchy.Count - 1] != ShortGuid.Invalid) hierarchy.Add(ShortGuid.Invalid);

            hierarchy.Reverse();
            ShortGuid checksumGenerated = hierarchy[0];
            for (int i = 0; i < hierarchy.Count; i++)
            {
                checksumGenerated = checksumGenerated.Combine(hierarchy[i + 1]);
                if (i == hierarchy.Count - 2) break;
            }
            hierarchy.Reverse();

            return checksumGenerated;
        }

        /* Generate the instance ID used to identify the instanced composite we're executed in */
        public ShortGuid GenerateInstance()
        {
            //TODO: This hijacks the usual use for this class, need to tidy it up
            ShortGuid entityID = GetPointedEntityID();
            hierarchy.Insert(0, ShortGuid.InitialiserBase);
            hierarchy.Remove(entityID);
            hierarchy.Reverse();
            ShortGuid instanceGenerated = hierarchy[0];
            for (int i = 0; i < hierarchy.Count; i++)
            {
                if (i == hierarchy.Count - 1) break;
                instanceGenerated = hierarchy[i + 1].Combine(instanceGenerated);
            }
            hierarchy.Reverse();
            hierarchy.RemoveAt(0);
            hierarchy.RemoveAll(o => o == ShortGuid.Invalid);
            hierarchy.Add(entityID);
            hierarchy.Add(ShortGuid.Invalid);
            return instanceGenerated;
        }
    }

#if DEBUG
    public class EntityHierarchyConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((EntityHierarchy)value).GetHierarchyAsString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            EntityHierarchy e = new EntityHierarchy();
            List<string> vals = reader.Value.ToString().Split(new[] { " -> " }, StringSplitOptions.None).ToList();
            for (int i = 0; i < vals.Count; i++) e.hierarchy.Add(new ShortGuid(vals[i]));
            return e;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(EntityHierarchy);
        }
    }
#endif
}
