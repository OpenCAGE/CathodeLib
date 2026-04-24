using System.IO;
using CathodeLib;
using System.Collections.Generic;
using CATHODE.Scripting;
using System;
using static CATHODE.EnvironmentAnimations;
using CathodeLib.ObjectExtensions;
using static CATHODE.CollisionMaps;
using System.Linq;





#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif 

namespace CATHODE
{
    //This file defines additional info for entities with DYNAMIC_PHYSICS_SYSTEM resources.

    /// <summary>
    /// DATA/ENV/x/WORLD/PHYSICS.MAP
    /// </summary>
    public class PhysicsMaps : CathodeFile
    {
        public List<DYNAMIC_PHYSICS_SYSTEM> Entries = new List<DYNAMIC_PHYSICS_SYSTEM>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public PhysicsMaps(string path) : base(path) { }
        public PhysicsMaps(MemoryStream stream, string path = "") : base(stream, path) { }
        public PhysicsMaps(byte[] data, string path = "") : base(data, path) { }

        private List<DYNAMIC_PHYSICS_SYSTEM> _writeList = new List<DYNAMIC_PHYSICS_SYSTEM>();

        ~PhysicsMaps()
        {
            Entries.Clear();
            _writeList.Clear();
        }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position = 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    DYNAMIC_PHYSICS_SYSTEM entry = new DYNAMIC_PHYSICS_SYSTEM();
                    entry.physics_system_index = reader.ReadInt32();
                    reader.BaseStream.Position += 8;
                    entry.composite_instance_id = Utilities.Consume<ShortGuid>(reader); 
                    entry.entity = Utilities.Consume<EntityHandle>(reader);

                    Vector4 Row0 = Utilities.Consume<Vector4>(reader);
                    Vector4 Row1 = Utilities.Consume<Vector4>(reader);
                    Vector4 Row2 = Utilities.Consume<Vector4>(reader);
                    double[,] matrix = new double[,]
                    {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                        {Row0.x, Row0.y, Row0.z, Row0.w},
                        {Row1.x, Row1.y, Row1.z, Row1.w},
                        {Row2.x, Row2.y, Row2.z, Row2.w},
#else
                        {Row0.X, Row0.Y, Row0.Z, Row0.W},
                        {Row1.X, Row1.Y, Row1.Z, Row1.W},
                        {Row2.X, Row2.Y, Row2.Z, Row2.W},
#endif
                    };

                    entry.Position = new Vector3(
                        (float)matrix[0, 3],
                        (float)matrix[1, 3],
                        (float)matrix[2, 3]
                    );
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    Matrix4x4 matrix4x4 = new Matrix4x4(
                        new Vector4((float)matrix[0, 0], (float)matrix[0, 1], (float)matrix[0, 2], 0),
                        new Vector4((float)matrix[1, 0], (float)matrix[1, 1], (float)matrix[1, 2], 0),
                        new Vector4((float)matrix[2, 0], (float)matrix[2, 1], (float)matrix[2, 2], 0),
                        new Vector4(0, 0, 0, 1)
                    );
                    entry.Rotation = Quaternion.LookRotation(matrix4x4.GetColumn(2), matrix4x4.GetColumn(1));
#else
                    entry.Rotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                        (float)matrix[0, 0], (float)matrix[0, 1], (float)matrix[0, 2], 0,
                        (float)matrix[1, 0], (float)matrix[1, 1], (float)matrix[1, 2], 0,
                        (float)matrix[2, 0], (float)matrix[2, 1], (float)matrix[2, 2], 0,
                        0, 0, 0, 1
                    ));
