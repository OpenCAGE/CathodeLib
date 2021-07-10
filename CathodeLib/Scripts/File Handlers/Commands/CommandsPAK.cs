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

        /* Save all changes back out */
        public void Save()
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(path));

            //Update all parameter values
            foreach (CathodeParameter parameter in _parameters)
            {
                writer.BaseStream.Position = parameter.offset + 4;
                switch (parameter.dataType)
                {
                    case CathodeDataType.POSITION:
                        CathodeTransform cTransform = (CathodeTransform)parameter;
#if UNITY_EDITOR || UNITY_STANDALONE
                        writer.Write(cTransform.position.x);
                        writer.Write(cTransform.position.y);
                        writer.Write(cTransform.position.z);
                        writer.Write(cTransform.rotation.y);
                        writer.Write(cTransform.rotation.x);
                        writer.Write(cTransform.rotation.z);
#else
                        writer.Write(cTransform.position.X);
                        writer.Write(cTransform.position.Y);
                        writer.Write(cTransform.position.Z);
                        writer.Write(cTransform.rotation.Y);
                        writer.Write(cTransform.rotation.X);
                        writer.Write(cTransform.rotation.Z);
#endif
                        break;
                    case CathodeDataType.DIRECTION:
                        CathodeVector3 cVector = (CathodeVector3)parameter;
#if UNITY_EDITOR || UNITY_STANDALONE
                        writer.Write(cVector.value.y);
                        writer.Write(cVector.value.x);
                        writer.Write(cVector.value.z);
#else
                        writer.Write(cVector.value.Y);
                        writer.Write(cVector.value.X);
                        writer.Write(cVector.value.Z);
#endif
                        break;
                    case CathodeDataType.INTEGER:
                        CathodeInteger cInt = (CathodeInteger)parameter;
                        writer.Write(cInt.value);
                        break;
                    case CathodeDataType.STRING:
                        CathodeString cString = (CathodeString)parameter;
                        writer.BaseStream.Position += 8;
                        for (int i = 0; i < cString.initial_length; i++)
                        {
                            char to_write = (char)0x00;
                            if (i < cString.value.Length) to_write = cString.value[i];
                            writer.Write((byte)to_write);
                        }
                        break;
                    case CathodeDataType.FLOAT:
                        CathodeFloat cFloat = (CathodeFloat)parameter;
                        writer.Write(cFloat.value);
                        break;
                }
            }

            //Update all selected parameter offsets & REDS references
            foreach (CathodeFlowgraph flowgraph in _flowgraphs)
            {
                foreach (CathodeNode node in flowgraph.nodes)
                {
                    foreach (CathodeParameterReference param_ref in node.nodeParameterReferences)
                    {
                        //writer.BaseStream.Position = param_ref.editOffset;
                        //writer.Write((int)(param_ref.offset));
                    }
                }
                foreach (CathodeResourceReference resRef in flowgraph.resources)
                {
                    if (resRef == null || resRef.entryType != CathodeResourceReferenceType.RENDERABLE_INSTANCE) continue;
                    writer.BaseStream.Position = resRef.editOffset + 32;
                    writer.Write(resRef.entryIndexREDS);
                    writer.Write(resRef.entryCountREDS);
                }
            }

            writer.Close();
        }

        public void NewSave()
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(path));
            writer.BaseStream.SetLength(0); //todo

            /* Write entry points */
            Utilities.Write<CommandsEntryPoints>(writer, _entryPoints);

            //we come back and rewrite these zeros later
            int offsetToRewrite = (int)writer.BaseStream.Position;
            writer.Write(0); 
            writer.Write(_parameters.Length);
            writer.Write(0); 
            writer.Write(_flowgraphs.Length);

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
                        writer.Write(((CathodeString)_parameters[i]).unk0.val);
                        writer.Write(((CathodeString)_parameters[i]).unk1.val);
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
                Utilities.Write<cGUID>(writer, _flowgraphs[i].globalID);
                for (int x = 0; x < _flowgraphs[i].name.Length; x++) writer.Write(_flowgraphs[i].name[x]);
                Utilities.Align(writer, 4);
                Utilities.Write<cGUID>(writer, _flowgraphs[i].nodeID);

                //write script data
                int test_offset = (int)writer.BaseStream.Position;

                flowgraphOffsets[i] = (int)writer.BaseStream.Position / 4;
                writer.Write(0);
                for (int x = 0; x < 13; x++)
                {
                    if (x == 0) Utilities.Write<cGUID>(writer, _flowgraphs[i].uniqueID);
                    if (x == 1) Utilities.Write<cGUID>(writer, _flowgraphs[i].nodeID);
                    writer.Write(test_offset / 4); //offset
                    writer.Write(0); //count
                }
            }

            /* Write out parameter offsets */
            int parameterOffsetPos = (int)writer.BaseStream.Position;
            Utilities.Write<int>(writer, parameterOffsets);

            /* Write out flowgraph offsets */
            int flowgraphOffsetPos = (int)writer.BaseStream.Position;
            Utilities.Write<int>(writer, flowgraphOffsets);

            //rewrite with offsets
            writer.BaseStream.Position = offsetToRewrite;
            writer.Write(parameterOffsetPos / 4);
            writer.BaseStream.Position += 4;
            writer.Write(flowgraphOffsetPos / 4);

            writer.Close();
        }

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

        /* Get entry point flowgraph objects */
        public CathodeFlowgraph[] EntryPoints { 
            get
            {
                if (_entryPointObjects != null) return _entryPointObjects;
                _entryPointObjects = new CathodeFlowgraph[_entryPoints.flowgraphIDs.Length];
                for (int i = 0; i < _entryPoints.flowgraphIDs.Length; i++) _entryPointObjects[i] = GetFlowgraph(_entryPoints.flowgraphIDs[i]);
                return _entryPointObjects;
            }
        }

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
                        ((CathodeString)this_parameter).unk0 = new cGUID(reader); // some kind of ID sometimes referenced in script and resource id
                        ((CathodeString)this_parameter).unk1 = new cGUID(reader); // sometimes flowgraph id ?!
                        bool shouldStop = false;
                        for (int x = 0; x < length - 8; x++)
                        {
                            byte thisByte = reader.ReadByte();
                            if (thisByte == 0x00) { shouldStop = true; continue; }
                            if (shouldStop && thisByte != 0x00) break;
                            ((CathodeString)this_parameter).value += (char)thisByte;
                        }
                        ((CathodeString)this_parameter).initial_length = length - 13;
                        reader.BaseStream.Position -= 1;
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
            int scriptStart = (parameterOffsets[parameterOffsets.Length - 1] * 4) + 8; //Relies on the last param always being 4 in length
            for (int i = 0; i < _flowgraphs.Length; i++)
            {
                CathodeFlowgraph flowgraph = new CathodeFlowgraph();

                //Game doesn't parse the script name, so there's no real nice way of grabbing it!!
                reader.BaseStream.Position = scriptStart;
                flowgraph.globalID = new cGUID(reader);
                string name = "";
                while (true)
                {
                    byte thisByte = reader.ReadByte();
                    if (thisByte == 0x00) break;
                    name += (char)thisByte;
                }
                flowgraph.name = name;
                scriptStart = (flowgraphOffsets[i] * 4) + 116;
                //End of crappy namegrab

                reader.BaseStream.Position = (flowgraphOffsets[i] * 4);
                reader.BaseStream.Position += 4; //Skip 0x00,0x00,0x00,0x00

                //Read the offsets and counts
                OffsetPair[] offsetPairs = new OffsetPair[13];
                for (int x = 0; x < 13; x++)
                {
                    if (x == 0) flowgraph.uniqueID = new cGUID(reader);
                    if (x == 1) flowgraph.nodeID = new cGUID(reader);
                    offsetPairs[x] = Utilities.Consume<OffsetPair>(reader);
                }

                //Pull data from those offsets
                for (int x = 0; x < offsetPairs.Length; x++)
                {
                    reader.BaseStream.Position = offsetPairs[x].GlobalOffset * 4;
                    for (int y = 0; y < offsetPairs[x].EntryCount; y++)
                    {
                        switch ((CathodeScriptBlocks)x)
                        {
                            case CathodeScriptBlocks.DEFINE_NODE_LINKS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                cGUID parentID = new cGUID(reader); //TODO: filter this by parentID? 
                                int NumberOfParams = JumpToOffset(ref reader);
                                flowgraph.links.AddRange(Utilities.ConsumeArray<CathodeNodeLink>(reader, NumberOfParams));
                                break;
                            }
                            case CathodeScriptBlocks.DEFINE_NODE_PARAMETERS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                CathodeNode thisNode = flowgraph.GetNodeByID(new cGUID(reader));
                                int NumberOfParams = JumpToOffset(ref reader);
                                thisNode.nodeParameterReferences.AddRange(Utilities.ConsumeArray<CathodeParameterReference>(reader, NumberOfParams));
                                break;
                            }
                            //NOT PARSING: This appears to define links to EnvironmentModelReference nodes through flowgraph ref nodes
                            case CathodeScriptBlocks.DEFINE_ENV_MODEL_REF_LINKS:
                            {
                                break;
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);

                                //This block defines some kind of ID, then an offset and a count of data at that offset
                                byte[] thisID = reader.ReadBytes(4);
                                int OffsetToFindParams = reader.ReadInt32() * 4;
                                int NumberOfParams = reader.ReadInt32();

                                //We jump to that offset, and read the x-ref listing
                                reader.BaseStream.Position = OffsetToFindParams;
                                List<byte[]> content = new List<byte[]>();
                                for (int z = 0; z < NumberOfParams; z++)
                                {
                                    content.Add(reader.ReadBytes(4)); //cross-refs: node ids (of flowgraph refs), then the EnvironmentModelReference node, then 0x00 (x4)
                                }
                                break;
                            }
                            //NOT PARSING: This block is only 8 bytes - first 4 is an ID for the block above, second is an ID not used anywhere else - potentially four 1-byte numbers?
                            case CathodeScriptBlocks.DEFINE_ENV_MODEL_REF_LINKS_EXTRA:
                            {
                                break;
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                byte[] linkID = reader.ReadBytes(4); //ID from DEFINE_ENV_MODEL_REF_LINKS (is this actually a node id?)
                                byte[] unk2 = reader.ReadBytes(4); //Dunno what this is, only appears to ever be used once
                                break;
                            }
                            case CathodeScriptBlocks.DEFINE_NODE_DATATYPES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);

                                CathodeNode thisNode = flowgraph.GetNodeByID(new cGUID(reader));
                                thisNode.dataType = GetDataType(reader.ReadBytes(4));
                                thisNode.dataTypeParam = new cGUID(reader);
                                break;
                            }
                            //NOT PARSING: This block is another x-ref list, potentially related to mission critical things (doors, maybe?) 
                            case CathodeScriptBlocks.DEFINE_LINKED_NODES:
                            {
                                break;
                                //This block appears to be populated mainly in mission flowgraphs, rather than other ones like archetypes or model placement
                                //It defines a node from another flowgraph, which is referenced by executation hierarchy
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 20);

                                byte[] unk1 = reader.ReadBytes(4); //flowgraph id?

                                int OffsetToFindParams = reader.ReadInt32() * 4; //offset
                                int NumberOfParams = reader.ReadInt32(); //count

                                int resetPos = (int)reader.BaseStream.Position;
                                reader.BaseStream.Position = OffsetToFindParams;
                                for (int p = 0; p < NumberOfParams; p++)
                                {
                                    byte[] unk69 = reader.ReadBytes(4); //cross-refs: node ids (of flowgraph refs), then the node, then 0x00 (x4)
                                }

                                reader.BaseStream.Position = resetPos;
                                byte[] unk4 = reader.ReadBytes(4); //flowgraph id again
                                byte[] unk5 = reader.ReadBytes(4); //another id for something else

                                break;
                            }
                            case CathodeScriptBlocks.DEFINE_NODE_NODETYPES:
                            {
                                CathodeNode thisNode = flowgraph.GetNodeByID(new cGUID(reader));
                                thisNode.nodeType = new cGUID(reader);
                                break;
                            }
                            //PARSING: I'm currently unsure on a lot of this, as the types vary (see entryType)
                            case CathodeScriptBlocks.DEFINE_RENDERABLE_ELEMENTS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 40);

                                //TODO: these values change by entry type - need to work out what they're for before allowing editing
                                CathodeResourceReference resource_ref = new CathodeResourceReference();
                                resource_ref.editOffset = (int)reader.BaseStream.Position;
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
                            //NOT PARSING: This is very similar in format to DEFINE_ENV_MODEL_REF_LINKS with the cross-references
                            case CathodeScriptBlocks.UNKNOWN_8:
                            {
                                break;
                                //This block is only four bytes - which translates to a pointer to another location... so read that
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                int offsetPos = reader.ReadInt32() * 4;

                                //Jump to the pointer location - this defines a node ID and another offset with count
                                reader.BaseStream.Position = offsetPos;
                                CathodeNode thisNode = flowgraph.GetNodeByID(new cGUID(reader)); //These always seem to be animation related nodes
                                int OffsetToFindParams = reader.ReadInt32() * 4;
                                int NumberOfParams = reader.ReadInt32();

                                //Now we jump to THAT pointer's location, and iterate by count. The blocks here are of length 32.
                                for (int z = 0; z < NumberOfParams; z++)
                                {
                                    reader.BaseStream.Position = OffsetToFindParams + (z * 32);

                                    //First 4: unknown id
                                    byte[] unk1 = reader.ReadBytes(4);

                                    //Second 4: datatype
                                    byte[] datatype1 = reader.ReadBytes(4);
                                    CathodeDataType datatype1_converted = GetDataType(datatype1);

                                    //Third 4: unknown id
                                    byte[] unk2 = reader.ReadBytes(4);

                                    //Fourth 4: parameter id
                                    byte[] unk3 = reader.ReadBytes(4);
                                    //string unk3_paramname_string = NodeDB.GetName(unk3);

                                    //Fifth 4: datatype
                                    byte[] datatype2 = reader.ReadBytes(4);
                                    CathodeDataType datatype2_converted = GetDataType(datatype2);

                                    //Sixth 4: unknown ID
                                    byte[] unk4 = reader.ReadBytes(4);

                                    //Now we get ANOTHER offset. This is a pointer to a location and a count of IDs at that location.
                                    int offset1 = reader.ReadInt32() * 4;
                                    int count1 = reader.ReadInt32();
                                    for (int p = 0; p < count1; p++)
                                    {
                                        reader.BaseStream.Position = offset1 + (p * 4);
                                        byte[] unk69 = reader.ReadBytes(4);  //cross-refs: node ids (of flowgraph refs), then the node, then 0x00 (x4)
                                    }
                                }
                                break;
                            }
                            //NOT PARSING: This is very similar in format to DEFINE_ENV_MODEL_REF_LINKS with the cross-references
                            case CathodeScriptBlocks.DEFINE_ZONE_CONTENT:
                            {
                                break;
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                int offsetPos = reader.ReadInt32() * 4;

                                reader.BaseStream.Position = offsetPos;
                                CathodeNode thisNode = flowgraph.GetNodeByID(new cGUID(reader));
                                //string test0 = NodeDB.GetFriendlyName(thisNode.nodeID);
                                int OffsetToFindParams = reader.ReadInt32() * 4;
                                int NumberOfParams = reader.ReadInt32();

                                int goTo = OffsetToFindParams;
                                for (int m = 0; m < NumberOfParams; m++)
                                {
                                    reader.BaseStream.Position = goTo;

                                    byte[] firstFour = reader.ReadBytes(4);
                                    int offset1 = -1;
                                    if (m > 0) //for some reason only the first entry is 8 bytes long, the rest are 12, with four bytes of something at the start (some kind of ID, zone id maybe?)
                                    {
                                        offset1 = reader.ReadInt32() * 4;
                                    }
                                    else
                                    {
                                        offset1 = BitConverter.ToInt32(firstFour, 0) * 4;
                                    }
                                    int count1 = reader.ReadInt32();

                                    goTo = (int)reader.BaseStream.Position;

                                    reader.BaseStream.Position = offset1;
                                    for (int p = 0; p < count1; p++)
                                    {
                                        byte[] unk55 = reader.ReadBytes(4);  //cross-refs: node ids (of flowgraph refs), then the node, then 0x00 (x4)
                                    }
                                }
                                break;
                            }
                        }
                    }
                }

                _flowgraphs[i] = flowgraph;
            }
        }

        private Dictionary<cGUID, CathodeDataType> _dataTypeLUT = new Dictionary<cGUID, CathodeDataType>();
        private void SetupDataTypeLUT()
        {
            if (_dataTypeLUT.Count == 0)
            {
                _dataTypeLUT.Add(new cGUID(new byte[] { 0xF0, 0x0B, 0x76, 0x96 }), CathodeDataType.BOOL);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0x87, 0xC1, 0x25, 0xE7 }), CathodeDataType.INTEGER);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0xDC, 0x72, 0x74, 0xFD }), CathodeDataType.FLOAT);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0x84, 0x11, 0xCD, 0x38 }), CathodeDataType.STRING);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0x6D, 0x8D, 0xDB, 0xC0 }), CathodeDataType.FILEPATH);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0x5E, 0x8E, 0x8E, 0x5A }), CathodeDataType.SPLINE_DATA);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0x38, 0x43, 0xFF, 0xBF }), CathodeDataType.DIRECTION);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0xDA, 0x6B, 0xD7, 0x02 }), CathodeDataType.POSITION);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0xBF, 0xA7, 0x62, 0x8C }), CathodeDataType.ENUM);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0xF6, 0xAF, 0x08, 0x93 }), CathodeDataType.SHORT_GUID);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0xC7, 0x6E, 0xC8, 0x05 }), CathodeDataType.OBJECT);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0xD1, 0xEA, 0x7E, 0x5E }), CathodeDataType.ZONE_PTR);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0x7E, 0x39, 0xA1, 0xDD }), CathodeDataType.ZONE_LINK_PTR);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0x25, 0x16, 0x14, 0x8C }), CathodeDataType.UNKNOWN_7);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0x93, 0xE9, 0xE9, 0x37 }), CathodeDataType.MARKER);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0x8A, 0x79, 0x61, 0xC5 }), CathodeDataType.CHARACTER);
                _dataTypeLUT.Add(new cGUID(new byte[] { 0x4F, 0x2A, 0x35, 0x5B }), CathodeDataType.CAMERA);
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
                _resourceReferenceTypeLUT.Add(new cGUID(new byte[] { 0xDC, 0x53, 0xD1, 0x45 }), CathodeResourceReferenceType.RENDERABLE_INSTANCE);
                _resourceReferenceTypeLUT.Add(new cGUID(new byte[] { 0xCD, 0xC5, 0x3B, 0x90 }), CathodeResourceReferenceType.TRAVERSAL_SEGMENT);
                _resourceReferenceTypeLUT.Add(new cGUID(new byte[] { 0xB7, 0x92, 0xB6, 0xCE }), CathodeResourceReferenceType.COLLISION_MAPPING);
                _resourceReferenceTypeLUT.Add(new cGUID(new byte[] { 0xB5, 0x5F, 0x6E, 0x4C }), CathodeResourceReferenceType.NAV_MESH_BARRIER_RESOURCE);
                _resourceReferenceTypeLUT.Add(new cGUID(new byte[] { 0xDF, 0xFF, 0x99, 0xED }), CathodeResourceReferenceType.EXCLUSIVE_MASTER_STATE_RESOURCE);
                _resourceReferenceTypeLUT.Add(new cGUID(new byte[] { 0x5D, 0x41, 0xF1, 0xFB }), CathodeResourceReferenceType.DYNAMIC_PHYSICS_SYSTEM);
                _resourceReferenceTypeLUT.Add(new cGUID(new byte[] { 0xD7, 0x3E, 0x1E, 0x5E }), CathodeResourceReferenceType.ANIMATED_MODEL);
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
}
