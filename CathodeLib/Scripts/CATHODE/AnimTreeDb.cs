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
                        SetName = _strings.Entries[reader.ReadUInt32()],
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
                        treeDef.BindingNames.Add(_strings.GetString(reader.ReadUInt32()));
                    for (uint x = 0; x < NumberOfBindings; x++)
                        treeDef.ParameterTypes.Add((AnimTreeParameterType)reader.ReadUInt32());
                    for (uint x = 0; x < NumberOfBindings; x++)
                        treeDef.IndicesInTypeArrays.Add(reader.ReadUInt32());

                    for (uint x = 0; x < NumberOfCallbacks; x++)
                        treeDef.CallbackNames.Add(_strings.GetString(reader.ReadUInt32()));

                    for (uint x = 0; x < NumberOfMetadataListeners; x++)
                        treeDef.MetadataListenerNames.Add(_strings.GetString(reader.ReadUInt32()));
                    for (uint x = 0; x < NumberOfMetadataListeners; x++)
                        treeDef.MetadataEventNames.Add(_strings.GetString(reader.ReadUInt32()));
                    for (uint x = 0; x < NumberOfMetadataListeners; x++)
                        treeDef.MetadataListenerWeightThresholds.Add(reader.ReadSingle());
                    for (uint x = 0; x < NumberOfMetadataListeners; x++)
                        treeDef.MetadataListenerFilterTimes.Add(reader.ReadSingle());

                    uint NumberOfAutoFloatBindings = reader.ReadUInt32();
                    for (uint x = 0; x < NumberOfAutoFloatBindings; x++)
                        treeDef.AutoFloatNames.Add(_strings.GetString(reader.ReadUInt32()));

                    for (uint x = 0; x < NumberOfPropertyListeners; x++)
                        treeDef.PropertyListenerNames.Add(_strings.GetString(reader.ReadUInt32()));
                    for (uint x = 0; x < NumberOfPropertyListeners; x++)
                        treeDef.PropertyListenerPropertyNames.Add(_strings.GetString(reader.ReadUInt32()));
                    for (uint x = 0; x < NumberOfPropertyListeners; x++)
                        treeDef.PropertyListenerLeafNames.Add(_strings.GetString(reader.ReadUInt32()));

                    for (uint x = 0; x < NumberOfPropertyValues; x++)
                        treeDef.PropertyValueNames.Add(_strings.GetString(reader.ReadUInt32()));
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
                        treeDef.FloatInterpolatorSourceNames.Add(_strings.GetString(reader.ReadUInt32()));
                    for (uint x = 0; x < NumberOfFloatInterpolatorBindings; x++)
                        treeDef.FloatInterpolatorNames.Add(_strings.GetString(reader.ReadUInt32()));
                    for (uint x = 0; x < NumberOfFloatInterpolatorBindings; x++)
                        treeDef.FloatInterpolatorStartValues.Add(reader.ReadSingle());
                    for (uint x = 0; x < NumberOfFloatInterpolatorBindings; x++)
                        treeDef.FloatInterpolatorRates.Add(reader.ReadSingle());

                    for (int x = 0; x < numChildren; x++)
                    {
                        var node = ReadNode(reader);
                        treeDef.Children.Add(node);
                    }

                    Entries.Add(treeDef);
                }
            }
            return true;
        }

        private AnimationNode ReadNode(BinaryReader reader)
        {
            AnimationNodeType nodeType = (AnimationNodeType)reader.ReadUInt32();
            string NodeName = _strings.GetString(reader.ReadUInt32());
            uint numChildren = reader.ReadUInt32();

            AnimationNode node;
            switch (nodeType)
            {
                case AnimationNodeType.ANIM_Animation:
                    {
                        node = new LeafNode
                        {
                            HasCallback = reader.ReadBoolean(),
                            CallbackName = _strings.GetString(reader.ReadUInt32()),
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
                case AnimationNodeType.ANIM_Selector:
                    {
                        uint childCount = reader.ReadUInt32();

                        List<string> bindings = new List<string>();
                        for (uint i = 0; i < childCount; i++)
                            bindings.Add(_strings.GetString(reader.ReadUInt32()));
                        List<uint> valueBindings = new List<uint>();
                        for (uint i = 0; i < childCount; i++)
                            valueBindings.Add(reader.ReadUInt32());
                        List<bool> footSyncOnSelect = new List<bool>();
                        for (uint i = 0; i < childCount; i++)
                            footSyncOnSelect.Add(reader.ReadBoolean());

                        node = new SelectorNode
                        {
                            Bindings = bindings,
                            ValueBindings = valueBindings,
                            FootSyncOnSelect = footSyncOnSelect,
                            BindingParameterName = _strings.GetString(reader.ReadUInt32()),
                            EaseTime = reader.ReadSingle(),
                            ResetPlaybackOnChange = reader.ReadBoolean()
                        };
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
                            BindingParameterName = _strings.GetString(reader.ReadUInt32()),
                            Min = reader.ReadSingle(),
                            Max = reader.ReadSingle(),
                            ParameterUsage = reader.ReadUInt32(),
                            AutoBlendProperty = reader.ReadUInt32(),
                            SyncDurations = reader.ReadBoolean(),
                            UseAutoDerivedBlendValues = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_2DParametric:
                    {
                        uint BlendSet = reader.ReadUInt32();
                        uint XParameter = reader.ReadUInt32();
                        uint YParameter = reader.ReadUInt32();
                        uint ZParameter = reader.ReadUInt32();
                        node = new Parametric3DNode
                        {
                            BlendSet = new uint[1] { BlendSet },
                            XParameter = _strings.GetString(XParameter),
                            YParameter = _strings.GetString(YParameter),
                            ZParameter = _strings.GetString(ZParameter),
                            OverflowListener = _strings.GetString(reader.ReadUInt32()),
                            LoopBlendSet = reader.ReadBoolean(),
                            SyncBlendSet = reader.ReadBoolean()
                        };
                        if (YParameter == 0)
                        {
                            Parametric1DNode node1D = (Parametric1DNode)node;
                            node1D.Type = AnimationNodeType.ANIM_1DParametric;
                            node = node1D;
                        }
                        else if (ZParameter == 0)
                        {
                            Parametric2DNode node2D = (Parametric2DNode)node;
                            node2D.Type = AnimationNodeType.ANIM_2DParametric;
                            node = node2D;
                        }
                    }
                    break;
                case AnimationNodeType.ANIM_4DParametric:
                    {
                        var blendSet = new uint[2];
                        for (int i = 0; i < 2; i++)
                            blendSet[i] = reader.ReadUInt32();

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
                            BaseNodeName = _strings.GetString(reader.ReadUInt32()),
                            AdditiveNodeName = _strings.GetString(reader.ReadUInt32()),
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
                        node = new IkNode
                        {
                            PoseLayer = reader.ReadUInt32(),
                            IkEffectorName = _strings.GetString(reader.ReadUInt32()),
                            IkType = reader.ReadUInt32(),
                            IkTarget = reader.ReadUInt32(),
                            EffectorFullyEffectiveRadius = reader.ReadSingle(),
                            EffectorLeastEffectiveRadius = reader.ReadSingle(),
                            FalloffRate = reader.ReadUInt32(),
                            EnforceTranslation = reader.ReadByte() == 1,
                            EnforceEndBoneRotation = reader.ReadByte() == 1
                        };
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
                        List<uint> levelAnimIndices = new List<uint>();
                        for (uint i = 0; i < numberOfAnimSlots; i++)
                            levelAnimIndices.Add(reader.ReadUInt32());
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
                            LevelAnimIndices = levelAnimIndices,
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
                case AnimationNodeType.ANIM_Enumerated_Selector:
                    {
                        uint childCount = reader.ReadUInt32();

                        List<string> bindings = new List<string>();
                        for (uint i = 0; i < childCount; i++)
                            bindings.Add(_strings.GetString(reader.ReadUInt32()));
                        List<uint> valueBindings = new List<uint>();
                        for (uint i = 0; i < childCount; i++)
                            valueBindings.Add(reader.ReadUInt32());
                        List<bool> footSyncOnSelect = new List<bool>();
                        for (uint i = 0; i < childCount; i++)
                            footSyncOnSelect.Add(reader.ReadBoolean());

                        node = new EnumeratedSelectorNode
                        {
                            Bindings = bindings,
                            ValueBindings = valueBindings,
                            FootSyncOnSelect = footSyncOnSelect,
                            BindingParameterName = _strings.GetString(reader.ReadUInt32()),
                            EaseTime = reader.ReadSingle(),
                            ResetPlaybackOnChange = reader.ReadBoolean()
                        };
                    }
                    break;
                case AnimationNodeType.ANIM_Parametric_Additive_Blend:
                    {
                        node = new ParametricAdditiveBlendNode
                        {
                            BaseNodeName = _strings.GetString(reader.ReadUInt32()),
                            AdditiveNodeName = _strings.GetString(reader.ReadUInt32()),
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
                        node = new WeightedNode
                        {
                            ParameterName = _strings.GetString(reader.ReadUInt32()),
                            ParameterMin = reader.ReadSingle(),
                            ParameterMax = reader.ReadSingle()
                        };
                    }
                    break;
                default:
                    throw new Exception("unknown node type");
            }
            node.Name = NodeName;

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
                    writer.Write(_strings.GetID(tree.SetName));
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

                    writer.Write(tree.BindingNames.Count);
                    writer.Write(new byte[20]);
                    writer.Write(tree.CallbackNames.Count);
                    writer.Write(tree.MetadataListenerNames.Count);
                    writer.Write(tree.PropertyListenerNames.Count);
                    writer.Write(tree.PropertyValueNames.Count);

                    foreach (var name in tree.BindingNames)
                        writer.Write(_strings.GetID(name));
                    foreach (var type in tree.ParameterTypes)
                        writer.Write((uint)type);
                    foreach (var index in tree.IndicesInTypeArrays)
                        writer.Write(index);

                    foreach (var name in tree.CallbackNames)
                        writer.Write(_strings.GetID(name));

                    foreach (var name in tree.MetadataListenerNames)
                        writer.Write(_strings.GetID(name));
                    foreach (var name in tree.MetadataEventNames)
                        writer.Write(_strings.GetID(name));
                    foreach (var threshold in tree.MetadataListenerWeightThresholds)
                        writer.Write(threshold);
                    foreach (var time in tree.MetadataListenerFilterTimes)
                        writer.Write(time);

                    writer.Write(tree.AutoFloatNames.Count);
                    foreach (var name in tree.AutoFloatNames)
                        writer.Write(_strings.GetID(name));

                    foreach (var name in tree.PropertyListenerNames)
                        writer.Write(_strings.GetID(name));
                    foreach (var name in tree.PropertyListenerPropertyNames)
                        writer.Write(_strings.GetID(name));
                    foreach (var name in tree.PropertyListenerLeafNames)
                        writer.Write(_strings.GetID(name));

                    foreach (var name in tree.PropertyValueNames)
                        writer.Write(_strings.GetID(name));
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
                        writer.Write(_strings.GetID(name));
                    foreach (var name in tree.FloatInterpolatorNames)
                        writer.Write(_strings.GetID(name));
                    foreach (var value in tree.FloatInterpolatorStartValues)
                        writer.Write(value);
                    foreach (var rate in tree.FloatInterpolatorRates)
                        writer.Write(rate);

                    foreach (var node in tree.Children)
                        WriteNode(writer, node);
                }
            }
            return true;
        }

        private void WriteNode(BinaryWriter writer, AnimationNode node)
        {
            AnimationNodeType writeType = node.Type == AnimationNodeType.ANIM_1DParametric || node.Type == AnimationNodeType.ANIM_3DParametric ? AnimationNodeType.ANIM_2DParametric : node.Type;
            writer.Write((uint)writeType);
            writer.Write(_strings.GetID(node.Name));

            switch (node.Type)
            {
                case AnimationNodeType.ANIM_Animation:
                    {
                        LeafNode data = (LeafNode)node;
                        writer.Write(data.HasCallback);
                        writer.Write(_strings.GetID(data.CallbackName));
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
                case AnimationNodeType.ANIM_Selector:
                    {
                        SelectorNode data = (SelectorNode)node;
                        writer.Write(node.Children.Count);
                        foreach (var hash in data.Bindings)
                            writer.Write(_strings.GetID(hash));
                        foreach (var value in data.ValueBindings)
                            writer.Write(value);
                        foreach (var footSync in data.FootSyncOnSelect)
                            writer.Write(footSync);
                        writer.Write(_strings.GetID(data.BindingParameterName));
                        writer.Write(data.EaseTime);
                        writer.Write(data.ResetPlaybackOnChange);
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
                        writer.Write(_strings.GetID(data.BindingParameterName));
                        writer.Write(data.Min);
                        writer.Write(data.Max);
                        writer.Write(data.ParameterUsage);
                        writer.Write(data.AutoBlendProperty);
                        writer.Write(data.SyncDurations);
                        writer.Write(data.UseAutoDerivedBlendValues);
                    }
                    break;
                case AnimationNodeType.ANIM_1DParametric:
                case AnimationNodeType.ANIM_2DParametric:
                case AnimationNodeType.ANIM_3DParametric:
                    {
                        Parametric1DNode data = (Parametric1DNode)node;
                        writer.Write(data.BlendSet[0]);
                        writer.Write(_strings.GetID(data.XParameter));
                        writer.Write(node.Type == AnimationNodeType.ANIM_2DParametric ? _strings.GetID(((Parametric2DNode)node).YParameter) : 0);
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
                            writer.Write(blendSet);
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
                        writer.Write(_strings.GetID(data.BaseNodeName));
                        writer.Write(_strings.GetID(data.AdditiveNodeName));
                        writer.Write(data.AdditiveNodeWeight);
                        writer.Write(data.SyncAdditiveDurationToBase);
                    }
                    break;
                case AnimationNodeType.ANIM_Parametric_Additive_Blend:
                    {
                        ParametricAdditiveBlendNode data = (ParametricAdditiveBlendNode)node;
                        writer.Write(_strings.GetID(data.BaseNodeName));
                        writer.Write(_strings.GetID(data.AdditiveNodeName));
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
                case AnimationNodeType.ANIM_Enumerated_Selector:
                    {
                        EnumeratedSelectorNode data = (EnumeratedSelectorNode)node;
                        writer.Write(node.Children.Count);
                        foreach (var hash in data.Bindings)
                            writer.Write(_strings.GetID(hash));
                        foreach (var value in data.ValueBindings)
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
                        writer.Write(_strings.GetID(data.IkEffectorName));
                        writer.Write(data.IkType);
                        writer.Write(data.IkTarget);
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
                        writer.Write(_strings.GetID(data.ParameterName));
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
                        foreach (var index in data.LevelAnimIndices)
                            writer.Write(index);
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
