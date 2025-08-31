using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Animations
{
    public class AnimationNode
    {
        public AnimationBlendNodeType NodeType;
        public string NodeName;
        public List<AnimationNode> Children = new List<AnimationNode>();
    }

    public class LeafNode : AnimationNode
    {
        public bool HasCallback;
        public string CallbackName;
        public bool Looped;
        public bool Mirrored;
        public BoneMaskGroups MaskingControl;
        public uint LevelAnimIndex;
        public uint OptionalContextParam;
        public uint OptionalConvergeVector;
        public uint OptionalConvergeFloat;
        public bool ConvergeOrientation;
        public bool ConvergeTranslation;
        public float NotifyTimeOffset;
        public float StartTimeOffset;
        public float EndTimeOffset;
    }

    public class SelectorNode : AnimationNode
    {
        public List<string> Bindings = new List<string>();
        public List<uint> ValueBindings = new List<uint>();
        public float EaseTime;
        public string BindingParameterName;
        public List<bool> FootSyncOnSelect = new List<bool>();
        public bool ResetPlaybackOnChange;

        public SelectorNode()
        {
            NodeType = AnimationBlendNodeType.SELECTOR_NODE;
        }
    }

    public class EnumeratedSelectorNode : AnimationNode
    {
        public List<string> Bindings = new List<string>();
        public List<uint> ValueBindings = new List<uint>();
        public float EaseTime;
        public string BindingParameterName;
        public List<bool> FootSyncOnSelect = new List<bool>();
        public bool ResetPlaybackOnChange;

        public EnumeratedSelectorNode()
        {
            NodeType = AnimationBlendNodeType.ENUMERATED_SELECTOR_NODE;
        }
    }

    public class ParametricNode : AnimationNode
    {
        public List<string> Bindings = new List<string>();
        public List<float> ValueBindings = new List<float>();

        public string BindingParameterName;
        public float Min;
        public float Max = 1.0f;
        public uint ParameterUsage;
        public uint AutoBlendProperty;
        public bool SyncDurations;
        public bool UseAutoDerivedBlendValues;

        public ParametricNode()
        {
            NodeType = AnimationBlendNodeType.PARAMETRIC_NODE;
        }
    }

    public class BlendSetNode : AnimationNode
    {
        public uint[] BlendSet = new uint[1];

        public uint XParameter;
        public uint YParameter;
        public uint ZParameter;
        public uint OverflowListener;
        public bool LoopBlendSet;
        public bool SyncBlendSet;

        public BlendSetNode()
        {
            NodeType = AnimationBlendNodeType.BLEND_SET_NODE;
        }
    }

    public class BlendSet4DNode : BlendSetNode
    {
        public new uint[] BlendSet = new uint[2];

        public uint WParameter;

        public BlendSet4DNode()
        {
            NodeType = AnimationBlendNodeType.BLEND_SET_4D_NODE;
        }
    }

    public class BilinearNode : AnimationNode
    {
        public string[] Bindings = new string[9];

        public string XParameter;
        public string YParameter;
        public float ParameterMin;
        public float ParameterMax = 1.0f;
        public bool ParameterWrap;
        public bool SyncDurations;

        public BilinearNode()
        {
            NodeType = AnimationBlendNodeType.BILINEAR_NODE;
        }
    }

    public class LoFiBilinearNode : BilinearNode
    {
        public new string[] Bindings = new string[4];

        public LoFiBilinearNode()
        {
            NodeType = AnimationBlendNodeType.LO_FI_BILINEAR_NODE;
        }
    }

    public class AdditiveBlendNode : AnimationNode
    {
        public string BaseNodeName;
        public string AdditiveNodeName;

        public float AdditiveNodeWeight = 1.0f;
        public bool SyncAdditiveDurationToBase;

        public AdditiveBlendNode()
        {
            NodeType = AnimationBlendNodeType.ADDITIVE_BLEND_NODE;
        }
    }

    public class ParametricAdditiveBlendNode : AdditiveBlendNode
    {
        public string ParameterName;

        public float ParameterMin;
        public float ParameterMax = 1.0f;

        public ParametricAdditiveBlendNode()
        {
            NodeType = AnimationBlendNodeType.PARAMETRIC_ADDITIVE_BLEND_NODE;
        }
    }

    public class RangedSelectorNode : AnimationNode
    {
        public List<string> Bindings = new List<string>();
        public List<float> MinValueBindings = new List<float>();
        public List<float> MaxValueBindings = new List<float>();

        public float EaseTime;
        public string BindingParameterName;
        public List<bool> FootSyncOnSelect = new List<bool>();
        public bool ResetPlaybackOnChange;

        public RangedSelectorNode()
        {
            NodeType = AnimationBlendNodeType.RANGED_SELECTOR_NODE;
        }
    }

    public class FootSyncSelectorNode : AnimationNode
    {
        public string[] Bindings = new string[2];

        public uint FootStrikeSelectionMethod;
        public bool GaitSyncTargetOnSelect;

        public FootSyncSelectorNode()
        {
            NodeType = AnimationBlendNodeType.FOOT_SYNC_SELECTOR_NODE;
        }
    }

    public class BoneMaskNode : AnimationNode
    {
        public uint MaskingControl;
        public byte MaskPreceding;
        public byte MaskFollowing;
        public byte MaskSelf;

        public BoneMaskNode()
        {
            NodeType = AnimationBlendNodeType.BONE_MASK_NODE;
        }
    }

    public class IkNode : AnimationNode
    {
        public uint PoseLayer;
        public string IkEffectorName;
        public uint IkType;
        public uint IkTarget;
        public float EffectorFullyEffectiveRadius;
        public float EffectorLeastEffectiveRadius;
        public float FalloffRate;
        public byte EnforceTranslation;
        public byte EnforceEndBoneRotation;

        public IkNode()
        {
            NodeType = AnimationBlendNodeType.IK_NODE;
        }
    }

    public class SphericalNode : AnimationNode
    {
        public string Coord;

        public List<BlendTriIndices> Tris = new List<BlendTriIndices>();
        public uint NumTris;
        public bool SyncDurations;

        public SphericalNode()
        {
            NodeType = AnimationBlendNodeType.SPHERICAL_NODE;
        }

        public class BlendTriIndices
        {
            public uint Index0;
            public uint Index1;
            public uint Index2;
            public float X0;
            public float Y0;
            public float X1;
            public float Y1;
            public float X2;
            public float Y2;
        }
    }

    public class WeightedNode : AnimationNode
    {
        public string ParameterName;

        public float ParameterMin;
        public float ParameterMax = 1.0f;

        public WeightedNode()
        {
            NodeType = AnimationBlendNodeType.WEIGHTED_NODE;
        }
    }

    public class RandomisedLeafNode : AnimationNode
    {
        public bool HasCallback;
        public float BlendTime;

        public string CallbackName;
        public string RandomNodeCallbackName;

        public bool Looped;
        public bool NewSelectionOnLoop;
        public List<bool> Mirrored = new List<bool>();
        public uint OptionalContextParam;
        public uint OptionalConvergeVector;
        public uint OptionalConvergeFloat;
        public uint NumberOfAnimSlots;

        public List<uint> LevelAnimIndices = new List<uint>();
        public List<string> Names = new List<string>();
        public List<float> WeightsForCdf = new List<float>();
        public List<uint> LoopsBeforeReselection = new List<uint>();
        public List<float> NotifyTimeOffset = new List<float>();
        public List<float> StartTimeOffset = new List<float>();
        public List<float> EndTimeOffset = new List<float>();

        public bool ConvergeOrientation;
        public bool ConvergeTranslation;

        public RandomisedLeafNode()
        {
            NodeType = AnimationBlendNodeType.RANDOMISED_LEAF_NODE;
        }
    }
}