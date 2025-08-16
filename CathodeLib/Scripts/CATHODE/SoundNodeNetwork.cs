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
    /// <summary>
    /// DATA/ENV/PRODUCTION/x/WORLD/SNDNODENETWORK.DAT
    /// </summary>
    public class SoundNodeNetwork : CathodeFile
    {
        public List<NetworkInfo> Entries = new List<NetworkInfo>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public SoundNodeNetwork(string path) : base(path) { }
        public SoundNodeNetwork(MemoryStream stream, string path = "") : base(stream, path) { }
        public SoundNodeNetwork(byte[] data, string path = "") : base(data, path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            var rawNetworkLinks = new List<(NetworkInfo owner, ushort netId, uint guid, ushort nodeId, ushort linkedNodeId)>();
            var rawNetworkPaths = new List<(NetworkInfo owner, ushort netId, List<uint> guids)>();
            var rawNodeLinks = new List<(NetworkNode owner, ushort linkedNodeId, byte pathDist, byte obstrDist)>();

            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.BaseStream.Position += 4; //version
                int numNodes = reader.ReadUInt16();
                int numNetworks = reader.ReadUInt16();

                List<NetworkNode> allNodes = new List<NetworkNode>(numNodes);
                Entries = new List<NetworkInfo>(numNetworks);

                for (int i = 0; i < numNetworks; i++)
                {
                    NetworkInfo networkInfo = new NetworkInfo();
                    ushort nameSize = reader.ReadUInt16();
                    networkInfo.NetworkName = Encoding.ASCII.GetString(reader.ReadBytes(nameSize));
                    networkInfo.ReverbIndex = reader.ReadUInt16();
                    networkInfo.EnterEventIndex = (short)reader.ReadUInt16();
                    networkInfo.ExitEventIndex = (short)reader.ReadUInt16();
                    networkInfo.RoomSizeValue = reader.ReadUInt32();
                    networkInfo.LinkedNetworkScalar = reader.ReadSingle();
                    networkInfo.NetworkBottomLeft = Utilities.Consume<Vector3>(reader);
                    networkInfo.NetworkTopRight = Utilities.Consume<Vector3>(reader);
                    reader.BaseStream.Position += 2; //node/link counts

                    ushort linkedNetworkCount = reader.ReadUInt16();
                    for (int j = 0; j < linkedNetworkCount; j++)
                        rawNetworkLinks.Add((networkInfo, reader.ReadUInt16(), reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadUInt16()));

                    ushort pathCount = reader.ReadUInt16();
                    for (int j = 0; j < pathCount; j++)
                    {
                        ushort netId = reader.ReadUInt16();
                        ushort barrierCount = reader.ReadUInt16();
                        var guids = new List<uint>(barrierCount);
                        for (int k = 0; k < barrierCount; k++) guids.Add(reader.ReadUInt32());
                        rawNetworkPaths.Add((networkInfo, netId, guids));
                    }
                    Entries.Add(networkInfo);
                }

                for (int i = 0; i < numNodes; i++)
                {
                    var networkOwner = Entries[reader.ReadUInt16()];
                    NetworkNode newNode = new NetworkNode(networkOwner, Utilities.Consume<Vector3>(reader));
                    networkOwner.Nodes.Add(newNode);
                    allNodes.Add(newNode);

                    ushort linkedNodeCount = reader.ReadUInt16();
                    for (int j = 0; j < linkedNodeCount; j++)
                        rawNodeLinks.Add((newNode, reader.ReadUInt16(), reader.ReadByte(), reader.ReadByte()));
                }

                foreach (var link in rawNetworkLinks)
                    link.owner.LinkedNetworks.Add(new NetworkLinkData(Entries[link.netId], link.guid, allNodes[link.nodeId], allNodes[link.linkedNodeId]));

                foreach (var path in rawNetworkPaths)
                    path.owner.NetworkPaths.Add(new NetworkPath(Entries[path.netId], path.guids));

                foreach (var link in rawNodeLinks)
                    link.owner.NodeLinks.Add(new NodeLinkData(allNodes[link.linkedNodeId], link.pathDist, link.obstrDist));
            }

            return true;
        }

        override protected bool SaveInternal()
        {
            List<NetworkNode> allNodes = new List<NetworkNode>();
            Dictionary<NetworkNode, ushort> nodeToIndex = new Dictionary<NetworkNode, ushort>();
            Dictionary<NetworkInfo, ushort> networkToIndex = new Dictionary<NetworkInfo, ushort>();

            for (ushort i = 0; i < Entries.Count; i++)
            {
                networkToIndex.Add(Entries[i], i);
                foreach (var node in Entries[i].Nodes)
                {
                    nodeToIndex.Add(node, (ushort)allNodes.Count);
                    allNodes.Add(node);
                }
            }

            using (BinaryWriter writer = new BinaryWriter(File.OpenWrite(_filepath)))
            {
                writer.Write(14);
                writer.Write((ushort)allNodes.Count);
                writer.Write((ushort)Entries.Count);

                foreach (var networkInfo in Entries)
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
                        writer.Write(networkToIndex[link.LinkedNetwork]);
                        writer.Write(link.BarrierInstanceGuid);
                        writer.Write(nodeToIndex[link.Node]);
                        writer.Write(nodeToIndex[link.LinkedNode]);
                    }

                    writer.Write((ushort)networkInfo.NetworkPaths.Count);
                    foreach (var path in networkInfo.NetworkPaths)
                    {
                        writer.Write(networkToIndex[path.Network]);
                        writer.Write((ushort)path.BarrierGuids.Count);
                        foreach (uint guid in path.BarrierGuids)
                        {
                            writer.Write(guid);
                        }
                    }
                }

                foreach (var node in allNodes)
                {
                    writer.Write(networkToIndex[node.SoundNetwork]);
                    Utilities.Write<Vector3>(writer, node.Position);
                    writer.Write((ushort)node.NodeLinks.Count);

                    foreach (var link in node.NodeLinks)
                    {
                        writer.Write(nodeToIndex[link.LinkedNode]);
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
            public NetworkNode LinkedNode;
            public byte PathDistance;
            public byte ObstructedDistance;

            public NodeLinkData(NetworkNode node, byte path, byte obstructed) { LinkedNode = node; PathDistance = path; ObstructedDistance = obstructed; }
        }

        public class NetworkNode
        {
            public NetworkInfo SoundNetwork;
            public Vector3 Position;
            public List<NodeLinkData> NodeLinks = new List<NodeLinkData>();

            public NetworkNode(NetworkInfo net, Vector3 pos) { SoundNetwork = net; Position = pos; }
        }

        public class NetworkLinkData
        {
            public NetworkInfo LinkedNetwork;
            public uint BarrierInstanceGuid; //sound barrier collision
            public NetworkNode Node;
            public NetworkNode LinkedNode;

            public NetworkLinkData(NetworkInfo net, uint guid, NetworkNode node, NetworkNode linkedNode) { LinkedNetwork = net; BarrierInstanceGuid = guid; Node = node; LinkedNode = linkedNode; }
        }

        public class NetworkPath
        {
            public NetworkInfo Network;
            public List<uint> BarrierGuids = new List<uint>(); //sound barrier entities

            public NetworkPath(NetworkInfo net, List<uint> guids) { Network = net; BarrierGuids = guids; }
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