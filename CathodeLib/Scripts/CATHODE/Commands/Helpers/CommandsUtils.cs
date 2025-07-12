using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using static CathodeLib.CompositeModificationInfoTable;
using System.IO;




#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Scripting
{
    public class CommandsUtils
    {
        //NOTE: This list is exposed publicly, because it is up to your app to manage it.
        public CompositePurgeTable PurgedComposites => _compPurges;

        private CompositePurgeTable _compPurges = new CompositePurgeTable();
        private EntityNameTable _entityNames = new EntityNameTable();
        private CompositeModificationInfoTable _modificationInfo = new CompositeModificationInfoTable();
        private CompositePinInfoTable _pinInfo = new CompositePinInfoTable();

        private static uint _nameID; //We remove the "name" param on every entity except Zone, since that is handled by EntityUtils.

        private Commands _commands = null;

        public CommandsUtils(Commands commands)
        {
            _commands = commands;

            _commands.OnLoadSuccess += LoadInfo;
            _commands.OnSaveSuccess += SaveInfo;

            if (_commands.Loaded)
                LoadInfo(_commands.Filepath);

            _nameID = ShortGuidUtils.Generate("name").AsUInt32;

            ShortGuidUtils.LinkCommands(_commands);
        }

        ~CommandsUtils()
        {
            ShortGuidUtils.UnlinkCommands(_commands);
        }

        #region Generic Utility Functions
        /* Gets the composite that contains the entity */
        public Composite GetContainedComposite(Entity entity)
        {
            for (int i = 0; i < _commands.Entries.Count; i++)
            {
                switch (entity.variant)
                {
                    case EntityVariant.FUNCTION:
                        for (int x = 0; x < _commands.Entries[i].functions.Count; x++)
                        {
                            if (_commands.Entries[i].functions[x].shortGUID == entity.shortGUID)
                            {
                                if (_commands.Entries[i].functions[x] == entity)
                                    return _commands.Entries[i];
                            }
                        }
                        break;
                    case EntityVariant.VARIABLE:
                        for (int x = 0; x < _commands.Entries[i].variables.Count; x++)
                        {
                            if (_commands.Entries[i].variables[x].shortGUID == entity.shortGUID)
                            {
                                if (_commands.Entries[i].variables[x] == entity)
                                    return _commands.Entries[i];
                            }    
                        }
                        break;
                    case EntityVariant.PROXY:
                        for (int x = 0; x < _commands.Entries[i].proxies.Count; x++)
                        {
                            if (_commands.Entries[i].proxies[x].shortGUID == entity.shortGUID)
                            {
                                if (_commands.Entries[i].proxies[x] == entity)
                                    return _commands.Entries[i];
                            }
                        }
                        break;
                    case EntityVariant.ALIAS:
                        for (int x = 0; x < _commands.Entries[i].aliases.Count; x++)
                        {
                            if (_commands.Entries[i].aliases[x].shortGUID == entity.shortGUID)
                            {
                                if (_commands.Entries[i].aliases[x] == entity)
                                    return _commands.Entries[i];
                            }
                        }
                        break;
                }
            }
            return null;
        }

        /* Resolve an entity hierarchy */
        public Entity ResolveHierarchy(Composite composite, ShortGuid[] hierarchy, out Composite containedComposite, out string asString, bool includeShortGuids = true)
        {
            if (hierarchy.Length == 0)
            {
                containedComposite = null;
                asString = "";
                return null;
            }

            List<ShortGuid> hierarchyCopy = hierarchy.ToList();
        
            Composite currentFlowgraphToSearch = composite;
            if (currentFlowgraphToSearch == null || currentFlowgraphToSearch.GetEntityByID(hierarchyCopy[0]) == null)
            {
                currentFlowgraphToSearch = _commands.EntryPoints[0];
                if (currentFlowgraphToSearch == null || currentFlowgraphToSearch.GetEntityByID(hierarchyCopy[0]) == null)
                {
                    currentFlowgraphToSearch = _commands.GetComposite(hierarchyCopy[0]);
                    if (currentFlowgraphToSearch == null || currentFlowgraphToSearch.GetEntityByID(hierarchyCopy[1]) == null)
                    {
                        containedComposite = null;
                        asString = "";
                        return null;
                    }
                    hierarchyCopy.RemoveAt(0);
                }
            }
        
            Entity entity = null;
            string hierarchyString = "";
            for (int i = 0; i < hierarchyCopy.Count; i++)
            {
                entity = currentFlowgraphToSearch.GetEntityByID(hierarchyCopy[i]);
        
                if (entity == null) break;
                if (includeShortGuids) hierarchyString += "[" + entity.shortGUID.ToByteString() + "] ";
                hierarchyString += GetEntityName(currentFlowgraphToSearch.shortGUID, entity.shortGUID);
                if (i >= hierarchyCopy.Count - 2) break; //Last is always 00-00-00-00
                hierarchyString += " -> ";
        
                if (entity.variant == EntityVariant.FUNCTION)
                {
                    Composite flowRef = _commands.GetComposite(((FunctionEntity)entity).function);
                    if (flowRef != null)
                    {
                        currentFlowgraphToSearch = flowRef;
                    }
                    else
                    {
                        entity = null;
                        break;
                    }
                }
            }
            containedComposite = (entity == null) ? null : currentFlowgraphToSearch;
            asString = hierarchyString;
            return entity;
        }

        /* Calculate an instanced entity's worldspace position & rotation */
        public (Vector3, Quaternion) CalculateInstancedPosition(EntityPath hierarchy)
        {
            cTransform globalTransform = new cTransform();
            Composite comp = _commands.EntryPoints[0];
            for (int x = 0; x < hierarchy.path.Length; x++)
            {
                FunctionEntity compInst = comp.functions.FirstOrDefault(o => o.shortGUID == hierarchy.path[x]);
                if (compInst == null)
                    break;

                Parameter positionParam = compInst.GetParameter("position");
                if (positionParam != null && positionParam.content != null && positionParam.content.dataType == DataType.TRANSFORM)
                    globalTransform += (cTransform)positionParam.content;

                comp = _commands.GetComposite(compInst.function);
                if (comp == null)
                    break;
            }
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            return (globalTransform.position, Quaternion.Euler(globalTransform.rotation.x, globalTransform.rotation.y, globalTransform.rotation.z));
#else
            return (globalTransform.position, Quaternion.CreateFromYawPitchRoll(globalTransform.rotation.Y * (float)Math.PI / 180.0f, globalTransform.rotation.X * (float)Math.PI / 180.0f, globalTransform.rotation.Z * (float)Math.PI / 180.0f));
#endif
        }

        /* CA's CAGE doesn't properly tidy up hierarchies pointing to deleted entities - so we can do that to save confusion */
        public bool PurgeDeadLinks(Composite composite, bool force = false)
        {
            if (!force && _compPurges.purged.Contains(composite.shortGUID))
            {
                //Console.WriteLine("Skipping purge, as this composite is listed within the purged table.");
                return false;
            }

            int originalUnknownCount = 0;
            int originalProxyCount = 0;
            int originalAliasCount = 0;
            int newTriggerCount = 0;
            int originalTriggerCount = 0;
            int newAnimCount = 0;
            int originalAnimCount = 0;
            int newLinkCount = 0;
            int originalLinkCount = 0;
            int originalFuncCount = 0;

            //Clear functions
            List<FunctionEntity> functionsPurged = new List<FunctionEntity>();
            for (int i = 0; i < composite.functions.Count; i++)
                if (composite.functions[i].function.IsFunctionType || _commands.GetComposite(composite.functions[i].function) != null)
                    functionsPurged.Add(composite.functions[i]);
            originalFuncCount = composite.functions.Count;
            composite.functions = functionsPurged;

            //Clear aliases
            List<AliasEntity> aliasesPurged = new List<AliasEntity>();
            for (int i = 0; i < composite.aliases.Count; i++)
                if (ResolveHierarchy(composite, composite.aliases[i].alias.path, out Composite flowTemp, out string hierarchy) != null)
                    aliasesPurged.Add(composite.aliases[i]);
            originalAliasCount = composite.aliases.Count;
            composite.aliases = aliasesPurged;

            //Clear proxies
            List<ProxyEntity> proxyPurged = new List<ProxyEntity>();
            for (int i = 0; i < composite.proxies.Count; i++)
                if (ResolveHierarchy(composite, composite.proxies[i].proxy.path, out Composite flowTemp, out string hierarchy) != null)
                    proxyPurged.Add(composite.proxies[i]);
            originalProxyCount = composite.proxies.Count;
            composite.proxies = proxyPurged;

            //Clear TriggerSequence and CAGEAnimation entities
            for (int i = 0; i < composite.functions.Count; i++)
            {
                //TODO: will this also clear up TriggerSequence/CAGEAnimation data for proxies?
                switch (ShortGuidUtils.FindString(composite.functions[i].function))
                {
                    case "TriggerSequence":
                        TriggerSequence trig = (TriggerSequence)composite.functions[i];
                        List<TriggerSequence.SequenceEntry> trigSeq = new List<TriggerSequence.SequenceEntry>();
                        for (int x = 0; x < trig.sequence.Count; x++)
                            if (ResolveHierarchy(composite, trig.sequence[x].connectedEntity.path, out Composite flowTemp, out string hierarchy) != null)
                                trigSeq.Add(trig.sequence[x]);
                        originalTriggerCount += trig.sequence.Count;
                        newTriggerCount += trigSeq.Count;
                        trig.sequence = trigSeq;
                        break;
                    case "CAGEAnimation":
                        CAGEAnimation anim = (CAGEAnimation)composite.functions[i];
                        List<CAGEAnimation.Connection> headers = new List<CAGEAnimation.Connection>();
                        for (int x = 0; x < anim.connections.Count; x++)
                        {
                            List<CAGEAnimation.FloatTrack> anim_target = anim.animations.FindAll(o => o.shortGUID == anim.connections[x].target_track);
                            List<CAGEAnimation.EventTrack> event_target = anim.events.FindAll(o => o.shortGUID == anim.connections[x].target_track);
                            if (!(anim_target.Count == 0 && event_target.Count == 0) &&
                                ResolveHierarchy(composite, anim.connections[x].connectedEntity.path, out Composite flowTemp, out string hierarchy) != null)
                                headers.Add(anim.connections[x]);
                        }
                        originalAnimCount += anim.connections.Count;
                        newAnimCount += headers.Count;
                        anim.connections = headers;
                        break;
                }
            }

            //Clear links 
            List<Entity> entities = composite.GetEntities();
            for (int i = 0; i < entities.Count; i++)
            {
                List<EntityConnector> childLinksPurged = new List<EntityConnector>();
                for (int x = 0; x < entities[i].childLinks.Count; x++)
                    if (composite.GetEntityByID(entities[i].childLinks[x].linkedEntityID) != null)
                        childLinksPurged.Add(entities[i].childLinks[x]);
                originalLinkCount += entities[i].childLinks.Count;
                newLinkCount += childLinksPurged.Count;
                entities[i].childLinks = childLinksPurged;
            }

            if (originalUnknownCount +
                (originalFuncCount - composite.functions.Count) +
                (originalProxyCount - composite.proxies.Count) +
                (originalAliasCount - composite.aliases.Count) +
                (originalTriggerCount - newTriggerCount) +
                (originalAnimCount - newAnimCount) +
                (originalLinkCount - newLinkCount) == 0)
            {
                //Console.WriteLine("Purge found nothing to clear up.");
                return true;
            }

            Console.WriteLine(
                "Purged all dead hierarchies and entities in " + composite.name + "!" +
                "\n - " + originalUnknownCount + " unknown entities" +
                "\n - " + (originalFuncCount - composite.functions.Count) + " functions (of " + originalFuncCount + ")" +
                "\n - " + (originalProxyCount - composite.proxies.Count) + " proxies (of " + originalProxyCount + ")" +
                "\n - " + (originalAliasCount - composite.aliases.Count) + " aliases (of " + originalAliasCount + ")" +
                "\n - " + (originalTriggerCount - newTriggerCount) + " triggers (of " + originalTriggerCount + ")" +
                "\n - " + (originalAnimCount - newAnimCount) + " anim connections (of " + originalAnimCount + ")" +
                "\n - " + (originalLinkCount - newLinkCount) + " entity links (of " + originalLinkCount + ")");
            return true;
        }

        /* Remove all links between Entities within the Composite */
        public void ClearAllLinks(Composite composite)
        {
            composite.GetEntities().ForEach(o => o.childLinks.Clear());
        }

        /* Count the number of links in the Composite */
        public int CountLinks(Composite composite)
        {
            int count = 0;
            List<Entity> entities = composite.GetEntities();
            foreach (Entity ent in entities)
                count += ent.childLinks.Count;
            return count;
        }
        #endregion

        #region Parameter Utils
        /* Get the inherited function type for a given function type (returns null if it doesn't inherit) */
        public FunctionType? GetInheritedFunction(FunctionType function)
        {
            return CustomTable.Vanilla.CathodeEntities.FunctionBaseClasses[function];
        }

        /* Add all parameters to a given entity with default values (NOTE: you only need to pass in composite if Entity is an Alias or Variable, otherwise feel free to pass null) */
        public void AddAllDefaultParameters(Entity entity, Composite composite, bool overwrite = true, ParameterVariant variants = ParameterVariant.STATE_PARAMETER | ParameterVariant.INPUT_PIN | ParameterVariant.PARAMETER, bool includeInherited = true)
        {
            switch (entity.variant)
            {
                case EntityVariant.VARIABLE:
                    ApplyDefaultVariable((VariableEntity)entity, entity, composite, variants, overwrite);
                    break;
                case EntityVariant.FUNCTION:
                    ApplyDefaultFunction((FunctionEntity)entity, entity, composite, variants, overwrite, includeInherited);
                    break;
                case EntityVariant.PROXY:
                    {
                        Entity proxiedEntity = ((ProxyEntity)entity).proxy.GetPointedEntity(_commands, out Composite proxiedComposite);
                        if (includeInherited)
                            ApplyDefaults(proxiedEntity, entity, overwrite, variants, FunctionType.ProxyInterface);
                        if (proxiedEntity != null && proxiedComposite != null)
                        {
                            switch (proxiedEntity.variant)
                            {
                                case EntityVariant.VARIABLE:
                                    ApplyDefaultVariable((VariableEntity)proxiedEntity, entity, proxiedComposite, variants, overwrite);
                                    break;
                                case EntityVariant.FUNCTION:
                                    ApplyDefaultFunction((FunctionEntity)proxiedEntity, entity, proxiedComposite, variants, overwrite, includeInherited);
                                    break;
                                default:
                                    throw new Exception("Unexpected!"); //we can't proxy to proxies or aliases
                            }
                        }
                    }
                    break;
                case EntityVariant.ALIAS:
                    {
                        Entity aliasedEntity = ((AliasEntity)entity).alias.GetPointedEntity(_commands, composite, out Composite aliasedComposite);
                        if (aliasedEntity != null && aliasedComposite != null)
                        {
                            switch (aliasedEntity.variant)
                            {
                                case EntityVariant.VARIABLE:
                                    ApplyDefaultVariable((VariableEntity)aliasedEntity, entity, aliasedComposite, variants, overwrite);
                                    break;
                                case EntityVariant.FUNCTION:
                                    ApplyDefaultFunction((FunctionEntity)aliasedEntity, entity, aliasedComposite, variants, overwrite, includeInherited);
                                    break;
                                case EntityVariant.PROXY:
                                    if (includeInherited)
                                        ApplyDefaults(aliasedEntity, entity, overwrite, variants, FunctionType.ProxyInterface);
                                    Entity proxiedEntity = ((ProxyEntity)aliasedEntity).proxy.GetPointedEntity(_commands, out Composite proxiedComposite);
                                    if (proxiedEntity != null && proxiedComposite != null)
                                    {
                                        switch (proxiedEntity.variant)
                                        {
                                            case EntityVariant.VARIABLE:
                                                ApplyDefaultVariable((VariableEntity)proxiedEntity, entity, proxiedComposite, variants, overwrite);
                                                break;
                                            case EntityVariant.FUNCTION:
                                                ApplyDefaultFunction((FunctionEntity)proxiedEntity, entity, proxiedComposite, variants, overwrite, includeInherited);
                                                break;
                                            default:
                                                throw new Exception("Unexpected!"); //we can't proxy to proxies or aliases
                                        }
                                    }
                                    break;
                                default:
                                    throw new Exception("Unexpected!"); //we can't alias to aliases
                            }
                        }
                    }
                    break;
            }
        }
        private void ApplyDefaults(Entity baseEntity, Entity targetEntity, bool overwrite, ParameterVariant variants, FunctionType function)
        {
            List<(ShortGuid, ParameterVariant, DataType)> parameters = GetAllParameters(function);
            foreach ((ShortGuid guid, ParameterVariant variant, DataType type) in parameters)
            {
                if (!variants.HasFlag(variant)) continue;
                ParameterData defaultValue = baseEntity.GetParameter(guid)?.content;
                targetEntity.AddParameter(guid, defaultValue != null ? defaultValue : CreateDefaultParameterData(function, guid, variant), variant, overwrite);
            }
        }
        private void ApplyDefaultVariable(VariableEntity baseEntity, Entity targetEntity, Composite composite, ParameterVariant variants, bool overwrite)
        {
            var pinInfo = _commands.Utils.GetParameterInfo(composite, baseEntity);
            ParameterData defaultValue = baseEntity.GetParameter(baseEntity.name)?.content;
            if (defaultValue != null)
            {
                if (pinInfo == null)
                {
                    if (variants.HasFlag(ParameterVariant.PARAMETER))
                        targetEntity.AddParameter(baseEntity.name, defaultValue, ParameterVariant.PARAMETER, overwrite);
                }
                else
                {
                    CompositePinType pinType = (CompositePinType)pinInfo.PinTypeGUID.AsUInt32;
                    ParameterVariant paramVariant = _commands.Utils.PinTypeToParameterVariant(pinType);
                    switch (pinType)
                    {
                        case CompositePinType.CompositeMethodPin:
                            if (variants.HasFlag(ParameterVariant.METHOD_PIN) || variants.HasFlag(ParameterVariant.METHOD_FUNCTION))
                                targetEntity.AddParameter(baseEntity.name, defaultValue, paramVariant, overwrite);
                            break;
                        default:
                            if (variants.HasFlag(paramVariant))
                                targetEntity.AddParameter(baseEntity.name, defaultValue, paramVariant, overwrite);
                            break;
                    }
                }
            }
            else
            {
                if (pinInfo == null)
                {
                    if (variants.HasFlag(ParameterVariant.PARAMETER))
                        targetEntity.AddParameter(baseEntity.name, baseEntity.type, ParameterVariant.PARAMETER, overwrite);
                }
                else
                {
                    CompositePinType pinType = (CompositePinType)pinInfo.PinTypeGUID.AsUInt32;
                    ParameterVariant paramVariant = _commands.Utils.PinTypeToParameterVariant(pinType);
                    switch (pinType)
                    {
                        case CompositePinType.CompositeMethodPin:
                            if (variants.HasFlag(ParameterVariant.METHOD_PIN) || variants.HasFlag(ParameterVariant.METHOD_FUNCTION))
                                targetEntity.AddParameter(baseEntity.name, baseEntity.type, paramVariant, overwrite);
                            break;
                        default:
                            if (variants.HasFlag(paramVariant))
                                targetEntity.AddParameter(baseEntity.name, baseEntity.type, paramVariant, overwrite);
                            break;
                    }
                }
            }
        }
        private void ApplyDefaultFunction(FunctionEntity baseEntity, Entity targetEntity, Composite composite, ParameterVariant variants, bool overwrite, bool includeInherited)
        {
            if (baseEntity.function.IsFunctionType)
            {
                FunctionType? functionType = baseEntity.function.AsFunctionType;
                while (true)
                {
                    ApplyDefaults(baseEntity, targetEntity, overwrite, variants, functionType.Value);
                    if (!includeInherited) break;
                    functionType = GetInheritedFunction(functionType.Value);
                    if (functionType == null) break;
                }
            }
            else
            {
                FunctionType? functionType = FunctionType.CompositeInterface;
                while (true)
                {
                    ApplyDefaults(baseEntity, targetEntity, overwrite, variants, functionType.Value);
                    if (!includeInherited) break;
                    functionType = GetInheritedFunction(functionType.Value);
                    if (functionType == null) break;
                }
                Composite compositeInstance = _commands.GetComposite((baseEntity).function);
                foreach (VariableEntity variable in compositeInstance.variables)
                {
                    ApplyDefaultVariable(variable, targetEntity, compositeInstance, variants, overwrite);
                }
            }
        }

        /* Get all possible parameters for a given entity */
        public List<(ShortGuid, ParameterVariant, DataType)> GetAllParameters(Entity entity, Composite composite, bool includeInherited = true)
        {
            List<(ShortGuid, ParameterVariant, DataType)> parameters = new List<(ShortGuid, ParameterVariant, DataType)>();
            switch (entity.variant)
            {
                case EntityVariant.FUNCTION:
                    FunctionEntity functionEntity = (FunctionEntity)entity;
                    if (functionEntity.function.IsFunctionType)
                    {
                        FunctionType? functionType = functionEntity.function.AsFunctionType;
                        while (true)
                        {
                            parameters.AddRange(GetAllParameters(functionType.Value));
                            if (!includeInherited) break;
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                    }
                    else
                    {
                        if (includeInherited)
                        {
                            FunctionType? functionType = FunctionType.CompositeInterface;
                            while (true)
                            {
                                parameters.AddRange(GetAllParameters(functionType.Value));
                                if (!includeInherited) break;
                                functionType = GetInheritedFunction(functionType.Value);
                                if (functionType == null) break;
                            }
                        }
                        Composite compositeInstance = _commands.GetComposite((functionEntity).function);
                        foreach (VariableEntity variable in compositeInstance.variables)
                        {
                            parameters.AddRange(GetAllParameters(variable, compositeInstance, includeInherited));
                        }
                    }
                    break;
                case EntityVariant.PROXY:
                    if (includeInherited)
                    {
                        FunctionType? functionType = FunctionType.ProxyInterface;
                        while (true)
                        {
                            parameters.AddRange(GetAllParameters(functionType.Value));
                            if (!includeInherited) break;
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                    }
                    ProxyEntity proxyEntity = (ProxyEntity)entity;
                    Entity proxiedEntity = proxyEntity.proxy.GetPointedEntity(_commands);
                    if (proxiedEntity != null)
                        parameters.AddRange(GetAllParameters(proxiedEntity, composite));
                    break;
                case EntityVariant.ALIAS:
                    AliasEntity aliasEntity = (AliasEntity)entity;
                    Entity aliasedEntity = aliasEntity.alias.GetPointedEntity(_commands, composite);
                    if (aliasedEntity != null)
                        parameters.AddRange(GetAllParameters(aliasedEntity, composite));
                    break;
                case EntityVariant.VARIABLE:
                    VariableEntity variableEntity = (VariableEntity)entity;
                    CompositePinInfoTable.PinInfo info = _commands.Utils.GetParameterInfo(composite, variableEntity);
                    if (info == null)
                        parameters.Add((variableEntity.name, ParameterVariant.PARAMETER, variableEntity.type));
                    else
                    {
                        parameters.Add((variableEntity.name, _commands.Utils.PinTypeToParameterVariant(info.PinTypeGUID), variableEntity.type));
                    }

                    break;
            }
            return parameters;
        }

        /* Get all possible parameters for a given function type (not including inherited) */
        public List<(ShortGuid, ParameterVariant, DataType)> GetAllParameters(FunctionType function)
        {
            List<(ShortGuid, ParameterVariant, DataType)> parameters = new List<(ShortGuid, ParameterVariant, DataType)>();
            using (BinaryReader reader = new BinaryReader(new MemoryStream(CustomTable.Vanilla.CathodeEntities.content)))
            {
                Dictionary<ParameterVariant, int> offsets = CustomTable.Vanilla.CathodeEntities.FunctionVariantOffsets[function];
                foreach (KeyValuePair<ParameterVariant, int> entry in offsets)
                {
                    reader.BaseStream.Position = entry.Value;
                    int paramCount = reader.ReadInt32();
                    for (int i = 0; i < paramCount; i++)
                    {
                        uint paramID = reader.ReadUInt32();
                        switch (entry.Key)
                        {
                            case ParameterVariant.REFERENCE_PIN:
                            case ParameterVariant.METHOD_FUNCTION:
                            case ParameterVariant.METHOD_PIN:
                                parameters.Add((new ShortGuid(paramID), entry.Key, DataType.FLOAT));
                                break;
                            default:
                                DataType dataType = IntToDatatype(reader.ReadInt32());
                                if (!(function != FunctionType.Zone && paramID == _nameID))
                                {
                                    if (dataType == DataType.NONE) //This only applies to TARGET_PIN, sometimes it has a value, other times it doesn't. If it doesn't, fall back to FLOAT for now.
                                        dataType = DataType.FLOAT;
                                    parameters.Add((new ShortGuid(paramID), entry.Key, dataType));
                                }
                                if (entry.Key == ParameterVariant.TARGET_PIN) continue; //TargetPin can have a type, but doesn't have data.
                                switch (dataType)
                                {
                                    case DataType.BOOL:
                                        reader.BaseStream.Position += 1;
                                        break;
                                    case DataType.INTEGER:
                                        reader.BaseStream.Position += 4;
                                        break;
                                    case DataType.FLOAT:
                                        reader.BaseStream.Position += 4;
                                        break;
                                    case DataType.STRING:
                                    case DataType.FILEPATH:
                                        {
                                            int seek = reader.ReadByte();
                                            reader.BaseStream.Position += seek;
                                        }
                                        break;
                                    case DataType.ENUM:
                                        {
                                            int enumType = reader.ReadInt32();
                                            if (enumType != -1)
                                            {
                                                reader.BaseStream.Position += 4;
                                            }
                                        }
                                        break;
                                    case DataType.ENUM_STRING:
                                        {
                                            int seek = reader.ReadByte();
                                            reader.BaseStream.Position += seek;
                                            seek = reader.ReadByte();
                                            reader.BaseStream.Position += seek;
                                        }
                                        break;
                                    case DataType.VECTOR:
                                        reader.BaseStream.Position += 12;
                                        break;
                                    case DataType.RESOURCE:
                                        reader.BaseStream.Position += 4;
                                        break;
                                    default:
                                        //Any other types have no default values.
                                        break;
                                }
                                break;
                        }
                    }
                }
            }
            return parameters;
        }

        /* Get metadata about a parameter on an entity: variant, type, and function/composite that implements (if applicable) */
        public (ParameterVariant?, DataType?, ShortGuid) GetParameterMetadata(Entity entity, string parameter, Composite composite)
        {
            return GetParameterMetadata(entity, ShortGuidUtils.Generate(parameter), composite);
        }
        public (ParameterVariant?, DataType?, ShortGuid) GetParameterMetadata(Entity entity, ShortGuid parameter, Composite composite)
        {
            switch (entity.variant)
            {
                case EntityVariant.VARIABLE:
                    if (parameter == ((VariableEntity)entity).name)
                        return (ParameterVariant.PARAMETER, ((VariableEntity)entity).type, ShortGuid.Invalid);
                    break;
                case EntityVariant.FUNCTION:
                    FunctionEntity functionEntity = (FunctionEntity)entity;
                    if (functionEntity.function.IsFunctionType)
                    {
                        FunctionType? functionType = (FunctionType)functionEntity.function.AsUInt32;
                        while (true)
                        {
                            var metadata = GetParameterMetadata(functionType.Value, parameter);
                            if (metadata.Item1 != null)
                                return (metadata.Item1, metadata.Item2, metadata.Item3 == null ? ShortGuid.Invalid : new ShortGuid((UInt32)metadata.Item3));
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                    }
                    else
                    {
                        FunctionType? functionType = FunctionType.CompositeInterface;
                        while (true)
                        {
                            var metadata = GetParameterMetadata(functionType.Value, parameter);
                            if (metadata.Item1 != null)
                                return (metadata.Item1, metadata.Item2, metadata.Item3 == null ? ShortGuid.Invalid : new ShortGuid((UInt32)metadata.Item3));
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                        Composite compositeInstance = _commands.GetComposite(functionEntity.function);
                        if (compositeInstance != null)
                        {
                            VariableEntity var = compositeInstance.variables.FirstOrDefault(o => o.name == parameter);
                            if (var != null)
                            {
                                CompositePinInfoTable.PinInfo info = _commands.Utils.GetParameterInfo(compositeInstance, var);
                                if (info == null)
                                    return (ParameterVariant.PARAMETER, var.type, compositeInstance.shortGUID);
                                else
                                {
                                    return (_commands.Utils.PinTypeToParameterVariant(info.PinTypeGUID), var.type, compositeInstance.shortGUID);
                                }
                            }
                        }
                    }
                    break;
                case EntityVariant.PROXY:
                    {
                        FunctionType? functionType = FunctionType.ProxyInterface;
                        while (true)
                        {
                            var metadata = GetParameterMetadata(functionType.Value, parameter);
                            if (metadata.Item1 != null)
                                return (metadata.Item1, metadata.Item2, metadata.Item3 == null ? ShortGuid.Invalid : new ShortGuid((UInt32)metadata.Item3));
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                        ProxyEntity proxyEntity = (ProxyEntity)entity;
                        Entity proxiedEntity = proxyEntity.proxy.GetPointedEntity(_commands);
                        if (proxiedEntity != null)
                            return GetParameterMetadata(proxiedEntity, parameter, composite);
                        break;
                    }
                case EntityVariant.ALIAS:
                    AliasEntity aliasEntity = (AliasEntity)entity;
                    Entity aliasedEntity = aliasEntity.alias.GetPointedEntity(_commands, composite);
                    if (aliasedEntity != null)
                        return GetParameterMetadata(aliasedEntity, parameter, composite);
                    break;
            }
            return (null, null, ShortGuid.Invalid);
        }

        /* Get metadata about a parameter on a function: variant, type, and function that implements */
        public (ParameterVariant?, DataType?, FunctionType?) GetParameterMetadata(FunctionType function, string parameter)
        {
            return GetParameterMetadata(function, ShortGuidUtils.Generate(parameter));
        }
        public (ParameterVariant?, DataType?, FunctionType?) GetParameterMetadata(FunctionType function, ShortGuid parameter)
        {
            Dictionary<ParameterVariant, int> offsets = CustomTable.Vanilla.CathodeEntities.FunctionVariantOffsets[function];
            foreach (KeyValuePair<ParameterVariant, int> entry in offsets)
            {
                (ParameterVariant?, DataType?, FunctionType?) data = GetParameterMetadata(function, parameter, entry.Key);
                if (data.Item1 != null && data.Item2 != null && data.Item3 != null)
                    return data;
            }
            return (null, null, null);
        }
        public (ParameterVariant?, DataType?, FunctionType?) GetParameterMetadata(FunctionType function, ShortGuid parameter, ParameterVariant variant)
        {
            List<(ShortGuid, ParameterVariant, DataType)> parameters = new List<(ShortGuid, ParameterVariant, DataType)>();
            using (BinaryReader reader = new BinaryReader(new MemoryStream(CustomTable.Vanilla.CathodeEntities.content)))
            {
                reader.BaseStream.Position = CustomTable.Vanilla.CathodeEntities.FunctionVariantOffsets[function][variant];
                int paramCount = reader.ReadInt32();
                for (int i = 0; i < paramCount; i++)
                {
                    uint paramID = reader.ReadUInt32();
                    bool isCorrectParam = paramID == parameter.AsUInt32;
                    switch (variant)
                    {
                        case ParameterVariant.REFERENCE_PIN:
                        case ParameterVariant.METHOD_FUNCTION:
                        case ParameterVariant.METHOD_PIN:
                            if (isCorrectParam)
                                return (variant, DataType.FLOAT, function);
                            break;
                        default:
                            DataType dataType = IntToDatatype(reader.ReadInt32());
                            if (dataType == DataType.NONE) //This only applies to TARGET_PIN, sometimes it has a value, other times it doesn't. If it doesn't, fall back to FLOAT for now.
                                dataType = DataType.FLOAT;
                            if (isCorrectParam)
                                return (variant, dataType, function);
                            if (variant == ParameterVariant.TARGET_PIN) continue; //TargetPin can have a type, but doesn't have data.
                            switch (dataType)
                            {
                                case DataType.BOOL:
                                    reader.BaseStream.Position += 1;
                                    break;
                                case DataType.INTEGER:
                                    reader.BaseStream.Position += 4;
                                    break;
                                case DataType.FLOAT:
                                    reader.BaseStream.Position += 4;
                                    break;
                                case DataType.STRING:
                                case DataType.FILEPATH:
                                    {
                                        int seek = reader.ReadByte();
                                        reader.BaseStream.Position += seek;
                                    }
                                    break;
                                case DataType.ENUM:
                                    {
                                        int enumType = reader.ReadInt32();
                                        if (enumType != -1)
                                        {
                                            reader.BaseStream.Position += 4;
                                        }
                                    }
                                    break;
                                case DataType.ENUM_STRING:
                                    {
                                        int seek = reader.ReadByte();
                                        reader.BaseStream.Position += seek;
                                        seek = reader.ReadByte();
                                        reader.BaseStream.Position += seek;
                                    }
                                    break;
                                case DataType.VECTOR:
                                    reader.BaseStream.Position += 12;
                                    break;
                                case DataType.RESOURCE:
                                    reader.BaseStream.Position += 4;
                                    break;
                                default:
                                    //Any other types have no default values.
                                    break;
                            }
                            break;
                    }
                }
            }
            return (null, null, null);
        }

        /* Create ParameterData with default values for the given entity's parameter */
        public ParameterData CreateDefaultParameterData(Entity entity, Composite composite, string parameter)
        {
            return CreateDefaultParameterData(entity, composite, ShortGuidUtils.Generate(parameter));
        }
        public ParameterData CreateDefaultParameterData(Entity entity, Composite composite, ShortGuid parameter)
        {
            switch (entity.variant)
            {
                case EntityVariant.VARIABLE:
                    VariableEntity variableEntity = (VariableEntity)entity;
                    if (parameter == variableEntity.name)
                    {
                        ParameterData defaultVal = variableEntity.GetParameter(parameter)?.content;
                        if (defaultVal != null)
                            return (ParameterData)defaultVal.Clone();
                        CompositePinInfoTable.PinInfo info = _commands.Utils.GetParameterInfo(composite, variableEntity);
                        switch (variableEntity.type)
                        {
                            case DataType.FLOAT:
                                return new cFloat();
                            case DataType.INTEGER:
                                return new cInteger();
                            case DataType.BOOL:
                                return new cBool();
                            case DataType.RESOURCE:
                                return new cResource();
                            case DataType.ENUM:
                                if (info == null)
                                    return new cEnum();
                                else
                                    return new cEnum(info.PinEnumTypeGUID, -1);
                            case DataType.ENUM_STRING:
                                if (info == null)
                                    return new cEnumString();
                                else
                                    return new cEnumString(info.PinEnumTypeGUID, "");
                            case DataType.STRING:
                            case DataType.FILEPATH:
                                return new cString();
                            case DataType.SPLINE:
                                return new cSpline();
                            case DataType.VECTOR:
                                return new cVector3();
                            case DataType.TRANSFORM:
                                return new cTransform();
                            default:
                                return new cString(); //string, or float?
                        }
                    }
                    break;
                case EntityVariant.FUNCTION:
                    FunctionEntity functionEntity = (FunctionEntity)entity;
                    if (functionEntity.function.IsFunctionType)
                    {
                        FunctionType? functionType = functionEntity.function.AsFunctionType;
                        while (true)
                        {
                            var data = CreateDefaultParameterData(functionType.Value, parameter);
                            if (data != null)
                                return data;
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                    }
                    else
                    {
                        ParameterData data;
                        FunctionType? functionType = FunctionType.CompositeInterface;
                        while (true)
                        {
                            data = CreateDefaultParameterData(functionType.Value, parameter);
                            if (data != null)
                                return data;
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                        Composite comp = _commands.GetComposite(functionEntity.function);
                        if (composite != null)
                        {
                            VariableEntity var = comp.variables.FirstOrDefault(o => o.name == parameter);
                            if (var != null)
                            {
                                data = CreateDefaultParameterData(var, comp, parameter);
                                if (data != null)
                                    return data;
                            }
                        }
                    }
                    break;
                case EntityVariant.PROXY:
                    {
                        ParameterData data;
                        FunctionType? functionType = FunctionType.ProxyInterface;
                        while (true)
                        {
                            data = CreateDefaultParameterData(functionType.Value, parameter);
                            if (data != null)
                                return data;
                            functionType = GetInheritedFunction(functionType.Value);
                            if (functionType == null) break;
                        }
                        ProxyEntity proxyEntity = (ProxyEntity)entity;
                        Entity proxiedEntity = proxyEntity.proxy.GetPointedEntity(_commands, out Composite proxiedComposite);
                        if (proxiedEntity != null)
                            return CreateDefaultParameterData(proxiedEntity, proxiedComposite, parameter);
                        break;
                    }
                case EntityVariant.ALIAS:
                    AliasEntity aliasEntity = (AliasEntity)entity;
                    Entity aliasedEntity = aliasEntity.alias.GetPointedEntity(_commands, composite, out Composite aliasedComposite);
                    if (aliasedEntity != null)
                        return CreateDefaultParameterData(aliasedEntity, aliasedComposite, parameter);
                    break;
            }
            return null;
        }

        /* Create ParameterData with default values for the given function type's parameter */
        public ParameterData CreateDefaultParameterData(FunctionType function, string parameter)
        {
            return CreateDefaultParameterData(function, ShortGuidUtils.Generate(parameter));
        }
        public ParameterData CreateDefaultParameterData(FunctionType function, ShortGuid parameter)
        {
            ParameterData data = null;
            Dictionary<ParameterVariant, int> offsets = CustomTable.Vanilla.CathodeEntities.FunctionVariantOffsets[function];
            foreach (KeyValuePair<ParameterVariant, int> entry in offsets)
            {
                data = CreateDefaultParameterData(function, parameter, entry.Key);
                if (data != null) break;
            }
            return data;
        }
        public ParameterData CreateDefaultParameterData(FunctionType function, ShortGuid parameter, ParameterVariant variant)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(CustomTable.Vanilla.CathodeEntities.content)))
            {
                reader.BaseStream.Position = CustomTable.Vanilla.CathodeEntities.FunctionVariantOffsets[function][variant];
                int paramCount = reader.ReadInt32();
                for (int i = 0; i < paramCount; i++)
                {
                    uint paramID = reader.ReadUInt32();
                    bool isCorrectParam = paramID == parameter.AsUInt32;
                    switch (variant)
                    {
                        case ParameterVariant.TARGET_PIN:
                        case ParameterVariant.REFERENCE_PIN:
                        case ParameterVariant.METHOD_FUNCTION:
                        case ParameterVariant.METHOD_PIN:
                            if (isCorrectParam)
                                return new cFloat();
                            break;
                        default:
                            DataType dataType = IntToDatatype(reader.ReadInt32());
                            switch (dataType)
                            {
                                case DataType.BOOL:
                                    if (isCorrectParam)
                                        return new cBool(reader.ReadBoolean());
                                    else
                                        reader.BaseStream.Position += 1;
                                    break;
                                case DataType.INTEGER:
                                    if (isCorrectParam)
                                        return new cInteger(reader.ReadInt32());
                                    else
                                        reader.BaseStream.Position += 4;
                                    break;
                                case DataType.FLOAT:
                                    if (isCorrectParam)
                                        return new cFloat(reader.ReadSingle());
                                    else
                                        reader.BaseStream.Position += 4;
                                    break;
                                case DataType.STRING:
                                case DataType.FILEPATH:
                                    if (isCorrectParam && !(function != FunctionType.Zone && paramID == _nameID))
                                        return new cString(reader.ReadString());
                                    else
                                    {
                                        int seek = reader.ReadByte();
                                        reader.BaseStream.Position += seek;
                                    }
                                    break;
                                case DataType.ENUM:
                                    {
                                        int enumType = reader.ReadInt32();
                                        if (enumType == -1)
                                        {
                                            if (isCorrectParam)
                                                return new cEnum();
                                        }
                                        else
                                        {
                                            if (isCorrectParam)
                                                return new cEnum((EnumType)enumType, reader.ReadInt32());
                                            else
                                                reader.BaseStream.Position += 4;
                                        }
                                    }
                                    break;
                                case DataType.ENUM_STRING:
                                    if (isCorrectParam)
                                        return new cEnumString((EnumStringType)Enum.Parse(typeof(EnumStringType), reader.ReadString()), reader.ReadString());
                                    else
                                    {
                                        int seek = reader.ReadByte();
                                        reader.BaseStream.Position += seek;
                                        seek = reader.ReadByte();
                                        reader.BaseStream.Position += seek;
                                    }
                                    break;
                                case DataType.VECTOR:
                                    if (isCorrectParam)
                                        return new cVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                    else
                                        reader.BaseStream.Position += 12;
                                    break;
                                case DataType.TRANSFORM:
                                    if (isCorrectParam)
                                        return new cTransform(); //There are no default transforms written
                                    break;
                                case DataType.RESOURCE:
                                    if (isCorrectParam)
                                        return new cResource((ResourceType)reader.ReadInt32());
                                    else
                                        reader.BaseStream.Position += 4;
                                    break;
                                default:
                                    if (isCorrectParam)
                                        return new cFloat(); //Any other types have no default values.
                                    break;
                            }
                            break;
                    }
                }
            }
            return null;
        }

        /* Get the relay pin for a given method pin */
        public ShortGuid GetRelay(ShortGuid guid)
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(CustomTable.Vanilla.CathodeEntities.content)))
            {
                reader.BaseStream.Position = CustomTable.Vanilla.CathodeEntities.RelayInfoOffset.Item1;
                for (int i = 0; i < CustomTable.Vanilla.CathodeEntities.RelayInfoOffset.Item2; i++)
                {
                    UInt32 method = reader.ReadUInt32();
                    UInt32 relay = reader.ReadUInt32();
                    if (method == guid.AsUInt32)
                        return new ShortGuid(relay);
                }
            }
            return ShortGuid.Invalid;
        }

        //This is a mapping for the old datatype enum which is still used by the BIN file - need to move it across to the new one.
        private DataType IntToDatatype(int i)
        {
            switch (i)
            {
                case 0:
                    return DataType.STRING;
                case 1:
                    return DataType.FLOAT;
                case 2:
                    return DataType.INTEGER;
                case 3:
                    return DataType.BOOL;
                case 4:
                    return DataType.VECTOR;
                case 5:
                    return DataType.TRANSFORM;
                case 6:
                    return DataType.ENUM;
                case 7:
                    return DataType.SPLINE;
                case 8:
                    return DataType.RESOURCE;
                case 9:
                    return DataType.NONE;
                case 10:
                    return DataType.FILEPATH;
                case 11:
                    return DataType.OBJECT;
                case 12:
                    return DataType.ZONE_LINK;
                case 13:
                    return DataType.ZONE;
                case 14:
                    return DataType.ANIMATION_INFO;
                case 15:
                    return DataType.COLOUR;
                case 16:
                    return DataType.RESOURCE_ID;
                case 17:
                    return DataType.REFERENCE_FRAME;
                case -1:
                    return DataType.ENUM_STRING;
            }
            throw new Exception();
        }
        #endregion

        #region Enum Utils
        //Check the formatted enum dump for content
        public CathodeEnumTable.EnumDescriptor GetEnum(string name)
        {
            ShortGuid id = ShortGuidUtils.Generate(name);
            return GetEnum(id);
        }
        public CathodeEnumTable.EnumDescriptor GetEnum(ShortGuid id)
        {
            return CustomTable.Vanilla.CathodeEnums.enums.FirstOrDefault(o => o.ID == id);
        }
        #endregion

        #region Entity Names
        /* Get the name of an entity contained within a composite */
        public string GetEntityName(Composite composite, Entity entity) => GetEntityName(composite.shortGUID, entity.shortGUID);
        public string GetEntityName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (_entityNames.names.TryGetValue(compositeID, out Dictionary<ShortGuid, string> customComposite))
                if (customComposite.TryGetValue(entityID, out string customName))
                    return customName;
            if (CustomTable.Vanilla.EntityNames.names.TryGetValue(compositeID, out Dictionary<ShortGuid, string> vanillaComposite))
                if (vanillaComposite.TryGetValue(entityID, out string vanillaName))
                    return vanillaName;
            return entityID.ToByteString();
        }

        /* Set the name of an entity contained within a composite */
        public void SetEntityName(Composite composite, Entity entity, string name) => SetEntityName(composite.shortGUID, entity.shortGUID, name);
        public void SetEntityName(ShortGuid compositeID, ShortGuid entityID, string name)
        {
            if (!_entityNames.names.ContainsKey(compositeID))
                _entityNames.names.Add(compositeID, new Dictionary<ShortGuid, string>());

            if (!_entityNames.names[compositeID].ContainsKey(entityID))
                _entityNames.names[compositeID].Add(entityID, name);
            else
                _entityNames.names[compositeID][entityID] = name;
        }

        /* Clear the name of an entity contained within a composite */
        public void ClearEntityName(Composite composite, Entity entity) => ClearEntityName(composite.shortGUID, entity.shortGUID);
        public void ClearEntityName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (_entityNames.names.ContainsKey(compositeID))
                _entityNames.names[compositeID].Remove(entityID);
        }
        #endregion

        #region Composite Modification Info
        /* Set/update the modification metadata for a composite */
        public void SetModificationInfo(CompositeModificationInfoTable.ModificationInfo info)
        {
            _modificationInfo.modification_info.RemoveAll(o => o.composite_id == info.composite_id);
            _modificationInfo.modification_info.Add(info);
        }

        /* Get the modification metadata for a composite (if it exists) */
        public CompositeModificationInfoTable.ModificationInfo GetModificationInfo(Composite composite) => GetModificationInfo(composite.shortGUID);
        public CompositeModificationInfoTable.ModificationInfo GetModificationInfo(ShortGuid composite)
        {
            return _modificationInfo.modification_info.FirstOrDefault(o => o.composite_id == composite);
        }
        #endregion

        #region Composite Pin Info 
        //TODO: perhaps this should be in ParameterUtils?

        /* Set/update the pin info for a composite VariableEntity */
        public void SetParameterInfo(Composite composite, CompositePinInfoTable.PinInfo info) => SetParameterInfo(composite.shortGUID, info);
        public void SetParameterInfo(ShortGuid composite, CompositePinInfoTable.PinInfo info)
        {
            List<CompositePinInfoTable.PinInfo> infos;
            if (!_pinInfo.composite_pin_infos.TryGetValue(composite, out infos))
            {
                infos = new List<CompositePinInfoTable.PinInfo>();
                _pinInfo.composite_pin_infos.Add(composite, infos);
            }

            infos.RemoveAll(o => o.VariableGUID == info.VariableGUID);
            infos.Add(info);
        }

        /* Get the pin info for a composite VariableEntity */
        public CompositePinInfoTable.PinInfo GetParameterInfo(Composite composite, VariableEntity variableEnt) => GetParameterInfo(composite.shortGUID, variableEnt.shortGUID);
        public CompositePinInfoTable.PinInfo GetParameterInfo(ShortGuid composite, ShortGuid variableEnt)
        {
            CompositePinInfoTable.PinInfo info = null;
            if (_pinInfo.composite_pin_infos.TryGetValue(composite, out List<CompositePinInfoTable.PinInfo> customInfos))
                info = customInfos.FirstOrDefault(o => o.VariableGUID == variableEnt);
            if (info != null)
                return info;
            if (CustomTable.Vanilla.CompositePinInfos.composite_pin_infos.TryGetValue(composite, out List<CompositePinInfoTable.PinInfo> vanillaInfos))
                info = vanillaInfos.FirstOrDefault(o => o.VariableGUID == variableEnt);
            return info;
        }

        /* Convert PinType enum to ParameterVariant enum */
        public ParameterVariant PinTypeToParameterVariant(ShortGuid type)
        {
            return PinTypeToParameterVariant((CompositePinType)type.AsUInt32);
        }
        public ParameterVariant PinTypeToParameterVariant(CompositePinType type)
        {
            switch (type)
            {
                case CompositePinType.CompositeInputAnimationInfoVariablePin:
                case CompositePinType.CompositeInputBoolVariablePin:
                case CompositePinType.CompositeInputDirectionVariablePin:
                case CompositePinType.CompositeInputFloatVariablePin:
                case CompositePinType.CompositeInputIntVariablePin:
                case CompositePinType.CompositeInputObjectVariablePin:
                case CompositePinType.CompositeInputPositionVariablePin:
                case CompositePinType.CompositeInputStringVariablePin:
                case CompositePinType.CompositeInputVariablePin:
                case CompositePinType.CompositeInputZoneLinkPtrVariablePin:
                case CompositePinType.CompositeInputZonePtrVariablePin:
                case CompositePinType.CompositeInputEnumVariablePin:
                case CompositePinType.CompositeInputEnumStringVariablePin:
                    return ParameterVariant.INPUT_PIN;
                case CompositePinType.CompositeOutputAnimationInfoVariablePin:
                case CompositePinType.CompositeOutputBoolVariablePin:
                case CompositePinType.CompositeOutputDirectionVariablePin:
                case CompositePinType.CompositeOutputFloatVariablePin:
                case CompositePinType.CompositeOutputIntVariablePin:
                case CompositePinType.CompositeOutputObjectVariablePin:
                case CompositePinType.CompositeOutputPositionVariablePin:
                case CompositePinType.CompositeOutputStringVariablePin:
                case CompositePinType.CompositeOutputVariablePin:
                case CompositePinType.CompositeOutputZoneLinkPtrVariablePin:
                case CompositePinType.CompositeOutputZonePtrVariablePin:
                case CompositePinType.CompositeOutputEnumVariablePin:
                case CompositePinType.CompositeOutputEnumStringVariablePin:
                    return ParameterVariant.OUTPUT_PIN;
                case CompositePinType.CompositeMethodPin:
                    return ParameterVariant.METHOD_PIN;
                case CompositePinType.CompositeTargetPin:
                    return ParameterVariant.TARGET_PIN;
                case CompositePinType.CompositeReferencePin:
                    return ParameterVariant.REFERENCE_PIN;
                default:
                    throw new Exception("Unexpected type!");
            }
        }
        #endregion

        #region Table Management
        /* Handle loading/saving "purge states" -> this tracks the composites that have had unresolvable entities removed from */
        private void LoadInfo(string filepath)
        {
            _compPurges = (CompositePurgeTable)CustomTable.ReadTable(filepath, CustomTableType.COMPOSITE_PURGE_STATES);
            if (_compPurges == null) 
                _compPurges = new CompositePurgeTable();
            else
                Console.WriteLine("Registered " + _compPurges.purged.Count + " pre-purged composites!");

            _entityNames = (EntityNameTable)CustomTable.ReadTable(filepath, CustomTableType.ENTITY_NAMES);
            if (_entityNames == null) 
                _entityNames = new EntityNameTable();
            else
                Console.WriteLine("Loaded " + _entityNames.names.Count + " custom entity names!");

            _modificationInfo = (CompositeModificationInfoTable)CustomTable.ReadTable(filepath, CustomTableType.COMPOSITE_MODIFICATION_INFO);
            if (_modificationInfo == null) 
                _modificationInfo = new CompositeModificationInfoTable();
            else
                Console.WriteLine("Loaded modification info for " + _modificationInfo.modification_info.Count + " composites!");

            _pinInfo = (CompositePinInfoTable)CustomTable.ReadTable(filepath, CustomTableType.COMPOSITE_PIN_INFO);
            if (_pinInfo == null) 
                _pinInfo = new CompositePinInfoTable();
            else
                Console.WriteLine("Loaded custom pin info for " + _pinInfo.composite_pin_infos.Count + " composites!");
        }
        private void SaveInfo(string filepath)
        {
            CustomTable.WriteTable(filepath, CustomTableType.COMPOSITE_PURGE_STATES, _compPurges);
            Console.WriteLine("Stored " + _compPurges.purged.Count + " pre-purged composites!");

            CustomTable.WriteTable(filepath, CustomTableType.ENTITY_NAMES, _entityNames);
            Console.WriteLine("Saved " + _entityNames.names.Count + " custom entity names!");

            CustomTable.WriteTable(filepath, CustomTableType.COMPOSITE_MODIFICATION_INFO, _modificationInfo);
            Console.WriteLine("Saved modification info for " + _modificationInfo.modification_info.Count + " composites!");

            CustomTable.WriteTable(filepath, CustomTableType.COMPOSITE_PIN_INFO, _pinInfo);
            Console.WriteLine("Saved custom pin info for " + _pinInfo.composite_pin_infos.Count + " composites!");
        }
        #endregion
    }
}
