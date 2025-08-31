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

        public List<string> BindingNames = new List<string>();
        public List<uint> ParameterTypes = new List<uint>();
        public List<uint> IndicesInTypeArrays = new List<uint>();

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
    }
}