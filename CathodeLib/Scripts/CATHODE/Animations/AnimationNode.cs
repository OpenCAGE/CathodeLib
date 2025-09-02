using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Animations
{
    //todo: replace all the name lookups with node refs, add all nodes to the 'datanodes' array

    public class AnimationNode
    {
        public AnimationNodeType Type;
        public string Name;
        public List<AnimationNode> Children = new List<AnimationNode>();
    }

    public class AnimationTree : AnimationNode
    {
        public string Set;

        public float TreeEaseInTime;
        public float MinInitialPlayspeed;
        public float MaxInitialPlayspeed;

        public bool NeverUseMotionExtraction;
        public bool RemoveMotionExtractionOnPreceding;
        public bool RemoveMotionExtractionOnEaseOut;
        public bool AllowFootIkIfPrimary;
        public bool AllowHipLeanIkIfPrimary;
        public bool GaitSyncOnStart;
        public bool UseLinearBlend;

        public List<AnimationNode> DataNodes = new List<AnimationNode>();

        public AnimationTree()
        {
            Type = AnimationNodeType.ANIM_Tree_Top_Level;
        }
    }

    public class LeafNode : AnimationNode
    {
        public bool HasCallback;
        public string Callback;

        public bool Looped;
        public bool Mirrored;

        public BoneMaskGroups MaskingControl;

        public string AnimationName;
               
        public string OptionalContextParam;
        public string OptionalConvergeVector;
        public string OptionalConvergeFloat;
        public bool ConvergeOrientation;
        public bool ConvergeTranslation;

        public float NotifyTimeOffset;
        public float StartTimeOffset;
        public float EndTimeOffset;
    }

    public class MetadataListenerNode : AnimationNode
    {
        public string EventName;
        public float WeightThreshold;
        public float FilterTime;

        public MetadataListenerNode()
        {
            Type = AnimationNodeType.ANIM_Metadata_Event_Listener;
        }
    }

    public class ParameterNode : AnimationNode
    {
        public AnimTreeParameterType ParameterType;
        public uint IndicesInTypeArray; //enum??

        public ParameterNode()
        {
            Type = AnimationNodeType.ANIM_Parameter;
        }
    }

    public class FloatInterpolatorNode : ParameterNode
    {
        public string SourceParameter;
        public float InitialValue;
        public float UnitsPerSecond;

        public FloatInterpolatorNode()
        {
            Type = AnimationNodeType.ANIM_FloatInterpolator;
        }
    }

    public class PropertyNode : AnimationNode
    {
        public AnimationMetadataValue Value;

        public PropertyNode()
        {
            Type = AnimationNodeType.ANIM_Parameter;
        }
    }

    public class PropertyListenerNode : AnimationNode
    {
        public string AnimProperty; // ANIMATION_PROPERTY enum value
        public string LeafNode;

        public PropertyListenerNode()
        {
            Type = AnimationNodeType.ANIM_Property_Listener;
        }
    }

    public class SelectorNode : AnimationNode
    {
        public List<string> Bindings = new List<string>();
        public List<uint> ValueBindings = new List<uint>();
        public float EaseTime;
        public string BindingParameter;
        public List<bool> FootSyncOnSelect = new List<bool>();
        public bool ResetPlaybackOnChange;

        public SelectorNode()
        {
            Type = AnimationNodeType.ANIM_Selector;
        }
    }

    public class EnumeratedSelectorNode : AnimationNode
    {
        public List<string> Bindings = new List<string>();
        public List<uint> ValueBindings = new List<uint>();
        public float EaseTime;
        public string BindingParameter;
        public List<bool> FootSyncOnSelect = new List<bool>();
        public bool ResetPlaybackOnChange;

        public EnumeratedSelectorNode()
        {
            Type = AnimationNodeType.ANIM_Enumerated_Selector;
        }
    }

    public class ParametricNode : AnimationNode
    {
        public List<string> Bindings = new List<string>();
        public List<float> ValueBindings = new List<float>();

        public string BindingParameter;
        public float Min;
        public float Max = 1.0f;
        public string ParameterUsage;
        public string AutoBlendProperty;
        public bool SyncDurations;
        public bool UseAutoDerivedBlendValues;

        public ParametricNode()
        {
            Type = AnimationNodeType.ANIM_Parametric;
        }
    }

    public class Parametric2DNode : AnimationNode
    {
        public string[] BlendSet = new string[1];

        public string XParameter;
        public string YParameter;

        public string OverflowListener;
        public bool LoopBlendSet;
        public bool SyncBlendSet;

        public Parametric2DNode()
        {
            Type = AnimationNodeType.ANIM_2DParametric;
        }
    }

    public class Parametric3DNode : Parametric2DNode
    {
        public string ZParameter;

        public Parametric3DNode()
        {
            Type = AnimationNodeType.ANIM_3DParametric;
        }
    }

    public class Parametric4DNode : Parametric3DNode
    {
        public new string[] BlendSet = new string[2];

        public string WParameter;

        public Parametric4DNode()
        {
            Type = AnimationNodeType.ANIM_4DParametric;
        }
    }

    public class BilinearHiFiNode : AnimationNode
    {
        public string[] Bindings = new string[9];

        public string XParameter;
        public string YParameter;
        public float ParameterMin;
        public float ParameterMax = 1.0f;
        public bool ParameterWrap;
        public bool SyncDurations;

        public BilinearHiFiNode()
        {
            Type = AnimationNodeType.ANIM_Bilinear_High_Fidelity;
        }
    }

    public class LoFiBilinearNode : BilinearHiFiNode
    {
        public new string[] Bindings = new string[4];

        public LoFiBilinearNode()
        {
            Type = AnimationNodeType.ANIM_Bilinear_Low_Fidelity;
        }
    }

    public class AdditiveBlendNode : AnimationNode
    {
        public string BaseNode;
        public string AdditiveNode;

        public float AdditiveNodeWeight = 1.0f;
        public bool SyncAdditiveDurationToBase;

        public AdditiveBlendNode()
        {
            Type = AnimationNodeType.ANIM_Additive_Blend;
        }
    }

    public class ParametricAdditiveBlendNode : AdditiveBlendNode
    {
        public string ParameterName;

        public float ParameterMin;
        public float ParameterMax = 1.0f;

        public ParametricAdditiveBlendNode()
        {
            Type = AnimationNodeType.ANIM_Parametric_Additive_Blend;
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
            Type = AnimationNodeType.ANIM_Ranged_Selector;
        }
    }

    public class FootSyncSelectorNode : AnimationNode
    {
        public string[] Bindings = new string[2];

        public FootStrikeSelectionMethod FootStrikeSelectionMethod;
        public bool GaitSyncTargetOnSelect;

        public FootSyncSelectorNode()
        {
            Type = AnimationNodeType.ANIM_Foot_Sync_Selector;
        }
    }

    public class BoneMaskNode : AnimationNode
    {
        public uint MaskingControl;
        public bool MaskPreceding;
        public bool MaskFollowing;
        public bool MaskSelf;

        public BoneMaskNode()
        {
            Type = AnimationNodeType.ANIM_Bone_Mask;
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
        public bool EnforceTranslation;
        public bool EnforceEndBoneRotation;

        public IkNode()
        {
            Type = AnimationNodeType.ANIM_IK;
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
            Type = AnimationNodeType.ANIM_Spherical;
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
            Type = AnimationNodeType.ANIM_Weighted;
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

        public List<string> Animations = new List<string>();
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
            Type = AnimationNodeType.ANIM_Randomised_Animation;
        }
    }
}