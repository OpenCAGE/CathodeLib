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
            _nameID = ShortGuidUtils.Generate("name").AsUInt32;
            _commands = commands;

            _commands.OnLoadSuccess += LoadInfo;
            _commands.OnSaveSuccess += SaveInfo;

            if (_commands.Loaded)
                LoadInfo(_commands.Filepath);
        }

        #region Generic Utility Functions
        /// <summary>
        /// Gets the composite that contains the entity
        /// </summary>
        public Composite GetContainedComposite(Entity entity)
        {
            if (entity == null) return null;
            
            for (int i = 0; i < _commands.Entries.Count; i++)
            {
                switch (entity.variant)
                {
                    case EntityVariant.FUNCTION:
                        if (_commands.Entries[i].functions_dictionary.TryGetValue(entity.shortGUID, out FunctionEntity func) && func == entity)
                            return _commands.Entries[i];
                        break;
                    case EntityVariant.VARIABLE:
                        if (_commands.Entries[i].variables_dictionary.TryGetValue(entity.shortGUID, out VariableEntity var) && var == entity)
                            return _commands.Entries[i];
                        break;
                    case EntityVariant.PROXY:
                        if (_commands.Entries[i].proxies_dictionary.TryGetValue(entity.shortGUID, out ProxyEntity proxy) && proxy == entity)
                            return _commands.Entries[i];
                        break;
                    case EntityVariant.ALIAS:
                        if (_commands.Entries[i].aliases_dictionary.TryGetValue(entity.shortGUID, out AliasEntity alias) && alias == entity)
                            return _commands.Entries[i];
                        break;
                }
            }
            return null;
        }

        /// <summary>
        /// Resolve an alias or proxy
        /// </summary>
        public List<Tuple<Composite, Entity>> ResolveAliasOrProxy(Entity entity, Composite composite)
        {
            switch (entity?.variant)
            {
                case EntityVariant.ALIAS:
                    return ResolveAlias((AliasEntity)entity, composite);
                case EntityVariant.PROXY:
                    return ResolveProxy((ProxyEntity)entity);
                default:
                    return new List<Tuple<Composite, Entity>>();
            }
        }
        public List<Tuple<Composite, Entity>> ResolveAliasOrProxy(EntityPath path, Composite composite)
        {
            return ResolveAliasOrProxy(path?.path, composite);
        }
        public List<Tuple<Composite, Entity>> ResolveAliasOrProxy(ShortGuid[] hierarchy, Composite composite)
        {
            List<Tuple<Composite,Entity>> path = ResolveAlias(hierarchy, composite);
            if (CouldResolve(path))
                return path;
            return ResolveProxy(hierarchy);
        }

        /// <summary>
        /// Resolve an alias
        /// </summary>
        public List<Tuple<Composite, Entity>> ResolveAlias(AliasEntity alias, Composite composite)
        {
            return ResolveAlias(alias?.alias?.path, composite);
        }
        public List<Tuple<Composite, Entity>> ResolveAlias(EntityPath path, Composite composite)
        {
            return ResolveAlias(path?.path, composite);
        }
        public List<Tuple<Composite, Entity>> ResolveAlias(ShortGuid[] hierarchy, Composite composite)
        {
            if (hierarchy == null || composite == null || hierarchy.Length <= 1)
                return new List<Tuple<Composite, Entity>>();

            bool hasTerminator = hierarchy[hierarchy.Length - 1] == ShortGuid.Invalid;
            int maxIndex = hierarchy.Length - (hasTerminator ? 1 : 0);
            
            // Pre-allocate list with estimated capacity
            var path = new List<Tuple<Composite, Entity>>(maxIndex);

            Composite currentComp = composite;
            for (int i = 0; i < maxIndex; i++)
            {
                Entity entity = currentComp.GetEntityByID(hierarchy[i]);
                if (entity == null)
                    return new List<Tuple<Composite, Entity>>(); //Unresolvable!

                path.Add(new Tuple<Composite, Entity>(currentComp, entity));

                //Look up next composite to check, if we're not on the last one
                if (i != maxIndex - 1)
                {
                    if (entity.variant != EntityVariant.FUNCTION)
                        return new List<Tuple<Composite, Entity>>(); //Unresolvable!

                    FunctionEntity function = (FunctionEntity)entity;
                    currentComp = _commands.GetComposite(function.function); 
                    if (currentComp == null)
                        return new List<Tuple<Composite, Entity>>(); //Unresolvable!
                }
            }
            return path;
        }

        /// <summary>
        /// Resolve a proxy
        /// </summary>
        public List<Tuple<Composite, Entity>> ResolveProxy(ProxyEntity proxy)
        {
            return ResolveProxy(proxy?.proxy?.path);
        }
        public List<Tuple<Composite, Entity>> ResolveProxy(EntityPath path)
        {
            return ResolveProxy(path?.path);
        }
        public List<Tuple<Composite, Entity>> ResolveProxy(ShortGuid[] hierarchy)
        { 
            if (hierarchy == null || hierarchy.Length <= 2)
                return new List<Tuple<Composite, Entity>>();

            Composite initialComp = _commands.GetComposite(hierarchy[0]); //NOTE: This isn't always the initial comp, so we check from the entry point first.
            Composite currentComp = _commands.EntryPoints[0];

            bool hasTerminator = hierarchy[hierarchy.Length - 1] == ShortGuid.Invalid;
            int maxIndex = hierarchy.Length - (hasTerminator ? 1 : 0);
            
            var path = new List<Tuple<Composite, Entity>>(maxIndex - 1);
            
            for (int i = 1; i < maxIndex; i++)
            {
                //Sometimes, the same entity is added twice. Seems wrong?
                if (hierarchy[i] == hierarchy[i - 1])
                    continue;

                Entity entity = currentComp.GetEntityByID(hierarchy[i]);
                if (entity == null && i == 1)
                {
                    if (initialComp == null)
                        return new List<Tuple<Composite, Entity>>(); //Unresolvable!

                    //This handles cases where the composite reference is actually where we start from. Seems wrong that this isn't ever the case?
                    entity = initialComp.GetEntityByID(hierarchy[i]);
                    if (entity != null)
                        currentComp = initialComp;
                }
                if (entity == null)
                    return new List<Tuple<Composite, Entity>>(); //Unresolvable!

                path.Add(new Tuple<Composite, Entity>(currentComp, entity));

                //Look up next composite to check, if we're not on the last one
                if (i != maxIndex - 1)
                {
                    if (entity.variant != EntityVariant.FUNCTION)
                        return new List<Tuple<Composite, Entity>>(); //Unresolvable!

                    FunctionEntity function = (FunctionEntity)entity;
                    currentComp = _commands.GetComposite(function.function);
                    if (currentComp == null)
                        return new List<Tuple<Composite, Entity>>(); //Unresolvable!
                }
            }
            return path;
        }

        /// <summary>
        /// Resolve a hierarchy pointing from the root composite
        /// </summary>
        public List<Tuple<Composite, Entity>> ResolveHierarchy(EntityPath path)
        {
            return ResolveHierarchy(path?.path);
        }
        public List<Tuple<Composite, Entity>> ResolveHierarchy(ShortGuid[] hierarchy)
        {
            if (hierarchy == null || hierarchy.Length == 0)
                return new List<Tuple<Composite, Entity>>();

            Composite currentComp = _commands.EntryPoints[0];

            bool hasTerminator = hierarchy[hierarchy.Length - 1] == ShortGuid.Invalid;
            int maxIndex = hierarchy.Length - (hasTerminator ? 1 : 0);

            var path = new List<Tuple<Composite, Entity>>(maxIndex - 1);

            for (int i = 0; i < maxIndex; i++)
            {
                Entity entity = currentComp.GetEntityByID(hierarchy[i]);
                if (entity == null)
                    return new List<Tuple<Composite, Entity>>(); //Unresolvable!

                path.Add(new Tuple<Composite, Entity>(currentComp, entity));

                //Look up next composite to check, if we're not on the last one
                if (i != maxIndex - 1)
                {
                    if (entity.variant != EntityVariant.FUNCTION)
                        return new List<Tuple<Composite, Entity>>(); //Unresolvable!

                    FunctionEntity function = (FunctionEntity)entity;
                    currentComp = _commands.GetComposite(function.function);
                    if (currentComp == null)
                        return new List<Tuple<Composite, Entity>>(); //Unresolvable!
                }
            }
            return path;
        }

        /// <summary>
        /// Checks a resolved alias or proxy to see if it could be resolved
        /// </summary>
        public bool CouldResolve(List<Tuple<Composite, Entity>> path)
        {
            return path != null && path.Count != 0;
        }

        /// <summary>
        /// Checks a resolved alias or proxy to get the pointed Entity and Composite
        /// </summary>
        public (Composite, Entity) GetResolvedTarget(List<Tuple<Composite, Entity>> path)
        {
            if (!CouldResolve(path))
                return (null, null);
            return (path[path.Count - 1].Item1, path[path.Count - 1].Item2);
        }

        /// <summary>
        /// Gets a resolved alias or proxy as a string representation - OPTIMIZED
        /// </summary>
        public string GetResolvedAsString(List<Tuple<Composite, Entity>> path, bool includeGuids = true)
        {
            if (path == null || path.Count == 0)
                return "";
                
            // Pre-allocate StringBuilder for better performance
            var sb = new StringBuilder();
            for (int i = 0; i < path.Count; i++)
            {
                if (includeGuids) 
                {
                    sb.Append('[');
                    sb.Append(path[i].Item2.shortGUID.ToByteString());
                    sb.Append("] ");
                }
                sb.Append(GetEntityName(path[i].Item1, path[i].Item2));
                if (i != path.Count - 1) 
                    sb.Append(" -> ");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Calculate an instanced entity's worldspace position & rotation
        /// </summary>
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

        /// <summary>
        /// CA's CAGE doesn't properly tidy up hierarchies pointing to deleted entities - so we can do that to save confusion
        /// </summary>
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

            // Functions must be a valid FunctionType, or point to a Composite that exists
            var functionsToRemove = new List<ShortGuid>();
            foreach (var kvp in composite.functions_dictionary)
            {
                if (!(kvp.Value.function.IsFunctionType || _commands.GetComposite(kvp.Value.function) != null))
                {
                    functionsToRemove.Add(kvp.Key);
                }
            }
            originalFuncCount = composite.functions_dictionary.Count;
            foreach (var guid in functionsToRemove)
            {
                composite.functions_dictionary.Remove(guid);
            }

            // Aliases must point to children of the Composite that still exist
            // Also remove aliases that don't have any links in or out, or any parameters
            var aliasesToRemove = new List<ShortGuid>();
            foreach (var kvp in composite.aliases_dictionary)
            {
                var alias = kvp.Value;
                // Remove if alias cannot be resolved
                if (!CouldResolve(ResolveAlias(alias, composite)))
                {
                    aliasesToRemove.Add(kvp.Key);
                }
                // Remove if alias has no child links, no parameters, and no parent links
                else if (alias.childLinks.Count == 0 && 
                         alias.parameters.Count == 0 && 
                         alias.GetParentLinks(composite).Count == 0)
                {
                    aliasesToRemove.Add(kvp.Key);
                }
            }
            originalAliasCount = composite.aliases_dictionary.Count;
            foreach (var guid in aliasesToRemove)
            {
                composite.aliases_dictionary.Remove(guid);
            }

            // Proxies must be able to be resolved in some form
            var proxiesToRemove = new List<ShortGuid>();
            foreach (var kvp in composite.proxies_dictionary)
            {
                if (!CouldResolve(ResolveProxy(kvp.Value)))
                {
                    proxiesToRemove.Add(kvp.Key);
                }
            }
            originalProxyCount = composite.proxies_dictionary.Count;
            foreach (var guid in proxiesToRemove)
            {
                composite.proxies_dictionary.Remove(guid);
            }

            // Process special function types (TriggerSequence and CAGEAnimation)
            foreach (var kvp in composite.functions_dictionary)
            {
                var function = kvp.Value;
                switch (ShortGuidUtils.FindString(function.function))
                {
                    case "TriggerSequence":
                        // TriggerSequence sequences must point to entities that still exist
                        TriggerSequence trig = (TriggerSequence)function;
                        var sequenceToRemove = new List<TriggerSequence.SequenceEntry>();
                        foreach (var entry in trig.sequence)
                        {
                            if (!CouldResolve(ResolveAlias(entry.connectedEntity.path, composite)))
                            {
                                sequenceToRemove.Add(entry);
                            }
                        }
                        originalTriggerCount += trig.sequence.Count;
                        newTriggerCount += trig.sequence.Count - sequenceToRemove.Count;
                        foreach (var entry in sequenceToRemove)
                        {
                            trig.sequence.Remove(entry);
                        }
                        break;
                    case "CAGEAnimation":
                        // CAGEAnimation connections must point to entities that still exist
                        CAGEAnimation anim = (CAGEAnimation)function;
                        var connectionsToRemove = new List<CAGEAnimation.Connection>();
                        foreach (var connection in anim.connections)
                        {
                            //TODO: Worth also removing connections that have no event/float tracks?
                            //List<CAGEAnimation.FloatTrack> floatTracks = anim.animations.FindAll(o => o.shortGUID == connection.target_track);
                            //List<CAGEAnimation.EventTrack> eventTracks = anim.events.FindAll(o => o.shortGUID == connection.target_track);
                            if (!CouldResolve(ResolveAlias(connection.connectedEntity.path, composite)))
                            {
                                connectionsToRemove.Add(connection);
                            }
                        }
                        originalAnimCount += anim.connections.Count;
                        newAnimCount += anim.connections.Count - connectionsToRemove.Count;
                        foreach (var connection in connectionsToRemove)
                        {
                            anim.connections.Remove(connection);
                        }
                        break;
                }
            }

            // Links must point to entities that still exist within the same Composite
            // Use dictionary values for efficient entity lookup
            var allEntities = composite.GetEntities();
            foreach (var entity in allEntities)
            {
                var linksToRemove = new List<EntityConnector>();
                foreach (var link in entity.childLinks)
                {
                    if (composite.GetEntityByID(link.linkedEntityID) == null)
                    {
                        linksToRemove.Add(link);
                    }
                }
                originalLinkCount += entity.childLinks.Count;
                newLinkCount += entity.childLinks.Count - linksToRemove.Count;
                foreach (var link in linksToRemove)
                {
                    entity.childLinks.Remove(link);
                }
            }

            int totalRemoved = originalUnknownCount +
                (originalFuncCount - composite.functions_dictionary.Count) +
                (originalProxyCount - composite.proxies_dictionary.Count) +
                (originalAliasCount - composite.aliases_dictionary.Count) +
                (originalTriggerCount - newTriggerCount) +
                (originalAnimCount - newAnimCount) +
                (originalLinkCount - newLinkCount);

            if (totalRemoved == 0)
            {
                //Console.WriteLine("Purge found nothing to clear up.");
                return true;
            }

            Console.WriteLine(
                "Purged all dead hierarchies and entities in " + composite.name + "!" +
                "\n - " + originalUnknownCount + " unknown entities" +
                "\n - " + (originalFuncCount - composite.functions_dictionary.Count) + " functions (of " + originalFuncCount + ")" +
                "\n - " + (originalProxyCount - composite.proxies_dictionary.Count) + " proxies (of " + originalProxyCount + ")" +
                "\n - " + (originalAliasCount - composite.aliases_dictionary.Count) + " aliases (of " + originalAliasCount + ")" +
                "\n - " + (originalTriggerCount - newTriggerCount) + " triggers (of " + originalTriggerCount + ")" +
                "\n - " + (originalAnimCount - newAnimCount) + " anim connections (of " + originalAnimCount + ")" +
                "\n - " + (originalLinkCount - newLinkCount) + " entity links (of " + originalLinkCount + ")");
            return true;
        }

        /// <summary>
        /// Remove all links between Entities within the Composite
        /// </summary>
        public void ClearAllLinks(Composite composite)
        {
            composite.GetEntities().ForEach(o => o.childLinks.Clear());
        }

        /// <summary>
        /// Count the number of links in the Composite
        /// </summary>
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
        /// <summary>
        /// Get the inherited function type for a given function type (returns null if it doesn't inherit)
        /// </summary>
        public FunctionType? GetInheritedFunction(FunctionType function)
        {
            return CustomTable.Vanilla.CathodeEntities.FunctionBaseClasses[function];
        }

        /// <summary>
        /// Add all parameters to a given entity with default values (NOTE: you only need to pass in composite if Entity is an Alias or Variable, otherwise feel free to pass null)
        /// </summary>
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
                        (Composite proxiedComposite, Entity proxiedEntity) = _commands.Utils.GetResolvedTarget(_commands.Utils.ResolveProxy((ProxyEntity)entity));
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
                        (Composite aliasedComposite, Entity aliasedEntity) = _commands.Utils.GetResolvedTarget(_commands.Utils.ResolveAlias((AliasEntity)entity, composite));
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
                                    (Composite proxiedComposite, Entity proxiedEntity) = _commands.Utils.GetResolvedTarget(_commands.Utils.ResolveProxy((ProxyEntity)aliasedEntity));
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
            var pinInfo = _commands.Utils.GetPinInfo(composite, baseEntity);
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

        /// <summary>
        /// Get all possible parameters for a given entity
        /// </summary>
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
                    (Composite proxiedComposite, Entity proxiedEntity) = GetResolvedTarget(ResolveProxy((ProxyEntity)entity));
                    if (proxiedEntity != null)
                        parameters.AddRange(GetAllParameters(proxiedEntity, composite)); //note while reading through again, shouldn't these be Proxied/Aliased composites?
                    break;
                case EntityVariant.ALIAS:
                    (Composite aliasedComposite, Entity aliasedEntity) = GetResolvedTarget(ResolveAlias((AliasEntity)entity, composite));
                    if (aliasedEntity != null)
                        parameters.AddRange(GetAllParameters(aliasedEntity, composite));
                    break;
                case EntityVariant.VARIABLE:
                    VariableEntity variableEntity = (VariableEntity)entity;
                    CompositePinInfoTable.PinInfo info = _commands.Utils.GetPinInfo(composite, variableEntity);
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

        /// <summary>
        /// Get all possible parameters for a given function type (not including inherited)
        /// </summary>
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
                                ShortGuid param = new ShortGuid(paramID);
                                parameters.Add((new ShortGuid(paramID), entry.Key, DataType.FLOAT));
                                break;
                            default:
                                int dataTypeTemp = reader.ReadInt32();
                                DataType dataType = dataTypeTemp == -1 ? DataType.ENUM_STRING : (DataType)dataTypeTemp;
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

        /// <summary>
        /// Get metadata about a parameter on an entity: variant, type, and function/composite that implements (if applicable)
        /// </summary>
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
                                CompositePinInfoTable.PinInfo info = _commands.Utils.GetPinInfo(compositeInstance, var);
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
                        Entity proxiedEntity = GetResolvedTarget(ResolveProxy(proxyEntity)).Item2;
                        if (proxiedEntity != null)
                            return GetParameterMetadata(proxiedEntity, parameter, composite);
                        break;
                    }
                case EntityVariant.ALIAS:
                    AliasEntity aliasEntity = (AliasEntity)entity;
                    Entity aliasedEntity = GetResolvedTarget(ResolveAlias(aliasEntity, composite)).Item2;
                    if (aliasedEntity != null)
                        return GetParameterMetadata(aliasedEntity, parameter, composite);
                    break;
            }
            return (null, null, ShortGuid.Invalid);
        }

        /// <summary>
        /// Get metadata about a parameter on a function: variant, type, and function that implements
        /// </summary>
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
                            int dataTypeTemp = reader.ReadInt32();
                            DataType dataType = dataTypeTemp == -1 ? DataType.ENUM_STRING : (DataType)dataTypeTemp;
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

        /// <summary>
        /// Create ParameterData with default values for the given entity's parameter
        /// </summary>
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
                        CompositePinInfoTable.PinInfo info = _commands.Utils.GetPinInfo(composite, variableEntity);
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
                        (Composite proxiedComposite, Entity proxiedEntity) = GetResolvedTarget(ResolveProxy((ProxyEntity)entity));
                        if (proxiedEntity != null)
                            return CreateDefaultParameterData(proxiedEntity, proxiedComposite, parameter);
                        break;
                    }
                case EntityVariant.ALIAS:
                    (Composite aliasedComposite, Entity aliasedEntity) = GetResolvedTarget(ResolveAlias((AliasEntity)entity, composite));
                    if (aliasedEntity != null)
                        return CreateDefaultParameterData(aliasedEntity, aliasedComposite, parameter);
                    break;
            }
            return null;
        }

        /// <summary>
        /// Create ParameterData with default values for the given function type's parameter
        /// </summary>
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
                            int dataTypeTemp = reader.ReadInt32();
                            DataType dataType = dataTypeTemp == -1 ? DataType.ENUM_STRING : (DataType)dataTypeTemp;
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

        /// <summary>
        /// Get the relay pin for a given method pin
        /// </summary>
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
        /// <summary>
        /// Get the name of an entity contained within a composite
        /// </summary>
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

        /// <summary>
        /// Set the name of an entity contained within a composite
        /// </summary>
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

        /// <summary>
        /// Clear the name of an entity contained within a composite
        /// </summary>
        public void ClearEntityName(Composite composite, Entity entity) => ClearEntityName(composite.shortGUID, entity.shortGUID);
        public void ClearEntityName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (_entityNames.names.ContainsKey(compositeID))
                _entityNames.names[compositeID].Remove(entityID);
        }
        #endregion

        #region Composite Modification Info
        /// <summary>
        /// Set/update the modification metadata for a composite
        /// </summary>
        public void SetModificationInfo(CompositeModificationInfoTable.ModificationInfo info)
        {
            _modificationInfo.modification_info.RemoveAll(o => o.composite_id == info.composite_id);
            _modificationInfo.modification_info.Add(info);
        }

        /// <summary>
        /// Get the modification metadata for a composite (if it exists)
        /// </summary>
        public CompositeModificationInfoTable.ModificationInfo GetModificationInfo(Composite composite) => GetModificationInfo(composite.shortGUID);
        public CompositeModificationInfoTable.ModificationInfo GetModificationInfo(ShortGuid composite)
        {
            return _modificationInfo.modification_info.FirstOrDefault(o => o.composite_id == composite);
        }
        #endregion

        #region Composite Pin Info 
        /// <summary>
        /// Set/update the pin info for a composite VariableEntity
        /// </summary>
        public void SetPinInfo(Composite composite, CompositePinInfoTable.PinInfo info) => SetPinInfo(composite.shortGUID, info);
        public void SetPinInfo(ShortGuid composite, CompositePinInfoTable.PinInfo info)
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

        /// <summary>
        /// Get the pin info for a composite VariableEntity
        /// </summary>
        public CompositePinInfoTable.PinInfo GetPinInfo(Composite composite, VariableEntity variableEnt) => GetPinInfo(composite.shortGUID, variableEnt.shortGUID);
        public CompositePinInfoTable.PinInfo GetPinInfo(ShortGuid composite, ShortGuid variableEnt)
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

        /// <summary>
        /// Convert PinType enum to ParameterVariant enum
        /// </summary>
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
        /// <summary>
        /// Handle loading/saving "purge states" -> this tracks the composites that have had unresolvable entities removed from
        /// </summary>
        private void LoadInfo(string filepath)
        {
            ShortGuidUtils.LoadCustomNames(_commands.Filepath);

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
            ShortGuidUtils.SaveCustomNames(_commands.Filepath);

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
