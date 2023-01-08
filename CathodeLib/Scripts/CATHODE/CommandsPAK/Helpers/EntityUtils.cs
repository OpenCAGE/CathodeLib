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

namespace CATHODE.Scripting
{
    //This serves as a helpful extension to manage entity names
    public static class EntityUtils
    {
        private static EntityNameTable _vanilla;
        private static EntityNameTable _custom;

        private static Commands _commands;

        /* Load all standard entity/composite names from our offline DB */
        static EntityUtils()
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(CathodeLib.Properties.Resources.composite_entity_names));
            _vanilla = new EntityNameTable(reader);
            _custom = new EntityNameTable();
            reader.Close();
        }

        /* Optionally, link a Commands file which can be used to save custom entity names to */
        public static void LinkCommands(Commands commands)
        {
            if (_commands != null)
            {
                _commands.OnLoaded -= LoadCustomNames;
                _commands.OnSaved -= SaveCustomNames;
            }

            _commands = commands;
            if (_commands != null)
            {
                _commands.OnLoaded += LoadCustomNames;
                _commands.OnSaved += SaveCustomNames;
            }

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
        public static void ApplyDefaults(FunctionEntity entity)
        {
            ApplyDefaultsInternal(entity);
        }

        /* Pull non-vanilla entity names from the CommandsPAK */
        private static void LoadCustomNames(string filepath)
        {
            _custom = (EntityNameTable)CustomTable.ReadTable(filepath, CustomEndTables.ENTITY_NAMES);
            if (_custom == null) _custom = new EntityNameTable();
        }

        /* Write non-vanilla entity names to the CommandsPAK */
        private static void SaveCustomNames(string filepath)
        {
            CustomTable.WriteTable(filepath, CustomEndTables.ENTITY_NAMES, _custom);
        }

        /* Applies all default parameter data to a Function entity (DESTRUCTIVE!) */
        private static void ApplyDefaultsInternal(FunctionEntity newEntity)
        {
            //Function entity points to a composite
            if (!CommandsUtils.FunctionTypeExists(newEntity.function))
            {
                if (_commands == null) return;
                Composite comp = _commands.Composites.FirstOrDefault(o => o.shortGUID == newEntity.function);
                if (comp == null) return;
                for (int i = 0; i < comp.variables.Count; i++)
                { 
                    newEntity.AddParameter(comp.variables[i].name, comp.variables[i].type, ParameterVariant.PARAMETER); //TODO: These are not always parameters - how can we distinguish?
                }
                return;
            }

            //Function entity points to a hard-coded function
            switch (CommandsUtils.GetFunctionType(newEntity.function))
            {
                case FunctionType.EntityMethodInterface:
                    newEntity.AddParameter("reference", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("callback", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("trigger", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("refresh", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("pause", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("resume", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("attach", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("detach", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("open", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("close", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("floating", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("sinking", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("lock", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("unlock", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("show", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("hide", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("spawn", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("despawn", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("light_switch_on", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("light_switch_off", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("proxy_enable", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("proxy_disable", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("simulate", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("keyframe", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("suspend", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("allow", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("request_open", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("request_close", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("request_lock", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("request_unlock", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("force_open", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("force_close", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("request_restore", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("rewind", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("kill", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("set", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("request_load", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("cancel_load", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("request_unload", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("cancel_unload", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("task_end", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("set_as_next_task", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("completed_pre_move", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("completed_interrupt", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("allow_early_end", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_allowing_interrupts", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("set_true", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("set_false", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("set_is_open", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("set_is_closed", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("apply_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("apply_stop", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("pause_activity", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("resume_activity", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("clear", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enter", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("exit", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("add_character", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("remove_character", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("purge", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("abort", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Evaluate", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("terminate", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("cancel", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("impact", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reloading", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("out_of_ammo", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("started_aiming", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stopped_aiming", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("expire", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Pin1", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Pin2", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Pin3", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Pin4", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Pin5", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Pin6", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Pin7", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Pin8", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Pin9", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Pin10", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Up", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Down", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Random", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_all", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_Random_1", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_Random_2", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_Random_3", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_Random_4", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_Random_5", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_Random_6", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_Random_7", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_Random_8", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_Random_9", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_Random_10", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_0", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_1", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_2", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_3", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_4", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_5", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_6", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_7", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_8", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_9", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_10", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_11", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_12", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_13", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_14", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_15", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Trigger_16", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("clear_user", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("clear_all", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("clear_of_alignment", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("clear_last", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_dynamic_rtpc", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_dynamic_rtpc", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("fail_game", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_X", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_X", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_Y", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_Y", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_Z", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_Z", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("fade_out", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("set_decal_time", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("increase_aggro", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("decrease_aggro", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("force_stand_down", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("force_aggressive", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("load_bank", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("unload_bank", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("bank_loaded", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("set_override", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_stealth", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_stealth", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_threat", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_threat", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_music", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_music", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("trigger_now", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("barrier_open", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("barrier_close", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_override", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_override", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("clear_pending_ui", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("hide_ui", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("show_ui", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("update_cost", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_chokepoint", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_chokepoint", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("update_squad_params", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_ping", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_ping", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_monitor", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_monitor", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_monitoring", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_monitoring", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("activate_tracker", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("deactivate_tracker", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_benchmark", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_benchmark", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("apply_hide", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("apply_show", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("display_tutorial", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("transition_completed", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("display_tutorial_breathing_1", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("display_tutorial_breathing_2", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("breathing_game_tutorial_fail", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("refresh_value", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("refresh_text", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_emitting", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("activate_camera", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("deactivate_camera", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("activate_behavior", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("deactivate_behavior", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("activate_modifier", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("deactivate_modifier", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("force_disable_highlight", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("cutting_panel_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("cutting_panel_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("keypad_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("keypad_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("traversal_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("lever_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("lever_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("button_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("button_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("ladder_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("ladder_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("hacking_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("hacking_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("rewire_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("rewire_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("terminal_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("terminal_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("suit_change_interaction_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("suit_change_interaction_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("cutscene_visibility_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("cutscene_visibility_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("hiding_visibility_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("hiding_visibility_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_radial", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_radial", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_radial_hacking_info", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_radial_hacking_info", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_radial_cutting_info", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_radial_cutting_info", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_radial_battery_info", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_radial_battery_info", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_hud_battery_info", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("enable_hud_battery_info", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("hide_objective_message", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("show_objective_message", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("finished_closing_container", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("seed", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("ignite", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("electrify", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("drench", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("poison", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("set_active", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("set_inactive", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("level_fade_start", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("level_fade_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("torch_turned_on", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("torch_turned_off", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("torch_new_battery_added", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("torch_battery_has_expired", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("torch_low_power", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("turn_off_torch", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("turn_on_torch", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("toggle_torch", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("resume_torch", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("allow_torch", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_timer", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_timer", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("notify_animation_started", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("notify_animation_finished", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("load_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("unload_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("pause_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("resume_cutscene", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("turn_on_system", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("turn_off_system", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("force_killtrap", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("cancel_force_killtrap", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disable_killtrap", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("cancel_disable_killtrap", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("hit_by_flamethrower", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("cancel_hit_by_flamethrower", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reload_fill", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reload_empty", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reload_load", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reload_open", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reload_fire", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reload_finish", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("display_hacking_upgrade", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("hide_hacking_upgrade", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("reset_hacking_success_flag", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("impact_with_world", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("start_interaction", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("stop_interaction", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("allow_interrupt", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("disallow_interrupt", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Get_In", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Add_NPC", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("Start_Breathing_Game", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("End_Breathing_Game", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("bind_all", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("verify", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("fake_light_on", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("fake_light_off", new cFloat(), ParameterVariant.METHOD); //
                    newEntity.AddParameter("callback_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("trigger_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("refresh_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("pause_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("resume_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("attach_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("detach_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("close_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("floating_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("sinking_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("lock_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("unlock_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("show_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("hide_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("spawn_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("despawn_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("light_switch_on_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("light_switch_off_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("proxy_enable_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("proxy_disable_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("simulate_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("keyframe_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("suspend_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("allow_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("request_open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("request_close_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("request_lock_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("request_unlock_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("force_open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("force_close_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("request_restore_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("rewind_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("kill_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("set_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("request_load_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("cancel_load_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("request_unload_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("cancel_unload_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("task_end_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("set_as_next_task_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("completed_pre_move_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("completed_interrupt_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("allow_early_end_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_allowing_interrupts_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("set_true_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("set_false_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("set_is_open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("set_is_closed_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("apply_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("apply_stop_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("pause_activity_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("resume_activity_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("clear_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enter_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("exit_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("add_character_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("remove_character_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("purge_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("abort_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Evaluate_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("terminate_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("cancel_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("impact_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reloading_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("out_of_ammo_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("started_aiming_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stopped_aiming_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("expire_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Pin1_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Pin2_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Pin3_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Pin4_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Pin5_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Pin6_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Pin7_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Pin8_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Pin9_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Pin10_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Up_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Down_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Random_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_all_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_Random_1_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_Random_2_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_Random_3_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_Random_4_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_Random_5_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_Random_6_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_Random_7_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_Random_8_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_Random_9_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_Random_10_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_0_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_1_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_2_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_3_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_4_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_5_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_6_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_7_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_8_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_9_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_10_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_11_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_12_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_13_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_14_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_15_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Trigger_16_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("clear_user_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("clear_all_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("clear_of_alignment_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("clear_last_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_dynamic_rtpc_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_dynamic_rtpc_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("fail_game_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_X_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_X_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_Y_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_Y_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_Z_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_Z_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("fade_out_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("set_decal_time_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("increase_aggro_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("decrease_aggro_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("force_stand_down_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("force_aggressive_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("load_bank_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("unload_bank_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("bank_loaded_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("set_override_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_stealth_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_stealth_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_threat_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_threat_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_music_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_music_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("trigger_now_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("barrier_open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("barrier_close_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_override_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_override_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("clear_pending_ui_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("hide_ui_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("show_ui_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("update_cost_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_chokepoint_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_chokepoint_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("update_squad_params_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_ping_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_ping_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_monitor_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_monitor_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_monitoring_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_monitoring_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("activate_tracker_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("deactivate_tracker_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_benchmark_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_benchmark_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("apply_hide_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("apply_show_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("display_tutorial_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("transition_completed_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("display_tutorial_breathing_1_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("display_tutorial_breathing_2_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("breathing_game_tutorial_fail_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("refresh_value_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("refresh_text_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_emitting_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("activate_camera_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("deactivate_camera_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("activate_behavior_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("deactivate_behavior_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("activate_modifier_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("deactivate_modifier_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("force_disable_highlight_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("cutting_panel_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("cutting_panel_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("keypad_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("keypad_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("traversal_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("lever_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("lever_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("button_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("button_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("ladder_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("ladder_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("hacking_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("hacking_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("rewire_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("rewire_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("terminal_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("terminal_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("suit_change_interaction_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("suit_change_interaction_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("cutscene_visibility_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("cutscene_visibility_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("hiding_visibility_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("hiding_visibility_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_radial_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_radial_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_radial_hacking_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_radial_hacking_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_radial_cutting_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_radial_cutting_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_radial_battery_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_radial_battery_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_hud_battery_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("enable_hud_battery_info_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("hide_objective_message_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("show_objective_message_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("finished_closing_container_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("seed_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("ignite_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("electrify_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("drench_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("poison_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("set_active_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("set_inactive_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("level_fade_start_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("level_fade_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("torch_turned_on_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("torch_turned_off_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("torch_new_battery_added_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("torch_battery_has_expired_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("torch_low_power_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("turn_off_torch_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("turn_on_torch_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("toggle_torch_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("resume_torch_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("allow_torch_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_timer_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_timer_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("notify_animation_started_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("notify_animation_finished_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("load_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("unload_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("pause_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("resume_cutscene_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("turn_on_system_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("turn_off_system_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("force_killtrap_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("cancel_force_killtrap_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disable_killtrap_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("cancel_disable_killtrap_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("hit_by_flamethrower_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("cancel_hit_by_flamethrower_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reload_fill_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reload_empty_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reload_load_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reload_open_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reload_fire_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reload_finish_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("display_hacking_upgrade_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("hide_hacking_upgrade_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("reset_hacking_success_flag_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("impact_with_world_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("start_interaction_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("stop_interaction_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("allow_interrupt_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("disallow_interrupt_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Get_In_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Add_NPC_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("Start_Breathing_Game_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("End_Breathing_Game_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("bind_all_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("verify_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("fake_light_on_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("fake_light_off_finished", new cFloat(), ParameterVariant.FINISHED); //
                    newEntity.AddParameter("triggered", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("refreshed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stopped", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("paused", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("resumed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("attached", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("detached", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("opened", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("closed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("enabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("disabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("disabled_gravity", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("enabled_gravity", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("locked", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("unlocked", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("shown", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hidden", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("spawned", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("despawned", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("light_switched_on", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("light_switched_off", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("proxy_enabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("proxy_disabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("simulating", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("keyframed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("suspended", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("allowed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("requested_open", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("requested_close", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("requested_lock", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("requested_unlock", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("forced_open", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("forced_close", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("requested_restore", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("rewound", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("killed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("been_set", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("load_requested", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("load_cancelled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("unload_requested", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("unload_cancelled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("task_ended", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("task_set_as_next", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_pre_move_completed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_completed_interrupt", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_early_end_allowed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_start_allowing_interrupts", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("set_to_true", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("set_to_false", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("set_to_open", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("set_to_closed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("start_applied", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stop_applied", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("pause_applied", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("resume_applied", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("cleared", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("entered", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("exited", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("reseted", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("added", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("removed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("purged", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("aborted", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Evaluated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("terminated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("cancelled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("impacted", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("reloading_handled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("out_of_ammo_handled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("started_aiming_handled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stopped_aiming_handled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("expired", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin1_Instant", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin2_Instant", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin3_Instant", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin4_Instant", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin5_Instant", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin6_Instant", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin7_Instant", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin8_Instant", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin9_Instant", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin10_Instant", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_Up", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_Down", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_Random", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_all", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_Random_1", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_Random_2", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_Random_3", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_Random_4", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_Random_5", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_Random_6", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_Random_7", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_Random_8", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_Random_9", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_reset_Random_10", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_0", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_1", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_2", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_3", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_4", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_5", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_6", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_7", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_8", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_9", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_10", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_11", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_12", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_13", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_14", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_15", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Pin_16", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("user_cleared", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("started_X", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stopped_X", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("started_Y", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stopped_Y", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("started_Z", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stopped_Z", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("faded_out", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("decal_time_set", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("aggro_increased", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("aggro_decreased", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("forced_stand_down", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("forced_aggressive", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("ui_hidden", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("ui_shown", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_updated_cost", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_enable_chokepoint", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("on_disable_chokepoint", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("squad_params_updated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("started_ping", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stopped_ping", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("started_monitor", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stopped_monitor", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("started_monitoring", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stopped_monitoring", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("activated_tracker", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("deactivated_tracker", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("started_benchmark", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stopped_benchmark", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hide_applied", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("show_applied", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("value_refeshed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("text_refeshed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("stopped_emitting", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("camera_activated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("camera_deactivated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("behavior_activated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("behavior_deactivated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("modifier_activated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("modifier_deactivated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("cutting_pannel_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("cutting_pannel_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("keypad_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("keypad_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("traversal_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("lever_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("lever_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("button_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("button_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("ladder_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("ladder_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hacking_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hacking_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("rewire_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("rewire_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("terminal_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("terminal_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("suit_change_interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("suit_change_interaction_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("cutscene_visibility_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("cutscene_visibility_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hiding_visibility_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hiding_visibility_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("radial_disabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("radial_enabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("radial_hacking_info_disabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("radial_hacking_info_enabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("radial_cutting_info_disabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("radial_cutting_info_enabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("radial_battery_info_disabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("radial_battery_info_enabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hud_battery_info_disabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hud_battery_info_enabled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("objective_message_hidden", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("objective_message_shown", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("closing_container_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("seeded", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("activated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("deactivated", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("level_fade_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("level_fade_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Turn_off_", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Turn_on_", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Toggle_Torch_", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Resume_", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Allow_", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("timer_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("timer_stopped", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("cutscene_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("cutscene_stopped", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("cutscene_paused", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("cutscene_resumed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("killtrap_forced", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("canceled_force_killtrap", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("upon_hit_by_flamethrower", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("reload_filled", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("reload_emptied", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("reload_loaded", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("reload_opened", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("reload_fired", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("reload_finished", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hacking_upgrade_displayed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hacking_upgrade_hidden", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("hacking_success_flag_reset", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("impacted_with_world", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("interaction_started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("interaction_stopped", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("interrupt_allowed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("interrupt_disallowed", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Getting_in", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Breathing_Game_Started", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("Breathing_Game_Ended", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("fake_light_on_triggered", new cFloat(), ParameterVariant.RELAY); //
                    newEntity.AddParameter("fake_light_off_triggered", new cFloat(), ParameterVariant.RELAY); //
                    break;
                case FunctionType.ScriptInterface:
                    newEntity.AddParameter("delete_me", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ProxyInterface:
                    newEntity.AddParameter("proxy_filter_targets", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("proxy_enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.ScriptVariable:
                    newEntity.AddParameter("on_changed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_restored", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.SensorInterface:
                    newEntity.AddParameter("start_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.CloseableInterface:
                    newEntity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.GateInterface:
                    newEntity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("lock_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.ZoneInterface:
                    newEntity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_unloaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_streaming", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("force_visible_on_load", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.AttachmentInterface:
                    newEntity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("attachment", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.SensorAttachmentInterface:
                    newEntity.AddParameter("start_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.CompositeInterface:
                    newEntity.AddParameter("is_template", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("local_only", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("suspend_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("is_shared", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("requires_script_for_current_gen", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("requires_script_for_next_gen", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("convert_to_physics", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("delete_standard_collision", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("delete_ballistic_collision", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("disable_display", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("disable_collision", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("disable_simulation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("mapping", new cString(), ParameterVariant.PARAMETER); //FilePath
                    newEntity.AddParameter("include_in_planar_reflections", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.EnvironmentModelReference:
                    cResource resourceData2 = new cResource(newEntity.shortGUID);
                    resourceData2.AddResource(ResourceType.ANIMATED_MODEL);
                    newEntity.parameters.Add(new Parameter("resource", resourceData2, ParameterVariant.INTERNAL));
                    break;
                case FunctionType.SplinePath:
                    newEntity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("orientated", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("points", new cSpline(), ParameterVariant.INTERNAL); //SplineData
                    break;
                case FunctionType.Box:
                    newEntity.AddParameter("event", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.HasAccessAtDifficulty:
                    newEntity.AddParameter("difficulty", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.UpdateLeaderBoardDisplay:
                    newEntity.AddParameter("time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SetNextLoadingMovie:
                    newEntity.AddParameter("playlist_to_load", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ButtonMashPrompt:
                    newEntity.AddParameter("on_back_to_zero", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_degrade", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_mashed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("count", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("mashes_to_completion", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("time_between_degrades", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("use_degrade", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("hold_to_charge", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GetFlashIntValue:
                    newEntity.AddParameter("callback", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("int_value", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("callback_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.GetFlashFloatValue:
                    newEntity.AddParameter("callback", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("float_value", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("callback_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.Sphere:
                    newEntity.AddParameter("event", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ImpactSphere:
                    newEntity.AddParameter("event", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("include_physics", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UiSelectionBox:
                    newEntity.AddParameter("is_priority", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UiSelectionSphere:
                    newEntity.AddParameter("is_priority", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CollisionBarrier:
                    newEntity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("collision_type", new cEnum(EnumType.COLLISION_TYPE, 0), ParameterVariant.PARAMETER); //COLLISION_TYPE
                    newEntity.AddParameter("static_collision", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayerTriggerBox:
                    newEntity.AddParameter("on_entered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_exited", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.PlayerUseTriggerBox:
                    newEntity.AddParameter("on_entered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_exited", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_use", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("text", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ModelReference:
                    newEntity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("simulate_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("convert_to_physics", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("material", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("occludes_atmosphere", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("lod_ranges", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("intensity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("radiosity_multiplier", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("emissive_tint", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("replace_intensity", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("replace_tint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("decal_scale", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("lightdecal_tint", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("lightdecal_intensity", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("diffuse_colour_scale", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("diffuse_opacity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("vertex_colour_scale", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("vertex_opacity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("uv_scroll_speed_x", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("uv_scroll_speed_y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("alpha_blend_noise_power_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("alpha_blend_noise_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("alpha_blend_noise_uv_offset_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("alpha_blend_noise_uv_offset_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("dirt_multiply_blend_spec_power_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("dirt_map_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("remove_on_damaged", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("damage_threshold", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("is_debris", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_prop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_thrown", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("report_sliding", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("force_keyframed", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("force_transparent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("soft_collision", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("allow_reposition_of_physics", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("disable_size_culling", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("cast_shadows", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("cast_shadows_in_torch", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("alpha_light_offset_x", new cFloat(0.0f), ParameterVariant.INTERNAL); //float
                    newEntity.AddParameter("alpha_light_offset_y", new cFloat(0.0f), ParameterVariant.INTERNAL); //float
                    newEntity.AddParameter("alpha_light_scale_x", new cFloat(1.0f), ParameterVariant.INTERNAL); //float
                    newEntity.AddParameter("alpha_light_scale_y", new cFloat(1.0f), ParameterVariant.INTERNAL); //float
                    newEntity.AddParameter("alpha_light_average_normal", new cVector3(), ParameterVariant.INTERNAL); //Direction
                    cResource resourceData = new cResource(newEntity.shortGUID);
                    resourceData.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    newEntity.parameters.Add(new Parameter("resource", resourceData, ParameterVariant.INTERNAL));
                    break;
                case FunctionType.LightReference:
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("occlusion_geometry", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //RENDERABLE_INSTANCE
                    newEntity.AddParameter("mastered_by_visibility", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("exclude_shadow_entities", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("type", new cEnum(EnumType.LIGHT_TYPE, 0), ParameterVariant.PARAMETER); //LIGHT_TYPE
                    newEntity.AddParameter("defocus_attenuation", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("start_attenuation", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("end_attenuation", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("physical_attenuation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("near_dist", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("near_dist_shadow_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("inner_cone_angle", new cFloat(22.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("outer_cone_angle", new cFloat(45.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("intensity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("radiosity_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("area_light_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("diffuse_softness", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("diffuse_bias", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("glossiness_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("flare_occluder_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("flare_spot_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("flare_intensity_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("cast_shadow", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("fade_type", new cEnum(EnumType.LIGHT_FADE_TYPE, 1), ParameterVariant.PARAMETER); //LIGHT_FADE_TYPE
                    newEntity.AddParameter("is_specular", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("has_lens_flare", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("has_noclip", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_square_light", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_flash_light", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("no_alphalight", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("shadow_priority", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("aspect_ratio", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("gobo_texture", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("horizontal_gobo_flip", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("strip_length", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("distance_mip_selection_gobo", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("volume", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("volume_end_attenuation", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("volume_colour_factor", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("volume_density", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("depth_bias", new cFloat(0.05f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("slope_scale_depth_bias", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.ParticleEmitterReference:
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("mastered_by_visibility", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("material", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("unique_material", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("quality_level", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("bounds_max", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("bounds_min", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("DRAW_PASS", new cInteger(8), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ASPECT_RATIO", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FADE_AT_DISTANCE", new cFloat(5000.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PARTICLE_COUNT", new cInteger(100), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SYSTEM_EXPIRY_TIME", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SIZE_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SIZE_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SIZE_END_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SIZE_END_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHA_IN", new cFloat(0.01f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHA_OUT", new cFloat(99.99f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MASK_AMOUNT_MIN", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MASK_AMOUNT_MAX", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MASK_AMOUNT_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PARTICLE_EXPIRY_TIME_MIN", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PARTICLE_EXPIRY_TIME_MAX", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("COLOUR_SCALE_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("COLOUR_SCALE_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIND_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIND_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIND_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHA_REF_VALUE", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("BILLBOARDING_LS", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARDING", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARDING_NONE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARDING_ON_AXIS_X", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARDING_ON_AXIS_Y", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARDING_ON_AXIS_Z", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARDING_VELOCITY_ALIGNED", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARDING_VELOCITY_STRETCHED", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARDING_SPHERE_PROJECTION", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BLENDING_STANDARD", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BLENDING_ALPHA_REF", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BLENDING_ADDITIVE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BLENDING_PREMULTIPLIED", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BLENDING_DISTORTION", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("LOW_RES", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("EARLY_ALPHA", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("LOOPING", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ANIMATED_ALPHA", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("NONE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("LIGHTING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("PER_PARTICLE_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("X_AXIS_FLIP", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Y_AXIS_FLIP", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARD_FACING", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARDING_ON_AXIS_FADEOUT", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BILLBOARDING_CAMERA_LOCKED", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("CAMERA_RELATIVE_POS_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CAMERA_RELATIVE_POS_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CAMERA_RELATIVE_POS_Z", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPHERE_PROJECTION_RADIUS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DISTORTION_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SCALE_MODIFIER", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CPU", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SPAWN_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPAWN_RATE_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPAWN_NUMBER", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("LIFETIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LIFETIME_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WORLD_TO_LOCAL_BLEND_START", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WORLD_TO_LOCAL_BLEND_END", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WORLD_TO_LOCAL_MAX_DIST", new cFloat(1000.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CELL_EMISSION", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("CELL_MAX_DIST", new cFloat(6.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CUSTOM_SEED_CPU", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SEED", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ALPHA_TEST", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ZTEST", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("START_MID_END_SPEED", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SPEED_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPEED_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPEED_MID_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPEED_MID_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPEED_END_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPEED_END_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LAUNCH_DECELERATE_SPEED", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("LAUNCH_DECELERATE_SPEED_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LAUNCH_DECELERATE_SPEED_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LAUNCH_DECELERATE_DEC_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EMISSION_AREA", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("EMISSION_AREA_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EMISSION_AREA_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EMISSION_AREA_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EMISSION_SURFACE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("EMISSION_DIRECTION_SURFACE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("AREA_CUBOID", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("AREA_SPHEROID", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("AREA_CYLINDER", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("PIVOT_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PIVOT_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("GRAVITY", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("GRAVITY_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("GRAVITY_MAX_STRENGTH", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("COLOUR_TINT", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("COLOUR_TINT_START", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("COLOUR_TINT_END", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("COLOUR_USE_MID", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("COLOUR_TINT_MID", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("COLOUR_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPREAD_FEATURE", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SPREAD_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPREAD", new cFloat(360.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ROTATION", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ROTATION_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ROTATION_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ROTATION_RANDOM_START", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ROTATION_BASE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ROTATION_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ROTATION_RAMP", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ROTATION_IN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ROTATION_OUT", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ROTATION_DAMP", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FADE_NEAR_CAMERA", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("FADE_NEAR_CAMERA_MAX_DIST", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FADE_NEAR_CAMERA_THRESHOLD", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TEXTURE_ANIMATION", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("TEXTURE_ANIMATION_FRAMES", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("NUM_ROWS", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("TEXTURE_ANIMATION_LOOP_COUNT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("RANDOM_START_FRAME", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("WRAP_FRAMES", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("NO_ANIM", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SUB_FRAME_BLEND", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SOFTNESS_EDGE", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SOFTNESS_ALPHA_THICKNESS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SOFTNESS_ALPHA_DEPTH_MODIFIER", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("REVERSE_SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("REVERSE_SOFTNESS_EDGE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PIVOT_AND_TURBULENCE", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("PIVOT_OFFSET_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PIVOT_OFFSET_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TURBULENCE_FREQUENCY_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TURBULENCE_FREQUENCY_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TURBULENCE_AMOUNT_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TURBULENCE_AMOUNT_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHATHRESHOLD", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ALPHATHRESHOLD_TOTALTIME", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHATHRESHOLD_RANGE", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHATHRESHOLD_BEGINSTART", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHATHRESHOLD_BEGINSTOP", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHATHRESHOLD_ENDSTART", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHATHRESHOLD_ENDSTOP", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("COLOUR_RAMP", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("COLOUR_RAMP_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("COLOUR_RAMP_ALPHA", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("DEPTH_FADE_AXIS", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("DEPTH_FADE_AXIS_DIST", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FADE_AXIS_PERCENT", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_UV_ANIMATION", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("FLOW_TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("CYCLE_TIME", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_TEX_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("INFINITE_PROJECTION", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("PARALLAX_POSITION", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DISTORTION_OCCLUSION", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("AMBIENT_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("AMBIENT_LIGHTING_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("NO_CLIP", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.RibbonEmitterReference:
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("mastered_by_visibility", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("include_in_planar_reflections", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("material", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("unique_material", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("quality_level", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BLENDING_STANDARD", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BLENDING_ALPHA_REF", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BLENDING_ADDITIVE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BLENDING_PREMULTIPLIED", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BLENDING_DISTORTION", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("NO_MIPS", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("UV_SQUARED", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("LOW_RES", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("LIGHTING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("MASK_AMOUNT_MIN", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MASK_AMOUNT_MAX", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MASK_AMOUNT_MIDPOINT", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DRAW_PASS", new cInteger(8), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SYSTEM_EXPIRY_TIME", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LIFETIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SMOOTHED", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("WORLD_TO_LOCAL_BLEND_START", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WORLD_TO_LOCAL_BLEND_END", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WORLD_TO_LOCAL_MAX_DIST", new cFloat(1000.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TEXTURE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("UV_REPEAT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("UV_SCROLLSPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MULTI_TEXTURE", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("U2_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("V2_REPEAT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("V2_SCROLLSPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MULTI_TEXTURE_BLEND", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("MULTI_TEXTURE_ADD", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("MULTI_TEXTURE_MULT", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("MULTI_TEXTURE_MAX", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("MULTI_TEXTURE_MIN", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SECOND_TEXTURE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("TEXTURE_MAP2", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("CONTINUOUS", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("BASE_LOCKED", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SPAWN_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TRAILING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("INSTANT", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("RATE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("TRAIL_SPAWN_RATE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TRAIL_DELAY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MAX_TRAILS", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("POINT_TO_POINT", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("TARGET_POINT_POSITION", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ABS_FADE_IN_0", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ABS_FADE_IN_1", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FORCES", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("GRAVITY_STRENGTH", new cFloat(-4.81f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("GRAVITY_MAX_STRENGTH", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DRAG_STRENGTH", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIND_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIND_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIND_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("START_MID_END_SPEED", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SPEED_START_MIN", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPEED_START_MAX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIDTH", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("WIDTH_START", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIDTH_MID", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIDTH_END", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIDTH_IN", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WIDTH_OUT", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("COLOUR_TINT", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("COLOUR_SCALE_START", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("COLOUR_SCALE_MID", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("COLOUR_SCALE_END", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("COLOUR_TINT_START", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("COLOUR_TINT_MID", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("COLOUR_TINT_END", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("ALPHA_FADE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("FADE_IN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FADE_OUT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EDGE_FADE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ALPHA_ERODE", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SIDE_ON_FADE", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SIDE_FADE_START", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SIDE_FADE_END", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DISTANCE_SCALING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("DIST_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPREAD_FEATURE", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SPREAD_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPREAD", new cFloat(0.99999f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EMISSION_AREA", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("EMISSION_AREA_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EMISSION_AREA_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EMISSION_AREA_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("AREA_CUBOID", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("AREA_SPHEROID", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("AREA_CYLINDER", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("COLOUR_RAMP", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("COLOUR_RAMP_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("SOFTNESS", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SOFTNESS_EDGE", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SOFTNESS_ALPHA_THICKNESS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SOFTNESS_ALPHA_DEPTH_MODIFIER", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("AMBIENT_LIGHTING", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("AMBIENT_LIGHTING_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("NO_CLIP", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.GPU_PFXEmitterReference:
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("mastered_by_visibility", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("EFFECT_NAME", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("SPAWN_NUMBER", new cInteger(100), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SPAWN_RATE", new cFloat(100.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPREAD_MIN", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPREAD_MAX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EMITTER_SIZE", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPEED_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LIFETIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LIFETIME_VAR", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FogSphere:
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("COLOUR_TINT", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EARLY_ALPHA", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("LOW_RES_ALPHA", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("CONVEX_GEOM", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DISABLE_SIZE_CULLING", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("NO_CLIP", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ALPHA_LIGHTING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DYNAMIC_ALPHA_LIGHTING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("EXPONENTIAL_DENSITY", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("SCENE_DEPENDANT_DENSITY", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("FRESNEL_TERM", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("FRESNEL_POWER", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SOFTNESS", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("SOFTNESS_EDGE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("BLEND_ALPHA_OVER_DISTANCE", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("FAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("NEAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_BLEND_ALPHA_OVER_DISTANCE", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("SECONDARY_FAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_NEAR_BLEND_DISTANCE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_INTERSECT_COLOUR", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DEPTH_INTERSECT_COLOUR_VALUE", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DEPTH_INTERSECT_ALPHA_VALUE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_INTERSECT_RANGE", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.FogBox:
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("GEOMETRY_TYPE", new cEnum(EnumType.FOG_BOX_TYPE, 1), ParameterVariant.PARAMETER); //FOG_BOX_TYPE
                    newEntity.AddParameter("COLOUR_TINT", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DISTANCE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ANGLE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("BILLBOARD", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("EARLY_ALPHA", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("LOW_RES", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("CONVEX_GEOM", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("THICKNESS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("START_DISTANT_CLIP", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("START_DISTANCE_FADE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SOFTNESS", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("SOFTNESS_EDGE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LINEAR_HEIGHT_DENSITY", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("SMOOTH_HEIGHT_DENSITY", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("HEIGHT_MAX_DENSITY", new cFloat(0.4f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FRESNEL_FALLOFF", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("FRESNEL_POWER", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_INTERSECT_COLOUR", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DEPTH_INTERSECT_INITIAL_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DEPTH_INTERSECT_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_INTERSECT_MIDPOINT_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DEPTH_INTERSECT_MIDPOINT_ALPHA", new cFloat(1.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_INTERSECT_MIDPOINT_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_INTERSECT_END_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DEPTH_INTERSECT_END_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_INTERSECT_END_DEPTH", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.SurfaceEffectSphere:
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("COLOUR_TINT", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("COLOUR_TINT_OUTER", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FADE_OUT_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SURFACE_WRAP", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ROUGHNESS_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPARKLE_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("METAL_STYLE_REFLECTIONS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SHININESS_OPACITY", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TILING_ZY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TILING_ZX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TILING_XY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("WS_LOCKED", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("SPARKLE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("ENVMAP", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("ENVMAP_PERCENT_EMISSIVE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPHERE", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.SurfaceEffectBox:
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("COLOUR_TINT", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("COLOUR_TINT_OUTER", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("INTENSITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("OPACITY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FADE_OUT_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SURFACE_WRAP", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ROUGHNESS_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPARKLE_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("METAL_STYLE_REFLECTIONS", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SHININESS_OPACITY", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TILING_ZY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TILING_ZX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("TILING_XY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FALLOFF", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("WS_LOCKED", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("TEXTURE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("SPARKLE_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("ENVMAP", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("ENVMAP_PERCENT_EMISSIVE", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPHERE", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("BOX", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.SimpleWater:
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("SHININESS", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("softness_edge", new cFloat(0.005f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FRESNEL_POWER", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MIN_FRESNEL", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MAX_FRESNEL", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LOW_RES_ALPHA_PASS", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ATMOSPHERIC_FOGGING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("SPEED", new cFloat(0.01f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_NORMAL_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("SECONDARY_SPEED", new cFloat(-0.01f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHA_MASKING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ALPHA_MASK", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("FLOW_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("CYCLE_TIME", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ENVIRONMENT_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ENVIRONMENT_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("ENVIRONMENT_MAP_MULT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LOCALISED_ENVIRONMENT_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ENVMAP_SIZE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LOCALISED_ENVMAP_BOX_PROJECTION", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ENVMAP_BOXPROJ_BB_SCALE", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("REFLECTIVE_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("REFLECTION_PERTURBATION_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FOG_INITIAL_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DEPTH_FOG_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FOG_MIDPOINT_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DEPTH_FOG_MIDPOINT_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FOG_MIDPOINT_DEPTH", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FOG_END_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DEPTH_FOG_END_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FOG_END_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CAUSTIC_TEXTURE", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("CAUSTIC_TEXTURE_SCALE", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CAUSTIC_REFRACTIONS", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("CAUSTIC_REFLECTIONS", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("CAUSTIC_SPEED_SCALAR", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CAUSTIC_INTENSITY", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CAUSTIC_SURFACE_WRAP", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CAUSTIC_HEIGHT", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    newEntity.AddParameter("CAUSTIC_TEXTURE_INDEX", new cInteger(-1), ParameterVariant.INTERNAL); //int
                    break;
                case FunctionType.SimpleRefraction:
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("DISTANCEFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_NORMAL_MAPPING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("SECONDARY_NORMAL_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHA_MASKING", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ALPHA_MASK", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("DISTORTION_OCCLUSION", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("MIN_OCCLUSION_DISTANCE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_UV_ANIMATION", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("FLOW_MAP", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("CYCLE_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.ProjectiveDecal:
                    newEntity.AddParameter("deleted", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("include_in_planar_reflections", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("material", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.LODControls:
                    newEntity.AddParameter("lod_range_scalar", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("disable_lods", new cBool(false), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.LightingMaster:
                    newEntity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.DebugCamera:
                    newEntity.AddParameter("linked_cameras", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.CameraResource:
                    newEntity.AddParameter("on_enter_transition_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_exit_transition_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("camera_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("is_camera_transformation_local", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("camera_transformation", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("fov", new cFloat(45.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("clipping_planes_preset", new cEnum(EnumType.CLIPPING_PLANES_PRESETS, 2), ParameterVariant.PARAMETER); //CLIPPING_PLANES_PRESETS
                    newEntity.AddParameter("is_ghost", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("converge_to_player_camera", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("reset_player_camera_on_exit", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("enable_enter_transition", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("transition_curve_direction", new cEnum(EnumType.TRANSITION_DIRECTION, 4), ParameterVariant.PARAMETER); //TRANSITION_DIRECTION
                    newEntity.AddParameter("transition_curve_strength", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("enable_exit_transition", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("exit_transition_curve_direction", new cEnum(EnumType.TRANSITION_DIRECTION, 4), ParameterVariant.PARAMETER); //TRANSITION_DIRECTION
                    newEntity.AddParameter("exit_transition_curve_strength", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("exit_transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("exit_transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("exit_transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraFinder:
                    newEntity.AddParameter("camera_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.CameraBehaviorInterface:
                    newEntity.AddParameter("start_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("linked_cameras", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CAMERA_INSTANCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CAMERA_INSTANCE
                    newEntity.AddParameter("behavior_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("priority", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("threshold", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("blend_in", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("blend_out", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.HandCamera:
                    newEntity.AddParameter("noise_type", new cEnum(EnumType.NOISE_TYPE, 0), ParameterVariant.PARAMETER); //NOISE_TYPE
                    newEntity.AddParameter("frequency", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("damping", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("rotation_intensity", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("min_fov_range", new cFloat(45.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_fov_range", new cFloat(45.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("min_noise", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_noise", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraShake:
                    newEntity.AddParameter("relative_transformation", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("impulse_intensity", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("impulse_position", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("shake_type", new cEnum(EnumType.SHAKE_TYPE, 0), ParameterVariant.PARAMETER); //SHAKE_TYPE
                    newEntity.AddParameter("shake_frequency", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("max_rotation_angles", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("max_position_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("shake_rotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("shake_position", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("bone_shaking", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("override_weapon_swing", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("internal_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("external_radius", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("strength_damping", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("explosion_push_back", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("spring_constant", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("spring_damping", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraPathDriven:
                    newEntity.AddParameter("position_path", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("target_path", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("reference_path", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("position_path_transform", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target_path_transform", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("reference_path_transform", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("point_to_project", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("path_driven_type", new cEnum(EnumType.PATH_DRIVEN_TYPE, 2), ParameterVariant.PARAMETER); //PATH_DRIVEN_TYPE
                    newEntity.AddParameter("invert_progression", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("position_path_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("target_path_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("animation_duration", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FixedCamera:
                    newEntity.AddParameter("use_transform_position", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("transform_position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("camera_position", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("camera_target", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("camera_position_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("camera_target_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("apply_target", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("apply_position", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_target_offset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_position_offset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.BoneAttachedCamera:
                    newEntity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("position_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("rotation_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("movement_damping", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("bone_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ControllableRange:
                    newEntity.AddParameter("min_range_x", new cFloat(-180.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_range_x", new cFloat(180.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("min_range_y", new cFloat(-180.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_range_y", new cFloat(180.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("min_feather_range_x", new cFloat(-180.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_feather_range_x", new cFloat(180.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("min_feather_range_y", new cFloat(-180.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_feather_range_y", new cFloat(180.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("speed_x", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("speed_y", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("damping_x", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("damping_y", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("mouse_speed_x", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("mouse_speed_y", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.StealCamera:
                    newEntity.AddParameter("on_converged", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("focus_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("steal_type", new cEnum(EnumType.STEAL_CAMERA_TYPE, 0), ParameterVariant.PARAMETER); //STEAL_CAMERA_TYPE
                    newEntity.AddParameter("check_line_of_sight", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("blend_in_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FollowCameraModifier:
                    newEntity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("position_curve", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("target_curve", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("modifier_type", new cEnum(EnumType.FOLLOW_CAMERA_MODIFIERS, 0), ParameterVariant.PARAMETER); //FOLLOW_CAMERA_MODIFIERS
                    newEntity.AddParameter("position_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("target_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("field_of_view", new cFloat(35.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("force_state", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("force_state_initial_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("can_mirror", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_first_person", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("bone_blending_ratio", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("movement_speed", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("movement_speed_vertical", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("movement_damping", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("horizontal_limit_min", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("horizontal_limit_max", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("vertical_limit_min", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("vertical_limit_max", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("mouse_speed_hori", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("mouse_speed_vert", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("acceleration_duration", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("acceleration_ease_in", new cFloat(0.25f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("acceleration_ease_out", new cFloat(0.25f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("transition_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("transition_ease_in", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("transition_ease_out", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraPath:
                    newEntity.AddParameter("linked_splines", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("path_name", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("path_type", new cEnum(EnumType.CAMERA_PATH_TYPE, 0), ParameterVariant.PARAMETER); //CAMERA_PATH_TYPE
                    newEntity.AddParameter("path_class", new cEnum(EnumType.CAMERA_PATH_CLASS, 0), ParameterVariant.PARAMETER); //CAMERA_PATH_CLASS
                    newEntity.AddParameter("is_local", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("relative_position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("is_loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraAimAssistant:
                    newEntity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("activation_radius", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("inner_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("camera_speed_attenuation", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("min_activation_distance", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("fading_range", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CameraPlayAnimation:
                    newEntity.AddParameter("on_animation_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("animated_camera", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("position_marker", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("character_to_focus", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("focal_length_mm", new cFloat(75.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("focal_plane_m", new cFloat(2.5f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("fnum", new cFloat(2.8f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("animation_length", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("frames_count", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("result_transformation", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("data_file", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("start_frame", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("end_frame", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("play_speed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("loop_play", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("clipping_planes_preset", new cEnum(EnumType.CLIPPING_PLANES_PRESETS, 2), ParameterVariant.PARAMETER); //CLIPPING_PLANES_PRESETS
                    newEntity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("dof_key", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("shot_number", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("override_dof", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("focal_point_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("bone_to_focus", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.CamPeek:
                    newEntity.AddParameter("pos", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("x_ratio", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("y_ratio", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("range_left", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("range_right", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("range_up", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("range_down", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("range_forward", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("range_backward", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("speed_x", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("speed_y", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("damping_x", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("damping_y", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("focal_distance", new cFloat(8.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("focal_distance_y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("roll_factor", new cFloat(15.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("use_ik_solver", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_horizontal_plane", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("stick", new cEnum(EnumType.SIDE, 0), ParameterVariant.PARAMETER); //SIDE
                    newEntity.AddParameter("disable_collision_test", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CameraDofController:
                    newEntity.AddParameter("character_to_focus", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("focal_length_mm", new cFloat(75.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("focal_plane_m", new cFloat(2.5f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("fnum", new cFloat(2.8f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("focal_point_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("bone_to_focus", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ClipPlanesController:
                    newEntity.AddParameter("near_plane", new cFloat(0.1f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("far_plane", new cFloat(1000.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("update_near", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("update_far", new cBool(false), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.GetCurrentCameraTarget:
                    newEntity.AddParameter("target", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("distance", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.Logic_Vent_Entrance:
                    newEntity.AddParameter("Hide_Pos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Emit_Pos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("force_stand_on_exit", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Logic_Vent_System:
                    newEntity.AddParameter("Vent_Entrances", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.VENT_ENTRANCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //VENT_ENTRANCE
                    break;
                case FunctionType.CharacterCommand:
                    newEntity.AddParameter("command_started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("override_all_ai", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_Follow:
                    newEntity.AddParameter("entered_inner_radius", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exitted_outer_radius", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Waypoint", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("idle_stance", new cEnum(EnumType.IDLE, 0), ParameterVariant.PARAMETER); //IDLE
                    newEntity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER); //MOVE
                    newEntity.AddParameter("inner_radius", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("outer_radius", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("prefer_traversals", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_FollowUsingJobs:
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("target_to_follow", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("who_Im_leading", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("fastest_allowed_move_type", new cEnum(EnumType.MOVE, 3), ParameterVariant.PARAMETER); //MOVE
                    newEntity.AddParameter("slowest_allowed_move_type", new cEnum(EnumType.MOVE, 0), ParameterVariant.PARAMETER); //MOVE
                    newEntity.AddParameter("centre_job_restart_radius", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("inner_radius", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("outer_radius", new cFloat(8.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("job_select_radius", new cFloat(6.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("job_cancel_radius", new cFloat(8.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("teleport_required_range", new cFloat(25.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("teleport_radius", new cFloat(20.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("prefer_traversals", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("avoid_player", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("allow_teleports", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("follow_type", new cEnum(EnumType.FOLLOW_TYPE, 0), ParameterVariant.PARAMETER); //FOLLOW_TYPE
                    newEntity.AddParameter("clamp_speed", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_FollowOffset:
                    newEntity.AddParameter("offset", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("target_to_follow", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.AnimationMask:
                    newEntity.AddParameter("maskHips", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskTorso", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskNeck", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskHead", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskFace", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskLeftLeg", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskRightLeg", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskLeftArm", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskRightArm", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskLeftHand", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskRightHand", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskLeftFingers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskRightFingers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskTail", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskLips", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskEyes", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskLeftShoulder", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskRightShoulder", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskRoot", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskPrecedingLayers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskSelf", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maskFollowingLayers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("weight", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddResource(ResourceType.ANIMATION_MASK_RESOURCE);
                    break;
                case FunctionType.CMD_PlayAnimation:
                    newEntity.AddParameter("Interrupted", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("badInterrupted", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SafePos", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Marker", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("ExitPosition", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("ExternalStartTime", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("ExternalTime", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("OverrideCharacter", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("OptionalMask", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("animationLength", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("AnimationSet", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("Animation", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("StartFrame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("EndFrame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("PlayCount", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("PlaySpeed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("AllowGravity", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("AllowCollision", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Start_Instantly", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("AllowInterruption", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("RemoveMotion", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DisableGunLayer", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("BlendInTime", new cFloat(0.3f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("GaitSyncStart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ConvergenceTime", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LocationConvergence", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("OrientationConvergence", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("UseExitConvergence", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ExitConvergenceTime", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Mirror", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("FullCinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("RagdollEnabled", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("NoIK", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("NoFootIK", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("NoLayers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("PlayerAnimDrivenView", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ExertionFactor", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("AutomaticZoning", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ManualLoading", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("IsCrouchedAnim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("InitiallyBackstage", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Death_by_ragdoll_only", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("dof_key", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("shot_number", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("UseShivaArms", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddResource(ResourceType.PLAY_ANIMATION_DATA_RESOURCE);
                    break;
                case FunctionType.CMD_Idle:
                    newEntity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("interrupted", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("target_to_face", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("should_face_target", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("should_raise_gun_while_turning", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("desired_stance", new cEnum(EnumType.CHARACTER_STANCE, 0), ParameterVariant.PARAMETER); //CHARACTER_STANCE
                    newEntity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("idle_style", new cEnum(EnumType.IDLE_STYLE, 1), ParameterVariant.PARAMETER); //IDLE_STYLE
                    newEntity.AddParameter("lock_cameras", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("anchor", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("start_instantly", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_GoTo:
                    newEntity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Waypoint", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("AimTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER); //MOVE
                    newEntity.AddParameter("enable_lookaround", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_stopping_anim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("always_stop_at_radius", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("stop_at_radius_if_lined_up", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("continue_from_previous_move", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("arrived_radius", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("allow_to_use_vents", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DestinationIsBackstage", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("maintain_current_facing", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("start_instantly", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_GoToCover:
                    newEntity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("entered_cover", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("CoverPoint", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("AimTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER); //MOVE
                    newEntity.AddParameter("SearchRadius", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("enable_lookaround", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("continue_from_previous_move", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_MoveTowards:
                    newEntity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("MoveTarget", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("AimTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("move_type", new cEnum(EnumType.MOVE, 1), ParameterVariant.PARAMETER); //MOVE
                    newEntity.AddParameter("disallow_traversal", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("should_be_aiming", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_current_target_as_aim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("never_succeed", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_Die:
                    newEntity.AddParameter("Killer", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("death_style", new cEnum(EnumType.DEATH_STYLE, 0), ParameterVariant.PARAMETER); //DEATH_STYLE
                    break;
                case FunctionType.CMD_LaunchMeleeAttack:
                    newEntity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("melee_attack_type", new cEnum(EnumType.MELEE_ATTACK_TYPE, 0), ParameterVariant.PARAMETER); //MELEE_ATTACK_TYPE
                    newEntity.AddParameter("enemy_type", new cEnum(EnumType.ENEMY_TYPE, 15), ParameterVariant.PARAMETER); //ENEMY_TYPE
                    newEntity.AddParameter("melee_attack_index", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("skip_convergence", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_ModifyCombatBehaviour:
                    newEntity.AddParameter("behaviour_type", new cEnum(EnumType.COMBAT_BEHAVIOUR, 0), ParameterVariant.PARAMETER); //COMBAT_BEHAVIOUR
                    newEntity.AddParameter("status", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_HolsterWeapon:
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("should_holster", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("skip_anims", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("equipment_slot", new cEnum(EnumType.EQUIPMENT_SLOT, -2), ParameterVariant.PARAMETER); //EQUIPMENT_SLOT
                    newEntity.AddParameter("force_player_unarmed_on_holster", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("force_drop_held_item", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_ForceReloadWeapon:
                    newEntity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CMD_ForceMeleeAttack:
                    newEntity.AddParameter("melee_attack_type", new cEnum(EnumType.MELEE_ATTACK_TYPE, 0), ParameterVariant.PARAMETER); //MELEE_ATTACK_TYPE
                    newEntity.AddParameter("enemy_type", new cEnum(EnumType.ENEMY_TYPE, 15), ParameterVariant.PARAMETER); //ENEMY_TYPE
                    newEntity.AddParameter("melee_attack_index", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.CHR_ModifyBreathing:
                    newEntity.AddParameter("Exhaustion", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_HoldBreath:
                    newEntity.AddParameter("ExhaustionOnStop", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_DeepCrouch:
                    newEntity.AddParameter("crouch_amount", new cFloat(0.4f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("smooth_damping", new cFloat(0.4f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("allow_stand_up", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_PlaySecondaryAnimation:
                    newEntity.AddParameter("Interrupted", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Marker", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("OptionalMask", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("ExternalStartTime", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("ExternalTime", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("animationLength", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("AnimationSet", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("Animation", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("StartFrame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("EndFrame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("PlayCount", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("PlaySpeed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("StartInstantly", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("AllowInterruption", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("BlendInTime", new cFloat(0.3f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("GaitSyncStart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Mirror", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("AnimationLayer", new cEnum(EnumType.SECONDARY_ANIMATION_LAYER, 0), ParameterVariant.PARAMETER); //SECONDARY_ANIMATION_LAYER
                    newEntity.AddParameter("AutomaticZoning", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ManualLoading", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_LocomotionModifier:
                    newEntity.AddParameter("Can_Run", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Can_Crouch", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Can_Aim", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Can_Injured", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Must_Walk", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Must_Run", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Must_Crouch", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Must_Aim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Must_Injured", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Is_In_Spacesuit", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_SetMood:
                    newEntity.AddParameter("mood", new cEnum(EnumType.MOOD, 0), ParameterVariant.PARAMETER); //MOOD
                    newEntity.AddParameter("moodIntensity", new cEnum(EnumType.MOOD_INTENSITY, 0), ParameterVariant.PARAMETER); //MOOD_INTENSITY
                    newEntity.AddParameter("timeOut", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_LocomotionEffect:
                    newEntity.AddParameter("Effect", new cEnum(EnumType.ANIMATION_EFFECT_TYPE, 0), ParameterVariant.PARAMETER); //ANIMATION_EFFECT_TYPE
                    break;
                case FunctionType.CHR_LocomotionDuck:
                    newEntity.AddParameter("Height", new cEnum(EnumType.DUCK_HEIGHT, 0), ParameterVariant.PARAMETER); //DUCK_HEIGHT
                    break;
                case FunctionType.CMD_ShootAt:
                    newEntity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.CMD_AimAtCurrentTarget:
                    newEntity.AddParameter("succeeded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Raise_gun", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CMD_AimAt:
                    newEntity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("AimTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Raise_gun", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_current_target", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Player_Sensor:
                    newEntity.AddParameter("Standard", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Running", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Aiming", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Vent", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Grapple", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Death", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Cover", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Motion_Tracked", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Motion_Tracked_Vent", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Leaning", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CMD_Ragdoll:
                    newEntity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("actor", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    newEntity.AddParameter("impact_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    break;
                case FunctionType.CHR_SetTacticalPosition:
                    newEntity.AddParameter("tactical_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("sweep_type", new cEnum(EnumType.AREA_SWEEP_TYPE, 0), ParameterVariant.PARAMETER); //AREA_SWEEP_TYPE
                    newEntity.AddParameter("fixed_sweep_radius", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_SetFocalPoint:
                    newEntity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("priority", new cEnum(EnumType.PRIORITY, 0), ParameterVariant.PARAMETER); //PRIORITY
                    newEntity.AddParameter("speed", new cEnum(EnumType.LOOK_SPEED, 1), ParameterVariant.PARAMETER); //LOOK_SPEED
                    newEntity.AddParameter("steal_camera", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("line_of_sight_test", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_SetAndroidThrowTarget:
                    newEntity.AddParameter("thrown", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("throw_position", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.CHR_SetAlliance:
                    newEntity.AddParameter("Alliance", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    break;
                case FunctionType.CHR_GetAlliance:
                    newEntity.AddParameter("Alliance", new cEnum(), ParameterVariant.OUTPUT); //Enum
                    break;
                case FunctionType.ALLIANCE_SetDisposition:
                    newEntity.AddParameter("A", new cEnum(EnumType.ALLIANCE_GROUP, 1), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    newEntity.AddParameter("B", new cEnum(EnumType.ALLIANCE_GROUP, 5), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    newEntity.AddParameter("Disposition", new cEnum(EnumType.ALLIANCE_STANCE, 1), ParameterVariant.PARAMETER); //ALLIANCE_STANCE
                    break;
                case FunctionType.CHR_SetInvincibility:
                    newEntity.AddParameter("damage_mode", new cEnum(EnumType.DAMAGE_MODE, 0), ParameterVariant.PARAMETER); //DAMAGE_MODE
                    break;
                case FunctionType.CHR_SetHealth:
                    newEntity.AddParameter("HealthPercentage", new cInteger(100), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("UsePercentageOfCurrentHeath", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_GetHealth:
                    newEntity.AddParameter("Health", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.CHR_SetDebugDisplayName:
                    newEntity.AddParameter("DebugName", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.CHR_TakeDamage:
                    newEntity.AddParameter("Damage", new cInteger(100), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("DamageIsAPercentage", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 0), ParameterVariant.PARAMETER); //AMMO_TYPE
                    break;
                case FunctionType.CHR_SetSubModelVisibility:
                    newEntity.AddParameter("is_visible", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("matching", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.CHR_SetHeadVisibility:
                    newEntity.AddParameter("is_visible", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_SetFacehuggerAggroRadius:
                    newEntity.AddParameter("radius", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_DamageMonitor:
                    newEntity.AddParameter("damaged", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("InstigatorFilter", new cBool(true), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("DamageDone", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("Instigator", new cFloat(), ParameterVariant.OUTPUT); //Object
                    newEntity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, -65536), ParameterVariant.PARAMETER); //DAMAGE_EFFECTS
                    break;
                case FunctionType.CHR_KnockedOutMonitor:
                    newEntity.AddParameter("on_knocked_out", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_recovered", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CHR_DeathMonitor:
                    newEntity.AddParameter("dying", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("killed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("KillerFilter", new cBool(true), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("Killer", new cFloat(), ParameterVariant.OUTPUT); //Object
                    newEntity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, -65536), ParameterVariant.PARAMETER); //DAMAGE_EFFECTS
                    break;
                case FunctionType.CHR_RetreatMonitor:
                    newEntity.AddParameter("reached_retreat", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("started_retreating", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CHR_WeaponFireMonitor:
                    newEntity.AddParameter("fired", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CHR_TorchMonitor:
                    newEntity.AddParameter("torch_on", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("torch_off", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("TorchOn", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_VentMonitor:
                    newEntity.AddParameter("entered_vent", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exited_vent", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("IsInVent", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CharacterTypeMonitor:
                    newEntity.AddParameter("spawned", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("despawned", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("all_despawned", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("AreAny", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 2), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    newEntity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("trigger_on_checkpoint_restart", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Convo:
                    newEntity.AddParameter("everyoneArrived", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("playerJoined", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("playerLeft", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("npcJoined", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("members", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.LOGIC_CHARACTER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //LOGIC_CHARACTER
                    newEntity.AddParameter("speaker", new cFloat(), ParameterVariant.OUTPUT); //Object
                    newEntity.AddParameter("alwaysTalkToPlayerIfPresent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("playerCanJoin", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("playerCanLeave", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("positionNPCs", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("circularShape", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("convoPosition", new cFloat(), ParameterVariant.PARAMETER); //Object
                    newEntity.AddParameter("personalSpaceRadius", new cFloat(0.4f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_NotifyDynamicDialogueEvent:
                    newEntity.AddParameter("DialogueEvent", new cEnum(EnumType.DIALOGUE_NPC_EVENT, -1), ParameterVariant.PARAMETER); //DIALOGUE_NPC_EVENT
                    break;
                case FunctionType.NPC_Squad_DialogueMonitor:
                    newEntity.AddParameter("Suspicious_Item_Initial", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Suspicious_Item_Close", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Suspicious_Warning", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Suspicious_Warning_Fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Missing_Buddy", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Search_Started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Search_Loop", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Search_Complete", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Detected_Enemy", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Alien_Heard_Backstage", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Interrogative", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Warning", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Last_Chance", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Stand_Down", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Attack", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Advance", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Melee", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Hit_By_Weapon", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Go_to_Cover", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("No_Cover", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Shoot_From_Cover", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Cover_Broken", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Retreat", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Panic", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Final_Hit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Ally_Death", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Incoming_IED", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Alert_Squad", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("My_Death", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Idle_Passive", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Idle_Aggressive", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Block", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Enter_Grapple", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Grapple_From_Cover", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Player_Observed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("squad_coordinator", new cFloat(), ParameterVariant.PARAMETER); //Object
                    break;
                case FunctionType.NPC_Group_DeathCounter:
                    newEntity.AddParameter("on_threshold", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("TriggerThreshold", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.NPC_Group_Death_Monitor:
                    newEntity.AddParameter("last_man_dying", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("all_killed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("squad_coordinator", new cFloat(), ParameterVariant.PARAMETER); //Object
                    newEntity.AddParameter("CheckAllNPCs", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SenseLimiter:
                    newEntity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER); //SENSORY_TYPE
                    break;
                case FunctionType.NPC_ResetSensesAndMemory:
                    newEntity.AddParameter("ResetMenaceToFull", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ResetSensesLimiters", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SetupMenaceManager:
                    newEntity.AddParameter("AgressiveMenace", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ProgressionFraction", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ResetMenaceMeter", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_AlienConfig:
                    newEntity.AddParameter("AlienConfigString", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.NPC_SetSenseSet:
                    newEntity.AddParameter("SenseSet", new cEnum(EnumType.SENSE_SET, 0), ParameterVariant.PARAMETER); //SENSE_SET
                    break;
                case FunctionType.NPC_GetLastSensedPositionOfTarget:
                    newEntity.AddParameter("NoRecentSense", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SensedOnLeft", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SensedOnRight", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SensedInFront", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SensedBehind", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OptionalTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("LastSensedPosition", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("MaxTimeSince", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.HeldItem_AINotifier:
                    newEntity.AddParameter("Item", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Duration", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.NPC_Gain_Aggression_In_Radius:
                    newEntity.AddParameter("Position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Radius", new cFloat(5.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("AggressionGain", new cEnum(EnumType.AGGRESSION_GAIN, 1), ParameterVariant.PARAMETER); //AGGRESSION_GAIN
                    break;
                case FunctionType.NPC_Aggression_Monitor:
                    newEntity.AddParameter("on_interrogative", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_warning", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_last_chance", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_stand_down", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_idle", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_aggressive", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.Explosion_AINotifier:
                    newEntity.AddParameter("on_character_damage_fx", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ExplosionPos", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 12), ParameterVariant.PARAMETER); //AMMO_TYPE
                    break;
                case FunctionType.NPC_Sleeping_Android_Monitor:
                    newEntity.AddParameter("Twitch", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SitUp_Start", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SitUp_End", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Sleeping_GetUp", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Sitting_GetUp", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Android_NPC", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.NPC_Highest_Awareness_Monitor:
                    newEntity.AddParameter("All_Dead", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Stunned", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Unaware", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Suspicious", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SearchingArea", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SearchingLastSensed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Aware", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_changed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("NPC_Coordinator", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("CheckAllNPCs", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_Squad_GetAwarenessState:
                    newEntity.AddParameter("All_Dead", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Stunned", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Unaware", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Suspicious", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SearchingArea", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SearchingLastSensed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Aware", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("NPC_Coordinator", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.NPC_Squad_GetAwarenessWatermark:
                    newEntity.AddParameter("All_Dead", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Stunned", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Unaware", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Suspicious", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SearchingArea", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SearchingLastSensed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Aware", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("NPC_Coordinator", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PlayerCameraMonitor:
                    newEntity.AddParameter("AndroidNeckSnap", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("AlienKill", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("AlienKillBroken", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("AlienKillInVent", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("StandardAnimDrivenView", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("StopNonStandardCameras", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.ScreenEffectEventMonitor:
                    newEntity.AddParameter("MeleeHit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("BulletHit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("MedkitHeal", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("StartStrangle", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("StopStrangle", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("StartLowHealth", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("StopLowHealth", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("StartDeath", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("StopDeath", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("AcidHit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("FlashbangHit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("HitAndRun", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("CancelHitAndRun", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.DEBUG_SenseLevels:
                    newEntity.AddParameter("no_activation", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("trace_activation", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("lower_activation", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("normal_activation", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("upper_activation", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER); //SENSORY_TYPE
                    break;
                case FunctionType.NPC_FakeSense:
                    newEntity.AddParameter("SensedObject", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("FakePosition", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Sense", new cEnum(EnumType.SENSORY_TYPE, -1), ParameterVariant.PARAMETER); //SENSORY_TYPE
                    newEntity.AddParameter("ForceThreshold", new cEnum(EnumType.THRESHOLD_QUALIFIER, 2), ParameterVariant.PARAMETER); //THRESHOLD_QUALIFIER
                    break;
                case FunctionType.NPC_SuspiciousItem:
                    newEntity.AddParameter("ItemPosition", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Item", new cEnum(EnumType.SUSPICIOUS_ITEM, 0), ParameterVariant.PARAMETER); //SUSPICIOUS_ITEM
                    newEntity.AddParameter("InitialReactionValidStartDuration", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FurtherReactionValidStartDuration", new cFloat(6.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("RetriggerDelay", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Trigger", new cEnum(EnumType.SUSPICIOUS_ITEM_TRIGGER, 1), ParameterVariant.PARAMETER); //SUSPICIOUS_ITEM_TRIGGER
                    newEntity.AddParameter("ShouldMakeAggressive", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("MaxGroupMembersInteract", new cInteger(2), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SystematicSearchRadius", new cFloat(8.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("AllowSamePriorityToOveride", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("UseSamePriorityCloserDistanceConstraint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("SamePriorityCloserDistanceConstraint", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("UseSamePriorityRecentTimeConstraint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("SamePriorityRecentTimeConstraint", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("BehaviourTreePriority", new cEnum(EnumType.SUSPICIOUS_ITEM_BEHAVIOUR_TREE_PRIORITY, 0), ParameterVariant.PARAMETER); //SUSPICIOUS_ITEM_BEHAVIOUR_TREE_PRIORITY
                    newEntity.AddParameter("InteruptSubPriority", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("DetectableByBackstageAlien", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DoIntialReaction", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("MoveCloseToSuspectPosition", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DoCloseToReaction", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DoCloseToWaitForGroupMembers", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DoSystematicSearch", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("GroupNotify", new cEnum(EnumType.SUSPICIOUS_ITEM_STAGE, 1), ParameterVariant.PARAMETER); //SUSPICIOUS_ITEM_STAGE
                    newEntity.AddParameter("DoIntialReactionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("MoveCloseToSuspectPositionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DoCloseToReactionSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DoCloseToWaitForGroupMembersSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DoSystematicSearchSubsequentGroupMember", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SetAlienDevelopmentStage:
                    newEntity.AddParameter("AlienStage", new cEnum(EnumType.ALIEN_DEVELOPMENT_MANAGER_STAGES, 0), ParameterVariant.PARAMETER); //ALIEN_DEVELOPMENT_MANAGER_STAGES
                    newEntity.AddParameter("Reset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_TargetAcquire:
                    newEntity.AddParameter("no_targets", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.CHR_IsWithinRange:
                    newEntity.AddParameter("In_range", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Out_of_range", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Radius", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Height", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Range_test_shape", new cEnum(EnumType.RANGE_TEST_SHAPE, 0), ParameterVariant.PARAMETER); //RANGE_TEST_SHAPE
                    break;
                case FunctionType.NPC_ForceCombatTarget:
                    newEntity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("LockOtherAttackersOut", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SetAimTarget:
                    newEntity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.CHR_SetTorch:
                    newEntity.AddParameter("TorchOn", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_GetTorch:
                    newEntity.AddParameter("torch_on", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("torch_off", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("TorchOn", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.NPC_SetAutoTorchMode:
                    newEntity.AddParameter("AutoUseTorchInDark", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_GetCombatTarget:
                    newEntity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("target", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.NPC_AreaBox:
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.NPC_MeleeContext:
                    newEntity.AddParameter("ConvergePos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Radius", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Context_Type", new cEnum(EnumType.MELEE_CONTEXT_TYPE, 0), ParameterVariant.PARAMETER); //MELEE_CONTEXT_TYPE
                    break;
                case FunctionType.NPC_SetSafePoint:
                    newEntity.AddParameter("SafePositions", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.Player_ExploitableArea:
                    newEntity.AddParameter("NpcSafePositions", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.NPC_SetDefendArea:
                    newEntity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //NPC_AREA_RESOURCE
                    break;
                case FunctionType.NPC_SetPursuitArea:
                    newEntity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //NPC_AREA_RESOURCE
                    break;
                case FunctionType.NPC_ForceRetreat:
                    newEntity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //NPC_AREA_RESOURCE
                    break;
                case FunctionType.NPC_DefineBackstageAvoidanceArea:
                    newEntity.AddParameter("AreaObjects", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NPC_AREA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //NPC_AREA_RESOURCE
                    break;
                case FunctionType.NPC_SetAlertness:
                    newEntity.AddParameter("AlertState", new cEnum(EnumType.ALERTNESS_STATE, 0), ParameterVariant.PARAMETER); //ALERTNESS_STATE
                    break;
                case FunctionType.NPC_SetStartPos:
                    newEntity.AddParameter("StartPos", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.NPC_SetAgressionProgression:
                    newEntity.AddParameter("allow_progression", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SetLocomotionTargetSpeed:
                    newEntity.AddParameter("Speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 1), ParameterVariant.PARAMETER); //LOCOMOTION_TARGET_SPEED
                    break;
                case FunctionType.NPC_SetGunAimMode:
                    newEntity.AddParameter("AimingMode", new cEnum(EnumType.NPC_GUN_AIM_MODE, 0), ParameterVariant.PARAMETER); //NPC_GUN_AIM_MODE
                    break;
                case FunctionType.NPC_set_behaviour_tree_flags:
                    newEntity.AddParameter("BehaviourTreeFlag", new cEnum(EnumType.BEHAVIOUR_TREE_FLAGS, 2), ParameterVariant.PARAMETER); //BEHAVIOUR_TREE_FLAGS
                    newEntity.AddParameter("FlagSetting", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_SetHidingSearchRadius:
                    newEntity.AddParameter("Radius", new cFloat(), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_SetHidingNearestLocation:
                    newEntity.AddParameter("hiding_pos", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.NPC_WithdrawAlien:
                    newEntity.AddParameter("allow_any_searches_to_complete", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("permanent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("killtraps", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("initial_radius", new cFloat(15.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("timed_out_radius", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("time_to_force", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_behaviour_monitor:
                    newEntity.AddParameter("state_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("state_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("behaviour", new cEnum(EnumType.BEHAVIOR_TREE_BRANCH_TYPE, 0), ParameterVariant.PARAMETER); //BEHAVIOR_TREE_BRANCH_TYPE
                    newEntity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_multi_behaviour_monitor:
                    newEntity.AddParameter("Cinematic_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Cinematic_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Damage_Response_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Damage_Response_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Target_Is_NPC_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Target_Is_NPC_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Breakout_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Breakout_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Attack_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Attack_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Stunned_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Stunned_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Backstage_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Backstage_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("In_Vent_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("In_Vent_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Killtrap_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Killtrap_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Threat_Aware_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Threat_Aware_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Suspect_Target_Response_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Suspect_Target_Response_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Player_Hiding_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Player_Hiding_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Suspicious_Item_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Suspicious_Item_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Search_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Search_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Area_Sweep_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Area_Sweep_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_ambush_monitor:
                    newEntity.AddParameter("setup", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("abandoned", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("trap_sprung", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ambush_type", new cEnum(EnumType.AMBUSH_TYPE, 0), ParameterVariant.PARAMETER); //AMBUSH_TYPE
                    newEntity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_navmesh_type_monitor:
                    newEntity.AddParameter("state_set", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("state_unset", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("nav_mesh_type", new cEnum(EnumType.NAV_MESH_AREA_TYPE, 0), ParameterVariant.PARAMETER); //NAV_MESH_AREA_TYPE
                    newEntity.AddParameter("trigger_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("trigger_on_checkpoint_restart", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CHR_HasWeaponOfType:
                    newEntity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER); //WEAPON_TYPE
                    newEntity.AddParameter("check_if_weapon_draw", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_TriggerAimRequest:
                    newEntity.AddParameter("started_aiming", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished_aiming", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("interrupted", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("AimTarget", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Raise_gun", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_current_target", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("clamp_angle", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("clear_current_requests", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_TriggerShootRequest:
                    newEntity.AddParameter("started_shooting", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished_shooting", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("interrupted", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("empty_current_clip", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("shot_count", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("duration", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("clear_current_requests", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Squad_SetMaxEscalationLevel:
                    newEntity.AddParameter("max_level", new cEnum(EnumType.NPC_AGGRO_LEVEL, 5), ParameterVariant.PARAMETER); //NPC_AGGRO_LEVEL
                    newEntity.AddParameter("squad_coordinator", new cFloat(), ParameterVariant.PARAMETER); //Object
                    break;
                case FunctionType.Chr_PlayerCrouch:
                    newEntity.AddParameter("crouch", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NPC_Once:
                    newEntity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.Custom_Hiding_Vignette_controller:
                    newEntity.AddParameter("StartFade", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("StopFade", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Breath", new cInteger(0), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("Blackout_start_time", new cInteger(15), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("run_out_time", new cInteger(60), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("Vignette", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("FadeValue", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.Custom_Hiding_Controller:
                    newEntity.AddParameter("Started_Idle", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Started_Exit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Got_Out", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Prompt_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Prompt_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Start_choking", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Start_oxygen_starvation", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Show_MT", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Hide_MT", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Spawn_MT", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Despawn_MT", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Start_Busted_By_Alien", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Start_Busted_By_Android", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("End_Busted_By_Android", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Start_Busted_By_Human", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("End_Busted_By_Human", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Enter_Anim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("Idle_Anim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("Exit_Anim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("has_MT", new cBool(), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("is_high", new cBool(), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("AlienBusted_Player_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AlienBusted_Alien_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AlienBusted_Player_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AlienBusted_Alien_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AlienBusted_Player_3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AlienBusted_Alien_3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AlienBusted_Player_4", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AlienBusted_Alien_4", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AndroidBusted_Player_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AndroidBusted_Android_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AndroidBusted_Player_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("AndroidBusted_Android_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("MT_pos", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.TorchDynamicMovement:
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("torch", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("max_spatial_velocity", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_angular_velocity", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_position_displacement", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_target_displacement", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("position_damping", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("target_damping", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.EQUIPPABLE_ITEM:
                    newEntity.AddParameter("finished_spawning", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("equipped", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("unequipped", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pickup", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_discard", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_melee_impact", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_used_basic_function", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("spawn_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("item_animated_asset", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("owner", new cFloat(), ParameterVariant.OUTPUT); //Object
                    newEntity.AddParameter("has_owner", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("character_animation_context", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("character_activate_animation_context", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("left_handed", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("inventory_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("equipment_slot", new cEnum(EnumType.EQUIPMENT_SLOT, 0), ParameterVariant.PARAMETER); //EQUIPMENT_SLOT
                    newEntity.AddParameter("holsters_on_owner", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("holster_node", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("holster_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("weapon_handedness", new cEnum(EnumType.WEAPON_HANDEDNESS, 0), ParameterVariant.PARAMETER); //WEAPON_HANDEDNESS
                    break;
                case FunctionType.AIMED_ITEM:
                    newEntity.AddParameter("on_started_aiming", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_stopped_aiming", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_display_on", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_display_off", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_effect_on", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_effect_off", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("target_position", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("average_target_distance", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("min_target_distance", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("fixed_target_distance_for_local_player", new cFloat(6.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.MELEE_WEAPON:
                    newEntity.AddParameter("item_animated_model_and_collision", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("normal_attack_damage", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("power_attack_damage", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("position_input", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.AIMED_WEAPON:
                    newEntity.AddParameter("on_fired_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_fired_fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_fired_fail_single", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_impact", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_reload_started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_reload_another", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_reload_empty_clip", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_reload_canceled", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_reload_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_reload_fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_shooting_started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_shooting_wind_down", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_shooting_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_overheated", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_cooled_down", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_charge_complete", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_charge_started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_charge_stopped", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_turned_on", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_turned_off", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_torch_on_requested", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_torch_off_requested", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ammoRemainingInClip", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("ammoToFillClip", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("ammoThatWasInClip", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("charge_percentage", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("charge_noise_percentage", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 2), ParameterVariant.PARAMETER); //WEAPON_TYPE
                    newEntity.AddParameter("requires_turning_on", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ejectsShellsOnFiring", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("aim_assist_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("default_ammo_type", new cEnum(EnumType.AMMO_TYPE, 0), ParameterVariant.PARAMETER); //AMMO_TYPE
                    newEntity.AddParameter("starting_ammo", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("clip_size", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("consume_ammo_over_time_when_turned_on", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_auto_shots_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_manual_shots_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("wind_down_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("maximum_continous_fire_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("overheat_recharge_time_in_seconds", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("automatic_firing", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("overheats", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("charged_firing", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("charging_duration", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("min_charge_to_fire", new cFloat(0.3f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("overcharge_timer", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("charge_noise_start_time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("reloadIndividualAmmo", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("alwaysDoFullReloadOfClips", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("movement_accuracy_penalty_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("aim_rotation_accuracy_penalty_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("accuracy_penalty_per_shot", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("accuracy_accumulated_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("player_exposed_accuracy_penalty_per_shot", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("player_exposed_accuracy_accumulated_per_second", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("recoils_on_fire", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("alien_threat_aware", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayerWeaponMonitor:
                    newEntity.AddParameter("on_clip_above_percentage", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_clip_below_percentage", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_clip_empty", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_clip_full", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 2), ParameterVariant.PARAMETER); //WEAPON_TYPE
                    newEntity.AddParameter("ammo_percentage_in_clip", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.PlayerDiscardsWeapons:
                    newEntity.AddParameter("discard_pistol", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_shotgun", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_flamethrower", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_boltgun", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_cattleprod", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_melee", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayerDiscardsItems:
                    newEntity.AddParameter("discard_ieds", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_medikits", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_ammo", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_flares_and_lights", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_materials", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_batteries", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayerDiscardsTools:
                    newEntity.AddParameter("discard_motion_tracker", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_cutting_torch", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_hacking_tool", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("discard_keycard", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.WEAPON_GiveToCharacter:
                    newEntity.AddParameter("Character", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Weapon", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("is_holstered", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.WEAPON_GiveToPlayer:
                    newEntity.AddParameter("weapon", new cEnum(EnumType.EQUIPMENT_SLOT, 1), ParameterVariant.PARAMETER); //EQUIPMENT_SLOT
                    newEntity.AddParameter("holster", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("starting_ammo", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.WEAPON_ImpactEffect:
                    newEntity.AddParameter("StaticEffects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("DynamicEffects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("DynamicAttachedEffects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Type", new cEnum(EnumType.WEAPON_IMPACT_EFFECT_TYPE, 0), ParameterVariant.PARAMETER); //WEAPON_IMPACT_EFFECT_TYPE
                    newEntity.AddParameter("Orientation", new cEnum(EnumType.WEAPON_IMPACT_EFFECT_ORIENTATION, 0), ParameterVariant.PARAMETER); //WEAPON_IMPACT_EFFECT_ORIENTATION
                    newEntity.AddParameter("Priority", new cInteger(16), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("SafeDistant", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("LifeTime", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("character_damage_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("RandomRotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.WEAPON_ImpactFilter:
                    newEntity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("PhysicMaterial", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.WEAPON_AttackerFilter:
                    newEntity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("filter", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.WEAPON_TargetObjectFilter:
                    newEntity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("filter", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.WEAPON_ImpactInspector:
                    newEntity.AddParameter("damage", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("impact_position", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("impact_target", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.WEAPON_DamageFilter:
                    newEntity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("damage_threshold", new cInteger(100), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.WEAPON_DidHitSomethingFilter:
                    newEntity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.WEAPON_MultiFilter:
                    newEntity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("AttackerFilter", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("TargetFilter", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("DamageThreshold", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("DamageType", new cEnum(EnumType.DAMAGE_EFFECTS, 33554432), ParameterVariant.PARAMETER); //DAMAGE_EFFECTS
                    newEntity.AddParameter("UseAmmoFilter", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("AmmoType", new cEnum(EnumType.AMMO_TYPE, 22), ParameterVariant.PARAMETER); //AMMO_TYPE
                    break;
                case FunctionType.WEAPON_ImpactCharacterFilter:
                    newEntity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    newEntity.AddParameter("character_body_location", new cEnum(EnumType.IMPACT_CHARACTER_BODY_LOCATION_TYPE, 0), ParameterVariant.PARAMETER); //IMPACT_CHARACTER_BODY_LOCATION_TYPE
                    break;
                case FunctionType.WEAPON_Effect:
                    newEntity.AddParameter("WorldPos", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("AttachedEffects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("UnattachedEffects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("LifeTime", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.WEAPON_AmmoTypeFilter:
                    newEntity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("AmmoType", new cEnum(EnumType.DAMAGE_EFFECTS, 33554432), ParameterVariant.PARAMETER); //DAMAGE_EFFECTS
                    break;
                case FunctionType.WEAPON_ImpactAngleFilter:
                    newEntity.AddParameter("greater", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("less", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ReferenceAngle", new cFloat(60.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.WEAPON_ImpactOrientationFilter:
                    newEntity.AddParameter("passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ThresholdAngle", new cFloat(15.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Orientation", new cEnum(EnumType.WEAPON_IMPACT_FILTER_ORIENTATION, 2), ParameterVariant.PARAMETER); //WEAPON_IMPACT_FILTER_ORIENTATION
                    break;
                case FunctionType.EFFECT_ImpactGenerator:
                    newEntity.AddParameter("on_impact", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_failed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("trigger_on_reset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("min_distance", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("distance", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_count", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("spread", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("skip_characters", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.EFFECT_EntityGenerator:
                    newEntity.AddParameter("entities", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("trigger_on_reset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("spread", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("force_min", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("force_max", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("force_offset_XY_min", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("force_offset_XY_max", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("force_offset_Z_min", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("force_offset_Z_max", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("lifetime_min", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("lifetime_max", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("use_local_rotation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.EFFECT_DirectionalPhysics:
                    newEntity.AddParameter("relative_direction", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("effect_distance", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("angular_falloff", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("min_force", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_force", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.PlatformConstantBool:
                    newEntity.AddParameter("NextGen", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("X360", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("PS3", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlatformConstantInt:
                    newEntity.AddParameter("NextGen", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("X360", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("PS3", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.PlatformConstantFloat:
                    newEntity.AddParameter("NextGen", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("X360", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PS3", new cFloat(), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.VariableBool:
                    newEntity.AddParameter("initial_value", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VariableInt:
                    newEntity.AddParameter("initial_value", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VariableFloat:
                    newEntity.AddParameter("initial_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VariableString:
                    newEntity.AddParameter("initial_value", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VariableVector:
                    newEntity.AddParameter("initial_x", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("initial_y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("initial_z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.VariableVector2:
                    newEntity.AddParameter("initial_value", new cVector3(), ParameterVariant.INPUT); //Direction
                    break;
                case FunctionType.VariableColour:
                    newEntity.AddParameter("initial_colour", new cVector3(), ParameterVariant.INPUT); //Direction
                    break;
                case FunctionType.VariableFlashScreenColour:
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("initial_colour", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("flash_layer_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.VariableHackingConfig:
                    newEntity.AddParameter("nodes", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("sensors", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("victory_nodes", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("victory_sensors", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.VariableEnum:
                    newEntity.AddParameter("initial_value", new cEnum(), ParameterVariant.PARAMETER); //Enum
                    newEntity.AddParameter("is_persistent", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VariableObject:
                    newEntity.AddParameter("initial", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.VariableAnimationInfo:
                    newEntity.AddParameter("AnimationSet", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("Animation", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.ExternalVariableBool:
                    newEntity.AddParameter("game_variable", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.GAME_VARIABLE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //GAME_VARIABLE
                    break;
                case FunctionType.NonPersistentBool:
                    newEntity.AddParameter("initial_value", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NonPersistentInt:
                    newEntity.AddParameter("initial_value", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("is_persistent", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GameDVR:
                    newEntity.AddParameter("start_time", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("duration", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("moment_ID", new cEnum(EnumType.GAME_CLIP, 0), ParameterVariant.PARAMETER); //GAME_CLIP
                    break;
                case FunctionType.Zone:
                    newEntity.AddParameter("composites", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("suspend_on_unload", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("space_visible", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ZoneLink:
                    newEntity.AddParameter("ZoneA", new cFloat(), ParameterVariant.INPUT); //ZonePtr
                    newEntity.AddParameter("ZoneB", new cFloat(), ParameterVariant.INPUT); //ZonePtr
                    newEntity.AddParameter("cost", new cInteger(6), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.ZoneExclusionLink:
                    newEntity.AddParameter("ZoneA", new cFloat(), ParameterVariant.INPUT); //ZonePtr
                    newEntity.AddParameter("ZoneB", new cFloat(), ParameterVariant.INPUT); //ZonePtr
                    newEntity.AddParameter("exclude_streaming", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ZoneLoaded:
                    newEntity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_unloaded", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.FlushZoneCache:
                    newEntity.AddParameter("CurrentGen", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("NextGen", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.StateQuery:
                    newEntity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Input", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.BooleanLogicInterface:
                    newEntity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("LHS", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("RHS", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.LogicOnce:
                    newEntity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.LogicDelay:
                    newEntity.AddParameter("on_delay_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("delay", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("can_suspend", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.LogicSwitch:
                    newEntity.AddParameter("true_now_false", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("false_now_true", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_restored_true", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_restored_false", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("initial_value", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.LogicGate:
                    newEntity.AddParameter("on_allowed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_disallowed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("allow", new cBool(), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.BooleanLogicOperation:
                    newEntity.AddParameter("Input", new cBool(), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.FloatMath_All:
                    newEntity.AddParameter("Numbers", new cFloat(), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatMultiply_All:
                    newEntity.AddParameter("Invert", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FloatMath:
                    newEntity.AddParameter("LHS", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("RHS", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatMultiplyClamp:
                    newEntity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.FloatClampMultiply:
                    newEntity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.FloatOperation:
                    newEntity.AddParameter("Input", new cFloat(), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatCompare:
                    newEntity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("LHS", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("RHS", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Threshold", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.FloatModulate:
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("wave_shape", new cEnum(EnumType.WAVE_SHAPE, 0), ParameterVariant.PARAMETER); //WAVE_SHAPE
                    newEntity.AddParameter("frequency", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("phase", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("amplitude", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("bias", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FloatModulateRandom:
                    newEntity.AddParameter("on_full_switched_on", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_full_switched_off", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("switch_on_anim", new cEnum(EnumType.LIGHT_TRANSITION, 1), ParameterVariant.PARAMETER); //LIGHT_TRANSITION
                    newEntity.AddParameter("switch_on_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("switch_on_custom_frequency", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("switch_on_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("switch_off_anim", new cEnum(EnumType.LIGHT_TRANSITION, 1), ParameterVariant.PARAMETER); //LIGHT_TRANSITION
                    newEntity.AddParameter("switch_off_custom_frequency", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("switch_off_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("behaviour_anim", new cEnum(EnumType.LIGHT_ANIM, 1), ParameterVariant.PARAMETER); //LIGHT_ANIM
                    newEntity.AddParameter("behaviour_frequency", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("behaviour_frequency_variance", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("behaviour_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("pulse_modulation", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("oscillate_range_min", new cFloat(0.75f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("sparking_speed", new cFloat(0.9f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("blink_rate", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("blink_range_min", new cFloat(0.01f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("flicker_rate", new cFloat(0.75f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("flicker_off_rate", new cFloat(0.15f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("flicker_range_min", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("flicker_off_range_min", new cFloat(0.01f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("disable_behaviour", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FloatLinearProportion:
                    newEntity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Proportion", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatGetLinearProportion:
                    newEntity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Input", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Proportion", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatLinearInterpolateTimed:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FloatLinearInterpolateSpeed:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Target_Value", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FloatLinearInterpolateSpeedAdvanced:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("trigger_on_min", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("trigger_on_max", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("trigger_on_loop", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("Initial_Value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Min_Value", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Max_Value", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FloatSmoothStep:
                    newEntity.AddParameter("Low_Edge", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("High_Edge", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Value", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FloatClamp:
                    newEntity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Value", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.FilterAbsorber:
                    newEntity.AddParameter("output", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("factor", new cFloat(0.95f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("start_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("input", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.IntegerMath_All:
                    newEntity.AddParameter("Numbers", new cInteger(), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("Result", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.IntegerMath:
                    newEntity.AddParameter("LHS", new cInteger(0), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("RHS", new cInteger(0), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("Result", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.IntegerOperation:
                    newEntity.AddParameter("Input", new cInteger(), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("Result", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.IntegerCompare:
                    newEntity.AddParameter("on_true", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_false", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("LHS", new cInteger(0), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("RHS", new cInteger(0), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.IntegerAnalyse:
                    newEntity.AddParameter("Input", new cInteger(0), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("Result", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("Val0", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Val1", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Val2", new cInteger(2), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Val3", new cInteger(3), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Val4", new cInteger(4), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Val5", new cInteger(5), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Val6", new cInteger(6), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Val7", new cInteger(7), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Val8", new cInteger(8), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Val9", new cInteger(9), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.SetEnum:
                    newEntity.AddParameter("Output", new cEnum(), ParameterVariant.OUTPUT); //Enum
                    newEntity.AddParameter("initial_value", new cEnum(), ParameterVariant.PARAMETER); //Enum
                    break;
                case FunctionType.SetString:
                    newEntity.AddParameter("Output", new cString(""), ParameterVariant.OUTPUT); //String
                    newEntity.AddParameter("initial_value", new cString(""), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.VectorMath:
                    newEntity.AddParameter("LHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("RHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorScale:
                    newEntity.AddParameter("LHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("RHS", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorNormalise:
                    newEntity.AddParameter("Input", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorModulus:
                    newEntity.AddParameter("Input", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.ScalarProduct:
                    newEntity.AddParameter("LHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("RHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.VectorDirection:
                    newEntity.AddParameter("From", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("To", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.VectorYaw:
                    newEntity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.VectorRotateYaw:
                    newEntity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Yaw", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorRotateRoll:
                    newEntity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Roll", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorRotatePitch:
                    newEntity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Pitch", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorRotateByPos:
                    newEntity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("WorldPos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorMultiplyByPos:
                    newEntity.AddParameter("Vector", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("WorldPos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.VectorDistance:
                    newEntity.AddParameter("LHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("RHS", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.VectorReflect:
                    newEntity.AddParameter("Input", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Normal", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.SetVector:
                    newEntity.AddParameter("x", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("y", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("z", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.SetVector2:
                    newEntity.AddParameter("Input", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.SetColour:
                    newEntity.AddParameter("Colour", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.GetTranslation:
                    newEntity.AddParameter("Input", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.GetRotation:
                    newEntity.AddParameter("Input", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.GetComponentInterface:
                    newEntity.AddParameter("Input", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.SetPosition:
                    newEntity.AddParameter("Translation", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Rotation", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Input", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("set_on_reset", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PositionDistance:
                    newEntity.AddParameter("LHS", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("RHS", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.VectorLinearProportion:
                    newEntity.AddParameter("Initial_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Target_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Proportion", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.VectorLinearInterpolateTimed:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Initial_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Target_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Reverse", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    newEntity.AddParameter("Time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.VectorLinearInterpolateSpeed:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Initial_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Target_Value", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Reverse", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    newEntity.AddParameter("Speed", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("PingPong", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.MoveInTime:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("end_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SmoothMove:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("timer", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("start_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("end_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("start_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("end_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.RotateInTime:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_pos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("origin", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("timer", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("time_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("time_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("time_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.RotateAtSpeed:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_pos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("origin", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("timer", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("speed_X", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("speed_Y", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("speed_Z", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PointAt:
                    newEntity.AddParameter("origin", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("target", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.SetLocationAndOrientation:
                    newEntity.AddParameter("location", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("axis", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("local_offset", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("axis_is", new cEnum(EnumType.ORIENTATION_AXIS, 2), ParameterVariant.PARAMETER); //ORIENTATION_AXIS
                    break;
                case FunctionType.ApplyRelativeTransform:
                    newEntity.AddParameter("origin", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("input", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("output", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("use_trigger_entity", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.RandomFloat:
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Max", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.RandomInt:
                    newEntity.AddParameter("Result", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("Min", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("Max", new cInteger(100), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.RandomBool:
                    newEntity.AddParameter("Result", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.RandomVector:
                    newEntity.AddParameter("Result", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    newEntity.AddParameter("MinX", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MaxX", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MinY", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MaxY", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MinZ", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MaxZ", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Normalised", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.RandomSelect:
                    newEntity.AddParameter("Input", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //Object
                    newEntity.AddParameter("Seed", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerRandom:
                    newEntity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_11", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_12", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Num", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.TriggerRandomSequence:
                    newEntity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("All_triggered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("current", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.Persistent_TriggerRandomSequence:
                    newEntity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("All_triggered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("current", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.TriggerWeightedRandom:
                    newEntity.AddParameter("Random_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Random_10", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("current", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("Weighting_01", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Weighting_02", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Weighting_03", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Weighting_04", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Weighting_05", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Weighting_06", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Weighting_07", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Weighting_08", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Weighting_09", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Weighting_10", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("allow_same_pin_in_succession", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayEnvironmentAnimation:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_finished_streaming", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("play_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("jump_to_the_end_on_play", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("marker", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("external_start_time", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("external_time", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("animation_length", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("animation_info", new cFloat(), ParameterVariant.PARAMETER); //AnimationInfoPtr
                    newEntity.AddParameter("AnimationSet", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("Animation", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("start_frame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("end_frame", new cInteger(-1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("play_speed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("loop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("shot_number", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.CAGEAnimation:
                    //newEntity = new CAGEAnimation(thisID);
                    newEntity.AddParameter("animation_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("animation_interrupted", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("animation_changed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("cinematic_loaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("cinematic_unloaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("external_time", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("current_time", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("use_external_time", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("rewind_on_stop", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("jump_to_the_end", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("playspeed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("anim_length", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_cinematic_skippable", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("skippable_timer", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("capture_video", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("capture_clip_name", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("playback", new cFloat(0.0f), ParameterVariant.INTERNAL); //float
                    break;
                case FunctionType.MultitrackLoop:
                    newEntity.AddParameter("current_time", new cFloat(), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("loop_condition", new cBool(), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("start_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("end_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ReTransformer:
                    newEntity.AddParameter("new_transform", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.TriggerSequence:
                    //newEntity = new TriggerSequence(thisID);
                    newEntity.AddParameter("proxy_enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("attach_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("duration", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("trigger_mode", new cEnum(EnumType.ANIM_MODE, 0), ParameterVariant.PARAMETER); //ANIM_MODE
                    newEntity.AddParameter("random_seed", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("use_random_intervals", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("no_duplicates", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("interval_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.Checkpoint:
                    newEntity.AddParameter("on_checkpoint", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_captured", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_saved", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished_saving", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished_loading", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("cancelled_saving", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished_saving_to_hdd", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("player_spawn_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("is_first_checkpoint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_first_autorun_checkpoint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("section", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("checkpoint_type", new cEnum(EnumType.CHECKPOINT_TYPE, 0), ParameterVariant.PARAMETER); //CHECKPOINT_TYPE
                    break;
                case FunctionType.MissionNumber:
                    newEntity.AddParameter("on_changed", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.SetAsActiveMissionLevel:
                    newEntity.AddParameter("clear_level", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.CheckpointRestoredNotify:
                    newEntity.AddParameter("restored", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.DebugLoadCheckpoint:
                    newEntity.AddParameter("previous_checkpoint", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GameStateChanged:
                    newEntity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.DisplayMessage:
                    newEntity.AddParameter("title_id", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("message_id", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.DisplayMessageWithCallbacks:
                    newEntity.AddParameter("on_yes", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_no", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_cancel", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("title_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("message_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("yes_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("no_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("cancel_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("yes_button", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("no_button", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("cancel_button", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.LevelInfo:
                    newEntity.AddParameter("save_level_name_id", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.DebugCheckpoint:
                    newEntity.AddParameter("on_checkpoint", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("section", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("level_reset", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Benchmark:
                    newEntity.AddParameter("benchmark_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("save_stats", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.EndGame:
                    newEntity.AddParameter("on_game_end_started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_game_ended", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("success", new cBool(), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.LeaveGame:
                    newEntity.AddParameter("disconnect_from_session", new cBool(), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.DebugTextStacking:
                    newEntity.AddParameter("float_input", new cFloat(), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("int_input", new cInteger(), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("bool_input", new cBool(), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("vector_input", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("enum_input", new cEnum(), ParameterVariant.INPUT); //Enum
                    newEntity.AddParameter("text", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("size", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("ci_type", new cEnum(EnumType.CI_MESSAGE_TYPE, 0), ParameterVariant.PARAMETER); //CI_MESSAGE_TYPE
                    newEntity.AddParameter("needs_debug_opt_to_render", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DebugText:
                    newEntity.AddParameter("duration_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("float_input", new cFloat(), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("int_input", new cInteger(), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("bool_input", new cBool(), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("vector_input", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("enum_input", new cEnum(), ParameterVariant.INPUT); //Enum
                    newEntity.AddParameter("text_input", new cString(""), ParameterVariant.INPUT); //String
                    newEntity.AddParameter("text", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("size", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("alignment", new cEnum(EnumType.TEXT_ALIGNMENT, 4), ParameterVariant.PARAMETER); //TEXT_ALIGNMENT
                    newEntity.AddParameter("duration", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("pause_game", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("cancel_pause_with_button_press", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("priority", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ci_type", new cEnum(EnumType.CI_MESSAGE_TYPE, 0), ParameterVariant.PARAMETER); //CI_MESSAGE_TYPE
                    break;
                case FunctionType.TutorialMessage:
                    newEntity.AddParameter("text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("text_list", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TUTORIAL_ENTRY_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //TUTORIAL_ENTRY_ID
                    newEntity.AddParameter("show_animation", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DebugEnvironmentMarker:
                    newEntity.AddParameter("target", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("float_input", new cFloat(), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("int_input", new cInteger(), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("bool_input", new cBool(), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("vector_input", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("enum_input", new cEnum(), ParameterVariant.INPUT); //Enum
                    newEntity.AddParameter("text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("size", new cFloat(20.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("duration", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("scale_with_distance", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("max_string_length", new cInteger(10), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("scroll_speed", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("show_distance_from_target", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DebugPositionMarker:
                    newEntity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.DebugCaptureScreenShot:
                    newEntity.AddParameter("finished_capturing", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("wait_for_streamer", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("capture_filename", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("fov", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("near", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("far", new cFloat(), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.DebugCaptureCorpse:
                    newEntity.AddParameter("finished_capturing", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("corpse_name", new cString(""), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.DebugMenuToggle:
                    newEntity.AddParameter("debug_variable", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("value", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PlayerTorch:
                    newEntity.AddParameter("requested_torch_holster", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("requested_torch_draw", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("power_in_current_battery", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("battery_count", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.Master:
                    newEntity.AddParameter("suspend_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("disable_display", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("disable_collision", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("disable_simulation", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ExclusiveMaster:
                    newEntity.AddParameter("active_objects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("inactive_objects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddResource(ResourceType.EXCLUSIVE_MASTER_STATE_RESOURCE);
                    break;
                case FunctionType.ThinkOnce:
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("use_random_start", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("random_start_delay", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.Thinker:
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("delay_between_triggers", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("is_continuous", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_random_start", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("random_start_delay", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("total_thinking_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.AllPlayersReady:
                    newEntity.AddParameter("on_all_players_ready", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("pause_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("activation_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SyncOnAllPlayers:
                    newEntity.AddParameter("on_synchronized", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_synchronized_host", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.SyncOnFirstPlayer:
                    newEntity.AddParameter("on_synchronized", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_synchronized_host", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_synchronized_local", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.NetPlayerCounter:
                    newEntity.AddParameter("on_full", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_empty", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_intermediate", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("is_full", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("is_empty", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("contains_local_player", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.BroadcastTrigger:
                    newEntity.AddParameter("on_triggered", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.HostOnlyTrigger:
                    newEntity.AddParameter("on_triggered", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.SpawnGroup:
                    newEntity.AddParameter("on_spawn_request", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("default_group", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("trigger_on_reset", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.RespawnExcluder:
                    newEntity.AddParameter("excluded_points", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.RespawnConfig:
                    newEntity.AddParameter("min_dist", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("preferred_dist", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_dist", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("respawn_mode", new cEnum(EnumType.RESPAWN_MODE, 0), ParameterVariant.PARAMETER); //RESPAWN_MODE
                    newEntity.AddParameter("respawn_wait_time", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("uncollidable_time", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("is_default", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.NumConnectedPlayers:
                    newEntity.AddParameter("on_count_changed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("count", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.NumPlayersOnStart:
                    newEntity.AddParameter("count", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.NetworkedTimer:
                    newEntity.AddParameter("on_second_changed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_started_counting", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_finished_counting", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("time_elapsed", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("time_left", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("time_elapsed_sec", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("time_left_sec", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("duration", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.DebugObjectMarker:
                    newEntity.AddParameter("marked_object", new cFloat(), ParameterVariant.PARAMETER); //Object
                    newEntity.AddParameter("marked_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.EggSpawner:
                    newEntity.AddParameter("egg_position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("hostile_egg", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.RandomObjectSelector:
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("chosen_object", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.CompoundVolume:
                    newEntity.AddParameter("event", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.TriggerVolumeFilter:
                    newEntity.AddParameter("on_event_entered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_event_exited", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.TriggerVolumeFilter_Monitored:
                    newEntity.AddParameter("on_event_entered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_event_exited", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.TriggerFilter:
                    newEntity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.TriggerObjectsFilter:
                    newEntity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.BindObjectsMultiplexer:
                    newEntity.AddParameter("Pin1_Bound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin2_Bound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin3_Bound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin4_Bound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin5_Bound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin6_Bound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin7_Bound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin8_Bound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin9_Bound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin10_Bound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.TriggerObjectsFilterCounter:
                    newEntity.AddParameter("none_passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("some_passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("all_passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.TriggerContainerObjectsFilterCounter:
                    newEntity.AddParameter("none_passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("some_passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("all_passed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("container", new cFloat(), ParameterVariant.PARAMETER); //Object
                    break;
                case FunctionType.TriggerTouch:
                    newEntity.AddParameter("touch_event", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("physics_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.COLLISION_MAPPING) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //COLLISION_MAPPING
                    newEntity.AddParameter("impact_normal", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.TriggerDamaged:
                    newEntity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("physics_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.COLLISION_MAPPING) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //COLLISION_MAPPING
                    newEntity.AddParameter("impact_normal", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    newEntity.AddParameter("threshold", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerBindCharacter:
                    newEntity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("characters", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.TriggerBindAllCharactersOfType:
                    newEntity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 2), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TriggerBindCharactersInSquad:
                    newEntity.AddParameter("bound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.TriggerUnbindCharacter:
                    newEntity.AddParameter("unbound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.TriggerExtractBoundObject:
                    newEntity.AddParameter("unbound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("bound_object", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.TriggerExtractBoundCharacter:
                    newEntity.AddParameter("unbound_trigger", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("bound_character", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.TriggerDelay:
                    newEntity.AddParameter("delayed_trigger", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("purged_trigger", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("time_left", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("Hrs", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Min", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Sec", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerSwitch:
                    newEntity.AddParameter("Pin_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_10", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("current", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("loop", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.TriggerSelect:
                    newEntity.AddParameter("Pin_0", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_10", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_11", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_12", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_13", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_14", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_15", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_16", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Object_0", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_1", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_2", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_3", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_4", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_5", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_6", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_7", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_8", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_9", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_10", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_11", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_12", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_13", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_14", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_15", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_16", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //Object
                    newEntity.AddParameter("index", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.TriggerSelect_Direct:
                    newEntity.AddParameter("Changed_to_0", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_10", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_11", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_12", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_13", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_14", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_15", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Changed_to_16", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Object_0", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_1", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_2", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_3", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_4", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_5", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_6", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_7", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_8", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_9", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_10", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_11", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_12", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_13", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_14", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_15", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Object_16", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //Object
                    newEntity.AddParameter("TriggeredIndex", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("Changes_only", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.TriggerCheckDifficulty:
                    newEntity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("DifficultyLevel", new cEnum(EnumType.DIFFICULTY_SETTING_TYPE, 2), ParameterVariant.PARAMETER); //DIFFICULTY_SETTING_TYPE
                    break;
                case FunctionType.TriggerSync:
                    newEntity.AddParameter("Pin1_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin2_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin3_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin4_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin5_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin6_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin7_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin8_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin9_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin10_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("reset_on_trigger", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.LogicAll:
                    newEntity.AddParameter("Pin1_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin2_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin3_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin4_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin5_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin6_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin7_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin8_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin9_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin10_Synced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("num", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("reset_on_trigger", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Logic_MultiGate:
                    newEntity.AddParameter("Underflow", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_10", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_11", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_12", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_13", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_14", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_15", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_16", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_17", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_18", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_19", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pin_20", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Overflow", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("trigger_pin", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.Counter:
                    newEntity.AddParameter("on_under_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_over_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Count", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("is_limitless", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("trigger_limit", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.LogicCounter:
                    newEntity.AddParameter("on_under_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_over_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("restored_on_under_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("restored_on_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("restored_on_over_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Count", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("is_limitless", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("trigger_limit", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("non_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.LogicPressurePad:
                    newEntity.AddParameter("Pad_Activated", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pad_Deactivated", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("bound_characters", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Limit", new cInteger(1), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("Count", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.SetObject:
                    newEntity.AddParameter("Input", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Output", new cFloat(), ParameterVariant.OUTPUT); //Object
                    break;
                case FunctionType.GateResourceInterface:
                    newEntity.AddParameter("gate_status_changed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("request_open_on_reset", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("request_lock_on_reset", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("force_open_on_reset", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("force_close_on_reset", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("is_auto", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("auto_close_delay", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("is_open", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("is_locked", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("gate_status", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.Door:
                    newEntity.AddParameter("started_opening", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("started_closing", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished_opening", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished_closing", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("used_locked", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("used_unlocked", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("used_forced_open", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("used_forced_closed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("waiting_to_open", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("highlight", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("unhighlight", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("zone_link", new cFloat(), ParameterVariant.INPUT); //ZoneLinkPtr
                    newEntity.AddParameter("animation", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ANIMATED_MODEL) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //ANIMATED_MODEL
                    newEntity.AddParameter("trigger_filter", new cBool(true), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("icon_pos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("icon_usable_radius", new cFloat(), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("show_icon_when_locked", new cBool(true), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("nav_mesh", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NAV_MESH_BARRIER_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //NAV_MESH_BARRIER_RESOURCE
                    newEntity.AddParameter("wait_point_1", new cInteger(), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("wait_point_2", new cInteger(), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("is_scripted", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("wait_to_open", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("is_waiting", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("unlocked_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("locked_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("icon_keyframe", new cEnum(EnumType.UI_ICON_ICON, 0), ParameterVariant.PARAMETER); //UI_ICON_ICON
                    newEntity.AddParameter("detach_anim", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("invert_nav_mesh_barrier", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.MonitorPadInput:
                    newEntity.AddParameter("on_pressed_A", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_A", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_B", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_B", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_X", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_X", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_Y", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_Y", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_L1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_L1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_R1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_R1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_L2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_L2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_R2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_R2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_L3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_L3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_R3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_R3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_dpad_left", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_dpad_left", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_dpad_right", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_dpad_right", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_dpad_up", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_dpad_up", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_dpad_down", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_dpad_down", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("left_stick_x", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("left_stick_y", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("right_stick_x", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("right_stick_y", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.MonitorActionMap:
                    newEntity.AddParameter("on_pressed_use", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_use", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_crouch", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_crouch", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_run", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_run", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_aim", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_aim", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_shoot", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_shoot", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_reload", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_reload", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_melee", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_melee", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_activate_item", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_activate_item", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_switch_weapon", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_switch_weapon", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_change_dof_focus", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_change_dof_focus", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_select_motion_tracker", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_select_motion_tracker", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_select_torch", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_select_torch", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_torch_beam", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_torch_beam", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_peek", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_peek", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pressed_back_close", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_released_back_close", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("movement_stick_x", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("movement_stick_y", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("camera_stick_x", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("camera_stick_y", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("mouse_x", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("mouse_y", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("analog_aim", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("analog_shoot", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.PadLightBar:
                    newEntity.AddParameter("colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.PadRumbleImpulse:
                    newEntity.AddParameter("low_frequency_rumble", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("high_frequency_rumble", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("left_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("right_trigger_impulse", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("aim_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("shoot_trigger_impulse", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerViewCone:
                    newEntity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("target_is_visible", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("no_target_visible", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("target", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("fov", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("max_distance", new cFloat(15.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("aspect_ratio", new cFloat(1.777f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("source_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("intersect_with_geometry", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("visible_target", new cFloat(), ParameterVariant.OUTPUT); //Object
                    newEntity.AddParameter("target_offset", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER); //VIEWCONE_TYPE
                    newEntity.AddParameter("visible_area_horizontal", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("visible_area_vertical", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerCameraViewCone:
                    newEntity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("target", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("fov", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("aspect_ratio", new cFloat(1.777f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("intersect_with_geometry", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("use_camera_fov", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("target_offset", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER); //VIEWCONE_TYPE
                    newEntity.AddParameter("visible_area_horizontal", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("visible_area_vertical", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerCameraViewConeMulti:
                    newEntity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enter1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enter2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enter3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enter4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enter5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enter6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enter7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enter8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enter9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("target", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target1", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target2", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target3", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target4", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target5", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target6", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target7", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target8", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target9", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("fov", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("aspect_ratio", new cFloat(1.777f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("intersect_with_geometry", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("number_of_inputs", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("use_camera_fov", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("visible_area_type", new cEnum(EnumType.VIEWCONE_TYPE, 1), ParameterVariant.PARAMETER); //VIEWCONE_TYPE
                    newEntity.AddParameter("visible_area_horizontal", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("visible_area_vertical", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("raycast_grace", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerCameraVolume:
                    newEntity.AddParameter("inside", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("exit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("inside_factor", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("lookat_factor", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("lookat_X_position", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("lookat_Y_position", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("start_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_Debug_Menu_Item:
                    newEntity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.Character:
                    newEntity.AddParameter("finished_spawning", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished_respawning", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("dead_container_take_slot", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("dead_container_emptied", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_ragdoll_impact", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_footstep", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_despawn_requested", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("spawn_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("contents_of_dead_container", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    newEntity.AddParameter("PopToNavMesh", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("disable_dead_container", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("allow_container_without_death", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("container_interaction_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("anim_set", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ANIM_SET) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //ANIM_SET
                    newEntity.AddParameter("anim_tree_set", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ANIM_TREE_SET) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //ANIM_TREE_SET
                    newEntity.AddParameter("attribute_set", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ATTRIBUTE_SET) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //ATTRIBUTE_SET
                    newEntity.AddParameter("is_player", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_backstage", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("force_backstage_on_respawn", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS, 3), ParameterVariant.PARAMETER); //CHARACTER_CLASS
                    newEntity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    newEntity.AddParameter("dialogue_voice", new cEnum(EnumType.DIALOGUE_VOICE_ACTOR, 0), ParameterVariant.PARAMETER); //DIALOGUE_VOICE_ACTOR
                    newEntity.AddParameter("spawn_id", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("display_model", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("reference_skeleton", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHR_SKELETON_SET) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //CHR_SKELETON_SET
                    newEntity.AddParameter("torso_sound", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_TORSO_GROUP) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_TORSO_GROUP
                    newEntity.AddParameter("leg_sound", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_LEG_GROUP) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_LEG_GROUP
                    newEntity.AddParameter("footwear_sound", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_FOOTWEAR_GROUP) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_FOOTWEAR_GROUP
                    newEntity.AddParameter("custom_character_type", new cEnum(EnumType.CUSTOM_CHARACTER_TYPE, 0), ParameterVariant.PARAMETER); //CUSTOM_CHARACTER_TYPE
                    newEntity.AddParameter("custom_character_accessory_override", new cEnum(EnumType.CUSTOM_CHARACTER_ACCESSORY_OVERRIDE, 0), ParameterVariant.PARAMETER); //CUSTOM_CHARACTER_ACCESSORY_OVERRIDE
                    newEntity.AddParameter("custom_character_population_type", new cEnum(EnumType.CUSTOM_CHARACTER_POPULATION, 0), ParameterVariant.PARAMETER); //CUSTOM_CHARACTER_POPULATION
                    newEntity.AddParameter("named_custom_character", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("named_custom_character_assets_set", new cEnum(EnumType.CUSTOM_CHARACTER_ASSETS, 0), ParameterVariant.PARAMETER); //CUSTOM_CHARACTER_ASSETS
                    newEntity.AddParameter("gcip_distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("inventory_set", new cEnum(EnumType.PLAYER_INVENTORY_SET, 0), ParameterVariant.PARAMETER); //PLAYER_INVENTORY_SET
                    break;
                case FunctionType.RegisterCharacterModel:
                    newEntity.AddParameter("display_model", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("reference_skeleton", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHR_SKELETON_SET) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //CHR_SKELETON_SET
                    break;
                case FunctionType.DespawnPlayer:
                    newEntity.AddParameter("despawned", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.DespawnCharacter:
                    newEntity.AddParameter("despawned", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.FilterAnd:
                    newEntity.AddParameter("filter", new cBool(), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.FilterOr:
                    newEntity.AddParameter("filter", new cBool(), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.FilterNot:
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.FilterIsEnemyOfCharacter:
                    newEntity.AddParameter("Character", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("use_alliance_at_death", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FilterIsEnemyOfAllianceGroup:
                    newEntity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    break;
                case FunctionType.FilterIsPhysicsObject:
                    newEntity.AddParameter("object", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsObject:
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsCharacter:
                    newEntity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsFacingTarget:
                    newEntity.AddParameter("target", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("tolerance", new cFloat(45.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FilterBelongsToAlliance:
                    newEntity.AddParameter("alliance_group", new cEnum(EnumType.ALLIANCE_GROUP, 0), ParameterVariant.PARAMETER); //ALLIANCE_GROUP
                    break;
                case FunctionType.FilterHasWeaponOfType:
                    newEntity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER); //WEAPON_TYPE
                    break;
                case FunctionType.FilterHasWeaponEquipped:
                    newEntity.AddParameter("weapon_type", new cEnum(EnumType.WEAPON_TYPE, 0), ParameterVariant.PARAMETER); //WEAPON_TYPE
                    break;
                case FunctionType.FilterIsinInventory:
                    newEntity.AddParameter("ItemName", new cString(" "), ParameterVariant.INPUT); //String
                    break;
                case FunctionType.FilterIsCharacterClass:
                    newEntity.AddParameter("character_class", new cEnum(EnumType.CHARACTER_CLASS, 3), ParameterVariant.PARAMETER); //CHARACTER_CLASS
                    break;
                case FunctionType.FilterIsCharacterClassCombo:
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.FilterIsInAlertnessState:
                    newEntity.AddParameter("AlertState", new cEnum(EnumType.ALERTNESS_STATE, 0), ParameterVariant.PARAMETER); //ALERTNESS_STATE
                    break;
                case FunctionType.FilterIsInLocomotionState:
                    newEntity.AddParameter("State", new cEnum(EnumType.LOCOMOTION_STATE, 0), ParameterVariant.PARAMETER); //LOCOMOTION_STATE
                    break;
                case FunctionType.FilterCanSeeTarget:
                    newEntity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsAgressing:
                    newEntity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsValidInventoryItem:
                    newEntity.AddParameter("item", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    break;
                case FunctionType.FilterIsInWeaponRange:
                    newEntity.AddParameter("weapon_owner", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.TriggerWhenSeeTarget:
                    newEntity.AddParameter("seen", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Target", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.FilterIsPlatform:
                    newEntity.AddParameter("Platform", new cEnum(EnumType.PLATFORM_TYPE, 5), ParameterVariant.PARAMETER); //PLATFORM_TYPE
                    break;
                case FunctionType.FilterIsUsingDevice:
                    newEntity.AddParameter("Device", new cEnum(EnumType.INPUT_DEVICE_TYPE, 0), ParameterVariant.PARAMETER); //INPUT_DEVICE_TYPE
                    break;
                case FunctionType.FilterSmallestUsedDifficulty:
                    newEntity.AddParameter("difficulty", new cEnum(EnumType.DIFFICULTY_SETTING_TYPE, 2), ParameterVariant.PARAMETER); //DIFFICULTY_SETTING_TYPE
                    break;
                case FunctionType.FilterHasPlayerCollectedIdTag:
                    newEntity.AddParameter("tag_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.IDTAG_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //IDTAG_ID
                    break;
                case FunctionType.FilterHasBehaviourTreeFlagSet:
                    newEntity.AddParameter("BehaviourTreeFlag", new cEnum(EnumType.BEHAVIOUR_TREE_FLAGS, 2), ParameterVariant.PARAMETER); //BEHAVIOUR_TREE_FLAGS
                    break;
                case FunctionType.Job:
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.JOB_Idle:
                    newEntity.AddParameter("task_operation_mode", new cEnum(EnumType.TASK_OPERATION_MODE, 0), ParameterVariant.PARAMETER); //TASK_OPERATION_MODE
                    newEntity.AddParameter("should_perform_all_tasks", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.JOB_SpottingPosition:
                    newEntity.AddParameter("SpottingPosition", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.Task:
                    newEntity.AddParameter("start_command", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("selected_by_npc", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("clean_up", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("Job", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("TaskPosition", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("should_stop_moving_when_reached", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("should_orientate_when_reached", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("reached_distance_threshold", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("selection_priority", new cEnum(EnumType.TASK_PRIORITY, 0), ParameterVariant.PARAMETER); //TASK_PRIORITY
                    newEntity.AddParameter("timeout", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("always_on_tracker", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FlareTask:
                    newEntity.AddParameter("specific_character", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("filter_options", new cEnum(EnumType.TASK_CHARACTER_CLASS_FILTER, 1024), ParameterVariant.PARAMETER); //TASK_CHARACTER_CLASS_FILTER
                    break;
                case FunctionType.IdleTask:
                    newEntity.AddParameter("start_pre_move", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_interrupt", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("interrupted_while_moving", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("specific_character", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("should_auto_move_to_position", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("ignored_for_auto_selection", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("has_pre_move_script", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("has_interrupt_script", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("filter_options", new cEnum(EnumType.TASK_CHARACTER_CLASS_FILTER, 1024), ParameterVariant.PARAMETER); //TASK_CHARACTER_CLASS_FILTER
                    break;
                case FunctionType.FollowTask:
                    newEntity.AddParameter("can_initially_end_early", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("stop_radius", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_ForceNextJob:
                    newEntity.AddParameter("job_started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("job_completed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("job_interrupted", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ShouldInterruptCurrentTask", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Job", new cFloat(), ParameterVariant.PARAMETER); //Object
                    newEntity.AddParameter("InitialTask", new cFloat(), ParameterVariant.PARAMETER); //Object
                    break;
                case FunctionType.NPC_SetRateOfFire:
                    newEntity.AddParameter("MinTimeBetweenShots", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("RandomRange", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_SetFiringRhythm:
                    newEntity.AddParameter("MinShootingTime", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("RandomRangeShootingTime", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MinNonShootingTime", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("RandomRangeNonShootingTime", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MinCoverNonShootingTime", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("RandomRangeCoverNonShootingTime", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_SetFiringAccuracy:
                    newEntity.AddParameter("Accuracy", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.TriggerBindAllNPCs:
                    newEntity.AddParameter("npc_inside", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("npc_outside", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("centre", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("radius", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.Trigger_AudioOccluded:
                    newEntity.AddParameter("NotOccluded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Occluded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("Range", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SwitchLevel:
                    newEntity.AddParameter("level_name", new cString(""), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.SoundPlaybackBaseClass:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("attached_sound_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_OBJECT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //SOUND_OBJECT
                    newEntity.AddParameter("sound_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("argument_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    newEntity.AddParameter("argument_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    newEntity.AddParameter("argument_3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    newEntity.AddParameter("argument_4", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    newEntity.AddParameter("argument_5", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    newEntity.AddParameter("namespace", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("object_position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("restore_on_checkpoint", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Sound:
                    newEntity.AddParameter("stop_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("is_static_ambience", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("start_on", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("multi_trigger", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_multi_emitter", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("create_sound_object", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("switch_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_SWITCH) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_SWITCH
                    newEntity.AddParameter("switch_value", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("last_gen_enabled", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("resume_after_suspended", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.Speech:
                    newEntity.AddParameter("on_speech_started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("alt_character", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 2), ParameterVariant.PARAMETER); //SPEECH_PRIORITY
                    newEntity.AddParameter("queue_time", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NPC_DynamicDialogueGlobalRange:
                    newEntity.AddParameter("dialogue_range", new cFloat(35.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CHR_PlayNPCBark:
                    newEntity.AddParameter("on_speech_started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_speech_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("queue_time", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("sound_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 0), ParameterVariant.PARAMETER); //SPEECH_PRIORITY
                    newEntity.AddParameter("dialogue_mode", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    newEntity.AddParameter("action", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    break;
                case FunctionType.SpeechScript:
                    newEntity.AddParameter("on_script_ended", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("character_01", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("character_02", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("character_03", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("character_04", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("character_05", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("alt_character_01", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("alt_character_02", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("alt_character_03", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("alt_character_04", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("alt_character_05", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("speech_priority", new cEnum(EnumType.SPEECH_PRIORITY, 2), ParameterVariant.PARAMETER); //SPEECH_PRIORITY
                    newEntity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("line_01_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("line_01_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("line_02_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("line_02_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("line_02_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("line_03_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("line_03_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("line_03_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("line_04_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("line_04_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("line_04_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("line_05_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("line_05_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("line_05_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("line_06_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("line_06_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("line_06_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("line_07_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("line_07_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("line_07_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("line_08_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("line_08_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("line_08_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("line_09_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("line_09_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("line_09_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("line_10_delay", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("line_10_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("line_10_character", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("restore_on_checkpoint", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SoundNetworkNode:
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.SoundEnvironmentMarker:
                    newEntity.AddParameter("reverb_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_REVERB) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_REVERB
                    newEntity.AddParameter("on_enter_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("on_exit_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("linked_network_occlusion_scaler", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("room_size", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_STATE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_STATE
                    newEntity.AddParameter("disable_network_creation", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.SoundEnvironmentZone:
                    newEntity.AddParameter("reverb_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_REVERB) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_REVERB
                    newEntity.AddParameter("priority", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.SoundLoadBank:
                    newEntity.AddParameter("bank_loaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("sound_bank", new cString(""), ParameterVariant.INPUT); //String
                    newEntity.AddParameter("trigger_via_pin", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("memory_pool", new cEnum(EnumType.SOUND_POOL, 0), ParameterVariant.PARAMETER); //SOUND_POOL
                    break;
                case FunctionType.SoundLoadSlot:
                    newEntity.AddParameter("bank_loaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("sound_bank", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("memory_pool", new cEnum(EnumType.SOUND_POOL, 0), ParameterVariant.PARAMETER); //SOUND_POOL
                    break;
                case FunctionType.SoundSetRTPC:
                    newEntity.AddParameter("rtpc_value", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("sound_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_OBJECT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //SOUND_OBJECT
                    newEntity.AddParameter("rtpc_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_RTPC) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_RTPC
                    newEntity.AddParameter("smooth_rate", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("start_on", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SoundSetState:
                    newEntity.AddParameter("state_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_STATE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_STATE
                    newEntity.AddParameter("state_value", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.SoundSetSwitch:
                    newEntity.AddParameter("sound_object", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_OBJECT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //SOUND_OBJECT
                    newEntity.AddParameter("switch_name", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_SWITCH) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_SWITCH
                    newEntity.AddParameter("switch_value", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.SoundImpact:
                    newEntity.AddParameter("sound_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("is_occludable", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("argument_1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    newEntity.AddParameter("argument_2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    newEntity.AddParameter("argument_3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_ARGUMENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_ARGUMENT
                    break;
                case FunctionType.SoundBarrier:
                    newEntity.AddParameter("default_open", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("band_aid", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("override_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddResource(ResourceType.COLLISION_MAPPING);
                    break;
                case FunctionType.MusicController:
                    newEntity.AddParameter("music_start_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("music_end_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("music_restart_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("layer_control_rtpc", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_PARAMETER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_PARAMETER
                    newEntity.AddParameter("smooth_rate", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("alien_max_distance", new cFloat(50.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("object_max_distance", new cFloat(50.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.MusicTrigger:
                    newEntity.AddParameter("on_triggered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("connected_object", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("music_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("smooth_rate", new cFloat(-1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("queue_time", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("interrupt_all", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("trigger_once", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("rtpc_set_mode", new cEnum(EnumType.MUSIC_RTPC_MODE, 0), ParameterVariant.PARAMETER); //MUSIC_RTPC_MODE
                    newEntity.AddParameter("rtpc_target_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("rtpc_duration", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("rtpc_set_return_mode", new cEnum(EnumType.MUSIC_RTPC_MODE, 0), ParameterVariant.PARAMETER); //MUSIC_RTPC_MODE
                    newEntity.AddParameter("rtpc_return_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SoundLevelInitialiser:
                    newEntity.AddParameter("auto_generate_networks", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("network_node_min_spacing", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("network_node_max_visibility", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("network_node_ceiling_height", new cFloat(), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SoundMissionInitialiser:
                    newEntity.AddParameter("human_max_threat", new cFloat(0.7f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("android_max_threat", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("alien_max_threat", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SoundRTPCController:
                    newEntity.AddParameter("stealth_default_on", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("threat_default_on", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SoundTimelineTrigger:
                    newEntity.AddParameter("sound_event", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_EVENT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_EVENT
                    newEntity.AddParameter("trigger_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SoundPhysicsInitialiser:
                    newEntity.AddParameter("contact_max_timeout", new cFloat(0.33f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("contact_smoothing_attack_rate", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("contact_smoothing_decay_rate", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("contact_min_magnitude", new cFloat(0.01f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("contact_max_trigger_distance", new cFloat(25.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("impact_min_speed", new cFloat(0.2f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("impact_max_trigger_distance", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ragdoll_min_timeout", new cFloat(0.25f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ragdoll_min_speed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SoundPlayerFootwearOverride:
                    newEntity.AddParameter("footwear_sound", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SOUND_FOOTWEAR_GROUP) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SOUND_FOOTWEAR_GROUP
                    break;
                case FunctionType.AddToInventory:
                    newEntity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.RemoveFromInventory:
                    newEntity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.LimitItemUse:
                    newEntity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PlayerHasItem:
                    newEntity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PlayerHasItemWithName:
                    newEntity.AddParameter("item_name", new cString(" "), ParameterVariant.INPUT); //String
                    break;
                case FunctionType.PlayerHasItemEntity:
                    newEntity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PlayerHasEnoughItems:
                    newEntity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("quantity", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.PlayerHasSpaceForItem:
                    newEntity.AddParameter("items", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.InventoryItem:
                    newEntity.AddParameter("collect", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("itemName", new cString(""), ParameterVariant.INPUT); //String
                    newEntity.AddParameter("out_itemName", new cString(""), ParameterVariant.OUTPUT); //String
                    newEntity.AddParameter("out_quantity", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("item", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("quantity", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("clear_on_collect", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("gcip_instances_count", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.GetInventoryItemName:
                    newEntity.AddParameter("item", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    newEntity.AddParameter("equippable_item", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.EQUIPPABLE_ITEM_INSTANCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //EQUIPPABLE_ITEM_INSTANCE
                    break;
                case FunctionType.PickupSpawner:
                    newEntity.AddParameter("collect", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("spawn_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("pos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("item_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.MultiplePickupSpawner:
                    newEntity.AddParameter("pos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("item_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.AddItemsToGCPool:
                    newEntity.AddParameter("items", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    break;
                case FunctionType.SetupGCDistribution:
                    newEntity.AddParameter("c00", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("c01", new cFloat(0.969f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("c02", new cFloat(0.882f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("c03", new cFloat(0.754f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("c04", new cFloat(0.606f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("c05", new cFloat(0.457f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("c06", new cFloat(0.324f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("c07", new cFloat(0.216f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("c08", new cFloat(0.135f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("c09", new cFloat(0.079f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("c10", new cFloat(0.043f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("minimum_multiplier", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("divisor", new cFloat(20.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("lookup_decrease_time", new cFloat(15.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("lookup_point_increase", new cInteger(2), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.AllocateGCItemsFromPool:
                    newEntity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("items", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    newEntity.AddParameter("force_usage_count", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.AllocateGCItemFromPoolBySubset:
                    newEntity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("selectable_items", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("item_name", new cString(""), ParameterVariant.OUTPUT); //String
                    newEntity.AddParameter("item_quantity", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("force_usage", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("distribution_bias", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.QueryGCItemPool:
                    newEntity.AddParameter("count", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("item_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.RemoveFromGCItemPool:
                    newEntity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("item_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("item_quantity", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("gcip_instances_to_remove", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.FlashScript:
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("filename", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("layer_name", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("target_texture_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("type", new cEnum(EnumType.FLASH_SCRIPT_RENDER_TYPE, 0), ParameterVariant.PARAMETER); //FLASH_SCRIPT_RENDER_TYPE
                    break;
                case FunctionType.UI_KeyGate:
                    newEntity.AddParameter("keycard_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("keycode_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("keycard_fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("keycode_fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("keycard_cancelled", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("keycode_cancelled", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("lock_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("code", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("carduid", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("key_type", new cEnum(EnumType.UI_KEYGATE_TYPE, 1), ParameterVariant.PARAMETER); //UI_KEYGATE_TYPE
                    break;
                case FunctionType.RTT_MoviePlayer:
                    newEntity.AddParameter("start", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("end", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("show_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("filename", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("layer_name", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("target_texture_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.MoviePlayer:
                    newEntity.AddParameter("start", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("end", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("skipped", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("trigger_end_on_skipped", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("filename", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("skippable", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("enable_debug_skip", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DurangoVideoCapture:
                    newEntity.AddParameter("clip_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.VideoCapture:
                    newEntity.AddParameter("clip_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("only_in_capture_mode", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.FlashInvoke:
                    newEntity.AddParameter("layer_name", new cString(" "), ParameterVariant.INPUT); //String
                    newEntity.AddParameter("mrtt_texture", new cString(" "), ParameterVariant.INPUT); //String
                    newEntity.AddParameter("method", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("invoke_type", new cEnum(EnumType.FLASH_INVOKE_TYPE, 0), ParameterVariant.PARAMETER); //FLASH_INVOKE_TYPE
                    newEntity.AddParameter("int_argument_0", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("int_argument_1", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("int_argument_2", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("int_argument_3", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("float_argument_0", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("float_argument_1", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("float_argument_2", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("float_argument_3", new cFloat(), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.MotionTrackerPing:
                    newEntity.AddParameter("FakePosition", new cTransform(), ParameterVariant.INPUT); //Position
                    break;
                case FunctionType.FlashCallback:
                    newEntity.AddParameter("callback", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("callback_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.PopupMessage:
                    newEntity.AddParameter("display", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("header_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("main_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("duration", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("sound_event", new cEnum(EnumType.POPUP_MESSAGE_SOUND, 1), ParameterVariant.PARAMETER); //POPUP_MESSAGE_SOUND
                    newEntity.AddParameter("icon_keyframe", new cEnum(EnumType.POPUP_MESSAGE_ICON, 0), ParameterVariant.PARAMETER); //POPUP_MESSAGE_ICON
                    break;
                case FunctionType.UIBreathingGameIcon:
                    newEntity.AddParameter("fill_percentage", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("prompt_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.GenericHighlightEntity:
                    newEntity.AddParameter("highlight_geometry", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //RENDERABLE_INSTANCE
                    break;
                case FunctionType.UI_Icon:
                    newEntity.AddParameter("start", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("button_released", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("broadcasted_start", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("highlight", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("unhighlight", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("lock_looked_at", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("lock_interaction", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("lock_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("enable_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("show_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("highlight_geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("target_pickup_item", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("highlight_distance_threshold", new cFloat(3.15f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("interaction_distance_threshold", new cFloat(), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("icon_user", new cFloat(), ParameterVariant.OUTPUT); //Object
                    newEntity.AddParameter("unlocked_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("locked_text", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("action_text", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("icon_keyframe", new cEnum(EnumType.UI_ICON_ICON, 0), ParameterVariant.PARAMETER); //UI_ICON_ICON
                    newEntity.AddParameter("can_be_used", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("category", new cEnum(EnumType.PICKUP_CATEGORY, 0), ParameterVariant.PARAMETER); //PICKUP_CATEGORY
                    newEntity.AddParameter("push_hold_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.UI_Attached:
                    newEntity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ui_icon", new cInteger(0), ParameterVariant.INPUT); //int
                    break;
                case FunctionType.UI_Container:
                    newEntity.AddParameter("take_slot", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("emptied", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("contents", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.INVENTORY_ITEM_QUANTITY) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //INVENTORY_ITEM_QUANTITY
                    newEntity.AddParameter("has_been_used", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("is_persistent", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_temporary", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UI_ReactionGame:
                    newEntity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stage0_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stage0_fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stage1_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stage1_fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stage2_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stage2_fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("resources_finished_unloading", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("resources_finished_loading", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("completion_percentage", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("exit_on_fail", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UI_Keypad:
                    newEntity.AddParameter("success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("code", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("exit_on_fail", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.HackingGame:
                    newEntity.AddParameter("win", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("alarm_triggered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("loaded_idle", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("loaded_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("resources_finished_unloading", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("resources_finished_loading", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("lock_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("completion_percentage", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("hacking_difficulty", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("auto_exit", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SetHackingToolLevel:
                    newEntity.AddParameter("level", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.TerminalContent:
                    newEntity.AddParameter("selected", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("content_title", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("content_decoration_title", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("additional_info", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("is_connected_to_audio_log", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_triggerable", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_single_use", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.TerminalFolder:
                    newEntity.AddParameter("code_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("code_fail", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("selected", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("lock_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("content0", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_CONTENT_DETAILS) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //TERMINAL_CONTENT_DETAILS
                    newEntity.AddParameter("content1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_CONTENT_DETAILS) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //TERMINAL_CONTENT_DETAILS
                    newEntity.AddParameter("code", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("folder_title", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("folder_lock_type", new cEnum(EnumType.FOLDER_LOCK_TYPE, 1), ParameterVariant.PARAMETER); //FOLDER_LOCK_TYPE
                    break;
                case FunctionType.AccessTerminal:
                    newEntity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("all_data_has_been_read", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("light_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("folder0", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //TERMINAL_FOLDER_DETAILS
                    newEntity.AddParameter("folder1", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //TERMINAL_FOLDER_DETAILS
                    newEntity.AddParameter("folder2", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //TERMINAL_FOLDER_DETAILS
                    newEntity.AddParameter("folder3", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.TERMINAL_FOLDER_DETAILS) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //TERMINAL_FOLDER_DETAILS
                    newEntity.AddParameter("all_data_read", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("location", new cEnum(EnumType.TERMINAL_LOCATION, 0), ParameterVariant.PARAMETER); //TERMINAL_LOCATION
                    break;
                case FunctionType.SetGatingToolLevel:
                    newEntity.AddParameter("level", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER); //GATING_TOOL_TYPE
                    break;
                case FunctionType.GetGatingToolLevel:
                    newEntity.AddParameter("level", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER); //GATING_TOOL_TYPE
                    break;
                case FunctionType.GetPlayerHasGatingTool:
                    newEntity.AddParameter("has_tool", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("doesnt_have_tool", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER); //GATING_TOOL_TYPE
                    break;
                case FunctionType.GetPlayerHasKeycard:
                    newEntity.AddParameter("has_card", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("doesnt_have_card", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("card_uid", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.SetPlayerHasKeycard:
                    newEntity.AddParameter("card_uid", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.SetPlayerHasGatingTool:
                    newEntity.AddParameter("tool_type", new cEnum(EnumType.GATING_TOOL_TYPE, 0), ParameterVariant.PARAMETER); //GATING_TOOL_TYPE
                    break;
                case FunctionType.CollectSevastopolLog:
                    newEntity.AddParameter("log_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.SEVASTOPOL_LOG_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //SEVASTOPOL_LOG_ID
                    break;
                case FunctionType.CollectNostromoLog:
                    newEntity.AddParameter("log_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.NOSTROMO_LOG_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //NOSTROMO_LOG_ID
                    break;
                case FunctionType.CollectIDTag:
                    newEntity.AddParameter("tag_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.IDTAG_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //IDTAG_ID
                    break;
                case FunctionType.StartNewChapter:
                    newEntity.AddParameter("chapter", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.UnlockLogEntry:
                    newEntity.AddParameter("entry", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.MapAnchor:
                    newEntity.AddParameter("map_north", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("map_pos", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("map_scale", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("keyframe", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("keyframe1", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("keyframe2", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("keyframe3", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("keyframe4", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("keyframe5", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("world_pos", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("is_default_for_items", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.MapItem:
                    newEntity.AddParameter("show_ui_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("item_type", new cEnum(EnumType.MAP_ICON_TYPE, 0), ParameterVariant.PARAMETER); //MAP_ICON_TYPE
                    newEntity.AddParameter("map_keyframe", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.MAP_KEYFRAME_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //MAP_KEYFRAME_ID
                    break;
                case FunctionType.UnlockMapDetail:
                    newEntity.AddParameter("map_keyframe", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("details", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.RewireSystem:
                    newEntity.AddParameter("on", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("off", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("world_pos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("display_name", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("display_name_enum", new cEnum(EnumType.REWIRE_SYSTEM_NAME, 0), ParameterVariant.PARAMETER); //REWIRE_SYSTEM_NAME
                    newEntity.AddParameter("on_by_default", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("running_cost", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("system_type", new cEnum(EnumType.REWIRE_SYSTEM_TYPE, 0), ParameterVariant.PARAMETER); //REWIRE_SYSTEM_TYPE
                    newEntity.AddParameter("map_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("element_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.RewireLocation:
                    newEntity.AddParameter("power_draw_increased", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("power_draw_reduced", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("systems", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_SYSTEM) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //REWIRE_SYSTEM
                    newEntity.AddParameter("element_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("display_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.RewireAccess_Point:
                    newEntity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("ui_breakout_triggered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("interactive_locations", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_LOCATION) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //REWIRE_LOCATION
                    newEntity.AddParameter("visible_locations", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_LOCATION) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //REWIRE_LOCATION
                    newEntity.AddParameter("additional_power", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("display_name", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("map_element_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("map_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("map_x_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("map_y_offset", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("map_zoom", new cFloat(3.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.RewireTotalPowerResource:
                    newEntity.AddParameter("total_power", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.Rewire:
                    newEntity.AddParameter("closed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("locations", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_LOCATION) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //REWIRE_LOCATION
                    newEntity.AddParameter("access_points", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.REWIRE_ACCESS_POINT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //REWIRE_ACCESS_POINT
                    newEntity.AddParameter("map_keyframe", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("total_power", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.SetMotionTrackerRange:
                    newEntity.AddParameter("range", new cFloat(20.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SetGamepadAxes:
                    newEntity.AddParameter("invert_x", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("invert_y", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("save_settings", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SetGameplayTips:
                    newEntity.AddParameter("tip_string_id", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.GameOver:
                    newEntity.AddParameter("tip_string_id", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("default_tips_enabled", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("level_tips_enabled", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GameplayTip:
                    newEntity.AddParameter("string_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.GAMEPLAY_TIP_STRING_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //GAMEPLAY_TIP_STRING_ID
                    break;
                case FunctionType.Minigames:
                    newEntity.AddParameter("on_success", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_failure", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("game_inertial_damping_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("game_green_text_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("game_yellow_chart_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("game_overloc_fail_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("game_docking_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("game_environ_ctr_active", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("config_pass_number", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("config_fail_limit", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("config_difficulty", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.SetBlueprintInfo:
                    newEntity.AddParameter("type", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.BLUEPRINT_TYPE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //BLUEPRINT_TYPE
                    newEntity.AddParameter("level", new cEnum(EnumType.BLUEPRINT_LEVEL, 1), ParameterVariant.PARAMETER); //BLUEPRINT_LEVEL
                    newEntity.AddParameter("available", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GetBlueprintLevel:
                    newEntity.AddParameter("level", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("type", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.BLUEPRINT_TYPE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //BLUEPRINT_TYPE
                    break;
                case FunctionType.GetBlueprintAvailable:
                    newEntity.AddParameter("available", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("type", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.BLUEPRINT_TYPE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //BLUEPRINT_TYPE
                    break;
                case FunctionType.GetSelectedCharacterId:
                    newEntity.AddParameter("character_id", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.GetNextPlaylistLevelName:
                    newEntity.AddParameter("level_name", new cString(""), ParameterVariant.OUTPUT); //String
                    break;
                case FunctionType.IsPlaylistTypeSingle:
                    newEntity.AddParameter("single", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.IsPlaylistTypeAll:
                    newEntity.AddParameter("all", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.IsPlaylistTypeMarathon:
                    newEntity.AddParameter("marathon", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.IsCurrentLevelAChallengeMap:
                    newEntity.AddParameter("challenge_map", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.IsCurrentLevelAPreorderMap:
                    newEntity.AddParameter("preorder_map", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.GetCurrentPlaylistLevelIndex:
                    newEntity.AddParameter("index", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.SetObjectiveCompleted:
                    newEntity.AddParameter("objective_id", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.GoToFrontend:
                    newEntity.AddParameter("frontend_state", new cEnum(EnumType.FRONTEND_STATE, 0), ParameterVariant.PARAMETER); //FRONTEND_STATE
                    break;
                case FunctionType.TriggerLooper:
                    newEntity.AddParameter("target", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("count", new cInteger(1), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("delay", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CoverLine:
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("low", new cBool(), ParameterVariant.INPUT); //bool
                    newEntity.AddResource(ResourceType.CATHODE_COVER_SEGMENT);
                    newEntity.AddParameter("LinePathPosition", new cTransform(), ParameterVariant.INTERNAL); //Position
                    break;
                case FunctionType.TRAV_ContinuousLadder:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("RungSpacing", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousPipe:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousLedge:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("Dangling", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.AUTODETECT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //AUTODETECT
                    newEntity.AddParameter("Sidling", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.AUTODETECT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //AUTODETECT
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousClimbingWall:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("Dangling", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.AUTODETECT) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //AUTODETECT
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousCinematicSidle:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousBalanceBeam:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_ContinuousTightGap:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_1ShotVentEntrance:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    newEntity.AddResource(ResourceType.TRAVERSAL_SEGMENT);
                    break;
                case FunctionType.TRAV_1ShotVentExit:
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    newEntity.AddResource(ResourceType.TRAVERSAL_SEGMENT);
                    break;
                case FunctionType.TRAV_1ShotFloorVentEntrance:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    newEntity.AddResource(ResourceType.TRAVERSAL_SEGMENT);
                    break;
                case FunctionType.TRAV_1ShotFloorVentExit:
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Completed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    newEntity.AddResource(ResourceType.TRAVERSAL_SEGMENT);
                    break;
                case FunctionType.TRAV_1ShotClimbUnder:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("LinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_1ShotLeap:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnSuccess", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnFailure", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("StartEdgeLinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("EndEdgeLinePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("InUse", new cBool(), ParameterVariant.OUTPUT); //bool
                    newEntity.AddParameter("MissDistance", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("NearMissDistance", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.TRAV_1ShotSpline:
                    newEntity.AddParameter("OnEnter", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OnExit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("enable_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("open_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("EntrancePath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("ExitPath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("MinimumPath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("MaximumPath", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("MinimumSupport", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("MaximumSupport", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("template", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("headroom", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("extra_cost", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("fit_end_to_edge", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("min_speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 0), ParameterVariant.PARAMETER); //LOCOMOTION_TARGET_SPEED
                    newEntity.AddParameter("max_speed", new cEnum(EnumType.LOCOMOTION_TARGET_SPEED, 0), ParameterVariant.PARAMETER); //LOCOMOTION_TARGET_SPEED
                    newEntity.AddParameter("animationTree", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    newEntity.AddResource(ResourceType.TRAVERSAL_SEGMENT);
                    break;
                case FunctionType.NavMeshBarrier:
                    newEntity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("opaque", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("allowed_character_classes_when_open", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    newEntity.AddParameter("allowed_character_classes_when_closed", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    newEntity.AddResource(ResourceType.NAV_MESH_BARRIER_RESOURCE);
                    break;
                case FunctionType.NavMeshWalkablePlatform:
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.NavMeshExclusionArea:
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.NavMeshArea:
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("area_type", new cEnum(EnumType.NAV_MESH_AREA_TYPE, 0), ParameterVariant.PARAMETER); //NAV_MESH_AREA_TYPE
                    break;
                case FunctionType.NavMeshReachabilitySeedPoint:
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.CoverExclusionArea:
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("exclude_cover", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("exclude_vaults", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("exclude_mantles", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("exclude_jump_downs", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("exclude_crawl_space_spotting_positions", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("exclude_spotting_positions", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("exclude_assault_positions", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SpottingExclusionArea:
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("half_dimensions", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.PathfindingTeleportNode:
                    newEntity.AddParameter("started_teleporting", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stopped_teleporting", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("extra_cost", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.PathfindingWaitNode:
                    newEntity.AddParameter("character_getting_near", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("character_arriving", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("character_stopped", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("started_waiting", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stopped_waiting", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("extra_cost", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 0), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.PathfindingManualNode:
                    newEntity.AddParameter("character_arriving", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("character_stopped", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("started_animating", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stopped_animating", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("PlayAnimData", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("destination", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("extra_cost", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("character_classes", new cEnum(EnumType.CHARACTER_CLASS_COMBINATION, 1023), ParameterVariant.PARAMETER); //CHARACTER_CLASS_COMBINATION
                    break;
                case FunctionType.PathfindingAlienBackstageNode:
                    newEntity.AddParameter("started_animating_Entry", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stopped_animating_Entry", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("started_animating_Exit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("stopped_animating_Exit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("killtrap_anim_started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("killtrap_anim_stopped", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("killtrap_fx_start", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("killtrap_fx_stop", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("open_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("PlayAnimData_Entry", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("PlayAnimData_Exit", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("Killtrap_alien", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("Killtrap_victim", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PLAY_ANIMATION_DATA_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //PLAY_ANIMATION_DATA_RESOURCE
                    newEntity.AddParameter("build_into_navmesh", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("top", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddParameter("extra_cost", new cFloat(100.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("network_id", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.ChokePoint:
                    newEntity.AddResource(ResourceType.CHOKE_POINT_RESOURCE);
                    break;
                case FunctionType.NPC_SetChokePoint:
                    newEntity.AddParameter("chokepoints", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHOKE_POINT_RESOURCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CHOKE_POINT_RESOURCE
                    break;
                case FunctionType.Planet:
                    newEntity.AddParameter("planet_resource", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("parallax_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("sun_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("light_shaft_source_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("parallax_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("planet_sort_key", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("overbright_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("light_wrap_angle_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("penumbra_falloff_power_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("lens_flare_brightness", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("lens_flare_colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("atmosphere_edge_falloff_power", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("atmosphere_edge_transparency", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("atmosphere_scroll_speed", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("atmosphere_detail_scroll_speed", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("override_global_tint", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("global_tint", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("flow_cycle_time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("flow_speed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("flow_tex_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("flow_warp_strength", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("detail_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("normal_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("terrain_uv_scale", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("atmosphere_normal_strength", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("terrain_normal_strength", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("light_shaft_colour", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("light_shaft_range", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("light_shaft_decay", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("light_shaft_min_occlusion_distance", new cFloat(100.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("light_shaft_intensity", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("light_shaft_density", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("light_shaft_source_occlusion", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("blocks_light_shafts", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SpaceTransform:
                    newEntity.AddParameter("affected_geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("yaw_speed", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("pitch_speed", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("roll_speed", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SpaceSuitVisor:
                    newEntity.AddParameter("breath_level", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.NonInteractiveWater:
                    newEntity.AddParameter("water_resource", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("SCALE_X", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SCALE_Z", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SHININESS", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_NORMAL_MAP_STRENGTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CYCLE_TIME", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FRESNEL_POWER", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MIN_FRESNEL", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MAX_FRESNEL", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ENVIRONMENT_MAP_MULT", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ENVMAP_SIZE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ENVMAP_BOXPROJ_BB_SCALE", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("REFLECTION_PERTURBATION_STRENGTH", new cFloat(0.05f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHA_PERTURBATION_STRENGTH", new cFloat(0.05f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ALPHALIGHT_MULT", new cFloat(0.4f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("softness_edge", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FOG_INITIAL_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DEPTH_FOG_INITIAL_ALPHA", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FOG_MIDPOINT_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DEPTH_FOG_MIDPOINT_ALPHA", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FOG_MIDPOINT_DEPTH", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FOG_END_COLOUR", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("DEPTH_FOG_END_ALPHA", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DEPTH_FOG_END_DEPTH", new cFloat(2.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.Refraction:
                    newEntity.AddParameter("refraction_resource", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("SCALE_X", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SCALE_Z", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DISTANCEFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_REFRACTFACTOR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_SPEED", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("SECONDARY_SCALE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("MIN_OCCLUSION_DISTANCE", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("CYCLE_TIME", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_SPEED", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_TEX_SCALE", new cFloat(4.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("FLOW_WARP_STRENGTH", new cFloat(0.5f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FogPlane:
                    newEntity.AddParameter("fog_plane_resource", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("start_distance_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("distance_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("angle_fade_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("linear_height_density_fresnel_power_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("linear_heigth_density_max_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("tint", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("thickness_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("edge_softness_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("diffuse_0_uv_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("diffuse_0_speed_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("diffuse_1_uv_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("diffuse_1_speed_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.PostprocessingSettings:
                    newEntity.AddParameter("intensity", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("priority", new cInteger(100), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("blend_mode", new cEnum(EnumType.BLEND_MODE, 2), ParameterVariant.PARAMETER); //BLEND_MODE
                    break;
                case FunctionType.BloomSettings:
                    newEntity.AddParameter("frame_buffer_scale", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("frame_buffer_offset", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("bloom_scale", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("bloom_gather_exponent", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("bloom_gather_scale", new cFloat(0.04f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.ColourSettings:
                    newEntity.AddParameter("brightness", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("contrast", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("saturation", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("red_tint", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("green_tint", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("blue_tint", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.FlareSettings:
                    newEntity.AddParameter("flareOffset0", new cFloat(-1.2f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareIntensity0", new cFloat(0.05f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareAttenuation0", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareOffset1", new cFloat(-1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareIntensity1", new cFloat(0.15f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareAttenuation1", new cFloat(0.7f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareOffset2", new cFloat(-0.8f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareIntensity2", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareAttenuation2", new cFloat(7.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareOffset3", new cFloat(-0.6f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareIntensity3", new cFloat(0.4f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("flareAttenuation3", new cFloat(1.5f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.HighSpecMotionBlurSettings:
                    newEntity.AddParameter("contribution", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("camera_velocity_scalar", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("camera_velocity_min", new cFloat(1.5f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("camera_velocity_max", new cFloat(3.5f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("object_velocity_scalar", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("object_velocity_min", new cFloat(1.5f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("object_velocity_max", new cFloat(3.5f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("blur_range", new cFloat(16.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.FilmGrainSettings:
                    newEntity.AddParameter("low_lum_amplifier", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("mid_lum_amplifier", new cFloat(0.25f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("high_lum_amplifier", new cFloat(0.4f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("low_lum_range", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("mid_lum_range", new cFloat(0.3f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("high_lum_range", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("noise_texture_scale", new cFloat(4.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("adaptive", new cBool(false), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("adaptation_scalar", new cFloat(3.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("adaptation_time_scalar", new cFloat(0.25f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("unadapted_low_lum_amplifier", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("unadapted_mid_lum_amplifier", new cFloat(0.25f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("unadapted_high_lum_amplifier", new cFloat(0.4f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.VignetteSettings:
                    newEntity.AddParameter("vignette_factor", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("vignette_chromatic_aberration_scale", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.DistortionSettings:
                    newEntity.AddParameter("radial_distort_factor", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("radial_distort_constraint", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("radial_distort_scalar", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.SharpnessSettings:
                    newEntity.AddParameter("local_contrast_factor", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.LensDustSettings:
                    newEntity.AddParameter("DUST_MAX_REFLECTED_BLOOM_INTENSITY", new cFloat(0.02f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DUST_REFLECTED_BLOOM_INTENSITY_SCALAR", new cFloat(0.25f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DUST_MAX_BLOOM_INTENSITY", new cFloat(0.004f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DUST_BLOOM_INTENSITY_SCALAR", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("DUST_THRESHOLD", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.IrawanToneMappingSettings:
                    newEntity.AddParameter("target_device_luminance", new cFloat(6.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("target_device_adaptation", new cFloat(20.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("saccadic_time", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("superbright_adaptation", new cFloat(0.5f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.HableToneMappingSettings:
                    newEntity.AddParameter("shoulder_strength", new cFloat(0.22f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("linear_strength", new cFloat(0.3f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("linear_angle", new cFloat(0.1f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("toe_strength", new cFloat(0.2f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("toe_numerator", new cFloat(0.01f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("toe_denominator", new cFloat(0.3f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("linear_white_point", new cFloat(11.2f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.DayToneMappingSettings:
                    newEntity.AddParameter("black_point", new cFloat(0.00625f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("cross_over_point", new cFloat(0.4f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("white_point", new cFloat(40.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("shoulder_strength", new cFloat(0.95f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("toe_strength", new cFloat(0.15f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("luminance_scale", new cFloat(5.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.LightAdaptationSettings:
                    newEntity.AddParameter("fast_neural_t0", new cFloat(5.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("slow_neural_t0", new cFloat(5.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("pigment_bleaching_t0", new cFloat(20.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("fb_luminance_to_candelas_per_m2", new cFloat(105.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("max_adaptation_lum", new cFloat(20000.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("min_adaptation_lum", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("adaptation_percentile", new cFloat(0.3f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("low_bracket", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("high_bracket", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("adaptation_mechanism", new cEnum(EnumType.LIGHT_ADAPTATION_MECHANISM, 0), ParameterVariant.PARAMETER); //LIGHT_ADAPTATION_MECHANISM
                    break;
                case FunctionType.ColourCorrectionTransition:
                    newEntity.AddParameter("interpolate", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("colour_lut_a", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("colour_lut_b", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("lut_a_contribution", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("lut_b_contribution", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("colour_lut_a_index", new cInteger(-1), ParameterVariant.INTERNAL); //int
                    newEntity.AddParameter("colour_lut_b_index", new cInteger(-1), ParameterVariant.INTERNAL); //int
                    break;
                case FunctionType.ProjectileMotion:
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_pos", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("start_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Current_Position", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("Current_Velocity", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    break;
                case FunctionType.ProjectileMotionComplex:
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("start_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("start_angular_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("flight_time_in_seconds", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("current_position", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("current_velocity", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    newEntity.AddParameter("current_angular_velocity", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    newEntity.AddParameter("current_flight_time_in_seconds", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.SplineDistanceLerp:
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("lerp_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.MoveAlongSpline:
                    newEntity.AddParameter("on_think", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("speed", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.GetSplineLength:
                    newEntity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.GetPointOnSpline:
                    newEntity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("percentage_of_spline", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("Result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.GetClosestPercentOnSpline:
                    newEntity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("pos_to_be_near", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("position_on_spline", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("Result", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("bidirectional", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.GetClosestPointOnSpline:
                    newEntity.AddParameter("spline", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("pos_to_be_near", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("position_on_spline", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("look_ahead_distance", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("unidirectional", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("directional_damping_threshold", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.GetClosestPoint:
                    newEntity.AddParameter("bound_to_closest", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Positions", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("pos_to_be_near", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("position_of_closest", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.GetClosestPointFromSet:
                    newEntity.AddParameter("closest_is_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("closest_is_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("closest_is_3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("closest_is_4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("closest_is_5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("closest_is_6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("closest_is_7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("closest_is_8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("closest_is_9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("closest_is_10", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Position_1", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Position_2", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Position_3", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Position_4", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Position_5", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Position_6", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Position_7", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Position_8", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Position_9", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Position_10", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("pos_to_be_near", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("position_of_closest", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("index_of_closest", new cInteger(), ParameterVariant.OUTPUT); //int
                    break;
                case FunctionType.GetCentrePoint:
                    newEntity.AddParameter("Positions", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("position_of_centre", new cTransform(), ParameterVariant.OUTPUT); //Position
                    break;
                case FunctionType.FogSetting:
                    newEntity.AddParameter("linear_distance", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("max_distance", new cFloat(850.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("linear_density", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("exponential_density", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("near_colour", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("far_colour", new cVector3(), ParameterVariant.INPUT); //Direction
                    break;
                case FunctionType.FullScreenBlurSettings:
                    newEntity.AddParameter("contribution", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.DistortionOverlay:
                    newEntity.AddParameter("intensity", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("time", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("distortion_texture", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("alpha_threshold_enabled", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("threshold_texture", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("range", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("begin_start_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("begin_stop_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("end_start_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("end_stop_time", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FullScreenOverlay:
                    newEntity.AddParameter("overlay_texture", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("threshold_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("threshold_start", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("threshold_stop", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("threshold_range", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("alpha_scalar", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.DepthOfFieldSettings:
                    newEntity.AddParameter("focal_length_mm", new cFloat(75.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("focal_plane_m", new cFloat(2.5f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("fnum", new cFloat(2.8f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("focal_point", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("use_camera_target", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ChromaticAberrations:
                    newEntity.AddParameter("aberration_scalar", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeOutToBlack:
                    newEntity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeOutToBlackTimed:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeOutToWhite:
                    newEntity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeOutToWhiteTimed:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeIn:
                    newEntity.AddParameter("fade_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ScreenFadeInTimed:
                    newEntity.AddParameter("on_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("time", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.BlendLowResFrame:
                    newEntity.AddParameter("blend_value", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CharacterMonitor:
                    newEntity.AddParameter("character", new cFloat(), ParameterVariant.INPUT); //ResourceID
                    break;
                case FunctionType.AreaHitMonitor:
                    newEntity.AddParameter("on_flamer_hit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_shotgun_hit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_pistol_hit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("SpherePos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("SphereRadius", new cFloat(), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.ENT_Debug_Exit_Game:
                    newEntity.AddParameter("FailureText", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("FailureCode", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.StreamingMonitor:
                    newEntity.AddParameter("on_loaded", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.Raycast:
                    newEntity.AddParameter("Obstructed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Unobstructed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("OutOfRange", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("source_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("target_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("max_distance", new cFloat(100.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("hit_object", new cFloat(), ParameterVariant.OUTPUT); //Object
                    newEntity.AddParameter("hit_distance", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("hit_position", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("priority", new cEnum(EnumType.RAYCAST_PRIORITY, 2), ParameterVariant.PARAMETER); //RAYCAST_PRIORITY
                    break;
                case FunctionType.PhysicsApplyImpulse:
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("offset", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("direction", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("force", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("can_damage", new cBool(true), ParameterVariant.INPUT); //bool
                    break;
                case FunctionType.PhysicsApplyVelocity:
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("angular_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("linear_velocity", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("propulsion_velocity", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.PhysicsModifyGravity:
                    newEntity.AddParameter("float_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PhysicsApplyBuoyancy:
                    newEntity.AddParameter("objects", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("water_height", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("water_density", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("water_viscosity", new cFloat(1.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("water_choppiness", new cFloat(0.05f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.AssetSpawner:
                    newEntity.AddParameter("finished_spawning", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("callback_triggered", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("forced_despawn", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("spawn_on_reset", new cBool(false), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("asset", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("spawn_on_load", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("allow_forced_despawn", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("persist_on_callback", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("allow_physics", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ProximityTrigger:
                    newEntity.AddParameter("ignited", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("electrified", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("drenched", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("poisoned", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("fire_spread_rate", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("water_permeate_rate", new cFloat(10.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("electrical_conduction_rate", new cFloat(100.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("gas_diffusion_rate", new cFloat(0.1f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("ignition_range", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("electrical_arc_range", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("water_flow_range", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("gas_dispersion_range", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.CharacterAttachmentNode:
                    newEntity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("character", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    newEntity.AddParameter("attachment", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    newEntity.AddParameter("Node", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER); //CHARACTER_NODE
                    newEntity.AddParameter("AdditiveNode", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER); //CHARACTER_NODE
                    newEntity.AddParameter("AdditiveNodeIntensity", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("UseOffset", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Translation", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("Rotation", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    break;
                case FunctionType.MultipleCharacterAttachmentNode:
                    newEntity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("character_01", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    newEntity.AddParameter("attachment_01", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    newEntity.AddParameter("character_02", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    newEntity.AddParameter("attachment_02", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    newEntity.AddParameter("character_03", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    newEntity.AddParameter("attachment_03", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    newEntity.AddParameter("character_04", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    newEntity.AddParameter("attachment_04", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    newEntity.AddParameter("character_05", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    newEntity.AddParameter("attachment_05", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    newEntity.AddParameter("node", new cEnum(EnumType.CHARACTER_NODE, 1), ParameterVariant.PARAMETER); //CHARACTER_NODE
                    newEntity.AddParameter("use_offset", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("translation", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("rotation", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("is_cinematic", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.AnimatedModelAttachmentNode:
                    newEntity.AddParameter("attach_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    newEntity.AddParameter("animated_model", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("attachment", new cFloat(), ParameterVariant.INPUT); //ReferenceFramePtr
                    newEntity.AddParameter("bone_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("use_offset", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("offset", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.GetCharacterRotationSpeed:
                    newEntity.AddParameter("character", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    newEntity.AddParameter("speed", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.LevelCompletionTargets:
                    newEntity.AddParameter("TargetTime", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("NumDeaths", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("TeamRespawnBonus", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("NoLocalRespawnBonus", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("NoRespawnBonus", new cInteger(), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("GrappleBreakBonus", new cInteger(), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.EnvironmentMap:
                    newEntity.AddParameter("Entities", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Priority", new cInteger(100), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("ColourFactor", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("EmissiveFactor", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("Texture", new cString(""), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("Texture_Index", new cInteger(-1), ParameterVariant.INTERNAL); //int
                    newEntity.AddParameter("environmentmap_index", new cInteger(-1), ParameterVariant.INTERNAL); //int
                    break;
                case FunctionType.Display_Element_On_Map:
                    newEntity.AddParameter("map_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("element_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.Map_Floor_Change:
                    newEntity.AddParameter("floor_name", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.Force_UI_Visibility:
                    newEntity.AddParameter("also_disable_interactions", new cBool(true), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.AddExitObjective:
                    newEntity.AddParameter("marker", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("level_name", new cEnum(EnumType.EXIT_WAYPOINT, 0), ParameterVariant.PARAMETER); //EXIT_WAYPOINT
                    break;
                case FunctionType.SetPrimaryObjective:
                    newEntity.AddParameter("title", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("additional_info", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("title_list", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.OBJECTIVE_ENTRY_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //OBJECTIVE_ENTRY_ID
                    newEntity.AddParameter("additional_info_list", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.OBJECTIVE_ENTRY_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //OBJECTIVE_ENTRY_ID
                    newEntity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.SetSubObjective:
                    newEntity.AddParameter("target_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("title", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("map_description", new cString(" "), ParameterVariant.PARAMETER); //String
                    newEntity.AddParameter("title_list", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.OBJECTIVE_ENTRY_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //OBJECTIVE_ENTRY_ID
                    newEntity.AddParameter("map_description_list", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.OBJECTIVE_ENTRY_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //OBJECTIVE_ENTRY_ID
                    newEntity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("objective_type", new cEnum(EnumType.SUB_OBJECTIVE_TYPE, 0), ParameterVariant.PARAMETER); //SUB_OBJECTIVE_TYPE
                    newEntity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ClearPrimaryObjective:
                    newEntity.AddParameter("clear_all_sub_objectives", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ClearSubObjective:
                    newEntity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.UpdatePrimaryObjective:
                    newEntity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("clear_objective", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UpdateSubObjective:
                    newEntity.AddParameter("slot_number", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("show_message", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("clear_objective", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DebugGraph:
                    newEntity.AddParameter("Inputs", new cFloat(), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("scale", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("duration", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("samples_per_second", new cFloat(), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("auto_scale", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("auto_scroll", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.UnlockAchievement:
                    newEntity.AddParameter("achievement_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ACHIEVEMENT_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //ACHIEVEMENT_ID
                    break;
                case FunctionType.AchievementMonitor:
                    newEntity.AddParameter("achievement_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ACHIEVEMENT_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //ACHIEVEMENT_ID
                    break;
                case FunctionType.AchievementStat:
                    newEntity.AddParameter("achievement_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ACHIEVEMENT_STAT_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //ACHIEVEMENT_STAT_ID
                    break;
                case FunctionType.AchievementUniqueCounter:
                    newEntity.AddParameter("achievement_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.ACHIEVEMENT_STAT_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //ACHIEVEMENT_STAT_ID
                    newEntity.AddParameter("unique_object", new cFloat(), ParameterVariant.PARAMETER); //Object
                    break;
                case FunctionType.SetRichPresence:
                    newEntity.AddParameter("presence_id", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.PRESENCE_ID) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.PARAMETER); //PRESENCE_ID
                    newEntity.AddParameter("mission_number", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.SmokeCylinder:
                    newEntity.AddParameter("pos", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("height", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.SmokeCylinderAttachmentInterface:
                    newEntity.AddParameter("radius", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("height", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.PointTracker:
                    newEntity.AddParameter("origin", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("target", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("target_offset", new cVector3(), ParameterVariant.INPUT); //Direction
                    newEntity.AddParameter("result", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("origin_offset", new cVector3(), ParameterVariant.PARAMETER); //Direction
                    newEntity.AddParameter("max_speed", new cFloat(180.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("damping_factor", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.ThrowingPointOfImpact:
                    newEntity.AddParameter("show_point_of_impact", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("hide_point_of_impact", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Location", new cTransform(), ParameterVariant.OUTPUT); //Position
                    newEntity.AddParameter("Visible", new cBool(), ParameterVariant.OUTPUT); //bool
                    break;
                case FunctionType.VisibilityMaster:
                    newEntity.AddParameter("renderable", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.RENDERABLE_INSTANCE) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //RENDERABLE_INSTANCE
                    newEntity.AddParameter("mastered_by_visibility", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.MotionTrackerMonitor:
                    newEntity.AddParameter("on_motion_sound", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_enter_range_sound", new cFloat(), ParameterVariant.TARGET); //
                    break;
                case FunctionType.GlobalEvent:
                    newEntity.AddParameter("EventValue", new cInteger(1), ParameterVariant.INPUT); //int
                    newEntity.AddParameter("EventName", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.GlobalEventMonitor:
                    newEntity.AddParameter("Event_1", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_2", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_3", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_4", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_5", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_6", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_7", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_8", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_9", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_10", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_11", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_12", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_13", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_14", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_15", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_16", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_17", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_18", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_19", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Event_20", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("EventName", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.GlobalPosition:
                    newEntity.AddParameter("PositionName", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.UpdateGlobalPosition:
                    newEntity.AddParameter("PositionName", new cString(" "), ParameterVariant.PARAMETER); //String
                    break;
                case FunctionType.PlayerLightProbe:
                    newEntity.AddParameter("output", new cVector3(), ParameterVariant.OUTPUT); //Direction
                    newEntity.AddParameter("light_level_for_ai", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("dark_threshold", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("fully_lit_threshold", new cFloat(), ParameterVariant.OUTPUT); //float
                    break;
                case FunctionType.PlayerKilledAllyMonitor:
                    newEntity.AddParameter("ally_killed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("start_on_reset", new cBool(true), ParameterVariant.STATE); //bool
                    break;
                case FunctionType.AILightCurveSettings:
                    newEntity.AddParameter("y0", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("x1", new cFloat(0.25f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("y1", new cFloat(0.3f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("x2", new cFloat(0.6f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("y2", new cFloat(0.8f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("x3", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.InteractiveMovementControl:
                    newEntity.AddParameter("completed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("duration", new cFloat(0.0f), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("start_time", new cFloat(), ParameterVariant.INPUT); //float
                    newEntity.AddParameter("progress_path", new cSpline(), ParameterVariant.INPUT); //SPLINE
                    newEntity.AddParameter("result", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("speed", new cFloat(), ParameterVariant.OUTPUT); //float
                    newEntity.AddParameter("can_go_both_ways", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("use_left_input_stick", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("base_progress_speed", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("movement_threshold", new cFloat(30.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("momentum_damping", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("track_bone_position", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("character_node", new cEnum(EnumType.CHARACTER_NODE, 9), ParameterVariant.PARAMETER); //CHARACTER_NODE
                    newEntity.AddParameter("track_position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    break;
                case FunctionType.PlayForMinDuration:
                    newEntity.AddParameter("timer_expired", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("first_animation_started", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("next_animation", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("all_animations_finished", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("MinDuration", new cFloat(5.0f), ParameterVariant.INPUT); //float
                    break;
                case FunctionType.GCIP_WorldPickup:
                    newEntity.AddParameter("spawn_completed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("pickup_collected", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("Pipe", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Gasoline", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Explosive", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Battery", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Blade", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Gel", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Adhesive", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("BoltGun Ammo", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Revolver Ammo", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Shotgun Ammo", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("BoltGun", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Revolver", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Shotgun", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Flare", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Flamer Fuel", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Flamer", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Scrap", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Torch Battery", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Torch", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Cattleprod Ammo", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("Cattleprod", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("StartOnReset", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("MissionNumber", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.Torch_Control:
                    newEntity.AddParameter("torch_switched_off", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("torch_switched_on", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("character", new cResource(new ResourceReference[] { new ResourceReference(ResourceType.CHARACTER) }.ToList<ResourceReference>(), newEntity.shortGUID), ParameterVariant.INPUT); //CHARACTER
                    break;
                case FunctionType.DoorStatus:
                    newEntity.AddParameter("hacking_difficulty", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    newEntity.AddParameter("gate_type", new cEnum(EnumType.UI_KEYGATE_TYPE, 0), ParameterVariant.PARAMETER); //UI_KEYGATE_TYPE
                    newEntity.AddParameter("has_correct_keycard", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("cutting_tool_level", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("is_locked", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_powered", new cBool(false), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("is_cutting_complete", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DeleteHacking:
                    newEntity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    break;
                case FunctionType.DeleteKeypad:
                    newEntity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    break;
                case FunctionType.DeleteCuttingPanel:
                    newEntity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    break;
                case FunctionType.DeleteBlankPanel:
                    newEntity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    break;
                case FunctionType.DeleteHousing:
                    newEntity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    newEntity.AddParameter("is_door", new cBool(true), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.DeletePullLever:
                    newEntity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    newEntity.AddParameter("lever_type", new cEnum(EnumType.LEVER_TYPE, 0), ParameterVariant.PARAMETER); //LEVER_TYPE
                    break;
                case FunctionType.DeleteRotateLever:
                    newEntity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    newEntity.AddParameter("lever_type", new cEnum(EnumType.LEVER_TYPE, 0), ParameterVariant.PARAMETER); //LEVER_TYPE
                    break;
                case FunctionType.DeleteButtonDisk:
                    newEntity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    newEntity.AddParameter("button_type", new cEnum(EnumType.BUTTON_TYPE, 0), ParameterVariant.PARAMETER); //BUTTON_TYPE
                    break;
                case FunctionType.DeleteButtonKeys:
                    newEntity.AddParameter("door_mechanism", new cEnum(EnumType.DOOR_MECHANISM, 0), ParameterVariant.PARAMETER); //DOOR_MECHANISM
                    newEntity.AddParameter("button_type", new cEnum(EnumType.BUTTON_TYPE, 0), ParameterVariant.PARAMETER); //BUTTON_TYPE
                    break;
                case FunctionType.Interaction:
                    newEntity.AddParameter("on_damaged", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_interrupt", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("on_killed", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("interruptible_on_start", new cBool(false), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.PhysicsSystem:
                    newEntity.AddParameter("system_index", new cInteger(), ParameterVariant.INTERNAL); //int
                    newEntity.AddResource(ResourceType.DYNAMIC_PHYSICS_SYSTEM).startIndex = 0;
                    break;
                case FunctionType.BulletChamber:
                    newEntity.AddParameter("Slot1", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Slot2", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Slot3", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Slot4", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Slot5", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Slot6", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Weapon", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("Geometry", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.PlayerDeathCounter:
                    newEntity.AddParameter("on_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("above_limit", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("filter", new cBool(), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("count", new cInteger(), ParameterVariant.OUTPUT); //int
                    newEntity.AddParameter("Limit", new cInteger(1), ParameterVariant.PARAMETER); //int
                    break;
                case FunctionType.RadiosityIsland:
                    newEntity.AddParameter("composites", new cFloat(), ParameterVariant.INPUT); //Object
                    newEntity.AddParameter("exclusions", new cFloat(), ParameterVariant.INPUT); //Object
                    break;
                case FunctionType.RadiosityProxy:
                    newEntity.AddParameter("position", new cTransform(), ParameterVariant.PARAMETER); //Position
                    newEntity.AddResource(ResourceType.RENDERABLE_INSTANCE);
                    break;
                case FunctionType.LeaderboardWriter:
                    newEntity.AddParameter("time_elapsed", new cFloat(0.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("score", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("level_number", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("grade", new cInteger(5), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("player_character", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("combat", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("stealth", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("improv", new cInteger(0), ParameterVariant.PARAMETER); //int
                    newEntity.AddParameter("star1", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("star2", new cBool(), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("star3", new cBool(), ParameterVariant.PARAMETER); //bool
                    break;
                case FunctionType.ProximityDetector:
                    newEntity.AddParameter("in_proximity", new cFloat(), ParameterVariant.TARGET); //
                    newEntity.AddParameter("filter", new cBool(true), ParameterVariant.INPUT); //bool
                    newEntity.AddParameter("detector_position", new cTransform(), ParameterVariant.INPUT); //Position
                    newEntity.AddParameter("min_distance", new cFloat(0.3f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("max_distance", new cFloat(100.0f), ParameterVariant.PARAMETER); //float
                    newEntity.AddParameter("requires_line_of_sight", new cBool(true), ParameterVariant.PARAMETER); //bool
                    newEntity.AddParameter("proximity_duration", new cFloat(1.0f), ParameterVariant.PARAMETER); //float
                    break;
                case FunctionType.FakeAILightSourceInPlayersHand:
                    newEntity.AddParameter("radius", new cFloat(5.0f), ParameterVariant.PARAMETER); //float
                    break;

            }
        }
    }
}
