using CathodeLib;
using CathodeLib.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CATHODE.Scripting
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
                    return ((cResource)x).resourceID == ((cResource)y).resourceID;
                case DataType.VECTOR:
                    return ((cVector3)x).value == ((cVector3)y).value;
                case DataType.ENUM:
                    cEnum x_e = (cEnum)x;
                    cEnum y_e = (cEnum)y;
                    return x_e.enumIndex == y_e.enumIndex && x_e.enumID == y_e.enumID;
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
                    return Convert.ToInt32(
                        x_t.rotation.x.ToString() + x_t.rotation.y.ToString() + x_t.rotation.z.ToString() +
                        x_t.position.x.ToString() + x_t.position.y.ToString() + x_t.position.z.ToString());
                case DataType.INTEGER:
                    return ((cInteger)this).value;
                case DataType.STRING:
                    cString x_s = (cString)this;
                    string num = "";
                    for (int i = 0; i < x_s.value.Length; i++) num += ((int)x_s.value[i]).ToString();
                    return Convert.ToInt32(num);
                case DataType.BOOL:
                    return ((cBool)this).value ? 1 : 0;
                case DataType.FLOAT:
                    return Convert.ToInt32(((cFloat)this).value.ToString().Replace(".", ""));
                case DataType.RESOURCE:
                    string x_g_s = ((cString)this).value.ToString();
                    string num2 = "";
                    for (int i = 0; i < x_g_s.Length; i++) num2 += ((int)x_g_s[i]).ToString();
                    return Convert.ToInt32(num2);
                case DataType.VECTOR:
                    cVector3 x_v = (cVector3)this;
                    return Convert.ToInt32(x_v.value.x.ToString() + x_v.value.y.ToString() + x_v.value.z.ToString());
                case DataType.ENUM:
                    cEnum x_e = (cEnum)this;
                    string x_e_s = x_e.enumID.ToString();
                    string num3 = "";
                    for (int i = 0; i < x_e_s.Length; i++) num3 += ((int)x_e_s[i]).ToString();
                    return Convert.ToInt32(num3 + x_e.enumIndex.ToString());
                case DataType.SPLINE:
                    cSpline x_sd = (cSpline)this;
                    string x_sd_s = "";
                    for (int i = 0; i < x_sd.splinePoints.Count; i++) x_sd_s += x_sd.splinePoints[i].position.GetHashCode().ToString();
                    ShortGuid x_sd_g = ShortGuidUtils.Generate(x_sd_s);
                    string x_sd_g_s = x_sd_g.ToString();
                    string num4 = "";
                    for (int i = 0; i < x_sd_g_s.Length; i++) num4 += ((int)x_sd_g_s[i]).ToString();
                    return Convert.ToInt32(num4);
                default:
                    return -1;
            }
        }

        public object Clone()
        {
            switch (dataType)
            {
                case DataType.SPLINE:
                case DataType.RESOURCE:
                    return Utilities.CloneObject(this);
                //HOTFIX FOR VECTOR 3 CLONE ISSUE - TODO: FIND WHY THIS ISN'T WORKING WITH MEMBERWISE CLONE
                case DataType.VECTOR:
                    cVector3 v3 = (cVector3)this.MemberwiseClone();
                    v3.value = (Vector3)((cVector3)this).value.Clone();
                    return v3;
                case DataType.TRANSFORM:
                    cTransform tr = (cTransform)this.MemberwiseClone();
                    tr.position = (Vector3)((cTransform)this).position.Clone();
                    tr.rotation = (Vector3)((cTransform)this).rotation.Clone();
                    return tr;
                //END OF HOTFIX - SHOULD THIS ALSO APPLY TO OTHERS??
                default:
                    return this.MemberwiseClone();
            }
        }
    }
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
        public Vector3 rotation = new Vector3(); //In CATHODE this is named Roll/Pitch/Yaw
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
    }
    [Serializable]
    public class cResource : ParameterData
    {
        public cResource() { dataType = DataType.RESOURCE; }
        public cResource(ShortGuid resourceID)
        {
            this.resourceID = resourceID;
            dataType = DataType.RESOURCE;
        }
        public cResource(List<ResourceReference> value, ShortGuid resourceID)
        {
            this.value = value;
            this.resourceID = resourceID;
            dataType = DataType.RESOURCE;
        }

        public List<ResourceReference> value = new List<ResourceReference>();
        public ShortGuid resourceID; //TODO: this is only ever gonna be the parent of the resouce (node or entity) - should we just generate on compilation?

        /* Add a new resource reference of type */
        public ResourceReference AddResource(ResourceType type)
        {
            //We can only have one type of resource reference, so if it already exists, we just return the existing one.
            ResourceReference rr = GetResource(type);
            if (rr == null)
            {
                rr = new ResourceReference(type);
                rr.resourceID = resourceID;
                switch (rr.entryType)
                {
                    case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                    case ResourceType.RENDERABLE_INSTANCE:
                    case ResourceType.ANIMATED_MODEL:
                        rr.startIndex = 0;
                        break;
                }
                value.Add(rr);
            }
            return rr;
        }

        /* Find a resource reference of type */
        public ResourceReference GetResource(ResourceType type)
        {
            return value.FirstOrDefault(o => o.entryType == type);
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

        public Vector3 value = new Vector3();
    }
    [Serializable]
    public class cEnum : ParameterData
    {
        public cEnum(ShortGuid enumID, int enumIndex)
        {
            this.enumID = enumID;
            this.enumIndex = enumIndex;
            dataType = DataType.ENUM;
        }
        public cEnum(string enumName = "ALERTNESS_STATE", int enumIndex = 0)
        {
            this.enumID = ShortGuidUtils.Generate(enumName);
            this.enumIndex = enumIndex;
            dataType = DataType.ENUM;
        }

        public ShortGuid enumID;
        public int enumIndex = 0;
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
