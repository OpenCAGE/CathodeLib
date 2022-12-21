//#define DO_PRETTY_COMPOSITES

using CathodeLib;
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
        public Action OnLoaded;
        public Action OnSaved;

        /* Load and parse the COMMANDS.PAK */
        public CommandsPAK(string pathToPak)
        {
            _path = pathToPak;
            _didLoadCorrectly = Load(_path);
        }

        #region ACCESSORS
        /* Return a list of filenames for composites in the CommandsPAK archive */
        public string[] GetCompositeNames()
        {
            string[] toReturn = new string[_composites.Count];
            for (int i = 0; i < _composites.Count; i++) toReturn[i] = _composites[i].name;
            return toReturn;
        }

        /* Find the a script entry object by name */
        public int GetFileIndex(string FileName)
        {
            for (int i = 0; i < _composites.Count; i++) if (_composites[i].name == FileName || _composites[i].name == FileName.Replace('/', '\\')) return i;
            return -1;
        }

        /* Get an individual composite */
        public CathodeComposite GetComposite(ShortGuid id)
        {
            if (id.val == null) return null;
            return _composites.FirstOrDefault(o => o.shortGUID == id);
        }
        public CathodeComposite GetCompositeByIndex(int index)
        {
            return (index >= _composites.Count || index < 0) ? null : _composites[index];
        }

        /* Get all composites */
        public List<CathodeComposite> Composites { get { return _composites; } }

        /* Get entry point composite objects */
        public CathodeComposite[] EntryPoints
        {
            get
            {
                if (_entryPointObjects != null) return _entryPointObjects;
                _entryPointObjects = new CathodeComposite[_entryPoints.compositeIDs.Length];
                for (int i = 0; i < _entryPoints.compositeIDs.Length; i++) _entryPointObjects[i] = GetComposite(_entryPoints.compositeIDs[i]);
                return _entryPointObjects;
            }
        }

        /* Set the root composite for this COMMANDS.PAK (the root of the level - GLOBAL and PAUSEMENU are also instanced) */
        public void SetRootComposite(ShortGuid id)
        {
            _entryPoints.compositeIDs[0] = id;
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

            //Write placeholder info for parameter/composite offsets
            int offsetToRewrite = (int)writer.BaseStream.Position;
            writer.Write(0); 
            writer.Write(0);
            writer.Write(0); 
            writer.Write(0);

            //Work out unique parameters to write
            List<CathodeParameter> parameters = new List<CathodeParameter>();
            for (int i = 0; i < _composites.Count; i++)
            {
                List<CathodeEntity> fgEntities = _composites[i].GetEntities();
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
                Utilities.Write<ShortGuid>(writer, CommandsUtils.GetDataTypeGUID(parameters[i].dataType));
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
                        writer.Write(ShortGuidUtils.Generate(str).val);
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
                    case CathodeDataType.RESOURCE:
                        Utilities.Write<ShortGuid>(writer, ((CathodeResource)parameters[i]).resourceID);
                        break;
                    case CathodeDataType.DIRECTION:
                        Vector3 dir = ((CathodeVector3)parameters[i]).value;
                        writer.Write(dir.x); writer.Write(dir.y); writer.Write(dir.z);
                        break;
                    case CathodeDataType.ENUM:
                        Utilities.Write<ShortGuid>(writer, ((CathodeEnum)parameters[i]).enumID);
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

            //Write out composites & track offsets
            int[] compositeOffsets = new int[_composites.Count];
            for (int i = 0; i < _composites.Count; i++)
            {
                int scriptStartPos = (int)writer.BaseStream.Position / 4;

                Utilities.Write<ShortGuid>(writer, ShortGuidUtils.Generate(_composites[i].name));
                for (int x = 0; x < _composites[i].name.Length; x++) writer.Write(_composites[i].name[x]);
                writer.Write((char)0x00);
                Utilities.Align(writer, 4);

                //Work out what we want to write
                List<CathodeEntity> ents = _composites[i].GetEntities();
                List<CathodeEntity> entitiesWithLinks = new List<CathodeEntity>(ents.FindAll(o => o.childLinks.Count != 0));
                List<CathodeEntity> entitiesWithParams = new List<CathodeEntity>(ents.FindAll(o => o.parameters.Count != 0));
                //TODO: find a nicer way to sort into entity class types
                List<CAGEAnimation> cageAnimationEntities = new List<CAGEAnimation>();
                List<TriggerSequence> triggerSequenceEntities = new List<TriggerSequence>();
                ShortGuid cageAnimationGUID = CommandsUtils.GetFunctionTypeGUID(CathodeFunctionType.CAGEAnimation);
                ShortGuid triggerSequenceGUID = CommandsUtils.GetFunctionTypeGUID(CathodeFunctionType.TriggerSequence);
                for (int x = 0; x < _composites[i].functions.Count; x++)
                {
                    if (_composites[i].functions[x].function == cageAnimationGUID)
                    {
                        CAGEAnimation thisEntity = (CAGEAnimation)_composites[i].functions[x];
                        if (thisEntity.keyframeHeaders.Count == 0 && thisEntity.keyframeData.Count == 0 && thisEntity.paramsData3.Count == 0) continue;
                        cageAnimationEntities.Add(thisEntity);
                    }
                    else if (_composites[i].functions[x].function == triggerSequenceGUID)
                    {
                        TriggerSequence thisEntity = (TriggerSequence)_composites[i].functions[x];
                        if (thisEntity.triggers.Count == 0 && thisEntity.events.Count == 0) continue;
                        triggerSequenceEntities.Add(thisEntity);
                    }
                }

                //Reconstruct resources
                List<CathodeResourceReference> resourceReferences = new List<CathodeResourceReference>();
                ShortGuid resourceParamID = ShortGuidUtils.Generate("resource");
                for (int x = 0; x < ents.Count; x++)
                {
                    for (int y = 0; y < ents[x].resources.Count; y++)
                        if (!resourceReferences.Contains(ents[x].resources[y]))
                            resourceReferences.Add(ents[x].resources[y]);

                    CathodeLoadedParameter resParam = ents[x].parameters.FirstOrDefault(o => o.shortGUID == resourceParamID);
                    if (resParam == null) continue;
                    List<CathodeResourceReference> resParamRef = ((CathodeResource)resParam.content).value;
                    for (int y = 0; y < resParamRef.Count; y++)
                        if (!resourceReferences.Contains(resParamRef[y]))
                            resourceReferences.Add(resParamRef[y]);
                }
                resourceReferences.AddRange(_composites[i].resources);

                //Sort
                entitiesWithLinks = entitiesWithLinks.OrderBy(o => o.shortGUID.ToUInt32()).ToList();
                entitiesWithParams = entitiesWithParams.OrderBy(o => o.shortGUID.ToUInt32()).ToList();
                List<OverrideEntity> reshuffledChecksums = _composites[i].overrides.OrderBy(o => o.checksum.ToUInt32()).ToList();
                _composites[i].SortEntities();

                //Write data
                OffsetPair[] scriptPointerOffsetInfo = new OffsetPair[(int)CommandsDataBlock.NUMBER_OF_SCRIPT_BLOCKS];
                for (int x = 0; x < (int)CommandsDataBlock.NUMBER_OF_SCRIPT_BLOCKS; x++)
                {
                    switch ((CommandsDataBlock)x)
                    {
                        case CommandsDataBlock.COMPOSITE_HEADER:
                        {
                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, 2);
                            Utilities.Write<ShortGuid>(writer, _composites[i].shortGUID);
                            writer.Write(0);
                            break;
                        }
                        case CommandsDataBlock.ENTITY_CONNECTIONS:
                        {
                            List<OffsetPair> offsetPairs = new List<OffsetPair>();
                            foreach (CathodeEntity entityWithLink in entitiesWithLinks)
                            {
                                offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, entityWithLink.childLinks.Count));
                                Utilities.Write<CathodeEntityLink>(writer, entityWithLink.childLinks);
                            }

                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, entitiesWithLinks.Count);
                            for (int p = 0; p < entitiesWithLinks.Count; p++)
                            {
                                writer.Write(entitiesWithLinks[p].shortGUID.val);
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
                                    Utilities.Write<ShortGuid>(writer, entityWithParam.parameters[y].shortGUID);
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
                                writer.Write(entitiesWithParams[p].shortGUID.val);
                                writer.Write(offsetPairs[p].GlobalOffset / 4);
                                writer.Write(offsetPairs[p].EntryCount);
                            }
                            break;
                        }
                        case CommandsDataBlock.ENTITY_OVERRIDES:
                        {
                            List<OffsetPair> offsetPairs = new List<OffsetPair>();
                            for (int p = 0; p < _composites[i].overrides.Count; p++)
                            {
                                offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, _composites[i].overrides[p].hierarchy.Count));
                                Utilities.Write<ShortGuid>(writer, _composites[i].overrides[p].hierarchy);
                            }

                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, _composites[i].overrides.Count);
                            for (int p = 0; p < _composites[i].overrides.Count; p++)
                            {
                                writer.Write(_composites[i].overrides[p].shortGUID.val);
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
                                writer.Write(reshuffledChecksums[p].shortGUID.val);
                                writer.Write(reshuffledChecksums[p].checksum.val);
                            }
                            break;
                        }
                        case CommandsDataBlock.COMPOSITE_EXPOSED_PARAMETERS:
                        {
                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, _composites[i].datatypes.Count);
                            for (int p = 0; p < _composites[i].datatypes.Count; p++)
                            {
                                writer.Write(_composites[i].datatypes[p].shortGUID.val);
                                writer.Write(CommandsUtils.GetDataTypeGUID(_composites[i].datatypes[p].type).val);
                                writer.Write(_composites[i].datatypes[p].parameter.val);
                            }
                            break;
                        }
                        case CommandsDataBlock.ENTITY_PROXIES:
                        {
                            List<OffsetPair> offsetPairs = new List<OffsetPair>();
                            for (int p = 0; p < _composites[i].proxies.Count; p++)
                            {
                                offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, _composites[i].proxies[p].hierarchy.Count));
                                Utilities.Write<ShortGuid>(writer, _composites[i].proxies[p].hierarchy);
                            }

                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, offsetPairs.Count);
                            for (int p = 0; p < _composites[i].proxies.Count; p++)
                            {
                                writer.Write(_composites[i].proxies[p].shortGUID.val);
                                writer.Write(offsetPairs[p].GlobalOffset / 4);
                                writer.Write(offsetPairs[p].EntryCount);
                                writer.Write(_composites[i].proxies[p].shortGUID.val);
                                writer.Write(_composites[i].proxies[p].extraId.val);
                            }
                            break;
                        }
                        case CommandsDataBlock.ENTITY_FUNCTIONS:
                        {
                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, _composites[i].functions.Count);
                            for (int p = 0; p < _composites[i].functions.Count; p++)
                            {
                                writer.Write(_composites[i].functions[p].shortGUID.val);
                                writer.Write(_composites[i].functions[p].function.val);
                            }
                            break;
                        }
                        case CommandsDataBlock.RESOURCE_REFERENCES:
                        {
                            scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, resourceReferences.Count);
                            for (int p = 0; p < resourceReferences.Count; p++)
                            {
                                writer.Write(resourceReferences[p].position.x);
                                writer.Write(resourceReferences[p].position.y);
                                writer.Write(resourceReferences[p].position.z);
                                writer.Write(resourceReferences[p].rotation.x);
                                writer.Write(resourceReferences[p].rotation.y);
                                writer.Write(resourceReferences[p].rotation.z);
                                writer.Write(resourceReferences[p].resourceID.val); //Sometimes this is the entity ID that uses the resource, other times it's the "resource" parameter ID link
                                writer.Write(CommandsUtils.GetResourceEntryTypeGUID(resourceReferences[p].entryType).val);
                                switch (resourceReferences[p].entryType)
                                {
                                    case CathodeResourceReferenceType.RENDERABLE_INSTANCE:
                                        writer.Write(resourceReferences[p].startIndex);
                                        writer.Write(resourceReferences[p].count);
                                        break;
                                    case CathodeResourceReferenceType.COLLISION_MAPPING:
                                        writer.Write(resourceReferences[p].startIndex);
                                        writer.Write(resourceReferences[p].entityID.val);
                                        break;
                                    case CathodeResourceReferenceType.ANIMATED_MODEL:
                                    case CathodeResourceReferenceType.DYNAMIC_PHYSICS_SYSTEM:
                                        writer.Write(resourceReferences[p].startIndex);
                                        writer.Write(-1);
                                        break;
                                    case CathodeResourceReferenceType.EXCLUSIVE_MASTER_STATE_RESOURCE:
                                    case CathodeResourceReferenceType.NAV_MESH_BARRIER_RESOURCE:
                                    case CathodeResourceReferenceType.TRAVERSAL_SEGMENT:
                                        writer.Write(-1);
                                        writer.Write(-1);
                                        break;
                                }
                            }
                            break;
                        }
                        case CommandsDataBlock.TRIGGERSEQUENCE_DATA: //Actually CAGEANIMATION_DATA, but indexes are flipped
                        {
                            List<int> globalOffsets = new List<int>();
                            for (int p = 0; p < cageAnimationEntities.Count; p++)
                            {
                                List<int> hierarchyOffsets = new List<int>();
                                for (int pp = 0; pp < cageAnimationEntities[p].keyframeHeaders.Count; pp++)
                                {
                                    hierarchyOffsets.Add((int)writer.BaseStream.Position);
                                    Utilities.Write<ShortGuid>(writer, cageAnimationEntities[p].keyframeHeaders[pp].connectedEntity);
                                }

                                int paramData1Offset = (int)writer.BaseStream.Position;
                                for (int pp = 0; pp < cageAnimationEntities[p].keyframeHeaders.Count; pp++)
                                {
                                    Utilities.Write(writer, cageAnimationEntities[p].keyframeHeaders[pp].ID);
                                    Utilities.Write(writer, CommandsUtils.GetDataTypeGUID(cageAnimationEntities[p].keyframeHeaders[pp].unk2));
                                    Utilities.Write(writer, cageAnimationEntities[p].keyframeHeaders[pp].keyframeDataID);
                                    Utilities.Write(writer, cageAnimationEntities[p].keyframeHeaders[pp].parameterID);
                                    Utilities.Write(writer, CommandsUtils.GetDataTypeGUID(cageAnimationEntities[p].keyframeHeaders[pp].parameterDataType));
                                    Utilities.Write(writer, cageAnimationEntities[p].keyframeHeaders[pp].parameterSubID);
                                    writer.Write(hierarchyOffsets[pp] / 4);
                                    writer.Write(cageAnimationEntities[p].keyframeHeaders[pp].connectedEntity.Count);
                                }

                                List<int> internalOffsets1 = new List<int>();
                                List<int> internalOffsets2 = new List<int>();
                                for (int pp = 0; pp < cageAnimationEntities[p].keyframeData.Count; pp++)
                                {
                                    int toPointTo = (int)writer.BaseStream.Position;
                                    for (int ppp = 0; ppp < cageAnimationEntities[p].keyframeData[pp].keyframes.Count; ppp++)
                                    {
                                        writer.Write(cageAnimationEntities[p].keyframeData[pp].keyframes[ppp].unk1);
                                        writer.Write(cageAnimationEntities[p].keyframeData[pp].keyframes[ppp].secondsSinceStart);
                                        writer.Write(cageAnimationEntities[p].keyframeData[pp].keyframes[ppp].secondsSinceStartValidation);
                                        writer.Write(cageAnimationEntities[p].keyframeData[pp].keyframes[ppp].paramValue);
                                        writer.Write(cageAnimationEntities[p].keyframeData[pp].keyframes[ppp].unk2);
                                        writer.Write(cageAnimationEntities[p].keyframeData[pp].keyframes[ppp].unk3);
                                        writer.Write(cageAnimationEntities[p].keyframeData[pp].keyframes[ppp].unk4);
                                        writer.Write(cageAnimationEntities[p].keyframeData[pp].keyframes[ppp].unk5);
                                    }

                                    internalOffsets1.Add(((int)writer.BaseStream.Position) / 4);

                                    writer.Write(cageAnimationEntities[p].keyframeData[pp].minSeconds);
                                    writer.Write(cageAnimationEntities[p].keyframeData[pp].maxSeconds);
                                    Utilities.Write(writer, cageAnimationEntities[p].keyframeData[pp].ID);

                                    writer.Write(toPointTo / 4);
                                    writer.Write(cageAnimationEntities[p].keyframeData[pp].keyframes.Count);
                                }

                                int paramData2Offset = (int)writer.BaseStream.Position;
                                Utilities.Write<int>(writer, internalOffsets1);

                                internalOffsets2 = new List<int>();
                                for (int pp = 0; pp < cageAnimationEntities[p].paramsData3.Count; pp++)
                                {
                                    int toPointTo = (int)writer.BaseStream.Position;
                                    for (int ppp = 0; ppp < cageAnimationEntities[p].paramsData3[pp].keyframes.Count; ppp++)
                                    {
                                        writer.Write(cageAnimationEntities[p].paramsData3[pp].keyframes[ppp].unk1);
                                        writer.Write(cageAnimationEntities[p].paramsData3[pp].keyframes[ppp].SecondsSinceStart);
                                        writer.Write(cageAnimationEntities[p].paramsData3[pp].keyframes[ppp].unk2);
                                        writer.Write(cageAnimationEntities[p].paramsData3[pp].keyframes[ppp].unk3);
                                        writer.Write(cageAnimationEntities[p].paramsData3[pp].keyframes[ppp].unk4);
                                        writer.Write(cageAnimationEntities[p].paramsData3[pp].keyframes[ppp].unk5);
                                    }

                                    internalOffsets2.Add(((int)writer.BaseStream.Position) / 4);

                                    writer.Write(cageAnimationEntities[p].paramsData3[pp].minSeconds);
                                    writer.Write(cageAnimationEntities[p].paramsData3[pp].maxSeconds);
                                    Utilities.Write(writer, cageAnimationEntities[p].paramsData3[pp].ID);

                                    writer.Write(toPointTo / 4);
                                    writer.Write(cageAnimationEntities[p].paramsData3[pp].keyframes.Count);
                                }

                                int paramData3Offset = (int)writer.BaseStream.Position;
                                Utilities.Write<int>(writer, internalOffsets2);

                                globalOffsets.Add((int)writer.BaseStream.Position);
                                writer.Write(cageAnimationEntities[p].shortGUID.val);
                                writer.Write(paramData1Offset / 4);
                                writer.Write(cageAnimationEntities[p].keyframeHeaders.Count);
                                writer.Write(paramData2Offset / 4);
                                writer.Write(cageAnimationEntities[p].keyframeData.Count);
                                writer.Write(paramData3Offset / 4);
                                writer.Write(cageAnimationEntities[p].paramsData3.Count);
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
                            for (int p = 0; p < triggerSequenceEntities.Count; p++)
                            {
                                List<int> hierarchyOffsets = new List<int>();
                                for (int pp = 0; pp < triggerSequenceEntities[p].triggers.Count; pp++)
                                {
                                    hierarchyOffsets.Add((int)writer.BaseStream.Position);
                                    Utilities.Write<ShortGuid>(writer, triggerSequenceEntities[p].triggers[pp].hierarchy);
                                }

                                int triggerOffset = (int)writer.BaseStream.Position;
                                for (int pp = 0; pp < triggerSequenceEntities[p].triggers.Count; pp++)
                                {
                                    writer.Write(hierarchyOffsets[pp] / 4);
                                    writer.Write(triggerSequenceEntities[p].triggers[pp].hierarchy.Count);
                                    writer.Write(triggerSequenceEntities[p].triggers[pp].timing);
                                }

                                int eventOffset = (int)writer.BaseStream.Position;
                                for (int pp = 0; pp < triggerSequenceEntities[p].events.Count; pp++)
                                {
                                    writer.Write(triggerSequenceEntities[p].events[pp].EventID.val);
                                    writer.Write(triggerSequenceEntities[p].events[pp].StartedID.val);
                                    writer.Write(triggerSequenceEntities[p].events[pp].FinishedID.val);
                                }

                                globalOffsets.Add((int)writer.BaseStream.Position);
                                writer.Write(triggerSequenceEntities[p].shortGUID.val);
                                writer.Write(triggerOffset / 4);
                                writer.Write(triggerSequenceEntities[p].triggers.Count);
                                writer.Write(eventOffset / 4);
                                writer.Write(triggerSequenceEntities[p].events.Count);
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
                            scriptPointerOffsetInfo[x] = new OffsetPair(_composites[i].unknownPair.GlobalOffset * 4, _composites[i].unknownPair.EntryCount);
                            break;
                        }
                    }
                }

                //Write pointers to the pointers of the content
                compositeOffsets[i] = (int)writer.BaseStream.Position / 4;
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
                    if (x == 0) Utilities.Write<ShortGuid>(writer, _composites[i].shortGUID);
                }
            }

            //Write out parameter offsets
            int parameterOffsetPos = (int)writer.BaseStream.Position;
            Utilities.Write<int>(writer, parameterOffsets);

            //Write out composite offsets
            int compositeOffsetPos = (int)writer.BaseStream.Position;
            Utilities.Write<int>(writer, compositeOffsets);

            //Rewrite header info with correct offsets 
            writer.BaseStream.Position = offsetToRewrite;
            writer.Write(parameterOffsetPos / 4);
            writer.Write(parameters.Count);
            writer.Write(compositeOffsetPos / 4);
            writer.Write(_composites.Count);

            writer.Close();
            OnSaved?.Invoke();
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

        /* Read the parameter and composite offsets & get entry points */
        private bool Load(string path)
        {
            if (!File.Exists(path)) return false;
            BinaryReader reader = new BinaryReader(File.OpenRead(path));

            //Read entry points
            _entryPoints = Utilities.Consume<CommandsEntryPoints>(reader);

            //Read parameter/composite counts
            int parameter_offset_pos = reader.ReadInt32() * 4;
            int parameter_count = reader.ReadInt32();
            int composite_offset_pos = reader.ReadInt32() * 4;
            int composite_count = reader.ReadInt32();

            //Read parameter/composite offsets
            reader.BaseStream.Position = parameter_offset_pos;
            int[] parameterOffsets = Utilities.ConsumeArray<int>(reader, parameter_count);
            reader.BaseStream.Position = composite_offset_pos;
            int[] compositeOffsets = Utilities.ConsumeArray<int>(reader, composite_count);

            //Read all parameters from the PAK
            Dictionary<int, CathodeParameter> parameters = new Dictionary<int, CathodeParameter>(parameter_count);
            for (int i = 0; i < parameter_count; i++)
            {
                reader.BaseStream.Position = parameterOffsets[i] * 4; 
                CathodeParameter this_parameter = new CathodeParameter(CommandsUtils.GetDataType(new ShortGuid(reader)));
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
                    case CathodeDataType.RESOURCE:
                        this_parameter = new CathodeResource();
                        ((CathodeResource)this_parameter).resourceID = new ShortGuid(reader);
                        break;
                    case CathodeDataType.DIRECTION:
                        this_parameter = new CathodeVector3();
                        ((CathodeVector3)this_parameter).value = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        break;
                    case CathodeDataType.ENUM:
                        this_parameter = new CathodeEnum();
                        ((CathodeEnum)this_parameter).enumID = new ShortGuid(reader);
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

            //Read all composites from the PAK
            CathodeComposite[] composites = new CathodeComposite[composite_count];
            for (int i = 0; i < composite_count; i++)
            {
                reader.BaseStream.Position = compositeOffsets[i] * 4;
                reader.BaseStream.Position += 4; //Skip 0x00,0x00,0x00,0x00
                CathodeComposite composite = new CathodeComposite();

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
                    if (x == 0) composite.shortGUID = new ShortGuid(reader);
                }
                composite.unknownPair = offsetPairs[12];

                //Read script ID and string name
                reader.BaseStream.Position = (scriptStartOffset * 4) + 4;
                composite.name = Utilities.ReadString(reader);
#if DO_PRETTY_COMPOSITES
                string prettyPath = CompositePathDB.GetPrettyPathForComposite(composite.shortGUID);
                if (prettyPath != "") composite.name = prettyPath;
#endif
                Utilities.Align(reader, 4);

                //Pull data from those offsets
                List<CommandsEntityLinks> entityLinks = new List<CommandsEntityLinks>();
                List<CommandsParamRefSet> paramRefSets = new List<CommandsParamRefSet>();
                List<CathodeResourceReference> resourceRefs = new List<CathodeResourceReference>();
                Dictionary<ShortGuid, ShortGuid> overrideChecksums = new Dictionary<ShortGuid, ShortGuid>();
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
                                entityLinks.Add(new CommandsEntityLinks(new ShortGuid(reader)));
                                int NumberOfParams = JumpToOffset(ref reader);
                                entityLinks[entityLinks.Count - 1].childLinks.AddRange(Utilities.ConsumeArray<CathodeEntityLink>(reader, NumberOfParams));
                                break;
                            }
                            case CommandsDataBlock.ENTITY_PARAMETERS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                paramRefSets.Add(new CommandsParamRefSet(new ShortGuid(reader)));
                                int NumberOfParams = JumpToOffset(ref reader);
                                paramRefSets[paramRefSets.Count - 1].refs.AddRange(Utilities.ConsumeArray<CathodeParameterReference>(reader, NumberOfParams));
                                break;
                            }
                            case CommandsDataBlock.ENTITY_OVERRIDES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                OverrideEntity overrider = new OverrideEntity(new ShortGuid(reader));
                                int NumberOfParams = JumpToOffset(ref reader);
                                overrider.hierarchy.AddRange(Utilities.ConsumeArray<ShortGuid>(reader, NumberOfParams));
                                composite.overrides.Add(overrider);
                                break;
                            }
                            case CommandsDataBlock.ENTITY_OVERRIDES_CHECKSUM:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                overrideChecksums.Add(new ShortGuid(reader), new ShortGuid(reader));
                                break;
                                }
                            //TODO: Really, I think these should be treated as parameters on the composite class as they are the pins we use for composite instances.
                            //      Need to look into this more and see if any of these entities actually contain much data other than links into the composite itself.
                            case CommandsDataBlock.COMPOSITE_EXPOSED_PARAMETERS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                DatatypeEntity dtEntity = new DatatypeEntity(new ShortGuid(reader));
                                dtEntity.type = CommandsUtils.GetDataType(new ShortGuid(reader));
                                dtEntity.parameter = new ShortGuid(reader);
                                composite.datatypes.Add(dtEntity);
                                break;
                            }
                            case CommandsDataBlock.ENTITY_PROXIES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 20);
                                ProxyEntity thisProxy = new ProxyEntity(new ShortGuid(reader));
                                int resetPos = (int)reader.BaseStream.Position + 8; //TODO: This is a HACK - I need to rework JumpToOffset to make a temp stream
                                int NumberOfParams = JumpToOffset(ref reader);
                                thisProxy.hierarchy.AddRange(Utilities.ConsumeArray<ShortGuid>(reader, NumberOfParams)); //Last is always 0x00, 0x00, 0x00, 0x00
                                reader.BaseStream.Position = resetPos;
                                ShortGuid idCheck = new ShortGuid(reader);
                                if (idCheck != thisProxy.shortGUID) throw new Exception("Proxy ID mismatch!");
                                thisProxy.extraId = new ShortGuid(reader);
                                composite.proxies.Add(thisProxy);
                                break;
                            }
                            case CommandsDataBlock.ENTITY_FUNCTIONS:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                ShortGuid entityID = new ShortGuid(reader);
                                ShortGuid functionID = new ShortGuid(reader);
                                if (CommandsUtils.FunctionTypeExists(functionID))
                                {
                                    //This entity executes a hard-coded CATHODE function
                                    CathodeFunctionType functionType = CommandsUtils.GetFunctionType(functionID);
                                    switch (functionType)
                                    {
                                        case CathodeFunctionType.CAGEAnimation:
                                            CAGEAnimation cageAnimation = new CAGEAnimation(entityID);
                                            composite.functions.Add(cageAnimation);
                                            break;
                                        case CathodeFunctionType.TriggerSequence:
                                            TriggerSequence triggerSequence = new TriggerSequence(entityID);
                                            composite.functions.Add(triggerSequence);
                                            break;
                                        default:
                                            FunctionEntity funcEntity = new FunctionEntity(entityID);
                                            funcEntity.function = functionID;
                                            composite.functions.Add(funcEntity);
                                            break;
                                    }
                                }
                                else
                                {
                                    //This entity is an instance of a composite entity collection
                                    FunctionEntity funcEntity = new FunctionEntity(entityID);
                                    funcEntity.function = functionID;
                                    composite.functions.Add(funcEntity);
                                }
                                break;
                            }
                            case CommandsDataBlock.RESOURCE_REFERENCES:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 40);

                                CathodeResourceReference resource = new CathodeResourceReference();
                                resource.position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                resource.rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()); 
                                resource.resourceID = new ShortGuid(reader);
                                resource.entryType = CommandsUtils.GetResourceEntryType(reader.ReadBytes(4));
                                switch (resource.entryType)
                                {
                                    case CathodeResourceReferenceType.RENDERABLE_INSTANCE:
                                        resource.startIndex = reader.ReadInt32(); //REDS.BIN entry index
                                        resource.count = reader.ReadInt32(); //REDS.BIN entry count
                                        break;
                                    case CathodeResourceReferenceType.COLLISION_MAPPING:
                                        resource.startIndex = reader.ReadInt32(); //COLLISION.MAP entry index?
                                        resource.entityID = new ShortGuid(reader); //ID which maps to the entity using the resource (?) - check GetFriendlyName
                                        break;
                                    case CathodeResourceReferenceType.ANIMATED_MODEL:
                                    case CathodeResourceReferenceType.DYNAMIC_PHYSICS_SYSTEM:
                                        resource.startIndex = reader.ReadInt32(); //PHYSICS.MAP entry index?
                                        reader.BaseStream.Position += 4;
                                        break;
                                    case CathodeResourceReferenceType.EXCLUSIVE_MASTER_STATE_RESOURCE:
                                    case CathodeResourceReferenceType.NAV_MESH_BARRIER_RESOURCE:
                                    case CathodeResourceReferenceType.TRAVERSAL_SEGMENT:
                                        reader.BaseStream.Position += 8;
                                        break;
                                }
                                resourceRefs.Add(resource);
                                break;
                            }
                            case CommandsDataBlock.CAGEANIMATION_DATA:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                reader.BaseStream.Position = (reader.ReadInt32() * 4);

                                CathodeEntity thisEntity = composite.GetEntityByID(new ShortGuid(reader));
                                if (thisEntity.variant == EntityVariant.PROXY)
                                {
                                    break; // We don't handle this just yet... need to resolve the proxy.
                                }
                                CAGEAnimation animEntity = (CAGEAnimation)thisEntity;

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
                                    thisHeader.ID = new ShortGuid(reader);//ID
                                    thisHeader.unk2 = CommandsUtils.GetDataType(new ShortGuid(reader)); //Datatype, seems to usually be NO_TYPE
                                    thisHeader.keyframeDataID = new ShortGuid(reader); 
                                    thisHeader.parameterID = new ShortGuid(reader); 
                                    thisHeader.parameterDataType = CommandsUtils.GetDataType(new ShortGuid(reader)); 
                                    thisHeader.parameterSubID = new ShortGuid(reader); 

                                    int hierarchyCount = JumpToOffset(ref reader);
                                    thisHeader.connectedEntity = Utilities.ConsumeArray<ShortGuid>(reader, hierarchyCount).ToList<ShortGuid>(); 
                                    animEntity.keyframeHeaders.Add(thisHeader);
                                }
                                
                                reader.BaseStream.Position = keyframeDataOffset;
                                int[] newOffset = Utilities.ConsumeArray<int>(reader, numberOfKeyframeDataEntries);
                                for (int z = 0; z < numberOfKeyframeDataEntries; z++)
                                {
                                    reader.BaseStream.Position = newOffset[z] * 4;

                                    CathodeParameterKeyframe thisParamKey = new CathodeParameterKeyframe();
                                    thisParamKey.minSeconds = reader.ReadSingle();
                                    thisParamKey.maxSeconds = reader.ReadSingle(); //max seconds for keyframe list
                                    thisParamKey.ID = new ShortGuid(reader); //this is perhaps an entity id

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
                                    animEntity.keyframeData.Add(thisParamKey);
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
                                    thisParamSet.ID = new ShortGuid(reader); //this is perhaps an entity id

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
                                    animEntity.paramsData3.Add(thisParamSet);
                                }
                                break;
                            }
                            case CommandsDataBlock.TRIGGERSEQUENCE_DATA:
                            {
                                reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                reader.BaseStream.Position = (reader.ReadInt32() * 4);

                                CathodeEntity thisEntity = composite.GetEntityByID(new ShortGuid(reader));
                                if (thisEntity.variant == EntityVariant.PROXY)
                                {
                                    break; // We don't handle this just yet... need to resolve the proxy.
                                }
                                TriggerSequence trigEntity = (TriggerSequence)thisEntity;

                                int triggersOffset = reader.ReadInt32() * 4;
                                int triggersCount = reader.ReadInt32();
                                int eventsOffset = reader.ReadInt32() * 4;
                                int eventsCount = reader.ReadInt32();

                                for (int z = 0; z < triggersCount; z++)
                                {
                                    reader.BaseStream.Position = triggersOffset + (z * 12);
                                    int hierarchyOffset = reader.ReadInt32() * 4;
                                    int hierarchyCount = reader.ReadInt32();

                                    CathodeTriggerSequenceTrigger thisTrigger = new CathodeTriggerSequenceTrigger();
                                    thisTrigger.timing = reader.ReadSingle();
                                    reader.BaseStream.Position = hierarchyOffset;
                                    thisTrigger.hierarchy = Utilities.ConsumeArray<ShortGuid>(reader, hierarchyCount).ToList<ShortGuid>();
                                    trigEntity.triggers.Add(thisTrigger);
                                }

                                for (int z = 0; z < eventsCount; z++)
                                {
                                    reader.BaseStream.Position = eventsOffset + (z * 12);

                                    CathodeTriggerSequenceEvent thisEvent = new CathodeTriggerSequenceEvent();
                                    thisEvent.EventID = new ShortGuid(reader);
                                    thisEvent.StartedID = new ShortGuid(reader);
                                    thisEvent.FinishedID = new ShortGuid(reader);
                                    trigEntity.events.Add(thisEvent);
                                }
                                break;
                            }
                        }
                    }
                }

                //Apply checksums to overrides
                for (int x = 0; x < composite.overrides.Count; x++)
                    composite.overrides[x].checksum = overrideChecksums[composite.overrides[x].shortGUID];

                //Apply connections between entities
                for (int x = 0; x < entityLinks.Count; x++)
                {
                    CathodeEntity entToApply = composite.GetEntityByID(entityLinks[x].parentID);
                    if (entToApply == null)
                    {
                        //TODO: We shouldn't hit this, but we do... is this perhaps an ID from another composite, similar to proxies?
                        entToApply = new CathodeEntity(entityLinks[x].parentID);
                        composite.unknowns.Add(entToApply);
                    }
                    entToApply.childLinks.AddRange(entityLinks[x].childLinks);
                }

                //Apply parameters to entities
                for (int x = 0; x < paramRefSets.Count; x++)
                {
                    CathodeEntity entToApply = composite.GetEntityByID(paramRefSets[x].id);
                    if (entToApply == null)
                    {
                        //TODO: We shouldn't hit this, but we do... is this perhaps an ID from another composite, similar to proxies?
                        entToApply = new CathodeEntity(paramRefSets[x].id);
                        composite.unknowns.Add(entToApply);
                    }
                    for (int y = 0; y < paramRefSets[x].refs.Count; y++)
                    {
                        entToApply.parameters.Add(new CathodeLoadedParameter(paramRefSets[x].refs[y].paramID, (CathodeParameter)parameters[paramRefSets[x].refs[y].offset].Clone()));
                    }
                }

                //Remap resources (TODO: This can be optimised)
                List<CathodeEntity> ents = composite.GetEntities();
                ShortGuid resParamID = ShortGuidUtils.Generate("resource");
                //Check to see if this resource applies to a PARAMETER
                for (int z = 0; z < ents.Count; z++)
                {
                    for (int y = 0; y < ents[z].parameters.Count; y++)
                    {
                        if (ents[z].parameters[y].shortGUID != resParamID) continue;

                        CathodeResource resourceParam = (CathodeResource)ents[z].parameters[y].content;
                        resourceParam.value.AddRange(resourceRefs.Where(o => o.resourceID == resourceParam.resourceID));
                        resourceRefs.RemoveAll(o => o.resourceID == resourceParam.resourceID);
                    }
                }
                //Check to see if this resource applies to an ENTITY
                for (int z = 0; z < ents.Count; z++)
                {
                    ents[z].resources.AddRange(resourceRefs.Where(o => o.resourceID == ents[z].shortGUID));
                    resourceRefs.RemoveAll(o => o.resourceID == ents[z].shortGUID);

                    // Note, only these types of entities (always functions) seem to have their own non-parameterised resources:
                    // - ParticleEmitterReference
                    // - RibbonEmitterReference
                    // - TRAV_1ShotSpline
                    // - LightReference
                    // - SurfaceEffectSphere
                    // - FogSphere
                    // - NavMeshBarrier
                    // - FogBox
                    // - SoundBarrier
                    // - SurfaceEffectBox
                    // - SimpleWater
                    // - SimpleRefraction
                    // - CollisionBarrier
                    // ... we should probably auto-generate these resources when adding new entities of these types.
                }
                //If it applied to none of the above, apply it to the COMPOSITE
                composite.resources.AddRange(resourceRefs);
                resourceRefs.Clear();

                composites[i] = composite;
            }
            _composites = composites.ToList<CathodeComposite>();

            reader.Close();
            OnLoaded?.Invoke();
            return true;
        }
        #endregion

        private string _path = "";

        private CommandsEntryPoints _entryPoints;
        private CathodeComposite[] _entryPointObjects = null;

        private List<CathodeComposite> _composites = null;

        private bool _didLoadCorrectly = false;
        public bool Loaded { get { return _didLoadCorrectly; } }
        public string Filepath { get { return _path; } }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CommandsEntryPoints
    {
        // This is always:
        //  - Root Instance (the map's entry composite, usually containing entities that call mission/environment composites)
        //  - Global Instance (the main data handler for keeping track of mission number, etc - kinda like a big singleton)
        //  - Pause Menu Instance

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public ShortGuid[] compositeIDs;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CommandsOffsetPair
    {
        public int offset;
        public int count;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CathodeEntityLink
    {
        public ShortGuid connectionID;  //The unique ID for this connection
        public ShortGuid parentParamID; //The ID of the parameter we're providing out 
        public ShortGuid childParamID;  //The ID of the parameter we're providing into the child
        public ShortGuid childID;       //The ID of the entity we're linking to to provide the value for
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
        public ShortGuid paramID; //The ID of the param in the entity
        public int offset;    //The offset of the param this reference points to (in memory this is *4)
    }

    public class CommandsEntityLinks
    {
        public ShortGuid parentID;
        public List<CathodeEntityLink> childLinks = new List<CathodeEntityLink>();

        public CommandsEntityLinks(ShortGuid _id)
        {
            parentID = _id;
        }
    }

    public class CommandsParamRefSet
    {
        public int Index = -1; //TEMP TEST

        public ShortGuid id;
        public List<CathodeParameterReference> refs = new List<CathodeParameterReference>();

        public CommandsParamRefSet(ShortGuid _id)
        {
            id = _id;
        }
    }

    /* TEMP STUFF TO FIX REWRITING */
    [Serializable]
    public class CathodeParameterKeyframeHeader
    {
        public ShortGuid ID;
        public CathodeDataType unk2;
        public ShortGuid keyframeDataID;
        //public float unk3;
        public ShortGuid parameterID;
        public CathodeDataType parameterDataType;
        public ShortGuid parameterSubID; //if parameterID is position, this might be x for example
        public List<ShortGuid> connectedEntity; //path to controlled entity
    }
    [Serializable]
    public class CathodeParameterKeyframe
    {
        public float minSeconds;
        public float maxSeconds;
        public ShortGuid ID;
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
        public ShortGuid ID;
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
    public class CathodeTriggerSequenceTrigger
    {
        public float timing;
        public List<ShortGuid> hierarchy;
    }
    [Serializable]
    public class CathodeTriggerSequenceEvent
    {
        public ShortGuid EventID; //Assumed
        public ShortGuid StartedID; //Assumed
        public ShortGuid FinishedID;
    }
}
