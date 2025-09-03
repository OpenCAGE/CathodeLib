using CATHODE;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace CATHODE.Animations
{
    public abstract class AnimationMetadataValue
    {
        public MetadataValueType ValueType { get; protected set; }
        public bool RequiresConvert { get; set; }
        public bool CanMirror { get; set; }
        public bool CanModulateByPlayspeed { get; set; }
        public ushort Flags { get; set; }

        protected AnimationMetadataValue(MetadataValueType valueType)
        {
            ValueType = valueType;
            RequiresConvert = false;
            CanMirror = false;
            CanModulateByPlayspeed = false;
            Flags = 0;
        }
    }

    public class UIntMetadataValue : AnimationMetadataValue
    {
        public uint Value { get; set; }

        public UIntMetadataValue() : base(MetadataValueType.UINT32)
        {
            Value = 0;
        }
        public UIntMetadataValue(uint value) : base(MetadataValueType.UINT32)
        {
            Value = value;
        }
    }

    public class IntMetadataValue : AnimationMetadataValue
    {
        public int Value { get; set; }

        public IntMetadataValue() : base(MetadataValueType.INT32)
        {
            Value = 0;
        }
        public IntMetadataValue(int value) : base(MetadataValueType.INT32)
        {
            Value = value;
        }
    }

    public class FloatMetadataValue : AnimationMetadataValue
    {
        public float Value { get; set; }

        public FloatMetadataValue() : base(MetadataValueType.FLOAT32)
        {
            Value = 0.0f;
        }
        public FloatMetadataValue(float value) : base(MetadataValueType.FLOAT32)
        {
            Value = value;
        }
    }

    public class StringMetadataValue : AnimationMetadataValue
    {
        public string Value { get; set; }

        public StringMetadataValue() : base(MetadataValueType.STRING)
        {
            Value = string.Empty;
        }
        public StringMetadataValue(string value) : base(MetadataValueType.STRING)
        {
            Value = value ?? string.Empty;
        }
    }

    public class BoolMetadataValue : AnimationMetadataValue
    {
        public bool Value { get; set; }

        public BoolMetadataValue() : base(MetadataValueType.BOOL)
        {
            Value = false;
        }
        public BoolMetadataValue(bool value) : base(MetadataValueType.BOOL)
        {
            Value = value;
        }
    }

    public class VectorMetadataValue : AnimationMetadataValue
    {
        public Vector3 Value { get; set; }

        public VectorMetadataValue() : base(MetadataValueType.VECTOR)
        {
            Value = Vector3.Zero;
        }
        public VectorMetadataValue(Vector3 value) : base(MetadataValueType.VECTOR)
        {
            Value = value;
        }
    }

    public class ULongMetadataValue : AnimationMetadataValue
    {
        public ulong Value { get; set; }

        public ULongMetadataValue() : base(MetadataValueType.UINT64)
        {
            Value = 0;
        }
        public ULongMetadataValue(ulong value) : base(MetadataValueType.UINT64)
        {
            Value = value;
        }
    }

    public class LongMetadataValue : AnimationMetadataValue
    {
        public long Value { get; set; }

        public LongMetadataValue() : base(MetadataValueType.INT64)
        {
            Value = 0;
        }
        public LongMetadataValue(long value) : base(MetadataValueType.INT64)
        {
            Value = value;
        }
    }

    public class Float64MetadataValue : AnimationMetadataValue
    {
        public double Value { get; set; }

        public Float64MetadataValue() : base(MetadataValueType.FLOAT64)
        {
            Value = 0.0;
        }
        public Float64MetadataValue(double value) : base(MetadataValueType.FLOAT64)
        {
            Value = value;
        }
    }

    public class AudioMetadataValue : AnimationMetadataValue
    {
        public string Value { get; set; }

        public AudioMetadataValue() : base(MetadataValueType.AUDIO)
        {
            Value = string.Empty;
        }
        public AudioMetadataValue(string value) : base(MetadataValueType.AUDIO)
        {
            Value = value ?? string.Empty;
        }
    }

    public class PropertyReferenceMetadataValue : AnimationMetadataValue
    {
        public string Value { get; set; }

        public PropertyReferenceMetadataValue() : base(MetadataValueType.PROPERTY_REFERENCE)
        {
            Value = string.Empty;
        }
        public PropertyReferenceMetadataValue(string value) : base(MetadataValueType.PROPERTY_REFERENCE)
        {
            Value = value ?? string.Empty;
        }
    }

    public class ScriptInterfaceMetadataValue : AnimationMetadataValue
    {
        public string Value { get; set; }

        public ScriptInterfaceMetadataValue() : base(MetadataValueType.SCRIPT_INTERFACE)
        {
            Value = string.Empty;
        }
        public ScriptInterfaceMetadataValue(string value) : base(MetadataValueType.SCRIPT_INTERFACE)
        {
            Value = value ?? string.Empty;
        }
    }
}