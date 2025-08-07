using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using CathodeLib;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
#endif

namespace CATHODE.Scripting
{
    /* A script composite containing entities */
    [Serializable]
    public class Composite
    {
        public Composite() { }
        public Composite(string name)
        {
            shortGUID = ShortGuidUtils.GenerateRandom();
            this.name = name;
        }

        ~Composite()
        {
            variables_dictionary.Clear();
            functions_dictionary.Clear();
            aliases_dictionary.Clear();
            proxies_dictionary.Clear();
        }

        public ShortGuid shortGUID;  //The id when this composite is used as an entity in another composite
        public string name = ""; //The string name of the composite

        /* All entities within the composite */
        public Dictionary<ShortGuid, VariableEntity> variables_dictionary = new Dictionary<ShortGuid, VariableEntity>(); //Variables which can be accessed outside of this composite as parameters, and connected to entities internally
        public Dictionary<ShortGuid, FunctionEntity> functions_dictionary = new Dictionary<ShortGuid, FunctionEntity>(); //Functional nodes, including hard-coded functions and references to other composites
        public Dictionary<ShortGuid, AliasEntity> aliases_dictionary = new Dictionary<ShortGuid, AliasEntity>(); //Aliases of entities in child composites
        public Dictionary<ShortGuid, ProxyEntity> proxies_dictionary = new Dictionary<ShortGuid, ProxyEntity>(); //Entites acting as entities from other composites

        /* Accessors for entities within the composite - ideally, use the dictionaries! */
        public ReadOnlyEntityCollection<VariableEntity> variables => new ReadOnlyEntityCollection<VariableEntity>(variables_dictionary.Values);
        public ReadOnlyEntityCollection<FunctionEntity> functions => new ReadOnlyEntityCollection<FunctionEntity>(functions_dictionary.Values);
        public ReadOnlyEntityCollection<AliasEntity> aliases => new ReadOnlyEntityCollection<AliasEntity>(aliases_dictionary.Values);
        public ReadOnlyEntityCollection<ProxyEntity> proxies => new ReadOnlyEntityCollection<ProxyEntity>(proxies_dictionary.Values);

        /* If an entity exists in the composite, return it */
        public Entity GetEntityByID(ShortGuid id)
        {
            if (variables_dictionary.TryGetValue(id, out VariableEntity variable)) return variable;
            if (functions_dictionary.TryGetValue(id, out FunctionEntity function)) return function;
            if (aliases_dictionary.TryGetValue(id, out AliasEntity alias)) return alias;
            if (proxies_dictionary.TryGetValue(id, out ProxyEntity proxy)) return proxy;
            return null;
        }

        /* Returns a collection of all entities in the composite */
        public List<Entity> GetEntities()
        {
            List<Entity> toReturn = new List<Entity>(variables_dictionary.Count + functions_dictionary.Count + aliases_dictionary.Count + proxies_dictionary.Count);
            toReturn.AddRange(variables_dictionary.Values);
            toReturn.AddRange(functions_dictionary.Values);
            toReturn.AddRange(aliases_dictionary.Values);
            toReturn.AddRange(proxies_dictionary.Values);
            return toReturn;
        }

        /* Returns a collection of function entities in the composite matching the given type */
        public List<FunctionEntity> GetFunctionEntitiesOfType(FunctionType type)
        {
            return functions_dictionary.Values.Where(o => o.function == type).ToList();
        }

        /* Removes all function entities in the composite matching the given type */
        public void RemoveAllFunctionEntitiesOfType(FunctionType type)
        {
            var keysToRemove = functions_dictionary.Where(kvp => kvp.Value.function == type).Select(kvp => kvp.Key).ToList();
            foreach (var key in keysToRemove)
            {
                functions_dictionary.Remove(key);
            }
        }

        /* Add a new function entity */
        public FunctionEntity AddFunction(FunctionType function)
        {
            FunctionEntity func = null;
            switch (function) {
                case FunctionType.CAGEAnimation:
                    func = new CAGEAnimation();
                    break;
                case FunctionType.TriggerSequence:
                    func = new TriggerSequence();
                    break;
                default:
                    func = new FunctionEntity(function);
                    break;
            }
            functions_dictionary.Add(func.shortGUID, func);
            return func;
        }
        public FunctionEntity AddFunction(Composite composite)
        {
            FunctionEntity func = new FunctionEntity(composite);
            functions_dictionary.Add(func.shortGUID, func);
            return func;
        }

        /* Add a new variable entity */
        public VariableEntity AddVariable(string parameter, DataType type)
        {
            VariableEntity vari = new VariableEntity(parameter, type);
            variables_dictionary.Add(vari.shortGUID, vari);
            return vari;
        }

        /* Add a new proxy entity */
        public ProxyEntity AddProxy(Commands commands, ShortGuid[] hierarchy)
        {
            ProxyEntity proxy = new ProxyEntity();
            proxy.proxy = new EntityPath(hierarchy);
            List<Tuple<Composite, Entity>> hierarchyResolved = commands.Utils.ResolveProxy(proxy);
            (Composite pointedComp, Entity pointedEnt) = commands.Utils.GetResolvedTarget(hierarchyResolved);
            if (pointedEnt?.variant != EntityVariant.FUNCTION)
                return null; //Proxies must point to a FunctionEntity!
            proxy.function = ((FunctionEntity)pointedEnt).function;
            proxies_dictionary.Add(proxy.shortGUID, proxy);
            return proxy;
        }

        /* Add a new alias entity */
        public AliasEntity AddAlias(ShortGuid[] hierarchy)
        {
            AliasEntity alias = new AliasEntity(hierarchy);
            aliases_dictionary.Add(alias.shortGUID, alias);
            return alias;
        }

        /* Remove a variable entity */
        public bool RemoveVariable(ShortGuid id)
        {
            return variables_dictionary.Remove(id);
        }
        public bool RemoveVariable(VariableEntity variable)
        {
            if (variable != null)
                return variables_dictionary.Remove(variable.shortGUID);
            return false;
        }

        /* Remove a function entity */
        public bool RemoveFunction(ShortGuid id)
        {
            return functions_dictionary.Remove(id);
        }
        public bool RemoveFunction(FunctionEntity function)
        {
            if (function != null)
                return functions_dictionary.Remove(function.shortGUID);
            return false;
        }

        /* Remove an alias entity */
        public bool RemoveAlias(ShortGuid id)
        {
            return aliases_dictionary.Remove(id);
        }
        public bool RemoveAlias(AliasEntity alias)
        {
            if (alias != null)
                return aliases_dictionary.Remove(alias.shortGUID);
            return false;
        }

        /* Remove a proxy entity by ShortGuid */
        public bool RemoveProxy(ShortGuid id)
        {
            return proxies_dictionary.Remove(id);
        }
        public bool RemoveProxy(ProxyEntity proxy)
        {
            if (proxy != null)
                return proxies_dictionary.Remove(proxy.shortGUID);
            return false;
        }

        /* Remove an entity */
        public bool RemoveEntity(ShortGuid id)
        {
            return RemoveVariable(id) || RemoveFunction(id) || RemoveAlias(id) || RemoveProxy(id);
        }
        public bool RemoveEntity(Entity entity)
        {
            if (entity == null) return false;
            
            switch (entity)
            {
                case VariableEntity variable:
                    return RemoveVariable(variable);
                case FunctionEntity function:
                    return RemoveFunction(function);
                case AliasEntity alias:
                    return RemoveAlias(alias);
                case ProxyEntity proxy:
                    return RemoveProxy(proxy);
                default:
                    return false;
            }
        }

        public override string ToString()
        {
            return name;
        }
    }
}
