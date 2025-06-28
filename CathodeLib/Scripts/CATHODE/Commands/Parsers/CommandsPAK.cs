using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Scripting.Internal.Parsers
{
    public static class CommandsPAK
    {
        public static void Read(byte[] content, out ShortGuid[] EntryPoints, out List<Composite> Entries)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(content)))
            {
                //Read entry points
                EntryPoints = new ShortGuid[3];
                for (int i = 0; i < 3; i++) EntryPoints[i] = Utilities.Consume<ShortGuid>(reader);

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
                {
                    ParameterData[] parametersArr = new ParameterData[parameter_count];
                    Parallel.For(0, parameter_count, i =>
                    {
                        using (BinaryReader reader_parallel = new BinaryReader(new MemoryStream(content)))
                        {
                            reader_parallel.BaseStream.Position = parameterOffsets[i] * 4;
                            parametersArr[i] = new ParameterData((DataType)reader_parallel.ReadUInt32());
                            switch (parametersArr[i].dataType)
                            {
                                case DataType.TRANSFORM:
                                    parametersArr[i] = new cTransform();
                                    ((cTransform)parametersArr[i]).position = new Vector3(reader_parallel.ReadSingle(), reader_parallel.ReadSingle(), reader_parallel.ReadSingle());
                                    float _x, _y, _z; _y = reader_parallel.ReadSingle(); _x = reader_parallel.ReadSingle(); _z = reader_parallel.ReadSingle(); //This is Y/X/Z as it's stored as Yaw/Pitch/Roll
                                    ((cTransform)parametersArr[i]).rotation = new Vector3(_x, _y, _z);
                                    break;
                                case DataType.INTEGER:
                                    parametersArr[i] = new cInteger(reader_parallel.ReadInt32());
                                    break;
                                case DataType.STRING:
                                    reader_parallel.BaseStream.Position += 8;
                                    parametersArr[i] = new cString(Utilities.ReadString(reader_parallel).Replace("\u0092", "'"));
                                    Utilities.Align(reader_parallel, 4);
                                    break;
                                case DataType.BOOL:
                                    parametersArr[i] = new cBool((reader_parallel.ReadInt32() == 1));
                                    break;
                                case DataType.FLOAT:
                                    parametersArr[i] = new cFloat(reader_parallel.ReadSingle());
                                    break;
                                case DataType.RESOURCE:
                                    parametersArr[i] = new cResource(new ShortGuid(reader_parallel));
                                    break;
                                case DataType.VECTOR:
                                    parametersArr[i] = new cVector3(new Vector3(reader_parallel.ReadSingle(), reader_parallel.ReadSingle(), reader_parallel.ReadSingle()));
                                    break;
                                case DataType.ENUM:
                                    parametersArr[i] = new cEnum(new ShortGuid(reader_parallel), reader_parallel.ReadInt32());
                                    break;
                                case DataType.SPLINE:
                                    reader_parallel.BaseStream.Position += 4;
                                    List<cTransform> points = new List<cTransform>(reader_parallel.ReadInt32());
                                    for (int x = 0; x < points.Capacity; x++)
                                    {
                                        cTransform spline_point = new cTransform();
                                        spline_point.position = new Vector3(reader_parallel.ReadSingle(), reader_parallel.ReadSingle(), reader_parallel.ReadSingle());
                                        float __x, __y, __z; __y = reader_parallel.ReadSingle(); __x = reader_parallel.ReadSingle(); __z = reader_parallel.ReadSingle(); //This is Y/X/Z as it's stored as Yaw/Pitch/Roll
                                        spline_point.rotation = new Vector3(__x, __y, __z);
                                        points.Add(spline_point);
                                    }
                                    parametersArr[i] = new cSpline(points);
                                    break;
                            }
                        }
                    });

                    for (int i = 0; i < parameter_count; i++)
                        parameters.Add(parameterOffsets[i], parametersArr[i]);
                }

                //Read all composites from the PAK
                Composite[] composites = new Composite[composite_count];
                Parallel.For(0, composite_count, i =>
                {
                    using (BinaryReader reader_parallel = new BinaryReader(new MemoryStream(content)))
                    {
                        reader_parallel.BaseStream.Position = compositeOffsets[i] * 4;
                        reader_parallel.BaseStream.Position += 4; //Skip 0x00,0x00,0x00,0x00
                        Composite composite = new Composite();

                        //Read the offsets and counts
                        OffsetPair[] offsetPairs = new OffsetPair[(int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS];
                        int scriptStartOffset = 0;
                        for (int x = 0; x < (int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS; x++)
                        {
                            if (x == 0)
                            {
                                byte[] startOffsetRaw = reader_parallel.ReadBytes(4);
                                startOffsetRaw[3] = 0x00; //For some reason this is 0x80?
                                scriptStartOffset = BitConverter.ToInt32(startOffsetRaw, 0);
                            }
                            offsetPairs[x] = Utilities.Consume<OffsetPair>(reader_parallel);
                            if (x == 0) composite.shortGUID = new ShortGuid(reader_parallel);
                        }
                        reader_parallel.BaseStream.Position += 8;

                        //Read script ID and string name
                        reader_parallel.BaseStream.Position = (scriptStartOffset * 4) + 4;
                        composite.name = Utilities.ReadString(reader_parallel);
                        if (Commands.UsePrettyPaths)
                        {
                            string prettyPath = CompositeUtils.GetPrettyPath(composite.shortGUID);
                            if (prettyPath != "") composite.name = prettyPath;
                            composite.name = composite.name.Replace("/", "\\");
                        }
                        Utilities.Align(reader_parallel, 4);

                        //Pull data from those offsets
                        List<CommandsEntityLinks> entityLinks = new List<CommandsEntityLinks>();
                        List<CommandsParamRefSet> paramRefSets = new List<CommandsParamRefSet>();
                        List<ResourceReference> resourceRefs = new List<ResourceReference>();
                        for (int x = 0; x < offsetPairs.Length; x++)
                        {
                            reader_parallel.BaseStream.Position = offsetPairs[x].GlobalOffset * 4;
                            for (int y = 0; y < offsetPairs[x].EntryCount; y++)
                            {
                                switch ((CompositeFileData)x)
                                {
                                    case CompositeFileData.ENTITY_CONNECTIONS:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                            entityLinks.Add(new CommandsEntityLinks(new ShortGuid(reader_parallel)));
                                            int NumberOfParams = JumpToOffset(reader_parallel);
                                            entityLinks[entityLinks.Count - 1].childLinks.AddRange(Utilities.ConsumeArray<EntityConnector>(reader_parallel, NumberOfParams));
                                            break;
                                        }
                                    case CompositeFileData.ENTITY_PARAMETERS:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                            paramRefSets.Add(new CommandsParamRefSet(new ShortGuid(reader_parallel)));
                                            int NumberOfParams = JumpToOffset(reader_parallel);
                                            paramRefSets[paramRefSets.Count - 1].refs.AddRange(Utilities.ConsumeArray<CathodeParameterReference>(reader_parallel, NumberOfParams));
                                            break;
                                        }
                                    case CompositeFileData.ALIASES:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                            AliasEntity overrider = new AliasEntity(new ShortGuid(reader_parallel));
                                            int NumberOfParams = JumpToOffset(reader_parallel);
                                            overrider.alias.path = Utilities.ConsumeArray<ShortGuid>(reader_parallel, NumberOfParams);
                                            composite.aliases.Add(overrider);
                                            break;
                                        }
                                    case CompositeFileData.ALIAS_PATH_HASHES:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                            reader_parallel.BaseStream.Position += 8;
                                            break;
                                        }
                                    case CompositeFileData.VARIABLES:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                            VariableEntity dtEntity = new VariableEntity(new ShortGuid(reader_parallel));
                                            dtEntity.type = (DataType)reader_parallel.ReadUInt32();
                                            dtEntity.name = new ShortGuid(reader_parallel);
                                            composite.variables.Add(dtEntity);
                                            break;
                                        }
                                    case CompositeFileData.PROXIES:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 20);
                                            ProxyEntity thisProxy = new ProxyEntity(new ShortGuid(reader_parallel));
                                            int resetPos = (int)reader_parallel.BaseStream.Position + 8; //TODO: This is a HACK - I need to rework JumpToOffset to make a temp stream
                                            int NumberOfParams = JumpToOffset(reader_parallel);
                                            thisProxy.proxy.path = Utilities.ConsumeArray<ShortGuid>(reader_parallel, NumberOfParams); //Last is always 0x00, 0x00, 0x00, 0x00
                                            reader_parallel.BaseStream.Position = resetPos;
                                            ShortGuid idCheck = new ShortGuid(reader_parallel);
                                            if (idCheck != thisProxy.shortGUID) throw new Exception("Proxy ID mismatch!");
                                            thisProxy.function = new ShortGuid(reader_parallel);
                                            composite.proxies.Add(thisProxy);
                                            break;
                                        }
                                    case CompositeFileData.FUNCTION_ENTITIES:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                            ShortGuid entityID = new ShortGuid(reader_parallel);
                                            ShortGuid functionID = new ShortGuid(reader_parallel);
                                            if (functionID.IsFunctionType)
                                            {
                                                //This entity executes a hard-coded CATHODE function
                                                switch (functionID.AsFunctionType)
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
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 40);

                                            ResourceReference resource = new ResourceReference();
                                            resource.position = new Vector3(reader_parallel.ReadSingle(), reader_parallel.ReadSingle(), reader_parallel.ReadSingle());
                                            resource.rotation = new Vector3(reader_parallel.ReadSingle(), reader_parallel.ReadSingle(), reader_parallel.ReadSingle());
                                            resource.resource_id = new ShortGuid(reader_parallel);
                                            resource.resource_type = (ResourceType)reader_parallel.ReadUInt32();
                                            switch (resource.resource_type)
                                            {
                                                case ResourceType.RENDERABLE_INSTANCE:
                                                    resource.index = reader_parallel.ReadInt32();
                                                    resource.count = reader_parallel.ReadInt32();
                                                    break;
                                                case ResourceType.COLLISION_MAPPING:
                                                    resource.index = reader_parallel.ReadInt32();
                                                    resource.entityID = new ShortGuid(reader_parallel);
                                                    break;
                                                case ResourceType.ANIMATED_MODEL:
                                                case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                                                    resource.index = reader_parallel.ReadInt32();
                                                    reader_parallel.BaseStream.Position += 4;
                                                    break;
                                                default:
                                                    reader_parallel.BaseStream.Position += 8;
                                                    break;
                                            }
                                            resourceRefs.Add(resource);
                                            break;
                                        }
                                    case CompositeFileData.CAGEANIMATION_DATA:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                            reader_parallel.BaseStream.Position = (reader_parallel.ReadInt32() * 4);

                                            CAGEAnimation animEntity = (CAGEAnimation)composite.GetEntityByID(new ShortGuid(reader_parallel));

                                            int headerOffset = reader_parallel.ReadInt32() * 4;
                                            int headerCount = reader_parallel.ReadInt32();
                                            int animationOffset = reader_parallel.ReadInt32() * 4;
                                            int animationCount = reader_parallel.ReadInt32();
                                            int eventOffset = reader_parallel.ReadInt32() * 4;
                                            int eventCount = reader_parallel.ReadInt32();

                                            for (int z = 0; z < headerCount; z++)
                                            {
                                                reader_parallel.BaseStream.Position = headerOffset + (z * 32);

                                                CAGEAnimation.Connection header = new CAGEAnimation.Connection();
                                                header.binding_guid = new ShortGuid(reader_parallel);
                                                header.binding_type = (ObjectType)reader_parallel.ReadUInt32();
                                                header.target_track = new ShortGuid(reader_parallel);
                                                header.target_param = new ShortGuid(reader_parallel);
                                                header.target_param_type = (DataType)reader_parallel.ReadUInt32();
                                                header.target_sub_param = new ShortGuid(reader_parallel);

                                                int hierarchyCount = JumpToOffset(reader_parallel);
                                                header.connectedEntity.path = Utilities.ConsumeArray<ShortGuid>(reader_parallel, hierarchyCount);
                                                animEntity.connections.Add(header);
                                            }

                                            reader_parallel.BaseStream.Position = animationOffset;
                                            int[] newOffset = Utilities.ConsumeArray<int>(reader_parallel, animationCount);
                                            for (int z = 0; z < animationCount; z++)
                                            {
                                                reader_parallel.BaseStream.Position = newOffset[z] * 4;

                                                CAGEAnimation.FloatTrack animation = new CAGEAnimation.FloatTrack();
                                                reader_parallel.BaseStream.Position += 8;
                                                animation.shortGUID = new ShortGuid(reader_parallel);

                                                int keyframeCount = JumpToOffset(reader_parallel);
                                                for (int m = 0; m < keyframeCount; m++)
                                                {
                                                    CAGEAnimation.FloatTrack.Keyframe keyframe = new CAGEAnimation.FloatTrack.Keyframe();
                                                    keyframe.mode = (CAGEAnimation.InterpolationMode)reader_parallel.ReadInt32();
                                                    keyframe.time = reader_parallel.ReadSingle();
                                                    keyframe.value = Utilities.Consume<Vector2>(reader_parallel);
                                                    keyframe.tan_in = Utilities.Consume<Vector2>(reader_parallel);
                                                    keyframe.tan_out = Utilities.Consume<Vector2>(reader_parallel);
                                                    animation.keyframes.Add(keyframe);
                                                }
                                                animEntity.animations.Add(animation);
                                            }

                                            reader_parallel.BaseStream.Position = eventOffset;
                                            int[] newOffset1 = Utilities.ConsumeArray<int>(reader_parallel, eventCount);
                                            for (int z = 0; z < eventCount; z++)
                                            {
                                                reader_parallel.BaseStream.Position = newOffset1[z] * 4;

                                                CAGEAnimation.EventTrack thisParamSet = new CAGEAnimation.EventTrack();
                                                reader_parallel.BaseStream.Position += 8;
                                                thisParamSet.shortGUID = new ShortGuid(reader_parallel);

                                                int keyframeCount = JumpToOffset(reader_parallel);
                                                for (int m = 0; m < keyframeCount; m++)
                                                {
                                                    CAGEAnimation.EventTrack.Keyframe keyframe = new CAGEAnimation.EventTrack.Keyframe();
                                                    keyframe.mode = (CAGEAnimation.InterpolationMode)reader_parallel.ReadInt32();
                                                    keyframe.time = reader_parallel.ReadSingle();
                                                    keyframe.forward = Utilities.Consume<ShortGuid>(reader_parallel);
                                                    keyframe.reverse = Utilities.Consume<ShortGuid>(reader_parallel);
                                                    keyframe.track_type = (CAGEAnimation.TrackType)reader_parallel.ReadInt32();
                                                    keyframe.duration = reader_parallel.ReadSingle();
                                                    thisParamSet.keyframes.Add(keyframe);
                                                }
                                                animEntity.events.Add(thisParamSet);
                                            }
                                            break;
                                        }
                                    case CompositeFileData.TRIGGERSEQUENCE_DATA:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 4);
                                            reader_parallel.BaseStream.Position = (reader_parallel.ReadInt32() * 4);

                                            Entity thisEntity = composite.GetEntityByID(new ShortGuid(reader_parallel));
                                            if (thisEntity.variant == EntityVariant.PROXY)
                                            {
                                                //Logs indicate that these vanilla entities contain no parameters or links, so they're not very valuable. We should implement this anyways though so we can add them ourselves.
                                                Console.WriteLine("WARNING: Skipping load of PROXY TriggerSequence in " + composite.name);
                                                break;
                                            }
                                            TriggerSequence trigEntity = (TriggerSequence)thisEntity;

                                            int triggersOffset = reader_parallel.ReadInt32() * 4;
                                            int triggersCount = reader_parallel.ReadInt32();
                                            int eventsOffset = reader_parallel.ReadInt32() * 4;
                                            int eventsCount = reader_parallel.ReadInt32();

                                            for (int z = 0; z < triggersCount; z++)
                                            {
                                                reader_parallel.BaseStream.Position = triggersOffset + (z * 12);
                                                int hierarchyOffset = reader_parallel.ReadInt32() * 4;
                                                int hierarchyCount = reader_parallel.ReadInt32();

                                                TriggerSequence.SequenceEntry thisTrigger = new TriggerSequence.SequenceEntry();
                                                thisTrigger.timing = reader_parallel.ReadSingle();
                                                reader_parallel.BaseStream.Position = hierarchyOffset;
                                                thisTrigger.connectedEntity.path = Utilities.ConsumeArray<ShortGuid>(reader_parallel, hierarchyCount);
                                                trigEntity.sequence.Add(thisTrigger);
                                            }

                                            for (int z = 0; z < eventsCount; z++)
                                            {
                                                reader_parallel.BaseStream.Position = eventsOffset + (z * 12);

                                                TriggerSequence.MethodEntry thisEvent = new TriggerSequence.MethodEntry();
                                                thisEvent.method = new ShortGuid(reader_parallel);
                                                thisEvent.relay = new ShortGuid(reader_parallel);
                                                thisEvent.finished = new ShortGuid(reader_parallel);
                                                trigEntity.methods.Add(thisEvent);
                                            }
                                            break;
                                        }
                                }
                            }
                        }

                        //Apply connections between entities
                        for (int x = 0; x < entityLinks.Count; x++)
                            composite.GetEntityByID(entityLinks[x].parentID)?.childLinks.AddRange(entityLinks[x].childLinks);

                        //Clone parameter data to entities
                        for (int x = 0; x < paramRefSets.Count; x++)
                        {
                            Entity entToApply = composite.GetEntityByID(paramRefSets[x].id);
                            if (entToApply == null) continue;
                            for (int y = 0; y < paramRefSets[x].refs.Count; y++)
                            {
                                ParameterData data = parameters[paramRefSets[x].refs[y].offset];
                                Parameter param = new Parameter(paramRefSets[x].refs[y].paramID, (ParameterData)data.Clone());
                                entToApply.parameters.Add(param);
                            }
                        }

                        //Remap resource references
                        ShortGuid resParamID = ShortGuidUtils.Generate("resource");
                        ShortGuid physEntID = ShortGuidUtils.Generate("PhysicsSystem");
                        //Check to see if this resource applies to a PARAMETER on an entity
                        for (int x = 0; x < composite.functions.Count; x++)
                        {
                            for (int y = 0; y < composite.functions[x].parameters.Count; y++)
                            {
                                if (composite.functions[x].parameters[y].name != resParamID) continue;

                                cResource resourceParam = (cResource)composite.functions[x].parameters[y].content;
                                resourceParam.value.AddRange(resourceRefs.Where(o => o.resource_id == resourceParam.shortGUID));
                                resourceRefs.RemoveAll(o => o.resource_id == resourceParam.shortGUID);
                            }
                        }
                        //Check to see if this resource applies directly to an ENTITY
                        for (int x = 0; x < composite.functions.Count; x++)
                        {
                            composite.functions[x].resources.AddRange(resourceRefs.Where(o => o.resource_id == composite.functions[x].shortGUID));
                            resourceRefs.RemoveAll(o => o.resource_id == composite.functions[x].shortGUID);
                        }
                        //Any that are left over will be applied to PhysicsSystem entities - really these just exist in the composite, but it's easier for us to track this way
                        if (resourceRefs.Count == 1 && resourceRefs[0].resource_type == ResourceType.DYNAMIC_PHYSICS_SYSTEM)
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
                });

                //Attempt to fixup variable entity types
                /*
                for (int i = 0; i < composites.Length; i++)
                {
                    for (int x = 0; x < composites[i].variables.Count; x++)
                    {
                        if (composites[i].variables[x].type != DataType.NONE) continue;
                        for (int y = 0; y < composites.Length; y++)
                        {
                            List<FunctionEntity> compInstance = composites[y].functions.FindAll(o => o.function == composites[i].shortGUID);
                            for (int z = 0; z < compInstance.Count; z++)
                            {
                                Parameter paramInstance = compInstance[z].parameters.FirstOrDefault(o => o.name == composites[i].variables[x].name);
                                if (paramInstance == null || paramInstance.content == null) continue;
                                if (composites[i].variables[x].type != DataType.NONE && composites[i].variables[x].type != paramInstance.content.dataType)
                                {
                                    throw new Exception("");
                                }
                                composites[i].variables[x].type = paramInstance.content.dataType;
                                Console.WriteLine("Changing DataType of " + composites[i].variables[x].name.ToString() + " to " + paramInstance.content.dataType);
                            }
                        }
                    }
                }
                */

                Entries = composites.ToList<Composite>();
            }
        }

        public static void Write(ShortGuid[] EntryPoints, List<Composite> Entries, out byte[] content)
        {
            ShortGuid SHORTGUID_resource = ShortGuidUtils.Generate("resource");

            //Make sure our composites are in ID order
            Entries = Entries.OrderBy(o => o.shortGUID.AsUInt32).ToList();

            //Work out data to write
            List<ParameterData> parameters = new List<ParameterData>();
            List<Entity>[] linkedEntities = new List<Entity>[Entries.Count];
            List<Entity>[] parameterisedEntities = new List<Entity>[Entries.Count];
            List<AliasEntity>[] reshuffledAliases = new List<AliasEntity>[Entries.Count];
            List<AliasEntity>[] reshuffledAliasPathHashes = new List<AliasEntity>[Entries.Count];
            List<ResourceReference>[] resourceReferences = new List<ResourceReference>[Entries.Count];
            List<CAGEAnimation>[] cageAnimationEntities = new List<CAGEAnimation>[Entries.Count];
            List<TriggerSequence>[] triggerSequenceEntities = new List<TriggerSequence>[Entries.Count];

            Parallel.For(0, Entries.Count, i =>
            {
                List<Entity> ents = Entries[i].GetEntities();
                linkedEntities[i] = new List<Entity>(ents.FindAll(o => o.childLinks.Count != 0)).OrderBy(o => o.shortGUID.AsUInt32).ToList();
                parameterisedEntities[i] = new List<Entity>(ents.FindAll(o => o.parameters.Count != 0)).OrderBy(o => o.shortGUID.AsUInt32).ToList();
                reshuffledAliases[i] = Entries[i].aliases.OrderBy(o => o.shortGUID.AsUInt32).ToList();
                reshuffledAliasPathHashes[i] = Entries[i].aliases.OrderBy(o => o.alias.GeneratePathHash().AsUInt32).ToList();

                cageAnimationEntities[i] = new List<CAGEAnimation>();
                triggerSequenceEntities[i] = new List<TriggerSequence>();
                resourceReferences[i] = new List<ResourceReference>();
                for (int x = 0; x < Entries[i].functions.Count; x++)
                {
                    //If this function is a valid CAGEAnimation or TriggerSequence, remember it
                    if (Entries[i].functions[x].function == FunctionType.CAGEAnimation)
                    {
                        CAGEAnimation thisEntity = (CAGEAnimation)Entries[i].functions[x];
                        if (thisEntity.connections.Count == 0 && thisEntity.animations.Count == 0 && thisEntity.events.Count == 0) continue;
                        cageAnimationEntities[i].Add(thisEntity);
                    }
                    else if (Entries[i].functions[x].function == FunctionType.TriggerSequence)
                    {
                        TriggerSequence thisEntity = (TriggerSequence)Entries[i].functions[x];
                        if (thisEntity.sequence.Count == 0 && thisEntity.methods.Count == 0) continue;
                        triggerSequenceEntities[i].Add(thisEntity);
                    }

                    //Get resources on this function
                    resourceReferences[i].AddRange(Entries[i].functions[x].resources);

                    //If the function contains a resource parameter, grab those too
                    Parameter param = Entries[i].functions[x].GetParameter(SHORTGUID_resource);
                    if (param == null) continue;
                    resourceReferences[i].AddRange(((cResource)param.content).value);
                }
                resourceReferences[i] = resourceReferences[i].Distinct().ToList();
            });

            for (int i = 0; i < Entries.Count; i++)
            {
                List<Entity> ents = Entries[i].GetEntities();
                for (int x = 0; x < ents.Count; x++)
                    for (int y = 0; y < ents[x].parameters.Count; y++)
                        parameters.Add(ents[x].parameters[y].content);
            }
            parameters = parameters.Distinct().ToList();

            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.BaseStream.SetLength(0);

                    //Write entry points
                    for (int i = 0; i < 3; i++)
                    {
                        if (EntryPoints[i].IsInvalid || Entries.FirstOrDefault(o => o != null && o.shortGUID == EntryPoints[i]) == null)
                            writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });
                        else
                            Utilities.Write<ShortGuid>(writer, EntryPoints[i]);
                    }

                    //Write placeholder info for parameter/composite offsets
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(0);

                    #region WRITE_PARAMETERS
                    //Write out parameters & track offsets
                    Dictionary<ParameterData, int> parameterOffsets = new Dictionary<ParameterData, int>(parameters.Count);
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        parameterOffsets.Add(parameters[i], (int)writer.BaseStream.Position / 4);
                        writer.Write((uint)(parameters[i].dataType == DataType.ENUM_STRING ? DataType.STRING : parameters[i].dataType));
                        switch (parameters[i].dataType)
                        {
                            case DataType.TRANSFORM:
                                Vector3 pos = ((cTransform)parameters[i]).position;
                                Vector3 rot = ((cTransform)parameters[i]).rotation;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                                writer.Write(pos.x); writer.Write(pos.y); writer.Write(pos.z);
                                writer.Write(rot.y); writer.Write(rot.x); writer.Write(rot.z);
#else
                                writer.Write(pos.X); writer.Write(pos.Y); writer.Write(pos.Z);
                                writer.Write(rot.Y); writer.Write(rot.X); writer.Write(rot.Z);
#endif
                                break;
                            case DataType.INTEGER:
                                writer.Write(((cInteger)parameters[i]).value);
                                break;
                            case DataType.ENUM_STRING:
                            case DataType.STRING:
                                int stringStart = ((int)writer.BaseStream.Position + 4) / 4;
                                byte[] stringStartRaw = BitConverter.GetBytes(stringStart);
                                stringStartRaw[3] = 0x80;
                                writer.Write(stringStartRaw);
                                string str = ((cString)parameters[i]).value.Replace("\u0092", "'");
                                writer.Write(ShortGuidUtils.Generate(str, false).AsUInt32);
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
                                Utilities.Write<ShortGuid>(writer, ((cResource)parameters[i]).shortGUID);
                                break;
                            case DataType.VECTOR:
                                Vector3 dir = ((cVector3)parameters[i]).value;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                                writer.Write(dir.x); writer.Write(dir.y); writer.Write(dir.z);
#else
                                writer.Write(dir.X); writer.Write(dir.Y); writer.Write(dir.Z);
#endif
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
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                                    writer.Write(thisSpline.splinePoints[x].position.x);
                                    writer.Write(thisSpline.splinePoints[x].position.y);
                                    writer.Write(thisSpline.splinePoints[x].position.z);
                                    writer.Write(thisSpline.splinePoints[x].rotation.y);
                                    writer.Write(thisSpline.splinePoints[x].rotation.x);
                                    writer.Write(thisSpline.splinePoints[x].rotation.z);
#else
                                    writer.Write(thisSpline.splinePoints[x].position.X);
                                    writer.Write(thisSpline.splinePoints[x].position.Y);
                                    writer.Write(thisSpline.splinePoints[x].position.Z);
                                    writer.Write(thisSpline.splinePoints[x].rotation.Y);
                                    writer.Write(thisSpline.splinePoints[x].rotation.X);
                                    writer.Write(thisSpline.splinePoints[x].rotation.Z);
#endif
                                }
                                break;
                        }
                    }
                    #endregion

                    #region WRITE_COMPOSITES
                    //Write out composites in order of their IDs & track offsets
                    int[] compositeOffsets = new int[Entries.Count];
                    for (int i = 0; i < Entries.Count; i++)
                    {
                        int scriptStartPos = (int)writer.BaseStream.Position / 4;

                        Utilities.Write<ShortGuid>(writer, ShortGuidUtils.Generate(Entries[i].name, false));
                        for (int x = 0; x < Entries[i].name.Length; x++) writer.Write(Entries[i].name[x]);
                        writer.Write((char)0x00);
                        Utilities.Align(writer, 4);

                        //Write data
                        OffsetPair[] scriptPointerOffsetInfo = new OffsetPair[(int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS];
                        for (int x = 0; x < (int)CompositeFileData.NUMBER_OF_SCRIPT_BLOCKS; x++)
                        {
                            switch ((CompositeFileData)x)
                            {
                                case CompositeFileData.HEADER:
                                    {
                                        scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, 2);
                                        Utilities.Write<ShortGuid>(writer, Entries[i].shortGUID);
                                        writer.Write(0);
                                        break;
                                    }
                                case CompositeFileData.ENTITY_CONNECTIONS:
                                    {
                                        List<OffsetPair> offsetPairs = new List<OffsetPair>(linkedEntities[i].Count);
                                        foreach (Entity entityWithLink in linkedEntities[i])
                                        {
                                            offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, entityWithLink.childLinks.Count));
                                            Utilities.Write<EntityConnector>(writer, entityWithLink.childLinks);
                                        }

                                        scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, linkedEntities[i].Count);
                                        for (int p = 0; p < linkedEntities[i].Count; p++)
                                        {
                                            writer.Write(linkedEntities[i][p].shortGUID.AsUInt32);
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
                                            List<Parameter> sortedParams = entityWithParam.parameters.OrderBy(o => o.name.AsUInt32).ToList();
                                            for (int y = 0; y < sortedParams.Count; y++)
                                            {
                                                Utilities.Write<ShortGuid>(writer, sortedParams[y].name);
                                                writer.Write(parameterOffsets[sortedParams[y].content]);
                                            }
                                        }

                                        scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, offsetPairs.Count);
                                        for (int p = 0; p < parameterisedEntities[i].Count; p++)
                                        {
                                            writer.Write(parameterisedEntities[i][p].shortGUID.AsUInt32);
                                            writer.Write(offsetPairs[p].GlobalOffset / 4);
                                            writer.Write(offsetPairs[p].EntryCount);
                                        }
                                        break;
                                    }
                                case CompositeFileData.ALIASES:
                                    {
                                        List<OffsetPair> offsetPairs = new List<OffsetPair>(reshuffledAliases[i].Count);
                                        for (int p = 0; p < reshuffledAliases[i].Count; p++)
                                        {
                                            offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, reshuffledAliases[i][p].alias.path.Length));
                                            Utilities.Write<ShortGuid>(writer, reshuffledAliases[i][p].alias.path);
                                        }

                                        scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, reshuffledAliases[i].Count);
                                        for (int p = 0; p < reshuffledAliases[i].Count; p++)
                                        {
                                            writer.Write(reshuffledAliases[i][p].shortGUID.AsUInt32);
                                            writer.Write(offsetPairs[p].GlobalOffset / 4);
                                            writer.Write(offsetPairs[p].EntryCount);
                                        }
                                        break;
                                    }
                                case CompositeFileData.ALIAS_PATH_HASHES:
                                    {
                                        scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, reshuffledAliasPathHashes[i].Count);
                                        for (int p = 0; p < reshuffledAliasPathHashes[i].Count; p++)
                                        {
                                            writer.Write(reshuffledAliasPathHashes[i][p].shortGUID.AsUInt32);
                                            writer.Write(reshuffledAliasPathHashes[i][p].alias.GeneratePathHash().AsUInt32);
                                        }
                                        break;
                                    }
                                case CompositeFileData.VARIABLES:
                                    {
                                        scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, Entries[i].variables.Count);
                                        for (int p = 0; p < Entries[i].variables.Count; p++)
                                        {
                                            writer.Write(Entries[i].variables[p].shortGUID.AsUInt32);
                                            writer.Write((uint)Entries[i].variables[p].type);
                                            writer.Write(Entries[i].variables[p].name.AsUInt32);
                                        }
                                        break;
                                    }
                                case CompositeFileData.PROXIES:
                                    {
                                        List<OffsetPair> offsetPairs = new List<OffsetPair>();
                                        for (int p = 0; p < Entries[i].proxies.Count; p++)
                                        {
                                            offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, Entries[i].proxies[p].proxy.path.Length));
                                            Utilities.Write<ShortGuid>(writer, Entries[i].proxies[p].proxy.path);
                                        }

                                        scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, offsetPairs.Count);
                                        for (int p = 0; p < Entries[i].proxies.Count; p++)
                                        {
                                            writer.Write(Entries[i].proxies[p].shortGUID.AsUInt32);
                                            writer.Write(offsetPairs[p].GlobalOffset / 4);
                                            writer.Write(offsetPairs[p].EntryCount);
                                            writer.Write(Entries[i].proxies[p].shortGUID.AsUInt32);
                                            writer.Write(Entries[i].proxies[p].function.AsUInt32);
                                        }
                                        break;
                                    }
                                case CompositeFileData.FUNCTION_ENTITIES:
                                    {
                                        scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, Entries[i].functions.Count);
                                        for (int p = 0; p < Entries[i].functions.Count; p++)
                                        {
                                            writer.Write(Entries[i].functions[p].shortGUID.AsUInt32);
                                            writer.Write(Entries[i].functions[p].function.AsUInt32);
                                        }
                                        break;
                                    }
                                case CompositeFileData.RESOURCE_REFERENCES:
                                    {
                                        scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, resourceReferences[i].Count);
                                        for (int p = 0; p < resourceReferences[i].Count; p++)
                                        {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                                            writer.Write(resourceReferences[i][p].position.x);
                                            writer.Write(resourceReferences[i][p].position.y);
                                            writer.Write(resourceReferences[i][p].position.z);
                                            writer.Write(resourceReferences[i][p].rotation.x);
                                            writer.Write(resourceReferences[i][p].rotation.y);
                                            writer.Write(resourceReferences[i][p].rotation.z);
#else
                                            writer.Write(resourceReferences[i][p].position.X);
                                            writer.Write(resourceReferences[i][p].position.Y);
                                            writer.Write(resourceReferences[i][p].position.Z);
                                            writer.Write(resourceReferences[i][p].rotation.X);
                                            writer.Write(resourceReferences[i][p].rotation.Y);
                                            writer.Write(resourceReferences[i][p].rotation.Z);
#endif
                                            writer.Write(resourceReferences[i][p].resource_id.AsUInt32); //Sometimes this is the entity ID that uses the resource, other times it's the "resource" parameter ID link
                                            writer.Write((uint)resourceReferences[i][p].resource_type);
                                            switch (resourceReferences[i][p].resource_type)
                                            {
                                                case ResourceType.RENDERABLE_INSTANCE:
                                                    writer.Write(resourceReferences[i][p].index);
                                                    writer.Write(resourceReferences[i][p].count);
                                                    break;
                                                case ResourceType.COLLISION_MAPPING:
                                                    writer.Write(resourceReferences[i][p].index);
                                                    writer.Write(resourceReferences[i][p].entityID.AsUInt32);
                                                    break;
                                                case ResourceType.ANIMATED_MODEL:
                                                case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                                                    writer.Write(resourceReferences[i][p].index);
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
                                            List<int> hierarchyOffsets = new List<int>(cageAnimationEntities[i][p].connections.Count);
                                            for (int pp = 0; pp < cageAnimationEntities[i][p].connections.Count; pp++)
                                            {
                                                hierarchyOffsets.Add((int)writer.BaseStream.Position);
                                                Utilities.Write<ShortGuid>(writer, cageAnimationEntities[i][p].connections[pp].connectedEntity.path);
                                            }

                                            int headerOffset = (int)writer.BaseStream.Position;
                                            for (int pp = 0; pp < cageAnimationEntities[i][p].connections.Count; pp++)
                                            {
                                                CAGEAnimation.Connection header = cageAnimationEntities[i][p].connections[pp];
                                                Utilities.Write(writer, header.binding_guid);
                                                writer.Write((uint)header.binding_type);
                                                Utilities.Write(writer, header.target_track);
                                                Utilities.Write(writer, header.target_param);
                                                writer.Write((uint)header.target_param_type);
                                                Utilities.Write(writer, header.target_sub_param);
                                                writer.Write(hierarchyOffsets[pp] / 4);
                                                writer.Write(header.connectedEntity.path.Length);
                                            }

                                            List<int> internalOffsets = new List<int>(cageAnimationEntities[i][p].animations.Count);
                                            for (int pp = 0; pp < cageAnimationEntities[i][p].animations.Count; pp++)
                                            {
                                                int toPointTo = (int)writer.BaseStream.Position;
                                                cageAnimationEntities[i][p].animations[pp].keyframes = cageAnimationEntities[i][p].animations[pp].keyframes.OrderBy(o => o.time).ToList();
                                                for (int ppp = 0; ppp < cageAnimationEntities[i][p].animations[pp].keyframes.Count; ppp++)
                                                {
                                                    CAGEAnimation.FloatTrack.Keyframe key = cageAnimationEntities[i][p].animations[pp].keyframes[ppp];
                                                    writer.Write((Int32)key.mode);
                                                    writer.Write(key.time);
                                                    Utilities.Write<Vector2>(writer, key.value);
                                                    Utilities.Write<Vector2>(writer, key.tan_in);
                                                    Utilities.Write<Vector2>(writer, key.tan_out);
                                                }

                                                internalOffsets.Add(((int)writer.BaseStream.Position) / 4);

                                                float minSeconds = 0;
                                                float maxSeconds = 0;
                                                if (cageAnimationEntities[i][p].animations[pp].keyframes.Count != 0)
                                                {
                                                    minSeconds = cageAnimationEntities[i][p].animations[pp].keyframes[0].time;
                                                    maxSeconds = cageAnimationEntities[i][p].animations[pp].keyframes[cageAnimationEntities[i][p].animations[pp].keyframes.Count - 1].time;
                                                }
                                                writer.Write(minSeconds);
                                                writer.Write(maxSeconds);

                                                Utilities.Write(writer, cageAnimationEntities[i][p].animations[pp].shortGUID);

                                                writer.Write(toPointTo / 4);
                                                writer.Write(cageAnimationEntities[i][p].animations[pp].keyframes.Count);
                                            }

                                            int animationOffset = (int)writer.BaseStream.Position;
                                            Utilities.Write<int>(writer, internalOffsets);

                                            internalOffsets = new List<int>(cageAnimationEntities[i][p].events.Count);
                                            for (int pp = 0; pp < cageAnimationEntities[i][p].events.Count; pp++)
                                            {
                                                int toPointTo = (int)writer.BaseStream.Position;
                                                List<CAGEAnimation.Connection> keyframeRefs = cageAnimationEntities[i][p].connections.FindAll(o => o.target_track == cageAnimationEntities[i][p].events[pp].shortGUID);
                                                cageAnimationEntities[i][p].events[pp].keyframes = cageAnimationEntities[i][p].events[pp].keyframes.OrderBy(o => o.time).ToList();
                                                for (int ppp = 0; ppp < cageAnimationEntities[i][p].events[pp].keyframes.Count; ppp++)
                                                {
                                                    CAGEAnimation.EventTrack.Keyframe key = cageAnimationEntities[i][p].events[pp].keyframes[ppp];
                                                    writer.Write((Int32)key.mode);
                                                    writer.Write(key.time);
                                                    Utilities.Write<ShortGuid>(writer, key.forward);
                                                    Utilities.Write<ShortGuid>(writer, key.reverse);
                                                    writer.Write((Int32)key.track_type);
                                                    writer.Write(key.duration);
                                                }

                                                internalOffsets.Add(((int)writer.BaseStream.Position) / 4);

                                                float minSeconds = 0;
                                                float maxSeconds = 0;
                                                if (cageAnimationEntities[i][p].events[pp].keyframes.Count != 0)
                                                {
                                                    minSeconds = cageAnimationEntities[i][p].events[pp].keyframes[0].time;
                                                    maxSeconds = cageAnimationEntities[i][p].events[pp].keyframes[cageAnimationEntities[i][p].events[pp].keyframes.Count - 1].time;
                                                }
                                                writer.Write(minSeconds);
                                                writer.Write(maxSeconds);

                                                Utilities.Write(writer, cageAnimationEntities[i][p].events[pp].shortGUID);

                                                writer.Write(toPointTo / 4);
                                                writer.Write(cageAnimationEntities[i][p].events[pp].keyframes.Count);
                                            }

                                            int eventOffset = (int)writer.BaseStream.Position;
                                            Utilities.Write<int>(writer, internalOffsets);

                                            globalOffsets.Add((int)writer.BaseStream.Position);
                                            writer.Write(cageAnimationEntities[i][p].shortGUID.AsUInt32);
                                            writer.Write(headerOffset / 4);
                                            writer.Write(cageAnimationEntities[i][p].connections.Count);
                                            writer.Write(animationOffset / 4);
                                            writer.Write(cageAnimationEntities[i][p].animations.Count);
                                            writer.Write(eventOffset / 4);
                                            writer.Write(cageAnimationEntities[i][p].events.Count);
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
                                            List<int> hierarchyOffsets = new List<int>(triggerSequenceEntities[i][p].sequence.Count);
                                            for (int pp = 0; pp < triggerSequenceEntities[i][p].sequence.Count; pp++)
                                            {
                                                hierarchyOffsets.Add((int)writer.BaseStream.Position);
                                                Utilities.Write<ShortGuid>(writer, triggerSequenceEntities[i][p].sequence[pp].connectedEntity.path);
                                            }

                                            int triggerOffset = (int)writer.BaseStream.Position;
                                            for (int pp = 0; pp < triggerSequenceEntities[i][p].sequence.Count; pp++)
                                            {
                                                writer.Write(hierarchyOffsets[pp] / 4);
                                                writer.Write(triggerSequenceEntities[i][p].sequence[pp].connectedEntity.path.Length);
                                                writer.Write(triggerSequenceEntities[i][p].sequence[pp].timing);
                                            }

                                            int eventOffset = (int)writer.BaseStream.Position;
                                            for (int pp = 0; pp < triggerSequenceEntities[i][p].methods.Count; pp++)
                                            {
                                                writer.Write(triggerSequenceEntities[i][p].methods[pp].method.AsUInt32);
                                                writer.Write(triggerSequenceEntities[i][p].methods[pp].relay.AsUInt32);
                                                writer.Write(triggerSequenceEntities[i][p].methods[pp].finished.AsUInt32);
                                            }

                                            globalOffsets.Add((int)writer.BaseStream.Position);
                                            writer.Write(triggerSequenceEntities[i][p].shortGUID.AsUInt32);
                                            writer.Write(triggerOffset / 4);
                                            writer.Write(triggerSequenceEntities[i][p].sequence.Count);
                                            writer.Write(eventOffset / 4);
                                            writer.Write(triggerSequenceEntities[i][p].methods.Count);
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
                            if (x == 0) Utilities.Write<ShortGuid>(writer, Entries[i].shortGUID);
                        }

                        //Write function count (TODO: sometimes this count excludes some entities in the vanilla paks - why?)
                        writer.Write(Entries[i].functions.FindAll(o => o.function.IsFunctionType).Count);
                        writer.Write(Entries[i].functions.Count);
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
                    writer.Write(Entries.Count);
                }
                content = stream.ToArray();
            }
        }

        /* Read offset info & count, jump to the offset & return the count */
        private static int JumpToOffset(BinaryReader reader)
        {
            int offset = reader.ReadInt32() * 4;
            int count = reader.ReadInt32();

            reader.BaseStream.Position = offset;
            return count;
        }

        #region STRUCTURES
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct CathodeParameterReference
        {
            public ShortGuid paramID; //The ID of the param in the entity
            public int offset;    //The offset of the param this reference points to (in memory this is *4)
        }

        private class CommandsEntityLinks
        {
            public ShortGuid parentID;
            public List<EntityConnector> childLinks = new List<EntityConnector>();

            public CommandsEntityLinks(ShortGuid _id)
            {
                parentID = _id;
            }
        }

        private class CommandsParamRefSet
        {
            public ShortGuid id;
            public List<CathodeParameterReference> refs = new List<CathodeParameterReference>();

            public CommandsParamRefSet(ShortGuid _id)
            {
                id = _id;
            }
        }
        #endregion
    }
}
