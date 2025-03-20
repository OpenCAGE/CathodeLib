//#define DO_DEBUG_DUMP

using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

namespace CATHODE.Scripting
{
    //This serves as a helpful extension to manage entity names
    public static class EntityUtils
    {
        private static EntityNameTable _vanilla;
        private static EntityNameTable _custom;

        public static Commands LinkedCommands => _commands;
        private static Commands _commands;

        /* Load all standard entity/composite names from our offline DB */
        static EntityUtils()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            byte[] dbContent = File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/composite_entity_names.bin");
#else
            byte[] dbContent = CathodeLib.Properties.Resources.composite_entity_names;
            if (File.Exists("LocalDB/composite_entity_names.bin"))
                dbContent = File.ReadAllBytes("LocalDB/composite_entity_names.bin");
#endif
            BinaryReader reader = new BinaryReader(new MemoryStream(dbContent));
            _vanilla = new EntityNameTable(reader);
            _custom = new EntityNameTable();
            reader.Close();

#if DO_DEBUG_DUMP
            Directory.CreateDirectory("DebugDump/entities");
            foreach (KeyValuePair<ShortGuid, Dictionary<ShortGuid, string>> entry in _vanilla.names)
            {
                List<string> names = new List<string>();
                foreach (KeyValuePair<ShortGuid, string> value in entry.Value)
                {
                    names.Add(value.Value);
                }
                names.Sort();
                File.WriteAllLines("DebugDump/entities/" + entry.Key.ToByteString() + ".txt", names);
            }
#endif
        }

        //For testing
        public static List<string> GetAllVanillaNames()
        {
            List<string> names = new List<string>();
            foreach (var entry in _vanilla.names)
            {
                foreach (var entry2 in entry.Value)
                {
                    names.Add(entry2.Value);
                }
            }
            return names;
        }

        /* Optionally, link a Commands file which can be used to save custom entity names to */
        public static void LinkCommands(Commands commands)
        {
            if (_commands != null)
            {
                _commands.OnLoadSuccess -= LoadCustomNames;
                _commands.OnSaveSuccess -= SaveCustomNames;
            }

            _commands = commands;
            if (_commands == null) return;

            _commands.OnLoadSuccess += LoadCustomNames;
            _commands.OnSaveSuccess += SaveCustomNames;

            LoadCustomNames(_commands.Filepath);
        }

