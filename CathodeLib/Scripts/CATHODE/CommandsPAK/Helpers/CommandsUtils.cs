using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Scripting
{
    //Helpful lookup tables for various Cathode Commands types
    public static class CommandsUtils
    {
        //NOTE: This list is exposed publicly, because it is up to your app to manage it.
        public static CompositePurgeTable PurgedComposites => _purged;
        private static CompositePurgeTable _purged;

        public static Commands LinkedCommands => _commands;
        private static Commands _commands;

        static CommandsUtils()
        {
            _purged = new CompositePurgeTable();
        }

        /* Optionally, link a Commands file which can be used to save purge states to */
        public static void LinkCommands(Commands commands)
        {
            if (_commands != null)
            {
                _commands.OnLoadSuccess -= LoadPurgeStates;
                _commands.OnSaveSuccess -= SavePurgeStates;
            }

            _commands = commands;
            if (_commands == null) return;

            _commands.OnLoadSuccess += LoadPurgeStates;
            _commands.OnSaveSuccess += SavePurgeStates;

            LoadPurgeStates(_commands.Filepath);
        }

        /* Pull non-vanilla entity names from the CommandsPAK */
        private static void LoadPurgeStates(string filepath)
        {
            _purged = (CompositePurgeTable)CustomTable.ReadTable(filepath, CustomEndTables.COMPOSITE_PURGE_STATES);
            if (_purged == null) _purged = new CompositePurgeTable();
            Console.WriteLine("Registered " + _purged.purged.Count + " pre-purged composites!");
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private static void SavePurgeStates(string filepath)
        {
            CustomTable.WriteTable(filepath, CustomEndTables.COMPOSITE_PURGE_STATES, _purged);
            Console.WriteLine("Stored " + _purged.purged.Count + " pre-purged composites!");
        }

        /* Gets the composite that contains the entity */
        public static Composite GetContainedComposite(this Entity entity)
        {
            if (_commands == null)
                throw (new Exception("Please link your Commands object to CommandsUtils using CommandsUtils.LinkCommands before calling this function"));

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

        #region HELPER_FUNCS
        /* Resolve an entity hierarchy */
        public static Entity ResolveHierarchy(Commands commands, Composite composite, ShortGuid[] hierarchy, out Composite containedComposite, out string asString, bool includeShortGuids = true)
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
                currentFlowgraphToSearch = commands.EntryPoints[0];
                if (currentFlowgraphToSearch == null || currentFlowgraphToSearch.GetEntityByID(hierarchyCopy[0]) == null)
                {
                    currentFlowgraphToSearch = commands.GetComposite(hierarchyCopy[0]);
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
                hierarchyString += EntityUtils.GetName(currentFlowgraphToSearch.shortGUID, entity.shortGUID);
                if (i >= hierarchyCopy.Count - 2) break; //Last is always 00-00-00-00
                hierarchyString += " -> ";
        
                if (entity.variant == EntityVariant.FUNCTION)
                {
                    Composite flowRef = commands.GetComposite(((FunctionEntity)entity).function);
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
        public static (Vector3, Quaternion) CalculateInstancedPosition(EntityPath hierarchy)
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
        public static bool PurgeDeadLinks(Commands commands, Composite composite, bool force = false)
        {
            if (!force && LinkedCommands == commands && _purged.purged.Contains(composite.shortGUID))
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
                if (composite.functions[i].function.IsFunctionType || commands.GetComposite(composite.functions[i].function) != null)
                    functionsPurged.Add(composite.functions[i]);
            originalFuncCount = composite.functions.Count;
            composite.functions = functionsPurged;

            //Clear aliases
            List<AliasEntity> aliasesPurged = new List<AliasEntity>();
            for (int i = 0; i < composite.aliases.Count; i++)
                if (ResolveHierarchy(commands, composite, composite.aliases[i].alias.path, out Composite flowTemp, out string hierarchy) != null)
                    aliasesPurged.Add(composite.aliases[i]);
            originalAliasCount = composite.aliases.Count;
            composite.aliases = aliasesPurged;

            //Clear proxies
            List<ProxyEntity> proxyPurged = new List<ProxyEntity>();
            for (int i = 0; i < composite.proxies.Count; i++)
                if (ResolveHierarchy(commands, composite, composite.proxies[i].proxy.path, out Composite flowTemp, out string hierarchy) != null)
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
                        List<TriggerSequence.Entity> trigSeq = new List<TriggerSequence.Entity>();
                        for (int x = 0; x < trig.entities.Count; x++)
                            if (ResolveHierarchy(commands, composite, trig.entities[x].connectedEntity.path, out Composite flowTemp, out string hierarchy) != null)
                                trigSeq.Add(trig.entities[x]);
                        originalTriggerCount += trig.entities.Count;
                        newTriggerCount += trigSeq.Count;
                        trig.entities = trigSeq;
                        break;
                    case "CAGEAnimation":
                        CAGEAnimation anim = (CAGEAnimation)composite.functions[i];
                        List<CAGEAnimation.Connection> headers = new List<CAGEAnimation.Connection>();
                        for (int x = 0; x < anim.connections.Count; x++)
                        {
                            List<CAGEAnimation.Animation> anim_target = anim.animations.FindAll(o => o.shortGUID == anim.connections[x].keyframeID);
                            List<CAGEAnimation.Event> event_target = anim.events.FindAll(o => o.shortGUID == anim.connections[x].keyframeID);
                            if (!(anim_target.Count == 0 && event_target.Count == 0) &&
                                ResolveHierarchy(commands, composite, anim.connections[x].connectedEntity.path, out Composite flowTemp, out string hierarchy) != null)
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
#endregion
    }
}
