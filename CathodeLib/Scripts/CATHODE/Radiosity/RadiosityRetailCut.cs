#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using CATHODE;
using CathodeLib;

namespace CathodeLib.Radiosity
{
    public static partial class RadiosityBaker
    {
        /// <summary>
        /// Version 2 of <see cref="RebuildLinksAsHierarchicalCut"/>, following the retail rules decoded on 24 Sep 2026
        /// (how retail opens its tree, how it builds the tree, and how it places cross-slice fixups):
        /// <list type="bullet">
        /// <item>the tree is a bottom-up greedy merge truncated to 8 levels (HierarchicalCutMergeRounds &gt; 0);</item>
        /// <item>each surface probe gets ONE disjoint 32-link cut over its own slice's tree AND its neighbour slices'
        /// trees; links landing in another slice are the cross-slice fixups, written first (slots 0..nFix-1), the
        /// in-slice links after them - retail: own + fixups = 32 on ~95% of receivers, fixups in the first slots on
        /// 99.5%, and the same cluster reached either way gets the same byte;</item>
        /// <item>no extent/distance threshold: nodes whose members do not face the receiver are dropped, nodes where
        /// fewer than half do are peeled open, then the frontier is refined nearest-first by W (1 + cosE) / d^3 until
        /// 32 links (reproduces 73-80% of retail's links given retail's own visibility);</item>
        /// <item>bytes by the retail law, gain envelope over the whole row (fixups included);</item>
        /// <item>only clusters some link references are kept (retail: 99.7-99.9% referenced), then slots, scatter
        /// (ordered by input probe) and the fixup list and ranges are rewritten.</item>
        /// </list>
        /// </summary>
        public static void RebuildLinksRetailCut(RadiosityRuntime rt, RadiosityBakeSettings settings,
                                                 Func<Vector3, Vector3, bool> visible, Action<string> log,
                                                 Func<Vector3, Vector3, float, float> trace = null)
        {
            int per = InfluencesPerProbe;
            int nRays = trace != null ? settings.HierarchicalCutRays : 0;
            float footprint = settings.HierarchicalCutRayFootprint, attr = settings.HierarchicalCutRayAttributeRadius;
            const double lg = 22.6096401 / 255.0;
            int wtop = Math.Max(2, settings.HierarchicalCutMaxMembers);
            float rmax = settings.HierarchicalCutMaxDistance;
            double med = settings.HierarchicalCutGainMedian, cap = settings.HierarchicalCutGainCap;
            const float theta = 0.5f;
            double vw = settings.HierarchicalCutVisibilityWeight;
            bool penaltyOnly = visible != null && settings.HierarchicalCutVisibilityPenaltyOnly;
            bool balanced = settings.HierarchicalCutBalanced;
            double hybridT = settings.HierarchicalCutHybridLeak;
            double hybridMax = settings.HierarchicalCutHybridLeakMax;
            float recvTol = settings.HierarchicalCutReceiverTolerance;
            float nearFloor = settings.HierarchicalCutByteNearFloor;
            double distScale = settings.HierarchicalCutByteDistanceScale;
            int forcedOpen = settings.HierarchicalCutForcedOpenings;
            int openBy = settings.HierarchicalCutForcedOpenCriterion;
            int centroidVis = visible != null ? settings.HierarchicalCutCentroidVisibility : 0;
            float recvOfs = settings.HierarchicalCutVisReceiverOffset, membOfs = settings.HierarchicalCutVisMemberOffset;
            float visMinDist = settings.HierarchicalCutVisMinDistance;
            bool recvNormalFallback = settings.HierarchicalCutReceiverNormalFallback;
            long hybridSwitched = 0;
            int S = rt.Slices.Count;

            var trees = new CutTree[S];
            for (int s = 0; s < S; s++)
                trees[s] = settings.HierarchicalCutUseFileTree ? BuildFileTree(rt.Slices[s], log)
                    : BuildCutTree(rt.Slices[s], wtop, s, settings.HierarchicalCutNormalClasses, settings.HierarchicalCutMergeRounds,
                                   settings.HierarchicalCutMergeNormalWeight, settings.HierarchicalCutMergeNormalGate, true, log, assignSlots: false,
                                   trace: trace, mergeVisPenalty: settings.HierarchicalCutMergeVisibilityPenalty, mergeVisLift: settings.HierarchicalCutMergeVisibilityLift);

            // input probe geometry per slice
            var ipos = new Vector3[S][]; var inrm = new Vector3[S][];
            for (int s = 0; s < S; s++)
            {
                var sl = rt.Slices[s]; int ni = sl.InputProbePositions.Count;
                ipos[s] = new Vector3[ni]; inrm[s] = new Vector3[ni];
                for (int i = 0; i < ni; i++) { var q = sl.InputProbePositions[i]; ipos[s][i] = new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z)); inrm[s][i] = CutNormal(sl, i); }
            }
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

