using CATHODE.Animations;
using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using static CATHODE.SkeleDB;

namespace CATHODE
{
    /// <summary>
    /// DATA/GLOBAL/ANIMATION.PAK -> ANIM_TREE_DB.BIN
    /// </summary>
    public class AnimTreeDb : CathodeFile
    {
        public string Set = "";
        public List<AnimationTree> Entries = new List<AnimationTree>();
        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;

        public AnimTreeDb(string path, AnimationStrings strings) : base(path)
        {
            _strings = strings;
            _loaded = Load();
        }
        public AnimTreeDb(MemoryStream stream, AnimationStrings strings, string path) : base(stream, path)
        {
            _strings = strings;
            _loaded = Load(stream);
        }
        public AnimTreeDb(byte[] data, AnimationStrings strings, string path) : base(data, path)
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
                        TreeName = _strings.Entries[reader.ReadUInt32()],
                        TreeSetName = _strings.Entries[reader.ReadUInt32()],
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

                    for (uint x = 0; x < NumberOfBindings; x++)
                        treeDef.BindingHashedNames.Add(reader.ReadUInt32());
                    for (uint x = 0; x < NumberOfBindings; x++)
                        treeDef.ParameterTypes.Add(reader.ReadUInt32());
                    for (uint x = 0; x < NumberOfBindings; x++)
                        treeDef.IndicesInTypeArrays.Add(reader.ReadUInt32());

                    for (uint x = 0; x < NumberOfCallbacks; x++)
                        treeDef.CallbackHashedNames.Add(reader.ReadUInt32());

                    for (uint x = 0; x < NumberOfMetadataListeners; x++)
                        treeDef.MetadataListenerNames.Add(reader.ReadUInt32());
                    for (uint x = 0; x < NumberOfMetadataListeners; x++)
                        treeDef.MetadataEventNames.Add(reader.ReadUInt32());
                    for (uint x = 0; x < NumberOfMetadataListeners; x++)
                        treeDef.MetadataListenerWeightThresholds.Add(reader.ReadSingle());
                    for (uint x = 0; x < NumberOfMetadataListeners; x++)
                        treeDef.MetadataListenerFilterTimes.Add(reader.ReadSingle());

                    uint NumberOfAutoFloatBindings = reader.ReadUInt32();
                    for (uint x = 0; x < NumberOfAutoFloatBindings; x++)
                        treeDef.AutoFloatNames.Add(reader.ReadUInt32());

                    for (uint x = 0; x < NumberOfPropertyListeners; x++)
                        treeDef.PropertyListenerNames.Add(reader.ReadUInt32());
                    for (uint x = 0; x < NumberOfPropertyListeners; x++)
                        treeDef.PropertyListenerPropertyNames.Add(reader.ReadUInt32());
                    for (uint x = 0; x < NumberOfPropertyListeners; x++)
                        treeDef.PropertyListenerLeafNames.Add(reader.ReadUInt32());

                    for (uint x = 0; x < NumberOfPropertyValues; x++)
                        treeDef.PropertyValueNames.Add(reader.ReadUInt32());
                    for (uint x = 0; x < NumberOfPropertyValues; x++)
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
                        treeDef.PropertyValues.Add(metadataValue);
                    }

                    uint NumberOfFloatInterpolatorBindings = reader.ReadUInt32();
                    for (uint x = 0; x < NumberOfFloatInterpolatorBindings; x++)
                        treeDef.FloatInterpolatorSourceNames.Add(reader.ReadUInt32());
                    for (uint x = 0; x < NumberOfFloatInterpolatorBindings; x++)
                        treeDef.FloatInterpolatorNames.Add(reader.ReadUInt32());
                    for (uint x = 0; x < NumberOfFloatInterpolatorBindings; x++)
                        treeDef.FloatInterpolatorStartValues.Add(reader.ReadSingle());
                    for (uint x = 0; x < NumberOfFloatInterpolatorBindings; x++)
                        treeDef.FloatInterpolatorRates.Add(reader.ReadSingle());

                    for (int x = 0; x < numChildren; x++)
                    {
                        var node = ReadNode(reader);
                        treeDef.Nodes.Add(node);
                    }

