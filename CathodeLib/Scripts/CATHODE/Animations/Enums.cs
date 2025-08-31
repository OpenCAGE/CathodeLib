using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Animations
{
    public enum AnimationBlendNodeType
    {
        LEAF_NODE,
        RANDOMISED_LEAF_NODE,
        SELECTOR_NODE,
        PARAMETRIC_NODE,
        BLEND_SET_NODE,
        BLEND_SET_4D_NODE,
        SEQUENCE_NODE,
        BILINEAR_NODE,
        LO_FI_BILINEAR_NODE,
        ADDITIVE_BLEND_NODE,
        PARAMETRIC_ADDITIVE_BLEND_NODE,
        RANGED_SELECTOR_NODE,
        ENUMERATED_SELECTOR_NODE,
        FOOT_SYNC_SELECTOR_NODE,
        BONE_MASK_NODE,
        IK_NODE,
        SPHERICAL_NODE,
        WEIGHTED_NODE,
        TOP_LEVEL_NODE,
    }

    [Flags]
    public enum BoneMaskGroups
    {
        NONE = 0,
        HIPS = 1,
        TORSO = 1 << 1,
        NECK = 1 << 2,
        HEAD = 1 << 3,
        FACE = 1 << 4,
        LEFT_LEG = 1 << 5,
        RIGHT_LEG = 1 << 6,
        LEFT_ARM = 1 << 7,
        RIGHT_ARM = 1 << 8,
        LEFT_HAND = 1 << 9,
        RIGHT_HAND = 1 << 10,
        LEFT_FINGERS = 1 << 11,
        RIGHT_FINGERS = 1 << 12,
        TAIL = 1 << 13,
        LIPS = 1 << 14,
        EYES = 1 << 15,
        LEFT_SHOULDER = 1 << 16,
        RIGHT_SHOULDER = 1 << 17,
        ROOT = 1 << 18,
    }

    public enum IkControlTarget
    {
        LEFT_FOOT,
        RIGHT_FOOT,
        LEFT_HAND,
        RIGHT_HAND,
        LEFT_FOOT_REFERENCE,
        RIGHT_FOOT_REFERENCE,
        LEFT_HAND_REFERENCE,
        RIGHT_HAND_REFERENCE,
        HEAD,
        EYES,
        SPINE,
        TAIL,
        LEFT_WEAPON_BONE,
        RIGHT_WEAPON_BONE,
        HIPS,
    }

    public enum IkSolverType
    {
        ANALYTICAL,
        LOOK_AT,
        CCD,
        SINGLE_BONE,
    }

    public enum AnimTreeParameterType
    {
        CARD32,
        FLOAT,
        STRING,
        MATRIX,
        VECTOR,
    }

    public enum AnimationParameterBlendUsage
    {
        Clamp,
        LoopOver,
        ExtrapolateBlend
    }

    public enum AnimationMetadataValueType
    {
        UINT32,
        INT32,
        FLOAT32,
        STRING,
        BOOL,
        VECTOR,
        UINT64,
        INT64,
        FLOAT64,
        AUDIO,
        PROPERTY_REFERENCE,
        SCRIPT_INTERFACE,
    }

    public enum FootStrikeSelectionMethod
    {
        NearestStrike = 0,
        NextStrike
    }
}
