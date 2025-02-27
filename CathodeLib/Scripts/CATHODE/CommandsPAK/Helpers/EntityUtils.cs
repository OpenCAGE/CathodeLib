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
                case FunctionType.ScriptInterface:
                    entity.AddParameter("name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NetworkProxy:
                    break;
                case FunctionType.ProxyInterface:
                    break;
                case FunctionType.ScriptVariable:
                    break;
                case FunctionType.Filter:
                    break;
                case FunctionType.InspectorInterface:
                    break;
                case FunctionType.EvaluatorInterface:
                    break;
                case FunctionType.ModifierInterface:
                    break;
                case FunctionType.SensorInterface:
                    break;
                case FunctionType.TransformerInterface:
                    break;
                case FunctionType.CloseableInterface:
                    break;
                case FunctionType.GateInterface:
                    break;
                case FunctionType.ZoneInterface:
                    entity.AddParameter("force_visible_on_load", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AttachmentInterface:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SensorAttachmentInterface:
                    break;
                case FunctionType.CompositeInterface:
                    entity.AddParameter("disable_display", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("disable_collision", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("disable_simulation", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("mapping", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.EnvironmentModelReference:
                    break;
                case FunctionType.PositionMarker:
                    break;
                case FunctionType.SplinePath:
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("orientated", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Box:
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.HasAccessAtDifficulty:
                    entity.AddParameter("difficulty", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UpdateLeaderBoardDisplay:
                    entity.AddParameter("time", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetNextLoadingMovie:
                    entity.AddParameter("playlist_to_load", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ButtonMashPrompt:
                    entity.AddParameter("mashes_to_completion", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("time_between_degrades", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_degrade", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("hold_to_charge", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetFlashIntValue:
                    entity.AddParameter("callback_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetFlashFloatValue:
                    entity.AddParameter("callback_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Sphere:
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ImpactSphere:
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UiSelectionBox:
                    entity.AddParameter("is_priority", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UiSelectionSphere:
                    entity.AddParameter("is_priority", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CollisionBarrier:
                    entity.AddParameter("collision_type", new cEnum(EnumType.COLLISION_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("static_collision", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayerTriggerBox:
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayerUseTriggerBox:
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("text", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ModelReference:
                    entity.AddParameter("material", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("occludes_atmosphere", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("lod_ranges", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("intensity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("radiosity_multiplier", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("emissive_tint", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("replace_intensity", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("replace_tint", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("decal_scale", new cVector3(new Vector3(1.0f, 1.0f, 1.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("lightdecal_tint", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("lightdecal_intensity", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("diffuse_colour_scale", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("diffuse_opacity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("vertex_colour_scale", new cVector3(new Vector3(1.0f, 1.0f, 1.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("vertex_opacity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("uv_scroll_speed_x", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("uv_scroll_speed_y", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("alpha_blend_noise_power_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("alpha_blend_noise_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("alpha_blend_noise_uv_offset_X", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("alpha_blend_noise_uv_offset_Y", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("dirt_multiply_blend_spec_power_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("dirt_map_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("remove_on_damaged", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("damage_threshold", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_debris", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_prop", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_thrown", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("report_sliding", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_keyframed", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_transparent", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("soft_collision", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("allow_reposition_of_physics", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("disable_size_culling", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("cast_shadows", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("cast_shadows_in_torch", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.LightReference:
                    entity.AddParameter("type", new cEnum(EnumType.LIGHT_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("defocus_attenuation", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("start_attenuation", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("end_attenuation", new cFloat(2.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("physical_attenuation", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("near_dist", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("near_dist_shadow_offset", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("inner_cone_angle", new cFloat(22.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("outer_cone_angle", new cFloat(45.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("intensity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("radiosity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("area_light_radius", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("diffuse_softness", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("diffuse_bias", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("glossiness_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("flare_occluder_radius", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("flare_spot_offset", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("flare_intensity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("cast_shadow", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("fade_type", new cEnum(EnumType.LIGHT_FADE_TYPE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_specular", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("has_lens_flare", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("has_noclip", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_square_light", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_flash_light", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("no_alphalight", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("shadow_priority", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("aspect_ratio", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("gobo_texture", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("horizontal_gobo_flip", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("strip_length", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("distance_mip_selection_gobo", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("volume", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("volume_end_attenuation", new cFloat(-1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("volume_colour_factor", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("volume_density", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("depth_bias", new cFloat(0.05f), ParameterVariant.PARAMETER);
                    entity.AddParameter("slope_scale_depth_bias", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ParticleEmitterReference:
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("material", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("unique_material", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("quality_level", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("bounds_max", new cVector3(new Vector3(2.0f, 2.0f, 2.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("bounds_min", new cVector3(new Vector3(-2.0f, -2.0f, -2.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("DRAW_PASS", new cInteger(8), ParameterVariant.PARAMETER);
                    entity.AddParameter("ASPECT_RATIO", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FADE_AT_DISTANCE", new cFloat(5000.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PARTICLE_COUNT", new cInteger(100), ParameterVariant.PARAMETER);
                    entity.AddParameter("SYSTEM_EXPIRY_TIME", new cFloat(10f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SIZE_START_MIN", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SIZE_START_MAX", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SIZE_END_MIN", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SIZE_END_MAX", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_IN", new cFloat(0.01f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_OUT", new cFloat(99.99f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MASK_AMOUNT_MIN", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MASK_AMOUNT_MAX", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MASK_AMOUNT_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PARTICLE_EXPIRY_TIME_MIN", new cFloat(2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PARTICLE_EXPIRY_TIME_MAX", new cFloat(2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_SCALE_MIN", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_SCALE_MAX", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIND_X", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIND_Y", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIND_Z", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_REF_VALUE", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING_LS", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING_NONE", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING_ON_AXIS_X", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING_ON_AXIS_Y", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING_ON_AXIS_Z", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING_VELOCITY_ALIGNED", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING_VELOCITY_STRETCHED", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING_SPHERE_PROJECTION", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLENDING_STANDARD", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLENDING_ALPHA_REF", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLENDING_ADDITIVE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLENDING_PREMULTIPLIED", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLENDING_DISTORTION", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("LOW_RES", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("EARLY_ALPHA", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("LOOPING", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("ANIMATED_ALPHA", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("NONE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("LIGHTING", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("PER_PARTICLE_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("X_AXIS_FLIP", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("Y_AXIS_FLIP", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARD_FACING", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING_ON_AXIS_FADEOUT", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARDING_CAMERA_LOCKED", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAMERA_RELATIVE_POS_X", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAMERA_RELATIVE_POS_Y", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAMERA_RELATIVE_POS_Z", new cFloat(3.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPHERE_PROJECTION_RADIUS", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DISTORTION_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SCALE_MODIFIER", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CPU", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPAWN_RATE", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPAWN_RATE_VAR", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPAWN_NUMBER", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("LIFETIME", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LIFETIME_VAR", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_START", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_END", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WORLD_TO_LOCAL_MAX_DIST", new cFloat(1000.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CELL_EMISSION", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("CELL_MAX_DIST", new cFloat(6.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CUSTOM_SEED_CPU", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("SEED", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_TEST", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("ZTEST", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("START_MID_END_SPEED", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED_MID_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED_MID_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED_END_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED_END_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LAUNCH_DECELERATE_SPEED", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("LAUNCH_DECELERATE_SPEED_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LAUNCH_DECELERATE_SPEED_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LAUNCH_DECELERATE_DEC_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMISSION_AREA", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMISSION_AREA_X", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMISSION_AREA_Y", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMISSION_AREA_Z", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMISSION_SURFACE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMISSION_DIRECTION_SURFACE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("AREA_CUBOID", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("AREA_SPHEROID", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("AREA_CYLINDER", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("PIVOT_X", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PIVOT_Y", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("GRAVITY", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("GRAVITY_STRENGTH", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("GRAVITY_MAX_STRENGTH", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT_START", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT_END", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_USE_MID", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT_MID", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPREAD_FEATURE", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPREAD_MIN", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPREAD", new cFloat(360f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROTATION", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROTATION_MIN", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROTATION_MAX", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROTATION_RANDOM_START", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROTATION_BASE", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROTATION_VAR", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROTATION_RAMP", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROTATION_IN", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROTATION_OUT", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROTATION_DAMP", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FADE_NEAR_CAMERA", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("FADE_NEAR_CAMERA_MAX_DIST", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FADE_NEAR_CAMERA_THRESHOLD", new cFloat(0.8f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TEXTURE_ANIMATION", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("TEXTURE_ANIMATION_FRAMES", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("NUM_ROWS", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("TEXTURE_ANIMATION_LOOP_COUNT", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("RANDOM_START_FRAME", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("WRAP_FRAMES", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("NO_ANIM", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("SUB_FRAME_BLEND", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS_ALPHA_THICKNESS", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS_ALPHA_DEPTH_MODIFIER", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("REVERSE_SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("REVERSE_SOFTNESS_EDGE", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PIVOT_AND_TURBULENCE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("PIVOT_OFFSET_MIN", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PIVOT_OFFSET_MAX", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TURBULENCE_FREQUENCY_MIN", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TURBULENCE_FREQUENCY_MAX", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TURBULENCE_AMOUNT_MIN", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TURBULENCE_AMOUNT_MAX", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHATHRESHOLD", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHATHRESHOLD_TOTALTIME", new cFloat(5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHATHRESHOLD_RANGE", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHATHRESHOLD_BEGINSTART", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHATHRESHOLD_BEGINSTOP", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHATHRESHOLD_ENDSTART", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHATHRESHOLD_ENDSTOP", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_RAMP", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_RAMP_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_RAMP_ALPHA", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FADE_AXIS", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FADE_AXIS_DIST", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FADE_AXIS_PERCENT", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_UV_ANIMATION", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("CYCLE_TIME", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_SPEED", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("INFINITE_PROJECTION", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("PARALLAX_POSITION", new cVector3(new Vector3(0.0f, 0.0f, 0.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DISTORTION_OCCLUSION", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("AMBIENT_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("AMBIENT_LIGHTING_COLOUR", new cVector3(new Vector3(0.0f, 0.0f, 0.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("NO_CLIP", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RibbonEmitterReference:
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("material", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("unique_material", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("quality_level", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLENDING_STANDARD", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLENDING_ALPHA_REF", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLENDING_ADDITIVE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLENDING_PREMULTIPLIED", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLENDING_DISTORTION", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("NO_MIPS", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("UV_SQUARED", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("LOW_RES", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("LIGHTING", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("MASK_AMOUNT_MIN", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MASK_AMOUNT_MAX", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MASK_AMOUNT_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DRAW_PASS", new cInteger(8), ParameterVariant.PARAMETER);
                    entity.AddParameter("SYSTEM_EXPIRY_TIME", new cFloat(10f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LIFETIME", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SMOOTHED", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_START", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WORLD_TO_LOCAL_BLEND_END", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WORLD_TO_LOCAL_MAX_DIST", new cFloat(1000.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TEXTURE", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("UV_REPEAT", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("UV_SCROLLSPEED", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MULTI_TEXTURE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("U2_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("V2_REPEAT", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("V2_SCROLLSPEED", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MULTI_TEXTURE_BLEND", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("MULTI_TEXTURE_ADD", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("MULTI_TEXTURE_MULT", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("MULTI_TEXTURE_MAX", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("MULTI_TEXTURE_MIN", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECOND_TEXTURE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("TEXTURE_MAP2", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("CONTINUOUS", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("BASE_LOCKED", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPAWN_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TRAILING", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("INSTANT", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("RATE", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("TRAIL_SPAWN_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TRAIL_DELAY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MAX_TRAILS", new cFloat(5.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("POINT_TO_POINT", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("TARGET_POINT_POSITION", new cVector3(new Vector3(0.0f, 0.0f, 0.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ABS_FADE_IN_0", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ABS_FADE_IN_1", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FORCES", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("GRAVITY_STRENGTH", new cFloat(-4.81f), ParameterVariant.PARAMETER);
                    entity.AddParameter("GRAVITY_MAX_STRENGTH", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DRAG_STRENGTH", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIND_X", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIND_Y", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIND_Z", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("START_MID_END_SPEED", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED_START_MIN", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED_START_MAX", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIDTH", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIDTH_START", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIDTH_MID", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIDTH_END", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIDTH_IN", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WIDTH_OUT", new cFloat(0.8f), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_SCALE_START", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_SCALE_MID", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_SCALE_END", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT_START", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT_MID", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT_END", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_FADE", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("FADE_IN", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FADE_OUT", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EDGE_FADE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_ERODE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("SIDE_ON_FADE", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("SIDE_FADE_START", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SIDE_FADE_END", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DISTANCE_SCALING", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("DIST_SCALE", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPREAD_FEATURE", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPREAD_MIN", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPREAD", new cFloat(0.99999f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMISSION_AREA", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMISSION_AREA_X", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMISSION_AREA_Y", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMISSION_AREA_Z", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("AREA_CUBOID", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("AREA_SPHEROID", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("AREA_CYLINDER", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_RAMP", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_RAMP_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS_ALPHA_THICKNESS", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS_ALPHA_DEPTH_MODIFIER", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("AMBIENT_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("AMBIENT_LIGHTING_COLOUR", new cVector3(new Vector3(0.0f, 0.0f, 0.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("NO_CLIP", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GPU_PFXEmitterReference:
                    entity.AddParameter("EFFECT_NAME", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPAWN_NUMBER", new cInteger(100), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPAWN_RATE", new cFloat(100.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPREAD_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPREAD_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EMITTER_SIZE", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LIFETIME", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LIFETIME_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FogSphere:
                    entity.AddParameter("COLOUR_TINT", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EARLY_ALPHA", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("LOW_RES_ALPHA", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("CONVEX_GEOM", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DISABLE_SIZE_CULLING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("NO_CLIP", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_LIGHTING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DYNAMIC_ALPHA_LIGHTING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("EXPONENTIAL_DENSITY", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("SCENE_DEPENDANT_DENSITY", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("FRESNEL_TERM", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("FRESNEL_POWER", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("BLEND_ALPHA_OVER_DISTANCE", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("FAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("NEAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_BLEND_ALPHA_OVER_DISTANCE", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_FAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_NEAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_COLOUR", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_COLOUR_VALUE", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_ALPHA_VALUE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_RANGE", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FogBox:
                    entity.AddParameter("GEOMETRY_TYPE", new cEnum(EnumType.FOG_BOX_TYPE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DISTANCE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ANGLE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("BILLBOARD", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("EARLY_ALPHA", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("LOW_RES", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("CONVEX_GEOM", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("THICKNESS", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("START_DISTANT_CLIP", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("START_DISTANCE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("SOFTNESS_EDGE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LINEAR_HEIGHT_DENSITY", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("SMOOTH_HEIGHT_DENSITY", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("HEIGHT_MAX_DENSITY", new cFloat(0.4f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FRESNEL_FALLOFF", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("FRESNEL_POWER", new cFloat(3.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_COLOUR", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_INITIAL_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_MIDPOINT_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_MIDPOINT_ALPHA", new cFloat(1.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_MIDPOINT_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_END_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_END_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_INTERSECT_END_DEPTH", new cFloat(2.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SurfaceEffectSphere:
                    entity.AddParameter("COLOUR_TINT", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT_OUTER", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FADE_OUT_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SURFACE_WRAP", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROUGHNESS_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPARKLE_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("METAL_STYLE_REFLECTIONS", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SHININESS_OPACITY", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TILING_ZY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TILING_ZX", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TILING_XY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("WS_LOCKED", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPARKLE_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVMAP", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVMAP_PERCENT_EMISSIVE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPHERE", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SurfaceEffectBox:
                    entity.AddParameter("COLOUR_TINT", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("COLOUR_TINT_OUTER", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FADE_OUT_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SURFACE_WRAP", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ROUGHNESS_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPARKLE_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("METAL_STYLE_REFLECTIONS", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SHININESS_OPACITY", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TILING_ZY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TILING_ZX", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("TILING_XY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FALLOFF", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER);
                    entity.AddParameter("WS_LOCKED", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPARKLE_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVMAP", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVMAP_PERCENT_EMISSIVE", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPHERE", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("BOX", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SimpleWater:
                    entity.AddParameter("SHININESS", new cFloat(0.8f), ParameterVariant.PARAMETER);
                    entity.AddParameter("softness_edge", new cFloat(0.005f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FRESNEL_POWER", new cFloat(0.8f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MIN_FRESNEL", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MAX_FRESNEL", new cFloat(5.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LOW_RES_ALPHA_PASS", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ATMOSPHERIC_FOGGING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED", new cFloat(0.01f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_NORMAL_MAPPING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(-0.01f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_MASKING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_MASK", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_MAPPING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("CYCLE_TIME", new cFloat(10f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVIRONMENT_MAPPING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVIRONMENT_MAP_MULT", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LOCALISED_ENVIRONMENT_MAPPING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVMAP_SIZE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LOCALISED_ENVMAP_BOX_PROJECTION", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVMAP_BOXPROJ_BB_SCALE", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER);
                    entity.AddParameter("REFLECTIVE_MAPPING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("REFLECTION_PERTURBATION_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_INITIAL_COLOUR", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_COLOUR", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_DEPTH", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_END_COLOUR", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_END_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_END_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAUSTIC_TEXTURE", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAUSTIC_TEXTURE_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAUSTIC_REFRACTIONS", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAUSTIC_REFLECTIONS", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAUSTIC_SPEED_SCALAR", new cFloat(20.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAUSTIC_INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAUSTIC_SURFACE_WRAP", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CAUSTIC_HEIGHT", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SimpleRefraction:
                    entity.AddParameter("DISTANCEFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_NORMAL_MAPPING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_MASKING", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_MASK", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("DISTORTION_OCCLUSION", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("MIN_OCCLUSION_DISTANCE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_UV_ANIMATION", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("CYCLE_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ProjectiveDecal:
                    entity.AddParameter("time", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("material", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.LODControls:
                    break;
                case FunctionType.LightingMaster:
                    break;
                case FunctionType.DebugCamera:
                    break;
                case FunctionType.CameraResource:
                    entity.AddParameter("camera_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_camera_transformation_local", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("camera_transformation", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("fov", new cFloat(45.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("clipping_planes_preset", new cEnum(EnumType.CLIPPING_PLANES_PRESETS, 2), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_ghost", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("converge_to_player_camera", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("reset_player_camera_on_exit", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("enable_enter_transition", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("transition_curve_direction", new cEnum(EnumType.TRANSITION_DIRECTION, 4), ParameterVariant.PARAMETER);
                    entity.AddParameter("transition_curve_strength", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("enable_exit_transition", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("exit_transition_curve_direction", new cEnum(EnumType.TRANSITION_DIRECTION, 4), ParameterVariant.PARAMETER);
                    entity.AddParameter("exit_transition_curve_strength", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("exit_transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("exit_transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("exit_transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CameraCollisionBox:
                    break;
                case FunctionType.CameraFinder:
                    entity.AddParameter("camera_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayerCamera:
                    break;
                case FunctionType.CameraBehaviorInterface:
                    entity.AddParameter("behavior_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("priority", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("threshold", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("blend_in", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("blend_out", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.HandCamera:
                    entity.AddParameter("noise_type", new cEnum(EnumType.NOISE_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("frequency", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("damping", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("rotation_intensity", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("min_fov_range", new cFloat(45.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_fov_range", new cFloat(45.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("min_noise", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_noise", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CameraShake:
                    entity.AddParameter("shake_type", new cEnum(EnumType.SHAKE_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("shake_frequency", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_rotation_angles", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_position_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("shake_rotation", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("shake_position", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("bone_shaking", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("override_weapon_swing", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("internal_radius", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("external_radius", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("strength_damping", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("explosion_push_back", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("spring_constant", new cFloat(3.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("spring_damping", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CameraPathDriven:
                    entity.AddParameter("path_driven_type", new cEnum(EnumType.PATH_DRIVEN_TYPE, 2), ParameterVariant.PARAMETER);
                    entity.AddParameter("invert_progression", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("position_path_offset", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("target_path_offset", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("animation_duration", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FixedCamera:
                    entity.AddParameter("use_transform_position", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("transform_position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("camera_position", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("camera_target", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("camera_position_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("camera_target_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("apply_target", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("apply_position", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_target_offset", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_position_offset", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.BoneAttachedCamera:
                    entity.AddParameter("position_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("rotation_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("movement_damping", new cFloat(0.6f), ParameterVariant.PARAMETER);
                    entity.AddParameter("bone_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ControllableRange:
                    entity.AddParameter("min_range_x", new cFloat(-180f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_range_x", new cFloat(180f), ParameterVariant.PARAMETER);
                    entity.AddParameter("min_range_y", new cFloat(-180f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_range_y", new cFloat(180f), ParameterVariant.PARAMETER);
                    entity.AddParameter("min_feather_range_x", new cFloat(-180f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_feather_range_x", new cFloat(180f), ParameterVariant.PARAMETER);
                    entity.AddParameter("min_feather_range_y", new cFloat(-180f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_feather_range_y", new cFloat(180f), ParameterVariant.PARAMETER);
                    entity.AddParameter("speed_x", new cFloat(30.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("speed_y", new cFloat(30.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("damping_x", new cFloat(0.6f), ParameterVariant.PARAMETER);
                    entity.AddParameter("damping_y", new cFloat(0.6f), ParameterVariant.PARAMETER);
                    entity.AddParameter("mouse_speed_x", new cFloat(30.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("mouse_speed_y", new cFloat(30.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.StealCamera:
                    entity.AddParameter("steal_type", new cEnum(EnumType.STEAL_CAMERA_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("check_line_of_sight", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("blend_in_duration", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FollowCameraModifier:
                    entity.AddParameter("modifier_type", new cEnum(EnumType.FOLLOW_CAMERA_MODIFIERS, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("position_offset", new cVector3(new Vector3(0.5f, 1.5f, -3.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("target_offset", new cVector3(new Vector3(0.5f, 1.5f, 0.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("field_of_view", new cFloat(35.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_state", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_state_initial_value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("can_mirror", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_first_person", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("bone_blending_ratio", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("movement_speed", new cFloat(0.7f), ParameterVariant.PARAMETER);
                    entity.AddParameter("movement_speed_vertical", new cFloat(0.7f), ParameterVariant.PARAMETER);
                    entity.AddParameter("movement_damping", new cFloat(0.7f), ParameterVariant.PARAMETER);
                    entity.AddParameter("horizontal_limit_min", new cFloat(-1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("horizontal_limit_max", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("vertical_limit_min", new cFloat(-1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("vertical_limit_max", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("mouse_speed_hori", new cFloat(0.7f), ParameterVariant.PARAMETER);
                    entity.AddParameter("mouse_speed_vert", new cFloat(0.7f), ParameterVariant.PARAMETER);
                    entity.AddParameter("acceleration_duration", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("acceleration_ease_in", new cFloat(0.25f), ParameterVariant.PARAMETER);
                    entity.AddParameter("acceleration_ease_out", new cFloat(0.25f), ParameterVariant.PARAMETER);
                    entity.AddParameter("transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CameraPath:
                    entity.AddParameter("path_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("path_type", new cEnum(EnumType.CAMERA_PATH_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("path_class", new cEnum(EnumType.CAMERA_PATH_CLASS, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_local", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("relative_position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_loop", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CameraAimAssistant:
                    entity.AddParameter("activation_radius", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("inner_radius", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("camera_speed_attenuation", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("min_activation_distance", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("fading_range", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CameraPlayAnimation:
                    entity.AddParameter("data_file", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("start_frame", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("end_frame", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("play_speed", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("loop_play", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("clipping_planes_preset", new cEnum(EnumType.CLIPPING_PLANES_PRESETS, 2), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("dof_key", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("shot_number", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("override_dof", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("focal_point_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("bone_to_focus", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CamPeek:
                    entity.AddParameter("range_left", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("range_right", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("range_up", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("range_down", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("range_forward", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("range_backward", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("speed_x", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("speed_y", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("damping_x", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("damping_y", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("focal_distance", new cFloat(8.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("focal_distance_y", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("roll_factor", new cFloat(15.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_ik_solver", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_horizontal_plane", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("stick", new cEnum(EnumType.SIDE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("disable_collision_test", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CameraDofController:
                    entity.AddParameter("focal_point_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("bone_to_focus", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ClipPlanesController:
                    break;
                case FunctionType.GetCurrentCameraPos:
                    break;
                case FunctionType.GetCurrentCameraTarget:
                    break;
                case FunctionType.GetCurrentCameraFov:
                    break;
                case FunctionType.CharacterShivaArms:
                    break;
                case FunctionType.Logic_Vent_Entrance:
                    entity.AddParameter("force_stand_on_exit", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Logic_Vent_System:
                    break;
                case FunctionType.CharacterCommand:
                    entity.AddParameter("override_all_ai", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_Follow:
                    entity.AddParameter("idle_stance", new cEnum(EnumType.IDLE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("inner_radius", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("outer_radius", new cFloat(2.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("prefer_traversals", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_FollowUsingJobs:
                    entity.AddParameter("fastest_allowed_move_type", new cEnum(EnumType.MOVE, 3), ParameterVariant.PARAMETER);
                    entity.AddParameter("slowest_allowed_move_type", new cEnum(EnumType.MOVE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("centre_job_restart_radius", new cFloat(2.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("inner_radius", new cFloat(4.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("outer_radius", new cFloat(8.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("job_select_radius", new cFloat(6.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("job_cancel_radius", new cFloat(8.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("teleport_required_range", new cFloat(25.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("teleport_radius", new cFloat(20.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("prefer_traversals", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("avoid_player", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("allow_teleports", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("follow_type", new cEnum(EnumType.FOLLOW_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("clamp_speed", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_FollowOffset:
                    break;
                case FunctionType.AnimationMask:
                    entity.AddParameter("maskHips", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskTorso", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskNeck", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskHead", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskFace", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskLeftLeg", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskRightLeg", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskLeftArm", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskRightArm", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskLeftHand", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskRightHand", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskLeftFingers", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskRightFingers", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskTail", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskLips", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskEyes", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskLeftShoulder", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskRightShoulder", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskRoot", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskPrecedingLayers", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskSelf", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maskFollowingLayers", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("weight", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_PlayAnimation:
                    entity.AddParameter("AnimationSet", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("Animation", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("StartFrame", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("EndFrame", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("PlayCount", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("PlaySpeed", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("AllowGravity", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("AllowCollision", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Start_Instantly", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("AllowInterruption", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("RemoveMotion", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DisableGunLayer", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("BlendInTime", new cFloat(0.3f), ParameterVariant.PARAMETER);
                    entity.AddParameter("GaitSyncStart", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ConvergenceTime", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LocationConvergence", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("OrientationConvergence", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("UseExitConvergence", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ExitConvergenceTime", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Mirror", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("FullCinematic", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("RagdollEnabled", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("NoIK", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("NoFootIK", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("NoLayers", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("PlayerAnimDrivenView", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("ExertionFactor", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("AutomaticZoning", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ManualLoading", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("IsCrouchedAnim", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("InitiallyBackstage", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Death_by_ragdoll_only", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("dof_key", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("shot_number", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("UseShivaArms", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_Idle:
                    entity.AddParameter("should_face_target", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("should_raise_gun_while_turning", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("desired_stance", new cEnum(EnumType.CHARACTER_STANCE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("idle_style", new cEnum(EnumType.IDLE_STYLE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("lock_cameras", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("anchor", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("start_instantly", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_StopScript:
                    break;
                case FunctionType.CMD_GoTo:
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("enable_lookaround", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_stopping_anim", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("always_stop_at_radius", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("stop_at_radius_if_lined_up", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("continue_from_previous_move", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("arrived_radius", new cFloat(0.6f), ParameterVariant.PARAMETER);
                    entity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("allow_to_use_vents", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DestinationIsBackstage", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("maintain_current_facing", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("start_instantly", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_GoToCover:
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("SearchRadius", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("enable_lookaround", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("continue_from_previous_move", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_MoveTowards:
                    entity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("never_succeed", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_Die:
                    entity.AddParameter("death_style", new cEnum(EnumType.DEATH_STYLE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_LaunchMeleeAttack:
                    entity.AddParameter("melee_attack_type", new cEnum(EnumType.MELEE_ATTACK_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("enemy_type", new cEnum(EnumType.ENEMY_TYPE, 15), ParameterVariant.PARAMETER);
                    entity.AddParameter("melee_attack_index", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("skip_convergence", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_ModifyCombatBehaviour:
                    entity.AddParameter("behaviour_type", new cEnum(EnumType.COMBAT_BEHAVIOUR, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("status", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_HolsterWeapon:
                    entity.AddParameter("should_holster", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("skip_anims", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("equipment_slot", new cEnum(EnumType.EQUIPMENT_SLOT, -2), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_player_unarmed_on_holster", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_drop_held_item", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_ForceReloadWeapon:
                    break;
                case FunctionType.CMD_ForceMeleeAttack:
                    entity.AddParameter("melee_attack_type", new cEnum(EnumType.MELEE_ATTACK_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("enemy_type", new cEnum(EnumType.ENEMY_TYPE, 15), ParameterVariant.PARAMETER);
                    entity.AddParameter("melee_attack_index", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_ModifyBreathing:
                    entity.AddParameter("Exhaustion", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_HoldBreath:
                    entity.AddParameter("ExhaustionOnStop", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_DeepCrouch:
                    entity.AddParameter("crouch_amount", new cFloat(0.4f), ParameterVariant.PARAMETER);
                    entity.AddParameter("smooth_damping", new cFloat(0.4f), ParameterVariant.PARAMETER);
                    entity.AddParameter("allow_stand_up", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_PlaySecondaryAnimation:
                    entity.AddParameter("AnimationSet", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("Animation", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("StartFrame", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("EndFrame", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("PlayCount", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("PlaySpeed", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("StartInstantly", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("AllowInterruption", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("BlendInTime", new cFloat(0.3f), ParameterVariant.PARAMETER);
                    entity.AddParameter("GaitSyncStart", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Mirror", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("AnimationLayer", new cEnum(EnumType.SECONDARY_ANIMATION_LAYER, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("AutomaticZoning", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ManualLoading", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_LocomotionModifier:
                    entity.AddParameter("Can_Run", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Can_Crouch", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Can_Aim", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Can_Injured", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Must_Walk", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Must_Run", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Must_Crouch", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Must_Aim", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Must_Injured", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Is_In_Spacesuit", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_SetMood:
                    entity.AddParameter("mood", new cEnum(EnumType.MOOD, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("moodIntensity", new cEnum(EnumType.MOOD_INTENSITY, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("timeOut", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_LocomotionEffect:
                    entity.AddParameter("Effect", new cEnum(EnumType.ANIMATION_EFFECT_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_LocomotionDuck:
                    entity.AddParameter("Height", new cEnum(EnumType.DUCK_HEIGHT, 1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_ShootAt:
                    break;
                case FunctionType.CMD_AimAtCurrentTarget:
                    entity.AddParameter("Raise_gun", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CMD_AimAt:
                    entity.AddParameter("Raise_gun", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_current_target", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Player_Sensor:
                    break;
                case FunctionType.CMD_Ragdoll:
                    break;
                case FunctionType.CHR_SetTacticalPosition:
                    entity.AddParameter("sweep_type", new cEnum(EnumType.AREA_SWEEP_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("fixed_sweep_radius", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_SetTacticalPositionToTarget:
                    break;
                case FunctionType.CHR_SetFocalPoint:
                    entity.AddParameter("priority", new cEnum(EnumType.PRIORITY, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("speed", new cEnum(EnumType.LOOK_SPEED, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("steal_camera", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_of_sight_test", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_SetAndroidThrowTarget:
                    break;
                case FunctionType.CHR_SetAlliance:
                    entity.AddParameter("Alliance", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_GetAlliance:
                    break;
                case FunctionType.ALLIANCE_SetDisposition:
                    entity.AddParameter("A", new cEnum(EnumType.ALLIANCE_GROUP, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("B", new cEnum(EnumType.ALLIANCE_GROUP, 5), ParameterVariant.PARAMETER);
                    entity.AddParameter("Disposition", new cEnum(EnumType.ALLIANCE_STANCE, 1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ALLIANCE_ResetAll:
                    break;
                case FunctionType.CHR_SetInvincibility:
                    entity.AddParameter("damage_mode", new cEnum(EnumType.DAMAGE_MODE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_SetHealth:
                    entity.AddParameter("HealthPercentage", new cInteger(100), ParameterVariant.PARAMETER);
                    entity.AddParameter("UsePercentageOfCurrentHeath", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_GetHealth:
                    break;
                case FunctionType.CHR_SetDebugDisplayName:
                    entity.AddParameter("DebugName", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_TakeDamage:
                    entity.AddParameter("Damage", new cInteger(100), ParameterVariant.PARAMETER);
                    entity.AddParameter("DamageIsAPercentage", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_SetSubModelVisibility:
                    entity.AddParameter("is_visible", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("matching", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_SetHeadVisibility:
                    entity.AddParameter("is_visible", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_SetFacehuggerAggroRadius:
                    entity.AddParameter("radius", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MonitorBase:
                    break;
                case FunctionType.CHR_DamageMonitor:
                    entity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, -65536), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_KnockedOutMonitor:
                    break;
                case FunctionType.CHR_DeathMonitor:
                    entity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, -65536), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_RetreatMonitor:
                    break;
                case FunctionType.CHR_WeaponFireMonitor:
                    break;
                case FunctionType.CHR_TorchMonitor:
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_VentMonitor:
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CharacterTypeMonitor:
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 2), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Convo:
                    entity.AddParameter("alwaysTalkToPlayerIfPresent", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("playerCanJoin", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("playerCanLeave", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("positionNPCs", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("circularShape", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("convoPosition", new cResource(), ParameterVariant.PARAMETER);
                    entity.AddParameter("personalSpaceRadius", new cFloat(0.4f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_NotifyDynamicDialogueEvent:
                    entity.AddParameter("DialogueEvent", new cEnum(EnumType.DIALOGUE_NPC_EVENT, -1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_Squad_DialogueMonitor:
                    entity.AddParameter("squad_coordinator", new cResource(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_Group_DeathCounter:
                    entity.AddParameter("TriggerThreshold", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_Group_Death_Monitor:
                    entity.AddParameter("squad_coordinator", new cResource(), ParameterVariant.PARAMETER);
                    entity.AddParameter("CheckAllNPCs", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_AllSensesLimiter:
                    break;
                case FunctionType.NPC_SenseLimiter:
                    entity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_ResetSensesAndMemory:
                    entity.AddParameter("ResetMenaceToFull", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ResetSensesLimiters", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetupMenaceManager:
                    entity.AddParameter("AgressiveMenace", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ProgressionFraction", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ResetMenaceMeter", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_AlienConfig:
                    entity.AddParameter("AlienConfigString", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetSenseSet:
                    entity.AddParameter("SenseSet", new cEnum(EnumType.SENSE_SET, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_GetLastSensedPositionOfTarget:
                    entity.AddParameter("MaxTimeSince", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Weapon_AINotifier:
                    break;
                case FunctionType.HeldItem_AINotifier:
                    break;
                case FunctionType.NPC_Gain_Aggression_In_Radius:
                    entity.AddParameter("AggressionGain", new cEnum(EnumType.AGGRESSION_GAIN, 1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_Aggression_Monitor:
                    break;
                case FunctionType.Explosion_AINotifier:
                    entity.AddParameter("ExplosionPos", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 12), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_Sleeping_Android_Monitor:
                    break;
                case FunctionType.NPC_Highest_Awareness_Monitor:
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("CheckAllNPCs", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_Squad_GetAwarenessState:
                    break;
                case FunctionType.NPC_Squad_GetAwarenessWatermark:
                    break;
                case FunctionType.PlayerCameraMonitor:
                    break;
                case FunctionType.ScreenEffectEventMonitor:
                    break;
                case FunctionType.DEBUG_SenseLevels:
                    entity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_FakeSense:
                    entity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER);
                    entity.AddParameter("ForceThreshold", new cEnum(EnumType.THRESHOLD_QUALIFIER, 2), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SuspiciousItem:
                    entity.AddParameter("Item", new cEnum(EnumType.SUSPICIOUS_ITEM, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("InitialReactionValidStartDuration", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FurtherReactionValidStartDuration", new cFloat(6.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("RetriggerDelay", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Trigger", new cEnum(EnumType.SUSPICIOUS_ITEM_TRIGGER, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("ShouldMakeAggressive", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("MaxGroupMembersInteract", new cInteger(2), ParameterVariant.PARAMETER);
                    entity.AddParameter("SystematicSearchRadius", new cFloat(8.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("AllowSamePriorityToOveride", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("UseSamePriorityCloserDistanceConstraint", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("SamePriorityCloserDistanceConstraint", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("UseSamePriorityRecentTimeConstraint", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("SamePriorityRecentTimeConstraint", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("BehaviourTreePriority", new cEnum(EnumType.SUSPICIOUS_ITEM_BEHAVIOUR_TREE_PRIORITY, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("InteruptSubPriority", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("DetectableByBackstageAlien", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("DoIntialReaction", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("MoveCloseToSuspectPosition", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DoCloseToReaction", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DoCloseToWaitForGroupMembers", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DoSystematicSearch", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("GroupNotify", new cEnum(EnumType.SUSPICIOUS_ITEM_STAGE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("DoIntialReactionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("MoveCloseToSuspectPositionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DoCloseToReactionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DoCloseToWaitForGroupMembersSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("DoSystematicSearchSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetAlienDevelopmentStage:
                    entity.AddParameter("AlienStage", new cEnum(EnumType.ALIEN_DEVELOPMENT_MANAGER_STAGES, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("Reset", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_TargetAcquire:
                    break;
                case FunctionType.CHR_IsWithinRange:
                    entity.AddParameter("Range_test_shape", new cEnum(EnumType.RANGE_TEST_SHAPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_ForceCombatTarget:
                    entity.AddParameter("LockOtherAttackersOut", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetAimTarget:
                    break;
                case FunctionType.CHR_SetTorch:
                    entity.AddParameter("TorchOn", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_GetTorch:
                    break;
                case FunctionType.NPC_SetAutoTorchMode:
                    entity.AddParameter("AutoUseTorchInDark", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_GetCombatTarget:
                    break;
                case FunctionType.NPC_SetTotallyBlindInDark:
                    break;
                case FunctionType.NPC_AreaBox:
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_MeleeContext:
                    entity.AddParameter("Context_Type", new cEnum(EnumType.MELEE_CONTEXT_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetSafePoint:
                    break;
                case FunctionType.Player_ExploitableArea:
                    break;
                case FunctionType.NPC_SetDefendArea:
                    break;
                case FunctionType.NPC_SetPursuitArea:
                    break;
                case FunctionType.NPC_ClearDefendArea:
                    break;
                case FunctionType.NPC_ClearPursuitArea:
                    break;
                case FunctionType.NPC_ForceRetreat:
                    break;
                case FunctionType.NPC_DefineBackstageAvoidanceArea:
                    break;
                case FunctionType.NPC_SetAlertness:
                    entity.AddParameter("AlertState", new cEnum(EnumType.ALERTNESS_STATE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetStartPos:
                    break;
                case FunctionType.NPC_SetAgressionProgression:
                    entity.AddParameter("allow_progression", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetLocomotionStyleForJobs:
                    break;
                case FunctionType.NPC_SetLocomotionTargetSpeed:
                    entity.AddParameter("Speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetGunAimMode:
                    entity.AddParameter("AimingMode", new cEnum(EnumType.NPC_GUN_AIM_MODE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_Coordinator:
                    break;
                case FunctionType.NPC_set_behaviour_tree_flags:
                    entity.AddParameter("BehaviourTreeFlag", new cEnum(EnumType.BEHAVIOUR_TREE_FLAGS, 2), ParameterVariant.PARAMETER);
                    entity.AddParameter("FlagSetting", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetHidingSearchRadius:
                    entity.AddParameter("Radius", new cFloat(15.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetHidingNearestLocation:
                    break;
                case FunctionType.NPC_WithdrawAlien:
                    entity.AddParameter("allow_any_searches_to_complete", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("permanent", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("killtraps", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("initial_radius", new cFloat(15.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("timed_out_radius", new cFloat(3.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("time_to_force", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_behaviour_monitor:
                    entity.AddParameter("behaviour", new cEnum(EnumType.BEHAVIOR_TREE_BRANCH_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_multi_behaviour_monitor:
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_ambush_monitor:
                    entity.AddParameter("ambush_type", new cEnum(EnumType.AMBUSH_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetInvisible:
                    break;
                case FunctionType.NPC_navmesh_type_monitor:
                    entity.AddParameter("nav_mesh_type", new cEnum(EnumType.NAV_MESH_AREA_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_HasWeaponOfType:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("check_if_weapon_draw", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_TriggerAimRequest:
                    entity.AddParameter("Raise_gun", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_current_target", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("clamp_angle", new cFloat(30.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("clear_current_requests", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_StopAiming:
                    break;
                case FunctionType.NPC_TriggerShootRequest:
                    entity.AddParameter("empty_current_clip", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("shot_count", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("clear_current_requests", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_StopShooting:
                    break;
                case FunctionType.Squad_SetMaxEscalationLevel:
                    entity.AddParameter("max_level", new cEnum(EnumType.NPC_AGGRO_LEVEL, 5), ParameterVariant.PARAMETER);
                    entity.AddParameter("squad_coordinator", new cResource(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Chr_PlayerCrouch:
                    entity.AddParameter("crouch", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_Once:
                    break;
                case FunctionType.Custom_Hiding_Vignette_controller:
                    break;
                case FunctionType.Custom_Hiding_Controller:
                    break;
                case FunctionType.TorchDynamicMovement:
                    entity.AddParameter("max_spatial_velocity", new cFloat(5.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_angular_velocity", new cFloat(30.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_position_displacement", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_target_displacement", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("position_damping", new cFloat(0.6f), ParameterVariant.PARAMETER);
                    entity.AddParameter("target_damping", new cFloat(0.6f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.EQUIPPABLE_ITEM:
                    entity.AddParameter("character_animation_context", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_activate_animation_context", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("left_handed", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("inventory_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("equipment_slot", new cEnum(EnumType.EQUIPMENT_SLOT, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("holsters_on_owner", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("holster_node", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("holster_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("weapon_handedness", new cEnum(EnumType.WEAPON_HANDEDNESS, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AIMED_ITEM:
                    entity.AddParameter("fixed_target_distance_for_local_player", new cFloat(6.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MELEE_WEAPON:
                    entity.AddParameter("normal_attack_damage", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("power_attack_damage", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("position_input", new cTransform(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AIMED_WEAPON:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 2), ParameterVariant.PARAMETER);
                    entity.AddParameter("requires_turning_on", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("ejectsShellsOnFiring", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("aim_assist_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("default_ammo_type", new cEnum(EnumType.AMMO_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("starting_ammo", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("clip_size", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("consume_ammo_over_time_when_turned_on", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_auto_shots_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_manual_shots_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("wind_down_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("maximum_continous_fire_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("overheat_recharge_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("automatic_firing", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("overheats", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("charged_firing", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("charging_duration", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("min_charge_to_fire", new cFloat(0.3f), ParameterVariant.PARAMETER);
                    entity.AddParameter("overcharge_timer", new cFloat(2.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("charge_noise_start_time", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("reloadIndividualAmmo", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("alwaysDoFullReloadOfClips", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("movement_accuracy_penalty_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("aim_rotation_accuracy_penalty_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("accuracy_penalty_per_shot", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("accuracy_accumulated_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("player_exposed_accuracy_penalty_per_shot", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("player_exposed_accuracy_accumulated_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("recoils_on_fire", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("alien_threat_aware", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayerWeaponMonitor:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 2), ParameterVariant.PARAMETER);
                    entity.AddParameter("ammo_percentage_in_clip", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RemoveWeaponsFromPlayer:
                    break;
                case FunctionType.PlayerDiscardsWeapons:
                    entity.AddParameter("discard_pistol", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_shotgun", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_flamethrower", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_boltgun", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_cattleprod", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_melee", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayerDiscardsItems:
                    entity.AddParameter("discard_ieds", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_medikits", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_ammo", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_flares_and_lights", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_materials", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_batteries", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayerDiscardsTools:
                    entity.AddParameter("discard_motion_tracker", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_cutting_torch", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_hacking_tool", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("discard_keycard", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_GiveToCharacter:
                    entity.AddParameter("is_holstered", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_GiveToPlayer:
                    entity.AddParameter("weapon", new cEnum(EnumType.EQUIPMENT_SLOT, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("holster", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("starting_ammo", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_ImpactEffect:
                    entity.AddParameter("Type", new cEnum(EnumType.WEAPON_IMPACT_EFFECT_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("Orientation", new cEnum(EnumType.WEAPON_IMPACT_EFFECT_ORIENTATION, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("Priority", new cInteger(16), ParameterVariant.PARAMETER);
                    entity.AddParameter("SafeDistant", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("LifeTime", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_damage_offset", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("RandomRotation", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_ImpactFilter:
                    entity.AddParameter("PhysicMaterial", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_AttackerFilter:
                    entity.AddParameter("filter", new cBool(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_TargetObjectFilter:
                    entity.AddParameter("filter", new cBool(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_ImpactInspector:
                    break;
                case FunctionType.WEAPON_DamageFilter:
                    entity.AddParameter("damage_threshold", new cInteger(100), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_DidHitSomethingFilter:
                    break;
                case FunctionType.WEAPON_MultiFilter:
                    entity.AddParameter("AttackerFilter", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("TargetFilter", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("DamageThreshold", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, 33554432), ParameterVariant.PARAMETER);
                    entity.AddParameter("UseAmmoFilter", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 22), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_ImpactCharacterFilter:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_body_location", new cEnum(EnumType.IMPACT_CHARACTER_BODY_LOCATION_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_Effect:
                    entity.AddParameter("LifeTime", new cFloat(0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_AmmoTypeFilter:
                    entity.AddParameter("AmmoType", new cEnum(EnumType.DAMAGE_EFFECTS, 33554432), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_ImpactAngleFilter:
                    entity.AddParameter("ReferenceAngle", new cFloat(60f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.WEAPON_ImpactOrientationFilter:
                    entity.AddParameter("ThresholdAngle", new cFloat(15f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Orientation", new cEnum(EnumType.WEAPON_IMPACT_FILTER_ORIENTATION, 2), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.EFFECT_ImpactGenerator:
                    entity.AddParameter("trigger_on_reset", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("min_distance", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("distance", new cFloat(3f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_count", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("spread", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("skip_characters", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.EFFECT_EntityGenerator:
                    entity.AddParameter("trigger_on_reset", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("spread", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_min", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_max", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_offset_XY_min", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_offset_XY_max", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_offset_Z_min", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_offset_Z_max", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("lifetime_min", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("lifetime_max", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.EFFECT_DirectionalPhysics:
                    entity.AddParameter("relative_direction", new cVector3(new Vector3(1.0f, 0.0f, 0.0f)), ParameterVariant.PARAMETER);
                    entity.AddParameter("effect_distance", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("angular_falloff", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("min_force", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_force", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlatformConstantBool:
                    entity.AddParameter("NextGen", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("X360", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("PS3", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlatformConstantInt:
                    entity.AddParameter("NextGen", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("X360", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("PS3", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlatformConstantFloat:
                    entity.AddParameter("NextGen", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("X360", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PS3", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VariableBool:
                    entity.AddParameter("initial_value", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VariableInt:
                    entity.AddParameter("initial_value", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VariableFloat:
                    entity.AddParameter("initial_value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VariableString:
                    entity.AddParameter("initial_value", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VariableVector:
                    entity.AddParameter("initial_x", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("initial_y", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("initial_z", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VariableVector2:
                    break;
                case FunctionType.VariableColour:
                    break;
                case FunctionType.VariableFlashScreenColour:
                    entity.AddParameter("flash_layer_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VariableHackingConfig:
                    entity.AddParameter("nodes", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("sensors", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("victory_nodes", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("victory_sensors", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VariableEnum:
                    entity.AddParameter("initial_value", new cEnum(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VariableEnumString:
                    break;
                case FunctionType.VariablePosition:
                    break;
                case FunctionType.VariableObject:
                    break;
                case FunctionType.VariableThePlayer:
                    break;
                case FunctionType.VariableFilterObject:
                    break;
                case FunctionType.VariableTriggerObject:
                    break;
                case FunctionType.VariableAnimationInfo:
                    entity.AddParameter("AnimationSet", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("Animation", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ExternalVariableBool:
                    entity.AddParameter("game_variable", new cString("DLC_Preorder_Weapon"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NonPersistentBool:
                    entity.AddParameter("initial_value", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NonPersistentInt:
                    entity.AddParameter("initial_value", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GameDVR:
                    entity.AddParameter("start_time", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("moment_ID", new cEnum(EnumType.GAME_CLIP, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Zone:
                    entity.AddParameter("suspend_on_unload", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("space_visible", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ZoneLink:
                    entity.AddParameter("cost", new cInteger(6), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ZoneExclusionLink:
                    entity.AddParameter("exclude_streaming", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ZoneLoaded:
                    break;
                case FunctionType.FlushZoneCache:
                    entity.AddParameter("CurrentGen", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("NextGen", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.StateQuery:
                    break;
                case FunctionType.IsActive:
                    break;
                case FunctionType.IsStarted:
                    break;
                case FunctionType.IsPaused:
                    break;
                case FunctionType.IsSuspended:
                    break;
                case FunctionType.IsAttached:
                    break;
                case FunctionType.IsEnabled:
                    break;
                case FunctionType.IsOpen:
                    break;
                case FunctionType.IsOpening:
                    break;
                case FunctionType.IsLocked:
                    break;
                case FunctionType.IsLoaded:
                    break;
                case FunctionType.IsLoading:
                    break;
                case FunctionType.IsVisible:
                    break;
                case FunctionType.IsSpawned:
                    break;
                case FunctionType.BooleanLogicInterface:
                    break;
                case FunctionType.LogicOnce:
                    break;
                case FunctionType.LogicDelay:
                    break;
                case FunctionType.TriggerSimple:
                    break;
                case FunctionType.LogicSwitch:
                    entity.AddParameter("initial_value", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.LogicGate:
                    break;
                case FunctionType.LogicGateAnd:
                    break;
                case FunctionType.LogicGateOr:
                    break;
                case FunctionType.LogicGateEquals:
                    break;
                case FunctionType.LogicGateNotEqual:
                    break;
                case FunctionType.BooleanLogicOperation:
                    break;
                case FunctionType.LogicNot:
                    break;
                case FunctionType.SetBool:
                    break;
                case FunctionType.FloatMath_All:
                    break;
                case FunctionType.FloatAdd_All:
                    break;
                case FunctionType.FloatMultiply_All:
                    entity.AddParameter("Invert", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FloatMax_All:
                    break;
                case FunctionType.FloatMin_All:
                    break;
                case FunctionType.FloatMath:
                    break;
                case FunctionType.FloatAdd:
                    break;
                case FunctionType.FloatSubtract:
                    break;
                case FunctionType.FloatMultiply:
                    break;
                case FunctionType.FloatMultiplyClamp:
                    break;
                case FunctionType.FloatClampMultiply:
                    break;
                case FunctionType.FloatDivide:
                    break;
                case FunctionType.FloatRemainder:
                    break;
                case FunctionType.FloatMax:
                    break;
                case FunctionType.FloatMin:
                    break;
                case FunctionType.FloatOperation:
                    break;
                case FunctionType.SetFloat:
                    break;
                case FunctionType.FloatAbsolute:
                    break;
                case FunctionType.FloatSqrt:
                    break;
                case FunctionType.FloatReciprocal:
                    break;
                case FunctionType.FloatCompare:
                    break;
                case FunctionType.FloatEquals:
                    break;
                case FunctionType.FloatNotEqual:
                    break;
                case FunctionType.FloatGreaterThan:
                    break;
                case FunctionType.FloatLessThan:
                    break;
                case FunctionType.FloatGreaterThanOrEqual:
                    break;
                case FunctionType.FloatLessThanOrEqual:
                    break;
                case FunctionType.FloatModulate:
                    entity.AddParameter("wave_shape", new cEnum(EnumType.WAVE_SHAPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("frequency", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("phase", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("amplitude", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("bias", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FloatModulateRandom:
                    entity.AddParameter("switch_on_anim", new cEnum(EnumType.LIGHT_TRANSITION, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("switch_on_delay", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("switch_on_custom_frequency", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("switch_on_duration", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("switch_off_anim", new cEnum(EnumType.LIGHT_TRANSITION, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("switch_off_custom_frequency", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("switch_off_duration", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("behaviour_anim", new cEnum(EnumType.LIGHT_ANIM, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("behaviour_frequency", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("behaviour_frequency_variance", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("behaviour_offset", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("pulse_modulation", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("oscillate_range_min", new cFloat(0.75f), ParameterVariant.PARAMETER);
                    entity.AddParameter("sparking_speed", new cFloat(0.9f), ParameterVariant.PARAMETER);
                    entity.AddParameter("blink_rate", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("blink_range_min", new cFloat(0.01f), ParameterVariant.PARAMETER);
                    entity.AddParameter("flicker_rate", new cFloat(0.75f), ParameterVariant.PARAMETER);
                    entity.AddParameter("flicker_off_rate", new cFloat(0.15f), ParameterVariant.PARAMETER);
                    entity.AddParameter("flicker_range_min", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("flicker_off_range_min", new cFloat(0.01f), ParameterVariant.PARAMETER);
                    entity.AddParameter("disable_behaviour", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FloatLinearProportion:
                    break;
                case FunctionType.FloatGetLinearProportion:
                    break;
                case FunctionType.FloatLinearInterpolateTimed:
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Time", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FloatLinearInterpolateSpeed:
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FloatLinearInterpolateSpeedAdvanced:
                    entity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Min_Value", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Max_Value", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FloatSmoothStep:
                    break;
                case FunctionType.FloatClamp:
                    break;
                case FunctionType.FilterAbsorber:
                    entity.AddParameter("factor", new cFloat(0.95f), ParameterVariant.PARAMETER);
                    entity.AddParameter("start_value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("input", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.IntegerMath_All:
                    break;
                case FunctionType.IntegerAdd_All:
                    break;
                case FunctionType.IntegerMultiply_All:
                    break;
                case FunctionType.IntegerMax_All:
                    break;
                case FunctionType.IntegerMin_All:
                    break;
                case FunctionType.IntegerMath:
                    break;
                case FunctionType.IntegerAdd:
                    break;
                case FunctionType.IntegerSubtract:
                    break;
                case FunctionType.IntegerMultiply:
                    break;
                case FunctionType.IntegerDivide:
                    break;
                case FunctionType.IntegerMax:
                    break;
                case FunctionType.IntegerMin:
                    break;
                case FunctionType.IntegerRemainder:
                    break;
                case FunctionType.IntegerAnd:
                    break;
                case FunctionType.IntegerOr:
                    break;
                case FunctionType.IntegerOperation:
                    break;
                case FunctionType.SetInteger:
                    break;
                case FunctionType.IntegerAbsolute:
                    break;
                case FunctionType.IntegerCompliment:
                    break;
                case FunctionType.IntegerCompare:
                    break;
                case FunctionType.IntegerEquals:
                    break;
                case FunctionType.IntegerNotEqual:
                    break;
                case FunctionType.IntegerGreaterThan:
                    break;
                case FunctionType.IntegerLessThan:
                    break;
                case FunctionType.IntegerGreaterThanOrEqual:
                    break;
                case FunctionType.IntegerLessThanOrEqual:
                    break;
                case FunctionType.IntegerAnalyse:
                    entity.AddParameter("Val0", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("Val1", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("Val2", new cInteger(2), ParameterVariant.PARAMETER);
                    entity.AddParameter("Val3", new cInteger(3), ParameterVariant.PARAMETER);
                    entity.AddParameter("Val4", new cInteger(4), ParameterVariant.PARAMETER);
                    entity.AddParameter("Val5", new cInteger(5), ParameterVariant.PARAMETER);
                    entity.AddParameter("Val6", new cInteger(6), ParameterVariant.PARAMETER);
                    entity.AddParameter("Val7", new cInteger(7), ParameterVariant.PARAMETER);
                    entity.AddParameter("Val8", new cInteger(8), ParameterVariant.PARAMETER);
                    entity.AddParameter("Val9", new cInteger(9), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetEnum:
                    entity.AddParameter("initial_value", new cEnum(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetString:
                    entity.AddParameter("initial_value", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetEnumString:
                    break;
                case FunctionType.VectorMath:
                    break;
                case FunctionType.VectorAdd:
                    break;
                case FunctionType.VectorSubtract:
                    break;
                case FunctionType.VectorProduct:
                    break;
                case FunctionType.VectorMultiply:
                    break;
                case FunctionType.VectorScale:
                    break;
                case FunctionType.VectorNormalise:
                    break;
                case FunctionType.VectorModulus:
                    break;
                case FunctionType.ScalarProduct:
                    break;
                case FunctionType.VectorDirection:
                    break;
                case FunctionType.VectorYaw:
                    break;
                case FunctionType.VectorRotateYaw:
                    break;
                case FunctionType.VectorRotateRoll:
                    break;
                case FunctionType.VectorRotatePitch:
                    break;
                case FunctionType.VectorRotateByPos:
                    break;
                case FunctionType.VectorMultiplyByPos:
                    break;
                case FunctionType.VectorDistance:
                    break;
                case FunctionType.VectorReflect:
                    break;
                case FunctionType.SetVector:
                    break;
                case FunctionType.SetVector2:
                    break;
                case FunctionType.SetColour:
                    break;
                case FunctionType.GetTranslation:
                    break;
                case FunctionType.GetRotation:
                    break;
                case FunctionType.GetComponentInterface:
                    break;
                case FunctionType.GetX:
                    break;
                case FunctionType.GetY:
                    break;
                case FunctionType.GetZ:
                    break;
                case FunctionType.SetPosition:
                    entity.AddParameter("set_on_reset", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PositionDistance:
                    break;
                case FunctionType.VectorLinearProportion:
                    break;
                case FunctionType.VectorLinearInterpolateTimed:
                    entity.AddParameter("Time", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VectorLinearInterpolateSpeed:
                    entity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MoveInTime:
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SmoothMove:
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RotateInTime:
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("time_X", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("time_Y", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("time_Z", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RotateAtSpeed:
                    entity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("speed_X", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("speed_Y", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("speed_Z", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PointAt:
                    break;
                case FunctionType.SetLocationAndOrientation:
                    entity.AddParameter("axis_is", new cEnum(EnumType.ORIENTATION_AXIS, 2), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ApplyRelativeTransform:
                    entity.AddParameter("use_trigger_entity", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RandomFloat:
                    entity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RandomInt:
                    entity.AddParameter("Min", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("Max", new cInteger(100), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RandomBool:
                    break;
                case FunctionType.RandomVector:
                    entity.AddParameter("MinX", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MaxX", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MinY", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MaxY", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MinZ", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MaxZ", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Normalised", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RandomSelect:
                    entity.AddParameter("Seed", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerRandom:
                    entity.AddParameter("Num", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerRandomSequence:
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Persistent_TriggerRandomSequence:
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerWeightedRandom:
                    entity.AddParameter("Weighting_01", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Weighting_02", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Weighting_03", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Weighting_04", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Weighting_05", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Weighting_06", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Weighting_07", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Weighting_08", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Weighting_09", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Weighting_10", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("allow_same_pin_in_succession", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayEnvironmentAnimation:
                    entity.AddParameter("animation_info", new cResource(), ParameterVariant.PARAMETER);
                    entity.AddParameter("AnimationSet", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("Animation", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("start_frame", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("end_frame", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("play_speed", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("shot_number", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.LevelLoaded:
                    break;
                case FunctionType.CAGEAnimation:
                    entity.AddParameter("use_external_time", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("rewind_on_stop", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("jump_to_the_end", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("playspeed", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("anim_length", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_cinematic_skippable", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("skippable_timer", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("capture_video", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("capture_clip_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MultitrackLoop:
                    entity.AddParameter("start_time", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("end_time", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ReTransformer:
                    break;
                case FunctionType.TriggerSequence:
                    entity.AddParameter("trigger_mode", new cEnum(EnumType.ANIM_MODE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("random_seed", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_random_intervals", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("no_duplicates", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("interval_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Checkpoint:
                    entity.AddParameter("is_first_checkpoint", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_first_autorun_checkpoint", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("section", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("checkpoint_type", new cEnum(EnumType.CHECKPOINT_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterIsAnySaveInProgress:
                    break;
                case FunctionType.SaveGlobalProgression:
                    break;
                case FunctionType.MissionNumber:
                    break;
                case FunctionType.SetAsActiveMissionLevel:
                    entity.AddParameter("clear_level", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CheckpointRestoredNotify:
                    break;
                case FunctionType.DebugLoadCheckpoint:
                    entity.AddParameter("previous_checkpoint", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GameStateChanged:
                    entity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DisplayMessage:
                    entity.AddParameter("title_id", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("message_id", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DisplayMessageWithCallbacks:
                    entity.AddParameter("title_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("message_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("yes_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("no_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("cancel_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("yes_button", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("no_button", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("cancel_button", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayerCampaignDeaths:
                    break;
                case FunctionType.PlayerCampaignDeathsInARow:
                    break;
                case FunctionType.SaveManagers:
                    break;
                case FunctionType.LevelInfo:
                    entity.AddParameter("save_level_name_id", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.IsInstallComplete:
                    break;
                case FunctionType.DebugCheckpoint:
                    entity.AddParameter("section", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("level_reset", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Benchmark:
                    entity.AddParameter("benchmark_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("save_stats", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.EndGame:
                    break;
                case FunctionType.LeaveGame:
                    break;
                case FunctionType.DebugTextStacking:
                    entity.AddParameter("text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("namespace", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("size", new cInteger(20), ParameterVariant.PARAMETER);
                    entity.AddParameter("colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("ci_type", new cEnum(EnumType.CI_MESSAGE_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("needs_debug_opt_to_render", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DebugText:
                    entity.AddParameter("text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("namespace", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("size", new cInteger(20), ParameterVariant.PARAMETER);
                    entity.AddParameter("colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("alignment", new cEnum(EnumType.TEXT_ALIGNMENT, 4), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cFloat(5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("pause_game", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("cancel_pause_with_button_press", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("priority", new cInteger(100), ParameterVariant.PARAMETER);
                    entity.AddParameter("ci_type", new cEnum(EnumType.CI_MESSAGE_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TutorialMessage:
                    entity.AddParameter("text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("text_list", new cString("A1_G0000_RIP_0010A"), ParameterVariant.PARAMETER);
                    entity.AddParameter("show_animation", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DebugEnvironmentMarker:
                    entity.AddParameter("text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("namespace", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("size", new cFloat(20f), ParameterVariant.PARAMETER);
                    entity.AddParameter("colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cFloat(5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("scale_with_distance", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_string_length", new cInteger(10), ParameterVariant.PARAMETER);
                    entity.AddParameter("scroll_speed", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("show_distance_from_target", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DebugPositionMarker:
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DebugCaptureScreenShot:
                    entity.AddParameter("wait_for_streamer", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("capture_filename", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("fov", new cFloat(45f), ParameterVariant.PARAMETER);
                    entity.AddParameter("near", new cFloat(0.01f), ParameterVariant.PARAMETER);
                    entity.AddParameter("far", new cFloat(200f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DebugCaptureCorpse:
                    entity.AddParameter("corpse_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DebugMenuToggle:
                    entity.AddParameter("debug_variable", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("value", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TogglePlayerTorch:
                    break;
                case FunctionType.PlayerTorch:
                    break;
                case FunctionType.Master:
                    entity.AddParameter("disable_display", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("disable_collision", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("disable_simulation", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ExclusiveMaster:
                    break;
                case FunctionType.ThinkOnce:
                    entity.AddParameter("use_random_start", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("random_start_delay", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Thinker:
                    entity.AddParameter("delay_between_triggers", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_continuous", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_random_start", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("random_start_delay", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("total_thinking_time", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AllPlayersReady:
                    entity.AddParameter("activation_delay", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SyncOnAllPlayers:
                    break;
                case FunctionType.SyncOnFirstPlayer:
                    break;
                case FunctionType.ParticipatingPlayersList:
                    break;
                case FunctionType.NetPlayerCounter:
                    break;
                case FunctionType.BroadcastTrigger:
                    break;
                case FunctionType.HostOnlyTrigger:
                    break;
                case FunctionType.SpawnGroup:
                    break;
                case FunctionType.RespawnExcluder:
                    break;
                case FunctionType.RespawnConfig:
                    entity.AddParameter("min_dist", new cFloat(2.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("preferred_dist", new cFloat(4.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_dist", new cFloat(30.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("respawn_mode", new cEnum(EnumType.RESPAWN_MODE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("respawn_wait_time", new cInteger(10), ParameterVariant.PARAMETER);
                    entity.AddParameter("uncollidable_time", new cInteger(5), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_default", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NumConnectedPlayers:
                    break;
                case FunctionType.NumPlayersOnStart:
                    break;
                case FunctionType.NumDeadPlayers:
                    break;
                case FunctionType.NetworkedTimer:
                    entity.AddParameter("duration", new cFloat(5.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DebugObjectMarker:
                    entity.AddParameter("marked_object", new cResource(), ParameterVariant.PARAMETER);
                    entity.AddParameter("marked_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.EggSpawner:
                    entity.AddParameter("egg_position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("hostile_egg", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RandomObjectSelector:
                    break;
                case FunctionType.IsMultiplayerMode:
                    break;
                case FunctionType.CompoundVolume:
                    break;
                case FunctionType.TriggerVolumeFilter:
                    break;
                case FunctionType.TriggerVolumeFilter_Monitored:
                    break;
                case FunctionType.TriggerFilter:
                    break;
                case FunctionType.TriggerObjectsFilter:
                    break;
                case FunctionType.BindObjectsMultiplexer:
                    break;
                case FunctionType.TriggerObjectsFilterCounter:
                    entity.AddParameter("filter", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerContainerObjectsFilterCounter:
                    entity.AddParameter("container", new cResource(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerTouch:
                    break;
                case FunctionType.TriggerDamaged:
                    entity.AddParameter("threshold", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerBindCharacter:
                    break;
                case FunctionType.TriggerBindAllCharactersOfType:
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 2), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerBindCharactersInSquad:
                    break;
                case FunctionType.TriggerUnbindCharacter:
                    break;
                case FunctionType.TriggerExtractBoundObject:
                    break;
                case FunctionType.TriggerExtractBoundCharacter:
                    break;
                case FunctionType.TriggerDelay:
                    entity.AddParameter("Hrs", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Min", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Sec", new cFloat(1f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerSwitch:
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("loop", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerSelect:
                    entity.AddParameter("index", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerSelect_Direct:
                    entity.AddParameter("Changes_only", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerCheckDifficulty:
                    entity.AddParameter("DifficultyLevel", new cEnum(EnumType.DIFFICULTY_SETTING_TYPE, 2), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerSync:
                    entity.AddParameter("reset_on_trigger", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.LogicAll:
                    entity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("reset_on_trigger", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Logic_MultiGate:
                    entity.AddParameter("trigger_pin", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Counter:
                    entity.AddParameter("is_limitless", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_limit", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.LogicCounter:
                    entity.AddParameter("is_limitless", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_limit", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("non_persistent", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.LogicPressurePad:
                    break;
                case FunctionType.SetObject:
                    break;
                case FunctionType.GateResourceInterface:
                    break;
                case FunctionType.Door:
                    entity.AddParameter("unlocked_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("locked_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("icon_keyframe", new cEnum(EnumType.UI_ICON_ICON, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("detach_anim", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("invert_nav_mesh_barrier", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MonitorPadInput:
                    break;
                case FunctionType.MonitorActionMap:
                    break;
                case FunctionType.InhibitActionsUntilRelease:
                    break;
                case FunctionType.PadLightBar:
                    entity.AddParameter("colour", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PadRumbleImpulse:
                    entity.AddParameter("low_frequency_rumble", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("high_frequency_rumble", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("left_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("right_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("aim_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("shoot_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerViewCone:
                    entity.AddParameter("target_offset", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("visible_area_horizontal", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("visible_area_vertical", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerCameraViewCone:
                    entity.AddParameter("use_camera_fov", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("target_offset", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("visible_area_horizontal", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("visible_area_vertical", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerCameraViewConeMulti:
                    entity.AddParameter("number_of_inputs", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_camera_fov", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("visible_area_horizontal", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("visible_area_vertical", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerCameraVolume:
                    entity.AddParameter("start_radius", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_Debug_Menu_Item:
                    break;
                case FunctionType.Character:
                    entity.AddParameter("PopToNavMesh", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("disable_dead_container", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("allow_container_without_death", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("container_interaction_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("anim_set", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("anim_tree_set", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("attribute_set", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_player", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_backstage", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("force_backstage_on_respawn", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS, 3), ParameterVariant.PARAMETER);
                    entity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("dialogue_voice", new cEnum(EnumType.DIALOGUE_VOICE_ACTOR, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("spawn_id", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("display_model", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("reference_skeleton", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("torso_sound", new cString("Shirt"), ParameterVariant.PARAMETER);
                    entity.AddParameter("leg_sound", new cString("Jeans"), ParameterVariant.PARAMETER);
                    entity.AddParameter("footwear_sound", new cString("Flats"), ParameterVariant.PARAMETER);
                    entity.AddParameter("custom_character_type", new cEnum(EnumType.CUSTOM_CHARACTER_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("custom_character_accessory_override", new cEnum(EnumType.CUSTOM_CHARACTER_ACCESSORY_OVERRIDE, -1), ParameterVariant.PARAMETER);
                    entity.AddParameter("custom_character_population_type", new cEnum(EnumType.CUSTOM_CHARACTER_POPULATION, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("named_custom_character", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("named_custom_character_assets_set", new cEnum(EnumType.CUSTOM_CHARACTER_ASSETS, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("gcip_distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("inventory_set", new cEnum(EnumType.PLAYER_INVENTORY_SET, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RegisterCharacterModel:
                    entity.AddParameter("display_model", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("reference_skeleton", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DespawnPlayer:
                    break;
                case FunctionType.DespawnCharacter:
                    break;
                case FunctionType.FilterAnd:
                    break;
                case FunctionType.FilterOr:
                    break;
                case FunctionType.FilterNot:
                    break;
                case FunctionType.FilterIsAPlayer:
                    break;
                case FunctionType.FilterIsLocalPlayer:
                    break;
                case FunctionType.FilterIsEnemyOfPlayer:
                    break;
                case FunctionType.FilterIsEnemyOfCharacter:
                    entity.AddParameter("use_alliance_at_death", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterIsNotDeadManWalking:
                    break;
                case FunctionType.FilterIsEnemyOfAllianceGroup:
                    entity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterIsPhysics:
                    break;
                case FunctionType.FilterIsPhysicsObject:
                    break;
                case FunctionType.FilterIsObject:
                    break;
                case FunctionType.FilterIsCharacter:
                    break;
                case FunctionType.FilterIsACharacter:
                    break;
                case FunctionType.FilterIsWithdrawnAlien:
                    break;
                case FunctionType.FilterIsFacingTarget:
                    entity.AddParameter("tolerance", new cFloat(45f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterIsDead:
                    break;
                case FunctionType.FilterBelongsToAlliance:
                    entity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterHasWeaponOfType:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterHasWeaponEquipped:
                    entity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterIsinInventory:
                    break;
                case FunctionType.FilterIsCharacterClass:
                    entity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS, 3), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterIsHumanNPC:
                    break;
                case FunctionType.FilterIsCharacterClassCombo:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterIsInAlertnessState:
                    entity.AddParameter("AlertState", new cEnum(EnumType.ALERTNESS_STATE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterIsInLocomotionState:
                    entity.AddParameter("State", new cEnum(EnumType.LOCOMOTION_STATE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterCanSeeTarget:
                    break;
                case FunctionType.FilterIsInAGroup:
                    break;
                case FunctionType.FilterIsAgressing:
                    break;
                case FunctionType.FilterIsValidInventoryItem:
                    break;
                case FunctionType.FilterIsInWeaponRange:
                    break;
                case FunctionType.TriggerWhenSeeTarget:
                    break;
                case FunctionType.FilterIsPlatform:
                    entity.AddParameter("Platform", new cEnum(EnumType.PLATFORM_TYPE, 5), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterIsUsingDevice:
                    entity.AddParameter("Device", new cEnum(EnumType.INPUT_DEVICE_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterSmallestUsedDifficulty:
                    entity.AddParameter("difficulty", new cEnum(EnumType.DIFFICULTY_SETTING_TYPE, 2), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterHasPlayerCollectedIdTag:
                    entity.AddParameter("tag_id", new cString("IDTAGABC"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FilterHasBehaviourTreeFlagSet:
                    entity.AddParameter("BehaviourTreeFlag", new cEnum(EnumType.BEHAVIOUR_TREE_FLAGS, 2), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Job:
                    break;
                case FunctionType.JobWithPosition:
                    break;
                case FunctionType.JOB_Idle:
                    entity.AddParameter("task_operation_mode", new cEnum(EnumType.TASK_OPERATION_MODE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("should_perform_all_tasks", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.JOB_AreaSweep:
                    break;
                case FunctionType.JOB_AreaSweepFlare:
                    break;
                case FunctionType.JOB_SystematicSearch:
                    break;
                case FunctionType.JOB_SystematicSearchFlare:
                    break;
                case FunctionType.JOB_SpottingPosition:
                    break;
                case FunctionType.JOB_Follow:
                    break;
                case FunctionType.JOB_Follow_Centre:
                    break;
                case FunctionType.Internal_JOB_SearchTarget:
                    break;
                case FunctionType.JOB_Assault:
                    break;
                case FunctionType.JOB_Panic:
                    break;
                case FunctionType.Task:
                    entity.AddParameter("should_stop_moving_when_reached", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("should_orientate_when_reached", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("reached_distance_threshold", new cFloat(0.6f), ParameterVariant.PARAMETER);
                    entity.AddParameter("selection_priority", new cEnum(EnumType.TASK_PRIORITY, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("timeout", new cFloat(5.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("always_on_tracker", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FlareTask:
                    entity.AddParameter("filter_options", new cEnum(EnumType.TASK_CHARACTER_CLASS_FILTER, 1024), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.IdleTask:
                    entity.AddParameter("should_auto_move_to_position", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("ignored_for_auto_selection", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("has_pre_move_script", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("has_interrupt_script", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("filter_options", new cEnum(EnumType.TASK_CHARACTER_CLASS_FILTER, 1024), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FollowTask:
                    entity.AddParameter("can_initially_end_early", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("stop_radius", new cFloat(0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_ForceNextJob:
                    entity.AddParameter("ShouldInterruptCurrentTask", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Job", new cResource(), ParameterVariant.PARAMETER);
                    entity.AddParameter("InitialTask", new cResource(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetRateOfFire:
                    entity.AddParameter("MinTimeBetweenShots", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("RandomRange", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetFiringRhythm:
                    entity.AddParameter("MinShootingTime", new cFloat(3.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("RandomRangeShootingTime", new cFloat(2.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MinNonShootingTime", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("RandomRangeNonShootingTime", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MinCoverNonShootingTime", new cFloat(3.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("RandomRangeCoverNonShootingTime", new cFloat(2.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_SetFiringAccuracy:
                    entity.AddParameter("Accuracy", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_ResetFiringStats:
                    break;
                case FunctionType.TriggerBindAllNPCs:
                    entity.AddParameter("radius", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Trigger_AudioOccluded:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("Range", new cFloat(30.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SwitchLevel:
                    entity.AddParameter("level_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundPlaybackBaseClass:
                    entity.AddParameter("sound_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("argument_1", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("argument_2", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("argument_3", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("argument_4", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("argument_5", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("namespace", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("object_position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("restore_on_checkpoint", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundObject:
                    break;
                case FunctionType.Sound:
                    entity.AddParameter("stop_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_static_ambience", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("start_on", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("multi_trigger", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_multi_emitter", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("create_sound_object", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("switch_name", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("switch_value", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("last_gen_enabled", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("resume_after_suspended", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Speech:
                    entity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 2), ParameterVariant.PARAMETER);
                    entity.AddParameter("queue_time", new cFloat(4f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NPC_DynamicDialogue:
                    break;
                case FunctionType.NPC_DynamicDialogueGlobalRange:
                    entity.AddParameter("dialogue_range", new cFloat(35f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CHR_PlayNPCBark:
                    entity.AddParameter("queue_time", new cFloat(4f), ParameterVariant.PARAMETER);
                    entity.AddParameter("sound_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("dialogue_mode", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("action", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SpeechScript:
                    entity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 2), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_01_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_01_character", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_02_delay", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_02_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_02_character", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_03_delay", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_03_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_03_character", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_04_delay", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_04_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_04_character", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_05_delay", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_05_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_05_character", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_06_delay", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_06_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_06_character", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_07_delay", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_07_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_07_character", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_08_delay", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_08_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_08_character", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_09_delay", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_09_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_09_character", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_10_delay", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_10_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("line_10_character", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("restore_on_checkpoint", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundSpline:
                    break;
                case FunctionType.SoundNetworkNode:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundEnvironmentMarker:
                    entity.AddParameter("reverb_name", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("on_enter_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("on_exit_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("linked_network_occlusion_scaler", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("room_size", new cString("Medium_Room"), ParameterVariant.PARAMETER);
                    entity.AddParameter("disable_network_creation", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundEnvironmentZone:
                    entity.AddParameter("reverb_name", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("priority", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundLoadBank:
                    entity.AddParameter("trigger_via_pin", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("memory_pool", new cEnum(EnumType.SOUND_POOL, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundLoadSlot:
                    entity.AddParameter("sound_bank", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("memory_pool", new cEnum(EnumType.SOUND_POOL, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundSetRTPC:
                    entity.AddParameter("rtpc_name", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("smooth_rate", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("start_on", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundSetState:
                    entity.AddParameter("state_name", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("state_value", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundSetSwitch:
                    entity.AddParameter("switch_name", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("switch_value", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundImpact:
                    entity.AddParameter("sound_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("argument_1", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("argument_2", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("argument_3", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundBarrier:
                    entity.AddParameter("default_open", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("band_aid", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("override_value", new cFloat(0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MusicController:
                    entity.AddParameter("music_start_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("music_end_event", new cString("MusicController_Music_End"), ParameterVariant.PARAMETER);
                    entity.AddParameter("music_restart_event", new cString("MusicController_Music_Restart"), ParameterVariant.PARAMETER);
                    entity.AddParameter("layer_control_rtpc", new cString("Music_All_Layers"), ParameterVariant.PARAMETER);
                    entity.AddParameter("smooth_rate", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("alien_max_distance", new cFloat(50f), ParameterVariant.PARAMETER);
                    entity.AddParameter("object_max_distance", new cFloat(50f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MusicTrigger:
                    entity.AddParameter("music_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("smooth_rate", new cFloat(-1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("queue_time", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("interrupt_all", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_once", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("rtpc_set_mode", new cEnum(EnumType.MUSIC_RTPC_MODE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("rtpc_target_value", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("rtpc_duration", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("rtpc_set_return_mode", new cEnum(EnumType.MUSIC_RTPC_MODE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("rtpc_return_value", new cFloat(0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundLevelInitialiser:
                    entity.AddParameter("auto_generate_networks", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("network_node_min_spacing", new cFloat(2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("network_node_max_visibility", new cFloat(10f), ParameterVariant.PARAMETER);
                    entity.AddParameter("network_node_ceiling_height", new cFloat(50f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundMissionInitialiser:
                    entity.AddParameter("human_max_threat", new cFloat(0.7f), ParameterVariant.PARAMETER);
                    entity.AddParameter("android_max_threat", new cFloat(0.8f), ParameterVariant.PARAMETER);
                    entity.AddParameter("alien_max_threat", new cFloat(1f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundRTPCController:
                    entity.AddParameter("stealth_default_on", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("threat_default_on", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundTimelineTrigger:
                    entity.AddParameter("sound_event", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("trigger_time", new cFloat(0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundPhysicsInitialiser:
                    entity.AddParameter("contact_max_timeout", new cFloat(0.33f), ParameterVariant.PARAMETER);
                    entity.AddParameter("contact_smoothing_attack_rate", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("contact_smoothing_decay_rate", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("contact_min_magnitude", new cFloat(0.01f), ParameterVariant.PARAMETER);
                    entity.AddParameter("contact_max_trigger_distance", new cFloat(25f), ParameterVariant.PARAMETER);
                    entity.AddParameter("impact_min_speed", new cFloat(0.2f), ParameterVariant.PARAMETER);
                    entity.AddParameter("impact_max_trigger_distance", new cFloat(10f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ragdoll_min_timeout", new cFloat(0.25f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ragdoll_min_speed", new cFloat(1f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SoundPlayerFootwearOverride:
                    entity.AddParameter("footwear_sound", new cString("Trainers"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AddToInventory:
                    break;
                case FunctionType.RemoveFromInventory:
                    break;
                case FunctionType.LimitItemUse:
                    break;
                case FunctionType.PlayerHasItem:
                    break;
                case FunctionType.PlayerHasItemWithName:
                    break;
                case FunctionType.PlayerHasItemEntity:
                    break;
                case FunctionType.PlayerHasEnoughItems:
                    entity.AddParameter("quantity", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayerHasSpaceForItem:
                    break;
                case FunctionType.InventoryItem:
                    entity.AddParameter("item", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("quantity", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("clear_on_collect", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("gcip_instances_count", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetInventoryItemName:
                    break;
                case FunctionType.PickupSpawner:
                    entity.AddParameter("item_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MultiplePickupSpawner:
                    entity.AddParameter("item_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AddItemsToGCPool:
                    break;
                case FunctionType.SetupGCDistribution:
                    entity.AddParameter("c00", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("c01", new cFloat(0.969f), ParameterVariant.PARAMETER);
                    entity.AddParameter("c02", new cFloat(0.882f), ParameterVariant.PARAMETER);
                    entity.AddParameter("c03", new cFloat(0.754f), ParameterVariant.PARAMETER);
                    entity.AddParameter("c04", new cFloat(0.606f), ParameterVariant.PARAMETER);
                    entity.AddParameter("c05", new cFloat(0.457f), ParameterVariant.PARAMETER);
                    entity.AddParameter("c06", new cFloat(0.324f), ParameterVariant.PARAMETER);
                    entity.AddParameter("c07", new cFloat(0.216f), ParameterVariant.PARAMETER);
                    entity.AddParameter("c08", new cFloat(0.135f), ParameterVariant.PARAMETER);
                    entity.AddParameter("c09", new cFloat(0.079f), ParameterVariant.PARAMETER);
                    entity.AddParameter("c10", new cFloat(0.043f), ParameterVariant.PARAMETER);
                    entity.AddParameter("minimum_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("divisor", new cFloat(20.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("lookup_decrease_time", new cFloat(15.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("lookup_point_increase", new cInteger(2), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AllocateGCItemsFromPool:
                    entity.AddParameter("force_usage_count", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AllocateGCItemFromPoolBySubset:
                    entity.AddParameter("force_usage", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.QueryGCItemPool:
                    entity.AddParameter("item_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RemoveFromGCItemPool:
                    entity.AddParameter("item_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("gcip_instances_to_remove", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FlashScript:
                    entity.AddParameter("filename", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("layer_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("target_texture_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("type", new cEnum(EnumType.FLASH_SCRIPT_RENDER_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UI_KeyGate:
                    entity.AddParameter("code", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("carduid", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("key_type", new cEnum(EnumType.UI_KEYGATE_TYPE, 1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RTT_MoviePlayer:
                    entity.AddParameter("filename", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("layer_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("target_texture_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MoviePlayer:
                    entity.AddParameter("trigger_end_on_skipped", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("filename", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("skippable", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("enable_debug_skip", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DurangoVideoCapture:
                    entity.AddParameter("clip_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.VideoCapture:
                    entity.AddParameter("clip_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("only_in_capture_mode", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ToggleFunctionality:
                    break;
                case FunctionType.FlashInvoke:
                    entity.AddParameter("method", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("invoke_type", new cEnum(EnumType.FLASH_INVOKE_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("int_argument_0", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("int_argument_1", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("int_argument_2", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("int_argument_3", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("float_argument_0", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("float_argument_1", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("float_argument_2", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("float_argument_3", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MotionTrackerPing:
                    break;
                case FunctionType.CHR_SetShowInMotionTracker:
                    break;
                case FunctionType.EnableMotionTrackerPassiveAudio:
                    break;
                case FunctionType.FlashCallback:
                    entity.AddParameter("callback_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PopupMessage:
                    entity.AddParameter("header_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("main_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cFloat(5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("sound_event", new cEnum(EnumType.POPUP_MESSAGE_SOUND, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("icon_keyframe", new cEnum(EnumType.POPUP_MESSAGE_ICON, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UIBreathingGameIcon:
                    entity.AddParameter("fill_percentage", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("prompt_text", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GenericHighlightEntity:
                    break;
                case FunctionType.UI_Icon:
                    entity.AddParameter("unlocked_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("locked_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("action_text", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("icon_keyframe", new cEnum(EnumType.UI_ICON_ICON, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("can_be_used", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("category", new cEnum(EnumType.PICKUP_CATEGORY, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("push_hold_time", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UI_Attached:
                    break;
                case FunctionType.UI_Container:
                    entity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_temporary", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UI_ReactionGame:
                    entity.AddParameter("exit_on_fail", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UI_Keypad:
                    entity.AddParameter("code", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("exit_on_fail", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.HackingGame:
                    entity.AddParameter("hacking_difficulty", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("auto_exit", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetHackingToolLevel:
                    entity.AddParameter("level", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TerminalContent:
                    entity.AddParameter("content_title", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("content_decoration_title", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("additional_info", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_connected_to_audio_log", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_triggerable", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_single_use", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TerminalFolder:
                    entity.AddParameter("code", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("folder_title", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("folder_lock_type", new cEnum(EnumType.FOLDER_LOCK_TYPE, 1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AccessTerminal:
                    entity.AddParameter("location", new cEnum(EnumType.TERMINAL_LOCATION, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetGatingToolLevel:
                    entity.AddParameter("level", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetGatingToolLevel:
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetPlayerHasGatingTool:
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetPlayerHasKeycard:
                    entity.AddParameter("card_uid", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetPlayerHasKeycard:
                    entity.AddParameter("card_uid", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetPlayerHasGatingTool:
                    entity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CollectSevastopolLog:
                    entity.AddParameter("log_id", new cString("SEV001"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CollectNostromoLog:
                    entity.AddParameter("log_id", new cString("NOS001"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CollectIDTag:
                    entity.AddParameter("tag_id", new cString("IDTAGABC"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.StartNewChapter:
                    entity.AddParameter("chapter", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UnlockLogEntry:
                    entity.AddParameter("entry", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MapAnchor:
                    entity.AddParameter("keyframe", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("keyframe1", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("keyframe2", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("keyframe3", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("keyframe4", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("keyframe5", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_default_for_items", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MapItem:
                    entity.AddParameter("item_type", new cEnum(EnumType.MAP_ICON_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("map_keyframe", new cString("RnD_HzdLab_1"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UnlockMapDetail:
                    entity.AddParameter("map_keyframe", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("details", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RewireSystem:
                    entity.AddParameter("display_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("display_name_enum", new cEnum(EnumType.REWIRE_SYSTEM_NAME, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("on_by_default", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("running_cost", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("system_type", new cEnum(EnumType.REWIRE_SYSTEM_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("map_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("element_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RewireLocation:
                    entity.AddParameter("element_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("display_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RewireAccess_Point:
                    entity.AddParameter("additional_power", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("display_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("map_element_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("map_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("map_x_offset", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("map_y_offset", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("map_zoom", new cFloat(3.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RewireTotalPowerResource:
                    entity.AddParameter("total_power", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Rewire:
                    entity.AddParameter("map_keyframe", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("total_power", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetMotionTrackerRange:
                    entity.AddParameter("range", new cFloat(20f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetGamepadAxes:
                    entity.AddParameter("invert_x", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("invert_y", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("save_settings", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetGameplayTips:
                    entity.AddParameter("tip_string_id", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GameOver:
                    entity.AddParameter("tip_string_id", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("default_tips_enabled", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("level_tips_enabled", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GameplayTip:
                    entity.AddParameter("string_id", new cString("AI_UI_GAMEOVER_CUSTOM_TIP_DEFAULT"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Minigames:
                    entity.AddParameter("game_inertial_damping_active", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("game_green_text_active", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("game_yellow_chart_active", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("game_overloc_fail_active", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("game_docking_active", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("game_environ_ctr_active", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("config_pass_number", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("config_fail_limit", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("config_difficulty", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetBlueprintInfo:
                    entity.AddParameter("type", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("level", new cEnum(EnumType.BLUEPRINT_LEVEL, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("available", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetBlueprintLevel:
                    entity.AddParameter("type", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetBlueprintAvailable:
                    entity.AddParameter("type", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetSelectedCharacterId:
                    break;
                case FunctionType.GetNextPlaylistLevelName:
                    break;
                case FunctionType.IsPlaylistTypeSingle:
                    break;
                case FunctionType.IsPlaylistTypeAll:
                    break;
                case FunctionType.IsPlaylistTypeMarathon:
                    break;
                case FunctionType.IsCurrentLevelAChallengeMap:
                    break;
                case FunctionType.IsCurrentLevelAPreorderMap:
                    break;
                case FunctionType.GetCurrentPlaylistLevelIndex:
                    break;
                case FunctionType.SetObjectiveCompleted:
                    entity.AddParameter("objective_id", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GameOverCredits:
                    break;
                case FunctionType.GoToFrontend:
                    entity.AddParameter("frontend_state", new cEnum(EnumType.FRONTEND_STATE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TriggerLooper:
                    entity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("delay", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CoverLine:
                    break;
                case FunctionType.TRAV_ContinuousLadder:
                    entity.AddParameter("RungSpacing", new cFloat(0.33f), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_ContinuousPipe:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_ContinuousLedge:
                    entity.AddParameter("Dangling", new cEnum(EnumType.AUTODETECT, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("Sidling", new cEnum(EnumType.AUTODETECT, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_ContinuousClimbingWall:
                    entity.AddParameter("Dangling", new cEnum(EnumType.AUTODETECT, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_ContinuousCinematicSidle:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_ContinuousBalanceBeam:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_ContinuousTightGap:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_1ShotVentEntrance:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_1ShotVentExit:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_1ShotFloorVentExit:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_1ShotClimbUnder:
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_1ShotLeap:
                    entity.AddParameter("MissDistance", new cFloat(2.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("NearMissDistance", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.TRAV_1ShotSpline:
                    entity.AddParameter("template", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("headroom", new cFloat(1.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("extra_cost", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("fit_end_to_edge", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("min_speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 3), ParameterVariant.PARAMETER);
                    entity.AddParameter("animationTree", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NavMeshBarrier:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("opaque", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("allowed_character_classes_when_open", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    entity.AddParameter("allowed_character_classes_when_closed", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NavMeshWalkablePlatform:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NavMeshExclusionArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NavMeshArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("area_type", new cEnum(EnumType.NAV_MESH_AREA_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NavMeshReachabilitySeedPoint:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CoverExclusionArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("exclude_cover", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("exclude_vaults", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("exclude_mantles", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("exclude_jump_downs", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("exclude_crawl_space_spotting_positions", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("exclude_spotting_positions", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("exclude_assault_positions", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SpottingExclusionArea:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("half_dimensions", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PathfindingTeleportNode:
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("extra_cost", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PathfindingWaitNode:
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("extra_cost", new cFloat(100.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 733), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PathfindingManualNode:
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("extra_cost", new cFloat(100.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PathfindingAlienBackstageNode:
                    entity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("top", new cTransform(), ParameterVariant.PARAMETER);
                    entity.AddParameter("extra_cost", new cFloat(100.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("network_id", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ChokePoint:
                    break;
                case FunctionType.NPC_SetChokePoint:
                    break;
                case FunctionType.Planet:
                    entity.AddParameter("parallax_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("planet_sort_key", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("overbright_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("light_wrap_angle_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("penumbra_falloff_power_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("lens_flare_brightness", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("lens_flare_colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("atmosphere_edge_falloff_power", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("atmosphere_edge_transparency", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("atmosphere_scroll_speed", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("atmosphere_detail_scroll_speed", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("override_global_tint", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("global_tint", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("flow_cycle_time", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("flow_speed", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("flow_tex_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("flow_warp_strength", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("detail_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("normal_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("terrain_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("atmosphere_normal_strength", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("terrain_normal_strength", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("light_shaft_colour", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("light_shaft_range", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("light_shaft_decay", new cFloat(0.8f), ParameterVariant.PARAMETER);
                    entity.AddParameter("light_shaft_min_occlusion_distance", new cFloat(100f), ParameterVariant.PARAMETER);
                    entity.AddParameter("light_shaft_intensity", new cFloat(0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("light_shaft_density", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("light_shaft_source_occlusion", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("blocks_light_shafts", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SpaceTransform:
                    entity.AddParameter("yaw_speed", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("pitch_speed", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("roll_speed", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SpaceSuitVisor:
                    entity.AddParameter("breath_level", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.NonInteractiveWater:
                    entity.AddParameter("SCALE_X", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SCALE_Z", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SHININESS", new cFloat(0.8f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CYCLE_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FRESNEL_POWER", new cFloat(0.8f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MIN_FRESNEL", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MAX_FRESNEL", new cFloat(5.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVIRONMENT_MAP_MULT", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVMAP_SIZE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ENVMAP_BOXPROJ_BB_SCALE", new cVector3(new Vector3(1, 1, 1)), ParameterVariant.PARAMETER);
                    entity.AddParameter("REFLECTION_PERTURBATION_STRENGTH", new cFloat(0.05f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHA_PERTURBATION_STRENGTH", new cFloat(0.05f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ALPHALIGHT_MULT", new cFloat(0.4f), ParameterVariant.PARAMETER);
                    entity.AddParameter("softness_edge", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_INITIAL_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_ALPHA", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_MIDPOINT_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_END_COLOUR", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_END_ALPHA", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DEPTH_FOG_END_DEPTH", new cFloat(2.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Refraction:
                    entity.AddParameter("SCALE_X", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SCALE_Z", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DISTANCEFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("MIN_OCCLUSION_DISTANCE", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("CYCLE_TIME", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FogPlane:
                    entity.AddParameter("start_distance_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("distance_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("angle_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("linear_height_density_fresnel_power_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("linear_heigth_density_max_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("tint", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("thickness_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("edge_softness_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("diffuse_0_uv_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("diffuse_0_speed_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("diffuse_1_uv_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("diffuse_1_speed_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PostprocessingSettings:
                    entity.AddParameter("priority", new cInteger(100), ParameterVariant.PARAMETER);
                    entity.AddParameter("blend_mode", new cEnum(EnumType.BLEND_MODE, 2), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.BloomSettings:
                    break;
                case FunctionType.ColourSettings:
                    break;
                case FunctionType.FlareSettings:
                    break;
                case FunctionType.HighSpecMotionBlurSettings:
                    break;
                case FunctionType.FilmGrainSettings:
                    break;
                case FunctionType.VignetteSettings:
                    break;
                case FunctionType.DistortionSettings:
                    break;
                case FunctionType.SharpnessSettings:
                    break;
                case FunctionType.LensDustSettings:
                    entity.AddParameter("DUST_MAX_REFLECTED_BLOOM_INTENSITY", new cFloat(0.02f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DUST_REFLECTED_BLOOM_INTENSITY_SCALAR", new cFloat(0.25f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DUST_MAX_BLOOM_INTENSITY", new cFloat(0.004f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DUST_BLOOM_INTENSITY_SCALAR", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("DUST_THRESHOLD", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.IrawanToneMappingSettings:
                    break;
                case FunctionType.HableToneMappingSettings:
                    break;
                case FunctionType.DayToneMappingSettings:
                    break;
                case FunctionType.LightAdaptationSettings:
                    entity.AddParameter("adaptation_mechanism", new cEnum(EnumType.LIGHT_ADAPTATION_MECHANISM, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ColourCorrectionTransition:
                    entity.AddParameter("colour_lut_a", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("colour_lut_b", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("lut_a_contribution", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("lut_b_contribution", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ProjectileMotion:
                    break;
                case FunctionType.ProjectileMotionComplex:
                    break;
                case FunctionType.SplineDistanceLerp:
                    break;
                case FunctionType.MoveAlongSpline:
                    break;
                case FunctionType.GetSplineLength:
                    break;
                case FunctionType.GetPointOnSpline:
                    break;
                case FunctionType.GetClosestPercentOnSpline:
                    entity.AddParameter("bidirectional", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetClosestPointOnSpline:
                    entity.AddParameter("look_ahead_distance", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("unidirectional", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("directional_damping_threshold", new cFloat(0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetClosestPoint:
                    break;
                case FunctionType.GetClosestPointFromSet:
                    break;
                case FunctionType.GetCentrePoint:
                    break;
                case FunctionType.FogSetting:
                    break;
                case FunctionType.FullScreenBlurSettings:
                    break;
                case FunctionType.DistortionOverlay:
                    entity.AddParameter("distortion_texture", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("alpha_threshold_enabled", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("threshold_texture", new cString(""), ParameterVariant.PARAMETER);
                    entity.AddParameter("range", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("begin_start_time", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("begin_stop_time", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("end_start_time", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("end_stop_time", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FullScreenOverlay:
                    entity.AddParameter("overlay_texture", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("threshold_value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("threshold_start", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("threshold_stop", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("threshold_range", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("alpha_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DepthOfFieldSettings:
                    entity.AddParameter("use_camera_target", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ChromaticAberrations:
                    entity.AddParameter("aberration_scalar", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ScreenFadeOutToBlack:
                    entity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ScreenFadeOutToBlackTimed:
                    entity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ScreenFadeOutToWhite:
                    entity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ScreenFadeOutToWhiteTimed:
                    entity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ScreenFadeIn:
                    entity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ScreenFadeInTimed:
                    entity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.LowResFrameCapture:
                    break;
                case FunctionType.BlendLowResFrame:
                    entity.AddParameter("blend_value", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CharacterMonitor:
                    break;
                case FunctionType.AreaHitMonitor:
                    break;
                case FunctionType.ENT_Debug_Exit_Game:
                    entity.AddParameter("FailureText", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("FailureCode", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.StreamingMonitor:
                    break;
                case FunctionType.Raycast:
                    entity.AddParameter("priority", new cEnum(EnumType.RAYCAST_PRIORITY, 2), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PhysicsApplyImpulse:
                    break;
                case FunctionType.PhysicsApplyVelocity:
                    break;
                case FunctionType.PhysicsModifyGravity:
                    break;
                case FunctionType.PhysicsApplyBuoyancy:
                    break;
                case FunctionType.AssetSpawner:
                    entity.AddParameter("spawn_on_load", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("allow_forced_despawn", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("persist_on_callback", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("allow_physics", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ProximityTrigger:
                    entity.AddParameter("fire_spread_rate", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("water_permeate_rate", new cFloat(10.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("electrical_conduction_rate", new cFloat(100.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("gas_diffusion_rate", new cFloat(0.1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("ignition_range", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("electrical_arc_range", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("water_flow_range", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("gas_dispersion_range", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.CharacterAttachmentNode:
                    entity.AddParameter("Node", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("AdditiveNode", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("AdditiveNodeIntensity", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("UseOffset", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Translation", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("Rotation", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.MultipleCharacterAttachmentNode:
                    entity.AddParameter("node", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_offset", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("translation", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("rotation", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AnimatedModelAttachmentNode:
                    entity.AddParameter("bone_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_offset", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("offset", new cTransform(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GetCharacterRotationSpeed:
                    break;
                case FunctionType.LevelCompletionTargets:
                    entity.AddParameter("TargetTime", new cFloat(-1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("NumDeaths", new cInteger(1), ParameterVariant.PARAMETER);
                    entity.AddParameter("TeamRespawnBonus", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("NoLocalRespawnBonus", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("NoRespawnBonus", new cInteger(-1), ParameterVariant.PARAMETER);
                    entity.AddParameter("GrappleBreakBonus", new cInteger(-1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.EnvironmentMap:
                    entity.AddParameter("Priority", new cInteger(100), ParameterVariant.PARAMETER);
                    entity.AddParameter("ColourFactor", new cVector3(new Vector3(255, 255, 255)), ParameterVariant.PARAMETER);
                    entity.AddParameter("EmissiveFactor", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("Texture", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Showlevel_Completed:
                    break;
                case FunctionType.Display_Element_On_Map:
                    entity.AddParameter("map_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("element_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Map_Floor_Change:
                    entity.AddParameter("floor_name", new cString("NULL"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Force_UI_Visibility:
                    break;
                case FunctionType.AddExitObjective:
                    entity.AddParameter("level_name", new cEnum(EnumType.EXIT_WAYPOINT, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetPrimaryObjective:
                    entity.AddParameter("title", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("additional_info", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("title_list", new cString("A1_G0000_RIP_0010A"), ParameterVariant.PARAMETER);
                    entity.AddParameter("additional_info_list", new cString("A1_G0000_RIP_0010A"), ParameterVariant.PARAMETER);
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetSubObjective:
                    entity.AddParameter("title", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("map_description", new cString("NULL"), ParameterVariant.PARAMETER);
                    entity.AddParameter("title_list", new cString("A1_G0000_RIP_0010A"), ParameterVariant.PARAMETER);
                    entity.AddParameter("map_description_list", new cString("A1_G0000_RIP_0010A"), ParameterVariant.PARAMETER);
                    entity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("objective_type", new cEnum(EnumType.SUB_OBJECTIVE_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ClearPrimaryObjective:
                    entity.AddParameter("clear_all_sub_objectives", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ClearSubObjective:
                    entity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UpdatePrimaryObjective:
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("clear_objective", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UpdateSubObjective:
                    entity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("clear_objective", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DebugGraph:
                    entity.AddParameter("scale", new cFloat(1f), ParameterVariant.PARAMETER);
                    entity.AddParameter("duration", new cFloat(5f), ParameterVariant.PARAMETER);
                    entity.AddParameter("samples_per_second", new cFloat(60f), ParameterVariant.PARAMETER);
                    entity.AddParameter("auto_scale", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("auto_scroll", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UnlockAchievement:
                    entity.AddParameter("achievement_id", new cString("CA_PROGRESSION_01"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AchievementMonitor:
                    entity.AddParameter("achievement_id", new cString("CA_PROGRESSION_01"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AchievementStat:
                    entity.AddParameter("achievement_id", new cString("CA_IDTAG_STAT"), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.AchievementUniqueCounter:
                    entity.AddParameter("achievement_id", new cString("CA_IDTAG_STAT"), ParameterVariant.PARAMETER);
                    entity.AddParameter("unique_object", new cResource(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SetRichPresence:
                    entity.AddParameter("presence_id", new cString("NULL_STRING"), ParameterVariant.PARAMETER);
                    entity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.SmokeCylinder:
                    break;
                case FunctionType.SmokeCylinderAttachmentInterface:
                    break;
                case FunctionType.PointTracker:
                    entity.AddParameter("origin_offset", new cVector3(new Vector3(0, 0, 0)), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_speed", new cFloat(180.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("damping_factor", new cFloat(0.6f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ThrowingPointOfImpact:
                    break;
                case FunctionType.VisibilityMaster:
                    break;
                case FunctionType.MotionTrackerMonitor:
                    break;
                case FunctionType.GlobalEvent:
                    entity.AddParameter("EventName", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GlobalEventMonitor:
                    entity.AddParameter("EventName", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.GlobalPosition:
                    entity.AddParameter("PositionName", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.UpdateGlobalPosition:
                    entity.AddParameter("PositionName", new cString(""), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayerLightProbe:
                    break;
                case FunctionType.PlayerKilledAllyMonitor:
                    break;
                case FunctionType.AILightCurveSettings:
                    entity.AddParameter("y0", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("x1", new cFloat(0.25f), ParameterVariant.PARAMETER);
                    entity.AddParameter("y1", new cFloat(0.3f), ParameterVariant.PARAMETER);
                    entity.AddParameter("x2", new cFloat(0.6f), ParameterVariant.PARAMETER);
                    entity.AddParameter("y2", new cFloat(0.8f), ParameterVariant.PARAMETER);
                    entity.AddParameter("x3", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.InteractiveMovementControl:
                    entity.AddParameter("can_go_both_ways", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("use_left_input_stick", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("base_progress_speed", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("movement_threshold", new cFloat(30.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("momentum_damping", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("track_bone_position", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("character_node", new cEnum(EnumType.CHARACTER_NODE, 9), ParameterVariant.PARAMETER);
                    entity.AddParameter("track_position", new cTransform(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PlayForMinDuration:
                    break;
                case FunctionType.GCIP_WorldPickup:
                    entity.AddParameter("Pipe", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Gasoline", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Explosive", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Battery", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Blade", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Gel", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Adhesive", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("BoltGun Ammo", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Revolver Ammo", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Shotgun Ammo", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("BoltGun", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Revolver", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Shotgun", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Flare", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Flamer Fuel", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Flamer", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Scrap", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("Torch Battery", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Torch", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Cattleprod Ammo", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("Cattleprod", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("StartOnReset", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("MissionNumber", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Torch_Control:
                    break;
                case FunctionType.DoorStatus:
                    entity.AddParameter("hacking_difficulty", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("gate_type", new cEnum(EnumType.UI_KEYGATE_TYPE, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("has_correct_keycard", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("cutting_tool_level", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_locked", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_powered", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_cutting_complete", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DeleteHacking:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DeleteKeypad:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DeleteCuttingPanel:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DeleteBlankPanel:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DeleteHousing:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("is_door", new cBool(true), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DeletePullLever:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("lever_type", new cEnum(EnumType.LEVER_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DeleteRotateLever:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("lever_type", new cEnum(EnumType.LEVER_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DeleteButtonDisk:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("button_type", new cEnum(EnumType.BUTTON_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.DeleteButtonKeys:
                    entity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER);
                    entity.AddParameter("button_type", new cEnum(EnumType.BUTTON_TYPE, 0), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.Interaction:
                    entity.AddParameter("interruptible_on_start", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.PhysicsSystem:
                    break;
                case FunctionType.BulletChamber:
                    break;
                case FunctionType.PlayerDeathCounter:
                    entity.AddParameter("Limit", new cInteger(1), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.RadiosityIsland:
                    break;
                case FunctionType.RadiosityProxy:
                    entity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ElapsedTimer:
                    break;
                case FunctionType.LeaderboardWriter:
                    entity.AddParameter("time_elapsed", new cFloat(0.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("score", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("level_number", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("grade", new cInteger(5), ParameterVariant.PARAMETER);
                    entity.AddParameter("player_character", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("combat", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("stealth", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("improv", new cInteger(0), ParameterVariant.PARAMETER);
                    entity.AddParameter("star1", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("star2", new cBool(false), ParameterVariant.PARAMETER);
                    entity.AddParameter("star3", new cBool(false), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.ProximityDetector:
                    entity.AddParameter("min_distance", new cFloat(0.3f), ParameterVariant.PARAMETER);
                    entity.AddParameter("max_distance", new cFloat(100.0f), ParameterVariant.PARAMETER);
                    entity.AddParameter("requires_line_of_sight", new cBool(true), ParameterVariant.PARAMETER);
                    entity.AddParameter("proximity_duration", new cFloat(1.0f), ParameterVariant.PARAMETER);
                    break;
                case FunctionType.FakeAILightSourceInPlayersHand:
                    entity.AddParameter("radius", new cFloat(5.0f), ParameterVariant.PARAMETER);
                    break;

            }
        }
    }
}
