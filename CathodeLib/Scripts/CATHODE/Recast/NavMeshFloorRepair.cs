#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Floor that Recast's single-sided rules leave out although a character can stand on it. Havok collision is
    /// double-sided, so a floor whose triangles are wound downward holds the player up like any other, and a sliver of
    /// steep collision at a threshold (a chamfer, a short ramp between two floor heights) is stepped over like any rise
    /// within the climb. Recast marks both unwalkable, and erosion then widens even a one-cell sliver into a gap of
    /// twice the agent radius - enough to split the rooms either side of a doorway.
    /// </summary>
    public static class NavMeshFloorRepair
    {
        // Rasterised area of a near-flat triangle that faces down, kept apart until it is known to be reachable floor.
        // Below walkable so a span shared with an upward-facing floor stays walkable, above null so it wins over walls.
        const int DownFacingArea = 1;
        // Walkable spans the reachability flood has passed, for the length of the flood.
        const int FloodedArea = RcRecast.RC_WALKABLE_AREA - 1;

        /// <summary>
        /// Rasterise the geometry as Recast does, keeping near-flat downward-facing triangles apart from walls. Those a
        /// character can walk onto from a reachability seed (floor to floor within the climb, with the agent's headroom)
        /// become floor; the rest are ceilings and undersides and stay unwalkable. No seeds, nothing changes.
        /// </summary>
        public static RcHeightfield BuildSolidHeightfield(RcContext ctx, IRcInputGeomProvider geom, RcBuilderConfig bcfg, IList<Vector3> seeds, bool downFacingFloors)
        {
            if (!downFacingFloors || seeds == null || seeds.Count == 0)
                return RcVoxelizations.BuildSolidHeightfield(ctx, geom, bcfg);

            RcConfig cfg = bcfg.cfg;
            RcHeightfield solid = new RcHeightfield(bcfg.width, bcfg.height, bcfg.bmin, bcfg.bmax, cfg.Cs, cfg.Ch, cfg.BorderSize);
            float walkableThr = MathF.Cos(cfg.WalkableSlopeAngle / 180.0f * MathF.PI);
            foreach (RcTriMesh mesh in geom.Meshes())
            {
                float[] verts = mesh.GetVerts();
                int[] tris = mesh.GetTris();
                int nt = tris.Length / 3;
                int[] areas = RcRecast.MarkWalkableTriangles(ctx, cfg.WalkableSlopeAngle, verts, tris, nt, cfg.WalkableAreaMod);
                RcVec3f norm = new RcVec3f();
                for (int i = 0; i < nt; i++)
                {
                    if (areas[i] != RcRecast.RC_NULL_AREA)
                        continue;
                    RcRecast.CalcTriNormal(verts.ToVec3(tris[i * 3] * 3), verts.ToVec3(tris[i * 3 + 1] * 3), verts.ToVec3(tris[i * 3 + 2] * 3), ref norm);
                    if (-norm.Y > walkableThr)
                        areas[i] = DownFacingArea;
                }
                RcRasterizations.RasterizeTriangles(ctx, verts, tris, areas, nt, solid, cfg.WalkableClimb);
            }
            ResolveDownFacing(solid, seeds, cfg.WalkableClimb, cfg.WalkableHeight);
            return solid;
        }

        /// <summary>Flood the floor from the seeds through walkable and down-facing spans; down-facing spans it reaches become walkable.</summary>
        static void ResolveDownFacing(RcHeightfield hf, IList<Vector3> seeds, int climb, int walkableHeight)
        {
            var queue = new Queue<(int x, int z, RcSpan span)>();
            foreach (Vector3 seed in seeds)
            {
                int sx = (int)MathF.Floor((seed.X - hf.bmin.X) / hf.cs), sz = (int)MathF.Floor((seed.Z - hf.bmin.Z) / hf.cs);
                int sy = (int)MathF.Floor((seed.Y - hf.bmin.Y) / hf.ch);
                RcSpan best = null; int bx = 0, bz = 0, bd = int.MaxValue;
                for (int oz = -1; oz <= 1; oz++)
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int cx = sx + ox, cz = sz + oz;
                        if (cx < 0 || cz < 0 || cx >= hf.width || cz >= hf.height)
                            continue;
                        for (RcSpan s = hf.spans[cx + cz * hf.width]; s != null; s = s.next)
                        {
                            int d = Math.Abs(s.smax - sy) + Math.Abs(ox) + Math.Abs(oz);
                            if ((s.area == RcRecast.RC_WALKABLE_AREA || s.area == DownFacingArea) && Math.Abs(s.smax - sy) <= climb + 3 && d < bd)
                            {
                                bd = d; best = s; bx = cx; bz = cz;
                            }
                        }
                    }
                if (best == null)
                    continue;
                best.area = FloodedArea;
                queue.Enqueue((bx, bz, best));
            }
            while (queue.Count > 0)
            {
                var (x, z, s) = queue.Dequeue();
                if (!HasHeadroom(s, walkableHeight))
                    continue;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + RcRecast.GetDirOffsetX(d), nz = z + RcRecast.GetDirOffsetY(d);
                    if (nx < 0 || nz < 0 || nx >= hf.width || nz >= hf.height)
                        continue;
                    for (RcSpan n = hf.spans[nx + nz * hf.width]; n != null; n = n.next)
                    {
                        if ((n.area != RcRecast.RC_WALKABLE_AREA && n.area != DownFacingArea) || Math.Abs(n.smax - s.smax) > climb || !HasHeadroom(n, walkableHeight))
                            continue;
                        n.area = FloodedArea;
                        queue.Enqueue((nx, nz, n));
                    }
                }
            }
            for (int i = 0; i < hf.spans.Length; i++)
                for (RcSpan s = hf.spans[i]; s != null; s = s.next)
                {
                    if (s.area == FloodedArea)
                        s.area = RcRecast.RC_WALKABLE_AREA;
                    else if (s.area == DownFacingArea)
                        s.area = RcRecast.RC_NULL_AREA;
                }
        }

        /// <summary>
        /// Closes cracks running through a floor, no wider than <paramref name="maxCells"/>: from each walkable span, along X
        /// and along Z, the cells up to the next walkable span the floor continues to (its top within the climb of the last).
        /// A cell on the way whose top is unwalkable (a steep or downward-facing sliver) is marked walkable; a cell with nothing
        /// within the climb and nothing in the agent's way above (a seam in the floor, a gap over a lower surface) gets a
        /// walkable span at the floor's height. Runs before the ledge and headroom filters, so a filled span still has to pass those.
        /// </summary>
        public static int FillCracks(RcHeightfield hf, int climb, int walkableHeight, int maxCells)
        {
            if (hf == null || maxCells <= 0)
                return 0;
            var fill = new HashSet<RcSpan>();
            var holes = new Dictionary<(int, int), int>();
            var nulls = new List<RcSpan>();
            var gaps = new List<(int x, int z, int top)>();
            for (int z = 0; z < hf.height; z++)
                for (int x = 0; x < hf.width; x++)
                    for (RcSpan s = hf.spans[x + z * hf.width]; s != null; s = s.next)
                    {
                        if (s.area == RcRecast.RC_NULL_AREA || !HasHeadroom(s, walkableHeight))
                            continue;
                        for (int axis = 0; axis < 2; axis++)
                        {
                            nulls.Clear();
                            gaps.Clear();
                            if (!Crack(hf, x, z, axis == 0 ? 1 : 0, axis == 0 ? 0 : 1, s.smax, climb, walkableHeight, maxCells, nulls, gaps))
                                continue;
                            foreach (RcSpan n in nulls)
                                fill.Add(n);
                            foreach (var g in gaps)
                                holes[(g.x, g.z)] = g.top;
                        }
                    }
            foreach (RcSpan n in fill)
                n.area = RcRecast.RC_WALKABLE_AREA;
            foreach (var h in holes)
                RcRasterizations.AddSpan(hf, h.Key.Item1, h.Key.Item2, Math.Max(0, h.Value - 1), h.Value, RcRecast.RC_WALKABLE_AREA, climb);
            return fill.Count + holes.Count;
        }

        /// <summary>
        /// Steps from a walkable span along one direction through at most maxCells crack cells to the next walkable span; true
        /// when it gets there, with the unwalkable spans and the empty cells it crossed.
        /// </summary>
        static bool Crack(RcHeightfield hf, int x, int z, int dx, int dz, int top, int climb, int walkableHeight, int maxCells,
            List<RcSpan> nulls, List<(int, int, int)> gaps)
        {
            int current = top;
            for (int k = 1; k <= maxCells + 1; k++)
            {
                int cx = x + dx * k, cz = z + dz * k;
                if (cx < 0 || cz < 0 || cx >= hf.width || cz >= hf.height)
                    return false;
                RcSpan next = null;
                int best = int.MaxValue;
                bool blocked = false;
                for (RcSpan n = hf.spans[cx + cz * hf.width]; n != null; n = n.next)
                {
                    int d = Math.Abs(n.smax - current);
                    if (d <= climb && d < best && HasHeadroom(n, walkableHeight))
                    {
                        best = d;
                        next = n;
                    }
                    //Anything reaching into the space a character crosses the gap in
                    if (n.smax > current - climb && n.smin < current + walkableHeight)
                        blocked = true;
                }
                if (next != null)
                {
                    if (next.area != RcRecast.RC_NULL_AREA)
                        return k > 1;
                    nulls.Add(next);
                    current = next.smax;
                    continue;
                }
                if (blocked)
                    return false;
                gaps.Add((cx, cz, current));
            }
            return false;
        }

        static bool HasHeadroom(RcSpan s, int walkableHeight)
        {
            return s.next == null || s.next.smin - s.smax >= walkableHeight;
        }
    }
}
#endif
