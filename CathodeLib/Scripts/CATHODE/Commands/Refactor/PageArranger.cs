#if !GODOT
using CATHODE.Scripting.Internal;
using CathodeLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using static CathodeLib.CompositeFlowgraphTable;
using static CathodeLib.CompositeFlowgraphTable.FlowgraphMeta;
using static CathodeLib.CompositeFlowgraphTable.FlowgraphMeta.NodeMeta;

namespace CATHODE.Scripting.Refactor
{
    /// <summary>Which edge of a node a pin is drawn on (in the order the flowgraph editor numbers them, as stored for unlinked pins).</summary>
    public enum PinSide
    {
        Top,
        Bottom,
        Left,
        Right,
    }

    /// <summary>
    /// Lays out the nodes of a script page so it reads left to right: logic connections (a right pin to a left
    /// pin) run forwards from column to column, ordered to cross as little as they can, and a node joined only by
    /// data connections (a top pin to a bottom pin) sits right above or below the node it serves - where the
    /// shipped pages put them. Separate groups of connected nodes are packed side by side, unconnected nodes last.
    /// </summary>
    /// <remarks>
    /// A layered (Sugiyama) layout: cycles are broken by turning the fewest connections round, every node gets a
    /// column by the longest chain of logic connections into it (then pulled towards whichever side it has more
    /// connections on), connections that skip columns are routed through placeholders, columns are reordered by
    /// the average height of what each node connects to until crossings stop falling, and heights are settled so
    /// connected pins line up as far as the columns allow. Only positions change: no node is added or removed.
    /// <para>Where the nodes are now plays no part - only their sizes, their order and their connections do - so a
    /// page comes out the same however tangled it was, and arranging an arranged page leaves it as it is.</para>
    /// </remarks>
    public static class PageArranger
    {
        /// <summary>A node to place.</summary>
        public struct Node
        {
            public Size Size;
        }

        /// <summary>
        /// A connection between two nodes' pins. Offsets run along the side the pin is on: from the node's top for a
        /// left or right pin, from its left for a top or bottom pin.
        /// </summary>
        public struct Connection
        {
            public int From;
            public PinSide FromSide;
            public int FromOffset;
            public int To;
            public PinSide ToSide;
            public int ToOffset;
        }

        private const int ColumnGap = 90;           //between columns, before the room more connections need
        private const int ColumnGapPerConnection = 6;
        private const int ColumnGapMax = 260;
        private const int RowGap = 36;              //between nodes in a column
        private const int PlaceholderGap = 12;      //next to a connection's placeholder
        private const int PlaceholderHeight = 8;
        private const int DataRowGap = 54;          //between a node and the row of data nodes above or below it
        private const int DataNodeGap = 24;         //between data nodes in a row
        private const int GroupGap = 140;           //between separately laid out groups
        private const double DataPull = 1;          //how hard a data connection between column nodes draws them together (as hard as a logic one)
        private const int MaxSweeps = 24;

        /// <summary>New top-left positions for the nodes, starting at (0, 0).</summary>
        public static Point[] Arrange(IReadOnlyList<Node> nodes, IReadOnlyList<Connection> connections)
        {
            Point[] result = new Point[nodes.Count];
            if (nodes.Count == 0)
                return result;

            List<Logic> logic = new List<Logic>();
            List<Data> data = new List<Data>();
            foreach (Connection c in connections)
            {
                if (c.From < 0 || c.To < 0 || c.From >= nodes.Count || c.To >= nodes.Count || c.From == c.To)
                    continue;
                if (c.FromSide == PinSide.Right && c.ToSide == PinSide.Left)
                    logic.Add(new Logic(c.From, c.FromOffset, c.To, c.ToOffset));
                else if (c.FromSide == PinSide.Left && c.ToSide == PinSide.Right)
                    logic.Add(new Logic(c.To, c.ToOffset, c.From, c.FromOffset));
                else if (c.FromSide == PinSide.Top && c.ToSide == PinSide.Bottom)
                    data.Add(new Data(c.From, c.FromOffset, c.To, c.ToOffset));
                else if (c.FromSide == PinSide.Bottom && c.ToSide == PinSide.Top)
                    data.Add(new Data(c.To, c.ToOffset, c.From, c.FromOffset));
                else //not a pairing the editor draws: lay it out as the logic connection it falls back to
                    logic.Add(new Logic(c.From, nodes[c.From].Size.Height / 2, c.To, nodes[c.To].Size.Height / 2));
            }

            //Groups of connected nodes
            int[] parent = Enumerable.Range(0, nodes.Count).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }
            foreach (Logic l in logic) Union(l.From, l.To);
            foreach (Data d in data) Union(d.Lower, d.Upper);

            Dictionary<int, List<int>> members = new Dictionary<int, List<int>>();
            for (int i = 0; i < nodes.Count; i++)
            {
                int root = Find(i);
                if (!members.TryGetValue(root, out List<int> list))
                    members.Add(root, list = new List<int>());
                list.Add(i);
            }

            List<Placed> groups = new List<Placed>();
            List<Placed> loose = new List<Placed>();
            foreach (List<int> group in members.Values)
            {
                if (group.Count == 1)
                {
                    int i = group[0];
                    loose.Add(new Placed() { Nodes = group, Positions = new[] { Point.Empty }, Size = nodes[i].Size, First = i });
                    continue;
                }
                HashSet<int> inGroup = new HashSet<int>(group);
                groups.Add(LayOutGroup(nodes, group, logic.Where(o => inGroup.Contains(o.From)).ToList(), data.Where(o => inGroup.Contains(o.Lower)).ToList()));
            }

            //Biggest groups first, unconnected nodes after, each in the page's order
            groups = groups.OrderByDescending(o => o.Nodes.Count).ThenBy(o => o.First).ToList();
            loose = loose.OrderBy(o => o.First).ToList();
            Pack(groups.Concat(loose).ToList(), result);
            return result;
        }

