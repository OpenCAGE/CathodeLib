using CATHODE.Scripting;
using System;
using System.Collections.Generic;
using System.Text;

namespace CathodeLib
{
    //A collection of common ShortGuids for faster lookup
    internal static class ShortGuids
    {
        public static readonly ShortGuid DYNAMIC_PHYSICS_SYSTEM = ShortGuidUtils.Generate("DYNAMIC_PHYSICS_SYSTEM");
        public static readonly ShortGuid reference = ShortGuidUtils.Generate("reference");
        public static readonly ShortGuid position = ShortGuidUtils.Generate("position");
        public static readonly ShortGuid door_mechanism = ShortGuidUtils.Generate("door_mechanism");
        public static readonly ShortGuid button_type = ShortGuidUtils.Generate("button_type");
        public static readonly ShortGuid lever_type = ShortGuidUtils.Generate("lever_type");
        public static readonly ShortGuid is_door = ShortGuidUtils.Generate("is_door");
        public static readonly ShortGuid filter = ShortGuidUtils.Generate("filter");
        public static readonly ShortGuid Input = ShortGuidUtils.Generate("Input");
        public static readonly ShortGuid LHS = ShortGuidUtils.Generate("LHS");
        public static readonly ShortGuid RHS = ShortGuidUtils.Generate("RHS");
        public static readonly ShortGuid Threshold = ShortGuidUtils.Generate("Threshold");
        public static readonly ShortGuid Min = ShortGuidUtils.Generate("Min");
        public static readonly ShortGuid Max = ShortGuidUtils.Generate("Max");
        public static readonly ShortGuid Value = ShortGuidUtils.Generate("Value");
        public static readonly ShortGuid Initial_Value = ShortGuidUtils.Generate("Initial_Value");
        public static readonly ShortGuid Target_Value = ShortGuidUtils.Generate("Target_Value");
        public static readonly ShortGuid Proportion = ShortGuidUtils.Generate("Proportion");
        public static readonly ShortGuid Numbers = ShortGuidUtils.Generate("Numbers");
        public static readonly ShortGuid bias = ShortGuidUtils.Generate("bias");
        public static readonly ShortGuid amplitude = ShortGuidUtils.Generate("amplitude");
        public static readonly ShortGuid phase = ShortGuidUtils.Generate("phase");
        public static readonly ShortGuid wave_shape = ShortGuidUtils.Generate("wave_shape");
        public static readonly ShortGuid allow = ShortGuidUtils.Generate("allow");
        public static readonly ShortGuid initial_value = ShortGuidUtils.Generate("initial_value");
        public static readonly ShortGuid NextGen = ShortGuidUtils.Generate("NextGen");
        public static readonly ShortGuid Colour = ShortGuidUtils.Generate("Colour");
        public static readonly ShortGuid x = ShortGuidUtils.Generate("x");
        public static readonly ShortGuid y = ShortGuidUtils.Generate("y");
        public static readonly ShortGuid z = ShortGuidUtils.Generate("z");
        public static readonly ShortGuid initial_colour = ShortGuidUtils.Generate("initial_colour");
        public static readonly ShortGuid initial_x = ShortGuidUtils.Generate("initial_x");
        public static readonly ShortGuid initial_y = ShortGuidUtils.Generate("initial_y");
        public static readonly ShortGuid initial_z = ShortGuidUtils.Generate("initial_z");
        public static readonly ShortGuid Normalised = ShortGuidUtils.Generate("Normalised");
        public static readonly ShortGuid MinX = ShortGuidUtils.Generate("MinX");
        public static readonly ShortGuid MaxX = ShortGuidUtils.Generate("MaxX");
        public static readonly ShortGuid MinY = ShortGuidUtils.Generate("MinY");
        public static readonly ShortGuid MaxY = ShortGuidUtils.Generate("MaxY");
        public static readonly ShortGuid MinZ = ShortGuidUtils.Generate("MinZ");
        public static readonly ShortGuid MaxZ = ShortGuidUtils.Generate("MaxZ");
        public static readonly ShortGuid is_template = ShortGuidUtils.Generate("is_template");
        public static readonly ShortGuid deleted = ShortGuidUtils.Generate("deleted");
        public static readonly ShortGuid resource = ShortGuidUtils.Generate("resource");
        public static readonly ShortGuid start_on_reset = ShortGuidUtils.Generate("start_on_reset");
        public static readonly ShortGuid is_shared = ShortGuidUtils.Generate("is_shared");
        public static readonly ShortGuid static_collision = ShortGuidUtils.Generate("static_collision");
        public static readonly ShortGuid delete_me = ShortGuidUtils.Generate("delete_me");
        public static readonly ShortGuid delete_standard_collision = ShortGuidUtils.Generate("delete_standard_collision");
        public static readonly ShortGuid ANIM_TRACK_TYPE = ShortGuidUtils.Generate("ANIM_TRACK_TYPE");
        public static readonly ShortGuid PhysicsSystem = ShortGuidUtils.Generate("PhysicsSystem");
        public static readonly ShortGuid name = ShortGuidUtils.Generate("name");
    }
}
