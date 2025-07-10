using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using static CathodeLib.CompositeModificationInfoTable;



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

        //TODO: I should merge all of these tables into one.
        private CompositePurgeTable _compPurges = new CompositePurgeTable();
        private EntityNameTable _entityNames = new EntityNameTable();
        private CompositeModificationInfoTable _modificationInfo = new CompositeModificationInfoTable();
        private CompositePinInfoTable _pinInfo = new CompositePinInfoTable();

        private Commands _commands = null;

        public CommandsUtils(Commands commands)
        {
            _commands = commands;

            _commands.OnLoadSuccess += LoadInfo;
            _commands.OnSaveSuccess += SaveInfo;

            if (_commands.Loaded)
                LoadInfo(_commands.Filepath);
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
            if (_compPurges == null) _compPurges = new CompositePurgeTable();
            Console.WriteLine("Registered " + _compPurges.purged.Count + " pre-purged composites!");

            _entityNames = (EntityNameTable)CustomTable.ReadTable(filepath, CustomTableType.ENTITY_NAMES);
            if (_entityNames == null) _entityNames = new EntityNameTable();
            Console.WriteLine("Loaded " + _entityNames.names.Count + " custom entity names!");

            _modificationInfo = (CompositeModificationInfoTable)CustomTable.ReadTable(filepath, CustomTableType.COMPOSITE_MODIFICATION_INFO);
            if (_modificationInfo == null) _modificationInfo = new CompositeModificationInfoTable();
            Console.WriteLine("Loaded modification info for " + _modificationInfo.modification_info.Count + " composites!");

            _pinInfo = (CompositePinInfoTable)CustomTable.ReadTable(filepath, CustomTableType.COMPOSITE_PIN_INFO);
            if (_pinInfo == null) _pinInfo = new CompositePinInfoTable();
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
