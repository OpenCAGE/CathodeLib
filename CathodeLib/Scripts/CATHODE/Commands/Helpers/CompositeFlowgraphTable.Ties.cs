using CATHODE.Scripting;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using static CathodeLib.CompositeFlowgraphTable.FlowgraphMeta;

namespace CathodeLib
{
    /* Wire ties: points several connections' lines are tied together at, so they run as one bundle. Kept in the "TIES" chunk of
       the table's extension block (see CompositeFlowgraphTable.Extensions.cs), so a reader without it loads the pages as before. */
    public partial class CompositeFlowgraphTable
    {
        /// <summary>Chunk tag of the pages' wire ties ("TIES").</summary>
        public const uint ChunkWireTies = 0x53454954;

        partial void WriteExtensionChunks(List<KeyValuePair<uint, byte[]>> chunks)
        {
            byte[] ties = WriteWireTies();
            if (ties != null)
                chunks.Add(new KeyValuePair<uint, byte[]>(ChunkWireTies, ties));
        }

        partial void ReadExtensionChunk(uint tag, BinaryReader reader, long end, ref bool handled)
        {
            if (tag != ChunkWireTies)
                return;
            ReadWireTies(reader, end);
            handled = true;
        }

        private byte[] WriteWireTies()
        {
            int pages = 0;
            for (int i = 0; i < flowgraphs.Count; i++)
                if (flowgraphs[i].WireTies != null && flowgraphs[i].WireTies.Count != 0)
                    pages++;
            if (pages == 0)
                return null;

            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(pages);
                for (int i = 0; i < flowgraphs.Count; i++)
                {
                    List<WireTieMeta> ties = flowgraphs[i].WireTies;
                    if (ties == null || ties.Count == 0)
                        continue;
                    writer.Write(i);
                    writer.Write(ties.Count);
                    foreach (WireTieMeta tie in ties)
                    {
                        //Records (a tie, and each of its members) say how long they are, so fields added later can be skipped
                        WriteRecord(writer, w =>
                        {
                            w.Write(tie.ID);
                            w.Write(tie.Position.X);
                            w.Write(tie.Position.Y);
                            w.Write(tie.Axis);
                            w.Write(tie.Members.Count);
                            foreach (WireTieMemberMeta member in tie.Members)
                            {
                                WriteRecord(w, m =>
                                {
                                    m.Write(member.FromNodeID);
                                    Utilities.Write<ShortGuid>(m, member.FromEntityGUID);
                                    Utilities.Write<ShortGuid>(m, member.FromParameterGUID);
                                    m.Write(member.ToNodeID);
                                    Utilities.Write<ShortGuid>(m, member.ToEntityGUID);
                                    Utilities.Write<ShortGuid>(m, member.ToParameterGUID);
                                    m.Write(member.Slot);
                                    m.Write(member.Order);
                                });
                            }
                        });
                    }
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteRecord(BinaryWriter writer, System.Action<BinaryWriter> write)
        {
            using (MemoryStream record = new MemoryStream())
            using (BinaryWriter recordWriter = new BinaryWriter(record))
            {
                write(recordWriter);
                recordWriter.Flush();
                writer.Write((int)record.Length);
                writer.Write(record.ToArray());
            }
        }

        private void ReadWireTies(BinaryReader reader, long end)
        {
            int pages = reader.ReadInt32();
            for (int p = 0; p < pages && reader.BaseStream.Position < end; p++)
            {
                int pageIndex = reader.ReadInt32();
                int count = reader.ReadInt32();
                List<WireTieMeta> ties = pageIndex >= 0 && pageIndex < flowgraphs.Count ? flowgraphs[pageIndex].WireTies : null;
                for (int t = 0; t < count; t++)
                {
                    int length = reader.ReadInt32();
                    long tieEnd = reader.BaseStream.Position + length;
                    if (length < 0 || tieEnd > end)
                        return;
                    WireTieMeta tie = new WireTieMeta();
                    tie.ID = reader.ReadInt32();
                    tie.Position = new Point(reader.ReadInt32(), reader.ReadInt32());
                    tie.Axis = reader.ReadByte();
                    int members = reader.ReadInt32();
                    for (int m = 0; m < members; m++)
                    {
                        int memberLength = reader.ReadInt32();
                        long memberEnd = reader.BaseStream.Position + memberLength;
                        if (memberLength < 0 || memberEnd > tieEnd)
                            return;
                        WireTieMemberMeta member = new WireTieMemberMeta();
                        member.FromNodeID = reader.ReadInt32();
                        member.FromEntityGUID = Utilities.Consume<ShortGuid>(reader);
                        member.FromParameterGUID = Utilities.Consume<ShortGuid>(reader);
                        member.ToNodeID = reader.ReadInt32();
                        member.ToEntityGUID = Utilities.Consume<ShortGuid>(reader);
                        member.ToParameterGUID = Utilities.Consume<ShortGuid>(reader);
                        member.Slot = reader.ReadInt32();
                        member.Order = reader.ReadInt32();
                        reader.BaseStream.Position = memberEnd;
                        tie.Members.Add(member);
                    }
                    reader.BaseStream.Position = tieEnd;
                    ties?.Add(tie);
                }
            }
        }

        public partial class FlowgraphMeta
        {
            /// <summary>
            /// The page's wire ties. Only how the page's lines are drawn: not part of what the page is (see Equals).
            /// </summary>
            public List<WireTieMeta> WireTies = new List<WireTieMeta>();

            partial void CloneExtensions(FlowgraphMeta copy)
            {
                copy.WireTies = new List<WireTieMeta>(WireTies?.Count ?? 0);
                if (WireTies != null)
                {
                    foreach (WireTieMeta tie in WireTies)
                        if (tie != null)
                            copy.WireTies.Add(tie.Clone());
                }
            }

            /// <summary>
            /// A point (canvas coordinates) several connections' lines are tied together at. Each member line passes through
            /// the tie in its own lane, in the order of <see cref="Members"/>.
            /// </summary>
            public class WireTieMeta
            {
                /// <summary>Unique among the page's ties.</summary>
                public int ID;
                public Point Position;
                /// <summary>0: the lanes are stacked down the tie (lines running left to right); 1: across it (lines running up or down).</summary>
                public byte Axis;
                public List<WireTieMemberMeta> Members = new List<WireTieMemberMeta>();

                public WireTieMeta Clone()
                {
                    WireTieMeta copy = (WireTieMeta)MemberwiseClone();
                    copy.Members = new List<WireTieMemberMeta>(Members?.Count ?? 0);
                    if (Members != null)
                    {
                        foreach (WireTieMemberMeta member in Members)
                            if (member != null)
                                copy.Members.Add(member.Clone());
                    }
                    return copy;
                }
            }

            /// <summary>
            /// A connection tied at a wire tie: the node (and pin) it goes out of - the one holding it in ConnectionsOut - and
            /// the one it goes into, and where the tie comes along its line.
            /// </summary>
            public class WireTieMemberMeta
            {
                public int FromNodeID;
                public ShortGuid FromEntityGUID;
                public ShortGuid FromParameterGUID;
                public int ToNodeID;
                public ShortGuid ToEntityGUID;
                public ShortGuid ToParameterGUID;
                /// <summary>How many of the line's own reroute points (RoutePoints) come before the tie.</summary>
                public int Slot;
                /// <summary>The tie's place among the ties the line passes through, from its feeding end.</summary>
                public int Order;

                public WireTieMemberMeta Clone() => (WireTieMemberMeta)MemberwiseClone();
            }
        }
    }
}
