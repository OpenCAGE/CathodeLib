#if !GODOT
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CATHODE.Scripting.Refactor
{
    /* Arranging a page that has boxes on it (the editor's comment boxes): each box's nodes are laid out on their own, the box is
       fitted round them (title bar and padding included) and then placed as one block among the nodes and boxes around it, so
       a box never overlaps anything after arranging. Boxes inside boxes nest the same way. */
    public static partial class PageArranger
    {
        /// <summary>A box nodes are grouped in.</summary>
        public struct Group
        {
            /// <summary>The group this one sits inside, or -1.</summary>
            public int Parent;
            /// <summary>The least it can be fitted to (room for its title, or its size when nothing is in it).</summary>
            public Size MinSize;
        }

        /// <summary>Room inside a box round what it holds.</summary>
        public const int GroupPadding = 30;
        /// <summary>The box's title bar, above that.</summary>
        public const int GroupHeader = 30;

        /// <summary>
        /// Which box each node is in - the smallest that wholly holds it, or -1 - and which box each box sits in (the smallest
        /// bigger one wholly holding it, or -1). Equal boxes nest in list order.
        /// </summary>
        public static void GroupsFromBoxes(IReadOnlyList<Rectangle> nodes, IReadOnlyList<Rectangle> boxes, out int[] nodeGroup, out int[] boxParent)
        {
            long Area(Rectangle r) => (long)r.Width * r.Height;
            nodeGroup = new int[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                int best = -1;
                for (int b = 0; b < boxes.Count; b++)
                {
                    if (boxes[b].Contains(nodes[i]) && (best < 0 || Area(boxes[b]) < Area(boxes[best])))
                        best = b;
                }
                nodeGroup[i] = best;
            }
            boxParent = new int[boxes.Count];
            for (int b = 0; b < boxes.Count; b++)
            {
                int best = -1;
                for (int o = 0; o < boxes.Count; o++)
                {
                    if (o == b || !boxes[o].Contains(boxes[b]))
                        continue;
                    bool outside = Area(boxes[o]) > Area(boxes[b]) || (Area(boxes[o]) == Area(boxes[b]) && o < b);
                    if (outside && (best < 0 || Area(boxes[o]) < Area(boxes[best])))
                        best = o;
                }
                boxParent[b] = best;
            }
        }

        /// <summary>
        /// Lay out nodes some of which are in groups: each group's own nodes and groups are laid out by <see cref="Arrange"/>,
        /// the group is fitted round them, and the groups are laid out as blocks with what is around them. Returns the nodes'
        /// new top-left positions, starting at (0, 0), and the groups' new bounds the same way.
        /// </summary>
        public static Point[] ArrangeGrouped(IReadOnlyList<Node> nodes, IReadOnlyList<Connection> connections, IReadOnlyList<int> nodeGroup, IReadOnlyList<Group> groups, out Rectangle[] groupBounds, int header = GroupHeader, int padding = GroupPadding)
        {
            int nodeCount = nodes.Count, groupCount = groups.Count;
            Point[] nodeAt = new Point[nodeCount];     //within the group it is in (or the page)
            Point[] groupAt = new Point[groupCount];   //within its parent (or the page)
            Size[] groupSize = new Size[groupCount];

            int ParentOf(int g) => g < 0 || g >= groupCount ? -1 : groups[g].Parent;
            int GroupOf(int n) => nodeGroup == null || n >= nodeGroup.Count || nodeGroup[n] < 0 || nodeGroup[n] >= groupCount ? -1 : nodeGroup[n];

            //The child of `group` that a node is under (as -2 - index), the node itself when it is in `group` (its index), or null
            int? ItemUnder(int node, int group)
            {
                int g = GroupOf(node);
                if (g == group) return node;
                for (int depth = 0; g >= 0 && depth <= groupCount; depth++)
                {
                    int parent = ParentOf(g);
                    if (parent == group) return -2 - g;
                    g = parent;
                }
                return null;
            }

            //Where a node is within a group it is under
            Point NodeWithin(int node, int group)
            {
                Point at = nodeAt[node];
                int g = GroupOf(node);
                for (int depth = 0; g >= 0 && g != group && depth <= groupCount; depth++)
                {
                    at.Offset(groupAt[g]);
                    g = ParentOf(g);
                }
                return at;
            }

            bool[] laidOut = new bool[groupCount];
            void LayOut(int group, int depth)
            {
                if (depth > groupCount) return;
                List<int> childGroups = Enumerable.Range(0, groupCount).Where(g => ParentOf(g) == group && g != group).ToList();
                foreach (int child in childGroups)
                {
                    if (!laidOut[child])
                    {
                        laidOut[child] = true;
                        LayOut(child, depth + 1);
                    }
                }

                //Its items: its own nodes, then its groups as blocks
                List<int> items = Enumerable.Range(0, nodeCount).Where(n => GroupOf(n) == group).ToList();
                items.AddRange(childGroups.Select(g => -2 - g));
                Dictionary<int, int> itemIndex = new Dictionary<int, int>();
                for (int i = 0; i < items.Count; i++)
                    itemIndex[items[i]] = i;
                List<Node> sized = items.Select(item => item >= 0 ? nodes[item] : new Node() { Size = groupSize[-2 - item] }).ToList();

                //A connection between two of its items; one into a block is to the pin where it sits within the block
                List<Connection> joined = new List<Connection>();
                foreach (Connection c in connections)
                {
                    if (c.From < 0 || c.To < 0 || c.From >= nodeCount || c.To >= nodeCount) continue;
                    int? from = ItemUnder(c.From, group), to = ItemUnder(c.To, group);
                    if (from == null || to == null || from == to) continue;
                    joined.Add(new Connection()
                    {
                        From = itemIndex[from.Value],
                        FromSide = c.FromSide,
                        FromOffset = c.FromOffset + OffsetInBlock(from.Value, c.From, c.FromSide),
                        To = itemIndex[to.Value],
                        ToSide = c.ToSide,
                        ToOffset = c.ToOffset + OffsetInBlock(to.Value, c.To, c.ToSide),
                    });
                }
                int OffsetInBlock(int item, int node, PinSide side)
                {
                    if (item >= 0) return 0;
                    Point at = NodeWithin(node, -2 - item);
                    return side == PinSide.Top || side == PinSide.Bottom ? at.X : at.Y;
                }

                Point[] positions = Arrange(sized, joined);
                Point inset = group < 0 ? Point.Empty : new Point(padding, header + padding);
                int right = 0, bottom = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    Point at = new Point(positions[i].X + inset.X, positions[i].Y + inset.Y);
                    if (items[i] >= 0) nodeAt[items[i]] = at;
                    else groupAt[-2 - items[i]] = at;
                    right = Math.Max(right, positions[i].X + sized[i].Size.Width);
                    bottom = Math.Max(bottom, positions[i].Y + sized[i].Size.Height);
                }
                if (group >= 0)
                {
                    Size least = groups[group].MinSize;
                    groupSize[group] = items.Count == 0 ? least : new Size(Math.Max(least.Width, right + padding * 2), Math.Max(least.Height, bottom + header + padding * 2));
                }
            }
            LayOut(-1, 0);

            //Groups no chain reaches (a parent loop) are left where they are: zero-sized at the origin
            Point[] result = new Point[nodeCount];
            for (int n = 0; n < nodeCount; n++)
                result[n] = NodeWithin(n, -1);
            groupBounds = new Rectangle[groupCount];
            for (int g = 0; g < groupCount; g++)
            {
                Point at = groupAt[g];
                int parent = ParentOf(g);
                for (int depth = 0; parent >= 0 && depth <= groupCount; depth++)
                {
                    at.Offset(groupAt[parent]);
                    parent = ParentOf(parent);
                }
                groupBounds[g] = new Rectangle(at, groupSize[g]);
            }
            return result;
        }
    }
}
#endif
