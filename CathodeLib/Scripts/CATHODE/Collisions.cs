using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.EXPERIMENTAL
{
    /* DATA/ENV/PRODUCTION/x/WORLD/COLLISION.BIN */
    public class Collisions : CathodeFile
    {
        public List<alien_collision_bin_entry> Entries = new List<alien_collision_bin_entry>();
        public static new Implementation Implementation = Implementation.NONE;
        public Collisions(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                //remove these if exceptions dont throw
                byte[] magic = reader.ReadBytes(4);
                if (!magic.SequenceEqual(new byte[4] { 0x0C, 0xA0, 0xFE, 0xEF })) throw new Exception();
                int ver = reader.ReadInt32();
                if (ver != 2) throw new Exception();
                int zer = reader.ReadInt32();
                if (zer != 0) throw new Exception();

                int dataSize = reader.ReadInt32();
                int entryCount = reader.ReadInt32();
                int objectCount = reader.ReadInt32();
                int vertexCount = reader.ReadInt32();
                int indexCount = reader.ReadInt32();

                for (int i = 0; i < entryCount; i++)
                {
                    alien_collision_bin_entry entry = new alien_collision_bin_entry();
                    entry.VertexCount = reader.ReadInt32();
                    entry.ObjectCount = reader.ReadInt32();
                    entry.UnknownFloat = reader.ReadSingle();
                    entry.UnknownBoolean = reader.ReadInt32();
                    Entries.Add(entry);
                }

                List<alien_collision_bin_object> objects = new List<alien_collision_bin_object>();
                for (int i = 0; i < objectCount; i++)
                {
                    alien_collision_bin_object obj = new alien_collision_bin_object();
                    obj.Transform = Utilities.Consume<Matrix4x4>(reader);
                    obj.UnknownFloat = reader.ReadSingle();
                    obj.UnknownIndex0 = reader.ReadInt32();
                    obj.IndexCount = reader.ReadInt32();
                    obj.VertexCount = reader.ReadInt32();
                    objects.Add(obj);
                }

                List<alien_collision_bin_vertex> vertexes = new List<alien_collision_bin_vertex>();
                for (int i = 0; i < vertexCount; i++)
                {
                    alien_collision_bin_vertex vertex = new alien_collision_bin_vertex();
                    byte[] bytes = reader.ReadBytes(6); //vec3 s16
                    vertex.UnknownBytes = reader.ReadBytes(10);
                    vertexes.Add(vertex);
                }

                List<int> indicies = new List<int>();
                for (int i = 0; i < indexCount; i++)
                {
                    indicies.Add(reader.ReadInt16());
                }

                int verIndex = 0;
                int objIndex = 0;
                int indIndex = 0;
                for (int i = 0; i < entryCount; ++i)
                {
                    alien_collision_bin_entry Entry = Entries[i];

                    Entry.Vertices = vertexes.GetRange(verIndex, verIndex + Entry.VertexCount);
                    Entry.Objects = objects.GetRange(objIndex, objIndex + Entry.ObjectCount);

                    for (int x = 0; x < Entry.ObjectCount; ++x)
                    {
                        alien_collision_bin_object Entry1 = Entry.Objects[x];
                        //Assert(Entry1->UnknownFloat == 4);

                        Entry1.Indices = indicies.GetRange(indIndex, indIndex + Entry1.IndexCount);
                        Entry1.Vertices = vertexes.GetRange(verIndex, verIndex + Entry1.VertexCount);
                    }
                }

                //string dsfdsfds = "";

            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                writer.Write(new byte[4] { 0x0C, 0xA0, 0xFE, 0xEF });
                writer.Write(2);
                writer.Write(0);

            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class alien_collision_bin_object
        {
            public Matrix4x4 Transform; // NOTE: Transpose me!
            public float UnknownFloat;
            public int UnknownIndex0;
            public int IndexCount;
            public int VertexCount;
            public List<int> Indices = new List<int>();
            public List<alien_collision_bin_vertex> Vertices = new List<alien_collision_bin_vertex>();
        };
        public class alien_collision_bin_vertex
        {
            public Vector3 point; //int16[3]
            public byte[] UnknownBytes = new byte[10];
        };
        public class alien_collision_bin_entry
        {
            public int VertexCount;
            public int ObjectCount;
            public List<alien_collision_bin_vertex> Vertices = new List<alien_collision_bin_vertex>();
            public List<alien_collision_bin_object> Objects = new List<alien_collision_bin_object>();
            public float UnknownFloat;
            public int UnknownBoolean; // NOTE: Only saw either zero or one here so far.
        };
        #endregion
    }
}