#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;
using CATHODE.Enums;
using CathodeLib;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;

namespace CathodeLib.NavMesh
{
    public static class NavMeshBaker
    {
        const int DT_NULL_LINK = unchecked((int)0xffffffff);

        public sealed class BakeResult
        {
            public NavigationMesh NavMesh;
            public int InputTriangles;
            public int InputVertices;
            public int ReachablePolysKept;
            public int ReachablePolysCulled;
            public int ExclusionPolysCulled;
            public int BarrierPolysStamped;
            public int BarriersWithCoverage;
            public int BackstagePolys;
            public int BackstageConnections;
            public string BackstageWarning;
            public List<PathBarrierResources.NAV_MESH_BARRIER_RESOURCE> PathBarriers =
                new List<PathBarrierResources.NAV_MESH_BARRIER_RESOURCE>();
            public string Message;
        }

        /// <summary>
        /// Bakes a navmesh for every state in <see cref="Instancing.States"/> and hands the results
        /// back through the level: each <see cref="Level.State.NavMesh"/> is replaced, and
        /// PATH_BARRIER_RESOURCES is rebuilt from state 0. Nothing is written to disk here —
        /// <see cref="Level.Save"/> persists it in the same pass as the rest of the level so
        /// resource indexes stay consistent.
        /// </summary>
        public static void BakeLevel(
            Level level,
            Instancing instancing,
            NavMeshBakeSettings settings = null,
            Action<string> log = null)
        {
            if (level == null)
                throw new ArgumentNullException(nameof(level));
            if (instancing == null)
                throw new ArgumentNullException(nameof(instancing));

            IReadOnlyList<Instancing.StateProperties> states = instancing.States;
            if (states == null || states.Count == 0)
                throw new ArgumentException("No state data to bake - needs a full Instancing pass.", nameof(instancing));

            settings ??= NavMeshBakeSettings.CreateDefault();

            //State 0 collects the authoring data (seeds, off-mesh links, barriers) that later states reuse.
            CollisionNavMeshSoup sharedAuthoring = null;
            BakeResult firstResult = null;

            foreach (Instancing.StateProperties state in states)
            {
                log?.Invoke("NavMesh STATE_" + state.StateIndex + ": " + state.Summary);

                CollisionNavMeshSoup soup = CollisionNavMeshSoup.CollectFromLevel(
                    level,
                    state.ExcludedCollision != null && state.ExcludedCollision.Count > 0 ? state.ExcludedCollision : null,
                    sharedAuthoring,
                    settings,
                    instancing);
                if (sharedAuthoring == null)
                    sharedAuthoring = soup;

                log?.Invoke("  soup: verts=" + soup.VertexCount + " tris=" + soup.TriangleCount +
                            " seeds=" + soup.ReachabilitySeeds.Count + " excluded=" + (state.ExcludedCollision?.Count ?? 0) +
                            " propsSkipped=" + soup.PropInstancesSkipped + " absurdTrisCulled=" + soup.AbsurdTrisCulled);

                BakeResult result = Bake(soup, settings);
                log?.Invoke("  " + result.Message);

                if (state.State != null)
                    state.State.NavMesh = result.NavMesh;
                if (firstResult == null)
                    firstResult = result;
            }

            if (firstResult != null && level.PathBarrierResources != null)
            {
                level.PathBarrierResources.Entries.Clear();
                level.PathBarrierResources.Entries.AddRange(firstResult.PathBarriers);
                log?.Invoke("PATH_BARRIER_RESOURCES: " + firstResult.PathBarriers.Count + " barriers");
            }
        }

