#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using CATHODE;

namespace CathodeLib.Radiosity
{
    public static partial class RadiosityBaker
    {
        /// <summary>
        /// Rebuild a finished runtime's cluster stage and every influence link as retail structures them:
        /// <list type="bullet">
        /// <item>the tree is a bottom-up greedy merge truncated to 8 levels (<see cref="BuildCutTree"/>);</item>
        /// <item>each surface probe gets ONE disjoint 32-link cut over its own slice's tree and its neighbour slices';
        /// links into another slice are the cross-slice fixups, written first (slots 0..nFix-1), and the same cluster
        /// reached either way gets the same byte;</item>
        /// <item>nodes whose members do not face the receiver are dropped, nodes where fewer than half do are peeled,
        /// then the frontier is refined nearest-first by W (1 + cosE) / d^3 to 32 links;</item>
        /// <item>hybrid visibility: a probe whose occluded share of W/d^2 gain lies between RadiosityTuning.CutHybridLeak
        /// and CutHybridLeakMax is cut again with the visibility test;</item>
        /// <item>CutForcedOpenings (8) openings of the highest-priority patch, the weakest links dropping;</item>
        /// <item>bytes by the retail law, distance floored at CutByteNearFloor (2 m), then the gain envelope over the
        /// whole row (fixups included);</item>
        /// <item>only referenced clusters are kept, then slots, scatter (by input probe) and the fixup list and ranges
        /// are rewritten.</item>
        /// </list>
        /// </summary>
        /// <returns>The in-slice links written over every slice (the cross-slice fixups are <c>rt.InfluenceFixups</c>).</returns>
        internal static int RebuildLinksRetailCut(RadiosityRuntime rt, Func<Vector3, Vector3, bool> visible, Action<string> log)
        {
            int per = InfluencesPerProbe;
            const double lg = 22.6096401 / 255.0;
            int wtop = RadiosityTuning.CutMaxMembers;
            float rmax = RadiosityTuning.CutMaxDistance;
            double med = RadiosityTuning.CutGainMedian, cap = RadiosityTuning.CutGainCap;
            const float theta = 0.5f;
            double hybridT = RadiosityTuning.CutHybridLeak;
            double hybridMax = RadiosityTuning.CutHybridLeakMax;
            float recvTol = RadiosityTuning.CutReceiverTolerance;
            float nearFloor = RadiosityTuning.CutByteNearFloor;
            int forcedOpen = RadiosityTuning.CutForcedOpenings;
            float recvOfs = RadiosityTuning.CutVisOffset, membOfs = RadiosityTuning.CutVisOffset;
            long hybridSwitched = 0;
            int S = rt.Slices.Count;

            // Each tree reads only its own slice, so they build in parallel. A failure surfaces as the serial loop's would:
            // the lowest failing slice's exception, unwrapped (Parallel.For alone would wrap it in an AggregateException).
            var trees = new CutTree[S];
            var treeFailures = new Exception[S];
            Parallel.For(0, S, s =>
            {
                try { trees[s] = BuildCutTree(rt.Slices[s], wtop, RadiosityTuning.CutMergeNormalWeight); }
                catch (Exception e) { treeFailures[s] = e; }
            });
            foreach (Exception e in treeFailures)
                if (e != null) ExceptionDispatchInfo.Capture(e).Throw();

            // input probe geometry per slice
            var ipos = new Vector3[S][]; var inrm = new Vector3[S][];
            for (int s = 0; s < S; s++)
            {
                var sl = rt.Slices[s]; int ni = sl.InputProbePositions.Count;
                ipos[s] = new Vector3[ni]; inrm[s] = new Vector3[ni];
                for (int i = 0; i < ni; i++) { var q = sl.InputProbePositions[i]; ipos[s][i] = new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z)); inrm[s][i] = CutNormal(sl, i); }
            }
            // (slice, input probe) -> one flat index, for the per-receiver visibility memo
            var ioff = new int[S + 1];
            for (int s = 0; s < S; s++) ioff[s + 1] = ioff[s] + ipos[s].Length;
            // neighbour slices (receiving slice s -> other slices, in the neighbour table's order)
            var neigh = new List<int>[S];
            for (int s = 0; s < S; s++)
            {
                neigh[s] = new List<int>();
                if (s < rt.SliceNeighbourCounts.Count && s < rt.SliceNeighbourArrayOffsets.Count)
                {
                    int off = rt.SliceNeighbourArrayOffsets[s];
                    for (int n = 0; n < rt.SliceNeighbourCounts[s] && off + n < rt.FlattenedOtherSliceIndices.Count; n++) neigh[s].Add(rt.FlattenedOtherSliceIndices[off + n]);
                }
            }

