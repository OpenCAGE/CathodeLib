using CATHODE;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib.Radiosity;
using NanoRT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib
{
    /// <summary>
    /// Build SNDNODENETWORK.DAT from a level's sound entities.
    /// </summary>
    /// <remarks>
    /// <para>A SoundEnvironmentMarker defines a network and carries its properties. SoundNetworkNode
    /// entities are hand-placed nodes. When the level's SoundLevelInitialiser has
    /// auto_generate_networks set, further nodes are scattered over the navmesh to fill the gaps.</para>
    /// <para>Measured against retail BSP_TORRENS: hand-placed nodes are carried through unchanged
    /// (95 of our 104 appear in retail's file to within a centimetre), the auto-placed ones sit a
    /// constant ~0.46 m above the navmesh, and every auto node keeps at least
    /// network_node_min_spacing from every other node, hand-placed ones included. Their coordinates
    /// show no lattice, so the placer scatters and rejects rather than stepping a grid.</para>
    /// <para>A node belongs to the marker that reaches it through clear sight lines. Nodes no
    /// marker can see form a network of their own with no reverb and no events - retail's file has
    /// five such on BSP_TORRENS, all sealed vents and shafts, and ten on Solace.</para>
    /// </remarks>
    public static class SoundNodeNetworkGenerator
    {
        /// <summary>How far above the navmesh an auto-placed node sits. Measured from retail.</summary>
        private const float NodeHeightAboveNavmesh = 0.46f;

        /// <summary>
        /// Backstop on link length. Deliberately far beyond anything retail produces: link reach is
        /// set by what a node can see, not by a radius. Retail's longest link is 32.7 m on
        /// BSP_TORRENS but 115.8 m on ENG_ReactorCore, with near-identical node spacing on both, so
        /// a fixed radius cannot be the rule - and network_node_max_visibility does not predict it
        /// either (Solace 7 m gives 23 links per node, ENG_ReactorCore 10 m gives 89).
        /// </summary>
        private const float MaxLinkDistance = 150.0f;

        /// <summary>
        /// Spacing between two generated nodes, as a multiple of network_node_min_spacing. Taking
        /// the parameter at face value put 149 generated nodes on BSP_TORRENS against retail's 104,
        /// so the placer evidently keeps generated nodes further apart than the floor it enforces
        /// against the hand-placed ones. Fitted on one level - revisit against others.
        /// </summary>
        private const float AutoSpacingScale = 1.30f;

        /// <summary>
        /// Most surfaces a link may cross before it is dropped rather than merely marked obstructed.
        /// Retail's ObstructedDistance is 0 on about 60% of links and only reaches 4 by the 90th
        /// percentile on BSP_TORRENS, so a link through more than a few surfaces is not one retail
        /// would have kept.
        /// </summary>
        private const byte MaxObstruction = 2;

        /// <summary>
        /// Surfaces a ray may cross and still count as unobstructed. A closed collision hull is
        /// entered and exited, so a path that clips one thin panel registers two hits without being
        /// meaningfully blocked.
        /// </summary>
        /// <remarks>
        /// Measured over all 21528 pairs of retail's own nodes on BSP_TORRENS, counting the surfaces
        /// our soup puts between them: of the pairs with clear sight retail links 1055 of 1055, at
        /// one surface 50 of 56 and at two 499 of 539 - then it falls off a cliff, 22 of 187 at
        /// three and around a tenth beyond. So two is where retail stops caring, and the pairs it
        /// still links past that are ones our soup blocks and its own did not.
        /// </remarks>
        private const int ClearSurfaceTolerance = 2;

        /// <summary>
        /// Visibility is tested at the node and again this far above it, and the clearer of the two
        /// answers is taken.
        /// </summary>
        /// <remarks>
        /// <para>Scored against retail's own links on BSP_TORRENS, using retail's own node
        /// positions: testing at the node agreed with 56.5% of the links retail calls unobstructed,
        /// at +0.5 m it agrees with 70.5%, and at +1.0 m it falls back to 57.3%. So a node is stored
        /// near the floor but heard from around standing height.</para>
        /// <para>Lifting alone is not safe on its own, though. A node authored at ceiling height -
        /// the roof of a transit train, the top of a vent - has its raised sample end up in the
        /// ceiling, and sees nothing at all. Five such nodes on SCI_Hub each became a network of
        /// one. Taking the better of the two heights costs nothing on BSP_TORRENS, where it changes
        /// no answer.</para>
        /// </remarks>
        private static readonly Vector3 VisibilityTestHeight = new Vector3(0.0f, 0.5f, 0.0f);

        /// <summary>
        /// How close two networks' nodes must come for the networks to count as adjoining.
        /// </summary>
        /// <remarks>
        /// Every one of retail's 26 network links on BSP_TORRENS is stored against a node pair
        /// 1.50 or 1.61 m apart - the spacing of the pair the door_audio prefab puts either side of
        /// a doorway. Two metres takes those and leaves the long crossings our looser link set
        /// invents between rooms that merely see into one another.
        /// </remarks>
        private const float AdjoinDistance = 2.0f;

        private static readonly ShortGuid DisableNetworkCreation = ShortGuidUtils.Generate("disable_network_creation");

        public static void Generate(Level level, IEnumerable<InstancedEntity> entities, Action<string> log = null)
        {
            if (level?.SoundNodeNetwork == null) return;

            var markers = new List<InstancedEntity>();
            var manualNodes = new List<Vector3>();
            var barriers = new List<InstancedEntity>();
            InstancedEntity initialiser = null;

            foreach (InstancedEntity entity in entities)
            {
                if (!(entity?.Entity is FunctionEntity function) || !function.function.IsFunctionType) continue;
                switch (function.function.AsFunctionType)
                {
                    case FunctionType.SoundLevelInitialiser: initialiser ??= entity; break;
                    case FunctionType.SoundEnvironmentMarker:
                        // A marker with disable_network_creation still exists to be entered and
                        // exited, but contributes no network of its own.
                        if (!entity.Bools.Get(DisableNetworkCreation)) markers.Add(entity);
                        break;
                    case FunctionType.SoundNetworkNode: manualNodes.Add(PositionOf(entity)); break;
                    case FunctionType.SoundBarrier:
                    case FunctionType.NavMeshBarrier: barriers.Add(entity); break;
                }
            }

            if (markers.Count == 0) { log?.Invoke("Sound networks: no SoundEnvironmentMarker, nothing to build."); return; }

            bool autoGenerate = initialiser != null && initialiser.Bools.Get(ShortGuidUtils.Generate("auto_generate_networks"));
            float minSpacing = initialiser == null ? 1.4f : initialiser.Floats.Get(ShortGuidUtils.Generate("network_node_min_spacing"));
            if (minSpacing <= 0.0f) minSpacing = 1.4f;

            // Sound is blocked by world collision and by SoundBarrier volumes, both of which are
            // already in the collision soup the radiosity occluder pass collects.
            BVHAccel occluders = null;
            if (RadiosityOccluders.TryCollect(level, null, out float[] verts, out int[] tris, log, true) &&
                tris != null && tris.Length >= 3)
            {
                occluders = new BVHAccel();
                occluders.Build(verts, tris);
                log?.Invoke("Sound occluders: " + (tris.Length / 3) + " triangles");
            }

            // One network per marker, named after the marker entity.
            var networks = new List<SoundNodeNetwork.NetworkInfo>(markers.Count);
            var markerPositions = new List<Vector3>(markers.Count);
            foreach (InstancedEntity marker in markers)
            {
                networks.Add(BuildNetwork(level, marker));
                markerPositions.Add(PositionOf(marker));
            }

            List<Vector3> positions = new List<Vector3>(manualNodes);
            int autoCount = 0;
            if (autoGenerate)
            {
                autoCount = ScatterOverNavmesh(level, positions, minSpacing, occluders, markerPositions);
            }

            List<Link> links = BuildLinks(positions, occluders, MaxLinkDistance, log);
            int markerNetworks = networks.Count;
            int[] owner = AssignToNetworks(positions, links, markerPositions, networks, occluders, log);

            int dropped = DiscardStrandedFill(positions, manualNodes.Count, markerNetworks, networks, ref owner);

            // Nodes are created in their owning network so the writer's grouping is stable.
            var nodes = new SoundNodeNetwork.NetworkNode[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                if (owner[i] < 0) continue;
                var node = new SoundNodeNetwork.NetworkNode(networks[owner[i]], positions[i]);
                networks[owner[i]].Nodes.Add(node);
                nodes[i] = node;
            }

            // A node no marker could see is in a sealed pocket, and retail writes no link out of
            // one: on BSP_TORRENS every link in its five nameless networks stays inside the network.
            // Our looser link rule would put 53 links through the vent walls, so they are dropped.
            int leaks = 0;
            foreach (Link link in links)
            {
                if (nodes[link.A] == null || nodes[link.B] == null) continue;
                if (owner[link.A] != owner[link.B] &&
                    (owner[link.A] >= markerNetworks || owner[link.B] >= markerNetworks))
                { leaks++; continue; }
                nodes[link.A].NodeLinks.Add(new SoundNodeNetwork.NodeLinkData(nodes[link.B], link.Path, link.Obstruction));
            }

            LinkNetworks(networks, markerNetworks, nodes, links, owner, CollectBarriers(level, barriers), log);

            for (int i = 0; i < networks.Count; i++)
            {
                SoundNodeNetwork.NetworkInfo network = networks[i];
                Vector3 low = new Vector3(float.MaxValue), high = new Vector3(float.MinValue);
                foreach (SoundNodeNetwork.NetworkNode node in network.Nodes)
                {
                    low = Vector3.Min(low, node.Position);
                    high = Vector3.Max(high, node.Position);
                }
                if (network.Nodes.Count == 0)
                {
                    low = Vector3.Zero;
                    high = Vector3.Zero;
                    // The marker's room has no nodes to carry its reverb, so nothing will ever play
                    // it. Either the marker sits outside the playable space or the fill never
                    // reached its room.
                    if (i < markerNetworks)
                        log?.Invoke("Sound networks: '" + network.NetworkName + "' has no nodes at all - " +
                                    "its marker sits somewhere no sound node reaches.");
                }
                network.NetworkBottomLeft = low;
                network.NetworkTopRight = high;
            }

            level.SoundNodeNetwork.Entries = networks;
            log?.Invoke("Sound networks: " + networks.Count + " networks (" + markerNetworks + " from markers, " +
                        (networks.Count - markerNetworks) + " sealed off), " +
                        (positions.Count - dropped) + " nodes (" + manualNodes.Count + " placed, " +
                        (autoCount - dropped) + " generated, " + dropped + " stranded and dropped), " +
                        networks.Sum(e => e.Nodes.Sum(n => n.NodeLinks.Count)) + " links, " + leaks + " dropped as leaks");
        }

        /// <summary>
        /// Record which networks adjoin which, and the route from each network to every other.
        /// </summary>
        /// <remarks>
        /// <para>Both structures were read off retail's BSP_TORRENS. Exactly the 13 network pairs
        /// with a node link crossing between them are the 13 pairs it declares as linked, each
        /// recorded on both sides, and the endpoint pair it stores is the shortest of that
        /// boundary's crossings in all 26 cases - of up to 71 candidates. The endpoints come out
        /// 1.50 m apart, which is the spacing of the node pairs the door_audio prefab puts either
        /// side of a doorway, and the nearest collision mapping to each midpoint is a door.</para>
        /// <para>Two networks adjoin only when their nodes come within <see cref="AdjoinDistance"/>
        /// of each other. Our link set reaches further than retail's, so counting every crossing
        /// declared 35 boundaries against its 13. Requiring a clear line of sight instead declares
        /// none at all, which is itself the tell: a boundary between two rooms is a doorway, and the
        /// door blocks the view.</para>
        /// <para>NetworkPaths is the full upper triangle over the named networks - 13 networks give
        /// 78 paths, which is exactly what the file holds - each carrying the barriers along the
        /// route. Walking the fewest network links reproduces 75 of the 78 lists; the three misses
        /// all take a longer way round rather than cross Torrens Corridor_1 to Corridor Junction
        /// Area, which is the most obstructed of the boundaries, so the real cost is probably
        /// weighted by occlusion rather than counted in hops.</para>
        /// <para>BarrierInstanceGuid is the collision instance index of the barrier in the doorway -
        /// see <see cref="CollectBarriers"/>.</para>
        /// </remarks>
        private static void LinkNetworks(List<SoundNodeNetwork.NetworkInfo> networks, int markerNetworks,
                                         SoundNodeNetwork.NetworkNode[] nodes, List<Link> links, int[] owner,
                                         List<(Vector3 position, uint instance)> barriers, Action<string> log)
        {
            // Shortest crossing per pair of marker networks. Sealed networks take no part: retail
            // gives them no links and no paths.
            var shortest = new Dictionary<(int, int), Link>();
            foreach (Link link in links)
            {
                if (nodes[link.A] == null || nodes[link.B] == null) continue;
                if (link.Distance > AdjoinDistance) continue;
                int a = owner[link.A], b = owner[link.B];
                if (a == b || a >= markerNetworks || b >= markerNetworks) continue;

                var key = a < b ? (a, b) : (b, a);
                if (shortest.TryGetValue(key, out Link held) && held.Distance <= link.Distance) continue;
                shortest[key] = link;
            }

            var adjacency = new List<int>[markerNetworks];
            var barrierOf = new Dictionary<(int, int), uint>();
            for (int i = 0; i < markerNetworks; i++) adjacency[i] = new List<int>();
            int unbarriered = 0;
            foreach (var pair in shortest)
            {
                (int a, int b) = pair.Key;
                Link link = pair.Value;
                SoundNodeNetwork.NetworkNode inA = owner[link.A] == a ? nodes[link.A] : nodes[link.B];
                SoundNodeNetwork.NetworkNode inB = ReferenceEquals(inA, nodes[link.A]) ? nodes[link.B] : nodes[link.A];

                uint barrier = NearestBarrier(barriers, (inA.Position + inB.Position) * 0.5f);
                if (barrier == 0u) unbarriered++;
                barrierOf[pair.Key] = barrier;

                networks[a].LinkedNetworks.Add(new SoundNodeNetwork.NetworkLinkData(networks[b], barrier, inA, inB));
                networks[b].LinkedNetworks.Add(new SoundNodeNetwork.NetworkLinkData(networks[a], barrier, inB, inA));
                adjacency[a].Add(b);
                adjacency[b].Add(a);
            }
            if (unbarriered > 0)
                log?.Invoke("Sound networks: " + unbarriered + " of " + shortest.Count +
                            " network boundaries have no barrier within " + BarrierSearchRadius.ToString("0.#") + " m.");

            // One path from each network to every network after it, holding one barrier per
            // boundary crossed on the way.
            for (int from = 0; from < markerNetworks; from++)
            {
                int[] previous = BreadthFirst(adjacency, from);
                for (int to = from + 1; to < markerNetworks; to++)
                {
                    if (previous[to] == int.MinValue) continue;

                    var route = new List<uint>();
                    for (int at = to; at != from; at = previous[at])
                    {
                        int step = previous[at];
                        route.Add(barrierOf.TryGetValue(at < step ? (at, step) : (step, at), out uint id) ? id : 0u);
                    }
                    route.Reverse();
                    networks[from].NetworkPaths.Add(new SoundNodeNetwork.NetworkPath(networks[to], route));
                }
            }
        }

        /// <summary>
        /// Complain when two markers share one enclosed space.
        /// </summary>
        /// <remarks>
        /// A network takes its reverb, room size and events from a single SoundEnvironmentMarker,
        /// so a marker is meant to sit in a space of its own, sealed off from the next by geometry.
        /// Two markers in one space cannot both describe it: the nodes there form one body and only
        /// one set of parameters can win, so the split we make between them is arbitrary and the
        /// level should be fixed rather than the bake worked around. Retail levels never trip this.
        /// </remarks>
        private static void WarnOnSharedRegions(List<(int to, float cost)>[] adjacency, List<Vector3> positions,
                                                List<SoundNodeNetwork.NetworkInfo> networks, int[] seeds, Action<string> log)
        {
            if (log == null) return;

            var region = new int[positions.Count];
            for (int i = 0; i < region.Length; i++) region[i] = -1;

            int count = 0;
            var stack = new Stack<int>();
            for (int i = 0; i < region.Length; i++)
            {
                if (region[i] >= 0) continue;
                region[i] = count;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int node = stack.Pop();
                    foreach (var (to, _) in adjacency[node])
                        if (region[to] < 0) { region[to] = count; stack.Push(to); }
                }
                count++;
            }

            var markersIn = new Dictionary<int, List<int>>();
            for (int m = 0; m < seeds.Length; m++)
            {
                if (seeds[m] < 0) continue;
                int at = region[seeds[m]];
                if (!markersIn.TryGetValue(at, out List<int> list)) markersIn[at] = list = new List<int>();
                list.Add(m);
            }

            foreach (var shared in markersIn)
            {
                if (shared.Value.Count < 2) continue;
                log?.Invoke("Sound networks: " + shared.Value.Count + " markers share one enclosed space - " +
                            string.Join(", ", shared.Value.Select(m => "'" + networks[m].NetworkName + "' at " +
                                                                       Round(positions[seeds[m]]))) +
                            ". A network carries one SoundEnvironmentMarker, so either the space needs " +
                            "dividing or all but one of these markers should go.");
            }
        }

        /// <summary>
        /// Position and collision-instance index of every barrier that could divide two networks.
        /// </summary>
        /// <remarks>
        /// A sound barrier is either a SoundBarrier entity or, far more commonly, the NavMeshBarrier
        /// that sits in a doorway - BSP_TORRENS has no SoundBarrier at all but a NavMeshBarrier on
        /// every door. What retail records is the barrier's collision INSTANCE index, the slot its
        /// collider occupies in the world host, which is what BarrierInstanceGuid is named after.
        /// It is not the collision proxy index: the proxy on these PATH_CLOSED rows is -1.
        /// Verified on all 13 of BSP_TORRENS' boundaries, each naming the instance index of the
        /// NavMeshBarrier about a metre from it.
        /// </remarks>
        private static List<(Vector3 position, uint instance)> CollectBarriers(Level level, List<InstancedEntity> barriers)
        {
            var found = new List<(Vector3, uint)>();
            if (level?.CollisionMaps?.Entries == null) return found;

            // One pass over the map, keyed by the handle the barrier entity will present.
            var instanceOf = new Dictionary<(ShortGuid, ShortGuid), int>();
            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                if (entry?.Entity == null || entry.CollisionInstance == null) continue;
                instanceOf[(entry.Entity.composite_instance_id, entry.Entity.entity_id)] = entry.CollisionInstance.Index;
            }

            foreach (InstancedEntity barrier in barriers)
            {
                if (barrier.ThisCompositeInstance == null) continue;
                if (!instanceOf.TryGetValue((barrier.ThisCompositeInstance.InstanceID, barrier.Entity.shortGUID),
                                            out int index) || index < 0) continue;
                found.Add((PositionOf(barrier), (uint)index));
            }
            return found;
        }

        /// <summary>
        /// How far from a boundary a barrier may sit and still be taken as the barrier for it.
        /// Retail's are 0.94 to 1.66 m from the midpoint of the node pair either side of the door.
        /// </summary>
        private const float BarrierSearchRadius = 4.0f;

        private static uint NearestBarrier(List<(Vector3 position, uint instance)> barriers, Vector3 at)
        {
            uint best = 0u;
            float bestDistance = BarrierSearchRadius;
            foreach (var barrier in barriers)
            {
                float distance = Vector3.Distance(barrier.position, at);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = barrier.instance;
            }
            return best;
        }

        /// <summary>Predecessor of every network reachable from <paramref name="from"/>.</summary>
        private static int[] BreadthFirst(List<int>[] adjacency, int from)
        {
            var previous = new int[adjacency.Length];
            for (int i = 0; i < previous.Length; i++) previous[i] = int.MinValue;
            previous[from] = from;

            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                foreach (int next in adjacency[at])
                {
                    if (previous[next] != int.MinValue) continue;
                    previous[next] = at;
                    queue.Enqueue(next);
                }
            }
            return previous;
        }

        /// <summary>
        /// Throw away sealed-off networks made entirely of scattered nodes, marking their nodes
        /// with an owner of -1, and return how many nodes went.
        /// </summary>
        /// <remarks>
        /// Every node in retail's five nameless networks on BSP_TORRENS is a hand-placed
        /// SoundNetworkNode: a designer put a node in a sealed vent, so it has to exist. A pocket
        /// holding only scattered nodes is the fill having spread somewhere no marker can reach,
        /// which retail's placer evidently does not do - it left us five extra networks, three of
        /// them a single node.
        /// </remarks>
        private static int DiscardStrandedFill(List<Vector3> positions, int manualCount, int markerNetworks,
                                               List<SoundNodeNetwork.NetworkInfo> networks, ref int[] owner)
        {
            var hasManual = new bool[networks.Count];
            for (int i = 0; i < manualCount; i++) hasManual[owner[i]] = true;

            // Networks survive if they came from a marker or hold a hand-placed node. Renumbering
            // has to follow, since a node's owner is an index into this list.
            var remap = new int[networks.Count];
            var kept = new List<SoundNodeNetwork.NetworkInfo>(networks.Count);
            for (int i = 0; i < networks.Count; i++)
            {
                remap[i] = i < markerNetworks || hasManual[i] ? kept.Count : -1;
                if (remap[i] >= 0) kept.Add(networks[i]);
            }
            if (kept.Count == networks.Count) return 0;

            int dropped = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                owner[i] = remap[owner[i]];
                if (owner[i] < 0) dropped++;
            }

            networks.Clear();
            networks.AddRange(kept);
            return dropped;
        }

        private static SoundNodeNetwork.NetworkInfo BuildNetwork(Level level, InstancedEntity marker)
        {
            string reverb = marker.Strings.Get(ShortGuidUtils.Generate("reverb_name"));
            string enter = marker.Strings.Get(ShortGuidUtils.Generate("on_enter_event"));
            string exit = marker.Strings.Get(ShortGuidUtils.Generate("on_exit_event"));
            string roomSize = marker.Strings.Get(ShortGuidUtils.Generate("room_size"));
            float scalar = marker.Floats.Get(ShortGuidUtils.Generate("linked_network_occlusion_scaler"));

            return new SoundNodeNetwork.NetworkInfo()
            {
                NetworkName = NameOf(level, marker),
                ReverbIndex = (ushort)IndexOfReverb(level, reverb),
                EnterEventIndex = (short)IndexOfEvent(level, enter),
                ExitEventIndex = (short)IndexOfEvent(level, exit),
                RoomSizeValue = Utilities.SoundHashedString(roomSize),
                LinkedNetworkScalar = scalar == 0.0f ? 1.0f : scalar,
            };
        }

        private static string NameOf(Level level, InstancedEntity entity)
        {
            if (level.Commands?.Utils == null || entity.Composite == null || entity.Entity == null) return "";
            string name = level.Commands.Utils.GetEntityName(entity.Composite.shortGUID, entity.Entity.shortGUID);
            return string.IsNullOrEmpty(name) ? "" : name;
        }

        /// <summary>Reverb names are held in SoundEnvironmentData; the network stores the index.</summary>
        private static int IndexOfReverb(Level level, string name)
        {
            if (string.IsNullOrEmpty(name) || level.SoundEnvironmentData?.Entries == null) return ushort.MaxValue;
            int index = level.SoundEnvironmentData.Entries.FindIndex(o => o.ToLower() == name.ToLower());
            return index < 0 ? ushort.MaxValue : index;
        }

        /// <summary>
        /// Event indices address the events of every soundbank flattened in order, not the events
        /// of one bank.
        /// </summary>
        private static int IndexOfEvent(Level level, string name)
        {
            if (string.IsNullOrEmpty(name) || level.SoundEventData?.Entries == null) return -1;
            int index = 0;
            foreach (SoundEventData.Soundbank bank in level.SoundEventData.Entries)
            {
                foreach (SoundEventData.Soundbank.Event soundEvent in bank.events)
                {
                    if (soundEvent.name == name) return index;
                    index++;
                }
            }
            return -1;
        }

        /// <summary>
        /// Scatter nodes over the navmesh, rejecting any that fall within <paramref name="minSpacing"/>
        /// of a node already accepted - which includes the hand-placed ones already in the list.
        /// </summary>
        private static int ScatterOverNavmesh(Level level, List<Vector3> accepted, float minSpacing, BVHAccel occluders,
                                              List<Vector3> markerPositions)
        {
            NavigationMesh nav = level.StateResources != null && level.StateResources.Count > 0
                ? level.StateResources[0].NavMesh : null;
            if (nav?.Vertices == null || nav.Vertices.Length == 0 || nav.Polygons == null) return 0;

            // Candidates: polygon corners and centroids. A polygon large enough to hold several
            // nodes also gets its edge midpoints, so open floor does not end up under-covered.
            var candidates = new List<Vector3>();
            foreach (NavigationMesh.dtPoly poly in nav.Polygons)
            {
                if (poly.verts == null || poly.vertCount < 3) continue;
                Vector3 centre = Vector3.Zero;
                int count = 0;
                for (int i = 0; i < poly.vertCount; i++)
                {
                    int index = poly.verts[i];
                    if (index < 0 || index >= nav.Vertices.Length) continue;
                    Vector3 vertex = nav.Vertices[index];
                    candidates.Add(vertex);
                    centre += vertex;
                    count++;
                }
                if (count == 0) continue;
                centre /= count;
                candidates.Add(centre);

                for (int i = 0; i < poly.vertCount; i++)
                {
                    int a = poly.verts[i], b = poly.verts[(i + 1) % poly.vertCount];
                    if (a < 0 || b < 0 || a >= nav.Vertices.Length || b >= nav.Vertices.Length) continue;
                    Vector3 edge = (nav.Vertices[a] + nav.Vertices[b]) * 0.5f;
                    candidates.Add(edge);
                    candidates.Add((edge + centre) * 0.5f);
                }
            }

            // Grow outwards from what is already placed rather than taking candidates in whatever
            // order the navmesh happens to list them. A candidate is only accepted if some node
            // already in the set can see it, so the result is one connected body of nodes instead
            // of islands that happen to sit near each other through a wall. Candidates are visited
            // nearest-first so the fill spreads evenly rather than racing down one corridor.
            var pending = new List<Vector3>(candidates.Count);
            foreach (Vector3 candidate in candidates)
                pending.Add(candidate + new Vector3(0.0f, NodeHeightAboveNavmesh, 0.0f));

            int seedCount = accepted.Count;
            float minSq = minSpacing * minSpacing;

            // Every marker gets a node in its own room. The fill only accepts a candidate some
            // accepted node can see, so a room with no hand-placed node and no sight line to one is
            // never entered at all - on DLC/ChallengeMap5 'Testing Room_1' ended up with no node
            // within sight and seeded itself through a wall into the room next door.
            foreach (Vector3 marker in markerPositions)
            {
                int nearest = -1;
                float nearestDistance = float.MaxValue;
                for (int i = 0; i < pending.Count; i++)
                {
                    float distance = Vector3.DistanceSquared(marker, pending[i]);
                    if (distance >= nearestDistance) continue;
                    if (!Visible(occluders, marker, pending[i])) continue;
                    nearestDistance = distance;
                    nearest = i;
                }
                if (nearest < 0) continue;
                if (accepted.Any(p => Vector3.DistanceSquared(p, pending[nearest]) < minSq)) continue;

                accepted.Add(pending[nearest]);
                pending.RemoveAt(nearest);
            }

            if (accepted.Count == 0 && pending.Count > 0)
            {
                accepted.Add(pending[0]);
                pending.RemoveAt(0);
            }
            seedCount = Math.Max(seedCount, accepted.Count);

            int added = 0;
            float autoMinSq = (minSpacing * AutoSpacingScale) * (minSpacing * AutoSpacingScale);
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    Vector3 position = pending[i];

                    bool toClose = false, visible = false;
                    for (int j = 0; j < accepted.Count; j++)
                    {
                        float distanceSq = Vector3.DistanceSquared(position, accepted[j]);
                        // Hand-placed nodes sit as close together as 0.14 m and are kept as they
                        // are, but a generated node has to clear its own spacing from everything.
                        if (distanceSq < (j < seedCount ? minSq : autoMinSq)) { toClose = true; break; }
                        if (!visible && Visible(occluders, position, accepted[j])) visible = true;
                    }

                    if (toClose) { pending.RemoveAt(i); continue; }
                    if (!visible) continue;   // may become reachable once the fill gets closer

                    accepted.Add(position);
                    pending.RemoveAt(i);
                    added++;
                    progress = true;
                }
            }
            return added;
        }

        /// <summary>
        /// Clear line of sight between two nodes - the same test the network assignment walks, so
        /// the fill cannot reach somewhere the assignment will then find unreachable and seal off.
        /// </summary>
        private static bool Visible(BVHAccel occluders, Vector3 from, Vector3 to)
        {
            return Crossings(occluders, from, to) == 0;
        }

        /// <summary>
        /// Link every pair of nodes within reach. Each pair is stored once, on the lower-indexed
        /// node, which is how retail stores them - none of BSP_TORRENS' 2491 links is reciprocated.
        /// </summary>
        /// <summary>
        /// Link every pair within <paramref name="radius"/>, stored once on the lower-indexed node -
        /// none of retail's 2491 links on BSP_TORRENS is reciprocated.
        /// </summary>
        /// <remarks>
        /// Blocked pairs are linked too rather than dropped. Retail's ObstructedDistance is zero on
        /// only about 60% of its links and runs up to 24 on the rest, so an obstruction is measured
        /// into the link, not used to reject it. Filtering on line of sight instead left us at half
        /// retail's link count.
        /// </remarks>
        private struct Link
        {
            public int A, B;
            public float Distance;
            public byte Path, Obstruction;

            /// <summary>Raw surfaces the sight line crosses; zero is a clear view.</summary>
            public int Crossed;
        }

        private static List<Link> BuildLinks(List<Vector3> positions, BVHAccel occluders,
                                             float radius, Action<string> log)
        {
            var links = new List<Link>();
            int obstructed = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                for (int j = i + 1; j < positions.Count; j++)
                {
                    float distance = Vector3.Distance(positions[i], positions[j]);
                    if (distance > radius) continue;

                    int crossed = Crossings(occluders, positions[i], positions[j]);
                    byte block = (byte)Math.Max(0, crossed - ClearSurfaceTolerance);

                    // An obstructed pair is still linked - retail records the obstruction rather
                    // than dropping the link - but only while the blockage is slight. Without this
                    // every node in the level links to every other through any amount of solid
                    // geometry, which is not what retail's ObstructedDistance distribution shows.
                    if (block > MaxObstruction) continue;
                    if (block > 0) obstructed++;
                    links.Add(new Link
                    {
                        A = i,
                        B = j,
                        Distance = distance,
                        Path = (byte)Math.Min(255, (int)Math.Round(distance)),
                        Obstruction = block,
                        Crossed = crossed,
                    });
                }
            }
            log?.Invoke("Sound links: " + links.Count + " within " + radius.ToString("0.#") + " m, " +
                        obstructed + " obstructed (" + (100.0 * obstructed / Math.Max(1, links.Count)).ToString("0.0") + "%)");
            return links;
        }

        /// <summary>
        /// Give every node to the marker that reaches it most cheaply through the link graph.
        /// </summary>
        /// <remarks>
        /// <para>Straight-line distance to the nearest marker puts nodes in the room next door
        /// whenever a wall happens to be thin - on BSP_TORRENS it gave Torrens Med Bay 61 nodes
        /// against retail's 20, while Torrens Corridor_1 got 7 against 30. Walking the links instead
        /// means a node joins the marker it is actually connected to.</para>
        /// <para>Only links with a clear line of sight are walked, which is stricter than the set
        /// that gets written out. A link through even one collision hull is enough to reach into a
        /// sealed vent from the room below it, and retail keeps those vents as networks of their
        /// own: on BSP_TORRENS its five nameless networks have 53 pairs to the rest of the level
        /// through one or two surfaces and not one with a clear view.</para>
        /// </remarks>
        private static int[] AssignToNetworks(List<Vector3> positions, List<Link> links, List<Vector3> markers,
                                              List<SoundNodeNetwork.NetworkInfo> networks, BVHAccel occluders,
                                              Action<string> log)
        {
            var adjacency = new List<(int to, float cost)>[positions.Count];
            for (int i = 0; i < positions.Count; i++) adjacency[i] = new List<(int, float)>();
            foreach (Link link in links)
            {
                if (link.Crossed != 0) continue;
                adjacency[link.A].Add((link.B, link.Distance));
                adjacency[link.B].Add((link.A, link.Distance));
            }

            var owner = new int[positions.Count];
            var best = new float[positions.Count];
            for (int i = 0; i < positions.Count; i++) { owner[i] = 0; best[i] = float.MaxValue; }

            // Multi-source Dijkstra, one source per marker: its closest node.
            var seeds = new int[markers.Count];
            for (int i = 0; i < seeds.Length; i++) seeds[i] = -1;
            var queue = new SortedSet<(float cost, int node)>();
            for (int m = 0; m < markers.Count; m++)
            {
                // Nearest node the marker can actually see. Straight-line nearest reaches through
                // walls: on DLC/ChallengeMap5 'Testing Room_1' seeded a node 4.2 m away in the room
                // through its window, so both it and 'Testing Room' claimed the same space and the
                // nodes were split arbitrarily between them.
                int seed = -1, fallback = -1;
                float seedDistance = float.MaxValue, fallbackDistance = float.MaxValue;
                for (int i = 0; i < positions.Count; i++)
                {
                    float distance = Vector3.DistanceSquared(markers[m], positions[i]);
                    if (distance < fallbackDistance) { fallbackDistance = distance; fallback = i; }
                    if (distance >= seedDistance) continue;
                    if (!Visible(occluders, markers[m], positions[i])) continue;
                    seedDistance = distance;
                    seed = i;
                }
                if (seed < 0) seed = fallback;
                if (seed < 0) continue;
                seeds[m] = seed;
                if (0.0f < best[seed]) { best[seed] = 0.0f; owner[seed] = m; queue.Add((0.0f, seed)); }
            }

            WarnOnSharedRegions(adjacency, positions, networks, seeds, log);

            while (queue.Count > 0)
            {
                var (cost, node) = queue.Min;
                queue.Remove(queue.Min);
                if (cost > best[node]) continue;

                foreach (var (to, step) in adjacency[node])
                {
                    float next = cost + step;
                    if (next >= best[to]) continue;
                    queue.Remove((best[to], to));
                    best[to] = next;
                    owner[to] = owner[node];
                    queue.Add((next, to));
                }
            }

            // Nodes the graph never reached belong to no marker at all. Retail gives each such
            // island its own nameless network - BSP_TORRENS has five, of two or three nodes each,
            // with no reverb (65535) and no enter/exit events (-1). Handing them to the nearest
            // marker by straight line would fold an unreachable cupboard into the room on the far
            // side of its wall, so they are grouped among themselves instead.
            //
            // Grouping uses clear sight, the same as the reachability above. Grouping on the full
            // link set instead merges pockets that are only a wall apart: the two shafts at
            // (-0.4, 12.7, -20.2) and (-1.0, 11.2, -20.2) on BSP_TORRENS are 1.6 m from each other
            // through a hatch and retail keeps them separate, and it took the level from eight
            // sealed networks to two against retail's five.
            var island = new int[positions.Count];
            for (int i = 0; i < positions.Count; i++) island[i] = -1;

            for (int i = 0; i < positions.Count; i++)
            {
                if (best[i] < float.MaxValue || island[i] >= 0) continue;

                int index = networks.Count;
                networks.Add(new SoundNodeNetwork.NetworkInfo()
                {
                    NetworkName = "",
                    ReverbIndex = ushort.MaxValue,
                    EnterEventIndex = -1,
                    ExitEventIndex = -1,
                    RoomSizeValue = 0,
                    LinkedNetworkScalar = 1.0f,
                });

                var stack = new Stack<int>();
                stack.Push(i);
                island[i] = index;
                while (stack.Count > 0)
                {
                    int node = stack.Pop();
                    owner[node] = index;
                    foreach (var (to, _) in adjacency[node])
                    {
                        if (island[to] >= 0 || best[to] < float.MaxValue) continue;
                        island[to] = index;
                        stack.Push(to);
                    }
                }
            }
            return owner;
        }

        /// <summary>
        /// How much geometry lies between two nodes, counted as surfaces crossed. Clear line of
        /// sight gives 0.
        /// </summary>
        /// <remarks>
        /// Measuring the span from the first hit to the last instead counts the air between two
        /// walls twenty metres apart as twenty metres of obstruction, which marked 96% of pairs
        /// blocked on BSP_TORRENS against retail's 40%. A surface count also matches the shape of
        /// retail's ObstructedDistance, which is 0 on about 60% of links and mostly single digits
        /// on the rest.
        /// </remarks>
        private static int Crossings(BVHAccel occluders, Vector3 from, Vector3 to)
        {
            if (occluders == null) return 0;

            int raised = CrossingsAt(occluders, from + VisibilityTestHeight, to + VisibilityTestHeight);
            return raised == 0 ? 0 : Math.Min(raised, CrossingsAt(occluders, from, to));
        }

        private static int CrossingsAt(BVHAccel occluders, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.Length();
            if (distance <= 0.05f) return 0;
            Vector3 direction = delta / distance;

            const float slack = 0.02f;
            float travelled = slack;
            int crossed = 0;

            // Walk the ray, stepping just past each surface. Capped a little above the largest
            // ObstructedDistance retail writes, so a pathological run of surfaces cannot stall the
            // bake and anything past the cap is dropped as too blocked to be worth a link.
            while (travelled < distance - slack && crossed <= MaxObstruction + ClearSurfaceTolerance)
            {
                var ray = new Ray(from + direction * travelled, direction, 0.0f, distance - slack - travelled);
                if (!occluders.Traverse(ref ray, out Hit hit)) break;
                crossed++;
                travelled += hit.T + 0.01f;
            }

            return crossed;
        }

        private static string Round(Vector3 v) =>
            "(" + v.X.ToString("0.00") + ", " + v.Y.ToString("0.00") + ", " + v.Z.ToString("0.00") + ")";

        private static Vector3 PositionOf(InstancedEntity entity)
        {
            Matrix4x4 world = entity.CalculateWorldTransformMatrix();
            return new Vector3(world.M41, world.M42, world.M43);
        }
    }
}
#endif
