using CATHODE.Animations;
using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Animations
{
    public class AnimationTree
    {
        public string TreeName;
        public string TreeSetName;

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

        public List<AnimationNode> Nodes = new List<AnimationNode>();

        public List<uint> BindingHashedNames = new List<uint>();
        public List<uint> ParameterTypes = new List<uint>();
        public List<uint> IndicesInTypeArrays = new List<uint>();

        public List<uint> CallbackHashedNames = new List<uint>();

        public List<uint> MetadataListenerNames = new List<uint>();
        public List<uint> MetadataEventNames = new List<uint>();
        public List<float> MetadataListenerWeightThresholds = new List<float>();
        public List<float> MetadataListenerFilterTimes = new List<float>();

        public List<uint> AutoFloatNames = new List<uint>();

        public List<uint> PropertyListenerNames = new List<uint>();
        public List<uint> PropertyListenerPropertyNames = new List<uint>();
        public List<uint> PropertyListenerLeafNames = new List<uint>();

        public List<uint> PropertyValueNames = new List<uint>();
        public List<AnimationMetadataValue> PropertyValues = new List<AnimationMetadataValue>();

        public List<uint> FloatInterpolatorSourceNames = new List<uint>();
        public List<uint> FloatInterpolatorNames = new List<uint>();
        public List<float> FloatInterpolatorStartValues = new List<float>();
        public List<float> FloatInterpolatorRates = new List<float>();
    }
}