using CATHODE;
using CATHODE.Enums;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace CathodeLib
{
    internal static class ShortGuids
    {
        public static readonly ShortGuid GUID_DYNAMIC_PHYSICS_SYSTEM = ShortGuidUtils.Generate("DYNAMIC_PHYSICS_SYSTEM");
        public static readonly ShortGuid Reference = ShortGuidUtils.Generate("reference");
        public static readonly ShortGuid Position = ShortGuidUtils.Generate("position");
        public static readonly ShortGuid DoorMechanism = ShortGuidUtils.Generate("door_mechanism");
        public static readonly ShortGuid ButtonType = ShortGuidUtils.Generate("button_type");
        public static readonly ShortGuid LeverType = ShortGuidUtils.Generate("lever_type");
        public static readonly ShortGuid IsDoor = ShortGuidUtils.Generate("is_door");
        public static readonly ShortGuid Filter = ShortGuidUtils.Generate("filter");
        public static readonly ShortGuid Input = ShortGuidUtils.Generate("Input");
        public static readonly ShortGuid LHS = ShortGuidUtils.Generate("LHS");
        public static readonly ShortGuid RHS = ShortGuidUtils.Generate("RHS");
        public static readonly ShortGuid Threshold = ShortGuidUtils.Generate("Threshold");
        public static readonly ShortGuid Min = ShortGuidUtils.Generate("Min");
        public static readonly ShortGuid Max = ShortGuidUtils.Generate("Max");
        public static readonly ShortGuid Value = ShortGuidUtils.Generate("Value");
        public static readonly ShortGuid InitialValue = ShortGuidUtils.Generate("Initial_Value");
        public static readonly ShortGuid TargetValue = ShortGuidUtils.Generate("Target_Value");
        public static readonly ShortGuid Proportion = ShortGuidUtils.Generate("Proportion");
        public static readonly ShortGuid Numbers = ShortGuidUtils.Generate("Numbers");
        public static readonly ShortGuid Bias = ShortGuidUtils.Generate("bias");
        public static readonly ShortGuid Amplitude = ShortGuidUtils.Generate("amplitude");
        public static readonly ShortGuid Phase = ShortGuidUtils.Generate("phase");
        public static readonly ShortGuid WaveShape = ShortGuidUtils.Generate("wave_shape");
        public static readonly ShortGuid Allow = ShortGuidUtils.Generate("allow");
        public static readonly ShortGuid InitialValueLower = ShortGuidUtils.Generate("initial_value");
        public static readonly ShortGuid NextGen = ShortGuidUtils.Generate("NextGen");
        public static readonly ShortGuid Colour = ShortGuidUtils.Generate("Colour");
        public static readonly ShortGuid X = ShortGuidUtils.Generate("x");
        public static readonly ShortGuid Y = ShortGuidUtils.Generate("y");
        public static readonly ShortGuid Z = ShortGuidUtils.Generate("z");
        public static readonly ShortGuid InitialColour = ShortGuidUtils.Generate("initial_colour");
        public static readonly ShortGuid InitialX = ShortGuidUtils.Generate("initial_x");
        public static readonly ShortGuid InitialY = ShortGuidUtils.Generate("initial_y");
        public static readonly ShortGuid InitialZ = ShortGuidUtils.Generate("initial_z");
        public static readonly ShortGuid Normalised = ShortGuidUtils.Generate("Normalised");
        public static readonly ShortGuid MinX = ShortGuidUtils.Generate("MinX");
        public static readonly ShortGuid MaxX = ShortGuidUtils.Generate("MaxX");
        public static readonly ShortGuid MinY = ShortGuidUtils.Generate("MinY");
        public static readonly ShortGuid MaxY = ShortGuidUtils.Generate("MaxY");
        public static readonly ShortGuid MinZ = ShortGuidUtils.Generate("MinZ");
        public static readonly ShortGuid MaxZ = ShortGuidUtils.Generate("MaxZ");
        public static readonly ShortGuid IsTemplate = ShortGuidUtils.Generate("is_template");
        public static readonly ShortGuid Deleted = ShortGuidUtils.Generate("deleted");
    }

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
                    existingLinks.AddRange(parentLinks); //todo - probs want to insert first?
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

        public Parameters<bool> Bools;
        public Parameters<int> Integers;
        public Parameters<float> Floats;
        public Parameters<int> EnumIndexes;
        public Parameters<Vector3> Vectors;
        public Parameters<Transform> Transforms;

        public Level Level;
        public Entity Entity;
        public EntityPath Path;
        public Composite Composite;

        //TODO: also load in MVR etc here?

        //The composite and entity one step back in the path, responsible for creating this instance: will be null if at root
        public InstancedEntity ParentCompositeInstanceEntity;
        public InstancedComposite ParentCompositeInstance;

        //The current composite instance
        public InstancedComposite ThisCompositeInstance;

        //The composite instanced by this entity, one step forward in the path: will be null if this doesn't instance one
        public InstancedComposite ChildCompositeInstance;

        private HashSet<(ShortGuid, ParameterVariant, DataType)> _parameters;

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

            int paramCount = parameters.Count;
            _parameters = new HashSet<(ShortGuid, ParameterVariant, DataType)>();
            Bools = new Parameters<bool>(paramCount);
            Integers = new Parameters<int>(paramCount);
            Floats = new Parameters<float>(paramCount);
            EnumIndexes = new Parameters<int>(paramCount);
            Vectors = new Parameters<Vector3>(paramCount);
            Transforms = new Parameters<Transform>(paramCount);
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
                                case DataType.STRING:
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
                                case DataType.STRING:
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
                                case DataType.STRING:
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
                                case DataType.STRING:
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
            ShortGuid guid = name == "reference" ? ShortGuids.Reference : ShortGuidUtils.Generate(name);
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
                if (Transforms.Has(ShortGuids.Position))
                    return (T)(object)Transforms.Get(ShortGuids.Position);
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
            if (guid != ShortGuids.Reference)
            {
                //Get the value of the parameter, taking in to account anything applied by to the instance
                if (typeof(T) == typeof(bool))
                    return (T)(object)Bools.Get(guid);
                else if (typeof(T) == typeof(int))
                {
                    if (Integers.Has(guid))
                        return (T)(object)Integers.Get(guid);
                    else
                        return (T)(object)EnumIndexes.Get(guid);
                }
                else if (typeof(T) == typeof(float))
                    return (T)(object)Floats.Get(guid);
                else if (typeof(T) == typeof(Vector3))
                    return (T)(object)Vectors.Get(guid);
                else if (typeof(T) == typeof(Transform))
                    return (T)(object)Transforms.Get(guid);
            }
            else
            {
                //Calculate the reference value based on the entity's internal logic
                switch (type)
                {
                    case FunctionType.Character:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.Checkpoint:
                        if (typeof(T) == typeof(string))
                            return (T)(object)"";
                        break;
                    case FunctionType.CoverExclusionArea:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.DeleteBlankPanel:
                        if (typeof(T) == typeof(bool))
                        {
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.DoorMechanism);
                            switch (door_mechanism)
                            {
                                case DOOR_MECHANISM.BLANK:
                                    return (T)(object)false;
                                default:
                                    return (T)(object)true;
                            }
                        }
                        break;
                    case FunctionType.DeleteButtonDisk:
                        if (typeof(T) == typeof(bool))
                        {
                            BUTTON_TYPE button_type = (BUTTON_TYPE)EnumIndexes.Get(ShortGuids.ButtonType);
                            if (button_type != BUTTON_TYPE.DISK) return (T)(object)true;

                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.DoorMechanism);
                            switch (door_mechanism)
                            {
                                case DOOR_MECHANISM.HIDDEN_BUTTON:
                                case DOOR_MECHANISM.BUTTON:
                                    return (T)(object)false;
                                default:
                                    return (T)(object)true;
                            }
                        }
                        break;
                    case FunctionType.DeleteButtonKeys:
                        if (typeof(T) == typeof(bool))
                        {
                            BUTTON_TYPE button_type = (BUTTON_TYPE)EnumIndexes.Get(ShortGuids.ButtonType);
                            if (button_type != BUTTON_TYPE.KEYS) return (T)(object)true;

                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.DoorMechanism);
                            switch (door_mechanism)
                            {
                                case DOOR_MECHANISM.HIDDEN_BUTTON:
                                case DOOR_MECHANISM.BUTTON:
                                    return (T)(object)false;
                                default:
                                    return (T)(object)true;
                            }
                        }
                        break;
                    case FunctionType.DeleteCuttingPanel:
                        if (typeof(T) == typeof(bool))
                        {
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.DoorMechanism);
                            switch (door_mechanism)
                            {
                                case DOOR_MECHANISM.HIDDEN_BUTTON:
                                case DOOR_MECHANISM.HIDDEN_KEYPAD:
                                case DOOR_MECHANISM.HIDDEN_HACKING:
                                case DOOR_MECHANISM.HIDDEN_LEVER:
                                    return (T)(object)false;
                                default:
                                    return (T)(object)true;
                            }
                        }
                        break;
                    case FunctionType.DeleteHacking:
                        if (typeof(T) == typeof(bool))
                        {
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.DoorMechanism);
                            switch (door_mechanism)
                            {
                                case DOOR_MECHANISM.HACKING:
                                case DOOR_MECHANISM.HIDDEN_HACKING:
                                    return (T)(object)false;
                                default:
                                    return (T)(object)true;
                            }
                        }
                        break;
                    case FunctionType.DeleteHousing:
                        if (typeof(T) == typeof(bool))
                        {
                            if (!Bools.Get(ShortGuids.IsDoor)) return (T)(object)true;

                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.DoorMechanism);
                            switch (door_mechanism)
                            {
                                case DOOR_MECHANISM.HIDDEN_BUTTON:
                                case DOOR_MECHANISM.HIDDEN_KEYPAD:
                                case DOOR_MECHANISM.HIDDEN_HACKING:
                                case DOOR_MECHANISM.HIDDEN_LEVER:
                                case DOOR_MECHANISM.BUTTON:
                                case DOOR_MECHANISM.KEYPAD:
                                case DOOR_MECHANISM.HACKING:
                                case DOOR_MECHANISM.LEVER:
                                    return (T)(object)false;
                                default:
                                    return (T)(object)true;
                            }
                        }
                        break;
                    case FunctionType.DeleteKeypad:
                        if (typeof(T) == typeof(bool))
                        {
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.DoorMechanism);
                            switch (door_mechanism)
                            {
                                case DOOR_MECHANISM.KEYPAD:
                                case DOOR_MECHANISM.HIDDEN_KEYPAD:
                                    return (T)(object)false;
                                default:
                                    return (T)(object)true;
                            }
                        }
                        break;
                    case FunctionType.DeletePullLever:
                        if (typeof(T) == typeof(bool))
                        {
                            LEVER_TYPE lever_type = (LEVER_TYPE)EnumIndexes.Get(ShortGuids.LeverType);
                            if (lever_type != LEVER_TYPE.PULL) return (T)(object)true;

                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.DoorMechanism);
                            switch (door_mechanism)
                            {
                                case DOOR_MECHANISM.HIDDEN_LEVER:
                                case DOOR_MECHANISM.LEVER:
                                    return (T)(object)false;
                                default:
                                    return (T)(object)true;
                            }
                        }
                        break;
                    case FunctionType.DeleteRotateLever:
                        if (typeof(T) == typeof(bool))
                        {
                            LEVER_TYPE lever_type = (LEVER_TYPE)EnumIndexes.Get(ShortGuids.LeverType);
                            if (lever_type != LEVER_TYPE.ROTATE) return (T)(object)true;

                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get(ShortGuids.DoorMechanism);
                            switch (door_mechanism)
                            {
                                case DOOR_MECHANISM.HIDDEN_LEVER:
                                case DOOR_MECHANISM.LEVER:
                                    return (T)(object)false;
                                default:
                                    return (T)(object)true;
                            }
                        }
                        break;
                    case FunctionType.DoorStatus:
                        if (typeof(T) == typeof(int))
                            return (T)(object)0;
                        break;
                    case FunctionType.FilterAnd:
                        if (typeof(T) == typeof(bool))
                        {
                            List<InstancedEntity> filters = Bools.GetLinks(ShortGuids.Filter);
                            for (int i = 0; i < filters.Count; i++)
                            {
                                if (!filters[i].GetAs<bool>())
                                    return (T)(object)false;
                            }
                            return (T)(object)true;
                        }
                        break;
                    case FunctionType.FilterNot:
                        if (typeof(T) == typeof(bool))
                        {
                            List<InstancedEntity> filters = Bools.GetLinks(ShortGuids.Filter);
                            return (T)(object)(filters.Count == 0 ? true : filters[0].GetAs<bool>());
                        }
                        break;
                    case FunctionType.FilterOr:
                        if (typeof(T) == typeof(bool))
                        {
                            List<InstancedEntity> filters = Bools.GetLinks(ShortGuids.Filter);
                            for (int i = 0; i < filters.Count; i++)
                            {
                                if (filters[i].GetAs<bool>())
                                    return (T)(object)true;
                            }
                            return (T)(object)false;
                        }
                        break;
                    case FunctionType.FloatAbsolute:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Math.Abs(Floats.Get(ShortGuids.Input));
                        break;
                    case FunctionType.FloatAdd:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(Floats.Get(ShortGuids.LHS) + Floats.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.FloatAdd_All:
                        if (typeof(T) == typeof(float))
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks(ShortGuids.Numbers);
                            float sum = 0;
                            for (int i = 0; i < numbers.Count; i++)
                                sum += numbers[i].GetAs<float>();
                            return (T)(object)sum;
                        }
                        break;
                    case FunctionType.FloatClamp:
                        if (typeof(T) == typeof(float))
                        {
                            float val = Floats.Get(ShortGuids.Value);
                            float min = Floats.Get(ShortGuids.Min);
                            float max = Floats.Get(ShortGuids.Max);
                            if (val < min) val = min;
                            if (val > max) val = max;
                            return (T)(object)val;
                        }
                        break;
                    case FunctionType.FloatClampMultiply:
                        if (typeof(T) == typeof(float))
                        {
                            float val = Floats.Get(ShortGuids.LHS);
                            float min = Floats.Get(ShortGuids.Min);
                            float max = Floats.Get(ShortGuids.Max) * Floats.Get(ShortGuids.RHS);
                            if (val < min) val = min;
                            if (val > max) val = max;
                            return (T)(object)val;
                        }
                        break;
                    case FunctionType.FloatDivide:
                        if (typeof(T) == typeof(float))
                        {
                            float rhs = Floats.Get(ShortGuids.RHS);
                            if (Math.Abs(rhs) < 0.0001f) return (T)(object)0.0f;
                            return (T)(object)(Floats.Get(ShortGuids.LHS) / rhs);
                        }
                        break;
                    case FunctionType.FloatEquals:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Math.Abs(Floats.Get(ShortGuids.LHS) - Floats.Get(ShortGuids.RHS)) < Math.Abs(Floats.Get(ShortGuids.Threshold)));
                        break;
                    case FunctionType.FloatGetLinearProportion:
                        if (typeof(T) == typeof(float))
                        {
                            float min = Floats.Get(ShortGuids.Min);
                            float max = Floats.Get(ShortGuids.Max);
                            float mid = Floats.Get(ShortGuids.Input);
                            return (T)(object)((mid - min) / (max - min));
                        }
                        break;
                    case FunctionType.FloatGreaterThan:
                        if (typeof(T) == typeof(bool))
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float threshold = Floats.Get(ShortGuids.Threshold);
                            if (Math.Abs(lhs - rhs) < threshold) return (T)(object)false;
                            return (T)(object)(lhs > rhs);
                        }
                        break;
                    case FunctionType.FloatGreaterThanOrEqual:
                        if (typeof(T) == typeof(bool))
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float threshold = Floats.Get(ShortGuids.Threshold);
                            if (Math.Abs(lhs - rhs) < threshold) return (T)(object)true;
                            return (T)(object)(lhs > rhs);
                        }
                        break;
                    case FunctionType.FloatLessThan:
                        if (typeof(T) == typeof(bool))
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float threshold = Floats.Get(ShortGuids.Threshold);
                            if (Math.Abs(lhs - rhs) < threshold) return (T)(object)false;
                            return (T)(object)(lhs < rhs);
                        }
                        break;
                    case FunctionType.FloatLessThanOrEqual:
                        if (typeof(T) == typeof(bool))
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            float threshold = Floats.Get(ShortGuids.Threshold);
                            if (Math.Abs(lhs - rhs) < threshold) return (T)(object)true;
                            return (T)(object)(lhs < rhs);
                        }
                        break;
                    case FunctionType.FloatLinearInterpolateSpeed:
                    case FunctionType.FloatLinearInterpolateTimed:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Floats.Get(ShortGuids.InitialValue);
                        break;
                    case FunctionType.FloatLinearProportion:
                        if (typeof(T) == typeof(float))
                        {
                            float min = Floats.Get(ShortGuids.InitialValue);
                            float max = Floats.Get(ShortGuids.TargetValue);
                            return (T)(object)(min + (max - min) * Floats.Get(ShortGuids.Proportion));
                        }
                        break;
                    case FunctionType.FloatMax:
                        if (typeof(T) == typeof(float))
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            if (lhs > rhs) return (T)(object)lhs;
                            return (T)(object)rhs;
                        }
                        break;
                    case FunctionType.FloatMax_All:
                        if (typeof(T) == typeof(float))
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks(ShortGuids.Numbers);
                            float max = 0;
                            for (int i = 0; i < numbers.Count; i++)
                            {
                                float number = numbers[i].GetAs<float>();
                                if (max < number) max = number;
                            }
                            return (T)(object)max;
                        }
                        break;
                    case FunctionType.FloatMin:
                        if (typeof(T) == typeof(float))
                        {
                            float lhs = Floats.Get(ShortGuids.LHS);
                            float rhs = Floats.Get(ShortGuids.RHS);
                            if (lhs < rhs) return (T)(object)lhs;
                            return (T)(object)rhs;
                        }
                        break;
                    case FunctionType.FloatMin_All:
                        if (typeof(T) == typeof(float))
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
                            return (T)(object)min;
                        }
                        break;
                    case FunctionType.FloatModulate:
                        if (typeof(T) == typeof(float))
                        {
                            float PI = 3.1415926535897932333797165867879296635503123989707390137482903185973555f;

                            float offset = Floats.Get(ShortGuids.Bias);
                            float amplitude = Floats.Get(ShortGuids.Amplitude);

                            float phase = Floats.Get(ShortGuids.Phase) / 360.0f;
                            float output = phase % 1.0f;

                            WAVE_SHAPE wave_shape = (WAVE_SHAPE)EnumIndexes.Get(ShortGuids.WaveShape);
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
                            return (T)(object)(offset + amplitude * output);
                        }
                        break;
                    case FunctionType.FloatModulateRandom:
                        if (typeof(T) == typeof(float))
                            return (T)(object)0.0f;
                        break;
                    case FunctionType.FloatMultiply:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(Floats.Get(ShortGuids.LHS) * Floats.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.FloatMultiply_All:
                        if (typeof(T) == typeof(float))
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks(ShortGuids.Numbers);
                            float sum = 0;
                            if (numbers.Count > 0)
                            {
                                sum = numbers[0].GetAs<float>();
                                for (int i = 1; i < numbers.Count; i++)
                                    sum *= numbers[i].GetAs<float>();
                            }
                            return (T)(object)sum;
                        }
                        break;
                    case FunctionType.FloatMultiplyClamp:
                        if (typeof(T) == typeof(float))
                        {
                            float val = Floats.Get(ShortGuids.LHS) * Floats.Get(ShortGuids.RHS);
                            float min = Floats.Get(ShortGuids.Min);
                            float max = Floats.Get(ShortGuids.Max);
                            if (val < min) val = min;
                            if (val > max) val = max;
                            return (T)(object)val;
                        }
                        break;
                    case FunctionType.FloatNotEqual:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)!(Math.Abs(Floats.Get(ShortGuids.LHS) - Floats.Get(ShortGuids.RHS)) < Math.Abs(Floats.Get(ShortGuids.Threshold)));
                        break;
                    case FunctionType.FloatReciprocal:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(1.0f / Floats.Get(ShortGuids.Input));
                        break;
                    case FunctionType.FloatRemainder:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(Floats.Get(ShortGuids.LHS) % Floats.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.FloatSqrt:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(float)Math.Sqrt(Math.Abs(Floats.Get(ShortGuids.Input)));
                        break;
                    case FunctionType.FloatSubtract:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(Floats.Get(ShortGuids.LHS) - Floats.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.GetGatingToolLevel:
                        if (typeof(T) == typeof(int))
                            return (T)(object)0;
                        break;
                    case FunctionType.GetPlayerHasGatingTool:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)false;
                        break;
                    case FunctionType.GetPlayerHasKeycard:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)false;
                        break;
                    case FunctionType.GetRotation:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Transforms.Get(ShortGuids.Input).Rotation;
                        break;
                    case FunctionType.GetTranslation:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Transforms.Get(ShortGuids.Input).Position;
                        break;
                    case FunctionType.GetX:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Vectors.Get(ShortGuids.Input).X;
                        break;
                    case FunctionType.GetY:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Vectors.Get(ShortGuids.Input).Y;
                        break;
                    case FunctionType.GetZ:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Vectors.Get(ShortGuids.Input).Z;
                        break;
                    case FunctionType.HasAccessAtDifficulty:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)false;
                        break;
                    case FunctionType.IntegerAbsolute:
                        if (typeof(T) == typeof(int))
                            return (T)(object)Math.Abs(Integers.Get(ShortGuids.Input));
                        break;
                    case FunctionType.IntegerAdd:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) + Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerAdd_All:
                        if (typeof(T) == typeof(int))
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks(ShortGuids.Numbers);
                            int sum = 0;
                            for (int i = 0; i < numbers.Count; i++)
                                sum += numbers[i].GetAs<int>();
                            return (T)(object)sum;
                        }
                        break;
                    case FunctionType.IntegerAnd:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) & Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerCompliment:
                        if (typeof(T) == typeof(int))
                            return (T)(object)~Integers.Get(ShortGuids.Input);
                        break;
                    case FunctionType.IntegerDivide:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) / Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerEquals:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) == Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerGreaterThan:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) > Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerGreaterThanOrEqual:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) >= Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerLessThan:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) < Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerLessThanOrEqual:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) <= Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerMax:
                        if (typeof(T) == typeof(int))
                        {
                            int lhs = Integers.Get(ShortGuids.LHS);
                            int rhs = Integers.Get(ShortGuids.RHS);
                            if (lhs > rhs) return (T)(object)lhs;
                            return (T)(object)rhs;
                        }
                        break;
                    case FunctionType.IntegerMax_All:
                        if (typeof(T) == typeof(int))
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks(ShortGuids.Numbers);
                            int max = 0;
                            for (int i = 0; i < numbers.Count; i++)
                            {
                                int number = numbers[i].GetAs<int>();
                                if (max < number) max = number;
                            }
                            return (T)(object)max;
                        }
                        break;
                    case FunctionType.IntegerMin:
                        if (typeof(T) == typeof(int))
                        {
                            int lhs = Integers.Get(ShortGuids.LHS);
                            int rhs = Integers.Get(ShortGuids.RHS);
                            if (lhs < rhs) return (T)(object)lhs;
                            return (T)(object)rhs;
                        }
                        break;
                    case FunctionType.IntegerMin_All:
                        if (typeof(T) == typeof(int))
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
                            return (T)(object)min;
                        }
                        break;
                    case FunctionType.IntegerMultiply:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) * Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerMultiply_All:
                        if (typeof(T) == typeof(int))
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks(ShortGuids.Numbers);
                            int sum = 0;
                            if (numbers.Count > 0)
                            {
                                sum = numbers[0].GetAs<int>();
                                for (int i = 1; i < numbers.Count; i++)
                                    sum *= numbers[i].GetAs<int>();
                            }
                            return (T)(object)sum;
                        }
                        break;
                    case FunctionType.IntegerNotEqual:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) != Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerOr:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) | Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerRemainder:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) % Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.IntegerSubtract:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get(ShortGuids.LHS) - Integers.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.JOB_SpottingPosition:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.LogicGate:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get(ShortGuids.Allow);
                        break;
                    case FunctionType.LogicGateAnd:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Bools.Get(ShortGuids.LHS) && Bools.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.LogicGateEquals:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Bools.Get(ShortGuids.LHS) == Bools.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.LogicGateNotEqual:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Bools.Get(ShortGuids.LHS) != Bools.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.LogicGateOr:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Bools.Get(ShortGuids.LHS) || Bools.Get(ShortGuids.RHS));
                        break;
                    case FunctionType.LogicNot:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)!Bools.Get(ShortGuids.Input);
                        break;
                    case FunctionType.LogicOnce:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)true;
                        break;
                    case FunctionType.LogicSwitch:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get(ShortGuids.InitialValueLower);
                        break;
                    case FunctionType.NavMeshArea:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.NavMeshBarrier:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.NavMeshExclusionArea:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.NavMeshReachabilitySeedPoint:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.NavMeshWalkablePlatform:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.NonPersistentBool:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get(ShortGuids.InitialValueLower);
                        break;
                    case FunctionType.NonPersistentInt:
                        if (typeof(T) == typeof(int))
                            return (T)(object)Integers.Get(ShortGuids.InitialValueLower);
                        break;
                    case FunctionType.PathfindingAlienBackstageNode:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.PathfindingManualNode:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.PathfindingTeleportNode:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.PathfindingWaitNode:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.PlatformConstantBool:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get(ShortGuids.NextGen);
                        break;
                    case FunctionType.PlatformConstantFloat:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Floats.Get(ShortGuids.NextGen);
                        break;
                    case FunctionType.PlatformConstantInt:
                        if (typeof(T) == typeof(int))
                            return (T)(object)Integers.Get(ShortGuids.NextGen);
                        break;
                    case FunctionType.PositionDistance:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(float)0;
                        break;
                    case FunctionType.RandomBool:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)((float)new Random().NextDouble() < 0.5f);
                        break;
                    case FunctionType.RandomFloat:
                        if (typeof(T) == typeof(float))
                        {
                            float min = Floats.Get(ShortGuids.Min);
                            float range = Floats.Get(ShortGuids.Max) - min;
                            float rand = (float)new Random().NextDouble() * range;
                            return (T)(object)(rand + min);
                        }
                        break;
                    case FunctionType.RandomInt:
                        if (typeof(T) == typeof(int))
                        {
                            int min = Integers.Get(ShortGuids.Min);
                            int range = Integers.Get(ShortGuids.Max) - min;
                            int rand = new Random().Next(range);
                            return (T)(object)(rand + min);
                        }
                        break;
                    case FunctionType.RandomVector:
                        if (typeof(T) == typeof(Vector3))
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
                            return (T)(object)result;
                        }
                        break;
                    case FunctionType.RegisterCharacterModel:
                        //if (typeof(T) == typeof(string))
                        //    return (T)(object)Strings.Get("display_model");
                        break;
                    case FunctionType.SetBool:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get(ShortGuids.Input);
                        break;
                    case FunctionType.SetColour:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get(ShortGuids.Colour);
                        break;
                    case FunctionType.SetFloat:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Floats.Get(ShortGuids.Input);
                        break;
                    case FunctionType.SetInteger:
                        if (typeof(T) == typeof(int))
                            return (T)(object)Integers.Get(ShortGuids.Input);
                        break;
                    case FunctionType.SetString:
                        //if (typeof(T) == typeof(string))
                        //    return (T)(object)Strings.Get("initial_value");
                        break;
                    case FunctionType.SetVector:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)new Vector3(Floats.Get(ShortGuids.X), Floats.Get(ShortGuids.Y), Floats.Get(ShortGuids.Z));
                        break;
                    case FunctionType.SetVector2:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get(ShortGuids.Input);
                        break;
                    case FunctionType.SoundObject:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)new Transform();
                        break;
                    case FunctionType.SpottingExclusionArea:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)CalculateWorldTransform();
                        break;
                    case FunctionType.TriggerCameraVolume:
                        if (typeof(T) == typeof(float))
                            return (T)(object)0.0f;
                        break;
                    case FunctionType.VariableBool:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get(ShortGuids.InitialValueLower);
                        break;
                    case FunctionType.VariableColour:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get(ShortGuids.InitialColour);
                        break;
                    case FunctionType.VariableEnum:
                        if (typeof(T) == typeof(int))
                            return (T)(object)EnumIndexes.Get(ShortGuids.InitialValueLower);
                        break;
                    case FunctionType.VariableFlashScreenColour:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get(ShortGuids.InitialColour);
                        break;
                    case FunctionType.VariableFloat:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Floats.Get(ShortGuids.InitialValueLower);
                        break;
                    case FunctionType.VariableInt:
                        if (typeof(T) == typeof(int))
                            return (T)(object)Integers.Get(ShortGuids.InitialValueLower);
                        break;
                    case FunctionType.VariablePosition:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)new Transform();
                        break;
                    case FunctionType.VariableString:
                        //if (typeof(T) == typeof(string))
                        //    return (T)(object)Strings.Get("initial_value");
                        break;
                    case FunctionType.VariableVector:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)new Vector3(Floats.Get(ShortGuids.InitialX), Floats.Get(ShortGuids.InitialY), Floats.Get(ShortGuids.InitialZ));
                        break;
                    case FunctionType.VariableVector2:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get(ShortGuids.InitialValueLower);
                        break;
                    case FunctionType.VectorLinearInterpolateTimed:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get(ShortGuids.InitialValue);
                        break;
                    case FunctionType.VectorScale:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)(Vectors.Get(ShortGuids.LHS) * Vectors.Get(ShortGuids.RHS));
                        break;
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
                    if (Transforms.Has(ShortGuids.Position))
                        return (T)(object)Transforms.Get(ShortGuids.Position);
                    else
                        return (T)(object)new Transform();
                }
                else
                {
                    throw new Exception("Unhandled");
                }
            }
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
            Transform localTransform = GetAs<Transform>(ShortGuids.Position);
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

        private InstancedComposite Root = new InstancedComposite();

        private Level _level = null;

        private readonly ConcurrentDictionary<(Entity, Composite), List<(ShortGuid, ParameterVariant, DataType)>> _parameterCache = new ConcurrentDictionary<(Entity, Composite), List<(ShortGuid, ParameterVariant, DataType)>>();
        private readonly ConcurrentDictionary<(Composite, ShortGuid), Entity> _entityLookupCache = new ConcurrentDictionary<(Composite, ShortGuid), Entity>();

        private readonly object _physicsMapsLock = new object();

        public Instancing(Level level)
        {
            _level = level;
        }

        public void GenerateInstances() => GenerateInstances(_level.Commands.EntryPoints[0], new EntityPath(), Root, null, null, new List<InstancedAlias>());
        public void ProcessInstances() => ProcessInstances(Root);

        private void GenerateInstances(Composite composite, EntityPath path, InstancedComposite compositeInstance, InstancedComposite parentCompositeInstance, InstancedEntity parentCompositeInstanceEntity, List<InstancedAlias> aliases)
        {
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
            localAliases.AddRange(aliasList);

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

            //TODO: proxies!

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
                newInstance.InstanceID = newPath.GenerateCompositeInstanceID();

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

        private void ProcessInstances(InstancedComposite composite)
        {
            var entitiesToProcess = composite.Entities.Where(e => e.Entity.variant == EntityVariant.FUNCTION && ((FunctionEntity)e.Entity).function.IsFunctionType).ToList();
            Parallel.ForEach(entitiesToProcess, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, entity =>
            {
                ProcessEntity(entity);
            });

            // Process child composites sequentially (they may have dependencies)
            foreach (InstancedEntity entity in composite.Entities)
            {
                if (entity.ChildCompositeInstance != null)
                {
                    //Ignore templates
                    if (entity.Bools.Get(ShortGuids.IsTemplate))
                        continue;

                    //Ignore deleted
                    if (entity.Bools.Get(ShortGuids.Deleted))
                        continue;

                    ProcessInstances(entity.ChildCompositeInstance);
                }
            }
        }

        private void ProcessEntity(InstancedEntity entity)
        {
            if (entity.Entity.variant != EntityVariant.FUNCTION)
                return;

            FunctionEntity function = (FunctionEntity)entity.Entity;

            if (function.function.IsFunctionType)
            {
                switch (function.function.AsFunctionType)
                {
                    case FunctionType.CAGEAnimation:

                        break;
                    case FunctionType.CameraPlayAnimation:

                        break;
                    case FunctionType.CameraResource:

                        break;
                    case FunctionType.Character:

                        break;
                    case FunctionType.Checkpoint:

                        break;
                    case FunctionType.CHR_PlaySecondaryAnimation:

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

                        break;
                    case FunctionType.ColourCorrectionTransition:

                        break;
                    case FunctionType.CoverExclusionArea:

                        break;
                    case FunctionType.CoverLine:

                        break;
                    case FunctionType.EnvironmentMap:

                        break;
                    case FunctionType.EnvironmentModelReference:

                        break;
                    case FunctionType.ExclusiveMaster:

                        break;
                    case FunctionType.FogBox:

                        break;
                    case FunctionType.FogPlane:

                        break;
                    case FunctionType.FogSphere:

                        break;
                    case FunctionType.JOB_Assault:

                        break;
                    case FunctionType.JOB_SpottingPosition:

                        break;
                    case FunctionType.LightingMaster:

                        break;
                    case FunctionType.ModelReference:

                        break;
                    case FunctionType.MusicController:

                        break;
                    case FunctionType.MusicTrigger:

                        break;
                    case FunctionType.NavMeshArea:

                        break;
                    case FunctionType.NavMeshBarrier:

                        break;
                    case FunctionType.NavMeshExclusionArea:

                        break;
                    case FunctionType.NavMeshReachabilitySeedPoint:

                        break;
                    case FunctionType.NavMeshWalkablePlatform:

                        break;
                    case FunctionType.ParticleEmitterReference:

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
                        ResourceReference physicsSystem = function.GetResource(ResourceType.DYNAMIC_PHYSICS_SYSTEM);
                        if (physicsSystem == null) break;

                        //Calculate the instance metadata
                        EntityPath pathToThisEntity = entity.Path.Copy();
                        ShortGuid compositeInstanceID = pathToThisEntity.GenerateCompositeInstanceID();
                        pathToThisEntity.GoBackOneStep();
                        EntityHandle compositeInstanceReference = new EntityHandle()
                        {
                            entity_id = pathToThisEntity.GetPointedEntityID(),
                            composite_instance_id = pathToThisEntity.GenerateCompositeInstanceID()
                        };

                        //Create the new entry
                        (Vector3 position, Quaternion rotation) = entity.CalculateWorldPositionRotation();
                        PhysicsMaps.Entry newEntry = new PhysicsMaps.Entry()
                        {
                            physics_system_index = physicsSystem.PhysicsSystemIndex,
                            resource_type = ShortGuids.GUID_DYNAMIC_PHYSICS_SYSTEM,
                            composite_instance_id = compositeInstanceID,
                            entity = compositeInstanceReference,
                            Position = position,
                            Rotation = rotation
                        };

                        lock (_physicsMapsLock)
                        {
                            _level.PhysicsMaps.Entries.Add(newEntry);
                        }
                        break;
                    case FunctionType.PlayEnvironmentAnimation:

                        break;
                    case FunctionType.ProjectiveDecal:

                        break;
                    case FunctionType.RadiosityIsland:

                        break;
                    case FunctionType.RadiosityProxy:

                        break;
                    case FunctionType.RegisterCharacterModel:

                        break;
                    case FunctionType.RibbonEmitterReference:

                        break;
                    case FunctionType.SimpleRefraction:

                        break;
                    case FunctionType.SimpleWater:

                        break;
                    case FunctionType.Sound:

                        break;
                    case FunctionType.SoundBarrier:

                        break;
                    case FunctionType.SoundEnvironmentMarker:

                        break;
                    case FunctionType.SoundImpact:

                        break;
                    case FunctionType.SoundLevelInitialiser:

                        break;
                    case FunctionType.SoundLoadBank:

                        break;
                    case FunctionType.SoundLoadSlot:

                        break;
                    case FunctionType.SoundNetworkNode:

                        break;
                    case FunctionType.Speech:

                        break;
                    case FunctionType.SpeechScript:

                        break;
                    case FunctionType.SpottingExclusionArea:

                        break;
                    case FunctionType.SurfaceEffectBox:

                        break;
                    case FunctionType.SurfaceEffectSphere:

                        break;
                    case FunctionType.TRAV_1ShotSpline:

                        break;
                    case FunctionType.Zone:

                        break;
                }
            }
        }

    }
}