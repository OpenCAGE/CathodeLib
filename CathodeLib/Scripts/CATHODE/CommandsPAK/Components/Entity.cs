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
using CathodeLib.Properties;
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

        public List<EntityConnector> childLinks = new List<EntityConnector>();
        public List<Parameter> parameters = new List<Parameter>();

        ~Entity()
        {
            childLinks.Clear();
            parameters.Clear();
        }

        /* Implements IComparable for searching */
        public int CompareTo(Entity other)
        {
            if (other == null) return 1;

            if (this.shortGUID.ToUInt32() > other.shortGUID.ToUInt32())
                return 1;
            else if (this.shortGUID.ToUInt32() < other.shortGUID.ToUInt32())
                return -1;

            return 0;
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

        /* Get all links out matching the given name */
        public List<EntityConnector> GetLinksOut(string name)
        {
            ShortGuid id = ShortGuidUtils.Generate(name);
            return GetLinksOut(id);
        }
        public List<EntityConnector> GetLinksOut(ShortGuid id)
        {
            return childLinks.FindAll(o => o.thisParamID == id);
        }

        /* Get all links in matching the given name */
        public List<EntityConnector> GetLinksIn(string name, Composite comp)
        {
            ShortGuid id = ShortGuidUtils.Generate(name);
            return GetLinksIn(id, comp);
        }
        public List<EntityConnector> GetLinksIn(ShortGuid id, Composite comp)
        {
            List<EntityConnector> parent_links = GetParentLinks(comp);
            return parent_links.FindAll(o => o.linkedParamID == id && o.linkedEntityID == this.shortGUID);
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
            childLinks.Add(new EntityConnector(childEntity.shortGUID, ShortGuidUtils.Generate(parameter), ShortGuidUtils.Generate(childParameter)));
        }

        /* Remove a link to another entity */
        public void RemoveParameterLink(string parameter, Entity childEntity, string childParameter)
        {
            ShortGuid parameter_id = ShortGuidUtils.Generate(parameter);
            ShortGuid childParameter_id = ShortGuidUtils.Generate(childParameter);
            //TODO: do we want to do RemoveAll? should probably just remove the first
            childLinks.RemoveAll(o => o.thisParamID == parameter_id && o.linkedEntityID == childEntity.shortGUID && o.linkedParamID == childParameter_id);
        }

        /* Utility: Find all links in to this entity (pass in the composite this entity is within) */
        public List<EntityConnector> GetParentLinks(Composite containedComposite)
        {
            List<EntityConnector> connections = new List<EntityConnector>();
            containedComposite.GetEntities().ForEach(ent => {
                ent.childLinks.ForEach(link =>
                {
                    if (link.linkedEntityID == shortGUID)
                    {
                        connections.Add(new EntityConnector()
                        {
                            ID = link.ID,
                            thisParamID = link.linkedParamID,
                            linkedParamID = link.thisParamID,
                            linkedEntityID = ent.shortGUID
                        });
                    }
                });
            });
            return connections;
        }

        /* Utility: Returns true if this entity has any links IN or OUT (pass in the composite this entity is within) */
        public bool HasLinks(Composite containedComposite)
        {
            if (childLinks.Count != 0)
                return true;

            List<Entity> entities = containedComposite.GetEntities();
            for (int i = 0; i < entities.Count; i++)
            {
                for (int x = 0; x < entities[i].childLinks.Count; x++)
                {
                    if (entities[i].childLinks[x].linkedEntityID == shortGUID)
                        return true;
                }
            }
            return false;
        }

        /* Utility: Remove all child links out from the given parameter */
        public void RemoveAllParameterLinksOut(string parameter)
        {
            ShortGuid parameter_id = ShortGuidUtils.Generate(parameter);
            childLinks.RemoveAll(o => o.thisParamID == parameter_id);
        }
        public void RemoveAllParameterLinksOut()
        {
            childLinks.Clear();
        }

        /* Utility: Remove all child links in to the given parameter */
        public void RemoveAllParameterLinksIn(string parameter, Composite comp)
        {
            ShortGuid parameter_id = ShortGuidUtils.Generate(parameter);
            List<EntityConnector> links_in = GetParentLinks(comp);
            foreach (EntityConnector link in links_in)
            {
                if (link.linkedParamID != parameter_id) continue;
                Entity ent = comp.GetEntityByID(link.linkedEntityID);
                if (ent == null) continue;
                ent.childLinks.RemoveAll(o => o.ID == link.ID);
            }
        }
        public void RemoveAllParameterLinksIn(Composite comp)
        {
            List<EntityConnector> links_in = GetParentLinks(comp);
            foreach (EntityConnector link in links_in)
            {
                Entity ent = comp.GetEntityByID(link.linkedEntityID);
                if (ent == null) continue;
                ent.childLinks.RemoveAll(o => o.ID == link.ID);
            }
        }

        /* Utility: Remove all child links in to and out of the given parameter */
        public void RemoveAllParameterLinks(string parameter, Composite comp)
        {
            RemoveAllParameterLinksIn(parameter, comp);
            RemoveAllParameterLinksOut(parameter);
        }

        /* Utility: Remove all child links in to and out of the given parameter */
        public void RemoveAllParameterLinks(Composite comp)
        {
            RemoveAllParameterLinksIn(comp);
            RemoveAllParameterLinksOut();
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

        ~FunctionEntity()
        {
            resources.Clear();
        }

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
                rr.resource_id = type == ResourceType.DYNAMIC_PHYSICS_SYSTEM ? ShortGuidUtils.Generate("DYNAMIC_PHYSICS_SYSTEM") : shortGUID;
                switch (rr.resource_type)
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

        /* Find a resource reference of type on the entity - will also check the "resource" parameter second if alsoCheckParameter is true */
        public ResourceReference GetResource(ResourceType type, bool alsoCheckParameter = false)
        {
            ResourceReference resource = resources.FirstOrDefault(o => o.resource_type == type);
            if (alsoCheckParameter && resource == null)
            {
                Parameter resourceParam = GetParameter("resource");
                if (resourceParam != null && resourceParam.content != null && resourceParam.content.dataType == DataType.RESOURCE)
                    resource = ((cResource)resourceParam.content).GetResource(ResourceType.COLLISION_MAPPING);
            }
            return resource;
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
            this.function = targetType;
            if (hierarchy != null) this.proxy.path = hierarchy;
            if (autoGenerateParameters) EntityUtils.ApplyDefaults(this);
        }
        public ProxyEntity(ShortGuid shortGUID, List<ShortGuid> hierarchy = null, ShortGuid targetType = new ShortGuid(), bool autoGenerateParameters = false) : base(shortGUID, EntityVariant.PROXY)
        {
            this.shortGUID = shortGUID;
            this.function = targetType; 
            if (hierarchy != null) this.proxy.path = hierarchy;
            if (autoGenerateParameters) EntityUtils.ApplyDefaults(this);
        }

        public ShortGuid function;                  //The "function" value on the entity we're proxying
        public EntityPath proxy = new EntityPath(); //A path to the entity we're proxying
    }
    [Serializable]
    public class AliasEntity : Entity
    {
        public AliasEntity() : base(EntityVariant.ALIAS) { }
        public AliasEntity(ShortGuid shortGUID) : base(shortGUID, EntityVariant.ALIAS) { }

        public AliasEntity(List<ShortGuid> hierarchy = null) : base(EntityVariant.ALIAS)
        {
            if (hierarchy != null) this.alias.path = hierarchy;
        }
        public AliasEntity(ShortGuid shortGUID, List<ShortGuid> hierarchy = null) : base(shortGUID, EntityVariant.ALIAS)
        {
            this.shortGUID = shortGUID;
            if (hierarchy != null) this.alias.path = hierarchy;
        }

        public EntityPath alias = new EntityPath(); //A path to the entity we're an alias of
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
            public EntityPath connectedEntity = new EntityPath(); 
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
            public EntityPath connectedEntity = new EntityPath();
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
    public struct EntityConnector
    {
        public EntityConnector(ShortGuid linkedEntity, ShortGuid param, ShortGuid linkedParam)
        {
            ID = ShortGuidUtils.GenerateRandom();

            thisParamID = param;
            linkedParamID = linkedParam;
            linkedEntityID = linkedEntity;
        }
        public EntityConnector(Entity linkedEntity, string param, string linkedParam)
        {
            ID = ShortGuidUtils.GenerateRandom();

            thisParamID = ShortGuidUtils.Generate(param);
            linkedParamID = ShortGuidUtils.Generate(linkedParam);
            linkedEntityID = linkedEntity.shortGUID;
        }

        public ShortGuid ID;   //The unique ID for this connection

        public ShortGuid thisParamID;    //The ID of the parameter
        public ShortGuid linkedParamID;  //The ID of the parameter we're connecting to
        public ShortGuid linkedEntityID; //The ID of the entity we're connecting to that has the linked parameter

        public Entity GetEntity(Composite comp)
        {
            return comp.GetEntityByID(linkedEntityID);
        }
    }

    /// <summary>
    /// This provides a way to handle paths to instances of entities within the root composite instance.
    /// The path should always be written to Commands with a trailing ShortGuid.Invalid.
    /// </summary>
    [Serializable]
#if DEBUG
    [JsonConverter(typeof(EntityPathConverter))]
#endif
    public class EntityPath
    {
        public EntityPath() { }
        public EntityPath(List<ShortGuid> _path)
        {
            path = _path;

            if (path.Count == 0 || path[path.Count - 1] != ShortGuid.Invalid)
                path.Add(ShortGuid.Invalid);
        }
        public List<ShortGuid> path = new List<ShortGuid>();

        public static bool operator ==(EntityPath x, EntityPath y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
            if (x.path.Count != y.path.Count) return false;
            for (int i = 0; i < x.path.Count; i++)
            {
                if (x.path[i].ToByteString() != y.path[i].ToByteString())
                    return false;
            }
            return true;
        }
        public static bool operator !=(EntityPath x, EntityPath y)
        {
            return !(x == y);
        }

        public override bool Equals(object obj)
        {
            return obj is EntityPath hierarchy &&
                   EqualityComparer<List<ShortGuid>>.Default.Equals(this.path, hierarchy.path);
        }

        public override int GetHashCode()
        {
            return 218564712 + EqualityComparer<List<ShortGuid>>.Default.GetHashCode(path);
        }

        /* Get this path as a string */
        public string GetAsString()
        {
            string val = "";
            for (int i = 0; i < path.Count; i++)
            {
                val += path[i].ToByteString();
                if (i != path.Count - 1) val += " -> ";
            }
            return val;
        }
        public string GetAsString(Commands commands, Composite composite, bool withIDs = true)
        {
            CommandsUtils.ResolveHierarchy(commands, composite, path, out Composite comp, out string str, withIDs);
            return str;
        }

        public UInt32 ToUInt32()
        {
            UInt32 val = 0;
            for (int i = 0; i < path.Count; i++) val += path[i].ToUInt32();
            return val;
        }

        /* Get the entity this path points to: FROM THE ROOT OF THE COMMANDS */
        public Entity GetPointedEntity(Commands commands)
        {
            return CommandsUtils.ResolveHierarchy(commands, commands.EntryPoints[0], path, out Composite comp, out string str);
        }

        /* Get the entity this path points to: FROM THE ROOT OF THE COMMANDS, RETURNING THE CONTAINED COMPOSITE */
        public Entity GetPointedEntity(Commands commands, out Composite containedComposite)
        {
            Entity ent = CommandsUtils.ResolveHierarchy(commands, commands.EntryPoints[0], path, out Composite comp, out string str);
            containedComposite = comp;
            return ent;
        }

        /* Get the entity this path points to: FROM A SPECIFIED COMPOSITE */
        public Entity GetPointedEntity(Commands commands, Composite startComposite)
        {
            return CommandsUtils.ResolveHierarchy(commands, startComposite, path, out Composite comp, out string str);
        }

        /* Get the entity this path points to: FROM A SPECIFIED COMPOSITE, RETURNING THE CONTAINED COMPOSITE */
        public Entity GetPointedEntity(Commands commands, Composite startComposite, out Composite containedComposite)
        {
            Entity ent = CommandsUtils.ResolveHierarchy(commands, startComposite, path, out Composite comp, out string str);
            containedComposite = comp;
            return ent;
        }

        /* Get the ID of the entity that this path points to */
        public ShortGuid GetPointedEntityID()
        {
            path.Reverse();
            ShortGuid id = ShortGuid.Invalid;
            for (int i = 0; i < path.Count; i++)
            {
                if (path[i] == ShortGuid.Invalid) continue;
                id = path[i];
                break;
            }
            path.Reverse();
            return id;
        }

        /* Does this path point to a valid entity? */
        public bool IsPathValid(Commands commands, Composite composite)
        {
            return GetPointedEntity(commands, composite) != null;
        }

        /* Generate the checksum used identify the path */
        public ShortGuid GeneratePathHash()
        {
            if (path.Count == 0) return ShortGuid.Invalid;
            if (path[path.Count - 1] != ShortGuid.Invalid) path.Add(ShortGuid.Invalid);

            path.Reverse();
            ShortGuid checksumGenerated = path[0];
            for (int i = 0; i < path.Count; i++)
            {
                checksumGenerated = checksumGenerated.Combine(path[i + 1]);
                if (i == path.Count - 2) break;
            }
            path.Reverse();

            return checksumGenerated;
        }

        /* Generate the instance ID used to identify the instanced composite we're executed in */
        public ShortGuid GenerateInstance()
        {
            //TODO: This hijacks the usual use for this class, need to tidy it up
            ShortGuid entityID = GetPointedEntityID();
            path.Insert(0, ShortGuid.InitialiserBase);
            path.Remove(entityID);
            path.Reverse();
            ShortGuid instanceGenerated = path[0];
            for (int i = 0; i < path.Count; i++)
            {
                if (i == path.Count - 1) break;
                instanceGenerated = path[i + 1].Combine(instanceGenerated);
            }
            path.Reverse();
            path.RemoveAt(0);
            path.RemoveAll(o => o == ShortGuid.Invalid);
            path.Add(entityID);
            path.Add(ShortGuid.Invalid);
            return instanceGenerated;
        }

        /* Generate a zone ID (use this when the EntityHandle points to a Zone entity) */
        public ShortGuid GenerateZoneID()
        {
            return new ShortGuid(0 + GenerateInstance().ToUInt32() + GetPointedEntityID().ToUInt32() + 1);
        }
        
        /* Updates this path to have the path to another entity prepended to it */
        public void PrependPath(EntityPath otherPath)
        {
            int length = otherPath.path[otherPath.path.Count - 1] == ShortGuid.Invalid ? otherPath.path.Count - 2 : otherPath.path.Count - 1;
            for (int i = 0; i < length; i++)
                path.Insert(i, otherPath.path[i]);
        }
    }

#if DEBUG
    public class EntityPathConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((EntityPath)value).GetAsString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            EntityPath e = new EntityPath();
            List<string> vals = reader.Value.ToString().Split(new[] { " -> " }, StringSplitOptions.None).ToList();
            for (int i = 0; i < vals.Count; i++) e.path.Add(new ShortGuid(vals[i]));
            return e;
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(EntityPath);
        }
    }
#endif
}
