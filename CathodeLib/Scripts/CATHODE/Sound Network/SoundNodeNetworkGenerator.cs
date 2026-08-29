using CATHODE;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CathodeLib.Radiosity;
using CathodeLib.Sound;
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
        /// Furthest a network of each authored <c>room_size</c> class may reach from its marker.
        /// </summary>
        /// <remarks>
        /// Measured off retail's own file across all 32 levels, as the distance from a network's
        /// centroid to its furthest node: small_room p50 3.3 / p99 15.1, vent 4.9 / 19.8, corridor
        /// 7.9 / 31.3, medium_room 8.4 / 26.8, large_room 13.9 / 65.1. Without this the multi-source
        /// flood runs a vent marker straight out of its duct and down the corridor it opens onto -
        /// Tech_RnD's 'Lobby - Corridor Vent' took 172 nodes against retail's 3, and the same
        /// failure accounts for nearly all of our node over-production: the MEDIAN network is
        /// already 1.00x retail's count, it is a tail of vents blown up 5-145x.
        /// </remarks>
        private static float ExtentCap(uint roomSize)
        {
            switch (roomSize)
            {
                case 4103918620: return 15.0f;  // small_room
                case 3321711160: return 20.0f;  // vent
                case 4063189299: return 31.0f;  // corridor
                case 1834233174: return 27.0f;  // medium_room
                case 2585319144: return 65.0f;  // large_room
                default: return float.MaxValue;
            }
        }

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

        private static readonly ShortGuid DisableNetworkCreation = ShortGuidUtils.Generate("disable_network_creation");

        /// <summary>
        /// The settings the running bake was given. The generator is a static pipeline whose
        /// helpers all read the same knobs, and a bake is not re-entrant, so one field is enough -
        /// Generate sets it before it does anything else.
        /// </summary>
        private static SoundNetworkBakeSettings _settings = new SoundNetworkBakeSettings();

        /// <summary>
        /// Build the level's sound node networks. Passing null settings does nothing at all,
        /// which is how a caller opts out of the bake and keeps whatever is already on disk.
        /// </summary>
        public static void Generate(Level level, IEnumerable<InstancedEntity> entities, SoundNetworkBakeSettings settings, Action<string> log = null)
        {
            if (settings == null) return;
            _settings = settings;

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

            // With no SoundLevelInitialiser at all the fill still runs. Five DLC levels ship without
            // one - ChallengeMap 3, 7, 9, 11 and 12 - and retail scatters over all of them: their
            // files hold 577-1070 nodes against the 239-539 each has hand-placed. Treating an absent
            // initialiser as "off" left those levels with only their authored nodes.
            bool autoGenerate = initialiser == null || initialiser.Bools.Get(ShortGuidUtils.Generate("auto_generate_networks"));
            float minSpacing = initialiser == null ? 1.4f : initialiser.Floats.Get(ShortGuidUtils.Generate("network_node_min_spacing"));
            if (minSpacing <= 0.0f) minSpacing = 1.4f;
            log?.Invoke("Sound networks: initialiser " + (initialiser == null ? "absent" : "present") +
                        ", auto_generate_networks=" + autoGenerate + ", min spacing=" + minSpacing.ToString("0.##") +
                        ", markers=" + markers.Count + ", hand-placed nodes=" + manualNodes.Count);

            // Sound is blocked by world collision and by SoundBarrier volumes, both of which are
            // already in the collision soup the radiosity occluder pass collects.
            BVHAccel occluders = null;
            // Per-triangle collision flags are only collected when someone is listening, so the
            // sealed-pocket report can name what is doing the blocking.
            List<CollisionMaps.CollisionFlags> occluderFlags = log == null ? null : new List<CollisionMaps.CollisionFlags>();
            HashSet<HavokPackfile.CompoundInstance> gameplayBarriers =
                _settings.SkipGameplayBarriers ? GameplayBarrierInstances(level, entities) : null;
            if (gameplayBarriers != null && gameplayBarriers.Count > 0)
                log?.Invoke("Sound occluders: ignoring " + gameplayBarriers.Count + " CollisionBarrier volume(s) - they stop the player, not sound");
            if (RadiosityOccluders.TryCollect(level, null, out float[] verts, out int[] tris, log, true, occluderFlags, _settings.SkipDoorBarriers, null, gameplayBarriers) &&
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
            int manualCount = manualNodes.Count;
            manualCount -= DiscardOrphanedManualNodes(level, positions, manualCount, occluders, log);

            int autoCount = 0;
            if (autoGenerate)
            {
                autoCount = ScatterOverNavmesh(level, positions, minSpacing, occluders, markerPositions);
            }

            List<Link> links = BuildLinks(positions, occluders, MaxLinkDistance, log);
            int markerNetworks = networks.Count;
            int[] owner = AssignToNetworks(positions, links, markerPositions, networks, occluders, log);

            int dropped = DiscardStrandedFill(positions, manualCount, markerNetworks, networks, ref owner);

            AbsorbEnclosedPockets(positions, owner, markerNetworks, occluders, log);
            ReportSealedReachability(positions, owner, markerNetworks, networks, occluders, occluderFlags, log);

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
            var starved = new List<string>();
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
                    if (i < markerNetworks) starved.Add("'" + network.NetworkName + "'");
                }
                network.NetworkBottomLeft = low;
                network.NetworkTopRight = high;
            }

            // A network with no nodes carries a reverb nothing can ever play, and retail ships none:
            // zero empty named networks across all 32 levels. DLC/SalvageMode2 is where this shows -
            // it holds a whole 'Medical - ...' wing of 36 markers with no navmesh anywhere near them
            // (nearest node 2.5 to 66 m away in RETAIL's file as much as in ours), so that wing is
            // simply not part of this map's playspace and retail left all 36 out. Keeping them put
            // us at 85 networks against retail's 42.
            if (starved.Count > 0)
                log?.Invoke("Sound networks: dropping " + starved.Count + " marker network(s) with no nodes - " +
                            string.Join(", ", starved.Take(12)) + (starved.Count > 12 ? ", ..." : "") +
                            ". Their markers sit where no sound node reaches.");

            // Retail never ships a network that links to nothing AND holds fewer than two nodes:
            // ZERO of the 1,364 networks across all 32 levels (129 DO have no links, but every one
            // of those holds two nodes or more). A lone node linked to nothing carries a reverb
            // nothing can reach and describes no space, so it is not a network at all. This is what
            // removes the last spurious pockets - on BSP_TORRENS the two nodes a door package puts
            // on the far side of a fake corridor door, out in the abyss.
            int linkless = 0;
            if (_settings.DropLinklessSingletons)
                linkless = networks.RemoveAll(o => o.Nodes.Count > 0 && o.Nodes.Count < 2 && o.LinkedNetworks.Count == 0);
            if (linkless > 0)
                log?.Invoke("Sound networks: dropped " + linkless + " network(s) of one node with no link to anything.");

            networks.RemoveAll(o => o.Nodes.Count == 0);
            level.SoundNodeNetwork.Entries = networks;
            int kept = markerNetworks - starved.Count;
            log?.Invoke("Sound networks: " + networks.Count + " networks (" + kept + " from markers, " +
                        (networks.Count - kept) + " sealed off), " +
                        (positions.Count - dropped) + " nodes (" + manualCount + " placed, " +
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
        /// <para>Two networks adjoin only when their nodes come within <see cref="SoundNetworkBakeSettings.AdjoinDistance"/>
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
            // Which networks may hold a boundary at all. Sealed networks - the ones no marker
            // reached, written with no name and no reverb - are not uniformly excluded, because
            // retail is not uniform about them: all five of BSP_TORRENS' have no link and no path,
            // while ten of DLC/ChallengeMap12's eleven have one or two links each and take part in
            // its paths. What separates them is size. The ones retail links hold a SINGLE node -
            // they are the lone door node a marker never claimed, sitting in the doorway between
            // two rooms - and the ones it leaves alone hold two or three, which is a sealed vent
            // pocket. ChallengeMap12's own 3-node sealed network has no link either.
            var eligible = new bool[networks.Count];
            for (int i = 0; i < networks.Count; i++)
                eligible[i] = i < markerNetworks || _settings.SealedNetworkLinking == 2 ||
                              _settings.SealedNetworkLinking == 3 ||
                              (_settings.SealedNetworkLinking == 1 && networks[i].Nodes.Count <= _settings.SealedLinkMaxNodes);

            // Shortest crossing per pair of networks.
            //
            // Proximity is measured over the nodes themselves rather than over the visibility link
            // set. A boundary between two networks IS a doorway, and a closed door blocks the view,
            // so the pair either side of it frequently has no link at all - which was costing us
            // around a third of retail's boundaries (65 of its 104 on HAB_Airport) and, through the
            // smaller connected components that followed, most of its NetworkPaths.
            var shortest = new Dictionary<(int, int), (int a, int b, float dist)>();
            {
                var grid = new Dictionary<(int, int, int), List<int>>();
                float cell = Math.Max(_settings.AdjoinDistance, 0.5f);
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i] == null || owner[i] < 0 || !eligible[owner[i]]) continue;
                    (int, int, int) key = CellOf(nodes[i].Position, cell);
                    if (!grid.TryGetValue(key, out List<int> bucket)) grid[key] = bucket = new List<int>();
                    bucket.Add(i);
                }

                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i] == null || owner[i] < 0 || !eligible[owner[i]]) continue;
                    (int cx, int cy, int cz) = CellOf(nodes[i].Position, cell);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int> bucket)) continue;
                                foreach (int j in bucket)
                                {
                                    if (j <= i) continue;
                                    int a = owner[i], b = owner[j];
                                    if (a == b) continue;
                                    float dist = Vector3.Distance(nodes[i].Position, nodes[j].Position);
                                    if (dist > _settings.AdjoinDistance) continue;

                                    var key = a < b ? (a, b) : (b, a);
                                    int lo = a < b ? i : j, hi = a < b ? j : i;
                                    if (shortest.TryGetValue(key, out var held) && held.dist <= dist) continue;
                                    shortest[key] = (lo, hi, dist);
                                }
                            }
                }
            }

            var adjacency = new List<int>[networks.Count];
            var barrierOf = new Dictionary<(int, int), uint>();
            for (int i = 0; i < networks.Count; i++) adjacency[i] = new List<int>();
            int unbarriered = 0;
            foreach (var pair in shortest)
            {
                (int a, int b) = pair.Key;
                SoundNodeNetwork.NetworkNode inA = nodes[pair.Value.a];
                SoundNodeNetwork.NetworkNode inB = nodes[pair.Value.b];

                uint barrier = NearestBarrier(barriers, (inA.Position + inB.Position) * 0.5f);

                // Under mode 3 a sealed network only holds a boundary when there is a barrier at
                // the crossing. The sealed networks retail links are door nodes - the lone node in
                // a doorway that no marker claimed - so the door itself is the qualification, not
                // the size of the network.
                if (_settings.SealedNetworkLinking == 3 && barrier == 0u &&
                    (a >= markerNetworks || b >= markerNetworks)) continue;

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
            for (int from = 0; from < networks.Count; from++)
            {
                int[] previous = BreadthFirst(adjacency, from);
                for (int to = from + 1; to < networks.Count; to++)
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

        private static (int, int, int) CellOf(Vector3 p, float cell) =>
            ((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Y / cell), (int)Math.Floor(p.Z / cell));

        /// <summary>
        /// Collision instances owned by CollisionBarrier entities - invisible volumes that stop the
        /// player going somewhere.
        /// </summary>
        /// <remarks>
        /// They are STANDARD/FIXED like a wall, so the occluder soup treats them as solid, and they
        /// seal sound nodes into pockets that retail puts in a room. On BSP_TORRENS the two hiding
        /// cupboards beside the bridge are blocked by `CollisionBarrier_2` of
        /// `AYZ\Technical\Feature_Lrg\Bridge_NOSTROMO` - not by the cupboard, and not by its
        /// animated door, which is not FIXED and so is not in the soup at all. The pockets retail
        /// really does seal are blocked by named collision MESHES instead: `Collision01_COL` of
        /// `Vent_Technical_Cap_A`, `COLLISION_TUNNEL_COL` of `LadderPassage_Adapter`. So a barrier
        /// blocks the player, not sound. `diag raynames &lt;level&gt; x1 y1 z1 x2 y2 z2` names every
        /// surface a sight line crosses, which is how this was found.
        /// </remarks>
        private static HashSet<HavokPackfile.CompoundInstance> GameplayBarrierInstances(
            Level level, IEnumerable<InstancedEntity> entities)
        {
            var skip = new HashSet<HavokPackfile.CompoundInstance>();
            if (level?.CollisionMaps?.Entries == null) return skip;

            var barriers = new HashSet<(ShortGuid entity, ShortGuid composite)>();
            foreach (InstancedEntity entity in entities)
            {
                if (!(entity?.Entity is FunctionEntity function) || !function.function.IsFunctionType) continue;
                if (function.function.AsFunctionType != FunctionType.CollisionBarrier) continue;
                EntityHandle handle = entity.Handle;
                if (handle != null) barriers.Add((handle.entity_id, handle.composite_instance_id));
            }
            if (barriers.Count == 0) return skip;

            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                if (entry?.CollisionInstance == null || entry.Entity == null) continue;
                if (barriers.Contains((entry.Entity.entity_id, entry.Entity.composite_instance_id)))
                    skip.Add(entry.CollisionInstance);
            }
            return skip;
        }
        /// <summary>
        /// Name what a sight line actually crosses: the collision type of each surface hit, in
        /// order. Answers "why is this pocket sealed" with the shipped data's own vocabulary.
        /// </summary>
        private static string CrossingKinds(BVHAccel occluders, List<CollisionMaps.CollisionFlags> flags,
                                            Vector3 from, Vector3 to)
        {
            if (occluders == null || flags == null || flags.Count == 0) return "";

            Vector3 delta = to - from;
            float distance = delta.Length();
            if (distance <= 0.05f) return "";
            Vector3 direction = delta / distance;

            const float slack = 0.02f;
            float travelled = slack;
            var kinds = new List<string>();
            while (travelled < distance - slack && kinds.Count <= 8)
            {
                var ray = new Ray(from + direction * travelled, direction, 0.0f, distance - slack - travelled);
                if (!occluders.Traverse(ref ray, out Hit hit)) break;
                if (hit.PrimId >= 0 && hit.PrimId < flags.Count)
                {
                    CollisionMaps.CollisionFlags f = flags[hit.PrimId];
                    kinds.Add(((CollisionMaps.CollisionType)((uint)f & (uint)CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK)).ToString());
                }
                else kinds.Add("?");
                travelled += hit.T + 0.01f;
            }
            return string.Join(">", kinds);
        }

        /// <summary>
        /// Fold a sealed pocket into the room around it when the pocket is a BOX rather than a
        /// PASSAGE.
        /// </summary>
        /// <remarks>
        /// <para>The pockets retail keeps sealed and the ones we invent are told apart by what they
        /// are, not by how far off or how blocked they are. On BSP_TORRENS retail's five are all
        /// vent ducts and ladder shafts - `Vents\Vent_Floor_Filler`, `Vents\Corner`, `Ladder1M`,
        /// `Ladder_2m`, `Ladder_Filler` - which are separate spaces. The four we invent are hiding
        /// cupboards: `AYZ\Controls\Hiding_Cupboard` ships three nodes stacked inside the locker,
        /// the cupboard's own collision hull seals them from the deck 1.7 m away, and retail puts
        /// them in 'Torrens Bridge'. Someone hiding in a locker hears the room.</para>
        /// <para>A duct or a shaft runs away from you in some direction; the inside of a cupboard
        /// does not. So the test is free space: cast along all six axes from every node in the
        /// pocket, and if nothing can see further than <see cref="SoundNetworkBakeSettings.EnclosedPocketExtent"/> in ANY
        /// direction, the pocket is a container standing in a room and its nodes join the network of
        /// the nearest node that has one.</para>
        /// </remarks>
        private static int AbsorbEnclosedPockets(List<Vector3> positions, int[] owner, int markerNetworks,
                                                 BVHAccel occluders, Action<string> log)
        {
            if (_settings.EnclosedPocketExtent <= 0.0f || occluders == null) return 0;

            var owned = new List<int>();
            for (int i = 0; i < positions.Count; i++)
                if (owner[i] >= 0 && owner[i] < markerNetworks) owned.Add(i);
            if (owned.Count == 0) return 0;

            var bySealed = new Dictionary<int, List<int>>();
            for (int i = 0; i < positions.Count; i++)
            {
                if (owner[i] < markerNetworks) continue;
                if (!bySealed.TryGetValue(owner[i], out List<int> list)) bySealed[owner[i]] = list = new List<int>();
                list.Add(i);
            }

            Vector3[] axes =
            {
                new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
                new Vector3(0, 1, 0), new Vector3(0, -1, 0),
                new Vector3(0, 0, 1), new Vector3(0, 0, -1),
            };

            int absorbed = 0;
            var names = new List<string>();
            foreach (var group in bySealed)
            {
                float widest = 0.0f;
                foreach (int node in group.Value)
                    foreach (Vector3 axis in axes)
                    {
                        var ray = new Ray(positions[node], axis, 0.0f, _settings.EnclosedPocketExtent * 2.0f);
                        float free = occluders.Traverse(ray: ref ray, hit: out Hit hit) ? hit.T : _settings.EnclosedPocketExtent * 2.0f;
                        if (free > widest) widest = free;
                    }
                if (widest > _settings.EnclosedPocketExtent) continue;

                // Join the network of the nearest node that already has one.
                int best = -1;
                float bestDistance = float.MaxValue;
                foreach (int node in group.Value)
                    foreach (int other in owned)
                    {
                        float d = Vector3.DistanceSquared(positions[node], positions[other]);
                        if (d >= bestDistance) continue;
                        bestDistance = d;
                        best = other;
                    }
                if (best < 0) continue;

                foreach (int node in group.Value) owner[node] = owner[best];
                absorbed++;
                names.Add(group.Value.Count + " node(s) at " + Round(positions[group.Value[0]]) +
                          " (free space " + widest.ToString("0.0") + " m)");
            }

            if (absorbed > 0)
                log?.Invoke("Sound networks: folded " + absorbed + " enclosed pocket(s) into the room around them - " +
                            string.Join("; ", names.Take(8)) + (names.Count > 8 ? "; ..." : ""));
            return absorbed;
        }

        /// <summary>
        /// For every sealed network, how many surfaces separate it from the nearest node a marker
        /// owns. A pocket retail also seals reads as two or more; one is usually a node we simply
        /// failed to reach.
        /// </summary>
        private static void ReportSealedReachability(List<Vector3> positions, int[] owner, int markerNetworks,
                                                     List<SoundNodeNetwork.NetworkInfo> networks,
                                                     BVHAccel occluders, List<CollisionMaps.CollisionFlags> flags,
                                                     Action<string> log)
        {
            if (log == null || occluders == null) return;

            var owned = new List<int>();
            for (int i = 0; i < positions.Count; i++)
                if (owner[i] >= 0 && owner[i] < markerNetworks) owned.Add(i);
            if (owned.Count == 0) return;

            var bySealed = new Dictionary<int, List<int>>();
            for (int i = 0; i < positions.Count; i++)
            {
                if (owner[i] < markerNetworks) continue;
                if (!bySealed.TryGetValue(owner[i], out List<int> list)) bySealed[owner[i]] = list = new List<int>();
                list.Add(i);
            }
            if (bySealed.Count == 0) return;

            var lines = new List<string>();
            foreach (var group in bySealed)
            {
                int bestCross = int.MaxValue;
                float atDistance = 0.0f;
                int bestFrom = -1, bestTo = -1;
                foreach (int node in group.Value)
                {
                    // Only the nearest handful of owned nodes are worth testing.
                    var nearest = owned.OrderBy(o => Vector3.DistanceSquared(positions[node], positions[o])).Take(12);
                    foreach (int other in nearest)
                    {
                        int crossed = Crossings(occluders, positions[node], positions[other]);
                        if (crossed >= bestCross) continue;
                        bestCross = crossed;
                        atDistance = Vector3.Distance(positions[node], positions[other]);
                        bestFrom = node; bestTo = other;
                        if (bestCross == 0) break;
                    }
                    if (bestCross == 0) break;
                }
                string through = bestFrom < 0 ? "" :
                    CrossingKinds(occluders, flags, positions[bestFrom], positions[bestTo]);
                lines.Add(group.Value.Count + " node(s) at " + Round(positions[group.Value[0]]) +
                          " sealed by " + (bestCross == int.MaxValue ? "?" : bestCross.ToString()) +
                          " surface(s) [" + through + "], nearest owned node " + atDistance.ToString("0.0") + " m");
            }
            log("Sound networks: " + lines.Count + " sealed pocket(s) - " + string.Join("; ", lines.Take(12)) +
                (lines.Count > 12 ? "; ..." : ""));
        }
        /// <summary>
        /// Complain when two markers claim one space.
        /// </summary>
        /// <remarks>
        /// <para>A network takes its reverb, room size and events from a single
        /// SoundEnvironmentMarker, so each marker is meant to own a space of its own. The test is
        /// local: two markers resolve to the SAME seed node, meaning the nearest node either can
        /// see is the same one. Only the first can own it, so the other is left describing a room
        /// it holds no part of and its network comes out empty. Retail ships zero empty named
        /// networks across all 32 levels, so this firing means the bake is wrong (or, on a level
        /// being authored, that two markers have been put in one room).</para>
        /// <para>A whole-level connectivity test was tried first and is useless. Defining a region
        /// as a connected component of the clear-sight link graph puts nearly every marker in one
        /// region - in retail's own shipped files as much as in ours: 77 of HAB_Airport's 80 named
        /// networks share a component, 74 of SCI_AndroidLab's 76, 62 of Tech_Hub's 65. Rooms
        /// connect through doorways; that is what a level is, and it is not a defect.</para>
        /// </remarks>
        private static void WarnOnSharedRegions(List<Vector3> positions, List<SoundNodeNetwork.NetworkInfo> networks,
                                                int[] seeds, bool[] blind, float[] reach, Action<string> log)
        {
            if (log == null) return;

            // A marker with no line of sight to any node is describing a room that has none. Report
            // it as what it is rather than as a clash over the distant node it fell back to.
            var unseen = new List<string>();
            for (int m = 0; m < seeds.Length; m++)
                if (seeds[m] < 0 || blind[m])
                    unseen.Add("'" + networks[m].NetworkName + "'" +
                               (seeds[m] < 0 ? "" : " (nearest node " + reach[m].ToString("0.#") + " m away, through geometry)"));
            if (unseen.Count > 0)
                log("Sound networks: " + unseen.Count + " marker(s) can see no sound node at all - " +
                    string.Join(", ", unseen.Take(8)) + (unseen.Count > 8 ? ", ..." : "") +
                    ". Each describes a room with no nodes in it.");

            // Two markers that can both SEE the same nearest node are in one space. Only the first
            // can own it, and the other is left describing a room it holds no part of.
            var byNode = new Dictionary<int, List<int>>();
            for (int m = 0; m < seeds.Length; m++)
            {
                if (seeds[m] < 0 || blind[m]) continue;
                if (!byNode.TryGetValue(seeds[m], out List<int> list)) byNode[seeds[m]] = list = new List<int>();
                list.Add(m);
            }

            foreach (var shared in byNode)
            {
                if (shared.Value.Count < 2) continue;
                log("Sound networks: " + shared.Value.Count + " markers claim the one node at " +
                    Round(positions[shared.Key]) + " - " +
                    string.Join(", ", shared.Value.Select(m => "'" + networks[m].NetworkName + "'")) +
                    ". Only the first can own it and the rest get an empty network, so either the space " +
                    "needs dividing or all but one of these markers should go.");
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

        /// <summary>
        /// Drop an authored node that is connected to nothing and stands over nothing.
        /// </summary>
        /// <remarks>
        /// <para>Matt: a door package places a sound node on each side of its doorway, and a fake
        /// corridor door backs into the abyss - so one of the pair sits outside the level with no
        /// floor under it. Retail does not write those nodes at all; in the game's debug view they
        /// show WHITE, meaning they belong to no network.</para>
        /// <para>Neither half of the test works alone. On BSP_TORRENS 25 of the 96 nodes retail
        /// KEEPS have no navmesh beneath them (every vent and ladder node in a shaft), and 3 of them
        /// see no other node (the far side of a real door, which the closed door hides). Together
        /// they are exact: precisely two nodes are both blind and floorless, they are precisely the
        /// two retail drops, and no node retail keeps satisfies both. A node with nothing to link to
        /// and no floor to grow fill from cannot end up in anyone's network, so writing it only
        /// invents a one-node network of its own - which is where our last two spurious networks on
        /// that level came from.</para>
        /// </remarks>
        private static int DiscardOrphanedManualNodes(Level level, List<Vector3> positions, int manualCount,
                                                      BVHAccel occluders, Action<string> log)
        {
            if (!_settings.DiscardOrphanNodes || manualCount <= 0 || occluders == null) return 0;

            NavigationMesh nav = level?.StateResources != null && level.StateResources.Count > 0
                ? level.StateResources[0].NavMesh : null;
            if (nav?.Vertices == null || nav.Polygons == null) return 0;

            // Ground triangles, bucketed by XZ, for a "is there floor under this" test.
            var grid = new Dictionary<(int, int), List<(Vector3 a, Vector3 b, Vector3 c)>>();
            const float cell = 4.0f;
            foreach (NavigationMesh.dtPoly poly in nav.Polygons)
            {
                if (poly.verts == null || poly.vertCount < 3) continue;
                if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND) continue;
                if (((uint)poly.area.GetMarkupFlags() & 2u) != 0) continue;
                for (int i = 1; i + 1 < poly.vertCount; i++)
                {
                    Vector3 a = nav.Vertices[poly.verts[0]], b = nav.Vertices[poly.verts[i]], c = nav.Vertices[poly.verts[i + 1]];
                    float minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
                    float minZ = Math.Min(a.Z, Math.Min(b.Z, c.Z)), maxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z));
                    for (int x = (int)Math.Floor(minX / cell); x <= (int)Math.Floor(maxX / cell); x++)
                        for (int z = (int)Math.Floor(minZ / cell); z <= (int)Math.Floor(maxZ / cell); z++)
                        {
                            if (!grid.TryGetValue((x, z), out var bucket)) grid[(x, z)] = bucket = new List<(Vector3, Vector3, Vector3)>();
                            bucket.Add((a, b, c));
                        }
                }
            }

            var drop = new List<int>();
            for (int i = 0; i < manualCount; i++)
            {
                if (FloorUnder(grid, cell, positions[i])) continue;

                bool sees = false;
                foreach (int j in Nearest(positions, i, 32))
                    if (Crossings(occluders, positions[i], positions[j]) == 0) { sees = true; break; }
                if (!sees) drop.Add(i);
            }
            if (drop.Count == 0) return 0;

            var where = string.Join(", ", drop.Take(6).Select(i => Round(positions[i])));
            var gone = new HashSet<int>(drop);
            var kept = new List<Vector3>(positions.Count - drop.Count);
            for (int i = 0; i < positions.Count; i++) if (!gone.Contains(i)) kept.Add(positions[i]);
            positions.Clear();
            positions.AddRange(kept);

            log?.Invoke("Sound networks: dropped " + drop.Count + " hand-placed node(s) that see nothing and have no " +
                        "floor beneath - " + where + (drop.Count > 6 ? ", ..." : "") +
                        ". A door package puts a node either side of its doorway, and a fake door backs into the abyss.");
            return drop.Count;
        }

        /// <summary>Indices of the nearest few other nodes.</summary>
        private static IEnumerable<int> Nearest(List<Vector3> positions, int of, int take)
        {
            var order = new List<(float d, int i)>();
            for (int i = 0; i < positions.Count; i++)
            {
                if (i == of) continue;
                order.Add((Vector3.DistanceSquared(positions[of], positions[i]), i));
            }
            order.Sort((x, y) => x.d.CompareTo(y.d));
            for (int i = 0; i < Math.Min(take, order.Count); i++) yield return order[i].i;
        }

        /// <summary>Is any ground polygon directly below (or above) this point in plan?</summary>
        private static bool FloorUnder(Dictionary<(int, int), List<(Vector3 a, Vector3 b, Vector3 c)>> grid,
                                       float cell, Vector3 p)
        {
            if (!grid.TryGetValue(((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Z / cell)), out var bucket)) return false;
            foreach ((Vector3 a, Vector3 b, Vector3 c) in bucket)
            {
                float d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
                if (Math.Abs(d) < 1e-6f) continue;
                float u = ((b.Z - c.Z) * (p.X - c.X) + (c.X - b.X) * (p.Z - c.Z)) / d;
                float v = ((c.Z - a.Z) * (p.X - c.X) + (a.X - c.X) * (p.Z - c.Z)) / d;
                float w = 1.0f - u - v;
                if (u < -0.02f || v < -0.02f || w < -0.02f) continue;
                return true;
            }
            return false;
        }
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
            if (_settings.KeepUnreachedNodes)
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
            string name = level.Commands.Utils.GetEntityName(entity.Composite, entity.Entity);
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
            var cores = new List<Vector3>();
            foreach (NavigationMesh.dtPoly poly in nav.Polygons)
            {
                if (poly.verts == null || poly.vertCount < 3) continue;
                // Floor only. The mesh also carries the alien's backstage sheet, and a node on the
                // ceiling network has no sight line to any marker below it, so the whole sheet ends
                // up as one nameless sealed network: on ENG_Alien_Nest that was 189 of our 398 nodes
                // against retail's 0 of 259, all at y 7.05 where retail's sit at 1.1-1.5.
                if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND) continue;
                if (((uint)poly.area.GetMarkupFlags() & 2u) != 0) continue;
                if (_settings.CandidateMode == 3)
                {
                    // Sample the polygon on a lattice of its own, so how many candidates a piece of
                    // floor offers depends on its AREA and not on how finely Recast happened to cut
                    // it up. With corner-and-edge candidates the packing quality tracks tessellation:
                    // on DLC/SalvageMode2, finely cut, the fill reaches 96% of a perfect hexagonal
                    // packing at the authored spacing, while ENG_Alien_Nest's big open polygons only
                    // reach 79% - so the same authored spacing produced two different densities.
                    float step = Math.Max(0.25f, minSpacing * _settings.CandidateLatticeStep);
                    for (int t = 1; t + 1 < poly.vertCount; t++)
                    {
                        int i0 = poly.verts[0], i1 = poly.verts[t], i2 = poly.verts[t + 1];
                        if (i0 < 0 || i1 < 0 || i2 < 0 ||
                            i0 >= nav.Vertices.Length || i1 >= nav.Vertices.Length || i2 >= nav.Vertices.Length) continue;
                        Vector3 a = nav.Vertices[i0], b = nav.Vertices[i1], c = nav.Vertices[i2];
                        float longest = Math.Max((b - a).Length(), Math.Max((c - a).Length(), (c - b).Length()));
                        int n = Math.Max(1, (int)Math.Ceiling(longest / step));
                        if (n > 64) n = 64;
                        for (int i = 0; i <= n; i++)
                            for (int j = 0; i + j <= n; j++)
                                candidates.Add(a + (b - a) * ((float)i / n) + (c - a) * ((float)j / n));
                    }
                    continue;
                }

                Vector3 centre = Vector3.Zero;
                int count = 0;
                for (int i = 0; i < poly.vertCount; i++)
                {
                    int index = poly.verts[i];
                    if (index < 0 || index >= nav.Vertices.Length) continue;
                    Vector3 vertex = nav.Vertices[index];
                    if (_settings.CandidateMode != 1) candidates.Add(vertex);
                    centre += vertex;
                    count++;
                }
                if (count == 0) continue;
                centre /= count;
                if (_settings.CandidateMode == 4) cores.Add(centre); else candidates.Add(centre);
                if (_settings.CandidateMode != 0 && _settings.CandidateMode != 4) continue;
                for (int i = 0; i < poly.vertCount; i++)
                {
                    int a = poly.verts[i], b = poly.verts[(i + 1) % poly.vertCount];
                    if (a < 0 || b < 0 || a >= nav.Vertices.Length || b >= nav.Vertices.Length) continue;
                    Vector3 edge = (nav.Vertices[a] + nav.Vertices[b]) * 0.5f;
                    candidates.Add(edge);
                    candidates.Add((edge + centre) * 0.5f);
                }
            }

            // Centroids go LAST so the fill, which walks the pending list backwards, reaches them
            // FIRST. Edge midpoints were being preferred to the middle of the floor purely because
            // of the order they were appended in, and retail's nodes look far more like polygon
            // centres: offering centroids alone beats the full set by 4.1 points on
            // DLC/SalvageMode2, where our fill overshoots retail's node count by a third.
            candidates.AddRange(cores);

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
            float autoMinSq = (minSpacing * _settings.AutoSpacingScale) * (minSpacing * _settings.AutoSpacingScale);
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
            var blind = new bool[markers.Count];
            var reach = new float[markers.Count];
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
                blind[m] = seed < 0;
                if (seed < 0) seed = fallback;
                seeds[m] = seed;
                reach[m] = seed < 0 ? -1.0f : (float)Math.Sqrt(blind[m] ? fallbackDistance : seedDistance);
            }

            // Markers that can SEE a node are seeded first. A marker with no line of sight to any
            // node falls back to the nearest one by straight line, and taking the markers in file
            // order let such a marker claim a node out from under one that could actually see it -
            // the sighted marker then found its seed taken, was never queued, and lost its whole
            // network. Sighted first, blind after, so a fallback only ever takes what is left.
            for (int pass = 0; pass < 2; pass++)
                for (int m = 0; m < markers.Count; m++)
                {
                    if (seeds[m] < 0 || blind[m] != (pass == 1)) continue;
                    int seed = seeds[m];
                    if (0.0f < best[seed]) { best[seed] = 0.0f; owner[seed] = m; queue.Add((0.0f, seed)); }
                }

            // Under MarkerSeedMode 1 a marker seeds EVERY node it can see, each at its straight-line
            // distance, not just the nearest one. The link graph is walked node to node, so a node a
            // marker can see plainly but which no OTHER node of that marker can see is left behind
            // and ends up in a sealed pocket of its own: on BSP_TORRENS two groups of three nodes
            // beside the bridge come out sealed while retail has those exact positions in 'Torrens
            // Bridge'. Seeding at distance rather than zero keeps the marker that is actually in a
            // room ahead of one peering in through a window from further away.
            if (_settings.MarkerSeedMode == 1)
                for (int m = 0; m < markers.Count; m++)
                {
                    if (blind[m]) continue;
                    for (int i = 0; i < positions.Count; i++)
                    {
                        float cost = Vector3.Distance(markers[m], positions[i]);
                        if (cost >= best[i]) continue;
                        if (!Visible(occluders, markers[m], positions[i])) continue;
                        queue.Remove((best[i], i));
                        best[i] = cost;
                        owner[i] = m;
                        queue.Add((cost, i));
                    }
                }

            WarnOnSharedRegions(positions, networks, seeds, blind, reach, log);

            while (queue.Count > 0)
            {
                var (cost, node) = queue.Min;
                queue.Remove(queue.Min);
                if (cost > best[node]) continue;

                int reaching = owner[node];
                float cap = float.MaxValue;
                if (_settings.RoomExtentScale > 0.0f && reaching >= 0 && reaching < markers.Count && reaching < networks.Count)
                {
                    float classCap = ExtentCap(networks[reaching].RoomSizeValue);
                    if (classCap < float.MaxValue) cap = classCap * _settings.RoomExtentScale;
                }

                foreach (var (to, step) in adjacency[node])
                {
                    float next = cost + step;
                    if (next >= best[to]) continue;
                    // A network only reaches as far as its authored room size allows, or a vent
                    // marker floods the corridor its duct opens onto.
                    if (cap < float.MaxValue && Vector3.Distance(markers[reaching], positions[to]) > cap) continue;
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
