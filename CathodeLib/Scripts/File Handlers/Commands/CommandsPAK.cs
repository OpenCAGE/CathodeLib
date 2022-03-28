using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

namespace CATHODE.Commands
{
    /*
    
    A:I iOS contains all symbols which has been super helpful. There's some new Commands info, as well as a bunch of info about REDS/MVR.

    COMMANDS.BIN loads into CommandBuffer object.
    CommandBuffer is looped through once for number of commands entries, adding or removing:
       composite_template
       alias_from_command
       parameter_from_command
       entity_to_seq_from_command
       link
       connector
       resource
       track_from_command
    It's then looped through again, adding or removing:
       entity
       binding_from_command
       **something undefined**
       method
       proxy_from_command
       breakpoint_from_command

    */

    public class CommandsPAK
    {
        /* Load and parse the COMMANDS.PAK */
        public CommandsPAK(string pathToPak)
        {
            _path = pathToPak;
            _didLoadCorrectly = Load(_path);
        }

        #region ACCESSORS
        /* Return a list of filenames for flowgraphs in the CommandsPAK archive */
        public string[] GetFlowgraphNames()
        {
            string[] toReturn = new string[_flowgraphs.Count];
            for (int i = 0; i < _flowgraphs.Count; i++) toReturn[i] = _flowgraphs[i].name;
            return toReturn;
        }

        /* Find the a script entry object by name */
        public int GetFileIndex(string FileName)
        {
            for (int i = 0; i < _flowgraphs.Count; i++) if (_flowgraphs[i].name == FileName || _flowgraphs[i].name == FileName.Replace('/', '\\')) return i;
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
            return (index >= _flowgraphs.Count || index < 0) ? null : _flowgraphs[index];
        }

        /* Get all flowgraphs/parameters */
        public List<CathodeFlowgraph> Flowgraphs { get { return _flowgraphs; } }

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

        /* Set the entry point (we only allow the level script to be set, PAUSEMENU and GLOBAL are required) */
        public void SetEntryPoint(cGUID id)
        {
            _entryPoints.flowgraphIDs[0] = id;
            _entryPointObjects = null;
        }
        #endregion

        #region WRITING
        /* Save all changes back out */
        public void Save()
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(_path));
            writer.BaseStream.SetLength(0);

            //Write entry points
            Utilities.Write<CommandsEntryPoints>(writer, _entryPoints);

            //Write placeholder info for parameter/flowgraph offsets
            int offsetToRewrite = (int)writer.BaseStream.Position;
            writer.Write(0); 
            writer.Write(0);
            writer.Write(0); 
            writer.Write(0);

            //Work out unique parameters to write
            List<CathodeParameter> parameters = new List<CathodeParameter>();
            for (int i = 0; i < _flowgraphs.Count; i++)
            {
                List<CathodeEntity> fgEntities = _flowgraphs[i].GetEntities();
                for (int x = 0; x < fgEntities.Count; x++)
                    for (int y = 0; y < fgEntities[x].parameters.Count; y++)
                        parameters.Add(fgEntities[x].parameters[y].content);
            }
            parameters = PruneParameterList(parameters);

            //Write out parameters & track offsets
            int[] parameterOffsets = new int[parameters.Count];
            for (int i = 0; i < parameters.Count; i++)
            {
                parameterOffsets[i] = (int)writer.BaseStream.Position / 4;
                Utilities.Write<cGUID>(writer, CommandsUtils.GetDataTypeGUID(parameters[i].dataType));
                switch (parameters[i].dataType)
                {
                    case CathodeDataType.POSITION:
                        Vector3 pos = ((CathodeTransform)parameters[i]).position;
                        Vector3 rot = ((CathodeTransform)parameters[i]).rotation;
                        writer.Write(pos.x); writer.Write(pos.y); writer.Write(pos.z);
                        writer.Write(rot.y); writer.Write(rot.x); writer.Write(rot.z);
                        break;
                    case CathodeDataType.INTEGER:
                        writer.Write(((CathodeInteger)parameters[i]).value);
                        break;
                    case CathodeDataType.STRING:
                        int stringStart = ((int)writer.BaseStream.Position + 4) / 4;
                        byte[] stringStartRaw = BitConverter.GetBytes(stringStart);
                        stringStartRaw[3] = 0x80; 
                        writer.Write(stringStartRaw);
                        string str = ((CathodeString)parameters[i]).value;
                        writer.Write(Utilities.GenerateGUID(str).val);
                        for (int x = 0; x < str.Length; x++) writer.Write(str[x]);
                        writer.Write((char)0x00);
                        Utilities.Align(writer, 4);
                        break;
                    case CathodeDataType.BOOL:
                        if (((CathodeBool)parameters[i]).value) writer.Write(1); else writer.Write(0);
                        break;
                    case CathodeDataType.FLOAT:
                        writer.Write(((CathodeFloat)parameters[i]).value);
                        break;
                    case CathodeDataType.SHORT_GUID:
                        Utilities.Write<cGUID>(writer, ((CathodeResource)parameters[i]).resourceID);
                        break;
                    case CathodeDataType.DIRECTION:
                        Vector3 dir = ((CathodeVector3)parameters[i]).value;
                        writer.Write(dir.x); writer.Write(dir.y); writer.Write(dir.z);
                        break;
                    case CathodeDataType.ENUM:
                        Utilities.Write<cGUID>(writer, ((CathodeEnum)parameters[i]).enumID);
                        writer.Write(((CathodeEnum)parameters[i]).enumIndex);
                        break;
                    case CathodeDataType.SPLINE_DATA:
                        CathodeSpline thisSpline = ((CathodeSpline)parameters[i]);
                        writer.Write(((int)writer.BaseStream.Position + 8) / 4);
                        writer.Write(thisSpline.splinePoints.Count);
                        for (int x = 0; x < thisSpline.splinePoints.Count; x++)
                        {
                            writer.Write(thisSpline.splinePoints[x].position.x);
                            writer.Write(thisSpline.splinePoints[x].position.y);
                            writer.Write(thisSpline.splinePoints[x].position.z);
                            //todo: is this YXZ
                            writer.Write(thisSpline.splinePoints[x].rotation.x);
                            writer.Write(thisSpline.splinePoints[x].rotation.y);
                            writer.Write(thisSpline.splinePoints[x].rotation.z);
                        }
                        break;
                }
            }