                    Entries.Add(treeDef);
                }
            }
            return true;
        }

        private AnimationNode ReadNode(BinaryReader reader)
        {
            AnimationBlendNodeType nodeType = (AnimationBlendNodeType)reader.ReadUInt32();
            string NodeName = _strings.GetString(reader.ReadUInt32());
            uint numChildren = reader.ReadUInt32();

            AnimationNode node;
            switch (nodeType)
            {
                case AnimationBlendNodeType.LEAF_NODE:
                    {
                        node = new LeafNode
                        {
                            NodeType = AnimationBlendNodeType.LEAF_NODE,
                            HasCallback = reader.ReadBoolean(),
                            HashedCallbackName = reader.ReadUInt32(),
                            Looped = reader.ReadBoolean(),
                            Mirrored = reader.ReadBoolean(),
                            MaskingControl = (BoneMaskGroups)reader.ReadUInt32(),
                            LevelAnimIndex = reader.ReadUInt32(),
                            OptionalContextParam = reader.ReadUInt32(),
                            OptionalConvergeVector = reader.ReadUInt32(),
                            OptionalConvergeFloat = reader.ReadUInt32(),
                            ConvergeOrientation = reader.ReadBoolean(),
                            ConvergeTranslation = reader.ReadBoolean(),
                            NotifyTimeOffset = reader.ReadSingle(),
                            StartTimeOffset = reader.ReadSingle(),
                            EndTimeOffset = reader.ReadSingle()
                        };
                    }
                    break;
                case AnimationBlendNodeType.SELECTOR_NODE:
                    {
                        uint childCount = reader.ReadUInt32();

                        List<uint> hashBindings = new List<uint>();
                        for (uint i = 0; i < childCount; i++)
                            hashBindings.Add(reader.ReadUInt32());
                        List<uint> valueBindings = new List<uint>();
                        for (uint i = 0; i < childCount; i++)
                            valueBindings.Add(reader.ReadUInt32());
                        List<bool> footSyncOnSelect = new List<bool>();
                        for (uint i = 0; i < childCount; i++)
                            footSyncOnSelect.Add(reader.ReadBoolean());

                        node = new SelectorNode
                        {
                            NodeType = AnimationBlendNodeType.SELECTOR_NODE,
                            NumChildren = childCount,
                            HashBindings = hashBindings,
                            ValueBindings = valueBindings,
                            FootSyncOnSelect = footSyncOnSelect,
                            BindingParameterHash = reader.ReadUInt32(),
                            EaseTime = reader.ReadSingle(),
                            ResetPlaybackOnChange = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.PARAMETRIC_NODE:
                    {
                        uint childCount = reader.ReadUInt32();

                        List<uint> hashBindings = new List<uint>();
                        for (uint i = 0; i < childCount; i++)
                            hashBindings.Add(reader.ReadUInt32());
                        List<float> valueBindings = new List<float>();
                        for (uint i = 0; i < childCount; i++)
                            valueBindings.Add(reader.ReadSingle());

                        node = new ParametricNode
                        {
                            NodeType = AnimationBlendNodeType.PARAMETRIC_NODE,
                            NumChildren = childCount,
                            HashBindings = hashBindings,
                            ValueBindings = valueBindings,
                            BindingParameterHash = reader.ReadUInt32(),
                            Min = reader.ReadSingle(),
                            Max = reader.ReadSingle(),
                            ParameterUsage = reader.ReadUInt32(),
                            AutoBlendProperty = reader.ReadUInt32(),
                            SyncDurations = reader.ReadBoolean(),
                            UseAutoDerivedBlendValues = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.BLEND_SET_NODE:
                    {
                        node = new BlendSetNode
                        {
                            NodeType = AnimationBlendNodeType.BLEND_SET_NODE,
                            BlendSet = reader.ReadUInt32(),
                            XParameter = reader.ReadUInt32(),
                            YParameter = reader.ReadUInt32(),
                            ZParameter = reader.ReadUInt32(),
                            OverflowListener = reader.ReadUInt32(),
                            LoopBlendSet = reader.ReadBoolean(),
                            SyncBlendSet = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.BILINEAR_NODE:
                    {
                        var hashBindings = new uint[9];
                        for (int i = 0; i < 9; i++)
                            hashBindings[i] = reader.ReadUInt32();

                        node = new BilinearNode
                        {
                            NodeType = AnimationBlendNodeType.BILINEAR_NODE,
                            HashBindings = hashBindings,
                            XParameterHash = reader.ReadUInt32(),
                            YParameterHash = reader.ReadUInt32(),
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle(),
                            ParameterWrap = reader.ReadBoolean(),
                            SyncDurations = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.ADDITIVE_BLEND_NODE:
                    {
                        node = new AdditiveBlendNode
                        {
                            NodeType = AnimationBlendNodeType.ADDITIVE_BLEND_NODE,
                            BaseNodeHash = reader.ReadUInt32(),
                            AdditiveNodeHash = reader.ReadUInt32(),
                            AdditiveNodeWeight = reader.ReadSingle(),
                            SyncAdditiveDurationToBase = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.BONE_MASK_NODE:
                    {
                        node = new BoneMaskNode
                        {
                            NodeType = AnimationBlendNodeType.BONE_MASK_NODE,
                            MaskingControl = reader.ReadUInt32(),
                            MaskPreceding = reader.ReadByte(),
                            MaskFollowing = reader.ReadByte(),
                            MaskSelf = reader.ReadByte()
                        };
                    }
                    break;
                case AnimationBlendNodeType.IK_NODE:
                    {
                        node = new IkNode
                        {
                            NodeType = AnimationBlendNodeType.IK_NODE,
                            PoseLayer = reader.ReadUInt32(),
                            IkEffectorHash = reader.ReadUInt32(),
                            IkType = reader.ReadUInt32(),
                            IkTarget = reader.ReadUInt32(),
                            EffectorFullyEffectiveRadius = reader.ReadSingle(),
                            EffectorLeastEffectiveRadius = reader.ReadSingle(),
                            FalloffRate = reader.ReadUInt32(),
                            EnforceTranslation = reader.ReadByte(),
                            EnforceEndBoneRotation = reader.ReadByte()
                        };
                    }
                    break;
                case AnimationBlendNodeType.RANDOMISED_LEAF_NODE:
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
                        List<uint> levelAnimIndices = new List<uint>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            levelAnimIndices.Add(reader.ReadUInt32());
                        List<uint> hashedNames = new List<uint>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            hashedNames.Add(reader.ReadUInt32());
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
                            NodeType = AnimationBlendNodeType.RANDOMISED_LEAF_NODE,
                            HasCallback = hasCallback,
                            BlendTime = blendTime,
                            HashedCallbackName = hashedCallbackName,
                            RandomNodeCallbackName = randomNodeCallbackName,
                            Looped = looped,
                            NewSelectionOnLoop = newSelectionOnLoop,
                            Mirrored = mirrored,
                            OptionalContextParam = optionalContextParam,
                            OptionalConvergeVector = optionalConvergeVector,
                            OptionalConvergeFloat = optionalConvergeFloat,
                            NumberOfAnimSlots = numberOfAnimSlots,
                            LevelAnimIndices = levelAnimIndices,
                            HashedNames = hashedNames,
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
                case AnimationBlendNodeType.ENUMERATED_SELECTOR_NODE:
                    {
                        uint childCount = reader.ReadUInt32();

                        List<uint> hashBindings = new List<uint>();
                        for (uint i = 0; i < childCount; i++)
                            hashBindings.Add(reader.ReadUInt32());
                        List<uint> valueBindings = new List<uint>();
                        for (uint i = 0; i < childCount; i++)
                            valueBindings.Add(reader.ReadUInt32());
                        List<bool> footSyncOnSelect = new List<bool>();
                        for (uint i = 0; i < childCount; i++)
                            footSyncOnSelect.Add(reader.ReadBoolean());

                        node = new EnumeratedSelectorNode
                        {
                            NodeType = AnimationBlendNodeType.ENUMERATED_SELECTOR_NODE,
                            NumChildren = childCount,
                            HashBindings = hashBindings,
                            ValueBindings = valueBindings,
                            FootSyncOnSelect = footSyncOnSelect,
                            BindingParameterHash = reader.ReadUInt32(),
                            EaseTime = reader.ReadSingle(),
                            ResetPlaybackOnChange = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.BLEND_SET_4D_NODE:
                    {
                        var blendSet = new uint[2];
                        for (int i = 0; i < 2; i++)
                            blendSet[i] = reader.ReadUInt32();

                        node = new BlendSet4DNode
                        {
                            NodeType = AnimationBlendNodeType.BLEND_SET_4D_NODE,
                            BlendSet = blendSet,
                            XParameter = reader.ReadUInt32(),
                            YParameter = reader.ReadUInt32(),
                            ZParameter = reader.ReadUInt32(),
                            WParameter = reader.ReadUInt32(),
                            OverflowListener = reader.ReadUInt32(),
                            LoopBlendSets = reader.ReadBoolean(),
                            SyncBlendSets = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.PARAMETRIC_ADDITIVE_BLEND_NODE:
                    {
                        node = new ParametricAdditiveBlendNode
                        {
                            NodeType = AnimationBlendNodeType.PARAMETRIC_ADDITIVE_BLEND_NODE,
                            BaseNodeHash = reader.ReadUInt32(),
                            AdditiveNodeHash = reader.ReadUInt32(),
                            AdditiveNodeWeight = reader.ReadSingle(),
                            ParameterHash = reader.ReadUInt32(),
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle(),
                            SyncAdditiveDurationToBase = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.SPHERICAL_NODE:
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
                            NodeType = AnimationBlendNodeType.SPHERICAL_NODE,
                            CoordHash = coordHash,
                            NumChildren = childCount,
                            Tris = tris,
                            NumTris = numTris,
                            SyncDurations = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.LO_FI_BILINEAR_NODE:
                    {
                        var hashBindings = new uint[4];
                        for (int i = 0; i < 4; i++)
                            hashBindings[i] = reader.ReadUInt32();

                        node = new LoFiBilinearNode
                        {
                            NodeType = AnimationBlendNodeType.LO_FI_BILINEAR_NODE,
                            HashBindings = hashBindings,
                            XParameterHash = reader.ReadUInt32(),
                            YParameterHash = reader.ReadUInt32(),
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle(),
                            ParameterWrap = reader.ReadBoolean(),
                            SyncDurations = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.RANGED_SELECTOR_NODE:
                    {
                        uint childCount = reader.ReadUInt32();

                        List<uint> hashBindings = new List<uint>();
                        for (uint i = 0; i < childCount; i++)
                            hashBindings.Add(reader.ReadUInt32());
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
                            NodeType = AnimationBlendNodeType.RANGED_SELECTOR_NODE,
                            NumChildren = childCount,
                            HashBindings = hashBindings,
                            MinValueBindings = minValueBindings,
                            MaxValueBindings = maxValueBindings,
                            FootSyncOnSelect = footSyncOnSelect,
                            BindingParameterHash = reader.ReadUInt32(),
                            EaseTime = reader.ReadSingle(),
                            ResetPlaybackOnChange = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationBlendNodeType.FOOT_SYNC_SELECTOR_NODE:
                    {
                        var hashBindings = new uint[2];
                        for (int i = 0; i < 2; i++)
                            hashBindings[i] = reader.ReadUInt32();

                        node = new FootSyncSelectorNode
                        {
                            NodeType = AnimationBlendNodeType.FOOT_SYNC_SELECTOR_NODE,
                            HashBindings = hashBindings,
                            FootStrikeSelectionMethod = reader.ReadUInt32(),
                            GaitSyncTargetOnSelect = reader.ReadBoolean(),
                        };
                    }
                    break;
                case AnimationBlendNodeType.WEIGHTED_NODE:
                    {
                        node = new WeightedNode
                        {
                            NodeType = AnimationBlendNodeType.WEIGHTED_NODE,
                            ParameterHash = reader.ReadUInt32(),
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle()
                        };
                    }
                    break;
                default:
                    throw new Exception("unknown node type");
            }
            node.NodeName = NodeName;

            for (int i = 0; i < numChildren; i++)
            {
                var childNode = ReadNode(reader);
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
                    writer.Write(_strings.GetID(Entries[i].TreeName));
                    writer.Write(i);
                }
                for (int i = 0; i < Entries.Count; i++)
                    writer.Write(i);

                writer.Write(Entries.Count);
                foreach (var tree in Entries)
                {
                    writer.Write(66);
                    writer.Write(tree.Nodes.Count);
                    writer.Write(_strings.GetID(tree.TreeName));
                    writer.Write(_strings.GetID(tree.TreeSetName));
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

                    writer.Write(tree.BindingHashedNames.Count);
                    writer.Write(new byte[20]);
                    writer.Write(tree.CallbackHashedNames.Count);
                    writer.Write(tree.MetadataListenerNames.Count);
                    writer.Write(tree.PropertyListenerNames.Count);
                    writer.Write(tree.PropertyValueNames.Count);

                    foreach (var name in tree.BindingHashedNames)
                        writer.Write(name);
                    foreach (var type in tree.ParameterTypes)
                        writer.Write(type);
                    foreach (var index in tree.IndicesInTypeArrays)
                        writer.Write(index);

                    foreach (var name in tree.CallbackHashedNames)
                        writer.Write(name);

                    foreach (var name in tree.MetadataListenerNames)
                        writer.Write(name);
                    foreach (var name in tree.MetadataEventNames)
                        writer.Write(name);
                    foreach (var threshold in tree.MetadataListenerWeightThresholds)
                        writer.Write(threshold);
                    foreach (var time in tree.MetadataListenerFilterTimes)
                        writer.Write(time);

                    writer.Write(tree.AutoFloatNames.Count);
                    foreach (var name in tree.AutoFloatNames)
                        writer.Write(name);

                    foreach (var name in tree.PropertyListenerNames)
                        writer.Write(name);
                    foreach (var name in tree.PropertyListenerPropertyNames)
                        writer.Write(name);
                    foreach (var name in tree.PropertyListenerLeafNames)
                        writer.Write(name);

                    foreach (var name in tree.PropertyValueNames)
                        writer.Write(name);
                    foreach (var value in tree.PropertyValues)
                    {
                        ulong valueUnion = 0;
                        switch (value.ValueType)
                        {
                            case AnimationMetadataValueType.UINT32:
                                valueUnion = ((UIntMetadataValue)value).Value;
                                break;
                            case AnimationMetadataValueType.INT32:
                                valueUnion = (ulong)((IntMetadataValue)value).Value;
                                break;
                            case AnimationMetadataValueType.FLOAT32:
                                valueUnion = BitConverter.ToUInt64(BitConverter.GetBytes(((FloatMetadataValue)value).Value), 0);
                                break;
                            case AnimationMetadataValueType.BOOL:
                                valueUnion = ((BoolMetadataValue)value).Value ? 1ul : 0ul;
                                break;
                            case AnimationMetadataValueType.UINT64:
                                valueUnion = ((ULongMetadataValue)value).Value;
                                break;
                            case AnimationMetadataValueType.INT64:
                                valueUnion = (ulong)((LongMetadataValue)value).Value;
                                break;
                            case AnimationMetadataValueType.FLOAT64:
                                valueUnion = BitConverter.ToUInt64(BitConverter.GetBytes(((Float64MetadataValue)value).Value), 0);
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
                        writer.Write((uint)value.ValueType);
                        writer.Write(value.Flags);
                        writer.Write((byte)(value.RequiresConvert ? 1 : 0));
                        writer.Write((byte)(value.CanMirror ? 1 : 0));
                        writer.Write((byte)(value.CanModulateByPlayspeed ? 1 : 0));
                        writer.Write(new byte[15]);
                    }

                    writer.Write(tree.FloatInterpolatorSourceNames.Count);
                    foreach (var name in tree.FloatInterpolatorSourceNames)
                        writer.Write(name);
                    foreach (var name in tree.FloatInterpolatorNames)
                        writer.Write(name);
                    foreach (var value in tree.FloatInterpolatorStartValues)
                        writer.Write(value);
                    foreach (var rate in tree.FloatInterpolatorRates)
                        writer.Write(rate);

                    foreach (var node in tree.Nodes)
                        WriteNode(writer, node);
                }
            }
            return true;
        }

        private void WriteNode(BinaryWriter writer, AnimationNode node)
        {
            writer.Write((int)node.NodeType);
            writer.Write(_strings.GetID(node.NodeName));

            switch (node.NodeType)
            {
                case AnimationBlendNodeType.LEAF_NODE:
                    {
                        LeafNode data = (LeafNode)node;
                        writer.Write(data.HasCallback);
                        writer.Write(data.HashedCallbackName);
                        writer.Write(data.Looped);
                        writer.Write(data.Mirrored);
                        writer.Write((uint)data.MaskingControl);
                        writer.Write(data.LevelAnimIndex);
                        writer.Write(data.OptionalContextParam);
                        writer.Write(data.OptionalConvergeVector);
                        writer.Write(data.OptionalConvergeFloat);
                        writer.Write(data.ConvergeOrientation);
                        writer.Write(data.ConvergeTranslation);
                        writer.Write(data.NotifyTimeOffset);
                        writer.Write(data.StartTimeOffset);
                        writer.Write(data.EndTimeOffset);
                    }
                    break;
                case AnimationBlendNodeType.SELECTOR_NODE:
                    {
                        SelectorNode data = (SelectorNode)node;
                        writer.Write(data.NumChildren);
                        foreach (var hash in data.HashBindings)
                            writer.Write(hash);
                        foreach (var value in data.ValueBindings)
                            writer.Write(value);
                        foreach (var footSync in data.FootSyncOnSelect)
                            writer.Write(footSync);
                        writer.Write(data.BindingParameterHash);
                        writer.Write(data.EaseTime);
                        writer.Write(data.ResetPlaybackOnChange);
                    }
                    break;
                case AnimationBlendNodeType.PARAMETRIC_NODE:
                    {
                        ParametricNode data = (ParametricNode)node;
                        writer.Write(data.NumChildren);
                        foreach (var hash in data.HashBindings)
                            writer.Write(hash);
                        foreach (var value in data.ValueBindings)
                            writer.Write(value);
                        writer.Write(data.BindingParameterHash);
                        writer.Write(data.Min);
                        writer.Write(data.Max);
                        writer.Write(data.ParameterUsage);
                        writer.Write(data.AutoBlendProperty);
                        writer.Write(data.SyncDurations);
                        writer.Write(data.UseAutoDerivedBlendValues);
                    }
                    break;
                case AnimationBlendNodeType.BLEND_SET_NODE:
                    {
                        BlendSetNode data = (BlendSetNode)node;
                        writer.Write(data.BlendSet);
                        writer.Write(data.XParameter);
                        writer.Write(data.YParameter);
                        writer.Write(data.ZParameter);
                        writer.Write(data.OverflowListener);
                        writer.Write(data.LoopBlendSet);
                        writer.Write(data.SyncBlendSet);
                    }
                    break;
                case AnimationBlendNodeType.BLEND_SET_4D_NODE:
                    {
                        BlendSet4DNode data = (BlendSet4DNode)node;
                        foreach (var blendSet in data.BlendSet)
                            writer.Write(blendSet);
                        writer.Write(data.XParameter);
                        writer.Write(data.YParameter);
                        writer.Write(data.ZParameter);
                        writer.Write(data.WParameter);
                        writer.Write(data.OverflowListener);
                        writer.Write(data.LoopBlendSets);
                        writer.Write(data.SyncBlendSets);
                    }
                    break;
                case AnimationBlendNodeType.BILINEAR_NODE:
                    {
                        BilinearNode data = (BilinearNode)node;
                        foreach (var hash in data.HashBindings)
                            writer.Write(hash);
                        writer.Write(data.XParameterHash);
                        writer.Write(data.YParameterHash);
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                        writer.Write(data.ParameterWrap);
                        writer.Write(data.SyncDurations);
                    }
                    break;
                case AnimationBlendNodeType.LO_FI_BILINEAR_NODE:
                    {
                        LoFiBilinearNode data = (LoFiBilinearNode)node;
                        foreach (var hash in data.HashBindings)
                            writer.Write(hash);
                        writer.Write(data.XParameterHash);
                        writer.Write(data.YParameterHash);
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                        writer.Write(data.ParameterWrap);
                        writer.Write(data.SyncDurations);
                    }
                    break;
                case AnimationBlendNodeType.ADDITIVE_BLEND_NODE:
                    {
                        AdditiveBlendNode data = (AdditiveBlendNode)node;
                        writer.Write(data.BaseNodeHash);
                        writer.Write(data.AdditiveNodeHash);
                        writer.Write(data.AdditiveNodeWeight);
                        writer.Write(data.SyncAdditiveDurationToBase);
                    }
                    break;
                case AnimationBlendNodeType.PARAMETRIC_ADDITIVE_BLEND_NODE:
                    {
                        ParametricAdditiveBlendNode data = (ParametricAdditiveBlendNode)node;
                        writer.Write(data.BaseNodeHash);
                        writer.Write(data.AdditiveNodeHash);
                        writer.Write(data.AdditiveNodeWeight);
                        writer.Write(data.ParameterHash);
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                        writer.Write(data.SyncAdditiveDurationToBase);
                    }
                    break;
                case AnimationBlendNodeType.RANGED_SELECTOR_NODE:
                    {
                        RangedSelectorNode data = (RangedSelectorNode)node;
                        writer.Write(data.NumChildren);
                        foreach (var hash in data.HashBindings)
                            writer.Write(hash);
                        foreach (var value in data.MinValueBindings)
                            writer.Write(value);
                        foreach (var value in data.MaxValueBindings)
                            writer.Write(value);
                        foreach (var footSync in data.FootSyncOnSelect)
                            writer.Write(footSync);
                        writer.Write(data.BindingParameterHash);
                        writer.Write(data.EaseTime);
                        writer.Write(data.ResetPlaybackOnChange);
                    }
                    break;
                case AnimationBlendNodeType.ENUMERATED_SELECTOR_NODE:
                    {
                        EnumeratedSelectorNode data = (EnumeratedSelectorNode)node;
                        writer.Write(data.NumChildren);
                        foreach (var hash in data.HashBindings)
                            writer.Write(hash);
                        foreach (var value in data.ValueBindings)
                            writer.Write(value);
                        foreach (var footSync in data.FootSyncOnSelect)
                            writer.Write(footSync);
                        writer.Write(data.BindingParameterHash);
                        writer.Write(data.EaseTime);
                        writer.Write(data.ResetPlaybackOnChange);
                    }
                    break;
                case AnimationBlendNodeType.FOOT_SYNC_SELECTOR_NODE:
                    {
                        FootSyncSelectorNode data = (FootSyncSelectorNode)node;
                        foreach (var hash in data.HashBindings)
                            writer.Write(hash);
                        writer.Write(data.FootStrikeSelectionMethod);
                        writer.Write(data.GaitSyncTargetOnSelect);
                    }
                    break;
                case AnimationBlendNodeType.BONE_MASK_NODE:
                    {
                        BoneMaskNode data = (BoneMaskNode)node;
                        writer.Write(data.MaskingControl);
                        writer.Write(data.MaskPreceding);
                        writer.Write(data.MaskFollowing);
                        writer.Write(data.MaskSelf);
                    }
                    break;
                case AnimationBlendNodeType.IK_NODE:
                    {
                        IkNode data = (IkNode)node;
                        writer.Write(data.PoseLayer);
                        writer.Write(data.IkEffectorHash);
                        writer.Write(data.IkType);
                        writer.Write(data.IkTarget);
                        writer.Write(data.EffectorFullyEffectiveRadius);
                        writer.Write(data.EffectorLeastEffectiveRadius);
                        writer.Write(data.FalloffRate);
                        writer.Write(data.EnforceTranslation);
                        writer.Write(data.EnforceEndBoneRotation);
                    }
                    break;
                case AnimationBlendNodeType.SPHERICAL_NODE:
                    {
                        SphericalNode data = (SphericalNode)node;
                        writer.Write(data.NumChildren);
                        writer.Write(data.CoordHash);
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
                case AnimationBlendNodeType.WEIGHTED_NODE:
                    {
                        WeightedNode data = (WeightedNode)node;
                        writer.Write(data.ParameterHash);
                        writer.Write(data.ParameterMin);
                        writer.Write(data.ParameterMax);
                    }
                    break;
                case AnimationBlendNodeType.RANDOMISED_LEAF_NODE:
                    {
                        RandomisedLeafNode data = (RandomisedLeafNode)node;
                        writer.Write(data.HasCallback);
                        writer.Write(data.BlendTime);
                        writer.Write(data.OptionalContextParam);
                        writer.Write(data.OptionalConvergeVector);
                        writer.Write(data.OptionalConvergeFloat);
                        writer.Write(data.ConvergeOrientation);
                        writer.Write(data.ConvergeTranslation);
                        writer.Write(data.HashedCallbackName);
                        writer.Write(data.RandomNodeCallbackName);
                        writer.Write(data.Looped);
                        writer.Write(data.NewSelectionOnLoop);
                        writer.Write(data.NumberOfAnimSlots);
                        foreach (var mirrored in data.Mirrored)
                            writer.Write(mirrored);
                        foreach (var index in data.LevelAnimIndices)
                            writer.Write(index);
                        foreach (var name in data.HashedNames)
                            writer.Write(name);
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