#endif

                    reader.BaseStream.Position += 8;
                    Entries.Add(entry);
                }
            }
            _writeList.AddRange(Entries);
            return true;
        }

        override protected bool SaveInternal()
        {
            //Entries = Entries.OrderBy(o => o.physics_system_index).ToList();

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(Entries.Count * 80);
                writer.Write(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    writer.Write(Entries[i].physics_system_index);
                    writer.Write(new byte[4]);
                    Utilities.Write(writer, ShortGuids.DYNAMIC_PHYSICS_SYSTEM);
                    Utilities.Write(writer, Entries[i].composite_instance_id);
                    Utilities.Write(writer, Entries[i].entity);

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                    Matrix4x4 rotationMatrix4x4 = Matrix4x4.Rotate(Entries[i].Rotation);
                    Vector4 Row0 = new Vector4(rotationMatrix4x4.m11, rotationMatrix4x4.m12, rotationMatrix4x4.m13, Entries[i].Position.x);
                    Vector4 Row1 = new Vector4(rotationMatrix4x4.m21, rotationMatrix4x4.m22, rotationMatrix4x4.m23, Entries[i].Position.y);
                    Vector4 Row2 = new Vector4(rotationMatrix4x4.m31, rotationMatrix4x4.m32, rotationMatrix4x4.m33, Entries[i].Position.z);
#else
                    Matrix4x4 rotationMatrix4x4 = Matrix4x4.CreateFromQuaternion(Entries[i].Rotation);
                    Vector4 Row0 = new Vector4(rotationMatrix4x4.M11, rotationMatrix4x4.M12, rotationMatrix4x4.M13, Entries[i].Position.X);
                    Vector4 Row1 = new Vector4(rotationMatrix4x4.M21, rotationMatrix4x4.M22, rotationMatrix4x4.M23, Entries[i].Position.Y);
                    Vector4 Row2 = new Vector4(rotationMatrix4x4.M31, rotationMatrix4x4.M32, rotationMatrix4x4.M33, Entries[i].Position.Z);
#endif

                    Utilities.Write<Vector4>(writer, Row0);
                    Utilities.Write<Vector4>(writer, Row1);
                    Utilities.Write<Vector4>(writer, Row2);
                    writer.Write(new byte[8]);
                }
            }
            _writeList.Clear();
            _writeList.AddRange(Entries);
            return true;
        }
        #endregion

        #region HELPERS
        /// <summary>
        /// Get the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public int GetWriteIndex(DYNAMIC_PHYSICS_SYSTEM envAnim)
        {
            if (!_writeList.Contains(envAnim)) return -1;
            return _writeList.IndexOf(envAnim);
        }

        /// <summary>
        /// Get the object at the write index (useful for cross-ref'ing with compiled binaries)
        /// Note: if the file hasn't been saved for a while, the write index may differ from the index on-disk
        /// </summary>
        public DYNAMIC_PHYSICS_SYSTEM GetAtWriteIndex(int index)
        {
            if (_writeList.Count <= index || index < 0) return null;
            return _writeList[index];
        }

        /// <summary>
        /// Copy an entry into the file, along with all child objects.
        /// </summary>
        public DYNAMIC_PHYSICS_SYSTEM ImportEntry(DYNAMIC_PHYSICS_SYSTEM physMap)
        {
            if (physMap == null)
                return null;

            var existing = Entries.FirstOrDefault(o => o == physMap);
            if (existing != null)
                return existing;

            DYNAMIC_PHYSICS_SYSTEM newPhysMap = physMap.Copy();
            Entries.Add(newPhysMap);
            return newPhysMap;
        }
        #endregion

        #region STRUCTURES
        public class DYNAMIC_PHYSICS_SYSTEM : IEquatable<DYNAMIC_PHYSICS_SYSTEM>, IComparable<DYNAMIC_PHYSICS_SYSTEM>
        {
            //Should match system_index on the PhysicsSystem entity.
            public int physics_system_index; // the proxy index for the system to clone

            //This is the instance ID for the composite containing the PhysicsSystem.
            //We do not need to worry about the entity ID for the PhysicsSystem as the resources are written to the composite that contains it.
            public ShortGuid composite_instance_id;

            //This is the entity ID and instance ID for the actual instanced composite entity (basically, a step down from the instance above).
            public EntityHandle entity;

            //This is the worldspace position of the composite instance
            public Vector3 Position;
            public Quaternion Rotation;

            public static bool operator ==(DYNAMIC_PHYSICS_SYSTEM x, DYNAMIC_PHYSICS_SYSTEM y)
            {
                if (ReferenceEquals(x, null)) return ReferenceEquals(y, null);
                if (ReferenceEquals(y, null)) return ReferenceEquals(x, null);
                if (x.physics_system_index != y.physics_system_index) return false;
                if (x.composite_instance_id != y.composite_instance_id) return false;
                if (x.entity != y.entity) return false;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                if (x.Position != y.Position) return false;
                if (x.Rotation != y.Rotation) return false;
#else
                if (x.Position.X != y.Position.X || x.Position.Y != y.Position.Y || x.Position.Z != y.Position.Z) return false;
                if (x.Rotation.X != y.Rotation.X || x.Rotation.Y != y.Rotation.Y || x.Rotation.Z != y.Rotation.Z || x.Rotation.W != y.Rotation.W) return false;
#endif
                return true;
            }

            public static bool operator !=(DYNAMIC_PHYSICS_SYSTEM x, DYNAMIC_PHYSICS_SYSTEM y)
            {
                return !(x == y);
            }

            public bool Equals(DYNAMIC_PHYSICS_SYSTEM other)
            {
                return this == other;
            }

            public override bool Equals(object obj)
            {
                return obj is DYNAMIC_PHYSICS_SYSTEM entry && this == entry;
            }

            public override int GetHashCode()
            {
                int hashCode = -1234567890;
                hashCode = hashCode * -1521134295 + physics_system_index.GetHashCode();
                hashCode = hashCode * -1521134295 + composite_instance_id.GetHashCode();
                hashCode = hashCode * -1521134295 + EqualityComparer<EntityHandle>.Default.GetHashCode(entity);
                hashCode = hashCode * -1521134295 + Position.GetHashCode();
                hashCode = hashCode * -1521134295 + Rotation.GetHashCode();
                return hashCode;
            }

            public int CompareTo(DYNAMIC_PHYSICS_SYSTEM other)
            {
                if (other == null) return 1;

                int comparison = physics_system_index.CompareTo(other.physics_system_index);
                if (comparison != 0) return comparison;
                comparison = composite_instance_id.CompareTo(other.composite_instance_id);
                if (comparison != 0) return comparison;
                if (entity == null && other.entity == null)
                {

                }
                else if (entity == null)
                    return -1;
                else if (other.entity == null)
                    return 1;
                else
                {
                    comparison = entity.entity_id.CompareTo(other.entity.entity_id);
                    if (comparison != 0) return comparison;

                    comparison = entity.composite_instance_id.CompareTo(other.entity.composite_instance_id);
                    if (comparison != 0) return comparison;
                }
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
                comparison = Position.x.CompareTo(other.Position.x);
                if (comparison != 0) return comparison;
                comparison = Position.y.CompareTo(other.Position.y);
                if (comparison != 0) return comparison;
                comparison = Position.z.CompareTo(other.Position.z);
                if (comparison != 0) return comparison;
                comparison = Rotation.x.CompareTo(other.Rotation.x);
                if (comparison != 0) return comparison;
                comparison = Rotation.y.CompareTo(other.Rotation.y);
                if (comparison != 0) return comparison;
                comparison = Rotation.z.CompareTo(other.Rotation.z);
                if (comparison != 0) return comparison;
                comparison = Rotation.w.CompareTo(other.Rotation.w);
#else
                comparison = Position.X.CompareTo(other.Position.X);
                if (comparison != 0) return comparison;
                comparison = Position.Y.CompareTo(other.Position.Y);
                if (comparison != 0) return comparison;
                comparison = Position.Z.CompareTo(other.Position.Z);
                if (comparison != 0) return comparison;
                comparison = Rotation.X.CompareTo(other.Rotation.X);
                if (comparison != 0) return comparison;
                comparison = Rotation.Y.CompareTo(other.Rotation.Y);
                if (comparison != 0) return comparison;
                comparison = Rotation.Z.CompareTo(other.Rotation.Z);
                if (comparison != 0) return comparison;
                comparison = Rotation.W.CompareTo(other.Rotation.W);
#endif
                return comparison;
            }
        };
#endregion
    }
}