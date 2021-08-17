using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
using System.Runtime.InteropServices;
#endif

namespace CATHODE.Commands
{
    /* Blocks of data in each compiled flowgraph */
    public enum CommandsDataBlock
    {
        DEFINE_SCRIPT_HEADER,         //Defines the header of the flowgraph, with global ID and string name
        ENTITY_CONNECTIONS,           //Defines the links between entities in the flowgraph
        ENTITY_PARAMETERS,            //Defines parameters to be applied to entities in the flowgraph 
        ENTITY_OVERRIDES,             //Defines overrides to apply to nested instances of flowgraphs in this flowgraph
        ENTITY_OVERRIDES_CHECKSUM,    //Defines a checksum value for the hierarchy override (TODO)
        FLOWGRAPH_EXPOSED_PARAMETERS,     //Defines variables which are exposed when instancing this flowgraph which are then connected in to entities (think variable pins in UE4 blueprint)
        ENTITY_PROXIES,               //Defines "proxies" similar to the overrides hierarchy (TODO)
        ENTITY_FUNCTIONS,        //Defines entities with an attached script function within Cathode
        RENDERABLE_DATA,       //Defines renderable data which is referenced by entities in this flowgraph
        CAGEANIMATION_DATA,    //Appears to define additional data for CAGEAnimation type nodes (TODO)
        TRIGGERSEQUENCE_DATA,  //Appears to define additional data for TriggerSequence type nodes (TODO)

        UNUSED,                       //Unused values
        UNKNOWN_COUNTS,               //TODO

        NUMBER_OF_SCRIPT_BLOCKS,      //THIS IS NOT A DATA BLOCK: merely used as an easy way of sanity checking the number of blocks in-code!
    }

    /* A reference to a parameter in a flowgraph */
    public class CathodeLoadedParameter
    {
        public CathodeLoadedParameter(cGUID id, CathodeParameter cont)
        {
            paramID = id;
            content = cont;
        }

        public cGUID paramID; //The ID of the param in the node
        public CathodeParameter content = null;
    }

    /* A resource that references a REnDerable elementS DB entry */
    public class CathodeResourceReference
    {
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
        public int unknownInteger;
        public cGUID nodeID;
    }

    /* A node in a flowgraph */
    public class CathodeEntity
    {
        public CathodeEntity(cGUID id)
        {
            nodeID = id;
        }
        
        public cGUID nodeID; //Translates to string in COMMANDS.BIN dump
        public EntityVariant variant = EntityVariant.NOT_SETUP;

        public List<CathodeNodeLink> childLinks = new List<CathodeNodeLink>();
        public List<CathodeLoadedParameter> parameters = new List<CathodeLoadedParameter>();
    }
    public class DatatypeEntity : CathodeEntity
    {
        public DatatypeEntity(cGUID id) : base(id) { variant = EntityVariant.DATATYPE; }
        public CathodeDataType type = CathodeDataType.NO_TYPE;
        public cGUID parameter; //Translates to string in COMMANDS.BIN dump
    }
    public class FunctionEntity : CathodeEntity
    {
        public FunctionEntity(cGUID id) : base(id) { variant = EntityVariant.FUNCTION; }
        public cGUID function; 
    }
    public class CAGEAnimation : FunctionEntity
    {
        public CAGEAnimation(cGUID id) : base(id) { function = Utilities.GenerateGUID("CAGEAnimation"); }
        public List<TEMP_CAGEAnimationExtraDataHolder1> paramsData1 = new List<TEMP_CAGEAnimationExtraDataHolder1>();
        public List<TEMP_CAGEAnimationExtraDataHolder2> paramsData2 = new List<TEMP_CAGEAnimationExtraDataHolder2>();
        public List<TEMP_CAGEAnimationExtraDataHolder3> paramsData3 = new List<TEMP_CAGEAnimationExtraDataHolder3>();
    }
    public class TriggerSequence : FunctionEntity
    {
        public TriggerSequence(cGUID id) : base(id) { function = Utilities.GenerateGUID("TriggerSequence"); }
        public List<TEMP_TriggerSequenceExtraDataHolder1> triggers = new List<TEMP_TriggerSequenceExtraDataHolder1>();
        public List<TEMP_TriggerSequenceExtraDataHolder2> events = new List<TEMP_TriggerSequenceExtraDataHolder2>();
    }
    public class ProxyEntity : CathodeEntity
    {
        public ProxyEntity(cGUID id) : base(id) { variant = EntityVariant.PROXY; }
        //todo: what does the proxy nodeID translate to? is it a flowgraph id?
        public cGUID extraId; //todo: what is this?
        public List<cGUID> hierarchy = new List<cGUID>();
    }
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

    /* A script flowgraph containing entities */
    public class CathodeFlowgraph
    {
        public cGUID globalID;  //cGUID generated from flowgraph name
        public cGUID nodeID;    //The id when this flowgraph is used as a prefab node in another flowgraph
        public string name = ""; //The string name of the flowgraph

        public OffsetPair unknownPair;

        public List<CathodeEntity> unknowns = new List<CathodeEntity>(); //These entities are generated using info from links & parameters. I know nothing else about them.

        public List<DatatypeEntity> datatypes = new List<DatatypeEntity>();
        public List<FunctionEntity> functions = new List<FunctionEntity>();

        public List<OverrideEntity> overrides = new List<OverrideEntity>();
        public List<ProxyEntity> proxies = new List<ProxyEntity>();

        public List<CathodeResourceReference> resources = new List<CathodeResourceReference>();

        /* If an entity exists in the flowgraph, return it */
        public CathodeEntity GetEntityByID(cGUID id)
        {
            foreach (CathodeEntity entity in datatypes) if (entity.nodeID == id) return entity;
            foreach (CathodeEntity entity in functions) if (entity.nodeID == id) return entity;
            foreach (CathodeEntity entity in overrides) if (entity.nodeID == id) return entity;
            foreach (CathodeEntity entity in proxies) if (entity.nodeID == id) return entity;
            foreach (CathodeEntity entity in unknowns) if (entity.nodeID == id) return entity;
            return null;
        }

        /* Returns a collection of all entities in the flowgraph */
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
    }
}
