#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using CATHODE;

namespace CathodeLib.Radiosity
{
    public static partial class RadiosityBaker
    {
        /// <summary>
        /// A delta slice's influence weight byte, from the emitter distance and how squarely the two surfaces face each
        /// other: <c>W0 * (D0/(D0+d))^K</c> (<see cref="RadiosityTuning.InfluenceCurveW0"/>), spread by the facing term.
        /// </summary>
        /// <remarks>
        /// Retail's weights are compressed (the strongest about twice the weakest) but fall monotonically with distance
        /// (Solace, 794,570 links):
        /// <code>
        ///   0-0.5m 205 | 0.5-1m 194 | 1-2m 179 | 2-3m 170 | 3-5m 151
        ///   5-8m   131 | 8-12m  118 | 12-20m 104 | 20-35m  63
        /// </code>
        /// The curve reproduces that within a few units inside 16 m (99.8% of retail's links). A fixed weight-by-rank table
        /// gave every full probe the same total and made the lighting patchy.
        /// </remarks>
        private static byte InfluenceWeight(float distance, float cosProduct)
        {
            float W0 = RadiosityTuning.InfluenceCurveW0;
            float D0 = RadiosityTuning.InfluenceCurveD0;
            float K = RadiosityTuning.InfluenceCurveK;

            float d = Math.Max(0.0f, distance);
            float baseWeight = W0 * (float)Math.Pow(D0 / (D0 + d), K);

            // Retail spreads about +/-14% either side of each distance band's mean (at 2-3 m the
            // mean is 170 with p10 144 and p90 191). The facing term is what varies within a band.
            float facing = (float)Math.Sqrt(Math.Max(0.0f, Math.Min(1.0f, cosProduct)));
            float weight = baseWeight * (0.85f + 0.30f * facing);

            int v = (int)Math.Round(weight);
            return (byte)Math.Max(1, Math.Min(255, v));
        }

        /// <summary>
        /// How far (m) the endpoints of a solve visibility ray are jittered across their surface patches when the centre
        /// ray is blocked. A link joins two texel-sized patches, so a centre-only test kills every link a thin edge or
        /// panel lip crosses (27% of our probes ended with 1-29 links against retail's ~5%, and rendered dark); retail's
        /// links are visibly soft-tested (34.2% of its 0-1 m links pass through render geometry).
        /// </summary>
        private const float SoftVisibilityJitter = 0.22f;

        /// <summary>Extra jittered rays tried after a blocked centre ray.</summary>
        private const int SoftVisibilityRays = 3;

        /// <summary>
        /// Area-to-area visibility: the centre ray first, then a few rays between points jittered
        /// across each endpoint's surface patch. A link is visible when any ray connects.
        /// </summary>
        private static bool VisibleSoft(
            RadiosityGeometry geometry, Vector3 from, Vector3 fromNormal, Vector3 to, Vector3 toNormal,
            int fromTexel, int toTexel)
        {
            if (geometry.Visible(from, to, RadiosityTuning.RayEpsilon))
                return true;

            // Tangent bases for the two patches, so jitter stays in each surface's plane.
            Vector3 fromT1 = Tangent(fromNormal), fromT2 = Vector3.Cross(fromNormal, fromT1);
            Vector3 toT1 = Tangent(toNormal), toT2 = Vector3.Cross(toNormal, toT1);

            uint seed = (uint)(fromTexel * 92837111) ^ (uint)(toTexel * 689287499);
            for (int i = 0; i < SoftVisibilityRays; i++)
            {
                // Two hashed offsets in -1..1 per endpoint, deterministic for the pair.
                seed = seed * 747796405u + 2891336453u;
                float a = ((seed >> 9) & 0x3FF) / 511.5f - 1.0f;
                seed = seed * 747796405u + 2891336453u;
                float b = ((seed >> 9) & 0x3FF) / 511.5f - 1.0f;
                seed = seed * 747796405u + 2891336453u;
                float c = ((seed >> 9) & 0x3FF) / 511.5f - 1.0f;
                seed = seed * 747796405u + 2891336453u;
                float d = ((seed >> 9) & 0x3FF) / 511.5f - 1.0f;

                Vector3 jFrom = from + (fromT1 * a + fromT2 * b) * SoftVisibilityJitter;
                Vector3 jTo = to + (toT1 * c + toT2 * d) * SoftVisibilityJitter;
                if (geometry.Visible(jFrom, jTo, RadiosityTuning.RayEpsilon))
                    return true;
            }
            return false;
        }

        private static Vector3 Tangent(Vector3 normal)
        {
            Vector3 t = Math.Abs(normal.Y) < 0.9f
                ? Vector3.Cross(normal, Vector3.UnitY)
                : Vector3.Cross(normal, Vector3.UnitX);
            float len = t.Length();
            return len > 1e-6f ? t / len : Vector3.UnitX;
        }

        /// <summary>Upper edge, in metres, of each influence distance band.</summary>
        private static readonly float[] InfluenceBandEdges = { 1.0f, 2.0f, 3.0f, 5.0f, 8.0f, 12.0f, 20.0f, float.MaxValue };

        /// <summary>Which band a distance falls in.</summary>
        private static int BandOf(float distance)
        {
            for (int b = 0; b < InfluenceBandEdges.Length - 1; b++)
                if (distance < InfluenceBandEdges[b]) return b;
            return InfluenceBandEdges.Length - 1;
        }

