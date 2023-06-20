﻿using CathodeLib;
using System;
using System.Collections.Generic;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Scripting
{
    /* A reference to a game resource (E.G. a renderable element, a collision mapping, etc) */
    [Serializable]
    public class ResourceReference : ICloneable
    {
        public ResourceReference()
        {

        }
        public ResourceReference(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                case ResourceType.RENDERABLE_INSTANCE:
                case ResourceType.ANIMATED_MODEL:
                    index = 0;
                    break;
            }
            entryType = type;
        }

        public static bool operator ==(ResourceReference x, ResourceReference y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);

            if (x.position != y.position) return false;
            if (x.rotation != y.rotation) return false;
            if (x.resourceID != y.resourceID) return false;
            if (x.entryType != y.entryType) return false;
            if (x.index != y.index) return false;
            if (x.count != y.count) return false;
            if (x.collisionID != y.collisionID) return false;

            return true;
        }
        public static bool operator !=(ResourceReference x, ResourceReference y)
        {
            return !(x == y);
        }

        public object Clone()
        {
            return this.MemberwiseClone();
        }

        public override bool Equals(object obj)
        {
            return obj is ResourceReference reference &&
                   EqualityComparer<Vector3>.Default.Equals(position, reference.position) &&
                   EqualityComparer<Vector3>.Default.Equals(rotation, reference.rotation) &&
                   EqualityComparer<ShortGuid>.Default.Equals(resourceID, reference.resourceID) &&
                   entryType == reference.entryType &&
                   index == reference.index &&
                   count == reference.count &&
                   EqualityComparer<ShortGuid>.Default.Equals(collisionID, reference.collisionID);
        }

        public override int GetHashCode()
        {
            int hashCode = -1286985782;
            hashCode = hashCode * -1521134295 + position.GetHashCode();
            hashCode = hashCode * -1521134295 + rotation.GetHashCode();
            hashCode = hashCode * -1521134295 + resourceID.GetHashCode();
            hashCode = hashCode * -1521134295 + entryType.GetHashCode();
            hashCode = hashCode * -1521134295 + index.GetHashCode();
            hashCode = hashCode * -1521134295 + count.GetHashCode();
            hashCode = hashCode * -1521134295 + collisionID.GetHashCode();
            return hashCode;
        }

        public Vector3 position = new Vector3(0, 0, 0);
        public Vector3 rotation = new Vector3(0, 0, 0);

        public ShortGuid resourceID; //TODO: we could deprecate this, and just write it knowing what we know with our object structure
        public ResourceType entryType;

        public int index = -1;
        public int count = 1;

        public ShortGuid collisionID = new ShortGuid("FF-FF-FF-FF");
    }
}
