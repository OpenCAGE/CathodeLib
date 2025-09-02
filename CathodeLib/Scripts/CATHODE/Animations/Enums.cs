using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Animations
{
    //Any types in the negative are not written to disk and only handled while processing
    public enum AnimationNodeType
    {
        ANIM_Base = -1, //Base class
        ANIM_IK = 15,
        ANIM_Bone_Mask = 14,
        ANIM_Animation = 0,
        ANIM_Randomised_Animation = 1,
        ANIM_AutoFloatParameter = -2,
        ANIM_Parameter = -3,
        ANIM_FloatInterpolator = -4,
        ANIM_Property = -5,
        ANIM_Callback = -6,
        ANIM_Metadata_Event_Listener = -7,
        ANIM_Selector = 2,
        ANIM_Enumerated_Selector = 12,
        ANIM_Ranged_Selector = 11,
        ANIM_Foot_Sync_Selector = 13,
        ANIM_Parametric = 3,
        ANIM_2DParametric = 4,
        ANIM_3DParametric = -8,
        ANIM_4DParametric = 5,
        ANIM_Bilinear_High_Fidelity = 7,
        ANIM_Bilinear_Low_Fidelity = 8,
        ANIM_Additive_Blend = 9,
        ANIM_Parametric_Additive_Blend = 10,
        ANIM_Spherical = 16,
        ANIM_Tree_Top_Level = -9,
        ANIM_Weighted = 17,
        ANIM_Event_Callback = -10,
        ANIM_Property_Listener = -11,
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
