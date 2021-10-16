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

namespace CATHODE.Commands
{
    public class CommandsPAK
    {
        /* Load and parse the COMMANDS.PAK */
        public CommandsPAK(string pathToPak)
        {
            SetupFunctionTypeLUT();
            SetupDataTypeLUT();
            SetupResourceEntryTypeLUT();

            path = pathToPak;
            _didLoadCorrectly = Load(path);
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
        #endregion

        #region WRITING
        /* Save all changes back out */
        public void Save()
        {
            BinaryWriter writer = new BinaryWriter(File.OpenWrite(path));
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
                        if (!parameters.Contains(fgEntities[x].parameters[y].content)) parameters.Add(fgEntities[x].parameters[y].content);
            }

            //Write out parameters & track offsets
            int[] parameterOffsets = new int[parameters.Count];
            for (int i = 0; i < parameters.Count; i++)
            {
                parameterOffsets[i] = (int)writer.BaseStream.Position / 4;
                Utilities.Write<cGUID>(writer, GetDataTypeGUID(parameters[i].dataType));
                switch (parameters[i].dataType)
                {
                    case CathodeDataType.POSITION:
                        Vector3 pos = ((CathodeTransform)parameters[i]).position;
                        Vector3 rot = ((CathodeTransform)parameters[i]).rotation;
                        writer.Write(pos.X); writer.Write(pos.Y); writer.Write(pos.Z);
                        writer.Write(rot.Y); writer.Write(rot.X); writer.Write(rot.Z);
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
                        writer.Write(dir.Y); writer.Write(dir.X); writer.Write(dir.Z);
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
                            writer.Write(thisSpline.splinePoints[x].position.X);
                            writer.Write(thisSpline.splinePoints[x].position.Y);
                            writer.Write(thisSpline.splinePoints[x].position.Z);
                            //todo: is this YXZ
                            writer.Write(thisSpline.splinePoints[x].rotation.X);
                            writer.Write(thisSpline.splinePoints[x].rotation.Y);
                            writer.Write(thisSpline.splinePoints[x].rotation.Z);
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
                List<CathodeEntity> entitiesWithLinks = new List<CathodeEntity>(_flowgraphs[i].GetEntities().FindAll(o => o.childLinks.Count != 0));
                List<CathodeEntity> entitiesWithParams = new List<CathodeEntity>(_flowgraphs[i].GetEntities().FindAll(o => o.parameters.Count != 0));
                //TODO: find a nicer way to sort into node class types
                List<CAGEAnimation> cageAnimationNodes = new List<CAGEAnimation>();
                List<TriggerSequence> triggerSequenceNodes = new List<TriggerSequence>();
                cGUID cageAnimationGUID = GetFunctionTypeGUID(CathodeFunctionType.CAGEAnimation);
                cGUID triggerSequenceGUID = GetFunctionTypeGUID(CathodeFunctionType.TriggerSequence);
                for (int x = 0; x < _flowgraphs[i].functions.Count; x++)
                {
                    if (_flowgraphs[i].functions[x].function == cageAnimationGUID)
                    {
                        CAGEAnimation thisNode = (CAGEAnimation)_flowgraphs[i].functions[x];
                        if (thisNode.paramsData1.Count == 0 && thisNode.paramsData2.Count == 0 && thisNode.paramsData3.Count == 0) continue;
                        cageAnimationNodes.Add(thisNode);
                    }
                    else if (_flowgraphs[i].functions[x].function == triggerSequenceGUID)
                    {
                        TriggerSequence thisNode = (TriggerSequence)_flowgraphs[i].functions[x];
                        if (thisNode.triggers.Count == 0 && thisNode.events.Count == 0) continue;
                        triggerSequenceNodes.Add(thisNode);
                    }
                }

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
                                writer.Write(GetDataTypeGUID(_flowgraphs[i].datatypes[p].type).val);
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
                        case CommandsDataBlock.RENDERABLE_DATA:
                        {
                            //TODO: this case is quite messy as full parsing still isn't known
                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, _flowgraphs[i].resources.Count);
                            for (int p = 0; p < _flowgraphs[i].resources.Count; p++)
                            {
                                writer.Write(_flowgraphs[i].resources[p].resourceRefID.val);
                                writer.Write(_flowgraphs[i].resources[p].unknownID1.val);
                                writer.Write(_flowgraphs[i].resources[p].positionOffset.X);
                                writer.Write(_flowgraphs[i].resources[p].positionOffset.Y);
                                writer.Write(_flowgraphs[i].resources[p].positionOffset.Z);
                                writer.Write(_flowgraphs[i].resources[p].unknownID2.val);
                                writer.Write(_flowgraphs[i].resources[p].resourceID.val);
                                writer.Write(GetResourceEntryTypeGUID(_flowgraphs[i].resources[p].entryType).val);
                                switch (_flowgraphs[i].resources[p].entryType)
                                {
                                    case CathodeResourceReferenceType.RENDERABLE_INSTANCE:
                                        writer.Write(_flowgraphs[i].resources[p].entryIndexREDS);
                                        writer.Write(_flowgraphs[i].resources[p].entryCountREDS);
                                        break;
                                    case CathodeResourceReferenceType.COLLISION_MAPPING:
                                        writer.Write(_flowgraphs[i].resources[p].unknownInteger1);
                                        writer.Write(_flowgraphs[i].resources[p].nodeID.val);
                                        break;
                                    case CathodeResourceReferenceType.EXCLUSIVE_MASTER_STATE_RESOURCE:
                                    case CathodeResourceReferenceType.NAV_MESH_BARRIER_RESOURCE:
                                    case CathodeResourceReferenceType.TRAVERSAL_SEGMENT:
                                        writer.Write(_flowgraphs[i].resources[p].unknownInteger1);
                                        writer.Write(_flowgraphs[i].resources[p].unknownInteger2);
                                        break;
                                    case CathodeResourceReferenceType.ANIMATED_MODEL:
                                    case CathodeResourceReferenceType.DYNAMIC_PHYSICS_SYSTEM:
                                        writer.Write(_flowgraphs[i].resources[p].unknownInteger1);
                                        writer.Write(_flowgraphs[i].resources[p].unknownInteger2);
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
                                for (int pp = 0; pp < cageAnimationNodes[p].paramsData1.Count; pp++)
                                {
                                    hierarchyOffsets.Add((int)writer.BaseStream.Position);
                                    Utilities.Write<cGUID>(writer, cageAnimationNodes[p].paramsData1[pp].hierarchy);
                                }

                                int paramData1Offset = (int)writer.BaseStream.Position;
                                for (int pp = 0; pp < cageAnimationNodes[p].paramsData1.Count; pp++)
                                {
                                    writer.Write(cageAnimationNodes[p].paramsData1[pp].unk1.val);
                                    //writer.Write(GetDataTypeGUID(cageAnimationNodes[p].paramsData1[pp].unk2).val);
                                    writer.Write(cageAnimationNodes[p].paramsData1[pp].unk2.val);
                                    writer.Write(cageAnimationNodes[p].paramsData1[pp].unk3.val);
                                    writer.Write(cageAnimationNodes[p].paramsData1[pp].unk4.val);
                                    //writer.Write(GetDataTypeGUID(cageAnimationNodes[p].paramsData1[pp].unk5).val);
                                    writer.Write(cageAnimationNodes[p].paramsData1[pp].unk5.val);
                                    writer.Write(cageAnimationNodes[p].paramsData1[pp].unk6.val);
                                    writer.Write(hierarchyOffsets[pp] / 4);
                                    writer.Write(cageAnimationNodes[p].paramsData1[pp].hierarchy.Count);
                                }

                                List<int> internalOffsets1 = new List<int>();
                                List<int> internalOffsets2 = new List<int>();
                                for (int pp = 0; pp < cageAnimationNodes[p].paramsData2.Count; pp++)
                                {
                                    int toPointTo = (int)writer.BaseStream.Position;
                                    for (int ppp = 0; ppp < cageAnimationNodes[p].paramsData2[pp].innerSets.Count; ppp++)
                                    {
                                        writer.Write(cageAnimationNodes[p].paramsData2[pp].innerSets[ppp].unk3);
                                        writer.Write(cageAnimationNodes[p].paramsData2[pp].innerSets[ppp].unk4);
                                        writer.Write(cageAnimationNodes[p].paramsData2[pp].innerSets[ppp].unk5);
                                        writer.Write(cageAnimationNodes[p].paramsData2[pp].innerSets[ppp].unk6);
                                        writer.Write(cageAnimationNodes[p].paramsData2[pp].innerSets[ppp].unk7);
                                        writer.Write(cageAnimationNodes[p].paramsData2[pp].innerSets[ppp].unk8);
                                        writer.Write(cageAnimationNodes[p].paramsData2[pp].innerSets[ppp].unk9);
                                        writer.Write(cageAnimationNodes[p].paramsData2[pp].innerSets[ppp].unk10);
                                    }

                                    internalOffsets1.Add(((int)writer.BaseStream.Position) / 4);

                                    writer.Write(cageAnimationNodes[p].paramsData2[pp].unk0);
                                    writer.Write(cageAnimationNodes[p].paramsData2[pp].unk1);
                                    writer.Write(cageAnimationNodes[p].paramsData2[pp].unk2);

                                    writer.Write(toPointTo / 4);
                                    writer.Write(cageAnimationNodes[p].paramsData2[pp].innerSets.Count);
                                }

                                int paramData2Offset = (int)writer.BaseStream.Position;
                                Utilities.Write<int>(writer, internalOffsets1);

                                internalOffsets2 = new List<int>();
                                for (int pp = 0; pp < cageAnimationNodes[p].paramsData3.Count; pp++)
                                {
                                    int toPointTo = (int)writer.BaseStream.Position;
                                    for (int ppp = 0; ppp < cageAnimationNodes[p].paramsData3[pp].innerSets.Count; ppp++)
                                    {
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].innerSets[ppp].unk3);
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].innerSets[ppp].unk4);
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].innerSets[ppp].unk5);
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].innerSets[ppp].unk6);
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].innerSets[ppp].unk7);
                                        writer.Write(cageAnimationNodes[p].paramsData3[pp].innerSets[ppp].unk8);
                                    }

                                    internalOffsets2.Add(((int)writer.BaseStream.Position) / 4);

                                    writer.Write(cageAnimationNodes[p].paramsData3[pp].unk0);
                                    writer.Write(cageAnimationNodes[p].paramsData3[pp].unk1);
                                    writer.Write(cageAnimationNodes[p].paramsData3[pp].unk2);

                                    writer.Write(toPointTo / 4);
                                    writer.Write(cageAnimationNodes[p].paramsData3[pp].innerSets.Count);
                                }

                                int paramData3Offset = (int)writer.BaseStream.Position;
                                Utilities.Write<int>(writer, internalOffsets2);

                                globalOffsets.Add((int)writer.BaseStream.Position);
                                writer.Write(cageAnimationNodes[p].nodeID.val);
                                writer.Write(paramData1Offset / 4);
                                writer.Write(cageAnimationNodes[p].paramsData1.Count);
                                writer.Write(paramData2Offset / 4);
                                writer.Write(cageAnimationNodes[p].paramsData2.Count);
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
                CathodeParameter this_parameter = new CathodeParameter(GetDataType(new cGUID(reader)));
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
                        float __x, __y, __z; __y = reader.ReadSingle(); __x = reader.ReadSingle(); __z = reader.ReadSingle(); //Y,X,Z!
                        ((CathodeVector3)this_parameter).value = new Vector3(__x, __y, __z);
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
                            case CommandsDataBlock.FLOWGRAPH_EXPOSED_PARAMETERS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                DatatypeEntity thisNode = new DatatypeEntity(new cGUID(reader));
                                thisNode.type = GetDataType(new cGUID(reader));
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
                                if (_functionTypeLUT.ContainsKey(functionID))
                                {
                                    //This node executes a hard-coded function
                                    CathodeFunctionType functionType = GetFunctionType(functionID);
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
                            case CommandsDataBlock.RENDERABLE_DATA:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 40);

                                //TODO: these values change by entry type - need to work out what they're for before allowing editing
                                CathodeResourceReference resource_ref = new CathodeResourceReference();
                                resource_ref.resourceRefID = new cGUID(reader); //renderable element ID (also used in one of the param blocks for something)
                                resource_ref.unknownID1 = new cGUID(reader); //unk (sometimes 0x00 x4?)
                                resource_ref.positionOffset = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); //position offset
                                resource_ref.unknownID2 = new cGUID(reader); //unk (sometimes 0x00 x4?)
                                resource_ref.resourceID = new cGUID(reader); //resource id
                                resource_ref.entryType = GetResourceEntryType(reader.ReadBytes(4)); //entry type
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
                                flowgraph.resources.Add(resource_ref);
                                break;
                            }
                            case CommandsDataBlock.CAGEANIMATION_DATA:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                reader.BaseStream.Position = (reader.ReadInt32() * 4);

                                //THIS SOMETIMES TRANSLATES TO A PROXY - NOT A CAGEANIMATION NODE
                                CAGEAnimation thisNode = (CAGEAnimation)flowgraph.GetEntityByID(new cGUID(reader));

                                int OffsetToFindParams = reader.ReadInt32() * 4;
                                int NumberOfParams = reader.ReadInt32();
                                int OffsetToFindParams2 = reader.ReadInt32() * 4;
                                int NumberOfParams2 = reader.ReadInt32();
                                int OffsetToFindParams3 = reader.ReadInt32() * 4;
                                int NumberOfParams3 = reader.ReadInt32();

                                for (int z = 0; z < NumberOfParams; z++)
                                {
                                    reader.BaseStream.Position = OffsetToFindParams + (z * 32);

                                    TEMP_CAGEAnimationExtraDataHolder1 thisParamSet = new TEMP_CAGEAnimationExtraDataHolder1();
                                    thisParamSet.unk1 = new cGUID(reader);//Unknown ID (does this link to unknown param ID on CAGEAnimation nodes?

                                    //thisParamSet.unk2 = GetDataType(new cGUID(reader)); //Datatype... used for?
                                    thisParamSet.unk2 = new cGUID(reader);
                                    thisParamSet.unk3 = new cGUID(reader); //Unknown ID (does this link to unknown param ID on CAGEAnimation nodes?
                                    thisParamSet.unk4 = new cGUID(reader); //Unknown ID - is this a named parameter id? (does this link to unknown param ID on CAGEAnimation nodes?

                                    //thisParamSet.unk5 = GetDataType(new cGUID(reader)); //Datatype... used for?
                                    thisParamSet.unk5 = new cGUID(reader);
                                    thisParamSet.unk6 = new cGUID(reader); //Unknown ID (does this link to unknown param ID on CAGEAnimation nodes?

                                    int NumberOfParams_ = JumpToOffset(ref reader);
                                    thisParamSet.hierarchy = Utilities.ConsumeArray<cGUID>(reader, NumberOfParams_).ToList<cGUID>(); 
                                    thisNode.paramsData1.Add(thisParamSet);
                                }

                                reader.BaseStream.Position = OffsetToFindParams2;
                                int[] newOffset = Utilities.ConsumeArray<int>(reader, NumberOfParams2);
                                for (int z = 0; z < NumberOfParams2; z++)
                                {
                                    reader.BaseStream.Position = newOffset[z] * 4;

                                    TEMP_CAGEAnimationExtraDataHolder2 thisParamSet = new TEMP_CAGEAnimationExtraDataHolder2();
                                    thisParamSet.unk0 = reader.ReadSingle();
                                    thisParamSet.unk1 = reader.ReadSingle();
                                    thisParamSet.unk2 = reader.ReadInt32();

                                    int NumberOfParams2_ = JumpToOffset(ref reader);
                                    for (int m = 0; m < NumberOfParams2_; m++)
                                    {
                                        TEMP_CAGEAnimationExtraDataHolder2_1 thisInnerSet = new TEMP_CAGEAnimationExtraDataHolder2_1();
                                        thisInnerSet.unk3 = reader.ReadInt32();
                                        thisInnerSet.unk4 = reader.ReadSingle();
                                        thisInnerSet.unk5 = reader.ReadSingle();
                                        thisInnerSet.unk6 = reader.ReadSingle();
                                        thisInnerSet.unk7 = reader.ReadSingle();
                                        thisInnerSet.unk8 = reader.ReadSingle();
                                        thisInnerSet.unk9 = reader.ReadSingle();
                                        thisInnerSet.unk10 = reader.ReadSingle();
                                        thisParamSet.innerSets.Add(thisInnerSet);
                                    }
                                    thisNode.paramsData2.Add(thisParamSet);
                                }

                                reader.BaseStream.Position = OffsetToFindParams3;
                                int[] newOffset1 = Utilities.ConsumeArray<int>(reader, NumberOfParams3);
                                for (int z = 0; z < NumberOfParams3; z++)
                                {
                                    reader.BaseStream.Position = newOffset1[z] * 4;

                                    TEMP_CAGEAnimationExtraDataHolder3 thisParamSet = new TEMP_CAGEAnimationExtraDataHolder3();
                                    thisParamSet.unk0 = reader.ReadSingle();
                                    thisParamSet.unk1 = reader.ReadSingle();
                                    thisParamSet.unk2 = reader.ReadInt32();

                                    int NumberOfParams3_ = JumpToOffset(ref reader);
                                    for (int m = 0; m < NumberOfParams3_; m++)
                                    {
                                        TEMP_CAGEAnimationExtraDataHolder3_1 thisInnerSet = new TEMP_CAGEAnimationExtraDataHolder3_1();
                                        thisInnerSet.unk3 = reader.ReadInt32(); //count?
                                        thisInnerSet.unk4 = reader.ReadSingle();
                                        thisInnerSet.unk5 = reader.ReadInt32(); //id?
                                        thisInnerSet.unk6 = reader.ReadInt32(); //id?
                                        thisInnerSet.unk7 = reader.ReadInt32(); //count?
                                        thisInnerSet.unk8 = reader.ReadInt32(); //zeros?
                                        thisParamSet.innerSets.Add(thisInnerSet);
                                    }
                                    thisNode.paramsData3.Add(thisParamSet);
                                }
                                break;
                            }
                            case CommandsDataBlock.TRIGGERSEQUENCE_DATA:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                reader.BaseStream.Position = (reader.ReadInt32() * 4);

                                TriggerSequence thisNode = (TriggerSequence)flowgraph.GetEntityByID(new cGUID(reader)); 

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
                        //TODO: We shouldn't hit this, but we do...
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
                        //TODO: We shouldn't hit this, but we do...
                        nodeToApply = new CathodeEntity(paramRefSets[x].id);
                        flowgraph.unknowns.Add(nodeToApply);
                    }
                    for (int y = 0; y < paramRefSets[x].refs.Count; y++)
                        nodeToApply.parameters.Add(new CathodeLoadedParameter(paramRefSets[x].refs[y].paramID, (CathodeParameter)parameters[paramRefSets[x].refs[y].offset].Clone()));
                }

                flowgraphs[i] = flowgraph;
            }
            _flowgraphs = flowgraphs.ToList<CathodeFlowgraph>();

            reader.Close();
            return true;
        }
        #endregion

        #region LOOKUP_TABLES
        private Dictionary<cGUID, CathodeFunctionType> _functionTypeLUT = new Dictionary<cGUID, CathodeFunctionType>();
        private void SetupFunctionTypeLUT()
        {
            if (_functionTypeLUT.Count != 0) return;
            
            foreach (CathodeFunctionType functionType in Enum.GetValues(typeof(CathodeFunctionType)))
                _functionTypeLUT.Add(Utilities.GenerateGUID(functionType.ToString()), functionType);
        }
        private CathodeFunctionType GetFunctionType(byte[] tag)
        {
            return GetFunctionType(new cGUID(tag));
        }
        private CathodeFunctionType GetFunctionType(cGUID tag)
        {
            SetupFunctionTypeLUT();
            return _functionTypeLUT[tag];
        }
        private cGUID GetFunctionTypeGUID(CathodeFunctionType type)
        {
            SetupFunctionTypeLUT();
            return _functionTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }

        private Dictionary<cGUID, CathodeDataType> _dataTypeLUT = new Dictionary<cGUID, CathodeDataType>();
        private void SetupDataTypeLUT()
        {
            if (_dataTypeLUT.Count != 0) return;

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
            SetupDataTypeLUT();
            return _dataTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }

        private Dictionary<cGUID, CathodeResourceReferenceType> _resourceReferenceTypeLUT = new Dictionary<cGUID, CathodeResourceReferenceType>();
        private void SetupResourceEntryTypeLUT()
        {
            if (_resourceReferenceTypeLUT.Count != 0) return;

            foreach (CathodeResourceReferenceType referenceType in Enum.GetValues(typeof(CathodeResourceReferenceType)))
                _resourceReferenceTypeLUT.Add(Utilities.GenerateGUID(referenceType.ToString()), referenceType);
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

        private List<CathodeFlowgraph> _flowgraphs = null;

        private bool _didLoadCorrectly = false;
        public bool Loaded { get { return _didLoadCorrectly; } }
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
    public class TEMP_CAGEAnimationExtraDataHolder1
    {
        public cGUID unk1;
        //public CathodeDataType unk2;
        public cGUID unk2;
        public cGUID unk3;
        public cGUID unk4;
        //public CathodeDataType unk5;
        public cGUID unk5;
        public cGUID unk6;
        public List<cGUID> hierarchy;
    }
    public class TEMP_CAGEAnimationExtraDataHolder2
    {
        public float unk0;
        public float unk1;
        public int unk2;
        public List<TEMP_CAGEAnimationExtraDataHolder2_1> innerSets = new List<TEMP_CAGEAnimationExtraDataHolder2_1>();
    }
    public class TEMP_CAGEAnimationExtraDataHolder2_1
    {
        public int unk3;
        public float unk4;
        public float unk5;
        public float unk6;
        public float unk7;
        public float unk8;
        public float unk9;
        public float unk10;
    }
    public class TEMP_CAGEAnimationExtraDataHolder3
    {
        public float unk0;
        public float unk1;
        public int unk2;
        public List<TEMP_CAGEAnimationExtraDataHolder3_1> innerSets = new List<TEMP_CAGEAnimationExtraDataHolder3_1>();
    }
    public class TEMP_CAGEAnimationExtraDataHolder3_1
    {
        public int unk3;
        public float unk4;
        public int unk5;
        public int unk6;
        public int unk7;
        public int unk8;
    }
    public class TEMP_TriggerSequenceExtraDataHolder1
    {
        public float timing;
        public List<cGUID> hierarchy;
    }
    public class TEMP_TriggerSequenceExtraDataHolder2
    {
        public cGUID EventID; //Assumed
        public cGUID StartedID; //Assumed
        public cGUID FinishedID;
    }
}
