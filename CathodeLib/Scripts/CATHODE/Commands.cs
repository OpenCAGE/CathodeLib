//#define DO_PRETTY_COMPOSITES

using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CATHODE
{
    public class Commands : CathodeFile
    {
        public Action OnLoaded;
        public Action OnSaved;

        // This is always:
        //  - Root Instance (the map's entry composite, usually containing entities that call mission/environment composites)
        //  - Global Instance (the main data handler for keeping track of mission number, etc - kinda like a big singleton)
        //  - Pause Menu Instance
        private ShortGuid[] _entryPoints = null;
        private Composite[] _entryPointObjects = null;

        public List<Composite> Composites { get { return _composites; } }
        private List<Composite> _composites = new List<Composite>();

        public Commands(string path) : base(path) { }

        #region FILE_IO
        /* Save all changes back out */
        override public bool Save()
        {
            //Validate entry points and composite count
            if (_composites.Count == 0) return false;
            if (_entryPoints == null) _entryPoints = new ShortGuid[3];
            if (_entryPoints[0].val == null && _entryPoints[1].val == null && _entryPoints[2].val == null && _composites.Count == 0) return false;

            #region FIX_POTENTIAL_ERRORS
            //If we have composites but the entry points are broken, correct them first!
            if (GetComposite(_entryPoints[2]) == null)
            {
                Composite pausemenu = GetComposite("PAUSEMENU");
                if (pausemenu == null)
                {
                    Console.WriteLine("WARNING: PAUSEMENU composite does not exist! Creating blank placeholder.");
                    pausemenu = AddComposite("PAUSEMENU");
                    pausemenu.shortGUID = new ShortGuid("FE-7B-FE-B3");
                }
                _entryPoints[2] = pausemenu.shortGUID;
            }
            if (GetComposite(_entryPoints[1]) == null)
            {
                Composite global = GetComposite("GLOBAL");
                if (global == null)
                {
                    Console.WriteLine("WARNING: GLOBAL composite does not exist! Creating blank placeholder. This may cause issues with GLOBAL references.");
                    global = AddComposite("GLOBAL");
                    global.shortGUID = new ShortGuid("1D-2E-CE-E5");
                }
                _entryPoints[1] = global.shortGUID;
            }
            if (GetComposite(_entryPoints[0]) == null)
            {
                Console.WriteLine("WARNING: Entry point was not set! Defaulting to composite at index zero.");
                _entryPoints[0] = _composites[0].shortGUID;
            }
            RefreshEntryPointObjects();

            //Fix (& verify) entity-attached resource info
            for (int i = 0; i < _composites.Count; i++)
            {
                List<ResourceReference> animatedModels = new List<ResourceReference>();
                for (int x = 0; x < _composites[i].functions.Count; x++)
                {
                    if (!CommandsUtils.FunctionTypeExists(_composites[i].functions[x].function)) continue;
                    FunctionType type = CommandsUtils.GetFunctionType(_composites[i].functions[x].function);
                    switch (type)
                    {
                        // Types below require resources we can add, and information we should probably correct, so do it automatically!
                        case FunctionType.SoundBarrier:
                            _composites[i].functions[x].AddResource(ResourceType.COLLISION_MAPPING);
                            break;
                        case FunctionType.ExclusiveMaster:
                            _composites[i].functions[x].AddResource(ResourceType.EXCLUSIVE_MASTER_STATE_RESOURCE);
                            break;
                        case FunctionType.TRAV_1ShotSpline:
                            //TODO: There are loads of TRAV_ entities which are unused in the vanilla game, so I'm not sure if they should apply to those too...
                            _composites[i].functions[x].AddResource(ResourceType.TRAVERSAL_SEGMENT);
                            break;
                        case FunctionType.NavMeshBarrier:
                            _composites[i].functions[x].AddResource(ResourceType.NAV_MESH_BARRIER_RESOURCE);
                            _composites[i].functions[x].AddResource(ResourceType.COLLISION_MAPPING);
                            break;
                        case FunctionType.PhysicsSystem:
                            Parameter dps_index = _composites[i].functions[x].GetParameter("system_index");
                            if (dps_index == null)
                            {
                                dps_index = new Parameter("system_index", new cInteger(0));
                                _composites[i].functions[x].parameters.Add(dps_index);
                            }
                            _composites[i].functions[x].AddResource(ResourceType.DYNAMIC_PHYSICS_SYSTEM).startIndex = ((cInteger)dps_index.content).value;
                            break;
                        case FunctionType.EnvironmentModelReference:
                            Parameter rsc = _composites[i].functions[x].GetParameter("resource");
                            if (rsc == null)
                            {
                                rsc = new Parameter("resource", new cResource(_composites[i].functions[x].shortGUID));
                                _composites[i].functions[x].parameters.Add(rsc);
                            }
                            cResource rsc_p = (cResource)rsc.content;
                            rsc_p.AddResource(ResourceType.ANIMATED_MODEL);
                            break;

                        // Types below require various things, but we don't add them as they work without it, so just log a warning.
                        case FunctionType.ModelReference:
                            Parameter mdl = _composites[i].functions[x].GetParameter("resource");
                            if (mdl == null)
                            {
                                mdl = new Parameter("resource", new cResource(_composites[i].functions[x].shortGUID));
                                _composites[i].functions[x].parameters.Add(mdl);
                            }
                            cResource mdl_p = (cResource)mdl.content;
                            if (mdl_p.GetResource(ResourceType.RENDERABLE_INSTANCE) == null)
                                Console.WriteLine("WARNING: ModelReference resource parameter does not contain a RENDERABLE_INSTANCE resource reference!");
                            if (mdl_p.GetResource(ResourceType.COLLISION_MAPPING) == null)
                                Console.WriteLine("WARNING: ModelReference resource parameter does not contain a COLLISION_MAPPING resource reference!");
                            break;
                        case FunctionType.CollisionBarrier:
                            if (_composites[i].functions[x].GetResource(ResourceType.COLLISION_MAPPING) == null)
                                Console.WriteLine("WARNING: CollisionBarrier entity does not contain a COLLISION_MAPPING resource reference!");
                            break;

                        // Types below require only RENDERABLE_INSTANCE resource references on the entity, pointing to the commented model.
                        // We can't add them automatically as we need to know REDS indexes!
                        // UPDATE: I think the game can handle any resource being set here!
                        case FunctionType.ParticleEmitterReference:   /// [dynamic_mesh]       /// - I think i've also seen 1000 particle system too
                        case FunctionType.RibbonEmitterReference:     /// [dynamic_mesh]
                        case FunctionType.SurfaceEffectBox:           /// Global/Props/fogbox.CS2 -> [VolumeFog]
                        case FunctionType.FogBox:                     /// Global/Props/fogplane.CS2 -> [Plane01] 
                        case FunctionType.SurfaceEffectSphere:        /// Global/Props/fogsphere.CS2 -> [Sphere01]
                        case FunctionType.FogSphere:                  /// Global/Props/fogsphere.CS2 -> [Sphere01]
                        case FunctionType.SimpleRefraction:           /// Global/Props/refraction.CS2 -> [Plane01]
                        case FunctionType.SimpleWater:                /// Global/Props/noninteractive_water.CS2 -> [Plane01]
                        case FunctionType.LightReference:             /// Global/Props/deferred_point_light.cs2 -> [Sphere01],  Global/Props/deferred_spot_light.cs2 -> [Sphere02], Global/Props/deferred_strip_light.cs2 -> [Sphere01]
                            if (_composites[i].functions[x].GetResource(ResourceType.RENDERABLE_INSTANCE) == null)
                                Console.WriteLine("ERROR: " + type + " entity does not contain a RENDERABLE_INSTANCE resource reference!");
                            if (_composites[i].functions[x].GetParameter("resource") != null)
                                throw new Exception("Function entity of type " + type + " had an invalid resource parameter applied!");
                            break;
                    }
                }
            }

            //Make sure our composites are in ID order
            _composites = _composites.OrderBy(o => o.shortGUID.ToUInt32()).ToList();
            #endregion

            #region WORK_OUT_WHAT_TO_WRITE
            //Remember some ShortGuid vals here to save re-calling them every time
            ShortGuid SHORTGUID_CAGEAnimation = CommandsUtils.GetFunctionTypeGUID(FunctionType.CAGEAnimation);
            ShortGuid SHORTGUID_TriggerSequence = CommandsUtils.GetFunctionTypeGUID(FunctionType.TriggerSequence);
            ShortGuid SHORTGUID_resource = ShortGuidUtils.Generate("resource");

            //Work out data to write
            List<ParameterData> parameters = new List<ParameterData>();
            List<Entity>[] linkedEntities = new List<Entity>[_composites.Count];
            List<Entity>[] parameterisedEntities = new List<Entity>[_composites.Count];
            List<OverrideEntity>[] reshuffledChecksums = new List<OverrideEntity>[_composites.Count];
            List<ResourceReference>[] resourceReferences = new List<ResourceReference>[_composites.Count];
            List<CAGEAnimation>[] cageAnimationEntities = new List<CAGEAnimation>[_composites.Count];
            List<TriggerSequence>[] triggerSequenceEntities = new List<TriggerSequence>[_composites.Count];
            for (int i = 0; i < _composites.Count; i++)
            {
                List<Entity> ents = _composites[i].GetEntities();
                for (int x = 0; x < ents.Count; x++)
                    for (int y = 0; y < ents[x].parameters.Count; y++)
                        parameters.Add(ents[x].parameters[y].content);

                linkedEntities[i] = new List<Entity>(ents.FindAll(o => o.childLinks.Count != 0)).OrderBy(o => o.shortGUID.ToUInt32()).ToList();
                parameterisedEntities[i] = new List<Entity>(ents.FindAll(o => o.parameters.Count != 0)).OrderBy(o => o.shortGUID.ToUInt32()).ToList();
                reshuffledChecksums[i] = _composites[i].overrides.OrderBy(o => o.checksum.ToUInt32()).ToList();

                cageAnimationEntities[i] = new List<CAGEAnimation>();
                triggerSequenceEntities[i] = new List<TriggerSequence>();
                resourceReferences[i] = new List<ResourceReference>();
                for (int x = 0; x < _composites[i].functions.Count; x++)
                {
                    //If this function is a valid CAGEAnimation or TriggerSequence, remember it
                    if (_composites[i].functions[x].function == SHORTGUID_CAGEAnimation)
                    {
                        CAGEAnimation thisEntity = (CAGEAnimation)_composites[i].functions[x];
                        if (thisEntity.keyframeHeaders.Count == 0 && thisEntity.keyframeData.Count == 0 && thisEntity.paramsData3.Count == 0) continue;
                        cageAnimationEntities[i].Add(thisEntity);
                    }
                    else if (_composites[i].functions[x].function == SHORTGUID_TriggerSequence)
                    {
                        TriggerSequence thisEntity = (TriggerSequence)_composites[i].functions[x];
                        if (thisEntity.triggers.Count == 0 && thisEntity.events.Count == 0) continue;
                        triggerSequenceEntities[i].Add(thisEntity);
                    }

                    //Get resources on this function
                    resourceReferences[i].AddRange(_composites[i].functions[x].resources);

                    //If the function contains a resource parameter, grab those too
                    Parameter param = _composites[i].functions[x].GetParameter(SHORTGUID_resource);
                    if (param == null) continue;
                    resourceReferences[i].AddRange(((cResource)param.content).value);
                }
                resourceReferences[i] = resourceReferences[i].Distinct().ToList();
            }
            parameters = parameters.Distinct().ToList();
            #endregion

            BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath));
            writer.BaseStream.SetLength(0);

            //Write entry points
            for (int i = 0; i < 3; i++)
            {
                if (_entryPoints[i].val == null || GetComposite(_entryPoints[i]) == null)
                    writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                else
                    Utilities.Write<ShortGuid>(writer, _entryPoints[i]);
            }

            //Write placeholder info for parameter/composite offsets
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);

            #region WRITE_PARAMETERS
            //Write out parameters & track offsets
            Dictionary<ParameterData, int> parameterOffsets = new Dictionary<ParameterData, int>();
            for (int i = 0; i < parameters.Count; i++)
            {
                parameterOffsets.Add(parameters[i], (int)writer.BaseStream.Position / 4);
                Utilities.Write<ShortGuid>(writer, CommandsUtils.GetDataTypeGUID(parameters[i].dataType));
                switch (parameters[i].dataType)
                {
                    case DataType.TRANSFORM:
                        Vector3 pos = ((cTransform)parameters[i]).position;
                        Vector3 rot = ((cTransform)parameters[i]).rotation;
                        writer.Write(pos.x); writer.Write(pos.y); writer.Write(pos.z);
                        writer.Write(rot.y); writer.Write(rot.x); writer.Write(rot.z);
                        break;
                    case DataType.INTEGER:
                        writer.Write(((cInteger)parameters[i]).value);
                        break;
                    case DataType.STRING:
                        int stringStart = ((int)writer.BaseStream.Position + 4) / 4;
                        byte[] stringStartRaw = BitConverter.GetBytes(stringStart);
                        stringStartRaw[3] = 0x80;
                        writer.Write(stringStartRaw);
                        string str = ((cString)parameters[i]).value;
                        writer.Write(ShortGuidUtils.Generate(str).val);
                        for (int x = 0; x < str.Length; x++) writer.Write(str[x]);
                        writer.Write((char)0x00);
                        Utilities.Align(writer, 4);
                        break;
                    case DataType.BOOL:
                        if (((cBool)parameters[i]).value) writer.Write(1); else writer.Write(0);
                        break;
                    case DataType.FLOAT:
                        writer.Write(((cFloat)parameters[i]).value);
                        break;
                    case DataType.RESOURCE:
                        Utilities.Write<ShortGuid>(writer, ((cResource)parameters[i]).resourceID);
                        break;
                    case DataType.VECTOR:
                        Vector3 dir = ((cVector3)parameters[i]).value;
                        writer.Write(dir.x); writer.Write(dir.y); writer.Write(dir.z);
                        break;
                    case DataType.ENUM:
                        Utilities.Write<ShortGuid>(writer, ((cEnum)parameters[i]).enumID);
                        writer.Write(((cEnum)parameters[i]).enumIndex);
                        break;
                    case DataType.SPLINE:
                        cSpline thisSpline = ((cSpline)parameters[i]);
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
            #endregion

            #region WRITE_COMPOSITES
            //Write out composites in order of their IDs & track offsets
            int[] compositeOffsets = new int[_composites.Count];
            for (int i = 0; i < _composites.Count; i++)
            {
                int scriptStartPos = (int)writer.BaseStream.Position / 4;

                Utilities.Write<ShortGuid>(writer, ShortGuidUtils.Generate(_composites[i].name));
                for (int x = 0; x < _composites[i].name.Length; x++) writer.Write(_composites[i].name[x]);
                writer.Write((char)0x00);
                Utilities.Align(writer, 4);

                //Write data
                OffsetPair[] scriptPointerOffsetInfo = new OffsetPair[(int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS];
                for (int x = 0; x < (int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS; x++)
                {
                    switch ((CompositeFileData)x)
                    {
                        case CompositeFileData.COMPOSITE_HEADER:
                            {
                                scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, 2);
                                Utilities.Write<ShortGuid>(writer, _composites[i].shortGUID);
                                writer.Write(0);
                                break;
                            }
                        case CompositeFileData.ENTITY_CONNECTIONS:
                            {
                                List<OffsetPair> offsetPairs = new List<OffsetPair>(linkedEntities[i].Count);
                                foreach (Entity entityWithLink in linkedEntities[i])
                                {
                                    offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, entityWithLink.childLinks.Count));
                                    Utilities.Write<EntityLink>(writer, entityWithLink.childLinks);
                                }

                                scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, linkedEntities[i].Count);
                                for (int p = 0; p < linkedEntities[i].Count; p++)
                                {
                                    writer.Write(linkedEntities[i][p].shortGUID.val);
                                    writer.Write(offsetPairs[p].GlobalOffset / 4);
                                    writer.Write(offsetPairs[p].EntryCount);
                                }

                                break;
                            }
                        case CompositeFileData.ENTITY_PARAMETERS:
                            {
                                List<OffsetPair> offsetPairs = new List<OffsetPair>(parameterisedEntities[i].Count);
                                foreach (Entity entityWithParam in parameterisedEntities[i])
                                {
                                    offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, entityWithParam.parameters.Count));

                                    //TODO: OPTIMISE THIS - IT TAKES ABOUT 9 SECONDS ALL TOGETHER ON TORRENS!
                                    Dictionary<ShortGuid, int> paramsWithOffsets = new Dictionary<ShortGuid, int>();
                                    for (int y = 0; y < entityWithParam.parameters.Count; y++)
                                        paramsWithOffsets.Add(entityWithParam.parameters[y].shortGUID, parameterOffsets[entityWithParam.parameters[y].content]);
                                    paramsWithOffsets = paramsWithOffsets.OrderBy(o => o.Value).ToDictionary(o => o.Key, o => o.Value);

                                    foreach (KeyValuePair<ShortGuid, int> entry in paramsWithOffsets)
                                    {
                                        Utilities.Write<ShortGuid>(writer, entry.Key);
                                        writer.Write(entry.Value);
                                    }
                                }

                                scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, offsetPairs.Count);
                                for (int p = 0; p < parameterisedEntities[i].Count; p++)
                                {
                                    writer.Write(parameterisedEntities[i][p].shortGUID.val);
                                    writer.Write(offsetPairs[p].GlobalOffset / 4);
                                    writer.Write(offsetPairs[p].EntryCount);
                                }
                                break;
                            }
                        case CompositeFileData.ENTITY_OVERRIDES:
                            {
                                List<OffsetPair> offsetPairs = new List<OffsetPair>(_composites[i].overrides.Count);
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
                        case CompositeFileData.ENTITY_OVERRIDES_CHECKSUM:
                            {
                                scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, reshuffledChecksums[i].Count);
                                for (int p = 0; p < reshuffledChecksums[i].Count; p++)
                                {
                                    writer.Write(reshuffledChecksums[i][p].shortGUID.val);
                                    writer.Write(reshuffledChecksums[i][p].checksum.val);
                                }
                                break;
                            }
                        case CompositeFileData.COMPOSITE_EXPOSED_PARAMETERS:
                            {
                                scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, _composites[i].variables.Count);
                                for (int p = 0; p < _composites[i].variables.Count; p++)
                                {
                                    writer.Write(_composites[i].variables[p].shortGUID.val);
                                    writer.Write(CommandsUtils.GetDataTypeGUID(_composites[i].variables[p].type).val);
                                    writer.Write(_composites[i].variables[p].parameter.val);
                                }
                                break;
                            }
                        case CompositeFileData.ENTITY_PROXIES:
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
                        case CompositeFileData.ENTITY_FUNCTIONS:
                            {
                                scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, _composites[i].functions.Count);
                                for (int p = 0; p < _composites[i].functions.Count; p++)
                                {
                                    writer.Write(_composites[i].functions[p].shortGUID.val);
                                    writer.Write(_composites[i].functions[p].function.val);
                                }
                                break;
                            }
                        case CompositeFileData.RESOURCE_REFERENCES:
                            {
                                scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, resourceReferences[i].Count);
                                for (int p = 0; p < resourceReferences[i].Count; p++)
                                {
                                    writer.Write(resourceReferences[i][p].position.x);
                                    writer.Write(resourceReferences[i][p].position.y);
                                    writer.Write(resourceReferences[i][p].position.z);
                                    writer.Write(resourceReferences[i][p].rotation.x);
                                    writer.Write(resourceReferences[i][p].rotation.y);
                                    writer.Write(resourceReferences[i][p].rotation.z);
                                    writer.Write(resourceReferences[i][p].resourceID.val); //Sometimes this is the entity ID that uses the resource, other times it's the "resource" parameter ID link
                                    writer.Write(CommandsUtils.GetResourceEntryTypeGUID(resourceReferences[i][p].entryType).val);
                                    switch (resourceReferences[i][p].entryType)
                                    {
                                        case ResourceType.RENDERABLE_INSTANCE:
                                            writer.Write(resourceReferences[i][p].startIndex);
                                            writer.Write(resourceReferences[i][p].count);
                                            break;
                                        case ResourceType.COLLISION_MAPPING:
                                            writer.Write(resourceReferences[i][p].startIndex);
                                            writer.Write(resourceReferences[i][p].collisionID.val);
                                            break;
                                        case ResourceType.ANIMATED_MODEL:
                                        case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                                            writer.Write(resourceReferences[i][p].startIndex);
                                            writer.Write(-1);
                                            break;
                                        case ResourceType.EXCLUSIVE_MASTER_STATE_RESOURCE:
                                        case ResourceType.NAV_MESH_BARRIER_RESOURCE:
                                        case ResourceType.TRAVERSAL_SEGMENT:
                                            writer.Write(-1);
                                            writer.Write(-1);
                                            break;
                                    }
                                }
                                break;
                            }
                        case CompositeFileData.TRIGGERSEQUENCE_DATA: //Actually CAGEANIMATION_DATA, but indexes are flipped
                            {
                                List<int> globalOffsets = new List<int>(cageAnimationEntities[i].Count);
                                for (int p = 0; p < cageAnimationEntities[i].Count; p++)
                                {
                                    List<int> hierarchyOffsets = new List<int>(cageAnimationEntities[i][p].keyframeHeaders.Count);
                                    for (int pp = 0; pp < cageAnimationEntities[i][p].keyframeHeaders.Count; pp++)
                                    {
                                        hierarchyOffsets.Add((int)writer.BaseStream.Position);
                                        Utilities.Write<ShortGuid>(writer, cageAnimationEntities[i][p].keyframeHeaders[pp].connectedEntity);
                                    }

                                    int paramData1Offset = (int)writer.BaseStream.Position;
                                    for (int pp = 0; pp < cageAnimationEntities[i][p].keyframeHeaders.Count; pp++)
                                    {
                                        Utilities.Write(writer, cageAnimationEntities[i][p].keyframeHeaders[pp].ID);
                                        Utilities.Write(writer, CommandsUtils.GetDataTypeGUID(cageAnimationEntities[i][p].keyframeHeaders[pp].unk2));
                                        Utilities.Write(writer, cageAnimationEntities[i][p].keyframeHeaders[pp].keyframeDataID);
                                        Utilities.Write(writer, cageAnimationEntities[i][p].keyframeHeaders[pp].parameterID);
                                        Utilities.Write(writer, CommandsUtils.GetDataTypeGUID(cageAnimationEntities[i][p].keyframeHeaders[pp].parameterDataType));
                                        Utilities.Write(writer, cageAnimationEntities[i][p].keyframeHeaders[pp].parameterSubID);
                                        writer.Write(hierarchyOffsets[pp] / 4);
                                        writer.Write(cageAnimationEntities[i][p].keyframeHeaders[pp].connectedEntity.Count);
                                    }

                                    List<int> internalOffsets = new List<int>(cageAnimationEntities[i][p].keyframeData.Count);
                                    for (int pp = 0; pp < cageAnimationEntities[i][p].keyframeData.Count; pp++)
                                    {
                                        int toPointTo = (int)writer.BaseStream.Position;
                                        for (int ppp = 0; ppp < cageAnimationEntities[i][p].keyframeData[pp].keyframes.Count; ppp++)
                                        {
                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].unk1);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].secondsSinceStart);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].secondsSinceStartValidation);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].paramValue);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].unk2);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].unk3);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].unk4);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].unk5);
                                        }

                                        internalOffsets.Add(((int)writer.BaseStream.Position) / 4);

                                        writer.Write(cageAnimationEntities[i][p].keyframeData[pp].minSeconds);
                                        writer.Write(cageAnimationEntities[i][p].keyframeData[pp].maxSeconds);
                                        Utilities.Write(writer, cageAnimationEntities[i][p].keyframeData[pp].ID);

                                        writer.Write(toPointTo / 4);
                                        writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes.Count);
                                    }

                                    int paramData2Offset = (int)writer.BaseStream.Position;
                                    Utilities.Write<int>(writer, internalOffsets);

                                    internalOffsets = new List<int>(cageAnimationEntities[i][p].paramsData3.Count);
                                    for (int pp = 0; pp < cageAnimationEntities[i][p].paramsData3.Count; pp++)
                                    {
                                        int toPointTo = (int)writer.BaseStream.Position;
                                        for (int ppp = 0; ppp < cageAnimationEntities[i][p].paramsData3[pp].keyframes.Count; ppp++)
                                        {
                                            writer.Write(cageAnimationEntities[i][p].paramsData3[pp].keyframes[ppp].unk1);
                                            writer.Write(cageAnimationEntities[i][p].paramsData3[pp].keyframes[ppp].SecondsSinceStart);
                                            writer.Write(cageAnimationEntities[i][p].paramsData3[pp].keyframes[ppp].unk2);
                                            writer.Write(cageAnimationEntities[i][p].paramsData3[pp].keyframes[ppp].unk3);
                                            writer.Write(cageAnimationEntities[i][p].paramsData3[pp].keyframes[ppp].unk4);
                                            writer.Write(cageAnimationEntities[i][p].paramsData3[pp].keyframes[ppp].unk5);
                                        }

                                        internalOffsets.Add(((int)writer.BaseStream.Position) / 4);

                                        writer.Write(cageAnimationEntities[i][p].paramsData3[pp].minSeconds);
                                        writer.Write(cageAnimationEntities[i][p].paramsData3[pp].maxSeconds);
                                        Utilities.Write(writer, cageAnimationEntities[i][p].paramsData3[pp].ID);

                                        writer.Write(toPointTo / 4);
                                        writer.Write(cageAnimationEntities[i][p].paramsData3[pp].keyframes.Count);
                                    }

                                    int paramData3Offset = (int)writer.BaseStream.Position;
                                    Utilities.Write<int>(writer, internalOffsets);

                                    globalOffsets.Add((int)writer.BaseStream.Position);
                                    writer.Write(cageAnimationEntities[i][p].shortGUID.val);
                                    writer.Write(paramData1Offset / 4);
                                    writer.Write(cageAnimationEntities[i][p].keyframeHeaders.Count);
                                    writer.Write(paramData2Offset / 4);
                                    writer.Write(cageAnimationEntities[i][p].keyframeData.Count);
                                    writer.Write(paramData3Offset / 4);
                                    writer.Write(cageAnimationEntities[i][p].paramsData3.Count);
                                }

                                scriptPointerOffsetInfo[(int)CompositeFileData.CAGEANIMATION_DATA] = new OffsetPair(writer.BaseStream.Position, globalOffsets.Count);
                                for (int p = 0; p < globalOffsets.Count; p++)
                                {
                                    writer.Write(globalOffsets[p] / 4);
                                }
                                break;
                            }
                        case CompositeFileData.CAGEANIMATION_DATA: //Actually TRIGGERSEQUENCE_DATA, but indexes are flipped
                            {
                                List<int> globalOffsets = new List<int>(triggerSequenceEntities[i].Count);
                                for (int p = 0; p < triggerSequenceEntities[i].Count; p++)
                                {
                                    List<int> hierarchyOffsets = new List<int>(triggerSequenceEntities[i][p].triggers.Count);
                                    for (int pp = 0; pp < triggerSequenceEntities[i][p].triggers.Count; pp++)
                                    {
                                        hierarchyOffsets.Add((int)writer.BaseStream.Position);
                                        Utilities.Write<ShortGuid>(writer, triggerSequenceEntities[i][p].triggers[pp].hierarchy);
                                    }

                                    int triggerOffset = (int)writer.BaseStream.Position;
                                    for (int pp = 0; pp < triggerSequenceEntities[i][p].triggers.Count; pp++)
                                    {
                                        writer.Write(hierarchyOffsets[pp] / 4);
                                        writer.Write(triggerSequenceEntities[i][p].triggers[pp].hierarchy.Count);
                                        writer.Write(triggerSequenceEntities[i][p].triggers[pp].timing);
                                    }

                                    int eventOffset = (int)writer.BaseStream.Position;
                                    for (int pp = 0; pp < triggerSequenceEntities[i][p].events.Count; pp++)
                                    {
                                        writer.Write(triggerSequenceEntities[i][p].events[pp].EventID.val);
                                        writer.Write(triggerSequenceEntities[i][p].events[pp].StartedID.val);
                                        writer.Write(triggerSequenceEntities[i][p].events[pp].FinishedID.val);
                                    }

                                    globalOffsets.Add((int)writer.BaseStream.Position);
                                    writer.Write(triggerSequenceEntities[i][p].shortGUID.val);
                                    writer.Write(triggerOffset / 4);
                                    writer.Write(triggerSequenceEntities[i][p].triggers.Count);
                                    writer.Write(eventOffset / 4);
                                    writer.Write(triggerSequenceEntities[i][p].events.Count);
                                }

                                scriptPointerOffsetInfo[(int)CompositeFileData.TRIGGERSEQUENCE_DATA] = new OffsetPair(writer.BaseStream.Position, globalOffsets.Count);
                                for (int p = 0; p < globalOffsets.Count; p++)
                                {
                                    writer.Write(globalOffsets[p] / 4);
                                }
                                break;
                            }
                        case CompositeFileData.UNUSED:
                            {
                                scriptPointerOffsetInfo[x] = new OffsetPair(0, 0);
                                break;
                            }
                    }
                }

                //Write pointers to the pointers of the content
                compositeOffsets[i] = (int)writer.BaseStream.Position / 4;
                writer.Write(0);
                for (int x = 0; x < (int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS; x++)
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

                //Write function count (TODO: sometimes this count excludes some entities in the vanilla paks - why?)
                writer.Write(_composites[i].functions.FindAll(o => CommandsUtils.FunctionTypeExists(o.function)).Count);
                writer.Write(_composites[i].functions.Count);
            }
            #endregion

            //Write out parameter offsets
            int parameterOffsetPos = (int)writer.BaseStream.Position;
            foreach (KeyValuePair<ParameterData, int> offset in parameterOffsets)
                writer.Write(offset.Value);

            //Write out composite offsets
            int compositeOffsetPos = (int)writer.BaseStream.Position;
            Utilities.Write<int>(writer, compositeOffsets);

            //Rewrite header info with correct offsets 
            writer.BaseStream.Position = 12;
            writer.Write(parameterOffsetPos / 4);
            writer.Write(parameters.Count);
            writer.Write(compositeOffsetPos / 4);
            writer.Write(_composites.Count);

            writer.Close();
            OnSaved?.Invoke();
            return true;
        }

        /* Read the parameter and composite offsets & get entry points */
        override protected bool Load()
        {
            if (!File.Exists(_filepath)) return false;

            BinaryReader reader = new BinaryReader(File.OpenRead(_filepath));

            //Read entry points
            _entryPoints = new ShortGuid[3];
            for (int i = 0; i < 3; i++) _entryPoints[i] = Utilities.Consume<ShortGuid>(reader);

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
            Dictionary<int, ParameterData> parameters = new Dictionary<int, ParameterData>(parameter_count);
            for (int i = 0; i < parameter_count; i++)
            {
                reader.BaseStream.Position = parameterOffsets[i] * 4;
                ParameterData this_parameter = new ParameterData(CommandsUtils.GetDataType(new ShortGuid(reader)));
                switch (this_parameter.dataType)
                {
                    case DataType.TRANSFORM:
                        this_parameter = new cTransform();
                        ((cTransform)this_parameter).position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        float _x, _y, _z; _y = reader.ReadSingle(); _x = reader.ReadSingle(); _z = reader.ReadSingle(); //Y,X,Z!
                        ((cTransform)this_parameter).rotation = new Vector3(_x, _y, _z);
                        break;
                    case DataType.INTEGER:
                        this_parameter = new cInteger(reader.ReadInt32());
                        break;
                    case DataType.STRING:
                        reader.BaseStream.Position += 8;
                        this_parameter = new cString(Utilities.ReadString(reader));
                        Utilities.Align(reader, 4);
                        break;
                    case DataType.BOOL:
                        this_parameter = new cBool((reader.ReadInt32() == 1));
                        break;
                    case DataType.FLOAT:
                        this_parameter = new cFloat(reader.ReadSingle());
                        break;
                    case DataType.RESOURCE:
                        this_parameter = new cResource(new ShortGuid(reader));
                        break;
                    case DataType.VECTOR:
                        this_parameter = new cVector3(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                        break;
                    case DataType.ENUM:
                        this_parameter = new cEnum(new ShortGuid(reader), reader.ReadInt32());
                        break;
                    case DataType.SPLINE:
                        reader.BaseStream.Position += 4;
                        List<cTransform> points = new List<cTransform>(reader.ReadInt32());
                        for (int x = 0; x < points.Capacity; x++)
                        {
                            points.Add(new cTransform(
                                new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                                new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()) //TODO is this YXZ?
                            ));
                        }
                        this_parameter = new cSpline(points);
                        break;
                }
                parameters.Add(parameterOffsets[i], this_parameter);
            }

            //Read all composites from the PAK
            Composite[] composites = new Composite[composite_count];
            for (int i = 0; i < composite_count; i++)
            {
                reader.BaseStream.Position = compositeOffsets[i] * 4;
                reader.BaseStream.Position += 4; //Skip 0x00,0x00,0x00,0x00
                Composite composite = new Composite();

                //Read the offsets and counts
                OffsetPair[] offsetPairs = new OffsetPair[(int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS];
                int scriptStartOffset = 0;
                for (int x = 0; x < (int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS; x++)
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
                reader.BaseStream.Position += 8;

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
                List<ResourceReference> resourceRefs = new List<ResourceReference>();
                Dictionary<ShortGuid, ShortGuid> overrideChecksums = new Dictionary<ShortGuid, ShortGuid>();
                for (int x = 0; x < offsetPairs.Length; x++)
                {
                    reader.BaseStream.Position = offsetPairs[x].GlobalOffset * 4;
                    for (int y = 0; y < offsetPairs[x].EntryCount; y++)
                    {
                        switch ((CompositeFileData)x)
                        {
                            case CompositeFileData.ENTITY_CONNECTIONS:
                                {
                                    reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                    entityLinks.Add(new CommandsEntityLinks(new ShortGuid(reader)));
                                    int NumberOfParams = JumpToOffset(ref reader);
                                    entityLinks[entityLinks.Count - 1].childLinks.AddRange(Utilities.ConsumeArray<EntityLink>(reader, NumberOfParams));
                                    break;
                                }
                            case CompositeFileData.ENTITY_PARAMETERS:
                                {
                                    reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                    paramRefSets.Add(new CommandsParamRefSet(new ShortGuid(reader)));
                                    int NumberOfParams = JumpToOffset(ref reader);
                                    paramRefSets[paramRefSets.Count - 1].refs.AddRange(Utilities.ConsumeArray<CathodeParameterReference>(reader, NumberOfParams));
                                    break;
                                }
                            case CompositeFileData.ENTITY_OVERRIDES:
                                {
                                    reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                    OverrideEntity overrider = new OverrideEntity(new ShortGuid(reader));
                                    int NumberOfParams = JumpToOffset(ref reader);
                                    overrider.hierarchy.AddRange(Utilities.ConsumeArray<ShortGuid>(reader, NumberOfParams));
                                    composite.overrides.Add(overrider);
                                    break;
                                }
                            case CompositeFileData.ENTITY_OVERRIDES_CHECKSUM:
                                {
                                    reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                    overrideChecksums.Add(new ShortGuid(reader), new ShortGuid(reader));
                                    break;
                                }
                            //TODO: Really, I think these should be treated as parameters on the composite class as they are the pins we use for composite instances.
                            //      Need to look into this more and see if any of these entities actually contain much data other than links into the composite itself.
                            case CompositeFileData.COMPOSITE_EXPOSED_PARAMETERS:
                                {
                                    reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                    VariableEntity dtEntity = new VariableEntity(new ShortGuid(reader));
                                    dtEntity.type = CommandsUtils.GetDataType(new ShortGuid(reader));
                                    dtEntity.parameter = new ShortGuid(reader);
                                    composite.variables.Add(dtEntity);
                                    break;
                                }
                            case CompositeFileData.ENTITY_PROXIES:
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
                            case CompositeFileData.ENTITY_FUNCTIONS:
                                {
                                    reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                    ShortGuid entityID = new ShortGuid(reader);
                                    ShortGuid functionID = new ShortGuid(reader);
                                    if (CommandsUtils.FunctionTypeExists(functionID))
                                    {
                                        //This entity executes a hard-coded CATHODE function
                                        FunctionType functionType = CommandsUtils.GetFunctionType(functionID);
                                        switch (functionType)
                                        {
                                            case FunctionType.CAGEAnimation:
                                                CAGEAnimation cageAnimation = new CAGEAnimation(entityID);
                                                composite.functions.Add(cageAnimation);
                                                break;
                                            case FunctionType.TriggerSequence:
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
                            case CompositeFileData.RESOURCE_REFERENCES:
                                {
                                    reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 40);

                                    ResourceReference resource = new ResourceReference();
                                    resource.position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                    resource.rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                    resource.resourceID = new ShortGuid(reader);
                                    resource.entryType = CommandsUtils.GetResourceEntryType(reader.ReadBytes(4));
                                    switch (resource.entryType)
                                    {
                                        case ResourceType.RENDERABLE_INSTANCE:
                                            resource.startIndex = reader.ReadInt32(); //REDS.BIN entry index
                                            resource.count = reader.ReadInt32(); //REDS.BIN entry count
                                            break;
                                        case ResourceType.COLLISION_MAPPING:
                                            resource.startIndex = reader.ReadInt32(); //COLLISION.MAP entry index?
                                            resource.collisionID = new ShortGuid(reader); //ID which maps to *something*
                                            break;
                                        case ResourceType.ANIMATED_MODEL:
                                        case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                                            resource.startIndex = reader.ReadInt32(); //PHYSICS.MAP entry index?
                                            reader.BaseStream.Position += 4;
                                            break;
                                        case ResourceType.EXCLUSIVE_MASTER_STATE_RESOURCE:
                                        case ResourceType.NAV_MESH_BARRIER_RESOURCE:
                                        case ResourceType.TRAVERSAL_SEGMENT:
                                            reader.BaseStream.Position += 8;
                                            break;
                                    }
                                    resourceRefs.Add(resource);
                                    break;
                                }
                            case CompositeFileData.CAGEANIMATION_DATA:
                                {
                                    reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                    reader.BaseStream.Position = (reader.ReadInt32() * 4);

                                    Entity thisEntity = composite.GetEntityByID(new ShortGuid(reader));
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
                            case CompositeFileData.TRIGGERSEQUENCE_DATA:
                                {
                                    reader.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                    reader.BaseStream.Position = (reader.ReadInt32() * 4);

                                    Entity thisEntity = composite.GetEntityByID(new ShortGuid(reader));
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
                    composite.GetEntityByID(entityLinks[x].parentID)?.childLinks.AddRange(entityLinks[x].childLinks);

                //Clone parameter data to entities
                for (int x = 0; x < paramRefSets.Count; x++)
                {
                    Entity entToApply = composite.GetEntityByID(paramRefSets[x].id);
                    if (entToApply == null) continue;
                    for (int y = 0; y < paramRefSets[x].refs.Count; y++)
                        entToApply.parameters.Add(new Parameter(paramRefSets[x].refs[y].paramID, (ParameterData)parameters[paramRefSets[x].refs[y].offset].Clone()));
                }

                //Remap resource references
                ShortGuid resParamID = ShortGuidUtils.Generate("resource");
                ShortGuid physEntID = ShortGuidUtils.Generate("PhysicsSystem");
                //Check to see if this resource applies to a PARAMETER on an entity
                for (int x = 0; x < composite.functions.Count; x++)
                {
                    for (int y = 0; y < composite.functions[x].parameters.Count; y++)
                    {
                        if (composite.functions[x].parameters[y].shortGUID != resParamID) continue;

                        cResource resourceParam = (cResource)composite.functions[x].parameters[y].content;
                        resourceParam.value.AddRange(resourceRefs.Where(o => o.resourceID == resourceParam.resourceID));
                        resourceRefs.RemoveAll(o => o.resourceID == resourceParam.resourceID);
                    }
                }
                //Check to see if this resource applies directly to an ENTITY
                for (int x = 0; x < composite.functions.Count; x++)
                {
                    composite.functions[x].resources.AddRange(resourceRefs.Where(o => o.resourceID == composite.functions[x].shortGUID));
                    resourceRefs.RemoveAll(o => o.resourceID == composite.functions[x].shortGUID);
                }
                //Any that are left over will be applied to PhysicsSystem entities
                if (resourceRefs.Count == 1 && resourceRefs[0].entryType == ResourceType.DYNAMIC_PHYSICS_SYSTEM)
                {
                    FunctionEntity physEnt = composite.functions.FirstOrDefault(o => o.function == physEntID);
                    if (physEnt != null) physEnt.resources.Add(resourceRefs[0]);
                }
                else if (resourceRefs.Count != 0)
                {
                    Console.WriteLine("WARNING: This composite contains unexpected trailing resources!");
                }
                resourceRefs.Clear();

                composites[i] = composite;
            }
            _composites = composites.ToList<Composite>();

            reader.Close();
            OnLoaded?.Invoke();
            return true;
        }
        #endregion

        #region ACCESSORS
        /* Add a new composite */
        public Composite AddComposite(string name, bool isRoot = false)
        {
            Composite comp = new Composite(name);
            _composites.Add(comp);
            if (isRoot) SetRootComposite(comp);
            return comp;
        }

        /* Return a list of filenames for composites in the CommandsPAK archive */
        public string[] GetCompositeNames()
        {
            string[] toReturn = new string[_composites.Count];
            for (int i = 0; i < _composites.Count; i++) toReturn[i] = _composites[i].name;
            return toReturn;
        }

        /* Get an individual composite */
        public Composite GetComposite(string name)
        {
            return _composites.FirstOrDefault(o => o.name == name || o.name == name.Replace('/', '\\'));
        }
        public Composite GetComposite(ShortGuid id)
        {
            if (id.val == null) return null;
            return _composites.FirstOrDefault(o => o.shortGUID == id);
        }

        /* Get entry point composite objects */
        public Composite[] EntryPoints
        {
            get
            {
                if (_entryPoints == null) return null;
                if (_entryPointObjects != null) return _entryPointObjects;
                RefreshEntryPointObjects();
                return _entryPointObjects;
            }
        }

        /* Set the root composite for this COMMANDS.PAK (the root of the level - GLOBAL and PAUSEMENU are also instanced) */
        public void SetRootComposite(Composite composite)
        {
            SetRootComposite(composite.shortGUID);
        }
        public void SetRootComposite(ShortGuid compositeID)
        {
            if (_entryPoints == null)
                _entryPoints = new ShortGuid[3];

            _entryPoints[0] = compositeID;
            _entryPointObjects = null;
        }
        #endregion

        #region HELPERS
        /* Read offset info & count, jump to the offset & return the count */
        private int JumpToOffset(ref BinaryReader reader)
        {
            int offset = reader.ReadInt32() * 4;
            int count = reader.ReadInt32();

            reader.BaseStream.Position = offset;
            return count;
        }

        /* Refresh the composite pointers for our entry points */
        private void RefreshEntryPointObjects()
        {
            _entryPointObjects = new Composite[_entryPoints.Length];
            for (int i = 0; i < _entryPoints.Length; i++) _entryPointObjects[i] = GetComposite(_entryPoints[i]);
        }
        #endregion

        /* -- */

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct CathodeParameterReference
        {
            public ShortGuid paramID; //The ID of the param in the entity
            public int offset;    //The offset of the param this reference points to (in memory this is *4)
        }

        public class CommandsEntityLinks
        {
            public ShortGuid parentID;
            public List<EntityLink> childLinks = new List<EntityLink>();

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

    /* TEMP STUFF TO FIX REWRITING */
    [Serializable]
    public class CathodeParameterKeyframeHeader
    {
        public ShortGuid ID;
        public DataType unk2;
        public ShortGuid keyframeDataID;
        //public float unk3;
        public ShortGuid parameterID;
        public DataType parameterDataType;
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
