#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CATHODE;
using CathodeLib;

namespace CathodeLib.Radiosity
{
    public static partial class RadiosityBaker
    {
        /// <summary>
        /// Full bakes: move each light slice's items onto the K live input probes of its runtime slice that score best by
        /// -d(probe, nearest emissive triangle of the emitter) + a x (probe normal . emissive normal), within 2.5 m of the
        /// emitter model's bbox centre, with a = <paramref name="facingWeight"/> (<see cref="RadiosityTuning.EmitterSurfacePlacement"/>,
        /// 0.5; a &lt;= 0 places nothing) and no visibility test. Counts, weights and colours are kept; only the items' probes move.
        /// </summary>
        private static void ApplyEmitterSurfacePlacement(RadiosityRuntime runtime, RadiosityGeometry geometry, Level level, float facingWeight, Action<string> log)
        {
            if (level?.Movers == null || facingWeight <= 0) return;
            const float R = 2.5f;
            var emiPts = new Dictionary<int, List<Vector3>>(); var emiN = new Dictionary<int, Vector3>(); var emiA = new Dictionary<int, float>();
            foreach (var inst in geometry.Instances)
                foreach (int tri in inst.Triangles)
                {
                    if (tri >= geometry.TriangleEmissive.Length || geometry.TriangleEmissive[tri] == Vector3.Zero) continue;
                    int slot = tri < geometry.TriangleMoverSlot.Length ? geometry.TriangleMoverSlot[tri] : 0;
                    if (slot < 0 || slot >= inst.Movers.Count) continue;
                    int mv = inst.Movers[slot]; float ar = geometry.TriangleArea(tri);
                    if (!emiPts.TryGetValue(mv, out var l)) emiPts[mv] = l = new List<Vector3>();
                    l.Add(geometry.TriangleCentroid(tri));
                    emiN[mv] = (emiN.TryGetValue(mv, out var n0) ? n0 : Vector3.Zero) + geometry.TriangleNormal(tri) * ar;
                    emiA[mv] = (emiA.TryGetValue(mv, out var a0) ? a0 : 0f) + ar;
                }
            var movesOf = new Dictionary<Resources.Resource, List<int>>();
            for (int k = 0; k < level.Movers.Entries.Count; k++)
            {
                var r0 = level.Movers.Entries[k].Resource; if (r0 == null || !emiPts.ContainsKey(k)) continue;
                if (!movesOf.TryGetValue(r0, out var l0)) movesOf[r0] = l0 = new List<int>(); l0.Add(k);
            }
            Vector3 BoxCentre(int mv)
            {
                var m = level.Movers.Entries[mv]; Vector3 mn = new Vector3(float.MaxValue), mx = new Vector3(float.MinValue); bool any = false;
                if (m.RenderableElements != null) foreach (var e in m.RenderableElements) if (e.Model != null) { mn = Vector3.Min(mn, e.Model.MinBounds); mx = Vector3.Max(mx, e.Model.MaxBounds); any = true; }
                return any ? Vector3.Transform((mn + mx) * 0.5f, m.Transform) : m.Transform.Translation;
            }
            Vector3 Nrm(RadiosityRuntime.RuntimeDataSlice sl, int i)
            {
                if (i >= sl.InputProbeNormals.Count) return Vector3.Zero;
                var nn = sl.InputProbeNormals[i]; var v = new Vector3((nn.B - 127.5f) / 127.5f, (nn.G - 127.5f) / 127.5f, (nn.R - 127.5f) / 127.5f);
                return v.LengthSquared() > 1e-4f ? Vector3.Normalize(v) : Vector3.Zero;
            }
            Vector3 Pos(RadiosityRuntime.RuntimeDataSlice sl, int i) { var q = sl.InputProbePositions[i]; return new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z)); }

