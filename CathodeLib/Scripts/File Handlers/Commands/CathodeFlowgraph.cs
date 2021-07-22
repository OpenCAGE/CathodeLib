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
        DEFINE_CONNECTIONS,           //Defines the links between entities in the flowgraph
        DEFINE_PARAMETERS,            //Defines parameters to be applied to entities in the flowgraph 
        DEFINE_OVERRIDES,             //Defines overrides to apply to nested instances of flowgraphs in this flowgraph
        DEFINE_OVERRIDES_CHECKSUM,    //Defines a checksum value for the hierarchy override (TODO)
        DEFINE_EXPOSED_VARIABLES,     //Defines variables which are exposed when instancing this flowgraph which are then connected in to entities (think variable pins in UE4 blueprint)
        DEFINE_PROXIES,               //Defines "proxies" similar to the overrides hierarchy (TODO)
        DEFINE_FUNCTION_NODES,        //Defines entities with an attached script function within Cathode
        DEFINE_RENDERABLE_DATA,       //Defines renderable data which is referenced by entities in this flowgraph
        DEFINE_CAGEANIMATION_DATA,    //Appears to define additional data for CAGEAnimation type nodes (TODO)
        DEFINE_TRIGGERSEQUENCE_DATA,  //Appears to define additional data for TriggerSequence type nodes (TODO)

        UNUSED,                       //Unused values
        UNKNOWN_COUNTS,               //TODO

        NUMBER_OF_SCRIPT_BLOCKS,      //THIS IS NOT A DATA BLOCK: merely used as an easy way of sanity checking the number of blocks in-code!
    }

    /* A reference to a parameter in a flowgraph */
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CathodeParameterReference
    {
        public cGUID paramID; //The ID of the param in the node
        public int offset;        //The offset of the param this reference points to (in memory this is *4)
        //public int editOffset;    //The offset in the PAK that this reference is

        public void UpdateOffset(int _offset)
        {
            offset = _offset;
        }
    }

    /* A hierarchical override applied to nodes in included flowgraphs, for bespoke functionality */
    public class CathodeFlowgraphHierarchyOverride
    {
        public cGUID id; //The unique ID of this override
        public cGUID checksum; //TODO: This value is apparently a hash of the hierarchy GUIDs, but need to verify that, and work out the salt.
        public List<cGUID> hierarchy = new List<cGUID>(); //Lists the nodeIDs to jump through (flowgraph refs) to get to the node that is being overridden, then that node's ID
        public List<CathodeParameterReference> paramRefs = new List<CathodeParameterReference>(); //Refererence to parameter to apply to the node being overidden
    }

    /* A "proxy" - still need to work out more about this, seems very similar to the hierarchy override above */
    public class CathodeProxy
    {
        public CathodeProxy(cGUID _id)
        {
            id = _id;
        }

        public cGUID id; //todo: is this actually flowgraph id?
        public cGUID extraId; //todo: what is this?
        public List<cGUID> hierarchy = new List<cGUID>();
        public List<CathodeParameterReference> paramRefs = new List<CathodeParameterReference>(); 
    }

    /* A resource that references a REnDerable elementS DB entry */
    public class CathodeResourceReference
    {
        public int editOffset; //The offset in the PAK that this is for temp rewrite logic

        public cGUID resourceRefID;                   //The ID of this entry?
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
    public class CathodeNode
    {
        public bool HasNodeType { get { return nodeType.val != null; } }
        public bool HasDataType { get { return dataType != CathodeDataType.NONE; } }

        public cGUID nodeID;   //Nodes always have a unique ID
        public cGUID nodeType; //Some nodes are of a node type

        public CathodeDataType dataType = CathodeDataType.NONE; //If nodes have no type, they're of a data type
        public cGUID dataTypeParam;                             //Data type nodes have a parameter ID

        public List<CathodeNodeLink> childLinks = new List<CathodeNodeLink>();
        public List<CathodeParameterReference> nodeParameterReferences = new List<CathodeParameterReference>();

        public CathodeParameterReference GetParameterReferenceByID(cGUID id)
        {
            return nodeParameterReferences.FirstOrDefault(o => o.paramID == id);
        }
    }

    /* A script flowgraph containing nodes with parameters */
    public class CathodeFlowgraph
    {
        public cGUID globalID;  //The four byte identifier code of the flowgraph global to all commands.paks
        public cGUID uniqueID;  //The four byte identifier code of the flowgraph unique to commands.pak
        public cGUID nodeID;    //The id when this flowgraph is used as a prefab node in another flowgraph
        public string name = ""; //The string name of the flowgraph

        public List<CathodeNode> nodes = new List<CathodeNode>();
        public List<CathodeResourceReference> resources = new List<CathodeResourceReference>();
        public List<CathodeFlowgraphHierarchyOverride> overrides = new List<CathodeFlowgraphHierarchyOverride>();
        public List<CathodeProxy> proxies = new List<CathodeProxy>();

        /* If a node exists in the flowgraph, return it - otherwise create it, and return it */
        public CathodeNode GetNodeByID(cGUID id)
        {
            foreach (CathodeNode node in nodes)
            {
                if (node.nodeID == id) return node;
            }
            CathodeNode newNode = new CathodeNode();
            newNode.nodeID = id;
            nodes.Add(newNode);
            return newNode;
        }

        /* Get child node override by ID - otherwise create it, and return it */
        public CathodeFlowgraphHierarchyOverride GetChildOverrideByID(cGUID id)
        {
            CathodeFlowgraphHierarchyOverride val = overrides.FirstOrDefault(o => o.id == id);
            if (val == null)
            {
                val = new CathodeFlowgraphHierarchyOverride();
                val.id = id;
            }
            return val;
        }
        
        /* Get resource references by ID */
        public List<CathodeResourceReference> GetResourceReferencesByID(cGUID id)
        {
            return resources.FindAll(o => o.resourceID == id);
        }
    }

    /* Temp holder for offset pairs */
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct OffsetPair
    {
        public int GlobalOffset;
        public int EntryCount;

        public OffsetPair(int _go, int _ec)
        {
            GlobalOffset = _go;
            EntryCount = _ec;
        }
        public OffsetPair(long _go, int _ec)
        {
            GlobalOffset = (int)_go;
            EntryCount = _ec;
        }
    }
}