            // ray gather: a grid of live input probes per slice, to attribute a ray hit to the input probe it landed on
            var grids = new Dictionary<(int, int, int), List<int>>[S];
            (int, int, int) Cell(Vector3 v) => ((int)Math.Floor(v.X / attr), (int)Math.Floor(v.Y / attr), (int)Math.Floor(v.Z / attr));
            if (nRays > 0)
                for (int s = 0; s < S; s++)
                {
                    grids[s] = new Dictionary<(int, int, int), List<int>>();
                    for (int i = 0; i < ipos[s].Length; i++)
                    {
                        if (rt.Slices[s].InputProbePositions[i].W == 0 || trees[s].LeafNodeOfProbe[i] < 0) continue;
                        var c = Cell(ipos[s][i]); if (!grids[s].TryGetValue(c, out var l)) grids[s][c] = l = new List<int>(); l.Add(i);
                    }
                }

            double Gain(int b) => b <= 0 ? 0 : Math.Pow(2, b * lg) * 1.48436e-7 - 1.48437e-7;
            var rows = new List<(int t, int node, int b, int w)>[S][];
            long totalFix = 0;
            // balanced cut: nodes that may not be opened (capacity passes - a slice has 16128 free cluster slots, so where
            // the cut references more clusters, the cheapest parents are closed and the cut is recomputed)
            var noOpen = new HashSet<int>[S]; for (int s = 0; s < S; s++) noOpen[s] = new HashSet<int>();
            for (int pass = 0; ; pass++)
            {
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
                if (recvNormalFallback)
                {
                    // receivers the mangle map gives no normal (about half of them): take the normal of the nearest live input
                    // probe of the slice within 0.5 m, so their visibility rays leave the surface like everyone else's
                    var grid = new Dictionary<(int, int, int), List<int>>();
                    for (int i = 0; i < ni; i++) { if (sl.InputProbePositions[i].W == 0) continue; var q = ipos[s][i]; var k = ((int)Math.Floor(q.X / 0.5f), (int)Math.Floor(q.Y / 0.5f), (int)Math.Floor(q.Z / 0.5f)); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(); l.Add(i); }
                    for (int p = 0; p < np; p++)
                    {
                        if (rn[p].LengthSquared() > 1e-6f) continue;
                        var P4 = sl.SurfaceProbePositions[p]; if (P4.W == 0) continue;
                        var P = new Vector3(P4.X, P4.Y, P4.Z); var k0 = ((int)Math.Floor(P.X / 0.5f), (int)Math.Floor(P.Y / 0.5f), (int)Math.Floor(P.Z / 0.5f));
                        int best = -1; float bd = 0.25f;
                        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                            if (grid.TryGetValue((k0.Item1 + dx, k0.Item2 + dy, k0.Item3 + dz), out var l)) foreach (int i in l) { float d2 = Vector3.DistanceSquared(ipos[s][i], P); if (d2 < bd) { bd = d2; best = i; } }
                        if (best >= 0) rn[p] = inrm[s][best];
                    }
                }
                var cand = new List<int> { s }; if (!settings.HierarchicalCutOwnSliceOnly) cand.AddRange(neigh[s]);
                rows[s] = new List<(int, int, int, int)>[np];
                var sRows = rows[s];
                Parallel.For(0, np, p =>
                {
                    var P4 = sl.SurfaceProbePositions[p];
                    if (P4.W == 0) return;
                    var P = new Vector3(P4.X, P4.Y, P4.Z);
                    Vector3 rnp = rn[p].LengthSquared() > 1e-6f ? Vector3.Normalize(rn[p]) : Vector3.Zero;
                    bool useVis = !(hybridT > 0);   // hybrid: first pass without visibility
                    // member validity: the member faces the receiver (and the receiver does not face away)
                    bool Valid(int t, int i)
                    {
                        Vector3 d = P - ipos[t][i]; float len = d.Length(); if (len < 1e-3f || len > rmax) return false;
                        if (Vector3.Dot(inrm[t][i], d) <= 0) return false;
                        if (rnp != Vector3.Zero && Vector3.Dot(rnp, -d) < -recvTol * len) return false;
                        return visible == null || penaltyOnly || !useVis || centroidVis > 0 || len < visMinDist || Sees(t, i);
                    }
                    // members are re-sampled by every node on the way down, so remember each (slice, input probe) answer
                    var seen = visible == null ? null : new Dictionary<(int, int), bool>();
                    bool Sees(int t, int i)
                    {
                        if (seen.TryGetValue((t, i), out bool b)) return b;
                        b = visible(P + (rnp == Vector3.Zero ? Vector3.Zero : rnp * recvOfs), ipos[t][i] + inrm[t][i] * membOfs);
                        seen[(t, i)] = b; return b;
                    }
                    // penalty-only visibility: the fraction of a linked patch's valid (facing) sampled members the ray reaches
                    float Seen(int t, CutNode n)
                    {
                        int step = Math.Max(1, n.M.Length / 12), ok = 0, tot = 0;
                        for (int k = 0; k < n.M.Length; k += step) { if (!Valid(t, n.M[k])) continue; tot++; if (Sees(t, n.M[k])) ok++; }
                        return tot > 0 ? (float)ok / tot : 1;
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
                        if (centroidVis > 0 && useVis && !visible(P + (rnp == Vector3.Zero ? Vector3.Zero : rnp * recvOfs), n.C + n.N * 0.05f))
                        {
                            // patch-level visibility: a hidden centre drops the patch (1) or opens it (2; a hidden leaf drops)
                            if (centroidVis == 2 && n.L >= 0 && n.R >= 0 && !noOpen[t].Contains(id)) { Add(t, n.L); Add(t, n.R); }
                            return;
                        }
                        if (v < theta && n.L >= 0 && n.R >= 0 && !noOpen[t].Contains(id)) { Add(t, n.L); Add(t, n.R); return; }   // peel
                        frontier.Add((t, id, v));
                    }
                    if (nRays > 0)
                    {
                        // RAY GATHER: cosine-weighted rays from the receiver; each hit goes to the facing live input probe
                        // it landed on, climbs the tree while the patch stays small for its distance (extent/d <= footprint),
                        // and nested picks fold into the coarser one - visible by construction, partial near coverage
                        Vector3 nz = rnp != Vector3.Zero ? rnp : Vector3.UnitY;
                        Vector3 tx = Vector3.Normalize(Vector3.Cross(nz, Math.Abs(nz.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX)), ty = Vector3.Cross(nz, tx);
                        Vector3 org = P + nz * 0.05f;
                        var hits = new Dictionary<(int t, int node), int>();
                        uint seed = (uint)(s * 7919 + p) * 747796405u ^ 0x9E3779B9u;
                        for (int r = 0; r < nRays; r++)
                        {
                            seed = seed * 747796405u + 2891336453u; float u1 = ((seed >> 8) & 0xFFFFFF) / 16777216.0f;
                            seed = seed * 747796405u + 2891336453u; float u2 = ((seed >> 8) & 0xFFFFFF) / 16777216.0f;
                            float rad = (float)Math.Sqrt(u1); double th = 2 * Math.PI * u2;
                            Vector3 dir = tx * (rad * (float)Math.Cos(th)) + ty * (rad * (float)Math.Sin(th)) + nz * (float)Math.Sqrt(Math.Max(0, 1 - u1));
                            float hitT = trace(org, dir, rmax); if (hitT <= 0) continue;
                            Vector3 H = org + dir * hitT;
                            int bt = -1, bi = -1; float bd = attr * attr;
                            var hc = Cell(H);
                            foreach (int t in cand)
                                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                                {
                                    if (!grids[t].TryGetValue((hc.Item1 + dx, hc.Item2 + dy, hc.Item3 + dz), out var l)) continue;
                                    foreach (int i in l)
                                    {
                                        if (Vector3.Dot(inrm[t][i], dir) > 0.1f) continue;   // must face back along the ray
                                        float d2 = Vector3.DistanceSquared(ipos[t][i], H); if (d2 < bd) { bd = d2; bt = t; bi = i; }
                                    }
                                }
                            if (bt < 0) continue;
                            int node = trees[bt].LeafNodeOfProbe[bi];
                            while (true)
                            {
                                int q = trees[bt].Nodes[node].Parent; if (q < 0) break;
                                var qn = trees[bt].Nodes[q];
                                if (qn.Ext > footprint * Math.Max(0.1f, (qn.C - P).Length())) break;
                                node = q;
                            }
                            // capacity passes: nothing under a closed node may be linked - use the highest closed ancestor
                            for (int a = trees[bt].Nodes[node].Parent, top = -1; ; a = trees[bt].Nodes[a].Parent)
                            {
                                if (a < 0) { if (top >= 0) node = top; break; }
                                if (noOpen[bt].Contains(a)) top = a;
                            }
                            hits[(bt, node)] = (hits.TryGetValue((bt, node), out int h) ? h : 0) + 1;
                        }
                        // disjoint: a pick whose ancestor was also picked folds into the ancestor
                        var picked = new HashSet<(int, int)>(hits.Keys);
                        foreach (var k in hits.Keys)
                        {
                            bool nested = false; int q = trees[k.t].Nodes[k.node].Parent;
                            while (q >= 0) { if (picked.Contains((k.t, q))) { nested = true; break; } q = trees[k.t].Nodes[q].Parent; }
                            if (!nested) frontier.Add((k.t, k.node, 1f));
                        }
                    }
                    else
                    foreach (int t in cand) foreach (int top in trees[t].Tops) Add(t, top);
                    double Prio(int t, int id)
                    {
                        var n = trees[t].Nodes[id]; Vector3 dv = n.C - P; float d = Math.Max(0.1f, dv.Length());
                        return n.M.Length * (1 + Vector3.Dot(n.N, -dv / d)) / (d * d * d);
                    }
                    int guard = 0;
                    if (balanced)
                    {
                        // balanced priority cut: keep the top `per` by priority, and open the highest-priority node whose
                        // children would all stay in the top `per` - retail's links end up at equal W(1+cosE)/d^3 (W1 at
                        // ~2.2 m .. W32-63 at ~7 m), far roots dropped, near surfaces refined to single probes
                        var closed = new HashSet<(int, int)>();
                        var pr = new Dictionary<(int, int), double>();
                        double Pc(int t, int id) { if (!pr.TryGetValue((t, id), out double x)) pr[(t, id)] = x = Prio(t, id); return x; }
                        void Rank(List<(int t, int id, float v)> l) => l.Sort((x, y) => Pc(y.t, y.id).CompareTo(Pc(x.t, x.id)));
                        Rank(frontier); if (frontier.Count > per) frontier.RemoveRange(per, frontier.Count - per);
                        while (guard++ < 4096)
                        {
                            bool opened = false;
                            for (int a = 0; a < frontier.Count && !opened; a++)
                            {
                                var f = frontier[a]; var nd = trees[f.t].Nodes[f.id];
                                if (nd.L < 0 || nd.R < 0 || closed.Contains((f.t, f.id)) || noOpen[f.t].Contains(f.id)) continue;
                                var keep = frontier; frontier = new List<(int t, int id, float v)>();
                                Add(f.t, nd.L); Add(f.t, nd.R);
                                var kids = frontier; frontier = keep;
                                var tent = new List<(int t, int id, float v)>(frontier.Count + kids.Count);
                                for (int k = 0; k < frontier.Count; k++) if (k != a) tent.Add(frontier[k]);
                                tent.AddRange(kids); Rank(tent);
                                bool ok = true;
                                if (tent.Count > per) { double pk = Pc(tent[per - 1].t, tent[per - 1].id); foreach (var k in kids) if (Pc(k.t, k.id) < pk) { ok = false; break; } }
                                if (!ok) { closed.Add((f.t, f.id)); continue; }
                                if (tent.Count > per) tent.RemoveRange(per, tent.Count - per);
                                frontier = tent; opened = true;
                            }
                            if (!opened) break;
                        }
                    }
                    else Refine();
                    void Refine()
                    {
                        int g2 = 0;
                        while (frontier.Count < per && g2++ < 512)
                        {
                            int best = -1; double bp = 0;
                            for (int a = 0; a < frontier.Count; a++)
                            {
                                var n = trees[frontier[a].t].Nodes[frontier[a].id];
                                if (n.L < 0 || n.R < 0 || noOpen[frontier[a].t].Contains(frontier[a].id)) continue;
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
                    // occluded (a probe next to a wall with a lit room behind it) - then redo this probe's cut with visibility
                    if (hybridT > 0 && visible != null && !useVis && nRays == 0 && !balanced)
                    {
                        double gs = 0, gl = 0;
                        foreach (var f in frontier)
                        {
                            var n = trees[f.t].Nodes[f.id]; float d = Math.Max(0.1f, (n.C - P).Length()); double w = n.M.Length / (d * d);
                            gs += w; if (Seen(f.t, n) < 0.5f) gl += w;
                        }
                        if (gs > 0 && gl / gs > hybridT && (hybridMax <= 0 || gl / gs < hybridMax))
                        {
                            useVis = true; frontier.Clear();
                            foreach (int t in cand) foreach (int top in trees[t].Tops) Add(t, top);
                            Refine();
                            Interlocked.Increment(ref hybridSwitched);
                        }
                    }
                    var row = new List<(int t, int node, int b, int w, double g)>(frontier.Count);
                    (int t, int node, int b, int w, double g) Link(int t, int id, float v)
                    {
                        var n = trees[t].Nodes[id]; Vector3 dv = n.C - P; float d = Math.Max(0.1f, dv.Length()); Vector3 dir = dv / d;
                        float ce = Math.Max(0, Vector3.Dot(n.N, -dir)), cr = rnp == Vector3.Zero ? 0.5f : Math.Max(0, Vector3.Dot(rnp, dir));
                        double L = distScale * Math.Log(Math.Max(d, nearFloor), 2), w = Math.Log(n.M.Length, 2);
                        double byt = 178.4 - 17.9 * L - 3.0 * L * L - 2.4 * w - 1.03 * w * w + 3.09 * L * w + 26.3 * ce + 10.6 * cr;
                        float fv = penaltyOnly ? Seen(t, n) : v;
                        if (vw > 0 && fv < 1) byt += vw * Math.Log(Math.Max(fv, 1.0 / 12), 2) / lg;
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
                            if (n.L < 0 || n.R < 0 || noOpen[row[a].t].Contains(row[a].node)) continue;
                            double pr = openBy == 1 ? row[a].g : openBy == 2 ? trees[row[a].t].Nodes[row[a].node].Ext / Math.Max(0.1f, (trees[row[a].t].Nodes[row[a].node].C - P).Length()) : Prio(row[a].t, row[a].node);
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
                });
            }

            if (hybridT > 0) log?.Invoke("    retail cut (hybrid): " + hybridSwitched + " probes re-cut with visibility (leak fraction > " + hybridT + ")");
            if ((!balanced && nRays == 0) || pass >= 3) break;
            bool over = false;
            for (int t = 0; t < S; t++)
            {
                var refCount = new Dictionary<int, int>();
                for (int s = 0; s < S; s++) foreach (var r in rows[s]) if (r != null) foreach (var x in r) if (x.t == t) refCount[x.node] = (refCount.TryGetValue(x.node, out var c) ? c : 0) + 1;
                int capacity = CutFreeSlots(rt.Slices[t].ClusterPositions.Count, settings.HierarchicalCutTileMajorSlots).Count();
                int excess = refCount.Count - capacity;
                if (excess <= 0) continue;
                over = true;
                // closing parent q replaces its referenced children with q: saves (children - (q referenced ? 0 : 1)) slots
                // at a cost of the links that coarsen; close the cheapest per slot saved
                var byParent = new Dictionary<int, (int kids, int refs)>();
                foreach (var kv in refCount)
                {
                    int q = trees[t].Nodes[kv.Key].Parent; if (q < 0 || noOpen[t].Contains(q)) continue;
                    var e = byParent.TryGetValue(q, out var v0) ? v0 : (kids: 0, refs: 0); byParent[q] = (e.kids + 1, e.refs + kv.Value);
                }
                int saved = 0, closedNow = 0;
                foreach (var kv in byParent.Select(kv => (q: kv.Key, save: kv.Value.kids - (refCount.ContainsKey(kv.Key) ? 0 : 1), cost: kv.Value.refs))
                                           .Where(x => x.save > 0).OrderBy(x => x.cost / (double)x.save))
                {
                    if (saved >= excess * 1.15 + 32) break;
                    noOpen[t].Add(kv.q); saved += kv.save; closedNow++;
                }
                log?.Invoke("    retail cut (balanced) pass " + pass + ", slice " + t + ": " + refCount.Count + " clusters referenced, capacity " + capacity + " -> closing " + closedNow + " parents");
            }
            if (!over) break;
            }

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
                free.AddRange(CutFreeSlots(nc, settings.HierarchicalCutTileMajorSlots));
                slotOf[t] = new Dictionary<int, int>();
                int fi = 0, dropped = 0;
                foreach (var kv in refCount.OrderByDescending(k => k.Value)) { if (fi < free.Count) slotOf[t][kv.Key] = free[fi++]; else dropped++; }
                for (int i = 0; i < nc; i++) sl.ClusterPositions[i] = new Vector4u16();
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
                scatter.Sort((a, b) => { int pa = a.A * 256 + a.B, pb = b.A * 256 + b.B; if (pa != pb) return pa.CompareTo(pb); return (a.G * 256 + a.R).CompareTo(b.G * 256 + b.R); });
                sl.Scatter = scatter;
                if (!settings.HierarchicalCutKeepTiles) CutCoverTiles(sl.InputProbeTiles, slotOf[t].Values, settings.HierarchicalCutTileMajorSlots);
                log?.Invoke("    retail cut, slice " + t + ": " + slotOf[t].Count + " referenced clusters kept of " + trees[t].Nodes.Count(n => n.Cluster) +
                            (dropped > 0 ? " (" + dropped + " dropped: no free slot)" : "") + ", memberships/probe max " + maxPer + ", scatter " + scatter.Count);
            }

            // write rows: fixups first (slots 0..nFix-1), in-slice links after; rebuild the fixup list and ranges
            var fixByPair = new Dictionary<(int s, int o), List<RadiosityRuntime.RuntimeInfluenceFixup>>();
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
                totalFix += fx;
                log?.Invoke("    retail cut, slice " + s + ": " + links + " in-slice links + " + fx + " fixups over " + live + " probes (" + (live > 0 ? (double)(links + fx) / live : 0).ToString("0.0") + "/probe, " + full + " full)");
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
        }

        /// <summary>
        /// A cut tree from the clusters ALREADY in a runtime file (retail's, for a split test): each cluster's members come from the
        /// scatter list, a cluster's parent is the smallest strict superset among them, members no child covers get singleton
        /// leaves, and n-ary nodes are chained into binary ones (synthetic nodes). Probes in no cluster become singleton tops.
        /// </summary>
        private static CutTree BuildFileTree(RadiosityRuntime.RuntimeDataSlice sl, Action<string> log)
        {
            var tree = new CutTree();
            int ni = sl.InputProbePositions.Count, nc = sl.ClusterPositions.Count;
            var pos = new Vector3[ni]; var nrm = new Vector3[ni]; var live = new bool[ni];
            for (int i = 0; i < ni; i++) { var q = sl.InputProbePositions[i]; pos[i] = new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z)); nrm[i] = CutNormal(sl, i); live[i] = q.W != 0; }
            var mem = new List<int>[nc];
            foreach (var e in sl.Scatter) { int c = e.G * 256 + e.R, i = e.A * 256 + e.B; if (c < nc && i < ni && live[i]) (mem[c] ??= new List<int>()).Add(i); }
            var clusters = Enumerable.Range(0, nc).Where(c => mem[c] != null && mem[c].Count > 0).Select(c => mem[c].Distinct().OrderBy(x => x).ToArray()).GroupBy(a => string.Join(",", a)).Select(g => g.First()).OrderBy(a => a.Length).ToList();
            var memberOf = new List<int>[ni];
            for (int k = 0; k < clusters.Count; k++) foreach (int i in clusters[k]) (memberOf[i] ??= new List<int>()).Add(k);
            var sets = clusters.Select(a => new HashSet<int>(a)).ToList();
            var parent = new int[clusters.Count]; var children = new List<int>[clusters.Count];
            for (int k = 0; k < clusters.Count; k++)
            {
                parent[k] = -1; int best = -1;
                foreach (int d in memberOf[clusters[k][0]])
                {
                    if (d == k || clusters[d].Length <= clusters[k].Length) continue;
                    if (best >= 0 && clusters[d].Length >= clusters[best].Length) continue;
                    bool sub = true; foreach (int i in clusters[k]) if (!sets[d].Contains(i)) { sub = false; break; }
                    if (sub) best = d;
                }
                parent[k] = best; if (best >= 0) (children[best] ??= new List<int>()).Add(k);
            }
            int Make(int[] m, int par)
            {
                var n = new CutNode { M = m, Parent = par, Cluster = true };
                Vector3 c = Vector3.Zero, ns = Vector3.Zero; foreach (int i in m) { c += pos[i]; ns += nrm[i]; }
                c /= m.Length; n.C = c; n.N = ns.LengthSquared() > 1e-6f ? Vector3.Normalize(ns) : Vector3.UnitY;
                double r = 0; foreach (int i in m) r += (pos[i] - c).LengthSquared(); n.Ext = (float)Math.Sqrt(r / m.Length);
                n.Target = m.Length == 1 ? pos[m[0]] + nrm[m[0]] * 0.02f : c + n.N * 0.05f;
                tree.Nodes.Add(n); return tree.Nodes.Count - 1;
            }
            // build node k (cluster), then its children chained in pairs
            int Build(int k, int par)
            {
                int id = Make(clusters[k], par);
                var kids = new List<int[]>(); var kidIdx = new List<int>();
                var covered = new HashSet<int>();
                if (children[k] != null) foreach (int ch in children[k].OrderByDescending(x => clusters[x].Length)) { if (clusters[ch].Any(covered.Contains)) continue; kidIdx.Add(ch); foreach (int i in clusters[ch]) covered.Add(i); }
                var parts = new List<Func<int, int>>();
                foreach (int ch in kidIdx) { int cc = ch; parts.Add(p => Build(cc, p)); }
                if (clusters[k].Length > 1) foreach (int i in clusters[k]) if (!covered.Contains(i)) { int ii = i; parts.Add(p => Make(new[] { ii }, p)); }
                if (parts.Count == 0) return id;
                if (parts.Count == 1) { int only = parts[0](id); tree.Nodes[id].L = only; return id; }
                Chain(id, parts, 0);
                return id;
            }
            void Chain(int id, List<Func<int, int>> parts, int from)
            {
                int l = parts[from](id); tree.Nodes[id].L = l;
                if (parts.Count - from == 2) { tree.Nodes[id].R = parts[from + 1](id); return; }
                var rest = new List<int>(); for (int t = from + 1; t < parts.Count; t++) { }
                // synthetic node for the remaining parts: members filled after its children exist
                var syn = new CutNode { M = new int[0], Parent = id, Cluster = true }; tree.Nodes.Add(syn); int sid = tree.Nodes.Count - 1; tree.Nodes[id].R = sid;
                Chain(sid, parts, from + 1);
                var m = new List<int>(); Collect(sid, m); var arr = m.Distinct().ToArray();
                Vector3 c = Vector3.Zero, ns = Vector3.Zero; foreach (int i in arr) { c += pos[i]; ns += nrm[i]; }
                c /= Math.Max(1, arr.Length); syn.M = arr; syn.C = c; syn.N = ns.LengthSquared() > 1e-6f ? Vector3.Normalize(ns) : Vector3.UnitY;
                double r = 0; foreach (int i in arr) r += (pos[i] - c).LengthSquared(); syn.Ext = (float)Math.Sqrt(r / Math.Max(1, arr.Length));
                syn.Target = c + syn.N * 0.05f;
            }
            void Collect(int id, List<int> m) { var n = tree.Nodes[id]; if (n.L < 0 && n.R < 0) { m.AddRange(n.M); return; } if (n.L >= 0) Collect(n.L, m); if (n.R >= 0) Collect(n.R, m); }
            for (int k = 0; k < clusters.Count; k++) if (parent[k] < 0) tree.Tops.Add(Build(k, -1));
            var inAny = new bool[ni]; foreach (var a in clusters) foreach (int i in a) inAny[i] = true;
            int orphans = 0; for (int i = 0; i < ni; i++) if (live[i] && !inAny[i]) { tree.Tops.Add(Make(new[] { i }, -1)); orphans++; }
            foreach (int t in tree.Tops) tree.Nodes[t].Top = true;
            tree.LeafNodeOfProbe = new int[ni]; for (int i = 0; i < ni; i++) tree.LeafNodeOfProbe[i] = -1;
            for (int id2 = 0; id2 < tree.Nodes.Count; id2++) if (tree.Nodes[id2].M.Length == 1) tree.LeafNodeOfProbe[tree.Nodes[id2].M[0]] = id2;
            tree.LeafSlotOfProbe = new int[ni]; for (int i = 0; i < ni; i++) tree.LeafSlotOfProbe[i] = -1;
            log?.Invoke("    file tree: " + clusters.Count + " distinct clusters, " + tree.Tops.Count + " tops (" + orphans + " orphan probes), " + tree.Nodes.Count + " nodes");
            return tree;
        }
    }
}
#endif
