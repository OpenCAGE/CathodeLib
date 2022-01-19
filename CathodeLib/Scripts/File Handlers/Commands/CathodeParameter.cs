using System;
using System.Collections.Generic;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Commands
{
    /* Data types in the CATHODE scripting system */
    public enum CathodeDataType
    {
        POSITION,
        FLOAT,
        STRING,
        SPLINE_DATA,
        ENUM,
        SHORT_GUID,
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

    /* Function node types in the CATHODE scripting system */
    public enum CathodeFunctionType
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
    public enum CathodeResourceReferenceType
    {
        //CATHODE_COVER_SEGMENT,
        COLLISION_MAPPING,               //This one seems to be called in another script block that I'm not currently parsing 
        DYNAMIC_PHYSICS_SYSTEM,          //This is a count (usually small) and then a -1 32-bit int
        EXCLUSIVE_MASTER_STATE_RESOURCE, //This just seems to be two -1 32-bit integers (same as above)
        NAV_MESH_BARRIER_RESOURCE,       //This just seems to be two -1 32-bit integers (same as above)
        RENDERABLE_INSTANCE,             //This one references an entry in the REnDerable elementS (REDS.BIN) database
        TRAVERSAL_SEGMENT,               //This just seems to be two -1 32-bit integers
        ANIMATED_MODEL,                  //This is a count (usually small) and then a -1 32-bit int (same as above)
    }

    /* A parameter compiled in COMMANDS.PAK */
    [Serializable]
    public class CathodeParameter : ICloneable
    {
        public CathodeParameter() { }
        public CathodeParameter(CathodeDataType type)
        {
            dataType = type;
        }
        public CathodeDataType dataType = CathodeDataType.NO_TYPE;

        public static bool operator ==(CathodeParameter x, CathodeParameter y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
            if (x.dataType != y.dataType) return false;
            switch (x.dataType)
            {
                case CathodeDataType.POSITION:
                    CathodeTransform x_t = (CathodeTransform)x;
                    CathodeTransform y_t = (CathodeTransform)y;
                    return x_t.position == y_t.position && x_t.rotation == y_t.rotation;
                case CathodeDataType.INTEGER:
                    return ((CathodeInteger)x).value == ((CathodeInteger)y).value;
                case CathodeDataType.STRING:
                    return ((CathodeString)x).value == ((CathodeString)y).value;
                case CathodeDataType.BOOL:
                    return ((CathodeBool)x).value == ((CathodeBool)y).value;
                case CathodeDataType.FLOAT:
                    return ((CathodeFloat)x).value == ((CathodeFloat)y).value;
                case CathodeDataType.SHORT_GUID:
                    return ((CathodeResource)x).resourceID == ((CathodeResource)y).resourceID;
                case CathodeDataType.DIRECTION:
                    return ((CathodeVector3)x).value == ((CathodeVector3)y).value;
                case CathodeDataType.ENUM:
                    CathodeEnum x_e = (CathodeEnum)x;
                    CathodeEnum y_e = (CathodeEnum)y;
                    return x_e.enumIndex == y_e.enumIndex && x_e.enumID == y_e.enumID;
                case CathodeDataType.SPLINE_DATA:
                    return ((CathodeSpline)x).splinePoints == ((CathodeSpline)y).splinePoints;
                case CathodeDataType.NO_TYPE:
                    return true;
                default:
                    return false;
            }
        }
        public static bool operator !=(CathodeParameter x, CathodeParameter y)
        {
            return !(x == y);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is CathodeParameter)) return false;
            return ((CathodeParameter)obj) == this;
        }

        public override int GetHashCode()
        {
            //this is gross
            switch (dataType)
            {
                case CathodeDataType.POSITION:
                    CathodeTransform x_t = (CathodeTransform)this;
                    return Convert.ToInt32(
                        x_t.rotation.x.ToString() + x_t.rotation.y.ToString() + x_t.rotation.z.ToString() +
                        x_t.position.x.ToString() + x_t.position.y.ToString() + x_t.position.z.ToString());
                case CathodeDataType.INTEGER:
                    return ((CathodeInteger)this).value;
                case CathodeDataType.STRING:
                    CathodeString x_s = (CathodeString)this;
                    string num = "";
                    for (int i = 0; i < x_s.value.Length; i++) num += ((int)x_s.value[i]).ToString();
                    return Convert.ToInt32(num);
                case CathodeDataType.BOOL:
                    return ((CathodeBool)this).value ? 1 : 0;
                case CathodeDataType.FLOAT:
                    return Convert.ToInt32(((CathodeFloat)this).value.ToString().Replace(".", ""));
                case CathodeDataType.SHORT_GUID:
                    string x_g_s = ((CathodeString)this).value.ToString();
                    string num2 = "";
                    for (int i = 0; i < x_g_s.Length; i++) num2 += ((int)x_g_s[i]).ToString();
                    return Convert.ToInt32(num2);
                case CathodeDataType.DIRECTION:
                    CathodeVector3 x_v = (CathodeVector3)this;
                    return Convert.ToInt32(x_v.value.x.ToString() + x_v.value.y.ToString() + x_v.value.z.ToString());
                case CathodeDataType.ENUM:
                    CathodeEnum x_e = (CathodeEnum)this;
                    string x_e_s = x_e.enumID.ToString();
                    string num3 = "";
                    for (int i = 0; i < x_e_s.Length; i++) num3 += ((int)x_e_s[i]).ToString();
                    return Convert.ToInt32(num3 + x_e.enumIndex.ToString());
                case CathodeDataType.SPLINE_DATA:
                    CathodeSpline x_sd = (CathodeSpline)this;
                    string x_sd_s = "";
                    for (int i = 0; i < x_sd.splinePoints.Count; i++) x_sd_s += x_sd.splinePoints[i].position.GetHashCode().ToString();
                    cGUID x_sd_g = Utilities.GenerateGUID(x_sd_s);
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
            return this.MemberwiseClone();
        }
    }
    [Serializable]
    public class CathodeTransform : CathodeParameter
    {
        public CathodeTransform() { dataType = CathodeDataType.POSITION; }
        public Vector3 position = new Vector3();
        public Vector3 rotation = new Vector3(); //In CATHODE this is named Roll/Pitch/Yaw
    }
    [Serializable]
    public class CathodeInteger : CathodeParameter
    {
        public CathodeInteger() { dataType = CathodeDataType.INTEGER; }
        public int value = 0;
    }
    [Serializable]
    public class CathodeString : CathodeParameter
    {
        public CathodeString() { dataType = CathodeDataType.STRING; }
        public string value = "";
    }
    [Serializable]
    public class CathodeBool : CathodeParameter
    {
        public CathodeBool() { dataType = CathodeDataType.BOOL; }
        public bool value = false;
    }
    [Serializable]
    public class CathodeFloat : CathodeParameter
    {
        public CathodeFloat() { dataType = CathodeDataType.FLOAT; }
        public float value = 0.0f;
    }
    [Serializable]
    public class CathodeResource : CathodeParameter
    {
        public CathodeResource() { dataType = CathodeDataType.SHORT_GUID; }
        public List<CathodeResourceReference> value = new List<CathodeResourceReference>(); //TODO: i dont know if this can actually have multiple entries. need to assert
        public cGUID resourceID;
    }
    [Serializable]
    public class CathodeVector3 : CathodeParameter
    {
        public CathodeVector3() { dataType = CathodeDataType.DIRECTION; }
        public Vector3 value = new Vector3();
    }
    [Serializable]
    public class CathodeEnum : CathodeParameter
    {
        public CathodeEnum() { dataType = CathodeDataType.ENUM; }
        public cGUID enumID;
        public int enumIndex = 0;
    }
    [Serializable]
    public class CathodeSpline : CathodeParameter
    {
        public CathodeSpline() { dataType = CathodeDataType.SPLINE_DATA; }
        public List<CathodeTransform> splinePoints = new List<CathodeTransform>();
    }
}
