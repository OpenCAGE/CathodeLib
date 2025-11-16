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
    /// <summary>
    /// A reference to a game resource (E.G. a renderable element, a collision mapping, etc)
    /// </summary>
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
                    //index = 0;
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
            if (x.entityID != y.entityID) return false;

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
                   EqualityComparer<ShortGuid>.Default.Equals(entityID, reference.entityID);
        }

        public override int GetHashCode()
        {
            int hashCode = -1286985782;
            hashCode = hashCode * -1521134295 + position.GetHashCode();
            hashCode = hashCode * -1521134295 + rotation.GetHashCode();
            hashCode = hashCode * -1521134295 + resource_id.GetHashCode();
            hashCode = hashCode * -1521134295 + resource_type.GetHashCode();
            hashCode = hashCode * -1521134295 + entityID.GetHashCode();
            return hashCode;
        }

        public Vector3 position = new Vector3(0, 0, 0); // todo - i think these should be based on the entity info?
        public Vector3 rotation = new Vector3(0, 0, 0); // todo - i think these should be based on the entity info?

        public ShortGuid resource_id; //this can be translated to a string sometimes, like DYNAMIC_PHYSICS_SYSTEM
        public ResourceType resource_type;

        public int index = 0; // THIS IS TEMP! I'm struggling to look up the DynamicPhysicsSystem object, so just saving the old index for now. Need to look into that.
        public int count = 0;

        public EnvironmentAnimations.EnvironmentAnimation AnimatedModel = null; //ANIMATED_MODEL
        public CollisionMaps.COLLISION_MAPPING CollisionMapping = null; //COLLISION_MAPPING
        public PhysicsMaps.Entry DynamicPhysicsSystem = null; //DYNAMIC_PHYSICS_SYSTEM
        //EXCLUSIVE_MASTER_STATE_RESOURCE
        //NAV_MESH_BARRIER_RESOURCE
        public List<RenderableElements.Element> RenderableInstance = new List<RenderableElements.Element>(); //RENDERABLE_INSTANCE
        //TRAVERSAL_SEGMENT

        public ShortGuid entityID = new ShortGuid("FF-FF-FF-FF");
    }
}
