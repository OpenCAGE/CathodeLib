using CATHODE.Animations;
using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using static CATHODE.SkeleDB;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> ANIM_TREE_DB.BIN
    /// </summary>
    public class AnimTreeDB : CathodeFile
    {
        public string Set = "";
        public List<AnimationTree> Entries = new List<AnimationTree>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;

        public AnimTreeDB(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public AnimTreeDB(MemoryStream stream, AnimationStrings strings, string path) : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public AnimTreeDB(byte[] data, AnimationStrings strings, string path) : base(data, path)
        {
            _strings = strings;
            using (MemoryStream stream = new MemoryStream(data))
            {
                _loaded = Load(stream);
            }
        }

        private AnimationStrings _strings;
        private NodeResolver _nodeResolver = new NodeResolver();

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            if (_strings == null || _filepath == null || _filepath == "")
                return false;

            Set = _strings.GetString(Convert.ToUInt32(Path.GetFileName(_filepath).Split('_')[0]));

            using (BinaryReader reader = new BinaryReader(stream))
            {
                int version = reader.ReadInt32();
                if (version != 66) throw new Exception("");

                int hashTableSize = reader.ReadInt32();
                int usedSize = reader.ReadInt32();
                if (hashTableSize != usedSize) throw new Exception("");

                var treeHashToIndex = new Dictionary<uint, int>();
                for (int i = 0; i < hashTableSize; i++)
                {
                    uint treeHash = reader.ReadUInt32();
                    int index = reader.ReadInt32();
                    treeHashToIndex[treeHash] = index;
                }

                int[] logicalToFile = new int[hashTableSize];
                for (int i = 0; i < hashTableSize; i++)
                    logicalToFile[i] = (int)reader.ReadUInt32();

                int treeCount = reader.ReadInt32();
                var fileOrderTrees = new AnimationTree[treeCount];
                for (int i = 0; i < treeCount; i++)
                {
                    _nodeResolver.Clear();
                    
                    uint treeVersion = reader.ReadUInt32();
                    if (treeVersion != 66)
                        throw new Exception("");

                    uint numChildren = reader.ReadUInt32();

                    AnimationTree treeDef = new AnimationTree
                    {
                        Name = _strings.Entries[reader.ReadUInt32()],
                        Set = _strings.Entries[reader.ReadUInt32()],
                        TreeEaseInTime = reader.ReadSingle(),
                        MinInitialPlayspeed = reader.ReadSingle(),
                        MaxInitialPlayspeed = reader.ReadSingle(),
                        NeverUseMotionExtraction = reader.ReadBoolean(),
                        RemoveMotionExtractionOnPreceding = reader.ReadBoolean(),
                        RemoveMotionExtractionOnEaseOut = reader.ReadBoolean(),
                        AllowFootIkIfPrimary = reader.ReadBoolean(),
                        AllowHipLeanIkIfPrimary = reader.ReadBoolean(),
                        GaitSyncOnStart = reader.ReadBoolean(),
                        UseLinearBlend = reader.ReadBoolean()
                    };

                    uint NumberOfBindings = reader.ReadUInt32();
                    reader.BaseStream.Position += 20;
                    uint NumberOfCallbacks = reader.ReadUInt32();
                    uint NumberOfMetadataListeners = reader.ReadUInt32();
                    uint NumberOfPropertyListeners = reader.ReadUInt32();
                    uint NumberOfPropertyValues = reader.ReadUInt32();

                    List<string> bindingNames = new List<string>();
                    for (int x = 0; x < NumberOfBindings; x++)
                        bindingNames.Add(_strings.GetString(reader.ReadUInt32()));
                    List<AnimTreeParameterType> bindingParamTypes = new List<AnimTreeParameterType>();
                    for (int x = 0; x < NumberOfBindings; x++)
                        bindingParamTypes.Add((AnimTreeParameterType)reader.ReadUInt32());
                    List<uint> bindingType = new List<uint>();
                    for (int x = 0; x < NumberOfBindings; x++)
                        bindingType.Add(reader.ReadUInt32());
                    for (int x = 0; x < NumberOfBindings; x++)
                    {
                        var param = new ParameterNode() { Name = bindingNames[x], ParameterType = bindingParamTypes[x] };
                        treeDef.AddNode(param);
                    }

                    for (int x = 0; x < NumberOfCallbacks; x++)
                    {
                        var cb = new AnimationNode() { Type = NodeType.ANIM_Callback, Name = _strings.GetString(reader.ReadUInt32()) };
                        treeDef.AddNode(cb);
                    }

                    List<string> metaListNames = new List<string>();
                    for (int x = 0; x < NumberOfMetadataListeners; x++)
                        metaListNames.Add(_strings.GetString(reader.ReadUInt32()));
                    List<string> metaListEvents = new List<string>();
                    for (int x = 0; x < NumberOfMetadataListeners; x++)
                        metaListEvents.Add(_strings.GetString(reader.ReadUInt32()));
                    List<float> metaListWeights = new List<float>();
                    for (int x = 0; x < NumberOfMetadataListeners; x++)
                        metaListWeights.Add(reader.ReadSingle());
                    List<float> metaListFilterTimes = new List<float>();
                    for (int x = 0; x < NumberOfMetadataListeners; x++)
                        metaListFilterTimes.Add(reader.ReadSingle());
                    for (int x = 0; x < NumberOfMetadataListeners; x++)
                    {
                        var meta = new MetadataListenerNode() { Name = metaListNames[x], EventName = metaListEvents[x], WeightThreshold = metaListWeights[x], FilterTime = metaListFilterTimes[x] };
                        treeDef.AddNode(meta);
                    }

                    uint NumberOfAutoFloatBindings = reader.ReadUInt32();
                    for (int x = 0; x < NumberOfAutoFloatBindings; x++)
                    {
                        var autoFloat = new AnimationNode() { Type = NodeType.ANIM_AutoFloatParameter, Name = _strings.GetString(reader.ReadUInt32()) };
                        treeDef.AddNode(autoFloat);
                    }

                    List<string> propListenerNames = new List<string>();
                    for (int x = 0; x < NumberOfPropertyListeners; x++)
                        propListenerNames.Add(_strings.GetString(reader.ReadUInt32()));
                    List<string> propListenerPropNames = new List<string>();
                    for (int x = 0; x < NumberOfPropertyListeners; x++)
                        propListenerPropNames.Add(_strings.GetString(reader.ReadUInt32()));
                    List<string> propListenerLeafNodes = new List<string>();
                    for (int x = 0; x < NumberOfPropertyListeners; x++)
                        propListenerLeafNodes.Add(_strings.GetString(reader.ReadUInt32()));
                    for (int x = 0; x < NumberOfPropertyListeners; x++)
                    {
                        PropertyListenerNode node = new PropertyListenerNode() { Name = propListenerNames[x], AnimProperty = propListenerPropNames[x], LeafNode = null };
                        treeDef.AddNode(node);
                        _nodeResolver.RegisterLookup(node, propListenerLeafNodes[x], (n, found) => n.LeafNode = found);
                    }

                    List<string> propertyNames = new List<string>();
                    for (int x = 0; x < NumberOfPropertyValues; x++)
                        propertyNames.Add(_strings.GetString(reader.ReadUInt32()));
                    List<AnimationMetadataValue> propertyValues = new List<AnimationMetadataValue>();
                    for (int x = 0; x < NumberOfPropertyValues; x++)
                        propertyValues.Add(ReadMetadataValue(reader));
                    for (int x = 0; x < NumberOfPropertyValues; x++)
                    {
                        var prop = new PropertyNode() { Name = propertyNames[x], Value = propertyValues[x] };
                        treeDef.AddNode(prop);
                    }

                    uint NumberOfFloatInterpolators = reader.ReadUInt32();
                    List<string> floatInterpSources = new List<string>();
                    for (int x = 0; x < NumberOfFloatInterpolators; x++)
                        floatInterpSources.Add(_strings.GetString(reader.ReadUInt32()));
                    List<string> floatInterpNames = new List<string>();
                    for (int x = 0; x < NumberOfFloatInterpolators; x++)
                        floatInterpNames.Add(_strings.GetString(reader.ReadUInt32()));
                    List<float> floatInterpStartVals = new List<float>();
                    for (int x = 0; x < NumberOfFloatInterpolators; x++)
                        floatInterpStartVals.Add(reader.ReadSingle());
                    List<float> floatInterpStartUPS = new List<float>();
                    for (int x = 0; x < NumberOfFloatInterpolators; x++)
                        floatInterpStartUPS.Add(reader.ReadSingle());
                    for (int x = 0; x < NumberOfFloatInterpolators; x++)
                    {
                        ParameterNode floatInterpNodeOrig = treeDef.GetNode<ParameterNode>(floatInterpNames[x], NodeType.ANIM_Parameter);
                        FloatInterpolatorNode floatInterpNode = new FloatInterpolatorNode() { Name = floatInterpNodeOrig.Name, ParameterType = floatInterpNodeOrig.ParameterType, Type = NodeType.ANIM_FloatInterpolator };
                        ParameterNode source = treeDef.GetNode<ParameterNode>(floatInterpSources[x], NodeType.ANIM_Parameter);
                        floatInterpNode.SourceParameter = source;
                        floatInterpNode.InitialValue = floatInterpStartVals[x];
                        floatInterpNode.UnitsPerSecond = floatInterpStartUPS[x];
                        treeDef.ReplaceNode(floatInterpNodeOrig, floatInterpNode);
                    }

                    for (int x = 0; x < numChildren; x++)
                    {
                        var childNode = ReadNode(reader, treeDef);
                        treeDef.AddNode(childNode);
                        treeDef.Children.Add(childNode);
                    }

                    // Resolve all deferred node lookups
                    _nodeResolver.ResolveAll(treeDef);

                    fileOrderTrees[i] = treeDef;
                }

                //Reorder into logical index order
                Entries = new List<AnimationTree>(treeCount);
                for (int logicalIdx = 0; logicalIdx < treeCount; logicalIdx++)
                    Entries.Add(fileOrderTrees[logicalToFile[logicalIdx]]);
            }
            return true;
        }
        
        private AnimationNode ReadNode(BinaryReader reader, AnimationTree tree)
        {
            NodeType nodeType = (NodeType)reader.ReadUInt32();
            string NodeName = _strings.GetString(reader.ReadUInt32());
            uint numChildren = reader.ReadUInt32();

            AnimationNode node;
            switch (nodeType)
            {
                case NodeType.ANIM_Animation:
                    {
                        bool hasCallback = reader.ReadBoolean();
                        string callback = _strings.GetString(reader.ReadUInt32());
                        bool looping = reader.ReadBoolean();
                        bool mirrored = reader.ReadBoolean();
                        BoneMaskGroups mask = (BoneMaskGroups)reader.ReadUInt32();
                        string animName = _strings.GetString(reader.ReadUInt32());
                        uint optParam = reader.ReadUInt32();
                        uint optVector = reader.ReadUInt32();
                        uint optFloat = reader.ReadUInt32();

                        node = new LeafNode
                        {
                            Callback = null,
                            Looping = looping,
                            Mirrored = mirrored,
                            Mask = mask,
                            AnimationName = animName,
                            ConvergeOrientation = reader.ReadBoolean(),
                            ConvergeTranslation = reader.ReadBoolean(),
                            NotifyTimeOffset = reader.ReadSingle(),
                            StartTimeOffset = reader.ReadSingle(),
                            EndTimeOffset = reader.ReadSingle()
                        };

                        if (hasCallback)
                            _nodeResolver.RegisterLookup((LeafNode)node, callback, (n, found) => n.Callback = found, NodeType.ANIM_Callback);
                        if (optParam != 0)
                            _nodeResolver.RegisterLookup((LeafNode)node, _strings.GetString(optParam), (n, found) => n.OptionalContextParam = found is ParameterNode ? (ParameterNode)found : null);
                        if (optVector != 0)
                            _nodeResolver.RegisterLookup((LeafNode)node, _strings.GetString(optVector), (n, found) => n.OptionalConvergeVector = found is ParameterNode ? (ParameterNode)found : null);
                        if (optFloat != 0)
                            _nodeResolver.RegisterLookup((LeafNode)node, _strings.GetString(optFloat), (n, found) => n.OptionalConvergeFloat = found is ParameterNode ? (ParameterNode)found : null);
                    }
                    break;
                case NodeType.ANIM_Parametric:
                    {
                        uint childCount = reader.ReadUInt32();

                        List<string> bindings = new List<string>();
                        for (uint i = 0; i < childCount; i++)
                            bindings.Add(_strings.GetString(reader.ReadUInt32()));
                        List<float> valueBindings = new List<float>();
                        for (uint i = 0; i < childCount; i++)
                            valueBindings.Add(reader.ReadSingle());

                        uint paramBind = reader.ReadUInt32();

                        node = new ParametricNode
                        {
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle(),
                            ParameterUsage = (ParameterBlendUsage)reader.ReadUInt32(), 
                            BlendProperty = _strings.GetString(reader.ReadUInt32()),
                            SyncDurations = reader.ReadBoolean(),
                            ExtractBlendPropertiesAutomatically = reader.ReadBoolean()
                        };

                        ParametricNode paramNode = (ParametricNode)node;
                        for (int i = 0; i < bindings.Count; i++)
                        {
                            paramNode.States[i].Value = valueBindings[i];
                            _nodeResolver.RegisterStateLookup(paramNode.States[i], bindings[i], (state, found) => state.Node = found);
                        }
                        if (paramBind != 0)
                            _nodeResolver.RegisterLookup(paramNode, _strings.GetString(paramBind), (n, found) => n.ParameterBinding = found is ParameterNode ? (ParameterNode)found : null);
                    }
                    break;
                case NodeType.ANIM_2DParametric:
                    {
                        uint blendSetId = reader.ReadUInt32();

                        uint XParameter = reader.ReadUInt32();
                        uint YParameter = reader.ReadUInt32();
                        uint ZParameter = reader.ReadUInt32();

                        uint Callback = reader.ReadUInt32();

                        if (ZParameter == 0)
                        {
                            node = new Parametric2DNode()
                            {
                                BlendSet = blendSetId == 0 ? "" : _strings.GetString(blendSetId),
                                LoopBlendSet = reader.ReadBoolean(),
                                SyncBlendSet = reader.ReadBoolean()
                            };
                        }
                        else
                        {
                            node = new Parametric3DNode
                            {
                                BlendSet = blendSetId == 0 ? "" : _strings.GetString(blendSetId),
                                LoopBlendSet = reader.ReadBoolean(),
                                SyncBlendSet = reader.ReadBoolean()
                            };
                        }

                        if (XParameter != 0)
                            _nodeResolver.RegisterLookup(node, _strings.GetString(XParameter), (n, found) => ((Parametric2DNode)n).ParameterBindingX = found is ParameterNode ? (ParameterNode)found : null);
                        if (YParameter != 0)
                            _nodeResolver.RegisterLookup(node, _strings.GetString(YParameter), (n, found) => ((Parametric2DNode)n).ParameterBindingY = found is ParameterNode ? (ParameterNode)found : null);
                        if (ZParameter != 0)
                            _nodeResolver.RegisterLookup(node, _strings.GetString(ZParameter), (n, found) => ((Parametric3DNode)n).ParameterBindingZ = found is ParameterNode ? (ParameterNode)found : null);
                        if (Callback != 0)
                            _nodeResolver.RegisterLookup(node, _strings.GetString(Callback), (n, found) => ((Parametric2DNode)n).OverflowCallback = found, NodeType.ANIM_Callback);
                    }
                    break;
                case NodeType.ANIM_4DParametric:
                    {
                        uint BlendSet = reader.ReadUInt32();
                        uint BlendSetExtra = reader.ReadUInt32();

                        uint XParameter = reader.ReadUInt32();
                        uint YParameter = reader.ReadUInt32();
                        uint ZParameter = reader.ReadUInt32();
                        uint WParameter = reader.ReadUInt32();

                        uint Callback = reader.ReadUInt32();

                        node = new Parametric4DNode
                        {
                            BlendSet = BlendSet == 0 ? "" : _strings.GetString(BlendSet),
                            ExtraBlendSet = BlendSetExtra == 0 ? "" : _strings.GetString(BlendSetExtra),
                            LoopBlendSet = reader.ReadBoolean(),
                            SyncBlendSet = reader.ReadBoolean()
                        };

                        if (XParameter != 0)
                            _nodeResolver.RegisterLookup(node, _strings.GetString(XParameter), (n, found) => ((Parametric4DNode)n).ParameterBindingX = found is ParameterNode ? (ParameterNode)found : null);
                        if (YParameter != 0)
                            _nodeResolver.RegisterLookup(node, _strings.GetString(YParameter), (n, found) => ((Parametric4DNode)n).ParameterBindingY = found is ParameterNode ? (ParameterNode)found : null);
                        if (ZParameter != 0)
                            _nodeResolver.RegisterLookup(node, _strings.GetString(ZParameter), (n, found) => ((Parametric4DNode)n).ParameterBindingZ = found is ParameterNode ? (ParameterNode)found : null);
                        if (WParameter != 0)
                            _nodeResolver.RegisterLookup(node, _strings.GetString(WParameter), (n, found) => ((Parametric4DNode)n).ParameterBindingW = found is ParameterNode ? (ParameterNode)found : null);
                        if (Callback != 0)
                            _nodeResolver.RegisterLookup(node, _strings.GetString(Callback), (n, found) => ((Parametric4DNode)n).OverflowCallback = found, NodeType.ANIM_Callback);
                    }
                    break;
                case NodeType.ANIM_Bone_Mask:
                    {
                        node = new BoneMaskNode
                        {
                            Mask = (BoneMaskGroups)reader.ReadUInt32(),
                            MaskPrecedingLayers = reader.ReadByte() == 1,
                            MaskFollowingLayers = reader.ReadByte() == 1,
                            MaskSelf = reader.ReadByte() == 1
                        };
                    }
                    break;
                case NodeType.ANIM_IK:
                    {
                        uint poseLayer = reader.ReadUInt32();
                        uint ikEffector = reader.ReadUInt32();
                        node = new IkNode
                        {
                            PoseLayer = (PoseLayer)poseLayer,
                            IkType = (IkSolverType)reader.ReadUInt32(),
                            Target = (IkControlTarget)reader.ReadUInt32(),
                            EffectorFullyEffectiveRadius = reader.ReadSingle(),
                            EffectorLeastEffectiveRadius = reader.ReadSingle(),
                            FalloffRate = reader.ReadSingle(),
                            EnforceTranslation = reader.ReadByte() == 1,
                            EnforceEndBoneRotation = reader.ReadByte() == 1
                        };
                        if (ikEffector != 0)
                            _nodeResolver.RegisterLookup((IkNode)node, _strings.GetString(ikEffector), (n, found) => n.IkEffector = found is ParameterNode ? (ParameterNode)found : null);
                    }
                    break;
                case NodeType.ANIM_Randomised_Animation:
                    {
                        bool hasCallback = reader.ReadBoolean();
                        float blendTime = reader.ReadSingle();
                        uint optionalContextParam = reader.ReadUInt32();
                        uint optionalConvergeVector = reader.ReadUInt32();
                        uint optionalConvergeFloat = reader.ReadUInt32();
                        bool convergeOrientation = reader.ReadBoolean();
                        bool convergeTranslation = reader.ReadBoolean();
                        uint hashedCallbackName = reader.ReadUInt32();
                        uint randomNodeCallbackName = reader.ReadUInt32();
                        bool looped = reader.ReadBoolean();
                        bool newSelectionOnLoop = reader.ReadBoolean();

                        uint numberOfAnimSlots = reader.ReadUInt32();
                        List<bool> mirrored = new List<bool>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            mirrored.Add(reader.ReadBoolean());
                        List<string> animNames = new List<string>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            animNames.Add(_strings.GetString(reader.ReadUInt32()));
                        List<string> hashedNames = new List<string>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            hashedNames.Add(_strings.GetString(reader.ReadUInt32()));
                        List<float> weightsForCdf = new List<float>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            weightsForCdf.Add(reader.ReadSingle());
                        List<uint> loopsBeforeReselection = new List<uint>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            loopsBeforeReselection.Add(reader.ReadUInt32());
                        List<float> notifyTimeOffset = new List<float>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            notifyTimeOffset.Add(reader.ReadSingle());
                        List<float> startTimeOffset = new List<float>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            startTimeOffset.Add(reader.ReadSingle());
                        List<float> endTimeOffset = new List<float>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            endTimeOffset.Add(reader.ReadSingle());

                        node = new RandomisedLeafNode
                        {
                            BlendTime = blendTime,
                            Callback = null,
                            RandomCallback = null,
                            Looping = looped,
                            NewSelectionOnLoop = newSelectionOnLoop,
                            ConvergeOrientation = convergeOrientation,
                            ConvergeTranslation = convergeTranslation
                        };

                        RandomisedLeafNode randomNode = (RandomisedLeafNode)node;
                        for (int i = 0; i < numberOfAnimSlots; i++)
                        {
                            RandomisedLeafNode.Animation anim = new RandomisedLeafNode.Animation()
                            {
                                Mirrored = mirrored[i],
                                AnimationName = animNames[i],
                                Weight = weightsForCdf[i],
                                LoopsBeforeReselection = loopsBeforeReselection[i],
                                NotifyTimeOffset = notifyTimeOffset[i],
                                StartTimeOffset = startTimeOffset[i],
                                EndTimeOffset = endTimeOffset[i]
                            };
                            randomNode.AnimationPool[i] = anim;
                        }

                        if (hashedCallbackName != 0)
                            _nodeResolver.RegisterLookup((RandomisedLeafNode)node, _strings.GetString(hashedCallbackName), (n, found) => n.Callback = found, NodeType.ANIM_Callback);
                        if (randomNodeCallbackName != 0)
                            _nodeResolver.RegisterLookup((RandomisedLeafNode)node, _strings.GetString(randomNodeCallbackName), (n, found) => n.RandomCallback = found, NodeType.ANIM_Callback);
                        if (optionalContextParam != 0)
                            _nodeResolver.RegisterLookup((RandomisedLeafNode)node, _strings.GetString(optionalContextParam), (n, found) => n.OptionalAnimationContext = found is ParameterNode ? (ParameterNode)found : null);
                        if (optionalConvergeVector != 0)
                            _nodeResolver.RegisterLookup((RandomisedLeafNode)node, _strings.GetString(optionalConvergeVector), (n, found) => n.OptionalConvergeVector = found is ParameterNode ? (ParameterNode)found : null);
                        if (optionalConvergeFloat != 0)
                            _nodeResolver.RegisterLookup((RandomisedLeafNode)node, _strings.GetString(optionalConvergeFloat), (n, found) => n.OptionalConvergeFloat = found is ParameterNode ? (ParameterNode)found : null);
                    }
                    break;
                case NodeType.ANIM_Selector:
                case NodeType.ANIM_Enumerated_Selector:
                    {
                        uint stateCount = reader.ReadUInt32();
                        List<string> stateNodes = new List<string>();
                        for (uint i = 0; i < stateCount; i++)
                            stateNodes.Add(_strings.GetString(reader.ReadUInt32()));
                        List<uint> stateValues = new List<uint>();
                        for (uint i = 0; i < stateCount; i++)
                            stateValues.Add(reader.ReadUInt32());
                        List<bool> stateFoots = new List<bool>();
                        for (uint i = 0; i < stateCount; i++)
                            stateFoots.Add(reader.ReadBoolean());

                        uint paramName = reader.ReadUInt32();
                        node = new SelectorNode
                        {
                            Type = nodeType,
                            EaseSelectionTime = reader.ReadSingle(),
                            ResetPlaybackOnChangeSelection = reader.ReadBoolean()
                        };
                        SelectorNode selector = (SelectorNode)node;
                        for (int i = 0; i < stateCount; i++)
                        {
                            selector.States[i].Value = stateValues[i];
                            selector.States[i].FootSyncOnSelect = stateFoots[i];
                        }

                        if (paramName != 0)
                            _nodeResolver.RegisterLookup(selector, _strings.GetString(paramName), (n, found) => n.ParameterBinding = found is ParameterNode ? (ParameterNode)found : null);
                        if (stateNodes.Count != 0)
                        {
                            for (int x = 0; x < stateNodes.Count; x++)
                            {
                                _nodeResolver.RegisterStateLookup(selector.States[x], stateNodes[x], (state, found) => state.Node = found);
                            }
                        }
                    }
                    break;
                case NodeType.ANIM_Additive_Blend:
                    {
                        uint baseNode = reader.ReadUInt32();
                        uint additveNode = reader.ReadUInt32();
                        node = new AdditiveBlendNode
                        {
                            AdditiveNodeWeight = reader.ReadSingle(),
                            SyncAdditiveDurationToBase = reader.ReadBoolean()
                        };
                        if (baseNode != 0)
                            _nodeResolver.RegisterLookup((AdditiveBlendNode)node, _strings.GetString(baseNode), (n, found) => n.BaseNode = found);
                        if (additveNode != 0)
                            _nodeResolver.RegisterLookup((AdditiveBlendNode)node, _strings.GetString(additveNode), (n, found) => n.AdditiveNode = found);
                    }
                    break;
                case NodeType.ANIM_Parametric_Additive_Blend:
                    {
                        uint baseNode = reader.ReadUInt32();
                        uint additiveNode = reader.ReadUInt32();
                        float additiveNodeWeight = reader.ReadSingle();
                        uint parameter = reader.ReadUInt32();

                        node = new ParametricAdditiveBlendNode
                        {
                            AdditiveNodeWeight = additiveNodeWeight,
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle(),
                            SyncAdditiveDurationToBase = reader.ReadBoolean()
                        };
                        if (baseNode != 0)
                            _nodeResolver.RegisterLookup((ParametricAdditiveBlendNode)node, _strings.GetString(baseNode), (n, found) => n.BaseNode = found);
                        if (additiveNode != 0)
                            _nodeResolver.RegisterLookup((ParametricAdditiveBlendNode)node, _strings.GetString(additiveNode), (n, found) => n.AdditiveNode = found);
                        if (parameter != 0)
                            _nodeResolver.RegisterLookup((ParametricAdditiveBlendNode)node, _strings.GetString(parameter), (n, found) => n.WeightControlParameter = found is ParameterNode ? (ParameterNode)found : null);
                    }
                    break;
                case NodeType.ANIM_Ranged_Selector:
                    {
                        uint childCount = reader.ReadUInt32();

                        List<string> bindings = new List<string>();
                        for (uint i = 0; i < childCount; i++)
                            bindings.Add(_strings.GetString(reader.ReadUInt32()));
                        List<float> minValueBindings = new List<float>();
                        for (uint i = 0; i < childCount; i++)
                            minValueBindings.Add(reader.ReadSingle());
                        List<float> maxValueBindings = new List<float>();
                        for (uint i = 0; i < childCount; i++)
                            maxValueBindings.Add(reader.ReadSingle());
                        List<bool> footSyncOnSelect = new List<bool>();
                        for (uint i = 0; i < childCount; i++)
                            footSyncOnSelect.Add(reader.ReadBoolean());

                        string parameterName = _strings.GetString(reader.ReadUInt32());
                        node = new RangedSelectorNode
                        {
                            EaseSelectionTime = reader.ReadSingle(),
                            ResetPlaybackOnChange = reader.ReadBoolean()
                        };
                        RangedSelectorNode rangedNode = (RangedSelectorNode)node;
                        for (int i = 0; i <  bindings.Count; i++)
                        {
                            rangedNode.States[i].Min = minValueBindings[i];
                            rangedNode.States[i].Max = maxValueBindings[i];
                            rangedNode.States[i].FootSyncOnSelect = footSyncOnSelect[i];
                            _nodeResolver.RegisterStateLookup(rangedNode.States[i], bindings[i], (state, found) => state.Node = found);
                        }
                        _nodeResolver.RegisterLookup(rangedNode, parameterName, (n, found) => n.ParameterBinding = found is ParameterNode ? (ParameterNode)found : null);
                    }
                    break;
                case NodeType.ANIM_Foot_Sync_Selector:
                    {
                        string[] bindings = new string[2];
                        for (int i = 0; i < 2; i++)
                            bindings[i] = _strings.GetString(reader.ReadUInt32());

                        node = new FootSyncSelectorNode
                        {
                            StrikeSelectionMethod = (FootStrikeSelectionMethod)reader.ReadUInt32(),
                            GaitSyncTargetOnSelect = reader.ReadBoolean(),
                        };
                        _nodeResolver.RegisterLookupArray((FootSyncSelectorNode)node, bindings, (n, found) => {
                            n.LeftStrikeChild = (LeafNode)found[0];
                            n.RightStrikeChild = (LeafNode)found[1];
                        });
                    }
                    break;
                case NodeType.ANIM_Weighted:
                    {
                        uint paramName = reader.ReadUInt32();
                        node = new WeightedNode
                        {
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle()
                        };
                        _nodeResolver.RegisterLookup((WeightedNode)node, _strings.GetString(paramName), (n, found) => n.Parameter = found is ParameterNode ? (ParameterNode)found : null);
                    }
                    break;
                default:
                    throw new Exception("unknown node type");
            }
            node.Name = NodeName;

            if (node.Type == NodeType.ANIM_Weighted && numChildren > 1)
            {
                throw new Exception("unexpected");
            }

            for (int i = 0; i < numChildren; i++)
            {
                var childNode = ReadNode(reader, tree);
                tree.AddNode(childNode);
                node.Children.Add(childNode);

                if (node.Type == NodeType.ANIM_Weighted)
                {
                    WeightedNode weighted = (WeightedNode)node;
                    weighted.Child = childNode;
                }
            }

            return node;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                writer.Write(66);

                writer.Write(Entries.Count);
                writer.Write(Entries.Count);

                var hashEntries = new List<(uint hash, int index)>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                    hashEntries.Add((_strings.GetID(Entries[i].Name), i));
                hashEntries.Sort((a, b) => b.hash.CompareTo(a.hash));

                for (int slot = 0; slot < hashEntries.Count; slot++)
                {
                    writer.Write(hashEntries[slot].hash);
                    writer.Write(hashEntries[slot].index);
                }

                for (int i = 0; i < Entries.Count; i++)
                    writer.Write(i);

                writer.Write(Entries.Count);
                foreach (var tree in Entries)
                {
                    writer.Write(66);
                    writer.Write(tree.Children.Count);
                    writer.Write(_strings.GetID(tree.Name));
                    writer.Write(_strings.GetID(tree.Set));
                    writer.Write(tree.TreeEaseInTime);
                    writer.Write(tree.MinInitialPlayspeed);
                    writer.Write(tree.MaxInitialPlayspeed);
                    writer.Write(tree.NeverUseMotionExtraction);
                    writer.Write(tree.RemoveMotionExtractionOnPreceding);
                    writer.Write(tree.RemoveMotionExtractionOnEaseOut);
                    writer.Write(tree.AllowFootIkIfPrimary);
                    writer.Write(tree.AllowHipLeanIkIfPrimary);
                    writer.Write(tree.GaitSyncOnStart);
                    writer.Write(tree.UseLinearBlend);

                    List<ParameterNode> parameterNodes = tree.Nodes.OfType<ParameterNode>().ToList();
                    List<AnimationNode> callbackNodes = tree.Nodes.Where(n => n.Type == NodeType.ANIM_Callback).ToList();
                    List<MetadataListenerNode> metaListenerNodes = tree.Nodes.OfType<MetadataListenerNode>().ToList();
                    List<AnimationNode> autoFloatNodes = tree.Nodes.Where(n => n.Type == NodeType.ANIM_AutoFloatParameter).ToList();
                    List<PropertyListenerNode> propListenerNodes = tree.Nodes.OfType<PropertyListenerNode>().ToList();
                    List<PropertyNode> propNodes = tree.Nodes.OfType<PropertyNode>().ToList();
                    List<FloatInterpolatorNode> floatInterpolatorNodes = tree.Nodes.OfType<FloatInterpolatorNode>().ToList();

                    writer.Write(parameterNodes.Count);
                    int[] typeCounts = new int[5];
                    foreach (var param in parameterNodes)
                        typeCounts[(int)param.ParameterType]++;
                    writer.Write(typeCounts[(int)AnimTreeParameterType.FLOAT]);
                    writer.Write(typeCounts[(int)AnimTreeParameterType.STRING]);
                    writer.Write(typeCounts[(int)AnimTreeParameterType.MATRIX]);
                    writer.Write(typeCounts[(int)AnimTreeParameterType.VECTOR]);
                    writer.Write(typeCounts[(int)AnimTreeParameterType.CARD32]);
                    writer.Write(callbackNodes.Count);
                    writer.Write(metaListenerNodes.Count);
                    writer.Write(propListenerNodes.Count);
                    writer.Write(propNodes.Count);

                    foreach (var param in parameterNodes)
                        writer.Write(_strings.GetID(param.Name));
                    foreach (var param in parameterNodes)
                        writer.Write((uint)param.ParameterType);

                    //untested - seems correct
                    int[] counts = new int[5];
                    foreach (var param in parameterNodes)
                    {
                        writer.Write((uint)counts[(int)param.ParameterType]);
                        counts[(int)param.ParameterType]++;
                    }

                    foreach (var callback in callbackNodes)
                        writer.Write(_strings.GetID(callback.Name));

                    foreach (var name in metaListenerNodes)
                        writer.Write(_strings.GetID(name.Name));
                    foreach (var name in metaListenerNodes)
                        writer.Write(_strings.GetID(name.EventName));
                    foreach (var threshold in metaListenerNodes)
                        writer.Write(threshold.WeightThreshold);
                    foreach (var time in metaListenerNodes)
                        writer.Write(time.FilterTime);

                    writer.Write(autoFloatNodes.Count);
                    foreach (var name in autoFloatNodes)
                        writer.Write(_strings.GetID(name.Name));

                    foreach (var propListener in propListenerNodes)
                        writer.Write(_strings.GetID(propListener.Name));
                    foreach (var propListener in propListenerNodes)
                        writer.Write(_strings.GetID(propListener.AnimProperty));
                    foreach (var propListener in propListenerNodes)
                        writer.Write(propListener.LeafNode == null ? 0 : _strings.GetID(propListener.LeafNode.Name));

                    foreach (var prop in propNodes)
                        writer.Write(_strings.GetID(prop.Name));
                    foreach (var prop in propNodes)
                        WriteMetadataValue(writer, prop.Value);

                    writer.Write(floatInterpolatorNodes.Count);
                    foreach (var floatInterp in floatInterpolatorNodes)
                        writer.Write(floatInterp.SourceParameter == null ? 0 : _strings.GetID(floatInterp.SourceParameter.Name));
                    foreach (var floatInterp in floatInterpolatorNodes)
                        writer.Write(_strings.GetID(floatInterp.Name));
                    foreach (var floatInterp in floatInterpolatorNodes)
                        writer.Write(floatInterp.InitialValue);
                    foreach (var floatInterp in floatInterpolatorNodes)
                        writer.Write(floatInterp.UnitsPerSecond);

                    foreach (var node in tree.Children)
                        WriteNode(writer, node);
                }
            }
            return true;
        }

        private void WriteNode(BinaryWriter writer, AnimationNode node)
        {
            writer.Write((uint)(node.Type == NodeType.ANIM_3DParametric ? NodeType.ANIM_2DParametric : node.Type));
            writer.Write(_strings.GetID(node.Name));

            writer.Write(node.Children.Count);

            switch (node.Type)
            {
                case NodeType.ANIM_Animation:
                    {
                        LeafNode data = (LeafNode)node;
                        writer.Write(data.Callback != null);
                        writer.Write(data.Callback == null ? 0 : _strings.GetID(data.Callback.Name));
                        writer.Write(data.Looping);
                        writer.Write(data.Mirrored);
                        writer.Write((uint)data.Mask);
                        writer.Write(_strings.GetID(data.AnimationName));
                        writer.Write(data.OptionalContextParam == null ? 0 : _strings.GetID(data.OptionalContextParam.Name));
                        writer.Write(data.OptionalConvergeVector == null ? 0 : _strings.GetID(data.OptionalConvergeVector.Name));
                        writer.Write(data.OptionalConvergeFloat == null ? 0 : _strings.GetID(data.OptionalConvergeFloat.Name));
                        writer.Write(data.ConvergeOrientation);
                        writer.Write(data.ConvergeTranslation);
                        writer.Write(data.NotifyTimeOffset);
                        writer.Write(data.StartTimeOffset);
                        writer.Write(data.EndTimeOffset);
                    }
                    break;
                case NodeType.ANIM_Selector:
                case NodeType.ANIM_Enumerated_Selector:
                    {
                        SelectorNode data = (SelectorNode)node;
                        SelectorNode.State[] states = data.States.Where(o => o.Node != null).ToArray();
                        writer.Write(states.Length);
                        foreach (var state in states)
                            writer.Write(state.Node == null ? 0 : _strings.GetID(state.Node.Name));
                        foreach (var state in states)
                            writer.Write(state.Value);
                        foreach (var state in states)
                            writer.Write(state.FootSyncOnSelect);
                        writer.Write(data.ParameterBinding == null ? 0 : _strings.GetID(data.ParameterBinding.Name));
                        writer.Write(data.EaseSelectionTime);
                        writer.Write(data.ResetPlaybackOnChangeSelection);
                    }
                    break;
                case NodeType.ANIM_Parametric:
                    {
                        ParametricNode data = (ParametricNode)node;
                        ParametricNode.State[] states = data.States.Where(o => o.Node != null).ToArray();
                        writer.Write(states.Length);
                        foreach (var state in states)
                            writer.Write(_strings.GetID(state.Node.Name));
                        foreach (var state in states)
                            writer.Write(state.Value);
                        writer.Write(data.ParameterBinding == null ? 0 : _strings.GetID(data.ParameterBinding.Name));
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                        writer.Write((uint)data.ParameterUsage); //todo verify above if this is string or index
                        writer.Write(_strings.GetID(data.BlendProperty));
                        writer.Write(data.SyncDurations);
                        writer.Write(data.ExtractBlendPropertiesAutomatically);
                    }
                    break;
                case NodeType.ANIM_2DParametric:
                case NodeType.ANIM_3DParametric:
                case NodeType.ANIM_4DParametric:
                    {
                        Parametric2DNode data = (Parametric2DNode)node;
                        writer.Write(string.IsNullOrEmpty(data.BlendSet) ? 0 : _strings.GetID(data.BlendSet));
                        if (node.Type == NodeType.ANIM_4DParametric)
                            writer.Write(string.IsNullOrEmpty(((Parametric4DNode)node).ExtraBlendSet) ? 0 : _strings.GetID(((Parametric4DNode)node).ExtraBlendSet));
                        writer.Write(data.ParameterBindingX == null ? 0 : _strings.GetID(data.ParameterBindingX.Name));
                        writer.Write(data.ParameterBindingY == null ? 0 : _strings.GetID(data.ParameterBindingY.Name));
                        writer.Write(node.Type == NodeType.ANIM_3DParametric || node.Type == NodeType.ANIM_4DParametric ? ((Parametric3DNode)node).ParameterBindingZ == null ? 0 : _strings.GetID(((Parametric3DNode)node).ParameterBindingZ.Name) : 0);
                        if (node.Type == NodeType.ANIM_4DParametric)
                            writer.Write(((Parametric4DNode)node).ParameterBindingW == null ? 0 : _strings.GetID(((Parametric4DNode)node).ParameterBindingW.Name));
                        writer.Write(data.OverflowCallback == null ? 0 : _strings.GetID(data.OverflowCallback.Name));
                        writer.Write(data.LoopBlendSet);
                        writer.Write(data.SyncBlendSet);
                    }
                    break;
                case NodeType.ANIM_Additive_Blend:
                    {
                        AdditiveBlendNode data = (AdditiveBlendNode)node;
                        writer.Write(data.BaseNode == null ? 0 : _strings.GetID(data.BaseNode.Name));
                        writer.Write(data.AdditiveNode == null ? 0 : _strings.GetID(data.AdditiveNode.Name));
                        writer.Write(data.AdditiveNodeWeight);
                        writer.Write(data.SyncAdditiveDurationToBase);
                    }
                    break;
                case NodeType.ANIM_Parametric_Additive_Blend:
                    {
                        ParametricAdditiveBlendNode data = (ParametricAdditiveBlendNode)node;
                        writer.Write(data.BaseNode == null ? 0 : _strings.GetID(data.BaseNode.Name));
                        writer.Write(data.AdditiveNode == null ? 0 : _strings.GetID(data.AdditiveNode.Name));
                        writer.Write(data.AdditiveNodeWeight);
                        writer.Write(data.WeightControlParameter == null ? 0 : _strings.GetID(data.WeightControlParameter.Name));
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                        writer.Write(data.SyncAdditiveDurationToBase);
                    }
                    break;
                case NodeType.ANIM_Ranged_Selector:
                    {
                        RangedSelectorNode data = (RangedSelectorNode)node;
                        RangedSelectorNode.State[] states = data.States.Where(o => o.Node != null).ToArray();
                        writer.Write(states.Length);
                        foreach (var state in states)
                            writer.Write(_strings.GetID(state.Node.Name));
                        foreach (var state in states)
                            writer.Write(state.Min);
                        foreach (var state in states)
                            writer.Write(state.Max);
                        foreach (var state in states)
                            writer.Write(state.FootSyncOnSelect);
                        writer.Write(data.ParameterBinding == null ? 0 : _strings.GetID(data.ParameterBinding.Name));
                        writer.Write(data.EaseSelectionTime);
                        writer.Write(data.ResetPlaybackOnChange);
                    }
                    break;
                case NodeType.ANIM_Foot_Sync_Selector:
                    {
                        FootSyncSelectorNode data = (FootSyncSelectorNode)node;
                        writer.Write(data.LeftStrikeChild == null ? 0 : _strings.GetID(data.LeftStrikeChild.Name));
                        writer.Write(data.RightStrikeChild == null ? 0 : _strings.GetID(data.RightStrikeChild.Name));
                        writer.Write((uint)data.StrikeSelectionMethod);
                        writer.Write(data.GaitSyncTargetOnSelect);
                    }
                    break;
                case NodeType.ANIM_Bone_Mask:
                    {
                        BoneMaskNode data = (BoneMaskNode)node;
                        writer.Write((uint)data.Mask);
                        writer.Write((byte)(data.MaskPrecedingLayers ? 1 : 0));
                        writer.Write((byte)(data.MaskFollowingLayers ? 1 : 0));
                        writer.Write((byte)(data.MaskSelf ? 1 : 0));
                    }
                    break;
                case NodeType.ANIM_IK:
                    {
                        IkNode data = (IkNode)node;
                        writer.Write((uint)data.PoseLayer);
                        writer.Write(data.IkEffector == null ? 0 : _strings.GetID(data.IkEffector.Name));
                        writer.Write((uint)data.IkType);
                        writer.Write((uint)data.Target);
                        writer.Write(data.EffectorFullyEffectiveRadius);
                        writer.Write(data.EffectorLeastEffectiveRadius);
                        writer.Write(data.FalloffRate);
                        writer.Write((byte)(data.EnforceTranslation ? 1 : 0));
                        writer.Write((byte)(data.EnforceEndBoneRotation ? 1 : 0));
                    }
                    break;
                case NodeType.ANIM_Weighted:
                    {
                        WeightedNode data = (WeightedNode)node;
                        writer.Write(data.Parameter == null ? 0 : _strings.GetID(data.Parameter.Name));
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                    }
                    break;
                case NodeType.ANIM_Randomised_Animation:
                    {
                        RandomisedLeafNode data = (RandomisedLeafNode)node;
                        writer.Write(data.Callback != null);
                        writer.Write(data.BlendTime);
                        writer.Write(data.OptionalAnimationContext == null ? 0 : _strings.GetID(data.OptionalAnimationContext.Name));
                        writer.Write(data.OptionalConvergeVector == null ? 0 : _strings.GetID(data.OptionalConvergeVector.Name));
                        writer.Write(data.OptionalConvergeFloat == null ? 0 : _strings.GetID(data.OptionalConvergeFloat.Name));
                        writer.Write(data.ConvergeOrientation);
                        writer.Write(data.ConvergeTranslation);
                        writer.Write(data.Callback == null ? 0 : _strings.GetID(data.Callback.Name));
                        writer.Write(data.RandomCallback == null ? 0 : _strings.GetID(data.RandomCallback.Name));
                        writer.Write(data.Looping);
                        writer.Write(data.NewSelectionOnLoop);
                        RandomisedLeafNode.Animation[] anims = data.AnimationPool.Where(o => o != null && o.AnimationName != "").ToArray();
                        writer.Write(anims.Length);
                        foreach (var anim in anims)
                            writer.Write(anim.Mirrored);
                        foreach (var anim in anims)
                            writer.Write(_strings.GetID(anim.AnimationName));
                        for (int i = 0; i < anims.Length; i++)
                            writer.Write(_strings.GetID(data.Name + "#@_RAND@#_random_animation_file" + i));
                        foreach (var anim in anims)
                            writer.Write(anim.Weight);
                        foreach (var anim in anims)
                            writer.Write(anim.LoopsBeforeReselection);
                        foreach (var anim in anims)
                            writer.Write(anim.NotifyTimeOffset);
                        foreach (var anim in anims)
                            writer.Write(anim.StartTimeOffset);
                        foreach (var anim in anims)
                            writer.Write(anim.EndTimeOffset);
                    }
                    break;
            }

            foreach (var child in node.Children)
                WriteNode(writer, child);
        }

        private AnimationMetadataValue ReadMetadataValue(BinaryReader reader)
        {
            reader.BaseStream.Position += 8;
            byte[] union = reader.ReadBytes(16);
            MetadataValueType valueType = (MetadataValueType)reader.ReadUInt32();
            ushort requiresConvert = reader.ReadUInt16();
            byte canMirror = reader.ReadByte();
            byte canModulate = reader.ReadByte();

            AnimationMetadataValue metadataValue;
            if (AnimationMetadataValue.Is64BitType(valueType))
            {
                ulong as64 = BitConverter.ToUInt64(union, 0);
                switch (valueType)
                {
                    case MetadataValueType.UINT64:
                        metadataValue = new ULongMetadataValue(as64);
                        break;
                    case MetadataValueType.INT64:
                        metadataValue = new LongMetadataValue((long)as64);
                        break;
                    case MetadataValueType.FLOAT64:
                        metadataValue = new Float64MetadataValue(BitConverter.ToDouble(union, 0));
                        break;
                    default:
                        metadataValue = new ULongMetadataValue(as64);
                        break;
                }
            }
            else if (AnimationMetadataValue.IsVectorType(valueType))
            {
                metadataValue = new VectorMetadataValue(new Vector3(
                    BitConverter.ToSingle(union, 0),
                    BitConverter.ToSingle(union, 4),
                    BitConverter.ToSingle(union, 8)));
            }
            else
            {
                uint as32 = BitConverter.ToUInt32(union, 0);
                switch (valueType)
                {
                    case MetadataValueType.UINT32:
                        metadataValue = new UIntMetadataValue(as32);
                        break;
                    case MetadataValueType.INT32:
                        metadataValue = new IntMetadataValue((int)as32);
                        break;
                    case MetadataValueType.FLOAT32:
                        metadataValue = new FloatMetadataValue(BitConverter.ToSingle(union, 0));
                        break;
                    case MetadataValueType.BOOL:
                        metadataValue = new BoolMetadataValue(as32 != 0);
                        break;
                    case MetadataValueType.STRING:
                        metadataValue = new StringMetadataValue(_strings.GetString(as32));
                        break;
                    case MetadataValueType.AUDIO:
                        metadataValue = new AudioMetadataValue(_strings.GetString(as32));
                        break;
                    case MetadataValueType.PROPERTY_REFERENCE:
                        metadataValue = new PropertyReferenceMetadataValue(_strings.GetString(as32));
                        break;
                    case MetadataValueType.SCRIPT_INTERFACE:
                        metadataValue = new ScriptInterfaceMetadataValue(_strings.GetString(as32));
                        break;
                    default:
                        metadataValue = new UIntMetadataValue(as32);
                        break;
                }
            }

            metadataValue.RequiresConvert = requiresConvert != 0;
            metadataValue.CanMirror = canMirror != 0;
            metadataValue.CanModulateByPlayspeed = canModulate != 0;
            return metadataValue;
        }

        private void WriteMetadataValue(BinaryWriter writer, AnimationMetadataValue value)
        {
            writer.Write((ulong)0); 

            if (AnimationMetadataValue.Is64BitType(value.ValueType))
            {
                ulong as64 = 0;
                switch (value.ValueType)
                {
                    case MetadataValueType.UINT64:
                        as64 = ((ULongMetadataValue)value).Value;
                        break;
                    case MetadataValueType.INT64:
                        as64 = (ulong)((LongMetadataValue)value).Value;
                        break;
                    case MetadataValueType.FLOAT64:
                        as64 = BitConverter.ToUInt64(BitConverter.GetBytes(((Float64MetadataValue)value).Value), 0);
                        break;
                }
                writer.Write(as64);
                writer.Write((ulong)0);
            }
            else if (AnimationMetadataValue.IsVectorType(value.ValueType))
            {
                Vector3 v = ((VectorMetadataValue)value).Value;
                writer.Write(v.X);
                writer.Write(v.Y);
                writer.Write(v.Z);
                writer.Write((uint)0);
            }
            else
            {
                uint as32 = 0;
                switch (value.ValueType)
                {
                    case MetadataValueType.UINT32:
                        as32 = ((UIntMetadataValue)value).Value;
                        break;
                    case MetadataValueType.INT32:
                        as32 = (uint)((IntMetadataValue)value).Value;
                        break;
                    case MetadataValueType.FLOAT32:
                        as32 = BitConverter.ToUInt32(BitConverter.GetBytes(((FloatMetadataValue)value).Value), 0);
                        break;
                    case MetadataValueType.BOOL:
                        as32 = ((BoolMetadataValue)value).Value ? 1u : 0u;
                        break;
                    case MetadataValueType.STRING:
                        as32 = string.IsNullOrEmpty(((StringMetadataValue)value).Value) ? 0 : _strings.GetID(((StringMetadataValue)value).Value);
                        break;
                    case MetadataValueType.AUDIO:
                        as32 = string.IsNullOrEmpty(((AudioMetadataValue)value).Value) ? 0 : _strings.GetID(((AudioMetadataValue)value).Value);
                        break;
                    case MetadataValueType.PROPERTY_REFERENCE:
                        as32 = string.IsNullOrEmpty(((PropertyReferenceMetadataValue)value).Value) ? 0 : _strings.GetID(((PropertyReferenceMetadataValue)value).Value);
                        break;
                    case MetadataValueType.SCRIPT_INTERFACE:
                        as32 = string.IsNullOrEmpty(((ScriptInterfaceMetadataValue)value).Value) ? 0 : _strings.GetID(((ScriptInterfaceMetadataValue)value).Value);
                        break;
                }
                writer.Write(as32);
                writer.Write((uint)0);
                writer.Write((ulong)0);
            }

            writer.Write((uint)value.ValueType);
            writer.Write((ushort)(value.RequiresConvert ? 1 : 0));
            writer.Write((byte)(value.CanMirror ? 1 : 0));
            writer.Write((byte)(value.CanModulateByPlayspeed ? 1 : 0));
        }
        #endregion

        private class NodeResolver
        {
            private List<Action<AnimationTree>> _deferredLookups = new List<Action<AnimationTree>>();

            private static AnimationNode Find(AnimationTree tree, string nodeName, NodeType? nodeType)
            {
                if (string.IsNullOrEmpty(nodeName))
                    return null;
                if (tree.TryGetNode(nodeName, out AnimationNode found, nodeType))
                    return found;
                return null;
            }

            public void RegisterLookup<T>(T target, string nodeName, Action<T, AnimationNode> setter, NodeType? nodeType = null) where T : class
            {
                _deferredLookups.Add(tree => setter(target, Find(tree, nodeName, nodeType)));
            }

            public void RegisterLookupList<T>(T target, List<string> nodeNames, Action<T, List<AnimationNode>> setter, NodeType? nodeType = null) where T : class
            {
                _deferredLookups.Add(tree =>
                {
                    setter(target, nodeNames
                        .Select(name => Find(tree, name, nodeType))
                        .Where(o => o != null)
                        .ToList());
                });
            }

            public void RegisterLookupArray<T>(T target, string[] nodeNames, Action<T, AnimationNode[]> setter, NodeType? nodeType = null) where T : class
            {
                _deferredLookups.Add(tree =>
                {
                    setter(target, nodeNames
                        .Select(name => Find(tree, name, nodeType))
                        .Where(o => o != null)
                        .ToArray());
                });
            }

            public void RegisterStateLookup<T>(T target, string nodeName, Action<T, AnimationNode> setter, NodeType? nodeType = null) where T : class
            {
                _deferredLookups.Add(tree => setter(target, Find(tree, nodeName, nodeType)));
            }

            public void ResolveAll(AnimationTree tree)
            {
                foreach (var lookup in _deferredLookups)
                    lookup(tree);
                _deferredLookups.Clear();
            }

            public void Clear()
            {
                _deferredLookups.Clear();
            }
        }
    }
}