        /// <summary>
        /// A band's integer slot count for one probe, spending the fractional part as a share of
        /// probes rather than rounding it away.
        /// </summary>
        /// <remarks>
        /// A quota of 0.5 means half of all probes get one slot in that band. The dither hashes the probe's atlas texel and
        /// the band: deterministic, and uncorrelated between bands.
        /// </remarks>
        private static int DitheredQuota(float quota, int probeTexel, int band)
        {
            int whole = (int)quota;
            float fraction = quota - whole;
            if (fraction <= 0.0f)
                return whole;

            // 32-bit integer hash (Wang), so neighbouring texels land on unrelated values.
            uint h = (uint)(probeTexel * 73856093) ^ (uint)(band * 19349663);
            h = (h ^ 61) ^ (h >> 16);
            h += h << 3;
            h ^= h >> 4;
            h *= 0x27d4eb2d;
            h ^= h >> 15;

            return whole + ((h & 0xFFFFFF) < (uint)(fraction * 0x1000000) ? 1 : 0);
        }

        /// <summary>
        /// How many of a probe's 32 influence slots retail gives to each distance band, measured
        /// over Solace's 794570 links (27.9 of 32 slots used on average).
        /// </summary>
        private static readonly float[] InfluenceBandQuota = { 2.6f, 6.1f, 6.7f, 6.7f, 3.8f, 1.4f, 0.5f, 0.1f };

        /// <summary>
        /// Reorder a probe's candidates so the kept prefix spans a range of distances, rather than
        /// being whatever happened to rank highest.
        /// </summary>
        /// <remarks>
        /// Ranking purely by form factor ranks by proximity (1/d^2), so a probe in open space without 32 near neighbours
        /// took a fistful of distant emitters at once: we gave long links to 11.0% of probes at 9.1 each, retail to 37.8%
        /// at 2.8. Per-band quotas first, then a backfill from the rest, keep the strongest near contributors and reserve
        /// room for far ones. The fractional quotas are dithered per probe (rounding them sent the far bands to zero on
        /// every probe and brought the clumping back).
        /// </remarks>
        private static void StratifyByDistance(List<(int texel, float weight, float distance, float cosProduct)> candidates,
                                               int keep, int probeTexel)
        {
            if (candidates.Count <= keep)
                return;

            var chosen = new List<int>(keep);
            var taken = new bool[candidates.Count];

            for (int band = 0; band < InfluenceBandEdges.Length && chosen.Count < keep; band++)
            {
                float low = band == 0 ? 0.0f : InfluenceBandEdges[band - 1];
                float high = InfluenceBandEdges[band];
                int quota = DitheredQuota(InfluenceBandQuota[band], probeTexel, band);
                if (quota <= 0)
                    continue;

                // Candidates are already in descending form factor, so the first match in a band
                // is that band's strongest.
                for (int i = 0; i < candidates.Count && quota > 0 && chosen.Count < keep; i++)
                {
                    if (taken[i]) continue;
                    float d = candidates[i].distance;
                    if (d < low || d >= high) continue;
                    taken[i] = true;
                    chosen.Add(i);
                    quota--;
                }
            }

            // Bands that were empty leave slots over; give them to the strongest remaining.
            for (int i = 0; i < candidates.Count && chosen.Count < keep; i++)
            {
                if (taken[i]) continue;
                taken[i] = true;
                chosen.Add(i);
            }

            // Move the selection to the front, strongest first, so the caller's prefix is it.
            var selected = new List<(int texel, float weight, float distance, float cosProduct)>(chosen.Count);
            foreach (int i in chosen) selected.Add(candidates[i]);
            selected.Sort((a, b) => b.weight.CompareTo(a.weight));
            for (int i = 0; i < selected.Count; i++) candidates[i] = selected[i];
        }

        #region VISIBILITY SOLVE

        /// <summary>A coarse cluster: a patch of input probes carried in one free cluster slot.</summary>
        private sealed class CoarseCluster
        {
            public int Slot;                 // cluster id (a dead atlas texel index outside the engine corner)
            public Vector3 Position;         // member centroid
            public Vector3 Normal;           // mean member normal
            public Vector3 RayOrigin;        // centroid lifted off the surface for visibility rays
            public readonly List<int> Members = new List<int>();
            public int W => Members.Count;
        }

