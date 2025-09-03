using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Animations
{
    public class AnimationNode
    {
        public NodeType Type;
        public string Name;
        public HashSet<AnimationNode> Children = new HashSet<AnimationNode>(); //todo - maybe we don't want children on all nodes, since they don't all support it

        public override bool Equals(object obj)
        {
            return obj is AnimationNode node && Name == node.Name && Type == node.Type;
        }

        public override int GetHashCode()
        {
            int hashCode = -1979447941;
            hashCode = hashCode * -1521134295 + Type.GetHashCode();
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
            return hashCode;
        }
    }

    //done
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

        public HashSet<AnimationNode> Nodes = new HashSet<AnimationNode>();

        public AnimationTree()
        {
            Type = NodeType.ANIM_Tree_Top_Level;
        }
    }

    //done
    public class BaseLeafNode : AnimationNode
    {
        public string AnimationName;

        public bool Mirrored;

        public float NotifyTimeOffset;
        public float StartTimeOffset;
        public float EndTimeOffset;

        public BaseLeafNode()
        {
            Type = NodeType.ANIM_Animation;
        }
    }

    public class LeafNode : BaseLeafNode
    {
        public BoneMaskGroups MaskingControl;

        public bool Looped;

        public AnimationNode Callback;

        //unsure on these values - same as random leaf node
        public string OptionalContextParam;
        public string OptionalConvergeVector;
        public string OptionalConvergeFloat;
        // ^ are these ANIM_Parameters?

        public bool ConvergeOrientation;
        public bool ConvergeTranslation;
    }

    //done
    public class RandomisedLeafLeafNode : BaseLeafNode
    {
        public float Weight;
        public uint LoopsBeforeReselection;
    }

    //done
    public class MetadataListenerNode : AnimationNode
    {
        public string EventName;
        public float WeightThreshold;
        public float FilterTime;

        public MetadataListenerNode()
        {
            Type = NodeType.ANIM_Metadata_Event_Listener;
        }
    }

    //done
    public class ParameterNode : AnimationNode
    {
        public AnimTreeParameterType ParameterType;

        public ParameterNode()
        {
            Type = NodeType.ANIM_Parameter;
        }
    }

    //done
    public class FloatInterpolatorNode : ParameterNode
    {
        public ParameterNode SourceParameter;

        public float InitialValue;
        public float UnitsPerSecond;

        public FloatInterpolatorNode()
        {
            Type = NodeType.ANIM_FloatInterpolator;
        }
    }

    //done
    public class PropertyNode : AnimationNode
    {
        public AnimationMetadataValue Value;

        public PropertyNode()
        {
            Type = NodeType.ANIM_Parameter;
        }
    }

    //done
    public class PropertyListenerNode : AnimationNode
    {
        public string AnimProperty;
        public AnimationNode LeafNode;

        public PropertyListenerNode()
        {
            Type = NodeType.ANIM_Property_Listener;
        }
    }

    //done
    public class SelectorNode : AnimationNode
    {
        public List<State> States = new List<State>();

        public float EaseSelectionTime;
        public bool ResetPlaybackOnChangeSelection;

        public ParameterNode BindingParameter;

        public SelectorNode()
        {
            Type = NodeType.ANIM_Selector;
        }

        public class State
        {
            public LeafNode Node;
            public uint Value;
            public bool FootSyncOnSelect;
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
            Type = NodeType.ANIM_Parametric;
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
            Type = NodeType.ANIM_2DParametric;
        }
    }

    //done
    public class Parametric3DNode : Parametric2DNode
    {
        public string ZParameter;

        public Parametric3DNode()
        {
            Type = NodeType.ANIM_3DParametric;
        }
    }

    public class Parametric4DNode : Parametric3DNode
    {
        public new string[] BlendSet = new string[2];

        public string WParameter;

        public Parametric4DNode()
        {
            Type = NodeType.ANIM_4DParametric;
        }
    }

    //done
    public class AdditiveBlendNode : AnimationNode
    {
        public AnimationNode BaseNode;
        public AnimationNode AdditiveNode;

        public float AdditiveNodeWeight = 1.0f;
        public bool SyncAdditiveDurationToBase;

        public AdditiveBlendNode()
        {
            Type = NodeType.ANIM_Additive_Blend;
        }
    }

    //done
    public class ParametricAdditiveBlendNode : AdditiveBlendNode
    {
        public ParameterNode Parameter;

        public float ParameterMin;
        public float ParameterMax = 1.0f;

        public ParametricAdditiveBlendNode()
        {
            Type = NodeType.ANIM_Parametric_Additive_Blend;
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
            Type = NodeType.ANIM_Ranged_Selector;
        }
    }

    public class FootSyncSelectorNode : AnimationNode
    {
        public string[] Bindings = new string[2];

        public FootStrikeSelectionMethod FootStrikeSelectionMethod;
        public bool GaitSyncTargetOnSelect;

        public FootSyncSelectorNode()
        {
            Type = NodeType.ANIM_Foot_Sync_Selector;
        }
    }

    //done
    public class BoneMaskNode : AnimationNode
    {
        public BoneMaskGroups MaskingControl;

        public bool MaskPreceding;
        public bool MaskFollowing;
        public bool MaskSelf;

        public BoneMaskNode()
        {
            Type = NodeType.ANIM_Bone_Mask;
        }
    }

    //done
    public class IkNode : AnimationNode
    {
        public ParameterNode IkEffector;

        public IkSolverType IkType;
        public IkControlTarget Target;

        public float EffectorFullyEffectiveRadius;
        public float EffectorLeastEffectiveRadius;
        public float FalloffRate;

        public bool EnforceTranslation;
        public bool EnforceEndBoneRotation;

        public PoseLayer PoseLayer;

        public IkNode()
        {
            Type = NodeType.ANIM_IK;
        }
    }

    //done
    public class WeightedNode : AnimationNode
    {
        public ParameterNode Parameter;

        public float ParameterMin;
        public float ParameterMax = 1.0f;

        public WeightedNode()
        {
            Type = NodeType.ANIM_Weighted;
        }
    }

    public class RandomisedLeafNode : AnimationNode
    {
        // note - this node's children are the animations that this selects

        public bool Looping;
        public bool NewSelectionOnLoop;
        public float BlendTime;
        public AnimationNode Callback;
        public AnimationNode RandomCallback;
        
        public uint OptionalAnimationContext;

        //unsure on these values
        public uint OptionalConvergeVector;
        public uint OptionalConvergeFloat;

        public bool ConvergeOrientation;
        public bool ConvergeTranslation;

        public RandomisedLeafNode()
        {
            Type = NodeType.ANIM_Randomised_Animation;
        }
    }
}