//#define TEST_READ
//#define TEST_WRITE

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

//TODO: 
// - Finish write functionality
// - Figure out proxies
// - Improve storage of parameters (don't use references by offset anymore)

namespace CATHODE.Commands
{
    public class CommandsPAK
    {
        /* Load and parse the COMMANDS.PAK */
        public CommandsPAK(string pathToPak)
        {
            path = pathToPak;

            BinaryReader reader = new BinaryReader(File.OpenRead(path));

            ReadHeaderInfo(reader);
            ReadParameters(reader);
            ReadFlowgraphs(reader);

            reader.Close();
        }

        #region ACCESSORS
        /* Return a list of filenames for flowgraphs in the CommandsPAK archive */
        public string[] GetFlowgraphNames()
        {
            string[] toReturn = new string[_flowgraphs.Length];
            for (int i = 0; i < _flowgraphs.Length; i++) toReturn[i] = _flowgraphs[i].name;
            return toReturn;
        }

        /* Find the a script entry object by name */
        public int GetFileIndex(string FileName)
        {
            for (int i = 0; i < _flowgraphs.Length; i++) if (_flowgraphs[i].name == FileName || _flowgraphs[i].name == FileName.Replace('/', '\\')) return i;
            return -1;
        }

        /* Get flowgraph/parameter */
        public CathodeFlowgraph GetFlowgraph(cGUID id)
        {
            if (id.val == null) return null;
            return _flowgraphs.FirstOrDefault(o => o.nodeID == id);
        }
        public CathodeFlowgraph GetFlowgraphByIndex(int index)
        {
            return (index >= _flowgraphs.Length || index < 0) ? null : _flowgraphs[index];
        }
        public CathodeParameter GetParameter(int offset)
        {
            return _parameters.FirstOrDefault(o => o.offset == offset);
        }

        /* Get all flowgraphs/parameters */
        public CathodeFlowgraph[] Flowgraphs { get { return _flowgraphs; } }
        public CathodeParameter[] Parameters { get { return _parameters; } } //TODO: parameters should be instanced per node

        /* Get entry point flowgraph objects */
        public CathodeFlowgraph[] EntryPoints
        {
            get
            {
                if (_entryPointObjects != null) return _entryPointObjects;
                _entryPointObjects = new CathodeFlowgraph[_entryPoints.flowgraphIDs.Length];
                for (int i = 0; i < _entryPoints.flowgraphIDs.Length; i++) _entryPointObjects[i] = GetFlowgraph(_entryPoints.flowgraphIDs[i]);
                return _entryPointObjects;
            }
        }
        #endregion

