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
    /// <para>Measured against retail BSP_TORRENS: hand-placed nodes are carried through unchanged,
    /// the auto-placed ones sit a constant ~0.46 m above the navmesh, and every auto node keeps at
    /// least network_node_min_spacing from every other node, hand-placed ones included. Their
    /// coordinates show no lattice, so the placer scatters and rejects rather than stepping a grid.</para>
    /// <para>A node belongs to the marker that reaches it through clear sight lines. Nodes no marker
    /// can see form a network of their own with no reverb and no events.</para>
    /// </remarks>
    public static class SoundNodeNetworkGenerator
    {
        /// <summary>How far above the navmesh an auto-placed node sits. Measured from retail.</summary>
        private const float NodeHeightAboveNavmesh = 0.46f;

        /// <summary>
        /// Backstop on link length. Deliberately far beyond anything retail produces: link reach is
        /// set by what a node can see, not by a radius. Retail's longest link is 32.7 m on
        /// BSP_TORRENS but 115.8 m on ENG_ReactorCore with near-identical node spacing, and
        /// network_node_max_visibility does not predict it either.
        /// </summary>
        private const float MaxLinkDistance = 150.0f;

        /// <summary>
        /// Furthest a network of each authored <c>room_size</c> class may reach from its marker.
        /// </summary>
        /// <remarks>
        /// Measured off retail's own file across all 32 levels, as the distance from a network's
        /// centroid to its furthest node: small_room p50 3.3 / p99 15.1, vent 4.9 / 19.8, corridor
        /// 7.9 / 31.3, medium_room 8.4 / 26.8, large_room 13.9 / 65.1. Without this the multi-source
        /// flood runs a vent marker straight out of its duct and down the corridor it opens onto,
        /// which accounts for nearly all of our node over-production - the MEDIAN network is already
        /// 1.00x retail's count, it is a tail of vents blown up 5-145x.
        /// </remarks>
        /// <summary>
        /// Seed cost of a marker's flood by its authored <c>room_size</c>: rooms 0, then corridors,
        /// small rooms, vents, each one step of <see cref="SoundNetworkBakeSettings.ClassSeedBias"/>
        /// further behind. A contested node goes to the bigger space.
        /// </summary>
        private static float ClassBias(uint roomSize)
        {
            float step = _settings.ClassSeedBias;
            if (step <= 0.0f) return 0.0f;
            switch (roomSize)
            {
                case 2585319144: return 0.0f;        // large_room
                case 1834233174: return step;        // medium_room
                case 4063189299: return step * 2f;   // corridor
                case 4103918620: return step * 3f;   // small_room
                case 3321711160: return step * 4f;   // vent
                default: return step * 2f;
            }
        }

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
        /// three. So two is where retail stops caring.
        /// </remarks>
        private const int ClearSurfaceTolerance = 2;

        /// <summary>
        /// Visibility is tested at the node and again this far above it, and the clearer of the two
        /// answers is taken.
        /// </summary>
        /// <remarks>
        /// <para>Scored against retail's own links and node positions on BSP_TORRENS: testing at the
        /// node agreed with 56.5% of the links retail calls unobstructed, at +0.5 m 70.5%, and at
        /// +1.0 m 57.3%. So a node is stored near the floor but heard from around standing height.</para>
        /// <para>Lifting alone is not safe, though: a node authored at ceiling height has its raised
        /// sample end up inside the ceiling and sees nothing at all. Taking the better of the two
        /// costs nothing on BSP_TORRENS, where it changes no answer.</para>
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
            var manualInstance = new List<InstancedComposite>();
            var manualCompName = new List<string>();
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
                    case FunctionType.SoundNetworkNode:
                        manualNodes.Add(PositionOf(entity));
                        manualInstance.Add(entity.ThisCompositeInstance);
                        manualCompName.Add(entity.Composite?.name ?? string.Empty);
                        break;
                    case FunctionType.SoundBarrier:
                    case FunctionType.NavMeshBarrier: barriers.Add(entity); break;
                }
            }

            // Retail deduplicates hand-placed nodes: two SoundNetworkNode entities within about half
            // a metre of each other - a freestanding cupboard's node beside the door_audio pair, a
            // ladder's beside a landing's - come through as one. Kept in entity order, first wins.
            // See SoundNetworkBakeSettings.AuthoredDedupDistance.
            // A door package's two nodes, one either side of its door, are a boundary by
            // construction when they land in different networks. See
            // SoundNetworkBakeSettings.DoorPairIsBoundary.
            var manualGroup = new List<object>(manualNodes.Count);
            for (int i = 0; i < manualNodes.Count; i++) manualGroup.Add(null);
            if (_settings.DoorPairIsBoundary || (_settings.SealedSeesThroughHullsAtDoor && _settings.SealedAtDoorRequiresDoorPair))
            {
                var byInstance = new Dictionary<InstancedComposite, List<int>>();
                for (int i = 0; i < manualNodes.Count; i++)
                {
                    if (manualInstance[i] == null || manualCompName[i].IndexOf("door", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (!byInstance.TryGetValue(manualInstance[i], out List<int> l)) byInstance[manualInstance[i]] = l = new List<int>();
                    l.Add(i);
                }
                foreach (var g in byInstance)
                {
                    if (g.Value.Count != 2) continue;
                    float d = Vector3.Distance(manualNodes[g.Value[0]], manualNodes[g.Value[1]]);
                    if (d < 1.0f || d > 2.5f) continue;
                    manualGroup[g.Value[0]] = g.Key;
                    manualGroup[g.Value[1]] = g.Key;
                }
            }

            if (_settings.AuthoredDedupDistance > 0.0f && manualNodes.Count > 1)
            {
                float ddSq = _settings.AuthoredDedupDistance * _settings.AuthoredDedupDistance;
                var keptNodes = new List<Vector3>(manualNodes.Count);
                var keptGroups = new List<object>(manualNodes.Count);
                for (int k = 0; k < manualNodes.Count; k++)
                {
                    Vector3 p = manualNodes[k];
                    bool dup = false;
                    for (int i = 0; i < keptNodes.Count; i++)
                        if (Vector3.DistanceSquared(keptNodes[i], p) <= ddSq) { dup = true; break; }
                    if (!dup) { keptNodes.Add(p); keptGroups.Add(manualGroup[k]); }
                }
                manualGroup = keptGroups;
                if (keptNodes.Count < manualNodes.Count)
                    log?.Invoke("Sound networks: dropped " + (manualNodes.Count - keptNodes.Count) + " hand-placed node(s) within " +
                                _settings.AuthoredDedupDistance.ToString("0.##") + " m of another - retail keeps one of such a pair.");
                manualNodes = keptNodes;
            }

            if (markers.Count == 0) { log?.Invoke("Sound networks: no SoundEnvironmentMarker, nothing to build."); return; }

            // With no SoundLevelInitialiser at all the fill still runs. Five DLC levels ship without
            // one and retail scatters over all of them; treating an absent initialiser as "off" left
            // those levels with only their authored nodes.
            bool autoGenerate = initialiser == null || initialiser.Bools.Get(ShortGuidUtils.Generate("auto_generate_networks"));
            float minSpacing = initialiser == null ? 1.4f : initialiser.Floats.Get(ShortGuidUtils.Generate("network_node_min_spacing"));
            // The initialiser's other parameter. No initialiser, no cap - which is the split between
            // the DLC maps that land on retail's node count and the campaign levels that overshoot.
            float markerSight = initialiser == null ? 0.0f : initialiser.Floats.Get(ShortGuidUtils.Generate("network_node_max_visibility"));
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
            // The authored sound hulls out of the FLOOD's occluders too, if asked: they attenuate
            // what passes a doorway, and a marker flood that cannot pass them leaves whole chunks of
            // a room as sealed pockets. See SoundNetworkBakeSettings.FloodSkipsSoundHulls.
            if (_settings.FloodSkipsSoundHulls && level?.CollisionMaps?.Entries != null)
            {
                gameplayBarriers = gameplayBarriers == null ? new HashSet<HavokPackfile.CompoundInstance>() : new HashSet<HavokPackfile.CompoundInstance>(gameplayBarriers);
                int hulls = 0;
                foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
                {
                    if (entry?.CollisionInstance == null) continue;
                    var type = (CollisionMaps.CollisionType)((uint)entry.Flags & (uint)CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK);
                    if (type != CollisionMaps.CollisionType.SOUND && type != CollisionMaps.CollisionType.SOUND_BARRIER) continue;
                    if (gameplayBarriers.Add(entry.CollisionInstance)) hulls++;
                }
                log?.Invoke("Sound occluders: ignoring " + hulls + " SOUND-typed hull instance(s) for the marker flood");
            }
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
            manualCount -= DiscardOrphanedManualNodes(level, positions, manualCount, occluders, log, manualGroup);

            int autoCount = 0;
            if (autoGenerate)
            {
                autoCount = ScatterOverNavmesh(level, positions, minSpacing, occluders, markerPositions, markerSight, log);
            }

            List<Link> links = BuildLinks(positions, occluders, MaxLinkDistance, log);
            int markerNetworks = networks.Count;
            NavigationMesh floodNav = _settings.FloodMedium == 1 && level?.StateResources != null && level.StateResources.Count > 0
                ? level.StateResources[0].NavMesh : null;
            if (_settings.FloodMedium == 1 && (floodNav == null || floodNav.Polygons == null || floodNav.Polygons.Length == 0))
                log?.Invoke("Sound networks: FloodMedium 1 asked for the navmesh but the level has none loaded - flooding the sight graph instead.");
            int[] owner = AssignToNetworks(positions, links, markerPositions, networks, occluders, log, floodNav);

            int dropped = DiscardStrandedFill(positions, manualCount, markerNetworks, networks, ref owner);

            // Retail's fill never enters a vent - every Vent-class network it ships is hand-placed
            // nodes only - so the generated nodes the flood hands to one are dropped here.
            // See SoundNetworkBakeSettings.NoFillInVents.
            if (_settings.NoFillInVents)
            {
                int ventFill = 0;
                for (int i = manualCount; i < positions.Count; i++)
                {
                    int o = owner[i];
                    if (o < 0 || o >= markerNetworks || networks[o].RoomSizeValue != 3321711160u) continue;
                    owner[i] = -1;
                    ventFill++;
                }
                if (ventFill > 0)
                {
                    dropped += ventFill;
                    log?.Invoke("Sound networks: dropped " + ventFill + " generated node(s) inside vent networks - retail's fill never enters a vent.");
                }
            }

            AbsorbEnclosedPockets(positions, owner, markerNetworks, occluders, log);
            if (_settings.AbsorbStandingPocketsThroughHulls)
                AbsorbStandingPockets(level, positions, owner, markerNetworks, BuildOpeningGeometry(level, barriers, true, null), log);
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

            uint[] barrierTriangleInstance = null;
            BVHAccel openingGeometry = _settings.BarrierBoundaryTest == 3
                ? BuildOpeningGeometry(level, barriers, _settings.OpeningSkipsSoundCollision, log) : null;
            // The sound hulls seal a pocket and wrongly block a doorway - two different jobs - so a
            // sealed network is tested against the soup that keeps them when the named test skips them.
            BVHAccel openingStrict = _settings.BarrierBoundaryTest == 3 && _settings.OpeningSkipsSoundCollision &&
                                     _settings.SealedOpeningKeepsSoundHulls && _settings.SealedNetworkLinking != 0
                ? BuildOpeningGeometry(level, barriers, false, log) : null;
            BVHAccel barrierGeometry = _settings.BarrierBoundaryTest == 2 || (_settings.SealedSeesThroughHullsAtDoor && openingStrict != null)
                ? BuildBarrierGeometry(level, barriers, out barrierTriangleInstance, log) : null;
            LinkNetworks(networks, markerNetworks, nodes, links, owner, CollectBarriers(level, barriers),
                         barrierGeometry, barrierTriangleInstance, openingGeometry, openingStrict, manualCount, manualGroup, log);
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
            // a whole wing of 36 markers has no navmesh anywhere near it in RETAIL's file as much as
            // in ours, so that wing is not part of the playspace and retail left all 36 out.
            if (starved.Count > 0)
                log?.Invoke("Sound networks: dropping " + starved.Count + " marker network(s) with no nodes - " +
                            string.Join(", ", starved.Take(12)) + (starved.Count > 12 ? ", ..." : "") +
                            ". Their markers sit where no sound node reaches.");

            // Retail never ships a network that links to nothing AND holds fewer than two nodes:
            // ZERO of the 1,364 networks across all 32 levels (129 DO have no links, but every one
            // of those holds two nodes or more). A lone node linked to nothing carries a reverb
            // nothing can reach and describes no space, so it is not a network at all.
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
        /// <para>Both structures were read off retail's BSP_TORRENS. Exactly the network pairs with
        /// a node link crossing between them are the pairs it declares as linked, each recorded on
        /// both sides, and the endpoint pair it stores is the shortest of that boundary's crossings
        /// in all 26 cases. Those endpoints come out 1.50 m apart - the spacing of the node pairs the
        /// door_audio prefab puts either side of a doorway - and the nearest collision mapping to
        /// each midpoint is a door.</para>
        /// <para>Two networks adjoin only when their nodes come within
        /// <see cref="SoundNetworkBakeSettings.AdjoinDistance"/> of each other. Our link set reaches
        /// further than retail's, so counting every crossing declared 35 boundaries against its 13;
        /// requiring a clear line of sight instead declares none at all, which is itself the tell -
        /// a boundary between two rooms is a doorway, and the door blocks the view.</para>
        /// <para>NetworkPaths is the full upper triangle over the named networks, each carrying the
        /// barriers along the route. Walking the fewest network links reproduces 75 of BSP_TORRENS'
        /// 78 lists; the three misses all take a longer way round rather than cross its most
        /// obstructed boundary, so the real cost is probably weighted by occlusion rather than
        /// counted in hops.</para>
        /// <para>BarrierInstanceGuid is the collision instance index of the barrier in the doorway -
        /// see <see cref="CollectBarriers"/>.</para>
        /// </remarks>
        private static void LinkNetworks(List<SoundNodeNetwork.NetworkInfo> networks, int markerNetworks,
                                         SoundNodeNetwork.NetworkNode[] nodes, List<Link> links, int[] owner,
                                         List<(Vector3 position, uint instance)> barriers,
                                         BVHAccel barrierGeometry, uint[] barrierTriangleInstance, BVHAccel openingGeometry,
                                         BVHAccel openingStrict,
                                         int manualCount, List<object> manualGroup, Action<string> log)
        {
            // Which networks may hold a boundary at all. Sealed networks - the ones no marker
            // reached, written with no name and no reverb - are not uniformly excluded, because
            // retail is not uniform about them. What separates them is size: the ones retail links
            // hold a SINGLE node, the lone door node a marker never claimed sitting in the doorway
            // between two rooms, and the ones it leaves alone hold two or three, which is a sealed
            // vent pocket. Admitting them by size ALONE resurrects both kinds, since retail's door
            // node is hand-placed and a sealed network of scattered fill is a pocket we invented.
            var authoredOnly = new bool[networks.Count];
            var hasFill = new bool[networks.Count];
            var hasAny = new bool[networks.Count];
            for (int i = 0; i < owner.Length && i < nodes.Length; i++)
            {
                int o = owner[i];
                if (o < 0 || o >= networks.Count) continue;
                hasAny[o] = true;
                if (i >= manualCount) hasFill[o] = true;
            }
            int authoredSealed = 0, fillSealed = 0;
            for (int i = 0; i < networks.Count; i++)
            {
                authoredOnly[i] = hasAny[i] && !hasFill[i];
                if (i < markerNetworks) continue;
                if (authoredOnly[i]) authoredSealed++; else fillSealed++;
            }
            if (authoredSealed + fillSealed > 0)
                log?.Invoke("Sound networks: of " + (authoredSealed + fillSealed) + " sealed network(s), " +
                            authoredSealed + " are authored nodes only and " + fillSealed + " contain scattered fill.");

            var eligible = new bool[networks.Count];
            for (int i = 0; i < networks.Count; i++)
                eligible[i] = i < markerNetworks || _settings.SealedNetworkLinking == 2 ||
                              _settings.SealedNetworkLinking == 3 ||
                              (_settings.SealedNetworkLinking == 1 && networks[i].Nodes.Count <= _settings.SealedLinkMaxNodes) ||
                              (_settings.SealedNetworkLinking == 4 && authoredOnly[i] &&
                               networks[i].Nodes.Count <= _settings.SealedLinkMaxNodes);

            // Shortest crossing per pair of networks.
            //
            // Proximity is measured over the nodes themselves rather than over the visibility link
            // set. A boundary between two networks IS a doorway, and a closed door blocks the view,
            // so the pair either side of it frequently has no link at all - which was costing us
            // around a third of retail's boundaries and, through the smaller connected components
            // that followed, most of its NetworkPaths.
            var shortest = new Dictionary<(int, int), (int a, int b, float dist, uint barrier, bool ok)>();
            // XZ extent of every candidate crossing's midpoint per pair - how WIDE the join is.
            var spread = new Dictionary<(int, int), (float x0, float x1, float z0, float z1)>();
            bool weighBarrier = _settings.BarrierBoundaryTest > 0;
            int doorPairAdmitted = 0;
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

                                    if (_settings.BarrierBoundaryTest == 3 && _settings.OpeningMaxWidth > 0f)
                                    {
                                        Vector3 m = (nodes[i].Position + nodes[j].Position) * 0.5f;
                                        if (spread.TryGetValue(key, out var s))
                                            spread[key] = (Math.Min(s.x0, m.X), Math.Max(s.x1, m.X), Math.Min(s.z0, m.Z), Math.Max(s.z1, m.Z));
                                        else
                                            spread[key] = (m.X, m.X, m.Z, m.Z);
                                    }

                                    // With the barrier required, the crossing we keep for a pair is
                                    // the shortest one AT A DOOR rather than the shortest one
                                    // anywhere. Two rooms are routinely near each other along a
                                    // shared wall as well as through the doorway that joins them,
                                    // and the wall is always the closer of the two.
                                    uint bar = 0u;
                                    bool ok = true;
                                    if (_settings.BarrierBoundaryTest == 2)
                                    {
                                        bar = BarrierCrossed(barrierGeometry, barrierTriangleInstance,
                                                             nodes[i].Position, nodes[j].Position);
                                        ok = bar != 0u;
                                    }
                                    else if (_settings.BarrierBoundaryTest == 3)
                                    {
                                        // The opening test is about whether two ROOMS adjoin, so it
                                        // applies between two marker networks and not to a sealed
                                        // one: a sealed network that holds a boundary at all is the
                                        // lone door node standing IN the doorway, and gating it on
                                        // seeing through that opening admits none of them.
                                        bool sealedPair = a >= markerNetworks || b >= markerNetworks;
                                        // A sealed door node whose crossing pierces the door itself is
                                        // judged without the hull over the doorway. See
                                        // SoundNetworkBakeSettings.SealedSeesThroughHullsAtDoor.
                                        bool atDoor = sealedPair && _settings.SealedSeesThroughHullsAtDoor && barrierGeometry != null &&
                                                      (a >= markerNetworks ? networks[a].Nodes.Count : networks[b].Nodes.Count) >= _settings.SealedAtDoorMinNodes &&
                                                      (!_settings.SealedAtDoorRequiresDoorPair || (i < manualCount && j < manualCount && manualGroup[i] != null && ReferenceEquals(manualGroup[i], manualGroup[j]))) &&
                                                      BarrierCrossed(barrierGeometry, barrierTriangleInstance, nodes[i].Position, nodes[j].Position) != 0u;
                                        ok = (_settings.OpeningExemptsSealed && sealedPair) ||
                                             SeesThroughOpening(sealedPair && openingStrict != null && !atDoor ? openingStrict : openingGeometry,
                                                                nodes[i].Position, nodes[j].Position);
                                        // The guid still names the nearest barrier - the OPENING is
                                        // what qualifies the pair, so a doorway whose barrier sits
                                        // out of range keeps its boundary and writes no guid.
                                        bar = NearestBarrier(barriers, (nodes[i].Position + nodes[j].Position) * 0.5f);
                                        // ...unless asked to insist on the door as well, for the
                                        // named-to-named joins that have no barrier anywhere near.
                                        if (_settings.OpeningRequiresBarrier && a < markerNetworks && b < markerNetworks && bar == 0u)
                                            ok = false;
                                        // A door node has a door: a sealed network's crossing must
                                        // have a barrier by it, or it is a pocket that merely sees out.
                                        if (_settings.SealedRequiresBarrier && (a >= markerNetworks || b >= markerNetworks) && bar == 0u)
                                            ok = false;
                                    }
                                    else if (weighBarrier)
                                    {
                                        bar = NearestBarrier(barriers, (nodes[i].Position + nodes[j].Position) * 0.5f);
                                        ok = bar != 0u;
                                    }

                                    // One node either side of the same door: a doorway whatever the
                                    // sight test says - the leaf is what it cannot see through. See
                                    // SoundNetworkBakeSettings.DoorPairIsBoundary.
                                    // Between two MARKER networks only: a sealed door-leaf network
                                    // attaches to its nearest room alone, and giving it its second
                                    // side here bridged Torrens' rooms (26/78 exact -> 28/91).
                                    if (!ok && _settings.DoorPairIsBoundary && i < manualCount && j < manualCount &&
                                        a < markerNetworks && b < markerNetworks &&
                                        manualGroup[i] != null && ReferenceEquals(manualGroup[i], manualGroup[j]))
                                    {
                                        ok = true;
                                        doorPairAdmitted++;
                                    }

                                    if (shortest.TryGetValue(key, out var held))
                                    {
                                        if (weighBarrier && held.ok && !ok) continue;
                                        if (!(weighBarrier && !held.ok && ok) && held.dist <= dist) continue;
                                    }
                                    shortest[key] = (lo, hi, dist, bar, ok);
                                }
                            }
                }
            }

            // A boundary IS a sound barrier. Retail is unambiguous: across all 32 shipped files
            // every one of its 3,050 network links carries a non-zero BarrierInstanceGuid, with no
            // exceptions anywhere. Our own rule was proximity alone, with the barrier looked up
            // afterwards and a zero tolerated, which is what let two rooms that merely pass close to
            // one another declare a boundary they have no door for.
            if (doorPairAdmitted > 0)
                log?.Invoke("Sound networks: admitted " + doorPairAdmitted + " door-package node pair(s) as boundary crossings the opening test refused.");
            if (weighBarrier)
            {
                var noDoor = new List<(int, int)>();
                var noDoorWhere = new List<string>();
                foreach (var pair in shortest)
                    if (!pair.Value.ok)
                    {
                        noDoor.Add(pair.Key);
                        if (noDoorWhere.Count < 12)
                        {
                            string na = pair.Key.Item1 < networks.Count && !string.IsNullOrEmpty(networks[pair.Key.Item1].NetworkName) ? networks[pair.Key.Item1].NetworkName : "(sealed " + pair.Key.Item1 + ")";
                            string nb = pair.Key.Item2 < networks.Count && !string.IsNullOrEmpty(networks[pair.Key.Item2].NetworkName) ? networks[pair.Key.Item2].NetworkName : "(sealed " + pair.Key.Item2 + ")";
                            noDoorWhere.Add(na + " -- " + nb + " " + Round(nodes[pair.Value.a].Position) + " to " + Round(nodes[pair.Value.b].Position) +
                                            (pair.Value.barrier == 0u ? " no barrier" : " barrier " + pair.Value.barrier));
                        }
                    }
                foreach ((int, int) key in noDoor) shortest.Remove(key);
                if (noDoor.Count > 0)
                    log?.Invoke("Sound networks: dropped " + noDoor.Count + " of " +
                                (noDoor.Count + shortest.Count) + " candidate boundary(s) that " +
                                (_settings.BarrierBoundaryTest == 3 ? "do not see each other through an opening" : "have no barrier within " + BarrierSearchRadius.ToString("0.#") + " m") +
                                " - a boundary is a doorway: " + string.Join("; ", noDoorWhere) + (noDoor.Count > noDoorWhere.Count ? "; ..." : ""));
            }

            // A doorway is NARROW. Two markers that meet across open floor put dozens of node pairs
            // within reach of each other along a join metres wide; a door puts a handful through a
            // gap the width of the door. See SoundNetworkBakeSettings.OpeningMaxWidth.
            if (_settings.BarrierBoundaryTest == 3 && _settings.OpeningMaxWidth > 0f)
            {
                var wide = new List<(int, int)>();
                foreach (var pair in shortest)
                {
                    if (!spread.TryGetValue(pair.Key, out var s)) continue;
                    float w = (float)Math.Sqrt((s.x1 - s.x0) * (s.x1 - s.x0) + (s.z1 - s.z0) * (s.z1 - s.z0));
                    if (w > _settings.OpeningMaxWidth) wide.Add(pair.Key);
                }
                foreach ((int, int) key in wide) shortest.Remove(key);
                if (wide.Count > 0)
                    log?.Invoke("Sound networks: dropped " + wide.Count + " boundary(s) whose join is wider than " +
                                _settings.OpeningMaxWidth.ToString("0.#") + " m - a doorway is narrow.");
            }


            // A sealed network holds ONE boundary, not every neighbour within AdjoinDistance. Of
            // retail's 100 one-node sealed networks, 97 hold exactly one link. A door node hangs off
            // the room it opens onto as a LEAF, not as a bridge between two rooms - giving it every
            // neighbour is what makes the link count overshoot. See SealedMaxBoundaries.
            if (_settings.SealedMaxBoundaries > 0)
            {
                var drop = new HashSet<(int, int)>();
                for (int n = markerNetworks; n < networks.Count; n++)
                {
                    var mine = new List<((int, int) key, float dist)>();
                    foreach (var pair in shortest)
                        if (pair.Key.Item1 == n || pair.Key.Item2 == n) mine.Add((pair.Key, pair.Value.dist));
                    // One boundary per NODE when asked: retail's one-node door leaves hold one link,
                    // but a lift's two-node shaft pair links to both stops and its three-node front
                    // group to both stops and the door beyond. See SealedBoundariesPerNode.
                    int cap = _settings.SealedBoundariesPerNode
                        ? _settings.SealedMaxBoundaries * Math.Max(1, networks[n].Nodes.Count)
                        : _settings.SealedMaxBoundaries;
                    if (mine.Count <= cap) continue;
                    mine.Sort((x, y) => x.dist.CompareTo(y.dist));
                    for (int k = cap; k < mine.Count; k++) drop.Add(mine[k].key);
                }
                foreach ((int, int) key in drop) shortest.Remove(key);
                if (drop.Count > 0)
                    log?.Invoke("Sound networks: dropped " + drop.Count +
                                " surplus boundary(s) from sealed networks - each holds at most " +
                                _settings.SealedMaxBoundaries + ".");
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

                uint barrier = weighBarrier ? pair.Value.barrier
                             : NearestBarrier(barriers, (inA.Position + inB.Position) * 0.5f);

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
        /// cupboards beside the bridge are blocked by a CollisionBarrier - not by the cupboard, and
        /// not by its animated door, which is not FIXED and so is not in the soup at all. The pockets
        /// retail really does seal are blocked by named collision MESHES instead. So a barrier blocks
        /// the player, not sound. `diag raynames` names every surface a sight line crosses, which is
        /// how this was found.
        /// </remarks>
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
        /// are, not by how far off or how blocked they are. On BSP_TORRENS retail's five are all vent
        /// ducts and ladder shafts, which are separate spaces; the four we invent are hiding
        /// cupboards, whose own collision hull seals the nodes from the deck 1.7 m away and which
        /// retail puts in 'Torrens Bridge'. Someone hiding in a locker hears the room.</para>
        /// <para>A duct or a shaft runs away from you in some direction; the inside of a cupboard
        /// does not. So the test is free space: cast along all six axes from every node in the
        /// pocket, and if nothing can see further than
        /// <see cref="SoundNetworkBakeSettings.EnclosedPocketExtent"/> in ANY direction, the pocket
        /// is a container standing in a room.</para>
        /// </remarks>
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
        /// Fold a sealed pocket that stands on STANDING navmesh into the named network one of its
        /// nodes can see through the hull-free soup. A pocket on crouch floor - a duct, a shaft -
        /// stays sealed. See <see cref="SoundNetworkBakeSettings.AbsorbStandingPocketsThroughHulls"/>.
        /// </summary>
        private static int AbsorbStandingPockets(Level level, List<Vector3> positions, int[] owner, int markerNetworks,
                                                 BVHAccel openSoup, Action<string> log)
        {
            if (openSoup == null) return 0;
            NavigationMesh nav = level?.StateResources != null && level.StateResources.Count > 0
                ? level.StateResources[0].NavMesh : null;
            if (nav?.Vertices == null || nav.Polygons == null) return 0;

            // Standing ground polygons, gridded for point lookup.
            var cells = new Dictionary<(int, int), List<Vector3[]>>();
            const float cell = 2.0f;
            foreach (NavigationMesh.dtPoly poly in nav.Polygons)
            {
                if (poly.verts == null || poly.vertCount < 3) continue;
                if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND) continue;
                if (((uint)poly.area.GetMarkupFlags() & 2u) != 0) continue;
                if (poly.area.GetHeightLimitedAmount() != NavigationMesh.AreaHeight.Standing) continue;
                var v = new Vector3[poly.vertCount];
                for (int i = 0; i < poly.vertCount; i++) v[i] = nav.Vertices[poly.verts[i]];
                int x0 = (int)Math.Floor(v.Min(a => a.X) / cell), x1 = (int)Math.Floor(v.Max(a => a.X) / cell);
                int z0 = (int)Math.Floor(v.Min(a => a.Z) / cell), z1 = (int)Math.Floor(v.Max(a => a.Z) / cell);
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        if (!cells.TryGetValue((x, z), out List<Vector3[]> l)) cells[(x, z)] = l = new List<Vector3[]>();
                        l.Add(v);
                    }
            }
            bool OnStanding(Vector3 p)
            {
                if (!cells.TryGetValue(((int)Math.Floor(p.X / cell), (int)Math.Floor(p.Z / cell)), out List<Vector3[]> l)) return false;
                foreach (Vector3[] v in l)
                {
                    bool inside = false;
                    for (int a = 0, b = v.Length - 1; a < v.Length; b = a++)
                        if ((v[a].Z > p.Z) != (v[b].Z > p.Z) && p.X < (v[b].X - v[a].X) * (p.Z - v[a].Z) / (v[b].Z - v[a].Z) + v[a].X)
                            inside = !inside;
                    if (!inside) continue;
                    float y = 0; foreach (Vector3 q in v) y += q.Y; y /= v.Length;
                    if (Math.Abs(y - p.Y) <= 1.0f) return true;
                }
                return false;
            }

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

            float range = _settings.StandingPocketSeeRange;
            int absorbed = 0, absorbedNodes = 0;
            foreach (var group in bySealed)
            {
                // A lone node in a doorway is a door node, and the sealed-admission rule decides its
                // fate; this pass is for CHUNKS of room the flood failed to reach.
                if (group.Value.Count < _settings.StandingPocketMinNodes) continue;
                int standing = group.Value.Count(n => OnStanding(positions[n]));
                if (standing * 2 < group.Value.Count) continue;   // a duct or a shaft: leave it sealed

                // The nearest owned node any pocket node can see through the hull-free soup.
                int best = -1; float bestD = float.MaxValue;
                foreach (int node in group.Value)
                    foreach (int other in owned)
                    {
                        float d = Vector3.Distance(positions[node], positions[other]);
                        if (d > range || d >= bestD) continue;
                        if (!SeesThroughOpening(openSoup, positions[node], positions[other])) continue;
                        bestD = d; best = other;
                    }
                if (best < 0) continue;
                foreach (int node in group.Value) owner[node] = owner[best];
                absorbed++; absorbedNodes += group.Value.Count;
            }
            if (absorbed > 0)
                log?.Invoke("Sound networks: folded " + absorbed + " standing pocket(s), " + absorbedNodes +
                            " node(s), into the room that sees them through the doorway.");
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
        /// local: two markers resolve to the SAME seed node, meaning the nearest node either can see
        /// is the same one. Only the first can own it, so the other is left describing a room it
        /// holds no part of and its network comes out empty - and retail ships zero empty named
        /// networks, so this firing means the bake is wrong (or two markers are in one room).</para>
        /// <para>A whole-level connectivity test was tried first and is useless: defining a region as
        /// a connected component of the clear-sight link graph puts nearly every marker in one
        /// region, in retail's own shipped files as much as in ours. Rooms connect through doorways;
        /// that is what a level is, and it is not a defect.</para>
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
        /// collider occupies in the world host. It is not the collision proxy index: the proxy on
        /// these PATH_CLOSED rows is -1.
        /// <para>**Retail's values do not match ours, and that is retail's staleness, not our bug**
        /// (2 Sep 2026, `diag barrguid BSP_TORRENS <regression-test install>`, which still holds the
        /// 2017 COLLISION.HKX). Against the shipped packfile's secondary host in its on-disk order,
        /// the instance nearest each of retail's 13 crossings is 0.07-0.37 m away - the barrier -
        /// and the difference between retail's guid and that instance's index is 0, 0, 0, 0, 7, 11,
        /// 11, 11, 15, 17, 17, 20, 20, rising monotonically with the index. That is about twenty
        /// instances inserted into the compound AFTER the sound network was baked: the guid is this
        /// index in the collision the sound bake saw, and the shipped collision is newer. So the
        /// semantics here are right, and the value we write is consistent with the COLLISION.HKX we
        /// write next to it, which is what the game reads. Against our own rebuilt file it can never
        /// equal retail's number, because our compound has a different instance count and order.</para>
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
        private static float BarrierSearchRadius => _settings.BarrierSearchRadius;

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

        /// <summary>Collision instances belonging to the level's sound and navmesh barriers.</summary>
        private static HashSet<HavokPackfile.CompoundInstance> BarrierInstances(Level level, List<InstancedEntity> barriers)
        {
            var wanted = new HashSet<HavokPackfile.CompoundInstance>();
            if (level?.CollisionMaps?.Entries == null) return wanted;

            var instanceOf = new Dictionary<(ShortGuid, ShortGuid), HavokPackfile.CompoundInstance>();
            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                if (entry?.Entity == null || entry.CollisionInstance == null) continue;
                instanceOf[(entry.Entity.composite_instance_id, entry.Entity.entity_id)] = entry.CollisionInstance;
            }
            foreach (InstancedEntity barrier in barriers)
            {
                if (barrier.ThisCompositeInstance == null) continue;
                if (instanceOf.TryGetValue((barrier.ThisCompositeInstance.InstanceID, barrier.Entity.shortGUID),
                                           out HavokPackfile.CompoundInstance ci) && ci != null) wanted.Add(ci);
            }
            return wanted;
        }

        /// <summary>
        /// The level's collision with every sound barrier taken OUT of it, so a candidate boundary
        /// can be asked whether the two rooms are joined by an opening.
        /// </summary>
        /// <remarks>
        /// Two rooms adjoin when you can get from one to the other, and what stands between them at a
        /// doorway is the door - which is a barrier. Requiring plain line of sight was tried early
        /// and declared no boundaries at all on BSP_TORRENS, but that was measured against the full
        /// soup, in which the closed door leaf is itself an occluder: the test was asking whether the
        /// rooms are joined by a HOLE, not by a DOORWAY. Subtracting the barriers asks the second
        /// question, and uses the barrier set as what sits IN the opening rather than as a solid the
        /// crossing must pierce, which <see cref="BuildBarrierGeometry"/> records as refuted.
        /// </remarks>
        /// </remarks>
        private static BVHAccel BuildOpeningGeometry(Level level, List<InstancedEntity> barriers, bool skipSoundHulls, Action<string> log)
        {
            HashSet<HavokPackfile.CompoundInstance> skip = BarrierInstances(level, barriers);
            // Authored sound-occlusion hulls (collision type SOUND) are laid across doorways to
            // attenuate what passes through them; they say how sound travels through an opening,
            // not whether there is one. See SoundNetworkBakeSettings.OpeningSkipsSoundCollision.
            int soundSkipped = 0, glassSkipped = 0;
            if ((skipSoundHulls || _settings.OpeningSkipsGlass) && level?.CollisionMaps?.Entries != null)
                foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
                {
                    if (entry?.CollisionInstance == null) continue;
                    var type = (CollisionMaps.CollisionType)((uint)entry.Flags & (uint)CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK);
                    if (skipSoundHulls && (type == CollisionMaps.CollisionType.SOUND || type == CollisionMaps.CollisionType.SOUND_BARRIER))
                    {
                        if (skip.Add(entry.CollisionInstance)) soundSkipped++;
                        continue;
                    }
                    // A window between two rooms is an opening as far as a boundary is concerned.
                    // See SoundNetworkBakeSettings.OpeningSkipsGlass.
                    if (_settings.OpeningSkipsGlass && (type == CollisionMaps.CollisionType.TRANSPARENT || type == CollisionMaps.CollisionType.DYNAMIC_TRANSPARENT))
                    {
                        if (skip.Add(entry.CollisionInstance)) glassSkipped++;
                    }
                }
            if (!RadiosityOccluders.TryCollect(level, null, out float[] verts, out int[] tris, null, true,
                                               null, false, null, skip) ||
                tris == null || tris.Length < 3) return null;
            var bvh = new BVHAccel();
            bvh.Build(verts, tris);
            log?.Invoke("Sound boundaries: " + (tris.Length / 3) + " occluder triangle(s) with " + skip.Count +
                        " barrier instance(s) removed (" + soundSkipped + " of them SOUND-typed hulls, " + glassSkipped + " glass) - two networks adjoin only through an opening.");
            return bvh;
        }

        /// <summary>
        /// Whether a run between two nodes is clear of everything but the door, tried at several
        /// heights.
        /// </summary>
        /// <remarks>
        /// A node sits on the navmesh, so a single run between two of them skims the floor and any
        /// threshold, step, ramp lip or door sill stands in its way - which reads as "these rooms do
        /// not adjoin" for a great many doorways that plainly do. A wall blocks at every height, so
        /// sampling upward costs no precision and is what separates a sill from a partition.
        /// </remarks>
        private static bool SeesThroughOpening(BVHAccel openingGeometry, Vector3 a, Vector3 b)
        {
            if (openingGeometry == null) return true;
            foreach (float lift in OpeningHeights)
                if (ClearRun(openingGeometry, a + new Vector3(0f, lift, 0f), b + new Vector3(0f, lift, 0f)))
                    return true;
            return false;
        }

        /// <summary>Heights above the node a boundary's run is tried at, in metres.</summary>
        private static readonly float[] OpeningHeights = { 0.0f, 0.6f, 1.2f };

        private static bool ClearRun(BVHAccel openingGeometry, Vector3 a, Vector3 b)
        {
            if (openingGeometry == null) return true;
            Vector3 delta = b - a;
            float length = delta.Length();
            if (length <= 1e-4f) return true;
            Vector3 direction = delta / length;
            // Both ends start on a node that sits just off the floor, so the run is lifted to chest
            // height the way the link builder does rather than skimming the ground.
            var ray = new Ray(a, direction, 0.02f, length - 0.02f);
            return !openingGeometry.Traverse(ref ray, out Hit _);
        }

        /// <summary>
        /// Collision geometry of the level's sound barriers alone, so a candidate boundary can be
        /// asked whether it passes THROUGH a door rather than merely near one.
        /// </summary>
        /// <remarks>
        /// <para>Distance to a barrier's pivot cannot make the distinction that matters. Retail's own
        /// boundaries sit a median 0.90 m from one, so the pivots are genuinely at the doorways - but
        /// so are the false boundaries we declare between two rooms that meet in a wall beside a
        /// door.</para>
        /// <para>Requiring the crossing to intersect this geometry is refuted by retail's own file:
        /// of the node pairs it stored for ChallengeMap11's 132 boundaries, only 39% pass through any
        /// barrier, and 39% again when the segment is extended a metre past each end. The barrier
        /// sits about 0.90 m to one SIDE of the midpoint rather than between them. A barrier is a
        /// label retail attaches to a boundary, not a solid the boundary passes through.</para>
        /// </remarks>
        private static BVHAccel BuildBarrierGeometry(Level level, List<InstancedEntity> barriers,
                                                     out uint[] triangleInstance, Action<string> log)
        {
            triangleInstance = null;
            if (level?.CollisionMaps?.Entries == null || barriers.Count == 0) return null;

            HashSet<HavokPackfile.CompoundInstance> wanted = BarrierInstances(level, barriers);
            if (wanted.Count == 0) return null;

            var owners = new List<HavokPackfile.CompoundInstance>();
            if (!RadiosityOccluders.TryCollect(level, null, out float[] verts, out int[] tris, null, true,
                                               null, false, owners, null) ||
                tris == null || tris.Length < 3 || owners.Count * 3 != tris.Length) return null;

            var keepVerts = new List<float>();
            var keepTris = new List<int>();
            var keepInstance = new List<uint>();
            var remap = new Dictionary<int, int>();
            for (int t = 0; t < owners.Count; t++)
            {
                if (owners[t] == null || !wanted.Contains(owners[t])) continue;
                for (int c = 0; c < 3; c++)
                {
                    int v = tris[t * 3 + c];
                    if (!remap.TryGetValue(v, out int nv))
                    {
                        nv = keepVerts.Count / 3;
                        remap[v] = nv;
                        keepVerts.Add(verts[v * 3]);
                        keepVerts.Add(verts[v * 3 + 1]);
                        keepVerts.Add(verts[v * 3 + 2]);
                    }
                    keepTris.Add(nv);
                }
                keepInstance.Add((uint)owners[t].Index);
            }
            if (keepTris.Count < 3) return null;

            var bvh = new BVHAccel();
            bvh.Build(keepVerts.ToArray(), keepTris.ToArray());
            triangleInstance = keepInstance.ToArray();
            log?.Invoke("Sound barriers: " + (keepTris.Count / 3) + " triangle(s) over " + wanted.Count +
                        " barrier instance(s) - boundaries must cross one.");
            return bvh;
        }

        /// <summary>
        /// The barrier a straight run between two nodes passes through, or 0 for none. Both ends are
        /// pulled in slightly so a node sitting flush against the door leaf still reads as crossing.
        /// </summary>
        private static uint BarrierCrossed(BVHAccel barrierGeometry, uint[] triangleInstance, Vector3 a, Vector3 b)
        {
            if (barrierGeometry == null) return 0u;
            Vector3 delta = b - a;
            float length = delta.Length();
            if (length <= 1e-4f) return 0u;
            Vector3 direction = delta / length;
            var ray = new Ray(a, direction, 0.0f, length);
            if (!barrierGeometry.Traverse(ref ray, out Hit hit)) return 0u;
            if (triangleInstance == null || hit.PrimId < 0 || hit.PrimId >= triangleInstance.Length) return 0u;
            return triangleInstance[hit.PrimId];
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
        /// Drop an authored node that is connected to nothing and stands over nothing.
        /// </summary>
        /// <remarks>
        /// <para>Matt: a door package places a sound node on each side of its doorway, and a fake
        /// corridor door backs into the abyss - so one of the pair sits outside the level with no
        /// floor under it. Retail does not write those nodes at all; in the game's debug view they
        /// show WHITE, meaning they belong to no network.</para>
        /// <para>Neither half of the test works alone. On BSP_TORRENS 25 of the 96 nodes retail KEEPS
        /// have no navmesh beneath them and 3 see no other node, but precisely two are both blind and
        /// floorless, they are precisely the two retail drops, and no node retail keeps satisfies
        /// both. A node with nothing to link to and no floor to grow fill from cannot end up in
        /// anyone's network, so writing it only invents a one-node network of its own.</para>
        /// </remarks>
        private static int DiscardOrphanedManualNodes(Level level, List<Vector3> positions, int manualCount,
                                                      BVHAccel occluders, Action<string> log, List<object> groups = null)
        {
            if (!_settings.DiscardOrphanNodes || manualCount <= 0 || occluders == null) return 0;

            // What counts as company is judged with doors transparent: retail's node links pass
            // through a door with obstruction 0, and a lift's door node has nothing but the car
            // nodes behind the lift door to keep it. See SoundNetworkBakeSettings.OrphanSightThroughDoors.
            BVHAccel sight = occluders;
            if (_settings.OrphanSightThroughDoors || _settings.OrphanSightThroughSoundHulls)
            {
                // The authored SOUND / SOUND_BARRIER hulls come out too: retail links straight
                // through them, and a door-package node standing inside one sees nothing otherwise.
                // See SoundNetworkBakeSettings.OrphanSightThroughSoundHulls.
                HashSet<HavokPackfile.CompoundInstance> hulls = _settings.OrphanSightThroughSoundHulls ? SoundHullInstances(level) : null;
                if (hulls != null && hulls.Count == 0) hulls = null;
                if (RadiosityOccluders.TryCollect(level, null, out float[] dfVerts, out int[] dfTris, null, true, null,
                                                  _settings.OrphanSightThroughDoors, null, hulls) &&
                    dfTris != null && dfTris.Length >= 3)
                {
                    sight = new BVHAccel();
                    sight.Build(dfVerts, dfTris);
                }
            }

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
                float rangeSq = _settings.OrphanSightRange > 0.0f ? _settings.OrphanSightRange * _settings.OrphanSightRange : float.MaxValue;
                foreach (int j in Nearest(positions, i, 32))
                {
                    // A node in the void sees other void nodes forty metres off across nothing;
                    // retail drops those, so what counts as company has to be near. See
                    // SoundNetworkBakeSettings.OrphanSightRange.
                    if (Vector3.DistanceSquared(positions[i], positions[j]) > rangeSq) break;
                    if (Crossings(sight, positions[i], positions[j]) == 0) { sees = true; break; }
                }
                if (!sees) drop.Add(i);
            }
            if (drop.Count == 0) return 0;

            var where = string.Join(", ", drop.Take(6).Select(i => Round(positions[i])));
            var gone = new HashSet<int>(drop);
            var kept = new List<Vector3>(positions.Count - drop.Count);
            for (int i = 0; i < positions.Count; i++) if (!gone.Contains(i)) kept.Add(positions[i]);
            positions.Clear();
            positions.AddRange(kept);
            if (groups != null && groups.Count >= manualCount)
            {
                var keptGroups = new List<object>(groups.Count);
                for (int i = 0; i < groups.Count; i++) if (!gone.Contains(i)) keptGroups.Add(groups[i]);
                groups.Clear();
                groups.AddRange(keptGroups);
            }

            log?.Invoke("Sound networks: dropped " + drop.Count + " hand-placed node(s) that see nothing and have no " +
                        "floor beneath - " + where + (drop.Count > 6 ? ", ..." : "") +
                        ". A door package puts a node either side of its doorway, and a fake door backs into the abyss.");
            return drop.Count;
        }

        /// <summary>Every collision instance whose mapping types it SOUND or SOUND_BARRIER.</summary>
        private static HashSet<HavokPackfile.CompoundInstance> SoundHullInstances(Level level)
        {
            var set = new HashSet<HavokPackfile.CompoundInstance>();
            if (level?.CollisionMaps?.Entries == null) return set;
            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                if (entry?.CollisionInstance == null) continue;
                var type = (CollisionMaps.CollisionType)((uint)entry.Flags & (uint)CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK);
                if (type == CollisionMaps.CollisionType.SOUND || type == CollisionMaps.CollisionType.SOUND_BARRIER)
                    set.Add(entry.CollisionInstance);
            }
            return set;
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
                if (_settings.OrphanFloorMustBeBelow)
                {
                    // Beneath means beneath: the floor sits at or below the node, and not too far.
                    float floorY = u * a.Y + v * b.Y + w * c.Y;
                    float gap = p.Y - floorY;
                    if (gap < -0.15f || gap > 3.0f) continue;
                }
                return true;
            }
            return false;
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
                                              List<Vector3> markerPositions, float markerSight = 0.0f, Action<string> log = null)
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
                // up as one nameless sealed network - 189 of ENG_Alien_Nest's 398 nodes at y 7.05,
                // against retail's 0 of 259.
                if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND) continue;
                if (((uint)poly.area.GetMarkupFlags() & 2u) != 0) continue;
                // Retail's fill lands on STANDING floor: 98% of SCI_HospitalLower's 562 fill nodes,
                // 0% on crouch, 2% on deep crouch, none off the mesh. Ours put 9% on crouch or deep
                // floor - under tables, in ducts - which retail leaves to the hand-placed nodes.
                // See SoundNetworkBakeSettings.FillStandingOnly.
                if (_settings.FillStandingOnly && poly.area.GetHeightLimitedAmount() != NavigationMesh.AreaHeight.Standing) continue;
                if (_settings.CandidateMode == 3)
                {
                    // Sample the polygon on a lattice of its own, so how many candidates a piece of
                    // floor offers depends on its AREA and not on how finely Recast happened to cut
                    // it up. With corner-and-edge candidates the packing quality tracks tessellation,
                    // so the same authored spacing produced two different densities.
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
            // centres.
            candidates.AddRange(cores);

            // Grow outwards from what is already placed rather than taking candidates in whatever
            // order the navmesh happens to list them. A candidate is only accepted if some node
            // already in the set can see it, so the result is one connected body of nodes instead
            // of islands that happen to sit near each other through a wall. Candidates are visited
            // nearest-first so the fill spreads evenly rather than racing down one corridor.
            var pending = new List<Vector3>(candidates.Count);
            foreach (Vector3 candidate in candidates)
                pending.Add(candidate + new Vector3(0.0f, NodeHeightAboveNavmesh, 0.0f));

            // Only where a marker can see, within the initialiser's network_node_max_visibility.
            // The two levels with no initialiser land on retail's node count; every level with one
            // is 20-35% over, at any authored spacing - so the initialiser's presence changes retail's
            // fill, and this is its other parameter. See SoundNetworkBakeSettings.FillRequiresMarkerSight.
            if (_settings.FillRequiresMarkerSight && markerSight > 0.0f && markerPositions.Count > 0)
            {
                float sightSq = markerSight * markerSight;
                int before = pending.Count;
                pending.RemoveAll(p =>
                {
                    foreach (Vector3 marker in markerPositions)
                    {
                        if (Vector3.DistanceSquared(marker, p) > sightSq) continue;
                        if (Visible(occluders, marker, p)) return false;
                    }
                    return true;
                });
                log?.Invoke("Sound networks: " + (before - pending.Count) + " of " + before +
                            " fill candidate(s) are out of sight of every marker within " + markerSight.ToString("0.#") + " m and are dropped.");
            }

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

            /* How many accepted nodes each pending candidate has already been measured against.
             * Both tests below are a plain OR over the accepted set - too close to ANY of them, or
             * visible from ANY of them - so a node that has already been ruled out for a candidate
             * can never change that candidate's answer, and only the ones added since need looking
             * at. Without this every surviving candidate re-tested the whole accepted set on every
             * pass, and each test is a raycast: Solace spent 18 of its 24 seconds of sound bake here. */
            var testedAgainst = new List<int>(pending.Count);
            for (int i = 0; i < pending.Count; i++)
                testedAgainst.Add(0);
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    Vector3 position = pending[i];
                    int from = testedAgainst[i];
                    int count = accepted.Count;
                    testedAgainst[i] = count;

                    /* Spacing first, then sight. Both tests ask whether ANY accepted node answers
                     * to them, so neither depends on the order they are asked in - but too-close
                     * wins outright, and doing the arithmetic pass first means a candidate that is
                     * going to be thrown away never pays for a raycast. Nearly all of them are
                     * thrown away, so nearly all of the raycasts were being wasted. */
                    bool toClose = false;
                    for (int j = from; j < count; j++)
                    {
                        float distanceSq = Vector3.DistanceSquared(position, accepted[j]);
                        // Hand-placed nodes sit as close together as 0.14 m and are kept as they
                        // are, but a generated node has to clear its own spacing from everything.
                        if (distanceSq < (j < seedCount ? minSq : autoMinSq)) { toClose = true; break; }
                    }

                    if (toClose)
                    {
                        pending.RemoveAt(i);
                        testedAgainst.RemoveAt(i);
                        continue;
                    }

                    bool visible = false;
                    for (int j = from; j < count && !visible; j++)
                        if (Visible(occluders, position, accepted[j])) visible = true;

                    if (!visible) continue;   // may become reachable once the fill gets closer

                    accepted.Add(position);
                    pending.RemoveAt(i);
                    testedAgainst.RemoveAt(i);
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
        /// <summary>
        /// Multi-source Dijkstra over navmesh polygons: each marker seeds the polygon under it at
        /// its class bias, the walk crosses shared edges and off-mesh links (Backstage links
        /// excepted), never enters a polygon carrying a barrier area id, and stops at the class
        /// extent cap. A node takes the owner of the polygon under it, at that polygon's cost plus
        /// its own offset; a node on no polygon, or on one no marker reached, is left unowned.
        /// </summary>
        private static void NavmeshFlood(NavigationMesh nav, List<Vector3> positions, List<Vector3> markers,
                                         List<SoundNodeNetwork.NetworkInfo> networks, int[] owner, float[] best,
                                         Action<string> log, List<(int to, float cost)>[] adjacency)
        {
            int n = nav.Polygons.Length;
            var centroid = new Vector3[n];
            var polyVerts = new Vector3[n][];
            for (int i = 0; i < n; i++)
            {
                NavigationMesh.dtPoly poly = nav.Polygons[i];
                int vc = poly.vertCount;
                var vs = new Vector3[vc];
                Vector3 c = Vector3.Zero;
                for (int k = 0; k < vc; k++) { vs[k] = nav.Vertices[poly.verts[k]]; c += vs[k]; }
                polyVerts[i] = vs;
                centroid[i] = vc > 0 ? c / vc : new Vector3(float.NaN);
            }

            var adj = new List<int>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<int>();
            for (int i = 0; i < n; i++)
            {
                NavigationMesh.dtPoly poly = nav.Polygons[i];
                if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND) continue;
                for (int e = 0; e < poly.vertCount; e++)
                {
                    ushort nei = poly.neis[e];
                    if (nei == 0 || (nei & 0x8000) != 0) continue;
                    int q = nei - 1;
                    if (q >= 0 && q < n) adj[i].Add(q);
                }
            }
            int offMesh = 0;
            if (nav.OffMeshConnections != null)
                foreach (NavigationMesh.dtOffMeshConnection con in nav.OffMeshConnections)
                {
                    if (con.pos == null || con.pos.Length < 6) continue;
                    int cp = con.poly_index_within_tile;
                    if (cp >= 0 && cp < n && nav.Polygons[cp].area.GetLinkType() == NavigationMesh.OffMeshLinkType.Backstage) continue;
                    int a = PolyUnder(new Vector3(con.pos[0], con.pos[1], con.pos[2]), polyVerts, centroid, 2.0f);
                    int b = PolyUnder(new Vector3(con.pos[3], con.pos[4], con.pos[5]), polyVerts, centroid, 2.0f);
                    if (a < 0 || b < 0 || a == b) continue;
                    adj[a].Add(b);
                    if ((con.flags & 1) != 0) adj[b].Add(a);
                    offMesh++;
                }

            var polyBest = new float[n];
            var polyOwner = new int[n];
            for (int i = 0; i < n; i++) { polyBest[i] = float.MaxValue; polyOwner[i] = -1; }
            var pq = new SortedSet<(float cost, int poly)>();
            int seeded = 0, blindMarkers = 0;
            var seedPoly = new int[markers.Count];
            for (int m = 0; m < markers.Count; m++) seedPoly[m] = -1;
            int seedOf(int m) => m >= 0 && m < seedPoly.Length ? seedPoly[m] : -1;
            for (int m = 0; m < markers.Count; m++)
            {
                int seed = PolyUnder(markers[m], polyVerts, centroid, 2.5f);
                if (seed < 0) { blindMarkers++; continue; }
                seedPoly[m] = seed;
                float bias = m < networks.Count ? ClassBias(networks[m].RoomSizeValue) : 0.0f;
                if (bias < polyBest[seed])
                {
                    pq.Remove((polyBest[seed], seed));
                    polyBest[seed] = bias;
                    polyOwner[seed] = m;
                    pq.Add((bias, seed));
                }
                seeded++;
            }
            while (pq.Count > 0)
            {
                var (cost, p) = pq.Min;
                pq.Remove(pq.Min);
                if (cost > polyBest[p]) continue;
                int m = polyOwner[p];
                float cap = float.MaxValue;
                if (_settings.RoomExtentScale > 0.0f && m >= 0 && m < networks.Count)
                {
                    float classCap = ExtentCap(networks[m].RoomSizeValue);
                    if (classCap < float.MaxValue) cap = classCap * _settings.RoomExtentScale;
                }
                // A doorway polygon (barrier area) may be stood in but not passed through: the
                // door node in it goes to the room that reaches it first, the room beyond does not.
                if (p != seedOf(m) && nav.Polygons[p].area.GetId() != 0) continue;
                foreach (int q in adj[p])
                {
                    float next = cost + Vector3.Distance(centroid[p], centroid[q]);
                    if (next >= polyBest[q]) continue;
                    if (cap < float.MaxValue && m >= 0 && m < markers.Count && Vector3.Distance(markers[m], centroid[q]) > cap) continue;
                    pq.Remove((polyBest[q], q));
                    polyBest[q] = next;
                    polyOwner[q] = m;
                    pq.Add((next, q));
                }
            }

            int reached = 0, noPoly = 0;
            var floorless = new bool[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                int p = PolyUnder(positions[i], polyVerts, centroid, 1.0f);
                if (p < 0) { noPoly++; floorless[i] = true; continue; }
                if (polyOwner[p] < 0) continue;
                owner[i] = polyOwner[p];
                best[i] = polyBest[p] + Vector3.Distance(positions[i], centroid[p]);
                reached++;
            }

            // A node with no polygon beneath - a ladder rung, a node on top of a prop - takes the
            // owner of the cheapest node it can see that has one, through other such nodes if it
            // must. Nodes standing on a polygon no marker reached stay unowned: that is retail's
            // sealed mezzanine, not a gap to paper over.
            int adopted = 0;
            if (adjacency != null && noPoly > 0)
            {
                var pq2 = new SortedSet<(float cost, int node)>();
                for (int i = 0; i < positions.Count; i++)
                    if (best[i] < float.MaxValue) pq2.Add((best[i], i));
                while (pq2.Count > 0)
                {
                    var (cost, node) = pq2.Min;
                    pq2.Remove(pq2.Min);
                    if (cost > best[node]) continue;
                    foreach (var (to, step) in adjacency[node])
                    {
                        if (!floorless[to]) continue;
                        float next = cost + step;
                        if (next >= best[to]) continue;
                        if (best[to] == float.MaxValue) adopted++;
                        pq2.Remove((best[to], to));
                        best[to] = next;
                        owner[to] = owner[node];
                        pq2.Add((next, to));
                    }
                }
            }
            log?.Invoke("Sound networks: navmesh flood - " + seeded + " marker(s) seeded, " + blindMarkers +
                        " off the navmesh, " + offMesh + " off-mesh link(s) followed; " + reached + " of " +
                        positions.Count + " node(s) reached, " + noPoly + " with no polygon beneath of which " +
                        adopted + " adopted by sight.");
        }

        /// <summary>The polygon a point stands on: containing it in plan and 0.5 m below to 2.5 m above; else the nearest centroid within the fallback radius.</summary>
        private static int PolyUnder(Vector3 p, Vector3[][] polyVerts, Vector3[] centroid, float fallbackRadius)
        {
            int found = -1; float bestScore = float.MaxValue;
            for (int i = 0; i < polyVerts.Length; i++)
            {
                if (float.IsNaN(centroid[i].X)) continue;
                float dy = p.Y - centroid[i].Y;
                if (dy < -0.5f || dy > 2.5f) continue;
                if (Math.Abs(centroid[i].X - p.X) > 8.0f || Math.Abs(centroid[i].Z - p.Z) > 8.0f) continue;
                if (!InsideXZ(polyVerts[i], p)) continue;
                float score = Math.Abs(dy - 0.46f);
                if (score < bestScore) { bestScore = score; found = i; }
            }
            if (found >= 0) return found;
            float bestD = fallbackRadius * fallbackRadius;
            for (int i = 0; i < polyVerts.Length; i++)
            {
                if (float.IsNaN(centroid[i].X)) continue;
                float dy = p.Y - centroid[i].Y;
                if (dy < -0.5f || dy > 2.5f) continue;
                float dx = centroid[i].X - p.X, dz = centroid[i].Z - p.Z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; found = i; }
            }
            return found;
        }

        private static bool InsideXZ(Vector3[] v, Vector3 p)
        {
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                if ((v[i].Z > p.Z) != (v[j].Z > p.Z) &&
                    p.X < (v[j].X - v[i].X) * (p.Z - v[i].Z) / (v[j].Z - v[i].Z) + v[i].X)
                    inside = !inside;
            }
            return inside;
        }

        private static bool Visible(BVHAccel occluders, Vector3 from, Vector3 to)
        {
            if (occluders == null) return true;

            /* Crossings walks the whole ray, stepping past surface after surface, because callers
             * that want the obstruction count need all of them. This one only asks whether the
             * count is zero, and Crossings is zero exactly when one of its two lines is unbroken -
             * min(raised, ground) == 0 iff either is - so a single first-hit test on each answers
             * it. The scatter fires about a million of these on a level and nearly all of them are
             * blocked, which is the case the walk is slowest on. */
            switch (_settings.SightTestMode)
            {
                case 1: return !AnyCrossingAt(occluders, from, to);
                case 2: return !AnyCrossingAt(occluders, from + VisibilityTestHeight, to + VisibilityTestHeight);
                case 3: return !AnyCrossingAt(occluders, from, to) && !AnyCrossingAt(occluders, from + VisibilityTestHeight, to + VisibilityTestHeight);
            }
            return !AnyCrossingAt(occluders, from + VisibilityTestHeight, to + VisibilityTestHeight)
                || !AnyCrossingAt(occluders, from, to);
        }

        /// <summary>
        /// Does this line cross anything at all? Matches the first step of <see cref="CrossingsAt"/>:
        /// that returns zero exactly when its opening traversal misses.
        /// </summary>
        private static bool AnyCrossingAt(BVHAccel occluders, Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.Length();
            if (distance <= 0.05f) return false;
            Vector3 direction = delta / distance;

            const float slack = 0.02f;
            var ray = new Ray(from + direction * slack, direction, 0.0f, distance - slack - slack);
            return occluders.Traverse(ref ray, out Hit _);
        }

        private struct Link
        {
            public int A, B;
            public float Distance;
            public byte Path, Obstruction;

            /// <summary>Raw surfaces the sight line crosses; zero is a clear view.</summary>
            public int Crossed;
        }

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
        private static List<Link> BuildLinks(List<Vector3> positions, BVHAccel occluders,
                                             float radius, Action<string> log)
        {
            /* Every pair is independent and the raycast dominates, so the rows are built in
             * parallel and then concatenated in order - the link list stays exactly as the
             * sequential loop produced it, which matters because the writer's order follows it. */
            var rows = new List<Link>[positions.Count];
            int obstructed = 0;
            System.Threading.Tasks.Parallel.For(0, positions.Count, i =>
            {
                List<Link> row = null;
                int rowObstructed = 0;
                for (int j = i + 1; j < positions.Count; j++)
                {
                    float distance = Vector3.Distance(positions[i], positions[j]);
                    if (distance > radius) continue;

                    int crossed = Crossings(occluders, positions[i], positions[j]);
                    byte block = (byte)Math.Max(0, crossed - ClearSurfaceTolerance);

                    // An obstructed pair is still linked - retail records the obstruction rather than
                    // dropping the link - but only while the blockage is slight, which is the shape
                    // of retail's own ObstructedDistance distribution.
                    if (block > MaxObstruction) continue;
                    if (block > 0) rowObstructed++;
                    (row ??= new List<Link>()).Add(new Link
                    {
                        A = i,
                        B = j,
                        Distance = distance,
                        // Retail truncates: PathDistance == floor(distance) on 7638 of 7720 unobstructed
                        // ChallengeMap9 links, 46877 of 46886 on Tech_Hub, 1410 of 1410 on BSP_TORRENS.
                        Path = (byte)Math.Min(255, (int)Math.Floor(distance)),
                        Obstruction = block,
                        Crossed = crossed,
                    });
                }
                rows[i] = row;
                if (rowObstructed != 0)
                    System.Threading.Interlocked.Add(ref obstructed, rowObstructed);
            });

            int total = 0;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] != null) total += rows[i].Count;
            var links = new List<Link>(total);
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] != null) links.AddRange(rows[i]);
            log?.Invoke("Sound links: " + links.Count + " within " + radius.ToString("0.#") + " m, " +
                        obstructed + " obstructed (" + (100.0 * obstructed / Math.Max(1, links.Count)).ToString("0.0") + "%)");
            return links;
        }

        /// <summary>
        /// Give every node to the marker that reaches it most cheaply through the link graph.
        /// </summary>
        /// <remarks>
        /// <para>Straight-line distance to the nearest marker puts nodes in the room next door
        /// whenever a wall happens to be thin. Walking the links instead means a node joins the
        /// marker it is actually connected to.</para>
        /// <para>Only links with a clear line of sight are walked, which is stricter than the set
        /// that gets written out. A link through even one collision hull is enough to reach into a
        /// sealed vent from the room below it, and retail keeps those vents as networks of their
        /// own.</para>
        /// </remarks>
        private static int[] AssignToNetworks(List<Vector3> positions, List<Link> links, List<Vector3> markers,
                                              List<SoundNodeNetwork.NetworkInfo> networks, BVHAccel occluders,
                                              Action<string> log, NavigationMesh nav = null)
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
                // walls, which had two markers claiming the same space with the nodes split
                // arbitrarily between them.
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
            // A marker's seed cost is its class bias: rooms flood from zero, passages and ducts
            // start a few metres "behind", so a contested node goes to the room. See
            // SoundNetworkBakeSettings.ClassSeedBias.
            for (int pass = 0; pass < 2; pass++)
                for (int m = 0; m < markers.Count; m++)
                {
                    if (seeds[m] < 0 || blind[m] != (pass == 1)) continue;
                    int seed = seeds[m];
                    float bias = m < networks.Count ? ClassBias(networks[m].RoomSizeValue) : 0.0f;
                    if (bias < best[seed]) { best[seed] = bias; owner[seed] = m; queue.Add((bias, seed)); }
                }

            // Under MarkerSeedMode 1 a marker seeds EVERY node it can see, each at its straight-line
            // distance, not just the nearest one. The link graph is walked node to node, so a node a
            // marker can see plainly but which no OTHER node of that marker can see is left behind in
            // a sealed pocket of its own. Seeding at distance rather than zero keeps the marker that
            // is actually in a room ahead of one peering in through a window from further away.
            if (_settings.MarkerSeedMode == 1)
                for (int m = 0; m < markers.Count; m++)
                {
                    if (blind[m]) continue;
                    float bias1 = m < networks.Count ? ClassBias(networks[m].RoomSizeValue) : 0.0f;
                    for (int i = 0; i < positions.Count; i++)
                    {
                        float cost = Vector3.Distance(markers[m], positions[i]) + bias1;
                        if (cost >= best[i]) continue;
                        if (!Visible(occluders, markers[m], positions[i])) continue;
                        queue.Remove((best[i], i));
                        best[i] = cost;
                        owner[i] = m;
                        queue.Add((cost, i));
                    }
                }

            WarnOnSharedRegions(positions, networks, seeds, blind, reach, log);

            // Under FloodMedium 1 the marker walks the NAVMESH instead of the node sight graph. The
            // sight seeding above is discarded; the sight adjacency still groups whatever the walk
            // never reached into sealed networks below. See SoundNetworkBakeSettings.FloodMedium.
            if (_settings.FloodMedium == 1 && nav != null && nav.Polygons != null && nav.Polygons.Length > 0)
            {
                queue.Clear();
                for (int i = 0; i < positions.Count; i++) { owner[i] = 0; best[i] = float.MaxValue; }
                NavmeshFlood(nav, positions, markers, networks, owner, best, log, adjacency);
            }

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

            // Nodes the graph never reached belong to no marker at all. Retail gives each such island
            // its own nameless network, with no reverb (65535) and no enter/exit events (-1). Handing
            // them to the nearest marker by straight line would fold an unreachable cupboard into the
            // room on the far side of its wall, so they are grouped among themselves instead.
            //
            // Grouping uses clear sight, the same as the reachability above. Grouping on the full
            // link set instead merges pockets that are only a wall apart, which retail keeps separate.
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
                float groupCap = _settings.SealedGroupMaxLink > 0.0f ? _settings.SealedGroupMaxLink : float.MaxValue;
                while (stack.Count > 0)
                {
                    int node = stack.Pop();
                    owner[node] = index;
                    foreach (var (to, step) in adjacency[node])
                    {
                        if (island[to] >= 0 || best[to] < float.MaxValue) continue;
                        // Retail keeps two lifts' door nodes 11.9 m apart in separate sealed
                        // networks with nothing between them; a sealed group is local. See
                        // SoundNetworkBakeSettings.SealedGroupMaxLink.
                        if (step > groupCap) continue;
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
        /// Measuring the span from the first hit to the last instead counts the air between two walls
        /// twenty metres apart as twenty metres of obstruction, which marked 96% of pairs blocked on
        /// BSP_TORRENS against retail's 40%. A surface count also matches the shape of retail's
        /// ObstructedDistance.
        /// </remarks>
        /// </remarks>
        private static int Crossings(BVHAccel occluders, Vector3 from, Vector3 to)
        {
            if (occluders == null) return 0;

            switch (_settings.SightTestMode)
            {
                case 1: return CrossingsAt(occluders, from, to);
                case 2: return CrossingsAt(occluders, from + VisibilityTestHeight, to + VisibilityTestHeight);
                case 3: return Math.Max(CrossingsAt(occluders, from, to), CrossingsAt(occluders, from + VisibilityTestHeight, to + VisibilityTestHeight));
            }
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
