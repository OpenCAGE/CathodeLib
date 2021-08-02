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
        public int offset;
        public CathodeDataType dataType;

        public byte[] unknownContent; //This contains any byte data not yet understood for children types
    }
    public class CathodeTransform : CathodeParameter
    {
        public Vector3 position = new Vector3();
        public Vector3 rotation = new Vector3();
    }
    public class CathodeInteger : CathodeParameter
    {
        public int value = 0;
    }
    public class CathodeString : CathodeParameter
    {
        public cGUID guid;
        public string value = "";
    }
    public class CathodeBool : CathodeParameter
    {
        public bool value = false;
    }
    public class CathodeFloat : CathodeParameter
    {
        public float value = 0.0f;
    }
    public class CathodeResource : CathodeParameter
    {
        public cGUID resourceID;
    }
    public class CathodeVector3 : CathodeParameter
    {
        public Vector3 value = new Vector3();
    }
    public class CathodeEnum : CathodeParameter
    {
        public cGUID enumID;
        public int enumIndex = 0;
    }
    public class CathodeSpline : CathodeParameter
    {
        public List<CathodeTransform> splinePoints = new List<CathodeTransform>();
    }
}