        /// <summary>
        /// Lay out the nodes of a stored page (or just <paramref name="only"/> of them), sizing each from the pins its
        /// drawn connections use, and keep the laid out nodes' top-left corner where theirs was. Pin sides come from
        /// <paramref name="sideOf"/> when given, else from the script (<see cref="SideOf"/>) when <paramref name="commands"/>
        /// is given, else every connection is taken as a logic one. The nodes are taken in an order of the arranger's own,
        /// so a page always comes out the same; <paramref name="pageOrder"/> takes them in the page's order instead, for
        /// nodes just made in an order that means something (that of the links they draw, say) - a better start.
        /// With <paramref name="split"/>, a data connection that would run a long way is given a node of its own (see
        /// <see cref="SplitsFor"/>): the page can gain nodes, but every link is still drawn exactly once. Returns how many
        /// nodes were added.
        /// </summary>
        public static int ArrangePage(Composite composite, FlowgraphMeta page, Commands commands = null, ICollection<NodeMeta> only = null, Func<Entity, ShortGuid, PinSide?> sideOf = null, bool pageOrder = false, bool split = true)
        {
            if (composite == null || page?.Nodes == null)
                return 0;
            HashSet<NodeMeta> chosen = only == null ? null : new HashSet<NodeMeta>(only);
            List<NodeMeta> placing = page.Nodes.Where(o => (chosen == null || chosen.Contains(o)) && composite.GetEntityByID(o.EntityGUID) != null).ToList();
            if (placing.Count == 0)
                return 0;
            Point origin = new Point(placing.Min(o => o.Position.X), placing.Min(o => o.Position.Y));
            if (!pageOrder)
                placing = OwnOrder(placing);

            PinSide Side(Entity entity, ShortGuid parameter, PinSide fallback)
            {
                PinSide? side = sideOf?.Invoke(entity, parameter);
                if (side == null && commands != null)
                    side = SideOf(commands, composite, entity, parameter);
                return side ?? fallback;
            }

            int added = 0;
            for (int round = 0; ; round++)
            {
                Dictionary<(int, ShortGuid), int> index = new Dictionary<(int, ShortGuid), int>();
                for (int i = 0; i < placing.Count; i++)
                    index[(placing[i].NodeID, placing[i].EntityGUID)] = i;

                List<List<(ShortGuid pin, PinSide side)>> pins = placing.Select(o => new List<(ShortGuid, PinSide)>()).ToList();
                List<(int from, ShortGuid fromPin, PinSide fromSide, int to, ShortGuid toPin, PinSide toSide, ConnectionMeta meta)> drawn = new List<(int, ShortGuid, PinSide, int, ShortGuid, PinSide, ConnectionMeta)>();
                for (int i = 0; i < placing.Count; i++)
                {
                    Entity owner = composite.GetEntityByID(placing[i].EntityGUID);
                    foreach (ConnectionMeta connection in placing[i].ConnectionsOut)
                    {
                        if (!index.TryGetValue((connection.ConnectedNodeID, connection.ConnectedEntityGUID), out int j))
                            continue;
                        Entity target = composite.GetEntityByID(connection.ConnectedEntityGUID);
                        PinSide fromSide = Side(owner, connection.ParameterGUID, PinSide.Right);
                        PinSide toSide = Side(target, connection.ConnectedParameterGUID, PinSide.Left);
                        if (!Joins(fromSide, toSide))
                        {
                            fromSide = PinSide.Right;
                            toSide = PinSide.Left;
                        }
                        pins[i].Add((connection.ParameterGUID, fromSide));
                        pins[j].Add((connection.ConnectedParameterGUID, toSide));
                        drawn.Add((i, connection.ParameterGUID, fromSide, j, connection.ConnectedParameterGUID, toSide, connection));
                    }
                }

                List<Node> sized = new List<Node>();
                List<Dictionary<(ShortGuid, PinSide), int>> offsets = new List<Dictionary<(ShortGuid, PinSide), int>>();
                for (int i = 0; i < placing.Count; i++)
                {
                    Entity entity = composite.GetEntityByID(placing[i].EntityGUID);
                    foreach (UnlinkedPinMeta unlinked in placing[i].UnlinkedPins ?? new List<UnlinkedPinMeta>())
                        if (Enum.IsDefined(typeof(PinSide), (int)unlinked.PinLocation))
                            pins[i].Add((unlinked.ParameterGUID, (PinSide)unlinked.PinLocation));
                    Dictionary<(ShortGuid, PinSide), int> nodeOffsets = new Dictionary<(ShortGuid, PinSide), int>();
                    Size size = EstimateSize(Title(entity), pins[i], nodeOffsets);
                    sized.Add(new Node() { Size = size });
                    offsets.Add(nodeOffsets);
                }

                List<Connection> connections = drawn.Select(o => new Connection()
                {
                    From = o.from,
                    FromSide = o.fromSide,
                    FromOffset = offsets[o.from][(o.fromPin, o.fromSide)],
                    To = o.to,
                    ToSide = o.toSide,
                    ToOffset = offsets[o.to][(o.toPin, o.toSide)],
                }).ToList();

                Point[] arranged = Arrange(sized, connections);
                List<Split> splits = split && round < SplitRounds ? SplitsFor(sized, connections, arranged) : new List<Split>();
                if (splits.Count == 0)
                {
                    for (int i = 0; i < placing.Count; i++)
                        placing[i].Position = new Point(origin.X + arranged[i].X, origin.Y + arranged[i].Y);
                    return added;
                }

                //Each split: a new node of the entity takes over the connections, from whichever end it is
                foreach (Split s in splits)
                {
                    NodeMeta from = placing[s.Node];
                    NodeMeta copy = new NodeMeta()
                    {
                        EntityGUID = from.EntityGUID,
                        NodeID = page.Nodes.Max(o => o.NodeID) + 1,
                        Position = origin,
                        ConnectionsOut = new List<ConnectionMeta>(),
                        UnlinkedPins = new List<UnlinkedPinMeta>(),
                    };
                    page.Nodes.Add(copy);
                    placing.Add(copy);
                    foreach (int i in s.Connections)
                    {
                        ConnectionMeta meta = drawn[i].meta;
                        if (drawn[i].from == s.Node)
                        {
                            from.ConnectionsOut.Remove(meta);
                            copy.ConnectionsOut.Add(meta);
                        }
                        else
                        {
                            meta.ConnectedNodeID = copy.NodeID;
                        }
                    }
                    added++;
                }
                if (!pageOrder)
                    placing = OwnOrder(placing);
            }
        }

        /// <summary>How many times a layout is checked for connections worth a node of their own, and laid out again.</summary>
        public const int SplitRounds = 10;

        /// <summary>A data connection longer than this, pin to pin, is drawn from a node of its own instead (see <see cref="SplitsFor"/>).</summary>
        public const int LongDataConnection = 600;

        /// <summary>
        /// A node worth another node of its entity: the new node goes beside <see cref="Partner"/> and takes over
        /// <see cref="Connections"/> (indices into the connections laid out), which all join the two.
        /// </summary>
        public sealed class Split
        {
            public int Node;
            public int Partner;
            public List<int> Connections = new List<int>();
        }