            double Gain(int b) => b <= 0 ? 0 : Math.Pow(2, b * lg) * 1.48436e-7 - 1.48437e-7;
            var rows = new List<(int t, int node, int b, int w)>[S][];
            for (int s = 0; s < S; s++)
            {
                var sl = rt.Slices[s]; int np = sl.SurfaceProbePositions.Count, ni = sl.InputProbePositions.Count;
                var rn = new Vector3[np];
                for (int i = 0; i < ni; i++)
                {
                    if (sl.InputProbePositions[i].W == 0 || i >= sl.MangleMap.Count) continue;
                    var m = sl.MangleMap[i];
                    if (m.B == 255 && m.A == 63) continue;
                    int tx = m.A * AtlasSize + m.B;
                    if (tx >= sl.MangleMap.Count) continue;
                    int sp = sl.MangleMap[tx].G * 256 + sl.MangleMap[tx].R;
                    if (sp < np) rn[sp] += inrm[s][i];
                }
                var cand = new List<int> { s }; cand.AddRange(neigh[s]);
                rows[s] = new List<(int, int, int, int)>[np];
                var sRows = rows[s];
                Parallel.For(0, np, () => new CutScratch(ioff[S]), (p, loop, scratch) =>
                {
                    var P4 = sl.SurfaceProbePositions[p];
                    if (P4.W == 0) return scratch;
                    int stamp = ++scratch.Stamp;   // a new receiver: every memo entry stamped before it is stale
                    var P = new Vector3(P4.X, P4.Y, P4.Z);
                    Vector3 rnp = rn[p].LengthSquared() > 1e-6f ? Vector3.Normalize(rn[p]) : Vector3.Zero;
                    bool useVis = false;   // hybrid: first pass without visibility
                    // member validity: the member faces the receiver (and the receiver does not face away)
                    bool Valid(int t, int i)
                    {
                        Vector3 d = P - ipos[t][i]; float len = d.Length(); if (len < 1e-3f || len > rmax) return false;
                        if (Vector3.Dot(inrm[t][i], d) <= 0) return false;
                        if (rnp != Vector3.Zero && Vector3.Dot(rnp, -d) < -recvTol * len) return false;
                        return !useVis || Sees(t, i);
                    }
                    // members are re-sampled by every node on the way down, so remember each (slice, input probe) answer for
                    // this receiver (the memo is only ever read back by the same key)
                    bool Sees(int t, int i)
                    {
                        int g = ioff[t] + i;
                        if (scratch.SeenStamp[g] == stamp) return scratch.SeenValue[g];
                        bool b = visible(P + (rnp == Vector3.Zero ? Vector3.Zero : rnp * recvOfs), ipos[t][i] + inrm[t][i] * membOfs);
                        scratch.SeenStamp[g] = stamp; scratch.SeenValue[g] = b; return b;
                    }
                    // true when the visibility test reaches fewer than half of a patch's valid (facing) sampled members; a
                    // patch with none reads as reached. The validity test needs no ray, so the members are counted first and
                    // the rays stop as soon as the majority is decided ((float)ok / tot < 0.5f is exactly 2 * ok < tot)
                    bool MostlyOccluded(int t, CutNode n)
                    {
                        int step = Math.Max(1, n.M.Length / 12), tot = 0;
                        int[] members = scratch.Members((n.M.Length + step - 1) / step);
                        for (int k = 0; k < n.M.Length; k += step) if (Valid(t, n.M[k])) members[tot++] = n.M[k];
                        int ok = 0;
                        for (int j = 0; j < tot && 2 * ok < tot && 2 * (ok + tot - j) >= tot; j++)
                            if (Sees(t, members[j])) ok++;
                        return 2 * ok < tot;
                    }
                    float V(int t, CutNode n)
                    {
                        int step = Math.Max(1, n.M.Length / 12), ok = 0, tot = 0;
                        for (int k = 0; k < n.M.Length; k += step) { tot++; if (Valid(t, n.M[k])) ok++; }
                        return tot > 0 ? (float)ok / tot : 0;
                    }
                    var frontier = new List<(int t, int id, float v)>();
                    void Add(int t, int id)
                    {
                        var n = trees[t].Nodes[id];
                        if (n.M.Length == 0 || (n.C - P).Length() - n.Ext > rmax) return;
                        float v = V(t, n);
                        if (v <= 0) return;
                        if (v < theta && n.L >= 0 && n.R >= 0) { Add(t, n.L); Add(t, n.R); return; }   // peel
                        frontier.Add((t, id, v));
                    }
                    foreach (int t in cand) foreach (int top in trees[t].Tops) Add(t, top);
                    double Prio(int t, int id)
                    {
                        var n = trees[t].Nodes[id]; Vector3 dv = n.C - P; float d = Math.Max(0.1f, dv.Length());
                        return n.M.Length * (1 + Vector3.Dot(n.N, -dv / d)) / (d * d * d);
                    }
                    Refine();
                    void Refine()
                    {
                        int g2 = 0;
                        while (frontier.Count < per && g2++ < 512)
                        {
                            int best = -1; double bp = 0;
                            for (int a = 0; a < frontier.Count; a++)
                            {
                                var n = trees[frontier[a].t].Nodes[frontier[a].id];
                                if (n.L < 0 || n.R < 0) continue;
                                double pr = Prio(frontier[a].t, frontier[a].id); if (pr > bp) { bp = pr; best = a; }
                            }
                            if (best < 0) break;
                            var f = frontier[best]; var nd = trees[f.t].Nodes[f.id];
                            frontier.RemoveAt(best);
                            int before = frontier.Count;
                            Add(f.t, nd.L); Add(f.t, nd.R);
                            if (frontier.Count > per) { frontier.RemoveRange(before, frontier.Count - before); frontier.Add(f); break; }   // opening would overflow: keep the patch
                        }
                    }
                    // HYBRID: keep the no-visibility cut unless most of its gain (W/d^2) sits on links the ray says are
                    // occluded (a probe next to a wall with a lit room behind it) - then redo this probe's cut with visibility.
                    // The test is gs > 0 && hybridT < gl / gs < hybridMax, with gs the gain of every link and gl that of the
                    // occluded ones, each summed in frontier order. gs needs no ray. gl is bracketed instead of summed: L sums
                    // the links found occluded so far and U adds every link not yet tested, each in frontier order too, so
                    // L <= gl <= U holds for the rounded sums as well (every w >= 0 and rounding is monotone; L and gl differ
                    // only by dropped terms, gl and U likewise), and so does L / gs <= gl / gs <= U / gs. Links are tested
                    // heaviest first and testing stops once the bracket decides; with every link tested, L == U == gl.
                    int nf = frontier.Count;
                    var hybridW = new double[nf]; var hybridOcc = new sbyte[nf]; var hybridOrder = new int[nf];
                    double gs = 0;
                    for (int a = 0; a < nf; a++)
                    {
                        var n = trees[frontier[a].t].Nodes[frontier[a].id]; float d = Math.Max(0.1f, (n.C - P).Length()); double w = n.M.Length / (d * d);
                        gs += w; hybridW[a] = w; hybridOcc[a] = -1; hybridOrder[a] = a;
                    }
                    bool recut = false;
                    if (gs > 0)
                    {
                        Array.Sort(hybridOrder, Comparer<int>.Create((x, y) => hybridW[x] != hybridW[y] ? hybridW[y].CompareTo(hybridW[x]) : x.CompareTo(y)));
                        double lo = 0, hi = gs;   // running estimates of L and U: the bracket is summed only when they look decisive
                        for (int k = 0; ; k++)
                        {
                            if (k == nf || lo / gs >= hybridMax || hi / gs <= hybridT || (lo / gs > hybridT && hi / gs < hybridMax))
                            {
                                double L = 0, U = 0;
                                for (int a = 0; a < nf; a++) { if (hybridOcc[a] == 1) L += hybridW[a]; if (hybridOcc[a] != 0) U += hybridW[a]; }
                                if (k == nf) { recut = L / gs > hybridT && L / gs < hybridMax; break; }   // L == U == gl
                                if (L / gs >= hybridMax || U / gs <= hybridT) break;
                                if (L / gs > hybridT && U / gs < hybridMax) { recut = true; break; }
                            }
                            int e = hybridOrder[k];
                            bool occ = MostlyOccluded(frontier[e].t, trees[frontier[e].t].Nodes[frontier[e].id]);
                            hybridOcc[e] = (sbyte)(occ ? 1 : 0);
                            if (occ) lo += hybridW[e]; else hi -= hybridW[e];
                        }
                    }
                    if (recut)
                    {
                        useVis = true; frontier.Clear();
                        foreach (int t in cand) foreach (int top in trees[t].Tops) Add(t, top);
                        Refine();
                        Interlocked.Increment(ref hybridSwitched);
                    }
                    var row = new List<(int t, int node, int b, int w, double g)>(frontier.Count);
                    (int t, int node, int b, int w, double g) Link(int t, int id, float v)
                    {
                        var n = trees[t].Nodes[id]; Vector3 dv = n.C - P; float d = Math.Max(0.1f, dv.Length()); Vector3 dir = dv / d;
                        float ce = Math.Max(0, Vector3.Dot(n.N, -dir)), cr = rnp == Vector3.Zero ? 0.5f : Math.Max(0, Vector3.Dot(rnp, dir));
                        double L = Math.Log(Math.Max(d, nearFloor), 2), w = Math.Log(n.M.Length, 2);
                        double byt = 178.4 - 17.9 * L - 3.0 * L * L - 2.4 * w - 1.03 * w * w + 3.09 * L * w + 26.3 * ce + 10.6 * cr;
                        int b = (int)Math.Round(Math.Max(1, Math.Min(255, byt)));
                        return (t, id, b, n.M.Length, Math.Pow(2, b * lg) * n.M.Length * v);
                    }
                    foreach (var f in frontier) row.Add(Link(f.t, f.id, f.v));
                    if (row.Count > per) { row.Sort((x, y) => y.g.CompareTo(x.g)); row.RemoveRange(per, row.Count - per); }
                    // forced openings: open the highest-priority patch (W(1+cosE)/d^3) this many times, keeping the budget by
                    // dropping the weakest links - a partial step from the coarse cut toward retail's finer near field
                    for (int fo = 0; fo < forcedOpen; fo++)
                    {
                        int best = -1; double bp = 0;
                        for (int a = 0; a < row.Count; a++)
                        {
                            var n = trees[row[a].t].Nodes[row[a].node];
                            if (n.L < 0 || n.R < 0) continue;
                            double pr = Prio(row[a].t, row[a].node);
                            if (pr > bp) { bp = pr; best = a; }
                        }
                        if (best < 0) break;
                        var o = row[best]; var on = trees[o.t].Nodes[o.node];
                        row.RemoveAt(best);
                        var keep = frontier; frontier = new List<(int t, int id, float v)>();
                        Add(o.t, on.L); Add(o.t, on.R);
                        foreach (var k in frontier) row.Add(Link(k.t, k.id, k.v));
                        frontier = keep;
                        if (row.Count > per) { row.Sort((x, y) => y.g.CompareTo(x.g)); row.RemoveRange(per, row.Count - per); }
                    }
                    sRows[p] = row.Select(x => (x.t, x.node, x.b, x.w)).ToList();
                    return scratch;
                }, scratch => { });
            }