        #region WRITING
        /* Save all changes back out */
        public void Save()
        {
#if TEST_WRITE
            _parameters = new CathodeParameter[0];
            CathodeFlowgraph[] flows = new CathodeFlowgraph[3];
            for (int i = 0; i < _flowgraphs.Length; i++)
            {
                if (_flowgraphs[i].name == @"P:\CONTENT\BUILD\LEVELS\PRODUCTION\SCI_HUB") flows[0] = _flowgraphs[i];
                if (_flowgraphs[i].name == "GLOBAL") flows[1] = _flowgraphs[i];
                if (_flowgraphs[i].name == "PAUSEMENU") flows[2] = _flowgraphs[i];
            }
            _flowgraphs = flows;
            _flowgraphs[0].name = "dummy_1"; _flowgraphs[1].name = "GLOBAL"; _flowgraphs[2].name = "dummy_2";
            _entryPoints.flowgraphIDs = new cGUID[3] { _flowgraphs[0].nodeID, _flowgraphs[1].nodeID, _flowgraphs[2].nodeID };
#endif

            BinaryWriter writer = new BinaryWriter(File.OpenWrite(path));
            writer.BaseStream.SetLength(0);

            /* Write entry points */
            Utilities.Write<CommandsEntryPoints>(writer, _entryPoints);

            /* Write placeholder info for parameter/flowgraph offsets */
            int offsetToRewrite = (int)writer.BaseStream.Position;
            writer.Write(0); 
            writer.Write(0);
            writer.Write(0); 
            writer.Write(0);

            /* Write out parameters */
            parameterOffsets = new int[_parameters.Length]; 
            for (int i = 0; i < _parameters.Length; i++)
            {
                parameterOffsets[i] = (int)writer.BaseStream.Position / 4;
                Utilities.Write<cGUID>(writer, GetDataTypeGUID(_parameters[i].dataType));
                switch (_parameters[i].dataType)
                {
                    case CathodeDataType.POSITION:
                        Vector3 pos = ((CathodeTransform)_parameters[i]).position;
                        Vector3 rot = ((CathodeTransform)_parameters[i]).rotation;
                        writer.Write(pos.X); writer.Write(pos.Y); writer.Write(pos.Z);
                        writer.Write(rot.Y); writer.Write(rot.X); writer.Write(rot.Z);
                        break;
                    case CathodeDataType.INTEGER:
                        writer.Write(((CathodeInteger)_parameters[i]).value);
                        break;
                    case CathodeDataType.STRING:
                        int stringStart = ((int)writer.BaseStream.Position + 4) / 4;
                        byte[] stringStartRaw = BitConverter.GetBytes(stringStart);
                        stringStartRaw[3] = 0x80; //Not sure why we have to do this
                        writer.Write(stringStartRaw);
                        writer.Write(((CathodeString)_parameters[i]).guid.val);
                        string str = ((CathodeString)_parameters[i]).value;
                        for (int x = 0; x < str.Length; x++) writer.Write(str[x]);
                        writer.Write((char)0x00);
                        Utilities.Align(writer, 4);
                        break;
                    case CathodeDataType.BOOL:
                        if (((CathodeBool)_parameters[i]).value) writer.Write(1); else writer.Write(0);
                        break;
                    case CathodeDataType.FLOAT:
                        writer.Write(((CathodeFloat)_parameters[i]).value);
                        break;
                    case CathodeDataType.SHORT_GUID:
                        Utilities.Write<cGUID>(writer, ((CathodeResource)_parameters[i]).resourceID);
                        break;
                    case CathodeDataType.DIRECTION:
                        Vector3 dir = ((CathodeVector3)_parameters[i]).value;
                        writer.Write(dir.Y); writer.Write(dir.X); writer.Write(dir.Z);
                        break;
                    case CathodeDataType.ENUM:
                        Utilities.Write<cGUID>(writer, ((CathodeEnum)_parameters[i]).enumID);
                        writer.Write(((CathodeEnum)_parameters[i]).enumIndex);
                        break;
                    default:
                        writer.Write(_parameters[i].unknownContent);
                        break;
                }
            }

            /* Write out flowgraphs */
            flowgraphOffsets = new int[_flowgraphs.Length];
            for (int i = 0; i < _flowgraphs.Length; i++)
            {
                int scriptStartPos = (int)writer.BaseStream.Position / 4;

                Utilities.Write<cGUID>(writer, Utilities.GenerateGUID(_flowgraphs[i].name));
                for (int x = 0; x < _flowgraphs[i].name.Length; x++) writer.Write(_flowgraphs[i].name[x]);
                writer.Write((char)0x00);
                Utilities.Align(writer, 4);

                //Work out what we want to write
                List<CathodeNode> nodesWithLinks = new List<CathodeNode>();
                nodesWithLinks.AddRange(_flowgraphs[i].functionNodes.FindAll(o => o.childLinks.Count != 0));
                nodesWithLinks.AddRange(_flowgraphs[i].datatypeNodes.FindAll(o => o.childLinks.Count != 0));

                //List<CathodeParameterReference> paramsToWrite = new List<CathodeParameterReference>();
                //List<CathodeNode> nodesWithParams = _flowgraphs[i].nodes.FindAll(o => o.nodeParameterReferences.Count != 0);
                //List<CathodeFlowgraphHierarchyOverride> overridesWithParams = _flowgraphs[i].overrides.FindAll(o => o.paramRefs.Count != 0);
                //List<CathodeProxy> proxiesWithParams = _flowgraphs[i].proxies.FindAll(o => o.paramRefs.Count != 0);
                //for (int x = 0; x < nodesWithParams.Count; x++) nodesWithParams[x].nodeParameterReferences

                //Write the content out that we will point to in a second
                List<List<OffsetPair>> scriptContentOffsetInfo = new List<List<OffsetPair>>();
                for (int x = 0; x < (int)CommandsDataBlock.NUMBER_OF_SCRIPT_BLOCKS; x++)
                {
                    scriptContentOffsetInfo.Add(new List<OffsetPair>());

                    switch ((CommandsDataBlock)x)
                    {
                        case CommandsDataBlock.DEFINE_SCRIPT_HEADER:
                            scriptContentOffsetInfo[x].Add(new OffsetPair(writer.BaseStream.Position, 2));
                            Utilities.Write<cGUID>(writer, _flowgraphs[i].nodeID);
                            writer.Write(0);
                            break;
                        case CommandsDataBlock.DEFINE_CONNECTIONS:
                            foreach (CathodeNode nodeWithLink in nodesWithLinks)
                            {
                                scriptContentOffsetInfo[x].Add(new OffsetPair(writer.BaseStream.Position, nodeWithLink.childLinks.Count));
                                Utilities.Write<CathodeNodeLink>(writer, nodeWithLink.childLinks);
                            }
                            break;
                        case CommandsDataBlock.DEFINE_PARAMETERS:
                            break;
                        case CommandsDataBlock.DEFINE_OVERRIDES:
                            break;
                        case CommandsDataBlock.DEFINE_OVERRIDES_CHECKSUM:
                            break;
                        case CommandsDataBlock.DEFINE_EXPOSED_VARIABLES:
                            break;
                        case CommandsDataBlock.DEFINE_PROXIES:
                            break;
                        case CommandsDataBlock.DEFINE_FUNCTION_NODES:
                            break;
                        case CommandsDataBlock.DEFINE_RENDERABLE_DATA:
                            break;
                        case CommandsDataBlock.DEFINE_CAGEANIMATION_DATA:
                            break;
                        case CommandsDataBlock.DEFINE_TRIGGERSEQUENCE_DATA:
                            break;
                    }
                }

                //Point to that content we just wrote out
                List<OffsetPair> scriptPointerOffsetInfo = new List<OffsetPair>();
                for (int x = 0; x < (int)CommandsDataBlock.NUMBER_OF_SCRIPT_BLOCKS; x++)
                {
                    switch ((CommandsDataBlock)x)
                    {
                        default:
                            scriptPointerOffsetInfo.Add(new OffsetPair(writer.BaseStream.Position, scriptContentOffsetInfo[x].Count));
                            for (int z = 0; z < scriptContentOffsetInfo[x].Count; z++)
                            {
                                switch ((CommandsDataBlock)x)
                                {
                                    case CommandsDataBlock.DEFINE_CONNECTIONS:
                                        writer.Write(nodesWithLinks[z].nodeID.val);
                                        writer.Write(scriptContentOffsetInfo[x][z].GlobalOffset / 4);
                                        writer.Write(scriptContentOffsetInfo[x][z].EntryCount);
                                        break;
                                    case CommandsDataBlock.DEFINE_PARAMETERS:
                                        //TODO: to write out params we will have to edit how data is stored here.
                                        //Save params to each node rather than just storing references, or reference a stored array not by offset!
                                        break;
                                    case CommandsDataBlock.DEFINE_OVERRIDES:
                                        break;
                                    case CommandsDataBlock.DEFINE_OVERRIDES_CHECKSUM:
                                        break;
                                    case CommandsDataBlock.DEFINE_EXPOSED_VARIABLES:
                                        break;
                                    case CommandsDataBlock.DEFINE_PROXIES:
                                        break;
                                    case CommandsDataBlock.DEFINE_FUNCTION_NODES:
                                        break;
                                    case CommandsDataBlock.DEFINE_RENDERABLE_DATA:
                                        break;
                                    case CommandsDataBlock.DEFINE_CAGEANIMATION_DATA:
                                        break;
                                    case CommandsDataBlock.DEFINE_TRIGGERSEQUENCE_DATA:
                                        break;
                                }
                            }
                            break;
                        case CommandsDataBlock.DEFINE_SCRIPT_HEADER:
                            //We actually just forward on the previous offsets here.
                            scriptPointerOffsetInfo.Add(scriptContentOffsetInfo[x][0]);
                            break;
                        case CommandsDataBlock.UNUSED:
                            scriptPointerOffsetInfo.Add(new OffsetPair(0, 0));
                            break;
                        case CommandsDataBlock.UNKNOWN_COUNTS:
                            //TODO: These count values are unknown. Just writing zeros for now.
                            scriptPointerOffsetInfo.Add(new OffsetPair(0, 0));
                            break;
                    }
                }

                //Write pointers to the pointers of the content
                flowgraphOffsets[i] = (int)writer.BaseStream.Position / 4;
                writer.Write(0);
                for (int x = 0; x < (int)CommandsDataBlock.NUMBER_OF_SCRIPT_BLOCKS; x++)
                {
                    if (x == 0)
                    {
                        byte[] scriptStartRaw = BitConverter.GetBytes(scriptStartPos);
                        scriptStartRaw[3] = 0x80; //Not sure why we have to do this
                        writer.Write(scriptStartRaw);
                    }
                    writer.Write(scriptPointerOffsetInfo[x].GlobalOffset / 4); //offset
                    writer.Write(scriptPointerOffsetInfo[x].EntryCount); //count
                    if (x == 0)
                    {
                        Utilities.Write<cGUID>(writer, _flowgraphs[i].nodeID);
                    }
                }
            }

            /* Write out parameter offsets */
            int parameterOffsetPos = (int)writer.BaseStream.Position;
            Utilities.Write<int>(writer, parameterOffsets);

            /* Write out flowgraph offsets */
            int flowgraphOffsetPos = (int)writer.BaseStream.Position;
            Utilities.Write<int>(writer, flowgraphOffsets);

            /* Rewrite header info with correct offsets */
            writer.BaseStream.Position = offsetToRewrite;
            writer.Write(parameterOffsetPos / 4);
            writer.Write(_parameters.Length);
            writer.Write(flowgraphOffsetPos / 4);
            writer.Write(_flowgraphs.Length);

            writer.Close();
        }
        #endregion

