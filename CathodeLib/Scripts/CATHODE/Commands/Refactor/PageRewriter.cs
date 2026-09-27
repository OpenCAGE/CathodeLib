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
    /// <summary>
    /// A suggestion for where to draw a link a refactor made: it stands in for a connection that was
    /// drawn between <see cref="From"/>'s owner and target, so it goes on that page, with any node it
    /// needs placed where that connection's nodes were.
    /// </summary>
    internal struct PageHint
    {
        public LinkKey Link;
        public LinkKey From;
    }

    /// <summary>Checks on script pages, for an editor keeping pages in step with links.</summary>
    public static class RefactorPages
    {
        /// <summary>
        /// Whether pages draw exactly a composite's links: every link to an existing entity drawn as many
        /// times as it occurs, and nothing else - the test an editor makes before it shows a composite's pages.
        /// </summary>
        public static bool PagesMatchLinks(Composite composite, IEnumerable<FlowgraphMeta> pages) => PageRewriter.PagesMatchLinks(composite, pages);

        /// <summary>
        /// Pages that draw exactly a composite's links, starting from the pages it has: what they already draw
        /// stays where it is, connections with no link behind them go, and every link not drawn is drawn beside
        /// a node of one of its ends, or else laid out in columns on the page named <paramref name="fallbackPageName"/>.
        /// The pages passed in are not changed. A composite with no links can come back with no pages.
        /// </summary>
        public static List<FlowgraphMeta> DrawLinks(Composite composite, IEnumerable<FlowgraphMeta> pages, string fallbackPageName)
        {
            PageRewriter rewriter = new PageRewriter(composite, pages ?? Enumerable.Empty<FlowgraphMeta>());
            rewriter.Reconcile(Enumerable.Empty<PageHint>(), fallbackPageName);
            return rewriter.Pages;
        }
    }

    /// <summary>
    /// Brings a composite's script pages back in step with its links after a refactor has changed them.
    /// </summary>
    /// <remarks>
    /// The editor compiles a composite's links from its pages, and only shows the pages at all while
    /// every link is drawn exactly once across them. So after a refactor the pages have to hold exactly
    /// the composite's links: connections whose link has gone are dropped, and every link not yet drawn
    /// is drawn - next to what it replaced where the refactor says what that was, otherwise beside a node
    /// of one of its ends, otherwise on a page of its own laid out in columns.
    /// </remarks>
    internal sealed class PageRewriter
    {
        private const int ColumnWidth = 420;
        private const int RowHeight = 200;

        private readonly Composite _composite;
        private readonly List<FlowgraphMeta> _pages;
        private readonly Dictionary<LinkKey, List<(FlowgraphMeta page, NodeMeta owner, NodeMeta target)>> _drawn = new Dictionary<LinkKey, List<(FlowgraphMeta, NodeMeta, NodeMeta)>>();

        public PageRewriter(Composite composite, IEnumerable<FlowgraphMeta> pages)
        {
            _composite = composite;
            _pages = pages.Select(o => Copy(o, composite.shortGUID)).ToList();
        }

        /// <summary>The pages as they stand.</summary>
        public List<FlowgraphMeta> Pages => _pages;

        /// <summary>Add a page (a copy of it), renamed if the composite already has one of that name.</summary>
        public FlowgraphMeta AddPage(FlowgraphMeta page, string preferredName)
        {
            FlowgraphMeta copy = Copy(page, _composite.shortGUID);
            copy.Name = UniqueName(preferredName);
            _pages.Add(copy);
            return copy;
        }

        /// <summary>Point every node (and connection) that names one entity at another instead.</summary>
        public void RemapEntities(IReadOnlyDictionary<ShortGuid, ShortGuid> map, FlowgraphMeta onlyPage = null)
        {
            foreach (FlowgraphMeta page in _pages)
            {
                if (onlyPage != null && page != onlyPage) continue;
                foreach (NodeMeta node in page.Nodes)
                {
                    if (map.TryGetValue(node.EntityGUID, out ShortGuid to)) node.EntityGUID = to;
                    foreach (ConnectionMeta connection in node.ConnectionsOut)
                        if (map.TryGetValue(connection.ConnectedEntityGUID, out ShortGuid target)) connection.ConnectedEntityGUID = target;
                }
            }
        }

        /// <summary>Record where every connection is drawn, before nodes are removed: hints are looked up in this.</summary>
        public void IndexDrawnConnections()
        {
            _drawn.Clear();
            foreach (FlowgraphMeta page in _pages)
            {
                Dictionary<int, NodeMeta> byId = NodesById(page);
                foreach (NodeMeta node in page.Nodes)
                {
                    foreach (ConnectionMeta connection in node.ConnectionsOut)
                    {
                        byId.TryGetValue(connection.ConnectedNodeID, out NodeMeta target);
                        LinkKey key = new LinkKey(node.EntityGUID, connection.ParameterGUID, connection.ConnectedEntityGUID, connection.ConnectedParameterGUID);
                        if (!_drawn.TryGetValue(key, out var list))
                            _drawn.Add(key, list = new List<(FlowgraphMeta, NodeMeta, NodeMeta)>());
                        list.Add((page, node, target));
                    }
                }
            }
        }

        /// <summary>Take out every node of these entities, and every connection into them.</summary>
        public void RemoveNodes(ICollection<ShortGuid> entities)
        {
            if (entities.Count == 0) return;
            foreach (FlowgraphMeta page in _pages)
            {
                HashSet<int> removed = new HashSet<int>(page.Nodes.Where(o => entities.Contains(o.EntityGUID)).Select(o => o.NodeID));
                if (removed.Count == 0) continue;
                page.Nodes.RemoveAll(o => removed.Contains(o.NodeID) && entities.Contains(o.EntityGUID));
                foreach (NodeMeta node in page.Nodes)
                    node.ConnectionsOut.RemoveAll(o => removed.Contains(o.ConnectedNodeID) && entities.Contains(o.ConnectedEntityGUID));
            }
        }

        /// <summary>
        /// Make the pages draw exactly the composite's links: drop connections with no link behind them,
        /// and draw every link that is not drawn, using the hints to decide where.
        /// </summary>
        public void Reconcile(IEnumerable<PageHint> hints, string fallbackPageName)
        {
            //What the data holds, as a count per link (to entities that exist - the editor ignores the rest)
            Dictionary<LinkKey, int> wanted = new Dictionary<LinkKey, int>();
            List<LinkKey> order = new List<LinkKey>();
            foreach (Entity entity in _composite.GetEntities())
            {
                foreach (EntityConnector link in entity.childLinks)
                {
                    if (_composite.GetEntityByID(link.linkedEntityID) == null) continue;
                    LinkKey key = new LinkKey(entity, link);
                    wanted.TryGetValue(key, out int count);
                    wanted[key] = count + 1;
                    order.Add(key);
                }
            }

            //Keep what is drawn and wanted, once per wanted copy; drop the rest (and nodes for entities that are gone)
            foreach (FlowgraphMeta page in _pages)
            {
                page.Nodes.RemoveAll(o => _composite.GetEntityByID(o.EntityGUID) == null);
                Dictionary<int, NodeMeta> byId = NodesById(page);
                foreach (NodeMeta node in page.Nodes)
                {
                    node.ConnectionsOut.RemoveAll(connection =>
                    {
                        if (!byId.TryGetValue(connection.ConnectedNodeID, out NodeMeta target) || target.EntityGUID != connection.ConnectedEntityGUID)
                            return true;
                        LinkKey key = new LinkKey(node.EntityGUID, connection.ParameterGUID, connection.ConnectedEntityGUID, connection.ConnectedParameterGUID);
                        if (!wanted.TryGetValue(key, out int count) || count == 0)
                            return true;
                        wanted[key] = count - 1;
                        return false;
                    });
                }
            }

            //Draw what is still wanted, in the order the data holds it
            Dictionary<LinkKey, Queue<PageHint>> hintsByLink = new Dictionary<LinkKey, Queue<PageHint>>();
            foreach (PageHint hint in hints)
            {
                if (!hintsByLink.TryGetValue(hint.Link, out var queue))
                    hintsByLink.Add(hint.Link, queue = new Queue<PageHint>());
                queue.Enqueue(hint);
            }

            List<LinkKey> unplaced = new List<LinkKey>();
            foreach (LinkKey key in order)
            {
                if (!wanted.TryGetValue(key, out int count) || count == 0) continue;
                wanted[key] = count - 1;
                if (!DrawFromHint(key, hintsByLink) && !DrawBesideExisting(key))
                    unplaced.Add(key);
            }
            if (unplaced.Count != 0)
                DrawOnFallbackPage(unplaced, fallbackPageName);
            OrderLikeData();
        }

        /// <summary>
        /// The editor rebuilds an entity's links from its node's connections, in their order. Where a drawn
        /// connection replaced one in the middle of a parameter's run, it was added at the end: put each
        /// parameter's connections back in the order the data has its links (the last one wins for a value),
        /// in the slots that parameter already had, so nothing else on the node moves.
        /// </summary>
        private void OrderLikeData()
        {
            foreach (FlowgraphMeta page in _pages)
            {
                foreach (NodeMeta node in page.Nodes)
                {
                    if (node.ConnectionsOut.Count < 2) continue;
                    Entity owner = _composite.GetEntityByID(node.EntityGUID);
                    if (owner == null) continue;
                    Dictionary<LinkKey, Queue<int>> rank = new Dictionary<LinkKey, Queue<int>>();
                    for (int i = 0; i < owner.childLinks.Count; i++)
                    {
                        LinkKey key = new LinkKey(owner, owner.childLinks[i]);
                        if (!rank.TryGetValue(key, out Queue<int> queue)) rank.Add(key, queue = new Queue<int>());
                        queue.Enqueue(i);
                    }
                    List<(ConnectionMeta connection, int slot, int rank)> entries = new List<(ConnectionMeta, int, int)>();
                    for (int i = 0; i < node.ConnectionsOut.Count; i++)
                    {
                        ConnectionMeta c = node.ConnectionsOut[i];
                        LinkKey key = new LinkKey(node.EntityGUID, c.ParameterGUID, c.ConnectedEntityGUID, c.ConnectedParameterGUID);
                        int r = rank.TryGetValue(key, out Queue<int> queue) && queue.Count != 0 ? queue.Dequeue() : int.MaxValue;
                        entries.Add((c, i, r));
                    }
                    foreach (IGrouping<ShortGuid, (ConnectionMeta connection, int slot, int rank)> group in entries.GroupBy(o => o.connection.ParameterGUID))
                    {
                        List<int> slots = group.Select(o => o.slot).OrderBy(o => o).ToList();
                        List<ConnectionMeta> ordered = group.OrderBy(o => o.rank).Select(o => o.connection).ToList();
                        for (int i = 0; i < slots.Count; i++)
                            node.ConnectionsOut[slots[i]] = ordered[i];
                    }
                }
            }
        }

        private bool DrawFromHint(LinkKey key, Dictionary<LinkKey, Queue<PageHint>> hints)
        {
            if (!hints.TryGetValue(key, out var queue))
                return false;
            while (queue.Count != 0)
            {
                PageHint hint = queue.Dequeue();
                if (!_drawn.TryGetValue(hint.From, out var drawnAt) || drawnAt.Count == 0)
                    continue;
                (FlowgraphMeta page, NodeMeta fromOwner, NodeMeta fromTarget) = drawnAt[0];
                Point ownerAt = fromOwner?.Position ?? new Point(0, 0);
                Point targetAt = fromTarget?.Position ?? new Point(ownerAt.X + ColumnWidth, ownerAt.Y);

                NodeMeta owner = key.Owner == hint.From.Owner && page.Nodes.Contains(fromOwner) ? fromOwner
                    : (key.Owner == hint.From.Target && fromTarget != null && page.Nodes.Contains(fromTarget) ? fromTarget : null);
                NodeMeta target = key.Target == hint.From.Target && fromTarget != null && page.Nodes.Contains(fromTarget) ? fromTarget
                    : (key.Target == hint.From.Owner && page.Nodes.Contains(fromOwner) ? fromOwner : null);

                //A new owner goes where the old owner was when it replaces it, otherwise where the old target was
                if (owner == null)
                    owner = NodeFor(page, key.Owner, key.Owner == hint.From.Owner || key.Target == hint.From.Target ? ownerAt : targetAt);
                if (target == null)
                    target = NodeFor(page, key.Target, key.Owner == hint.From.Owner || key.Target == hint.From.Target ? targetAt : ownerAt);
                Connect(page, owner, target, key);
                return true;
            }
            return false;
        }

        private bool DrawBesideExisting(LinkKey key)
        {
            foreach (FlowgraphMeta page in _pages)
            {
                NodeMeta owner = page.Nodes.FirstOrDefault(o => o.EntityGUID == key.Owner);
                NodeMeta target = page.Nodes.FirstOrDefault(o => o.EntityGUID == key.Target);
                if (owner != null && target != null)
                {
                    Connect(page, owner, target, key);
                    return true;
                }
            }
            foreach (FlowgraphMeta page in _pages)
            {
                NodeMeta owner = page.Nodes.FirstOrDefault(o => o.EntityGUID == key.Owner);
                if (owner != null)
                {
                    Connect(page, owner, NodeFor(page, key.Target, FreeSpot(page, new Point(owner.Position.X + ColumnWidth, owner.Position.Y))), key);
                    return true;
                }
                NodeMeta target = page.Nodes.FirstOrDefault(o => o.EntityGUID == key.Target);
                if (target != null)
                {
                    Connect(page, NodeFor(page, key.Owner, FreeSpot(page, new Point(target.Position.X - ColumnWidth, target.Position.Y))), target, key);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Links whose ends have no node anywhere go on one page, laid out in columns by how far down the chain of links each entity sits.</summary>
        private void DrawOnFallbackPage(List<LinkKey> links, string name)
        {
            FlowgraphMeta page = _pages.FirstOrDefault(o => o.Name == name);
            if (page == null)
            {
                page = new FlowgraphMeta() { CompositeGUID = _composite.shortGUID, Name = UniqueName(name), CanvasScale = 1.0f };
                _pages.Add(page);
            }

            List<ShortGuid> entities = new List<ShortGuid>();
            foreach (LinkKey link in links)
            {
                if (!entities.Contains(link.Owner)) entities.Add(link.Owner);
                if (!entities.Contains(link.Target)) entities.Add(link.Target);
            }
            Dictionary<ShortGuid, int> depth = entities.ToDictionary(o => o, o => 0);
            for (int pass = 0; pass < entities.Count; pass++)
            {
                bool changed = false;
                foreach (LinkKey link in links)
                {
                    if (link.Owner == link.Target) continue;
                    if (depth[link.Target] < depth[link.Owner] + 1 && depth[link.Owner] + 1 < entities.Count)
                    {
                        depth[link.Target] = depth[link.Owner] + 1;
                        changed = true;
                    }
                }
                if (!changed) break;
            }

            int top = page.Nodes.Count == 0 ? 0 : page.Nodes.Max(o => o.Position.Y) + RowHeight * 2;
            Dictionary<int, int> rows = new Dictionary<int, int>();
            Dictionary<ShortGuid, NodeMeta> placed = new Dictionary<ShortGuid, NodeMeta>();
            foreach (ShortGuid entity in entities)
            {
                NodeMeta existing = page.Nodes.FirstOrDefault(o => o.EntityGUID == entity);
                if (existing != null)
                {
                    placed[entity] = existing;
                    continue;
                }
                int column = depth[entity];
                rows.TryGetValue(column, out int row);
                rows[column] = row + 1;
                placed[entity] = NodeFor(page, entity, new Point(column * ColumnWidth, top + row * RowHeight));
            }
            foreach (LinkKey link in links)
                Connect(page, placed[link.Owner], placed[link.Target], link);
        }

        private NodeMeta NodeFor(FlowgraphMeta page, ShortGuid entity, Point at)
        {
            //One node per entity per page is enough to draw anything; reuse it rather than stacking copies
            NodeMeta existing = page.Nodes.FirstOrDefault(o => o.EntityGUID == entity);
            if (existing != null)
                return existing;
            return NewNode(page, entity, at);
        }

        private NodeMeta NewNode(FlowgraphMeta page, ShortGuid entity, Point at)
        {
            NodeMeta node = new NodeMeta()
            {
                EntityGUID = entity,
                NodeID = page.Nodes.Count == 0 ? 0 : page.Nodes.Max(o => o.NodeID) + 1,
                Position = FreeSpot(page, at),
            };
            page.Nodes.Add(node);
            return node;
        }

        private static Point FreeSpot(FlowgraphMeta page, Point at)
        {
            //Step down until nothing sits within a node's width of the spot
            Point spot = at;
            for (int i = 0; i < 64; i++)
            {
                if (!page.Nodes.Any(o => Math.Abs(o.Position.X - spot.X) < ColumnWidth / 2 && Math.Abs(o.Position.Y - spot.Y) < RowHeight / 3))
                    return spot;
                spot = new Point(spot.X, spot.Y + RowHeight / 3);
            }
            return spot;
        }

        private void Connect(FlowgraphMeta page, NodeMeta owner, NodeMeta target, LinkKey key)
        {
            //The same two pins can only be joined once: a link that is there twice needs a second node to go to
            if (owner.ConnectionsOut.Any(c => c.ConnectedNodeID == target.NodeID && c.ParameterGUID == key.Param && c.ConnectedEntityGUID == key.Target && c.ConnectedParameterGUID == key.TargetParam))
                target = NewNode(page, key.Target, new Point(target.Position.X, target.Position.Y + RowHeight / 3));
            owner.ConnectionsOut.Add(new ConnectionMeta()
            {
                ParameterGUID = key.Param,
                ConnectedEntityGUID = key.Target,
                ConnectedParameterGUID = key.TargetParam,
                ConnectedNodeID = target.NodeID,
            });
            //A pin that is now connected is no longer an unlinked one
            owner.UnlinkedPins.RemoveAll(o => o.ParameterGUID == key.Param);
            target.UnlinkedPins.RemoveAll(o => o.ParameterGUID == key.TargetParam);
        }

        private string UniqueName(string preferred)
        {
            string name = string.IsNullOrWhiteSpace(preferred) ? "Page" : preferred;
            if (!_pages.Any(o => o.Name == name))
                return name;
            for (int i = 2; ; i++)
            {
                string candidate = name + " (" + i + ")";
                if (!_pages.Any(o => o.Name == candidate))
                    return candidate;
            }
        }

        private static Dictionary<int, NodeMeta> NodesById(FlowgraphMeta page)
        {
            Dictionary<int, NodeMeta> byId = new Dictionary<int, NodeMeta>();
            foreach (NodeMeta node in page.Nodes)
                if (!byId.ContainsKey(node.NodeID))
                    byId.Add(node.NodeID, node);
            return byId;
        }

        /// <summary>A deep copy of a page, for the given composite.</summary>
        public static FlowgraphMeta Copy(FlowgraphMeta page, ShortGuid composite)
        {
            return new FlowgraphMeta()
            {
                CompositeGUID = composite,
                Name = page.Name,
                CanvasPosition = page.CanvasPosition,
                CanvasScale = page.CanvasScale <= 0 ? 1.0f : page.CanvasScale,
                SupportedLevels = page.SupportedLevels,
                AlwaysUse = page.AlwaysUse,
                Nodes = page.Nodes.Select(node => new NodeMeta()
                {
                    EntityGUID = node.EntityGUID,
                    NodeID = node.NodeID,
                    Position = node.Position,
                    ConnectionsOut = node.ConnectionsOut.Select(c => new ConnectionMeta()
                    {
                        ParameterGUID = c.ParameterGUID,
                        ConnectedEntityGUID = c.ConnectedEntityGUID,
                        ConnectedParameterGUID = c.ConnectedParameterGUID,
                        ConnectedNodeID = c.ConnectedNodeID,
                    }).ToList(),
                    UnlinkedPins = node.UnlinkedPins.Select(u => new UnlinkedPinMeta()
                    {
                        ParameterGUID = u.ParameterGUID,
                        PinLocation = u.PinLocation,
                        PinStyle = u.PinStyle,
                    }).ToList(),
                }).ToList(),
            };
        }

        /// <summary>
        /// Whether pages draw exactly a composite's links (the editor's own test, FlowgraphLayoutManager.LinksMatch,
        /// without its logging): every link to an existing entity drawn as many times as it occurs, and nothing else.
        /// </summary>
        public static bool PagesMatchLinks(Composite composite, IEnumerable<FlowgraphMeta> pages)
        {
            Dictionary<LinkKey, int> counts = new Dictionary<LinkKey, int>();
            foreach (Entity entity in composite.GetEntities())
            {
                foreach (EntityConnector link in entity.childLinks)
                {
                    if (composite.GetEntityByID(link.linkedEntityID) == null) continue;
                    LinkKey key = new LinkKey(entity, link);
                    counts.TryGetValue(key, out int count);
                    counts[key] = count + 1;
                }
            }
            foreach (FlowgraphMeta page in pages)
            {
                foreach (NodeMeta node in page.Nodes)
                {
                    foreach (ConnectionMeta connection in node.ConnectionsOut)
                    {
                        LinkKey key = new LinkKey(node.EntityGUID, connection.ParameterGUID, connection.ConnectedEntityGUID, connection.ConnectedParameterGUID);
                        if (!counts.TryGetValue(key, out int count) || count == 0)
                            return false;
                        counts[key] = count - 1;
                    }
                }
            }
            return counts.Values.All(o => o == 0);
        }
    }
}
#endif
