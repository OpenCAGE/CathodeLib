using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
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
            variables.Clear();
            functions.Clear();
            aliases.Clear();
            proxies.Clear();
        }

        public ShortGuid shortGUID;  //The id when this composite is used as an entity in another composite
        public string name = ""; //The string name of the composite

        public List<VariableEntity> variables = new List<VariableEntity>(); //Variables which can be accessed outside of this composite as parameters, and connected to entities internally
        public List<FunctionEntity> functions = new List<FunctionEntity>(); //Functional nodes, including hard-coded functions and references to other composites

        public List<AliasEntity> aliases = new List<AliasEntity>(); //Aliases of entities in child composites
        public List<ProxyEntity> proxies = new List<ProxyEntity>(); //Entites acting as entities from other composites

        /* If an entity exists in the composite, return it */
        public Entity GetEntityByID(ShortGuid id)
        {
            foreach (Entity entity in variables) if (entity.shortGUID == id) return entity;
            foreach (Entity entity in functions) if (entity.shortGUID == id) return entity;
            foreach (Entity entity in aliases) if (entity.shortGUID == id) return entity;
            foreach (Entity entity in proxies) if (entity.shortGUID == id) return entity;
            return null;
        }

        /* Returns a collection of all entities in the composite */
        public List<Entity> GetEntities()
        {
            List<Entity> toReturn = new List<Entity>(variables.Count + functions.Count + aliases.Count + proxies.Count);
            toReturn.AddRange(variables);
            toReturn.AddRange(functions);
            toReturn.AddRange(aliases);
            toReturn.AddRange(proxies);
            return toReturn;
        }

        /* Returns a collection of function entities in the composite matching the given type */
        public List<FunctionEntity> GetFunctionEntitiesOfType(FunctionType type)
        {
            ShortGuid guid = CommandsUtils.GetFunctionTypeGUID(type);
            return functions.FindAll(o => o.function == guid);
        }

        /* Removes all function entities in the composite matching the given type */
        public void RemoveAllFunctionEntitiesOfType(FunctionType type)
        {
            ShortGuid guid = CommandsUtils.GetFunctionTypeGUID(type);
            functions.RemoveAll(o => o.function == guid);
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
            functions.Add(func);
            return func;
        }
        public FunctionEntity AddFunction(Composite composite)
        {
            FunctionEntity func = new FunctionEntity(composite);
            functions.Add(func);
            return func;
        }

        /* Add a new variable entity */
        public VariableEntity AddVariable(string parameter, DataType type)
        {
            VariableEntity vari = new VariableEntity(parameter, type);
            variables.Add(vari);
            return vari;
        }

        /* Add a new proxy entity */
        public ProxyEntity AddProxy(Commands commands, ShortGuid[] hierarchy)
        {
            CommandsUtils.ResolveHierarchy(commands, this, hierarchy, out Composite targetComposite, out string str);
            Entity ent = targetComposite.GetEntityByID(hierarchy[hierarchy.Length - 2]);
            if (ent.variant != EntityVariant.FUNCTION) return null;

            ProxyEntity proxy = new ProxyEntity(hierarchy, ((FunctionEntity)ent).function);
            proxies.Add(proxy);
            return proxy;
        }

        /* Add a new alias entity */
        public AliasEntity AddAlias(ShortGuid[] hierarchy)
        {
            AliasEntity alias = new AliasEntity(hierarchy);
            aliases.Add(alias);
            return alias;
        }

        public override string ToString()
        {
            return name;
        }
    }
}
