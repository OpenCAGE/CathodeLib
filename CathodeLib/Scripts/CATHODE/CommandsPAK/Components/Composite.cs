using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public ShortGuid shortGUID;  //The id when this composite is used as an entity in another composite
        public string name = ""; //The string name of the composite

        public List<VariableEntity> variables = new List<VariableEntity>(); //Variables which can be accessed outside of this flowgraph as parameters, and connected to nodes as parameters internally
        public List<FunctionEntity> functions = new List<FunctionEntity>(); //Functional nodes, including hard-coded functions and references to other composites

        public List<OverrideEntity> overrides = new List<OverrideEntity>(); //Overrides of parameters in child composites
        public List<ProxyEntity> proxies = new List<ProxyEntity>();         //Instances of entities from other composites

        /* If an entity exists in the composite, return it */
        public Entity GetEntityByID(ShortGuid id)
        {
            foreach (Entity entity in variables) if (entity.shortGUID == id) return entity;
            foreach (Entity entity in functions) if (entity.shortGUID == id) return entity;
            foreach (Entity entity in overrides) if (entity.shortGUID == id) return entity;
            foreach (Entity entity in proxies) if (entity.shortGUID == id) return entity;
            return null;
        }

        /* Returns a collection of all entities in the composite */
        public List<Entity> GetEntities()
        {
            List<Entity> toReturn = new List<Entity>(variables.Count + functions.Count + overrides.Count + proxies.Count);
            toReturn.AddRange(variables);
            toReturn.AddRange(functions);
            toReturn.AddRange(overrides);
            toReturn.AddRange(proxies);
            return toReturn;
        }

        /* Add a new function entity */
        public FunctionEntity AddFunction(FunctionType function, bool autopopulateParameters = false)
        {
            FunctionEntity func = null;
            switch (function) {
                case FunctionType.CAGEAnimation:
                    func = new CAGEAnimation(autopopulateParameters);
                    break;
                case FunctionType.TriggerSequence:
                    func = new TriggerSequence(autopopulateParameters);
                    break;
                default:
                    func = new FunctionEntity(function, autopopulateParameters);
                    break;
            }
            functions.Add(func);
            return func;
        }
        public FunctionEntity AddFunction(Composite composite, bool autopopulateParameters = false)
        {
            FunctionEntity func = new FunctionEntity(composite.shortGUID, autopopulateParameters);
            functions.Add(func);
            return func;
        }

        /* Add a new variable entity */
        public VariableEntity AddVariable(string parameter, DataType type, bool addDefaultParam = false)
        {
            VariableEntity vari = new VariableEntity(parameter, type, addDefaultParam);
            variables.Add(vari);
            return vari;
        }

        public override string ToString()
        {
            return name;
        }
    }
}
