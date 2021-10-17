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
    public class NodeDBDescriptor
    {
        public cGUID ID;
    }
    public class ShortGUIDDescriptor : NodeDBDescriptor
    {
        public string Description;
    }
    public class EnumDescriptor : NodeDBDescriptor
    {
        public string Name;
        public List<string> Entries = new List<string>();
    }

    public class NodeDB
    {
        static NodeDB()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            cathode_id_map = ReadDB(File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_generic_lut.bin")).Cast<ShortGUIDDescriptor>().ToList(); //Names for node types, parameters, enums, etc from EXE
            cathode_enum_map = ReadDB(File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_enum_lut.bin")).Cast<EnumDescriptor>().ToList(); //Correctly formatted enum list from EXE
            node_friendly_names = ReadDB(File.ReadAllBytes(Application.streamingAssetsPath + "/NodeDBs/cathode_nodename_lut.bin")).Cast<ShortGUIDDescriptor>().ToList(); //Names for unique nodes from commands BIN
#else
            cathode_id_map = ReadDB(CathodeLib.Properties.Resources.cathode_generic_lut).Cast<ShortGUIDDescriptor>().ToList(); //Names for node types, parameters, enums, etc from EXE
            cathode_enum_map = ReadDB(CathodeLib.Properties.Resources.cathode_enum_lut).Cast<EnumDescriptor>().ToList(); //Correctly formatted enum list from EXE
            node_friendly_names = ReadDB(CathodeLib.Properties.Resources.cathode_nodename_lut).Cast<ShortGUIDDescriptor>().ToList(); //Names for unique nodes from commands BIN
#endif
            SetupNodeParameterList();
        }

        //Check the CATHODE data dump for a corresponding name
        public static string GetCathodeName(cGUID id)
        {
            if (id.val == null) return "";
            foreach (ShortGUIDDescriptor db_entry in cathode_id_map) if (db_entry.ID == id) return db_entry.Description;
            return id.ToString();
        }
        public static string GetCathodeName(cGUID id, CommandsPAK pak) //This is performed separately to be able to remap nodes that are flowgraphs
        {
            if (id.val == null) return "";
            foreach (ShortGUIDDescriptor db_entry in cathode_id_map) if (db_entry.ID == id) return db_entry.Description;
            CathodeFlowgraph flow = pak.GetFlowgraph(id); if (flow == null) return id.ToString();
            return flow.name;
        }

        //Reverse CATHODE name check
        public static cGUID GetCathodeGUID(string text)
        {
            ShortGUIDDescriptor thisDesc = cathode_id_map.FirstOrDefault(o => o.Description == text);
            if (thisDesc == null) return new cGUID();
            return thisDesc.ID;
        }

        //Check the COMMANDS.BIN dump for node in-editor names
        public static string GetEditorName(cGUID id)
        {
            if (id.val == null) return "";
            foreach (ShortGUIDDescriptor db_entry in node_friendly_names) if (db_entry.ID == id) return db_entry.Description;
            return id.ToString();
        }

        //Reverse editor name check
        public static cGUID GetEditorGUID(string text)
        {
            ShortGUIDDescriptor thisDesc = node_friendly_names.FirstOrDefault(o => o.Description == text);
            if (thisDesc == null) return new cGUID();
            return thisDesc.ID;
        }

        //Check the formatted enum dump for content
        public static EnumDescriptor GetEnum(cGUID id)
        {
            return cathode_enum_map.FirstOrDefault(o => o.ID == id);
        }

        //Get the known-valid params for a node (this list is incomplete, and needs populating with default vals)
        public static string[] GetEntityParameterList(string entity_name)
        {
            if (!node_parameters.ContainsKey(entity_name)) return null;
            return node_parameters[entity_name];
        }

        //Read a generic node database file
        private static List<NodeDBDescriptor> ReadDB(byte[] db_content)
        {
            List<NodeDBDescriptor> toReturn = new List<NodeDBDescriptor>();

            MemoryStream readerStream = new MemoryStream(db_content);
            BinaryReader reader = new BinaryReader(readerStream);
            int type = reader.ReadChar(); //0 = normal db, 1 = enum db
            switch (type)
            {
                case 0:
                {
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        ShortGUIDDescriptor thisDesc = new ShortGUIDDescriptor();
                        thisDesc.ID = new cGUID(reader.ReadBytes(4));
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
                        thisDesc.ID = new cGUID(reader.ReadBytes(4));
                        thisDesc.Name = reader.ReadString();
                        int entryCount = reader.ReadInt32();
                        for (int i = 0; i < entryCount; i++) thisDesc.Entries.Add(reader.ReadString());
                        toReturn.Add(thisDesc);
                    }
                    break;
                }
            }
            reader.Close();

            return toReturn;
        }

        //Populate the node parameter dictionary
        private static void SetupNodeParameterList()
        {
            if (node_parameters.Count != 0) return;

            node_parameters.Add("AccessTerminal", new string[] { "closed", "all_data_has_been_read", "ui_breakout_triggered", "light_on_reset", "folder0", "folder1", "folder2", "folder3", "all_data_read", "location" });
            node_parameters.Add("AchievementMonitor", new string[] { "achievement_id" });
            node_parameters.Add("AchievementStat", new string[] { "achievement_id" });
            node_parameters.Add("AchievementUniqueCounter", new string[] { "achievement_id", "unique_object" });
            node_parameters.Add("AddExitObjective", new string[] { "marker", "level_name" });
            node_parameters.Add("AddItemsToGCPool", new string[] { "items" });
            node_parameters.Add("AddToInventory", new string[] { "success", "fail", "items" });
            node_parameters.Add("AILightCurveSettings", new string[] { "y0", "x1", "y1", "x2", "y2", "x3" });
            node_parameters.Add("AIMED_ITEM", new string[] { "on_started_aiming", "on_stopped_aiming", "on_display_on", "on_display_off", "on_effect_on", "on_effect_off", "target_position", "average_target_distance", "min_target_distance", "fixed_target_distance_for_local_player" });
            node_parameters.Add("AIMED_WEAPON", new string[] { "on_fired_success", "on_fired_fail", "on_fired_fail_single", "on_impact", "on_reload_started", "on_reload_another", "on_reload_empty_clip", "on_reload_canceled", "on_reload_success", "on_reload_fail", "on_shooting_started", "on_shooting_wind_down", "on_shooting_finished", "on_overheated", "on_cooled_down", "on_charge_complete", "on_charge_started", "on_charge_stopped", "on_turned_on", "on_turned_off", "on_torch_on_requested", "on_torch_off_requested", "ammoRemainingInClip", "ammoToFillClip", "ammoThatWasInClip", "charge_percentage", "charge_noise_percentage", "weapon_type", "requires_turning_on", "ejectsShellsOnFiring", "aim_assist_scale", "default_ammo_type", "starting_ammo", "clip_size", "consume_ammo_over_time_when_turned_on", "max_auto_shots_per_second", "max_manual_shots_per_second", "wind_down_time_in_seconds", "maximum_continous_fire_time_in_seconds", "overheat_recharge_time_in_seconds", "automatic_firing", "overheats", "charged_firing", "charging_duration", "min_charge_to_fire", "overcharge_timer", "charge_noise_start_time", "reloadIndividualAmmo", "alwaysDoFullReloadOfClips", "movement_accuracy_penalty_per_second", "aim_rotation_accuracy_penalty_per_second", "accuracy_penalty_per_shot", "accuracy_accumulated_per_second", "player_exposed_accuracy_penalty_per_shot", "player_exposed_accuracy_accumulated_per_second", "recoils_on_fire", "alien_threat_aware" });
            node_parameters.Add("ALLIANCE_ResetAll", new string[] { });
            node_parameters.Add("ALLIANCE_SetDisposition", new string[] { "A", "B", "Disposition" });
            node_parameters.Add("AllocateGCItemFromPoolBySubset", new string[] { "on_success", "on_failure", "selectable_items", "item_name", "item_quantity", "force_usage", "distribution_bias" });
            node_parameters.Add("AllocateGCItemsFromPool", new string[] { "on_success", "on_failure", "items", "force_usage_count", "distribution_bias" });
            node_parameters.Add("AllPlayersReady", new string[] { "on_all_players_ready", "start_on_reset", "pause_on_reset", "activation_delay" });
            node_parameters.Add("AnimatedModelAttachmentNode", new string[] { "attach_on_reset", "animated_model", "attachment", "bone_name", "use_offset", "offset" });
            node_parameters.Add("AnimationMask", new string[] { "maskHips", "maskTorso", "maskNeck", "maskHead", "maskFace", "maskLeftLeg", "maskRightLeg", "maskLeftArm", "maskRightArm", "maskLeftHand", "maskRightHand", "maskLeftFingers", "maskRightFingers", "maskTail", "maskLips", "maskEyes", "maskLeftShoulder", "maskRightShoulder", "maskRoot", "maskPrecedingLayers", "maskSelf", "maskFollowingLayers", "weight", "resource" });
            node_parameters.Add("ApplyRelativeTransform", new string[] { "origin", "destination", "input", "output", "use_trigger_entity" });
            node_parameters.Add("AreaHitMonitor", new string[] { "on_flamer_hit", "on_shotgun_hit", "on_pistol_hit", "SpherePos", "SphereRadius" });
            node_parameters.Add("AssetSpawner", new string[] { "finished_spawning", "callback_triggered", "forced_despawn", "spawn_on_reset", "asset", "spawn_on_load", "allow_forced_despawn", "persist_on_callback", "allow_physics" });
            node_parameters.Add("Benchmark", new string[] { "benchmark_name", "save_stats" });
            node_parameters.Add("BindObjectsMultiplexer", new string[] { "Pin1_Bound", "Pin2_Bound", "Pin3_Bound", "Pin4_Bound", "Pin5_Bound", "Pin6_Bound", "Pin7_Bound", "Pin8_Bound", "Pin9_Bound", "Pin10_Bound", "objects" });
            node_parameters.Add("BlendLowResFrame", new string[] { "blend_value", "CharacterMonitor", "character" });
            node_parameters.Add("BloomSettings", new string[] { "frame_buffer_scale", "frame_buffer_offset", "bloom_scale", "bloom_gather_exponent", "bloom_gather_scale" });
            node_parameters.Add("BoneAttachedCamera", new string[] { "character", "position_offset", "rotation_offset", "movement_damping", "bone_name" });
            node_parameters.Add("BooleanLogicInterface", new string[] { "on_true", "on_false", "LHS", "RHS", "Result" });
            node_parameters.Add("BooleanLogicOperation", new string[] { "Input", "Result" });
            node_parameters.Add("Box", new string[] { "event", "enable_on_reset", "half_dimensions", "include_physics" });
            node_parameters.Add("BroadcastTrigger", new string[] { "on_triggered" });
            node_parameters.Add("BulletChamber", new string[] { "Slot1", "Slot2", "Slot3", "Slot4", "Slot5", "Slot6", "Weapon", "Geometry" });
            node_parameters.Add("ButtonMashPrompt", new string[] { "on_back_to_zero", "on_degrade", "on_mashed", "on_success", "count", "mashes_to_completion", "time_between_degrades", "use_degrade", "hold_to_charge" });
            node_parameters.Add("CAGEAnimation", new string[] { "animation_finished", "animation_interrupted", "animation_changed", "cinematic_loaded", "cinematic_unloaded", "enable_on_reset", "external_time", "current_time", "use_external_time", "rewind_on_stop", "jump_to_the_end", "playspeed", "anim_length", "is_cinematic", "is_cinematic_skippable", "skippable_timer", "capture_video", "capture_clip_name", "playback" });
            node_parameters.Add("CameraAimAssistant", new string[] { "enable_on_reset", "activation_radius", "inner_radius", "camera_speed_attenuation", "min_activation_distance", "fading_range" });
            node_parameters.Add("CameraBehaviorInterface", new string[] { "start_on_reset", "pause_on_reset", "enable_on_reset", "linked_cameras", "behavior_name", "priority", "threshold", "blend_in", "duration", "blend_out" });
            node_parameters.Add("CameraCollisionBox", new string[] { });
            node_parameters.Add("CameraDofController", new string[] { "character_to_focus", "focal_length_mm", "focal_plane_m", "fnum", "focal_point", "focal_point_offset", "bone_to_focus" });
            node_parameters.Add("CameraFinder", new string[] { "camera_name" });
            node_parameters.Add("CameraPath", new string[] { "linked_splines", "path_name", "path_type", "path_class", "is_local", "relative_position", "is_loop", "duration" });
            node_parameters.Add("CameraPathDriven", new string[] { "position_path", "target_path", "reference_path", "position_path_transform", "target_path_transform", "reference_path_transform", "point_to_project", "path_driven_type", "invert_progression", "position_path_offset", "target_path_offset", "animation_duration" });
            node_parameters.Add("CameraPlayAnimation", new string[] { "on_animation_finished", "animated_camera", "position_marker", "character_to_focus", "focal_length_mm", "focal_plane_m", "fnum", "focal_point", "animation_length", "frames_count", "result_transformation", "data_file", "start_frame", "end_frame", "play_speed", "loop_play", "clipping_planes_preset", "is_cinematic", "dof_key", "shot_number", "override_dof", "focal_point_offset", "bone_to_focus" });
            node_parameters.Add("CameraResource", new string[] { "on_enter_transition_finished", "on_exit_transition_finished", "enable_on_reset", "camera_name", "is_camera_transformation_local", "camera_transformation", "fov", "clipping_planes_preset", "is_ghost", "converge_to_player_camera", "reset_player_camera_on_exit", "enable_enter_transition", "transition_curve_direction", "transition_curve_strength", "transition_duration", "transition_ease_in", "transition_ease_out", "enable_exit_transition", "exit_transition_curve_direction", "exit_transition_curve_strength", "exit_transition_duration", "exit_transition_ease_in", "exit_transition_ease_out" });
            node_parameters.Add("CameraShake", new string[] { "relative_transformation", "impulse_intensity", "impulse_position", "shake_type", "shake_frequency", "max_rotation_angles", "max_position_offset", "shake_rotation", "shake_position", "bone_shaking", "override_weapon_swing", "internal_radius", "external_radius", "strength_damping", "explosion_push_back", "spring_constant", "spring_damping" });
            node_parameters.Add("CamPeek", new string[] { "pos", "x_ratio", "y_ratio", "range_left", "range_right", "range_up", "range_down", "range_forward", "range_backward", "speed_x", "speed_y", "damping_x", "damping_y", "focal_distance", "focal_distance_y", "roll_factor", "use_ik_solver", "use_horizontal_plane", "stick", "disable_collision_test" });
            node_parameters.Add("Character", new string[] { "finished_spawning", "finished_respawning", "dead_container_take_slot", "dead_container_emptied", "on_ragdoll_impact", "on_footstep", "on_despawn_requested", "spawn_on_reset", "show_on_reset", "contents_of_dead_container", "PopToNavMesh", "is_cinematic", "disable_dead_container", "allow_container_without_death", "container_interaction_text", "anim_set", "anim_tree_set", "attribute_set", "is_player", "is_backstage", "force_backstage_on_respawn", "character_class", "alliance_group", "dialogue_voice", "spawn_id", "position", "display_model", "reference_skeleton", "torso_sound", "leg_sound", "footwear_sound", "custom_character_type", "custom_character_accessory_override", "custom_character_population_type", "named_custom_character", "named_custom_character_assets_set", "gcip_distribution_bias", "inventory_set" });
            node_parameters.Add("CharacterAttachmentNode", new string[] { "attach_on_reset", "character", "attachment", "Node", "AdditiveNode", "AdditiveNodeIntensity", "UseOffset", "Translation", "Rotation" });
            node_parameters.Add("CharacterCommand", new string[] { "command_started", "override_all_ai" });
            node_parameters.Add("CharacterShivaArms", new string[] { });
            node_parameters.Add("CharacterTypeMonitor", new string[] { "spawned", "despawned", "all_despawned", "AreAny", "character_class", "trigger_on_start", "trigger_on_checkpoint_restart" });
            node_parameters.Add("Checkpoint", new string[] { "on_checkpoint", "on_captured", "on_saved", "finished_saving", "finished_loading", "cancelled_saving", "finished_saving_to_hdd", "player_spawn_position", "is_first_checkpoint", "is_first_autorun_checkpoint", "section", "mission_number", "checkpoint_type" });
            node_parameters.Add("CheckpointRestoredNotify", new string[] { "restored" });
            node_parameters.Add("ChokePoint", new string[] { "resource" });
            node_parameters.Add("CHR_DamageMonitor", new string[] { "damaged", "InstigatorFilter", "DamageDone", "Instigator", "DamageType" });
            node_parameters.Add("CHR_DeathMonitor", new string[] { "dying", "killed", "KillerFilter", "Killer", "DamageType" });
            node_parameters.Add("CHR_DeepCrouch", new string[] { "crouch_amount", "smooth_damping", "allow_stand_up" });
            node_parameters.Add("CHR_GetAlliance", new string[] { "Alliance" });
            node_parameters.Add("CHR_GetHealth", new string[] { "Health" });
            node_parameters.Add("CHR_GetTorch", new string[] { "torch_on", "torch_off", "TorchOn" });
            node_parameters.Add("CHR_HasWeaponOfType", new string[] { "on_true", "on_false", "Result", "weapon_type", "check_if_weapon_draw" });
            node_parameters.Add("CHR_HoldBreath", new string[] { "ExhaustionOnStop" });
            node_parameters.Add("CHR_IsWithinRange", new string[] { "In_range", "Out_of_range", "Position", "Radius", "Height", "Range_test_shape" });
            node_parameters.Add("CHR_KnockedOutMonitor", new string[] { "on_knocked_out", "on_recovered" });
            node_parameters.Add("CHR_LocomotionDuck", new string[] { "Height" });
            node_parameters.Add("CHR_LocomotionEffect", new string[] { "Effect" });
            node_parameters.Add("CHR_LocomotionModifier", new string[] { "Can_Run", "Can_Crouch", "Can_Aim", "Can_Injured", "Must_Walk", "Must_Run", "Must_Crouch", "Must_Aim", "Must_Injured", "Is_In_Spacesuit" });
            node_parameters.Add("CHR_ModifyBreathing", new string[] { "Exhaustion" });
            node_parameters.Add("Chr_PlayerCrouch", new string[] { "crouch" });
            node_parameters.Add("CHR_PlayNPCBark", new string[] { "on_speech_started", "on_speech_finished", "queue_time", "sound_event", "speech_priority", "dialogue_mode", "action" });
            node_parameters.Add("CHR_PlaySecondaryAnimation", new string[] { "Interrupted", "finished", "on_loaded", "Marker", "OptionalMask", "ExternalStartTime", "ExternalTime", "animationLength", "AnimationSet", "Animation", "StartFrame", "EndFrame", "PlayCount", "PlaySpeed", "StartInstantly", "AllowInterruption", "BlendInTime", "GaitSyncStart", "Mirror", "AnimationLayer", "AutomaticZoning", "ManualLoading" });
            node_parameters.Add("CHR_RetreatMonitor", new string[] { "reached_retreat", "started_retreating" });
            node_parameters.Add("CHR_SetAlliance", new string[] { "Alliance" });
            node_parameters.Add("CHR_SetAndroidThrowTarget", new string[] { "thrown", "throw_position" });
            node_parameters.Add("CHR_SetDebugDisplayName", new string[] { "DebugName" });
            node_parameters.Add("CHR_SetFacehuggerAggroRadius", new string[] { "radius" });
            node_parameters.Add("CHR_SetFocalPoint", new string[] { "focal_point", "priority", "speed", "steal_camera", "line_of_sight_test" });
            node_parameters.Add("CHR_SetHeadVisibility", new string[] { "is_visible" });
            node_parameters.Add("CHR_SetHealth", new string[] { "HealthPercentage", "UsePercentageOfCurrentHeath" });
            node_parameters.Add("CHR_SetInvincibility", new string[] { "damage_mode" });
            node_parameters.Add("CHR_SetMood", new string[] { "mood", "moodIntensity", "timeOut" });
            node_parameters.Add("CHR_SetShowInMotionTracker", new string[] { });
            node_parameters.Add("CHR_SetSubModelVisibility", new string[] { "is_visible", "matching" });
            node_parameters.Add("CHR_SetTacticalPosition", new string[] { "tactical_position", "sweep_type", "fixed_sweep_radius" });
            node_parameters.Add("CHR_SetTacticalPositionToTarget", new string[] { });
            node_parameters.Add("CHR_SetTorch", new string[] { "TorchOn" });
            node_parameters.Add("CHR_TakeDamage", new string[] { "Damage", "DamageIsAPercentage", "AmmoType" });
            node_parameters.Add("CHR_TorchMonitor", new string[] { "torch_on", "torch_off", "TorchOn", "trigger_on_start", "trigger_on_checkpoint_restart" });
            node_parameters.Add("CHR_VentMonitor", new string[] { "entered_vent", "exited_vent", "IsInVent", "trigger_on_start", "trigger_on_checkpoint_restart" });
            node_parameters.Add("CHR_WeaponFireMonitor", new string[] { "fired" });
            node_parameters.Add("ChromaticAberrations", new string[] { "aberration_scalar" });
            node_parameters.Add("ClearPrimaryObjective", new string[] { "clear_all_sub_objectives" });
            node_parameters.Add("ClearSubObjective", new string[] { "slot_number" });
            node_parameters.Add("ClipPlanesController", new string[] { "near_plane", "far_plane", "update_near", "update_far" });
            node_parameters.Add("CMD_AimAt", new string[] { "finished", "AimTarget", "Raise_gun", "use_current_target" });
            node_parameters.Add("CMD_AimAtCurrentTarget", new string[] { "succeeded", "Raise_gun" });
            node_parameters.Add("CMD_Die", new string[] { "Killer", "death_style" });
            node_parameters.Add("CMD_Follow", new string[] { "entered_inner_radius", "exitted_outer_radius", "failed", "Waypoint", "idle_stance", "move_type", "inner_radius", "outer_radius", "prefer_traversals" });
            node_parameters.Add("CMD_FollowUsingJobs", new string[] { "failed", "target_to_follow", "who_Im_leading", "fastest_allowed_move_type", "slowest_allowed_move_type", "centre_job_restart_radius", "inner_radius", "outer_radius", "job_select_radius", "job_cancel_radius", "teleport_required_range", "teleport_radius", "prefer_traversals", "avoid_player", "allow_teleports", "follow_type", "clamp_speed" });
            node_parameters.Add("CMD_ForceMeleeAttack", new string[] { "melee_attack_type", "enemy_type", "melee_attack_index" });
            node_parameters.Add("CMD_ForceReloadWeapon", new string[] { "success" });
            node_parameters.Add("CMD_GoTo", new string[] { "succeeded", "failed", "Waypoint", "AimTarget", "move_type", "enable_lookaround", "use_stopping_anim", "always_stop_at_radius", "stop_at_radius_if_lined_up", "continue_from_previous_move", "disallow_traversal", "arrived_radius", "should_be_aiming", "use_current_target_as_aim", "allow_to_use_vents", "DestinationIsBackstage", "maintain_current_facing", "start_instantly" });
            node_parameters.Add("CMD_GoToCover", new string[] { "succeeded", "failed", "entered_cover", "CoverPoint", "AimTarget", "move_type", "SearchRadius", "enable_lookaround", "duration", "continue_from_previous_move", "disallow_traversal", "should_be_aiming", "use_current_target_as_aim" });
            node_parameters.Add("CMD_HolsterWeapon", new string[] { "failed", "success", "should_holster", "skip_anims", "equipment_slot", "force_player_unarmed_on_holster", "force_drop_held_item" });
            node_parameters.Add("CMD_Idle", new string[] { "finished", "interrupted", "target_to_face", "should_face_target", "should_raise_gun_while_turning", "desired_stance", "duration", "idle_style", "lock_cameras", "anchor", "start_instantly" });
            node_parameters.Add("CMD_LaunchMeleeAttack", new string[] { "finished", "melee_attack_type", "enemy_type", "melee_attack_index", "skip_convergence" });
            node_parameters.Add("CMD_ModifyCombatBehaviour", new string[] { "behaviour_type", "status" });
            node_parameters.Add("CMD_MoveTowards", new string[] { "succeeded", "failed", "MoveTarget", "AimTarget", "move_type", "disallow_traversal", "should_be_aiming", "use_current_target_as_aim", "never_succeed" });
            node_parameters.Add("CMD_PlayAnimation", new string[] { "Interrupted", "finished", "badInterrupted", "on_loaded", "SafePos", "Marker", "ExitPosition", "ExternalStartTime", "ExternalTime", "OverrideCharacter", "OptionalMask", "animationLength", "AnimationSet", "Animation", "StartFrame", "EndFrame", "PlayCount", "PlaySpeed", "AllowGravity", "AllowCollision", "Start_Instantly", "AllowInterruption", "RemoveMotion", "DisableGunLayer", "BlendInTime", "GaitSyncStart", "ConvergenceTime", "LocationConvergence", "OrientationConvergence", "UseExitConvergence", "ExitConvergenceTime", "Mirror", "FullCinematic", "RagdollEnabled", "NoIK", "NoFootIK", "NoLayers", "PlayerAnimDrivenView", "ExertionFactor", "AutomaticZoning", "ManualLoading", "IsCrouchedAnim", "InitiallyBackstage", "Death_by_ragdoll_only", "dof_key", "shot_number", "UseShivaArms", "resource" });
            node_parameters.Add("CMD_Ragdoll", new string[] { "finished", "actor", "impact_velocity" });
            node_parameters.Add("CMD_ShootAt", new string[] { "succeeded", "failed", "Target" });
            node_parameters.Add("CMD_StopScript", new string[] { });
            node_parameters.Add("CollectIDTag", new string[] { "tag_id" });
            node_parameters.Add("CollectNostromoLog", new string[] { "log_id" });
            node_parameters.Add("CollectSevastopolLog", new string[] { "log_id" });
            node_parameters.Add("CollisionBarrier", new string[] { "on_damaged", "deleted", "collision_type", "static_collision" });
            node_parameters.Add("ColourCorrectionTransition", new string[] { "interpolate", "colour_lut_a", "colour_lut_b", "lut_a_contribution", "lut_b_contribution", "colour_lut_a_index", "colour_lut_b_index" });
            node_parameters.Add("ColourSettings", new string[] { "brightness", "contrast", "saturation", "red_tint", "green_tint", "blue_tint" });
            node_parameters.Add("CompoundVolume", new string[] { "event" });
            node_parameters.Add("ControllableRange", new string[] { "min_range_x", "max_range_x", "min_range_y", "max_range_y", "min_feather_range_x", "max_feather_range_x", "min_feather_range_y", "max_feather_range_y", "speed_x", "speed_y", "damping_x", "damping_y", "mouse_speed_x", "mouse_speed_y" });
            node_parameters.Add("Convo", new string[] { "everyoneArrived", "playerJoined", "playerLeft", "npcJoined", "members", "speaker", "alwaysTalkToPlayerIfPresent", "playerCanJoin", "playerCanLeave", "positionNPCs", "circularShape", "convoPosition", "personalSpaceRadius" });
            node_parameters.Add("Counter", new string[] { "on_under_limit", "on_limit", "on_over_limit", "Count", "is_limitless", "trigger_limit" });
            node_parameters.Add("CoverExclusionArea", new string[] { "position", "half_dimensions", "exclude_cover", "exclude_vaults", "exclude_mantles", "exclude_jump_downs", "exclude_crawl_space_spotting_positions", "exclude_spotting_positions", "exclude_assault_positions" });
            node_parameters.Add("CoverLine", new string[] { "enable_on_reset", "LinePath", "low", "resource", "LinePathPosition" });
            node_parameters.Add("Custom_Hiding_Controller", new string[] { "Started_Idle", "Started_Exit", "Got_Out", "Prompt_1", "Prompt_2", "Start_choking", "Start_oxygen_starvation", "Show_MT", "Hide_MT", "Spawn_MT", "Despawn_MT", "Start_Busted_By_Alien", "Start_Busted_By_Android", "End_Busted_By_Android", "Start_Busted_By_Human", "End_Busted_By_Human", "Enter_Anim", "Idle_Anim", "Exit_Anim", "has_MT", "is_high", "AlienBusted_Player_1", "AlienBusted_Alien_1", "AlienBusted_Player_2", "AlienBusted_Alien_2", "AlienBusted_Player_3", "AlienBusted_Alien_3", "AlienBusted_Player_4", "AlienBusted_Alien_4", "AndroidBusted_Player_1", "AndroidBusted_Android_1", "AndroidBusted_Player_2", "AndroidBusted_Android_2", "MT_pos" });
            node_parameters.Add("Custom_Hiding_Vignette_controller", new string[] { "StartFade", "StopFade", "Breath", "Blackout_start_time", "run_out_time", "Vignette", "FadeValue" });
            node_parameters.Add("DayToneMappingSettings", new string[] { "black_point", "cross_over_point", "white_point", "shoulder_strength", "toe_strength", "luminance_scale" });
            node_parameters.Add("DEBUG_SenseLevels", new string[] { "no_activation", "trace_activation", "lower_activation", "normal_activation", "upper_activation", "Sense" });
            node_parameters.Add("DebugCamera", new string[] { "linked_cameras" });
            node_parameters.Add("DebugCaptureCorpse", new string[] { "finished_capturing", "character", "corpse_name" });
            node_parameters.Add("DebugCaptureScreenShot", new string[] { "finished_capturing", "wait_for_streamer", "capture_filename", "fov", "near", "far" });
            node_parameters.Add("DebugCheckpoint", new string[] { "on_checkpoint", "section", "level_reset" });
            node_parameters.Add("DebugEnvironmentMarker", new string[] { "target", "float_input", "int_input", "bool_input", "vector_input", "enum_input", "text", "namespace", "size", "colour", "world_pos", "duration", "scale_with_distance", "max_string_length", "scroll_speed", "show_distance_from_target", "DebugPositionMarker", "world_pos" });
            node_parameters.Add("DebugGraph", new string[] { "Inputs", "scale", "duration", "samples_per_second", "auto_scale", "auto_scroll" });
            node_parameters.Add("DebugLoadCheckpoint", new string[] { "previous_checkpoint" });
            node_parameters.Add("DebugMenuToggle", new string[] { "debug_variable", "value" });
            node_parameters.Add("DebugObjectMarker", new string[] { "marked_object", "marked_name" });
            node_parameters.Add("DebugText", new string[] { "duration_finished", "float_input", "int_input", "bool_input", "vector_input", "enum_input", "text_input", "text", "namespace", "size", "colour", "alignment", "duration", "pause_game", "cancel_pause_with_button_press", "priority", "ci_type" });
            node_parameters.Add("DebugTextStacking", new string[] { "float_input", "int_input", "bool_input", "vector_input", "enum_input", "text", "namespace", "size", "colour", "ci_type", "needs_debug_opt_to_render" });
            node_parameters.Add("DeleteBlankPanel", new string[] { "door_mechanism" });
            node_parameters.Add("DeleteButtonDisk", new string[] { "door_mechanism", "button_type" });
            node_parameters.Add("DeleteButtonKeys", new string[] { "door_mechanism", "button_type" });
            node_parameters.Add("DeleteCuttingPanel", new string[] { "door_mechanism" });
            node_parameters.Add("DeleteHacking", new string[] { "door_mechanism" });
            node_parameters.Add("DeleteHousing", new string[] { "door_mechanism", "is_door" });
            node_parameters.Add("DeleteKeypad", new string[] { "door_mechanism" });
            node_parameters.Add("DeletePullLever", new string[] { "door_mechanism", "lever_type" });
            node_parameters.Add("DeleteRotateLever", new string[] { "door_mechanism", "lever_type" });
            node_parameters.Add("DepthOfFieldSettings", new string[] { "focal_length_mm", "focal_plane_m", "fnum", "focal_point", "use_camera_target" });
            node_parameters.Add("DespawnCharacter", new string[] { "despawned" });
            node_parameters.Add("DespawnPlayer", new string[] { "despawned" });
            node_parameters.Add("Display_Element_On_Map", new string[] { "map_name", "element_name" });
            node_parameters.Add("DisplayMessage", new string[] { "title_id", "message_id" });
            node_parameters.Add("DisplayMessageWithCallbacks", new string[] { "on_yes", "on_no", "on_cancel", "title_text", "message_text", "yes_text", "no_text", "cancel_text", "yes_button", "no_button", "cancel_button" });
            node_parameters.Add("DistortionOverlay", new string[] { "intensity", "time", "distortion_texture", "alpha_threshold_enabled", "threshold_texture", "range", "begin_start_time", "begin_stop_time", "end_start_time", "end_stop_time" });
            node_parameters.Add("DistortionSettings", new string[] { "radial_distort_factor", "radial_distort_constraint", "radial_distort_scalar" });
            node_parameters.Add("Door", new string[] { "started_opening", "started_closing", "finished_opening", "finished_closing", "used_locked", "used_unlocked", "used_forced_open", "used_forced_closed", "waiting_to_open", "highlight", "unhighlight", "zone_link", "animation", "trigger_filter", "icon_pos", "icon_usable_radius", "show_icon_when_locked", "nav_mesh", "wait_point_1", "wait_point_2", "geometry", "is_scripted", "wait_to_open", "is_waiting", "unlocked_text", "locked_text", "icon_keyframe", "detach_anim", "invert_nav_mesh_barrier" });
            node_parameters.Add("DoorStatus", new string[] { "hacking_difficulty", "door_mechanism", "gate_type", "has_correct_keycard", "cutting_tool_level", "is_locked", "is_powered", "is_cutting_complete" });
            node_parameters.Add("DurangoVideoCapture", new string[] { "clip_name" });
            node_parameters.Add("EFFECT_DirectionalPhysics", new string[] { "relative_direction", "effect_distance", "angular_falloff", "min_force", "max_force" });
            node_parameters.Add("EFFECT_EntityGenerator", new string[] { "entities", "trigger_on_reset", "count", "spread", "force_min", "force_max", "force_offset_XY_min", "force_offset_XY_max", "force_offset_Z_min", "force_offset_Z_max", "lifetime_min", "lifetime_max", "use_local_rotation" });
            node_parameters.Add("EFFECT_ImpactGenerator", new string[] { "on_impact", "on_failed", "trigger_on_reset", "min_distance", "distance", "max_count", "count", "spread", "skip_characters", "use_local_rotation" });
            node_parameters.Add("EggSpawner", new string[] { "egg_position", "hostile_egg" });
            node_parameters.Add("ElapsedTimer", new string[] { });
            node_parameters.Add("EnableMotionTrackerPassiveAudio", new string[] { });
            node_parameters.Add("EndGame", new string[] { "on_game_end_started", "on_game_ended", "success" });
            node_parameters.Add("ENT_Debug_Exit_Game", new string[] { "FailureText", "FailureCode" });
            node_parameters.Add("EnvironmentMap", new string[] { "Entities", "Priority", "ColourFactor", "EmissiveFactor", "Texture", "Texture_Index", "environmentmap_index" });
            node_parameters.Add("EnvironmentModelReference", new string[] { "resource" });
            node_parameters.Add("EQUIPPABLE_ITEM", new string[] { "finished_spawning", "equipped", "unequipped", "on_pickup", "on_discard", "on_melee_impact", "on_used_basic_function", "spawn_on_reset", "item_animated_asset", "owner", "has_owner", "character_animation_context", "character_activate_animation_context", "left_handed", "inventory_name", "equipment_slot", "holsters_on_owner", "holster_node", "holster_scale", "weapon_handedness" });
            node_parameters.Add("ExclusiveMaster", new string[] { "active_objects", "inactive_objects", "resource" });
            node_parameters.Add("Explosion_AINotifier", new string[] { "on_character_damage_fx", "ExplosionPos", "AmmoType" });
            node_parameters.Add("ExternalVariableBool", new string[] { "game_variable" });
            node_parameters.Add("FakeAILightSourceInPlayersHand", new string[] { "radius", "pos" });
            node_parameters.Add("FilmGrainSettings", new string[] { "low_lum_amplifier", "mid_lum_amplifier", "high_lum_amplifier", "low_lum_range", "mid_lum_range", "high_lum_range", "noise_texture_scale", "adaptive", "adaptation_scalar", "adaptation_time_scalar", "unadapted_low_lum_amplifier", "unadapted_mid_lum_amplifier", "unadapted_high_lum_amplifier" });
            node_parameters.Add("FilterAbsorber", new string[] { "output", "factor", "start_value", "input" });
            node_parameters.Add("FilterAnd", new string[] { "filter" });
            node_parameters.Add("FilterBelongsToAlliance", new string[] { "alliance_group" });
            node_parameters.Add("FilterCanSeeTarget", new string[] { "Target" });
            node_parameters.Add("FilterHasBehaviourTreeFlagSet", new string[] { "BehaviourTreeFlag" });
            node_parameters.Add("FilterHasPlayerCollectedIdTag", new string[] { "tag_id" });
            node_parameters.Add("FilterHasWeaponEquipped", new string[] { "weapon_type" });
            node_parameters.Add("FilterHasWeaponOfType", new string[] { "weapon_type" });
            node_parameters.Add("FilterIsACharacter", new string[] { });
            node_parameters.Add("FilterIsAgressing", new string[] { "Target" });
            node_parameters.Add("FilterIsAnySaveInProgress", new string[] { });
            node_parameters.Add("FilterIsAPlayer", new string[] { });
            node_parameters.Add("FilterIsCharacter", new string[] { "character" });
            node_parameters.Add("FilterIsCharacterClass", new string[] { "character_class" });
            node_parameters.Add("FilterIsCharacterClassCombo", new string[] { "character_classes" });
            node_parameters.Add("FilterIsDead", new string[] { });
            node_parameters.Add("FilterIsEnemyOfAllianceGroup", new string[] { "alliance_group" });
            node_parameters.Add("FilterIsEnemyOfCharacter", new string[] { "Character", "use_alliance_at_death" });
            node_parameters.Add("FilterIsEnemyOfPlayer", new string[] { });
            node_parameters.Add("FilterIsFacingTarget", new string[] { "target", "tolerance" });
            node_parameters.Add("FilterIsHumanNPC", new string[] { });
            node_parameters.Add("FilterIsInAGroup", new string[] { });
            node_parameters.Add("FilterIsInAlertnessState", new string[] { "AlertState" });
            node_parameters.Add("FilterIsinInventory", new string[] { "ItemName" });
            node_parameters.Add("FilterIsInLocomotionState", new string[] { "State" });
            node_parameters.Add("FilterIsInWeaponRange", new string[] { "weapon_owner" });
            node_parameters.Add("FilterIsLocalPlayer", new string[] { });
            node_parameters.Add("FilterIsNotDeadManWalking", new string[] { });
            node_parameters.Add("FilterIsObject", new string[] { "objects" });
            node_parameters.Add("FilterIsPhysics", new string[] { });
            node_parameters.Add("FilterIsPhysicsObject", new string[] { "object" });
            node_parameters.Add("FilterIsPlatform", new string[] { "Platform" });
            node_parameters.Add("FilterIsUsingDevice", new string[] { "Device" });
            node_parameters.Add("FilterIsValidInventoryItem", new string[] { "item" });
            node_parameters.Add("FilterIsWithdrawnAlien", new string[] { });
            node_parameters.Add("FilterNot", new string[] { "filter" });
            node_parameters.Add("FilterOr", new string[] { "filter" });
            node_parameters.Add("FilterSmallestUsedDifficulty", new string[] { "difficulty" });
            node_parameters.Add("FixedCamera", new string[] { "use_transform_position", "transform_position", "camera_position", "camera_target", "camera_position_offset", "camera_target_offset", "apply_target", "apply_position", "use_target_offset", "use_position_offset" });
            node_parameters.Add("FlareSettings", new string[] { "flareOffset0", "flareIntensity0", "flareAttenuation0", "flareOffset1", "flareIntensity1", "flareAttenuation1", "flareOffset2", "flareIntensity2", "flareAttenuation2", "flareOffset3", "flareIntensity3", "flareAttenuation3" });
            node_parameters.Add("FlareTask", new string[] { "specific_character", "filter_options" });
            node_parameters.Add("FlashCallback", new string[] { "callback", "callback_name" });
            node_parameters.Add("FlashInvoke", new string[] { "layer_name", "mrtt_texture", "method", "invoke_type", "int_argument_0", "int_argument_1", "int_argument_2", "int_argument_3", "float_argument_0", "float_argument_1", "float_argument_2", "float_argument_3" });
            node_parameters.Add("FlashScript", new string[] { "show_on_reset", "filename", "layer_name", "target_texture_name", "type" });
            node_parameters.Add("FloatAbsolute", new string[] { });
            node_parameters.Add("FloatAdd", new string[] { });
            node_parameters.Add("FloatAdd_All", new string[] { });
            node_parameters.Add("FloatClamp", new string[] { "Min", "Max", "Value", "Result" });
            node_parameters.Add("FloatClampMultiply", new string[] { "Min", "Max" });
            node_parameters.Add("FloatCompare", new string[] { "on_true", "on_false", "LHS", "RHS", "Threshold", "Result" });
            node_parameters.Add("FloatDivide", new string[] { });
            node_parameters.Add("FloatEquals", new string[] { });
            node_parameters.Add("FloatGetLinearProportion", new string[] { "Min", "Input", "Max", "Proportion" });
            node_parameters.Add("FloatGreaterThan", new string[] { });
            node_parameters.Add("FloatGreaterThanOrEqual", new string[] { });
            node_parameters.Add("FloatLessThan", new string[] { });
            node_parameters.Add("FloatLessThanOrEqual", new string[] { });
            node_parameters.Add("FloatLinearInterpolateSpeed", new string[] { "on_finished", "on_think", "Result", "Initial_Value", "Target_Value", "Speed", "PingPong", "Loop" });
            node_parameters.Add("FloatLinearInterpolateSpeedAdvanced", new string[] { "on_finished", "on_think", "trigger_on_min", "trigger_on_max", "trigger_on_loop", "Result", "Initial_Value", "Min_Value", "Max_Value", "Speed", "PingPong", "Loop" });
            node_parameters.Add("FloatLinearInterpolateTimed", new string[] { "on_finished", "on_think", "Result", "Initial_Value", "Target_Value", "Time", "PingPong", "Loop" });
            node_parameters.Add("FloatLinearProportion", new string[] { "Initial_Value", "Target_Value", "Proportion", "Result" });
            node_parameters.Add("FloatMath", new string[] { "LHS", "RHS", "Result" });
            node_parameters.Add("FloatMath_All", new string[] { "Numbers", "Result" });
            node_parameters.Add("FloatMax", new string[] { });
            node_parameters.Add("FloatMax_All", new string[] { });
            node_parameters.Add("FloatMin", new string[] { });
            node_parameters.Add("FloatMin_All", new string[] { });
            node_parameters.Add("FloatModulate", new string[] { "on_think", "Result", "wave_shape", "frequency", "phase", "amplitude", "bias" });
            node_parameters.Add("FloatModulateRandom", new string[] { "on_full_switched_on", "on_full_switched_off", "on_think", "Result", "switch_on_anim", "switch_on_delay", "switch_on_custom_frequency", "switch_on_duration", "switch_off_anim", "switch_off_custom_frequency", "switch_off_duration", "behaviour_anim", "behaviour_frequency", "behaviour_frequency_variance", "behaviour_offset", "pulse_modulation", "oscillate_range_min", "sparking_speed", "blink_rate", "blink_range_min", "flicker_rate", "flicker_off_rate", "flicker_range_min", "flicker_off_range_min", "disable_behaviour" });
            node_parameters.Add("FloatMultiply", new string[] { });
            node_parameters.Add("FloatMultiply_All", new string[] { "Invert" });
            node_parameters.Add("FloatMultiplyClamp", new string[] { "Min", "Max" });
            node_parameters.Add("FloatNotEqual", new string[] { });
            node_parameters.Add("FloatOperation", new string[] { "Input", "Result" });
            node_parameters.Add("FloatReciprocal", new string[] { });
            node_parameters.Add("FloatRemainder", new string[] { });
            node_parameters.Add("FloatSmoothStep", new string[] { "Low_Edge", "High_Edge", "Value", "Result" });
            node_parameters.Add("FloatSqrt", new string[] { });
            node_parameters.Add("FloatSubtract", new string[] { });
            node_parameters.Add("FlushZoneCache", new string[] { "CurrentGen", "NextGen" });
            node_parameters.Add("FogBox", new string[] { "deleted", "show_on_reset", "GEOMETRY_TYPE", "COLOUR_TINT", "DISTANCE_FADE", "ANGLE_FADE", "BILLBOARD", "EARLY_ALPHA", "LOW_RES", "CONVEX_GEOM", "THICKNESS", "START_DISTANT_CLIP", "START_DISTANCE_FADE", "SOFTNESS", "SOFTNESS_EDGE", "LINEAR_HEIGHT_DENSITY", "SMOOTH_HEIGHT_DENSITY", "HEIGHT_MAX_DENSITY", "FRESNEL_FALLOFF", "FRESNEL_POWER", "DEPTH_INTERSECT_COLOUR", "DEPTH_INTERSECT_INITIAL_COLOUR", "DEPTH_INTERSECT_INITIAL_ALPHA", "DEPTH_INTERSECT_MIDPOINT_COLOUR", "DEPTH_INTERSECT_MIDPOINT_ALPHA", "DEPTH_INTERSECT_MIDPOINT_DEPTH", "DEPTH_INTERSECT_END_COLOUR", "DEPTH_INTERSECT_END_ALPHA", "DEPTH_INTERSECT_END_DEPTH", "resource" });
            node_parameters.Add("FogPlane", new string[] { "fog_plane_resource", "start_distance_fade_scalar", "distance_fade_scalar", "angle_fade_scalar", "linear_height_density_fresnel_power_scalar", "linear_heigth_density_max_scalar", "tint", "thickness_scalar", "edge_softness_scalar", "diffuse_0_uv_scalar", "diffuse_0_speed_scalar", "diffuse_1_uv_scalar", "diffuse_1_speed_scalar" });
            node_parameters.Add("FogSetting", new string[] { "linear_distance", "max_distance", "linear_density", "exponential_density", "near_colour", "far_colour" });
            node_parameters.Add("FogSphere", new string[] { "deleted", "show_on_reset", "COLOUR_TINT", "INTENSITY", "OPACITY", "EARLY_ALPHA", "LOW_RES_ALPHA", "CONVEX_GEOM", "DISABLE_SIZE_CULLING", "NO_CLIP", "ALPHA_LIGHTING", "DYNAMIC_ALPHA_LIGHTING", "DENSITY", "EXPONENTIAL_DENSITY", "SCENE_DEPENDANT_DENSITY", "FRESNEL_TERM", "FRESNEL_POWER", "SOFTNESS", "SOFTNESS_EDGE", "BLEND_ALPHA_OVER_DISTANCE", "FAR_BLEND_DISTANCE", "NEAR_BLEND_DISTANCE", "SECONDARY_BLEND_ALPHA_OVER_DISTANCE", "SECONDARY_FAR_BLEND_DISTANCE", "SECONDARY_NEAR_BLEND_DISTANCE", "DEPTH_INTERSECT_COLOUR", "DEPTH_INTERSECT_COLOUR_VALUE", "DEPTH_INTERSECT_ALPHA_VALUE", "DEPTH_INTERSECT_RANGE", "resource" });
            node_parameters.Add("FollowCameraModifier", new string[] { "enable_on_reset", "position_curve", "target_curve", "modifier_type", "position_offset", "target_offset", "field_of_view", "force_state", "force_state_initial_value", "can_mirror", "is_first_person", "bone_blending_ratio", "movement_speed", "movement_speed_vertical", "movement_damping", "horizontal_limit_min", "horizontal_limit_max", "vertical_limit_min", "vertical_limit_max", "mouse_speed_hori", "mouse_speed_vert", "acceleration_duration", "acceleration_ease_in", "acceleration_ease_out", "transition_duration", "transition_ease_in", "transition_ease_out" });
            node_parameters.Add("FollowTask", new string[] { "can_initially_end_early", "stop_radius" });
            node_parameters.Add("Force_UI_Visibility", new string[] { "also_disable_interactions" });
            node_parameters.Add("FullScreenBlurSettings", new string[] { "contribution" });
            node_parameters.Add("FullScreenOverlay", new string[] { "overlay_texture", "threshold_value", "threshold_start", "threshold_stop", "threshold_range", "alpha_scalar" });
            node_parameters.Add("GameDVR", new string[] { "start_time", "duration", "moment_ID" });
            node_parameters.Add("GameOver", new string[] { "tip_string_id", "default_tips_enabled", "level_tips_enabled" });
            node_parameters.Add("GameOverCredits", new string[] { });
            node_parameters.Add("GameplayTip", new string[] { "string_id" });
            node_parameters.Add("GameStateChanged", new string[] { "mission_number" });
            node_parameters.Add("GateResourceInterface", new string[] { "gate_status_changed", "request_open_on_reset", "request_lock_on_reset", "force_open_on_reset", "force_close_on_reset", "is_auto", "auto_close_delay", "is_open", "is_locked", "gate_status" });
            node_parameters.Add("GenericHighlightEntity", new string[] { "highlight_geometry" });
            node_parameters.Add("GetBlueprintAvailable", new string[] { "available", "type" });
            node_parameters.Add("GetBlueprintLevel", new string[] { "level", "type" });
            node_parameters.Add("GetCentrePoint", new string[] { "Positions", "position_of_centre" });
            node_parameters.Add("GetCharacterRotationSpeed", new string[] { "character", "speed" });
            node_parameters.Add("GetClosestPercentOnSpline", new string[] { "spline", "pos_to_be_near", "position_on_spline", "Result", "bidirectional" });
            node_parameters.Add("GetClosestPoint", new string[] { "bound_to_closest", "Positions", "pos_to_be_near", "position_of_closest" });
            node_parameters.Add("GetClosestPointFromSet", new string[] { "closest_is_1", "closest_is_2", "closest_is_3", "closest_is_4", "closest_is_5", "closest_is_6", "closest_is_7", "closest_is_8", "closest_is_9", "closest_is_10", "Position_1", "Position_2", "Position_3", "Position_4", "Position_5", "Position_6", "Position_7", "Position_8", "Position_9", "Position_10", "pos_to_be_near", "position_of_closest", "index_of_closest" });
            node_parameters.Add("GetClosestPointOnSpline", new string[] { "spline", "pos_to_be_near", "position_on_spline", "look_ahead_distance", "unidirectional", "directional_damping_threshold" });
            node_parameters.Add("GetComponentInterface", new string[] { "Input", "Result" });
            node_parameters.Add("GetCurrentCameraFov", new string[] { });
            node_parameters.Add("GetCurrentCameraPos", new string[] { });
            node_parameters.Add("GetCurrentCameraTarget", new string[] { "target", "distance" });
            node_parameters.Add("GetCurrentPlaylistLevelIndex", new string[] { "index" });
            node_parameters.Add("GetFlashFloatValue", new string[] { "callback", "enable_on_reset", "float_value", "callback_name" });
            node_parameters.Add("GetFlashIntValue", new string[] { "callback", "enable_on_reset", "int_value", "callback_name" });
            node_parameters.Add("GetGatingToolLevel", new string[] { "level", "tool_type" });
            node_parameters.Add("GetInventoryItemName", new string[] { "item", "equippable_item" });
            node_parameters.Add("GetNextPlaylistLevelName", new string[] { "level_name" });
            node_parameters.Add("GetPlayerHasGatingTool", new string[] { "has_tool", "doesnt_have_tool", "tool_type" });
            node_parameters.Add("GetPlayerHasKeycard", new string[] { "has_card", "doesnt_have_card", "card_uid" });
            node_parameters.Add("GetPointOnSpline", new string[] { "spline", "percentage_of_spline", "Result" });
            node_parameters.Add("GetRotation", new string[] { "Input", "Result" });
            node_parameters.Add("GetSelectedCharacterId", new string[] { "character_id" });
            node_parameters.Add("GetSplineLength", new string[] { "spline", "Result" });
            node_parameters.Add("GetTranslation", new string[] { "Input", "Result" });
            node_parameters.Add("GetX", new string[] { });
            node_parameters.Add("GetY", new string[] { });
            node_parameters.Add("GetZ", new string[] { });
            node_parameters.Add("GlobalEvent", new string[] { "EventValue", "EventName" });
            node_parameters.Add("GlobalEventMonitor", new string[] { "Event_1", "Event_2", "Event_3", "Event_4", "Event_5", "Event_6", "Event_7", "Event_8", "Event_9", "Event_10", "Event_11", "Event_12", "Event_13", "Event_14", "Event_15", "Event_16", "Event_17", "Event_18", "Event_19", "Event_20", "EventName" });
            node_parameters.Add("GlobalPosition", new string[] { "PositionName" });
            node_parameters.Add("GoToFrontend", new string[] { "frontend_state" });
            node_parameters.Add("GPU_PFXEmitterReference", new string[] { "start_on_reset", "deleted", "mastered_by_visibility", "EFFECT_NAME", "SPAWN_NUMBER", "SPAWN_RATE", "SPREAD_MIN", "SPREAD_MAX", "EMITTER_SIZE", "SPEED", "SPEED_VAR", "LIFETIME", "LIFETIME_VAR" });
            node_parameters.Add("HableToneMappingSettings", new string[] { "shoulder_strength", "linear_strength", "linear_angle", "toe_strength", "toe_numerator", "toe_denominator", "linear_white_point" });
            node_parameters.Add("HackingGame", new string[] { "win", "fail", "alarm_triggered", "closed", "loaded_idle", "loaded_success", "ui_breakout_triggered", "resources_finished_unloading", "resources_finished_loading", "lock_on_reset", "light_on_reset", "completion_percentage", "hacking_difficulty", "auto_exit" });
            node_parameters.Add("HandCamera", new string[] { "noise_type", "frequency", "damping", "rotation_intensity", "min_fov_range", "max_fov_range", "min_noise", "max_noise" });
            node_parameters.Add("HasAccessAtDifficulty", new string[] { "difficulty" });
            node_parameters.Add("HeldItem_AINotifier", new string[] { "Item", "Duration" });
            node_parameters.Add("HighSpecMotionBlurSettings", new string[] { "contribution", "camera_velocity_scalar", "camera_velocity_min", "camera_velocity_max", "object_velocity_scalar", "object_velocity_min", "object_velocity_max", "blur_range" });
            node_parameters.Add("HostOnlyTrigger", new string[] { "on_triggered" });
            node_parameters.Add("IdleTask", new string[] { "start_pre_move", "start_interrupt", "interrupted_while_moving", "specific_character", "should_auto_move_to_position", "ignored_for_auto_selection", "has_pre_move_script", "has_interrupt_script", "filter_options" });
            node_parameters.Add("ImpactSphere", new string[] { "event", "radius", "include_physics" });
            node_parameters.Add("InhibitActionsUntilRelease", new string[] { });
            node_parameters.Add("IntegerAbsolute", new string[] { });
            node_parameters.Add("IntegerAdd", new string[] { });
            node_parameters.Add("IntegerAdd_All", new string[] { });
            node_parameters.Add("IntegerAnalyse", new string[] { "Input", "Result", "Val0", "Val1", "Val2", "Val3", "Val4", "Val5", "Val6", "Val7", "Val8", "Val9" });
            node_parameters.Add("IntegerAnd", new string[] { });
            node_parameters.Add("IntegerCompare", new string[] { "on_true", "on_false", "LHS", "RHS", "Result" });
            node_parameters.Add("IntegerCompliment", new string[] { });
            node_parameters.Add("IntegerDivide", new string[] { });
            node_parameters.Add("IntegerEquals", new string[] { });
            node_parameters.Add("IntegerGreaterThan", new string[] { });
            node_parameters.Add("IntegerGreaterThanOrEqual", new string[] { });
            node_parameters.Add("IntegerLessThan", new string[] { });
            node_parameters.Add("IntegerLessThanOrEqual", new string[] { });
            node_parameters.Add("IntegerMath", new string[] { "LHS", "RHS", "Result" });
            node_parameters.Add("IntegerMath_All", new string[] { "Numbers", "Result" });
            node_parameters.Add("IntegerMax", new string[] { });
            node_parameters.Add("IntegerMax_All", new string[] { });
            node_parameters.Add("IntegerMin", new string[] { });
            node_parameters.Add("IntegerMin_All", new string[] { });
            node_parameters.Add("IntegerMultiply", new string[] { });
            node_parameters.Add("IntegerMultiply_All", new string[] { });
            node_parameters.Add("IntegerNotEqual", new string[] { });
            node_parameters.Add("IntegerOperation", new string[] { "Input", "Result" });
            node_parameters.Add("IntegerOr", new string[] { });
            node_parameters.Add("IntegerRemainder", new string[] { });
            node_parameters.Add("IntegerSubtract", new string[] { });
            node_parameters.Add("Interaction", new string[] { "on_damaged", "on_interrupt", "on_killed", "interruptible_on_start" });
            node_parameters.Add("InteractiveMovementControl", new string[] { "completed", "duration", "start_time", "progress_path", "result", "speed", "can_go_both_ways", "use_left_input_stick", "base_progress_speed", "movement_threshold", "momentum_damping", "track_bone_position", "character_node", "track_position" });
            node_parameters.Add("Internal_JOB_SearchTarget", new string[] { });
            node_parameters.Add("InventoryItem", new string[] { "collect", "itemName", "out_itemName", "out_quantity", "item", "quantity", "clear_on_collect", "gcip_instances_count" });
            node_parameters.Add("IrawanToneMappingSettings", new string[] { "target_device_luminance", "target_device_adaptation", "saccadic_time", "superbright_adaptation" });
            node_parameters.Add("IsActive", new string[] { });
            node_parameters.Add("IsAttached", new string[] { });
            node_parameters.Add("IsCurrentLevelAChallengeMap", new string[] { "challenge_map" });
            node_parameters.Add("IsCurrentLevelAPreorderMap", new string[] { "preorder_map" });
            node_parameters.Add("IsEnabled", new string[] { });
            node_parameters.Add("IsInstallComplete", new string[] { });
            node_parameters.Add("IsLoaded", new string[] { });
            node_parameters.Add("IsLoading", new string[] { });
            node_parameters.Add("IsLocked", new string[] { });
            node_parameters.Add("IsMultiplayerMode", new string[] { });
            node_parameters.Add("IsOpen", new string[] { });
            node_parameters.Add("IsOpening", new string[] { });
            node_parameters.Add("IsPaused", new string[] { });
            node_parameters.Add("IsPlaylistTypeAll", new string[] { "all" });
            node_parameters.Add("IsPlaylistTypeMarathon", new string[] { "marathon" });
            node_parameters.Add("IsPlaylistTypeSingle", new string[] { "single" });
            node_parameters.Add("IsSpawned", new string[] { });
            node_parameters.Add("IsStarted", new string[] { });
            node_parameters.Add("IsSuspended", new string[] { });
            node_parameters.Add("IsVisible", new string[] { });
            node_parameters.Add("Job", new string[] { "start_on_reset" });
            node_parameters.Add("JOB_AreaSweep", new string[] { });
            node_parameters.Add("JOB_AreaSweepFlare", new string[] { });
            node_parameters.Add("JOB_Assault", new string[] { });
            node_parameters.Add("JOB_Follow", new string[] { });
            node_parameters.Add("JOB_Follow_Centre", new string[] { });
            node_parameters.Add("JOB_Idle", new string[] { "task_operation_mode", "should_perform_all_tasks" });
            node_parameters.Add("JOB_Panic", new string[] { });
            node_parameters.Add("JOB_SpottingPosition", new string[] { "SpottingPosition" });
            node_parameters.Add("JOB_SystematicSearch", new string[] { });
            node_parameters.Add("JOB_SystematicSearchFlare", new string[] { });
            node_parameters.Add("JobWithPosition", new string[] { });
            node_parameters.Add("LeaderboardWriter", new string[] { "time_elapsed", "score", "level_number", "grade", "player_character", "combat", "stealth", "improv", "star1", "star2", "star3" });
            node_parameters.Add("LeaveGame", new string[] { "disconnect_from_session" });
            node_parameters.Add("LensDustSettings", new string[] { "DUST_MAX_REFLECTED_BLOOM_INTENSITY", "DUST_REFLECTED_BLOOM_INTENSITY_SCALAR", "DUST_MAX_BLOOM_INTENSITY", "DUST_BLOOM_INTENSITY_SCALAR", "DUST_THRESHOLD" });
            node_parameters.Add("LevelCompletionTargets", new string[] { "TargetTime", "NumDeaths", "TeamRespawnBonus", "NoLocalRespawnBonus", "NoRespawnBonus", "GrappleBreakBonus" });
            node_parameters.Add("LevelInfo", new string[] { "save_level_name_id" });
            node_parameters.Add("LevelLoaded", new string[] { });
            node_parameters.Add("LightAdaptationSettings", new string[] { "fast_neural_t0", "slow_neural_t0", "pigment_bleaching_t0", "fb_luminance_to_candelas_per_m2", "max_adaptation_lum", "min_adaptation_lum", "adaptation_percentile", "low_bracket", "high_bracket", "adaptation_mechanism" });
            node_parameters.Add("LightingMaster", new string[] { "light_on_reset", "objects" });
            node_parameters.Add("LightReference", new string[] { "deleted", "show_on_reset", "light_on_reset", "occlusion_geometry", "mastered_by_visibility", "exclude_shadow_entities", "type", "defocus_attenuation", "start_attenuation", "end_attenuation", "physical_attenuation", "near_dist", "near_dist_shadow_offset", "inner_cone_angle", "outer_cone_angle", "intensity_multiplier", "radiosity_multiplier", "area_light_radius", "diffuse_softness", "diffuse_bias", "glossiness_scale", "flare_occluder_radius", "flare_spot_offset", "flare_intensity_scale", "cast_shadow", "fade_type", "is_specular", "has_lens_flare", "has_noclip", "is_square_light", "is_flash_light", "no_alphalight", "include_in_planar_reflections", "shadow_priority", "aspect_ratio", "gobo_texture", "horizontal_gobo_flip", "colour", "strip_length", "distance_mip_selection_gobo", "volume", "volume_end_attenuation", "volume_colour_factor", "volume_density", "depth_bias", "slope_scale_depth_bias", "resource" });
            node_parameters.Add("LimitItemUse", new string[] { "enable_on_reset", "items" });
            node_parameters.Add("LODControls", new string[] { "lod_range_scalar", "disable_lods" });
            node_parameters.Add("Logic_MultiGate", new string[] { "Underflow", "Pin_1", "Pin_2", "Pin_3", "Pin_4", "Pin_5", "Pin_6", "Pin_7", "Pin_8", "Pin_9", "Pin_10", "Pin_11", "Pin_12", "Pin_13", "Pin_14", "Pin_15", "Pin_16", "Pin_17", "Pin_18", "Pin_19", "Pin_20", "Overflow", "trigger_pin" });
            node_parameters.Add("Logic_Vent_Entrance", new string[] { "Hide_Pos", "Emit_Pos", "force_stand_on_exit" });
            node_parameters.Add("Logic_Vent_System", new string[] { "Vent_Entrances" });
            node_parameters.Add("LogicAll", new string[] { "Pin1_Synced", "Pin2_Synced", "Pin3_Synced", "Pin4_Synced", "Pin5_Synced", "Pin6_Synced", "Pin7_Synced", "Pin8_Synced", "Pin9_Synced", "Pin10_Synced", "num", "reset_on_trigger" });
            node_parameters.Add("LogicCounter", new string[] { "on_under_limit", "on_limit", "on_over_limit", "restored_on_under_limit", "restored_on_limit", "restored_on_over_limit", "Count", "is_limitless", "trigger_limit", "non_persistent" });
            node_parameters.Add("LogicDelay", new string[] { "on_delay_finished", "delay", "can_suspend" });
            node_parameters.Add("LogicGate", new string[] { "on_allowed", "on_disallowed", "allow" });
            node_parameters.Add("LogicGateAnd", new string[] { });
            node_parameters.Add("LogicGateEquals", new string[] { });
            node_parameters.Add("LogicGateNotEqual", new string[] { });
            node_parameters.Add("LogicGateOr", new string[] { });
            node_parameters.Add("LogicNot", new string[] { });
            node_parameters.Add("LogicOnce", new string[] { "on_success", "on_failure" });
            node_parameters.Add("LogicPressurePad", new string[] { "Pad_Activated", "Pad_Deactivated", "bound_characters", "Limit", "Count" });
            node_parameters.Add("LogicSwitch", new string[] { "true_now_false", "false_now_true", "on_true", "on_false", "on_restored_true", "on_restored_false", "initial_value", "is_persistent" });
            node_parameters.Add("LowResFrameCapture", new string[] { });
            node_parameters.Add("Map_Floor_Change", new string[] { "floor_name" });
            node_parameters.Add("MapAnchor", new string[] { "map_north", "map_pos", "map_scale", "keyframe", "keyframe1", "keyframe2", "keyframe3", "keyframe4", "keyframe5", "world_pos", "is_default_for_items" });
            node_parameters.Add("MapItem", new string[] { "show_ui_on_reset", "item_type", "map_keyframe" });
            node_parameters.Add("Master", new string[] { "suspend_on_reset", "objects", "disable_display", "disable_collision", "disable_simulation" });
            node_parameters.Add("MELEE_WEAPON", new string[] { "item_animated_model_and_collision", "normal_attack_damage", "power_attack_damage", "position_input" });
            node_parameters.Add("Minigames", new string[] { "on_success", "on_failure", "game_inertial_damping_active", "game_green_text_active", "game_yellow_chart_active", "game_overloc_fail_active", "game_docking_active", "game_environ_ctr_active", "config_pass_number", "config_fail_limit", "config_difficulty" });
            node_parameters.Add("MissionNumber", new string[] { "on_changed" });
            node_parameters.Add("ModelReference", new string[] { "on_damaged", "show_on_reset", "enable_on_reset", "simulate_on_reset", "light_on_reset", "convert_to_physics", "material", "occludes_atmosphere", "include_in_planar_reflections", "lod_ranges", "intensity_multiplier", "radiosity_multiplier", "emissive_tint", "replace_intensity", "replace_tint", "decal_scale", "lightdecal_tint", "lightdecal_intensity", "diffuse_colour_scale", "diffuse_opacity_scale", "vertex_colour_scale", "vertex_opacity_scale", "uv_scroll_speed_x", "uv_scroll_speed_y", "alpha_blend_noise_power_scale", "alpha_blend_noise_uv_scale", "alpha_blend_noise_uv_offset_X", "alpha_blend_noise_uv_offset_Y", "dirt_multiply_blend_spec_power_scale", "dirt_map_uv_scale", "remove_on_damaged", "damage_threshold", "is_debris", "is_prop", "is_thrown", "report_sliding", "force_keyframed", "force_transparent", "soft_collision", "allow_reposition_of_physics", "disable_size_culling", "cast_shadows", "cast_shadows_in_torch", "resource", "alpha_light_offset_x", "alpha_light_offset_y", "alpha_light_scale_x", "alpha_light_scale_y", "alpha_light_average_normal" });
            node_parameters.Add("MonitorActionMap", new string[] { "on_pressed_use", "on_released_use", "on_pressed_crouch", "on_released_crouch", "on_pressed_run", "on_released_run", "on_pressed_aim", "on_released_aim", "on_pressed_shoot", "on_released_shoot", "on_pressed_reload", "on_released_reload", "on_pressed_melee", "on_released_melee", "on_pressed_activate_item", "on_released_activate_item", "on_pressed_switch_weapon", "on_released_switch_weapon", "on_pressed_change_dof_focus", "on_released_change_dof_focus", "on_pressed_select_motion_tracker", "on_released_select_motion_tracker", "on_pressed_select_torch", "on_released_select_torch", "on_pressed_torch_beam", "on_released_torch_beam", "on_pressed_peek", "on_released_peek", "on_pressed_back_close", "on_released_back_close", "movement_stick_x", "movement_stick_y", "camera_stick_x", "camera_stick_y", "mouse_x", "mouse_y", "analog_aim", "analog_shoot" });
            node_parameters.Add("MonitorBase", new string[] { });
            node_parameters.Add("MonitorPadInput", new string[] { "on_pressed_A", "on_released_A", "on_pressed_B", "on_released_B", "on_pressed_X", "on_released_X", "on_pressed_Y", "on_released_Y", "on_pressed_L1", "on_released_L1", "on_pressed_R1", "on_released_R1", "on_pressed_L2", "on_released_L2", "on_pressed_R2", "on_released_R2", "on_pressed_L3", "on_released_L3", "on_pressed_R3", "on_released_R3", "on_dpad_left", "on_released_dpad_left", "on_dpad_right", "on_released_dpad_right", "on_dpad_up", "on_released_dpad_up", "on_dpad_down", "on_released_dpad_down", "left_stick_x", "left_stick_y", "right_stick_x", "right_stick_y" });
            node_parameters.Add("MotionTrackerMonitor", new string[] { "on_motion_sound", "on_enter_range_sound" });
            node_parameters.Add("MotionTrackerPing", new string[] { "FakePosition" });
            node_parameters.Add("MoveAlongSpline", new string[] { "on_think", "on_finished", "spline", "speed", "Result" });
            node_parameters.Add("MoveInTime", new string[] { "on_finished", "start_position", "end_position", "result", "duration" });
            node_parameters.Add("MoviePlayer", new string[] { "start", "end", "skipped", "trigger_end_on_skipped", "filename", "skippable", "enable_debug_skip" });
            node_parameters.Add("MultipleCharacterAttachmentNode", new string[] { "attach_on_reset", "character_01", "attachment_01", "character_02", "attachment_02", "character_03", "attachment_03", "character_04", "attachment_04", "character_05", "attachment_05", "node", "use_offset", "translation", "rotation", "is_cinematic" });
            node_parameters.Add("MultiplePickupSpawner", new string[] { "pos", "item_name" });
            node_parameters.Add("MultitrackLoop", new string[] { "current_time", "loop_condition", "start_time", "end_time" });
            node_parameters.Add("MusicController", new string[] { "music_start_event", "music_end_event", "music_restart_event", "layer_control_rtpc", "smooth_rate", "alien_max_distance", "object_max_distance" });
            node_parameters.Add("MusicTrigger", new string[] { "on_triggered", "connected_object", "music_event", "smooth_rate", "queue_time", "interrupt_all", "trigger_once", "rtpc_set_mode", "rtpc_target_value", "rtpc_duration", "rtpc_set_return_mode", "rtpc_return_value" });
            node_parameters.Add(@"n:\content\build\library\archetypes\gameplay\gcip_worldpickup", new string[] { "spawn_completed", "pickup_collected", "Pipe", "Gasoline", "Explosive", "Battery", "Blade", "Gel", "Adhesive", "BoltGun Ammo", "Revolver Ammo", "Shotgun Ammo", "BoltGun", "Revolver", "Shotgun", "Flare", "Flamer Fuel", "Flamer", "Scrap", "Torch Battery", "Torch", "Cattleprod Ammo", "Cattleprod", "StartOnReset", "MissionNumber" });
            node_parameters.Add(@"n:\content\build\library\archetypes\script\gameplay\torch_control", new string[] { "torch_switched_off", "torch_switched_on", "character" });
            node_parameters.Add(@"n:\content\build\library\ayz\animation\logichelpers\playforminduration", new string[] { "timer_expired", "first_animation_started", "next_animation", "all_animations_finished", "MinDuration" });
            node_parameters.Add("NavMeshArea", new string[] { "position", "half_dimensions", "area_type" });
            node_parameters.Add("NavMeshBarrier", new string[] { "open_on_reset", "position", "half_dimensions", "opaque", "allowed_character_classes_when_open", "allowed_character_classes_when_closed", "resource" });
            node_parameters.Add("NavMeshExclusionArea", new string[] { "position", "half_dimensions" });
            node_parameters.Add("NavMeshReachabilitySeedPoint", new string[] { "position" });
            node_parameters.Add("NavMeshWalkablePlatform", new string[] { "position", "half_dimensions" });
            node_parameters.Add("NetPlayerCounter", new string[] { "on_full", "on_empty", "on_intermediate", "is_full", "is_empty", "contains_local_player" });
            node_parameters.Add("NetworkedTimer", new string[] { "on_second_changed", "on_started_counting", "on_finished_counting", "time_elapsed", "time_left", "time_elapsed_sec", "time_left_sec", "duration" });
            node_parameters.Add("NonInteractiveWater", new string[] { "water_resource", "SCALE_X", "SCALE_Z", "SHININESS", "SPEED", "SCALE", "NORMAL_MAP_STRENGTH", "SECONDARY_SPEED", "SECONDARY_SCALE", "SECONDARY_NORMAL_MAP_STRENGTH", "CYCLE_TIME", "FLOW_SPEED", "FLOW_TEX_SCALE", "FLOW_WARP_STRENGTH", "FRESNEL_POWER", "MIN_FRESNEL", "MAX_FRESNEL", "ENVIRONMENT_MAP_MULT", "ENVMAP_SIZE", "ENVMAP_BOXPROJ_BB_SCALE", "REFLECTION_PERTURBATION_STRENGTH", "ALPHA_PERTURBATION_STRENGTH", "ALPHALIGHT_MULT", "softness_edge", "DEPTH_FOG_INITIAL_COLOUR", "DEPTH_FOG_INITIAL_ALPHA", "DEPTH_FOG_MIDPOINT_COLOUR", "DEPTH_FOG_MIDPOINT_ALPHA", "DEPTH_FOG_MIDPOINT_DEPTH", "DEPTH_FOG_END_COLOUR", "DEPTH_FOG_END_ALPHA", "DEPTH_FOG_END_DEPTH" });
            node_parameters.Add("NonPersistentBool", new string[] { "initial_value" });
            node_parameters.Add("NonPersistentInt", new string[] { "initial_value", "is_persistent" });
            node_parameters.Add("NPC_Aggression_Monitor", new string[] { "on_interrogative", "on_warning", "on_last_chance", "on_stand_down", "on_idle", "on_aggressive" });
            node_parameters.Add("NPC_AlienConfig", new string[] { "AlienConfigString" });
            node_parameters.Add("NPC_AllSensesLimiter", new string[] { });
            node_parameters.Add("NPC_ambush_monitor", new string[] { "setup", "abandoned", "trap_sprung", "ambush_type", "trigger_on_start", "trigger_on_checkpoint_restart" });
            node_parameters.Add("NPC_AreaBox", new string[] { "half_dimensions", "position" });
            node_parameters.Add("NPC_behaviour_monitor", new string[] { "state_set", "state_unset", "behaviour", "trigger_on_start", "trigger_on_checkpoint_restart" });
            node_parameters.Add("NPC_ClearDefendArea", new string[] { });
            node_parameters.Add("NPC_ClearPursuitArea", new string[] { });
            node_parameters.Add("NPC_Coordinator", new string[] { "Target", "trigger_on_start", "CheckAllNPCs" });
            node_parameters.Add("NPC_Debug_Menu_Item", new string[] { "character" });
            node_parameters.Add("NPC_DefineBackstageAvoidanceArea", new string[] { "AreaObjects" });
            node_parameters.Add("NPC_DynamicDialogue", new string[] { });
            node_parameters.Add("NPC_DynamicDialogueGlobalRange", new string[] { "dialogue_range" });
            node_parameters.Add("NPC_FakeSense", new string[] { "SensedObject", "FakePosition", "Sense", "ForceThreshold" });
            node_parameters.Add("NPC_FollowOffset", new string[] { "offset", "target_to_follow", "Result" });
            node_parameters.Add("NPC_ForceCombatTarget", new string[] { "Target", "LockOtherAttackersOut" });
            node_parameters.Add("NPC_ForceNextJob", new string[] { "job_started", "job_completed", "job_interrupted", "ShouldInterruptCurrentTask", "Job", "InitialTask" });
            node_parameters.Add("NPC_ForceRetreat", new string[] { "AreaObjects" });
            node_parameters.Add("NPC_Gain_Aggression_In_Radius", new string[] { "Position", "Radius", "AggressionGain" });
            node_parameters.Add("NPC_GetCombatTarget", new string[] { "bound_trigger", "target" });
            node_parameters.Add("NPC_GetLastSensedPositionOfTarget", new string[] { "NoRecentSense", "SensedOnLeft", "SensedOnRight", "SensedInFront", "SensedBehind", "OptionalTarget", "LastSensedPosition", "MaxTimeSince" });
            node_parameters.Add("NPC_Group_Death_Monitor", new string[] { "last_man_dying", "all_killed", "squad_coordinator", "CheckAllNPCs" });
            node_parameters.Add("NPC_Group_DeathCounter", new string[] { "on_threshold", "TriggerThreshold" });
            node_parameters.Add("NPC_Highest_Awareness_Monitor", new string[] { "All_Dead", "Stunned", "Unaware", "Suspicious", "SearchingArea", "SearchingLastSensed", "Aware", "on_changed" });
            node_parameters.Add("NPC_MeleeContext", new string[] { "ConvergePos", "Radius", "Context_Type" });
            node_parameters.Add("NPC_multi_behaviour_monitor", new string[] { "Cinematic_set", "Cinematic_unset", "Damage_Response_set", "Damage_Response_unset", "Target_Is_NPC_set", "Target_Is_NPC_unset", "Breakout_set", "Breakout_unset", "Attack_set", "Attack_unset", "Stunned_set", "Stunned_unset", "Backstage_set", "Backstage_unset", "In_Vent_set", "In_Vent_unset", "Killtrap_set", "Killtrap_unset", "Threat_Aware_set", "Threat_Aware_unset", "Suspect_Target_Response_set", "Suspect_Target_Response_unset", "Player_Hiding_set", "Player_Hiding_unset", "Suspicious_Item_set", "Suspicious_Item_unset", "Search_set", "Search_unset", "Area_Sweep_set", "Area_Sweep_unset", "trigger_on_start", "trigger_on_checkpoint_restart" });
            node_parameters.Add("NPC_navmesh_type_monitor", new string[] { "state_set", "state_unset", "nav_mesh_type", "trigger_on_start", "trigger_on_checkpoint_restart" });
            node_parameters.Add("NPC_NotifyDynamicDialogueEvent", new string[] { "DialogueEvent" });
            node_parameters.Add("NPC_Once", new string[] { "on_success", "on_failure" });
            node_parameters.Add("NPC_ResetFiringStats", new string[] { });
            node_parameters.Add("NPC_ResetSensesAndMemory", new string[] { "ResetMenaceToFull", "ResetSensesLimiters" });
            node_parameters.Add("NPC_SenseLimiter", new string[] { "Sense" });
            node_parameters.Add("NPC_set_behaviour_tree_flags", new string[] { "BehaviourTreeFlag", "FlagSetting" });
            node_parameters.Add("NPC_SetAgressionProgression", new string[] { "allow_progression" });
            node_parameters.Add("NPC_SetAimTarget", new string[] { "Target" });
            node_parameters.Add("NPC_SetAlertness", new string[] { "AlertState" });
            node_parameters.Add("NPC_SetAlienDevelopmentStage", new string[] { "AlienStage", "Reset" });
            node_parameters.Add("NPC_SetAutoTorchMode", new string[] { "AutoUseTorchInDark" });
            node_parameters.Add("NPC_SetChokePoint", new string[] { "chokepoints" });
            node_parameters.Add("NPC_SetDefendArea", new string[] { "AreaObjects" });
            node_parameters.Add("NPC_SetFiringAccuracy", new string[] { "Accuracy" });
            node_parameters.Add("NPC_SetFiringRhythm", new string[] { "MinShootingTime", "RandomRangeShootingTime", "MinNonShootingTime", "RandomRangeNonShootingTime", "MinCoverNonShootingTime", "RandomRangeCoverNonShootingTime" });
            node_parameters.Add("NPC_SetGunAimMode", new string[] { "AimingMode" });
            node_parameters.Add("NPC_SetHidingNearestLocation", new string[] { "hiding_pos" });
            node_parameters.Add("NPC_SetHidingSearchRadius", new string[] { "Radius" });
            node_parameters.Add("NPC_SetInvisible", new string[] { });
            node_parameters.Add("NPC_SetLocomotionStyleForJobs", new string[] { });
            node_parameters.Add("NPC_SetLocomotionTargetSpeed", new string[] { "Speed" });
            node_parameters.Add("NPC_SetPursuitArea", new string[] { "AreaObjects" });
            node_parameters.Add("NPC_SetRateOfFire", new string[] { "MinTimeBetweenShots", "RandomRange" });
            node_parameters.Add("NPC_SetSafePoint", new string[] { "SafePositions" });
            node_parameters.Add("NPC_SetSenseSet", new string[] { "SenseSet" });
            node_parameters.Add("NPC_SetStartPos", new string[] { "StartPos" });
            node_parameters.Add("NPC_SetTotallyBlindInDark", new string[] { });
            node_parameters.Add("NPC_SetupMenaceManager", new string[] { "AgressiveMenace", "ProgressionFraction", "ResetMenaceMeter" });
            node_parameters.Add("NPC_Sleeping_Android_Monitor", new string[] { "Twitch", "SitUp_Start", "SitUp_End", "Sleeping_GetUp", "Sitting_GetUp", "Android_NPC" });
            node_parameters.Add("NPC_Squad_DialogueMonitor", new string[] { "Suspicious_Item_Initial", "Suspicious_Item_Close", "Suspicious_Warning", "Suspicious_Warning_Fail", "Missing_Buddy", "Search_Started", "Search_Loop", "Search_Complete", "Detected_Enemy", "Alien_Heard_Backstage", "Interrogative", "Warning", "Last_Chance", "Stand_Down", "Attack", "Advance", "Melee", "Hit_By_Weapon", "Go_to_Cover", "No_Cover", "Shoot_From_Cover", "Cover_Broken", "Retreat", "Panic", "Final_Hit", "Ally_Death", "Incoming_IED", "Alert_Squad", "My_Death", "Idle_Passive", "Idle_Aggressive", "Block", "Enter_Grapple", "Grapple_From_Cover", "Player_Observed", "squad_coordinator" });
            node_parameters.Add("NPC_Squad_GetAwarenessState", new string[] { "All_Dead", "Stunned", "Unaware", "Suspicious", "SearchingArea", "SearchingLastSensed", "Aware" });
            node_parameters.Add("NPC_Squad_GetAwarenessWatermark", new string[] { "All_Dead", "Stunned", "Unaware", "Suspicious", "SearchingArea", "SearchingLastSensed", "Aware" });
            node_parameters.Add("NPC_StopAiming", new string[] { });
            node_parameters.Add("NPC_StopShooting", new string[] { });
            node_parameters.Add("NPC_SuspiciousItem", new string[] { "ItemPosition", "Item", "InitialReactionValidStartDuration", "FurtherReactionValidStartDuration", "RetriggerDelay", "Trigger", "ShouldMakeAggressive", "MaxGroupMembersInteract", "SystematicSearchRadius", "AllowSamePriorityToOveride", "UseSamePriorityCloserDistanceConstraint", "SamePriorityCloserDistanceConstraint", "UseSamePriorityRecentTimeConstraint", "SamePriorityRecentTimeConstraint", "BehaviourTreePriority", "InteruptSubPriority", "DetectableByBackstageAlien", "DoIntialReaction", "MoveCloseToSuspectPosition", "DoCloseToReaction", "DoCloseToWaitForGroupMembers", "DoSystematicSearch", "GroupNotify", "DoIntialReactionSubsequentGroupMember", "MoveCloseToSuspectPositionSubsequentGroupMember", "DoCloseToReactionSubsequentGroupMember", "DoCloseToWaitForGroupMembersSubsequentGroupMember", "DoSystematicSearchSubsequentGroupMember" });
            node_parameters.Add("NPC_TargetAcquire", new string[] { "no_targets" });
            node_parameters.Add("NPC_TriggerAimRequest", new string[] { "started_aiming", "finished_aiming", "interrupted", "AimTarget", "Raise_gun", "use_current_target", "duration", "clamp_angle", "clear_current_requests" });
            node_parameters.Add("NPC_TriggerShootRequest", new string[] { "started_shooting", "finished_shooting", "interrupted", "empty_current_clip", "shot_count", "duration", "clear_current_requests" });
            node_parameters.Add("NPC_WithdrawAlien", new string[] { "allow_any_searches_to_complete", "permanent", "killtraps", "initial_radius", "timed_out_radius", "time_to_force" });
            node_parameters.Add("NumConnectedPlayers", new string[] { "on_count_changed", "count" });
            node_parameters.Add("NumDeadPlayers", new string[] { });
            node_parameters.Add("NumPlayersOnStart", new string[] { "count" });
            node_parameters.Add("PadLightBar", new string[] { "colour" });
            node_parameters.Add("PadRumbleImpulse", new string[] { "low_frequency_rumble", "high_frequency_rumble", "left_trigger_impulse", "right_trigger_impulse", "aim_trigger_impulse", "shoot_trigger_impulse" });
            node_parameters.Add("ParticipatingPlayersList", new string[] { });
            node_parameters.Add("ParticleEmitterReference", new string[] { "start_on_reset", "show_on_reset", "deleted", "mastered_by_visibility", "use_local_rotation", "include_in_planar_reflections", "material", "unique_material", "quality_level", "bounds_max", "bounds_min", "TEXTURE_MAP", "DRAW_PASS", "ASPECT_RATIO", "FADE_AT_DISTANCE", "PARTICLE_COUNT", "SYSTEM_EXPIRY_TIME", "SIZE_START_MIN", "SIZE_START_MAX", "SIZE_END_MIN", "SIZE_END_MAX", "ALPHA_IN", "ALPHA_OUT", "MASK_AMOUNT_MIN", "MASK_AMOUNT_MAX", "MASK_AMOUNT_MIDPOINT", "PARTICLE_EXPIRY_TIME_MIN", "PARTICLE_EXPIRY_TIME_MAX", "COLOUR_SCALE_MIN", "COLOUR_SCALE_MAX", "WIND_X", "WIND_Y", "WIND_Z", "ALPHA_REF_VALUE", "BILLBOARDING_LS", "BILLBOARDING", "BILLBOARDING_NONE", "BILLBOARDING_ON_AXIS_X", "BILLBOARDING_ON_AXIS_Y", "BILLBOARDING_ON_AXIS_Z", "BILLBOARDING_VELOCITY_ALIGNED", "BILLBOARDING_VELOCITY_STRETCHED", "BILLBOARDING_SPHERE_PROJECTION", "BLENDING_STANDARD", "BLENDING_ALPHA_REF", "BLENDING_ADDITIVE", "BLENDING_PREMULTIPLIED", "BLENDING_DISTORTION", "LOW_RES", "EARLY_ALPHA", "LOOPING", "ANIMATED_ALPHA", "NONE", "LIGHTING", "PER_PARTICLE_LIGHTING", "X_AXIS_FLIP", "Y_AXIS_FLIP", "BILLBOARD_FACING", "BILLBOARDING_ON_AXIS_FADEOUT", "BILLBOARDING_CAMERA_LOCKED", "CAMERA_RELATIVE_POS_X", "CAMERA_RELATIVE_POS_Y", "CAMERA_RELATIVE_POS_Z", "SPHERE_PROJECTION_RADIUS", "DISTORTION_STRENGTH", "SCALE_MODIFIER", "CPU", "SPAWN_RATE", "SPAWN_RATE_VAR", "SPAWN_NUMBER", "LIFETIME", "LIFETIME_VAR", "WORLD_TO_LOCAL_BLEND_START", "WORLD_TO_LOCAL_BLEND_END", "WORLD_TO_LOCAL_MAX_DIST", "CELL_EMISSION", "CELL_MAX_DIST", "CUSTOM_SEED_CPU", "SEED", "ALPHA_TEST", "ZTEST", "START_MID_END_SPEED", "SPEED_START_MIN", "SPEED_START_MAX", "SPEED_MID_MIN", "SPEED_MID_MAX", "SPEED_END_MIN", "SPEED_END_MAX", "LAUNCH_DECELERATE_SPEED", "LAUNCH_DECELERATE_SPEED_START_MIN", "LAUNCH_DECELERATE_SPEED_START_MAX", "LAUNCH_DECELERATE_DEC_RATE", "EMISSION_AREA", "EMISSION_AREA_X", "EMISSION_AREA_Y", "EMISSION_AREA_Z", "EMISSION_SURFACE", "EMISSION_DIRECTION_SURFACE", "AREA_CUBOID", "AREA_SPHEROID", "AREA_CYLINDER", "PIVOT_X", "PIVOT_Y", "GRAVITY", "GRAVITY_STRENGTH", "GRAVITY_MAX_STRENGTH", "COLOUR_TINT", "COLOUR_TINT_START", "COLOUR_TINT_END", "COLOUR_USE_MID", "COLOUR_TINT_MID", "COLOUR_MIDPOINT", "SPREAD_FEATURE", "SPREAD_MIN", "SPREAD", "ROTATION", "ROTATION_MIN", "ROTATION_MAX", "ROTATION_RANDOM_START", "ROTATION_BASE", "ROTATION_VAR", "ROTATION_RAMP", "ROTATION_IN", "ROTATION_OUT", "ROTATION_DAMP", "FADE_NEAR_CAMERA", "FADE_NEAR_CAMERA_MAX_DIST", "FADE_NEAR_CAMERA_THRESHOLD", "TEXTURE_ANIMATION", "TEXTURE_ANIMATION_FRAMES", "NUM_ROWS", "TEXTURE_ANIMATION_LOOP_COUNT", "RANDOM_START_FRAME", "WRAP_FRAMES", "NO_ANIM", "SUB_FRAME_BLEND", "SOFTNESS", "SOFTNESS_EDGE", "SOFTNESS_ALPHA_THICKNESS", "SOFTNESS_ALPHA_DEPTH_MODIFIER", "REVERSE_SOFTNESS", "REVERSE_SOFTNESS_EDGE", "PIVOT_AND_TURBULENCE", "PIVOT_OFFSET_MIN", "PIVOT_OFFSET_MAX", "TURBULENCE_FREQUENCY_MIN", "TURBULENCE_FREQUENCY_MAX", "TURBULENCE_AMOUNT_MIN", "TURBULENCE_AMOUNT_MAX", "ALPHATHRESHOLD", "ALPHATHRESHOLD_TOTALTIME", "ALPHATHRESHOLD_RANGE", "ALPHATHRESHOLD_BEGINSTART", "ALPHATHRESHOLD_BEGINSTOP", "ALPHATHRESHOLD_ENDSTART", "ALPHATHRESHOLD_ENDSTOP", "COLOUR_RAMP", "COLOUR_RAMP_MAP", "COLOUR_RAMP_ALPHA", "DEPTH_FADE_AXIS", "DEPTH_FADE_AXIS_DIST", "DEPTH_FADE_AXIS_PERCENT", "FLOW_UV_ANIMATION", "FLOW_MAP", "FLOW_TEXTURE_MAP", "CYCLE_TIME", "FLOW_SPEED", "FLOW_TEX_SCALE", "FLOW_WARP_STRENGTH", "INFINITE_PROJECTION", "PARALLAX_POSITION", "DISTORTION_OCCLUSION", "AMBIENT_LIGHTING", "AMBIENT_LIGHTING_COLOUR", "NO_CLIP", "resource" });
            node_parameters.Add("PathfindingAlienBackstageNode", new string[] { "started_animating_Entry", "stopped_animating_Entry", "started_animating_Exit", "stopped_animating_Exit", "killtrap_anim_started", "killtrap_anim_stopped", "killtrap_fx_start", "killtrap_fx_stop", "on_loaded", "open_on_reset", "PlayAnimData_Entry", "PlayAnimData_Exit", "Killtrap_alien", "Killtrap_victim", "build_into_navmesh", "position", "top", "extra_cost", "network_id" });
            node_parameters.Add("PathfindingManualNode", new string[] { "character_arriving", "character_stopped", "started_animating", "stopped_animating", "on_loaded", "PlayAnimData", "destination", "build_into_navmesh", "position", "extra_cost", "character_classes" });
            node_parameters.Add("PathfindingTeleportNode", new string[] { "started_teleporting", "stopped_teleporting", "destination", "build_into_navmesh", "position", "extra_cost", "character_classes" });
            node_parameters.Add("PathfindingWaitNode", new string[] { "character_getting_near", "character_arriving", "character_stopped", "started_waiting", "stopped_waiting", "destination", "build_into_navmesh", "position", "extra_cost", "character_classes" });
            node_parameters.Add("Persistent_TriggerRandomSequence", new string[] { "Random_1", "Random_2", "Random_3", "Random_4", "Random_5", "Random_6", "Random_7", "Random_8", "Random_9", "Random_10", "All_triggered", "current", "num" });
            node_parameters.Add("PhysicsApplyBuoyancy", new string[] { "objects", "water_height", "water_density", "water_viscosity", "water_choppiness" });
            node_parameters.Add("PhysicsApplyImpulse", new string[] { "objects", "offset", "direction", "force", "can_damage" });
            node_parameters.Add("PhysicsApplyVelocity", new string[] { "objects", "angular_velocity", "linear_velocity", "propulsion_velocity" });
            node_parameters.Add("PhysicsModifyGravity", new string[] { "float_on_reset", "objects" });
            node_parameters.Add("PhysicsSystem", new string[] { "system_index" });
            node_parameters.Add("PickupSpawner", new string[] { "collect", "spawn_on_reset", "pos", "item_name", "item_quantity" });
            node_parameters.Add("Planet", new string[] { "planet_resource", "parallax_position", "sun_position", "light_shaft_source_position", "parallax_scale", "planet_sort_key", "overbright_scalar", "light_wrap_angle_scalar", "penumbra_falloff_power_scalar", "lens_flare_brightness", "lens_flare_colour", "atmosphere_edge_falloff_power", "atmosphere_edge_transparency", "atmosphere_scroll_speed", "atmosphere_detail_scroll_speed", "override_global_tint", "global_tint", "flow_cycle_time", "flow_speed", "flow_tex_scale", "flow_warp_strength", "detail_uv_scale", "normal_uv_scale", "terrain_uv_scale", "atmosphere_normal_strength", "terrain_normal_strength", "light_shaft_colour", "light_shaft_range", "light_shaft_decay", "light_shaft_min_occlusion_distance", "light_shaft_intensity", "light_shaft_density", "light_shaft_source_occlusion", "blocks_light_shafts" });
            node_parameters.Add("PlatformConstantBool", new string[] { "NextGen", "X360", "PS3" });
            node_parameters.Add("PlatformConstantFloat", new string[] { "NextGen", "X360", "PS3" });
            node_parameters.Add("PlatformConstantInt", new string[] { "NextGen", "X360", "PS3" });
            node_parameters.Add("PlayEnvironmentAnimation", new string[] { "on_finished", "on_finished_streaming", "play_on_reset", "jump_to_the_end_on_play", "geometry", "marker", "external_start_time", "external_time", "animation_length", "animation_info", "AnimationSet", "Animation", "start_frame", "end_frame", "play_speed", "loop", "is_cinematic", "shot_number" });
            node_parameters.Add("Player_ExploitableArea", new string[] { "NpcSafePositions" });
            node_parameters.Add("Player_Sensor", new string[] { "Standard", "Running", "Aiming", "Vent", "Grapple", "Death", "Cover", "Motion_Tracked", "Motion_Tracked_Vent", "Leaning" });
            node_parameters.Add("PlayerCamera", new string[] { });
            node_parameters.Add("PlayerCameraMonitor", new string[] { "AndroidNeckSnap", "AlienKill", "AlienKillBroken", "AlienKillInVent", "StandardAnimDrivenView", "StopNonStandardCameras" });
            node_parameters.Add("PlayerCampaignDeaths", new string[] { });
            node_parameters.Add("PlayerCampaignDeathsInARow", new string[] { });
            node_parameters.Add("PlayerDeathCounter", new string[] { "on_limit", "above_limit", "filter", "count", "Limit" });
            node_parameters.Add("PlayerDiscardsItems", new string[] { "discard_ieds", "discard_medikits", "discard_ammo", "discard_flares_and_lights", "discard_materials", "discard_batteries" });
            node_parameters.Add("PlayerDiscardsTools", new string[] { "discard_motion_tracker", "discard_cutting_torch", "discard_hacking_tool", "discard_keycard" });
            node_parameters.Add("PlayerDiscardsWeapons", new string[] { "discard_pistol", "discard_shotgun", "discard_flamethrower", "discard_boltgun", "discard_cattleprod", "discard_melee" });
            node_parameters.Add("PlayerHasEnoughItems", new string[] { "items", "quantity" });
            node_parameters.Add("PlayerHasItem", new string[] { "items" });
            node_parameters.Add("PlayerHasItemEntity", new string[] { "success", "fail", "items" });
            node_parameters.Add("PlayerHasItemWithName", new string[] { "item_name" });
            node_parameters.Add("PlayerHasSpaceForItem", new string[] { "items" });
            node_parameters.Add("PlayerKilledAllyMonitor", new string[] { "ally_killed", "start_on_reset" });
            node_parameters.Add("PlayerLightProbe", new string[] { "output", "light_level_for_ai", "dark_threshold", "fully_lit_threshold" });
            node_parameters.Add("PlayerTorch", new string[] { "requested_torch_holster", "requested_torch_draw", "start_on_reset", "power_in_current_battery", "battery_count" });
            node_parameters.Add("PlayerTriggerBox", new string[] { "on_entered", "on_exited", "enable_on_reset", "half_dimensions" });
            node_parameters.Add("PlayerUseTriggerBox", new string[] { "on_entered", "on_exited", "on_use", "enable_on_reset", "half_dimensions", "text" });
            node_parameters.Add("PlayerWeaponMonitor", new string[] { "on_clip_above_percentage", "on_clip_below_percentage", "on_clip_empty", "on_clip_full", "weapon_type", "ammo_percentage_in_clip" });
            node_parameters.Add("PointAt", new string[] { "origin", "target", "Result" });
            node_parameters.Add("PointTracker", new string[] { "origin", "target", "target_offset", "result", "origin_offset", "max_speed", "damping_factor" });
            node_parameters.Add("PopupMessage", new string[] { "display", "finished", "header_text", "main_text", "duration", "sound_event", "icon_keyframe" });
            node_parameters.Add("PositionDistance", new string[] { "LHS", "RHS", "Result" });
            node_parameters.Add("PositionMarker", new string[] { });
            node_parameters.Add("PostprocessingSettings", new string[] { "intensity", "priority", "blend_mode" });
            node_parameters.Add("ProjectileMotion", new string[] { "on_think", "on_finished", "start_pos", "start_velocity", "duration", "Current_Position", "Current_Velocity" });
            node_parameters.Add("ProjectileMotionComplex", new string[] { "on_think", "on_finished", "start_position", "start_velocity", "start_angular_velocity", "flight_time_in_seconds", "current_position", "current_velocity", "current_angular_velocity", "current_flight_time_in_seconds" });
            node_parameters.Add("ProjectiveDecal", new string[] { "deleted", "show_on_reset", "time", "include_in_planar_reflections", "material", "resource" });
            node_parameters.Add("ProximityDetector", new string[] { "in_proximity", "filter", "detector_position", "min_distance", "max_distance", "requires_line_of_sight", "proximity_duration" });
            node_parameters.Add("ProximityTrigger", new string[] { "ignited", "electrified", "drenched", "poisoned", "fire_spread_rate", "water_permeate_rate", "electrical_conduction_rate", "gas_diffusion_rate", "ignition_range", "electrical_arc_range", "water_flow_range", "gas_dispersion_range" });
            node_parameters.Add("QueryGCItemPool", new string[] { "count", "item_name", "item_quantity" });
            node_parameters.Add("RadiosityIsland", new string[] { "composites", "exclusions" });
            node_parameters.Add("RadiosityProxy", new string[] { "position", "resource" });
            node_parameters.Add("RandomBool", new string[] { "Result" });
            node_parameters.Add("RandomFloat", new string[] { "Result", "Min", "Max" });
            node_parameters.Add("RandomInt", new string[] { "Result", "Min", "Max" });
            node_parameters.Add("RandomObjectSelector", new string[] { "objects", "chosen_object" });
            node_parameters.Add("RandomSelect", new string[] { "Input", "Result", "Seed" });
            node_parameters.Add("RandomVector", new string[] { "Result", "MinX", "MaxX", "MinY", "MaxY", "MinZ", "MaxZ", "Normalised" });
            node_parameters.Add("Raycast", new string[] { "Obstructed", "Unobstructed", "OutOfRange", "source_position", "target_position", "max_distance", "hit_object", "hit_distance", "hit_position", "priority" });
            node_parameters.Add("Refraction", new string[] { "refraction_resource", "SCALE_X", "SCALE_Z", "DISTANCEFACTOR", "REFRACTFACTOR", "SPEED", "SCALE", "SECONDARY_REFRACTFACTOR", "SECONDARY_SPEED", "SECONDARY_SCALE", "MIN_OCCLUSION_DISTANCE", "CYCLE_TIME", "FLOW_SPEED", "FLOW_TEX_SCALE", "FLOW_WARP_STRENGTH" });
            node_parameters.Add("RegisterCharacterModel", new string[] { "display_model", "reference_skeleton" });
            node_parameters.Add("RemoveFromGCItemPool", new string[] { "on_success", "on_failure", "item_name", "item_quantity", "gcip_instances_to_remove" });
            node_parameters.Add("RemoveFromInventory", new string[] { "success", "fail", "items" });
            node_parameters.Add("RemoveWeaponsFromPlayer", new string[] { });
            node_parameters.Add("RespawnConfig", new string[] { "min_dist", "preferred_dist", "max_dist", "respawn_mode", "respawn_wait_time", "uncollidable_time", "is_default" });
            node_parameters.Add("RespawnExcluder", new string[] { "excluded_points" });
            node_parameters.Add("ReTransformer", new string[] { "new_transform", "result" });
            node_parameters.Add("Rewire", new string[] { "closed", "locations", "access_points", "map_keyframe", "total_power" });
            node_parameters.Add("RewireAccess_Point", new string[] { "closed", "ui_breakout_triggered", "interactive_locations", "visible_locations", "additional_power", "display_name", "map_element_name", "map_name", "map_x_offset", "map_y_offset", "map_zoom" });
            node_parameters.Add("RewireLocation", new string[] { "power_draw_increased", "power_draw_reduced", "systems", "element_name", "display_name" });
            node_parameters.Add("RewireSystem", new string[] { "on", "off", "world_pos", "display_name", "display_name_enum", "on_by_default", "running_cost", "system_type", "map_name", "element_name" });
            node_parameters.Add("RewireTotalPowerResource", new string[] { "total_power" });
            node_parameters.Add("RibbonEmitterReference", new string[] { "deleted", "start_on_reset", "show_on_reset", "mastered_by_visibility", "use_local_rotation", "include_in_planar_reflections", "material", "unique_material", "quality_level", "BLENDING_STANDARD", "BLENDING_ALPHA_REF", "BLENDING_ADDITIVE", "BLENDING_PREMULTIPLIED", "BLENDING_DISTORTION", "NO_MIPS", "UV_SQUARED", "LOW_RES", "LIGHTING", "MASK_AMOUNT_MIN", "MASK_AMOUNT_MAX", "MASK_AMOUNT_MIDPOINT", "DRAW_PASS", "SYSTEM_EXPIRY_TIME", "LIFETIME", "SMOOTHED", "WORLD_TO_LOCAL_BLEND_START", "WORLD_TO_LOCAL_BLEND_END", "WORLD_TO_LOCAL_MAX_DIST", "TEXTURE", "TEXTURE_MAP", "UV_REPEAT", "UV_SCROLLSPEED", "MULTI_TEXTURE", "U2_SCALE", "V2_REPEAT", "V2_SCROLLSPEED", "MULTI_TEXTURE_BLEND", "MULTI_TEXTURE_ADD", "MULTI_TEXTURE_MULT", "MULTI_TEXTURE_MAX", "MULTI_TEXTURE_MIN", "SECOND_TEXTURE", "TEXTURE_MAP2", "CONTINUOUS", "BASE_LOCKED", "SPAWN_RATE", "TRAILING", "INSTANT", "RATE", "TRAIL_SPAWN_RATE", "TRAIL_DELAY", "MAX_TRAILS", "POINT_TO_POINT", "TARGET_POINT_POSITION", "DENSITY", "ABS_FADE_IN_0", "ABS_FADE_IN_1", "FORCES", "GRAVITY_STRENGTH", "GRAVITY_MAX_STRENGTH", "DRAG_STRENGTH", "WIND_X", "WIND_Y", "WIND_Z", "START_MID_END_SPEED", "SPEED_START_MIN", "SPEED_START_MAX", "WIDTH", "WIDTH_START", "WIDTH_MID", "WIDTH_END", "WIDTH_IN", "WIDTH_OUT", "COLOUR_TINT", "COLOUR_SCALE_START", "COLOUR_SCALE_MID", "COLOUR_SCALE_END", "COLOUR_TINT_START", "COLOUR_TINT_MID", "COLOUR_TINT_END", "ALPHA_FADE", "FADE_IN", "FADE_OUT", "EDGE_FADE", "ALPHA_ERODE", "SIDE_ON_FADE", "SIDE_FADE_START", "SIDE_FADE_END", "DISTANCE_SCALING", "DIST_SCALE", "SPREAD_FEATURE", "SPREAD_MIN", "SPREAD", "EMISSION_AREA", "EMISSION_AREA_X", "EMISSION_AREA_Y", "EMISSION_AREA_Z", "AREA_CUBOID", "AREA_SPHEROID", "AREA_CYLINDER", "COLOUR_RAMP", "COLOUR_RAMP_MAP", "SOFTNESS", "SOFTNESS_EDGE", "SOFTNESS_ALPHA_THICKNESS", "SOFTNESS_ALPHA_DEPTH_MODIFIER", "AMBIENT_LIGHTING", "AMBIENT_LIGHTING_COLOUR", "NO_CLIP", "resource" });
            node_parameters.Add("RotateAtSpeed", new string[] { "on_finished", "on_think", "start_pos", "origin", "timer", "Result", "duration", "speed_X", "speed_Y", "speed_Z", "loop" });
            node_parameters.Add("RotateInTime", new string[] { "on_finished", "on_think", "start_pos", "origin", "timer", "Result", "duration", "time_X", "time_Y", "time_Z", "loop" });
            node_parameters.Add("RTT_MoviePlayer", new string[] { "start", "end", "show_on_reset", "filename", "layer_name", "target_texture_name" });
            node_parameters.Add("SaveGlobalProgression", new string[] { });
            node_parameters.Add("SaveManagers", new string[] { });
            node_parameters.Add("ScalarProduct", new string[] { "LHS", "RHS", "Result" });
            node_parameters.Add("ScreenEffectEventMonitor", new string[] { "MeleeHit", "BulletHit", "MedkitHeal", "StartStrangle", "StopStrangle", "StartLowHealth", "StopLowHealth", "StartDeath", "StopDeath", "AcidHit", "FlashbangHit", "HitAndRun", "CancelHitAndRun" });
            node_parameters.Add("ScreenFadeIn", new string[] { "fade_value" });
            node_parameters.Add("ScreenFadeInTimed", new string[] { "on_finished", "time" });
            node_parameters.Add("ScreenFadeOutToBlack", new string[] { "fade_value" });
            node_parameters.Add("ScreenFadeOutToBlackTimed", new string[] { "on_finished", "time" });
            node_parameters.Add("ScreenFadeOutToWhite", new string[] { "fade_value" });
            node_parameters.Add("ScreenFadeOutToWhiteTimed", new string[] { "on_finished", "time" });
            node_parameters.Add("SetAsActiveMissionLevel", new string[] { "clear_level" });
            node_parameters.Add("SetBlueprintInfo", new string[] { "type", "level", "available" });
            node_parameters.Add("SetBool", new string[] { });
            node_parameters.Add("SetColour", new string[] { "Colour", "Result" });
            node_parameters.Add("SetEnum", new string[] { "Output", "initial_value" });
            node_parameters.Add("SetFloat", new string[] { });
            node_parameters.Add("SetGamepadAxes", new string[] { "invert_x", "invert_y", "save_settings" });
            node_parameters.Add("SetGameplayTips", new string[] { "tip_string_id" });
            node_parameters.Add("SetGatingToolLevel", new string[] { "level", "tool_type" });
            node_parameters.Add("SetHackingToolLevel", new string[] { "level" });
            node_parameters.Add("SetInteger", new string[] { });
            node_parameters.Add("SetLocationAndOrientation", new string[] { "location", "axis", "local_offset", "result", "axis_is" });
            node_parameters.Add("SetMotionTrackerRange", new string[] { "range" });
            node_parameters.Add("SetNextLoadingMovie", new string[] { "playlist_to_load" });
            node_parameters.Add("SetObject", new string[] { "Input", "Output" });
            node_parameters.Add("SetObjectiveCompleted", new string[] { "objective_id" });
            node_parameters.Add("SetPlayerHasGatingTool", new string[] { "tool_type" });
            node_parameters.Add("SetPlayerHasKeycard", new string[] { "card_uid" });
            node_parameters.Add("SetPosition", new string[] { "Translation", "Rotation", "Input", "Result", "set_on_reset" });
            node_parameters.Add("SetPrimaryObjective", new string[] { "title", "additional_info", "title_list", "additional_info_list", "show_message" });
            node_parameters.Add("SetRichPresence", new string[] { "presence_id", "mission_number" });
            node_parameters.Add("SetString", new string[] { "Output", "initial_value", "SetEnumString" });
            node_parameters.Add("SetSubObjective", new string[] { "target_position", "title", "map_description", "title_list", "map_description_list", "slot_number", "objective_type", "show_message" });
            node_parameters.Add("SetupGCDistribution", new string[] { "c00", "c01", "c02", "c03", "c04", "c05", "c06", "c07", "c08", "c09", "c10", "minimum_multiplier", "divisor", "lookup_decrease_time", "lookup_point_increase" });
            node_parameters.Add("SetVector", new string[] { "x", "y", "z", "Result" });
            node_parameters.Add("SetVector2", new string[] { "Input", "Result" });
            node_parameters.Add("SharpnessSettings", new string[] { "local_contrast_factor" });
            node_parameters.Add("Showlevel_Completed", new string[] { });
            node_parameters.Add("SimpleRefraction", new string[] { "deleted", "show_on_reset", "DISTANCEFACTOR", "NORMAL_MAP", "SPEED", "SCALE", "REFRACTFACTOR", "SECONDARY_NORMAL_MAPPING", "SECONDARY_NORMAL_MAP", "SECONDARY_SPEED", "SECONDARY_SCALE", "SECONDARY_REFRACTFACTOR", "ALPHA_MASKING", "ALPHA_MASK", "DISTORTION_OCCLUSION", "MIN_OCCLUSION_DISTANCE", "FLOW_UV_ANIMATION", "FLOW_MAP", "CYCLE_TIME", "FLOW_SPEED", "FLOW_TEX_SCALE", "FLOW_WARP_STRENGTH", "resource" });
            node_parameters.Add("SimpleWater", new string[] { "deleted", "show_on_reset", "SHININESS", "softness_edge", "FRESNEL_POWER", "MIN_FRESNEL", "MAX_FRESNEL", "LOW_RES_ALPHA_PASS", "ATMOSPHERIC_FOGGING", "NORMAL_MAP", "SPEED", "SCALE", "NORMAL_MAP_STRENGTH", "SECONDARY_NORMAL_MAPPING", "SECONDARY_SPEED", "SECONDARY_SCALE", "SECONDARY_NORMAL_MAP_STRENGTH", "ALPHA_MASKING", "ALPHA_MASK", "FLOW_MAPPING", "FLOW_MAP", "CYCLE_TIME", "FLOW_SPEED", "FLOW_TEX_SCALE", "FLOW_WARP_STRENGTH", "ENVIRONMENT_MAPPING", "ENVIRONMENT_MAP", "ENVIRONMENT_MAP_MULT", "LOCALISED_ENVIRONMENT_MAPPING", "ENVMAP_SIZE", "LOCALISED_ENVMAP_BOX_PROJECTION", "ENVMAP_BOXPROJ_BB_SCALE", "REFLECTIVE_MAPPING", "REFLECTION_PERTURBATION_STRENGTH", "DEPTH_FOG_INITIAL_COLOUR", "DEPTH_FOG_INITIAL_ALPHA", "DEPTH_FOG_MIDPOINT_COLOUR", "DEPTH_FOG_MIDPOINT_ALPHA", "DEPTH_FOG_MIDPOINT_DEPTH", "DEPTH_FOG_END_COLOUR", "DEPTH_FOG_END_ALPHA", "DEPTH_FOG_END_DEPTH", "CAUSTIC_TEXTURE", "CAUSTIC_TEXTURE_SCALE", "CAUSTIC_REFRACTIONS", "CAUSTIC_REFLECTIONS", "CAUSTIC_SPEED_SCALAR", "CAUSTIC_INTENSITY", "CAUSTIC_SURFACE_WRAP", "CAUSTIC_HEIGHT", "resource", "CAUSTIC_TEXTURE_INDEX" });
            node_parameters.Add("SmokeCylinder", new string[] { "pos", "radius", "height", "duration" });
            node_parameters.Add("SmokeCylinderAttachmentInterface", new string[] { "radius", "height", "duration" });
            node_parameters.Add("SmoothMove", new string[] { "on_finished", "timer", "start_position", "end_position", "start_velocity", "end_velocity", "result", "duration" });
            node_parameters.Add("Sound", new string[] { "stop_event", "is_static_ambience", "start_on", "multi_trigger", "use_multi_emitter", "create_sound_object", "position", "switch_name", "switch_value", "last_gen_enabled", "resume_after_suspended" });
            node_parameters.Add("SoundBarrier", new string[] { "default_open", "position", "half_dimensions", "band_aid", "override_value", "resource" });
            node_parameters.Add("SoundEnvironmentMarker", new string[] { "reverb_name", "on_enter_event", "on_exit_event", "linked_network_occlusion_scaler", "room_size", "disable_network_creation", "position" });
            node_parameters.Add("SoundEnvironmentZone", new string[] { "reverb_name", "priority", "position", "half_dimensions" });
            node_parameters.Add("SoundImpact", new string[] { "sound_event", "is_occludable", "argument_1", "argument_2", "argument_3" });
            node_parameters.Add("SoundLevelInitialiser", new string[] { "auto_generate_networks", "network_node_min_spacing", "network_node_max_visibility", "network_node_ceiling_height" });
            node_parameters.Add("SoundLoadBank", new string[] { "bank_loaded", "sound_bank", "trigger_via_pin", "memory_pool" });
            node_parameters.Add("SoundLoadSlot", new string[] { "bank_loaded", "sound_bank", "memory_pool" });
            node_parameters.Add("SoundMissionInitialiser", new string[] { "human_max_threat", "android_max_threat", "alien_max_threat" });
            node_parameters.Add("SoundNetworkNode", new string[] { "position" });
            node_parameters.Add("SoundObject", new string[] { });
            node_parameters.Add("SoundPhysicsInitialiser", new string[] { "contact_max_timeout", "contact_smoothing_attack_rate", "contact_smoothing_decay_rate", "contact_min_magnitude", "contact_max_trigger_distance", "impact_min_speed", "impact_max_trigger_distance", "ragdoll_min_timeout", "ragdoll_min_speed" });
            node_parameters.Add("SoundPlaybackBaseClass", new string[] { "on_finished", "attached_sound_object", "sound_event", "is_occludable", "argument_1", "argument_2", "argument_3", "argument_4", "argument_5", "namespace", "object_position", "restore_on_checkpoint" });
            node_parameters.Add("SoundPlayerFootwearOverride", new string[] { "footwear_sound" });
            node_parameters.Add("SoundRTPCController", new string[] { "stealth_default_on", "threat_default_on" });
            node_parameters.Add("SoundSetRTPC", new string[] { "rtpc_value", "sound_object", "rtpc_name", "smooth_rate", "start_on" });
            node_parameters.Add("SoundSetState", new string[] { "state_name", "state_value" });
            node_parameters.Add("SoundSetSwitch", new string[] { "sound_object", "switch_name", "switch_value" });
            node_parameters.Add("SoundSpline", new string[] { });
            node_parameters.Add("SoundTimelineTrigger", new string[] { "sound_event", "trigger_time" });
            node_parameters.Add("SpaceSuitVisor", new string[] { "breath_level" });
            node_parameters.Add("SpaceTransform", new string[] { "affected_geometry", "yaw_speed", "pitch_speed", "roll_speed" });
            node_parameters.Add("SpawnGroup", new string[] { "on_spawn_request", "default_group", "trigger_on_reset" });
            node_parameters.Add("Speech", new string[] { "on_speech_started", "character", "alt_character", "speech_priority", "queue_time" });
            node_parameters.Add("SpeechScript", new string[] { "on_script_ended", "character_01", "character_02", "character_03", "character_04", "character_05", "alt_character_01", "alt_character_02", "alt_character_03", "alt_character_04", "alt_character_05", "speech_priority", "is_occludable", "line_01_event", "line_01_character", "line_02_delay", "line_02_event", "line_02_character", "line_03_delay", "line_03_event", "line_03_character", "line_04_delay", "line_04_event", "line_04_character", "line_05_delay", "line_05_event", "line_05_character", "line_06_delay", "line_06_event", "line_06_character", "line_07_delay", "line_07_event", "line_07_character", "line_08_delay", "line_08_event", "line_08_character", "line_09_delay", "line_09_event", "line_09_character", "line_10_delay", "line_10_event", "line_10_character", "restore_on_checkpoint" });
            node_parameters.Add("Sphere", new string[] { "event", "enable_on_reset", "radius", "include_physics" });
            node_parameters.Add("SplineDistanceLerp", new string[] { "on_think", "spline", "lerp_position", "Result" });
            node_parameters.Add("SplinePath", new string[] { "loop", "orientated", "points" });
            node_parameters.Add("SpottingExclusionArea", new string[] { "position", "half_dimensions" });
            node_parameters.Add("Squad_SetMaxEscalationLevel", new string[] { "max_level", "squad_coordinator" });
            node_parameters.Add("StartNewChapter", new string[] { "chapter" });
            node_parameters.Add("StateQuery", new string[] { "on_true", "on_false", "Input", "Result" });
            node_parameters.Add("StealCamera", new string[] { "on_converged", "focus_position", "steal_type", "check_line_of_sight", "blend_in_duration" });
            node_parameters.Add("StreamingMonitor", new string[] { "on_loaded" });
            node_parameters.Add("SurfaceEffectBox", new string[] { "deleted", "show_on_reset", "COLOUR_TINT", "COLOUR_TINT_OUTER", "INTENSITY", "OPACITY", "FADE_OUT_TIME", "SURFACE_WRAP", "ROUGHNESS_SCALE", "SPARKLE_SCALE", "METAL_STYLE_REFLECTIONS", "SHININESS_OPACITY", "TILING_ZY", "TILING_ZX", "TILING_XY", "FALLOFF", "WS_LOCKED", "TEXTURE_MAP", "SPARKLE_MAP", "ENVMAP", "ENVIRONMENT_MAP", "ENVMAP_PERCENT_EMISSIVE", "SPHERE", "BOX", "resource" });
            node_parameters.Add("SurfaceEffectSphere", new string[] { "deleted", "show_on_reset", "COLOUR_TINT", "COLOUR_TINT_OUTER", "INTENSITY", "OPACITY", "FADE_OUT_TIME", "SURFACE_WRAP", "ROUGHNESS_SCALE", "SPARKLE_SCALE", "METAL_STYLE_REFLECTIONS", "SHININESS_OPACITY", "TILING_ZY", "TILING_ZX", "TILING_XY", "WS_LOCKED", "TEXTURE_MAP", "SPARKLE_MAP", "ENVMAP", "ENVIRONMENT_MAP", "ENVMAP_PERCENT_EMISSIVE", "SPHERE", "resource" });
            node_parameters.Add("SwitchLevel", new string[] { "level_name" });
            node_parameters.Add("SyncOnAllPlayers", new string[] { "on_synchronized", "on_synchronized_host" });
            node_parameters.Add("SyncOnFirstPlayer", new string[] { "on_synchronized", "on_synchronized_host", "on_synchronized_local" });
            node_parameters.Add("Task", new string[] { "start_command", "selected_by_npc", "clean_up", "start_on_reset", "Job", "TaskPosition", "filter", "should_stop_moving_when_reached", "should_orientate_when_reached", "reached_distance_threshold", "selection_priority", "timeout", "always_on_tracker" });
            node_parameters.Add("TerminalContent", new string[] { "selected", "content_title", "content_decoration_title", "additional_info", "is_connected_to_audio_log", "is_triggerable", "is_single_use" });
            node_parameters.Add("TerminalFolder", new string[] { "code_success", "code_fail", "selected", "lock_on_reset", "content0", "content1", "code", "folder_title", "folder_lock_type" });
            node_parameters.Add("Thinker", new string[] { "on_think", "delay_between_triggers", "is_continuous", "use_random_start", "random_start_delay", "total_thinking_time" });
            node_parameters.Add("ThinkOnce", new string[] { "on_think", "start_on_reset", "use_random_start", "random_start_delay" });
            node_parameters.Add("ThrowingPointOfImpact", new string[] { "show_point_of_impact", "hide_point_of_impact", "Location", "Visible" });
            node_parameters.Add("ToggleFunctionality", new string[] { });
            node_parameters.Add("TogglePlayerTorch", new string[] { });
            node_parameters.Add("TorchDynamicMovement", new string[] { "start_on_reset", "torch", "max_spatial_velocity", "max_angular_velocity", "max_position_displacement", "max_target_displacement", "position_damping", "target_damping" });
            node_parameters.Add("TRAV_1ShotClimbUnder", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "character_classes" });
            node_parameters.Add("TRAV_1ShotFloorVentEntrance", new string[] { "OnEnter", "Completed", "enable_on_reset", "LinePath", "character_classes", "resource" });
            node_parameters.Add("TRAV_1ShotFloorVentExit", new string[] { "OnExit", "Completed", "enable_on_reset", "LinePath", "character_classes", "resource" });
            node_parameters.Add("TRAV_1ShotLeap", new string[] { "OnEnter", "OnExit", "OnSuccess", "OnFailure", "enable_on_reset", "StartEdgeLinePath", "EndEdgeLinePath", "InUse", "MissDistance", "NearMissDistance", "character_classes" });
            node_parameters.Add("TRAV_1ShotSpline", new string[] { "OnEnter", "OnExit", "enable_on_reset", "open_on_reset", "EntrancePath", "ExitPath", "MinimumPath", "MaximumPath", "MinimumSupport", "MaximumSupport", "template", "headroom", "extra_cost", "fit_end_to_edge", "min_speed", "max_speed", "animationTree", "character_classes", "resource" });
            node_parameters.Add("TRAV_1ShotVentEntrance", new string[] { "OnEnter", "Completed", "enable_on_reset", "LinePath", "character_classes", "resource" });
            node_parameters.Add("TRAV_1ShotVentExit", new string[] { "OnExit", "Completed", "enable_on_reset", "LinePath", "character_classes", "resource" });
            node_parameters.Add("TRAV_ContinuousBalanceBeam", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "character_classes" });
            node_parameters.Add("TRAV_ContinuousCinematicSidle", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "character_classes" });
            node_parameters.Add("TRAV_ContinuousClimbingWall", new string[] { "OnEnter", "OnExit", "LinePath", "InUse", "Dangling", "character_classes" });
            node_parameters.Add("TRAV_ContinuousLadder", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "RungSpacing", "character_classes" });
            node_parameters.Add("TRAV_ContinuousLedge", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "Dangling", "Sidling", "character_classes" });
            node_parameters.Add("TRAV_ContinuousPipe", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "character_classes" });
            node_parameters.Add("TRAV_ContinuousTightGap", new string[] { "OnEnter", "OnExit", "enable_on_reset", "LinePath", "InUse", "character_classes" });
            node_parameters.Add("Trigger_AudioOccluded", new string[] { "NotOccluded", "Occluded", "position", "Range" });
            node_parameters.Add("TriggerBindAllCharactersOfType", new string[] { "bound_trigger", "character_class" });
            node_parameters.Add("TriggerBindAllNPCs", new string[] { "npc_inside", "npc_outside", "filter", "centre", "radius" });
            node_parameters.Add("TriggerBindCharacter", new string[] { "bound_trigger", "characters" });
            node_parameters.Add("TriggerBindCharactersInSquad", new string[] { "bound_trigger" });
            node_parameters.Add("TriggerCameraViewCone", new string[] { "enter", "exit", "target", "fov", "aspect_ratio", "intersect_with_geometry", "use_camera_fov", "target_offset", "visible_area_type", "visible_area_horizontal", "visible_area_vertical", "raycast_grace" });
            node_parameters.Add("TriggerCameraViewConeMulti", new string[] { "enter", "exit", "enter1", "exit1", "enter2", "exit2", "enter3", "exit3", "enter4", "exit4", "enter5", "exit5", "enter6", "exit6", "enter7", "exit7", "enter8", "exit8", "enter9", "exit9", "target", "target1", "target2", "target3", "target4", "target5", "target6", "target7", "target8", "target9", "fov", "aspect_ratio", "intersect_with_geometry", "number_of_inputs", "use_camera_fov", "visible_area_type", "visible_area_horizontal", "visible_area_vertical", "raycast_grace" });
            node_parameters.Add("TriggerCameraVolume", new string[] { "inside", "enter", "exit", "inside_factor", "lookat_factor", "lookat_X_position", "lookat_Y_position", "start_radius", "radius" });
            node_parameters.Add("TriggerCheckDifficulty", new string[] { "on_success", "on_failure", "DifficultyLevel" });
            node_parameters.Add("TriggerContainerObjectsFilterCounter", new string[] { "none_passed", "some_passed", "all_passed", "filter", "container" });
            node_parameters.Add("TriggerDamaged", new string[] { "on_damaged", "enable_on_reset", "physics_object", "impact_normal", "threshold" });
            node_parameters.Add("TriggerDelay", new string[] { "delayed_trigger", "purged_trigger", "time_left", "Hrs", "Min", "Sec" });
            node_parameters.Add("TriggerExtractBoundCharacter", new string[] { "unbound_trigger", "bound_character" });
            node_parameters.Add("TriggerExtractBoundObject", new string[] { "unbound_trigger", "bound_object" });
            node_parameters.Add("TriggerFilter", new string[] { "on_success", "on_failure", "filter" });
            node_parameters.Add("TriggerLooper", new string[] { "target", "count", "delay" });
            node_parameters.Add("TriggerObjectsFilter", new string[] { "on_success", "on_failure", "filter", "objects" });
            node_parameters.Add("TriggerObjectsFilterCounter", new string[] { "none_passed", "some_passed", "all_passed", "objects", "filter" });
            node_parameters.Add("TriggerRandom", new string[] { "Random_1", "Random_2", "Random_3", "Random_4", "Random_5", "Random_6", "Random_7", "Random_8", "Random_9", "Random_10", "Random_11", "Random_12", "Num" });
            node_parameters.Add("TriggerRandomSequence", new string[] { "Random_1", "Random_2", "Random_3", "Random_4", "Random_5", "Random_6", "Random_7", "Random_8", "Random_9", "Random_10", "All_triggered", "current", "num" });
            node_parameters.Add("TriggerSelect", new string[] { "Pin_0", "Pin_1", "Pin_2", "Pin_3", "Pin_4", "Pin_5", "Pin_6", "Pin_7", "Pin_8", "Pin_9", "Pin_10", "Pin_11", "Pin_12", "Pin_13", "Pin_14", "Pin_15", "Pin_16", "Object_0", "Object_1", "Object_2", "Object_3", "Object_4", "Object_5", "Object_6", "Object_7", "Object_8", "Object_9", "Object_10", "Object_11", "Object_12", "Object_13", "Object_14", "Object_15", "Object_16", "Result", "index" });
            node_parameters.Add("TriggerSelect_Direct", new string[] { "Changed_to_0", "Changed_to_1", "Changed_to_2", "Changed_to_3", "Changed_to_4", "Changed_to_5", "Changed_to_6", "Changed_to_7", "Changed_to_8", "Changed_to_9", "Changed_to_10", "Changed_to_11", "Changed_to_12", "Changed_to_13", "Changed_to_14", "Changed_to_15", "Changed_to_16", "Object_0", "Object_1", "Object_2", "Object_3", "Object_4", "Object_5", "Object_6", "Object_7", "Object_8", "Object_9", "Object_10", "Object_11", "Object_12", "Object_13", "Object_14", "Object_15", "Object_16", "Result", "TriggeredIndex", "Changes_only" });
            node_parameters.Add("TriggerSequence", new string[] { "proxy_enable_on_reset", "attach_on_reset", "duration", "trigger_mode", "random_seed", "use_random_intervals", "no_duplicates", "interval_multiplier" });
            node_parameters.Add("TriggerSimple", new string[] { });
            node_parameters.Add("TriggerSwitch", new string[] { "Pin_1", "Pin_2", "Pin_3", "Pin_4", "Pin_5", "Pin_6", "Pin_7", "Pin_8", "Pin_9", "Pin_10", "current", "num", "loop" });
            node_parameters.Add("TriggerSync", new string[] { "Pin1_Synced", "Pin2_Synced", "Pin3_Synced", "Pin4_Synced", "Pin5_Synced", "Pin6_Synced", "Pin7_Synced", "Pin8_Synced", "Pin9_Synced", "Pin10_Synced", "reset_on_trigger" });
            node_parameters.Add("TriggerTouch", new string[] { "touch_event", "enable_on_reset", "physics_object", "impact_normal" });
            node_parameters.Add("TriggerUnbindCharacter", new string[] { "unbound_trigger" });
            node_parameters.Add("TriggerViewCone", new string[] { "enter", "exit", "target_is_visible", "no_target_visible", "target", "fov", "max_distance", "aspect_ratio", "source_position", "filter", "intersect_with_geometry", "visible_target", "target_offset", "visible_area_type", "visible_area_horizontal", "visible_area_vertical", "raycast_grace" });
            node_parameters.Add("TriggerVolumeFilter", new string[] { "on_event_entered", "on_event_exited", "filter" });
            node_parameters.Add("TriggerVolumeFilter_Monitored", new string[] { "on_event_entered", "on_event_exited", "filter" });
            node_parameters.Add("TriggerWeightedRandom", new string[] { "Random_1", "Random_2", "Random_3", "Random_4", "Random_5", "Random_6", "Random_7", "Random_8", "Random_9", "Random_10", "current", "Weighting_01", "Weighting_02", "Weighting_03", "Weighting_04", "Weighting_05", "Weighting_06", "Weighting_07", "Weighting_08", "Weighting_09", "Weighting_10", "allow_same_pin_in_succession" });
            node_parameters.Add("TriggerWhenSeeTarget", new string[] { "seen", "Target" });
            node_parameters.Add("TutorialMessage", new string[] { "text", "text_list", "show_animation" });
            node_parameters.Add("UI_Attached", new string[] { "closed", "ui_icon" });
            node_parameters.Add("UI_Container", new string[] { "take_slot", "emptied", "contents", "has_been_used", "is_persistent", "is_temporary" });
            node_parameters.Add("UI_Icon", new string[] { "start", "start_fail", "button_released", "broadcasted_start", "highlight", "unhighlight", "lock_looked_at", "lock_interaction", "lock_on_reset", "enable_on_reset", "show_on_reset", "geometry", "highlight_geometry", "target_pickup_item", "highlight_distance_threshold", "interaction_distance_threshold", "icon_user", "unlocked_text", "locked_text", "action_text", "icon_keyframe", "can_be_used", "category", "push_hold_time" });
            node_parameters.Add("UI_KeyGate", new string[] { "keycard_success", "keycode_success", "keycard_fail", "keycode_fail", "keycard_cancelled", "keycode_cancelled", "ui_breakout_triggered", "lock_on_reset", "light_on_reset", "code", "carduid", "key_type" });
            node_parameters.Add("UI_Keypad", new string[] { "success", "fail", "code", "exit_on_fail" });
            node_parameters.Add("UI_ReactionGame", new string[] { "success", "fail", "stage0_success", "stage0_fail", "stage1_success", "stage1_fail", "stage2_success", "stage2_fail", "ui_breakout_triggered", "resources_finished_unloading", "resources_finished_loading", "completion_percentage", "exit_on_fail" });
            node_parameters.Add("UIBreathingGameIcon", new string[] { "fill_percentage", "prompt_text" });
            node_parameters.Add("UiSelectionBox", new string[] { "is_priority" });
            node_parameters.Add("UiSelectionSphere", new string[] { "is_priority" });
            node_parameters.Add("UnlockAchievement", new string[] { "achievement_id" });
            node_parameters.Add("UnlockLogEntry", new string[] { "entry" });
            node_parameters.Add("UnlockMapDetail", new string[] { "map_keyframe", "details" });
            node_parameters.Add("UpdateGlobalPosition", new string[] { "PositionName" });
            node_parameters.Add("UpdateLeaderBoardDisplay", new string[] { "time" });
            node_parameters.Add("UpdatePrimaryObjective", new string[] { "show_message", "clear_objective" });
            node_parameters.Add("UpdateSubObjective", new string[] { "slot_number", "show_message", "clear_objective" });
            node_parameters.Add("VariableAnimationInfo", new string[] { "AnimationSet", "Animation" });
            node_parameters.Add("VariableBool", new string[] { "initial_value", "is_persistent" });
            node_parameters.Add("VariableColour", new string[] { "initial_colour" });
            node_parameters.Add("VariableEnum", new string[] { "initial_value", "is_persistent", "VariableEnumString" });
            node_parameters.Add("VariableFilterObject", new string[] { });
            node_parameters.Add("VariableFlashScreenColour", new string[] { "start_on_reset", "pause_on_reset", "initial_colour", "flash_layer_name" });
            node_parameters.Add("VariableFloat", new string[] { "initial_value", "is_persistent" });
            node_parameters.Add("VariableHackingConfig", new string[] { "nodes", "sensors", "victory_nodes", "victory_sensors" });
            node_parameters.Add("VariableInt", new string[] { "initial_value", "is_persistent" });
            node_parameters.Add("VariableObject", new string[] { "initial" });
            node_parameters.Add("VariablePosition", new string[] { });
            node_parameters.Add("VariableString", new string[] { "initial_value", "is_persistent" });
            node_parameters.Add("VariableThePlayer", new string[] { });
            node_parameters.Add("VariableTriggerObject", new string[] { });
            node_parameters.Add("VariableVector", new string[] { "initial_x", "initial_y", "initial_z" });
            node_parameters.Add("VariableVector2", new string[] { "initial_value" });
            node_parameters.Add("VectorAdd", new string[] { });
            node_parameters.Add("VectorDirection", new string[] { "From", "To", "Result" });
            node_parameters.Add("VectorDistance", new string[] { "LHS", "RHS", "Result" });
            node_parameters.Add("VectorLinearInterpolateSpeed", new string[] { "on_finished", "on_think", "Initial_Value", "Target_Value", "Reverse", "Result", "Speed", "PingPong", "Loop" });
            node_parameters.Add("VectorLinearInterpolateTimed", new string[] { "on_finished", "on_think", "Initial_Value", "Target_Value", "Reverse", "Result", "Time", "PingPong", "Loop" });
            node_parameters.Add("VectorLinearProportion", new string[] { "Initial_Value", "Target_Value", "Proportion", "Result" });
            node_parameters.Add("VectorMath", new string[] { "LHS", "RHS", "Result" });
            node_parameters.Add("VectorModulus", new string[] { "Input", "Result" });
            node_parameters.Add("VectorMultiply", new string[] { });
            node_parameters.Add("VectorMultiplyByPos", new string[] { "Vector", "WorldPos", "Result" });
            node_parameters.Add("VectorNormalise", new string[] { "Input", "Result" });
            node_parameters.Add("VectorProduct", new string[] { });
            node_parameters.Add("VectorReflect", new string[] { "Input", "Normal", "Result" });
            node_parameters.Add("VectorRotateByPos", new string[] { "Vector", "WorldPos", "Result" });
            node_parameters.Add("VectorRotatePitch", new string[] { "Vector", "Pitch", "Result" });
            node_parameters.Add("VectorRotateRoll", new string[] { "Vector", "Roll", "Result" });
            node_parameters.Add("VectorRotateYaw", new string[] { "Vector", "Yaw", "Result" });
            node_parameters.Add("VectorScale", new string[] { "LHS", "RHS", "Result" });
            node_parameters.Add("VectorSubtract", new string[] { });
            node_parameters.Add("VectorYaw", new string[] { "Vector", "Result" });
            node_parameters.Add("VideoCapture", new string[] { "clip_name", "only_in_capture_mode" });
            node_parameters.Add("VignetteSettings", new string[] { "vignette_factor", "vignette_chromatic_aberration_scale" });
            node_parameters.Add("VisibilityMaster", new string[] { "renderable", "mastered_by_visibility" });
            node_parameters.Add("Weapon_AINotifier", new string[] { });
            node_parameters.Add("WEAPON_AmmoTypeFilter", new string[] { "passed", "failed", "AmmoType" });
            node_parameters.Add("WEAPON_AttackerFilter", new string[] { "passed", "failed", "filter" });
            node_parameters.Add("WEAPON_DamageFilter", new string[] { "passed", "failed", "damage_threshold", "WEAPON_DidHitSomethingFilter", "passed", "failed" });
            node_parameters.Add("WEAPON_Effect", new string[] { "WorldPos", "AttachedEffects", "UnattachedEffects", "LifeTime" });
            node_parameters.Add("WEAPON_GiveToCharacter", new string[] { "Character", "Weapon", "is_holstered" });
            node_parameters.Add("WEAPON_GiveToPlayer", new string[] { "weapon", "holster", "starting_ammo" });
            node_parameters.Add("WEAPON_ImpactAngleFilter", new string[] { "greater", "less", "ReferenceAngle" });
            node_parameters.Add("WEAPON_ImpactCharacterFilter", new string[] { "passed", "failed", "character_classes", "character_body_location" });
            node_parameters.Add("WEAPON_ImpactEffect", new string[] { "StaticEffects", "DynamicEffects", "DynamicAttachedEffects", "Type", "Orientation", "Priority", "SafeDistant", "LifeTime", "character_damage_offset", "RandomRotation" });
            node_parameters.Add("WEAPON_ImpactFilter", new string[] { "passed", "failed", "PhysicMaterial" });
            node_parameters.Add("WEAPON_ImpactInspector", new string[] { "damage", "impact_position", "impact_target" });
            node_parameters.Add("WEAPON_ImpactOrientationFilter", new string[] { "passed", "failed", "ThresholdAngle", "Orientation" });
            node_parameters.Add("WEAPON_MultiFilter", new string[] { "passed", "failed", "AttackerFilter", "TargetFilter", "DamageThreshold", "DamageType", "UseAmmoFilter", "AmmoType" });
            node_parameters.Add("WEAPON_TargetObjectFilter", new string[] { "passed", "failed", "filter" });
            node_parameters.Add("Zone", new string[] { "composites", "suspend_on_unload", "space_visible" });
            node_parameters.Add("ZoneExclusionLink", new string[] { "ZoneA", "ZoneB", "exclude_streaming" });
            node_parameters.Add("ZoneLink", new string[] { "ZoneA", "ZoneB", "cost" });
            node_parameters.Add("ZoneLoaded", new string[] { "on_loaded", "on_unloaded" });
        }

        private static List<ShortGUIDDescriptor> cathode_id_map;
        private static List<EnumDescriptor> cathode_enum_map;
        private static List<ShortGUIDDescriptor> node_friendly_names;
        private static Dictionary<string, string[]> node_parameters = new Dictionary<string, string[]>();
    }
}
