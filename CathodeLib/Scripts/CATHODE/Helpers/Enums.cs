namespace CATHODE.Enums
{
    public enum AGGRESSION_GAIN
    {
        LOW_AGGRESSION_GAIN = 0,
        MED_AGGRESSION_GAIN = 1,
        HIGH_AGGRESSION_GAIN = 2,
        UNKNOWN_AGGRESSION_GAIN = -1,
    }

    public enum ALERTNESS_STATE
    {
        IGNORE_PLAYER = 0,
        ALERT = 1,
        AGGRESSIVE = 2,
        UNKNOWN_ALERTNESS_STATE = -1,
    }

    public enum ALIEN_CONFIGURATION_TYPE
    {
        DEFAULT = 0,
        MILD = 1,
        MODERATE = 2,
        INTENSE = 3,
        BACKSTAGEHOLD = 4,
        MODERATELY_INTENSE = 5,
        BACKSTAGEALERT = 6,
        BACSTAGEHOLD_CLOSE = 7,
        BACKSTAGEHOLD_VCLOSE = 8,
        UNKNOWN_ALIEN_CONFIGURATION_TYPE = -1,
    }

    public enum ALIEN_DEVELOPMENT_MANAGER_ABILITIES
    {
        NONE = 0,
        THREAT_AWARE = 1,
        LIKES_TO_CLOSE_VIA_BACKSTAGE = 2,
        WILL_KILLTRAP = 3,
        WILL_FLANK = 4,
        WILL_FLANK_FROM_THREAT_AWARE = 5,
        WILL_AMBUSH = 6,
        SEARCH_LOCKERS = 7,
        SEARCH_UNDER_STUFF = 8,
        UNKNOWN_ALIEN_DEVELOPMENT_MANAGER_ABILITIES = -1,
    }

    public enum ALIEN_DEVELOPMENT_MANAGER_ABILITY_MASKS
    {
        NONE = 0,
        THREAT_AWARE = 2,
        LIKES_TO_CLOSE_VIA_BACKSTAGE = 4,
        WILL_KILLTRAP = 8,
        WILL_FLANK = 16,
        WILL_FLANK_FROM_THREAT_AWARE = 32,
        WILL_AMBUSH = 64,
        SEARCH_LOCKERS = 128,
        SEARCH_UNDER_STUFF = 256,
        UNKNOWN_ALIEN_DEVELOPMENT_MANAGER_ABILITY_MASKS = -1,
    }

    public enum ALIEN_DEVELOPMENT_MANAGER_STAGES
    {
        NAIVE = 0,
        THREAT_AWARE = 2,
        GETTING_SNEAKY = 398,
        REALLY_SNEAKY = 478,
        UNKNOWN_ALIEN_DEVELOPMENT_MANAGER_STAGES = -1,
    }

    public enum ALLIANCE_GROUP
    {
        NEUTRAL = 0,
        PLAYER = 1,
        PLAYER_ALLY = 2,
        ALIEN = 3,
        ANDROID = 4,
        CIVILIAN = 5,
        SECURITY = 6,
        DEAD = 7,
        DEAD_MAN_WALKING = 8,
        UNKNOWN_ALLIANCE_GROUP = -1,
    }

    public enum ALLIANCE_STANCE
    {
        FRIEND = 0,
        NEUTRAL = 1,
        ENEMY = 2,
        UNKNOWN_ALLIANCE_STANCE = -1,
    }

    public enum AMBUSH_TYPE
    {
        KILLTRAP = 0,
        FRONTSTAGE_AMBUSH = 1,
        UNKNOWN_AMBUSH_TYPE = -1,
    }

    public enum AMMO_TYPE
    {
        PISTOL_NORMAL = 0,
        PISTOL_DUM_DUM = 1,
        SMG_NORMAL = 2,
        SMG_DUM_DUM = 3,
        SHOTGUN_NORMAL = 4,
        SHOTGUN_SLUG = 5,
        FLAMETHROWER_NORMAL = 6,
        FLAMETHROWER_AERATED = 7,
        FLAMETHROWER_HIGH_DAMAGE = 8,
        SHOTGUN_INCENDIARY = 9,
        PISTOL_NORMAL_NPC = 10,
        SHOTGUN_NORMAL_NPC = 11,
        GRENADE_HE = 12,
        GRENADE_FIRE = 13,
        CATALYST_HE_SMALL = 14,
        CATALYST_HE_LARGE = 15,
        CATALYST_FIRE_SMALL = 16,
        CATALYST_FIRE_LARGE = 17,
        ACID_BURST_SMALL = 18,
        ACID_BURST_LARGE = 19,
        EMP_BURST_SMALL = 20,
        EMP_BURST_LARGE = 21,
        IMPACT = 22,
        GRENADE_STUN = 23,
        GRENADE_SMOKE = 24,
        PISTOL_TAZER = 25,
        MELEE_CROW_AXE = 26,
        BOLTGUN_NORMAL = 27,
        PUSH = 28,
        CATTLEPROD_POWERPACK = 29,
        EMP_BURST_LARGE_TIER2 = 30,
        EMP_BURST_LARGE_TIER3 = 31,
        GRENADE_FIRE_TIER2 = 32,
        GRENADE_FIRE_TIER3 = 33,
        GRENADE_HE_TIER2 = 34,
        GRENADE_HE_TIER3 = 35,
        GRENADE_STUN_TIER2 = 36,
        GRENADE_STUN_TIER3 = 37,
        ENVIRONMENT_FLAME = 38,
        UNKNOWN_AMMO_TYPE = -1,
    }

    public enum ANIM_CALLBACK_ENUM
    {
        NONE = 0,
        STUN_DAMAGE_CALLBACK = 1,
        UNKNOWN_ANIM_CALLBACK_ENUM = -1,
    }

    public enum ANIM_MODE
    {
        FORWARD = 0,
        BACKWARD = 1,
        BOUNCE = 2,
        RANDOM = 3,
        UNKNOWN_ANIM_MODE = -1,
    }

    public enum ANIM_TRACK_TYPE
    {
        T_FLOAT = 0,
        T_FLOAT3 = 1,
        T_POSITION = 2,
        T_STRING = 3,
        T_GUID = 4,
        T_MASTERING = 5,
        UNKNOWN_ANIM_TRACK_TYPE = -1,
    }

    public enum ANIM_TREE_ENUM
    {
        NONE = 0,
        STUN_DAMAGE_TREE = 1,
        UNKNOWN_ANIM_TREE_ENUM = -1,
    }

    public enum ANIMATION_EFFECT_TYPE
    {
        STUMBLE = 0,
        UNKNOWN_ANIMATION_EFFECT_TYPE = -1,
    }

    public enum AREA_SWEEP_TYPE
    {
        IN_AND_OUT_BETWEEN_TARGET_AND_POSITION = 0,
        FIXED_RADIUS_AROUND_POSITION = 1,
        UNKNOWN_AREA_SWEEP_TYPE = -1,
    }

    public enum AREA_SWEEP_TYPE_CODE
    {
        IN_AND_OUT_BETWEEN_TARGET_AND_POSITION = 0,
        FIXED_RADIUS_AROUND_POSITION = 1,
        AROUND_TARGET = 2,
        UNKNOWN_AREA_SWEEP_TYPE_CODE = -1,
    }

    public enum AUTODETECT
    {
        AUTO = 0,
        FORCE_ON = 1,
        FORCE_OFF = 2,
        UNKNOWN_AUTODETECT = -1,
    }

    public enum BEHAVIOR_TREE_BRANCH_TYPE
    {
        NONE = 0,
        CINEMATIC_BRANCH = 1,
        ATTACK_BRANCH = 2,
        AIM_BRANCH = 3,
        DESPAWN_BRANCH = 4,
        FOLLOW_BRANCH = 5,
        STANDARD_BRANCH = 6,
        SEARCH_BRANCH = 7,
        AREA_SWEEP_BRANCH = 8,
        BACKSTAGE_AREA_SWEEP_BRANCH = 9,
        SHOT_BRANCH = 10,
        SUSPECT_TARGET_RESPONSE_BRANCH = 11,
        THREAT_AWARE_BRANCH = 12,
        BACKSTAGE_AMBUSH_BRANCH = 13,
        IDLE_JOB_BRANCH = 14,
        USE_COVER_BRANCH = 15,
        ASSAULT_BRANCH = 16,
        MELEE_BRANCH = 17,
        RETREAT_BRANCH = 18,
        CLOSE_ON_TARGET_BRANCH = 19,
        MUTUAL_MELEE_ONLY_BRANCH = 20,
        VENT_MELEE_BRANCH = 21,
        ASSAULT_NOT_ALLOWED_BRANCH = 22,
        IN_VENT_BRANCH = 23,
        CLOSE_COMBAT_BRANCH = 24,
        DRAW_WEAPON_BRANCH = 25,
        PURSUE_TARGET_BRANCH = 26,
        RANGED_ATTACK_BRANCH = 27,
        RANGED_COMBAT_BRANCH = 28,
        PRIMARY_CONTROL_RESPONSE_BRANCH = 29,
        DEAD_BRANCH = 30,
        SCRIPT_BRANCH = 31,
        IDLE_BRANCH = 32,
        DOWN_BUT_NOT_OUT_BRANCH = 33,
        MELEE_BLOCK_BRANCH = 34,
        AGRESSIVE_BRANCH = 35,
        ALERT_BRANCH = 36,
        SHOOTING_BRANCH = 37,
        GRAPPLE_BREAK_BRANCH = 38,
        REACT_TO_WEAPON_FIRE_BRANCH = 39,
        IN_COVER_BRANCH = 40,
        SUSPICIOUS_ITEM_BRANCH_HIGH = 41,
        SUSPICIOUS_ITEM_BRANCH_MEDIUM = 42,
        SUSPICIOUS_ITEM_BRANCH_LOW = 43,
        AGGRESSION_ESCALATION_BRANCH = 44,
        STUN_DAMAGE_BRANCH = 45,
        BREAKOUT_BRANCH = 46,
        SUSPEND_BRANCH = 47,
        TARGET_IS_NPC_BRANCH = 48,
        PLAYER_HIDING_BRANCH = 49,
        ATTACK_CORE_BRANCH = 50,
        CORPSE_TRAP_BRANCH = 51,
        OBSERVE_TARGET_BRANCH = 52,
        TARGET_IN_CRAWLSPACE_BRANCH = 53,
        MB_SUSPICIOUS_ITEM_ATLEAST_MOVE_CLOSE_TO = 54,
        MB_THREAT_AWARE_ATTACK_TARGET_WITHIN_CLOSE_RANGE = 55,
        MB_THREAT_AWARE_ATTACK_TARGET_WITHIN_VERY_CLOSE_RANGE = 56,
        MB_THREAT_AWARE_ATTACK_TARGET_FLAMED_ME = 57,
        MB_THREAT_AWARE_ATTACK_WEAPON_NOT_AIMED = 58,
        MB_THREAT_AWARE_MOVE_TO_LOST_VISUAL = 59,
        MB_THREAT_AWARE_MOVE_TO_BEFORE_ANIM = 60,
        MB_THREAT_AWARE_MOVE_TO_AFTER_ANIM = 61,
        MB_THREAT_AWARE_MOVE_TO_FLANKED_VENT = 62,
        KILLTRAP_BRANCH = 63,
        PANIC_BRANCH = 64,
        BACKSTAGE_ALIEN_RESPONSE_BRANCH = 65,
        NPC_VS_ALIEN_BRANCH = 66,
        USE_COVER_VS_ALIEN_BRANCH = 67,
        IN_COVER_VS_ALIEN_BRANCH = 68,
        REPEATED_PATHFIND_FAILS_BRANCH = 69,
        ALL_SEARCH_VARIANTS_BRANCH = 70,
        UNKNOWN_BEHAVIOR_TREE_BRANCH_TYPE = -1,
    }

    public enum BEHAVIOUR_MOOD_SET
    {
        NEUTRAL = 0,
        THREAT_ESCALATION_AGGRESSIVE = 1,
        THREAT_ESCALATION_PANICKED = 2,
        AGGRESSIVE = 3,
        PANICKED = 4,
        SUSPICIOUS = 5,
        UNKNOWN_BEHAVIOUR_MOOD_SET = -1,
    }

    public enum BEHAVIOUR_TREE_FLAGS
    {
        DO_ASSAULT_ATTACK_CHECKS = 2,
        IS_IN_VENT = 3,
        IS_SITTING = 7,
        PLAYER_HIDING = 13,
        ATTACK_HIDING_PLAYER = 14,
        ALIEN_ALWAYS_KNOWS_WHEN_IN_VENT = 15,
        IS_CORPSE_TRAP_ON_START = 16,
        IS_BACKSTAGE_STALK_LOCKED = 19,
        PLAYER_WON_HIDING_QTE = 21,
        ANDROID_IS_INERT = 22,
        ANDROID_IS_SHOWROOM_DUMMY = 23,
        NEVER_AGGRESSIVE = 25,
        MUTE_DYNAMIC_DIALOGUE = 26,
        BLOCK_AMBUSH_AND_KILLTRAPS = 29,
        PREVENT_GRAPPLES = 30,
        PREVENT_ALL_ATTACKS = 31,
        USE_AIMED_STANCE_FOR_IDLE_JOBS = 34,
        USE_AIMED_LOW_STANCE_FOR_IDLE_JOBS = 35,
        IGNORE_PLAYER_IN_VENT_BEHAVIOUR = 33,
        IS_ON_LADDER = 38,
        UNKNOWN_BEHAVIOUR_TREE_FLAGS = -1,
    }

    public enum BLEND_MODE
    {
        BLEND = 1,
        ADDITIVE = 2,
        UNKNOWN_BLEND_MODE = -1,
    }

    public enum BLUEPRINT_LEVEL
    {
        LEVEL_1 = 1,
        LEVEL_2 = 2,
        LEVEL_3 = 3,
        UNKNOWN_BLUEPRINT_LEVEL = -1,
    }

    public enum BUTTON_TYPE
    {
        KEYS = 0,
        DISK = 1,
        UNKNOWN_BUTTON_TYPE = -1,
    }

    public enum CAMERA_PATH_CLASS
    {
        GENERIC = 0,
        POSITION = 1,
        TARGET = 2,
        REFERENCE = 3,
        UNKNOWN_CAMERA_PATH_CLASS = -1,
    }

    public enum CAMERA_PATH_TYPE
    {
        LINEAR = 0,
        BEZIER = 1,
        UNKNOWN_CAMERA_PATH_TYPE = -1,
    }

    public enum CHARACTER_BB_ENTRY_TYPE
    {
        CBB_HAVE_CURRENT_TARGET = 0,
        CBB_MOST_RECENT_TARGET_SENSE_IS_ACTIVATED = 1,
        CBB_MOST_RECENT_TARGET_SENSE_HAS_BEEN_ACTIVATED = 2,
        CBB_SENSE_ABOVE_FIRST = 3,
        CBB_SENSE_ABOVE_LAST = 10,
        CBB_SENSE_TRIGGERED_FIRST = 11,
        CBB_SENSE_TRIGGERED_LAST = 18,
        CBB_SENSE_BEEN_ABOVE_FIRST = 19,
        CBB_SENSE_BEEN_ABOVE_LAST = 26,
        CBB_SENSE_LAST_TIME_FIRST = 27,
        CBB_SENSE_LAST_TIME_LAST = 34,
        CBB_TARGET_CLOSEST_THRESHOLD = 35,
        CBB_CAN_MOVE_TO_TARGET = 36,
        CBB_HAVE_NEXT_TARGET = 37,
        CBB_AGENT_MOTIVATION = 38,
        CBB_GAUGE_AMOUNT_ABOVE_RETREAT = 39,
        CBB_GAUGE_AMOUNT_ABOVE_FIRST = 39,
        CBB_GAUGE_AMOUNT_ABOVE_MELEE_DEFENSE = 40,
        CBB_GAUGE_AMOUNT_ABOVE_STUN_DAMAGE = 41,
        CBB_GAUGE_AMOUNT_ABOVE_LAST = 41,
        CBB_HAS_MELEE_ATTACK_AVAILABLE = 42,
        CBB_ALLOWED_TO_ATTACK_TARGET = 43,
        CBB_ALERTNESS_STATE = 44,
        CBB_HAS_A_WEAPON = 45,
        CBB_WEAPON_IS_EQUIPPED = 46,
        CBB_WEAPON_NEEDS_RELOADING = 47,
        CBB_LOGIC_CHARACTER_SUSPECT_TARGET_RESPONSE_TIMER = 48,
        CBB_LOGIC_CHARACTER_TIMER_FIRST = 48,
        CBB_LOGIC_CHARACTER_THREAT_AWARE_TIMEOUT_TIMER = 49,
        CBB_LOGIC_CHARACTER_THREAT_AWARE_DURATION_TIMER = 50,
        CBB_LOGIC_CHARACTER_SEARCH_TIMEOUT_TIMER = 51,
        CBB_LOGIC_CHARACTER_BACKSTAGE_STALK_TIMEOUT_TIMER = 52,
        CBB_LOGIC_CHARACTER_AMBUSH_TIMEOUT_TIMER = 53,
        CBB_LOGIC_CHARACTER_BACKSTAGE_STALK_PICK_KILLTRAP_TIMER = 54,
        CBB_LOGIC_CHARACTER_ATTACK_BAN_TIMER = 55,
        CBB_LOGIC_CHARACTER_MELEE_ATTACK_BAN_TIMER = 56,
        CBB_LOGIC_CHARACTER_VENT_BAN_TIMER = 57,
        CBB_LOGIC_CHARACTER_NPC_STAY_IN_COVER_TIMER = 58,
        CBB_LOGIC_CHARACTER_NPC_JUST_LEFT_COMBAT_TIMER = 59,
        CBB_LOGIC_CHARACTER_ATTACK_KEEP_CHASING_TIMER = 60,
        CBB_LOGIC_CHARACTER_DELAY_RETURN_TO_SPAWN_POINT_TIMER = 61,
        CBB_LOGIC_CHARACTER_TARGET_IN_CRAWLSPACE_TIMER = 62,
        CBB_LOGIC_CHARACTER_DURATION_SINCE_SEARCH_TIMER = 63,
        CBB_LOGIC_CHARACTER_HEIGHTENED_SENSES_TIMER = 64,
        CBB_LOGIC_CHARACTER_FLANKED_VENT_ATTACK_TIMER = 65,
        CBB_LOGIC_CHARACTER_THREAT_AWARE_VISUAL_RETENTION_TIMER = 66,
        CBB_LOGIC_CHARACTER_RESPONSE_TO_BACKSTAGE_ALIEN_TIMEOUT_TIMER = 67,
        CBB_LOGIC_CHARACTER_VENT_ATTRACT_TIMER = 68,
        CBB_LOGIC_CHARACTER_SEEN_PLAYER_AIM_WEAPON_TIMER = 69,
        CBB_LOGIC_CHARACTER_SEARCH_BAN_TIMER = 70,
        CBB_LOGIC_CHARACTER_OBSERVE_TARGET_TIMER = 71,
        CBB_LOGIC_CHARACTER_REPEATED_PATHFIND_FAILUREST_TIMER = 72,
        CBB_LOGIC_CHARACTER_TIMER_LAST = 72,
        CBB_AFFECTED_BY_TARGETS_FLAME_THROWER = 73,
        UNKNOWN_CHARACTER_BB_ENTRY_TYPE = -1,
    }

    public enum CHARACTER_CLASS
    {
        PLAYER = 0,
        ALIEN = 1,
        ANDROID = 2,
        CIVILIAN = 3,
        SECURITY = 4,
        FACEHUGGER = 5,
        INNOCENT = 6,
        ANDROID_HEAVY = 7,
        MOTION_TRACKER = 8,
        MELEE_HUMAN = 9,
        UNKNOWN_CHARACTER_CLASS = -1,
    }

    public enum CHARACTER_CLASS_COMBINATION
    {
        NONE = 0,
        PLAYER_ONLY = 1,
        ALIEN_ONLY = 2,
        ANDROID_ONLY = 4,
        CIVILIAN_ONLY = 8,
        SECURITY_ONLY = 16,
        FACEHUGGER_ONLY = 32,
        INNOCENT_ONLY = 64,
        ANDROID_HEAVY_ONLY = 128,
        MOTION_TRACKER = 256,
        MELEE_HUMAN_ONLY = 512,
        ANDROIDS = 132,
        ALIENS = 34,
        HUMAN_NPC = 600,
        HUMAN = 601,
        HUMANOID_NPC = 732,
        HUMANOID = 733,
        ANDROIDS_AND_ALIEN = 166,
        PLAYER_AND_ALIEN = 35,
        ALL = 1023,
        UNKNOWN_CHARACTER_CLASS_COMBINATION = -1,
    }

    public enum CHARACTER_FOLEY_SOUND
    {
        LEATHER = 0,
        HEAVY_JACKET = 1,
        HEAVY_OVERALLS = 2,
        SHIRT = 3,
        SUIT_JACKET = 4,
        SUIT_TROUSERS = 5,
        JEANS = 6,
        BOOTS = 7,
        FLATS = 8,
        TRAINERS = 9,
        UNKNOWN_CHARACTER_FOLEY_SOUND = -1,
    }

    public enum CHARACTER_NODE
    {
        HEAD1 = 0,
        HEAD = 1,
        HIPS = 2,
        TORSO = 3,
        SPINE2 = 4,
        SPINE1 = 5,
        SPINE = 6,
        LEFT_ARM = 7,
        RIGHT_ARM = 8,
        LEFT_HAND = 9,
        RIGHT_HAND = 10,
        LEFT_LEG = 11,
        RIGHT_LEG = 12,
        LEFT_FOOT = 13,
        RIGHT_FOOT = 14,
        LEFT_WEAPON_BONE = 15,
        RIGHT_WEAPON_BONE = 16,
        LEFT_SHOULDER = 17,
        RIGHT_SHOULDER = 18,
        WEAPON = 19,
        ROOT = 20,
        UNKNOWN_CHARACTER_NODE = -1,
    }

    public enum CHARACTER_STANCE
    {
        DONT_CHANGE = 0,
        STAND = 1,
        CROUCHED = 2,
        UNKNOWN_CHARACTER_STANCE = -1,
    }

    public enum CHECKPOINT_TYPE
    {
        CAMPAIGN = 0,
        MANUAL = 1,
        CAMPAIGN_MISSION = 2,
        MISSION_TEMP_STATE = 3,
        UNKNOWN_CHECKPOINT_TYPE = -1,
    }

    public enum CI_MESSAGE_TYPE
    {
        MSG_NORMAL = 0,
        MSG_IMPORTANT = 1,
        MSG_ERROR = 2,
        UNKNOWN_CI_MESSAGE_TYPE = -1,
    }

    public enum CLIPPING_PLANES_PRESETS
    {
        MACRO = 0,
        CLOSE = 1,
        MID = 2,
        WIDE = 3,
        ULTRA = 4,
        UNKNOWN_CLIPPING_PLANES_PRESETS = -1,
    }

    public enum COLLISION_TYPE
    {
        LINE_OF_SIGHT_COL = 0,
        CAMERA_COL = 1,
        STANDARD_COL = 2,
        UI = 3,
        PLAYER_COL = 4,
        PHYSICS_COL = 5,
        TRANSPARENT_COL = 6,
        DETECTABLE = 7,
        UNKNOWN_COLLISION_TYPE = -1,
    }

    public enum COMBAT_BEHAVIOUR
    {
        ALLOW_ATTACK = 0,
        ALLOW_AIM = 1,
        CINEMATICS_ONLY = 2,
        ALLOW_IDLE_JOBS = 3,
        ALLOW_USE_COVER = 4,
        MUTUAL_MELEE = 5,
        VENT_MELEE = 6,
        ALLOW_SYSTEMATIC_SEARCH = 7,
        ALLOW_AREA_SWEEP = 8,
        ALLOW_WITHDRAW_TO_BACKSTAGE = 9,
        ALLOW_SUSPECT_TARGET_RESPONSE = 10,
        ALLOW_THREAT_AWARE = 11,
        ALLOW_BREAKOUT_WHEN_SHOT = 12,
        ALLOW_BACKSTAGE_AMBUSH = 13,
        ALLOW_ASSAULT = 14,
        ALLOW_MELEE = 15,
        ALLOW_RETREAT = 16,
        ALLOW_CLOSE_ON_TARGET = 17,
        ALLOW_REACT_TO_WEAPON_FIRE = 18,
        ALLOW_AGGRESSION_ESCALATION = 19,
        RESET_ALL_TO_DEFAULTS = 20,
        ALLOW_ADVANCE_ON_TARGET = 21,
        ALLOW_ALIEN_AMBUSHES = 22,
        ALLOW_PANIC = 23,
        ALLOW_SUSPICIOUS_ITEM = 24,
        ALLOW_RESPONSE_TO_BACKSTAGE_ALIEN = 25,
        ALLOW_ESCALATION_PREVENTS_SEARCH = 26,
        ALLOW_OBSERVE_TARGET = 27,
        UNKNOWN_COMBAT_BEHAVIOUR = -1,
    }

    public enum CROUCH_MODE
    {
        FORCE_CROUCH = 0,
        FORCE_UNCROUCH = 1,
        ALLOW_UNCROUCH = 2,
        UNKNOWN_CROUCH_MODE = -1,
    }

    public enum CUSTOM_CHARACTER_ACCESSORY_OVERRIDE
    {
        ACCESSORY_OVERRIDE_NONE = -1,
        ACCESSORY_OVERRIDE_01 = 0,
        ACCESSORY_OVERRIDE_02 = 1,
        ACCESSORY_OVERRIDE_03 = 2,
        ACCESSORY_OVERRIDE_04 = 3,
        ACCESSORY_OVERRIDE_05 = 4,
        ACCESSORY_OVERRIDE_06 = 5,
        ACCESSORY_OVERRIDE_07 = 6,
        ACCESSORY_OVERRIDE_08 = 7,
        ACCESSORY_OVERRIDE_09 = 8,
        ACCESSORY_OVERRIDE_10 = 9,
    }

    public enum CUSTOM_CHARACTER_ASSETS
    {
        ASSETSET_01 = 0,
        ASSETSET_02 = 1,
        ASSETSET_03 = 2,
        ASSETSET_04 = 3,
        ASSETSET_05 = 4,
        ASSETSET_06 = 5,
        ASSETSET_07 = 6,
        ASSETSET_08 = 7,
        ASSETSET_09 = 8,
        ASSETSET_10 = 9,
        UNKNOWN_CUSTOM_CHARACTER_ASSETS = -1,
    }

    public enum CUSTOM_CHARACTER_BUILD
    {
        STANDARD = 0,
        HEAVY = 1,
        UNKNOWN_CUSTOM_CHARACTER_BUILD = -1,
    }

    public enum CUSTOM_CHARACTER_COMPONENT
    {
        TORSO = 0,
        LEGS = 1,
        SHOES = 2,
        HEAD = 3,
        ARMS = 4,
        COLLISION = 5,
        UNKNOWN_CUSTOM_CHARACTER_COMPONENT = -1,
    }

    public enum CUSTOM_CHARACTER_ETHNICITY
    {
        AFRICAN = 0,
        CAUCASIAN = 1,
        ASIAN = 2,
        UNKNOWN_CUSTOM_CHARACTER_ETHNICITY = -1,
    }

    public enum CUSTOM_CHARACTER_GENDER
    {
        MALE = 0,
        FEMALE = 1,
        UNKNOWN_CUSTOM_CHARACTER_GENDER = -1,
    }

    public enum CUSTOM_CHARACTER_MODEL
    {
        NPC = 0,
        ANDROID = 1,
        CORPSE = 2,
        UNKNOWN_CUSTOM_CHARACTER_MODEL = -1,
    }

    public enum CUSTOM_CHARACTER_POPULATION
    {
        POPULATION_01 = 0,
        POPULATION_02 = 1,
        POPULATION_03 = 2,
        POPULATION_04 = 3,
        POPULATION_05 = 4,
        POPULATION_06 = 5,
        POPULATION_07 = 6,
        POPULATION_08 = 7,
        POPULATION_09 = 8,
        POPULATION_10 = 9,
        UNKNOWN_CUSTOM_CHARACTER_POPULATION = -1,
    }

    public enum CUSTOM_CHARACTER_SLEEVETYPE
    {
        LONG = 0,
        MEDIUM = 1,
        SHORT = 2,
        UNKNOWN_CUSTOM_CHARACTER_SLEEVETYPE = -1,
    }

    public enum CUSTOM_CHARACTER_TYPE
    {
        NONE = 0,
        RANDOM = 1,
        NAMED = 2,
        UNKNOWN_CUSTOM_CHARACTER_TYPE = -1,
    }

    public enum DAMAGE_EFFECT_TYPE_FLAGS
    {
        NONE = 0,
        INCENDIARY = 65536,
        STUN = 131072,
        BLIND = 262144,
        EMP = 524288,
        ACID = 1048576,
        GAS = 8388608,
        IMPACT = 33554432,
        COLLISION = 67108864,
        SLIDING = 268435456,
        MELEE = 134217728,
        ALL = -65536,
        INVALID_FLAG_MASK = 65535,
        PROJECTILE = 2097152,
        ANY_IMPACTING_WEAPON = 201326592,
        UNKNOWN_DAMAGE_EFFECT_TYPE_FLAGS = -1,
    }

    public enum DAMAGE_EFFECTS
    {
        INCENDIARY = 65536,
        EMP = 524288,
        ACID = 1048576,
        GAS = 8388608,
        IMPACT = 33554432,
        BALLISTIC = 2097152,
        COLLISION = 67108864,
        SLIDING = 268435456,
        MELEE = 134217728,
        BALLISTIC_OR_MELEE = 201326592,
        ANY = -65536,
        UNKNOWN_DAMAGE_EFFECTS = -1,
    }

    public enum DAMAGE_MODE
    {
        DAMAGED_BY_ALL = 0,
        DAMAGED_BY_PLAYER_ONLY = 1,
        INVINCIBLE = 2,
        UNKNOWN_DAMAGE_MODE = -1,
    }

    public enum DEATH_STYLE
    {
        PDS_DROP_DEAD = 0,
        PDS_SKIP_ALL_ANIMS = 1,
        PDS_SKIP_ALL_ANIMS_NO_RAGDOLL = 2,
        UNKNOWN_DEATH_STYLE = -1,
    }

    public enum DEVICE_INTERACTION_MODE
    {
        NONE = 0,
        HACKING = 1,
        HACKING_REACTION = 2,
        REWIRE = 3,
        CONTAINER = 4,
        KEYPAD = 5,
        INTERACTIVE_TERMINAL = 6,
        CUTTING_PANEL = 7,
        UNKNOWN_DEVICE_INTERACTION_MODE = -1,
    }

    public enum DIALOGUE_ACTION
    {
        Suspicious_Warning = 0,
        Suspicious_Warning_Fail = 1,
        Missing_Buddy = 2,
        Search_Starts = 3,
        Search_Loop = 4,
        Search_Fail = 5,
        Detected_Enemy = 6,
        Interrogative = 7,
        Warning = 8,
        Last_Chance = 9,
        Stand_Down = 10,
        Use_Cover = 11,
        No_Cover = 12,
        Shoot_From_Cover = 13,
        Cover_Invalidated = 14,
        Alamo = 15,
        Panic = 16,
        Attack = 17,
        Hit_By_Weapon = 18,
        Blocks_Blow = 19,
        Final_Hit = 20,
        Ally_Death = 21,
        Incoming_IED = 22,
        Alert_Squad = 23,
        Idle_Chatter = 24,
        Enter_Grapple = 25,
        Grab_From_Cover = 26,
        Player_Observed = 27,
        Suspicious_Item_Initial = 28,
        Suspicious_Item_CloseTo = 29,
        Melee = 30,
        Advance = 31,
        Death = 32,
        Alien_Heard_Backstage = 33,
        UNKNOWN_DIALOGUE_ACTION = -1,
    }

    public enum DIALOGUE_ARGUMENT
    {
        Character_Voice = 0,
        Dialogue_Progression = 1,
        Target_Character = 2,
        NPC_Group_Status = 3,
        NPC_Dialogue_Mode = 4,
        Action = 5,
        Seen_Target = 6,
        Call_Response = 7,
        Android_Escalation = 8,
        Suspicious_Item_Type = 9,
        Last_Activated_Sense = 10,
        Last_Hit_By_Weapon = 11,
        Injured_State = 12,
        UNKNOWN_DIALOGUE_ARGUMENT = -1,
    }

    public enum DIALOGUE_NPC_COMBAT_MODE
    {
        Area_Sweep = 0,
        Attack = 1,
        Idle = 2,
        Suspect_Response = 3,
        Systematic_Search = 4,
        Target_Lost = 5,
        UNKNOWN_DIALOGUE_NPC_COMBAT_MODE = -1,
    }

    public enum DIALOGUE_NPC_CONTEXT
    {
        Alamo = 0,
        Been_In_Combat = 1,
        Seen_Target = 2,
        Unknown_Target = 3,
        UNKNOWN_DIALOGUE_NPC_CONTEXT = -1,
    }

    public enum DIALOGUE_NPC_EVENT
    {
        SUSPICIOUS_WARNING = 0,
        SUSPICIOUS_WARNING_FAIL = 1,
        MISSING_BUDDY = 2,
        SEARCH_STARTED = 3,
        SEARCH_LOOP = 4,
        SEARCH_COMPLETED = 5,
        DETECTED_ENEMY = 6,
        INTERROGATIVE = 7,
        WARNING = 8,
        LAST_CHANCE = 9,
        STAND_DOWN = 10,
        GO_TO_COVER = 11,
        NO_COVER = 12,
        SHOOT_FROM_COVER = 13,
        COVER_BROKEN = 14,
        ALAMO = 15,
        PANIC = 16,
        ATTACK = 17,
        HIT_BY_WEAPON = 18,
        BLOCK = 19,
        FINAL_HIT = 20,
        ALLY_DEATH = 21,
        INCOMING_IED = 22,
        ALERT_SQUAD = 23,
        IDLE_PASSIVE = 24,
        IDLE_AGGRESSIVE = 25,
        ENTER_GRAPPLE = 26,
        GRAPPLE_FROM_COVER = 27,
        PLAYER_OBSERVED = 28,
        SUSPICIOUS_ITEM_INITIAL = 29,
        SUSPICIOUS_ITEM_CLOSE = 30,
        MELEE = 31,
        ADVANCE = 32,
        MY_DEATH = 33,
        ALIEN_HEARD_BACKSTAGE = 34,
        ALIEN_SIGHTED = 35,
        ALIEN_ATTACKING_ME = 36,
        UNKNOWN_DIALOGUE_NPC_EVENT = -1,
    }

    public enum DIALOGUE_VOICE_ACTOR
    {
        AUTO = 0,
        CV1 = 1,
        CV2 = 2,
        CV3 = 3,
        CV4 = 4,
        CV5 = 5,
        CV6 = 6,
        RT1 = 7,
        RT2 = 8,
        RT3 = 9,
        RT4 = 10,
        AN1 = 11,
        AN2 = 12,
        AN3 = 13,
        ANH = 14,
        TOTAL = 15,
        FIRST_CIVILIAN_MALE_VOICE = 1,
        LAST_CIVILIAN_MALE_VOICE = 4,
        FIRST_CIVILIAN_FEMALE_VOICE = 5,
        LAST_CIVILIAN_FEMALE_VOICE = 6,
        FIRST_SECURITY_MALE_VOICE = 7,
        LAST_SECURITY_MALE_VOICE = 10,
        FIRST_ANDROID_VOICE = 11,
        LAST_ANDROID_VOICE = 13,
        UNKNOWN_DIALOGUE_VOICE_ACTOR = -1,
    }

    public enum DIFFICULTY_SETTING_TYPE
    {
        EASY = 0,
        MEDIUM = 1,
        HARD = 2,
        IRON = 3,
        NOVICE = 4,
        UNKNOWN_DIFFICULTY_SETTING_TYPE = -1,
    }

    public enum DOOR_MECHANISM
    {
        NONE = 0,
        BLANK = 1,
        KEYPAD = 2,
        LEVER = 3,
        BUTTON = 4,
        HACKING = 5,
        HIDDEN_KEYPAD = 6,
        HIDDEN_LEVER = 7,
        HIDDEN_HACKING = 8,
        HIDDEN_BUTTON = 9,
        UNKNOWN_DOOR_MECHANISM = -1,
    }

    public enum DOOR_STATE
    {
        USE_KEYCARD = 0,
        USE_KEYCODE = 1,
        USE_LEVER = 2,
        USE_HACKING = 3,
        USE_CUTTING = 4,
        USE_BUTTON = 5,
        USE_MECHANISM = 6,
        UPGRADE_HACKING = 7,
        HACKING_REQUIRED = 8,
        RESTORE_POWER = 9,
        UPGRADE_CUTTING = 10,
        CUTTING_REQUIRED = 11,
        KEYCARD_REQUIRED = 12,
        LOCKED = 13,
        UNKNOWN_DOOR_STATE = -1,
    }

    public enum DUCK_HEIGHT
    {
        LOW = 0,
        LOWER = 1,
        LOWEST = 2,
        UNKNOWN_DUCK_HEIGHT = -1,
    }

    public enum ENEMY_TYPE
    {
        PLAYER = 1,
        HUMAN = 2,
        HUMAN_AND_PLAYER = 3,
        ANDROID = 4,
        NPC_HUMANOID = 5,
        HUMANOID = 7,
        ALIEN = 8,
        ANY = 15,
        UNKNOWN_ENEMY_TYPE = -1,
    }

    public enum ENVIRONMENT_ARCHETYPE
    {
        SCIENCE = 0,
        HABITATION = 1,
        TECHNICAL = 2,
        ENGINEERING = 3,
        UNKNOWN_ENVIRONMENT_ARCHETYPE = -1,
    }

    public enum EQUIPMENT_SLOT
    {
        USE_CURRENT_SLOT = -2,
        ANY_WEAPON_SLOT = -3,
        WEAPON_SLOT_SHOTGUN = 0,
        WEAPON_SLOT_PISTOL = 1,
        MELEE_SLOT = 2,
        CATTLEPROD_SLOT = 3,
        WEAPON_SLOT_FLAMETHROWER = 4,
        WEAPON_SLOT_BOLTGUN = 5,
        MOTION_TRACKER_SLOT = 6,
        TORCH_SLOT = 7,
        MEDIPEN_SLOT = 8,
        TEMPORARY_WEAPON_SLOT = 9,
        NO_WEAPON_SLOT = 10,
        UNKNOWN_EQUIPMENT_SLOT = -1,
    }

    public enum EVENT_OCCURED_TYPE
    {
        SENSED_TARGET = 0,
        SENSED_SUSPICIOUS_ITEM = 1,
        TARGET_HIDEING = 2,
        SUSPECT_TARGET_RESPONSE = 3,
        UNKNOWN_EVENT_OCCURED_TYPE = -1,
    }

    public enum EXIT_WAYPOINT
    {
        SOLACE = 0,
        AIRPORT = 1,
        TECH_HUB = 2,
        TECH_COMMS = 3,
        SCI_HUB = 4,
        HOSPITAL_UPPER = 5,
        HOSPITAL_LOWER = 6,
        ANDROID_LAB = 7,
        SHOPPING_CENTRE = 8,
        LV426 = 9,
        TECH_RND = 10,
        REACTOR_CORE = 11,
        RND_HAZLAB = 12,
        CORP_PENT = 13,
        APPOLLO_CORE = 14,
        DRY_DOCKS = 15,
        GRAVITY_ANCHORS = 16,
        TOWING_PLATFORM = 17,
        UNKNOWN_EXIT_WAYPOINT = -1,
    }

    public enum FLAG_CHANGE_SOURCE_TYPE
    {
        SCRIPT = 0,
        ACTION = 1,
        UNKNOWN_FLAG_CHANGE_SOURCE_TYPE = -1,
    }

    public enum FLASH_INVOKE_TYPE
    {
        NONE = 0,
        INT = 1,
        FLOAT = 2,
        INT_INT = 3,
        FLOAT_FLOAT = 4,
        INT_FLOAT = 5,
        FLOAT_INT = 6,
        INT_INT_INT = 7,
        FLOAT_FLOAT_FLOAT = 8,
        UNKNOWN_FLASH_INVOKE_TYPE = -1,
    }

    public enum FLASH_SCRIPT_RENDER_TYPE
    {
        NORMAL = 0,
        RENDER_TO_TEXTURE = 1,
        MULTI_RENDER_TO_TEXTURE = 2,
        UNKNOWN_FLASH_SCRIPT_RENDER_TYPE = -1,
    }

    public enum FOG_BOX_TYPE
    {
        BOX = 0,
        PLANE = 1,
        UNKNOWN_FOG_BOX_TYPE = -1,
    }

    public enum FOLDER_LOCK_TYPE
    {
        LOCKED = 0,
        NONE = 1,
        KEYCODE = 2,
        UNKNOWN_FOLDER_LOCK_TYPE = -1,
    }

    public enum FOLLOW_CAMERA_MODIFIERS
    {
        WALKING = 0,
        RUNNING = 1,
        CROUCHING = 2,
        AIMING = 3,
        AIMING_CROUCHED = 4,
        AIMING_THROW = 5,
        AIMING_CROUCHED_THROW = 6,
        EDGE_HORIZONTAL = 7,
        EDGE_VERTICAL = 8,
        COVER_LEFT = 9,
        COVER_RIGHT = 10,
        PEEKING_LEFT = 11,
        PEEKING_RIGHT = 12,
        PEEKING_OVER_LEFT = 13,
        PEEKING_OVER_RIGHT = 14,
        COVER_AIM_LEFT = 15,
        COVER_AIM_RIGHT = 16,
        COVER_AIM_OVER_LEFT = 17,
        COVER_AIM_OVER_RIGHT = 18,
        VENTS_CROUCH = 19,
        VENTS_AIM = 20,
        HIDING_COVER = 21,
        TRAVERSAL_GAP = 22,
        TRAVERSAL_AIMING = 23,
        TRAVERSAL_CLIMB_OVER_VAULT_LOW = 24,
        TRAVERSAL_CLIMB_OVER_VAULT_HIGH = 25,
        TRAVERSAL_CLIMB_OVER_CLAMBER_LOW = 26,
        TRAVERSAL_CLIMB_OVER_CLAMBER_MEDIUM = 27,
        TRAVERSAL_CLIMB_OVER_CLAMBER_HIGH = 28,
        TRAVERSAL_CLIMB_OVER_MANTLE_LOW = 29,
        TRAVERSAL_CLIMB_OVER_MANTLE_MEDIUM = 30,
        TRAVERSAL_CLIMB_OVER_MANTLE_HIGH = 31,
        TRAVERSAL_CLIMB_UNDER = 32,
        TRAVERSAL_LEAP = 33,
        MELEE_ATTACK = 34,
        COVER_LEFT_STANDING = 35,
        COVER_RIGHT_STANDING = 36,
        COVER_TRANSITION_LEFT = 37,
        COVER_TRANSITION_RIGHT = 38,
        HUB_ACCESSED = 39,
        COVER_AIM_LEFT_STANDING = 40,
        COVER_AIM_RIGHT_STANDING = 41,
        SAFE_MODIFIER_STAND = 42,
        SAFE_MODIFIER_CROUCH = 43,
        HIDE_CROUCH = 44,
        SAFE_MODIFIER_AIMING = 45,
        SAFE_MODIFIER_AIMING_CROUCH = 46,
        MOTION_TRACKER = 47,
        MOTION_TRACKER_CROUCHED = 48,
        MOTION_TRACKER_VENTS = 49,
        BOLTGUN_AIM = 50,
        BOLTGUN_AIM_CROUCHED = 51,
        BOLTGUN_AIM_VENTS = 52,
        UNKNOWN_FOLLOW_CAMERA_MODIFIERS = -1,
    }

    public enum FOLLOW_TYPE
    {
        LEADING_THE_WAY = 0,
        EXPLORATION = 1,
        UNKNOWN_FOLLOW_TYPE = -1,
    }

    public enum FRAME_FLAGS
    {
        SUSPICIOUS_ITEM_LOW_PRIORITY = 0,
        SUSPICIOUS_ITEM_MEDIUM_PRIORITY = 1,
        SUSPICIOUS_ITEM_HIGH_PRIORITY = 2,
        COULD_SEARCH = 3,
        COULD_RESPOND_TO_HIDING_PLAYER = 4,
        COULD_DO_SUSPICIOUS_ITEM_HIGH_PRIORITY = 5,
        COULD_DO_SUSPECT_TARGET_RESPONSE_MOVE_TO = 6,
        UNKNOWN_FRAME_FLAGS = -1,
    }

    public enum FRONTEND_STATE
    {
        FRONTEND_STATE_SPLASH = 0,
        FRONTEND_STATE_MENU = 1,
        FRONTEND_STATE_ATTRACT = 2,
        FRONTEND_STATE_DLC_MAP = 3,
        UNKNOWN_FRONTEND_STATE = -1,
    }

    public enum GAME_CLIP
    {
        DEATH_FROM_BELOW = 0,
        THE_HUNT_BEGINS = 1,
        SYNTHETIC_OFFLINE = 2,
        ACCESS_ALL_AREAS = 3,
        GOING_DOWN = 4,
        JUST_IN_TIME = 5,
        SYNTHETIC_INFERNO = 6,
        SYSTEMS_SHOCK = 7,
        HIGHLY_ADAPTABLE = 8,
        GET_BACK = 9,
        HUNTED = 10,
        A_HAZARD_CONTAINED = 11,
        ON_TARGET = 12,
        FLAMIN_JOE = 13,
        DEATH_FROM_ABOVE = 14,
        UNKNOWN_GAME_CLIP = -1,
    }

    public enum GATING_TOOL_TYPE
    {
        KEY = 0,
        AXE = 1,
        CROWBAR = 2,
        GAS_MASK = 3,
        CUTTING_TOOL = 4,
        HACKING_TOOL = 5,
        REWIRE = 6,
        CABLE_CLAMPS = 7,
        UNKNOWN_GATING_TOOL_TYPE = -1,
    }

    public enum IDLE
    {
        STAND = 0,
        QUAD = 1,
        DONT_CHANGE = 2,
        DEFAULT = 3,
        CROUCHED = 4,
        IN_COVER = 5,
        UNKNOWN_IDLE = -1,
    }

    public enum IDLE_STYLE
    {
        NO_FIDGETS = 0,
        NORMAL = 1,
        SEARCH = 2,
        CORPSE_TRAP = 3,
        UNKNOWN_IDLE_STYLE = -1,
    }

    public enum IMPACT_CHARACTER_BODY_LOCATION_TYPE
    {
        ALL = 0,
        ARMS = 1,
        HEAD = 2,
        NECK = 3,
        TORSO = 4,
        LEGS = 5,
        HEAD_NECK = 6,
        TORSO_LEGS = 7,
        TORSO_ARMS = 8,
        TORSO_ARMS_LEGS = 9,
        ARMS_LEGS = 10,
        UNKNOWN_IMPACT_CHARACTER_BODY_LOCATION_TYPE = -1,
    }

    public enum INPUT_DEVICE_TYPE
    {
        MOUSE_AND_KEYBOARD = 0,
        GAMEPAD = 1,
        UNKNOWN_INPUT_DEVICE_TYPE = -1,
    }

    public enum JOB_TYPE
    {
        IDLE = 0,
        DESPAWN = 1,
        AREA_SWEEP = 2,
        AREA_SWEEP_FLARE = 3,
        SYSTEMATIC_SEARCH_TARGET_JOB = 4,
        SYSTEMATIC_SEARCH = 5,
        SYSTEMATIC_SEARCH_FLARE = 6,
        SYSTEMATIC_SEARCH_SPOTTING_POSITION = 7,
        SYSTEMATIC_SEARCH_CRAWL_SPACE_SPOTTING_POSITION = 8,
        PICKUP_WEAPON = 9,
        ASSAULT = 10,
        PLAYER_HIDING = 11,
        FOLLOW = 12,
        FOLLOW_CENTRE = 13,
        PANIC = 14,
        UNKNOWN_JOB_TYPE = -1,
    }

    public enum LEVEL_HEAP_TAG
    {
        AI_MEM_A_CHARACTER = 0,
        AI_MEM_A_ENTITIES = 1,
        AI_MEM_A_JOBS = 2,
        AI_MEM_A_NAV_MESH = 3,
        AI_MEM_AGG_MANAGER = 4,
        AI_MEM_ENEMY_RECORD_CONTAINER = 5,
        AI_MEM_SPOT_TASK_CONTAINER = 6,
        AI_MEM_SUS_ITEM_CONTAINER = 7,
        AI_MEM_SEARCH_TASK_CONTAINER = 8,
        AI_MEM_VENT_MANAGER_CONTAINER = 9,
        AI_MEM_COVER_CONTAINER = 10,
        AI_MEM_DIALOGUE_CONTAINER = 11,
        AI_MEM_SQUAD_CONTAINER = 12,
        AI_MEM_NPC_COMBAT_CONTAINER = 13,
        AI_MEM_NPC_CHARACTER_CONTAINER = 14,
        AI_MEM_NPC_GROUP_MEMBER_CONTAINER = 15,
        AI_MEM_JOB_CONTAINER = 16,
        AI_MEM_VENT_CONTAINER = 17,
        AI_MEM_BLACKBOARD_CONTAINER = 18,
        AI_MEM_CATHODE_SUBS_CONTAINER = 19,
        AI_MEM_PUBLISHER_CONTAINER = 20,
        AI_MEM_THROWN_CONTAINER = 21,
        AI_MEM_SMOKE_MANAGER_CONTAINER = 22,
        UNKNOWN_LEVEL_HEAP_TAG = -1,
    }

    public enum LEVER_TYPE
    {
        PULL = 0,
        ROTATE = 1,
        UNKNOWN_LEVER_TYPE = -1,
    }

    public enum LIGHT_ADAPTATION_MECHANISM
    {
        PERCENTILE = 0,
        BRACKETED_MEAN = 1,
        UNKNOWN_LIGHT_ADAPTATION_MECHANISM = -1,
    }

    public enum LIGHT_ANIM
    {
        UNIFORM = 1,
        PULSATE = 2,
        OSCILLATE = 3,
        FLICKER = 4,
        FLUCTUATE = 5,
        FLICKER_OFF = 6,
        SPARKING = 7,
        BLINK = 8,
        UNKNOWN_LIGHT_ANIM = -1,
    }

    public enum LIGHT_FADE_TYPE
    {
        NONE = 0,
        SHADOW = 1,
        LIGHT = 2,
        UNKNOWN_LIGHT_FADE_TYPE = -1,
    }

    public enum LIGHT_TRANSITION
    {
        INSTANT = 1,
        FADE = 2,
        FLICKER = 3,
        FLICKER_CUSTOM = 4,
        FADE_FLICKER_CUSTOM = 5,
        UNKNOWN_LIGHT_TRANSITION = -1,
    }

    public enum LIGHT_TYPE
    {
        OMNI = 0,
        SPOT = 1,
        STRIP = 2,
        UNKNOWN_LIGHT_TYPE = -1,
    }

    public enum LOCOMOTION_STATE
    {
        WALKING = 0,
        RUNNING = 1,
        CROUCHING = 2,
        IN_VENT = 3,
        AIMING = 4,
        TRAVERSING = 6,
        IDLING = 7,
        UNKNOWN_LOCOMOTION_STATE = -1,
    }

    public enum LOCOMOTION_TARGET_SPEED
    {
        SLOWEST = 0,
        SLOW = 1,
        FAST = 2,
        FASTEST = 3,
        UNKNOWN_LOCOMOTION_TARGET_SPEED = -1,
    }

    public enum LOGIC_CHARACTER_FLAGS
    {
        DONE_BREAKOUT = 0,
        SHOULD_RESET = 1,
        DO_ASSAULT_ATTACK_CHECKS = 2,
        IS_IN_VENT = 3,
        BANNED_FROM_VENT = 4,
        HAS_DONE_GRAPPLE_BREAK = 5,
        HAS_RECEIVED_DOT = 6,
        IS_SITTING = 7,
        DONE_ESCALATION_JOB = 8,
        SHOULD_BREAKOUT = 9,
        SHOULD_ATTACK = 10,
        SHOULD_HIT_AND_RUN = 11,
        DONE_HIT_AND_RUN = 12,
        PLAYER_HIDING = 13,
        ATTACK_HIDING_PLAYER = 14,
        ALIEN_ALWAYS_KNOWS_WHEN_IN_VENT = 15,
        IS_CORPSE_TRAP_ON_START = 16,
        SHOULD_DESPAWN = 17,
        ATTACK_HAS_GOT_WITHIN_ROUTING_THRESHOLD = 18,
        LOCK_BACKSTAGE_STALK = 19,
        TOTALLY_BLIND_IN_DARK = 20,
        PLAYER_WON_HIDING_QTE = 21,
        ANDROID_IS_INERT = 22,
        ANDROID_IS_SHOWROOM_DUMMY = 23,
        SHOULD_AMBUSH = 24,
        NEVER_AGGRESSIVE = 25,
        MUTE_DYNAMIC_DIALOGUE = 26,
        DOING_THREAT_AWARE_ANIM = 27,
        DONE_THREAT_AWARE = 28,
        BLOCK_AMBUSH_AND_KILLTRAPS = 29,
        PREVENT_GRAPPLES = 30,
        PREVENT_ALL_ATTACKS = 31,
        ALLOW_FLANKED_VENT_ATTACK = 32,
        IGNORE_PLAYER_IN_VENT_BEHAVIOUR = 33,
        USE_AIMED_STANCE_FOR_IDLE_JOBS = 34,
        USE_AIMED_LOW_STANCE_FOR_IDLE_JOBS = 35,
        CLOSE_TO_BACKSTAGE_ALIEN = 36,
        IS_IN_EXPLOITABLE_AREA = 37,
        IS_ON_LADDER = 38,
        HAS_REPEATED_PATHFIND_FAILURES = 39,
        UNKNOWN_LOGIC_CHARACTER_FLAGS = -1,
    }

    public enum LOGIC_CHARACTER_GAUGE_TYPE
    {
        RETREAT_GAUGE = 0,
        STUN_DAMAGE_GAUGE = 1,
        UNKNOWN_LOGIC_CHARACTER_GAUGE_TYPE = -1,
    }

    public enum LOGIC_CHARACTER_TIMER_TYPE
    {
        SUSPECT_TARGET_RESPONSE_DELAY_TIMER = 0,
        FIRST_LOGIC_CHARACTER_TIMER = 0,
        THREAT_AWARE_TIMEOUT_TIMER = 1,
        THREAT_AWARE_DURATION_TIMER = 2,
        SEARCH_TIMEOUT_TIMER = 3,
        BACKSTAGE_STALK_TIMEOUT_TIMER = 4,
        AMBUSH_TIMEOUT_TIMER = 5,
        ATTACK_BAN_TIMER = 6,
        MELEE_ATTACK_BAN_TIMER = 7,
        VENT_BAN_TIMER = 8,
        NPC_STAY_IN_COVER_SHOOT_TIMER = 9,
        NPC_JUST_LEFT_COMBAT_TIMER = 10,
        ATTACK_KEEP_CHASING_TIMER = 11,
        DELAY_RETURN_TO_SPAWN_POINT_TIMER = 12,
        TARGET_IN_CRAWLSPACE_TIMER = 13,
        DURATION_SINCE_SEARCH_TIMER = 14,
        HEIGHTENED_SENSES_TIMER = 15,
        BACKSTAGE_STALK_PICK_KILLTRAP_TIMER = 16,
        FLANKED_VENT_ATTACK_TIMER = 17,
        THREAT_AWARE_VISUAL_RETENTION_TIMER = 18,
        RESPONSE_TO_BACKSTAGE_ALIEN_TIMEOUT_TIMER = 19,
        VENT_ATTRACT_TIMER = 20,
        SEEN_PLAYER_AIM_WEAPON_TIMER = 21,
        SEARCH_BAN_TIMER = 22,
        OBSERVE_TARGET_TIMER = 23,
        REPEATED_PATHFIND_FAILUREST_TIMER = 24,
        UNKNOWN_LOGIC_CHARACTER_TIMER_TYPE = -1,
    }

    public enum LOOK_SPEED
    {
        SLOW = 0,
        MODERATE = 1,
        FAST = 2,
        FREE_LOOK = 3,
        LEADING_LOOK = 4,
        UNKNOWN_LOOK_SPEED = -1,
    }

    public enum MAP_ICON_TYPE
    {
        REWIRE = 0,
        HACKING = 1,
        KEYPAD = 2,
        LOCKED = 3,
        SAVEPOINT = 4,
        WAYPOINT = 5,
        TOOL_PICKUP = 6,
        WEAPON_PICKUP = 7,
        CUTTING_POINT = 8,
        LEVEL_LOAD = 9,
        LADDER = 10,
        CONTAINER = 11,
        TERMINAL_INTERACTION = 12,
        LOCKED_DOOR = 13,
        UNLOCKED_DOOR = 14,
        HIDDEN = 15,
        GENERIC_INTERACTION = 16,
        POWERED_DOWN_DOOR = 17,
        KEYCODE = 18,
        UNKNOWN_MAP_ICON_TYPE = -1,
    }

    public enum MELEE_ATTACK_TYPE
    {
        HIT_ATTACK = 0,
        GRAPPLE_ATTACK = 1,
        KILL_ATTACK = 2,
        VENT = 3,
        TRAP_ATTACK = 4,
        ANY = 5,
        NONE = 6,
        UNKNOWN_MELEE_ATTACK_TYPE = -1,
    }

    public enum MELEE_CONTEXT_TYPE
    {
        ANDROID_LOW_COVER_GRAPPLE = 0,
        ANDROID_HIGH_COVER_GRAPPLE = 1,
        ANDROID_CORPSE_TRAP_GRAPPLE = 2,
        UNKNOWN_MELEE_CONTEXT_TYPE = -1,
    }

    public enum MOOD
    {
        NEUTRAL = 0,
        SCARED = 1,
        ANGRY = 2,
        HAPPY = 3,
        SAD = 4,
        SUSPICIOUS = 5,
        INJURED = 6,
        UNKNOWN_MOOD = -1,
    }

    public enum MOOD_INTENSITY
    {
        LOW = 0,
        MEDIUM = 1,
        HIGH = 2,
        UNKNOWN_MOOD_INTENSITY = -1,
    }

    public enum MOVE
    {
        SLOW_WALK = 0,
        WALK = 1,
        FAST_WALK = 2,
        RUN = 3,
        OBSOLETE_1 = 4,
        OBSOLETE_2 = 5,
        TELEPORT = 6,
        UNKNOWN_MOVE = -1,
    }

    public enum MUSIC_RTPC_MODE
    {
        UNCHANGED = 0,
        THREAT = 1,
        STEALTH = 2,
        ALIEN_DISTANCE = 3,
        MANUAL = 4,
        OBJECT_DISTANCE = 5,
        UNKNOWN_MUSIC_RTPC_MODE = -1,
    }

    public enum NAV_MESH_AREA_TYPE
    {
        BACKSTAGE = 0,
        EXPENSIVE = 1,
        UNKNOWN_NAV_MESH_AREA_TYPE = -1,
    }

    public enum NAVIGATION_CHARACTER_CLASS
    {
        PLAYER = 0,
        ALIEN = 1,
        ANDROID = 2,
        HUMAN_NPC = 3,
        FACEHUGGER = 4,
        UNKNOWN_NAVIGATION_CHARACTER_CLASS = -1,
    }

    public enum NAVIGATION_CHARACTER_CLASS_COMBINATION
    {
        NONE = 0,
        PLAYER = 1,
        ALIEN = 2,
        ANDROID = 4,
        HUMAN_NPC = 8,
        FACEHUGGER = 16,
        HUMAN = 9,
        HUMANOID_NPC = 13,
        HUMANOID = 13,
        ANDROID_AND_ALIEN = 6,
        PLAYER_AND_ALIEN = 3,
        ALL = 31,
        UNKNOWN_NAVIGATION_CHARACTER_CLASS_COMBINATION = -1,
    }

    public enum NOISE_TYPE
    {
        HARMONIC = 0,
        FRACTAL = 1,
        UNKNOWN_NOISE_TYPE = -1,
    }

    public enum NPC_AGGRO_LEVEL
    {
        NONE = 0,
        STAND_DOWN = 1,
        INTERROGATIVE = 2,
        WARNING = 3,
        LAST_CHANCE = 4,
        NO_LIMIT = 5,
        AGGRESSIVE = 5,
        UNKNOWN_NPC_AGGRO_LEVEL = -1,
    }

    public enum NPC_COMBAT_STATE
    {
        NONE = 0,
        WARNING = 1,
        ATTACKING = 2,
        REACHED_OBJECTIVE = 3,
        ENTERED_COVER = 4,
        LEAVE_COVER = 5,
        START_RETREATING = 6,
        REACHED_RETREAT = 7,
        LOST_SENSE = 8,
        SUSPICIOUS_WARNING = 9,
        SUSPICIOUS_WARNING_FAILED = 10,
        START_ADVANCE = 11,
        DONE_ADVANCE = 12,
        BLOCKING = 13,
        HEARD_BS_ALIEN = 14,
        ALIEN_SIGHTED = 15,
        UNKNOWN_NPC_COMBAT_STATE = -1,
    }

    public enum NPC_COVER_REQUEST_TYPE
    {
        DEFAULT = 0,
        RETREAT = 1,
        AGGRESSIVE = 2,
        DEFENSIVE = 3,
        ALIEN = 4,
        PLAYER_IN_VENT = 5,
        UNKNOWN_NPC_COVER_REQUEST_TYPE = -1,
    }

    public enum NPC_GUN_AIM_MODE
    {
        AUTO = 0,
        GUN_DOWN = 1,
        GUN_RAISED = 2,
        UNKNOWN_NPC_GUN_AIM_MODE = -1,
    }

    public enum ORIENTATION_AXIS
    {
        X_AXIS = 0,
        Y_AXIS = 1,
        Z_AXIS = 2,
        UNKNOWN_ORIENTATION_AXIS = -1,
    }

    public enum PATH_DRIVEN_TYPE
    {
        CHARACTER_PROJECTION = 0,
        POINT_PROJECTION = 1,
        TIME_PROGRESS = 2,
        UNKNOWN_PATH_DRIVEN_TYPE = -1,
    }

    public enum PAUSE_SENSES_TYPE
    {
        KNOCKED_OUT = 0,
        UNKNOWN_PAUSE_SENSES_TYPE = -1,
    }

    public enum PICKUP_CATEGORY
    {
        UNKNOWN = 0,
        WEAPON = 1,
        AMMO = 2,
        ITEM = 3,
        KILLTRAP = 4,
        DOOR = 5,
        COMPUTER = 6,
        ALIEN = 7,
        SAVE_TERMINAL = 8,
        UNKNOWN_PICKUP_CATEGORY = -1,
    }

    public enum PLATFORM_TYPE
    {
        PL_PC = 0,
        PL_PS3 = 1,
        PL_X360 = 2,
        PL_PS4 = 3,
        PL_XBOXONE = 4,
        PL_OLDGEN = 5,
        PL_NEXTGEN = 6,
        UNKNOWN_PLATFORM_TYPE = -1,
    }

    public enum PLAYER_INVENTORY_SET
    {
        DEFAULT_PLAYER = 0,
        LV426_PLAYER = 1,
        OTHER_PLAYER = 2,
        UNKNOWN_PLAYER_INVENTORY_SET = -1,
    }

    public enum POPUP_MESSAGE_ICON
    {
        ALERT = 0,
        AUDIOLOG = 1,
        UNKNOWN_POPUP_MESSAGE_ICON = -1,
    }

    public enum POPUP_MESSAGE_SOUND
    {
        NONE = 0,
        OBJECTIVE_NEW = 1,
        OBJECTIVE_UPDATED = 2,
        OBJECTIVE_COMPLETED = 3,
        UNKNOWN_POPUP_MESSAGE_SOUND = -1,
    }

    public enum PRIORITY
    {
        LOWEST = 0,
        LOW = 1,
        MEDIUM = 2,
        HIGH = 3,
        HIGHEST = 4,
        UNKNOWN_PRIORITY = -1,
    }

    public enum RANGE_TEST_SHAPE
    {
        SPHERE = 0,
        VERTICAL_CYLINDER = 1,
        UNKNOWN_RANGE_TEST_SHAPE = -1,
    }

    public enum RAYCAST_PRIORITY
    {
        LOW = 0,
        MEDIUM = 1,
        HIGH = 2,
        CRITICAL = 3,
        UNKNOWN_RAYCAST_PRIORITY = -1,
    }

    public enum RESPAWN_MODE
    {
        ON_DEATH_POINT = 0,
        NEAR_DEATH_POINT = 1,
        NEAR_COMPANION = 2,
        UNKNOWN_RESPAWN_MODE = -1,
    }

    public enum REWIRE_SYSTEM_NAME
    {
        AI_UI_MAIN_LIGHTING = 0,
        AI_UI_DOOR_SYSTEM = 1,
        AI_UI_VENT_ACCESS = 2,
        AI_UI_SECURITY_CAMERAS = 3,
        AI_UI_TANNOY = 4,
        AI_UI_ALARMS = 5,
        AI_UI_SPRINKLERS = 6,
        AI_UI_RELEASE_VALVE = 7,
        AI_UI_GAS_LEAK = 8,
        AI_UI_AIR_CONDITIONING = 9,
        AI_UI_UNSTABLE_SYSTEM = 10,
        UNKNOWN_REWIRE_SYSTEM_NAME = -1,
    }

    public enum REWIRE_SYSTEM_TYPE
    {
        AI_UI_MAIN_LIGHTING = 0,
        AI_UI_DOOR_SYSTEM = 1,
        AI_UI_VENT_ACCESS = 2,
        AI_UI_SECURITY_CAMERAS = 3,
        AI_UI_TANNOY = 4,
        AI_UI_ALARMS = 5,
        AI_UI_SPRINKLERS = 6,
        AI_UI_RELEASE_VALVE = 7,
        AI_UI_GAS_LEAK = 8,
        AI_UI_AIR_CONDITIONING = 9,
        AI_UI_UNSTABLE_SYSTEM = 10,
        UNKNOWN_REWIRE_SYSTEM_TYPE = -1,
    }

    public enum SECONDARY_ANIMATION_LAYER
    {
        GENERAL_ADDITIVE = 0,
        BREATHE = 1,
        GUN = 2,
        TAIL = 3,
        LOOK = 4,
        HANDS = 5,
        FACE = 6,
        UNKNOWN_SECONDARY_ANIMATION_LAYER = -1,
    }

    public enum SENSE_SET
    {
        SET_1 = 0,
        SET_2 = 2,
        SET_3 = 3,
        UNKNOWN_SENSE_SET = -1,
    }

    public enum SENSE_SET_DEFAULT
    {
        SET_NORMAL = 0,
        SET_HEIGHTENED = 1,
        UNKNOWN_SENSE_SET_DEFAULT = -1,
    }

    public enum SENSE_SET_SYSTEM
    {
        SET_1_NORMAL = 0,
        SET_1_HEIGHTENED = 1,
        SET_LAST_DEFAULT = 1,
        SET_2 = 2,
        SET_3 = 3,
        UNKNOWN_SENSE_SET_SYSTEM = -1,
    }

    public enum SENSORY_TYPE
    {
        NONE = -1,
        VISUAL = 0,
        WEAPON_SOUND = 1,
        MOVEMENT_SOUND = 2,
        DAMAGE_CAUSED = 3,
        TOUCHED = 4,
        AFFECTED_BY_FLAME_THROWER = 5,
        SEE_FLASH_LIGHT = 6,
        COMBINED_SENSE = 7,
    }

    public enum SHAKE_TYPE
    {
        CONSTANT = 0,
        IMPULSE = 1,
        UNKNOWN_SHAKE_TYPE = -1,
    }

    public enum SIDE
    {
        LEFT = 0,
        RIGHT = 1,
        UNKNOWN_SIDE = -1,
    }

    public enum SOUND_POOL
    {
        GENERAL = 0,
        PLAYER_WEAPON = 1,
        UNKNOWN_SOUND_POOL = -1,
    }

    public enum SPEECH_PRIORITY
    {
        LOW = 0,
        MEDIUM = 1,
        HIGH = 2,
        STREAMED_COMBAT = 3,
        COMBAT = 4,
        DEATH = 5,
        UNKNOWN_SPEECH_PRIORITY = -1,
    }

    public enum STEAL_CAMERA_TYPE
    {
        FORCE_STEAL = 0,
        BUTTON_PROMPT = 1,
        JUST_CONVERGE = 2,
        UNKNOWN_STEAL_CAMERA_TYPE = -1,
    }

    public enum SUB_OBJECTIVE_TYPE
    {
        NONE = 0,
        POINT = 1,
        SMALL_AREA = 2,
        MEDIUM_AREA = 3,
        LARGE_AREA = 4,
        UNKNOWN_SUB_OBJECTIVE_TYPE = -1,
    }

    public enum SUSPECT_RESPONSE_PHASE
    {
        HEARD_GUN_SHOT = 0,
        IDLE = 1,
        VISUAL_SUCCESS = 2,
        VISUAL_FAIL = 3,
        UNKNOWN_SUSPECT_RESPONSE_PHASE = -1,
    }

    public enum SUSPICIOUS_ITEM
    {
        EXPLOSION = 0,
        DEAD_BODY = 1,
        ALARM = 2,
        VENT_HISS = 3,
        ACTIVE_FLARE = 4,
        CUT_PANEL = 5,
        FIRE_EXTINGUISHER = 6,
        LIGHTS_ON = 7,
        LIGHTS_OFF = 8,
        DOOR_OPENED = 9,
        DOOR_CLOSED = 10,
        DISGUARDED_GUN = 11,
        DEAD_FLARE = 12,
        GLOW_STICK = 13,
        OPEN_CONTAINER = 14,
        NOISE_MAKER = 15,
        EMP_MINE = 16,
        FLASH_BANG = 17,
        MOLOTOV = 18,
        PIPE_BOMB = 19,
        SMOKE_BOMB = 20,
        UNKNOWN_SUSPICIOUS_ITEM = -1,
    }

    public enum SUSPICIOUS_ITEM_BEHAVIOUR_TREE_PRIORITY
    {
        LOW = 0,
        MEDIUM = 1,
        HIGH = 2,
        UNKNOWN_SUSPICIOUS_ITEM_BEHAVIOUR_TREE_PRIORITY = -1,
    }

    public enum SUSPICIOUS_ITEM_CLOSE_REACTION_DETAIL
    {
        VISUAL_FLOOR = 0,
        VISUAL_WALL = 1,
        UNKNOWN_SUSPICIOUS_ITEM_CLOSE_REACTION_DETAIL = -1,
    }

    public enum SUSPICIOUS_ITEM_REACTION
    {
        INITIAL_REACTION = 0,
        CLOSE_TO_FIRST_GROUP_MEMBER_REACTION = 1,
        CLOSE_TO_SUBSEQUENT_GROUP_MEMBER_REACTION = 2,
        UNKNOWN_SUSPICIOUS_ITEM_REACTION = -1,
    }

    public enum SUSPICIOUS_ITEM_STAGE
    {
        NONE = 0,
        FIRST_SENSED = 1,
        INITIAL_REACTION = 2,
        WAIT_FOR_TEAM_MEMBERS_ROUTING = 3,
        MOVE_CLOSE_TO = 4,
        CLOSE_TO_REACTION = 5,
        CLOSE_TO_WAIT_FOR_GROUP_MEMBERS = 6,
        SEARCH_AREA = 7,
        UNKNOWN_SUSPICIOUS_ITEM_STAGE = -1,
    }

    public enum SUSPICIOUS_ITEM_START_OR_CONTINUE_STATE
    {
        VALID_CURRENT_AND_IN_PROGRESS = 0,
        VALID_TO_DO_INITIAL_REACTION = 1,
        VALID_TO_DO_FURTHER_REACTION = 2,
        LAST_VALID_STATE = 2,
        DELAYED_INTERACTION_DELAY = 3,
        LAST_VALID_OR_DELAYED_STATE = 3,
        INVALID_NO_ITEM = 4,
        INVALID_ALREADY_COMPLETED = 5,
        INVALID_ALREADY_DONE_MIN_STAGE = 6,
        INVALID_NO_MUST_DO_STAGE = 7,
        INVALID_INTERACTION_DELAYED = 8,
        INVALID_MEMBER_NOT_ALLOWED_TO_PROGRESS = 9,
        INVALID_FUTHER_REACTION_TIMED_OUT_IN_PROGRESS = 10,
        INVALID_FUTHER_REACTION_TIMED_OUT_LAST_TIME_TRIGGERED = 11,
        UNKNOWN_SUSPICIOUS_ITEM_START_OR_CONTINUE_STATE = -1,
    }

    public enum SUSPICIOUS_ITEM_TRIGGER
    {
        VISBLE = 0,
        INSTANT = 1,
        CONTINUOUS = 2,
        UNKNOWN_SUSPICIOUS_ITEM_TRIGGER = -1,
    }

    public enum TASK_CHARACTER_CLASS_FILTER
    {
        USE_CHARACTER_PIN = 0,
        PLAYER_ONLY = 1,
        ALIEN_ONLY = 2,
        ANDROID_ONLY = 4,
        CIVILIAN_ONLY = 8,
        SECURITY_ONLY = 16,
        FACEHUGGER_ONLY = 32,
        INNOCENT_ONLY = 64,
        ANDROID_HEAVY_ONLY = 128,
        MOTION_TRACKER = 256,
        MELEE_HUMAN_ONLY = 512,
        ANDROIDS = 132,
        ALIENS = 34,
        HUMAN_NPC = 600,
        HUMAN = 601,
        HUMANOID_NPC = 732,
        HUMANOID = 733,
        ANDROIDS_AND_ALIEN = 166,
        PLAYER_AND_ALIEN = 35,
        ALL = 1023,
        USE_FILTER_PIN = 1024,
        EXCLUDE_CHARACTER_PIN = 1025,
        UNKNOWN_TASK_CHARACTER_CLASS_FILTER = -1,
    }

    public enum TASK_OPERATION_MODE
    {
        FULLY_SHARED = 0,
        SINGLE_AND_EXCLUSIVE = 1,
        UNKNOWN_TASK_OPERATION_MODE = -1,
    }

    public enum TASK_PRIORITY
    {
        NORMAL = 0,
        MEDIUM = 1,
        HIGH = 2,
        UNKNOWN_TASK_PRIORITY = -1,
    }

    public enum TERMINAL_LOCATION
    {
        SEVASTOPOL = 0,
        TORRENS = 1,
        NOSTROMO = 2,
        UNKNOWN_TERMINAL_LOCATION = -1,
    }

    public enum TEXT_ALIGNMENT
    {
        TOP_LEFT = 0,
        TOP_CENTRE = 1,
        TOP_RIGHT = 2,
        LEFT = 3,
        CENTRE = 4,
        RIGHT = 5,
        BOTTOM_LEFT = 6,
        BOTTOM_CENTRE = 7,
        BOTTOM_RIGHT = 8,
        UNKNOWN_TEXT_ALIGNMENT = -1,
    }

    public enum THRESHOLD_QUALIFIER
    {
        UNSET = -1,
        NONE = -1,
        TRACE = 0,
        LOWER = 1,
        ACTIVATED = 2,
        UPPER = 3,
    }

    public enum TRANSITION_DIRECTION
    {
        POSITIVE_X = 0,
        NEGATIVE_X = 1,
        POSITIVE_Y = 2,
        NEGATIVE_Y = 3,
        CENTER = 4,
        UNKNOWN_TRANSITION_DIRECTION = -1,
    }

    public enum TRAVERSAL_ANIMS
    {
        Leap_Small = 0,
        Leap_Medium = 1,
        Leap_Large = 2,
        Mantle_Small = 3,
        Mantle_Medium = 4,
        Mantle_Large = 5,
        Vault_Small = 6,
        Vault_Medium = 7,
        Vault_Large = 8,
        Custom = 9,
        UNKNOWN_TRAVERSAL_ANIMS = -1,
    }

    public enum TRAVERSAL_TYPE
    {
        VAULT = 0,
        MANTLE = 1,
        CLIMB_OVER = 2,
        JUMP_DOWN = 3,
        VENT_ENTRY = 4,
        VENT_EXIT = 5,
        LADDER = 6,
        FLOOR_VENT_ENTRY = 7,
        FLOOR_VENT_EXIT = 8,
        UNKNOWN_TRAVERSAL_TYPE = -1,
    }

    public enum UI_ICON_ICON
    {
        IMPORTANT = 0,
        CONTAINER = 1,
        UNKNOWN_UI_ICON_ICON = -1,
    }

    public enum UI_KEYGATE_TYPE
    {
        KEYCARD = 0,
        KEYPAD = 1,
        UNKNOWN_UI_KEYGATE_TYPE = -1,
    }

    public enum VENT_LOCK_REASON
    {
        FLANKED_VENT_ATTACK_FROM_ATTACK = 0,
        FLANKED_VENT_ATTACK_FROM_THREAT_AWARE = 1,
        UNKNOWN_VENT_LOCK_REASON = -1,
    }

    public enum VIEWCONE_TYPE
    {
        RECTANGLE = 1,
        ELLIPSE = 2,
        UNKNOWN_VIEWCONE_TYPE = -1,
    }

    public enum VISIBILITY_SETTINGS_TYPE
    {
        VIEWCONESET_NONE = 0,
        VIEWCONESET_STANDARD = 1,
        VIEWCONESET_STANDARD_HIEGHTENED = 2,
        VIEWCONESET_HUMAN = 3,
        VIEWCONESET_HUMAN_HIEGHTENED = 4,
        VIEWCONESET_SLEEPING = 5,
        VIEWCONESET_ANDROID = 6,
        VIEWCONESET_ANDROID_HIEGHTENED = 7,
        UNKNOWN_VISIBILITY_SETTINGS_TYPE = -1,
    }

    public enum WAVE_SHAPE
    {
        SIN = 0,
        SAW = 1,
        REV_SAW = 2,
        SQUARE = 3,
        TRIANGLE = 4,
        UNKNOWN_WAVE_SHAPE = -1,
    }

    public enum WEAPON_HANDEDNESS
    {
        TWO_HANDED = 0,
        ONE_HANDED = 1,
        ONE_OR_TWO_HANDED = 2,
        UNKNOWN_WEAPON_HANDEDNESS = -1,
    }

    public enum WEAPON_IMPACT_EFFECT_ORIENTATION
    {
        HIT_NORMAL = 0,
        DIRECTION = 1,
        REFLECTION = 2,
        UP = 3,
        UNKNOWN_WEAPON_IMPACT_EFFECT_ORIENTATION = -1,
    }

    public enum WEAPON_IMPACT_EFFECT_TYPE
    {
        STANDARD = 0,
        CHARACTER_DAMAGE = 1,
        UNKNOWN_WEAPON_IMPACT_EFFECT_TYPE = -1,
    }

    public enum WEAPON_IMPACT_FILTER_ORIENTATION
    {
        CEILING = 0,
        FLOOR = 1,
        WALL = 2,
        UNKNOWN_WEAPON_IMPACT_FILTER_ORIENTATION = -1,
    }

    public enum WEAPON_PROPERTY
    {
        ALIEN_THREAT_AWARE_OF = 0,
        UNKNOWN_WEAPON_PROPERTY = -1,
    }

    public enum WEAPON_TYPE
    {
        NO_WEAPON = 0,
        FLAMETHROWER = 1,
        PISTOL = 2,
        SHOTGUN = 3,
        MELEE = 4,
        BOLTGUN = 5,
        CATTLEPROD = 6,
        UNKNOWN_WEAPON_TYPE = -1,
    }
}