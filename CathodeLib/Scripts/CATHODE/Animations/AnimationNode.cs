using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Animations
{
    public class AnimationNode
    {
        public NodeType Type; // ANIM_Base
        public string Name = "";
        public List<AnimationNode> Children = new List<AnimationNode>();

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

        public List<AnimationNode> Nodes { get; } = new List<AnimationNode>();

        private readonly Dictionary<string, AnimationNode> _byName = new Dictionary<string, AnimationNode>();
        private readonly Dictionary<(string Name, NodeType Type), AnimationNode> _byNameAndType = new Dictionary<(string, NodeType), AnimationNode>();

        public AnimationTree()
        {
            Type = NodeType.ANIM_Tree_Top_Level;
        }

        public void AddNode(AnimationNode node)
        {
            if (node == null || string.IsNullOrEmpty(node.Name))
                return;
            Nodes.Add(node);
            if (!_byName.ContainsKey(node.Name))
                _byName[node.Name] = node;
            var key = (node.Name, node.Type);
            if (!_byNameAndType.ContainsKey(key))
                _byNameAndType[key] = node;
        }

        public void ReplaceNode(AnimationNode oldNode, AnimationNode newNode)
        {
            if (oldNode == null || newNode == null || string.IsNullOrEmpty(newNode.Name))
                return;
            int idx = Nodes.IndexOf(oldNode);
            if (idx >= 0)
                Nodes[idx] = newNode;
            else
                Nodes.Add(newNode);

            _byNameAndType.Remove((oldNode.Name, oldNode.Type));
            _byNameAndType[(newNode.Name, newNode.Type)] = newNode;

            if (_byName.TryGetValue(oldNode.Name, out AnimationNode mapped) && ReferenceEquals(mapped, oldNode))
                _byName.Remove(oldNode.Name);
            if (!_byName.ContainsKey(newNode.Name))
                _byName[newNode.Name] = newNode;
            else if (ReferenceEquals(_byName[newNode.Name], oldNode))
                _byName[newNode.Name] = newNode;
        }

        public void RemoveNode(AnimationNode node)
        {
            if (node == null)
                return;

            Nodes.Remove(node);
            Children.Remove(node);

            if (!string.IsNullOrEmpty(node.Name))
            {
                if (_byName.TryGetValue(node.Name, out AnimationNode mapped) && ReferenceEquals(mapped, node))
                    _byName.Remove(node.Name);
                _byNameAndType.Remove((node.Name, node.Type));
            }

            foreach (AnimationNode other in Nodes)
                ClearReferencesTo(other, node);
            ClearReferencesTo(this, node);
        }

        private static void ClearReferencesTo(AnimationNode owner, AnimationNode target)
        {
            if (owner == null || target == null)
                return;

            owner.Children.Remove(target);

            switch (owner)
            {
                case LeafNode leaf:
                    if (ReferenceEquals(leaf.Callback, target)) leaf.Callback = null;
                    if (ReferenceEquals(leaf.OptionalContextParam, target)) leaf.OptionalContextParam = null;
                    if (ReferenceEquals(leaf.OptionalConvergeVector, target)) leaf.OptionalConvergeVector = null;
                    if (ReferenceEquals(leaf.OptionalConvergeFloat, target)) leaf.OptionalConvergeFloat = null;
                    break;
                case FloatInterpolatorNode interp:
                    if (ReferenceEquals(interp.SourceParameter, target)) interp.SourceParameter = null;
                    break;
                case PropertyListenerNode listener:
                    if (ReferenceEquals(listener.LeafNode, target)) listener.LeafNode = null;
                    break;
                case SelectorNode selector:
                    if (ReferenceEquals(selector.ParameterBinding, target)) selector.ParameterBinding = null;
                    if (selector.States != null)
                    {
                        foreach (var state in selector.States)
                        {
                            if (state != null && ReferenceEquals(state.Node, target))
                                state.Node = null;
                        }
                    }
                    break;
                case ParametricNode parametric:
                    if (ReferenceEquals(parametric.ParameterBinding, target)) parametric.ParameterBinding = null;
                    if (parametric.States != null)
                    {
                        foreach (var state in parametric.States)
                        {
                            if (state != null && ReferenceEquals(state.Node, target))
                                state.Node = null;
                        }
                    }
                    break;
                case Parametric2DNode parametric2D:
                    if (ReferenceEquals(parametric2D.ParameterBindingX, target)) parametric2D.ParameterBindingX = null;
                    if (ReferenceEquals(parametric2D.ParameterBindingY, target)) parametric2D.ParameterBindingY = null;
                    if (ReferenceEquals(parametric2D.OverflowCallback, target)) parametric2D.OverflowCallback = null;
                    if (parametric2D is Parametric3DNode parametric3D)
                    {
                        if (ReferenceEquals(parametric3D.ParameterBindingZ, target)) parametric3D.ParameterBindingZ = null;
                        if (parametric3D is Parametric4DNode parametric4D)
                        {
                            if (ReferenceEquals(parametric4D.ParameterBindingW, target)) parametric4D.ParameterBindingW = null;
                        }
                    }
                    break;
                case AdditiveBlendNode additive:
                    if (ReferenceEquals(additive.BaseNode, target)) additive.BaseNode = null;
                    if (ReferenceEquals(additive.AdditiveNode, target)) additive.AdditiveNode = null;
                    if (additive is ParametricAdditiveBlendNode parametricAdditive
                        && ReferenceEquals(parametricAdditive.WeightControlParameter, target))
                        parametricAdditive.WeightControlParameter = null;
                    break;
                case RangedSelectorNode ranged:
                    if (ReferenceEquals(ranged.ParameterBinding, target)) ranged.ParameterBinding = null;
                    if (ranged.States != null)
                    {
                        foreach (var state in ranged.States)
                        {
                            if (state != null && ReferenceEquals(state.Node, target))
                                state.Node = null;
                        }
                    }
                    break;
                case FootSyncSelectorNode footSync:
                    if (ReferenceEquals(footSync.LeftStrikeChild, target)) footSync.LeftStrikeChild = null;
                    if (ReferenceEquals(footSync.RightStrikeChild, target)) footSync.RightStrikeChild = null;
                    break;
                case IkNode ik:
                    if (ReferenceEquals(ik.IkEffector, target)) ik.IkEffector = null;
                    break;
                case WeightedNode weighted:
                    if (ReferenceEquals(weighted.Parameter, target)) weighted.Parameter = null;
                    if (ReferenceEquals(weighted.Child, target)) weighted.Child = null;
                    break;
                case RandomisedLeafNode randomised:
                    if (ReferenceEquals(randomised.Callback, target)) randomised.Callback = null;
                    if (ReferenceEquals(randomised.RandomCallback, target)) randomised.RandomCallback = null;
                    if (ReferenceEquals(randomised.OptionalAnimationContext, target)) randomised.OptionalAnimationContext = null;
                    if (ReferenceEquals(randomised.OptionalConvergeVector, target)) randomised.OptionalConvergeVector = null;
                    if (ReferenceEquals(randomised.OptionalConvergeFloat, target)) randomised.OptionalConvergeFloat = null;
                    break;
            }
        }

        public void RenameNode(AnimationNode node, string newName)
        {
            if (node == null || string.IsNullOrEmpty(newName) || node.Name == newName)
                return;

            if (TryGetNode(newName, out AnimationNode existing) && !ReferenceEquals(existing, node))
                throw new InvalidOperationException($"A node named '{newName}' already exists in this tree.");

            string oldName = node.Name;
            if (!string.IsNullOrEmpty(oldName))
            {
                if (_byName.TryGetValue(oldName, out AnimationNode mapped) && ReferenceEquals(mapped, node))
                    _byName.Remove(oldName);
                _byNameAndType.Remove((oldName, node.Type));
            }

            node.Name = newName;
            if (!_byName.ContainsKey(newName))
                _byName[newName] = node;
            _byNameAndType[(newName, node.Type)] = node;
        }

        public bool TryGetNode(string name, out AnimationNode node, NodeType? type = null)
        {
            node = null;
            if (string.IsNullOrEmpty(name))
                return false;
            if (type.HasValue)
                return _byNameAndType.TryGetValue((name, type.Value), out node);
            return _byName.TryGetValue(name, out node);
        }

        public T GetNode<T>(string name, NodeType? type = null) where T : AnimationNode
        {
            if (TryGetNode(name, out AnimationNode node, type) && node is T typed)
                return typed;
            return null;
        }
    }

    public class LeafNode : AnimationNode
    {
        public string AnimationName = "";

        public bool Mirrored = false;
        public bool Looping = false;

        public AnimationNode Callback = null;

        public ParameterNode OptionalContextParam;
        public ParameterNode OptionalConvergeVector;
        public ParameterNode OptionalConvergeFloat;

        public bool ConvergeOrientation = false;
        public bool ConvergeTranslation = false;

        public BoneMaskGroups Mask;

        public float NotifyTimeOffset = 0.3f;
        public float StartTimeOffset = 0.0f;
        public float EndTimeOffset = -1.0f;

        public LeafNode()
        {
            Type = NodeType.ANIM_Animation;
        }
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
            public AnimationNode Node = null;
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

        public string BlendSet = "";
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

        public string ExtraBlendSet = "";

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
        public LeafNode LeftStrikeChild = null;
        public LeafNode RightStrikeChild = null;

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

        public AnimationNode Child = null;

        public WeightedNode()
        {
            Type = NodeType.ANIM_Weighted;
        }
    }

    public class RandomisedLeafNode : AnimationNode
    {
        public Animation[] AnimationPool = new Animation[8];

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

        public class Animation
        {
            public string AnimationName = "";

            public bool Mirrored = false;

            public float Weight = 1.0f;
            public uint LoopsBeforeReselection = 0;

            public float NotifyTimeOffset = 0.3f;
            public float StartTimeOffset = 0.0f;
            public float EndTimeOffset = -1.0f;
        }
    }
}