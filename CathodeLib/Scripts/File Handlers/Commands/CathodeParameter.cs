using System;
using System.Collections.Generic;
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Commands
{
    /* Data types in the CATHODE scripting system */
    public enum CathodeDataType
    {
        POSITION,
        FLOAT,
        STRING,
        SPLINE_DATA,
        ENUM,
        SHORT_GUID,
        FILEPATH,
        BOOL,
        DIRECTION,
        INTEGER,

        OBJECT,
        NO_TYPE, //Translates to a blank string
        ZONE_LINK_PTR,
        ZONE_PTR,
        MARKER,
        CHARACTER,
        CAMERA
    }

    /* Resource reference types */
    public enum CathodeResourceReferenceType
    {
        RENDERABLE_INSTANCE,             //This one references an entry in the REnDerable elementS (REDS.BIN) database
        COLLISION_MAPPING,               //This one seems to be called in another script block that I'm not currently parsing 
        TRAVERSAL_SEGMENT,               //This just seems to be two -1 32-bit integers
        NAV_MESH_BARRIER_RESOURCE,       //This just seems to be two -1 32-bit integers (same as above)
        EXCLUSIVE_MASTER_STATE_RESOURCE, //This just seems to be two -1 32-bit integers (same as above)
        DYNAMIC_PHYSICS_SYSTEM,          //This is a count (usually small) and then a -1 32-bit int
        ANIMATED_MODEL,                  //This is a count (usually small) and then a -1 32-bit int (same as above)
    }

    /* A parameter compiled in COMMANDS.PAK */
    public class CathodeParameter
    {
        public CathodeParameter() { }
        public CathodeParameter(CathodeDataType type)
        {
            dataType = type;
        }

        public CathodeDataType dataType = CathodeDataType.NO_TYPE;
        public byte[] unknownContent; //This contains any byte data not yet understood for children types (TODO: remove this)
    }
    public class CathodeTransform : CathodeParameter
    {
        public CathodeTransform() { dataType = CathodeDataType.POSITION; }
        public Vector3 position = new Vector3();
        public Vector3 rotation = new Vector3();
    }
    public class CathodeInteger : CathodeParameter
    {
        public CathodeInteger() { dataType = CathodeDataType.INTEGER; }
        public int value = 0;
    }
    public class CathodeString : CathodeParameter
    {
        public CathodeString() { dataType = CathodeDataType.STRING; }
        public cGUID id; //cGUID generated from value
        public string value = "";
    }
    public class CathodeBool : CathodeParameter
    {
        public CathodeBool() { dataType = CathodeDataType.BOOL; }
        public bool value = false;
    }
    public class CathodeFloat : CathodeParameter
    {
        public CathodeFloat() { dataType = CathodeDataType.FLOAT; }
        public float value = 0.0f;
    }
    public class CathodeResource : CathodeParameter
    {
        public CathodeResource() { dataType = CathodeDataType.SHORT_GUID; }
        public cGUID resourceID;
    }
    public class CathodeVector3 : CathodeParameter
    {
        public CathodeVector3() { dataType = CathodeDataType.DIRECTION; }
        public Vector3 value = new Vector3();
    }
    public class CathodeEnum : CathodeParameter
    {
        public CathodeEnum() { dataType = CathodeDataType.ENUM; }
        public cGUID enumID;
        public int enumIndex = 0;
    }
    public class CathodeSpline : CathodeParameter
    {
        public CathodeSpline() { dataType = CathodeDataType.SPLINE_DATA; }
        public List<CathodeTransform> splinePoints = new List<CathodeTransform>();
    }
}