            log?.Invoke("    retail cut (hybrid): " + hybridSwitched + " probes re-cut with visibility (leak fraction > " + hybridT + ")");

            // gain envelope over whole rows (fixups included): slice median to the target, per-probe cap
            for (int s = 0; s < S; s++)
            {
                var gs = new List<double>();
                foreach (var r in rows[s]) if (r != null && r.Count > 0) { double g = 0; foreach (var x in r) g += Gain(x.b) * x.w; gs.Add(g); }
                gs.Sort();
                double shift = med > 0 && gs.Count > 0 && gs[gs.Count / 2] > 0 ? Math.Log(med / gs[gs.Count / 2], 2) / lg : 0;
                foreach (var r in rows[s])
                {
                    if (r == null || r.Count == 0) continue;
                    for (int k = 0; k < r.Count; k++) r[k] = (r[k].t, r[k].node, (int)Math.Round(Math.Max(1, Math.Min(255, r[k].b + shift))), r[k].w);
                    double g = 0; foreach (var x in r) g += Gain(x.b) * x.w;
                    if (cap > 0 && g > cap) { double sh = Math.Log(cap / g, 2) / lg; for (int k = 0; k < r.Count; k++) r[k] = (r[k].t, r[k].node, (int)Math.Max(1, Math.Min(255, Math.Floor(r[k].b + sh))), r[k].w); }
                }
                log?.Invoke("    retail cut, slice " + s + ": gain shift " + shift.ToString("0.0") + " bytes");
            }

