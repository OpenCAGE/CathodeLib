#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Builds the alien's backstage navmesh: a triangulated sheet spanning the tops of the
    /// level's PathfindingAlienBackstageNode entities (the ceiling ends of the vent traversals).
    /// </summary>
    /// <remarks>
    /// Matches the retail generator, decoded from SCI_Hub's shipped NAV_MESH:
    /// the node top positions are Delaunay-triangulated in XZ, then every edge is recursively
    /// halved (Y interpolated linearly) while longer than 15 m, and the refined point set is
    /// triangulated again. Every subdivision point retail ships - e.g. the 7 interior points of
    /// SCI_Hub's 88 m south hull edge - falls exactly on those halving fractions.
    /// </remarks>
    public static class BackstageMeshBuilder
    {
        public sealed class Result
        {
            public List<Vector3> Vertices = new List<Vector3>();
            /// <summary>Vertex index triples, wound counter-clockwise seen from above (+Y).</summary>
            public List<int> Triangles = new List<int>();
            public string Warning;
        }

        /// <summary>
        /// Triangulate the backstage sheet over <paramref name="tops"/>. Returns null when no
        /// sheet can be built (fewer than 3 distinct points); a degenerate colinear layout falls
        /// back to a thin strip along the line so the level still gets a backstage. Both cases
        /// set <c>Warning</c> on the result (null return reports through <paramref name="warning"/>).
        /// </summary>
        public static Result Build(List<Vector3> tops, float maxEdgeLength, float stripHalfWidth, out string warning)
        {
            warning = null;
            if (tops == null || tops.Count == 0)
            {
                warning = "no backstage nodes";
                return null;
            }

            // Dedupe near-identical tops (two nodes can share one vent mouth).
            List<Vector3> points = DedupePoints(tops, 0.05f);
            if (points.Count < 3)
            {
                warning = $"only {points.Count} distinct backstage node top(s) - need 3 to triangulate";
                return null;
            }

            List<int> coarse = DelaunayXZ(points);
            if (coarse.Count == 0)
            {
                // All points colinear: no retail example survives in this install to copy, so
                // widen the line into a strip rather than shipping no backstage at all.
                Result strip = BuildColinearStrip(points, maxEdgeLength, stripHalfWidth);
                if (strip == null)
                {
                    warning = "backstage nodes are colinear and too close together to build a strip";
                    return null;
                }
                strip.Warning = "backstage nodes are colinear - built a fallback strip instead of a Delaunay sheet";
                return strip;
            }

            // Refine by longest-edge bisection: while any edge exceeds the limit, insert its
            // midpoint and connect it to the opposite vertex of each triangle sharing the edge.
            // No retriangulation happens after the coarse Delaunay - the retail data proves it:
            // SCI_Hub's 67 backstage triangles are exactly its 10 coarse triangles plus one per
            // boundary split (13) plus two per interior split (22), and midpoints of the interior
            // edges the bisection itself creates are shipped too.
            List<Vector3> refined = new List<Vector3>(points);
            List<int> tris = BisectLongEdges(refined, coarse, maxEdgeLength);

            return new Result { Vertices = refined, Triangles = tris };
        }

        /// <summary>Test hook: the deduped coarse Delaunay for a top set, before refinement.</summary>
        public static List<int> DebugCoarseDelaunay(List<Vector3> tops)
        {
            return DelaunayXZ(DedupePoints(tops, 0.05f));
        }

        static List<Vector3> DedupePoints(List<Vector3> points, float epsilon)
        {
            var result = new List<Vector3>(points.Count);
            float epsSq = epsilon * epsilon;
            foreach (Vector3 p in points)
            {
                bool dupe = false;
                for (int i = 0; i < result.Count; i++)
                {
                    if (Vector3.DistanceSquared(result[i], p) <= epsSq)
                    {
                        dupe = true;
                        break;
                    }
                }
                if (!dupe)
                    result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Edge bisection refinement: split every edge longer than the limit at its midpoint,
        /// dividing each triangle that shares the edge in two (midpoint joined to the opposite
        /// vertex). The coarse Delaunay edges are processed longest-first; edges created along
        /// the way (halves and the new midpoint-to-opposite connectors) queue up behind them in
        /// creation order. That ordering reproduces retail: SCI_Hub ships the midpoint of
        /// EA@0.5-BA@0.5 (a connector split), which only exists if EA is split before BA's
        /// second-generation halves despite being shorter than them.
        /// </summary>
        static List<int> BisectLongEdges(List<Vector3> points, List<int> coarse, float maxEdgeLength)
        {
            var tris = new List<(int a, int b, int c)>();
            for (int t = 0; t + 2 < coarse.Count; t += 3)
                tris.Add((coarse[t], coarse[t + 1], coarse[t + 2]));

            // Seed the queue with the unique coarse edges, longest first.
            var queue = new Queue<(int a, int b)>();
            var initial = new List<(int a, int b)>();
            var seen = new HashSet<(int, int)>();
            foreach (var (a, b, c) in tris)
            {
                AddInitial(a, b); AddInitial(b, c); AddInitial(c, a);
            }
            void AddInitial(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (seen.Add(key))
                    initial.Add(key);
            }
            initial.Sort((e, f) =>
                Vector3.DistanceSquared(points[f.a], points[f.b])
                    .CompareTo(Vector3.DistanceSquared(points[e.a], points[e.b])));
            foreach (var e in initial)
                queue.Enqueue(e);

            int guard = 0;
            while (queue.Count > 0 && guard++ < 100000)
            {
                var (ea, eb) = queue.Dequeue();
                if (Vector3.Distance(points[ea], points[eb]) <= maxEdgeLength)
                    continue;

                // The edge may have been consumed by an earlier split of a neighbour; only
                // split it while some triangle still owns it.
                bool present = false;
                foreach (var (a, b, c) in tris)
                {
                    if (HasEdge(a, b, c, ea, eb)) { present = true; break; }
                }
                if (!present)
                    continue;

                int mid = points.Count;
                points.Add((points[ea] + points[eb]) * 0.5f);

                for (int t = tris.Count - 1; t >= 0; t--)
                {
                    var (a, b, c) = tris[t];
                    int opposite;
                    // Rotate so the split edge is (a, b) in the triangle's own winding.
                    if ((a == ea && b == eb) || (a == eb && b == ea)) opposite = c;
                    else if ((b == ea && c == eb) || (b == eb && c == ea)) { opposite = a; (a, b, c) = (b, c, a); }
                    else if ((c == ea && a == eb) || (c == eb && a == ea)) { opposite = b; (a, b, c) = (c, a, b); }
                    else continue;

                    tris.RemoveAt(t);
                    tris.Add((a, mid, opposite));
                    tris.Add((mid, b, opposite));
                    queue.Enqueue((mid, opposite));
                }
                queue.Enqueue((ea, mid));
                queue.Enqueue((mid, eb));
            }

            var result = new List<int>(tris.Count * 3);
            foreach (var (a, b, c) in tris)
            {
                result.Add(a); result.Add(b); result.Add(c);
            }
            return result;

            static bool HasEdge(int a, int b, int c, int ea, int eb)
            {
                return (a == ea || b == ea || c == ea) && (a == eb || b == eb || c == eb);
            }
        }

        /// <summary>
        /// Fallback for colinear node layouts: sort along the principal axis and emit a thin
        /// quad strip (two triangles per segment) so the sheet still exists.
        /// </summary>
        static Result BuildColinearStrip(List<Vector3> points, float maxEdgeLength, float halfWidth)
        {
            if (halfWidth <= 0f)
                halfWidth = 0.5f;

            // Principal direction in XZ.
            Vector3 mean = Vector3.Zero;
            foreach (Vector3 p in points)
                mean += p;
            mean /= points.Count;
            float sxx = 0, szz = 0, sxz = 0;
            foreach (Vector3 p in points)
            {
                float dx = p.X - mean.X, dz = p.Z - mean.Z;
                sxx += dx * dx; szz += dz * dz; sxz += dx * dz;
            }
            Vector2 dir = Math.Abs(sxx) >= Math.Abs(szz)
                ? new Vector2(sxx, sxz)
                : new Vector2(sxz, szz);
            if (dir.LengthSquared() < 1e-10f)
                return null;
            dir = Vector2.Normalize(dir);
            var perp = new Vector2(-dir.Y, dir.X);

            var ordered = new List<Vector3>(points);
            ordered.Sort((p, q) =>
                ((p.X - mean.X) * dir.X + (p.Z - mean.Z) * dir.Y)
                .CompareTo((q.X - mean.X) * dir.X + (q.Z - mean.Z) * dir.Y));

            // Subdivide long runs the same way real edges are.
            var line = new List<Vector3> { ordered[0] };
            for (int i = 1; i < ordered.Count; i++)
            {
                AppendSubdivided(line, ordered[i - 1], ordered[i], maxEdgeLength);
                line.Add(ordered[i]);
            }

            var result = new Result();
            foreach (Vector3 p in line)
            {
                result.Vertices.Add(new Vector3(p.X - perp.X * halfWidth, p.Y, p.Z - perp.Y * halfWidth));
                result.Vertices.Add(new Vector3(p.X + perp.X * halfWidth, p.Y, p.Z + perp.Y * halfWidth));
            }
            for (int i = 0; i + 1 < line.Count; i++)
            {
                int a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d = i * 2 + 3;
                AddTriangleCcw(result, a, b, c);
                AddTriangleCcw(result, b, d, c);
            }
            return result.Triangles.Count == 0 ? null : result;
        }

        static void AppendSubdivided(List<Vector3> line, Vector3 a, Vector3 b, float maxEdgeLength)
        {
            if (Vector3.Distance(a, b) <= maxEdgeLength)
                return;
            Vector3 mid = (a + b) * 0.5f;
            AppendSubdivided(line, a, mid, maxEdgeLength);
            line.Add(mid);
            AppendSubdivided(line, mid, b, maxEdgeLength);
        }

        static void AddTriangleCcw(Result result, int a, int b, int c)
        {
            Vector3 pa = result.Vertices[a], pb = result.Vertices[b], pc = result.Vertices[c];
            // Detour convention: positive dtTriArea2D ((c-a).x*(b-a).z - (b-a).x*(c-a).z).
            float area2 = (pc.X - pa.X) * (pb.Z - pa.Z) - (pb.X - pa.X) * (pc.Z - pa.Z);
            if (area2 > 0f)
            {
                result.Triangles.Add(a); result.Triangles.Add(b); result.Triangles.Add(c);
            }
            else
            {
                result.Triangles.Add(a); result.Triangles.Add(c); result.Triangles.Add(b);
            }
        }

        /// <summary>
        /// Bowyer-Watson Delaunay triangulation over XZ (Y is carried, not consulted).
        /// Returns index triples wound with positive Detour 2D area; degenerate (colinear)
        /// inputs return an empty list.
        /// </summary>
        static List<int> DelaunayXZ(List<Vector3> points)
        {
            int n = points.Count;
            var tris = new List<int>();
            if (n < 3)
                return tris;

            // Super-triangle around everything.
            double minX = double.MaxValue, maxX = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                minX = Math.Min(minX, points[i].X); maxX = Math.Max(maxX, points[i].X);
                minZ = Math.Min(minZ, points[i].Z); maxZ = Math.Max(maxZ, points[i].Z);
            }
            double span = Math.Max(maxX - minX, maxZ - minZ);
            if (span <= 0)
                span = 1;
            double cx = (minX + maxX) / 2, cz = (minZ + maxZ) / 2;

            var px = new double[n + 3];
            var pz = new double[n + 3];
            for (int i = 0; i < n; i++)
            {
                px[i] = points[i].X;
                pz[i] = points[i].Z;
            }
            px[n] = cx - span * 20; pz[n] = cz - span * 10;
            px[n + 1] = cx + span * 20; pz[n + 1] = cz - span * 10;
            px[n + 2] = cx; pz[n + 2] = cz + span * 20;

            var triList = new List<(int a, int b, int c)> { (n, n + 1, n + 2) };

            for (int i = 0; i < n; i++)
            {
                var bad = new List<int>();
                for (int t = 0; t < triList.Count; t++)
                {
                    if (InCircumcircle(px, pz, triList[t], i))
                        bad.Add(t);
                }

                // Boundary of the union of bad triangles.
                var edgeCount = new Dictionary<(int, int), int>();
                foreach (int t in bad)
                {
                    var (a, b, c) = triList[t];
                    CountEdge(edgeCount, a, b);
                    CountEdge(edgeCount, b, c);
                    CountEdge(edgeCount, c, a);
                }
                for (int t = bad.Count - 1; t >= 0; t--)
                    triList.RemoveAt(bad[t]);

                foreach (var kv in edgeCount)
                {
                    if (kv.Value != 1)
                        continue;
                    triList.Add((kv.Key.Item1, kv.Key.Item2, i));
                }
            }

            foreach (var (a, b, c) in triList)
            {
                if (a >= n || b >= n || c >= n)
                    continue;
                // Drop degenerate slivers (colinear points on subdivided hull edges).
                double area2 = (px[c] - px[a]) * (pz[b] - pz[a]) - (px[b] - px[a]) * (pz[c] - pz[a]);
                if (Math.Abs(area2) < 1e-6)
                    continue;
                if (area2 > 0)
                {
                    tris.Add(a); tris.Add(b); tris.Add(c);
                }
                else
                {
                    tris.Add(a); tris.Add(c); tris.Add(b);
                }
            }
            return tris;
        }

        static void CountEdge(Dictionary<(int, int), int> edges, int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            edges.TryGetValue(key, out int c);
            edges[key] = c + 1;
        }

        static bool InCircumcircle(double[] px, double[] pz, (int a, int b, int c) tri, int p)
        {
            double ax = px[tri.a], az = pz[tri.a];
            double bx = px[tri.b], bz = pz[tri.b];
            double cx = px[tri.c], cz = pz[tri.c];
            double dx = px[p], dz = pz[p];

            // Standard incircle determinant; orient first so the sign is meaningful.
            double orient = (bx - ax) * (cz - az) - (bz - az) * (cx - ax);
            if (Math.Abs(orient) < 1e-12)
                return false;

            double adx = ax - dx, adz = az - dz;
            double bdx = bx - dx, bdz = bz - dz;
            double cdx = cx - dx, cdz = cz - dz;
            double det =
                (adx * adx + adz * adz) * (bdx * cdz - cdx * bdz)
                - (bdx * bdx + bdz * bdz) * (adx * cdz - cdx * adz)
                + (cdx * cdx + cdz * cdz) * (adx * bdz - bdx * adz);

            return orient > 0 ? det > 0 : det < 0;
        }
    }
}
#endif
