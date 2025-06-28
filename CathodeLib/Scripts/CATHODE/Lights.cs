using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.EXPERIMENTAL
{
    /* DATA/ENV/PRODUCTION/x/WORLD/LIGHTS.BIN */
    public class Lights : CathodeFile
    {
        public List<int> Indexes = new List<int>();
        public List<Node> Values = new List<Node>();

        public DirectionalLight Sun = new DirectionalLight();

        public static new Implementation Implementation = Implementation.LOAD | Implementation.SAVE;
        public Lights(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 8;

                //TODO: how do the indexes map to the nodes?

                int numInstances = reader.ReadInt32();
                for (int i = 0; i < numInstances; i++)
                {
                    Indexes.Add(reader.ReadInt32()); // MVR index
                }

                int numNodes = reader.ReadInt16();
                for (int i = 0; i < numNodes; i++)
                {
                    Node node = new Node();
                    node.min = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    node.max = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    node.childA = reader.ReadInt16();
                    node.childB = reader.ReadInt16();
                    node.first = reader.ReadInt16();
                    node.count = reader.ReadInt16();
                    node.is_leaf = reader.ReadBoolean();
                    reader.BaseStream.Position += 3;
                    Values.Add(node);
                }

                Sun.enabled = reader.ReadBoolean();
                Sun.colour = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Sun.direction = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                Sun.feature_flags = (LightFeature)reader.ReadUInt16();
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.BaseStream.SetLength(0);
                Utilities.WriteString("ligt", writer);
                writer.Write(4);

                writer.Write(Indexes.Count);
                for (int i = 0; i < Indexes.Count; i++)
                    writer.Write(Indexes[i]);

                writer.Write((Int16)Values.Count);
                for (int i = 0; i < Values.Count; i++)
                {
                    Utilities.Write<Vector3>(writer, Values[i].min);
                    Utilities.Write<Vector3>(writer, Values[i].max);
                    writer.Write((Int16)Values[i].childA);
                    writer.Write((Int16)Values[i].childB);
                    writer.Write((Int16)Values[i].first);
                    writer.Write((Int16)Values[i].count);
                    writer.Write(Values[i].is_leaf);
                    writer.Write(new byte[3]);
                }

                writer.Write(Sun.enabled);
                Utilities.Write<Vector3>(writer, Sun.colour);
                Utilities.Write<Vector3>(writer, Sun.direction);
                writer.Write((ushort)Sun.feature_flags);
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Node
        {
            public Vector3 min;
            public Vector3 max;
            public Int16 childA;
            public Int16 childB;
            public Int16 first;
            public Int16 count;
            public bool is_leaf;
        }

        public class DirectionalLight
        {
            public bool enabled;
            public Vector3 colour;
            public Vector3 direction;
            public LightFeature feature_flags;
        }
        #endregion
    }
}