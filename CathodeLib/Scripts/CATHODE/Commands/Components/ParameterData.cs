using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Scripting.Internal
{
    /* Data which can be used within a parameter */
    [Serializable]
    public class ParameterData : ICloneable
    {
        public ParameterData() { }
        public ParameterData(DataType type)
        {
            dataType = type;
        }
        public DataType dataType = DataType.NONE;

        public static bool operator ==(ParameterData x, ParameterData y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
            if (x.dataType != y.dataType) return false;
            switch (x.dataType)
            {
                case DataType.TRANSFORM:
                    cTransform x_t = (cTransform)x;
                    cTransform y_t = (cTransform)y;
                    return x_t.position == y_t.position && x_t.rotation == y_t.rotation;
                case DataType.INTEGER:
                    return ((cInteger)x).value == ((cInteger)y).value;
                case DataType.STRING:
                    return ((cString)x).value == ((cString)y).value;
                case DataType.BOOL:
                    return ((cBool)x).value == ((cBool)y).value;
                case DataType.FLOAT:
                    return ((cFloat)x).value == ((cFloat)y).value;
                case DataType.RESOURCE:
                    return ((cResource)x).shortGUID == ((cResource)y).shortGUID;
                case DataType.VECTOR:
                    return ((cVector3)x).value == ((cVector3)y).value;
                case DataType.ENUM:
                    return ((cEnum)x).enumIndex == ((cEnum)y).enumIndex && ((cEnum)x).enumID == ((cEnum)y).enumID;
                case DataType.ENUM_STRING:
                    return ((cEnumString)x).value == ((cEnumString)y).value && ((cEnumString)x).enumID == ((cEnumString)y).enumID;
                case DataType.SPLINE:
                    return ((cSpline)x).splinePoints == ((cSpline)y).splinePoints;
                case DataType.NONE:
                    return true;
                default:
                    return false;
            }
        }
        public static bool operator !=(ParameterData x, ParameterData y)
        {
            return !(x == y);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is ParameterData)) return false;
            return ((ParameterData)obj) == this;
        }

        public override int GetHashCode()
        {
            //this is gross
            switch (dataType)
            {
                case DataType.TRANSFORM:
                    cTransform x_t = (cTransform)this;
                    return x_t.position.GetHashCode() + x_t.rotation.GetHashCode();
                case DataType.INTEGER:
                    return ((cInteger)this).value.GetHashCode();
                case DataType.STRING:
                    return ((cString)this).value.GetHashCode();
                case DataType.BOOL:
                    return ((cBool)this).value.GetHashCode();
                case DataType.FLOAT:
                    return ((cFloat)this).value.GetHashCode();
                case DataType.RESOURCE:
                    return ((cResource)this).shortGUID.GetHashCode();
                case DataType.VECTOR:
                    return ((cVector3)this).value.GetHashCode();
                case DataType.ENUM:
                    return ((cEnum)this).enumID.ToByteString().GetHashCode() + ((cEnum)this).enumIndex;
                case DataType.ENUM_STRING:
                    return ((cEnumString)this).enumID.ToByteString().GetHashCode() + ((cEnumString)this).value.GetHashCode();
                case DataType.SPLINE:
                    cSpline x_sd = (cSpline)this;
                    int x_sd_i = 0;
                    for (int i = 0; i < x_sd.splinePoints.Count; i++) x_sd_i += x_sd.splinePoints[i].GetHashCode();
                    return x_sd_i;
                default:
                    return -1;
            }
        }

        public object Clone()
        {
            switch (dataType)
            {
                case DataType.RESOURCE:
                    cResource r = (cResource)this.MemberwiseClone();
                    r.value = ((cResource)this).value.Select(item => (ResourceReference)item.Clone()).ToList();
                    return r;
                case DataType.SPLINE:
                    cSpline s = (cSpline)this.MemberwiseClone();
                    s.splinePoints = ((cSpline)this).splinePoints.Select(item => (cTransform)item.Clone()).ToList();
                    return s;
                case DataType.VECTOR:
                    cVector3 v3 = (cVector3)this.MemberwiseClone();
                    Vector3 v3_v = (Vector3)((cVector3)this).value;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    v3.value = new Vector3(v3_v.x, v3_v.y, v3_v.z);
#else
                    v3.value = new Vector3(v3_v.X, v3_v.Y, v3_v.Z);
#endif
                    return v3;
                case DataType.TRANSFORM:
                    cTransform tr = (cTransform)this.MemberwiseClone();
                    Vector3 tr_p = (Vector3)((cTransform)this).position;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    tr.position = new Vector3(tr_p.x, tr_p.y, tr_p.z);
#else
                    tr.position = new Vector3(tr_p.X, tr_p.Y, tr_p.Z);
#endif
                    Vector3 tr_r = (Vector3)((cTransform)this).rotation;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    tr.rotation = new Vector3(tr_r.x, tr_r.y, tr_r.z);
#else
                    tr.rotation = new Vector3(tr_r.X, tr_r.Y, tr_r.Z);
#endif
                    return tr;
                default:
                    return this.MemberwiseClone();
            }
        }
    }
}

namespace CATHODE.Scripting
{ 
    [Serializable]
    public class cTransform : ParameterData
    {
        public cTransform() { dataType = DataType.TRANSFORM; }
        public cTransform(Vector3 position, Vector3 rotation)
        {
            this.position = position;
            this.rotation = rotation;
            dataType = DataType.TRANSFORM;
        }

        public Vector3 position = new Vector3();
        public Vector3 rotation = new Vector3(); //In CATHODE this is named Roll/Pitch/Yaw - but we handle it as euler X,Y,Z

