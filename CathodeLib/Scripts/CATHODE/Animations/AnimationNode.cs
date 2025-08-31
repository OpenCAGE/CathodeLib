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
        public uint HashedCallbackName;
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
        public List<uint> HashBindings = new List<uint>();
        public List<uint> ValueBindings = new List<uint>();
        public float EaseTime;
        public uint BindingParameterHash;
        public uint NumChildren;
        public List<bool> FootSyncOnSelect = new List<bool>();
        public bool ResetPlaybackOnChange;

        public SelectorNode()
        {
            NodeType = AnimationBlendNodeType.SELECTOR_NODE;
        }
    }

    public class EnumeratedSelectorNode : AnimationNode
    {
        public uint NumChildren;
        public List<uint> HashBindings = new List<uint>();
        public List<uint> ValueBindings = new List<uint>();
        public float EaseTime;
        public uint BindingParameterHash;
        public List<bool> FootSyncOnSelect = new List<bool>();
        public bool ResetPlaybackOnChange;

        public EnumeratedSelectorNode()
        {
            NodeType = AnimationBlendNodeType.ENUMERATED_SELECTOR_NODE;
        }
    }

    public class ParametricNode : AnimationNode
    {
        public List<uint> HashBindings = new List<uint>();
        public List<float> ValueBindings = new List<float>();
        public uint BindingParameterHash;
        public uint NumChildren;
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
        public uint BlendSet;
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

    public class BlendSet4DNode : AnimationNode
    {
        public uint[] BlendSet = new uint[2];
        public uint XParameter;
        public uint YParameter;
        public uint ZParameter;
        public uint WParameter;
        public uint OverflowListener;
        public bool LoopBlendSets;
        public bool SyncBlendSets;

        public BlendSet4DNode()
        {
            NodeType = AnimationBlendNodeType.BLEND_SET_4D_NODE;
        }
    }

    public class BilinearNode : AnimationNode
    {
        public uint[] HashBindings = new uint[9];
        public uint XParameterHash;
        public uint YParameterHash;
        public float ParameterMin;
        public float ParameterMax = 1.0f;
        public bool ParameterWrap;
        public bool SyncDurations;

        public BilinearNode()
        {
            NodeType = AnimationBlendNodeType.BILINEAR_NODE;
        }
    }

    public class LoFiBilinearNode : AnimationNode
    {
        public uint[] HashBindings = new uint[4];
        public uint XParameterHash;
        public uint YParameterHash;
        public float ParameterMin;
        public float ParameterMax = 1.0f;
        public bool ParameterWrap;
        public bool SyncDurations;

        public LoFiBilinearNode()
        {
            NodeType = AnimationBlendNodeType.LO_FI_BILINEAR_NODE;
        }
    }

    public class AdditiveBlendNode : AnimationNode
    {
        public uint BaseNodeHash;
        public uint AdditiveNodeHash;
        public float AdditiveNodeWeight = 1.0f;
        public bool SyncAdditiveDurationToBase;

        public AdditiveBlendNode()
        {
            NodeType = AnimationBlendNodeType.ADDITIVE_BLEND_NODE;
        }
    }

    public class ParametricAdditiveBlendNode : AnimationNode
    {
        public uint BaseNodeHash;
        public uint AdditiveNodeHash;
        public float AdditiveNodeWeight = 1.0f;
        public uint ParameterHash;
        public float ParameterMin;
        public float ParameterMax = 1.0f;
        public bool SyncAdditiveDurationToBase;

        public ParametricAdditiveBlendNode()
        {
            NodeType = AnimationBlendNodeType.PARAMETRIC_ADDITIVE_BLEND_NODE;
        }
    }

    public class RangedSelectorNode : AnimationNode
    {
        public List<uint> HashBindings = new List<uint>();
        public List<float> MinValueBindings = new List<float>();
        public List<float> MaxValueBindings = new List<float>();
        public float EaseTime;
        public uint BindingParameterHash;
        public uint NumChildren;
        public List<bool> FootSyncOnSelect = new List<bool>();
        public bool ResetPlaybackOnChange;

        public RangedSelectorNode()
        {
            NodeType = AnimationBlendNodeType.RANGED_SELECTOR_NODE;
        }
    }

    public class FootSyncSelectorNode : AnimationNode
    {
        public uint[] HashBindings = new uint[2];
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
        public uint IkEffectorHash;
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
        public uint CoordHash;
        public uint NumChildren;
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
        public uint ParameterHash;
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
        public uint HashedCallbackName;
        public uint RandomNodeCallbackName;
        public bool Looped;
        public bool NewSelectionOnLoop;
        public List<bool> Mirrored = new List<bool>();
        public uint OptionalContextParam;
        public uint OptionalConvergeVector;
        public uint OptionalConvergeFloat;
        public uint NumberOfAnimSlots;
        public List<uint> LevelAnimIndices = new List<uint>();
        public List<uint> HashedNames = new List<uint>();
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