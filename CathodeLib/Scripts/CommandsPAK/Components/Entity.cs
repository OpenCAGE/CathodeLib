using CATHODE.Assets.Utilities;
using CATHODE.Commands;
using System;
using System.Collections.Generic;
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

        public int CompareTo(Entity other)
        {
            int TotalThis = shortGUID.val[0] + shortGUID.val[1] + shortGUID.val[2] + shortGUID.val[3];
            int TotalOther = other.shortGUID.val[0] + other.shortGUID.val[1] + other.shortGUID.val[2] + other.shortGUID.val[3];
            if (TotalThis > TotalOther) return 1;
            else if (TotalThis == TotalOther) return 0;
            return -1;
        }
    }
    [Serializable]
    public class DatatypeEntity : Entity
    {
        public DatatypeEntity(ShortGuid id) : base(id) { variant = EntityVariant.DATATYPE; }
        public DataType type = DataType.NO_TYPE;
        public ShortGuid parameter; //Translates to string via ShortGuidUtils.FindString
    }
    [Serializable]
    public class FunctionEntity : Entity
    {
        public FunctionEntity(ShortGuid id) : base(id) { variant = EntityVariant.FUNCTION; }
        public ShortGuid function;
        public List<ResourceReference> resources = new List<ResourceReference>();
    }
    [Serializable]
    public class ProxyEntity : Entity
    {
        public ProxyEntity(ShortGuid id) : base(id) { variant = EntityVariant.PROXY; }
        public ShortGuid extraId;
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
