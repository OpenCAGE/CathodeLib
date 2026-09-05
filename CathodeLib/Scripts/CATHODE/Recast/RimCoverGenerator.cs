#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;
using NanoRT;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Cover generation driven by the navmesh rim, built to match what retail's own COVER files
    /// measurably are rather than to rediscover them from a rasterised world.
    /// </summary>
    /// <remarks>
    /// <para>Measured across SCI_Hub, Tech_Hub, SCI_HospitalLower and BSP_Torrens, every shipped
    /// cover segment lies on the navmesh rim, displaced 0.2925 m OUTWARD - onto the wall side.
    /// The navmesh is eroded by the walkable radius (0.3125), so that puts the cover face 0.02 m off
    /// the collision surface, and pushing a segment 0.3 m back along its own normal lands it on the
    /// mesh for 79-85% of them. The spread is tiny: the 10th and 90th percentiles of the distance to
    /// the rim are 0.29 and 0.31.</para>
    /// <para>Two conditions separate rim that retail covers from rim it does not, and both hold on
    /// every level measured. Cover never forms where the approach side is crouch or deep-crouch
    /// floor (0.0% of 73 crouch samples on SCI_Hub, 0.0% of 361 and 1.2% of 341 on Tech_Hub), and it
    /// grows steadily with how much open floor stands in front of it - from 3-5% of samples with
    /// under 3 m2 within 2.5 m to 41-58% of those with over 15 m2. A corridor wall is not cover; the
    /// same wall facing a room is.</para>
    /// <para>Obstacle height is a weaker signal than it first looks. SCI_Hub strongly prefers
    /// waist-high (~1 m) and full-wall (~3.5 m) obstacles and disfavours everything between, but
    /// Tech_Hub is far flatter, so only the floor - an obstacle at least
    /// <see cref="CoverBakeSettings.MinimumHeight"/> tall - is enforced here.</para>
    /// </remarks>
    public static class RimCoverGenerator
    {
        /// <summary>
        /// A run of rim, already split where the geometry behind it stops being one piece of cover.
        /// </summary>
        private struct Span
        {
            /// <summary>Distance from the rim to the cover face - the flush stage's answer, or RimOffset.</summary>
            public float Offset;
            public Vector3 Start;
            public Vector3 End;
            public Vector3 Inward;   // unit, XZ, toward the walkable side
            public float Height;     // classified cover height
        }

        /// <summary>
        /// Measurement hook: when set, decides every rim sample (point on the rim, inward normal)
        /// in place of the gates and the learned selector, so a diag can feed retail's own answer
        /// through the segmentation pipeline and read its ceiling. Never set in production.
        /// </summary>
        public static Func<Vector3, Vector3, bool> OracleAccept;

        public static List<Cover.CoverSegment> Generate(
            CollisionNavMeshSoup soup,
            NavigationMesh nav,
            CoverBakeSettings settings,
            out string message)
        {
            var segments = new List<Cover.CoverSegment>();
            message = "rim cover: no navmesh";
            if (nav?.Polygons == null || nav.Vertices == null || nav.Vertices.Length == 0)
                return segments;

            // The voxel field is only needed when the obstacle height is read from it. With the ray
            // measurement on, rasterising the whole soup into it is pure cost.
            var obstacles = settings.UseRayObstacleTop ? null : new ObstacleField(soup, settings);
            var floor = new NavFloorGrid(nav);
            CoverGbdtModel learned = LearnedCover.TryLoad(settings);
            var depth = learned != null || settings.UseRayObstacleTop
                     || settings.MinObstacleDepth > 0f
                     || settings.MinObstacleDepthHighCover > 0f
                     || settings.MaxWallEndDistance > 0f
                     || settings.MinFrontClearance > 0f
                     || settings.MinFiringArcDegrees > 0f
                ? new DepthProbe(soup) : null;

            List<RimEdge> rim = CollectStandingRim(nav, new NavLoops(nav), settings);
            List<List<RimEdge>> runs = ChainRuns(rim, settings.RimRunMaxTurnDegrees);

            int rejectedNoObstacle = 0, rejectedCramped = 0, rejectedShort = 0;
            var spans = new List<Span>();
            foreach (List<RimEdge> run in runs)
                CollectSpans(run, obstacles, floor, depth, settings, spans, ref rejectedNoObstacle, ref rejectedCramped, learned);

            foreach (Span span in spans)
            {
                Vector3 delta = span.End - span.Start; delta.Y = 0;
                float length = delta.Length();
                // MinimumLength is not applied here: fragments get stitched back into runs by the
                // colinear merge afterwards, and culling them first destroys the run.
                if (length < 0.2f) { rejectedShort++; continue; }

                // Retail writes the face on the wall side of the rim.
                Vector3 outward = -span.Inward;
                float faceOffset = span.Offset > 0f ? span.Offset : settings.RimOffset;
                Vector3 left = span.Start + outward * faceOffset;
                Vector3 right = span.End + outward * faceOffset;

                // Left/Right are ordered so that the normal is the segment's left-hand side, which is
                // the winding retail uses.
                if (Vector3.Cross(Vector3.Normalize(delta), span.Inward).Y < 0)
                    (left, right) = (right, left);

                segments.Add(new Cover.CoverSegment
                {
                    Left = left,
                    Right = right,
                    Normal = span.Inward,
                    Height = span.Height,
                    Flags = span.Height < settings.LowHighDividingLine ? 0x2000 : 0,
                });
            }

            message = $"rim cover: rim={rim.Count} runs={runs.Count} spans={spans.Count} segs={segments.Count} " +
                      $"(dropped {rejectedNoObstacle} no-obstacle, {rejectedCramped} cramped, {rejectedShort} short)";
            return segments;
        }

        #region rim collection

        private struct RimEdge
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 Inward;
            public float Length;
            /// <summary>Metres round the closed navmesh loop this edge belongs to.</summary>
            public float LoopPerimeter;
            /// <summary>The floor lies outside the loop: an obstacle you can walk around, not a room shell.</summary>
            public bool LoopIsHole;
        }

        /// <summary>
        /// The navmesh rim chained into closed loops, so a rim edge can be asked what it goes round.
        /// Built over every ground polygon, not just the standing ones: a loop broken by a crouch
        /// polygon would report a fragment's length instead of the obstacle's.
        /// </summary>
        private sealed class NavLoops
        {
            readonly Dictionary<long, int> _ofEdge = new Dictionary<long, int>();
            readonly List<float> _perimeter = new List<float>();
            readonly List<bool> _isHole = new List<bool>();

            static long EdgeKey(int a, int b) { return ((long)a << 32) ^ (uint)b; }

            public NavLoops(NavigationMesh nav)
            {
                var next = new Dictionary<int, List<int>>();
                var centreOf = new Dictionary<long, Vector3>();
                for (int p = 0; p < nav.Polygons.Length; p++)
                {
                    NavigationMesh.dtPoly poly = nav.Polygons[p];
                    if (poly.vertCount < 3 || poly.verts == null || poly.neis == null) continue;
                    if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND) continue;
                    if (((uint)poly.area.GetMarkupFlags() & (uint)NavigationMesh.NavMeshAreaTypeFlags.BackstageFlag) != 0) continue;
                    Vector3 centre = Vector3.Zero;
                    for (int i = 0; i < poly.vertCount; i++) centre += nav.Vertices[poly.verts[i]];
                    centre /= poly.vertCount;
                    for (int i = 0; i < poly.vertCount; i++)
                    {
                        ushort nei = poly.neis[i];
                        if (!(nei == 0 || (nei & 0x8000) != 0)) continue;
                        int a = poly.verts[i], b = poly.verts[(i + 1) % poly.vertCount];
                        if (a == b) continue;
                        if (!next.TryGetValue(a, out List<int> l)) next[a] = l = new List<int>();
                        l.Add(b);
                        centreOf[EdgeKey(a, b)] = centre;
                    }
                }

                var used = new HashSet<long>();
                foreach (int start in next.Keys)
                    foreach (int firstTo in next[start])
                    {
                        if (used.Contains(EdgeKey(start, firstTo))) continue;
                        var loop = new List<int> { start };
                        int cur = start, nxt = firstTo;
                        for (int guard = 0; guard < 100000; guard++)
                        {
                            long k = EdgeKey(cur, nxt);
                            if (used.Contains(k)) break;
                            used.Add(k);
                            loop.Add(nxt);
                            if (nxt == start) break;
                            if (!next.TryGetValue(nxt, out List<int> outs) || outs.Count == 0) break;
                            int pick = -1;
                            foreach (int o in outs) if (!used.Contains(EdgeKey(nxt, o))) { pick = o; break; }
                            if (pick < 0) break;
                            cur = nxt; nxt = pick;
                        }
                        if (loop.Count >= 4) Record(nav, loop, centreOf);
                    }
            }

            void Record(NavigationMesh nav, List<int> loop, Dictionary<long, Vector3> centreOf)
            {
                int id = _perimeter.Count, n = loop.Count - 1;
                float perim = 0f;
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = nav.Vertices[loop[i]], b = nav.Vertices[loop[i + 1]];
                    perim += (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
                    _ofEdge[EdgeKey(loop[i], loop[i + 1])] = id;
                }
                // The floor is on the side of the owning polygon's centre; if that centre falls
                // outside the loop then the loop goes round an obstacle rather than round a room.
                bool hole = false;
                if (centreOf.TryGetValue(EdgeKey(loop[0], loop[1]), out Vector3 c))
                {
                    bool inside = false;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                    {
                        Vector3 vi = nav.Vertices[loop[i]], vj = nav.Vertices[loop[j]];
                        if (((vi.Z > c.Z) != (vj.Z > c.Z)) && (c.X < (vj.X - vi.X) * (c.Z - vi.Z) / (vj.Z - vi.Z) + vi.X)) inside = !inside;
                    }
                    hole = !inside;
                }
                _perimeter.Add(perim);
                _isHole.Add(hole);
            }

            public void Lookup(int a, int b, out float perimeter, out bool isHole)
            {
                if (_ofEdge.TryGetValue(EdgeKey(a, b), out int id)) { perimeter = _perimeter[id]; isHole = _isHole[id]; return; }
                perimeter = 0f; isHole = false;
            }
        }

        /// <summary>
        /// Rim edges of standing-height floor. Crouch and deep-crouch polygons are excluded outright:
        /// retail places no cover against them at all.
        /// </summary>
        private static List<RimEdge> CollectStandingRim(NavigationMesh nav, NavLoops loops, CoverBakeSettings settings)
        {
            var rim = new List<RimEdge>();
            for (int p = 0; p < nav.Polygons.Length; p++)
            {
                NavigationMesh.dtPoly poly = nav.Polygons[p];
                if (poly.vertCount < 3 || poly.verts == null || poly.neis == null)
                    continue;
                if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND)
                    continue;
                if (((uint)poly.area.GetMarkupFlags() & (uint)NavigationMesh.NavMeshAreaTypeFlags.BackstageFlag) != 0)
                    continue;
                if (settings.RequireStandingApproach &&
                    poly.area.GetHeightLimitedAmount() != NavigationMesh.AreaHeight.Standing)
                    continue;

                Vector3 centre = Vector3.Zero;
                for (int i = 0; i < poly.vertCount; i++)
                    centre += nav.Vertices[poly.verts[i]];
                centre /= poly.vertCount;

                for (int i = 0; i < poly.vertCount; i++)
                {
                    ushort nei = poly.neis[i];
                    if (!(nei == 0 || (nei & 0x8000) != 0))
                        continue;

                    Vector3 a = nav.Vertices[poly.verts[i]];
                    Vector3 b = nav.Vertices[poly.verts[(i + 1) % poly.vertCount]];
                    var along = new Vector3(b.X - a.X, 0f, b.Z - a.Z);
                    float length = along.Length();
                    if (length < 1e-4f)
                        continue;

                    var inward = Vector3.Normalize(new Vector3(-along.Z, 0f, along.X));
                    Vector3 mid = (a + b) * 0.5f;
                    if (Vector3.Dot(new Vector3(centre.X - mid.X, 0f, centre.Z - mid.Z), inward) < 0f)
                        inward = -inward;

                    loops.Lookup(poly.verts[i], poly.verts[(i + 1) % poly.vertCount], out float loopPerimeter, out bool loopIsHole);
                    rim.Add(new RimEdge { A = a, B = b, Inward = inward, Length = length, LoopPerimeter = loopPerimeter, LoopIsHole = loopIsHole });
                }
            }
            return rim;
        }

        /// <summary>
        /// Chain rim edges end to end while they keep roughly the same heading. Edges are indexed by
        /// BOTH endpoints and flipped as needed: neighbouring navmesh polygons hand their shared
        /// boundary out in opposite directions often enough that matching starts against ends alone
        /// leaves almost every edge in a run of its own.
        /// </summary>
        private static List<List<RimEdge>> ChainRuns(List<RimEdge> rim, float maxTurnDegrees)
        {
            var runs = new List<List<RimEdge>>();
            if (rim.Count == 0)
                return runs;

            float minDot = (float)Math.Cos(maxTurnDegrees * Math.PI / 180.0);

            // Binned by position rather than matched exactly: adjacent polygons meet at T-junctions
            // and their boundary vertices do not always coincide to the bit.
            var byPoint = new Dictionary<long, List<int>>();
            for (int i = 0; i < rim.Count; i++)
            {
                Add(byPoint, CellKey(rim[i].A), i);
                Add(byPoint, CellKey(rim[i].B), i);
            }

            var used = new bool[rim.Count];
            for (int i = 0; i < rim.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                var run = new List<RimEdge> { rim[i] };

                Extend(run, rim, byPoint, used, minDot, forward: true);
                Extend(run, rim, byPoint, used, minDot, forward: false);
                runs.Add(run);
            }
            return runs;
        }

        private static void Add(Dictionary<long, List<int>> map, long key, int value)
        {
            if (!map.TryGetValue(key, out List<int> l)) map[key] = l = new List<int>();
            l.Add(value);
        }

        /// <summary>
        /// Grow a run off one of its ends, taking the straightest continuation whose inward normal
        /// still agrees. Edges are reversed when their far endpoint is the one that joins.
        /// </summary>
        private static void Extend(List<RimEdge> run, List<RimEdge> rim, Dictionary<long, List<int>> byPoint,
                                   bool[] used, float minDot, bool forward)
        {
            while (true)
            {
                RimEdge tip = forward ? run[run.Count - 1] : run[0];
                Vector3 tipPoint = forward ? tip.B : tip.A;
                Vector3 tipDir = forward ? Direction(tip) : -Direction(tip);

                int best = -1;
                bool bestFlip = false;
                float bestDot = minDot;
                foreach (int c in Nearby(byPoint, tipPoint))
                {
                    if (used[c]) continue;
                    RimEdge e = rim[c];
                    if (Vector3.Dot(e.Inward, tip.Inward) < minDot) continue;

                    float da = Vector3.Distance(e.A, tipPoint);
                    float db = Vector3.Distance(e.B, tipPoint);
                    if (Math.Min(da, db) > JoinTolerance) continue;

                    bool joinsAtA = da <= db;
                    Vector3 dir = joinsAtA ? Direction(e) : -Direction(e);
                    float dot = Vector3.Dot(dir, tipDir);
                    if (dot >= bestDot) { bestDot = dot; best = c; bestFlip = !joinsAtA; }
                }
                if (best < 0) return;

                used[best] = true;
                RimEdge next = rim[best];
                if (bestFlip)
                    next = new RimEdge { A = next.B, B = next.A, Inward = next.Inward, Length = next.Length, LoopPerimeter = next.LoopPerimeter, LoopIsHole = next.LoopIsHole };
                if (forward)
                    run.Add(next);
                else
                    run.Insert(0, new RimEdge { A = next.B, B = next.A, Inward = next.Inward, Length = next.Length, LoopPerimeter = next.LoopPerimeter, LoopIsHole = next.LoopIsHole });
            }
        }

        private static Vector3 Direction(RimEdge e)
        {
            var d = new Vector3(e.B.X - e.A.X, 0f, e.B.Z - e.A.Z);
            float len = d.Length();
            return len < 1e-6f ? new Vector3(0, 0, 1) : d / len;
        }

        private const float JoinTolerance = 0.12f;
        private const float JoinCell = 0.25f;

        private static long CellKey(Vector3 p)
        {
            long x = (long)Math.Floor(p.X / JoinCell);
            long y = (long)Math.Floor(p.Y / 1.0f);
            long z = (long)Math.Floor(p.Z / JoinCell);
            return (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
        }

        /// <summary>Edges with an endpoint in or beside the cell holding <paramref name="p"/>.</summary>
        private static IEnumerable<int> Nearby(Dictionary<long, List<int>> byPoint, Vector3 p)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var probe = new Vector3(p.X + dx * JoinCell, p.Y + dy * 1.0f, p.Z + dz * JoinCell);
                        if (byPoint.TryGetValue(CellKey(probe), out List<int> l))
                            foreach (int i in l) yield return i;
                    }
        }

        #endregion

        #region span building

        /// <summary>
        /// Walk a run, testing every step, and cut it into spans of continuous usable cover. A span
        /// ends where the obstacle stops, where the room in front runs out, where the cover changes
        /// height class, or where the rim turns.
        /// </summary>
        /// <summary>Rim on a room shell rather than on something an NPC can walk around.</summary>
        private static bool IsShell(RimEdge edge, CoverBakeSettings settings)
        {
            return !edge.LoopIsHole || edge.LoopPerimeter >= settings.ShellLoopPerimeter;
        }

        private static void CollectSpans(
            List<RimEdge> run,
            ObstacleField obstacles,
            NavFloorGrid floor,
            DepthProbe depth,
            CoverBakeSettings settings,
            List<Span> spans,
            ref int rejectedNoObstacle,
            ref int rejectedCramped,
            CoverGbdtModel learned = null)
        {
            float step = Math.Max(0.05f, settings.RimSampleStep);
            float runLength = 0f;
            foreach (RimEdge re in run) runLength += re.Length;
            float runPos = 0f;
            float learnedThreshold = learned == null ? 0f : (settings.LearnedSelectorThreshold > 0f ? settings.LearnedSelectorThreshold : learned.Threshold);

            // Sample the whole run first. Testing sample by sample and cutting on every failure turns
            // one wall into a row of dashes - the probes flicker where a doorframe, a pipe or a
            // rasterisation seam interrupts them - and dashes shorter than MinimumLength are then
            // thrown away, which is most of them.
            var points = new List<Vector3>();
            var inwards = new List<Vector3>();
            var offsets = new List<float>();
            var ok = new List<bool>();
            var heights = new List<float>();

            // The view from a leaning head at either end of the run, for MinLeanEndView.
            float leanEndView = float.MaxValue;
            int leanEndWalkable = int.MaxValue;
            if ((settings.MinLeanEndView > 0f || settings.MinLeanEndWalkable > 0f) && depth != null && run.Count > 0)
            {
                leanEndView = 0f;
                leanEndWalkable = 0;
                RimEdge first = run[0], last = run[run.Count - 1];
                foreach ((Vector3 end, Vector3 dir, Vector3 inw) in new[]
                {
                    (first.A, Vector3.Normalize(new Vector3(first.A.X - first.B.X, 0f, first.A.Z - first.B.Z)), first.Inward),
                    (last.B, Vector3.Normalize(new Vector3(last.B.X - last.A.X, 0f, last.B.Z - last.A.Z)), last.Inward),
                })
                {
                    if (float.IsNaN(dir.X)) continue;
                    Vector3 outw = -inw;
                    var head = new Vector3(end.X + dir.X * 0.5f + outw.X * 0.39f, end.Y + 1.4f, end.Z + dir.Z * 0.5f + outw.Z * 0.39f);
                    leanEndView = Math.Max(leanEndView, depth.FanMax(head, dir, 60f, 10f, 30f));
                    if (settings.MinLeanEndWalkable > 0f) leanEndWalkable = Math.Max(leanEndWalkable, depth.VisibleWalkable(head, dir, end.Y, floor));
                }
            }

            for (int e = 0; e < run.Count; e++)
            {
                RimEdge edge = run[e];
                int steps = Math.Max(1, (int)Math.Ceiling(edge.Length / step));
                for (int i = 0; i < steps; i++)
                {
                    Vector3 p = Vector3.Lerp(edge.A, edge.B, (float)i / steps);
                    points.Add(p);
                    inwards.Add(edge.Inward);
                    offsets.Add(FlushedOffset(p, -edge.Inward, depth, settings));

                    // The obstacle sits just outside the rim. Sample across its thickness rather than
                    // at one distance: the rim stands a walkable radius off the surface, so a single
                    // probe deep enough to clear the erosion passes straight through a thin panel.
                    float top = settings.UseRayObstacleTop && depth != null
                        ? depth.TopAlong(p, -edge.Inward, p.Y, settings)
                        : obstacles == null ? 0f : obstacles.ObstacleTopAlong(p, -edge.Inward, settings);
                    // The obstacle as seen from above at depth, for things whose front face under-reads
                    // (a table's rounded edge). It admits the sample; the class still follows the front
                    // face where that passes, so a crate in front of a tall shelf stays low cover.
                    if (top < settings.MinimumHeight && depth != null && settings.DepthTopDistances != null)
                    {
                        float depthTop = 0f;
                        foreach (float dd in settings.DepthTopDistances)
                        {
                            float dt = depth.DepthTop(p, edge.Inward, p.Y, dd);
                            if (dt > depthTop) depthTop = dt;
                        }
                        if (depthTop >= settings.MinimumHeight) top = Math.Min(depthTop, settings.LowHighDividingLine - 0.05f);
                    }
                    bool accept = top >= settings.MinimumHeight;
                    if (OracleAccept != null)
                    {
                        accept = OracleAccept(p, edge.Inward);
                        if (!accept) rejectedNoObstacle++;
                        ok.Add(accept);
                        heights.Add(settings.ClassifyCoverHeight(Math.Min(top, settings.MaximumObstacleHeight)));
                        continue;
                    }
                    if (learned != null && depth != null)
                    {
                        // The learned selector reads the same station description the training
                        // tables carry and replaces every hand gate below with one probability.
                        float[] x = LearnedCoverFeatures.Describe(p, edge.Inward, edge.Length, runLength,
                            runPos + edge.Length * ((float)i / steps), depth.Bvh, depth, settings);
                        accept = learned.Predict(x) >= learnedThreshold;
                        if (!accept) rejectedNoObstacle++;
                        ok.Add(accept);
                        heights.Add(settings.ClassifyCoverHeight(Math.Min(top, settings.MaximumObstacleHeight)));
                        continue;
                    }
                    if (accept && settings.UseObstacleHeightBands)
                        accept = (top <= settings.LowCoverMaxTop) || (top >= settings.HighCoverMinTop);
                    // A waist-high obstacle is a crate or a desk and may legitimately be thin; a tall
                    // one has to be a wall, and a tall THIN thing with open space behind it is a
                    // panel or a railing, which retail does not treat as cover. Our surplus is
                    // almost entirely tall - SCI_Hub comes out 102 low / 314 high against retail's
                    // 97 / 63 - so the thickness demanded depends on which it is.
                    if (accept && depth != null)
                    {
                        bool tall = top >= settings.LowHighDividingLine;
                        float required = tall ? settings.MinObstacleDepthHighCover : settings.MinObstacleDepth;
                        // A tall THIN thing is either a railing or a WALL, and retail covers walls.
                        // What separates them is how far up it goes: a railing stops around waist to
                        // chest, a wall runs past head height into the ceiling. So a thin obstacle
                        // that reaches HighCoverWallTop is exempt from the thickness demand.
                        if (tall && settings.HighCoverWallTop > 0f && top >= settings.HighCoverWallTop)
                            required = 0f;
                        if (required > 0f)
                            accept = depth.Thickness(p, -edge.Inward, p.Y, settings) >= required;
                    }
                    // Cover belongs near where the wall ends, not down the middle of a long run.
                    if (accept && settings.MaxWallEndDistance > 0f && depth != null
                        && (top >= settings.LowHighDividingLine || settings.WallEndAppliesToLowCover)
                        && (!settings.WallEndShellOnly || IsShell(edge, settings)))
                    {
                        float want = top >= settings.LowHighDividingLine && settings.MaxWallEndDistanceHigh > 0f
                            ? settings.MaxWallEndDistanceHigh : settings.MaxWallEndDistance;
                        if (top >= settings.LowHighDividingLine && settings.WallEndFraction > 0f)
                            want = Math.Max(want, settings.WallEndFraction * runLength);
                        if (want < 50f)   // a large window is the gate switched off; do not pay for the scan
                        {
                            float toEnd = depth.WallEndDistance(p, -edge.Inward, p.Y, want + 0.5f);
                            if (toEnd > want) accept = false;
                        }
                    }
                    // Can anything be engaged from here? Retail ships no cover slot with less than
                    // 60 degrees of clear arc. Sweep OUTWARD: what matters is what you can see past
                    // or over the obstacle, not the room at your back.
                    if (accept && settings.MinFiringArcDegrees > 0f && depth != null)
                    {
                        Vector3 stand = p + edge.Inward * settings.FiringArcStandOffset;
                        float arc = depth.ClearArcDegrees(stand, -edge.Inward, p.Y + 1.2f,
                                                          settings.FiringArcRange, settings.FiringArcStepDegrees);
                        if (arc < settings.MinFiringArcDegrees && top < settings.LowHighDividingLine)
                            arc = Math.Max(arc, depth.ClearArcDegrees(stand, -edge.Inward,
                                                                      p.Y + Math.Max(top + 0.15f, 1.0f),
                                                                      settings.FiringArcRange, settings.FiringArcStepDegrees));
                        if (arc < settings.MinFiringArcDegrees) accept = false;
                    }
                    if (!accept) rejectedNoObstacle++;

                    // Cover you cannot stand in front of is not cover. Retail covers rim with under
                    // a metre of clear run in front of it 3-7% of the time against 24% overall.
                    if (accept && settings.MinFrontClearance > 0f && depth != null)
                    {
                        if (depth.Clearance(p, edge.Inward, p.Y, settings.MinFrontClearance) < settings.MinFrontClearance)
                        {
                            accept = false;
                            rejectedCramped++;
                        }
                    }

                    // Line of sight over low cover, and from an end of a tall run - see the settings.
                    if (accept && depth != null && top < settings.LowHighDividingLine && settings.MinOverTopView > 0f)
                    {
                        float headOff = settings.FlushProbeOrigins && settings.FlushDistanceFromEdge > 0f
                            ? FlushedOffset(p, -edge.Inward, depth, settings) : settings.RimOffset;
                        var head = new Vector3(p.X - edge.Inward.X * headOff, p.Y + 1.5f, p.Z - edge.Inward.Z * headOff);
                        float want = settings.MinOverTopView * (settings.ShellViewScale != 1f && IsShell(edge, settings) ? settings.ShellViewScale : 1f);
                        if (depth.FanMax(head, -edge.Inward, 60f, 10f, 30f) < want) accept = false;
                    }
                    if (accept && top >= settings.LowHighDividingLine && settings.MinLeanEndView > 0f
                        && leanEndView < settings.MinLeanEndView * (settings.ShellViewScale != 1f && IsShell(edge, settings) ? settings.ShellViewScale : 1f))
                        accept = false;
                    if (accept && top >= settings.LowHighDividingLine && settings.MinLeanEndWalkable > 0f && leanEndWalkable < settings.MinLeanEndWalkable)
                        accept = false;
                    if (accept && settings.ExclusionBoxes != null)
                        for (int x = 0; x < settings.ExclusionBoxes.Count; x++)
                            if (settings.ExclusionBoxes[x].Contains(p)) { accept = false; break; }
                    // What the rim goes round, which no ray fired from the station can see.
                    if (accept && settings.MinLoopPerimeter > 0f && edge.LoopPerimeter > 0f && edge.LoopPerimeter < settings.MinLoopPerimeter)
                        accept = false;
                    if (accept && settings.MinReachableShare > 0f && !settings.ReachableGateOnSegments
                        && floor.ReachableShare(p + edge.Inward * settings.OpenAreaProbeInset, p.Y, settings.ReachableRadius) < settings.MinReachableShare)
                    {
                        accept = false;
                        rejectedCramped++;
                    }
                    if (accept && settings.MinOpenFloorArea > 0f)
                    {
                        float area = floor.AreaNear(p + edge.Inward * settings.OpenAreaProbeInset, p.Y, settings.OpenAreaRadius);
                        if (area < settings.MinOpenFloorArea) { accept = false; rejectedCramped++; }
                    }

                    ok.Add(accept);
                    heights.Add(settings.ClassifyCoverHeight(Math.Min(top, settings.MaximumObstacleHeight)));
                }
                runPos += edge.Length;
                if (e == run.Count - 1)
                {
                    points.Add(edge.B);
                    inwards.Add(edge.Inward);
                    offsets.Add(FlushedOffset(edge.B, -edge.Inward, depth, settings));
                    ok.Add(ok.Count > 0 && ok[ok.Count - 1]);
                    heights.Add(heights.Count > 0 ? heights[heights.Count - 1] : settings.LowHeight);
                }
            }
            if (points.Count < 2) return;

            BridgeGaps(ok, (int)Math.Round(settings.SpanGapTolerance / step));

            // Retail takes a wall whole or leaves it alone - see CoverBakeSettings.DecidePerRun - so
            // the gates decide the run, not the sample. The samples are evenly spaced along it, so
            // counting them is counting length.
            if (settings.DecidePerRun && ok.Count > 0)
            {
                int passed = 0;
                for (int i = 0; i < ok.Count; i++) if (ok[i]) passed++;
                bool accept = passed >= settings.RunAcceptFraction * ok.Count;
                for (int i = 0; i < ok.Count; i++) ok[i] = accept;
            }

            SmoothHeights(heights, ok, (int)Math.Round(settings.HeightSmoothingDistance / step));

            // Ground split: cut where the floor under the run deviates.
            List<bool> splitHere = SplitOnGround(points, step, settings);

            int start = -1;
            for (int i = 0; i < points.Count; i++)
            {
                bool endsHere = !ok[i] || (splitHere != null && splitHere[i])
                             || (start >= 0 && Math.Abs(heights[i] - heights[start]) > 0.01f);
                if (start >= 0 && endsHere)
                {
                    Close(spans, points[start], points[i], AverageInward(inwards, start, i), heights[start], AverageOffset(offsets, start, i), settings);
                    start = ok[i] ? i : -1;
                    continue;
                }
                if (start < 0 && ok[i]) start = i;
            }
            if (start >= 0 && start < points.Count - 1)
                Close(spans, points[start], points[points.Count - 1], AverageInward(inwards, start, points.Count - 1), heights[start], AverageOffset(offsets, start, points.Count - 1), settings);
        }

        /// <summary>
        /// Flush with collision: how far the cover face sits from the rim at this point -
        /// the collision surface less <see cref="CoverBakeSettings.FlushDistanceFromEdge"/>, clamped to
        /// <see cref="CoverBakeSettings.FlushMaxAdjustment"/> either side of the fixed offset. Falls
        /// back to the fixed offset when the stage is off or nothing is in front.
        /// </summary>
        private static float FlushedOffset(Vector3 p, Vector3 outward, DepthProbe depth, CoverBakeSettings settings)
        {
            if (settings.FlushDistanceFromEdge <= 0f || depth == null)
                return settings.RimOffset;
            float limit = settings.RimOffset + settings.FlushMaxAdjustment;
            float d = depth.SurfaceDistance(p, outward, p.Y + 0.5f, limit);
            if (d < 0f)
                return settings.RimOffset;
            float want = d - settings.FlushDistanceFromEdge;
            float lo = settings.RimOffset - settings.FlushMaxAdjustment;
            return Math.Max(lo, Math.Min(limit, want));
        }

        private static float AverageOffset(List<float> offsets, int start, int end)
        {
            float sum = 0f; int n = 0;
            for (int i = start; i <= end && i < offsets.Count; i++) { sum += offsets[i]; n++; }
            return n == 0 ? 0f : sum / n;
        }

        private static Vector3 AverageInward(
List<Vector3> inwards, int from, int to)
        {
            Vector3 sum = Vector3.Zero;
            for (int i = from; i <= to && i < inwards.Count; i++) sum += inwards[i];
            return sum.LengthSquared() < 1e-8f ? inwards[from] : Vector3.Normalize(sum);
        }

        /// <summary>Close runs of rejected samples shorter than the tolerance.</summary>
        private static void BridgeGaps(List<bool> ok, int maxGap)
        {
            if (maxGap <= 0) return;
            int i = 0;
            while (i < ok.Count)
            {
                if (ok[i]) { i++; continue; }
                int j = i;
                while (j < ok.Count && !ok[j]) j++;
                bool bounded = i > 0 && j < ok.Count;
                if (bounded && j - i <= maxGap)
                    for (int k = i; k < j; k++) ok[k] = true;
                i = j;
            }
        }

        /// <summary>
        /// Ground split: clear the accept flag at any sample where the ground
        /// deviates from its neighbours by more than
        /// <see cref="CoverBakeSettings.GroundHeightSplitDeviation"/>, which ends the span there and
        /// starts a new one after it.
        /// </summary>
        private static List<bool> SplitOnGround(List<Vector3> points, float step, CoverBakeSettings settings)
        {
            float deviation = settings.GroundHeightSplitDeviation;
            if (deviation <= 0f || points.Count < 3)
                return null;

            // Early out: a run whose ground never moves cannot be split.
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Y < lo) lo = points[i].Y;
                if (points[i].Y > hi) hi = points[i].Y;
            }
            if (hi - lo < settings.GroundHeightSplitEarlyOut)
                return null;

            var split = new List<bool>(points.Count);
            for (int i = 0; i < points.Count; i++) split.Add(false);

            int window = Math.Max(1, (int)Math.Round(settings.GroundHeightSplitCheckDistance / step));
            int stride = Math.Max(1, points.Count / Math.Max(1, settings.GroundHeightSplitMaxSamples));
            for (int i = 0; i < points.Count; i += stride)
            {
                int a = Math.Max(0, i - window), b = Math.Min(points.Count - 1, i + window);
                float worst = Math.Max(Math.Abs(points[a].Y - points[i].Y), Math.Abs(points[b].Y - points[i].Y));
                if (worst > deviation)
                    split[i] = true;
            }
            return split;
        }

        /// <summary>
        /// Majority filter over the low/high classification, so one tall sample in a run of waist-high
        /// ones does not split the segment in two.
        /// </summary>
        private static void SmoothHeights(List<float> heights, List<bool> ok, int window)
        {
            if (window < 1 || heights.Count == 0) return;
            var smoothed = new float[heights.Count];
            for (int i = 0; i < heights.Count; i++)
            {
                int lo = Math.Max(0, i - window), hi = Math.Min(heights.Count - 1, i + window);
                int high = 0, total = 0;
                for (int k = lo; k <= hi; k++)
                {
                    if (!ok[k]) continue;
                    total++;
                    if (heights[k] > heights[i] || heights[k] > 1.2f) high++;
                }
                smoothed[i] = total == 0 ? heights[i] : (high * 2 >= total ? MaxOf(heights, lo, hi) : MinOf(heights, lo, hi));
            }
            for (int i = 0; i < heights.Count; i++) heights[i] = smoothed[i];
        }

        private static float MaxOf(List<float> v, int lo, int hi)
        {
            float m = v[lo];
            for (int i = lo; i <= hi; i++) if (v[i] > m) m = v[i];
            return m;
        }

        private static float MinOf(List<float> v, int lo, int hi)
        {
            float m = v[lo];
            for (int i = lo; i <= hi; i++) if (v[i] < m) m = v[i];
            return m;
        }

        /// <summary>
        /// Emit a span, cut into pieces no longer than the ceiling retail's own lengths sit under.
        /// </summary>
        private static void Close(List<Span> spans, Vector3 start, Vector3 end, Vector3 inward, float height, float offset, CoverBakeSettings settings)
        {
            Vector3 delta = end - start; delta.Y = 0;
            float length = delta.Length();
            if (length < 0.2f)
                return;

            float max = settings.MaximumSegmentLength;
            int pieces = max > 0.5f ? Math.Max(1, (int)Math.Ceiling(length / max)) : 1;
            for (int i = 0; i < pieces; i++)
            {
                Vector3 a = Vector3.Lerp(start, end, (float)i / pieces);
                Vector3 b = Vector3.Lerp(start, end, (float)(i + 1) / pieces);
                spans.Add(new Span { Start = a, End = b, Inward = inward, Height = height, Offset = offset });
            }
        }

        #endregion

        #region geometry lookups

        /// <summary>
        /// Solid occupancy of the level, as a column of half-metre slabs per quarter-metre cell.
        /// Answers "is there something behind this point, and how tall is it" without a ray cast.
        /// </summary>
        public sealed class ObstacleField
        {
            private readonly Dictionary<long, ulong> _cells = new Dictionary<long, ulong>();
            private const float Cell = 0.25f;
            private const float Slab = 0.5f;
            private const int SlabsPerGroup = 64;

            public ObstacleField(CollisionNavMeshSoup soup, CoverBakeSettings settings)
            {
                if (soup?.Tris == null) return;
                for (int t = 0; t + 2 < soup.Tris.Length; t += 3)
                {
                    Vector3 a = Vertex(soup, soup.Tris[t]);
                    Vector3 b = Vertex(soup, soup.Tris[t + 1]);
                    Vector3 c = Vertex(soup, soup.Tris[t + 2]);

                    float minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
                    float minZ = Math.Min(a.Z, Math.Min(b.Z, c.Z)), maxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z));
                    float minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)), maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));

                    int x0 = (int)Math.Floor(minX / Cell), x1 = (int)Math.Floor(maxX / Cell);
                    int z0 = (int)Math.Floor(minZ / Cell), z1 = (int)Math.Floor(maxZ / Cell);
                    // A single triangle spanning a whole level is a decode outlier, not cover.
                    if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 40000)
                        continue;

                    int s0 = (int)Math.Floor(minY / Slab), s1 = (int)Math.Floor(maxY / Slab);
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                            for (int s = s0; s <= s1; s++)
                                Mark(x, z, s);
                }
            }

            private static Vector3 Vertex(CollisionNavMeshSoup soup, int i) =>
                new Vector3(soup.Verts[i * 3], soup.Verts[i * 3 + 1], soup.Verts[i * 3 + 2]);

            private void Mark(int x, int z, int slab)
            {
                int group = slab >= 0 ? slab / SlabsPerGroup : (slab - SlabsPerGroup + 1) / SlabsPerGroup;
                int bit = slab - group * SlabsPerGroup;
                long key = Key(x, z, group);
                _cells.TryGetValue(key, out ulong mask);
                _cells[key] = mask | (1UL << bit);
            }

            private bool IsSet(int x, int z, int slab)
            {
                int group = slab >= 0 ? slab / SlabsPerGroup : (slab - SlabsPerGroup + 1) / SlabsPerGroup;
                int bit = slab - group * SlabsPerGroup;
                return _cells.TryGetValue(Key(x, z, group), out ulong mask) && (mask & (1UL << bit)) != 0;
            }

            private static long Key(int x, int z, int group) =>
                ((long)x * 73856093) ^ ((long)z * 19349663) ^ ((long)group * 83492791);

            /// <summary>
            /// How far the solid behind a rim point extends away from it, measured at knee height.
            /// Zero when there is nothing there; one cell when it is a single thin panel.
            /// </summary>
            /// <remarks>
            /// Retail is choosy about this and the corridor levels show it most clearly: on
            /// BSP_LV426_Pt01 the rim it covers has a median depth of 3.2 m and a 10th percentile of
            /// 1.0, while the rim it leaves alone has a median of 0.0 - a single thin panel with open
            /// space behind it. SCI_Hub and Tech_Hub lean the same way more gently.
            /// </remarks>
            public float ObstacleDepth(Vector3 from, Vector3 outward, float floorY, float maxDepth)
            {
                int slab = (int)Math.Floor((floorY + 0.4f) / Slab);
                float first = -1f, last = -1f;
                for (float d = 0.15f; d <= maxDepth; d += Cell)
                {
                    Vector3 p = from + outward * d;
                    if (IsSet((int)Math.Floor(p.X / Cell), (int)Math.Floor(p.Z / Cell), slab))
                    {
                        if (first < 0f) first = d;
                        last = d;
                    }
                    else if (first >= 0f)
                    {
                        break; // out the far side of the obstacle
                    }
                }
                return first < 0f ? 0f : last - first + Cell;
            }

            /// <summary>
            /// Tallest solid found stepping outward from <paramref name="from"/>, over the band the
            /// obstacle could occupy. The rim stands a walkable radius clear of the surface, so the
            /// obstacle starts around 0.31 m out and a thin panel is gone again by 0.5 m.
            /// </summary>
            public float ObstacleTopAlong(Vector3 from, Vector3 outward, CoverBakeSettings settings)
            {
                float best = 0;
                for (float d = 0.15f; d <= settings.ObstacleProbeDistance + 0.2f; d += Cell)
                {
                    float top = ObstacleTop(from + outward * d, from.Y, settings);
                    if (top > best) best = top;
                }
                return best;
            }

            /// <summary>
            /// Height of the solid standing at <paramref name="p"/>, measured up from
            /// <paramref name="floorY"/>. Zero when there is nothing there. The search stops at the
            /// second consecutive gap, so a ceiling is never mistaken for the top of a waist-high
            /// desk but a slab the rasteriser missed does not end the object either.
            /// </summary>
            public float ObstacleTop(Vector3 p, float floorY, CoverBakeSettings settings)
            {
                int x = (int)Math.Floor(p.X / Cell), z = (int)Math.Floor(p.Z / Cell);
                int baseSlab = (int)Math.Floor((floorY + 0.05f) / Slab);
                float limit = Math.Max(settings.MaximumObstacleHeight, 4.0f);
                int maxSlabs = (int)Math.Ceiling(limit / Slab);

                float top = 0;
                int gap = 0;
                for (int s = 0; s <= maxSlabs; s++)
                {
                    if (IsSet(x, z, baseSlab + s))
                    {
                        gap = 0;
                        top = (baseSlab + s + 1) * Slab - floorY;
                    }
                    else if (++gap > 1)
                    {
                        break;
                    }
                }
                return Math.Max(0f, top);
            }
        }

        /// <summary>
        /// How thick the solid behind a rim point is, measured by walking a ray through it rather
        /// than by reading the voxel field.
        /// </summary>
        /// <remarks>
        /// <para>This is the one measurement the voxel field cannot make. Its cells are 0.25 m and a
        /// triangle marks every cell its bounding box touches, so a 10 cm panel comes out half a
        /// metre thick and is indistinguishable from a wall. Ray casting sees the real surfaces.</para>
        /// <para>What it buys: retail will not put cover against a single thin panel with open space
        /// behind it. On BSP_LV426_Pt01 - the level we are furthest from, shipping 24 segments where
        /// we produced 237 - the rim retail covers has a median thickness of 3.2 m and a 10th
        /// percentile of 1.0, while the rim it ignores has a median of 0.0.</para>
        /// </remarks>
        public sealed class DepthProbe
        {
            private readonly BVHAccel _bvh;

            /// <summary>The acceleration structure over the soup, for callers that cast their own rays.</summary>
            public BVHAccel Bvh => _bvh;

            public DepthProbe(CollisionNavMeshSoup soup)
            {
                if (soup == null || soup.TriangleCount == 0)
                    return;
                _bvh = new BVHAccel();
                _bvh.Build(soup.Verts, soup.Tris);
            }

            /// <summary>
            /// Distance between the first and last surface the ray meets. Zero when it meets one
            /// surface or none - a one-sided sheet is not something to hide behind.
            /// </summary>
            public float Thickness(Vector3 rimPoint, Vector3 outward, float floorY, CoverBakeSettings settings)
            {
                if (_bvh == null)
                    return float.MaxValue; // nothing to test against; do not reject on that basis

                Vector3 origin = new Vector3(rimPoint.X, floorY + 0.4f, rimPoint.Z);
                float limit = settings.ObstacleDepthSearchDistance;
                float first = -1f, last = -1f;

                float t = 0.02f;
                for (int i = 0; i < 32 && t < limit; i++)
                {
                    var ray = new Ray(origin, outward, t, limit);
                    if (!_bvh.Traverse(ref ray, out Hit hit))
                        break;
                    if (first < 0f) first = hit.T;
                    last = hit.T;
                    t = hit.T + 0.01f;
                }
                return first < 0f || last <= first ? 0f : last - first;
            }

            /// <summary>
            /// Height of the obstacle in front of the rim, measured by ray rather than by the voxel
            /// field. The voxel grid marks a triangle into every half-metre slab its bounding box
            /// touches, so it reads high and blurs the waist-high objects retail actually covers
            /// into the walls it mostly does not.
            /// </summary>
            /// <remarks>
            /// Scans upwards in <see cref="CoverBakeSettings.RayTopStep"/> increments and stops at
            /// the first miss above 0.3 m, so a ceiling with nothing under it is not the top of a
            /// desk. This is the measurement the rim-feature survey used, where the covered rate by
            /// obstacle height is 36.9% at 0.5-1.0 m against 15.6% above 3.5.
            /// </remarks>
            public float TopAlong(Vector3 rimPoint, Vector3 outward, float floorY, CoverBakeSettings settings)
            {
                if (_bvh == null)
                    return 0f;
                var flat = new Vector3(outward.X, 0f, outward.Z);
                if (flat.LengthSquared() < 1e-8f)
                    return 0f;
                flat = Vector3.Normalize(flat);

                float top = 0f;
                float step = Math.Max(0.05f, settings.RayTopStep);
                float reach = settings.RayTopReach;
                float firstMiss = -1f;
                float firstT = -1f;
                for (float h = step; h <= settings.MaximumObstacleHeight + 1.5f; h += step)
                {
                    var origin = new Vector3(rimPoint.X, floorY + h, rimPoint.Z);
                    var ray = new Ray(origin, flat, 0f, reach);
                    bool hit = _bvh.Traverse(ref ray, out Hit hh);
                    if (hit && settings.RayTopSameSurface > 0f)
                    {
                        if (firstT < 0f) firstT = hh.T;
                        else if (hh.T > firstT + settings.RayTopSameSurface) hit = false; // a different, set-back object
                    }
                    if (hit) { top = h; firstMiss = -1f; }
                    else if (h > 0.3f) { firstMiss = h; break; }
                }

                // The scan above reports the last height that still HIT, so the real top is
                // somewhere in the step above it and we under-report by up to a whole RayTopStep.
                // That is 0.15 m, which is exactly the distance between our fitted MinimumHeight of
                // 0.65 and the 0.8 it started from - a waist-high desk at 0.85 m reads as 0.75 and
                // fails a gate it should pass. Close the interval by bisection.
                if (settings.RayTopRefineSteps > 0 && top > 0f && firstMiss > top)
                {
                    float lo = top, hi = firstMiss;
                    for (int i = 0; i < settings.RayTopRefineSteps; i++)
                    {
                        float mid = 0.5f * (lo + hi);
                        var probe = new Ray(new Vector3(rimPoint.X, floorY + mid, rimPoint.Z), flat, 0f, reach);
                        bool phit = _bvh.Traverse(ref probe, out Hit ph);
                        if (phit && settings.RayTopSameSurface > 0f && firstT >= 0f && ph.T > firstT + settings.RayTopSameSurface) phit = false;
                        if (phit) lo = mid; else hi = mid;
                    }
                    top = lo;
                }
                return top;
            }

            /// <summary>
            /// How far the ray runs before it meets anything, capped at <paramref name="limit"/>.
            /// Measured at chest height so a kerb does not read as a wall.
            /// </summary>

            /// <summary>
            /// How far along the wall you can walk from here before the thing you are hiding behind
            /// stops. Capped at <paramref name="limit"/>.
            /// </summary>
            /// <remarks>
            /// Retail's cover sits near where a wall ENDS - somewhere you can lean out - and thins
            /// out along a long run. Measured over the rim of four levels, covered% against metres
            /// to the end: Tech_Hub 37.7 / 27.2 / 16.4 / 9.5 / 4.6 / 1.7 at 0/1/2/3/5/6 m against a
            /// 30% base, SCI_Hub 28.2 / 18.6 / 5.8 and then ZERO beyond 4 m across 132 samples,
            /// Tech_MuthrCore 22.0 falling to ~11. It is the first rim feature that runs the same
            /// direction on every level with signal, and it is exactly the shape of our surplus:
            /// we run cover down whole walls, retail does not.
            /// </remarks>
            public float WallEndDistance(Vector3 rimPoint, Vector3 outward, float floorY, float limit)
            {
                if (_bvh == null) return limit;
                var along = new Vector3(outward.Z, 0.0f, -outward.X);
                float best = limit;
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    for (float d = 0.25f; d <= limit; d += 0.25f)
                    {
                        Vector3 q = rimPoint + along * (dir * d);
                        var origin = new Vector3(q.X, floorY + 1.2f, q.Z);
                        var ray = new Ray(origin, outward, 0.02f, 1.0f);
                        if (_bvh.Traverse(ref ray, out Hit _)) continue;
                        if (d < best) best = d;
                        break;
                    }
                }
                return best;
            }

            /// <summary>
            /// Widest contiguous clear firing arc from where the occupant stands, swept horizontally
            /// about the inward normal at the given eye height. Returned in degrees.
            /// </summary>
            /// <remarks>
            /// This is the measurement retail's own files carry and we never made. Every shipped
            /// cover slot has at least 60 degrees of clear arc (p10 84, and 0.0% of 9,085 slots below
            /// 60), and the two height classes are defined by WHICH arc: low cover's over-the-top arc
            /// has a median of 180 degrees and is zero on 0.2% of slots, while high cover's is zero on
            /// 100% of them and it is a lean-past-the-edge arc instead, median 96.
            /// </remarks>
            public float ClearArcDegrees(Vector3 stand, Vector3 inward, float eyeY, float range, float stepDegrees)
            {
                if (_bvh == null) return 360f;
                var origin = new Vector3(stand.X, eyeY, stand.Z);
                int steps = Math.Max(3, (int)Math.Round(180f / stepDegrees) + 1);
                int best = 0, run = 0;
                for (int i = 0; i < steps; i++)
                {
                    float deg = -90f + 180f * i / (steps - 1);
                    float r = (float)(deg * Math.PI / 180.0);
                    float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
                    var dir = new Vector3(inward.X * c - inward.Z * s, 0f, inward.X * s + inward.Z * c);
                    var ray = new Ray(origin, dir, 0.02f, range);
                    if (_bvh.Traverse(ref ray, out Hit _)) run = 0;
                    else { run++; if (run > best) best = run; }
                }
                return best * (180f / (steps - 1));
            }

            /// <summary>
            /// Count of polar-grid points (1 to 12 m by 1 m, +-60 degrees by 15 about <paramref name="centre"/>)
            /// that carry navmesh floor at <paramref name="floorY"/> and are seen from <paramref name="head"/>
            /// at 1.0 m above that floor.
            /// </summary>
            public int VisibleWalkable(Vector3 head, Vector3 centre, float floorY, NavFloorGrid floor)
            {
                if (_bvh == null || floor == null) return 45;
                int count = 0;
                for (float a = -60f; a <= 60.01f; a += 15f)
                {
                    double r = a * Math.PI / 180.0; float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
                    var dir = Vector3.Normalize(new Vector3(centre.X * c - centre.Z * s, 0f, centre.X * s + centre.Z * c));
                    for (float d = 1f; d <= 12.01f; d += 1f)
                    {
                        float qx = head.X + dir.X * d, qz = head.Z + dir.Z * d;
                        if (!floor.HasFloor(qx, qz, floorY, 0.5f)) continue;
                        var target = new Vector3(qx, floorY + 1.0f, qz);
                        Vector3 v = target - head; float len = v.Length();
                        if (len < 0.1f) continue;
                        var ray = new Ray(head, v / len, 0.02f, len - 0.05f);
                        if (_bvh.Traverse(ref ray, out Hit _)) continue;
                        count++;
                    }
                }
                return count;
            }

            /// <summary>Farthest a horizontal ray gets from <paramref name="origin"/> within <paramref name="halfDeg"/> of <paramref name="centre"/>, capped.</summary>
            public float FanMax(Vector3 origin, Vector3 centre, float halfDeg, float stepDeg, float cap)
            {
                if (_bvh == null) return cap;
                float best = 0f;
                for (float a = -halfDeg; a <= halfDeg + 0.01f; a += stepDeg)
                {
                    double r = a * Math.PI / 180.0; float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
                    var dir = Vector3.Normalize(new Vector3(centre.X * c - centre.Z * s, 0f, centre.X * s + centre.Z * c));
                    var ray = new Ray(origin, dir, 0.02f, cap);
                    float d = _bvh.Traverse(ref ray, out Hit hit) ? hit.T : cap;
                    if (d > best) best = d;
                }
                return best;
            }

            /// <summary>
            /// Height of solid above the floor at a point <paramref name="depth"/> m along the
            /// outward normal from the rim. Two-sided triangles make "inside" ambiguous, so: parity
            /// of crossings along a ray from 0.5 m inside the rim (1.0 m up) says whether the point
            /// is inside a solid at that height; if so an upward ray from just above the floor finds
            /// the top face from inside; otherwise a downward ray from 2.5 m finds the top of
            /// anything lower, and an upward ray the underside of an overhang. 0 = floor level or
            /// nothing there.
            /// </summary>
            public float DepthTop(Vector3 rimPoint, Vector3 inward, float floorY, float depth)
            {
                if (_bvh == null) return 0f;
                Vector3 outward = -inward;
                Vector3 s = rimPoint + outward * depth;
                Vector3 from = new Vector3(rimPoint.X + inward.X * 0.5f, floorY + 1.0f, rimPoint.Z + inward.Z * 0.5f);
                Vector3 to = new Vector3(s.X, floorY + 1.0f, s.Z);
                Vector3 dv = to - from; float len = dv.Length(); if (len < 1e-4f) return 0f; dv /= len;
                int hits = 0; float t = 0.001f;
                for (int i = 0; i < 16 && t < len; i++)
                {
                    var ray = new Ray(from, dv, t, len);
                    if (!_bvh.Traverse(ref ray, out Hit h)) break;
                    hits++; t = h.T + 0.002f;
                }
                bool inside = (hits & 1) == 1;
                var up = new Ray(new Vector3(s.X, floorY + 0.05f, s.Z), Vector3.UnitY, 0.001f, 3.5f);
                float upH = _bvh.Traverse(ref up, out Hit uh) ? uh.T + 0.05f : 99f;
                var down = new Ray(new Vector3(s.X, floorY + 2.5f, s.Z), -Vector3.UnitY, 0.001f, 4.5f);
                float downH = _bvh.Traverse(ref down, out Hit dh) ? 2.5f - dh.T : -2f;
                if (inside) return upH < 3.5f ? Math.Min(upH, 2.5f) : 2.5f;
                if (downH >= 0.1f && downH < 2.0f) return downH;
                if (upH < 2.0f) return upH;
                return 0f;
            }

            /// <summary>
            /// Distance from a rim point to the collision surface in front of it, or -1 if nothing is
            /// within the limit. This is what a flush-with-collision placement measures and
            /// what our fixed RimOffset stands in for.
            /// </summary>
            public float SurfaceDistance(Vector3 rimPoint, Vector3 outward, float atY, float limit)
            {
                if (_bvh == null) return -1f;
                var ray = new Ray(new Vector3(rimPoint.X, atY, rimPoint.Z), outward, 0f, limit);
                return _bvh.Traverse(ref ray, out Hit h) ? h.T : -1f;
            }

            public float Clearance(Vector3 rimPoint, Vector3 inward, float floorY, float limit)
            {
                if (_bvh == null)
                    return float.MaxValue;
                var origin = new Vector3(rimPoint.X, floorY + 1.0f, rimPoint.Z);
                var ray = new Ray(origin, inward, 0.02f, limit);
                return _bvh.Traverse(ref ray, out Hit hit) ? hit.T : limit;
            }
        }

        /// <summary>Walkable floor of the navmesh on a coarse grid, for "how much room is in front".</summary>
        public sealed class NavFloorGrid
        {
            private readonly Dictionary<long, List<float>> _cells = new Dictionary<long, List<float>>();
            private const float Cell = 0.5f;

            public NavFloorGrid(NavigationMesh nav)
            {
                foreach (NavigationMesh.dtPoly poly in nav.Polygons)
                {
                    if (poly.vertCount < 3 || poly.verts == null) continue;
                    if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND) continue;
                    if (((uint)poly.area.GetMarkupFlags() & (uint)NavigationMesh.NavMeshAreaTypeFlags.BackstageFlag) != 0) continue;

                    var verts = new Vector3[poly.vertCount];
                    for (int i = 0; i < poly.vertCount; i++) verts[i] = nav.Vertices[poly.verts[i]];
                    for (int i = 2; i < verts.Length; i++)
                        RasterTriangle(verts[0], verts[i - 1], verts[i]);
                }
            }

            private void RasterTriangle(Vector3 a, Vector3 b, Vector3 c)
            {
                float minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
                float minZ = Math.Min(a.Z, Math.Min(b.Z, c.Z)), maxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z));
                int x0 = (int)Math.Floor(minX / Cell), x1 = (int)Math.Floor(maxX / Cell);
                int z0 = (int)Math.Floor(minZ / Cell), z1 = (int)Math.Floor(maxZ / Cell);
                if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 1_000_000) return;

                double det = (double)(b.Z - c.Z) * (a.X - c.X) + (double)(c.X - b.X) * (a.Z - c.Z);
                if (Math.Abs(det) < 1e-12) return;

                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++)
                    {
                        float px = (x + 0.5f) * Cell, pz = (z + 0.5f) * Cell;
                        double l1 = ((double)(b.Z - c.Z) * (px - c.X) + (double)(c.X - b.X) * (pz - c.Z)) / det;
                        if (l1 < 0 || l1 > 1) continue;
                        double l2 = ((double)(c.Z - a.Z) * (px - c.X) + (double)(a.X - c.X) * (pz - c.Z)) / det;
                        if (l2 < 0 || l2 > 1) continue;
                        double l3 = 1.0 - l1 - l2;
                        if (l3 < 0) continue;
                        float y = (float)(l1 * a.Y + l2 * b.Y + l3 * c.Y);
                        Add(x, z, y);
                    }
            }

            private void Add(int x, int z, float y)
            {
                long k = ((long)x << 32) ^ (uint)z;
                if (!_cells.TryGetValue(k, out List<float> ys)) _cells[k] = ys = new List<float>(1);
                for (int i = 0; i < ys.Count; i++)
                    if (Math.Abs(ys[i] - y) < 0.5f) return;
                ys.Add(y);
            }

            /// <summary>Is there navmesh floor within <paramref name="tol"/> of height <paramref name="y"/> at this XZ?</summary>
            public bool HasFloor(float x, float z, float y, float tol)
            {
                if (!_cells.TryGetValue(((long)(int)Math.Floor(x / Cell) << 32) ^ (uint)(int)Math.Floor(z / Cell), out List<float> ys)) return false;
                for (int i = 0; i < ys.Count; i++)
                    if (Math.Abs(ys[i] - y) <= tol) return true;
                return false;
            }

            /// <summary>
            /// Share of the floor within <paramref name="radius"/> that can be WALKED to from the
            /// point, against the floor that is merely present - so an alcove behind a neck scores
            /// low even where the area is large. See CoverBakeSettings.MinReachableShare.
            /// </summary>
            public float ReachableShare(Vector3 p, float y, float radius)
            {
                int gx = (int)Math.Floor(p.X / Cell), gz = (int)Math.Floor(p.Z / Cell);
                bool Solid(int cx2, int cz2)
                {
                    if (!_cells.TryGetValue(((long)cx2 << 32) ^ (uint)cz2, out List<float> ys)) return false;
                    for (int i = 0; i < ys.Count; i++) if (Math.Abs(ys[i] - y) < 1.0f) return true;
                    return false;
                }
                long CellKey(int cx2, int cz2) { return ((long)cx2 << 32) ^ (uint)cz2; }
                if (!Solid(gx, gz)) return 0f;
                int n = (int)Math.Ceiling(radius / Cell);
                float rr = radius * radius;
                int present = 0;
                for (int dx = -n; dx <= n; dx++)
                    for (int dz = -n; dz <= n; dz++)
                    {
                        float cx = dx * Cell, cz = dz * Cell;
                        if (cx * cx + cz * cz > rr) continue;
                        if (Solid(gx + dx, gz + dz)) present++;
                    }
                if (present == 0) return 0f;

                var seen = new HashSet<long> { CellKey(gx, gz) };
                var queue = new Queue<(int x, int z)>();
                queue.Enqueue((gx, gz));
                int reached = 1;
                while (queue.Count > 0)
                {
                    (int x, int z) = queue.Dequeue();
                    for (int k = 0; k < 4; k++)
                    {
                        int ax = x + (k == 0 ? 1 : k == 1 ? -1 : 0);
                        int az = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        float cx = (ax - gx) * Cell, cz = (az - gz) * Cell;
                        if (cx * cx + cz * cz > rr) continue;
                        if (!Solid(ax, az)) continue;
                        if (!seen.Add(CellKey(ax, az))) continue;
                        reached++;
                        queue.Enqueue((ax, az));
                    }
                }
                return (float)reached / present;
            }

            /// <summary>Walkable area (m2) within <paramref name="radius"/> and on the same storey.</summary>
            public float AreaNear(Vector3 p, float y, float radius)
            {
                int r = (int)Math.Ceiling(radius / Cell);
                int cx = (int)Math.Floor(p.X / Cell), cz = (int)Math.Floor(p.Z / Cell);
                int r2 = r * r;
                float area = 0;
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (dx * dx + dz * dz > r2) continue;
                        if (!_cells.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out List<float> ys)) continue;
                        for (int i = 0; i < ys.Count; i++)
                            if (Math.Abs(ys[i] - y) <= 1.5f) { area += Cell * Cell; break; }
                    }
                return area;
            }
        }

        #endregion
    }
}
#endif
