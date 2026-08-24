#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;
using CathodeLib;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Volume probe hash re-encoding: decode a slice's tree to a dense cell grid and rebuild it
    /// at a multiple of the grid resolution over the same AABB, each fine cell inheriting its
    /// parent's probe. The engine derives cell size from the hash's own AABB and dims (proven
    /// in-game: retail hashes re-encoded at 2x render identically to retail within run-to-run
    /// noise), so this adds no information but halves the world-space span of the engine's
    /// 8-cell probe interpolation per doubling - which is what makes a dynamic prop in a dark
    /// corner stop blending in lit probes from 4 m away.
    /// </summary>
    public static class VolumeHashUtils
    {
        /// <summary>
        /// Upsample every populated hash among the first <paramref name="sliceCount"/> slices.
        /// Returns how many were re-encoded. Slices whose fine encoding would overflow the
        /// shared ushort value space (nodeCount + itemStart &gt; 65535) are left untouched -
        /// retail peaks at ~9% of that ceiling, so this is a guard, not an expectation.
        /// With <paramref name="rebind"/>, each fine cell is REBOUND to whichever probe of its
        /// 27-cell coarse neighbourhood sits nearest the fine cell's centre (positions resolved
        /// through the slice's mangle map), instead of merely inheriting its parent's - genuine
        /// densification from the existing visibility-vetted probe set. Occupancy never changes
        /// either way: a fine cell is populated exactly when its parent was.
        /// </summary>
        public static int UpsampleSlices(RadiosityRuntime runtime, int sliceCount, int factor, bool rebind, Action<string> log = null)
        {
            if (runtime == null || factor < 2)
                return 0;
            int done = 0;
            for (int s = 0; s < Math.Min(sliceCount, runtime.Slices.Count); s++)
            {
                RadiosityRuntime.RuntimeDataSlice slice = runtime.Slices[s];
                RadiosityRuntime.VolumeProbeHash hash = slice.VolumeProbeHash;
                if (hash == null || hash.Dims.X == 0)
                    continue;
                (uint ox, uint oy, uint oz) = (hash.Dims.X, hash.Dims.Y, hash.Dims.Z);
                int before = hash.Items.Count;
                if (!Upsample(hash, factor, rebind ? slice : null, out int rebound))
                {
                    log?.Invoke("    volume hash slice " + s + ": upsample x" + factor + " would overflow, left at " +
                                ox + "x" + oy + "x" + oz);
                    continue;
                }
                done++;
                log?.Invoke("    volume hash slice " + s + ": " + ox + "x" + oy + "x" + oz + " -> " +
                            hash.Dims.X + "x" + hash.Dims.Y + "x" + hash.Dims.Z +
                            "  probes " + before + " -> " + hash.Items.Count +
                            (rebind ? " (" + rebound + " rebound to a nearer probe)" : ""));
            }
            return done;
        }

        /// <summary>
        /// Re-encode one hash at <paramref name="factor"/> x its grid resolution in place.
        /// Returns false (hash untouched) when the result would overflow the format. When
        /// <paramref name="rebindSlice"/> is non-null, fine cells rebind to the nearest probe of
        /// their coarse neighbourhood by world position (see <see cref="UpsampleSlices"/>).
        /// </summary>
        public static bool Upsample(RadiosityRuntime.VolumeProbeHash hash, int factor,
            RadiosityRuntime.RuntimeDataSlice rebindSlice = null)
        {
            return Upsample(hash, factor, rebindSlice, out _);
        }

        public static bool Upsample(RadiosityRuntime.VolumeProbeHash hash, int factor,
            RadiosityRuntime.RuntimeDataSlice rebindSlice, out int rebound)
        {
            rebound = 0;
            if (hash == null || hash.Dims.X == 0 || factor < 2)
                return false;
            int gx = (int)hash.Dims.X, gy = (int)hash.Dims.Y, gz = (int)hash.Dims.Z;
            int nodeCount = hash.Nodes.Count;
            int subdiv = Math.Max(2, (int)hash.NumSubdivsPerLevel);

            // ---- decode: dense cell -> probe ----
            var cellProbe = new RadiosityRuntime.VolumeProbeHash.Probe[gx * gy * gz];
            void Walk(int node, int ox, int oy, int oz, int ex, int ey, int ez)
            {
                int nx = ex > subdiv ? subdiv : 1, ny = ey > subdiv ? subdiv : 1, nz = ez > subdiv ? subdiv : 1;
                int idx = 0;
                for (int zi = 0; zi < nz; zi++)
                    for (int yi = 0; yi < ny; yi++)
                        for (int xi = 0; xi < nx; xi++, idx++)
                        {
                            int v = hash.Offsets[hash.Nodes[node] + idx];
                            if (v == 0)
                                continue;
                            int cox = ox + SplitOrigin(ex, nx, xi), cex = SplitPart(ex, nx, xi);
                            int coy = oy + SplitOrigin(ey, ny, yi), cey = SplitPart(ey, ny, yi);
                            int coz = oz + SplitOrigin(ez, nz, zi), cez = SplitPart(ez, nz, zi);
                            if (v < nodeCount) { Walk(v, cox, coy, coz, cex, cey, cez); continue; }
                            int start = v - nodeCount;
                            int i = 0;
                            for (int z = coz; z < coz + cez; z++)
                                for (int y = coy; y < coy + cey; y++)
                                    for (int x = cox; x < cox + cex; x++, i++)
                                        cellProbe[(z * gy + y) * gx + x] = hash.Items[start + i];
                        }
            }
            Walk(0, 0, 0, 0, gx, gy, gz);

            // World position of each coarse cell's probe, resolved through the slice's mangle
            // map (atlas texel -> surface probe slot -> position). Only needed for rebinding.
            Vector3?[] probePos = null;
            if (rebindSlice?.MangleMap != null && rebindSlice.SurfaceProbePositions != null &&
                rebindSlice.MangleMap.Count > 0 && rebindSlice.SurfaceProbePositions.Count > 0)
            {
                probePos = new Vector3?[gx * gy * gz];
                for (int c = 0; c < cellProbe.Length; c++)
                {
                    RadiosityRuntime.VolumeProbeHash.Probe p = cellProbe[c];
                    if (p == null || (p.UV.X == 255 && p.UV.Y == 255))
                        continue;
                    int atlasIdx = p.UV.Y * 128 + p.UV.X;
                    if (atlasIdx >= rebindSlice.MangleMap.Count)
                        continue;
                    ColourRGBA8 m = rebindSlice.MangleMap[atlasIdx];
                    int slot = m.G * 256 + m.R;
                    if (slot >= rebindSlice.SurfaceProbePositions.Count)
                        continue;
                    Vector4 sp = rebindSlice.SurfaceProbePositions[slot];
                    if (sp.W == 0 || (sp.X == 0 && sp.Y == 0 && sp.Z == 0))
                        continue;
                    probePos[c] = new Vector3(sp.X, sp.Y, sp.Z);
                }
            }

            // ---- re-encode at the fine grid ----
            int fx = gx * factor, fy = gy * factor, fz = gz * factor;
            RadiosityRuntime.VolumeProbeHash.Probe Parent(int x, int y, int z) =>
                cellProbe[((z / factor) * gy + (y / factor)) * gx + (x / factor)];
            bool Occupied(int x, int y, int z)
            {
                RadiosityRuntime.VolumeProbeHash.Probe p = Parent(x, y, z);
                return p != null && !(p.UV.X == 255 && p.UV.Y == 255);
            }

            // occupancy summed-area table for O(1) empty-box pruning
            var sat = new int[(fx + 1) * (fy + 1) * (fz + 1)];
            int S(int x, int y, int z) => sat[(z * (fy + 1) + y) * (fx + 1) + x];
            for (int z = 1; z <= fz; z++)
                for (int y = 1; y <= fy; y++)
                    for (int x = 1; x <= fx; x++)
                        sat[(z * (fy + 1) + y) * (fx + 1) + x] =
                            (Occupied(x - 1, y - 1, z - 1) ? 1 : 0)
                            + S(x - 1, y, z) + S(x, y - 1, z) + S(x, y, z - 1)
                            - S(x - 1, y - 1, z) - S(x - 1, y, z - 1) - S(x, y - 1, z - 1)
                            + S(x - 1, y - 1, z - 1);
            int BoxCount(int ox, int oy, int oz, int ex, int ey, int ez) =>
                S(ox + ex, oy + ey, oz + ez) - S(ox, oy + ey, oz + ez) - S(ox + ex, oy, oz + ez) - S(ox + ex, oy + ey, oz)
                + S(ox, oy, oz + ez) + S(ox, oy + ey, oz) + S(ox + ex, oy, oz) - S(ox, oy, oz);

            var groups = new List<List<int>>();
            var itemCells = new List<int>();
            int Build(int ox, int oy, int oz, int ex, int ey, int ez)
            {
                int index = groups.Count;
                var group = new List<int>();
                groups.Add(group);
                int nx = ex > subdiv ? subdiv : 1, ny = ey > subdiv ? subdiv : 1, nz = ez > subdiv ? subdiv : 1;
                for (int zi = 0; zi < nz; zi++)
                    for (int yi = 0; yi < ny; yi++)
                        for (int xi = 0; xi < nx; xi++)
                        {
                            int cox = ox + SplitOrigin(ex, nx, xi), cex = SplitPart(ex, nx, xi);
                            int coy = oy + SplitOrigin(ey, ny, yi), cey = SplitPart(ey, ny, yi);
                            int coz = oz + SplitOrigin(ez, nz, zi), cez = SplitPart(ez, nz, zi);
                            if (BoxCount(cox, coy, coz, cex, cey, cez) == 0) { group.Add(0); continue; }
                            if (cex > subdiv || cey > subdiv || cez > subdiv) { group.Add(Build(cox, coy, coz, cex, cey, cez)); continue; }
                            group.Add(-(itemCells.Count + 1));
                            for (int z = coz; z < coz + cez; z++)
                                for (int y = coy; y < coy + cey; y++)
                                    for (int x = cox; x < cox + cex; x++)
                                        itemCells.Add((z * fy + y) * fx + x);
                        }
                return index;
            }
            Build(0, 0, 0, fx, fy, fz);

            int newNodeCount = groups.Count;
            if (newNodeCount + itemCells.Count > 65535)
                return false;
            var newNodes = new List<ushort>(newNodeCount);
            var newOffsets = new List<ushort>();
            foreach (List<int> group in groups)
            {
                newNodes.Add((ushort)newOffsets.Count);
                foreach (int v in group)
                    newOffsets.Add((ushort)(v < 0 ? newNodeCount + (-v - 1) : v));
            }
            Vector3 aabbExtent = new Vector3(hash.AabbMax.X - hash.AabbMin.X,
                                             hash.AabbMax.Y - hash.AabbMin.Y,
                                             hash.AabbMax.Z - hash.AabbMin.Z);
            var newItems = new List<RadiosityRuntime.VolumeProbeHash.Probe>(itemCells.Count);
            int reboundLocal = 0;
            foreach (int c in itemCells)
            {
                int cz = c / (fx * fy), cy = (c / fx) % fy, cx = c % fx;
                RadiosityRuntime.VolumeProbeHash.Probe p = Parent(cx, cy, cz);
                if (p != null && probePos != null && !(p.UV.X == 255 && p.UV.Y == 255))
                {
                    // Rebind: among the probes of the surrounding 3x3x3 coarse cells (all of
                    // them visibility-vetted picks at bake time), take whichever sits nearest
                    // this fine cell's centre. The parent stays the fallback.
                    var centre = new Vector3(hash.AabbMin.X + (cx + 0.5f) * aabbExtent.X / fx,
                                             hash.AabbMin.Y + (cy + 0.5f) * aabbExtent.Y / fy,
                                             hash.AabbMin.Z + (cz + 0.5f) * aabbExtent.Z / fz);
                    int pcx = cx / factor, pcy = cy / factor, pcz = cz / factor;
                    RadiosityRuntime.VolumeProbeHash.Probe best = p;
                    int parentIdx = (pcz * gy + pcy) * gx + pcx;
                    float bestD = probePos[parentIdx].HasValue
                        ? Vector3.DistanceSquared(probePos[parentIdx].Value, centre)
                        : float.MaxValue;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx2 = pcx + dx, ny2 = pcy + dy, nz2 = pcz + dz;
                                if (nx2 < 0 || ny2 < 0 || nz2 < 0 || nx2 >= gx || ny2 >= gy || nz2 >= gz)
                                    continue;
                                int nc = (nz2 * gy + ny2) * gx + nx2;
                                if (!probePos[nc].HasValue)
                                    continue;
                                float d = Vector3.DistanceSquared(probePos[nc].Value, centre);
                                if (d < bestD) { bestD = d; best = cellProbe[nc]; }
                            }
                    if (!ReferenceEquals(best, p))
                        reboundLocal++;
                    p = best;
                }
                newItems.Add(p == null
                    ? new RadiosityRuntime.VolumeProbeHash.Probe { UV = new Vector2u8 { X = 255, Y = 255 }, VisPaletteEntries = new byte[6] }
                    : new RadiosityRuntime.VolumeProbeHash.Probe
                    {
                        UV = new Vector2u8 { X = p.UV.X, Y = p.UV.Y },
                        VisPaletteEntries = (byte[])p.VisPaletteEntries.Clone()
                    });
            }
            rebound = reboundLocal;

            hash.Dims = new Vector3u32 { X = (uint)fx, Y = (uint)fy, Z = (uint)fz };
            hash.Nodes = newNodes;
            hash.Offsets = newOffsets;
            hash.Items = newItems;
            return true;
        }

        private static int SplitPart(int extent, int parts, int i)
        {
            if (parts <= 1) return extent;
            int size = extent / parts, remainder = extent % parts;
            return size + (i >= parts - remainder ? 1 : 0);
        }

        private static int SplitOrigin(int extent, int parts, int i)
        {
            int origin = 0;
            for (int k = 0; k < i; k++) origin += SplitPart(extent, parts, k);
            return origin;
        }
    }
}
#endif
