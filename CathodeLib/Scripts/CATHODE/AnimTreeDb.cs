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

                //todo: does this index matter? is it some priority order?
                var treeHashToIndex = new Dictionary<uint, int>();
                for (int i = 0; i < hashTableSize; i++)
                {
                    uint treeHash = reader.ReadUInt32();
                    //string treeHashStr = _strings.Entries[treeHash];
                    int index = reader.ReadInt32();
                    treeHashToIndex[treeHash] = index;
                }
                for (int i = 0; i < hashTableSize; i++)
                    reader.ReadUInt32();

                int treeCount = reader.ReadInt32();
                for (int i = 0; i < treeCount; i++)
                {
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
                        treeDef.Nodes.Add(new ParameterNode() { Name = bindingNames[x], ParameterType = bindingParamTypes[x], IndicesInTypeArray = bindingType[x] });

                    for (int x = 0; x < NumberOfCallbacks; x++)
                        treeDef.Nodes.Add(new AnimationNode() { Type = AnimationNodeType.ANIM_Callback, Name = _strings.GetString(reader.ReadUInt32()) });

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
                        treeDef.Nodes.Add(new MetadataListenerNode() { Name = metaListNames[x], EventName = metaListEvents[x], WeightThreshold = metaListWeights[x], FilterTime = metaListFilterTimes[x] });

                    uint NumberOfAutoFloatBindings = reader.ReadUInt32();
                    for (int x = 0; x < NumberOfAutoFloatBindings; x++)
                        treeDef.Nodes.Add(new AnimationNode() { Type = AnimationNodeType.ANIM_AutoFloatParameter, Name = _strings.GetString(reader.ReadUInt32()) });

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
                        treeDef.Nodes.Add(new PropertyListenerNode() { Name = propListenerNames[x], AnimProperty = propListenerPropNames[x], LeafNode = propListenerLeafNodes[x] });

                    List<string> propertyNames = new List<string>();
                    for (int x = 0; x < NumberOfPropertyValues; x++)
                        propertyNames.Add(_strings.GetString(reader.ReadUInt32()));
                    List<AnimationMetadataValue> propertyValues = new List<AnimationMetadataValue>();
                    for (int x = 0; x < NumberOfPropertyValues; x++)
                    {
                        ulong valueUnion = reader.ReadUInt64();
                        uint valueType = reader.ReadUInt32();
                        ushort flags = reader.ReadUInt16();
                        byte requiresConvert = reader.ReadByte();
                        byte canMirror = reader.ReadByte();
                        byte canModulateByPlayspeed = reader.ReadByte();
                        reader.BaseStream.Position += 15;

                        AnimationMetadataValue metadataValue;
                        switch ((AnimationMetadataValueType)valueType)
                        {
                            case AnimationMetadataValueType.UINT32:
                                metadataValue = new UIntMetadataValue((uint)valueUnion);
                                break;
                            case AnimationMetadataValueType.INT32:
                                metadataValue = new IntMetadataValue((int)valueUnion);
                                break;
                            case AnimationMetadataValueType.UINT64:
                                metadataValue = new ULongMetadataValue(valueUnion);
                                break;
                            case AnimationMetadataValueType.INT64:
                                metadataValue = new LongMetadataValue((long)valueUnion);
                                break;
                            case AnimationMetadataValueType.FLOAT32:
                                metadataValue = new FloatMetadataValue(BitConverter.ToSingle(BitConverter.GetBytes(valueUnion), 0));
                                break;
                            case AnimationMetadataValueType.FLOAT64:
                                metadataValue = new Float64MetadataValue(BitConverter.ToDouble(BitConverter.GetBytes(valueUnion), 0));
                                break;
                            case AnimationMetadataValueType.BOOL:
                                metadataValue = new BoolMetadataValue(valueUnion != 0);
                                break;
                            case AnimationMetadataValueType.STRING:
                                metadataValue = new StringMetadataValue();
                                break;
                            case AnimationMetadataValueType.VECTOR:
                                metadataValue = new VectorMetadataValue();
                                break;
                            case AnimationMetadataValueType.AUDIO:
                                metadataValue = new AudioMetadataValue();
                                break;
                            case AnimationMetadataValueType.PROPERTY_REFERENCE:
                                metadataValue = new PropertyReferenceMetadataValue();
                                break;
                            case AnimationMetadataValueType.SCRIPT_INTERFACE:
                                metadataValue = new ScriptInterfaceMetadataValue();
                                break;
                            default:
                                metadataValue = new UIntMetadataValue((uint)valueUnion);
                                break;
                        }
                        metadataValue.Flags = flags;
                        metadataValue.RequiresConvert = requiresConvert != 0;
                        metadataValue.CanMirror = canMirror != 0;
                        metadataValue.CanModulateByPlayspeed = canModulateByPlayspeed != 0;
                        propertyValues.Add(metadataValue);
                    }
                    for (int x = 0; x < NumberOfPropertyValues; x++)
                        treeDef.Nodes.Add(new PropertyNode() { Name = propertyNames[x], Value = propertyValues[x] });

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
                        ParameterNode floatInterpNodeOrig = (ParameterNode)treeDef.Nodes.FirstOrDefault(o => o.Name == floatInterpNames[x]);
                        FloatInterpolatorNode floatInterpNode = new FloatInterpolatorNode() { Name = floatInterpNodeOrig.Name, Children = floatInterpNodeOrig.Children, IndicesInTypeArray = floatInterpNodeOrig.IndicesInTypeArray, ParameterType = floatInterpNodeOrig.ParameterType, Type = AnimationNodeType.ANIM_FloatInterpolator };
                        floatInterpNode.SourceParameter = floatInterpSources[x];
                        floatInterpNode.InitialValue = floatInterpStartVals[x];
                        floatInterpNode.UnitsPerSecond = floatInterpStartUPS[x];
                        treeDef.Nodes.Add(floatInterpNode);
                        treeDef.Nodes.Remove(floatInterpNodeOrig);
                    }

                    for (int x = 0; x < numChildren; x++)
                    {
                        var childNode = ReadNode(reader, treeDef);
                        treeDef.Nodes.Add(childNode);
                        treeDef.Children.Add(childNode);
                    }

                    foreach (KeyValuePair<LeafNode, string> kvp in _animLookups)
                    {
                        kvp.Key.Callback = treeDef.Nodes.FirstOrDefault(o => o.Type == AnimationNodeType.ANIM_Callback &&  o.Name == kvp.Value);
                    }
                    foreach (KeyValuePair<WeightedNode, string> kvp in _weightedLookups)
                    {
                        kvp.Key.Parameter = (ParameterNode)treeDef.Nodes.FirstOrDefault(o => o.Type == AnimationNodeType.ANIM_Parameter && o.Name == kvp.Value);
                    }
                    foreach (KeyValuePair<SelectorNode, string> kvp in _selectorLookups)
                    {
                        kvp.Key.BindingParameter = (ParameterNode)treeDef.Nodes.FirstOrDefault(o => o.Type == AnimationNodeType.ANIM_Parameter && o.Name == kvp.Value);
                    }
                    foreach (KeyValuePair<SelectorNode, List<string>> kvp in _selectorBindingLookups)
                    {
                        if (kvp.Value.Count != kvp.Key.States.Count)
                            throw new Exception("unexpected count");

                        for (int x = 0; x < kvp.Key.States.Count; x++)
                        {
                            kvp.Key.States[x].Node = (LeafNode)treeDef.Nodes.FirstOrDefault(o => o.Type == AnimationNodeType.ANIM_Animation && o.Name == kvp.Value[x]);
                        }
                    }
                    foreach (KeyValuePair<IkNode, string> kvp in _ikLookups)
                    {
                        kvp.Key.IkEffector = (ParameterNode)treeDef.Nodes.FirstOrDefault(o => o.Type == AnimationNodeType.ANIM_Parameter && o.Name == kvp.Value);
                    }

                    Entries.Add(treeDef);
                }
            }
            return true;
        }

        Dictionary<LeafNode, string> _animLookups = new Dictionary<LeafNode, string>();
        Dictionary<WeightedNode, string> _weightedLookups = new Dictionary<WeightedNode, string>();
        Dictionary<SelectorNode, string> _selectorLookups = new Dictionary<SelectorNode, string>();
        Dictionary<SelectorNode, List<string>> _selectorBindingLookups = new Dictionary<SelectorNode, List<string>>();
        Dictionary<IkNode, string> _ikLookups = new Dictionary<IkNode, string>();

        private AnimationNode ReadNode(BinaryReader reader, AnimationTree tree)
        {
            AnimationNodeType nodeType = (AnimationNodeType)reader.ReadUInt32();
            string NodeName = _strings.GetString(reader.ReadUInt32());
            uint numChildren = reader.ReadUInt32();

            AnimationNode node;
            switch (nodeType)
            {
                case AnimationNodeType.ANIM_Animation:
                    {
                        bool hasCallback = reader.ReadBoolean();
                        string callback = _strings.GetString(reader.ReadUInt32());
                        node = new LeafNode
                        {
                            Callback = null,
                            Looped = reader.ReadBoolean(),
                            Mirrored = reader.ReadBoolean(),
                            MaskingControl = (BoneMaskGroups)reader.ReadUInt32(),
                            AnimationName = _strings.GetString(reader.ReadUInt32()),
                            OptionalContextParam = _strings.GetString(reader.ReadUInt32()),
                            OptionalConvergeVector = _strings.GetString(reader.ReadUInt32()),
                            OptionalConvergeFloat = _strings.GetString(reader.ReadUInt32()),
                            ConvergeOrientation = reader.ReadBoolean(),
                            ConvergeTranslation = reader.ReadBoolean(),
                            NotifyTimeOffset = reader.ReadSingle(),
                            StartTimeOffset = reader.ReadSingle(),
                            EndTimeOffset = reader.ReadSingle()
                        };
                        if (hasCallback)
                            _animLookups.Add((LeafNode)node, callback);
                    }
                    break;
                case AnimationNodeType.ANIM_Parametric:
                    {
                        uint childCount = reader.ReadUInt32();

                        List<string> bindings = new List<string>();
                        for (uint i = 0; i < childCount; i++)
                            bindings.Add(_strings.GetString(reader.ReadUInt32()));
                        List<float> valueBindings = new List<float>();
                        for (uint i = 0; i < childCount; i++)
                            valueBindings.Add(reader.ReadSingle());

                        node = new ParametricNode
                        {
                            Bindings = bindings,
                            ValueBindings = valueBindings,
                            BindingParameter = _strings.GetString(reader.ReadUInt32()),
                            Min = reader.ReadSingle(),
                            Max = reader.ReadSingle(),
                            ParameterUsage = _strings.GetString(reader.ReadUInt32()),
                            AutoBlendProperty = _strings.GetString(reader.ReadUInt32()),
                            SyncDurations = reader.ReadBoolean(),
                            UseAutoDerivedBlendValues = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_2DParametric:
                    {
                        string BlendSet = _strings.GetString(reader.ReadUInt32());
                        uint XParameter = reader.ReadUInt32();
                        uint YParameter = reader.ReadUInt32();
                        uint ZParameter = reader.ReadUInt32();
                        node = new Parametric3DNode
                        {
                            BlendSet = new string[1] { BlendSet },
                            XParameter = _strings.GetString(XParameter),
                            YParameter = _strings.GetString(YParameter),
                            ZParameter = _strings.GetString(ZParameter),
                            OverflowListener = _strings.GetString(reader.ReadUInt32()),
                            LoopBlendSet = reader.ReadBoolean(),
                            SyncBlendSet = reader.ReadBoolean()
                        };
                        if (ZParameter == 0)
                        {
                            Parametric2DNode node2D = (Parametric2DNode)node; //todo: this doesnt actually change class from 3d like i expected
                            node2D.Type = AnimationNodeType.ANIM_2DParametric;
                            node = node2D;
                        }
                    }
                    break;
                case AnimationNodeType.ANIM_4DParametric:
                    {
                        string[] blendSet = new string[2];
                        for (int i = 0; i < 2; i++)
                            blendSet[i] = _strings.GetString(reader.ReadUInt32());

                        node = new Parametric4DNode
                        {
                            BlendSet = blendSet,
                            XParameter = _strings.GetString(reader.ReadUInt32()),
                            YParameter = _strings.GetString(reader.ReadUInt32()),
                            ZParameter = _strings.GetString(reader.ReadUInt32()),
                            WParameter = _strings.GetString(reader.ReadUInt32()),
                            OverflowListener = _strings.GetString(reader.ReadUInt32()),
                            LoopBlendSet = reader.ReadBoolean(),
                            SyncBlendSet = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_Additive_Blend:
                    {
                        node = new AdditiveBlendNode
                        {
                            BaseNode = _strings.GetString(reader.ReadUInt32()),
                            AdditiveNode = _strings.GetString(reader.ReadUInt32()),
                            AdditiveNodeWeight = reader.ReadSingle(),
                            SyncAdditiveDurationToBase = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_Bone_Mask:
                    {
                        node = new BoneMaskNode
                        {
                            MaskingControl = reader.ReadUInt32(),
                            MaskPreceding = reader.ReadByte() == 1,
                            MaskFollowing = reader.ReadByte() == 1,
                            MaskSelf = reader.ReadByte() == 1
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_IK:
                    {
                        uint poseLayer = reader.ReadUInt32();
                        uint ikEffector = reader.ReadUInt32();
                        node = new IkNode
                        {
                            PoseLayer = poseLayer,
                            IkEffector = null,
                            IkType = _strings.GetString(reader.ReadUInt32()),
                            Target = _strings.GetString(reader.ReadUInt32()),
                            EffectorFullyEffectiveRadius = reader.ReadSingle(),
                            EffectorLeastEffectiveRadius = reader.ReadSingle(),
                            FalloffRate = reader.ReadUInt32(),
                            EnforceTranslation = reader.ReadByte() == 1,
                            EnforceEndBoneRotation = reader.ReadByte() == 1
                        };
                        if (ikEffector != 0)
                            _ikLookups.Add((IkNode)node, _strings.GetString(ikEffector));
                    }
                    break;
                case AnimationNodeType.ANIM_Randomised_Animation:
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
                            HasCallback = hasCallback,
                            BlendTime = blendTime,
                            CallbackName = _strings.GetString(hashedCallbackName),
                            RandomNodeCallbackName = _strings.GetString(randomNodeCallbackName),
                            Looped = looped,
                            NewSelectionOnLoop = newSelectionOnLoop,
                            Mirrored = mirrored,
                            OptionalContextParam = optionalContextParam,
                            OptionalConvergeVector = optionalConvergeVector,
                            OptionalConvergeFloat = optionalConvergeFloat,
                            NumberOfAnimSlots = numberOfAnimSlots,
                            Animations = animNames,
                            Names = hashedNames,
                            WeightsForCdf = weightsForCdf,
                            LoopsBeforeReselection = loopsBeforeReselection,
                            NotifyTimeOffset = notifyTimeOffset,
                            StartTimeOffset = startTimeOffset,
                            EndTimeOffset = endTimeOffset,
                            ConvergeOrientation = convergeOrientation,
                            ConvergeTranslation = convergeTranslation
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_Selector:
                case AnimationNodeType.ANIM_Enumerated_Selector:
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
                        List<SelectorNode.State> states = new List<SelectorNode.State>();
                        for (int i = 0; i < stateCount; i++)
                            states.Add(new SelectorNode.State() { Node = null, Value = stateValues[i], FootSyncOnSelect = stateFoots[i] });

                        uint paramName = reader.ReadUInt32();
                        node = new SelectorNode
                        {
                            Type = nodeType,
                            States = states,
                            BindingParameter = null,
                            EaseSelectionTime = reader.ReadSingle(),
                            ResetPlaybackOnChangeSelection = reader.ReadBoolean()
                        };
                        if (paramName != 0)
                            _selectorLookups.Add((SelectorNode)node, _strings.GetString(paramName));
                        if (stateNodes.Count != 0)
                            _selectorBindingLookups.Add((SelectorNode)node, stateNodes);
                    }
                    break;
                case AnimationNodeType.ANIM_Parametric_Additive_Blend:
                    {
                        node = new ParametricAdditiveBlendNode
                        {
                            BaseNode = _strings.GetString(reader.ReadUInt32()),
                            AdditiveNode = _strings.GetString(reader.ReadUInt32()),
                            AdditiveNodeWeight = reader.ReadSingle(),
                            ParameterName = _strings.GetString(reader.ReadUInt32()),
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle(),
                            SyncAdditiveDurationToBase = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_Spherical:
                    {
                        uint childCount = reader.ReadUInt32();
                        uint coordHash = reader.ReadUInt32();
                        uint numTris = reader.ReadUInt32();

                        List<SphericalNode.BlendTriIndices> tris = new List<SphericalNode.BlendTriIndices>();
                        for (uint i = 0; i < numTris; i++)
                        {
                            tris.Add(new SphericalNode.BlendTriIndices
                            {
                                Index0 = reader.ReadUInt32(),
                                Index1 = reader.ReadUInt32(),
                                Index2 = reader.ReadUInt32(),
                                X0 = reader.ReadSingle(),
                                Y0 = reader.ReadSingle(),
                                X1 = reader.ReadSingle(),
                                Y1 = reader.ReadSingle(),
                                X2 = reader.ReadSingle(),
                                Y2 = reader.ReadSingle(),
                            });
                            reader.BaseStream.Position += 12;
                        }

                        node = new SphericalNode
                        {
                            Coord = _strings.GetString(coordHash),
                            Tris = tris,
                            NumTris = numTris,
                            SyncDurations = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_Bilinear_Low_Fidelity:
                    {
                        string[] bindings = new string[4];
                        for (int i = 0; i < 4; i++)
                            bindings[i] = _strings.GetString(reader.ReadUInt32());

                        node = new LoFiBilinearNode
                        {
                            Bindings = bindings,
                            XParameter = _strings.GetString(reader.ReadUInt32()),
                            YParameter = _strings.GetString(reader.ReadUInt32()),
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle(),
                            ParameterWrap = reader.ReadBoolean(),
                            SyncDurations = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_Bilinear_High_Fidelity:
                    {
                        string[] hashBindings = new string[9];
                        for (int i = 0; i < 9; i++)
                            hashBindings[i] = _strings.GetString(reader.ReadUInt32());

                        node = new BilinearHiFiNode
                        {
                            Bindings = hashBindings,
                            XParameter = _strings.GetString(reader.ReadUInt32()),
                            YParameter = _strings.GetString(reader.ReadUInt32()),
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle(),
                            ParameterWrap = reader.ReadBoolean(),
                            SyncDurations = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_Ranged_Selector:
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

                        node = new RangedSelectorNode
                        {
                            Bindings = bindings,
                            MinValueBindings = minValueBindings,
                            MaxValueBindings = maxValueBindings,
                            FootSyncOnSelect = footSyncOnSelect,
                            BindingParameterName = _strings.GetString(reader.ReadUInt32()),
                            EaseTime = reader.ReadSingle(),
                            ResetPlaybackOnChange = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_Foot_Sync_Selector:
                    {
                        string[] bindings = new string[2];
                        for (int i = 0; i < 2; i++)
                            bindings[i] = _strings.GetString(reader.ReadUInt32());

                        node = new FootSyncSelectorNode
                        {
                            Bindings = bindings,
                            FootStrikeSelectionMethod = (FootStrikeSelectionMethod)reader.ReadUInt32(),
                            GaitSyncTargetOnSelect = reader.ReadBoolean(),
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_Weighted:
                    {
                        uint paramName = reader.ReadUInt32();
                        node = new WeightedNode
                        {
                            Parameter = null,
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle()
                        };
                        _weightedLookups.Add((WeightedNode)node, _strings.GetString(paramName));
                    }
                    break;
                default:
                    throw new Exception("unknown node type");
            }
            node.Name = NodeName;

            for (int i = 0; i < numChildren; i++)
            {
                var childNode = ReadNode(reader, tree);
                tree.Nodes.Add(childNode);
                node.Children.Add(childNode);
            }

            return node;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);

                writer.Write(66);

                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(_strings.GetID(Entries[i].Name));
                    writer.Write(i);
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

                    //note to self - these sorting methods are untested
                    List<AnimationNode> nodes = tree.Nodes.ToList();
                    List<ParameterNode> parameterNodes = nodes.FindAll(o => o is ParameterNode).Cast<ParameterNode>().ToList();
                    List<AnimationNode> callbackNodes = nodes.FindAll(o => o.Type == AnimationNodeType.ANIM_Callback).Cast<AnimationNode>().ToList();
                    List<MetadataListenerNode> metaListenerNodes = nodes.FindAll(o => o.Type == AnimationNodeType.ANIM_Metadata_Event_Listener).Cast<MetadataListenerNode>().ToList();
                    List<AnimationNode> autoFloatNodes = nodes.FindAll(o => o.Type == AnimationNodeType.ANIM_AutoFloatParameter).Cast<AnimationNode>().ToList();
                    List<PropertyListenerNode> propListenerNodes = nodes.FindAll(o => o.Type == AnimationNodeType.ANIM_Property_Listener).Cast<PropertyListenerNode>().ToList();
                    List<PropertyNode> propNodes = nodes.FindAll(o => o.Type == AnimationNodeType.ANIM_Property).Cast<PropertyNode>().ToList();
                    List<FloatInterpolatorNode> floatInterpolatorNodes = nodes.FindAll(o => o.Type == AnimationNodeType.ANIM_FloatInterpolator).Cast<FloatInterpolatorNode>().ToList();

                    writer.Write(parameterNodes.Count);
                    writer.Write(new byte[20]);
                    writer.Write(callbackNodes.Count);
                    writer.Write(metaListenerNodes.Count);
                    writer.Write(propListenerNodes.Count);
                    writer.Write(propNodes.Count);

                    foreach (var param in parameterNodes)
                        writer.Write(_strings.GetID(param.Name));
                    foreach (var param in parameterNodes)
                        writer.Write((uint)param.ParameterType);
                    foreach (var param in parameterNodes)
                        writer.Write(param.IndicesInTypeArray);

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
                        writer.Write(_strings.GetID(propListener.LeafNode));

                    foreach (var prop in propNodes)
                        writer.Write(_strings.GetID(prop.Name));
                    foreach (var prop in propNodes)
                    {
                        ulong valueUnion = 0;
                        switch (prop.Value.ValueType)
                        {
                            case AnimationMetadataValueType.UINT32:
                                valueUnion = ((UIntMetadataValue)prop.Value).Value;
                                break;
                            case AnimationMetadataValueType.INT32:
                                valueUnion = (ulong)((IntMetadataValue)prop.Value).Value;
                                break;
                            case AnimationMetadataValueType.FLOAT32:
                                valueUnion = BitConverter.ToUInt64(BitConverter.GetBytes(((FloatMetadataValue)prop.Value).Value), 0);
                                break;
                            case AnimationMetadataValueType.BOOL:
                                valueUnion = ((BoolMetadataValue)prop.Value).Value ? 1ul : 0ul;
                                break;
                            case AnimationMetadataValueType.UINT64:
                                valueUnion = ((ULongMetadataValue)prop.Value).Value;
                                break;
                            case AnimationMetadataValueType.INT64:
                                valueUnion = (ulong)((LongMetadataValue)prop.Value).Value;
                                break;
                            case AnimationMetadataValueType.FLOAT64:
                                valueUnion = BitConverter.ToUInt64(BitConverter.GetBytes(((Float64MetadataValue)prop.Value).Value), 0);
                                break;
                            case AnimationMetadataValueType.STRING:
                            case AnimationMetadataValueType.VECTOR:
                            case AnimationMetadataValueType.AUDIO:
                            case AnimationMetadataValueType.PROPERTY_REFERENCE:
                            case AnimationMetadataValueType.SCRIPT_INTERFACE:
                                //These types don't store their values in the union, they're stored separately
                                valueUnion = 0;
                                break;
                            default:
                                valueUnion = 0;
                                break;
                        }

                        writer.Write(valueUnion);
                        writer.Write((uint)prop.Value.ValueType);
                        writer.Write(prop.Value.Flags);
                        writer.Write((byte)(prop.Value.RequiresConvert ? 1 : 0));
                        writer.Write((byte)(prop.Value.CanMirror ? 1 : 0));
                        writer.Write((byte)(prop.Value.CanModulateByPlayspeed ? 1 : 0));
                        writer.Write(new byte[15]);
                    }

                    writer.Write(floatInterpolatorNodes.Count);
                    foreach (var floatInterp in floatInterpolatorNodes)
                        writer.Write(_strings.GetID(floatInterp.SourceParameter));
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
            writer.Write((uint)(node.Type == AnimationNodeType.ANIM_3DParametric ? AnimationNodeType.ANIM_2DParametric : node.Type));
            writer.Write(_strings.GetID(node.Name));

            switch (node.Type)
            {
                case AnimationNodeType.ANIM_Animation:
                    {
                        LeafNode data = (LeafNode)node;
                        writer.Write(data.Callback != null);
                        writer.Write(data.Callback == null ? 0 : _strings.GetID(data.Callback.Name));
                        writer.Write(data.Looped);
                        writer.Write(data.Mirrored);
                        writer.Write((uint)data.MaskingControl);
                        writer.Write(_strings.GetID(data.AnimationName));
                        writer.Write(_strings.GetID(data.OptionalContextParam));
                        writer.Write(_strings.GetID(data.OptionalConvergeVector));
                        writer.Write(_strings.GetID(data.OptionalConvergeFloat));
                        writer.Write(data.ConvergeOrientation);
                        writer.Write(data.ConvergeTranslation);
                        writer.Write(data.NotifyTimeOffset);
                        writer.Write(data.StartTimeOffset);
                        writer.Write(data.EndTimeOffset);
                    }
                    break;
                case AnimationNodeType.ANIM_Selector:
                case AnimationNodeType.ANIM_Enumerated_Selector:
                    {
                        SelectorNode data = (SelectorNode)node;
                        writer.Write(node.Children.Count);
                        foreach (var state in data.States)
                            writer.Write(state.Node == null ? 0 : _strings.GetID(state.Node.Name));
                        foreach (var state in data.States)
                            writer.Write(state.Value);
                        foreach (var state in data.States)
                            writer.Write(state.FootSyncOnSelect);
                        writer.Write(data.BindingParameter == null ? 0 : _strings.GetID(data.BindingParameter.Name));
                        writer.Write(data.EaseSelectionTime);
                        writer.Write(data.ResetPlaybackOnChangeSelection);
                    }
                    break;
                case AnimationNodeType.ANIM_Parametric:
                    {
                        ParametricNode data = (ParametricNode)node;
                        writer.Write(node.Children.Count);
                        foreach (var hash in data.Bindings)
                            writer.Write(_strings.GetID(hash));
                        foreach (var value in data.ValueBindings)
                            writer.Write(value);
                        writer.Write(_strings.GetID(data.BindingParameter));
                        writer.Write(data.Min);
                        writer.Write(data.Max);
                        writer.Write(_strings.GetID(data.ParameterUsage));
                        writer.Write(_strings.GetID(data.AutoBlendProperty));
                        writer.Write(data.SyncDurations);
                        writer.Write(data.UseAutoDerivedBlendValues);
                    }
                    break;
                case AnimationNodeType.ANIM_2DParametric:
                case AnimationNodeType.ANIM_3DParametric:
                    {
                        Parametric2DNode data = (Parametric2DNode)node;
                        writer.Write(_strings.GetID(data.BlendSet[0]));
                        writer.Write(_strings.GetID(data.XParameter));
                        writer.Write(_strings.GetID(data.YParameter));
                        writer.Write(node.Type == AnimationNodeType.ANIM_3DParametric ? _strings.GetID(((Parametric3DNode)node).ZParameter) : 0);
                        writer.Write(_strings.GetID(data.OverflowListener));
                        writer.Write(data.LoopBlendSet);
                        writer.Write(data.SyncBlendSet);
                    }
                    break;
                case AnimationNodeType.ANIM_4DParametric:
                    {
                        Parametric4DNode data = (Parametric4DNode)node;
                        foreach (var blendSet in data.BlendSet)
                            writer.Write(_strings.GetID(blendSet));
                        writer.Write(_strings.GetID(data.XParameter));
                        writer.Write(_strings.GetID(data.YParameter));
                        writer.Write(_strings.GetID(data.ZParameter));
                        writer.Write(_strings.GetID(data.WParameter));
                        writer.Write(_strings.GetID(data.OverflowListener));
                        writer.Write(data.LoopBlendSet);
                        writer.Write(data.SyncBlendSet);
                    }
                    break;
                case AnimationNodeType.ANIM_Bilinear_High_Fidelity:
                    {
                        BilinearHiFiNode data = (BilinearHiFiNode)node;
                        foreach (var hash in data.Bindings)
                            writer.Write(_strings.GetID(hash));
                        writer.Write(_strings.GetID(data.XParameter));
                        writer.Write(_strings.GetID(data.YParameter));
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                        writer.Write(data.ParameterWrap);
                        writer.Write(data.SyncDurations);
                    }
                    break;
                case AnimationNodeType.ANIM_Bilinear_Low_Fidelity:
                    {
                        LoFiBilinearNode data = (LoFiBilinearNode)node;
                        foreach (var hash in data.Bindings)
                            writer.Write(_strings.GetID(hash));
                        writer.Write(_strings.GetID(data.XParameter));
                        writer.Write(_strings.GetID(data.YParameter));
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                        writer.Write(data.ParameterWrap);
                        writer.Write(data.SyncDurations);
                    }
                    break;
                case AnimationNodeType.ANIM_Additive_Blend:
                    {
                        AdditiveBlendNode data = (AdditiveBlendNode)node;
                        writer.Write(_strings.GetID(data.BaseNode));
                        writer.Write(_strings.GetID(data.AdditiveNode));
                        writer.Write(data.AdditiveNodeWeight);
                        writer.Write(data.SyncAdditiveDurationToBase);
                    }
                    break;
                case AnimationNodeType.ANIM_Parametric_Additive_Blend:
                    {
                        ParametricAdditiveBlendNode data = (ParametricAdditiveBlendNode)node;
                        writer.Write(_strings.GetID(data.BaseNode));
                        writer.Write(_strings.GetID(data.AdditiveNode));
                        writer.Write(data.AdditiveNodeWeight);
                        writer.Write(_strings.GetID(data.ParameterName));
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                        writer.Write(data.SyncAdditiveDurationToBase);
                    }
                    break;
                case AnimationNodeType.ANIM_Ranged_Selector:
                    {
                        RangedSelectorNode data = (RangedSelectorNode)node;
                        writer.Write(node.Children.Count);
                        foreach (var hash in data.Bindings)
                            writer.Write(_strings.GetID(hash));
                        foreach (var value in data.MinValueBindings)
                            writer.Write(value);
                        foreach (var value in data.MaxValueBindings)
                            writer.Write(value);
                        foreach (var footSync in data.FootSyncOnSelect)
                            writer.Write(footSync);
                        writer.Write(_strings.GetID(data.BindingParameterName));
                        writer.Write(data.EaseTime);
                        writer.Write(data.ResetPlaybackOnChange);
                    }
                    break;
                case AnimationNodeType.ANIM_Foot_Sync_Selector:
                    {
                        FootSyncSelectorNode data = (FootSyncSelectorNode)node;
                        foreach (var hash in data.Bindings)
                            writer.Write(_strings.GetID(hash));
                        writer.Write((uint)data.FootStrikeSelectionMethod);
                        writer.Write(data.GaitSyncTargetOnSelect);
                    }
                    break;
                case AnimationNodeType.ANIM_Bone_Mask:
                    {
                        BoneMaskNode data = (BoneMaskNode)node;
                        writer.Write(data.MaskingControl);
                        writer.Write((byte)(data.MaskPreceding ? 1 : 0));
                        writer.Write((byte)(data.MaskFollowing ? 1 : 0));
                        writer.Write((byte)(data.MaskSelf ? 1 : 0));
                    }
                    break;
                case AnimationNodeType.ANIM_IK:
                    {
                        IkNode data = (IkNode)node;
                        writer.Write(data.PoseLayer);
                        writer.Write(data.IkEffector == null ? 0 : _strings.GetID(data.IkEffector.Name));
                        writer.Write(data.IkType);
                        writer.Write(data.Target);
                        writer.Write(data.EffectorFullyEffectiveRadius);
                        writer.Write(data.EffectorLeastEffectiveRadius);
                        writer.Write(data.FalloffRate);
                        writer.Write((byte)(data.EnforceTranslation ? 1 : 0));
                        writer.Write((byte)(data.EnforceEndBoneRotation ? 1 : 0));
                    }
                    break;
                case AnimationNodeType.ANIM_Spherical:
                    {
                        SphericalNode data = (SphericalNode)node;
                        writer.Write(node.Children.Count);
                        writer.Write(_strings.GetID(data.Coord));
                        writer.Write(data.NumTris);
                        foreach (var tri in data.Tris)
                        {
                            writer.Write(tri.Index0);
                            writer.Write(tri.Index1);
                            writer.Write(tri.Index2);
                            writer.Write(tri.X0);
                            writer.Write(tri.Y0);
                            writer.Write(tri.X1);
                            writer.Write(tri.Y1);
                            writer.Write(tri.X2);
                            writer.Write(tri.Y2);
                            writer.Write(new byte[12]);
                        }
                        writer.Write(data.SyncDurations);
                    }
                    break;
                case AnimationNodeType.ANIM_Weighted:
                    {
                        WeightedNode data = (WeightedNode)node;
                        writer.Write(data.Parameter == null ? 0 : _strings.GetID(data.Parameter.Name));
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                    }
                    break;
                case AnimationNodeType.ANIM_Randomised_Animation:
                    {
                        RandomisedLeafNode data = (RandomisedLeafNode)node;
                        writer.Write(data.HasCallback);
                        writer.Write(data.BlendTime);
                        writer.Write(data.OptionalContextParam);
                        writer.Write(data.OptionalConvergeVector);
                        writer.Write(data.OptionalConvergeFloat);
                        writer.Write(data.ConvergeOrientation);
                        writer.Write(data.ConvergeTranslation);
                        writer.Write(_strings.GetID(data.CallbackName));
                        writer.Write(_strings.GetID(data.RandomNodeCallbackName));
                        writer.Write(data.Looped);
                        writer.Write(data.NewSelectionOnLoop);
                        writer.Write(data.NumberOfAnimSlots);
                        foreach (var mirrored in data.Mirrored)
                            writer.Write(mirrored);
                        foreach (var anim in data.Animations)
                            writer.Write(_strings.GetID(anim));
                        foreach (var name in data.Names)
                            writer.Write(_strings.GetID(name));
                        foreach (var weight in data.WeightsForCdf)
                            writer.Write(weight);
                        foreach (var loops in data.LoopsBeforeReselection)
                            writer.Write(loops);
                        foreach (var offset in data.NotifyTimeOffset)
                            writer.Write(offset);
                        foreach (var offset in data.StartTimeOffset)
                            writer.Write(offset);
                        foreach (var offset in data.EndTimeOffset)
                            writer.Write(offset);
                    }
                    break;
            }

            writer.Write(node.Children.Count);
            foreach (var child in node.Children)
                WriteNode(writer, child);
        }
        #endregion
    }
}
