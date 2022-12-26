using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
#endif

namespace CATHODE.Commands
{
    /* A script composite containing entities */
    [Serializable]
    public class Composite
    {
        public ShortGuid shortGUID;  //The id when this composite is used as an entity in another composite
        public string name = ""; //The string name of the composite

        public OffsetPair unknownPair;

        public List<DatatypeEntity> datatypes = new List<DatatypeEntity>();
        public List<FunctionEntity> functions = new List<FunctionEntity>();

        public List<OverrideEntity> overrides = new List<OverrideEntity>();
        public List<ProxyEntity> proxies = new List<ProxyEntity>();

        /* If an entity exists in the composite, return it */
        public Entity GetEntityByID(ShortGuid id)
        {
            foreach (Entity entity in datatypes) if (entity.shortGUID == id) return entity;
            foreach (Entity entity in functions) if (entity.shortGUID == id) return entity;
            foreach (Entity entity in overrides) if (entity.shortGUID == id) return entity;
            foreach (Entity entity in proxies) if (entity.shortGUID == id) return entity;
            return null;
        }

        /* Returns a collection of all entities in the composite */
        public List<Entity> GetEntities()
        {
            List<Entity> toReturn = new List<Entity>();
            toReturn.AddRange(datatypes);
            toReturn.AddRange(functions);
            toReturn.AddRange(overrides);
            toReturn.AddRange(proxies);
            return toReturn;
        }

        /* Sort all entity arrays */
        public void SortEntities()
        {
            datatypes.OrderBy(o => o.shortGUID.ToUInt32());
            functions.OrderBy(o => o.shortGUID.ToUInt32());
            overrides.OrderBy(o => o.shortGUID.ToUInt32());
            proxies.OrderBy(o => o.shortGUID.ToUInt32());
        }
    }
}
