using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            BinaryReader reader = new BinaryReader(File.OpenRead(Application.streamingAssetsPath + "/NodeDBs/composite_entity_names.bin"));
#else
            BinaryReader reader = new BinaryReader(new MemoryStream(CathodeLib.Properties.Resources.composite_entity_names));
#endif
            _vanilla = new EntityNameTable(reader);
            _custom = new EntityNameTable();
            reader.Close();
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
            if (_custom.names.ContainsKey(compositeID) && _custom.names[compositeID].ContainsKey(entityID))
                return _custom.names[compositeID][entityID];
            if (_vanilla.names.ContainsKey(compositeID) && _vanilla.names[compositeID].ContainsKey(entityID))
                return _vanilla.names[compositeID][entityID];
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

        /* Applies all default parameter data to a Function entity (POTENTIALLY DESTRUCTIVE!) */
        public static void ApplyDefaults(FunctionEntity entity, bool includeInheritedMembers = true)
        {
            ApplyDefaults(entity, !CommandsUtils.FunctionTypeExists(entity.function) ? FunctionType.CompositeInterface : CommandsUtils.GetFunctionType(entity.function), includeInheritedMembers);
        }
        public static void ApplyDefaults(ProxyEntity entity, bool includeInheritedMembers = true)
        {
            //TODO: should we also populate defaults based on the entity we're pointing to?
            ApplyDefaults(entity, FunctionType.ProxyInterface, includeInheritedMembers);
        }
        private static void ApplyDefaults(Entity entity, FunctionType currentType, bool includeInheritedMembers)
        {
            //Figure out the chain of inheritance to this function type
            List<FunctionType> inheritance = new List<FunctionType>();
            while (currentType != FunctionType.EntityMethodInterface)
            {
                inheritance.Add(currentType);
                if (!includeInheritedMembers) break;
                currentType = GetBaseFunction(currentType);
            }
            inheritance.Reverse();

            //Apply parameters
            for (int i = 0; i < inheritance.Count; i++) ApplyDefaultsInternal(entity, inheritance[i]);
            //TODO: we don't apply any on_custom_method implementations here - we need to write them out from cathode_vartype

            //If we're a composite reference, add the composite's parameters too
            if (entity.variant == EntityVariant.FUNCTION && !CommandsUtils.FunctionTypeExists(((FunctionEntity)entity).function))
            {
                if (_commands == null) return;
                Composite comp = _commands.Entries.FirstOrDefault(o => o.shortGUID == ((FunctionEntity)entity).function);
                if (comp == null) return;
                for (int i = 0; i < comp.variables.Count; i++)
                    entity.AddParameter(comp.variables[i].name, comp.variables[i].type, ParameterVariant.PARAMETER); //TODO: These are not always parameters - how can we distinguish?
            }
        }

        /* Gets the function this function inherits from - you can keep calling this down to EntityMethodInterface */
        public static FunctionType GetBaseFunction(FunctionEntity entity)
        {
            return GetBaseFunction(CommandsUtils.GetFunctionType(entity));
        }
        public static FunctionType GetBaseFunction(FunctionType type)
        {
            return GetBaseFunctionInternal(type);
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

        /* Gets the base function for this function type */
        private static FunctionType GetBaseFunctionInternal(FunctionType type)
        {
            switch (type)
            {
                //These are best guesses
                case FunctionType.WEAPON_DidHitSomethingFilter:
                    return FunctionType.ScriptInterface;
                case FunctionType.DebugPositionMarker:
                    return FunctionType.SensorInterface;

                //This is as far as we go, but it actually inherits from EntityResourceInterface
                case FunctionType.EntityMethodInterface:
                    return FunctionType.EntityMethodInterface;

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
                case FunctionType.CameraAimAssistant:
                    return FunctionType.EntityInterface;
                case FunctionType.CameraBehaviorInterface:
                    return FunctionType.EntityInterface;
                case FunctionType.CameraCollisionBox:
                    return FunctionType.Box;
                case FunctionType.CameraDofController:
                    return FunctionType.CameraBehaviorInterface;
                case FunctionType.CameraFinder:
                    return FunctionType.EntityInterface;
                case FunctionType.CameraPath:
                    return FunctionType.EntityInterface;
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
                case FunctionType.DebugCaptureCorpse:
                    return FunctionType.EntityInterface;
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
                case FunctionType.DebugMenuToggle:
                    return FunctionType.EntityInterface;
                case FunctionType.DebugObjectMarker:
                    return FunctionType.ScriptInterface;
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
                case FunctionType.EntityInterface:
                    return FunctionType.EntityMethodInterface;
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
                case FunctionType.FollowCameraModifier:
                    return FunctionType.EntityInterface;
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
                case FunctionType.PlayerCamera:
                    return FunctionType.EntityInterface;
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
                case FunctionType.ScriptInterface:
                    return FunctionType.EntityInterface;
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
                case FunctionType.StealCamera:
                    return FunctionType.EntityInterface;
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
                case FunctionType.TogglePlayerTorch:
                    return FunctionType.EntityInterface;
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
            throw new Exception("Unhandled function type");
        }

        /* Applies all default parameter data to a Function entity (DESTRUCTIVE!) */
        private static void ApplyDefaultsInternal(Entity entity, FunctionType type)
        {
            switch (type)
            {
                case FunctionType.EntityMethodInterface:
                    entity.AddParameter("reference", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("pause", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("resume", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("attach", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("detach", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("open", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("close", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("floating", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("sinking", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("lock", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("unlock", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("show", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("spawn", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("despawn", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("light_switch_on", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("light_switch_off", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("proxy_enable", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("proxy_disable", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("simulate", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("keyframe", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("suspend", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("allow", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("request_open", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("request_close", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("request_lock", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("request_unlock", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("force_open", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("force_close", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("request_restore", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("rewind", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("kill", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("set", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("request_load", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("cancel_load", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("request_unload", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("cancel_unload", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("task_end", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("set_as_next_task", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("completed_pre_move", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("completed_interrupt", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("allow_early_end", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_allowing_interrupts", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("set_is_open", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("set_is_closed", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("pause_activity", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("resume_activity", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("clear", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("add_character", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("remove_character", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("purge", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("abort", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Evaluate", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("terminate", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("cancel", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reloading", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("out_of_ammo", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("started_aiming", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stopped_aiming", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("expire", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Pin1", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Pin2", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Pin3", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Pin4", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Pin5", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Pin6", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Pin7", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Pin8", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Pin9", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Pin10", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Up", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Down", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Random", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_all", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_Random_1", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_Random_2", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_Random_3", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_Random_4", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_Random_5", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_Random_6", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_Random_7", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_Random_8", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_Random_9", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_Random_10", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_0", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_1", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_2", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_3", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_4", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_5", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_6", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_7", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_8", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_9", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_10", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_11", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_12", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_13", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_14", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_15", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Trigger_16", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("clear_user", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("clear_all", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("clear_of_alignment", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("clear_last", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_dynamic_rtpc", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_dynamic_rtpc", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("fail_game", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_X", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_X", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_Y", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_Y", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_Z", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_Z", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("fade_out", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("set_decal_time", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("increase_aggro", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("decrease_aggro", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("force_stand_down", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("force_aggressive", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("load_bank", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("unload_bank", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("bank_loaded", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("set_override", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_stealth", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_stealth", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_threat", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_threat", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_music", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_music", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("trigger_now", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("barrier_open", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("barrier_close", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_override", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_override", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("clear_pending_ui", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("hide_ui", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("show_ui", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("update_cost", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_chokepoint", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_chokepoint", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("update_squad_params", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_ping", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_ping", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_monitor", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_monitor", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_monitoring", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_monitoring", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("activate_tracker", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("deactivate_tracker", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_benchmark", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_benchmark", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("apply_hide", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("apply_show", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("display_tutorial", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("transition_completed", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("display_tutorial_breathing_1", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("display_tutorial_breathing_2", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("breathing_game_tutorial_fail", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("refresh_value", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("refresh_text", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_emitting", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("activate_camera", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("deactivate_camera", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("activate_behavior", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("deactivate_behavior", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("activate_modifier", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("deactivate_modifier", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("force_disable_highlight", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("cutting_panel_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("cutting_panel_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("keypad_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("keypad_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("traversal_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("lever_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("lever_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("button_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("button_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("ladder_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("ladder_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("hacking_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("hacking_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("rewire_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("rewire_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("terminal_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("terminal_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("suit_change_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("suit_change_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("cutscene_visibility_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("cutscene_visibility_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("hiding_visibility_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("hiding_visibility_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_radial", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_radial", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_radial_hacking_info", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_radial_hacking_info", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_radial_cutting_info", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_radial_cutting_info", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_radial_battery_info", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_radial_battery_info", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_hud_battery_info", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("enable_hud_battery_info", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("hide_objective_message", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("show_objective_message", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("finished_closing_container", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("seed", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("ignite", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("electrify", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("drench", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("poison", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("set_active", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("set_inactive", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("level_fade_start", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("level_fade_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("torch_turned_on", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("torch_turned_off", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("torch_new_battery_added", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("torch_battery_has_expired", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("torch_low_power", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("turn_off_torch", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("turn_on_torch", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("toggle_torch", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("resume_torch", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("allow_torch", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_timer", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_timer", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("notify_animation_started", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("notify_animation_finished", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("load_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("unload_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("pause_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("resume_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("turn_on_system", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("turn_off_system", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("force_killtrap", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("cancel_force_killtrap", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disable_killtrap", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("cancel_disable_killtrap", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("hit_by_flamethrower", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("cancel_hit_by_flamethrower", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reload_fill", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reload_empty", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reload_load", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reload_open", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reload_fire", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reload_finish", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("display_hacking_upgrade", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("hide_hacking_upgrade", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("reset_hacking_success_flag", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("impact_with_world", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("start_interaction", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("stop_interaction", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("allow_interrupt", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("disallow_interrupt", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Get_In", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Add_NPC", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("Start_Breathing_Game", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("End_Breathing_Game", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("bind_all", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("verify", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("fake_light_on", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("fake_light_off", new cFloat(), ParameterVariant.METHOD); //
                    entity.AddParameter("callback_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("trigger_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("refresh_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("pause_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("resume_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("attach_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("detach_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("close_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("floating_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("sinking_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("lock_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("unlock_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("show_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("hide_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("spawn_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("despawn_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("light_switch_on_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("light_switch_off_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("proxy_enable_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("proxy_disable_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("simulate_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("keyframe_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("suspend_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("allow_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("request_open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("request_close_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("request_lock_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("request_unlock_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("force_open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("force_close_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("request_restore_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("rewind_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("kill_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("set_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("request_load_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("cancel_load_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("request_unload_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("cancel_unload_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("task_end_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("set_as_next_task_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("completed_pre_move_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("completed_interrupt_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("allow_early_end_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_allowing_interrupts_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("set_true_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("set_false_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("set_is_open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("set_is_closed_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("apply_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("apply_stop_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("pause_activity_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("resume_activity_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("clear_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enter_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("exit_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("add_character_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("remove_character_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("purge_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("abort_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Evaluate_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("terminate_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("cancel_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("impact_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reloading_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("out_of_ammo_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("started_aiming_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stopped_aiming_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("expire_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Pin1_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Pin2_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Pin3_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Pin4_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Pin5_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Pin6_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Pin7_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Pin8_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Pin9_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Pin10_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Up_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Down_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Random_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_all_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_Random_1_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_Random_2_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_Random_3_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_Random_4_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_Random_5_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_Random_6_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_Random_7_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_Random_8_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_Random_9_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_Random_10_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_0_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_1_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_2_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_3_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_4_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_5_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_6_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_7_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_8_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_9_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_10_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_11_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_12_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_13_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_14_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_15_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Trigger_16_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("clear_user_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("clear_all_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("clear_of_alignment_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("clear_last_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_dynamic_rtpc_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_dynamic_rtpc_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("fail_game_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_X_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_X_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_Y_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_Y_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_Z_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_Z_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("fade_out_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("set_decal_time_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("increase_aggro_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("decrease_aggro_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("force_stand_down_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("force_aggressive_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("load_bank_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("unload_bank_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("bank_loaded_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("set_override_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_stealth_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_stealth_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_threat_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_threat_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_music_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_music_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("trigger_now_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("barrier_open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("barrier_close_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_override_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_override_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("clear_pending_ui_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("hide_ui_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("show_ui_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("update_cost_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_chokepoint_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_chokepoint_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("update_squad_params_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_ping_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_ping_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_monitor_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_monitor_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_monitoring_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_monitoring_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("activate_tracker_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("deactivate_tracker_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_benchmark_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_benchmark_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("apply_hide_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("apply_show_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("display_tutorial_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("transition_completed_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("display_tutorial_breathing_1_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("display_tutorial_breathing_2_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("breathing_game_tutorial_fail_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("refresh_value_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("refresh_text_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_emitting_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("activate_camera_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("deactivate_camera_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("activate_behavior_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("deactivate_behavior_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("activate_modifier_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("deactivate_modifier_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("force_disable_highlight_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("cutting_panel_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("cutting_panel_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("keypad_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("keypad_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("traversal_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("lever_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("lever_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("button_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("button_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("ladder_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("ladder_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("hacking_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("hacking_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("rewire_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("rewire_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("terminal_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("terminal_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("suit_change_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("suit_change_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("cutscene_visibility_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("cutscene_visibility_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("hiding_visibility_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("hiding_visibility_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_radial_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_radial_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_radial_hacking_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_radial_hacking_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_radial_cutting_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_radial_cutting_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_radial_battery_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_radial_battery_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_hud_battery_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("enable_hud_battery_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("hide_objective_message_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("show_objective_message_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("finished_closing_container_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("seed_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("ignite_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("electrify_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("drench_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("poison_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("set_active_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("set_inactive_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("level_fade_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("level_fade_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("torch_turned_on_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("torch_turned_off_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("torch_new_battery_added_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("torch_battery_has_expired_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("torch_low_power_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("turn_off_torch_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("turn_on_torch_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("toggle_torch_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("resume_torch_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("allow_torch_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_timer_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_timer_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("notify_animation_started_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("notify_animation_finished_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("load_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("unload_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("pause_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("resume_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("turn_on_system_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("turn_off_system_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("force_killtrap_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("cancel_force_killtrap_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disable_killtrap_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("cancel_disable_killtrap_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("hit_by_flamethrower_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("cancel_hit_by_flamethrower_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reload_fill_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reload_empty_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reload_load_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reload_open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reload_fire_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reload_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("display_hacking_upgrade_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("hide_hacking_upgrade_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("reset_hacking_success_flag_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("impact_with_world_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("start_interaction_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("stop_interaction_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("allow_interrupt_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("disallow_interrupt_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Get_In_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Add_NPC_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("Start_Breathing_Game_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("End_Breathing_Game_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("bind_all_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("verify_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("fake_light_on_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("fake_light_off_finished", new cFloat(), ParameterVariant.FINISHED); //
                    entity.AddParameter("triggered", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("refreshed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stopped", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("paused", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("resumed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("attached", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("detached", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("opened", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("enabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("disabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("disabled_gravity", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("enabled_gravity", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("locked", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("unlocked", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("shown", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hidden", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("spawned", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("despawned", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("light_switched_on", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("light_switched_off", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("proxy_enabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("proxy_disabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("simulating", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("keyframed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("suspended", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("allowed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("requested_open", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("requested_close", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("requested_lock", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("requested_unlock", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("forced_open", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("forced_close", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("requested_restore", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("rewound", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("killed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("been_set", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("load_requested", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("load_cancelled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("unload_requested", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("unload_cancelled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("task_ended", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("task_set_as_next", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_pre_move_completed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_completed_interrupt", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_early_end_allowed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_start_allowing_interrupts", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("set_to_true", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("set_to_false", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("set_to_open", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("set_to_closed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("start_applied", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stop_applied", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("pause_applied", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("resume_applied", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("cleared", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("entered", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("exited", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("reseted", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("added", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("removed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("purged", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("aborted", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Evaluated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("terminated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("cancelled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("impacted", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("reloading_handled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("out_of_ammo_handled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("started_aiming_handled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stopped_aiming_handled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("expired", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin1_Instant", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin2_Instant", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin3_Instant", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin4_Instant", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin5_Instant", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin6_Instant", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin7_Instant", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin8_Instant", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin9_Instant", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin10_Instant", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_Up", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_Down", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_Random", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_all", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_Random_1", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_Random_2", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_Random_3", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_Random_4", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_Random_5", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_Random_6", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_Random_7", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_Random_8", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_Random_9", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_reset_Random_10", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_0", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_1", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_2", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_3", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_4", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_5", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_6", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_7", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_8", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_9", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_10", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_11", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_12", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_13", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_14", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_15", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Pin_16", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("user_cleared", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("started_X", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stopped_X", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("started_Y", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stopped_Y", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("started_Z", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stopped_Z", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("faded_out", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("decal_time_set", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("aggro_increased", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("aggro_decreased", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("forced_stand_down", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("forced_aggressive", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("ui_hidden", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("ui_shown", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_updated_cost", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_enable_chokepoint", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("on_disable_chokepoint", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("squad_params_updated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("started_ping", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stopped_ping", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("started_monitor", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stopped_monitor", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("started_monitoring", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stopped_monitoring", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("activated_tracker", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("deactivated_tracker", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("started_benchmark", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stopped_benchmark", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hide_applied", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("show_applied", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("value_refeshed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("text_refeshed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("stopped_emitting", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("camera_activated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("camera_deactivated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("behavior_activated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("behavior_deactivated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("modifier_activated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("modifier_deactivated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("cutting_pannel_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("cutting_pannel_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("keypad_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("keypad_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("traversal_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("lever_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("lever_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("button_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("button_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("ladder_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("ladder_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hacking_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hacking_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("rewire_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("rewire_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("terminal_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("terminal_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("suit_change_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("suit_change_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("cutscene_visibility_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("cutscene_visibility_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hiding_visibility_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hiding_visibility_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("radial_disabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("radial_enabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("radial_hacking_info_disabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("radial_hacking_info_enabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("radial_cutting_info_disabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("radial_cutting_info_enabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("radial_battery_info_disabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("radial_battery_info_enabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hud_battery_info_disabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hud_battery_info_enabled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("objective_message_hidden", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("objective_message_shown", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("closing_container_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("seeded", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("activated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("deactivated", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("level_fade_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("level_fade_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Turn_off_", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Turn_on_", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Toggle_Torch_", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Resume_", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Allow_", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("timer_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("timer_stopped", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("cutscene_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("cutscene_stopped", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("cutscene_paused", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("cutscene_resumed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("killtrap_forced", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("canceled_force_killtrap", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("upon_hit_by_flamethrower", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("reload_filled", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("reload_emptied", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("reload_loaded", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("reload_opened", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("reload_fired", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("reload_finished", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hacking_upgrade_displayed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hacking_upgrade_hidden", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("hacking_success_flag_reset", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("impacted_with_world", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("interaction_stopped", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("interrupt_allowed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("interrupt_disallowed", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Getting_in", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Breathing_Game_Started", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("Breathing_Game_Ended", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("fake_light_on_triggered", new cFloat(), ParameterVariant.RELAY); //
                    entity.AddParameter("fake_light_off_triggered", new cFloat(), ParameterVariant.RELAY); //
                    break;
                case FunctionType.ScriptInterface:
                    entity.AddParameter("delete_me", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("name", new cString(" "), ParameterVariant.PARAMETER); //String 
                    break;
                case FunctionType.ProxyInterface:
                    entity.AddParameter("proxy_filter_targets", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("proxy_enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.ScriptVariable:
                    entity.AddParameter("on_changed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_restored", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.SensorInterface:
                    entity.AddParameter("start_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.CloseableInterface:
                    entity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.GateInterface:
                    entity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("lock_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.ZoneInterface:
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_unloaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_streaming", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("force_visible_on_load", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.AttachmentInterface:
                    entity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("attachment", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.SensorAttachmentInterface:
                    entity.AddParameter("start_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.CompositeInterface:
                    entity.AddParameter("is_template", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("local_only", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("suspend_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("is_shared", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("requires_script_for_current_gen", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("requires_script_for_next_gen", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("convert_to_physics", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("delete_standard_collision", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("delete_ballistic_collision", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("disable_display", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("disable_collision", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("disable_simulation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("mapping", new cString(), ParameterVariant.PARAMETER); //FilePath
                    entity.AddParameter("include_in_planar_reflections", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.EnvironmentModelReference:
                    cResource resourceData2 = new cResource(entity.shortGUID);
                    resourceData2.AddResource(ResourceType.ANIMATED_MODEL);
                    entity.parameters.Add(new Parameter("resource", resourceData2, ParameterVariant.INTERNAL));
                    break;
                case FunctionType.SplinePath:
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("orientated", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("points", new cSpline(), ParameterVariant.INTERNAL); //SplineData
                    break;
                case FunctionType.Box:
                    entity.AddParameter("event", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.HasAccessAtDifficulty:
                    entity.AddParameter("difficulty", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.UpdateLeaderBoardDisplay:
                    entity.AddParameter("time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SetNextLoadingMovie:
                    entity.AddParameter("playlist_to_load", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ButtonMashPrompt:
                    entity.AddParameter("on_back_to_zero", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_degrade", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_mashed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("count", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("mashes_to_completion", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("time_between_degrades", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("use_degrade", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("hold_to_charge", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GetFlashIntValue:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("int_value", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("callback_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.GetFlashFloatValue:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("float_value", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("callback_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.Sphere:
                    entity.AddParameter("event", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ImpactSphere:
                    entity.AddParameter("event", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UiSelectionBox:
                    entity.AddParameter("is_priority", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UiSelectionSphere:
                    entity.AddParameter("is_priority", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CollisionBarrier:
                    entity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("collision_type", new cEnum(EnumType.COLLISION_TYPE, 0), ParameterVariant.PARAMETER); //COLLISION_TYPE
                    entity.AddParameter("static_collision", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayerTriggerBox:
                    entity.AddParameter("on_entered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_exited", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.PlayerUseTriggerBox:
                    entity.AddParameter("on_entered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_exited", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_use", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("text", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ModelReference:
                    entity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("simulate_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("convert_to_physics", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("material", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("occludes_atmosphere", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("lod_ranges", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("intensity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("radiosity_multiplier", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("emissive_tint", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("replace_intensity", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("replace_tint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("decal_scale", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("lightdecal_tint", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("lightdecal_intensity", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("diffuse_colour_scale", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("diffuse_opacity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("vertex_colour_scale", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("vertex_opacity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("uv_scroll_speed_x", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("uv_scroll_speed_y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("alpha_blend_noise_power_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("alpha_blend_noise_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("alpha_blend_noise_uv_offset_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("alpha_blend_noise_uv_offset_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("dirt_multiply_blend_spec_power_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("dirt_map_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("remove_on_damaged", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("damage_threshold", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("is_debris", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_prop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_thrown", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("report_sliding", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("force_keyframed", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("force_transparent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("soft_collision", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("allow_reposition_of_physics", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("disable_size_culling", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("cast_shadows", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("cast_shadows_in_torch", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("alpha_light_offset_x", new cFloat(0.0f), ParameterVariant.INTERNAL); //float
                    entity.AddParameter("alpha_light_offset_y", new cFloat(0.0f), ParameterVariant.INTERNAL); //float
                    entity.AddParameter("alpha_light_scale_x", new cFloat(1.0f), ParameterVariant.INTERNAL); //float
                    entity.AddParameter("alpha_light_scale_y", new cFloat(1.0f), ParameterVariant.INTERNAL); //float
                    entity.AddParameter("alpha_light_average_normal", new cVector3(), ParameterVariant.INTERNAL); //Direction
                    cResource resourceData = new cResource(entity.shortGUID);
                    resourceData.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    entity.parameters.Add(new Parameter("resource", resourceData, ParameterVariant.INTERNAL));
                    break;
                case FunctionType.LightReference:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("occlusion_geometry", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //RENDERABLE_INSTANCE
                    entity.AddParameter("mastered_by_visibility", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("exclude_shadow_entities", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("type", new cEnum(EnumType.LIGHT_TYPE, 0), ParameterVariant.PARAMETER); //LIGHT_TYPE
                    entity.AddParameter("defocus_attenuation", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("start_attenuation", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("end_attenuation", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("physical_attenuation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("near_dist", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("near_dist_shadow_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("inner_cone_angle", new cFloat(22.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("outer_cone_angle", new cFloat(45.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("intensity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("radiosity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("area_light_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("diffuse_softness", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("diffuse_bias", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("glossiness_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("flare_occluder_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("flare_spot_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("flare_intensity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("cast_shadow", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("fade_type", new cEnum(EnumType.LIGHT_FADE_TYPE, 1), ParameterVariant.PARAMETER); //LIGHT_FADE_TYPE
                    entity.AddParameter("is_specular", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("has_lens_flare", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("has_noclip", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_square_light", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_flash_light", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("no_alphalight", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("shadow_priority", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("aspect_ratio", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("gobo_texture", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("horizontal_gobo_flip", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("strip_length", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("distance_mip_selection_gobo", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("volume", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("volume_end_attenuation", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("volume_colour_factor", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("volume_density", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("depth_bias", new cFloat(0.05f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("slope_scale_depth_bias", new cInteger(1), ParameterVariant.PARAMETER); //int
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.ParticleEmitterReference:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("mastered_by_visibility", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("material", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("unique_material", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("quality_level", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("bounds_max", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("bounds_min", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("DRAW_PASS", new cInteger(8), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ASPECT_RATIO", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FADE_AT_DISTANCE", new cFloat(5000.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PARTICLE_COUNT", new cInteger(100), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SYSTEM_EXPIRY_TIME", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SIZE_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SIZE_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SIZE_END_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SIZE_END_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHA_IN", new cFloat(0.01f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHA_OUT", new cFloat(99.99f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MASK_AMOUNT_MIN", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MASK_AMOUNT_MAX", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MASK_AMOUNT_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PARTICLE_EXPIRY_TIME_MIN", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PARTICLE_EXPIRY_TIME_MAX", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("COLOUR_SCALE_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("COLOUR_SCALE_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIND_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIND_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIND_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHA_REF_VALUE", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("BILLBOARDING_LS", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARDING", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARDING_NONE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARDING_ON_AXIS_X", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARDING_ON_AXIS_Y", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARDING_ON_AXIS_Z", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARDING_VELOCITY_ALIGNED", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARDING_VELOCITY_STRETCHED", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARDING_SPHERE_PROJECTION", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BLENDING_STANDARD", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BLENDING_ALPHA_REF", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BLENDING_ADDITIVE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BLENDING_PREMULTIPLIED", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BLENDING_DISTORTION", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("LOW_RES", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("EARLY_ALPHA", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("LOOPING", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ANIMATED_ALPHA", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("NONE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("LIGHTING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("PER_PARTICLE_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("X_AXIS_FLIP", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Y_AXIS_FLIP", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARD_FACING", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARDING_ON_AXIS_FADEOUT", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BILLBOARDING_CAMERA_LOCKED", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("CAMERA_RELATIVE_POS_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CAMERA_RELATIVE_POS_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CAMERA_RELATIVE_POS_Z", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPHERE_PROJECTION_RADIUS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DISTORTION_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SCALE_MODIFIER", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CPU", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SPAWN_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPAWN_RATE_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPAWN_NUMBER", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("LIFETIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LIFETIME_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_START", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_END", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WORLD_TO_LOCAL_MAX_DIST", new cFloat(1000.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CELL_EMISSION", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("CELL_MAX_DIST", new cFloat(6.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CUSTOM_SEED_CPU", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SEED", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ALPHA_TEST", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ZTEST", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("START_MID_END_SPEED", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SPEED_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPEED_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPEED_MID_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPEED_MID_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPEED_END_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPEED_END_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LAUNCH_DECELERATE_SPEED", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("LAUNCH_DECELERATE_SPEED_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LAUNCH_DECELERATE_SPEED_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LAUNCH_DECELERATE_DEC_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EMISSION_AREA", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("EMISSION_AREA_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EMISSION_AREA_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EMISSION_AREA_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EMISSION_SURFACE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("EMISSION_DIRECTION_SURFACE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("AREA_CUBOID", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("AREA_SPHEROID", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("AREA_CYLINDER", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("PIVOT_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PIVOT_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("GRAVITY", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("GRAVITY_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("GRAVITY_MAX_STRENGTH", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("COLOUR_TINT", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("COLOUR_TINT_START", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("COLOUR_TINT_END", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("COLOUR_USE_MID", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("COLOUR_TINT_MID", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("COLOUR_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPREAD_FEATURE", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SPREAD_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPREAD", new cFloat(360.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ROTATION", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ROTATION_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ROTATION_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ROTATION_RANDOM_START", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ROTATION_BASE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ROTATION_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ROTATION_RAMP", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ROTATION_IN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ROTATION_OUT", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ROTATION_DAMP", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FADE_NEAR_CAMERA", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("FADE_NEAR_CAMERA_MAX_DIST", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FADE_NEAR_CAMERA_THRESHOLD", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TEXTURE_ANIMATION", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("TEXTURE_ANIMATION_FRAMES", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("NUM_ROWS", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("TEXTURE_ANIMATION_LOOP_COUNT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("RANDOM_START_FRAME", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("WRAP_FRAMES", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("NO_ANIM", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SUB_FRAME_BLEND", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SOFTNESS_ALPHA_THICKNESS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SOFTNESS_ALPHA_DEPTH_MODIFIER", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("REVERSE_SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("REVERSE_SOFTNESS_EDGE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PIVOT_AND_TURBULENCE", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("PIVOT_OFFSET_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PIVOT_OFFSET_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TURBULENCE_FREQUENCY_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TURBULENCE_FREQUENCY_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TURBULENCE_AMOUNT_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TURBULENCE_AMOUNT_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHATHRESHOLD", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ALPHATHRESHOLD_TOTALTIME", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHATHRESHOLD_RANGE", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHATHRESHOLD_BEGINSTART", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHATHRESHOLD_BEGINSTOP", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHATHRESHOLD_ENDSTART", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHATHRESHOLD_ENDSTOP", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("COLOUR_RAMP", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("COLOUR_RAMP_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("COLOUR_RAMP_ALPHA", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("DEPTH_FADE_AXIS", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("DEPTH_FADE_AXIS_DIST", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FADE_AXIS_PERCENT", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_UV_ANIMATION", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("FLOW_TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("CYCLE_TIME", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("INFINITE_PROJECTION", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("PARALLAX_POSITION", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DISTORTION_OCCLUSION", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("AMBIENT_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("AMBIENT_LIGHTING_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("NO_CLIP", new cInteger(), ParameterVariant.PARAMETER); //int
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.RibbonEmitterReference:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("mastered_by_visibility", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("material", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("unique_material", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("quality_level", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BLENDING_STANDARD", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BLENDING_ALPHA_REF", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BLENDING_ADDITIVE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BLENDING_PREMULTIPLIED", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BLENDING_DISTORTION", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("NO_MIPS", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("UV_SQUARED", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("LOW_RES", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("LIGHTING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("MASK_AMOUNT_MIN", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MASK_AMOUNT_MAX", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MASK_AMOUNT_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DRAW_PASS", new cInteger(8), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SYSTEM_EXPIRY_TIME", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LIFETIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SMOOTHED", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_START", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_END", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WORLD_TO_LOCAL_MAX_DIST", new cFloat(1000.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TEXTURE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("UV_REPEAT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("UV_SCROLLSPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MULTI_TEXTURE", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("U2_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("V2_REPEAT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("V2_SCROLLSPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MULTI_TEXTURE_BLEND", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("MULTI_TEXTURE_ADD", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("MULTI_TEXTURE_MULT", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("MULTI_TEXTURE_MAX", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("MULTI_TEXTURE_MIN", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SECOND_TEXTURE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("TEXTURE_MAP2", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("CONTINUOUS", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("BASE_LOCKED", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SPAWN_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TRAILING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("INSTANT", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("RATE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("TRAIL_SPAWN_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TRAIL_DELAY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MAX_TRAILS", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("POINT_TO_POINT", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("TARGET_POINT_POSITION", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ABS_FADE_IN_0", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ABS_FADE_IN_1", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FORCES", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("GRAVITY_STRENGTH", new cFloat(-4.81f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("GRAVITY_MAX_STRENGTH", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DRAG_STRENGTH", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIND_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIND_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIND_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("START_MID_END_SPEED", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SPEED_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPEED_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIDTH", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("WIDTH_START", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIDTH_MID", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIDTH_END", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIDTH_IN", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WIDTH_OUT", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("COLOUR_TINT", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("COLOUR_SCALE_START", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("COLOUR_SCALE_MID", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("COLOUR_SCALE_END", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("COLOUR_TINT_START", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("COLOUR_TINT_MID", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("COLOUR_TINT_END", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("ALPHA_FADE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("FADE_IN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FADE_OUT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EDGE_FADE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ALPHA_ERODE", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SIDE_ON_FADE", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SIDE_FADE_START", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SIDE_FADE_END", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DISTANCE_SCALING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("DIST_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPREAD_FEATURE", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SPREAD_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPREAD", new cFloat(0.99999f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EMISSION_AREA", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("EMISSION_AREA_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EMISSION_AREA_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EMISSION_AREA_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("AREA_CUBOID", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("AREA_SPHEROID", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("AREA_CYLINDER", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("COLOUR_RAMP", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("COLOUR_RAMP_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SOFTNESS_ALPHA_THICKNESS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SOFTNESS_ALPHA_DEPTH_MODIFIER", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("AMBIENT_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("AMBIENT_LIGHTING_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("NO_CLIP", new cInteger(), ParameterVariant.PARAMETER); //int
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.GPU_PFXEmitterReference:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("mastered_by_visibility", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("EFFECT_NAME", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("SPAWN_NUMBER", new cInteger(100), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SPAWN_RATE", new cFloat(100.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPREAD_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPREAD_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EMITTER_SIZE", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPEED_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LIFETIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LIFETIME_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FogSphere:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("COLOUR_TINT", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EARLY_ALPHA", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("LOW_RES_ALPHA", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("CONVEX_GEOM", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DISABLE_SIZE_CULLING", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("NO_CLIP", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ALPHA_LIGHTING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DYNAMIC_ALPHA_LIGHTING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("EXPONENTIAL_DENSITY", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("SCENE_DEPENDANT_DENSITY", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("FRESNEL_TERM", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("FRESNEL_POWER", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SOFTNESS", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("BLEND_ALPHA_OVER_DISTANCE", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("FAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("NEAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_BLEND_ALPHA_OVER_DISTANCE", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("SECONDARY_FAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_NEAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_INTERSECT_COLOUR", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DEPTH_INTERSECT_COLOUR_VALUE", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DEPTH_INTERSECT_ALPHA_VALUE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_INTERSECT_RANGE", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.FogBox:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("GEOMETRY_TYPE", new cEnum(EnumType.FOG_BOX_TYPE, 1), ParameterVariant.PARAMETER); //FOG_BOX_TYPE
                    entity.AddParameter("COLOUR_TINT", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DISTANCE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ANGLE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("BILLBOARD", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("EARLY_ALPHA", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("LOW_RES", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("CONVEX_GEOM", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("THICKNESS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("START_DISTANT_CLIP", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("START_DISTANCE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SOFTNESS", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LINEAR_HEIGHT_DENSITY", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("SMOOTH_HEIGHT_DENSITY", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("HEIGHT_MAX_DENSITY", new cFloat(0.4f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FRESNEL_FALLOFF", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("FRESNEL_POWER", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_INTERSECT_COLOUR", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DEPTH_INTERSECT_INITIAL_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DEPTH_INTERSECT_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_INTERSECT_MIDPOINT_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DEPTH_INTERSECT_MIDPOINT_ALPHA", new cFloat(1.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_INTERSECT_MIDPOINT_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_INTERSECT_END_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DEPTH_INTERSECT_END_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_INTERSECT_END_DEPTH", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.SurfaceEffectSphere:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("COLOUR_TINT", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("COLOUR_TINT_OUTER", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FADE_OUT_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SURFACE_WRAP", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ROUGHNESS_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPARKLE_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("METAL_STYLE_REFLECTIONS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SHININESS_OPACITY", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TILING_ZY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TILING_ZX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TILING_XY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("WS_LOCKED", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("SPARKLE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("ENVMAP", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("ENVMAP_PERCENT_EMISSIVE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPHERE", new cBool(), ParameterVariant.PARAMETER); //bool
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.SurfaceEffectBox:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("COLOUR_TINT", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("COLOUR_TINT_OUTER", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FADE_OUT_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SURFACE_WRAP", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ROUGHNESS_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPARKLE_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("METAL_STYLE_REFLECTIONS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SHININESS_OPACITY", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TILING_ZY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TILING_ZX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("TILING_XY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FALLOFF", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("WS_LOCKED", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("SPARKLE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("ENVMAP", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("ENVMAP_PERCENT_EMISSIVE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPHERE", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("BOX", new cBool(), ParameterVariant.PARAMETER); //bool
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.SimpleWater:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("SHININESS", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("softness_edge", new cFloat(0.005f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FRESNEL_POWER", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MIN_FRESNEL", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MAX_FRESNEL", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LOW_RES_ALPHA_PASS", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ATMOSPHERIC_FOGGING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("SPEED", new cFloat(0.01f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_NORMAL_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(-0.01f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHA_MASKING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ALPHA_MASK", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("FLOW_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("CYCLE_TIME", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ENVIRONMENT_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("ENVIRONMENT_MAP_MULT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LOCALISED_ENVIRONMENT_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ENVMAP_SIZE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LOCALISED_ENVMAP_BOX_PROJECTION", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ENVMAP_BOXPROJ_BB_SCALE", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("REFLECTIVE_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("REFLECTION_PERTURBATION_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FOG_INITIAL_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DEPTH_FOG_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_DEPTH", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FOG_END_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DEPTH_FOG_END_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FOG_END_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CAUSTIC_TEXTURE", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("CAUSTIC_TEXTURE_SCALE", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CAUSTIC_REFRACTIONS", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("CAUSTIC_REFLECTIONS", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("CAUSTIC_SPEED_SCALAR", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CAUSTIC_INTENSITY", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CAUSTIC_SURFACE_WRAP", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CAUSTIC_HEIGHT", new cFloat(), ParameterVariant.PARAMETER); //float
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    entity.AddParameter("CAUSTIC_TEXTURE_INDEX", new cInteger(-1), ParameterVariant.INTERNAL); //int
                    break;
                case FunctionType.SimpleRefraction:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("DISTANCEFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_NORMAL_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("SECONDARY_NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHA_MASKING", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ALPHA_MASK", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("DISTORTION_OCCLUSION", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("MIN_OCCLUSION_DISTANCE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_UV_ANIMATION", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("CYCLE_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.ProjectiveDecal:
                    entity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("include_in_planar_reflections", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("material", new cString(" "), ParameterVariant.PARAMETER); //String
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.LODControls:
                    entity.AddParameter("lod_range_scalar", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("disable_lods", new cBool(false), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.LightingMaster:
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.DebugCamera:
                    entity.AddParameter("linked_cameras", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.CameraResource:
                    entity.AddParameter("on_enter_transition_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_exit_transition_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("camera_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("is_camera_transformation_local", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("camera_transformation", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("fov", new cFloat(45.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("clipping_planes_preset", new cEnum(EnumType.CLIPPING_PLANES_PRESETS, 2), ParameterVariant.PARAMETER); //CLIPPING_PLANES_PRESETS
                    entity.AddParameter("is_ghost", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("converge_to_player_camera", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("reset_player_camera_on_exit", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("enable_enter_transition", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("transition_curve_direction", new cEnum(EnumType.TRANSITION_DIRECTION, 4), ParameterVariant.PARAMETER); //TRANSITION_DIRECTION
                    entity.AddParameter("transition_curve_strength", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("enable_exit_transition", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("exit_transition_curve_direction", new cEnum(EnumType.TRANSITION_DIRECTION, 4), ParameterVariant.PARAMETER); //TRANSITION_DIRECTION
                    entity.AddParameter("exit_transition_curve_strength", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("exit_transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("exit_transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("exit_transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraFinder:
                    entity.AddParameter("camera_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.CameraBehaviorInterface:
                    entity.AddParameter("start_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("linked_cameras", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CAMERA_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CAMERA_INSTANCE
                    entity.AddParameter("behavior_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("priority", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("threshold", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("blend_in", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("blend_out", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.HandCamera:
                    entity.AddParameter("noise_type", new cEnum(EnumType.NOISE_TYPE, 0), ParameterVariant.PARAMETER); //NOISE_TYPE
                    entity.AddParameter("frequency", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("damping", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("rotation_intensity", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("min_fov_range", new cFloat(45.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_fov_range", new cFloat(45.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("min_noise", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_noise", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraShake:
                    entity.AddParameter("relative_transformation", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("impulse_intensity", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("impulse_position", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("shake_type", new cEnum(EnumType.SHAKE_TYPE, 0), ParameterVariant.PARAMETER); //SHAKE_TYPE
                    entity.AddParameter("shake_frequency", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("max_rotation_angles", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("max_position_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("shake_rotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("shake_position", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("bone_shaking", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("override_weapon_swing", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("internal_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("external_radius", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("strength_damping", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("explosion_push_back", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("spring_constant", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("spring_damping", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraPathDriven:
                    entity.AddParameter("position_path", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("target_path", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("reference_path", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("position_path_transform", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target_path_transform", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("reference_path_transform", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("point_to_project", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("path_driven_type", new cEnum(EnumType.PATH_DRIVEN_TYPE, 2), ParameterVariant.PARAMETER); //PATH_DRIVEN_TYPE
                    entity.AddParameter("invert_progression", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("position_path_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("target_path_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("animation_duration", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FixedCamera:
                    entity.AddParameter("use_transform_position", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("transform_position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("camera_position", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("camera_target", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("camera_position_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("camera_target_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("apply_target", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("apply_position", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_target_offset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_position_offset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.BoneAttachedCamera:
                    entity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("position_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("rotation_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("movement_damping", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("bone_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ControllableRange:
                    entity.AddParameter("min_range_x", new cFloat(-180.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_range_x", new cFloat(180.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("min_range_y", new cFloat(-180.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_range_y", new cFloat(180.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("min_feather_range_x", new cFloat(-180.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_feather_range_x", new cFloat(180.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("min_feather_range_y", new cFloat(-180.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_feather_range_y", new cFloat(180.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("speed_x", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("speed_y", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("damping_x", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("damping_y", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("mouse_speed_x", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("mouse_speed_y", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.StealCamera:
                    entity.AddParameter("on_converged", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("focus_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("steal_type", new cEnum(EnumType.STEAL_CAMERA_TYPE, 0), ParameterVariant.PARAMETER); //STEAL_CAMERA_TYPE
                    entity.AddParameter("check_line_of_sight", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("blend_in_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FollowCameraModifier:
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("position_curve", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("target_curve", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("modifier_type", new cEnum(EnumType.FOLLOW_CAMERA_MODIFIERS, 0), ParameterVariant.PARAMETER); //FOLLOW_CAMERA_MODIFIERS
                    entity.AddParameter("position_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("target_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("field_of_view", new cFloat(35.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("force_state", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("force_state_initial_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("can_mirror", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_first_person", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("bone_blending_ratio", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("movement_speed", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("movement_speed_vertical", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("movement_damping", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("horizontal_limit_min", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("horizontal_limit_max", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("vertical_limit_min", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("vertical_limit_max", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("mouse_speed_hori", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("mouse_speed_vert", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("acceleration_duration", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("acceleration_ease_in", new cFloat(0.25f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("acceleration_ease_out", new cFloat(0.25f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraPath:
                    entity.AddParameter("linked_splines", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("path_name", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("path_type", new cEnum(EnumType.CAMERA_PATH_TYPE, 0), ParameterVariant.PARAMETER); //CAMERA_PATH_TYPE
                    entity.AddParameter("path_class", new cEnum(EnumType.CAMERA_PATH_CLASS, 0), ParameterVariant.PARAMETER); //CAMERA_PATH_CLASS
                    entity.AddParameter("is_local", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("relative_position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("is_loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraAimAssistant:
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("activation_radius", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("inner_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("camera_speed_attenuation", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("min_activation_distance", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("fading_range", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraPlayAnimation:
                    entity.AddParameter("on_animation_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("animated_camera", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("position_marker", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("character_to_focus", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("focal_length_mm", new cFloat(75.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("focal_plane_m", new cFloat(2.5f), ParameterVariant.INPUT); //float
                    entity.AddParameter("fnum", new cFloat(2.8f), ParameterVariant.INPUT); //float
                    entity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("animation_length", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("frames_count", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("result_transformation", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("data_file", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("start_frame", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("end_frame", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("play_speed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("loop_play", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("clipping_planes_preset", new cEnum(EnumType.CLIPPING_PLANES_PRESETS, 2), ParameterVariant.PARAMETER); //CLIPPING_PLANES_PRESETS
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("dof_key", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("shot_number", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("override_dof", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("focal_point_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("bone_to_focus", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.CamPeek:
                    entity.AddParameter("pos", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("x_ratio", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("y_ratio", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("range_left", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("range_right", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("range_up", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("range_down", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("range_forward", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("range_backward", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("speed_x", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("speed_y", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("damping_x", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("damping_y", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("focal_distance", new cFloat(8.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("focal_distance_y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("roll_factor", new cFloat(15.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("use_ik_solver", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_horizontal_plane", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("stick", new cEnum(EnumType.SIDE, 0), ParameterVariant.PARAMETER); //SIDE
                    entity.AddParameter("disable_collision_test", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CameraDofController:
                    entity.AddParameter("character_to_focus", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("focal_length_mm", new cFloat(75.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("focal_plane_m", new cFloat(2.5f), ParameterVariant.INPUT); //float
                    entity.AddParameter("fnum", new cFloat(2.8f), ParameterVariant.INPUT); //float
                    entity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("focal_point_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("bone_to_focus", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ClipPlanesController:
                    entity.AddParameter("near_plane", new cFloat(0.1f), ParameterVariant.INPUT); //float
                    entity.AddParameter("far_plane", new cFloat(1000.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("update_near", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("update_far", new cBool(false), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.GetCurrentCameraTarget:
                    entity.AddParameter("target", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("distance", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.Logic_Vent_Entrance:
                    entity.AddParameter("Hide_Pos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Emit_Pos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("force_stand_on_exit", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Logic_Vent_System:
                    entity.AddParameter("Vent_Entrances", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.VENT_ENTRANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //VENT_ENTRANCE
                    break;
                case FunctionType.CharacterCommand:
                    entity.AddParameter("command_started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("override_all_ai", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_Follow:
                    entity.AddParameter("entered_inner_radius", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exitted_outer_radius", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Waypoint", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("idle_stance", new cEnum(EnumType.IDLE, 0), ParameterVariant.PARAMETER); //IDLE
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER); //MOVE
                    entity.AddParameter("inner_radius", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("outer_radius", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("prefer_traversals", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_FollowUsingJobs:
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("target_to_follow", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("who_Im_leading", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("fastest_allowed_move_type", new cEnum(EnumType.MOVE, 3), ParameterVariant.PARAMETER); //MOVE
                    entity.AddParameter("slowest_allowed_move_type", new cEnum(EnumType.MOVE, 0), ParameterVariant.PARAMETER); //MOVE
                    entity.AddParameter("centre_job_restart_radius", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("inner_radius", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("outer_radius", new cFloat(8.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("job_select_radius", new cFloat(6.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("job_cancel_radius", new cFloat(8.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("teleport_required_range", new cFloat(25.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("teleport_radius", new cFloat(20.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("prefer_traversals", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("avoid_player", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("allow_teleports", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("follow_type", new cEnum(EnumType.FOLLOW_TYPE, 0), ParameterVariant.PARAMETER); //FOLLOW_TYPE
                    entity.AddParameter("clamp_speed", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_FollowOffset:
                    entity.AddParameter("offset", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("target_to_follow", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.AnimationMask:
                    entity.AddParameter("maskHips", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskTorso", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskNeck", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskHead", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskFace", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskLeftLeg", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskRightLeg", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskLeftArm", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskRightArm", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskLeftHand", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskRightHand", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskLeftFingers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskRightFingers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskTail", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskLips", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskEyes", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskLeftShoulder", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskRightShoulder", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskRoot", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskPrecedingLayers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskSelf", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maskFollowingLayers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("weight", new cFloat(), ParameterVariant.PARAMETER); //float
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.ANIMATION_MASK_RESOURCE);
                    break;
                case FunctionType.CMD_PlayAnimation:
                    entity.AddParameter("Interrupted", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("badInterrupted", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SafePos", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Marker", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("ExitPosition", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("ExternalStartTime", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("ExternalTime", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("OverrideCharacter", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("OptionalMask", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("animationLength", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("AnimationSet", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("Animation", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("StartFrame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("EndFrame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("PlayCount", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("PlaySpeed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("AllowGravity", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("AllowCollision", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Start_Instantly", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("AllowInterruption", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("RemoveMotion", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DisableGunLayer", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("BlendInTime", new cFloat(0.3f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("GaitSyncStart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ConvergenceTime", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LocationConvergence", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("OrientationConvergence", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("UseExitConvergence", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ExitConvergenceTime", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Mirror", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("FullCinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("RagdollEnabled", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("NoIK", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("NoFootIK", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("NoLayers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("PlayerAnimDrivenView", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ExertionFactor", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("AutomaticZoning", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ManualLoading", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("IsCrouchedAnim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("InitiallyBackstage", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Death_by_ragdoll_only", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("dof_key", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("shot_number", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("UseShivaArms", new cBool(true), ParameterVariant.PARAMETER); //bool
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.PLAY_ANIMATION_DATA_RESOURCE);
                    break;
                case FunctionType.CMD_Idle:
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("interrupted", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("target_to_face", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("should_face_target", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("should_raise_gun_while_turning", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("desired_stance", new cEnum(EnumType.CHARACTER_STANCE, 0), ParameterVariant.PARAMETER); //CHARACTER_STANCE
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("idle_style", new cEnum(EnumType.IDLE_STYLE, 1), ParameterVariant.PARAMETER); //IDLE_STYLE
                    entity.AddParameter("lock_cameras", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("anchor", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("start_instantly", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_GoTo:
                    entity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Waypoint", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("AimTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER); //MOVE
                    entity.AddParameter("enable_lookaround", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_stopping_anim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("always_stop_at_radius", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("stop_at_radius_if_lined_up", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("continue_from_previous_move", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("arrived_radius", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("allow_to_use_vents", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DestinationIsBackstage", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("maintain_current_facing", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("start_instantly", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_GoToCover:
                    entity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("entered_cover", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("CoverPoint", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("AimTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER); //MOVE
                    entity.AddParameter("SearchRadius", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("enable_lookaround", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("continue_from_previous_move", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_MoveTowards:
                    entity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("MoveTarget", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("AimTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER); //MOVE
                    entity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("never_succeed", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_Die:
                    entity.AddParameter("Killer", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("death_style", new cEnum(EnumType.DEATH_STYLE, 0), ParameterVariant.PARAMETER); //DEATH_STYLE
                    break;
                case FunctionType.CMD_LaunchMeleeAttack:
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("melee_attack_type", new cEnum(EnumType.MELEE_ATTACK_TYPE, 0), ParameterVariant.PARAMETER); //MELEE_ATTACK_TYPE
                    entity.AddParameter("enemy_type", new cEnum(EnumType.ENEMY_TYPE, 15), ParameterVariant.PARAMETER); //ENEMY_TYPE
                    entity.AddParameter("melee_attack_index", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("skip_convergence", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_ModifyCombatBehaviour:
                    entity.AddParameter("behaviour_type", new cEnum(EnumType.COMBAT_BEHAVIOUR, 0), ParameterVariant.PARAMETER); //COMBAT_BEHAVIOUR
                    entity.AddParameter("status", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_HolsterWeapon:
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("should_holster", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("skip_anims", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("equipment_slot", new cEnum(EnumType.EQUIPMENT_SLOT, -2), ParameterVariant.PARAMETER); //EQUIPMENT_SLOT
                    entity.AddParameter("force_player_unarmed_on_holster", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("force_drop_held_item", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_ForceReloadWeapon:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CMD_ForceMeleeAttack:
                    entity.AddParameter("melee_attack_type", new cEnum(EnumType.MELEE_ATTACK_TYPE, 0), ParameterVariant.PARAMETER); //MELEE_ATTACK_TYPE
                    entity.AddParameter("enemy_type", new cEnum(EnumType.ENEMY_TYPE, 15), ParameterVariant.PARAMETER); //ENEMY_TYPE
                    entity.AddParameter("melee_attack_index", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.CHR_ModifyBreathing:
                    entity.AddParameter("Exhaustion", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_HoldBreath:
                    entity.AddParameter("ExhaustionOnStop", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_DeepCrouch:
                    entity.AddParameter("crouch_amount", new cFloat(0.4f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("smooth_damping", new cFloat(0.4f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("allow_stand_up", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_PlaySecondaryAnimation:
                    entity.AddParameter("Interrupted", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Marker", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("OptionalMask", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("ExternalStartTime", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("ExternalTime", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("animationLength", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("AnimationSet", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("Animation", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("StartFrame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("EndFrame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("PlayCount", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("PlaySpeed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("StartInstantly", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("AllowInterruption", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("BlendInTime", new cFloat(0.3f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("GaitSyncStart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Mirror", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("AnimationLayer", new cEnum(EnumType.SECONDARY_ANIMATION_LAYER, 0), ParameterVariant.PARAMETER); //SECONDARY_ANIMATION_LAYER
                    entity.AddParameter("AutomaticZoning", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ManualLoading", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_LocomotionModifier:
                    entity.AddParameter("Can_Run", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Can_Crouch", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Can_Aim", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Can_Injured", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Must_Walk", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Must_Run", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Must_Crouch", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Must_Aim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Must_Injured", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Is_In_Spacesuit", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_SetMood:
                    entity.AddParameter("mood", new cEnum(EnumType.MOOD, 0), ParameterVariant.PARAMETER); //MOOD
                    entity.AddParameter("moodIntensity", new cEnum(EnumType.MOOD_INTENSITY, 0), ParameterVariant.PARAMETER); //MOOD_INTENSITY
                    entity.AddParameter("timeOut", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_LocomotionEffect:
                    entity.AddParameter("Effect", new cEnum(EnumType.ANIMATION_EFFECT_TYPE, 0), ParameterVariant.PARAMETER); //ANIMATION_EFFECT_TYPE
                    break;
                case FunctionType.CHR_LocomotionDuck:
                    entity.AddParameter("Height", new cEnum(EnumType.DUCK_HEIGHT, 0), ParameterVariant.PARAMETER); //DUCK_HEIGHT
                    break;
                case FunctionType.CMD_ShootAt:
                    entity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.CMD_AimAtCurrentTarget:
                    entity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Raise_gun", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_AimAt:
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("AimTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Raise_gun", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_current_target", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Player_Sensor:
                    entity.AddParameter("Standard", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Running", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Aiming", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Vent", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Grapple", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Death", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Cover", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Motion_Tracked", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Motion_Tracked_Vent", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Leaning", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CMD_Ragdoll:
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("actor", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    entity.AddParameter("impact_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    break;
                case FunctionType.CHR_SetTacticalPosition:
                    entity.AddParameter("tactical_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("sweep_type", new cEnum(EnumType.AREA_SWEEP_TYPE, 0), ParameterVariant.PARAMETER); //AREA_SWEEP_TYPE
                    entity.AddParameter("fixed_sweep_radius", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_SetFocalPoint:
                    entity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("priority", new cEnum(EnumType.PRIORITY, 0), ParameterVariant.PARAMETER); //PRIORITY
                    entity.AddParameter("speed", new cEnum(EnumType.LOOK_SPEED, 1), ParameterVariant.PARAMETER); //LOOK_SPEED
                    entity.AddParameter("steal_camera", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("line_of_sight_test", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_SetAndroidThrowTarget:
                    entity.AddParameter("thrown", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("throw_position", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.CHR_SetAlliance:
                    entity.AddParameter("Alliance", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    break;
                case FunctionType.CHR_GetAlliance:
                    entity.AddParameter("Alliance", new cEnum(), ParameterVariant.OUTPUT); //Enum
                    break;
                case FunctionType.ALLIANCE_SetDisposition:
                    entity.AddParameter("A", new cEnum(EnumType.ALLIANCE_GROUP, 1), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    entity.AddParameter("B", new cEnum(EnumType.ALLIANCE_GROUP, 5), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    entity.AddParameter("Disposition", new cEnum(EnumType.ALLIANCE_STANCE, 1), ParameterVariant.PARAMETER); //ALLIANCE_STANCE
                    break;
                case FunctionType.CHR_SetInvincibility:
                    entity.AddParameter("damage_mode", new cEnum(EnumType.DAMAGE_MODE, 0), ParameterVariant.PARAMETER); //DAMAGE_MODE
                    break;
                case FunctionType.CHR_SetHealth:
                    entity.AddParameter("HealthPercentage", new cInteger(100), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("UsePercentageOfCurrentHeath", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_GetHealth:
                    entity.AddParameter("Health", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.CHR_SetDebugDisplayName:
                    entity.AddParameter("DebugName", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.CHR_TakeDamage:
                    entity.AddParameter("Damage", new cInteger(100), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("DamageIsAPercentage", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 0), ParameterVariant.PARAMETER); //AMMO_TYPE
                    break;
                case FunctionType.CHR_SetSubModelVisibility:
                    entity.AddParameter("is_visible", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("matching", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.CHR_SetHeadVisibility:
                    entity.AddParameter("is_visible", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_SetFacehuggerAggroRadius:
                    entity.AddParameter("radius", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_DamageMonitor:
                    entity.AddParameter("damaged", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("InstigatorFilter", new cBool(true), ParameterVariant.INPUT); //bool
                    entity.AddParameter("DamageDone", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("Instigator", new cFloat(), ParameterVariant.OUTPUT); //Object
                    entity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, -65536), ParameterVariant.PARAMETER); //DAMAGE_EFFECTS
                    break;
                case FunctionType.CHR_KnockedOutMonitor:
                    entity.AddParameter("on_knocked_out", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_recovered", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CHR_DeathMonitor:
                    entity.AddParameter("dying", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("killed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("KillerFilter", new cBool(true), ParameterVariant.INPUT); //bool
                    entity.AddParameter("Killer", new cFloat(), ParameterVariant.OUTPUT); //Object
                    entity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, -65536), ParameterVariant.PARAMETER); //DAMAGE_EFFECTS
                    break;
                case FunctionType.CHR_RetreatMonitor:
                    entity.AddParameter("reached_retreat", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("started_retreating", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CHR_WeaponFireMonitor:
                    entity.AddParameter("fired", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CHR_TorchMonitor:
                    entity.AddParameter("torch_on", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("torch_off", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("TorchOn", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_VentMonitor:
                    entity.AddParameter("entered_vent", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exited_vent", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("IsInVent", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CharacterTypeMonitor:
                    entity.AddParameter("spawned", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("despawned", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("all_despawned", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("AreAny", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 2), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Convo:
                    entity.AddParameter("everyoneArrived", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("playerJoined", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("playerLeft", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("npcJoined", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("members", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.LOGIC_CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //LOGIC_CHARACTER
                    entity.AddParameter("speaker", new cFloat(), ParameterVariant.OUTPUT); //Object
                    entity.AddParameter("alwaysTalkToPlayerIfPresent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("playerCanJoin", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("playerCanLeave", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("positionNPCs", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("circularShape", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("convoPosition", new cFloat(), ParameterVariant.PARAMETER); //Object
                    entity.AddParameter("personalSpaceRadius", new cFloat(0.4f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_NotifyDynamicDialogueEvent:
                    entity.AddParameter("DialogueEvent", new cEnum(EnumType.DIALOGUE_NPC_EVENT, -1), ParameterVariant.PARAMETER); //DIALOGUE_NPC_EVENT
                    break;
                case FunctionType.NPC_Squad_DialogueMonitor:
                    entity.AddParameter("Suspicious_Item_Initial", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Suspicious_Item_Close", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Suspicious_Warning", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Suspicious_Warning_Fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Missing_Buddy", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Search_Started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Search_Loop", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Search_Complete", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Detected_Enemy", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Alien_Heard_Backstage", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Interrogative", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Warning", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Last_Chance", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Stand_Down", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Attack", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Advance", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Melee", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Hit_By_Weapon", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Go_to_Cover", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("No_Cover", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Shoot_From_Cover", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Cover_Broken", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Retreat", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Panic", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Final_Hit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Ally_Death", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Incoming_IED", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Alert_Squad", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("My_Death", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Idle_Passive", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Idle_Aggressive", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Block", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Enter_Grapple", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Grapple_From_Cover", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Player_Observed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("squad_coordinator", new cFloat(), ParameterVariant.PARAMETER); //Object
                    break;
                case FunctionType.NPC_Group_DeathCounter:
                    entity.AddParameter("on_threshold", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("TriggerThreshold", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.NPC_Group_Death_Monitor:
                    entity.AddParameter("last_man_dying", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("all_killed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("squad_coordinator", new cFloat(), ParameterVariant.PARAMETER); //Object
                    entity.AddParameter("CheckAllNPCs", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SenseLimiter:
                    entity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER); //SENSORY_TYPE
                    break;
                case FunctionType.NPC_ResetSensesAndMemory:
                    entity.AddParameter("ResetMenaceToFull", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ResetSensesLimiters", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SetupMenaceManager:
                    entity.AddParameter("AgressiveMenace", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ProgressionFraction", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ResetMenaceMeter", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_AlienConfig:
                    entity.AddParameter("AlienConfigString", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.NPC_SetSenseSet:
                    entity.AddParameter("SenseSet", new cEnum(EnumType.SENSE_SET, 0), ParameterVariant.PARAMETER); //SENSE_SET
                    break;
                case FunctionType.NPC_GetLastSensedPositionOfTarget:
                    entity.AddParameter("NoRecentSense", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SensedOnLeft", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SensedOnRight", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SensedInFront", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SensedBehind", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OptionalTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("LastSensedPosition", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("MaxTimeSince", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.HeldItem_AINotifier:
                    entity.AddParameter("Item", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Duration", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.NPC_Gain_Aggression_In_Radius:
                    entity.AddParameter("Position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Radius", new cFloat(5.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("AggressionGain", new cEnum(EnumType.AGGRESSION_GAIN, 1), ParameterVariant.PARAMETER); //AGGRESSION_GAIN
                    break;
                case FunctionType.NPC_Aggression_Monitor:
                    entity.AddParameter("on_interrogative", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_warning", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_last_chance", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_stand_down", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_idle", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_aggressive", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.Explosion_AINotifier:
                    entity.AddParameter("on_character_damage_fx", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ExplosionPos", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 12), ParameterVariant.PARAMETER); //AMMO_TYPE
                    break;
                case FunctionType.NPC_Sleeping_Android_Monitor:
                    entity.AddParameter("Twitch", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SitUp_Start", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SitUp_End", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Sleeping_GetUp", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Sitting_GetUp", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Android_NPC", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.NPC_Highest_Awareness_Monitor:
                    entity.AddParameter("All_Dead", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Stunned", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Unaware", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Suspicious", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SearchingArea", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SearchingLastSensed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Aware", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_changed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("NPC_Coordinator", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("CheckAllNPCs", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_Squad_GetAwarenessState:
                    entity.AddParameter("All_Dead", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Stunned", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Unaware", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Suspicious", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SearchingArea", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SearchingLastSensed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Aware", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("NPC_Coordinator", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.NPC_Squad_GetAwarenessWatermark:
                    entity.AddParameter("All_Dead", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Stunned", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Unaware", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Suspicious", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SearchingArea", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SearchingLastSensed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Aware", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("NPC_Coordinator", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PlayerCameraMonitor:
                    entity.AddParameter("AndroidNeckSnap", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("AlienKill", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("AlienKillBroken", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("AlienKillInVent", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("StandardAnimDrivenView", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("StopNonStandardCameras", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.ScreenEffectEventMonitor:
                    entity.AddParameter("MeleeHit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("BulletHit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("MedkitHeal", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("StartStrangle", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("StopStrangle", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("StartLowHealth", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("StopLowHealth", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("StartDeath", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("StopDeath", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("AcidHit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("FlashbangHit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("HitAndRun", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("CancelHitAndRun", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.DEBUG_SenseLevels:
                    entity.AddParameter("no_activation", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("trace_activation", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("lower_activation", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("normal_activation", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("upper_activation", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER); //SENSORY_TYPE
                    break;
                case FunctionType.NPC_FakeSense:
                    entity.AddParameter("SensedObject", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("FakePosition", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER); //SENSORY_TYPE
                    entity.AddParameter("ForceThreshold", new cEnum(EnumType.THRESHOLD_QUALIFIER, 2), ParameterVariant.PARAMETER); //THRESHOLD_QUALIFIER
                    break;
                case FunctionType.NPC_SuspiciousItem:
                    entity.AddParameter("ItemPosition", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Item", new cEnum(EnumType.SUSPICIOUS_ITEM, 0), ParameterVariant.PARAMETER); //SUSPICIOUS_ITEM
                    entity.AddParameter("InitialReactionValidStartDuration", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FurtherReactionValidStartDuration", new cFloat(6.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("RetriggerDelay", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Trigger", new cEnum(EnumType.SUSPICIOUS_ITEM_TRIGGER, 1), ParameterVariant.PARAMETER); //SUSPICIOUS_ITEM_TRIGGER
                    entity.AddParameter("ShouldMakeAggressive", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("MaxGroupMembersInteract", new cInteger(2), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SystematicSearchRadius", new cFloat(8.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("AllowSamePriorityToOveride", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("UseSamePriorityCloserDistanceConstraint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("SamePriorityCloserDistanceConstraint", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("UseSamePriorityRecentTimeConstraint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("SamePriorityRecentTimeConstraint", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("BehaviourTreePriority", new cEnum(EnumType.SUSPICIOUS_ITEM_BEHAVIOUR_TREE_PRIORITY, 0), ParameterVariant.PARAMETER); //SUSPICIOUS_ITEM_BEHAVIOUR_TREE_PRIORITY
                    entity.AddParameter("InteruptSubPriority", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("DetectableByBackstageAlien", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DoIntialReaction", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("MoveCloseToSuspectPosition", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DoCloseToReaction", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DoCloseToWaitForGroupMembers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DoSystematicSearch", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("GroupNotify", new cEnum(EnumType.SUSPICIOUS_ITEM_STAGE, 1), ParameterVariant.PARAMETER); //SUSPICIOUS_ITEM_STAGE
                    entity.AddParameter("DoIntialReactionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("MoveCloseToSuspectPositionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DoCloseToReactionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DoCloseToWaitForGroupMembersSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DoSystematicSearchSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SetAlienDevelopmentStage:
                    entity.AddParameter("AlienStage", new cEnum(EnumType.ALIEN_DEVELOPMENT_MANAGER_STAGES, 0), ParameterVariant.PARAMETER); //ALIEN_DEVELOPMENT_MANAGER_STAGES
                    entity.AddParameter("Reset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_TargetAcquire:
                    entity.AddParameter("no_targets", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CHR_IsWithinRange:
                    entity.AddParameter("In_range", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Out_of_range", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Radius", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Height", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Range_test_shape", new cEnum(EnumType.RANGE_TEST_SHAPE, 0), ParameterVariant.PARAMETER); //RANGE_TEST_SHAPE
                    break;
                case FunctionType.NPC_ForceCombatTarget:
                    entity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("LockOtherAttackersOut", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SetAimTarget:
                    entity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.CHR_SetTorch:
                    entity.AddParameter("TorchOn", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_GetTorch:
                    entity.AddParameter("torch_on", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("torch_off", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("TorchOn", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.NPC_SetAutoTorchMode:
                    entity.AddParameter("AutoUseTorchInDark", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_GetCombatTarget:
                    entity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("target", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.NPC_AreaBox:
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.NPC_MeleeContext:
                    entity.AddParameter("ConvergePos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Radius", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Context_Type", new cEnum(EnumType.MELEE_CONTEXT_TYPE, 0), ParameterVariant.PARAMETER); //MELEE_CONTEXT_TYPE
                    break;
                case FunctionType.NPC_SetSafePoint:
                    entity.AddParameter("SafePositions", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.Player_ExploitableArea:
                    entity.AddParameter("NpcSafePositions", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.NPC_SetDefendArea:
                    entity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //NPC_AREA_RESOURCE
                    break;
                case FunctionType.NPC_SetPursuitArea:
                    entity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //NPC_AREA_RESOURCE
                    break;
                case FunctionType.NPC_ForceRetreat:
                    entity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //NPC_AREA_RESOURCE
                    break;
                case FunctionType.NPC_DefineBackstageAvoidanceArea:
                    entity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //NPC_AREA_RESOURCE
                    break;
                case FunctionType.NPC_SetAlertness:
                    entity.AddParameter("AlertState", new cEnum(EnumType.ALERTNESS_STATE, 0), ParameterVariant.PARAMETER); //ALERTNESS_STATE
                    break;
                case FunctionType.NPC_SetStartPos:
                    entity.AddParameter("StartPos", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.NPC_SetAgressionProgression:
                    entity.AddParameter("allow_progression", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SetLocomotionTargetSpeed:
                    entity.AddParameter("Speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 1), ParameterVariant.PARAMETER); //LOCOMOTION_TARGET_SPEED
                    break;
                case FunctionType.NPC_SetGunAimMode:
                    entity.AddParameter("AimingMode", new cEnum(EnumType.NPC_GUN_AIM_MODE, 0), ParameterVariant.PARAMETER); //NPC_GUN_AIM_MODE
                    break;
                case FunctionType.NPC_set_behaviour_tree_flags:
                    entity.AddParameter("BehaviourTreeFlag", new cEnum(EnumType.BEHAVIOUR_TREE_FLAGS, 2), ParameterVariant.PARAMETER); //BEHAVIOUR_TREE_FLAGS
                    entity.AddParameter("FlagSetting", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SetHidingSearchRadius:
                    entity.AddParameter("Radius", new cFloat(), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_SetHidingNearestLocation:
                    entity.AddParameter("hiding_pos", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.NPC_WithdrawAlien:
                    entity.AddParameter("allow_any_searches_to_complete", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("permanent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("killtraps", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("initial_radius", new cFloat(15.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("timed_out_radius", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("time_to_force", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_behaviour_monitor:
                    entity.AddParameter("state_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("state_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("behaviour", new cEnum(EnumType.BEHAVIOR_TREE_BRANCH_TYPE, 0), ParameterVariant.PARAMETER); //BEHAVIOR_TREE_BRANCH_TYPE
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_multi_behaviour_monitor:
                    entity.AddParameter("Cinematic_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Cinematic_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Damage_Response_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Damage_Response_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Target_Is_NPC_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Target_Is_NPC_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Breakout_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Breakout_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Attack_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Attack_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Stunned_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Stunned_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Backstage_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Backstage_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("In_Vent_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("In_Vent_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Killtrap_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Killtrap_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Threat_Aware_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Threat_Aware_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Suspect_Target_Response_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Suspect_Target_Response_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Player_Hiding_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Player_Hiding_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Suspicious_Item_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Suspicious_Item_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Search_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Search_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Area_Sweep_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Area_Sweep_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_ambush_monitor:
                    entity.AddParameter("setup", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("abandoned", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("trap_sprung", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ambush_type", new cEnum(EnumType.AMBUSH_TYPE, 0), ParameterVariant.PARAMETER); //AMBUSH_TYPE
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_navmesh_type_monitor:
                    entity.AddParameter("state_set", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("state_unset", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("nav_mesh_type", new cEnum(EnumType.NAV_MESH_AREA_TYPE, 0), ParameterVariant.PARAMETER); //NAV_MESH_AREA_TYPE
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_HasWeaponOfType:
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER); //WEAPON_TYPE
                    entity.AddParameter("check_if_weapon_draw", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_TriggerAimRequest:
                    entity.AddParameter("started_aiming", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished_aiming", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("interrupted", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("AimTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Raise_gun", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_current_target", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("clamp_angle", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("clear_current_requests", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_TriggerShootRequest:
                    entity.AddParameter("started_shooting", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished_shooting", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("interrupted", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("empty_current_clip", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("shot_count", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("clear_current_requests", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Squad_SetMaxEscalationLevel:
                    entity.AddParameter("max_level", new cEnum(EnumType.NPC_AGGRO_LEVEL, 5), ParameterVariant.PARAMETER); //NPC_AGGRO_LEVEL
                    entity.AddParameter("squad_coordinator", new cFloat(), ParameterVariant.PARAMETER); //Object
                    break;
                case FunctionType.Chr_PlayerCrouch:
                    entity.AddParameter("crouch", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_Once:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.Custom_Hiding_Vignette_controller:
                    entity.AddParameter("StartFade", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("StopFade", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Breath", new cInteger(0), ParameterVariant.INPUT); //int
                    entity.AddParameter("Blackout_start_time", new cInteger(15), ParameterVariant.INPUT); //int
                    entity.AddParameter("run_out_time", new cInteger(60), ParameterVariant.INPUT); //int
                    entity.AddParameter("Vignette", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("FadeValue", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.Custom_Hiding_Controller:
                    entity.AddParameter("Started_Idle", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Started_Exit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Got_Out", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Prompt_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Prompt_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Start_choking", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Start_oxygen_starvation", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Show_MT", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Hide_MT", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Spawn_MT", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Despawn_MT", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Start_Busted_By_Alien", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Start_Busted_By_Android", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("End_Busted_By_Android", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Start_Busted_By_Human", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("End_Busted_By_Human", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Enter_Anim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("Idle_Anim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("Exit_Anim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("has_MT", new cBool(), ParameterVariant.INPUT); //bool
                    entity.AddParameter("is_high", new cBool(), ParameterVariant.INPUT); //bool
                    entity.AddParameter("AlienBusted_Player_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AlienBusted_Alien_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AlienBusted_Player_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AlienBusted_Alien_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AlienBusted_Player_3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AlienBusted_Alien_3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AlienBusted_Player_4", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AlienBusted_Alien_4", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AndroidBusted_Player_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AndroidBusted_Android_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AndroidBusted_Player_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("AndroidBusted_Android_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("MT_pos", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.TorchDynamicMovement:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("torch", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("max_spatial_velocity", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_angular_velocity", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_position_displacement", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_target_displacement", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("position_damping", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("target_damping", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.EQUIPPABLE_ITEM:
                    entity.AddParameter("finished_spawning", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("equipped", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("unequipped", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pickup", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_discard", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_melee_impact", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_used_basic_function", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("spawn_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("item_animated_asset", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("owner", new cFloat(), ParameterVariant.OUTPUT); //Object
                    entity.AddParameter("has_owner", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("character_animation_context", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("character_activate_animation_context", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("left_handed", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("inventory_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("equipment_slot", new cEnum(EnumType.EQUIPMENT_SLOT, 0), ParameterVariant.PARAMETER); //EQUIPMENT_SLOT
                    entity.AddParameter("holsters_on_owner", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("holster_node", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("holster_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("weapon_handedness", new cEnum(EnumType.WEAPON_HANDEDNESS, 0), ParameterVariant.PARAMETER); //WEAPON_HANDEDNESS
                    break;
                case FunctionType.AIMED_ITEM:
                    entity.AddParameter("on_started_aiming", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_stopped_aiming", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_display_on", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_display_off", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_effect_on", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_effect_off", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("target_position", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("average_target_distance", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("min_target_distance", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("fixed_target_distance_for_local_player", new cFloat(6.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.MELEE_WEAPON:
                    entity.AddParameter("item_animated_model_and_collision", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("normal_attack_damage", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("power_attack_damage", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("position_input", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.AIMED_WEAPON:
                    entity.AddParameter("on_fired_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_fired_fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_fired_fail_single", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_impact", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_reload_started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_reload_another", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_reload_empty_clip", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_reload_canceled", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_reload_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_reload_fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_shooting_started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_shooting_wind_down", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_shooting_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_overheated", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_cooled_down", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_charge_complete", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_charge_started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_charge_stopped", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_turned_on", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_turned_off", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_torch_on_requested", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_torch_off_requested", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ammoRemainingInClip", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("ammoToFillClip", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("ammoThatWasInClip", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("charge_percentage", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("charge_noise_percentage", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 2), ParameterVariant.PARAMETER); //WEAPON_TYPE
                    entity.AddParameter("requires_turning_on", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ejectsShellsOnFiring", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("aim_assist_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("default_ammo_type", new cEnum(EnumType.AMMO_TYPE, 0), ParameterVariant.PARAMETER); //AMMO_TYPE
                    entity.AddParameter("starting_ammo", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("clip_size", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("consume_ammo_over_time_when_turned_on", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_auto_shots_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_manual_shots_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("wind_down_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("maximum_continous_fire_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("overheat_recharge_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("automatic_firing", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("overheats", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("charged_firing", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("charging_duration", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("min_charge_to_fire", new cFloat(0.3f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("overcharge_timer", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("charge_noise_start_time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("reloadIndividualAmmo", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("alwaysDoFullReloadOfClips", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("movement_accuracy_penalty_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("aim_rotation_accuracy_penalty_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("accuracy_penalty_per_shot", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("accuracy_accumulated_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("player_exposed_accuracy_penalty_per_shot", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("player_exposed_accuracy_accumulated_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("recoils_on_fire", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("alien_threat_aware", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayerWeaponMonitor:
                    entity.AddParameter("on_clip_above_percentage", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_clip_below_percentage", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_clip_empty", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_clip_full", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 2), ParameterVariant.PARAMETER); //WEAPON_TYPE
                    entity.AddParameter("ammo_percentage_in_clip", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.PlayerDiscardsWeapons:
                    entity.AddParameter("discard_pistol", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_shotgun", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_flamethrower", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_boltgun", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_cattleprod", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_melee", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayerDiscardsItems:
                    entity.AddParameter("discard_ieds", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_medikits", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_ammo", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_flares_and_lights", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_materials", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_batteries", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayerDiscardsTools:
                    entity.AddParameter("discard_motion_tracker", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_cutting_torch", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_hacking_tool", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("discard_keycard", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.WEAPON_GiveToCharacter:
                    entity.AddParameter("Character", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Weapon", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("is_holstered", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.WEAPON_GiveToPlayer:
                    entity.AddParameter("weapon", new cEnum(EnumType.EQUIPMENT_SLOT, 1), ParameterVariant.PARAMETER); //EQUIPMENT_SLOT
                    entity.AddParameter("holster", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("starting_ammo", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.WEAPON_ImpactEffect:
                    entity.AddParameter("StaticEffects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("DynamicEffects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("DynamicAttachedEffects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Type", new cEnum(EnumType.WEAPON_IMPACT_EFFECT_TYPE, 0), ParameterVariant.PARAMETER); //WEAPON_IMPACT_EFFECT_TYPE
                    entity.AddParameter("Orientation", new cEnum(EnumType.WEAPON_IMPACT_EFFECT_ORIENTATION, 0), ParameterVariant.PARAMETER); //WEAPON_IMPACT_EFFECT_ORIENTATION
                    entity.AddParameter("Priority", new cInteger(16), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("SafeDistant", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("LifeTime", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("character_damage_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("RandomRotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.WEAPON_ImpactFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("PhysicMaterial", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.WEAPON_AttackerFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("filter", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.WEAPON_TargetObjectFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("filter", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.WEAPON_ImpactInspector:
                    entity.AddParameter("damage", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("impact_position", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("impact_target", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.WEAPON_DamageFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("damage_threshold", new cInteger(100), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.WEAPON_DidHitSomethingFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.WEAPON_MultiFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("AttackerFilter", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("TargetFilter", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("DamageThreshold", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, 33554432), ParameterVariant.PARAMETER); //DAMAGE_EFFECTS
                    entity.AddParameter("UseAmmoFilter", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 22), ParameterVariant.PARAMETER); //AMMO_TYPE
                    break;
                case FunctionType.WEAPON_ImpactCharacterFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    entity.AddParameter("character_body_location", new cEnum(EnumType.IMPACT_CHARACTER_BODY_LOCATION_TYPE, 0), ParameterVariant.PARAMETER); //IMPACT_CHARACTER_BODY_LOCATION_TYPE
                    break;
                case FunctionType.WEAPON_Effect:
                    entity.AddParameter("WorldPos", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("AttachedEffects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("UnattachedEffects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("LifeTime", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.WEAPON_AmmoTypeFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("AmmoType", new cEnum(EnumType.DAMAGE_EFFECTS, 33554432), ParameterVariant.PARAMETER); //DAMAGE_EFFECTS
                    break;
                case FunctionType.WEAPON_ImpactAngleFilter:
                    entity.AddParameter("greater", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("less", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ReferenceAngle", new cFloat(60.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.WEAPON_ImpactOrientationFilter:
                    entity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ThresholdAngle", new cFloat(15.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Orientation", new cEnum(EnumType.WEAPON_IMPACT_FILTER_ORIENTATION, 2), ParameterVariant.PARAMETER); //WEAPON_IMPACT_FILTER_ORIENTATION
                    break;
                case FunctionType.EFFECT_ImpactGenerator:
                    entity.AddParameter("on_impact", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_failed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("trigger_on_reset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("min_distance", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("distance", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_count", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("spread", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("skip_characters", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.EFFECT_EntityGenerator:
                    entity.AddParameter("entities", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("trigger_on_reset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("spread", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("force_min", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("force_max", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("force_offset_XY_min", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("force_offset_XY_max", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("force_offset_Z_min", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("force_offset_Z_max", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("lifetime_min", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("lifetime_max", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.EFFECT_DirectionalPhysics:
                    entity.AddParameter("relative_direction", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("effect_distance", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("angular_falloff", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("min_force", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_force", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.PlatformConstantBool:
                    entity.AddParameter("NextGen", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("X360", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("PS3", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlatformConstantInt:
                    entity.AddParameter("NextGen", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("X360", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("PS3", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.PlatformConstantFloat:
                    entity.AddParameter("NextGen", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("X360", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PS3", new cFloat(), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.VariableBool:
                    entity.AddParameter("initial_value", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VariableInt:
                    entity.AddParameter("initial_value", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VariableFloat:
                    entity.AddParameter("initial_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VariableString:
                    entity.AddParameter("initial_value", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VariableVector:
                    entity.AddParameter("initial_x", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("initial_y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("initial_z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.VariableVector2:
                    entity.AddParameter("initial_value", new cVector3(), ParameterVariant.INPUT); //Direction
                    break;
                case FunctionType.VariableColour:
                    entity.AddParameter("initial_colour", new cVector3(), ParameterVariant.INPUT); //Direction
                    break;
                case FunctionType.VariableFlashScreenColour:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("initial_colour", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("flash_layer_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.VariableHackingConfig:
                    entity.AddParameter("nodes", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("sensors", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("victory_nodes", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("victory_sensors", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.VariableEnum:
                    entity.AddParameter("initial_value", new cEnum(), ParameterVariant.PARAMETER); //Enum
                    entity.AddParameter("is_persistent", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VariableObject:
                    entity.AddParameter("initial", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.VariableAnimationInfo:
                    entity.AddParameter("AnimationSet", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("Animation", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ExternalVariableBool:
                    entity.AddParameter("game_variable", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.GAME_VARIABLE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //GAME_VARIABLE
                    break;
                case FunctionType.NonPersistentBool:
                    entity.AddParameter("initial_value", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NonPersistentInt:
                    entity.AddParameter("initial_value", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("is_persistent", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GameDVR:
                    entity.AddParameter("start_time", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("duration", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("moment_ID", new cEnum(EnumType.GAME_CLIP, 0), ParameterVariant.PARAMETER); //GAME_CLIP
                    break;
                case FunctionType.Zone:
                    entity.AddParameter("composites", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("suspend_on_unload", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("space_visible", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ZoneLink:
                    entity.AddParameter("ZoneA", new cFloat(), ParameterVariant.INPUT); //ZonePtr
                    entity.AddParameter("ZoneB", new cFloat(), ParameterVariant.INPUT); //ZonePtr
                    entity.AddParameter("cost", new cInteger(6), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.ZoneExclusionLink:
                    entity.AddParameter("ZoneA", new cFloat(), ParameterVariant.INPUT); //ZonePtr
                    entity.AddParameter("ZoneB", new cFloat(), ParameterVariant.INPUT); //ZonePtr
                    entity.AddParameter("exclude_streaming", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ZoneLoaded:
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_unloaded", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.FlushZoneCache:
                    entity.AddParameter("CurrentGen", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("NextGen", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.StateQuery:
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Input", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.BooleanLogicInterface:
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("LHS", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("RHS", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.LogicOnce:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.LogicDelay:
                    entity.AddParameter("on_delay_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("delay", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("can_suspend", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.LogicSwitch:
                    entity.AddParameter("true_now_false", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("false_now_true", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_restored_true", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_restored_false", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("initial_value", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.LogicGate:
                    entity.AddParameter("on_allowed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_disallowed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("allow", new cBool(), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.BooleanLogicOperation:
                    entity.AddParameter("Input", new cBool(), ParameterVariant.INPUT); //bool
                    entity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.FloatMath_All:
                    entity.AddParameter("Numbers", new cFloat(), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatMultiply_All:
                    entity.AddParameter("Invert", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FloatMath:
                    entity.AddParameter("LHS", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("RHS", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatMultiplyClamp:
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.FloatClampMultiply:
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.FloatOperation:
                    entity.AddParameter("Input", new cFloat(), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatCompare:
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("LHS", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("RHS", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Threshold", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.FloatModulate:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("wave_shape", new cEnum(EnumType.WAVE_SHAPE, 0), ParameterVariant.PARAMETER); //WAVE_SHAPE
                    entity.AddParameter("frequency", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("phase", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("amplitude", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("bias", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FloatModulateRandom:
                    entity.AddParameter("on_full_switched_on", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_full_switched_off", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("switch_on_anim", new cEnum(EnumType.LIGHT_TRANSITION, 1), ParameterVariant.PARAMETER); //LIGHT_TRANSITION
                    entity.AddParameter("switch_on_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("switch_on_custom_frequency", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("switch_on_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("switch_off_anim", new cEnum(EnumType.LIGHT_TRANSITION, 1), ParameterVariant.PARAMETER); //LIGHT_TRANSITION
                    entity.AddParameter("switch_off_custom_frequency", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("switch_off_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("behaviour_anim", new cEnum(EnumType.LIGHT_ANIM, 1), ParameterVariant.PARAMETER); //LIGHT_ANIM
                    entity.AddParameter("behaviour_frequency", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("behaviour_frequency_variance", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("behaviour_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("pulse_modulation", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("oscillate_range_min", new cFloat(0.75f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("sparking_speed", new cFloat(0.9f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("blink_rate", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("blink_range_min", new cFloat(0.01f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("flicker_rate", new cFloat(0.75f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("flicker_off_rate", new cFloat(0.15f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("flicker_range_min", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("flicker_off_range_min", new cFloat(0.01f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("disable_behaviour", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FloatLinearProportion:
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Proportion", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatGetLinearProportion:
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Input", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Proportion", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatLinearInterpolateTimed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FloatLinearInterpolateSpeed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FloatLinearInterpolateSpeedAdvanced:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("trigger_on_min", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("trigger_on_max", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("trigger_on_loop", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Min_Value", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Max_Value", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FloatSmoothStep:
                    entity.AddParameter("Low_Edge", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("High_Edge", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Value", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatClamp:
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Value", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FilterAbsorber:
                    entity.AddParameter("output", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("factor", new cFloat(0.95f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("start_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("input", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.IntegerMath_All:
                    entity.AddParameter("Numbers", new cInteger(), ParameterVariant.INPUT); //int
                    entity.AddParameter("Result", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.IntegerMath:
                    entity.AddParameter("LHS", new cInteger(0), ParameterVariant.INPUT); //int
                    entity.AddParameter("RHS", new cInteger(0), ParameterVariant.INPUT); //int
                    entity.AddParameter("Result", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.IntegerOperation:
                    entity.AddParameter("Input", new cInteger(), ParameterVariant.INPUT); //int
                    entity.AddParameter("Result", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.IntegerCompare:
                    entity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("LHS", new cInteger(0), ParameterVariant.INPUT); //int
                    entity.AddParameter("RHS", new cInteger(0), ParameterVariant.INPUT); //int
                    entity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.IntegerAnalyse:
                    entity.AddParameter("Input", new cInteger(0), ParameterVariant.INPUT); //int
                    entity.AddParameter("Result", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("Val0", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Val1", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Val2", new cInteger(2), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Val3", new cInteger(3), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Val4", new cInteger(4), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Val5", new cInteger(5), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Val6", new cInteger(6), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Val7", new cInteger(7), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Val8", new cInteger(8), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Val9", new cInteger(9), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.SetEnum:
                    entity.AddParameter("Output", new cEnum(), ParameterVariant.OUTPUT); //Enum
                    entity.AddParameter("initial_value", new cEnum(), ParameterVariant.PARAMETER); //Enum
                    break;
                case FunctionType.SetString:
                    entity.AddParameter("Output", new cString(""), ParameterVariant.OUTPUT); //String
                    entity.AddParameter("initial_value", new cString(""), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.VectorMath:
                    entity.AddParameter("LHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("RHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorScale:
                    entity.AddParameter("LHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("RHS", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorNormalise:
                    entity.AddParameter("Input", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorModulus:
                    entity.AddParameter("Input", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.ScalarProduct:
                    entity.AddParameter("LHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("RHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.VectorDirection:
                    entity.AddParameter("From", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("To", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.VectorYaw:
                    entity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.VectorRotateYaw:
                    entity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Yaw", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorRotateRoll:
                    entity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Roll", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorRotatePitch:
                    entity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Pitch", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorRotateByPos:
                    entity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("WorldPos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorMultiplyByPos:
                    entity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("WorldPos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.VectorDistance:
                    entity.AddParameter("LHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("RHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.VectorReflect:
                    entity.AddParameter("Input", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Normal", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.SetVector:
                    entity.AddParameter("x", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("y", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("z", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.SetVector2:
                    entity.AddParameter("Input", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.SetColour:
                    entity.AddParameter("Colour", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.GetTranslation:
                    entity.AddParameter("Input", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.GetRotation:
                    entity.AddParameter("Input", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.GetComponentInterface:
                    entity.AddParameter("Input", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.SetPosition:
                    entity.AddParameter("Translation", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Rotation", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Input", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("set_on_reset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PositionDistance:
                    entity.AddParameter("LHS", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("RHS", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.VectorLinearProportion:
                    entity.AddParameter("Initial_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Target_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Proportion", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorLinearInterpolateTimed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Initial_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Target_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Reverse", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    entity.AddParameter("Time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VectorLinearInterpolateSpeed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Initial_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Target_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Reverse", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    entity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.MoveInTime:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("end_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SmoothMove:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("timer", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("start_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("end_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("start_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("end_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.RotateInTime:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_pos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("origin", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("timer", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("time_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("time_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("time_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.RotateAtSpeed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_pos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("origin", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("timer", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("speed_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("speed_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("speed_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PointAt:
                    entity.AddParameter("origin", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("target", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.SetLocationAndOrientation:
                    entity.AddParameter("location", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("axis", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("local_offset", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("axis_is", new cEnum(EnumType.ORIENTATION_AXIS, 2), ParameterVariant.PARAMETER); //ORIENTATION_AXIS
                    break;
                case FunctionType.ApplyRelativeTransform:
                    entity.AddParameter("origin", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("input", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("output", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("use_trigger_entity", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.RandomFloat:
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.RandomInt:
                    entity.AddParameter("Result", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("Min", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("Max", new cInteger(100), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.RandomBool:
                    entity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.RandomVector:
                    entity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    entity.AddParameter("MinX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MaxX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MinY", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MaxY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MinZ", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MaxZ", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Normalised", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.RandomSelect:
                    entity.AddParameter("Input", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //Object
                    entity.AddParameter("Seed", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerRandom:
                    entity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_11", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_12", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Num", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.TriggerRandomSequence:
                    entity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("All_triggered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("current", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.Persistent_TriggerRandomSequence:
                    entity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("All_triggered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("current", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.TriggerWeightedRandom:
                    entity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("current", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("Weighting_01", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Weighting_02", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Weighting_03", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Weighting_04", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Weighting_05", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Weighting_06", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Weighting_07", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Weighting_08", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Weighting_09", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Weighting_10", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("allow_same_pin_in_succession", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayEnvironmentAnimation:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_finished_streaming", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("play_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("jump_to_the_end_on_play", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("marker", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("external_start_time", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("external_time", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("animation_length", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("animation_info", new cFloat(), ParameterVariant.PARAMETER); //AnimationInfoPtr
                    entity.AddParameter("AnimationSet", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("Animation", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("start_frame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("end_frame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("play_speed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("shot_number", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.CAGEAnimation:
                    //newEntity = new CAGEAnimation(thisID);
                    entity.AddParameter("animation_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("animation_interrupted", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("animation_changed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("cinematic_loaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("cinematic_unloaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("external_time", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("current_time", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("use_external_time", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("rewind_on_stop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("jump_to_the_end", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("playspeed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("anim_length", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_cinematic_skippable", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("skippable_timer", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("capture_video", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("capture_clip_name", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("playback", new cFloat(0.0f), ParameterVariant.INTERNAL); //float
                    break;
                case FunctionType.MultitrackLoop:
                    entity.AddParameter("current_time", new cFloat(), ParameterVariant.INPUT); //float
                    entity.AddParameter("loop_condition", new cBool(), ParameterVariant.INPUT); //bool
                    entity.AddParameter("start_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("end_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ReTransformer:
                    entity.AddParameter("new_transform", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.TriggerSequence:
                    //newEntity = new TriggerSequence(thisID);
                    entity.AddParameter("proxy_enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("attach_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("duration", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("trigger_mode", new cEnum(EnumType.ANIM_MODE, 0), ParameterVariant.PARAMETER); //ANIM_MODE
                    entity.AddParameter("random_seed", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("use_random_intervals", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("no_duplicates", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("interval_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.Checkpoint:
                    entity.AddParameter("on_checkpoint", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_captured", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_saved", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished_saving", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished_loading", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("cancelled_saving", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished_saving_to_hdd", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("player_spawn_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("is_first_checkpoint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_first_autorun_checkpoint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("section", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("checkpoint_type", new cEnum(EnumType.CHECKPOINT_TYPE, 0), ParameterVariant.PARAMETER); //CHECKPOINT_TYPE
                    break;
                case FunctionType.MissionNumber:
                    entity.AddParameter("on_changed", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.SetAsActiveMissionLevel:
                    entity.AddParameter("clear_level", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CheckpointRestoredNotify:
                    entity.AddParameter("restored", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.DebugLoadCheckpoint:
                    entity.AddParameter("previous_checkpoint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GameStateChanged:
                    entity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.DisplayMessage:
                    entity.AddParameter("title_id", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("message_id", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.DisplayMessageWithCallbacks:
                    entity.AddParameter("on_yes", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_no", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_cancel", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("title_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("message_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("yes_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("no_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("cancel_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("yes_button", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("no_button", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("cancel_button", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.LevelInfo:
                    entity.AddParameter("save_level_name_id", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.DebugCheckpoint:
                    entity.AddParameter("on_checkpoint", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("section", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("level_reset", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Benchmark:
                    entity.AddParameter("benchmark_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("save_stats", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.EndGame:
                    entity.AddParameter("on_game_end_started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_game_ended", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("success", new cBool(), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.LeaveGame:
                    entity.AddParameter("disconnect_from_session", new cBool(), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.DebugTextStacking:
                    entity.AddParameter("float_input", new cFloat(), ParameterVariant.INPUT); //float
                    entity.AddParameter("int_input", new cInteger(), ParameterVariant.INPUT); //int
                    entity.AddParameter("bool_input", new cBool(), ParameterVariant.INPUT); //bool
                    entity.AddParameter("vector_input", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("enum_input", new cEnum(), ParameterVariant.INPUT); //Enum
                    entity.AddParameter("text", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("size", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("ci_type", new cEnum(EnumType.CI_MESSAGE_TYPE, 0), ParameterVariant.PARAMETER); //CI_MESSAGE_TYPE
                    entity.AddParameter("needs_debug_opt_to_render", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DebugText:
                    entity.AddParameter("duration_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("float_input", new cFloat(), ParameterVariant.INPUT); //float
                    entity.AddParameter("int_input", new cInteger(), ParameterVariant.INPUT); //int
                    entity.AddParameter("bool_input", new cBool(), ParameterVariant.INPUT); //bool
                    entity.AddParameter("vector_input", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("enum_input", new cEnum(), ParameterVariant.INPUT); //Enum
                    entity.AddParameter("text_input", new cString(""), ParameterVariant.INPUT); //String
                    entity.AddParameter("text", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("size", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("alignment", new cEnum(EnumType.TEXT_ALIGNMENT, 4), ParameterVariant.PARAMETER); //TEXT_ALIGNMENT
                    entity.AddParameter("duration", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("pause_game", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("cancel_pause_with_button_press", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("priority", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ci_type", new cEnum(EnumType.CI_MESSAGE_TYPE, 0), ParameterVariant.PARAMETER); //CI_MESSAGE_TYPE
                    break;
                case FunctionType.TutorialMessage:
                    entity.AddParameter("text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("text_list", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TUTORIAL_ENTRY_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //TUTORIAL_ENTRY_ID
                    entity.AddParameter("show_animation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DebugEnvironmentMarker:
                    entity.AddParameter("target", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("float_input", new cFloat(), ParameterVariant.INPUT); //float
                    entity.AddParameter("int_input", new cInteger(), ParameterVariant.INPUT); //int
                    entity.AddParameter("bool_input", new cBool(), ParameterVariant.INPUT); //bool
                    entity.AddParameter("vector_input", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("enum_input", new cEnum(), ParameterVariant.INPUT); //Enum
                    entity.AddParameter("text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("size", new cFloat(20.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("duration", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("scale_with_distance", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("max_string_length", new cInteger(10), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("scroll_speed", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("show_distance_from_target", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DebugPositionMarker:
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.DebugCaptureScreenShot:
                    entity.AddParameter("finished_capturing", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("wait_for_streamer", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("capture_filename", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("fov", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("near", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("far", new cFloat(), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.DebugCaptureCorpse:
                    entity.AddParameter("finished_capturing", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("corpse_name", new cString(""), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.DebugMenuToggle:
                    entity.AddParameter("debug_variable", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("value", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayerTorch:
                    entity.AddParameter("requested_torch_holster", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("requested_torch_draw", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("power_in_current_battery", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("battery_count", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.Master:
                    entity.AddParameter("suspend_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("disable_display", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("disable_collision", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("disable_simulation", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ExclusiveMaster:
                    entity.AddParameter("active_objects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("inactive_objects", new cFloat(), ParameterVariant.INPUT); //Object
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.EXCLUSIVE_MASTER_STATE_RESOURCE);
                    break;
                case FunctionType.ThinkOnce:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("use_random_start", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("random_start_delay", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.Thinker:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("delay_between_triggers", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("is_continuous", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_random_start", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("random_start_delay", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("total_thinking_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.AllPlayersReady:
                    entity.AddParameter("on_all_players_ready", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("activation_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SyncOnAllPlayers:
                    entity.AddParameter("on_synchronized", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_synchronized_host", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.SyncOnFirstPlayer:
                    entity.AddParameter("on_synchronized", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_synchronized_host", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_synchronized_local", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.NetPlayerCounter:
                    entity.AddParameter("on_full", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_empty", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_intermediate", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("is_full", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("is_empty", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("contains_local_player", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.BroadcastTrigger:
                    entity.AddParameter("on_triggered", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.HostOnlyTrigger:
                    entity.AddParameter("on_triggered", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.SpawnGroup:
                    entity.AddParameter("on_spawn_request", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("default_group", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("trigger_on_reset", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.RespawnExcluder:
                    entity.AddParameter("excluded_points", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.RespawnConfig:
                    entity.AddParameter("min_dist", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("preferred_dist", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_dist", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("respawn_mode", new cEnum(EnumType.RESPAWN_MODE, 0), ParameterVariant.PARAMETER); //RESPAWN_MODE
                    entity.AddParameter("respawn_wait_time", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("uncollidable_time", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("is_default", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NumConnectedPlayers:
                    entity.AddParameter("on_count_changed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("count", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.NumPlayersOnStart:
                    entity.AddParameter("count", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.NetworkedTimer:
                    entity.AddParameter("on_second_changed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_started_counting", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_finished_counting", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("time_elapsed", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("time_left", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("time_elapsed_sec", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("time_left_sec", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("duration", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.DebugObjectMarker:
                    entity.AddParameter("marked_object", new cFloat(), ParameterVariant.PARAMETER); //Object
                    entity.AddParameter("marked_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.EggSpawner:
                    entity.AddParameter("egg_position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("hostile_egg", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.RandomObjectSelector:
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("chosen_object", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.CompoundVolume:
                    entity.AddParameter("event", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.TriggerVolumeFilter:
                    entity.AddParameter("on_event_entered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_event_exited", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.TriggerVolumeFilter_Monitored:
                    entity.AddParameter("on_event_entered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_event_exited", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.TriggerFilter:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.TriggerObjectsFilter:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.BindObjectsMultiplexer:
                    entity.AddParameter("Pin1_Bound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin2_Bound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin3_Bound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin4_Bound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin5_Bound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin6_Bound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin7_Bound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin8_Bound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin9_Bound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin10_Bound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.TriggerObjectsFilterCounter:
                    entity.AddParameter("none_passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("some_passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("all_passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.TriggerContainerObjectsFilterCounter:
                    entity.AddParameter("none_passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("some_passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("all_passed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    entity.AddParameter("container", new cFloat(), ParameterVariant.PARAMETER); //Object
                    break;
                case FunctionType.TriggerTouch:
                    entity.AddParameter("touch_event", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("physics_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.COLLISION_MAPPING) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //COLLISION_MAPPING
                    entity.AddParameter("impact_normal", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.TriggerDamaged:
                    entity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("physics_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.COLLISION_MAPPING) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //COLLISION_MAPPING
                    entity.AddParameter("impact_normal", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    entity.AddParameter("threshold", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerBindCharacter:
                    entity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("characters", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.TriggerBindAllCharactersOfType:
                    entity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 2), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TriggerBindCharactersInSquad:
                    entity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.TriggerUnbindCharacter:
                    entity.AddParameter("unbound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.TriggerExtractBoundObject:
                    entity.AddParameter("unbound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("bound_object", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.TriggerExtractBoundCharacter:
                    entity.AddParameter("unbound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("bound_character", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.TriggerDelay:
                    entity.AddParameter("delayed_trigger", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("purged_trigger", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("time_left", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("Hrs", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Sec", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerSwitch:
                    entity.AddParameter("Pin_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_10", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("current", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("loop", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.TriggerSelect:
                    entity.AddParameter("Pin_0", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_10", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_11", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_12", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_13", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_14", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_15", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_16", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Object_0", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_1", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_2", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_3", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_4", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_5", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_6", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_7", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_8", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_9", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_10", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_11", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_12", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_13", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_14", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_15", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_16", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //Object
                    entity.AddParameter("index", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.TriggerSelect_Direct:
                    entity.AddParameter("Changed_to_0", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_10", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_11", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_12", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_13", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_14", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_15", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Changed_to_16", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Object_0", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_1", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_2", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_3", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_4", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_5", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_6", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_7", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_8", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_9", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_10", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_11", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_12", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_13", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_14", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_15", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Object_16", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //Object
                    entity.AddParameter("TriggeredIndex", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("Changes_only", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.TriggerCheckDifficulty:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("DifficultyLevel", new cEnum(EnumType.DIFFICULTY_SETTING_TYPE, 2), ParameterVariant.PARAMETER); //DIFFICULTY_SETTING_TYPE
                    break;
                case FunctionType.TriggerSync:
                    entity.AddParameter("Pin1_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin2_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin3_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin4_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin5_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin6_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin7_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin8_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin9_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin10_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("reset_on_trigger", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.LogicAll:
                    entity.AddParameter("Pin1_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin2_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin3_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin4_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin5_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin6_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin7_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin8_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin9_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin10_Synced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("reset_on_trigger", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Logic_MultiGate:
                    entity.AddParameter("Underflow", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_10", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_11", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_12", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_13", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_14", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_15", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_16", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_17", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_18", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_19", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pin_20", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Overflow", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("trigger_pin", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.Counter:
                    entity.AddParameter("on_under_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_over_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Count", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("is_limitless", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("trigger_limit", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.LogicCounter:
                    entity.AddParameter("on_under_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_over_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("restored_on_under_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("restored_on_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("restored_on_over_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Count", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("is_limitless", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("trigger_limit", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("non_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.LogicPressurePad:
                    entity.AddParameter("Pad_Activated", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pad_Deactivated", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("bound_characters", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Limit", new cInteger(1), ParameterVariant.INPUT); //int
                    entity.AddParameter("Count", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.SetObject:
                    entity.AddParameter("Input", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Output", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.GateResourceInterface:
                    entity.AddParameter("gate_status_changed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("request_open_on_reset", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("request_lock_on_reset", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("force_open_on_reset", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("force_close_on_reset", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("is_auto", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("auto_close_delay", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("is_open", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("is_locked", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("gate_status", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.Door:
                    entity.AddParameter("started_opening", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("started_closing", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished_opening", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished_closing", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("used_locked", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("used_unlocked", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("used_forced_open", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("used_forced_closed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("waiting_to_open", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("highlight", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("unhighlight", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("zone_link", new cFloat(), ParameterVariant.INPUT); //ZoneLinkPtr
                    entity.AddParameter("animation", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ANIMATED_MODEL) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //ANIMATED_MODEL
                    entity.AddParameter("trigger_filter", new cBool(true), ParameterVariant.INPUT); //bool
                    entity.AddParameter("icon_pos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("icon_usable_radius", new cFloat(), ParameterVariant.INPUT); //float
                    entity.AddParameter("show_icon_when_locked", new cBool(true), ParameterVariant.INPUT); //bool
                    entity.AddParameter("nav_mesh", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NAV_MESH_BARRIER_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //NAV_MESH_BARRIER_RESOURCE
                    entity.AddParameter("wait_point_1", new cInteger(), ParameterVariant.INPUT); //int
                    entity.AddParameter("wait_point_2", new cInteger(), ParameterVariant.INPUT); //int
                    entity.AddParameter("geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("is_scripted", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("wait_to_open", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("is_waiting", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("unlocked_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("locked_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("icon_keyframe", new cEnum(EnumType.UI_ICON_ICON, 0), ParameterVariant.PARAMETER); //UI_ICON_ICON
                    entity.AddParameter("detach_anim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("invert_nav_mesh_barrier", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.MonitorPadInput:
                    entity.AddParameter("on_pressed_A", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_A", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_B", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_B", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_X", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_X", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_Y", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_Y", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_L1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_L1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_R1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_R1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_L2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_L2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_R2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_R2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_L3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_L3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_R3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_R3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_dpad_left", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_dpad_left", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_dpad_right", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_dpad_right", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_dpad_up", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_dpad_up", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_dpad_down", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_dpad_down", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("left_stick_x", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("left_stick_y", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("right_stick_x", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("right_stick_y", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.MonitorActionMap:
                    entity.AddParameter("on_pressed_use", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_use", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_crouch", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_crouch", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_run", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_run", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_aim", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_aim", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_shoot", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_shoot", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_reload", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_reload", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_melee", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_melee", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_activate_item", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_activate_item", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_switch_weapon", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_switch_weapon", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_change_dof_focus", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_change_dof_focus", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_select_motion_tracker", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_select_motion_tracker", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_select_torch", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_select_torch", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_torch_beam", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_torch_beam", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_peek", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_peek", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pressed_back_close", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_released_back_close", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("movement_stick_x", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("movement_stick_y", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("camera_stick_x", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("camera_stick_y", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("mouse_x", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("mouse_y", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("analog_aim", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("analog_shoot", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.PadLightBar:
                    entity.AddParameter("colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.PadRumbleImpulse:
                    entity.AddParameter("low_frequency_rumble", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("high_frequency_rumble", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("left_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("right_trigger_impulse", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("aim_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("shoot_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerViewCone:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("target_is_visible", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("no_target_visible", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("target", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("fov", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("max_distance", new cFloat(15.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("aspect_ratio", new cFloat(1.777f), ParameterVariant.INPUT); //float
                    entity.AddParameter("source_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    entity.AddParameter("intersect_with_geometry", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("visible_target", new cFloat(), ParameterVariant.OUTPUT); //Object
                    entity.AddParameter("target_offset", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER); //VIEWCONE_TYPE
                    entity.AddParameter("visible_area_horizontal", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("visible_area_vertical", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerCameraViewCone:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("target", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("fov", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("aspect_ratio", new cFloat(1.777f), ParameterVariant.INPUT); //float
                    entity.AddParameter("intersect_with_geometry", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("use_camera_fov", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("target_offset", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER); //VIEWCONE_TYPE
                    entity.AddParameter("visible_area_horizontal", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("visible_area_vertical", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerCameraViewConeMulti:
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enter1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enter2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enter3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enter4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enter5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enter6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enter7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enter8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enter9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("target", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target1", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target2", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target3", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target4", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target5", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target6", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target7", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target8", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target9", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("fov", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("aspect_ratio", new cFloat(1.777f), ParameterVariant.INPUT); //float
                    entity.AddParameter("intersect_with_geometry", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("number_of_inputs", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("use_camera_fov", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER); //VIEWCONE_TYPE
                    entity.AddParameter("visible_area_horizontal", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("visible_area_vertical", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerCameraVolume:
                    entity.AddParameter("inside", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("inside_factor", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("lookat_factor", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("lookat_X_position", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("lookat_Y_position", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("start_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_Debug_Menu_Item:
                    entity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.Character:
                    entity.AddParameter("finished_spawning", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished_respawning", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("dead_container_take_slot", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("dead_container_emptied", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_ragdoll_impact", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_footstep", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_despawn_requested", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("spawn_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("contents_of_dead_container", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    entity.AddParameter("PopToNavMesh", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("disable_dead_container", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("allow_container_without_death", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("container_interaction_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("anim_set", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ANIM_SET) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //ANIM_SET
                    entity.AddParameter("anim_tree_set", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ANIM_TREE_SET) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //ANIM_TREE_SET
                    entity.AddParameter("attribute_set", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ATTRIBUTE_SET) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //ATTRIBUTE_SET
                    entity.AddParameter("is_player", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_backstage", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("force_backstage_on_respawn", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS, 3), ParameterVariant.PARAMETER); //CHARACTER_CLASS
                    entity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    entity.AddParameter("dialogue_voice", new cEnum(EnumType.DIALOGUE_VOICE_ACTOR, 0), ParameterVariant.PARAMETER); //DIALOGUE_VOICE_ACTOR
                    entity.AddParameter("spawn_id", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("display_model", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("reference_skeleton", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHR_SKELETON_SET) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //CHR_SKELETON_SET
                    entity.AddParameter("torso_sound", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_TORSO_GROUP) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_TORSO_GROUP
                    entity.AddParameter("leg_sound", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_LEG_GROUP) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_LEG_GROUP
                    entity.AddParameter("footwear_sound", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_FOOTWEAR_GROUP) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_FOOTWEAR_GROUP
                    entity.AddParameter("custom_character_type", new cEnum(EnumType.CUSTOM_CHARACTER_TYPE, 0), ParameterVariant.PARAMETER); //CUSTOM_CHARACTER_TYPE
                    entity.AddParameter("custom_character_accessory_override", new cEnum(EnumType.CUSTOM_CHARACTER_ACCESSORY_OVERRIDE, 0), ParameterVariant.PARAMETER); //CUSTOM_CHARACTER_ACCESSORY_OVERRIDE
                    entity.AddParameter("custom_character_population_type", new cEnum(EnumType.CUSTOM_CHARACTER_POPULATION, 0), ParameterVariant.PARAMETER); //CUSTOM_CHARACTER_POPULATION
                    entity.AddParameter("named_custom_character", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("named_custom_character_assets_set", new cEnum(EnumType.CUSTOM_CHARACTER_ASSETS, 0), ParameterVariant.PARAMETER); //CUSTOM_CHARACTER_ASSETS
                    entity.AddParameter("gcip_distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("inventory_set", new cEnum(EnumType.PLAYER_INVENTORY_SET, 0), ParameterVariant.PARAMETER); //PLAYER_INVENTORY_SET
                    break;
                case FunctionType.RegisterCharacterModel:
                    entity.AddParameter("display_model", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("reference_skeleton", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHR_SKELETON_SET) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //CHR_SKELETON_SET
                    break;
                case FunctionType.DespawnPlayer:
                    entity.AddParameter("despawned", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.DespawnCharacter:
                    entity.AddParameter("despawned", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.FilterAnd:
                    entity.AddParameter("filter", new cBool(), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.FilterOr:
                    entity.AddParameter("filter", new cBool(), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.FilterNot:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.FilterIsEnemyOfCharacter:
                    entity.AddParameter("Character", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("use_alliance_at_death", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FilterIsEnemyOfAllianceGroup:
                    entity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    break;
                case FunctionType.FilterIsPhysicsObject:
                    entity.AddParameter("object", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsObject:
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsCharacter:
                    entity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsFacingTarget:
                    entity.AddParameter("target", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("tolerance", new cFloat(45.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FilterBelongsToAlliance:
                    entity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    break;
                case FunctionType.FilterHasWeaponOfType:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER); //WEAPON_TYPE
                    break;
                case FunctionType.FilterHasWeaponEquipped:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER); //WEAPON_TYPE
                    break;
                case FunctionType.FilterIsinInventory:
                    entity.AddParameter("ItemName", new cString(" "), ParameterVariant.INPUT); //String
                    break;
                case FunctionType.FilterIsCharacterClass:
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS, 3), ParameterVariant.PARAMETER); //CHARACTER_CLASS
                    break;
                case FunctionType.FilterIsCharacterClassCombo:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.FilterIsInAlertnessState:
                    entity.AddParameter("AlertState", new cEnum(EnumType.ALERTNESS_STATE, 0), ParameterVariant.PARAMETER); //ALERTNESS_STATE
                    break;
                case FunctionType.FilterIsInLocomotionState:
                    entity.AddParameter("State", new cEnum(EnumType.LOCOMOTION_STATE, 0), ParameterVariant.PARAMETER); //LOCOMOTION_STATE
                    break;
                case FunctionType.FilterCanSeeTarget:
                    entity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsAgressing:
                    entity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsValidInventoryItem:
                    entity.AddParameter("item", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    break;
                case FunctionType.FilterIsInWeaponRange:
                    entity.AddParameter("weapon_owner", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.TriggerWhenSeeTarget:
                    entity.AddParameter("seen", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsPlatform:
                    entity.AddParameter("Platform", new cEnum(EnumType.PLATFORM_TYPE, 5), ParameterVariant.PARAMETER); //PLATFORM_TYPE
                    break;
                case FunctionType.FilterIsUsingDevice:
                    entity.AddParameter("Device", new cEnum(EnumType.INPUT_DEVICE_TYPE, 0), ParameterVariant.PARAMETER); //INPUT_DEVICE_TYPE
                    break;
                case FunctionType.FilterSmallestUsedDifficulty:
                    entity.AddParameter("difficulty", new cEnum(EnumType.DIFFICULTY_SETTING_TYPE, 2), ParameterVariant.PARAMETER); //DIFFICULTY_SETTING_TYPE
                    break;
                case FunctionType.FilterHasPlayerCollectedIdTag:
                    entity.AddParameter("tag_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.IDTAG_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //IDTAG_ID
                    break;
                case FunctionType.FilterHasBehaviourTreeFlagSet:
                    entity.AddParameter("BehaviourTreeFlag", new cEnum(EnumType.BEHAVIOUR_TREE_FLAGS, 2), ParameterVariant.PARAMETER); //BEHAVIOUR_TREE_FLAGS
                    break;
                case FunctionType.Job:
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.JOB_Idle:
                    entity.AddParameter("task_operation_mode", new cEnum(EnumType.TASK_OPERATION_MODE, 0), ParameterVariant.PARAMETER); //TASK_OPERATION_MODE
                    entity.AddParameter("should_perform_all_tasks", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.JOB_SpottingPosition:
                    entity.AddParameter("SpottingPosition", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.Task:
                    entity.AddParameter("start_command", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("selected_by_npc", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("clean_up", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("Job", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("TaskPosition", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    entity.AddParameter("should_stop_moving_when_reached", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("should_orientate_when_reached", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("reached_distance_threshold", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("selection_priority", new cEnum(EnumType.TASK_PRIORITY, 0), ParameterVariant.PARAMETER); //TASK_PRIORITY
                    entity.AddParameter("timeout", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("always_on_tracker", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FlareTask:
                    entity.AddParameter("specific_character", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("filter_options", new cEnum(EnumType.TASK_CHARACTER_CLASS_FILTER, 1024), ParameterVariant.PARAMETER); //TASK_CHARACTER_CLASS_FILTER
                    break;
                case FunctionType.IdleTask:
                    entity.AddParameter("start_pre_move", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_interrupt", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("interrupted_while_moving", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("specific_character", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("should_auto_move_to_position", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("ignored_for_auto_selection", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("has_pre_move_script", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("has_interrupt_script", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("filter_options", new cEnum(EnumType.TASK_CHARACTER_CLASS_FILTER, 1024), ParameterVariant.PARAMETER); //TASK_CHARACTER_CLASS_FILTER
                    break;
                case FunctionType.FollowTask:
                    entity.AddParameter("can_initially_end_early", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("stop_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_ForceNextJob:
                    entity.AddParameter("job_started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("job_completed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("job_interrupted", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ShouldInterruptCurrentTask", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Job", new cFloat(), ParameterVariant.PARAMETER); //Object
                    entity.AddParameter("InitialTask", new cFloat(), ParameterVariant.PARAMETER); //Object
                    break;
                case FunctionType.NPC_SetRateOfFire:
                    entity.AddParameter("MinTimeBetweenShots", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("RandomRange", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_SetFiringRhythm:
                    entity.AddParameter("MinShootingTime", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("RandomRangeShootingTime", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MinNonShootingTime", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("RandomRangeNonShootingTime", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MinCoverNonShootingTime", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("RandomRangeCoverNonShootingTime", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_SetFiringAccuracy:
                    entity.AddParameter("Accuracy", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerBindAllNPCs:
                    entity.AddParameter("npc_inside", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("npc_outside", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    entity.AddParameter("centre", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("radius", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.Trigger_AudioOccluded:
                    entity.AddParameter("NotOccluded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Occluded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("Range", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SwitchLevel:
                    entity.AddParameter("level_name", new cString(""), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.SoundPlaybackBaseClass:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("attached_sound_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_OBJECT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //SOUND_OBJECT
                    entity.AddParameter("sound_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("argument_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    entity.AddParameter("argument_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    entity.AddParameter("argument_3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    entity.AddParameter("argument_4", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    entity.AddParameter("argument_5", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    entity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("object_position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("restore_on_checkpoint", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Sound:
                    entity.AddParameter("stop_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("is_static_ambience", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("start_on", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("multi_trigger", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_multi_emitter", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("create_sound_object", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("switch_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_SWITCH) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_SWITCH
                    entity.AddParameter("switch_value", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("last_gen_enabled", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("resume_after_suspended", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Speech:
                    entity.AddParameter("on_speech_started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("alt_character", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 2), ParameterVariant.PARAMETER); //SPEECH_PRIORITY
                    entity.AddParameter("queue_time", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_DynamicDialogueGlobalRange:
                    entity.AddParameter("dialogue_range", new cFloat(35.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_PlayNPCBark:
                    entity.AddParameter("on_speech_started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_speech_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("queue_time", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("sound_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 0), ParameterVariant.PARAMETER); //SPEECH_PRIORITY
                    entity.AddParameter("dialogue_mode", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    entity.AddParameter("action", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    break;
                case FunctionType.SpeechScript:
                    entity.AddParameter("on_script_ended", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("character_01", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("character_02", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("character_03", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("character_04", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("character_05", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("alt_character_01", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("alt_character_02", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("alt_character_03", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("alt_character_04", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("alt_character_05", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 2), ParameterVariant.PARAMETER); //SPEECH_PRIORITY
                    entity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("line_01_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("line_01_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("line_02_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("line_02_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("line_02_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("line_03_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("line_03_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("line_03_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("line_04_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("line_04_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("line_04_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("line_05_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("line_05_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("line_05_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("line_06_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("line_06_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("line_06_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("line_07_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("line_07_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("line_07_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("line_08_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("line_08_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("line_08_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("line_09_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("line_09_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("line_09_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("line_10_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("line_10_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("line_10_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("restore_on_checkpoint", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SoundNetworkNode:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.SoundEnvironmentMarker:
                    entity.AddParameter("reverb_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_REVERB) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_REVERB
                    entity.AddParameter("on_enter_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("on_exit_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("linked_network_occlusion_scaler", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("room_size", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_STATE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_STATE
                    entity.AddParameter("disable_network_creation", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.SoundEnvironmentZone:
                    entity.AddParameter("reverb_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_REVERB) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_REVERB
                    entity.AddParameter("priority", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.SoundLoadBank:
                    entity.AddParameter("bank_loaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("sound_bank", new cString(""), ParameterVariant.INPUT); //String
                    entity.AddParameter("trigger_via_pin", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("memory_pool", new cEnum(EnumType.SOUND_POOL, 0), ParameterVariant.PARAMETER); //SOUND_POOL
                    break;
                case FunctionType.SoundLoadSlot:
                    entity.AddParameter("bank_loaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("sound_bank", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("memory_pool", new cEnum(EnumType.SOUND_POOL, 0), ParameterVariant.PARAMETER); //SOUND_POOL
                    break;
                case FunctionType.SoundSetRTPC:
                    entity.AddParameter("rtpc_value", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("sound_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_OBJECT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //SOUND_OBJECT
                    entity.AddParameter("rtpc_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_RTPC) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_RTPC
                    entity.AddParameter("smooth_rate", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("start_on", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SoundSetState:
                    entity.AddParameter("state_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_STATE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_STATE
                    entity.AddParameter("state_value", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.SoundSetSwitch:
                    entity.AddParameter("sound_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_OBJECT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //SOUND_OBJECT
                    entity.AddParameter("switch_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_SWITCH) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_SWITCH
                    entity.AddParameter("switch_value", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.SoundImpact:
                    entity.AddParameter("sound_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("argument_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    entity.AddParameter("argument_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    entity.AddParameter("argument_3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    break;
                case FunctionType.SoundBarrier:
                    entity.AddParameter("default_open", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("band_aid", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("override_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.COLLISION_MAPPING);
                    break;
                case FunctionType.MusicController:
                    entity.AddParameter("music_start_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("music_end_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("music_restart_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("layer_control_rtpc", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_PARAMETER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_PARAMETER
                    entity.AddParameter("smooth_rate", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("alien_max_distance", new cFloat(50.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("object_max_distance", new cFloat(50.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.MusicTrigger:
                    entity.AddParameter("on_triggered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("connected_object", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("music_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("smooth_rate", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("queue_time", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("interrupt_all", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("trigger_once", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("rtpc_set_mode", new cEnum(EnumType.MUSIC_RTPC_MODE, 0), ParameterVariant.PARAMETER); //MUSIC_RTPC_MODE
                    entity.AddParameter("rtpc_target_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("rtpc_duration", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("rtpc_set_return_mode", new cEnum(EnumType.MUSIC_RTPC_MODE, 0), ParameterVariant.PARAMETER); //MUSIC_RTPC_MODE
                    entity.AddParameter("rtpc_return_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SoundLevelInitialiser:
                    entity.AddParameter("auto_generate_networks", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("network_node_min_spacing", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("network_node_max_visibility", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("network_node_ceiling_height", new cFloat(), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SoundMissionInitialiser:
                    entity.AddParameter("human_max_threat", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("android_max_threat", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("alien_max_threat", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SoundRTPCController:
                    entity.AddParameter("stealth_default_on", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("threat_default_on", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SoundTimelineTrigger:
                    entity.AddParameter("sound_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    entity.AddParameter("trigger_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SoundPhysicsInitialiser:
                    entity.AddParameter("contact_max_timeout", new cFloat(0.33f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("contact_smoothing_attack_rate", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("contact_smoothing_decay_rate", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("contact_min_magnitude", new cFloat(0.01f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("contact_max_trigger_distance", new cFloat(25.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("impact_min_speed", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("impact_max_trigger_distance", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ragdoll_min_timeout", new cFloat(0.25f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ragdoll_min_speed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SoundPlayerFootwearOverride:
                    entity.AddParameter("footwear_sound", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_FOOTWEAR_GROUP) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SOUND_FOOTWEAR_GROUP
                    break;
                case FunctionType.AddToInventory:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.RemoveFromInventory:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.LimitItemUse:
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PlayerHasItem:
                    entity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PlayerHasItemWithName:
                    entity.AddParameter("item_name", new cString(" "), ParameterVariant.INPUT); //String
                    break;
                case FunctionType.PlayerHasItemEntity:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PlayerHasEnoughItems:
                    entity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("quantity", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.PlayerHasSpaceForItem:
                    entity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.InventoryItem:
                    entity.AddParameter("collect", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("itemName", new cString(""), ParameterVariant.INPUT); //String
                    entity.AddParameter("out_itemName", new cString(""), ParameterVariant.OUTPUT); //String
                    entity.AddParameter("out_quantity", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("item", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("quantity", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("clear_on_collect", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("gcip_instances_count", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.GetInventoryItemName:
                    entity.AddParameter("item", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    entity.AddParameter("equippable_item", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.EQUIPPABLE_ITEM_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //EQUIPPABLE_ITEM_INSTANCE
                    break;
                case FunctionType.PickupSpawner:
                    entity.AddParameter("collect", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("spawn_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("pos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("item_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.MultiplePickupSpawner:
                    entity.AddParameter("pos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("item_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.AddItemsToGCPool:
                    entity.AddParameter("items", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    break;
                case FunctionType.SetupGCDistribution:
                    entity.AddParameter("c00", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("c01", new cFloat(0.969f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("c02", new cFloat(0.882f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("c03", new cFloat(0.754f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("c04", new cFloat(0.606f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("c05", new cFloat(0.457f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("c06", new cFloat(0.324f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("c07", new cFloat(0.216f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("c08", new cFloat(0.135f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("c09", new cFloat(0.079f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("c10", new cFloat(0.043f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("minimum_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("divisor", new cFloat(20.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("lookup_decrease_time", new cFloat(15.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("lookup_point_increase", new cInteger(2), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.AllocateGCItemsFromPool:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("items", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    entity.AddParameter("force_usage_count", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.AllocateGCItemFromPoolBySubset:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("selectable_items", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("item_name", new cString(""), ParameterVariant.OUTPUT); //String
                    entity.AddParameter("item_quantity", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("force_usage", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.QueryGCItemPool:
                    entity.AddParameter("count", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("item_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.RemoveFromGCItemPool:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("item_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("gcip_instances_to_remove", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.FlashScript:
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("filename", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("layer_name", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("target_texture_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("type", new cEnum(EnumType.FLASH_SCRIPT_RENDER_TYPE, 0), ParameterVariant.PARAMETER); //FLASH_SCRIPT_RENDER_TYPE
                    break;
                case FunctionType.UI_KeyGate:
                    entity.AddParameter("keycard_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("keycode_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("keycard_fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("keycode_fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("keycard_cancelled", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("keycode_cancelled", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("lock_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("code", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("carduid", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("key_type", new cEnum(EnumType.UI_KEYGATE_TYPE, 1), ParameterVariant.PARAMETER); //UI_KEYGATE_TYPE
                    break;
                case FunctionType.RTT_MoviePlayer:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("end", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("filename", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("layer_name", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("target_texture_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.MoviePlayer:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("end", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("skipped", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("trigger_end_on_skipped", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("filename", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("skippable", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("enable_debug_skip", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DurangoVideoCapture:
                    entity.AddParameter("clip_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.VideoCapture:
                    entity.AddParameter("clip_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("only_in_capture_mode", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FlashInvoke:
                    entity.AddParameter("layer_name", new cString(" "), ParameterVariant.INPUT); //String
                    entity.AddParameter("mrtt_texture", new cString(" "), ParameterVariant.INPUT); //String
                    entity.AddParameter("method", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("invoke_type", new cEnum(EnumType.FLASH_INVOKE_TYPE, 0), ParameterVariant.PARAMETER); //FLASH_INVOKE_TYPE
                    entity.AddParameter("int_argument_0", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("int_argument_1", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("int_argument_2", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("int_argument_3", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("float_argument_0", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("float_argument_1", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("float_argument_2", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("float_argument_3", new cFloat(), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.MotionTrackerPing:
                    entity.AddParameter("FakePosition", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.FlashCallback:
                    entity.AddParameter("callback", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("callback_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.PopupMessage:
                    entity.AddParameter("display", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("header_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("main_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("duration", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("sound_event", new cEnum(EnumType.POPUP_MESSAGE_SOUND, 1), ParameterVariant.PARAMETER); //POPUP_MESSAGE_SOUND
                    entity.AddParameter("icon_keyframe", new cEnum(EnumType.POPUP_MESSAGE_ICON, 0), ParameterVariant.PARAMETER); //POPUP_MESSAGE_ICON
                    break;
                case FunctionType.UIBreathingGameIcon:
                    entity.AddParameter("fill_percentage", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("prompt_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.GenericHighlightEntity:
                    entity.AddParameter("highlight_geometry", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //RENDERABLE_INSTANCE
                    break;
                case FunctionType.UI_Icon:
                    entity.AddParameter("start", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("button_released", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("broadcasted_start", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("highlight", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("unhighlight", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("lock_looked_at", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("lock_interaction", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("lock_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("show_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("highlight_geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("target_pickup_item", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("highlight_distance_threshold", new cFloat(3.15f), ParameterVariant.INPUT); //float
                    entity.AddParameter("interaction_distance_threshold", new cFloat(), ParameterVariant.INPUT); //float
                    entity.AddParameter("icon_user", new cFloat(), ParameterVariant.OUTPUT); //Object
                    entity.AddParameter("unlocked_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("locked_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("action_text", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("icon_keyframe", new cEnum(EnumType.UI_ICON_ICON, 0), ParameterVariant.PARAMETER); //UI_ICON_ICON
                    entity.AddParameter("can_be_used", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("category", new cEnum(EnumType.PICKUP_CATEGORY, 0), ParameterVariant.PARAMETER); //PICKUP_CATEGORY
                    entity.AddParameter("push_hold_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.UI_Attached:
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ui_icon", new cInteger(0), ParameterVariant.INPUT); //int
                    break;
                case FunctionType.UI_Container:
                    entity.AddParameter("take_slot", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("emptied", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("contents", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    entity.AddParameter("has_been_used", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_temporary", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UI_ReactionGame:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stage0_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stage0_fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stage1_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stage1_fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stage2_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stage2_fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("resources_finished_unloading", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("resources_finished_loading", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("completion_percentage", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("exit_on_fail", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UI_Keypad:
                    entity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("code", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("exit_on_fail", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.HackingGame:
                    entity.AddParameter("win", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("alarm_triggered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("loaded_idle", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("loaded_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("resources_finished_unloading", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("resources_finished_loading", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("lock_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("completion_percentage", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("hacking_difficulty", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("auto_exit", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SetHackingToolLevel:
                    entity.AddParameter("level", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.TerminalContent:
                    entity.AddParameter("selected", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("content_title", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("content_decoration_title", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("additional_info", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("is_connected_to_audio_log", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_triggerable", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_single_use", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.TerminalFolder:
                    entity.AddParameter("code_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("code_fail", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("selected", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("lock_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("content0", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_CONTENT_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //TERMINAL_CONTENT_DETAILS
                    entity.AddParameter("content1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_CONTENT_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //TERMINAL_CONTENT_DETAILS
                    entity.AddParameter("code", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("folder_title", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("folder_lock_type", new cEnum(EnumType.FOLDER_LOCK_TYPE, 1), ParameterVariant.PARAMETER); //FOLDER_LOCK_TYPE
                    break;
                case FunctionType.AccessTerminal:
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("all_data_has_been_read", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("folder0", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //TERMINAL_FOLDER_DETAILS
                    entity.AddParameter("folder1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //TERMINAL_FOLDER_DETAILS
                    entity.AddParameter("folder2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //TERMINAL_FOLDER_DETAILS
                    entity.AddParameter("folder3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //TERMINAL_FOLDER_DETAILS
                    entity.AddParameter("all_data_read", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("location", new cEnum(EnumType.TERMINAL_LOCATION, 0), ParameterVariant.PARAMETER); //TERMINAL_LOCATION
                    break;
                case FunctionType.SetGatingToolLevel:
                    entity.AddParameter("level", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER); //GATING_TOOL_TYPE
                    break;
                case FunctionType.GetGatingToolLevel:
                    entity.AddParameter("level", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER); //GATING_TOOL_TYPE
                    break;
                case FunctionType.GetPlayerHasGatingTool:
                    entity.AddParameter("has_tool", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("doesnt_have_tool", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER); //GATING_TOOL_TYPE
                    break;
                case FunctionType.GetPlayerHasKeycard:
                    entity.AddParameter("has_card", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("doesnt_have_card", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("card_uid", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.SetPlayerHasKeycard:
                    entity.AddParameter("card_uid", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.SetPlayerHasGatingTool:
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER); //GATING_TOOL_TYPE
                    break;
                case FunctionType.CollectSevastopolLog:
                    entity.AddParameter("log_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SEVASTOPOL_LOG_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //SEVASTOPOL_LOG_ID
                    break;
                case FunctionType.CollectNostromoLog:
                    entity.AddParameter("log_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NOSTROMO_LOG_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //NOSTROMO_LOG_ID
                    break;
                case FunctionType.CollectIDTag:
                    entity.AddParameter("tag_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.IDTAG_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //IDTAG_ID
                    break;
                case FunctionType.StartNewChapter:
                    entity.AddParameter("chapter", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.UnlockLogEntry:
                    entity.AddParameter("entry", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.MapAnchor:
                    entity.AddParameter("map_north", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("map_pos", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("map_scale", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("keyframe", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("keyframe1", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("keyframe2", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("keyframe3", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("keyframe4", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("keyframe5", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("is_default_for_items", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.MapItem:
                    entity.AddParameter("show_ui_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("item_type", new cEnum(EnumType.MAP_ICON_TYPE, 0), ParameterVariant.PARAMETER); //MAP_ICON_TYPE
                    entity.AddParameter("map_keyframe", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.MAP_KEYFRAME_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //MAP_KEYFRAME_ID
                    break;
                case FunctionType.UnlockMapDetail:
                    entity.AddParameter("map_keyframe", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("details", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.RewireSystem:
                    entity.AddParameter("on", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("off", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("display_name", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("display_name_enum", new cEnum(EnumType.REWIRE_SYSTEM_NAME, 0), ParameterVariant.PARAMETER); //REWIRE_SYSTEM_NAME
                    entity.AddParameter("on_by_default", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("running_cost", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("system_type", new cEnum(EnumType.REWIRE_SYSTEM_TYPE, 0), ParameterVariant.PARAMETER); //REWIRE_SYSTEM_TYPE
                    entity.AddParameter("map_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("element_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.RewireLocation:
                    entity.AddParameter("power_draw_increased", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("power_draw_reduced", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("systems", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_SYSTEM) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //REWIRE_SYSTEM
                    entity.AddParameter("element_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("display_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.RewireAccess_Point:
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("interactive_locations", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_LOCATION) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //REWIRE_LOCATION
                    entity.AddParameter("visible_locations", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_LOCATION) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //REWIRE_LOCATION
                    entity.AddParameter("additional_power", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("display_name", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("map_element_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("map_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("map_x_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("map_y_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("map_zoom", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.RewireTotalPowerResource:
                    entity.AddParameter("total_power", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.Rewire:
                    entity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("locations", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_LOCATION) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //REWIRE_LOCATION
                    entity.AddParameter("access_points", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_ACCESS_POINT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //REWIRE_ACCESS_POINT
                    entity.AddParameter("map_keyframe", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("total_power", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.SetMotionTrackerRange:
                    entity.AddParameter("range", new cFloat(20.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SetGamepadAxes:
                    entity.AddParameter("invert_x", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("invert_y", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("save_settings", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SetGameplayTips:
                    entity.AddParameter("tip_string_id", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.GameOver:
                    entity.AddParameter("tip_string_id", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("default_tips_enabled", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("level_tips_enabled", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GameplayTip:
                    entity.AddParameter("string_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.GAMEPLAY_TIP_STRING_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //GAMEPLAY_TIP_STRING_ID
                    break;
                case FunctionType.Minigames:
                    entity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("game_inertial_damping_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("game_green_text_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("game_yellow_chart_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("game_overloc_fail_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("game_docking_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("game_environ_ctr_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("config_pass_number", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("config_fail_limit", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("config_difficulty", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.SetBlueprintInfo:
                    entity.AddParameter("type", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.BLUEPRINT_TYPE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //BLUEPRINT_TYPE
                    entity.AddParameter("level", new cEnum(EnumType.BLUEPRINT_LEVEL, 1), ParameterVariant.PARAMETER); //BLUEPRINT_LEVEL
                    entity.AddParameter("available", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GetBlueprintLevel:
                    entity.AddParameter("level", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("type", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.BLUEPRINT_TYPE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //BLUEPRINT_TYPE
                    break;
                case FunctionType.GetBlueprintAvailable:
                    entity.AddParameter("available", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("type", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.BLUEPRINT_TYPE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //BLUEPRINT_TYPE
                    break;
                case FunctionType.GetSelectedCharacterId:
                    entity.AddParameter("character_id", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.GetNextPlaylistLevelName:
                    entity.AddParameter("level_name", new cString(""), ParameterVariant.OUTPUT); //String
                    break;
                case FunctionType.IsPlaylistTypeSingle:
                    entity.AddParameter("single", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.IsPlaylistTypeAll:
                    entity.AddParameter("all", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.IsPlaylistTypeMarathon:
                    entity.AddParameter("marathon", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.IsCurrentLevelAChallengeMap:
                    entity.AddParameter("challenge_map", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.IsCurrentLevelAPreorderMap:
                    entity.AddParameter("preorder_map", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.GetCurrentPlaylistLevelIndex:
                    entity.AddParameter("index", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.SetObjectiveCompleted:
                    entity.AddParameter("objective_id", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.GoToFrontend:
                    entity.AddParameter("frontend_state", new cEnum(EnumType.FRONTEND_STATE, 0), ParameterVariant.PARAMETER); //FRONTEND_STATE
                    break;
                case FunctionType.TriggerLooper:
                    entity.AddParameter("target", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("delay", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CoverLine:
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("low", new cBool(), ParameterVariant.INPUT); //bool
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.CATHODE_COVER_SEGMENT);
                    entity.AddParameter("LinePathPosition", new cTransform(), ParameterVariant.INTERNAL); //Position
                    break;
                case FunctionType.TRAV_ContinuousLadder:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("RungSpacing", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousPipe:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousLedge:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("Dangling", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.AUTODETECT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //AUTODETECT
                    entity.AddParameter("Sidling", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.AUTODETECT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //AUTODETECT
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousClimbingWall:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("Dangling", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.AUTODETECT) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //AUTODETECT
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousCinematicSidle:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousBalanceBeam:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousTightGap:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_1ShotVentEntrance:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.TRAVERSAL_SEGMENT);
                    break;
                case FunctionType.TRAV_1ShotVentExit:
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.TRAVERSAL_SEGMENT);
                    break;
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.TRAVERSAL_SEGMENT);
                    break;
                case FunctionType.TRAV_1ShotFloorVentExit:
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.TRAVERSAL_SEGMENT);
                    break;
                case FunctionType.TRAV_1ShotClimbUnder:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_1ShotLeap:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnSuccess", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnFailure", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("StartEdgeLinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("EndEdgeLinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    entity.AddParameter("MissDistance", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("NearMissDistance", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_1ShotSpline:
                    entity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("open_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("EntrancePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("ExitPath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("MinimumPath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("MaximumPath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("MinimumSupport", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("MaximumSupport", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("template", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("headroom", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("extra_cost", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("fit_end_to_edge", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("min_speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 0), ParameterVariant.PARAMETER); //LOCOMOTION_TARGET_SPEED
                    entity.AddParameter("max_speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 0), ParameterVariant.PARAMETER); //LOCOMOTION_TARGET_SPEED
                    entity.AddParameter("animationTree", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.TRAVERSAL_SEGMENT);
                    break;
                case FunctionType.NavMeshBarrier:
                    entity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("opaque", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("allowed_character_classes_when_open", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    entity.AddParameter("allowed_character_classes_when_closed", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.NAV_MESH_BARRIER_RESOURCE);
                    break;
                case FunctionType.NavMeshWalkablePlatform:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.NavMeshExclusionArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.NavMeshArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("area_type", new cEnum(EnumType.NAV_MESH_AREA_TYPE, 0), ParameterVariant.PARAMETER); //NAV_MESH_AREA_TYPE
                    break;
                case FunctionType.NavMeshReachabilitySeedPoint:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.CoverExclusionArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("exclude_cover", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("exclude_vaults", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("exclude_mantles", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("exclude_jump_downs", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("exclude_crawl_space_spotting_positions", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("exclude_spotting_positions", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("exclude_assault_positions", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SpottingExclusionArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.PathfindingTeleportNode:
                    entity.AddParameter("started_teleporting", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stopped_teleporting", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("extra_cost", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.PathfindingWaitNode:
                    entity.AddParameter("character_getting_near", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("character_arriving", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("character_stopped", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("started_waiting", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stopped_waiting", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("extra_cost", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.PathfindingManualNode:
                    entity.AddParameter("character_arriving", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("character_stopped", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("started_animating", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stopped_animating", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("PlayAnimData", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("extra_cost", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.PathfindingAlienBackstageNode:
                    entity.AddParameter("started_animating_Entry", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stopped_animating_Entry", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("started_animating_Exit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("stopped_animating_Exit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("killtrap_anim_started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("killtrap_anim_stopped", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("killtrap_fx_start", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("killtrap_fx_stop", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("PlayAnimData_Entry", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("PlayAnimData_Exit", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("Killtrap_alien", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("Killtrap_victim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("top", new cTransform(), ParameterVariant.PARAMETER); //Position
                    entity.AddParameter("extra_cost", new cFloat(100.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("network_id", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.ChokePoint:
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.CHOKE_POINT_RESOURCE);
                    break;
                case FunctionType.NPC_SetChokePoint:
                    entity.AddParameter("chokepoints", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHOKE_POINT_RESOURCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CHOKE_POINT_RESOURCE
                    break;
                case FunctionType.Planet:
                    entity.AddParameter("planet_resource", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("parallax_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("sun_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("light_shaft_source_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("parallax_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("planet_sort_key", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("overbright_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("light_wrap_angle_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("penumbra_falloff_power_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("lens_flare_brightness", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("lens_flare_colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("atmosphere_edge_falloff_power", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("atmosphere_edge_transparency", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("atmosphere_scroll_speed", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("atmosphere_detail_scroll_speed", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("override_global_tint", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("global_tint", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("flow_cycle_time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("flow_speed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("flow_tex_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("flow_warp_strength", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("detail_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("normal_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("terrain_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("atmosphere_normal_strength", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("terrain_normal_strength", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("light_shaft_colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("light_shaft_range", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("light_shaft_decay", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("light_shaft_min_occlusion_distance", new cFloat(100.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("light_shaft_intensity", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("light_shaft_density", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("light_shaft_source_occlusion", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("blocks_light_shafts", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SpaceTransform:
                    entity.AddParameter("affected_geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("yaw_speed", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("pitch_speed", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("roll_speed", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SpaceSuitVisor:
                    entity.AddParameter("breath_level", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NonInteractiveWater:
                    entity.AddParameter("water_resource", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("SCALE_X", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SCALE_Z", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SHININESS", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CYCLE_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FRESNEL_POWER", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MIN_FRESNEL", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MAX_FRESNEL", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ENVIRONMENT_MAP_MULT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ENVMAP_SIZE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ENVMAP_BOXPROJ_BB_SCALE", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("REFLECTION_PERTURBATION_STRENGTH", new cFloat(0.05f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHA_PERTURBATION_STRENGTH", new cFloat(0.05f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ALPHALIGHT_MULT", new cFloat(0.4f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("softness_edge", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FOG_INITIAL_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DEPTH_FOG_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_ALPHA", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FOG_END_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("DEPTH_FOG_END_ALPHA", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DEPTH_FOG_END_DEPTH", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.Refraction:
                    entity.AddParameter("refraction_resource", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("SCALE_X", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SCALE_Z", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DISTANCEFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("MIN_OCCLUSION_DISTANCE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("CYCLE_TIME", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FogPlane:
                    entity.AddParameter("fog_plane_resource", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("start_distance_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("distance_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("angle_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("linear_height_density_fresnel_power_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("linear_heigth_density_max_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("tint", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("thickness_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("edge_softness_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("diffuse_0_uv_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("diffuse_0_speed_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("diffuse_1_uv_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("diffuse_1_speed_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.PostprocessingSettings:
                    entity.AddParameter("intensity", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("priority", new cInteger(100), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("blend_mode", new cEnum(EnumType.BLEND_MODE, 2), ParameterVariant.PARAMETER); //BLEND_MODE
                    break;
                case FunctionType.BloomSettings:
                    entity.AddParameter("frame_buffer_scale", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("frame_buffer_offset", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("bloom_scale", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("bloom_gather_exponent", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("bloom_gather_scale", new cFloat(0.04f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.ColourSettings:
                    entity.AddParameter("brightness", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("contrast", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("saturation", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("red_tint", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("green_tint", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("blue_tint", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.FlareSettings:
                    entity.AddParameter("flareOffset0", new cFloat(-1.2f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareIntensity0", new cFloat(0.05f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareAttenuation0", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareOffset1", new cFloat(-1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareIntensity1", new cFloat(0.15f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareAttenuation1", new cFloat(0.7f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareOffset2", new cFloat(-0.8f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareIntensity2", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareAttenuation2", new cFloat(7.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareOffset3", new cFloat(-0.6f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareIntensity3", new cFloat(0.4f), ParameterVariant.INPUT); //float
                    entity.AddParameter("flareAttenuation3", new cFloat(1.5f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.HighSpecMotionBlurSettings:
                    entity.AddParameter("contribution", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("camera_velocity_scalar", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("camera_velocity_min", new cFloat(1.5f), ParameterVariant.INPUT); //float
                    entity.AddParameter("camera_velocity_max", new cFloat(3.5f), ParameterVariant.INPUT); //float
                    entity.AddParameter("object_velocity_scalar", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("object_velocity_min", new cFloat(1.5f), ParameterVariant.INPUT); //float
                    entity.AddParameter("object_velocity_max", new cFloat(3.5f), ParameterVariant.INPUT); //float
                    entity.AddParameter("blur_range", new cFloat(16.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.FilmGrainSettings:
                    entity.AddParameter("low_lum_amplifier", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    entity.AddParameter("mid_lum_amplifier", new cFloat(0.25f), ParameterVariant.INPUT); //float
                    entity.AddParameter("high_lum_amplifier", new cFloat(0.4f), ParameterVariant.INPUT); //float
                    entity.AddParameter("low_lum_range", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    entity.AddParameter("mid_lum_range", new cFloat(0.3f), ParameterVariant.INPUT); //float
                    entity.AddParameter("high_lum_range", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    entity.AddParameter("noise_texture_scale", new cFloat(4.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("adaptive", new cBool(false), ParameterVariant.INPUT); //bool
                    entity.AddParameter("adaptation_scalar", new cFloat(3.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("adaptation_time_scalar", new cFloat(0.25f), ParameterVariant.INPUT); //float
                    entity.AddParameter("unadapted_low_lum_amplifier", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    entity.AddParameter("unadapted_mid_lum_amplifier", new cFloat(0.25f), ParameterVariant.INPUT); //float
                    entity.AddParameter("unadapted_high_lum_amplifier", new cFloat(0.4f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.VignetteSettings:
                    entity.AddParameter("vignette_factor", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("vignette_chromatic_aberration_scale", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.DistortionSettings:
                    entity.AddParameter("radial_distort_factor", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("radial_distort_constraint", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("radial_distort_scalar", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.SharpnessSettings:
                    entity.AddParameter("local_contrast_factor", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.LensDustSettings:
                    entity.AddParameter("DUST_MAX_REFLECTED_BLOOM_INTENSITY", new cFloat(0.02f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DUST_REFLECTED_BLOOM_INTENSITY_SCALAR", new cFloat(0.25f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DUST_MAX_BLOOM_INTENSITY", new cFloat(0.004f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DUST_BLOOM_INTENSITY_SCALAR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("DUST_THRESHOLD", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.IrawanToneMappingSettings:
                    entity.AddParameter("target_device_luminance", new cFloat(6.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("target_device_adaptation", new cFloat(20.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("saccadic_time", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("superbright_adaptation", new cFloat(0.5f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.HableToneMappingSettings:
                    entity.AddParameter("shoulder_strength", new cFloat(0.22f), ParameterVariant.INPUT); //float
                    entity.AddParameter("linear_strength", new cFloat(0.3f), ParameterVariant.INPUT); //float
                    entity.AddParameter("linear_angle", new cFloat(0.1f), ParameterVariant.INPUT); //float
                    entity.AddParameter("toe_strength", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    entity.AddParameter("toe_numerator", new cFloat(0.01f), ParameterVariant.INPUT); //float
                    entity.AddParameter("toe_denominator", new cFloat(0.3f), ParameterVariant.INPUT); //float
                    entity.AddParameter("linear_white_point", new cFloat(11.2f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.DayToneMappingSettings:
                    entity.AddParameter("black_point", new cFloat(0.00625f), ParameterVariant.INPUT); //float
                    entity.AddParameter("cross_over_point", new cFloat(0.4f), ParameterVariant.INPUT); //float
                    entity.AddParameter("white_point", new cFloat(40.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("shoulder_strength", new cFloat(0.95f), ParameterVariant.INPUT); //float
                    entity.AddParameter("toe_strength", new cFloat(0.15f), ParameterVariant.INPUT); //float
                    entity.AddParameter("luminance_scale", new cFloat(5.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.LightAdaptationSettings:
                    entity.AddParameter("fast_neural_t0", new cFloat(5.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("slow_neural_t0", new cFloat(5.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("pigment_bleaching_t0", new cFloat(20.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("fb_luminance_to_candelas_per_m2", new cFloat(105.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("max_adaptation_lum", new cFloat(20000.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("min_adaptation_lum", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("adaptation_percentile", new cFloat(0.3f), ParameterVariant.INPUT); //float
                    entity.AddParameter("low_bracket", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("high_bracket", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("adaptation_mechanism", new cEnum(EnumType.LIGHT_ADAPTATION_MECHANISM, 0), ParameterVariant.PARAMETER); //LIGHT_ADAPTATION_MECHANISM
                    break;
                case FunctionType.ColourCorrectionTransition:
                    entity.AddParameter("interpolate", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("colour_lut_a", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("colour_lut_b", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("lut_a_contribution", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("lut_b_contribution", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("colour_lut_a_index", new cInteger(-1), ParameterVariant.INTERNAL); //int
                    entity.AddParameter("colour_lut_b_index", new cInteger(-1), ParameterVariant.INTERNAL); //int
                    break;
                case FunctionType.ProjectileMotion:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_pos", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("start_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Current_Position", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("Current_Velocity", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.ProjectileMotionComplex:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("start_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("start_angular_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("flight_time_in_seconds", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("current_position", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("current_velocity", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    entity.AddParameter("current_angular_velocity", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    entity.AddParameter("current_flight_time_in_seconds", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.SplineDistanceLerp:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("lerp_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.MoveAlongSpline:
                    entity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("speed", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.GetSplineLength:
                    entity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.GetPointOnSpline:
                    entity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("percentage_of_spline", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.GetClosestPercentOnSpline:
                    entity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("pos_to_be_near", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("position_on_spline", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("bidirectional", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GetClosestPointOnSpline:
                    entity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("pos_to_be_near", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("position_on_spline", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("look_ahead_distance", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("unidirectional", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("directional_damping_threshold", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.GetClosestPoint:
                    entity.AddParameter("bound_to_closest", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Positions", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("pos_to_be_near", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("position_of_closest", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.GetClosestPointFromSet:
                    entity.AddParameter("closest_is_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("closest_is_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("closest_is_3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("closest_is_4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("closest_is_5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("closest_is_6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("closest_is_7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("closest_is_8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("closest_is_9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("closest_is_10", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Position_1", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Position_2", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Position_3", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Position_4", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Position_5", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Position_6", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Position_7", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Position_8", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Position_9", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Position_10", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("pos_to_be_near", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("position_of_closest", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("index_of_closest", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.GetCentrePoint:
                    entity.AddParameter("Positions", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("position_of_centre", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.FogSetting:
                    entity.AddParameter("linear_distance", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("max_distance", new cFloat(850.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("linear_density", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("exponential_density", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("near_colour", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("far_colour", new cVector3(), ParameterVariant.INPUT); //Direction
                    break;
                case FunctionType.FullScreenBlurSettings:
                    entity.AddParameter("contribution", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.DistortionOverlay:
                    entity.AddParameter("intensity", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("time", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("distortion_texture", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("alpha_threshold_enabled", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("threshold_texture", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("range", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("begin_start_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("begin_stop_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("end_start_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("end_stop_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FullScreenOverlay:
                    entity.AddParameter("overlay_texture", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("threshold_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("threshold_start", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("threshold_stop", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("threshold_range", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("alpha_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.DepthOfFieldSettings:
                    entity.AddParameter("focal_length_mm", new cFloat(75.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("focal_plane_m", new cFloat(2.5f), ParameterVariant.INPUT); //float
                    entity.AddParameter("fnum", new cFloat(2.8f), ParameterVariant.INPUT); //float
                    entity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("use_camera_target", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ChromaticAberrations:
                    entity.AddParameter("aberration_scalar", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeOutToBlack:
                    entity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeOutToBlackTimed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeOutToWhite:
                    entity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeOutToWhiteTimed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeIn:
                    entity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeInTimed:
                    entity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.BlendLowResFrame:
                    entity.AddParameter("blend_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                //case FunctionType.CharacterMonitor:
                //    entity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //ResourceID
                //    break;
                case FunctionType.AreaHitMonitor:
                    entity.AddParameter("on_flamer_hit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_shotgun_hit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_pistol_hit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("SpherePos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("SphereRadius", new cFloat(), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.ENT_Debug_Exit_Game:
                    entity.AddParameter("FailureText", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("FailureCode", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.StreamingMonitor:
                    entity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.Raycast:
                    entity.AddParameter("Obstructed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Unobstructed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("OutOfRange", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("source_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("target_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("max_distance", new cFloat(100.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("hit_object", new cFloat(), ParameterVariant.OUTPUT); //Object
                    entity.AddParameter("hit_distance", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("hit_position", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("priority", new cEnum(EnumType.RAYCAST_PRIORITY, 2), ParameterVariant.PARAMETER); //RAYCAST_PRIORITY
                    break;
                case FunctionType.PhysicsApplyImpulse:
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("offset", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("direction", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("force", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("can_damage", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.PhysicsApplyVelocity:
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("angular_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("linear_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("propulsion_velocity", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.PhysicsModifyGravity:
                    entity.AddParameter("float_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PhysicsApplyBuoyancy:
                    entity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("water_height", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("water_density", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("water_viscosity", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("water_choppiness", new cFloat(0.05f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.AssetSpawner:
                    entity.AddParameter("finished_spawning", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("callback_triggered", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("forced_despawn", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("spawn_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    entity.AddParameter("asset", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("spawn_on_load", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("allow_forced_despawn", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("persist_on_callback", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("allow_physics", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ProximityTrigger:
                    entity.AddParameter("ignited", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("electrified", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("drenched", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("poisoned", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("fire_spread_rate", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("water_permeate_rate", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("electrical_conduction_rate", new cFloat(100.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("gas_diffusion_rate", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("ignition_range", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("electrical_arc_range", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("water_flow_range", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("gas_dispersion_range", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CharacterAttachmentNode:
                    entity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("character", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    entity.AddParameter("attachment", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    entity.AddParameter("Node", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER); //CHARACTER_NODE
                    entity.AddParameter("AdditiveNode", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER); //CHARACTER_NODE
                    entity.AddParameter("AdditiveNodeIntensity", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("UseOffset", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Translation", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("Rotation", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.MultipleCharacterAttachmentNode:
                    entity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("character_01", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    entity.AddParameter("attachment_01", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    entity.AddParameter("character_02", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    entity.AddParameter("attachment_02", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    entity.AddParameter("character_03", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    entity.AddParameter("attachment_03", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    entity.AddParameter("character_04", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    entity.AddParameter("attachment_04", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    entity.AddParameter("character_05", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    entity.AddParameter("attachment_05", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    entity.AddParameter("node", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER); //CHARACTER_NODE
                    entity.AddParameter("use_offset", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("translation", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("rotation", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.AnimatedModelAttachmentNode:
                    entity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    entity.AddParameter("animated_model", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("attachment", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    entity.AddParameter("bone_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("use_offset", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("offset", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.GetCharacterRotationSpeed:
                    entity.AddParameter("character", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    entity.AddParameter("speed", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.LevelCompletionTargets:
                    entity.AddParameter("TargetTime", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("NumDeaths", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("TeamRespawnBonus", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("NoLocalRespawnBonus", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("NoRespawnBonus", new cInteger(), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("GrappleBreakBonus", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.EnvironmentMap:
                    entity.AddParameter("Entities", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Priority", new cInteger(100), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("ColourFactor", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("EmissiveFactor", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("Texture", new cString(""), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("Texture_Index", new cInteger(-1), ParameterVariant.INTERNAL); //int
                    entity.AddParameter("environmentmap_index", new cInteger(-1), ParameterVariant.INTERNAL); //int
                    break;
                case FunctionType.Display_Element_On_Map:
                    entity.AddParameter("map_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("element_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.Map_Floor_Change:
                    entity.AddParameter("floor_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.Force_UI_Visibility:
                    entity.AddParameter("also_disable_interactions", new cBool(true), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.AddExitObjective:
                    entity.AddParameter("marker", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("level_name", new cEnum(EnumType.EXIT_WAYPOINT, 0), ParameterVariant.PARAMETER); //EXIT_WAYPOINT
                    break;
                case FunctionType.SetPrimaryObjective:
                    entity.AddParameter("title", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("additional_info", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("title_list", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.OBJECTIVE_ENTRY_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //OBJECTIVE_ENTRY_ID
                    entity.AddParameter("additional_info_list", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.OBJECTIVE_ENTRY_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //OBJECTIVE_ENTRY_ID
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SetSubObjective:
                    entity.AddParameter("target_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("title", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("map_description", new cString(" "), ParameterVariant.PARAMETER); //String
                    entity.AddParameter("title_list", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.OBJECTIVE_ENTRY_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //OBJECTIVE_ENTRY_ID
                    entity.AddParameter("map_description_list", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.OBJECTIVE_ENTRY_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //OBJECTIVE_ENTRY_ID
                    entity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("objective_type", new cEnum(EnumType.SUB_OBJECTIVE_TYPE, 0), ParameterVariant.PARAMETER); //SUB_OBJECTIVE_TYPE
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ClearPrimaryObjective:
                    entity.AddParameter("clear_all_sub_objectives", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ClearSubObjective:
                    entity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.UpdatePrimaryObjective:
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("clear_objective", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UpdateSubObjective:
                    entity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("clear_objective", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DebugGraph:
                    entity.AddParameter("Inputs", new cFloat(), ParameterVariant.INPUT); //float
                    entity.AddParameter("scale", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("duration", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("samples_per_second", new cFloat(), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("auto_scale", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("auto_scroll", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UnlockAchievement:
                    entity.AddParameter("achievement_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ACHIEVEMENT_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //ACHIEVEMENT_ID
                    break;
                case FunctionType.AchievementMonitor:
                    entity.AddParameter("achievement_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ACHIEVEMENT_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //ACHIEVEMENT_ID
                    break;
                case FunctionType.AchievementStat:
                    entity.AddParameter("achievement_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ACHIEVEMENT_STAT_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //ACHIEVEMENT_STAT_ID
                    break;
                case FunctionType.AchievementUniqueCounter:
                    entity.AddParameter("achievement_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ACHIEVEMENT_STAT_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //ACHIEVEMENT_STAT_ID
                    entity.AddParameter("unique_object", new cFloat(), ParameterVariant.PARAMETER); //Object
                    break;
                case FunctionType.SetRichPresence:
                    entity.AddParameter("presence_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PRESENCE_ID) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.PARAMETER); //PRESENCE_ID
                    entity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SmokeCylinder:
                    entity.AddParameter("pos", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("height", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.SmokeCylinderAttachmentInterface:
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("height", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.PointTracker:
                    entity.AddParameter("origin", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("target", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("target_offset", new cVector3(), ParameterVariant.INPUT); //Direction
                    entity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("origin_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    entity.AddParameter("max_speed", new cFloat(180.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("damping_factor", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ThrowingPointOfImpact:
                    entity.AddParameter("show_point_of_impact", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("hide_point_of_impact", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Location", new cTransform(), ParameterVariant.OUTPUT); //Position
                    entity.AddParameter("Visible", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.VisibilityMaster:
                    entity.AddParameter("renderable", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //RENDERABLE_INSTANCE
                    entity.AddParameter("mastered_by_visibility", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.MotionTrackerMonitor:
                    entity.AddParameter("on_motion_sound", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_enter_range_sound", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.GlobalEvent:
                    entity.AddParameter("EventValue", new cInteger(1), ParameterVariant.INPUT); //int
                    entity.AddParameter("EventName", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.GlobalEventMonitor:
                    entity.AddParameter("Event_1", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_2", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_3", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_4", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_5", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_6", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_7", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_8", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_9", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_10", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_11", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_12", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_13", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_14", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_15", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_16", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_17", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_18", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_19", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Event_20", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("EventName", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.GlobalPosition:
                    entity.AddParameter("PositionName", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.UpdateGlobalPosition:
                    entity.AddParameter("PositionName", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.PlayerLightProbe:
                    entity.AddParameter("output", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    entity.AddParameter("light_level_for_ai", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("dark_threshold", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("fully_lit_threshold", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.PlayerKilledAllyMonitor:
                    entity.AddParameter("ally_killed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.AILightCurveSettings:
                    entity.AddParameter("y0", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("x1", new cFloat(0.25f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("y1", new cFloat(0.3f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("x2", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("y2", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("x3", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.InteractiveMovementControl:
                    entity.AddParameter("completed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    entity.AddParameter("start_time", new cFloat(), ParameterVariant.INPUT); //float
                    entity.AddParameter("progress_path", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    entity.AddParameter("result", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("speed", new cFloat(), ParameterVariant.OUTPUT); //float
                    entity.AddParameter("can_go_both_ways", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("use_left_input_stick", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("base_progress_speed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("movement_threshold", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("momentum_damping", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("track_bone_position", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("character_node", new cEnum(EnumType.CHARACTER_NODE, 9), ParameterVariant.PARAMETER); //CHARACTER_NODE
                    entity.AddParameter("track_position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.PlayForMinDuration:
                    entity.AddParameter("timer_expired", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("first_animation_started", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("next_animation", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("all_animations_finished", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("MinDuration", new cFloat(5.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.GCIP_WorldPickup:
                    entity.AddParameter("spawn_completed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("pickup_collected", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("Pipe", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Gasoline", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Explosive", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Battery", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Blade", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Gel", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Adhesive", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("BoltGun Ammo", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Revolver Ammo", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Shotgun Ammo", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("BoltGun", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Revolver", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Shotgun", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Flare", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Flamer Fuel", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Flamer", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Scrap", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Torch Battery", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Torch", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Cattleprod Ammo", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("Cattleprod", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("StartOnReset", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("MissionNumber", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.Torch_Control:
                    entity.AddParameter("torch_switched_off", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("torch_switched_on", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("character", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), entity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    break;
                case FunctionType.DoorStatus:
                    entity.AddParameter("hacking_difficulty", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    entity.AddParameter("gate_type", new cEnum(EnumType.UI_KEYGATE_TYPE, 0), ParameterVariant.PARAMETER); //UI_KEYGATE_TYPE
                    entity.AddParameter("has_correct_keycard", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("cutting_tool_level", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("is_locked", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_powered", new cBool(false), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("is_cutting_complete", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DeleteHacking:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    break;
                case FunctionType.DeleteKeypad:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    break;
                case FunctionType.DeleteCuttingPanel:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    break;
                case FunctionType.DeleteBlankPanel:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    break;
                case FunctionType.DeleteHousing:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    entity.AddParameter("is_door", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DeletePullLever:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    entity.AddParameter("lever_type", new cEnum(EnumType.LEVER_TYPE, 0), ParameterVariant.PARAMETER); //LEVER_TYPE
                    break;
                case FunctionType.DeleteRotateLever:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    entity.AddParameter("lever_type", new cEnum(EnumType.LEVER_TYPE, 0), ParameterVariant.PARAMETER); //LEVER_TYPE
                    break;
                case FunctionType.DeleteButtonDisk:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    entity.AddParameter("button_type", new cEnum(EnumType.BUTTON_TYPE, 0), ParameterVariant.PARAMETER); //BUTTON_TYPE
                    break;
                case FunctionType.DeleteButtonKeys:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    entity.AddParameter("button_type", new cEnum(EnumType.BUTTON_TYPE, 0), ParameterVariant.PARAMETER); //BUTTON_TYPE
                    break;
                case FunctionType.Interaction:
                    entity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_interrupt", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("on_killed", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("interruptible_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PhysicsSystem:
                    entity.AddParameter("system_index", new cInteger(), ParameterVariant.INTERNAL); //int
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.DYNAMIC_PHYSICS_SYSTEM).index = 0;
                    break;
                case FunctionType.BulletChamber:
                    entity.AddParameter("Slot1", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Slot2", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Slot3", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Slot4", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Slot5", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Slot6", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Weapon", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("Geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PlayerDeathCounter:
                    entity.AddParameter("on_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("above_limit", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("filter", new cBool(), ParameterVariant.INPUT); //bool
                    entity.AddParameter("count", new cInteger(), ParameterVariant.OUTPUT); //int
                    entity.AddParameter("Limit", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.RadiosityIsland:
                    entity.AddParameter("composites", new cFloat(), ParameterVariant.INPUT); //Object
                    entity.AddParameter("exclusions", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.RadiosityProxy:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    if (entity.variant == EntityVariant.FUNCTION) ((FunctionEntity)entity).AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.LeaderboardWriter:
                    entity.AddParameter("time_elapsed", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("score", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("level_number", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("grade", new cInteger(5), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("player_character", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("combat", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("stealth", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("improv", new cInteger(0), ParameterVariant.PARAMETER); //int
                    entity.AddParameter("star1", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("star2", new cBool(), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("star3", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ProximityDetector:
                    entity.AddParameter("in_proximity", new cFloat(), ParameterVariant.TARGET); //
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    entity.AddParameter("detector_position", new cTransform(), ParameterVariant.INPUT); //Position
                    entity.AddParameter("min_distance", new cFloat(0.3f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("max_distance", new cFloat(100.0f), ParameterVariant.PARAMETER); //float
                    entity.AddParameter("requires_line_of_sight", new cBool(true), ParameterVariant.PARAMETER); //bool
                    entity.AddParameter("proximity_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FakeAILightSourceInPlayersHand:
                    entity.AddParameter("radius", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    break;

            }
        }
    }
}
