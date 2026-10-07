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
        /// Retail light PLACEMENT prior (<see cref="RadiosityBakeSettings.UseRetailLightPlacement"/>). The light priors already
        /// give every entity retail's item count, colour and energy, but our items land on different input probes: only
        /// 13-16% of retail's items have one of ours within 0.3 m (median 0.6 m away), and ~40% sit on probes whose
        /// facing none of our input probes within 0.5 m shares. For every entity retail lights, this replaces our chain(s)
        /// with retail's own items, each moved to our nearest input probe that faces the same way (dot >= 0.5) within the
        /// radius, split across our slices where they land, sibling chains (one slice per emissive state) preserved.
        /// Entities retail does not light keep our items. The live table is rewritten as a copy (both ship that way).
        /// 26 Sep 2026, 13 small levels (file-side test, on top of the retail-rule cut with 8 forced openings):
        /// 10.14 / 10.07 -> 9.89 / 9.91 / 9.86 (mean screenshot RMSE against retail).
        /// </summary>
        private static void ApplyRetailLightPlacement(RadiosityRuntime runtime, List<RadiosityRuntime.RuntimeDataSlice> retailSlices,
                                                      Level level, float radius, Action<string> log)
        {
            if (retailSlices == null || retailSlices.Count == 0 || level?.Resources == null) return;
            Vector3 Nrm(RadiosityRuntime.RuntimeDataSlice sl, int i)
            {
                if (i >= sl.InputProbeNormals.Count) return Vector3.Zero;
                var nn = sl.InputProbeNormals[i]; var v = new Vector3((nn.B - 127.5f) / 127.5f, (nn.G - 127.5f) / 127.5f, (nn.R - 127.5f) / 127.5f);
                return v.LengthSquared() > 1e-4f ? Vector3.Normalize(v) : Vector3.Zero;
            }
            Vector3 Pos(RadiosityRuntime.RuntimeDataSlice sl, int i) { var q = sl.InputProbePositions[i]; return new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z)); }
            IEnumerable<List<int>> Chains(List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice> LS)
            {
                var pointed = new HashSet<int>(); foreach (var x in LS) if (x.SiblingIndex != 0) pointed.Add(x.SiblingIndex);
                var done = new bool[LS.Count];
                for (int h = 0; h < LS.Count; h++)
                {
                    if (done[h] || (pointed.Contains(h) && h != 0)) continue;
                    var chain = new List<int>(); int c = h; var seen = new HashSet<int>();
                    while (c >= 0 && c < LS.Count && seen.Add(c)) { chain.Add(c); done[c] = true; int nx = LS[c].SiblingIndex; if (nx == 0) break; c = nx; }
                    yield return chain;
                }
            }

            // retail chains per entity resource: each state's items with world position and facing
            var retail = new Dictionary<Resources.Resource, List<List<List<(Vector3 p, Vector3 n, RadiosityRuntime.RuntimeSurfaceLights.Light l)>>>>();
            foreach (var rsl in retailSlices)
            {
                var L = rsl.SurfaceLights?.Lights; var LS = rsl.SurfaceLights?.LightSlices;
                if (L == null || LS == null) continue;
                foreach (var chain in Chains(LS))
                {
                    var res = level.Resources.GetAtWriteIndex(LS[chain[0]].EntityInstanceIndex);
                    if (res == null) continue;
                    var states = new List<List<(Vector3, Vector3, RadiosityRuntime.RuntimeSurfaceLights.Light)>>();
                    foreach (int ci in chain)
                    {
                        var items = new List<(Vector3, Vector3, RadiosityRuntime.RuntimeSurfaceLights.Light)>();
                        for (uint it = LS[ci].FirstItem; it < LS[ci].FirstItem + LS[ci].NumItems && it < L.Count; it++)
                        {
                            var l = L[(int)it]; int i = l.V * 256 + l.U; if (i >= rsl.InputProbePositions.Count) continue;
                            items.Add((Pos(rsl, i), Nrm(rsl, i), l));
                        }
                        states.Add(items);
                    }
                    if (!retail.TryGetValue(res, out var list)) retail[res] = list = new List<List<List<(Vector3, Vector3, RadiosityRuntime.RuntimeSurfaceLights.Light)>>>();
                    list.Add(states);
                }
            }
            if (retail.Count == 0) { log?.Invoke("Radiosity light placement: no retail light items"); return; }

            // our live input probes on a grid
            int S = runtime.Slices.Count;
            var grid = new Dictionary<(int, int, int), List<(int s, int i, Vector3 p, Vector3 n)>>();
            (int, int, int) K(Vector3 p) => ((int)Math.Floor(p.X / radius), (int)Math.Floor(p.Y / radius), (int)Math.Floor(p.Z / radius));
            for (int s = 0; s < S; s++)
            {
                var sl = runtime.Slices[s];
                for (int i = 0; i < sl.InputProbePositions.Count; i++)
                {
                    if (sl.InputProbePositions[i].W == 0) continue;
                    var p = Pos(sl, i); var k = K(p);
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<(int, int, Vector3, Vector3)>();
                    l.Add((s, i, p, Nrm(sl, i)));
                }
            }
            (int s, int i) Nearest(Vector3 p, Vector3 n)
            {
                var k0 = K(p); (int, int) best = (-1, -1); float bd = radius * radius;
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                    if (grid.TryGetValue((k0.Item1 + dx, k0.Item2 + dy, k0.Item3 + dz), out var l))
                        foreach (var e in l)
                        {
                            if (n != Vector3.Zero && e.n != Vector3.Zero && Vector3.Dot(e.n, n) < 0.5f) continue;
                            float d2 = Vector3.DistanceSquared(e.p, p); if (d2 < bd) { bd = d2; best = (e.s, e.i); }
                        }
                return best;
            }

            var newL = new List<RadiosityRuntime.RuntimeSurfaceLights.Light>[S];
            var newLS = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>[S];
            var newE = new List<Resources.Resource>[S];
            for (int s = 0; s < S; s++) { newL[s] = new List<RadiosityRuntime.RuntimeSurfaceLights.Light>(); newLS[s] = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>(); newE[s] = new List<Resources.Resource>(); }
            void Emit(int s, Resources.Resource res, int entityIndex, List<List<RadiosityRuntime.RuntimeSurfaceLights.Light>> states)
            {
                int baseIdx = newLS[s].Count;
                for (int k = 0; k < states.Count; k++)
                {
                    uint first = (uint)newL[s].Count; newL[s].AddRange(states[k]);
                    newLS[s].Add(new RadiosityRuntime.RuntimeSurfaceLights.LightSlice
                    {
                        FirstItem = first, NumItems = (ushort)states[k].Count, EntityInstanceIndex = entityIndex,
                        SiblingIndex = (ushort)(k + 1 < states.Count ? baseIdx + k + 1 : 0)
                    });
                    newE[s].Add(res);
                }
            }
            // 1. our chains for entities retail does not light, kept as they are
            int keptChains = 0;
            for (int s = 0; s < S; s++)
            {
                var sl = runtime.Slices[s]; var L = sl.SurfaceLights?.Lights; var LS = sl.SurfaceLights?.LightSlices; var ents = sl.SurfaceLights?.LightSliceEntities;
                if (L == null || LS == null) continue;
                foreach (var chain in Chains(LS))
                {
                    var res = ents != null && chain[0] < ents.Count ? ents[chain[0]] : null;
                    if (res != null && retail.ContainsKey(res)) continue;
                    var states = chain.Select(ci => Enumerable.Range((int)LS[ci].FirstItem, LS[ci].NumItems).Where(it => it < L.Count).Select(it => L[it]).ToList()).ToList();
                    Emit(s, res, LS[chain[0]].EntityInstanceIndex, states); keptChains++;
                }
            }
            // 2. retail's chains, items moved onto our facing-matched input probes and split by the slice they land in
            long moved = 0, dropped = 0; int retailChains = 0;
            foreach (var kv in retail)
                foreach (var states in kv.Value)
                {
                    var perSlice = new Dictionary<int, List<List<RadiosityRuntime.RuntimeSurfaceLights.Light>>>();
                    for (int k = 0; k < states.Count; k++)
                        foreach (var (p, n, l0) in states[k])
                        {
                            var (ts, ti) = Nearest(p, n); if (ts < 0) { dropped++; continue; }
                            if (!perSlice.TryGetValue(ts, out var st)) { st = new List<List<RadiosityRuntime.RuntimeSurfaceLights.Light>>(); for (int q = 0; q < states.Count; q++) st.Add(new List<RadiosityRuntime.RuntimeSurfaceLights.Light>()); perSlice[ts] = st; }
                            var l = l0; l.U = (byte)(ti % 256); l.V = (byte)(ti / 256); st[k].Add(l); moved++;
                        }
                    foreach (var ps in perSlice) { Emit(ps.Key, kv.Key, -1, ps.Value); retailChains++; }
                }
            for (int s = 0; s < S; s++)
            {
                var sl = runtime.Slices[s];
                sl.SurfaceLights.Lights = newL[s]; sl.SurfaceLights.LightSlices = newLS[s]; sl.SurfaceLights.LightSliceEntities = newE[s];
                sl.LiveSurfaceLights = new List<RadiosityRuntime.RuntimeSurfaceLights.LightSlice>(newLS[s]);
                sl.LiveSurfaceLightEntities = new List<Resources.Resource>(newE[s]);
            }
            log?.Invoke("Radiosity light placement: " + retail.Count + " retail-lit entities placed at retail's items (" + moved + " items in " + retailChains + " chains, " + dropped + " without a facing probe within " + radius + " m); " + keptChains + " of our chains kept for entities retail does not light");
        }
        /// <summary>
        /// <see cref="RadiosityBakeSettings.EmitterSurfacePlacement"/> = a &gt; 0 (the validated full-bake profile uses 0.5): with no retail bake to take placement
        /// from, move each light slice's items onto the K live input probes of its runtime slice that score best by
        /// -d(probe, nearest emissive triangle of the emitter) + a x (probe normal . emissive normal), within 2.5 m of the emitter
        /// model's bbox centre; no visibility test. Measured 1 Oct (`placelearn`, retail placement on our scaffold as labels,
        /// 6 levels): a = 0.5 reproduces 48.8% of retail placement's probes exactly, a logistic model over 16 features 48.2-48.9%,
        /// the bbox-centre rule 40-41%, our own sampler 32.0%; requiring visibility from the emissive centroid (as our lost-emitter
        /// pass does) costs ~7 points. Counts, weights and colours are kept; only the items' probes move.
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
