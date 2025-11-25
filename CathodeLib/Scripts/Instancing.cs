using CATHODE;
using CATHODE.Enums;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace CathodeLib
{
    public class InstancedEntity : IComparable<InstancedEntity>
    {
        public class Parameters<T> : IComparable<Parameters<T>>
        {
            //Values set on the entity itself at initialisation time
            public Dictionary<string, T> Values = new Dictionary<string, T>();

            //Any links to other entities that set parameter values
            public Dictionary<string, List<Tuple<string, InstancedEntity>>> Links = new Dictionary<string, List<Tuple<string, InstancedEntity>>>();

            public bool Has(string name)
            {
                return Values.ContainsKey(name);
            }

            public T Get(string name)
            {
                //Check links first, these override the values
                if (Links.TryGetValue(name, out List<Tuple<string, InstancedEntity>> links))
                    if (links.Count != 0)
                        return links[0].Item2.GetAs<T>(links[0].Item1); //temp - filters accept multiple links

                //Fall back to our own value
                if (Values.TryGetValue(name, out T val))
                    return val;

                throw new Exception("Failed to find param."); //can just return false here i guess and hope for the best
            }

            public List<InstancedEntity> GetLinks(string name)
            {
                List<InstancedEntity> entities = new List<InstancedEntity>();
                if (Links.TryGetValue(name, out List<Tuple<string, InstancedEntity>> ents))
                {
                    for (int i = 0; i < ents.Count; i++)
                    {
                        entities.Add(ents[i].Item2);
                    }
                }
                return entities;
            }

            public void AddLinks(string name, List<Tuple<string, InstancedEntity>> links)
            {
                if (Links.ContainsKey(name))
                    Links[name].AddRange(links);
                else
                    Links.Add(name, links);
            }

            //For VariableEntities -> we want to override the default values and add links for matching variable names on the entity that instanced the composite they're contained in
            public void PopulateVariableParentInfo(Parameters<T> compInstParams, string varName)
            {
                if (compInstParams.Values.ContainsKey(varName))
                {
                    if (!Values.ContainsKey(varName))
                        Values.Add(varName, compInstParams.Values[varName]);
                    else
                        Values[varName] = compInstParams.Values[varName];
                }
                if (compInstParams.Links.ContainsKey(varName))
                {
                    if (!Links.ContainsKey(varName))
                        Links.Add(varName, new List<Tuple<string, InstancedEntity>>());
                    Links[varName].AddRange(compInstParams.Links[varName]); //todo - probs want to insert first?
                }
            }

            //Any entity can have an Alias override the values on it, kinda similar to the above 
            public void PopulateAliasInfo(Parameters<T> aliasParams)
            {
                foreach (KeyValuePair<string, T> value in aliasParams.Values)
                {
                    if (!Values.ContainsKey(value.Key))
                        Values.Add(value.Key, value.Value);
                    else
                        Values[value.Key] = value.Value;
                }
                foreach (KeyValuePair<string, List<Tuple<string, InstancedEntity>>> value in aliasParams.Links)
                {
                    AddLinks(value.Key, value.Value);
                }
            }

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
                        if (!other.Links.TryGetValue(kvp.Key, out List<Tuple<string, InstancedEntity>> otherLinks))
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

            private int CompareDictionaries(Dictionary<string, T> dict1, Dictionary<string, T> dict2)
            {
                int countCompare = dict1.Count.CompareTo(dict2.Count);
                if (countCompare != 0) return countCompare;

                var keys1 = new List<string>(dict1.Keys);
                var keys2 = new List<string>(dict2.Keys);
                keys1.Sort();
                keys2.Sort();
                for (int i = 0; i < keys1.Count; i++)
                {
                    int keyCompare = string.Compare(keys1[i], keys2[i], StringComparison.Ordinal);
                    if (keyCompare != 0) return keyCompare;

                    T val1 = dict1[keys1[i]];
                    T val2 = dict2[keys2[i]];
                    int valCompare = CompareValues(val1, val2);
                    if (valCompare != 0) return valCompare;
                }
                return 0;
            }

            private int CompareLinksDictionaries(Dictionary<string, List<Tuple<string, InstancedEntity>>> dict1, Dictionary<string, List<Tuple<string, InstancedEntity>>> dict2)
            {
                int countCompare = dict1.Count.CompareTo(dict2.Count);
                if (countCompare != 0) return countCompare;

                var keys1 = new List<string>(dict1.Keys);
                var keys2 = new List<string>(dict2.Keys);
                keys1.Sort();
                keys2.Sort();

                for (int i = 0; i < keys1.Count; i++)
                {
                    int keyCompare = string.Compare(keys1[i], keys2[i], StringComparison.Ordinal);
                    if (keyCompare != 0) return keyCompare;

                    var list1 = dict1[keys1[i]];
                    var list2 = dict2[keys2[i]];
                    int listCountCompare = list1.Count.CompareTo(list2.Count);
                    if (listCountCompare != 0) return listCountCompare;

                    for (int j = 0; j < list1.Count; j++)
                    {
                        int item1Compare = string.Compare(list1[j].Item1, list2[j].Item1, StringComparison.Ordinal);
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
        }

        public Parameters<bool> Bools = new Parameters<bool>();
        public Parameters<int> Integers = new Parameters<int>();
        public Parameters<float> Floats = new Parameters<float>();
        public Parameters<int> EnumIndexes = new Parameters<int>();
        public Parameters<Vector3> Vectors = new Parameters<Vector3>();
        public Parameters<Transform> Transforms = new Parameters<Transform>();

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

        private HashSet<(ShortGuid, ParameterVariant, DataType)> _parameters = new HashSet<(ShortGuid, ParameterVariant, DataType)>();

        public InstancedEntity(Level level, Composite composite, Entity entity, EntityPath path)
        {
            Level = level;
            Entity = entity;
            Path = path;
            Composite = composite;

            var parameters = Level.Commands.Utils.GetAllParameters(entity, composite);
            parameters.RemoveAll(o =>
                o.Item2 == ParameterVariant.REFERENCE_PIN ||
                o.Item2 == ParameterVariant.TARGET_PIN ||
                o.Item2 == ParameterVariant.METHOD_FUNCTION ||
                o.Item2 == ParameterVariant.METHOD_PIN
            //TODO: remove "output pin" as well? or perhaps we should impleement the logic for these?
            );
            switch (entity.variant)
            {
                //For aliases, only factor in the parameters and links that are actually set, since these are OVERRIDES
                case EntityVariant.ALIAS:
                    foreach (Parameter p in entity.parameters)
                    {
                        if (p.content == null)
                            continue;
                        _parameters.Add(parameters.FirstOrDefault(o => o.Item1 == p.name));
                    }
                    //TODO: also need to factor in parent links somehow
                    foreach (EntityConnector c in entity.childLinks)
                        _parameters.Add(parameters.FirstOrDefault(o => o.Item1 == c.thisParamID));
                    break;
                //For others, get all default values, as well as ones that are set
                default:
                    //NOTE: GetAllParameters does not check for duplicates, so do that now - need to fix that.
                    // An example of another issue is {UI_ReactionGame} - the child UI_Attached should not add another 'success' entry
                    foreach (var entry in parameters)
                        _parameters.Add(entry);
                    break;
            }

            //Get all boolean values on this entity
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
                            Bools.Values.Add(guid.ToString(), value);
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
                            Integers.Values.Add(guid.ToString(), value);
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
                            if (!Floats.Values.ContainsKey(guid.ToString())) //todo - deprecate this when the hashset above is fixed
                                Floats.Values.Add(guid.ToString(), value);
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
                            EnumIndexes.Values.Add(guid.ToString(), value);
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
                            Vectors.Values.Add(guid.ToString(), value);
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

                            Transforms.Values.Add(guid.ToString(), value);
                        }
                        break;
                }

            }

            //TODO: need to handle triggersequences a bit different i think? they can apply parameter data down
        }

        public void PopulateLinks(List<InstancedEntity> entities)
        {
            foreach ((ShortGuid guid, ParameterVariant variant, DataType datatype) in _parameters)
            {
                List<EntityConnector> links = Entity.childLinks.FindAll(o => o.thisParamID == guid);
                if (links.Count == 0)
                    continue;

                List<Tuple<string, InstancedEntity>> linksParsed = new List<Tuple<string, InstancedEntity>>();
                for (int i = 0; i < links.Count; i++)
                {
                    Entity connectedEnt = Composite.GetEntityByID(links[i].linkedEntityID);
                    if (connectedEnt == null) continue;
                    linksParsed.Add(new Tuple<string, InstancedEntity>(links[i].linkedParamID.ToString(), entities.FirstOrDefault(o => o.Entity == connectedEnt)));
                }

                switch (datatype)
                {
                    case DataType.BOOL:
                        Bools.AddLinks(guid.ToString(), linksParsed);
                        break;
                    case DataType.INTEGER:
                        Integers.AddLinks(guid.ToString(), linksParsed);
                        break;
                    case DataType.FLOAT:
                        Floats.AddLinks(guid.ToString(), linksParsed);
                        break;
                    case DataType.ENUM:
                        EnumIndexes.AddLinks(guid.ToString(), linksParsed);
                        break;
                    case DataType.VECTOR:
                        Vectors.AddLinks(guid.ToString(), linksParsed);
                        break;
                    case DataType.TRANSFORM:
                        Transforms.AddLinks(guid.ToString(), linksParsed);
                        break;
                }
            }

            //If this entity is a Composite interface type, we need to look for the parent entity that instanced our composite and forward the links on.
            if (Entity.variant == EntityVariant.VARIABLE)
            {
                if (ParentCompositeInstanceEntity != null)
                {
                    VariableEntity var = (VariableEntity)Entity;
                    string varName = var.name.ToString();

                    Bools.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Bools, varName);
                    Integers.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Integers, varName);
                    Floats.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Floats, varName);
                    EnumIndexes.PopulateVariableParentInfo(ParentCompositeInstanceEntity.EnumIndexes, varName);
                    Vectors.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Vectors, varName);
                    Transforms.PopulateVariableParentInfo(ParentCompositeInstanceEntity.Transforms, varName);
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
            switch (Entity.variant)
            {
                case EntityVariant.FUNCTION:
                    {
                        FunctionEntity func = (FunctionEntity)Entity;
                        if (func.function.IsFunctionType)
                        {
                            return GetFunctionData<T>(name, func.function.AsFunctionType);
                        }
                        else
                        {
                            return GetFunctionData<T>(name, FunctionType.CompositeInterface);
                        }
                    }

                case EntityVariant.VARIABLE:
                    {
                        VariableEntity var = (VariableEntity)Entity;
                        switch (var.type)
                        {
                            case DataType.BOOL:
                                bool b = Bools.Get(var.name.ToString());
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
                                int i = Integers.Get(var.name.ToString());
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
                                float f = Floats.Get(var.name.ToString());
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
                                int e = EnumIndexes.Get(var.name.ToString());
                                if (typeof(T) == typeof(int))
                                    return (T)(object)e;
                                if (typeof(T) == typeof(float))
                                    return (T)(object)(float)e;
                                if (typeof(T) == typeof(bool))
                                    return (T)(object)(e == 1);
                                break;
                            case DataType.VECTOR:
                                Vector3 v = Vectors.Get(var.name.ToString());
                                if (typeof(T) == typeof(Vector3))
                                    return (T)(object)v;
                                if (typeof(T) == typeof(Transform))
                                    return (T)(object)new Transform() { Position = v };
                                break;
                            case DataType.TRANSFORM:
                                Transform t = Transforms.Get(var.name.ToString());
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
                if (Transforms.Has("position"))
                    return (T)(object)Transforms.Get("position");
                else
                    return (T)(object)new Transform();
            }
            else
            {
                throw new Exception("Unhandled");
            }
        }

        private T GetFunctionData<T>(string name, FunctionType type)
        {
            if (name != "reference")
            {
                if (typeof(T) == typeof(bool))
                    return (T)(object)Bools.Get(name);
                else if (typeof(T) == typeof(int))
                {
                    if (Integers.Has(name))
                        return (T)(object)Integers.Get(name);
                    else
                        return (T)(object)EnumIndexes.Get(name);
                }
                else if (typeof(T) == typeof(float))
                    return (T)(object)Floats.Get(name);
                else if (typeof(T) == typeof(Vector3))
                    return (T)(object)Vectors.Get(name);
                else if (typeof(T) == typeof(Transform))
                    return (T)(object)Transforms.Get(name);
            }
            else
            {
                switch (type)
                {
                    case FunctionType.Character:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.Checkpoint:
                        if (typeof(T) == typeof(string))
                            return (T)(object)"";
                        break;
                    case FunctionType.CoverExclusionArea:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.DeleteBlankPanel:
                        if (typeof(T) == typeof(bool))
                        {
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
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
                            BUTTON_TYPE button_type = (BUTTON_TYPE)EnumIndexes.Get("button_type");
                            if (button_type != BUTTON_TYPE.DISK) return (T)(object)true;

                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
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
                            BUTTON_TYPE button_type = (BUTTON_TYPE)EnumIndexes.Get("button_type");
                            if (button_type != BUTTON_TYPE.KEYS) return (T)(object)true;

                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
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
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
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
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
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
                            if (!Bools.Get("is_door")) return (T)(object)true;

                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
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
                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
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
                            LEVER_TYPE lever_type = (LEVER_TYPE)EnumIndexes.Get("lever_type");
                            if (lever_type != LEVER_TYPE.PULL) return (T)(object)true;

                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
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
                            LEVER_TYPE lever_type = (LEVER_TYPE)EnumIndexes.Get("lever_type");
                            if (lever_type != LEVER_TYPE.ROTATE) return (T)(object)true;

                            DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
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
                            List<InstancedEntity> filters = Bools.GetLinks("filter");
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
                            List<InstancedEntity> filters = Bools.GetLinks("filter");
                            return (T)(object)(filters.Count == 0 ? true : filters[0].GetAs<bool>());
                        }
                        break;
                    case FunctionType.FilterOr:
                        if (typeof(T) == typeof(bool))
                        {
                            List<InstancedEntity> filters = Bools.GetLinks("filter");
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
                            return (T)(object)Math.Abs(Floats.Get("Input"));
                        break;
                    case FunctionType.FloatAdd:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(Floats.Get("LHS") + Floats.Get("RHS"));
                        break;
                    case FunctionType.FloatAdd_All:
                        if (typeof(T) == typeof(float))
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks("Numbers");
                            float sum = 0;
                            for (int i = 0; i < numbers.Count; i++)
                                sum += numbers[i].GetAs<float>();
                            return (T)(object)sum;
                        }
                        break;
                    case FunctionType.FloatClamp:
                        if (typeof(T) == typeof(float))
                        {
                            float val = Floats.Get("Value");
                            float min = Floats.Get("Min");
                            float max = Floats.Get("Max");
                            if (val < min) val = min;
                            if (val > max) val = max;
                            return (T)(object)val;
                        }
                        break;
                    case FunctionType.FloatClampMultiply:
                        if (typeof(T) == typeof(float))
                        {
                            float val = Floats.Get("LHS");
                            float min = Floats.Get("Min");
                            float max = Floats.Get("Max") * Floats.Get("RHS");
                            if (val < min) val = min;
                            if (val > max) val = max;
                            return (T)(object)val;
                        }
                        break;
                    case FunctionType.FloatDivide:
                        if (typeof(T) == typeof(float))
                        {
                            float rhs = Floats.Get("RHS");
                            if (Math.Abs(rhs) < 0.0001f) return (T)(object)0.0f;
                            return (T)(object)(Floats.Get("LHS") / rhs);
                        }
                        break;
                    case FunctionType.FloatEquals:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Math.Abs(Floats.Get("LHS") - Floats.Get("RHS")) < Math.Abs(Floats.Get("Threshold")));
                        break;
                    case FunctionType.FloatGetLinearProportion:
                        if (typeof(T) == typeof(float))
                        {
                            float min = Floats.Get("Min");
                            float max = Floats.Get("Max");
                            float mid = Floats.Get("Input");
                            return (T)(object)((mid - min) / (max - min));
                        }
                        break;
                    case FunctionType.FloatGreaterThan:
                        if (typeof(T) == typeof(bool))
                        {
                            float lhs = Floats.Get("LHS");
                            float rhs = Floats.Get("RHS");
                            float threshold = Floats.Get("Threshold");
                            if (Math.Abs(lhs - rhs) < threshold) return (T)(object)false;
                            return (T)(object)(lhs > rhs);
                        }
                        break;
                    case FunctionType.FloatGreaterThanOrEqual:
                        if (typeof(T) == typeof(bool))
                        {
                            float lhs = Floats.Get("LHS");
                            float rhs = Floats.Get("RHS");
                            float threshold = Floats.Get("Threshold");
                            if (Math.Abs(lhs - rhs) < threshold) return (T)(object)true;
                            return (T)(object)(lhs > rhs);
                        }
                        break;
                    case FunctionType.FloatLessThan:
                        if (typeof(T) == typeof(bool))
                        {
                            float lhs = Floats.Get("LHS");
                            float rhs = Floats.Get("RHS");
                            float threshold = Floats.Get("Threshold");
                            if (Math.Abs(lhs - rhs) < threshold) return (T)(object)false;
                            return (T)(object)(lhs < rhs);
                        }
                        break;
                    case FunctionType.FloatLessThanOrEqual:
                        if (typeof(T) == typeof(bool))
                        {
                            float lhs = Floats.Get("LHS");
                            float rhs = Floats.Get("RHS");
                            float threshold = Floats.Get("Threshold");
                            if (Math.Abs(lhs - rhs) < threshold) return (T)(object)true;
                            return (T)(object)(lhs < rhs);
                        }
                        break;
                    case FunctionType.FloatLinearInterpolateSpeed:
                    case FunctionType.FloatLinearInterpolateTimed:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Floats.Get("Initial_Value");
                        break;
                    case FunctionType.FloatLinearProportion:
                        if (typeof(T) == typeof(float))
                        {
                            float min = Floats.Get("Initial_Value");
                            float max = Floats.Get("Target_Value");
                            return (T)(object)(min + (max - min) * Floats.Get("Proportion"));
                        }
                        break;
                    case FunctionType.FloatMax:
                        if (typeof(T) == typeof(float))
                        {
                            float lhs = Floats.Get("LHS");
                            float rhs = Floats.Get("RHS");
                            if (lhs > rhs) return (T)(object)lhs;
                            return (T)(object)rhs;
                        }
                        break;
                    case FunctionType.FloatMax_All:
                        if (typeof(T) == typeof(float))
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks("Numbers");
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
                            float lhs = Floats.Get("LHS");
                            float rhs = Floats.Get("RHS");
                            if (lhs < rhs) return (T)(object)lhs;
                            return (T)(object)rhs;
                        }
                        break;
                    case FunctionType.FloatMin_All:
                        if (typeof(T) == typeof(float))
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks("Numbers");
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

                            float offset = Floats.Get("bias");
                            float amplitude = Floats.Get("amplitude");

                            float phase = Floats.Get("phase") / 360.0f;
                            float output = phase % 1.0f;

                            WAVE_SHAPE wave_shape = (WAVE_SHAPE)EnumIndexes.Get("wave_shape");
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
                            return (T)(object)(Floats.Get("LHS") * Floats.Get("RHS"));
                        break;
                    case FunctionType.FloatMultiply_All:
                        if (typeof(T) == typeof(float))
                        {
                            List<InstancedEntity> numbers = Floats.GetLinks("Numbers");
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
                            float val = Floats.Get("LHS") * Floats.Get("RHS");
                            float min = Floats.Get("Min");
                            float max = Floats.Get("Max");
                            if (val < min) val = min;
                            if (val > max) val = max;
                            return (T)(object)val;
                        }
                        break;
                    case FunctionType.FloatNotEqual:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)!(Math.Abs(Floats.Get("LHS") - Floats.Get("RHS")) < Math.Abs(Floats.Get("Threshold")));
                        break;
                    case FunctionType.FloatReciprocal:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(1.0f / Floats.Get("Input"));
                        break;
                    case FunctionType.FloatRemainder:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(Floats.Get("LHS") % Floats.Get("RHS"));
                        break;
                    case FunctionType.FloatSqrt:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(float)Math.Sqrt(Math.Abs(Floats.Get("Input")));
                        break;
                    case FunctionType.FloatSubtract:
                        if (typeof(T) == typeof(float))
                            return (T)(object)(Floats.Get("LHS") - Floats.Get("RHS"));
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
                            return (T)(object)Transforms.Get("Input").Rotation;
                        break;
                    case FunctionType.GetTranslation:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Transforms.Get("Input").Position;
                        break;
                    case FunctionType.GetX:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Vectors.Get("Input").X;
                        break;
                    case FunctionType.GetY:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Vectors.Get("Input").Y;
                        break;
                    case FunctionType.GetZ:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Vectors.Get("Input").Z;
                        break;
                    case FunctionType.HasAccessAtDifficulty:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)false;
                        break;
                    case FunctionType.IntegerAbsolute:
                        if (typeof(T) == typeof(int))
                            return (T)(object)Math.Abs(Integers.Get("Input"));
                        break;
                    case FunctionType.IntegerAdd:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get("LHS") + Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerAdd_All:
                        if (typeof(T) == typeof(int))
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks("Numbers");
                            int sum = 0;
                            for (int i = 0; i < numbers.Count; i++)
                                sum += numbers[i].GetAs<int>();
                            return (T)(object)sum;
                        }
                        break;
                    case FunctionType.IntegerAnd:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get("LHS") & Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerCompliment:
                        if (typeof(T) == typeof(int))
                            return (T)(object)~Integers.Get("Input");
                        break;
                    case FunctionType.IntegerDivide:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get("LHS") / Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerEquals:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get("LHS") == Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerGreaterThan:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get("LHS") > Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerGreaterThanOrEqual:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get("LHS") >= Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerLessThan:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get("LHS") < Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerLessThanOrEqual:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Integers.Get("LHS") <= Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerMax:
                        if (typeof(T) == typeof(int))
                        {
                            int lhs = Integers.Get("LHS");
                            int rhs = Integers.Get("RHS");
                            if (lhs > rhs) return (T)(object)lhs;
                            return (T)(object)rhs;
                        }
                        break;
                    case FunctionType.IntegerMax_All:
                        if (typeof(T) == typeof(int))
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks("Numbers");
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
                            int lhs = Integers.Get("LHS");
                            int rhs = Integers.Get("RHS");
                            if (lhs < rhs) return (T)(object)lhs;
                            return (T)(object)rhs;
                        }
                        break;
                    case FunctionType.IntegerMin_All:
                        if (typeof(T) == typeof(int))
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks("Numbers");
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
                            return (T)(object)(Integers.Get("LHS") * Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerMultiply_All:
                        if (typeof(T) == typeof(int))
                        {
                            List<InstancedEntity> numbers = Integers.GetLinks("Numbers");
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
                            return (T)(object)(Integers.Get("LHS") != Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerOr:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get("LHS") | Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerRemainder:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get("LHS") % Integers.Get("RHS"));
                        break;
                    case FunctionType.IntegerSubtract:
                        if (typeof(T) == typeof(int))
                            return (T)(object)(Integers.Get("LHS") - Integers.Get("RHS"));
                        break;
                    case FunctionType.JOB_SpottingPosition:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("SpottingPosition"); //THIS SHOULD BE IN WORLDSPACE!
                        break;
                    case FunctionType.LogicGate:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get("allow");
                        break;
                    case FunctionType.LogicGateAnd:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Bools.Get("LHS") && Bools.Get("RHS"));
                        break;
                    case FunctionType.LogicGateEquals:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Bools.Get("LHS") == Bools.Get("RHS"));
                        break;
                    case FunctionType.LogicGateNotEqual:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Bools.Get("LHS") != Bools.Get("RHS"));
                        break;
                    case FunctionType.LogicGateOr:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)(Bools.Get("LHS") || Bools.Get("RHS"));
                        break;
                    case FunctionType.LogicNot:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)!Bools.Get("Input");
                        break;
                    case FunctionType.LogicOnce:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)true;
                        break;
                    case FunctionType.LogicSwitch:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get("initial_value");
                        break;
                    case FunctionType.NavMeshArea:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.NavMeshBarrier:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.NavMeshExclusionArea:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.NavMeshReachabilitySeedPoint:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.NavMeshWalkablePlatform:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.NonPersistentBool:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get("initial_value");
                        break;
                    case FunctionType.NonPersistentInt:
                        if (typeof(T) == typeof(int))
                            return (T)(object)Integers.Get("initial_value");
                        break;
                    case FunctionType.PathfindingAlienBackstageNode:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.PathfindingManualNode:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.PathfindingTeleportNode:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.PathfindingWaitNode:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.PlatformConstantBool:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get("NextGen");
                        break;
                    case FunctionType.PlatformConstantFloat:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Floats.Get("NextGen");
                        break;
                    case FunctionType.PlatformConstantInt:
                        if (typeof(T) == typeof(int))
                            return (T)(object)Integers.Get("NextGen");
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
                            float min = Floats.Get("Min");
                            float range = Floats.Get("Max") - min;
                            float rand = (float)new Random().NextDouble() * range;
                            return (T)(object)(rand + min);
                        }
                        break;
                    case FunctionType.RandomInt:
                        if (typeof(T) == typeof(int))
                        {
                            int min = Integers.Get("Min");
                            int range = Integers.Get("Max") - min;
                            int rand = new Random().Next(range);
                            return (T)(object)(rand + min);
                        }
                        break;
                    case FunctionType.RandomVector:
                        if (typeof(T) == typeof(Vector3))
                        {
                            float minX = Integers.Get("MinX");
                            float rangeX = Integers.Get("MaxX") - minX;
                            float randX = (float)new Random().NextDouble() * rangeX;
                            float minY = Integers.Get("MinY");
                            float rangeY = Integers.Get("MaxY") - minY;
                            float randY = (float)new Random().NextDouble() * rangeY;
                            float minZ = Integers.Get("MinZ");
                            float rangeZ = Integers.Get("MaxZ") - minZ;
                            float randZ = (float)new Random().NextDouble() * rangeZ;

                            Vector3 result = new Vector3(randX + minX, randY + minY, randZ + minZ);
                            if (Bools.Get("Normalised"))
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
                            return (T)(object)Bools.Get("Input");
                        break;
                    case FunctionType.SetColour:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get("Colour");
                        break;
                    case FunctionType.SetFloat:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Floats.Get("Input");
                        break;
                    case FunctionType.SetInteger:
                        if (typeof(T) == typeof(int))
                            return (T)(object)Integers.Get("Input");
                        break;
                    case FunctionType.SetString:
                        //if (typeof(T) == typeof(string))
                        //    return (T)(object)Strings.Get("initial_value");
                        break;
                    case FunctionType.SetVector:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)new Vector3(Floats.Get("x"), Floats.Get("y"), Floats.Get("z"));
                        break;
                    case FunctionType.SetVector2:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get("Input");
                        break;
                    case FunctionType.SoundObject:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)new Transform();
                        break;
                    case FunctionType.SpottingExclusionArea:
                        if (typeof(T) == typeof(Transform))
                            return (T)(object)Transforms.Get("position"); //SHOULD BE WORLDSPACE POSITION!
                        break;
                    case FunctionType.TriggerCameraVolume:
                        if (typeof(T) == typeof(float))
                            return (T)(object)0.0f;
                        break;
                    case FunctionType.VariableBool:
                        if (typeof(T) == typeof(bool))
                            return (T)(object)Bools.Get("initial_value");
                        break;
                    case FunctionType.VariableColour:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get("initial_colour");
                        break;
                    case FunctionType.VariableEnum:
                        if (typeof(T) == typeof(int))
                            return (T)(object)EnumIndexes.Get("initial_value");
                        break;
                    case FunctionType.VariableFlashScreenColour:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get("initial_colour");
                        break;
                    case FunctionType.VariableFloat:
                        if (typeof(T) == typeof(float))
                            return (T)(object)Floats.Get("initial_value");
                        break;
                    case FunctionType.VariableInt:
                        if (typeof(T) == typeof(int))
                            return (T)(object)Integers.Get("initial_value");
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
                            return (T)(object)new Vector3(Floats.Get("initial_x"), Floats.Get("initial_y"), Floats.Get("initial_z"));
                        break;
                    case FunctionType.VariableVector2:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get("initial_value");
                        break;
                    case FunctionType.VectorLinearInterpolateTimed:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)Vectors.Get("Initial_Value");
                        break;
                    case FunctionType.VectorScale:
                        if (typeof(T) == typeof(Vector3))
                            return (T)(object)(Vectors.Get("LHS") * Vectors.Get("RHS"));
                        break;
                }
            }

            FunctionType? inherited = Level.Commands.Utils.GetInheritedFunction(type);
            if (inherited.HasValue)
            {
                return GetFunctionData<T>(name, inherited.Value);
            }
            else
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
                {
                    if (Transforms.Has("position"))
                        return (T)(object)Transforms.Get("position");
                    else
                        return (T)(object)new Transform();
                }
                else
                {
                    throw new Exception("Unhandled");
                }
            }
        }

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
    }

    public class Instancing
    {
        private List<InstancedEntity> AllEntities = new List<InstancedEntity>();
        private List<InstancedComposite> AllComposites = new List<InstancedComposite>();

        private InstancedComposite Root = new InstancedComposite();

        private Level _level = null;

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
            foreach (Entity entity in composite.GetEntities())
            {
                EntityPath pathToThisEntity = path.Copy();
                pathToThisEntity.AddNextStep(entity);

                InstancedEntity newInstance = new InstancedEntity(_level, composite, entity, pathToThisEntity);
                newInstance.ParentCompositeInstanceEntity = parentCompositeInstanceEntity;
                newInstance.ParentCompositeInstance = parentCompositeInstance;
                newInstance.ThisCompositeInstance = compositeInstance;
                compositeInstance.Entities.Add(newInstance);

                //Keep track of aliases
                if (entity.variant == EntityVariant.ALIAS)
                {
                    InstancedAlias alias = new InstancedAlias() { ActivePath = ((AliasEntity)entity).alias.path.ToList(), InstancedInfo = newInstance };
                    localAliases.Add(alias);
                }
            }

            //Next, hook up the instanced entity links as references
            foreach (InstancedEntity entity in compositeInstance.Entities)
            {
                entity.PopulateLinks(compositeInstance.Entities);
            }

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
                    InstancedEntity toApply = compositeInstance.Entities.FirstOrDefault(o => o.Entity.shortGUID == currentStep);
                    if (toApply != null)
                    {
                        toApply.ApplyAlias(alias);
                    }
                }
                else
                {
                    //Otherwise, just keep a track of the alias with its newly updated path to use further down
                    if (!trackedAliases.ContainsKey(currentStep))
                        trackedAliases.Add(currentStep, new List<InstancedAlias>());
                    trackedAliases[currentStep].Add(alias);
                }
            }

            //TODO: proxies!

            AllEntities.AddRange(compositeInstance.Entities);
            AllComposites.Add(compositeInstance);

            //Now, traverse down in to any child composites, and rinse and repeat
            foreach (FunctionEntity function in composite.functions)
            {
                if (function.function.IsFunctionType)
                    continue;

                Composite child = _level.Commands.GetComposite(function.function);
                if (child == null)
                    continue;

                List<InstancedAlias> childAliases;
                if (!trackedAliases.TryGetValue(function.shortGUID, out childAliases))
                    childAliases = new List<InstancedAlias>();

                EntityPath newPath = path.Copy();
                newPath.AddNextStep(function);
                InstancedComposite newInstance = new InstancedComposite();
                newInstance.InstanceID = newPath.GenerateCompositeInstanceID();
                InstancedEntity instancedEnt = compositeInstance.Entities.FirstOrDefault(o => o.Entity == function);
                instancedEnt.ChildCompositeInstance = newInstance;
                GenerateInstances(child, newPath, newInstance, compositeInstance, instancedEnt, childAliases);
            }
        }

        private void ProcessInstances(InstancedComposite composite)
        {
            ShortGuid GUID_DYNAMIC_PHYSICS_SYSTEM = ShortGuidUtils.Generate("DYNAMIC_PHYSICS_SYSTEM");

            foreach (InstancedEntity entity in composite.Entities)
            {
                if (entity.Entity.variant != EntityVariant.FUNCTION)
                    continue;

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

                            //Calculate the instanced position
                            (Vector3 position, Quaternion rotation) = CalculateInstancedPosition(entity);

                            //For sanity: get the existing entry
                            List<PhysicsMaps.Entry> existing = _level.PhysicsMaps.Entries.FindAll(o =>
                                o.physics_system_index == physicsSystem.PhysicsSystemIndex &&
                                o.resource_type == GUID_DYNAMIC_PHYSICS_SYSTEM &&
                                o.composite_instance_id == compositeInstanceID &&
                                o.entity == compositeInstanceReference);

                            //Create the new entry
                            PhysicsMaps.Entry newEntry = new PhysicsMaps.Entry()
                            {
                                physics_system_index = physicsSystem.PhysicsSystemIndex,
                                resource_type = GUID_DYNAMIC_PHYSICS_SYSTEM,
                                composite_instance_id = compositeInstanceID,
                                entity = compositeInstanceReference,
                                Position = position,
                                Rotation = rotation
                            };

                            //TODO: position calculation is wrong!

                            _level.PhysicsMaps.Entries.Add(newEntry);
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

                if (entity.ChildCompositeInstance != null)
                {
                    //Ignore templates
                    if (entity.Bools.Get("is_template"))
                        continue;

                    //Ignore deleted
                    if (entity.Bools.Get("deleted"))
                        continue;

                    ProcessInstances(entity.ChildCompositeInstance);
                }
            }
        }

        private (Vector3, Quaternion) CalculateInstancedPosition(InstancedEntity entity)
        {
            List<InstancedEntity.Transform> transforms = new List<InstancedEntity.Transform>();
            InstancedEntity parent = entity;
            while (parent != null)
            {
                //Console.WriteLine("Entity: " + parent.Entity.shortGUID.ToByteString() + "\n" +
                //    "\tWithin Composite: " + parent.Composite.name + "\n" +
                //    "\tAt Path: " + parent.Path.ToString() + "\n" +
                //    "\tHas Position: " + parent.GetAs<InstancedEntity.Transform>("position").ToString() + "\n" +
                //    "----------");
                transforms.Add(parent.GetAs<InstancedEntity.Transform>("position"));
                parent = parent.ParentCompositeInstanceEntity;
            }
            transforms.Reverse();

            cTransform stacked = new cTransform();
            for (int i = 0; i < transforms.Count; i++)
            {
                //TODO: does this work correctly?
                stacked += new cTransform() { position = transforms[i].Position, rotation = transforms[i].Rotation };
            }
            return (stacked.position, Quaternion.CreateFromYawPitchRoll(stacked.rotation.Y * (float)Math.PI / 180.0f, stacked.rotation.X * (float)Math.PI / 180.0f, stacked.rotation.Z * (float)Math.PI / 180.0f));
        }
    }
}