        /// <summary>
        /// Build coarse clusters over the input probes - retail's hierarchy above the texel clusters.
        /// For each rung of <see cref="RadiosityTuning.CoarseClusterRungs"/> (largest first),
        /// probes are bucketed on a world grid of that cell size and, within a cell, by dominant
        /// normal axis; a bucket with at least <see cref="RadiosityTuning.CoarseClusterMinMembers"/>
        /// probes becomes a cluster whose
        /// position is the member centroid and whose W is the member count. Each takes a DEAD atlas
        /// texel index outside the engine-owned corner as its slot, so nothing live is displaced.
        /// </summary>
        /// <remarks>
        /// Retail's clusters carry W = 1..90 members and a surface probe's 32 links reach coarse patches at any distance:
        /// that is how one light lights a whole room (it rides every cluster its probe belongs to). Full bakes only, offered
        /// to the solve as area-weighted candidates; the cut then rewrites every cluster and link, so what they still
        /// decide is which surface probes have no link (the dead-texel repoint in BakeSlice).
        /// </remarks>
        private static List<CoarseCluster> BuildCoarseClusters(SurfaceTexel[] texels, List<ProbePoint> probes,
                                                              RadiosityRuntime.RuntimeDataSlice slice,
                                                              Action<string> log)
        {
            var result = new List<CoarseCluster>();
            if (probes.Count == 0) return result;

            // Free slots: dead atlas texels outside the 16x16 engine corner, highest first. A cluster id is an atlas texel
            // id here (ClusterRef), which the engine reads as a 256x64 texture in 16x16 tiles; it clears and updates that
            // texture per input-probe tile, so a slot past the last tile is never cleared, and a patch there saturated
            // CM5/CM11 (17 Sep 2026 bisect). Only slots inside the slice's InputProbeTiles are used; a bucket that finds
            // none is dropped.
            var free = new Stack<int>();
            int outsideUnused = 0;
            int tileCount = slice.InputProbeTiles?.Count ?? 0;
            bool InsideTiles(int cx, int cy)
            {
                foreach (RadiosityRuntime.ProbeTileDims t in slice.InputProbeTiles)
                    if (cx >= t.X && cx < t.X + t.Width && cy >= t.Y && cy < t.Y + t.Height) return true;
                return false;
            }
            for (int i = 0; i < AtlasTexels; i++)
            {
                if (texels[i].Live && (i >= slice.ClusterPositions.Count || slice.ClusterPositions[i].W != 0)) continue;   // a live texel that is not a cluster frees its slot
                int ax = i % AtlasSize, ay = i / AtlasSize;
                if (ax < 16 && ay < 16) continue;
                if (!InsideTiles(i % ProbeTexWidth, i / ProbeTexWidth)) outsideUnused++;
                else free.Push(i);
            }
            int insideSlots = free.Count;

            // Retail nests every input probe in a binary ladder of clusters (W 1, 2, 3-4 ... 33-64; 99% of small clusters
            // lie wholly inside a bigger one) and its rooms gather from ~1000 distinct patches. These spatial rungs give
            // overlapping patches instead (texel clusters stay the fine level); largest rung FIRST, so the far-carrying
            // patches take slots first.
            int minMembers = RadiosityTuning.CoarseClusterMinMembers;
            foreach (float cell in RadiosityTuning.CoarseClusterRungs)
            {
                // Bucket: (cell x, y, z, dominant axis with sign)
                var buckets = new Dictionary<(int, int, int, int), List<int>>();
                for (int p = 0; p < probes.Count; p++)
                {
                    Vector3 q = probes[p].Position, n = probes[p].Normal;
                    int axis = Math.Abs(n.X) >= Math.Abs(n.Y) && Math.Abs(n.X) >= Math.Abs(n.Z) ? (n.X >= 0 ? 0 : 1)
                             : Math.Abs(n.Y) >= Math.Abs(n.Z) ? (n.Y >= 0 ? 2 : 3) : (n.Z >= 0 ? 4 : 5);
                    var key = ((int)Math.Floor(q.X / cell), (int)Math.Floor(q.Y / cell), (int)Math.Floor(q.Z / cell), axis);
                    if (!buckets.TryGetValue(key, out var l)) buckets[key] = l = new List<int>();
                    l.Add(p);
                }

                int first = result.Count;
                // Pop order: highest indices first (push ascending, pop descending).
                int noSlot = 0;
                // Biggest bucket first, each one whole: no member cap and no split. Retail's biggest
                // patch has 128 members and it reaches that by NESTING; truncating buckets to a cap lost
                // the corridors' fill (Solace 13.57 -> 13.92, ChallengeMap5 14.00 -> 14.79).
                foreach (var kv in buckets.OrderByDescending(k => k.Value.Count))
                {
                    if (kv.Value.Count < minMembers) continue;
                    if (free.Count == 0) { noSlot++; continue; }
                    var c = new CoarseCluster { Slot = free.Pop() };
                    Vector3 sum = Vector3.Zero, nsum = Vector3.Zero;
                    foreach (int p in kv.Value) { sum += probes[p].Position; nsum += probes[p].Normal; c.Members.Add(p); }
                    c.Position = sum / c.Members.Count;
                    c.Normal = nsum.LengthSquared() > 1e-6f ? Vector3.Normalize(nsum) : Vector3.UnitY;
                    c.RayOrigin = c.Position + c.Normal * 0.05f;
                    slice.ClusterPositions[c.Slot] = ToHalf4(c.Position, c.W);
                    result.Add(c);
                }
                if (result.Count > first)
                {
                    var ws = result.Skip(first).Select(c => c.W).OrderBy(w => w).ToList();
                    log?.Invoke("    coarse clusters: " + (result.Count - first) + " over " + buckets.Count + " buckets (cell " + cell.ToString("0.0") + " m, min " + minMembers +
                                "), W p10/50/90 " + ws[ws.Count / 10] + "/" + ws[ws.Count / 2] + "/" + ws[ws.Count * 9 / 10] + " max " + ws[ws.Count - 1] +
                                (noSlot > 0 ? ", " + noSlot + " buckets had no free slot" : "") +
                                ", slots inside the " + tileCount + " probe tiles: " + insideSlots + " free (" + outsideUnused + " outside left unused)");
                }
            }
            return result;
        }

