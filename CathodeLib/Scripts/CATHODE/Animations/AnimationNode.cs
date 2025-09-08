using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Animations
{
    public class AnimationNode
    {
        public NodeType Type; // ANIM_Base
        public string Name = "";

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

    public class AnimationTree : AnimationNode
    {
        public string Set = "";

        public float TreeEaseInTime = 0.25f;

        public bool RemoveMotionExtractionOnEaseOut = false;
        public bool RemoveMotionExtractionOnPreceding = false;
        public bool NeverUseMotionExtraction = false;

        public bool AllowFootIkIfPrimary = true;
        public bool AllowHipLeanIkIfPrimary = true;
        public bool GaitSyncOnStart = false;
        public bool UseLinearBlend = false;

        public float MinInitialPlayspeed = 1.0f;
        public float MaxInitialPlayspeed = 1.0f;

        public HashSet<AnimationNode> Nodes = new HashSet<AnimationNode>(); //All nodes contained within this tree
        public HashSet<AnimationNode> Children = new HashSet<AnimationNode>(); //Direct children of this top level node

        public AnimationTree()
        {
            Type = NodeType.ANIM_Tree_Top_Level;
        }
    }

    //perhaps we just want 'leaf node' again tho, with random leaf potentially having an internal anim object not node
    public class BaseLeafNode : AnimationNode
    {
        public string AnimationName = "";

        public bool Mirrored = false;

        public float NotifyTimeOffset = 0.3f;
        public float StartTimeOffset = 0.0f;
        public float EndTimeOffset = -1.0f;

        public BaseLeafNode()
        {
            Type = NodeType.ANIM_Animation;
        }
    }

    public class LeafNode : BaseLeafNode
    {
        public bool Looping = false;

        public AnimationNode Callback = null;

        public ParameterNode OptionalContextParam;
        public ParameterNode OptionalConvergeVector;
        public ParameterNode OptionalConvergeFloat;

        public bool ConvergeOrientation = false;
        public bool ConvergeTranslation = false;

        public BoneMaskGroups Mask;
    }

    public class RandomisedLeafLeafNode : BaseLeafNode
    {
        public float Weight = 1.0f;
        public uint LoopsBeforeReselection = 0;
    }

    public class MetadataListenerNode : AnimationNode
    {
        public string EventName = "";
        public float WeightThreshold = 0.1f;
        public float FilterTime = 0.1f;

        public MetadataListenerNode()
        {
            Type = NodeType.ANIM_Metadata_Event_Listener;
        }
    }

    public class ParameterNode : AnimationNode
    {
        public AnimTreeParameterType ParameterType = AnimTreeParameterType.FLOAT;

        public ParameterNode()
        {
            Type = NodeType.ANIM_Parameter;
        }
    }

    public class FloatInterpolatorNode : ParameterNode
    {
        public ParameterNode SourceParameter = null;

        public float InitialValue = 0.0f;
        public float UnitsPerSecond = 1.0f;

        public FloatInterpolatorNode()
        {
            Type = NodeType.ANIM_FloatInterpolator;
        }
    }

    public class PropertyNode : AnimationNode
    {
        public AnimationMetadataValue Value; //this can defo be simplified

        public PropertyNode()
        {
            Type = NodeType.ANIM_Property;
        }
    }

    public class PropertyListenerNode : AnimationNode
    {
        public string AnimProperty = "linear_velocity";
        public AnimationNode LeafNode = null;

        public PropertyListenerNode()
        {
            Type = NodeType.ANIM_Property_Listener;
        }
    }

    public class SelectorNode : AnimationNode
    {
        public ParameterNode ParameterBinding = null;

        public bool ResetPlaybackOnChangeSelection = true;
        public float EaseSelectionTime = 0.1f;

        public State[] States = new State[16];

        public SelectorNode()
        {
            Type = NodeType.ANIM_Selector;
            for (uint i = 0; i < 16; i++)
                States[i] = new State() { Value = i };
        }

        public class State
        {
            public LeafNode Node = null;
            public uint Value;
            public bool FootSyncOnSelect = false;
        }
    }

    public class ParametricNode : AnimationNode
    {
        public ParameterNode ParameterBinding = null; 

        public float ParameterMin = 0.0f;
        public float ParameterMax = 1000.0f;
        public ParameterBlendUsage ParameterUsage = ParameterBlendUsage.Clamp;
        public bool ExtractBlendPropertiesAutomatically = false;
        public string BlendProperty = "linear_speed";
        public bool SyncDurations = true;

        public State[] States = new State[16];

        public ParametricNode()
        {
            Type = NodeType.ANIM_Parametric;
            for (int i = 0; i < 16; i++)
                States[i] = new State() { Value = i };
        }

        public class State
        {
            public AnimationNode Node = null;
            public float Value;
        }
    }

    public class Parametric2DNode : AnimationNode
    {
        public ParameterNode ParameterBindingX = null;
        public ParameterNode ParameterBindingY = null;

        public bool SyncBlendSet = true;
        public bool LoopBlendSet = true;

        public AnimationNode BlendSet = null; // is this right type?
        public AnimationNode OverflowCallback = null;

        public Parametric2DNode()
        {
            Type = NodeType.ANIM_2DParametric;
        }
    }

    public class Parametric3DNode : Parametric2DNode
    {
        public ParameterNode ParameterBindingZ = null;

        public Parametric3DNode()
        {
            Type = NodeType.ANIM_3DParametric;
        }
    }

    public class Parametric4DNode : Parametric3DNode
    {
        public ParameterNode ParameterBindingW = null;

        public AnimationNode ExtraBlendSet = null; // is this right type?

        public Parametric4DNode()
        {
            Type = NodeType.ANIM_4DParametric;
        }
    }

    public class AdditiveBlendNode : AnimationNode
    {
        public AnimationNode BaseNode = null;
        public AnimationNode AdditiveNode = null;

        public float AdditiveNodeWeight = 1.0f;
        public bool SyncAdditiveDurationToBase = false;

        public AdditiveBlendNode()
        {
            Type = NodeType.ANIM_Additive_Blend;
        }
    }

    public class ParametricAdditiveBlendNode : AdditiveBlendNode
    {
        public ParameterNode WeightControlParameter = null;

        public float ParameterMin = 0.0f;
        public float ParameterMax = 1.0f;

        public ParametricAdditiveBlendNode()
        {
            Type = NodeType.ANIM_Parametric_Additive_Blend;
        }
    }

    public class RangedSelectorNode : AnimationNode
    {
        public ParameterNode ParameterBinding = null;

        public bool ResetPlaybackOnChange = true;
        public float EaseSelectionTime = 0.1f;

        public State[] States = new State[8];

        public RangedSelectorNode()
        {
            Type = NodeType.ANIM_Ranged_Selector;
            for (int i = 0; i < 8; i++)
                States[i] = new State();
        }

        public class State
        {
            public AnimationNode Node = null;
            public float Min = 0.0f;
            public float Max = 0.0f;
            public bool FootSyncOnSelect = false;
        }
    }

    public class FootSyncSelectorNode : AnimationNode
    {
        public BaseLeafNode LeftStrikeChild = null;
        public BaseLeafNode RightStrikeChild = null;

        public FootStrikeSelectionMethod StrikeSelectionMethod = FootStrikeSelectionMethod.NextStrike;
        public bool GaitSyncTargetOnSelect = false;

        public FootSyncSelectorNode()
        {
            Type = NodeType.ANIM_Foot_Sync_Selector;
        }
    }

    public class BoneMaskNode : AnimationNode
    {
        public bool MaskPrecedingLayers = false;
        public bool MaskSelf = false;
        public bool MaskFollowingLayers = false;

        public BoneMaskGroups Mask = BoneMaskGroups.NONE;

        public BoneMaskNode()
        {
            Type = NodeType.ANIM_Bone_Mask;
        }
    }

    public class IkNode : AnimationNode
    {
        public ParameterNode IkEffector = null;

        public IkSolverType IkType = IkSolverType.ANALYTICAL;
        public IkControlTarget Target = IkControlTarget.LEFT_FOOT;

        public float EffectorFullyEffectiveRadius = 0.1f;
        public float EffectorLeastEffectiveRadius = 0.1f;
        public float FalloffRate = 1.0f;

        public bool EnforceTranslation = true;
        public bool EnforceEndBoneRotation = false;

        public PoseLayer PoseLayer; //todo - where does this come from, what's the default

        public IkNode()
        {
            Type = NodeType.ANIM_IK;
        }
    }

    public class WeightedNode : AnimationNode
    {
        public ParameterNode Parameter = null;

        public float ParameterMin = 0.0f;
        public float ParameterMax = 1.0f;

        public WeightedNode()
        {
            Type = NodeType.ANIM_Weighted;
        }
    }

    public class RandomisedLeafNode : AnimationNode
    {
        public RandomisedLeafLeafNode[] AnimationPool = new RandomisedLeafLeafNode[8];

        public bool Looping = false;
        public bool NewSelectionOnLoop = false;
        public float BlendTime = 0.3f;
        public AnimationNode Callback = null;
        public AnimationNode RandomCallback = null;
        
        public ParameterNode OptionalAnimationContext;
        public ParameterNode OptionalConvergeVector;
        public ParameterNode OptionalConvergeFloat;

        public bool ConvergeOrientation = false;
        public bool ConvergeTranslation = false;

        public RandomisedLeafNode()
        {
            Type = NodeType.ANIM_Randomised_Animation;
        }
    }
}