            //Write out flowgraphs & track offsets
            int[] flowgraphOffsets = new int[_flowgraphs.Count];
            for (int i = 0; i < _flowgraphs.Count; i++)
            {
                int scriptStartPos = (int)writer.BaseStream.Position / 4;

                Utilities.Write<cGUID>(writer, Utilities.GenerateGUID(_flowgraphs[i].name));
                for (int x = 0; x < _flowgraphs[i].name.Length; x++) writer.Write(_flowgraphs[i].name[x]);
                writer.Write((char)0x00);
                Utilities.Align(writer, 4);

                //Work out what we want to write
                List<CathodeEntity> ents = _flowgraphs[i].GetEntities();
                List<CathodeEntity> entitiesWithLinks = new List<CathodeEntity>(ents.FindAll(o => o.childLinks.Count != 0));
                List<CathodeEntity> entitiesWithParams = new List<CathodeEntity>(ents.FindAll(o => o.parameters.Count != 0));
                //TODO: find a nicer way to sort into node class types
                List<CAGEAnimation> cageAnimationNodes = new List<CAGEAnimation>();
                List<TriggerSequence> triggerSequenceNodes = new List<TriggerSequence>();
                cGUID cageAnimationGUID = CommandsUtils.GetFunctionTypeGUID(CathodeFunctionType.CAGEAnimation);
                cGUID triggerSequenceGUID = CommandsUtils.GetFunctionTypeGUID(CathodeFunctionType.TriggerSequence);
                for (int x = 0; x < _flowgraphs[i].functions.Count; x++)
                {
                    if (_flowgraphs[i].functions[x].function == cageAnimationGUID)
                    {
                        CAGEAnimation thisNode = (CAGEAnimation)_flowgraphs[i].functions[x];
                        if (thisNode.keyframeHeaders.Count == 0 && thisNode.keyframeData.Count == 0 && thisNode.paramsData3.Count == 0) continue;
                        cageAnimationNodes.Add(thisNode);
                    }
                    else if (_flowgraphs[i].functions[x].function == triggerSequenceGUID)
                    {
                        TriggerSequence thisNode = (TriggerSequence)_flowgraphs[i].functions[x];
                        if (thisNode.triggers.Count == 0 && thisNode.events.Count == 0) continue;
                        triggerSequenceNodes.Add(thisNode);
                    }
                }

                //Reconstruct resources
                List<CathodeResourceReference> resourceReferences = new List<CathodeResourceReference>();
                cGUID resourceParamID = Utilities.GenerateGUID("resource");
                for (int x = 0; x < ents.Count; x++)
                {
                    for (int y = 0; y < ents[x].resources.Count; y++)
                        if (!resourceReferences.Contains(ents[x].resources[y]))
                            resourceReferences.Add(ents[x].resources[y]);

                    CathodeLoadedParameter resParam = ents[x].parameters.FirstOrDefault(o => o.paramID == resourceParamID);
                    if (resParam == null) continue;
                    List<CathodeResourceReference> resParamRef = ((CathodeResource)resParam.content).value;
                    for (int y = 0; y < resParamRef.Count; y++)
                        if (!resourceReferences.Contains(resParamRef[y]))
                            resourceReferences.Add(resParamRef[y]);
                }
                resourceReferences.AddRange(_flowgraphs[i].resources);

                //Sort
                entitiesWithLinks = entitiesWithLinks.OrderBy(o => o.nodeID.ToUInt32()).ToList();
                entitiesWithParams = entitiesWithParams.OrderBy(o => o.nodeID.ToUInt32()).ToList();
                List<OverrideEntity> reshuffledChecksums = _flowgraphs[i].overrides.OrderBy(o => o.checksum.ToUInt32()).ToList();
                _flowgraphs[i].SortEntities();

                //Write data
                OffsetPair[] scriptPointerOffsetInfo = new OffsetPair[(int)CommandsDataBlock.NUMBER_OF_SCRIPT_BLOCKS];
                for (int x = 0; x < (int)CommandsDataBlock.NUMBER_OF_SCRIPT_BLOCKS; x++)
                {
                    switch ((CommandsDataBlock)x)
                    {
                        case CommandsDataBlock.DEFINE_SCRIPT_HEADER:
                        {
                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, 2);
                            Utilities.Write<cGUID>(writer, _flowgraphs[i].nodeID);
                            writer.Write(0);
                            break;
                        }
                        case CommandsDataBlock.ENTITY_CONNECTIONS:
                        {
                            List<OffsetPair> offsetPairs = new List<OffsetPair>();
                            foreach (CathodeEntity entityWithLink in entitiesWithLinks)
                            {
                                offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, entityWithLink.childLinks.Count));
                                Utilities.Write<CathodeNodeLink>(writer, entityWithLink.childLinks);
                            }

                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, entitiesWithLinks.Count);
                            for (int p = 0; p < entitiesWithLinks.Count; p++)
                            {
                                writer.Write(entitiesWithLinks[p].nodeID.val);
                                writer.Write(offsetPairs[p].GlobalOffset / 4);
                                writer.Write(offsetPairs[p].EntryCount);
                            }

