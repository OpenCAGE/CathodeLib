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

            RcVec3f bmin = geom.GetMeshBoundsMin();
            RcVec3f bmax = geom.GetMeshBoundsMax();

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
            NavigationMesh.dt_area_t[] polyAreas = BuildPolyAreas(tile, soup.Barriers, settings, keep, out stamped, out barriersWithCoverage);

            int crouchMarked = 0, deepCrouchMarked = 0;
            ApplyHeightLimitedMarkup(
                tile, rcResult.CompactHeightfield, settings, keep, polyAreas,
                out crouchMarked, out deepCrouchMarked);

            NavigationMesh nav = new NavigationMesh("");
            AdaptTile(nav, tile, settings, keep, polyAreas, soup.OffMeshLinks);

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
            message += $" Barriers={soup.Barriers?.Count ?? 0} stampedPolys={stamped} covered={barriersWithCoverage}."
                + $" Platforms={soup.WalkablePlatforms?.Count ?? 0} exclusions={soup.ExclusionAreas?.Count ?? 0}."
                + $" HeightLimited crouch={crouchMarked} deepCrouch={deepCrouchMarked}.";

            return new BakeResult
            {
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
                float[] verts =
                {
                    amin.X, 0f, amin.Z,
                    amax.X, 0f, amin.Z,
                    amax.X, 0f, amax.Z,
                    amin.X, 0f, amax.Z
                };
                geom.AddConvexVolume(verts, amin.Y, amax.Y, nullArea);
            }
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

        static NavigationMesh.dt_area_t[] BuildPolyAreas(
            DtMeshTile tile,
            List<CollisionNavMeshSoup.BarrierVolume> barriers,
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
                PolyAabb(data, poly, out Vector3 polyMin, out Vector3 polyMax);
                int hitBarrier = -1;
                for (int b = 0; b < barriers.Count; b++)
                {
                    CollisionNavMeshSoup.BarrierVolume barrier = barriers[b];
                    if (PolyOverlapsBarrier(data, poly, centroid, polyMin, polyMax, barrier, inflate))
                    {
                        hitBarrier = b;
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
            int cellCount = chf.width * chf.height;

            var active = new bool[cellCount];
            for (int i = 0; i < polyCount; i++)
            {
                if (keep != null && i < keep.Length && !keep[i])
                    continue;
                DtPoly poly = data.polys[i];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                    continue;
                MarkPolyCells(chf, data, poly, active);
            }

            var cellHeight = new NavigationMesh.AreaHeight[cellCount];
            for (int i = 0; i < cellCount; i++)
                cellHeight[i] = NavigationMesh.AreaHeight.Standing;

            for (int z = 0; z < chf.height; z++)
            {
                for (int x = 0; x < chf.width; x++)
                {
                    int ci = x + z * chf.width;
                    if (!active[ci])
                        continue;

                    RcCompactCell cell = chf.cells[ci];
                    if (cell.count <= 0)
                        continue;

                    // Clearance of the lowest span in this nav-covered column.
                    float clearance = chf.spans[cell.index].h * chf.ch;
                    cellHeight[ci] = ClassifyClearance(clearance, settings);
                }
            }

            int spread = Math.Max(0, settings.HeightLimitedAreaSpread);
            int crouchExtra = Math.Max(0, settings.HeightLimitedAreaSpreadExtraForNonDeepCrouch);
            _ = settings.HeightLimitedAreaModeFilterPasses;

            DilateCellHeightMarks(cellHeight, active, chf.width, chf.height, NavigationMesh.AreaHeight.DeepCrouch, spread);
            DilateCellHeightMarks(cellHeight, active, chf.width, chf.height, NavigationMesh.AreaHeight.Crouch, spread + crouchExtra);

            for (int i = 0; i < polyCount; i++)
            {
                if (keep != null && i < keep.Length && !keep[i])
                    continue;
                DtPoly poly = data.polys[i];
                if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION)
                    continue;

                NavigationMesh.AreaHeight h = SampleCellHeight(chf, cellHeight, data, poly);
                NavigationMesh.dt_area_t area = areas[i];
                area.SetHeightLimitedAmount(h);
                areas[i] = area;

                if (h == NavigationMesh.AreaHeight.Crouch)
                    crouchCount++;
                else if (h == NavigationMesh.AreaHeight.DeepCrouch)
                    deepCrouchCount++;
            }
        }

        static void MarkPolyCells(
            RcCompactHeightfield chf,
            DtMeshData data,
            DtPoly poly,
            bool[] active)
        {
            void Mark(float x, float z)
            {
                int ix = (int)Math.Floor((x - chf.bmin.X) / chf.cs);
                int iz = (int)Math.Floor((z - chf.bmin.Z) / chf.cs);
                if (ix < 0 || iz < 0 || ix >= chf.width || iz >= chf.height)
                    return;
                active[ix + iz * chf.width] = true;
            }

            Vector3 c = PolyCentroid(data, poly);
            Mark(c.X, c.Z);
            for (int v = 0; v < poly.vertCount; v++)
            {
                int o = poly.verts[v] * 3;
                Mark(data.verts[o], data.verts[o + 2]);
            }

            // Light fill: mark cells along each edge so thin polys still cover a rim.
            for (int v = 0; v < poly.vertCount; v++)
            {
                int o0 = poly.verts[v] * 3;
                int o1 = poly.verts[(v + 1) % poly.vertCount] * 3;
                float x0 = data.verts[o0], z0 = data.verts[o0 + 2];
                float x1 = data.verts[o1], z1 = data.verts[o1 + 2];
                float len = MathF.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
                int steps = Math.Max(1, (int)MathF.Ceiling(len / chf.cs));
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    Mark(x0 + (x1 - x0) * t, z0 + (z1 - z0) * t);
                }
            }
        }

        static NavigationMesh.AreaHeight ClassifyClearance(float clearance, NavMeshBakeSettings settings)
        {
            if (float.IsInfinity(clearance) || clearance >= settings.CrouchHeight)
                return NavigationMesh.AreaHeight.Standing;
            if (clearance >= settings.DeepCrouchHeight)
                return NavigationMesh.AreaHeight.Crouch;
            return NavigationMesh.AreaHeight.DeepCrouch;
        }

        static int HeightRank(NavigationMesh.AreaHeight h)
        {
            switch (h)
            {
                case NavigationMesh.AreaHeight.DeepCrouch: return 2;
                case NavigationMesh.AreaHeight.Crouch: return 1;
                default: return 0;
            }
        }

        static void DilateCellHeightMarks(
            NavigationMesh.AreaHeight[] cells,
            bool[] active,
            int width,
            int height,
            NavigationMesh.AreaHeight minMark,
            int passes)
        {
            if (passes <= 0 || cells == null || width <= 0 || height <= 0)
                return;

            int minRank = HeightRank(minMark);
            int n = cells.Length;
            var next = new NavigationMesh.AreaHeight[n];
            int[] dx = { -1, 1, 0, 0 };
            int[] dz = { 0, 0, -1, 1 };

            for (int pass = 0; pass < passes; pass++)
            {
                Array.Copy(cells, next, n);
                for (int z = 0; z < height; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = x + z * width;
                        if (active != null && !active[i])
                            continue;
                        if (HeightRank(cells[i]) >= minRank)
                            continue;

                        for (int d = 0; d < 4; d++)
                        {
                            int nx = x + dx[d];
                            int nz = z + dz[d];
                            if (nx < 0 || nz < 0 || nx >= width || nz >= height)
                                continue;
                            int ni = nx + nz * width;
                            if (active != null && !active[ni])
                                continue;
                            if (HeightRank(cells[ni]) < minRank)
                                continue;

                            if (HeightRank(cells[ni]) > HeightRank(next[i]))
                                next[i] = cells[ni];
                            else if (HeightRank(next[i]) < minRank)
                                next[i] = minMark;
                        }
                    }
                }
                Array.Copy(next, cells, n);
            }
        }

        static NavigationMesh.AreaHeight SampleCellHeight(
            RcCompactHeightfield chf,
            NavigationMesh.AreaHeight[] cellHeight,
            DtMeshData data,
            DtPoly poly)
        {
            NavigationMesh.AreaHeight worst = NavigationMesh.AreaHeight.Standing;

            void Consider(float x, float z)
            {
                int ix = (int)Math.Floor((x - chf.bmin.X) / chf.cs);
                int iz = (int)Math.Floor((z - chf.bmin.Z) / chf.cs);
                if (ix < 0 || iz < 0 || ix >= chf.width || iz >= chf.height)
                    return;
                NavigationMesh.AreaHeight h = cellHeight[ix + iz * chf.width];
                if (HeightRank(h) > HeightRank(worst))
                    worst = h;
            }

            Vector3 c = PolyCentroid(data, poly);
            Consider(c.X, c.Z);
            for (int v = 0; v < poly.vertCount; v++)
            {
                int o = poly.verts[v] * 3;
                Consider(data.verts[o], data.verts[o + 2]);
            }
            return worst;
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
        /// Drop kept polys whose centroid sits above the primary floor median by more than
        /// <see cref="NavMeshBakeSettings.ElevatedPolyStripAboveFloor"/> (prop / duct tops
        /// still linked into the main component).
        /// </summary>
        static int StripElevatedPolys(DtMeshTile tile, NavMeshBakeSettings settings, bool[] keep)
        {
            if (keep == null)
                return 0;

            int polyCount = tile.data.header.polyCount;
            var ys = new List<float>();
            for (int i = 0; i < polyCount; i++)
            {
                if (!keep[i])
                    continue;
                ys.Add(PolyCentroid(tile.data, tile.data.polys[i]).Y);
            }
            if (ys.Count == 0)
                return 0;

            ys.Sort();
            float medianY = ys[ys.Count / 2];
            float maxY = medianY + Math.Max(0.05f, settings.ElevatedPolyStripAboveFloor);

            int culled = 0;
            for (int i = 0; i < polyCount; i++)
            {
                if (!keep[i])
                    continue;
                float y = PolyCentroid(tile.data, tile.data.polys[i]).Y;
                if (y > maxY)
                {
                    keep[i] = false;
                    culled++;
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
            List<CollisionNavMeshSoup.OffMeshLinkDraft> offMeshDrafts)
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
                    omPoly.neis[0] = DtDetour.DT_EXT_LINK;
                    omPoly.neis[1] = DtDetour.DT_EXT_LINK;
                    polyList.Add(omPoly);
                    dstDetail.Add(new NavigationMesh.dtPolyDetail());

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

            // Rebuild BV tree over kept polys (quantized like Detour CreateBVTree).
            NavigationMesh.dtBVNode[] bvNodes = BuildBvTree(dstPolys, dstVerts, srcHeader.bmin, settings.CellSize);

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
                maxLinkCount = links.Count,
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

        static NavigationMesh.dtBVNode[] BuildBvTree(
            NavigationMesh.dtPoly[] polys,
            List<Vector3> verts,
            RcVec3f bmin,
            float cellSize)
        {
            int n = polys.Length;
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
