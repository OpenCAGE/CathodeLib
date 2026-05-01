using CATHODE;
using CATHODE.Enums;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CATHODE.ShaderTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using static CATHODE.Lights;
using static CATHODE.Movers.MOVER_DESCRIPTOR;
using static CATHODE.Movers.MOVER_DESCRIPTOR.GPU_CONSTANTS;
using static CATHODE.Movers.MOVER_DESCRIPTOR.RENDER_CONSTANTS;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN)
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
                //Check links first, these override the values
                if (Links.TryGetValue(guid, out List<Tuple<ShortGuid, InstancedEntity>> links))
                    if (links.Count != 0)
                        return links[0].Item2.GetAs<T>(links[0].Item1);

                //Fall back to our own value
                if (Values.TryGetValue(guid, out T val))
                    return val;

                throw new Exception("Failed to find param.");
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

        public Level Level = null;
        public Entity Entity = null;
        public EntityPath Path = null;
        public Composite Composite = null;

        //TODO: also load in MVR etc here?

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
                        }
                    }
                    break;

                case EntityVariant.ALIAS:
                    throw new Exception("unexpected");

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
                            float rand = (float)new Random().NextDouble() * range;
                            float result = rand + min;
                            return GetValueAs<T>(result);
                        }
                    case FunctionType.RandomInt:
                        {
                            int min = Integers.Get(ShortGuids.Min);
                            int range = Integers.Get(ShortGuids.Max) - min;
                            int rand = new Random().Next(range);
                            int result = rand + min;
                            return GetValueAs<T>(result);
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
                        //if (typeof(T) == typeof(string))
                        //    return (T)(object)Strings.Get("display_model");
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
                        //if (typeof(T) == typeof(string))
                        //    return (T)(object)Strings.Get("initial_value");
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
                        //if (typeof(T) == typeof(string))
                        //    return (T)(object)Strings.Get("initial_value");
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
                else
                {
                    throw new Exception("Unhandled");
                }
            }
        }

        private T GetValueAs<T>(object value)
        {
            if (value == null)
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
                else if (typeof(T) == typeof(string))
                    return (T)(object)"";
                else
                    throw new Exception("Unhandled type conversion");
            }

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
        
        private Matrix4x4 CalculateWorldTransformMatrix()
        {
            Transform localTransform = GetAs<Transform>(ShortGuids.position);
            Matrix4x4 localMatrix = localTransform.AsMatrix();
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

        private readonly ConcurrentDictionary<(Entity, Composite), List<(ShortGuid, ParameterVariant, DataType)>> _parameterCache = new ConcurrentDictionary<(Entity, Composite), List<(ShortGuid, ParameterVariant, DataType)>>();
        private readonly ConcurrentDictionary<(Composite, ShortGuid), Entity> _entityLookupCache = new ConcurrentDictionary<(Composite, ShortGuid), Entity>();

        private readonly object _resourcesLock = new object();
        private readonly object _physicsMapsLock = new object();
        private readonly object _collisionMapsLock = new object();

        private List<ShortGuid> _sharedComposites = new List<ShortGuid>();
        private ShortGuid _globalGUID;

        public Instancing(Level level)
        {
            _level = level;
        }

        public void GenerateInstances()
        {
            _globalGUID = _level.Commands.EntryPoints[1].shortGUID;

            List<Composite> requiredAssets = new List<Composite>();
            requiredAssets.Add(_level.Commands.Entries.FirstOrDefault(o => o.name.ToUpper() == "GLOBAL"));
            requiredAssets.Add(_level.Commands.Entries.FirstOrDefault(o => o.name.ToUpper() == "PAUSEMENU"));
            //requiredAssets.Add(_level.Commands.Entries.FirstOrDefault(o => o.name.ToUpper().Replace("/", "\\") == "REQUIRED_ASSETS\\JOBS\\INTERNAL\\SEARCHTARGETJOB\\SEARCHTARGETJOB"));
            requiredAssets.AddRange(_level.Commands.Entries.FindAll(o => o.name.ToUpper().Replace("/", "\\").StartsWith("REQUIRED_ASSETS\\")));
            foreach (Composite requiredAsset in requiredAssets)
            {
                InstancedComposite instancedRequiredAsset = new InstancedComposite()
                {
                    Composite = requiredAsset,
                    InstanceID = requiredAsset.shortGUID
                };
                RequiredAssets.Add(instancedRequiredAsset);
                GenerateInstances(requiredAsset, new EntityPath(), instancedRequiredAsset, null, null, new List<InstancedAlias>());
            }

            Root = new InstancedComposite()
            {
                Composite = _level.Commands.EntryPoints[0],
                InstanceID = ShortGuid.InstanceGuid
            };
            GenerateInstances(Root.Composite, new EntityPath(), Root, null, null, new List<InstancedAlias>());
        }
        public void ProcessInstances()
        {
            if (Root?.Composite == null)
                throw new Exception("Call GenerateInstances first");

            for (int i = 0; i < 18; i++)
                _level.CollisionMaps.Entries.Add(new CollisionMaps.COLLISION_MAPPING());

            _sharedComposites.Clear(); // i think we shouldn't populate shared things for required, OR, should do Root first?
            foreach (InstancedComposite instancedRequiredAsset in RequiredAssets)
            {
                //ProcessInstances(instancedRequiredAsset, false, false, true, false, false);
            }
            _sharedComposites.Clear();
            ProcessInstances(Root, false, false, false, false, false);
        }

        private void GenerateInstances(Composite composite, EntityPath path, InstancedComposite compositeInstance, InstancedComposite parentCompositeInstance, InstancedEntity parentCompositeInstanceEntity, List<InstancedAlias> aliases)
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
            List<(FunctionEntity function, Composite child, List<InstancedAlias> childAliases, EntityPath newPath, InstancedEntity instancedEnt)> childComposites = new List<(FunctionEntity, Composite, List<InstancedAlias>, EntityPath, InstancedEntity)>();
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
                InstancedComposite newInstance = new InstancedComposite();
                newInstance.InstanceID = newPath.GenerateCompositeInstanceID(false);
                newInstance.Composite = child;

                if (!entityByGuid.TryGetValue(function.shortGUID, out InstancedEntity instancedEnt))
                    continue;

                instancedEnt.ChildCompositeInstance = newInstance;
                childComposites.Add((function, child, childAliases, newPath, instancedEnt));
            }
            Parallel.ForEach(childComposites, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, childInfo =>
            {
                GenerateInstances(childInfo.child, childInfo.newPath, childInfo.instancedEnt.ChildCompositeInstance,
                    compositeInstance, childInfo.instancedEnt, childInfo.childAliases);
            });
        }

        private void ProcessInstances(InstancedComposite composite, bool isTemplate, bool isShared, bool isRequiredAssets, bool deleteStandardCollision, bool isDeleted)
        {
            if (composite.Composite.shortGUID == _globalGUID)
                return;

            var entitiesToProcess = composite.Entities.Where(e => e.Entity.variant == EntityVariant.FUNCTION && ((FunctionEntity)e.Entity).function.IsFunctionType).ToList();
            Parallel.ForEach(entitiesToProcess, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, entity =>
            {
                ProcessEntity(entity, isTemplate, isRequiredAssets, deleteStandardCollision, isDeleted);
            });

            foreach (InstancedEntity entity in composite.Entities)
            {
                if (entity.ChildCompositeInstance != null)
                {
                    bool thisIsDeleted = isDeleted || entity.Bools.Get(ShortGuids.deleted) || (entity.Bools.Has(ShortGuids.delete_me) && entity.Bools.Get(ShortGuids.delete_me));

                    bool thisIsShared = entity.Bools.Get(ShortGuids.is_shared);
                    if (thisIsShared && !isRequiredAssets && !thisIsDeleted)
                    {
                        if (_sharedComposites.Contains(entity.ChildCompositeInstance.Composite.shortGUID))
                            continue;
                        _sharedComposites.Add(entity.ChildCompositeInstance.Composite.shortGUID);
                    }

                    ProcessInstances(entity.ChildCompositeInstance, isTemplate || entity.Bools.Get(ShortGuids.is_template), isRequiredAssets ? false : isShared || thisIsShared, isRequiredAssets, deleteStandardCollision || entity.Bools.Get(ShortGuids.delete_standard_collision), thisIsDeleted);
                }
            }
        }

        private void ProcessEntity(InstancedEntity entity, bool isTemplate, bool isRequiredAssets, bool deleteStandardCollision, bool isDeleted)
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
                    if (!isDeleted && !isTemplate && static_collision && !deleteStandardCollision)
                    {
                        CollisionMaps.COLLISION_MAPPING newMap = new CollisionMaps.COLLISION_MAPPING()
                        {
                            Entity = new EntityHandle()
                            {
                                composite_instance_id = entity.ThisCompositeInstance.InstanceID,
                                entity_id = entity.Entity.shortGUID
                            },
                        };
                        lock (_collisionMapsLock)
                        {
                            if (!isTemplate && !isRequiredAssets)
                                _level.CollisionMaps.Entries.Add(newMap);
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
                        AddResourceEntry(entity);
                    break;
                case FunctionType.FogBox:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    {
                        CA_FOGPLANE.FEATURES features = 0;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("BILLBOARD")))
                            features |= CA_FOGPLANE.FEATURES.BILLBOARD;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("LOW_RES")) & !entity.Bools.Get(ShortGuidUtils.Generate("EARLY_ALPHA")))
                            features |= CA_FOGPLANE.FEATURES.LOW_RES;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("EARLY_ALPHA")))
                            features |= CA_FOGPLANE.FEATURES.EARLY_ALPHA;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("CONVEX_GEOM")))
                            features |= CA_FOGPLANE.FEATURES.CONVEX_GEOM;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("START_DISTANT_CLIP")))
                            features |= CA_FOGPLANE.FEATURES.START_DISTANT_CLIP;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("SOFTNESS")))
                            features |= CA_FOGPLANE.FEATURES.SOFTNESS;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("LINEAR_HEIGHT_DENSITY")))
                            features |= CA_FOGPLANE.FEATURES.LINEAR_HEIGHT_DENSITY;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("FRESNEL_FALLOFF")))
                            features |= CA_FOGPLANE.FEATURES.FRESNEL_FALLOFF;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("DEPTH_INTERSECT_COLOUR")))
                            features |= CA_FOGPLANE.FEATURES.DEPTH_INTERSECT_COLOUR;

                        //if material is CA_FOGPLANE
                        FOGPLANE_GPU_CONSTANTS gpuConstants = new FOGPLANE_GPU_CONSTANTS();
                        gpuConstants.StartDistanceFadeScalar = entity.Bools.Get(ShortGuidUtils.Generate("START_DISTANT_CLIP")) ? entity.Floats.Get(ShortGuidUtils.Generate("START_DISTANCE_FADE")) : 0.0f;
                        gpuConstants.DistanceFadeScalar = entity.Floats.Get(ShortGuidUtils.Generate("DISTANCE_FADE")) + 1.192092896e-07F;
                        gpuConstants.AngleFadeScalar = entity.Floats.Get(ShortGuidUtils.Generate("ANGLE_FADE"));
                        gpuConstants.FresnelPowerScalar = entity.Floats.Get(ShortGuidUtils.Generate("FRESNEL_POWER"));
                        gpuConstants.HeightMaxDensityScalar = entity.Floats.Get(ShortGuidUtils.Generate("HEIGHT_MAX_DENSITY"));
                        gpuConstants.ThicknessScalar = entity.Floats.Get(ShortGuidUtils.Generate("THICKNESS"));
                        gpuConstants.ColourTint = entity.Vectors.Get(ShortGuidUtils.Generate("COLOUR_TINT")) / 255.0f;
                    }
                    break;
                case FunctionType.FogPlane:
                    {
                        //if material is CA_FOGPLANE
                        FOGPLANE_GPU_CONSTANTS gpuConstants = new FOGPLANE_GPU_CONSTANTS();
                        gpuConstants.StartDistanceFadeScalar = entity.Floats.Get(ShortGuidUtils.Generate("start_distance_fade_scalar"));
                        gpuConstants.DistanceFadeScalar = entity.Floats.Get(ShortGuidUtils.Generate("distance_fade_scalar")) + 1.192092896e-07F;
                        gpuConstants.AngleFadeScalar = entity.Floats.Get(ShortGuidUtils.Generate("angle_fade_scalar"));
                        gpuConstants.FresnelPowerScalar = entity.Floats.Get(ShortGuidUtils.Generate("linear_height_density_fresnel_power_scalar"));
                        gpuConstants.HeightMaxDensityScalar = entity.Floats.Get(ShortGuidUtils.Generate("linear_heigth_density_max_scalar"));
                        gpuConstants.ThicknessScalar = entity.Floats.Get(ShortGuidUtils.Generate("thickness_scalar"));
                        gpuConstants.EdgeSoftnessScalar = entity.Floats.Get(ShortGuidUtils.Generate("edge_softness_scalar"));
                        gpuConstants.DiffuseMap0_UvScalar = entity.Floats.Get(ShortGuidUtils.Generate("diffuse_0_uv_scalar"));
                        gpuConstants.DiffuseMap0_SpeedScalar = entity.Floats.Get(ShortGuidUtils.Generate("diffuse_0_speed_scalar"));
                        gpuConstants.DiffuseMap1_UvScalar = entity.Floats.Get(ShortGuidUtils.Generate("diffuse_1_uv_scalar"));
                        gpuConstants.DiffuseMap1_SpeedScalar = entity.Floats.Get(ShortGuidUtils.Generate("diffuse_1_speed_scalar"));
                        gpuConstants.ColourTint = entity.Vectors.Get(ShortGuidUtils.Generate("tint")) / 255.0f;
                    }
                    break;
                case FunctionType.FogSphere:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);

                    {
                        CA_FOGSPHERE.FEATURES features = 0;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("EXPONENTIAL_DENSITY")))
                            features |= CA_FOGSPHERE.FEATURES.EXPONENTIAL_DENSITY;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("SCENE_DEPENDANT_DENSITY")))
                            features |= CA_FOGSPHERE.FEATURES.SCENE_DEPENDANT_DENSITY;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("FRESNEL_TERM")))
                            features |= CA_FOGSPHERE.FEATURES.FRESNEL_TERM;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("SOFTNESS")))
                            features |= CA_FOGSPHERE.FEATURES.SOFTNESS;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("LOW_RES_ALPHA")) & !entity.Bools.Get(ShortGuidUtils.Generate("EARLY_ALPHA")))
                            features |= CA_FOGSPHERE.FEATURES.LOW_RES_ALPHA;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("EARLY_ALPHA")))
                            features |= CA_FOGSPHERE.FEATURES.EARLY_ALPHA;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("BLEND_ALPHA_OVER_DISTANCE")))
                            features |= CA_FOGSPHERE.FEATURES.BLEND_ALPHA_OVER_DISTANCE;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("SECONDARY_BLEND_ALPHA_OVER_DISTANCE")))
                            features |= CA_FOGSPHERE.FEATURES.SECONDARY_BLEND_ALPHA_OVER_DISTANCE;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("CONVEX_GEOM")))
                            features |= CA_FOGSPHERE.FEATURES.CONVEX_GEOM;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("ALPHA_LIGHTING")))
                        {
                            features |= CA_FOGSPHERE.FEATURES.ALPHA_LIGHTING;
                            if (entity.Bools.Get(ShortGuidUtils.Generate("DYNAMIC_ALPHA_LIGHTING")))
                                features |= CA_FOGSPHERE.FEATURES.DYNAMIC_ALPHA_LIGHTING;
                        }
                        if (entity.Bools.Get(ShortGuidUtils.Generate("DEPTH_INTERSECT_COLOUR")))
                            features |= CA_FOGSPHERE.FEATURES.DEPTH_INTERSECT_COLOUR;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("NO_CLIP")))
                            features |= CA_FOGSPHERE.FEATURES.NO_CLIP;

                        //if material is CA_FOGSPHERE
                        FOGSPHERE_GPU_CONSTANTS gpuConstants = new FOGSPHERE_GPU_CONSTANTS();
                        gpuConstants.ColourTint = entity.Vectors.Get(ShortGuidUtils.Generate("COLOUR_TINT")) / 255.0f;
                        gpuConstants.Intensity = entity.Floats.Get(ShortGuidUtils.Generate("INTENSITY"));
                        gpuConstants.Opacity = entity.Floats.Get(ShortGuidUtils.Generate("OPACITY"));
                        gpuConstants.Density = entity.Floats.Get(ShortGuidUtils.Generate("DENSITY"));
                        gpuConstants.FresnelPower = entity.Floats.Get(ShortGuidUtils.Generate("FRESNEL_POWER"));
                        gpuConstants.SoftnessEdge = entity.Floats.Get(ShortGuidUtils.Generate("SOFTNESS_EDGE"));
                        gpuConstants.FarBlendDistance = entity.Floats.Get(ShortGuidUtils.Generate("FAR_BLEND_DISTANCE"));
                        gpuConstants.NearBlendDistance = entity.Floats.Get(ShortGuidUtils.Generate("NEAR_BLEND_DISTANCE"));
                        gpuConstants.SecondaryFarBlendDistance = entity.Floats.Get(ShortGuidUtils.Generate("SECONDARY_FAR_BLEND_DISTANCE"));
                        gpuConstants.SecondaryNearBlendDistance = entity.Floats.Get(ShortGuidUtils.Generate("SECONDARY_NEAR_BLEND_DISTANCE"));
                        gpuConstants.Radius = entity.Floats.Get(ShortGuidUtils.Generate("radius"));
                        gpuConstants.DepthIntersectionColour = entity.Vectors.Get(ShortGuidUtils.Generate("DEPTH_INTERSECT_COLOUR_VALUE")) / 255.0f;
                        gpuConstants.DepthIntersectionAlpha = entity.Floats.Get(ShortGuidUtils.Generate("DEPTH_INTERSECT_ALPHA_VALUE"));
                        gpuConstants.DepthIntersectionRange = entity.Floats.Get(ShortGuidUtils.Generate("DEPTH_INTERSECT_RANGE"));
                    }
                    break;
                case FunctionType.JOB_Assault:

                    break;
                case FunctionType.JOB_SpottingPosition:

                    break;
                case FunctionType.LightingMaster:

                    break;
                case FunctionType.LightReference:
                    {
                        if (!isDeleted && !isTemplate && !isRequiredAssets)
                            AddResourceEntry(entity);


                        DEFERRED_PARAMS cpuConstants = new DEFERRED_PARAMS();
                        cpuConstants.Visibility = 1.0f;
                        cpuConstants.FlareIntensityScale = entity.Floats.Get(ShortGuidUtils.Generate("flare_intensity_scale"));
                        cpuConstants.RadiosityFraction = entity.Floats.Get(ShortGuidUtils.Generate("radiosity_multiplier"));
                        cpuConstants.Type = (LightType)entity.EnumIndexes.Get(ShortGuidUtils.Generate("type"));
                        cpuConstants.ShadowPriorityOffset = (byte)entity.Integers.Get(ShortGuidUtils.Generate("shadow_priority"));
                        cpuConstants.SlopeScaleDepthBias = (byte)entity.Integers.Get(ShortGuidUtils.Generate("slope_scale_depth_bias"));
                        if (entity.Floats.Get(ShortGuidUtils.Generate("diffuse_bias")) > 1.0f)
                            cpuConstants.Features |= LightFeature.DiffuseBias;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("is_flash_light")))
                            cpuConstants.Features |= LightFeature.Flashlight;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("has_lens_flare")))
                            cpuConstants.Features |= LightFeature.LensFlare;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("has_noclip")))
                            cpuConstants.Features |= LightFeature.NoClip;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("physical_attenuation")))
                            cpuConstants.Features |= LightFeature.PhysicalAttenuation;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("horizontal_gobo_flip")))
                            cpuConstants.Features |= LightFeature.HorizontalGoboFlip;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("is_specular")))
                            cpuConstants.Features |= LightFeature.Specular;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("no_alphalight")))
                            cpuConstants.Features |= LightFeature.NoAlphaLight;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("volume")) && cpuConstants.Type == LightType.Spot)
                            cpuConstants.Features |= LightFeature.Volume;
                        //if (entity.Strings.Get(ShortGuidUtils.Generate("gobo_texture")) && cpuConstants.Type == LightType.Spot)
                        //    cpuConstants.Features |= LightFeature.Gobo;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("cast_shadow")) && cpuConstants.Type == LightType.Spot)
                            cpuConstants.Features |= LightFeature.Shadow;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("is_square_light")) && cpuConstants.Type == LightType.Spot)
                            cpuConstants.Features |= LightFeature.SquareLight;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("distance_mip_selection_gobo")) && cpuConstants.Type == LightType.Spot)
                            cpuConstants.Features |= LightFeature.DistanceMipSelectionGobo;
                        float areaLightRadius = entity.Floats.Get(ShortGuidUtils.Generate("area_light_radius"));
                        if (areaLightRadius > 0.0001f)
                            cpuConstants.Features |= LightFeature.AreaLight;
                        cpuConstants.LightFadeType = (LightFadeType)entity.EnumIndexes.Get(ShortGuidUtils.Generate("fade_type"));
                        cpuConstants.FlareOccluderRadius = entity.Floats.Get(ShortGuidUtils.Generate("flare_occluder_radius"));
                        cpuConstants.FlareSpotOffset = entity.Floats.Get(ShortGuidUtils.Generate("flare_spot_offset"));
                        cpuConstants.DepthBias = entity.Floats.Get(ShortGuidUtils.Generate("depth_bias"));

                        DEFERRED_GPU_CONSTANTS gpuConstants = new DEFERRED_GPU_CONSTANTS();
                        float endAttenuation = entity.Floats.Get(ShortGuidUtils.Generate("end_attenuation"));
                        float startAttenuation = Math.Min(entity.Floats.Get(ShortGuidUtils.Generate("start_attenuation")), endAttenuation - 0.05f);
                        gpuConstants.AttenuationBegin = Math.Max(Math.Min(startAttenuation, endAttenuation), 0.00001f); //not sure if these start/ends are correct
                        gpuConstants.AttenuationEnd = Math.Max(Math.Min(startAttenuation, endAttenuation), 0.00001f);
                        Vector3 colour = entity.Vectors.Get(ShortGuidUtils.Generate("colour"));
                        gpuConstants.Colour = Math.Max(0.0f, entity.Floats.Get(ShortGuidUtils.Generate("intensity_multiplier"))) * new Vector3((float)MathsUtils.sRGBToLinear(colour.X / 255.0f), (float)MathsUtils.sRGBToLinear(colour.Y / 255.0f), (float)MathsUtils.sRGBToLinear(colour.Z / 255.0f));
                        if (cpuConstants.Features.HasFlag(LightFeature.PhysicalAttenuation))
                        {
                            gpuConstants.VolumeColour = gpuConstants.Colour;
                        }
                        else
                        {
                            gpuConstants.AttenuationDefocus = (gpuConstants.AttenuationBegin / 5.0f) * (gpuConstants.AttenuationBegin / 5.0f);
                            gpuConstants.VolumeColour = gpuConstants.AttenuationDefocus * gpuConstants.Colour;
                        }
                        if (cpuConstants.Features.HasFlag(LightFeature.Volume))
                        {
                            Vector3 volumeColourFactor = entity.Vectors.Get(ShortGuidUtils.Generate("volume_colour_factor"));
                            gpuConstants.VolumeColour *= new Vector3((float)MathsUtils.sRGBToLinear(volumeColourFactor.X / 255.0f), (float)MathsUtils.sRGBToLinear(volumeColourFactor.Y / 255.0f), (float)MathsUtils.sRGBToLinear(volumeColourFactor.Z / 255.0f));
                        }
                        gpuConstants.NearDist = Math.Min(entity.Floats.Get(ShortGuidUtils.Generate("near_dist")), gpuConstants.AttenuationEnd - 0.00001f);
                        gpuConstants.Softness = entity.Floats.Get(ShortGuidUtils.Generate("diffuse_softness"));
                        gpuConstants.DiffuseBias = entity.Floats.Get(ShortGuidUtils.Generate("diffuse_bias"));
                        gpuConstants.GlossinessScale = Math.Max(0.0f, Math.Min(1.0f, entity.Floats.Get(ShortGuidUtils.Generate("glossiness_scale"))));
                        gpuConstants.OuterAngle = (float)Math.Min(Math.Min(Math.Max(Math.Cos(MathsUtils.Deg2Rad(entity.Floats.Get(ShortGuidUtils.Generate("outer_cone_angle"))) / 2.0f), 0.0f), 1.0f), 0.999f);
                        gpuConstants.InnerAngle = (float)Math.Min(Math.Min(Math.Max(Math.Cos(MathsUtils.Deg2Rad(entity.Floats.Get(ShortGuidUtils.Generate("inner_cone_angle"))) / 2.0f), 0.0f), 1.0f), 0.999f);
                        if (!cpuConstants.Features.HasFlag(LightFeature.SquareLight))
                        {
                            gpuConstants.InnerAngle = Math.Min(Math.Max(gpuConstants.OuterAngle + 0.01f, gpuConstants.InnerAngle), 0.999f);
                        }
                        else
                        {
                            gpuConstants.InnerAngle = Math.Min(gpuConstants.OuterAngle + 0.01f, 0.999f);
                        }
                        gpuConstants.ArealightRadius = areaLightRadius;
                        gpuConstants.NearDistShadowOffset = entity.Floats.Get(ShortGuidUtils.Generate("near_dist_shadow_offset"));
                        gpuConstants.AspectRatio = cpuConstants.Features.HasFlag(LightFeature.SquareLight) ? Math.Max(entity.Floats.Get(ShortGuidUtils.Generate("aspect_ratio")), 0.001f) : 1.0f;
                        gpuConstants.VolumeDensity = entity.Floats.Get(ShortGuidUtils.Generate("volume_density"));
                        float volumeEndAttenuation = entity.Floats.Get(ShortGuidUtils.Generate("volume_end_attenuation"));
                        gpuConstants.VolumeAttenuationEnd = volumeEndAttenuation > 0.0f ? volumeEndAttenuation : entity.Floats.Get(ShortGuidUtils.Generate("end_attenuation"));
                    }
                    break;
                case FunctionType.ModelReference:
                    if (!isDeleted && !isRequiredAssets)
                    {
                        Parameter p = function.GetParameter("resource");
                        if (p?.content != null && p.content.dataType == DataType.RESOURCE)
                        {
                            cResource r = (cResource)p.content;
                            if (r.value.Count != 0)
                            {
                                AddResourceEntry(entity);
                            }
                        }
                    }
                    {
                        ResourceReference renderableInstance = function.GetResource(ResourceType.RENDERABLE_INSTANCE, true);
                        ResourceReference collisionMapping = function.GetResource(ResourceType.COLLISION_MAPPING, true);
                        if (collisionMapping?.CollisionMapping != null)
                        {
                            CollisionMaps.COLLISION_MAPPING newMap = new CollisionMaps.COLLISION_MAPPING()
                            {
                                Flags = collisionMapping.CollisionMapping.Flags,
                                Index = collisionMapping.CollisionMapping.Index, //seems like this index is always -1 on the generic ones too, which i should update to the actual index
                                ResourceGUID = collisionMapping.CollisionMapping.ResourceGUID,
                                Entity = new EntityHandle()
                                {
                                    composite_instance_id = entity.ThisCompositeInstance.InstanceID,
                                    entity_id = entity.Entity.shortGUID
                                },
                                Material = collisionMapping.CollisionMapping.Material,
                                CollisionProxyIndex = collisionMapping.CollisionMapping.CollisionProxyIndex,
                                MaterialMapping = collisionMapping.CollisionMapping.MaterialMapping, //this is tricky
                                ZoneID = collisionMapping.CollisionMapping.ZoneID //need to work this out
                            };
                            lock (_collisionMapsLock)
                            {
                                if (_level.CollisionMaps.Entries.FirstOrDefault(o => o.Entity.entity_id == collisionMapping.CollisionMapping.Entity.entity_id) == null)
                                    _level.CollisionMaps.Entries.Add(collisionMapping.CollisionMapping); 
                                if (!isDeleted /*&& !!deleteStandardCollision && !isTemplate && !isRequiredAssets*/) 
                                    _level.CollisionMaps.Entries.Add(newMap);
                            }
                        }
                    }

                    {
                        // if material is CA_ENVIRONMENT
                        ENVIRONMENT_GPU_CONSTANTS gpuConstants = new ENVIRONMENT_GPU_CONSTANTS();
                        Vector3 vertColourScale = entity.Vectors.Get(ShortGuidUtils.Generate("vertex_colour_scale"));
                        gpuConstants.VertexColourScalars = new Vector4(vertColourScale.X, vertColourScale.Y, vertColourScale.Z, entity.Floats.Get(ShortGuidUtils.Generate("vertex_opacity_scale")));
                        Vector3 diffColourScale = entity.Vectors.Get(ShortGuidUtils.Generate("diffuse_colour_scale")) / 255.0f;
                        gpuConstants.DiffuseColourScalars = new Vector4(vertColourScale.X, vertColourScale.Y, vertColourScale.Z, entity.Floats.Get(ShortGuidUtils.Generate("diffuse_opacity_scale")));
                        gpuConstants.AlphaBlendNoisePowerScale = entity.Floats.Get(ShortGuidUtils.Generate("alpha_blend_noise_power_scale"));
                        gpuConstants.AlphaBlendNoiseUvScale = entity.Floats.Get(ShortGuidUtils.Generate("alpha_blend_noise_uv_scale"));
                        gpuConstants.AlphaBlendNoiseUvOffset = new Vector2(entity.Floats.Get(ShortGuidUtils.Generate("alpha_blend_noise_uv_offset_X")), entity.Floats.Get(ShortGuidUtils.Generate("alpha_blend_noise_uv_offset_Y")));
                        gpuConstants.DirtMultiplyBlendSpecPowerScale = entity.Floats.Get(ShortGuidUtils.Generate("dirt_multiply_blend_spec_power_scale"));
                        gpuConstants.DirtMapUvScale = entity.Floats.Get(ShortGuidUtils.Generate("dirt_map_uv_scale"));
                    }

                    {
                        // if material is CA_LIGHT_DECAL
                        LIGHTDECAL_GPU_CONSTANTS gpuConstants = new LIGHTDECAL_GPU_CONSTANTS();
                        Vector3 tint = entity.Vectors.Get(ShortGuidUtils.Generate("lightdecal_tint")) / 255.0f;
                        float intensity = entity.Floats.Get(ShortGuidUtils.Generate("lightdecal_intensity"));
                        gpuConstants.LightdecalIntensity = new Vector3((float)MathsUtils.sRGBToLinear(tint.X), (float)MathsUtils.sRGBToLinear(tint.Y), (float)MathsUtils.sRGBToLinear(tint.Z)) * intensity;
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
                            Entity = new EntityHandle()
                            {
                                composite_instance_id = entity.ThisCompositeInstance.InstanceID,
                                entity_id = entity.Entity.shortGUID
                            },
                        };
                        lock (_collisionMapsLock)
                        {
                            if (!isTemplate && !isRequiredAssets)
                                _level.CollisionMaps.Entries.Add(newMap);
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
                        bool uniqueMaterial = entity.Bools.Get(ShortGuidUtils.Generate("unique_material"));
                        //string material = entity.Strings.Get(ShortGuidUtils.Generate("material"));

                        if (!isDeleted && !isTemplate && !isRequiredAssets)
                            AddResourceEntry(entity);

                        if (entity.Bools.Get(ShortGuidUtils.Generate("CPU")))
                        {
                            //model is CPU_PARTICLE_MODEL

                            DYNAMIC_FX_GPU_CONSTANTS gpuConstants = new DYNAMIC_FX_GPU_CONSTANTS();
                            //gpuConstants.RandomNumber <- random number, is this important?
                            gpuConstants.ExpiryTime = entity.Floats.Get(ShortGuidUtils.Generate("SYSTEM_EXPIRY_TIME"));

                            DYNAMIC_PFX_PARAMS cpuConstants = new DYNAMIC_PFX_PARAMS();
                            cpuConstants.DrawPass = entity.Integers.Get(ShortGuidUtils.Generate("DRAW_PASS"));
                            cpuConstants.EntityGuid = entity.Entity.shortGUID;
                            cpuConstants.ParentGuid = entity.ParentCompositeInstance.InstanceID; //todo - is this correct? i think i should replace this whole thing with entity handle.
                        }
                        else
                        {
                            //model is 1000_PARTICLE_CUBE

                            PARTICLE_GPU_CONSTANTS gpuConstants = new PARTICLE_GPU_CONSTANTS();
                            //gpuConstants.RandomNumber <- random number
                            gpuConstants.ExpiryTime = entity.Floats.Get(ShortGuidUtils.Generate("SYSTEM_EXPIRY_TIME"));
                            gpuConstants.AspectRatio = entity.Floats.Get(ShortGuidUtils.Generate("ASPECT_RATIO"));
                            gpuConstants.FadeAtDistance = entity.Floats.Get(ShortGuidUtils.Generate("FADE_AT_DISTANCE"));
                            gpuConstants.AlphaIn = entity.Floats.Get(ShortGuidUtils.Generate("ALPHA_IN")) * 0.01f;
                            gpuConstants.AlphaOut = entity.Floats.Get(ShortGuidUtils.Generate("ALPHA_OUT")) * 0.01f;
                            gpuConstants.AlphaRefValue = entity.Floats.Get(ShortGuidUtils.Generate("ALPHA_REF_VALUE"));
                            gpuConstants.SizeStartMin = entity.Floats.Get(ShortGuidUtils.Generate("SIZE_START_MIN"));
                            gpuConstants.SizeStartMax = entity.Floats.Get(ShortGuidUtils.Generate("SIZE_START_MAX"));
                            gpuConstants.SizeEndMin = entity.Floats.Get(ShortGuidUtils.Generate("SIZE_END_MIN"));
                            gpuConstants.SizeEndMax = entity.Floats.Get(ShortGuidUtils.Generate("SIZE_END_MAX"));
                            gpuConstants.MaskAmountMin = entity.Floats.Get(ShortGuidUtils.Generate("MASK_AMOUNT_MIN"));
                            gpuConstants.MaskAmountMax = entity.Floats.Get(ShortGuidUtils.Generate("MASK_AMOUNT_MAX"));
                            gpuConstants.MaskAmountMidpoint = entity.Floats.Get(ShortGuidUtils.Generate("MASK_AMOUNT_MIDPOINT"));
                            gpuConstants.ColourScaleMin = entity.Floats.Get(ShortGuidUtils.Generate("COLOUR_SCALE_MIN"));
                            gpuConstants.ColourScaleMax = entity.Floats.Get(ShortGuidUtils.Generate("COLOUR_SCALE_MAX"));
                            gpuConstants.ParticleExpiryTimeMin = entity.Floats.Get(ShortGuidUtils.Generate("PARTICLE_EXPIRY_TIME_MIN"));
                            gpuConstants.ParticleExpiryTimeMax = entity.Floats.Get(ShortGuidUtils.Generate("PARTICLE_EXPIRY_TIME_MAX"));
                            gpuConstants.Wind = new Vector3(entity.Floats.Get(ShortGuidUtils.Generate("WIND_X")), entity.Floats.Get(ShortGuidUtils.Generate("WIND_Y")), entity.Floats.Get(ShortGuidUtils.Generate("WIND_Z")));

                            PARTICLE_PARAMS cpuConstants = new PARTICLE_PARAMS();
                            int particleCount = entity.Integers.Get(ShortGuidUtils.Generate("PARTICLE_COUNT"));
                            cpuConstants.NumVerts = 2 * particleCount * 4;
                            cpuConstants.PrimitiveCount = 2 * particleCount;
                            cpuConstants.VertexOffset = (int)(gpuConstants.RandomNumber * (float)(1000 - particleCount)) * 4;
                            cpuConstants.DrawPass = entity.Integers.Get(ShortGuidUtils.Generate("DRAW_PASS"));
                            cpuConstants.BoundingBoxMax = entity.Vectors.Get(ShortGuidUtils.Generate("bounds_max"));
                            cpuConstants.BoundingBoxMin = entity.Vectors.Get(ShortGuidUtils.Generate("bounds_min"));
                            cpuConstants.EntityGuid = entity.Entity.shortGUID;
                            cpuConstants.ParentGuid = entity.ParentCompositeInstance.InstanceID; //todo - is this correct? i think i should replace this whole thing with entity handle.
                        }
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
                        if (physicsSystem == null || physicsSystem.PhysicsSystemIndex == -1) //todo - this also maps to the index parameter, should consolidate!
                        {
                            //Should warn here!
                            break;
                        }

                        (Vector3 position, Quaternion rotation) = entity.CalculateWorldPositionRotation();
                        lock (_physicsMapsLock)
                        {
                            _level.PhysicsMaps.Entries.Add(new PhysicsMaps.DYNAMIC_PHYSICS_SYSTEM()
                            {
                                physics_system_index = physicsSystem.PhysicsSystemIndex,
                                composite_instance_id = entity.ThisCompositeInstance.InstanceID,
                                entity = new EntityHandle()
                                {
                                    entity_id = entity.ParentCompositeInstanceEntity.Entity.shortGUID,
                                    composite_instance_id = entity.ParentCompositeInstance.InstanceID
                                },
                                Position = position,
                                Rotation = rotation
                            });
                        }

                        AddResourceEntry(entity);
                    }
                    break;
                case FunctionType.ProjectiveDecal:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
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
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    {
                        DYNAMIC_FX_GPU_CONSTANTS gpuConstants = new DYNAMIC_FX_GPU_CONSTANTS();
                        //gpuConstants.RandomNumber <- generate one
                        gpuConstants.ExpiryTime = entity.Floats.Get(ShortGuidUtils.Generate("SYSTEM_EXPIRY_TIME"));

                        DYNAMIC_PFX_PARAMS cpuConstants = new DYNAMIC_PFX_PARAMS();
                        cpuConstants.DrawPass = entity.Integers.Get(ShortGuidUtils.Generate("DRAW_PASS"));
                        cpuConstants.EntityGuid = entity.Entity.shortGUID;
                        cpuConstants.ParentGuid = entity.ParentCompositeInstance.InstanceID; //todo - is this correct? i think i should replace this whole thing with entity handle.
                    }
                    break;
                case FunctionType.SimpleRefraction:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    {
                        CA_SIMPLE_REFRACTION.FEATURES features = 0;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("SECONDARY_NORMAL_MAPPING")))
                            features |= CA_SIMPLE_REFRACTION.FEATURES.SECONDARY_NORMAL_MAPPING;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("ALPHA_MASKING")))
                            features |= CA_SIMPLE_REFRACTION.FEATURES.ALPHA_MASKING;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("DISTORTION_OCCLUSION")))
                            features |= CA_SIMPLE_REFRACTION.FEATURES.DISTORTION_OCCLUSION;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("FLOW_UV_ANIMATION")))
                            features |= CA_SIMPLE_REFRACTION.FEATURES.FLOW_UV_ANIMATION;
                    }
                    break;
                case FunctionType.SimpleWater:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    {
                        CA_SIMPLEWATER.FEATURES features = 0;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("SECONDARY_NORMAL_MAPPING")))
                            features |= CA_SIMPLEWATER.FEATURES.SECONDARY_NORMAL_MAPPING;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("LOW_RES_ALPHA_PASS")))
                            features |= CA_SIMPLEWATER.FEATURES.LOW_RES_ALPHA_PASS;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("ALPHA_MASKING")))
                            features |= CA_SIMPLEWATER.FEATURES.ALPHA_MASKING;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("FLOW_MAPPING")))
                            features |= CA_SIMPLEWATER.FEATURES.FLOW_UV_ANIMATION;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("ENVIRONMENT_MAPPING")))
                            features |= CA_SIMPLEWATER.FEATURES.ENVIRONMENT_MAPPING;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("LOCALISED_ENVIRONMENT_MAPPING")))
                            features |= CA_SIMPLEWATER.FEATURES.LOCALISED_ENVIRONMENT_MAPPING;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("LOCALISED_ENVMAP_BOX_PROJECTION")))
                            features |= CA_SIMPLEWATER.FEATURES.LOCALISED_ENVMAP_BOX_PROJECTION;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("REFLECTIVE_MAPPING")))
                            features |= CA_SIMPLEWATER.FEATURES.REFLECTIVE_MAPPING;
                    }
                    break;
                case FunctionType.SoundBarrier:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    if (!isDeleted && !isTemplate)
                    {
                        CollisionMaps.COLLISION_MAPPING newMap = new CollisionMaps.COLLISION_MAPPING()
                        {
                            Entity = new EntityHandle()
                            {
                                composite_instance_id = entity.ThisCompositeInstance.InstanceID,
                                entity_id = entity.Entity.shortGUID
                            },
                        };
                        lock (_collisionMapsLock)
                        {
                            if (!isTemplate && !isRequiredAssets)
                                _level.CollisionMaps.Entries.Add(newMap);
                        }
                    }
                    break;
                case FunctionType.SoundEnvironmentMarker:

                    break;
                case FunctionType.SoundLevelInitialiser:

                    break;
                case FunctionType.SoundNetworkNode:

                    break;
                case FunctionType.SpottingExclusionArea:

                    break;
                case FunctionType.SurfaceEffectBox:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    {
                        CA_EFFECT_OVERLAY.FEATURES features = CA_EFFECT_OVERLAY.FEATURES.BOX;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("WS_LOCKED")))
                            features |= CA_EFFECT_OVERLAY.FEATURES.WS_LOCKED;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("ENVMAP")))
                            features |= CA_EFFECT_OVERLAY.FEATURES.ENVMAP;
                    }
                    break;
                case FunctionType.SurfaceEffectSphere:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    {
                        CA_EFFECT_OVERLAY.FEATURES features = CA_EFFECT_OVERLAY.FEATURES.SPHERE;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("WS_LOCKED")))
                            features |= CA_EFFECT_OVERLAY.FEATURES.WS_LOCKED;
                        if (entity.Bools.Get(ShortGuidUtils.Generate("ENVMAP")))
                            features |= CA_EFFECT_OVERLAY.FEATURES.ENVMAP;
                    }
                    break;
                case FunctionType.TRAV_1ShotSpline:
                    if (!isDeleted && !isTemplate && !isRequiredAssets)
                        AddResourceEntry(entity);
                    break;
                case FunctionType.Zone:

                    break;
            }
        }

        private void AddResourceEntry(InstancedEntity entity)
        {
            lock (_resourcesLock)
            {
                //NOTE: Because of 'is_shared', we get some differences with added resources instance IDs, since the first hit (which may differ) is always the one that's written, but hopefully that's fine.
                _level.Resources.AddUniqueResource(GetResourceID(entity), entity.ThisCompositeInstance.InstanceID);
            }
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