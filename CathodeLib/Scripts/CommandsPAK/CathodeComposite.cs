using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
#endif

namespace CATHODE.Commands
{
    /* Blocks of data in each compiled composite */
    public enum CommandsDataBlock
    {
        COMPOSITE_HEADER,             //Defines the header of the composite, with global ID and string name
        ENTITY_CONNECTIONS,           //Defines the links between entities in the composite
        ENTITY_PARAMETERS,            //Defines parameters to be applied to entities in the composite 
        ENTITY_OVERRIDES,             //Defines overrides to apply to nested instances of composites in this composite
        ENTITY_OVERRIDES_CHECKSUM,    //Defines a checksum value for the hierarchy override (TODO)
        COMPOSITE_EXPOSED_PARAMETERS, //Defines variables which are exposed when instancing this composite which are then connected in to entities (think variable pins in UE4 blueprint)
        ENTITY_PROXIES,               //Defines "proxies" similar to the overrides hierarchy (TODO)
        ENTITY_FUNCTIONS,             //Defines entities with an attached script function within Cathode
        RESOURCE_REFERENCES,          //Defines renderable data which is referenced by entities in this composite
        CAGEANIMATION_DATA,           //Appears to define additional data for CAGEAnimation type entities (TODO)
        TRIGGERSEQUENCE_DATA,         //Appears to define additional data for TriggerSequence type entities (TODO)

        UNUSED,                       //Unused values
        UNKNOWN_COUNTS,               //TODO - unused?

        NUMBER_OF_SCRIPT_BLOCKS,      //THIS IS NOT A DATA BLOCK: merely used as an easy way of sanity checking the number of blocks in-code!
    }

    /* A reference to a parameter in a composite */
    [Serializable]
    public class CathodeLoadedParameter
    {
        public CathodeLoadedParameter(ShortGuid id, CathodeParameter cont)
        {
            shortGUID = id;
            content = cont;
        }

        public ShortGuid shortGUID; //The ID of the param in the entity
        public CathodeParameter content = null;
    }

    /* A reference to a game resource (E.G. a renderable element, a collision mapping, etc) */
    [Serializable]
    public class CathodeResourceReference : ICloneable
    {
        public static bool operator ==(CathodeResourceReference x, CathodeResourceReference y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);

            if (x.position != y.position) return false;
            if (x.rotation != y.rotation) return false;
            if (x.resourceID != y.resourceID) return false;
            if (x.entryType != y.entryType) return false;
            if (x.startIndex != y.startIndex) return false;
            if (x.count != y.count) return false;
            if (x.entityID != y.entityID) return false;

            return true;
        }
        public static bool operator !=(CathodeResourceReference x, CathodeResourceReference y)
        {
            return !(x == y);
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }

        public override bool Equals(object obj)
        {
            return obj is CathodeResourceReference reference &&
                   EqualityComparer<Vector3>.Default.Equals(position, reference.position) &&
                   EqualityComparer<Vector3>.Default.Equals(rotation, reference.rotation) &&
                   EqualityComparer<ShortGuid>.Default.Equals(resourceID, reference.resourceID) &&
                   entryType == reference.entryType &&
                   startIndex == reference.startIndex &&
                   count == reference.count &&
                   EqualityComparer<ShortGuid>.Default.Equals(entityID, reference.entityID);
        }

        public override int GetHashCode()
        {
            int hashCode = -1286985782;
            hashCode = hashCode * -1521134295 + position.GetHashCode();
            hashCode = hashCode * -1521134295 + rotation.GetHashCode();
            hashCode = hashCode * -1521134295 + resourceID.GetHashCode();
            hashCode = hashCode * -1521134295 + entryType.GetHashCode();
            hashCode = hashCode * -1521134295 + startIndex.GetHashCode();
            hashCode = hashCode * -1521134295 + count.GetHashCode();
            hashCode = hashCode * -1521134295 + entityID.GetHashCode();
            return hashCode;
        }

        public Vector3 position;
        public Vector3 rotation;

        public ShortGuid resourceID;
        public CathodeResourceReferenceType entryType;

        public int startIndex = -1;
        public int count = 1;

