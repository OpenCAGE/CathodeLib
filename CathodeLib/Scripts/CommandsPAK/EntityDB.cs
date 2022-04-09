using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CATHODE;
using CATHODE.Commands;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#endif

namespace CathodeLib
{
    public class EntityDBDescriptor
    {
        public ShortGuid ID;
        public string ID_cachedstring;
    }
    public class ShortGuidDescriptor : EntityDBDescriptor
    {
        public string Description;
    }
    public class EnumDescriptor : EntityDBDescriptor
    {
        public string Name;
        public List<string> Entries = new List<string>();
    }

    public class EntityDB
    {
        static EntityDB()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            cathode_id_map = ReadDB(File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_generic_lut.bin")).Cast<ShortGUIDDescriptor>().ToList(); //Names for entity types, parameters, enums, etc from EXE
            cathode_enum_map = ReadDB(File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_enum_lut.bin")).Cast<EnumDescriptor>().ToList(); //Correctly formatted enum list from EXE
            entity_friendly_names = ReadDB(File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_nodename_lut.bin")).Cast<ShortGUIDDescriptor>().ToList(); //Names for unique entities from commands BIN
#else
            cathode_id_map = ReadDB(CathodeLib.Properties.Resources.cathode_generic_lut).Cast<ShortGuidDescriptor>().ToList(); //Names for entity types, parameters, enums, etc from EXE
            cathode_enum_map = ReadDB(CathodeLib.Properties.Resources.cathode_enum_lut).Cast<EnumDescriptor>().ToList(); //Correctly formatted enum list from EXE
            entity_friendly_names = ReadDB(CathodeLib.Properties.Resources.cathode_nodename_lut).Cast<ShortGuidDescriptor>().ToList(); //Names for unique entities from commands BIN
#endif
            SetupEntityParameterList();
        }

        //Check the CATHODE data dump for a corresponding name
        public static string GetCathodeName(ShortGuid id)
        {
            if (id.val == null) return "";
            string id_string = id.ToString();
            for (int i = 0; i < cathode_id_map.Count; i++) if (cathode_id_map[i].ID_cachedstring == id_string) return cathode_id_map[i].Description;
            return id.ToString();
        }
        public static string GetCathodeName(ShortGuid id, CommandsPAK pak) //This is performed separately to be able to remap entities that are composites
        {
            if (id.val == null) return "";
            string id_string = id.ToString();
            for (int i = 0; i < cathode_id_map.Count; i++) if (cathode_id_map[i].ID_cachedstring == id_string) return cathode_id_map[i].Description;
            CathodeComposite composite = pak.GetComposite(id); if (composite == null) return id.ToString();
            return composite.name;
        }

        //Reverse CATHODE name check
        public static ShortGuid GetCathodeGUID(string text)
        {
            ShortGuidDescriptor thisDesc = cathode_id_map.FirstOrDefault(o => o.Description == text);
            if (thisDesc == null) return new ShortGuid();
            return thisDesc.ID;
        }

        //Check the COMMANDS.BIN dump for entity in-editor names
        public static string GetEditorName(ShortGuid id)
        {
            if (id.val == null) return "";
            string id_string = id.ToString();
            for (int i = 0; i < entity_friendly_names.Count; i++) if (entity_friendly_names[i].ID_cachedstring == id_string) return entity_friendly_names[i].Description;
            return id.ToString();
        }

        //Reverse editor name check
        public static ShortGuid GetEditorGUID(string text)
        {
            ShortGuidDescriptor thisDesc = entity_friendly_names.FirstOrDefault(o => o.Description == text);
            if (thisDesc == null) return new ShortGuid();
            return thisDesc.ID;
        }

        //Check the formatted enum dump for content
        public static EnumDescriptor GetEnum(ShortGuid id)
        {
            return cathode_enum_map.FirstOrDefault(o => o.ID == id);
        }

        //Get the known-valid params for a entity (this list is incomplete, and needs populating with default vals)
        public static string[] GetEntityParameterList(string entity_name)
        {
            if (!entity_parameters.ContainsKey(entity_name)) return null;
            return entity_parameters[entity_name];
        }

        //Read a generic entity database file
        private static List<EntityDBDescriptor> ReadDB(byte[] db_content)
        {
            List<EntityDBDescriptor> toReturn = new List<EntityDBDescriptor>();

            MemoryStream readerStream = new MemoryStream(db_content);
            BinaryReader reader = new BinaryReader(readerStream);
            int type = reader.ReadChar(); //0 = normal db, 1 = enum db
            switch (type)
            {
                case 0:
                {
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        ShortGuidDescriptor thisDesc = new ShortGuidDescriptor();
                        thisDesc.ID = new ShortGuid(reader.ReadBytes(4));
                        thisDesc.Description = reader.ReadString();
                        toReturn.Add(thisDesc);
                    }
                    break;
                }
                case 1:
                {
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        EnumDescriptor thisDesc = new EnumDescriptor();
                        thisDesc.ID = new ShortGuid(reader.ReadBytes(4));
                        thisDesc.Name = reader.ReadString();
                        int entryCount = reader.ReadInt32();
                        for (int i = 0; i < entryCount; i++) thisDesc.Entries.Add(reader.ReadString());
                        toReturn.Add(thisDesc);
                    }
                    break;
                }
            }
            reader.Close();

            for (int i = 0; i < toReturn.Count; i++) toReturn[i].ID_cachedstring = toReturn[i].ID.ToString();
            return toReturn;
        }

