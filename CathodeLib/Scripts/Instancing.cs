using CATHODE;
using CATHODE.Enums;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CATHODE.ShaderTypes;
using CathodeLib.Alphalight;
using CathodeLib.NavMesh;
using CathodeLib.ObjectExtensions;
using CathodeLib.Radiosity;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using static CATHODE.Lights;
using static CATHODE.MaterialMappings.MaterialMapping;
using static CATHODE.MorphTargets.MorphTarget;
using static CATHODE.Movers.MOVER_DESCRIPTOR;
using static CATHODE.Movers.MOVER_DESCRIPTOR.GPU_CONSTANTS;
using static CATHODE.Movers.MOVER_DESCRIPTOR.RENDER_CONSTANTS;
using static CATHODE.Resources;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib
{
    public class InstancedEntity : IComparable<InstancedEntity>
    {
        public class Parameters<T> : IComparable<Parameters<T>>
        {
            //Values set on the entity itself at initialisation time
            public Dictionary<ShortGuid, T> Values;

            //Any links to other entities that set parameter values
            public Dictionary<ShortGuid, List<Tuple<ShortGuid, InstancedEntity>>> Links;

            //The entity these belong to, so a resolution can be seeded on whoever asked for it.
            public InstancedEntity Owner;

            public Parameters(int capacity = 0)
            {
                Values = new Dictionary<ShortGuid, T>(capacity);
                Links = new Dictionary<ShortGuid, List<Tuple<ShortGuid, InstancedEntity>>>(capacity);
            }

            public bool Has(ShortGuid guid)
            {
                return Values.ContainsKey(guid);
            }

            public T Get(ShortGuid guid)
            {
                //Check links first, these override the values.
                //When several links drive one parameter the LAST one wins, in the order the
                //connections are authored - links push their value in, so the final write is what
                //the parameter ends up holding. Scored against the light flags retail ships (which
                //equal their movers' own DEFERRED_PARAMS exactly, so they are a reliable witness):
                //last-link is exact on all 14,641 light movers of ChallengeMap4, SCI_HospitalUpper,
                //BSP_Torrens, Solace, Tech_Hub and HAB_Airport, where taking the first link instead
                //misses 76 of them (5/0/40/4/23/4). The cases that separate the two are lights
                //whose is_specular is fed by both a composite variable and a PlatformConstantBool,
                //and it is always the constant - the later connection - retail agrees with.
                if (Links.TryGetValue(guid, out List<Tuple<ShortGuid, InstancedEntity>> links))
                    if (links.Count != 0)
                    {
                        //Note who asked, so a randomiser several entities down the chain can be
                        //seeded on them - see _resolutionRoot.
                        bool outermost = _resolutionRoot == null;
                        if (outermost) _resolutionRoot = Owner;
                        try { return links[links.Count - 1].Item2.GetAs<T>(links[links.Count - 1].Item1); }
                        finally { if (outermost) _resolutionRoot = null; }
                    }

                //Fall back to our own value
                if (Values.TryGetValue(guid, out T val))
                    return val;

                if (guid == ShortGuids.mapping)
                {
                    //This is a bodge for 'mapping' parameters, their type is actual FileType, but we handle them as cResource
                    //As such, their default value won't be populated, so return one here if it hasn't been set directly or via link
#if DEBUG
                    if (typeof(T) != typeof(cResource))
                        throw new Exception("Unexpected!");
#endif
                    return (T)(object)new cResource();
                }

                //Fall back to type defaults
                if (typeof(T) == typeof(cResource))
                    return (T)(object)new cResource();
                return default(T);
            }

            public List<InstancedEntity> GetLinks(ShortGuid guid)
            {
                List<InstancedEntity> entities = new List<InstancedEntity>();
                if (Links.TryGetValue(guid, out List<Tuple<ShortGuid, InstancedEntity>> ents))
                {
                    for (int i = 0; i < ents.Count; i++)
                    {
                        entities.Add(ents[i].Item2);
                    }
                }
                return entities;
            }

            public void AddLinks(ShortGuid guid, List<Tuple<ShortGuid, InstancedEntity>> links)
            {
                if (Links.ContainsKey(guid))
                    Links[guid].AddRange(links);
                else
                    Links.Add(guid, links);
            }

            //For VariableEntities -> we want to override the default values and add links for matching variable names on the entity that instanced the composite they're contained in
            public void PopulateVariableParentInfo(Parameters<T> compInstParams, ShortGuid varGuid)
            {
                if (compInstParams.Values.TryGetValue(varGuid, out T value))
                {
                    Values[varGuid] = value;
                }
                if (compInstParams.Links.TryGetValue(varGuid, out List<Tuple<ShortGuid, InstancedEntity>> parentLinks))
                {
                    if (!Links.TryGetValue(varGuid, out List<Tuple<ShortGuid, InstancedEntity>> existingLinks))
                    {
                        existingLinks = new List<Tuple<ShortGuid, InstancedEntity>>(parentLinks.Count);
                        Links[varGuid] = existingLinks;
                    }
                    existingLinks.AddRange(parentLinks); 
                }
            }

            //Any entity can have an Alias override the values on it, kinda similar to the above 
            public void PopulateAliasInfo(Parameters<T> aliasParams)
            {
                foreach (KeyValuePair<ShortGuid, T> value in aliasParams.Values)
                {
                    Values[value.Key] = value.Value;
                }
                foreach (KeyValuePair<ShortGuid, List<Tuple<ShortGuid, InstancedEntity>>> value in aliasParams.Links)
                {
                    AddLinks(value.Key, value.Value);
                }
            }

            #region Equality Checks
            public override bool Equals(object obj)
            {
                if (obj is Parameters<T> other)
                {
                    if (Values.Count != other.Values.Count) return false;
                    foreach (var kvp in Values)
                    {
                        if (!other.Values.TryGetValue(kvp.Key, out T otherValue) || !Equals(kvp.Value, otherValue))
                            return false;
                    }

                    if (Links.Count != other.Links.Count) return false;
                    foreach (var kvp in Links)
                    {
                        if (!other.Links.TryGetValue(kvp.Key, out List<Tuple<ShortGuid, InstancedEntity>> otherLinks))
                            return false;
                        if (kvp.Value.Count != otherLinks.Count) return false;
                        for (int i = 0; i < kvp.Value.Count; i++)
                        {
                            if (kvp.Value[i].Item1 != otherLinks[i].Item1 ||
                                kvp.Value[i].Item2 != otherLinks[i].Item2)
                                return false;
                        }
                    }

                    return true;
                }
                return false;
            }

            public override int GetHashCode()
            {
                int hashCode = -1757656154;
                foreach (var kvp in Values)
                {
                    hashCode = hashCode * -1521134295 + kvp.Key.GetHashCode();
                    hashCode = hashCode * -1521134295 + (kvp.Value?.GetHashCode() ?? 0);
                }
                foreach (var kvp in Links)
                {
                    hashCode = hashCode * -1521134295 + kvp.Key.GetHashCode();
                    hashCode = hashCode * -1521134295 + kvp.Value.Count.GetHashCode();
                    foreach (var link in kvp.Value)
                    {
                        hashCode = hashCode * -1521134295 + link.Item1.GetHashCode();
                        hashCode = hashCode * -1521134295 + (link.Item2?.GetHashCode() ?? 0);
                    }
                }
                return hashCode;
            }

            public static bool operator ==(Parameters<T> x, Parameters<T> y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                return x.Equals(y);
            }

            public static bool operator !=(Parameters<T> x, Parameters<T> y)
            {
                return !(x == y);
            }

            public int CompareTo(Parameters<T> other)
            {
                if (other == null) return 1;
                if (ReferenceEquals(this, other)) return 0;

                int valuesCompare = CompareDictionaries(Values, other.Values);
                if (valuesCompare != 0) return valuesCompare;
                return CompareLinksDictionaries(Links, other.Links);
            }

            private int CompareDictionaries(Dictionary<ShortGuid, T> dict1, Dictionary<ShortGuid, T> dict2)
            {
                int countCompare = dict1.Count.CompareTo(dict2.Count);
                if (countCompare != 0) return countCompare;

                var keys1 = new List<ShortGuid>(dict1.Keys);
                var keys2 = new List<ShortGuid>(dict2.Keys);
                keys1.Sort();
                keys2.Sort();
                for (int i = 0; i < keys1.Count; i++)
                {
                    int keyCompare = keys1[i].CompareTo(keys2[i]);
                    if (keyCompare != 0) return keyCompare;

                    T val1 = dict1[keys1[i]];
                    T val2 = dict2[keys2[i]];
                    int valCompare = CompareValues(val1, val2);
                    if (valCompare != 0) return valCompare;
                }
                return 0;
            }

            private int CompareLinksDictionaries(Dictionary<ShortGuid, List<Tuple<ShortGuid, InstancedEntity>>> dict1, Dictionary<ShortGuid, List<Tuple<ShortGuid, InstancedEntity>>> dict2)
            {
                int countCompare = dict1.Count.CompareTo(dict2.Count);
                if (countCompare != 0) return countCompare;

                var keys1 = new List<ShortGuid>(dict1.Keys);
                var keys2 = new List<ShortGuid>(dict2.Keys);
                keys1.Sort();
                keys2.Sort();

                for (int i = 0; i < keys1.Count; i++)
                {
                    int keyCompare = keys1[i].CompareTo(keys2[i]);
                    if (keyCompare != 0) return keyCompare;

                    var list1 = dict1[keys1[i]];
                    var list2 = dict2[keys2[i]];
                    int listCountCompare = list1.Count.CompareTo(list2.Count);
                    if (listCountCompare != 0) return listCountCompare;

                    for (int j = 0; j < list1.Count; j++)
                    {
                        int item1Compare = list1[j].Item1.CompareTo(list2[j].Item1);
                        if (item1Compare != 0) return item1Compare;

                        int item2Compare = list1[j].Item2?.CompareTo(list2[j].Item2) ?? (list2[j].Item2 == null ? 0 : -1);
                        if (item2Compare != 0) return item2Compare;
                    }
                }

                return 0;
            }

            private int CompareValues(T val1, T val2)
            {
                if (val1 == null && val2 == null) return 0;
                if (val1 == null) return -1;
                if (val2 == null) return 1;

                if (val1 is IComparable<T> comparable1)
                {
                    return comparable1.CompareTo(val2);
                }
                if (val1 is IComparable comparable2)
                {
                    return comparable2.CompareTo(val2);
                }
                if (Equals(val1, val2)) return 0;
                return val1.GetHashCode().CompareTo(val2.GetHashCode());
            }
            #endregion
        }

        public class Transform : IComparable<Transform>
        {
            public Vector3 Position = new Vector3();
            public Vector3 Rotation = new Vector3();

            public Matrix4x4 AsMatrix()
            {
                Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
                    Rotation.Y * (float)Math.PI / 180.0f,
                    Rotation.X * (float)Math.PI / 180.0f,
                    Rotation.Z * (float)Math.PI / 180.0f
                );

                return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(Position);
            }

            public override string ToString()
            {
                return Position.ToString() + ", " + Rotation.ToString();
            }

            public static Transform operator *(Transform lhs, Transform rhs)
            {
                Matrix4x4 lhsMatrix = lhs.AsMatrix();
                Matrix4x4 rhsMatrix = rhs.AsMatrix();

                Matrix4x4 resultMatrix = lhsMatrix * rhsMatrix;
                Matrix4x4.Decompose(resultMatrix, out Vector3 scale, out Quaternion rotation, out Vector3 position);

                (decimal yaw, decimal pitch, decimal roll) = rotation.ToYawPitchRoll();

                return new Transform()
                {
                    Position = position,
                    Rotation = new Vector3((float)pitch, (float)yaw, (float)roll)
                };
            }

            #region Equality Checks
            public override bool Equals(object obj)
            {
                if (obj is Transform other)
                {
                    return this.Position.X == other.Position.X &&
                           this.Position.Y == other.Position.Y &&
                           this.Position.Z == other.Position.Z &&
                           this.Rotation.X == other.Rotation.X &&
                           this.Rotation.Y == other.Rotation.Y &&
                           this.Rotation.Z == other.Rotation.Z;
                }
                return false;
            }

            public override int GetHashCode()
            {
                int hashCode = -1757656154;
                hashCode = hashCode * -1521134295 + Position.X.GetHashCode();
                hashCode = hashCode * -1521134295 + Position.Y.GetHashCode();
                hashCode = hashCode * -1521134295 + Position.Z.GetHashCode();
                hashCode = hashCode * -1521134295 + Rotation.X.GetHashCode();
                hashCode = hashCode * -1521134295 + Rotation.Y.GetHashCode();
                hashCode = hashCode * -1521134295 + Rotation.Z.GetHashCode();
                return hashCode;
            }

            public static bool operator ==(Transform x, Transform y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return false;
                return x.Position.X == y.Position.X &&
                       x.Position.Y == y.Position.Y &&
                       x.Position.Z == y.Position.Z &&
                       x.Rotation.X == y.Rotation.X &&
                       x.Rotation.Y == y.Rotation.Y &&
                       x.Rotation.Z == y.Rotation.Z;
            }

            public static bool operator !=(Transform x, Transform y)
            {
                return !(x == y);
            }

            public int CompareTo(Transform other)
            {
                if (other == null) return 1;
                if (ReferenceEquals(this, other)) return 0;

                int posCompare = CompareVector3(Position, other.Position);
                if (posCompare != 0) return posCompare;
                return CompareVector3(Rotation, other.Rotation);
            }

            private int CompareVector3(Vector3 a, Vector3 b)
            {
                int xCompare = a.X.CompareTo(b.X);
                if (xCompare != 0) return xCompare;

                int yCompare = a.Y.CompareTo(b.Y);
                if (yCompare != 0) return yCompare;

                return a.Z.CompareTo(b.Z);
            }
            #endregion
        }

        public Parameters<bool> Bools = new Parameters<bool>();
        public Parameters<int> Integers = new Parameters<int>();
        public Parameters<float> Floats = new Parameters<float>();
        public Parameters<int> EnumIndexes = new Parameters<int>();
        public Parameters<Vector3> Vectors = new Parameters<Vector3>();
        public Parameters<Transform> Transforms = new Parameters<Transform>();
        public Parameters<cResource> Resources = new Parameters<cResource>();
        public Parameters<string> Strings = new Parameters<string>();

        public Level Level = null;
        public Entity Entity = null;
        public EntityPath Path = null;
        public Composite Composite = null;

        public EntityHandle Handle
        {
            get
            {
                //todo should store this really
                return new EntityHandle()
                {
                    composite_instance_id = ThisCompositeInstance.InstanceID,
                    entity_id = Entity.shortGUID
                };
            }
        }

        //This is information we generate at compile time - the Mover should contain everything, but we might be 
        //creating instanced info BEFORE the Mover is created, so there are separate members here to store that.
        public Movers.MOVER_DESCRIPTOR Mover = null;
        public Textures.TEX4 EnvironmentMap = null;
        public ShortGuid PrimaryZone = ShortGuid.Invalid;
        public ShortGuid SecondaryZone = ShortGuid.Invalid;
        public ShortGuid PrimaryZoneSourceInstance = ShortGuid.Invalid;
        public bool PrimaryZoneWasDirect = false;
        public ShortGuid PrimaryZoneDescentRoot = ShortGuid.Invalid;
        public int LightingMaster = 0; //seems to be deprecated - remove?

        //The composite and entity one step back in the path, responsible for creating this instance: will be null if at root
        public InstancedEntity ParentCompositeInstanceEntity = null;
        public InstancedComposite ParentCompositeInstance = null;

        //The current composite instance
        public InstancedComposite ThisCompositeInstance = null;

        //The composite instanced by this entity, one step forward in the path: will be null if this doesn't instance one
        public InstancedComposite ChildCompositeInstance = null;

        private HashSet<(ShortGuid, ParameterVariant, DataType)> _parameters = new HashSet<(ShortGuid, ParameterVariant, DataType)>();

        public InstancedEntity(Level level, Composite composite, Entity entity, EntityPath path, ConcurrentDictionary<(Entity, Composite), List<(ShortGuid, ParameterVariant, DataType)>> parameterCache, ConcurrentDictionary<(Composite, ShortGuid), Entity> entityLookupCache)
        {
            Level = level;
            Entity = entity;
            Path = path;
            Composite = composite;
            Bools.Owner = this; Integers.Owner = this; Floats.Owner = this; EnumIndexes.Owner = this;
            Vectors.Owner = this; Transforms.Owner = this; Resources.Owner = this; Strings.Owner = this;

            //Get all parameters that supply values - use cache if available
            List<(ShortGuid, ParameterVariant, DataType)> parameters;
            if (parameterCache != null)
            {
                var cacheKey = (entity, composite);
                parameters = parameterCache.GetOrAdd(cacheKey, key => Level.Commands.Utils.GetAllParameters(key.Item1, key.Item2));
                parameters = new List<(ShortGuid, ParameterVariant, DataType)>(parameters);
            }
            else
            {
                parameters = Level.Commands.Utils.GetAllParameters(entity, composite);
            }
            
            if (parameters == null)
                parameters = new List<(ShortGuid, ParameterVariant, DataType)>();
            
            parameters.RemoveAll(o =>
                o.Item2 == ParameterVariant.REFERENCE_PIN ||
                o.Item2 == ParameterVariant.TARGET_PIN ||
                o.Item2 == ParameterVariant.METHOD_FUNCTION ||
                o.Item2 == ParameterVariant.METHOD_PIN
            );
            Dictionary<ShortGuid, (ShortGuid, ParameterVariant, DataType)> paramLookup = new Dictionary<ShortGuid, (ShortGuid, ParameterVariant, DataType)>(parameters.Count);
            foreach (var param in parameters)
            {
                if (!paramLookup.ContainsKey(param.Item1))
                    paramLookup[param.Item1] = param;
            }

            switch (entity.variant)
            {
                //For aliases, only factor in the parameters and links that are actually set, since these are OVERRIDES
                case EntityVariant.ALIAS:
                    foreach (Parameter p in entity.parameters)
                    {
                        if (p.content == null)
                            continue;
                        if (paramLookup.TryGetValue(p.name, out var param))
                            _parameters.Add(param);
                    }
                    //TODO: also need to factor in parent links somehow (?) -> actually, i think we can disregard logic links?
                    foreach (EntityConnector c in entity.childLinks)
                    {
                        if (paramLookup.TryGetValue(c.thisParamID, out var param))
                            _parameters.Add(param);
                    }
                    break;
                //For others, get all default values, as well as ones that are set
                default:
                    //NOTE: GetAllParameters does not check for duplicates, so do that now - need to fix that.
                    // An example of another issue is {UI_ReactionGame} - the child UI_Attached should not add another 'success' entry - parent should override it
                    foreach (var entry in parameters)
                        _parameters.Add(entry);
                    break;
            }

            //Get the values off the entity, or create the default value if its not set
            foreach ((ShortGuid guid, ParameterVariant variant, DataType datatype) in _parameters)
            {
                switch (datatype)
                {
                    //should really make this a utility
                    case DataType.BOOL:
                        {
                            bool value = false;
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.INTEGER:
                                    value = ((cInteger)p.content).value == 1;
                                    break;
                                case DataType.FLOAT:
                                    value = ((cFloat)p.content).value == 1.0f;
                                    break;
                                case DataType.BOOL:
                                    value = ((cBool)p.content).value;
                                    break;
                                case DataType.FILEPATH:
                                case DataType.STRING:
                                case DataType.ENUM_STRING:
                                    value = ((cString)p.content).value.ToUpper() == "TRUE";
                                    break;
                                default:
                                    value = ((cBool)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid)).value;
                                    break;
                            }
                            Bools.Values.Add(guid, value);
                        }
                        break;
                    case DataType.INTEGER:
                        {
                            int value = 0;
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.ENUM:
                                    value = ((cEnum)p.content).enumIndex;
                                    break;
                                case DataType.INTEGER:
                                    value = ((cInteger)p.content).value;
                                    break;
                                case DataType.FLOAT:
                                    value = (int)((cFloat)p.content).value;
                                    break;
                                case DataType.BOOL:
                                    value = ((cBool)p.content).value ? 1 : 0;
                                    break;
                                case DataType.FILEPATH:
                                case DataType.STRING:
                                case DataType.ENUM_STRING:
                                    try
                                    {
                                        value = Convert.ToInt32(((cString)p.content).value);
                                    }
                                    catch { }
                                    break;
                                default:
                                    value = ((cInteger)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid)).value;
                                    break;
                            }
                            Integers.Values.Add(guid, value);
                        }
                        break;
                    case DataType.FLOAT:
                        {
                            float value = 0.0f;
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.ENUM:
                                    value = ((cEnum)p.content).enumIndex;
                                    break;
                                case DataType.INTEGER:
                                    value = ((cInteger)p.content).value;
                                    break;
                                case DataType.FLOAT:
                                    value = ((cFloat)p.content).value;
                                    break;
                                case DataType.BOOL:
                                    value = ((cBool)p.content).value ? 1 : 0;
                                    break;
                                case DataType.FILEPATH:
                                case DataType.STRING:
                                case DataType.ENUM_STRING:
                                    try
                                    {
                                        //note - we hit this a lot as seemingly reference is often a string but flagged in our logic a float
                                        value = Convert.ToSingle(((cString)p.content).value);
                                    }
                                    catch { }
                                    break;
                                default:
                                    value = ((cFloat)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid)).value;
                                    break;
                            }
                            if (!Floats.Values.ContainsKey(guid)) //todo - deprecate this when the hashset above is fixed
                                Floats.Values.Add(guid, value);
                        }
                        break;
                    case DataType.ENUM:
                        {
                            int value = 0;
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.ENUM:
                                    value = ((cEnum)p.content).enumIndex;
                                    break;
                                case DataType.INTEGER:
                                    value = ((cInteger)p.content).value;
                                    break;
                                case DataType.FLOAT:
                                    value = (int)((cFloat)p.content).value;
                                    break;
                                case DataType.BOOL:
                                    value = ((cBool)p.content).value ? 1 : 0;
                                    break;
                                case DataType.FILEPATH:
                                case DataType.STRING:
                                case DataType.ENUM_STRING:
                                    try
                                    {
                                        value = Convert.ToInt32(((cString)p.content).value); //todo - if this is ever string, it's probably actually the enum as a string. need to check if that's even supported.
                                    }
                                    catch { }
                                    break;
                                default:
                                    value = ((cEnum)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid)).enumIndex;
                                    break;
                            }
                            EnumIndexes.Values.Add(guid, value);
                        }
                        break;
                    case DataType.VECTOR:
                        {
                            Vector3 value = new Vector3();
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.VECTOR:
                                    value = ((cVector3)p.content).value;
                                    break;
                                case DataType.TRANSFORM:
                                    value = ((cTransform)p.content).position;
                                    break;
                                default:
                                    value = ((cVector3)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid)).value;
                                    break;
                            }
                            Vectors.Values.Add(guid, value);
                        }
                        break;
                    case DataType.TRANSFORM:
                        {
                            Transform value = new Transform();
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.VECTOR:
                                    value = new Transform() { Position = ((cVector3)p.content).value };
                                    break;
                                case DataType.TRANSFORM:
                                    cTransform t = (cTransform)p.content;
                                    value = new Transform() { Position = t.position, Rotation = t.rotation };
                                    break;
                                default:
                                    cTransform tD = (cTransform)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid);
                                    value = new Transform() { Position = tD.position, Rotation = tD.rotation };
                                    break;
                            }

                            Transforms.Values.Add(guid, value);
                        }
                        break;
                    case DataType.RESOURCE:
                        {
                            cResource value = new cResource();
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.RESOURCE:
                                    value = (cResource)p.content;
                                    break;
                            }
                            Resources.Values.Add(guid, value);
                        }
                        break;
                    case DataType.FILEPATH:
                        {
                            Parameter p = entity.GetParameter(guid);
                            //I have bodged material remappings in as a guid that points to the remapping, rather than storing it as a filepath.
                            //However since it's actually down as a filepath internally, I need to add a special case to convert it back here (and down below).
                            if (guid == ShortGuids.mapping && p?.content?.dataType == DataType.RESOURCE)
                            {
                                cResource value = new cResource();
                                switch (p?.content?.dataType)
                                {
                                    case DataType.RESOURCE:
                                        value = (cResource)p.content;
                                        break;
                                }
                                Resources.Values.Add(guid, value);
                            }
                            else
                            {
                                string value = "";
                                switch (p?.content?.dataType)
                                {
                                    case DataType.FILEPATH:
                                    case DataType.STRING:
                                    case DataType.ENUM_STRING:
                                        value = ((cString)p.content).value;
                                        break;
                                    default:
                                        cString sD = (cString)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid);
                                        value = sD?.value ?? "";
                                        break;
                                }
                                Strings.Values.Add(guid, value);
                            }
                        }
                        break;
                    case DataType.ENUM_STRING:
                    case DataType.STRING:
                        {
                            string value = "";
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.FILEPATH:
                                case DataType.STRING:
                                case DataType.ENUM_STRING:
                                    value = ((cString)p.content).value;
                                    break;
                                case DataType.ENUM:
                                    value = Level.Commands.Utils.GetEnum(((cEnum)p.content).enumID).Entries.FirstOrDefault(o => o.Index == ((cEnum)p.content).enumIndex).ToString(); //todo is this right?
                                    break;
                                case DataType.INTEGER:
                                    value = ((cInteger)p.content).value.ToString();
                                    break;
                                case DataType.FLOAT:
                                    value = ((cFloat)p.content).value.ToString();
                                    break;
                                case DataType.BOOL:
                                    value = ((cBool)p.content).value ? "TRUE" : "FALSE";
                                    break;
                                default:
                                    cString sD = (cString)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid);
                                    value = sD.value;
                                    break;
                            }

                            Strings.Values.Add(guid, value);
                        }
                        break;
                }
            }

            //TODO: need to handle triggersequences a bit different i think? they can apply parameter data down
        }

        public void PopulateLinks(List<InstancedEntity> entities)
        {
            PopulateLinks(entities, null);
        }

        public void PopulateLinks(List<InstancedEntity> entities, Dictionary<ShortGuid, InstancedEntity> entityByGuid)
        {
            if (entityByGuid == null)
            {
                entityByGuid = new Dictionary<ShortGuid, InstancedEntity>(entities.Count);
                foreach (var ent in entities)
                {
                    entityByGuid[ent.Entity.shortGUID] = ent;
                }
            }

            if (_parameters != null)
            {
                foreach ((ShortGuid guid, ParameterVariant variant, DataType datatype) in _parameters)
                {
                    List<EntityConnector> links = Entity.childLinks.FindAll(o => o.thisParamID == guid);
                    if (links.Count == 0)
                        continue;

                    List<Tuple<ShortGuid, InstancedEntity>> linksParsed = new List<Tuple<ShortGuid, InstancedEntity>>(links.Count);
                    for (int i = 0; i < links.Count; i++)
                    {
                        Entity connectedEnt = Composite.GetEntityByID(links[i].linkedEntityID);
                        if (connectedEnt == null) continue;
                        if (entityByGuid.TryGetValue(connectedEnt.shortGUID, out InstancedEntity instancedEntity))
                        {
                            linksParsed.Add(new Tuple<ShortGuid, InstancedEntity>(links[i].linkedParamID, instancedEntity));
                        }
                    }

                    if (linksParsed.Count == 0)
                        continue;

                    switch (datatype)
                    {
                        case DataType.BOOL:
                            Bools.AddLinks(guid, linksParsed);
                            break;
                        case DataType.INTEGER:
                            Integers.AddLinks(guid, linksParsed);
                            break;
                        case DataType.FLOAT:
                            Floats.AddLinks(guid, linksParsed);
                            break;
                        case DataType.ENUM:
                            EnumIndexes.AddLinks(guid, linksParsed);
                            break;
                        case DataType.VECTOR:
                            Vectors.AddLinks(guid, linksParsed);
                            break;
                        case DataType.TRANSFORM:
                            Transforms.AddLinks(guid, linksParsed);
                            break;
                        case DataType.RESOURCE:
                            Resources.AddLinks(guid, linksParsed);
                            break;
                        case DataType.FILEPATH:
                            if (guid == ShortGuids.mapping) //pt2 of the material mapping change
                                Resources.AddLinks(guid, linksParsed);
                            else
                                Strings.AddLinks(guid, linksParsed);
                            break;
                        case DataType.ENUM_STRING:
                        case DataType.STRING:
                            Strings.AddLinks(guid, linksParsed);
                            break;
                    }
                }
                _parameters = null;
            }

            //If this entity is a Composite interface type, we need to look for the parent entity that instanced our composite and forward the links on.
            if (Entity.variant == EntityVariant.VARIABLE)
            {
                if (ParentCompositeInstanceEntity != null)
                {
                    VariableEntity var = (VariableEntity)Entity;
                    ShortGuid varGuid = var.name;

                    Bools.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Bools, varGuid);
                    Integers.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Integers, varGuid);
                    Floats.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Floats, varGuid);
                    EnumIndexes.PopulateVariableParentInfo(ParentCompositeInstanceEntity.EnumIndexes, varGuid);
                    Vectors.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Vectors, varGuid);
                    Transforms.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Transforms, varGuid);
                    Resources.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Resources, varGuid);
                    Strings.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Strings, varGuid);
                }
            }
        }

        public void ApplyAlias(InstancedAlias alias)
        {
            Bools.PopulateAliasInfo(alias.InstancedInfo.Bools);
            Integers.PopulateAliasInfo(alias.InstancedInfo.Integers);
            Floats.PopulateAliasInfo(alias.InstancedInfo.Floats);
            EnumIndexes.PopulateAliasInfo(alias.InstancedInfo.EnumIndexes);
            Vectors.PopulateAliasInfo(alias.InstancedInfo.Vectors);
            Transforms.PopulateAliasInfo(alias.InstancedInfo.Transforms);
            Resources.PopulateAliasInfo(alias.InstancedInfo.Resources);
            Strings.PopulateAliasInfo(alias.InstancedInfo.Strings);
        }

        public T GetAs<T>(string name = "reference")
        {
            ShortGuid guid = name == "reference" ? ShortGuids.reference : ShortGuidUtils.Generate(name);
            return GetAs<T>(guid);
        }

        public T GetAs<T>(ShortGuid guid)
        {
            switch (Entity.variant)
            {
                case EntityVariant.FUNCTION:
                    {
                        FunctionEntity func = (FunctionEntity)Entity;
                        if (func.function.IsFunctionType)
                        {
                            return GetFunctionData<T>(guid, func.function.AsFunctionType);
                        }
                        else
                        {
                            if (guid != ShortGuids.reference && ChildCompositeInstance != null)
                            {
                                InstancedEntity pinEntity = FindChildVariablePin(guid);
                                if (pinEntity != null)
                                    return pinEntity.GetAs<T>();
                            }
                            return GetFunctionData<T>(guid, FunctionType.CompositeInterface);
                        }
                    }

                case EntityVariant.VARIABLE:
                    {
                        VariableEntity var = (VariableEntity)Entity;
                        switch (var.type)
                        {
                            case DataType.BOOL:
                                bool b = Bools.Get(var.name);
                                if (typeof(T) == typeof(int))
                                    return (T)(object)(b ? 1 : 0);
                                if (typeof(T) == typeof(float))
                                    return (T)(object)(float)(b ? 1.0f : 0.0f);
                                if (typeof(T) == typeof(bool))
                                    return (T)(object)b;
                                if (typeof(T) == typeof(string))
                                    return (T)(object)(string)(b ? "TRUE" : "FALSE");
                                break;
                            case DataType.INTEGER:
                                int i = Integers.Get(var.name);
                                if (typeof(T) == typeof(int))
                                    return (T)(object)i;
                                if (typeof(T) == typeof(float))
                                    return (T)(object)(float)i;
                                if (typeof(T) == typeof(bool))
                                    return (T)(object)(i == 1);
                                if (typeof(T) == typeof(string))
                                    return (T)(object)i.ToString();
                                break;
                            case DataType.FLOAT:
                                float f = Floats.Get(var.name);
                                if (typeof(T) == typeof(int))
                                    return (T)(object)(int)f;
                                if (typeof(T) == typeof(float))
                                    return (T)(object)f;
                                if (typeof(T) == typeof(bool))
                                    return (T)(object)(f == 1.0f);
                                if (typeof(T) == typeof(string))
                                    return (T)(object)f.ToString();
                                break;
                            case DataType.ENUM:
                                int e = EnumIndexes.Get(var.name);
                                if (typeof(T) == typeof(int))
                                    return (T)(object)e;
                                if (typeof(T) == typeof(float))
                                    return (T)(object)(float)e;
                                if (typeof(T) == typeof(bool))
                                    return (T)(object)(e == 1);
                                break;
                            case DataType.VECTOR:
                                Vector3 v = Vectors.Get(var.name);
                                if (typeof(T) == typeof(Vector3))
                                    return (T)(object)v;
                                if (typeof(T) == typeof(Transform))
                                    return (T)(object)new Transform() { Position = v };
                                break;
                            case DataType.TRANSFORM:
                                Transform t = Transforms.Get(var.name);
                                if (typeof(T) == typeof(Vector3))
                                    return (T)(object)t.Position;
                                if (typeof(T) == typeof(Transform))
                                    return (T)(object)t;
                                break;
                            case DataType.RESOURCE:
                                cResource r = Resources.Get(var.name);
                                if (typeof(T) == typeof(cResource))
                                    return (T)(object)r;
                                break;
                            case DataType.STRING:
                                string s = Strings.Get(var.name);
                                if (typeof(T) == typeof(int) && int.TryParse(s, out int sI))
                                    return (T)(object)(int)sI;
                                if (typeof(T) == typeof(float) && float.TryParse(s, out float sF))
                                    return (T)(object)(float)sF;
                                if (typeof(T) == typeof(bool))
                                    return (T)(object)(bool)(s.ToUpper() == "TRUE");
                                if (typeof(T) == typeof(string))
                                    return (T)(object)s;
                                break;
                            case DataType.NONE:
                            case DataType.OBJECT:
                                {
                                    if (TryResolveOwnChildLink<T>(var.name, out T linkedValue))
                                        return linkedValue;
                                }
                                break;
                        }
                    }
                    break;

                case EntityVariant.ALIAS:
                    if (typeof(T) == typeof(bool) && Bools.Has(guid))
                        return (T)(object)Bools.Get(guid);
                    if (typeof(T) == typeof(int) && Integers.Has(guid))
                        return (T)(object)Integers.Get(guid);
                    if (typeof(T) == typeof(float) && Floats.Has(guid))
                        return (T)(object)Floats.Get(guid);
                    if (typeof(T) == typeof(Vector3) && Vectors.Has(guid))
                        return (T)(object)Vectors.Get(guid);
                    if (typeof(T) == typeof(Transform) && Transforms.Has(guid))
                        return (T)(object)Transforms.Get(guid);
                    if (typeof(T) == typeof(cResource) && Resources.Has(guid))
                        return (T)(object)Resources.Get(guid);
                    if (typeof(T) == typeof(string) && Strings.Has(guid))
                        return (T)(object)Strings.Get(guid);
                    try
                    {
                        if (typeof(T) == typeof(bool)) return (T)(object)Bools.Get(guid);
                        if (typeof(T) == typeof(int)) return (T)(object)Integers.Get(guid);
                        if (typeof(T) == typeof(float)) return (T)(object)Floats.Get(guid);
                        if (typeof(T) == typeof(Vector3)) return (T)(object)Vectors.Get(guid);
                        if (typeof(T) == typeof(Transform)) return (T)(object)Transforms.Get(guid);
                        if (typeof(T) == typeof(cResource)) return (T)(object)Resources.Get(guid);
                        if (typeof(T) == typeof(string)) return (T)(object)Strings.Get(guid);
                    }
                    catch { /* fall through to defaults */ }
                    break;

                case EntityVariant.PROXY:
                    //resolve the proxy and forward (?)
                    break;
            }

            //todo - really we shouldn't get here after handling proxies (i think?). should throw.
            if (typeof(T) == typeof(bool))
                return (T)(object)false;
            else if (typeof(T) == typeof(int))
                return (T)(object)0;
            else if (typeof(T) == typeof(float))
                return (T)(object)0.0f;
            else if (typeof(T) == typeof(Vector3))
                return (T)(object)new Vector3(0, 0, 0);
            else if (typeof(T) == typeof(Transform))
            {
                if (Transforms.Has(ShortGuids.position))
                    return (T)(object)Transforms.Get(ShortGuids.position);
                else
                    return (T)(object)new Transform();
            }
            else if (typeof(T) == typeof(cResource))
                return (T)(object)new cResource();
            else
            {
                throw new Exception("Unhandled");
            }
        }

        private T GetFunctionData<T>(ShortGuid guid, FunctionType type)
        {
            if (guid != ShortGuids.reference)
            {
                //Get the value of the parameter, taking in to account anything applied by to the instance
                //Try to get from the most appropriate collection first, then convert if needed
                if (Bools.Has(guid))
                {
                    bool value = Bools.Get(guid);
                    return GetValueAs<T>(value);
                }
                else if (Integers.Has(guid))
                {
                    int value = Integers.Get(guid);
                    return GetValueAs<T>(value);
                }
                else if (EnumIndexes.Has(guid))
                {
                    int value = EnumIndexes.Get(guid);
                    return GetValueAs<T>(value);
                }
                else if (Floats.Has(guid))
                {
                    float value = Floats.Get(guid);
                    return GetValueAs<T>(value);
                }
                else if (Vectors.Has(guid))
                {
                    Vector3 value = Vectors.Get(guid);
                    return GetValueAs<T>(value);
                }
                else if (Transforms.Has(guid))
                {
                    Transform value = Transforms.Get(guid);
                    return GetValueAs<T>(value);
                }
                else if (Resources.Has(guid))
                {
                    cResource value = Resources.Get(guid);
                    return GetValueAs<T>(value);
                }
                else if (Strings.Has(guid))
                {
                    string value = Strings.Get(guid);
                    return GetValueAs<T>(value);
                }
            }
            else
            {
                //Calculate the reference value based on the entity's internal logic
                switch (type)
                {
                    case FunctionType.Character:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.Checkpoint:
                        {
                            string result = "";
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.CoverExclusionArea:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.DeleteBlankPanel:
                        {
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.door_mechanism);
                            bool result = door_mechanism != DOOR_MECHANISM.BLANK;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.DeleteButtonDisk:
                        {
                            BUTTON_TYPE button_type = (BUTTON_TYPE)EnumIndexes.Get(ShortGuids.button_type);
                            bool result = true;
                            if (button_type == BUTTON_TYPE.DISK)
                            {
                                DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.door_mechanism);
                                result = door_mechanism != DOOR_MECHANISM.HIDDEN_BUTTON && door_mechanism != DOOR_MECHANISM.BUTTON;
                            }
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.DeleteButtonKeys:
                        {
                            BUTTON_TYPE button_type = (BUTTON_TYPE)EnumIndexes.Get(ShortGuids.button_type);
                            bool result = true;
                            if (button_type == BUTTON_TYPE.KEYS)
                            {
                                DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.door_mechanism);
                                result = door_mechanism != DOOR_MECHANISM.HIDDEN_BUTTON && door_mechanism != DOOR_MECHANISM.BUTTON;
                            }
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.DeleteCuttingPanel:
                        {
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.door_mechanism);
                            bool result = door_mechanism != DOOR_MECHANISM.HIDDEN_BUTTON &&
                                         door_mechanism != DOOR_MECHANISM.HIDDEN_KEYPAD &&
                                         door_mechanism != DOOR_MECHANISM.HIDDEN_HACKING &&
                                         door_mechanism != DOOR_MECHANISM.HIDDEN_LEVER;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.DeleteHacking:
                        {
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.door_mechanism);
                            bool result = door_mechanism != DOOR_MECHANISM.HACKING && door_mechanism != DOOR_MECHANISM.HIDDEN_HACKING;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.DeleteHousing:
                        {
                            if (!Bools.Get(ShortGuids.is_door))
                            {
                                bool result = true;
                                return GetValueAs<T>(result);
                            }
                            {
                                DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.door_mechanism);
                                bool result = door_mechanism != DOOR_MECHANISM.HIDDEN_BUTTON &&
                                             door_mechanism != DOOR_MECHANISM.HIDDEN_KEYPAD &&
                                             door_mechanism != DOOR_MECHANISM.HIDDEN_HACKING &&
                                             door_mechanism != DOOR_MECHANISM.HIDDEN_LEVER &&
                                             door_mechanism != DOOR_MECHANISM.BUTTON &&
                                             door_mechanism != DOOR_MECHANISM.KEYPAD &&
                                             door_mechanism != DOOR_MECHANISM.HACKING &&
                                             door_mechanism != DOOR_MECHANISM.LEVER;
                                return GetValueAs<T>(result);
                            }
                        }
                    case FunctionType.DeleteKeypad:
                        {
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.door_mechanism);
                            bool result = door_mechanism != DOOR_MECHANISM.KEYPAD && door_mechanism != DOOR_MECHANISM.HIDDEN_KEYPAD;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.DeletePullLever:
                        {
                            LEVER_TYPE lever_type = (LEVER_TYPE)EnumIndexes.Get(ShortGuids.lever_type);
                            bool result = true;
                            if (lever_type == LEVER_TYPE.PULL)
                            {
                                DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.door_mechanism);
                                result = door_mechanism != DOOR_MECHANISM.HIDDEN_LEVER && door_mechanism != DOOR_MECHANISM.LEVER;
                            }
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.DeleteRotateLever:
                        {
                            LEVER_TYPE lever_type = (LEVER_TYPE)EnumIndexes.Get(ShortGuids.lever_type);
                            bool result = true;
                            if (lever_type == LEVER_TYPE.ROTATE)
                            {
                                DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.door_mechanism);
                                result = door_mechanism != DOOR_MECHANISM.HIDDEN_LEVER && door_mechanism != DOOR_MECHANISM.LEVER;
                            }
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.DoorStatus:
                        {
                            int result = 0;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FilterAnd:
                        {
                            List<InstancedEntity> filters = Bools.GetLinks(ShortGuids.filter);
                            bool result = true;
                            for (int i = 0; i < filters.Count; i++)
                            {
                                if (!filters[i].GetAs<bool>())
                                {
                                    result = false;
                                    break;
                                }
                            }
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FilterNot:
                        {
                            List<InstancedEntity> filters = Bools.GetLinks(ShortGuids.filter);
                            bool result = filters.Count == 0 ? true : !filters[0].GetAs<bool>();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FilterOr:
                        {
                            List<InstancedEntity> filters = Bools.GetLinks(ShortGuids.filter);
                            bool result = false;
                            for (int i = 0; i < filters.Count; i++)
                            {
                                if (filters[i].GetAs<bool>())
                                {
                                    result = true;
                                    break;
                                }
                            }
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatAbsolute:
                        {
                            float result = Math.Abs(Floats.Get(ShortGuids.Input));
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatAdd:
                        {
                            float result = Floats.Get(ShortGuids.LHS) + Floats.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatAdd_All:
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks(ShortGuids.Numbers);
                            float sum = 0;
                            for (int i = 0; i < numbers.Count; i++)
                                sum += numbers[i].GetAs<float>();
                            return GetValueAs<T>(sum);
                        }
                    case FunctionType.FloatClamp:
                        {
                            float val = Floats.Get(ShortGuids.Value);
                            float min = Floats.Get(ShortGuids.Min);
                            float max = Floats.Get(ShortGuids.Max);
                            if (val < min) val = min;
                            if (val > max) val = max;
                            return GetValueAs<T>(val);
                        }
                    case FunctionType.FloatClampMultiply:
                        {
                            float val = Floats.Get(ShortGuids.LHS);
                            float min = Floats.Get(ShortGuids.Min);
                            float max = Floats.Get(ShortGuids.Max) * Floats.Get(ShortGuids.RHS);
                            if (val < min) val = min;
                            if (val > max) val = max;
                            return GetValueAs<T>(val);
                        }
                    case FunctionType.FloatDivide:
                        {
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float result = Math.Abs(rhs) < 0.0001f ? 0.0f : (Floats.Get(ShortGuids.LHS) / rhs);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatEquals:
                        {
                            bool result = Math.Abs(Floats.Get(ShortGuids.LHS) - Floats.Get(ShortGuids.RHS)) < Math.Abs(Floats.Get(ShortGuids.Threshold));
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatGetLinearProportion:
                        {
                            float min = Floats.Get(ShortGuids.Min);
                            float max = Floats.Get(ShortGuids.Max);
                            float mid = Floats.Get(ShortGuids.Input);
                            float result = (mid - min) / (max - min);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatGreaterThan:
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float threshold = Floats.Get(ShortGuids.Threshold);
                            bool result = Math.Abs(lhs - rhs) >= threshold && lhs > rhs;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatGreaterThanOrEqual:
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float threshold = Floats.Get(ShortGuids.Threshold);
                            bool result = Math.Abs(lhs - rhs) < threshold || lhs > rhs;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatLessThan:
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float threshold = Floats.Get(ShortGuids.Threshold);
                            bool result = Math.Abs(lhs - rhs) >= threshold && lhs < rhs;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatLessThanOrEqual:
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float threshold = Floats.Get(ShortGuids.Threshold);
                            bool result = Math.Abs(lhs - rhs) < threshold || lhs < rhs;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatLinearInterpolateSpeed:
                    case FunctionType.FloatLinearInterpolateTimed:
                        {
                            float result = Floats.Get(ShortGuids.Initial_Value);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatLinearProportion:
                        {
                            float min = Floats.Get(ShortGuids.Initial_Value);
                            float max = Floats.Get(ShortGuids.Target_Value);
                            float result = min + (max - min) * Floats.Get(ShortGuids.Proportion);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatMax:
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float result = lhs > rhs ? lhs : rhs;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatMax_All:
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks(ShortGuids.Numbers);
                            float max = 0;
                            for (int i = 0; i < numbers.Count; i++)
                            {
                                float number = numbers[i].GetAs<float>();
                                if (max < number) max = number;
                            }
                            return GetValueAs<T>(max);
                        }
                    case FunctionType.FloatMin:
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float result = lhs < rhs ? lhs : rhs;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatMin_All:
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks(ShortGuids.Numbers);
                            float min = 0;
                            if (numbers.Count > 0)
                            {
                                min = numbers[0].GetAs<float>();
                                for (int i = 1; i < numbers.Count; i++)
                                {
                                    float number = numbers[i].GetAs<float>();
                                    if (number < min) min = number;
                                }
                            }
                            return GetValueAs<T>(min);
                        }
                    case FunctionType.FloatModulate:
                        {
                            float PI = 3.1415926535897932333797165867879296635503123989707390137482903185973555f;

                            float offset = Floats.Get(ShortGuids.bias);
                            float amplitude = Floats.Get(ShortGuids.amplitude);

                            float phase = Floats.Get(ShortGuids.phase) / 360.0f;
                            float output = phase % 1.0f;

                            WAVE_SHAPE wave_shape = (WAVE_SHAPE)EnumIndexes.Get(ShortGuids.wave_shape);
                            switch (wave_shape)
                            {
                                case WAVE_SHAPE.SIN:
                                    output = (float)Math.Sin(output * 2.0f * PI);
                                    break;
                                case WAVE_SHAPE.SAW:
                                    output = (0.5f - output) * 2.0f;
                                    break;
                                case WAVE_SHAPE.REV_SAW:
                                    output = (output - 0.5f) * 2.0f;
                                    break;
                                case WAVE_SHAPE.SQUARE:
                                    output = (output < 0.5f) ? 1.0f : -1.0f;
                                    break;
                                case WAVE_SHAPE.TRIANGLE:
                                    if (output < 0.25f) output = output * 4.0f;
                                    else if (output < 0.75f) output = (0.5f - output) * 4.0f;
                                    else output = (output - 1.0f) * 4.0f;
                                    break;
                            }
                            float result = offset + amplitude * output;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatModulateRandom:
                        {
                            float result = 0.0f;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatMultiply:
                        {
                            float result = Floats.Get(ShortGuids.LHS) * Floats.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatMultiply_All:
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks(ShortGuids.Numbers);
                            float sum = 0;
                            if (numbers.Count > 0)
                            {
                                sum = numbers[0].GetAs<float>();
                                for (int i = 1; i < numbers.Count; i++)
                                    sum *= numbers[i].GetAs<float>();
                            }
                            return GetValueAs<T>(sum);
                        }
                    case FunctionType.FloatMultiplyClamp:
                        {
                            float val = Floats.Get(ShortGuids.LHS) * Floats.Get(ShortGuids.RHS);
                            float min = Floats.Get(ShortGuids.Min);
                            float max = Floats.Get(ShortGuids.Max);
                            if (val < min) val = min;
                            if (val > max) val = max;
                            return GetValueAs<T>(val);
                        }
                    case FunctionType.FloatNotEqual:
                        {
                            bool result = !(Math.Abs(Floats.Get(ShortGuids.LHS) - Floats.Get(ShortGuids.RHS)) < Math.Abs(Floats.Get(ShortGuids.Threshold)));
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatReciprocal:
                        {
                            float result = 1.0f / Floats.Get(ShortGuids.Input);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatRemainder:
                        {
                            float result = Floats.Get(ShortGuids.LHS) % Floats.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatSqrt:
                        {
                            float result = (float)Math.Sqrt(Math.Abs(Floats.Get(ShortGuids.Input)));
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.FloatSubtract:
                        {
                            float result = Floats.Get(ShortGuids.LHS) - Floats.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.GetGatingToolLevel:
                        {
                            int result = 0;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.GetPlayerHasGatingTool:
                        {
                            bool result = false;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.GetPlayerHasKeycard:
                        {
                            bool result = false;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.GetRotation:
                        {
                            Vector3 result = Transforms.Get(ShortGuids.Input).Rotation;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.GetTranslation:
                        {
                            Vector3 result = Transforms.Get(ShortGuids.Input).Position;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.GetX:
                        {
                            float result = Vectors.Get(ShortGuids.Input).X;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.GetY:
                        {
                            float result = Vectors.Get(ShortGuids.Input).Y;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.GetZ:
                        {
                            float result = Vectors.Get(ShortGuids.Input).Z;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.HasAccessAtDifficulty:
                        {
                            bool result = false;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerAbsolute:
                        {
                            int result = Math.Abs(Integers.Get(ShortGuids.Input));
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerAdd:
                        {
                            int result = Integers.Get(ShortGuids.LHS) + Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerAdd_All:
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks(ShortGuids.Numbers);
                            int sum = 0;
                            for (int i = 0; i < numbers.Count; i++)
                                sum += numbers[i].GetAs<int>();
                            return GetValueAs<T>(sum);
                        }
                    case FunctionType.IntegerAnd:
                        {
                            int result = Integers.Get(ShortGuids.LHS) & Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerCompliment:
                        {
                            int result = ~Integers.Get(ShortGuids.Input);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerDivide:
                        {
                            int result = Integers.Get(ShortGuids.LHS) / Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerEquals:
                        {
                            bool result = Integers.Get(ShortGuids.LHS) == Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerGreaterThan:
                        {
                            bool result = Integers.Get(ShortGuids.LHS) > Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerGreaterThanOrEqual:
                        {
                            bool result = Integers.Get(ShortGuids.LHS) >= Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerLessThan:
                        {
                            bool result = Integers.Get(ShortGuids.LHS) < Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerLessThanOrEqual:
                        {
                            bool result = Integers.Get(ShortGuids.LHS) <= Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerMax:
                        {
                            int lhs = Integers.Get(ShortGuids.LHS);
                            int rhs = Integers.Get(ShortGuids.RHS);
                            int result = lhs > rhs ? lhs : rhs;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerMax_All:
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks(ShortGuids.Numbers);
                            int max = 0;
                            for (int i = 0; i < numbers.Count; i++)
                            {
                                int number = numbers[i].GetAs<int>();
                                if (max < number) max = number;
                            }
                            return GetValueAs<T>(max);
                        }
                    case FunctionType.IntegerMin:
                        {
                            int lhs = Integers.Get(ShortGuids.LHS);
                            int rhs = Integers.Get(ShortGuids.RHS);
                            int result = lhs < rhs ? lhs : rhs;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerMin_All:
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks(ShortGuids.Numbers);
                            int min = 0;
                            if (numbers.Count > 0)
                            {
                                min = numbers[0].GetAs<int>();
                                for (int i = 1; i < numbers.Count; i++)
                                {
                                    int number = numbers[i].GetAs<int>();
                                    if (number < min) min = number;
                                }
                            }
                            return GetValueAs<T>(min);
                        }
                    case FunctionType.IntegerMultiply:
                        {
                            int result = Integers.Get(ShortGuids.LHS) * Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerMultiply_All:
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks(ShortGuids.Numbers);
                            int sum = 0;
                            if (numbers.Count > 0)
                            {
                                sum = numbers[0].GetAs<int>();
                                for (int i = 1; i < numbers.Count; i++)
                                    sum *= numbers[i].GetAs<int>();
                            }
                            return GetValueAs<T>(sum);
                        }
                    case FunctionType.IntegerNotEqual:
                        {
                            bool result = Integers.Get(ShortGuids.LHS) != Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerOr:
                        {
                            int result = Integers.Get(ShortGuids.LHS) | Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerRemainder:
                        {
                            int result = Integers.Get(ShortGuids.LHS) % Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.IntegerSubtract:
                        {
                            int result = Integers.Get(ShortGuids.LHS) - Integers.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.JOB_SpottingPosition:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.LogicGate:
                        {
                            bool result = Bools.Get(ShortGuids.allow);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.LogicGateAnd:
                        {
                            bool result = Bools.Get(ShortGuids.LHS) && Bools.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.LogicGateEquals:
                        {
                            bool result = Bools.Get(ShortGuids.LHS) == Bools.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.LogicGateNotEqual:
                        {
                            bool result = Bools.Get(ShortGuids.LHS) != Bools.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.LogicGateOr:
                        {
                            bool result = Bools.Get(ShortGuids.LHS) || Bools.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.LogicNot:
                        {
                            bool result = !Bools.Get(ShortGuids.Input);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.LogicOnce:
                        {
                            bool result = true;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.LogicSwitch:
                        {
                            bool result = Bools.Get(ShortGuids.initial_value);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.NavMeshArea:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.NavMeshBarrier:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.NavMeshExclusionArea:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.NavMeshReachabilitySeedPoint:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.NavMeshWalkablePlatform:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.NonPersistentBool:
                        {
                            bool result = Bools.Get(ShortGuids.initial_value);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.NonPersistentInt:
                        {
                            int result = Integers.Get(ShortGuids.initial_value);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.PathfindingAlienBackstageNode:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.PathfindingManualNode:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.PathfindingTeleportNode:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.PathfindingWaitNode:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.PlatformConstantBool:
                        {
                            bool result = Bools.Get(ShortGuids.NextGen);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.PlatformConstantFloat:
                        {
                            float result = Floats.Get(ShortGuids.NextGen);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.PlatformConstantInt:
                        {
                            int result = Integers.Get(ShortGuids.NextGen);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.PositionDistance:
                        {
                            float result = 0.0f;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.RandomBool:
                        {
                            bool result = (float)new Random().NextDouble() < 0.5f;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.RandomFloat:
                        {
                            float min = Floats.Get(ShortGuids.Min);
                            float range = Floats.Get(ShortGuids.Max) - min;
                            float rand = (float)new Random(GetDeterministicSeed()).NextDouble() * range;
                            float result = rand + min;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.RandomInt:
                        {
                            int min = Integers.Get(ShortGuids.Min);
                            int range = Integers.Get(ShortGuids.Max) - min;
                            int rand = range > 0 ? new Random(GetDeterministicSeed()).Next(range) : 0;
                            int result = rand + min;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.RandomSelect:
                        {
                            if (TryResolveRandomSelectInput<T>(out T selected))
                                return selected;
                            break;
                        }
                    case FunctionType.RandomVector:
                        {
                            float minX = Integers.Get(ShortGuids.MinX);
                            float rangeX = Integers.Get(ShortGuids.MaxX) - minX;
                            float randX = (float)new Random().NextDouble() * rangeX;
                            float minY = Integers.Get(ShortGuids.MinY);
                            float rangeY = Integers.Get(ShortGuids.MaxY) - minY;
                            float randY = (float)new Random().NextDouble() * rangeY;
                            float minZ = Integers.Get(ShortGuids.MinZ);
                            float rangeZ = Integers.Get(ShortGuids.MaxZ) - minZ;
                            float randZ = (float)new Random().NextDouble() * rangeZ;

                            Vector3 result = new Vector3(randX + minX, randY + minY, randZ + minZ);
                            if (Bools.Get(ShortGuids.Normalised))
                            {
                                float length = (float)Math.Sqrt(result.X * result.X + result.Y * result.Y + result.Z * result.Z);
                                if (length == 0.0f)
                                    result = new Vector3(0, 1, 0);
                                result /= length;
                            }
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.RegisterCharacterModel:
                        if (typeof(T) == typeof(string))
                            return (T)(object)Strings.Get(ShortGuids.display_model);
                        break;
                    case FunctionType.SetBool:
                        {
                            bool result = Bools.Get(ShortGuids.Input);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.SetColour:
                        {
                            Vector3 result = Vectors.Get(ShortGuids.Colour);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.SetFloat:
                        {
                            float result = Floats.Get(ShortGuids.Input);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.SetInteger:
                        {
                            int result = Integers.Get(ShortGuids.Input);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.SetString:
                        if (typeof(T) == typeof(string))
                            return (T)(object)Strings.Get(ShortGuids.initial_value);
                        break;
                    case FunctionType.SetVector:
                        {
                            Vector3 result = new Vector3(Floats.Get(ShortGuids.x), Floats.Get(ShortGuids.y), Floats.Get(ShortGuids.z));
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.SetVector2:
                        {
                            Vector3 result = Vectors.Get(ShortGuids.Input);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.SoundObject:
                        {
                            Transform result = new Transform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.SpottingExclusionArea:
                        {
                            Transform result = CalculateWorldTransform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.TriggerCameraVolume:
                        {
                            float result = 0.0f;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.TriggerSelect:
                        {
                            if (TryResolveTriggerSelectObject<T>(out T selected))
                                return selected;
                            break;
                        }
                    case FunctionType.VariableBool:
                        {
                            bool result = Bools.Get(ShortGuids.initial_value);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.VariableColour:
                        {
                            Vector3 result = Vectors.Get(ShortGuids.initial_colour);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.VariableEnum:
                        {
                            int result = EnumIndexes.Get(ShortGuids.initial_value);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.VariableFlashScreenColour:
                        {
                            Vector3 result = Vectors.Get(ShortGuids.initial_colour);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.VariableFloat:
                        {
                            float result = Floats.Get(ShortGuids.initial_value);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.VariableInt:
                        {
                            int result = Integers.Get(ShortGuids.initial_value);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.VariablePosition:
                        {
                            Transform result = new Transform();
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.VariableString:
                        if (typeof(T) == typeof(string))
                            return (T)(object)Strings.Get(ShortGuids.initial_value);
                        break;
                    case FunctionType.VariableVector:
                        {
                            Vector3 result = new Vector3(Floats.Get(ShortGuids.initial_x), Floats.Get(ShortGuids.initial_y), Floats.Get(ShortGuids.initial_z));
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.VariableVector2:
                        {
                            Vector3 result = Vectors.Get(ShortGuids.initial_value);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.VectorLinearInterpolateTimed:
                        {
                            Vector3 result = Vectors.Get(ShortGuids.Initial_Value);
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.VectorScale:
                        {
                            Vector3 result = Vectors.Get(ShortGuids.LHS) * Vectors.Get(ShortGuids.RHS);
                            return GetValueAs<T>(result);
                        }
                }
            }

            FunctionType? inherited = Level.Commands.Utils.GetInheritedFunction(type);
            if (inherited.HasValue)
            {
                //If the child class might supply a value, check it
                return GetFunctionData<T>(guid, inherited.Value);
            }
            else
            {
                //We've reached the bottom of the inheritance, so just return a default value
                if (typeof(T) == typeof(bool))
                    return (T)(object)false;
                else if (typeof(T) == typeof(int))
                    return (T)(object)0;
                else if (typeof(T) == typeof(float))
                    return (T)(object)0.0f;
                else if (typeof(T) == typeof(Vector3))
                    return (T)(object)new Vector3(0, 0, 0);
                else if (typeof(T) == typeof(Transform))
                {
                    if (Transforms.Has(ShortGuids.position))
                        return (T)(object)Transforms.Get(ShortGuids.position);
                    else
                        return (T)(object)new Transform();
                }
                else if (typeof(T) == typeof(cResource))
                    return (T)(object)new cResource();
                else if (typeof(T) == typeof(string))
                    return (T)(object)""; //NULL_STRING
                else
                {
                    throw new Exception("Unhandled");
                }
            }
        }

        private InstancedEntity FindChildVariablePin(ShortGuid pinName)
        {
            if (ChildCompositeInstance?.Entities == null)
                return null;
            for (int i = 0; i < ChildCompositeInstance.Entities.Count; i++)
            {
                InstancedEntity child = ChildCompositeInstance.Entities[i];
                if (child?.Entity is VariableEntity variable && variable.name == pinName)
                    return child;
            }
            return null;
        }

        private InstancedEntity FindEntityInThisComposite(ShortGuid entityId)
        {
            if (ThisCompositeInstance?.Entities == null)
                return null;
            for (int i = 0; i < ThisCompositeInstance.Entities.Count; i++)
            {
                InstancedEntity entity = ThisCompositeInstance.Entities[i];
                if (entity?.Entity != null && entity.Entity.shortGUID == entityId)
                    return entity;
            }
            return null;
        }

        private bool TryResolveOwnChildLink<T>(ShortGuid thisParamId, out T value)
        {
            value = default;
            if (Entity?.childLinks == null)
                return false;
            for (int i = 0; i < Entity.childLinks.Count; i++)
            {
                EntityConnector link = Entity.childLinks[i];
                if (link.thisParamID != thisParamId)
                    continue;
                InstancedEntity target = FindEntityInThisComposite(link.linkedEntityID);
                if (target == null)
                    continue;
                value = target.GetAs<T>(link.linkedParamID);
                return true;
            }
            return false;
        }

        private bool TryResolveObjectPinLink<T>(ShortGuid objectPin, out T value)
        {
            value = default;
            return TryResolveOwnChildLink(objectPin, out value);
        }

        private bool TryResolveTriggerSelectObject<T>(out T value)
        {
            value = default;
            int index = Integers.Get(ShortGuids.index);
            if (index < 0)
                index = 0;
            if (index >= ShortGuids.TriggerSelectObjectPins.Length)
                index = ShortGuids.TriggerSelectObjectPins.Length - 1;
            return TryResolveObjectPinLink(ShortGuids.TriggerSelectObjectPins[index], out value);
        }

        private bool TryResolveRandomSelectInput<T>(out T value)
        {
            value = default;
            if (Entity?.childLinks == null)
                return false;

            List<EntityConnector> inputs = null;
            for (int i = 0; i < Entity.childLinks.Count; i++)
            {
                EntityConnector link = Entity.childLinks[i];
                if (link.thisParamID != ShortGuids.Input)
                    continue;
                if (inputs == null)
                    inputs = new List<EntityConnector>();
                inputs.Add(link);
            }
            if (inputs == null || inputs.Count == 0)
                return false;

            int seed = GetDeterministicSeed();
            if (Floats.Has(ShortGuids.Seed) || (Floats.Links != null && Floats.Links.ContainsKey(ShortGuids.Seed)))
            {
                float seedFloat = Floats.Get(ShortGuids.Seed);
                if (seedFloat != 0.0f)
                    seed = seedFloat.GetHashCode();
            }

            int pick = new Random(seed).Next(inputs.Count);
            EntityConnector chosen = inputs[pick];
            InstancedEntity target = FindEntityInThisComposite(chosen.linkedEntityID);
            if (target == null)
                return false;
            value = target.GetAs<T>(chosen.linkedParamID);
            return true;
        }

        // The entity whose parameter is being resolved right now, for the length of that resolution.
        // A composite marked is_shared is instanced ONCE and every user links to that one instance,
        // so a randomiser inside it would hand all of them the same roll - every VDU in
        // ChallengeMap4 ends up showing the same screen where retail's show a spread of fifteen.
        // Seeding on the entity that ASKED restores the variety. It has to be tracked rather than
        // passed down because the chain from a parameter to the randomiser runs through several
        // entities (SCREEN.material -> Material -> MaterialStringSelect -> MaterialString ->
        // RandomStaticVDUscreen.RandomVDUString -> RandomSelect_1), and only the outermost caller
        // is the one to seed on. Thread-static because the instancing pass resolves in parallel.
        [ThreadStatic] private static InstancedEntity _resolutionRoot;

        //The murmur3 32-bit finaliser: spreads a clustered input across the whole range.
        private static int Avalanche(int value)
        {
            unchecked
            {
                uint h = (uint)value;
                h ^= h >> 16;
                h *= 0x85ebca6b;
                h ^= h >> 13;
                h *= 0xc2b2ae35;
                h ^= h >> 16;
                return (int)h;
            }
        }

        internal int GetDeterministicSeed()
        {
            unchecked
            {
                //Whoever asked, if anyone did; otherwise this entity, which is the same thing when
                //nothing is being resolved through a link.
                InstancedEntity subject = _resolutionRoot ?? this;
                int seed = subject.Entity != null ? (int)subject.Entity.shortGUID.AsUInt32 : 0;
                if (subject.ParentCompositeInstanceEntity?.ThisCompositeInstance != null)
                    seed = (seed * 397) ^ (int)subject.ParentCompositeInstanceEntity.ThisCompositeInstance.InstanceID.AsUInt32;
                else if (subject.ThisCompositeInstance != null)
                    seed = (seed * 397) ^ (int)subject.ThisCompositeInstance.InstanceID.AsUInt32;
                //Mix the randomiser's own identity back in so two different randomisers reached by
                //the same caller do not roll in lockstep.
                if (!ReferenceEquals(subject, this) && Entity != null)
                    seed = (seed * 397) ^ (int)Entity.shortGUID.AsUInt32;
                //Avalanche it. Composite instance ids come out in clusters, and Random(seed).Next(n)
                //returns the same value for a whole run of nearby seeds - ChallengeMap4's 318 VDUs
                //landed on six of their randomiser's twenty-five screens until this was added.
                seed = Avalanche(seed);
                return seed == 0 ? 1 : seed;
            }
        }

        private T GetDefaultValueAs<T>()
        {
            if (typeof(T) == typeof(bool))
                return (T)(object)false;
            else if (typeof(T) == typeof(int))
                return (T)(object)0;
            else if (typeof(T) == typeof(float))
                return (T)(object)0.0f;
            else if (typeof(T) == typeof(Vector3))
                return (T)(object)new Vector3(0, 0, 0);
            else if (typeof(T) == typeof(Transform))
                return (T)(object)new Transform();
            else if (typeof(T) == typeof(cResource))
                return (T)(object)new cResource();
            else if (typeof(T) == typeof(string))
                return (T)(object)"";
            else
                return default(T);
        }

        private T GetValueAs<T>(object value)
        {
            if (value == null)
                return GetDefaultValueAs<T>();

            Type valueType = value.GetType();

            if (typeof(T) == valueType)
            {
                return (T)value;
            }
            else if (valueType == typeof(bool))
            {
                bool b = (bool)value;
                if (typeof(T) == typeof(int))
                    return (T)(object)(b ? 1 : 0);
                if (typeof(T) == typeof(float))
                    return (T)(object)(b ? 1.0f : 0.0f);
                if (typeof(T) == typeof(string))
                    return (T)(object)(b ? "TRUE" : "FALSE");
            }
            else if (valueType == typeof(int))
            {
                int i = (int)value;
                if (typeof(T) == typeof(bool))
                    return (T)(object)(i != 0);
                if (typeof(T) == typeof(float))
                    return (T)(object)(float)i;
                if (typeof(T) == typeof(string))
                    return (T)(object)i.ToString();
            }
            else if (valueType == typeof(float))
            {
                float f = (float)value;
                if (typeof(T) == typeof(bool))
                    return (T)(object)(f != 0.0f);
                if (typeof(T) == typeof(int))
                    return (T)(object)(int)f;
                if (typeof(T) == typeof(string))
                    return (T)(object)f.ToString();
                if (typeof(T) == typeof(Vector3))
                    return (T)(object)new Vector3(f, f, f);
                if (typeof(T) == typeof(Transform))
                    return (T)(object)new Transform() { Position = new Vector3(f, f, f) };
            }
            else if (valueType == typeof(string))
            {
                string s = (string)value;
                if (typeof(T) == typeof(bool))
                {
                    bool result = s.ToUpper() == "TRUE";
                    return (T)(object)result;
                }
                if (typeof(T) == typeof(int))
                {
                    if (int.TryParse(s, out int result))
                        return (T)(object)result;
                    return (T)(object)0;
                }
                if (typeof(T) == typeof(float))
                {
                    if (float.TryParse(s, out float result))
                        return (T)(object)result;
                    return (T)(object)0.0f;
                }
            }
            else if (valueType == typeof(Vector3))
            {
                Vector3 v = (Vector3)value;
                if (typeof(T) == typeof(Transform))
                    return (T)(object)new Transform() { Position = v };
            }
            else if (valueType == typeof(Transform))
            {
                Transform t = (Transform)value;
                if (typeof(T) == typeof(Vector3))
                    return (T)(object)t.Position;
            }

            if (!typeof(T).IsInstanceOfType(value))
                return GetDefaultValueAs<T>();

            return (T)value;
        }

        #region Equality Checks
        public override bool Equals(object obj)
        {
            if (obj is InstancedEntity other)
            {
                return this.Path == other.Path;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return Path?.GetHashCode() ?? 0;
        }

        public static bool operator ==(InstancedEntity x, InstancedEntity y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return false;
            return x.Path == y.Path;
        }

        public static bool operator !=(InstancedEntity x, InstancedEntity y)
        {
            return !(x == y);
        }

        public int CompareTo(InstancedEntity other)
        {
            if (other == null) return 1;
            if (ReferenceEquals(this, other)) return 0;

            uint thisPathValue = Path?.ToUInt32() ?? 0;
            uint otherPathValue = other.Path?.ToUInt32() ?? 0;
            if (thisPathValue > otherPathValue)
                return 1;
            else if (thisPathValue < otherPathValue)
                return -1;
            return 0;
        }

        public (Vector3 position, Quaternion rotation) CalculateWorldPositionRotation()
        {
            Matrix4x4 worldMatrix = CalculateWorldTransformMatrix();
            Matrix4x4.Decompose(worldMatrix, out Vector3 scale, out Quaternion rotation, out Vector3 position);
            return (position, rotation);
        }

        public Transform CalculateWorldTransform()
        {
            (Vector3 position, Quaternion rotation) = CalculateWorldPositionRotation();
            (decimal yaw, decimal pitch, decimal roll) = rotation.ToYawPitchRoll();
            return new Transform()
            {
                Position = position,
                Rotation = new Vector3((float)pitch, (float)yaw, (float)roll)
            };
        }

        public Matrix4x4 CalculateWorldTransformMatrix()
        {
            Transform localTransform = GetAs<Transform>(ShortGuids.position);
            Matrix4x4 localMatrix = localTransform?.AsMatrix() ?? Matrix4x4.Identity;
            if (ParentCompositeInstanceEntity != null)
            {
                Matrix4x4 parentWorldMatrix = ParentCompositeInstanceEntity.CalculateWorldTransformMatrix();
                localMatrix = localMatrix * parentWorldMatrix;
            }
            return localMatrix;
        }
        #endregion
    }

    public class InstancedAlias
    {
        public List<ShortGuid> ActivePath = new List<ShortGuid>();
        public InstancedEntity InstancedInfo;
    }

    public class InstancedComposite : IComparable<InstancedComposite>
    {
        public ShortGuid InstanceID;
        public Composite Composite;
        public List<InstancedEntity> Entities = new List<InstancedEntity>();

        #region Equality Checks
        public override bool Equals(object obj)
        {
            if (obj is InstancedComposite other)
            {
                return this.InstanceID == other.InstanceID;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return InstanceID.GetHashCode();
        }

        public static bool operator ==(InstancedComposite x, InstancedComposite y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return false;
            return x.InstanceID == y.InstanceID;
        }

        public static bool operator !=(InstancedComposite x, InstancedComposite y)
        {
            return !(x == y);
        }

        public int CompareTo(InstancedComposite other)
        {
            if (other == null) return 1;
            if (ReferenceEquals(this, other)) return 0;

            return InstanceID.CompareTo(other.InstanceID);
        }
        #endregion
    }

    public class Instancing
    {
        private ConcurrentBag<InstancedEntity> AllEntities = new ConcurrentBag<InstancedEntity>();
        private ConcurrentBag<InstancedComposite> AllComposites = new ConcurrentBag<InstancedComposite>();

        private List<InstancedComposite> RequiredAssets = new List<InstancedComposite>();
        private InstancedComposite Root = new InstancedComposite();

        private Level _level = null;

        //Creates (or finds) the materials an instance needs that the authored data has no entry for.
        private MaterialFactory _materialFactory = null;

        private readonly ConcurrentDictionary<(Entity, Composite), List<(ShortGuid, ParameterVariant, DataType)>> _parameterCache = new ConcurrentDictionary<(Entity, Composite), List<(ShortGuid, ParameterVariant, DataType)>>();
        private readonly ConcurrentDictionary<(Composite, ShortGuid), Entity> _entityLookupCache = new ConcurrentDictionary<(Composite, ShortGuid), Entity>();

        private readonly object _resourcesLock = new object();
        private readonly object _physicsMapsLock = new object();
        private readonly object _collisionMapsLock = new object();
        private readonly object _mvrLock = new object();
        private readonly ConcurrentBag<InstancedEntity> _exclusiveMasters = new ConcurrentBag<InstancedEntity>();

        private List<ShortGuid> _sharedComposites = new List<ShortGuid>();
        private ShortGuid _globalGUID;

        private static readonly ShortGuid GlobalZoneId = new ShortGuid("01-00-00-00");

        private HavokCollisionTarget _collision;  
        private HavokCollisionTarget _collisionMirror;
        private readonly object _havokLock = new object();

        private sealed class HavokCollisionTarget
        {
            public readonly HavokPackfile Packfile;
            public readonly Dictionary<int, HavokPackfile.CompoundInstance> Prototypes;
            public readonly HavokPackfile.CompoundInstance FlagsPrototype;

            public HavokCollisionTarget(HavokPackfile packfile)
            {
                Packfile = packfile;
                Prototypes = packfile.BeginInstanceRebuild();
                FlagsPrototype = BuildFlagsPrototype(packfile.WorldHostPrimary);
            }

            public HavokPackfile.StaticCompoundShape PrimaryHost => Packfile.WorldHostPrimary;
            public HavokPackfile.StaticCompoundShape SecondaryHost => Packfile.WorldHostSecondary;
            public bool HasWorldHosts => PrimaryHost != null;

            public HavokPackfile.StaticCompoundShape HostFor(CollisionMaps.CollisionFlags mapFlags) => Packfile.WorldHostFor((mapFlags & CollisionMaps.CollisionFlags.WORLD) != 0);

            public void ClearWorldHosts()
            {
                if (PrimaryHost == null)
                    return;
                Packfile.PrepareCompoundForRebuild(PrimaryHost);
                if (SecondaryHost != null && !ReferenceEquals(SecondaryHost, PrimaryHost))
                    Packfile.PrepareCompoundForRebuild(SecondaryHost);
            }

            static HavokPackfile.CompoundInstance BuildFlagsPrototype(HavokPackfile.StaticCompoundShape host)
            {
                if (host == null || host.Instances.Count == 0)
                    return null;
                HavokPackfile.CompoundInstance src = host.Instances[0];
                return new HavokPackfile.CompoundInstance
                {
                    Translation = src.Translation,
                    Rotation = src.Rotation,
                    Scale = src.Scale,
                    FilterInfo = src.FilterInfo,
                    ChildFilterInfoMask = 0xFFFFFFFFu,
                    ShapeDataOffset = src.ShapeDataOffset,
                    ShapeClassName = src.ShapeClassName,
                };
            }
        }

        private readonly List<StateProperties> _states = new List<StateProperties>();
        private readonly List<string> _stateNotes = new List<string>();

        public sealed class StateProperties
        {
            public int StateIndex;
            public Level.State State;

            public Entity ExclusiveMaster;
            public ShortGuid CompositeInstanceId = ShortGuid.Invalid;
            public Resources.Resource Resource;

            public HashSet<HavokPackfile.CompoundInstance> ExcludedCollision = new HashSet<HavokPackfile.CompoundInstance>();

            public string Summary;
        }

        public IReadOnlyList<StateProperties> States => _states;
        public IReadOnlyList<string> StateNotes => _stateNotes;

        public IReadOnlyList<string> BakeWarnings => _bakeWarnings;
        private readonly List<string> _bakeWarnings = new List<string>();

        private void RunOptionalBake(string stage, Action bake)
        {
            try
            {
                bake();
            }
            catch (Exception e)
            {
                string message = stage + ": " + e.Message;
                _bakeWarnings.Add(message);
                Console.WriteLine("WARNING: skipped " + message);
            }
        }

        /// <summary>
        /// Entities whose radiosity_multiplier parameter is authored zero, keyed by
        /// (composite_instance_id, entity_id). The radiosity bake must attach no surface light to
        /// these; the MVR field alone cannot express the rule because an absent parameter also
        /// stores 0 there and those movers ARE lit.
        /// </summary>
        public readonly HashSet<(uint, uint)> RadiosityAuthoredOff = new HashSet<(uint, uint)>();

        /// <summary>
        /// Skip the navmesh / cover / job position / sound network bakes even though their
        /// settings are supplied. For fast lighting-only iteration from test harnesses; the
        /// previously saved data for those systems is left as-is on disk.
        /// </summary>
        public static bool SkipAgentBakes = false;

        /// <summary>
        /// Skip the alphalight bake, leaving the loaded data to round-trip. For isolating the
        /// radiosity bake's effect in A/B tests.
        /// </summary>
        public static bool SkipAlphalightBake = false;
 
        /// <summary>
        /// Obsolete. Template FX emitters are never given movers now - retail's own MVR shows
        /// isTemplate is the discriminator, so there is no longer a per-level choice to make.
        /// Retained only so existing callers still compile; it has no effect.
        /// </summary>
        [Obsolete("Template FX emitters are never emitted; this flag no longer has any effect.")]
        public static bool EmitFxTemplates = true;

        /// <summary>
        /// Emit movers for particle emitters inside REQUIRED_ASSETS composites (weapons, gadgets -
        /// content that spawns as temporary entities at runtime). The ribbon case has always
        /// skipped these; the particle case did not, and a pre-instanced mover for an emitter the
        /// engine expects to instantiate fresh at spawn is the current suspect for Solace's
        /// weapon-spawn fault in PARTICLE_EMITTER_REFERENCE::update_parameters.
        /// </summary>
        public static bool EmitRequiredAssetParticles = true;

        public Instancing(Level level, NavMeshBakeSettings navMeshSettings = null, CoverBakeSettings coverSettings = null, RadiosityBakeSettings radiositySettings = null, JobPositionBakeSettings jobPositionSettings = null, AlphalightBakeSettings alphalightSettings = null)
        {
            _level = level;

            GenerateInstances();
            ProcessInstances();
            BuildStateProperties();
            CarryRetailModelParams();

            if (!SkipAgentBakes)
            {
                RunOptionalBake("navmesh", () => NavMeshBaker.BakeLevel(level, this, navMeshSettings));
                RunOptionalBake("cover", () => CoverBaker.BakeLevel(level, this, coverSettings));
                RunOptionalBake("job positions", () => JobPositionBaker.BakeLevel(level, jobPositionSettings, Console.WriteLine));
                RunOptionalBake("sound networks", () => SoundNodeNetworkGenerator.Generate(level, AllEntities, Console.WriteLine));
            }

            if (!SkipAlphalightBake)
                RunOptionalBake("alphalight", () => AlphalightBaker.BakeLevel(level, alphalightSettings, Console.WriteLine));

            if (radiositySettings != null)
            {
                RadiosityBaker.BakeLevel(level, this, radiositySettings, Console.WriteLine);

                if (_level.Patched)
                    ClearRadiosityPatch();
            }
            else if (!SkipRadiosityClear)
            {
                ClearRadiosity();
            }
        }

        /// <summary>
        /// Do not blank the level's radiosity files when instancing without radiosity settings.
        /// Constructing an Instancing WRITES TO DISK - it empties RADIOSITY_RUNTIME.BIN and deletes
        /// RADIOSITY_INSTANCE_MAP.TXT in the level's own folder - even though nothing asked it to
        /// save. Anything that instances a level only to read the result (a diagnostic, a
        /// comparison against retail) must set this first, or it destroys the copy it is measuring.
        /// </summary>
        public static bool SkipRadiosityClear = false;

        /// <summary>
        /// Swap every mover's primary and secondary zone when it has two.
        /// </summary>
        /// <remarks>
        /// A FLICKER EXPERIMENT, not a fix - leave it false unless you are testing.
        ///
        /// Measured on ChallengeMap4: 95.8% of movers carry both zones exactly as retail does, and
        /// of the 529 that do not, 468 (88.5%) are our pair the RIGHT way up but the WRONG way
        /// round. Zones drive culling and streaming, and the level carries 3584 deliberately
        /// coincident movers (door, VDU and light state variants sharing a transform), so a mover
        /// whose zones are backwards is a candidate for models popping in and out.
        ///
        /// What to look for: if the flickering changes character at all - different models, or it
        /// stops - zones are implicated. Turning this on trades errors rather than removing them
        /// (the 468 become right and roughly 277 currently-correct ones become wrong), so a level
        /// that looks BETTER with it on is still evidence even though this is not the real rule.
        /// The real rule is not spatial: containment picks retail's primary only 64.6% of the time
        /// and nearest-centroid 45.9%, worse than chance.
        /// </remarks>
        public static bool SwapTwoZoneMoverOrder = false;

        /// <summary>
        /// The value written to every mover's <c>Flags.RequiresScript</c>.
        /// </summary>
        /// <remarks>
        /// ANOTHER FLICKER EXPERIMENT. We hardcode true and retail disagrees on 4049 of
        /// ChallengeMap4's 12672 movers - retail says false for 2172 ENVIRONMENT, 1438
        /// ENVIRONMENT_EXTRA, 203 LIGHT and 174 DYNAMICFX movers. "Always true" agrees with retail
        /// 68.5% of the time and no predicate tried beats it, so the rule is undecoded.
        ///
        /// If the engine expects a script to drive a mover marked this way and nothing does, that
        /// is a plausible source of a model appearing and disappearing. Setting this false makes us
        /// agree with retail on the 4049 and disagree on the 8623 that should be true, so neither
        /// setting is correct - but if the flickering moves to a DIFFERENT set of models when it is
        /// flipped, this flag is the cause and the rule is worth decoding properly.
        /// </remarks>
        public static bool MoverRequiresScript = true;

        private void ClearRadiosity()
        {
            ClearRadiosityAt(_level.Filepath);
            if (_level.Patched)
                ClearRadiosityPatch();
        }

        private void ClearRadiosityPatch()
        {
            ClearRadiosityAt(_level.Filepath.TrimEnd('/').TrimEnd('\\') + "_PATCH");
        }

        private static void ClearRadiosityAt(string root)
        {
            string world = root + "/WORLD/";
            if (Directory.Exists(world))
                File.WriteAllBytes(world + "RADIOSITY_COLLISION_MAPPING.BIN", new byte[4]);

            string renderable = root + "/RENDERABLE/";
            if (Directory.Exists(renderable))
            {
                File.WriteAllBytes(renderable + "RADIOSITY_RUNTIME.BIN", new byte[0]);
                File.Delete(renderable + "RADIOSITY_INSTANCE_MAP.TXT");
            }
        }

        // -----
        //todo - remove these!! - for test code in opencage app.
        public Level Level => _level;
        public IEnumerable<InstancedEntity> GeneratedEntities => AllEntities;
        public IEnumerable<InstancedComposite> GeneratedComposites => AllComposites;
        public IReadOnlyList<InstancedComposite> RequiredAssetInstances => RequiredAssets;
        public InstancedComposite LevelRoot => Root;
        public Dictionary<ShortGuid, (ShortGuid Primary, ShortGuid Secondary)> GetDualZoneAssignmentsByCompositeInstance()
        {
            var result = new Dictionary<ShortGuid, (ShortGuid Primary, ShortGuid Secondary)>();
            foreach (InstancedComposite composite in AllComposites)
            {
                if (composite.Entities == null)
                    continue;
                foreach (InstancedEntity entity in composite.Entities)
                {
                    if (entity.PrimaryZone == ShortGuid.Invalid && entity.SecondaryZone == ShortGuid.Invalid)
                        continue;
                    result[composite.InstanceID] = (entity.PrimaryZone, entity.SecondaryZone);
                    break;
                }
            }
            return result;
        }
        public Dictionary<ShortGuid, ShortGuid> GetZoneAssignmentsByCompositeInstance()
        {
            var result = new Dictionary<ShortGuid, ShortGuid>();
            foreach (InstancedComposite composite in AllComposites)
            {
                if (composite.Entities == null)
                    continue;
                foreach (InstancedEntity entity in composite.Entities)
                {
                    if (entity.PrimaryZone == ShortGuid.Invalid)
                        continue;
                    result[composite.InstanceID] = entity.PrimaryZone;
                    break;
                }
            }
            return result;
        }
        // -----

        private void GenerateInstances()
        {
            _globalGUID = _level.Commands.EntryPoints[1].shortGUID;

            List<Composite> requiredAssets = new List<Composite>();
            void AddRequired(Composite composite)
            {
                if (composite != null && !requiredAssets.Contains(composite))
                    requiredAssets.Add(composite);
            }

            AddRequired(_level.Commands.Entries.FirstOrDefault(o => o.name.ToUpper() == "GLOBAL"));
            AddRequired(_level.Commands.Entries.FirstOrDefault(o => o.name.ToUpper() == "PAUSEMENU"));
            AddRequired(_level.Commands.Entries.FirstOrDefault(o => o.name.ToUpper().Replace("/", "\\") == "REQUIRED_ASSETS\\JOBS\\INTERNAL\\SEARCHTARGETJOB\\SEARCHTARGETJOB"));
            foreach (Composite composite in _level.Commands.Entries)
            {
                if (composite.name.ToUpper().Replace("/", "\\").StartsWith("REQUIRED_ASSETS\\"))
                    AddRequired(composite);
            }

            foreach (Composite requiredAsset in requiredAssets)
            {
                InstancedComposite instancedRequiredAsset = new InstancedComposite()
                {
                    Composite = requiredAsset,
                    InstanceID = requiredAsset.shortGUID
                };
                RequiredAssets.Add(instancedRequiredAsset);
                GenerateInstances(requiredAsset, new EntityPath(), instancedRequiredAsset, null, null, new List<InstancedAlias>(), false, null);
            }

            Root = new InstancedComposite()
            {
                Composite = _level.Commands.EntryPoints[0],
                InstanceID = ShortGuid.InstanceGuid
            };
            GenerateInstances(Root.Composite, new EntityPath(), Root, null, null, new List<InstancedAlias>(), false, null);
        }

        private void ProcessInstances()
        {
            if (Root?.Composite == null)
                throw new Exception("Call GenerateInstances first");

            //TEMPORARY TEST CODE - need to actually figure out how frozen is applied rather than doing this best-guess
            _applyDefaultFrozen = ShouldApplyFrozen(_level.CollisionMaps.Entries);

            //Prep and clear Havok data
            /* A COLLISION.MAP row names an instance by its slot in the host compound, so one file
             * decides the numbering and the other width has to be built to match it slot for slot.
             * PC ships both and the 32-bit one leads; the mobile and Switch builds ship only 64, and
             * that one leads instead - there is nothing to mirror it to. */
            HavokPackfile collision32 = _level.CollisionHKX != null && _level.CollisionHKX.Loaded ? _level.CollisionHKX : null;
            HavokPackfile collision64 = _level.CollisionHKX64 != null && _level.CollisionHKX64.Loaded ? _level.CollisionHKX64 : null;

            _collision = collision32 != null ? new HavokCollisionTarget(collision32)
                : collision64 != null ? new HavokCollisionTarget(collision64) : null;
            _collision?.ClearWorldHosts();

            _collisionMirror = collision32 != null && collision64 != null ? new HavokCollisionTarget(collision64) : null;
            _collisionMirror?.ClearWorldHosts();

            //Snapshot everything the carries need BEFORE any table is cleared - the resource
            //table snapshot read an empty list when this ran after the clears.
            SnapshotModelParams();

            //Clear other various bits we'll re-write
            _level.Resources.Entries.Clear();
            _level.PhysicsMaps.Entries.Clear();
            // NOTE: RenderableElements is deliberately NOT cleared. Indices into it are held by
            // systems this pass does not renumber, so clearing it and rebuilding renumbered every
            // entry underneath them - the engine then read a character's renderable run out of
            // range and faulted in calculate_renderable_instance_type the moment ACTIVE_CHARACTERS
            // finalised its first temporary entity, on most levels in the game. Retail's own file
            // is mostly entries that no mover or Commands resource points at, which is the same
            // shape. EnsureRegistered reuses identical runs, so appending keeps it at retail's size
            // (18635 vs retail's 18633 on BSP_LV426_Pt01).
            _level.SoundEnvironmentData.Entries.Clear();
            while (!_exclusiveMasters.IsEmpty)
                _exclusiveMasters.TryTake(out _);

            //First 12 movers are required assets used by various things like particle systems, etc - keep them!
            //If building a level from scratch I'll need to add these somehow - store them? They're the same everywhere.
            //Before the old list goes: keep every environment mover's MODEL_PARAMS lightmap
            //transform, keyed by resource GUID. Instancing rebuilds movers from Commands and
            //cannot compute this - it is bake output - so without carrying it every rebuilt wall
            //samples a wrong atlas region (ChallengeMap3's vent wall rendered its neighbouring
            //tube-lights' yellow; ceiling pieces degenerated entirely). A full radiosity bake
            //rewrites these afterwards, so carrying is harmless there and essential everywhere
            //else (instonly, delta patches). NOTE: the snapshot itself now runs earlier, before
            //the table clears above.
            List<Movers.MOVER_DESCRIPTOR> requiredAssets = new List<Movers.MOVER_DESCRIPTOR>();
            if (_level.Movers.Entries.Count >= 12)
                for (int i = 0; i < 12; i++)
                    requiredAssets.Add(_level.Movers.Entries[i]);
            _level.Movers.Entries = requiredAssets;

            //Each entity with a collision mapping associated with it provides a 'template' version which has no instancing info.
            //Clear out the whole maps list and then add those back in before we add the instanced ones. Note there's also 18 empty ones in each level, so add those too.
            _level.CollisionMaps.Entries.Clear();
            for (int i = 0; i < 18; i++)
                _level.CollisionMaps.Entries.Add(new CollisionMaps.COLLISION_MAPPING());
            foreach (Composite composite in _level.Commands.Entries)
            {
                foreach (FunctionEntity function in composite.functions)
                {
                    ResourceReference resource = function.GetResource(ResourceType.COLLISION_MAPPING, true);
                    if (resource?.CollisionMapping == null)
                        continue;

                    _level.CollisionMaps.Entries.Add(resource.CollisionMapping);
                }
            }

            //Now calculate linked entity logic - zone and environment map assignment
            CalculateZones();
            CalculateEnvironmentMaps();

            //Materials an instance needs but the authored data doesn't hold - see MaterialFactory.
            _materialFactory = new MaterialFactory(_level);

            //Do the instancing!
            _sharedComposites.Clear();
            ProcessInstances(Root, false, false, false, false, false, false);

            if (_materialFactory.MaterialsCreated != 0 || _materialFactory.TexturesNotFound != 0)
            {
                Console.WriteLine("Instanced materials: created " + _materialFactory.MaterialsCreated +
                                  ", reused " + _materialFactory.MaterialsReused +
                                  (_materialFactory.TexturesNotFound != 0
                                      ? ", " + _materialFactory.TexturesNotFound + " gobo texture(s) unresolved"
                                      : ""));
                foreach (KeyValuePair<string, string> unresolved in _materialFactory.UnresolvedTextures)
                    Console.WriteLine("WARNING: gobo texture not packed with this level: " + unresolved.Key +
                                      (unresolved.Value == "" ? "" : "  (first requested by " + unresolved.Value + ")"));
            }

            //Re-write Commands-only (not instanced) REDs back to REDs since we cleared it out earlier
            PopulateCommandsREDs();

            //Rebuild Havok data
            ApplyHavokUserRows();
            _collision?.Packfile.CommitInstanceRebuild();
            _collisionMirror?.Packfile.CommitInstanceRebuild();

            //Regenerate level states (navmesh, cover, etc)
            BuildExclusiveMasterStates();

            //Rebuild the BVH for lights
            _level.Lights.RebuildFromMovers(_level.Movers);

            //Rebuild the BVH for occluder triangles (CA_OCCLUSION_CULLING meshes)
            _level.OccluderTriangleBVH.RebuildFromMovers(_level.Movers);
        }

        private void BuildStateProperties()
        {
            _states.Clear();
            _stateNotes.Clear();
            if (_level?.StateResources == null)
                return;

            ExclusiveMasterNavFilter filter = ExclusiveMasterNavFilter.Build(_level);
            _stateNotes.AddRange(filter.Notes);

            for (int i = 0; i < _level.StateResources.Count; i++)
            {
                Level.State state = _level.StateResources[i];
                ExclusiveMasterNavFilter.StateSkipSet skip = filter.GetSkipSetForState(i, state);
                _states.Add(new StateProperties
                {
                    StateIndex = i,
                    State = state,
                    ExclusiveMaster = state?.ExclusiveMaster,
                    CompositeInstanceId = state?.CompositeInstanceId ?? ShortGuid.Invalid,
                    Resource = state?.Resource,
                    ExcludedCollision = skip.Exclude,
                    Summary = skip.Summary
                });
            }
        }

        void PopulateCommandsREDs()
        {
            if (_level.RenderableElements == null)
                return;

            for (int i = 0; i < _level.Movers.Entries.Count; i++)
            {
                Movers.MOVER_DESCRIPTOR mvr = _level.Movers.Entries[i];
                if (mvr?.RenderableElements == null || mvr.RenderableElements.Count == 0)
                    continue;
                mvr.RenderableElements = _level.RenderableElements.EnsureRegistered(mvr.RenderableElements);
            }

            if (_level.Commands?.Entries == null)
                return;

            foreach (Composite composite in _level.Commands.Entries)
            {
                if (composite?.functions == null)
                    continue;
                foreach (FunctionEntity function in composite.functions)
                {
                    ResourceReference resource = function.GetResource(ResourceType.RENDERABLE_INSTANCE, true);
                    if (resource?.RenderableInstance == null || resource.RenderableInstance.Count == 0)
                        continue;
                    resource.RenderableInstance = _level.RenderableElements.EnsureRegistered(resource.RenderableInstance);
                }
            }
        }

        //Havok 'user data' looks up the collision map, so write the indexes now its populated
        void ApplyHavokUserRows()
        {
            List<CollisionMaps.COLLISION_MAPPING> rows = _level.CollisionMaps.Entries;
            var stamped = new HashSet<HavokPackfile.CompoundInstance>();

            for (int i = 0; i < rows.Count; i++)
            {
                HavokPackfile.CompoundInstance instance = rows[i].CollisionInstance;
                if (instance == null)
                    continue;

                instance.UserData = (ulong)i;
                stamped.Add(instance);

                //Whichever width leads, the other has to carry the same row index on the same slot
                HavokPackfile.StaticCompoundShape hostMirror = _collisionMirror?.HostFor(rows[i].Flags);
                int slot = instance.Index;
                if (hostMirror != null && slot >= 0 && slot < hostMirror.Instances.Count)
                    hostMirror.Instances[slot].UserData = (ulong)i;
            }

            //todo - i think maybe navmesh barrier uses a different id system?

            int orphans = CountUnstamped(_collision?.PrimaryHost, stamped) + CountUnstamped(_collision?.SecondaryHost, stamped);
            if (orphans > 0)
                Console.WriteLine("  WARNING: {0} Havok instances have no COLLISION.MAP row (zone lookup will miss)", orphans);
        }

        static int CountUnstamped(HavokPackfile.StaticCompoundShape host, HashSet<HavokPackfile.CompoundInstance> stamped)
        {
            if (host == null)
                return 0;
            int count = 0;
            for (int i = 0; i < host.Instances.Count; i++)
                if (!stamped.Contains(host.Instances[i]))
                    count++;
            return count;
        }

        //Creates a new instance for a Havok proxy
        HavokPackfile.CompoundInstance AllocateHavokCompoundInstance(InstancedEntity entity, HavokPackfile.StaticCompoundShape collisionProxy, CollisionMaps.CollisionFlags mapFlags)
        {
            if (_collision == null || collisionProxy == null)
                return null;

            Matrix4x4 worldMatrix = entity.CalculateWorldTransformMatrix();
            if (!Matrix4x4.Decompose(worldMatrix, out Vector3 scale, out Quaternion rotation, out Vector3 position))
            {
                position = Vector3.Zero;
                rotation = Quaternion.Identity;
                scale = Vector3.One;
            }

            if (Math.Abs(scale.X) < 1e-6f) scale.X = 1f;
            if (Math.Abs(scale.Y) < 1e-6f) scale.Y = 1f;
            if (Math.Abs(scale.Z) < 1e-6f) scale.Z = 1f;

            float padding = Math.Max(0.25f, Math.Max(Math.Abs(scale.X), Math.Max(Math.Abs(scale.Y), Math.Abs(scale.Z))));

            lock (_havokLock)
            {
                //Prefer world hosts: instance shape = the template compound object itself.
                if (_collision.HasWorldHosts)
                {
                    HavokPackfile.StaticCompoundShape host = _collision.HostFor(mapFlags);
                    HavokPackfile.CompoundInstance instance = EmitCompoundInstance(_collision, host, collisionProxy, mapFlags, position, rotation, scale, padding);

                    HavokPackfile.StaticCompoundShape hostMirror = _collisionMirror?.HostFor(mapFlags);
                    if (hostMirror != null)
                    {
                        HavokPackfile.StaticCompoundShape proxyMirror = _collisionMirror.Packfile.GetCompound(collisionProxy.ProxyIndex);
                        if (proxyMirror != null)
                        {
                            EmitCompoundInstance(_collisionMirror, hostMirror, proxyMirror, mapFlags, position, rotation, scale, padding);
                            if (hostMirror.Instances.Count != host.Instances.Count)
                                throw new InvalidOperationException(
                                    "COLLISION.HKX and HKX64 world-host instance slots diverged during rebuild.");
                        }
                    }
                    return instance;
                }

                //Fallback for packs with no recognisable world host: enqueue onto the template compound.
                int collisionProxyIndex = collisionProxy.ProxyIndex;
                if (!_collision.Prototypes.TryGetValue(collisionProxyIndex, out HavokPackfile.CompoundInstance prototype))
                    return null;

                HavokPackfile.CompoundInstance fallback = _collision.Packfile.EnqueueInstance(collisionProxy, position, rotation, scale, prototype, padding);
                if (_collisionMirror != null && _collisionMirror.Prototypes.TryGetValue(collisionProxyIndex, out HavokPackfile.CompoundInstance prototypeMirror))
                {
                    HavokPackfile.StaticCompoundShape proxyMirror = _collisionMirror.Packfile.GetCompound(collisionProxyIndex);
                    if (proxyMirror != null)
                        _collisionMirror.Packfile.EnqueueInstance(proxyMirror, position, rotation, scale, prototypeMirror, padding);
                }
                return fallback;
            }
        }

        static HavokPackfile.CompoundInstance EmitCompoundInstance(HavokCollisionTarget target, HavokPackfile.StaticCompoundShape host, HavokPackfile.StaticCompoundShape collisionProxy, CollisionMaps.CollisionFlags mapFlags, Vector3 position, Quaternion rotation, Vector3 scale, float padding)
        {
            HavokPackfile.CompoundInstance properties = BuildWorldHostInstanceProperties(collisionProxy.DataOffset, mapFlags, target.FlagsPrototype);
            HavokPackfile.CompoundInstance instance = target.Packfile.EnqueueInstance(host, position, rotation, scale, properties, padding);
            target.Packfile.ExpandDomainWithTransformedChild(host, collisionProxy, position, rotation, scale, padding);
            return instance;
        }

        static HavokPackfile.CompoundInstance BuildWorldHostInstanceProperties(uint templateCompoundDataOffset, CollisionMaps.CollisionFlags mapFlags, HavokPackfile.CompoundInstance flagsPrototype)
        {
            uint filterInfo = (uint)mapFlags & (uint)CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK;
            if ((mapFlags & CollisionMaps.CollisionFlags.GHOSTED) != 0)
                filterInfo = 0x12;

            uint flagBits = 0x3F000006u;
            if (flagsPrototype != null)
            {
                uint proto = BitConverter.ToUInt32(BitConverter.GetBytes(flagsPrototype.Translation.W), 0);
                flagBits = (proto & 0xFFFFFF80u) | 0x6u;
            }

            return new HavokPackfile.CompoundInstance
            {
                Translation = new Vector4(0, 0, 0, BitConverter.ToSingle(BitConverter.GetBytes(flagBits), 0)),
                Rotation = Quaternion.Identity,
                Scale = flagsPrototype != null ? flagsPrototype.Scale : new Vector4(1, 1, 1, BitConverter.ToSingle(BitConverter.GetBytes(0x3F000000u), 0)),
                FilterInfo = filterInfo,
                ChildFilterInfoMask = 0xFFFFFFFFu,
                // note - userdata is populated at the end
                ShapeDataOffset = templateCompoundDataOffset,
                ShapeClassName = "hkpStaticCompoundShape",
            };
        }

        //Adds a box shape to the Havok data (for CollisionBarriers, etc)
        HavokPackfile.CompoundInstance AllocateHavokBoxInstance(InstancedEntity entity, CollisionMaps.CollisionFlags mapFlags)
        {
            if (_collision == null)
                return null;

            HavokPackfile.StaticCompoundShape host = _collision.HostFor(mapFlags);
            if (host == null)
                return null;

            Matrix4x4 worldMatrix = entity.CalculateWorldTransformMatrix();
            if (!Matrix4x4.Decompose(worldMatrix, out Vector3 lossyScale, out Quaternion rotation, out Vector3 position))
            {
                position = Vector3.Zero;
                rotation = Quaternion.Identity;
                lossyScale = Vector3.One;
            }

            Vector3 halfDim = entity.Vectors.Has(ShortGuids.half_dimensions) ? entity.Vectors.Get(ShortGuids.half_dimensions) : new Vector3(0.5f, 1f, 0.5f);
            Vector3 halfExtents = new Vector3(Math.Abs(halfDim.X * lossyScale.X), Math.Abs(halfDim.Y * lossyScale.Y), Math.Abs(halfDim.Z * lossyScale.Z));
            if (halfExtents.X < 1e-4f) halfExtents.X = 1e-4f;
            if (halfExtents.Y < 1e-4f) halfExtents.Y = 1e-4f;
            if (halfExtents.Z < 1e-4f) halfExtents.Z = 1e-4f;

            Vector3 centre = position + Vector3.Transform(new Vector3(0f, halfExtents.Y, 0f), rotation);

            uint filterInfo = (uint)mapFlags & (uint)CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK;
            if ((mapFlags & CollisionMaps.CollisionFlags.GHOSTED) != 0)
                filterInfo = 0x12;

            const uint leafFlagBits = 0x3F000007u;
            Vector3 unityScale = Vector3.One;

            lock (_havokLock)
            {
                HavokPackfile.CompoundInstance instance = EmitBoxInstance(_collision, host, centre, rotation, halfExtents, unityScale, filterInfo, leafFlagBits);

                HavokPackfile.StaticCompoundShape hostMirror = _collisionMirror?.HostFor(mapFlags);
                if (hostMirror != null)
                {
                    EmitBoxInstance(_collisionMirror, hostMirror, centre, rotation, halfExtents, unityScale, filterInfo, leafFlagBits);
                    if (hostMirror.Instances.Count != host.Instances.Count)
                        throw new InvalidOperationException("COLLISION.HKX and HKX64 world-host instance slots diverged during box emit.");
                }

                return instance;
            }
        }

        static HavokPackfile.CompoundInstance EmitBoxInstance(HavokCollisionTarget target, HavokPackfile.StaticCompoundShape host, Vector3 centre, Quaternion rotation, Vector3 halfExtents, Vector3 scale, uint filterInfo, uint leafFlagBits)
        {
            var properties = new HavokPackfile.CompoundInstance
            {
                Translation = new Vector4(0, 0, 0, BitConverter.ToSingle(BitConverter.GetBytes(leafFlagBits), 0)),
                Rotation = Quaternion.Identity,
                Scale = new Vector4(1, 1, 1, BitConverter.ToSingle(BitConverter.GetBytes(0x3F000000u), 0)),
                FilterInfo = filterInfo,
                ChildFilterInfoMask = 0,
                // user data written at the end
                ShapeDataOffset = target.Packfile.AppendBoxShape(halfExtents),
                ShapeClassName = "hkpBoxShape",
            };

            HavokPackfile.CompoundInstance instance = target.Packfile.EnqueueInstance(host, centre, rotation, scale, properties, aabbPadding: 0.05f);
            target.Packfile.ExpandDomainWithBox(host, centre, rotation, halfExtents, scale);
            return instance;
        }

        //This finds every Zone entity and applies itself to any entities connected to it via the 'composites' pin.
        private void CalculateZones()
        {
            ParallelOptions opts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            Parallel.ForEach(AllEntities, opts, entity =>
            {
                entity.PrimaryZone = ShortGuid.Invalid;
                entity.SecondaryZone = ShortGuid.Invalid;
                entity.PrimaryZoneSourceInstance = ShortGuid.Invalid;
                entity.PrimaryZoneWasDirect = false;
                entity.PrimaryZoneDescentRoot = ShortGuid.Invalid;
            });

            List<InstancedEntity> zones = new List<InstancedEntity>();
            foreach (InstancedEntity entity in AllEntities)
            {
                if (entity.Entity.variant != EntityVariant.FUNCTION)
                    continue;

                FunctionEntity function = (FunctionEntity)entity.Entity;
                if (!function.function.IsFunctionType || function.function.AsFunctionType != FunctionType.Zone)
                    continue;
                if (entity.ThisCompositeInstance == null)
                    continue;

                zones.Add(entity);
            }

            //SEQUENTIAL on purpose. AssignZone is a first-arrival state machine (first zone to
            //reach an entity becomes primary, later arrivals fight for secondary), so running the
            //zones in Parallel.ForEach made arrival order a thread race: two runs of the same code
            //disagreed on ~800 movers, and ChallengeMap9 put 4,757 of 12,485 movers (38%) in a
            //different zone from retail - which flips zone streaming states, and that is visible:
            //rooms rendered that retail keeps unloaded (cam4), the vent exterior black (cam11),
            //required-asset FX pulled out of the persistent global zone. Iterating in AllEntities
            //order is deterministic; zones per level number in the dozens, so this costs nothing.
            foreach (InstancedEntity entity in zones)
                ApplyZoneLinks(entity, variablePinsOnly: false);
            foreach (InstancedEntity entity in zones)
                ApplyZoneLinks(entity, variablePinsOnly: true);
        }

        //This finds every EnvironmentMap entity and applies itself to any entities connected to it via the 'Entities' pin
        private void CalculateEnvironmentMaps()
        {
            ParallelOptions opts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

            Parallel.ForEach(AllEntities, opts, entity =>
            {
                entity.EnvironmentMap = null;
            });

            List<(InstancedEntity Entity, Textures.TEX4 Texture, int Priority)> maps = new List<(InstancedEntity, Textures.TEX4, int)>();
            foreach (InstancedEntity entity in AllEntities)
            {
                if (entity.Entity.variant != EntityVariant.FUNCTION)
                    continue;

                FunctionEntity function = (FunctionEntity)entity.Entity;
                if (!function.function.IsFunctionType || function.function.AsFunctionType != FunctionType.EnvironmentMap)
                    continue;
                if (entity.ThisCompositeInstance == null)
                    continue;

                string texturePath = entity.Strings.Get(ShortGuids.Texture);
                if (string.IsNullOrEmpty(texturePath))
                    continue;

                Textures.TEX4 tex = _level.Textures.GetEnvironmentMapByPath(texturePath);
                if (tex == null && _level.Global?.Textures != null)
                    tex = _level.Global.Textures.GetEnvironmentMapByPath(texturePath);
                if (tex == null)
                {
                    Console.WriteLine("WARNING: EnvironmentMap texture not found: " + texturePath);
                    continue;
                }

                int priority = entity.Integers.Has(ShortGuids.Priority) ? entity.Integers.Get(ShortGuids.Priority) : 100;
                maps.Add((entity, tex, priority));
            }

            maps.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            foreach (var map in maps)
                ApplyEnvironmentMapLinks(map.Entity, map.Texture, variablePinsOnly: false);
            foreach (var map in maps)
                ApplyEnvironmentMapLinks(map.Entity, map.Texture, variablePinsOnly: true);
        }

        private void ApplyEnvironmentMapLinks(InstancedEntity entity, Textures.TEX4 texture, bool variablePinsOnly)
        {
            FunctionEntity function = (FunctionEntity)entity.Entity;

            Dictionary<ShortGuid, InstancedEntity> siblings = null;
            InstancedEntity FindSibling(ShortGuid id)
            {
                if (siblings == null)
                {
                    List<InstancedEntity> list = entity.ThisCompositeInstance.Entities;
                    siblings = new Dictionary<ShortGuid, InstancedEntity>(list.Count);
                    foreach (InstancedEntity sibling in list)
                        siblings[sibling.Entity.shortGUID] = sibling;
                }
                siblings.TryGetValue(id, out InstancedEntity found);
                return found;
            }

            foreach (EntityConnector link in function.childLinks)
            {
                if (link.thisParamID != ShortGuids.Entities)
                    continue;

                InstancedEntity linkedEnt = FindSibling(link.linkedEntityID);
                if (linkedEnt?.Entity == null)
                    continue;

                bool isVariable = linkedEnt.Entity is VariableEntity;
                if (variablePinsOnly != isVariable)
                    continue;

                if (linkedEnt.Entity is TriggerSequence trig)
                {
                    foreach (TriggerSequence.SequenceEntry entry in trig.sequence)
                    {
                        InstancedEntity target = ResolvePathInComposite(entity.ThisCompositeInstance, entry.connectedEntity);
                        if (target == null)
                            continue;

                        AssignEnvironmentMap(target, texture);
                        AssignEnvironmentMapIntoChildComposite(target, texture, descendIntoShared: false);
                    }
                }
                else if (linkedEnt.Entity is VariableEntity varEnt)
                {
                    InstancedEntity hostPlacement = entity.ParentCompositeInstanceEntity;
                    InstancedComposite parentInst = entity.ParentCompositeInstance;
                    if (hostPlacement?.Entity == null || parentInst?.Entities == null)
                        continue;

                    ShortGuid pinName = varEnt.name;
                    ShortGuid pinGuid = varEnt.shortGUID;

                    foreach (EntityConnector plink in hostPlacement.Entity.childLinks)
                    {
                        if (plink.thisParamID != pinName && plink.thisParamID != pinGuid)
                            continue;

                        InstancedEntity target = null;
                        foreach (InstancedEntity sibling in parentInst.Entities)
                        {
                            if (sibling.Entity != null && sibling.Entity.shortGUID == plink.linkedEntityID)
                            {
                                target = sibling;
                                break;
                            }
                        }
                        if (target == null)
                            continue;

                        AssignEnvironmentMap(target, texture);
                        AssignEnvironmentMapIntoChildComposite(target, texture, descendIntoShared: true);
                    }
                }
                else
                {
                    AssignEnvironmentMap(linkedEnt, texture);
                    AssignEnvironmentMapIntoChildComposite(linkedEnt, texture, descendIntoShared: false);
                }
            }
        }

        private static void AssignEnvironmentMapIntoChildComposite(InstancedEntity entity, Textures.TEX4 texture, bool descendIntoShared = false)
        {
            if (entity?.ChildCompositeInstance == null)
                return;
            if (!descendIntoShared && entity.Bools.Get(ShortGuids.is_shared))
                return;
            AssignEnvironmentMap(entity.ChildCompositeInstance, texture, descendIntoShared);
        }

        private static void AssignEnvironmentMap(InstancedComposite composite, Textures.TEX4 texture, bool descendIntoShared = false)
        {
            if (composite?.Entities == null)
                return;

            foreach (InstancedEntity entity in composite.Entities)
            {
                AssignEnvironmentMap(entity, texture);
                if (entity?.ChildCompositeInstance == null)
                    continue;
                if (!descendIntoShared && entity.Bools.Get(ShortGuids.is_shared))
                    continue;
                AssignEnvironmentMap(entity.ChildCompositeInstance, texture, descendIntoShared);
            }
        }

        private static void AssignEnvironmentMap(InstancedEntity entity, Textures.TEX4 texture)
        {
            if (entity == null || texture == null)
                return;

            //Only apply to some types (todo - is this correct?)
            if (entity.Entity is FunctionEntity fe &&
                fe.function.IsFunctionType &&
                fe.function.AsFunctionType != FunctionType.ModelReference &&
                fe.function.AsFunctionType != FunctionType.SurfaceEffectBox &&
                fe.function.AsFunctionType != FunctionType.SurfaceEffectSphere)
                return;

            lock (entity)
            {
                if (entity.EnvironmentMap != null)
                    return;
                entity.EnvironmentMap = texture;
            }
        }

        private void ApplyZoneLinks(InstancedEntity entity, bool variablePinsOnly)
        {
            FunctionEntity function = (FunctionEntity)entity.Entity;
            ShortGuid zoneId = ResolveZoneId(entity);
            if (zoneId == ShortGuid.Invalid)
                return;

            Dictionary<ShortGuid, InstancedEntity> siblings = null;
            InstancedEntity FindSibling(ShortGuid id)
            {
                if (siblings == null)
                {
                    List<InstancedEntity> list = entity.ThisCompositeInstance.Entities;
                    siblings = new Dictionary<ShortGuid, InstancedEntity>(list.Count);
                    foreach (InstancedEntity sibling in list)
                        siblings[sibling.Entity.shortGUID] = sibling;
                }
                siblings.TryGetValue(id, out InstancedEntity found);
                return found;
            }

            ShortGuid zoneSourceInstance = entity.ThisCompositeInstance.InstanceID;

            foreach (EntityConnector link in function.childLinks)
            {
                if (link.thisParamID != ShortGuids.composites)
                    continue;

                InstancedEntity linkedEnt = FindSibling(link.linkedEntityID);
                if (linkedEnt?.Entity == null)
                    continue;

                bool isVariable = linkedEnt.Entity is VariableEntity;
                if (variablePinsOnly != isVariable)
                    continue;

                if (linkedEnt.Entity is TriggerSequence trig)
                {
                    foreach (TriggerSequence.SequenceEntry entry in trig.sequence)
                    {
                        InstancedEntity target = ResolvePathInComposite(entity.ThisCompositeInstance, entry.connectedEntity);
                        if (target == null)
                            continue;

                        AssignZone(target, zoneId, zoneSourceInstance, isDirect: true, descentRoot: ShortGuid.Invalid);
                        AssignZoneIntoChildComposite(target, zoneId, zoneSourceInstance, descendIntoShared: false);
                    }
                }
                else if (linkedEnt.Entity is VariableEntity varEnt)
                {
                    InstancedEntity hostPlacement = entity.ParentCompositeInstanceEntity;
                    InstancedComposite parentInst = entity.ParentCompositeInstance;
                    if (hostPlacement?.Entity == null || parentInst?.Entities == null)
                        continue;

                    ShortGuid pinName = varEnt.name;
                    ShortGuid pinGuid = varEnt.shortGUID;

                    foreach (EntityConnector plink in hostPlacement.Entity.childLinks)
                    {
                        if (plink.thisParamID != pinName && plink.thisParamID != pinGuid)
                            continue;

                        InstancedEntity target = null;
                        foreach (InstancedEntity sibling in parentInst.Entities)
                        {
                            if (sibling.Entity != null && sibling.Entity.shortGUID == plink.linkedEntityID)
                            {
                                target = sibling;
                                break;
                            }
                        }
                        if (target == null)
                            continue;

                        AssignZoneIfUnzoned(target, zoneId, zoneSourceInstance);
                        AssignZoneIntoChildCompositeIfUnzoned(target, zoneId, zoneSourceInstance, descendIntoShared: true);
                    }
                }
                else
                {
                    AssignZone(linkedEnt, zoneId, zoneSourceInstance, isDirect: true, descentRoot: ShortGuid.Invalid);
                    AssignZoneIntoChildComposite(linkedEnt, zoneId, zoneSourceInstance, descendIntoShared: false);
                }
            }
        }

        private static ShortGuid ResolveZoneId(InstancedEntity zoneEntity)
        {
            if (zoneEntity?.Entity == null || zoneEntity.ThisCompositeInstance == null)
                return ShortGuid.Invalid;
            uint instance = zoneEntity.ThisCompositeInstance.InstanceID.AsUInt32;
            uint entityId = zoneEntity.Entity.shortGUID.AsUInt32;
            if (instance == 0 && entityId == 0)
                return ShortGuid.Invalid;
            return new ShortGuid(instance + entityId + 1);
        }

        private static void AssignZoneIntoChildComposite(InstancedEntity entity, ShortGuid zoneId, ShortGuid zoneSourceInstance, bool descendIntoShared = false)
        {
            if (entity?.ChildCompositeInstance == null)
                return;
            if (!descendIntoShared && entity.Bools.Get(ShortGuids.is_shared))
                return;
            ShortGuid descentRoot = entity.ChildCompositeInstance.InstanceID;
            AssignZone(entity.ChildCompositeInstance, zoneId, zoneSourceInstance, descentRoot, descendIntoShared);
        }

        private static void AssignZoneIntoChildCompositeIfUnzoned(InstancedEntity entity, ShortGuid zoneId, ShortGuid zoneSourceInstance, bool descendIntoShared = true)
        {
            if (entity?.ChildCompositeInstance == null)
                return;
            if (!descendIntoShared && entity.Bools.Get(ShortGuids.is_shared))
                return;
            AssignZoneIfUnzoned(entity.ChildCompositeInstance, zoneId, zoneSourceInstance, descendIntoShared);
        }

        private static void AssignZone(InstancedComposite composite, ShortGuid zoneId, ShortGuid zoneSourceInstance, ShortGuid descentRoot, bool descendIntoShared = false)
        {
            if (composite?.Entities == null)
                return;

            foreach (InstancedEntity entity in composite.Entities)
            {
                AssignZone(entity, zoneId, zoneSourceInstance, isDirect: false, descentRoot: descentRoot);
                if (entity?.ChildCompositeInstance == null)
                    continue;
                if (!descendIntoShared && entity.Bools.Get(ShortGuids.is_shared))
                    continue;
                AssignZone(entity.ChildCompositeInstance, zoneId, zoneSourceInstance, descentRoot, descendIntoShared);
            }
        }

        private static void AssignZoneIfUnzoned(InstancedComposite composite, ShortGuid zoneId, ShortGuid zoneSourceInstance, bool descendIntoShared = true)
        {
            if (composite?.Entities == null)
                return;

            foreach (InstancedEntity entity in composite.Entities)
            {
                AssignZoneIfUnzoned(entity, zoneId, zoneSourceInstance);
                AssignZoneIntoChildCompositeIfUnzoned(entity, zoneId, zoneSourceInstance, descendIntoShared);
            }
        }

        private static void AssignZone(InstancedEntity entity, ShortGuid zoneId, ShortGuid zoneSourceInstance, bool isDirect, ShortGuid descentRoot)
        {
            //The logic here is a result of a lot of trial and error. I'm not sure it's correct really given its so convoluted, but it seems to hold up.

            if (entity == null || zoneId == ShortGuid.Invalid)
                return;

            lock (entity)
            {
                if (entity.PrimaryZone == zoneId || entity.SecondaryZone == zoneId)
                    return;

                if (entity.PrimaryZone == ShortGuid.Invalid)
                {
                    entity.PrimaryZone = zoneId;
                    entity.PrimaryZoneSourceInstance = zoneSourceInstance;
                    entity.PrimaryZoneWasDirect = isDirect;
                    entity.PrimaryZoneDescentRoot = isDirect ? ShortGuid.Invalid : descentRoot;
                    return;
                }

                if (!isDirect && !entity.PrimaryZoneWasDirect && descentRoot != ShortGuid.Invalid && entity.PrimaryZoneDescentRoot != ShortGuid.Invalid && descentRoot != entity.PrimaryZoneDescentRoot)
                {
                    if (IsCloserDescentRoot(entity, descentRoot, entity.PrimaryZoneDescentRoot))
                    {
                        entity.PrimaryZone = zoneId;
                        entity.PrimaryZoneSourceInstance = zoneSourceInstance;
                        entity.PrimaryZoneWasDirect = false;
                        entity.PrimaryZoneDescentRoot = descentRoot;
                        entity.SecondaryZone = ShortGuid.Invalid;
                        return;
                    }
                    if (IsCloserDescentRoot(entity, entity.PrimaryZoneDescentRoot, descentRoot))
                        return;
                }

                if (isDirect && !entity.PrimaryZoneWasDirect)
                {
                    ShortGuid demotedRoot = entity.PrimaryZoneDescentRoot;
                    if (entity.SecondaryZone == ShortGuid.Invalid && IsPeerPackageDescentRoot(entity, demotedRoot))
                    {
                        entity.SecondaryZone = zoneId;
                        return;
                    }

                    entity.PrimaryZone = zoneId;
                    entity.PrimaryZoneSourceInstance = zoneSourceInstance;
                    entity.PrimaryZoneWasDirect = true;
                    entity.PrimaryZoneDescentRoot = ShortGuid.Invalid;
                    entity.SecondaryZone = ShortGuid.Invalid;
                    return;
                }

                if (entity.SecondaryZone != ShortGuid.Invalid)
                {
                    if (isDirect && entity.PrimaryZoneWasDirect)
                        Console.WriteLine("WARNING: An entity tried to apply itself to more than two zones!");
                    return;
                }

                bool bothDirect = isDirect && entity.PrimaryZoneWasDirect;
                bool bothDescentSameRoot = !isDirect && !entity.PrimaryZoneWasDirect && descentRoot != ShortGuid.Invalid && descentRoot == entity.PrimaryZoneDescentRoot && zoneSourceInstance != entity.PrimaryZoneSourceInstance;
                bool descentOntoDirect = !isDirect && entity.PrimaryZoneWasDirect && IsPeerPackageDescentRoot(entity, descentRoot);
                bool siblingZoneSources = zoneSourceInstance != ShortGuid.Invalid && zoneSourceInstance == entity.PrimaryZoneSourceInstance && !(entity.PrimaryZoneWasDirect && !isDirect && !IsPeerPackageDescentRoot(entity, descentRoot));

                if (bothDirect || bothDescentSameRoot || descentOntoDirect || siblingZoneSources)
                    entity.SecondaryZone = zoneId;
            }
        }

        private static bool IsPeerPackageDescentRoot(InstancedEntity entity, ShortGuid descentRoot)
        {
            if (entity == null || descentRoot == ShortGuid.Invalid)
                return false;

            InstancedEntity walk = entity;
            for (int hop = 0; walk != null && hop < 3; hop++)
            {
                if (walk.ThisCompositeInstance != null &&
                    walk.ThisCompositeInstance.InstanceID == descentRoot)
                    return true;
                if (walk.ParentCompositeInstanceEntity?.ChildCompositeInstance != null &&
                    walk.ParentCompositeInstanceEntity.ChildCompositeInstance.InstanceID == descentRoot)
                    return true;
                walk = walk.ParentCompositeInstanceEntity;
            }
            return false;
        }

        private static bool IsCloserDescentRoot(InstancedEntity entity, ShortGuid candidateRoot, ShortGuid incumbentRoot)
        {
            if (entity == null || candidateRoot == ShortGuid.Invalid || incumbentRoot == ShortGuid.Invalid)
                return false;
            if (candidateRoot == incumbentRoot)
                return false;

            InstancedEntity walkEnt = entity;
            for (int guard = 0; walkEnt != null && guard < 64; guard++)
            {
                InstancedComposite comp = walkEnt.ThisCompositeInstance;
                if (comp != null)
                {
                    if (comp.InstanceID == candidateRoot)
                        return true;
                    if (comp.InstanceID == incumbentRoot)
                        return false;
                }
                walkEnt = walkEnt.ParentCompositeInstanceEntity;
            }
            return false;
        }

        private static void AssignZoneIfUnzoned(InstancedEntity entity, ShortGuid zoneId, ShortGuid zoneSourceInstance)
        {
            if (entity == null || zoneId == ShortGuid.Invalid)
                return;

            lock (entity)
            {
                if (entity.PrimaryZone != ShortGuid.Invalid)
                    return;
                entity.PrimaryZone = zoneId;
                entity.PrimaryZoneSourceInstance = zoneSourceInstance;
                entity.PrimaryZoneWasDirect = false;
                entity.PrimaryZoneDescentRoot = ShortGuid.Invalid;
            }
        }

        //Look up an entity in the current composite instance by following the path
        private static InstancedEntity ResolvePathInComposite(InstancedComposite start, EntityPath path)
        {
            if (start?.Entities == null || path?.path == null || path.path.Length == 0)
                return null;

            InstancedComposite current = start;
            InstancedEntity last = null;
            int lastIndex = path.path.Length - 1;
            if (lastIndex >= 0 && path.path[lastIndex] == ShortGuid.Invalid)
                lastIndex--;

            for (int i = 0; i <= lastIndex; i++)
            {
                ShortGuid step = path.path[i];
                if (step == ShortGuid.Invalid || current?.Entities == null)
                    return null;

                InstancedEntity next = null;
                foreach (InstancedEntity e in current.Entities)
                {
                    if (e.Entity.shortGUID == step)
                    {
                        next = e;
                        break;
                    }
                }
                if (next == null)
                    return null;

                last = next;
                if (i < lastIndex)
                {
                    if (next.ChildCompositeInstance == null)
                        return null;
                    current = next.ChildCompositeInstance;
                }
            }
            return last;
        }

        //Pristine MODEL_PARAMS per resource GUID, harvested before the mover list is discarded
        //and written back once the rebuilt movers exist. Environment renderable types only: a
        //LIGHT's first 16 constant bytes are DEFERRED_PARAMS and must never be touched.
        private System.Collections.Generic.Dictionary<ulong, byte[]> _retailModelParams = null;
        private System.Collections.Generic.Dictionary<ulong, byte[][]> _retailFxConstants = null;
        private System.Collections.Generic.Dictionary<ulong, bool> _retailRequiresScript = null;
        private System.Collections.Generic.Dictionary<ulong, (byte[] refs, int index)> _retailRuntimeRefs = null;
        private System.Collections.Generic.Dictionary<ulong, (ShortGuid primary, ShortGuid secondary)> _retailZones = null;
        private System.Collections.Generic.Dictionary<(uint inst, uint ent, uint res), (ShortGuid zone, CollisionMaps.CollisionFlags flags)> _retailCollisionZones = null;
        private System.Collections.Generic.Dictionary<ulong, int> _retailResourceIndex = null;

        private void SnapshotModelParams()
        {
            _retailModelParams = new System.Collections.Generic.Dictionary<ulong, byte[]>();
            _retailFxConstants = new System.Collections.Generic.Dictionary<ulong, byte[][]>();
            _retailRequiresScript = new System.Collections.Generic.Dictionary<ulong, bool>();
            _retailRuntimeRefs = new System.Collections.Generic.Dictionary<ulong, (byte[], int)>();
            _retailZones = new System.Collections.Generic.Dictionary<ulong, (ShortGuid, ShortGuid)>();

            // RESOURCES.BIN index assignment: a row's index is just its position in the runtime
            // list, and the rebuild hands out positions in instancing order - 16,891 of
            // ChallengeMap9's 18,468 pairs landed at a different index than retail even though the
            // pair SETS are identical. The engine resolves entities through this table at spawn
            // (PARTICLE_EMITTER_REFERENCE::on_initialise faulted when the table and MVR came from
            // different builds), so retail's assignment is restored before save.
            _retailResourceIndex = new System.Collections.Generic.Dictionary<ulong, int>();
            if (_level.Resources?.Entries != null)
                for (int ri = 0; ri < _level.Resources.Entries.Count; ri++)
                {
                    Resources.Resource r = _level.Resources.Entries[ri];
                    if (r == null) continue;
                    ulong rkey = ((ulong)r.composite_instance_id.AsUInt32 << 32) | r.resource_id.AsUInt32;
                    if (!_retailResourceIndex.ContainsKey(rkey))
                        _retailResourceIndex[rkey] = ri;
                }

            // Collision-row zones and flags: COLLISION.MAP feeds the engine's position->zone
            // lookup and collider state, and its rows take the same computed PrimaryZone the
            // movers do plus the ShouldApplyFrozen best-guess for the state bits. The measured
            // CM9 delta vs retail is ~550 rows differing ONLY in the state nibble (we add
            // FROZEN|PRE_FROZEN where retail leaves colliders live, and drop GHOSTED|PRE_GHOSTED
            // where retail ghosts them) - retail's own answer is carried per row.
            _retailCollisionZones = new System.Collections.Generic.Dictionary<(uint, uint, uint), (ShortGuid, CollisionMaps.CollisionFlags)>();
            if (_level.CollisionMaps?.Entries != null)
                foreach (CollisionMaps.COLLISION_MAPPING row in _level.CollisionMaps.Entries)
                {
                    if (row?.Entity == null)
                        continue;
                    var ckey = (row.Entity.composite_instance_id.AsUInt32, row.Entity.entity_id.AsUInt32, row.ResourceGUID.AsUInt32);
                    if (!_retailCollisionZones.ContainsKey(ckey))
                        _retailCollisionZones[ckey] = (row.ZoneID, row.Flags);
                }
            foreach (Movers.MOVER_DESCRIPTOR m in _level.Movers.Entries)
            {
                if (m.Resource == null || m.RenderableElements == null || m.RenderableElements.Count == 0)
                    continue;
                RenderableInstanceType type;
                try { type = m.GetRenderableType(); }
                catch { continue; }
                ulong key = ((ulong)m.Resource.composite_instance_id.AsUInt32 << 32) | m.Resource.resource_id.AsUInt32;

                // RequiresScript: the rule is undecoded (hardcoding either way disagrees with a
                // third of retail). Retail's own answer is carried per mover.
                if (m.Flags != null && !_retailRequiresScript.ContainsKey(key))
                    _retailRequiresScript[key] = m.Flags.RequiresScript;

                // The undecoded runtime words (RuntimeRefs at +256, RuntimeIndex at +280):
                // retail fills the refs on EVERY mover and hands ~40% a unique sequential
                // RuntimeIndex; we used to zero one and write a bogus mover index for the other.
                if (!_retailRuntimeRefs.ContainsKey(key))
                    _retailRuntimeRefs[key] = (m.RuntimeRefs, m.RuntimeIndex);

                // Zones: our AssignZone pass disagrees with retail's per-entity arrival order on
                // ~4% of ChallengeMap4's movers and 38% (!) of ChallengeMap9's - retail resolves
                // the same contested zone PAIR both ways depending on the entity, so the true rule
                // is per-entity and still undecoded. Zone membership drives streaming: CM9 rendered
                // rooms retail keeps unloaded and blacked out the vent exterior. Retail's own pair
                // is carried per mover; the computed pass still covers new content.
                if (!_retailZones.ContainsKey(key))
                    _retailZones[key] = (m.PrimaryZoneID, m.SecondaryZoneID);

                // FX movers: the whole constant pair is carried verbatim. Retail's fogsphere GPU
                // block is NOT the authored-parameter layout our generation writes (it opens with
                // a rotation matrix and carries packed fields) - the mismatch rendered CM9's
                // vented-gas floor fog invisible. Until that layout is decoded, retail's bytes
                // are strictly better for every unedited FX entity.
                if (type == RenderableInstanceType.FOGSPHERE ||
                    type == RenderableInstanceType.DYNAMICFX ||
                    type == RenderableInstanceType.DYNAMICFX_UNIQUE_MAT)
                {
                    if (!_retailFxConstants.ContainsKey(key))
                        _retailFxConstants[key] = new byte[][]
                        {
                            m.GPUConstants?.RawBytes,
                            m.RenderConstants?.RawBytes
                        };
                    continue;
                }

                if (type != RenderableInstanceType.ENVIRONMENT &&
                    type != RenderableInstanceType.ENVIRONMENT_EXTRA &&
                    type != RenderableInstanceType.MISC)
                    continue;
                byte[] raw = m.RenderConstants?.RawBytes;
                if (raw == null || raw.Length < 16)
                    continue;
                if (_retailModelParams.ContainsKey(key))
                    continue;
                byte[] copy = new byte[16];
                Array.Copy(raw, copy, 16);
                _retailModelParams[key] = copy;
            }
        }

        private void CarryRetailModelParams()
        {
            if (_retailModelParams == null || _retailModelParams.Count == 0)
                return;
            int carried = 0;
            foreach (Movers.MOVER_DESCRIPTOR m in _level.Movers.Entries)
            {
                if (m.Resource == null || m.RenderConstants == null)
                    continue;
                RenderableInstanceType type;
                try { type = m.GetRenderableType(); }
                catch { continue; }
                if (type != RenderableInstanceType.ENVIRONMENT &&
                    type != RenderableInstanceType.ENVIRONMENT_EXTRA &&
                    type != RenderableInstanceType.MISC)
                    continue;
                ulong key = ((ulong)m.Resource.composite_instance_id.AsUInt32 << 32) | m.Resource.resource_id.AsUInt32;
                if (!_retailModelParams.TryGetValue(key, out byte[] pristine))
                    continue;
                byte[] raw = m.RenderConstants.RawBytes;
                if (raw == null || raw.Length < 16)
                    continue;
                bool differs = false;
                for (int b = 0; b < 16; b++)
                    if (raw[b] != pristine[b]) { differs = true; break; }
                if (!differs)
                    continue;
                Array.Copy(pristine, raw, 16);
                m.RenderConstants.SetRawBytes(raw);
                carried++;
            }
            if (carried > 0)
                Console.WriteLine("Instancing: carried MODEL_PARAMS lightmap transforms for " + carried + " movers");

            // FX constant carry (see SnapshotModelParams): pristine GPU + render constants
            // verbatim for matched fogsphere/FX movers.
            if (_retailFxConstants != null && _retailFxConstants.Count > 0)
            {
                int fxCarried = 0;
                foreach (Movers.MOVER_DESCRIPTOR m in _level.Movers.Entries)
                {
                    if (m.Resource == null)
                        continue;
                    RenderableInstanceType type;
                    try { type = m.GetRenderableType(); }
                    catch { continue; }
                    if (type != RenderableInstanceType.FOGSPHERE &&
                        type != RenderableInstanceType.DYNAMICFX &&
                        type != RenderableInstanceType.DYNAMICFX_UNIQUE_MAT)
                        continue;
                    ulong key = ((ulong)m.Resource.composite_instance_id.AsUInt32 << 32) | m.Resource.resource_id.AsUInt32;
                    if (!_retailFxConstants.TryGetValue(key, out byte[][] pristine))
                        continue;
                    bool touched = false;
                    if (pristine[0] != null && m.GPUConstants != null)
                    {
                        m.GPUConstants.SetRawBytes(pristine[0]);
                        touched = true;
                    }
                    if (pristine[1] != null && m.RenderConstants != null)
                    {
                        m.RenderConstants.SetRawBytes(pristine[1]);
                        touched = true;
                    }
                    if (touched) fxCarried++;
                }
                if (fxCarried > 0)
                    Console.WriteLine("Instancing: carried FX constants for " + fxCarried + " movers");
            }

            // RequiresScript carry (see SnapshotModelParams): retail's per-mover answer for the
            // undecoded rule, all types.
            if (_retailRequiresScript != null && _retailRequiresScript.Count > 0)
            {
                int flagsCarried = 0;
                foreach (Movers.MOVER_DESCRIPTOR m in _level.Movers.Entries)
                {
                    if (m.Resource == null || m.Flags == null)
                        continue;
                    ulong key = ((ulong)m.Resource.composite_instance_id.AsUInt32 << 32) | m.Resource.resource_id.AsUInt32;
                    if (!_retailRequiresScript.TryGetValue(key, out bool pristine))
                        continue;
                    if (m.Flags.RequiresScript != pristine)
                    {
                        m.Flags.RequiresScript = pristine;
                        flagsCarried++;
                    }
                }
                if (flagsCarried > 0)
                    Console.WriteLine("Instancing: carried RequiresScript for " + flagsCarried + " movers");
            }

            // Runtime words carry (see SnapshotModelParams), all types.
            if (_retailRuntimeRefs != null && _retailRuntimeRefs.Count > 0)
            {
                int refsCarried = 0;
                foreach (Movers.MOVER_DESCRIPTOR m in _level.Movers.Entries)
                {
                    if (m.Resource == null)
                        continue;
                    ulong key = ((ulong)m.Resource.composite_instance_id.AsUInt32 << 32) | m.Resource.resource_id.AsUInt32;
                    if (!_retailRuntimeRefs.TryGetValue(key, out (byte[] refs, int index) pristine))
                        continue;
                    m.RuntimeRefs = pristine.refs;
                    m.RuntimeIndex = pristine.index;
                    refsCarried++;
                }
                if (refsCarried > 0)
                    Console.WriteLine("Instancing: carried runtime reference words for " + refsCarried + " movers");
            }

            // Zone carry (see SnapshotModelParams): retail's primary/secondary pair, all types.
            if (_retailZones != null && _retailZones.Count > 0)
            {
                int zonesCarried = 0;
                foreach (Movers.MOVER_DESCRIPTOR m in _level.Movers.Entries)
                {
                    if (m.Resource == null)
                        continue;
                    ulong key = ((ulong)m.Resource.composite_instance_id.AsUInt32 << 32) | m.Resource.resource_id.AsUInt32;
                    if (!_retailZones.TryGetValue(key, out (ShortGuid primary, ShortGuid secondary) pristine))
                        continue;
                    if (m.PrimaryZoneID != pristine.primary || m.SecondaryZoneID != pristine.secondary)
                    {
                        m.PrimaryZoneID = pristine.primary;
                        m.SecondaryZoneID = pristine.secondary;
                        zonesCarried++;
                    }
                }
                if (zonesCarried > 0)
                    Console.WriteLine("Instancing: carried zones for " + zonesCarried + " movers");
            }

            // Collision-row zone + state-flag carry (see SnapshotModelParams).
            if (_retailCollisionZones != null && _retailCollisionZones.Count > 0 && _level.CollisionMaps?.Entries != null)
            {
                int colZonesCarried = 0, colFlagsCarried = 0;
                foreach (CollisionMaps.COLLISION_MAPPING row in _level.CollisionMaps.Entries)
                {
                    if (row?.Entity == null)
                        continue;
                    var ckey = (row.Entity.composite_instance_id.AsUInt32, row.Entity.entity_id.AsUInt32, row.ResourceGUID.AsUInt32);
                    if (!_retailCollisionZones.TryGetValue(ckey, out (ShortGuid zone, CollisionMaps.CollisionFlags flags) pristine))
                        continue;
                    if (row.ZoneID != pristine.zone)
                    {
                        row.ZoneID = pristine.zone;
                        colZonesCarried++;
                    }
                    // State byte only (GHOSTED/PRE_GHOSTED/FROZEN/PRE_FROZEN/REMOVED/...): the low
                    // bits pick the Havok host at build time, so they must stay what the instance
                    // was actually built with.
                    CollisionMaps.CollisionFlags carriedFlags =
                        (row.Flags & (CollisionMaps.CollisionFlags)0x00FFFFFF) |
                        (pristine.flags & (CollisionMaps.CollisionFlags)0xFF000000);
                    if (row.Flags != carriedFlags)
                    {
                        row.Flags = carriedFlags;
                        colFlagsCarried++;
                    }
                }
                if (colZonesCarried > 0)
                    Console.WriteLine("Instancing: carried collision-row zones for " + colZonesCarried + " rows");
                if (colFlagsCarried > 0)
                    Console.WriteLine("Instancing: carried collision-row flags for " + colFlagsCarried + " rows");
            }

            // Resource-table order restore (see SnapshotModelParams): retail pairs return to
            // retail's index; new pairs fill the gaps in creation order.
            if (_retailResourceIndex != null && _retailResourceIndex.Count > 0 && _level.Resources?.Entries != null)
            {
                List<Resources.Resource> entries = _level.Resources.Entries;
                var slots = new Resources.Resource[Math.Max(entries.Count, _retailResourceIndex.Count)];
                var leftovers = new List<Resources.Resource>();
                int matched = 0;
                foreach (Resources.Resource r in entries)
                {
                    if (r == null) continue;
                    ulong rkey = ((ulong)r.composite_instance_id.AsUInt32 << 32) | r.resource_id.AsUInt32;
                    if (_retailResourceIndex.TryGetValue(rkey, out int idx) && slots[idx] == null)
                    {
                        slots[idx] = r;
                        matched++;
                    }
                    else
                        leftovers.Add(r);
                }
                var reordered = new List<Resources.Resource>(entries.Count);
                int li = 0;
                for (int i = 0; i < slots.Length && reordered.Count < entries.Count; i++)
                {
                    if (slots[i] != null) reordered.Add(slots[i]);
                    else if (li < leftovers.Count) reordered.Add(leftovers[li++]);
                }
                while (li < leftovers.Count) reordered.Add(leftovers[li++]);
                _level.Resources.Entries = reordered;
                Console.WriteLine("Instancing: restored retail resource-table order (" + matched + " of " + reordered.Count + " at retail index)");
            }
        }

        private void AddMover(InstancedEntity entity, Movers.MOVER_DESCRIPTOR mvr, bool isTemplate = false)
        {
            if (mvr == null || mvr.RenderableElements == null || mvr.RenderableElements.Count == 0)
                return;

            if (entity != null)
            {
                ApplyMoverFlags(mvr, entity, isTemplate);
                entity.Mover = mvr;
            }

            lock (_mvrLock)
            {
                //Retail's tool gives every instanced FX mover its OWN renderable entry - value
                //duplicates of the composite resource's run, one per placed instance (measured on
                //ChallengeMap9: fogspheres 6226/6235/6244..., particle emitters 6230/6239...).
                //Sharing the resource's single entry across all instances is the wrong shape, and
                //the fogsphere gas carpet never rendered under it.
                RenderableInstanceType renderType = RenderableInstanceType.MISC;
                try { renderType = mvr.GetRenderableType(); } catch { }
                if (!isTemplate && (renderType == RenderableInstanceType.FOGSPHERE ||
                                    renderType == RenderableInstanceType.DYNAMICFX ||
                                    renderType == RenderableInstanceType.DYNAMICFX_UNIQUE_MAT))
                    mvr.RenderableElements = _level.RenderableElements.RegisterDuplicateRun(mvr.RenderableElements);
                else
                    mvr.RenderableElements = _level.RenderableElements.EnsureRegistered(mvr.RenderableElements);
                _level.Movers.Entries.Add(mvr);
            }
        }

        //Utility to get the correct zone ID for a collision map entry
        private ShortGuid ResolveCollisionZoneId(InstancedEntity entity)
        {
            if (entity.PrimaryZone != ShortGuid.Invalid && entity.SecondaryZone != ShortGuid.Invalid && entity.PrimaryZone != entity.SecondaryZone)
                return ShortGuid.Invalid;

            if (entity.PrimaryZone == ShortGuid.Invalid)
                return GlobalZoneId;

            return entity.PrimaryZone;
        }

        //Utility to get correct zone for Mover entry
        private void ApplyMoverZones(Movers.MOVER_DESCRIPTOR mvr, InstancedEntity entity)
        {
            ShortGuid primary = entity.PrimaryZone;
            ShortGuid secondary = entity.SecondaryZone;
            if (primary == ShortGuid.Invalid)
                primary = GlobalZoneId;
            if (SwapTwoZoneMoverOrder && primary != ShortGuid.Invalid && secondary != ShortGuid.Invalid)
            {
                ShortGuid swap = primary;
                primary = secondary;
                secondary = swap;
            }
            mvr.PrimaryZoneID = primary;
            mvr.SecondaryZoneID = secondary;
        }

        //Attempt to calculate Stationary / Visible / RequiresScript for MVR
        private static void ApplyMoverFlags(Movers.MOVER_DESCRIPTOR mvr, InstancedEntity entity, bool isTemplate)
        {
            if (mvr?.Flags == null || entity == null)
                return;

            RenderableInstanceType renderType = RenderableInstanceType.MISC;
            try
            {
                if (mvr.RenderableElements != null && mvr.RenderableElements.Count > 0)
                    renderType = mvr.GetRenderableType();
            }
            catch { /* keep MISC */ }

            mvr.Flags.Stationary = renderType != RenderableInstanceType.DYNAMICFX && renderType != RenderableInstanceType.DYNAMICFX_UNIQUE_MAT;

            bool visible = entity.Bools.Get(ShortGuids.show_on_reset) && !isTemplate;
            if (visible && IsHiddenByAncestorTemplate(entity))
                visible = false;
            if (visible && IsRequiredAssetsContent(entity) && !IsRequiredAssetsVisibleException(renderType))
                visible = false;
            if (visible && IsPhysicsOrTemplatePath(entity))
                visible = false;
            if (visible && AncestorHasDisableDisplay(entity))
                visible = false;
            if (visible && !PassesLightOnGate(entity))
                visible = false;
            if (visible && IsAnimationAnchorEntity(entity))
                visible = false;
            mvr.Flags.Visible = visible;

            //Defaulting to true - the real rule is undecoded, and retail disagrees on a third of
            //ChallengeMap4's movers. Instancing.MoverRequiresScript flips it for testing.
            mvr.Flags.RequiresScript = MoverRequiresScript;
        }

        private static bool IsHiddenByAncestorTemplate(InstancedEntity entity)
        {
            for (InstancedEntity walk = entity; walk != null; walk = walk.ParentCompositeInstanceEntity)
            {
                if (walk.Bools.Has(ShortGuids.is_template) && walk.Bools.Get(ShortGuids.is_template))
                    return true;
            }
            return false;
        }

        private static bool IsRequiredAssetsVisibleException(RenderableInstanceType renderType)
        {
            return renderType == RenderableInstanceType.DYNAMICFX
                || renderType == RenderableInstanceType.DYNAMICFX_UNIQUE_MAT
                || renderType == RenderableInstanceType.MISC;
        }

        private static bool IsRequiredAssetsContent(InstancedEntity entity)
        {
            for (InstancedEntity walk = entity; walk != null; walk = walk.ParentCompositeInstanceEntity)
            {
                string name = walk.Composite?.name;
                if (string.IsNullOrEmpty(name))
                    continue;
                if (name.Replace('\\', '/').IndexOf("REQUIRED_ASSETS", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool IsPhysicsOrTemplatePath(InstancedEntity entity)
        {
            for (InstancedEntity walk = entity; walk != null; walk = walk.ParentCompositeInstanceEntity)
            {
                string name = walk.Composite?.name;
                if (string.IsNullOrEmpty(name))
                    continue;
                string path = name.Replace('\\', '/').ToUpperInvariant();
                if (path.Contains("/PHYSICS/") || path.Contains("TEMPLATE"))
                    return true;
            }
            return false;
        }

        private static bool AncestorHasDisableDisplay(InstancedEntity entity)
        {
            for (InstancedEntity walk = entity?.ParentCompositeInstanceEntity; walk != null; walk = walk.ParentCompositeInstanceEntity)
            {
                if (walk.Bools.Has(ShortGuids.disable_display) && walk.Bools.Get(ShortGuids.disable_display))
                    return true;
            }
            return false;
        }

        private static bool PassesLightOnGate(InstancedEntity entity)
        {
            if (!(entity?.Entity is FunctionEntity fe) || fe.function != FunctionType.LightReference)
                return true;

            if (entity.Bools.Has(ShortGuids.light_on_reset) && !entity.Bools.Get(ShortGuids.light_on_reset))
                return false;

            return true;
        }

        // Reads an ubershader's named PARAMETER off the entity, in the shape the material's constant
        // slots want it. A parameter the entity does not itself supply returns null so the material
        // being replaced keeps its own value - a fog box declares the DEPTH_INTERSECT colours but
        // only authors them when that feature is on, and retail leaves the material's alone.
        private static MaterialFactory.ParameterLookup EntityShaderParameters(InstancedEntity entity, SHADER_LIST ubershader)
        {
            return (name, width) =>
            {
                if (MaterialFactory.NotBakedIntoMaterial(ubershader, name))
                    return null;
                ShortGuid guid = ShortGuidUtils.Generate(name);
                bool authored = entity.Entity?.GetParameter(guid) != null ||
                                (entity.Floats.Links?.ContainsKey(guid) ?? false) ||
                                (entity.Integers.Links?.ContainsKey(guid) ?? false) ||
                                (entity.Vectors.Links?.ContainsKey(guid) ?? false);
                if (!authored)
                    return null;

                if (width == 1)
                {
                    //An integer parameter (DRAW_PASS, PARTICLE_COUNT) lives in its own table.
                    float scalar = entity.Floats.Values.ContainsKey(guid) || (entity.Floats.Links?.ContainsKey(guid) ?? false)
                        ? entity.Floats.Get(guid) : entity.Integers.Get(guid);
                    return new[] { MaterialFactory.ConvertParameter(ubershader, name, scalar) };
                }

                Vector3 raw = entity.Vectors.Get(guid);
                if (MaterialFactory.TreatAsUnauthored(ubershader, name, new[] { raw.X, raw.Y, raw.Z }))
                    return null;
                Vector3 vector = raw * MaterialFactory.VectorScale(ubershader, name);
                float[] result = new float[width];
                if (width > 0) result[0] = vector.X;
                if (width > 1) result[1] = vector.Y;
                if (width > 2) result[2] = vector.Z;
                if (width > 3) result[3] = 1.0f;
                return result;
            };
        }

        // Give a volume the material its own parameters call for. Its shader's feature mask IS those
        // parameters, and its constants are the ubershader's named parameters - regenerating every
        // ChallengeMap4 volume from its entity reproduces retail's material exactly: 662 of 662 fog
        // spheres, 24 of 24 fog boxes, 2 of 2 surface effect boxes. Returns the run unchanged when
        // nothing needs to change or when the level has no shader for the combination asked for.
        private List<RenderableElements.Element> ApplyShaderFeatureMaterial(List<RenderableElements.Element> reds, InstancedEntity entity, long features)
        {
            if (_materialFactory == null || reds == null || reds.Count != 1 || reds[0]?.Material?.Shader == null)
                return reds;

            SHADER_LIST ubershader = reds[0].Material.Shader.Ubershader;
            string prefix = ubershader == SHADER_LIST.CA_FOGSPHERE ? "FOGSPHERE_"
                          : ubershader == SHADER_LIST.CA_FOGPLANE ? "FOGBOX_"
                          : ubershader == SHADER_LIST.CA_EFFECT_OVERLAY ? "SURFACE_EFFECT_" : null;
            Materials.Material material = _materialFactory.GetShaderFeatureMaterial(
                reds[0].Material, features, prefix, EntityShaderParameters(entity, ubershader), DescribeForLog(entity));
            return material == null ? reds : _materialFactory.ApplyMaterial(reds, material);
        }

        // Give an FX emitter the material its own parameters call for. The shader stays the one the
        // composite authored - nothing here computes CA_PARTICLE features - but the constants are
        // rebuilt, because they ARE the emitter's parameters and retail bakes each instance's own
        // values into a material of its own. Regenerating every ChallengeMap4 emitter this way
        // reproduces retail's material exactly on 585 of 588 particle movers and 59 of 59 ribbons;
        // the three that differ have a PARTICLE_COUNT retail lowered (60 -> 8, 20 -> 7, 34 -> 1).
        //
        // An emitter with unique_material set gets a material nobody else shares, which is what the
        // offline flags value of 1 marks (Utilities.CalculateRenderableType reads it as
        // DYNAMICFX_UNIQUE_MAT) - so those are never deduplicated against an existing entry.
        private List<RenderableElements.Element> ApplyFxMaterial(List<RenderableElements.Element> reds, InstancedEntity entity)
        {
            if (_materialFactory == null || reds == null || reds.Count != 1 || reds[0]?.Material?.Shader == null)
                return reds;

            Materials.Material template = reds[0].Material;
            bool unique = entity.Bools.Get(ShortGuids.unique_material);
            string name = null;
            if (unique)
            {
                //Retail's shape is {material guid}_{per emitter}_{per instance}; keep everything up
                //to the last group and put this instance's id in its place.
                string stem = template.Name ?? "FX";
                int lastUnderscore = stem.LastIndexOf('_');
                if (lastUnderscore > 32) stem = stem.Substring(0, lastUnderscore);
                name = stem + "_" + (entity.ThisCompositeInstance?.InstanceID.AsUInt32 ?? 0).ToString("X8");

                //When the level already ships a material with this exact name, THIS instance is the
                //one it was authored for - use it rather than regenerating. Regenerating collided
                //with the shipped name (ClaimName suffixed it "[000000]") and detached the mover's
                //REDS run from the material the Commands-side resource still points at; the engine
                //then never drew the emitter. ChallengeMap9's poison-gas carpet was the proof: its
                //renderable chain was byte-identical to retail EXCEPT the mover's material was the
                //"[000000]" copy, and the gas simply did not render. 547 materials per level were
                //duplicated this way before the lookup.
                if (name == template.Name)
                    return reds;
                Materials.Material shipped = _materialFactory.FindByName(name);
                if (shipped != null)
                    return _materialFactory.ApplyMaterial(reds, shipped);
            }

            //The offline dword is cleared, never set. Retail ships 0 on EVERY emitter mover of
            //ChallengeMap4, BSP_Torrens, Sci_Hub and Tech_RnD_HzdLab - 56 distinct
            //(retail, authored, unique_material, sharing, CPU) groups and not one with a 1 - while
            //six ChallengeMap4 ribbons have a composite material that carries 1 and a shipped one
            //that does not. So the 1 lives only on an authored material and means "this needs its
            //own copy": it is an instruction to the build, spent once the copy exists, not a
            //property of the copy. Writing it onto the instance instead was tried and is wrong.
            Materials.Material material = _materialFactory.GetShaderFeatureMaterial(
                template, template.Shader.UbershaderFeatureFlags, null,
                EntityShaderParameters(entity, template.Shader.Ubershader), DescribeForLog(entity), !unique, name,
                clearOfflineFlags: true);
            return material == null ? reds : _materialFactory.ApplyMaterial(reds, material);
        }

        //"composite / entity name" for a warning line, best-effort - never worth throwing over.
        private static string DescribeForLog(InstancedEntity entity)
        {
            if (entity == null)
                return "";
            string name = null;
            try { name = entity.Level?.Commands?.Utils?.GetEntityName(entity.Composite, entity.Entity); }
            catch { }
            return (entity.Composite?.name ?? "?") + " / " + (string.IsNullOrEmpty(name) ? entity.Entity?.shortGUID.ToByteString() : name);
        }

        private static bool IsAnimationAnchorEntity(InstancedEntity entity)
        {
            string name = null;
            try { name = entity.Level?.Commands?.Utils?.GetEntityName(entity.Composite, entity.Entity); }
            catch { }
            if (string.IsNullOrEmpty(name))
                return false;
            return name.IndexOf("Animation_Anchor", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        //TEMP TEST CODE - remove this eventually - need to figure out how frozen actually works. this just goes for the most popular option.
        private bool _applyDefaultFrozen = true;
        public bool ApplyDefaultFrozen => _applyDefaultFrozen;
        public static bool ShouldApplyFrozen(IList<CollisionMaps.COLLISION_MAPPING> retailRows)
        {
            int nonGhost = 0, nonGhostFrozen = 0;
            if (retailRows == null)
                return true;
            foreach (CollisionMaps.COLLISION_MAPPING row in retailRows)
            {
                if (row?.Entity == null || row.Entity.composite_instance_id.AsUInt32 == 0)
                    continue;
                uint f = (uint)row.Flags;
                if ((f & (uint)CollisionMaps.CollisionFlags.PREBUILT) == 0)
                    continue;
                if ((f & (uint)CollisionMaps.CollisionFlags.FIXED) == 0)
                    continue;
                if ((f & (uint)CollisionMaps.CollisionFlags.GHOSTED) != 0)
                    continue;
                nonGhost++;
                if ((f & (uint)CollisionMaps.CollisionFlags.FROZEN) != 0)
                    nonGhostFrozen++;
            }
            if (nonGhost < 10)
                return true;
            return nonGhostFrozen * 2 >= nonGhost;
        }

        //Calculate collision flags 
        private CollisionMaps.CollisionFlags BuildInstanceCollisionFlags(InstancedEntity entity, bool deleteBallistic, bool forceGhosted, Materials.Material material = null, bool? applyDefaultFrozenOverride = null)
        {
            CollisionMaps.CollisionFlags flags = CollisionMaps.CollisionFlags.FIXED | CollisionMaps.CollisionFlags.PREBUILT;

            if (deleteBallistic)
                flags |= CollisionMaps.CollisionFlags.STANDARD_ONLY;

            bool enable = !entity.Bools.Has(ShortGuids.enable_on_reset) || entity.Bools.Get(ShortGuids.enable_on_reset);
            bool ghosted = forceGhosted || !enable;
            if (ghosted)
            {
                flags |= CollisionMaps.CollisionFlags.GHOSTED;
                flags |= CollisionMaps.CollisionFlags.PRE_GHOSTED;
            }

            bool applyDefaultFrozen = applyDefaultFrozenOverride ?? _applyDefaultFrozen;
            if (ghosted || applyDefaultFrozen)
            {
                flags |= CollisionMaps.CollisionFlags.FROZEN;
                flags |= CollisionMaps.CollisionFlags.PRE_FROZEN;
            }

            if (entity.Bools.Has(ShortGuids.report_sliding) && entity.Bools.Get(ShortGuids.report_sliding))
                flags |= CollisionMaps.CollisionFlags.REPORT_SLIDING;
            if (entity.Bools.Has(ShortGuids.soft_collision) && entity.Bools.Get(ShortGuids.soft_collision))
                flags |= CollisionMaps.CollisionFlags.SOFT_COLLISION;
            if (entity.Bools.Has(ShortGuids.force_keyframed) && entity.Bools.Get(ShortGuids.force_keyframed))
                flags |= CollisionMaps.CollisionFlags.FORCE_KEYFRAMED;
            if (entity.Bools.Has(ShortGuids.force_transparent) && entity.Bools.Get(ShortGuids.force_transparent))
                flags |= CollisionMaps.CollisionFlags.FORCE_TRANSPARENT;

            //This is a bit of a bodge because I can't get any useful metadata from these materials, so just using their name for now.
            if (material?.Name == "Collision->Collision" ||
                material?.Name == "AudioCollision->AudioCollision" ||
                material?.Name == "WindowCollision->WindowCollision" ||
                material?.Name == "COLLISION_ONLY")
            {
                flags |= CollisionMaps.CollisionFlags.WORLD;
                if (material?.Name == "WindowCollision->WindowCollision")
                    flags |= (CollisionMaps.CollisionFlags)CollisionMaps.CollisionType.TRANSPARENT;
                else if (material?.Name == "AudioCollision->AudioCollision")
                    flags |= (CollisionMaps.CollisionFlags)CollisionMaps.CollisionType.SOUND;
                else
                    flags |= (CollisionMaps.CollisionFlags)CollisionMaps.CollisionType.STANDARD;
            }
            else if (material?.Name != null && material.Name.Length != 0)
            {
                flags |= CollisionMaps.CollisionFlags.BALLISTIC;
                flags |= (CollisionMaps.CollisionFlags)CollisionMaps.CollisionType.BALLISTICS;
            }

            return flags;
        }

        private void GenerateInstances(Composite composite, EntityPath path, InstancedComposite compositeInstance, InstancedComposite parentCompositeInstance, InstancedEntity parentCompositeInstanceEntity, List<InstancedAlias> aliases, bool underShared, List<ShortGuid> sharedRelativePath)
        {
            //todo - when this logic is more complete, i need to add a whitelist which means that unused entity and parameter types are ignored to save on memory overhead

            List<InstancedAlias> localAliases = new List<InstancedAlias>(aliases);

            //First, create all 'instanced entity' objects - these populate their default bool values on creation
            var entities = composite.GetEntities();
            compositeInstance.Entities = new List<InstancedEntity>(entities.Count);
            Dictionary<ShortGuid, InstancedEntity> entityByGuid = new Dictionary<ShortGuid, InstancedEntity>(entities.Count);
            var entityArray = entities.ToArray();
            var instances = new InstancedEntity[entityArray.Length];
            var aliasList = new List<InstancedAlias>();
            Parallel.For(0, entityArray.Length, i =>
            {
                Entity entity = entityArray[i];
                EntityPath pathToThisEntity = path.Copy();
                pathToThisEntity.AddNextStep(entity);

                InstancedEntity newInstance = new InstancedEntity(_level, composite, entity, pathToThisEntity, _parameterCache, _entityLookupCache);
                newInstance.ParentCompositeInstanceEntity = parentCompositeInstanceEntity;
                newInstance.ParentCompositeInstance = parentCompositeInstance;
                newInstance.ThisCompositeInstance = compositeInstance;
                instances[i] = newInstance;

                //Keep track of aliases
                if (entity.variant == EntityVariant.ALIAS)
                {
                    lock (aliasList)
                    {
                        InstancedAlias alias = new InstancedAlias() { ActivePath = ((AliasEntity)entity).alias.path.ToList(), InstancedInfo = newInstance };
                        aliasList.Add(alias);
                    }
                }
            });

            //Add instances to collections
            for (int i = 0; i < instances.Length; i++)
            {
                compositeInstance.Entities.Add(instances[i]);
                entityByGuid[entityArray[i].shortGUID] = instances[i];
            }
            localAliases.InsertRange(0, aliasList);

            //Next, hook up the instanced entity links as references
            Parallel.ForEach(compositeInstance.Entities, entity =>
            {
                entity.PopulateLinks(compositeInstance.Entities, entityByGuid);
            });

            //Now, split all the aliases up by the first part of their path so that we can apply them
            Dictionary<ShortGuid, List<InstancedAlias>> trackedAliases = new Dictionary<ShortGuid, List<InstancedAlias>>();
            foreach (InstancedAlias alias in localAliases)
            {
                if (alias.ActivePath.Count == 0)
                    continue;

                ShortGuid currentStep = alias.ActivePath[0];
                alias.ActivePath.RemoveAt(0);

                if (alias.ActivePath.Count == 0 || alias.ActivePath[0] == ShortGuid.Invalid)
                {
                    //We've arrived at the entity within this composite, apply the data out
                    if (entityByGuid.TryGetValue(currentStep, out InstancedEntity toApply))
                    {
                        toApply.ApplyAlias(alias);
                    }
                }
                else
                {
                    //Otherwise, just keep a track of the alias with its newly updated path to use further down
                    if (!trackedAliases.TryGetValue(currentStep, out List<InstancedAlias> aliasList2))
                    {
                        aliasList2 = new List<InstancedAlias>();
                        trackedAliases[currentStep] = aliasList2;
                    }
                    aliasList2.Add(alias);
                }
            }

            foreach (var entity in compositeInstance.Entities)
            {
                AllEntities.Add(entity);
            }
            AllComposites.Add(compositeInstance);

            //Now, traverse down in to any child composites, and rinse and repeat
            List<(FunctionEntity function, Composite child, List<InstancedAlias> childAliases, EntityPath newPath, InstancedEntity instancedEnt, bool childUnderShared, List<ShortGuid> childSharedPath)> childComposites = new List<(FunctionEntity, Composite, List<InstancedAlias>, EntityPath, InstancedEntity, bool, List<ShortGuid>)>();
            foreach (FunctionEntity function in composite.functions)
            {
                if (function.function.IsFunctionType)
                    continue;

                Composite child = _level.Commands.GetComposite(function.function);
                if (child == null)
                    continue;

                if (!trackedAliases.TryGetValue(function.shortGUID, out List<InstancedAlias> childAliases))
                    childAliases = new List<InstancedAlias>();

                EntityPath newPath = path.Copy();
                newPath.AddNextStep(function);

                if (!entityByGuid.TryGetValue(function.shortGUID, out InstancedEntity instancedEnt))
                    continue;

                //Once an is_shared entity is hit, instance IDs are re-rooted at that entity
                bool thisIsShared = instancedEnt.Bools.Get(ShortGuids.is_shared);
                bool childUnderShared;
                List<ShortGuid> childSharedPath;
                if (thisIsShared)
                {
                    childUnderShared = true;
                    childSharedPath = new List<ShortGuid> { function.shortGUID };
                }
                else if (underShared)
                {
                    childUnderShared = true;
                    childSharedPath = new List<ShortGuid>(sharedRelativePath);
                    childSharedPath.Add(function.shortGUID);
                }
                else
                {
                    childUnderShared = false;
                    childSharedPath = null;
                }

                InstancedComposite newInstance = new InstancedComposite();
                if (childUnderShared)
                    newInstance.InstanceID = childSharedPath.GenerateCompositeInstanceID(false, ShortGuid.Invalid);
                else
                    newInstance.InstanceID = newPath.GenerateCompositeInstanceID(false);
                newInstance.Composite = child;

                instancedEnt.ChildCompositeInstance = newInstance;
                childComposites.Add((function, child, childAliases, newPath, instancedEnt, childUnderShared, childSharedPath));
            }
            Parallel.ForEach(childComposites, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, childInfo =>
            {
                GenerateInstances(childInfo.child, childInfo.newPath, childInfo.instancedEnt.ChildCompositeInstance,
                    compositeInstance, childInfo.instancedEnt, childInfo.childAliases, childInfo.childUnderShared, childInfo.childSharedPath);
            });
        }

        private void ProcessInstances(InstancedComposite composite, bool isTemplate, bool isShared, bool isRequiredAssets, bool deleteStandardCollision, bool deleteBallisticCollision, bool isDeleted)
        {
            if (composite.Composite.shortGUID == _globalGUID)
                return;

            //Handle root ordering a bit different to more closely match retail
            bool isRoot = ReferenceEquals(composite, Root);

            List<InstancedEntity> pakOrder = new List<InstancedEntity>();
            List<InstancedEntity> composites = new List<InstancedEntity>();
            List<InstancedEntity> functionTypes = new List<InstancedEntity>();
            foreach (InstancedEntity entity in composite.Entities)
            {
                if (entity.Entity.variant != EntityVariant.FUNCTION)
                    continue;
                pakOrder.Add(entity);
                FunctionEntity fe = (FunctionEntity)entity.Entity;
                if (fe.function.IsFunctionType)
                    functionTypes.Add(entity);
                else
                    composites.Add(entity);
            }

            if (isRoot)
            {
                composites = composites.OrderByDescending(e => e.Entity.shortGUID.AsUInt32).ToList();
                foreach (InstancedEntity entity in composites)
                    ProcessInstanceEntityAndChildren(entity, isTemplate, isShared, isRequiredAssets, deleteStandardCollision, deleteBallisticCollision, isDeleted);
                foreach (InstancedEntity entity in functionTypes)
                    ProcessInstanceEntityAndChildren(entity, isTemplate, isShared, isRequiredAssets, deleteStandardCollision, deleteBallisticCollision, isDeleted);
            }
            else
            {
                foreach (InstancedEntity entity in pakOrder)
                    ProcessInstanceEntityAndChildren(entity, isTemplate, isShared, isRequiredAssets, deleteStandardCollision, deleteBallisticCollision, isDeleted);
            }
        }

        private void ProcessInstanceEntityAndChildren(InstancedEntity entity, bool isTemplate, bool isShared, bool isRequiredAssets, bool deleteStandardCollision, bool deleteBallisticCollision, bool isDeleted)
        {
            FunctionEntity function = (FunctionEntity)entity.Entity;
            if (function.function.IsFunctionType)
                ProcessEntity(entity, isTemplate, isRequiredAssets, deleteStandardCollision, deleteBallisticCollision, isDeleted, isShared);

            if (entity.ChildCompositeInstance == null)
                return;

            bool thisIsDeleted = isDeleted || entity.Bools.Get(ShortGuids.deleted) || (entity.Bools.Has(ShortGuids.delete_me) && entity.Bools.Get(ShortGuids.delete_me));
            bool thisIsTemplate = isTemplate || entity.Bools.Get(ShortGuids.is_template);

            bool thisIsShared = entity.Bools.Get(ShortGuids.is_shared);
            if (thisIsShared && !isRequiredAssets && !thisIsDeleted)
            {
                if (_sharedComposites.Contains(entity.ChildCompositeInstance.Composite.shortGUID))
                    return;
                if (!thisIsTemplate)
                    _sharedComposites.Add(entity.ChildCompositeInstance.Composite.shortGUID);
            }

            ProcessInstances(
                entity.ChildCompositeInstance,
                thisIsTemplate,
                isRequiredAssets ? false : (isShared || thisIsShared),
                isRequiredAssets,
                deleteStandardCollision || entity.Bools.Get(ShortGuids.delete_standard_collision),
                deleteBallisticCollision || entity.Bools.Get(ShortGuids.delete_ballistic_collision),
                thisIsDeleted);
        }

        private void ProcessEntity(InstancedEntity entity, bool isTemplate, bool isRequiredAssets, bool deleteStandardCollision, bool deleteBallisticCollision, bool isDeleted, bool isShared)
        {
            if (entity.Entity.variant != EntityVariant.FUNCTION)
                return;

            FunctionEntity function = (FunctionEntity)entity.Entity;
            if (!function.function.IsFunctionType)
                return;

            isDeleted = isDeleted || (entity.Bools.Has(ShortGuids.deleted) && entity.Bools.Get(ShortGuids.deleted)) || (entity.Bools.Has(ShortGuids.delete_me) && entity.Bools.Get(ShortGuids.delete_me));

            switch (function.function.AsFunctionType)
            {
                case FunctionType.CAGEAnimation:

                    break;
                case FunctionType.CameraPlayAnimation:

                    break;
                case FunctionType.Character:

                    break;
                case FunctionType.CMD_GoTo:

                    break;
                case FunctionType.CMD_GoToCover:

                    break;
                case FunctionType.CMD_MoveTowards:

                    break;
                case FunctionType.CMD_PlayAnimation:

                    break;
                case FunctionType.CollisionBarrier:
                    bool static_collision = entity.Bools.Get(ShortGuids.static_collision);
                    if (!isDeleted && !isTemplate && !isRequiredAssets && static_collision)
                    {
                        if (function.GetResource(ResourceType.COLLISION_MAPPING) != null) // note - we should add if the resource exists, even if it doesn't map to a valid collision mapping entry
                        {
                            AddResourceEntry(entity);
                        }

                    }
                    if (!isDeleted && !isTemplate && static_collision)
                    {
                        CollisionMaps.CollisionType collisionType = CollisionMaps.CollisionType.STANDARD;
                        switch ((COLLISION_TYPE)entity.EnumIndexes.Get(ShortGuids.collision_type))
                        {
                            case COLLISION_TYPE.CAMERA_COL:
                                collisionType = CollisionMaps.CollisionType.CAMERA;
                                break;
                            case COLLISION_TYPE.LINE_OF_SIGHT_COL:
                                collisionType = CollisionMaps.CollisionType.PATH_CLOSED;
                                break;
                            case COLLISION_TYPE.UI:
                                collisionType = CollisionMaps.CollisionType.UI;
                                break;
                            case COLLISION_TYPE.PLAYER_COL:
                                collisionType = CollisionMaps.CollisionType.PLAYER_ONLY;
                                break;
                            case COLLISION_TYPE.PHYSICS_COL:
                                collisionType = CollisionMaps.CollisionType.AGAINST_DYNAMIC_SIMULATED;
                                break;
                            case COLLISION_TYPE.TRANSPARENT_COL:
                                collisionType = CollisionMaps.CollisionType.TRANSPARENT;
                                break;
                        }

                        CollisionMaps.CollisionFlags barrierFlags = CollisionMaps.CollisionFlags.WORLD | CollisionMaps.CollisionFlags.FIXED | CollisionMaps.CollisionFlags.PREBUILT | (CollisionMaps.CollisionFlags)collisionType;
                        bool barrierEnable = !entity.Bools.Has(ShortGuids.enable_on_reset) || entity.Bools.Get(ShortGuids.enable_on_reset);
                        bool barrierGhosted = !barrierEnable;
                        if (barrierGhosted)
                        {
                            barrierFlags |= CollisionMaps.CollisionFlags.GHOSTED;
                            barrierFlags |= CollisionMaps.CollisionFlags.PRE_GHOSTED;
                        }

                        //Barriers are frozen in retail on essentially all levels
                        barrierFlags |= CollisionMaps.CollisionFlags.FROZEN;
                        barrierFlags |= CollisionMaps.CollisionFlags.PRE_FROZEN;

                        CollisionMaps.COLLISION_MAPPING newMap = new CollisionMaps.COLLISION_MAPPING()
                        {
                            Entity = entity.Handle,
                            Flags = barrierFlags,
                            ResourceGUID = GetResourceID(entity),
                            ZoneID = ResolveCollisionZoneId(entity)
                        };
                        if (!isRequiredAssets)
                        {
                            newMap.CollisionInstance = AllocateHavokBoxInstance(entity, barrierFlags);
                            lock (_collisionMapsLock)
                            {
                                _level.CollisionMaps.Entries.Add(newMap);
                            }
                        }
                    }
                    break;
                case FunctionType.ColourCorrectionTransition:

                    break;
                case FunctionType.CoverExclusionArea:

                    break;
                case FunctionType.CoverLine:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    break;
                case FunctionType.EnvironmentMap:

                    break;
                case FunctionType.EnvironmentModelReference:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    break;
                case FunctionType.ExclusiveMaster:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        AddResourceEntry(entity);
                        _exclusiveMasters.Add(entity);
                    }
                    break;
                case FunctionType.FogBox:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        FOG_BOX_TYPE type = (FOG_BOX_TYPE)entity.EnumIndexes.Get(ShortGuids.GEOMETRY_TYPE); //defines the model to use

                        CA_FOGPLANE.FEATURES features = 0;
                        if (entity.Bools.Get(ShortGuids.BILLBOARD))
                            features |= CA_FOGPLANE.FEATURES.BILLBOARD;
                        if (entity.Bools.Get(ShortGuids.LOW_RES) & !entity.Bools.Get(ShortGuids.EARLY_ALPHA))
                            features |= CA_FOGPLANE.FEATURES.LOW_RES;
                        if (entity.Bools.Get(ShortGuids.EARLY_ALPHA))
                            features |= CA_FOGPLANE.FEATURES.EARLY_ALPHA;
                        if (entity.Bools.Get(ShortGuids.CONVEX_GEOM))
                            features |= CA_FOGPLANE.FEATURES.CONVEX_GEOM;
                        if (entity.Bools.Get(ShortGuids.START_DISTANT_CLIP))
                            features |= CA_FOGPLANE.FEATURES.START_DISTANT_CLIP;
                        if (entity.Bools.Get(ShortGuids.SOFTNESS))
                            features |= CA_FOGPLANE.FEATURES.SOFTNESS;
                        if (entity.Bools.Get(ShortGuids.LINEAR_HEIGHT_DENSITY))
                            features |= CA_FOGPLANE.FEATURES.LINEAR_HEIGHT_DENSITY;
                        if (entity.Bools.Get(ShortGuids.FRESNEL_FALLOFF))
                            features |= CA_FOGPLANE.FEATURES.FRESNEL_FALLOFF;
                        if (entity.Bools.Get(ShortGuids.DEPTH_INTERSECT_COLOUR))
                            features |= CA_FOGPLANE.FEATURES.DEPTH_INTERSECT_COLOUR;

                        Resources.Resource resource = AddResourceEntry(entity);

                        Movers.MOVER_DESCRIPTOR mvr = new Movers.MOVER_DESCRIPTOR();
                        mvr.Transform = entity.CalculateWorldTransformMatrix();
                        List<RenderableElements.Element> reds = ((FunctionEntity)entity.Entity).GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance;
                        reds = ApplyShaderFeatureMaterial(reds, entity, (long)features);
                        if (reds != null && reds.Count > 0 && reds[0].Material != null && reds[0].Material.Shader != null)
                        {
                            switch (reds[0].Material.Shader.Ubershader)
                            {
                                case SHADER_LIST.CA_FOGPLANE:
                                    FOGPLANE_GPU_CONSTANTS gpuConstants = new FOGPLANE_GPU_CONSTANTS();
                                    gpuConstants.StartDistanceFadeScalar = entity.Bools.Get(ShortGuids.START_DISTANT_CLIP) ? entity.Floats.Get(ShortGuids.START_DISTANCE_FADE) : 0.0f;
                                    gpuConstants.DistanceFadeScalar = entity.Floats.Get(ShortGuids.DISTANCE_FADE) + 1.192092896e-07F;
                                    gpuConstants.AngleFadeScalar = entity.Floats.Get(ShortGuids.ANGLE_FADE);
                                    gpuConstants.FresnelPowerScalar = entity.Floats.Get(ShortGuids.FRESNEL_POWER);
                                    gpuConstants.HeightMaxDensityScalar = entity.Floats.Get(ShortGuids.HEIGHT_MAX_DENSITY);
                                    gpuConstants.ThicknessScalar = entity.Floats.Get(ShortGuids.THICKNESS);
                                    gpuConstants.ColourTint = entity.Vectors.Get(ShortGuids.COLOUR_TINT) / 255.0f;
                                    mvr.GPUConstants.SetAs<FOGPLANE_GPU_CONSTANTS>(gpuConstants);
                                    break;
                            }
                        }
                        mvr.RenderableElements = reds;
                        mvr.Resource = resource;
                        mvr.Entity = entity.Handle;
                        //Retail FogBox movers always seem to have NO_CAST_SHADOWS
                        mvr.CullFlags |= Movers.CullFlag.NO_CAST_SHADOWS;
                        ApplyMoverZones(mvr, entity);
                        mvr.LightingMasterID = entity.LightingMaster;
                        AddMover(entity, mvr, isTemplate);
                    }
                    break;
                case FunctionType.FogPlane:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        // i think this might actually be set on the entity connected to fog_plane_resource ??

                        Movers.MOVER_DESCRIPTOR mvr = new Movers.MOVER_DESCRIPTOR();
                        mvr.Transform = entity.CalculateWorldTransformMatrix();
                        List<RenderableElements.Element> reds = ((FunctionEntity)entity.Entity).GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance;
                        if (reds != null && reds.Count > 0 && reds[0].Material != null && reds[0].Material.Shader != null)
                        {
                            switch (reds[0].Material.Shader.Ubershader)
                            {
                                case SHADER_LIST.CA_FOGPLANE:
                                    FOGPLANE_GPU_CONSTANTS gpuConstants = new FOGPLANE_GPU_CONSTANTS();
                                    gpuConstants.StartDistanceFadeScalar = entity.Floats.Get(ShortGuids.start_distance_fade_scalar);
                                    gpuConstants.DistanceFadeScalar = entity.Floats.Get(ShortGuids.distance_fade_scalar) + 1.192092896e-07F;
                                    gpuConstants.AngleFadeScalar = entity.Floats.Get(ShortGuids.angle_fade_scalar);
                                    gpuConstants.FresnelPowerScalar = entity.Floats.Get(ShortGuids.linear_height_density_fresnel_power_scalar);
                                    gpuConstants.HeightMaxDensityScalar = entity.Floats.Get(ShortGuids.linear_heigth_density_max_scalar);
                                    gpuConstants.ThicknessScalar = entity.Floats.Get(ShortGuids.thickness_scalar);
                                    gpuConstants.EdgeSoftnessScalar = entity.Floats.Get(ShortGuids.edge_softness_scalar);
                                    gpuConstants.DiffuseMap0_UvScalar = entity.Floats.Get(ShortGuids.diffuse_0_uv_scalar);
                                    gpuConstants.DiffuseMap0_SpeedScalar = entity.Floats.Get(ShortGuids.diffuse_0_speed_scalar);
                                    gpuConstants.DiffuseMap1_UvScalar = entity.Floats.Get(ShortGuids.diffuse_1_uv_scalar);
                                    gpuConstants.DiffuseMap1_SpeedScalar = entity.Floats.Get(ShortGuids.diffuse_1_speed_scalar);
                                    gpuConstants.ColourTint = entity.Vectors.Get(ShortGuids.tint) / 255.0f;
                                    mvr.GPUConstants.SetAs<FOGPLANE_GPU_CONSTANTS>(gpuConstants);
                                    break;
                            }
                        }
                        mvr.RenderableElements = reds;
                        mvr.Entity = entity.Handle;
                        ApplyMoverZones(mvr, entity);
                        mvr.LightingMasterID = entity.LightingMaster;
                        AddMover(entity, mvr, isTemplate);
                    }
                    break;
                case FunctionType.FogSphere:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        CA_FOGSPHERE.FEATURES features = 0;
                        if (entity.Bools.Get(ShortGuids.EXPONENTIAL_DENSITY))
                            features |= CA_FOGSPHERE.FEATURES.EXPONENTIAL_DENSITY;
                        if (entity.Bools.Get(ShortGuids.SCENE_DEPENDANT_DENSITY))
                            features |= CA_FOGSPHERE.FEATURES.SCENE_DEPENDANT_DENSITY;
                        if (entity.Bools.Get(ShortGuids.FRESNEL_TERM))
                            features |= CA_FOGSPHERE.FEATURES.FRESNEL_TERM;
                        if (entity.Bools.Get(ShortGuids.SOFTNESS))
                            features |= CA_FOGSPHERE.FEATURES.SOFTNESS;
                        if (entity.Bools.Get(ShortGuids.LOW_RES_ALPHA) & !entity.Bools.Get(ShortGuids.EARLY_ALPHA))
                            features |= CA_FOGSPHERE.FEATURES.LOW_RES_ALPHA;
                        if (entity.Bools.Get(ShortGuids.EARLY_ALPHA))
                            features |= CA_FOGSPHERE.FEATURES.EARLY_ALPHA;
                        if (entity.Bools.Get(ShortGuids.BLEND_ALPHA_OVER_DISTANCE))
                            features |= CA_FOGSPHERE.FEATURES.BLEND_ALPHA_OVER_DISTANCE;
                        if (entity.Bools.Get(ShortGuids.SECONDARY_BLEND_ALPHA_OVER_DISTANCE))
                            features |= CA_FOGSPHERE.FEATURES.SECONDARY_BLEND_ALPHA_OVER_DISTANCE;
                        if (entity.Bools.Get(ShortGuids.CONVEX_GEOM))
                            features |= CA_FOGSPHERE.FEATURES.CONVEX_GEOM;
                        if (entity.Bools.Get(ShortGuids.ALPHA_LIGHTING))
                        {
                            features |= CA_FOGSPHERE.FEATURES.ALPHA_LIGHTING;
                            if (entity.Bools.Get(ShortGuids.DYNAMIC_ALPHA_LIGHTING))
                                features |= CA_FOGSPHERE.FEATURES.DYNAMIC_ALPHA_LIGHTING;
                        }
                        if (entity.Bools.Get(ShortGuids.DEPTH_INTERSECT_COLOUR))
                            features |= CA_FOGSPHERE.FEATURES.DEPTH_INTERSECT_COLOUR;
                        if (entity.Bools.Get(ShortGuids.NO_CLIP))
                            features |= CA_FOGSPHERE.FEATURES.NO_CLIP;

                        //exit if template init mode

                        Resources.Resource resource = AddResourceEntry(entity);

                        Movers.MOVER_DESCRIPTOR mvr = new Movers.MOVER_DESCRIPTOR();
                        mvr.Transform = entity.CalculateWorldTransformMatrix();
                        List<RenderableElements.Element> reds = ((FunctionEntity)entity.Entity).GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance;
                        reds = ApplyShaderFeatureMaterial(reds, entity, (long)features);
                        if (reds != null && reds.Count > 0 && reds[0].Material != null && reds[0].Material.Shader != null)
                        {
                            switch (reds[0].Material.Shader.Ubershader)
                            {
                                case SHADER_LIST.CA_FOGSPHERE:
                                    FOGSPHERE_GPU_CONSTANTS gpuConstants = new FOGSPHERE_GPU_CONSTANTS();
                                    gpuConstants.ColourTint = entity.Vectors.Get(ShortGuids.COLOUR_TINT) / 255.0f;
                                    gpuConstants.Intensity = entity.Floats.Get(ShortGuids.INTENSITY);
                                    gpuConstants.Opacity = entity.Floats.Get(ShortGuids.OPACITY);
                                    gpuConstants.Density = entity.Floats.Get(ShortGuids.DENSITY);
                                    gpuConstants.FresnelPower = entity.Floats.Get(ShortGuids.FRESNEL_POWER);
                                    gpuConstants.SoftnessEdge = entity.Floats.Get(ShortGuids.SOFTNESS_EDGE);
                                    gpuConstants.FarBlendDistance = entity.Floats.Get(ShortGuids.FAR_BLEND_DISTANCE);
                                    gpuConstants.NearBlendDistance = entity.Floats.Get(ShortGuids.NEAR_BLEND_DISTANCE);
                                    gpuConstants.SecondaryFarBlendDistance = entity.Floats.Get(ShortGuids.SECONDARY_FAR_BLEND_DISTANCE);
                                    gpuConstants.SecondaryNearBlendDistance = entity.Floats.Get(ShortGuids.SECONDARY_NEAR_BLEND_DISTANCE);
                                    gpuConstants.Radius = entity.Floats.Get(ShortGuids.radius);
                                    gpuConstants.DepthIntersectionColour = entity.Vectors.Get(ShortGuids.DEPTH_INTERSECT_COLOUR_VALUE) / 255.0f;
                                    gpuConstants.DepthIntersectionAlpha = entity.Floats.Get(ShortGuids.DEPTH_INTERSECT_ALPHA_VALUE);
                                    gpuConstants.DepthIntersectionRange = entity.Floats.Get(ShortGuids.DEPTH_INTERSECT_RANGE); 
                                    mvr.GPUConstants.SetAs<FOGSPHERE_GPU_CONSTANTS>(gpuConstants); 
                                    break;
                            }
                        }
                        mvr.RenderableElements = reds;
                        mvr.Resource = resource;
                        mvr.Entity = entity.Handle;
                        ApplyMoverZones(mvr, entity);
                        mvr.LightingMasterID = entity.LightingMaster;
                        AddMover(entity, mvr, isTemplate);
                    }
                    break;
                case FunctionType.JOB_Assault:

                    break;
                case FunctionType.JOB_SpottingPosition:

                    break;
                case FunctionType.LightingMaster:

                    break;
                case FunctionType.LightReference:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        Resources.Resource resource = AddResourceEntry(entity);

                        Movers.MOVER_DESCRIPTOR mvr = new Movers.MOVER_DESCRIPTOR();
                        mvr.Transform = entity.CalculateWorldTransformMatrix();
                        DEFERRED_PARAMS cpuConstants = new DEFERRED_PARAMS();
                        cpuConstants.Visibility = 1.0f;
                        cpuConstants.FlareIntensityScale = entity.Floats.Get(ShortGuids.flare_intensity_scale);
                        cpuConstants.RadiosityFraction = entity.Floats.Get(ShortGuids.radiosity_multiplier);
                        cpuConstants.Type = ((LIGHT_TYPE)entity.EnumIndexes.Get(ShortGuids.type)).AsLightType();
                        cpuConstants.ShadowPriorityOffset = (byte)entity.Integers.Get(ShortGuids.shadow_priority);
                        cpuConstants.SlopeScaleDepthBias = (byte)entity.Integers.Get(ShortGuids.slope_scale_depth_bias);
                        if (entity.Floats.Get(ShortGuids.diffuse_bias) > 1.0f)
                            cpuConstants.Features |= LightFeature.DiffuseBias;
                        if (entity.Bools.Get(ShortGuids.is_flash_light))
                            cpuConstants.Features |= LightFeature.Flashlight;
                        if (entity.Bools.Get(ShortGuids.has_lens_flare))
                            cpuConstants.Features |= LightFeature.LensFlare;
                        if (entity.Bools.Get(ShortGuids.has_noclip))
                            cpuConstants.Features |= LightFeature.NoClip;
                        if (entity.Bools.Get(ShortGuids.physical_attenuation))
                            cpuConstants.Features |= LightFeature.PhysicalAttenuation;
                        if (entity.Bools.Get(ShortGuids.horizontal_gobo_flip))
                            cpuConstants.Features |= LightFeature.HorizontalGoboFlip;
                        // Reading the entity's own value ahead of its links was tried here and is
                        // wrong: every light in ChallengeMap4 that disagreed with retail (112 of
                        // 1838) has is_specular driven by a PlatformConstantBool, and retail
                        // follows the link in BOTH directions - 104 where the own value is true and
                        // the link false, 8 the other way. Retail's light materials and their
                        // movers' DEFERRED_PARAMS agree on all 1862, so the material table is a
                        // second witness to the same answer.
                        if (entity.Bools.Get(ShortGuids.is_specular))
                            cpuConstants.Features |= LightFeature.Specular;
                        if (entity.Bools.Get(ShortGuids.no_alphalight))
                            cpuConstants.Features |= LightFeature.NoAlphaLight;
                        if (entity.Bools.Get(ShortGuids.volume) && cpuConstants.Type == LightType.Spot)
                            cpuConstants.Features |= LightFeature.Volume;
                        string goboTexture = entity.Strings.Get(ShortGuids.gobo_texture);
                        if (!string.IsNullOrEmpty(goboTexture) && cpuConstants.Type == LightType.Spot)
                            cpuConstants.Features |= LightFeature.Gobo;
                        if (entity.Bools.Get(ShortGuids.cast_shadow) && cpuConstants.Type == LightType.Spot)
                            cpuConstants.Features |= LightFeature.Shadow;
                        if (entity.Bools.Get(ShortGuids.is_square_light) && cpuConstants.Type == LightType.Spot)
                            cpuConstants.Features |= LightFeature.SquareLight;
                        if (entity.Bools.Get(ShortGuids.distance_mip_selection_gobo) && cpuConstants.Type == LightType.Spot)
                            cpuConstants.Features |= LightFeature.DistanceMipSelectionGobo;
                        float areaLightRadius = entity.Floats.Get(ShortGuids.area_light_radius);
                        if (areaLightRadius > 0.0001f)
                            cpuConstants.Features |= LightFeature.AreaLight;
                        float diffuseSoftness = entity.Floats.Get(ShortGuids.diffuse_softness);
                        if (diffuseSoftness > 0.0001f)
                            cpuConstants.Features |= LightFeature.SoftDiffuse;
                        cpuConstants.LightFadeType = (LightFadeType)(LIGHT_FADE_TYPE)entity.EnumIndexes.Get(ShortGuids.fade_type);
                        cpuConstants.FlareOccluderRadius = entity.Floats.Get(ShortGuids.flare_occluder_radius);
                        cpuConstants.FlareSpotOffset = entity.Floats.Get(ShortGuids.flare_spot_offset);
                        cpuConstants.DepthBias = entity.Floats.Get(ShortGuids.depth_bias);
                        // The material comes from these same resolved parameters, and is settled
                        // before the constants are written because the factory can drop the GOBO
                        // feature when the named texture is not packed with the level - retail's
                        // material flags equal their mover's DEFERRED_PARAMS on all 1862 of
                        // ChallengeMap4's light movers, so the two must never part company.
                        Materials.Material lightMaterial = _materialFactory?.GetLightMaterial(
                            cpuConstants.Type, cpuConstants.Features, goboTexture, DescribeForLog(entity));
                        if (lightMaterial?.OfflineLightFeatures != null)
                            cpuConstants.Features = (LightFeature)((lightMaterial.OfflineLightFeatures.Value >> 8) & 0xFFFF);
                        mvr.RenderConstants.SetAs<DEFERRED_PARAMS>(cpuConstants);
                        DEFERRED_GPU_CONSTANTS gpuConstants = new DEFERRED_GPU_CONSTANTS();
                        float endAttenuation = Math.Max(entity.Floats.Get(ShortGuids.end_attenuation), 0.00001f);
                        float startAttenuation = entity.Floats.Get(ShortGuids.start_attenuation);
                        startAttenuation = Math.Min(startAttenuation, endAttenuation - 0.05f);
                        gpuConstants.AttenuationBegin = Math.Max(startAttenuation, 0.00001f);
                        gpuConstants.AttenuationEnd = endAttenuation;
                        Vector3 colour = entity.Vectors.Get(ShortGuids.colour);
                        float intensity = ResolveLightIntensityMultiplier(entity);
                        Vector3 linearColour = Math.Max(0.0f, intensity) * new Vector3((float)MathsUtils.sRGBToLinear(colour.X / 255.0f), (float)MathsUtils.sRGBToLinear(colour.Y / 255.0f), (float)MathsUtils.sRGBToLinear(colour.Z / 255.0f));
                        if (cpuConstants.Features.HasFlag(LightFeature.PhysicalAttenuation))
                        {
                            gpuConstants.AttenuationDefocus = entity.Floats.Get(ShortGuids.defocus_attenuation);
                            gpuConstants.Colour = linearColour;
                            gpuConstants.VolumeColour = linearColour;
                        }
                        else
                        {
                            float attenRange = Math.Max(gpuConstants.AttenuationEnd - gpuConstants.AttenuationBegin, 0.0f);
                            float attenRangeOver5 = attenRange / 5.0f;
                            gpuConstants.AttenuationDefocus = attenRangeOver5 * attenRangeOver5;
                            gpuConstants.Colour = linearColour * gpuConstants.AttenuationDefocus;
                            gpuConstants.VolumeColour = gpuConstants.Colour;
                        }
                        if (cpuConstants.Features.HasFlag(LightFeature.Volume))
                        {
                            Vector3 volumeColourFactor = entity.Vectors.Get(ShortGuids.volume_colour_factor);
                            gpuConstants.VolumeColour *= new Vector3((float)MathsUtils.sRGBToLinear(volumeColourFactor.X / 255.0f), (float)MathsUtils.sRGBToLinear(volumeColourFactor.Y / 255.0f), (float)MathsUtils.sRGBToLinear(volumeColourFactor.Z / 255.0f));
                        }
                        gpuConstants.NearDist = Math.Min(entity.Floats.Get(ShortGuids.near_dist), gpuConstants.AttenuationEnd - 0.00001f);
                        gpuConstants.Softness = diffuseSoftness;
                        gpuConstants.DiffuseBias = entity.Floats.Get(ShortGuids.diffuse_bias);
                        gpuConstants.GlossinessScale = Math.Max(0.0f, Math.Min(1.0f, entity.Floats.Get(ShortGuids.glossiness_scale)));
                        if (cpuConstants.Type == LightType.Strip)
                        {
                            gpuConstants.OuterAngle = entity.Floats.Get(ShortGuids.strip_length) * 0.5f;
                            gpuConstants.InnerAngle = (float)Math.Min(Math.Min(Math.Max(Math.Cos(MathsUtils.Deg2Rad(entity.Floats.Get(ShortGuids.inner_cone_angle)) / 2.0f), 0.0f), 1.0f), 0.999f);
                        }
                        else
                        {
                            gpuConstants.OuterAngle = (float)Math.Min(Math.Min(Math.Max(Math.Cos(MathsUtils.Deg2Rad(entity.Floats.Get(ShortGuids.outer_cone_angle)) / 2.0f), 0.0f), 1.0f), 0.999f);
                            gpuConstants.InnerAngle = (float)Math.Min(Math.Min(Math.Max(Math.Cos(MathsUtils.Deg2Rad(entity.Floats.Get(ShortGuids.inner_cone_angle)) / 2.0f), 0.0f), 1.0f), 0.999f);
                            if (!cpuConstants.Features.HasFlag(LightFeature.SquareLight))
                            {
                                gpuConstants.InnerAngle = Math.Min(Math.Max(gpuConstants.OuterAngle + 0.01f, gpuConstants.InnerAngle), 0.999f);
                            }
                            else
                            {
                                gpuConstants.InnerAngle = Math.Min(gpuConstants.OuterAngle + 0.01f, 0.999f);
                            }
                        }
                        gpuConstants.ArealightRadius = areaLightRadius;
                        gpuConstants.NearDistShadowOffset = entity.Floats.Get(ShortGuids.near_dist_shadow_offset);
                        gpuConstants.AspectRatio = cpuConstants.Features.HasFlag(LightFeature.SquareLight) ? Math.Max(entity.Floats.Get(ShortGuids.aspect_ratio), 0.001f) : 1.0f;
                        gpuConstants.VolumeDensity = entity.Floats.Get(ShortGuids.volume_density);
                        float volumeEndAttenuation = entity.Floats.Get(ShortGuids.volume_end_attenuation);
                        gpuConstants.VolumeAttenuationEnd = volumeEndAttenuation > 0.0f ? volumeEndAttenuation : entity.Floats.Get(ShortGuids.end_attenuation);
                        mvr.GPUConstants.SetAs<DEFERRED_GPU_CONSTANTS>(gpuConstants);
                        // The renderable run belongs to the COMPOSITE, so every instance of a light
                        // prefab shares one material - but the features above and the gobo are
                        // per-instance, freely rewritten by aliases anywhere up the tree. Give the
                        // instance the material its own resolved parameters call for, which is what
                        // retail ships: 485 of ChallengeMap4's 1862 light movers point somewhere
                        // other than their composite's authored material.
                        List<RenderableElements.Element> lightReds =
                            ((FunctionEntity)entity.Entity).GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance;
                        mvr.RenderableElements = _materialFactory != null
                            ? _materialFactory.ApplyMaterial(lightReds, lightMaterial)
                            : lightReds;
                        mvr.Resource = resource;
                        if (entity.Bools.Get(ShortGuids.include_in_planar_reflections))
                            mvr.CullFlags |= Movers.CullFlag.INCLUDE_IN_REFLECTIVE;
                        else if (entity.ParentCompositeInstanceEntity != null && entity.ParentCompositeInstanceEntity.Bools.Get(ShortGuids.include_in_planar_reflections))
                            mvr.CullFlags |= Movers.CullFlag.INCLUDE_IN_REFLECTIVE;
                        mvr.Entity = entity.Handle;
                        ApplyMoverZones(mvr, entity);
                        mvr.LightingMasterID = entity.LightingMaster;
                        AddMover(entity, mvr, isTemplate);
                    }
                    break;
                case FunctionType.ModelReference:
                    {
                        Resources.Resource resource = null;
                        if (!isDeleted && !isRequiredAssets)
                        {
                            Parameter p = function.GetParameter("resource");
                            if (p?.content != null && p.content.dataType == DataType.RESOURCE)
                            {
                                cResource r = (cResource)p.content;
                                if (r.value.Count != 0)
                                {
                                    resource = AddResourceEntry(entity);
                                }
                            }
                        }

                        //Handle remapping the materials using the 'mapping' parameter.
                        MaterialMappings.MaterialMapping mapping = null;
                        List<RenderableElements.Element> reds = null;
                        if (!(isDeleted || isRequiredAssets) || resource != null)
                        {
                            List<RenderableElements.Element> ogReds = ((FunctionEntity)entity.Entity).GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance;
                            mapping = MaterialRemappingUtils.TryResolveMappingForModelReference(_level, entity);
                            reds = MaterialRemappingUtils.ApplyMapping(_level, mapping, ogReds);
                            if (entity.Strings.Has(ShortGuids.material) || (entity.Strings.Links != null && entity.Strings.Links.ContainsKey(ShortGuids.material)))
                            {
                                string materialOverride = entity.Strings.Get(ShortGuids.material);
                                if (materialOverride != "" && materialOverride != null)
                                    reds = MaterialRemappingUtils.ApplyMaterialParameterOverride(_level, materialOverride, reds);
                            }
                        }

                        if (!(isDeleted || isRequiredAssets))
                        {
                            bool deleteStandard = deleteStandardCollision || (entity.Bools.Has(ShortGuids.delete_standard_collision) && entity.Bools.Get(ShortGuids.delete_standard_collision));
                            bool deleteBallistic = deleteBallisticCollision || (entity.Bools.Has(ShortGuids.delete_ballistic_collision) && entity.Bools.Get(ShortGuids.delete_ballistic_collision));

                            CollisionMaps.COLLISION_MAPPING template = ((FunctionEntity)entity.Entity).GetResource(ResourceType.COLLISION_MAPPING, true)?.CollisionMapping;
                            if (template != null)
                            {
                                //REQUIRED_ASSETS composites keep GHOSTED (seems odd?)
                                bool forceGhosted = isTemplate;
                                string compositeName = entity.Composite?.name?.Replace('/', '\\') ?? string.Empty;
                                if (compositeName.StartsWith("Required_Assets\\", StringComparison.OrdinalIgnoreCase))
                                    forceGhosted = true;

                                bool emit = true;
                                if (deleteStandard && deleteBallistic)
                                {
                                    if (!forceGhosted)
                                        emit = false;
                                    else
                                    {
                                        //Ghosted shell only — do not set BALLISTIC_ONLY|STANDARD_ONLY.
                                        deleteStandard = false;
                                        deleteBallistic = false;
                                    }
                                }

                                if (emit)
                                {
                                    CollisionMaps.CollisionFlags flags = BuildInstanceCollisionFlags(entity, deleteBallistic, forceGhosted, template.Material);
                                    CollisionMaps.COLLISION_MAPPING newMap = new CollisionMaps.COLLISION_MAPPING()
                                    {
                                        Flags = flags,
                                        CollisionProxy = template.CollisionProxy,
                                        CollisionInstance = AllocateHavokCompoundInstance(entity, template.CollisionProxy, flags),
                                        ResourceGUID = template.ResourceGUID != ShortGuid.Invalid ? template.ResourceGUID : GetResourceID(entity),
                                        Entity = entity.Handle,
                                        Material = template.Material, // note - this is a physics material, not the renderable one. it's only stored in the template collision mapping!
                                        MaterialMapping = mapping, //todo - if this has no renderable maybe we discard the remapping? i guess it doesn't matter.
                                        ZoneID = ResolveCollisionZoneId(entity)
                                    };

                                    lock (_collisionMapsLock)
                                    {
                                        _level.CollisionMaps.Entries.Add(newMap);
                                    }
                                }
                            }
                        }

                        if (resource != null)
                        {
                            Movers.MOVER_DESCRIPTOR mvr = new Movers.MOVER_DESCRIPTOR();
                            mvr.Transform = entity.CalculateWorldTransformMatrix();
                            if (reds != null && reds.Count > 0 && reds[0].Material != null && reds[0].Material.Shader != null)
                            {
                                switch (reds[0].Material.Shader.Ubershader)
                                {
                                    case SHADER_LIST.CA_ENVIRONMENT:
                                        {
                                            ENVIRONMENT_GPU_CONSTANTS gpuConstants = new ENVIRONMENT_GPU_CONSTANTS();
                                            Vector3 vertColourScale = entity.Vectors.Get(ShortGuids.vertex_colour_scale);
                                            gpuConstants.VertexColourScalars = new Vector4(vertColourScale.X, vertColourScale.Y, vertColourScale.Z, entity.Floats.Get(ShortGuids.vertex_opacity_scale));
                                            Vector3 diffColourScale = entity.Vectors.Get(ShortGuids.diffuse_colour_scale) / 255.0f;
                                            gpuConstants.DiffuseColourScalars = new Vector4(diffColourScale.X, diffColourScale.Y, diffColourScale.Z, entity.Floats.Get(ShortGuids.diffuse_opacity_scale));
                                            gpuConstants.AlphaBlendNoisePowerScale = entity.Floats.Get(ShortGuids.alpha_blend_noise_power_scale);
                                            gpuConstants.AlphaBlendNoiseUvScale = entity.Floats.Get(ShortGuids.alpha_blend_noise_uv_scale);
                                            gpuConstants.AlphaBlendNoiseUvOffset = new Vector2(entity.Floats.Get(ShortGuids.alpha_blend_noise_uv_offset_X), entity.Floats.Get(ShortGuids.alpha_blend_noise_uv_offset_Y));
                                            gpuConstants.DirtMultiplyBlendSpecPowerScale = entity.Floats.Get(ShortGuids.dirt_multiply_blend_spec_power_scale);
                                            gpuConstants.DirtMapUvScale = entity.Floats.Get(ShortGuids.dirt_map_uv_scale);
                                            mvr.GPUConstants.SetAs<ENVIRONMENT_GPU_CONSTANTS>(gpuConstants);
                                        }
                                        break;
                                    case SHADER_LIST.CA_LIGHT_DECAL:
                                        {
                                            LIGHTDECAL_GPU_CONSTANTS gpuConstants = new LIGHTDECAL_GPU_CONSTANTS();
                                            Vector3 tint = entity.Vectors.Get(ShortGuids.lightdecal_tint) / 255.0f;
                                            float intensity = entity.Floats.Get(ShortGuids.lightdecal_intensity);
                                            gpuConstants.LightdecalIntensity = new Vector3((float)MathsUtils.sRGBToLinear(tint.X), (float)MathsUtils.sRGBToLinear(tint.Y), (float)MathsUtils.sRGBToLinear(tint.Z)) * intensity;
                                            mvr.GPUConstants.SetAs<LIGHTDECAL_GPU_CONSTANTS>(gpuConstants);
                                        }
                                        break;
                                }
                            }
                            mvr.RenderableElements = reds;
                            mvr.Resource = resource;
                            if (entity.Bools.Get(ShortGuids.disable_size_culling))
                                mvr.CullFlags |= Movers.CullFlag.NO_SIZE_CULLING;
                            if (!entity.Bools.Get(ShortGuids.cast_shadows))
                                mvr.CullFlags |= Movers.CullFlag.NO_CAST_SHADOWS;
                            if (!entity.Bools.Get(ShortGuids.cast_shadows_in_torch))
                                mvr.CullFlags |= Movers.CullFlag.NO_CAST_TORCH_SHADOW;
                            if (entity.Bools.Get(ShortGuids.include_in_planar_reflections))
                                mvr.CullFlags |= Movers.CullFlag.INCLUDE_IN_REFLECTIVE;
                            else if (entity.ParentCompositeInstanceEntity != null && entity.ParentCompositeInstanceEntity.Bools.Get(ShortGuids.include_in_planar_reflections))
                                mvr.CullFlags |= Movers.CullFlag.INCLUDE_IN_REFLECTIVE;
                            mvr.Entity = entity.Handle;
                            if (MaterialUsesMoverEnvironmentMap(reds))
                                mvr.EnvironmentMap = entity.EnvironmentMap;
                            mvr.EmissiveTint = entity.Vectors.Get(ShortGuids.emissive_tint);
                            if (entity.Bools.Get(ShortGuids.replace_intensity))
                                mvr.EmissiveFlags |= Movers.EmissiveFlag.ReplaceIntensity;
                            if (entity.Bools.Get(ShortGuids.replace_tint))
                                mvr.EmissiveFlags |= Movers.EmissiveFlag.ReplaceTint;
                            mvr.EmissiveIntensityMultiplier = ResolveModelReferenceEmissiveIntensity(entity, isTemplate);
                            mvr.EmissiveRadiosityMultiplier = Math.Max(0.0f, entity.Floats.Get(ShortGuids.radiosity_multiplier));
                            // An AUTHORED radiosity_multiplier of 0 excludes the model from the
                            // radiosity bake - retail Solace drops exactly its 4 such fixtures
                            // while lighting 900 whose multiplier is merely absent (also 0 in the
                            // MVR, so the distinction only exists here where the parameter table
                            // is in hand). The value itself does not scale the baked output:
                            // retail's per-entity Weight/sqrt(area) is flat (~270) across every
                            // authored value from 0.2 to >1.5, and Scale stays on the material
                            // EMISSIVE_MULT grid, so nonzero values are runtime-side.
                            if (entity.Floats.Values.TryGetValue(ShortGuids.radiosity_multiplier, out float authoredRadiosity) &&
                                authoredRadiosity <= 0.0f)
                                RadiosityAuthoredOff.Add((entity.Handle.composite_instance_id.AsUInt32, entity.Handle.entity_id.AsUInt32));
                            ApplyMoverZones(mvr, entity);
                            mvr.LightingMasterID = entity.LightingMaster;
                            AddMover(entity, mvr, isTemplate);
                        }
                    }
                    break;
                case FunctionType.NavMeshArea:

                    break;
                case FunctionType.NavMeshBarrier:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    if (!isDeleted && !isTemplate)
                    {
                        CollisionMaps.COLLISION_MAPPING newMap = new CollisionMaps.COLLISION_MAPPING()
                        {
                            Entity = entity.Handle,
                            Flags = (CollisionMaps.CollisionFlags)CollisionMaps.CollisionType.PATH_CLOSED |
                                    CollisionMaps.CollisionFlags.WORLD |
                                    CollisionMaps.CollisionFlags.FIXED |
                                    CollisionMaps.CollisionFlags.PREBUILT,
                            ResourceGUID = GetResourceID(entity),
                            ZoneID = ShortGuid.Invalid
                        };
                        if (!isRequiredAssets)
                        {
                            newMap.CollisionInstance = AllocateHavokBoxInstance(entity, newMap.Flags);
                            lock (_collisionMapsLock)
                            {
                                _level.CollisionMaps.Entries.Add(newMap);
                            }
                        }
                    }
                    break;
                case FunctionType.NavMeshExclusionArea:

                    break;
                case FunctionType.NavMeshReachabilitySeedPoint:

                    break;
                case FunctionType.NavMeshWalkablePlatform:

                    break;
                case FunctionType.ParticleEmitterReference:
                    {
                        // See task #43 - this guard is the SCI_Hub / Tech_Hub trade-off. Emitting
                        // templates fixes SCI_Hub's load crash but breaks Tech_Hub, which then
                        // crashes in update_parameters ~28s in.
                        //
                        // Measured against retail BSP_TORRENS: with templates emitted we write 1053
                        // more movers than retail (+18.6%), 4079 more REDS entries (+14.4%) and 1023
                        // more resources (+12.5%), and nothing retail writes is missing - it is a
                        // strict superset. Retail emits FX movers for 31 composites where we emit
                        // for 92, and 61 of the extra 61 are composites retail never instances at
                        // all: *_Template prefabs, Character_Burning\AndroidBurn*, Blood\FX_*_On_Lens,
                        // Debris\Bottle*, Pistol_VFX\Tazer_* - all spawned at runtime rather than
                        // placed. They are correctly marked invisible, but still occupy a slot each.
                        //
                        // Retail never ships a mover for a template (prefab) FX emitter. Joining
                        // our instanced mover set to the shipped MVR by entity handle, isTemplate
                        // separates the two sets exactly on BSP_LV426_Pt01, SCI_Hub, Solace,
                        // BSP_Torrens and HAB_Airport: every emitter reached only through a
                        // template path has no retail mover, and every non-template one has
                        // precisely one. A template is a definition the spawner instantiates at
                        // runtime, so pre-instancing it hands the engine a second copy of an
                        // emitter it is about to create itself.
                        if (isDeleted || isTemplate)
                            break;
                        if (isRequiredAssets && !EmitRequiredAssetParticles)
                            break;

                        Resources.Resource resource = AddResourceEntry(entity);

                        Movers.MOVER_DESCRIPTOR mvr = new Movers.MOVER_DESCRIPTOR();
                        mvr.Transform = entity.CalculateWorldTransformMatrix();
                        if (entity.Integers.Get(ShortGuids.CPU) == 1)
                        {
                            //model is CPU_PARTICLE_MODEL

                            //requires_script

                            mvr.GPUConstants.SetAs<DYNAMIC_FX_GPU_CONSTANTS>(new DYNAMIC_FX_GPU_CONSTANTS()
                            {
                                 ExpiryTime = entity.Floats.Get(ShortGuids.SYSTEM_EXPIRY_TIME),
                                 //generate random number
                            });
                            mvr.RenderConstants.SetAs<DYNAMIC_PFX_PARAMS>(new DYNAMIC_PFX_PARAMS()
                            {
                                DrawPass = entity.Integers.Get(ShortGuids.DRAW_PASS),
                                Entity = entity.Handle
                            });
                        }
                        else
                        {
                            //model is 1000_PARTICLE_CUBE

                            PARTICLE_GPU_CONSTANTS gpuConstants = new PARTICLE_GPU_CONSTANTS();
                            //gpuConstants.RandomNumber <- random number, is this important?
                            gpuConstants.ExpiryTime = entity.Floats.Get(ShortGuids.SYSTEM_EXPIRY_TIME);
                            gpuConstants.AspectRatio = entity.Floats.Get(ShortGuids.ASPECT_RATIO);
                            gpuConstants.FadeAtDistance = entity.Floats.Get(ShortGuids.FADE_AT_DISTANCE);
                            gpuConstants.AlphaIn = entity.Floats.Get(ShortGuids.ALPHA_IN) * 0.01f;
                            gpuConstants.AlphaOut = entity.Floats.Get(ShortGuids.ALPHA_OUT) * 0.01f;
                            gpuConstants.AlphaRefValue = entity.Floats.Get(ShortGuids.ALPHA_REF_VALUE);
                            gpuConstants.SizeStartMin = entity.Floats.Get(ShortGuids.SIZE_START_MIN);
                            gpuConstants.SizeStartMax = entity.Floats.Get(ShortGuids.SIZE_START_MAX);
                            gpuConstants.SizeEndMin = entity.Floats.Get(ShortGuids.SIZE_END_MIN);
                            gpuConstants.SizeEndMax = entity.Floats.Get(ShortGuids.SIZE_END_MAX);
                            gpuConstants.MaskAmountMin = entity.Floats.Get(ShortGuids.MASK_AMOUNT_MIN);
                            gpuConstants.MaskAmountMax = entity.Floats.Get(ShortGuids.MASK_AMOUNT_MAX);
                            gpuConstants.MaskAmountMidpoint = entity.Floats.Get(ShortGuids.MASK_AMOUNT_MIDPOINT);
                            gpuConstants.ColourScaleMin = entity.Floats.Get(ShortGuids.COLOUR_SCALE_MIN);
                            gpuConstants.ColourScaleMax = entity.Floats.Get(ShortGuids.COLOUR_SCALE_MAX);
                            gpuConstants.ParticleExpiryTimeMin = entity.Floats.Get(ShortGuids.PARTICLE_EXPIRY_TIME_MIN);
                            gpuConstants.ParticleExpiryTimeMax = entity.Floats.Get(ShortGuids.PARTICLE_EXPIRY_TIME_MAX);
                            gpuConstants.Wind = new Vector3(entity.Floats.Get(ShortGuids.WIND_X), entity.Floats.Get(ShortGuids.WIND_Y), entity.Floats.Get(ShortGuids.WIND_Z));
                            // Retail stores a random per-system slot in RandomNumber/VertexOffset,
                            // but the pool layout it indexes is the RETAIL tool's, not ours -
                            // randomising them here KILLED live FX (ChallengeMap3 cam10's door
                            // light-shaft vanished; offsets pointing at garbage verts). All-zero
                            // offsets are what we have always shipped and they render correctly,
                            // so they stay zero deliberately.
                            mvr.GPUConstants.SetAs<PARTICLE_GPU_CONSTANTS>(gpuConstants);

                            PARTICLE_PARAMS cpuConstants = new PARTICLE_PARAMS();
                            int particleCount = entity.Integers.Get(ShortGuids.PARTICLE_COUNT);
                            cpuConstants.NumVerts = 2 * particleCount * 4;
                            cpuConstants.PrimitiveCount = 2 * particleCount;
                            cpuConstants.VertexOffset = (int)(gpuConstants.RandomNumber * (float)(1000 - particleCount)) * 4;
                            cpuConstants.DrawPass = entity.Integers.Get(ShortGuids.DRAW_PASS);
                            cpuConstants.BoundingBoxMax = entity.Vectors.Get(ShortGuids.bounds_max);
                            cpuConstants.BoundingBoxMin = entity.Vectors.Get(ShortGuids.bounds_min);
                            cpuConstants.Entity = entity.Handle;
                            mvr.RenderConstants.SetAs<PARTICLE_PARAMS>(cpuConstants);
                        }
                        mvr.RenderableElements = ApplyFxMaterial(((FunctionEntity)entity.Entity).GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance, entity);
                        mvr.Resource = resource;
                        if (mvr.RenderableElements != null && mvr.RenderableElements.Count > 0 && mvr.RenderableElements[0].Material != null && mvr.RenderableElements[0].Material.Shader != null)
                        {
                            if ((mvr.RenderableElements[0].Material.Shader.UbershaderRequirementFlags & (1L << (int)SHADER_REQUIREMENTS.APPROXIMATE_LIGHTING)) == 0)
                            {
                                // todo - is this correct?
                                if (entity.Bools.Get(ShortGuids.include_in_planar_reflections))
                                    mvr.CullFlags |= Movers.CullFlag.INCLUDE_IN_REFLECTIVE;
                                else if (entity.ParentCompositeInstanceEntity != null && entity.ParentCompositeInstanceEntity.Bools.Get(ShortGuids.include_in_planar_reflections))
                                    mvr.CullFlags |= Movers.CullFlag.INCLUDE_IN_REFLECTIVE;
                            }
                            if (mvr.RenderableElements[0].Material.Priority >= 59 && mvr.RenderableElements[0].Material.Priority <= 80)
                            {
                                mvr.CullFlags |= Movers.CullFlag.NO_CAST_SHADOWS;
                            }
                            if ((mvr.RenderableElements[0].Material.Shader.UbershaderRequirementFlags & (1L << (int)SHADER_REQUIREMENTS.RIBBON)) == 0 ||
                                ((mvr.RenderableElements[0].Material.Shader.UbershaderRequirementFlags & (1L << (int)SHADER_REQUIREMENTS.PARTICLE)) == 0 && (mvr.RenderableElements[0].Material.Shader.UbershaderRequirementFlags & (1L << (int)SHADER_REQUIREMENTS.CPU)) == 0))
                            {
                                //dynamic geometry
                                mvr.CullFlags |= Movers.CullFlag.NO_CAST_SHADOWS;
                            }
                            if ((mvr.RenderableElements[0].Material.Shader.UbershaderRequirementFlags & (1L << (int)SHADER_REQUIREMENTS.STREAMER)) == 0)
                            {
                                mvr.CullFlags |= Movers.CullFlag.NO_CAST_SHADOWS;
                            }
                        }
                        if (entity.Integers.Get(ShortGuids.CPU) != 1)
                        {
                            mvr.CullFlags |= Movers.CullFlag.NO_CAST_SHADOWS;
                        }
                        mvr.Entity = entity.Handle;
                        ApplyMoverZones(mvr, entity);
                        mvr.LightingMasterID = entity.LightingMaster;
                        AddMover(entity, mvr, isTemplate);
                    }
                    break;
                case FunctionType.PathfindingAlienBackstageNode:

                    break;
                case FunctionType.PathfindingManualNode:

                    break;
                case FunctionType.PathfindingTeleportNode:

                    break;
                case FunctionType.PathfindingWaitNode:

                    break;
                case FunctionType.PhysicsModifyGravity:

                    break;
                case FunctionType.PhysicsSystem:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        ResourceReference physicsSystem = function.GetResource(ResourceType.DYNAMIC_PHYSICS_SYSTEM);
                        int systemIndex = physicsSystem?.PhysicsSystem?.SystemIndex ?? physicsSystem?.PhysicsSystemIndex ?? -1;
                        if (physicsSystem == null || systemIndex == -1)
                        {
                            //Should warn here!
                            break;
                        }

                        HavokPackfile.PhysicsSystem template = physicsSystem.PhysicsSystem ?? _level.Physics?.GetPhysicsSystem(systemIndex);
                        if (template == null)
                        {
                            //Should warn here!
                            break;
                        }

                        (Vector3 position, Quaternion rotation) = entity.CalculateWorldPositionRotation();
                        lock (_physicsMapsLock)
                        {
                            _level.PhysicsMaps.Entries.Add(new PhysicsMaps.DYNAMIC_PHYSICS_SYSTEM()
                            {
                                PhysicsSystem = template,
                                composite_instance_id = entity.ThisCompositeInstance.InstanceID,
                                entity = entity.ParentCompositeInstanceEntity.Handle,
                                Position = position,
                                Rotation = rotation
                            });
                        }

                        AddResourceEntry(entity);
                    }
                    break;
                case FunctionType.ProjectiveDecal:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        Movers.MOVER_DESCRIPTOR mvr = new Movers.MOVER_DESCRIPTOR();
                        mvr.Transform = entity.CalculateWorldTransformMatrix();
                        mvr.RenderableElements = ((FunctionEntity)entity.Entity).GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance;
                        mvr.Resource = AddResourceEntry(entity);
                        if (entity.Bools.Get(ShortGuids.include_in_planar_reflections))
                            mvr.CullFlags |= Movers.CullFlag.INCLUDE_IN_REFLECTIVE;
                        else if (entity.ParentCompositeInstanceEntity != null && entity.ParentCompositeInstanceEntity.Bools.Get(ShortGuids.include_in_planar_reflections))
                            mvr.CullFlags |= Movers.CullFlag.INCLUDE_IN_REFLECTIVE;
                        mvr.Entity = entity.Handle;
                        ApplyMoverZones(mvr, entity);
                        mvr.LightingMasterID = entity.LightingMaster;
                        AddMover(entity, mvr, isTemplate);
                    }
                    break;
                case FunctionType.RadiosityIsland:

                    break;
                case FunctionType.RadiosityProxy:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    //if (!isDeleted && !isTemplate && !isRequiredAssets)
                    //{
                    //    CollisionMaps.COLLISION_MAPPING newMap = new CollisionMaps.COLLISION_MAPPING()
                    //    {
                    //        Entity = new EntityHandle()
                    //        {
                    //            composite_instance_id = entity.ThisCompositeInstance.InstanceID,
                    //            entity_id = entity.Entity.shortGUID
                    //        },
                    //    };
                    //    lock (_collisionMapsLock)
                    //    {
                    //        if (!isTemplate && !isRequiredAssets)
                    //            _level.CollisionMaps.Entries.Add(newMap);
                    //    }
                    //}
                    break;
                case FunctionType.RegisterCharacterModel:

                    break;
                case FunctionType.RibbonEmitterReference:
                    // Held in step with ParticleEmitterReference above - see the note there for the
                    // measurements behind the isTemplate guard.
                    if (!isDeleted && !isRequiredAssets && !isTemplate)
                    {
                        Resources.Resource resource = AddResourceEntry(entity);

                        //same thing about early exiting - ENTITY2 resource happens above the exit, i'm confused what each do - need to investigate

                        Movers.MOVER_DESCRIPTOR mvr = new Movers.MOVER_DESCRIPTOR();
                        mvr.Transform = entity.CalculateWorldTransformMatrix();
                        mvr.GPUConstants.SetAs<DYNAMIC_FX_GPU_CONSTANTS>(new DYNAMIC_FX_GPU_CONSTANTS()
                        {
                            ExpiryTime = entity.Floats.Get(ShortGuids.SYSTEM_EXPIRY_TIME),
                            //generate a random number
                        });
                        mvr.RenderConstants.SetAs<DYNAMIC_PFX_PARAMS>(new DYNAMIC_PFX_PARAMS()
                        {
                            DrawPass = entity.Integers.Get(ShortGuids.DRAW_PASS),
                            Entity = entity.Handle
                        });
                        mvr.RenderableElements = ApplyFxMaterial(((FunctionEntity)entity.Entity).GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance, entity);
                        mvr.Resource = resource;
                        if (mvr.RenderableElements != null && mvr.RenderableElements.Count > 0 && mvr.RenderableElements[0].Material != null && mvr.RenderableElements[0].Material.Shader != null)
                        {
                            if ((mvr.RenderableElements[0].Material.Shader.UbershaderRequirementFlags & (1L << (int)SHADER_REQUIREMENTS.APPROXIMATE_LIGHTING)) == 0)
                            {
                                // todo - is this correct?
                                if (entity.Bools.Get(ShortGuids.include_in_planar_reflections))
                                    mvr.CullFlags |= Movers.CullFlag.INCLUDE_IN_REFLECTIVE;
                                else if (entity.ParentCompositeInstanceEntity != null && entity.ParentCompositeInstanceEntity.Bools.Get(ShortGuids.include_in_planar_reflections))
                                    mvr.CullFlags |= Movers.CullFlag.INCLUDE_IN_REFLECTIVE;
                            }
                        }
                        mvr.Entity = entity.Handle;
                        //RibbonEmitterReference movers seem to always have NO_CAST_SHADOWS
                        mvr.CullFlags |= Movers.CullFlag.NO_CAST_SHADOWS;
                        ApplyMoverZones(mvr, entity);
                        mvr.LightingMasterID = entity.LightingMaster;
                        AddMover(entity, mvr, isTemplate);
                    }
                    break;
                //SimpleWater and SimpleRefraction produce no mover - retail emits none for either, on
                //any level (6 and 3 entities across the whole game) - so there is no renderable to
                //hang a material on. The feature mask is still derived so that whoever gives these a
                //renderable does not have to rediscover it.
                case FunctionType.SimpleRefraction:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        CA_SIMPLE_REFRACTION.FEATURES features = 0;
                        if (entity.Bools.Get(ShortGuids.SECONDARY_NORMAL_MAPPING))
                            features |= CA_SIMPLE_REFRACTION.FEATURES.SECONDARY_NORMAL_MAPPING;
                        if (entity.Bools.Get(ShortGuids.ALPHA_MASKING))
                            features |= CA_SIMPLE_REFRACTION.FEATURES.ALPHA_MASKING;
                        if (entity.Bools.Get(ShortGuids.DISTORTION_OCCLUSION))
                            features |= CA_SIMPLE_REFRACTION.FEATURES.DISTORTION_OCCLUSION;
                        if (entity.Bools.Get(ShortGuids.FLOW_UV_ANIMATION))
                            features |= CA_SIMPLE_REFRACTION.FEATURES.FLOW_UV_ANIMATION;
                        AddResourceEntry(entity);
                    }
                    break;
                case FunctionType.SimpleWater:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        AddResourceEntry(entity);
                        CA_SIMPLEWATER.FEATURES features = 0;
                        if (entity.Bools.Get(ShortGuids.SECONDARY_NORMAL_MAPPING))
                            features |= CA_SIMPLEWATER.FEATURES.SECONDARY_NORMAL_MAPPING;
                        if (entity.Bools.Get(ShortGuids.LOW_RES_ALPHA_PASS))
                            features |= CA_SIMPLEWATER.FEATURES.LOW_RES_ALPHA_PASS;
                        if (entity.Bools.Get(ShortGuids.ALPHA_MASKING))
                            features |= CA_SIMPLEWATER.FEATURES.ALPHA_MASKING;
                        if (entity.Bools.Get(ShortGuids.FLOW_MAPPING))
                            features |= CA_SIMPLEWATER.FEATURES.FLOW_UV_ANIMATION;
                        if (entity.Bools.Get(ShortGuids.ENVIRONMENT_MAPPING))
                            features |= CA_SIMPLEWATER.FEATURES.ENVIRONMENT_MAPPING;
                        if (entity.Bools.Get(ShortGuids.LOCALISED_ENVIRONMENT_MAPPING))
                            features |= CA_SIMPLEWATER.FEATURES.LOCALISED_ENVIRONMENT_MAPPING;
                        if (entity.Bools.Get(ShortGuids.LOCALISED_ENVMAP_BOX_PROJECTION))
                            features |= CA_SIMPLEWATER.FEATURES.LOCALISED_ENVMAP_BOX_PROJECTION;
                        if (entity.Bools.Get(ShortGuids.REFLECTIVE_MAPPING))
                            features |= CA_SIMPLEWATER.FEATURES.REFLECTIVE_MAPPING;
                    }
                    break;
                case FunctionType.SoundBarrier:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    if (!isDeleted && !isTemplate)
                    {
                        bool bandAid = entity.Bools.Get(ShortGuids.band_aid);

                        CollisionMaps.COLLISION_MAPPING newMap = new CollisionMaps.COLLISION_MAPPING()
                        {
                            Entity = entity.Handle, 
                            Flags = (CollisionMaps.CollisionFlags)(bandAid ? CollisionMaps.CollisionType.SOUND : CollisionMaps.CollisionType.SOUND_BARRIER) |
                                    CollisionMaps.CollisionFlags.WORLD |
                                    CollisionMaps.CollisionFlags.FIXED |
                                    CollisionMaps.CollisionFlags.PREBUILT,
                            ResourceGUID = GetResourceID(entity),
                            ZoneID = ShortGuid.Invalid
                        };
                        if (!isRequiredAssets)
                        {
                            newMap.CollisionInstance = AllocateHavokBoxInstance(entity, newMap.Flags);
                            lock (_collisionMapsLock)
                            {
                                _level.CollisionMaps.Entries.Add(newMap);
                            }
                        }
                    }
                    break;
                case FunctionType.SoundEnvironmentMarker:
                    if (entity.Strings.Has(ShortGuids.reverb_name))
                    {
                        string reverb = entity.Strings.Get(ShortGuids.reverb_name);
                        bool shouldAdd = true;
                        for (int i = 0; i < _level.SoundEnvironmentData.Entries.Count; i++)
                        {
                            if (_level.SoundEnvironmentData.Entries[i].ToLower() == reverb.ToLower())
                            {
                                shouldAdd = false;
                                break;
                            }
                        }
                        if (shouldAdd)
                            _level.SoundEnvironmentData.Entries.Add(reverb);
                    }
                    break;
                case FunctionType.SoundLevelInitialiser:

                    break;
                case FunctionType.SoundNetworkNode:

                    break;
                case FunctionType.SpottingExclusionArea:

                    break;
                case FunctionType.SurfaceEffectBox:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        CA_EFFECT_OVERLAY.FEATURES features = CA_EFFECT_OVERLAY.FEATURES.BOX;
                        if (entity.Bools.Get(ShortGuids.WS_LOCKED))
                            features |= CA_EFFECT_OVERLAY.FEATURES.WS_LOCKED;
                        if (entity.Bools.Get(ShortGuids.ENVMAP))
                            features |= CA_EFFECT_OVERLAY.FEATURES.ENVMAP;
                        Resources.Resource resource = AddResourceEntry(entity);
                        Movers.MOVER_DESCRIPTOR mvr = new Movers.MOVER_DESCRIPTOR();
                        mvr.Transform = entity.CalculateWorldTransformMatrix();
                        mvr.RenderableElements = ApplyShaderFeatureMaterial(
                            ((FunctionEntity)entity.Entity).GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance,
                            entity, (long)features);
                        mvr.Resource = resource;
                        mvr.Entity = entity.Handle;
                        mvr.CullFlags |= Movers.CullFlag.NO_CAST_SHADOWS; // i think?
                        if (entity.Bools.Get(ShortGuids.ENVMAP))
                            mvr.EnvironmentMap = entity.EnvironmentMap;
                        ApplyMoverZones(mvr, entity);
                        mvr.LightingMasterID = entity.LightingMaster;
                        AddMover(entity, mvr, isTemplate);
                    }
                    break;
                case FunctionType.SurfaceEffectSphere:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                    {
                        CA_EFFECT_OVERLAY.FEATURES features = CA_EFFECT_OVERLAY.FEATURES.SPHERE;
                        if (entity.Bools.Get(ShortGuids.WS_LOCKED))
                            features |= CA_EFFECT_OVERLAY.FEATURES.WS_LOCKED;
                        if (entity.Bools.Get(ShortGuids.ENVMAP))
                            features |= CA_EFFECT_OVERLAY.FEATURES.ENVMAP;
                        Resources.Resource resource = AddResourceEntry(entity);
                        Movers.MOVER_DESCRIPTOR mvr = new Movers.MOVER_DESCRIPTOR();
                        mvr.Transform = entity.CalculateWorldTransformMatrix();
                        mvr.RenderableElements = ApplyShaderFeatureMaterial(
                            ((FunctionEntity)entity.Entity).GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance,
                            entity, (long)features);
                        mvr.Resource = resource;
                        mvr.Entity = entity.Handle;
                        mvr.CullFlags |= Movers.CullFlag.NO_CAST_SHADOWS;
                        if (entity.Bools.Get(ShortGuids.ENVMAP))
                            mvr.EnvironmentMap = entity.EnvironmentMap;
                        ApplyMoverZones(mvr, entity);
                        mvr.LightingMasterID = entity.LightingMaster;
                        AddMover(entity, mvr, isTemplate);
                    }
                    break;
                case FunctionType.TRAV_1ShotClimbUnder:
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                case FunctionType.TRAV_1ShotFloorVentExit:
                case FunctionType.TRAV_1ShotLeap:
                case FunctionType.TRAV_1ShotSpline:
                case FunctionType.TRAV_1ShotVentEntrance:
                case FunctionType.TRAV_1ShotVentExit:
                case FunctionType.TRAV_ContinuousBalanceBeam:
                case FunctionType.TRAV_ContinuousCinematicSidle:
                case FunctionType.TRAV_ContinuousClimbingWall:
                case FunctionType.TRAV_ContinuousLadder:
                case FunctionType.TRAV_ContinuousLedge:
                case FunctionType.TRAV_ContinuousPipe:
                case FunctionType.TRAV_ContinuousTightGap:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    break;
                case FunctionType.Zone:

                    break;
            }
        }

        private Resources.Resource AddResourceEntry(InstancedEntity entity)
        {
            lock (_resourcesLock)
            {
                return _level.Resources.AddUniqueResource(GetResourceID(entity), entity.ThisCompositeInstance.InstanceID);
            }
        }

        //Rebuild level states from the instanced ExclusiveMaster entities
        private void BuildExclusiveMasterStates()
        {
            if (_level.StateResources == null)
                _level.StateResources = new List<Level.State>();

            Level.State state0 = _level.StateResources.Count > 0 ? _level.StateResources[0] : new Level.State();
            if (_level.StateResources.Count == 0)
                _level.StateResources.Add(state0);

            List<InstancedEntity> masters = _exclusiveMasters.GroupBy(e => (e.ThisCompositeInstance.InstanceID.AsUInt32, GetResourceID(e).AsUInt32)).Select(g => g.First()).ToList();

            var remaining = new Dictionary<(uint, uint), InstancedEntity>(masters.Count);
            for (int i = 0; i < masters.Count; i++)
            {
                InstancedEntity master = masters[i];
                remaining[(master.ThisCompositeInstance.InstanceID.AsUInt32, GetResourceID(master).AsUInt32)] = master;
            }

            var rebuilt = new List<Level.State>(_level.StateResources.Count);
            rebuilt.Add(state0);

            // THIS LOGIC IS TEMPORARY!!
            // - If I can look up an existing ExclusiveMaster state, keep it
            // - If I can't, copy from state 0 (the default state)
            // This is really NOT IDEAL though - we should generate this data here instead. It includes navmesh and cover, and is built off the entities given to the masters.

            for (int i = 1; i < _level.StateResources.Count; i++)
            {
                Level.State existing = _level.StateResources[i];
                if (existing == null)
                    continue;

                InstancedEntity match = null;
                (uint, uint) matchKey = default;

                if (existing.ExclusiveMaster != null)
                {
                    foreach (var kvp in remaining)
                    {
                        InstancedEntity candidate = kvp.Value;
                        if (candidate.Entity == existing.ExclusiveMaster &&
                            candidate.ThisCompositeInstance.InstanceID == existing.CompositeInstanceId)
                        {
                            match = candidate;
                            matchKey = kvp.Key;
                            break;
                        }
                    }
                }
                else if (existing.Resource != null)
                {
                    matchKey = (existing.Resource.composite_instance_id.AsUInt32, existing.Resource.resource_id.AsUInt32);
                    remaining.TryGetValue(matchKey, out match);
                }

                if (match == null)
                    continue;

                existing.ExclusiveMaster = match.Entity;
                existing.CompositeInstanceId = match.ThisCompositeInstance.InstanceID;
                existing.Resource = AddResourceEntry(match);
                rebuilt.Add(existing);
                remaining.Remove(matchKey);
            }

            string world = _level.Filepath + (_level.Patched ? "_PATCH" : "") + "/WORLD/";
            foreach (InstancedEntity master in remaining.Values.OrderBy(e => e.ThisCompositeInstance.InstanceID.AsUInt32).ThenBy(e => GetResourceID(e).AsUInt32))
            {
                int stateIndex = rebuilt.Count;
                string statePath = world + "STATE_" + stateIndex + "/";
                rebuilt.Add(CreateStateFromState0(state0, statePath, master));
            }

            _level.StateResources = rebuilt;
        }
        private Level.State CreateStateFromState0(Level.State state0, string stateDirectory, InstancedEntity master)
        {
            Directory.CreateDirectory(stateDirectory);

            string coverPath = Path.Combine(stateDirectory, "COVER");
            string navMeshPath = Path.Combine(stateDirectory, "NAV_MESH");

            TryCopyStateFile(state0?.Cover?.Filepath, coverPath);
            TryCopyStateFile(state0?.NavMesh?.Filepath, navMeshPath);

            return new Level.State()
            {
                ExclusiveMaster = master.Entity,
                CompositeInstanceId = master.ThisCompositeInstance.InstanceID,
                Resource = AddResourceEntry(master),
                Cover = new Cover(coverPath),
                NavMesh = new NavigationMesh(navMeshPath)
            };
        }
        private static void TryCopyStateFile(string sourcePath, string destPath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return;
            File.Copy(sourcePath, destPath, true);
        }

        //Materials with EnvironmentMapIndex 255 do not sample a cubemap from the mover.
        private static bool MaterialUsesMoverEnvironmentMap(List<RenderableElements.Element> reds)
        {
            if (reds == null)
                return false;
            foreach (RenderableElements.Element red in reds)
            {
                if (red?.Material != null && red.Material.EnvironmentMapIndex != 255)
                    return true;
            }
            return false;
        }

        //Utility for working out the emissive intensity multiplier for mover - not quite right yet, but a good match
        private static float ResolveModelReferenceEmissiveIntensity(InstancedEntity entity, bool isTemplate)
        {
            if (isTemplate || entity.Bools.Get(ShortGuids.is_template))
                return 0.0f;
            if (!entity.Bools.Get(ShortGuids.light_on_reset))
                return 0.0f;

            // Follows the link the same way a light's intensity does, and for the same measured
            // reason: on the 7611 ChallengeMap4 model references this pass lights, following it
            // matches retail's own EmissiveIntensityMultiplier 7559 times against 7409 for the
            // "only some link sources count" test, which reads 1.0 where the level authored 0.05.
            float intensity;
            if (entity.Floats.Links.TryGetValue(ShortGuids.intensity_multiplier, out List<Tuple<ShortGuid, InstancedEntity>> links) && links.Count > 0)
            {
                intensity = entity.Floats.Get(ShortGuids.intensity_multiplier);
            }
            else if (entity.Floats.Values.TryGetValue(ShortGuids.intensity_multiplier, out float value))
            {
                intensity = value;
            }
            else
            {
                intensity = 1.0f;
            }
            return Math.Max(0.0f, intensity);
        }

        // A light's intensity_multiplier resolves like any other parameter: whatever drives it wins,
        // then the entity's own value, then 1 for a light that never mentions it. Judging which
        // link sources count as "static" and falling back to the local value for the rest was tried
        // and is much worse - scored against the Colour retail's own light movers carry (their
        // attenuation terms already agree exactly, so colour isolates the intensity), following the
        // link is right on 92-99.5% of lights on every level while the static test manages 43-77%:
        // ChallengeMap4 1829/1838 against 1415, SCI_HospitalUpper 2101/2125 against 1063,
        // BSP_Torrens 731/740 against 496, Solace 1644/1677 against 723, Tech_Hub 3361/3452
        // against 2582, HAB_Airport 4453/4814 against 2979. Gating on light_on_reset was tried too
        // and is wrong in the other direction: it darkens 165-1292 lights a level that retail
        // leaves lit.
        private static float ResolveLightIntensityMultiplier(InstancedEntity entity)
        {
            if (entity.Floats.Links.TryGetValue(ShortGuids.intensity_multiplier, out List<Tuple<ShortGuid, InstancedEntity>> links) && links.Count > 0)
                return Math.Max(0.0f, entity.Floats.Get(ShortGuids.intensity_multiplier));

            if (entity.Floats.Values.TryGetValue(ShortGuids.intensity_multiplier, out float value))
                return Math.Max(0.0f, value);

            return 1.0f;
        }

        private static ShortGuid GetResourceID(InstancedEntity entity)
        {
            //Resource IDs for PhysicsSystem entities are always 'DYNAMIC_PHYSICS_SYSTEM'.
            ShortGuid resourceID = ((FunctionEntity)entity.Entity).function == FunctionType.PhysicsSystem ? ShortGuids.DYNAMIC_PHYSICS_SYSTEM : entity.Entity.shortGUID;
            if (resourceID == entity.Entity.shortGUID)
            {
                Parameter resource = entity.Entity.GetParameter(ShortGuids.resource);
                if (resource?.content != null && resource.content.dataType == DataType.RESOURCE)
                {
                    //In the case that the resource is a parameter, we take that ID, which is actually based on a hash of the entity name instead of the direct entity ID.
                    resourceID = ((cResource)resource.content).shortGUID;
                }
            }
            return resourceID;
        }
    }
}
#endif