        public ShortGuid entityID;
    }

    /* An entity in a composite */
    [Serializable]
    public class CathodeEntity : IComparable<CathodeEntity>
    {
        public CathodeEntity(ShortGuid id)
        {
            shortGUID = id;
        }
        
        public ShortGuid shortGUID; //Translates to string in COMMANDS.BIN dump
        public EntityVariant variant = EntityVariant.NOT_SETUP;

        public List<CathodeEntityLink> childLinks = new List<CathodeEntityLink>();
        public List<CathodeLoadedParameter> parameters = new List<CathodeLoadedParameter>();
        public List<CathodeResourceReference> resources = new List<CathodeResourceReference>();

        public int CompareTo(CathodeEntity other)
        {
            int TotalThis = shortGUID.val[0] + shortGUID.val[1] + shortGUID.val[2] + shortGUID.val[3];
            int TotalOther = other.shortGUID.val[0] + other.shortGUID.val[1] + other.shortGUID.val[2] + other.shortGUID.val[3];
            if (TotalThis > TotalOther) return 1;
            else if (TotalThis == TotalOther) return 0;
            return -1;
        }
    }
    [Serializable]
    public class DatatypeEntity : CathodeEntity
    {
        public DatatypeEntity(ShortGuid id) : base(id) { variant = EntityVariant.DATATYPE; }
        public CathodeDataType type = CathodeDataType.NO_TYPE;
        public ShortGuid parameter; //Translates to string in COMMANDS.BIN dump
    }
    [Serializable]
    public class FunctionEntity : CathodeEntity
    {
        public FunctionEntity(ShortGuid id) : base(id) { variant = EntityVariant.FUNCTION; }
        public ShortGuid function; 
    }
    [Serializable]
    public class CAGEAnimation : FunctionEntity
    {
        public CAGEAnimation(ShortGuid id) : base(id) { function = ShortGuidUtils.Generate("CAGEAnimation"); }
        public List<CathodeParameterKeyframeHeader> keyframeHeaders = new List<CathodeParameterKeyframeHeader>();
        public List<CathodeParameterKeyframe> keyframeData = new List<CathodeParameterKeyframe>();
        public List<TEMP_CAGEAnimationExtraDataHolder3> paramsData3 = new List<TEMP_CAGEAnimationExtraDataHolder3>(); //events?
    }
    [Serializable]
    public class TriggerSequence : FunctionEntity
    {
        public TriggerSequence(ShortGuid id) : base(id) { function = ShortGuidUtils.Generate("TriggerSequence"); }
        public List<CathodeTriggerSequenceTrigger> triggers = new List<CathodeTriggerSequenceTrigger>();
        public List<CathodeTriggerSequenceEvent> events = new List<CathodeTriggerSequenceEvent>();
    }
    [Serializable]
    public class ProxyEntity : CathodeEntity
    {
        public ProxyEntity(ShortGuid id) : base(id) { variant = EntityVariant.PROXY; }
        //todo: what does the proxy nodeID translate to? is it a composite id?
        public ShortGuid extraId; //todo: what is this?
        public List<ShortGuid> hierarchy = new List<ShortGuid>();
    }
    [Serializable]
    public class OverrideEntity : CathodeEntity
    {
        public OverrideEntity(ShortGuid id) : base(id) { variant = EntityVariant.OVERRIDE; }
        public ShortGuid checksum; //TODO: This value is apparently a hash of the hierarchy GUIDs, but need to verify that, and work out the salt.
        public List<ShortGuid> hierarchy = new List<ShortGuid>();
    }
    public enum EntityVariant
    {
        DATATYPE,
        FUNCTION,

        PROXY,
        OVERRIDE,

        NOT_SETUP,
    }

    /* A script composite containing entities */
    [Serializable]
    public class CathodeComposite
    {
        public ShortGuid shortGUID;  //The id when this composite is used as an entity in another composite
        public string name = ""; //The string name of the composite

        public OffsetPair unknownPair;

        public List<CathodeEntity> unknowns = new List<CathodeEntity>(); //These entities are generated using info from links & parameters. I know nothing else about them.

        public List<DatatypeEntity> datatypes = new List<DatatypeEntity>();
        public List<FunctionEntity> functions = new List<FunctionEntity>();

        public List<OverrideEntity> overrides = new List<OverrideEntity>();
        public List<ProxyEntity> proxies = new List<ProxyEntity>();

        public List<CathodeResourceReference> resources = new List<CathodeResourceReference>(); //Resources are per-entity, and also per-composite!

        /* If an entity exists in the composite, return it */
        public CathodeEntity GetEntityByID(ShortGuid id)
        {
            foreach (CathodeEntity entity in datatypes) if (entity.shortGUID == id) return entity;
            foreach (CathodeEntity entity in functions) if (entity.shortGUID == id) return entity;
            foreach (CathodeEntity entity in overrides) if (entity.shortGUID == id) return entity;
            foreach (CathodeEntity entity in proxies) if (entity.shortGUID == id) return entity;
            foreach (CathodeEntity entity in unknowns) if (entity.shortGUID == id) return entity;
            return null;
        }

        /* Returns a collection of all entities in the composite */
        public List<CathodeEntity> GetEntities()
        {
            List<CathodeEntity> toReturn = new List<CathodeEntity>();
            toReturn.AddRange(datatypes);
            toReturn.AddRange(functions);
            toReturn.AddRange(overrides);
            toReturn.AddRange(proxies);
            toReturn.AddRange(unknowns);
            return toReturn;
        }

        /* Sort all entity arrays */
        public void SortEntities()
        {
            datatypes.OrderBy(o => o.shortGUID.ToUInt32());
            functions.OrderBy(o => o.shortGUID.ToUInt32());
            overrides.OrderBy(o => o.shortGUID.ToUInt32());
            proxies.OrderBy(o => o.shortGUID.ToUInt32());
            unknowns.OrderBy(o => o.shortGUID.ToUInt32());
        }
    }
}