        /// <summary>
        /// For every live surface probe, find the clusters that can see it and store the strongest
        /// <see cref="InfluencesPerProbe"/> of them as (clusterXY, weight) pairs. Returns the links written.
        /// </summary>
        /// <param name="surfaceSlotForTexel">
        /// Atlas texel to surface probe slot. The influence maps are keyed by slot, not by texel -
        /// every one of the 1195115 populated retail probe records sits at a live surface probe.
        /// </param>
        /// <param name="existenceOnly">
        /// The full bake's test. RebuildLinksRetailCut rewrites every link, so all a full bake keeps of the solve is which
        /// probes have NO link (BakeSlice's dead-texel repoint). Each probe stops at its first candidate that passes the
        /// same facing, form-factor and visibility tests (fine clusters, then coarse); nothing is sorted or written,
        /// <paramref name="usedSlots"/> holds 1 or 0, and the return value is the probes with a link. The zero-link set is
        /// the full solve's: a probe has no link exactly when no candidate passes, and VisibleSoft depends only on its
        /// endpoints, so the order the candidates are tested in cannot change it.
        /// </param>
        /// <remarks>
        /// <paramref name="usedSlots"/> comes back keyed by surface probe slot so a delta slice's cross-slice
        /// pass (EmitPairFixups) can carry on filling the same records.
        /// </remarks>
        private static int SolveInfluences(
            RadiosityGeometry geometry, SurfaceTexel[] texels, int[] surfaceSlotForTexel,
            int[] inputProbeForTexel,
            RadiosityRuntime.RuntimeDataSlice slice,
            out byte[] usedSlots, Action<string> log, List<CoarseCluster> coarse, bool existenceOnly)
        {
            usedSlots = new byte[AtlasTexels];
            byte[] used = usedSlots;
            slice.SurfaceProbeInfluences = NewList<ColourRGBA8>(AtlasTexels * InfluencesPerProbe / 2);
            slice.SurfaceProbeWeights = NewList<Vector4u8>(AtlasTexels * InfluencesPerProbe / 4);

            var live = new List<int>();
            for (int i = 0; i < AtlasTexels; i++) if (texels[i].Live && surfaceSlotForTexel[i] >= 0) live.Add(i);
            if (live.Count == 0)
                return 0;

            // Only a texel that became a cluster can be named as an influence, so the emitter grid
            // is the cluster set - not the receiver set.
            var emitters = new List<int>();
            for (int i = 0; i < AtlasTexels; i++) if (texels[i].Live && inputProbeForTexel[i] >= 0) emitters.Add(i);

            // Uniform grid over the emitters so each receiver only tests nearby candidates.
            var grid = new ProbeGrid(texels, emitters, RadiosityTuning.MaxInfluenceDistance);

            int total = 0;
            object totalLock = new object();

            // A probe that ends up with no influence renders with no bounced light, and next to a
            // lit one that is a hard edge. Counting why they fail separates "nothing faces it" from
            // "everything that faces it is occluded", which want different fixes.
            int noCandidates = 0, allOccluded = 0, noneFacing = 0;

            // Visibility rays cast per receiver, and how far they reached. One ray is cast per
            // candidate that survives the distance and mutual-facing tests, so this is the solve's
            // real cost and the honest answer to "how many rays per probe".
            var raysPerProbe = new int[live.Count];
            var rayLengthSum = new double[live.Count];
            var rayLengthMax = new float[live.Count];

            // Retail's influence count per probe is bimodal - a probe is either empty or full at
            // 30-32 - where ours ramps through every value in between. To tell whether the thin
            // ones are starved of candidates or drowning in occlusion, record for each probe how
            // many candidates reached the ray test and how many survived it.
            var facedPerProbe = new int[live.Count];
            var keptPerProbe = new int[live.Count];
            var nearestBlockedPerProbe = new float[live.Count];

            // Supply against spend, per distance band. The near bands are the ones that carry the
            // energy, so a shortfall there is not cosmetic: whatever a band cannot fill is handed
            // to the backfill, which spends it further out. Telling "no candidates were offered"
            // apart from "candidates were offered and passed over" needs both halves counted.
            var bandOffered = new long[InfluenceBandEdges.Length];
            var bandTaken = new long[InfluenceBandEdges.Length];

            void Solve(int liveIndex)
            {
                int probeTexel = live[liveIndex];
                SurfaceTexel probe = texels[probeTexel];
                Vector3 origin = probe.RayOrigin;

                int facing = 0, occluded = 0;
                var candidates = new List<(int texel, float weight, float distance, float cosProduct)>();
                foreach (int otherTexel in grid.Neighbours(probe.Position))
                {
                    if (otherTexel == probeTexel)
                        continue;

                    SurfaceTexel other = texels[otherTexel];
                    Vector3 delta = other.Position - origin;
                    float distanceSq = delta.LengthSquared();
                    if (distanceSq < 1e-6f || distanceSq > RadiosityTuning.MaxInfluenceDistance * RadiosityTuning.MaxInfluenceDistance)
                        continue;

                    float distance = (float)Math.Sqrt(distanceSq);
                    Vector3 direction = delta / distance;

                    // Diffuse form factor: both surfaces must face each other.
                    float cosReceiver = Vector3.Dot(probe.Normal, direction);
                    if (cosReceiver <= 0.02f)
                        continue;
                    float cosEmitter = Vector3.Dot(other.Normal, -direction);
                    if (cosEmitter <= 0.02f)
                        continue;

                    float formFactor = cosReceiver * cosEmitter / (float)(Math.PI * distanceSq);
                    if (formFactor <= 1e-5f)
                        continue;

                    facing++;
                    raysPerProbe[liveIndex]++;
                    rayLengthSum[liveIndex] += distance;
                    if (distance > rayLengthMax[liveIndex]) rayLengthMax[liveIndex] = distance;

                    if (!VisibleSoft(geometry, origin, probe.Normal, other.RayOrigin, other.Normal,
                                     probeTexel, otherTexel))
                    {
                        occluded++;
                        if (nearestBlockedPerProbe[liveIndex] == 0f || distance < nearestBlockedPerProbe[liveIndex])
                            nearestBlockedPerProbe[liveIndex] = distance;
                        continue;
                    }
                    if (existenceOnly) { used[surfaceSlotForTexel[probeTexel]] = 1; return; }

                    candidates.Add((otherTexel, formFactor, distance, cosReceiver * cosEmitter));
                }

                // Coarse clusters as candidates: area-weighted (W members), visibility to the centroid,
                // within twice the single-texel reach because a patch is read from further away.
                if (coarse != null && coarse.Count > 0)
                {
                    float reach = RadiosityTuning.MaxInfluenceDistance * 2.0f;
                    foreach (CoarseCluster cc in coarse)
                    {
                        Vector3 delta = cc.Position - origin;
                        float distanceSq = delta.LengthSquared();
                        if (distanceSq < 0.25f || distanceSq > reach * reach) continue;
                        float distance = (float)Math.Sqrt(distanceSq);
                        Vector3 direction = delta / distance;
                        float cosReceiver = Vector3.Dot(probe.Normal, direction);
                        if (cosReceiver <= 0.02f) continue;
                        float cosEmitter = Vector3.Dot(cc.Normal, -direction);
                        if (cosEmitter <= 0.02f) continue;
                        float formFactor = cc.W * cosReceiver * cosEmitter / (float)(Math.PI * distanceSq);
                        if (formFactor <= 1e-5f) continue;
                        facing++;
                        raysPerProbe[liveIndex]++;
                        if (!VisibleSoft(geometry, origin, probe.Normal, cc.RayOrigin, cc.Normal, probeTexel, cc.Slot))
                        {
                            occluded++;
                            continue;
                        }
                        if (existenceOnly) { used[surfaceSlotForTexel[probeTexel]] = 1; return; }
                        candidates.Add((cc.Slot, formFactor, distance, cosReceiver * cosEmitter));
                    }
                }

                facedPerProbe[liveIndex] = facing;
                keptPerProbe[liveIndex] = candidates.Count;

                if (candidates.Count == 0)
                {
                    lock (totalLock)
                    {
                        noCandidates++;
                        if (facing == 0) noneFacing++;
                        else if (occluded == facing) allOccluded++;
                    }
                    return;
                }

                candidates.Sort((a, b) => b.weight.CompareTo(a.weight));
                if (candidates[0].weight <= 0)
                    return;

                int keep = Math.Min(InfluencesPerProbe, candidates.Count);
                StratifyByDistance(candidates, keep, probeTexel);

                // Influence weights are absolute gains, not a normalised kernel: scaling a probe's weights to retail's
                // per-probe total brightened the render ~1.7x. Retail's tight weight-sum band comes from nearly every
                // probe carrying ~32 links.
                for (int k = 0; k < keep; k++)
                {
                    int otherTexel = candidates[k].texel;
                    ClusterRef(otherTexel, out byte cx, out byte cy);
                    byte weight = InfluenceWeight(candidates[k].distance, candidates[k].cosProduct);

                    int influenceSlot = surfaceSlotForTexel[probeTexel] * InfluencesPerProbe + k;
                    WriteInfluence(slice, influenceSlot, cx, cy, weight);
                }

                used[surfaceSlotForTexel[probeTexel]] = (byte)keep;

                if (log != null)
                {
                    var offered = new int[InfluenceBandEdges.Length];
                    var taken = new int[InfluenceBandEdges.Length];
                    for (int i = 0; i < candidates.Count; i++)
                        offered[BandOf(candidates[i].distance)]++;
                    for (int k = 0; k < keep; k++)
                        taken[BandOf(candidates[k].distance)]++;
                    lock (totalLock)
                    {
                        total += keep;
                        for (int b = 0; b < InfluenceBandEdges.Length; b++)
                        {
                            bandOffered[b] += offered[b];
                            bandTaken[b] += taken[b];
                        }
                    }
                    return;
                }

                lock (totalLock) total += keep;
            }

            Parallel.For(0, live.Count, Solve);

            // The ray, kept-count and band diagnostics describe a full solve; the existence test stops at each probe's first
            // link. Its zero-link probes still tested every candidate, so the no-influence breakdown below holds for both.
            if (log != null && !existenceOnly)
            {
                long totalRays = 0;
                double totalLength = 0;
                float longest = 0;
                var sorted = new int[live.Count];
                for (int i = 0; i < live.Count; i++)
                {
                    totalRays += raysPerProbe[i];
                    totalLength += rayLengthSum[i];
                    if (rayLengthMax[i] > longest) longest = rayLengthMax[i];
                    sorted[i] = raysPerProbe[i];
                }
                Array.Sort(sorted);
                var deciles = new string[11];
                for (int i = 0; i < 11; i++)
                    deciles[i] = sorted[Math.Min(sorted.Length - 1, (int)((long)i * sorted.Length / 10))].ToString();

                // Bucket probes by how many influences they ended with, and show what the ray test
                // was given to work with in each bucket. Starved candidates and heavy occlusion
                // want opposite fixes.
                int[] edges = { 1, 4, 8, 16, 32, int.MaxValue };
                string[] names = { "0", "1-3", "4-7", "8-15", "16-31", "32" };
                var bucketProbes = new int[edges.Length];
                var bucketFaced = new double[edges.Length];
                var bucketBlocked = new double[edges.Length];
                for (int i = 0; i < live.Count; i++)
                {
                    int kept = keptPerProbe[i];
                    int b = 0;
                    while (b < edges.Length - 1 && kept >= edges[b]) b++;
                    if (kept == 0) b = 0;
                    bucketProbes[b]++;
                    bucketFaced[b] += facedPerProbe[i];
                    bucketBlocked[b] += nearestBlockedPerProbe[i];
                }
                for (int b = 0; b < edges.Length; b++)
                {
                    if (bucketProbes[b] == 0) continue;
                    log?.Invoke("      kept " + names[b].PadRight(6) +
                                bucketProbes[b].ToString().PadLeft(6) + " probes  (" +
                                (100.0 * bucketProbes[b] / live.Count).ToString("0.0").PadLeft(5) + "%)" +
                                "   candidates offered " + (bucketFaced[b] / bucketProbes[b]).ToString("0").PadLeft(5) +
                                "   nearest blocked at " + (bucketBlocked[b] / bucketProbes[b]).ToString("0.00") + " m");
                }

                long offeredAll = 0, takenAll = 0;
                for (int b = 0; b < InfluenceBandEdges.Length; b++) { offeredAll += bandOffered[b]; takenAll += bandTaken[b]; }
                log?.Invoke("    influence bands   quota   offered   taken   share of slots   retail share");
                float quotaSum = 0;
                for (int b = 0; b < InfluenceBandEdges.Length; b++) quotaSum += InfluenceBandQuota[b];
                for (int b = 0; b < InfluenceBandEdges.Length; b++)
                {
                    string range = (b == 0 ? "0" : InfluenceBandEdges[b - 1].ToString("0")) + "-" +
                                   (InfluenceBandEdges[b] == float.MaxValue ? "inf" : InfluenceBandEdges[b].ToString("0"));
                    log?.Invoke("      " + range.PadRight(10) +
                                InfluenceBandQuota[b].ToString("0.0").PadLeft(9) +
                                bandOffered[b].ToString().PadLeft(10) +
                                bandTaken[b].ToString().PadLeft(9) +
                                (100.0 * bandTaken[b] / Math.Max(1, takenAll)).ToString("0.0").PadLeft(14) + "%" +
                                (100.0 * InfluenceBandQuota[b] / quotaSum).ToString("0.0").PadLeft(14) + "%");
                }

                log?.Invoke("    visibility rays: " + totalRays + " total, " +
                            (totalRays / (double)live.Count).ToString("0") + " per probe" +
                            "   deciles " + string.Join(" ", deciles) +
                            "   mean length " + (totalRays == 0 ? 0 : totalLength / totalRays).ToString("0.00") +
                            " m, longest " + longest.ToString("0.0") + " m of " +
                            RadiosityTuning.MaxInfluenceDistance.ToString("0") + " m allowed");
            }

            if (noCandidates > 0)
                log?.Invoke("    probes left with no influence: " + noCandidates + " of " + live.Count +
                            " (" + (100.0 * noCandidates / live.Count).ToString("0.0") + "%)" +
                            "   nothing facing them: " + noneFacing +
                            "   everything facing them occluded: " + allOccluded);

            return existenceOnly ? live.Count - noCandidates : total;
        }

