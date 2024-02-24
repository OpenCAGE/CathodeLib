using CathodeLib;
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
            resource_type = type;
        }

        public static bool operator ==(ResourceReference x, ResourceReference y)
        {
            if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
            if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);

            if (x.position != y.position) return false;
            if (x.rotation != y.rotation) return false;
            if (x.resource_id != y.resource_id) return false;
            if (x.resource_type != y.resource_type) return false;
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
                   EqualityComparer<ShortGuid>.Default.Equals(resource_id, reference.resource_id) &&
                   resource_type == reference.resource_type &&
                   index == reference.index &&
                   count == reference.count &&
                   EqualityComparer<ShortGuid>.Default.Equals(collisionID, reference.collisionID);
        }

        public override int GetHashCode()
        {
            int hashCode = -1286985782;
            hashCode = hashCode * -1521134295 + position.GetHashCode();
            hashCode = hashCode * -1521134295 + rotation.GetHashCode();
            hashCode = hashCode * -1521134295 + resource_id.GetHashCode();
            hashCode = hashCode * -1521134295 + resource_type.GetHashCode();
            hashCode = hashCode * -1521134295 + index.GetHashCode();
            hashCode = hashCode * -1521134295 + count.GetHashCode();
            hashCode = hashCode * -1521134295 + collisionID.GetHashCode();
            return hashCode;
        }

        public Vector3 position = new Vector3(0, 0, 0);
        public Vector3 rotation = new Vector3(0, 0, 0);

        public ShortGuid resource_id; //this can be translated to a string sometimes, like DYNAMIC_PHYSICS_SYSTEM
        public ResourceType resource_type;

        public int index = -1;
        public int count = 1;

        public ShortGuid collisionID = new ShortGuid("FF-FF-FF-FF");
    }
}