        /* Get the name of an entity contained within a composite */
        public static string GetName(Composite composite, Entity entity)
        {
            return GetName(composite.shortGUID, entity.shortGUID);
        }
        public static string GetName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (_custom.names.TryGetValue(compositeID, out Dictionary<ShortGuid, string> customComposite))
                if (customComposite.TryGetValue(entityID, out string customName))
                    return customName;
            if (_vanilla.names.TryGetValue(compositeID, out Dictionary<ShortGuid, string> vanillaComposite))
                if (vanillaComposite.TryGetValue(entityID, out string vanillaName))
                    return vanillaName;
            return entityID.ToByteString();
        }

        /* Set the name of an entity contained within a composite */
        public static void SetName(Composite composite, Entity entity, string name)
        {
            SetName(composite.shortGUID, entity.shortGUID, name);
        }
        public static void SetName(ShortGuid compositeID, ShortGuid entityID, string name)
        {
            if (!_custom.names.ContainsKey(compositeID))
                _custom.names.Add(compositeID, new Dictionary<ShortGuid, string>());

            if (!_custom.names[compositeID].ContainsKey(entityID))
                _custom.names[compositeID].Add(entityID, name);
            else
                _custom.names[compositeID][entityID] = name;
        }

        /* Clear the name of an entity contained within a composite */
        public static void ClearName(ShortGuid compositeID, ShortGuid entityID)
        {
            if (_custom.names.ContainsKey(compositeID))
                _custom.names[compositeID].Remove(entityID);
        }

        /* Applies all default parameter data to a Function entity - optionally, you can avoid overwriting existing members, but this will by default */
        public static void ApplyDefaults(FunctionEntity entity, bool includeInheritedMembers = true, bool overwriteExisting = true)
        {
            ApplyDefaults(entity, !CommandsUtils.FunctionTypeExists(entity.function) ? FunctionType.CompositeInterface : CommandsUtils.GetFunctionType(entity.function), includeInheritedMembers, overwriteExisting);
        }
        public static void ApplyDefaults(ProxyEntity entity, bool includeInheritedMembers = true, bool overwriteExisting = true)
        {
            //TODO: should we also populate defaults based on the entity we're pointing to?
            ApplyDefaults(entity, FunctionType.ProxyInterface, includeInheritedMembers, overwriteExisting);
        }
        private static void ApplyDefaults(Entity entity, FunctionType currentType, bool includeInheritedMembers, bool overwriteExisting)
        {
            //Figure out the chain of inheritance to this function type
            List<FunctionType> inheritance = new List<FunctionType>();
            FunctionType typeDescending = currentType;
            while (true)
            {
                inheritance.Add(typeDescending);
                if (!includeInheritedMembers) break;
                FunctionType? baseFunc = GetBaseFunction(typeDescending);
                if (baseFunc == null) break;
                typeDescending = baseFunc.Value;
            }
            inheritance.Reverse();

            //Apply parameters
            for (int i = 0; i < inheritance.Count; i++)
                ApplyDefaultsForFunction(
                    entity,
                    inheritance[i],
                    //TODO: need to actually validate if these are the right ones to add. maybe we should allow the flags to be passed in?
                    //ParameterVariant.STATE_PARAMETER | ParameterVariant.INPUT_PIN | ParameterVariant.OUTPUT_PIN | ParameterVariant.PARAMETER | ParameterVariant.INTERNAL, 
                    ParameterVariant.PARAMETER,
                    overwriteExisting);

            //If we're a composite reference, add the composite's parameters and inputs too
            if (entity.variant == EntityVariant.FUNCTION && !CommandsUtils.FunctionTypeExists(((FunctionEntity)entity).function))
            {
                if (_commands == null) return;
                Composite comp = _commands.Entries.FirstOrDefault(o => o.shortGUID == ((FunctionEntity)entity).function);
                if (comp == null) return;
                for (int i = 0; i < comp.variables.Count; i++)
                {
                    //TODO: this adds a dependency from EntityUtils to CompositeUtils. we should ensure they use the same LinkedCommands (or just improve this dump way of doing it).
                    CompositePinInfoTable.PinInfo info = CompositeUtils.GetParameterInfo(comp, comp.variables[i]);
                    if (info == null)
                        continue;
                    CompositePinType pinType = (CompositePinType)info.PinTypeGUID.ToUInt32();
                    //TODO: need to filter these to the ones that should actually be params. i assume it's inputs and methods?
                    switch (pinType)
                    {
                        //case CompositePinType.CompositeReferencePin:
                        case CompositePinType.CompositeInputVariablePin:
                        case CompositePinType.CompositeInputAnimationInfoVariablePin:
                        case CompositePinType.CompositeInputBoolVariablePin:
                        case CompositePinType.CompositeInputDirectionVariablePin:
                        case CompositePinType.CompositeInputEnumStringVariablePin:
                        case CompositePinType.CompositeInputFloatVariablePin:
                        case CompositePinType.CompositeInputIntVariablePin:
                        case CompositePinType.CompositeInputObjectVariablePin:
                        case CompositePinType.CompositeInputPositionVariablePin:
                        case CompositePinType.CompositeInputStringVariablePin:
                        case CompositePinType.CompositeInputZoneLinkPtrVariablePin:
                        case CompositePinType.CompositeInputZonePtrVariablePin:
                        //case CompositePinType.CompositeOutputVariablePin:
                        //case CompositePinType.CompositeOutputAnimationInfoVariablePin:
                        //case CompositePinType.CompositeOutputBoolVariablePin:
                        //case CompositePinType.CompositeOutputDirectionVariablePin:
                        //case CompositePinType.CompositeOutputEnumStringVariablePin:
                        //case CompositePinType.CompositeOutputFloatVariablePin:
                        //case CompositePinType.CompositeOutputIntVariablePin:
                        //case CompositePinType.CompositeOutputObjectVariablePin:
                        //case CompositePinType.CompositeOutputPositionVariablePin:
                        //case CompositePinType.CompositeOutputStringVariablePin:
                        //case CompositePinType.CompositeOutputZoneLinkPtrVariablePin:
                        //case CompositePinType.CompositeOutputZonePtrVariablePin:
                        //case CompositePinType.CompositeTargetPin:
                        case CompositePinType.CompositeMethodPin:
                            entity.AddParameter(comp.variables[i].name, comp.variables[i].type, ParameterVariant.INPUT_PIN, overwriteExisting);
                            break;
                        case CompositePinType.CompositeInputEnumVariablePin:
                        //case CompositePinType.CompositeOutputEnumVariablePin:
                            {
                                Parameter param = comp.variables[i].GetParameter(comp.variables[i].name);
                                int paramI = 0;
                                if (param != null && param.content.dataType == DataType.ENUM)
                                    paramI = ((cEnum)param.content).enumIndex;
                                entity.AddParameter(comp.variables[i].name, new cEnum(info.PinEnumTypeGUID, paramI), pinType == CompositePinType.CompositeInputEnumVariablePin ? ParameterVariant.INPUT_PIN : ParameterVariant.OUTPUT_PIN, overwriteExisting);
                            }
                            break;
                    }
                }
            }
        }

        /* Gets the function this function inherits from - if it inherits from nothing, it will return null */
        public static FunctionType? GetBaseFunction(FunctionEntity entity)
        {
            return GetBaseFunction(CommandsUtils.GetFunctionType(entity));
        }
        public static FunctionType? GetBaseFunction(FunctionType type)
        {
            switch (type)
            {
                case FunctionType.AccessTerminal:
                    return FunctionType.ScriptInterface;
                case FunctionType.AchievementMonitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.AchievementStat:
                    return FunctionType.ScriptInterface;
                case FunctionType.AchievementUniqueCounter:
                    return FunctionType.ScriptInterface;
                case FunctionType.AddExitObjective:
                    return FunctionType.ScriptInterface;
                case FunctionType.AddItemsToGCPool:
                    return FunctionType.ScriptInterface;
                case FunctionType.AddToInventory:
                    return FunctionType.ScriptInterface;
                case FunctionType.AILightCurveSettings:
                    return FunctionType.InspectorInterface;
                case FunctionType.AIMED_ITEM:
                    return FunctionType.EQUIPPABLE_ITEM;
                case FunctionType.AIMED_WEAPON:
                    return FunctionType.AIMED_ITEM;
                case FunctionType.ALLIANCE_ResetAll:
                    return FunctionType.ScriptInterface;
                case FunctionType.ALLIANCE_SetDisposition:
                    return FunctionType.ScriptInterface;
                case FunctionType.AllocateGCItemFromPoolBySubset:
                    return FunctionType.ScriptInterface;
                case FunctionType.AllocateGCItemsFromPool:
                    return FunctionType.ScriptInterface;
                case FunctionType.AllPlayersReady:
                    return FunctionType.SensorInterface;
                case FunctionType.AnimatedModelAttachmentNode:
                    return FunctionType.ScriptInterface;
                case FunctionType.AnimationMask:
                    return FunctionType.ScriptInterface;
                case FunctionType.ApplyRelativeTransform:
                    return FunctionType.ScriptInterface;
                case FunctionType.AreaHitMonitor:
                    return FunctionType.SensorInterface;
                case FunctionType.AssetSpawner:
                    return FunctionType.AttachmentInterface;
                case FunctionType.AttachmentInterface:
                    return FunctionType.ScriptInterface;
                case FunctionType.Benchmark:
                    return FunctionType.ScriptInterface;
                case FunctionType.BindObjectsMultiplexer:
                    return FunctionType.ModifierInterface;
                case FunctionType.BlendLowResFrame:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.BloomSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.BoneAttachedCamera:
                    return FunctionType.CameraBehaviorInterface;
                case FunctionType.BooleanLogicInterface:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.BooleanLogicOperation:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.Box:
                    return FunctionType.AttachmentInterface;
                case FunctionType.BroadcastTrigger:
                    return FunctionType.ScriptInterface;
                case FunctionType.BulletChamber:
                    return FunctionType.ScriptInterface;
                case FunctionType.ButtonMashPrompt:
                    return FunctionType.ScriptInterface;
                case FunctionType.CAGEAnimation:
                    return FunctionType.TransformerInterface;
                case FunctionType.CameraCollisionBox:
                    return FunctionType.Box;
                case FunctionType.CameraDofController:
                    return FunctionType.CameraBehaviorInterface;
                case FunctionType.CameraPathDriven:
                    return FunctionType.CameraBehaviorInterface;
                case FunctionType.CameraPlayAnimation:
                    return FunctionType.SensorInterface;
                case FunctionType.CameraResource:
                    return FunctionType.AttachmentInterface;
                case FunctionType.CameraShake:
                    return FunctionType.CameraBehaviorInterface;
                case FunctionType.CamPeek:
                    return FunctionType.CameraBehaviorInterface;
                case FunctionType.Character:
                    return FunctionType.ScriptInterface;
                case FunctionType.CharacterAttachmentNode:
                    return FunctionType.ScriptInterface;
                case FunctionType.CharacterCommand:
                    return FunctionType.ScriptInterface;
                case FunctionType.CharacterMonitor:
                    return FunctionType.SensorInterface;
                case FunctionType.CharacterShivaArms:
                    return FunctionType.ScriptInterface;
                case FunctionType.CharacterTypeMonitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.Checkpoint:
                    return FunctionType.ModifierInterface;
                case FunctionType.CheckpointRestoredNotify:
                    return FunctionType.ScriptInterface;
                case FunctionType.ChokePoint:
                    return FunctionType.AttachmentInterface;
                case FunctionType.CHR_DamageMonitor:
                    return FunctionType.MonitorBase;
                case FunctionType.CHR_DeathMonitor:
                    return FunctionType.MonitorBase;
                case FunctionType.CHR_DeepCrouch:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_GetAlliance:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_GetHealth:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_GetTorch:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_HasWeaponOfType:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_HoldBreath:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_IsWithinRange:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_KnockedOutMonitor:
                    return FunctionType.MonitorBase;
                case FunctionType.CHR_LocomotionDuck:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_LocomotionEffect:
                    return FunctionType.ModifierInterface;
                case FunctionType.CHR_LocomotionModifier:
                    return FunctionType.ModifierInterface;
                case FunctionType.CHR_ModifyBreathing:
                    return FunctionType.ScriptInterface;
                case FunctionType.Chr_PlayerCrouch:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_PlayNPCBark:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_PlaySecondaryAnimation:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_RetreatMonitor:
                    return FunctionType.MonitorBase;
                case FunctionType.CHR_SetAlliance:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetAndroidThrowTarget:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetDebugDisplayName:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetFacehuggerAggroRadius:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetFocalPoint:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetHeadVisibility:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetHealth:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetInvincibility:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetMood:
                    return FunctionType.ModifierInterface;
                case FunctionType.CHR_SetShowInMotionTracker:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetSubModelVisibility:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetTacticalPosition:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetTacticalPositionToTarget:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_SetTorch:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_TakeDamage:
                    return FunctionType.ScriptInterface;
                case FunctionType.CHR_TorchMonitor:
                    return FunctionType.MonitorBase;
                case FunctionType.CHR_VentMonitor:
                    return FunctionType.MonitorBase;
                case FunctionType.CHR_WeaponFireMonitor:
                    return FunctionType.MonitorBase;
                case FunctionType.ChromaticAberrations:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.ClearPrimaryObjective:
                    return FunctionType.ScriptInterface;
                case FunctionType.ClearSubObjective:
                    return FunctionType.ScriptInterface;
                case FunctionType.ClipPlanesController:
                    return FunctionType.CameraBehaviorInterface;
                case FunctionType.CloseableInterface:
                    return FunctionType.ScriptInterface;
                case FunctionType.CMD_AimAt:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_AimAtCurrentTarget:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_Die:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_Follow:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_FollowUsingJobs:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_ForceMeleeAttack:
                    return FunctionType.ScriptInterface;
                case FunctionType.CMD_ForceReloadWeapon:
                    return FunctionType.ScriptInterface;
                case FunctionType.CMD_GoTo:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_GoToCover:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_HolsterWeapon:
                    return FunctionType.ScriptInterface;
                case FunctionType.CMD_Idle:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_LaunchMeleeAttack:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_ModifyCombatBehaviour:
                    return FunctionType.ScriptInterface;
                case FunctionType.CMD_MoveTowards:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_PlayAnimation:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_Ragdoll:
                    return FunctionType.ScriptInterface;
                case FunctionType.CMD_ShootAt:
                    return FunctionType.CharacterCommand;
                case FunctionType.CMD_StopScript:
                    return FunctionType.CharacterCommand;
                case FunctionType.CollectIDTag:
                    return FunctionType.ScriptInterface;
                case FunctionType.CollectNostromoLog:
                    return FunctionType.ScriptInterface;
                case FunctionType.CollectSevastopolLog:
                    return FunctionType.ScriptInterface;
                case FunctionType.CollisionBarrier:
                    return FunctionType.Box;
                case FunctionType.ColourCorrectionTransition:
                    return FunctionType.TransformerInterface;
                case FunctionType.ColourSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.CompositeInterface:
                    return FunctionType.AttachmentInterface;
                case FunctionType.CompoundVolume:
                    return FunctionType.InspectorInterface;
                case FunctionType.ControllableRange:
                    return FunctionType.CameraBehaviorInterface;
                case FunctionType.Convo:
                    return FunctionType.ScriptInterface;
                case FunctionType.Counter:
                    return FunctionType.ModifierInterface;
                case FunctionType.CoverExclusionArea:
                    return FunctionType.ScriptInterface;
                case FunctionType.CoverLine:
                    return FunctionType.ScriptInterface;
                case FunctionType.Custom_Hiding_Controller:
                    return FunctionType.ScriptInterface;
                case FunctionType.Custom_Hiding_Vignette_controller:
                    return FunctionType.ScriptInterface;
                case FunctionType.DayToneMappingSettings:
                    return FunctionType.TransformerInterface;
                case FunctionType.DEBUG_SenseLevels:
                    return FunctionType.InspectorInterface;
                case FunctionType.DebugCamera:
                    return FunctionType.SensorInterface;
                case FunctionType.DebugCaptureScreenShot:
                    return FunctionType.AttachmentInterface;
                case FunctionType.DebugCheckpoint:
                    return FunctionType.ModifierInterface;
                case FunctionType.DebugEnvironmentMarker:
                    return FunctionType.SensorInterface;
                case FunctionType.DebugGraph:
                    return FunctionType.SensorInterface;
                case FunctionType.DebugLoadCheckpoint:
                    return FunctionType.ModifierInterface;
                case FunctionType.DebugObjectMarker:
                    return FunctionType.ScriptInterface;
                case FunctionType.DebugPositionMarker:
                    return FunctionType.SensorInterface;
                case FunctionType.DebugText:
                    return FunctionType.SensorInterface;
                case FunctionType.DebugTextStacking:
                    return FunctionType.SensorInterface;
                case FunctionType.DeleteBlankPanel:
                    return FunctionType.Filter;
                case FunctionType.DeleteButtonDisk:
                    return FunctionType.Filter;
                case FunctionType.DeleteButtonKeys:
                    return FunctionType.Filter;
                case FunctionType.DeleteCuttingPanel:
                    return FunctionType.Filter;
                case FunctionType.DeleteHacking:
                    return FunctionType.Filter;
                case FunctionType.DeleteHousing:
                    return FunctionType.Filter;
                case FunctionType.DeleteKeypad:
                    return FunctionType.Filter;
                case FunctionType.DeletePullLever:
                    return FunctionType.Filter;
                case FunctionType.DeleteRotateLever:
                    return FunctionType.Filter;
                case FunctionType.DepthOfFieldSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.DespawnCharacter:
                    return FunctionType.ScriptInterface;
                case FunctionType.DespawnPlayer:
                    return FunctionType.ScriptInterface;
                case FunctionType.Display_Element_On_Map:
                    return FunctionType.ScriptInterface;
                case FunctionType.DisplayMessage:
                    return FunctionType.ScriptInterface;
                case FunctionType.DisplayMessageWithCallbacks:
                    return FunctionType.ScriptInterface;
                case FunctionType.DistortionOverlay:
                    return FunctionType.TransformerInterface;
                case FunctionType.DistortionSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.Door:
                    return FunctionType.GateResourceInterface;
                case FunctionType.DoorStatus:
                    return FunctionType.ScriptInterface;
                case FunctionType.DurangoVideoCapture:
                    return FunctionType.TransformerInterface;
                case FunctionType.EFFECT_DirectionalPhysics:
                    return FunctionType.SensorAttachmentInterface;
                case FunctionType.EFFECT_EntityGenerator:
                    return FunctionType.AttachmentInterface;
                case FunctionType.EFFECT_ImpactGenerator:
                    return FunctionType.AttachmentInterface;
                case FunctionType.EggSpawner:
                    return FunctionType.ScriptInterface;
                case FunctionType.ElapsedTimer:
                    return FunctionType.ScriptInterface;
                case FunctionType.EnableMotionTrackerPassiveAudio:
                    return FunctionType.ScriptInterface;
                case FunctionType.EndGame:
                    return FunctionType.ScriptInterface;
                case FunctionType.ENT_Debug_Exit_Game:
                    return FunctionType.InspectorInterface;
                case FunctionType.EnvironmentMap:
                    return FunctionType.AttachmentInterface;
                case FunctionType.EnvironmentModelReference:
                    return FunctionType.AttachmentInterface;
                case FunctionType.EQUIPPABLE_ITEM:
                    return FunctionType.ScriptInterface;
                case FunctionType.EvaluatorInterface:
                    return FunctionType.InspectorInterface;
                case FunctionType.ExclusiveMaster:
                    return FunctionType.ScriptInterface;
                case FunctionType.Explosion_AINotifier:
                    return FunctionType.ScriptInterface;
                case FunctionType.ExternalVariableBool:
                    return FunctionType.ScriptInterface;
                case FunctionType.FakeAILightSourceInPlayersHand:
                    return FunctionType.ScriptInterface;
                case FunctionType.FilmGrainSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.Filter:
                    return FunctionType.ScriptInterface;
                case FunctionType.FilterAbsorber:
                    return FunctionType.SensorInterface;
                case FunctionType.FilterAnd:
                    return FunctionType.Filter;
                case FunctionType.FilterBelongsToAlliance:
                    return FunctionType.Filter;
                case FunctionType.FilterCanSeeTarget:
                    return FunctionType.Filter;
                case FunctionType.FilterHasBehaviourTreeFlagSet:
                    return FunctionType.Filter;
                case FunctionType.FilterHasPlayerCollectedIdTag:
                    return FunctionType.Filter;
                case FunctionType.FilterHasWeaponEquipped:
                    return FunctionType.Filter;
                case FunctionType.FilterHasWeaponOfType:
                    return FunctionType.Filter;
                case FunctionType.FilterIsACharacter:
                    return FunctionType.Filter;
                case FunctionType.FilterIsAgressing:
                    return FunctionType.Filter;
                case FunctionType.FilterIsAnySaveInProgress:
                    return FunctionType.Filter;
                case FunctionType.FilterIsAPlayer:
                    return FunctionType.Filter;
                case FunctionType.FilterIsCharacter:
                    return FunctionType.Filter;
                case FunctionType.FilterIsCharacterClass:
                    return FunctionType.Filter;
                case FunctionType.FilterIsCharacterClassCombo:
                    return FunctionType.Filter;
                case FunctionType.FilterIsDead:
                    return FunctionType.Filter;
                case FunctionType.FilterIsEnemyOfAllianceGroup:
                    return FunctionType.Filter;
                case FunctionType.FilterIsEnemyOfCharacter:
                    return FunctionType.Filter;
                case FunctionType.FilterIsEnemyOfPlayer:
                    return FunctionType.Filter;
                case FunctionType.FilterIsFacingTarget:
                    return FunctionType.Filter;
                case FunctionType.FilterIsHumanNPC:
                    return FunctionType.Filter;
                case FunctionType.FilterIsInAGroup:
                    return FunctionType.Filter;
                case FunctionType.FilterIsInAlertnessState:
                    return FunctionType.Filter;
                case FunctionType.FilterIsinInventory:
                    return FunctionType.Filter;
                case FunctionType.FilterIsInLocomotionState:
                    return FunctionType.Filter;
                case FunctionType.FilterIsInWeaponRange:
                    return FunctionType.Filter;
                case FunctionType.FilterIsLocalPlayer:
                    return FunctionType.Filter;
                case FunctionType.FilterIsNotDeadManWalking:
                    return FunctionType.Filter;
                case FunctionType.FilterIsObject:
                    return FunctionType.Filter;
                case FunctionType.FilterIsPhysics:
                    return FunctionType.Filter;
                case FunctionType.FilterIsPhysicsObject:
                    return FunctionType.Filter;
                case FunctionType.FilterIsPlatform:
                    return FunctionType.Filter;
                case FunctionType.FilterIsUsingDevice:
                    return FunctionType.Filter;
                case FunctionType.FilterIsValidInventoryItem:
                    return FunctionType.Filter;
                case FunctionType.FilterIsWithdrawnAlien:
                    return FunctionType.Filter;
                case FunctionType.FilterNot:
                    return FunctionType.Filter;
                case FunctionType.FilterOr:
                    return FunctionType.Filter;
                case FunctionType.FilterSmallestUsedDifficulty:
                    return FunctionType.Filter;
                case FunctionType.FixedCamera:
                    return FunctionType.CameraBehaviorInterface;
                case FunctionType.FlareSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.FlareTask:
                    return FunctionType.Task;
                case FunctionType.FlashCallback:
                    return FunctionType.ScriptInterface;
                case FunctionType.FlashInvoke:
                    return FunctionType.ScriptInterface;
                case FunctionType.FlashScript:
                    return FunctionType.ScriptInterface;
                case FunctionType.FloatAbsolute:
                    return FunctionType.FloatOperation;
                case FunctionType.FloatAdd:
                    return FunctionType.FloatMath;
                case FunctionType.FloatAdd_All:
                    return FunctionType.FloatMath_All;
                case FunctionType.FloatClamp:
                    return FunctionType.ScriptInterface;
                case FunctionType.FloatClampMultiply:
                    return FunctionType.FloatMath;
                case FunctionType.FloatCompare:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.FloatDivide:
                    return FunctionType.FloatMath;
                case FunctionType.FloatEquals:
                    return FunctionType.FloatCompare;
                case FunctionType.FloatGetLinearProportion:
                    return FunctionType.ScriptInterface;
                case FunctionType.FloatGreaterThan:
                    return FunctionType.FloatCompare;
                case FunctionType.FloatGreaterThanOrEqual:
                    return FunctionType.FloatCompare;
                case FunctionType.FloatLessThan:
                    return FunctionType.FloatCompare;
                case FunctionType.FloatLessThanOrEqual:
                    return FunctionType.FloatCompare;
                case FunctionType.FloatLinearInterpolateSpeed:
                    return FunctionType.TransformerInterface;
                case FunctionType.FloatLinearInterpolateSpeedAdvanced:
                    return FunctionType.SensorInterface;
                case FunctionType.FloatLinearInterpolateTimed:
                    return FunctionType.TransformerInterface;
                case FunctionType.FloatLinearProportion:
                    return FunctionType.ScriptInterface;
                case FunctionType.FloatMath:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.FloatMath_All:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.FloatMax:
                    return FunctionType.FloatMath;
                case FunctionType.FloatMax_All:
                    return FunctionType.FloatMath_All;
                case FunctionType.FloatMin:
                    return FunctionType.FloatMath;
                case FunctionType.FloatMin_All:
                    return FunctionType.FloatMath_All;
                case FunctionType.FloatModulate:
                    return FunctionType.TransformerInterface;
                case FunctionType.FloatModulateRandom:
                    return FunctionType.TransformerInterface;
                case FunctionType.FloatMultiply:
                    return FunctionType.FloatMath;
                case FunctionType.FloatMultiply_All:
                    return FunctionType.FloatMath_All;
                case FunctionType.FloatMultiplyClamp:
                    return FunctionType.FloatMath;
                case FunctionType.FloatNotEqual:
                    return FunctionType.FloatCompare;
                case FunctionType.FloatOperation:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.FloatReciprocal:
                    return FunctionType.FloatOperation;
                case FunctionType.FloatRemainder:
                    return FunctionType.FloatMath;
                case FunctionType.FloatSmoothStep:
                    return FunctionType.ScriptInterface;
                case FunctionType.FloatSqrt:
                    return FunctionType.FloatOperation;
                case FunctionType.FloatSubtract:
                    return FunctionType.FloatMath;
                case FunctionType.FlushZoneCache:
                    return FunctionType.ScriptInterface;
                case FunctionType.FogBox:
                    return FunctionType.Box;
                case FunctionType.FogPlane:
                    return FunctionType.TransformerInterface;
                case FunctionType.FogSetting:
                    return FunctionType.ModifierInterface;
                case FunctionType.FogSphere:
                    return FunctionType.Sphere;
                case FunctionType.FollowTask:
                    return FunctionType.IdleTask;
                case FunctionType.Force_UI_Visibility:
                    return FunctionType.ScriptInterface;
                case FunctionType.FullScreenBlurSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.FullScreenOverlay:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.GameDVR:
                    return FunctionType.ScriptInterface;
                case FunctionType.GameOver:
                    return FunctionType.ScriptInterface;
                case FunctionType.GameOverCredits:
                    return FunctionType.ScriptInterface;
                case FunctionType.GameplayTip:
                    return FunctionType.ScriptInterface;
                case FunctionType.GameStateChanged:
                    return FunctionType.ModifierInterface;
                case FunctionType.GateInterface:
                    return FunctionType.ScriptInterface;
                case FunctionType.GateResourceInterface:
                    return FunctionType.ScriptInterface;
                case FunctionType.GenericHighlightEntity:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetBlueprintAvailable:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetBlueprintLevel:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetCentrePoint:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetCharacterRotationSpeed:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetClosestPercentOnSpline:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetClosestPoint:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetClosestPointFromSet:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetClosestPointOnSpline:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetComponentInterface:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.GetCurrentCameraFov:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetCurrentCameraPos:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetCurrentCameraTarget:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetCurrentPlaylistLevelIndex:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetFlashFloatValue:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetFlashIntValue:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetGatingToolLevel:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetInventoryItemName:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetNextPlaylistLevelName:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetPlayerHasGatingTool:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetPlayerHasKeycard:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetPointOnSpline:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetRotation:
                    return FunctionType.InspectorInterface;
                case FunctionType.GetSelectedCharacterId:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetSplineLength:
                    return FunctionType.ScriptInterface;
                case FunctionType.GetTranslation:
                    return FunctionType.InspectorInterface;
                case FunctionType.GetX:
                    return FunctionType.GetComponentInterface;
                case FunctionType.GetY:
                    return FunctionType.GetComponentInterface;
                case FunctionType.GetZ:
                    return FunctionType.GetComponentInterface;
                case FunctionType.GlobalEvent:
                    return FunctionType.ScriptInterface;
                case FunctionType.GlobalEventMonitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.GlobalPosition:
                    return FunctionType.ScriptVariable;
                case FunctionType.GoToFrontend:
                    return FunctionType.ScriptInterface;
                case FunctionType.GPU_PFXEmitterReference:
                    return FunctionType.SensorAttachmentInterface;
                case FunctionType.HableToneMappingSettings:
                    return FunctionType.TransformerInterface;
                case FunctionType.HackingGame:
                    return FunctionType.ScriptInterface;
                case FunctionType.HandCamera:
                    return FunctionType.CameraBehaviorInterface;
                case FunctionType.HasAccessAtDifficulty:
                    return FunctionType.ScriptInterface;
                case FunctionType.HeldItem_AINotifier:
                    return FunctionType.ScriptInterface;
                case FunctionType.HighSpecMotionBlurSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.HostOnlyTrigger:
                    return FunctionType.ScriptInterface;
                case FunctionType.IdleTask:
                    return FunctionType.Task;
                case FunctionType.ImpactSphere:
                    return FunctionType.AttachmentInterface;
                case FunctionType.InhibitActionsUntilRelease:
                    return FunctionType.ScriptInterface;
                case FunctionType.InspectorInterface:
                    return FunctionType.ScriptInterface;
                case FunctionType.IntegerAbsolute:
                    return FunctionType.IntegerOperation;
                case FunctionType.IntegerAdd:
                    return FunctionType.IntegerMath;
                case FunctionType.IntegerAdd_All:
                    return FunctionType.IntegerMath_All;
                case FunctionType.IntegerAnalyse:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.IntegerAnd:
                    return FunctionType.IntegerMath;
                case FunctionType.IntegerCompare:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.IntegerCompliment:
                    return FunctionType.IntegerOperation;
                case FunctionType.IntegerDivide:
                    return FunctionType.IntegerMath;
                case FunctionType.IntegerEquals:
                    return FunctionType.IntegerCompare;
                case FunctionType.IntegerGreaterThan:
                    return FunctionType.IntegerCompare;
                case FunctionType.IntegerGreaterThanOrEqual:
                    return FunctionType.IntegerCompare;
                case FunctionType.IntegerLessThan:
                    return FunctionType.IntegerCompare;
                case FunctionType.IntegerLessThanOrEqual:
                    return FunctionType.IntegerCompare;
                case FunctionType.IntegerMath:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.IntegerMath_All:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.IntegerMax:
                    return FunctionType.IntegerMath;
                case FunctionType.IntegerMax_All:
                    return FunctionType.IntegerMath_All;
                case FunctionType.IntegerMin:
                    return FunctionType.IntegerMath;
                case FunctionType.IntegerMin_All:
                    return FunctionType.IntegerMath_All;
                case FunctionType.IntegerMultiply:
                    return FunctionType.IntegerMath;
                case FunctionType.IntegerMultiply_All:
                    return FunctionType.IntegerMath_All;
                case FunctionType.IntegerNotEqual:
                    return FunctionType.IntegerCompare;
                case FunctionType.IntegerOperation:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.IntegerOr:
                    return FunctionType.IntegerMath;
                case FunctionType.IntegerRemainder:
                    return FunctionType.IntegerMath;
                case FunctionType.IntegerSubtract:
                    return FunctionType.IntegerMath;
                case FunctionType.Interaction:
                    return FunctionType.ScriptInterface;
                case FunctionType.InteractiveMovementControl:
                    return FunctionType.TransformerInterface;
                case FunctionType.Internal_JOB_SearchTarget:
                    return FunctionType.Job;
                case FunctionType.InventoryItem:
                    return FunctionType.ScriptInterface;
                case FunctionType.IrawanToneMappingSettings:
                    return FunctionType.TransformerInterface;
                case FunctionType.IsActive:
                    return FunctionType.StateQuery;
                case FunctionType.IsAttached:
                    return FunctionType.StateQuery;
                case FunctionType.IsCurrentLevelAChallengeMap:
                    return FunctionType.ScriptInterface;
                case FunctionType.IsCurrentLevelAPreorderMap:
                    return FunctionType.ScriptInterface;
                case FunctionType.IsEnabled:
                    return FunctionType.StateQuery;
                case FunctionType.IsInstallComplete:
                    return FunctionType.ScriptInterface;
                case FunctionType.IsLoaded:
                    return FunctionType.StateQuery;
                case FunctionType.IsLoading:
                    return FunctionType.StateQuery;
                case FunctionType.IsLocked:
                    return FunctionType.StateQuery;
                case FunctionType.IsMultiplayerMode:
                    return FunctionType.Filter;
                case FunctionType.IsOpen:
                    return FunctionType.StateQuery;
                case FunctionType.IsOpening:
                    return FunctionType.StateQuery;
                case FunctionType.IsPaused:
                    return FunctionType.StateQuery;
                case FunctionType.IsPlaylistTypeAll:
                    return FunctionType.ScriptInterface;
                case FunctionType.IsPlaylistTypeMarathon:
                    return FunctionType.ScriptInterface;
                case FunctionType.IsPlaylistTypeSingle:
                    return FunctionType.ScriptInterface;
                case FunctionType.IsSpawned:
                    return FunctionType.StateQuery;
                case FunctionType.IsStarted:
                    return FunctionType.StateQuery;
                case FunctionType.IsSuspended:
                    return FunctionType.StateQuery;
                case FunctionType.IsVisible:
                    return FunctionType.StateQuery;
                case FunctionType.Job:
                    return FunctionType.ScriptInterface;
                case FunctionType.JOB_AreaSweep:
                    return FunctionType.Job;
                case FunctionType.JOB_AreaSweepFlare:
                    return FunctionType.Job;
                case FunctionType.JOB_Assault:
                    return FunctionType.Job;
                case FunctionType.JOB_Follow:
                    return FunctionType.Job;
                case FunctionType.JOB_Follow_Centre:
                    return FunctionType.Job;
                case FunctionType.JOB_Idle:
                    return FunctionType.Job;
                case FunctionType.JOB_Panic:
                    return FunctionType.Job;
                case FunctionType.JOB_SpottingPosition:
                    return FunctionType.JobWithPosition;
                case FunctionType.JOB_SystematicSearch:
                    return FunctionType.Job;
                case FunctionType.JOB_SystematicSearchFlare:
                    return FunctionType.Job;
                case FunctionType.JobWithPosition:
                    return FunctionType.Job;
                case FunctionType.LeaderboardWriter:
                    return FunctionType.ScriptInterface;
                case FunctionType.LeaveGame:
                    return FunctionType.ScriptInterface;
                case FunctionType.LensDustSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.LevelCompletionTargets:
                    return FunctionType.ScriptInterface;
                case FunctionType.LevelInfo:
                    return FunctionType.ScriptInterface;
                case FunctionType.LevelLoaded:
                    return FunctionType.SensorInterface;
                case FunctionType.LightAdaptationSettings:
                    return FunctionType.TransformerInterface;
                case FunctionType.LightingMaster:
                    return FunctionType.ScriptInterface;
                case FunctionType.LightReference:
                    return FunctionType.AttachmentInterface;
                case FunctionType.LimitItemUse:
                    return FunctionType.ScriptInterface;
                case FunctionType.LODControls:
                    return FunctionType.ModifierInterface;
                case FunctionType.Logic_MultiGate:
                    return FunctionType.ModifierInterface;
                case FunctionType.Logic_Vent_Entrance:
                    return FunctionType.ScriptInterface;
                case FunctionType.Logic_Vent_System:
                    return FunctionType.ScriptInterface;
                case FunctionType.LogicAll:
                    return FunctionType.ScriptInterface;
                case FunctionType.LogicCounter:
                    return FunctionType.ScriptInterface;
                case FunctionType.LogicDelay:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.LogicGate:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.LogicGateAnd:
                    return FunctionType.BooleanLogicInterface;
                case FunctionType.LogicGateEquals:
                    return FunctionType.BooleanLogicInterface;
                case FunctionType.LogicGateNotEqual:
                    return FunctionType.BooleanLogicInterface;
                case FunctionType.LogicGateOr:
                    return FunctionType.BooleanLogicInterface;
                case FunctionType.LogicNot:
                    return FunctionType.BooleanLogicOperation;
                case FunctionType.LogicOnce:
                    return FunctionType.ModifierInterface;
                case FunctionType.LogicPressurePad:
                    return FunctionType.ScriptInterface;
                case FunctionType.LogicSwitch:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.LowResFrameCapture:
                    return FunctionType.ModifierInterface;
                case FunctionType.Map_Floor_Change:
                    return FunctionType.ScriptInterface;
                case FunctionType.MapAnchor:
                    return FunctionType.ScriptInterface;
                case FunctionType.MapItem:
                    return FunctionType.ScriptInterface;
                case FunctionType.Master:
                    return FunctionType.ScriptInterface;
                case FunctionType.MELEE_WEAPON:
                    return FunctionType.EQUIPPABLE_ITEM;
                case FunctionType.Minigames:
                    return FunctionType.SensorInterface;
                case FunctionType.MissionNumber:
                    return FunctionType.ScriptInterface;
                case FunctionType.ModelReference:
                    return FunctionType.AttachmentInterface;
                case FunctionType.ModifierInterface:
                    return FunctionType.InspectorInterface;
                case FunctionType.MonitorActionMap:
                    return FunctionType.SensorInterface;
                case FunctionType.MonitorBase:
                    return FunctionType.ScriptInterface;
                case FunctionType.MonitorPadInput:
                    return FunctionType.SensorInterface;
                case FunctionType.MotionTrackerMonitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.MotionTrackerPing:
                    return FunctionType.ScriptInterface;
                case FunctionType.MoveAlongSpline:
                    return FunctionType.TransformerInterface;
                case FunctionType.MoveInTime:
                    return FunctionType.TransformerInterface;
                case FunctionType.MoviePlayer:
                    return FunctionType.ScriptInterface;
                case FunctionType.MultipleCharacterAttachmentNode:
                    return FunctionType.ScriptInterface;
                case FunctionType.MultiplePickupSpawner:
                    return FunctionType.ScriptInterface;
                case FunctionType.MultitrackLoop:
                    return FunctionType.ScriptInterface;
                case FunctionType.MusicController:
                    return FunctionType.ScriptInterface;
                case FunctionType.MusicTrigger:
                    return FunctionType.ScriptInterface;
                case FunctionType.GCIP_WorldPickup:
                    return FunctionType.AttachmentInterface;
                case FunctionType.Torch_Control:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayForMinDuration:
                    return FunctionType.ScriptInterface;
                case FunctionType.NavMeshArea:
                    return FunctionType.ScriptInterface;
                case FunctionType.NavMeshBarrier:
                    return FunctionType.Box;
                case FunctionType.NavMeshExclusionArea:
                    return FunctionType.ScriptInterface;
                case FunctionType.NavMeshReachabilitySeedPoint:
                    return FunctionType.ScriptInterface;
                case FunctionType.NavMeshWalkablePlatform:
                    return FunctionType.ScriptInterface;
                case FunctionType.NetPlayerCounter:
                    return FunctionType.ScriptInterface;
                case FunctionType.NetworkedTimer:
                    return FunctionType.SensorInterface;
                case FunctionType.NetworkProxy:
                    return FunctionType.ScriptInterface;
                case FunctionType.NonInteractiveWater:
                    return FunctionType.TransformerInterface;
                case FunctionType.NonPersistentBool:
                    return FunctionType.ScriptVariable;
                case FunctionType.NonPersistentInt:
                    return FunctionType.ScriptVariable;
                case FunctionType.NPC_Aggression_Monitor:
                    return FunctionType.MonitorBase;
                case FunctionType.NPC_AlienConfig:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_AllSensesLimiter:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_ambush_monitor:
                    return FunctionType.MonitorBase;
                case FunctionType.NPC_AreaBox:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_behaviour_monitor:
                    return FunctionType.MonitorBase;
                case FunctionType.NPC_ClearDefendArea:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_ClearPursuitArea:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Coordinator:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Debug_Menu_Item:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_DefineBackstageAvoidanceArea:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_DynamicDialogue:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_DynamicDialogueGlobalRange:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_FakeSense:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_FollowOffset:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_ForceCombatTarget:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_ForceNextJob:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_ForceRetreat:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Gain_Aggression_In_Radius:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_GetCombatTarget:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_GetLastSensedPositionOfTarget:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Group_Death_Monitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Group_DeathCounter:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Highest_Awareness_Monitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_MeleeContext:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_multi_behaviour_monitor:
                    return FunctionType.MonitorBase;
                case FunctionType.NPC_navmesh_type_monitor:
                    return FunctionType.MonitorBase;
                case FunctionType.NPC_NotifyDynamicDialogueEvent:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Once:
                    return FunctionType.ModifierInterface;
                case FunctionType.NPC_ResetFiringStats:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_ResetSensesAndMemory:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SenseLimiter:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_set_behaviour_tree_flags:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetAgressionProgression:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetAimTarget:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetAlertness:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetAlienDevelopmentStage:
                    return FunctionType.InspectorInterface;
                case FunctionType.NPC_SetAutoTorchMode:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetChokePoint:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetDefendArea:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetFiringAccuracy:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetFiringRhythm:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetGunAimMode:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetHidingNearestLocation:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetHidingSearchRadius:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetInvisible:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetLocomotionStyleForJobs:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetLocomotionTargetSpeed:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetPursuitArea:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetRateOfFire:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetSafePoint:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetSenseSet:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetStartPos:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetTotallyBlindInDark:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SetupMenaceManager:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Sleeping_Android_Monitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Squad_DialogueMonitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Squad_GetAwarenessState:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_Squad_GetAwarenessWatermark:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_StopAiming:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_StopShooting:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_SuspiciousItem:
                    return FunctionType.SensorInterface;
                case FunctionType.NPC_TargetAcquire:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_TriggerAimRequest:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_TriggerShootRequest:
                    return FunctionType.ScriptInterface;
                case FunctionType.NPC_WithdrawAlien:
                    return FunctionType.ScriptInterface;
                case FunctionType.NumConnectedPlayers:
                    return FunctionType.ScriptInterface;
                case FunctionType.NumDeadPlayers:
                    return FunctionType.ScriptInterface;
                case FunctionType.NumPlayersOnStart:
                    return FunctionType.ScriptInterface;
                case FunctionType.PadLightBar:
                    return FunctionType.ScriptInterface;
                case FunctionType.PadRumbleImpulse:
                    return FunctionType.ScriptInterface;
                case FunctionType.ParticipatingPlayersList:
                    return FunctionType.ScriptInterface;
                case FunctionType.ParticleEmitterReference:
                    return FunctionType.SensorAttachmentInterface;
                case FunctionType.PathfindingAlienBackstageNode:
                    return FunctionType.AttachmentInterface;
                case FunctionType.PathfindingManualNode:
                    return FunctionType.CloseableInterface;
                case FunctionType.PathfindingTeleportNode:
                    return FunctionType.CloseableInterface;
                case FunctionType.PathfindingWaitNode:
                    return FunctionType.CloseableInterface;
                case FunctionType.Persistent_TriggerRandomSequence:
                    return FunctionType.ScriptInterface;
                case FunctionType.PhysicsApplyBuoyancy:
                    return FunctionType.ModifierInterface;
                case FunctionType.PhysicsApplyImpulse:
                    return FunctionType.ModifierInterface;
                case FunctionType.PhysicsApplyVelocity:
                    return FunctionType.ModifierInterface;
                case FunctionType.PhysicsModifyGravity:
                    return FunctionType.ScriptInterface;
                case FunctionType.PhysicsSystem:
                    return FunctionType.AttachmentInterface;
                case FunctionType.PickupSpawner:
                    return FunctionType.ScriptInterface;
                case FunctionType.Planet:
                    return FunctionType.TransformerInterface;
                case FunctionType.PlatformConstantBool:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlatformConstantFloat:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlatformConstantInt:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayEnvironmentAnimation:
                    return FunctionType.ScriptInterface;
                case FunctionType.Player_ExploitableArea:
                    return FunctionType.ScriptInterface;
                case FunctionType.Player_Sensor:
                    return FunctionType.SensorInterface;
                case FunctionType.PlayerCameraMonitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerCampaignDeaths:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerCampaignDeathsInARow:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerDeathCounter:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerDiscardsItems:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerDiscardsTools:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerDiscardsWeapons:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerHasEnoughItems:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerHasItem:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerHasItemEntity:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerHasItemWithName:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerHasSpaceForItem:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerKilledAllyMonitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.PlayerLightProbe:
                    return FunctionType.SensorInterface;
                case FunctionType.PlayerTorch:
                    return FunctionType.SensorInterface;
                case FunctionType.PlayerTriggerBox:
                    return FunctionType.AttachmentInterface;
                case FunctionType.PlayerUseTriggerBox:
                    return FunctionType.AttachmentInterface;
                case FunctionType.PlayerWeaponMonitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.PointAt:
                    return FunctionType.ScriptInterface;
                case FunctionType.PointTracker:
                    return FunctionType.SensorInterface;
                case FunctionType.PopupMessage:
                    return FunctionType.SensorInterface;
                case FunctionType.PositionDistance:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.PositionMarker:
                    return FunctionType.AttachmentInterface;
                case FunctionType.PostprocessingSettings:
                    return FunctionType.TransformerInterface;
                case FunctionType.ProjectileMotion:
                    return FunctionType.TransformerInterface;
                case FunctionType.ProjectileMotionComplex:
                    return FunctionType.TransformerInterface;
                case FunctionType.ProjectiveDecal:
                    return FunctionType.Box;
                case FunctionType.ProximityDetector:
                    return FunctionType.ScriptInterface;
                case FunctionType.ProximityTrigger:
                    return FunctionType.AttachmentInterface;
                case FunctionType.ProxyInterface:
                    return FunctionType.ScriptInterface;
                case FunctionType.QueryGCItemPool:
                    return FunctionType.ScriptInterface;
                case FunctionType.RadiosityIsland:
                    return FunctionType.ScriptInterface;
                case FunctionType.RadiosityProxy:
                    return FunctionType.ScriptInterface;
                case FunctionType.RandomBool:
                    return FunctionType.ScriptInterface;
                case FunctionType.RandomFloat:
                    return FunctionType.ScriptInterface;
                case FunctionType.RandomInt:
                    return FunctionType.ScriptInterface;
                case FunctionType.RandomObjectSelector:
                    return FunctionType.ScriptInterface;
                case FunctionType.RandomSelect:
                    return FunctionType.ScriptInterface;
                case FunctionType.RandomVector:
                    return FunctionType.ScriptInterface;
                case FunctionType.Raycast:
                    return FunctionType.SensorInterface;
                case FunctionType.Refraction:
                    return FunctionType.TransformerInterface;
                case FunctionType.RegisterCharacterModel:
                    return FunctionType.ScriptInterface;
                case FunctionType.RemoveFromGCItemPool:
                    return FunctionType.ScriptInterface;
                case FunctionType.RemoveFromInventory:
                    return FunctionType.ScriptInterface;
                case FunctionType.RemoveWeaponsFromPlayer:
                    return FunctionType.ScriptInterface;
                case FunctionType.RespawnConfig:
                    return FunctionType.ScriptInterface;
                case FunctionType.RespawnExcluder:
                    return FunctionType.ScriptInterface;
                case FunctionType.ReTransformer:
                    return FunctionType.AttachmentInterface;
                case FunctionType.Rewire:
                    return FunctionType.ScriptInterface;
                case FunctionType.RewireAccess_Point:
                    return FunctionType.ScriptInterface;
                case FunctionType.RewireLocation:
                    return FunctionType.ScriptInterface;
                case FunctionType.RewireSystem:
                    return FunctionType.ScriptInterface;
                case FunctionType.RewireTotalPowerResource:
                    return FunctionType.ScriptInterface;
                case FunctionType.RibbonEmitterReference:
                    return FunctionType.SensorAttachmentInterface;
                case FunctionType.RotateAtSpeed:
                    return FunctionType.TransformerInterface;
                case FunctionType.RotateInTime:
                    return FunctionType.TransformerInterface;
                case FunctionType.RTT_MoviePlayer:
                    return FunctionType.ScriptInterface;
                case FunctionType.SaveGlobalProgression:
                    return FunctionType.ScriptInterface;
                case FunctionType.SaveManagers:
                    return FunctionType.ScriptInterface;
                case FunctionType.ScalarProduct:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.ScreenEffectEventMonitor:
                    return FunctionType.ScriptInterface;
                case FunctionType.ScreenFadeIn:
                    return FunctionType.TransformerInterface;
                case FunctionType.ScreenFadeInTimed:
                    return FunctionType.TransformerInterface;
                case FunctionType.ScreenFadeOutToBlack:
                    return FunctionType.TransformerInterface;
                case FunctionType.ScreenFadeOutToBlackTimed:
                    return FunctionType.TransformerInterface;
                case FunctionType.ScreenFadeOutToWhite:
                    return FunctionType.TransformerInterface;
                case FunctionType.ScreenFadeOutToWhiteTimed:
                    return FunctionType.TransformerInterface;
                case FunctionType.ScriptVariable:
                    return FunctionType.ScriptInterface;
                case FunctionType.SensorAttachmentInterface:
                    return FunctionType.AttachmentInterface;
                case FunctionType.SensorInterface:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetAsActiveMissionLevel:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetBlueprintInfo:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetBool:
                    return FunctionType.BooleanLogicOperation;
                case FunctionType.SetColour:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.SetEnum:
                    return FunctionType.ModifierInterface;
                case FunctionType.SetEnumString:
                    return FunctionType.SetString;
                case FunctionType.SetFloat:
                    return FunctionType.FloatOperation;
                case FunctionType.SetGamepadAxes:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetGameplayTips:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetGatingToolLevel:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetHackingToolLevel:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetInteger:
                    return FunctionType.IntegerOperation;
                case FunctionType.SetLocationAndOrientation:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetMotionTrackerRange:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetNextLoadingMovie:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetObject:
                    return FunctionType.ModifierInterface;
                case FunctionType.SetObjectiveCompleted:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetPlayerHasGatingTool:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetPlayerHasKeycard:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetPosition:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.SetPrimaryObjective:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetRichPresence:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetString:
                    return FunctionType.ModifierInterface;
                case FunctionType.SetSubObjective:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetupGCDistribution:
                    return FunctionType.ScriptInterface;
                case FunctionType.SetVector:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.SetVector2:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.SharpnessSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.Showlevel_Completed:
                    return FunctionType.ScriptInterface;
                case FunctionType.SimpleRefraction:
                    return FunctionType.Box;
                case FunctionType.SimpleWater:
                    return FunctionType.Box;
                case FunctionType.SmokeCylinder:
                    return FunctionType.SensorInterface;
                case FunctionType.SmokeCylinderAttachmentInterface:
                    return FunctionType.SensorAttachmentInterface;
                case FunctionType.SmoothMove:
                    return FunctionType.TransformerInterface;
                case FunctionType.Sound:
                    return FunctionType.SoundPlaybackBaseClass;
                case FunctionType.SoundBarrier:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundEnvironmentMarker:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundEnvironmentZone:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundImpact:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundLevelInitialiser:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundLoadBank:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundLoadSlot:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundMissionInitialiser:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundNetworkNode:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundObject:
                    return FunctionType.AttachmentInterface;
                case FunctionType.SoundPhysicsInitialiser:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundPlaybackBaseClass:
                    return FunctionType.SensorAttachmentInterface;
                case FunctionType.SoundPlayerFootwearOverride:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundRTPCController:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundSetRTPC:
                    return FunctionType.SensorInterface;
                case FunctionType.SoundSetState:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundSetSwitch:
                    return FunctionType.ScriptInterface;
                case FunctionType.SoundSpline:
                    return FunctionType.SoundPlaybackBaseClass;
                case FunctionType.SoundTimelineTrigger:
                    return FunctionType.ScriptInterface;
                case FunctionType.SpaceSuitVisor:
                    return FunctionType.TransformerInterface;
                case FunctionType.SpaceTransform:
                    return FunctionType.SensorInterface;
                case FunctionType.SpawnGroup:
                    return FunctionType.ScriptInterface;
                case FunctionType.Speech:
                    return FunctionType.SoundPlaybackBaseClass;
                case FunctionType.SpeechScript:
                    return FunctionType.TransformerInterface;
                case FunctionType.Sphere:
                    return FunctionType.AttachmentInterface;
                case FunctionType.SplineDistanceLerp:
                    return FunctionType.TransformerInterface;
                case FunctionType.SplinePath:
                    return FunctionType.AttachmentInterface;
                case FunctionType.SpottingExclusionArea:
                    return FunctionType.ScriptInterface;
                case FunctionType.Squad_SetMaxEscalationLevel:
                    return FunctionType.ScriptInterface;
                case FunctionType.StartNewChapter:
                    return FunctionType.ScriptInterface;
                case FunctionType.StateQuery:
                    return FunctionType.InspectorInterface;
                case FunctionType.StreamingMonitor:
                    return FunctionType.SensorInterface;
                case FunctionType.SurfaceEffectBox:
                    return FunctionType.Box;
                case FunctionType.SurfaceEffectSphere:
                    return FunctionType.Sphere;
                case FunctionType.SwitchLevel:
                    return FunctionType.ScriptInterface;
                case FunctionType.SyncOnAllPlayers:
                    return FunctionType.ScriptInterface;
                case FunctionType.SyncOnFirstPlayer:
                    return FunctionType.ScriptInterface;
                case FunctionType.Task:
                    return FunctionType.ScriptInterface;
                case FunctionType.TerminalContent:
                    return FunctionType.ScriptInterface;
                case FunctionType.TerminalFolder:
                    return FunctionType.ScriptInterface;
                case FunctionType.Thinker:
                    return FunctionType.SensorInterface;
                case FunctionType.ThinkOnce:
                    return FunctionType.SensorInterface;
                case FunctionType.ThrowingPointOfImpact:
                    return FunctionType.SensorInterface;
                case FunctionType.ToggleFunctionality:
                    return FunctionType.ScriptInterface;
                case FunctionType.TorchDynamicMovement:
                    return FunctionType.ScriptInterface;
                case FunctionType.TransformerInterface:
                    return FunctionType.SensorInterface;
                case FunctionType.TRAV_1ShotClimbUnder:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_1ShotFloorVentExit:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_1ShotLeap:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_1ShotSpline:
                    return FunctionType.CloseableInterface;
                case FunctionType.TRAV_1ShotVentEntrance:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_1ShotVentExit:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_ContinuousBalanceBeam:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_ContinuousCinematicSidle:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_ContinuousClimbingWall:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_ContinuousLadder:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_ContinuousLedge:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_ContinuousPipe:
                    return FunctionType.ScriptInterface;
                case FunctionType.TRAV_ContinuousTightGap:
                    return FunctionType.ScriptInterface;
                case FunctionType.Trigger_AudioOccluded:
                    return FunctionType.AttachmentInterface;
                case FunctionType.TriggerBindAllCharactersOfType:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerBindAllNPCs:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerBindCharacter:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerBindCharactersInSquad:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerCameraViewCone:
                    return FunctionType.SensorInterface;
                case FunctionType.TriggerCameraViewConeMulti:
                    return FunctionType.SensorInterface;
                case FunctionType.TriggerCameraVolume:
                    return FunctionType.SensorAttachmentInterface;
                case FunctionType.TriggerCheckDifficulty:
                    return FunctionType.ModifierInterface;
                case FunctionType.TriggerContainerObjectsFilterCounter:
                    return FunctionType.InspectorInterface;
                case FunctionType.TriggerDamaged:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerDelay:
                    return FunctionType.ModifierInterface;
                case FunctionType.TriggerExtractBoundCharacter:
                    return FunctionType.ModifierInterface;
                case FunctionType.TriggerExtractBoundObject:
                    return FunctionType.ModifierInterface;
                case FunctionType.TriggerFilter:
                    return FunctionType.ModifierInterface;
                case FunctionType.TriggerLooper:
                    return FunctionType.TransformerInterface;
                case FunctionType.TriggerObjectsFilter:
                    return FunctionType.ModifierInterface;
                case FunctionType.TriggerObjectsFilterCounter:
                    return FunctionType.InspectorInterface;
                case FunctionType.TriggerRandom:
                    return FunctionType.ModifierInterface;
                case FunctionType.TriggerRandomSequence:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerSelect:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerSelect_Direct:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerSequence:
                    return FunctionType.AttachmentInterface;
                case FunctionType.TriggerSimple:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.TriggerSwitch:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerSync:
                    return FunctionType.ModifierInterface;
                case FunctionType.TriggerTouch:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerUnbindCharacter:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerViewCone:
                    return FunctionType.SensorInterface;
                case FunctionType.TriggerVolumeFilter:
                    return FunctionType.InspectorInterface;
                case FunctionType.TriggerVolumeFilter_Monitored:
                    return FunctionType.InspectorInterface;
                case FunctionType.TriggerWeightedRandom:
                    return FunctionType.ScriptInterface;
                case FunctionType.TriggerWhenSeeTarget:
                    return FunctionType.Filter;
                case FunctionType.TutorialMessage:
                    return FunctionType.SensorInterface;
                case FunctionType.UI_Attached:
                    return FunctionType.TransformerInterface;
                case FunctionType.UI_Container:
                    return FunctionType.UI_Attached;
                case FunctionType.UI_Icon:
                    return FunctionType.AttachmentInterface;
                case FunctionType.UI_KeyGate:
                    return FunctionType.ScriptInterface;
                case FunctionType.UI_Keypad:
                    return FunctionType.UI_Attached;
                case FunctionType.UI_ReactionGame:
                    return FunctionType.UI_Attached;
                case FunctionType.UIBreathingGameIcon:
                    return FunctionType.ScriptInterface;
                case FunctionType.UiSelectionBox:
                    return FunctionType.Box;
                case FunctionType.UiSelectionSphere:
                    return FunctionType.Sphere;
                case FunctionType.UnlockAchievement:
                    return FunctionType.ScriptInterface;
                case FunctionType.UnlockLogEntry:
                    return FunctionType.ScriptInterface;
                case FunctionType.UnlockMapDetail:
                    return FunctionType.ScriptInterface;
                case FunctionType.UpdateGlobalPosition:
                    return FunctionType.ScriptInterface;
                case FunctionType.UpdateLeaderBoardDisplay:
                    return FunctionType.ScriptInterface;
                case FunctionType.UpdatePrimaryObjective:
                    return FunctionType.ScriptInterface;
                case FunctionType.UpdateSubObjective:
                    return FunctionType.ScriptInterface;
                case FunctionType.VariableAnimationInfo:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableBool:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableColour:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableEnum:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableEnumString:
                    return FunctionType.VariableString;
                case FunctionType.VariableFilterObject:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableFlashScreenColour:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableFloat:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableHackingConfig:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableInt:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableObject:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariablePosition:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableString:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableThePlayer:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableTriggerObject:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableVector:
                    return FunctionType.ScriptVariable;
                case FunctionType.VariableVector2:
                    return FunctionType.ScriptVariable;
                case FunctionType.VectorAdd:
                    return FunctionType.VectorMath;
                case FunctionType.VectorDirection:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorDistance:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorLinearInterpolateSpeed:
                    return FunctionType.TransformerInterface;
                case FunctionType.VectorLinearInterpolateTimed:
                    return FunctionType.TransformerInterface;
                case FunctionType.VectorLinearProportion:
                    return FunctionType.ScriptInterface;
                case FunctionType.VectorMath:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorModulus:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorMultiply:
                    return FunctionType.VectorMath;
                case FunctionType.VectorMultiplyByPos:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorNormalise:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorProduct:
                    return FunctionType.VectorMath;
                case FunctionType.VectorReflect:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorRotateByPos:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorRotatePitch:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorRotateRoll:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorRotateYaw:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorScale:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VectorSubtract:
                    return FunctionType.VectorMath;
                case FunctionType.VectorYaw:
                    return FunctionType.EvaluatorInterface;
                case FunctionType.VideoCapture:
                    return FunctionType.TransformerInterface;
                case FunctionType.VignetteSettings:
                    return FunctionType.PostprocessingSettings;
                case FunctionType.VisibilityMaster:
                    return FunctionType.ScriptInterface;
                case FunctionType.Weapon_AINotifier:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_AmmoTypeFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_AttackerFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_DamageFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_DidHitSomethingFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_Effect:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_GiveToCharacter:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_GiveToPlayer:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_ImpactAngleFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_ImpactCharacterFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_ImpactEffect:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_ImpactFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_ImpactInspector:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_ImpactOrientationFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_MultiFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.WEAPON_TargetObjectFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.Zone:
                    return FunctionType.ZoneInterface;
                case FunctionType.ZoneExclusionLink:
                    return FunctionType.ScriptInterface;
                case FunctionType.ZoneInterface:
                    return FunctionType.ScriptInterface;
                case FunctionType.ZoneLink:
                    return FunctionType.GateInterface;
                case FunctionType.ZoneLoaded:
                    return FunctionType.ScriptInterface;
            }
            return null;
        }

        /* Pull non-vanilla entity names from the CommandsPAK */
        private static void LoadCustomNames(string filepath)
        {
            _custom = (EntityNameTable)CustomTable.ReadTable(filepath, CustomEndTables.ENTITY_NAMES);
            if (_custom == null) _custom = new EntityNameTable();
            Console.WriteLine("Loaded " + _custom.names.Count + " custom entity names!");
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private static void SaveCustomNames(string filepath)
        {
            CustomTable.WriteTable(filepath, CustomEndTables.ENTITY_NAMES, _custom);
            Console.WriteLine("Saved " + _custom.names.Count + " custom entity names!");
        }

        /* Applies all default parameters to a Function entity for the given type - if overwrite is set to true, it will apply defaults over those that already exist */
        private static void ApplyDefaultsForFunction(Entity entity, FunctionType type, ParameterVariant variants, bool overwrite)
        {
            if (variants.HasFlag(ParameterVariant.REFERENCE_PIN))
                ApplyDefaultReferencePinForFunction(entity, type, overwrite);
            if (variants.HasFlag(ParameterVariant.TARGET_PIN))
                ApplyDefaultTargetPinForFunction(entity, type, overwrite);
            if (variants.HasFlag(ParameterVariant.STATE_PARAMETER))
                ApplyDefaultStateParameterForFunction(entity, type, overwrite);
            if (variants.HasFlag(ParameterVariant.INPUT_PIN))
                ApplyDefaultInputPinForFunction(entity, type, overwrite);
            if (variants.HasFlag(ParameterVariant.OUTPUT_PIN))
                ApplyDefaultOutputPinForFunction(entity, type, overwrite);
            if (variants.HasFlag(ParameterVariant.PARAMETER))
                ApplyDefaultParameterForFunction(entity, type, overwrite);
            if (variants.HasFlag(ParameterVariant.INTERNAL))
                ApplyDefaultInternalForFunction(entity, type, overwrite);
            if (variants.HasFlag(ParameterVariant.METHOD_FUNCTION))
                ApplyDefaultMethodFunctionForFunction(entity, type, overwrite);
            if (variants.HasFlag(ParameterVariant.METHOD_PIN))
                ApplyDefaultMethodPinForFunction(entity, type, overwrite);
        }
        #region APPLY_DEFAULTS
        private static void ApplyDefaultReferencePinForFunction(Entity entity, FunctionType type, bool overwrite)
        {
            switch (type)
            {
                case FunctionType.ScriptInterface:
                    entity.AddParameter("reference", new cFloat(), ParameterVariant.REFERENCE_PIN, overwrite);
                    break;
                case FunctionType.CameraFinder:
                    entity.AddParameter("reference", new cFloat(), ParameterVariant.REFERENCE_PIN, overwrite);
                    break;
                case FunctionType.PlayerCamera:
                    entity.AddParameter("reference", new cFloat(), ParameterVariant.REFERENCE_PIN, overwrite);
                    break;
                case FunctionType.CameraBehaviorInterface:
                    entity.AddParameter("reference", new cFloat(), ParameterVariant.REFERENCE_PIN, overwrite);
                    break;
                case FunctionType.CameraPath:
                    entity.AddParameter("reference", new cFloat(), ParameterVariant.REFERENCE_PIN, overwrite);
                    break;
            }
        }
        private static void ApplyDefaultTargetPinForFunction(Entity entity, FunctionType type, bool overwrite)
        {
            switch (type)
            {
                case FunctionType.ScriptVariable:
                    entity.AddParameter("on_changed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_restored", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ZoneInterface:
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_unloaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_streaming", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Box:
                    entity.AddParameter("event", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ButtonMashPrompt:
                    entity.AddParameter("on_back_to_zero", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_degrade", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_mashed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.GetFlashIntValue:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.GetFlashFloatValue:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Sphere:
                    entity.AddParameter("event", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ImpactSphere:
                    entity.AddParameter("event", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CollisionBarrier:
                    entity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PlayerTriggerBox:
                    entity.AddParameter("on_entered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_exited", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PlayerUseTriggerBox:
                    entity.AddParameter("on_entered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_exited", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_use", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ModelReference:
                    entity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CameraResource:
                    entity.AddParameter("on_enter_transition_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_exit_transition_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.StealCamera:
                    entity.AddParameter("on_converged", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CameraPlayAnimation:
                    entity.AddParameter("on_animation_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CharacterCommand:
                    entity.AddParameter("command_started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_Follow:
                    entity.AddParameter("entered_inner_radius", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exitted_outer_radius", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_FollowUsingJobs:
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_PlayAnimation:
                    entity.AddParameter("Interrupted", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("badInterrupted", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_Idle:
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("interrupted", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_GoTo:
                    entity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_GoToCover:
                    entity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("entered_cover", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_MoveTowards:
                    entity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_LaunchMeleeAttack:
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_HolsterWeapon:
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_ForceReloadWeapon:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_PlaySecondaryAnimation:
                    entity.AddParameter("Interrupted", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_ShootAt:
                    entity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_AimAtCurrentTarget:
                    entity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_AimAt:
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Player_Sensor:
                    entity.AddParameter("Standard", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Running", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Aiming", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Vent", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Grapple", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Death", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Cover", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Motion_Tracked", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Motion_Tracked_Vent", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Leaning", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CMD_Ragdoll:
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetAndroidThrowTarget:
                    entity.AddParameter("thrown", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_DamageMonitor:
                    entity.AddParameter("damaged", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_KnockedOutMonitor:
                    entity.AddParameter("on_knocked_out", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_recovered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_DeathMonitor:
                    entity.AddParameter("dying", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("killed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_RetreatMonitor:
                    entity.AddParameter("reached_retreat", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("started_retreating", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_WeaponFireMonitor:
                    entity.AddParameter("fired", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_TorchMonitor:
                    entity.AddParameter("torch_on", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("torch_off", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_VentMonitor:
                    entity.AddParameter("entered_vent", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exited_vent", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CharacterTypeMonitor:
                    entity.AddParameter("spawned", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("despawned", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("all_despawned", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Convo:
                    entity.AddParameter("everyoneArrived", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("playerJoined", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("playerLeft", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("npcJoined", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_Squad_DialogueMonitor:
                    entity.AddParameter("Suspicious_Item_Initial", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Suspicious_Item_Close", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Suspicious_Warning", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Suspicious_Warning_Fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Missing_Buddy", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Search_Started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Search_Loop", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Search_Complete", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Detected_Enemy", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Alien_Heard_Backstage", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Interrogative", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Warning", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Last_Chance", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Stand_Down", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Attack", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Advance", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Melee", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Hit_By_Weapon", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Go_to_Cover", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("No_Cover", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Shoot_From_Cover", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Cover_Broken", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Retreat", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Panic", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Final_Hit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Ally_Death", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Incoming_IED", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Alert_Squad", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("My_Death", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Idle_Passive", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Idle_Aggressive", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Block", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Enter_Grapple", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Grapple_From_Cover", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Player_Observed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_Group_DeathCounter:
                    entity.AddParameter("on_threshold", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_Group_Death_Monitor:
                    entity.AddParameter("last_man_dying", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("all_killed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_GetLastSensedPositionOfTarget:
                    entity.AddParameter("NoRecentSense", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SensedOnLeft", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SensedOnRight", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SensedInFront", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SensedBehind", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_Aggression_Monitor:
                    entity.AddParameter("on_interrogative", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_warning", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_last_chance", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_stand_down", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_idle", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_aggressive", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Explosion_AINotifier:
                    entity.AddParameter("on_character_damage_fx", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_Sleeping_Android_Monitor:
                    entity.AddParameter("Twitch", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SitUp_Start", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SitUp_End", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Sleeping_GetUp", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Sitting_GetUp", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_Highest_Awareness_Monitor:
                    entity.AddParameter("All_Dead", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Stunned", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Unaware", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Suspicious", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SearchingArea", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SearchingLastSensed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Aware", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_changed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_Squad_GetAwarenessState:
                    entity.AddParameter("All_Dead", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Stunned", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Unaware", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Suspicious", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SearchingArea", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SearchingLastSensed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Aware", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_Squad_GetAwarenessWatermark:
                    entity.AddParameter("All_Dead", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Stunned", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Unaware", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Suspicious", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SearchingArea", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("SearchingLastSensed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Aware", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PlayerCameraMonitor:
                    entity.AddParameter("AndroidNeckSnap", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("AlienKill", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("AlienKillBroken", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("AlienKillInVent", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("StandardAnimDrivenView", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("StopNonStandardCameras", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ScreenEffectEventMonitor:
                    entity.AddParameter("MeleeHit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("BulletHit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("MedkitHeal", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("StartStrangle", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("StopStrangle", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("StartLowHealth", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("StopLowHealth", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("StartDeath", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("StopDeath", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("AcidHit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("FlashbangHit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("HitAndRun", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("CancelHitAndRun", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.DEBUG_SenseLevels:
                    entity.AddParameter("no_activation", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("trace_activation", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("lower_activation", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("normal_activation", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("upper_activation", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_TargetAcquire:
                    entity.AddParameter("no_targets", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_IsWithinRange:
                    entity.AddParameter("In_range", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Out_of_range", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_GetTorch:
                    entity.AddParameter("torch_on", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("torch_off", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_GetCombatTarget:
                    entity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_behaviour_monitor:
                    entity.AddParameter("state_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("state_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_multi_behaviour_monitor:
                    entity.AddParameter("Cinematic_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Cinematic_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Damage_Response_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Damage_Response_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Target_Is_NPC_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Target_Is_NPC_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Breakout_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Breakout_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Attack_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Attack_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Stunned_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Stunned_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Backstage_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Backstage_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("In_Vent_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("In_Vent_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Killtrap_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Killtrap_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Threat_Aware_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Threat_Aware_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Suspect_Target_Response_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Suspect_Target_Response_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Player_Hiding_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Player_Hiding_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Suspicious_Item_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Suspicious_Item_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Search_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Search_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Area_Sweep_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Area_Sweep_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_ambush_monitor:
                    entity.AddParameter("setup", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("abandoned", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("trap_sprung", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_navmesh_type_monitor:
                    entity.AddParameter("state_set", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("state_unset", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_HasWeaponOfType:
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_TriggerAimRequest:
                    entity.AddParameter("started_aiming", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished_aiming", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("interrupted", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_TriggerShootRequest:
                    entity.AddParameter("started_shooting", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished_shooting", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("interrupted", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_Once:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Custom_Hiding_Vignette_controller:
                    entity.AddParameter("StartFade", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("StopFade", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Custom_Hiding_Controller:
                    entity.AddParameter("Started_Idle", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Started_Exit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Got_Out", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Prompt_1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Prompt_2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Start_choking", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Start_oxygen_starvation", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Show_MT", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Hide_MT", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Spawn_MT", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Despawn_MT", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Start_Busted_By_Alien", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Start_Busted_By_Android", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("End_Busted_By_Android", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Start_Busted_By_Human", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("End_Busted_By_Human", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.EQUIPPABLE_ITEM:
                    entity.AddParameter("finished_spawning", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("equipped", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("unequipped", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pickup", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_discard", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_melee_impact", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_used_basic_function", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.AIMED_ITEM:
                    entity.AddParameter("on_started_aiming", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_stopped_aiming", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_display_on", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_display_off", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_effect_on", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_effect_off", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.AIMED_WEAPON:
                    entity.AddParameter("on_fired_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_fired_fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_fired_fail_single", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_impact", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_reload_started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_reload_another", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_reload_empty_clip", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_reload_canceled", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_reload_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_reload_fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_shooting_started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_shooting_wind_down", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_shooting_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_overheated", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_cooled_down", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_charge_complete", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_charge_started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_charge_stopped", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_turned_on", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_turned_off", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_torch_on_requested", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_torch_off_requested", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PlayerWeaponMonitor:
                    entity.AddParameter("on_clip_above_percentage", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_clip_below_percentage", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_clip_empty", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_clip_full", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_AttackerFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_TargetObjectFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_DamageFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_DidHitSomethingFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_MultiFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactCharacterFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_AmmoTypeFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactAngleFilter:
                    entity.AddParameter("greater", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("less", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactOrientationFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.EFFECT_ImpactGenerator:
                    entity.AddParameter("on_impact", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_failed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ZoneLoaded:
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_unloaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.StateQuery:
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.BooleanLogicInterface:
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.LogicOnce:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.LogicDelay:
                    entity.AddParameter("on_delay_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.LogicSwitch:
                    entity.AddParameter("true_now_false", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("false_now_true", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_restored_true", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_restored_false", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.LogicGate:
                    entity.AddParameter("on_allowed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_disallowed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.FloatCompare:
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.FloatModulate:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.FloatModulateRandom:
                    entity.AddParameter("on_full_switched_on", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_full_switched_off", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.FloatLinearInterpolateTimed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.FloatLinearInterpolateSpeed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.FloatLinearInterpolateSpeedAdvanced:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("trigger_on_min", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("trigger_on_max", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("trigger_on_loop", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.IntegerCompare:
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.VectorLinearInterpolateTimed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.VectorLinearInterpolateSpeed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.MoveInTime:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.SmoothMove:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.RotateInTime:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.RotateAtSpeed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerRandom:
                    entity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_11", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_12", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerRandomSequence:
                    entity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("All_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Persistent_TriggerRandomSequence:
                    entity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("All_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerWeightedRandom:
                    entity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PlayEnvironmentAnimation:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_finished_streaming", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CAGEAnimation:
                    entity.AddParameter("animation_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("animation_interrupted", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("animation_changed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("cinematic_loaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("cinematic_unloaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Checkpoint:
                    entity.AddParameter("on_checkpoint", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_captured", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_saved", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished_saving", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished_loading", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("cancelled_saving", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished_saving_to_hdd", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.MissionNumber:
                    entity.AddParameter("on_changed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CheckpointRestoredNotify:
                    entity.AddParameter("restored", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.DisplayMessageWithCallbacks:
                    entity.AddParameter("on_yes", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_no", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_cancel", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.DebugCheckpoint:
                    entity.AddParameter("on_checkpoint", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.EndGame:
                    entity.AddParameter("on_game_end_started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_game_ended", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.DebugText:
                    entity.AddParameter("duration_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.DebugCaptureScreenShot:
                    entity.AddParameter("finished_capturing", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.DebugCaptureCorpse:
                    entity.AddParameter("finished_capturing", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PlayerTorch:
                    entity.AddParameter("requested_torch_holster", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("requested_torch_draw", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ThinkOnce:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Thinker:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.AllPlayersReady:
                    entity.AddParameter("on_all_players_ready", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.SyncOnAllPlayers:
                    entity.AddParameter("on_synchronized", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_synchronized_host", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.SyncOnFirstPlayer:
                    entity.AddParameter("on_synchronized", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_synchronized_host", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_synchronized_local", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NetPlayerCounter:
                    entity.AddParameter("on_full", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_empty", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_intermediate", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.BroadcastTrigger:
                    entity.AddParameter("on_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.HostOnlyTrigger:
                    entity.AddParameter("on_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.SpawnGroup:
                    entity.AddParameter("on_spawn_request", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NumConnectedPlayers:
                    entity.AddParameter("on_count_changed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NetworkedTimer:
                    entity.AddParameter("on_second_changed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_started_counting", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_finished_counting", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CompoundVolume:
                    entity.AddParameter("event", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerVolumeFilter:
                    entity.AddParameter("on_event_entered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_event_exited", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerVolumeFilter_Monitored:
                    entity.AddParameter("on_event_entered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_event_exited", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerFilter:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerObjectsFilter:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.BindObjectsMultiplexer:
                    entity.AddParameter("Pin1_Bound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin2_Bound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin3_Bound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin4_Bound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin5_Bound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin6_Bound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin7_Bound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin8_Bound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin9_Bound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin10_Bound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerObjectsFilterCounter:
                    entity.AddParameter("none_passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("some_passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("all_passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerContainerObjectsFilterCounter:
                    entity.AddParameter("none_passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("some_passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("all_passed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerTouch:
                    entity.AddParameter("touch_event", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerDamaged:
                    entity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerBindCharacter:
                    entity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerBindAllCharactersOfType:
                    entity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerBindCharactersInSquad:
                    entity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerUnbindCharacter:
                    entity.AddParameter("unbound_trigger", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerExtractBoundObject:
                    entity.AddParameter("unbound_trigger", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerExtractBoundCharacter:
                    entity.AddParameter("unbound_trigger", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerDelay:
                    entity.AddParameter("delayed_trigger", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("purged_trigger", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerSwitch:
                    entity.AddParameter("Pin_1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_10", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerSelect:
                    entity.AddParameter("Pin_0", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_10", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_11", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_12", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_13", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_14", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_15", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_16", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerSelect_Direct:
                    entity.AddParameter("Changed_to_0", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_10", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_11", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_12", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_13", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_14", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_15", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Changed_to_16", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerCheckDifficulty:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerSync:
                    entity.AddParameter("Pin1_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin2_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin3_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin4_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin5_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin6_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin7_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin8_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin9_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin10_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.LogicAll:
                    entity.AddParameter("Pin1_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin2_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin3_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin4_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin5_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin6_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin7_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin8_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin9_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin10_Synced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Logic_MultiGate:
                    entity.AddParameter("Underflow", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_10", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_11", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_12", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_13", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_14", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_15", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_16", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_17", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_18", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_19", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pin_20", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Overflow", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Counter:
                    entity.AddParameter("on_under_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_over_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.LogicCounter:
                    entity.AddParameter("on_under_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_over_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("restored_on_under_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("restored_on_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("restored_on_over_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.LogicPressurePad:
                    entity.AddParameter("Pad_Activated", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Pad_Deactivated", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("bound_characters", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.GateResourceInterface:
                    entity.AddParameter("gate_status_changed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Door:
                    entity.AddParameter("started_opening", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("started_closing", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished_opening", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished_closing", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("used_locked", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("used_unlocked", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("used_forced_open", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("used_forced_closed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("waiting_to_open", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("highlight", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("unhighlight", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.MonitorPadInput:
                    entity.AddParameter("on_pressed_A", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_A", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_B", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_B", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_X", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_X", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_Y", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_Y", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_L1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_L1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_R1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_R1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_L2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_L2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_R2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_R2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_L3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_L3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_R3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_R3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_dpad_left", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_dpad_left", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_dpad_right", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_dpad_right", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_dpad_up", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_dpad_up", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_dpad_down", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_dpad_down", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.MonitorActionMap:
                    entity.AddParameter("on_pressed_use", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_use", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_crouch", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_crouch", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_run", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_run", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_aim", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_aim", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_shoot", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_shoot", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_reload", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_reload", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_melee", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_melee", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_activate_item", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_activate_item", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_switch_weapon", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_switch_weapon", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_change_dof_focus", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_change_dof_focus", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_select_motion_tracker", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_select_motion_tracker", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_select_torch", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_select_torch", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_torch_beam", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_torch_beam", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_peek", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_peek", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pressed_back_close", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_released_back_close", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerViewCone:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("target_is_visible", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("no_target_visible", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerCameraViewCone:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerCameraViewConeMulti:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("enter1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("enter2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("enter3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("enter4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("enter5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("enter6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("enter7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("enter8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("enter9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerCameraVolume:
                    entity.AddParameter("inside", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Character:
                    entity.AddParameter("finished_spawning", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished_respawning", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("dead_container_take_slot", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("dead_container_emptied", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_ragdoll_impact", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_footstep", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_despawn_requested", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.DespawnPlayer:
                    entity.AddParameter("despawned", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.DespawnCharacter:
                    entity.AddParameter("despawned", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerWhenSeeTarget:
                    entity.AddParameter("seen", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Task:
                    entity.AddParameter("start_command", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("selected_by_npc", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("clean_up", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.IdleTask:
                    entity.AddParameter("start_pre_move", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("start_interrupt", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("interrupted_while_moving", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.NPC_ForceNextJob:
                    entity.AddParameter("job_started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("job_completed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("job_interrupted", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerBindAllNPCs:
                    entity.AddParameter("npc_inside", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("npc_outside", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Trigger_AudioOccluded:
                    entity.AddParameter("NotOccluded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Occluded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.SoundPlaybackBaseClass:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Speech:
                    entity.AddParameter("on_speech_started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.CHR_PlayNPCBark:
                    entity.AddParameter("on_speech_started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_speech_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.SpeechScript:
                    entity.AddParameter("on_script_ended", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.SoundLoadBank:
                    entity.AddParameter("bank_loaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.SoundLoadSlot:
                    entity.AddParameter("bank_loaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.MusicTrigger:
                    entity.AddParameter("on_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.AddToInventory:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.RemoveFromInventory:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PlayerHasItemEntity:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.InventoryItem:
                    entity.AddParameter("collect", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PickupSpawner:
                    entity.AddParameter("collect", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.AllocateGCItemsFromPool:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.AllocateGCItemFromPoolBySubset:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.RemoveFromGCItemPool:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.UI_KeyGate:
                    entity.AddParameter("keycard_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("keycode_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("keycard_fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("keycode_fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("keycard_cancelled", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("keycode_cancelled", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.RTT_MoviePlayer:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("end", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.MoviePlayer:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("end", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("skipped", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.FlashCallback:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PopupMessage:
                    entity.AddParameter("display", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.UI_Icon:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("start_fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("button_released", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("broadcasted_start", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("highlight", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("unhighlight", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("lock_looked_at", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("lock_interaction", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.UI_Attached:
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.UI_Container:
                    entity.AddParameter("take_slot", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("emptied", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.UI_ReactionGame:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stage0_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stage0_fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stage1_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stage1_fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stage2_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stage2_fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("resources_finished_unloading", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("resources_finished_loading", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.UI_Keypad:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.HackingGame:
                    entity.AddParameter("win", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("alarm_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("loaded_idle", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("loaded_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("resources_finished_unloading", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("resources_finished_loading", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TerminalContent:
                    entity.AddParameter("selected", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TerminalFolder:
                    entity.AddParameter("code_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("code_fail", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("selected", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.AccessTerminal:
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("all_data_has_been_read", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.GetPlayerHasGatingTool:
                    entity.AddParameter("has_tool", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("doesnt_have_tool", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.GetPlayerHasKeycard:
                    entity.AddParameter("has_card", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("doesnt_have_card", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.RewireSystem:
                    entity.AddParameter("on", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("off", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.RewireLocation:
                    entity.AddParameter("power_draw_increased", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("power_draw_reduced", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.RewireAccess_Point:
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Rewire:
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Minigames:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TriggerLooper:
                    entity.AddParameter("target", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLadder:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousPipe:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLedge:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousClimbingWall:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousCinematicSidle:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousBalanceBeam:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousTightGap:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentEntrance:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentExit:
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentExit:
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotClimbUnder:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotLeap:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnSuccess", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnFailure", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotSpline:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PathfindingTeleportNode:
                    entity.AddParameter("started_teleporting", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stopped_teleporting", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PathfindingWaitNode:
                    entity.AddParameter("character_getting_near", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("character_arriving", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("character_stopped", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("started_waiting", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stopped_waiting", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PathfindingManualNode:
                    entity.AddParameter("character_arriving", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("character_stopped", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("started_animating", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stopped_animating", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PathfindingAlienBackstageNode:
                    entity.AddParameter("started_animating_Entry", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stopped_animating_Entry", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("started_animating_Exit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("stopped_animating_Exit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("killtrap_anim_started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("killtrap_anim_stopped", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("killtrap_fx_start", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("killtrap_fx_stop", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ProjectileMotion:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ProjectileMotionComplex:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.SplineDistanceLerp:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.MoveAlongSpline:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPoint:
                    entity.AddParameter("bound_to_closest", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPointFromSet:
                    entity.AddParameter("closest_is_1", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("closest_is_2", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("closest_is_3", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("closest_is_4", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("closest_is_5", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("closest_is_6", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("closest_is_7", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("closest_is_8", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("closest_is_9", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("closest_is_10", new cTransform(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ScreenFadeOutToBlackTimed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ScreenFadeOutToWhiteTimed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ScreenFadeInTimed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.AreaHitMonitor:
                    entity.AddParameter("on_flamer_hit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_shotgun_hit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_pistol_hit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.StreamingMonitor:
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Raycast:
                    entity.AddParameter("Obstructed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Unobstructed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("OutOfRange", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.AssetSpawner:
                    entity.AddParameter("finished_spawning", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("callback_triggered", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("forced_despawn", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ProximityTrigger:
                    entity.AddParameter("ignited", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("electrified", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("drenched", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("poisoned", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ThrowingPointOfImpact:
                    entity.AddParameter("show_point_of_impact", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("hide_point_of_impact", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.MotionTrackerMonitor:
                    entity.AddParameter("on_motion_sound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_enter_range_sound", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.GlobalEventMonitor:
                    entity.AddParameter("Event_1", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_2", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_3", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_4", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_5", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_6", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_7", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_8", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_9", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_10", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_11", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_12", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_13", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_14", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_15", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_16", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_17", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_18", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_19", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("Event_20", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PlayerKilledAllyMonitor:
                    entity.AddParameter("ally_killed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.InteractiveMovementControl:
                    entity.AddParameter("completed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PlayForMinDuration:
                    entity.AddParameter("timer_expired", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("first_animation_started", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("next_animation", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("all_animations_finished", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.GCIP_WorldPickup:
                    entity.AddParameter("spawn_completed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("pickup_collected", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Torch_Control:
                    entity.AddParameter("torch_switched_off", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("torch_switched_on", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.Interaction:
                    entity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_interrupt", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("on_killed", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.PlayerDeathCounter:
                    entity.AddParameter("on_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    entity.AddParameter("above_limit", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
                case FunctionType.ProximityDetector:
                    entity.AddParameter("in_proximity", new cFloat(), ParameterVariant.TARGET_PIN, overwrite);
                    break;
            }
        }
        private static void ApplyDefaultStateParameterForFunction(Entity entity, FunctionType type, bool overwrite)
        {
            switch (type)
            {
                case FunctionType.ScriptInterface:
                    entity.AddParameter("delete_me", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.ProxyInterface:
                    entity.AddParameter("proxy_filter_targets", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("proxy_enable_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.SensorInterface:
                    entity.AddParameter("start_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.CloseableInterface:
                    entity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.GateInterface:
                    entity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("lock_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.AttachmentInterface:
                    entity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.SensorAttachmentInterface:
                    entity.AddParameter("start_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.CompositeInterface:
                    entity.AddParameter("is_template", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("local_only", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("suspend_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("is_shared", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("requires_script_for_current_gen", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("requires_script_for_next_gen", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("convert_to_physics", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("delete_standard_collision", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("delete_ballistic_collision", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.Box:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.GetFlashIntValue:
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.GetFlashFloatValue:
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.Sphere:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.CollisionBarrier:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerTriggerBox:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerUseTriggerBox:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.ModelReference:
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("simulate_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("convert_to_physics", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.LightReference:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.ParticleEmitterReference:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.RibbonEmitterReference:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.GPU_PFXEmitterReference:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.FogSphere:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.FogBox:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.SurfaceEffectSphere:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.SurfaceEffectBox:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.SimpleWater:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.SimpleRefraction:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.ProjectiveDecal:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.LightingMaster:
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.CameraResource:
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.CameraBehaviorInterface:
                    entity.AddParameter("start_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.FollowCameraModifier:
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.CameraAimAssistant:
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TorchDynamicMovement:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.EQUIPPABLE_ITEM:
                    entity.AddParameter("spawn_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.VariableFlashScreenColour:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.PlayEnvironmentAnimation:
                    entity.AddParameter("play_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("jump_to_the_end_on_play", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.CAGEAnimation:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerSequence:
                    entity.AddParameter("proxy_enable_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("attach_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerTorch:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.Master:
                    entity.AddParameter("suspend_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.ThinkOnce:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.AllPlayersReady:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerTouch:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerDamaged:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.Character:
                    entity.AddParameter("spawn_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.Job:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.Task:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.LimitItemUse:
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.PickupSpawner:
                    entity.AddParameter("spawn_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.FlashScript:
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.UI_KeyGate:
                    entity.AddParameter("lock_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.RTT_MoviePlayer:
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.UI_Icon:
                    entity.AddParameter("lock_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("show_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.HackingGame:
                    entity.AddParameter("lock_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TerminalFolder:
                    entity.AddParameter("lock_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.AccessTerminal:
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.MapItem:
                    entity.AddParameter("show_ui_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.CoverLine:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLadder:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousPipe:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLedge:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousCinematicSidle:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousBalanceBeam:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousTightGap:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentEntrance:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentExit:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentExit:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotClimbUnder:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotLeap:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotSpline:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    entity.AddParameter("open_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.NavMeshBarrier:
                    entity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.PathfindingAlienBackstageNode:
                    entity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.PhysicsModifyGravity:
                    entity.AddParameter("float_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.AssetSpawner:
                    entity.AddParameter("spawn_on_reset", new cBool(false), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.CharacterAttachmentNode:
                    entity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.MultipleCharacterAttachmentNode:
                    entity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.AnimatedModelAttachmentNode:
                    entity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.Force_UI_Visibility:
                    entity.AddParameter("also_disable_interactions", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerKilledAllyMonitor:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE_PARAMETER, overwrite);
                    break;
            }
        }
        private static void ApplyDefaultInputPinForFunction(Entity entity, FunctionType type, bool overwrite)
        {
            switch (type)
            {
                case FunctionType.AttachmentInterface:
                    entity.AddParameter("attachment", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.LightReference:
                    entity.AddParameter("occlusion_geometry", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("mastered_by_visibility", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("exclude_shadow_entities", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ParticleEmitterReference:
                    entity.AddParameter("mastered_by_visibility", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RibbonEmitterReference:
                    entity.AddParameter("mastered_by_visibility", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GPU_PFXEmitterReference:
                    entity.AddParameter("mastered_by_visibility", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.LODControls:
                    entity.AddParameter("lod_range_scalar", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("disable_lods", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.LightingMaster:
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.DebugCamera:
                    entity.AddParameter("linked_cameras", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CameraBehaviorInterface:
                    entity.AddParameter("linked_cameras", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CAMERA_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CameraShake:
                    entity.AddParameter("relative_transformation", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("impulse_intensity", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("impulse_position", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CameraPathDriven:
                    entity.AddParameter("position_path", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target_path", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("reference_path", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("position_path_transform", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target_path_transform", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("reference_path_transform", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("point_to_project", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.BoneAttachedCamera:
                    entity.AddParameter("character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.StealCamera:
                    entity.AddParameter("focus_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FollowCameraModifier:
                    entity.AddParameter("position_curve", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target_curve", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CameraPath:
                    entity.AddParameter("linked_splines", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CameraPlayAnimation:
                    entity.AddParameter("animated_camera", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("position_marker", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("character_to_focus", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("focal_length_mm", new cFloat(75.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("focal_plane_m", new cFloat(2.5f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("fnum", new cFloat(2.8f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CameraDofController:
                    entity.AddParameter("character_to_focus", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("focal_length_mm", new cFloat(75.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("focal_plane_m", new cFloat(2.5f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("fnum", new cFloat(2.8f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ClipPlanesController:
                    entity.AddParameter("near_plane", new cFloat(0.1f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("far_plane", new cFloat(1000.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("update_near", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("update_far", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Logic_Vent_Entrance:
                    entity.AddParameter("Hide_Pos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Emit_Pos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Logic_Vent_System:
                    entity.AddParameter("Vent_Entrances", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.VENT_ENTRANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_Follow:
                    entity.AddParameter("Waypoint", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_FollowUsingJobs:
                    entity.AddParameter("target_to_follow", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("who_Im_leading", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_FollowOffset:
                    entity.AddParameter("offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target_to_follow", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_PlayAnimation:
                    entity.AddParameter("SafePos", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Marker", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("ExitPosition", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("ExternalStartTime", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("ExternalTime", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("OverrideCharacter", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("OptionalMask", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_Idle:
                    entity.AddParameter("target_to_face", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_GoTo:
                    entity.AddParameter("Waypoint", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AimTarget", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_GoToCover:
                    entity.AddParameter("CoverPoint", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AimTarget", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_MoveTowards:
                    entity.AddParameter("MoveTarget", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AimTarget", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_Die:
                    entity.AddParameter("Killer", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_PlaySecondaryAnimation:
                    entity.AddParameter("Marker", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("OptionalMask", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("ExternalStartTime", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("ExternalTime", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_ShootAt:
                    entity.AddParameter("Target", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_AimAt:
                    entity.AddParameter("AimTarget", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_Ragdoll:
                    entity.AddParameter("actor", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("impact_velocity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetTacticalPosition:
                    entity.AddParameter("tactical_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetFocalPoint:
                    entity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetAndroidThrowTarget:
                    entity.AddParameter("throw_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_DamageMonitor:
                    entity.AddParameter("InstigatorFilter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_DeathMonitor:
                    entity.AddParameter("KillerFilter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Convo:
                    entity.AddParameter("members", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.LOGIC_CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_GetLastSensedPositionOfTarget:
                    entity.AddParameter("OptionalTarget", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.HeldItem_AINotifier:
                    entity.AddParameter("Item", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Duration", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_Gain_Aggression_In_Radius:
                    entity.AddParameter("Position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Radius", new cFloat(5.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_Sleeping_Android_Monitor:
                    entity.AddParameter("Android_NPC", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_Highest_Awareness_Monitor:
                    entity.AddParameter("NPC_Coordinator", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Target", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_Squad_GetAwarenessState:
                    entity.AddParameter("NPC_Coordinator", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_Squad_GetAwarenessWatermark:
                    entity.AddParameter("NPC_Coordinator", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_FakeSense:
                    entity.AddParameter("SensedObject", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("FakePosition", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_SuspiciousItem:
                    entity.AddParameter("ItemPosition", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_IsWithinRange:
                    entity.AddParameter("Position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Radius", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Height", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_ForceCombatTarget:
                    entity.AddParameter("Target", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetAimTarget:
                    entity.AddParameter("Target", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_MeleeContext:
                    entity.AddParameter("ConvergePos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Radius", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetSafePoint:
                    entity.AddParameter("SafePositions", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Player_ExploitableArea:
                    entity.AddParameter("NpcSafePositions", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetDefendArea:
                    entity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetPursuitArea:
                    entity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_ForceRetreat:
                    entity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_DefineBackstageAvoidanceArea:
                    entity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetStartPos:
                    entity.AddParameter("StartPos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetHidingNearestLocation:
                    entity.AddParameter("hiding_pos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_TriggerAimRequest:
                    entity.AddParameter("AimTarget", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Custom_Hiding_Vignette_controller:
                    entity.AddParameter("Breath", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Blackout_start_time", new cInteger(15), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("run_out_time", new cInteger(60), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Custom_Hiding_Controller:
                    entity.AddParameter("Enter_Anim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Idle_Anim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Exit_Anim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("has_MT", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("is_high", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AlienBusted_Player_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AlienBusted_Alien_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AlienBusted_Player_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AlienBusted_Alien_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AlienBusted_Player_3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AlienBusted_Alien_3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AlienBusted_Player_4", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AlienBusted_Alien_4", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AndroidBusted_Player_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AndroidBusted_Android_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AndroidBusted_Player_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AndroidBusted_Android_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TorchDynamicMovement:
                    entity.AddParameter("torch", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.EQUIPPABLE_ITEM:
                    entity.AddParameter("item_animated_asset", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.MELEE_WEAPON:
                    entity.AddParameter("item_animated_model_and_collision", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_GiveToCharacter:
                    entity.AddParameter("Character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Weapon", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactEffect:
                    entity.AddParameter("StaticEffects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("DynamicEffects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("DynamicAttachedEffects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_Effect:
                    entity.AddParameter("WorldPos", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("AttachedEffects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("UnattachedEffects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.EFFECT_EntityGenerator:
                    entity.AddParameter("entities", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VariableVector2:
                    entity.AddParameter("initial_value", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VariableColour:
                    entity.AddParameter("initial_colour", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VariableFlashScreenColour:
                    entity.AddParameter("initial_colour", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VariableObject:
                    entity.AddParameter("initial", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Zone:
                    entity.AddParameter("composites", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ZoneLink:
                    entity.AddParameter("ZoneA", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("ZoneB", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ZoneExclusionLink:
                    entity.AddParameter("ZoneA", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("ZoneB", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.StateQuery:
                    entity.AddParameter("Input", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.BooleanLogicInterface:
                    entity.AddParameter("LHS", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("RHS", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.LogicDelay:
                    entity.AddParameter("delay", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("can_suspend", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.LogicGate:
                    entity.AddParameter("allow", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.BooleanLogicOperation:
                    entity.AddParameter("Input", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatMath_All:
                    entity.AddParameter("Numbers", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatMath:
                    entity.AddParameter("LHS", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("RHS", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatMultiplyClamp:
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatClampMultiply:
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatOperation:
                    entity.AddParameter("Input", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatCompare:
                    entity.AddParameter("LHS", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("RHS", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Threshold", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatLinearProportion:
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Proportion", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatGetLinearProportion:
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Input", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatSmoothStep:
                    entity.AddParameter("Low_Edge", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("High_Edge", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Value", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatClamp:
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Value", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.IntegerMath_All:
                    entity.AddParameter("Numbers", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.IntegerMath:
                    entity.AddParameter("LHS", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("RHS", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.IntegerOperation:
                    entity.AddParameter("Input", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.IntegerCompare:
                    entity.AddParameter("LHS", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("RHS", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.IntegerAnalyse:
                    entity.AddParameter("Input", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorMath:
                    entity.AddParameter("LHS", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("RHS", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorScale:
                    entity.AddParameter("LHS", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("RHS", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorNormalise:
                    entity.AddParameter("Input", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorModulus:
                    entity.AddParameter("Input", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ScalarProduct:
                    entity.AddParameter("LHS", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("RHS", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorDirection:
                    entity.AddParameter("From", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("To", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorYaw:
                    entity.AddParameter("Vector", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorRotateYaw:
                    entity.AddParameter("Vector", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Yaw", new cFloat(0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorRotateRoll:
                    entity.AddParameter("Vector", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Roll", new cFloat(0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorRotatePitch:
                    entity.AddParameter("Vector", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Pitch", new cFloat(0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorRotateByPos:
                    entity.AddParameter("Vector", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("WorldPos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorMultiplyByPos:
                    entity.AddParameter("Vector", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("WorldPos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorDistance:
                    entity.AddParameter("LHS", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("RHS", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorReflect:
                    entity.AddParameter("Input", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Normal", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SetVector:
                    entity.AddParameter("x", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("y", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("z", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SetVector2:
                    entity.AddParameter("Input", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SetColour:
                    entity.AddParameter("Colour", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetTranslation:
                    entity.AddParameter("Input", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetRotation:
                    entity.AddParameter("Input", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetComponentInterface:
                    entity.AddParameter("Input", new cVector3(new Vector3(0.0f, 0.0f, 0.0f)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SetPosition:
                    entity.AddParameter("Translation", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Rotation", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Input", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PositionDistance:
                    entity.AddParameter("LHS", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("RHS", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorLinearProportion:
                    entity.AddParameter("Initial_Value", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Target_Value", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Proportion", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorLinearInterpolateTimed:
                    entity.AddParameter("Initial_Value", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Target_Value", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Reverse", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorLinearInterpolateSpeed:
                    entity.AddParameter("Initial_Value", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Target_Value", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Reverse", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.MoveInTime:
                    entity.AddParameter("start_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("end_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SmoothMove:
                    entity.AddParameter("timer", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("start_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("end_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("start_velocity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("end_velocity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RotateInTime:
                    entity.AddParameter("start_pos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("origin", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("timer", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RotateAtSpeed:
                    entity.AddParameter("start_pos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("origin", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("timer", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PointAt:
                    entity.AddParameter("origin", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SetLocationAndOrientation:
                    entity.AddParameter("location", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("axis", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("local_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ApplyRelativeTransform:
                    entity.AddParameter("origin", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("input", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RandomSelect:
                    entity.AddParameter("Input", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayEnvironmentAnimation:
                    entity.AddParameter("geometry", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("marker", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("external_start_time", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("external_time", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CAGEAnimation:
                    entity.AddParameter("external_time", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.MultitrackLoop:
                    entity.AddParameter("current_time", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("loop_condition", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ReTransformer:
                    entity.AddParameter("new_transform", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Checkpoint:
                    entity.AddParameter("player_spawn_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.EndGame:
                    entity.AddParameter("success", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.LeaveGame:
                    entity.AddParameter("disconnect_from_session", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.DebugTextStacking:
                    entity.AddParameter("float_input", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("int_input", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("bool_input", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("vector_input", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("enum_input", new cEnum(0), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.DebugText:
                    entity.AddParameter("float_input", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("int_input", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("bool_input", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("vector_input", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("enum_input", new cEnum(0), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("text_input", new cString(""), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.DebugEnvironmentMarker:
                    entity.AddParameter("target", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("float_input", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("int_input", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("bool_input", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("vector_input", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("enum_input", new cEnum(0), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.DebugCaptureCorpse:
                    entity.AddParameter("character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayerTorch:
                    entity.AddParameter("power_in_current_battery", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Master:
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ExclusiveMaster:
                    entity.AddParameter("active_objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("inactive_objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SpawnGroup:
                    entity.AddParameter("default_group", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("trigger_on_reset", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RespawnExcluder:
                    entity.AddParameter("excluded_points", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RandomObjectSelector:
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerVolumeFilter:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerVolumeFilter_Monitored:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerFilter:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerObjectsFilter:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.BindObjectsMultiplexer:
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerObjectsFilterCounter:
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerContainerObjectsFilterCounter:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerTouch:
                    entity.AddParameter("physics_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.COLLISION_MAPPING) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerDamaged:
                    entity.AddParameter("physics_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.COLLISION_MAPPING) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerBindCharacter:
                    entity.AddParameter("characters", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerSelect:
                    entity.AddParameter("Object_0", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_1", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_2", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_3", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_4", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_5", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_6", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_7", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_8", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_9", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_10", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_11", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_12", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_13", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_14", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_15", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_16", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerSelect_Direct:
                    entity.AddParameter("Object_0", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_1", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_2", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_3", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_4", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_5", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_6", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_7", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_8", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_9", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_10", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_11", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_12", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_13", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_14", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_15", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Object_16", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.LogicPressurePad:
                    entity.AddParameter("Limit", new cInteger(1), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SetObject:
                    entity.AddParameter("Input", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GateResourceInterface:
                    entity.AddParameter("request_open_on_reset", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("request_lock_on_reset", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("force_open_on_reset", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("force_close_on_reset", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("is_auto", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("auto_close_delay", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Door:
                    entity.AddParameter("zone_link", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("animation", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ANIMATED_MODEL) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("trigger_filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("icon_pos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("icon_usable_radius", new cFloat(3.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("show_icon_when_locked", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("nav_mesh", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NAV_MESH_BARRIER_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("wait_point_1", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("wait_point_2", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("geometry", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("is_scripted", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("wait_to_open", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerViewCone:
                    entity.AddParameter("target", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("fov", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("max_distance", new cFloat(15.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("aspect_ratio", new cFloat(1.777f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("source_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("intersect_with_geometry", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerCameraViewCone:
                    entity.AddParameter("target", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("fov", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("aspect_ratio", new cFloat(1.777f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("intersect_with_geometry", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerCameraViewConeMulti:
                    entity.AddParameter("target", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target1", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target2", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target3", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target4", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target5", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target6", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target7", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target8", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target9", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("fov", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("aspect_ratio", new cFloat(1.777f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("intersect_with_geometry", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_Debug_Menu_Item:
                    entity.AddParameter("character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Character:
                    entity.AddParameter("contents_of_dead_container", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterAnd:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterOr:
                    entity.AddParameter("filter", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterNot:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterIsEnemyOfCharacter:
                    entity.AddParameter("Character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterIsPhysicsObject:
                    entity.AddParameter("object", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterIsObject:
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterIsCharacter:
                    entity.AddParameter("character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterIsFacingTarget:
                    entity.AddParameter("target", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterIsinInventory:
                    entity.AddParameter("ItemName", new cString(""), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterCanSeeTarget:
                    entity.AddParameter("Target", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterIsAgressing:
                    entity.AddParameter("Target", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterIsValidInventoryItem:
                    entity.AddParameter("item", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterIsInWeaponRange:
                    entity.AddParameter("weapon_owner", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerWhenSeeTarget:
                    entity.AddParameter("Target", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.JOB_SpottingPosition:
                    entity.AddParameter("SpottingPosition", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Task:
                    entity.AddParameter("Job", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("TaskPosition", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FlareTask:
                    entity.AddParameter("specific_character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.IdleTask:
                    entity.AddParameter("specific_character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerBindAllNPCs:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("centre", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SoundPlaybackBaseClass:
                    entity.AddParameter("attached_sound_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_OBJECT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Speech:
                    entity.AddParameter("character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("alt_character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SpeechScript:
                    entity.AddParameter("character_01", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("character_02", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("character_03", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("character_04", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("character_05", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("alt_character_01", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("alt_character_02", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("alt_character_03", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("alt_character_04", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("alt_character_05", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SoundLoadBank:
                    entity.AddParameter("sound_bank", new cString(""), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SoundSetRTPC:
                    entity.AddParameter("rtpc_value", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("sound_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_OBJECT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SoundSetSwitch:
                    entity.AddParameter("sound_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_OBJECT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.MusicTrigger:
                    entity.AddParameter("connected_object", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.AddToInventory:
                    entity.AddParameter("items", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RemoveFromInventory:
                    entity.AddParameter("items", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.LimitItemUse:
                    entity.AddParameter("items", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayerHasItem:
                    entity.AddParameter("items", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayerHasItemWithName:
                    entity.AddParameter("item_name", new cString(""), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayerHasItemEntity:
                    entity.AddParameter("items", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayerHasEnoughItems:
                    entity.AddParameter("items", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayerHasSpaceForItem:
                    entity.AddParameter("items", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.InventoryItem:
                    entity.AddParameter("itemName", new cString(""), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetInventoryItemName:
                    entity.AddParameter("item", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("equippable_item", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.EQUIPPABLE_ITEM_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PickupSpawner:
                    entity.AddParameter("pos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.MultiplePickupSpawner:
                    entity.AddParameter("pos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.AddItemsToGCPool:
                    entity.AddParameter("items", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.AllocateGCItemsFromPool:
                    entity.AddParameter("items", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.AllocateGCItemFromPoolBySubset:
                    entity.AddParameter("selectable_items", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FlashInvoke:
                    entity.AddParameter("layer_name", new cString(""), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("mrtt_texture", new cString(""), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.MotionTrackerPing:
                    entity.AddParameter("FakePosition", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GenericHighlightEntity:
                    entity.AddParameter("highlight_geometry", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.UI_Icon:
                    entity.AddParameter("geometry", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("highlight_geometry", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target_pickup_item", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("highlight_distance_threshold", new cFloat(3.15f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("interaction_distance_threshold", new cFloat(20f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.UI_Attached:
                    entity.AddParameter("ui_icon", new cInteger(0), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.UI_Container:
                    entity.AddParameter("contents", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TerminalFolder:
                    entity.AddParameter("content0", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_CONTENT_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("content1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_CONTENT_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.AccessTerminal:
                    entity.AddParameter("folder0", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("folder1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("folder2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("folder3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.MapAnchor:
                    entity.AddParameter("map_north", new cVector3(new Vector3(0, 0, 1)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("map_pos", new cVector3(new Vector3(0.5f, 0, 0.5f)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("map_scale", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RewireSystem:
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RewireLocation:
                    entity.AddParameter("systems", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_SYSTEM) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RewireAccess_Point:
                    entity.AddParameter("interactive_locations", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_LOCATION) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("visible_locations", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_LOCATION) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Rewire:
                    entity.AddParameter("locations", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_LOCATION) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("access_points", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_ACCESS_POINT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CoverLine:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("low", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLadder:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousPipe:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLedge:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousClimbingWall:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousCinematicSidle:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousBalanceBeam:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousTightGap:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentEntrance:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentExit:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentExit:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotClimbUnder:
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotLeap:
                    entity.AddParameter("StartEdgeLinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("EndEdgeLinePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotSpline:
                    entity.AddParameter("EntrancePath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("ExitPath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("MinimumPath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("MaximumPath", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("MinimumSupport", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("MaximumSupport", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PathfindingTeleportNode:
                    entity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PathfindingWaitNode:
                    entity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PathfindingManualNode:
                    entity.AddParameter("PlayAnimData", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PathfindingAlienBackstageNode:
                    entity.AddParameter("PlayAnimData_Entry", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("PlayAnimData_Exit", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Killtrap_alien", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Killtrap_victim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetChokePoint:
                    entity.AddParameter("chokepoints", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHOKE_POINT_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Planet:
                    entity.AddParameter("planet_resource", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("parallax_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("sun_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("light_shaft_source_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SpaceTransform:
                    entity.AddParameter("affected_geometry", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.NonInteractiveWater:
                    entity.AddParameter("water_resource", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Refraction:
                    entity.AddParameter("refraction_resource", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FogPlane:
                    entity.AddParameter("fog_plane_resource", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PostprocessingSettings:
                    entity.AddParameter("intensity", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.BloomSettings:
                    entity.AddParameter("frame_buffer_scale", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("frame_buffer_offset", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("bloom_scale", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("bloom_gather_exponent", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("bloom_gather_scale", new cFloat(0.04f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ColourSettings:
                    entity.AddParameter("brightness", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("contrast", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("saturation", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("red_tint", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("green_tint", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("blue_tint", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FlareSettings:
                    entity.AddParameter("flareOffset0", new cFloat(-1.2f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareIntensity0", new cFloat(0.05f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareAttenuation0", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareOffset1", new cFloat(-1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareIntensity1", new cFloat(0.15f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareAttenuation1", new cFloat(0.7f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareOffset2", new cFloat(-0.8f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareIntensity2", new cFloat(0.20f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareAttenuation2", new cFloat(7.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareOffset3", new cFloat(-0.6f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareIntensity3", new cFloat(0.40f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flareAttenuation3", new cFloat(1.5f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.HighSpecMotionBlurSettings:
                    entity.AddParameter("contribution", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("camera_velocity_scalar", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("camera_velocity_min", new cFloat(1.5f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("camera_velocity_max", new cFloat(3.5f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("object_velocity_scalar", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("object_velocity_min", new cFloat(1.5f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("object_velocity_max", new cFloat(3.5f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("blur_range", new cFloat(16f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FilmGrainSettings:
                    entity.AddParameter("low_lum_amplifier", new cFloat(0.2f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("mid_lum_amplifier", new cFloat(0.25f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("high_lum_amplifier", new cFloat(0.4f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("low_lum_range", new cFloat(0.2f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("mid_lum_range", new cFloat(0.3f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("high_lum_range", new cFloat(0.2f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("noise_texture_scale", new cFloat(4.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("adaptive", new cBool(false), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("adaptation_scalar", new cFloat(3.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("adaptation_time_scalar", new cFloat(0.25f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("unadapted_low_lum_amplifier", new cFloat(0.2f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("unadapted_mid_lum_amplifier", new cFloat(0.25f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("unadapted_high_lum_amplifier", new cFloat(0.4f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VignetteSettings:
                    entity.AddParameter("vignette_factor", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("vignette_chromatic_aberration_scale", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.DistortionSettings:
                    entity.AddParameter("radial_distort_factor", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("radial_distort_constraint", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("radial_distort_scalar", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SharpnessSettings:
                    entity.AddParameter("local_contrast_factor", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.IrawanToneMappingSettings:
                    entity.AddParameter("target_device_luminance", new cFloat(6.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target_device_adaptation", new cFloat(20.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("saccadic_time", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("superbright_adaptation", new cFloat(0.5f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.HableToneMappingSettings:
                    entity.AddParameter("shoulder_strength", new cFloat(0.22f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("linear_strength", new cFloat(0.30f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("linear_angle", new cFloat(0.10f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("toe_strength", new cFloat(0.20f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("toe_numerator", new cFloat(0.01f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("toe_denominator", new cFloat(0.30f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("linear_white_point", new cFloat(11.2f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.DayToneMappingSettings:
                    entity.AddParameter("black_point", new cFloat(0.00625f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("cross_over_point", new cFloat(0.4f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("white_point", new cFloat(40f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("shoulder_strength", new cFloat(0.95f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("toe_strength", new cFloat(0.15f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("luminance_scale", new cFloat(5f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.LightAdaptationSettings:
                    entity.AddParameter("fast_neural_t0", new cFloat(5.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("slow_neural_t0", new cFloat(5.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("pigment_bleaching_t0", new cFloat(20.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("fb_luminance_to_candelas_per_m2", new cFloat(105.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("max_adaptation_lum", new cFloat(20000f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("min_adaptation_lum", new cFloat(0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("adaptation_percentile", new cFloat(0.3f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("low_bracket", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("high_bracket", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ColourCorrectionTransition:
                    entity.AddParameter("interpolate", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ProjectileMotion:
                    entity.AddParameter("start_pos", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("start_velocity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ProjectileMotionComplex:
                    entity.AddParameter("start_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("start_velocity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("start_angular_velocity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("flight_time_in_seconds", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SplineDistanceLerp:
                    entity.AddParameter("spline", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("lerp_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.MoveAlongSpline:
                    entity.AddParameter("spline", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("speed", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetSplineLength:
                    entity.AddParameter("spline", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetPointOnSpline:
                    entity.AddParameter("spline", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("percentage_of_spline", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPercentOnSpline:
                    entity.AddParameter("spline", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("pos_to_be_near", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPointOnSpline:
                    entity.AddParameter("spline", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("pos_to_be_near", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPoint:
                    entity.AddParameter("Positions", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("pos_to_be_near", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPointFromSet:
                    entity.AddParameter("Position_1", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Position_2", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Position_3", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Position_4", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Position_5", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Position_6", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Position_7", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Position_8", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Position_9", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Position_10", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("pos_to_be_near", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetCentrePoint:
                    entity.AddParameter("Positions", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FogSetting:
                    entity.AddParameter("linear_distance", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("max_distance", new cFloat(850.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("linear_density", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("exponential_density", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("near_colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("far_colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.FullScreenBlurSettings:
                    entity.AddParameter("contribution", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.DistortionOverlay:
                    entity.AddParameter("intensity", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("time", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.DepthOfFieldSettings:
                    entity.AddParameter("focal_length_mm", new cFloat(75.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("focal_plane_m", new cFloat(2.5f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("fnum", new cFloat(2.8f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CharacterMonitor:
                    entity.AddParameter("character", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.AreaHitMonitor:
                    entity.AddParameter("SpherePos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("SphereRadius", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Raycast:
                    entity.AddParameter("source_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("max_distance", new cFloat(100.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PhysicsApplyImpulse:
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("direction", new cVector3(new Vector3(0, 1, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("force", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("can_damage", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PhysicsApplyVelocity:
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("angular_velocity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("linear_velocity", new cVector3(new Vector3(0, 1, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("propulsion_velocity", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PhysicsModifyGravity:
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PhysicsApplyBuoyancy:
                    entity.AddParameter("objects", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("water_height", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("water_density", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("water_viscosity", new cFloat(1.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("water_choppiness", new cFloat(0.05f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.AssetSpawner:
                    entity.AddParameter("asset", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.CharacterAttachmentNode:
                    entity.AddParameter("character", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("attachment", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.MultipleCharacterAttachmentNode:
                    entity.AddParameter("character_01", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("attachment_01", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("character_02", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("attachment_02", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("character_03", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("attachment_03", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("character_04", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("attachment_04", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("character_05", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("attachment_05", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.AnimatedModelAttachmentNode:
                    entity.AddParameter("animated_model", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("attachment", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GetCharacterRotationSpeed:
                    entity.AddParameter("character", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.EnvironmentMap:
                    entity.AddParameter("Entities", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.AddExitObjective:
                    entity.AddParameter("marker", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SetSubObjective:
                    entity.AddParameter("target_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.DebugGraph:
                    entity.AddParameter("Inputs", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SmokeCylinder:
                    entity.AddParameter("pos", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("height", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.SmokeCylinderAttachmentInterface:
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("height", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PointTracker:
                    entity.AddParameter("origin", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("target_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.VisibilityMaster:
                    entity.AddParameter("renderable", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("mastered_by_visibility", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.GlobalEvent:
                    entity.AddParameter("EventValue", new cInteger(1), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.InteractiveMovementControl:
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("start_time", new cFloat(0.0f), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("progress_path", new cSpline(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayForMinDuration:
                    entity.AddParameter("MinDuration", new cFloat(5.0f), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.Torch_Control:
                    entity.AddParameter("character", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.BulletChamber:
                    entity.AddParameter("Slot1", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Slot2", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Slot3", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Slot4", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Slot5", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Slot6", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Weapon", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("Geometry", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayerDeathCounter:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.RadiosityIsland:
                    entity.AddParameter("composites", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("exclusions", new cString(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
                case FunctionType.ProximityDetector:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT_PIN, overwrite);
                    entity.AddParameter("detector_position", new cTransform(), ParameterVariant.INPUT_PIN, overwrite);
                    break;
            }
        }
        private static void ApplyDefaultOutputPinForFunction(Entity entity, FunctionType type, bool overwrite)
        {
            switch (type)
            {
                case FunctionType.ButtonMashPrompt:
                    entity.AddParameter("count", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetFlashIntValue:
                    entity.AddParameter("int_value", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetFlashFloatValue:
                    entity.AddParameter("float_value", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CameraPlayAnimation:
                    entity.AddParameter("animation_length", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("frames_count", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("result_transformation", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CamPeek:
                    entity.AddParameter("pos", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("x_ratio", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("y_ratio", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetCurrentCameraTarget:
                    entity.AddParameter("target", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("distance", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_FollowOffset:
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CMD_PlayAnimation:
                    entity.AddParameter("animationLength", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_PlaySecondaryAnimation:
                    entity.AddParameter("animationLength", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_GetAlliance:
                    entity.AddParameter("Alliance", new cEnum(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_GetHealth:
                    entity.AddParameter("Health", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_DamageMonitor:
                    entity.AddParameter("DamageDone", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("Instigator", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_DeathMonitor:
                    entity.AddParameter("Killer", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_TorchMonitor:
                    entity.AddParameter("TorchOn", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_VentMonitor:
                    entity.AddParameter("IsInVent", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CharacterTypeMonitor:
                    entity.AddParameter("AreAny", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.Convo:
                    entity.AddParameter("speaker", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_GetLastSensedPositionOfTarget:
                    entity.AddParameter("LastSensedPosition", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_GetTorch:
                    entity.AddParameter("TorchOn", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.NPC_GetCombatTarget:
                    entity.AddParameter("target", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CHR_HasWeaponOfType:
                    entity.AddParameter("Result", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.Custom_Hiding_Vignette_controller:
                    entity.AddParameter("Vignette", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("FadeValue", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.Custom_Hiding_Controller:
                    entity.AddParameter("MT_pos", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.EQUIPPABLE_ITEM:
                    entity.AddParameter("owner", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("has_owner", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.AIMED_ITEM:
                    entity.AddParameter("target_position", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("average_target_distance", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("min_target_distance", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.AIMED_WEAPON:
                    entity.AddParameter("ammoRemainingInClip", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("ammoToFillClip", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("ammoThatWasInClip", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("charge_percentage", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("charge_noise_percentage", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactInspector:
                    entity.AddParameter("damage", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("impact_position", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("impact_target", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.StateQuery:
                    entity.AddParameter("Result", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.BooleanLogicInterface:
                    entity.AddParameter("Result", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.BooleanLogicOperation:
                    entity.AddParameter("Result", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatMath_All:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatMath:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatOperation:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatCompare:
                    entity.AddParameter("Result", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatModulate:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatModulateRandom:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatLinearProportion:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatGetLinearProportion:
                    entity.AddParameter("Proportion", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatLinearInterpolateTimed:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatLinearInterpolateSpeed:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatLinearInterpolateSpeedAdvanced:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatSmoothStep:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FloatClamp:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.FilterAbsorber:
                    entity.AddParameter("output", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.IntegerMath_All:
                    entity.AddParameter("Result", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.IntegerMath:
                    entity.AddParameter("Result", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.IntegerOperation:
                    entity.AddParameter("Result", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.IntegerCompare:
                    entity.AddParameter("Result", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.IntegerAnalyse:
                    entity.AddParameter("Result", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.SetEnum:
                    entity.AddParameter("Output", new cEnum(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.SetString:
                    entity.AddParameter("Output", new cString(""), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorMath:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorScale:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorNormalise:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorModulus:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.ScalarProduct:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorDirection:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorYaw:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorRotateYaw:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorRotateRoll:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorRotatePitch:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorRotateByPos:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorMultiplyByPos:
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorDistance:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorReflect:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.SetVector:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.SetVector2:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.SetColour:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetTranslation:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetRotation:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetComponentInterface:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.SetPosition:
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.PositionDistance:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorLinearProportion:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorLinearInterpolateTimed:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.VectorLinearInterpolateSpeed:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.MoveInTime:
                    entity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.SmoothMove:
                    entity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.RotateInTime:
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.RotateAtSpeed:
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.PointAt:
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.SetLocationAndOrientation:
                    entity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.ApplyRelativeTransform:
                    entity.AddParameter("output", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.RandomFloat:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.RandomInt:
                    entity.AddParameter("Result", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.RandomBool:
                    entity.AddParameter("Result", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.RandomVector:
                    entity.AddParameter("Result", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.RandomSelect:
                    entity.AddParameter("Result", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerRandomSequence:
                    entity.AddParameter("current", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.Persistent_TriggerRandomSequence:
                    entity.AddParameter("current", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerWeightedRandom:
                    entity.AddParameter("current", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayEnvironmentAnimation:
                    entity.AddParameter("animation_length", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.CAGEAnimation:
                    entity.AddParameter("current_time", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.ReTransformer:
                    entity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerSequence:
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayerTorch:
                    entity.AddParameter("battery_count", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.NetPlayerCounter:
                    entity.AddParameter("is_full", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("is_empty", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("contains_local_player", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.NumConnectedPlayers:
                    entity.AddParameter("count", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.NumPlayersOnStart:
                    entity.AddParameter("count", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.NetworkedTimer:
                    entity.AddParameter("time_elapsed", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("time_left", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("time_elapsed_sec", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("time_left_sec", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.RandomObjectSelector:
                    entity.AddParameter("chosen_object", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerTouch:
                    entity.AddParameter("impact_normal", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerDamaged:
                    entity.AddParameter("impact_normal", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerExtractBoundObject:
                    entity.AddParameter("bound_object", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerExtractBoundCharacter:
                    entity.AddParameter("bound_character", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerDelay:
                    entity.AddParameter("time_left", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerSwitch:
                    entity.AddParameter("current", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerSelect:
                    entity.AddParameter("Result", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerSelect_Direct:
                    entity.AddParameter("Result", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("TriggeredIndex", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.Counter:
                    entity.AddParameter("Count", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.LogicCounter:
                    entity.AddParameter("Count", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.LogicPressurePad:
                    entity.AddParameter("Count", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.SetObject:
                    entity.AddParameter("Output", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GateResourceInterface:
                    entity.AddParameter("is_open", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("is_locked", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("gate_status", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.Door:
                    entity.AddParameter("is_waiting", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.MonitorPadInput:
                    entity.AddParameter("left_stick_x", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("left_stick_y", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("right_stick_x", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("right_stick_y", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.MonitorActionMap:
                    entity.AddParameter("movement_stick_x", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("movement_stick_y", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("camera_stick_x", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("camera_stick_y", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("mouse_x", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("mouse_y", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("analog_aim", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("analog_shoot", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerViewCone:
                    entity.AddParameter("visible_target", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TriggerCameraVolume:
                    entity.AddParameter("inside_factor", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("lookat_factor", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("lookat_X_position", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("lookat_Y_position", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.InventoryItem:
                    entity.AddParameter("out_itemName", new cString(""), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("out_quantity", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.AllocateGCItemFromPoolBySubset:
                    entity.AddParameter("item_name", new cString(""), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.QueryGCItemPool:
                    entity.AddParameter("count", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.UI_Icon:
                    entity.AddParameter("icon_user", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.UI_Container:
                    entity.AddParameter("has_been_used", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.UI_ReactionGame:
                    entity.AddParameter("completion_percentage", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.HackingGame:
                    entity.AddParameter("completion_percentage", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.AccessTerminal:
                    entity.AddParameter("all_data_read", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetGatingToolLevel:
                    entity.AddParameter("level", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetBlueprintLevel:
                    entity.AddParameter("level", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetBlueprintAvailable:
                    entity.AddParameter("available", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetSelectedCharacterId:
                    entity.AddParameter("character_id", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetNextPlaylistLevelName:
                    entity.AddParameter("level_name", new cString(""), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.IsPlaylistTypeSingle:
                    entity.AddParameter("single", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.IsPlaylistTypeAll:
                    entity.AddParameter("all", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.IsPlaylistTypeMarathon:
                    entity.AddParameter("marathon", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.IsCurrentLevelAChallengeMap:
                    entity.AddParameter("challenge_map", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.IsCurrentLevelAPreorderMap:
                    entity.AddParameter("preorder_map", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetCurrentPlaylistLevelIndex:
                    entity.AddParameter("index", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLadder:
                    entity.AddParameter("InUse", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousPipe:
                    entity.AddParameter("InUse", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLedge:
                    entity.AddParameter("InUse", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousClimbingWall:
                    entity.AddParameter("InUse", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousCinematicSidle:
                    entity.AddParameter("InUse", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousBalanceBeam:
                    entity.AddParameter("InUse", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousTightGap:
                    entity.AddParameter("InUse", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotClimbUnder:
                    entity.AddParameter("InUse", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotLeap:
                    entity.AddParameter("InUse", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.ProjectileMotion:
                    entity.AddParameter("Current_Position", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("Current_Velocity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.ProjectileMotionComplex:
                    entity.AddParameter("current_position", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("current_velocity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("current_angular_velocity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("current_flight_time_in_seconds", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.SplineDistanceLerp:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.MoveAlongSpline:
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetSplineLength:
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetPointOnSpline:
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPercentOnSpline:
                    entity.AddParameter("position_on_spline", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("Result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPointOnSpline:
                    entity.AddParameter("position_on_spline", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPoint:
                    entity.AddParameter("position_of_closest", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPointFromSet:
                    entity.AddParameter("position_of_closest", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("index_of_closest", new cInteger(0), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetCentrePoint:
                    entity.AddParameter("position_of_centre", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.Raycast:
                    entity.AddParameter("hit_object", new cString(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("hit_distance", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("hit_position", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.GetCharacterRotationSpeed:
                    entity.AddParameter("speed", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.PointTracker:
                    entity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.ThrowingPointOfImpact:
                    entity.AddParameter("Location", new cTransform(), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("Visible", new cBool(false), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayerLightProbe:
                    entity.AddParameter("output", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("light_level_for_ai", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("dark_threshold", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("fully_lit_threshold", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.InteractiveMovementControl:
                    entity.AddParameter("result", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    entity.AddParameter("speed", new cFloat(0.0f), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
                case FunctionType.PlayerDeathCounter:
                    entity.AddParameter("count", new cInteger(1), ParameterVariant.OUTPUT_PIN, overwrite);
                    break;
            }
        }
        private static void ApplyDefaultParameterForFunction(Entity entity, FunctionType type, bool overwrite)
        {
            switch (type)
            {
                case FunctionType.ScriptInterface:
                    //entity.AddParameter("name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ZoneInterface:
                    entity.AddParameter("force_visible_on_load", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AttachmentInterface:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CompositeInterface:
                    entity.AddParameter("disable_display", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disable_collision", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disable_simulation", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("mapping", new cString(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SplinePath:
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("orientated", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Box:
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.HasAccessAtDifficulty:
                    entity.AddParameter("difficulty", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UpdateLeaderBoardDisplay:
                    entity.AddParameter("time", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetNextLoadingMovie:
                    entity.AddParameter("playlist_to_load", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ButtonMashPrompt:
                    entity.AddParameter("mashes_to_completion", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("time_between_degrades", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_degrade", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("hold_to_charge", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GetFlashIntValue:
                    entity.AddParameter("callback_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GetFlashFloatValue:
                    entity.AddParameter("callback_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Sphere:
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ImpactSphere:
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UiSelectionBox:
                    entity.AddParameter("is_priority", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UiSelectionSphere:
                    entity.AddParameter("is_priority", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CollisionBarrier:
                    entity.AddParameter("collision_type", new cEnum(EnumType.COLLISION_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("static_collision", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerTriggerBox:
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerUseTriggerBox:
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ModelReference:
                    entity.AddParameter("material", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("occludes_atmosphere", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lod_ranges", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("intensity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("radiosity_multiplier", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("emissive_tint", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("replace_intensity", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("replace_tint", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("decal_scale", new cVector3(new Vector3(1.0f, 1.0f, 1.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lightdecal_tint", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lightdecal_intensity", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("diffuse_colour_scale", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("diffuse_opacity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("vertex_colour_scale", new cVector3(new Vector3(1.0f, 1.0f, 1.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("vertex_opacity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("uv_scroll_speed_x", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("uv_scroll_speed_y", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alpha_blend_noise_power_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alpha_blend_noise_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alpha_blend_noise_uv_offset_X", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alpha_blend_noise_uv_offset_Y", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("dirt_multiply_blend_spec_power_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("dirt_map_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("remove_on_damaged", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("damage_threshold", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_debris", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_prop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_thrown", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("report_sliding", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_keyframed", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_transparent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("soft_collision", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("allow_reposition_of_physics", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disable_size_culling", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("cast_shadows", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("cast_shadows_in_torch", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.LightReference:
                    entity.AddParameter("type", new cEnum(EnumType.LIGHT_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("defocus_attenuation", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("start_attenuation", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("end_attenuation", new cFloat(2.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("physical_attenuation", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("near_dist", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("near_dist_shadow_offset", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("inner_cone_angle", new cFloat(22.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("outer_cone_angle", new cFloat(45.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("intensity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("radiosity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("area_light_radius", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("diffuse_softness", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("diffuse_bias", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("glossiness_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flare_occluder_radius", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flare_spot_offset", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flare_intensity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("cast_shadow", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("fade_type", new cEnum(EnumType.LIGHT_FADE_TYPE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_specular", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("has_lens_flare", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("has_noclip", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_square_light", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_flash_light", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("no_alphalight", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("shadow_priority", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("aspect_ratio", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("gobo_texture", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("horizontal_gobo_flip", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("strip_length", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("distance_mip_selection_gobo", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("volume", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("volume_end_attenuation", new cFloat(-1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("volume_colour_factor", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("volume_density", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("depth_bias", new cFloat(0.05f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("slope_scale_depth_bias", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ParticleEmitterReference:
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("material", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("unique_material", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("quality_level", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("bounds_max", new cVector3(new Vector3(2.0f, 2.0f, 2.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("bounds_min", new cVector3(new Vector3(-2.0f, -2.0f, -2.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DRAW_PASS", new cInteger(8), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ASPECT_RATIO", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FADE_AT_DISTANCE", new cFloat(5000.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PARTICLE_COUNT", new cInteger(100), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SYSTEM_EXPIRY_TIME", new cFloat(10f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SIZE_START_MIN", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SIZE_START_MAX", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SIZE_END_MIN", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SIZE_END_MAX", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_IN", new cFloat(0.01f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_OUT", new cFloat(99.99f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MASK_AMOUNT_MIN", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MASK_AMOUNT_MAX", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MASK_AMOUNT_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PARTICLE_EXPIRY_TIME_MIN", new cFloat(2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PARTICLE_EXPIRY_TIME_MAX", new cFloat(2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_SCALE_MIN", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_SCALE_MAX", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIND_X", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIND_Y", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIND_Z", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_REF_VALUE", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING_LS", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING_NONE", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING_ON_AXIS_X", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING_ON_AXIS_Y", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING_ON_AXIS_Z", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING_VELOCITY_ALIGNED", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING_VELOCITY_STRETCHED", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING_SPHERE_PROJECTION", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLENDING_STANDARD", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLENDING_ALPHA_REF", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLENDING_ADDITIVE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLENDING_PREMULTIPLIED", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLENDING_DISTORTION", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LOW_RES", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EARLY_ALPHA", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LOOPING", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ANIMATED_ALPHA", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NONE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LIGHTING", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PER_PARTICLE_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("X_AXIS_FLIP", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Y_AXIS_FLIP", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARD_FACING", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING_ON_AXIS_FADEOUT", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARDING_CAMERA_LOCKED", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAMERA_RELATIVE_POS_X", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAMERA_RELATIVE_POS_Y", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAMERA_RELATIVE_POS_Z", new cFloat(3.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPHERE_PROJECTION_RADIUS", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DISTORTION_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SCALE_MODIFIER", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CPU", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPAWN_RATE", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPAWN_RATE_VAR", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPAWN_NUMBER", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LIFETIME", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LIFETIME_VAR", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_START", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_END", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WORLD_TO_LOCAL_MAX_DIST", new cFloat(1000.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CELL_EMISSION", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CELL_MAX_DIST", new cFloat(6.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CUSTOM_SEED_CPU", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SEED", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_TEST", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ZTEST", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("START_MID_END_SPEED", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED_MID_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED_MID_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED_END_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED_END_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LAUNCH_DECELERATE_SPEED", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LAUNCH_DECELERATE_SPEED_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LAUNCH_DECELERATE_SPEED_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LAUNCH_DECELERATE_DEC_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMISSION_AREA", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMISSION_AREA_X", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMISSION_AREA_Y", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMISSION_AREA_Z", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMISSION_SURFACE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMISSION_DIRECTION_SURFACE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AREA_CUBOID", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AREA_SPHEROID", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AREA_CYLINDER", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PIVOT_X", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PIVOT_Y", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("GRAVITY", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("GRAVITY_STRENGTH", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("GRAVITY_MAX_STRENGTH", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT_START", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT_END", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_USE_MID", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT_MID", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPREAD_FEATURE", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPREAD_MIN", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPREAD", new cFloat(360f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROTATION", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROTATION_MIN", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROTATION_MAX", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROTATION_RANDOM_START", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROTATION_BASE", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROTATION_VAR", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROTATION_RAMP", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROTATION_IN", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROTATION_OUT", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROTATION_DAMP", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FADE_NEAR_CAMERA", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FADE_NEAR_CAMERA_MAX_DIST", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FADE_NEAR_CAMERA_THRESHOLD", new cFloat(0.8f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TEXTURE_ANIMATION", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TEXTURE_ANIMATION_FRAMES", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NUM_ROWS", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TEXTURE_ANIMATION_LOOP_COUNT", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("RANDOM_START_FRAME", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WRAP_FRAMES", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NO_ANIM", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SUB_FRAME_BLEND", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS_ALPHA_THICKNESS", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS_ALPHA_DEPTH_MODIFIER", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("REVERSE_SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("REVERSE_SOFTNESS_EDGE", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PIVOT_AND_TURBULENCE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PIVOT_OFFSET_MIN", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PIVOT_OFFSET_MAX", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TURBULENCE_FREQUENCY_MIN", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TURBULENCE_FREQUENCY_MAX", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TURBULENCE_AMOUNT_MIN", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TURBULENCE_AMOUNT_MAX", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHATHRESHOLD", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHATHRESHOLD_TOTALTIME", new cFloat(5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHATHRESHOLD_RANGE", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHATHRESHOLD_BEGINSTART", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHATHRESHOLD_BEGINSTOP", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHATHRESHOLD_ENDSTART", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHATHRESHOLD_ENDSTOP", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_RAMP", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_RAMP_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_RAMP_ALPHA", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FADE_AXIS", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FADE_AXIS_DIST", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FADE_AXIS_PERCENT", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_UV_ANIMATION", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CYCLE_TIME", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_SPEED", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("INFINITE_PROJECTION", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PARALLAX_POSITION", new cVector3(new Vector3(0.0f, 0.0f, 0.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DISTORTION_OCCLUSION", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AMBIENT_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AMBIENT_LIGHTING_COLOUR", new cVector3(new Vector3(0.0f, 0.0f, 0.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NO_CLIP", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RibbonEmitterReference:
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("material", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("unique_material", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("quality_level", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLENDING_STANDARD", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLENDING_ALPHA_REF", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLENDING_ADDITIVE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLENDING_PREMULTIPLIED", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLENDING_DISTORTION", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NO_MIPS", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("UV_SQUARED", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LOW_RES", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LIGHTING", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MASK_AMOUNT_MIN", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MASK_AMOUNT_MAX", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MASK_AMOUNT_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DRAW_PASS", new cInteger(8), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SYSTEM_EXPIRY_TIME", new cFloat(10f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LIFETIME", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SMOOTHED", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_START", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_END", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WORLD_TO_LOCAL_MAX_DIST", new cFloat(1000.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TEXTURE", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("UV_REPEAT", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("UV_SCROLLSPEED", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MULTI_TEXTURE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("U2_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("V2_REPEAT", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("V2_SCROLLSPEED", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MULTI_TEXTURE_BLEND", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MULTI_TEXTURE_ADD", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MULTI_TEXTURE_MULT", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MULTI_TEXTURE_MAX", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MULTI_TEXTURE_MIN", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECOND_TEXTURE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TEXTURE_MAP2", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CONTINUOUS", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BASE_LOCKED", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPAWN_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TRAILING", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("INSTANT", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("RATE", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TRAIL_SPAWN_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TRAIL_DELAY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MAX_TRAILS", new cFloat(5.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("POINT_TO_POINT", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TARGET_POINT_POSITION", new cVector3(new Vector3(0.0f, 0.0f, 0.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ABS_FADE_IN_0", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ABS_FADE_IN_1", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FORCES", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("GRAVITY_STRENGTH", new cFloat(-4.81f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("GRAVITY_MAX_STRENGTH", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DRAG_STRENGTH", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIND_X", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIND_Y", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIND_Z", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("START_MID_END_SPEED", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED_START_MIN", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED_START_MAX", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIDTH", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIDTH_START", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIDTH_MID", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIDTH_END", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIDTH_IN", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WIDTH_OUT", new cFloat(0.8f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_SCALE_START", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_SCALE_MID", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_SCALE_END", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT_START", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT_MID", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT_END", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_FADE", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FADE_IN", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FADE_OUT", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EDGE_FADE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_ERODE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SIDE_ON_FADE", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SIDE_FADE_START", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SIDE_FADE_END", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DISTANCE_SCALING", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DIST_SCALE", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPREAD_FEATURE", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPREAD_MIN", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPREAD", new cFloat(0.99999f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMISSION_AREA", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMISSION_AREA_X", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMISSION_AREA_Y", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMISSION_AREA_Z", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AREA_CUBOID", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AREA_SPHEROID", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AREA_CYLINDER", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_RAMP", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_RAMP_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS_ALPHA_THICKNESS", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS_ALPHA_DEPTH_MODIFIER", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AMBIENT_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AMBIENT_LIGHTING_COLOUR", new cVector3(new Vector3(0.0f, 0.0f, 0.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NO_CLIP", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GPU_PFXEmitterReference:
                    entity.AddParameter("EFFECT_NAME", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPAWN_NUMBER", new cInteger(100), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPAWN_RATE", new cFloat(100.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPREAD_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPREAD_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EMITTER_SIZE", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LIFETIME", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LIFETIME_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FogSphere:
                    entity.AddParameter("COLOUR_TINT", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EARLY_ALPHA", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LOW_RES_ALPHA", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CONVEX_GEOM", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DISABLE_SIZE_CULLING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NO_CLIP", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_LIGHTING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DYNAMIC_ALPHA_LIGHTING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EXPONENTIAL_DENSITY", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SCENE_DEPENDANT_DENSITY", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FRESNEL_TERM", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FRESNEL_POWER", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BLEND_ALPHA_OVER_DISTANCE", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NEAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_BLEND_ALPHA_OVER_DISTANCE", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_FAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_NEAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_COLOUR", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_COLOUR_VALUE", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_ALPHA_VALUE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_RANGE", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FogBox:
                    entity.AddParameter("GEOMETRY_TYPE", new cEnum(EnumType.FOG_BOX_TYPE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DISTANCE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ANGLE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BILLBOARD", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EARLY_ALPHA", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LOW_RES", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CONVEX_GEOM", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("THICKNESS", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("START_DISTANT_CLIP", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("START_DISTANCE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LINEAR_HEIGHT_DENSITY", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SMOOTH_HEIGHT_DENSITY", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("HEIGHT_MAX_DENSITY", new cFloat(0.4f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FRESNEL_FALLOFF", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FRESNEL_POWER", new cFloat(3.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_COLOUR", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_INITIAL_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_MIDPOINT_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_MIDPOINT_ALPHA", new cFloat(1.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_MIDPOINT_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_END_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_END_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_INTERSECT_END_DEPTH", new cFloat(2.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SurfaceEffectSphere:
                    entity.AddParameter("COLOUR_TINT", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT_OUTER", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FADE_OUT_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SURFACE_WRAP", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROUGHNESS_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPARKLE_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("METAL_STYLE_REFLECTIONS", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SHININESS_OPACITY", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TILING_ZY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TILING_ZX", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TILING_XY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WS_LOCKED", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPARKLE_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVMAP", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVMAP_PERCENT_EMISSIVE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPHERE", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SurfaceEffectBox:
                    entity.AddParameter("COLOUR_TINT", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("COLOUR_TINT_OUTER", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FADE_OUT_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SURFACE_WRAP", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ROUGHNESS_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPARKLE_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("METAL_STYLE_REFLECTIONS", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SHININESS_OPACITY", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TILING_ZY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TILING_ZX", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TILING_XY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FALLOFF", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("WS_LOCKED", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPARKLE_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVMAP", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVMAP_PERCENT_EMISSIVE", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPHERE", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BOX", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SimpleWater:
                    entity.AddParameter("SHININESS", new cFloat(0.8f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("softness_edge", new cFloat(0.005f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FRESNEL_POWER", new cFloat(0.8f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MIN_FRESNEL", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MAX_FRESNEL", new cFloat(5.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LOW_RES_ALPHA_PASS", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ATMOSPHERIC_FOGGING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED", new cFloat(0.01f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_NORMAL_MAPPING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(-0.01f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_MASKING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_MASK", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_MAPPING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CYCLE_TIME", new cFloat(10f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVIRONMENT_MAPPING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVIRONMENT_MAP_MULT", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LOCALISED_ENVIRONMENT_MAPPING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVMAP_SIZE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LOCALISED_ENVMAP_BOX_PROJECTION", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVMAP_BOXPROJ_BB_SCALE", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("REFLECTIVE_MAPPING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("REFLECTION_PERTURBATION_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_INITIAL_COLOUR", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_COLOUR", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_DEPTH", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_END_COLOUR", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_END_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_END_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAUSTIC_TEXTURE", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAUSTIC_TEXTURE_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAUSTIC_REFRACTIONS", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAUSTIC_REFLECTIONS", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAUSTIC_SPEED_SCALAR", new cFloat(20.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAUSTIC_INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAUSTIC_SURFACE_WRAP", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CAUSTIC_HEIGHT", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SimpleRefraction:
                    entity.AddParameter("DISTANCEFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_NORMAL_MAPPING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_MASKING", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_MASK", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DISTORTION_OCCLUSION", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MIN_OCCLUSION_DISTANCE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_UV_ANIMATION", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CYCLE_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ProjectiveDecal:
                    entity.AddParameter("time", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("material", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CameraResource:
                    entity.AddParameter("camera_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_camera_transformation_local", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("camera_transformation", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("fov", new cFloat(45.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("clipping_planes_preset", new cEnum(EnumType.CLIPPING_PLANES_PRESETS, 2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_ghost", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("converge_to_player_camera", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("reset_player_camera_on_exit", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("enable_enter_transition", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("transition_curve_direction", new cEnum(EnumType.TRANSITION_DIRECTION, 4), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("transition_curve_strength", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("enable_exit_transition", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exit_transition_curve_direction", new cEnum(EnumType.TRANSITION_DIRECTION, 4), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exit_transition_curve_strength", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exit_transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exit_transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exit_transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CameraFinder:
                    entity.AddParameter("camera_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CameraBehaviorInterface:
                    entity.AddParameter("behavior_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("priority", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("threshold", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("blend_in", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("blend_out", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.HandCamera:
                    entity.AddParameter("noise_type", new cEnum(EnumType.NOISE_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("frequency", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("damping", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("rotation_intensity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("min_fov_range", new cFloat(45.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_fov_range", new cFloat(45.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("min_noise", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_noise", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CameraShake:
                    entity.AddParameter("shake_type", new cEnum(EnumType.SHAKE_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("shake_frequency", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_rotation_angles", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_position_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("shake_rotation", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("shake_position", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("bone_shaking", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("override_weapon_swing", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("internal_radius", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("external_radius", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("strength_damping", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("explosion_push_back", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("spring_constant", new cFloat(3.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("spring_damping", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CameraPathDriven:
                    entity.AddParameter("path_driven_type", new cEnum(EnumType.PATH_DRIVEN_TYPE, 2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("invert_progression", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position_path_offset", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("target_path_offset", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("animation_duration", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FixedCamera:
                    entity.AddParameter("use_transform_position", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("transform_position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("camera_position", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("camera_target", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("camera_position_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("camera_target_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("apply_target", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("apply_position", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_target_offset", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_position_offset", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.BoneAttachedCamera:
                    entity.AddParameter("position_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("rotation_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("movement_damping", new cFloat(0.6f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("bone_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ControllableRange:
                    entity.AddParameter("min_range_x", new cFloat(-180f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_range_x", new cFloat(180f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("min_range_y", new cFloat(-180f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_range_y", new cFloat(180f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("min_feather_range_x", new cFloat(-180f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_feather_range_x", new cFloat(180f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("min_feather_range_y", new cFloat(-180f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_feather_range_y", new cFloat(180f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("speed_x", new cFloat(30.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("speed_y", new cFloat(30.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("damping_x", new cFloat(0.6f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("damping_y", new cFloat(0.6f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("mouse_speed_x", new cFloat(30.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("mouse_speed_y", new cFloat(30.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.StealCamera:
                    entity.AddParameter("steal_type", new cEnum(EnumType.STEAL_CAMERA_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("check_line_of_sight", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("blend_in_duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FollowCameraModifier:
                    entity.AddParameter("modifier_type", new cEnum(EnumType.FOLLOW_CAMERA_MODIFIERS, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position_offset", new cVector3(new Vector3(0.5f, 1.5f, -3.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("target_offset", new cVector3(new Vector3(0.5f, 1.5f, 0.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("field_of_view", new cFloat(35.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_state", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_state_initial_value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("can_mirror", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_first_person", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("bone_blending_ratio", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("movement_speed", new cFloat(0.7f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("movement_speed_vertical", new cFloat(0.7f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("movement_damping", new cFloat(0.7f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("horizontal_limit_min", new cFloat(-1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("horizontal_limit_max", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("vertical_limit_min", new cFloat(-1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("vertical_limit_max", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("mouse_speed_hori", new cFloat(0.7f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("mouse_speed_vert", new cFloat(0.7f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("acceleration_duration", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("acceleration_ease_in", new cFloat(0.25f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("acceleration_ease_out", new cFloat(0.25f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CameraPath:
                    entity.AddParameter("path_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("path_type", new cEnum(EnumType.CAMERA_PATH_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("path_class", new cEnum(EnumType.CAMERA_PATH_CLASS, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_local", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("relative_position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_loop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CameraAimAssistant:
                    entity.AddParameter("activation_radius", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("inner_radius", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("camera_speed_attenuation", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("min_activation_distance", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("fading_range", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CameraPlayAnimation:
                    entity.AddParameter("data_file", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("start_frame", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("end_frame", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("play_speed", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("loop_play", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("clipping_planes_preset", new cEnum(EnumType.CLIPPING_PLANES_PRESETS, 2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("dof_key", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("shot_number", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("override_dof", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("focal_point_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("bone_to_focus", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CamPeek:
                    entity.AddParameter("range_left", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("range_right", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("range_up", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("range_down", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("range_forward", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("range_backward", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("speed_x", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("speed_y", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("damping_x", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("damping_y", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("focal_distance", new cFloat(8.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("focal_distance_y", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("roll_factor", new cFloat(15.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_ik_solver", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_horizontal_plane", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("stick", new cEnum(EnumType.SIDE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disable_collision_test", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CameraDofController:
                    entity.AddParameter("focal_point_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("bone_to_focus", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Logic_Vent_Entrance:
                    entity.AddParameter("force_stand_on_exit", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CharacterCommand:
                    entity.AddParameter("override_all_ai", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_Follow:
                    entity.AddParameter("idle_stance", new cEnum(EnumType.IDLE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("inner_radius", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("outer_radius", new cFloat(2.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("prefer_traversals", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_FollowUsingJobs:
                    entity.AddParameter("fastest_allowed_move_type", new cEnum(EnumType.MOVE, 3), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("slowest_allowed_move_type", new cEnum(EnumType.MOVE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("centre_job_restart_radius", new cFloat(2.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("inner_radius", new cFloat(4.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("outer_radius", new cFloat(8.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("job_select_radius", new cFloat(6.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("job_cancel_radius", new cFloat(8.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("teleport_required_range", new cFloat(25.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("teleport_radius", new cFloat(20.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("prefer_traversals", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("avoid_player", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("allow_teleports", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("follow_type", new cEnum(EnumType.FOLLOW_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("clamp_speed", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AnimationMask:
                    entity.AddParameter("maskHips", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskTorso", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskNeck", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskHead", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskFace", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskLeftLeg", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskRightLeg", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskLeftArm", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskRightArm", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskLeftHand", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskRightHand", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskLeftFingers", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskRightFingers", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskTail", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskLips", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskEyes", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskLeftShoulder", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskRightShoulder", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskRoot", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskPrecedingLayers", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskSelf", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maskFollowingLayers", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("weight", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_PlayAnimation:
                    entity.AddParameter("AnimationSet", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Animation", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("StartFrame", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EndFrame", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PlayCount", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PlaySpeed", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AllowGravity", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AllowCollision", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Start_Instantly", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AllowInterruption", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("RemoveMotion", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DisableGunLayer", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BlendInTime", new cFloat(0.3f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("GaitSyncStart", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ConvergenceTime", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LocationConvergence", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("OrientationConvergence", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("UseExitConvergence", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ExitConvergenceTime", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Mirror", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FullCinematic", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("RagdollEnabled", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NoIK", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NoFootIK", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NoLayers", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PlayerAnimDrivenView", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ExertionFactor", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AutomaticZoning", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ManualLoading", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("IsCrouchedAnim", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("InitiallyBackstage", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Death_by_ragdoll_only", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("dof_key", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("shot_number", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("UseShivaArms", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_Idle:
                    entity.AddParameter("should_face_target", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("should_raise_gun_while_turning", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("desired_stance", new cEnum(EnumType.CHARACTER_STANCE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("idle_style", new cEnum(EnumType.IDLE_STYLE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lock_cameras", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("anchor", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("start_instantly", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_GoTo:
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("enable_lookaround", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_stopping_anim", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("always_stop_at_radius", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("stop_at_radius_if_lined_up", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("continue_from_previous_move", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("arrived_radius", new cFloat(0.6f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("allow_to_use_vents", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DestinationIsBackstage", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maintain_current_facing", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("start_instantly", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_GoToCover:
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SearchRadius", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("enable_lookaround", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("continue_from_previous_move", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_MoveTowards:
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("never_succeed", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_Die:
                    entity.AddParameter("death_style", new cEnum(EnumType.DEATH_STYLE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_LaunchMeleeAttack:
                    entity.AddParameter("melee_attack_type", new cEnum(EnumType.MELEE_ATTACK_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("enemy_type", new cEnum(EnumType.ENEMY_TYPE, 15), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("melee_attack_index", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("skip_convergence", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_ModifyCombatBehaviour:
                    entity.AddParameter("behaviour_type", new cEnum(EnumType.COMBAT_BEHAVIOUR, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("status", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_HolsterWeapon:
                    entity.AddParameter("should_holster", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("skip_anims", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("equipment_slot", new cEnum(EnumType.EQUIPMENT_SLOT, -2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_player_unarmed_on_holster", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_drop_held_item", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_ForceMeleeAttack:
                    entity.AddParameter("melee_attack_type", new cEnum(EnumType.MELEE_ATTACK_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("enemy_type", new cEnum(EnumType.ENEMY_TYPE, 15), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("melee_attack_index", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_ModifyBreathing:
                    entity.AddParameter("Exhaustion", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_HoldBreath:
                    entity.AddParameter("ExhaustionOnStop", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_DeepCrouch:
                    entity.AddParameter("crouch_amount", new cFloat(0.4f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("smooth_damping", new cFloat(0.4f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("allow_stand_up", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_PlaySecondaryAnimation:
                    entity.AddParameter("AnimationSet", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Animation", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("StartFrame", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EndFrame", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PlayCount", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PlaySpeed", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("StartInstantly", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AllowInterruption", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BlendInTime", new cFloat(0.3f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("GaitSyncStart", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Mirror", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AnimationLayer", new cEnum(EnumType.SECONDARY_ANIMATION_LAYER, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AutomaticZoning", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ManualLoading", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_LocomotionModifier:
                    entity.AddParameter("Can_Run", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Can_Crouch", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Can_Aim", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Can_Injured", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Must_Walk", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Must_Run", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Must_Crouch", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Must_Aim", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Must_Injured", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Is_In_Spacesuit", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetMood:
                    entity.AddParameter("mood", new cEnum(EnumType.MOOD, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("moodIntensity", new cEnum(EnumType.MOOD_INTENSITY, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("timeOut", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_LocomotionEffect:
                    entity.AddParameter("Effect", new cEnum(EnumType.ANIMATION_EFFECT_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_LocomotionDuck:
                    entity.AddParameter("Height", new cEnum(EnumType.DUCK_HEIGHT, 1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_AimAtCurrentTarget:
                    entity.AddParameter("Raise_gun", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CMD_AimAt:
                    entity.AddParameter("Raise_gun", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_current_target", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetTacticalPosition:
                    entity.AddParameter("sweep_type", new cEnum(EnumType.AREA_SWEEP_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("fixed_sweep_radius", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetFocalPoint:
                    entity.AddParameter("priority", new cEnum(EnumType.PRIORITY, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("speed", new cEnum(EnumType.LOOK_SPEED, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("steal_camera", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_of_sight_test", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetAlliance:
                    entity.AddParameter("Alliance", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ALLIANCE_SetDisposition:
                    entity.AddParameter("A", new cEnum(EnumType.ALLIANCE_GROUP, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("B", new cEnum(EnumType.ALLIANCE_GROUP, 5), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Disposition", new cEnum(EnumType.ALLIANCE_STANCE, 1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetInvincibility:
                    entity.AddParameter("damage_mode", new cEnum(EnumType.DAMAGE_MODE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetHealth:
                    entity.AddParameter("HealthPercentage", new cInteger(100), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("UsePercentageOfCurrentHeath", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetDebugDisplayName:
                    entity.AddParameter("DebugName", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_TakeDamage:
                    entity.AddParameter("Damage", new cInteger(100), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DamageIsAPercentage", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetSubModelVisibility:
                    entity.AddParameter("is_visible", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("matching", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetHeadVisibility:
                    entity.AddParameter("is_visible", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetFacehuggerAggroRadius:
                    entity.AddParameter("radius", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_DamageMonitor:
                    entity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, -65536), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_DeathMonitor:
                    entity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, -65536), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_TorchMonitor:
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_VentMonitor:
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CharacterTypeMonitor:
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Convo:
                    entity.AddParameter("alwaysTalkToPlayerIfPresent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("playerCanJoin", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("playerCanLeave", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("positionNPCs", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("circularShape", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("convoPosition", new cString(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("personalSpaceRadius", new cFloat(0.4f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_NotifyDynamicDialogueEvent:
                    entity.AddParameter("DialogueEvent", new cEnum(EnumType.DIALOGUE_NPC_EVENT, -1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_Squad_DialogueMonitor:
                    entity.AddParameter("squad_coordinator", new cString(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_Group_DeathCounter:
                    entity.AddParameter("TriggerThreshold", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_Group_Death_Monitor:
                    entity.AddParameter("squad_coordinator", new cString(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CheckAllNPCs", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SenseLimiter:
                    entity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_ResetSensesAndMemory:
                    entity.AddParameter("ResetMenaceToFull", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ResetSensesLimiters", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetupMenaceManager:
                    entity.AddParameter("AgressiveMenace", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ProgressionFraction", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ResetMenaceMeter", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_AlienConfig:
                    entity.AddParameter("AlienConfigString", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetSenseSet:
                    entity.AddParameter("SenseSet", new cEnum(EnumType.SENSE_SET, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_GetLastSensedPositionOfTarget:
                    entity.AddParameter("MaxTimeSince", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_Gain_Aggression_In_Radius:
                    entity.AddParameter("AggressionGain", new cEnum(EnumType.AGGRESSION_GAIN, 1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Explosion_AINotifier:
                    entity.AddParameter("ExplosionPos", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 12), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_Highest_Awareness_Monitor:
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CheckAllNPCs", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DEBUG_SenseLevels:
                    entity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_FakeSense:
                    entity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ForceThreshold", new cEnum(EnumType.THRESHOLD_QUALIFIER, 2), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SuspiciousItem:
                    entity.AddParameter("Item", new cEnum(EnumType.SUSPICIOUS_ITEM, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("InitialReactionValidStartDuration", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FurtherReactionValidStartDuration", new cFloat(6.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("RetriggerDelay", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Trigger", new cEnum(EnumType.SUSPICIOUS_ITEM_TRIGGER, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ShouldMakeAggressive", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MaxGroupMembersInteract", new cInteger(2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SystematicSearchRadius", new cFloat(8.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AllowSamePriorityToOveride", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("UseSamePriorityCloserDistanceConstraint", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SamePriorityCloserDistanceConstraint", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("UseSamePriorityRecentTimeConstraint", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SamePriorityRecentTimeConstraint", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BehaviourTreePriority", new cEnum(EnumType.SUSPICIOUS_ITEM_BEHAVIOUR_TREE_PRIORITY, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("InteruptSubPriority", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DetectableByBackstageAlien", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DoIntialReaction", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MoveCloseToSuspectPosition", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DoCloseToReaction", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DoCloseToWaitForGroupMembers", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DoSystematicSearch", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("GroupNotify", new cEnum(EnumType.SUSPICIOUS_ITEM_STAGE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DoIntialReactionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MoveCloseToSuspectPositionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DoCloseToReactionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DoCloseToWaitForGroupMembersSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DoSystematicSearchSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetAlienDevelopmentStage:
                    entity.AddParameter("AlienStage", new cEnum(EnumType.ALIEN_DEVELOPMENT_MANAGER_STAGES, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Reset", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_IsWithinRange:
                    entity.AddParameter("Range_test_shape", new cEnum(EnumType.RANGE_TEST_SHAPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_ForceCombatTarget:
                    entity.AddParameter("LockOtherAttackersOut", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_SetTorch:
                    entity.AddParameter("TorchOn", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetAutoTorchMode:
                    entity.AddParameter("AutoUseTorchInDark", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_AreaBox:
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_MeleeContext:
                    entity.AddParameter("Context_Type", new cEnum(EnumType.MELEE_CONTEXT_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetAlertness:
                    entity.AddParameter("AlertState", new cEnum(EnumType.ALERTNESS_STATE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetAgressionProgression:
                    entity.AddParameter("allow_progression", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetLocomotionTargetSpeed:
                    entity.AddParameter("Speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetGunAimMode:
                    entity.AddParameter("AimingMode", new cEnum(EnumType.NPC_GUN_AIM_MODE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_set_behaviour_tree_flags:
                    entity.AddParameter("BehaviourTreeFlag", new cEnum(EnumType.BEHAVIOUR_TREE_FLAGS, 2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FlagSetting", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetHidingSearchRadius:
                    entity.AddParameter("Radius", new cFloat(15.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_WithdrawAlien:
                    entity.AddParameter("allow_any_searches_to_complete", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("permanent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("killtraps", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("initial_radius", new cFloat(15.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("timed_out_radius", new cFloat(3.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("time_to_force", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_behaviour_monitor:
                    entity.AddParameter("behaviour", new cEnum(EnumType.BEHAVIOR_TREE_BRANCH_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_multi_behaviour_monitor:
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_ambush_monitor:
                    entity.AddParameter("ambush_type", new cEnum(EnumType.AMBUSH_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_navmesh_type_monitor:
                    entity.AddParameter("nav_mesh_type", new cEnum(EnumType.NAV_MESH_AREA_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_HasWeaponOfType:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("check_if_weapon_draw", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_TriggerAimRequest:
                    entity.AddParameter("Raise_gun", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_current_target", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("clamp_angle", new cFloat(30.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("clear_current_requests", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_TriggerShootRequest:
                    entity.AddParameter("empty_current_clip", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("shot_count", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("clear_current_requests", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Squad_SetMaxEscalationLevel:
                    entity.AddParameter("max_level", new cEnum(EnumType.NPC_AGGRO_LEVEL, 5), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("squad_coordinator", new cString(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Chr_PlayerCrouch:
                    entity.AddParameter("crouch", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TorchDynamicMovement:
                    entity.AddParameter("max_spatial_velocity", new cFloat(5.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_angular_velocity", new cFloat(30.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_position_displacement", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_target_displacement", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position_damping", new cFloat(0.6f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("target_damping", new cFloat(0.6f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.EQUIPPABLE_ITEM:
                    entity.AddParameter("character_animation_context", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_activate_animation_context", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("left_handed", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("inventory_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("equipment_slot", new cEnum(EnumType.EQUIPMENT_SLOT, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("holsters_on_owner", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("holster_node", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("holster_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("weapon_handedness", new cEnum(EnumType.WEAPON_HANDEDNESS, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AIMED_ITEM:
                    entity.AddParameter("fixed_target_distance_for_local_player", new cFloat(6.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.MELEE_WEAPON:
                    entity.AddParameter("normal_attack_damage", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("power_attack_damage", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position_input", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AIMED_WEAPON:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("requires_turning_on", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ejectsShellsOnFiring", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("aim_assist_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("default_ammo_type", new cEnum(EnumType.AMMO_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("starting_ammo", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("clip_size", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("consume_ammo_over_time_when_turned_on", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_auto_shots_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_manual_shots_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("wind_down_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("maximum_continous_fire_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("overheat_recharge_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("automatic_firing", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("overheats", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("charged_firing", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("charging_duration", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("min_charge_to_fire", new cFloat(0.3f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("overcharge_timer", new cFloat(2.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("charge_noise_start_time", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("reloadIndividualAmmo", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alwaysDoFullReloadOfClips", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("movement_accuracy_penalty_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("aim_rotation_accuracy_penalty_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("accuracy_penalty_per_shot", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("accuracy_accumulated_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("player_exposed_accuracy_penalty_per_shot", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("player_exposed_accuracy_accumulated_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("recoils_on_fire", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alien_threat_aware", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerWeaponMonitor:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ammo_percentage_in_clip", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerDiscardsWeapons:
                    entity.AddParameter("discard_pistol", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_shotgun", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_flamethrower", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_boltgun", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_cattleprod", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_melee", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerDiscardsItems:
                    entity.AddParameter("discard_ieds", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_medikits", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_ammo", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_flares_and_lights", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_materials", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_batteries", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerDiscardsTools:
                    entity.AddParameter("discard_motion_tracker", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_cutting_torch", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_hacking_tool", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("discard_keycard", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_GiveToCharacter:
                    entity.AddParameter("is_holstered", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_GiveToPlayer:
                    entity.AddParameter("weapon", new cEnum(EnumType.EQUIPMENT_SLOT, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("holster", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("starting_ammo", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactEffect:
                    entity.AddParameter("Type", new cEnum(EnumType.WEAPON_IMPACT_EFFECT_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Orientation", new cEnum(EnumType.WEAPON_IMPACT_EFFECT_ORIENTATION, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Priority", new cInteger(16), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SafeDistant", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("LifeTime", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_damage_offset", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("RandomRotation", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactFilter:
                    entity.AddParameter("PhysicMaterial", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_AttackerFilter:
                    entity.AddParameter("filter", new cBool(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_TargetObjectFilter:
                    entity.AddParameter("filter", new cBool(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_DamageFilter:
                    entity.AddParameter("damage_threshold", new cInteger(100), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_MultiFilter:
                    entity.AddParameter("AttackerFilter", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TargetFilter", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DamageThreshold", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, 33554432), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("UseAmmoFilter", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 22), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactCharacterFilter:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_body_location", new cEnum(EnumType.IMPACT_CHARACTER_BODY_LOCATION_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_Effect:
                    entity.AddParameter("LifeTime", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_AmmoTypeFilter:
                    entity.AddParameter("AmmoType", new cEnum(EnumType.DAMAGE_EFFECTS, 33554432), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactAngleFilter:
                    entity.AddParameter("ReferenceAngle", new cFloat(60f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactOrientationFilter:
                    entity.AddParameter("ThresholdAngle", new cFloat(15f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Orientation", new cEnum(EnumType.WEAPON_IMPACT_FILTER_ORIENTATION, 2), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.EFFECT_ImpactGenerator:
                    entity.AddParameter("trigger_on_reset", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("min_distance", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("distance", new cFloat(3.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_count", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("spread", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("skip_characters", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.EFFECT_EntityGenerator:
                    entity.AddParameter("trigger_on_reset", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("spread", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_min", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_max", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_offset_XY_min", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_offset_XY_max", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_offset_Z_min", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_offset_Z_max", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lifetime_min", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lifetime_max", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.EFFECT_DirectionalPhysics:
                    entity.AddParameter("relative_direction", new cVector3(new Vector3(1.0f, 0.0f, 0.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("effect_distance", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("angular_falloff", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("min_force", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_force", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlatformConstantBool:
                    entity.AddParameter("NextGen", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("X360", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PS3", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlatformConstantInt:
                    entity.AddParameter("NextGen", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("X360", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PS3", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlatformConstantFloat:
                    entity.AddParameter("NextGen", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("X360", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PS3", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VariableBool:
                    entity.AddParameter("initial_value", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VariableInt:
                    entity.AddParameter("initial_value", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VariableFloat:
                    entity.AddParameter("initial_value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VariableString:
                    entity.AddParameter("initial_value", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VariableVector:
                    entity.AddParameter("initial_x", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("initial_y", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("initial_z", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VariableFlashScreenColour:
                    entity.AddParameter("flash_layer_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VariableHackingConfig:
                    entity.AddParameter("nodes", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("sensors", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("victory_nodes", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("victory_sensors", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VariableEnum:
                    entity.AddParameter("initial_value", new cEnum(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VariableAnimationInfo:
                    entity.AddParameter("AnimationSet", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Animation", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ExternalVariableBool:
                    entity.AddParameter("game_variable", new cString("DLC_Preorder_Weapon"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NonPersistentBool:
                    entity.AddParameter("initial_value", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NonPersistentInt:
                    entity.AddParameter("initial_value", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GameDVR:
                    entity.AddParameter("start_time", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("moment_ID", new cEnum(EnumType.GAME_CLIP, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Zone:
                    entity.AddParameter("name", new cString(""), ParameterVariant.PARAMETER, overwrite); // manually added
                    entity.AddParameter("suspend_on_unload", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("space_visible", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ZoneLink:
                    entity.AddParameter("cost", new cInteger(6), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ZoneExclusionLink:
                    entity.AddParameter("exclude_streaming", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FlushZoneCache:
                    entity.AddParameter("CurrentGen", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NextGen", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.LogicSwitch:
                    entity.AddParameter("initial_value", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FloatMultiply_All:
                    entity.AddParameter("Invert", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FloatModulate:
                    entity.AddParameter("wave_shape", new cEnum(EnumType.WAVE_SHAPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("frequency", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("phase", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("amplitude", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("bias", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FloatModulateRandom:
                    entity.AddParameter("switch_on_anim", new cEnum(EnumType.LIGHT_TRANSITION, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("switch_on_delay", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("switch_on_custom_frequency", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("switch_on_duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("switch_off_anim", new cEnum(EnumType.LIGHT_TRANSITION, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("switch_off_custom_frequency", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("switch_off_duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("behaviour_anim", new cEnum(EnumType.LIGHT_ANIM, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("behaviour_frequency", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("behaviour_frequency_variance", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("behaviour_offset", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("pulse_modulation", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("oscillate_range_min", new cFloat(0.75f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("sparking_speed", new cFloat(0.9f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("blink_rate", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("blink_range_min", new cFloat(0.01f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flicker_rate", new cFloat(0.75f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flicker_off_rate", new cFloat(0.15f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flicker_range_min", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flicker_off_range_min", new cFloat(0.01f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disable_behaviour", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FloatLinearInterpolateTimed:
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Time", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FloatLinearInterpolateSpeed:
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FloatLinearInterpolateSpeedAdvanced:
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Min_Value", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Max_Value", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterAbsorber:
                    entity.AddParameter("factor", new cFloat(0.95f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("start_value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("input", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.IntegerAnalyse:
                    entity.AddParameter("Val0", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Val1", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Val2", new cInteger(2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Val3", new cInteger(3), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Val4", new cInteger(4), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Val5", new cInteger(5), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Val6", new cInteger(6), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Val7", new cInteger(7), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Val8", new cInteger(8), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Val9", new cInteger(9), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetEnum:
                    entity.AddParameter("initial_value", new cEnum(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetString:
                    entity.AddParameter("initial_value", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetPosition:
                    entity.AddParameter("set_on_reset", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VectorLinearInterpolateTimed:
                    entity.AddParameter("Time", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VectorLinearInterpolateSpeed:
                    entity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.MoveInTime:
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SmoothMove:
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RotateInTime:
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("time_X", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("time_Y", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("time_Z", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RotateAtSpeed:
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("speed_X", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("speed_Y", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("speed_Z", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetLocationAndOrientation:
                    entity.AddParameter("axis_is", new cEnum(EnumType.ORIENTATION_AXIS, 2), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ApplyRelativeTransform:
                    entity.AddParameter("use_trigger_entity", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RandomFloat:
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RandomInt:
                    entity.AddParameter("Min", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Max", new cInteger(100), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RandomVector:
                    entity.AddParameter("MinX", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MaxX", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MinY", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MaxY", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MinZ", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MaxZ", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Normalised", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RandomSelect:
                    entity.AddParameter("Seed", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerRandom:
                    entity.AddParameter("Num", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerRandomSequence:
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Persistent_TriggerRandomSequence:
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerWeightedRandom:
                    entity.AddParameter("Weighting_01", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Weighting_02", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Weighting_03", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Weighting_04", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Weighting_05", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Weighting_06", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Weighting_07", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Weighting_08", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Weighting_09", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Weighting_10", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("allow_same_pin_in_succession", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlayEnvironmentAnimation:
                    entity.AddParameter("animation_info", new cResource(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AnimationSet", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Animation", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("start_frame", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("end_frame", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("play_speed", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("shot_number", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CAGEAnimation:
                    entity.AddParameter("use_external_time", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("rewind_on_stop", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("jump_to_the_end", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("playspeed", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("anim_length", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_cinematic_skippable", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("skippable_timer", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("capture_video", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("capture_clip_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.MultitrackLoop:
                    entity.AddParameter("start_time", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("end_time", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerSequence:
                    entity.AddParameter("trigger_mode", new cEnum(EnumType.ANIM_MODE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("random_seed", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_random_intervals", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("no_duplicates", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("interval_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Checkpoint:
                    entity.AddParameter("is_first_checkpoint", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_first_autorun_checkpoint", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("section", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("checkpoint_type", new cEnum(EnumType.CHECKPOINT_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetAsActiveMissionLevel:
                    entity.AddParameter("clear_level", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugLoadCheckpoint:
                    entity.AddParameter("previous_checkpoint", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GameStateChanged:
                    entity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DisplayMessage:
                    entity.AddParameter("title_id", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("message_id", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DisplayMessageWithCallbacks:
                    entity.AddParameter("title_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("message_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("yes_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("no_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("cancel_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("yes_button", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("no_button", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("cancel_button", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.LevelInfo:
                    entity.AddParameter("save_level_name_id", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugCheckpoint:
                    entity.AddParameter("section", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("level_reset", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Benchmark:
                    entity.AddParameter("benchmark_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("save_stats", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugTextStacking:
                    entity.AddParameter("text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("size", new cInteger(20), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ci_type", new cEnum(EnumType.CI_MESSAGE_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("needs_debug_opt_to_render", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugText:
                    entity.AddParameter("text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("size", new cInteger(20), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alignment", new cEnum(EnumType.TEXT_ALIGNMENT, 4), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cFloat(5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("pause_game", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("cancel_pause_with_button_press", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("priority", new cInteger(100), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ci_type", new cEnum(EnumType.CI_MESSAGE_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TutorialMessage:
                    entity.AddParameter("text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("text_list", new cString("A1_G0000_RIP_0010A"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("show_animation", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugEnvironmentMarker:
                    entity.AddParameter("text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("size", new cFloat(20f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cFloat(5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("scale_with_distance", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_string_length", new cInteger(10), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("scroll_speed", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("show_distance_from_target", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugPositionMarker:
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugCaptureScreenShot:
                    entity.AddParameter("wait_for_streamer", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("capture_filename", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("fov", new cFloat(45f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("near", new cFloat(0.01f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("far", new cFloat(200.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugCaptureCorpse:
                    entity.AddParameter("corpse_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugMenuToggle:
                    entity.AddParameter("debug_variable", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("value", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Master:
                    entity.AddParameter("disable_display", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disable_collision", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disable_simulation", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ThinkOnce:
                    entity.AddParameter("use_random_start", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("random_start_delay", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Thinker:
                    entity.AddParameter("delay_between_triggers", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_continuous", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_random_start", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("random_start_delay", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("total_thinking_time", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AllPlayersReady:
                    entity.AddParameter("activation_delay", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RespawnConfig:
                    entity.AddParameter("min_dist", new cFloat(2.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("preferred_dist", new cFloat(4.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_dist", new cFloat(30.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("respawn_mode", new cEnum(EnumType.RESPAWN_MODE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("respawn_wait_time", new cInteger(10), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("uncollidable_time", new cInteger(5), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_default", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NetworkedTimer:
                    entity.AddParameter("duration", new cFloat(5.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugObjectMarker:
                    entity.AddParameter("marked_object", new cString(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("marked_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.EggSpawner:
                    entity.AddParameter("egg_position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("hostile_egg", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerObjectsFilterCounter:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerContainerObjectsFilterCounter:
                    entity.AddParameter("container", new cString(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerDamaged:
                    entity.AddParameter("threshold", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerBindAllCharactersOfType:
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 2), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerDelay:
                    entity.AddParameter("Hrs", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Min", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Sec", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerSwitch:
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("loop", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerSelect:
                    entity.AddParameter("index", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerSelect_Direct:
                    entity.AddParameter("Changes_only", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerCheckDifficulty:
                    entity.AddParameter("DifficultyLevel", new cEnum(EnumType.DIFFICULTY_SETTING_TYPE, 2), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerSync:
                    entity.AddParameter("reset_on_trigger", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.LogicAll:
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("reset_on_trigger", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Logic_MultiGate:
                    entity.AddParameter("trigger_pin", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Counter:
                    entity.AddParameter("is_limitless", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_limit", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.LogicCounter:
                    entity.AddParameter("is_limitless", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_limit", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("non_persistent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Door:
                    entity.AddParameter("unlocked_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("locked_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("icon_keyframe", new cEnum(EnumType.UI_ICON_ICON, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("detach_anim", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("invert_nav_mesh_barrier", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PadLightBar:
                    entity.AddParameter("colour", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PadRumbleImpulse:
                    entity.AddParameter("low_frequency_rumble", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("high_frequency_rumble", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("left_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("right_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("aim_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("shoot_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerViewCone:
                    entity.AddParameter("target_offset", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("visible_area_horizontal", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("visible_area_vertical", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerCameraViewCone:
                    entity.AddParameter("use_camera_fov", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("target_offset", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("visible_area_horizontal", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("visible_area_vertical", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerCameraViewConeMulti:
                    entity.AddParameter("number_of_inputs", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_camera_fov", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("visible_area_horizontal", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("visible_area_vertical", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerCameraVolume:
                    entity.AddParameter("start_radius", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Character:
                    entity.AddParameter("PopToNavMesh", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disable_dead_container", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("allow_container_without_death", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("container_interaction_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("anim_set", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("anim_tree_set", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("attribute_set", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_player", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_backstage", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("force_backstage_on_respawn", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS, 3), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("dialogue_voice", new cEnum(EnumType.DIALOGUE_VOICE_ACTOR, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("spawn_id", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("display_model", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("reference_skeleton", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("torso_sound", new cString("Shirt"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("leg_sound", new cString("Jeans"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("footwear_sound", new cString("Flats"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("custom_character_type", new cEnum(EnumType.CUSTOM_CHARACTER_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("custom_character_accessory_override", new cEnum(EnumType.CUSTOM_CHARACTER_ACCESSORY_OVERRIDE, -1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("custom_character_population_type", new cEnum(EnumType.CUSTOM_CHARACTER_POPULATION, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("named_custom_character", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("named_custom_character_assets_set", new cEnum(EnumType.CUSTOM_CHARACTER_ASSETS, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("gcip_distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("inventory_set", new cEnum(EnumType.PLAYER_INVENTORY_SET, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RegisterCharacterModel:
                    entity.AddParameter("display_model", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("reference_skeleton", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterIsEnemyOfCharacter:
                    entity.AddParameter("use_alliance_at_death", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterIsEnemyOfAllianceGroup:
                    entity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterIsFacingTarget:
                    entity.AddParameter("tolerance", new cFloat(45f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterBelongsToAlliance:
                    entity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterHasWeaponOfType:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterHasWeaponEquipped:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterIsCharacterClass:
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS, 3), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterIsCharacterClassCombo:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterIsInAlertnessState:
                    entity.AddParameter("AlertState", new cEnum(EnumType.ALERTNESS_STATE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterIsInLocomotionState:
                    entity.AddParameter("State", new cEnum(EnumType.LOCOMOTION_STATE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterIsPlatform:
                    entity.AddParameter("Platform", new cEnum(EnumType.PLATFORM_TYPE, 5), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterIsUsingDevice:
                    entity.AddParameter("Device", new cEnum(EnumType.INPUT_DEVICE_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterSmallestUsedDifficulty:
                    entity.AddParameter("difficulty", new cEnum(EnumType.DIFFICULTY_SETTING_TYPE, 2), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterHasPlayerCollectedIdTag:
                    entity.AddParameter("tag_id", new cString("IDTAGABC"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FilterHasBehaviourTreeFlagSet:
                    entity.AddParameter("BehaviourTreeFlag", new cEnum(EnumType.BEHAVIOUR_TREE_FLAGS, 2), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.JOB_Idle:
                    entity.AddParameter("task_operation_mode", new cEnum(EnumType.TASK_OPERATION_MODE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("should_perform_all_tasks", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Task:
                    entity.AddParameter("should_stop_moving_when_reached", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("should_orientate_when_reached", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("reached_distance_threshold", new cFloat(0.6f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("selection_priority", new cEnum(EnumType.TASK_PRIORITY, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("timeout", new cFloat(5.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("always_on_tracker", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FlareTask:
                    entity.AddParameter("filter_options", new cEnum(EnumType.TASK_CHARACTER_CLASS_FILTER, 1024), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.IdleTask:
                    entity.AddParameter("should_auto_move_to_position", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ignored_for_auto_selection", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("has_pre_move_script", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("has_interrupt_script", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("filter_options", new cEnum(EnumType.TASK_CHARACTER_CLASS_FILTER, 1024), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FollowTask:
                    entity.AddParameter("can_initially_end_early", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("stop_radius", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_ForceNextJob:
                    entity.AddParameter("ShouldInterruptCurrentTask", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Job", new cString(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("InitialTask", new cString(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetRateOfFire:
                    entity.AddParameter("MinTimeBetweenShots", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("RandomRange", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetFiringRhythm:
                    entity.AddParameter("MinShootingTime", new cFloat(3.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("RandomRangeShootingTime", new cFloat(2.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MinNonShootingTime", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("RandomRangeNonShootingTime", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MinCoverNonShootingTime", new cFloat(3.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("RandomRangeCoverNonShootingTime", new cFloat(2.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_SetFiringAccuracy:
                    entity.AddParameter("Accuracy", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerBindAllNPCs:
                    entity.AddParameter("radius", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Trigger_AudioOccluded:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Range", new cFloat(30.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SwitchLevel:
                    entity.AddParameter("level_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundPlaybackBaseClass:
                    entity.AddParameter("sound_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("argument_1", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("argument_2", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("argument_3", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("argument_4", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("argument_5", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("object_position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("restore_on_checkpoint", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Sound:
                    entity.AddParameter("stop_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_static_ambience", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("start_on", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("multi_trigger", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_multi_emitter", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("create_sound_object", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("switch_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("switch_value", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("last_gen_enabled", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("resume_after_suspended", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Speech:
                    entity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("queue_time", new cFloat(4f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NPC_DynamicDialogueGlobalRange:
                    entity.AddParameter("dialogue_range", new cFloat(35f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CHR_PlayNPCBark:
                    entity.AddParameter("queue_time", new cFloat(4f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("sound_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("dialogue_mode", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("action", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SpeechScript:
                    entity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 2), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_01_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_01_character", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_02_delay", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_02_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_02_character", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_03_delay", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_03_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_03_character", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_04_delay", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_04_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_04_character", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_05_delay", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_05_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_05_character", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_06_delay", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_06_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_06_character", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_07_delay", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_07_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_07_character", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_08_delay", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_08_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_08_character", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_09_delay", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_09_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_09_character", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_10_delay", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_10_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("line_10_character", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("restore_on_checkpoint", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundNetworkNode:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundEnvironmentMarker:
                    entity.AddParameter("reverb_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("on_enter_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("on_exit_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("linked_network_occlusion_scaler", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("room_size", new cString("Medium_Room"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("disable_network_creation", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundEnvironmentZone:
                    entity.AddParameter("reverb_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("priority", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundLoadBank:
                    entity.AddParameter("trigger_via_pin", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("memory_pool", new cEnum(EnumType.SOUND_POOL, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundLoadSlot:
                    entity.AddParameter("sound_bank", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("memory_pool", new cEnum(EnumType.SOUND_POOL, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundSetRTPC:
                    entity.AddParameter("rtpc_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("smooth_rate", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("start_on", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundSetState:
                    entity.AddParameter("state_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("state_value", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundSetSwitch:
                    entity.AddParameter("switch_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("switch_value", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundImpact:
                    entity.AddParameter("sound_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("argument_1", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("argument_2", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("argument_3", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundBarrier:
                    entity.AddParameter("default_open", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("band_aid", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("override_value", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.MusicController:
                    entity.AddParameter("music_start_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("music_end_event", new cString("MusicController_Music_End"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("music_restart_event", new cString("MusicController_Music_Restart"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("layer_control_rtpc", new cString("Music_All_Layers"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("smooth_rate", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alien_max_distance", new cFloat(50f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("object_max_distance", new cFloat(50f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.MusicTrigger:
                    entity.AddParameter("music_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("smooth_rate", new cFloat(-1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("queue_time", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("interrupt_all", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_once", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("rtpc_set_mode", new cEnum(EnumType.MUSIC_RTPC_MODE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("rtpc_target_value", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("rtpc_duration", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("rtpc_set_return_mode", new cEnum(EnumType.MUSIC_RTPC_MODE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("rtpc_return_value", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundLevelInitialiser:
                    entity.AddParameter("auto_generate_networks", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("network_node_min_spacing", new cFloat(2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("network_node_max_visibility", new cFloat(10f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("network_node_ceiling_height", new cFloat(50f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundMissionInitialiser:
                    entity.AddParameter("human_max_threat", new cFloat(0.7f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("android_max_threat", new cFloat(0.8f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alien_max_threat", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundRTPCController:
                    entity.AddParameter("stealth_default_on", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("threat_default_on", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundTimelineTrigger:
                    entity.AddParameter("sound_event", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("trigger_time", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundPhysicsInitialiser:
                    entity.AddParameter("contact_max_timeout", new cFloat(0.33f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("contact_smoothing_attack_rate", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("contact_smoothing_decay_rate", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("contact_min_magnitude", new cFloat(0.01f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("contact_max_trigger_distance", new cFloat(25f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("impact_min_speed", new cFloat(0.2f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("impact_max_trigger_distance", new cFloat(10f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ragdoll_min_timeout", new cFloat(0.25f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ragdoll_min_speed", new cFloat(1f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SoundPlayerFootwearOverride:
                    entity.AddParameter("footwear_sound", new cString("Trainers"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerHasEnoughItems:
                    entity.AddParameter("quantity", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.InventoryItem:
                    entity.AddParameter("item", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("quantity", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("clear_on_collect", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("gcip_instances_count", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PickupSpawner:
                    entity.AddParameter("item_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.MultiplePickupSpawner:
                    entity.AddParameter("item_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetupGCDistribution:
                    entity.AddParameter("c00", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("c01", new cFloat(0.969f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("c02", new cFloat(0.882f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("c03", new cFloat(0.754f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("c04", new cFloat(0.606f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("c05", new cFloat(0.457f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("c06", new cFloat(0.324f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("c07", new cFloat(0.216f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("c08", new cFloat(0.135f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("c09", new cFloat(0.079f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("c10", new cFloat(0.043f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("minimum_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("divisor", new cFloat(20.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lookup_decrease_time", new cFloat(15.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lookup_point_increase", new cInteger(2), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AllocateGCItemsFromPool:
                    entity.AddParameter("force_usage_count", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AllocateGCItemFromPoolBySubset:
                    entity.AddParameter("force_usage", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.QueryGCItemPool:
                    entity.AddParameter("item_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RemoveFromGCItemPool:
                    entity.AddParameter("item_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("gcip_instances_to_remove", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FlashScript:
                    entity.AddParameter("filename", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("layer_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("target_texture_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("type", new cEnum(EnumType.FLASH_SCRIPT_RENDER_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UI_KeyGate:
                    entity.AddParameter("code", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("carduid", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("key_type", new cEnum(EnumType.UI_KEYGATE_TYPE, 1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RTT_MoviePlayer:
                    entity.AddParameter("filename", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("layer_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("target_texture_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.MoviePlayer:
                    entity.AddParameter("trigger_end_on_skipped", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("filename", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("skippable", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("enable_debug_skip", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DurangoVideoCapture:
                    entity.AddParameter("clip_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.VideoCapture:
                    entity.AddParameter("clip_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("only_in_capture_mode", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FlashInvoke:
                    entity.AddParameter("method", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("invoke_type", new cEnum(EnumType.FLASH_INVOKE_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("int_argument_0", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("int_argument_1", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("int_argument_2", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("int_argument_3", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("float_argument_0", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("float_argument_1", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("float_argument_2", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("float_argument_3", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FlashCallback:
                    entity.AddParameter("callback_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PopupMessage:
                    entity.AddParameter("header_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("main_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cFloat(5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("sound_event", new cEnum(EnumType.POPUP_MESSAGE_SOUND, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("icon_keyframe", new cEnum(EnumType.POPUP_MESSAGE_ICON, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UIBreathingGameIcon:
                    entity.AddParameter("fill_percentage", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("prompt_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UI_Icon:
                    entity.AddParameter("unlocked_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("locked_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("action_text", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("icon_keyframe", new cEnum(EnumType.UI_ICON_ICON, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("can_be_used", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("category", new cEnum(EnumType.PICKUP_CATEGORY, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("push_hold_time", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UI_Container:
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_temporary", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UI_ReactionGame:
                    entity.AddParameter("exit_on_fail", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UI_Keypad:
                    entity.AddParameter("code", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exit_on_fail", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.HackingGame:
                    entity.AddParameter("hacking_difficulty", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("auto_exit", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetHackingToolLevel:
                    entity.AddParameter("level", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TerminalContent:
                    entity.AddParameter("content_title", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("content_decoration_title", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("additional_info", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_connected_to_audio_log", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_triggerable", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_single_use", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TerminalFolder:
                    entity.AddParameter("code", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("folder_title", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("folder_lock_type", new cEnum(EnumType.FOLDER_LOCK_TYPE, 1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AccessTerminal:
                    entity.AddParameter("location", new cEnum(EnumType.TERMINAL_LOCATION, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetGatingToolLevel:
                    entity.AddParameter("level", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GetGatingToolLevel:
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GetPlayerHasGatingTool:
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GetPlayerHasKeycard:
                    entity.AddParameter("card_uid", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetPlayerHasKeycard:
                    entity.AddParameter("card_uid", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetPlayerHasGatingTool:
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CollectSevastopolLog:
                    entity.AddParameter("log_id", new cString("SEV001"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CollectNostromoLog:
                    entity.AddParameter("log_id", new cString("NOS001"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CollectIDTag:
                    entity.AddParameter("tag_id", new cString("IDTAGABC"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.StartNewChapter:
                    entity.AddParameter("chapter", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UnlockLogEntry:
                    entity.AddParameter("entry", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.MapAnchor:
                    entity.AddParameter("keyframe", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("keyframe1", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("keyframe2", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("keyframe3", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("keyframe4", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("keyframe5", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_default_for_items", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.MapItem:
                    entity.AddParameter("item_type", new cEnum(EnumType.MAP_ICON_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("map_keyframe", new cString("RnD_HzdLab_1"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UnlockMapDetail:
                    entity.AddParameter("map_keyframe", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("details", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RewireSystem:
                    entity.AddParameter("display_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("display_name_enum", new cEnum(EnumType.REWIRE_SYSTEM_NAME, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("on_by_default", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("running_cost", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("system_type", new cEnum(EnumType.REWIRE_SYSTEM_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("map_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("element_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RewireLocation:
                    entity.AddParameter("element_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("display_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RewireAccess_Point:
                    entity.AddParameter("additional_power", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("display_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("map_element_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("map_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("map_x_offset", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("map_y_offset", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("map_zoom", new cFloat(3.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RewireTotalPowerResource:
                    entity.AddParameter("total_power", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Rewire:
                    entity.AddParameter("map_keyframe", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("total_power", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetMotionTrackerRange:
                    entity.AddParameter("range", new cFloat(20f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetGamepadAxes:
                    entity.AddParameter("invert_x", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("invert_y", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("save_settings", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetGameplayTips:
                    entity.AddParameter("tip_string_id", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GameOver:
                    entity.AddParameter("tip_string_id", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("default_tips_enabled", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("level_tips_enabled", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GameplayTip:
                    entity.AddParameter("string_id", new cString("AI_UI_GAMEOVER_CUSTOM_TIP_DEFAULT"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Minigames:
                    entity.AddParameter("game_inertial_damping_active", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("game_green_text_active", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("game_yellow_chart_active", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("game_overloc_fail_active", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("game_docking_active", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("game_environ_ctr_active", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("config_pass_number", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("config_fail_limit", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("config_difficulty", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetBlueprintInfo:
                    entity.AddParameter("type", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("level", new cEnum(EnumType.BLUEPRINT_LEVEL, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("available", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GetBlueprintLevel:
                    entity.AddParameter("type", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GetBlueprintAvailable:
                    entity.AddParameter("type", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetObjectiveCompleted:
                    entity.AddParameter("objective_id", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GoToFrontend:
                    entity.AddParameter("frontend_state", new cEnum(EnumType.FRONTEND_STATE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TriggerLooper:
                    entity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("delay", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLadder:
                    entity.AddParameter("RungSpacing", new cFloat(0.33f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousPipe:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLedge:
                    entity.AddParameter("Dangling", new cEnum(EnumType.AUTODETECT, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Sidling", new cEnum(EnumType.AUTODETECT, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousClimbingWall:
                    entity.AddParameter("Dangling", new cEnum(EnumType.AUTODETECT, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousCinematicSidle:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousBalanceBeam:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousTightGap:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentEntrance:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentExit:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentExit:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotClimbUnder:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotLeap:
                    entity.AddParameter("MissDistance", new cFloat(2.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NearMissDistance", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.TRAV_1ShotSpline:
                    entity.AddParameter("template", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("headroom", new cFloat(1.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("extra_cost", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("fit_end_to_edge", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("min_speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 3), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("animationTree", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NavMeshBarrier:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("opaque", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("allowed_character_classes_when_open", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("allowed_character_classes_when_closed", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NavMeshWalkablePlatform:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NavMeshExclusionArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NavMeshArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("area_type", new cEnum(EnumType.NAV_MESH_AREA_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NavMeshReachabilitySeedPoint:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CoverExclusionArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exclude_cover", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exclude_vaults", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exclude_mantles", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exclude_jump_downs", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exclude_crawl_space_spotting_positions", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exclude_spotting_positions", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("exclude_assault_positions", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SpottingExclusionArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PathfindingTeleportNode:
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("extra_cost", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PathfindingWaitNode:
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("extra_cost", new cFloat(100.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 733), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PathfindingManualNode:
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("extra_cost", new cFloat(100.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PathfindingAlienBackstageNode:
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("top", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("extra_cost", new cFloat(100.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("network_id", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Planet:
                    entity.AddParameter("parallax_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("planet_sort_key", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("overbright_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("light_wrap_angle_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("penumbra_falloff_power_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lens_flare_brightness", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lens_flare_colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("atmosphere_edge_falloff_power", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("atmosphere_edge_transparency", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("atmosphere_scroll_speed", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("atmosphere_detail_scroll_speed", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("override_global_tint", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("global_tint", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flow_cycle_time", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flow_speed", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flow_tex_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("flow_warp_strength", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("detail_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("normal_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("terrain_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("atmosphere_normal_strength", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("terrain_normal_strength", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("light_shaft_colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("light_shaft_range", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("light_shaft_decay", new cFloat(0.8f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("light_shaft_min_occlusion_distance", new cFloat(100f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("light_shaft_intensity", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("light_shaft_density", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("light_shaft_source_occlusion", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("blocks_light_shafts", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SpaceTransform:
                    entity.AddParameter("yaw_speed", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("pitch_speed", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("roll_speed", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SpaceSuitVisor:
                    entity.AddParameter("breath_level", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.NonInteractiveWater:
                    entity.AddParameter("SCALE_X", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SCALE_Z", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SHININESS", new cFloat(0.8f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CYCLE_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FRESNEL_POWER", new cFloat(0.8f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MIN_FRESNEL", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MAX_FRESNEL", new cFloat(5.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVIRONMENT_MAP_MULT", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVMAP_SIZE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ENVMAP_BOXPROJ_BB_SCALE", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("REFLECTION_PERTURBATION_STRENGTH", new cFloat(0.05f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHA_PERTURBATION_STRENGTH", new cFloat(0.05f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ALPHALIGHT_MULT", new cFloat(0.4f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("softness_edge", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_INITIAL_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_ALPHA", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_END_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_END_ALPHA", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DEPTH_FOG_END_DEPTH", new cFloat(2.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Refraction:
                    entity.AddParameter("SCALE_X", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SCALE_Z", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DISTANCEFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MIN_OCCLUSION_DISTANCE", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("CYCLE_TIME", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FogPlane:
                    entity.AddParameter("start_distance_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("distance_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("angle_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("linear_height_density_fresnel_power_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("linear_heigth_density_max_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("tint", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("thickness_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("edge_softness_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("diffuse_0_uv_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("diffuse_0_speed_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("diffuse_1_uv_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("diffuse_1_speed_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PostprocessingSettings:
                    entity.AddParameter("priority", new cInteger(100), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("blend_mode", new cEnum(EnumType.BLEND_MODE, 2), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.LensDustSettings:
                    entity.AddParameter("DUST_MAX_REFLECTED_BLOOM_INTENSITY", new cFloat(0.02f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DUST_REFLECTED_BLOOM_INTENSITY_SCALAR", new cFloat(0.25f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DUST_MAX_BLOOM_INTENSITY", new cFloat(0.004f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DUST_BLOOM_INTENSITY_SCALAR", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("DUST_THRESHOLD", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.LightAdaptationSettings:
                    entity.AddParameter("adaptation_mechanism", new cEnum(EnumType.LIGHT_ADAPTATION_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ColourCorrectionTransition:
                    entity.AddParameter("colour_lut_a", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("colour_lut_b", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lut_a_contribution", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lut_b_contribution", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GetClosestPercentOnSpline:
                    entity.AddParameter("bidirectional", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GetClosestPointOnSpline:
                    entity.AddParameter("look_ahead_distance", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("unidirectional", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("directional_damping_threshold", new cFloat(0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DistortionOverlay:
                    entity.AddParameter("distortion_texture", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alpha_threshold_enabled", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("threshold_texture", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("range", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("begin_start_time", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("begin_stop_time", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("end_start_time", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("end_stop_time", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FullScreenOverlay:
                    entity.AddParameter("overlay_texture", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("threshold_value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("threshold_start", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("threshold_stop", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("threshold_range", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("alpha_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DepthOfFieldSettings:
                    entity.AddParameter("use_camera_target", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ChromaticAberrations:
                    entity.AddParameter("aberration_scalar", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ScreenFadeOutToBlack:
                    entity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ScreenFadeOutToBlackTimed:
                    entity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ScreenFadeOutToWhite:
                    entity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ScreenFadeOutToWhiteTimed:
                    entity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ScreenFadeIn:
                    entity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ScreenFadeInTimed:
                    entity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.BlendLowResFrame:
                    entity.AddParameter("blend_value", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ENT_Debug_Exit_Game:
                    entity.AddParameter("FailureText", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("FailureCode", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Raycast:
                    entity.AddParameter("priority", new cEnum(EnumType.RAYCAST_PRIORITY, 2), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AssetSpawner:
                    entity.AddParameter("spawn_on_load", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("allow_forced_despawn", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("persist_on_callback", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("allow_physics", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ProximityTrigger:
                    entity.AddParameter("fire_spread_rate", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("water_permeate_rate", new cFloat(10.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("electrical_conduction_rate", new cFloat(100.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("gas_diffusion_rate", new cFloat(0.1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ignition_range", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("electrical_arc_range", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("water_flow_range", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("gas_dispersion_range", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.CharacterAttachmentNode:
                    entity.AddParameter("Node", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AdditiveNode", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("AdditiveNodeIntensity", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("UseOffset", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Translation", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Rotation", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.MultipleCharacterAttachmentNode:
                    entity.AddParameter("node", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_offset", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("translation", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("rotation", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AnimatedModelAttachmentNode:
                    entity.AddParameter("bone_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_offset", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("offset", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.LevelCompletionTargets:
                    entity.AddParameter("TargetTime", new cFloat(-1f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NumDeaths", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("TeamRespawnBonus", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NoLocalRespawnBonus", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("NoRespawnBonus", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("GrappleBreakBonus", new cInteger(-1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.EnvironmentMap:
                    entity.AddParameter("Priority", new cInteger(100), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("ColourFactor", new cVector3(new Vector3(255.0f, 255.0f, 255.0f)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("EmissiveFactor", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Texture", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Display_Element_On_Map:
                    entity.AddParameter("map_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("element_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Map_Floor_Change:
                    entity.AddParameter("floor_name", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AddExitObjective:
                    entity.AddParameter("level_name", new cEnum(EnumType.EXIT_WAYPOINT, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetPrimaryObjective:
                    entity.AddParameter("title", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("additional_info", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("title_list", new cString("A1_G0000_RIP_0010A"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("additional_info_list", new cString("A1_G0000_RIP_0010A"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetSubObjective:
                    entity.AddParameter("title", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("map_description", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("title_list", new cString("A1_G0000_RIP_0010A"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("map_description_list", new cString("A1_G0000_RIP_0010A"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("objective_type", new cEnum(EnumType.SUB_OBJECTIVE_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ClearPrimaryObjective:
                    entity.AddParameter("clear_all_sub_objectives", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ClearSubObjective:
                    entity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UpdatePrimaryObjective:
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("clear_objective", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UpdateSubObjective:
                    entity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("clear_objective", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DebugGraph:
                    entity.AddParameter("scale", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("duration", new cFloat(5.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("samples_per_second", new cFloat(60.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("auto_scale", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("auto_scroll", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UnlockAchievement:
                    entity.AddParameter("achievement_id", new cString("CA_PROGRESSION_01"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AchievementMonitor:
                    entity.AddParameter("achievement_id", new cString("CA_PROGRESSION_01"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AchievementStat:
                    entity.AddParameter("achievement_id", new cString("CA_IDTAG_STAT"), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AchievementUniqueCounter:
                    entity.AddParameter("achievement_id", new cString("CA_IDTAG_STAT"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("unique_object", new cString(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.SetRichPresence:
                    entity.AddParameter("presence_id", new cString("NULL_STRING"), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PointTracker:
                    entity.AddParameter("origin_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_speed", new cFloat(180.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("damping_factor", new cFloat(0.6f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GlobalEvent:
                    entity.AddParameter("EventName", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GlobalEventMonitor:
                    entity.AddParameter("EventName", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GlobalPosition:
                    entity.AddParameter("PositionName", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.UpdateGlobalPosition:
                    entity.AddParameter("PositionName", new cString(""), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.AILightCurveSettings:
                    entity.AddParameter("y0", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("x1", new cFloat(0.25f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("y1", new cFloat(0.3f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("x2", new cFloat(0.6f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("y2", new cFloat(0.8f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("x3", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.InteractiveMovementControl:
                    entity.AddParameter("can_go_both_ways", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("use_left_input_stick", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("base_progress_speed", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("movement_threshold", new cFloat(30.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("momentum_damping", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("track_bone_position", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("character_node", new cEnum(EnumType.CHARACTER_NODE, 9), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("track_position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.GCIP_WorldPickup:
                    entity.AddParameter("Pipe", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Gasoline", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Explosive", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Battery", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Blade", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Gel", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Adhesive", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BoltGun Ammo", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Revolver Ammo", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Shotgun Ammo", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("BoltGun", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Revolver", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Shotgun", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Flare", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Flamer Fuel", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Flamer", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Scrap", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Torch Battery", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Torch", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Cattleprod Ammo", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("Cattleprod", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("StartOnReset", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("MissionNumber", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DoorStatus:
                    entity.AddParameter("hacking_difficulty", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("gate_type", new cEnum(EnumType.UI_KEYGATE_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("has_correct_keycard", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("cutting_tool_level", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_locked", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_powered", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_cutting_complete", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DeleteHacking:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DeleteKeypad:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DeleteCuttingPanel:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DeleteBlankPanel:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DeleteHousing:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("is_door", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DeletePullLever:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lever_type", new cEnum(EnumType.LEVER_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DeleteRotateLever:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("lever_type", new cEnum(EnumType.LEVER_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DeleteButtonDisk:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("button_type", new cEnum(EnumType.BUTTON_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.DeleteButtonKeys:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("button_type", new cEnum(EnumType.BUTTON_TYPE, 0), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.Interaction:
                    entity.AddParameter("interruptible_on_start", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.PlayerDeathCounter:
                    entity.AddParameter("Limit", new cInteger(1), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.RadiosityProxy:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.LeaderboardWriter:
                    entity.AddParameter("time_elapsed", new cFloat(0.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("score", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("level_number", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("grade", new cInteger(5), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("player_character", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("combat", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("stealth", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("improv", new cInteger(0), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("star1", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("star2", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("star3", new cBool(false), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.ProximityDetector:
                    entity.AddParameter("min_distance", new cFloat(0.3f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("max_distance", new cFloat(100.0f), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("requires_line_of_sight", new cBool(true), ParameterVariant.PARAMETER, overwrite);
                    entity.AddParameter("proximity_duration", new cFloat(1.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
                case FunctionType.FakeAILightSourceInPlayersHand:
                    entity.AddParameter("radius", new cFloat(5.0f), ParameterVariant.PARAMETER, overwrite);
                    break;
            }
        }
        private static void ApplyDefaultInternalForFunction(Entity entity, FunctionType type, bool overwrite)
        {
            switch (type)
            {
                case FunctionType.EnvironmentModelReference:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ANIMATED_MODEL) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.SplinePath:
                    entity.AddParameter("points", new cSpline(), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.ModelReference:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    entity.AddParameter("alpha_light_offset_x", new cFloat(0.0f), ParameterVariant.INTERNAL, overwrite);
                    entity.AddParameter("alpha_light_offset_y", new cFloat(0.0f), ParameterVariant.INTERNAL, overwrite);
                    entity.AddParameter("alpha_light_scale_x", new cFloat(1.0f), ParameterVariant.INTERNAL, overwrite);
                    entity.AddParameter("alpha_light_scale_y", new cFloat(1.0f), ParameterVariant.INTERNAL, overwrite);
                    entity.AddParameter("alpha_light_average_normal", new cVector3(new Vector3(0.0f, 0.0f, 0.0f)), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.LightReference:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.ParticleEmitterReference:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.RibbonEmitterReference:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.FogSphere:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.FogBox:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.SurfaceEffectSphere:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.SurfaceEffectBox:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.SimpleWater:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    entity.AddParameter("CAUSTIC_TEXTURE_INDEX", new cInteger(-1), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.SimpleRefraction:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.ProjectiveDecal:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.AnimationMask:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ANIMATION_MASK_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.CMD_PlayAnimation:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.CAGEAnimation:
                    entity.AddParameter("playback", new cFloat(0.0f), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.ExclusiveMaster:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.EXCLUSIVE_MASTER_STATE_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.SoundBarrier:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.COLLISION_MAPPING) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.CoverLine:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CATHODE_COVER_SEGMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    entity.AddParameter("LinePathPosition", new cTransform(), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentEntrance:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TRAVERSAL_SEGMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentExit:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TRAVERSAL_SEGMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TRAVERSAL_SEGMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentExit:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TRAVERSAL_SEGMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.TRAV_1ShotSpline:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TRAVERSAL_SEGMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.NavMeshBarrier:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NAV_MESH_BARRIER_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.ChokePoint:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHOKE_POINT_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.ColourCorrectionTransition:
                    entity.AddParameter("colour_lut_a_index", new cInteger(-1), ParameterVariant.INTERNAL, overwrite);
                    entity.AddParameter("colour_lut_b_index", new cInteger(-1), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.EnvironmentMap:
                    entity.AddParameter("Texture_Index", new cInteger(-1), ParameterVariant.INTERNAL, overwrite);
                    entity.AddParameter("environmentmap_index", new cInteger(-1), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.PhysicsSystem:
                    entity.AddParameter("system_index", new cInteger(0), ParameterVariant.INTERNAL, overwrite);
                    break;
                case FunctionType.RadiosityProxy:
                    entity.AddParameter("resource", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INTERNAL, overwrite);
                    break;
            }
        }
        private static void ApplyDefaultMethodFunctionForFunction(Entity entity, FunctionType type, bool overwrite)
        {
            switch (type)
            {
                case FunctionType.SensorInterface:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.AttachmentInterface:
                    entity.AddParameter("deactivate", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("reposition", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.SensorAttachmentInterface:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CharacterCommand:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("deactivate", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("live_edit", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CMD_PlayAnimation:
                    entity.AddParameter("load", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("unload", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CMD_HolsterWeapon:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CHR_PlaySecondaryAnimation:
                    entity.AddParameter("load", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("unload", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.Player_Sensor:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CHR_SetAndroidThrowTarget:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CHR_DamageMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("shutdown", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CHR_KnockedOutMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("shutdown", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CHR_DeathMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("shutdown", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CHR_RetreatMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("shutdown", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CHR_WeaponFireMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("shutdown", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CHR_TorchMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("shutdown", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.CHR_VentMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("shutdown", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NPC_Squad_DialogueMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("shutdown", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NPC_Group_DeathCounter:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NPC_Group_Death_Monitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("shutdown", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NPC_Aggression_Monitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NPC_Highest_Awareness_Monitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.PlayerCameraMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.ScreenEffectEventMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NPC_behaviour_monitor:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NPC_multi_behaviour_monitor:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NPC_ambush_monitor:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NPC_navmesh_type_monitor:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.Checkpoint:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.DebugEnvironmentMarker:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.DebugPositionMarker:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.SyncOnAllPlayers:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.SyncOnFirstPlayer:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NetPlayerCounter:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.BroadcastTrigger:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NetworkedTimer:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.Door:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.Job:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.JobWithPosition:
                    entity.AddParameter("live_edit", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("post_restore", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.Task:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.NPC_ForceNextJob:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.UI_Icon:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.UI_Attached:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("stop_using", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("success", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("failure", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.RewireAccess_Point:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.PathfindingManualNode:
                    entity.AddParameter("load", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("unload", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.PathfindingAlienBackstageNode:
                    entity.AddParameter("load", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    entity.AddParameter("unload", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.ScreenFadeOutToBlackTimed:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.ScreenFadeOutToWhiteTimed:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.ScreenFadeInTimed:
                    entity.AddParameter("update", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.SmokeCylinder:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.SmokeCylinderAttachmentInterface:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
                case FunctionType.PlayerKilledAllyMonitor:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD_FUNCTION, overwrite);
                    break;
            }
        }
        private static void ApplyDefaultMethodPinForFunction(Entity entity, FunctionType type, bool overwrite)
        {
            switch (type)
            {
                case FunctionType.ProxyInterface:
                    entity.AddParameter("proxy_enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("proxy_disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ScriptVariable:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.InspectorInterface:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SensorInterface:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("pause", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("resume", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CloseableInterface:
                    entity.AddParameter("open", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("close", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GateInterface:
                    entity.AddParameter("open", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("close", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("lock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("unlock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ZoneInterface:
                    entity.AddParameter("request_load", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel_load", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("request_unload", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel_unload", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AttachmentInterface:
                    entity.AddParameter("attach", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("detach", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SensorAttachmentInterface:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("pause", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("resume", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CompositeInterface:
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("simulate", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("keyframe", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("suspend", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("allow", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Box:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UpdateLeaderBoardDisplay:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetNextLoadingMovie:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ButtonMashPrompt:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetFlashIntValue:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetFlashFloatValue:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Sphere:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ImpactSphere:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerTriggerBox:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerUseTriggerBox:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ModelReference:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("simulate", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("keyframe", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_on", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_off", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LightReference:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_on", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_off", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("purge", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ParticleEmitterReference:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("terminate", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RibbonEmitterReference:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("terminate", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FogSphere:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FogBox:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SurfaceEffectSphere:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("fade_out", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SurfaceEffectBox:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("fade_out", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SimpleWater:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SimpleRefraction:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ProjectiveDecal:
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("fade_out", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_decal_time", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LightingMaster:
                    entity.AddParameter("light_switch_on", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_off", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CameraResource:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("activate_camera", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("deactivate_camera", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CameraFinder:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerCamera:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CameraBehaviorInterface:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("activate_behavior", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("deactivate_behavior", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CameraShake:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CameraPathDriven:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FixedCamera:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.BoneAttachedCamera:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.StealCamera:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FollowCameraModifier:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CameraPath:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CameraAimAssistant:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CameraPlayAnimation:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetCurrentCameraTarget:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CharacterShivaArms:
                    entity.AddParameter("apply_hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Logic_Vent_Entrance:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_is_open", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_is_closed", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_Follow:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_FollowUsingJobs:
                    entity.AddParameter("seed", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_PlayAnimation:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("request_load", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel_load", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_Idle:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_StopScript:
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_GoTo:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_GoToCover:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_MoveTowards:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_Die:
                    entity.AddParameter("kill", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_LaunchMeleeAttack:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_ModifyCombatBehaviour:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_HolsterWeapon:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_ForceReloadWeapon:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_ForceMeleeAttack:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_ModifyBreathing:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_HoldBreath:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_DeepCrouch:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_PlaySecondaryAnimation:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("request_load", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel_load", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_LocomotionDuck:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_ShootAt:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_AimAtCurrentTarget:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_AimAt:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CMD_Ragdoll:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetTacticalPosition:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("clear", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetTacticalPositionToTarget:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("clear", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetFocalPoint:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetAndroidThrowTarget:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("clear", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetAlliance:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_GetAlliance:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ALLIANCE_SetDisposition:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ALLIANCE_ResetAll:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetInvincibility:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetHealth:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_GetHealth:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetDebugDisplayName:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_TakeDamage:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetSubModelVisibility:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetHeadVisibility:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetFacehuggerAggroRadius:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MonitorBase:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CharacterTypeMonitor:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Convo:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_NotifyDynamicDialogueEvent:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_Squad_DialogueMonitor:
                    entity.AddParameter("start_monitor", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop_monitor", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_Group_DeathCounter:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_Group_Death_Monitor:
                    entity.AddParameter("start_monitor", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop_monitor", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_AllSensesLimiter:
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SenseLimiter:
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_ResetSensesAndMemory:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetupMenaceManager:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_AlienConfig:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_GetLastSensedPositionOfTarget:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Weapon_AINotifier:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reloading", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("out_of_ammo", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("started_aiming", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stopped_aiming", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.HeldItem_AINotifier:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("expire", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_Gain_Aggression_In_Radius:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Explosion_AINotifier:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_Sleeping_Android_Monitor:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("task_end", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_Highest_Awareness_Monitor:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_Squad_GetAwarenessState:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_Squad_GetAwarenessWatermark:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerCameraMonitor:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ScreenEffectEventMonitor:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_FakeSense:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SuspiciousItem:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_TargetAcquire:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("add_character", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("remove_character", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_IsWithinRange:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_ForceCombatTarget:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetAimTarget:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetTorch:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_GetTorch:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetAutoTorchMode:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_GetCombatTarget:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetTotallyBlindInDark:
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetSafePoint:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Player_ExploitableArea:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetDefendArea:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetPursuitArea:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_ClearDefendArea:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_ClearPursuitArea:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_ForceRetreat:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetAlertness:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetStartPos:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetAgressionProgression:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetLocomotionStyleForJobs:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetLocomotionTargetSpeed:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetGunAimMode:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_Coordinator:
                    entity.AddParameter("add_character", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("remove_character", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("update_squad_params", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_set_behaviour_tree_flags:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetHidingSearchRadius:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetHidingNearestLocation:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_WithdrawAlien:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetInvisible:
                    entity.AddParameter("apply_hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_HasWeaponOfType:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_TriggerAimRequest:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_StopAiming:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_TriggerShootRequest:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_StopShooting:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Squad_SetMaxEscalationLevel:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Chr_PlayerCrouch:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_Once:
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Custom_Hiding_Vignette_controller:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Custom_Hiding_Controller:
                    entity.AddParameter("Get_In", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Add_NPC", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Start_Breathing_Game", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("End_Breathing_Game", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TorchDynamicMovement:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.EQUIPPABLE_ITEM:
                    entity.AddParameter("spawn", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("despawn", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MELEE_WEAPON:
                    entity.AddParameter("impact_with_world", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerWeaponMonitor:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RemoveWeaponsFromPlayer:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerDiscardsWeapons:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerDiscardsItems:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerDiscardsTools:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_GiveToCharacter:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_GiveToPlayer:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactEffect:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactFilter:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_AttackerFilter:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_TargetObjectFilter:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactInspector:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_DamageFilter:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_DidHitSomethingFilter:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_MultiFilter:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactCharacterFilter:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_Effect:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_AmmoTypeFilter:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactAngleFilter:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.WEAPON_ImpactOrientationFilter:
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.EFFECT_ImpactGenerator:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.EFFECT_EntityGenerator:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.VariableBool:
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.VariableFlashScreenColour:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("pause", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("resume", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NonPersistentBool:
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GameDVR:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FlushZoneCache:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.StateQuery:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.BooleanLogicInterface:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LogicOnce:
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LogicSwitch:
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.BooleanLogicOperation:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FloatMath_All:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FloatMath:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FloatOperation:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FloatCompare:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FloatModulateRandom:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FloatLinearProportion:
                    entity.AddParameter("Evaluate", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FloatGetLinearProportion:
                    entity.AddParameter("Evaluate", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FloatLinearInterpolateSpeedAdvanced:
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FloatSmoothStep:
                    entity.AddParameter("Evaluate", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FloatClamp:
                    entity.AddParameter("Evaluate", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.IntegerMath_All:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.IntegerMath:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.IntegerOperation:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.IntegerCompare:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.VectorMath:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetComponentInterface:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.VectorLinearProportion:
                    entity.AddParameter("Evaluate", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PointAt:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetLocationAndOrientation:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ApplyRelativeTransform:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RandomFloat:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RandomInt:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RandomBool:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RandomVector:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RandomSelect:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerRandomSequence:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_all", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_1", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_2", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_3", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_4", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_5", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_6", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_7", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_8", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_9", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_10", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Persistent_TriggerRandomSequence:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_all", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_1", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_2", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_3", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_4", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_5", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_6", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_7", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_8", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_9", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_Random_10", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerWeightedRandom:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayEnvironmentAnimation:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CAGEAnimation:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("rewind", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("load_cutscene", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("unload_cutscene", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("start_cutscene", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop_cutscene", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("pause_cutscene", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("resume_cutscene", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MultitrackLoop:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerSequence:
                    entity.AddParameter("proxy_enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("proxy_disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SaveGlobalProgression:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetAsActiveMissionLevel:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.DisplayMessage:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.DisplayMessageWithCallbacks:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Benchmark:
                    entity.AddParameter("start_benchmark", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop_benchmark", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.EndGame:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LeaveGame:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.DebugTextStacking:
                    entity.AddParameter("clear_all", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("clear_last", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.DebugText:
                    entity.AddParameter("clear_all", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("clear_of_alignment", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.DebugCaptureScreenShot:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.DebugCaptureCorpse:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.DebugMenuToggle:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TogglePlayerTorch:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerTorch:
                    entity.AddParameter("torch_turned_on", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("torch_turned_off", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("torch_new_battery_added", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("torch_battery_has_expired", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("torch_low_power", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Master:
                    entity.AddParameter("suspend", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("allow", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("simulate", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("keyframe", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ExclusiveMaster:
                    entity.AddParameter("set_active", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_inactive", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SyncOnAllPlayers:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SyncOnFirstPlayer:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ParticipatingPlayersList:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NetPlayerCounter:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.BroadcastTrigger:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.HostOnlyTrigger:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SpawnGroup:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RespawnExcluder:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RespawnConfig:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.EggSpawner:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RandomObjectSelector:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.BindObjectsMultiplexer:
                    entity.AddParameter("Pin1", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin2", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin3", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin4", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin5", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin6", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin7", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin8", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin9", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin10", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerTouch:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerDamaged:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerBindCharacter:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerBindAllCharactersOfType:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerBindCharactersInSquad:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerUnbindCharacter:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerDelay:
                    entity.AddParameter("abort", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("purge", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("pause", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("resume", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerSwitch:
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Up", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Down", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Random", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerSelect:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerSelect_Direct:
                    entity.AddParameter("Trigger_0", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_1", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_2", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_3", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_4", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_5", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_6", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_7", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_8", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_9", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_10", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_11", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_12", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_13", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_14", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_15", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Trigger_16", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerSync:
                    entity.AddParameter("Pin1", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin2", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin3", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin4", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin5", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin6", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin7", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin8", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin9", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin10", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LogicAll:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin1", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin2", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin3", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin4", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin5", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin6", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin7", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin8", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin9", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Pin10", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Counter:
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LogicCounter:
                    entity.AddParameter("Up", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("Down", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LogicPressurePad:
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("bind_all", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("verify", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GateResourceInterface:
                    entity.AddParameter("request_open", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("request_close", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("request_lock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("request_unlock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("force_open", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("force_close", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("request_restore", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.InhibitActionsUntilRelease:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PadLightBar:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PadRumbleImpulse:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Character:
                    entity.AddParameter("spawn", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("despawn", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.DespawnPlayer:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.DespawnCharacter:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FilterIsInWeaponRange:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerWhenSeeTarget:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Task:
                    entity.AddParameter("task_end", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.IdleTask:
                    entity.AddParameter("set_as_next_task", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("completed_pre_move", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("completed_interrupt", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FollowTask:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("allow_early_end", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_ForceNextJob:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetRateOfFire:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetFiringRhythm:
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetFiringAccuracy:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_ResetFiringStats:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TriggerBindAllNPCs:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Trigger_AudioOccluded:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SwitchLevel:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_DynamicDialogue:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_DynamicDialogueGlobalRange:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_PlayNPCBark:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SoundLoadBank:
                    entity.AddParameter("load_bank", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("unload_bank", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SoundLoadSlot:
                    entity.AddParameter("load_bank", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SoundSetState:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SoundSetSwitch:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SoundImpact:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SoundBarrier:
                    entity.AddParameter("barrier_open", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("barrier_close", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_override", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MusicController:
                    entity.AddParameter("enable_music", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable_music", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MusicTrigger:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SoundRTPCController:
                    entity.AddParameter("enable_stealth", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable_stealth", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable_threat", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable_threat", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SoundTimelineTrigger:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("trigger_now", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("abort", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SoundPlayerFootwearOverride:
                    entity.AddParameter("enable_override", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable_override", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AddToInventory:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RemoveFromInventory:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LimitItemUse:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerHasItemEntity:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.InventoryItem:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PickupSpawner:
                    entity.AddParameter("spawn", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("despawn", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MultiplePickupSpawner:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AddItemsToGCPool:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetupGCDistribution:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AllocateGCItemsFromPool:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AllocateGCItemFromPoolBySubset:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.QueryGCItemPool:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RemoveFromGCItemPool:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FlashScript:
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UI_KeyGate:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("lock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("unlock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_on", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_off", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RTT_MoviePlayer:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MoviePlayer:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ToggleFunctionality:
                    entity.AddParameter("disable_radial", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable_radial", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable_radial_hacking_info", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable_radial_hacking_info", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable_radial_cutting_info", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable_radial_cutting_info", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable_radial_battery_info", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable_radial_battery_info", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable_hud_battery_info", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable_hud_battery_info", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FlashInvoke:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MotionTrackerPing:
                    entity.AddParameter("start_ping", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop_ping", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CHR_SetShowInMotionTracker:
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.EnableMotionTrackerPassiveAudio:
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UIBreathingGameIcon:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("refresh_value", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("display_tutorial", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("display_tutorial_breathing_1", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("display_tutorial_breathing_2", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("breathing_game_tutorial_fail", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GenericHighlightEntity:
                    entity.AddParameter("light_switch_on", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_off", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UI_Icon:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("lock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("unlock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_on", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_off", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("clear_user", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("force_disable_highlight", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UI_Attached:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UI_Container:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UI_ReactionGame:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.HackingGame:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("lock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("unlock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_on", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_off", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("display_tutorial", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("transition_completed", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset_hacking_success_flag", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("display_hacking_upgrade", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide_hacking_upgrade", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetHackingToolLevel:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TerminalFolder:
                    entity.AddParameter("refresh_value", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("refresh_text", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("lock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("unlock", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AccessTerminal:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_on", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("light_switch_off", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetGatingToolLevel:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetGatingToolLevel:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetPlayerHasGatingTool:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetPlayerHasKeycard:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetPlayerHasKeycard:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetPlayerHasGatingTool:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CollectSevastopolLog:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CollectNostromoLog:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CollectIDTag:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.StartNewChapter:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UnlockLogEntry:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MapAnchor:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MapItem:
                    entity.AddParameter("refresh_value", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide_ui", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show_ui", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UnlockMapDetail:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RewireSystem:
                    entity.AddParameter("turn_on_system", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("turn_off_system", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.RewireAccess_Point:
                    entity.AddParameter("display_tutorial", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("finished_closing_container", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Rewire:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetMotionTrackerRange:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetGamepadAxes:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetGameplayTips:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GameOver:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetBlueprintInfo:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetBlueprintLevel:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetBlueprintAvailable:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetSelectedCharacterId:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetNextPlaylistLevelName:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.IsPlaylistTypeSingle:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.IsPlaylistTypeAll:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.IsPlaylistTypeMarathon:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.IsCurrentLevelAChallengeMap:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.IsCurrentLevelAPreorderMap:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetCurrentPlaylistLevelIndex:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetObjectiveCompleted:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GameOverCredits:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GoToFrontend:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CoverLine:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLadder:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousPipe:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousLedge:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousClimbingWall:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousCinematicSidle:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousBalanceBeam:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_ContinuousTightGap:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentEntrance:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotVentExit:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotFloorVentExit:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotClimbUnder:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotLeap:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.TRAV_1ShotSpline:
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NavMeshBarrier:
                    entity.AddParameter("open", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("close", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PathfindingTeleportNode:
                    entity.AddParameter("update_cost", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PathfindingWaitNode:
                    entity.AddParameter("update_cost", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PathfindingManualNode:
                    entity.AddParameter("update_cost", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PathfindingAlienBackstageNode:
                    entity.AddParameter("open", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("close", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("force_killtrap", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel_force_killtrap", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable_killtrap", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel_disable_killtrap", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hit_by_flamethrower", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cancel_hit_by_flamethrower", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ChokePoint:
                    entity.AddParameter("enable_chokepoint", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disable_chokepoint", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.NPC_SetChokePoint:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SpaceTransform:
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FogPlane:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetSplineLength:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetPointOnSpline:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPercentOnSpline:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPointOnSpline:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPoint:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetClosestPointFromSet:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetCentrePoint:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ENT_Debug_Exit_Game:
                    entity.AddParameter("fail_game", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PhysicsModifyGravity:
                    entity.AddParameter("floating", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("sinking", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PhysicsApplyBuoyancy:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AssetSpawner:
                    entity.AddParameter("spawn", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("despawn", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ProximityTrigger:
                    entity.AddParameter("ignite", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("electrify", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("drench", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("poison", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.CharacterAttachmentNode:
                    entity.AddParameter("attach", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("detach", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MultipleCharacterAttachmentNode:
                    entity.AddParameter("attach", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("detach", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AnimatedModelAttachmentNode:
                    entity.AddParameter("attach", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("detach", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GetCharacterRotationSpeed:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LevelCompletionTargets:
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.EnvironmentMap:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("purge", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Showlevel_Completed:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Display_Element_On_Map:
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Map_Floor_Change:
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Force_UI_Visibility:
                    entity.AddParameter("clear_pending_ui", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hide_objective_message", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("show_objective_message", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cutting_panel_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cutting_panel_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("keypad_interaction_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("keypad_interaction_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("traversal_interaction_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("traversal_interaction_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("suit_change_interaction_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("suit_change_interaction_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("terminal_interaction_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("terminal_interaction_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("rewire_interaction_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("rewire_interaction_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hacking_interaction_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hacking_interaction_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("ladder_interaction_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("ladder_interaction_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("button_interaction_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("button_interaction_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("lever_interaction_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("lever_interaction_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("level_fade_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("level_fade_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cutscene_visibility_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("cutscene_visibility_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hiding_visibility_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("hiding_visibility_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AddExitObjective:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetPrimaryObjective:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetSubObjective:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ClearPrimaryObjective:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ClearSubObjective:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UpdatePrimaryObjective:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UpdateSubObjective:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.UnlockAchievement:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AchievementMonitor:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AchievementStat:
                    entity.AddParameter("Up", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.AchievementUniqueCounter:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SetRichPresence:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.SmokeCylinderAttachmentInterface:
                    entity.AddParameter("stop_emitting", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.MotionTrackerMonitor:
                    entity.AddParameter("activate_tracker", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("deactivate_tracker", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GlobalEvent:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GlobalEventMonitor:
                    entity.AddParameter("start_monitoring", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop_monitoring", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerKilledAllyMonitor:
                    entity.AddParameter("start_monitor", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop_monitor", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.InteractiveMovementControl:
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayForMinDuration:
                    entity.AddParameter("start_timer", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop_timer", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("notify_animation_started", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("notify_animation_finished", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.GCIP_WorldPickup:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Torch_Control:
                    entity.AddParameter("turn_off_torch", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("turn_on_torch", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("toggle_torch", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("resume_torch", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("allow_torch", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.DoorStatus:
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.Interaction:
                    entity.AddParameter("start_interaction", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("stop_interaction", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("allow_interrupt", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("disallow_interrupt", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.BulletChamber:
                    entity.AddParameter("reload_fill", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reload_open", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reload_empty", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reload_load", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reload_fire", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reload_finish", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.PlayerDeathCounter:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ElapsedTimer:
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.LeaderboardWriter:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.ProximityDetector:
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
                case FunctionType.FakeAILightSourceInPlayersHand:
                    entity.AddParameter("fake_light_on", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    entity.AddParameter("fake_light_off", new cFloat(), ParameterVariant.METHOD_PIN, overwrite);
                    break;
            }
        } 
        #endregion
    }
}