        /// <summary>
        /// Where giving an entity another node would make a laid out page neater, as the shipped pages do with a value
        /// used in several places: a data connection (a top pin to a bottom pin) that runs further than
        /// <paramref name="longerThan"/> is better drawn from a node of its own, beside the node at its other end. The
        /// node repeated is the end that is only data (a variable, say), else the upper end, whose value it carries - and
        /// only one that keeps something where it is: a logic connection, or another node it connects to. Lay the nodes
        /// out again with the new ones (each at its partner's side, by the usual rules) after making them.
        /// </summary>
        public static List<Split> SplitsFor(IReadOnlyList<Node> nodes, IReadOnlyList<Connection> connections, IReadOnlyList<Point> positions, int longerThan = LongDataConnection)
        {
            int count = nodes.Count;
            bool Valid(Connection c) => c.From >= 0 && c.To >= 0 && c.From < count && c.To < count && c.From != c.To;
            bool IsData(Connection c) => (c.FromSide == PinSide.Top && c.ToSide == PinSide.Bottom) || (c.FromSide == PinSide.Bottom && c.ToSide == PinSide.Top);
            PointF Pin(int node, PinSide side, int offset)
            {
                Point at = positions[node];
                Size size = nodes[node].Size;
                switch (side)
                {
                    case PinSide.Left: return new PointF(at.X, at.Y + offset);
                    case PinSide.Right: return new PointF(at.X + size.Width, at.Y + offset);
                    case PinSide.Top: return new PointF(at.X + offset, at.Y);
                    default: return new PointF(at.X + offset, at.Y + size.Height);
                }
            }

            bool[] logic = new bool[count];
            HashSet<int>[] partners = Enumerable.Range(0, count).Select(o => new HashSet<int>()).ToArray();
            foreach (Connection c in connections)
            {
                if (!Valid(c)) continue;
                partners[c.From].Add(c.To);
                partners[c.To].Add(c.From);
                if (!IsData(c))
                    logic[c.From] = logic[c.To] = true;
            }

            Dictionary<(int, int), Split> splits = new Dictionary<(int, int), Split>();
            for (int i = 0; i < connections.Count; i++)
            {
                Connection c = connections[i];
                if (!Valid(c) || !IsData(c))
                    continue;
                PointF a = Pin(c.From, c.FromSide, c.FromOffset), b = Pin(c.To, c.ToSide, c.ToOffset);
                if (Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) <= longerThan)
                    continue;
                int upper = c.FromSide == PinSide.Bottom ? c.From : c.To;
                int lower = upper == c.From ? c.To : c.From;
                int node = !logic[lower] && logic[upper] ? lower : upper;
                int partner = node == upper ? lower : upper;
                if (!logic[node] && partners[node].Count < 2)
                {
                    if (!logic[partner] && partners[partner].Count < 2)
                        continue;
                    (node, partner) = (partner, node);
                }
                if (!splits.TryGetValue((node, partner), out Split s))
                    splits.Add((node, partner), s = new Split() { Node = node, Partner = partner });
                s.Connections.Add(i);
            }

            //A node with only data connections must keep one partner: if all of them would go, the first stays
            foreach (IGrouping<int, Split> byNode in splits.Values.GroupBy(o => o.Node).ToList())
                if (!logic[byNode.Key] && byNode.Count() >= partners[byNode.Key].Count)
                    splits.Remove((byNode.Key, byNode.Min(o => o.Partner)));