        public static cTransform operator +(cTransform x, cTransform y)
        {
            if (x == null) return y;
            if (y == null) return x;


#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            return new cTransform(x.position + y.position, x.rotation + y.rotation);
#else
            return new cTransform(x.position + y.position, x.rotation.AddEulerAngles(y.rotation));
#endif
        }

        public override string ToString()
        {
            return position.ToString() + ", " + rotation.ToString();
        }
    }
    [Serializable]
    public class cInteger : ParameterData
    {
        public cInteger() { dataType = DataType.INTEGER; }
        public cInteger(int value)
        {
            this.value = value;
            dataType = DataType.INTEGER;
        }

        public int value = 0;

        public override string ToString()
        {
            return value.ToString();
        }
    }
    [Serializable]
    public class cString : ParameterData
    {
        public cString() { dataType = DataType.STRING; }
        public cString(string value)
        {
            this.value = value;
            dataType = DataType.STRING;
        }

        public string value = "";

        public override string ToString()
        {
            return value;
        }
    }
    [Serializable]
    public class cBool : ParameterData
    {
        public cBool() { dataType = DataType.BOOL; }
        public cBool(bool value)
        {
            this.value = value;
            dataType = DataType.BOOL;
        }

        public bool value = false;

        public override string ToString()
        {
            return value.ToString();
        }
    }
    [Serializable]
    public class cFloat : ParameterData
    {
        public cFloat() { dataType = DataType.FLOAT; }
        public cFloat(float value)
        {
            this.value = value;
            dataType = DataType.FLOAT;
        }

        public float value = 0.0f;

        public override string ToString()
        {
            return value.ToString();
        }
    }
    [Serializable]
    public class cResource : ParameterData
    {
        public cResource() 
        {
            this.shortGUID = ShortGuidUtils.GenerateRandom();
            dataType = DataType.RESOURCE;
        }
        public cResource(ResourceType type)
        {
            this.shortGUID = new ShortGuid((UInt32)type);
            dataType = DataType.RESOURCE;
        }
        public cResource(ShortGuid resourceID)
        {
            this.shortGUID = resourceID;
            dataType = DataType.RESOURCE;
        }
        public cResource(List<ResourceReference> value, ResourceType type)
        {
            this.value = value;
            this.shortGUID = new ShortGuid((UInt32)type);
            dataType = DataType.RESOURCE;
        }
        public cResource(List<ResourceReference> value, ShortGuid resourceID)
        {
            this.value = value;
            this.shortGUID = resourceID;
            dataType = DataType.RESOURCE;
        }

        public List<ResourceReference> value = new List<ResourceReference>();
        public ShortGuid shortGUID; 

        /* Add a new resource reference of type */
        public ResourceReference AddResource(ResourceType type)
        {
            //We can only have one type of resource reference, so if it already exists, we just return the existing one.
            ResourceReference rr = GetResource(type);
            if (rr == null)
            {
                rr = new ResourceReference(type);
                rr.resource_id = shortGUID;
                switch (rr.resource_type)
                {
                    case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                    case ResourceType.RENDERABLE_INSTANCE:
                    case ResourceType.ANIMATED_MODEL:
                        rr.index = 0;
                        break;
                }
                value.Add(rr);
            }
            return rr;
        }

        /* Find a resource reference of type */
        public ResourceReference GetResource(ResourceType type)
        {
            return value.FirstOrDefault(o => o.resource_type == type);
        }
    }
    [Serializable]
    public class cVector3 : ParameterData
    {
        public cVector3() { dataType = DataType.VECTOR; }
        public cVector3(Vector3 value)
        {
            this.value = value;
            dataType = DataType.VECTOR;
        }
        public cVector3(float x, float y, float z)
        {
            this.value = new Vector3(x,y,z);
            dataType = DataType.VECTOR;
        }

        public Vector3 value = new Vector3();

        public override string ToString()
        {
            return value.ToString();
        }
    }
    [Serializable]
    public class cEnum : ParameterData
    {
        public cEnum()
        {
            this.enumID = ShortGuid.Invalid;
            this.enumIndex = -1;
            dataType = DataType.ENUM;
        }
        public cEnum(ShortGuid enumID, int enumIndex = 0)
        {
            this.enumID = enumID;
            this.enumIndex = enumIndex;
            dataType = DataType.ENUM;
        }
        public cEnum(EnumType enumType, int enumIndex = 0) 
        {
            this.enumID = ShortGuidUtils.Generate(enumType.ToString());
            this.enumIndex = enumIndex;
            dataType = DataType.ENUM;
        }

        public ShortGuid enumID;
        public int enumIndex;

        public override string ToString()
        {
            return enumID.ToString() + " -> " + enumIndex;
        }
    }
    [Serializable]
    public class cEnumString : cString
    {
        public cEnumString()
        {
            this.enumID = ShortGuid.Invalid;
            dataType = DataType.ENUM_STRING;
        }
        public cEnumString(ShortGuid enumID, string enumString = "") 
        {
            this.enumID = enumID;
            this.value = enumString;
            dataType = DataType.ENUM_STRING;
        }
        public cEnumString(EnumStringType enumType, string enumString = "")
        {
            this.enumID = ShortGuidUtils.Generate(enumType.ToString());
            this.value = enumString;
            dataType = DataType.ENUM_STRING;
        }

        public ShortGuid enumID;

        public override string ToString()
        {
            return enumID.ToString() + " -> " + value;
        }
    }
    [Serializable]
    public class cSpline : ParameterData
    {
        public cSpline() { dataType = DataType.SPLINE; }
        public cSpline(List<cTransform> splinePoints)
        {
            this.splinePoints = splinePoints;
            dataType = DataType.SPLINE;
        }

        public List<cTransform> splinePoints = new List<cTransform>();
    }
}
