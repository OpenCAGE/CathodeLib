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
    public enum CathodeScriptBlocks
    {
        //These are +1 comapred to the ones in isolation_testground because we pull the block that skips first.

        DEFINE_NODE_LINKS = 1,                //This defines the logic links between nodes
        DEFINE_NODE_PARAMETERS = 2,           //This defines executable nodes with parameters 
        DEFINE_ENV_MODEL_REF_LINKS = 3,       //This appears to define links through flowgraphs to EnvironmentModelReference nodes
        DEFINE_ENV_MODEL_REF_LINKS_EXTRA = 4, //This appears to define 4-bytes of extra information for the links defined in the previous block
        DEFINE_NODE_DATATYPES = 5,            //This defines variable nodes which connect to other executable nodes to provide parameters: these seem to be exposed to other flowgraphs as parameters if the flowgraph is used as a type
        DEFINE_LINKED_NODES = 6,              //This defines a connected node through the flowgraph hierarchy 
        DEFINE_NODE_NODETYPES = 7,            //This defines the type ID for all executable nodes (completes the list from the parameter population in step 2) 
        DEFINE_RENDERABLE_ELEMENTS = 8,       //This defines resources used for rendering, etc - E.G. a reference to a model renderable comp
        UNKNOWN_8 = 9,                        //
        DEFINE_ZONE_CONTENT = 10              //This defines zone content data for Zone nodes
    }

    /* A unique id assigned to CATHODE objects */
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cGUID
    {
        public cGUID(byte[] id)
        {
            val = id;
        }
        public cGUID(BinaryReader reader)
        {
            val = reader.ReadBytes(4);
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] val;

        public override bool Equals(object obj)
        {
            if (!(obj is cGUID)) return false;
            return ((cGUID)obj).val.SequenceEqual(this.val);
        }
        public static bool operator ==(cGUID x, cGUID y)
        {
            return x.val.SequenceEqual(y.val);
        }
        public static bool operator !=(cGUID x, cGUID y)
        {
            return !x.val.SequenceEqual(y.val);
        }
        public override int GetHashCode()
        {
            return BitConverter.ToInt32(val, 0);
        }
    }

    /* Defines a link between parent and child IDs, with a connection ID */
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CathodeNodeLink
    {
        public cGUID connectionID;  //The unique ID for this connection
        //public cGUID parentID;      //The ID of the node we're connecting from, providing the value
        public cGUID parentParamID; //The ID of the parameter we're providing out of this node
        public cGUID childID;       //The ID of the node we're linking to to provide the value for
        public cGUID childParamID;  //The ID of the parameter we're providing into the child
    }

    /* A reference to a parameter in a flowgraph */
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CathodeParameterReference
    {
        public cGUID paramID; //The ID of the param in the node
        public int offset;        //The offset of the param this reference points to (in memory this is *4)
        //public int editOffset;    //The offset in the PAK that this reference is
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
        public List<CathodeNodeLink> links = new List<CathodeNodeLink>();
        public List<CathodeResourceReference> resources = new List<CathodeResourceReference>();

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

        /* Get all child links for a node */
        /*
        public List<CathodeNodeLink> GetChildLinksByID(cGUID id)
        {
            return links.FindAll(o => o.parentID == id);
        }
        */

        /* Get all parent links for a node */
        public List<CathodeNodeLink> GetParentLinksByID(cGUID id)
        {
            return links.FindAll(o => o.childID == id);
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
    }
}
