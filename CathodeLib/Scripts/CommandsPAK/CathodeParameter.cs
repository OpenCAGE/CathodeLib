using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
using System.Runtime.Serialization.Formatters.Binary;
#endif

namespace CATHODE.Commands
{
    /* Data types in the CATHODE scripting system */
    public enum DataType
    {
        POSITION,
        FLOAT,
        STRING,
        SPLINE_DATA,
        ENUM,
        RESOURCE,
        FILEPATH,
        BOOL,
        DIRECTION,
        INTEGER,

        OBJECT,
        NO_TYPE, //Translates to a blank string
        ZONE_LINK_PTR,
        ZONE_PTR,
        MARKER,
        CHARACTER,
        CAMERA
    }

    /* Function types in the CATHODE scripting system */
    public enum FunctionType
    {
        AccessTerminal,
        AchievementMonitor,
        AchievementStat,
        AchievementUniqueCounter,
        AddExitObjective,
        AddItemsToGCPool,
        AddToInventory,
        AILightCurveSettings,
        AIMED_ITEM,
        AIMED_WEAPON,
        ALLIANCE_ResetAll,
        ALLIANCE_SetDisposition,
        AllocateGCItemFromPoolBySubset,
        AllocateGCItemsFromPool,
        AllPlayersReady,
        AnimatedModelAttachmentNode,
        AnimationMask,
        ApplyRelativeTransform,
        AreaHitMonitor,
        AssetSpawner,
        AttachmentInterface,
        Benchmark,
        BindObjectsMultiplexer,
        BlendLowResFrame,
        BloomSettings,
        BoneAttachedCamera,
        BooleanLogicInterface,
        BooleanLogicOperation,
        Box,
        BroadcastTrigger,
        BulletChamber,
        ButtonMashPrompt,
        CAGEAnimation,
        CameraAimAssistant,
        CameraBehaviorInterface,
        CameraCollisionBox,
        CameraDofController,
        CameraFinder,
        CameraPath,
        CameraPathDriven,
        CameraPlayAnimation,
        CameraResource,
        CameraShake,
        CamPeek,
        Character,
        CharacterAttachmentNode,
        CharacterCommand,
        CharacterMonitor,
        CharacterShivaArms,
        CharacterTypeMonitor,
        Checkpoint,
        CheckpointRestoredNotify,
        ChokePoint,
        CHR_DamageMonitor,
        CHR_DeathMonitor,
        CHR_DeepCrouch,
        CHR_GetAlliance,
        CHR_GetHealth,
        CHR_GetTorch,
        CHR_HasWeaponOfType,
        CHR_HoldBreath,
        CHR_IsWithinRange,
        CHR_KnockedOutMonitor,
        CHR_LocomotionDuck,
        CHR_LocomotionEffect,
        CHR_LocomotionModifier,
        CHR_ModifyBreathing,
        Chr_PlayerCrouch,
        CHR_PlayNPCBark,
        CHR_PlaySecondaryAnimation,
        CHR_RetreatMonitor,
        CHR_SetAlliance,
        CHR_SetAndroidThrowTarget,
        CHR_SetDebugDisplayName,
        CHR_SetFacehuggerAggroRadius,
        CHR_SetFocalPoint,
        CHR_SetHeadVisibility,
        CHR_SetHealth,
        CHR_SetInvincibility,
        CHR_SetMood,
        CHR_SetShowInMotionTracker,
        CHR_SetSubModelVisibility,
        CHR_SetTacticalPosition,
        CHR_SetTacticalPositionToTarget,
        CHR_SetTorch,
        CHR_TakeDamage,
        CHR_TorchMonitor,
        CHR_VentMonitor,
        CHR_WeaponFireMonitor,
        ChromaticAberrations,
        ClearPrimaryObjective,
        ClearSubObjective,
        ClipPlanesController,
        CloseableInterface,
        CMD_AimAt,
        CMD_AimAtCurrentTarget,
        CMD_Die,
        CMD_Follow,
        CMD_FollowUsingJobs,
        CMD_ForceMeleeAttack,
        CMD_ForceReloadWeapon,
        CMD_GoTo,
        CMD_GoToCover,
        CMD_HolsterWeapon,
        CMD_Idle,
        CMD_LaunchMeleeAttack,
        CMD_ModifyCombatBehaviour,
        CMD_MoveTowards,
        CMD_PlayAnimation,
        CMD_Ragdoll,
        CMD_ShootAt,
        CMD_StopScript,
        CollectIDTag,
        CollectNostromoLog,
        CollectSevastopolLog,
        CollisionBarrier,
        ColourCorrectionTransition,
        ColourSettings,
        CompositeInterface,
        CompoundVolume,
        ControllableRange,
        Convo,
        Counter,
        CoverExclusionArea,
        CoverLine,
        Custom_Hiding_Controller,
        Custom_Hiding_Vignette_controller,
        DayToneMappingSettings,
        DEBUG_SenseLevels,
        DebugCamera,
        DebugCaptureCorpse,
        DebugCaptureScreenShot,
        DebugCheckpoint,
        DebugEnvironmentMarker,
        DebugGraph,
        DebugLoadCheckpoint,
        DebugMenuToggle,
        DebugObjectMarker,
        DebugPositionMarker,
        DebugText,
        DebugTextStacking,
        DeleteBlankPanel,
        DeleteButtonDisk,
        DeleteButtonKeys,
        DeleteCuttingPanel,
        DeleteHacking,
        DeleteHousing,
        DeleteKeypad,
        DeletePullLever,
        DeleteRotateLever,
        DepthOfFieldSettings,
        DespawnCharacter,
        DespawnPlayer,
        Display_Element_On_Map,
        DisplayMessage,
        DisplayMessageWithCallbacks,
        DistortionOverlay,
        DistortionSettings,
        Door,
        DoorStatus,
        DurangoVideoCapture,
        EFFECT_DirectionalPhysics,
        EFFECT_EntityGenerator,
        EFFECT_ImpactGenerator,
        EggSpawner,
        ElapsedTimer,
        EnableMotionTrackerPassiveAudio,
        EndGame,
        ENT_Debug_Exit_Game,
        EnvironmentMap,
        EnvironmentModelReference,
        EQUIPPABLE_ITEM,
        EvaluatorInterface,
        ExclusiveMaster,
        Explosion_AINotifier,
        ExternalVariableBool,
        FakeAILightSourceInPlayersHand,
        FilmGrainSettings,
        Filter,
        FilterAbsorber,
        FilterAnd,
        FilterBelongsToAlliance,
        FilterCanSeeTarget,
        FilterHasBehaviourTreeFlagSet,
        FilterHasPlayerCollectedIdTag,
        FilterHasWeaponEquipped,
        FilterHasWeaponOfType,
        FilterIsACharacter,
        FilterIsAgressing,
        FilterIsAnySaveInProgress,
        FilterIsAPlayer,
        FilterIsCharacter,
        FilterIsCharacterClass,
        FilterIsCharacterClassCombo,
        FilterIsDead,
        FilterIsEnemyOfAllianceGroup,
        FilterIsEnemyOfCharacter,
        FilterIsEnemyOfPlayer,
        FilterIsFacingTarget,
        FilterIsHumanNPC,
        FilterIsInAGroup,
        FilterIsInAlertnessState,
        FilterIsinInventory,
        FilterIsInLocomotionState,
        FilterIsInWeaponRange,
        FilterIsLocalPlayer,
        FilterIsNotDeadManWalking,
        FilterIsObject,
        FilterIsPhysics,
        FilterIsPhysicsObject,
        FilterIsPlatform,
        FilterIsUsingDevice,
        FilterIsValidInventoryItem,
        FilterIsWithdrawnAlien,
        FilterNot,
        FilterOr,
        FilterSmallestUsedDifficulty,
        FixedCamera,
        FlareSettings,
        FlareTask,
        FlashCallback,
        FlashInvoke,
        FlashScript,
        FloatAbsolute,
        FloatAdd,
        FloatAdd_All,
        FloatClamp,
        FloatClampMultiply,
        FloatCompare,
        FloatDivide,
        FloatEquals,
        FloatGetLinearProportion,
        FloatGreaterThan,
        FloatGreaterThanOrEqual,
        FloatLessThan,
        FloatLessThanOrEqual,
        FloatLinearInterpolateSpeed,
        FloatLinearInterpolateSpeedAdvanced,
        FloatLinearInterpolateTimed,
        FloatLinearProportion,
        FloatMath,
        FloatMath_All,
        FloatMax,
        FloatMax_All,
        FloatMin,
        FloatMin_All,
        FloatModulate,
        FloatModulateRandom,
        FloatMultiply,
        FloatMultiply_All,
        FloatMultiplyClamp,
        FloatNotEqual,
        FloatOperation,
        FloatReciprocal,
        FloatRemainder,
        FloatSmoothStep,
        FloatSqrt,
        FloatSubtract,
        FlushZoneCache,
        FogBox,
        FogPlane,
        FogSetting,
        FogSphere,
        FollowCameraModifier,
        FollowTask,
        Force_UI_Visibility,
        FullScreenBlurSettings,
        FullScreenOverlay,
        GameDVR,
        GameOver,
        GameOverCredits,
        GameplayTip,
        GameStateChanged,
        GateInterface,
        GateResourceInterface,
        GCIP_WorldPickup, //n:\\content\\build\\library\\archetypes\\gameplay\\gcip_worldpickup
        GenericHighlightEntity,
        GetBlueprintAvailable,
        GetBlueprintLevel,
        GetCentrePoint,
        GetCharacterRotationSpeed,
        GetClosestPercentOnSpline,
        GetClosestPoint,
        GetClosestPointFromSet,
        GetClosestPointOnSpline,
        GetComponentInterface,
        GetCurrentCameraFov,
        GetCurrentCameraPos,
        GetCurrentCameraTarget,
        GetCurrentPlaylistLevelIndex,
        GetFlashFloatValue,
        GetFlashIntValue,
        GetGatingToolLevel,
        GetInventoryItemName,
        GetNextPlaylistLevelName,
        GetPlayerHasGatingTool,
        GetPlayerHasKeycard,
        GetPointOnSpline,
        GetRotation,
        GetSelectedCharacterId,
        GetSplineLength,
        GetTranslation,
        GetX,
        GetY,
        GetZ,
        GlobalEvent,
        GlobalEventMonitor,
        GlobalPosition,
        GoToFrontend,
        GPU_PFXEmitterReference,
        HableToneMappingSettings,
        HackingGame,
        HandCamera,
        HasAccessAtDifficulty,
        HeldItem_AINotifier,
        HighSpecMotionBlurSettings,
        HostOnlyTrigger,
        IdleTask,
        ImpactSphere,
        InhibitActionsUntilRelease,
        InspectorInterface,
        IntegerAbsolute,
        IntegerAdd,
        IntegerAdd_All,
        IntegerAnalyse,
        IntegerAnd,
        IntegerCompare,
        IntegerCompliment,
        IntegerDivide,
        IntegerEquals,
        IntegerGreaterThan,
        IntegerGreaterThanOrEqual,
        IntegerLessThan,
        IntegerLessThanOrEqual,
        IntegerMath,
        IntegerMath_All,
        IntegerMax,
        IntegerMax_All,
        IntegerMin,
        IntegerMin_All,
        IntegerMultiply,
        IntegerMultiply_All,
        IntegerNotEqual,
        IntegerOperation,
        IntegerOr,
        IntegerRemainder,
        IntegerSubtract,
        Interaction,
        InteractiveMovementControl,
        Internal_JOB_SearchTarget,
        InventoryItem,
        IrawanToneMappingSettings,
        IsActive,
        IsAttached,
        IsCurrentLevelAChallengeMap,
        IsCurrentLevelAPreorderMap,
        IsEnabled,
        IsInstallComplete,
        IsLoaded,
        IsLoading,
        IsLocked,
        IsMultiplayerMode,
        IsOpen,
        IsOpening,
        IsPaused,
        IsPlaylistTypeAll,
        IsPlaylistTypeMarathon,
        IsPlaylistTypeSingle,
        IsSpawned,
        IsStarted,
        IsSuspended,
        IsVisible,
        Job,
        JOB_AreaSweep,
        JOB_AreaSweepFlare,
        JOB_Assault,
        JOB_Follow,
        JOB_Follow_Centre,
        JOB_Idle,
        JOB_Panic,
        JOB_SpottingPosition,
        JOB_SystematicSearch,
        JOB_SystematicSearchFlare,
        JobWithPosition,
        LeaderboardWriter,
        LeaveGame,
        LensDustSettings,
        LevelCompletionTargets,
        LevelInfo,
        LevelLoaded,
        LightAdaptationSettings,
        LightingMaster,
        LightReference,
        LimitItemUse,
        LODControls,
        Logic_MultiGate,
        Logic_Vent_Entrance,
        Logic_Vent_System,
        LogicAll,
        LogicCounter,
        LogicDelay,
        LogicGate,
        LogicGateAnd,
        LogicGateEquals,
        LogicGateNotEqual,
        LogicGateOr,
        LogicNot,
        LogicOnce,
        LogicPressurePad,
        LogicSwitch,
        LowResFrameCapture,
        Map_Floor_Change,
        MapAnchor,
        MapItem,
        Master,
        MELEE_WEAPON,
        Minigames,
        MissionNumber,
        ModelReference,
        ModifierInterface,
        MonitorActionMap,
        MonitorBase,
        MonitorPadInput,
        MotionTrackerMonitor,
        MotionTrackerPing,
        MoveAlongSpline,
        MoveInTime,
        MoviePlayer,
        MultipleCharacterAttachmentNode,
        MultiplePickupSpawner,
        MultitrackLoop,
        MusicController,
        MusicTrigger,
        NavMeshArea,
        NavMeshBarrier,
        NavMeshExclusionArea,
        NavMeshReachabilitySeedPoint,
        NavMeshWalkablePlatform,
        NetPlayerCounter,
        NetworkedTimer,
        NetworkProxy,
        NonInteractiveWater,
        NonPersistentBool,
        NonPersistentInt,
        NPC_Aggression_Monitor,
        NPC_AlienConfig,
        NPC_AllSensesLimiter,
        NPC_ambush_monitor,
        NPC_AreaBox,
        NPC_behaviour_monitor,
        NPC_ClearDefendArea,
        NPC_ClearPursuitArea,
        NPC_Coordinator,
        NPC_Debug_Menu_Item,
        NPC_DefineBackstageAvoidanceArea,
        NPC_DynamicDialogue,
        NPC_DynamicDialogueGlobalRange,
        NPC_FakeSense,
        NPC_FollowOffset,
        NPC_ForceCombatTarget,
        NPC_ForceNextJob,
        NPC_ForceRetreat,
        NPC_Gain_Aggression_In_Radius,
        NPC_GetCombatTarget,
        NPC_GetLastSensedPositionOfTarget,
        NPC_Group_Death_Monitor,
        NPC_Group_DeathCounter,
        NPC_Highest_Awareness_Monitor,
        NPC_MeleeContext,
        NPC_multi_behaviour_monitor,
        NPC_navmesh_type_monitor,
        NPC_NotifyDynamicDialogueEvent,
        NPC_Once,
        NPC_ResetFiringStats,
        NPC_ResetSensesAndMemory,
        NPC_SenseLimiter,
        NPC_set_behaviour_tree_flags,
        NPC_SetAgressionProgression,
        NPC_SetAimTarget,
        NPC_SetAlertness,
        NPC_SetAlienDevelopmentStage,
        NPC_SetAutoTorchMode,
        NPC_SetChokePoint,
        NPC_SetDefendArea,
        NPC_SetFiringAccuracy,
        NPC_SetFiringRhythm,
        NPC_SetGunAimMode,
        NPC_SetHidingNearestLocation,
        NPC_SetHidingSearchRadius,
        NPC_SetInvisible,
        NPC_SetLocomotionStyleForJobs,
        NPC_SetLocomotionTargetSpeed,
        NPC_SetPursuitArea,
        NPC_SetRateOfFire,
        NPC_SetSafePoint,
        NPC_SetSenseSet,
        NPC_SetStartPos,
        NPC_SetTotallyBlindInDark,
        NPC_SetupMenaceManager,
        NPC_Sleeping_Android_Monitor,
        NPC_Squad_DialogueMonitor,
        NPC_Squad_GetAwarenessState,
        NPC_Squad_GetAwarenessWatermark,
        NPC_StopAiming,
        NPC_StopShooting,
        NPC_SuspiciousItem,
        NPC_TargetAcquire,
        NPC_TriggerAimRequest,
        NPC_TriggerShootRequest,
        NPC_WithdrawAlien,
        NumConnectedPlayers,
        NumDeadPlayers,
        NumPlayersOnStart,
        PadLightBar,
        PadRumbleImpulse,
        ParticipatingPlayersList,
        ParticleEmitterReference,
        PathfindingAlienBackstageNode,
        PathfindingManualNode,
        PathfindingTeleportNode,
        PathfindingWaitNode,
        Persistent_TriggerRandomSequence,
        PhysicsApplyBuoyancy,
        PhysicsApplyImpulse,
        PhysicsApplyVelocity,
        PhysicsModifyGravity,
        PhysicsSystem,
        PickupSpawner,
        Planet,
        PlatformConstantBool,
        PlatformConstantFloat,
        PlatformConstantInt,
        PlayEnvironmentAnimation,
        Player_ExploitableArea,
        Player_Sensor,
        PlayerCamera,
        PlayerCameraMonitor,
        PlayerCampaignDeaths,
        PlayerCampaignDeathsInARow,
        PlayerDeathCounter,
        PlayerDiscardsItems,
        PlayerDiscardsTools,
        PlayerDiscardsWeapons,
        PlayForMinDuration, //n:\\content\\build\\library\\ayz\\animation\\logichelpers\\playforminduration
        PlayerHasEnoughItems,
        PlayerHasItem,
        PlayerHasItemEntity,
        PlayerHasItemWithName,
        PlayerHasSpaceForItem,
        PlayerKilledAllyMonitor,
        PlayerLightProbe,
        PlayerTorch,
        PlayerTriggerBox,
        PlayerUseTriggerBox,
        PlayerWeaponMonitor,
        PointAt,
        PointTracker,
        PopupMessage,
        PositionDistance,
        PositionMarker,
        PostprocessingSettings,
        ProjectileMotion,
        ProjectileMotionComplex,
        ProjectiveDecal,
        ProximityDetector,
        ProximityTrigger,
        ProxyInterface,
        QueryGCItemPool,
        RadiosityIsland,
        RadiosityProxy,
        RandomBool,
        RandomFloat,
        RandomInt,
        RandomObjectSelector,
        RandomSelect,
        RandomVector,
        Raycast,
        Refraction,
        RegisterCharacterModel,
        RemoveFromGCItemPool,
        RemoveFromInventory,
        RemoveWeaponsFromPlayer,
        RespawnConfig,
        RespawnExcluder,
        ReTransformer,
        Rewire,
        RewireAccess_Point,
        RewireLocation,
        RewireSystem,
        RewireTotalPowerResource,
        RibbonEmitterReference,
        RotateAtSpeed,
        RotateInTime,
        RTT_MoviePlayer,
        SaveGlobalProgression,
        SaveManagers,
        ScalarProduct,
        ScreenEffectEventMonitor,
        ScreenFadeIn,
        ScreenFadeInTimed,
        ScreenFadeOutToBlack,
        ScreenFadeOutToBlackTimed,
        ScreenFadeOutToWhite,
        ScreenFadeOutToWhiteTimed,
        ScriptInterface,
        ScriptVariable,
        SensorAttachmentInterface,
        SensorInterface,
        SetAsActiveMissionLevel,
        SetBlueprintInfo,
        SetBool,
        SetColour,
        SetEnum,
        SetEnumString,
        SetFloat,
        SetGamepadAxes,
        SetGameplayTips,
        SetGatingToolLevel,
        SetHackingToolLevel,
        SetInteger,
        SetLocationAndOrientation,
        SetMotionTrackerRange,
        SetNextLoadingMovie,
        SetObject,
        SetObjectiveCompleted,
        SetPlayerHasGatingTool,
        SetPlayerHasKeycard,
        SetPosition,
        SetPrimaryObjective,
        SetRichPresence,
        SetString,
        SetSubObjective,
        SetupGCDistribution,
        SetVector,
        SetVector2,
        SharpnessSettings,
        Showlevel_Completed,
        SimpleRefraction,
        SimpleWater,
        SmokeCylinder,
        SmokeCylinderAttachmentInterface,
        SmoothMove,
        Sound,
        SoundBarrier,
        SoundEnvironmentMarker,
        SoundEnvironmentZone,
        SoundImpact,
        SoundLevelInitialiser,
        SoundLoadBank,
        SoundLoadSlot,
        SoundMissionInitialiser,
        SoundNetworkNode,
        SoundObject,
        SoundPhysicsInitialiser,
        SoundPlaybackBaseClass,
        SoundPlayerFootwearOverride,
        SoundRTPCController,
        SoundSetRTPC,
        SoundSetState,
        SoundSetSwitch,
        SoundSpline,
        SoundTimelineTrigger,
        SpaceSuitVisor,
        SpaceTransform,
        SpawnGroup,
        Speech,
        SpeechScript,
        Sphere,
        SplineDistanceLerp,
        SplinePath,
        SpottingExclusionArea,
        Squad_SetMaxEscalationLevel,
        StartNewChapter,
        StateQuery,
        StealCamera,
        StreamingMonitor,
        SurfaceEffectBox,
        SurfaceEffectSphere,
        SwitchLevel,
        SyncOnAllPlayers,
        SyncOnFirstPlayer,
        Task,
        TerminalContent,
        TerminalFolder,
        Thinker,
        ThinkOnce,
        ThrowingPointOfImpact,
        ToggleFunctionality,
        TogglePlayerTorch,
        Torch_Control, //n:\\content\\build\\library\\archetypes\\script\\gameplay\\torch_control
        TorchDynamicMovement,
        TransformerInterface,
        TRAV_1ShotClimbUnder,
        TRAV_1ShotFloorVentEntrance,
        TRAV_1ShotFloorVentExit,
        TRAV_1ShotLeap,
        TRAV_1ShotSpline,
        TRAV_1ShotVentEntrance,
        TRAV_1ShotVentExit,
        TRAV_ContinuousBalanceBeam,
        TRAV_ContinuousCinematicSidle,
        TRAV_ContinuousClimbingWall,
        TRAV_ContinuousLadder,
        TRAV_ContinuousLedge,
        TRAV_ContinuousPipe,
        TRAV_ContinuousTightGap,
        Trigger_AudioOccluded,
        TriggerBindAllCharactersOfType,
        TriggerBindAllNPCs,
        TriggerBindCharacter,
        TriggerBindCharactersInSquad,
        TriggerCameraViewCone,
        TriggerCameraViewConeMulti,
        TriggerCameraVolume,
        TriggerCheckDifficulty,
        TriggerContainerObjectsFilterCounter,
        TriggerDamaged,
        TriggerDelay,
        TriggerExtractBoundCharacter,
        TriggerExtractBoundObject,
        TriggerFilter,
        TriggerLooper,
        TriggerObjectsFilter,
        TriggerObjectsFilterCounter,
        TriggerRandom,
        TriggerRandomSequence,
        TriggerSelect,
        TriggerSelect_Direct,
        TriggerSequence,
        TriggerSimple,
        TriggerSwitch,
        TriggerSync,
        TriggerTouch,
        TriggerUnbindCharacter,
        TriggerViewCone,
        TriggerVolumeFilter,
        TriggerVolumeFilter_Monitored,
        TriggerWeightedRandom,
        TriggerWhenSeeTarget,
        TutorialMessage,
        UI_Attached,
        UI_Container,
        UI_Icon,
        UI_KeyGate,
        UI_Keypad,
        UI_ReactionGame,
        UIBreathingGameIcon,
        UiSelectionBox,
        UiSelectionSphere,
        UnlockAchievement,
        UnlockLogEntry,
        UnlockMapDetail,
        UpdateGlobalPosition,
        UpdateLeaderBoardDisplay,
        UpdatePrimaryObjective,
        UpdateSubObjective,
        VariableAnimationInfo,
        VariableBool,
        VariableColour,
        VariableEnum,
        VariableEnumString,
        VariableFilterObject,
        VariableFlashScreenColour,
        VariableFloat,
        VariableHackingConfig,
        VariableInt,
        VariableObject,
        VariablePosition,
        VariableString,
        VariableThePlayer,
        VariableTriggerObject,
        VariableVector,
        VariableVector2,
        VectorAdd,
        VectorDirection,
        VectorDistance,
        VectorLinearInterpolateSpeed,
        VectorLinearInterpolateTimed,
        VectorLinearProportion,
        VectorMath,
        VectorModulus,
        VectorMultiply,
        VectorMultiplyByPos,
        VectorNormalise,
        VectorProduct,
        VectorReflect,
        VectorRotateByPos,
        VectorRotatePitch,
        VectorRotateRoll,
        VectorRotateYaw,
        VectorScale,
        VectorSubtract,
        VectorYaw,
        VideoCapture,
        VignetteSettings,
        VisibilityMaster,
        Weapon_AINotifier,
        WEAPON_AmmoTypeFilter,
        WEAPON_AttackerFilter,
        WEAPON_DamageFilter,
        WEAPON_DidHitSomethingFilter,
        WEAPON_Effect,
        WEAPON_GiveToCharacter,
        WEAPON_GiveToPlayer,
        WEAPON_ImpactAngleFilter,
        WEAPON_ImpactCharacterFilter,
        WEAPON_ImpactEffect,
        WEAPON_ImpactFilter,
        WEAPON_ImpactInspector,
        WEAPON_ImpactOrientationFilter,
        WEAPON_MultiFilter,
        WEAPON_TargetObjectFilter,
        Zone,
        ZoneExclusionLink,
        ZoneInterface,
        ZoneLink,
        ZoneLoaded,
    }