        //Populate the entity parameter dictionary
        private static void SetupEntityParameterList()
        {
            if (entity_parameters.Count != 0) return;

            entity_parameters.Add("AccessTerminal", new string[] { "closed", "all_data_has_been_read", "ui_breakout_triggered", "light_on_reset", "folder0", "folder1", "folder2", "folder3", "all_data_read", "location" });
            entity_parameters.Add("AchievementMonitor", new string[] { "achievement_id" });
            entity_parameters.Add("AchievementStat", new string[] { "achievement_id" });
            entity_parameters.Add("AchievementUniqueCounter", new string[] { "achievement_id", "unique_object" });
            entity_parameters.Add("AddExitObjective", new string[] { "marker", "level_name" });
            entity_parameters.Add("AddItemsToGCPool", new string[] { "items" });
            entity_parameters.Add("AddToInventory", new string[] { "success", "fail", "items" });
            entity_parameters.Add("AILightCurveSettings", new string[] { "y0", "x1", "y1", "x2", "y2", "x3" });
            entity_parameters.Add("AIMED_ITEM", new string[] { "on_started_aiming", "on_stopped_aiming", "on_display_on", "on_display_off", "on_effect_on", "on_effect_off", "target_position", "average_target_distance", "min_target_distance", "fixed_target_distance_for_local_player" });
            entity_parameters.Add("AIMED_WEAPON", new string[] { "on_fired_success", "on_fired_fail", "on_fired_fail_single", "on_impact", "on_reload_started", "on_reload_another", "on_reload_empty_clip", "on_reload_canceled", "on_reload_success", "on_reload_fail", "on_shooting_started", "on_shooting_wind_down", "on_shooting_finished", "on_overheated", "on_cooled_down", "on_charge_complete", "on_charge_started", "on_charge_stopped", "on_turned_on", "on_turned_off", "on_torch_on_requested", "on_torch_off_requested", "ammoRemainingInClip", "ammoToFillClip", "ammoThatWasInClip", "charge_percentage", "charge_noise_percentage", "weapon_type", "requires_turning_on", "ejectsShellsOnFiring", "aim_assist_scale", "default_ammo_type", "starting_ammo", "clip_size", "consume_ammo_over_time_when_turned_on", "max_auto_shots_per_second", "max_manual_shots_per_second", "wind_down_time_in_seconds", "maximum_continous_fire_time_in_seconds", "overheat_recharge_time_in_seconds", "automatic_firing", "overheats", "charged_firing", "charging_duration", "min_charge_to_fire", "overcharge_timer", "charge_noise_start_time", "reloadIndividualAmmo", "alwaysDoFullReloadOfClips", "movement_accuracy_penalty_per_second", "aim_rotation_accuracy_penalty_per_second", "accuracy_penalty_per_shot", "accuracy_accumulated_per_second", "player_exposed_accuracy_penalty_per_shot", "player_exposed_accuracy_accumulated_per_second", "recoils_on_fire", "alien_threat_aware" });
            entity_parameters.Add("ALLIANCE_ResetAll", new string[] { });
            entity_parameters.Add("ALLIANCE_SetDisposition", new string[] { "A", "B", "Disposition" });
            entity_parameters.Add("AllocateGCItemFromPoolBySubset", new string[] { "on_success", "on_failure", "selectable_items", "item_name", "item_quantity", "force_usage", "distribution_bias" });
            entity_parameters.Add("AllocateGCItemsFromPool", new string[] { "on_success", "on_failure", "items", "force_usage_count", "distribution_bias" });
            entity_parameters.Add("AllPlayersReady", new string[] { "on_all_players_ready", "start_on_reset", "pause_on_reset", "activation_delay" });
            entity_parameters.Add("AnimatedModelAttachmentNode", new string[] { "attach_on_reset", "animated_model", "attachment", "bone_name", "use_offset", "offset" });
            entity_parameters.Add("AnimationMask", new string[] { "maskHips", "maskTorso", "maskNeck", "maskHead", "maskFace", "maskLeftLeg", "maskRightLeg", "maskLeftArm", "maskRightArm", "maskLeftHand", "maskRightHand", "maskLeftFingers", "maskRightFingers", "maskTail", "maskLips", "maskEyes", "maskLeftShoulder", "maskRightShoulder", "maskRoot", "maskPrecedingLayers", "maskSelf", "maskFollowingLayers", "weight", "resource" });
            entity_parameters.Add("ApplyRelativeTransform", new string[] { "origin", "destination", "input", "output", "use_trigger_entity" });
            entity_parameters.Add("AreaHitMonitor", new string[] { "on_flamer_hit", "on_shotgun_hit", "on_pistol_hit", "SpherePos", "SphereRadius" });
            entity_parameters.Add("AssetSpawner", new string[] { "finished_spawning", "callback_triggered", "forced_despawn", "spawn_on_reset", "asset", "spawn_on_load", "allow_forced_despawn", "persist_on_callback", "allow_physics" });
            entity_parameters.Add("Benchmark", new string[] { "benchmark_name", "save_stats" });
            entity_parameters.Add("BindObjectsMultiplexer", new string[] { "Pin1_Bound", "Pin2_Bound", "Pin3_Bound", "Pin4_Bound", "Pin5_Bound", "Pin6_Bound", "Pin7_Bound", "Pin8_Bound", "Pin9_Bound", "Pin10_Bound", "objects" });
            entity_parameters.Add("BlendLowResFrame", new string[] { "blend_value", "CharacterMonitor", "character" });
            entity_parameters.Add("BloomSettings", new string[] { "frame_buffer_scale", "frame_buffer_offset", "bloom_scale", "bloom_gather_exponent", "bloom_gather_scale" });
            entity_parameters.Add("BoneAttachedCamera", new string[] { "character", "position_offset", "rotation_offset", "movement_damping", "bone_name" });
            entity_parameters.Add("BooleanLogicInterface", new string[] { "on_true", "on_false", "LHS", "RHS", "Result" });
            entity_parameters.Add("BooleanLogicOperation", new string[] { "Input", "Result" });
            entity_parameters.Add("Box", new string[] { "event", "enable_on_reset", "half_dimensions", "include_physics" });
            entity_parameters.Add("BroadcastTrigger", new string[] { "on_triggered" });
            entity_parameters.Add("BulletChamber", new string[] { "Slot1", "Slot2", "Slot3", "Slot4", "Slot5", "Slot6", "Weapon", "Geometry" });
            entity_parameters.Add("ButtonMashPrompt", new string[] { "on_back_to_zero", "on_degrade", "on_mashed", "on_success", "count", "mashes_to_completion", "time_between_degrades", "use_degrade", "hold_to_charge" });
            entity_parameters.Add("CAGEAnimation", new string[] { "animation_finished", "animation_interrupted", "animation_changed", "cinematic_loaded", "cinematic_unloaded", "enable_on_reset", "external_time", "current_time", "use_external_time", "rewind_on_stop", "jump_to_the_end", "playspeed", "anim_length", "is_cinematic", "is_cinematic_skippable", "skippable_timer", "capture_video", "capture_clip_name", "playback" });
            entity_parameters.Add("CameraAimAssistant", new string[] { "enable_on_reset", "activation_radius", "inner_radius", "camera_speed_attenuation", "min_activation_distance", "fading_range" });
            entity_parameters.Add("CameraBehaviorInterface", new string[] { "start_on_reset", "pause_on_reset", "enable_on_reset", "linked_cameras", "behavior_name", "priority", "threshold", "blend_in", "duration", "blend_out" });
            entity_parameters.Add("CameraCollisionBox", new string[] { });
            entity_parameters.Add("CameraDofController", new string[] { "character_to_focus", "focal_length_mm", "focal_plane_m", "fnum", "focal_point", "focal_point_offset", "bone_to_focus" });
            entity_parameters.Add("CameraFinder", new string[] { "camera_name" });
            entity_parameters.Add("CameraPath", new string[] { "linked_splines", "path_name", "path_type", "path_class", "is_local", "relative_position", "is_loop", "duration" });
            entity_parameters.Add("CameraPathDriven", new string[] { "position_path", "target_path", "reference_path", "position_path_transform", "target_path_transform", "reference_path_transform", "point_to_project", "path_driven_type", "invert_progression", "position_path_offset", "target_path_offset", "animation_duration" });
            entity_parameters.Add("CameraPlayAnimation", new string[] { "on_animation_finished", "animated_camera", "position_marker", "character_to_focus", "focal_length_mm", "focal_plane_m", "fnum", "focal_point", "animation_length", "frames_count", "result_transformation", "data_file", "start_frame", "end_frame", "play_speed", "loop_play", "clipping_planes_preset", "is_cinematic", "dof_key", "shot_number", "override_dof", "focal_point_offset", "bone_to_focus" });
            entity_parameters.Add("CameraResource", new string[] { "on_enter_transition_finished", "on_exit_transition_finished", "enable_on_reset", "camera_name", "is_camera_transformation_local", "camera_transformation", "fov", "clipping_planes_preset", "is_ghost", "converge_to_player_camera", "reset_player_camera_on_exit", "enable_enter_transition", "transition_curve_direction", "transition_curve_strength", "transition_duration", "transition_ease_in", "transition_ease_out", "enable_exit_transition", "exit_transition_curve_direction", "exit_transition_curve_strength", "exit_transition_duration", "exit_transition_ease_in", "exit_transition_ease_out" });
            entity_parameters.Add("CameraShake", new string[] { "relative_transformation", "impulse_intensity", "impulse_position", "shake_type", "shake_frequency", "max_rotation_angles", "max_position_offset", "shake_rotation", "shake_position", "bone_shaking", "override_weapon_swing", "internal_radius", "external_radius", "strength_damping", "explosion_push_back", "spring_constant", "spring_damping" });
            entity_parameters.Add("CamPeek", new string[] { "pos", "x_ratio", "y_ratio", "range_left", "range_right", "range_up", "range_down", "range_forward", "range_backward", "speed_x", "speed_y", "damping_x", "damping_y", "focal_distance", "focal_distance_y", "roll_factor", "use_ik_solver", "use_horizontal_plane", "stick", "disable_collision_test" });
            entity_parameters.Add("Character", new string[] { "finished_spawning", "finished_respawning", "dead_container_take_slot", "dead_container_emptied", "on_ragdoll_impact", "on_footstep", "on_despawn_requested", "spawn_on_reset", "show_on_reset", "contents_of_dead_container", "PopToNavMesh", "is_cinematic", "disable_dead_container", "allow_container_without_death", "container_interaction_text", "anim_set", "anim_tree_set", "attribute_set", "is_player", "is_backstage", "force_backstage_on_respawn", "character_class", "alliance_group", "dialogue_voice", "spawn_id", "position", "display_model", "reference_skeleton", "torso_sound", "leg_sound", "footwear_sound", "custom_character_type", "custom_character_accessory_override", "custom_character_population_type", "named_custom_character", "named_custom_character_assets_set", "gcip_distribution_bias", "inventory_set" });
            entity_parameters.Add("CharacterAttachmentNode", new string[] { "attach_on_reset", "character", "attachment", "Node", "AdditiveNode", "AdditiveNodeIntensity", "UseOffset", "Translation", "Rotation" });
            entity_parameters.Add("CharacterCommand", new string[] { "command_started", "override_all_ai" });
            entity_parameters.Add("CharacterShivaArms", new string[] { });
            entity_parameters.Add("CharacterTypeMonitor", new string[] { "spawned", "despawned", "all_despawned", "AreAny", "character_class", "trigger_on_start", "trigger_on_checkpoint_restart" });
            entity_parameters.Add("Checkpoint", new string[] { "on_checkpoint", "on_captured", "on_saved", "finished_saving", "finished_loading", "cancelled_saving", "finished_saving_to_hdd", "player_spawn_position", "is_first_checkpoint", "is_first_autorun_checkpoint", "section", "mission_number", "checkpoint_type" });
            entity_parameters.Add("CheckpointRestoredNotify", new string[] { "restored" });
            entity_parameters.Add("ChokePoint", new string[] { "resource" });
            entity_parameters.Add("CHR_DamageMonitor", new string[] { "damaged", "InstigatorFilter", "DamageDone", "Instigator", "DamageType" });
            entity_parameters.Add("CHR_DeathMonitor", new string[] { "dying", "killed", "KillerFilter", "Killer", "DamageType" });
            entity_parameters.Add("CHR_DeepCrouch", new string[] { "crouch_amount", "smooth_damping", "allow_stand_up" });
            entity_parameters.Add("CHR_GetAlliance", new string[] { "Alliance" });
            entity_parameters.Add("CHR_GetHealth", new string[] { "Health" });
            entity_parameters.Add("CHR_GetTorch", new string[] { "torch_on", "torch_off", "TorchOn" });
            entity_parameters.Add("CHR_HasWeaponOfType", new string[] { "on_true", "on_false", "Result", "weapon_type", "check_if_weapon_draw" });
            entity_parameters.Add("CHR_HoldBreath", new string[] { "ExhaustionOnStop" });
            entity_parameters.Add("CHR_IsWithinRange", new string[] { "In_range", "Out_of_range", "Position", "Radius", "Height", "Range_test_shape" });
            entity_parameters.Add("CHR_KnockedOutMonitor", new string[] { "on_knocked_out", "on_recovered" });
            entity_parameters.Add("CHR_LocomotionDuck", new string[] { "Height" });
            entity_parameters.Add("CHR_LocomotionEffect", new string[] { "Effect" });
            entity_parameters.Add("CHR_LocomotionModifier", new string[] { "Can_Run", "Can_Crouch", "Can_Aim", "Can_Injured", "Must_Walk", "Must_Run", "Must_Crouch", "Must_Aim", "Must_Injured", "Is_In_Spacesuit" });
            entity_parameters.Add("CHR_ModifyBreathing", new string[] { "Exhaustion" });
            entity_parameters.Add("Chr_PlayerCrouch", new string[] { "crouch" });
            entity_parameters.Add("CHR_PlayNPCBark", new string[] { "on_speech_started", "on_speech_finished", "queue_time", "sound_event", "speech_priority", "dialogue_mode", "action" });
            entity_parameters.Add("CHR_PlaySecondaryAnimation", new string[] { "Interrupted", "finished", "on_loaded", "Marker", "OptionalMask", "ExternalStartTime", "ExternalTime", "animationLength", "AnimationSet", "Animation", "StartFrame", "EndFrame", "PlayCount", "PlaySpeed", "StartInstantly", "AllowInterruption", "BlendInTime", "GaitSyncStart", "Mirror", "AnimationLayer", "AutomaticZoning", "ManualLoading" });
            entity_parameters.Add("CHR_RetreatMonitor", new string[] { "reached_retreat", "started_retreating" });
            entity_parameters.Add("CHR_SetAlliance", new string[] { "Alliance" });
            entity_parameters.Add("CHR_SetAndroidThrowTarget", new string[] { "thrown", "throw_position" });
            entity_parameters.Add("CHR_SetDebugDisplayName", new string[] { "DebugName" });
            entity_parameters.Add("CHR_SetFacehuggerAggroRadius", new string[] { "radius" });
            entity_parameters.Add("CHR_SetFocalPoint", new string[] { "focal_point", "priority", "speed", "steal_camera", "line_of_sight_test" });
            entity_parameters.Add("CHR_SetHeadVisibility", new string[] { "is_visible" });
            entity_parameters.Add("CHR_SetHealth", new string[] { "HealthPercentage", "UsePercentageOfCurrentHeath" });
            entity_parameters.Add("CHR_SetInvincibility", new string[] { "damage_mode" });
            entity_parameters.Add("CHR_SetMood", new string[] { "mood", "moodIntensity", "timeOut" });
            entity_parameters.Add("CHR_SetShowInMotionTracker", new string[] { });
            entity_parameters.Add("CHR_SetSubModelVisibility", new string[] { "is_visible", "matching" });
            entity_parameters.Add("CHR_SetTacticalPosition", new string[] { "tactical_position", "sweep_type", "fixed_sweep_radius" });
            entity_parameters.Add("CHR_SetTacticalPositionToTarget", new string[] { });
            entity_parameters.Add("CHR_SetTorch", new string[] { "TorchOn" });
            entity_parameters.Add("CHR_TakeDamage", new string[] { "Damage", "DamageIsAPercentage", "AmmoType" });
            entity_parameters.Add("CHR_TorchMonitor", new string[] { "torch_on", "torch_off", "TorchOn", "trigger_on_start", "trigger_on_checkpoint_restart" });
            entity_parameters.Add("CHR_VentMonitor", new string[] { "entered_vent", "exited_vent", "IsInVent", "trigger_on_start", "trigger_on_checkpoint_restart" });
            entity_parameters.Add("CHR_WeaponFireMonitor", new string[] { "fired" });
            entity_parameters.Add("ChromaticAberrations", new string[] { "aberration_scalar" });
            entity_parameters.Add("ClearPrimaryObjective", new string[] { "clear_all_sub_objectives" });
            entity_parameters.Add("ClearSubObjective", new string[] { "slot_number" });
            entity_parameters.Add("ClipPlanesController", new string[] { "near_plane", "far_plane", "update_near", "update_far" });
            entity_parameters.Add("CMD_AimAt", new string[] { "finished", "AimTarget", "Raise_gun", "use_current_target" });
            entity_parameters.Add("CMD_AimAtCurrentTarget", new string[] { "succeeded", "Raise_gun" });
            entity_parameters.Add("CMD_Die", new string[] { "Killer", "death_style" });
            entity_parameters.Add("CMD_Follow", new string[] { "entered_inner_radius", "exitted_outer_radius", "failed", "Waypoint", "idle_stance", "move_type", "inner_radius", "outer_radius", "prefer_traversals" });
            entity_parameters.Add("CMD_FollowUsingJobs", new string[] { "failed", "target_to_follow", "who_Im_leading", "fastest_allowed_move_type", "slowest_allowed_move_type", "centre_job_restart_radius", "inner_radius", "outer_radius", "job_select_radius", "job_cancel_radius", "teleport_required_range", "teleport_radius", "prefer_traversals", "avoid_player", "allow_teleports", "follow_type", "clamp_speed" });
            entity_parameters.Add("CMD_ForceMeleeAttack", new string[] { "melee_attack_type", "enemy_type", "melee_attack_index" });
            entity_parameters.Add("CMD_ForceReloadWeapon", new string[] { "success" });
            entity_parameters.Add("CMD_GoTo", new string[] { "succeeded", "failed", "Waypoint", "AimTarget", "move_type", "enable_lookaround", "use_stopping_anim", "always_stop_at_radius", "stop_at_radius_if_lined_up", "continue_from_previous_move", "disallow_traversal", "arrived_radius", "should_be_aiming", "use_current_target_as_aim", "allow_to_use_vents", "DestinationIsBackstage", "maintain_current_facing", "start_instantly" });
            entity_parameters.Add("CMD_GoToCover", new string[] { "succeeded", "failed", "entered_cover", "CoverPoint", "AimTarget", "move_type", "SearchRadius", "enable_lookaround", "duration", "continue_from_previous_move", "disallow_traversal", "should_be_aiming", "use_current_target_as_aim" });
            entity_parameters.Add("CMD_HolsterWeapon", new string[] { "failed", "success", "should_holster", "skip_anims", "equipment_slot", "force_player_unarmed_on_holster", "force_drop_held_item" });
            entity_parameters.Add("CMD_Idle", new string[] { "finished", "interrupted", "target_to_face", "should_face_target", "should_raise_gun_while_turning", "desired_stance", "duration", "idle_style", "lock_cameras", "anchor", "start_instantly" });
            entity_parameters.Add("CMD_LaunchMeleeAttack", new string[] { "finished", "melee_attack_type", "enemy_type", "melee_attack_index", "skip_convergence" });
            entity_parameters.Add("CMD_ModifyCombatBehaviour", new string[] { "behaviour_type", "status" });
            entity_parameters.Add("CMD_MoveTowards", new string[] { "succeeded", "failed", "MoveTarget", "AimTarget", "move_type", "disallow_traversal", "should_be_aiming", "use_current_target_as_aim", "never_succeed" });
            entity_parameters.Add("CMD_PlayAnimation", new string[] { "Interrupted", "finished", "badInterrupted", "on_loaded", "SafePos", "Marker", "ExitPosition", "ExternalStartTime", "ExternalTime", "OverrideCharacter", "OptionalMask", "animationLength", "AnimationSet", "Animation", "StartFrame", "EndFrame", "PlayCount", "PlaySpeed", "AllowGravity", "AllowCollision", "Start_Instantly", "AllowInterruption", "RemoveMotion", "DisableGunLayer", "BlendInTime", "GaitSyncStart", "ConvergenceTime", "LocationConvergence", "OrientationConvergence", "UseExitConvergence", "ExitConvergenceTime", "Mirror", "FullCinematic", "RagdollEnabled", "NoIK", "NoFootIK", "NoLayers", "PlayerAnimDrivenView", "ExertionFactor", "AutomaticZoning", "ManualLoading", "IsCrouchedAnim", "InitiallyBackstage", "Death_by_ragdoll_only", "dof_key", "shot_number", "UseShivaArms", "resource" });
            entity_parameters.Add("CMD_Ragdoll", new string[] { "finished", "actor", "impact_velocity" });
            entity_parameters.Add("CMD_ShootAt", new string[] { "succeeded", "failed", "Target" });
            entity_parameters.Add("CMD_StopScript", new string[] { });
            entity_parameters.Add("CollectIDTag", new string[] { "tag_id" });
            entity_parameters.Add("CollectNostromoLog", new string[] { "log_id" });
            entity_parameters.Add("CollectSevastopolLog", new string[] { "log_id" });
            entity_parameters.Add("CollisionBarrier", new string[] { "on_damaged", "deleted", "collision_type", "static_collision" });
            entity_parameters.Add("ColourCorrectionTransition", new string[] { "interpolate", "colour_lut_a", "colour_lut_b", "lut_a_contribution", "lut_b_contribution", "colour_lut_a_index", "colour_lut_b_index" });
            entity_parameters.Add("ColourSettings", new string[] { "brightness", "contrast", "saturation", "red_tint", "green_tint", "blue_tint" });
            entity_parameters.Add("CompoundVolume", new string[] { "event" });
            entity_parameters.Add("ControllableRange", new string[] { "min_range_x", "max_range_x", "min_range_y", "max_range_y", "min_feather_range_x", "max_feather_range_x", "min_feather_range_y", "max_feather_range_y", "speed_x", "speed_y", "damping_x", "damping_y", "mouse_speed_x", "mouse_speed_y" });
            entity_parameters.Add("Convo", new string[] { "everyoneArrived", "playerJoined", "playerLeft", "npcJoined", "members", "speaker", "alwaysTalkToPlayerIfPresent", "playerCanJoin", "playerCanLeave", "positionNPCs", "circularShape", "convoPosition", "personalSpaceRadius" });
            entity_parameters.Add("Counter", new string[] { "on_under_limit", "on_limit", "on_over_limit", "Count", "is_limitless", "trigger_limit" });
            entity_parameters.Add("CoverExclusionArea", new string[] { "position", "half_dimensions", "exclude_cover", "exclude_vaults", "exclude_mantles", "exclude_jump_downs", "exclude_crawl_space_spotting_positions", "exclude_spotting_positions", "exclude_assault_positions" });
            entity_parameters.Add("CoverLine", new string[] { "enable_on_reset", "LinePath", "low", "resource", "LinePathPosition" });
            entity_parameters.Add("Custom_Hiding_Controller", new string[] { "Started_Idle", "Started_Exit", "Got_Out", "Prompt_1", "Prompt_2", "Start_choking", "Start_oxygen_starvation", "Show_MT", "Hide_MT", "Spawn_MT", "Despawn_MT", "Start_Busted_By_Alien", "Start_Busted_By_Android", "End_Busted_By_Android", "Start_Busted_By_Human", "End_Busted_By_Human", "Enter_Anim", "Idle_Anim", "Exit_Anim", "has_MT", "is_high", "AlienBusted_Player_1", "AlienBusted_Alien_1", "AlienBusted_Player_2", "AlienBusted_Alien_2", "AlienBusted_Player_3", "AlienBusted_Alien_3", "AlienBusted_Player_4", "AlienBusted_Alien_4", "AndroidBusted_Player_1", "AndroidBusted_Android_1", "AndroidBusted_Player_2", "AndroidBusted_Android_2", "MT_pos" });
            entity_parameters.Add("Custom_Hiding_Vignette_controller", new string[] { "StartFade", "StopFade", "Breath", "Blackout_start_time", "run_out_time", "Vignette", "FadeValue" });
            entity_parameters.Add("DayToneMappingSettings", new string[] { "black_point", "cross_over_point", "white_point", "shoulder_strength", "toe_strength", "luminance_scale" });
            entity_parameters.Add("DEBUG_SenseLevels", new string[] { "no_activation", "trace_activation", "lower_activation", "normal_activation", "upper_activation", "Sense" });
            entity_parameters.Add("DebugCamera", new string[] { "linked_cameras" });
            entity_parameters.Add("DebugCaptureCorpse", new string[] { "finished_capturing", "character", "corpse_name" });
            entity_parameters.Add("DebugCaptureScreenShot", new string[] { "finished_capturing", "wait_for_streamer", "capture_filename", "fov", "near", "far" });
            entity_parameters.Add("DebugCheckpoint", new string[] { "on_checkpoint", "section", "level_reset" });
            entity_parameters.Add("DebugEnvironmentMarker", new string[] { "target", "float_input", "int_input", "bool_input", "vector_input", "enum_input", "text", "namespace", "size", "colour", "world_pos", "duration", "scale_with_distance", "max_string_length", "scroll_speed", "show_distance_from_target", "DebugPositionMarker", "world_pos" });
            entity_parameters.Add("DebugGraph", new string[] { "Inputs", "scale", "duration", "samples_per_second", "auto_scale", "auto_scroll" });
            entity_parameters.Add("DebugLoadCheckpoint", new string[] { "previous_checkpoint" });
            entity_parameters.Add("DebugMenuToggle", new string[] { "debug_variable", "value" });
            entity_parameters.Add("DebugObjectMarker", new string[] { "marked_object", "marked_name" });
            entity_parameters.Add("DebugText", new string[] { "duration_finished", "float_input", "int_input", "bool_input", "vector_input", "enum_input", "text_input", "text", "namespace", "size", "colour", "alignment", "duration", "pause_game", "cancel_pause_with_button_press", "priority", "ci_type" });
            entity_parameters.Add("DebugTextStacking", new string[] { "float_input", "int_input", "bool_input", "vector_input", "enum_input", "text", "namespace", "size", "colour", "ci_type", "needs_debug_opt_to_render" });
            entity_parameters.Add("DeleteBlankPanel", new string[] { "door_mechanism" });
            entity_parameters.Add("DeleteButtonDisk", new string[] { "door_mechanism", "button_type" });
            entity_parameters.Add("DeleteButtonKeys", new string[] { "door_mechanism", "button_type" });
            entity_parameters.Add("DeleteCuttingPanel", new string[] { "door_mechanism" });
            entity_parameters.Add("DeleteHacking", new string[] { "door_mechanism" });
            entity_parameters.Add("DeleteHousing", new string[] { "door_mechanism", "is_door" });
            entity_parameters.Add("DeleteKeypad", new string[] { "door_mechanism" });
            entity_parameters.Add("DeletePullLever", new string[] { "door_mechanism", "lever_type" });
            entity_parameters.Add("DeleteRotateLever", new string[] { "door_mechanism", "lever_type" });
            entity_parameters.Add("DepthOfFieldSettings", new string[] { "focal_length_mm", "focal_plane_m", "fnum", "focal_point", "use_camera_target" });
            entity_parameters.Add("DespawnCharacter", new string[] { "despawned" });
            entity_parameters.Add("DespawnPlayer", new string[] { "despawned" });
            entity_parameters.Add("Display_Element_On_Map", new string[] { "map_name", "element_name" });
            entity_parameters.Add("DisplayMessage", new string[] { "title_id", "message_id" });
            entity_parameters.Add("DisplayMessageWithCallbacks", new string[] { "on_yes", "on_no", "on_cancel", "title_text", "message_text", "yes_text", "no_text", "cancel_text", "yes_button", "no_button", "cancel_button" });
            entity_parameters.Add("DistortionOverlay", new string[] { "intensity", "time", "distortion_texture", "alpha_threshold_enabled", "threshold_texture", "range", "begin_start_time", "begin_stop_time", "end_start_time", "end_stop_time" });
            entity_parameters.Add("DistortionSettings", new string[] { "radial_distort_factor", "radial_distort_constraint", "radial_distort_scalar" });
            entity_parameters.Add("Door", new string[] { "started_opening", "started_closing", "finished_opening", "finished_closing", "used_locked", "used_unlocked", "used_forced_open", "used_forced_closed", "waiting_to_open", "highlight", "unhighlight", "zone_link", "animation", "trigger_filter", "icon_pos", "icon_usable_radius", "show_icon_when_locked", "nav_mesh", "wait_point_1", "wait_point_2", "geometry", "is_scripted", "wait_to_open", "is_waiting", "unlocked_text", "locked_text", "icon_keyframe", "detach_anim", "invert_nav_mesh_barrier" });
            entity_parameters.Add("DoorStatus", new string[] { "hacking_difficulty", "door_mechanism", "gate_type", "has_correct_keycard", "cutting_tool_level", "is_locked", "is_powered", "is_cutting_complete" });
            entity_parameters.Add("DurangoVideoCapture", new string[] { "clip_name" });
            entity_parameters.Add("EFFECT_DirectionalPhysics", new string[] { "relative_direction", "effect_distance", "angular_falloff", "min_force", "max_force" });
            entity_parameters.Add("EFFECT_EntityGenerator", new string[] { "entities", "trigger_on_reset", "count", "spread", "force_min", "force_max", "force_offset_XY_min", "force_offset_XY_max", "force_offset_Z_min", "force_offset_Z_max", "lifetime_min", "lifetime_max", "use_local_rotation" });
            entity_parameters.Add("EFFECT_ImpactGenerator", new string[] { "on_impact", "on_failed", "trigger_on_reset", "min_distance", "distance", "max_count", "count", "spread", "skip_characters", "use_local_rotation" });
            entity_parameters.Add("EggSpawner", new string[] { "egg_position", "hostile_egg" });
            entity_parameters.Add("ElapsedTimer", new string[] { });
            entity_parameters.Add("EnableMotionTrackerPassiveAudio", new string[] { });
            entity_parameters.Add("EndGame", new string[] { "on_game_end_started", "on_game_ended", "success" });
            entity_parameters.Add("ENT_Debug_Exit_Game", new string[] { "FailureText", "FailureCode" });
            entity_parameters.Add("EnvironmentMap", new string[] { "Entities", "Priority", "ColourFactor", "EmissiveFactor", "Texture", "Texture_Index", "environmentmap_index" });
            entity_parameters.Add("EnvironmentModelReference", new string[] { "resource" });
            entity_parameters.Add("EQUIPPABLE_ITEM", new string[] { "finished_spawning", "equipped", "unequipped", "on_pickup", "on_discard", "on_melee_impact", "on_used_basic_function", "spawn_on_reset", "item_animated_asset", "owner", "has_owner", "character_animation_context", "character_activate_animation_context", "left_handed", "inventory_name", "equipment_slot", "holsters_on_owner", "holster_node", "holster_scale", "weapon_handedness" });
            entity_parameters.Add("ExclusiveMaster", new string[] { "active_objects", "inactive_objects", "resource" });
            entity_parameters.Add("Explosion_AINotifier", new string[] { "on_character_damage_fx", "ExplosionPos", "AmmoType" });
            entity_parameters.Add("ExternalVariableBool", new string[] { "game_variable" });
            entity_parameters.Add("FakeAILightSourceInPlayersHand", new string[] { "radius", "pos" });
            entity_parameters.Add("FilmGrainSettings", new string[] { "low_lum_amplifier", "mid_lum_amplifier", "high_lum_amplifier", "low_lum_range", "mid_lum_range", "high_lum_range", "noise_texture_scale", "adaptive", "adaptation_scalar", "adaptation_time_scalar", "unadapted_low_lum_amplifier", "unadapted_mid_lum_amplifier", "unadapted_high_lum_amplifier" });
            entity_parameters.Add("FilterAbsorber", new string[] { "output", "factor", "start_value", "input" });
            entity_parameters.Add("FilterAnd", new string[] { "filter" });
            entity_parameters.Add("FilterBelongsToAlliance", new string[] { "alliance_group" });
            entity_parameters.Add("FilterCanSeeTarget", new string[] { "Target" });
            entity_parameters.Add("FilterHasBehaviourTreeFlagSet", new string[] { "BehaviourTreeFlag" });
            entity_parameters.Add("FilterHasPlayerCollectedIdTag", new string[] { "tag_id" });
            entity_parameters.Add("FilterHasWeaponEquipped", new string[] { "weapon_type" });
            entity_parameters.Add("FilterHasWeaponOfType", new string[] { "weapon_type" });
            entity_parameters.Add("FilterIsACharacter", new string[] { });
            entity_parameters.Add("FilterIsAgressing", new string[] { "Target" });
            entity_parameters.Add("FilterIsAnySaveInProgress", new string[] { });
            entity_parameters.Add("FilterIsAPlayer", new string[] { });
            entity_parameters.Add("FilterIsCharacter", new string[] { "character" });
            entity_parameters.Add("FilterIsCharacterClass", new string[] { "character_class" });
            entity_parameters.Add("FilterIsCharacterClassCombo", new string[] { "character_classes" });
            entity_parameters.Add("FilterIsDead", new string[] { });
            entity_parameters.Add("FilterIsEnemyOfAllianceGroup", new string[] { "alliance_group" });
            entity_parameters.Add("FilterIsEnemyOfCharacter", new string[] { "Character", "use_alliance_at_death" });
            entity_parameters.Add("FilterIsEnemyOfPlayer", new string[] { });
            entity_parameters.Add("FilterIsFacingTarget", new string[] { "target", "tolerance" });
            entity_parameters.Add("FilterIsHumanNPC", new string[] { });
            entity_parameters.Add("FilterIsInAGroup", new string[] { });
            entity_parameters.Add("FilterIsInAlertnessState", new string[] { "AlertState" });
            entity_parameters.Add("FilterIsinInventory", new string[] { "ItemName" });
            entity_parameters.Add("FilterIsInLocomotionState", new string[] { "State" });
            entity_parameters.Add("FilterIsInWeaponRange", new string[] { "weapon_owner" });
            entity_parameters.Add("FilterIsLocalPlayer", new string[] { });
            entity_parameters.Add("FilterIsNotDeadManWalking", new string[] { });
            entity_parameters.Add("FilterIsObject", new string[] { "objects" });
            entity_parameters.Add("FilterIsPhysics", new string[] { });
            entity_parameters.Add("FilterIsPhysicsObject", new string[] { "object" });
            entity_parameters.Add("FilterIsPlatform", new string[] { "Platform" });
            entity_parameters.Add("FilterIsUsingDevice", new string[] { "Device" });
            entity_parameters.Add("FilterIsValidInventoryItem", new string[] { "item" });
            entity_parameters.Add("FilterIsWithdrawnAlien", new string[] { });
            entity_parameters.Add("FilterNot", new string[] { "filter" });
            entity_parameters.Add("FilterOr", new string[] { "filter" });
            entity_parameters.Add("FilterSmallestUsedDifficulty", new string[] { "difficulty" });
            entity_parameters.Add("FixedCamera", new string[] { "use_transform_position", "transform_position", "camera_position", "camera_target", "camera_position_offset", "camera_target_offset", "apply_target", "apply_position", "use_target_offset", "use_position_offset" });
            entity_parameters.Add("FlareSettings", new string[] { "flareOffset0", "flareIntensity0", "flareAttenuation0", "flareOffset1", "flareIntensity1", "flareAttenuation1", "flareOffset2", "flareIntensity2", "flareAttenuation2", "flareOffset3", "flareIntensity3", "flareAttenuation3" });
            entity_parameters.Add("FlareTask", new string[] { "specific_character", "filter_options" });
            entity_parameters.Add("FlashCallback", new string[] { "callback", "callback_name" });
            entity_parameters.Add("FlashInvoke", new string[] { "layer_name", "mrtt_texture", "method", "invoke_type", "int_argument_0", "int_argument_1", "int_argument_2", "int_argument_3", "float_argument_0", "float_argument_1", "float_argument_2", "float_argument_3" });
            entity_parameters.Add("FlashScript", new string[] { "show_on_reset", "filename", "layer_name", "target_texture_name", "type" });
            entity_parameters.Add("FloatAbsolute", new string[] { });
            entity_parameters.Add("FloatAdd", new string[] { });
            entity_parameters.Add("FloatAdd_All", new string[] { });
            entity_parameters.Add("FloatClamp", new string[] { "Min", "Max", "Value", "Result" });
            entity_parameters.Add("FloatClampMultiply", new string[] { "Min", "Max" });
            entity_parameters.Add("FloatCompare", new string[] { "on_true", "on_false", "LHS", "RHS", "Threshold", "Result" });
            entity_parameters.Add("FloatDivide", new string[] { });
            entity_parameters.Add("FloatEquals", new string[] { });
            entity_parameters.Add("FloatGetLinearProportion", new string[] { "Min", "Input", "Max", "Proportion" });
            entity_parameters.Add("FloatGreaterThan", new string[] { });
            entity_parameters.Add("FloatGreaterThanOrEqual", new string[] { });
            entity_parameters.Add("FloatLessThan", new string[] { });
            entity_parameters.Add("FloatLessThanOrEqual", new string[] { });
            entity_parameters.Add("FloatLinearInterpolateSpeed", new string[] { "on_finished", "on_think", "Result", "Initial_Value", "Target_Value", "Speed", "PingPong", "Loop" });
            entity_parameters.Add("FloatLinearInterpolateSpeedAdvanced", new string[] { "on_finished", "on_think", "trigger_on_min", "trigger_on_max", "trigger_on_loop", "Result", "Initial_Value", "Min_Value", "Max_Value", "Speed", "PingPong", "Loop" });
            entity_parameters.Add("FloatLinearInterpolateTimed", new string[] { "on_finished", "on_think", "Result", "Initial_Value", "Target_Value", "Time", "PingPong", "Loop" });
            entity_parameters.Add("FloatLinearProportion", new string[] { "Initial_Value", "Target_Value", "Proportion", "Result" });
            entity_parameters.Add("FloatMath", new string[] { "LHS", "RHS", "Result" });
            entity_parameters.Add("FloatMath_All", new string[] { "Numbers", "Result" });
            entity_parameters.Add("FloatMax", new string[] { });
            entity_parameters.Add("FloatMax_All", new string[] { });
            entity_parameters.Add("FloatMin", new string[] { });
            entity_parameters.Add("FloatMin_All", new string[] { });
            entity_parameters.Add("FloatModulate", new string[] { "on_think", "Result", "wave_shape", "frequency", "phase", "amplitude", "bias" });
            entity_parameters.Add("FloatModulateRandom", new string[] { "on_full_switched_on", "on_full_switched_off", "on_think", "Result", "switch_on_anim", "switch_on_delay", "switch_on_custom_frequency", "switch_on_duration", "switch_off_anim", "switch_off_custom_frequency", "switch_off_duration", "behaviour_anim", "behaviour_frequency", "behaviour_frequency_variance", "behaviour_offset", "pulse_modulation", "oscillate_range_min", "sparking_speed", "blink_rate", "blink_range_min", "flicker_rate", "flicker_off_rate", "flicker_range_min", "flicker_off_range_min", "disable_behaviour" });
            entity_parameters.Add("FloatMultiply", new string[] { });
            entity_parameters.Add("FloatMultiply_All", new string[] { "Invert" });
            entity_parameters.Add("FloatMultiplyClamp", new string[] { "Min", "Max" });
            entity_parameters.Add("FloatNotEqual", new string[] { });
            entity_parameters.Add("FloatOperation", new string[] { "Input", "Result" });
            entity_parameters.Add("FloatReciprocal", new string[] { });
            entity_parameters.Add("FloatRemainder", new string[] { });
            entity_parameters.Add("FloatSmoothStep", new string[] { "Low_Edge", "High_Edge", "Value", "Result" });
            entity_parameters.Add("FloatSqrt", new string[] { });
            entity_parameters.Add("FloatSubtract", new string[] { });
            entity_parameters.Add("FlushZoneCache", new string[] { "CurrentGen", "NextGen" });
            entity_parameters.Add("FogBox", new string[] { "deleted", "show_on_reset", "GEOMETRY_TYPE", "COLOUR_TINT", "DISTANCE_FADE", "ANGLE_FADE", "BILLBOARD", "EARLY_ALPHA", "LOW_RES", "CONVEX_GEOM", "THICKNESS", "START_DISTANT_CLIP", "START_DISTANCE_FADE", "SOFTNESS", "SOFTNESS_EDGE", "LINEAR_HEIGHT_DENSITY", "SMOOTH_HEIGHT_DENSITY", "HEIGHT_MAX_DENSITY", "FRESNEL_FALLOFF", "FRESNEL_POWER", "DEPTH_INTERSECT_COLOUR", "DEPTH_INTERSECT_INITIAL_COLOUR", "DEPTH_INTERSECT_INITIAL_ALPHA", "DEPTH_INTERSECT_MIDPOINT_COLOUR", "DEPTH_INTERSECT_MIDPOINT_ALPHA", "DEPTH_INTERSECT_MIDPOINT_DEPTH", "DEPTH_INTERSECT_END_COLOUR", "DEPTH_INTERSECT_END_ALPHA", "DEPTH_INTERSECT_END_DEPTH", "resource" });
            entity_parameters.Add("FogPlane", new string[] { "fog_plane_resource", "start_distance_fade_scalar", "distance_fade_scalar", "angle_fade_scalar", "linear_height_density_fresnel_power_scalar", "linear_heigth_density_max_scalar", "tint", "thickness_scalar", "edge_softness_scalar", "diffuse_0_uv_scalar", "diffuse_0_speed_scalar", "diffuse_1_uv_scalar", "diffuse_1_speed_scalar" });
            entity_parameters.Add("FogSetting", new string[] { "linear_distance", "max_distance", "linear_density", "exponential_density", "near_colour", "far_colour" });
            entity_parameters.Add("FogSphere", new string[] { "deleted", "show_on_reset", "COLOUR_TINT", "INTENSITY", "OPACITY", "EARLY_ALPHA", "LOW_RES_ALPHA", "CONVEX_GEOM", "DISABLE_SIZE_CULLING", "NO_CLIP", "ALPHA_LIGHTING", "DYNAMIC_ALPHA_LIGHTING", "DENSITY", "EXPONENTIAL_DENSITY", "SCENE_DEPENDANT_DENSITY", "FRESNEL_TERM", "FRESNEL_POWER", "SOFTNESS", "SOFTNESS_EDGE", "BLEND_ALPHA_OVER_DISTANCE", "FAR_BLEND_DISTANCE", "NEAR_BLEND_DISTANCE", "SECONDARY_BLEND_ALPHA_OVER_DISTANCE", "SECONDARY_FAR_BLEND_DISTANCE", "SECONDARY_NEAR_BLEND_DISTANCE", "DEPTH_INTERSECT_COLOUR", "DEPTH_INTERSECT_COLOUR_VALUE", "DEPTH_INTERSECT_ALPHA_VALUE", "DEPTH_INTERSECT_RANGE", "resource" });
            entity_parameters.Add("FollowCameraModifier", new string[] { "enable_on_reset", "position_curve", "target_curve", "modifier_type", "position_offset", "target_offset", "field_of_view", "force_state", "force_state_initial_value", "can_mirror", "is_first_person", "bone_blending_ratio", "movement_speed", "movement_speed_vertical", "movement_damping", "horizontal_limit_min", "horizontal_limit_max", "vertical_limit_min", "vertical_limit_max", "mouse_speed_hori", "mouse_speed_vert", "acceleration_duration", "acceleration_ease_in", "acceleration_ease_out", "transition_duration", "transition_ease_in", "transition_ease_out" });
            entity_parameters.Add("FollowTask", new string[] { "can_initially_end_early", "stop_radius" });
            entity_parameters.Add("Force_UI_Visibility", new string[] { "also_disable_interactions" });
            entity_parameters.Add("FullScreenBlurSettings", new string[] { "contribution" });
            entity_parameters.Add("FullScreenOverlay", new string[] { "overlay_texture", "threshold_value", "threshold_start", "threshold_stop", "threshold_range", "alpha_scalar" });
            entity_parameters.Add("GameDVR", new string[] { "start_time", "duration", "moment_ID" });
            entity_parameters.Add("GameOver", new string[] { "tip_string_id", "default_tips_enabled", "level_tips_enabled" });
            entity_parameters.Add("GameOverCredits", new string[] { });
            entity_parameters.Add("GameplayTip", new string[] { "string_id" });
            entity_parameters.Add("GameStateChanged", new string[] { "mission_number" });
            entity_parameters.Add("GateResourceInterface", new string[] { "gate_status_changed", "request_open_on_reset", "request_lock_on_reset", "force_open_on_reset", "force_close_on_reset", "is_auto", "auto_close_delay", "is_open", "is_locked", "gate_status" });
            entity_parameters.Add("GenericHighlightEntity", new string[] { "highlight_geometry" });
            entity_parameters.Add("GetBlueprintAvailable", new string[] { "available", "type" });
            entity_parameters.Add("GetBlueprintLevel", new string[] { "level", "type" });
            entity_parameters.Add("GetCentrePoint", new string[] { "Positions", "position_of_centre" });
            entity_parameters.Add("GetCharacterRotationSpeed", new string[] { "character", "speed" });
            entity_parameters.Add("GetClosestPercentOnSpline", new string[] { "spline", "pos_to_be_near", "position_on_spline", "Result", "bidirectional" });
            entity_parameters.Add("GetClosestPoint", new string[] { "bound_to_closest", "Positions", "pos_to_be_near", "position_of_closest" });
            entity_parameters.Add("GetClosestPointFromSet", new string[] { "closest_is_1", "closest_is_2", "closest_is_3", "closest_is_4", "closest_is_5", "closest_is_6", "closest_is_7", "closest_is_8", "closest_is_9", "closest_is_10", "Position_1", "Position_2", "Position_3", "Position_4", "Position_5", "Position_6", "Position_7", "Position_8", "Position_9", "Position_10", "pos_to_be_near", "position_of_closest", "index_of_closest" });
            entity_parameters.Add("GetClosestPointOnSpline", new string[] { "spline", "pos_to_be_near", "position_on_spline", "look_ahead_distance", "unidirectional", "directional_damping_threshold" });
            entity_parameters.Add("GetComponentInterface", new string[] { "Input", "Result" });
            entity_parameters.Add("GetCurrentCameraFov", new string[] { });
            entity_parameters.Add("GetCurrentCameraPos", new string[] { });
            entity_parameters.Add("GetCurrentCameraTarget", new string[] { "target", "distance" });
            entity_parameters.Add("GetCurrentPlaylistLevelIndex", new string[] { "index" });
            entity_parameters.Add("GetFlashFloatValue", new string[] { "callback", "enable_on_reset", "float_value", "callback_name" });
            entity_parameters.Add("GetFlashIntValue", new string[] { "callback", "enable_on_reset", "int_value", "callback_name" });
            entity_parameters.Add("GetGatingToolLevel", new string[] { "level", "tool_type" });
            entity_parameters.Add("GetInventoryItemName", new string[] { "item", "equippable_item" });
            entity_parameters.Add("GetNextPlaylistLevelName", new string[] { "level_name" });
            entity_parameters.Add("GetPlayerHasGatingTool", new string[] { "has_tool", "doesnt_have_tool", "tool_type" });
            entity_parameters.Add("GetPlayerHasKeycard", new string[] { "has_card", "doesnt_have_card", "card_uid" });
            entity_parameters.Add("GetPointOnSpline", new string[] { "spline", "percentage_of_spline", "Result" });
            entity_parameters.Add("GetRotation", new string[] { "Input", "Result" });
            entity_parameters.Add("GetSelectedCharacterId", new string[] { "character_id" });
            entity_parameters.Add("GetSplineLength", new string[] { "spline", "Result" });
            entity_parameters.Add("GetTranslation", new string[] { "Input", "Result" });
            entity_parameters.Add("GetX", new string[] { });
            entity_parameters.Add("GetY", new string[] { });
            entity_parameters.Add("GetZ", new string[] { });
            entity_parameters.Add("GlobalEvent", new string[] { "EventValue", "EventName" });
            entity_parameters.Add("GlobalEventMonitor", new string[] { "Event_1", "Event_2", "Event_3", "Event_4", "Event_5", "Event_6", "Event_7", "Event_8", "Event_9", "Event_10", "Event_11", "Event_12", "Event_13", "Event_14", "Event_15", "Event_16", "Event_17", "Event_18", "Event_19", "Event_20", "EventName" });
            entity_parameters.Add("GlobalPosition", new string[] { "PositionName" });
            entity_parameters.Add("GoToFrontend", new string[] { "frontend_state" });
            entity_parameters.Add("GPU_PFXEmitterReference", new string[] { "start_on_reset", "deleted", "mastered_by_visibility", "EFFECT_NAME", "SPAWN_NUMBER", "SPAWN_RATE", "SPREAD_MIN", "SPREAD_MAX", "EMITTER_SIZE", "SPEED", "SPEED_VAR", "LIFETIME", "LIFETIME_VAR" });
            entity_parameters.Add("HableToneMappingSettings", new string[] { "shoulder_strength", "linear_strength", "linear_angle", "toe_strength", "toe_numerator", "toe_denominator", "linear_white_point" });
            entity_parameters.Add("HackingGame", new string[] { "win", "fail", "alarm_triggered", "closed", "loaded_idle", "loaded_success", "ui_breakout_triggered", "resources_finished_unloading", "resources_finished_loading", "lock_on_reset", "light_on_reset", "completion_percentage", "hacking_difficulty", "auto_exit" });
            entity_parameters.Add("HandCamera", new string[] { "noise_type", "frequency", "damping", "rotation_intensity", "min_fov_range", "max_fov_range", "min_noise", "max_noise" });
            entity_parameters.Add("HasAccessAtDifficulty", new string[] { "difficulty" });
            entity_parameters.Add("HeldItem_AINotifier", new string[] { "Item", "Duration" });
            entity_parameters.Add("HighSpecMotionBlurSettings", new string[] { "contribution", "camera_velocity_scalar", "camera_velocity_min", "camera_velocity_max", "object_velocity_scalar", "object_velocity_min", "object_velocity_max", "blur_range" });
            entity_parameters.Add("HostOnlyTrigger", new string[] { "on_triggered" });
            entity_parameters.Add("IdleTask", new string[] { "start_pre_move", "start_interrupt", "interrupted_while_moving", "specific_character", "should_auto_move_to_position", "ignored_for_auto_selection", "has_pre_move_script", "has_interrupt_script", "filter_options" });
            entity_parameters.Add("ImpactSphere", new string[] { "event", "radius", "include_physics" });
            entity_parameters.Add("InhibitActionsUntilRelease", new string[] { });
            entity_parameters.Add("IntegerAbsolute", new string[] { });
            entity_parameters.Add("IntegerAdd", new string[] { });
            entity_parameters.Add("IntegerAdd_All", new string[] { });
            entity_parameters.Add("IntegerAnalyse", new string[] { "Input", "Result", "Val0", "Val1", "Val2", "Val3", "Val4", "Val5", "Val6", "Val7", "Val8", "Val9" });
            entity_parameters.Add("IntegerAnd", new string[] { });
            entity_parameters.Add("IntegerCompare", new string[] { "on_true", "on_false", "LHS", "RHS", "Result" });
            entity_parameters.Add("IntegerCompliment", new string[] { });
            entity_parameters.Add("IntegerDivide", new string[] { });
            entity_parameters.Add("IntegerEquals", new string[] { });
            entity_parameters.Add("IntegerGreaterThan", new string[] { });
            entity_parameters.Add("IntegerGreaterThanOrEqual", new string[] { });
            entity_parameters.Add("IntegerLessThan", new string[] { });
            entity_parameters.Add("IntegerLessThanOrEqual", new string[] { });
            entity_parameters.Add("IntegerMath", new string[] { "LHS", "RHS", "Result" });
            entity_parameters.Add("IntegerMath_All", new string[] { "Numbers", "Result" });
            entity_parameters.Add("IntegerMax", new string[] { });
            entity_parameters.Add("IntegerMax_All", new string[] { });
            entity_parameters.Add("IntegerMin", new string[] { });
            entity_parameters.Add("IntegerMin_All", new string[] { });
            entity_parameters.Add("IntegerMultiply", new string[] { });
            entity_parameters.Add("IntegerMultiply_All", new string[] { });
            entity_parameters.Add("IntegerNotEqual", new string[] { });
            entity_parameters.Add("IntegerOperation", new string[] { "Input", "Result" });
            entity_parameters.Add("IntegerOr", new string[] { });
            entity_parameters.Add("IntegerRemainder", new string[] { });
            entity_parameters.Add("IntegerSubtract", new string[] { });
            entity_parameters.Add("Interaction", new string[] { "on_damaged", "on_interrupt", "on_killed", "interruptible_on_start" });
            entity_parameters.Add("InteractiveMovementControl", new string[] { "completed", "duration", "start_time", "progress_path", "result", "speed", "can_go_both_ways", "use_left_input_stick", "base_progress_speed", "movement_threshold", "momentum_damping", "track_bone_position", "character_node", "track_position" });
            entity_parameters.Add("Internal_JOB_SearchTarget", new string[] { });
            entity_parameters.Add("InventoryItem", new string[] { "collect", "itemName", "out_itemName", "out_quantity", "item", "quantity", "clear_on_collect", "gcip_instances_count" });
            entity_parameters.Add("IrawanToneMappingSettings", new string[] { "target_device_luminance", "target_device_adaptation", "saccadic_time", "superbright_adaptation" });
            entity_parameters.Add("IsActive", new string[] { });
            entity_parameters.Add("IsAttached", new string[] { });
            entity_parameters.Add("IsCurrentLevelAChallengeMap", new string[] { "challenge_map" });
            entity_parameters.Add("IsCurrentLevelAPreorderMap", new string[] { "preorder_map" });
            entity_parameters.Add("IsEnabled", new string[] { });
            entity_parameters.Add("IsInstallComplete", new string[] { });
            entity_parameters.Add("IsLoaded", new string[] { });
            entity_parameters.Add("IsLoading", new string[] { });
            entity_parameters.Add("IsLocked", new string[] { });
            entity_parameters.Add("IsMultiplayerMode", new string[] { });
            entity_parameters.Add("IsOpen", new string[] { });
            entity_parameters.Add("IsOpening", new string[] { });
            entity_parameters.Add("IsPaused", new string[] { });
            entity_parameters.Add("IsPlaylistTypeAll", new string[] { "all" });
            entity_parameters.Add("IsPlaylistTypeMarathon", new string[] { "marathon" });
            entity_parameters.Add("IsPlaylistTypeSingle", new string[] { "single" });
            entity_parameters.Add("IsSpawned", new string[] { });
            entity_parameters.Add("IsStarted", new string[] { });
            entity_parameters.Add("IsSuspended", new string[] { });
            entity_parameters.Add("IsVisible", new string[] { });
            entity_parameters.Add("Job", new string[] { "start_on_reset" });
            entity_parameters.Add("JOB_AreaSweep", new string[] { });
            entity_parameters.Add("JOB_AreaSweepFlare", new string[] { });
            entity_parameters.Add("JOB_Assault", new string[] { });
            entity_parameters.Add("JOB_Follow", new string[] { });
            entity_parameters.Add("JOB_Follow_Centre", new string[] { });
            entity_parameters.Add("JOB_Idle", new string[] { "task_operation_mode", "should_perform_all_tasks" });
            entity_parameters.Add("JOB_Panic", new string[] { });
            entity_parameters.Add("JOB_SpottingPosition", new string[] { "SpottingPosition" });
            entity_parameters.Add("JOB_SystematicSearch", new string[] { });
            entity_parameters.Add("JOB_SystematicSearchFlare", new string[] { });
            entity_parameters.Add("JobWithPosition", new string[] { });
            entity_parameters.Add("LeaderboardWriter", new string[] { "time_elapsed", "score", "level_number", "grade", "player_character", "combat", "stealth", "improv", "star1", "star2", "star3" });
            entity_parameters.Add("LeaveGame", new string[] { "disconnect_from_session" });
            entity_parameters.Add("LensDustSettings", new string[] { "DUST_MAX_REFLECTED_BLOOM_INTENSITY", "DUST_REFLECTED_BLOOM_INTENSITY_SCALAR", "DUST_MAX_BLOOM_INTENSITY", "DUST_BLOOM_INTENSITY_SCALAR", "DUST_THRESHOLD" });
            entity_parameters.Add("LevelCompletionTargets", new string[] { "TargetTime", "NumDeaths", "TeamRespawnBonus", "NoLocalRespawnBonus", "NoRespawnBonus", "GrappleBreakBonus" });
            entity_parameters.Add("LevelInfo", new string[] { "save_level_name_id" });
            entity_parameters.Add("LevelLoaded", new string[] { });
            entity_parameters.Add("LightAdaptationSettings", new string[] { "fast_neural_t0", "slow_neural_t0", "pigment_bleaching_t0", "fb_luminance_to_candelas_per_m2", "max_adaptation_lum", "min_adaptation_lum", "adaptation_percentile", "low_bracket", "high_bracket", "adaptation_mechanism" });
            entity_parameters.Add("LightingMaster", new string[] { "light_on_reset", "objects" });
            entity_parameters.Add("LightReference", new string[] { "deleted", "show_on_reset", "light_on_reset", "occlusion_geometry", "mastered_by_visibility", "exclude_shadow_entities", "type", "defocus_attenuation", "start_attenuation", "end_attenuation", "physical_attenuation", "near_dist", "near_dist_shadow_offset", "inner_cone_angle", "outer_cone_angle", "intensity_multiplier", "radiosity_multiplier", "area_light_radius", "diffuse_softness", "diffuse_bias", "glossiness_scale", "flare_occluder_radius", "flare_spot_offset", "flare_intensity_scale", "cast_shadow", "fade_type", "is_specular", "has_lens_flare", "has_noclip", "is_square_light", "is_flash_light", "no_alphalight", "include_in_planar_reflections", "shadow_priority", "aspect_ratio", "gobo_texture", "horizontal_gobo_flip", "colour", "strip_length", "distance_mip_selection_gobo", "volume", "volume_end_attenuation", "volume_colour_factor", "volume_density", "depth_bias", "slope_scale_depth_bias", "resource" });
            entity_parameters.Add("LimitItemUse", new string[] { "enable_on_reset", "items" });
            entity_parameters.Add("LODControls", new string[] { "lod_range_scalar", "disable_lods" });
            entity_parameters.Add("Logic_MultiGate", new string[] { "Underflow", "Pin_1", "Pin_2", "Pin_3", "Pin_4", "Pin_5", "Pin_6", "Pin_7", "Pin_8", "Pin_9", "Pin_10", "Pin_11", "Pin_12", "Pin_13", "Pin_14", "Pin_15", "Pin_16", "Pin_17", "Pin_18", "Pin_19", "Pin_20", "Overflow", "trigger_pin" });
            entity_parameters.Add("Logic_Vent_Entrance", new string[] { "Hide_Pos", "Emit_Pos", "force_stand_on_exit" });
            entity_parameters.Add("Logic_Vent_System", new string[] { "Vent_Entrances" });
            entity_parameters.Add("LogicAll", new string[] { "Pin1_Synced", "Pin2_Synced", "Pin3_Synced", "Pin4_Synced", "Pin5_Synced", "Pin6_Synced", "Pin7_Synced", "Pin8_Synced", "Pin9_Synced", "Pin10_Synced", "num", "reset_on_trigger" });
            entity_parameters.Add("LogicCounter", new string[] { "on_under_limit", "on_limit", "on_over_limit", "restored_on_under_limit", "restored_on_limit", "restored_on_over_limit", "Count", "is_limitless", "trigger_limit", "non_persistent" });
            entity_parameters.Add("LogicDelay", new string[] { "on_delay_finished", "delay", "can_suspend" });
            entity_parameters.Add("LogicGate", new string[] { "on_allowed", "on_disallowed", "allow" });
            entity_parameters.Add("LogicGateAnd", new string[] { });
            entity_parameters.Add("LogicGateEquals", new string[] { });
            entity_parameters.Add("LogicGateNotEqual", new string[] { });
            entity_parameters.Add("LogicGateOr", new string[] { });
            entity_parameters.Add("LogicNot", new string[] { });
            entity_parameters.Add("LogicOnce", new string[] { "on_success", "on_failure" });
            entity_parameters.Add("LogicPressurePad", new string[] { "Pad_Activated", "Pad_Deactivated", "bound_characters", "Limit", "Count" });
            entity_parameters.Add("LogicSwitch", new string[] { "true_now_false", "false_now_true", "on_true", "on_false", "on_restored_true", "on_restored_false", "initial_value", "is_persistent" });
            entity_parameters.Add("LowResFrameCapture", new string[] { });
            entity_parameters.Add("Map_Floor_Change", new string[] { "floor_name" });
            entity_parameters.Add("MapAnchor", new string[] { "map_north", "map_pos", "map_scale", "keyframe", "keyframe1", "keyframe2", "keyframe3", "keyframe4", "keyframe5", "world_pos", "is_default_for_items" });
            entity_parameters.Add("MapItem", new string[] { "show_ui_on_reset", "item_type", "map_keyframe" });
            entity_parameters.Add("Master", new string[] { "suspend_on_reset", "objects", "disable_display", "disable_collision", "disable_simulation" });
            entity_parameters.Add("MELEE_WEAPON", new string[] { "item_animated_model_and_collision", "normal_attack_damage", "power_attack_damage", "position_input" });
            entity_parameters.Add("Minigames", new string[] { "on_success", "on_failure", "game_inertial_damping_active", "game_green_text_active", "game_yellow_chart_active", "game_overloc_fail_active", "game_docking_active", "game_environ_ctr_active", "config_pass_number", "config_fail_limit", "config_difficulty" });
            entity_parameters.Add("MissionNumber", new string[] { "on_changed" });
            entity_parameters.Add("ModelReference", new string[] { "on_damaged", "show_on_reset", "enable_on_reset", "simulate_on_reset", "light_on_reset", "convert_to_physics", "material", "occludes_atmosphere", "include_in_planar_reflections", "lod_ranges", "intensity_multiplier", "radiosity_multiplier", "emissive_tint", "replace_intensity", "replace_tint", "decal_scale", "lightdecal_tint", "lightdecal_intensity", "diffuse_colour_scale", "diffuse_opacity_scale", "vertex_colour_scale", "vertex_opacity_scale", "uv_scroll_speed_x", "uv_scroll_speed_y", "alpha_blend_noise_power_scale", "alpha_blend_noise_uv_scale", "alpha_blend_noise_uv_offset_X", "alpha_blend_noise_uv_offset_Y", "dirt_multiply_blend_spec_power_scale", "dirt_map_uv_scale", "remove_on_damaged", "damage_threshold", "is_debris", "is_prop", "is_thrown", "report_sliding", "force_keyframed", "force_transparent", "soft_collision", "allow_reposition_of_physics", "disable_size_culling", "cast_shadows", "cast_shadows_in_torch", "resource", "alpha_light_offset_x", "alpha_light_offset_y", "alpha_light_scale_x", "alpha_light_scale_y", "alpha_light_average_normal" });
            entity_parameters.Add("MonitorActionMap", new string[] { "on_pressed_use", "on_released_use", "on_pressed_crouch", "on_released_crouch", "on_pressed_run", "on_released_run", "on_pressed_aim", "on_released_aim", "on_pressed_shoot", "on_released_shoot", "on_pressed_reload", "on_released_reload", "on_pressed_melee", "on_released_melee", "on_pressed_activate_item", "on_released_activate_item", "on_pressed_switch_weapon", "on_released_switch_weapon", "on_pressed_change_dof_focus", "on_released_change_dof_focus", "on_pressed_select_motion_tracker", "on_released_select_motion_tracker", "on_pressed_select_torch", "on_released_select_torch", "on_pressed_torch_beam", "on_released_torch_beam", "on_pressed_peek", "on_released_peek", "on_pressed_back_close", "on_released_back_close", "movement_stick_x", "movement_stick_y", "camera_stick_x", "camera_stick_y", "mouse_x", "mouse_y", "analog_aim", "analog_shoot" });
            entity_parameters.Add("MonitorBase", new string[] { });
            entity_parameters.Add("MonitorPadInput", new string[] { "on_pressed_A", "on_released_A", "on_pressed_B", "on_released_B", "on_pressed_X", "on_released_X", "on_pressed_Y", "on_released_Y", "on_pressed_L1", "on_released_L1", "on_pressed_R1", "on_released_R1", "on_pressed_L2", "on_released_L2", "on_pressed_R2", "on_released_R2", "on_pressed_L3", "on_released_L3", "on_pressed_R3", "on_released_R3", "on_dpad_left", "on_released_dpad_left", "on_dpad_right", "on_released_dpad_right", "on_dpad_up", "on_released_dpad_up", "on_dpad_down", "on_released_dpad_down", "left_stick_x", "left_stick_y", "right_stick_x", "right_stick_y" });
            entity_parameters.Add("MotionTrackerMonitor", new string[] { "on_motion_sound", "on_enter_range_sound" });
            entity_parameters.Add("MotionTrackerPing", new string[] { "FakePosition" });
            entity_parameters.Add("MoveAlongSpline", new string[] { "on_think", "on_finished", "spline", "speed", "Result" });
            entity_parameters.Add("MoveInTime", new string[] { "on_finished", "start_position", "end_position", "result", "duration" });
            entity_parameters.Add("MoviePlayer", new string[] { "start", "end", "skipped", "trigger_end_on_skipped", "filename", "skippable", "enable_debug_skip" });
            entity_parameters.Add("MultipleCharacterAttachmentNode", new string[] { "attach_on_reset", "character_01", "attachment_01", "character_02", "attachment_02", "character_03", "attachment_03", "character_04", "attachment_04", "character_05", "attachment_05", "node", "use_offset", "translation", "rotation", "is_cinematic" });
            entity_parameters.Add("MultiplePickupSpawner", new string[] { "pos", "item_name" });
            entity_parameters.Add("MultitrackLoop", new string[] { "current_time", "loop_condition", "start_time", "end_time" });
            entity_parameters.Add("MusicController", new string[] { "music_start_event", "music_end_event", "music_restart_event", "layer_control_rtpc", "smooth_rate", "alien_max_distance", "object_max_distance" });
            entity_parameters.Add("MusicTrigger", new string[] { "on_triggered", "connected_object", "music_event", "smooth_rate", "queue_time", "interrupt_all", "trigger_once", "rtpc_set_mode", "rtpc_target_value", "rtpc_duration", "rtpc_set_return_mode", "rtpc_return_value" });
            entity_parameters.Add(@"n:\content\build\library\archetypes\gameplay\gcip_worldpickup", new string[] { "spawn_completed", "pickup_collected", "Pipe", "Gasoline", "Explosive", "Battery", "Blade", "Gel", "Adhesive", "BoltGun Ammo", "Revolver Ammo", "Shotgun Ammo", "BoltGun", "Revolver", "Shotgun", "Flare", "Flamer Fuel", "Flamer", "Scrap", "Torch Battery", "Torch", "Cattleprod Ammo", "Cattleprod", "StartOnReset", "MissionNumber" });
            entity_parameters.Add(@"n:\content\build\library\archetypes\script\gameplay\torch_control", new string[] { "torch_switched_off", "torch_switched_on", "character" });
            entity_parameters.Add(@"n:\content\build\library\ayz\animation\logichelpers\playforminduration", new string[] { "timer_expired", "first_animation_started", "next_animation", "all_animations_finished", "MinDuration" });
            entity_parameters.Add("NavMeshArea", new string[] { "position", "half_dimensions", "area_type" });
            entity_parameters.Add("NavMeshBarrier", new string[] { "open_on_reset", "position", "half_dimensions", "opaque", "allowed_character_classes_when_open", "allowed_character_classes_when_closed", "resource" });
            entity_parameters.Add("NavMeshExclusionArea", new string[] { "position", "half_dimensions" });
            entity_parameters.Add("NavMeshReachabilitySeedPoint", new string[] { "position" });
            entity_parameters.Add("NavMeshWalkablePlatform", new string[] { "position", "half_dimensions" });
            entity_parameters.Add("NetPlayerCounter", new string[] { "on_full", "on_empty", "on_intermediate", "is_full", "is_empty", "contains_local_player" });
            entity_parameters.Add("NetworkedTimer", new string[] { "on_second_changed", "on_started_counting", "on_finished_counting", "time_elapsed", "time_left", "time_elapsed_sec", "time_left_sec", "duration" });
            entity_parameters.Add("NonInteractiveWater", new string[] { "water_resource", "SCALE_X", "SCALE_Z", "SHININESS", "SPEED", "SCALE", "NORMAL_MAP_STRENGTH", "SECONDARY_SPEED", "SECONDARY_SCALE", "SECONDARY_NORMAL_MAP_STRENGTH", "CYCLE_TIME", "FLOW_SPEED", "FLOW_TEX_SCALE", "FLOW_WARP_STRENGTH", "FRESNEL_POWER", "MIN_FRESNEL", "MAX_FRESNEL", "ENVIRONMENT_MAP_MULT", "ENVMAP_SIZE", "ENVMAP_BOXPROJ_BB_SCALE", "REFLECTION_PERTURBATION_STRENGTH", "ALPHA_PERTURBATION_STRENGTH", "ALPHALIGHT_MULT", "softness_edge", "DEPTH_FOG_INITIAL_COLOUR", "DEPTH_FOG_INITIAL_ALPHA", "DEPTH_FOG_MIDPOINT_COLOUR", "DEPTH_FOG_MIDPOINT_ALPHA", "DEPTH_FOG_MIDPOINT_DEPTH", "DEPTH_FOG_END_COLOUR", "DEPTH_FOG_END_ALPHA", "DEPTH_FOG_END_DEPTH" });
            entity_parameters.Add("NonPersistentBool", new string[] { "initial_value" });
            entity_parameters.Add("NonPersistentInt", new string[] { "initial_value", "is_persistent" });
            entity_parameters.Add("NPC_Aggression_Monitor", new string[] { "on_interrogative", "on_warning", "on_last_chance", "on_stand_down", "on_idle", "on_aggressive" });
            entity_parameters.Add("NPC_AlienConfig", new string[] { "AlienConfigString" });
            entity_parameters.Add("NPC_AllSensesLimiter", new string[] { });
            entity_parameters.Add("NPC_ambush_monitor", new string[] { "setup", "abandoned", "trap_sprung", "ambush_type", "trigger_on_start", "trigger_on_checkpoint_restart" });
            entity_parameters.Add("NPC_AreaBox", new string[] { "half_dimensions", "position" });
            entity_parameters.Add("NPC_behaviour_monitor", new string[] { "state_set", "state_unset", "behaviour", "trigger_on_start", "trigger_on_checkpoint_restart" });
            entity_parameters.Add("NPC_ClearDefendArea", new string[] { });
            entity_parameters.Add("NPC_ClearPursuitArea", new string[] { });
            entity_parameters.Add("NPC_Coordinator", new string[] { "Target", "trigger_on_start", "CheckAllNPCs" });
            entity_parameters.Add("NPC_Debug_Menu_Item", new string[] { "character" });
            entity_parameters.Add("NPC_DefineBackstageAvoidanceArea", new string[] { "AreaObjects" });
            entity_parameters.Add("NPC_DynamicDialogue", new string[] { });
            entity_parameters.Add("NPC_DynamicDialogueGlobalRange", new string[] { "dialogue_range" });
            entity_parameters.Add("NPC_FakeSense", new string[] { "SensedObject", "FakePosition", "Sense", "ForceThreshold" });
            entity_parameters.Add("NPC_FollowOffset", new string[] { "offset", "target_to_follow", "Result" });
            entity_parameters.Add("NPC_ForceCombatTarget", new string[] { "Target", "LockOtherAttackersOut" });
            entity_parameters.Add("NPC_ForceNextJob", new string[] { "job_started", "job_completed", "job_interrupted", "ShouldInterruptCurrentTask", "Job", "InitialTask" });
            entity_parameters.Add("NPC_ForceRetreat", new string[] { "AreaObjects" });
            entity_parameters.Add("NPC_Gain_Aggression_In_Radius", new string[] { "Position", "Radius", "AggressionGain" });
            entity_parameters.Add("NPC_GetCombatTarget", new string[] { "bound_trigger", "target" });
            entity_parameters.Add("NPC_GetLastSensedPositionOfTarget", new string[] { "NoRecentSense", "SensedOnLeft", "SensedOnRight", "SensedInFront", "SensedBehind", "OptionalTarget", "LastSensedPosition", "MaxTimeSince" });
            entity_parameters.Add("NPC_Group_Death_Monitor", new string[] { "last_man_dying", "all_killed", "squad_coordinator", "CheckAllNPCs" });
            entity_parameters.Add("NPC_Group_DeathCounter", new string[] { "on_threshold", "TriggerThreshold" });
            entity_parameters.Add("NPC_Highest_Awareness_Monitor", new string[] { "All_Dead", "Stunned", "Unaware", "Suspicious", "SearchingArea", "SearchingLastSensed", "Aware", "on_changed" });
            entity_parameters.Add("NPC_MeleeContext", new string[] { "ConvergePos", "Radius", "Context_Type" });
            entity_parameters.Add("NPC_multi_behaviour_monitor", new string[] { "Cinematic_set", "Cinematic_unset", "Damage_Response_set", "Damage_Response_unset", "Target_Is_NPC_set", "Target_Is_NPC_unset", "Breakout_set", "Breakout_unset", "Attack_set", "Attack_unset", "Stunned_set", "Stunned_unset", "Backstage_set", "Backstage_unset", "In_Vent_set", "In_Vent_unset", "Killtrap_set", "Killtrap_unset", "Threat_Aware_set", "Threat_Aware_unset", "Suspect_Target_Response_set", "Suspect_Target_Response_unset", "Player_Hiding_set", "Player_Hiding_unset", "Suspicious_Item_set", "Suspicious_Item_unset", "Search_set", "Search_unset", "Area_Sweep_set", "Area_Sweep_unset", "trigger_on_start", "trigger_on_checkpoint_restart" });
            entity_parameters.Add("NPC_navmesh_type_monitor", new string[] { "state_set", "state_unset", "nav_mesh_type", "trigger_on_start", "trigger_on_checkpoint_restart" });
            entity_parameters.Add("NPC_NotifyDynamicDialogueEvent", new string[] { "DialogueEvent" });
            entity_parameters.Add("NPC_Once", new string[] { "on_success", "on_failure" });
            entity_parameters.Add("NPC_ResetFiringStats", new string[] { });
            entity_parameters.Add("NPC_ResetSensesAndMemory", new string[] { "ResetMenaceToFull", "ResetSensesLimiters" });
            entity_parameters.Add("NPC_SenseLimiter", new string[] { "Sense" });
            entity_parameters.Add("NPC_set_behaviour_tree_flags", new string[] { "BehaviourTreeFlag", "FlagSetting" });
            entity_parameters.Add("NPC_SetAgressionProgression", new string[] { "allow_progression" });
            entity_parameters.Add("NPC_SetAimTarget", new string[] { "Target" });
            entity_parameters.Add("NPC_SetAlertness", new string[] { "AlertState" });
            entity_parameters.Add("NPC_SetAlienDevelopmentStage", new string[] { "AlienStage", "Reset" });
            entity_parameters.Add("NPC_SetAutoTorchMode", new string[] { "AutoUseTorchInDark" });
            entity_parameters.Add("NPC_SetChokePoint", new string[] { "chokepoints" });
            entity_parameters.Add("NPC_SetDefendArea", new string[] { "AreaObjects" });
            entity_parameters.Add("NPC_SetFiringAccuracy", new string[] { "Accuracy" });
            entity_parameters.Add("NPC_SetFiringRhythm", new string[] { "MinShootingTime", "RandomRangeShootingTime", "MinNonShootingTime", "RandomRangeNonShootingTime", "MinCoverNonShootingTime", "RandomRangeCoverNonShootingTime" });
            entity_parameters.Add("NPC_SetGunAimMode", new string[] { "AimingMode" });
            entity_parameters.Add("NPC_SetHidingNearestLocation", new string[] { "hiding_pos" });
            entity_parameters.Add("NPC_SetHidingSearchRadius", new string[] { "Radius" });
            entity_parameters.Add("NPC_SetInvisible", new string[] { });
            entity_parameters.Add("NPC_SetLocomotionStyleForJobs", new string[] { });
            entity_parameters.Add("NPC_SetLocomotionTargetSpeed", new string[] { "Speed" });
            entity_parameters.Add("NPC_SetPursuitArea", new string[] { "AreaObjects" });
            entity_parameters.Add("NPC_SetRateOfFire", new string[] { "MinTimeBetweenShots", "RandomRange" });
            entity_parameters.Add("NPC_SetSafePoint", new string[] { "SafePositions" });
            entity_parameters.Add("NPC_SetSenseSet", new string[] { "SenseSet" });
            entity_parameters.Add("NPC_SetStartPos", new string[] { "StartPos" });
            entity_parameters.Add("NPC_SetTotallyBlindInDark", new string[] { });
            entity_parameters.Add("NPC_SetupMenaceManager", new string[] { "AgressiveMenace", "ProgressionFraction", "ResetMenaceMeter" });
            entity_parameters.Add("NPC_Sleeping_Android_Monitor", new string[] { "Twitch", "SitUp_Start", "SitUp_End", "Sleeping_GetUp", "Sitting_GetUp", "Android_NPC" });
            entity_parameters.Add("NPC_Squad_DialogueMonitor", new string[] { "Suspicious_Item_Initial", "Suspicious_Item_Close", "Suspicious_Warning", "Suspicious_Warning_Fail", "Missing_Buddy", "Search_Started", "Search_Loop", "Search_Complete", "Detected_Enemy", "Alien_Heard_Backstage", "Interrogative", "Warning", "Last_Chance", "Stand_Down", "Attack", "Advance", "Melee", "Hit_By_Weapon", "Go_to_Cover", "No_Cover", "Shoot_From_Cover", "Cover_Broken", "Retreat", "Panic", "Final_Hit", "Ally_Death", "Incoming_IED", "Alert_Squad", "My_Death", "Idle_Passive", "Idle_Aggressive", "Block", "Enter_Grapple", "Grapple_From_Cover", "Player_Observed", "squad_coordinator" });
            entity_parameters.Add("NPC_Squad_GetAwarenessState", new string[] { "All_Dead", "Stunned", "Unaware", "Suspicious", "SearchingArea", "SearchingLastSensed", "Aware" });
            entity_parameters.Add("NPC_Squad_GetAwarenessWatermark", new string[] { "All_Dead", "Stunned", "Unaware", "Suspicious", "SearchingArea", "SearchingLastSensed", "Aware" });
            entity_parameters.Add("NPC_StopAiming", new string[] { });
            entity_parameters.Add("NPC_StopShooting", new string[] { });
            entity_parameters.Add("NPC_SuspiciousItem", new string[] { "ItemPosition", "Item", "InitialReactionValidStartDuration", "FurtherReactionValidStartDuration", "RetriggerDelay", "Trigger", "ShouldMakeAggressive", "MaxGroupMembersInteract", "SystematicSearchRadius", "AllowSamePriorityToOveride", "UseSamePriorityCloserDistanceConstraint", "SamePriorityCloserDistanceConstraint", "UseSamePriorityRecentTimeConstraint", "SamePriorityRecentTimeConstraint", "BehaviourTreePriority", "InteruptSubPriority", "DetectableByBackstageAlien", "DoIntialReaction", "MoveCloseToSuspectPosition", "DoCloseToReaction", "DoCloseToWaitForGroupMembers", "DoSystematicSearch", "GroupNotify", "DoIntialReactionSubsequentGroupMember", "MoveCloseToSuspectPositionSubsequentGroupMember", "DoCloseToReactionSubsequentGroupMember", "DoCloseToWaitForGroupMembersSubsequentGroupMember", "DoSystematicSearchSubsequentGroupMember" });
            entity_parameters.Add("NPC_TargetAcquire", new string[] { "no_targets" });
            entity_parameters.Add("NPC_TriggerAimRequest", new string[] { "started_aiming", "finished_aiming", "interrupted", "AimTarget", "Raise_gun", "use_current_target", "duration", "clamp_angle", "clear_current_requests" });
            entity_parameters.Add("NPC_TriggerShootRequest", new string[] { "started_shooting", "finished_shooting", "interrupted", "empty_current_clip", "shot_count", "duration", "clear_current_requests" });
            entity_parameters.Add("NPC_WithdrawAlien", new string[] { "allow_any_searches_to_complete", "permanent", "killtraps", "initial_radius", "timed_out_radius", "time_to_force" });
            entity_parameters.Add("NumConnectedPlayers", new string[] { "on_count_changed", "count" });
            entity_parameters.Add("NumDeadPlayers", new string[] { });
            entity_parameters.Add("NumPlayersOnStart", new string[] { "count" });
            entity_parameters.Add("PadLightBar", new string[] { "colour" });
            entity_parameters.Add("PadRumbleImpulse", new string[] { "low_frequency_rumble", "high_frequency_rumble", "left_trigger_impulse", "right_trigger_impulse", "aim_trigger_impulse", "shoot_trigger_impulse" });
            entity_parameters.Add("ParticipatingPlayersList", new string[] { });
            entity_parameters.Add("ParticleEmitterReference", new string[] { "start_on_reset", "show_on_reset", "deleted", "mastered_by_visibility", "use_local_rotation", "include_in_planar_reflections", "material", "unique_material", "quality_level", "bounds_max", "bounds_min", "TEXTURE_MAP", "DRAW_PASS", "ASPECT_RATIO", "FADE_AT_DISTANCE", "PARTICLE_COUNT", "SYSTEM_EXPIRY_TIME", "SIZE_START_MIN", "SIZE_START_MAX", "SIZE_END_MIN", "SIZE_END_MAX", "ALPHA_IN", "ALPHA_OUT", "MASK_AMOUNT_MIN", "MASK_AMOUNT_MAX", "MASK_AMOUNT_MIDPOINT", "PARTICLE_EXPIRY_TIME_MIN", "PARTICLE_EXPIRY_TIME_MAX", "COLOUR_SCALE_MIN", "COLOUR_SCALE_MAX", "WIND_X", "WIND_Y", "WIND_Z", "ALPHA_REF_VALUE", "BILLBOARDING_LS", "BILLBOARDING", "BILLBOARDING_NONE", "BILLBOARDING_ON_AXIS_X", "BILLBOARDING_ON_AXIS_Y", "BILLBOARDING_ON_AXIS_Z", "BILLBOARDING_VELOCITY_ALIGNED", "BILLBOARDING_VELOCITY_STRETCHED", "BILLBOARDING_SPHERE_PROJECTION", "BLENDING_STANDARD", "BLENDING_ALPHA_REF", "BLENDING_ADDITIVE", "BLENDING_PREMULTIPLIED", "BLENDING_DISTORTION", "LOW_RES", "EARLY_ALPHA", "LOOPING", "ANIMATED_ALPHA", "NONE", "LIGHTING", "PER_PARTICLE_LIGHTING", "X_AXIS_FLIP", "Y_AXIS_FLIP", "BILLBOARD_FACING", "BILLBOARDING_ON_AXIS_FADEOUT", "BILLBOARDING_CAMERA_LOCKED", "CAMERA_RELATIVE_POS_X", "CAMERA_RELATIVE_POS_Y", "CAMERA_RELATIVE_POS_Z", "SPHERE_PROJECTION_RADIUS", "DISTORTION_STRENGTH", "SCALE_MODIFIER", "CPU", "SPAWN_RATE", "SPAWN_RATE_VAR", "SPAWN_NUMBER", "LIFETIME", "LIFETIME_VAR", "WORLD_TO_LOCAL_BLEND_START", "WORLD_TO_LOCAL_BLEND_END", "WORLD_TO_LOCAL_MAX_DIST", "CELL_EMISSION", "CELL_MAX_DIST", "CUSTOM_SEED_CPU", "SEED", "ALPHA_TEST", "ZTEST", "START_MID_END_SPEED", "SPEED_START_MIN", "SPEED_START_MAX", "SPEED_MID_MIN", "SPEED_MID_MAX", "SPEED_END_MIN", "SPEED_END_MAX", "LAUNCH_DECELERATE_SPEED", "LAUNCH_DECELERATE_SPEED_START_MIN", "LAUNCH_DECELERATE_SPEED_START_MAX", "LAUNCH_DECELERATE_DEC_RATE", "EMISSION_AREA", "EMISSION_AREA_X", "EMISSION_AREA_Y", "EMISSION_AREA_Z", "EMISSION_SURFACE", "EMISSION_DIRECTION_SURFACE", "AREA_CUBOID", "AREA_SPHEROID", "AREA_CYLINDER", "PIVOT_X", "PIVOT_Y", "GRAVITY", "GRAVITY_STRENGTH", "GRAVITY_MAX_STRENGTH", "COLOUR_TINT", "COLOUR_TINT_START", "COLOUR_TINT_END", "COLOUR_USE_MID", "COLOUR_TINT_MID", "COLOUR_MIDPOINT", "SPREAD_FEATURE", "SPREAD_MIN", "SPREAD", "ROTATION", "ROTATION_MIN", "ROTATION_MAX", "ROTATION_RANDOM_START", "ROTATION_BASE", "ROTATION_VAR", "ROTATION_RAMP", "ROTATION_IN", "ROTATION_OUT", "ROTATION_DAMP", "FADE_NEAR_CAMERA", "FADE_NEAR_CAMERA_MAX_DIST", "FADE_NEAR_CAMERA_THRESHOLD", "TEXTURE_ANIMATION", "TEXTURE_ANIMATION_FRAMES", "NUM_ROWS", "TEXTURE_ANIMATION_LOOP_COUNT", "RANDOM_START_FRAME", "WRAP_FRAMES", "NO_ANIM", "SUB_FRAME_BLEND", "SOFTNESS", "SOFTNESS_EDGE", "SOFTNESS_ALPHA_THICKNESS", "SOFTNESS_ALPHA_DEPTH_MODIFIER", "REVERSE_SOFTNESS", "REVERSE_SOFTNESS_EDGE", "PIVOT_AND_TURBULENCE", "PIVOT_OFFSET_MIN", "PIVOT_OFFSET_MAX", "TURBULENCE_FREQUENCY_MIN", "TURBULENCE_FREQUENCY_MAX", "TURBULENCE_AMOUNT_MIN", "TURBULENCE_AMOUNT_MAX", "ALPHATHRESHOLD", "ALPHATHRESHOLD_TOTALTIME", "ALPHATHRESHOLD_RANGE", "ALPHATHRESHOLD_BEGINSTART", "ALPHATHRESHOLD_BEGINSTOP", "ALPHATHRESHOLD_ENDSTART", "ALPHATHRESHOLD_ENDSTOP", "COLOUR_RAMP", "COLOUR_RAMP_MAP", "COLOUR_RAMP_ALPHA", "DEPTH_FADE_AXIS", "DEPTH_FADE_AXIS_DIST", "DEPTH_FADE_AXIS_PERCENT", "FLOW_UV_ANIMATION", "FLOW_MAP", "FLOW_TEXTURE_MAP", "CYCLE_TIME", "FLOW_SPEED", "FLOW_TEX_SCALE", "FLOW_WARP_STRENGTH", "INFINITE_PROJECTION", "PARALLAX_POSITION", "DISTORTION_OCCLUSION", "AMBIENT_LIGHTING", "AMBIENT_LIGHTING_COLOUR", "NO_CLIP", "resource" });
            entity_parameters.Add("PathfindingAlienBackstageNode", new string[] { "started_animating_Entry", "stopped_animating_Entry", "started_animating_Exit", "stopped_animating_Exit", "killtrap_anim_started", "killtrap_anim_stopped", "killtrap_fx_start", "killtrap_fx_stop", "on_loaded", "open_on_reset", "PlayAnimData_Entry", "PlayAnimData_Exit", "Killtrap_alien", "Killtrap_victim", "build_into_navmesh", "position", "top", "extra_cost", "network_id" });
            entity_parameters.Add("PathfindingManualNode", new string[] { "character_arriving", "character_stopped", "started_animating", "stopped_animating", "on_loaded", "PlayAnimData", "destination", "build_into_navmesh", "position", "extra_cost", "character_classes" });
            entity_parameters.Add("PathfindingTeleportNode", new string[] { "started_teleporting", "stopped_teleporting", "destination", "build_into_navmesh", "position", "extra_cost", "character_classes" });
            entity_parameters.Add("PathfindingWaitNode", new string[] { "character_getting_near", "character_arriving", "character_stopped", "started_waiting", "stopped_waiting", "destination", "build_into_navmesh", "position", "extra_cost", "character_classes" });
            entity_parameters.Add("Persistent_TriggerRandomSequence", new string[] { "Random_1", "Random_2", "Random_3", "Random_4", "Random_5", "Random_6", "Random_7", "Random_8", "Random_9", "Random_10", "All_triggered", "current", "num" });
            entity_parameters.Add("PhysicsApplyBuoyancy", new string[] { "objects", "water_height", "water_density", "water_viscosity", "water_choppiness" });
            entity_parameters.Add("PhysicsApplyImpulse", new string[] { "objects", "offset", "direction", "force", "can_damage" });
            entity_parameters.Add("PhysicsApplyVelocity", new string[] { "objects", "angular_velocity", "linear_velocity", "propulsion_velocity" });
            entity_parameters.Add("PhysicsModifyGravity", new string[] { "float_on_reset", "objects" });
            entity_parameters.Add("PhysicsSystem", new string[] { "system_index" });
            entity_parameters.Add("PickupSpawner", new string[] { "collect", "spawn_on_reset", "pos", "item_name", "item_quantity" });
            entity_parameters.Add("Planet", new string[] { "planet_resource", "parallax_position", "sun_position", "light_shaft_source_position", "parallax_scale", "planet_sort_key", "overbright_scalar", "light_wrap_angle_scalar", "penumbra_falloff_power_scalar", "lens_flare_brightness", "lens_flare_colour", "atmosphere_edge_falloff_power", "atmosphere_edge_transparency", "atmosphere_scroll_speed", "atmosphere_detail_scroll_speed", "override_global_tint", "global_tint", "flow_cycle_time", "flow_speed", "flow_tex_scale", "flow_warp_strength", "detail_uv_scale", "normal_uv_scale", "terrain_uv_scale", "atmosphere_normal_strength", "terrain_normal_strength", "light_shaft_colour", "light_shaft_range", "light_shaft_decay", "light_shaft_min_occlusion_distance", "light_shaft_intensity", "light_shaft_density", "light_shaft_source_occlusion", "blocks_light_shafts" });
            entity_parameters.Add("PlatformConstantBool", new string[] { "NextGen", "X360", "PS3" });
            entity_parameters.Add("PlatformConstantFloat", new string[] { "NextGen", "X360", "PS3" });
            entity_parameters.Add("PlatformConstantInt", new string[] { "NextGen", "X360", "PS3" });
            entity_parameters.Add("PlayEnvironmentAnimation", new string[] { "on_finished", "on_finished_streaming", "play_on_reset", "jump_to_the_end_on_play", "geometry", "marker", "external_start_time", "external_time", "animation_length", "animation_info", "AnimationSet", "Animation", "start_frame", "end_frame", "play_speed", "loop", "is_cinematic", "shot_number" });
            entity_parameters.Add("Player_ExploitableArea", new string[] { "NpcSafePositions" });
            entity_parameters.Add("Player_Sensor", new string[] { "Standard", "Running", "Aiming", "Vent", "Grapple", "Death", "Cover", "Motion_Tracked", "Motion_Tracked_Vent", "Leaning" });
            entity_parameters.Add("PlayerCamera", new string[] { });
            entity_parameters.Add("PlayerCameraMonitor", new string[] { "AndroidNeckSnap", "AlienKill", "AlienKillBroken", "AlienKillInVent", "StandardAnimDrivenView", "StopNonStandardCameras" });
            entity_parameters.Add("PlayerCampaignDeaths", new string[] { });
            entity_parameters.Add("PlayerCampaignDeathsInARow", new string[] { });
            entity_parameters.Add("PlayerDeathCounter", new string[] { "on_limit", "above_limit", "filter", "count", "Limit" });
            entity_parameters.Add("PlayerDiscardsItems", new string[] { "discard_ieds", "discard_medikits", "discard_ammo", "discard_flares_and_lights", "discard_materials", "discard_batteries" });
            entity_parameters.Add("PlayerDiscardsTools", new string[] { "discard_motion_tracker", "discard_cutting_torch", "discard_hacking_tool", "discard_keycard" });
            entity_parameters.Add("PlayerDiscardsWeapons", new string[] { "discard_pistol", "discard_shotgun", "discard_flamethrower", "discard_boltgun", "discard_cattleprod", "discard_melee" });
            entity_parameters.Add("PlayerHasEnoughItems", new string[] { "items", "quantity" });
            entity_parameters.Add("PlayerHasItem", new string[] { "items" });
            entity_parameters.Add("PlayerHasItemEntity", new string[] { "success", "fail", "items" });
            entity_parameters.Add("PlayerHasItemWithName", new string[] { "item_name" });
            entity_parameters.Add("PlayerHasSpaceForItem", new string[] { "items" });
            entity_parameters.Add("PlayerKilledAllyMonitor", new string[] { "ally_killed", "start_on_reset" });
            entity_parameters.Add("PlayerLightProbe", new string[] { "output", "light_level_for_ai", "dark_threshold", "fully_lit_threshold" });
            entity_parameters.Add("PlayerTorch", new string[] { "requested_torch_holster", "requested_torch_draw", "start_on_reset", "power_in_current_battery", "battery_count" });
            entity_parameters.Add("PlayerTriggerBox", new string[] { "on_entered", "on_exited", "enable_on_reset", "half_dimensions" });
            entity_parameters.Add("PlayerUseTriggerBox", new string[] { "on_entered", "on_exited", "on_use", "enable_on_reset", "half_dimensions", "text" });
            entity_parameters.Add("PlayerWeaponMonitor", new string[] { "on_clip_above_percentage", "on_clip_below_percentage", "on_clip_empty", "on_clip_full", "weapon_type", "ammo_percentage_in_clip" });
            entity_parameters.Add("PointAt", new string[] { "origin", "target", "Result" });
            entity_parameters.Add("PointTracker", new string[] { "origin", "target", "target_offset", "result", "origin_offset", "max_speed", "damping_factor" });
            entity_parameters.Add("PopupMessage", new string[] { "display", "finished", "header_text", "main_text", "duration", "sound_event", "icon_keyframe" });
            entity_parameters.Add("PositionDistance", new string[] { "LHS", "RHS", "Result" });
            entity_parameters.Add("PositionMarker", new string[] { });
            entity_parameters.Add("PostprocessingSettings", new string[] { "intensity", "priority", "blend_mode" });
            entity_parameters.Add("ProjectileMotion", new string[] { "on_think", "on_finished", "start_pos", "start_velocity", "duration", "Current_Position", "Current_Velocity" });
            entity_parameters.Add("ProjectileMotionComplex", new string[] { "on_think", "on_finished", "start_position", "start_velocity", "start_angular_velocity", "flight_time_in_seconds", "current_position", "current_velocity", "current_angular_velocity", "current_flight_time_in_seconds" });
            entity_parameters.Add("ProjectiveDecal", new string[] { "deleted", "show_on_reset", "time", "include_in_planar_reflections", "material", "resource" });
            entity_parameters.Add("ProximityDetector", new string[] { "in_proximity", "filter", "detector_position", "min_distance", "max_distance", "requires_line_of_sight", "proximity_duration" });
            entity_parameters.Add("ProximityTrigger", new string[] { "ignited", "electrified", "drenched", "poisoned", "fire_spread_rate", "water_permeate_rate", "electrical_conduction_rate", "gas_diffusion_rate", "ignition_range", "electrical_arc_range", "water_flow_range", "gas_dispersion_range" });
            entity_parameters.Add("QueryGCItemPool", new string[] { "count", "item_name", "item_quantity" });
            entity_parameters.Add("RadiosityIsland", new string[] { "composites", "exclusions" });
            entity_parameters.Add("RadiosityProxy", new string[] { "position", "resource" });
            entity_parameters.Add("RandomBool", new string[] { "Result" });
            entity_parameters.Add("RandomFloat", new string[] { "Result", "Min", "Max" });
            entity_parameters.Add("RandomInt", new string[] { "Result", "Min", "Max" });
            entity_parameters.Add("RandomObjectSelector", new string[] { "objects", "chosen_object" });
            entity_parameters.Add("RandomSelect", new string[] { "Input", "Result", "Seed" });
            entity_parameters.Add("RandomVector", new string[] { "Result", "MinX", "MaxX", "MinY", "MaxY", "MinZ", "MaxZ", "Normalised" });
            entity_parameters.Add("Raycast", new string[] { "Obstructed", "Unobstructed", "OutOfRange", "source_position", "target_position", "max_distance", "hit_object", "hit_distance", "hit_position", "priority" });
            entity_parameters.Add("Refraction", new string[] { "refraction_resource", "SCALE_X", "SCALE_Z", "DISTANCEFACTOR", "REFRACTFACTOR", "SPEED", "SCALE", "SECONDARY_REFRACTFACTOR", "SECONDARY_SPEED", "SECONDARY_SCALE", "MIN_OCCLUSION_DISTANCE", "CYCLE_TIME", "FLOW_SPEED", "FLOW_TEX_SCALE", "FLOW_WARP_STRENGTH" });
            entity_parameters.Add("RegisterCharacterModel", new string[] { "display_model", "reference_skeleton" });
            entity_parameters.Add("RemoveFromGCItemPool", new string[] { "on_success", "on_failure", "item_name", "item_quantity", "gcip_instances_to_remove" });
            entity_parameters.Add("RemoveFromInventory", new string[] { "success", "fail", "items" });
            entity_parameters.Add("RemoveWeaponsFromPlayer", new string[] { });
            entity_parameters.Add("RespawnConfig", new string[] { "min_dist", "preferred_dist", "max_dist", "respawn_mode", "respawn_wait_time", "uncollidable_time", "is_default" });
            entity_parameters.Add("RespawnExcluder", new string[] { "excluded_points" });
            entity_parameters.Add("ReTransformer", new string[] { "new_transform", "result" });
            entity_parameters.Add("Rewire", new string[] { "closed", "locations", "access_points", "map_keyframe", "total_power" });
            entity_parameters.Add("RewireAccess_Point", new string[] { "closed", "ui_breakout_triggered", "interactive_locations", "visible_locations", "additional_power", "display_name", "map_element_name", "map_name", "map_x_offset", "map_y_offset", "map_zoom" });
            entity_parameters.Add("RewireLocation", new string[] { "power_draw_increased", "power_draw_reduced", "systems", "element_name", "display_name" });
            entity_parameters.Add("RewireSystem", new string[] { "on", "off", "world_pos", "display_name", "display_name_enum", "on_by_default", "running_cost", "system_type", "map_name", "element_name" });
            entity_parameters.Add("RewireTotalPowerResource", new string[] { "total_power" });
            entity_parameters.Add("RibbonEmitterReference", new string[] { "deleted", "start_on_reset", "show_on_reset", "mastered_by_visibility", "use_local_rotation", "include_in_planar_reflections", "material", "unique_material", "quality_level", "BLENDING_STANDARD", "BLENDING_ALPHA_REF", "BLENDING_ADDITIVE", "BLENDING_PREMULTIPLIED", "BLENDING_DISTORTION", "NO_MIPS", "UV_SQUARED", "LOW_RES", "LIGHTING", "MASK_AMOUNT_MIN", "MASK_AMOUNT_MAX", "MASK_AMOUNT_MIDPOINT", "DRAW_PASS", "SYSTEM_EXPIRY_TIME", "LIFETIME", "SMOOTHED", "WORLD_TO_LOCAL_BLEND_START", "WORLD_TO_LOCAL_BLEND_END", "WORLD_TO_LOCAL_MAX_DIST", "TEXTURE", "TEXTURE_MAP", "UV_REPEAT", "UV_SCROLLSPEED", "MULTI_TEXTURE", "U2_SCALE", "V2_REPEAT", "V2_SCROLLSPEED", "MULTI_TEXTURE_BLEND", "MULTI_TEXTURE_ADD", "MULTI_TEXTURE_MULT", "MULTI_TEXTURE_MAX", "MULTI_TEXTURE_MIN", "SECOND_TEXTURE", "TEXTURE_MAP2", "CONTINUOUS", "BASE_LOCKED", "SPAWN_RATE", "TRAILING", "INSTANT", "RATE", "TRAIL_SPAWN_RATE", "TRAIL_DELAY", "MAX_TRAILS", "POINT_TO_POINT", "TARGET_POINT_POSITION", "DENSITY", "ABS_FADE_IN_0", "ABS_FADE_IN_1", "FORCES", "GRAVITY_STRENGTH", "GRAVITY_MAX_STRENGTH", "DRAG_STRENGTH", "WIND_X", "WIND_Y", "WIND_Z", "START_MID_END_SPEED", "SPEED_START_MIN", "SPEED_START_MAX", "WIDTH", "WIDTH_START", "WIDTH_MID", "WIDTH_END", "WIDTH_IN", "WIDTH_OUT", "COLOUR_TINT", "COLOUR_SCALE_START", "COLOUR_SCALE_MID", "COLOUR_SCALE_END", "COLOUR_TINT_START", "COLOUR_TINT_MID", "COLOUR_TINT_END", "ALPHA_FADE", "FADE_IN", "FADE_OUT", "EDGE_FADE", "ALPHA_ERODE", "SIDE_ON_FADE", "SIDE_FADE_START", "SIDE_FADE_END", "DISTANCE_SCALING", "DIST_SCALE", "SPREAD_FEATURE", "SPREAD_MIN", "SPREAD", "EMISSION_AREA", "EMISSION_AREA_X", "EMISSION_AREA_Y", "EMISSION_AREA_Z", "AREA_CUBOID", "AREA_SPHEROID", "AREA_CYLINDER", "COLOUR_RAMP", "COLOUR_RAMP_MAP", "SOFTNESS", "SOFTNESS_EDGE", "SOFTNESS_ALPHA_THICKNESS", "SOFTNESS_ALPHA_DEPTH_MODIFIER", "AMBIENT_LIGHTING", "AMBIENT_LIGHTING_COLOUR", "NO_CLIP", "resource" });
            entity_parameters.Add("RotateAtSpeed", new string[] { "on_finished", "on_think", "start_pos", "origin", "timer", "Result", "duration", "speed_X", "speed_Y", "speed_Z", "loop" });
            entity_parameters.Add("RotateInTime", new string[] { "on_finished", "on_think", "start_pos", "origin", "timer", "Result", "duration", "time_X", "time_Y", "time_Z", "loop" });
            entity_parameters.Add("RTT_MoviePlayer", new string[] { "start", "end", "show_on_reset", "filename", "layer_name", "target_texture_name" });
            entity_parameters.Add("SaveGlobalProgression", new string[] { });
            entity_parameters.Add("SaveManagers", new string[] { });
            entity_parameters.Add("ScalarProduct", new string[] { "LHS", "RHS", "Result" });
            entity_parameters.Add("ScreenEffectEventMonitor", new string[] { "MeleeHit", "BulletHit", "MedkitHeal", "StartStrangle", "StopStrangle", "StartLowHealth", "StopLowHealth", "StartDeath", "StopDeath", "AcidHit", "FlashbangHit", "HitAndRun", "CancelHitAndRun" });
            entity_parameters.Add("ScreenFadeIn", new string[] { "fade_value" });
            entity_parameters.Add("ScreenFadeInTimed", new string[] { "on_finished", "time" });
            entity_parameters.Add("ScreenFadeOutToBlack", new string[] { "fade_value" });
            entity_parameters.Add("ScreenFadeOutToBlackTimed", new string[] { "on_finished", "time" });
            entity_parameters.Add("ScreenFadeOutToWhite", new string[] { "fade_value" });
            entity_parameters.Add("ScreenFadeOutToWhiteTimed", new string[] { "on_finished", "time" });
            entity_parameters.Add("SetAsActiveMissionLevel", new string[] { "clear_level" });
            entity_parameters.Add("SetBlueprintInfo", new string[] { "type", "level", "available" });
            entity_parameters.Add("SetBool", new string[] { });
            entity_parameters.Add("SetColour", new string[] { "Colour", "Result" });
            entity_parameters.Add("SetEnum", new string[] { "Output", "initial_value" });
            entity_parameters.Add("SetFloat", new string[] { });
            entity_parameters.Add("SetGamepadAxes", new string[] { "invert_x", "invert_y", "save_settings" });
            entity_parameters.Add("SetGameplayTips", new string[] { "tip_string_id" });
            entity_parameters.Add("SetGatingToolLevel", new string[] { "level", "tool_type" });
            entity_parameters.Add("SetHackingToolLevel", new string[] { "level" });
            entity_parameters.Add("SetInteger", new string[] { });
            entity_parameters.Add("SetLocationAndOrientation", new string[] { "location", "axis", "local_offset", "result", "axis_is" });
            entity_parameters.Add("SetMotionTrackerRange", new string[] { "range" });
            entity_parameters.Add("SetNextLoadingMovie", new string[] { "playlist_to_load" });
            entity_parameters.Add("SetObject", new string[] { "Input", "Output" });
            entity_parameters.Add("SetObjectiveCompleted", new string[] { "objective_id" });
            entity_parameters.Add("SetPlayerHasGatingTool", new string[] { "tool_type" });
            entity_parameters.Add("SetPlayerHasKeycard", new string[] { "card_uid" });
            entity_parameters.Add("SetPosition", new string[] { "Translation", "Rotation", "Input", "Result", "set_on_reset" });
            entity_parameters.Add("SetPrimaryObjective", new string[] { "title", "additional_info", "title_list", "additional_info_list", "show_message" });
            entity_parameters.Add("SetRichPresence", new string[] { "presence_id", "mission_number" });
            entity_parameters.Add("SetString", new string[] { "Output", "initial_value", "SetEnumString" });
            entity_parameters.Add("SetSubObjective", new string[] { "target_position", "title", "map_description", "title_list", "map_description_list", "slot_number", "objective_type", "show_message" });
            entity_parameters.Add("SetupGCDistribution", new string[] { "c00", "c01", "c02", "c03", "c04", "c05", "c06", "c07", "c08", "c09", "c10", "minimum_multiplier", "divisor", "lookup_decrease_time", "lookup_point_increase" });
            entity_parameters.Add("SetVector", new string[] { "x", "y", "z", "Result" });
            entity_parameters.Add("SetVector2", new string[] { "Input", "Result" });
            entity_parameters.Add("SharpnessSettings", new string[] { "local_contrast_factor" });
            entity_parameters.Add("Showlevel_Completed", new string[] { });
            entity_parameters.Add("SimpleRefraction", new string[] { "deleted", "show_on_reset", "DISTANCEFACTOR", "NORMAL_MAP", "SPEED", "SCALE", "REFRACTFACTOR", "SECONDARY_NORMAL_MAPPING", "SECONDARY_NORMAL_MAP", "SECONDARY_SPEED", "SECONDARY_SCALE", "SECONDARY_REFRACTFACTOR", "ALPHA_MASKING", "ALPHA_MASK", "DISTORTION_OCCLUSION", "MIN_OCCLUSION_DISTANCE", "FLOW_UV_ANIMATION", "FLOW_MAP", "CYCLE_TIME", "FLOW_SPEED", "FLOW_TEX_SCALE", "FLOW_WARP_STRENGTH", "resource" });
            entity_parameters.Add("SimpleWater", new string[] { "deleted", "show_on_reset", "SHININESS", "softness_edge", "FRESNEL_POWER", "MIN_FRESNEL", "MAX_FRESNEL", "LOW_RES_ALPHA_PASS", "ATMOSPHERIC_FOGGING", "NORMAL_MAP", "SPEED", "SCALE", "NORMAL_MAP_STRENGTH", "SECONDARY_NORMAL_MAPPING", "SECONDARY_SPEED", "SECONDARY_SCALE", "SECONDARY_NORMAL_MAP_STRENGTH", "ALPHA_MASKING", "ALPHA_MASK", "FLOW_MAPPING", "FLOW_MAP", "CYCLE_TIME", "FLOW_SPEED", "FLOW_TEX_SCALE", "FLOW_WARP_STRENGTH", "ENVIRONMENT_MAPPING", "ENVIRONMENT_MAP", "ENVIRONMENT_MAP_MULT", "LOCALISED_ENVIRONMENT_MAPPING", "ENVMAP_SIZE", "LOCALISED_ENVMAP_BOX_PROJECTION", "ENVMAP_BOXPROJ_BB_SCALE", "REFLECTIVE_MAPPING", "REFLECTION_PERTURBATION_STRENGTH", "DEPTH_FOG_INITIAL_COLOUR", "DEPTH_FOG_INITIAL_ALPHA", "DEPTH_FOG_MIDPOINT_COLOUR", "DEPTH_FOG_MIDPOINT_ALPHA", "DEPTH_FOG_MIDPOINT_DEPTH", "DEPTH_FOG_END_COLOUR", "DEPTH_FOG_END_ALPHA", "DEPTH_FOG_END_DEPTH", "CAUSTIC_TEXTURE", "CAUSTIC_TEXTURE_SCALE", "CAUSTIC_REFRACTIONS", "CAUSTIC_REFLECTIONS", "CAUSTIC_SPEED_SCALAR", "CAUSTIC_INTENSITY", "CAUSTIC_SURFACE_WRAP", "CAUSTIC_HEIGHT", "resource", "CAUSTIC_TEXTURE_INDEX" });
            entity_parameters.Add("SmokeCylinder", new string[] { "pos", "radius", "height", "duration" });
            entity_parameters.Add("SmokeCylinderAttachmentInterface", new string[] { "radius", "height", "duration" });
            entity_parameters.Add("SmoothMove", new string[] { "on_finished", "timer", "start_position", "end_position", "start_velocity", "end_velocity", "result", "duration" });
            entity_parameters.Add("Sound", new string[] { "stop_event", "is_static_ambience", "start_on", "multi_trigger", "use_multi_emitter", "create_sound_object", "position", "switch_name", "switch_value", "last_gen_enabled", "resume_after_suspended" });
            entity_parameters.Add("SoundBarrier", new string[] { "default_open", "position", "half_dimensions", "band_aid", "override_value", "resource" });
            entity_parameters.Add("SoundEnvironmentMarker", new string[] { "reverb_name", "on_enter_event", "on_exit_event", "linked_network_occlusion_scaler", "room_size", "disable_network_creation", "position" });
            entity_parameters.Add("SoundEnvironmentZone", new string[] { "reverb_name", "priority", "position", "half_dimensions" });
            entity_parameters.Add("SoundImpact", new string[] { "sound_event", "is_occludable", "argument_1", "argument_2", "argument_3" });
            entity_parameters.Add("SoundLevelInitialiser", new string[] { "auto_generate_networks", "network_node_min_spacing", "network_node_max_visibility", "network_node_ceiling_height" });
            entity_parameters.Add("SoundLoadBank", new string[] { "bank_loaded", "sound_bank", "trigger_via_pin", "memory_pool" });
            entity_parameters.Add("SoundLoadSlot", new string[] { "bank_loaded", "sound_bank", "memory_pool" });
            entity_parameters.Add("SoundMissionInitialiser", new string[] { "human_max_threat", "android_max_threat", "alien_max_threat" });
            entity_parameters.Add("SoundNetworkNode", new string[] { "position" });
            entity_parameters.Add("SoundObject", new string[] { });
            entity_parameters.Add("SoundPhysicsInitialiser", new string[] { "contact_max_timeout", "contact_smoothing_attack_rate", "contact_smoothing_decay_rate", "contact_min_magnitude", "contact_max_trigger_distance", "impact_min_speed", "impact_max_trigger_distance", "ragdoll_min_timeout", "ragdoll_min_speed" });
            entity_parameters.Add("SoundPlaybackBaseClass", new string[] { "on_finished", "attached_sound_object", "sound_event", "is_occludable", "argument_1", "argument_2", "argument_3", "argument_4", "argument_5", "namespace", "object_position", "restore_on_checkpoint" });
            entity_parameters.Add("SoundPlayerFootwearOverride", new string[] { "footwear_sound" });
            entity_parameters.Add("SoundRTPCController", new string[] { "stealth_default_on", "threat_default_on" });
            entity_parameters.Add("SoundSetRTPC", new string[] { "rtpc_value", "sound_object", "rtpc_name", "smooth_rate", "start_on" });
            entity_parameters.Add("SoundSetState", new string[] { "state_name", "state_value" });
            entity_parameters.Add("SoundSetSwitch", new string[] { "sound_object", "switch_name", "switch_value" });
            entity_parameters.Add("SoundSpline", new string[] { });
            entity_parameters.Add("SoundTimelineTrigger", new string[] { "sound_event", "trigger_time" });
            entity_parameters.Add("SpaceSuitVisor", new string[] { "breath_level" });
            entity_parameters.Add("SpaceTransform", new string[] { "affected_geometry", "yaw_speed", "pitch_speed", "roll_speed" });
            entity_parameters.Add("SpawnGroup", new string[] { "on_spawn_request", "default_group", "trigger_on_reset" });
            entity_parameters.Add("Speech", new string[] { "on_speech_started", "character", "alt_character", "speech_priority", "queue_time" });
            entity_parameters.Add("SpeechScript", new string[] { "on_script_ended", "character_01", "character_02", "character_03", "character_04", "character_05", "alt_character_01", "alt_character_02", "alt_character_03", "alt_character_04", "alt_character_05", "speech_priority", "is_occludable", "line_01_event", "line_01_character", "line_02_delay", "line_02_event", "line_02_character", "line_03_delay", "line_03_event", "line_03_character", "line_04_delay", "line_04_event", "line_04_character", "line_05_delay", "line_05_event", "line_05_character", "line_06_delay", "line_06_event", "line_06_character", "line_07_delay", "line_07_event", "line_07_character", "line_08_delay", "line_08_event", "line_08_character", "line_09_delay", "line_09_event", "line_09_character", "line_10_delay", "line_10_event", "line_10_character", "restore_on_checkpoint" });
            entity_parameters.Add("Sphere", new string[] { "event", "enable_on_reset", "radius", "include_physics" });
            entity_parameters.Add("SplineDistanceLerp", new string[] { "on_think", "spline", "lerp_position", "Result" });
            entity_parameters.Add("SplinePath", new string[] { "loop", "orientated", "points" });
            entity_parameters.Add("SpottingExclusionArea", new string[] { "position", "half_dimensions" });
            entity_parameters.Add("Squad_SetMaxEscalationLevel", new string[] { "max_level", "squad_coordinator" });
            entity_parameters.Add("StartNewChapter", new string[] { "chapter" });
            entity_parameters.Add("StateQuery", new string[] { "on_true", "on_false", "Input", "Result" });
            entity_parameters.Add("StealCamera", new string[] { "on_converged", "focus_position", "steal_type", "check_line_of_sight", "blend_in_duration" });
            entity_parameters.Add("StreamingMonitor", new string[] { "on_loaded" });
            entity_parameters.Add("SurfaceEffectBox", new string[] { "deleted", "show_on_reset", "COLOUR_TINT", "COLOUR_TINT_OUTER", "INTENSITY", "OPACITY", "FADE_OUT_TIME", "SURFACE_WRAP", "ROUGHNESS_SCALE", "SPARKLE_SCALE", "METAL_STYLE_REFLECTIONS", "SHININESS_OPACITY", "TILING_ZY", "TILING_ZX", "TILING_XY", "FALLOFF", "WS_LOCKED", "TEXTURE_MAP", "SPARKLE_MAP", "ENVMAP", "ENVIRONMENT_MAP", "ENVMAP_PERCENT_EMISSIVE", "SPHERE", "BOX", "resource" });
            entity_parameters.Add("SurfaceEffectSphere", new string[] { "deleted", "show_on_reset", "COLOUR_TINT", "COLOUR_TINT_OUTER", "INTENSITY", "OPACITY", "FADE_OUT_TIME", "SURFACE_WRAP", "ROUGHNESS_SCALE", "SPARKLE_SCALE", "METAL_STYLE_REFLECTIONS", "SHININESS_OPACITY", "TILING_ZY", "TILING_ZX", "TILING_XY", "WS_LOCKED", "TEXTURE_MAP", "SPARKLE_MAP", "ENVMAP", "ENVIRONMENT_MAP", "ENVMAP_PERCENT_EMISSIVE", "SPHERE", "resource" });
            entity_parameters.Add("SwitchLevel", new string[] { "level_name" });
            entity_parameters.Add("SyncOnAllPlayers", new string[] { "on_synchronized", "on_synchronized_host" });
            entity_parameters.Add("SyncOnFirstPlayer", new string[] { "on_synchronized", "on_synchronized_host", "on_synchronized_local" });
            entity_parameters.Add("Task", new string[] { "start_command", "selected_by_npc", "clean_up", "start_on_reset", "Job", "TaskPosition", "filter", "should_stop_moving_when_reached", "should_orientate_when_reached", "reached_distance_threshold", "selection_priority", "timeout", "always_on_tracker" });
            entity_parameters.Add("TerminalContent", new string[] { "selected", "content_title", "content_decoration_title", "additional_info", "is_connected_to_audio_log", "is_triggerable", "is_single_use" });
            entity_parameters.Add("TerminalFolder", new string[] { "code_success", "code_fail", "selected", "lock_on_reset", "content0", "content1", "code", "folder_title", "folder_lock_type" });
            entity_parameters.Add("Thinker", new string[] { "on_think", "delay_between_triggers", "is_continuous", "use_random_start", "random_start_delay", "total_thinking_time" });
            entity_parameters.Add("ThinkOnce", new string[] { "on_think", "start_on_reset", "use_random_start", "random_start_delay" });
            entity_parameters.Add("ThrowingPointOfImpact", new string[] { "show_point_of_impact", "hide_point_of_impact", "Location", "Visible" });
            entity_parameters.Add("ToggleFunctionality", new string[] { });
            entity_parameters.Add("TogglePlayerTorch", new string[] { });
            entity_parameters.Add("TorchDynamicMovement", new string[] { "start_on_reset", "torch", "max_spatial_velocity", "max_angular_velocity", "max_position_displacement", "max_target_displacement", "position_damping", "target_damping" });
            entity_parameters.Add("TRAV_1ShotClimbUnder", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "character_classes" });
            entity_parameters.Add("TRAV_1ShotFloorVentEntrance", new string[] { "OnEnter", "Completed", "enable_on_reset", "LinePath", "character_classes", "resource" });
            entity_parameters.Add("TRAV_1ShotFloorVentExit", new string[] { "OnExit", "Completed", "enable_on_reset", "LinePath", "character_classes", "resource" });
            entity_parameters.Add("TRAV_1ShotLeap", new string[] { "OnEnter", "OnExit", "OnSuccess", "OnFailure", "enable_on_reset", "StartEdgeLinePath", "EndEdgeLinePath", "InUse", "MissDistance", "NearMissDistance", "character_classes" });
            entity_parameters.Add("TRAV_1ShotSpline", new string[] { "OnEnter", "OnExit", "enable_on_reset", "open_on_reset", "EntrancePath", "ExitPath", "MinimumPath", "MaximumPath", "MinimumSupport", "MaximumSupport", "template", "headroom", "extra_cost", "fit_end_to_edge", "min_speed", "max_speed", "animationTree", "character_classes", "resource" });
            entity_parameters.Add("TRAV_1ShotVentEntrance", new string[] { "OnEnter", "Completed", "enable_on_reset", "LinePath", "character_classes", "resource" });
            entity_parameters.Add("TRAV_1ShotVentExit", new string[] { "OnExit", "Completed", "enable_on_reset", "LinePath", "character_classes", "resource" });
            entity_parameters.Add("TRAV_ContinuousBalanceBeam", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "character_classes" });
            entity_parameters.Add("TRAV_ContinuousCinematicSidle", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "character_classes" });
            entity_parameters.Add("TRAV_ContinuousClimbingWall", new string[] { "OnEnter", "OnExit", "LinePath", "InUse", "Dangling", "character_classes" });
            entity_parameters.Add("TRAV_ContinuousLadder", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "RungSpacing", "character_classes" });
            entity_parameters.Add("TRAV_ContinuousLedge", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "Dangling", "Sidling", "character_classes" });
            entity_parameters.Add("TRAV_ContinuousPipe", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "character_classes" });
            entity_parameters.Add("TRAV_ContinuousTightGap", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "character_classes" });
            entity_parameters.Add("Trigger_AudioOccluded", new string[] { "NotOccluded", "Occluded", "position", "Range" });
            entity_parameters.Add("TriggerBindAllCharactersOfType", new string[] { "bound_trigger", "character_class" });
            entity_parameters.Add("TriggerBindAllNPCs", new string[] { "npc_inside", "npc_outside", "filter", "centre", "radius" });
            entity_parameters.Add("TriggerBindCharacter", new string[] { "bound_trigger", "characters" });
            entity_parameters.Add("TriggerBindCharactersInSquad", new string[] { "bound_trigger" });
            entity_parameters.Add("TriggerCameraViewCone", new string[] { "enter", "exit", "target", "fov", "aspect_ratio", "intersect_with_geometry", "use_camera_fov", "target_offset", "visible_area_type", "visible_area_horizontal", "visible_area_vertical", "raycast_grace" });
            entity_parameters.Add("TriggerCameraViewConeMulti", new string[] { "enter", "exit", "enter1", "exit1", "enter2", "exit2", "enter3", "exit3", "enter4", "exit4", "enter5", "exit5", "enter6", "exit6", "enter7", "exit7", "enter8", "exit8", "enter9", "exit9", "target", "target1", "target2", "target3", "target4", "target5", "target6", "target7", "target8", "target9", "fov", "aspect_ratio", "intersect_with_geometry", "number_of_inputs", "use_camera_fov", "visible_area_type", "visible_area_horizontal", "visible_area_vertical", "raycast_grace" });
            entity_parameters.Add("TriggerCameraVolume", new string[] { "inside", "enter", "exit", "inside_factor", "lookat_factor", "lookat_X_position", "lookat_Y_position", "start_radius", "radius" });
            entity_parameters.Add("TriggerCheckDifficulty", new string[] { "on_success", "on_failure", "DifficultyLevel" });
            entity_parameters.Add("TriggerContainerObjectsFilterCounter", new string[] { "none_passed", "some_passed", "all_passed", "filter", "container" });
            entity_parameters.Add("TriggerDamaged", new string[] { "on_damaged", "enable_on_reset", "physics_object", "impact_normal", "threshold" });
            entity_parameters.Add("TriggerDelay", new string[] { "delayed_trigger", "purged_trigger", "time_left", "Hrs", "Min", "Sec" });
            entity_parameters.Add("TriggerExtractBoundCharacter", new string[] { "unbound_trigger", "bound_character" });
            entity_parameters.Add("TriggerExtractBoundObject", new string[] { "unbound_trigger", "bound_object" });
            entity_parameters.Add("TriggerFilter", new string[] { "on_success", "on_failure", "filter" });
            entity_parameters.Add("TriggerLooper", new string[] { "target", "count", "delay" });
            entity_parameters.Add("TriggerObjectsFilter", new string[] { "on_success", "on_failure", "filter", "objects" });
            entity_parameters.Add("TriggerObjectsFilterCounter", new string[] { "none_passed", "some_passed", "all_passed", "objects", "filter" });
            entity_parameters.Add("TriggerRandom", new string[] { "Random_1", "Random_2", "Random_3", "Random_4", "Random_5", "Random_6", "Random_7", "Random_8", "Random_9", "Random_10", "Random_11", "Random_12", "Num" });
            entity_parameters.Add("TriggerRandomSequence", new string[] { "Random_1", "Random_2", "Random_3", "Random_4", "Random_5", "Random_6", "Random_7", "Random_8", "Random_9", "Random_10", "All_triggered", "current", "num" });
            entity_parameters.Add("TriggerSelect", new string[] { "Pin_0", "Pin_1", "Pin_2", "Pin_3", "Pin_4", "Pin_5", "Pin_6", "Pin_7", "Pin_8", "Pin_9", "Pin_10", "Pin_11", "Pin_12", "Pin_13", "Pin_14", "Pin_15", "Pin_16", "Object_0", "Object_1", "Object_2", "Object_3", "Object_4", "Object_5", "Object_6", "Object_7", "Object_8", "Object_9", "Object_10", "Object_11", "Object_12", "Object_13", "Object_14", "Object_15", "Object_16", "Result", "index" });
            entity_parameters.Add("TriggerSelect_Direct", new string[] { "Changed_to_0", "Changed_to_1", "Changed_to_2", "Changed_to_3", "Changed_to_4", "Changed_to_5", "Changed_to_6", "Changed_to_7", "Changed_to_8", "Changed_to_9", "Changed_to_10", "Changed_to_11", "Changed_to_12", "Changed_to_13", "Changed_to_14", "Changed_to_15", "Changed_to_16", "Object_0", "Object_1", "Object_2", "Object_3", "Object_4", "Object_5", "Object_6", "Object_7", "Object_8", "Object_9", "Object_10", "Object_11", "Object_12", "Object_13", "Object_14", "Object_15", "Object_16", "Result", "TriggeredIndex", "Changes_only" });
            entity_parameters.Add("TriggerSequence", new string[] { "proxy_enable_on_reset", "attach_on_reset", "duration", "trigger_mode", "random_seed", "use_random_intervals", "no_duplicates", "interval_multiplier" });
            entity_parameters.Add("TriggerSimple", new string[] { });
            entity_parameters.Add("TriggerSwitch", new string[] { "Pin_1", "Pin_2", "Pin_3", "Pin_4", "Pin_5", "Pin_6", "Pin_7", "Pin_8", "Pin_9", "Pin_10", "current", "num", "loop" });
            entity_parameters.Add("TriggerSync", new string[] { "Pin1_Synced", "Pin2_Synced", "Pin3_Synced", "Pin4_Synced", "Pin5_Synced", "Pin6_Synced", "Pin7_Synced", "Pin8_Synced", "Pin9_Synced", "Pin10_Synced", "reset_on_trigger" });
            entity_parameters.Add("TriggerTouch", new string[] { "touch_event", "enable_on_reset", "physics_object", "impact_normal" });
            entity_parameters.Add("TriggerUnbindCharacter", new string[] { "unbound_trigger" });
            entity_parameters.Add("TriggerViewCone", new string[] { "enter", "exit", "target_is_visible", "no_target_visible", "target", "fov", "max_distance", "aspect_ratio", "source_position", "filter", "intersect_with_geometry", "visible_target", "target_offset", "visible_area_type", "visible_area_horizontal", "visible_area_vertical", "raycast_grace" });
            entity_parameters.Add("TriggerVolumeFilter", new string[] { "on_event_entered", "on_event_exited", "filter" });
            entity_parameters.Add("TriggerVolumeFilter_Monitored", new string[] { "on_event_entered", "on_event_exited", "filter" });
            entity_parameters.Add("TriggerWeightedRandom", new string[] { "Random_1", "Random_2", "Random_3", "Random_4", "Random_5", "Random_6", "Random_7", "Random_8", "Random_9", "Random_10", "current", "Weighting_01", "Weighting_02", "Weighting_03", "Weighting_04", "Weighting_05", "Weighting_06", "Weighting_07", "Weighting_08", "Weighting_09", "Weighting_10", "allow_same_pin_in_succession" });
            entity_parameters.Add("TriggerWhenSeeTarget", new string[] { "seen", "Target" });
            entity_parameters.Add("TutorialMessage", new string[] { "text", "text_list", "show_animation" });
            entity_parameters.Add("UI_Attached", new string[] { "closed", "ui_icon" });
            entity_parameters.Add("UI_Container", new string[] { "take_slot", "emptied", "contents", "has_been_used", "is_persistent", "is_temporary" });
            entity_parameters.Add("UI_Icon", new string[] { "start", "start_fail", "button_released", "broadcasted_start", "highlight", "unhighlight", "lock_looked_at", "lock_interaction", "lock_on_reset", "enable_on_reset", "show_on_reset", "geometry", "highlight_geometry", "target_pickup_item", "highlight_distance_threshold", "interaction_distance_threshold", "icon_user", "unlocked_text", "locked_text", "action_text", "icon_keyframe", "can_be_used", "category", "push_hold_time" });
            entity_parameters.Add("UI_KeyGate", new string[] { "keycard_success", "keycode_success", "keycard_fail", "keycode_fail", "keycard_cancelled", "keycode_cancelled", "ui_breakout_triggered", "lock_on_reset", "light_on_reset", "code", "carduid", "key_type" });
            entity_parameters.Add("UI_Keypad", new string[] { "success", "fail", "code", "exit_on_fail" });
            entity_parameters.Add("UI_ReactionGame", new string[] { "success", "fail", "stage0_success", "stage0_fail", "stage1_success", "stage1_fail", "stage2_success", "stage2_fail", "ui_breakout_triggered", "resources_finished_unloading", "resources_finished_loading", "completion_percentage", "exit_on_fail" });
            entity_parameters.Add("UIBreathingGameIcon", new string[] { "fill_percentage", "prompt_text" });
            entity_parameters.Add("UiSelectionBox", new string[] { "is_priority" });
            entity_parameters.Add("UiSelectionSphere", new string[] { "is_priority" });
            entity_parameters.Add("UnlockAchievement", new string[] { "achievement_id" });
            entity_parameters.Add("UnlockLogEntry", new string[] { "entry" });
            entity_parameters.Add("UnlockMapDetail", new string[] { "map_keyframe", "details" });
            entity_parameters.Add("UpdateGlobalPosition", new string[] { "PositionName" });
            entity_parameters.Add("UpdateLeaderBoardDisplay", new string[] { "time" });
            entity_parameters.Add("UpdatePrimaryObjective", new string[] { "show_message", "clear_objective" });
            entity_parameters.Add("UpdateSubObjective", new string[] { "slot_number", "show_message", "clear_objective" });
            entity_parameters.Add("VariableAnimationInfo", new string[] { "AnimationSet", "Animation" });
            entity_parameters.Add("VariableBool", new string[] { "initial_value", "is_persistent" });
            entity_parameters.Add("VariableColour", new string[] { "initial_colour" });
            entity_parameters.Add("VariableEnum", new string[] { "initial_value", "is_persistent", "VariableEnumString" });
            entity_parameters.Add("VariableFilterObject", new string[] { });
            entity_parameters.Add("VariableFlashScreenColour", new string[] { "start_on_reset", "pause_on_reset", "initial_colour", "flash_layer_name" });
            entity_parameters.Add("VariableFloat", new string[] { "initial_value", "is_persistent" });
            entity_parameters.Add("VariableHackingConfig", new string[] { "nodes", "sensors", "victory_nodes", "victory_sensors" });
            entity_parameters.Add("VariableInt", new string[] { "initial_value", "is_persistent" });
            entity_parameters.Add("VariableObject", new string[] { "initial" });
            entity_parameters.Add("VariablePosition", new string[] { });
            entity_parameters.Add("VariableString", new string[] { "initial_value", "is_persistent" });
            entity_parameters.Add("VariableThePlayer", new string[] { });
            entity_parameters.Add("VariableTriggerObject", new string[] { });
            entity_parameters.Add("VariableVector", new string[] { "initial_x", "initial_y", "initial_z" });
            entity_parameters.Add("VariableVector2", new string[] { "initial_value" });
            entity_parameters.Add("VectorAdd", new string[] { });
            entity_parameters.Add("VectorDirection", new string[] { "From", "To", "Result" });
            entity_parameters.Add("VectorDistance", new string[] { "LHS", "RHS", "Result" });
            entity_parameters.Add("VectorLinearInterpolateSpeed", new string[] { "on_finished", "on_think", "Initial_Value", "Target_Value", "Reverse", "Result", "Speed", "PingPong", "Loop" });
            entity_parameters.Add("VectorLinearInterpolateTimed", new string[] { "on_finished", "on_think", "Initial_Value", "Target_Value", "Reverse", "Result", "Time", "PingPong", "Loop" });
            entity_parameters.Add("VectorLinearProportion", new string[] { "Initial_Value", "Target_Value", "Proportion", "Result" });
            entity_parameters.Add("VectorMath", new string[] { "LHS", "RHS", "Result" });
            entity_parameters.Add("VectorModulus", new string[] { "Input", "Result" });
            entity_parameters.Add("VectorMultiply", new string[] { });
            entity_parameters.Add("VectorMultiplyByPos", new string[] { "Vector", "WorldPos", "Result" });
            entity_parameters.Add("VectorNormalise", new string[] { "Input", "Result" });
            entity_parameters.Add("VectorProduct", new string[] { });
            entity_parameters.Add("VectorReflect", new string[] { "Input", "Normal", "Result" });
            entity_parameters.Add("VectorRotateByPos", new string[] { "Vector", "WorldPos", "Result" });
            entity_parameters.Add("VectorRotatePitch", new string[] { "Vector", "Pitch", "Result" });
            entity_parameters.Add("VectorRotateRoll", new string[] { "Vector", "Roll", "Result" });
            entity_parameters.Add("VectorRotateYaw", new string[] { "Vector", "Yaw", "Result" });
            entity_parameters.Add("VectorScale", new string[] { "LHS", "RHS", "Result" });
            entity_parameters.Add("VectorSubtract", new string[] { });
            entity_parameters.Add("VectorYaw", new string[] { "Vector", "Result" });
            entity_parameters.Add("VideoCapture", new string[] { "clip_name", "only_in_capture_mode" });
            entity_parameters.Add("VignetteSettings", new string[] { "vignette_factor", "vignette_chromatic_aberration_scale" });
            entity_parameters.Add("VisibilityMaster", new string[] { "renderable", "mastered_by_visibility" });
            entity_parameters.Add("Weapon_AINotifier", new string[] { });
            entity_parameters.Add("WEAPON_AmmoTypeFilter", new string[] { "passed", "failed", "AmmoType" });
            entity_parameters.Add("WEAPON_AttackerFilter", new string[] { "passed", "failed", "filter" });
            entity_parameters.Add("WEAPON_DamageFilter", new string[] { "passed", "failed", "damage_threshold", "WEAPON_DidHitSomethingFilter", "passed", "failed" });
            entity_parameters.Add("WEAPON_Effect", new string[] { "WorldPos", "AttachedEffects", "UnattachedEffects", "LifeTime" });
            entity_parameters.Add("WEAPON_GiveToCharacter", new string[] { "Character", "Weapon", "is_holstered" });
            entity_parameters.Add("WEAPON_GiveToPlayer", new string[] { "weapon", "holster", "starting_ammo" });
            entity_parameters.Add("WEAPON_ImpactAngleFilter", new string[] { "greater", "less", "ReferenceAngle" });
            entity_parameters.Add("WEAPON_ImpactCharacterFilter", new string[] { "passed", "failed", "character_classes", "character_body_location" });
            entity_parameters.Add("WEAPON_ImpactEffect", new string[] { "StaticEffects", "DynamicEffects", "DynamicAttachedEffects", "Type", "Orientation", "Priority", "SafeDistant", "LifeTime", "character_damage_offset", "RandomRotation" });
            entity_parameters.Add("WEAPON_ImpactFilter", new string[] { "passed", "failed", "PhysicMaterial" });
            entity_parameters.Add("WEAPON_ImpactInspector", new string[] { "damage", "impact_position", "impact_target" });
            entity_parameters.Add("WEAPON_ImpactOrientationFilter", new string[] { "passed", "failed", "ThresholdAngle", "Orientation" });
            entity_parameters.Add("WEAPON_MultiFilter", new string[] { "passed", "failed", "AttackerFilter", "TargetFilter", "DamageThreshold", "DamageType", "UseAmmoFilter", "AmmoType" });
            entity_parameters.Add("WEAPON_TargetObjectFilter", new string[] { "passed", "failed", "filter" });
            entity_parameters.Add("Zone", new string[] { "composites", "suspend_on_unload", "space_visible" });
            entity_parameters.Add("ZoneExclusionLink", new string[] { "ZoneA", "ZoneB", "exclude_streaming" });
            entity_parameters.Add("ZoneLink", new string[] { "ZoneA", "ZoneB", "cost" });
            entity_parameters.Add("ZoneLoaded", new string[] { "on_loaded", "on_unloaded" });
        }

        private static List<ShortGuidDescriptor> cathode_id_map;
        private static List<EnumDescriptor> cathode_enum_map;
        private static List<ShortGuidDescriptor> entity_friendly_names;
        private static Dictionary<string, string[]> entity_parameters = new Dictionary<string, string[]>();
    }
}