            return splits.Values.OrderBy(o => o.Node).ThenBy(o => o.Partner).ToList();
        }

        /// <summary>
        /// Stored nodes in an order of the arranger's own rather than the page's (the editor saves nodes in the order it
        /// draws them, which clicking one changes): by entity, then by what each node connects to, then where it is, for twins.
        /// </summary>
        private static List<NodeMeta> OwnOrder(List<NodeMeta> placing)
        {
            Dictionary<NodeMeta, List<string>> joins = placing.ToDictionary(o => o, o => new List<string>());
            Dictionary<(int, ShortGuid), NodeMeta> byId = new Dictionary<(int, ShortGuid), NodeMeta>();
            foreach (NodeMeta node in placing)
                byId[(node.NodeID, node.EntityGUID)] = node;
            foreach (NodeMeta node in placing)
            {
                foreach (ConnectionMeta connection in node.ConnectionsOut)
                {
                    if (!byId.TryGetValue((connection.ConnectedNodeID, connection.ConnectedEntityGUID), out NodeMeta target))
                        continue;
                    joins[node].Add(connection.ParameterGUID.AsUInt32 + ">" + connection.ConnectedEntityGUID.AsUInt32 + "." + connection.ConnectedParameterGUID.AsUInt32);
                    joins[target].Add(connection.ConnectedParameterGUID.AsUInt32 + ">" + node.EntityGUID.AsUInt32 + "." + connection.ParameterGUID.AsUInt32);
                }
            }
            return placing.OrderBy(o => o.EntityGUID.AsUInt32).ThenBy(o => string.Join(";", joins[o].OrderBy(s => s, StringComparer.Ordinal)), StringComparer.Ordinal)
                .ThenBy(o => o.Position.Y).ThenBy(o => o.Position.X).ToList();
        }

        /// <summary>Whether the flowgraph draws a connection between pins on these sides (right to left, or top to bottom, either way round).</summary>
        public static bool Joins(PinSide a, PinSide b)
        {
            return (a == PinSide.Right && b == PinSide.Left) || (a == PinSide.Left && b == PinSide.Right) ||
                   (a == PinSide.Top && b == PinSide.Bottom) || (a == PinSide.Bottom && b == PinSide.Top);
        }

        /// <summary>
        /// The side the flowgraph editor shows a parameter's pin on, from the script alone: a variable's by its pin
        /// type, a method on the left with its relay on the right, data parameters on top, reference pins below.
        /// Null if the entity has no such parameter.
        /// </summary>
        public static PinSide? SideOf(Commands commands, Composite composite, Entity entity, ShortGuid parameter)
        {
            if (commands == null || composite == null || entity == null)
                return null;

            if (entity is VariableEntity variable)
            {
                if (variable.name != parameter)
                    return null;
                CompositePinInfoTable.PinInfo info = commands.Utils.GetPinInfo(composite, variable);
                if (info == null)
                    return PinSide.Top;
                switch (info.PinTypeGUID.AsCompositePinType)
                {
                    case CompositePinType.CompositeMethodPin: return PinSide.Right;
                    case CompositePinType.CompositeTargetPin: return PinSide.Left;
                    case CompositePinType.CompositeReferencePin: return PinSide.Top;
                    default: return PinSide.Bottom;
                }
            }

            List<(ShortGuid, ParameterVariant, DataType)> parameters;
            try { parameters = commands.Utils.GetAllParameters(entity, composite); }
            catch { return null; }
            foreach ((ShortGuid id, ParameterVariant variant, DataType type) in parameters)
            {
                if (id == parameter)
                {
                    switch (variant)
                    {
                        case ParameterVariant.INPUT_PIN:
                        case ParameterVariant.PARAMETER:
                        case ParameterVariant.STATE_PARAMETER:
                        case ParameterVariant.OUTPUT_PIN:
                            return PinSide.Top;
                        case ParameterVariant.METHOD_PIN:
                            return PinSide.Left;
                        case ParameterVariant.TARGET_PIN:
                            return PinSide.Right;
                        case ParameterVariant.REFERENCE_PIN:
                            return PinSide.Bottom;
                    }
                }
                if (variant == ParameterVariant.METHOD_PIN && commands.Utils.GetRelay(id) == parameter)
                    return PinSide.Right;
            }
            if (entity is TriggerSequence sequence)
            {
                foreach (TriggerSequence.MethodEntry method in sequence.methods)
                {
                    if (method.method == parameter) return PinSide.Left;
                    if (method.relay == parameter || method.finished == parameter) return PinSide.Right;
                }
            }
            if (entity is CAGEAnimation animation)
            {
                foreach (CAGEAnimation.EventTrack track in animation.eventTracks)
                    foreach (CAGEAnimation.EventTrack.Keyframe keyframe in track.keyframes)
                        if (keyframe.forward == parameter || keyframe.reverse == parameter)
                            return PinSide.Right;
            }
            return null;
        }

        /// <summary>
        /// Roughly the size the flowgraph editor draws a node showing these pins at (rows of 20 px, a title with its
        /// type under it, a row above for top pins and below for bottom pins), and each pin's offset along its side.
        /// </summary>
        public static Size EstimateSize(string title, IEnumerable<(ShortGuid pin, PinSide side)> pins, Dictionary<(ShortGuid, PinSide), int> offsets = null)
        {
            List<(ShortGuid pin, PinSide side)> distinct = pins.Distinct().ToList();
            List<ShortGuid> lefts = distinct.Where(o => o.side == PinSide.Left).Select(o => o.pin).ToList();
            List<ShortGuid> rights = distinct.Where(o => o.side == PinSide.Right).Select(o => o.pin).ToList();
            List<ShortGuid> tops = distinct.Where(o => o.side == PinSide.Top).Select(o => o.pin).ToList();
            List<ShortGuid> bottoms = distinct.Where(o => o.side == PinSide.Bottom).Select(o => o.pin).ToList();

            int TextWidth(ShortGuid pin) => Math.Min(100, (int)Math.Ceiling((pin.ToString()?.Length ?? 8) * 6.2));
            int topSpace = tops.Count > 0 ? 20 : 0;
            int bottomSpace = bottoms.Count > 0 ? 20 : 0;
            int height = topSpace + 35 + Math.Max(lefts.Count, rights.Count) * 20 + bottomSpace;
            int topWidth = tops.Sum(o => TextWidth(o) + 15);
            int bottomWidth = bottoms.Sum(o => TextWidth(o) + 25);
            int sideWidth = (lefts.Count == 0 ? 0 : lefts.Max(TextWidth)) + (rights.Count == 0 ? 0 : rights.Max(TextWidth)) + 40;
            int width = Math.Max(Math.Max(110, (int)Math.Ceiling((title?.Length ?? 0) * 6.5) + 30), Math.Max(sideWidth, Math.Max(topWidth, bottomWidth)));

            if (offsets != null)
            {
                for (int i = 0; i < lefts.Count; i++) offsets[(lefts[i], PinSide.Left)] = topSpace + 35 + i * 20 + 10;
                for (int i = 0; i < rights.Count; i++) offsets[(rights[i], PinSide.Right)] = topSpace + 35 + i * 20 + 10;
                int x = (width - topWidth) / 2;
                foreach (ShortGuid pin in tops) { int w = TextWidth(pin) + 15; offsets[(pin, PinSide.Top)] = x + w / 2; x += w; }
                x = (width - bottomWidth) / 2;
                foreach (ShortGuid pin in bottoms) { int w = TextWidth(pin) + 25; offsets[(pin, PinSide.Bottom)] = x + w / 2; x += w; }
            }
            return new Size(width, height);
        }

        private static string Title(Entity entity)
        {
            string name = CommandsUtils.GetEntityNameParameter(entity);
            if (!string.IsNullOrEmpty(name))
                return name;
            if (entity is FunctionEntity function)
                return function.function.ToString();
            return entity.variant.ToString();
        }

        #region Connections, as the layout sees them

        /// <summary>A logic connection: out of a right pin on From, into a left pin on To.</summary>
        private struct Logic
        {
            public int From, FromOffset, To, ToOffset;
            public Logic(int from, int fromOffset, int to, int toOffset) { From = from; FromOffset = fromOffset; To = to; ToOffset = toOffset; }
        }

        /// <summary>A data connection: up out of a top pin on Lower, into a bottom pin on Upper.</summary>
        private struct Data
        {
            public int Lower, LowerOffset, Upper, UpperOffset;
            public Data(int lower, int lowerOffset, int upper, int upperOffset) { Lower = lower; LowerOffset = lowerOffset; Upper = upper; UpperOffset = upperOffset; }
        }

        #endregion

        #region One group of connected nodes

        /// <summary>A laid out group (or a single node): its nodes' positions relative to its top-left, and its size.</summary>
        private sealed class Placed
        {
            public List<int> Nodes;
            public Point[] Positions;
            public Size Size;
            public int First;                       //the lowest node index in it, for ordering
        }

        /// <summary>
        /// Something placed in a column: a node (with the data nodes that hang above and below it), or the
        /// placeholder a connection passes through on its way to a column further on.
        /// </summary>
        private sealed class Item
        {
            public int Layer;
            public int Width, Height;
            public bool Placeholder;
            public int Node = -1;                   //the node, for a block
            public Point NodeAt;                    //its top-left within the block
            public List<(int node, Point at)> Hung = new List<(int, Point)>();
            public int Index = int.MaxValue;        //the node's index, for ordering (placeholders last)
            public double Rank;
            public double Y;                        //top, while heights are worked out
            public int Order;                       //place in its column
            public List<Link> Before = new List<Link>(), After = new List<Link>();
            public List<Pull> Pulls = new List<Pull>();
        }

        /// <summary>
        /// A data connection between two column nodes: it cannot choose their columns, but it draws the node with the
        /// bottom pin to just above the one with the top pin, as far as their other connections allow.
        /// </summary>
        private struct Pull
        {
            public Item Other;
            public int Want;                        //this item's top less the other's, for the pair to sit as drawn
        }

        /// <summary>A connection between items in different columns, by pin height within each.</summary>
        private sealed class Link
        {
            public Item From, To;
            public int FromOffset, ToOffset;
            public double Weight;
            public bool Adjacent => To.Layer == From.Layer + 1;
        }

        private static Placed LayOutGroup(IReadOnlyList<Node> nodes, List<int> group, List<Logic> logic, List<Data> data)
        {
            //A group joined only by data connections reads top to bottom: lay it out turned on its side, then turn it back
            if (logic.Count == 0)
            {
                List<Node> turned = nodes.Select(o => new Node() { Size = new Size(o.Size.Height, o.Size.Width) }).ToList();
                Placed sideways = LayOutLayers(turned, group, data.Select(o => new Logic(o.Upper, o.UpperOffset, o.Lower, o.LowerOffset)).ToList(), new List<Data>());
                return new Placed()
                {
                    Nodes = sideways.Nodes,
                    Positions = sideways.Positions.Select(o => new Point(o.Y, o.X)).ToArray(),
                    Size = new Size(sideways.Size.Height, sideways.Size.Width),
                    First = sideways.First,
                };
            }
            return LayOutLayers(nodes, group, logic, data);
        }

        private static Placed LayOutLayers(IReadOnlyList<Node> nodes, List<int> group, List<Logic> logic, List<Data> data)
        {
            //Nodes with logic connections take columns; the rest hang off them
            HashSet<int> core = new HashSet<int>();
            foreach (Logic l in logic) { core.Add(l.From); core.Add(l.To); }
            List<int> coreNodes = group.Where(core.Contains).ToList();

            Dictionary<int, int> layer = AssignLayers(nodes, coreNodes, logic, out List<(Logic link, bool turned)> dag);

            //Blocks: one per column node, data-only nodes hung above or below one of the nodes they connect to
            Dictionary<int, Item> blockOf = new Dictionary<int, Item>();
            List<Item> items = new List<Item>();
            foreach (int n in coreNodes)
            {
                Item block = new Item() { Layer = layer[n], Node = n, Index = n, Width = nodes[n].Size.Width, Height = nodes[n].Size.Height };
                blockOf[n] = block;
                items.Add(block);
            }
            HangDataNodes(nodes, group, core, layer, data, blockOf);

            //Data connections between column nodes: the upper node (bottom pin) wants to sit a data row's gap above the lower (top pin)
            foreach (Data d in data)
            {
                if (!blockOf.TryGetValue(d.Upper, out Item upper) || !blockOf.TryGetValue(d.Lower, out Item lower) || upper == lower)
                    continue;
                int upperBottom = upper.NodeAt.Y + nodes[d.Upper].Size.Height, lowerTop = lower.NodeAt.Y;
                upper.Pulls.Add(new Pull() { Other = lower, Want = lowerTop - DataRowGap - upperBottom });
                lower.Pulls.Add(new Pull() { Other = upper, Want = upperBottom + DataRowGap - lowerTop });
            }

            //Placeholders for connections that skip columns
            List<Link> links = new List<Link>();
            int budget = Math.Max(2000, coreNodes.Count * 8);
            foreach ((Logic l, bool turned) in dag.OrderBy(o => Math.Abs(layer[o.link.To] - layer[o.link.From])))
            {
                Item a = blockOf[turned ? l.To : l.From];
                Item b = blockOf[turned ? l.From : l.To];
                int aOffset = a.NodeAt.Y + (turned ? l.ToOffset : l.FromOffset);
                int bOffset = b.NodeAt.Y + (turned ? l.FromOffset : l.ToOffset);
                int span = b.Layer - a.Layer;
                if (span > 1 && budget >= span - 1)
                {
                    budget -= span - 1;
                    Item previous = a;
                    int previousOffset = aOffset;
                    for (int k = 1; k < span; k++)
                    {
                        Item placeholder = new Item() { Layer = a.Layer + k, Placeholder = true, Width = 0, Height = PlaceholderHeight };
                        items.Add(placeholder);
                        links.Add(new Link() { From = previous, FromOffset = previousOffset, To = placeholder, ToOffset = PlaceholderHeight / 2, Weight = previous.Placeholder ? 8 : 2 });
                        previous = placeholder;
                        previousOffset = PlaceholderHeight / 2;
                    }
                    links.Add(new Link() { From = previous, FromOffset = previousOffset, To = b, ToOffset = bOffset, Weight = 2 });
                }
                else
                {
                    links.Add(new Link() { From = a, FromOffset = aOffset, To = b, ToOffset = bOffset, Weight = span > 1 ? 0.5 : 1 });
                }
            }
            foreach (Link link in links)
            {
                link.From.After.Add(link);
                link.To.Before.Add(link);
            }

            int layerCount = items.Max(o => o.Layer) + 1;
            List<Item>[] columns = Enumerable.Range(0, layerCount).Select(o => new List<Item>()).ToList().ToArray();
            foreach (Item item in items)
                columns[item.Layer].Add(item);

            //Two starting orders - a walk along the connections from the left, and one back from the right - each swept
            //to as few crossings as it gets to; the one that crosses less is kept
            List<Item>[] best = null;
            long fewest = long.MaxValue;
            foreach (bool forwards in new[] { true, false })
            {
                SeedRanks(items, forwards);
                List<Item>[] trial = columns.Select(o => o.OrderBy(i => i.Rank).ToList()).ToArray();
                foreach (List<Item> column in trial)
                    Restack(column);
                long crossings = Order(trial);
                if (crossings < fewest)
                {
                    best = trial;
                    fewest = crossings;
                }
            }
            columns = best;
            foreach (List<Item> column in columns)
                Restack(column);
            Heights(columns);

            //Columns left to right, wider gaps where more connections pass
            int[] columnX = new int[layerCount];
            for (int i = 1; i < layerCount; i++)
            {
                int width = columns[i - 1].Count == 0 ? 0 : columns[i - 1].Max(o => o.Width);
                int crossing = columns[i - 1].Sum(o => o.After.Count);
                columnX[i] = columnX[i - 1] + width + Math.Min(ColumnGapMax, ColumnGap + (int)(ColumnGapPerConnection * Math.Sqrt(crossing) * 3));
            }

            Dictionary<int, Point> at = new Dictionary<int, Point>();
            foreach (Item item in items)
            {
                if (item.Placeholder)
                    continue;
                Point topLeft = new Point(columnX[item.Layer], (int)Math.Round(item.Y));
                at[item.Node] = new Point(topLeft.X + item.NodeAt.X, topLeft.Y + item.NodeAt.Y);
                foreach ((int node, Point offset) in item.Hung)
                    at[node] = new Point(topLeft.X + offset.X, topLeft.Y + offset.Y);
            }
            return Finish(nodes, group, at);
        }

        /// <summary>Positions relative to the group's top-left, and its size.</summary>
        private static Placed Finish(IReadOnlyList<Node> nodes, List<int> group, Dictionary<int, Point> at)
        {
            int minX = group.Min(o => at[o].X), minY = group.Min(o => at[o].Y);
            int maxX = group.Max(o => at[o].X + nodes[o].Size.Width), maxY = group.Max(o => at[o].Y + nodes[o].Size.Height);
            return new Placed()
            {
                Nodes = group,
                Positions = group.Select(o => new Point(at[o].X - minX, at[o].Y - minY)).ToArray(),
                Size = new Size(maxX - minX, maxY - minY),
                First = group.Min(),
            };
        }

        /// <summary>
        /// A column for every node with logic connections: cycles broken by turning round as few connections as can
        /// be found (preferring ones already drawn right to left), the longest chain of connections into each node
        /// setting its column, then each node pulled as far towards its connections as the others allow.
        /// </summary>
        private static Dictionary<int, int> AssignLayers(IReadOnlyList<Node> nodes, List<int> coreNodes, List<Logic> logic, out List<(Logic link, bool turned)> dag)
        {
            //Weighted adjacency, multiple connections between a pair counted once each
            Dictionary<int, Dictionary<int, int>> outs = coreNodes.ToDictionary(o => o, o => new Dictionary<int, int>());
            Dictionary<int, Dictionary<int, int>> ins = coreNodes.ToDictionary(o => o, o => new Dictionary<int, int>());
            foreach (Logic l in logic)
            {
                outs[l.From][l.To] = (outs[l.From].TryGetValue(l.To, out int a) ? a : 0) + 1;
                ins[l.To][l.From] = (ins[l.To].TryGetValue(l.From, out int b) ? b : 0) + 1;
            }

            //Eades-Lin-Smyth: peel sinks to the back and sources to the front; otherwise the node with most out over in
            Dictionary<int, int> outWeight = coreNodes.ToDictionary(o => o, o => outs[o].Sum(p => p.Key == o ? 0 : p.Value));
            Dictionary<int, int> inWeight = coreNodes.ToDictionary(o => o, o => ins[o].Sum(p => p.Key == o ? 0 : p.Value));
            HashSet<int> left = new HashSet<int>(coreNodes);
            List<int> front = new List<int>(), back = new List<int>();
            void Take(int n)
            {
                left.Remove(n);
                foreach (KeyValuePair<int, int> o in outs[n]) if (left.Contains(o.Key)) inWeight[o.Key] -= o.Value;
                foreach (KeyValuePair<int, int> i in ins[n]) if (left.Contains(i.Key)) outWeight[i.Key] -= i.Value;
            }
            //Ties go to the earlier node in the page's order
            List<int> byPlace = coreNodes.OrderBy(o => o).ToList();
            while (left.Count != 0)
            {
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (int n in byPlace)
                    {
                        if (!left.Contains(n) || outWeight[n] != 0) continue;
                        back.Add(n); Take(n); changed = true;
                    }
                    foreach (int n in byPlace)
                    {
                        if (!left.Contains(n) || inWeight[n] != 0) continue;
                        front.Add(n); Take(n); changed = true;
                    }
                }
                if (left.Count == 0)
                    break;
                int best = -1, bestScore = int.MinValue;
                foreach (int n in byPlace)
                {
                    if (!left.Contains(n)) continue;
                    int score = outWeight[n] - inWeight[n];
                    if (score > bestScore) { best = n; bestScore = score; }
                }
                front.Add(best); Take(best);
            }
            back.Reverse();
            Dictionary<int, int> rank = new Dictionary<int, int>();
            foreach (int n in front.Concat(back)) rank[n] = rank.Count;

            dag = new List<(Logic, bool)>();
            Dictionary<int, List<int>> successors = coreNodes.ToDictionary(o => o, o => new List<int>());
            Dictionary<int, List<int>> predecessors = coreNodes.ToDictionary(o => o, o => new List<int>());
            foreach (Logic l in logic)
            {
                bool turned = rank[l.From] > rank[l.To];
                dag.Add((l, turned));
                int a = turned ? l.To : l.From, b = turned ? l.From : l.To;
                successors[a].Add(b);
                predecessors[b].Add(a);
            }

            //Longest path in
            Dictionary<int, int> layer = coreNodes.ToDictionary(o => o, o => 0);
            foreach (int n in coreNodes.OrderBy(o => rank[o]))
                foreach (int p in predecessors[n])
                    layer[n] = Math.Max(layer[n], layer[p] + 1);

            //Pull each node towards the side it has more connections on, within what its neighbours allow
            int maxLayer = layer.Values.Max();
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                foreach (int n in coreNodes.OrderByDescending(o => rank[o]))
                {
                    int ins_ = predecessors[n].Count, outs_ = successors[n].Count;
                    if (ins_ == outs_) continue;
                    int lo = predecessors[n].Count == 0 ? 0 : predecessors[n].Max(o => layer[o]) + 1;
                    int hi = successors[n].Count == 0 ? maxLayer : successors[n].Min(o => layer[o]) - 1;
                    int want = outs_ > ins_ ? hi : lo;
                    if (want != layer[n] && want >= lo && want <= hi)
                    {
                        layer[n] = want;
                        moved = true;
                    }
                }
                if (!moved) break;
            }
            int min = layer.Values.Min();
            foreach (int n in coreNodes) layer[n] -= min;
            return layer;
        }

        /// <summary>
        /// Hang each data-only node off a column node it connects to (the middle one by column, if several): above it
        /// if the node feeds it from a top pin, below if it feeds the node. Each row is placed over the pins it joins.
        /// </summary>
        private static void HangDataNodes(IReadOnlyList<Node> nodes, List<int> group, HashSet<int> core, Dictionary<int, int> layer, List<Data> data, Dictionary<int, Item> blockOf)
        {
            //Where each hung node sits: which block, above or below, which row, and the pin x it lines up under/over
            Dictionary<int, (Item block, bool above, int row, int targetX, int ownOffset)> hung = new Dictionary<int, (Item, bool, int, int, int)>();
            List<int> pending = group.Where(o => !core.Contains(o)).OrderBy(o => o).ToList();

            while (pending.Count != 0)
            {
                bool progress = false;
                foreach (int n in pending.ToList())
                {
                    //Partners already placed: column nodes first
                    List<(int other, bool nIsUpper, int otherOffset, int ownOffset)> partners = new List<(int, bool, int, int)>();
                    foreach (Data d in data)
                    {
                        if (d.Upper == n && d.Lower != n) partners.Add((d.Lower, true, d.LowerOffset, d.UpperOffset));
                        else if (d.Lower == n && d.Upper != n) partners.Add((d.Upper, false, d.UpperOffset, d.LowerOffset));
                    }
                    List<(int other, bool nIsUpper, int otherOffset, int ownOffset)> toColumns = partners.Where(o => blockOf.ContainsKey(o.other)).ToList();
                    if (toColumns.Count != 0)
                    {
                        List<int> byLayer = toColumns.Select(o => o.other).Distinct().OrderBy(o => layer[o]).ThenBy(o => o).ToList();
                        int anchor = byLayer[(byLayer.Count - 1) / 2];
                        List<(int other, bool nIsUpper, int otherOffset, int ownOffset)> toAnchor = toColumns.Where(o => o.other == anchor).ToList();
                        bool above = toAnchor.Count(o => o.nIsUpper) * 2 >= toAnchor.Count;
                        (int other, bool nIsUpper, int otherOffset, int ownOffset) first = toAnchor.First(o => o.nIsUpper == above);
                        hung[n] = (blockOf[anchor], above, 0, first.otherOffset, first.ownOffset);
                        pending.Remove(n);
                        progress = true;
                        continue;
                    }
                    (int other, bool nIsUpper, int otherOffset, int ownOffset) viaHung = partners.FirstOrDefault(o => hung.ContainsKey(o.other));
                    if (partners.Any(o => hung.ContainsKey(o.other)))
                    {
                        var parent = hung[viaHung.other];
                        hung[n] = (parent.block, parent.above, parent.row + 1, -1 - viaHung.other, viaHung.ownOffset); //target x resolved once the parent is placed
                        pending.Remove(n);
                        progress = true;
                    }
                }
                if (!progress)
                {
                    //Joined to nothing placed (should not happen in a connected group): hang above the first column node
                    Item fallback = blockOf.Values.First();
                    foreach (int n in pending)
                        hung[n] = (fallback, true, 0, fallback.Width / 2, nodes[n].Size.Width / 2);
                    pending.Clear();
                }
            }

            //Place rows, block by block, nearest row first, each node centred on its pin as far as its neighbours allow
            foreach (IGrouping<Item, KeyValuePair<int, (Item block, bool above, int row, int targetX, int ownOffset)>> byBlock in hung.GroupBy(o => o.Value.block))
            {
                Item block = byBlock.Key;
                Size anchorSize = nodes[block.Node].Size;
                Dictionary<int, Point> local = new Dictionary<int, Point>(); //relative to the column node's top-left
                foreach (bool above in new[] { true, false })
                {
                    int edge = above ? 0 : anchorSize.Height;
                    foreach (IGrouping<int, KeyValuePair<int, (Item block, bool above, int row, int targetX, int ownOffset)>> row in byBlock.Where(o => o.Value.above == above).GroupBy(o => o.Value.row).OrderBy(o => o.Key))
                    {
                        List<(int node, double want)> wants = row.Select(o =>
                        {
                            int targetX = o.Value.targetX;
                            if (targetX < 0)
                            {
                                int via = -1 - targetX;
                                targetX = local.TryGetValue(via, out Point p) ? p.X + nodes[via].Size.Width / 2 : anchorSize.Width / 2;
                            }
                            return (o.Key, (double)(targetX - o.Value.ownOffset));
                        }).OrderBy(o => o.Item2).ThenBy(o => o.Key).ToList();
                        double[] xs = Settle(wants.Select(o => o.want).ToArray(), wants.Select(o => 1.0).ToArray(), wants.Select(o => (double)nodes[o.node].Size.Width).ToArray(), DataNodeGap);
                        int rowHeight = wants.Max(o => nodes[o.node].Size.Height);
                        for (int i = 0; i < wants.Count; i++)
                        {
                            int n = wants[i].node;
                            int y = above ? edge - DataRowGap - nodes[n].Size.Height : edge + DataRowGap;
                            local[n] = new Point((int)Math.Round(xs[i]), y);
                        }
                        edge = above ? edge - DataRowGap - rowHeight : edge + DataRowGap + rowHeight;
                    }
                }

                int minX = Math.Min(0, local.Values.Min(o => o.X));
                int minY = Math.Min(0, local.Values.Min(o => o.Y));
                int maxX = Math.Max(anchorSize.Width, local.Max(o => o.Value.X + nodes[o.Key].Size.Width));
                int maxY = Math.Max(anchorSize.Height, local.Max(o => o.Value.Y + nodes[o.Key].Size.Height));
                block.NodeAt = new Point(-minX, -minY);
                block.Width = maxX - minX;
                block.Height = maxY - minY;
                foreach (KeyValuePair<int, Point> p in local)
                    block.Hung.Add((p.Key, new Point(p.Value.X - minX, p.Value.Y - minY)));
            }
        }

        /// <summary>A starting order down each column: the order a walk along the connections meets things, from the left (or back from the right).</summary>
        private static void SeedRanks(List<Item> items, bool forwards)
        {
            HashSet<Item> seen = new HashSet<Item>();
            int next = 0;
            foreach (Item start in (forwards ? items.OrderBy(o => o.Layer) : items.OrderByDescending(o => o.Layer)).ThenBy(o => o.Index))
            {
                if (seen.Contains(start)) continue;
                Stack<Item> stack = new Stack<Item>();
                stack.Push(start);
                while (stack.Count != 0)
                {
                    Item item = stack.Pop();
                    if (!seen.Add(item)) continue;
                    item.Rank = next++;
                    if (forwards)
                    {
                        foreach (Link link in item.After.OrderByDescending(o => o.FromOffset))
                            if (!seen.Contains(link.To)) stack.Push(link.To);
                    }
                    else
                    {
                        foreach (Link link in item.Before.OrderByDescending(o => o.ToOffset))
                            if (!seen.Contains(link.From)) stack.Push(link.From);
                    }
                }
            }
        }

        /// <summary>Tops for a column in its current order, packed from 0.</summary>
        private static void Restack(List<Item> column)
        {
            double y = 0;
            for (int i = 0; i < column.Count; i++)
            {
                column[i].Order = i;
                column[i].Y = y;
                y += column[i].Height + (i + 1 < column.Count ? Gap(column[i], column[i + 1]) : 0);
            }
        }

        private static int Gap(Item a, Item b) => a.Placeholder || b.Placeholder ? PlaceholderGap : RowGap;

        /// <summary>
        /// Reorder columns to cut crossings: sweep right then left, sorting each column by where its connections come
        /// from (pin heights in the column swept from), keeping the best order seen; then swap neighbours while that helps.
        /// Returns the crossings left.
        /// </summary>
        private static long Order(List<Item>[] columns)
        {
            List<Item>[] best = columns.Select(o => o.ToList()).ToArray();
            long bestCrossings = Crossings(columns);
            int stale = 0;
            for (int sweep = 0; sweep < MaxSweeps && bestCrossings > 0; sweep++)
            {
                for (int i = 1; i < columns.Length; i++)
                    SortBy(columns, i, before: true);
                for (int i = columns.Length - 2; i >= 0; i--)
                    SortBy(columns, i, before: false);
                long crossings = Crossings(columns);
                if (crossings < bestCrossings)
                {
                    bestCrossings = crossings;
                    best = columns.Select(o => o.ToList()).ToArray();
                    stale = 0;
                }
                else if (++stale >= 4)
                    break;
            }
            for (int i = 0; i < columns.Length; i++)
            {
                columns[i] = best[i];
                Restack(columns[i]);
            }

            //Swap neighbours where that crosses less
            for (int pass = 0; pass < 6; pass++)
            {
                bool swapped = false;
                for (int i = 0; i < columns.Length; i++)
                {
                    List<Item> column = columns[i];
                    for (int k = 0; k + 1 < column.Count; k++)
                    {
                        Item a = column[k], b = column[k + 1];
                        long now = PairCrossings(a, b);
                        long flipped = PairCrossings(b, a);
                        if (flipped < now)
                        {
                            column[k] = b;
                            column[k + 1] = a;
                            a.Order = k + 1;
                            b.Order = k;
                            swapped = true;
                        }
                    }
                }
                if (!swapped) break;
            }
            foreach (List<Item> column in columns)
                Restack(column);
            return Crossings(columns);
        }

        /// <summary>Sort a column by the average pin height of what its items connect to on one side (items with nothing there keep their height).</summary>
        private static void SortBy(List<Item>[] columns, int index, bool before)
        {
            List<Item> column = columns[index];
            Dictionary<Item, double> key = new Dictionary<Item, double>();
            foreach (Item item in column)
            {
                double sum = 0, weight = 0;
                foreach (Link link in before ? item.Before : item.After)
                {
                    Item other = before ? link.From : link.To;
                    double otherPin = other.Y + (before ? link.FromOffset : link.ToOffset);
                    double ownPin = before ? link.ToOffset : link.FromOffset;
                    sum += (otherPin - ownPin + item.Height / 2.0) * link.Weight;
                    weight += link.Weight;
                }
                //A data connection to a node in a column on the side swept from counts too, at the height it wants
                foreach (Pull pull in item.Pulls)
                {
                    if (pull.Other.Layer == item.Layer || (pull.Other.Layer < item.Layer) != before)
                        continue;
                    sum += (pull.Other.Y + pull.Want + item.Height / 2.0) * DataPull;
                    weight += DataPull;
                }
                key[item] = weight > 0 ? sum / weight : item.Y + item.Height / 2.0;
            }
            columns[index] = column.OrderBy(o => key[o]).ThenBy(o => o.Order).ToList();
            Restack(columns[index]);
        }

        /// <summary>Crossings between each pair of neighbouring columns, counting pins in order down each node.</summary>
        private static long Crossings(List<Item>[] columns)
        {
            long total = 0;
            for (int i = 0; i + 1 < columns.Length; i++)
            {
                List<(long a, long b)> pairs = new List<(long, long)>();
                foreach (Item item in columns[i])
                    foreach (Link link in item.After)
                        if (link.Adjacent)
                            pairs.Add((item.Order * 100000L + link.FromOffset, link.To.Order * 100000L + link.ToOffset));
                pairs.Sort((x, y) => x.a != y.a ? x.a.CompareTo(y.a) : x.b.CompareTo(y.b));
                total += Inversions(pairs.Select(o => o.b).ToArray());
            }
            return total;
        }

        private static long Inversions(long[] values)
        {
            if (values.Length < 2) return 0;
            long[] buffer = new long[values.Length];
            long Count(int lo, int hi)
            {
                if (hi - lo < 2) return 0;
                int mid = (lo + hi) / 2;
                long n = Count(lo, mid) + Count(mid, hi);
                int i = lo, j = mid, k = lo;
                while (i < mid && j < hi)
                {
                    if (values[j] < values[i]) { n += mid - i; buffer[k++] = values[j++]; }
                    else buffer[k++] = values[i++];
                }
                while (i < mid) buffer[k++] = values[i++];
                while (j < hi) buffer[k++] = values[j++];
                Array.Copy(buffer, lo, values, lo, hi - lo);
                return n;
            }
            return Count(0, values.Length);
        }

        /// <summary>Crossings among the connections of two neighbours in a column, with <paramref name="upper"/> placed above <paramref name="lower"/>.</summary>
        private static long PairCrossings(Item upper, Item lower)
        {
            long n = 0;
            foreach (bool before in new[] { true, false })
            {
                foreach (Link a in before ? upper.Before : upper.After)
                {
                    if (!a.Adjacent) continue;
                    Item aOther = before ? a.From : a.To;
                    long aKey = aOther.Order * 100000L + (before ? a.FromOffset : a.ToOffset);
                    foreach (Link b in before ? lower.Before : lower.After)
                    {
                        if (!b.Adjacent) continue;
                        Item bOther = before ? b.From : b.To;
                        long bKey = bOther.Order * 100000L + (before ? b.FromOffset : b.ToOffset);
                        if (aKey > bKey) n++;
                    }
                }
            }
            return n;
        }

        /// <summary>
        /// Settle heights so connected pins line up: each column in turn moves its items towards the pins they connect
        /// to (on one side, then the other, then both), keeping their order and spacing.
        /// </summary>
        private static void Heights(List<Item>[] columns)
        {
            void Pass(int index, bool before, bool after)
            {
                List<Item> column = columns[index];
                if (column.Count == 0) return;
                double[] want = new double[column.Count], weight = new double[column.Count], height = new double[column.Count];
                for (int k = 0; k < column.Count; k++)
                {
                    Item item = column[k];
                    double sum = 0, w = 0;
                    if (before)
                        foreach (Link link in item.Before) { sum += (link.From.Y + link.FromOffset - link.ToOffset) * link.Weight; w += link.Weight; }
                    if (after)
                        foreach (Link link in item.After) { sum += (link.To.Y + link.ToOffset - link.FromOffset) * link.Weight; w += link.Weight; }
                    if (before && after)
                        foreach (Pull pull in item.Pulls) { sum += (pull.Other.Y + pull.Want) * DataPull; w += DataPull; }
                    want[k] = w > 0 ? sum / w : item.Y;
                    weight[k] = w > 0 ? w : 0.01;
                    height[k] = item.Height;
                }
                double[] gaps = new double[column.Count];
                for (int k = 0; k + 1 < column.Count; k++) gaps[k] = Gap(column[k], column[k + 1]);
                double[] ys = Settle(want, weight, height, gaps);
                for (int k = 0; k < column.Count; k++) column[k].Y = ys[k];
            }

            for (int round = 0; round < 4; round++)
            {
                for (int i = 1; i < columns.Length; i++) Pass(i, true, false);
                for (int i = columns.Length - 2; i >= 0; i--) Pass(i, false, true);
            }
            for (int round = 0; round < 4; round++)
                for (int i = 0; i < columns.Length; i++) Pass(i, true, true);
        }

        private static double[] Settle(double[] want, double[] weight, double[] size, double gap)
        {
            return Settle(want, weight, size, Enumerable.Repeat(gap, want.Length).ToArray());
        }

        /// <summary>
        /// Positions as close (least squares, weighted) to <paramref name="want"/> as they can be while keeping their
        /// order with each at least its size plus the gap after the one before: pool-adjacent-violators on the
        /// positions less the room everything before them needs.
        /// </summary>
        private static double[] Settle(double[] want, double[] weight, double[] size, double[] gaps)
        {
            int n = want.Length;
            double[] room = new double[n];
            for (int i = 1; i < n; i++) room[i] = room[i - 1] + size[i - 1] + gaps[i - 1];

            //Blocks of pooled values: weighted mean, total weight, how many
            double[] mean = new double[n], total = new double[n];
            int[] count = new int[n];
            int blocks = 0;
            for (int i = 0; i < n; i++)
            {
                mean[blocks] = want[i] - room[i];
                total[blocks] = weight[i];
                count[blocks] = 1;
                blocks++;
                while (blocks > 1 && mean[blocks - 2] > mean[blocks - 1])
                {
                    double w = total[blocks - 2] + total[blocks - 1];
                    mean[blocks - 2] = (mean[blocks - 2] * total[blocks - 2] + mean[blocks - 1] * total[blocks - 1]) / w;
                    total[blocks - 2] = w;
                    count[blocks - 2] += count[blocks - 1];
                    blocks--;
                }
            }
            double[] result = new double[n];
            int at = 0;
            for (int b = 0; b < blocks; b++)
                for (int k = 0; k < count[b]; k++, at++)
                    result[at] = mean[b] + room[at];
            return result;
        }

        #endregion

        #region Packing groups

        /// <summary>Shelves: groups left to right until the row is as wide as the widest group (or a page-ish width), then a new row.</summary>
        private static void Pack(List<Placed> placed, Point[] result)
        {
            if (placed.Count == 0) return;
            double area = placed.Sum(o => (double)(o.Size.Width + GroupGap) * (o.Size.Height + GroupGap));
            int width = Math.Max(placed.Max(o => o.Size.Width), (int)(Math.Sqrt(area) * 1.6));
            int x = 0, y = 0, shelf = 0;
            foreach (Placed group in placed)
            {
                if (x > 0 && x + group.Size.Width > width)
                {
                    x = 0;
                    y += shelf + GroupGap;
                    shelf = 0;
                }
                for (int i = 0; i < group.Nodes.Count; i++)
                    result[group.Nodes[i]] = new Point(x + group.Positions[i].X, y + group.Positions[i].Y);
                x += group.Size.Width + GroupGap;
                shelf = Math.Max(shelf, group.Size.Height);
            }
        }

        #endregion
    }
}
#endif
