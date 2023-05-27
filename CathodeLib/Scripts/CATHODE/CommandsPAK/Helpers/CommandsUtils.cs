using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CATHODE.Scripting
{
    //Helpful lookup tables for various Cathode Commands types
    public static class CommandsUtils
    {
        static CommandsUtils()
        {
            SetupFunctionTypeLUT();
            SetupDataTypeLUT();
            SetupObjectTypeLUT();
            SetupResourceEntryTypeLUT();
        }

        #region FUNCTION_TYPE_UTILS
        /* Function Types */
        private static Dictionary<ShortGuid, FunctionType> _functionTypeLUT = new Dictionary<ShortGuid, FunctionType>();
        private static void SetupFunctionTypeLUT()
        {
            if (_functionTypeLUT.Count != 0) return;

            foreach (FunctionType functionType in Enum.GetValues(typeof(FunctionType)))
            {
                string shortGuidString = functionType.ToString();
                if (functionType == FunctionType.GCIP_WorldPickup) 
                    shortGuidString = "n:\\content\\build\\library\\archetypes\\gameplay\\gcip_worldpickup";
                if (functionType == FunctionType.PlayForMinDuration)
                    shortGuidString = "n:\\content\\build\\library\\ayz\\animation\\logichelpers\\playforminduration";
                if (functionType == FunctionType.Torch_Control)
                    shortGuidString = "n:\\content\\build\\library\\archetypes\\script\\gameplay\\torch_control";

                _functionTypeLUT.Add(ShortGuidUtils.Generate(shortGuidString), functionType);
            }
        }
        public static FunctionType GetFunctionType(byte[] tag)
        {
            return GetFunctionType(new ShortGuid(tag));
        }
        public static FunctionType GetFunctionType(ShortGuid tag)
        {
            SetupFunctionTypeLUT();
            return _functionTypeLUT[tag];
        }
        public static ShortGuid GetFunctionTypeGUID(FunctionType type)
        {
            SetupFunctionTypeLUT();
            return _functionTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        public static bool FunctionTypeExists(ShortGuid tag)
        {
            return _functionTypeLUT.ContainsKey(tag);
        }
        #endregion

        #region DATATYPE_TYPE_UTILS
        /* Data Types */
        private static Dictionary<ShortGuid, DataType> _dataTypeLUT = new Dictionary<ShortGuid, DataType>();
        private static void SetupDataTypeLUT()
        {
            if (_dataTypeLUT.Count != 0) return;

            _dataTypeLUT.Add(ShortGuidUtils.Generate("bool"), DataType.BOOL);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("int"), DataType.INTEGER);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("float"), DataType.FLOAT);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("String"), DataType.STRING);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("FilePath"), DataType.FILEPATH);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("SplineData"), DataType.SPLINE);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Direction"), DataType.VECTOR);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Position"), DataType.TRANSFORM);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Enum"), DataType.ENUM);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("ShortGuid"), DataType.RESOURCE);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("Object"), DataType.OBJECT);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("ZonePtr"), DataType.ZONE_PTR);
            _dataTypeLUT.Add(ShortGuidUtils.Generate("ZoneLinkPtr"), DataType.ZONE_LINK_PTR);
            _dataTypeLUT.Add(ShortGuidUtils.Generate(""), DataType.NONE);
        }
        public static DataType GetDataType(byte[] tag)
        {
            return GetDataType(new ShortGuid(tag));
        }
        public static DataType GetDataType(ShortGuid tag)
        {
            SetupDataTypeLUT();
            return _dataTypeLUT[tag];
        }
        public static ShortGuid GetDataTypeGUID(DataType type)
        {
            SetupDataTypeLUT();
            return _dataTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        public static bool DataTypeExists(ShortGuid tag)
        {
            return _dataTypeLUT.ContainsKey(tag);
        }
        #endregion

        #region OBJECT_TYPE_UTILS
        /* Object Types */
        private static Dictionary<ShortGuid, ObjectType> _objectTypeLUT = new Dictionary<ShortGuid, ObjectType>();
        private static void SetupObjectTypeLUT()
        {
            if (_objectTypeLUT.Count != 0) return;

            _objectTypeLUT.Add(ShortGuidUtils.Generate(""), ObjectType.ENTITY);
            _objectTypeLUT.Add(ShortGuidUtils.Generate("Marker"), ObjectType.MARKER);
            _objectTypeLUT.Add(ShortGuidUtils.Generate("Character"), ObjectType.CHARACTER);
            _objectTypeLUT.Add(ShortGuidUtils.Generate("Camera"), ObjectType.CAMERA);
        }
        public static ObjectType GetObjectType(byte[] tag)
        {
            return GetObjectType(new ShortGuid(tag));
        }
        public static ObjectType GetObjectType(ShortGuid tag)
        {
            SetupObjectTypeLUT();
            return _objectTypeLUT[tag];
        }
        public static ShortGuid GetObjectTypeGUID(ObjectType type)
        {
            SetupObjectTypeLUT();
            return _objectTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        public static bool DataObjectExists(ShortGuid tag)
        {
            return _objectTypeLUT.ContainsKey(tag);
        }
        #endregion

        #region RESOURCE_TYPE_UTILS
        /* Resource Reference Types */
        private static Dictionary<ShortGuid, ResourceType> _resourceReferenceTypeLUT = new Dictionary<ShortGuid, ResourceType>();
        private static void SetupResourceEntryTypeLUT()
        {
            if (_resourceReferenceTypeLUT.Count != 0) return;

            foreach (ResourceType referenceType in Enum.GetValues(typeof(ResourceType)))
                _resourceReferenceTypeLUT.Add(ShortGuidUtils.Generate(referenceType.ToString()), referenceType);
        }
        public static ResourceType GetResourceEntryType(byte[] tag)
        {
            return GetResourceEntryType(new ShortGuid(tag));
        }
        public static ResourceType GetResourceEntryType(ShortGuid tag)
        {
            SetupResourceEntryTypeLUT();
            return _resourceReferenceTypeLUT[tag];
        }
        public static ShortGuid GetResourceEntryTypeGUID(ResourceType type)
        {
            SetupResourceEntryTypeLUT();
            return _resourceReferenceTypeLUT.FirstOrDefault(x => x.Value == type).Key;
        }
        #endregion

        #region HELPER_FUNCS
        /* Resolve an entity hierarchy */
        public static Entity ResolveHierarchy(Commands commands, Composite composite, List<ShortGuid> hierarchy, out Composite containedComposite, out string asString, bool includeShortGuids = true)
        {
            if (hierarchy.Count == 0)
            {
                containedComposite = null;
                asString = "";
                return null;
            }

            List<ShortGuid> hierarchyCopy = new List<ShortGuid>();
            for (int x = 0; x < hierarchy.Count; x++)
                hierarchyCopy.Add(new ShortGuid((byte[])hierarchy[x].val.Clone()));

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
                if (includeShortGuids) hierarchyString += "[" + entity.shortGUID + "] ";
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

        /* CA's CAGE doesn't properly tidy up hierarchies pointing to deleted entities - so we can do that to save confusion */
        public static void PurgeDeadLinks(Commands commands, Composite composite)
        {
            int originalUnknownCount = 0;
            int originalProxyCount = 0;
            int newProxyCount = 0;
            int originalOverrideCount = 0;
            int newOverrideCount = 0;
            int originalTriggerCount = 0;
            int newTriggerCount = 0;
            int originalAnimCount = 0;
            int newAnimCount = 0;
            int originalLinkCount = 0;
            int newLinkCount = 0;
            int originalFuncCount = 0;
            int newFuncCount = 0;

            //Clear functions
            List<FunctionEntity> functionsPurged = new List<FunctionEntity>();
            for (int i = 0; i < composite.functions.Count; i++)
                if (CommandsUtils.FunctionTypeExists(composite.functions[i].function) || commands.GetComposite(composite.functions[i].function) != null)
                    functionsPurged.Add(composite.functions[i]);
            originalFuncCount += composite.functions.Count;
            newFuncCount += functionsPurged.Count;
            composite.functions = functionsPurged;

            //Clear overrides
            List<OverrideEntity> overridePurged = new List<OverrideEntity>();
            for (int i = 0; i < composite.overrides.Count; i++)
                if (ResolveHierarchy(commands, composite, composite.overrides[i].connectedEntity.hierarchy, out Composite flowTemp, out string hierarchy) != null)
                    overridePurged.Add(composite.overrides[i]);
            originalOverrideCount += composite.overrides.Count;
            newOverrideCount += overridePurged.Count;
            composite.overrides = overridePurged;

            //Clear proxies
            List<ProxyEntity> proxyPurged = new List<ProxyEntity>();
            for (int i = 0; i < composite.proxies.Count; i++)
                if (ResolveHierarchy(commands, composite, composite.proxies[i].connectedEntity.hierarchy, out Composite flowTemp, out string hierarchy) != null)
                    proxyPurged.Add(composite.proxies[i]);
            originalProxyCount += composite.proxies.Count;
            newProxyCount += proxyPurged.Count;
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
                            if (ResolveHierarchy(commands, composite, trig.entities[x].connectedEntity.hierarchy, out Composite flowTemp, out string hierarchy) != null)
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
                                ResolveHierarchy(commands, composite, anim.connections[x].connectedEntity.hierarchy, out Composite flowTemp, out string hierarchy) != null)
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
                List<EntityLink> childLinksPurged = new List<EntityLink>();
                for (int x = 0; x < entities[i].childLinks.Count; x++)
                    if (composite.GetEntityByID(entities[i].childLinks[x].childID) != null)
                        childLinksPurged.Add(entities[i].childLinks[x]);
                originalLinkCount += entities[i].childLinks.Count;
                newLinkCount += childLinksPurged.Count;
                entities[i].childLinks = childLinksPurged;
            }

            if (originalUnknownCount +
                (originalFuncCount - newFuncCount) +
                (originalProxyCount - newProxyCount) +
                (originalOverrideCount - newOverrideCount) +
                (originalTriggerCount - newTriggerCount) +
                (originalAnimCount - newAnimCount) +
                (originalLinkCount - newLinkCount) == 0)
                return;
            Console.WriteLine(
                "Purged all dead hierarchies and entities in " + composite.name + "!" +
                "\n - " + originalUnknownCount + " unknown entities" +
                "\n - " + (originalFuncCount - newFuncCount) + " functions (of " + originalFuncCount + ")" +
                "\n - " + (originalProxyCount - newProxyCount) + " proxies (of " + originalProxyCount + ")" +
                "\n - " + (originalOverrideCount - newOverrideCount) + " overrides (of " + originalOverrideCount + ")" +
                "\n - " + (originalTriggerCount - newTriggerCount) + " triggers (of " + originalTriggerCount + ")" +
                "\n - " + (originalAnimCount - newAnimCount) + " anim connections (of " + originalAnimCount + ")" +
                "\n - " + (originalLinkCount - newLinkCount) + " entity links (of " + originalLinkCount + ")");
        }
        #endregion
    }
}