        public static BakeResult Bake(CollisionNavMeshSoup soup, NavMeshBakeSettings settings = null)
        {
            if (soup == null)
                throw new ArgumentNullException(nameof(soup));
            settings ??= NavMeshBakeSettings.CreateDefault();

            if (soup.TriangleCount <= 0)
                throw new InvalidOperationException("No triangles to bake.");

            AssignBarrierAreaIds(soup.Barriers);

            var geom = new RcSampleInputGeomProvider(soup.Verts, soup.Tris);
            AddExclusionConvexVolumes(geom, soup.ExclusionAreas);
            // Barriers are carved into the polygon layout rather than stamped over it, so their
            // flags land on the footprint the designer drew. Has to happen before the build.
            int[] barrierRecastAreas = AddBarrierConvexVolumes(geom, soup.Barriers, settings);

            RcVec3f bmin = geom.GetMeshBoundsMin();
            RcVec3f bmax = geom.GetMeshBoundsMax();
            // Align the tile origin to the cell grid so every emitted vertex lands on absolute
            // cell-size multiples, the way retail meshes do (their bmin is always grid-aligned).
            bmin = new RcVec3f(
                MathF.Floor(bmin.X / settings.CellSize) * settings.CellSize,
                MathF.Floor(bmin.Y / settings.CellHeight) * settings.CellHeight,
                MathF.Floor(bmin.Z / settings.CellSize) * settings.CellSize);

            float sizeX = bmax.X - bmin.X;
            float sizeZ = bmax.Z - bmin.Z;
            float sizeY = bmax.Y - bmin.Y;
            if (sizeX > settings.RecastMaxBoundsSize || sizeZ > settings.RecastMaxBoundsSize)
                throw new InvalidOperationException(
                    $"XZ bounds ({sizeX:F1} x {sizeZ:F1}) exceed recast_max_bounds_size ({settings.RecastMaxBoundsSize}). Tiling not implemented yet.");
            if (sizeY > settings.RecastMaxBoundsSizeY)
                throw new InvalidOperationException(
                    $"Y bounds ({sizeY:F1}) exceed recast_max_bounds_size_y ({settings.RecastMaxBoundsSizeY}).");

            // Detail distances are authored in world units; RcConfig expects the Recast-demo
            // multipliers (world = cell * multiplier) when multiplier >= 0.9.
            float detailDistParam = settings.DetailSampleDist / settings.CellSize;
            float detailErrorParam = settings.MaxDetailError / settings.CellHeight;

            var cfg = new RcConfig(
                useTiles: false,
                tileSizeX: 0,
                tileSizeZ: 0,
                borderSize: 0,
                partition: RcPartition.WATERSHED,
                cellSize: settings.CellSize,
                cellHeight: settings.CellHeight,
                agentMaxSlope: settings.WalkableSlopeAngle,
                agentHeight: settings.LowestNavigableHeight,
                agentRadius: settings.WalkableRadius,
                agentMaxClimb: settings.WalkableClimb,
                minRegionArea: settings.MinRegionArea,
                mergeRegionArea: settings.MergeRegionArea,
                edgeMaxLen: settings.MaxEdgeLength,
                edgeMaxError: settings.MaxContourError,
                vertsPerPoly: settings.MaxVertsInPolyMeshTriangle,
                detailSampleDist: detailDistParam,
                detailSampleMaxError: detailErrorParam,
                filterLowHangingObstacles: true,
                filterLedgeSpans: true,
                filterWalkableLowHeightSpans: true,
                walkableAreaMod: new RcAreaModification(RcRecast.RC_WALKABLE_AREA),
                buildMeshDetail: true);

            var bcfg = new RcBuilderConfig(cfg, bmin, bmax);
            var rcBuilder = new RcBuilder();
            // Keep compact heightfield for crouch / deep-crouch clearance sampling.
            RcBuilderResult rcResult = rcBuilder.Build(geom, bcfg, keepInterResults: true);
            if (rcResult?.Mesh == null || rcResult.Mesh.npolys <= 0)
                throw new InvalidOperationException("Recast produced an empty poly mesh.");

            DtNavMeshCreateParams createParams = BuildCreateParams(cfg, rcResult, settings);
            DtMeshData meshData = DtNavMeshBuilder.CreateNavMeshData(createParams);
            if (meshData == null)
                throw new InvalidOperationException("DtNavMeshBuilder.CreateNavMeshData failed.");

            var detour = new DtNavMesh();
            DtStatus status = detour.Init(meshData, settings.MaxVertsInPolyMeshTriangle, 0);
            if (status.Failed())
                throw new InvalidOperationException("DtNavMesh.Init failed: " + status);

            DtMeshTile tile = detour.GetTile(0);
            if (tile?.data == null)
                throw new InvalidOperationException("DtNavMesh produced no tile.");

            int polyCount = tile.data.header.polyCount;
            bool[] keep = null;
            int culled = 0;
            int exclusionCulled = 0;

            if (soup.ExclusionAreas != null && soup.ExclusionAreas.Count > 0)
            {
                keep = new bool[polyCount];
                for (int i = 0; i < polyCount; i++)
                    keep[i] = true;
                exclusionCulled = CullPolysInsideExclusions(tile, soup.ExclusionAreas, keep);
            }

            int islandCulled = 0;
            bool usedSeedFilter = false;
            bool usedIslandCull = false;

            if (settings.FilterUnreachable && soup.ReachabilitySeeds != null && soup.ReachabilitySeeds.Count > 0)
            {
                bool[] reachable = MarkReachablePolys(detour, tile, soup.ReachabilitySeeds, settings);
                int reachableCount = 0;
                for (int i = 0; i < reachable.Length; i++)
                    if (reachable[i]) reachableCount++;

                // If seeds only reach a tiny island (common when ExclusiveMaster
                // excludes large regions), fall through to island cull / keep exclusions.
                int minKeep = Math.Max(50, polyCount / 10);
                if (reachableCount >= minKeep)
                {
                    if (keep == null)
                        keep = reachable;
                    else
                    {
                        for (int i = 0; i < polyCount; i++)
                            keep[i] = keep[i] && reachable[i];
                    }
                    usedSeedFilter = true;
                }
            }

            if (!usedSeedFilter && settings.CullUnseededIslands)
            {
                // Torrens and many levels ship zero NavMeshReachabilitySeedPoint entities.
                // Recast still marks ceiling tops / duct lids as walkable; drop disconnected
                // islands whose median Y sits outside the primary floor band.
                bool[] floorKeep = MarkPrimaryFloorPolys(tile, settings);
                if (keep == null)
                {
                    keep = floorKeep;
                }
                else
                {
                    for (int i = 0; i < polyCount; i++)
                        keep[i] = keep[i] && floorKeep[i];
                }
                usedIslandCull = true;
                for (int i = 0; i < polyCount; i++)
                    if (!floorKeep[i]) islandCulled++;
            }

            int elevatedCulled = 0;
            if (keep != null)
            {
                elevatedCulled = StripElevatedPolys(tile, settings, keep);
                for (int i = 0; i < keep.Length; i++)
                    if (!keep[i]) culled++;
            }

            int stamped = 0;
            int barriersWithCoverage = 0;
            NavigationMesh.dt_area_t[] polyAreas = BuildPolyAreas(
                tile, soup.Barriers, barrierRecastAreas, settings, keep, out stamped, out barriersWithCoverage);

            int crouchMarked = 0, deepCrouchMarked = 0;
            ApplyHeightLimitedMarkup(
                tile, rcResult.CompactHeightfield, settings, keep, polyAreas,
                out crouchMarked, out deepCrouchMarked);

            // Backstage: the alien's ceiling sheet, triangulated over the node tops. Failure is
            // a warning, not an error - the level simply ships without a backstage.
            BackstageMeshBuilder.Result backstage = null;
            string backstageWarning = null;
            List<CollisionNavMeshSoup.OffMeshLinkDraft> offMeshDrafts = soup.OffMeshLinks;
            if (soup.BackstageNodes != null && soup.BackstageNodes.Count > 0)
            {
                backstage = BuildBackstageSheets(soup.BackstageNodes, settings, out backstageWarning);

                // Each node becomes a vertical Backstage off-mesh connection from its frontstage
                // mouth to the sheet, whether or not its own network managed to triangulate -
                // retail ENG_TowPlatform ships all 19 connections with a sheet over only some of
                // them. Retail admits only the alien on these.
                offMeshDrafts = new List<CollisionNavMeshSoup.OffMeshLinkDraft>(soup.OffMeshLinks ?? new List<CollisionNavMeshSoup.OffMeshLinkDraft>());
                foreach (CollisionNavMeshSoup.BackstageNodeDraft node in soup.BackstageNodes)
                {
                    offMeshDrafts.Add(new CollisionNavMeshSoup.OffMeshLinkDraft
                    {
                        Start = node.Bottom,
                        End = node.Bottom + new Vector3(0f, settings.BackstageNodeHeight, 0f),
                        LinkType = NavigationMesh.OffMeshLinkType.Backstage,
                        ExtraCost = node.ExtraCost,
                        CharacterClasses = NAVIGATION_CHARACTER_CLASS_COMBINATION.ALIEN,
                        OpenOnReset = node.OpenOnReset,
                        Entity = node.Entity,
                    });
                }
            }

            NavigationMesh nav = new NavigationMesh("");
            AdaptTile(nav, tile, settings, keep, polyAreas, offMeshDrafts, backstage);

            var pathBarriers = new List<PathBarrierResources.NAV_MESH_BARRIER_RESOURCE>(soup.Barriers?.Count ?? 0);
            if (soup.Barriers != null)
            {
                for (int i = 0; i < soup.Barriers.Count; i++)
                {
                    CollisionNavMeshSoup.BarrierVolume b = soup.Barriers[i];
                    pathBarriers.Add(new PathBarrierResources.NAV_MESH_BARRIER_RESOURCE
                    {
                        Resource = b.Resource,
                        area_id = b.AreaId,
                        allowed_character_classes = b.InitialClasses
                    });
                }
            }

            string message;
            if (keep == null)
            {
                if (settings.FilterUnreachable && soup.ReachabilitySeeds != null && soup.ReachabilitySeeds.Count > 0)
                    message = "Unreachable filter skipped (seed flood too small vs mesh).";
                else if (settings.FilterUnreachable && !settings.CullUnseededIslands)
                    message = "Unreachable filter skipped (no reachability seeds; island cull off).";
                else if (!settings.FilterUnreachable && !settings.CullUnseededIslands)
                    message = "Reachability / island filters disabled.";
                else
                    message = "No poly filter applied.";
            }
            else if (usedSeedFilter)
            {
                message = $"Seed filter removed {culled} polys (exclusion={exclusionCulled}, elevatedStrip={elevatedCulled}).";
            }
            else if (usedIslandCull)
            {
                message = $"Island cull removed {culled} polys (island={islandCulled}, elevatedStrip={elevatedCulled}, exclusion={exclusionCulled}).";
            }
            else
            {
                message = $"Filter removed {culled} polys (exclusion={exclusionCulled}, elevatedStrip={elevatedCulled}).";
            }
            int carvedBarriers = 0;
            for (int i = 0; i < barrierRecastAreas.Length; i++)
                if (barrierRecastAreas[i] > 0) carvedBarriers++;

            message += $" Barriers={soup.Barriers?.Count ?? 0} carved={carvedBarriers} stampedPolys={stamped} covered={barriersWithCoverage}."
                + $" Platforms={soup.WalkablePlatforms?.Count ?? 0} exclusions={soup.ExclusionAreas?.Count ?? 0}."
                + $" HeightLimited crouch={crouchMarked} deepCrouch={deepCrouchMarked}.";

            int backstagePolys = backstage != null ? backstage.Triangles.Count / 3 : 0;
            int backstageCons = soup.BackstageNodes?.Count ?? 0;
            if (soup.BackstageNodes != null && soup.BackstageNodes.Count > 0)
            {
                var networkCounts = new SortedDictionary<int, int>();
                foreach (CollisionNavMeshSoup.BackstageNodeDraft node in soup.BackstageNodes)
                {
                    networkCounts.TryGetValue(node.NetworkId, out int n);
                    networkCounts[node.NetworkId] = n + 1;
                }
                var parts = new List<string>();
                foreach (KeyValuePair<int, int> kv in networkCounts)
                    parts.Add($"{kv.Key}:x{kv.Value}");
                message += $" Backstage nodes={soup.BackstageNodes.Count} networks=[{string.Join(" ", parts)}] polys={backstagePolys} cons={backstageCons}.";
            }
            if (backstageWarning != null)
                message += $" BACKSTAGE WARNING: {backstageWarning}.";

            return new BakeResult
            {
                BackstagePolys = backstagePolys,
                BackstageConnections = backstageCons,
                BackstageWarning = backstageWarning,
                NavMesh = nav,
                InputTriangles = soup.TriangleCount,
                InputVertices = soup.VertexCount,
                ReachablePolysKept = keep == null ? tile.data.header.polyCount : keep.Length - culled,
                ReachablePolysCulled = culled,
                ExclusionPolysCulled = exclusionCulled,
                BarrierPolysStamped = stamped,
                BarriersWithCoverage = barriersWithCoverage,
                PathBarriers = pathBarriers,
                Message = message
            };
        }

        static void AssignBarrierAreaIds(List<CollisionNavMeshSoup.BarrierVolume> barriers)
        {
            if (barriers == null)
                return;
            for (int i = 0; i < barriers.Count; i++)
            {
                if (barriers[i].AreaId <= 0)
                    barriers[i].AreaId = i + 1;
                if (barriers[i].AreaId > 511)
                    throw new InvalidOperationException("Barrier area id exceeds 9-bit dt_area_t limit (511).");
            }
        }

        static void AddExclusionConvexVolumes(
            RcSampleInputGeomProvider geom,
            List<CollisionNavMeshSoup.AuthoringBoxVolume> exclusions)
        {
            if (exclusions == null || exclusions.Count == 0)
                return;

            var nullArea = new RcAreaModification(RcRecast.RC_NULL_AREA);
            for (int i = 0; i < exclusions.Count; i++)
            {
                CollisionNavMeshSoup.AuthoringBoxVolume box = exclusions[i];
                CollisionNavMeshSoup.OrientedBoxAabb(box.Centre, box.Rotation, box.HalfExtents, out Vector3 amin, out Vector3 amax);
                // The rotated footprint, not its axis-aligned bound - a volume turned 45 degrees
                // would otherwise carve away twice the floor the designer drew.
                geom.AddConvexVolume(FootprintXZ(box.Centre, box.Rotation, box.HalfExtents), amin.Y, amax.Y, nullArea);
            }
        }

        // Recast area ids are 6 bits (RcAreaModification.RC_AREA_FLAGS_MASK). 0 means unwalkable
        // and RC_WALKABLE_AREA means plain floor, so barriers share what is left.
        const int FirstBarrierRecastArea = 1;
        const int LastBarrierRecastArea = RcRecast.RC_WALKABLE_AREA - 1;

