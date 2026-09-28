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
    }
}
#endif