        #region READING
        /* Read offset info & count, jump to the offset & return the count */
        private int JumpToOffset(ref BinaryReader reader)
        {
            CommandsOffsetPair pair = Utilities.Consume<CommandsOffsetPair>(reader);
            reader.BaseStream.Position = pair.offset * 4;
            return pair.count;
        }

        /* Read the parameter and flowgraph offsets & get entry points */
        private void ReadHeaderInfo(BinaryReader reader)
        {
            /* Read entry points */
            _entryPoints = Utilities.Consume<CommandsEntryPoints>(reader);

            /* Initialise parameter info */
            int parameter_offset_pos = reader.ReadInt32() * 4;
            _parameters = new CathodeParameter[reader.ReadInt32()];

            /* Initialise flowgraph info */
            int flowgraph_offset_pos = reader.ReadInt32() * 4;
            _flowgraphs = new CathodeFlowgraph[reader.ReadInt32()];

            /* Archive offsets for parameters */
            reader.BaseStream.Position = parameter_offset_pos;
            parameterOffsets = Utilities.ConsumeArray<int>(reader, _parameters.Length);

            /* Archive offsets for flowgraphs */
            reader.BaseStream.Position = flowgraph_offset_pos;
            flowgraphOffsets = Utilities.ConsumeArray<int>(reader, _flowgraphs.Length);
        }

