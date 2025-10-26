namespace CATHODE.ShaderTypes
{
    public enum SHADER_LIST
    {
        CA_RADIOSITY_INDIRECT = 0,
        CA_RADIOSITY_INDIRECT_BOUNCE = 1,
        CA_RADIOSITY_INDIRECT_BLUR = 2,
        CA_RADIOSITY_INDIRECT_SCATTER = 3,
        CA_RADIOSITY_OBJECT_PROBE_INTERP = 4,
        CA_RADIOSITY_DIRECT_SPOT = 5,
        CA_RADIOSITY_DIRECT_SURFACE = 6,
        CA_RADIOSITY_DIRECT_STRIP = 7,
        CA_RADIOSITY_RENDER = 8,
        CA_RADIOSITY_UNMANGLE = 9,
        CA_RADIOSITY_INDIRECT_RESTORE = 10,
        CA_RADIOSITY_DOOR_TRANSFER = 11,
        CA_PARTICLE = 12,
        CA_RIBBON = 13,
        CA_DAMAGE_RENDER_LOCATIONS = 14,
        CA_DAMAGE_DILATE_LOCATIONS = 15,
        CA_DAMAGE_RENDER_DAMAGE = 16,
        CA_ENVIRONMENT = 17,
        CA_SHADOWCASTER = 18,
        CA_DECAL_ENVIRONMENT = 19,
        CA_CHARACTER = 20,
        CA_SKIN = 21,
        CA_HAIR = 22,
        CA_EYE = 23,
        CA_SKIN_OCCLUSION = 24,
        CA_VELOCITY = 25,
        CA_LIGHTPROBE = 26,
        CA_DEFERRED = 27,
        CA_DEFERRED_DEPTH = 28,
        CA_DEFERRED_CONST = 29,
        CA_DECAL = 30,
        CA_FOGPLANE = 31,
        CA_FOGSPHERE = 32,
        CA_DEBUG = 33,
        CA_EFFECT = 34,
        CA_POST_PROCESSING = 35,
        CA_MOTION_BLUR_HI_SPEC = 36,
        CA_FILTERS = 37,
        CA_LENS_FLARE = 38,
        CA_LIQUID_ENVIRONMENT = 39,
        CA_LIQUID_CHARACTER = 40,
        CA_OCCLUSION_TEST = 41,
        CA_OCCLUSION_CULLING = 42,
        CA_REFRACTION = 43,
        CA_SIMPLE_REFRACTION = 44,
        CA_DISTORTION_OVERLAY = 45,
        CA_SKYDOME = 46,
        CA_ALPHALIGHT_POSITION = 47,
        CA_ALPHALIGHT_CLEAR = 48,
        CA_ALPHALIGHT_LIGHT = 49,
        CA_SURFACE_EFFECTS = 50,
        CA_EFFECT_OVERLAY = 51,
        CA_TERRAIN = 52,
        CA_NONINTERACTIVE_WATER = 53,
        CA_SIMPLEWATER = 54,
        CA_PLANET = 55,
        CA_GALAXY = 56,
        CA_DIRECTIONAL_DEFERRED = 57,
        CA_LIGHTMAP_ENVIRONMENT = 58,
        CA_STREAMER = 59,
        CA_LOW_LOD_CHARACTER = 60,
        CA_LIGHT_DECAL = 61,
        CA_VOLUME_LIGHT = 62,
        CA_WATER_CAUSTICS_OVERLAY = 63,
        CA_SPACESUIT_VISOR = 64,
        CA_CAMERA_MAP = 65,
    };

    public enum SHADER_REQUIREMENTS
    {
        PARTICLE = 0,
        DEFERRED_LIGHTING = 1,
        DECAL = 2,
        CONSTANT_COLOUR = 3,
        REFLECTIVE = 4,
        CHARACTER = 5,
        CAMERA_ALIGN = 6,
        ENVIRONMENT_MAP = 7,
        UNUSED_FLAG = 8,
        INFINITE_PROJECTION = 9,
        COLLISION = 10,
        RADIOSITY_STATIC = 11,
        RADIOSITY_DYNAMIC = 12,
        RIBBON = 13,
        APPROXIMATE_LIGHTING = 14,
        RADIOSITY_CUBEMAP = 15,
        OCCLUSION_QUERIED = 16,
        CLOTH = 17,
        CUSTOM_POSITION_ARRAY = 18,
        EXCLUDE_FROM_ZPREFILL = 19,
        DISTORTION = 20,
        CPU = 21,
        BINORMALS_AND_TANGENTS = 22,
        GENERIC = 23,
        FORCE_TO_ALPHA = 24,
        USEASDECAL = 25,
        OCCLUDER = 26,
        OCCLUSION_CULL_VOLUME = 27,
        NEVER_CULL = 28,
        DYNAMIC_DEBUG_COLOUR = 29,
        LOWRES_ALPHA = 30,
        MAIN_CAMERA_ONLY = 31,
        SKIN_BRDF = 32,
        HAIR_BRDF = 33,
        EARLY_ALPHA = 34,
        COMPUTEDIRECTLIGHT = 35,
        SELF_LIT = 36,
        LIGHT_DECAL = 37,
        WRINKLE_MAPPING = 38,
        EMISSIVE = 39,
        ALPHALIGHT_POINT_SAMPLE_LIGHTING = 40,
        VOLUME_LIGHTING = 41,
        PLANET = 42,
        TEXTURE_TRANSFORM = 43,
        DECAL_AFFECTING_SPECULAR_NORMAL = 44,
        SOLID_DECAL = 45,
        POST_ALPHA = 46,
        STREAMER = 47,
        SKIN_OCCLUSION_PASS = 48,
        RECEIVE_SKIN_OCCLUSION = 49,
        FORCE_TO_HI_ALPHA = 50,
        USES_AUTO_TEXTURE_ATLAS = 51,
        USES_COLOUR_SCALAR_CONSTANTS = 52,
        NO_CLIP = 53,
        VISOR = 54,
    };

    public static class CA_RADIOSITY_INDIRECT
    {
        public enum FEATURES
        {
            POINT_LIST = 0,
            FULL_SCREEN_QUAD = 1,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_INDIRECT_BOUNCE
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_INDIRECT_BLUR
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_INDIRECT_SCATTER
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_OBJECT_PROBE_INTERP
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_DIRECT_SPOT
    {
        public enum FEATURES
        {
            RADIOSITY_SPOT_LIGHT = 0,
            RADIOSITY_POINT_LIGHT = 1,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_DIRECT_SURFACE
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_DIRECT_STRIP
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_RENDER
    {
        public enum FEATURES
        {
            RADIOSITY_DEBUG_DISPLAY = 0,
            RADIOSITY_AMBIENT_OCCLUSION = 1,
            RADIOSITY_RENDER_EMISSIVE_ONLY = 2,
            RADIOSITY_RENDER_SKIN_ONLY = 3,
            RADIOSITY_RENDER_CHARACTER_ONLY = 4,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_UNMANGLE
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_INDIRECT_RESTORE
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_RADIOSITY_DOOR_TRANSFER
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_PARTICLE
    {
        public enum FEATURES
        {
            BILLBOARDING_LS = 0,
            LOW_RES = 1,
            EARLY_ALPHA = 2,
            LOOPING = 3,
            ANIMATED_ALPHA = 4,
            X_AXIS_FLIP = 5,
            Y_AXIS_FLIP = 6,
            BILLBOARD_FACING = 7,
            BILLBOARDING_ON_AXIS_FADEOUT = 8,
            BILLBOARDING_CAMERA_LOCKED = 9,
            BILLBOARDING_NONE = 10,
            BILLBOARDING = 11,
            BILLBOARDING_ON_AXIS_X = 12,
            BILLBOARDING_ON_AXIS_Y = 13,
            BILLBOARDING_ON_AXIS_Z = 14,
            BILLBOARDING_VELOCITY_ALIGNED = 15,
            BILLBOARDING_VELOCITY_STRETCHED = 16,
            BILLBOARDING_SPHERE_PROJECTION = 17,
            BLENDING_STANDARD = 18,
            BLENDING_ALPHA_REF = 19,
            BLENDING_ADDITIVE = 20,
            BLENDING_PREMULTIPLIED = 21,
            BLENDING_DISTORTION = 22,
            ALPHA_TEST = 23,
            NONE = 24,
            LIGHTING = 25,
            PER_PARTICLE_LIGHTING = 26,
            CPU = 27,
            CELL_EMISSION = 28,
            CUSTOM_SEED_CPU = 29,
            ZTEST = 30,
            START_MID_END_SPEED = 31,
            LAUNCH_DECELERATE_SPEED = 32,
            EMISSION_AREA = 33,
            EMISSION_SURFACE = 34,
            EMISSION_DIRECTION_SURFACE = 35,
            AREA_CUBOID = 36,
            AREA_SPHEROID = 37,
            AREA_CYLINDER = 38,
            GRAVITY = 39,
            COLOUR_TINT = 40,
            COLOUR_USE_MID = 41,
            SPREAD_FEATURE = 42,
            ROTATION = 43,
            ROTATION_RANDOM_START = 44,
            ROTATION_RAMP = 45,
            FADE_NEAR_CAMERA = 46,
            TEXTURE_ANIMATION = 47,
            RANDOM_START_FRAME = 48,
            WRAP_FRAMES = 49,
            NO_ANIM = 50,
            SUB_FRAME_BLEND = 51,
            SOFTNESS = 52,
            REVERSE_SOFTNESS = 53,
            PIVOT_AND_TURBULENCE = 54,
            ALPHATHRESHOLD = 55,
            COLOUR_RAMP = 56,
            COLOUR_RAMP_ALPHA = 57,
            DEPTH_FADE_AXIS = 58,
            FLOW_UV_ANIMATION = 59,
            INFINITE_PROJECTION = 60,
            DISTORTION_OCCLUSION = 61,
            AMBIENT_LIGHTING = 62,
            NO_CLIP = 63,
        }
        public enum SAMPLERS
        {
            TEXTURE_MAP = 0,
            COLOUR_RAMP_MAP = 1,
            FLOW_MAP = 2,
            FLOW_TEXTURE_MAP = 3,
        }
        public enum PARAMETERS
        {
            DRAW_PASS = 0,
            ASPECT_RATIO = 1,
            FADE_AT_DISTANCE = 2,
            PARTICLE_COUNT = 3,
            SYSTEM_EXPIRY_TIME = 4,
            SIZE_START_MIN = 5,
            SIZE_START_MAX = 6,
            SIZE_END_MIN = 7,
            SIZE_END_MAX = 8,
            ALPHA_IN = 9,
            ALPHA_OUT = 10,
            MASK_AMOUNT_MIN = 11,
            MASK_AMOUNT_MAX = 12,
            MASK_AMOUNT_MIDPOINT = 13,
            PARTICLE_EXPIRY_TIME_MIN = 14,
            PARTICLE_EXPIRY_TIME_MAX = 15,
            COLOUR_SCALE_MIN = 16,
            COLOUR_SCALE_MAX = 17,
            WIND_X = 18,
            WIND_Y = 19,
            WIND_Z = 20,
            ALPHA_REF_VALUE = 21,
            CAMERA_RELATIVE_POS_X = 22,
            CAMERA_RELATIVE_POS_Y = 23,
            CAMERA_RELATIVE_POS_Z = 24,
            SPHERE_PROJECTION_RADIUS = 25,
            DISTORTION_STRENGTH = 26,
            PIVOT_X = 27,
            PIVOT_Y = 28,
            SPAWN_RATE = 29,
            SPAWN_RATE_VAR = 30,
            SPAWN_NUMBER = 31,
            LIFETIME = 32,
            LIFETIME_VAR = 33,
            WORLD_TO_LOCAL_BLEND_START = 34,
            WORLD_TO_LOCAL_BLEND_END = 35,
            WORLD_TO_LOCAL_MAX_DIST = 36,
            CELL_MAX_DIST = 37,
            SEED = 38,
            SPEED_START_MIN = 39,
            SPEED_START_MAX = 40,
            SPEED_MID_MIN = 41,
            SPEED_MID_MAX = 42,
            SPEED_END_MIN = 43,
            SPEED_END_MAX = 44,
            LAUNCH_DECELERATE_SPEED_START_MIN = 45,
            LAUNCH_DECELERATE_SPEED_START_MAX = 46,
            LAUNCH_DECELERATE_DEC_RATE = 47,
            EMISSION_AREA_X = 48,
            EMISSION_AREA_Y = 49,
            EMISSION_AREA_Z = 50,
            GRAVITY_STRENGTH = 51,
            GRAVITY_MAX_STRENGTH = 52,
            COLOUR_TINT_START = 53,
            COLOUR_TINT_END = 54,
            COLOUR_TINT_MID = 55,
            COLOUR_MIDPOINT = 56,
            SPREAD_MIN = 57,
            SPREAD = 58,
            ROTATION_MIN = 59,
            ROTATION_MAX = 60,
            ROTATION_BASE = 61,
            ROTATION_VAR = 62,
            ROTATION_IN = 63,
            ROTATION_OUT = 64,
            ROTATION_DAMP = 65,
            FADE_NEAR_CAMERA_MAX_DIST = 66,
            FADE_NEAR_CAMERA_THRESHOLD = 67,
            TEXTURE_ANIMATION_FRAMES = 68,
            NUM_ROWS = 69,
            TEXTURE_ANIMATION_LOOP_COUNT = 70,
            SOFTNESS_EDGE = 71,
            SOFTNESS_ALPHA_THICKNESS = 72,
            SOFTNESS_ALPHA_DEPTH_MODIFIER = 73,
            REVERSE_SOFTNESS_EDGE = 74,
            PIVOT_OFFSET_MIN = 75,
            PIVOT_OFFSET_MAX = 76,
            TURBULENCE_FREQUENCY_MIN = 77,
            TURBULENCE_FREQUENCY_MAX = 78,
            TURBULENCE_AMOUNT_MIN = 79,
            TURBULENCE_AMOUNT_MAX = 80,
            ALPHATHRESHOLD_TOTALTIME = 81,
            ALPHATHRESHOLD_RANGE = 82,
            ALPHATHRESHOLD_BEGINSTART = 83,
            ALPHATHRESHOLD_BEGINSTOP = 84,
            ALPHATHRESHOLD_ENDSTART = 85,
            ALPHATHRESHOLD_ENDSTOP = 86,
            DEPTH_FADE_AXIS_DIST = 87,
            DEPTH_FADE_AXIS_PERCENT = 88,
            CYCLE_TIME = 89,
            FLOW_SPEED = 90,
            FLOW_TEX_SCALE = 91,
            FLOW_WARP_STRENGTH = 92,
            PARALLAX_POSITION = 93,
            AMBIENT_LIGHTING_COLOUR = 94,
        }
    }

    public static class CA_RIBBON
    {
        public enum FEATURES
        {
            NO_MIPS = 0,
            UV_SQUARED = 1,
            LOW_RES = 2,
            LIGHTING = 3,
            BLENDING_STANDARD = 4,
            BLENDING_ALPHA_REF = 5,
            BLENDING_ADDITIVE = 6,
            BLENDING_PREMULTIPLIED = 7,
            BLENDING_DISTORTION = 8,
            CUSTOM_SEED = 9,
            MULTI_TEXTURE = 10,
            MULTI_TEXTURE_BLEND = 11,
            MULTI_TEXTURE_ADD = 12,
            MULTI_TEXTURE_MULT = 13,
            MULTI_TEXTURE_MAX = 14,
            MULTI_TEXTURE_MIN = 15,
            SECOND_TEXTURE = 16,
            CONTINUOUS = 17,
            TRAILING = 18,
            INSTANT = 19,
            RATE = 20,
            POINT_TO_POINT = 21,
            COLOUR_TINT = 22,
            EDGE_FADE = 23,
            ALPHA_ERODE = 24,
            SIDE_ON_FADE = 25,
            DISTANCE_SCALING = 26,
            SPREAD_FEATURE = 27,
            EMISSION_SURFACE = 28,
            AREA_CUBOID = 29,
            AREA_SPHEROID = 30,
            AREA_CYLINDER = 31,
            SPARK_LIGHT = 32,
            COLOUR_RAMP = 33,
            SOFTNESS = 34,
            DISTORTION_OCCLUSION = 35,
            AMBIENT_LIGHTING = 36,
            NO_CLIP = 37,
        }
        public enum SAMPLERS
        {
            TEXTURE_MAP = 0,
            TEXTURE_MAP2 = 1,
            COLOUR_RAMP_MAP = 2,
        }
        public enum PARAMETERS
        {
            MASK_AMOUNT_MIN = 0,
            MASK_AMOUNT_MAX = 1,
            MASK_AMOUNT_MIDPOINT = 2,
            DRAW_PASS = 3,
            SYSTEM_EXPIRY_TIME = 4,
            LIFETIME = 5,
            SMOOTHED = 6,
            WORLD_TO_LOCAL_BLEND_START = 7,
            WORLD_TO_LOCAL_BLEND_END = 8,
            WORLD_TO_LOCAL_MAX_DIST = 9,
            SEED = 10,
            UV_REPEAT = 11,
            UV_SCROLLSPEED = 12,
            U2_SCALE = 13,
            V2_REPEAT = 14,
            V2_SCROLLSPEED = 15,
            BASE_LOCKED = 16,
            SPAWN_RATE = 17,
            TRAIL_SPAWN_RATE = 18,
            TRAIL_DELAY = 19,
            MAX_TRAILS = 20,
            DENSITY = 21,
            ABS_FADE_IN_0 = 22,
            ABS_FADE_IN_1 = 23,
            GRAVITY_STRENGTH = 24,
            GRAVITY_MAX_STRENGTH = 25,
            DRAG_STRENGTH = 26,
            WIND_X = 27,
            WIND_Y = 28,
            WIND_Z = 29,
            SPEED_START_MIN = 30,
            SPEED_START_MAX = 31,
            WIDTH_START = 32,
            WIDTH_MID = 33,
            WIDTH_END = 34,
            WIDTH_IN = 35,
            WIDTH_OUT = 36,
            COLOUR_SCALE_START = 37,
            COLOUR_SCALE_MID = 38,
            COLOUR_SCALE_END = 39,
            COLOUR_TINT_START = 40,
            COLOUR_TINT_MID = 41,
            COLOUR_TINT_END = 42,
            FADE_IN = 43,
            FADE_OUT = 44,
            SIDE_FADE_START = 45,
            SIDE_FADE_END = 46,
            DIST_SCALE = 47,
            SPREAD_MIN = 48,
            SPREAD = 49,
            EMISSION_AREA_X = 50,
            EMISSION_AREA_Y = 51,
            EMISSION_AREA_Z = 52,
            SOFTNESS_EDGE = 53,
            SOFTNESS_ALPHA_THICKNESS = 54,
            SOFTNESS_ALPHA_DEPTH_MODIFIER = 55,
            AMBIENT_LIGHTING_COLOUR = 56,
        }
    }

    public static class CA_DAMAGE_RENDER_LOCATIONS
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_DAMAGE_DILATE_LOCATIONS
    {
        public enum FEATURES
        {
            HORIZONTAL = 0,
        }
        public enum SAMPLERS
        {
            LOCATION_MAP = 0,
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_DAMAGE_RENDER_DAMAGE
    {
        public enum FEATURES
        {
            DEBUG_VIEW = 0,
        }
        public enum SAMPLERS
        {
            LOCATION_MAP = 0,
            EFFECT_MAP = 1,
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_ENVIRONMENT
    {
        public enum FEATURES
        {
            VERTEX_COLOUR = 0,
            FOG_ALPHA = 1,
            REFLECTIVE_PLASTIC = 2,
            DOUBLE_SIDED = 3,
            USE_ALPHA_AS_BLENDFACTOR = 4,
            FORCE_TO_ALPHA = 6,
            ALPHA_TEST = 7,
            TEXTURE_LOD_BIAS_NONE = 8,
            TEXTURE_LOD_BIAS_SLIGHT = 9,
            TEXTURE_LOD_BIAS_HIGH = 10,
            PLANAR_REFLECTIVE = 11,
            SEPARATE_ALPHA = 12,
            SEPARATE_ALPHA_MAP_USE_GREEN_CHANNEL = 13,
            SIGNED_DISTANCE_FIELD = 14,
            DIFFUSE_MAPPING_PARALLAX = 15,
            SECONDARY_DIFFUSE_MAPPING = 16,
            SECONDARY_DIFFUSE_BLEND_MULTIPLY = 17,
            NORMAL_MAPPING = 18,
            NORMAL_MAPPING_PARALLAX = 19,
            SECONDARY_NORMAL_MAPPING = 20,
            SECONDARY_NORMAL_BLEND_ADD = 21,
            SPECULAR_MAPPING = 22,
            SPECULAR_MAPPING_PARALLAX = 23,
            SECONDARY_SPECULAR_MAPPING = 24,
            SECONDARY_SPECULAR_MAPPING_PARALLAX = 25,
            SECONDARY_SPECULAR_BLEND_MULTIPLY = 26,
            GLASS = 27,
            DIFFUSE_ROUGHNESS = 28,
            FRONT_ROUGHNESS = 29,
            ADDITIVE_ROUGHNESS = 30,
            ENVIRONMENT_MAPPING = 31,
            AMBIENT_OCCLUSION_MAPPING = 32,
            AMBIENT_OCCLUSION_UV = 33,
            VERTEX_AMBIENT_OCCLUSION = 34,
            EMISSIVE = 35,
            DUST_MAPPING = 36,
            DUST_MAPPING_PARALLAX = 37,
            SSR = 38,
            IRRADIANCE_CUBE = 39,
            RADIOSITY_DYNAMIC = 40,
            FUR_RIM_LIGHTING = 41,
            PARALLAX_MAPPING = 42,
            DECAL = 43,
            DECAL_DIFFUSE = 44,
            DECAL_NORMAL = 45,
            DECAL_SPECULAR_EMISSIVE = 46,
            SPECULAR_MAPPING_METALNESS_MASKING = 47,
            ALPHABLEND_NOISE = 48,
            ALPHA_LIGHTING = 49,
            SPARKLE = 50,
            RADIOSITY_STATIC = 51,
            DIRT_MAPPING = 52,
            DIRT_BLEND_MULTIPLY = 53,
            DIRT_MAPPING_PARALLAX = 54,
            WETNESS = 55,
            HI_LOD_CUSTOM_CHARACTER_CORPSE_CONSTANTS = 56,
            NO_CLIP = 57,
            TESSELLATION = 58,
            ORIENTATION_ADAPTIVE_TESSELLATION = 59,
            PHONG_TESSELLATION = 60,
            DISPLACEMENT_MAPPING = 61,
        }
        public enum SAMPLERS
        {
            SEPARATE_ALPHA_MAP = 0,
            DIFFUSE_MAP = 1,
            SECONDARY_DIFFUSE_MAP = 2,
            NORMAL_MAP = 3,
            SECONDARY_NORMAL_MAP = 4,
            SPECULAR_MAP = 5,
            SECONDARY_SPECULAR_MAP = 6,
            ENVIRONMENT_MAP = 7,
            AMBIENT_OCCLUSION_MAP = 8,
            DUST_MAP = 9,
            IRRADIANCE_CUBE_MAP = 10,
            PARALLAX_MAP = 11,
            ALPHABLEND_NOISE_MAP = 12,
            SPARKLE_MAP = 13,
            DIRT_MAP = 14,
            WETNESS_NOISE = 15,
            DISPLACEMENT_MAP = 16,
        }
        public enum PARAMETERS
        {
            SIZE_CULLING_THRESHOLD = 0,
            FORCE_PRIORITY_LEVEL = 1,
            SHIFT_PRIORITY_LEVEL = 2,
            FRESNEL_INTENSITY = 3,
            PLANAR_REFLECTIVE_OVERBRIGHT_SCALAR = 4,
            SEPARATE_ALPHA_UV_MULT = 5,
            DIFFUSE_UV_MULT = 6,
            DIFFUSE_TINT = 7,
            SECONDARY_DIFFUSE_UV_MULT = 8,
            SECONDARY_DIFFUSE_TINT = 9,
            NORMAL_UV_MULT = 10,
            NORMAL_MAP_STRENGTH = 11,
            SECONDARY_NORMAL_UV_MULT = 12,
            SECONDARY_NORMAL_MAP_STRENGTH = 13,
            SPECULAR_TINT = 14,
            SPECULAR_UV_MULT = 15,
            SPECULAR_POWER = 16,
            SECONDARY_SPECULAR_TINT = 17,
            SECONDARY_SPECULAR_UV_MULT = 18,
            SECONDARY_SPECULAR_POWER = 19,
            GLASS_DENSITY = 20,
            GLASS_LIGHTNESS = 21,
            GLASS_TINT = 22,
            DIFFUSE_ROUGHNESS_FACTOR = 23,
            ENVIRONMENT_EMISSIVE_FACTOR = 24,
            ENVIRONMENT_MAP_MULT = 25,
            AO_TINT = 26,
            AMBIENT_OCCLUSION_MAP_MULT = 27,
            VERT_AO_TINT = 28,
            EMISSIVE_MULT = 29,
            EMISSIVE_TINT = 30,
            DUST_UV_MULT = 31,
            DUST_FALLOFF = 32,
            SSR_AMOUNT = 33,
            FUR_RIM_LIGHTING_FACTOR = 34,
            PARALLAX_UV_MULT = 35,
            PARALLAX_SCALE = 36,
            PARALLAX_BIAS = 37,
            OPACITY_MODIFIER_VALUE = 38,
            ALPHABLEND_NOISE_UV_MULT = 39,
            ALPHABLEND_NOISE_POWER = 40,
            SPARKLE_UV_SCALE = 41,
            SPARKLE_NORMAL_BIAS = 42,
            SPARKLE_MULTIPLIER = 43,
            SPARKLE_FADE_START = 44,
            SPARKLE_POWER = 45,
            SPARKLE_THRESHOLD = 46,
            DIRT_BLEND_MULT_SPEC_POWER = 47,
            DIRT_UV_MULT = 48,
            DIRT_AO_AMOUNT = 49,
            WET_LEVEL = 50,
            WETNESS_UV_MULT = 51,
            CUSTOM_TINT_COLOUR = 52,
            TESSELLATION_FACTOR = 53,
            MIN_TESSELLATION_DISTANCE = 54,
            TESSELLATION_RANGE = 55,
            SHAPE_FACTOR = 56,
            DISPLACEMENT_FACTOR = 57,
            DISPLACEMENT_MAP_UV_SCALE = 58,
        }
    }

    public static class CA_SHADOWCASTER
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_DECAL_ENVIRONMENT
    {
        public enum FEATURES
        {
            VERTEX_COLOUR = 0,
            REFLECTIVE_PLASTIC = 1,
            DOUBLE_SIDED = 2,
            USE_ALPHA_AS_BLENDFACTOR = 3,
            FORCE_TO_ALPHA = 5,
            ALPHA_TEST = 6,
            TEXTURE_LOD_BIAS_NONE = 7,
            TEXTURE_LOD_BIAS_SLIGHT = 8,
            TEXTURE_LOD_BIAS_HIGH = 9,
            BEST_FIT_NORMALS = 10,
            DIRT_MAPPING = 11,
            DIRT_BLEND_MULTIPLY = 12,
            DIRT_MAPPING_PARALLAX = 13,
            ALPHABLEND_NOISE = 14,
            SEPARATE_ALPHA = 15,
            SEPARATE_ALPHA_MAP_USE_GREEN_CHANNEL = 16,
            SIGNED_DISTANCE_FIELD = 17,
            DIFFUSE_MAPPING_PARALLAX = 18,
            SECONDARY_DIFFUSE_MAPPING = 19,
            SECONDARY_DIFFUSE_BLEND_MULTIPLY = 20,
            NORMAL_MAPPING = 21,
            NORMAL_MAPPING_PARALLAX = 22,
            SECONDARY_NORMAL_MAPPING = 23,
            SECONDARY_NORMAL_BLEND_ADD = 24,
            SPECULAR_MAPPING = 25,
            SPECULAR_MAPPING_PARALLAX = 26,
            SECONDARY_SPECULAR_MAPPING = 27,
            SECONDARY_SPECULAR_MAPPING_PARALLAX = 28,
            SECONDARY_SPECULAR_BLEND_MULTIPLY = 29,
            GLASS = 30,
            DIFFUSE_ROUGHNESS = 31,
            FRONT_ROUGHNESS = 32,
            ADDITIVE_ROUGHNESS = 33,
            ENVIRONMENT_MAPPING = 34,
            AMBIENT_OCCLUSION_MAPPING = 35,
            AMBIENT_OCCLUSION_UV = 36,
            VERTEX_AMBIENT_OCCLUSION = 37,
            EMISSIVE = 38,
            DUST_MAPPING = 39,
            DUST_MAPPING_PARALLAX = 40,
            SSR = 41,
            IRRADIANCE_CUBE = 42,
            RADIOSITY_DYNAMIC = 43,
            FUR_RIM_LIGHTING = 44,
            PARALLAX_MAPPING = 45,
            DECAL = 46,
            DECAL_DIFFUSE = 47,
            DECAL_NORMAL = 48,
            DECAL_SPECULAR_EMISSIVE = 49,
            RADIOSITY_STATIC = 50,
            ALPHA_LIGHTING = 51,
            TESSELLATION = 52,
            ORIENTATION_ADAPTIVE_TESSELLATION = 53,
            PHONG_TESSELLATION = 54,
            DISPLACEMENT_MAPPING = 55,
            USEASDECAL = 56,
            ALPHATHRESHOLD = 57,
            ALPHATHRESHOLD_EXTRAALPHA = 58,
            COLOUR_LERP = 59,
            COLOUR_RAMP = 60,
        }
        public enum SAMPLERS
        {
            BEST_FIT_NORMAL_LOOKUP = 0,
            DIRT_MAP = 1,
            ALPHABLEND_NOISE_MAP = 2,
            SEPARATE_ALPHA_MAP = 3,
            DIFFUSE_MAP = 4,
            SECONDARY_DIFFUSE_MAP = 5,
            NORMAL_MAP = 6,
            SECONDARY_NORMAL_MAP = 7,
            SPECULAR_MAP = 8,
            SECONDARY_SPECULAR_MAP = 9,
            ENVIRONMENT_MAP = 10,
            AMBIENT_OCCLUSION_MAP = 11,
            DUST_MAP = 12,
            IRRADIANCE_CUBE_MAP = 13,
            PARALLAX_MAP = 14,
            DISPLACEMENT_MAP = 15,
            ALPHATHRESHOLD_MAP = 16,
            COLOUR_RAMP_MAP = 17,
        }
        public enum PARAMETERS
        {
            SIZE_CULLING_THRESHOLD = 0,
            FORCE_PRIORITY_LEVEL = 1,
            SHIFT_PRIORITY_LEVEL = 2,
            FRESNEL_INTENSITY = 3,
            DIRT_BLEND_MULT_SPEC_POWER = 4,
            DIRT_UV_MULT = 5,
            DIRT_AO_AMOUNT = 6,
            ALPHABLEND_NOISE_UV_MULT = 7,
            ALPHABLEND_NOISE_POWER = 8,
            SEPARATE_ALPHA_UV_MULT = 9,
            DIFFUSE_UV_MULT = 10,
            DIFFUSE_TINT = 11,
            SECONDARY_DIFFUSE_UV_MULT = 12,
            SECONDARY_DIFFUSE_TINT = 13,
            NORMAL_UV_MULT = 14,
            NORMAL_MAP_STRENGTH = 15,
            SECONDARY_NORMAL_UV_MULT = 16,
            SECONDARY_NORMAL_MAP_STRENGTH = 17,
            SPECULAR_TINT = 18,
            SPECULAR_UV_MULT = 19,
            SPECULAR_POWER = 20,
            SECONDARY_SPECULAR_TINT = 21,
            SECONDARY_SPECULAR_UV_MULT = 22,
            SECONDARY_SPECULAR_POWER = 23,
            GLASS_DENSITY = 24,
            GLASS_LIGHTNESS = 25,
            GLASS_TINT = 26,
            DIFFUSE_ROUGHNESS_FACTOR = 27,
            ENVIRONMENT_EMISSIVE_FACTOR = 28,
            ENVIRONMENT_MAP_MULT = 29,
            AO_TINT = 30,
            AMBIENT_OCCLUSION_MAP_MULT = 31,
            VERT_AO_TINT = 32,
            EMISSIVE_MULT = 33,
            EMISSIVE_TINT = 34,
            DUST_UV_MULT = 35,
            DUST_FALLOFF = 36,
            SSR_AMOUNT = 37,
            FUR_RIM_LIGHTING_FACTOR = 38,
            PARALLAX_UV_MULT = 39,
            PARALLAX_SCALE = 40,
            PARALLAX_BIAS = 41,
            OPACITY_MODIFIER_VALUE = 42,
            TESSELLATION_FACTOR = 43,
            MIN_TESSELLATION_DISTANCE = 44,
            TESSELLATION_RANGE = 45,
            SHAPE_FACTOR = 46,
            DISPLACEMENT_FACTOR = 47,
            DISPLACEMENT_MAP_UV_SCALE = 48,
            FADE_TOTALTIME = 49,
            ALPHATHRESHOLD_TOTALTIME = 50,
            ALPHATHRESHOLD_RANGE = 51,
            ALPHATHRESHOLD_BEGINSTART = 52,
            ALPHATHRESHOLD_BEGINSTOP = 53,
            ALPHATHRESHOLD_ENDSTART = 54,
            ALPHATHRESHOLD_ENDSTOP = 55,
            COLOUR_START = 56,
            COLOUR_END = 57,
            COLOUR_LERP_POWER = 58,
        }
    }

    public static class CA_CHARACTER
    {
        public enum FEATURES
        {
            BLUR_MASKING = 0,
            DEPTH_ONLY = 1,
            REFLECTIVE_PLASTIC = 2,
            DOUBLE_SIDED = 3,
            USE_ALPHA_AS_BLENDFACTOR = 4,
            VERTEX_COLOUR = 5,
            VERTEX_PROCESSING_GPU_SKINNING = 6,
            VERTEX_PROCESSING_COLLISION_SKINNING = 7,
            VERTEX_PROCESSING_HAVOK_SKINNING = 8,
            FORCE_TO_ALPHA = 10,
            ALPHA_TEST = 11,
            TEXTURE_LOD_BIAS_NONE = 12,
            TEXTURE_LOD_BIAS_SLIGHT = 13,
            TEXTURE_LOD_BIAS_HIGH = 14,
            DETAIL_FADE = 15,
            CUSTOM_CHARACTER = 16,
            CHARACTER_DAMAGE = 17,
            DIRT_MAPPING = 18,
            DIRT_BLEND_MULTIPLY = 19,
            DIRT_MAPPING_PARALLAX = 20,
            ALPHABLEND_NOISE = 21,
            VERTEX_ALPHA_OPACITY_ONLY = 22,
            SEPARATE_ALPHA = 23,
            SEPARATE_ALPHA_MAP_USE_GREEN_CHANNEL = 24,
            SIGNED_DISTANCE_FIELD = 25,
            DIFFUSE_MAPPING_PARALLAX = 26,
            SECONDARY_DIFFUSE_MAPPING = 27,
            SECONDARY_DIFFUSE_BLEND_MULTIPLY = 28,
            NORMAL_MAPPING = 29,
            NORMAL_MAPPING_PARALLAX = 30,
            SECONDARY_NORMAL_MAPPING = 31,
            SECONDARY_NORMAL_BLEND_ADD = 32,
            SPECULAR_MAPPING = 33,
            SPECULAR_MAPPING_PARALLAX = 34,
            SECONDARY_SPECULAR_MAPPING = 35,
            SECONDARY_SPECULAR_MAPPING_PARALLAX = 36,
            SECONDARY_SPECULAR_BLEND_MULTIPLY = 37,
            GLASS = 38,
            DIFFUSE_ROUGHNESS = 39,
            FRONT_ROUGHNESS = 40,
            ADDITIVE_ROUGHNESS = 41,
            ENVIRONMENT_MAPPING = 42,
            AMBIENT_OCCLUSION_MAPPING = 43,
            AMBIENT_OCCLUSION_UV = 44,
            VERTEX_AMBIENT_OCCLUSION = 45,
            EMISSIVE = 46,
            DUST_MAPPING = 47,
            DUST_MAPPING_PARALLAX = 48,
            SSR = 49,
            IRRADIANCE_CUBE = 50,
            RADIOSITY_DYNAMIC = 51,
            FUR_RIM_LIGHTING = 52,
            PARALLAX_MAPPING = 53,
            DECAL = 54,
            DECAL_DIFFUSE = 55,
            DECAL_NORMAL = 56,
            DECAL_SPECULAR_EMISSIVE = 57,
            DECAL_SPECULAR_NORMAL = 58,
            DECAL_SOLID = 59,
            NO_CLIP = 60,
            ANGULAR_OPACITY_RAMP = 61,
        }
        public enum SAMPLERS
        {
            DIRT_MAP = 0,
            ALPHABLEND_NOISE_MAP = 1,
            SEPARATE_ALPHA_MAP = 2,
            DIFFUSE_MAP = 3,
            SECONDARY_DIFFUSE_MAP = 4,
            NORMAL_MAP = 5,
            SECONDARY_NORMAL_MAP = 6,
            SPECULAR_MAP = 7,
            SECONDARY_SPECULAR_MAP = 8,
            ENVIRONMENT_MAP = 9,
            AMBIENT_OCCLUSION_MAP = 10,
            DUST_MAP = 11,
            IRRADIANCE_CUBE_MAP = 12,
            PARALLAX_MAP = 13,
        }
        public enum PARAMETERS
        {
            DRAW_PASS = 0,
            SIZE_CULLING_THRESHOLD = 1,
            FORCE_PRIORITY_LEVEL = 2,
            SHIFT_PRIORITY_LEVEL = 3,
            FRESNEL_INTENSITY = 4,
            DETAIL_FADE_START = 5,
            DETAIL_FADE_END = 6,
            IS_CUSTOM_CHARACTER_DECAL = 7,
            CUSTOM_CHARACTER_TINT_PRIORITY = 8,
            DIRT_BLEND_MULT_SPEC_POWER = 9,
            DIRT_UV_MULT = 10,
            DIRT_AO_AMOUNT = 11,
            ALPHABLEND_NOISE_UV_MULT = 12,
            ALPHABLEND_NOISE_POWER = 13,
            SEPARATE_ALPHA_UV_MULT = 14,
            DIFFUSE_UV_MULT = 15,
            DIFFUSE_TINT = 16,
            SECONDARY_DIFFUSE_UV_MULT = 17,
            SECONDARY_DIFFUSE_TINT = 18,
            NORMAL_UV_MULT = 19,
            NORMAL_MAP_STRENGTH = 20,
            SECONDARY_NORMAL_UV_MULT = 21,
            SECONDARY_NORMAL_MAP_STRENGTH = 22,
            SPECULAR_TINT = 23,
            SPECULAR_UV_MULT = 24,
            SPECULAR_POWER = 25,
            SECONDARY_SPECULAR_TINT = 26,
            SECONDARY_SPECULAR_UV_MULT = 27,
            SECONDARY_SPECULAR_POWER = 28,
            GLASS_DENSITY = 29,
            GLASS_LIGHTNESS = 30,
            GLASS_TINT = 31,
            DIFFUSE_ROUGHNESS_FACTOR = 32,
            ENVIRONMENT_EMISSIVE_FACTOR = 33,
            ENVIRONMENT_MAP_MULT = 34,
            AO_TINT = 35,
            AMBIENT_OCCLUSION_MAP_MULT = 36,
            VERT_AO_TINT = 37,
            EMISSIVE_MULT = 38,
            EMISSIVE_TINT = 39,
            DUST_UV_MULT = 40,
            DUST_FALLOFF = 41,
            SSR_AMOUNT = 42,
            FUR_RIM_LIGHTING_FACTOR = 43,
            PARALLAX_UV_MULT = 44,
            PARALLAX_SCALE = 45,
            PARALLAX_BIAS = 46,
            OPACITY_MODIFIER_VALUE = 47,
            ANGULAR_OPACITY_RAMP_MIN = 48,
            ANGULAR_OPACITY_RAMP_MAX = 49,
        }
    }

    public static class CA_SKIN
    {
        public enum FEATURES
        {
            BLUR_MASKING = 0,
            RECEIVE_SKIN_OCCLUSION = 1,
            TEXTURE_LOD_BIAS_NONE = 3,
            TEXTURE_LOD_BIAS_SLIGHT = 4,
            TEXTURE_LOD_BIAS_HIGH = 5,
            VERTEX_PROCESSING_GPU_SKINNING = 6,
            SECONDARY_DIFFUSE_MAPPING = 7,
            DIFFUSE_ROUGHNESS = 8,
            NORMAL_MAPPING = 9,
            SECONDARY_NORMAL_MAPPING = 10,
            WRINKLE_MAPPING = 11,
            SPECULAR_MAPPING = 12,
            SECONDARY_SPECULAR_MAPPING = 13,
            ENVIRONMENT_MAPPING = 14,
            ENVMAP_LOCK = 15,
            SSR = 16,
            IRRADIANCE_CUBE = 17,
            DIRT_MAPPING = 18,
            DIRT_BLEND_MULTIPLY = 19,
            NO_CLIP = 20,
            CHARACTER_DAMAGE = 21,
        }
        public enum SAMPLERS
        {
            CONVOLVED_BRDF_MAX_HACK = 0,
            DIFFUSE_MAP = 1,
            SECONDARY_DIFFUSE_MAP = 2,
            NORMAL_MAP = 3,
            SECONDARY_NORMAL_MAP = 4,
            WRINKLE_MASK = 5,
            WRINKLE_NORMAL_MAP = 6,
            SPECULAR_MAP = 7,
            SECONDARY_SPECULAR_MAP = 8,
            ENVIRONMENT_MAP = 9,
            IRRADIANCE_CUBE_MAP = 10,
            DIRT_MAP = 11,
            ALPHABLEND_NOISE_MAP = 12,
        }
        public enum PARAMETERS
        {
            DRAW_PASS = 0,
            TRANSMITTANCE_SCALE = 1,
            SUBSURFACE_SCALE = 2,
            BUMP_SCATTERING_AMOUNT = 3,
            DIFFUSE_UV_MULT = 4,
            DIFFUSE_TINT = 5,
            SECONDARY_DIFFUSE_UV_MULT = 6,
            DIFFUSE_ROUGHNESS_FACTOR = 7,
            NORMAL_UV_MULT = 8,
            NORMAL_MAP_STRENGTH_DIFFUSE = 9,
            NORMAL_MAP_STRENGTH_SPEC = 10,
            SECONDARY_NORMAL_UV_MULT = 11,
            SECONDARY_NORMAL_MAP_STRENGTH_DIFFUSE = 12,
            SECONDARY_NORMAL_MAP_STRENGTH_DIFFUSE_NEXTGEN = 13,
            SECONDARY_NORMAL_MAP_STRENGTH_SPEC = 14,
            SECONDARY_SPEC_NORMAL_MASKING_MIN = 15,
            SECONDARY_SPEC_NORMAL_MASKING_MAX = 16,
            SPECULAR_TINT = 41,
            SPECULAR_POWER = 42,
            SPECULAR_UV_MULT = 43,
            SECONDARY_SPECULAR_UV_MULT = 44,
            ENVIRONMENT_MAP_MULT = 45,
            SSR_AMOUNT = 46,
            DIRT_BLEND_MULT_SPEC_POWER = 47,
            DIRT_UV_MULT = 48,
            DIRT_AO_AMOUNT = 49,
            ALPHABLEND_NOISE_UV_MULT = 50,
            ALPHABLEND_NOISE_POWER = 51,
        }
    }

    public static class CA_HAIR
    {
        public enum FEATURES
        {
            VERTEX_COLOUR = 0,
            DEPTH_ONLY = 1,
            TEXTURE_LOD_BIAS_NONE = 2,
            TEXTURE_LOD_BIAS_SLIGHT = 3,
            TEXTURE_LOD_BIAS_HIGH = 4,
            VERTEX_PROCESSING_GPU_SKINNING = 5,
            NO_SKINNING = 6,
            ENVIRONMENT_MAPPING = 7,
            ENVMAP_LOCK = 8,
            IRRADIANCE_CUBE = 9,
            SPECULAR_MAPPING = 10,
            NORMAL_MAPPING = 11,
        }
        public enum SAMPLERS
        {
            FLOW_MAP = 0,
            DIFFUSE_MAP = 1,
            ENVIRONMENT_MAP = 2,
            IRRADIANCE_CUBE_MAP = 3,
            SPECULAR_MAP = 4,
            NORMAL_MAP = 5,
        }
        public enum PARAMETERS
        {
            DRAW_PASS = 0,
            DIFFUSE_UV_MULT = 1,
            DIFFUSE_TINT = 2,
            DIFFUSE_CONTRAST = 3,
            ENVIRONMENT_MAP_MULT = 4,
            SPECULAR_TINT = 5,
            SPECULAR_POWER = 6,
            SPECULAR_UV_MULT = 7,
            NORMAL_UV_MULT = 8,
            NORMAL_MAP_STRENGTH_DIFFUSE = 9,
        }
    }

    public static class CA_EYE
    {
        public enum FEATURES
        {
            BLUR_MASKING = 0,
            VERTEX_PROCESSING_GPU_SKINNING = 1,
            NO_SKINNING = 2,
            NORMAL_MAPPING = 3,
            ENVIRONMENT_MAPPING = 4,
            ENVMAP_LOCK = 5,
            SSR = 6,
            IRRADIANCE_CUBE = 7,
            CHARACTER_DAMAGE = 8,
        }
        public enum SAMPLERS
        {
            CONVOLVED_BRDF_MAX_HACK = 0,
            IRIS_MAP = 1,
            VEINS_MAP = 2,
            SCATTER_MAP = 3,
            NORMAL_MAP = 4,
            ENVIRONMENT_MAP = 5,
            IRRADIANCE_CUBE_MAP = 6,
        }
        public enum PARAMETERS
        {
            DRAW_PASS = 0,
            IRIS_SIZE = 1,
            IRIS_DEPTH = 2,
            IRIS_EDGE_SOFTNESS = 3,
            CORNEA_IOR = 4,
            PUPIL_DILATION = 5,
            SCATTERING_INTENSITY = 6,
            DIFFUSE_TINT_R = 7,
            DIFFUSE_TINT_G = 8,
            DIFFUSE_TINT_B = 9,
            NORMAL_UV_MULT = 10,
            NORMAL_MAP_STRENGTH_SPEC = 11,
            ENVIRONMENT_MAP_MULT = 12,
            SSR_AMOUNT = 13,
        }
    }

    public static class CA_SKIN_OCCLUSION
    {
        public enum FEATURES
        {
            VERTEX_COLOUR = 0,
            VERTEX_PROCESSING_GPU_SKINNING = 1,
            DECAL_DIFFUSE = 2,
        }
        public enum SAMPLERS
        {
            DIFFUSE_MAP = 0,
        }
        public enum PARAMETERS
        {
            DRAW_PASS = 0,
            DEPTH_BIAS = 1,
            DIFFUSE_UV_MULT = 2,
            DIFFUSE_TINT = 3,
        }
    }

    public static class CA_VELOCITY
    {
        public enum FEATURES
        {
            VERTEX_PROCESSING_GPU_SKINNING = 0,
            VERTEX_PROCESSING_COLLISION_SKINNING = 1,
            VERTEX_PROCESSING_HAVOK_SKINNING = 2,
            NON_SKINNED = 3,
            NO_CLIP = 4,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_LIGHTPROBE
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
            ENVIRONMENT_MAP = 0,
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_DEFERRED
    {
        public enum FEATURES
        {
            POINT_LIGHT = 0,
            SPOT_LIGHT = 1,
            SQUARE_LIGHT = 2,
            STRIP_LIGHT = 3,
            SPECULAR = 4,
            DIFFUSE = 5,
            SHADOW_MAP = 6,
            GOBO = 7,
            SOFT_DIFFUSE = 8,
            DIFFUSE_BIAS = 9,
            AREA_LIGHT = 10,
            SKIN_BRDF = 11,
            HAIR_BRDF = 12,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_DEFERRED_DEPTH
    {
        public enum FEATURES
        {
            POINT_LIGHT = 0,
            SPOT_LIGHT = 1,
            STRIP_LIGHT = 2,
            DEPTH_ONLY = 3,
            SQUARE_LIGHT = 4,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_DEFERRED_CONST
    {
        public enum FEATURES
        {
            POINT_LIGHT = 0,
            SPOT_LIGHT = 1,
            SQUARE_LIGHT = 2,
            STRIP_LIGHT = 3,
            CONST_COLOR_OUTPUT = 4,
            DEPTH_ONLY = 5,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_DECAL
    {
        public enum FEATURES
        {
            STRETCH_DETECT = 0,
            SPECULAR_PLASTIC = 1,
            DECAL_DIFFUSE = 2,
            DECAL_NORMAL = 3,
            DECAL_SPECULAR_EMISSIVE = 4,
            DIFFUSE_MAPPING = 5,
            SEPARATE_ALPHA = 6,
            SEPARATE_ALPHA_MAP_USE_GREEN_CHANNEL = 7,
            SEPARATE_ALPHA_MAP_USE_RGB = 8,
            NORMAL_MAPPING = 9,
            NORMAL_MAPPING_EASE = 10,
            GLOW_MAPPING = 11,
            SPECULAR_MAPPING = 12,
            SPECULAR_GLOSS = 13,
            PARALLAX_MAPPING = 14,
            BURNTHROUGH_MAPPING = 15,
            LIQUIFX_MAPPING = 16,
            LIQUIFX_NATURALMOTION = 17,
            LIQUIFX_INFINITE_LOOP = 18,
            ALPHATHRESHOLD = 19,
            ALPHATHRESHOLD_EXTRAALPHA = 20,
            ALPHATHRESHOLD_CLAMP = 21,
            LIQUIFX2_MAPPING = 22,
            LIQUIFX2_NATURALMOTION = 23,
            ENVIRONMENT_MAPPING = 24,
            DECAL_ADD_ENVIRONMENT = 25,
            FRESNEL = 26,
            COLOUR_LERP = 27,
            COLOUR_RAMP = 28,
        }
        public enum SAMPLERS
        {
            DIFFUSE_MAP = 0,
            SEPARATE_ALPHA_MAP = 1,
            NORMAL_MAP = 2,
            GLOW_MAP = 3,
            SPECULAR_MAP = 4,
            PARALLAX_MAP = 5,
            BURNTHROUGH_MAP = 6,
            LIQUIFX_MAP = 7,
            ALPHATHRESHOLD_MAP = 8,
            LIQUIFX2_MAP = 9,
            ENVIRONMENT_MAP = 10,
            COLOUR_RAMP_MAP = 11,
        }
        public enum PARAMETERS
        {
            FADE_TOTALTIME = 0,
            SPECULAR_POWER = 1,
            SPECULAR_LEVEL = 2,
            GLOW_COLOUR = 3,
            DRAW_PASS = 4,
            NORMAL_MAP_EASE_DURATION = 5,
            NORMAL_MAP_MULTIPLY_START = 6,
            NORMAL_MAP_MULTIPLY_END = 7,
            PARALLAX_SCALE = 8,
            PARALLAX_EASE_DURATION = 9,
            BURNTHROUGH_THRESHOLD = 10,
            BURNTHROUGH_DEPTH = 11,
            LIQUIFX_SPEED_0 = 12,
            LIQUIFX_TILE_0 = 13,
            LIQUIFX_SPEED_1 = 14,
            LIQUIFX_TILE_1 = 15,
            LIQUIFX_DURATION = 16,
            ALPHATHRESHOLD_TOTALTIME = 17,
            ALPHATHRESHOLD_RANGE = 18,
            ALPHATHRESHOLD_BEGINSTART = 19,
            ALPHATHRESHOLD_BEGINSTOP = 20,
            ALPHATHRESHOLD_ENDSTART = 21,
            ALPHATHRESHOLD_ENDSTOP = 22,
            ALPHATHRESHOLD_CLAMP_REFERENCE = 23,
            LIQUIFX2_DURATION = 24,
            ENVIRONMENT_MAP_MULT = 25,
            MAXFRESNEL = 26,
            MINFRESNEL = 27,
            FRESNELPOWER = 28,
            COLOUR_START = 29,
            COLOUR_END = 30,
            COLOUR_LERP_POWER = 31,
        }
    }

    public static class CA_FOGPLANE
    {
        public enum FEATURES
        {
            BILLBOARD = 0,
            CONVEX_GEOM = 1,
            ALPHA = 2,
            LOW_RES = 3,
            EARLY_ALPHA = 4,
            START_DISTANT_CLIP = 5,
            DIFFUSE_MAPPING_0 = 6,
            DIFFUSE_MAPPING_1 = 7,
            DIFFUSE_MAPPING_STATIC = 8,
            SOFTNESS = 9,
            DEPTH_INTERSECT_COLOUR = 10,
            LINEAR_HEIGHT_DENSITY = 11,
            FRESNEL_FALLOFF = 12,
            SMOOTH_HEIGHT_DENSITY = 13,
            ALPHA_LIGHTING = 14,
        }
        public enum SAMPLERS
        {
            DIFFUSE_MAP_0 = 0,
            DIFFUSE_MAP_1 = 1,
            DIFFUSE_MAP_STATIC = 2,
        }
        public enum PARAMETERS
        {
            HALF_DIMENSIONS = 0,
            DISTANCE_FADE = 1,
            ANGLE_FADE = 2,
            START_DISTANCE_FADE = 3,
            SOFTNESS_EDGE = 4,
            THICKNESS = 5,
            FRESNEL_POWER = 6,
            HEIGHT_MAX_DENSITY = 7,
            DEPTH_INTERSECT_INITIAL_COLOUR = 8,
            DEPTH_INTERSECT_INITIAL_ALPHA = 9,
            DEPTH_INTERSECT_MIDPOINT_COLOUR = 10,
            DEPTH_INTERSECT_MIDPOINT_ALPHA = 11,
            DEPTH_INTERSECT_MIDPOINT_DEPTH = 12,
            DEPTH_INTERSECT_END_COLOUR = 13,
            DEPTH_INTERSECT_END_ALPHA = 14,
            DEPTH_INTERSECT_END_DEPTH = 15,
            SPEED_0 = 16,
            SCALE_0 = 17,
            SPEED_1 = 18,
            SCALE_1 = 19,
        }
    }

    public static class CA_FOGSPHERE
    {
        public enum FEATURES
        {
            ALPHA = 0,
            LOW_RES_ALPHA = 1,
            EARLY_ALPHA = 2,
            FRESNEL_TERM = 3,
            SCENE_DEPENDANT_DENSITY = 4,
            EXPONENTIAL_DENSITY = 5,
            SOFTNESS = 6,
            ALPHA_LIGHTING = 7,
            DYNAMIC_ALPHA_LIGHTING = 8,
            BLEND_ALPHA_OVER_DISTANCE = 9,
            SECONDARY_BLEND_ALPHA_OVER_DISTANCE = 10,
            CONVEX_GEOM = 11,
            DEPTH_INTERSECT_COLOUR = 12,
            NO_CLIP = 13,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_DEBUG
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
            COLOUR_TINT = 0,
        }
    }

    public static class CA_EFFECT
    {
        public enum FEATURES
        {
            LASER = 0,
        }
        public enum SAMPLERS
        {
            PROFILE_MAP = 0,
            DIFFUSE_MAP_0 = 1,
            DIFFUSE_MAP_1 = 2,
        }
        public enum PARAMETERS
        {
            SPEED_0 = 0,
            SPEED_1 = 1,
            COLOUR_TINT = 2,
            THICKNESS_START = 3,
            THICKNESS_END = 4,
            LENGTH_UV_MULT = 5,
            ANGLE_UV_MULT = 6,
            FADE_INITIAL = 7,
            FADE_BACK_IN = 8,
        }
    }

    public static class CA_POST_PROCESSING
    {
        public enum FEATURES
        {
            LUT_COLOUR_CORRECTION = 0,
            LUT_COLOUR_CORRECTION_BLENDING = 1,
            MOTION_BLUR_LEGACY_DOF = 2,
            COLOUR_MATRIX = 3,
            FILM_GRAIN = 4,
            FULL_SCREEN_BLUR = 5,
            OVERLAY = 6,
            LOW_RES_FRAME_BLENDING = 7,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
            FRAMEBUFFER_SCALAR_OFFSET = 0,
            DISTORTION_RANGES = 1,
            CHROMATIC_ABERRATION_SCALAR = 2,
            LUT_CORRECTION_BLEND = 3,
            MOTION_BLUR_CONTRIBUTION = 4,
            FULL_SCREEN_BLUR_CONTRIBUTION = 5,
            COLOUR_MATRIX0 = 6,
            COLOUR_MATRIX1 = 7,
            COLOUR_MATRIX2 = 8,
            FILM_GRAIN_RANGE_PARAMS = 9,
            FILM_GRAIN_INTENSITY_PARAMS = 10,
            FILM_GRAIN_SCALE_PARAMS = 11,
            VIGNETTE_PARAMS = 12,
            VIGNETTE_CHROMATIC_ABERRATION_SCALAR = 13,
            RADIAL_DISTORT_PARAM = 14,
            RADIAL_DISTORT_CONSTRAINT = 15,
            RADIAL_DISTORT_SCALAR = 16,
            OVERLAY_THRESHOLD_VALUE = 17,
            OVERLAY_THRESHOLD_START = 18,
            OVERLAY_THRESHOLD_STOP = 19,
            OVERLAY_THRESHOLD_RANGE = 20,
            OVERLAY_ALPHA_SCALAR = 21,
            LOW_RES_FRAME_CONTRIBUTION = 22,
        }
    }

    public static class CA_MOTION_BLUR_HI_SPEC
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_FILTERS
    {
        public enum FEATURES
        {
            BLOOM_GATHER = 0,
            BLOOM_MOTION_BLUR_DOWNSAMPLE_2x2 = 1,
            BLOOM_DOWNSAMPLE_2x2 = 2,
            CONTRAST_PRECOMPUTATION = 3,
            CONTRAST_MASK_CONVOLUTION = 4,
            CONTRAST_MASK_BLUR = 5,
            DOWNSAMPLE_BOX_12X1 = 6,
            GAUSSIAN_31_SKIPPING_VERT = 7,
            GAUSSIAN_31_SKIPPING_HORIZ = 8,
            GAUSSIAN_31_SKIPPING_VERT_NEG = 9,
            GAUSSIAN_31_SKIPPING_HORIZ_NEG = 10,
            GAUSSIAN_9_VERT = 11,
            GAUSSIAN_9_VERT_OPT = 12,
            GAUSSIAN_9_HORIZ_OPT = 13,
            GAUSSIAN_9_HORIZ = 14,
            GAUSSIAN_31_VERT = 15,
            GAUSSIAN_31_HORIZ = 16,
            GAUSSIAN_31_VERT_OPT = 17,
            GAUSSIAN_31_HORIZ_OPT = 18,
            REFLECTION_BILATERAL = 19,
            CONSTANT_ALPHA = 20,
            SPECIFY_MIP_LEVEL = 21,
            NO_RENDERTARGET_SCALE_BIAS = 22,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
            FLARE_OFFSETS = 0,
            FLARE_INTENSITY = 1,
            FLARE_ATTENUATION = 2,
            DUST_MAX_REFLECTED_BLOOM_INTENSITY = 3,
            DUST_MAX_BLOOM_INTENSITY = 4,
            DUST_REFLECTED_BLOOM_INTENSITY_SCALAR = 5,
            DUST_BLOOM_INTENSITY_SCALAR = 6,
            DUST_THRESHOLD = 7,
        }
    }

    public static class CA_LENS_FLARE
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_LIQUID_ENVIRONMENT
    {
        public enum FEATURES
        {
            VERTEX_COLOUR = 0,
            NORMAL_MAPPING = 1,
            ENVIRONMENT_MAPPING = 2,
            ALPHA_LIGHTING = 3,
        }
        public enum SAMPLERS
        {
            LIQUIFLOW_DISTORTION_MAP = 0,
            LIQUIFLOW_LINE_NORMAL_MAP = 1,
            LIQUIFLOW_LINE_ALPHA_MAP = 2,
            LIQUIFLOW_DRIP_NORMAL_MAP = 3,
            LIQUIFLOW_DRIP_ALPHA_MAP = 4,
            NORMAL_MAP = 5,
            NORMAL_ALPHA_MAP = 6,
            ENVIRONMENT_MAP = 7,
        }
        public enum PARAMETERS
        {
            LIQUIFLOW_LINE_SPEED = 0,
            LIQUIFLOW_SMALL_SPEED = 1,
            LIQUIFLOW_LARGE_SPEED = 2,
            LIQUIFLOW_SMALL_DISTORTION_SCALE = 3,
            LIQUIFLOW_SMALL_DISTORTION_AMOUNT = 4,
            LIQUIFLOW_LARGE_DISTORTION_SCALE = 5,
            LIQUIFLOW_LARGE_DISTORTION_AMOUNT = 6,
            LIQUIFLOW_DRIP_SPEED_VERTICAL = 7,
            LIQUIFLOW_DRIP_SPEED_HORIZONTAL = 8,
            LIQUIFLOW_DRIP_DISTORTION_SCALE = 9,
            LIQUIFLOW_DRIP_DISTORTION_AMOUNT = 10,
            LIQUIFLOW_TRANSPARENCY = 11,
            LIQUIFLOW_REFLECTION_AMOUNT = 12,
            LIQUIFLOW_COLOUR_THICK = 13,
            LIQUIFLOW_COLOUR_THIN = 14,
            ENVIRONMENT_MAP_MULT = 15,
        }
    }

    public static class CA_LIQUID_CHARACTER
    {
        public enum FEATURES
        {
            VERTEX_PROCESSING_GPU_SKINNING = 0,
            VERTEX_COLOUR = 1,
            NORMAL_MAPPING = 2,
            ENVIRONMENT_MAPPING = 3,
            ALPHA_LIGHTING = 4,
        }
        public enum SAMPLERS
        {
            LIQUIFLOW_DISTORTION_MAP = 1,
            LIQUIFLOW_LINE_NORMAL_MAP = 2,
            LIQUIFLOW_LINE_ALPHA_MAP = 3,
            LIQUIFLOW_DRIP_NORMAL_MAP = 4,
            LIQUIFLOW_DRIP_ALPHA_MAP = 5,
            NORMAL_MAP = 6,
            NORMAL_ALPHA_MAP = 7,
            ENVIRONMENT_MAP = 8,
        }
        public enum PARAMETERS
        {
            DRAW_PASS = 0,
            LIQUIFLOW_LINE_SPEED = 1,
            LIQUIFLOW_SMALL_SPEED = 2,
            LIQUIFLOW_LARGE_SPEED = 3,
            LIQUIFLOW_SMALL_DISTORTION_SCALE = 4,
            LIQUIFLOW_SMALL_DISTORTION_AMOUNT = 5,
            LIQUIFLOW_LARGE_DISTORTION_SCALE = 6,
            LIQUIFLOW_LARGE_DISTORTION_AMOUNT = 7,
            LIQUIFLOW_DRIP_SPEED_VERTICAL = 8,
            LIQUIFLOW_DRIP_SPEED_HORIZONTAL = 9,
            LIQUIFLOW_DRIP_DISTORTION_SCALE = 10,
            LIQUIFLOW_DRIP_DISTORTION_AMOUNT = 11,
            LIQUIFLOW_TRANSPARENCY = 12,
            LIQUIFLOW_REFLECTION_AMOUNT = 13,
            LIQUIFLOW_COLOUR_THICK = 14,
            LIQUIFLOW_COLOUR_THIN = 15,
            ENVIRONMENT_MAP_MULT = 16,
        }
    }

    public static class CA_OCCLUSION_TEST
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_OCCLUSION_CULLING
    {
        public enum FEATURES
        {
            OCCLUDER = 0,
            OCCLUDEE = 1,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_REFRACTION
    {
        public enum FEATURES
        {
            DOUBLE_SIDED = 0,
            SECONDARY_NORMAL_MAPPING = 1,
            ALPHA_MASKING = 2,
            DISTORTION_OCCLUSION = 3,
            FLOW_UV_ANIMATION = 4,
            USEASDECAL = 5,
            ALPHATHRESHOLD = 6,
            ALPHATHRESHOLD_EXTRAALPHA = 7,
            COLOUR_LERP = 8,
            COLOUR_RAMP = 9,
        }
        public enum SAMPLERS
        {
            NORMAL_MAP = 0,
            SECONDARY_NORMAL_MAP = 1,
            ALPHA_MASK = 2,
            FLOW_MAP = 3,
            ALPHATHRESHOLD_MAP = 4,
            COLOUR_RAMP_MAP = 5,
        }
        public enum PARAMETERS
        {
            REFRACTFACTOR = 0,
            DISTANCEFACTOR = 1,
            SPEED = 2,
            SCALE = 3,
            SECONDARY_SPEED = 4,
            SECONDARY_SCALE = 5,
            SECONDARY_REFRACTFACTOR = 6,
            DISTORTION_RANGES = 7,
            MIN_OCCLUSION_DISTANCE = 8,
            CYCLE_TIME = 9,
            FLOW_SPEED = 10,
            FLOW_TEX_SCALE = 11,
            FLOW_WARP_STRENGTH = 12,
            FADE_TOTALTIME = 13,
            ALPHATHRESHOLD_TOTALTIME = 14,
            ALPHATHRESHOLD_RANGE = 15,
            ALPHATHRESHOLD_BEGINSTART = 16,
            ALPHATHRESHOLD_BEGINSTOP = 17,
            ALPHATHRESHOLD_ENDSTART = 18,
            ALPHATHRESHOLD_ENDSTOP = 19,
            COLOUR_START = 20,
            COLOUR_END = 21,
            COLOUR_LERP_POWER = 22,
        }
    }

    public static class CA_SIMPLE_REFRACTION
    {
        public enum FEATURES
        {
            DOUBLE_SIDED = 0,
            SECONDARY_NORMAL_MAPPING = 1,
            ALPHA_MASKING = 2,
            DISTORTION_OCCLUSION = 3,
            FLOW_UV_ANIMATION = 4,
        }
        public enum SAMPLERS
        {
            NORMAL_MAP = 0,
            SECONDARY_NORMAL_MAP = 1,
            ALPHA_MASK = 2,
            FLOW_MAP = 3,
        }
        public enum PARAMETERS
        {
            REFRACTFACTOR = 0,
            DISTANCEFACTOR = 1,
            SPEED = 2,
            SCALE = 3,
            SECONDARY_SPEED = 4,
            SECONDARY_SCALE = 5,
            SECONDARY_REFRACTFACTOR = 6,
            DISTORTION_RANGES = 7,
            MIN_OCCLUSION_DISTANCE = 8,
            CYCLE_TIME = 9,
            FLOW_SPEED = 10,
            FLOW_TEX_SCALE = 11,
            FLOW_WARP_STRENGTH = 12,
        }
    }

    public static class CA_DISTORTION_OVERLAY
    {
        public enum FEATURES
        {
            ALPHATHRESHOLD = 0,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
            BLEND_SCALAR = 0,
            TIME = 1,
            ALPHATHRESHOLD_TOTALTIME = 2,
            ALPHATHRESHOLD_RANGE = 3,
            ALPHATHRESHOLD_BEGINSTART = 4,
            ALPHATHRESHOLD_BEGINSTOP = 5,
            ALPHATHRESHOLD_ENDSTART = 6,
            ALPHATHRESHOLD_ENDSTOP = 7,
        }
    }

    public static class CA_SKYDOME
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
            SKYDOME_MAP = 0,
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_ALPHALIGHT_POSITION
    {
        public enum FEATURES
        {
            PARTICLE = 0,
            ENTITY = 1,
            POINT_SAMPLE = 2,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_ALPHALIGHT_CLEAR
    {
        public enum FEATURES
        {
            POINT_SAMPLE = 0,
            DEBUG_TEXCOORDS = 1,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_ALPHALIGHT_LIGHT
    {
        public enum FEATURES
        {
            POINT_LIGHT = 0,
            SPOT_LIGHT = 1,
            SQUARE_LIGHT = 2,
            STRIP_LIGHT = 3,
            SHADOW_MAP = 4,
            GOBO = 5,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_SURFACE_EFFECTS
    {
        public enum FEATURES
        {
            REFLECTIVE_PLASTIC = 0,
            DOUBLE_SIDED = 1,
            USE_ALPHA_AS_BLENDFACTOR = 2,
            FORCE_TO_ALPHA = 4,
            ALPHA_TEST = 5,
            TEXTURE_LOD_BIAS_NONE = 6,
            TEXTURE_LOD_BIAS_SLIGHT = 7,
            TEXTURE_LOD_BIAS_HIGH = 8,
            DIFFUSE_MAPPING_PARALLAX = 9,
            NORMAL_MAPPING = 10,
            NORMAL_MAPPING_PARALLAX = 11,
            SPECULAR_MAPPING = 12,
            SPECULAR_GLOSS = 13,
            MATERIAL_MAP = 14,
            SPECULAR_MAPPING_PARALLAX = 15,
            EMISSIVE = 16,
            PARALLAX_MAPPING = 17,
            FROST_MAPPING = 18,
            FROST_MAPPING_PARALLAX = 19,
            ENVIRONMENT_MAPPING = 20,
            ENVMAP_LOCK = 21,
            GLASS = 22,
            DEPTH_COLOURING = 23,
            FRONT_ROUGHNESS = 24,
            RIM_LIGHTING = 25,
            WRAP_NORMALS = 26,
            SPARKLE = 27,
            RADIOSITY_STATIC = 28,
            RADIOSITY_DYNAMIC = 29,
            ALPHA_LIGHTING = 30,
        }
        public enum SAMPLERS
        {
            DIFFUSE_MAP = 0,
            NORMAL_MAP = 1,
            SPECULAR_MAP = 2,
            PARALLAX_MAP = 3,
            FROST_MAP = 4,
            ENVIRONMENT_MAP = 5,
            SPARKLE_MAP = 6,
        }
        public enum PARAMETERS
        {
            SIZE_CULLING_THRESHOLD = 0,
            FORCE_PRIORITY_LEVEL = 1,
            SHIFT_PRIORITY_LEVEL = 2,
            FRESNEL_INTENSITY = 3,
            DIFFUSE_UV_MULT = 4,
            DIFFUSE_TINT = 5,
            NORMAL_UV_MULT = 6,
            NORMAL_MAP_STRENGTH = 7,
            SPECULAR_TINT = 8,
            SPECULAR_UV_MULT = 9,
            SPECULAR_POWER = 10,
            EMISSIVE_MULT = 11,
            EMISSIVE_TINT = 12,
            PARALLAX_SCALE = 13,
            PARALLAX_BIAS = 14,
            FROST_UV_MULT = 15,
            FROST_FALLOFF = 16,
            ENVIRONMENT_MAP_MULT = 17,
            MAXFRESNEL = 18,
            MINFRESNEL = 19,
            FRESNELPOWER = 20,
            DEPTH_COLOUR = 21,
            DIFFUSE_ROUGHNESS_FACTOR = 22,
            RIM_LIGHTING_FACTOR = 23,
            RIM_LIGHTING_COLOUR = 24,
            WRAP_NORMALS_FACTOR = 25,
            SPARKLE_UV_SCALE = 26,
            SPARKLE_NORMAL_BIAS = 27,
            SPARKLE_MULTIPLIER = 28,
            SPARKLE_FADE_START = 29,
            SPARKLE_POWER = 30,
            SPARKLE_THRESHOLD = 31,
        }
    }

    public static class CA_EFFECT_OVERLAY
    {
        public enum FEATURES
        {
            WS_LOCKED = 0,
            SPHERE = 1,
            BOX = 2,
            FULLSCREEN = 3,
            ENVMAP = 4,
        }
        public enum SAMPLERS
        {
            TEXTURE_MAP = 0,
            SPARKLE_MAP = 1,
            ENVIRONMENT_MAP = 2,
        }
        public enum PARAMETERS
        {
            COLOUR_TINT = 0,
            COLOUR_TINT_OUTER = 1,
            INTENSITY = 2,
            OPACITY = 3,
            SURFACE_WRAP = 4,
            ROUGHNESS_SCALE = 5,
            SPARKLE_SCALE = 6,
            METAL_STYLE_REFLECTIONS = 7,
            SHININESS_OPACITY = 8,
            FADE_TOTALTIME = 9,
            TILING_ZY = 10,
            TILING_ZX = 11,
            TILING_XY = 12,
            RADIUS = 13,
            FALLOFF = 14,
            HALF_DIMENSIONS = 15,
            ENVMAP_PERCENT_EMISSIVE = 16,
        }
    }

    public static class CA_TERRAIN
    {
        public enum FEATURES
        {
            RADIOSITY_STATIC = 0,
            RADIOSITY_DYNAMIC = 1,
            SECONDARY_DIFFUSE_MAPPING = 2,
            NORMAL_MAPPING = 3,
            SECONDARY_NORMAL_MAPPING = 4,
            SPECULAR_MAPPING = 5,
            SECONDARY_SPECULAR_MAPPING = 6,
            PARALLAX_MAPPING = 7,
            ALPHABLEND_NOISE = 8,
            ENVIRONMENT_MAPPING = 9,
            AMBIENT_LIGHTING = 10,
            LIGHTMAP = 11,
            IRRADIANCE_CUBE = 12,
        }
        public enum SAMPLERS
        {
            DIFFUSE_MAP = 0,
            SECONDARY_DIFFUSE_MAP = 1,
            NORMAL_MAP = 2,
            SECONDARY_NORMAL_MAP = 3,
            SPECULAR_MAP = 4,
            SECONDARY_SPECULAR_MAP = 5,
            PARALLAX_MAP = 6,
            ALPHABLEND_NOISE_MAP = 7,
            ENVIRONMENT_MAP = 8,
            LIGHTMAP_MAP = 9,
            IRRADIANCE_CUBE_MAP = 10,
        }
        public enum PARAMETERS
        {
            FORCE_PRIORITY_LEVEL = 0,
            SHIFT_PRIORITY_LEVEL = 1,
            FRESNEL_INTENSITY = 2,
            DIFFUSE_UV_MULT = 3,
            DIFFUSE_TINT = 4,
            SECONDARY_DIFFUSE_UV_MULT = 5,
            SECONDARY_DIFFUSE_TINT = 6,
            NORMAL_UV_MULT = 7,
            SECONDARY_NORMAL_UV_MULT = 8,
            SPECULAR_TINT = 9,
            SPECULAR_UV_MULT = 10,
            SPECULAR_GLOSS = 11,
            SECONDARY_SPECULAR_TINT = 12,
            SECONDARY_SPECULAR_UV_MULT = 13,
            SECONDARY_SPECULAR_GLOSS = 14,
            PARALLAX_UV_MULT = 15,
            PARALLAX_SCALE = 16,
            PARALLAX_BIAS = 17,
            ALPHABLEND_NOISE_UV_MULT = 18,
            ENVIRONMENT_MAP_MULT = 19,
            ENVIRONMENT_LIGHTING_MULT = 20,
            DIFFUSE_AMBIENT = 21,
            LIGHTMAP_UV_MULT = 22,
            LIGHTMAP_SCALE = 23,
        }
    }

    public static class CA_NONINTERACTIVE_WATER
    {
        public enum FEATURES
        {
            LOW_RES_ALPHA_PASS = 0,
            SECONDARY_NORMAL_MAPPING = 1,
            ALPHA_MASKING = 2,
            FLOW_UV_ANIMATION = 3,
            ENVIRONMENT_MAPPING = 4,
            LOCALISED_ENVIRONMENT_MAPPING = 5,
            LOCALISED_ENVMAP_BOX_PROJECTION = 6,
            REFLECTIVE_MAPPING = 7,
            ALPHA_LIGHTING = 8,
            ATMOSPHERIC_FOGGING = 9,
        }
        public enum SAMPLERS
        {
            NORMAL_MAP = 0,
            SECONDARY_NORMAL_MAP = 1,
            ALPHA_MASK = 2,
            FLOW_MAP = 3,
            ENVIRONMENT_MAP = 4,
        }
        public enum PARAMETERS
        {
            SHININESS = 0,
            DEPTH_FOG_INITIAL_COLOUR = 1,
            DEPTH_FOG_INITIAL_ALPHA = 2,
            DEPTH_FOG_MIDPOINT_COLOUR = 3,
            DEPTH_FOG_MIDPOINT_ALPHA = 4,
            DEPTH_FOG_MIDPOINT_DEPTH = 5,
            DEPTH_FOG_END_COLOUR = 6,
            DEPTH_FOG_END_ALPHA = 7,
            DEPTH_FOG_END_DEPTH = 8,
            SPEED = 9,
            SCALE = 10,
            NORMAL_MAP_STRENGTH = 11,
            SECONDARY_SPEED = 12,
            SECONDARY_SCALE = 13,
            SECONDARY_NORMAL_MAP_STRENGTH = 14,
            CYCLE_TIME = 15,
            FLOW_SPEED = 16,
            FLOW_TEX_SCALE = 17,
            FLOW_WARP_STRENGTH = 18,
            FRESNEL_POWER = 19,
            MIN_FRESNEL = 20,
            MAX_FRESNEL = 21,
            ENVIRONMENT_MAP_MULT = 22,
            ENVMAP_SIZE = 23,
            ENVMAP_BOXPROJ_BB_X = 24,
            ENVMAP_BOXPROJ_BB_Y = 25,
            ENVMAP_BOXPROJ_BB_Z = 26,
            REFLECTION_PERTURBATION_STRENGTH = 27,
            ALPHA_PERTURBATION_STRENGTH = 28,
            ALPHALIGHT_MULT = 29,
        }
    }

    public static class CA_SIMPLEWATER
    {
        public enum FEATURES
        {
            LOW_RES_ALPHA_PASS = 0,
            SECONDARY_NORMAL_MAPPING = 1,
            ALPHA_MASKING = 2,
            FLOW_UV_ANIMATION = 3,
            ENVIRONMENT_MAPPING = 4,
            LOCALISED_ENVIRONMENT_MAPPING = 5,
            LOCALISED_ENVMAP_BOX_PROJECTION = 6,
            REFLECTIVE_MAPPING = 7,
            ATMOSPHERIC_FOGGING = 8,
        }
        public enum SAMPLERS
        {
            NORMAL_MAP = 0,
            ALPHA_MASK = 1,
            FLOW_MAP = 2,
            ENVIRONMENT_MAP = 3,
        }
        public enum PARAMETERS
        {
            SHININESS = 0,
            DEPTH_FOG_INITIAL_COLOUR = 1,
            DEPTH_FOG_INITIAL_ALPHA = 2,
            DEPTH_FOG_MIDPOINT_COLOUR = 3,
            DEPTH_FOG_MIDPOINT_ALPHA = 4,
            DEPTH_FOG_MIDPOINT_DEPTH = 5,
            DEPTH_FOG_END_COLOUR = 6,
            DEPTH_FOG_END_ALPHA = 7,
            DEPTH_FOG_END_DEPTH = 8,
            REFLECTION_PERTURBATION_STRENGTH = 9,
            SPEED = 10,
            SCALE = 11,
            NORMAL_MAP_STRENGTH = 12,
            SECONDARY_SPEED = 13,
            SECONDARY_SCALE = 14,
            SECONDARY_NORMAL_MAP_STRENGTH = 15,
            CYCLE_TIME = 16,
            FLOW_SPEED = 17,
            FLOW_TEX_SCALE = 18,
            FLOW_WARP_STRENGTH = 19,
            FRESNEL_POWER = 20,
            MIN_FRESNEL = 21,
            MAX_FRESNEL = 22,
            ENVIRONMENT_MAP_MULT = 23,
            ENVMAP_SIZE = 24,
            ENVMAP_BOXPROJ_BB_X = 25,
            ENVMAP_BOXPROJ_BB_Y = 26,
            ENVMAP_BOXPROJ_BB_Z = 27,
        }
    }

    public static class CA_PLANET
    {
        public enum FEATURES
        {
            VERTEX_COLOUR = 0,
            OVERBRIGHT = 1,
            DETAIL_MAPPING = 2,
            ATMOSPHERE_NORMAL_MAPPING = 3,
            TERRAIN_MAPPING = 4,
            TERRAIN_NORMAL_MAPPING = 5,
            SCROLLING_UV = 6,
            DETAIL_SCROLLING_UV = 7,
            FLOW_UV_ANIMATION = 8,
            ATMOSPHERE_RIM = 9,
            LIGHT_WRAPPING = 10,
            PENUMBRA_FALLOFF = 11,
            SHADOW_COLOURISATION = 12,
            GLOBAL_TINT = 13,
            DEPTH_ONLY = 14,
            ZFUNC_DISABLED = 15,
            MASK = 16,
        }
        public enum SAMPLERS
        {
            ATMOSPHERE_MAP = 0,
            DETAIL_MAP = 1,
            ATMOSPHERE_NORMAL_MAP = 2,
            TERRAIN_MAP = 3,
            TERRAIN_NORMAL_MAP = 4,
            FLOW_MAP = 5,
        }
        public enum PARAMETERS
        {
            ATMOSPHERE_RIM_TRANSPARENCY = 0,
            OVERBRIGHT_SCALAR = 1,
            DETAIL_TEX_SCALAR = 2,
            ATMOSPHERE_NORMAL_MAP_SCALAR = 3,
            ATMOSPHERE_NORMAL_MAP_STRENGTH = 4,
            TERRAIN_MAP_UV_SCALE = 5,
            TERRAIN_MAP_SPECULAR_LEVEL = 6,
            TERRAIN_MAP_SPECULAR_POWER = 7,
            TERRAIN_NORMAL_MAP_STRENGTH = 8,
            SCROLL_SPEED = 9,
            DETAIL_SCROLL_SPEED = 10,
            CYCLE_TIME = 11,
            FLOW_SPEED = 12,
            FLOW_TEX_SCALE = 13,
            FLOW_WARP_STRENGTH = 14,
            ATMOSPHERE_RIM_FRESNEL = 15,
            ATMOSPHERE_RIM_COLOUR = 16,
            ATMOSPHERE_RIM_BRIGHTNESS = 17,
            HORIZON_BOOSTER = 18,
            UNLIT_ATMOSPHERE_RIM_COLOUR = 19,
            UNLIT_ATMOSPHERE_RIM_BRIGHTNESS = 20,
            LIGHT_WRAP_ANGLE = 21,
            PENUMBRA_FALLOFF_POWER = 22,
            SHADOW_HUE = 23,
            GLOBAL_TINT_VALUE = 24,
        }
    }

    public static class CA_GALAXY
    {
        public enum FEATURES
        {
            TRILIST_VERTS = 0,
            POINTSPRITE_VERTS = 1,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_DIRECTIONAL_DEFERRED
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_LIGHTMAP_ENVIRONMENT
    {
        public enum FEATURES
        {
            VERTEX_COLOUR = 0,
            REFLECTIVE_PLASTIC = 1,
            DOUBLE_SIDED = 2,
            USE_ALPHA_AS_BLENDFACTOR = 3,
            FORCE_TO_ALPHA = 5,
            ALPHA_TEST = 6,
            TEXTURE_LOD_BIAS_NONE = 7,
            TEXTURE_LOD_BIAS_SLIGHT = 8,
            TEXTURE_LOD_BIAS_HIGH = 9,
            COMPUTEDIRECTLIGHT = 10,
            BEST_FIT_NORMALS = 11,
            DIRT_MAPPING = 12,
            DIRT_BLEND_MULTIPLY = 13,
            DIRT_MAPPING_PARALLAX = 14,
            ALPHABLEND_NOISE = 15,
            SEPARATE_ALPHA = 16,
            SEPARATE_ALPHA_MAP_USE_GREEN_CHANNEL = 17,
            SIGNED_DISTANCE_FIELD = 18,
            DIFFUSE_MAPPING_PARALLAX = 19,
            SECONDARY_DIFFUSE_MAPPING = 20,
            SECONDARY_DIFFUSE_BLEND_MULTIPLY = 21,
            NORMAL_MAPPING = 22,
            NORMAL_MAPPING_PARALLAX = 23,
            SECONDARY_NORMAL_MAPPING = 24,
            SECONDARY_NORMAL_BLEND_ADD = 25,
            SPECULAR_MAPPING = 26,
            SPECULAR_MAPPING_PARALLAX = 27,
            SECONDARY_SPECULAR_MAPPING = 28,
            SECONDARY_SPECULAR_MAPPING_PARALLAX = 29,
            SECONDARY_SPECULAR_BLEND_MULTIPLY = 30,
            GLASS = 31,
            DIFFUSE_ROUGHNESS = 32,
            FRONT_ROUGHNESS = 33,
            ADDITIVE_ROUGHNESS = 34,
            ENVIRONMENT_MAPPING = 35,
            AMBIENT_OCCLUSION_MAPPING = 36,
            AMBIENT_OCCLUSION_UV = 37,
            VERTEX_AMBIENT_OCCLUSION = 38,
            EMISSIVE = 39,
            DUST_MAPPING = 40,
            DUST_MAPPING_PARALLAX = 41,
            SSR = 42,
            IRRADIANCE_CUBE = 43,
            RADIOSITY_DYNAMIC = 44,
            FUR_RIM_LIGHTING = 45,
            PARALLAX_MAPPING = 46,
            DECAL = 47,
            DECAL_DIFFUSE = 48,
            DECAL_NORMAL = 49,
            DECAL_SPECULAR_EMISSIVE = 50,
        }
        public enum SAMPLERS
        {
            LIGHTMAP_MAP = 0,
            BEST_FIT_NORMAL_LOOKUP = 1,
            DIRT_MAP = 2,
            ALPHABLEND_NOISE_MAP = 3,
            SEPARATE_ALPHA_MAP = 4,
            DIFFUSE_MAP = 5,
            SECONDARY_DIFFUSE_MAP = 6,
            NORMAL_MAP = 7,
            SECONDARY_NORMAL_MAP = 8,
            SPECULAR_MAP = 9,
            SECONDARY_SPECULAR_MAP = 10,
            ENVIRONMENT_MAP = 11,
            AMBIENT_OCCLUSION_MAP = 12,
            DUST_MAP = 13,
            IRRADIANCE_CUBE_MAP = 14,
            PARALLAX_MAP = 15,
        }
        public enum PARAMETERS
        {
            SIZE_CULLING_THRESHOLD = 0,
            FORCE_PRIORITY_LEVEL = 1,
            SHIFT_PRIORITY_LEVEL = 2,
            FRESNEL_INTENSITY = 3,
            LIGHTMAP_INTENSITY_SCALE = 4,
            DIRT_BLEND_MULT_SPEC_POWER = 5,
            DIRT_UV_MULT = 6,
            DIRT_AO_AMOUNT = 7,
            ALPHABLEND_NOISE_UV_MULT = 8,
            ALPHABLEND_NOISE_POWER = 9,
            SEPARATE_ALPHA_UV_MULT = 10,
            DIFFUSE_UV_MULT = 11,
            DIFFUSE_TINT = 12,
            SECONDARY_DIFFUSE_UV_MULT = 13,
            SECONDARY_DIFFUSE_TINT = 14,
            NORMAL_UV_MULT = 15,
            NORMAL_MAP_STRENGTH = 16,
            SECONDARY_NORMAL_UV_MULT = 17,
            SECONDARY_NORMAL_MAP_STRENGTH = 18,
            SPECULAR_TINT = 19,
            SPECULAR_UV_MULT = 20,
            SPECULAR_POWER = 21,
            SECONDARY_SPECULAR_TINT = 22,
            SECONDARY_SPECULAR_UV_MULT = 23,
            SECONDARY_SPECULAR_POWER = 24,
            GLASS_DENSITY = 25,
            GLASS_LIGHTNESS = 26,
            GLASS_TINT = 27,
            DIFFUSE_ROUGHNESS_FACTOR = 28,
            ENVIRONMENT_EMISSIVE_FACTOR = 29,
            ENVIRONMENT_MAP_MULT = 30,
            AO_TINT = 31,
            AMBIENT_OCCLUSION_MAP_MULT = 32,
            VERT_AO_TINT = 33,
            EMISSIVE_MULT = 34,
            EMISSIVE_TINT = 35,
            DUST_UV_MULT = 36,
            DUST_FALLOFF = 37,
            SSR_AMOUNT = 38,
            FUR_RIM_LIGHTING_FACTOR = 39,
            PARALLAX_UV_MULT = 40,
            PARALLAX_SCALE = 41,
            PARALLAX_BIAS = 42,
            OPACITY_MODIFIER_VALUE = 43,
        }
    }

    public static class CA_STREAMER
    {
        public enum FEATURES
        {
            REFLECTIVE_PLASTIC = 0,
            DOUBLE_SIDED = 1,
            USE_ALPHA_AS_BLENDFACTOR = 2,
            FORCE_TO_ALPHA = 4,
            ALPHA_TEST = 5,
            TEXTURE_LOD_BIAS_NONE = 6,
            TEXTURE_LOD_BIAS_SLIGHT = 7,
            TEXTURE_LOD_BIAS_HIGH = 8,
            TURBULENCE = 9,
            DIRT_MAPPING = 10,
            DIRT_BLEND_MULTIPLY = 11,
            DIRT_MAPPING_PARALLAX = 12,
            ALPHABLEND_NOISE = 13,
            SEPARATE_ALPHA = 14,
            SEPARATE_ALPHA_MAP_USE_GREEN_CHANNEL = 15,
            SIGNED_DISTANCE_FIELD = 16,
            DIFFUSE_MAPPING_PARALLAX = 17,
            SECONDARY_DIFFUSE_MAPPING = 18,
            SECONDARY_DIFFUSE_BLEND_MULTIPLY = 19,
            NORMAL_MAPPING = 20,
            NORMAL_MAPPING_PARALLAX = 21,
            SECONDARY_NORMAL_MAPPING = 22,
            SECONDARY_NORMAL_BLEND_ADD = 23,
            SPECULAR_MAPPING = 24,
            SPECULAR_MAPPING_PARALLAX = 25,
            SECONDARY_SPECULAR_MAPPING = 26,
            SECONDARY_SPECULAR_MAPPING_PARALLAX = 27,
            SECONDARY_SPECULAR_BLEND_MULTIPLY = 28,
            GLASS = 29,
            DIFFUSE_ROUGHNESS = 30,
            FRONT_ROUGHNESS = 31,
            ADDITIVE_ROUGHNESS = 32,
            ENVIRONMENT_MAPPING = 33,
            AMBIENT_OCCLUSION_MAPPING = 34,
            AMBIENT_OCCLUSION_UV = 35,
            VERTEX_AMBIENT_OCCLUSION = 36,
            EMISSIVE = 37,
            DUST_MAPPING = 38,
            DUST_MAPPING_PARALLAX = 39,
            SSR = 40,
            IRRADIANCE_CUBE = 41,
            RADIOSITY_DYNAMIC = 42,
            FUR_RIM_LIGHTING = 43,
            PARALLAX_MAPPING = 44,
            DECAL = 45,
            DECAL_DIFFUSE = 46,
            DECAL_NORMAL = 47,
            DECAL_SPECULAR_EMISSIVE = 48,
        }
        public enum SAMPLERS
        {
            DIRT_MAP = 0,
            ALPHABLEND_NOISE_MAP = 1,
            SEPARATE_ALPHA_MAP = 2,
            DIFFUSE_MAP = 3,
            SECONDARY_DIFFUSE_MAP = 4,
            NORMAL_MAP = 5,
            SECONDARY_NORMAL_MAP = 6,
            SPECULAR_MAP = 7,
            SECONDARY_SPECULAR_MAP = 8,
            ENVIRONMENT_MAP = 9,
            AMBIENT_OCCLUSION_MAP = 10,
            DUST_MAP = 11,
            IRRADIANCE_CUBE_MAP = 12,
            PARALLAX_MAP = 13,
        }
        public enum PARAMETERS
        {
            SIZE_CULLING_THRESHOLD = 0,
            FORCE_PRIORITY_LEVEL = 1,
            SHIFT_PRIORITY_LEVEL = 2,
            FRESNEL_INTENSITY = 3,
            MAX_SINE_AMPLITUDE = 4,
            ANIMATION_SPEED = 5,
            MOTION_DIRECTION_X = 6,
            MOTION_DIRECTION_Y = 7,
            MOTION_DIRECTION_Z = 8,
            TURBULENCE_AMPLITUDE = 9,
            DIRT_BLEND_MULT_SPEC_POWER = 10,
            DIRT_UV_MULT = 11,
            DIRT_AO_AMOUNT = 12,
            ALPHABLEND_NOISE_UV_MULT = 13,
            ALPHABLEND_NOISE_POWER = 14,
            SEPARATE_ALPHA_UV_MULT = 15,
            DIFFUSE_UV_MULT = 16,
            DIFFUSE_TINT = 17,
            SECONDARY_DIFFUSE_UV_MULT = 18,
            SECONDARY_DIFFUSE_TINT = 19,
            NORMAL_UV_MULT = 20,
            NORMAL_MAP_STRENGTH = 21,
            SECONDARY_NORMAL_UV_MULT = 22,
            SECONDARY_NORMAL_MAP_STRENGTH = 23,
            SPECULAR_TINT = 24,
            SPECULAR_UV_MULT = 25,
            SPECULAR_POWER = 26,
            SECONDARY_SPECULAR_TINT = 27,
            SECONDARY_SPECULAR_UV_MULT = 28,
            SECONDARY_SPECULAR_POWER = 29,
            GLASS_DENSITY = 30,
            GLASS_LIGHTNESS = 31,
            GLASS_TINT = 32,
            DIFFUSE_ROUGHNESS_FACTOR = 33,
            ENVIRONMENT_EMISSIVE_FACTOR = 34,
            ENVIRONMENT_MAP_MULT = 35,
            AO_TINT = 36,
            AMBIENT_OCCLUSION_MAP_MULT = 37,
            VERT_AO_TINT = 38,
            EMISSIVE_MULT = 39,
            EMISSIVE_TINT = 40,
            DUST_UV_MULT = 41,
            DUST_FALLOFF = 42,
            SSR_AMOUNT = 43,
            FUR_RIM_LIGHTING_FACTOR = 44,
            PARALLAX_UV_MULT = 45,
            PARALLAX_SCALE = 46,
            PARALLAX_BIAS = 47,
            OPACITY_MODIFIER_VALUE = 48,
        }
    }

    public static class CA_LOW_LOD_CHARACTER
    {
        public enum FEATURES
        {
            BLUR_MASKING = 0,
            DEPTH_ONLY = 1,
            REFLECTIVE_PLASTIC = 2,
            DOUBLE_SIDED = 3,
            USE_ALPHA_AS_BLENDFACTOR = 4,
            VERTEX_COLOUR = 5,
            VERTEX_PROCESSING_GPU_SKINNING = 6,
            NO_SKINNING = 7,
            FORCE_TO_ALPHA = 9,
            ALPHA_TEST = 10,
            TEXTURE_LOD_BIAS_NONE = 11,
            TEXTURE_LOD_BIAS_SLIGHT = 12,
            TEXTURE_LOD_BIAS_HIGH = 13,
            DIFFUSE_MAPPING_PARALLAX = 14,
            NORMAL_MAPPING = 15,
            NORMAL_MAPPING_PARALLAX = 16,
            SPECULAR_MAPPING = 17,
            SPECULAR_MAPPING_PARALLAX = 18,
            DIFFUSE_ROUGHNESS = 19,
            FRONT_ROUGHNESS = 20,
            ADDITIVE_ROUGHNESS = 21,
            CUSTOM_CHARACTER = 22,
            LOW_LOD_CUSTOM_CHARACTER_CORPSE_CONSTANTS = 23,
            LOW_LOD_CHARACTER_MASK = 24,
            LOW_LOD_CUSTOM_CHARACTER_TINT_MASK = 25,
            LOW_LOD_CUSTOM_CHARACTER_PLASTIC_MASK = 26,
            SSR = 27,
            IRRADIANCE_CUBE = 28,
            ENVIRONMENT_MAPPING = 29,
            ENVMAP_LOCK = 30,
        }
        public enum SAMPLERS
        {
            DIFFUSE_MAP = 0,
            NORMAL_MAP = 1,
            SPECULAR_MAP = 2,
            LOW_LOD_CHARACTER_MASK_TEX = 3,
            IRRADIANCE_CUBE_MAP = 4,
            ENVIRONMENT_MAP = 5,
        }
        public enum PARAMETERS
        {
            DRAW_PASS = 0,
            SIZE_CULLING_THRESHOLD = 1,
            FORCE_PRIORITY_LEVEL = 2,
            SHIFT_PRIORITY_LEVEL = 3,
            FRESNEL_INTENSITY = 4,
            DIFFUSE_UV_MULT = 5,
            DIFFUSE_TINT = 6,
            NORMAL_UV_MULT = 7,
            NORMAL_MAP_STRENGTH = 8,
            SPECULAR_TINT = 9,
            SPECULAR_UV_MULT = 10,
            SPECULAR_POWER = 11,
            DIFFUSE_ROUGHNESS_FACTOR = 12,
            IS_CUSTOM_CHARACTER_DECAL = 13,
            CUSTOM_PRIMARY_TINT_COLOUR_CONSTANT = 14,
            CUSTOM_SECONDARY_TINT_COLOUR_CONSTANT = 15,
            CUSTOM_TERTIARY_TINT_COLOUR_CONSTANT = 16,
            SSR_AMOUNT = 17,
            ENVIRONMENT_EMISSIVE_FACTOR = 18,
            ENVIRONMENT_MAP_MULT = 19,
        }
    }

    public static class CA_LIGHT_DECAL
    {
        public enum FEATURES
        {
            LIGHT_DECAL_DIRECTIONAL = 0,
            LIGHT_DECAL_SPECULAR = 1,
        }
        public enum SAMPLERS
        {
            INTENSITY_MAP = 0,
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_VOLUME_LIGHT
    {
        public enum FEATURES
        {
            SHADOW_MAP = 0,
            GOBO = 1,
            SQUARE_LIGHT = 2,
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
        }
    }

    public static class CA_WATER_CAUSTICS_OVERLAY
    {
        public enum FEATURES
        {
        }
        public enum SAMPLERS
        {
        }
        public enum PARAMETERS
        {
            CAUSTIC_TEXTURE_SCALE = 0,
            CAUSTIC_INTENSITY = 1,
            CAUSTIC_SURFACE_WRAP = 2,
            CAUSTIC_HALF_DIMENSIONS = 3,
            CAUSTIC_SPEED = 4,
            CAUSTIC_WRAP_DIRECTION = 5,
        }
    }

    public static class CA_SPACESUIT_VISOR
    {
        public enum FEATURES
        {
            VERTEX_COLOUR = 0,
            VERTEX_PROCESSING_GPU_SKINNING = 1,
            NON_SKINNED = 2,
            ENVIRONMENT_MAPPING = 3,
            SCREEN_SPACE_REFLECTION_MAPPING = 4,
            NORMAL_MAPPING = 6,
            MASKING = 7,
            FACE_MAPPING = 8,
            BREATH = 9,
            DIRT_MAPPING = 10,
            ALPHA_LIGHTING = 11,
            VISOR_DISTORTION = 12,
        }
        public enum SAMPLERS
        {
            ENVIRONMENT_MAP = 0,
            NORMAL_MAP = 1,
            MASKING_MAP = 2,
            FACE_MAP = 3,
            BREATH_GRADIENT_MAP = 4,
            UNSCALED_DIRT_MAP = 5,
            DIRT_MAP = 6,
        }
        public enum PARAMETERS
        {
            DRAW_PASS = 0,
            GLASS_SPEC_POWER = 1,
            ENVIRONMENT_MAP_MULT = 2,
            SSR_SCALE_X = 3,
            SSR_SCALE_Y = 4,
            SSR_OFFSET_X = 5,
            SSR_OFFSET_Y = 6,
            NORMAL_MAP_MULT = 7,
            NORMAL_MAPPING_STRENGTH = 8,
            MASKING_MAP_MULT = 9,
            FACE_INTENSITY_MULT = 10,
            BREATH_INTENSITY_MULT = 11,
            BREATH_COLOUR = 12,
            DIRT_BLEND_MULT_SPEC_POWER = 13,
            DIRT_UV_MULT = 14,
            REFRACTFACTOR = 15,
            DISTANCEFACTOR = 16,
        }
    }

    public static class CA_CAMERA_MAP
    {
        public enum FEATURES
        {
            MANUAL_UV_MAP = 0,
        }
        public enum SAMPLERS
        {
            DIFFUSE_MAP = 0,
        }
        public enum PARAMETERS
        {
            LIGHT_MULT = 0,
            PROJECTION_MATRIX_ROW0 = 1,
            PROJECTION_MATRIX_ROW1 = 2,
            PROJECTION_MATRIX_ROW2 = 3,
            PROJECTION_MATRIX_ROW3 = 4,
            SHIFT_PRIORITY_LEVEL = 9,
        }
    }
}