        /// <summary>
        /// A delta slice's scatter: every input probe gathers from its local cluster neighbourhood directly, retail-shaped.
        /// </summary>
        /// <remarks>
        /// <para>The scatter point list consumed by CA_RADIOSITY_INDIRECT_SCATTER. Each entry is <c>(R, G, B, A)</c>:
        /// <c>(R, G)</c> the source cluster (<c>G * 256 + R</c>), <c>(B, A)</c> the destination input probe texel
        /// (<c>A * 256 + B</c>). One probe's entries are contiguous, at most eight. In all 128 retail slices every source is
        /// a live cluster, every destination a live input probe inside a declared tile, and the sources are exactly the
        /// live clusters. No retail slice ships it empty (the smallest holds 502 entries), but an empty list still loads.</para>
        /// <para>Retail's shape (Solace): ~6 sources per probe at p50 0.75 m / p99 5.4 m, every input probe covered. This
        /// hop feeds everything downstream of the lightmap (input probe radiance, volume probes, every RADIOSITY_DYNAMIC
        /// prop). Sources prefer same-facing nearby clusters, falling back to a visibility test on a normal-disjoint
        /// neighbourhood; every live cluster then gets a destination through its own bound input probe.</para>
        /// </remarks>
        private static List<ColourRGBA8> BuildScatterListLocal(
            RadiosityGeometry geometry,
            List<ProbePoint> probes,
            SurfaceTexel[] texels,
            int[] probeForTexel)
        {
            var scatter = new List<ColourRGBA8>();
            int cap = RadiosityTuning.MaxScatterTargetsPerProbe;
            int solveCap = Math.Max(1, cap - 1);
            float radius = RadiosityTuning.LocalScatterRadius;
            float reach = RadiosityTuning.LocalScatterReachRadius;

            var clusterTexels = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
                if (texels[i].Live && probeForTexel[i] >= 0) clusterTexels.Add(i);
            if (clusterTexels.Count == 0)
                return scatter;

            // The grid is built at the outer reach so the mid and far bands can query it; the near neighbourhood is still
            // bounded by `radius`. The long-link tail reaches to twice ScatterMaxLinkDistance (retail ships links to 11.2 m).
            float candidateReach = ScatterMaxLinkDistance * 2;
            var grid = new ProbeGrid(texels, clusterTexels, candidateReach);
            var groups = new Dictionary<int, List<(int cluster, float weight)>>(probes.Count);
            var coveredClusters = new HashSet<int>();

            for (int p = 0; p < probes.Count; p++)
            {
                Vector3 origin = probes[p].Position;
                Vector3 normal = probes[p].Normal;

                var near = new List<(int cluster, float d2, bool agrees)>();
                foreach (int texel in grid.Neighbours(origin))
                {
                    float d2 = Vector3.DistanceSquared(texels[texel].Position, origin);
                    if (d2 > candidateReach * candidateReach)
                        continue;
                    bool agrees = Vector3.Dot(texels[texel].Normal, normal) > 0.2f;
                    near.Add((texel, d2, agrees));
                }
                near.Sort((a, b) => a.d2.CompareTo(b.d2));

                float baseRadiusSq = radius * radius;
                var chosen = new List<(int cluster, float weight)>();

                // Band-stratified selection: EVERY probe gets near, mid and far sources. Retail's link lengths (p50 0.75 /
                // p90 2.45 / p99 6.1 m, a 3.3x p90/p50 ratio no single ball produces) decompose as this mixture: with 7
                // links, p90 is the mid link deep in the 1.25-3 m band and p99 the far link. Scatter is the only structure
                // coupling surface lights into the standing field (emptying retail's list collapses its render 3.008 ->
                // 0.359), and an all-local ball delivered ~0.6x retail's gather mass per light.
                float midSq = reach * reach;
                float farSq = ScatterMaxLinkDistance * ScatterMaxLinkDistance;
                int farQ = 1, midQ = 1;
                int nearQ = Math.Max(1, solveCap - midQ - farQ);

                // NEAR: the probe's own surface neighbourhood - same-facing, nearest-first.
                foreach ((int cluster, float d2, bool agrees) in near)
                {
                    if (chosen.Count >= nearQ)
                        break;
                    if (agrees && d2 <= baseRadiusSq)
                        chosen.Add((cluster, 1.0f / (1.0f + d2)));
                }
                // Normal-disjoint neighbourhood (a probe on a lone strut): take what is visible.
                if (chosen.Count < Math.Min(3, nearQ))
                {
                    foreach ((int cluster, float d2, bool agrees) in near)
                    {
                        if (chosen.Count >= nearQ)
                            break;
                        if (agrees || d2 > baseRadiusSq || chosen.Exists(c => c.cluster == cluster))
                            continue;
                        if (geometry.Visible(origin + normal * RadiosityTuning.ProbeSurfaceOffset,
                                             texels[cluster].Position + texels[cluster].Normal * RadiosityTuning.ProbeSurfaceOffset,
                                             RadiosityTuning.RayEpsilon))
                            chosen.Add((cluster, 1.0f / (1.0f + d2)));
                    }
                }

                // MID and FAR: the room, not the surface. A far source FACES the probe and must be visible: a link carries
                // radiance with no runtime occlusion, so an unchecked one lights the next room through the wall. Each band's
                // pick walks outward from the band's MIDDLE: retail's link-length shape is the same on every level measured
                // and its p90 sits mid-band (furthest-first over-coupled ChallengeMap9 to 1.25x). Returns the links added;
                // targetSq >= 0 centres the walk on the candidate nearest that length instead (the long-tail pick below).
                int Band(float loSq, float hiSq, int quota, float targetSq = -1f)
                {
                    int lo = -1, hi = -1;   // candidate index range inside the band
                    for (int i = 0; i < near.Count; i++)
                    {
                        if (near[i].d2 <= loSq) continue;
                        if (near[i].d2 > hiSq) break;
                        if (lo < 0) lo = i;
                        hi = i;
                    }
                    if (lo < 0)
                        return 0;

                    int added = 0;
                    int target = Math.Min(solveCap, chosen.Count + quota);
                    int mid = (lo + hi) / 2;
                    if (targetSq >= 0f)
                    {
                        mid = lo;
                        for (int i = lo + 1; i <= hi; i++)
                            if (Math.Abs(near[i].d2 - targetSq) < Math.Abs(near[mid].d2 - targetSq))
                                mid = i;
                    }
                    for (int step = 0; step <= hi - lo && chosen.Count < target; step++)
                    {
                        // middle outward: mid, mid+1, mid-1, mid+2, ...
                        int i = mid + (step % 2 == 1 ? (step + 1) / 2 : -(step / 2));
                        if (i < lo || i > hi)
                            continue;
                        (int cluster, float d2, bool _) = near[i];
                        if (chosen.Exists(c => c.cluster == cluster))
                            continue;
                        Vector3 toProbe = origin - texels[cluster].Position;
                        if (Vector3.Dot(texels[cluster].Normal, toProbe) <= 0.0f)
                            continue;
                        if (!geometry.Visible(origin + normal * RadiosityTuning.ProbeSurfaceOffset,
                                              texels[cluster].Position + texels[cluster].Normal * RadiosityTuning.ProbeSurfaceOffset,
                                              RadiosityTuning.RayEpsilon))
                            continue;
                        chosen.Add((cluster, 1.0f / (1.0f + d2)));
                        added++;
                    }
                    return added;
                }
                int midAdded = Band(baseRadiusSq, midSq, midQ);
                // FAR is a MINORITY QUOTA, not just a fallback: retail puts a >3 m link on 25-31% of destination probes, and
                // those links stitch the scatter graph into one component (as a pure fallback every link capped at ~2.8 m
                // and the graph fragmented at doorways). A per-probe dither picks the minority; mid-starved probes take
                // the tail regardless.
                bool farFires = ((uint)p * 2654435761u) % 100u <
                                (uint)Math.Round(RadiosityTuning.ScatterFarBandFraction * 100.0f);
                // ULTRA-FAR: a second, independent dither for the thin tail beyond ScatterMaxLinkDistance (~1% of retail's
                // links exceed 6 m; around Solace's cold cam13 room our 6 m cap carried half retail's crossing count).
                bool ultraFires = ((uint)p * 2246822519u) % 100u <
                                  (uint)Math.Round(RadiosityTuning.ScatterUltraFarFraction * 100.0f);
                // ONE decaying tail, not two banded picks: across 12 retail slices the long-link length is a single
                // distribution, floor + Exp(1.4 m) (p10 3.1, p50 3.8-4.2, p90 5.9-7.8 m). Two banded picks piled links at
                // 4.5 and 9 m and doubled retail's >4 m rate. The per-probe hash draws the target length; the outward walk
                // keeps the facing and visibility tests.
                if (farFires || ultraFires || midAdded == 0)
                {
                    uint th = (uint)p * 3266489917u;
                    float u = ((th >> 8) % 10000u + 0.5f) / 10000.0f;
                    float tail = reach + RadiosityTuning.ScatterLongLinkMean * (0.0f - (float)Math.Log(u));
                    float tailCap = ScatterMaxLinkDistance * 2.0f;
                    if (tail > tailCap) tail = tailCap;
                    Band(midSq, farSq * 4.0f, farQ, tail * tail);
                }

                // Last resort: an unfed probe is a hole in the field, so take the nearest clusters
                // on any terms rather than leave it dark. Retail leaves 0.2% of its probes unfed;
                // a plain radius ball left 5% of ours.
                if (chosen.Count == 0)
                {
                    foreach ((int cluster, float d2, bool agrees) in near)
                    {
                        if (chosen.Count >= Math.Min(3, solveCap))
                            break;
                        chosen.Add((cluster, 1.0f / (1.0f + d2)));
                    }
                }

                if (chosen.Count == 0)
                    continue;
                groups[p] = chosen;
                foreach ((int cluster, float _) in chosen)
                    coveredClusters.Add(cluster);
            }

            // Source-side invariant: every live cluster appears somewhere. Its own bound input
            // probe is the natural home.
            foreach (int cluster in clusterTexels)
            {
                if (coveredClusters.Contains(cluster))
                    continue;
                int probe = probeForTexel[cluster];
                if (probe < 0 || probe >= probes.Count)
                    continue;
                if (!groups.TryGetValue(probe, out List<(int cluster, float weight)> group))
                    groups[probe] = group = new List<(int cluster, float weight)>();
                if (group.Count < cap)
                    group.Add((cluster, 0.0f));
            }

            // No entry ceiling to enforce: <= 8 per group x <= 16384 input probes = 131072, the engine's
            // scatter vertex limit (no 16-bit index: the busiest retail slice holds 62661).
            foreach (int probe in groups.Keys.OrderBy(k => k))
            {
                InputProbeTexel(probe, out int dx, out int dy);
                foreach ((int cluster, float _) in groups[probe])
                {
                    ClusterRef(cluster, out byte cx, out byte cy);
                    scatter.Add(new ColourRGBA8 { R = cx, G = cy, B = (byte)dx, A = (byte)dy });
                }
            }
            return scatter;
        }

