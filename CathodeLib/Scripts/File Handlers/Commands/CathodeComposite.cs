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
        public CathodeLoadedParameter(cGUID id, CathodeParameter cont)
        {
            shortGUID = id;
            content = cont;
        }

        public cGUID shortGUID; //The ID of the param in the entity
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

            if (x.resourceRefID != y.resourceRefID) return false;
            if (x.unknownID1 != y.unknownID1) return false;
            if (x.unknownID2 != y.unknownID2) return false;
            if (x.positionOffset != y.positionOffset) return false;
            if (x.resourceID != y.resourceID) return false;
            if (x.entryType != y.entryType) return false;
            if (x.entryIndexREDS != y.entryIndexREDS) return false;
            if (x.entryCountREDS != y.entryCountREDS) return false;
            if (x.unknownInteger1 != y.unknownInteger1) return false;
            if (x.unknownInteger2 != y.unknownInteger2) return false;
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

        public cGUID resourceRefID;                   //The ID of this entry?
        public cGUID unknownID1;
        public cGUID unknownID2;
        public Vector3 positionOffset;     //The 3D position to offset the resource by
        public cGUID resourceID;                      //This is the ID also contained in the RESOURCE_ID parameter list
        public CathodeResourceReferenceType entryType; //This is the type of resource entry

        //For type REDS_REFERENCE
        public int entryIndexREDS;                     //The index in REDS.BIN
        public int entryCountREDS;                     //The count in REDS.BIN

        //For type UNKNOWN_REFERENCE & others
        public int unknownInteger1;
        public int unknownInteger2;
        public cGUID entityID;
    }

    /* An entity in a composite */
    [Serializable]
    public class CathodeEntity : IComparable<CathodeEntity>
    {
        public CathodeEntity(cGUID id)
        {
            shortGUID = id;
        }
        
        public cGUID shortGUID; //Translates to string in COMMANDS.BIN dump
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
        public DatatypeEntity(cGUID id) : base(id) { variant = EntityVariant.DATATYPE; }
        public CathodeDataType type = CathodeDataType.NO_TYPE;
        public cGUID parameter; //Translates to string in COMMANDS.BIN dump
    }
    [Serializable]
    public class FunctionEntity : CathodeEntity
    {
        public FunctionEntity(cGUID id) : base(id) { variant = EntityVariant.FUNCTION; }
        public cGUID function; 
    }
    [Serializable]
    public class CAGEAnimation : FunctionEntity
    {
        public CAGEAnimation(cGUID id) : base(id) { function = Utilities.GenerateGUID("CAGEAnimation"); }
        public List<CathodeParameterKeyframeHeader> keyframeHeaders = new List<CathodeParameterKeyframeHeader>();
        public List<CathodeParameterKeyframe> keyframeData = new List<CathodeParameterKeyframe>();
        public List<TEMP_CAGEAnimationExtraDataHolder3> paramsData3 = new List<TEMP_CAGEAnimationExtraDataHolder3>(); //events?
    }
    [Serializable]
    public class TriggerSequence : FunctionEntity
    {
        public TriggerSequence(cGUID id) : base(id) { function = Utilities.GenerateGUID("TriggerSequence"); }
        public List<TEMP_TriggerSequenceExtraDataHolder1> triggers = new List<TEMP_TriggerSequenceExtraDataHolder1>();
        public List<TEMP_TriggerSequenceExtraDataHolder2> events = new List<TEMP_TriggerSequenceExtraDataHolder2>();
    }
    [Serializable]
    public class ProxyEntity : CathodeEntity
    {
        public ProxyEntity(cGUID id) : base(id) { variant = EntityVariant.PROXY; }
        //todo: what does the proxy nodeID translate to? is it a composite id?
        public cGUID extraId; //todo: what is this?
        public List<cGUID> hierarchy = new List<cGUID>();
    }
    [Serializable]
    public class OverrideEntity : CathodeEntity
    {
        public OverrideEntity(cGUID id) : base(id) { variant = EntityVariant.OVERRIDE; }
        public cGUID checksum; //TODO: This value is apparently a hash of the hierarchy GUIDs, but need to verify that, and work out the salt.
        public List<cGUID> hierarchy = new List<cGUID>();
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
        public cGUID shortGUID;  //The id when this composite is used as an entity in another composite
        public string name = ""; //The string name of the composite

        public OffsetPair unknownPair;

        public List<CathodeEntity> unknowns = new List<CathodeEntity>(); //These entities are generated using info from links & parameters. I know nothing else about them.

        public List<DatatypeEntity> datatypes = new List<DatatypeEntity>();
        public List<FunctionEntity> functions = new List<FunctionEntity>();

        public List<OverrideEntity> overrides = new List<OverrideEntity>();
        public List<ProxyEntity> proxies = new List<ProxyEntity>();

        public List<CathodeResourceReference> resources = new List<CathodeResourceReference>(); //Resources are per-entity, and also per-composite!

        /* If an entity exists in the composite, return it */
        public CathodeEntity GetEntityByID(cGUID id)
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