            // keep referenced clusters only, assign slots, positions, scatter (by input probe)
            var slotOf = new Dictionary<int, int>[S];
            for (int t = 0; t < S; t++)
            {
                var sl = rt.Slices[t];
                var refCount = new Dictionary<int, int>();
                for (int s = 0; s < S; s++) foreach (var r in rows[s]) if (r != null) foreach (var x in r) if (x.t == t) refCount[x.node] = (refCount.TryGetValue(x.node, out var c) ? c : 0) + 1;
                int nc = sl.ClusterPositions.Count;
                var free = new List<int>();
                free.AddRange(CutFreeSlots(nc));
                slotOf[t] = new Dictionary<int, int>();
                int fi = 0, dropped = 0;
                foreach (var kv in refCount.OrderByDescending(k => k.Value)) { if (fi < free.Count) slotOf[t][kv.Key] = free[fi++]; else dropped++; }
                for (int i = 0; i < nc; i++) sl.ClusterPositions[i] = new Vector4u16();
                // The scatter is cluster MEMBERSHIP: one entry per (cluster, member input probe), and the engine SUMS a
                // cluster's listed members into it (16 Sep 2026: a retail W=1 cluster has exactly one entry; a probe is in
                // 1-8 clusters). Naming each cluster ~4.6 times saturated ChallengeMap7 white; W is not a divisor.
                var scatter = new List<ColourRGBA8>(); var perProbe = new int[sl.InputProbePositions.Count]; int maxPer = 0;
                foreach (var kv in slotOf[t])
                {
                    var n = trees[t].Nodes[kv.Key]; int slot = kv.Value;
                    sl.ClusterPositions[slot] = ToHalf4(n.C, n.M.Length);
                    foreach (int i in n.M)
                    {
                        scatter.Add(new ColourRGBA8 { R = (byte)(slot % 256), G = (byte)(slot / 256), B = (byte)(i % 256), A = (byte)(i / 256) });
                        if (++perProbe[i] > maxPer) maxPer = perProbe[i];
                    }
                }
                // Ordered by input probe, as retail's (95%) and every earlier bake of ours (100%) are - the engine appears to walk the
                // scatter per input-probe tile; a cluster-ordered list rendered every rebuilt cluster stage dark.
                scatter.Sort((a, b) => { int pa = a.A * 256 + a.B, pb = b.A * 256 + b.B; if (pa != pb) return pa.CompareTo(pb); return (a.G * 256 + a.R).CompareTo(b.G * 256 + b.R); });
                sl.Scatter = scatter;
                log?.Invoke("    retail cut, slice " + t + ": " + slotOf[t].Count + " referenced clusters kept of " + trees[t].Nodes.Count(n => n.Cluster) +
                            (dropped > 0 ? " (" + dropped + " dropped: no free slot)" : "") + ", memberships/probe max " + maxPer + ", scatter " + scatter.Count);
            }

