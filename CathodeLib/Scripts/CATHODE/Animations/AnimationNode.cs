using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Animations
{
    public class AnimationNode
    {
        public AnimationNodeType Type;
        public string Name;
        public List<AnimationNode> Children = new List<AnimationNode>();
    }

    public class AnimationTree : AnimationNode
    {
        public string SetName; //the anim set this tree is contained within

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

        public List<string> BindingNames = new List<string>();
        public List<AnimTreeParameterType> ParameterTypes = new List<AnimTreeParameterType>();
        public List<uint> IndicesInTypeArrays = new List<uint>(); //bool??

        public List<string> CallbackNames = new List<string>();

        public List<string> MetadataListenerNames = new List<string>();
        public List<string> MetadataEventNames = new List<string>();
        public List<float> MetadataListenerWeightThresholds = new List<float>();
        public List<float> MetadataListenerFilterTimes = new List<float>();

        public List<string> AutoFloatNames = new List<string>();

        public List<string> PropertyListenerNames = new List<string>();
        public List<string> PropertyListenerPropertyNames = new List<string>();
        public List<string> PropertyListenerLeafNames = new List<string>();

        public List<string> PropertyValueNames = new List<string>();
        public List<AnimationMetadataValue> PropertyValues = new List<AnimationMetadataValue>();

        public List<string> FloatInterpolatorSourceNames = new List<string>();
        public List<string> FloatInterpolatorNames = new List<string>();
        public List<float> FloatInterpolatorStartValues = new List<float>();
        public List<float> FloatInterpolatorRates = new List<float>();

        public AnimationTree()
        {
            Type = AnimationNodeType.ANIM_Tree_Top_Level;
        }
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
            Type = AnimationNodeType.ANIM_Selector;
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
            Type = AnimationNodeType.ANIM_Enumerated_Selector;
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
            Type = AnimationNodeType.ANIM_Parametric;
        }
    }

    public class Parametric1DNode : AnimationNode
    {
        public uint[] BlendSet = new uint[1];

        public string XParameter;
        public string OverflowListener;
        public bool LoopBlendSet;
        public bool SyncBlendSet;

        public Parametric1DNode()
        {
            Type = AnimationNodeType.ANIM_1DParametric;
        }
    }
    public class Parametric2DNode : Parametric1DNode
    {
        public string YParameter;

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
        public new uint[] BlendSet = new uint[2];

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
        public string BaseNodeName;
        public string AdditiveNodeName;

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
            Type = AnimationNodeType.ANIM_Randomised_Animation;
        }
    }
}