            long slicesMoved = 0, itemsMoved = 0, itemsSame = 0, skipped = 0;
            foreach (var sl in runtime.Slices)
            {
                var L = sl.SurfaceLights?.Lights; var LS = sl.SurfaceLights?.LightSlices; var ents = sl.SurfaceLights?.LightSliceEntities;
                if (L == null || LS == null || ents == null) continue;
                var live = new List<int>(); for (int i = 0; i < sl.InputProbePositions.Count; i++) if (sl.InputProbePositions[i].W != 0) live.Add(i);
                var lp = live.Select(i => Pos(sl, i)).ToArray(); var ln = live.Select(i => Nrm(sl, i)).ToArray();
                var grid = new Dictionary<(int, int, int), List<int>>();
                (int, int, int) G(Vector3 p) => ((int)Math.Floor(p.X / R), (int)Math.Floor(p.Y / R), (int)Math.Floor(p.Z / R));
                for (int j = 0; j < lp.Length; j++) { var k = G(lp[j]); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(); l.Add(j); }
                for (int c = 0; c < LS.Count && c < ents.Count; c++)
                {
                    var ls = LS[c]; var res = ents[c];
                    if (ls.NumItems == 0 || res == null || !movesOf.TryGetValue(res, out var movers)) { skipped++; continue; }
                    var order = new List<int>(); var seenP = new HashSet<int>();
                    for (uint it = ls.FirstItem; it < ls.FirstItem + ls.NumItems && it < L.Count; it++) { int pi = L[(int)it].V * 256 + L[(int)it].U; if (seenP.Add(pi)) order.Add(pi); }
                    if (order.Count == 0) { skipped++; continue; }
                    Vector3 ic = Vector3.Zero; int nic = 0; foreach (int pi in order) if (pi < sl.InputProbePositions.Count) { ic += Pos(sl, pi); nic++; }
                    if (nic > 0) ic /= nic;
                    int em = movers.OrderBy(k => Vector3.DistanceSquared(level.Movers.Entries[k].Transform.Translation, ic)).First();
                    Vector3 bc = BoxCentre(em); var pts = emiPts[em];
                    bool hasN = emiN[em].Length() >= 0.3f * emiA[em]; Vector3 ne = hasN ? Vector3.Normalize(emiN[em]) : Vector3.Zero;
                    var cand = new List<(int probe, float score)>(); var g0 = G(bc);
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                        if (grid.TryGetValue((g0.Item1 + dx, g0.Item2 + dy, g0.Item3 + dz), out var l))
                            foreach (int j in l)
                            {
                                if (Vector3.Distance(lp[j], bc) > R) continue;
                                float dS = 3f; foreach (var q in pts) { float d = Vector3.Distance(q, lp[j]); if (d < dS) dS = d; }
                                float cosE = hasN && ln[j] != Vector3.Zero ? Vector3.Dot(ln[j], ne) : 0f;
                                cand.Add((live[j], -dS + facingWeight * cosE));
                            }
                    if (cand.Count == 0) { skipped++; continue; }
                    var pick = cand.OrderByDescending(x => x.score).ThenBy(x => x.probe).Take(order.Count).Select(x => x.probe).ToList();
                    var map = new Dictionary<int, int>();
                    for (int k = 0; k < order.Count && k < pick.Count; k++) map[order[k]] = pick[k];
                    // probes already chosen keep their own items where the new set still contains them
                    var keep = new HashSet<int>(order.Where(p => pick.Contains(p)));
                    var freeNew = new Queue<int>(pick.Where(p => !keep.Contains(p)));
                    map.Clear(); foreach (int p in order) map[p] = keep.Contains(p) ? p : (freeNew.Count > 0 ? freeNew.Dequeue() : p);
                    for (uint it = ls.FirstItem; it < ls.FirstItem + ls.NumItems && it < L.Count; it++)
                    {
                        var l2 = L[(int)it]; int pi = l2.V * 256 + l2.U; int np = map.TryGetValue(pi, out int m2) ? m2 : pi;
                        if (np == pi) { itemsSame++; continue; }
                        l2.U = (byte)(np % 256); l2.V = (byte)(np / 256); L[(int)it] = l2; itemsMoved++;
                    }
                    slicesMoved++;
                }
            }
            log?.Invoke("Radiosity emitter-surface placement (a = " + facingWeight + "): " + slicesMoved + " light slices, " + itemsMoved + " items moved, " + itemsSame + " already in place, " + skipped + " slices skipped (no emissive emitter / candidates)");
        }
    }
}
#endif
