using CATHODE.Assets.Utilities;
using CATHODE.Commands;
using CathodeLib.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace CATHODE.Commands
{
    /* An entity in a composite */
    [Serializable]
    public class Entity : IComparable<Entity>
    {
        public Entity(ShortGuid id)
        {
            shortGUID = id;
        }

        public ShortGuid shortGUID; //Translates to string via EntityNameLookup.GetEntityName
        public EntityVariant variant;

        public List<EntityLink> childLinks = new List<EntityLink>();
        public List<Parameter> parameters = new List<Parameter>();

        /* Implements IComparable for searching */
        public int CompareTo(Entity other)
        {
            int TotalThis = shortGUID.val[0] + shortGUID.val[1] + shortGUID.val[2] + shortGUID.val[3];
            int TotalOther = other.shortGUID.val[0] + other.shortGUID.val[1] + other.shortGUID.val[2] + other.shortGUID.val[3];
            if (TotalThis > TotalOther) return 1;
            else if (TotalThis == TotalOther) return 0;
            return -1;
        }

        /* Get parameter by string name or ShortGuid */
        public Parameter GetParameter(string name)
        {
            ShortGuid id = ShortGuidUtils.Generate(name);
            return GetParameter(id);
        }
        public Parameter GetParameter(ShortGuid id)
        {
            return parameters.FirstOrDefault(o => o.shortGUID == id);
        }

        /* Add a parameter by string name or ShortGuid, and return it */
        public Parameter AddParameter(string name, ParameterData data, ParameterVariant variant = ParameterVariant.PARAMETER)
        {
            ShortGuid id = ShortGuidUtils.Generate(name);
            return AddParameter(id, data, variant);
        }
        public Parameter AddParameter(ShortGuid id, ParameterData data, ParameterVariant variant = ParameterVariant.PARAMETER)
        {
            //We can only have one parameter matching a name/guid per entity, so if it already exists, we just return that, regardless of the datatype
            Parameter param = GetParameter(id);
            if (param == null)
            {
                param = new Parameter(id, data, variant);
                parameters.Add(param);
            }
            else
            {
                Console.WriteLine("WARNING: Updating data and variant type in parameter " + id);
                param.content = data;
                param.variant = variant;
            }
            return param;
        }
    }
    [Serializable]
    public class VariableEntity : Entity
    {
        public VariableEntity(ShortGuid id) : base(id) { variant = EntityVariant.DATATYPE; }
        public ShortGuid parameter; //Translates to string via ShortGuidUtils.FindString
        public DataType type = DataType.NONE;
    }
    [Serializable]
    public class FunctionEntity : Entity
    {
        public FunctionEntity(ShortGuid id) : base(id) { variant = EntityVariant.FUNCTION; }
        public ShortGuid function; //Translates to string via ShortGuidUtils.FindString
        public List<ResourceReference> resources = new List<ResourceReference>(); //TODO: can we replace this with a cResource to save duplicating functionality?

        /* Add a new resource reference of type */
        public ResourceReference AddResource(ResourceType type)
        {
            //We can only have one type of resource reference per function entity, so if it already exists, we just return the existing one.
            ResourceReference rr = GetResource(type);
            if (rr == null)
            {
                rr = new ResourceReference(type);
                rr.resourceID = shortGUID;
                switch (rr.entryType)
                {
                    case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                    case ResourceType.RENDERABLE_INSTANCE:
                    case ResourceType.ANIMATED_MODEL:
                        rr.startIndex = 0;
                        break;
                }
                resources.Add(rr);
            }
            return rr;
        }

        /* Find a resource reference of type */
        public ResourceReference GetResource(ResourceType type)
        {
            return resources.FirstOrDefault(o => o.entryType == type);
        }
    }
    [Serializable]
    public class ProxyEntity : Entity
    {
        public ProxyEntity(ShortGuid id) : base(id) { variant = EntityVariant.PROXY; }
        public ShortGuid extraId; //TODO: I'm unsure if this is actually used by the game - we might not need to store it and just make up something when we write.
        public List<ShortGuid> hierarchy = new List<ShortGuid>();
    }
    [Serializable]
    public class OverrideEntity : Entity
    {
        public OverrideEntity(ShortGuid id) : base(id) { variant = EntityVariant.OVERRIDE; }
        public ShortGuid checksum; //TODO: This value is apparently a hash of the hierarchy GUIDs, but need to verify that, and work out the salt.
        public List<ShortGuid> hierarchy = new List<ShortGuid>();
    }

    #region SPECIAL FUNCTION ENTITIES
    [Serializable]
    public class CAGEAnimation : FunctionEntity
    {
        public CAGEAnimation(ShortGuid id) : base(id) { function = ShortGuidUtils.Generate("CAGEAnimation"); }
        public List<CathodeParameterKeyframeHeader> keyframeHeaders = new List<CathodeParameterKeyframeHeader>();
        public List<CathodeParameterKeyframe> keyframeData = new List<CathodeParameterKeyframe>();
        public List<TEMP_CAGEAnimationExtraDataHolder3> paramsData3 = new List<TEMP_CAGEAnimationExtraDataHolder3>(); //events?
    }
    [Serializable]
    public class TriggerSequence : FunctionEntity
    {
        public TriggerSequence(ShortGuid id) : base(id) { function = ShortGuidUtils.Generate("TriggerSequence"); }
        public List<CathodeTriggerSequenceTrigger> triggers = new List<CathodeTriggerSequenceTrigger>();
        public List<CathodeTriggerSequenceEvent> events = new List<CathodeTriggerSequenceEvent>();
    }
    #endregion

    [Serializable]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct EntityLink
    {
        public ShortGuid connectionID;  //The unique ID for this connection
        public ShortGuid parentParamID; //The ID of the parameter we're providing out 
        public ShortGuid childParamID;  //The ID of the parameter we're providing into the child
        public ShortGuid childID;       //The ID of the entity we're linking to to provide the value for
    }
}