        /// <summary>
        /// Give each barrier its own Recast area id and add its footprint to the geometry as a
        /// convex volume, so the contour builder cuts the navmesh along the barrier's own outline.
        /// Returns the id chosen per barrier, or -1 for one that could not be given a distinct id.
        /// </summary>
        /// <remarks>
        /// This is what makes a barrier's flags apply to the shape the designer drew rather than to
        /// whichever polygons happened to overlap it. Recast never lets a region span two area ids
        /// (see RcRegions.CanMergeWithRegion), so a 1x1 barrier produces a 1x1 hole in the polygon
        /// layout and the area stamp lands on exactly those polys.
        ///
        /// There are only 62 usable ids and levels carry more barriers than that - SCI_Hub has 77 -
        /// so ids are reused between barriers whose padded bounds do not overlap. Same-id barriers
        /// are therefore too far apart to end up in one region, and the stamping pass can tell them
        /// apart by position.
        /// </remarks>
        static int[] AddBarrierConvexVolumes(
            RcSampleInputGeomProvider geom,
            List<CollisionNavMeshSoup.BarrierVolume> barriers,
            NavMeshBakeSettings settings)
        {
            if (barriers == null || barriers.Count == 0)
                return Array.Empty<int>();

            int count = barriers.Count;
            var mins = new Vector3[count];
            var maxs = new Vector3[count];
            var padded = new Vector3(Math.Max(0f, settings.BarrierAreaIdSeparation));
            for (int i = 0; i < count; i++)
            {
                CollisionNavMeshSoup.BarrierVolume b = barriers[i];
                CollisionNavMeshSoup.OrientedBoxAabb(b.Centre, b.Rotation, b.HalfExtents, out mins[i], out maxs[i]);
                mins[i] -= padded;
                maxs[i] += padded;
            }

            var areas = new int[count];
            var taken = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                taken.Clear();
                for (int j = 0; j < i; j++)
                {
                    if (areas[j] <= 0)
                        continue;
                    if (maxs[i].X < mins[j].X || mins[i].X > maxs[j].X
                        || maxs[i].Y < mins[j].Y || mins[i].Y > maxs[j].Y
                        || maxs[i].Z < mins[j].Z || mins[i].Z > maxs[j].Z)
                        continue;
                    taken.Add(areas[j]);
                }

                areas[i] = -1;
                for (int a = FirstBarrierRecastArea; a <= LastBarrierRecastArea; a++)
                {
                    if (taken.Contains(a))
                        continue;
                    areas[i] = a;
                    break;
                }
                if (areas[i] < 0)
                    continue;

                CollisionNavMeshSoup.BarrierVolume barrier = barriers[i];
                CollisionNavMeshSoup.OrientedBoxAabb(
                    barrier.Centre, barrier.Rotation, barrier.HalfExtents, out Vector3 bmin, out Vector3 bmax);
                // Reach a little below the box: a barrier authored flush with the floor would
                // otherwise miss the floor span it is meant to cover once heights are quantised.
                geom.AddConvexVolume(
                    FootprintXZ(barrier.Centre, barrier.Rotation, barrier.HalfExtents),
                    bmin.Y - settings.WalkableClimb,
                    bmax.Y,
                    new RcAreaModification(areas[i]));
            }
            return areas;
        }

        /// <summary>The oriented box's footprint on the ground, as x/y/z triples with y unused.</summary>
        static float[] FootprintXZ(Vector3 centre, Quaternion rotation, Vector3 halfExtents)
        {
            var corners = new Vector2[8];
            for (int corner = 0; corner < 8; corner++)
            {
                var local = new Vector3(
                    (corner & 1) == 0 ? -halfExtents.X : halfExtents.X,
                    (corner & 2) == 0 ? -halfExtents.Y : halfExtents.Y,
                    (corner & 4) == 0 ? -halfExtents.Z : halfExtents.Z);
                Vector3 world = centre + Vector3.Transform(local, rotation);
                corners[corner] = new Vector2(world.X, world.Z);
            }

            List<Vector2> hull = ConvexHullXZ(corners);
            var verts = new float[hull.Count * 3];
            for (int i = 0; i < hull.Count; i++)
            {
                verts[i * 3 + 0] = hull[i].X;
                verts[i * 3 + 1] = 0f;
                verts[i * 3 + 2] = hull[i].Y;
            }
            return verts;
        }