            // write rows: fixups first (slots 0..nFix-1), in-slice links after; rebuild the fixup list and ranges
            var fixByPair = new Dictionary<(int s, int o), List<RadiosityRuntime.RuntimeInfluenceFixup>>();
            long allLinks = 0;
            for (int s = 0; s < S; s++)
            {
                var sl = rt.Slices[s];
                for (int i = 0; i < sl.SurfaceProbeWeights.Count; i++) sl.SurfaceProbeWeights[i] = new Vector4u8();
                for (int i = 0; i < sl.SurfaceProbeInfluences.Count; i++) sl.SurfaceProbeInfluences[i] = new ColourRGBA8();
                long links = 0, fx = 0; int full = 0, live = 0;
                for (int p = 0; p < rows[s].Length; p++)
                {
                    if (sl.SurfaceProbePositions[p].W == 0) continue;
                    live++;
                    var r = rows[s][p]; if (r == null) continue;
                    int k = 0;
                    foreach (var x in r.Where(x => x.t != s))
                    {
                        if (!slotOf[x.t].TryGetValue(x.node, out int slot)) continue;
                        var f = new RadiosityRuntime.RuntimeInfluenceFixup { WeightTexOffset = p * per + k, InflTexOffset = (p * per + k) * 2, Weight = (byte)x.b, Padding = 0, ClusterTex = new Vector2u8 { X = (byte)(slot % 256), Y = (byte)(slot / 256) } };
                        if (!fixByPair.TryGetValue((s, x.t), out var l)) fixByPair[(s, x.t)] = l = new List<RadiosityRuntime.RuntimeInfluenceFixup>();
                        l.Add(f); k++; fx++;
                    }
                    foreach (var x in r.Where(x => x.t == s))
                    {
                        if (!slotOf[s].TryGetValue(x.node, out int slot) || k >= per) continue;
                        WriteInfluence(sl, p * per + k, (byte)(slot % 256), (byte)(slot / 256), (byte)x.b); k++; links++;
                    }
                    if (k >= per - 2) full++;
                }
                log?.Invoke("    retail cut, slice " + s + ": " + links + " in-slice links + " + fx + " fixups over " + live + " probes (" + (live > 0 ? (double)(links + fx) / live : 0).ToString("0.0") + "/probe, " + full + " full)");
                allLinks += links;
            }
            var newFix = new List<RadiosityRuntime.RuntimeInfluenceFixup>();
            for (int s = 0; s < S && s < rt.SliceNeighbourArrayOffsets.Count; s++)
            {
                int off = rt.SliceNeighbourArrayOffsets[s];
                for (int n = 0; n < neigh[s].Count && off + n < rt.FlattenedFixupRanges.Count; n++)
                {
                    int o = neigh[s][n];
                    var range = rt.FlattenedFixupRanges[off + n];
                    range.First = newFix.Count;
                    if (fixByPair.TryGetValue((s, o), out var l)) newFix.AddRange(l.OrderBy(f => f.WeightTexOffset));
                    range.Num = newFix.Count - range.First;
                    rt.FlattenedFixupRanges[off + n] = range;
                }
            }
            rt.InfluenceFixups = newFix;
            log?.Invoke("    retail cut: " + newFix.Count + " cross-slice fixups written (joint 32-link cut)");
            return (int)allLinks;
        }

        /// <summary>
        /// Free cluster slots (the engine-owned 16x16 atlas corner excluded). Tile-major order fills one 16x16 probe
        /// tile of the 256x64 cluster texture before the next, so N clusters span ceil(N / 256) tiles instead of all 64.
        /// The corner test reads the slot index in 128-wide atlas coordinates (i % AtlasSize, i / AtlasSize), as the rest of
        /// the baker treats cluster indices, so in the 256-wide texture it excludes two 16x8 blocks (x 0-15 and 128-143,
        /// y 0-7) rather than one 16x16 block. Changing it changes slot assignment and the output.
        /// </summary>
        private static List<int> CutFreeSlots(int nc)
        {
            var free = new List<int>();
            for (int t = 0; t < MaxInputProbeTiles; t++)
            {
                int x0 = (t / TileRows) * TileSize, y0 = (t % TileRows) * TileSize;
                for (int y = y0; y < y0 + TileSize; y++)
                    for (int x = x0; x < x0 + TileSize; x++)
                    {
                        int i = y * ProbeTexWidth + x;
                        if (i >= nc) continue;
                        int ax = i % AtlasSize, ay = i / AtlasSize;
                        if (ax < 16 && ay < 16) continue;
                        free.Add(i);
                    }
            }
            return free;
        }

        /// <summary>
        /// One cut worker's scratch, reused across the receivers it takes: the visibility memo (an entry is current only
        /// while its stamp is the receiver's) and the sampled-member buffer.
        /// </summary>
        private sealed class CutScratch
        {
            public readonly int[] SeenStamp;
            public readonly bool[] SeenValue;
            public int Stamp;
            private int[] _members = new int[24];
            public CutScratch(int inputProbes) { SeenStamp = new int[inputProbes]; SeenValue = new bool[inputProbes]; }
            public int[] Members(int count) => _members.Length >= count ? _members : (_members = new int[count]);
        }

        /// <summary>Binary min-heap of (cost, a, b) merge candidates (netstandard2.0 has no PriorityQueue).</summary>
        private sealed class CutHeap
        {
            private readonly List<(double c, int a, int b)> _h = new List<(double c, int a, int b)>();
            public int Count => _h.Count;
            public void Push(double c, int a, int b)
            {
                _h.Add((c, a, b)); int i = _h.Count - 1;
                while (i > 0) { int p = (i - 1) / 2; if (_h[p].c <= _h[i].c) break; var t = _h[p]; _h[p] = _h[i]; _h[i] = t; i = p; }
            }
            public (double c, int a, int b) Pop()
            {
                var top = _h[0]; var last = _h[_h.Count - 1]; _h.RemoveAt(_h.Count - 1);
                if (_h.Count > 0)
                {
                    _h[0] = last; int i = 0;
                    while (true)
                    {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        if (l < _h.Count && _h[l].c < _h[m].c) m = l;
                        if (r < _h.Count && _h[r].c < _h[m].c) m = r;
                        if (m == i) break;
                        var t = _h[m]; _h[m] = _h[i]; _h[i] = t; i = m;
                    }
                }
                return top;
            }
        }

        private sealed class CutNode
        {
            public int[] M;
            public int L = -1, R = -1, Parent = -1;
            public Vector3 C, N;
            public float Ext;
            public bool Cluster, Top;
        }

        private sealed class CutTree
        {
            public readonly List<CutNode> Nodes = new List<CutNode>();
            public readonly List<int> Tops = new List<int>();
        }

        private static Vector3 CutNormal(RadiosityRuntime.RuntimeDataSlice sl, int i)
        {
            if (i >= sl.InputProbeNormals.Count) return Vector3.UnitY;
            var nn = sl.InputProbeNormals[i];
            var v = new Vector3((nn.B - 127.5f) / 127.5f, (nn.G - 127.5f) / 127.5f, (nn.R - 127.5f) / 127.5f);
            return v.LengthSquared() > 1e-4f ? Vector3.Normalize(v) : Vector3.UnitY;
        }

        private static CutTree BuildCutTree(RadiosityRuntime.RuntimeDataSlice sl, int wtop, float mergeNormalWeight)
        {
            var tree = new CutTree();
            int ni = sl.InputProbePositions.Count;
            var pos = new Vector3[ni]; var nrm = new Vector3[ni]; var live = new List<int>();
            for (int i = 0; i < ni; i++)
            {
                var q = sl.InputProbePositions[i];
                pos[i] = new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z));
                nrm[i] = CutNormal(sl, i);
                if (q.W != 0) live.Add(i);
            }
            if (live.Count > 0)
            {
                // Retail's tree (decoded 24 Sep 2026): ONE global greedy merge of the cheapest adjacent pair (24-nearest-
                // neighbour graph), cost = rms radius of the union x (1 + w (1 - dot of the mean normals)), truncated to its
                // lowest 8 levels (the engine reads at most eight memberships per probe).
                const int maxHeight = 8;
                var sumP = new List<Vector3>(); var sumPP = new List<double>(); var sumN = new List<Vector3>();
                var cnt = new List<int>(); var height = new List<int>(); var alive = new List<bool>();
                int AddNode(int[] idx, int count, int l, int r, Vector3 sp, double spp, Vector3 sn, int h)
                {
                    var node = new CutNode { M = idx, L = l, R = r };
                    node.C = sp / count;
                    node.N = sn.LengthSquared() > 1e-6f ? Vector3.Normalize(sn) : Vector3.UnitY;
                    node.Ext = (float)Math.Sqrt(Math.Max(0, spp / count - node.C.LengthSquared()));
                    node.Cluster = idx != null && h <= maxHeight && count <= wtop;
                    int id = tree.Nodes.Count; tree.Nodes.Add(node);
                    sumP.Add(sp); sumPP.Add(spp); sumN.Add(sn); cnt.Add(count); height.Add(h); alive.Add(true);
                    if (l >= 0) tree.Nodes[l].Parent = id;
                    if (r >= 0) tree.Nodes[r].Parent = id;
                    return id;
                }
                double Cost(int a, int b)
                {
                    int n2 = cnt[a] + cnt[b];
                    Vector3 c2 = (sumP[a] + sumP[b]) / n2;
                    double rad = Math.Sqrt(Math.Max(0, (sumPP[a] + sumPP[b]) / n2 - c2.LengthSquared()));
                    Vector3 na = sumN[a].LengthSquared() > 1e-6f ? Vector3.Normalize(sumN[a]) : Vector3.UnitY;
                    Vector3 nb = sumN[b].LengthSquared() > 1e-6f ? Vector3.Normalize(sumN[b]) : Vector3.UnitY;
                    double dot = Vector3.Dot(na, nb);
                    return Math.Max(rad, 1e-4) * (1 + mergeNormalWeight * (1 - dot));
                }
                var leafIds = new List<int>();
                foreach (int i in live) leafIds.Add(AddNode(new[] { i }, 1, -1, -1, pos[i], pos[i].LengthSquared(), nrm[i], 1));
                var adj = new Dictionary<int, HashSet<int>>();
                foreach (int id in leafIds) adj[id] = new HashSet<int>();
                var grid = new Dictionary<(int, int, int), List<int>>();
                foreach (int id in leafIds)
                {
                    var c = tree.Nodes[id].C; var k = ((int)Math.Floor(c.X), (int)Math.Floor(c.Y), (int)Math.Floor(c.Z));
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                    l.Add(id);
                }
                foreach (int a in leafIds)
                {
                    var ca = tree.Nodes[a].C; var k = ((int)Math.Floor(ca.X), (int)Math.Floor(ca.Y), (int)Math.Floor(ca.Z));
                    var near = new List<(float d, int b)>();
                    for (int ring = 1; ring <= 3 && near.Count < 24; ring++)
                    {
                        near.Clear();
                        for (int dx = -ring; dx <= ring; dx++) for (int dy = -ring; dy <= ring; dy++) for (int dz = -ring; dz <= ring; dz++)
                            if (grid.TryGetValue((k.Item1 + dx, k.Item2 + dy, k.Item3 + dz), out var l))
                                foreach (int b in l) if (b != a) near.Add((Vector3.DistanceSquared(ca, tree.Nodes[b].C), b));
                    }
                    near.Sort((x, y) => x.d.CompareTo(y.d));
                    for (int j = 0; j < near.Count && j < 24; j++) { adj[a].Add(near[j].b); adj[near[j].b].Add(a); }
                }
                var heap = new CutHeap();
                foreach (int a in leafIds) foreach (int b in adj[a]) if (a < b) { double c = Cost(a, b); if (!double.IsInfinity(c)) heap.Push(c, a, b); }
                while (heap.Count > 0)
                {
                    var top = heap.Pop(); int a = top.a, b = top.b;
                    if (!alive[a] || !alive[b]) continue;
                    int h = 1 + Math.Max(height[a], height[b]);
                    int count = cnt[a] + cnt[b];
                    int[] mm = null;
                    if (h <= maxHeight && count <= wtop && tree.Nodes[a].M != null && tree.Nodes[b].M != null)
                    { var ma = tree.Nodes[a].M; var mb = tree.Nodes[b].M; mm = new int[ma.Length + mb.Length]; ma.CopyTo(mm, 0); mb.CopyTo(mm, ma.Length); }
                    int id = AddNode(mm, count, a, b, sumP[a] + sumP[b], sumPP[a] + sumPP[b], sumN[a] + sumN[b], h);
                    alive[a] = false; alive[b] = false;
                    var na2 = new HashSet<int>();
                    foreach (int c in adj[a]) if (c != b && alive[c]) na2.Add(c);
                    foreach (int c in adj[b]) if (c != a && alive[c]) na2.Add(c);
                    adj.Remove(a); adj.Remove(b); adj[id] = na2;
                    foreach (int c in na2)
                    {
                        var s2 = adj[c]; s2.Remove(a); s2.Remove(b); s2.Add(id);
                        double cc = Cost(id, c); if (!double.IsInfinity(cc)) heap.Push(cc, id, c);
                    }
                }
                for (int id = 0; id < tree.Nodes.Count; id++)
                {
                    var n = tree.Nodes[id];
                    if (n.M == null) n.M = new int[0];
                    n.Top = n.Cluster && (n.Parent < 0 || !tree.Nodes[n.Parent].Cluster);
                    if (n.Top) tree.Tops.Add(id);
                }
                // above the kept height nothing is a cluster: the cut starts from the tops
                foreach (var n in tree.Nodes) if (n.Cluster && n.Parent >= 0 && !tree.Nodes[n.Parent].Cluster) n.Parent = -1;
            }
            return tree;
        }
    }
}
#endif