        /// <summary>Rewrite only the weight byte of an influence slot, keeping its cluster index.</summary>
        private static void WriteInfluenceWeight(RadiosityRuntime.RuntimeDataSlice slice, int influenceSlot, byte weight)
        {
            Vector4u8 weights = slice.SurfaceProbeWeights[influenceSlot / 4];
            switch (influenceSlot & 3)
            {
                case 0: weights.X = weight; break;
                case 1: weights.Y = weight; break;
                case 2: weights.Z = weight; break;
                default: weights.W = weight; break;
            }
            slice.SurfaceProbeWeights[influenceSlot / 4] = weights;
        }

        /// <summary>Weight byte previously written for an influence slot.</summary>
        /// <remarks>
        /// The engine's decode of the byte, from the CA_RADIOSITY_INDIRECT pixel shader (reconstructed
        /// master in ShadersDatGenerator): gain = <c>exp2(b / 255 * 22.6096) * 1.48436e-7 - 1.48437e-7</c>.
        /// Every +11.28 bytes doubles the gain; a byte of 255 alone is 0.95, 240 is 0.38, 205 is 0.044,
        /// 170 is 0.005. A probe's 32 gains are SUMMED with no normalisation, times the cluster's
        /// irradiance. (Not 2^(w/32) as the delta calibration assumed - that curve is 2.8x too gentle.)
        /// The cut's gain envelope (RebuildLinksRetailCut) works in this decode.
        /// </remarks>
        private static byte ReadInfluenceWeight(RadiosityRuntime.RuntimeDataSlice slice, int influenceSlot)
        {
            Vector4u8 weights = slice.SurfaceProbeWeights[influenceSlot / 4];
            switch (influenceSlot & 3)
            {
                case 0: return weights.X;
                case 1: return weights.Y;
                case 2: return weights.Z;
                default: return weights.W;
            }
        }