        /// <summary>Andrew's monotone chain, counter-clockwise, over a handful of points.</summary>
        static List<Vector2> ConvexHullXZ(Vector2[] points)
        {
            Array.Sort(points, (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

            var hull = new List<Vector2>(points.Length + 1);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 p = pass == 0 ? points[i] : points[points.Length - 1 - i];
                    while (hull.Count - start >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;

            float Cross(Vector2 o, Vector2 a, Vector2 b) =>
                (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        static int CullPolysInsideExclusions(
            DtMeshTile tile,
            List<CollisionNavMeshSoup.AuthoringBoxVolume> exclusions,
            bool[] keep)
        {
            int culled = 0;
            DtMeshData data = tile.data;
            for (int i = 0; i < data.header.polyCount; i++)
            {
                if (!keep[i])
                    continue;
                DtPoly poly = data.polys[i];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                    continue;

                Vector3 centroid = PolyCentroid(data, poly);
                for (int e = 0; e < exclusions.Count; e++)
                {
                    CollisionNavMeshSoup.AuthoringBoxVolume box = exclusions[e];
                    if (CollisionNavMeshSoup.PointInOrientedBox(centroid, box.Centre, box.Rotation, box.HalfExtents))
                    {
                        keep[i] = false;
                        culled++;
                        break;
                    }
                }
            }
            return culled;
        }

        /// <summary>
        /// Stamp each barrier's id and admittance onto the polygons Recast carved out for it.
        /// </summary>
        /// <remarks>
        /// The polygons come pre-cut to the barrier footprint by
        /// <see cref="AddBarrierConvexVolumes"/>, so this is a lookup by area id rather than a
        /// geometric test. Only barriers that could not be given an id of their own still go
        /// through the old "does the poly overlap the volume" path, which over-stamps.
        /// </remarks>
        static NavigationMesh.dt_area_t[] BuildPolyAreas(
            DtMeshTile tile,
            List<CollisionNavMeshSoup.BarrierVolume> barriers,
            int[] barrierRecastAreas,
            NavMeshBakeSettings settings,
            bool[] keep,
            out int stampedCount,
            out int barriersWithCoverage)
        {
            stampedCount = 0;
            barriersWithCoverage = 0;
            DtMeshData data = tile.data;
            int polyCount = data.header.polyCount;
            var areas = new NavigationMesh.dt_area_t[polyCount];
            NavigationMesh.dt_area_t ground = NavigationMesh.CreateDefaultGroundArea();
            for (int i = 0; i < polyCount; i++)
                areas[i] = ground;

            if (barriers == null || barriers.Count == 0)
                return areas;

            var byRecastArea = new Dictionary<int, List<int>>();
            var uncarved = new List<int>();
            for (int b = 0; b < barriers.Count; b++)
            {
                int recastArea = barrierRecastAreas != null && b < barrierRecastAreas.Length ? barrierRecastAreas[b] : -1;
                if (recastArea <= 0)
                {
                    uncarved.Add(b);
                    continue;
                }
                if (!byRecastArea.TryGetValue(recastArea, out List<int> sharing))
                    byRecastArea[recastArea] = sharing = new List<int>();
                sharing.Add(b);
            }

            float inflate = settings.WalkableRadius + settings.CellSize * 2f;
            var covered = new bool[barriers.Count];

            for (int i = 0; i < polyCount; i++)
            {
                if (keep != null && i < keep.Length && !keep[i])
                    continue;
                DtPoly poly = data.polys[i];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                    continue;

                Vector3 centroid = PolyCentroid(data, poly);
                int hitBarrier = -1;

                if (byRecastArea.TryGetValue(poly.GetArea(), out List<int> candidates))
                {
                    // One id can be shared by barriers that are far apart, so pick by position.
                    hitBarrier = candidates[0];
                    if (candidates.Count > 1)
                    {
                        float best = float.MaxValue;
                        for (int c = 0; c < candidates.Count; c++)
                        {
                            CollisionNavMeshSoup.BarrierVolume candidate = barriers[candidates[c]];
                            float d = Vector3.DistanceSquared(centroid, candidate.Centre);
                            if (d >= best)
                                continue;
                            best = d;
                            hitBarrier = candidates[c];
                        }
                    }
                }
                else if (uncarved.Count > 0)
                {
                    PolyAabb(data, poly, out Vector3 polyMin, out Vector3 polyMax);
                    for (int u = 0; u < uncarved.Count; u++)
                    {
                        if (!PolyOverlapsBarrier(data, poly, centroid, polyMin, polyMax, barriers[uncarved[u]], inflate))
                            continue;
                        hitBarrier = uncarved[u];
                        break;
                    }
                }

                if (hitBarrier < 0)
                    continue;

                CollisionNavMeshSoup.BarrierVolume hit = barriers[hitBarrier];
                NavigationMesh.dt_area_t area = NavigationMesh.CreateDefaultGroundArea();
                area.SetId((ushort)hit.AreaId);
                area.SetAdmittanceFlags(hit.InitialClasses);
                areas[i] = area;
                stampedCount++;
                covered[hitBarrier] = true;
            }

            for (int i = 0; i < covered.Length; i++)
                if (covered[i]) barriersWithCoverage++;

            return areas;
        }

        /// <summary>
        /// Classify walkable CHF spans by clearance, dilate on the cell grid
        /// (height_limited_area_spread), then stamp the resulting class onto polys.
        /// Dilation is masked to cells covered by kept nav polys so elevated junk
        /// spans cannot flood the floor with DeepCrouch.
        /// </summary>
        /// <summary>
        /// Wire each off-mesh connection into the link pool, the way Detour's
        /// <c>baseOffMeshLinks</c> does and the way retail data ships: four links per connection -
        /// ground-&gt;offmesh and offmesh-&gt;ground at each of the two endpoints.
        /// </summary>
        /// <remarks>
        /// Without these the off-mesh polys exist but are unreachable, so ladders, vents and
        /// traversals silently stop working. Retail Solace has 58 connections and exactly
        /// 58 * 4 = 232 links beyond its 1728 internal ones.
        /// </remarks>
        static void ConnectOffMeshLinks(
            NavigationMesh.dtPoly[] polys,
            List<Vector3> verts,
            NavigationMesh.dtOffMeshConnection[] connections,
            int groundPolyCount,
            uint polyRefBase,
            List<NavigationMesh.dtLink> links)
        {
            if (connections == null || connections.Length == 0)
                return;

            for (int c = 0; c < connections.Length; c++)
            {
                NavigationMesh.dtOffMeshConnection con = connections[c];
                int offPolyIndex = con.poly_index_within_tile;
                if (offPolyIndex < 0 || offPolyIndex >= polys.Length)
                    continue;

                var start = new Vector3(con.pos[0], con.pos[1], con.pos[2]);
                var end = new Vector3(con.pos[3], con.pos[4], con.pos[5]);

                // Endpoint 0 sits on edge 0 of the off-mesh poly, endpoint 1 on edge 1.
                LinkEndpoint(start, 0);
                LinkEndpoint(end, 1);

                void LinkEndpoint(Vector3 position, byte edge)
                {
                    int ground = FindNearestGroundPoly(polys, verts, groundPolyCount, position, con.rad);
                    if (ground < 0)
                        return;

                    // ground -> off-mesh
                    NavigationMesh.dtPoly gp = polys[ground];
                    links.Add(new NavigationMesh.dtLink
                    {
                        polygonRef = polyRefBase | (uint)offPolyIndex,
                        next = gp.firstLink,
                        edge = 0xff,
                        side = 0xff,
                        bmin = 0,
                        bmax = 0
                    });
                    gp.firstLink = links.Count - 1;
                    polys[ground] = gp;

                    // off-mesh -> ground
                    NavigationMesh.dtPoly op = polys[offPolyIndex];
                    links.Add(new NavigationMesh.dtLink
                    {
                        polygonRef = polyRefBase | (uint)ground,
                        next = op.firstLink,
                        edge = edge,
                        side = 0xff,
                        bmin = 0,
                        bmax = 0
                    });
                    op.firstLink = links.Count - 1;
                    polys[offPolyIndex] = op;
                }
            }
        }

        /// <summary>
        /// Ground poly whose polygon contains <paramref name="position"/> in XZ, preferring the
        /// closest in height; falls back to the nearest vertex within <paramref name="radius"/>.
        /// </summary>
        static int FindNearestGroundPoly(
            NavigationMesh.dtPoly[] polys, List<Vector3> verts, int groundPolyCount, Vector3 position, float radius)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            float searchSq = Math.Max(radius, 1.0f);
            searchSq *= searchSq;

            for (int i = 0; i < groundPolyCount && i < polys.Length; i++)
            {
                NavigationMesh.dtPoly p = polys[i];
                if (p.vertCount < 3)
                    continue;

                bool inside = PointInPolyXZ(position, p, verts);
                float score;
                if (inside)
                {
                    // Among containing polys pick the one whose surface is nearest in height -
                    // vertex distance would let the floor 6 m below a large backstage triangle
                    // beat the triangle whose middle the endpoint actually sits on.
                    float dy = PolyHeightAtXZ(p, verts, position) - position.Y;
                    score = dy * dy * 0.001f;
                }
                else
                {
                    score = float.MaxValue;
                    for (int v = 0; v < p.vertCount; v++)
                    {
                        Vector3 pv = verts[p.verts[v]];
                        float dx = pv.X - position.X, dz = pv.Z - position.Z, dy = pv.Y - position.Y;
                        float d = dx * dx + dz * dz + dy * dy;
                        if (d < score) score = d;
                    }
                    if (score > searchSq) continue;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Surface height of a poly at an XZ position, via its triangle fan (average Y fallback).</summary>
        static float PolyHeightAtXZ(NavigationMesh.dtPoly poly, List<Vector3> verts, Vector3 position)
        {
            Vector3 a = verts[poly.verts[0]];
            for (int v = 1; v + 1 < poly.vertCount; v++)
            {
                Vector3 b = verts[poly.verts[v]];
                Vector3 c = verts[poly.verts[v + 1]];
                float det = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
                if (Math.Abs(det) < 1e-8f)
                    continue;
                float u = ((position.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (position.Z - a.Z)) / det;
                float w = ((b.X - a.X) * (position.Z - a.Z) - (position.X - a.X) * (b.Z - a.Z)) / det;
                if (u < -0.01f || w < -0.01f || u + w > 1.01f)
                    continue;
                return a.Y + u * (b.Y - a.Y) + w * (c.Y - a.Y);
            }

            float sum = 0f;
            for (int v = 0; v < poly.vertCount; v++)
                sum += verts[poly.verts[v]].Y;
            return sum / Math.Max(1, (int)poly.vertCount);
        }

        static bool PointInPolyXZ(Vector3 point, NavigationMesh.dtPoly poly, List<Vector3> verts)
        {
            bool inside = false;
            for (int i = 0, j = poly.vertCount - 1; i < poly.vertCount; j = i++)
            {
                Vector3 vi = verts[poly.verts[i]];
                Vector3 vj = verts[poly.verts[j]];
                if (((vi.Z > point.Z) != (vj.Z > point.Z)) &&
                    (point.X < (vj.X - vi.X) * (point.Z - vi.Z) / (vj.Z - vi.Z) + vi.X))
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Grow the link array to leave runtime headroom, returning the pool size to advertise.
        /// Padding entries are inert (polygonRef 0, next = DT_NULL_LINK).
        /// </summary>
        static int PadLinkPool(List<NavigationMesh.dtLink> links)
        {
            int used = links.Count;
            int target = used + used / 2 + 64;
            for (int i = used; i < target; i++)
            {
                links.Add(new NavigationMesh.dtLink
                {
                    polygonRef = 0,
                    next = DT_NULL_LINK,
                    edge = 0,
                    side = 0,
                    bmin = 0,
                    bmax = 0
                });
            }
            return links.Count;
        }

        static void ApplyHeightLimitedMarkup(
            DtMeshTile tile,
            RcCompactHeightfield chf,
            NavMeshBakeSettings settings,
            bool[] keep,
            NavigationMesh.dt_area_t[] areas,
            out int crouchCount,
            out int deepCrouchCount)
        {
            crouchCount = 0;
            deepCrouchCount = 0;
            if (tile?.data == null || areas == null || chf?.spans == null || chf.cells == null)
                return;

            DtMeshData data = tile.data;
            int polyCount = data.header.polyCount;

            for (int i = 0; i < polyCount; i++)
            {
                if (keep != null && i < keep.Length && !keep[i])
                    continue;
                DtPoly poly = data.polys[i];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                    continue;

                NavigationMesh.AreaHeight h = ClassifyClearance(PolyClearance(chf, data, poly, settings), settings);
                NavigationMesh.dt_area_t area = areas[i];
                area.SetHeightLimitedAmount(h);
                areas[i] = area;

                if (h == NavigationMesh.AreaHeight.Crouch)
                    crouchCount++;
                else if (h == NavigationMesh.AreaHeight.DeepCrouch)
                    deepCrouchCount++;
            }
        }

        /// <summary>
        /// Smallest headroom over a polygon: for each compact-heightfield column under it, the
        /// walkable span nearest the polygon's own surface, and the free height above that span.
        /// Positive infinity when nothing resolves, which classifies as Standing.
        /// </summary>
        /// <remarks>
        /// This was measured per cell rather than per polygon, and it took two things it should
        /// not have. It read chf.spans[cell.index], the LOWEST span in the column, so a polygon on
        /// an upper deck was judged by the headroom of the ground floor underneath it - which on a
        /// multi-storey level is the gap up to that very deck, and reliably reads as a crouch. And
        /// it did not skip spans the erosion pass had already killed, so the sliver of floor inside
        /// a wall counted as surface someone stands on. Both inflate the crouch classes.
        /// </remarks>
        static float PolyClearance(
            RcCompactHeightfield chf,
            DtMeshData data,
            DtPoly poly,
            NavMeshBakeSettings settings)
        {
            float worst = float.PositiveInfinity;
            float reach = settings.WalkableClimb + chf.ch * 2f;

            void Sample(float x, float y, float z)
            {
                int ix = (int)Math.Floor((x - chf.bmin.X) / chf.cs);
                int iz = (int)Math.Floor((z - chf.bmin.Z) / chf.cs);
                if (ix < 0 || iz < 0 || ix >= chf.width || iz >= chf.height)
                    return;

                RcCompactCell cell = chf.cells[ix + iz * chf.width];
                int nearest = -1;
                float nearestGap = reach;
                for (int s = cell.index; s < cell.index + cell.count; s++)
                {
                    if (chf.areas[s] == RcRecast.RC_NULL_AREA)
                        continue;
                    float gap = Math.Abs(chf.bmin.Y + chf.spans[s].y * chf.ch - y);
                    if (gap > nearestGap)
                        continue;
                    nearestGap = gap;
                    nearest = s;
                }
                if (nearest < 0)
                    return;

                float clearance = chf.spans[nearest].h * chf.ch;
                if (clearance < worst)
                    worst = clearance;
            }

            Vector3 c = PolyCentroid(data, poly);
            Sample(c.X, c.Y, c.Z);
            for (int v = 0; v < poly.vertCount; v++)
            {
                int o = poly.verts[v] * 3;
                Sample(data.verts[o], data.verts[o + 1], data.verts[o + 2]);
            }

            // Walk the edges too, so a long thin polygon is not judged on three points.
            for (int v = 0; v < poly.vertCount; v++)
            {
                int o0 = poly.verts[v] * 3;
                int o1 = poly.verts[(v + 1) % poly.vertCount] * 3;
                float x0 = data.verts[o0], y0 = data.verts[o0 + 1], z0 = data.verts[o0 + 2];
                float x1 = data.verts[o1], y1 = data.verts[o1 + 1], z1 = data.verts[o1 + 2];
                float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
                int steps = Math.Max(1, (int)MathF.Ceiling(len / chf.cs));
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Sample(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, z0 + (z1 - z0) * t);
                }
            }
            return worst;
        }

        static NavigationMesh.AreaHeight ClassifyClearance(float clearance, NavMeshBakeSettings settings)
        {
            float measured = clearance + settings.HeightLimitedClearanceBias;
            if (float.IsInfinity(measured) || measured >= settings.CrouchHeight)
                return NavigationMesh.AreaHeight.Standing;
            if (measured >= settings.DeepCrouchHeight)
                return NavigationMesh.AreaHeight.Crouch;
            return NavigationMesh.AreaHeight.DeepCrouch;
        }

        static bool PolyOverlapsBarrier(
            DtMeshData data,
            DtPoly poly,
            Vector3 centroid,
            Vector3 polyMin,
            Vector3 polyMax,
            CollisionNavMeshSoup.BarrierVolume barrier,
            float inflate)
        {
            CollisionNavMeshSoup.OrientedBoxAabb(
                barrier.Centre, barrier.Rotation,
                barrier.HalfExtents + new Vector3(inflate, inflate, inflate),
                out Vector3 bmin, out Vector3 bmax);
            if (polyMax.X < bmin.X || polyMin.X > bmax.X
                || polyMax.Y < bmin.Y || polyMin.Y > bmax.Y
                || polyMax.Z < bmin.Z || polyMin.Z > bmax.Z)
                return false;

            if (CollisionNavMeshSoup.PointInOrientedBox(
                    centroid, barrier.Centre, barrier.Rotation, barrier.HalfExtents, inflate))
                return true;

            for (int v = 0; v < poly.vertCount; v++)
            {
                int o = poly.verts[v] * 3;
                var p = new Vector3(data.verts[o], data.verts[o + 1], data.verts[o + 2]);
                if (CollisionNavMeshSoup.PointInOrientedBox(
                        p, barrier.Centre, barrier.Rotation, barrier.HalfExtents, inflate))
                    return true;
            }

            // Thin vertical door slabs: stamp if poly AABB intersects the inflated barrier AABB
            // and the poly centroid is within inflate of the barrier OBB on XZ (ignore thin Y miss).
            Vector3 flat = new Vector3(centroid.X, barrier.Centre.Y, centroid.Z);
            return CollisionNavMeshSoup.PointInOrientedBox(
                flat, barrier.Centre, barrier.Rotation, barrier.HalfExtents, inflate);
        }

        static void PolyAabb(DtMeshData data, DtPoly poly, out Vector3 min, out Vector3 max)
        {
            int o0 = poly.verts[0] * 3;
            min = new Vector3(data.verts[o0], data.verts[o0 + 1], data.verts[o0 + 2]);
            max = min;
            for (int v = 1; v < poly.vertCount; v++)
            {
                int o = poly.verts[v] * 3;
                var p = new Vector3(data.verts[o], data.verts[o + 1], data.verts[o + 2]);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        }

        static Vector3 PolyCentroid(DtMeshData data, DtPoly poly)
        {
            Vector3 sum = Vector3.Zero;
            for (int v = 0; v < poly.vertCount; v++)
            {
                int o = poly.verts[v] * 3;
                sum.X += data.verts[o];
                sum.Y += data.verts[o + 1];
                sum.Z += data.verts[o + 2];
            }
            float inv = 1f / Math.Max(1, poly.vertCount);
            return sum * inv;
        }

        static DtNavMeshCreateParams BuildCreateParams(RcConfig cfg, RcBuilderResult rcResult, NavMeshBakeSettings settings)
        {
            RcPolyMesh pmesh = rcResult.Mesh;
            RcPolyMeshDetail dmesh = rcResult.MeshDetail;

            // Ensure poly flags are non-zero (Detour query filters often require them).
            if (pmesh.flags != null)
            {
                for (int i = 0; i < pmesh.npolys; i++)
                    if (pmesh.flags[i] == 0)
                        pmesh.flags[i] = 1;
            }

            var option = new DtNavMeshCreateParams
            {
                verts = pmesh.verts,
                vertCount = pmesh.nverts,
                polys = pmesh.polys,
                polyAreas = pmesh.areas,
                polyFlags = pmesh.flags,
                polyCount = pmesh.npolys,
                nvp = pmesh.nvp,
                walkableHeight = settings.LowestNavigableHeight,
                walkableRadius = settings.WalkableRadius,
                walkableClimb = settings.WalkableClimb,
                bmin = pmesh.bmin,
                bmax = pmesh.bmax,
                cs = cfg.Cs,
                ch = cfg.Ch,
                buildBvTree = true
            };

            if (dmesh != null)
            {
                option.detailMeshes = dmesh.meshes;
                option.detailVerts = dmesh.verts;
                option.detailVertsCount = dmesh.nverts;
                option.detailTris = dmesh.tris;
                option.detailTriCount = dmesh.ntris;
            }

            return option;
        }

        static bool[] MarkReachablePolys(
            DtNavMesh nav,
            DtMeshTile tile,
            List<Vector3> seeds,
            NavMeshBakeSettings settings)
        {
            int polyCount = tile.data.header.polyCount;
            var keep = new bool[polyCount];
            var query = new DtNavMeshQuery(nav);
            var filter = new DtQueryDefaultFilter();

            float above = settings.ReachabilitySeedHeightToleranceAbove;
            float below = settings.ReachabilitySeedHeightToleranceBelow;
            var extents = new RcVec3f(settings.WalkableRadius * 2f, Math.Max(above, below) + settings.WalkableClimb, settings.WalkableRadius * 2f);

            var queue = new Queue<int>();
            foreach (Vector3 seed in seeds)
            {
                var center = new RcVec3f(seed.X, seed.Y, seed.Z);
                query.FindNearestPoly(center, extents, filter, out long nearestRef, out _, out _);
                if (nearestRef == 0)
                    continue;
                DtDetour.DecodePolyId(nearestRef, out _, out _, out int ip);
                if (ip < 0 || ip >= polyCount || keep[ip])
                    continue;
                keep[ip] = true;
                queue.Enqueue(ip);
            }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                DtPoly poly = tile.data.polys[i];
                for (int link = poly.firstLink; link != DT_NULL_LINK; link = tile.links[link].next)
                {
                    long refs = tile.links[link].refs;
                    if (refs == 0)
                        continue;
                    DtDetour.DecodePolyId(refs, out _, out _, out int ni);
                    if (ni < 0 || ni >= polyCount || keep[ni])
                        continue;
                    keep[ni] = true;
                    queue.Enqueue(ni);
                }
            }

            // If no seed hit the mesh, keep everything rather than wiping the tile.
            bool any = false;
            for (int i = 0; i < keep.Length; i++)
                if (keep[i]) { any = true; break; }
            if (!any)
            {
                for (int i = 0; i < keep.Length; i++)
                    keep[i] = true;
            }
            return keep;
        }

        /// <summary>
        /// Keep the largest connected component plus any other components whose median
        /// centroid Y lies within <see cref="NavMeshBakeSettings.IslandFloorYBand"/>
        /// of that primary median. Drops elevated ceiling/duct islands when no seeds exist.
        /// </summary>
        static bool[] MarkPrimaryFloorPolys(DtMeshTile tile, NavMeshBakeSettings settings)
        {
            int polyCount = tile.data.header.polyCount;
            var keep = new bool[polyCount];
            if (polyCount == 0)
                return keep;

            var component = new int[polyCount];
            for (int i = 0; i < polyCount; i++)
                component[i] = -1;

            var centroidsY = new float[polyCount];
            for (int i = 0; i < polyCount; i++)
                centroidsY[i] = PolyCentroid(tile.data, tile.data.polys[i]).Y;

            int nextComp = 0;
            var sizes = new List<int>();
            var medians = new List<float>();
            var queue = new Queue<int>();
            var yScratch = new List<float>();

            for (int start = 0; start < polyCount; start++)
            {
                if (component[start] >= 0)
                    continue;

                int id = nextComp++;
                component[start] = id;
                queue.Enqueue(start);
                yScratch.Clear();
                int size = 0;

                while (queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    size++;
                    yScratch.Add(centroidsY[i]);

                    DtPoly poly = tile.data.polys[i];
                    for (int link = poly.firstLink; link != DT_NULL_LINK; link = tile.links[link].next)
                    {
                        long refs = tile.links[link].refs;
                        if (refs == 0)
                            continue;
                        DtDetour.DecodePolyId(refs, out _, out _, out int ni);
                        if (ni < 0 || ni >= polyCount || component[ni] >= 0)
                            continue;
                        component[ni] = id;
                        queue.Enqueue(ni);
                    }
                }

                yScratch.Sort();
                float median = yScratch[yScratch.Count / 2];
                sizes.Add(size);
                medians.Add(median);
            }

            int primary = 0;
            for (int c = 1; c < sizes.Count; c++)
            {
                if (sizes[c] > sizes[primary])
                    primary = c;
            }

            float primaryMedian = medians[primary];
            float band = Math.Max(0.05f, settings.IslandFloorYBand);
            int minSecondary = Math.Max(
                settings.IslandMinSecondaryPolys,
                (int)Math.Ceiling(sizes[primary] * Math.Max(0f, settings.IslandMinSecondaryFraction)));

            for (int i = 0; i < polyCount; i++)
            {
                int c = component[i];
                if (c < 0)
                    continue;
                if (c == primary)
                {
                    keep[i] = true;
                    continue;
                }
                // Same Y band as the floor, but large enough to be a real deck pocket —
                // tiny exterior speckles at floor height are dropped.
                if (Math.Abs(medians[c] - primaryMedian) <= band && sizes[c] >= minSecondary)
                    keep[i] = true;
            }

            // Safety: never wipe the whole tile.
            bool any = false;
            for (int i = 0; i < keep.Length; i++)
                if (keep[i]) { any = true; break; }
            if (!any)
            {
                for (int i = 0; i < keep.Length; i++)
                    keep[i] = true;
            }
            return keep;
        }

        /// <summary>
        /// Drop kept polys that are a prop or duct top rather than floor: walkable surface sits
        /// directly beneath them, close enough that the two cannot be separate storeys.
        /// </summary>
        /// <remarks>
        /// This used to compare every poly against the median Y of the whole tile, which works
        /// on a single-deck level like BSP_TORRENS and decapitates everything else - SCI_Hub lost
        /// all 236 of its upper-deck polys, so the Control Room and the rest of the upper floor
        /// had no navmesh and therefore no sound nodes. The test is per-poly now: what matters is
        /// the height above the surface immediately below, not above the level as a whole.
        /// </remarks>
        static int StripElevatedPolys(DtMeshTile tile, NavMeshBakeSettings settings, bool[] keep)
        {
            if (keep == null)
                return 0;

            int polyCount = tile.data.header.polyCount;
            float minRise = Math.Max(0.05f, settings.ElevatedPolyStripAboveFloor);
            float storey = Math.Max(minRise + 0.05f, settings.ElevatedPolyStoreySeparation);

            var centroids = new Vector3[polyCount];
            var mins = new Vector3[polyCount];
            var maxs = new Vector3[polyCount];
            var eligible = new bool[polyCount];
            for (int i = 0; i < polyCount; i++)
            {
                DtPoly poly = tile.data.polys[i];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                    continue;
                eligible[i] = keep[i];
                if (!eligible[i])
                    continue;
                centroids[i] = PolyCentroid(tile.data, poly);
                PolyAabb(tile.data, poly, out mins[i], out maxs[i]);
            }

            // Test against the set as it stood on entry: culling a crate lid must not promote the
            // box stacked on it to floor.
            int culled = 0;
            for (int i = 0; i < polyCount; i++)
            {
                if (!eligible[i])
                    continue;

                Vector3 c = centroids[i];
                for (int j = 0; j < polyCount; j++)
                {
                    if (j == i || !eligible[j])
                        continue;
                    float rise = c.Y - centroids[j].Y;
                    if (rise <= minRise || rise >= storey)
                        continue;
                    // Overlapping in plan is what makes the lower poly the surface underneath.
                    if (c.X < mins[j].X || c.X > maxs[j].X || c.Z < mins[j].Z || c.Z > maxs[j].Z)
                        continue;
                    keep[i] = false;
                    culled++;
                    break;
                }
            }

            // Safety: never wipe the whole tile.
            bool any = false;
            for (int i = 0; i < keep.Length; i++)
                if (keep[i]) { any = true; break; }
            if (!any)
            {
                for (int i = 0; i < keep.Length; i++)
                    keep[i] = true;
                return 0;
            }
            return culled;
        }

        static void AdaptTile(
            NavigationMesh nav,
            DtMeshTile tile,
            NavMeshBakeSettings settings,
            bool[] keepGroundPolys,
            NavigationMesh.dt_area_t[] polyAreas,
            List<CollisionNavMeshSoup.OffMeshLinkDraft> offMeshDrafts,
            BackstageMeshBuilder.Result backstage = null)
        {
            DtMeshData data = tile.data;
            DtMeshHeader srcHeader = data.header;
            int srcPolyCount = srcHeader.polyCount;

            // Phase 1: drop off-mesh polys if any slipped in; optionally drop unreachable ground.
            var keepIndex = new int[srcPolyCount];
            for (int i = 0; i < srcPolyCount; i++)
                keepIndex[i] = -1;

            int dstPolyCount = 0;
            for (int i = 0; i < srcPolyCount; i++)
            {
                DtPoly poly = data.polys[i];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                    continue;
                if (keepGroundPolys != null && i < keepGroundPolys.Length && !keepGroundPolys[i])
                    continue;
                keepIndex[i] = dstPolyCount++;
            }

            if (dstPolyCount == 0)
                throw new InvalidOperationException("No ground polygons left after filtering.");

            // Collect used verts.
            var vertRemap = new int[srcHeader.vertCount];
            for (int i = 0; i < vertRemap.Length; i++)
                vertRemap[i] = -1;
            var dstVerts = new List<Vector3>(srcHeader.vertCount);
            for (int i = 0; i < srcPolyCount; i++)
            {
                if (keepIndex[i] < 0)
                    continue;
                DtPoly poly = data.polys[i];
                for (int v = 0; v < poly.vertCount; v++)
                {
                    int vi = poly.verts[v];
                    if (vertRemap[vi] >= 0)
                        continue;
                    vertRemap[vi] = dstVerts.Count;
                    int o = vi * 3;
                    dstVerts.Add(new Vector3(data.verts[o], data.verts[o + 1], data.verts[o + 2]));
                }
            }

            NavigationMesh.dt_area_t defaultArea = NavigationMesh.CreateDefaultGroundArea();
            var dstPolys = new NavigationMesh.dtPoly[dstPolyCount];
            for (int i = 0; i < srcPolyCount; i++)
            {
                int di = keepIndex[i];
                if (di < 0)
                    continue;
                DtPoly src = data.polys[i];
                NavigationMesh.dt_area_t area = defaultArea;
                if (polyAreas != null && i < polyAreas.Length)
                    area = polyAreas[i];
                var dst = new NavigationMesh.dtPoly
                {
                    firstLink = DT_NULL_LINK,
                    verts = new ushort[6],
                    neis = new ushort[6],
                    vertCount = (byte)src.vertCount,
                    area = area
                };
                for (int v = 0; v < 6; v++)
                {
                    dst.verts[v] = v < src.vertCount ? (ushort)vertRemap[src.verts[v]] : (ushort)0;
                    // Neighbour indices are 1-based poly indices; remap when both sides kept.
                    if (v < src.vertCount)
                    {
                        int nei = src.neis[v];
                        if (nei == 0 || (nei & DtDetour.DT_EXT_LINK) != 0)
                            dst.neis[v] = (ushort)nei;
                        else
                        {
                            int srcNei = nei - 1;
                            int dstNei = (srcNei >= 0 && srcNei < keepIndex.Length) ? keepIndex[srcNei] : -1;
                            dst.neis[v] = dstNei >= 0 ? (ushort)(dstNei + 1) : (ushort)0;
                        }
                    }
                }
                dstPolys[di] = dst;
            }

            // Detail meshes for kept polys only.
            var dstDetail = new List<NavigationMesh.dtPolyDetail>(dstPolyCount);
            var dstDetailVerts = new List<Vector3>();
            var dstDetailTris = new List<byte>();
            if (data.detailMeshes != null && data.detailMeshes.Length > 0)
            {
                for (int i = 0; i < srcPolyCount; i++)
                {
                    int di = keepIndex[i];
                    if (di < 0)
                        continue;
                    // Off-mesh / missing detail: pad with empty so detailMeshCount == polyCount.
                    if (i >= data.detailMeshes.Length)
                    {
                        dstDetail.Add(new NavigationMesh.dtPolyDetail());
                        continue;
                    }

                    DtPolyDetail dm = data.detailMeshes[i];
                    int vertBase = dstDetailVerts.Count;
                    int triBase = dstDetailTris.Count / 4;

                    // Extra detail verts only (poly verts live in Vertices).
                    for (int v = 0; v < dm.vertCount; v++)
                    {
                        int o = (dm.vertBase + v) * 3;
                        dstDetailVerts.Add(new Vector3(
                            data.detailVerts[o],
                            data.detailVerts[o + 1],
                            data.detailVerts[o + 2]));
                    }

                    for (int t = 0; t < dm.triCount; t++)
                    {
                        int o = (dm.triBase + t) * 4;
                        dstDetailTris.Add((byte)data.detailTris[o + 0]);
                        dstDetailTris.Add((byte)data.detailTris[o + 1]);
                        dstDetailTris.Add((byte)data.detailTris[o + 2]);
                        dstDetailTris.Add((byte)data.detailTris[o + 3]);
                    }

                    dstDetail.Add(new NavigationMesh.dtPolyDetail
                    {
                        vertBase = vertBase,
                        triBase = triBase,
                        vertCount = (byte)dm.vertCount,
                        triCount = (byte)dm.triCount
                    });
                }
            }
            else
            {
                for (int i = 0; i < dstPolyCount; i++)
                    dstDetail.Add(new NavigationMesh.dtPolyDetail());
            }

            // Backstage sheet: appended after the filtered ground polys and before the off-mesh
            // connections, matching the retail layout (ground, backstage, off-mesh).
            if (backstage != null && backstage.Triangles.Count >= 3)
            {
                dstPolys = AppendBackstagePolys(
                    backstage, settings, srcHeader, dstVerts, dstPolys, dstDetail, dstDetailVerts, dstDetailTris);
                dstPolyCount = dstPolys.Length;
            }

            int groundPolyCount = dstPolyCount;
            NavigationMesh.dtOffMeshConnection[] offMeshConnections = Array.Empty<NavigationMesh.dtOffMeshConnection>();
            if (offMeshDrafts != null && offMeshDrafts.Count > 0)
            {
                var polyList = new List<NavigationMesh.dtPoly>(dstPolys);
                var offList = new List<NavigationMesh.dtOffMeshConnection>(offMeshDrafts.Count);
                for (int li = 0; li < offMeshDrafts.Count; li++)
                {
                    CollisionNavMeshSoup.OffMeshLinkDraft draft = offMeshDrafts[li];
                    int vertA = dstVerts.Count;
                    dstVerts.Add(draft.Start);
                    dstVerts.Add(draft.End);
                    int polyIndex = polyList.Count;

                    NavigationMesh.dt_area_t area = NavigationMesh.CreateDefaultGroundArea();
                    area.SetPolyType(NavigationMesh.dtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION);
                    area.SetLinkType(draft.LinkType);
                    area.SetAdmittanceFlags(draft.CharacterClasses);
                    area.SetIsEnabled(draft.OpenOnReset);
                    //Retail marks off-mesh polys Normal while ground stays Backstage.
                    area.SetMarkupFlags(NavigationMesh.NavMeshAreaType.Normal);

                    var omPoly = new NavigationMesh.dtPoly
                    {
                        firstLink = DT_NULL_LINK,
                        verts = new ushort[6],
                        neis = new ushort[6],
                        vertCount = 2,
                        area = area
                    };
                    omPoly.verts[0] = (ushort)vertA;
                    omPoly.verts[1] = (ushort)(vertA + 1);
                    //Leave neis at 0. DT_EXT_LINK means "crosses a tile boundary", and Cathode
                    //levels are a single tile - retail writes 0 here on every off-mesh poly. The
                    //connection is expressed through the link pool below, not through neis.
                    //No detail entry either: retail's detailMeshCount covers ground polys only
                    //(SCI_Hub ships 1134 detail meshes for 1218 polys, 84 of them off-mesh).
                    polyList.Add(omPoly);

                    offList.Add(new NavigationMesh.dtOffMeshConnection
                    {
                        pos = new[]
                        {
                            draft.Start.X, draft.Start.Y, draft.Start.Z,
                            draft.End.X, draft.End.Y, draft.End.Z
                        },
                        rad = draft.Radius,
                        poly_index_within_tile = (ushort)polyIndex,
                        extra_cost = draft.ExtraCost,
                        entity = new NavigationMesh.dtOffMeshEntityHandle
                        {
                            entity_id = draft.Entity.entity_id,
                            composite_instance_id = draft.Entity.composite_instance_id
                        },
                        min_speed = LOCOMOTION_TARGET_SPEED.SLOWEST,
                        max_speed = LOCOMOTION_TARGET_SPEED.FASTEST,
                    });
                }

                dstPolys = polyList.ToArray();
                dstPolyCount = dstPolys.Length;
                offMeshConnections = offList.ToArray();
            }

            // Build internal links with AI 32-bit polyref encoding.
            int polyBits = Ilog2(NextPow2(dstPolyCount));
            if (polyBits < 1) polyBits = 1;
            uint polyRefBase = 1u << polyBits;

            var links = new List<NavigationMesh.dtLink>(dstPolyCount * 4);
            for (int i = 0; i < dstPolyCount; i++)
            {
                NavigationMesh.dtPoly poly = dstPolys[i];
                poly.firstLink = DT_NULL_LINK;
                for (int j = poly.vertCount - 1; j >= 0; --j)
                {
                    ushort nei = poly.neis[j];
                    if (nei == 0 || (nei & DtDetour.DT_EXT_LINK) != 0)
                        continue;
                    int idx = links.Count;
                    links.Add(new NavigationMesh.dtLink
                    {
                        polygonRef = polyRefBase | (uint)(nei - 1),
                        next = poly.firstLink,
                        edge = (byte)j,
                        side = 0xff,
                        bmin = 0,
                        bmax = 0
                    });
                    poly.firstLink = idx;
                }
                dstPolys[i] = poly;
            }

            ConnectOffMeshLinks(dstPolys, dstVerts, offMeshConnections, groundPolyCount, polyRefBase, links);

            // Rebuild BV tree over ground polys only (quantized like Detour CreateBVTree).
            // Off-mesh polys stay out of it, as in Detour and retail data (SCI_Hub ships
            // 2268 nodes = 2 x 1134 ground polys for 1218 total).
            NavigationMesh.dtBVNode[] bvNodes = BuildBvTree(dstPolys, groundPolyCount, dstVerts, srcHeader.bmin, settings.CellSize);

            var header = new NavigationMesh.dtMeshHeader
            {
                FourCC = new fourcc { V = new[] { 'V', 'A', 'N', 'D' } },
                version = 7,
                x = 0,
                y = 0,
                layer = 0,
                userId = 0,
                polyCount = dstPolyCount,
                vertCount = dstVerts.Count,
                //The pool has to have room for the links the runtime adds on load (doors opening,
                //off-mesh connections being re-linked). Retail ships roughly 1.7x the used count;
                //over-allocating only costs memory, under-allocating silently drops links.
                maxLinkCount = PadLinkPool(links),
                detailMeshCount = dstDetail.Count,
                detailVertCount = dstDetailVerts.Count,
                detailTriCount = dstDetailTris.Count / 4,
                bvNodeCount = bvNodes.Length,
                offMeshConCount = offMeshConnections.Length,
                offMeshBase = groundPolyCount,
                walkableHeight = settings.LowestNavigableHeight,
                walkableRadius = settings.WalkableRadius,
                walkableClimb = settings.WalkableClimb,
                bMin = new[] { srcHeader.bmin.X, srcHeader.bmin.Y, srcHeader.bmin.Z },
                bMax = new[] { srcHeader.bmax.X, srcHeader.bmax.Y, srcHeader.bmax.Z },
                bvQuantFactor = 1.0f / settings.CellSize
            };

            nav.SetTileData(
                header,
                dstVerts.ToArray(),
                dstPolys,
                links.ToArray(),
                dstDetail.ToArray(),
                dstDetailVerts.ToArray(),
                dstDetailTris.ToArray(),
                bvNodes,
                offMeshConnections);
        }

        /// <summary>
        /// Build one merged backstage sheet from the nodes, triangulating each network id's
        /// nodes separately (nodes only ever mesh with others on their own network - retail
        /// ENG_TowPlatform's sheet covers a single network's region while other networks' nodes
        /// keep their connections but get no sheet). Returns null when nothing triangulated.
        /// </summary>
        static BackstageMeshBuilder.Result BuildBackstageSheets(
            List<CollisionNavMeshSoup.BackstageNodeDraft> nodes,
            NavMeshBakeSettings settings,
            out string warning)
        {
            warning = null;
            var byNetwork = new SortedDictionary<int, List<Vector3>>();
            foreach (CollisionNavMeshSoup.BackstageNodeDraft node in nodes)
            {
                if (!byNetwork.TryGetValue(node.NetworkId, out List<Vector3> tops))
                    byNetwork[node.NetworkId] = tops = new List<Vector3>();
                tops.Add(node.Bottom + new Vector3(0f, settings.BackstageNodeHeight, 0f));
            }

            var merged = new BackstageMeshBuilder.Result();
            var warnings = new List<string>();
            foreach (KeyValuePair<int, List<Vector3>> network in byNetwork)
            {
                BackstageMeshBuilder.Result sheet = BackstageMeshBuilder.Build(
                    network.Value, settings.BackstageMaxEdgeLength, settings.BackstageColinearStripHalfWidth,
                    out string sheetWarning);
                sheetWarning ??= sheet?.Warning;
                if (sheetWarning != null)
                    warnings.Add((byNetwork.Count > 1 ? $"network {network.Key}: " : "") + sheetWarning);
                if (sheet == null)
                    continue;

                int vertBase = merged.Vertices.Count;
                merged.Vertices.AddRange(sheet.Vertices);
                foreach (int index in sheet.Triangles)
                    merged.Triangles.Add(vertBase + index);
            }

            if (warnings.Count > 0)
                warning = string.Join("; ", warnings);
            return merged.Triangles.Count == 0 ? null : merged;
        }

        /// <summary>
        /// Append the backstage sheet's triangles as ground polys flagged Backstage, sharing the
        /// tile vertex pool. Retail parity: admittance ALL, Standing, enabled, area id 0, detail
        /// entry [2,0,1] with edge flags 21, and vertices snapped onto the Recast cell grid.
        /// </summary>
        static NavigationMesh.dtPoly[] AppendBackstagePolys(
            BackstageMeshBuilder.Result backstage,
            NavMeshBakeSettings settings,
            DtMeshHeader srcHeader,
            List<Vector3> dstVerts,
            NavigationMesh.dtPoly[] dstPolys,
            List<NavigationMesh.dtPolyDetail> dstDetail,
            List<Vector3> dstDetailVerts,
            List<byte> dstDetailTris)
        {
            float cs = settings.CellSize;
            float ch = settings.CellHeight;
            RcVec3f bmin = srcHeader.bmin;

            // Snap onto the tile grid the way Recast quantizes its own vertices; identical
            // snapped positions collapse to one vertex so triangles share edges properly.
            var vertRemap = new int[backstage.Vertices.Count];
            var snappedIndex = new Dictionary<(int, int, int), int>();
            for (int i = 0; i < backstage.Vertices.Count; i++)
            {
                Vector3 v = backstage.Vertices[i];
                int gx = (int)MathF.Floor((v.X - bmin.X) / cs);
                int gy = (int)MathF.Floor((v.Y - bmin.Y) / ch);
                int gz = (int)MathF.Floor((v.Z - bmin.Z) / cs);
                var key = (gx, gy, gz);
                if (!snappedIndex.TryGetValue(key, out int vi))
                {
                    vi = dstVerts.Count;
                    dstVerts.Add(new Vector3(bmin.X + gx * cs, bmin.Y + gy * ch, bmin.Z + gz * cs));
                    snappedIndex[key] = vi;
                }
                vertRemap[i] = vi;
            }

            // Remap to snapped indices, dropping triangles the snap degenerated, and re-fix
            // winding on the snapped positions (Detour wants positive 2D area).
            var tris = new List<(int a, int b, int c)>(backstage.Triangles.Count / 3);
            for (int t = 0; t + 2 < backstage.Triangles.Count; t += 3)
            {
                int a = vertRemap[backstage.Triangles[t]];
                int b = vertRemap[backstage.Triangles[t + 1]];
                int c = vertRemap[backstage.Triangles[t + 2]];
                if (a == b || b == c || a == c)
                    continue;
                Vector3 pa = dstVerts[a], pb = dstVerts[b], pc = dstVerts[c];
                float area2 = (pc.X - pa.X) * (pb.Z - pa.Z) - (pb.X - pa.X) * (pc.Z - pa.Z);
                if (area2 == 0f)
                    continue;
                tris.Add(area2 > 0f ? (a, b, c) : (a, c, b));
            }

            int triCount = tris.Count;
            int baseIndex = dstPolys.Length;
            var polys = new List<NavigationMesh.dtPoly>(dstPolys);

            // Adjacency between sheet triangles by shared (snapped) edge.
            var edgeOwner = new Dictionary<(int, int), int>();
            var neighbours = new int[triCount, 3];
            for (int t = 0; t < triCount; t++)
                for (int e = 0; e < 3; e++)
                    neighbours[t, e] = -1;
            for (int t = 0; t < triCount; t++)
            {
                var (ta, tb, tc) = tris[t];
                Span<int> v = stackalloc int[3] { ta, tb, tc };
                for (int e = 0; e < 3; e++)
                {
                    int a = v[e];
                    int b = v[(e + 1) % 3];
                    var key = a < b ? (a, b) : (b, a);
                    if (edgeOwner.TryGetValue(key, out int packed))
                    {
                        int otherTri = packed >> 2;
                        int otherEdge = packed & 3;
                        neighbours[t, e] = otherTri;
                        neighbours[otherTri, otherEdge] = t;
                    }
                    else
                    {
                        edgeOwner[key] = (t << 2) | e;
                    }
                }
            }

            for (int t = 0; t < triCount; t++)
            {
                NavigationMesh.dt_area_t area = NavigationMesh.CreateDefaultGroundArea();
                // Markup is a bitfield here: bit 0 normal, bit 1 backstage, bit 2 expensive.
                area.SetMarkupFlags((NavigationMesh.NavMeshAreaType)(uint)NavigationMesh.NavMeshAreaTypeFlags.BackstageFlag);

                var poly = new NavigationMesh.dtPoly
                {
                    firstLink = DT_NULL_LINK,
                    verts = new ushort[6],
                    neis = new ushort[6],
                    vertCount = 3,
                    area = area
                };
                var (va, vb, vc) = tris[t];
                Span<int> v = stackalloc int[3] { va, vb, vc };
                for (int e = 0; e < 3; e++)
                {
                    poly.verts[e] = (ushort)v[e];
                    int nei = neighbours[t, e];
                    poly.neis[e] = nei >= 0 ? (ushort)(baseIndex + nei + 1) : (ushort)0;
                }
                polys.Add(poly);

                // Retail backstage detail: no extra verts, one tri [2,0,1] with edge flags 21.
                dstDetail.Add(new NavigationMesh.dtPolyDetail
                {
                    vertBase = dstDetailVerts.Count,
                    triBase = dstDetailTris.Count / 4,
                    vertCount = 0,
                    triCount = 1
                });
                dstDetailTris.Add(2);
                dstDetailTris.Add(0);
                dstDetailTris.Add(1);
                dstDetailTris.Add(21);
            }

            return polys.ToArray();
        }

        static NavigationMesh.dtBVNode[] BuildBvTree(
            NavigationMesh.dtPoly[] polys,
            int polyCount,
            List<Vector3> verts,
            RcVec3f bmin,
            float cellSize)
        {
            int n = Math.Min(polyCount, polys.Length);
            if (n == 0)
                return Array.Empty<NavigationMesh.dtBVNode>();

            float quant = 1.0f / cellSize;
            var items = new BvItem[n];
            for (int i = 0; i < n; i++)
            {
                NavigationMesh.dtPoly p = polys[i];
                Vector3 amin = verts[p.verts[0]];
                Vector3 amax = amin;
                for (int v = 1; v < p.vertCount; v++)
                {
                    Vector3 q = verts[p.verts[v]];
                    amin = Vector3.Min(amin, q);
                    amax = Vector3.Max(amax, q);
                }
                items[i] = new BvItem
                {
                    i = i,
                    bmin = new[]
                    {
                        ClampShort((amin.X - bmin.X) * quant),
                        ClampShort((amin.Y - bmin.Y) * quant),
                        ClampShort((amin.Z - bmin.Z) * quant)
                    },
                    bmax = new[]
                    {
                        ClampShort((amax.X - bmin.X) * quant),
                        ClampShort((amax.Y - bmin.Y) * quant),
                        ClampShort((amax.Z - bmin.Z) * quant)
                    }
                };
            }

            var nodes = new List<NavigationMesh.dtBVNode>(n * 2);
            Subdivide(items, 0, n, nodes);
            return nodes.ToArray();
        }

        struct BvItem
        {
            public int i;
            public short[] bmin;
            public short[] bmax;
        }

        static void Subdivide(BvItem[] items, int imin, int imax, List<NavigationMesh.dtBVNode> nodes)
        {
            int inum = imax - imin;
            int icur = nodes.Count;
            var node = new NavigationMesh.dtBVNode
            {
                bmin = new short[3],
                bmax = new short[3]
            };

            node.bmin[0] = items[imin].bmin[0];
            node.bmin[1] = items[imin].bmin[1];
            node.bmin[2] = items[imin].bmin[2];
            node.bmax[0] = items[imin].bmax[0];
            node.bmax[1] = items[imin].bmax[1];
            node.bmax[2] = items[imin].bmax[2];
            for (int i = imin + 1; i < imax; i++)
            {
                for (int a = 0; a < 3; a++)
                {
                    if (items[i].bmin[a] < node.bmin[a]) node.bmin[a] = items[i].bmin[a];
                    if (items[i].bmax[a] > node.bmax[a]) node.bmax[a] = items[i].bmax[a];
                }
            }

            nodes.Add(node);

            if (inum == 1)
            {
                node.i = items[imin].i;
                nodes[icur] = node;
                return;
            }

            int axis = 0;
            int maxAxis = node.bmax[0] - node.bmin[0];
            for (int a = 1; a < 3; a++)
            {
                int d = node.bmax[a] - node.bmin[a];
                if (d > maxAxis) { axis = a; maxAxis = d; }
            }

            Array.Sort(items, imin, inum, Comparer<BvItem>.Create((a, b) =>
            {
                int ca = (a.bmin[axis] + a.bmax[axis]) / 2;
                int cb = (b.bmin[axis] + b.bmax[axis]) / 2;
                return ca.CompareTo(cb);
            }));

            int isplit = imin + inum / 2;
            Subdivide(items, imin, isplit, nodes);
            Subdivide(items, isplit, imax, nodes);
            node.i = -(nodes.Count - icur);
            nodes[icur] = node;
        }

        static short ClampShort(float v)
        {
            int i = (int)v;
            if (i < short.MinValue) return short.MinValue;
            if (i > short.MaxValue) return short.MaxValue;
            return (short)i;
        }

        static int NextPow2(int v)
        {
            v--;
            v |= v >> 1;
            v |= v >> 2;
            v |= v >> 4;
            v |= v >> 8;
            v |= v >> 16;
            v++;
            return v < 1 ? 1 : v;
        }

        static int Ilog2(int v)
        {
            int r = 0;
            while ((1 << r) < v) r++;
            return r;
        }
    }
}
#endif