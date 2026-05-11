using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;

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
    /// <summary>
    /// An entity in a composite
    /// </summary>
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

        public List<EntityConnector> childLinks = new List<EntityConnector>();
        public List<Parameter> parameters = new List<Parameter>();

        ~Entity()
        {
            childLinks.Clear();
            parameters.Clear();
        }

        /// <summary>
        /// Implements IComparable for searching
        /// </summary>
        public int CompareTo(Entity other)
        {
            if (other == null) return 1;

            if (this.shortGUID.AsUInt32 > other.shortGUID.AsUInt32)
                return 1;
            else if (this.shortGUID.AsUInt32 < other.shortGUID.AsUInt32)
                return -1;

            return 0;
        }

        /// <summary>
        /// Get parameter by string name or ShortGuid
        /// </summary>
        public Parameter GetParameter(string name)
        {
            ShortGuid id = ShortGuidUtils.Generate(name);
            return GetParameter(id);
        }
        public Parameter GetParameter(ShortGuid id)
        {
            return parameters.FirstOrDefault(o => o.name == id);
        }

        /// <summary>
        /// Get all links out matching the given name
        /// </summary>
        public List<EntityConnector> GetLinksOut(string name)
        {
            ShortGuid id = ShortGuidUtils.Generate(name);
            return GetLinksOut(id);
        }
        public List<EntityConnector> GetLinksOut(ShortGuid id)
        {
            return childLinks.FindAll(o => o.thisParamID == id);
        }

        /// <summary>
        /// Get all links in matching the given name
        /// </summary>
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

        /// <summary>
        /// Add a data-supplying parameter to the entity
        /// </summary>
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
        public Parameter AddParameter(string name, DataType type, ParameterVariant variant = ParameterVariant.PARAMETER, bool overwriteIfExists = true)
        {
            return AddParameter(ShortGuidUtils.Generate(name), type, variant, overwriteIfExists);
        }
        public Parameter AddParameter(ShortGuid id, DataType type, ParameterVariant variant = ParameterVariant.PARAMETER, bool overwriteIfExists = true)
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
                case DataType.ENUM_STRING:
                    data = new cEnumString();
                    break;
                case DataType.SPLINE:
                    data = new cSpline();
                    break;
                case DataType.RESOURCE:
                    data = new cResource(shortGUID);
                    break;
                default:
                    data = new cFloat();
                    Console.WriteLine("WARNING: Tried to add parameter of type which is currently unsupported by CathodeLib (" + type + ") - falling back to FLOAT"); //todo: should we fall to STRING?
                    break;
            }
            return AddParameter(id, data, variant, overwriteIfExists);
        }
        public Parameter AddParameter(string name, ParameterData data, ParameterVariant variant = ParameterVariant.PARAMETER, bool overwriteIfExists = true)
        {
            return AddParameter(ShortGuidUtils.Generate(name), data, variant, overwriteIfExists);
        }
        public Parameter AddParameter(ShortGuid id, ParameterData data, ParameterVariant variant = ParameterVariant.PARAMETER, bool overwriteIfExists = true)
        {
            if (data == null)
                Console.WriteLine("WARNING: Entity " + this.shortGUID + " (" + this.variant + ") has null parameter data for " + id.ToString());

            Parameter param = GetParameter(id);
            //TODO: we should also take inputs and outputs into account here??
            if (param == null)
            {
                param = new Parameter(id, data, variant);
                parameters.Add(param);
            }
            else if (overwriteIfExists)
            {
                param.content = data;
                param.variant = variant;
            }
            return param;
        }

        /// <summary>
        /// Remove a parameter from the entity
        /// </summary>
        public bool RemoveParameter(string name)
        {
            ShortGuid name_id = ShortGuidUtils.Generate(name);
            return RemoveParameter(name_id);
        }
        public bool RemoveParameter(Parameter param)
        {
            return RemoveParameter(param.name);
        }
        public bool RemoveParameter(ShortGuid guid)
        {
            int count = parameters.RemoveAll(o => o.name == guid);
            return count != 0;
        }

        /// <summary>
        /// Add a link from a parameter on us out to a parameter on another entity
        /// </summary>
        public void AddParameterLink(string parameter, Entity childEntity, string childParameter)
        {
            childLinks.Add(new EntityConnector(childEntity.shortGUID, ShortGuidUtils.Generate(parameter), ShortGuidUtils.Generate(childParameter)));
        }
        public void AddParameterLink(Parameter parameter, Entity childEntity, Parameter childParameter)
        {
            childLinks.Add(new EntityConnector(childEntity.shortGUID, parameter.name, childParameter.name));
        }
        public void AddParameterLink(ShortGuid parameterGUID, Entity childEntity, ShortGuid childParameterGUID)
        {
            childLinks.Add(new EntityConnector(childEntity.shortGUID, parameterGUID, childParameterGUID));
        }
        public void AddParameterLink(ShortGuid parameterGUID, ShortGuid childEntityGUID, ShortGuid childParameterGUID)
        {
            childLinks.Add(new EntityConnector(childEntityGUID, parameterGUID, childParameterGUID));
        }

        /// <summary>
        /// Remove a link to another entity
        /// </summary>
        public void RemoveParameterLink(string parameter, Entity childEntity, string childParameter)
        {
            ShortGuid parameter_id = ShortGuidUtils.Generate(parameter);
            ShortGuid childParameter_id = ShortGuidUtils.Generate(childParameter);
            //TODO: do we want to do RemoveAll? should probably just remove the first
            childLinks.RemoveAll(o => o.thisParamID == parameter_id && o.linkedEntityID == childEntity.shortGUID && o.linkedParamID == childParameter_id);
        }
        public void RemoveParameterLink(Parameter parameter, Entity childEntity, Parameter childParameter)
        {
            childLinks.RemoveAll(o => o.thisParamID == parameter.name && o.linkedEntityID == childEntity.shortGUID && o.linkedParamID == childParameter.name);
        }
        public void RemoveParameterLink(ShortGuid parameterGUID, Entity childEntity, ShortGuid childParameterGUID)
        {
            childLinks.RemoveAll(o => o.thisParamID == parameterGUID && o.linkedEntityID == childEntity.shortGUID && o.linkedParamID == childParameterGUID);
        }
        public void RemoveParameterLink(ShortGuid parameterGUID, ShortGuid childEntityGUID, ShortGuid childParameterGUID)
        {
            childLinks.RemoveAll(o => o.thisParamID == parameterGUID && o.linkedEntityID == childEntityGUID && o.linkedParamID == childParameterGUID);
        }

        /// <summary>
        /// Utility: Find all links in to this entity (pass in the composite this entity is within)
        /// </summary>
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

        /// <summary>
        /// Utility: Returns true if this entity has any links IN or OUT (pass in the composite this entity is within)
        /// </summary>
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

        /// <summary>
        /// Utility: Remove all child links out from the given parameter
        /// </summary>
        public void RemoveAllParameterLinksOut(string parameter)
        {
            ShortGuid parameter_id = ShortGuidUtils.Generate(parameter);
            childLinks.RemoveAll(o => o.thisParamID == parameter_id);
        }
        public void RemoveAllParameterLinksOut()
        {
            childLinks.Clear();
        }

        /// <summary>
        /// Utility: Remove all child links in to the given parameter
        /// </summary>
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

        /// <summary>
        /// Utility: Remove all child links in to and out of the given parameter
        /// </summary>
        public void RemoveAllParameterLinks(string parameter, Composite comp)
        {
            RemoveAllParameterLinksIn(parameter, comp);
            RemoveAllParameterLinksOut(parameter);
        }

        /// <summary>
        /// Utility: Remove all child links in to and out of the given parameter
        /// </summary>
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
        public VariableEntity() : base(EntityVariant.VARIABLE) {  }
        public VariableEntity(ShortGuid shortGUID) : base(shortGUID, EntityVariant.VARIABLE) { }

        public VariableEntity(string parameter, DataType type) : base(EntityVariant.VARIABLE)
        {
            this.name = ShortGuidUtils.Generate(parameter);
            this.type = type;
        }

        public VariableEntity(ShortGuid shortGUID, ShortGuid parameter, DataType type) : base(shortGUID, EntityVariant.VARIABLE)
        {
            this.name = parameter;
            this.type = type;
        }
        public VariableEntity(ShortGuid shortGUID, string parameter, DataType type) : base(shortGUID, EntityVariant.VARIABLE)
        {
            this.name = ShortGuidUtils.Generate(parameter);
            this.type = type;
        }

        public ShortGuid name;
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

        public FunctionEntity(string function) : base(EntityVariant.FUNCTION)
        {
            this.function = ShortGuidUtils.Generate(function);
        }
        public FunctionEntity(Composite function) : base (EntityVariant.FUNCTION)
        {
            this.function = function.shortGUID;
        }
        public FunctionEntity(FunctionType function) : base(EntityVariant.FUNCTION)
        {
            this.function = new ShortGuid((uint)function);
        }

        public FunctionEntity(ShortGuid shortGUID, string function) : base(shortGUID, EntityVariant.FUNCTION)
        {
            this.function = ShortGuidUtils.Generate(function);
        }
        public FunctionEntity(ShortGuid shortGUID, Composite function) : base(shortGUID, EntityVariant.FUNCTION)
        {
            this.function = function.shortGUID;
        }
        public FunctionEntity(ShortGuid shortGUID, FunctionType function) : base(shortGUID, EntityVariant.FUNCTION)
        {
            this.function = new ShortGuid((uint)function);
        }

        public ShortGuid function;
        public List<ResourceReference> resources = new List<ResourceReference>(); //TODO: can we replace this with a cResource to save duplicating functionality?

        /// <summary>
        /// Add a new resource reference of type
        /// </summary>
        public ResourceReference AddResource(ResourceType type)
        {
            //We can only have one type of resource reference per function entity, so if it already exists, we just return the existing one.
            ResourceReference rr = GetResource(type);
            if (rr == null)
            {
                rr = new ResourceReference(type);
                rr.resource_id = type == ResourceType.DYNAMIC_PHYSICS_SYSTEM ? ShortGuids.DYNAMIC_PHYSICS_SYSTEM : shortGUID;
                resources.Add(rr);
            }
            return rr;
        }

        /// <summary>
        /// Find a resource reference of type on the entity - will also check the "resource" parameter second if alsoCheckParameter is true
        /// </summary>
        public ResourceReference GetResource(ResourceType type, bool alsoCheckParameter = false)
        {
            ResourceReference resource = resources.FirstOrDefault(o => o.resource_type == type);
            if (alsoCheckParameter && resource == null)
            {
                Parameter resourceParam = GetParameter(ShortGuids.resource);
                if (resourceParam != null && resourceParam.content != null && resourceParam.content.dataType == DataType.RESOURCE)
                    resource = ((cResource)resourceParam.content).GetResource(type);
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

        public ProxyEntity(ShortGuid[] hierarchy = null, ShortGuid targetType = new ShortGuid()) : base(EntityVariant.PROXY)
        {
            this.function = targetType;
            if (hierarchy != null) this.proxy = new EntityPath(hierarchy);
        }
        public ProxyEntity(ShortGuid shortGUID, ShortGuid[] hierarchy = null, ShortGuid targetType = new ShortGuid()) : base(shortGUID, EntityVariant.PROXY)
        {
            this.shortGUID = shortGUID;
            this.function = targetType; 
            if (hierarchy != null) this.proxy = new EntityPath(hierarchy);
        }

        public ShortGuid function;                  //The "function" value on the entity we're proxying
        public EntityPath proxy = new EntityPath(); //A path to the entity we're proxying
    }
    [Serializable]
    public class AliasEntity : Entity
    {
        public AliasEntity() : base(EntityVariant.ALIAS) { }
        public AliasEntity(ShortGuid shortGUID) : base(shortGUID, EntityVariant.ALIAS) { }

        public AliasEntity(ShortGuid[] hierarchy = null) : base(EntityVariant.ALIAS)
        {
            if (hierarchy != null) this.alias = new EntityPath(hierarchy);
        }
        public AliasEntity(ShortGuid shortGUID, ShortGuid[] hierarchy = null) : base(shortGUID, EntityVariant.ALIAS)
        {
            this.shortGUID = shortGUID;
            if (hierarchy != null) this.alias = new EntityPath(hierarchy);
        }

        public EntityPath alias = new EntityPath(); //A path to the entity we're an alias of
    }

    #region SPECIAL FUNCTION ENTITIES
    [Serializable]
    public class CAGEAnimation : FunctionEntity
    {
        public CAGEAnimation() : base(FunctionType.CAGEAnimation) { }
        public CAGEAnimation(ShortGuid id) : base(id, FunctionType.CAGEAnimation) { }

        public List<Connection> connections = new List<Connection>();
        public List<FloatTrack> animations = new List<FloatTrack>();
        public List<EventTrack> events = new List<EventTrack>();

        [Serializable]
        public class Connection
        {
            public ShortGuid binding_guid; //Unique ID - TODO: can we just generate this?
            public ShortGuid target_track; //The keyframe ID we're pointing to

            public ObjectType binding_type; //The type of object at the connected entity

            //Specifics for the parameter we're connected to
            public ShortGuid target_param;
            public DataType target_param_type; 
            public ShortGuid target_sub_param; //if parameterID is position, this might be x for example

            //The path to the connected entity which has the above parameter
            public EntityPath connectedEntity = new EntityPath();
        }

        public enum InterpolationMode
        {
            Invalid,
            Flat,
            Linear,
            Bezier,
        };

        public enum TrackType
        {
            FLOAT,
            FLOAT3,
            POSITION,
            STRING,
            GUID,
            MASTERING,

            INVALID = -1
        };

        [Serializable]
        public class FloatTrack
        {
            public ShortGuid shortGUID;
            public List<Keyframe> keyframes = new List<Keyframe>();

            [Serializable]
            public class Keyframe
            {
                public InterpolationMode mode;
                public float time = 0.0f;

                public Vector2 value = new Vector2(1, 0);

                public Vector2 tan_in = new Vector2(1,0);
                public Vector2 tan_out = new Vector2(1, 0);
            }
        }

        [Serializable]
        public class EventTrack
        {
            public ShortGuid shortGUID;
            public List<Keyframe> keyframes = new List<Keyframe>();

            [Serializable]
            public class Keyframe
            {
                public Keyframe() { }
                public Keyframe(float time, string event_name)
                {
                    this.time = time;
                    forward = ShortGuidUtils.Generate(event_name);
                    reverse = ShortGuidUtils.Generate("reverse_" + event_name);
                }

                public InterpolationMode mode;
                public float time = 0.0f;

                public ShortGuid forward;
                public ShortGuid reverse; //"reverse_" + forward

                public TrackType track_type;
                public float duration;
            }
        }
    }
    [Serializable]
    public class TriggerSequence : FunctionEntity
    {
        public TriggerSequence() : base(FunctionType.TriggerSequence) { }
        public TriggerSequence(ShortGuid id) : base(id, FunctionType.TriggerSequence) { }

        public List<SequenceEntry> sequence = new List<SequenceEntry>();
        public List<MethodEntry> methods = new List<MethodEntry>();

        [Serializable]
        public class SequenceEntry
        {
            public float timing = 0.0f;
            public EntityPath connectedEntity = new EntityPath();
        }
        [Serializable]
        public class MethodEntry
        {
            public MethodEntry() { }
            public MethodEntry(string method)
            {
                this.method = ShortGuidUtils.Generate(method);
                this.relay = ShortGuidUtils.Generate(method + "_relay");
                this.finished = ShortGuidUtils.Generate(method + "_finished");
            }
            public MethodEntry(ShortGuid method, ShortGuid relay, ShortGuid finished) 
            {
                this.method = method;
                this.relay = relay; //method + "_relay"
                this.finished = finished; //method + "_finished"
            }

            public ShortGuid method;
            public ShortGuid relay; 
            public ShortGuid finished;
        }
    }
    #endregion

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EntityConnector : IEquatable<EntityConnector>
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

        public ShortGuid ID;   //The unique ID for this connection - ignored in equality checks, but used to identify the connection in the composite (maybe should be removed)

        public ShortGuid thisParamID;    //The ID of the parameter
        public ShortGuid linkedParamID;  //The ID of the parameter we're connecting to
        public ShortGuid linkedEntityID; //The ID of the entity we're connecting to that has the linked parameter

        public Entity GetEntity(Composite comp)
        {
            return comp.GetEntityByID(linkedEntityID);
        }

        public override string ToString()
        {
            return "[" + thisParamID + "] -> " + linkedEntityID.ToByteString() + " [" + linkedParamID + "]";
        }

        public bool Equals(EntityConnector other)
        {
            return this.thisParamID.Equals(other.thisParamID) &&
                   this.linkedEntityID.Equals(other.linkedEntityID) &&
                   this.linkedParamID.Equals(other.linkedParamID);
        }

        public override bool Equals(object obj)
        {
            return obj is EntityConnector other && Equals(other);
        }

        public override int GetHashCode()
        {
            int hashCode = 937505261;
            hashCode = hashCode * -1521134295 + thisParamID.GetHashCode();
            hashCode = hashCode * -1521134295 + linkedParamID.GetHashCode();
            hashCode = hashCode * -1521134295 + linkedEntityID.GetHashCode();
            return hashCode;
        }

        public static bool operator ==(EntityConnector x, EntityConnector y)
        {
            if (x.thisParamID != y.thisParamID) return false;
            if (x.linkedParamID != y.linkedParamID) return false;
            if (x.linkedEntityID != y.linkedEntityID) return false;
            return true;
        }
        public static bool operator !=(EntityConnector x, EntityConnector y)
        {
            return !(x == y);
        }
    }

    /// <summary>
    /// This provides a way to handle paths to instances of entities within the root composite instance.
    /// The path should always be written to Commands with a trailing ShortGuid.Invalid.
    /// </summary>
    [Serializable]
    public class EntityPath
    {
        public EntityPath() { }
        public EntityPath(ShortGuid[] _path)
        {
            path = _path;
            EnsureFinalIsEmpty();
        }
        public ShortGuid[] path = new ShortGuid[0];
        public List<uint> pathUint
        {
            get
            {
                List<uint> p = new List<uint>();
                for (int i = 0; i < path.Length; i++)
                    p.Add(path[i].AsUInt32);
                return p;
            }
        }

        public static bool operator ==(EntityPath x, EntityPath y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
            if (x.path.Length != y.path.Length) return false;
            for (int i = 0; i < x.path.Length; i++)
            {
                if (x.path[i] != y.path[i])
                    return false;
            }
            return true;
        }
        public static bool operator !=(EntityPath x, EntityPath y)
        {
            return !(x == y);
        }

        public static EntityPath operator +(EntityPath x, EntityPath y)
        {
            List<ShortGuid> newPath = new List<ShortGuid>();
            newPath.AddRange(x.path);
            if (newPath.Count > 0 && newPath[newPath.Count - 1] == ShortGuid.Invalid)
                newPath.RemoveAt(newPath.Count - 1);
            newPath.AddRange(y.path);
            return new EntityPath(newPath.ToArray());
        }

        public override bool Equals(object obj)
        {
            return obj is EntityPath path &&
                   EqualityComparer<ShortGuid[]>.Default.Equals(this.path, path.path);
        }

        public override int GetHashCode()
        {
            return -1757656154 + EqualityComparer<ShortGuid[]>.Default.GetHashCode(path);
        }

        private void EnsureFinalIsEmpty()
        {
            if (path.Length == 0 || path[path.Length - 1] != ShortGuid.Invalid)
            {
                Array.Resize(ref path, path.Length + 1);
                path[path.Length - 1] = ShortGuid.Invalid;
            }
        }

        public override string ToString()
        {
            string val = "";
            for (int i = 0; i < path.Length; i++)
            {
                val += path[i].ToByteString();
                if (i != path.Length - 1) val += " -> ";
            }
            return val;
        }

        [Obsolete("Please use CommandsUtils GetResolvedAsString after resolving with ResolveAliasOrProxy, or ResolveHierarchy if a direct style from root.")]
        public string ToString(Commands commands, Composite composite, bool withIDs = true)
        {
            return commands.Utils.GetResolvedAsString(commands.Utils.ResolveAliasOrProxy(path, composite), withIDs);
        }

        public UInt32 ToUInt32()
        {
            UInt32 val = 0;
            for (int i = 0; i < path.Length; i++) val += path[i].AsUInt32;
            return val;
        }

                 /// <summary>
         /// Get the ID of the entity that this path points to
         /// </summary>
         public ShortGuid GetPointedEntityID()
        {
            ShortGuid id = ShortGuid.Invalid;
            for (int i = path.Length - 1; i >= 0; i--)
            {
                if (path[i] == ShortGuid.Invalid) continue;
                id = path[i];
                break;
            }
            return id;
        }

                 /// <summary>
         /// Generate the checksum used identify the path
         /// </summary>
         public ShortGuid GeneratePathHash()
        {
            if (path.Length == 0) return ShortGuid.Invalid;
            EnsureFinalIsEmpty();

            ShortGuid checksumGenerated = path[path.Length - 2];
            for (int i = path.Length - 2; i >= 1; i--)
            {
                checksumGenerated = checksumGenerated.Combine(path[i - 1]);
            }
            return checksumGenerated;
        }

                 /// <summary>
         /// Generate the instance ID used to identify the instanced composite we're executed in
         /// </summary>
         public ShortGuid GenerateCompositeInstanceID(bool hasInternalEntityID = true) //Set this to false the final value in the path is not an entity ID within the composite
        {
            return path.GenerateCompositeInstanceID(hasInternalEntityID);
        }

                 /// <summary>
         /// Generate a zone ID (use this when the EntityHandle points to a Zone entity)
         /// </summary>
         public ShortGuid GenerateZoneID()
        {
            return new ShortGuid(0 + GenerateCompositeInstanceID().AsUInt32 + GetPointedEntityID().AsUInt32 + 1);
        }

                 /// <summary>
         /// Add the next entity GUID along the path
         /// </summary>
         public void AddNextStep(Entity entity)
        {
            AddNextStep(entity.shortGUID);
        }
        public void AddNextStep(ShortGuid guid)
        {
            if (path.Length > 0 && path[path.Length - 1] == ShortGuid.Invalid)
            {
                path[path.Length - 1] = guid;
            }
            else
            {
                Array.Resize(ref path, path.Length + 1);
                path[path.Length - 1] = guid;
            }
            EnsureFinalIsEmpty();
        }

                 /// <summary>
         /// Remove the last entity GUID along the path
         /// </summary>
        public void GoBackOneStep()
        {
            if (path.Length > 0 && path[path.Length - 1] == ShortGuid.Invalid)
            {
                if (path.Length > 1)
                {
                    path[path.Length - 2] = ShortGuid.Invalid;
                    Array.Resize(ref path, path.Length - 1);
                }
            }
            else if (path.Length > 0)
            {
                path[path.Length - 1] = ShortGuid.Invalid;
            }
            else
            {
                EnsureFinalIsEmpty();
            }
        }

        /// <summary>
        /// Create a copy of this EntityPath
        /// </summary>
        public EntityPath Copy()
        {
            ShortGuid[] newPath = new ShortGuid[path.Length];
            Array.Copy(path, newPath, path.Length);
            return new EntityPath(newPath);
        }
        
                 /// <summary>
         /// Updates this path to have the path to another entity prepended to it
         /// </summary>
        //public void PrependPath(EntityPath otherPath)
        //{
        //    int length = otherPath.path[otherPath.path.Count - 1] == ShortGuid.Invalid ? otherPath.path.Count - 2 : otherPath.path.Count - 1;
        //    for (int i = 0; i < length; i++)
        //        path.Insert(i, otherPath.path[i]);
        //}
    }

    public static class PathUtils
    {
                 /// <summary>
         /// Generate the instance ID used to identify the instanced composite we're executed in
         /// </summary>
         public static ShortGuid GenerateCompositeInstanceID(this ShortGuid[] path, bool hasInternalEntityID = true) //Set this to false the final value in the path is not an entity ID within the composite
        {
            bool hasTrailingInvalid = (path.Length > 0 && path[path.Length - 1] == ShortGuid.Invalid);
            ShortGuid[] values = new ShortGuid[hasInternalEntityID ? (hasTrailingInvalid ? path.Length - 1 : path.Length) : (hasTrailingInvalid ? path.Length : path.Length + 1)];
            values[values.Length - 1] = ShortGuid.InstanceGuid;
            int x = 0;
            for (int i = values.Length - 2; i >= 0; i--)
            {
                values[i] = path[x];
                x++;
            }
            ShortGuid instanceGenerated = values[0];
            for (int i = 0; i < values.Length; i++)
            {
                if (i == values.Length - 1) break;
                instanceGenerated = values[i + 1].Combine(instanceGenerated);
            }
            return instanceGenerated;
        }
                 /// <summary>
         /// Generate the instance ID used to identify the instanced composite we're executed in
         /// </summary>
         public static ShortGuid GenerateCompositeInstanceID(this List<ShortGuid> path, bool hasInternalEntityID = true) //Set this to false the final value in the path is not an entity ID within the composite
        {
            bool hasTrailingInvalid = (path.Count > 0 && path[path.Count - 1] == ShortGuid.Invalid);
            ShortGuid[] values = new ShortGuid[hasInternalEntityID ? (hasTrailingInvalid ? path.Count - 1 : path.Count) : (hasTrailingInvalid ? path.Count : path.Count + 1)];
            values[values.Length - 1] = ShortGuid.InstanceGuid;
            int x = 0;
            for (int i = values.Length - 2; i >= 0; i--)
            {
                values[i] = path[x];
                x++;
            }
            ShortGuid instanceGenerated = values[0];
            for (int i = 0; i < values.Length; i++)
            {
                if (i == values.Length - 1) break;
                instanceGenerated = values[i + 1].Combine(instanceGenerated);
            }
            return instanceGenerated;
        }
    }
}
