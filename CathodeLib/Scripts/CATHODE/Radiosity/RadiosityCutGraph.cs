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
        /// Rebuild a finished runtime's cluster stage and influence links as retail structures them (see
        /// <see cref="RadiosityBakeSettings.HierarchicalCutLinks"/>). Per slice: a tree over the live input probes - a k-d
        /// tree (median split on the longest axis), or with HierarchicalCutMergeRounds &gt; 0 retail's bottom-up greedy
        /// merge - whose nodes of up to HierarchicalCutMaxMembers members are the clusters (leaves = the probes; the
        /// smallest patches are dropped first if the texture is full), the scatter list rewritten as membership (at most
        /// eight per probe), the probe tiles padded so every slot is cleared unless HierarchicalCutKeepTiles is on (the
        /// default); then for every surface probe a disjoint cut through the tree - a node is opened while its rms
        /// extent over distance exceeds the threshold, kept if it faces the probe (and, optionally, is visible),
        /// then refined or trimmed to the 32-link budget left after the probe's cross-slice fixups. Bytes follow
        /// the law fitted to retail's links, then the members-weighted gain envelope. Fixups keep their weight, are
        /// re-pointed at the new leaf of their old cluster's first member, and move into reserved empty slots
        /// (retail's fixups never overwrite a live link).
        /// </summary>
        /// <param name="visible">Line-of-sight test, or null for none.</param>
        public static void RebuildLinksAsHierarchicalCut(RadiosityRuntime rt, RadiosityBakeSettings settings,
                                                         Func<Vector3, Vector3, bool> visible, Action<string> log)
        {
            float T = settings.HierarchicalCutThreshold;
            int wtop = Math.Max(2, settings.HierarchicalCutMaxMembers);
            float rmax = settings.HierarchicalCutMaxDistance;
            double med = settings.HierarchicalCutGainMedian, cap = settings.HierarchicalCutGainCap;
            float refineMin = settings.HierarchicalCutRefineMin;
            const float lift = 0.02f;
            const double logGainPerByte = 22.6096401 / 255.0;
            int per = InfluencesPerProbe;

            var oldFirstMember = new int[rt.Slices.Count][];
            for (int s = 0; s < rt.Slices.Count; s++)
            {
                var sl = rt.Slices[s];
                var fm = new int[sl.ClusterPositions.Count];
                for (int i = 0; i < fm.Length; i++) fm[i] = -1;
                foreach (var e in sl.Scatter) { int c = e.G * 256 + e.R, i = e.A * 256 + e.B; if (c < fm.Length && fm[c] < 0) fm[c] = i; }
                oldFirstMember[s] = fm;
            }

            var trees = new CutTree[rt.Slices.Count];
            for (int s = 0; s < rt.Slices.Count; s++) trees[s] = BuildCutTree(rt.Slices[s], wtop, s, settings.HierarchicalCutNormalClasses, settings.HierarchicalCutMergeRounds, settings.HierarchicalCutMergeNormalWeight, settings.HierarchicalCutMergeNormalGate, settings.HierarchicalCutScatterByProbe, log, true, settings.HierarchicalCutTileMajorSlots, settings.HierarchicalCutKeepTiles);

            var fixByProbe = new Dictionary<int, List<int>>[rt.Slices.Count];
            for (int s = 0; s < rt.Slices.Count; s++) fixByProbe[s] = new Dictionary<int, List<int>>();
            var fixOther = new int[rt.InfluenceFixups.Count];
            for (int i = 0; i < fixOther.Length; i++) fixOther[i] = -1;
            for (int s = 0; s < rt.Slices.Count && s < rt.SliceNeighbourCounts.Count && s < rt.SliceNeighbourArrayOffsets.Count; s++)
            {
                int off = rt.SliceNeighbourArrayOffsets[s];
                for (int n = 0; n < rt.SliceNeighbourCounts[s]; n++)
                {
                    if (off + n >= rt.FlattenedOtherSliceIndices.Count || off + n >= rt.FlattenedFixupRanges.Count) break;
                    int other = rt.FlattenedOtherSliceIndices[off + n];
                    var range = rt.FlattenedFixupRanges[off + n];
                    for (int i = range.First; i < range.First + range.Num && i < rt.InfluenceFixups.Count; i++)
                    {
                        fixOther[i] = other;
                        int p = rt.InfluenceFixups[i].WeightTexOffset / per;
                        if (!fixByProbe[s].TryGetValue(p, out var l)) fixByProbe[s][p] = l = new List<int>();
                        l.Add(i);
                    }
                }
            }

            double Gain(int b) => b <= 0 ? 0 : Math.Pow(2, b * logGainPerByte) * 1.48436e-7 - 1.48437e-7;
            long fixKept = 0, fixDropped = 0;
            for (int s = 0; s < rt.Slices.Count; s++)
            {
                var sl = rt.Slices[s]; var tree = trees[s];
                int ni = sl.InputProbePositions.Count, np = sl.SurfaceProbePositions.Count;
                var ip = new Vector3[ni]; var inn = new Vector3[ni];
                for (int i = 0; i < ni; i++)
                {
                    var q = sl.InputProbePositions[i];
                    ip[i] = new Vector3(FromHalf(q.X), FromHalf(q.Y), FromHalf(q.Z));
                    inn[i] = CutNormal(sl, i);
                }
                // A surface probe's normal: the mean of the input probes whose feedback texel it is.
                var rn = new Vector3[np];
                for (int i = 0; i < ni; i++)
                {
                    if (sl.InputProbePositions[i].W == 0 || i >= sl.MangleMap.Count) continue;
                    var m = sl.MangleMap[i];
                    if (m.B == 255 && m.A == 63) continue;
                    int t = m.A * AtlasSize + m.B;
                    if (t >= sl.MangleMap.Count) continue;
                    int sp = sl.MangleMap[t].G * 256 + sl.MangleMap[t].R;
                    if (sp < np) rn[sp] += inn[i];
                }
                var grid = new Dictionary<(int, int, int), List<int>>();
                for (int i = 0; i < ni; i++)
                {
                    if (sl.InputProbePositions[i].W == 0) continue;
                    var k = ((int)Math.Floor(ip[i].X), (int)Math.Floor(ip[i].Y), (int)Math.Floor(ip[i].Z));
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>();
                    l.Add(i);
                }

                var linksOut = new List<(int slot, int cluster, int b, int w)>[np];
                int noNormal = 0, flipped = 0, blind = 0;
                Parallel.For(0, np, p =>
                {
                    var P4 = sl.SurfaceProbePositions[p];
                    if (P4.W == 0) return;
                    var S = new Vector3(P4.X, P4.Y, P4.Z);
                    Vector3 n = rn[p];
                    if (n.LengthSquared() < 1e-6f)
                    {
                        float bd = 1.5f * 1.5f; int bi = -1;
                        var k0 = ((int)Math.Floor(S.X), (int)Math.Floor(S.Y), (int)Math.Floor(S.Z));
                        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                            if (grid.TryGetValue((k0.Item1 + dx, k0.Item2 + dy, k0.Item3 + dz), out var l))
                                foreach (int i in l) { float d = Vector3.DistanceSquared(ip[i], S); if (d < bd) { bd = d; bi = i; } }
                        if (bi < 0) { Interlocked.Increment(ref noNormal); return; }
                        n = inn[bi];
                    }
                    n = Vector3.Normalize(n);
                    Vector3 O = S + n * lift;
                    int nFix = fixByProbe[s].TryGetValue(p, out var fl) ? fl.Count : 0;
                    int budget = Math.Max(0, per - nFix);
                    var accepted = new List<(CutNode node, float d, float cr, float ce, float ratio)>();
                    bool noVis = visible == null;
                    bool Vis(Vector3 a, Vector3 b) => noVis || visible(a, b);
                    void Visit(int id)
                    {
                        CutNode node = tree.Nodes[id];
                        Vector3 dv = node.C - S; float d = dv.Length();
                        if (d - node.Ext > rmax) return;
                        float ratio = node.Ext / Math.Max(0.05f, d);
                        bool canSplit = node.L >= 0 && node.R >= 0;
                        if (canSplit && ratio > T) { Visit(node.L); Visit(node.R); return; }
                        if (node.Slot < 0) { if (canSplit) { Visit(node.L); Visit(node.R); } return; }
                        Vector3 dir = d > 1e-4f ? dv / d : n;
                        float cr = Vector3.Dot(n, dir), ce = Vector3.Dot(node.N, -dir);
                        if (cr <= 0.02f || ce <= 0.02f) { if (canSplit && ratio > 0.1f) { Visit(node.L); Visit(node.R); } return; }
                        if (!Vis(O, node.Target)) { if (canSplit && ratio > 0.05f) { Visit(node.L); Visit(node.R); } return; }
                        accepted.Add((node, d, cr, ce, ratio));
                    }
                    foreach (int ti in tree.Tops) Visit(ti);
                    if (accepted.Count == 0)
                    {
                        // Nothing faces (or is visible to) the receiver: it sits behind or inside geometry (retail's
                        // never do). Try the other side of the surface, then without visibility, rather than leave the
                        // texel black.
                        n = -n; O = S + n * 0.08f;
                        foreach (int ti in tree.Tops) Visit(ti);
                        if (accepted.Count > 0) Interlocked.Increment(ref flipped);
                        else if (!noVis) { n = -n; O = S + n * lift; noVis = true; foreach (int ti in tree.Tops) Visit(ti); if (accepted.Count > 0) Interlocked.Increment(ref blind); }
                    }
                    var frozen = new HashSet<CutNode>(); int guard = 0;
                    while (accepted.Count < budget && guard++ < 256)
                    {
                        int best = -1; float br = refineMin;
                        for (int a = 0; a < accepted.Count; a++)
                            if (accepted[a].node.L >= 0 && accepted[a].node.R >= 0 && accepted[a].ratio > br && !frozen.Contains(accepted[a].node)) { br = accepted[a].ratio; best = a; }
                        if (best < 0) break;
                        var keepA = accepted[best]; accepted.RemoveAt(best);
                        int before = accepted.Count;
                        Visit(keepA.node.L); Visit(keepA.node.R);
                        if (accepted.Count == before) { accepted.Add(keepA); frozen.Add(keepA.node); }
                    }
                    var scored = new List<(CutNode node, int b, double g)>(accepted.Count);
                    foreach (var a in accepted)
                    {
                        double L = Math.Log(Math.Max(0.1, a.d), 2), w = Math.Log(a.node.M.Length, 2);
                        double byt = 178.4 - 17.9 * L - 3.0 * L * L - 2.4 * w - 1.03 * w * w + 3.09 * L * w + 26.3 * a.ce + 10.6 * a.cr;
                        int b = (int)Math.Round(Math.Max(1, Math.Min(255, byt)));
                        scored.Add((a.node, b, Math.Pow(2, b * logGainPerByte) * a.node.M.Length));
                    }
                    scored.Sort((x, y) => y.g.CompareTo(x.g));
                    if (scored.Count > budget) scored.RemoveRange(budget, scored.Count - budget);
                    var outl = new List<(int, int, int, int)>(scored.Count);
                    for (int k = 0; k < scored.Count; k++) outl.Add((p * per + k, scored[k].node.Slot, scored[k].b, scored[k].node.M.Length));
                    linksOut[p] = outl;
                });

                // Members-weighted gain envelope: slice median to the target, then a per-probe cap.
                double G(List<(int slot, int cluster, int b, int w)> l) { double g = 0; foreach (var x in l) g += Gain(x.b) * x.w; return g; }
                var gs = new List<double>();
                for (int p = 0; p < np; p++) if (linksOut[p] != null && linksOut[p].Count > 0) gs.Add(G(linksOut[p]));
                gs.Sort();
                double shift = med > 0 && gs.Count > 0 && gs[gs.Count / 2] > 0 ? Math.Log(med / gs[gs.Count / 2], 2) / logGainPerByte : 0;
                for (int p = 0; p < np; p++)
                {
                    var l = linksOut[p];
                    if (l == null || l.Count == 0) continue;
                    for (int k = 0; k < l.Count; k++) l[k] = (l[k].slot, l[k].cluster, (int)Math.Round(Math.Max(1, Math.Min(255, l[k].b + shift))), l[k].w);
                    double g = G(l);
                    if (cap > 0 && g > cap)
                    {
                        double sh = Math.Log(cap / g, 2) / logGainPerByte;
                        for (int k = 0; k < l.Count; k++) l[k] = (l[k].slot, l[k].cluster, (int)Math.Max(1, Math.Min(255, Math.Floor(l[k].b + sh))), l[k].w);
                    }
                }

                for (int i = 0; i < sl.SurfaceProbeWeights.Count; i++) sl.SurfaceProbeWeights[i] = new Vector4u8();
                for (int i = 0; i < sl.SurfaceProbeInfluences.Count; i++) sl.SurfaceProbeInfluences[i] = new ColourRGBA8();
                long links = 0; int full = 0, live = 0;
                for (int p = 0; p < np; p++)
                {
                    if (sl.SurfaceProbePositions[p].W == 0) continue;
                    live++;
                    var l = linksOut[p];
                    if (l == null) continue;
                    if (l.Count >= per - 2) full++;
                    foreach (var (slot, cl, b, w) in l) { WriteInfluence(sl, slot, (byte)(cl % 256), (byte)(cl / 256), (byte)b); links++; }
                    if (fixByProbe[s].TryGetValue(p, out var fl))
                    {
                        int k = l.Count;
                        var P4 = sl.SurfaceProbePositions[p];
                        var S = new Vector3(P4.X, P4.Y, P4.Z);
                        Vector3 rnp = rn[p].LengthSquared() > 1e-6f ? Vector3.Normalize(rn[p]) : Vector3.Zero;
                        var chosen = new List<CutNode>();
                        foreach (int fi in fl)
                        {
                            var fx = rt.InfluenceFixups[fi]; int o = fixOther[fi];
                            int oldC = fx.ClusterTex.Y * 256 + fx.ClusterTex.X;
                            int mem = o >= 0 && oldC < oldFirstMember[o].Length ? oldFirstMember[o][oldC] : -1;
                            int leaf = mem >= 0 && mem < trees[o].LeafNodeOfProbe.Length ? trees[o].LeafNodeOfProbe[mem] : -1;
                            int newC = leaf >= 0 ? trees[o].Nodes[leaf].Slot : -1;
                            if (newC >= 0 && settings.HierarchicalCutFixupPatches)
                            {
                                // Retail's fixups read patches of the neighbouring slice, not single probes: climb from the
                                // leaf to the coarsest ancestor still small enough at this distance, skip one already
                                // covered by this probe's other fixups, and weight it by the same law as in-slice links.
                                var tr = trees[o]; int id = leaf;
                                while (tr.Nodes[id].Parent >= 0)
                                {
                                    var par = tr.Nodes[tr.Nodes[id].Parent];
                                    if (!par.Cluster || par.Slot < 0 || par.Ext / Math.Max(0.05f, (par.C - S).Length()) > T) break;
                                    id = tr.Nodes[id].Parent;
                                }
                                var node = tr.Nodes[id];
                                bool overlap = false;
                                foreach (var c in chosen)
                                {
                                    for (int a = id; a >= 0 && !overlap; a = tr.Nodes[a].Parent) if (tr.Nodes[a] == c) overlap = true;
                                    if (!overlap && c.M.Contains(node.M[0]) && c.M.Length < node.M.Length) overlap = true;
                                }
                                if (overlap) newC = -1;
                                else
                                {
                                    chosen.Add(node); newC = node.Slot;
                                    Vector3 dv = node.C - S; float d = Math.Max(0.1f, dv.Length()); Vector3 dir = dv / d;
                                    float cr = rnp == Vector3.Zero ? 0.5f : Math.Max(0, Vector3.Dot(rnp, dir)), ce = Math.Max(0, Vector3.Dot(node.N, -dir));
                                    double L = Math.Log(d, 2), w = Math.Log(node.M.Length, 2);
                                    double byt = 178.4 - 17.9 * L - 3.0 * L * L - 2.4 * w - 1.03 * w * w + 3.09 * L * w + 26.3 * ce + 10.6 * cr + shift;
                                    fx.Weight = (byte)Math.Round(Math.Max(1, Math.Min(255, byt)));
                                }
                            }
                            if (newC < 0) { fx.Weight = 0; fixDropped++; }
                            else { fx.ClusterTex = new Vector2u8 { X = (byte)(newC % 256), Y = (byte)(newC / 256) }; fixKept++; }
                            fx.WeightTexOffset = p * per + k; fx.InflTexOffset = fx.WeightTexOffset * 2; k++;
                        }
                    }
                }
                log?.Invoke("    hierarchical cut, slice " + s + ": " + links + " links over " + live + " probes (" + (live > 0 ? (double)links / live : 0).ToString("0.0") +
                            "/probe, " + full + " full), byte shift " + shift.ToString("0.0") + ", " + flipped + " from the far side" + (visible != null ? ", " + blind + " blind" : ", no visibility") +
                            (noNormal > 0 ? ", " + noNormal + " without a normal" : ""));
            }
            log?.Invoke("    hierarchical cut: fixups " + fixKept + " re-pointed into free slots, " + fixDropped + " dropped");
        }

        /// <summary>
        /// Free cluster slots (the engine-owned 16x16 atlas corner excluded). Tile-major order fills one 16x16 probe
        /// tile of the 256x64 cluster texture before the next, so N clusters span ceil(N / 256) tiles instead of all 64.
        /// </summary>
        private static List<int> CutFreeSlots(int nc, bool tileMajor)
        {
            var free = new List<int>();
            if (!tileMajor)
            {
                for (int i = 0; i < nc; i++) { int ax = i % AtlasSize, ay = i / AtlasSize; if (ax < 16 && ay < 16) continue; free.Add(i); }
                return free;
            }
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
        /// Make every used cluster slot lie inside the slice's input-probe tiles. minimal: un-clip the real tiles and
        /// append only the tiles the slots reach; otherwise pad to all 64 (PadInputProbeTiles).
        /// </summary>
        private static void CutCoverTiles(List<RadiosityRuntime.ProbeTileDims> tiles, IEnumerable<int> usedSlots, bool minimal)
        {
            if (!minimal) { PadInputProbeTiles(tiles, null); return; }
            int maxTile = tiles.Count - 1;
            foreach (int i in usedSlots)
            {
                int x = i % ProbeTexWidth, y = i / ProbeTexWidth;
                int t = (x / TileSize) * TileRows + (y / TileSize);
                if (t > maxTile) maxTile = t;
            }
            for (int t = 0; t < tiles.Count; t++) { var d = tiles[t]; d.Width = TileSize; d.Height = TileSize; tiles[t] = d; }
            for (int t = tiles.Count; t <= maxTile && t < MaxInputProbeTiles; t++)
            {
                TileRect(t, TileSize * TileSize, out int x, out int y, out int w, out int h);
                tiles.Add(new RadiosityRuntime.ProbeTileDims { X = (byte)x, Y = (byte)y, Width = (byte)w, Height = (byte)h });
            }
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
            public Vector3 C, N, Target;
            public float Ext;
            public int Slot = -1;
            public bool Cluster, Top;
        }

        private sealed class CutTree
        {
            public readonly List<CutNode> Nodes = new List<CutNode>();
            public readonly List<int> Tops = new List<int>();
            public int[] LeafSlotOfProbe;
            public int[] LeafNodeOfProbe;
        }

        private static Vector3 CutNormal(RadiosityRuntime.RuntimeDataSlice sl, int i)
        {
            if (i >= sl.InputProbeNormals.Count) return Vector3.UnitY;
            var nn = sl.InputProbeNormals[i];
            var v = new Vector3((nn.B - 127.5f) / 127.5f, (nn.G - 127.5f) / 127.5f, (nn.R - 127.5f) / 127.5f);
            return v.LengthSquared() > 1e-4f ? Vector3.Normalize(v) : Vector3.UnitY;
        }

        private static CutTree BuildCutTree(RadiosityRuntime.RuntimeDataSlice sl, int wtop, int s, bool normalClasses, int mergeRounds, float mergeNormalWeight, float mergeNormalGate, bool settings_ScatterByProbe, Action<string> log, bool assignSlots = true, bool tileMajorSlots = false, bool keepTiles = true,
                                            Func<Vector3, Vector3, float, float> trace = null, float mergeVisPenalty = 0, float mergeVisLift = 0.05f)
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
            int Build(int[] idx, bool parentIsCluster)
            {
                var node = new CutNode { M = idx };
                Vector3 c = Vector3.Zero, nsum = Vector3.Zero;
                foreach (int i in idx) { c += pos[i]; nsum += nrm[i]; }
                c /= idx.Length; node.C = c;
                node.N = nsum.LengthSquared() > 1e-6f ? Vector3.Normalize(nsum) : Vector3.UnitY;
                double r = 0; foreach (int i in idx) r += (pos[i] - c).LengthSquared();
                node.Ext = (float)Math.Sqrt(r / idx.Length);
                node.Target = idx.Length == 1 ? pos[idx[0]] + nrm[idx[0]] * 0.02f : c + node.N * 0.05f;
                node.Cluster = idx.Length <= wtop;
                node.Top = node.Cluster && !parentIsCluster;
                int id = tree.Nodes.Count; tree.Nodes.Add(node);
                if (node.Top) tree.Tops.Add(id);
                if (idx.Length > 1)
                {
                    Vector3 lo = pos[idx[0]], hi = lo;
                    foreach (int i in idx) { lo = Vector3.Min(lo, pos[i]); hi = Vector3.Max(hi, pos[i]); }
                    Vector3 e = hi - lo; int axis = e.X >= e.Y && e.X >= e.Z ? 0 : e.Y >= e.Z ? 1 : 2;
                    var sorted = idx.OrderBy(i => axis == 0 ? pos[i].X : axis == 1 ? pos[i].Y : pos[i].Z).ToArray();
                    int half = sorted.Length / 2;
                    int l = Build(sorted.Take(half).ToArray(), node.Cluster);
                    int rr = Build(sorted.Skip(half).ToArray(), node.Cluster);
                    node.L = l; node.R = rr;
                    tree.Nodes[l].Parent = id; tree.Nodes[rr].Parent = id;
                }
                return id;
            }
            if (live.Count > 0 && mergeRounds > 0)
            {
                // Bottom-up, as retail builds it (decoded 24 Sep: retail patches are normal-coherent - W2 0.98, W9-16
                // 0.88 - where a position-only k-d tree gives 0.76 / 0.53 and puts both sides of a wall in 61% of its
                // 33-64-member patches; a pairwise merge on rms radius + lambda (1 - normal dot) reproduces retail's
                // coherence and radii). Each round pairs the current clusters greedily by the cheapest union among
                // near neighbours, so patch size at most doubles per round: 7 rounds -> <= 128 members, <= 8
                // memberships per probe, exactly retail's limits.
                // Decoded 24 Sep 2026: retail's tree is ONE global greedy merge of the cheapest ADJACENT pair
                // (24-nearest-neighbour graph), cost = rms radius of the union x (1 + w (1 - dot of the mean normals)),
                // truncated to its lowest 8 levels (the engine's eight-memberships limit; retail's top patches are
                // small and uneven, p50 20-30). Reproduces retail's clusters about twice as well as a k-d tree.
                const int maxHeight = 8;
                var sumP = new List<Vector3>(); var sumPP = new List<double>(); var sumN = new List<Vector3>();
                var cnt = new List<int>(); var height = new List<int>(); var alive = new List<bool>();
                long mergeRays = 0, mergeBlocked = 0, leafRays = 0, leafBlocked = 0; double leafHitSum = 0;
                int AddNode(int[] idx, int count, int l, int r, Vector3 sp, double spp, Vector3 sn, int h)
                {
                    var node = new CutNode { M = idx, L = l, R = r };
                    node.C = sp / count;
                    node.N = sn.LengthSquared() > 1e-6f ? Vector3.Normalize(sn) : Vector3.UnitY;
                    node.Ext = (float)Math.Sqrt(Math.Max(0, spp / count - node.C.LengthSquared()));
                    node.Target = count == 1 && idx != null ? pos[idx[0]] + nrm[idx[0]] * 0.02f : node.C + node.N * 0.05f;
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
                    if (dot <= mergeNormalGate) return double.PositiveInfinity;
                    double cost = Math.Max(rad, 1e-4) * (1 + mergeNormalWeight * (1 - dot));
                    if (trace != null && mergeVisPenalty > 0)
                    {
                        // a WALL between the two clusters, not clutter at one end: both targets lifted off their own surfaces,
                        // and the segment is blocked from both ends by a hit at least 15 cm from either endpoint (a ~22% of
                        // input probes are enclosed by clutter, so a single ray blocked 52-69% of candidate merges)
                        Vector3 ta = sumP[a] / cnt[a] + na * mergeVisLift, tb = sumP[b] / cnt[b] + nb * mergeVisLift, d = tb - ta;
                        float len = d.Length();
                        const float endSlack = 0.15f;
                        if (len > 2 * endSlack + 0.02f)
                        {
                            mergeRays++; bool leaf = cnt[a] == 1 && cnt[b] == 1; if (leaf) leafRays++;
                            float lim = len - endSlack;
                            float hf = trace(ta + d / len * endSlack, d / len, lim - endSlack);
                            if (hf >= 0)
                            {
                                float hb = trace(tb - d / len * endSlack, -d / len, lim - endSlack);
                                if (hb >= 0) { mergeBlocked++; if (leaf) { leafBlocked++; leafHitSum += (hf + endSlack) / len; } cost *= mergeVisPenalty; }
                            }
                        }
                    }
                    return cost;
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
                if (mergeRays > 0) log?.Invoke("    hierarchical cut, slice " + s + " merge visibility: " + mergeBlocked + " of " + mergeRays + " candidate merges blocked (cost x" + mergeVisPenalty + "; leaf pairs " + leafBlocked + " of " + leafRays + ", mean hit at " + (leafHitSum / Math.Max(1, leafBlocked)).ToString("0.00") + " of the way), " + tree.Tops.Count + " roots");
            }
            else if (live.Count > 0)
            {
                if (normalClasses)
                {
                    // Patches never mix facings: split the probes by dominant normal axis (six classes) first, then
                    // spatially within each - a position-only tree put floors, ceilings and both sides of a wall in one
                    // patch and leaked light between rooms (retail links on such a tree: Solace 14.6 vs 11.3).
                    var classes = new List<int>[6];
                    foreach (int i in live)
                    {
                        Vector3 v = nrm[i];
                        int c = Math.Abs(v.X) >= Math.Abs(v.Y) && Math.Abs(v.X) >= Math.Abs(v.Z) ? (v.X >= 0 ? 0 : 1)
                              : Math.Abs(v.Y) >= Math.Abs(v.Z) ? (v.Y >= 0 ? 2 : 3) : (v.Z >= 0 ? 4 : 5);
                        (classes[c] ?? (classes[c] = new List<int>())).Add(i);
                    }
                    foreach (var cl in classes) if (cl != null && cl.Count > 0) Build(cl.ToArray(), false);
                }
                else Build(live.ToArray(), false);
            }

            if (!assignSlots)
            {
                tree.LeafNodeOfProbe = new int[ni];
                for (int i = 0; i < ni; i++) tree.LeafNodeOfProbe[i] = -1;
                for (int id2 = 0; id2 < tree.Nodes.Count; id2++) if (tree.Nodes[id2].Cluster && tree.Nodes[id2].M.Length == 1) tree.LeafNodeOfProbe[tree.Nodes[id2].M[0]] = id2;
                tree.LeafSlotOfProbe = new int[ni];
                for (int i = 0; i < ni; i++) tree.LeafSlotOfProbe[i] = -1;
                return tree;
            }
            int nc = sl.ClusterPositions.Count;
            var free = CutFreeSlots(nc, tileMajorSlots);
            var leaves = tree.Nodes.Where(n => n.Cluster && n.M.Length == 1).ToList();
            var patches = tree.Nodes.Where(n => n.Cluster && n.M.Length > 1).OrderByDescending(n => n.M.Length).ToList();
            int fi = 0, dropped = 0;
            foreach (var n in leaves) if (fi < free.Count) n.Slot = free[fi++];
            foreach (var n in patches) { if (fi < free.Count) n.Slot = free[fi++]; else dropped++; }
            tree.LeafSlotOfProbe = new int[ni];
            for (int i = 0; i < ni; i++) tree.LeafSlotOfProbe[i] = -1;
            foreach (var n in leaves) if (n.Slot >= 0) tree.LeafSlotOfProbe[n.M[0]] = n.Slot;
            tree.LeafNodeOfProbe = new int[ni];
            for (int i = 0; i < ni; i++) tree.LeafNodeOfProbe[i] = -1;
            for (int id2 = 0; id2 < tree.Nodes.Count; id2++) if (tree.Nodes[id2].Cluster && tree.Nodes[id2].M.Length == 1) tree.LeafNodeOfProbe[tree.Nodes[id2].M[0]] = id2;

            for (int i = 0; i < nc; i++) sl.ClusterPositions[i] = new Vector4u16();
            var scatter = new List<ColourRGBA8>();
            int maxPer = 0; var perProbe = new int[ni];
            foreach (var n in tree.Nodes)
            {
                if (!n.Cluster || n.Slot < 0) continue;
                sl.ClusterPositions[n.Slot] = ToHalf4(n.C, n.M.Length);
                foreach (int i in n.M)
                {
                    scatter.Add(new ColourRGBA8 { R = (byte)(n.Slot % 256), G = (byte)(n.Slot / 256), B = (byte)(i % 256), A = (byte)(i / 256) });
                    if (++perProbe[i] > maxPer) maxPer = perProbe[i];
                }
            }
            // Ordered by input probe, as retail's (95%) and every earlier bake of ours (100%) are - the engine appears to walk the
            // scatter per input-probe tile; a cluster-ordered list rendered every rebuilt cluster stage dark.
            if (settings_ScatterByProbe) scatter.Sort((a, b) => { int pa = a.A * 256 + a.B, pb = b.A * 256 + b.B; if (pa != pb) return pa.CompareTo(pb); return (a.G * 256 + a.R).CompareTo(b.G * 256 + b.R); });
            sl.Scatter = scatter;
            if (!keepTiles) CutCoverTiles(sl.InputProbeTiles, tree.Nodes.Where(n => n.Cluster && n.Slot >= 0).Select(n => n.Slot), tileMajorSlots);
            log?.Invoke("    hierarchical cut, slice " + s + " tree: " + live.Count + " probes, " + leaves.Count + " leaves + " + (patches.Count - dropped) + " patches (" +
                        dropped + " smallest dropped for slots), " + tree.Tops.Count + " roots, memberships/probe max " + maxPer + ", scatter " + scatter.Count);
            return tree;
        }
    }
}
#endif