    /* Resource reference types */
    public enum ResourceType
    {
        //CATHODE_COVER_SEGMENT,
        COLLISION_MAPPING,               
        DYNAMIC_PHYSICS_SYSTEM,          
        EXCLUSIVE_MASTER_STATE_RESOURCE, 
        NAV_MESH_BARRIER_RESOURCE,       
        RENDERABLE_INSTANCE,             
        TRAVERSAL_SEGMENT,               
        ANIMATED_MODEL,                  
    }

    /* A parameter compiled in COMMANDS.PAK */
    [Serializable]
    public class ParameterData : ICloneable
    {
        public ParameterData() { }
        public ParameterData(DataType type)
        {
            dataType = type;
        }
        public DataType dataType = DataType.NO_TYPE;

        public static bool operator ==(ParameterData x, ParameterData y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
            if (x.dataType != y.dataType) return false;
            switch (x.dataType)
            {
                case DataType.POSITION:
                    cTransform x_t = (cTransform)x;
                    cTransform y_t = (cTransform)y;
                    return x_t.position == y_t.position && x_t.rotation == y_t.rotation;
                case DataType.INTEGER:
                    return ((cInteger)x).value == ((cInteger)y).value;
                case DataType.STRING:
                    return ((cString)x).value == ((cString)y).value;
                case DataType.BOOL:
                    return ((cBool)x).value == ((cBool)y).value;
                case DataType.FLOAT:
                    return ((cFloat)x).value == ((cFloat)y).value;
                case DataType.RESOURCE:
                    return ((cResource)x).resourceID == ((cResource)y).resourceID;
                case DataType.DIRECTION:
                    return ((cVector3)x).value == ((cVector3)y).value;
                case DataType.ENUM:
                    cEnum x_e = (cEnum)x;
                    cEnum y_e = (cEnum)y;
                    return x_e.enumIndex == y_e.enumIndex && x_e.enumID == y_e.enumID;
                case DataType.SPLINE_DATA:
                    return ((cSpline)x).splinePoints == ((cSpline)y).splinePoints;
                case DataType.NO_TYPE:
                    return true;
                default:
                    return false;
            }
        }
        public static bool operator !=(ParameterData x, ParameterData y)
        {
            return !(x == y);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is ParameterData)) return false;
            return ((ParameterData)obj) == this;
        }

        public override int GetHashCode()
        {
            //this is gross
            switch (dataType)
            {
                case DataType.POSITION:
                    cTransform x_t = (cTransform)this;
                    return Convert.ToInt32(
                        x_t.rotation.x.ToString() + x_t.rotation.y.ToString() + x_t.rotation.z.ToString() +
                        x_t.position.x.ToString() + x_t.position.y.ToString() + x_t.position.z.ToString());
                case DataType.INTEGER:
                    return ((cInteger)this).value;
                case DataType.STRING:
                    cString x_s = (cString)this;
                    string num = "";
                    for (int i = 0; i < x_s.value.Length; i++) num += ((int)x_s.value[i]).ToString();
                    return Convert.ToInt32(num);
                case DataType.BOOL:
                    return ((cBool)this).value ? 1 : 0;
                case DataType.FLOAT:
                    return Convert.ToInt32(((cFloat)this).value.ToString().Replace(".", ""));
                case DataType.RESOURCE:
                    string x_g_s = ((cString)this).value.ToString();
                    string num2 = "";
                    for (int i = 0; i < x_g_s.Length; i++) num2 += ((int)x_g_s[i]).ToString();
                    return Convert.ToInt32(num2);
                case DataType.DIRECTION:
                    cVector3 x_v = (cVector3)this;
                    return Convert.ToInt32(x_v.value.x.ToString() + x_v.value.y.ToString() + x_v.value.z.ToString());
                case DataType.ENUM:
                    cEnum x_e = (cEnum)this;
                    string x_e_s = x_e.enumID.ToString();
                    string num3 = "";
                    for (int i = 0; i < x_e_s.Length; i++) num3 += ((int)x_e_s[i]).ToString();
                    return Convert.ToInt32(num3 + x_e.enumIndex.ToString());
                case DataType.SPLINE_DATA:
                    cSpline x_sd = (cSpline)this;
                    string x_sd_s = "";
                    for (int i = 0; i < x_sd.splinePoints.Count; i++) x_sd_s += x_sd.splinePoints[i].position.GetHashCode().ToString();
                    ShortGuid x_sd_g = ShortGuidUtils.Generate(x_sd_s);
                    string x_sd_g_s = x_sd_g.ToString();
                    string num4 = "";
                    for (int i = 0; i < x_sd_g_s.Length; i++) num4 += ((int)x_sd_g_s[i]).ToString();
                    return Convert.ToInt32(num4);
                default:
                    return -1;
            }
        }

        public object Clone()
        {
            switch (dataType)
            {
                case DataType.SPLINE_DATA:
                case DataType.RESOURCE:
                    return Utilities.CloneObject(this);
                //HOTFIX FOR VECTOR 3 CLONE ISSUE - TODO: FIND WHY THIS ISN'T WORKING WITH MEMBERWISE CLONE
                case DataType.DIRECTION:
                    cVector3 v3 = (cVector3)this.MemberwiseClone();
                    v3.value = (Vector3)((cVector3)this).value.Clone();
                    return v3;
                case DataType.POSITION:
                    cTransform tr = (cTransform)this.MemberwiseClone();
                    tr.position = (Vector3)((cTransform)this).position.Clone();
                    tr.rotation = (Vector3)((cTransform)this).rotation.Clone();
                    return tr;
                //END OF HOTFIX - SHOULD THIS ALSO APPLY TO OTHERS??
                default:
                    return this.MemberwiseClone();
            }
        }
    }
    [Serializable]
    public class cTransform : ParameterData
    {
        public cTransform() { dataType = DataType.POSITION; }
        public cTransform(Vector3 position, Vector3 rotation)
        {
            this.position = position;
            this.rotation = rotation;
            dataType = DataType.POSITION;
        }

        public Vector3 position = new Vector3();
        public Vector3 rotation = new Vector3(); //In CATHODE this is named Roll/Pitch/Yaw
    }
    [Serializable]
    public class cInteger : ParameterData
    {
        public cInteger() { dataType = DataType.INTEGER; }
        public cInteger(int value)
        {
            this.value = value;
            dataType = DataType.INTEGER;
        }

        public int value = 0;
    }
    [Serializable]
    public class cString : ParameterData
    {
        public cString() { dataType = DataType.STRING; }
        public cString(string value)
        {
            this.value = value;
            dataType = DataType.STRING;
        }

        public string value = "";
    }
    [Serializable]
    public class cBool : ParameterData
    {
        public cBool() { dataType = DataType.BOOL; }
        public cBool(bool value)
        {
            this.value = value;
            dataType = DataType.BOOL;
        }

        public bool value = false;
    }
    [Serializable]
    public class cFloat : ParameterData
    {
        public cFloat() { dataType = DataType.FLOAT; }
        public cFloat(float value)
        {
            this.value = value;
            dataType = DataType.FLOAT;
        }

        public float value = 0.0f;
    }
    [Serializable]
    public class cResource : ParameterData
    {
        public cResource() { dataType = DataType.RESOURCE; }
        public cResource(ShortGuid resourceID)
        {
            this.resourceID = resourceID;
            dataType = DataType.RESOURCE;
        }
        public cResource(List<ResourceReference> value, ShortGuid resourceID)
        {
            this.value = value;
            this.resourceID = resourceID;
            dataType = DataType.RESOURCE;
        }

        public List<ResourceReference> value = new List<ResourceReference>();
        public ShortGuid resourceID;
    }
    [Serializable]
    public class cVector3 : ParameterData
    {
        public cVector3() { dataType = DataType.DIRECTION; }
        public cVector3(Vector3 value)
        {
            this.value = value;
            dataType = DataType.RESOURCE;
        }

        public Vector3 value = new Vector3();
    }
    [Serializable]
    public class cEnum : ParameterData
    {
        public cEnum() { dataType = DataType.ENUM; }
        public cEnum(ShortGuid enumID, int enumIndex)
        {
            this.enumID = enumID;
            this.enumIndex = enumIndex;
            dataType = DataType.ENUM;
        }
        public cEnum(string enumName, int enumIndex)
        {
            this.enumID = ShortGuidUtils.Generate(enumName);
            this.enumIndex = enumIndex;
            dataType = DataType.ENUM;
        }

        public ShortGuid enumID;
        public int enumIndex = 0;
    }
    [Serializable]
    public class cSpline : ParameterData
    {
        public cSpline() { dataType = DataType.SPLINE_DATA; }
        public cSpline(List<cTransform> splinePoints)
        {
            this.splinePoints = splinePoints;
            dataType = DataType.SPLINE_DATA;
        }

        public List<cTransform> splinePoints = new List<cTransform>();
    }
}