        /* Read all parameters from the PAK */
        private void ReadParameters(BinaryReader reader)
        {
            reader.BaseStream.Position = parameterOffsets[0] * 4;
            for (int i = 0; i < _parameters.Length; i++)
            {
                int length = (i == _parameters.Length - 1) ? (flowgraphOffsets[0] * 4) - (parameterOffsets[i] * 4) : (parameterOffsets[i + 1] * 4) - (parameterOffsets[i] * 4);
                CathodeParameter this_parameter = new CathodeParameter();
                CathodeDataType this_datatype = GetDataType(reader.ReadBytes(4));
                switch (this_datatype)
                {
                    case CathodeDataType.POSITION:
                        this_parameter = new CathodeTransform();
                        ((CathodeTransform)this_parameter).position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        float _x, _y, _z; _y = reader.ReadSingle(); _x = reader.ReadSingle(); _z = reader.ReadSingle(); //Y,X,Z!
                        ((CathodeTransform)this_parameter).rotation = new Vector3(_x, _y, _z);
                        break;
                    case CathodeDataType.INTEGER:
                        this_parameter = new CathodeInteger();
                        ((CathodeInteger)this_parameter).value = reader.ReadInt32();
                        break;
                    case CathodeDataType.STRING:
                        this_parameter = new CathodeString();
                        reader.BaseStream.Position += 4; //Pointer to next 4 bytes (no reason to read this!)
                        ((CathodeString)this_parameter).guid = new cGUID(reader); //Hashed GUID? Doesn't seem to be validated.
                        ((CathodeString)this_parameter).value = Utilities.ReadString(reader);
                        Utilities.Align(reader, 4);
                        break;
                    case CathodeDataType.BOOL:
                        this_parameter = new CathodeBool();
                        ((CathodeBool)this_parameter).value = (reader.ReadInt32() == 1);
                        break;
                    case CathodeDataType.FLOAT:
                        this_parameter = new CathodeFloat();
                        ((CathodeFloat)this_parameter).value = reader.ReadSingle();
                        break;
                    case CathodeDataType.SHORT_GUID:
                        this_parameter = new CathodeResource();
                        ((CathodeResource)this_parameter).resourceID = new cGUID(reader);
                        break;
                    case CathodeDataType.DIRECTION:
                        this_parameter = new CathodeVector3();
                        float __x, __y, __z; __y = reader.ReadSingle(); __x = reader.ReadSingle(); __z = reader.ReadSingle(); //Y,X,Z!
                        ((CathodeVector3)this_parameter).value = new Vector3(__x, __y, __z);
                        break;
                    case CathodeDataType.ENUM:
                        this_parameter = new CathodeEnum();
                        ((CathodeEnum)this_parameter).enumID = new cGUID(reader);
                        ((CathodeEnum)this_parameter).enumIndex = reader.ReadInt32();
                        break;
                        /*
                    case CathodeDataType.SPLINE_DATA:
                        this_parameter = new CathodeSpline();
                        int start_offset = reader.ReadInt32(); //This just gives us a pointless offset
                        int num_points = reader.ReadInt32();

                        if (length - 12 != num_points * 24)
                        {
                            string dsfsdf = ""; //for some reason some have extra data at the end
                        }

                        for (int x = 0; x < num_points; x++)
                        {
                            CathodeTransform this_point = new CathodeTransform();
                            this_point.position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            this_point.rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            ((CathodeSpline)this_parameter).splinePoints.Add(this_point);
                        }
                        break;
                        */
                    default:
                        this_parameter.unknownContent = reader.ReadBytes(length - 4); //Should never hit this!
                        break;
                }

                this_parameter.offset = parameterOffsets[i];
                this_parameter.dataType = this_datatype;

                _parameters[i] = this_parameter;
            }
        }

