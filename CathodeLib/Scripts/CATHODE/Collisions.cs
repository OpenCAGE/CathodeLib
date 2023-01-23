using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
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

                }

            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);


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
            public List<Int16> Indices = new List<Int16>();
            public List<Int16> Vertices = new List<Int16>();
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