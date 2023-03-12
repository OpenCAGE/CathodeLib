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
using System.Threading.Tasks;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/COMMANDS.PAK */
    public class Commands : CathodeFile
    {
        public List<Composite> Entries = new List<Composite>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public Commands(string path) : base(path) { }

        // This is always:
        //  - Root Instance (the map's entry composite, usually containing entities that call mission/environment composites)
        //  - Global Instance (the main data handler for keeping track of mission number, etc - kinda like a big singleton)
        //  - Pause Menu Instance
        private ShortGuid[] _entryPoints = null;
        private Composite[] _entryPointObjects = null;

        #region FILE_IO
        override protected bool LoadInternal()
        {
            byte[] content = File.ReadAllBytes(_filepath);

            using (BinaryReader reader = new BinaryReader(new MemoryStream(content)))
            {
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
#if DO_PRETTY_COMPOSITES
                    string prettyPath = CompositePathDB.GetPrettyPathForComposite(composite.shortGUID);
                    if (prettyPath != "") composite.name = prettyPath;
#endif
                        Utilities.Align(reader_parallel, 4);

                        //Pull data from those offsets
                        List<CommandsEntityLinks> entityLinks = new List<CommandsEntityLinks>();
                        List<CommandsParamRefSet> paramRefSets = new List<CommandsParamRefSet>();
                        List<ResourceReference> resourceRefs = new List<ResourceReference>();
                        Dictionary<ShortGuid, ShortGuid> overrideChecksums = new Dictionary<ShortGuid, ShortGuid>();
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
                                            entityLinks[entityLinks.Count - 1].childLinks.AddRange(Utilities.ConsumeArray<EntityLink>(reader_parallel, NumberOfParams));
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
                                    case CompositeFileData.ENTITY_OVERRIDES:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                            OverrideEntity overrider = new OverrideEntity(new ShortGuid(reader_parallel));
                                            int NumberOfParams = JumpToOffset(reader_parallel);
                                            overrider.hierarchy.AddRange(Utilities.ConsumeArray<ShortGuid>(reader_parallel, NumberOfParams));
                                            composite.overrides.Add(overrider);
                                            break;
                                        }
                                    case CompositeFileData.ENTITY_OVERRIDES_CHECKSUM:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                            overrideChecksums.Add(new ShortGuid(reader_parallel), new ShortGuid(reader_parallel));
                                            break;
                                        }
                                    case CompositeFileData.COMPOSITE_EXPOSED_PARAMETERS:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 12);
                                            VariableEntity dtEntity = new VariableEntity(new ShortGuid(reader_parallel));
                                            dtEntity.type = CommandsUtils.GetDataType(new ShortGuid(reader_parallel));
                                            dtEntity.name = new ShortGuid(reader_parallel);
                                            composite.variables.Add(dtEntity);
                                            break;
                                        }
                                    case CompositeFileData.ENTITY_PROXIES:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 20);
                                            ProxyEntity thisProxy = new ProxyEntity(new ShortGuid(reader_parallel));
                                            int resetPos = (int)reader_parallel.BaseStream.Position + 8; //TODO: This is a HACK - I need to rework JumpToOffset to make a temp stream
                                            int NumberOfParams = JumpToOffset(reader_parallel);
                                            thisProxy.hierarchy.AddRange(Utilities.ConsumeArray<ShortGuid>(reader_parallel, NumberOfParams)); //Last is always 0x00, 0x00, 0x00, 0x00
                                            reader_parallel.BaseStream.Position = resetPos;
                                            ShortGuid idCheck = new ShortGuid(reader_parallel);
                                            if (idCheck != thisProxy.shortGUID) throw new Exception("Proxy ID mismatch!");
                                            thisProxy.targetType = new ShortGuid(reader_parallel);
                                            composite.proxies.Add(thisProxy);
                                            break;
                                        }
                                    case CompositeFileData.ENTITY_FUNCTIONS:
                                        {
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 8);
                                            ShortGuid entityID = new ShortGuid(reader_parallel);
                                            ShortGuid functionID = new ShortGuid(reader_parallel);
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
                                            reader_parallel.BaseStream.Position = (offsetPairs[x].GlobalOffset * 4) + (y * 40);

                                            ResourceReference resource = new ResourceReference();
                                            resource.position = new Vector3(reader_parallel.ReadSingle(), reader_parallel.ReadSingle(), reader_parallel.ReadSingle());
                                            resource.rotation = new Vector3(reader_parallel.ReadSingle(), reader_parallel.ReadSingle(), reader_parallel.ReadSingle());
                                            resource.resourceID = new ShortGuid(reader_parallel);
                                            resource.entryType = CommandsUtils.GetResourceEntryType(reader_parallel.ReadBytes(4));
                                            switch (resource.entryType)
                                            {
                                                case ResourceType.RENDERABLE_INSTANCE:
                                                    resource.startIndex = reader_parallel.ReadInt32();
                                                    resource.count = reader_parallel.ReadInt32();
                                                    break;
                                                case ResourceType.COLLISION_MAPPING:
                                                    resource.startIndex = reader_parallel.ReadInt32();
                                                    resource.collisionID = new ShortGuid(reader_parallel);
                                                    break;
                                                case ResourceType.ANIMATED_MODEL:
                                                case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                                                    resource.startIndex = reader_parallel.ReadInt32();
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

                                            int keyframeHeaderOffset = reader_parallel.ReadInt32() * 4;
                                            int numberOfKeyframeHeaders = reader_parallel.ReadInt32();
                                            int keyframeDataOffset = reader_parallel.ReadInt32() * 4;
                                            int numberOfKeyframeDataEntries = reader_parallel.ReadInt32();
                                            int OffsetToFindParams3 = reader_parallel.ReadInt32() * 4;
                                            int NumberOfParams3 = reader_parallel.ReadInt32();

                                            for (int z = 0; z < numberOfKeyframeHeaders; z++)
                                            {
                                                reader_parallel.BaseStream.Position = keyframeHeaderOffset + (z * 32);

                                                CAGEAnimation.Header thisHeader = new CAGEAnimation.Header();
                                                thisHeader.shortGUID = new ShortGuid(reader_parallel);//ID
                                                thisHeader.objectType = CommandsUtils.GetObjectType(new ShortGuid(reader_parallel));
                                                thisHeader.keyframeID = new ShortGuid(reader_parallel);
                                                thisHeader.parameterID = new ShortGuid(reader_parallel);
                                                thisHeader.parameterDataType = CommandsUtils.GetDataType(new ShortGuid(reader_parallel));
                                                thisHeader.parameterSubID = new ShortGuid(reader_parallel);

                                                int hierarchyCount = JumpToOffset(reader_parallel);
                                                thisHeader.connectedEntity = Utilities.ConsumeArray<ShortGuid>(reader_parallel, hierarchyCount).ToList<ShortGuid>();
                                                animEntity.keyframeHeaders.Add(thisHeader);
                                            }

                                            reader_parallel.BaseStream.Position = keyframeDataOffset;
                                            int[] newOffset = Utilities.ConsumeArray<int>(reader_parallel, numberOfKeyframeDataEntries);
                                            for (int z = 0; z < numberOfKeyframeDataEntries; z++)
                                            {
                                                reader_parallel.BaseStream.Position = newOffset[z] * 4;

                                                CAGEAnimation.Keyframe thisParamKey = new CAGEAnimation.Keyframe();
                                                thisParamKey.minSeconds = reader_parallel.ReadSingle();
                                                thisParamKey.maxSeconds = reader_parallel.ReadSingle(); 
                                                thisParamKey.shortGUID = new ShortGuid(reader_parallel);

                                                int numberOfKeyframes = JumpToOffset(reader_parallel);
                                                for (int m = 0; m < numberOfKeyframes; m++)
                                                {
                                                    CAGEAnimation.Keyframe.Data thisKeyframe = new CAGEAnimation.Keyframe.Data();
                                                    reader_parallel.BaseStream.Position += 4;
                                                    thisKeyframe.secondsSinceStart = reader_parallel.ReadSingle(); 
                                                    reader_parallel.BaseStream.Position += 4;
                                                    thisKeyframe.paramValue = reader_parallel.ReadSingle(); 
                                                    thisKeyframe.unk2 = reader_parallel.ReadSingle();
                                                    thisKeyframe.unk3 = reader_parallel.ReadSingle();
                                                    thisKeyframe.unk4 = reader_parallel.ReadSingle();
                                                    thisKeyframe.unk5 = reader_parallel.ReadSingle();
                                                    thisParamKey.keyframes.Add(thisKeyframe);
                                                }
                                                animEntity.keyframeData.Add(thisParamKey);
                                            }

                                            //UNKNOWN - is this maybe event triggers?
                                            reader_parallel.BaseStream.Position = OffsetToFindParams3;
                                            int[] newOffset1 = Utilities.ConsumeArray<int>(reader_parallel, NumberOfParams3);
                                            for (int z = 0; z < NumberOfParams3; z++)
                                            {
                                                reader_parallel.BaseStream.Position = newOffset1[z] * 4;

                                                CAGEAnimation.Keyframe2 thisParamSet = new CAGEAnimation.Keyframe2();
                                                thisParamSet.minSeconds = reader_parallel.ReadSingle();
                                                thisParamSet.maxSeconds = reader_parallel.ReadSingle();
                                                thisParamSet.shortGUID = new ShortGuid(reader_parallel); 

                                                int NumberOfParams3_ = JumpToOffset(reader_parallel);
                                                for (int m = 0; m < NumberOfParams3_; m++)
                                                {
                                                    CAGEAnimation.Keyframe2.Data thisInnerSet = new CAGEAnimation.Keyframe2.Data();
                                                    reader_parallel.BaseStream.Position += 4;
                                                    thisInnerSet.secondsSinceStart = reader_parallel.ReadSingle(); 
                                                    thisInnerSet.unk2 = Utilities.Consume<ShortGuid>(reader_parallel);
                                                    thisInnerSet.unk3 = Utilities.Consume<ShortGuid>(reader_parallel);
                                                    thisInnerSet.unk4 = reader_parallel.ReadInt32();
                                                    reader_parallel.BaseStream.Position += 4;
                                                    thisParamSet.keyframes.Add(thisInnerSet);
                                                }
                                                animEntity.keyframeData2.Add(thisParamSet);
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
                                                break; // We don't handle this just yet... need to resolve the proxy.
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

                                                TriggerSequence.Entity thisTrigger = new TriggerSequence.Entity();
                                                thisTrigger.timing = reader_parallel.ReadSingle();
                                                reader_parallel.BaseStream.Position = hierarchyOffset;
                                                thisTrigger.hierarchy = Utilities.ConsumeArray<ShortGuid>(reader_parallel, hierarchyCount).ToList<ShortGuid>();
                                                trigEntity.entities.Add(thisTrigger);
                                            }

                                            for (int z = 0; z < eventsCount; z++)
                                            {
                                                reader_parallel.BaseStream.Position = eventsOffset + (z * 12);

                                                TriggerSequence.Event thisEvent = new TriggerSequence.Event();
                                                thisEvent.start = new ShortGuid(reader_parallel);
                                                thisEvent.shortGUID = new ShortGuid(reader_parallel);
                                                thisEvent.end = new ShortGuid(reader_parallel);
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
                                resourceParam.value.AddRange(resourceRefs.Where(o => o.resourceID == resourceParam.shortGUID));
                                resourceRefs.RemoveAll(o => o.resourceID == resourceParam.shortGUID);
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

            //TODO: DOING THIS CAUSES THE NEW-DOOR-CREATION ISSUE WITH EXISTING DOORS
            //      DOING THE INVERSE CAUSES A CRASH
            /*
            foreach (Composite comp in Entries)
            {
                foreach (FunctionEntity ent in comp.functions)
                {
                    if (ent.resources.Count != 0)
                    {
                        cResource res = new cResource();
                        res.value.AddRange(ent.resources);
                        foreach (ResourceReference resRef in res.value) resRef.resourceID = res.shortGUID;
                        Parameter param = ent.AddParameter("resource", res);
                        ent.resources.Clear();
                    }
                }
            }*/

            return true;
        }

        override protected bool SaveInternal()
        {
            //Validate entry points and composite count
            if (Entries.Count == 0) return false;
            if (_entryPoints == null) _entryPoints = new ShortGuid[3];
            if (_entryPoints[0].val == null && _entryPoints[1].val == null && _entryPoints[2].val == null && Entries.Count == 0) return false;

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
                _entryPoints[0] = Entries[0].shortGUID;
            }
            RefreshEntryPointObjects();

            //Fix (& verify) entity-attached resource info
            Parallel.For(0, Entries.Count, i =>
            {
                Parallel.For(0, Entries[i].functions.Count, x =>
                {
                    if (CommandsUtils.FunctionTypeExists(Entries[i].functions[x].function))
                    {
                        FunctionType type = CommandsUtils.GetFunctionType(Entries[i].functions[x].function);
                        switch (type)
                        {
                            case FunctionType.TRAV_1ShotClimbUnder:
                            case FunctionType.TRAV_1ShotFloorVentEntrance:
                            case FunctionType.TRAV_1ShotFloorVentExit:
                            case FunctionType.TRAV_1ShotLeap:
                            case FunctionType.TRAV_1ShotSpline:
                            case FunctionType.TRAV_1ShotVentEntrance:
                            case FunctionType.TRAV_1ShotVentExit:
                            case FunctionType.TRAV_ContinuousBalanceBeam:
                            case FunctionType.TRAV_ContinuousCinematicSidle:
                            case FunctionType.TRAV_ContinuousClimbingWall:
                            case FunctionType.TRAV_ContinuousLadder:
                            case FunctionType.TRAV_ContinuousLedge:
                            case FunctionType.TRAV_ContinuousPipe:
                            case FunctionType.TRAV_ContinuousTightGap:
                                Entries[i].functions[x].AddResource(ResourceType.TRAVERSAL_SEGMENT);
                                break;
                            case FunctionType.NavMeshBarrier:
                                Entries[i].functions[x].AddResource(ResourceType.NAV_MESH_BARRIER_RESOURCE);
                                break;
                            case FunctionType.ExclusiveMaster:
                                Entries[i].functions[x].AddResource(ResourceType.EXCLUSIVE_MASTER_STATE_RESOURCE);
                                break;

                            case FunctionType.PhysicsSystem:
                                Parameter dps_index = Entries[i].functions[x].GetParameter("system_index");
                                if (dps_index == null)
                                {
                                    dps_index = new Parameter("system_index", new cInteger(0));
                                    Entries[i].functions[x].parameters.Add(dps_index);
                                }
                                Entries[i].functions[x].AddResource(ResourceType.DYNAMIC_PHYSICS_SYSTEM).startIndex = ((cInteger)dps_index.content).value;
                                break;
                            case FunctionType.EnvironmentModelReference:
                                Parameter rsc = Entries[i].functions[x].GetParameter("resource");
                                if (rsc == null)
                                {
                                    rsc = new Parameter("resource", new cResource(Entries[i].functions[x].shortGUID));
                                    Entries[i].functions[x].parameters.Add(rsc);
                                }
                                cResource rsc_p = (cResource)rsc.content;
                                rsc_p.AddResource(ResourceType.ANIMATED_MODEL);
                                break;
                            case FunctionType.ModelReference:
                                Parameter mdl = Entries[i].functions[x].GetParameter("resource");
                                if (mdl == null)
                                {
                                    mdl = new Parameter("resource", new cResource(Entries[i].functions[x].shortGUID));
                                    Entries[i].functions[x].parameters.Add(mdl);
                                }
                                break;
                        }
                    }
                });
            });

            //Make sure our composites are in ID order
            Entries = Entries.OrderBy(o => o.shortGUID.ToUInt32()).ToList();
            #endregion

            #region WORK_OUT_WHAT_TO_WRITE
            //Remember some ShortGuid vals here to save re-calling them every time
            ShortGuid SHORTGUID_CAGEAnimation = CommandsUtils.GetFunctionTypeGUID(FunctionType.CAGEAnimation);
            ShortGuid SHORTGUID_TriggerSequence = CommandsUtils.GetFunctionTypeGUID(FunctionType.TriggerSequence);
            ShortGuid SHORTGUID_resource = ShortGuidUtils.Generate("resource");

            //Work out data to write
            List<ParameterData> parameters = new List<ParameterData>();
            List<Entity>[] linkedEntities = new List<Entity>[Entries.Count];
            List<Entity>[] parameterisedEntities = new List<Entity>[Entries.Count];
            List<OverrideEntity>[] reshuffledChecksums = new List<OverrideEntity>[Entries.Count];
            List<ResourceReference>[] resourceReferences = new List<ResourceReference>[Entries.Count];
            List<CAGEAnimation>[] cageAnimationEntities = new List<CAGEAnimation>[Entries.Count];
            List<TriggerSequence>[] triggerSequenceEntities = new List<TriggerSequence>[Entries.Count];

            Parallel.For(0, Entries.Count, i => 
            {
                List<Entity> ents = Entries[i].GetEntities();
                linkedEntities[i] = new List<Entity>(ents.FindAll(o => o.childLinks.Count != 0)).OrderBy(o => o.shortGUID.ToUInt32()).ToList();
                parameterisedEntities[i] = new List<Entity>(ents.FindAll(o => o.parameters.Count != 0)).OrderBy(o => o.shortGUID.ToUInt32()).ToList();
                reshuffledChecksums[i] = Entries[i].overrides.OrderBy(o => o.checksum.ToUInt32()).ToList();

                cageAnimationEntities[i] = new List<CAGEAnimation>();
                triggerSequenceEntities[i] = new List<TriggerSequence>();
                resourceReferences[i] = new List<ResourceReference>();
                for (int x = 0; x < Entries[i].functions.Count; x++)
                {
                    //If this function is a valid CAGEAnimation or TriggerSequence, remember it
                    if (Entries[i].functions[x].function == SHORTGUID_CAGEAnimation)
                    {
                        CAGEAnimation thisEntity = (CAGEAnimation)Entries[i].functions[x];
                        if (thisEntity.keyframeHeaders.Count == 0 && thisEntity.keyframeData.Count == 0 && thisEntity.keyframeData2.Count == 0) continue;
                        cageAnimationEntities[i].Add(thisEntity);
                    }
                    else if (Entries[i].functions[x].function == SHORTGUID_TriggerSequence)
                    {
                        TriggerSequence thisEntity = (TriggerSequence)Entries[i].functions[x];
                        if (thisEntity.entities.Count == 0 && thisEntity.events.Count == 0) continue;
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
            #endregion

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
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
                int[] parameterOffsets = new int[parameters.Count];
                for (int i = 0; i < parameters.Count; i++)
                {
                    parameterOffsets[i] = (int)writer.BaseStream.Position / 4;
                    Utilities.Write<ShortGuid>(writer, CommandsUtils.GetDataTypeGUID(parameters[i].dataType));
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
                                //todo: is this YXZ
                                writer.Write(thisSpline.splinePoints[x].rotation.x);
                                writer.Write(thisSpline.splinePoints[x].rotation.y);
                                writer.Write(thisSpline.splinePoints[x].rotation.z);
#else
                                writer.Write(thisSpline.splinePoints[x].position.X);
                                writer.Write(thisSpline.splinePoints[x].position.Y);
                                writer.Write(thisSpline.splinePoints[x].position.Z);
                                //todo: is this YXZ
                                writer.Write(thisSpline.splinePoints[x].rotation.X);
                                writer.Write(thisSpline.splinePoints[x].rotation.Y);
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

                    Utilities.Write<ShortGuid>(writer, ShortGuidUtils.Generate(Entries[i].name));
                    for (int x = 0; x < Entries[i].name.Length; x++) writer.Write(Entries[i].name[x]);
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
                                        List<Parameter> sortedParams = entityWithParam.parameters.OrderBy(o => o.name.ToUInt32()).ToList();
                                        for (int y = 0; y < sortedParams.Count; y++)
                                        {
                                            Utilities.Write<ShortGuid>(writer, sortedParams[y].name);
                                            writer.Write(GetParameterOffset(ref parameterOffsets, ref parameters, ref sortedParams[y].content));
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
                                    List<OffsetPair> offsetPairs = new List<OffsetPair>(Entries[i].overrides.Count);
                                    for (int p = 0; p < Entries[i].overrides.Count; p++)
                                    {
                                        offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, Entries[i].overrides[p].hierarchy.Count));
                                        Utilities.Write<ShortGuid>(writer, Entries[i].overrides[p].hierarchy);
                                    }

                                    scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, Entries[i].overrides.Count);
                                    for (int p = 0; p < Entries[i].overrides.Count; p++)
                                    {
                                        writer.Write(Entries[i].overrides[p].shortGUID.val);
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
                                    scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, Entries[i].variables.Count);
                                    for (int p = 0; p < Entries[i].variables.Count; p++)
                                    {
                                        writer.Write(Entries[i].variables[p].shortGUID.val);
                                        writer.Write(CommandsUtils.GetDataTypeGUID(Entries[i].variables[p].type).val);
                                        writer.Write(Entries[i].variables[p].name.val);
                                    }
                                    break;
                                }
                            case CompositeFileData.ENTITY_PROXIES:
                                {
                                    List<OffsetPair> offsetPairs = new List<OffsetPair>();
                                    for (int p = 0; p < Entries[i].proxies.Count; p++)
                                    {
                                        offsetPairs.Add(new OffsetPair(writer.BaseStream.Position, Entries[i].proxies[p].hierarchy.Count));
                                        Utilities.Write<ShortGuid>(writer, Entries[i].proxies[p].hierarchy);
                                    }

                                    scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, offsetPairs.Count);
                                    for (int p = 0; p < Entries[i].proxies.Count; p++)
                                    {
                                        writer.Write(Entries[i].proxies[p].shortGUID.val);
                                        writer.Write(offsetPairs[p].GlobalOffset / 4);
                                        writer.Write(offsetPairs[p].EntryCount);
                                        writer.Write(Entries[i].proxies[p].shortGUID.val);
                                        writer.Write(Entries[i].proxies[p].targetType.val);
                                    }
                                    break;
                                }
                            case CompositeFileData.ENTITY_FUNCTIONS:
                                {
                                    scriptPointerOffsetInfo[x] = new OffsetPair(writer.BaseStream.Position, Entries[i].functions.Count);
                                    for (int p = 0; p < Entries[i].functions.Count; p++)
                                    {
                                        writer.Write(Entries[i].functions[p].shortGUID.val);
                                        writer.Write(Entries[i].functions[p].function.val);
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
                                            Utilities.Write(writer, cageAnimationEntities[i][p].keyframeHeaders[pp].shortGUID);
                                            Utilities.Write(writer, CommandsUtils.GetObjectTypeGUID(cageAnimationEntities[i][p].keyframeHeaders[pp].objectType));
                                            Utilities.Write(writer, cageAnimationEntities[i][p].keyframeHeaders[pp].keyframeID);
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
                                                writer.Write((Int32)2);
                                                writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].secondsSinceStart);
                                                writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].secondsSinceStart);
                                                writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].paramValue);
                                                //writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].unk2);
                                                //writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].unk3);
                                                //writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].unk4);
                                                //writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes[ppp].unk5);
                                            }

                                            internalOffsets.Add(((int)writer.BaseStream.Position) / 4);

                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].minSeconds);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].maxSeconds);
                                            Utilities.Write(writer, cageAnimationEntities[i][p].keyframeData[pp].shortGUID);

                                            writer.Write(toPointTo / 4);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData[pp].keyframes.Count);
                                        }

                                        int paramData2Offset = (int)writer.BaseStream.Position;
                                        Utilities.Write<int>(writer, internalOffsets);

                                        internalOffsets = new List<int>(cageAnimationEntities[i][p].keyframeData2.Count);
                                        for (int pp = 0; pp < cageAnimationEntities[i][p].keyframeData2.Count; pp++)
                                        {
                                            int toPointTo = (int)writer.BaseStream.Position;
                                            for (int ppp = 0; ppp < cageAnimationEntities[i][p].keyframeData2[pp].keyframes.Count; ppp++)
                                            {
                                                writer.Write((Int32)1);
                                                writer.Write(cageAnimationEntities[i][p].keyframeData2[pp].keyframes[ppp].secondsSinceStart);
                                                //writer.Write(cageAnimationEntities[i][p].keyframeData2[pp].keyframes[ppp].unk2);
                                                //writer.Write(cageAnimationEntities[i][p].keyframeData2[pp].keyframes[ppp].unk3);
                                                //writer.Write(cageAnimationEntities[i][p].keyframeData2[pp].keyframes[ppp].unk4);
                                                writer.Write((Int32)0);
                                            }

                                            internalOffsets.Add(((int)writer.BaseStream.Position) / 4);

                                            writer.Write(cageAnimationEntities[i][p].keyframeData2[pp].minSeconds);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData2[pp].maxSeconds);
                                            Utilities.Write(writer, cageAnimationEntities[i][p].keyframeData2[pp].shortGUID);

                                            writer.Write(toPointTo / 4);
                                            writer.Write(cageAnimationEntities[i][p].keyframeData2[pp].keyframes.Count);
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
                                        writer.Write(cageAnimationEntities[i][p].keyframeData2.Count);
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
                                        List<int> hierarchyOffsets = new List<int>(triggerSequenceEntities[i][p].entities.Count);
                                        for (int pp = 0; pp < triggerSequenceEntities[i][p].entities.Count; pp++)
                                        {
                                            hierarchyOffsets.Add((int)writer.BaseStream.Position);
                                            Utilities.Write<ShortGuid>(writer, triggerSequenceEntities[i][p].entities[pp].hierarchy);
                                        }

                                        int triggerOffset = (int)writer.BaseStream.Position;
                                        for (int pp = 0; pp < triggerSequenceEntities[i][p].entities.Count; pp++)
                                        {
                                            writer.Write(hierarchyOffsets[pp] / 4);
                                            writer.Write(triggerSequenceEntities[i][p].entities[pp].hierarchy.Count);
                                            writer.Write(triggerSequenceEntities[i][p].entities[pp].timing);
                                        }

                                        int eventOffset = (int)writer.BaseStream.Position;
                                        for (int pp = 0; pp < triggerSequenceEntities[i][p].events.Count; pp++)
                                        {
                                            writer.Write(triggerSequenceEntities[i][p].events[pp].start.val);
                                            writer.Write(triggerSequenceEntities[i][p].events[pp].shortGUID.val);
                                            writer.Write(triggerSequenceEntities[i][p].events[pp].end.val);
                                        }

                                        globalOffsets.Add((int)writer.BaseStream.Position);
                                        writer.Write(triggerSequenceEntities[i][p].shortGUID.val);
                                        writer.Write(triggerOffset / 4);
                                        writer.Write(triggerSequenceEntities[i][p].entities.Count);
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
                        if (x == 0) Utilities.Write<ShortGuid>(writer, Entries[i].shortGUID);
                    }

                    //Write function count (TODO: sometimes this count excludes some entities in the vanilla paks - why?)
                    writer.Write(Entries[i].functions.FindAll(o => CommandsUtils.FunctionTypeExists(o.function)).Count);
                    writer.Write(Entries[i].functions.Count);
                }
                #endregion

                //Write out parameter offsets
                int parameterOffsetPos = (int)writer.BaseStream.Position;
                Utilities.Write<int>(writer, parameterOffsets);

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
            return true;
        }
        #endregion

        #region ACCESSORS
        /* Add a new composite */
        public Composite AddComposite(string name, bool isRoot = false)
        {
            Composite comp = new Composite(name);
            Entries.Add(comp);
            if (isRoot) SetRootComposite(comp);
            return comp;
        }

        /* Return a list of filenames for composites in the CommandsPAK archive */
        public string[] GetCompositeNames()
        {
            string[] toReturn = new string[Entries.Count];
            for (int i = 0; i < Entries.Count; i++) toReturn[i] = Entries[i].name;
            return toReturn;
        }

        /* Get an individual composite */
        public Composite GetComposite(string name)
        {
            return Entries.FirstOrDefault(o => o.name == name || o.name == name.Replace('/', '\\'));
        }
        public Composite GetComposite(ShortGuid id)
        {
            if (id.val == null) return null;
            return Entries.FirstOrDefault(o => o.shortGUID == id);
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
        private int JumpToOffset(BinaryReader reader)
        {
            int offset = reader.ReadInt32() * 4;
            int count = reader.ReadInt32();

            reader.BaseStream.Position = offset;
            return count;
        }

        /* Get the offset for the parameter data in the overarching write list */
        private int GetParameterOffset(ref int[] offsets, ref List<ParameterData> parameters, ref ParameterData parameter)
        {
            for (int i = 0; i < parameters.Count; i++)
            {
                if (parameters[i] == parameter)
                    return offsets[i];
            }
            throw new Exception("Failed to lookup parameter in parameters list, could not find offset!");
        }

        /* Refresh the composite pointers for our entry points */
        private void RefreshEntryPointObjects()
        {
            _entryPointObjects = new Composite[_entryPoints.Length];
            for (int i = 0; i < _entryPoints.Length; i++) _entryPointObjects[i] = GetComposite(_entryPoints[i]);
        }

        /* Test Stuff */
        private void IsParameterReferencedAnywhere(ShortGuid id)
        {
            Parallel.ForEach(Entries, comp =>
            {
                Parallel.ForEach(comp.functions, func =>
                {
                    Parallel.ForEach(func.parameters, param =>
                    {
                        if (param.name == id)
                            Console.WriteLine("[" + id + "] " + comp.name + " -> " + EntityUtils.GetName(comp.shortGUID, func.shortGUID) + " -> " + param.name + " [" + param.content.dataType + "]");
                    });
                });
            });
        }
        #endregion

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
            public List<EntityLink> childLinks = new List<EntityLink>();

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