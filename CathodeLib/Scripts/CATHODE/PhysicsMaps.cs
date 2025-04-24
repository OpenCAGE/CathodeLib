using System.IO;
using CathodeLib;
using System.Collections.Generic;
using CATHODE.Scripting;
using System;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif 

namespace CATHODE
{
    //This file defines additional info for entities with DYNAMIC_PHYSICS_SYSTEM resources.

    /* DATA/ENV/PRODUCTION/x/WORLD/PHYSICS.MAP */
    public class PhysicsMaps : CathodeFile
    {
        public List<Entry> Entries = new List<Entry>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public PhysicsMaps(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position = 4;
                int entryCount = reader.ReadInt32();
                for (int i = 0; i < entryCount; i++)
                {
                    Entry entry = new Entry();
                    entry.physics_system_index = reader.ReadInt32();
                    reader.BaseStream.Position += 4;
                    entry.resource_type = Utilities.Consume<ShortGuid>(reader);

                    if (entry.resource_type != ShortGuidUtils.Generate("DYNAMIC_PHYSICS_SYSTEM"))
                        throw new Exception("Unexpected resource type! Expected DYNAMIC_PHYSICS_SYSTEM.");

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
                    Utilities.Write(writer, Entries[i].resource_type);
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
            return true;
        }
#endregion

        #region STRUCTURES
        public class Entry
        {
            //Should match system_index on the PhysicsSystem entity.
            public int physics_system_index; //TODO: is this the havok index? collision.map points to havok indexes, so would make sense

            //DYNAMIC_PHYSICS_SYSTEM
            public ShortGuid resource_type;

            //This is the instance ID for the composite containing the PhysicsSystem.
            //We do not need to worry about the entity ID for the PhysicsSystem as the resources are written to the composite that contains it.
            public ShortGuid composite_instance_id;

            //This is the entity ID and instance ID for the actual instanced composite entity (basically, a step down from the instance above).
            public EntityHandle entity;

            //This is the worldspace position of the composite instance
            public Vector3 Position;
            public Quaternion Rotation;
        };
        #endregion
    }
}