        /// <summary>
        /// The index map holds two bytes per influence (cluster x, y) and the weight map one, so
        /// influence <c>n</c> lands in index-map element <c>n/2</c> and weight element <c>n/4</c>.
        /// </summary>
        private static void WriteInfluence(RadiosityRuntime.RuntimeDataSlice slice, int influenceSlot, byte x, byte y, byte weight)
        {
            ColourRGBA8 index = slice.SurfaceProbeInfluences[influenceSlot / 2];
            if ((influenceSlot & 1) == 0) { index.R = x; index.G = y; }
            else { index.B = x; index.A = y; }

            Vector4u8 weights = slice.SurfaceProbeWeights[influenceSlot / 4];
            switch (influenceSlot & 3)
            {
                case 0: weights.X = weight; break;
                case 1: weights.Y = weight; break;
                case 2: weights.Z = weight; break;
                default: weights.W = weight; break;
            }
        }

        /// <summary>Uniform grid over live probe texels, for neighbour queries during the solve.</summary>
        private sealed class ProbeGrid
        {
            private readonly Dictionary<(int, int, int), List<int>> _cells = new Dictionary<(int, int, int), List<int>>();
            private readonly float _cellSize;

            public ProbeGrid(SurfaceTexel[] texels, List<int> live, float radius)
            {
                _cellSize = Math.Max(0.5f, radius);
                foreach (int texel in live)
                {
                    (int, int, int) key = Key(texels[texel].Position);
                    if (!_cells.TryGetValue(key, out List<int> bucket))
                        _cells[key] = bucket = new List<int>();
                    bucket.Add(texel);
                }
            }

            public IEnumerable<int> Neighbours(Vector3 position)
            {
                (int cx, int cy, int cz) = Key(position);
                for (int z = -1; z <= 1; z++)
                    for (int y = -1; y <= 1; y++)
                        for (int x = -1; x <= 1; x++)
                        {
                            if (!_cells.TryGetValue((cx + x, cy + y, cz + z), out List<int> bucket))
                                continue;
                            foreach (int texel in bucket)
                                yield return texel;
                        }
            }

            private (int, int, int) Key(Vector3 p) => (
                (int)Math.Floor(p.X / _cellSize),
                (int)Math.Floor(p.Y / _cellSize),
                (int)Math.Floor(p.Z / _cellSize));
        }

        #endregion
    }
}
#endif