                            break;
                        }
                        case CommandsDataBlock.ENTITY_PARAMETERS:
                        {
                            List<OffsetPair> offsetPairs = new List<OffsetPair>();
                            foreach (CathodeEntity entityWithParam in entitiesWithParams)
                            {
                                offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, entityWithParam.parameters.Count));
                                for (int y = 0; y < entityWithParam.parameters.Count; y++)
                                {
                                    Utilities.Write<cGUID>(writer, entityWithParam.parameters[y].paramID);
                                    //TODO: this is super slow! Find a better way to lookup parameter content offsets (precalculate a nicer structure)
                                    int paramOffset = -1;
                                    for (int z = 0; z < parameters.Count; z++)
                                    {
                                        if (parameters[z] == entityWithParam.parameters[y].content)
                                        {
                                            paramOffset = parameterOffsets[z];
                                            break;
                                        }
                                    }
                                    if (paramOffset == -1) throw new Exception("Error writing parameter offset. Could not find parameter content!");
                                    writer.Write(paramOffset);
                                }
                            }

                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, offsetPairs.Count);
                            for (int p = 0; p < entitiesWithParams.Count; p++)
                            {
                                writer.Write(entitiesWithParams[p].nodeID.val);
                                writer.Write(offsetPairs[p].GlobalOffset / 4);
                                writer.Write(offsetPairs[p].EntryCount);
                            }
                            break;
                        }
                        case CommandsDataBlock.ENTITY_OVERRIDES:
                        {
                            List<OffsetPair> offsetPairs = new List<OffsetPair>();
                            for (int p = 0; p < _flowgraphs[i].overrides.Count; p++)
                            {
                                offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, _flowgraphs[i].overrides[p].hierarchy.Count));
                                Utilities.Write<cGUID>(writer, _flowgraphs[i].overrides[p].hierarchy);
                            }

                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, _flowgraphs[i].overrides.Count);
                            for (int p = 0; p < _flowgraphs[i].overrides.Count; p++)
                            {
                                writer.Write(_flowgraphs[i].overrides[p].nodeID.val);
                                writer.Write(offsetPairs[p].GlobalOffset / 4);
                                writer.Write(offsetPairs[p].EntryCount);
                            }
                            break;
                        }
                        case CommandsDataBlock.ENTITY_OVERRIDES_CHECKSUM:
                        {
                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, reshuffledChecksums.Count);
                            for (int p = 0; p < reshuffledChecksums.Count; p++)
                            {
                                writer.Write(reshuffledChecksums[p].nodeID.val);
                                writer.Write(reshuffledChecksums[p].checksum.val);
                            }
                            break;
                        }
                        case CommandsDataBlock.FLOWGRAPH_EXPOSED_PARAMETERS:
                        {
                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, _flowgraphs[i].datatypes.Count);
                            for (int p = 0; p < _flowgraphs[i].datatypes.Count; p++)
                            {
                                writer.Write(_flowgraphs[i].datatypes[p].nodeID.val);
                                writer.Write(CommandsUtils.GetDataTypeGUID(_flowgraphs[i].datatypes[p].type).val);
                                writer.Write(_flowgraphs[i].datatypes[p].parameter.val);
                            }
                            break;
                        }
                        case CommandsDataBlock.ENTITY_PROXIES:
                        {
                            List<OffsetPair> offsetPairs = new List<OffsetPair>();
                            for (int p = 0; p < _flowgraphs[i].proxies.Count; p++)
                            {
                                offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, _flowgraphs[i].proxies[p].hierarchy.Count));
                                Utilities.Write<cGUID>(writer, _flowgraphs[i].proxies[p].hierarchy);
                            }

                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, offsetPairs.Count);
                            for (int p = 0; p < _flowgraphs[i].proxies.Count; p++)
                            {
                                writer.Write(_flowgraphs[i].proxies[p].nodeID.val);
                                writer.Write(offsetPairs[p].GlobalOffset / 4);
                                writer.Write(offsetPairs[p].EntryCount);
                                writer.Write(_flowgraphs[i].proxies[p].nodeID.val);
                                writer.Write(_flowgraphs[i].proxies[p].extraId.val);
                            }
                            break;
                        }
                        case CommandsDataBlock.ENTITY_FUNCTIONS:
                        {
                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, _flowgraphs[i].functions.Count);
                            for (int p = 0; p < _flowgraphs[i].functions.Count; p++)
                            {
                                writer.Write(_flowgraphs[i].functions[p].nodeID.val);
                                writer.Write(_flowgraphs[i].functions[p].function.val);
                            }
                            break;
                        }
                        case CommandsDataBlock.RESOURCE_REFERENCES:
                        {
                            //TODO: this case is quite messy as full parsing still isn't known
                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, resourceReferences.Count);
                            for (int p = 0; p < resourceReferences.Count; p++)
                            {
                                writer.Write(resourceReferences[p].resourceRefID.val);
                                writer.Write(resourceReferences[p].unknownID1.val);
                                writer.Write(resourceReferences[p].positionOffset.x);
                                writer.Write(resourceReferences[p].positionOffset.y);
                                writer.Write(resourceReferences[p].positionOffset.z);
                                writer.Write(resourceReferences[p].unknownID2.val);
                                writer.Write(resourceReferences[p].resourceID.val); //Sometimes this is the NodeID that uses the resource, other times it's the "resource" parameter ID link
                                writer.Write(CommandsUtils.GetResourceEntryTypeGUID(resourceReferences[p].entryType).val);
                                switch (resourceReferences[p].entryType)
                                {
                                    case CathodeResourceReferenceType.RENDERABLE_INSTANCE:
                                        writer.Write(resourceReferences[p].entryIndexREDS);
                                        writer.Write(resourceReferences[p].entryCountREDS);
                                        break;
                                    case CathodeResourceReferenceType.COLLISION_MAPPING:
                                        writer.Write(resourceReferences[p].unknownInteger1);
                                        writer.Write(resourceReferences[p].nodeID.val);
                                        break;
                                    case CathodeResourceReferenceType.EXCLUSIVE_MASTER_STATE_RESOURCE:
                                    case CathodeResourceReferenceType.NAV_MESH_BARRIER_RESOURCE:
                                    case CathodeResourceReferenceType.TRAVERSAL_SEGMENT:
                                        writer.Write(resourceReferences[p].unknownInteger1);
                                        writer.Write(resourceReferences[p].unknownInteger2);
                                        break;
                                    case CathodeResourceReferenceType.ANIMATED_MODEL:
                                    case CathodeResourceReferenceType.DYNAMIC_PHYSICS_SYSTEM:
                                        writer.Write(resourceReferences[p].unknownInteger1);
                                        writer.Write(resourceReferences[p].unknownInteger2);
                                        break;
                                }
                            }
                            break;
                        }
                        case CommandsDataBlock.TRIGGERSEQUENCE_DATA: //Actually CAGEANIMATION_DATA, but indexes are flipped
                        {
                            List<int> globalOffsets = new List<int>();
                            for (int p = 0; p < cageAnimationNodes.Count; p++)
                            {
                                List<int> hierarchyOffsets = new List<int>();
                                for (int pp = 0; pp < cageAnimationNodes[p].keyframeHeaders.Count; pp++)
                                {
                                    hierarchyOffsets.Add((int)writer.BaseStream.Position);
                                    Utilities.Write<cGUID>(writer, cageAnimationNodes[p].keyframeHeaders[pp].connectedEntity);
                                }

                                int paramData1Offset = (int)writer.BaseStream.Position;
                                for (int pp = 0; pp < cageAnimationNodes[p].keyframeHeaders.Count; pp++)
                                {
                                    Utilities.Write(writer, cageAnimationNodes[p].keyframeHeaders[pp].ID);
                                    Utilities.Write(writer, CommandsUtils.GetDataTypeGUID(cageAnimationNodes[p].keyframeHeaders[pp].unk2));
                                    Utilities.Write(writer, cageAnimationNodes[p].keyframeHeaders[pp].keyframeDataID);
                                    Utilities.Write(writer, cageAnimationNodes[p].keyframeHeaders[pp].parameterID);
                                    Utilities.Write(writer, CommandsUtils.GetDataTypeGUID(cageAnimationNodes[p].keyframeHeaders[pp].parameterDataType));
                                    Utilities.Write(writer, cageAnimationNodes[p].keyframeHeaders[pp].parameterSubID);
                                    writer.Write(hierarchyOffsets[pp] / 4);
                                    writer.Write(cageAnimationNodes[p].keyframeHeaders[pp].connectedEntity.Count);
                                }

                                List<int> internalOffsets1 = new List<int>();
                                List<int> internalOffsets2 = new List<int>();
                                for (int pp = 0; pp < cageAnimationNodes[p].keyframeData.Count; pp++)
                                {
                                    int toPointTo = (int)writer.BaseStream.Position;
                                    for (int ppp = 0; ppp < cageAnimationNodes[p].keyframeData[pp].keyframes.Count; ppp++)
                                    {
                                        writer.Write(cageAnimationNodes[p].keyframeData[pp].keyframes[ppp].unk1);
                                        writer.Write(cageAnimationNodes[p].keyframeData[pp].keyframes[ppp].secondsSinceStart);
                                        writer.Write(cageAnimationNodes[p].keyframeData[pp].keyframes[ppp].secondsSinceStartValidation);
                                        writer.Write(cageAnimationNodes[p].keyframeData[pp].keyframes[ppp].paramValue);
                                        writer.Write(cageAnimationNodes[p].keyframeData[pp].keyframes[ppp].unk2);
                                        writer.Write(cageAnimationNodes[p].keyframeData[pp].keyframes[ppp].unk3);
                                        writer.Write(cageAnimationNodes[p].keyframeData[pp].keyframes[ppp].unk4);
                                        writer.Write(cageAnimationNodes[p].keyframeData[pp].keyframes[ppp].unk5);
                                    }

                                    internalOffsets1.Add(((int)writer.BaseStream.Position) / 4);

                                    writer.Write(cageAnimationNodes[p].keyframeData[pp].minSeconds);
                                    writer.Write(cageAnimationNodes[p].keyframeData[pp].maxSeconds);
                                    Utilities.Write(writer, cageAnimationNodes[p].keyframeData[pp].ID);

                                    writer.Write(toPointTo / 4);
                                    writer.Write(cageAnimationNodes[p].keyframeData[pp].keyframes.Count);
                                }

                                int paramData2Offset = (int)writer.BaseStream.Position;
                                Utilities.Write<int>(writer, internalOffsets1);

                                internalOffsets2 = new List<int>();
                                for (int pp = 0; pp < cageAnimationNodes[p].paramsData3.Count; pp++)
                                {
                                    int toPointTo = (int)writer.BaseStream.Position;
                                    for (int ppp = 0; ppp < cageAnimationNodes[p].paramsData3[pp].keyframes.Count; ppp++)
                                    {
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].keyframes[ppp].unk1);
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].keyframes[ppp].SecondsSinceStart);
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].keyframes[ppp].unk2);
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].keyframes[ppp].unk3);
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].keyframes[ppp].unk4);
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].keyframes[ppp].unk5);
                                    }

                                    internalOffsets2.Add(((int)writer.BaseStream.Position) / 4);

                                    writer.Write(cageAnimationNodes[p].paramsData3[pp].minSeconds);
                                    writer.Write(cageAnimationNodes[p].paramsData3[pp].maxSeconds);
                                    Utilities.Write(writer, cageAnimationNodes[p].paramsData3[pp].ID);

                                    writer.Write(toPointTo / 4);
                                    writer.Write(cageAnimationNodes[p].paramsData3[pp].keyframes.Count);
                                }

                                int paramData3Offset = (int)writer.BaseStream.Position;
                                Utilities.Write<int>(writer, internalOffsets2);

                                globalOffsets.Add((int)writer.BaseStream.Position);
                                writer.Write(cageAnimationNodes[p].nodeID.val);
                                writer.Write(paramData1Offset / 4);
                                writer.Write(cageAnimationNodes[p].keyframeHeaders.Count);
                                writer.Write(paramData2Offset / 4);
                                writer.Write(cageAnimationNodes[p].keyframeData.Count);
                                writer.Write(paramData3Offset / 4);
                                writer.Write(cageAnimationNodes[p].paramsData3.Count);
                            }

                            scriptPointerOffsetInfo[(int)CommandsDataBlock.CAGEANIMATION_DATA] = new OffsetPair(writer.BaseStream.Position, globalOffsets.Count);
                            for (int p = 0; p < globalOffsets.Count; p++)
                            {
                                writer.Write(globalOffsets[p] / 4);
                            }
                            break;
                        }
                        case CommandsDataBlock.CAGEANIMATION_DATA: //Actually TRIGGERSEQUENCE_DATA, but indexes are flipped
                        {
                            List<int> globalOffsets = new List<int>();
                            for (int p = 0; p < triggerSequenceNodes.Count; p++)
                            {
                                List<int> hierarchyOffsets = new List<int>();
                                for (int pp = 0; pp < triggerSequenceNodes[p].triggers.Count; pp++)
                                {
                                    hierarchyOffsets.Add((int)writer.BaseStream.Position);
                                    Utilities.Write<cGUID>(writer, triggerSequenceNodes[p].triggers[pp].hierarchy);
                                }

                                int triggerOffset = (int)writer.BaseStream.Position;
                                for (int pp = 0; pp < triggerSequenceNodes[p].triggers.Count; pp++)
                                {
                                    writer.Write(hierarchyOffsets[pp] / 4);
                                    writer.Write(triggerSequenceNodes[p].triggers[pp].hierarchy.Count);
                                    writer.Write(triggerSequenceNodes[p].triggers[pp].timing);
                                }

                                int eventOffset = (int)writer.BaseStream.Position;
                                for (int pp = 0; pp < triggerSequenceNodes[p].events.Count; pp++)
                                {
                                    writer.Write(triggerSequenceNodes[p].events[pp].EventID.val);
                                    writer.Write(triggerSequenceNodes[p].events[pp].StartedID.val);
                                    writer.Write(triggerSequenceNodes[p].events[pp].FinishedID.val);
                                }

                                globalOffsets.Add((int)writer.BaseStream.Position);
                                writer.Write(triggerSequenceNodes[p].nodeID.val);
                                writer.Write(triggerOffset / 4);
                                writer.Write(triggerSequenceNodes[p].triggers.Count);
                                writer.Write(eventOffset / 4);
                                writer.Write(triggerSequenceNodes[p].events.Count);
                            }

                            scriptPointerOffsetInfo[(int)CommandsDataBlock.TRIGGERSEQUENCE_DATA] = new OffsetPair(writer.BaseStream.Position, globalOffsets.Count);
                            for (int p = 0; p < globalOffsets.Count; p++)
                            {
                                writer.Write(globalOffsets[p] / 4);
                            }
                            break;
                        }
                        case CommandsDataBlock.UNUSED:
                        {
                            scriptPointerOffsetInfo[x] = new OffsetPair(0, 0);
                            break;
                        }
                        case CommandsDataBlock.UNKNOWN_COUNTS:
                        {
                            //TODO: These count values are unknown. Temp fix in place for the div by 4 at the end on offset (as this isn't actually an offset!)
                            scriptPointerOffsetInfo[x] = new OffsetPair(_flowgraphs[i].unknownPair.GlobalOffset * 4, _flowgraphs[i].unknownPair.EntryCount);
                            break;
                        }
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
                        scriptStartRaw[3] = 0x80;
                        writer.Write(scriptStartRaw);
                    }
                    writer.Write(scriptPointerOffsetInfo[x].GlobalOffset / 4); 
                    writer.Write(scriptPointerOffsetInfo[x].EntryCount);
                    if (x == 0) Utilities.Write<cGUID>(writer, _flowgraphs[i].nodeID);
                }
            }

            //Write out parameter offsets
            int parameterOffsetPos = (int)writer.BaseStream.Position;
            Utilities.Write<int>(writer, parameterOffsets);

            //Write out flowgraph offsets
            int flowgraphOffsetPos = (int)writer.BaseStream.Position;
            Utilities.Write<int>(writer, flowgraphOffsets);

            //Rewrite header info with correct offsets 
            writer.BaseStream.Position = offsetToRewrite;
            writer.Write(parameterOffsetPos / 4);
            writer.Write(parameters.Count);
            writer.Write(flowgraphOffsetPos / 4);
            writer.Write(_flowgraphs.Count);

            writer.Close();
        }

        /* Filter down a list of parameters to contain only unique entries */
        private List<CathodeParameter> PruneParameterList(List<CathodeParameter> parameters)
        {
            List<CathodeParameter> prunedList = new List<CathodeParameter>();
            bool canAdd = true;
            for (int i = 0; i < parameters.Count; i++)
            {
                canAdd = true;
                for (int x = 0; x < prunedList.Count; x++)
                {
                    if (prunedList[x] == parameters[i]) //This is where the bulk of our logic lies
                    {
                        canAdd = false;
                        break;
                    }
                }
                if (canAdd) prunedList.Add(parameters[i]);
            }
            return prunedList;
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
        private bool Load(string path)
        {
            if (!File.Exists(path)) return false;
            BinaryReader reader = new BinaryReader(File.OpenRead(path));

            //Read entry points
            _entryPoints = Utilities.Consume<CommandsEntryPoints>(reader);

            //Read parameter/flowgraph counts
            int parameter_offset_pos = reader.ReadInt32() * 4;
            int parameter_count = reader.ReadInt32();
            int flowgraph_offset_pos = reader.ReadInt32() * 4;
            int flowgraph_count = reader.ReadInt32();

            //Read parameter/flowgraph offsets
            reader.BaseStream.Position = parameter_offset_pos;
            int[] parameterOffsets = Utilities.ConsumeArray<int>(reader, parameter_count);
            reader.BaseStream.Position = flowgraph_offset_pos;
            int[] flowgraphOffsets = Utilities.ConsumeArray<int>(reader, flowgraph_count);

            //Read all parameters from the PAK
            Dictionary<int, CathodeParameter> parameters = new Dictionary<int, CathodeParameter>(parameter_count);
            for (int i = 0; i < parameter_count; i++)
            {
                reader.BaseStream.Position = parameterOffsets[i] * 4; 
                CathodeParameter this_parameter = new CathodeParameter(CommandsUtils.GetDataType(new cGUID(reader)));
                switch (this_parameter.dataType)
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
                        reader.BaseStream.Position += 8;
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
                        ((CathodeVector3)this_parameter).value = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        break;
                    case CathodeDataType.ENUM:
                        this_parameter = new CathodeEnum();
                        ((CathodeEnum)this_parameter).enumID = new cGUID(reader);
                        ((CathodeEnum)this_parameter).enumIndex = reader.ReadInt32();
                        break;
                    case CathodeDataType.SPLINE_DATA:
                        this_parameter = new CathodeSpline();
                        reader.BaseStream.Position += 4;
                        int num_points = reader.ReadInt32();
                        for (int x = 0; x < num_points; x++)
                        {
                            CathodeTransform this_point = new CathodeTransform();
                            this_point.position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            this_point.rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); //TODO is this YXZ?
                            ((CathodeSpline)this_parameter).splinePoints.Add(this_point);
                        }
                        break;
                }
                parameters.Add(parameterOffsets[i], this_parameter);
            }
            
            //Read all flowgraphs from the PAK
            CathodeFlowgraph[] flowgraphs = new CathodeFlowgraph[flowgraph_count];
            for (int i = 0; i < flowgraph_count; i++)
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
                    if (x == 0) flowgraph.nodeID = new cGUID(reader);
                }
                flowgraph.unknownPair = offsetPairs[12];

                //Read script ID and string name
                reader.BaseStream.Position = scriptStartOffset * 4;
                flowgraph.globalID = new cGUID(reader);
                flowgraph.name = Utilities.ReadString(reader);
                Utilities.Align(reader, 4);

                //Pull data from those offsets
                List<CommandsEntityLinks> entityLinks = new List<CommandsEntityLinks>();
                List<CommandsParamRefSet> paramRefSets = new List<CommandsParamRefSet>();
                List<CathodeResourceReference> resourceRefs = new List<CathodeResourceReference>();
                Dictionary<cGUID, (cGUID, int)> overrideChecksums = new Dictionary<cGUID, (cGUID, int)>();
                for (int x = 0; x < offsetPairs.Length; x++)
                {
                    reader.BaseStream.Position = offsetPairs[x].GlobalOffset * 4;
                    for (int y = 0; y < offsetPairs[x].EntryCount; y++)
                    {
                        switch ((CommandsDataBlock)x)
                        {
                            case CommandsDataBlock.ENTITY_CONNECTIONS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                entityLinks.Add(new CommandsEntityLinks(new cGUID(reader)));
                                int NumberOfParams = JumpToOffset(ref reader);
                                entityLinks[entityLinks.Count - 1].childLinks.AddRange(Utilities.ConsumeArray<CathodeNodeLink>(reader, NumberOfParams));
                                break;
                            }
                            case CommandsDataBlock.ENTITY_PARAMETERS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                paramRefSets.Add(new CommandsParamRefSet(new cGUID(reader)));
                                int NumberOfParams = JumpToOffset(ref reader);
                                paramRefSets[paramRefSets.Count - 1].refs.AddRange(Utilities.ConsumeArray<CathodeParameterReference>(reader, NumberOfParams));
                                break;
                            }
                            case CommandsDataBlock.ENTITY_OVERRIDES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                OverrideEntity overrider = new OverrideEntity(new cGUID(reader));
                                int NumberOfParams = JumpToOffset(ref reader);
                                overrider.hierarchy.AddRange(Utilities.ConsumeArray<cGUID>(reader, NumberOfParams));
                                flowgraph.overrides.Add(overrider);
                                break;
                            }
                            case CommandsDataBlock.ENTITY_OVERRIDES_CHECKSUM:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                overrideChecksums.Add(new cGUID(reader), (new cGUID(reader), (int)reader.BaseStream.Position)); //TODO: Added in reader.BaseStream.Position as offset hack before working out proper sort order algo
                                break;
                                }
                            //TODO: Really, I think these should be treated as parameters on the flowgraph class as they are the pins we use for flowgraph instances.
                            //      Need to look into this more and see if any of these entities actually contain much data other than links into the flowgraph itself.
                            case CommandsDataBlock.FLOWGRAPH_EXPOSED_PARAMETERS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                DatatypeEntity thisNode = new DatatypeEntity(new cGUID(reader));
                                thisNode.type = CommandsUtils.GetDataType(new cGUID(reader));
                                thisNode.parameter = new cGUID(reader);
                                flowgraph.datatypes.Add(thisNode);
                                break;
                            }
                            case CommandsDataBlock.ENTITY_PROXIES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 20);
                                ProxyEntity thisProxy = new ProxyEntity(new cGUID(reader));
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
                            case CommandsDataBlock.ENTITY_FUNCTIONS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                cGUID nodeID = new cGUID(reader);
                                cGUID functionID = new cGUID(reader);
                                if (CommandsUtils.FunctionTypeExists(functionID))
                                {
                                    //This node executes a hard-coded function
                                    CathodeFunctionType functionType = CommandsUtils.GetFunctionType(functionID);
                                    switch (functionType)
                                    {
                                        case CathodeFunctionType.CAGEAnimation:
                                            CAGEAnimation cageAnimation = new CAGEAnimation(nodeID);
                                            flowgraph.functions.Add(cageAnimation);
                                            break;
                                        case CathodeFunctionType.TriggerSequence:
                                            TriggerSequence triggerSequence = new TriggerSequence(nodeID);
                                            flowgraph.functions.Add(triggerSequence);
                                            break;
                                        default:
                                            FunctionEntity genericNode = new FunctionEntity(nodeID);
                                            genericNode.function = functionID;
                                            flowgraph.functions.Add(genericNode);
                                            break;
                                    }
                                }
                                else
                                {
                                    //This node executes a flowgraph
                                    FunctionEntity genericNode = new FunctionEntity(nodeID);
                                    genericNode.function = functionID;
                                    flowgraph.functions.Add(genericNode);
                                }
                                break;
                            }
                            //TODO: this case needs a refactor!
                            case CommandsDataBlock.RESOURCE_REFERENCES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 40);

                                //TODO: these values change by entry type - need to work out what they're for before allowing editing
                                CathodeResourceReference resource_ref = new CathodeResourceReference();
                                resource_ref.resourceRefID = new cGUID(reader); //renderable element ID (also used in one of the param blocks for something)
                                resource_ref.unknownID1 = new cGUID(reader); //unk (sometimes 0x00 x4?)
                                resource_ref.positionOffset = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); //position offset
                                resource_ref.unknownID2 = new cGUID(reader); //unk (sometimes 0x00 x4?)
                                resource_ref.resourceID = new cGUID(reader); //resource id
                                resource_ref.entryType = CommandsUtils.GetResourceEntryType(reader.ReadBytes(4)); //entry type
                                switch (resource_ref.entryType)
                                {
                                    case CathodeResourceReferenceType.RENDERABLE_INSTANCE:
                                        resource_ref.entryIndexREDS = reader.ReadInt32(); //REDS.BIN entry index
                                        resource_ref.entryCountREDS = reader.ReadInt32(); //REDS.BIN entry count
                                        break;
                                    case CathodeResourceReferenceType.COLLISION_MAPPING:
                                        resource_ref.unknownInteger1 = reader.ReadInt32(); //unknown integer (COLLISION.MAP index?)
                                        resource_ref.nodeID = new cGUID(reader); //ID which maps to the node using the resource (?) - check GetFriendlyName
                                        break;
                                    case CathodeResourceReferenceType.EXCLUSIVE_MASTER_STATE_RESOURCE:
                                    case CathodeResourceReferenceType.NAV_MESH_BARRIER_RESOURCE:
                                    case CathodeResourceReferenceType.TRAVERSAL_SEGMENT:
                                        resource_ref.unknownInteger1 = reader.ReadInt32(); //always -1?
                                        resource_ref.unknownInteger2 = reader.ReadInt32(); //always -1?
                                        break;
                                    case CathodeResourceReferenceType.ANIMATED_MODEL:
                                    case CathodeResourceReferenceType.DYNAMIC_PHYSICS_SYSTEM:
                                        resource_ref.unknownInteger1 = reader.ReadInt32(); //unknown integer
                                        resource_ref.unknownInteger2 = reader.ReadInt32(); //always zero/-1?
                                        break;
                                }
                                    if (resource_ref.entryIndexREDS == 20 && resource_ref.entryCountREDS == 1)
                                    {
                                        string breakhere = "";
                                    }
                                resourceRefs.Add(resource_ref);
                                break;
                            }
                            case CommandsDataBlock.CAGEANIMATION_DATA:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                reader.BaseStream.Position = (reader.ReadInt32() * 4);

                                CathodeEntity thisEntity = flowgraph.GetEntityByID(new cGUID(reader));
                                if (thisEntity.variant == EntityVariant.PROXY)
                                {
                                    break; // We don't handle this just yet... need to resolve the proxy.
                                }
                                CAGEAnimation thisNode = (CAGEAnimation)thisEntity;

                                int keyframeHeaderOffset = reader.ReadInt32() * 4;
                                int numberOfKeyframeHeaders = reader.ReadInt32();
                                int keyframeDataOffset = reader.ReadInt32() * 4;
                                int numberOfKeyframeDataEntries = reader.ReadInt32();
                                int OffsetToFindParams3 = reader.ReadInt32() * 4;
                                int NumberOfParams3 = reader.ReadInt32();

                                for (int z = 0; z < numberOfKeyframeHeaders; z++)
                                {
                                    reader.BaseStream.Position = keyframeHeaderOffset + (z * 32);

                                    CathodeParameterKeyframeHeader thisHeader = new CathodeParameterKeyframeHeader();
                                    thisHeader.ID = new cGUID(reader);//ID
                                    thisHeader.unk2 = CommandsUtils.GetDataType(new cGUID(reader)); //Datatype, seems to usually be NO_TYPE
                                    thisHeader.keyframeDataID = new cGUID(reader); 
                                    thisHeader.parameterID = new cGUID(reader); 
                                    thisHeader.parameterDataType = CommandsUtils.GetDataType(new cGUID(reader)); 
                                    thisHeader.parameterSubID = new cGUID(reader); 

                                    int hierarchyCount = JumpToOffset(ref reader);
                                    thisHeader.connectedEntity = Utilities.ConsumeArray<cGUID>(reader, hierarchyCount).ToList<cGUID>(); 
                                    thisNode.keyframeHeaders.Add(thisHeader);
                                }
                                
                                reader.BaseStream.Position = keyframeDataOffset;
                                int[] newOffset = Utilities.ConsumeArray<int>(reader, numberOfKeyframeDataEntries);
                                for (int z = 0; z < numberOfKeyframeDataEntries; z++)
                                {
                                    reader.BaseStream.Position = newOffset[z] * 4;

                                    CathodeParameterKeyframe thisParamKey = new CathodeParameterKeyframe();
                                    thisParamKey.minSeconds = reader.ReadSingle();
                                    thisParamKey.maxSeconds = reader.ReadSingle(); //max seconds for keyframe list
                                    thisParamKey.ID = new cGUID(reader); //this is perhaps a node id

                                    int numberOfKeyframes = JumpToOffset(ref reader);
                                    for (int m = 0; m < numberOfKeyframes; m++)
                                    {
                                        CathodeKeyframe thisKeyframe = new CathodeKeyframe();
                                        thisKeyframe.unk1 = reader.ReadSingle(); //
                                        thisKeyframe.secondsSinceStart = reader.ReadSingle(); //Seconds since start of animation
                                        thisKeyframe.secondsSinceStartValidation = reader.ReadSingle(); //Seconds since start of animation
                                        thisKeyframe.paramValue = reader.ReadSingle(); //Parameter value
                                        thisKeyframe.unk2 = reader.ReadSingle(); //
                                        thisKeyframe.unk3 = reader.ReadSingle(); // 
                                        thisKeyframe.unk4 = reader.ReadSingle(); //
                                        thisKeyframe.unk5 = reader.ReadSingle(); //
                                        thisParamKey.keyframes.Add(thisKeyframe);
                                    }
                                    thisNode.keyframeData.Add(thisParamKey);
                                }

                                //UNKNOWN - is this maybe event triggers?
                                reader.BaseStream.Position = OffsetToFindParams3;
                                int[] newOffset1 = Utilities.ConsumeArray<int>(reader, NumberOfParams3);
                                for (int z = 0; z < NumberOfParams3; z++)
                                {
                                    reader.BaseStream.Position = newOffset1[z] * 4;

                                    TEMP_CAGEAnimationExtraDataHolder3 thisParamSet = new TEMP_CAGEAnimationExtraDataHolder3();
                                    thisParamSet.minSeconds = reader.ReadSingle();
                                    thisParamSet.maxSeconds = reader.ReadSingle();
                                    thisParamSet.ID = new cGUID(reader); //this is perhaps a node id

                                    int NumberOfParams3_ = JumpToOffset(ref reader);
                                    for (int m = 0; m < NumberOfParams3_; m++)
                                    {
                                        TEMP_CAGEAnimationExtraDataHolder3_1 thisInnerSet = new TEMP_CAGEAnimationExtraDataHolder3_1();
                                        thisInnerSet.unk1 = reader.ReadSingle(); //type?
                                        thisInnerSet.SecondsSinceStart = reader.ReadSingle(); //seconds since start of animation
                                        thisInnerSet.unk2 = reader.ReadSingle(); //id ?
                                        thisInnerSet.unk3 = reader.ReadSingle(); // id ?
                                        thisInnerSet.unk4 = reader.ReadSingle(); //
                                        thisInnerSet.unk5 = reader.ReadSingle(); //
                                        thisParamSet.keyframes.Add(thisInnerSet);
                                    }
                                    thisNode.paramsData3.Add(thisParamSet);
                                }
                                break;
                            }
                            case CommandsDataBlock.TRIGGERSEQUENCE_DATA:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                reader.BaseStream.Position = (reader.ReadInt32() * 4);

                                CathodeEntity thisEntity = flowgraph.GetEntityByID(new cGUID(reader));
                                if (thisEntity.variant == EntityVariant.PROXY)
                                {
                                    break; // We don't handle this just yet... need to resolve the proxy.
                                }
                                TriggerSequence thisNode = (TriggerSequence)thisEntity;

                                int triggersOffset = reader.ReadInt32() * 4;
                                int triggersCount = reader.ReadInt32();
                                int eventsOffset = reader.ReadInt32() * 4;
                                int eventsCount = reader.ReadInt32();

                                for (int z = 0; z < triggersCount; z++)
                                {
                                    reader.BaseStream.Position = triggersOffset + (z * 12);
                                    int hierarchyOffset = reader.ReadInt32() * 4;
                                    int hierarchyCount = reader.ReadInt32();

                                    TEMP_TriggerSequenceExtraDataHolder1 thisTrigger = new TEMP_TriggerSequenceExtraDataHolder1();
                                    thisTrigger.timing = reader.ReadSingle();
                                    reader.BaseStream.Position = hierarchyOffset;
                                    thisTrigger.hierarchy = Utilities.ConsumeArray<cGUID>(reader, hierarchyCount).ToList<cGUID>();
                                    thisNode.triggers.Add(thisTrigger);
                                }

                                for (int z = 0; z < eventsCount; z++)
                                {
                                    reader.BaseStream.Position = eventsOffset + (z * 12);

                                    TEMP_TriggerSequenceExtraDataHolder2 thisEvent = new TEMP_TriggerSequenceExtraDataHolder2();
                                    thisEvent.EventID = new cGUID(reader);
                                    thisEvent.StartedID = new cGUID(reader);
                                    thisEvent.FinishedID = new cGUID(reader);
                                    thisNode.events.Add(thisEvent);
                                }
                                break;
                            }
                        }
                    }
                }

                for (int x = 0; x < flowgraph.overrides.Count; x++)
                {
                    flowgraph.overrides[x].checksum = overrideChecksums[flowgraph.overrides[x].nodeID].Item1;
                }
                for (int x = 0; x < entityLinks.Count; x++)
                {
                    CathodeEntity nodeToApply = flowgraph.GetEntityByID(entityLinks[x].parentID);
                    if (nodeToApply == null)
                    {
                        //TODO: We shouldn't hit this, but we do... is this perhaps an ID from another flowgraph, similar to proxies?
                        nodeToApply = new CathodeEntity(entityLinks[x].parentID);
                        flowgraph.unknowns.Add(nodeToApply);
                    }
                    nodeToApply.childLinks.AddRange(entityLinks[x].childLinks);
                }
                for (int x = 0; x < paramRefSets.Count; x++)
                {
                    CathodeEntity nodeToApply = flowgraph.GetEntityByID(paramRefSets[x].id);
                    if (nodeToApply == null)
                    {
                        //TODO: We shouldn't hit this, but we do... is this perhaps an ID from another flowgraph, similar to proxies?
                        nodeToApply = new CathodeEntity(paramRefSets[x].id);
                        flowgraph.unknowns.Add(nodeToApply);
                    }
                    for (int y = 0; y < paramRefSets[x].refs.Count; y++)
                    {
                        nodeToApply.parameters.Add(new CathodeLoadedParameter(paramRefSets[x].refs[y].paramID, (CathodeParameter)parameters[paramRefSets[x].refs[y].offset].Clone()));
                    }
                }

                //Remap resources (TODO: This can be optimised)
                List<CathodeEntity> ents = flowgraph.GetEntities();
                cGUID resParamID = Utilities.GenerateGUID("resource");
                //Check to see if this resource applies to an ENTITY
                List<CathodeResourceReference> resourceRefsCulled = new List<CathodeResourceReference>();
                for (int x = 0; x < resourceRefs.Count; x++)
                {
                    CathodeEntity ent = ents.FirstOrDefault(o => o.nodeID == resourceRefs[x].resourceID);
                    if (ent != null)
                    {
                        ent.resources.Add(resourceRefs[x]);
                        continue;
                    }
                    resourceRefsCulled.Add(resourceRefs[x]);
                }
                resourceRefs = resourceRefsCulled;
                //Check to see if this resource applies to a PARAMETER
                for (int z = 0; z < ents.Count; z++)
                {
                    for (int y = 0; y < ents[z].parameters.Count; y++)
                    {
                        if (ents[z].parameters[y].paramID != resParamID) continue;

                        CathodeResource resourceParam = (CathodeResource)ents[z].parameters[y].content;
                        resourceRefsCulled = new List<CathodeResourceReference>();
                        for (int m = 0; m < resourceRefs.Count; m++)
                        {
                            if (resourceParam.resourceID == resourceRefs[m].resourceID)
                            {
                                resourceParam.value.Add(resourceRefs[m]);
                                continue;
                            }
                            resourceRefsCulled.Add(resourceRefs[m]);
                        }
                        resourceRefs = resourceRefsCulled;
                    }
                }
                //If it applied to none of the above, apply it to the FLOWGRAPH
                for (int z = 0; z < resourceRefs.Count; z++)
                {
                    flowgraph.resources.Add(resourceRefs[z]);
                }

                flowgraphs[i] = flowgraph;
            }
            _flowgraphs = flowgraphs.ToList<CathodeFlowgraph>();

            reader.Close();
            return true;
        }
        #endregion

        private string _path = "";

        private CommandsEntryPoints _entryPoints;
        private CathodeFlowgraph[] _entryPointObjects = null;

        private List<CathodeFlowgraph> _flowgraphs = null;

        private bool _didLoadCorrectly = false;
        public bool Loaded { get { return _didLoadCorrectly; } }
        public string Filepath { get { return _path; } }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CommandsEntryPoints
    {
        // This is always:
        //  - Root Instance (the map's entry flowgraph, usually containing entities that call mission/environment flowgraphs)
        //  - Global Instance (the main data handler for keeping track of mission number, etc - kinda like a big singleton)
        //  - Pause Menu Instance

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public cGUID[] flowgraphIDs;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CommandsOffsetPair
    {
        public int offset;
        public int count;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CathodeNodeLink
    {
        public cGUID connectionID;  //The unique ID for this connection
        public cGUID parentParamID; //The ID of the parameter we're providing out 
        public cGUID childParamID;  //The ID of the parameter we're providing into the child
        public cGUID childID;       //The ID of the entity we're linking to to provide the value for
    }

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

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CathodeParameterReference
    {
        public cGUID paramID; //The ID of the param in the node
        public int offset;    //The offset of the param this reference points to (in memory this is *4)
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
        public int Index = -1; //TEMP TEST

        public cGUID id;
        public List<CathodeParameterReference> refs = new List<CathodeParameterReference>();

        public CommandsParamRefSet(cGUID _id)
        {
            id = _id;
        }
    }

    /* TEMP STUFF TO FIX REWRITING */
    [Serializable]
    public class CathodeParameterKeyframeHeader
    {
        public cGUID ID;
        public CathodeDataType unk2;
        public cGUID keyframeDataID;
        //public float unk3;
        public cGUID parameterID;
        public CathodeDataType parameterDataType;
        public cGUID parameterSubID; //if parameterID is position, this might be x for example
        public List<cGUID> connectedEntity; //path to controlled entity
    }
    [Serializable]
    public class CathodeParameterKeyframe
    {
        public float minSeconds;
        public float maxSeconds;
        public cGUID ID;
        public List<CathodeKeyframe> keyframes = new List<CathodeKeyframe>();
    }
    [Serializable]
    public class CathodeKeyframe
    {
        public float unk1;
        public float secondsSinceStart;
        public float secondsSinceStartValidation;
        public float paramValue;
        public float unk2;
        public float unk3;
        public float unk4;
        public float unk5;
    }
    [Serializable]
    public class TEMP_CAGEAnimationExtraDataHolder3
    {
        public float minSeconds;
        public float maxSeconds;
        public cGUID ID;
        public List<TEMP_CAGEAnimationExtraDataHolder3_1> keyframes = new List<TEMP_CAGEAnimationExtraDataHolder3_1>();
    }
    [Serializable]
    public class TEMP_CAGEAnimationExtraDataHolder3_1
    {
        public float unk1;
        public float SecondsSinceStart;
        public float unk2;
        public float unk3;
        public float unk4;
        public float unk5;
    }
    [Serializable]
    public class TEMP_TriggerSequenceExtraDataHolder1
    {
        public float timing;
        public List<cGUID> hierarchy;
    }
    [Serializable]
    public class TEMP_TriggerSequenceExtraDataHolder2
    {
        public cGUID EventID; //Assumed
        public cGUID StartedID; //Assumed
        public cGUID FinishedID;
    }
}