        /* Read all flowgraphs from the PAK */
        private void ReadFlowgraphs(BinaryReader reader)
        {
            for (int i = 0; i < _flowgraphs.Length; i++)
            {
                reader.BaseStream.Position = flowgraphOffsets[i] * 4;
                reader.BaseStream.Position += 4; //Skip 0x00,0x00,0x00,0x00
                CathodeFlowgraph flowgraph = new CathodeFlowgraph();

                //Read the offsets and counts
                OffsetPair[] offsetPairs = new OffsetPair[(int)CommandsDataBlock.NUMBER_OF_SCRIPT_BLOCKS];
                int scriptStartOffset = 0;
                for (int x = 0; x < (int)CommandsDataBlock.NUMBER_OF_SCRIPT_BLOCKS; x++)
                {
                    if (x == 0)
                    {
                        byte[] startOffsetRaw = reader.ReadBytes(4);
                        startOffsetRaw[3] = 0x00; //For some reason this is 0x80?
                        scriptStartOffset = BitConverter.ToInt32(startOffsetRaw, 0);
                    }
                    offsetPairs[x] = Utilities.Consume<OffsetPair>(reader);
                    if (x == 0)
                    {
                        flowgraph.nodeID = new cGUID(reader);
                    }
                }

                //Read script ID and string name
                reader.BaseStream.Position = scriptStartOffset * 4;
                flowgraph.globalID = new cGUID(reader);
                flowgraph.name = Utilities.ReadString(reader);
                Utilities.Align(reader, 4);

                //Pull data from those offsets
                List<CommandsEntityLinks> entityLinks = new List<CommandsEntityLinks>();
                List<CommandsParamRefSet> paramRefSets = new List<CommandsParamRefSet>();
                Dictionary<cGUID, cGUID> overrideChecksums = new Dictionary<cGUID, cGUID>();
                for (int x = 0; x < offsetPairs.Length; x++)
                {
                    reader.BaseStream.Position = offsetPairs[x].GlobalOffset * 4;
                    for (int y = 0; y < offsetPairs[x].EntryCount; y++)
                    {
                        switch ((CommandsDataBlock)x)
                        {
                            case CommandsDataBlock.DEFINE_CONNECTIONS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                entityLinks.Add(new CommandsEntityLinks(new cGUID(reader)));
                                int NumberOfParams = JumpToOffset(ref reader);
                                entityLinks[entityLinks.Count - 1].childLinks.AddRange(Utilities.ConsumeArray<CathodeNodeLink>(reader, NumberOfParams));
                                break;
                            }
                            case CommandsDataBlock.DEFINE_PARAMETERS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                paramRefSets.Add(new CommandsParamRefSet(new cGUID(reader)));
                                int NumberOfParams = JumpToOffset(ref reader);
                                paramRefSets[paramRefSets.Count - 1].refs.AddRange(Utilities.ConsumeArray<CathodeParameterReference>(reader, NumberOfParams));
                                break;
                            }
                            case CommandsDataBlock.DEFINE_OVERRIDES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                CathodeOverrideNode overrider = new CathodeOverrideNode(new cGUID(reader));
                                int NumberOfParams = JumpToOffset(ref reader);
                                overrider.hierarchy.AddRange(Utilities.ConsumeArray<cGUID>(reader, NumberOfParams));
                                flowgraph.overrides.Add(overrider);
                                break;
                            }
                            case CommandsDataBlock.DEFINE_OVERRIDES_CHECKSUM:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                overrideChecksums.Add(new cGUID(reader), new cGUID(reader));
                                break;
                            }
                            case CommandsDataBlock.DEFINE_EXPOSED_VARIABLES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                CathodeDatatypeNode thisNode = new CathodeDatatypeNode(new cGUID(reader));
                                thisNode.type = GetDataType(reader.ReadBytes(4));
                                thisNode.parameter = new cGUID(reader);
                                flowgraph.datatypeNodes.Add(thisNode);
                                break;
                            }
                            case CommandsDataBlock.DEFINE_PROXIES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 20);
                                CathodeProxyNode thisProxy = new CathodeProxyNode(new cGUID(reader));
                                int resetPos = (int)reader.BaseStream.Position + 8; //TODO: This is a HACK - I need to rework JumpToOffset to make a temp stream
                                int NumberOfParams = JumpToOffset(ref reader);
                                thisProxy.hierarchy.AddRange(Utilities.ConsumeArray<cGUID>(reader, NumberOfParams)); //Last is always 0x00, 0x00, 0x00, 0x00
                                reader.BaseStream.Position = resetPos;
                                cGUID idCheck = new cGUID(reader);
                                if (idCheck != thisProxy.nodeID) throw new Exception("Proxy ID mismatch!");
                                thisProxy.extraId = new cGUID(reader);
                                flowgraph.proxies.Add(thisProxy);
                                break;
                            }
                            case CommandsDataBlock.DEFINE_FUNCTION_NODES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                CathodeFunctionNode thisNode = new CathodeFunctionNode(new cGUID(reader));
                                thisNode.function = new cGUID(reader);
                                flowgraph.functionNodes.Add(thisNode);
                                break;
                            }
                            //TODO: this case needs a GIANT refactor!
                            case CommandsDataBlock.DEFINE_RENDERABLE_DATA:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 40);

                                //TODO: these values change by entry type - need to work out what they're for before allowing editing
                                CathodeResourceReference resource_ref = new CathodeResourceReference();
                                resource_ref.resourceRefID = new cGUID(reader); //renderable element ID (also used in one of the param blocks for something)
                                reader.BaseStream.Position += 4; //unk (always 0x00 x4?)
                                resource_ref.positionOffset = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); //position offset
                                reader.BaseStream.Position += 4; //unk (always 0x00 x4?)
                                resource_ref.resourceID = new cGUID(reader); //resource id
                                resource_ref.entryType = GetResourceEntryType(reader.ReadBytes(4)); //entry type
                                switch (resource_ref.entryType)
                                {
                                    case CathodeResourceReferenceType.RENDERABLE_INSTANCE:
                                        resource_ref.entryIndexREDS = reader.ReadInt32(); //REDS.BIN entry index
                                        resource_ref.entryCountREDS = reader.ReadInt32(); //REDS.BIN entry count
                                        break;
                                    case CathodeResourceReferenceType.COLLISION_MAPPING:
                                        resource_ref.unknownInteger = reader.ReadInt32(); //unknown integer (COLLISION.MAP index?)
                                        resource_ref.nodeID = new cGUID(reader); //ID which maps to the node using the resource (?) - check GetFriendlyName
                                        break;
                                    case CathodeResourceReferenceType.EXCLUSIVE_MASTER_STATE_RESOURCE:
                                    case CathodeResourceReferenceType.NAV_MESH_BARRIER_RESOURCE:
                                    case CathodeResourceReferenceType.TRAVERSAL_SEGMENT:
                                        reader.BaseStream.Position += 8; //just two -1 32-bit integers for some reason
                                        break;
                                    case CathodeResourceReferenceType.ANIMATED_MODEL:
                                    case CathodeResourceReferenceType.DYNAMIC_PHYSICS_SYSTEM:
                                        resource_ref.unknownInteger = reader.ReadInt32(); //unknown integer
                                        reader.BaseStream.Position += 4;
                                        break;
                                }
                                flowgraph.resources.Add(resource_ref);
                                break;
                            }
                            case CommandsDataBlock.DEFINE_CAGEANIMATION_DATA:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                reader.BaseStream.Position = (reader.ReadInt32() * 4);

                                CathodeNode animationNode = flowgraph.GetNodeByID(new cGUID(reader)); //CAGEAnimation to apply the following data to

                                //TODO refactor the JumpToOffset function to allow it to be used here
                                int OffsetToFindParams = reader.ReadInt32() * 4;
                                int NumberOfParams = reader.ReadInt32();
                                for (int z = 0; z < NumberOfParams; z++)
                                {
                                    reader.BaseStream.Position = OffsetToFindParams + (z * 32);

                                    cGUID unk1 = new cGUID(reader);//Unknown ID (does this link to unknown param ID on CAGEAnimation nodes?

                                    CathodeDataType unk2 = GetDataType(new cGUID(reader)); //Datatype... used for?
                                    cGUID unk3 = new cGUID(reader); //Unknown ID (does this link to unknown param ID on CAGEAnimation nodes?
                                    cGUID unk4 = new cGUID(reader); //Unknown ID - is this a named parameter id? (does this link to unknown param ID on CAGEAnimation nodes?

                                    CathodeDataType unk5 = GetDataType(new cGUID(reader)); //Datatype... used for?
                                    cGUID unk6 = new cGUID(reader); //Unknown ID (does this link to unknown param ID on CAGEAnimation nodes?

                                    int NumberOfParams2 = JumpToOffset(ref reader);
                                    List<cGUID> unk7_hierarchy = Utilities.ConsumeArray<cGUID>(reader, NumberOfParams2).ToList<cGUID>(); //Last is always 0x00, 0x00, 0x00, 0x00
                                }
                                break;
                            }
                            case CommandsDataBlock.DEFINE_TRIGGERSEQUENCE_DATA:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                reader.BaseStream.Position = (reader.ReadInt32() * 4);

                                CathodeNode thisNode = flowgraph.GetNodeByID(new cGUID(reader)); //TriggerSequence to apply the following data to

                                //TODO refactor the JumpToOffset function to allow it to be used here
                                int OffsetToFindParams = reader.ReadInt32() * 4;
                                int NumberOfParams = reader.ReadInt32();
                                for (int z = 0; z < NumberOfParams; z++)
                                {
                                    reader.BaseStream.Position = OffsetToFindParams + (z * 12);

                                    int OffsetToFindParams2 = reader.ReadInt32() * 4;
                                    int NumberOfParams2 = reader.ReadInt32();

                                    float TriggerAfterSeconds = reader.ReadSingle(); //This defines how long we wait (sequentially) before triggering the node referenced in the hierarchy below (in seconds)

                                    reader.BaseStream.Position = OffsetToFindParams2;
                                    List<cGUID> HierarchyToTrigger = Utilities.ConsumeArray<cGUID>(reader, NumberOfParams2).ToList<cGUID>(); //Last is always 0x00, 0x00, 0x00, 0x00
                                }
                                break;
                            }
                        }
                    }
                }

                for (int x = 0; x < flowgraph.overrides.Count; x++)
                {
                    flowgraph.overrides[x].checksum = overrideChecksums[flowgraph.overrides[x].nodeID];
                }
                for (int x = 0; x < entityLinks.Count; x++)
                {
                    CathodeNode nodeToApply = flowgraph.GetNodeByID(entityLinks[x].parentID);
                    if (nodeToApply == null)
                    {
                        continue; //TODO: We shouldn't hit this, but we do...
                    }
                    if (nodeToApply != null) nodeToApply.childLinks.AddRange(entityLinks[x].childLinks);
                }
                for (int x = 0; x < paramRefSets.Count; x++)
                {
                    CathodeNode nodeToApply = flowgraph.GetNodeByID(paramRefSets[x].id);
                    if (nodeToApply == null)
                    {
                        continue; //TODO: We shouldn't hit this, but we do...
                    }
                    if (nodeToApply != null) nodeToApply.nodeParameterReferences.AddRange(paramRefSets[x].refs);
                }

                _flowgraphs[i] = flowgraph;
            }
        }
        #endregion

        #region LOOKUP_TABLES
        private Dictionary<cGUID, CathodeDataType> _dataTypeLUT = new Dictionary<cGUID, CathodeDataType>();
        private void SetupDataTypeLUT()
        {
            if (_dataTypeLUT.Count == 0)
            {
                _dataTypeLUT.Add(Utilities.GenerateGUID("bool"), CathodeDataType.BOOL);
                _dataTypeLUT.Add(Utilities.GenerateGUID("int"), CathodeDataType.INTEGER);
                _dataTypeLUT.Add(Utilities.GenerateGUID("float"), CathodeDataType.FLOAT);
                _dataTypeLUT.Add(Utilities.GenerateGUID("String"), CathodeDataType.STRING);
                _dataTypeLUT.Add(Utilities.GenerateGUID("FilePath"), CathodeDataType.FILEPATH);
                _dataTypeLUT.Add(Utilities.GenerateGUID("SplineData"), CathodeDataType.SPLINE_DATA);
                _dataTypeLUT.Add(Utilities.GenerateGUID("Direction"), CathodeDataType.DIRECTION);
                _dataTypeLUT.Add(Utilities.GenerateGUID("Position"), CathodeDataType.POSITION);
                _dataTypeLUT.Add(Utilities.GenerateGUID("Enum"), CathodeDataType.ENUM);
                _dataTypeLUT.Add(Utilities.GenerateGUID("ShortGuid"), CathodeDataType.SHORT_GUID);
                _dataTypeLUT.Add(Utilities.GenerateGUID("Object"), CathodeDataType.OBJECT);
                _dataTypeLUT.Add(Utilities.GenerateGUID("ZonePtr"), CathodeDataType.ZONE_PTR);
                _dataTypeLUT.Add(Utilities.GenerateGUID("ZoneLinkPtr"), CathodeDataType.ZONE_LINK_PTR);
                _dataTypeLUT.Add(Utilities.GenerateGUID(""), CathodeDataType.NO_TYPE);
                _dataTypeLUT.Add(Utilities.GenerateGUID("Marker"), CathodeDataType.MARKER);
                _dataTypeLUT.Add(Utilities.GenerateGUID("Character"), CathodeDataType.CHARACTER);
                _dataTypeLUT.Add(Utilities.GenerateGUID("Camera"), CathodeDataType.CAMERA);
            }
        }
        private CathodeDataType GetDataType(byte[] tag)
        {
            return GetDataType(new cGUID(tag));
        }
        private CathodeDataType GetDataType(cGUID tag)
        {
            SetupDataTypeLUT();
            return _dataTypeLUT[tag];
        }
        private cGUID GetDataTypeGUID(CathodeDataType type)
        {
            SetupResourceEntryTypeLUT();
            return _dataTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }

        private Dictionary<cGUID, CathodeResourceReferenceType> _resourceReferenceTypeLUT = new Dictionary<cGUID, CathodeResourceReferenceType>();
        private void SetupResourceEntryTypeLUT()
        {
            if (_resourceReferenceTypeLUT.Count == 0)
            {
                _resourceReferenceTypeLUT.Add(Utilities.GenerateGUID("RENDERABLE_INSTANCE"), CathodeResourceReferenceType.RENDERABLE_INSTANCE);
                _resourceReferenceTypeLUT.Add(Utilities.GenerateGUID("TRAVERSAL_SEGMENT"), CathodeResourceReferenceType.TRAVERSAL_SEGMENT);
                _resourceReferenceTypeLUT.Add(Utilities.GenerateGUID("COLLISION_MAPPING"), CathodeResourceReferenceType.COLLISION_MAPPING);
                _resourceReferenceTypeLUT.Add(Utilities.GenerateGUID("NAV_MESH_BARRIER_RESOURCE"), CathodeResourceReferenceType.NAV_MESH_BARRIER_RESOURCE);
                _resourceReferenceTypeLUT.Add(Utilities.GenerateGUID("EXCLUSIVE_MASTER_STATE_RESOURCE"), CathodeResourceReferenceType.EXCLUSIVE_MASTER_STATE_RESOURCE);
                _resourceReferenceTypeLUT.Add(Utilities.GenerateGUID("DYNAMIC_PHYSICS_SYSTEM"), CathodeResourceReferenceType.DYNAMIC_PHYSICS_SYSTEM);
                _resourceReferenceTypeLUT.Add(Utilities.GenerateGUID("ANIMATED_MODEL"), CathodeResourceReferenceType.ANIMATED_MODEL);
            }
        }
        private CathodeResourceReferenceType GetResourceEntryType(byte[] tag)
        {
            return GetResourceEntryType(new cGUID(tag));
        }
        private CathodeResourceReferenceType GetResourceEntryType(cGUID tag)
        {
            SetupResourceEntryTypeLUT();
            return _resourceReferenceTypeLUT[tag];
        }
        private cGUID GetResourceEntryTypeGUID(CathodeResourceReferenceType type)
        {
            SetupResourceEntryTypeLUT();
            return _resourceReferenceTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        #endregion

        private string path = "";

        private CommandsEntryPoints _entryPoints;
        private CathodeFlowgraph[] _entryPointObjects = null;

        private CathodeParameter[] _parameters = null;
        private CathodeFlowgraph[] _flowgraphs = null;

        private int[] parameterOffsets; //These values need to be multiplied by four when reading
        private int[] flowgraphOffsets; //These values need to be multiplied by four when reading
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CommandsEntryPoints
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public cGUID[] flowgraphIDs;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CommandsOffsetPair
    {
        public int offset;
        public int count;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CathodeNodeLink
    {
        public cGUID connectionID;  //The unique ID for this connection
        public cGUID parentParamID; //The ID of the parameter we're providing out 
        public cGUID childParamID;  //The ID of the parameter we're providing into the child
        public cGUID childID;       //The ID of the entity we're linking to to provide the value for
    }

    public class CommandsEntityLinks
    {
        public cGUID parentID;
        public List<CathodeNodeLink> childLinks = new List<CathodeNodeLink>();

        public CommandsEntityLinks(cGUID _id)
        {
            parentID = _id;
        }
    }

    public class CommandsParamRefSet
    {
        public cGUID id;
        public List<CathodeParameterReference> refs = new List<CathodeParameterReference>();

        public CommandsParamRefSet(cGUID _id)
        {
            id = _id;
        }
    }
}
