#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CATHODE;
using CATHODE.Scripting;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// First-pass cover generation from instanced collision, driven by
    /// <see cref="CoverBakeSettings"/> (COVERANDTRAVERSALRULES.BML defaults).
    /// </summary>
    public static class CoverBaker
    {
        public sealed class BakeResult
        {
            public Cover Cover;
            public int InputTriangles;
            public int SampleCount;
            public int SegmentCount;
            public int SlotCount;
            public int SlotsWithoutLineOfSight;
            public int SlotsOffNavMesh;
            public string Message;
        }

        const int DirCount = 8;
        // Cardinal + diagonal (retail cover frequently sits on 30–60° normals).
        static readonly int[] DirX = { 1, 1, 0, -1, -1, -1, 0, 1 };
        static readonly int[] DirZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

        /// <summary>
        /// Generate cover for state 0 (and copy to other states for now) from collision.
        /// Does not write disk — <see cref="Level.Save"/> persists COVER.
        /// </summary>
        public static BakeResult BakeLevel(Level level, Instancing placement, CoverBakeSettings settings)
        {
            // No settings means the caller did not ask for cover, so nothing is baked and the
            // level keeps whatever COVER it already had.
            if (settings == null)
                return null;
            if (level == null)
                throw new ArgumentNullException(nameof(level));

            var navSettings = NavMeshBakeSettings.CreateDefault();
            navSettings.SkipSmallPropCollision = !settings.IncludeSmallPropCollision;
            // You cannot hide behind a window, and you can see straight through one.
            navSettings.SkipTransparentCollision = settings.SkipGlass;

            if (placement != null && placement.CoverExclusionBoxes.Count > 0)
                settings.ExclusionBoxes = placement.CoverExclusionBoxes;

            CollisionNavMeshSoup soup = CollisionNavMeshSoup.CollectFromLevel(
                level,
                null,
                placement == null ? new CollisionNavMeshSoup() : null,
                navSettings,
                placement);
            BakeResult result = BakeFromSoup(soup, settings, level.StateResources.Count > 0 ? level.StateResources[0].NavMesh : null);
            result.Message = $"Cover bake: tris={result.InputTriangles} samples={result.SampleCount} segs={result.SegmentCount} slots={result.SlotCount}.";

            if (level.StateResources.Count == 0)
            {
                level.StateResources.Add(new Level.State { Cover = result.Cover });
            }
            else
            {
                for (int i = 0; i < level.StateResources.Count; i++)
                {
                    // Per-state ExclusiveMaster filtering can come later; first pass shares state-0 cover.
                    level.StateResources[i].Cover = i == 0 ? result.Cover : CloneCover(result.Cover);
                }
            }
            return result;
        }

        public static BakeResult BakeFromSoup(CollisionNavMeshSoup soup, CoverBakeSettings settings, NavigationMesh navMesh = null)
        {
            if (soup == null)
                throw new ArgumentNullException(nameof(soup));
            settings ??= new CoverBakeSettings();

            var result = new BakeResult { InputTriangles = soup.TriangleCount, Cover = CreateEmptyCover() };
            if (soup.TriangleCount == 0)
            {
                result.Message = "No collision triangles.";
                return result;
            }

            if (settings.UseRimGenerator && navMesh?.Polygons != null)
                return BakeFromRim(soup, settings, navMesh, result);

            float cellSize = settings.CoverGridCellSize > 0 ? settings.CoverGridCellSize : settings.SamplingSizeXZ;
            float cell = Math.Max(0.025f, cellSize);
            GetSoupBounds(soup, out Vector3 bmin, out Vector3 bmax);
            if (navMesh?.Vertices != null && navMesh.Vertices.Length > 0)
            {
                GetNavBounds(navMesh, out Vector3 nmin, out Vector3 nmax);
                float pad = Math.Max(2f, settings.RequiredClearanceDistance + settings.HeightSamplingDistanceAlongNormal + 1f);
                nmin -= new Vector3(pad, 0, pad);
                nmax += new Vector3(pad, 0, pad);
                bmin = Vector3.Max(bmin, nmin);
                bmax = Vector3.Min(bmax, nmax);
            }
            bmin -= new Vector3(cell, 0, cell);
            bmax += new Vector3(cell, 0, cell);

            int w = Math.Max(1, (int)Math.Ceiling((bmax.X - bmin.X) / cell));
            int d = Math.Max(1, (int)Math.Ceiling((bmax.Z - bmin.Z) / cell));
            if (w * d > 25_000_000)
                throw new InvalidOperationException($"Cover sampling grid too large ({w}x{d}).");

            var floorY = new float[w * d];
            var solidTop = new float[w * d];
            var hasFloor = new bool[w * d];
            bool useNavEdges = settings.PreferNavMeshBoundaryEdges && navMesh?.Polygons != null && navMesh.Vertices != null;
            float offset = settings.DistanceFromGeometry;
            List<(Vector3 a, Vector3 b)> navBoundaryEdges = useNavEdges && settings.FloorGapFillNavEdges
                ? CollectNavBoundaryEdges(navMesh)
                : null;

            // Multi-deck: bake each navmesh Y-band separately so upper platforms aren't
            // crushed into the lowest floor of the same XZ column. Band ranges are disjoint
            // so the same obstacle isn't sampled twice from adjacent decks.
            List<(float y, float min, float max)> bands = CollectFloorBands(navMesh, settings);
            var allSegments = new List<Cover.CoverSegment>();
            int totalSamples = 0;
            int islandCount = 0;
            int solidCells = 0;

            foreach (var band in bands)
            {
                float bandMin = band.min;
                float bandMax = band.max;
                for (int i = 0; i < floorY.Length; i++)
                {
                    floorY[i] = float.PositiveInfinity;
                    solidTop[i] = float.NegativeInfinity;
                    hasFloor[i] = false;
                }

                RasterizeFloorAndSolid(soup, bmin, cell, w, d, floorY, solidTop, hasFloor, settings, bandMin, bandMax);
                if (settings.FilterSolidIslands)
                    islandCount += FilterSolidIslands(floorY, solidTop, hasFloor, w, d, cell, settings);

                for (int i = 0; i < solidTop.Length; i++)
                {
                    if (hasFloor[i] && solidTop[i] >= floorY[i] + settings.MinimumHeight)
                        solidCells++;
                }

                if (useNavEdges)
                {
                    var bandSegs = BuildSegmentsFromNavMeshEdges(navMesh, bmin, cell, w, d, floorY, solidTop, hasFloor, settings, bandMin, bandMax);
                    totalSamples += bandSegs.Count;
                    allSegments.AddRange(bandSegs);

                    // Gap-fill: floor samples only where nav-edge cover is sparse, and only on
                    // nav boundaries (retail cover is boundary-aligned).
                    if (settings.FloorGapFillNavEdges)
                    {
                        List<CoverSample> samples = SampleFloorGrid(bmin, cell, w, d, floorY, solidTop, hasFloor, settings);
                        var floorSegs = MergeSamplesToSegments(samples, cell, settings, offset);
                        float gap = Math.Max(settings.GapFillSkipIfNearExisting, settings.NavMeshProximity);
                        float boundMax = settings.GapFillMaxBoundaryDistance;
                        foreach (var fs in floorSegs)
                        {
                            Vector3 fm = (fs.Left + fs.Right) * 0.5f;
                            if (fm.Y < bandMin || fm.Y > bandMax)
                                continue;
                            if (navBoundaryEdges != null && navBoundaryEdges.Count > 0)
                            {
                                float bedge = float.MaxValue;
                                for (int e = 0; e < navBoundaryEdges.Count; e++)
                                {
                                    float edgeDist = DistPointToSegmentXZ(fm, navBoundaryEdges[e].a, navBoundaryEdges[e].b);
                                    if (edgeDist < bedge) bedge = edgeDist;
                                }
                                if (bedge > boundMax)
                                    continue;
                            }
                            bool covered = false;
                            foreach (var ns in bandSegs)
                            {
                                if (Math.Abs(fs.Height - ns.Height) > settings.ColinearMergeMaxHeightDifference)
                                    continue;
                                if (Vector3.Dot(SafeXZ(fs.Normal), SafeXZ(ns.Normal)) < 0.85f)
                                    continue;
                                Vector3 nm = (ns.Left + ns.Right) * 0.5f;
                                if (Math.Abs(fm.Y - nm.Y) > 0.85f)
                                    continue;
                                if (DistPointToSegmentXZ(fm, ns.Left, ns.Right) <= gap)
                                {
                                    covered = true;
                                    break;
                                }
                            }
                            if (!covered)
                                allSegments.Add(fs);
                        }
                        totalSamples += samples.Count;
                    }
                }
                else
                {
                    List<CoverSample> samples = SampleFloorGrid(bmin, cell, w, d, floorY, solidTop, hasFloor, settings);
                    totalSamples += samples.Count;
                    allSegments.AddRange(MergeSamplesToSegments(samples, cell, settings, offset));
                }
            }

            List<Cover.CoverSegment> segments = MergeColinearSegments(allSegments, settings);
            segments = SuppressOverlappingSegments(segments, settings);
            segments = MergeColinearSegments(segments, settings);
            segments = DeduplicateSegments(segments, settings.ConnectingDistanceBetweenSegmentEnds * 3f);
            if (settings.RequireNearNavMesh && navMesh != null)
                segments = FilterNearNavMesh(segments, navMesh, settings.NavMeshProximity);
            if (settings.MaxDistanceToNavBoundary > 0f && navMesh != null)
                segments = FilterNearNavBoundary(segments, navMesh, settings.MaxDistanceToNavBoundary);
            result.SampleCount = totalSamples;

            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].UID = i + 1;
                segments[i].CathodeIndex = -1;
                segments[i].CathodeEnt = ShortGuid.Invalid;
                segments[i].CathodeParent = ShortGuid.Invalid;
                segments[i].LeftCornerUID = 0;
                segments[i].RightCornerUID = 0;
                segments[i].LeftColinearUID = 0;
                segments[i].RightColinearUID = 0;
                segments[i].TraversalUID = 0;
            }
            LinkCornersAndColinear(segments, settings);

            var aimSolver = new CoverAimSolver(soup, navMesh, settings);
            PlaceSlots(segments, settings, aimSolver);

            // A segment whose every slot was rejected is not usable cover.
            segments = RemoveUnoccupiableSegments(segments);
            ApplyLinkFlags(segments, settings);

            result.SlotsWithoutLineOfSight = aimSolver.SlotsWithoutLineOfSight;
            result.SlotsOffNavMesh = aimSolver.SlotsOffNavMesh;

            int slotUid = 0;
            int slotCount = 0;
            foreach (var seg in segments)
            {
                foreach (var slot in seg.OccupancySlots)
                    slot.UID = slotUid++;
                slotCount += seg.OccupancySlots.Count;
            }

            Cover cover = CreateEmptyCover();
            cover.Entries.AddRange(segments);
            BuildTraversalGrid(cover, settings.TraversalUnitSize);
            result.Cover = cover;
            result.SegmentCount = segments.Count;
            result.SlotCount = slotCount;
            result.Message = $"Cover bake: tris={result.InputTriangles} islands={islandCount} solidCells={solidCells} samples={result.SampleCount} segs={result.SegmentCount} slots={result.SlotCount}";
            return result;
        }

        /// <summary>
        /// Rim-driven generation. Everything after the segments themselves - UIDs, corner and
        /// colinear links, occupancy slots, the traversal grid - is shared with the older path.
        /// </summary>
        static BakeResult BakeFromRim(CollisionNavMeshSoup soup, CoverBakeSettings settings, NavigationMesh navMesh, BakeResult result)
        {
            List<Cover.CoverSegment> segments = RimCoverGenerator.Generate(soup, navMesh, settings, out string message);
            int raw = segments.Count;

            // Spans come out fragmented wherever a probe flickers; stitch them back into runs before
            // the length cull, then drop what is still too short to stand behind.
            segments = MergeColinearSegments(segments, settings);
            segments = DeduplicateSegments(segments, 0.35f);
            segments = RemoveSegmentsBehindSegments(segments, settings);
            segments.RemoveAll(s => SegmentLengthXZ(s) < settings.MinimumLength);
            segments = KeepOneArmPerCorner(segments, soup, settings);
            segments = RemoveUnreachableSegments(segments, navMesh, settings);
            message += $" merged {raw}->{segments.Count}";

            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].UID = i + 1;
                segments[i].CathodeIndex = -1;
                segments[i].CathodeEnt = ShortGuid.Invalid;
                segments[i].CathodeParent = ShortGuid.Invalid;
                segments[i].LeftCornerUID = 0;
                segments[i].RightCornerUID = 0;
                segments[i].LeftColinearUID = 0;
                segments[i].RightColinearUID = 0;
                segments[i].TraversalUID = 0;
            }
            LinkCornersAndColinear(segments, settings);

            var aimSolver = new CoverAimSolver(soup, navMesh, settings);
            PlaceSlots(segments, settings, aimSolver);
            segments = RemoveUnoccupiableSegments(segments);

            result.SlotsWithoutLineOfSight = aimSolver.SlotsWithoutLineOfSight;
            result.SlotsOffNavMesh = aimSolver.SlotsOffNavMesh;

            int slotUid = 0, slotCount = 0;
            foreach (var seg in segments)
            {
                foreach (var slot in seg.OccupancySlots)
                    slot.UID = slotUid++;
                slotCount += seg.OccupancySlots.Count;
            }

            // UIDs have to be dense and 1-based after the unoccupiable pass removed segments, or the
            // corner/colinear links point at nothing.
            RenumberSegments(segments);
            ApplyLinkFlags(segments, settings);

            Cover cover = CreateEmptyCover();
            cover.Entries.AddRange(segments);
            BuildTraversalGrid(cover, settings.TraversalUnitSize);
            result.Cover = cover;
            result.SegmentCount = segments.Count;
            result.SlotCount = slotCount;
            result.Message = message + $" -> kept {segments.Count} after occupancy, slots={slotCount}";
            return result;
        }

        /// <summary>
        /// Give the surviving segments consecutive UIDs and rewrite the links that referred to the
        /// old ones.
        /// </summary>
        static void RenumberSegments(List<Cover.CoverSegment> segments)
        {
            var remap = new Dictionary<int, int>();
            for (int i = 0; i < segments.Count; i++)
                remap[segments[i].UID] = i + 1;

            foreach (Cover.CoverSegment s in segments)
            {
                s.LeftCornerUID = Remap(remap, s.LeftCornerUID);
                s.RightCornerUID = Remap(remap, s.RightCornerUID);
                s.LeftColinearUID = Remap(remap, s.LeftColinearUID);
                s.RightColinearUID = Remap(remap, s.RightColinearUID);
            }
            for (int i = 0; i < segments.Count; i++)
                segments[i].UID = i + 1;
        }

        static int Remap(Dictionary<int, int> remap, int uid) =>
            uid != 0 && remap.TryGetValue(uid, out int mapped) ? mapped : 0;

        static Cover CreateEmptyCover()
        {
            // Minimal valid COVER payload: magic, version, zero segments, zero slots, empty grid.
            using (var ms = new System.IO.MemoryStream())
            using (var bw = new System.IO.BinaryWriter(ms))
            {
                bw.Write(846362211);
                bw.Write(7);
                bw.Write((short)0);
                bw.Write(0); // numSlots
                bw.Write((short)0);
                bw.Write((short)0);
                bw.Write(0f);
                bw.Write(0f);
                bw.Write(4f);
                return new Cover(ms.ToArray(), "");
            }
        }

        struct CoverSample
        {
            public int X, Z, Dir;
            public Vector3 Position;
            public float FloorY;
            public float Height;
            public Vector3 Normal;
        }

        const ushort DtExtLink = 0x8000;

        static List<CoverSample> SampleFloorGrid(
            Vector3 bmin,
            float cell,
            int w,
            int d,
            float[] floorY,
            float[] solidTop,
            bool[] hasFloor,
            CoverBakeSettings settings)
        {
            var samples = new List<CoverSample>(4096);
            float minH = settings.MinimumHeight;
            float clearDist = Math.Max(0.35f,
                settings.RequiredClearanceDistance - settings.RequiredClearanceGraceDistance);
            float offset = settings.DistanceFromGeometry;
            float probeDist = Math.Max(offset, settings.HeightSamplingDistanceAlongNormal);

            for (int z = 1; z < d - 1; z++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    int idx = z * w + x;
                    if (!hasFloor[idx])
                        continue;
                    float fy = floorY[idx];
                    if (solidTop[idx] >= fy + minH)
                        continue;

                    int dirCount = settings.AllowDiagonalSampling ? DirCount : 4;
                    // Cardinal dirs are at indices 0,2,4,6 in the 8-dir table; when diagonals
                    // are off we iterate 0..3 mapped through CardinalDirs.
                    for (int di = 0; di < dirCount; di++)
                    {
                        int dir = settings.AllowDiagonalSampling ? di : di * 2;
                        bool diag = DirX[dir] != 0 && DirZ[dir] != 0;
                        float stepLen = diag ? cell * 1.41421356f : cell;
                        int probeCells = Math.Max(1, (int)Math.Ceiling(probeDist / stepLen));
                        int clearCells = Math.Max(1, (int)Math.Ceiling(clearDist / stepLen));

                        float obstTop = float.NegativeInfinity;
                        int hitStep = -1;
                        for (int step = 1; step <= probeCells + 1; step++)
                        {
                            int nx = x + DirX[dir] * step;
                            int nz = z + DirZ[dir] * step;
                            if (nx < 0 || nz < 0 || nx >= w || nz >= d)
                                break;
                            int nidx = nz * w + nx;
                            if (solidTop[nidx] >= fy + minH)
                            {
                                obstTop = solidTop[nidx];
                                hitStep = step;
                                break;
                            }
                            if (!hasFloor[nidx])
                                break;
                            if (Math.Abs(floorY[nidx] - fy) > settings.SupportingFloorHeightTolerance)
                                break;
                        }
                        if (hitStep < 0 || hitStep > probeCells)
                            continue;

                        float obstHeight = obstTop - fy;
                        if (obstHeight < minH)
                            continue;
                        // Tall walls still provide standing cover — clamp rather than reject.
                        if (obstHeight > settings.MaximumObstacleHeight)
                            obstHeight = settings.MaximumObstacleHeight;
                        float coverH = settings.ClassifyCoverHeight(obstHeight);

                        bool clear = true;
                        for (int step = 1; step <= clearCells; step++)
                        {
                            int cx = x - DirX[dir] * step;
                            int cz = z - DirZ[dir] * step;
                            if (cx < 0 || cz < 0 || cx >= w || cz >= d) { clear = false; break; }
                            int cidx = cz * w + cx;
                            if (!hasFloor[cidx] || solidTop[cidx] >= floorY[cidx] + minH) { clear = false; break; }
                            if (Math.Abs(floorY[cidx] - fy) > settings.SupportingFloorHeightTolerance) { clear = false; break; }
                        }
                        if (!clear)
                            continue;

                        Vector3 wallNormal = Vector3.Normalize(new Vector3(DirX[dir], 0, DirZ[dir]));
                        samples.Add(new CoverSample
                        {
                            X = x,
                            Z = z,
                            Dir = dir,
                            Position = new Vector3(bmin.X + (x + 0.5f) * cell, fy, bmin.Z + (z + 0.5f) * cell),
                            FloorY = fy,
                            Height = coverH,
                            Normal = -wallNormal
                        });
                    }
                }
            }
            return samples;
        }

        /// <summary>
        /// Emit one cover segment per navmesh boundary edge that faces a kept solid island.
        /// </summary>
        static List<Cover.CoverSegment> BuildSegmentsFromNavMeshEdges(
            NavigationMesh navMesh,
            Vector3 bmin,
            float cell,
            int w,
            int d,
            float[] floorY,
            float[] solidTop,
            bool[] hasFloor,
            CoverBakeSettings settings,
            float bandMin = float.NegativeInfinity,
            float bandMax = float.PositiveInfinity)
        {
            var segments = new List<Cover.CoverSegment>();
            float minH = settings.MinimumHeight;
            float probe = Math.Max(settings.DistanceFromGeometry + cell, settings.HeightSamplingDistanceAlongNormal);
            int probeCells = Math.Max(1, (int)Math.Ceiling(probe / cell));
            int clearCells = Math.Max(1, (int)Math.Ceiling(
                Math.Max(0.35f, settings.RequiredClearanceDistance - settings.RequiredClearanceGraceDistance) / cell));
            // Accept short boundary edges; MinimumLength is applied after colinear merge.
            float minEdgeLen = Math.Max(0.15f, cell * 2f);

            foreach (var poly in navMesh.Polygons)
            {
                if (poly.vertCount < 3)
                    continue;
                if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND)
                    continue;
                // Backstage is the alien's ceiling network, not playspace.
                if (((uint)poly.area.GetMarkupFlags() & (uint)NavigationMesh.NavMeshAreaTypeFlags.BackstageFlag) != 0)
                    continue;

                for (int i = 0; i < poly.vertCount; i++)
                {
                    ushort nei = poly.neis[i];
                    if (!(nei == 0 || (nei & DtExtLink) != 0))
                        continue;

                    Vector3 a = navMesh.Vertices[poly.verts[i]];
                    Vector3 b = navMesh.Vertices[poly.verts[(i + 1) % poly.vertCount]];
                    float midY = (a.Y + b.Y) * 0.5f;
                    if (midY < bandMin || midY > bandMax)
                        continue;

                    Vector3 along = b - a; along.Y = 0;
                    float len = along.Length();
                    if (len < minEdgeLen)
                        continue;
                    along /= len;

                    Vector3[] outwards =
                    {
                        new Vector3(along.Z, 0, -along.X),
                        new Vector3(-along.Z, 0, along.X)
                    };

                    foreach (Vector3 outward in outwards)
                    {
                        Vector3 mid = (a + b) * 0.5f;
                        int probeCellsUse = Math.Max(probeCells, (int)Math.Ceiling((probe + 0.25f) / cell));
                        if (!TryProbeSolid(mid, outward, bmin, cell, w, d, floorY, solidTop, hasFloor,
                                probeCellsUse, minH, settings, out float obstTop, out float fy, out _, out _))
                            continue;

                        if (!HasClearance(mid, -outward, bmin, cell, w, d, floorY, solidTop, hasFloor, clearCells, minH, fy, settings))
                            continue;

                        float obstHeight = obstTop - fy;
                        if (obstHeight < minH)
                            continue;
                        // Tall walls still provide standing cover — clamp rather than reject.
                        if (obstHeight > settings.MaximumObstacleHeight)
                            obstHeight = settings.MaximumObstacleHeight;
                        float coverH = settings.ClassifyCoverHeight(obstHeight);

                        Vector3 normal = -outward;
                        Vector3 left = a + outward * settings.DistanceFromGeometry;
                        Vector3 right = b + outward * settings.DistanceFromGeometry;
                        left.Y = fy; right.Y = fy;
                        Vector3 delta = right - left; delta.Y = 0;
                        if (delta.LengthSquared() > 1e-8f && Vector3.Cross(Vector3.Normalize(delta), normal).Y < 0)
                            (left, right) = (right, left);

                        segments.Add(new Cover.CoverSegment
                        {
                            Left = left,
                            Right = right,
                            Normal = normal,
                            Height = coverH,
                            Flags = coverH < 1.2f ? 0x2000 : 0
                        });
                        break;
                    }
                }
            }

            // Nav boundary edges are often short/choppy; allow generous end joins then cull length.
            float savedMove = settings.ColinearMergeMaxMovement;
            settings.ColinearMergeMaxMovement = Math.Max(savedMove, 1.25f);
            segments = MergeColinearSegments(segments, settings);
            settings.ColinearMergeMaxMovement = savedMove;
            segments = DeduplicateSegments(segments, 0.35f);
            segments.RemoveAll(s => SegmentLengthXZ(s) < settings.MinimumLength);
            return segments;
        }

        static List<Cover.CoverSegment> MergeColinearSegments(List<Cover.CoverSegment> segments, CoverBakeSettings settings)
        {
            if (segments.Count < 2)
                return segments;

            var used = new bool[segments.Count];
            var merged = new List<Cover.CoverSegment>();
            float joinDist = settings.ColinearMergeJoinDistance > 0f
                ? settings.ColinearMergeJoinDistance
                : Math.Max(settings.ConnectingDistanceBetweenSegmentEnds, settings.ColinearMergeMaxMovement);
            float minDot = (float)Math.Cos(settings.ColinearMergeMaxAngleDifferenceDegrees * Math.PI / 180.0);

            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i]) continue;
                var cur = segments[i];
                used[i] = true;
                bool grew;
                do
                {
                    grew = false;
                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (used[j]) continue;
                        var o = segments[j];
                        if (Math.Abs(cur.Height - o.Height) > settings.ColinearMergeMaxHeightDifference)
                            continue;
                        if (Vector3.Dot(SafeXZ(cur.Normal), SafeXZ(o.Normal)) < minDot)
                            continue;

                        if (TryJoin(ref cur, o, joinDist, settings) || TryJoin(ref cur, SwapEnds(o), joinDist, settings))
                        {
                            used[j] = true;
                            grew = true;
                        }
                    }
                } while (grew);
                merged.Add(cur);
            }
            return merged;
        }

        static Cover.CoverSegment SwapEnds(Cover.CoverSegment s)
        {
            return new Cover.CoverSegment
            {
                Left = s.Right,
                Right = s.Left,
                Normal = s.Normal,
                Height = s.Height,
                Flags = s.Flags
            };
        }

        static bool TryJoin(ref Cover.CoverSegment a, Cover.CoverSegment b, float joinDist, CoverBakeSettings settings)
        {
            float maxY = settings.ColinearMergeMaxMovementY;
            // A short segment may not reach across a gap as long as itself. See
            // CoverBakeSettings.ColinearMergeMaxProportionalGap.
            if (settings.ColinearMergeMaxProportionalGap > 0f)
            {
                float shorter = Math.Min(SegmentLengthXZ(a), SegmentLengthXZ(b));
                float cap = shorter * settings.ColinearMergeMaxProportionalGap;
                if (cap < joinDist) joinDist = cap;
            }
            if (Vector3.DistanceSquared(a.Right, b.Left) <= joinDist * joinDist
                && Math.Abs(a.Right.Y - b.Left.Y) <= maxY)
            {
                a.Right = b.Right;
                a.Right.Y = (a.Left.Y + b.Right.Y) * 0.5f;
                a.Left.Y = a.Right.Y;
                return true;
            }
            if (Vector3.DistanceSquared(a.Left, b.Right) <= joinDist * joinDist
                && Math.Abs(a.Left.Y - b.Right.Y) <= maxY)
            {
                a.Left = b.Left;
                a.Left.Y = (a.Right.Y + b.Left.Y) * 0.5f;
                a.Right.Y = a.Left.Y;
                return true;
            }
            return false;
        }

        static bool TryProbeSolid(
            Vector3 origin,
            Vector3 dir,
            Vector3 bmin,
            float cell,
            int w,
            int d,
            float[] floorY,
            float[] solidTop,
            bool[] hasFloor,
            int probeCells,
            float minH,
            CoverBakeSettings settings,
            out float obstTop,
            out float fy,
            out int hitX,
            out int hitZ)
        {
            obstTop = float.NegativeInfinity;
            fy = origin.Y;
            hitX = hitZ = -1;
            int ox = (int)Math.Floor((origin.X - bmin.X) / cell);
            int oz = (int)Math.Floor((origin.Z - bmin.Z) / cell);
            if (ox >= 0 && oz >= 0 && ox < w && oz < d && hasFloor[oz * w + ox])
                fy = floorY[oz * w + ox];

            for (int step = 1; step <= probeCells; step++)
            {
                Vector3 p = origin + dir * (step * cell);
                int x = (int)Math.Floor((p.X - bmin.X) / cell);
                int z = (int)Math.Floor((p.Z - bmin.Z) / cell);
                if (x < 0 || z < 0 || x >= w || z >= d)
                    return false;
                int idx = z * w + x;
                if (solidTop[idx] >= fy + minH)
                {
                    obstTop = solidTop[idx];
                    hitX = x;
                    hitZ = z;
                    return true;
                }
            }
            return false;
        }

        static bool HasClearance(
            Vector3 origin,
            Vector3 dir,
            Vector3 bmin,
            float cell,
            int w,
            int d,
            float[] floorY,
            float[] solidTop,
            bool[] hasFloor,
            int clearCells,
            float minH,
            float fy,
            CoverBakeSettings settings)
        {
            for (int step = 1; step <= clearCells; step++)
            {
                Vector3 p = origin + dir * (step * cell);
                int x = (int)Math.Floor((p.X - bmin.X) / cell);
                int z = (int)Math.Floor((p.Z - bmin.Z) / cell);
                if (x < 0 || z < 0 || x >= w || z >= d)
                    return false;
                int idx = z * w + x;
                if (!hasFloor[idx])
                    return false;
                if (solidTop[idx] >= floorY[idx] + minH)
                    return false;
                if (Math.Abs(floorY[idx] - fy) > settings.SupportingFloorHeightTolerance)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Keep the longest segment when several share a midpoint and normal (parallel fragments).
        /// </summary>
        static List<Cover.CoverSegment> SuppressOverlappingSegments(List<Cover.CoverSegment> segments, CoverBakeSettings settings)
        {
            if (segments.Count < 2)
                return segments;

            // Longest first so we keep winners.
            var ordered = segments
                .Select((s, i) => (s, i, len: SegmentLengthXZ(s)))
                .OrderByDescending(t => t.len)
                .ToList();
            var drop = new bool[segments.Count];
            float midTolSame = 0.75f;
            float midTolSimilar = 1.5f;
            float midTolSameSq = midTolSame * midTolSame;
            float midTolSimilarSq = midTolSimilar * midTolSimilar;
            float yTol = Math.Max(0.85f, settings.ColinearMergeMaxMovementY * 6f);
            float lineTol = Math.Max(0.85f, settings.DistanceFromGeometry * 5f);

            for (int a = 0; a < ordered.Count; a++)
            {
                if (drop[ordered[a].i]) continue;
                Vector3 am = (ordered[a].s.Left + ordered[a].s.Right) * 0.5f;
                Vector3 an = SafeXZ(ordered[a].s.Normal);
                for (int b = a + 1; b < ordered.Count; b++)
                {
                    if (drop[ordered[b].i]) continue;
                    if (Math.Abs(ordered[a].s.Height - ordered[b].s.Height) > settings.ColinearMergeMaxHeightDifference)
                        continue;
                    Vector3 bm = (ordered[b].s.Left + ordered[b].s.Right) * 0.5f;
                    if (Math.Abs(am.Y - bm.Y) > yTol)
                        continue;
                    float distSq = new Vector3(am.X - bm.X, 0, am.Z - bm.Z).LengthSquared();
                    float dot = Vector3.Dot(an, SafeXZ(ordered[b].s.Normal));
                    // Same-facing duplicates / diagonal vs cardinal of the same face.
                    if (dot >= 0.95f && distSq <= midTolSameSq)
                    {
                        drop[ordered[b].i] = true;
                        continue;
                    }
                    if (dot >= 0.85f && distSq <= midTolSimilarSq)
                    {
                        float la = ordered[a].len, lb = ordered[b].len;
                        if (lb < la * 0.85f || (lb <= la * 1.05f && Math.Max(Math.Abs(an.X), Math.Abs(an.Z)) >= Math.Max(Math.Abs(SafeXZ(ordered[b].s.Normal).X), Math.Abs(SafeXZ(ordered[b].s.Normal).Z))))
                        {
                            drop[ordered[b].i] = true;
                            continue;
                        }
                    }
                    // Shorter run whose midpoint lies on the longer segment (same facing).
                    if (dot >= 0.9f && ordered[b].len <= ordered[a].len * 1.05f)
                    {
                        float dLine = DistPointToSegmentXZ(bm, ordered[a].s.Left, ordered[a].s.Right);
                        if (dLine <= lineTol)
                            drop[ordered[b].i] = true;
                    }
                }
            }

            var keep = new List<Cover.CoverSegment>();
            for (int i = 0; i < segments.Count; i++)
            {
                if (drop[i]) continue;
                if (SegmentLengthXZ(segments[i]) < settings.MinimumLength) continue;
                keep.Add(segments[i]);
            }
            return keep;
        }

        static float SegmentLengthXZ(Cover.CoverSegment s)
        {
            Vector3 d = s.Right - s.Left; d.Y = 0;
            return d.Length();
        }

        static float DistPointToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float abx = b.X - a.X, abz = b.Z - a.Z;
            float apx = p.X - a.X, apz = p.Z - a.Z;
            float ab2 = abx * abx + abz * abz;
            float t = ab2 > 1e-12f ? Math.Max(0f, Math.Min(1f, (apx * abx + apz * abz) / ab2)) : 0f;
            float qx = a.X + abx * t - p.X;
            float qz = a.Z + abz * t - p.Z;
            return (float)Math.Sqrt(qx * qx + qz * qz);
        }

        /// <summary>
        /// Drop whole segments whose floor in front is mostly unreachable - an alcove behind a neck.
        /// See <see cref="CoverBakeSettings.MinReachableShare"/>; applied here rather than per sample
        /// because a mid-run rejection ends the span and the pieces fall under the length cull.
        /// </summary>
        static List<Cover.CoverSegment> RemoveUnreachableSegments(List<Cover.CoverSegment> segments, NavigationMesh navMesh, CoverBakeSettings settings)
        {
            if (settings.MinReachableShare <= 0f || !settings.ReachableGateOnSegments || navMesh == null)
                return segments;

            var floor = new RimCoverGenerator.NavFloorGrid(navMesh);
            var keep = new List<Cover.CoverSegment>(segments.Count);
            foreach (Cover.CoverSegment s in segments)
            {
                Vector3 mid = (s.Left + s.Right) * 0.5f;
                Vector3 inward = SafeXZ(s.Normal);
                float share = floor.ReachableShare(mid + inward * settings.SlotStandOffset, mid.Y, settings.ReachableRadius);
                if (share >= settings.MinReachableShare)
                    keep.Add(s);
            }
            return keep;
        }

        /// <summary>
        /// Where two cover segments meet at a right angle, keep one and drop the other. See
        /// <see cref="CoverBakeSettings.CornerArmRule"/>.
        /// </summary>
        /// <remarks>
        /// Each corner is settled once and neither arm can go on to knock out a third segment: a
        /// curved wall's own pieces meet at an angle, and letting the cull chain through them removes
        /// most of a level.
        /// </remarks>
        static List<Cover.CoverSegment> KeepOneArmPerCorner(List<Cover.CoverSegment> segments, CollisionNavMeshSoup soup, CoverBakeSettings settings)
        {
            if (settings.CornerArmRule == 0 || segments.Count < 2)
                return segments;

            RimCoverGenerator.DepthProbe probe = settings.CornerArmRule == 1 ? new RimCoverGenerator.DepthProbe(soup) : null;
            int n = segments.Count;
            var score = new float[n];
            var length = new float[n];
            for (int i = 0; i < n; i++)
            {
                length[i] = SegmentLengthXZ(segments[i]);
                Vector3 mid = (segments[i].Left + segments[i].Right) * 0.5f;
                Vector3 outward = SafeXZ(segments[i].Normal);       // segment normals point away from the cover
                switch (settings.CornerArmRule)
                {
                    case 1:
                        score[i] = probe.FanMax(new Vector3(mid.X, mid.Y + 1.5f, mid.Z), outward, 60f, 10f, 30f);
                        break;
                    case 3:
                        score[i] = -segments[i].Height;
                        break;
                    default:
                        score[i] = length[i];
                        break;
                }
            }

            float join = settings.CornerArmJoinDistance;
            var settled = new bool[n];
            var drop = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (settled[i]) continue;
                for (int j = i + 1; j < n; j++)
                {
                    if (settled[j]) continue;
                    if (length[i] < settings.CornerArmMinLength || length[j] < settings.CornerArmMinLength) continue;
                    float dot = Vector3.Dot(SafeXZ(segments[i].Normal), SafeXZ(segments[j].Normal));
                    if (dot > 0.7f || dot < -0.7f) continue;
                    if (!CornerTouch(segments[i], segments[j], join)) continue;

                    int lose = score[i] >= score[j] ? j : i;
                    drop[lose] = true;
                    settled[i] = true;
                    settled[j] = true;
                    break;
                }
            }

            var keep = new List<Cover.CoverSegment>(n);
            for (int i = 0; i < n; i++) if (!drop[i]) keep.Add(segments[i]);
            return keep;
        }

        static float Flat2(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        static bool CornerTouch(Cover.CoverSegment a, Cover.CoverSegment b, float tol)
        {
            if (Math.Abs(a.Left.Y - b.Left.Y) > 1.5f) return false;
            return Flat2(a.Left, b.Left) < tol || Flat2(a.Left, b.Right) < tol
                || Flat2(a.Right, b.Left) < tol || Flat2(a.Right, b.Right) < tol;
        }

        /// <summary>
        /// Drop cover that stands behind other cover facing the same way. See
        /// <see cref="CoverBakeSettings.SameFacingClearance"/>.
        /// </summary>
        static List<Cover.CoverSegment> RemoveSegmentsBehindSegments(List<Cover.CoverSegment> segments, CoverBakeSettings settings)
        {
            float clearance = settings.SameFacingClearance;
            if (clearance <= 0f)
                return segments;

            var drop = new bool[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                if (drop[i]) continue;
                Vector3 ni = SafeXZ(segments[i].Normal);
                Vector3 mi = (segments[i].Left + segments[i].Right) * 0.5f;
                for (int j = i + 1; j < segments.Count; j++)
                {
                    if (drop[j]) continue;
                    Vector3 nj = SafeXZ(segments[j].Normal);
                    if (Vector3.Dot(ni, nj) < 0.94f) continue;             // not the same way
                    Vector3 mj = (segments[j].Left + segments[j].Right) * 0.5f;
                    if (Math.Abs(mi.Y - mj.Y) > 1.5f) continue;            // different storey

                    // Front to back along the shared normal, and side to side along the wall.
                    float front = Vector3.Dot(new Vector3(mj.X - mi.X, 0f, mj.Z - mi.Z), ni);
                    if (Math.Abs(front) >= clearance) continue;
                    Vector3 along = new Vector3(-ni.Z, 0f, ni.X);
                    float ai0 = Vector3.Dot(new Vector3(segments[i].Left.X, 0f, segments[i].Left.Z), along);
                    float ai1 = Vector3.Dot(new Vector3(segments[i].Right.X, 0f, segments[i].Right.Z), along);
                    float aj0 = Vector3.Dot(new Vector3(segments[j].Left.X, 0f, segments[j].Left.Z), along);
                    float aj1 = Vector3.Dot(new Vector3(segments[j].Right.X, 0f, segments[j].Right.Z), along);
                    if (Math.Min(ai0, ai1) >= Math.Max(aj0, aj1) || Math.Min(aj0, aj1) >= Math.Max(ai0, ai1))
                        continue;                                           // no overlap along the wall

                    // The one further along the shared normal is the one nearer the room; it wins.
                    if (front > 0f) { drop[i] = true; break; }
                    drop[j] = true;
                }
            }

            var keep = new List<Cover.CoverSegment>(segments.Count);
            for (int i = 0; i < segments.Count; i++)
                if (!drop[i]) keep.Add(segments[i]);
            return keep;
        }

        static List<Cover.CoverSegment> DeduplicateSegments(List<Cover.CoverSegment> segments, float midTol)
        {
            var keep = new List<Cover.CoverSegment>();
            foreach (var s in segments)
            {
                Vector3 mid = (s.Left + s.Right) * 0.5f;
                bool dup = false;
                foreach (var k in keep)
                {
                    Vector3 km = (k.Left + k.Right) * 0.5f;
                    if (Math.Abs(mid.Y - km.Y) > 0.75f)
                        continue;
                    if (Vector3.DistanceSquared(new Vector3(mid.X, 0, mid.Z), new Vector3(km.X, 0, km.Z)) > midTol * midTol)
                        continue;
                    float dot = Math.Abs(Vector3.Dot(SafeXZ(s.Normal), SafeXZ(k.Normal)));
                    if (dot > 0.85f && Math.Abs(s.Height - k.Height) < 0.05f)
                    {
                        dup = true;
                        break;
                    }
                }
                if (!dup)
                    keep.Add(s);
            }
            return keep;
        }

        /// <summary>
        /// Normal flattened onto XZ and renormalised. Scalar for the same reason as
        /// <see cref="AngleBetweenNormalsDeg"/> - see the note there.
        /// </summary>
        static Vector3 SafeXZ(Vector3 n)
        {
            float x = n.X, z = n.Z;
            float lenSq = x * x + z * z;
            if (lenSq <= 1e-8f)
                return new Vector3(0, 0, 1);

            float len = (float)Math.Sqrt(lenSq);
            return new Vector3(x / len, 0, z / len);
        }

        static void GetSoupBounds(CollisionNavMeshSoup soup, out Vector3 bmin, out Vector3 bmax)
        {
            bmin = new Vector3(float.PositiveInfinity);
            bmax = new Vector3(float.NegativeInfinity);
            for (int i = 0; i < soup.VertexCount; i++)
            {
                var p = new Vector3(soup.Verts[i * 3], soup.Verts[i * 3 + 1], soup.Verts[i * 3 + 2]);
                bmin = Vector3.Min(bmin, p);
                bmax = Vector3.Max(bmax, p);
            }
        }

        static void GetNavBounds(NavigationMesh nav, out Vector3 bmin, out Vector3 bmax)
        {
            bmin = new Vector3(float.PositiveInfinity);
            bmax = new Vector3(float.NegativeInfinity);
            for (int i = 0; i < nav.Vertices.Length; i++)
            {
                bmin = Vector3.Min(bmin, nav.Vertices[i]);
                bmax = Vector3.Max(bmax, nav.Vertices[i]);
            }
        }

        /// <summary>
        /// Keep only compact solid footprints (prop-scale islands). Clears solidTop on
        /// oversized components that behave like walls/decks.
        /// </summary>
        static int FilterSolidIslands(
            float[] floorY,
            float[] solidTop,
            bool[] hasFloor,
            int w,
            int d,
            float cell,
            CoverBakeSettings settings)
        {
            float minH = settings.MinimumHeight;
            float cellArea = cell * cell;
            int minCells = Math.Max(1, (int)Math.Ceiling(settings.MinimumIslandArea / cellArea));
            int maxCells = Math.Max(minCells, (int)Math.Ceiling(settings.MaximumIslandArea / cellArea));
            int maxExtent = Math.Max(1, (int)Math.Ceiling(settings.MaximumIslandExtent / cell));

            var label = new int[w * d];
            var stack = new Stack<int>();
            int kept = 0;

            for (int z = 0; z < d; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    int seed = z * w + x;
                    if (label[seed] != 0)
                        continue;
                    if (!IsSolidCell(seed, floorY, solidTop, hasFloor, minH))
                        continue;

                    var cells = new List<int>(64);
                    stack.Push(seed);
                    label[seed] = 1; // visited
                    int minX = x, maxX = x, minZ = z, maxZ = z;
                    while (stack.Count > 0)
                    {
                        int i = stack.Pop();
                        cells.Add(i);
                        int cx = i % w, cz = i / w;
                        minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
                        minZ = Math.Min(minZ, cz); maxZ = Math.Max(maxZ, cz);
                        for (int dir = 0; dir < DirCount; dir++)
                        {
                            int nx = cx + DirX[dir], nz = cz + DirZ[dir];
                            if (nx < 0 || nz < 0 || nx >= w || nz >= d)
                                continue;
                            int ni = nz * w + nx;
                            if (label[ni] != 0)
                                continue;
                            if (!IsSolidCell(ni, floorY, solidTop, hasFloor, minH))
                                continue;
                            label[ni] = 1;
                            stack.Push(ni);
                        }
                    }

                    int extX = maxX - minX;
                    int extZ = maxZ - minZ;
                    float thinAxis = Math.Min(extX, extZ) * cell;
                    bool thinWall = thinAxis <= Math.Max(settings.MaximumObstacleDepth, 1.25f)
                        && Math.Max(extX, extZ) * cell >= settings.MinimumLength;
                    bool compactOk = cells.Count >= minCells
                        && cells.Count <= maxCells
                        && extX <= maxExtent
                        && extZ <= maxExtent;
                    bool keep = compactOk || (thinWall && cells.Count >= minCells);
                    if (keep)
                    {
                        kept++;
                        foreach (int i in cells)
                            label[i] = 2; // keep
                    }
                    else
                    {
                        foreach (int i in cells)
                        {
                            label[i] = -1;
                            solidTop[i] = float.NegativeInfinity;
                        }
                    }
                }
            }
            return kept;
        }

        static bool IsSolidCell(int idx, float[] floorY, float[] solidTop, bool[] hasFloor, float minH)
        {
            if (float.IsNegativeInfinity(solidTop[idx]))
                return false;
            if (hasFloor[idx])
                return solidTop[idx] >= floorY[idx] + minH;
            return true;
        }

        static void RasterizeFloorAndSolid(
            CollisionNavMeshSoup soup,
            Vector3 bmin,
            float cell,
            int w,
            int d,
            float[] floorY,
            float[] solidTop,
            bool[] hasFloor,
            CoverBakeSettings settings,
            float bandMin = float.NegativeInfinity,
            float bandMax = float.PositiveInfinity)
        {
            float maxInclineRad = settings.MaximumInclineDegrees * (float)Math.PI / 180f;
            float minFloorDot = (float)Math.Cos(maxInclineRad);
            float wallDot = (float)Math.Sin(maxInclineRad);
            float wallThickness = Math.Max(cell * 1.25f, settings.DistanceFromGeometry);

            // Pass 1: walkable floor within this Y band (multi-deck support).
            for (int t = 0; t + 2 < soup.Tris.Length; t += 3)
            {
                if (!TryGetTri(soup, t, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 n))
                    continue;
                if (n.Y < minFloorDot)
                    continue;
                float triMaxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
                float triMinY = Math.Min(a.Y, Math.Min(b.Y, c.Y));
                if (triMaxY < bandMin || triMinY > bandMax)
                    continue;

                GetTriBoundsXZ(a, b, c, out float minX, out float maxX, out float minZ, out float maxZ);
                int x0 = Math.Max(0, (int)Math.Floor((minX - bmin.X) / cell));
                int x1 = Math.Min(w - 1, (int)Math.Floor((maxX - bmin.X) / cell));
                int z0 = Math.Max(0, (int)Math.Floor((minZ - bmin.Z) / cell));
                int z1 = Math.Min(d - 1, (int)Math.Floor((maxZ - bmin.Z) / cell));

                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float cx = bmin.X + (x + 0.5f) * cell;
                        float cz = bmin.Z + (z + 0.5f) * cell;
                        if (!PointInTriangleXZ(cx, cz, a, b, c))
                            continue;
                        float y = InterpolateY(cx, cz, a, b, c);
                        if (y < bandMin || y > bandMax)
                            continue;
                        int idx = z * w + x;
                        if (y < floorY[idx])
                        {
                            floorY[idx] = y;
                            hasFloor[idx] = true;
                        }
                    }
                }
            }

            // Pass 2: elevated tops + walls. Short props capped by MaximumObstacleHeight;
            // full-height walls still paint a standing-cover solid so AI can hug them.
            float maxObstH = settings.MaximumObstacleHeight;
            float wallBandPad = Math.Max(maxObstH, 4f);
            for (int t = 0; t + 2 < soup.Tris.Length; t += 3)
            {
                if (!TryGetTri(soup, t, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 n))
                    continue;

                float maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
                float minY = Math.Min(a.Y, Math.Min(b.Y, c.Y));
                bool isFloorish = n.Y >= minFloorDot;
                bool isCeiling = n.Y <= -minFloorDot;
                bool isWallish = Math.Abs(n.Y) <= wallDot;
                if (isCeiling)
                    continue;
                // Skip geometry that can't interact with this band's cover volume.
                if (maxY < bandMin || minY > bandMax + wallBandPad)
                    continue;

                GetTriBoundsXZ(a, b, c, out float minX, out float maxX, out float minZ, out float maxZ);
                float expand = isWallish ? wallThickness : 0f;
                int x0 = Math.Max(0, (int)Math.Floor((minX - expand - bmin.X) / cell));
                int x1 = Math.Min(w - 1, (int)Math.Floor((maxX + expand - bmin.X) / cell));
                int z0 = Math.Max(0, (int)Math.Floor((minZ - expand - bmin.Z) / cell));
                int z1 = Math.Min(d - 1, (int)Math.Floor((maxZ + expand - bmin.Z) / cell));

                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float cx = bmin.X + (x + 0.5f) * cell;
                        float cz = bmin.Z + (z + 0.5f) * cell;
                        int idx = z * w + x;
                        if (!hasFloor[idx])
                            continue;
                        float fy = floorY[idx];

                        if (isFloorish)
                        {
                            if (!PointInTriangleXZ(cx, cz, a, b, c))
                                continue;
                            float y = InterpolateY(cx, cz, a, b, c);
                            if (y >= fy + settings.MinimumHeight && y <= fy + maxObstH)
                                solidTop[idx] = Math.Max(solidTop[idx], y);
                            continue;
                        }

                        if (isWallish)
                        {
                            // Wall must reach near the floor; height may exceed maxObstH (tall walls).
                            if (maxY < fy + settings.MinimumHeight)
                                continue;
                            if (minY > fy + 0.35f)
                                continue;
                            bool inside = PointInTriangleXZ(cx, cz, a, b, c);
                            float edgeDist = DistPointToTriangleEdgesXZ(cx, cz, a, b, c);
                            if (!inside && edgeDist > wallThickness)
                                continue;
                            float recordedTop = Math.Min(maxY, fy + maxObstH);
                            solidTop[idx] = Math.Max(solidTop[idx], recordedTop);
                        }
                    }
                }
            }
        }

        static List<(Vector3 a, Vector3 b)> CollectNavBoundaryEdges(NavigationMesh navMesh)
        {
            var edges = new List<(Vector3 a, Vector3 b)>();
            if (navMesh?.Polygons == null || navMesh.Vertices == null)
                return edges;
            foreach (var poly in navMesh.Polygons)
            {
                if (poly.vertCount < 3)
                    continue;
                if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND)
                    continue;
                // Backstage is the alien's ceiling network, not playspace.
                if (((uint)poly.area.GetMarkupFlags() & (uint)NavigationMesh.NavMeshAreaTypeFlags.BackstageFlag) != 0)
                    continue;
                for (int i = 0; i < poly.vertCount; i++)
                {
                    ushort nei = poly.neis[i];
                    if (!(nei == 0 || (nei & DtExtLink) != 0))
                        continue;
                    edges.Add((navMesh.Vertices[poly.verts[i]], navMesh.Vertices[poly.verts[(i + 1) % poly.vertCount]]));
                }
            }
            return edges;
        }

        /// <summary>
        /// Distinct walkable Y levels from navmesh vert density peaks (1m bins). Returns disjoint
        /// (y, min, max) ranges so multi-deck bakes don't double-count the same obstacle.
        /// Falls back to one open band when no navmesh is available.
        /// </summary>
        static List<(float y, float min, float max)> CollectFloorBands(NavigationMesh navMesh, CoverBakeSettings settings)
        {
            if (navMesh?.Vertices == null || navMesh.Vertices.Length == 0)
                return new List<(float, float, float)> { (float.NaN, float.NegativeInfinity, float.PositiveInfinity) };

            int minBin = int.MaxValue, maxBin = int.MinValue;
            for (int i = 0; i < navMesh.Vertices.Length; i++)
            {
                int b = (int)Math.Floor(navMesh.Vertices[i].Y);
                if (b < minBin) minBin = b;
                if (b > maxBin) maxBin = b;
            }
            int n = maxBin - minBin + 1;
            var counts = new int[n];
            var sums = new double[n];
            for (int i = 0; i < navMesh.Vertices.Length; i++)
            {
                float y = navMesh.Vertices[i].Y;
                int b = (int)Math.Floor(y) - minBin;
                counts[b]++;
                sums[b] += y;
            }

            int maxCount = 0;
            for (int i = 0; i < n; i++)
                if (counts[i] > maxCount) maxCount = counts[i];
            int threshold = Math.Max(20, maxCount / 20); // ~5% of strongest deck

            var peakBins = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (counts[i] < threshold) continue;
                int left = i > 0 ? counts[i - 1] : 0;
                int right = i + 1 < n ? counts[i + 1] : 0;
                if (counts[i] >= left && counts[i] >= right)
                    peakBins.Add(i);
            }
            if (peakBins.Count == 0)
                return new List<(float, float, float)> { (float.NaN, float.NegativeInfinity, float.PositiveInfinity) };

            // Merge neighbouring peak bins (plateaus / adjacent strong floors).
            var centers = new List<float>();
            for (int p = 0; p < peakBins.Count; )
            {
                int start = peakBins[p];
                int end = start;
                int total = counts[start];
                double ySum = sums[start];
                p++;
                while (p < peakBins.Count && peakBins[p] <= end + 1)
                {
                    end = peakBins[p];
                    total += counts[end];
                    ySum += sums[end];
                    p++;
                }
                centers.Add((float)(ySum / total));
            }

            // Drop weaker peaks that sit within 2.0m of a stronger neighbour (already merged plateaus;
            // this catches secondary humps on stairs between decks).
            // Re-score by nearby bin mass.
            var keep = new List<float>();
            foreach (float c in centers)
            {
                if (keep.Count == 0 || c - keep[keep.Count - 1] > 2.0f)
                    keep.Add(c);
                else
                {
                    // Prefer the center with more local mass.
                    int bi = (int)Math.Floor(c) - minBin;
                    int bj = (int)Math.Floor(keep[keep.Count - 1]) - minBin;
                    int mi = bi >= 0 && bi < n ? counts[bi] : 0;
                    int mj = bj >= 0 && bj < n ? counts[bj] : 0;
                    if (mi > mj)
                        keep[keep.Count - 1] = c;
                }
            }

            var bands = new List<(float y, float min, float max)>(keep.Count);
            for (int i = 0; i < keep.Count; i++)
            {
                float lo = i == 0 ? keep[i] - 2.5f : (keep[i - 1] + keep[i]) * 0.5f;
                float hi = i == keep.Count - 1 ? keep[i] + 2.5f : (keep[i] + keep[i + 1]) * 0.5f;
                if (i > 0) lo += 0.01f;
                bands.Add((keep[i], lo, hi));
            }
            return bands;
        }

        static bool TryGetTri(CollisionNavMeshSoup soup, int t, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 n)
        {
            int i0 = soup.Tris[t], i1 = soup.Tris[t + 1], i2 = soup.Tris[t + 2];
            a = new Vector3(soup.Verts[i0 * 3], soup.Verts[i0 * 3 + 1], soup.Verts[i0 * 3 + 2]);
            b = new Vector3(soup.Verts[i1 * 3], soup.Verts[i1 * 3 + 1], soup.Verts[i1 * 3 + 2]);
            c = new Vector3(soup.Verts[i2 * 3], soup.Verts[i2 * 3 + 1], soup.Verts[i2 * 3 + 2]);
            Vector3 ab = b - a, ac = c - a;
            n = Vector3.Cross(ab, ac);
            float nLen = n.Length();
            if (nLen < 1e-8f)
                return false;
            n /= nLen;
            return true;
        }

        static void GetTriBoundsXZ(Vector3 a, Vector3 b, Vector3 c, out float minX, out float maxX, out float minZ, out float maxZ)
        {
            minX = Math.Min(a.X, Math.Min(b.X, c.X));
            maxX = Math.Max(a.X, Math.Max(b.X, c.X));
            minZ = Math.Min(a.Z, Math.Min(b.Z, c.Z));
            maxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z));
        }

        static float DistPointToTriangleEdgesXZ(float px, float pz, Vector3 a, Vector3 b, Vector3 c)
        {
            return Math.Min(DistPointSegXZ(px, pz, a, b),
                Math.Min(DistPointSegXZ(px, pz, b, c), DistPointSegXZ(px, pz, c, a)));
        }

        static float DistPointSegXZ(float px, float pz, Vector3 a, Vector3 b)
        {
            float abx = b.X - a.X, abz = b.Z - a.Z;
            float apx = px - a.X, apz = pz - a.Z;
            float ab2 = abx * abx + abz * abz;
            float t = ab2 > 1e-12f ? Math.Max(0f, Math.Min(1f, (apx * abx + apz * abz) / ab2)) : 0f;
            float qx = a.X + abx * t - px;
            float qz = a.Z + abz * t - pz;
            return (float)Math.Sqrt(qx * qx + qz * qz);
        }

        static bool PointInTriangleXZ(float px, float pz, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = SignXZ(px, pz, a, b);
            float d2 = SignXZ(px, pz, b, c);
            float d3 = SignXZ(px, pz, c, a);
            bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
            bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);
            return !(hasNeg && hasPos);
        }

        static float SignXZ(float px, float pz, Vector3 a, Vector3 b) =>
            (px - b.X) * (a.Z - b.Z) - (a.X - b.X) * (pz - b.Z);

        static float InterpolateY(float px, float pz, Vector3 a, Vector3 b, Vector3 c)
        {
            // Barycentric in XZ
            float v0x = b.X - a.X, v0z = b.Z - a.Z;
            float v1x = c.X - a.X, v1z = c.Z - a.Z;
            float v2x = px - a.X, v2z = pz - a.Z;
            float den = v0x * v1z - v1x * v0z;
            if (Math.Abs(den) < 1e-12f)
                return (a.Y + b.Y + c.Y) / 3f;
            float v = (v2x * v1z - v1x * v2z) / den;
            float w = (v0x * v2z - v2x * v0z) / den;
            float u = 1f - v - w;
            return u * a.Y + v * b.Y + w * c.Y;
        }

        static List<Cover.CoverSegment> MergeSamplesToSegments(List<CoverSample> samples, float cell, CoverBakeSettings settings, float offset)
        {
            // Group by dir + height class + run-length along the tangent axis.
            var segments = new List<Cover.CoverSegment>();
            var used = new bool[samples.Count];

            var byKey = new Dictionary<(int dir, int hBucket, int x, int z), int>();
            for (int i = 0; i < samples.Count; i++)
            {
                var s = samples[i];
                int hb = s.Height < 1.2f ? 0 : 1;
                byKey[(s.Dir, hb, s.X, s.Z)] = i;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                if (used[i])
                    continue;
                var seed = samples[i];
                int hb = seed.Height < 1.2f ? 0 : 1;
                used[i] = true;

                int tx = -DirZ[seed.Dir];
                int tz = DirX[seed.Dir];

                var run = new List<CoverSample> { seed };
                int cx = seed.X - tx, cz = seed.Z - tz;
                while (byKey.TryGetValue((seed.Dir, hb, cx, cz), out int j) && !used[j])
                {
                    used[j] = true;
                    run.Insert(0, samples[j]);
                    cx -= tx;
                    cz -= tz;
                }
                cx = seed.X + tx;
                cz = seed.Z + tz;
                while (byKey.TryGetValue((seed.Dir, hb, cx, cz), out int j) && !used[j])
                {
                    used[j] = true;
                    run.Add(samples[j]);
                    cx += tx;
                    cz += tz;
                }

                if (run.Count == 0)
                    continue;

                // Collapse multi-row depth: keep the band closest to the wall (toward -Normal).
                // Samples already sit on open floor; push endpoints to DistanceFromGeometry from the wall cell.
                Vector3 wallDir = -seed.Normal; // toward geometry
                Vector3 left = run[0].Position + wallDir * offset;
                Vector3 right = run[run.Count - 1].Position + wallDir * offset;
                Vector3 tangent = new Vector3(tx, 0, tz);
                if (tangent.LengthSquared() > 0)
                {
                    tangent = Vector3.Normalize(tangent);
                    left -= tangent * (cell * 0.5f);
                    right += tangent * (cell * 0.5f);
                }

                Vector3 delta = right - left;
                delta.Y = 0;
                float len = delta.Length();
                if (len < settings.MinimumLength)
                    continue;

                Vector3 normal = seed.Normal;
                normal.Y = 0;
                if (normal.LengthSquared() > 1e-8f)
                    normal = Vector3.Normalize(normal);
                else
                    normal = new Vector3(0, 0, 1);

                Vector3 along = Vector3.Normalize(delta);
                if (Vector3.Cross(along, normal).Y < 0)
                    (left, right) = (right, left);

                float avgY = 0;
                foreach (var s in run)
                    avgY += s.FloorY;
                avgY /= run.Count;
                left.Y = avgY;
                right.Y = avgY;

                segments.Add(new Cover.CoverSegment
                {
                    Left = left,
                    Right = right,
                    Normal = normal,
                    Height = seed.Height,
                    Flags = seed.Height < 1.2f ? 0x2000 : 0,
                    LeftCornerUID = 0,
                    RightCornerUID = 0,
                    LeftColinearUID = 0,
                    RightColinearUID = 0,
                    TraversalUID = 0
                });
            }

            return segments;
        }

        /// <summary>
        /// Restate the corner and colinear links in each segment's Flags field, the way retail does.
        /// </summary>
        /// <remarks>
        /// <para>Cross-tabulated over all 7,361 cover segments the game ships: 0x0008 is set on 2,044
        /// segments and every one of them has a left corner link (2,061 segments have one at all);
        /// 0x0010 is the same for right corners, 0x0020 for left colinear links and 0x0040 for right.
        /// Each is a 100% correlation. 0x2000 is set on 4,218 segments and every one is low cover.</para>
        /// <para>Retail also uses 0x0080/0x0100, 0x0200/0x0400 and 0x0800/0x1000 - the same left/right
        /// pairing, and only ever on segments that already carry the primary bit, so they refine a
        /// corner or colinear link (external versus internal, most likely, given the generator has
        /// angle thresholds for exactly that) rather than stating anything new. Which of the three
        /// states applies is not established, so they are left clear: guessing would be worse.</para>
        /// </remarks>
        static void ApplyLinkFlags(List<Cover.CoverSegment> segments, CoverBakeSettings settings)
        {
            if (!settings.WriteLinkFlags)
                return;

            var byUid = new Dictionary<int, Cover.CoverSegment>();
            foreach (Cover.CoverSegment s in segments)
                byUid[s.UID] = s;

            foreach (Cover.CoverSegment s in segments)
            {
                // Verified exact against the whole campaign with `diag coverbits all`: on the
                // SEGMENT word these five bits match retail in BOTH directions - 2,829
                // LEFT_CORNER_LINK bits against exactly the 2,829 segments carrying a left corner
                // UID, and the same for the other three - and no other bit is ever set there.
                int flags = s.Height < settings.LowHighDividingLine ? Cover.CoverSegment.IsLowCoverBit : 0;
                if (s.LeftCornerUID != 0)
                {
                    flags |= Cover.CoverSegment.LeftCornerLinkBit;
                    // A corner is one physical thing with one angle, so both segments naming it
                    // have to measure it the same way round. The link runs from the segment whose
                    // RIGHT end meets the other's LEFT end, so a LEFT link measures self->partner
                    // and a RIGHT link measures partner->self.
                    if (byUid.TryGetValue(s.LeftCornerUID, out Cover.CoverSegment lo))
                        flags |= CornerClassBits(ExternalCornerAngle(s.Normal, lo.Normal),
                                                 EndGap(s.Left, lo), settings,
                                                 Cover.CoverSegment.LeftCornerExternalBit,
                                                 Cover.CoverSegment.LeftCornerAutoBit);
                }
                if (s.RightCornerUID != 0)
                {
                    flags |= Cover.CoverSegment.RightCornerLinkBit;
                    if (byUid.TryGetValue(s.RightCornerUID, out Cover.CoverSegment ro))
                        flags |= CornerClassBits(ExternalCornerAngle(ro.Normal, s.Normal),
                                                 EndGap(s.Right, ro), settings,
                                                 Cover.CoverSegment.RightCornerExternalBit,
                                                 Cover.CoverSegment.RightCornerAutoBit);
                }
                if (s.LeftColinearUID != 0)
                {
                    flags |= Cover.CoverSegment.LeftColinearLinkBit;
                    if (byUid.TryGetValue(s.LeftColinearUID, out Cover.CoverSegment lc)
                        && IsAutoTransition(ExternalCornerAngle(s.Normal, lc.Normal), EndGap(s.Left, lc), settings))
                        flags |= Cover.CoverSegment.LeftColinearAutoBit;
                }
                if (s.RightColinearUID != 0)
                {
                    flags |= Cover.CoverSegment.RightColinearLinkBit;
                    if (byUid.TryGetValue(s.RightColinearUID, out Cover.CoverSegment rc)
                        && IsAutoTransition(ExternalCornerAngle(rc.Normal, s.Normal), EndGap(s.Right, rc), settings))
                        flags |= Cover.CoverSegment.RightColinearAutoBit;
                }
                s.Flags = flags;

                // The end-most slot carries its own end's link state, external and auto bits
                // included - retail sets LEFT_CORNER_EXTERNAL on 21.7% of slots and
                // LEFT_CORNER_AUTO on 8.6%, where we shipped zero. Same end-most rule as the
                // terminating bits, so the pct tie-break applies here too.
                if (s.OccupancySlots.Count == 0)
                    continue;
                Cover.CoverSegment.CoverSlot first = s.OccupancySlots[0];
                Cover.CoverSegment.CoverSlot last = s.OccupancySlots[s.OccupancySlots.Count - 1];
                const int leftEnd = Cover.CoverSegment.LeftCornerLinkBit
                                  | Cover.CoverSegment.LeftColinearLinkBit
                                  | Cover.CoverSegment.LeftCornerExternalBit
                                  | Cover.CoverSegment.LeftCornerAutoBit;
                const int rightEnd = Cover.CoverSegment.RightCornerLinkBit
                                   | Cover.CoverSegment.RightColinearLinkBit
                                   | Cover.CoverSegment.RightCornerExternalBit
                                   | Cover.CoverSegment.RightCornerAutoBit;
                if (first.PctAlongCoverSegment <= 0.5f)
                    first.Flags |= flags & leftEnd;
                if (last.PctAlongCoverSegment > 0.5f)
                    last.Flags |= flags & rightEnd;
            }
        }

        static void LinkCornersAndColinear(List<Cover.CoverSegment> segments, CoverBakeSettings settings)
        {
            for (int i = 0; i < segments.Count; i++)
                segments[i].UID = i + 1;

            float linkDist = settings.LinkDistanceForCornerOrAutoLink;
            float colinearDist = settings.LinkMaxDistanceForColinear;
            float minCorner = settings.LinkMinCornerAngle;
            float maxCorner = settings.LinkMaxCornerAngle;
            float maxH = settings.ColinearMergeMaxHeightDifference;
            // One slot per segment per side per kind, filled with the NEAREST candidate rather than
            // the first the double loop reaches - see Offer.
            var bestCorner = new Dictionary<(int uid, bool left), (int partner, float distSq)>();
            var bestColinear = new Dictionary<(int uid, bool left), (int partner, float distSq)>();

            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var a = segments[i];
                    var b = segments[j];
                    if (Math.Abs(a.Height - b.Height) > maxH)
                        continue;

                    TryLinkEnds(a, b, linkDist, colinearDist, minCorner, maxCorner, bestCorner, bestColinear, settings);
                    TryLinkEnds(b, a, linkDist, colinearDist, minCorner, maxCorner, bestCorner, bestColinear, settings);
                }
            }

            var byUidForLinks = new Dictionary<int, Cover.CoverSegment>();
            foreach (Cover.CoverSegment s in segments)
                byUidForLinks[s.UID] = s;

            foreach (KeyValuePair<(int uid, bool left), (int partner, float distSq)> kv in bestCorner)
            {
                if (!byUidForLinks.TryGetValue(kv.Key.uid, out Cover.CoverSegment s)) continue;
                if (kv.Key.left) s.LeftCornerUID = kv.Value.partner;
                else s.RightCornerUID = kv.Value.partner;
            }
            foreach (KeyValuePair<(int uid, bool left), (int partner, float distSq)> kv in bestColinear)
            {
                if (!byUidForLinks.TryGetValue(kv.Key.uid, out Cover.CoverSegment s)) continue;
                if (kv.Key.left) s.LeftColinearUID = kv.Value.partner;
                else s.RightColinearUID = kv.Value.partner;
            }

            SnapCornerEndsToCrossings(segments, settings);
        }

        /// <summary>
        /// Move each corner-linked endpoint onto the crossing of the two segments' lines, so the
        /// two segments actually meet the way retail's do.
        /// </summary>
        /// <remarks>
        /// Every target is computed before any endpoint moves. Snapping in place would shift a line
        /// that a later pair in the same pass still has to cross against, so the result would depend
        /// on segment order. The crossing lies on the segment's own infinite line, so this changes
        /// its length but never its direction or normal.
        /// </remarks>
        /// <summary>
        /// The signed corner angle the EXTERNAL and AUTO corner windows are measured over.
        /// </summary>
        /// <remarks>
        /// The turn from one normal to the other about +Y, offset by 180. The offset is what makes
        /// the configured corner angles land on the data - see
        /// <see cref="CoverBakeSettings.LinkMinExternalCornerAngle"/>.
        /// </remarks>
        static float ExternalCornerAngle(Vector3 na, Vector3 nb)
        {
            Vector3 a = SafeXZ(na), b = SafeXZ(nb);
            float cross = a.Z * b.X - a.X * b.Z;
            float dot = a.X * b.X + a.Z * b.Z;
            float deg = (float)(Math.Atan2(cross, dot) * 180.0 / Math.PI) + 180f;
            if (deg < 0f) deg += 360f;
            if (deg >= 360f) deg -= 360f;
            return deg;
        }

        static int CornerClassBits(float angle, float gap, CoverBakeSettings settings, int externalBit, int autoBit)
        {
            int bits = 0;
            if (angle >= settings.LinkMinExternalCornerAngle && angle <= settings.LinkMaxExternalCornerAngle)
                bits |= externalBit;
            if (IsAutoTransition(angle, gap, settings))
                bits |= autoBit;
            return bits;
        }

        /// <summary>
        /// Can an NPC slide straight from one segment to its linked neighbour?
        /// </summary>
        /// <remarks>
        /// <para>One rule serves both AUTO bits: the two must be CONTIGUOUS - their ends within
        /// <see cref="CoverBakeSettings.LinkDistanceForCornerOrAutoLink"/> - and the turn between
        /// them no more than <see cref="CoverBakeSettings.LinkMaxAutoTransitionAngle"/>. Each link
        /// kind then satisfies one half for free, which is why the two bits look like different
        /// rules: a corner link is only ever formed at 0.5 m so its gap always passes, and a
        /// colinear partner faces the same way so its angle is always about 180.</para>
        /// <para>The colinear half is measured exact over retail's 860 left colinear links
        /// (`diag covercolin all`): 0 of the 178 AUTO ones have a gap over 0.5 m and only 1 of the
        /// 682 non-AUTO ones is under it, with the worst set gap at 0.49 and the smallest clear gap
        /// at 0.47. Plain colinear links run out to 4.0 m (p90 3.58), so the two constants really do
        /// separate two different things. That single exception is most likely a blocked path -
        /// close enough to walk to, but not actually reachable - which we do not test.</para>
        /// </remarks>
        static bool IsAutoTransition(float angle, float gap, CoverBakeSettings settings)
        {
            return angle <= settings.LinkMaxAutoTransitionAngle
                && gap <= settings.LinkDistanceForCornerOrAutoLink;
        }

        /// <summary>Flat distance from an endpoint to the nearer end of a linked segment.</summary>
        static float EndGap(Vector3 end, Cover.CoverSegment other)
        {
            float toLeft = FlatDistance(end.X, end.Z, other.Left.X, other.Left.Z);
            float toRight = FlatDistance(end.X, end.Z, other.Right.X, other.Right.Z);
            return Math.Min(toLeft, toRight);
        }

        static void SnapCornerEndsToCrossings(List<Cover.CoverSegment> segments, CoverBakeSettings settings)
        {
            float maxSnap = settings.MaxCornerEndSnapDistance;
            if (maxSnap <= 0f)
                return;

            var byUid = new Dictionary<int, Cover.CoverSegment>();
            foreach (Cover.CoverSegment s in segments)
                byUid[s.UID] = s;

            var moves = new List<(Cover.CoverSegment seg, bool left, Vector3 to)>();
            foreach (Cover.CoverSegment s in segments)
            {
                for (int side = 0; side < 2; side++)
                {
                    bool left = side == 0;
                    int uid = left ? s.LeftCornerUID : s.RightCornerUID;
                    if (uid == 0 || !byUid.TryGetValue(uid, out Cover.CoverSegment o))
                        continue;
                    if (!CrossingXZ(s.Left, s.Right, o.Left, o.Right, out float cx, out float cz))
                        continue;

                    Vector3 end = left ? s.Left : s.Right;
                    Vector3 far = left ? s.Right : s.Left;
                    if (FlatDistance(end.X, end.Z, cx, cz) > maxSnap)
                        continue;
                    if (Math.Abs(s.Height - o.Height) > settings.CornerSquaringMaxHeightDifference)
                        continue;
                    // How far past its own end this drags the segment, as a fraction of its length.
                    // t is measured from the FAR end, so t = 1 is the segment's own end and the
                    // configured 1.7 allows a 70% extension.
                    float ownLen = FlatDistance(far.X, far.Z, end.X, end.Z);
                    if (ownLen > 1e-4f && settings.CornerSquaringMaxT > 0f
                        && FlatDistance(far.X, far.Z, cx, cz) / ownLen > settings.CornerSquaringMaxT)
                        continue;
                    // Area of the triangle the square adds or removes: the two ends and the
                    // crossing. A shallow crossing sweeps a large one even when both ends are
                    // close to it, which is the case a distance cap alone lets through.
                    if (settings.CornerSquaringMaxAreaToChopOff > 0f)
                    {
                        Vector3 oEnd = FlatDistance(o.Left.X, o.Left.Z, cx, cz)
                                       <= FlatDistance(o.Right.X, o.Right.Z, cx, cz) ? o.Left : o.Right;
                        float area = 0.5f * Math.Abs((end.X - cx) * (oEnd.Z - cz) - (oEnd.X - cx) * (end.Z - cz));
                        if (area > settings.CornerSquaringMaxAreaToChopOff)
                            continue;
                    }
                    // Never pull an end past its own far end, or so close to it that the segment
                    // collapses below the length we would have accepted in the first place.
                    if (FlatDistance(far.X, far.Z, cx, cz) < settings.MinimumLength)
                        continue;

                    moves.Add((s, left, new Vector3(cx, end.Y, cz)));
                }
            }

            foreach ((Cover.CoverSegment seg, bool left, Vector3 to) in moves)
            {
                if (left) seg.Left = to;
                else seg.Right = to;
            }
        }

        /// <summary>Crossing of the infinite lines through ab and cd in XZ. False when parallel.</summary>
        static bool CrossingXZ(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out float x, out float z)
        {
            x = 0f;
            z = 0f;
            float r1 = b.X - a.X, r2 = b.Z - a.Z;
            float s1 = d.X - c.X, s2 = d.Z - c.Z;
            float denom = r1 * s2 - r2 * s1;
            if (Math.Abs(denom) < 1e-6f)
                return false;
            float t = ((c.X - a.X) * s2 - (c.Z - a.Z) * s1) / denom;
            x = a.X + t * r1;
            z = a.Z + t * r2;
            return true;
        }

        static float FlatDistance(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        static void TryLinkEnds(
            Cover.CoverSegment a,
            Cover.CoverSegment b,
            float cornerDist,
            float colinearDist,
            float minCornerDeg,
            float maxCornerDeg,
            Dictionary<(int uid, bool left), (int partner, float distSq)> bestCorner,
            Dictionary<(int uid, bool left), (int partner, float distSq)> bestColinear,
            CoverBakeSettings settings)
        {
            LinkIfClose(a, true, b, true, cornerDist, colinearDist, minCornerDeg, maxCornerDeg, bestCorner, bestColinear, settings);
            LinkIfClose(a, true, b, false, cornerDist, colinearDist, minCornerDeg, maxCornerDeg, bestCorner, bestColinear, settings);
            LinkIfClose(a, false, b, true, cornerDist, colinearDist, minCornerDeg, maxCornerDeg, bestCorner, bestColinear, settings);
            LinkIfClose(a, false, b, false, cornerDist, colinearDist, minCornerDeg, maxCornerDeg, bestCorner, bestColinear, settings);
        }

        static void LinkIfClose(
            Cover.CoverSegment a,
            bool aLeft,
            Cover.CoverSegment b,
            bool bLeft,
            float cornerDist,
            float colinearDist,
            float minCornerDeg,
            float maxCornerDeg,
            Dictionary<(int uid, bool left), (int partner, float distSq)> bestCorner,
            Dictionary<(int uid, bool left), (int partner, float distSq)> bestColinear,
            CoverBakeSettings settings)
        {
            Vector3 ap = aLeft ? a.Left : a.Right;
            Vector3 bp = bLeft ? b.Left : b.Right;
            float distSq = Vector3.DistanceSquared(ap, bp);
            float maxDist = Math.Max(cornerDist, colinearDist);
            if (distSq > maxDist * maxDist)
                return;

            Vector3 na = SafeXZ(a.Normal);
            Vector3 nb = SafeXZ(b.Normal);
            float dot = Vector3.Dot(na, nb);

            // UNSIGNED, so the test gives the same answer from either segment. Retail names every
            // corner on both of them (100.0% of 4,088 links are reciprocal); a signed turn is
            // antisymmetric and could only ever admit one direction, which is why we shipped 13.6%
            // reciprocity and half the corner links retail has. Measured with `diag coverlinks`.
            float cornerAngle = AngleBetweenNormalsDeg(na, nb);

            bool corner = distSq <= cornerDist * cornerDist
                && cornerAngle >= minCornerDeg && cornerAngle <= maxCornerDeg;

            // NOT Math.Abs: a colinear partner has to face the SAME way, not merely lie on the same
            // line. Retail's 1,724 colinear links have 0.0 degrees between the two normals on 100%
            // of them (p90 3.6); with the absolute value, 66% of ours pointed at a segment facing
            // the opposite way - the far side of a wall, or across a corridor - median 177.4
            // degrees. That tells the game an NPC can slide from one to the other along the same
            // cover when the two face apart. Measured with `diag coverlinks`.
            bool colinear = distSq <= colinearDist * colinearDist
                && dot >= settings.LinkColinearDotProductThreshold;

            // Facing the same way along the same heading is not the same as lying on the same
            // LINE. Two walls either side of a doorway pass every test above.
            if (colinear && settings.LinkColinearCrossLengthThreshold > 0f)
            {
                float cross = DistPointToSegmentLineXZ(bp, a.Left, a.Right);
                if (cross > settings.LinkColinearCrossLengthThreshold)
                    colinear = false;
            }

            // NEAREST wins, not first-seen. A segment has one slot per side per kind, and taking
            // whichever candidate the double loop happened to reach first makes the answer depend on
            // segment order and breaks reciprocity: b can be a's colinear partner while a is not b's.
            // Retail's links are 99.5% reciprocal for colinear and 100.0% for corner; ours were
            // 80.7% on colinear. Nearest is symmetric, so both ends agree.
            if (corner)
                Offer(bestCorner, a.UID, aLeft, b.UID, distSq);
            else if (colinear)
                Offer(bestColinear, a.UID, aLeft, b.UID, distSq);
        }

        /// <summary>
        /// Perpendicular distance from a point to the INFINITE line through ab, in XZ. Unlike
        /// <see cref="DistPointToSegmentXZ"/> this does not clamp to the segment: a colinear
        /// partner legitimately lies beyond both ends, and only its sideways offset matters.
        /// </summary>
        static float DistPointToSegmentLineXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6f)
                return FlatDistance(p.X, p.Z, a.X, a.Z);
            return Math.Abs((p.X - a.X) * dz - (p.Z - a.Z) * dx) / len;
        }

        /// <summary>Keep the closest candidate for one segment's one side.</summary>
        static void Offer(Dictionary<(int uid, bool left), (int partner, float distSq)> best,
                          int uid, bool left, int partner, float distSq)
        {
            var key = (uid, left);
            if (!best.TryGetValue(key, out (int partner, float distSq) held) || distSq < held.distSq)
                best[key] = (partner, distSq);
        }

        /// <summary>
        /// Angle between two normals projected onto XZ, in degrees.
        /// </summary>
        /// <remarks>
        /// Kept in scalars on purpose. Writing to a field of a by-value <see cref="Vector3"/>
        /// parameter (<c>a.Y = 0</c>) miscompiles under .NET Framework's RyuJIT and faults with an
        /// AccessViolationException - which, being a corrupted-state exception, is not catchable
        /// and takes the process down. The same code runs fine on .NET 8, so it only bites the
        /// WinForms app. Do not "simplify" this back into Vector3 operations.
        /// </remarks>
        static float AngleBetweenNormalsDeg(Vector3 a, Vector3 b)
        {
            float ax = a.X, az = a.Z;
            float bx = b.X, bz = b.Z;

            float aLenSq = ax * ax + az * az;
            float bLenSq = bx * bx + bz * bz;
            if (aLenSq < 1e-8f || bLenSq < 1e-8f)
                return 0;

            float dot = (ax * bx + az * bz) / (float)Math.Sqrt(aLenSq * bLenSq);
            dot = Math.Max(-1f, Math.Min(1f, dot));
            return (float)(Math.Acos(dot) * 180.0 / Math.PI);
        }

        /// <summary>
        /// Drop segments that ended up with no occupancy slots, then renumber UIDs and rewrite the
        /// corner / colinear links through an old-to-new map so surviving links are preserved.
        /// </summary>
        static List<Cover.CoverSegment> RemoveUnoccupiableSegments(List<Cover.CoverSegment> segments)
        {
            var kept = new List<Cover.CoverSegment>(segments.Count);
            foreach (var s in segments)
                if (s.OccupancySlots.Count > 0) kept.Add(s);

            if (kept.Count == segments.Count)
                return segments;

            // Map the UIDs the linking pass used onto the UIDs we are about to assign.
            var remap = new Dictionary<int, int>(kept.Count);
            for (int i = 0; i < kept.Count; i++)
                remap[kept[i].UID] = i + 1;

            int Remap(int uid) => uid != 0 && remap.TryGetValue(uid, out int n) ? n : 0;

            foreach (var s in kept)
            {
                s.LeftCornerUID = Remap(s.LeftCornerUID);
                s.RightCornerUID = Remap(s.RightCornerUID);
                s.LeftColinearUID = Remap(s.LeftColinearUID);
                s.RightColinearUID = Remap(s.RightColinearUID);
            }
            for (int i = 0; i < kept.Count; i++)
                kept[i].UID = i + 1;

            return kept;
        }

        static void PlaceSlots(List<Cover.CoverSegment> segments, CoverBakeSettings settings, CoverAimSolver aimSolver = null)
        {
            float edgePad = settings.OccupancyMinSlotDistanceFromEdge;
            float spacing = settings.OccupancyDistanceBetweenSlots;

            foreach (var seg in segments)
            {
                Vector3 d = seg.Right - seg.Left;
                d.Y = 0;
                float len = d.Length();
                if (len < 1e-4f)
                    continue;

                var pcts = new List<float>();
                // Usable interior after edge padding on both ends.
                float usable = len - 2f * edgePad;
                if (usable <= 0f || len < spacing)
                {
                    pcts.Add(0.5f);
                }
                else
                {
                    int count = Math.Max(1, (int)Math.Floor(usable / spacing) + 1);
                    if (count == 1)
                    {
                        pcts.Add(0.5f);
                    }
                    else
                    {
                        // Slots from edgePad to len-edgePad at ~spacing.
                        for (int i = 0; i < count; i++)
                        {
                            float dist = edgePad + i * (usable / (count - 1));
                            pcts.Add(dist / len);
                        }
                    }
                }

                bool low = seg.Height < 1.2f;
                Vector3 tangent = d / len;
                Vector3 normal = SafeXZ(seg.Normal);

                foreach (float pct in pcts)
                {
                    var slot = new Cover.CoverSegment.CoverSlot
                    {
                        PctAlongCoverSegment = pct,
                        Flags = low ? Cover.CoverSegment.IsLowCoverBit : 0,
                        // Fallback cones, overwritten below when a solver is available.
                        ClearAimAnglesHorizontal = low ? 0x00FF : unchecked((short)0x800F),
                        ClearAimAnglesVertical = low ? 0x00690000 : unchecked(0x59000000)
                    };

                    if (aimSolver != null)
                    {
                        Vector3 slotPos = seg.Left + d * pct;
                        // Where the occupant actually stands: on the walkable side of the cover line.
                        // The face is written almost against the collision surface, so tracing sight
                        // lines from it puts the eye inside the wall and every ray comes back
                        // blocked - which was discarding nearly half the segments.
                        Vector3 stand = slotPos + normal * settings.SlotStandOffset;

                        if (aimSolver.HasNavMesh && !aimSolver.IsOnNavMesh(stand, settings.NavMeshProximity))
                        {
                            aimSolver.NoteSlotOffNavMesh();
                            continue;
                        }

                        // The solver wants the position on the COVER LINE; it steps off the line
                        // itself, differently for each aim model. `stand` is only the nav check.
                        if (!aimSolver.SolveSlot(slot, slotPos, normal, tangent,
                                                 len * pct, len * (1f - pct), seg.Height))
                            continue;

                        // The low bits of Flags say WHICH firing positions this slot actually has,
                        // so they have to follow the arcs the solver just found rather than being a
                        // constant per height class. Cross-tabbed over retail's 9,085 slots the
                        // implication is exact: 0x1 set means a live lean-left arc on 100% of 3,480
                        // slots, 0x2 a live lean-right on 100% of 3,459, 0x4 a live over-the-top on
                        // 100% of 4,984 - and 0x2000 marks low cover on exactly its 5,815. Writing
                        // 0x4001 for every high segment told the game they all lean LEFT.
                        int peek = PeekBits(slot, settings);
                        // A slot with no peek bit can be stood in but not fought from. Retail has
                        // none; see CoverBakeSettings.RequireUsableSlot.
                        if (peek == 0 && settings.RequireUsableSlot && settings.ApplyPeekThresholds)
                            continue;
                        slot.Flags = (low ? Cover.CoverSegment.IsLowCoverBit : 0) | peek;
                    }

                    seg.OccupancySlots.Add(slot);
                }

                // Retail keeps a slot on every segment it ships - 160 segments carry 175 slots on
                // SCI_Hub - so cover with a real obstacle behind it is not thrown away merely because
                // the sight-line sweep found nothing.
                if (seg.OccupancySlots.Count == 0 && settings.KeepSegmentsWithoutClearAim)
                {
                    Vector3 centre = seg.Left + d * 0.5f + normal * settings.SlotStandOffset;
                    if (aimSolver == null || !aimSolver.HasNavMesh || aimSolver.IsOnNavMesh(centre, settings.NavMeshProximity))
                    {
                        seg.OccupancySlots.Add(new Cover.CoverSegment.CoverSlot
                        {
                            PctAlongCoverSegment = 0.5f,
                            Flags = low ? Cover.CoverSegment.IsLowCoverBit : 0,
                            ClearAimAnglesHorizontal = low ? 0x00FF : unchecked((short)0x800F),
                            ClearAimAnglesVertical = low ? 0x00690000 : unchecked(0x59000000)
                        });
                    }
                }

                // After the fallback, so a segment kept without a clear aim still gets its bits.
                ApplyEndSlotBits(seg);
            }
        }

        /// <summary>
        /// The three peek bits of a slot's Flags word, from the arcs the solver found.
        /// </summary>
        /// <remarks>
        /// <para>The peek angles inform BITS, they do not gate the arc. Verified over
        /// retail's 9,085 slots with `diag coverbits all`: every peek bit implies its arc clears the
        /// threshold (100.0% on all three), no DEAD arc carries the bit (0 of 3,279 over-tops), and
        /// yet a live arc without its bit is ordinary - 21.8% of live lean-lefts, 22.1% of
        /// lean-rights, 14.2% of over-tops, at median widths of 60 to 108 degrees. So retail writes
        /// the full arc either way and only withholds the bit.</para>
        /// <para>This previously read bare liveness, which set the bit on every arc however narrow.
        /// The over-the-top test is "30 either side" - the arc has to reach BOTH ways, not merely be
        /// 60 degrees wide somewhere off to one side.</para>
        /// </remarks>
        static int PeekBits(Cover.CoverSegment.CoverSlot slot, CoverBakeSettings settings)
        {
            if (!settings.ApplyPeekThresholds)
                return 0;

            const float deg = (float)(Math.PI / 180.0);
            const float halfPi = (float)(Math.PI / 2.0);
            const float slack = 0.001f;
            float inner = settings.PeekInnerAngleSideDegrees * deg;
            float overHalf = settings.PeekArcOverDegrees * 0.5f * deg;
            float vert = settings.PeekVerticalArcDegrees * deg;

            int bits = 0;
            if (slot.LeftEdgeRightmostHorizontal + halfPi > 0.01f
                && slot.LeftEdgeRightmostHorizontal >= inner - slack
                && slot.LeftEdgeTopVertical - slot.LeftEdgeBottomVertical >= vert - slack)
                bits |= Cover.CoverSegment.LeftPeekBit;

            if (halfPi - slot.RightEdgeLeftmostHorizontal > 0.01f
                && slot.RightEdgeLeftmostHorizontal <= -inner + slack
                && slot.RightEdgeTopVertical - slot.RightEdgeBottomVertical >= vert - slack)
                bits |= Cover.CoverSegment.RightPeekBit;

            if (slot.OverTopRightmostHorizontal - slot.OverTopLeftmostHorizontal > 0.01f
                && slot.OverTopLeftmostHorizontal <= -overHalf + slack
                && slot.OverTopRightmostHorizontal >= overHalf - slack
                && slot.OverTopTopVertical - slot.OverTopBottomVertical >= vert - slack)
                bits |= Cover.CoverSegment.OverPeekBit;

            return bits;
        }

        /// <summary>
        /// Mark the slot at each end of a segment with that end's terminating-edge and link bits.
        /// </summary>
        /// <remarks>
        /// <para>Measured over retail's 9,085 slots with `diag coverbits all`. Only the END-MOST
        /// slot ever carries one: IS_TERMINATING_LEFT_EDGE implies "leftmost slot" on 100.0% of
        /// its 5,600, and IS_TERMINATING_RIGHT_EDGE implies "rightmost slot AND pct &gt; 0.5" on
        /// 100.0% of its 938. The pct tie-break is what makes the two counts so lopsided - a
        /// single-slot segment puts its slot at exactly 0.5, which can take the LEFT bit but never
        /// the right.</para>
        /// <para>Necessary but not sufficient: 76.1% of leftmost slots and 82.0% of qualifying
        /// rightmost ones actually carry the bit, and no combination of the link fields, the peek
        /// bits or arc liveness explains the rest - the closest, "no left link at all", overlaps
        /// only 64.6%. The residual condition is undecoded, and is most likely geometric: whether
        /// the cover really ENDS there rather than being continued by something we do not model.
        /// Writing the necessary condition still beats what this replaced, which set the left bit
        /// on 100% of slots against retail's 61.6% and never set the right one at all.</para>
        /// <para>The four LINK bits follow the same end-most-slot rule, and there they are all but
        /// exact: a slot's RIGHT_CORNER_LINK bit is "last slot, pct &gt; 0.5, and the segment has a
        /// right corner link" on 100.0% of its 412 in BOTH directions, right colinear 100.0/99.4%,
        /// and the two left bits 100.0/97.3% and 100.0/97.7%. We wrote these only on the segment
        /// word and left the slot word at zero, against retail's 28.6% and 9.7%.</para>
        /// </remarks>
        static void ApplyEndSlotBits(Cover.CoverSegment seg)
        {
            if (seg.OccupancySlots.Count == 0)
                return;
            Cover.CoverSegment.CoverSlot first = seg.OccupancySlots[0];
            Cover.CoverSegment.CoverSlot last = seg.OccupancySlots[seg.OccupancySlots.Count - 1];

            if (first.PctAlongCoverSegment <= 0.5f)
                first.Flags |= Cover.CoverSegment.IsTerminatingLeftEdgeBit;
            if (last.PctAlongCoverSegment > 0.5f)
                last.Flags |= Cover.CoverSegment.IsTerminatingRightEdgeBit;
        }

        static List<Cover.CoverSegment> FilterNearNavBoundary(
            List<Cover.CoverSegment> segments,
            NavigationMesh navMesh,
            float maxDist)
        {
            var edges = CollectNavBoundaryEdges(navMesh);
            if (edges.Count == 0)
                return segments;
            var keep = new List<Cover.CoverSegment>(segments.Count);
            foreach (var seg in segments)
            {
                Vector3 mid = (seg.Left + seg.Right) * 0.5f;
                float best = float.MaxValue;
                for (int e = 0; e < edges.Count; e++)
                {
                    Vector3 a = edges[e].a, b = edges[e].b;
                    // Allow floor-snap / ramp variance between cover Y and nav edge verts.
                    float edgeMinY = Math.Min(a.Y, b.Y) - 1.5f;
                    float edgeMaxY = Math.Max(a.Y, b.Y) + 1.5f;
                    if (mid.Y < edgeMinY || mid.Y > edgeMaxY)
                        continue;
                    float minX = Math.Min(a.X, b.X) - maxDist, maxX = Math.Max(a.X, b.X) + maxDist;
                    float minZ = Math.Min(a.Z, b.Z) - maxDist, maxZ = Math.Max(a.Z, b.Z) + maxDist;
                    if (mid.X < minX || mid.X > maxX || mid.Z < minZ || mid.Z > maxZ)
                        continue;
                    // Use closest of mid/ends so long segments aren't dropped when mid sits inward.
                    float d = DistPointToSegmentXZ(mid, a, b);
                    d = Math.Min(d, DistPointToSegmentXZ(seg.Left, a, b));
                    d = Math.Min(d, DistPointToSegmentXZ(seg.Right, a, b));
                    if (d < best) best = d;
                    if (best <= maxDist)
                        break;
                }
                if (best <= maxDist)
                    keep.Add(seg);
            }
            return keep;
        }

        static List<Cover.CoverSegment> FilterNearNavMesh(List<Cover.CoverSegment> segments, NavigationMesh nav, float radius)
        {
            // Lightweight: keep segment if either endpoint is within radius of any nav vert.
            var verts = new List<Vector3>();
            try
            {
                CollectNavVerts(nav, verts);
            }
            catch
            {
                return segments;
            }
            if (verts.Count == 0)
                return segments;

            float r2 = radius * radius;
            var keep = new List<Cover.CoverSegment>();
            foreach (var seg in segments)
            {
                if (NearAny(seg.Left, verts, r2) || NearAny(seg.Right, verts, r2) || NearAny((seg.Left + seg.Right) * 0.5f, verts, r2))
                    keep.Add(seg);
            }
            return keep;
        }

        static void CollectNavVerts(NavigationMesh nav, List<Vector3> verts)
        {
            if (nav?.Vertices == null)
                return;
            foreach (var v in nav.Vertices)
                verts.Add(v);
        }

        static bool NearAny(Vector3 p, List<Vector3> verts, float r2)
        {
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 d = verts[i] - p;
                d.Y *= 0.25f; // looser vertical
                if (d.LengthSquared() <= r2)
                    return true;
            }
            return false;
        }

        static void BuildTraversalGrid(Cover cover, float unitSize)
        {
            if (cover.Entries.Count == 0)
            {
                cover.Traversal.UnitSize = unitSize;
                cover.Traversal.XCells = 0;
                cover.Traversal.ZCells = 0;
                return;
            }

            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            foreach (var s in cover.Entries)
            {
                minX = Math.Min(minX, Math.Min(s.Left.X, s.Right.X));
                maxX = Math.Max(maxX, Math.Max(s.Left.X, s.Right.X));
                minZ = Math.Min(minZ, Math.Min(s.Left.Z, s.Right.Z));
                maxZ = Math.Max(maxZ, Math.Max(s.Left.Z, s.Right.Z));
            }
            minX -= unitSize;
            minZ -= unitSize;
            maxX += unitSize;
            maxZ += unitSize;

            short xCells = (short)Math.Max(1, (int)Math.Ceiling((maxX - minX) / unitSize));
            short zCells = (short)Math.Max(1, (int)Math.Ceiling((maxZ - minZ) / unitSize));
            var grid = new Cover.TraversalGrid
            {
                MinX = minX,
                MinZ = minZ,
                UnitSize = unitSize,
                XCells = xCells,
                ZCells = zCells
            };
            int cellCount = xCells * zCells;
            for (int i = 0; i < cellCount; i++)
                grid.Cells.Add(new List<short>());

            foreach (var s in cover.Entries)
            {
                Vector3 mid = (s.Left + s.Right) * 0.5f;
                int cx = (int)Math.Floor((mid.X - minX) / unitSize);
                int cz = (int)Math.Floor((mid.Z - minZ) / unitSize);
                cx = Math.Max(0, Math.Min(xCells - 1, cx));
                cz = Math.Max(0, Math.Min(zCells - 1, cz));
                // Also stamp endpoints
                Stamp(grid, s.Left, (short)s.UID);
                Stamp(grid, s.Right, (short)s.UID);
                Stamp(grid, mid, (short)s.UID);
            }

            cover.Traversal = grid;
        }

        static void Stamp(Cover.TraversalGrid grid, Vector3 p, short uid)
        {
            int cx = (int)Math.Floor((p.X - grid.MinX) / grid.UnitSize);
            int cz = (int)Math.Floor((p.Z - grid.MinZ) / grid.UnitSize);
            if (cx < 0 || cz < 0 || cx >= grid.XCells || cz >= grid.ZCells)
                return;
            var cell = grid.Cells[cz * grid.XCells + cx];
            if (!cell.Contains(uid))
                cell.Add(uid);
        }

        static Cover CloneCover(Cover src)
        {
            var c = CreateEmptyCover();
            foreach (var s in src.Entries)
            {
                var ns = new Cover.CoverSegment
                {
                    Left = s.Left,
                    Right = s.Right,
                    Normal = s.Normal,
                    Height = s.Height,
                    Flags = s.Flags,
                    UID = s.UID,
                    LeftCornerUID = s.LeftCornerUID,
                    RightCornerUID = s.RightCornerUID,
                    LeftColinearUID = s.LeftColinearUID,
                    RightColinearUID = s.RightColinearUID,
                    TraversalUID = s.TraversalUID,
                    CathodeIndex = s.CathodeIndex,
                    CathodeEnt = s.CathodeEnt,
                    CathodeParent = s.CathodeParent
                };
                foreach (var slot in s.OccupancySlots)
                {
                    ns.OccupancySlots.Add(new Cover.CoverSegment.CoverSlot
                    {
                        UID = slot.UID,
                        PctAlongCoverSegment = slot.PctAlongCoverSegment,
                        Flags = slot.Flags,
                        ClearAimAnglesHorizontal = slot.ClearAimAnglesHorizontal,
                        ClearAimAnglesVertical = slot.ClearAimAnglesVertical
                    });
                }
                c.Entries.Add(ns);
            }
            c.Traversal.XCells = src.Traversal.XCells;
            c.Traversal.ZCells = src.Traversal.ZCells;
            c.Traversal.MinX = src.Traversal.MinX;
            c.Traversal.MinZ = src.Traversal.MinZ;
            c.Traversal.UnitSize = src.Traversal.UnitSize;
            foreach (var cell in src.Traversal.Cells)
                c.Traversal.Cells.Add(new List<short>(cell));
            return c;
        }
    }
}
#endif
