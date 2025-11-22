using CATHODE;
using CATHODE.Enums;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace CathodeLib
{
    public class InstancedEntity
    {
        public class Parameters<T>
        { 
            //Values set on the entity itself at initialisation time
            public Dictionary<string, T> Values = new Dictionary<string, T>();
            
            //Any links to other entities that set parameter values
            public Dictionary<string, List<Tuple<string, InstancedEntity>>> Links = new Dictionary<string, List<Tuple<string, InstancedEntity>>>();

            public T Get(string name)
            {
                //Check links first, these override the values
                if (Links.TryGetValue(name, out List<Tuple<string, InstancedEntity>> links))
                    if (links.Count != 0)
                        return links[0].Item2.GetAs<T>(links[0].Item1); //temp - filters accept multiple links

                //Fall back to our own value
                if (Values.TryGetValue(name, out T val))
                    return val;

                throw new Exception("Failed to find param."); //can just return false here i guess and hope for the best
            }

            public List<InstancedEntity> GetLinks(string name)
            {
                List<InstancedEntity> entities = new List<InstancedEntity>();
                if (Links.TryGetValue(name, out List<Tuple<string, InstancedEntity>> ents))
                {
                    for (int i = 0; i < ents.Count; i++)
                    {
                        entities.Add(ents[i].Item2);
                    }
                }
                return entities;
            }
        }

        public Parameters<bool> Bools = new Parameters<bool>();
        public Parameters<int> Integers = new Parameters<int>();
        public Parameters<float> Floats = new Parameters<float>();
        public Parameters<int> EnumIndexes = new Parameters<int>();
        public Parameters<Vector3> Vectors = new Parameters<Vector3>();

        public Level Level;
        public Entity Entity;
        public EntityPath Path;
        public Composite Composite;

        //TODO: also load in MVR etc here?

        public InstancedComposite ThisCompositeInstance; //The instanced composite containing this instanced entity
        public InstancedComposite ChildCompositeInstance; //The instanced composite which is created by this entity

        private HashSet<(ShortGuid, ParameterVariant, DataType)> _parameters = new HashSet<(ShortGuid, ParameterVariant, DataType)>();

        public InstancedEntity(Level level, Composite composite, Entity entity, EntityPath path)
        {
            Level = level;
            Entity = entity;
            Path = path;
            Composite = composite;

            //NOTE: GetAllParameters does not check for duplicates, so do that now - need to fix that.
            // An example of another issue is {UI_ReactionGame} - the child UI_Attached should not add another 'success' entry
            var parameters = Level.Commands.Utils.GetAllParameters(entity, composite);
            foreach (var entry in parameters)
                _parameters.Add(entry);

            //Get all boolean values on this entity
            foreach ((ShortGuid guid, ParameterVariant variant, DataType datatype) in _parameters)
            {
                switch (datatype)
                {
                    //should really make this a utility
                    case DataType.BOOL:
                        {
                            bool value = false;
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.INTEGER:
                                    value = ((cInteger)p.content).value == 1;
                                    break;
                                case DataType.FLOAT:
                                    value = ((cFloat)p.content).value == 1.0f;
                                    break;
                                case DataType.BOOL:
                                    value = ((cBool)p.content).value;
                                    break;
                                case DataType.STRING:
                                    value = ((cString)p.content).value.ToUpper() == "TRUE";
                                    break;
                                default:
                                    value = ((cBool)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid)).value;
                                    break;
                            }
                            Bools.Values.Add(guid.ToString(), value);
                        }
                        break;
                    case DataType.INTEGER:
                        {
                            int value = 0;
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.ENUM:
                                    value = ((cEnum)p.content).enumIndex;
                                    break;
                                case DataType.INTEGER:
                                    value = ((cInteger)p.content).value;
                                    break;
                                case DataType.FLOAT:
                                    value = (int)((cFloat)p.content).value;
                                    break;
                                case DataType.BOOL:
                                    value = ((cBool)p.content).value ? 1 : 0;
                                    break;
                                case DataType.STRING:
                                    try
                                    {
                                        value = Convert.ToInt32(((cString)p.content).value);
                                    }
                                    catch { }
                                    break;
                                default:
                                    value = ((cInteger)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid)).value;
                                    break;
                            }
                            Integers.Values.Add(guid.ToString(), value);
                        }
                        break;
                    case DataType.FLOAT:
                        {
                            float value = 0.0f;
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.ENUM:
                                    value = ((cEnum)p.content).enumIndex;
                                    break;
                                case DataType.INTEGER:
                                    value = ((cInteger)p.content).value;
                                    break;
                                case DataType.FLOAT:
                                    value = ((cFloat)p.content).value;
                                    break;
                                case DataType.BOOL:
                                    value = ((cBool)p.content).value ? 1 : 0;
                                    break;
                                case DataType.STRING:
                                    try
                                    {
                                        //note - we hit this a lot as seemingly reference is often a string but flagged in our logic a float
                                        value = Convert.ToSingle(((cString)p.content).value);
                                    }
                                    catch { }
                                    break;
                                default:
                                    value = ((cFloat)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid)).value;
                                    break;
                            }
                            if (!Floats.Values.ContainsKey(guid.ToString())) //todo - deprecate this when the hashset above is fixed
                                Floats.Values.Add(guid.ToString(), value);
                        }
                        break;
                    case DataType.ENUM:
                        {
                            int value = 0;
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.ENUM:
                                    value = ((cEnum)p.content).enumIndex;
                                    break;
                                case DataType.INTEGER:
                                    value = ((cInteger)p.content).value;
                                    break;
                                case DataType.FLOAT:
                                    value = (int)((cFloat)p.content).value;
                                    break;
                                case DataType.BOOL:
                                    value = ((cBool)p.content).value ? 1 : 0;
                                    break;
                                case DataType.STRING:
                                    try
                                    {
                                        value = Convert.ToInt32(((cString)p.content).value); //todo - if this is ever string, it's probably actually the enum as a string. need to check if that's even supported.
                                    }
                                    catch { }
                                    break;
                                default:
                                    value = ((cEnum)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid)).enumIndex;
                                    break;
                            }
                            Integers.Values.Add(guid.ToString(), value);
                        }
                        break;
                    case DataType.VECTOR:
                        {
                            Vector3 value = new Vector3();
                            Parameter p = entity.GetParameter(guid);
                            switch (p?.content?.dataType)
                            {
                                case DataType.VECTOR:
                                    value = ((cVector3)p.content).value;
                                    break;
                                case DataType.TRANSFORM:
                                    value = ((cTransform)p.content).position;
                                    break;
                                default:
                                    value = ((cVector3)Level.Commands.Utils.CreateDefaultParameterData(entity, composite, guid)).value;
                                    break;
                            }
                            Vectors.Values.Add(guid.ToString(), value);
                        }
                        break;
                }

            }

            //TODO: need to handle triggersequences a bit different i think? they can apply parameter data down
        }

        public void PopulateLinks(List<InstancedEntity> entities)
        {
            foreach ((ShortGuid guid, ParameterVariant variant, DataType datatype) in _parameters)
            {
                List<EntityConnector> links = Entity.childLinks.FindAll(o => o.thisParamID == guid);
                if (links.Count == 0)
                    continue;

                List<Tuple<string, InstancedEntity>> linksParsed = new List<Tuple<string, InstancedEntity>>();
                for (int i = 0; i < links.Count; i++)
                {
                    Entity connectedEnt = Composite.GetEntityByID(links[i].linkedEntityID);
                    if (connectedEnt == null) continue;
                    linksParsed.Add(new Tuple<string, InstancedEntity>(links[i].linkedParamID.ToString(), entities.FirstOrDefault(o => o.Entity == connectedEnt)));
                }

                switch (datatype)
                {
                    case DataType.BOOL:
                        Bools.Links.Add(guid.ToString(), linksParsed);
                        break;
                    case DataType.INTEGER:
                        Integers.Links.Add(guid.ToString(), linksParsed);
                        break;
                    case DataType.FLOAT:
                        if (!Floats.Links.ContainsKey(guid.ToString())) //todo - deprecate this when the hashset above is fixed
                            Floats.Links.Add(guid.ToString(), linksParsed);
                        break;
                    case DataType.ENUM:
                        EnumIndexes.Links.Add(guid.ToString(), linksParsed);
                        break;
                    case DataType.VECTOR:
                        Vectors.Links.Add(guid.ToString(), linksParsed);
                        break;
                }
            }
        }

        public T GetAs<T>(string name = "reference")
        {
            switch (Entity.variant)
            {
                case EntityVariant.FUNCTION:
                    {
                        FunctionEntity func = (FunctionEntity)Entity;
                        if (func.function.IsFunctionType)
                        {
                            switch (func.function.AsFunctionType)
                            {
                                case FunctionType.AccessTerminal:
                                    break;
                                case FunctionType.AchievementMonitor:
                                    break;
                                case FunctionType.AchievementStat:
                                    break;
                                case FunctionType.AchievementUniqueCounter:
                                    break;
                                case FunctionType.AddExitObjective:
                                    break;
                                case FunctionType.AddItemsToGCPool:
                                    break;
                                case FunctionType.AddToInventory:
                                    break;
                                case FunctionType.AILightCurveSettings:
                                    break;
                                case FunctionType.AIMED_ITEM:
                                    break;
                                case FunctionType.AIMED_WEAPON:
                                    break;
                                case FunctionType.ALLIANCE_ResetAll:
                                    break;
                                case FunctionType.ALLIANCE_SetDisposition:
                                    break;
                                case FunctionType.AllocateGCItemFromPoolBySubset:
                                    break;
                                case FunctionType.AllocateGCItemsFromPool:
                                    break;
                                case FunctionType.AllPlayersReady:
                                    break;
                                case FunctionType.AnimatedModelAttachmentNode:
                                    break;
                                case FunctionType.AnimationMask:
                                    break;
                                case FunctionType.ApplyRelativeTransform:
                                    break;
                                case FunctionType.AreaHitMonitor:
                                    break;
                                case FunctionType.AssetSpawner:
                                    break;
                                case FunctionType.AttachmentInterface:
                                    break;
                                case FunctionType.Benchmark:
                                    break;
                                case FunctionType.BindObjectsMultiplexer:
                                    break;
                                case FunctionType.BlendLowResFrame:
                                    break;
                                case FunctionType.BloomSettings:
                                    break;
                                case FunctionType.BoneAttachedCamera:
                                    break;
                                case FunctionType.BooleanLogicInterface:
                                    break;
                                case FunctionType.BooleanLogicOperation:
                                    break;
                                case FunctionType.Box:
                                    if (typeof(T) == typeof(int))
                                    {

                                    }
                                    break;
                                case FunctionType.BroadcastTrigger:
                                    break;
                                case FunctionType.BulletChamber:
                                    break;
                                case FunctionType.ButtonMashPrompt:
                                    break;
                                case FunctionType.CAGEAnimation:
                                    break;
                                case FunctionType.CameraAimAssistant:
                                    break;
                                case FunctionType.CameraBehaviorInterface:
                                    break;
                                case FunctionType.CameraCollisionBox:
                                    if (typeof(T) == typeof(int))
                                    {

                                    }
                                    break;
                                case FunctionType.CameraDofController:
                                    break;
                                case FunctionType.CameraFinder:
                                    break;
                                case FunctionType.CameraPath:
                                    break;
                                case FunctionType.CameraPathDriven:
                                    break;
                                case FunctionType.CameraPlayAnimation:
                                    break;
                                case FunctionType.CameraResource:
                                    break;
                                case FunctionType.CameraShake:
                                    break;
                                case FunctionType.CamPeek:
                                    break;
                                case FunctionType.Character:
                                    break;
                                case FunctionType.CharacterAttachmentNode:
                                    break;
                                case FunctionType.CharacterCommand:
                                    break;
                                case FunctionType.CharacterMonitor:
                                    break;
                                case FunctionType.CharacterShivaArms:
                                    break;
                                case FunctionType.CharacterTypeMonitor:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false; 
                                    break;
                                case FunctionType.Checkpoint:
                                    break;
                                case FunctionType.CheckpointRestoredNotify:
                                    break;
                                case FunctionType.ChokePoint:
                                    break;
                                case FunctionType.CHR_DamageMonitor:
                                    break;
                                case FunctionType.CHR_DeathMonitor:
                                    break;
                                case FunctionType.CHR_DeepCrouch:
                                    break;
                                case FunctionType.CHR_GetAlliance:
                                    break;
                                case FunctionType.CHR_GetHealth:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)0;
                                    break;
                                case FunctionType.CHR_GetTorch:
                                    break;
                                case FunctionType.CHR_HasWeaponOfType:
                                    break;
                                case FunctionType.CHR_HoldBreath:
                                    break;
                                case FunctionType.CHR_IsWithinRange:
                                    break;
                                case FunctionType.CHR_KnockedOutMonitor:
                                    break;
                                case FunctionType.CHR_LocomotionDuck:
                                    break;
                                case FunctionType.CHR_LocomotionEffect:
                                    break;
                                case FunctionType.CHR_LocomotionModifier:
                                    break;
                                case FunctionType.CHR_ModifyBreathing:
                                    break;
                                case FunctionType.Chr_PlayerCrouch:
                                    break;
                                case FunctionType.CHR_PlayNPCBark:
                                    break;
                                case FunctionType.CHR_PlaySecondaryAnimation:
                                    break;
                                case FunctionType.CHR_RetreatMonitor:
                                    break;
                                case FunctionType.CHR_SetAlliance:
                                    break;
                                case FunctionType.CHR_SetAndroidThrowTarget:
                                    break;
                                case FunctionType.CHR_SetDebugDisplayName:
                                    break;
                                case FunctionType.CHR_SetFacehuggerAggroRadius:
                                    break;
                                case FunctionType.CHR_SetFocalPoint:
                                    break;
                                case FunctionType.CHR_SetHeadVisibility:
                                    break;
                                case FunctionType.CHR_SetHealth:
                                    break;
                                case FunctionType.CHR_SetInvincibility:
                                    break;
                                case FunctionType.CHR_SetMood:
                                    break;
                                case FunctionType.CHR_SetShowInMotionTracker:
                                    break;
                                case FunctionType.CHR_SetSubModelVisibility:
                                    break;
                                case FunctionType.CHR_SetTacticalPosition:
                                    break;
                                case FunctionType.CHR_SetTacticalPositionToTarget:
                                    break;
                                case FunctionType.CHR_SetTorch:
                                    break;
                                case FunctionType.CHR_TakeDamage:
                                    break;
                                case FunctionType.CHR_TorchMonitor:
                                    break;
                                case FunctionType.CHR_VentMonitor:
                                    break;
                                case FunctionType.CHR_WeaponFireMonitor:
                                    break;
                                case FunctionType.ChromaticAberrations:
                                    break;
                                case FunctionType.ClearPrimaryObjective:
                                    break;
                                case FunctionType.ClearSubObjective:
                                    break;
                                case FunctionType.ClipPlanesController:
                                    break;
                                case FunctionType.CloseableInterface:
                                    break;
                                case FunctionType.CMD_AimAt:
                                    break;
                                case FunctionType.CMD_AimAtCurrentTarget:
                                    break;
                                case FunctionType.CMD_Die:
                                    break;
                                case FunctionType.CMD_Follow:
                                    break;
                                case FunctionType.CMD_FollowUsingJobs:
                                    break;
                                case FunctionType.CMD_ForceMeleeAttack:
                                    break;
                                case FunctionType.CMD_ForceReloadWeapon:
                                    break;
                                case FunctionType.CMD_GoTo:
                                    break;
                                case FunctionType.CMD_GoToCover:
                                    break;
                                case FunctionType.CMD_HolsterWeapon:
                                    break;
                                case FunctionType.CMD_Idle:
                                    break;
                                case FunctionType.CMD_LaunchMeleeAttack:
                                    break;
                                case FunctionType.CMD_ModifyCombatBehaviour:
                                    break;
                                case FunctionType.CMD_MoveTowards:
                                    break;
                                case FunctionType.CMD_PlayAnimation:
                                    break;
                                case FunctionType.CMD_Ragdoll:
                                    break;
                                case FunctionType.CMD_ShootAt:
                                    break;
                                case FunctionType.CMD_StopScript:
                                    break;
                                case FunctionType.CollectIDTag:
                                    break;
                                case FunctionType.CollectNostromoLog:
                                    break;
                                case FunctionType.CollectSevastopolLog:
                                    break;
                                case FunctionType.CollisionBarrier:
                                    break;
                                case FunctionType.ColourCorrectionTransition:
                                    break;
                                case FunctionType.ColourSettings:
                                    break;
                                case FunctionType.CompositeInterface:
                                    break;
                                case FunctionType.CompoundVolume:
                                    break;
                                case FunctionType.ControllableRange:
                                    break;
                                case FunctionType.Convo:
                                    break;
                                case FunctionType.Counter:
                                    break;
                                case FunctionType.CoverExclusionArea:
                                    break;
                                case FunctionType.CoverLine:
                                    break;
                                case FunctionType.Custom_Hiding_Controller:
                                    break;
                                case FunctionType.Custom_Hiding_Vignette_controller:
                                    break;
                                case FunctionType.DayToneMappingSettings:
                                    break;
                                case FunctionType.DEBUG_SenseLevels:
                                    break;
                                case FunctionType.DebugCamera:
                                    break;
                                case FunctionType.DebugCaptureCorpse:
                                    break;
                                case FunctionType.DebugCaptureScreenShot:
                                    break;
                                case FunctionType.DebugCheckpoint:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)Bools.Get("level_reset");
                                    break;
                                case FunctionType.DebugEnvironmentMarker:
                                    break;
                                case FunctionType.DebugGraph:
                                    break;
                                case FunctionType.DebugLoadCheckpoint:
                                    break;
                                case FunctionType.DebugMenuToggle:
                                    break;
                                case FunctionType.DebugObjectMarker:
                                    break;
                                case FunctionType.DebugPositionMarker:
                                    break;
                                case FunctionType.DebugText:
                                    break;
                                case FunctionType.DebugTextStacking:
                                    break;
                                case FunctionType.DeleteBlankPanel:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
                                        switch (door_mechanism)
                                        {
                                            case DOOR_MECHANISM.BLANK:
                                                return (T)(object)false;
                                            default:
                                                return (T)(object)true;
                                        }
                                    }
                                    break;
                                case FunctionType.DeleteButtonDisk:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        BUTTON_TYPE button_type = (BUTTON_TYPE)EnumIndexes.Get("button_type");
                                        if (button_type != BUTTON_TYPE.DISK) return (T)(object)true;

                                        DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
                                        switch (door_mechanism)
                                        {
                                            case DOOR_MECHANISM.HIDDEN_BUTTON:
                                            case DOOR_MECHANISM.BUTTON:
                                                return (T)(object)false;
                                            default:
                                                return (T)(object)true;
                                        }
                                    }
                                    break;
                                case FunctionType.DeleteButtonKeys:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        BUTTON_TYPE button_type = (BUTTON_TYPE)EnumIndexes.Get("button_type");
                                        if (button_type != BUTTON_TYPE.KEYS) return (T)(object)true;

                                        DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism"); 
                                        switch (door_mechanism)
                                        {
                                            case DOOR_MECHANISM.HIDDEN_BUTTON:
                                            case DOOR_MECHANISM.BUTTON: 
                                                return (T)(object)false;
                                            default:
                                                return (T)(object)true;
                                        }
                                    }
                                    break;
                                case FunctionType.DeleteCuttingPanel:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
                                        switch (door_mechanism)
                                        {
                                            case DOOR_MECHANISM.HIDDEN_BUTTON:
                                            case DOOR_MECHANISM.HIDDEN_KEYPAD:
                                            case DOOR_MECHANISM.HIDDEN_HACKING:
                                            case DOOR_MECHANISM.HIDDEN_LEVER:
                                                return (T)(object)false;
                                            default:
                                                return (T)(object)true;
                                        }
                                    }
                                    break;
                                case FunctionType.DeleteHacking:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
                                        switch (door_mechanism)
                                        {
                                            case DOOR_MECHANISM.HACKING:
                                            case DOOR_MECHANISM.HIDDEN_HACKING:
                                                return (T)(object)false;
                                            default:
                                                return (T)(object)true;
                                        }
                                    }
                                    break;
                                case FunctionType.DeleteHousing:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        if (!Bools.Get("is_door")) return (T)(object)true;

                                        DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
                                        switch (door_mechanism)
                                        {
                                            case DOOR_MECHANISM.HIDDEN_BUTTON:
                                            case DOOR_MECHANISM.HIDDEN_KEYPAD:
                                            case DOOR_MECHANISM.HIDDEN_HACKING:
                                            case DOOR_MECHANISM.HIDDEN_LEVER:
                                            case DOOR_MECHANISM.BUTTON:
                                            case DOOR_MECHANISM.KEYPAD:
                                            case DOOR_MECHANISM.HACKING:
                                            case DOOR_MECHANISM.LEVER:
                                                return (T)(object)false;
                                            default:
                                                return (T)(object)true;
                                        }
                                    }
                                    break;
                                case FunctionType.DeleteKeypad:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
                                        switch (door_mechanism)
                                        {
                                            case DOOR_MECHANISM.KEYPAD:
                                            case DOOR_MECHANISM.HIDDEN_KEYPAD:
                                                return (T)(object)false;
                                            default:
                                                return (T)(object)true;
                                        }
                                    }
                                    break;
                                case FunctionType.DeletePullLever:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        LEVER_TYPE lever_type = (LEVER_TYPE)EnumIndexes.Get("lever_type");
                                        if (lever_type != LEVER_TYPE.PULL) return (T)(object)true;

                                        DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
                                        switch (door_mechanism)
                                        {
                                            case DOOR_MECHANISM.HIDDEN_LEVER:
                                            case DOOR_MECHANISM.LEVER:
                                                return (T)(object)false;
                                            default:
                                                return (T)(object)true;
                                        }
                                    }
                                    break;
                                case FunctionType.DeleteRotateLever:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        LEVER_TYPE lever_type = (LEVER_TYPE)EnumIndexes.Get("lever_type");
                                        if (lever_type != LEVER_TYPE.ROTATE) return (T)(object)true;

                                        DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
                                        switch (door_mechanism)
                                        {
                                            case DOOR_MECHANISM.HIDDEN_LEVER:
                                            case DOOR_MECHANISM.LEVER:
                                                return (T)(object)false;
                                            default:
                                                return (T)(object)true;
                                        }
                                    }
                                    break;
                                case FunctionType.DepthOfFieldSettings:
                                    break;
                                case FunctionType.DespawnCharacter:
                                    break;
                                case FunctionType.DespawnPlayer:
                                    break;
                                case FunctionType.Display_Element_On_Map:
                                    break;
                                case FunctionType.DisplayMessage:
                                    break;
                                case FunctionType.DisplayMessageWithCallbacks:
                                    break;
                                case FunctionType.DistortionOverlay:
                                    break;
                                case FunctionType.DistortionSettings:
                                    break;
                                case FunctionType.Door:
                                    break;
                                case FunctionType.DoorStatus:
                                    if (typeof(T) == typeof(int))
                                    {
                                        bool is_locked = Bools.Get("is_locked");
                                        if (!is_locked)
                                        {
                                            if (Bools.Get("is_powered")) 
                                                return (T)(object)(int)DOOR_STATE.USE_MECHANISM;
                                            return (T)(object)(int)DOOR_STATE.RESTORE_POWER;
                                        }

                                        DOOR_MECHANISM door_mechanism = (DOOR_MECHANISM)EnumIndexes.Get("door_mechanism");
                                        if (Bools.Get("cut_complete"))
                                        {
                                            switch (door_mechanism)
                                            {
                                                case DOOR_MECHANISM.HIDDEN_HACKING:
                                                    door_mechanism = DOOR_MECHANISM.HACKING;
                                                    break;
                                                case DOOR_MECHANISM.HIDDEN_KEYPAD:
                                                    door_mechanism = DOOR_MECHANISM.KEYPAD;
                                                    break;
                                                case DOOR_MECHANISM.HIDDEN_LEVER:
                                                    door_mechanism = DOOR_MECHANISM.LEVER;
                                                    break;
                                                case DOOR_MECHANISM.HIDDEN_BUTTON:
                                                    door_mechanism = DOOR_MECHANISM.BUTTON;
                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            return (T)(object)(int)DOOR_STATE.CUTTING_REQUIRED;
                                        }

                                        if (!Bools.Get("is_powered"))
                                            return (T)(object)(int)DOOR_STATE.RESTORE_POWER;

                                        switch (door_mechanism)
                                        {
                                            case DOOR_MECHANISM.HACKING:
                                                return (T)(object)(int)DOOR_STATE.HACKING_REQUIRED;
                                            case DOOR_MECHANISM.LEVER:
                                                return (T)(object)(int)DOOR_STATE.USE_LEVER;
                                            case DOOR_MECHANISM.BUTTON:
                                                return (T)(object)(int)DOOR_STATE.USE_BUTTON;
                                            case DOOR_MECHANISM.KEYPAD:
                                                UI_KEYGATE_TYPE gate_type = (UI_KEYGATE_TYPE)EnumIndexes.Get("gate_type");
                                                if (gate_type == UI_KEYGATE_TYPE.KEYPAD)
                                                {
                                                    return (T)(object)(int)DOOR_STATE.USE_KEYCODE;
                                                }
                                                else
                                                {
                                                    if (Bools.Get("has_correct_keycard"))
                                                        return (T)(object)(int)DOOR_STATE.USE_KEYCARD;
                                                    else
                                                        return (T)(object)(int)DOOR_STATE.KEYCARD_REQUIRED;
                                                }
                                            default:
                                                return (T)(object)(int)DOOR_STATE.LOCKED;
                                        }
                                    }
                                    break;
                                case FunctionType.DurangoVideoCapture:
                                    break;
                                case FunctionType.EFFECT_DirectionalPhysics:
                                    break;
                                case FunctionType.EFFECT_EntityGenerator:
                                    break;
                                case FunctionType.EFFECT_ImpactGenerator:
                                    break;
                                case FunctionType.EggSpawner:
                                    break;
                                case FunctionType.ElapsedTimer:
                                    break;
                                case FunctionType.EnableMotionTrackerPassiveAudio:
                                    break;
                                case FunctionType.EndGame:
                                    break;
                                case FunctionType.ENT_Debug_Exit_Game:
                                    break;
                                case FunctionType.EnvironmentMap:
                                    break;
                                case FunctionType.EnvironmentModelReference:
                                    break;
                                case FunctionType.EQUIPPABLE_ITEM:
                                    break;
                                case FunctionType.EvaluatorInterface:
                                    break;
                                case FunctionType.ExclusiveMaster:
                                    break;
                                case FunctionType.Explosion_AINotifier:
                                    break;
                                case FunctionType.ExternalVariableBool:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false; 
                                    break;
                                case FunctionType.FakeAILightSourceInPlayersHand:
                                    break;
                                case FunctionType.FilmGrainSettings:
                                    break;

                                case FunctionType.Filter:
                                    break;
                                case FunctionType.FilterAbsorber:
                                    break;
                                case FunctionType.FilterAnd:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        List<InstancedEntity> filters = Bools.GetLinks("filter");
                                        for (int i = 0; i < filters.Count; i++)
                                        {
                                            if (!filters[i].GetAs<bool>())
                                                return (T)(object)false;
                                        }
                                        return (T)(object)true;
                                    }
                                    break;
                                case FunctionType.FilterBelongsToAlliance:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterCanSeeTarget:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterHasBehaviourTreeFlagSet:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterHasPlayerCollectedIdTag:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterHasWeaponEquipped:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterHasWeaponOfType:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsACharacter:
                                    break;
                                case FunctionType.FilterIsAgressing:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsAnySaveInProgress:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsAPlayer:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsCharacter:
                                    break;
                                case FunctionType.FilterIsCharacterClass:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsCharacterClassCombo:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsDead:
                                    break;
                                case FunctionType.FilterIsEnemyOfAllianceGroup:
                                    break;
                                case FunctionType.FilterIsEnemyOfCharacter:
                                    break;
                                case FunctionType.FilterIsEnemyOfPlayer:
                                    break;
                                case FunctionType.FilterIsFacingTarget:
                                    break;
                                case FunctionType.FilterIsHumanNPC:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsInAGroup:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsInAlertnessState:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsinInventory:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsInLocomotionState:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsInWeaponRange:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsLocalPlayer:
                                    break;
                                case FunctionType.FilterIsNotDeadManWalking:
                                    break;
                                case FunctionType.FilterIsObject:
                                    break;
                                case FunctionType.FilterIsPhysics:
                                    break;
                                case FunctionType.FilterIsPhysicsObject:
                                    break;
                                case FunctionType.FilterIsPlatform:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        PLATFORM_TYPE platform = (PLATFORM_TYPE)EnumIndexes.Get("Platform");
                                        return (T)(object)(platform == PLATFORM_TYPE.PL_NEXTGEN || platform == PLATFORM_TYPE.PL_PC);
                                    }
                                    break;
                                case FunctionType.FilterIsUsingDevice:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.FilterIsValidInventoryItem:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false; //hmmm, unsure
                                    break;
                                case FunctionType.FilterIsWithdrawnAlien:
                                    break;
                                case FunctionType.FilterNot:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        List<InstancedEntity> filters = Bools.GetLinks("filter");
                                        return (T)(object)(filters.Count == 0 ? false : filters[0].GetAs<bool>());
                                    }
                                    break;
                                case FunctionType.FilterOr:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        List<InstancedEntity> filters = Bools.GetLinks("filter");
                                        for (int i = 0; i < filters.Count; i++)
                                        {
                                            if (filters[i].GetAs<bool>())
                                                return (T)(object)true;
                                        }
                                        return (T)(object)false;
                                    }
                                    break;
                                case FunctionType.FilterSmallestUsedDifficulty:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false; 
                                    break;
                                case FunctionType.FixedCamera:
                                    break;
                                case FunctionType.FlareSettings:
                                    break;
                                case FunctionType.FlareTask:
                                    break;
                                case FunctionType.FlashCallback:
                                    break;
                                case FunctionType.FlashInvoke:
                                    break;
                                case FunctionType.FlashScript:
                                    break;
                                case FunctionType.FloatAbsolute:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)Math.Abs(Floats.Get("Input"));
                                    break;
                                case FunctionType.FloatAdd:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)(Floats.Get("LHS") + Floats.Get("RHS"));
                                    break;
                                case FunctionType.FloatAdd_All:
                                    if (typeof(T) == typeof(float))
                                    {
                                        List<InstancedEntity> numbers = Floats.GetLinks("Numbers");
                                        float sum = 0;
                                        for (int i = 0; i < numbers.Count; i++)
                                            sum += numbers[i].GetAs<float>();
                                        return (T)(object)sum;
                                    }
                                    break;
                                case FunctionType.FloatClamp:
                                    if (typeof(T) == typeof(float))
                                    {
                                        float val = Floats.Get("Value");
                                        float min = Floats.Get("Min");
                                        float max = Floats.Get("Max");
                                        if (min < 0.0f) val = min;
                                        if (max > 1.0f) val = max;
                                        return (T)(object)val;
                                    }
                                    break;
                                case FunctionType.FloatClampMultiply:
                                    if (typeof(T) == typeof(float))
                                    {
                                        float mult = Floats.Get("LHS");
                                        if (mult < Floats.Get("Min"))
                                            return (T)(object)Floats.Get("Min");
                                        if (mult > (Floats.Get("Max") * Floats.Get("RHS")))
                                            return (T)(object)Floats.Get("Max");
                                    }
                                    break;
                                case FunctionType.FloatCompare:
                                    break;
                                case FunctionType.FloatDivide:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)(Floats.Get("LHS") / Floats.Get("RHS"));
                                    break;
                                case FunctionType.FloatEquals:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        float threshold = Floats.Get("Threshold");
                                        return (T)(object)(Floats.Get("LHS") == Floats.Get("RHS")); //todo - need to account for threshold  (if equal within threshold ,return true)
                                    }
                                    break;
                                case FunctionType.FloatGetLinearProportion:
                                    if (typeof(T) == typeof(float))
                                    {
                                        float min = Floats.Get("Min");
                                        float max = Floats.Get("Max");
                                        float mid = Floats.Get("Input");
                                        return (T)(object)((mid - min) / (max - min));
                                    }
                                    break;
                                case FunctionType.FloatGreaterThan:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        float threshold = Floats.Get("Threshold");
                                        return (T)(object)(Floats.Get("LHS") > Floats.Get("RHS")); //todo - need to account for threshold  (if equal within threshold ,return false)
                                    }
                                    break;
                                case FunctionType.FloatGreaterThanOrEqual:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        float threshold = Floats.Get("Threshold");
                                        return (T)(object)(Floats.Get("LHS") >= Floats.Get("RHS")); //todo - need to account for threshold  (if equal within threshold ,return true)
                                    }
                                    break;
                                case FunctionType.FloatLessThan:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        float threshold = Floats.Get("Threshold");
                                        return (T)(object)(Floats.Get("LHS") < Floats.Get("RHS")); //todo - need to account for threshold (if equal within threshold ,return false)
                                    }
                                    break;
                                case FunctionType.FloatLessThanOrEqual:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        float threshold = Floats.Get("Threshold");
                                        return (T)(object)(Floats.Get("LHS") <= Floats.Get("RHS")); //todo - need to account for threshold (if equal within threshold ,return true)
                                    }
                                    break;
                                case FunctionType.FloatLinearInterpolateSpeed:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)Floats.Get("Initial_Value");
                                    break;
                                case FunctionType.FloatLinearInterpolateSpeedAdvanced:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)Floats.Get("Initial_Value");
                                    break;
                                case FunctionType.FloatLinearInterpolateTimed:
                                    break;
                                case FunctionType.FloatLinearProportion:
                                    if (typeof(T) == typeof(float))
                                    {
                                        float min = Floats.Get("Initial_Value");
                                        float max = Floats.Get("Target_Value");
                                        return (T)(object)(min + (max - min) * Floats.Get("Proportion"));
                                    }
                                    break;
                                case FunctionType.FloatMath:
                                    break;
                                case FunctionType.FloatMath_All:
                                    break;
                                case FunctionType.FloatMax:
                                    if (typeof(T) == typeof(float))
                                    {
                                        float lhs = Floats.Get("LHS");
                                        float rhs = Floats.Get("RHS");
                                        if (lhs > rhs) return (T)(object)lhs;
                                        return (T)(object)rhs;
                                    }
                                    break;
                                case FunctionType.FloatMax_All:
                                    if (typeof(T) == typeof(float))
                                    {
                                        List<InstancedEntity> numbers = Floats.GetLinks("Numbers");
                                        float max = 0;
                                        for (int i = 0; i < numbers.Count; i++)
                                        {
                                            float number = numbers[i].GetAs<float>();
                                            if (max < number) max = number;
                                        }
                                        return (T)(object)max;
                                    }
                                    break;
                                case FunctionType.FloatMin:
                                    if (typeof(T) == typeof(float))
                                    {
                                        float lhs = Floats.Get("LHS");
                                        float rhs = Floats.Get("RHS");
                                        if (lhs < rhs) return (T)(object)lhs;
                                        return (T)(object)rhs;
                                    }
                                    break;
                                case FunctionType.FloatMin_All:
                                    if (typeof(T) == typeof(float))
                                    {
                                        List<InstancedEntity> numbers = Floats.GetLinks("Numbers");
                                        float min = 0;
                                        if (numbers.Count > 0)
                                        {
                                            min = numbers[0].GetAs<float>();
                                            for (int i = 1; i < numbers.Count; i++)
                                            {
                                                float number = numbers[i].GetAs<float>();
                                                if (number < min) min = number;
                                            }
                                        }
                                        return (T)(object)min;
                                    }
                                    break;
                                case FunctionType.FloatModulate:
                                    break;
                                case FunctionType.FloatModulateRandom:
                                    break;
                                case FunctionType.FloatMultiply:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)(Floats.Get("LHS") * Floats.Get("RHS"));
                                    break;
                                case FunctionType.FloatMultiply_All:
                                    if (typeof(T) == typeof(float))
                                    {
                                        List<InstancedEntity> numbers = Floats.GetLinks("Numbers");
                                        float sum = 0;
                                        if (numbers.Count > 0)
                                        {
                                            sum = numbers[0].GetAs<float>();
                                            for (int i = 1; i < numbers.Count; i++)
                                                sum *= numbers[i].GetAs<float>();
                                        }
                                        return (T)(object)sum;
                                    }
                                    break;
                                case FunctionType.FloatMultiplyClamp:
                                    if (typeof(T) == typeof(float))
                                    {
                                        float mult = Floats.Get("LHS") * Floats.Get("RHS");
                                        if (mult < Floats.Get("Min"))
                                            return (T)(object)Floats.Get("Min");
                                        if (mult > Floats.Get("Max"))
                                            return (T)(object)Floats.Get("Max");
                                    }
                                    break;
                                case FunctionType.FloatNotEqual:
                                    if (typeof(T) == typeof(bool))
                                    {
                                        float threshold = Floats.Get("Threshold");
                                        return (T)(object)(Floats.Get("LHS") != Floats.Get("RHS")); //todo - need to account for threshold  (if equal within threshold ,return true)
                                    }
                                    break;
                                case FunctionType.FloatOperation:
                                    break;
                                case FunctionType.FloatReciprocal:
                                    if (typeof(T) == typeof(float))
                                    {
                                        float input = Floats.Get("Input");
                                        if (Math.Abs(input) < 0.00001f) return (T)(object)0.0f;
                                        return (T)(object)(1.0f / input);
                                    }
                                    break;
                                case FunctionType.FloatRemainder:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)(Floats.Get("LHS") % Floats.Get("RHS"));
                                    break;
                                case FunctionType.FloatSmoothStep:
                                    if (typeof(T) == typeof(float))
                                    {
                                        float edge0 = Floats.Get("Low_Edge");
                                        float edge1 = Floats.Get("High_Edge");
                                        float t = Floats.Get("Value");

                                        float result = (t - edge0) / (edge1 - edge0);
                                        if (result > 1.0f) result = 1.0f;
                                        if (result < 0.0f) result = 0.0f;
                                        return (T)(object)(result * result * (3.0f - 2.0f * result));
                                    }
                                    break;
                                case FunctionType.FloatSqrt:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)(float)Math.Sqrt(Math.Abs(Floats.Get("Input")));
                                    break;
                                case FunctionType.FloatSubtract:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)(Floats.Get("LHS") - Floats.Get("RHS"));
                                    break;
                                case FunctionType.FlushZoneCache:
                                    break;
                                case FunctionType.FogBox:
                                    break;
                                case FunctionType.FogPlane:
                                    break;
                                case FunctionType.FogSetting:
                                    break;
                                case FunctionType.FogSphere:
                                    break;
                                case FunctionType.FollowCameraModifier:
                                    break;
                                case FunctionType.FollowTask:
                                    break;
                                case FunctionType.Force_UI_Visibility:
                                    break;
                                case FunctionType.FullScreenBlurSettings:
                                    break;
                                case FunctionType.FullScreenOverlay:
                                    break;
                                case FunctionType.GameDVR:
                                    break;
                                case FunctionType.GameOver:
                                    break;
                                case FunctionType.GameOverCredits:
                                    break;
                                case FunctionType.GameplayTip:
                                    break;
                                case FunctionType.GameStateChanged:
                                    break;
                                case FunctionType.GateInterface:
                                    break;
                                case FunctionType.GateResourceInterface:
                                    break;
                                case FunctionType.GCIP_WorldPickup:
                                    break;
                                case FunctionType.GenericHighlightEntity:
                                    break;
                                case FunctionType.GetBlueprintAvailable:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)true;
                                    break;
                                case FunctionType.GetBlueprintLevel:
                                    break;
                                case FunctionType.GetCentrePoint:
                                    break;
                                case FunctionType.GetCharacterRotationSpeed:
                                    break;
                                case FunctionType.GetClosestPercentOnSpline:
                                    break;
                                case FunctionType.GetClosestPoint:
                                    break;
                                case FunctionType.GetClosestPointFromSet:
                                    break;
                                case FunctionType.GetClosestPointOnSpline:
                                    break;
                                case FunctionType.GetComponentInterface:
                                    break;
                                case FunctionType.GetCurrentCameraFov:
                                    break;
                                case FunctionType.GetCurrentCameraPos:
                                    break;
                                case FunctionType.GetCurrentCameraTarget:
                                    break;
                                case FunctionType.GetCurrentPlaylistLevelIndex:
                                    break;
                                case FunctionType.GetFlashFloatValue:
                                    break;
                                case FunctionType.GetFlashIntValue:
                                    break;
                                case FunctionType.GetGatingToolLevel:
                                    break;
                                case FunctionType.GetInventoryItemName:
                                    break;
                                case FunctionType.GetNextPlaylistLevelName:
                                    break;
                                case FunctionType.GetPlayerHasGatingTool:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.GetPlayerHasKeycard:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.GetPointOnSpline:
                                    break;
                                case FunctionType.GetRotation:
                                    break;
                                case FunctionType.GetSelectedCharacterId:
                                    break;
                                case FunctionType.GetSplineLength:
                                    break;
                                case FunctionType.GetTranslation:
                                    break;
                                case FunctionType.GetX:
                                    break;
                                case FunctionType.GetY:
                                    break;
                                case FunctionType.GetZ:
                                    break;
                                case FunctionType.GlobalEvent:
                                    break;
                                case FunctionType.GlobalEventMonitor:
                                    break;
                                case FunctionType.GlobalPosition:
                                    break;
                                case FunctionType.GoToFrontend:
                                    break;
                                case FunctionType.GPU_PFXEmitterReference:
                                    break;
                                case FunctionType.HableToneMappingSettings:
                                    break;
                                case FunctionType.HackingGame:
                                    break;
                                case FunctionType.HandCamera:
                                    break;
                                case FunctionType.HasAccessAtDifficulty:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.HeldItem_AINotifier:
                                    break;
                                case FunctionType.HighSpecMotionBlurSettings:
                                    break;
                                case FunctionType.HostOnlyTrigger:
                                    break;
                                case FunctionType.IdleTask:
                                    break;
                                case FunctionType.ImpactSphere:
                                    break;
                                case FunctionType.InhibitActionsUntilRelease:
                                    break;
                                case FunctionType.InspectorInterface:
                                    break;
                                case FunctionType.IntegerAbsolute:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)Math.Abs(Integers.Get("Input"));
                                    break;
                                case FunctionType.IntegerAdd:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)(Integers.Get("LHS") + Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerAdd_All:
                                    if (typeof(T) == typeof(int))
                                    {
                                        List<InstancedEntity> numbers = Integers.GetLinks("Numbers");
                                        int sum = 0;
                                        for (int i = 0; i < numbers.Count; i++)
                                            sum += numbers[i].GetAs<int>();
                                        return (T)(object)sum;
                                    }
                                    break;
                                case FunctionType.IntegerAnalyse:
                                    break;
                                case FunctionType.IntegerAnd:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)(Integers.Get("LHS") & Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerCompare:
                                    break;
                                case FunctionType.IntegerCompliment:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)~Integers.Get("Input");
                                    break;
                                case FunctionType.IntegerDivide:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)(Integers.Get("LHS") / Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerEquals:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)(Integers.Get("LHS") == Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerGreaterThan:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)(Integers.Get("LHS") > Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerGreaterThanOrEqual:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)(Integers.Get("LHS") >= Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerLessThan:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)(Integers.Get("LHS") < Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerLessThanOrEqual:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)(Integers.Get("LHS") <= Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerMath:
                                    break;
                                case FunctionType.IntegerMath_All:
                                    break;
                                case FunctionType.IntegerMax:
                                    if (typeof(T) == typeof(int))
                                    {
                                        int lhs = Integers.Get("LHS");
                                        int rhs = Integers.Get("RHS");
                                        if (lhs > rhs) return (T)(object)lhs;
                                        return (T)(object)rhs;
                                    }
                                    break;
                                case FunctionType.IntegerMax_All:
                                    if (typeof(T) == typeof(int))
                                    {
                                        List<InstancedEntity> numbers = Integers.GetLinks("Numbers");
                                        int max = 0;
                                        for (int i = 0; i < numbers.Count; i++)
                                        {
                                            int number = numbers[i].GetAs<int>();
                                            if (max < number) max = number;
                                        }
                                        return (T)(object)max;
                                    }
                                    break;
                                case FunctionType.IntegerMin:
                                    if (typeof(T) == typeof(int))
                                    {
                                        int lhs = Integers.Get("LHS");
                                        int rhs = Integers.Get("RHS");
                                        if (lhs < rhs) return (T)(object)lhs;
                                        return (T)(object)rhs;
                                    }
                                    break;
                                case FunctionType.IntegerMin_All:
                                    if (typeof(T) == typeof(int))
                                    {
                                        List<InstancedEntity> numbers = Integers.GetLinks("Numbers");
                                        int min = 0;
                                        if (numbers.Count > 0)
                                        {
                                            min = numbers[0].GetAs<int>();
                                            for (int i = 1; i < numbers.Count; i++)
                                            {
                                                int number = numbers[i].GetAs<int>();
                                                if (number < min) min = number;
                                            }
                                        }
                                        return (T)(object)min;
                                    }
                                    break;
                                case FunctionType.IntegerMultiply:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)(Integers.Get("LHS") * Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerMultiply_All:
                                    if (typeof(T) == typeof(int))
                                    {
                                        List<InstancedEntity> numbers = Integers.GetLinks("Numbers");
                                        int sum = 0;
                                        if (numbers.Count > 0)
                                        {
                                            sum = numbers[0].GetAs<int>();
                                            for (int i = 1; i < numbers.Count; i++)
                                                sum *= numbers[i].GetAs<int>();
                                        }
                                        return (T)(object)sum;
                                    }
                                    break;
                                case FunctionType.IntegerNotEqual:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)(Integers.Get("LHS") != Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerOperation:
                                    break;
                                case FunctionType.IntegerOr:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)(Integers.Get("LHS") | Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerRemainder:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)(Integers.Get("LHS") % Integers.Get("RHS"));
                                    break;
                                case FunctionType.IntegerSubtract:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)(Integers.Get("LHS") - Integers.Get("RHS"));
                                    break;
                                case FunctionType.Interaction:
                                    break;
                                case FunctionType.InteractiveMovementControl:
                                    break;
                                case FunctionType.Internal_JOB_SearchTarget:
                                    break;
                                case FunctionType.InventoryItem:
                                    break;
                                case FunctionType.IrawanToneMappingSettings:
                                    break;
                                case FunctionType.IsActive:
                                    break; 
                                case FunctionType.IsAttached:
                                    break; 
                                case FunctionType.IsCurrentLevelAChallengeMap:
                                    break; //todo - check level name
                                case FunctionType.IsCurrentLevelAPreorderMap:
                                    break; //todo - check level name
                                case FunctionType.IsEnabled:
                                    break; 
                                case FunctionType.IsInstallComplete:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)true;
                                    break;
                                case FunctionType.IsLoaded:
                                    break; 
                                case FunctionType.IsLoading:
                                    break; 
                                case FunctionType.IsLocked:
                                    break; 
                                case FunctionType.IsMultiplayerMode:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.IsOpen:
                                    break; 
                                case FunctionType.IsOpening:
                                    break; 
                                case FunctionType.IsPaused:
                                    break; 
                                case FunctionType.IsPlaylistTypeAll:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.IsPlaylistTypeMarathon:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.IsPlaylistTypeSingle:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.IsSpawned:
                                    break; 
                                case FunctionType.IsStarted:
                                    break;
                                case FunctionType.IsSuspended:
                                    break; 
                                case FunctionType.IsVisible:
                                    break; 
                                case FunctionType.Job:
                                    break;
                                case FunctionType.JOB_AreaSweep:
                                    break;
                                case FunctionType.JOB_AreaSweepFlare:
                                    break;
                                case FunctionType.JOB_Assault:
                                    break;
                                case FunctionType.JOB_Follow:
                                    break;
                                case FunctionType.JOB_Follow_Centre:
                                    break;
                                case FunctionType.JOB_Idle:
                                    break;
                                case FunctionType.JOB_Panic:
                                    break;
                                case FunctionType.JOB_SpottingPosition:
                                    break;
                                case FunctionType.JOB_SystematicSearch:
                                    break;
                                case FunctionType.JOB_SystematicSearchFlare:
                                    break;
                                case FunctionType.JobWithPosition:
                                    break;
                                case FunctionType.LeaderboardWriter:
                                    break;
                                case FunctionType.LeaveGame:
                                    break;
                                case FunctionType.LensDustSettings:
                                    break;
                                case FunctionType.LevelCompletionTargets:
                                    break;
                                case FunctionType.LevelInfo:
                                    break;
                                case FunctionType.LevelLoaded:
                                    break;
                                case FunctionType.LightAdaptationSettings:
                                    break;
                                case FunctionType.LightingMaster:
                                    break;
                                case FunctionType.LightReference:
                                    break;
                                case FunctionType.LimitItemUse:
                                    break;
                                case FunctionType.LODControls:
                                    break;
                                case FunctionType.Logic_MultiGate:
                                    break;
                                case FunctionType.Logic_Vent_Entrance:
                                    break;
                                case FunctionType.Logic_Vent_System:
                                    break;
                                case FunctionType.LogicAll:
                                    break;
                                case FunctionType.LogicCounter:
                                    break;
                                case FunctionType.LogicDelay:
                                    break;
                                case FunctionType.LogicGate:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)Bools.Get("allow");
                                    break;
                                case FunctionType.LogicGateAnd:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)(Bools.Get("LHS") && Bools.Get("RHS"));
                                    break;
                                case FunctionType.LogicGateEquals:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)(Bools.Get("LHS") == Bools.Get("RHS"));
                                    break;
                                case FunctionType.LogicGateNotEqual:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)(Bools.Get("LHS") != Bools.Get("RHS"));
                                    break;
                                case FunctionType.LogicGateOr:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)(Bools.Get("LHS") || Bools.Get("RHS"));
                                    break;
                                case FunctionType.LogicNot:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)!Bools.Get("Input");
                                    break;
                                case FunctionType.LogicOnce:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)true;
                                    break;
                                case FunctionType.LogicPressurePad:
                                    break;
                                case FunctionType.LogicSwitch:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)Bools.Get("initial_value");
                                    break;
                                case FunctionType.LowResFrameCapture:
                                    break;
                                case FunctionType.Map_Floor_Change:
                                    break;
                                case FunctionType.MapAnchor:
                                    break;
                                case FunctionType.MapItem:
                                    break;
                                case FunctionType.Master:
                                    break;
                                case FunctionType.MELEE_WEAPON:
                                    break;
                                case FunctionType.Minigames:
                                    break;
                                case FunctionType.MissionNumber:
                                    break;
                                case FunctionType.ModelReference:
                                    break;
                                case FunctionType.ModifierInterface:
                                    break;
                                case FunctionType.MonitorActionMap:
                                    break;
                                case FunctionType.MonitorBase:
                                    break;
                                case FunctionType.MonitorPadInput:
                                    break;
                                case FunctionType.MotionTrackerMonitor:
                                    break;
                                case FunctionType.MotionTrackerPing:
                                    break;
                                case FunctionType.MoveAlongSpline:
                                    break;
                                case FunctionType.MoveInTime:
                                    break;
                                case FunctionType.MoviePlayer:
                                    break;
                                case FunctionType.MultipleCharacterAttachmentNode:
                                    break;
                                case FunctionType.MultiplePickupSpawner:
                                    break;
                                case FunctionType.MultitrackLoop:
                                    break;
                                case FunctionType.MusicController:
                                    break;
                                case FunctionType.MusicTrigger:
                                    break;
                                case FunctionType.NavMeshArea:
                                    break;
                                case FunctionType.NavMeshBarrier:
                                    break;
                                case FunctionType.NavMeshExclusionArea:
                                    break;
                                case FunctionType.NavMeshReachabilitySeedPoint:
                                    break;
                                case FunctionType.NavMeshWalkablePlatform:
                                    break;
                                case FunctionType.NetPlayerCounter:
                                    break;
                                case FunctionType.NetworkedTimer:
                                    break;
                                case FunctionType.NetworkProxy:
                                    break;
                                case FunctionType.NonInteractiveWater:
                                    break;
                                case FunctionType.NonPersistentBool:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)Bools.Get("initial_value");
                                    break;
                                case FunctionType.NonPersistentInt:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)Integers.Get("initial_value");
                                    break;
                                case FunctionType.NPC_Aggression_Monitor:
                                    break;
                                case FunctionType.NPC_AlienConfig:
                                    break;
                                case FunctionType.NPC_AllSensesLimiter:
                                    break;
                                case FunctionType.NPC_ambush_monitor:
                                    break;
                                case FunctionType.NPC_AreaBox:
                                    break;
                                case FunctionType.NPC_behaviour_monitor:
                                    break;
                                case FunctionType.NPC_ClearDefendArea:
                                    break;
                                case FunctionType.NPC_ClearPursuitArea:
                                    break;
                                case FunctionType.NPC_Coordinator:
                                    break;
                                case FunctionType.NPC_Debug_Menu_Item:
                                    break;
                                case FunctionType.NPC_DefineBackstageAvoidanceArea:
                                    break;
                                case FunctionType.NPC_DynamicDialogue:
                                    break;
                                case FunctionType.NPC_DynamicDialogueGlobalRange:
                                    break;
                                case FunctionType.NPC_FakeSense:
                                    break;
                                case FunctionType.NPC_FollowOffset:
                                    break;
                                case FunctionType.NPC_ForceCombatTarget:
                                    break;
                                case FunctionType.NPC_ForceNextJob:
                                    break;
                                case FunctionType.NPC_ForceRetreat:
                                    break;
                                case FunctionType.NPC_Gain_Aggression_In_Radius:
                                    break;
                                case FunctionType.NPC_GetCombatTarget:
                                    break;
                                case FunctionType.NPC_GetLastSensedPositionOfTarget:
                                    break;
                                case FunctionType.NPC_Group_Death_Monitor:
                                    break;
                                case FunctionType.NPC_Group_DeathCounter:
                                    break;
                                case FunctionType.NPC_Highest_Awareness_Monitor:
                                    break;
                                case FunctionType.NPC_MeleeContext:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)EnumIndexes.Get("Context_Type");
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)Floats.Get("Radius");
                                    break;
                                case FunctionType.NPC_multi_behaviour_monitor:
                                    break;
                                case FunctionType.NPC_navmesh_type_monitor:
                                    break;
                                case FunctionType.NPC_NotifyDynamicDialogueEvent:
                                    break;
                                case FunctionType.NPC_Once:
                                    break;
                                case FunctionType.NPC_ResetFiringStats:
                                    break;
                                case FunctionType.NPC_ResetSensesAndMemory:
                                    break;
                                case FunctionType.NPC_SenseLimiter:
                                    break;
                                case FunctionType.NPC_set_behaviour_tree_flags:
                                    break;
                                case FunctionType.NPC_SetAgressionProgression:
                                    break;
                                case FunctionType.NPC_SetAimTarget:
                                    break;
                                case FunctionType.NPC_SetAlertness:
                                    break;
                                case FunctionType.NPC_SetAlienDevelopmentStage:
                                    break;
                                case FunctionType.NPC_SetAutoTorchMode:
                                    break;
                                case FunctionType.NPC_SetChokePoint:
                                    break;
                                case FunctionType.NPC_SetDefendArea:
                                    break;
                                case FunctionType.NPC_SetFiringAccuracy:
                                    break;
                                case FunctionType.NPC_SetFiringRhythm:
                                    break;
                                case FunctionType.NPC_SetGunAimMode:
                                    break;
                                case FunctionType.NPC_SetHidingNearestLocation:
                                    break;
                                case FunctionType.NPC_SetHidingSearchRadius:
                                    break;
                                case FunctionType.NPC_SetInvisible:
                                    break;
                                case FunctionType.NPC_SetLocomotionStyleForJobs:
                                    break;
                                case FunctionType.NPC_SetLocomotionTargetSpeed:
                                    break;
                                case FunctionType.NPC_SetPursuitArea:
                                    break;
                                case FunctionType.NPC_SetRateOfFire:
                                    break;
                                case FunctionType.NPC_SetSafePoint:
                                    break;
                                case FunctionType.NPC_SetSenseSet:
                                    break;
                                case FunctionType.NPC_SetStartPos:
                                    break;
                                case FunctionType.NPC_SetTotallyBlindInDark:
                                    break;
                                case FunctionType.NPC_SetupMenaceManager:
                                    break;
                                case FunctionType.NPC_Sleeping_Android_Monitor:
                                    break;
                                case FunctionType.NPC_Squad_DialogueMonitor:
                                    break;
                                case FunctionType.NPC_Squad_GetAwarenessState:
                                    break;
                                case FunctionType.NPC_Squad_GetAwarenessWatermark:
                                    break;
                                case FunctionType.NPC_StopAiming:
                                    break;
                                case FunctionType.NPC_StopShooting:
                                    break;
                                case FunctionType.NPC_SuspiciousItem:
                                    break;
                                case FunctionType.NPC_TargetAcquire:
                                    break;
                                case FunctionType.NPC_TriggerAimRequest:
                                    break;
                                case FunctionType.NPC_TriggerShootRequest:
                                    break;
                                case FunctionType.NPC_WithdrawAlien:
                                    break;
                                case FunctionType.NumConnectedPlayers:
                                    break;
                                case FunctionType.NumDeadPlayers:
                                    break;
                                case FunctionType.NumPlayersOnStart:
                                    break;
                                case FunctionType.PadLightBar:
                                    break;
                                case FunctionType.PadRumbleImpulse:
                                    break;
                                case FunctionType.ParticipatingPlayersList:
                                    break;
                                case FunctionType.ParticleEmitterReference:
                                    break;
                                case FunctionType.PathfindingAlienBackstageNode:
                                    break;
                                case FunctionType.PathfindingManualNode:
                                    break;
                                case FunctionType.PathfindingTeleportNode:
                                    break;
                                case FunctionType.PathfindingWaitNode:
                                    break;
                                case FunctionType.Persistent_TriggerRandomSequence:
                                    break;
                                case FunctionType.PhysicsApplyBuoyancy:
                                    break;
                                case FunctionType.PhysicsApplyImpulse:
                                    break;
                                case FunctionType.PhysicsApplyVelocity:
                                    break;
                                case FunctionType.PhysicsModifyGravity:
                                    break;
                                case FunctionType.PhysicsSystem:
                                    break;
                                case FunctionType.PickupSpawner:
                                    break;
                                case FunctionType.Planet:
                                    break;
                                case FunctionType.PlatformConstantBool:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)Bools.Get("NextGen");
                                    break;
                                case FunctionType.PlatformConstantFloat:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)Floats.Get("NextGen");
                                    break;
                                case FunctionType.PlatformConstantInt:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)Integers.Get("NextGen");
                                    break;
                                case FunctionType.PlayEnvironmentAnimation:
                                    break;
                                case FunctionType.Player_ExploitableArea:
                                    break;
                                case FunctionType.Player_Sensor:
                                    break;
                                case FunctionType.PlayerCamera:
                                    break;
                                case FunctionType.PlayerCameraMonitor:
                                    break;
                                case FunctionType.PlayerCampaignDeaths:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)0;
                                    break;
                                case FunctionType.PlayerCampaignDeathsInARow:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)0;
                                    break;
                                case FunctionType.PlayerDeathCounter:
                                    break;
                                case FunctionType.PlayerDiscardsItems:
                                    break;
                                case FunctionType.PlayerDiscardsTools:
                                    break;
                                case FunctionType.PlayerDiscardsWeapons:
                                    break;
                                case FunctionType.PlayerHasEnoughItems:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.PlayerHasItem:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.PlayerHasItemEntity:
                                    break;
                                case FunctionType.PlayerHasItemWithName:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.PlayerHasSpaceForItem:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)true;
                                    break;
                                case FunctionType.PlayerKilledAllyMonitor:
                                    break;
                                case FunctionType.PlayerLightProbe:
                                    break;
                                case FunctionType.PlayerTorch:
                                    break;
                                case FunctionType.PlayerTriggerBox:
                                    break;
                                case FunctionType.PlayerUseTriggerBox:
                                    break;
                                case FunctionType.PlayerWeaponMonitor:
                                    break;
                                case FunctionType.PlayForMinDuration:
                                    break;
                                case FunctionType.PointAt:
                                    break;
                                case FunctionType.PointTracker:
                                    break;
                                case FunctionType.PopupMessage:
                                    break;
                                case FunctionType.PositionDistance:
                                    break;
                                case FunctionType.PositionMarker:
                                    break;
                                case FunctionType.PostprocessingSettings:
                                    break;
                                case FunctionType.ProjectileMotion:
                                    break;
                                case FunctionType.ProjectileMotionComplex:
                                    break;
                                case FunctionType.ProjectiveDecal:
                                    break;
                                case FunctionType.ProximityDetector:
                                    break;
                                case FunctionType.ProximityTrigger:
                                    break;
                                case FunctionType.ProxyInterface:
                                    break;
                                case FunctionType.QueryGCItemPool:
                                    break;
                                case FunctionType.RadiosityIsland:
                                    break;
                                case FunctionType.RadiosityProxy:
                                    break;
                                case FunctionType.RandomBool:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.RandomFloat:
                                    break;
                                case FunctionType.RandomInt:
                                    break;
                                case FunctionType.RandomObjectSelector:
                                    break;
                                case FunctionType.RandomSelect:
                                    break;
                                case FunctionType.RandomVector:
                                    break;
                                case FunctionType.Raycast:
                                    break;
                                case FunctionType.Refraction:
                                    break;
                                case FunctionType.RegisterCharacterModel:
                                    break;
                                case FunctionType.RemoveFromGCItemPool:
                                    break;
                                case FunctionType.RemoveFromInventory:
                                    break;
                                case FunctionType.RemoveWeaponsFromPlayer:
                                    break;
                                case FunctionType.RespawnConfig:
                                    break;
                                case FunctionType.RespawnExcluder:
                                    break;
                                case FunctionType.ReTransformer:
                                    break;
                                case FunctionType.Rewire:
                                    break;
                                case FunctionType.RewireAccess_Point:
                                    break;
                                case FunctionType.RewireLocation:
                                    break;
                                case FunctionType.RewireSystem:
                                    break;
                                case FunctionType.RewireTotalPowerResource:
                                    break;
                                case FunctionType.RibbonEmitterReference:
                                    break;
                                case FunctionType.RotateAtSpeed:
                                    break;
                                case FunctionType.RotateInTime:
                                    break;
                                case FunctionType.RTT_MoviePlayer:
                                    break;
                                case FunctionType.SaveGlobalProgression:
                                    break;
                                case FunctionType.SaveManagers:
                                    break;
                                case FunctionType.ScalarProduct:
                                    if (typeof(T) == typeof(float))
                                    {

                                    }
                                    break;
                                case FunctionType.ScreenEffectEventMonitor:
                                    break;
                                case FunctionType.ScreenFadeIn:
                                    break;
                                case FunctionType.ScreenFadeInTimed:
                                    break;
                                case FunctionType.ScreenFadeOutToBlack:
                                    break;
                                case FunctionType.ScreenFadeOutToBlackTimed:
                                    break;
                                case FunctionType.ScreenFadeOutToWhite:
                                    break;
                                case FunctionType.ScreenFadeOutToWhiteTimed:
                                    break;
                                case FunctionType.ScriptInterface:
                                    break;
                                case FunctionType.ScriptVariable:
                                    break;
                                case FunctionType.SensorAttachmentInterface:
                                    break;
                                case FunctionType.SensorInterface:
                                    break;
                                case FunctionType.SetAsActiveMissionLevel:
                                    break;
                                case FunctionType.SetBlueprintInfo:
                                    break;
                                case FunctionType.SetBool:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)Bools.Get("Input");
                                    break;
                                case FunctionType.SetColour:
                                    break;
                                case FunctionType.SetEnum:
                                    break;
                                case FunctionType.SetEnumString:
                                    break;
                                case FunctionType.SetFloat:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)Floats.Get("Input");
                                    break;
                                case FunctionType.SetGamepadAxes:
                                    break;
                                case FunctionType.SetGameplayTips:
                                    break;
                                case FunctionType.SetGatingToolLevel:
                                    break;
                                case FunctionType.SetHackingToolLevel:
                                    break;
                                case FunctionType.SetInteger:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)Integers.Get("Input");
                                    break;
                                case FunctionType.SetLocationAndOrientation:
                                    break;
                                case FunctionType.SetMotionTrackerRange:
                                    break;
                                case FunctionType.SetNextLoadingMovie:
                                    break;
                                case FunctionType.SetObject:
                                    break;
                                case FunctionType.SetObjectiveCompleted:
                                    break;
                                case FunctionType.SetPlayerHasGatingTool:
                                    break;
                                case FunctionType.SetPlayerHasKeycard:
                                    break;
                                case FunctionType.SetPosition:
                                    break;
                                case FunctionType.SetPrimaryObjective:
                                    break;
                                case FunctionType.SetRichPresence:
                                    break;
                                case FunctionType.SetString:
                                    break;
                                case FunctionType.SetSubObjective:
                                    break;
                                case FunctionType.SetupGCDistribution:
                                    break;
                                case FunctionType.SetVector:
                                    break;
                                case FunctionType.SetVector2:
                                    break;
                                case FunctionType.SharpnessSettings:
                                    break;
                                case FunctionType.Showlevel_Completed:
                                    break;
                                case FunctionType.SimpleRefraction:
                                    break;
                                case FunctionType.SimpleWater:
                                    break;
                                case FunctionType.SmokeCylinder:
                                    break;
                                case FunctionType.SmokeCylinderAttachmentInterface:
                                    break;
                                case FunctionType.SmoothMove:
                                    break;
                                case FunctionType.Sound:
                                    break;
                                case FunctionType.SoundBarrier:
                                    break;
                                case FunctionType.SoundEnvironmentMarker:
                                    break;
                                case FunctionType.SoundEnvironmentZone:
                                    break;
                                case FunctionType.SoundImpact:
                                    break;
                                case FunctionType.SoundLevelInitialiser:
                                    break;
                                case FunctionType.SoundLoadBank:
                                    break;
                                case FunctionType.SoundLoadSlot:
                                    break;
                                case FunctionType.SoundMissionInitialiser:
                                    break;
                                case FunctionType.SoundNetworkNode:
                                    break;
                                case FunctionType.SoundObject:
                                    break;
                                case FunctionType.SoundPhysicsInitialiser:
                                    break;
                                case FunctionType.SoundPlaybackBaseClass:
                                    break;
                                case FunctionType.SoundPlayerFootwearOverride:
                                    break;
                                case FunctionType.SoundRTPCController:
                                    break;
                                case FunctionType.SoundSetRTPC:
                                    break;
                                case FunctionType.SoundSetState:
                                    break;
                                case FunctionType.SoundSetSwitch:
                                    break;
                                case FunctionType.SoundSpline:
                                    break;
                                case FunctionType.SoundTimelineTrigger:
                                    break;
                                case FunctionType.SpaceSuitVisor:
                                    break;
                                case FunctionType.SpaceTransform:
                                    break;
                                case FunctionType.SpawnGroup:
                                    break;
                                case FunctionType.Speech:
                                    break;
                                case FunctionType.SpeechScript:
                                    break;
                                case FunctionType.Sphere:
                                    break;
                                case FunctionType.SplineDistanceLerp:
                                    break;
                                case FunctionType.SplinePath:
                                    break;
                                case FunctionType.SpottingExclusionArea:
                                    break;
                                case FunctionType.Squad_SetMaxEscalationLevel:
                                    break;
                                case FunctionType.StartNewChapter:
                                    break;
                                case FunctionType.StateQuery:
                                    break;
                                case FunctionType.StealCamera:
                                    break;
                                case FunctionType.StreamingMonitor:
                                    break;
                                case FunctionType.SurfaceEffectBox:
                                    break;
                                case FunctionType.SurfaceEffectSphere:
                                    break;
                                case FunctionType.SwitchLevel:
                                    break;
                                case FunctionType.SyncOnAllPlayers:
                                    break;
                                case FunctionType.SyncOnFirstPlayer:
                                    break;
                                case FunctionType.Task:
                                    break;
                                case FunctionType.TerminalContent:
                                    break;
                                case FunctionType.TerminalFolder:
                                    break;
                                case FunctionType.Thinker:
                                    break;
                                case FunctionType.ThinkOnce:
                                    break;
                                case FunctionType.ThrowingPointOfImpact:
                                    break;
                                case FunctionType.ToggleFunctionality:
                                    break;
                                case FunctionType.TogglePlayerTorch:
                                    break;
                                case FunctionType.Torch_Control:
                                    break;
                                case FunctionType.TorchDynamicMovement:
                                    break;
                                case FunctionType.TransformerInterface:
                                    break;
                                case FunctionType.TRAV_1ShotClimbUnder:
                                    break;
                                case FunctionType.TRAV_1ShotFloorVentEntrance:
                                    break;
                                case FunctionType.TRAV_1ShotFloorVentExit:
                                    break;
                                case FunctionType.TRAV_1ShotLeap:
                                    break;
                                case FunctionType.TRAV_1ShotSpline:
                                    break;
                                case FunctionType.TRAV_1ShotVentEntrance:
                                    break;
                                case FunctionType.TRAV_1ShotVentExit:
                                    break;
                                case FunctionType.TRAV_ContinuousBalanceBeam:
                                    break;
                                case FunctionType.TRAV_ContinuousCinematicSidle:
                                    break;
                                case FunctionType.TRAV_ContinuousClimbingWall:
                                    break;
                                case FunctionType.TRAV_ContinuousLadder:
                                    break;
                                case FunctionType.TRAV_ContinuousLedge:
                                    break;
                                case FunctionType.TRAV_ContinuousPipe:
                                    break;
                                case FunctionType.TRAV_ContinuousTightGap:
                                    break;
                                case FunctionType.Trigger_AudioOccluded:
                                    break;
                                case FunctionType.TriggerBindAllCharactersOfType:
                                    break;
                                case FunctionType.TriggerBindAllNPCs:
                                    break;
                                case FunctionType.TriggerBindCharacter:
                                    break;
                                case FunctionType.TriggerBindCharactersInSquad:
                                    break;
                                case FunctionType.TriggerCameraViewCone:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.TriggerCameraViewConeMulti:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.TriggerCameraVolume:
                                    break;
                                case FunctionType.TriggerCheckDifficulty:
                                    break;
                                case FunctionType.TriggerContainerObjectsFilterCounter:
                                    break;
                                case FunctionType.TriggerDamaged:
                                    break;
                                case FunctionType.TriggerDelay:
                                    if (typeof(T) == typeof(float))
                                        return (T)(object)Floats.Get("delay");
                                    break;
                                case FunctionType.TriggerExtractBoundCharacter:
                                    break;
                                case FunctionType.TriggerExtractBoundObject:
                                    break;
                                case FunctionType.TriggerFilter:
                                    break;
                                case FunctionType.TriggerLooper:
                                    break;
                                case FunctionType.TriggerObjectsFilter:
                                    break;
                                case FunctionType.TriggerObjectsFilterCounter:
                                    break;
                                case FunctionType.TriggerRandom:
                                    break;
                                case FunctionType.TriggerRandomSequence:
                                    break;
                                case FunctionType.TriggerSelect:
                                    break;
                                case FunctionType.TriggerSelect_Direct:
                                    break;
                                case FunctionType.TriggerSequence:
                                    break;
                                case FunctionType.TriggerSimple:
                                    break;
                                case FunctionType.TriggerSwitch:
                                    break;
                                case FunctionType.TriggerSync:
                                    break;
                                case FunctionType.TriggerTouch:
                                    break;
                                case FunctionType.TriggerUnbindCharacter:
                                    break;
                                case FunctionType.TriggerViewCone:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)false;
                                    break;
                                case FunctionType.TriggerVolumeFilter:
                                    break;
                                case FunctionType.TriggerVolumeFilter_Monitored:
                                    break;
                                case FunctionType.TriggerWeightedRandom:
                                    break;
                                case FunctionType.TriggerWhenSeeTarget:
                                    break;
                                case FunctionType.TutorialMessage:
                                    break;
                                case FunctionType.UI_Attached:
                                    break;
                                case FunctionType.UI_Container:
                                    break;
                                case FunctionType.UI_Icon:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)-1;
                                    break;
                                case FunctionType.UI_KeyGate:
                                    break;
                                case FunctionType.UI_Keypad:
                                    break;
                                case FunctionType.UI_ReactionGame:
                                    break;
                                case FunctionType.UIBreathingGameIcon:
                                    break;
                                case FunctionType.UiSelectionBox:
                                    if (typeof(T) == typeof(int))
                                    {

                                    }
                                    break;
                                case FunctionType.UiSelectionSphere:
                                    break;
                                case FunctionType.UnlockAchievement:
                                    break;
                                case FunctionType.UnlockLogEntry:
                                    break;
                                case FunctionType.UnlockMapDetail:
                                    break;
                                case FunctionType.UpdateGlobalPosition:
                                    break;
                                case FunctionType.UpdateLeaderBoardDisplay:
                                    break;
                                case FunctionType.UpdatePrimaryObjective:
                                    break;
                                case FunctionType.UpdateSubObjective:
                                    break;
                                case FunctionType.VariableAnimationInfo:
                                    break;
                                case FunctionType.VariableBool:
                                    if (typeof(T) == typeof(bool))
                                        return (T)(object)Bools.Get("initial_value");
                                    break;
                                case FunctionType.VariableColour:
                                    break;
                                case FunctionType.VariableEnum:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)EnumIndexes.Get("initial_value");
                                    break;
                                case FunctionType.VariableEnumString:
                                    break;
                                case FunctionType.VariableFilterObject:
                                    break;
                                case FunctionType.VariableFlashScreenColour:
                                    break;
                                case FunctionType.VariableFloat:
                                    break;
                                case FunctionType.VariableHackingConfig:
                                    break;
                                case FunctionType.VariableInt:
                                    if (typeof(T) == typeof(int))
                                        return (T)(object)Integers.Get("initial_value");
                                    break;
                                case FunctionType.VariableObject:
                                    break;
                                case FunctionType.VariablePosition:
                                    break;
                                case FunctionType.VariableString:
                                    //TODO - if implementing strings, the int call returns the anim hash as int
                                    break;
                                case FunctionType.VariableThePlayer:
                                    break;
                                case FunctionType.VariableTriggerObject:
                                    break;
                                case FunctionType.VariableVector:
                                    break;
                                case FunctionType.VariableVector2:
                                    break;
                                case FunctionType.VectorAdd:
                                    break;
                                case FunctionType.VectorDirection:
                                    break;
                                case FunctionType.VectorDistance:
                                    break;
                                case FunctionType.VectorLinearInterpolateSpeed:
                                    break;
                                case FunctionType.VectorLinearInterpolateTimed:
                                    break;
                                case FunctionType.VectorLinearProportion:
                                    break;
                                case FunctionType.VectorMath:
                                    break;
                                case FunctionType.VectorModulus:
                                    if (typeof(T) == typeof(float))
                                    {
                                        Vector3 val = Vectors.Get("Input");
                                        return (T)(object)(float)Math.Sqrt(val.X * val.X + val.Y * val.Y + val.Z * val.Z);
                                    }
                                    break;
                                case FunctionType.VectorMultiply:
                                    break;
                                case FunctionType.VectorMultiplyByPos:
                                    break;
                                case FunctionType.VectorNormalise:
                                    break;
                                case FunctionType.VectorProduct:
                                    break;
                                case FunctionType.VectorReflect:
                                    break;
                                case FunctionType.VectorRotateByPos:
                                    break;
                                case FunctionType.VectorRotatePitch:
                                    break;
                                case FunctionType.VectorRotateRoll:
                                    break;
                                case FunctionType.VectorRotateYaw:
                                    break;
                                case FunctionType.VectorScale:
                                    break;
                                case FunctionType.VectorSubtract:
                                    break;
                                case FunctionType.VectorYaw:
                                    break;
                                case FunctionType.VideoCapture:
                                    break;
                                case FunctionType.VignetteSettings:
                                    break;
                                case FunctionType.VisibilityMaster:
                                    break;
                                case FunctionType.Weapon_AINotifier:
                                    break;
                                case FunctionType.WEAPON_AmmoTypeFilter:
                                    break;
                                case FunctionType.WEAPON_AttackerFilter:
                                    break;
                                case FunctionType.WEAPON_DamageFilter:
                                    break;
                                case FunctionType.WEAPON_DidHitSomethingFilter:
                                    break;
                                case FunctionType.WEAPON_Effect:
                                    break;
                                case FunctionType.WEAPON_GiveToCharacter:
                                    break;
                                case FunctionType.WEAPON_GiveToPlayer:
                                    break;
                                case FunctionType.WEAPON_ImpactAngleFilter:
                                    break;
                                case FunctionType.WEAPON_ImpactCharacterFilter:
                                    break;
                                case FunctionType.WEAPON_ImpactEffect:
                                    break;
                                case FunctionType.WEAPON_ImpactFilter:
                                    break;
                                case FunctionType.WEAPON_ImpactInspector:
                                    break;
                                case FunctionType.WEAPON_ImpactOrientationFilter:
                                    break;
                                case FunctionType.WEAPON_MultiFilter:
                                    break;
                                case FunctionType.WEAPON_TargetObjectFilter:
                                    break;
                                case FunctionType.Zone:
                                    break;
                                case FunctionType.ZoneExclusionLink:
                                    break;
                                case FunctionType.ZoneInterface:
                                    break;
                                case FunctionType.ZoneLink:
                                    break;
                                case FunctionType.ZoneLoaded:
                                    break;
                                default:
                                    break;
                            }
                        }
                        else
                        {
                            //if it's a composite instance, we need to step inside the composite and look up the logic related to the VariableEntity (including overrides).
                            //if the value is set on this entity, we use that, but otherwise it comes from internal logic
                        }
                    }
                    break;

                case EntityVariant.VARIABLE:

                    break;
            }

            if (typeof(T) == typeof(bool))
                return (T)(object)false;
            if (typeof(T) == typeof(int))
                return (T)(object)0;
            if (typeof(T) == typeof(float))
                return (T)(object)0.0f;

            return (T)(object)null;
        }
    }

    public class InstancedComposite
    {
        public ShortGuid InstanceID;
        public List<InstancedEntity> Entities = new List<InstancedEntity>();
    }

    public static class InstanceWriter
    {
        private static List<InstancedEntity> AllEntities = new List<InstancedEntity>();
        private static List<InstancedComposite> AllComposites = new List<InstancedComposite>();

        private static InstancedComposite Root = new InstancedComposite();

        public static void DoStuff(Level level)
        {
            GenerateInstances(level, level.Commands.EntryPoints[0], new EntityPath(), Root);

            level.PhysicsMaps.Entries.Clear();
            WritePhysicsMaps(level, Root);
            level.PhysicsMaps.Save();

            string gsdfsd = "";
        }

        private static void GenerateInstances(Level level, Composite composite, EntityPath path, InstancedComposite compositeInstance)
        {
            //First, create all 'instanced entity' objects - these populate their default bool values on creation
            foreach (Entity entity in composite.GetEntities())
            {
                EntityPath pathToThisEntity = path.Copy();
                pathToThisEntity.AddNextStep(entity);

                InstancedEntity newInstance = new InstancedEntity(level, composite, entity, pathToThisEntity);
                newInstance.ThisCompositeInstance = compositeInstance;
                compositeInstance.Entities.Add(newInstance);
            }

            //Next, hook up the instanced entity references in cases where they provide boolean parameter data 
            foreach (InstancedEntity entity in compositeInstance.Entities)
            {
                entity.PopulateLinks(compositeInstance.Entities);
            }

            AllEntities.AddRange(compositeInstance.Entities);
            AllComposites.Add(compositeInstance);

            //TODO: need to modify the ParameterValues depending on aliases - also need to track them down the hierarchy

            //Now, traverse down in to any child composites, and rinse and repeat
            foreach (FunctionEntity function in composite.functions)
            {
                if (function.function.IsFunctionType)
                    continue;

                Composite child = level.Commands.GetComposite(function.function);
                if (child == null)
                    continue;

                EntityPath newPath = path.Copy();
                newPath.AddNextStep(function);
                InstancedComposite newInstance = new InstancedComposite();
                newInstance.InstanceID = newPath.GenerateCompositeInstanceID();
                compositeInstance.Entities.FirstOrDefault(o => o.Entity == function).ChildCompositeInstance = newInstance;
                GenerateInstances(level, child, newPath, newInstance);
            }
        }

        private static void WritePhysicsMaps(Level level, InstancedComposite composite)
        {
            ShortGuid GUID_DYNAMIC_PHYSICS_SYSTEM = ShortGuidUtils.Generate("DYNAMIC_PHYSICS_SYSTEM");

            foreach (InstancedEntity entity in composite.Entities)
            {
                if (entity.Entity.variant != EntityVariant.FUNCTION)
                    continue;

                FunctionEntity function = (FunctionEntity)entity.Entity;

                if (function.function.IsFunctionType)
                {
                    switch (function.function.AsFunctionType)
                    {
                        case FunctionType.PhysicsSystem:
                            ResourceReference physicsSystem = function.GetResource(ResourceType.DYNAMIC_PHYSICS_SYSTEM);
                            if (physicsSystem == null) break;

                            EntityPath pathToThisEntity = entity.Path.Copy();
                            (Vector3 position, Quaternion rotation) = level.Commands.Utils.CalculateInstancedPosition(pathToThisEntity);
                            ShortGuid compositeInstanceID = pathToThisEntity.GenerateCompositeInstanceID();
                            pathToThisEntity.GoBackOneStep();
                            EntityHandle compositeInstanceReference = new EntityHandle()
                            {
                                entity_id = pathToThisEntity.GetPointedEntityID(),
                                composite_instance_id = pathToThisEntity.GenerateCompositeInstanceID()
                            };

                            level.PhysicsMaps.Entries.Add(new PhysicsMaps.Entry()
                            {
                                physics_system_index = physicsSystem.PhysicsSystemIndex,
                                resource_type = GUID_DYNAMIC_PHYSICS_SYSTEM,
                                composite_instance_id = compositeInstanceID,
                                entity = compositeInstanceReference,
                                Position = position,
                                Rotation = rotation
                            });
                            break;
                    }
                }

                if (entity.ChildCompositeInstance != null)
                {
                    if (entity.Bools.Get("is_template"))
                        continue;

                    if (entity.Bools.Get("deleted"))
                        continue;

                    WritePhysicsMaps(level, entity.ChildCompositeInstance);
                }
            }
        }

    }
}