using CATHODE.Scripting;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace CATHODE
{
    /* DATA/ENV/PRODUCTION/x/WORLD/SNDNODENETWORK.DAT */
    public class SoundNodeNetwork : CathodeFile
    {
        public List<NetworkInfo> NetworkInfos = new List<NetworkInfo>();
        public List<NetworkNode> AllNodes = new List<NetworkNode>(); //todo: should just generate this automatically
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public SoundNodeNetwork(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                reader.BaseStream.Position += 4; //version
                int numNodes = reader.ReadUInt16();
                int numNetworks = reader.ReadUInt16();

                for (int i = 0; i < numNetworks; i++)
                {
                    var networkInfo = new NetworkInfo();

                    ushort nameSize = reader.ReadUInt16();
                    networkInfo.NetworkName = Encoding.ASCII.GetString(reader.ReadBytes(nameSize));

                    networkInfo.ReverbIndex = reader.ReadUInt16();
                    networkInfo.EnterEventIndex = (short)reader.ReadUInt16();
                    networkInfo.ExitEventIndex = (short)reader.ReadUInt16();
                    networkInfo.RoomSizeValue = reader.ReadUInt32();
                    networkInfo.LinkedNetworkScalar = reader.ReadSingle();

                    networkInfo.NetworkBottomLeft = Utilities.Consume<Vector3>(reader);
                    networkInfo.NetworkTopRight = Utilities.Consume<Vector3>(reader);

                    ushort nodeCountInNetwork = reader.ReadUInt16();
                    ushort linkedNetworkCount = reader.ReadUInt16();

                    for (int j = 0; j < linkedNetworkCount; j++)
                    {
                        networkInfo.LinkedNetworks.Add(new NetworkLinkData
                        {
                            LinkedNetworkId = reader.ReadUInt16(),
                            BarrierInstanceGuid = reader.ReadUInt32(),
                            NodeId = reader.ReadUInt16(),
                            LinkedNodeId = reader.ReadUInt16()
                        });
                    }

                    ushort pathCount = reader.ReadUInt16();
                    for (int j = 0; j < pathCount; j++)
                    {
                        var path = new NetworkPath();
                        path.NetworkId = reader.ReadUInt16();
                        ushort barrierCount = reader.ReadUInt16();
                        for (int k = 0; k < barrierCount; k++)
                        {
                            path.BarrierGuids.Add(reader.ReadUInt32());
                        }
                        networkInfo.NetworkPaths.Add(path);
                    }

                    NetworkInfos.Add(networkInfo);
                }

                for (int i = 0; i < numNodes; i++)
                {
                    var node = new NetworkNode
                    {
                        SoundNetworkId = reader.ReadUInt16(),
                        Position = Utilities.Consume<Vector3>(reader)
                    };

                    ushort linkedNodeCount = reader.ReadUInt16();
                    for (int j = 0; j < linkedNodeCount; j++)
                    {
                        node.NodeLinks.Add(new NodeLinkData
                        {
                            LinkedNodeId = reader.ReadUInt16(),
                            PathDistance = reader.ReadByte(),
                            ObstructedDistance = reader.ReadByte()
                        });
                    }
                    AllNodes.Add(node);
                }

                foreach (var node in AllNodes)
                {
                    if (node.SoundNetworkId < NetworkInfos.Count)
                    {
                        NetworkInfos[node.SoundNetworkId].Nodes.Add(node);
                    }
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.Write(14);
                writer.Write((ushort)AllNodes.Count);
                writer.Write((ushort)NetworkInfos.Count);

                foreach (var networkInfo in NetworkInfos)
                {
                    byte[] nameBytes = Encoding.ASCII.GetBytes(networkInfo.NetworkName);
                    writer.Write((ushort)nameBytes.Length);
                    writer.Write(nameBytes);

                    writer.Write(networkInfo.ReverbIndex);
                    writer.Write((ushort)networkInfo.EnterEventIndex);
                    writer.Write((ushort)networkInfo.ExitEventIndex);
                    writer.Write(networkInfo.RoomSizeValue);
                    writer.Write(networkInfo.LinkedNetworkScalar);

                    Utilities.Write<Vector3>(writer, networkInfo.NetworkBottomLeft);
                    Utilities.Write<Vector3>(writer, networkInfo.NetworkTopRight);

                    writer.Write((ushort)networkInfo.Nodes.Count);
                    writer.Write((ushort)networkInfo.LinkedNetworks.Count);

                    foreach (var link in networkInfo.LinkedNetworks)
                    {
                        writer.Write(link.LinkedNetworkId);
                        writer.Write(link.BarrierInstanceGuid);
                        writer.Write(link.NodeId);
                        writer.Write(link.LinkedNodeId);
                    }

                    writer.Write((ushort)networkInfo.NetworkPaths.Count);
                    foreach (var path in networkInfo.NetworkPaths)
                    {
                        writer.Write(path.NetworkId);
                        writer.Write((ushort)path.BarrierGuids.Count);
                        foreach (uint guid in path.BarrierGuids)
                        {
                            writer.Write(guid);
                        }
                    }
                }

                foreach (var node in AllNodes)
                {
                    writer.Write(node.SoundNetworkId);
                    Utilities.Write<Vector3>(writer, node.Position);
                    writer.Write((ushort)node.NodeLinks.Count);

                    foreach (var link in node.NodeLinks)
                    {
                        writer.Write(link.LinkedNodeId);
                        writer.Write(link.PathDistance); 
                        writer.Write(link.ObstructedDistance);
                    }
                }
            }
            return true;
        }
        #endregion

        #region STRUCTURES
        public class NodeLinkData
        {
            public ushort LinkedNodeId;
            public byte PathDistance;
            public byte ObstructedDistance;
        }

        public class NetworkNode
        {
            public ushort SoundNetworkId;
            public Vector3 Position;
            public List<NodeLinkData> NodeLinks = new List<NodeLinkData>();
        }

        public class NetworkLinkData
        {
            public ushort LinkedNetworkId;
            public uint BarrierInstanceGuid; //sound barrier collision
            public ushort NodeId;
            public ushort LinkedNodeId;
        }

        public class NetworkPath
        {
            public ushort NetworkId;
            public List<uint> BarrierGuids = new List<uint>(); //sound barrier entities
        }

        public class NetworkInfo
        {
            public string NetworkName;
            public ushort ReverbIndex;
            public short EnterEventIndex; //-1 if none
            public short ExitEventIndex; //-1 if none
            public uint RoomSizeValue;
            public float LinkedNetworkScalar;
            public Vector3 NetworkBottomLeft;
            public Vector3 NetworkTopRight;
            public List<NetworkNode> Nodes = new List<NetworkNode>();
            public List<NetworkLinkData> LinkedNetworks = new List<NetworkLinkData>();
            public List<NetworkPath> NetworkPaths = new List<NetworkPath>();
        }
        #endregion
    }
}