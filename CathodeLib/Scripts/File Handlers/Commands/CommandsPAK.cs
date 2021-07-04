using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Commands
{
    public class CommandsPAK
    {
        /* Load and parse the COMMANDS.PAK */
        public CommandsPAK(string pathToPak)
        {
            path_to_pak = pathToPak;

            BinaryReader reader = new BinaryReader(File.OpenRead(path_to_pak));

            ReadEntryPoints(reader);
            ReadPrimaryOffsets(reader);
            ReadParameters(reader);
            ReadFlowgraphs(reader);

            reader.Close();
        }

        /* Save all changes back out */
        public void Save()
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(path_to_pak));

            //Update all parameter values
            foreach (CathodeParameter parameter in parameters)
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
            foreach (CathodeFlowgraph flowgraph in flowgraphs)
            {
                foreach (CathodeNodeEntity node in flowgraph.nodes)
                {
                    foreach (CathodeParameterReference param_ref in node.nodeParameterReferences)
                    {
                        writer.BaseStream.Position = param_ref.editOffset;
                        writer.Write((int)(param_ref.offset/4));
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

        /* Return a list of filenames for flowgraphs in the CommandsPAK archive */
        public List<string> GetFlowgraphNames()
        {
            List<string> toReturn = new List<string>();
            foreach (CathodeFlowgraph flowgraph in flowgraphs) toReturn.Add(flowgraph.name);
            return toReturn;
        }

        /* Find the a script entry object by name */
        public int GetFileIndex(string FileName)
        {
            for (int i = 0; i < flowgraphs.Count; i++) if (flowgraphs[i].name == FileName || flowgraphs[i].name == FileName.Replace('/', '\\')) return i;
            throw new Exception("ERROR! Could not find the requested file index.");
        }

        /* Get flowgraph/parameter */
        public CathodeFlowgraph GetFlowgraph(UInt32 id)
        {
            if (id == 0) return null;
            return flowgraphs.FirstOrDefault(o => o.nodeID == id);
        }
        public CathodeFlowgraph GetFlowgraphByIndex(int index)
        {
            return (index >= flowgraphs.Count || index < 0) ? null : flowgraphs[index];
        }
        public CathodeParameter GetParameter(int offset)
        {
            return parameters.FirstOrDefault(o => o.offset == offset);
        }

        /* Get all flowgraphs/parameters */
        public List<CathodeFlowgraph> AllFlowgraphs { get { return flowgraphs; } }
        public List<CathodeParameter> AllParameters { get { return parameters; } }

        /* Get entry points (TODO: don't calculate this in accessor as it's used a fair bit) */
        public List<CathodeFlowgraph> EntryPoints { get
            {
                List<CathodeFlowgraph> entry_points_CF = new List<CathodeFlowgraph>();
                foreach (UInt32 flow_id in entry_points) entry_points_CF.Add(GetFlowgraph(flow_id));
                return entry_points_CF;
            }
        }

        /* Parse the three entry flowgraphs for this COMMANDS.PAK */
        private void ReadEntryPoints(BinaryReader reader)
        {
            for (int i = 0; i < 3; i++) entry_points.Add(reader.ReadUInt32());
        }

        /* Read the parameter and flowgraph offsets */
        private void ReadPrimaryOffsets(BinaryReader reader)
        {
            /* Initial parameter/flowgraph offset and count info */
            int parameter_offset_pos = reader.ReadInt32() * 4;
            parameter_count = reader.ReadInt32();
            int flowgraph_offset_pos = reader.ReadInt32() * 4;
            flowgraph_count = reader.ReadInt32();

            /* Archive offsets for parameters */
            parameter_offsets = new int[parameter_count];
            reader.BaseStream.Position = parameter_offset_pos;
            for (int i = 0; i < parameter_count; i++)
            {
                parameter_offsets[i] = reader.ReadInt32() * 4;
            }

            /* Archive offsets for flowgraphs */
            flowgraph_offsets = new int[flowgraph_count];
            reader.BaseStream.Position = flowgraph_offset_pos;
            for (int i = 0; i < flowgraph_count; i++)
            {
                flowgraph_offsets[i] = reader.ReadInt32() * 4;
            }
        }

        /* Read all parameters from the PAK */
        private void ReadParameters(BinaryReader reader)
        {
            reader.BaseStream.Position = parameter_offsets[0];
            for (int i = 0; i < parameter_count; i++)
            {
                int length = (i == parameter_count - 1) ? flowgraph_offsets[0] - parameter_offsets[i] : parameter_offsets[i + 1] - parameter_offsets[i];
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
                        ((CathodeString)this_parameter).unk0 = reader.ReadBytes(4); // some kind of ID sometimes referenced in script and resource id
                        ((CathodeString)this_parameter).unk1 = reader.ReadBytes(4); // sometimes flowgraph id ?!
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
                        ((CathodeResource)this_parameter).resourceID = reader.ReadUInt32();
                        break;
                    case CathodeDataType.DIRECTION:
                        this_parameter = new CathodeVector3();
                        float __x, __y, __z; __y = reader.ReadSingle(); __x = reader.ReadSingle(); __z = reader.ReadSingle(); //Y,X,Z!
                        ((CathodeVector3)this_parameter).value = new Vector3(__x, __y, __z);
                        break;
                    case CathodeDataType.ENUM:
                        this_parameter = new CathodeEnum();
                        ((CathodeEnum)this_parameter).enumID = reader.ReadUInt32();
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

                this_parameter.offset = parameter_offsets[i];
                this_parameter.dataType = this_datatype;

                parameters.Add(this_parameter);
            }
        }

        /* Read all flowgraphs from the PAK */
        private void ReadFlowgraphs(BinaryReader reader)
        {
            int scriptStart = parameter_offsets[parameter_offsets.Length - 1] + 8; //Relies on the last param always being 4 in length
            for (int i = 0; i < flowgraph_count; i++)
            {
                CathodeFlowgraph flowgraph = new CathodeFlowgraph();

                //Game doesn't parse the script name, so there's no real nice way of grabbing it!!
                reader.BaseStream.Position = scriptStart;
                flowgraph.globalID = reader.ReadUInt32();
                string name = "";
                while (true)
                {
                    byte thisByte = reader.ReadByte();
                    if (thisByte == 0x00) break;
                    name += (char)thisByte;
                }
                flowgraph.name = name;
                scriptStart = flowgraph_offsets[i] + 116;
                //End of crappy namegrab

                reader.BaseStream.Position = flowgraph_offsets[i];
                reader.BaseStream.Position += 4; //Skip 0x00,0x00,0x00,0x00

                //Read the offsets and counts
                List<OffsetPair> offsetPairs = new List<OffsetPair>();
                for (int x = 0; x < 13; x++)
                {
                    if (x == 0) flowgraph.uniqueID = reader.ReadUInt32();
                    if (x == 1) flowgraph.nodeID = reader.ReadUInt32();
                    OffsetPair newPair = new OffsetPair();
                    newPair.GlobalOffset = reader.ReadInt32() * 4;
                    newPair.EntryCount = reader.ReadInt32();
                    offsetPairs.Add(newPair);
                }

                //Pull data from those offsets
                for (int x = 0; x < offsetPairs.Count; x++)
                {
                    reader.BaseStream.Position = offsetPairs[x].GlobalOffset;
                    for (int y = 0; y < offsetPairs[x].EntryCount; y++)
                    {
                        switch ((CathodeScriptBlocks)x)
                        {
                            case CathodeScriptBlocks.DEFINE_NODE_LINKS:
                            {
                                reader.BaseStream.Position = offsetPairs[x].GlobalOffset + (y * 12);
                                UInt32 parentID = reader.ReadUInt32();

                                int OffsetToFindParams = reader.ReadInt32() * 4;
                                int NumberOfParams = reader.ReadInt32();

                                for (int z = 0; z < NumberOfParams; z++)
                                {
                                    reader.BaseStream.Position = OffsetToFindParams + (z * 16);
                                    CathodeNodeLink newLink = new CathodeNodeLink();
                                    newLink.connectionID = reader.ReadUInt32();
                                    newLink.parentParamID = reader.ReadUInt32();
                                    newLink.childParamID = reader.ReadUInt32();
                                    newLink.childID = reader.ReadUInt32();
                                    newLink.parentID = parentID;
                                    flowgraph.links.Add(newLink);
                                }
                                break;
                            }
                            case CathodeScriptBlocks.DEFINE_NODE_PARAMETERS:
                            {
                                reader.BaseStream.Position = offsetPairs[x].GlobalOffset + (y * 12);
                                CathodeNodeEntity thisNode = flowgraph.GetNodeByID(reader.ReadUInt32());

                                int OffsetToFindParams = reader.ReadInt32() * 4;
                                int NumberOfParams = reader.ReadInt32();

                                for (int z = 0; z < NumberOfParams; z++)
                                {
                                    reader.BaseStream.Position = OffsetToFindParams + (z * 8);
                                    CathodeParameterReference thisParamRef = new CathodeParameterReference();
                                    thisParamRef.paramID = reader.ReadUInt32();
                                    thisParamRef.editOffset = (int)reader.BaseStream.Position;
                                    thisParamRef.offset = reader.ReadInt32() * 4;
                                    thisNode.nodeParameterReferences.Add(thisParamRef);
                                }
                                break;
                            }
                            //NOT PARSING: This appears to define links to EnvironmentModelReference nodes through flowgraph ref nodes
                            case CathodeScriptBlocks.DEFINE_ENV_MODEL_REF_LINKS:
                            {
                                break;
                                reader.BaseStream.Position = offsetPairs[x].GlobalOffset + (y * 12);

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
                                reader.BaseStream.Position = offsetPairs[x].GlobalOffset + (y * 8);
                                byte[] linkID = reader.ReadBytes(4); //ID from DEFINE_ENV_MODEL_REF_LINKS (is this actually a node id?)
                                byte[] unk2 = reader.ReadBytes(4); //Dunno what this is, only appears to ever be used once
                                break;
                            }
                            case CathodeScriptBlocks.DEFINE_NODE_DATATYPES:
                            {
                                reader.BaseStream.Position = offsetPairs[x].GlobalOffset + (y * 12);

                                CathodeNodeEntity thisNode = flowgraph.GetNodeByID(reader.ReadUInt32());
                                thisNode.dataType = GetDataType(reader.ReadBytes(4));
                                thisNode.dataTypeParam = reader.ReadUInt32();
                                break;
                            }
                            //NOT PARSING: This block is another x-ref list, potentially related to mission critical things (doors, maybe?) 
                            case CathodeScriptBlocks.DEFINE_LINKED_NODES:
                            {
                                break;
                                //This block appears to be populated mainly in mission flowgraphs, rather than other ones like archetypes or model placement
                                //It defines a node from another flowgraph, which is referenced by executation hierarchy
                                reader.BaseStream.Position = offsetPairs[x].GlobalOffset + (y * 20);

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
                                CathodeNodeEntity thisNode = flowgraph.GetNodeByID(reader.ReadUInt32());
                                thisNode.nodeType = reader.ReadUInt32();
                                break;
                            }
                            //PARSING: I'm currently unsure on a lot of this, as the types vary (see entryType)
                            case CathodeScriptBlocks.DEFINE_RENDERABLE_ELEMENTS:
                            {
                                reader.BaseStream.Position = offsetPairs[x].GlobalOffset + (y * 40);

                                //TODO: these values change by entry type - need to work out what they're for before allowing editing
                                CathodeResourceReference resource_ref = new CathodeResourceReference();
                                resource_ref.editOffset = (int)reader.BaseStream.Position;
                                resource_ref.resourceRefID = reader.ReadUInt32(); //renderable element ID (also used in one of the param blocks for something)
                                reader.BaseStream.Position += 4; //unk (always 0x00 x4?)
                                resource_ref.positionOffset = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); //position offset
                                reader.BaseStream.Position += 4; //unk (always 0x00 x4?)
                                resource_ref.resourceID = reader.ReadUInt32(); //resource id
                                resource_ref.entryType = GetResourceEntryType(reader.ReadBytes(4)); //entry type
                                switch (resource_ref.entryType)
                                {
                                    case CathodeResourceReferenceType.RENDERABLE_INSTANCE:
                                        resource_ref.entryIndexREDS = reader.ReadInt32(); //REDS.BIN entry index
                                        resource_ref.entryCountREDS = reader.ReadInt32(); //REDS.BIN entry count
                                        break;
                                    case CathodeResourceReferenceType.COLLISION_MAPPING:
                                        resource_ref.unknownInteger = reader.ReadInt32(); //unknown integer (COLLISION.MAP index?)
                                        resource_ref.nodeID = reader.ReadUInt32(); //ID which maps to the node using the resource (?) - check GetFriendlyName
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
                                reader.BaseStream.Position = offsetPairs[x].GlobalOffset + (y * 4);
                                int offsetPos = reader.ReadInt32() * 4;

                                //Jump to the pointer location - this defines a node ID and another offset with count
                                reader.BaseStream.Position = offsetPos;
                                CathodeNodeEntity thisNode = flowgraph.GetNodeByID(reader.ReadUInt32()); //These always seem to be animation related nodes
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
                                reader.BaseStream.Position = offsetPairs[x].GlobalOffset + (y * 4);
                                int offsetPos = reader.ReadInt32() * 4;

                                reader.BaseStream.Position = offsetPos;
                                CathodeNodeEntity thisNode = flowgraph.GetNodeByID(reader.ReadUInt32());
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

                flowgraphs.Add(flowgraph);
            }
        }

        private CathodeDataType GetDataType(byte[] tag)
        {
            if      (tag.SequenceEqual(new byte[] { 0xF0, 0x0B, 0x76, 0x96 })) return CathodeDataType.BOOL;
            else if (tag.SequenceEqual(new byte[] { 0x87, 0xC1, 0x25, 0xE7 })) return CathodeDataType.INTEGER;
            else if (tag.SequenceEqual(new byte[] { 0xDC, 0x72, 0x74, 0xFD })) return CathodeDataType.FLOAT;
            else if (tag.SequenceEqual(new byte[] { 0x84, 0x11, 0xCD, 0x38 })) return CathodeDataType.STRING;
            else if (tag.SequenceEqual(new byte[] { 0x6D, 0x8D, 0xDB, 0xC0 })) return CathodeDataType.FILEPATH;
            else if (tag.SequenceEqual(new byte[] { 0x5E, 0x8E, 0x8E, 0x5A })) return CathodeDataType.SPLINE_DATA;
            else if (tag.SequenceEqual(new byte[] { 0x38, 0x43, 0xFF, 0xBF })) return CathodeDataType.DIRECTION;
            else if (tag.SequenceEqual(new byte[] { 0xDA, 0x6B, 0xD7, 0x02 })) return CathodeDataType.POSITION;
            else if (tag.SequenceEqual(new byte[] { 0xBF, 0xA7, 0x62, 0x8C })) return CathodeDataType.ENUM;
            else if (tag.SequenceEqual(new byte[] { 0xF6, 0xAF, 0x08, 0x93 })) return CathodeDataType.SHORT_GUID;
            else if (tag.SequenceEqual(new byte[] { 0xC7, 0x6E, 0xC8, 0x05 })) return CathodeDataType.OBJECT;
            else if (tag.SequenceEqual(new byte[] { 0xD1, 0xEA, 0x7E, 0x5E })) return CathodeDataType.ZONE_PTR;
            else if (tag.SequenceEqual(new byte[] { 0x7E, 0x39, 0xA1, 0xDD })) return CathodeDataType.ZONE_LINK_PTR;
            else if (tag.SequenceEqual(new byte[] { 0x25, 0x16, 0x14, 0x8C })) return CathodeDataType.UNKNOWN_7; //Oddly this just maps to a blank string in the CATHODE dump
            else if (tag.SequenceEqual(new byte[] { 0x93, 0xE9, 0xE9, 0x37 })) return CathodeDataType.MARKER;
            else if (tag.SequenceEqual(new byte[] { 0x8A, 0x79, 0x61, 0xC5 })) return CathodeDataType.CHARACTER;
            else if (tag.SequenceEqual(new byte[] { 0x4F, 0x2A, 0x35, 0x5B })) return CathodeDataType.CAMERA;
            else
            {
                throw new Exception("ERROR! GetDataType couldn't match any CathodeDataType values.");
            }
        }

        private CathodeResourceReferenceType GetResourceEntryType(byte[] tag)
        {
            if      (tag.SequenceEqual(new byte[] { 0xDC, 0x53, 0xD1, 0x45 })) return CathodeResourceReferenceType.RENDERABLE_INSTANCE;
            else if (tag.SequenceEqual(new byte[] { 0xCD, 0xC5, 0x3B, 0x90 })) return CathodeResourceReferenceType.TRAVERSAL_SEGMENT;
            else if (tag.SequenceEqual(new byte[] { 0xB7, 0x92, 0xB6, 0xCE })) return CathodeResourceReferenceType.COLLISION_MAPPING;
            else if (tag.SequenceEqual(new byte[] { 0xB5, 0x5F, 0x6E, 0x4C })) return CathodeResourceReferenceType.NAV_MESH_BARRIER_RESOURCE;
            else if (tag.SequenceEqual(new byte[] { 0xDF, 0xFF, 0x99, 0xED })) return CathodeResourceReferenceType.EXCLUSIVE_MASTER_STATE_RESOURCE;
            else if (tag.SequenceEqual(new byte[] { 0x5D, 0x41, 0xF1, 0xFB })) return CathodeResourceReferenceType.DYNAMIC_PHYSICS_SYSTEM;
            else if (tag.SequenceEqual(new byte[] { 0xD7, 0x3E, 0x1E, 0x5E })) return CathodeResourceReferenceType.ANIMATED_MODEL;
            else
            {
                throw new Exception("ERROR! GetDataType couldn't match any CathodeResourceReferenceType values.");
            }
        }

        private string path_to_pak = "";

        private List<UInt32> entry_points = new List<UInt32>();

        private int[] parameter_offsets;
        private int parameter_count;
        private int[] flowgraph_offsets;
        private int flowgraph_count;

        private List<CathodeFlowgraph> flowgraphs = new List<CathodeFlowgraph>();
        private List<CathodeParameter> parameters = new List<CathodeParameter>();
    }
}
