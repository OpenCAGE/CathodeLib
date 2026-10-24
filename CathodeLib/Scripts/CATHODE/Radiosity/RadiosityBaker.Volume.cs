#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;

namespace CathodeLib.Radiosity
{
    public static partial class RadiosityBaker
    {
        #region PROBE TREES

        /// <summary>
        /// BVH over probes, split at powers of two, with leaves sized to one 16x16 tile so the leaf count
        /// matches the tile count - which is how retail data is arranged.
        /// </summary>
        private static List<RadiosityRuntime.ProbeTreeNode> BuildProbeTree(
            int probeCount, Func<int, Vector3> positionOf, out List<uint> quads)
        {
            var nodes = new List<RadiosityRuntime.ProbeTreeNode>();
            var leafQuads = new List<uint>();
            quads = leafQuads;
            if (probeCount == 0)
                return nodes;

            const int leafSize = TileSize * TileSize;

            // Probes arrive already spatially ordered, so the tree only has to bisect the range -
            // no re-sorting, which keeps leaf n covering exactly tile n.
            int Build(int first, int count)
            {
                int index = nodes.Count;
                var node = new RadiosityRuntime.ProbeTreeNode
                {
                    MinBounds = new Vector3(float.MaxValue),
                    MaxBounds = new Vector3(float.MinValue),
                    IdxFirst = (ushort)Math.Min(ushort.MaxValue, first),
                    IdxCount = (ushort)Math.Min(ushort.MaxValue, count)
                };
                nodes.Add(node);

                for (int i = first; i < first + count; i++)
                {
                    Vector3 p = positionOf(i);
                    node.MinBounds = Vector3.Min(node.MinBounds, p);
                    node.MaxBounds = Vector3.Max(node.MaxBounds, p);
                }

                if (count <= leafSize)
                {
                    node.ChildA = 0;
                    node.ChildB = 0;

                    // A leaf addresses the QUAD list, not the probe list: IdxFirst is the index of its first of four
                    // corner vertices and IdxCount is one, counting tiles (all 9208 leaves of 19 retail levels: IdxFirst 0,
                    // 4, 8...). Internal nodes keep their probe range. A probe range here makes the engine draw 256 quads
                    // out of a buffer of ~120, an out-of-bounds read that faults inside d3d11.
                    node.IdxFirst = (ushort)leafQuads.Count;
                    node.IdxCount = 1;

                    EmitLeafQuad(leafQuads, first / leafSize, count);
                    return index;
                }

                // Split on a leaf-size boundary so leaves stay tile-aligned. Retail splits at the
                // highest power of two below the count (9553 -> 8192 | 1361 -> 1024 | 337 -> 256 | 81;
                // decoded 18 Sep 2026 from Solace/CM4 node dumps), not at the median.
                int half = Math.Max(leafSize, HighestPowerOfTwoBelow(count));
                if (half >= count) half = count - leafSize;

                node.ChildA = (ushort)Build(first, half);
                node.ChildB = (ushort)Build(first + half, count - half);
                return index;
            }

            Build(0, probeCount);
            return nodes;
        }

        /// <summary>
        /// Reorder in place by recursive median split on the longest axis, giving a spatially
        /// coherent sequence that tiles and tree leaves can both be cut from.
        /// </summary>
        private static void SpatialSort(List<int> items, Func<int, Vector3> positionOf, bool pow2Cuts = false)
        {
            if (items.Count <= 1)
                return;

            var scratch = items.ToArray();
            Sort(0, scratch.Length);
            items.Clear();
            items.AddRange(scratch);

            void Sort(int first, int count)
            {
                if (count <= TileSize)
                    return;

                Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
                for (int i = first; i < first + count; i++)
                {
                    Vector3 p = positionOf(scratch[i]);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }

                Vector3 extent = max - min;
                int axis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 : extent.Y >= extent.Z ? 1 : 2;
                Array.Sort(scratch, first, count, Comparer<int>.Create((a, b) =>
                    Axis(positionOf(a), axis).CompareTo(Axis(positionOf(b), axis))));

                // Retail's probe order is cut at the highest power of two below the count (its
                // trees split 9553 -> 8192 | 1361), so every 256-probe tile is one contiguous k-d
                // cell; a median split leaves tiles straddling cells (leaf volume 2x retail's).
                int half = pow2Cuts && count > TileSize * TileSize ? HighestPowerOfTwoBelow(count) : count / 2;
                Sort(first, half);
                Sort(first + half, count - half);
            }
        }

        /// <summary>
        /// The four packed corners of a leaf's tile rect, as bytes <c>(x, y, cornerU, cornerV)</c>
        /// covering <c>(x, y)</c> to <c>(x + width - 1, y + height - 1)</c> inclusive.
        /// </summary>
        private static void EmitLeafQuad(List<uint> quads, int tileIndex, int probeCount)
        {
            TileRect(tileIndex, probeCount, out int x, out int y, out int width, out int height);
            int right = x + Math.Max(0, width - 1);
            int bottom = y + Math.Max(0, height - 1);

            quads.Add(Pack(x, y, 0, 0));
            quads.Add(Pack(right, y, 1, 0));
            quads.Add(Pack(x, bottom, 0, 1));
            quads.Add(Pack(right, bottom, 1, 1));
        }

        private static uint Pack(int b0, int b1, int b2, int b3) =>
            (uint)((b0 & 0xFF) | ((b1 & 0xFF) << 8) | ((b2 & 0xFF) << 16) | ((b3 & 0xFF) << 24));

        #endregion

        #region INPUT PROBE TILING

        /// <summary>
        /// Split an atlas texel index into the byte pair the engine uses to reference a cluster.
        /// </summary>
        private static void ClusterRef(int atlasTexel, out byte x, out byte y)
        {
            x = (byte)(atlasTexel % ProbeTexWidth);
            y = (byte)(atlasTexel / ProbeTexWidth);
        }

        /// <summary>
        /// Input probes fill 16x16 tiles in column-major order: tiles 0-3 are the four rows of the
        /// leftmost column, then the next column, matching the retail tile list.
        /// </summary>
        private static void InputProbeTexel(int probeIndex, out int x, out int y)
        {
            int tile = probeIndex / (TileSize * TileSize);
            int within = probeIndex % (TileSize * TileSize);
            int tileX = (tile / TileRows) * TileSize;
            int tileY = (tile % TileRows) * TileSize;
            x = tileX + within % TileSize;
            y = tileY + within / TileSize;
        }

        /// <summary>
        /// Slot in the 256x64 probe texture for the nth probe of a compacted list. Surface probes
        /// and input probes use identical tiling, so this serves both.
        /// </summary>
        private static int ProbeSlot(int probeIndex)
        {
            InputProbeTexel(probeIndex, out int x, out int y);
            return y * ProbeTexWidth + x;
        }

        private static void BuildInputProbeTiles(int probeCount, List<RadiosityRuntime.ProbeTileDims> tiles)
        {
            tiles.Clear();
            if (probeCount == 0)
                return;

            int tileCount = (probeCount + TileSize * TileSize - 1) / (TileSize * TileSize);
            for (int t = 0; t < tileCount; t++)
            {
                int remaining = probeCount - t * TileSize * TileSize;
                TileRect(t, remaining, out int x, out int y, out int width, out int height);
                tiles.Add(new RadiosityRuntime.ProbeTileDims
                {
                    X = (byte)x,
                    Y = (byte)y,
                    Width = (byte)width,
                    Height = (byte)height
                });
            }
        }

        /// <summary>
        /// Rect of a tile. The final tile is clipped to the rows actually used, which is what
        /// retail does - BSP_TORRENS slice 0 ends with a 16x1 tile for its last 5 probes.
        /// </summary>
        private static void TileRect(int tileIndex, int probesInTile, out int x, out int y, out int width, out int height)
        {
            x = (tileIndex / TileRows) * TileSize;
            y = (tileIndex % TileRows) * TileSize;
            width = TileSize;
            int rows = (Math.Max(0, Math.Min(probesInTile, TileSize * TileSize)) + TileSize - 1) / TileSize;
            height = Math.Max(1, Math.Min(TileSize, rows));
        }

        #endregion

        #region VOLUME PROBES

        /// <summary>
        /// Uniform grid over the slice's bounds mapping a world position to the nearest input
        /// probe, used by CA_RADIOSITY_OBJECT_PROBE_INTERP to light dynamic objects: without it,
        /// characters and movable props receive no bounced light. The tree over the cell grid
        /// reproduces the layout of all 126 populated retail hashes exactly.
        /// </summary>
        private static RadiosityRuntime.VolumeProbeHash BuildVolumeProbeHash(
            RadiosityGeometry geometry, SurfaceTexel[] texels, int[] inputProbeForTexel, int[] surfaceSlotForTexel,
            float cellSize, float consumerMinY, float consumerMaxY, out List<byte[]> visFaceGrids)
        {
            visFaceGrids = new List<byte[]>();
            var hash = new RadiosityRuntime.VolumeProbeHash { NumSubdivsPerLevel = RadiosityTuning.VolumeProbeSubdivsPerLevel };

            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            var sources = new List<int>();
            for (int i = 0; i < AtlasTexels; i++)
            {
                if (!texels[i].Live || inputProbeForTexel[i] < 0)
                    continue;
                // The ENGINE resolves a volume item as atlas texel -> mangle map -> surface probe slot, so a cluster-only
                // texel (donor shell, corner fill: no surface probe) resolves to a DEAD slot and any prop blending it reads
                // black (27.8% of our items against retail's 1.9% before this).
                if (surfaceSlotForTexel[i] < 0)
                    continue;
                min = Vector3.Min(min, texels[i].Position);
                max = Vector3.Max(max, texels[i].Position);
                sources.Add(i);
            }

            if (sources.Count == 0)
            {
                hash.AabbMin = Vector3.Zero;
                hash.AabbMax = Vector3.Zero;
                hash.Dims = new Vector3u32 { X = 0, Y = 0, Z = 0 };
                return hash;
            }

            // Vertical consumer-space extension (see the call site): the box grows to the slice's
            // instance geometry range so elevated dynamic movers stay in bounds. X/Z stay
            // probe-driven - the measured retail boxes match our probe footprints there.
            if (consumerMinY < float.MaxValue && consumerMinY < min.Y) min.Y = consumerMinY;
            if (consumerMaxY > float.MinValue && consumerMaxY > max.Y) max.Y = consumerMaxY;

            float cell = cellSize;

            // Retail's slice lattices coincide (their origins sit a whole number of cells apart);
            // ours are phased off each slice's own box, so a dynamic object crossing a slice
            // boundary meets two differently-phased fields. Snapping every box out to a
            // world-anchored grid (at most one extra cell per axis) was built and left off.
            hash.AabbMin = min;
            hash.AabbMax = max;

            Vector3 extent = max - min;
            uint dx = (uint)Math.Max(1, Math.Ceiling(extent.X / cell));
            uint dy = (uint)Math.Max(1, Math.Ceiling(extent.Y / cell));
            uint dz = (uint)Math.Max(1, Math.Ceiling(extent.Z / cell));
            hash.Dims = new Vector3u32 { X = dx, Y = dy, Z = dz };

            int gx = (int)dx, gy = (int)dy, gz = (int)dz;

            // Representative texel per cell, or -1: the nearest source VISIBLE from the cell centre. A prop's nearest texel
            // is often the floor in its own baked shadow, which renders the prop black in a lit room (cf. retail's
            // least-backface candidate fixup).
            var cellCandidates = new Dictionary<int, List<int>>();
            foreach (int texel in sources)
            {
                Vector3 local = texels[texel].Position - min;
                int cx = Math.Min(gx - 1, Math.Max(0, (int)(local.X / cell)));
                int cy = Math.Min(gy - 1, Math.Max(0, (int)(local.Y / cell)));
                int cz = Math.Min(gz - 1, Math.Max(0, (int)(local.Z / cell)));
                int key = (cz * gy + cy) * gx + cx;
                if (!cellCandidates.TryGetValue(key, out List<int> list))
                    cellCandidates[key] = list = new List<int>();
                list.Add(texel);
            }

            var probeForCell = new int[gx * gy * gz];
            for (int i = 0; i < probeForCell.Length; i++) probeForCell[i] = -1;

            foreach (KeyValuePair<int, List<int>> pair in cellCandidates)
            {
                int key = pair.Key;
                int cz2 = key / (gx * gy), cy2 = (key / gx) % gy, cx2 = key % gx;
                Vector3 centre = min + new Vector3((cx2 + 0.5f) * cell, (cy2 + 0.5f) * cell, (cz2 + 0.5f) * cell);

                pair.Value.Sort((a, b) =>
                    Vector3.DistanceSquared(centre, texels[a].Position)
                        .CompareTo(Vector3.DistanceSquared(centre, texels[b].Position)));

                int chosen = -1;
                int tried = 0;
                foreach (int texel in pair.Value)
                {
                    if (tried++ >= 12) break;   // visibility rays are not free; nearest few suffice
                    if (geometry.Visible(centre,
                                         texels[texel].Position + texels[texel].Normal * RadiosityTuning.ProbeSurfaceOffset,
                                         RadiosityTuning.RayEpsilon))
                    {
                        chosen = texel;
                        break;
                    }
                }
                probeForCell[key] = chosen >= 0 ? chosen : pair.Value[0];
            }

            // Mid-air fill. A cell no probe falls inside would stay a (255,255) no-probe item, but the engine's 8-cell blend
            // does NOT renormalise around missing items, so a mover centred between floor and ceiling probe layers reads a
            // mostly-black blend (F6's black ceilings: 73% of one blend on no-probe cells). Retail's items resolve probes
            // up to ~2.6 m from their cell, so empty cells borrow the nearest probe within reach, preferring a visible one.
            float reach = RadiosityTuning.VolumeProbeFillReach;
            int ring = Math.Max(1, (int)Math.Ceiling(reach / cell));
            float reachSq = reach * reach;

            // Dilate outward from occupied cells so far-out empties (a stretched hash can
            // cover most of the level) are never even visited.
            var fillTargets = new HashSet<int>();
            foreach (int key in cellCandidates.Keys)
            {
                int cz2 = key / (gx * gy), cy2 = (key / gx) % gy, cx2 = key % gx;
                for (int dz2 = -ring; dz2 <= ring; dz2++)
                    for (int dy2 = -ring; dy2 <= ring; dy2++)
                        for (int dx2 = -ring; dx2 <= ring; dx2++)
                        {
                            int nx = cx2 + dx2, ny = cy2 + dy2, nz = cz2 + dz2;
                            if (nx < 0 || ny < 0 || nz < 0 || nx >= gx || ny >= gy || nz >= gz)
                                continue;
                            int nk = (nz * gy + ny) * gx + nx;
                            if (probeForCell[nk] < 0)
                                fillTargets.Add(nk);
                        }
            }

            bool OccupiedAt(int x, int y, int z) =>
                x >= 0 && y >= 0 && z >= 0 && x < gx && y < gy && z < gz &&
                cellCandidates.ContainsKey((z * gy + y) * gx + x);

            var scratch = new List<int>();
            foreach (int key in fillTargets)
            {
                int cz2 = key / (gx * gy), cy2 = (key / gx) % gy, cx2 = key % gx;

                // Interior air only: surfaces on OPPOSITE sides along some axis within the
                // ring (the floor-below-ceiling-above layer where mover bounds centres sit).
                // Filling the outward shell too - outside walls, above ceilings - was the F7
                // item explosion, and nothing ever samples out there.
                bool interior = false;
                for (int axis = 0; axis < 3 && !interior; axis++)
                {
                    bool neg = false, pos = false;
                    for (int step = 1; step <= ring && !(neg && pos); step++)
                    {
                        int sx = axis == 0 ? step : 0, sy = axis == 1 ? step : 0, sz = axis == 2 ? step : 0;
                        neg = neg || OccupiedAt(cx2 - sx, cy2 - sy, cz2 - sz);
                        pos = pos || OccupiedAt(cx2 + sx, cy2 + sy, cz2 + sz);
                    }
                    interior = neg && pos;
                }
                if (!interior)
                    continue;

                Vector3 centre = min + new Vector3((cx2 + 0.5f) * cell, (cy2 + 0.5f) * cell, (cz2 + 0.5f) * cell);

                scratch.Clear();
                for (int dz2 = -ring; dz2 <= ring; dz2++)
                    for (int dy2 = -ring; dy2 <= ring; dy2++)
                        for (int dx2 = -ring; dx2 <= ring; dx2++)
                        {
                            int nx = cx2 + dx2, ny = cy2 + dy2, nz = cz2 + dz2;
                            if (nx < 0 || ny < 0 || nz < 0 || nx >= gx || ny >= gy || nz >= gz)
                                continue;
                            if (cellCandidates.TryGetValue((nz * gy + ny) * gx + nx, out List<int> near))
                                foreach (int texel in near)
                                    if (Vector3.DistanceSquared(centre, texels[texel].Position) <= reachSq)
                                        scratch.Add(texel);
                        }
                if (scratch.Count == 0)
                    continue;

                scratch.Sort((a, b) =>
                    Vector3.DistanceSquared(centre, texels[a].Position)
                        .CompareTo(Vector3.DistanceSquared(centre, texels[b].Position)));

                int chosen = -1;
                int tried = 0;
                foreach (int texel in scratch)
                {
                    if (tried++ >= 12) break;
                    if (geometry.Visible(centre,
                                         texels[texel].Position + texels[texel].Normal * RadiosityTuning.ProbeSurfaceOffset,
                                         RadiosityTuning.RayEpsilon))
                    {
                        chosen = texel;
                        break;
                    }
                }
                probeForCell[key] = chosen >= 0 ? chosen : scratch[0];
            }

            // Summed-area table over occupancy so "does this box hold a probe" is O(1) and empty
            // subtrees can be pruned, which is what keeps the item count near retail's.
            var occupancy = BuildOccupancySat(probeForCell, gx, gy, gz);

            var groups = new List<List<int>>();          // per node, in node index order
            var itemCells = new List<int>();             // linear cell index per item, in item order
            int subdiv = (int)RadiosityTuning.VolumeProbeSubdivsPerLevel;

            // Leaves are recorded as -(itemStart + 1) because their encoded value depends on the
            // final node count, which is not known until the whole tree exists.
            int Build(int ox, int oy, int oz, int ex, int ey, int ez)
            {
                int index = groups.Count;
                var group = new List<int>();
                groups.Add(group);

                int nx = ex > subdiv ? subdiv : 1;
                int ny = ey > subdiv ? subdiv : 1;
                int nz = ez > subdiv ? subdiv : 1;

                for (int zi = 0; zi < nz; zi++)
                    for (int yi = 0; yi < ny; yi++)
                        for (int xi = 0; xi < nx; xi++)
                        {
                            int cox = ox + SplitOrigin(ex, nx, xi), cex = SplitPart(ex, nx, xi);
                            int coy = oy + SplitOrigin(ey, ny, yi), cey = SplitPart(ey, ny, yi);
                            int coz = oz + SplitOrigin(ez, nz, zi), cez = SplitPart(ez, nz, zi);

                            if (SatCount(occupancy, gx, gy, gz, cox, coy, coz, cex, cey, cez) == 0)
                            {
                                group.Add(0);
                                continue;
                            }

                            if (cex > subdiv || cey > subdiv || cez > subdiv)
                            {
                                group.Add(Build(cox, coy, coz, cex, cey, cez));
                                continue;
                            }

                            // Leaf: it owns one item per cell, laid out x fastest.
                            group.Add(-(itemCells.Count + 1));
                            for (int z = coz; z < coz + cez; z++)
                                for (int y = coy; y < coy + cey; y++)
                                    for (int x = cox; x < cox + cex; x++)
                                        itemCells.Add((z * gy + y) * gx + x);
                        }
                return index;
            }

            Build(0, 0, 0, gx, gy, gz);

            // Flatten. A value is 0 for empty, the child's node index while below Nodes.Count, and
            // Nodes.Count + itemStart for a leaf - one value space, which is how the reader tells
            // the two apart without a flag.
            int nodeCount = groups.Count;
            hash.Nodes = new List<ushort>(nodeCount);
            hash.Offsets = new List<ushort>();
            foreach (List<int> group in groups)
            {
                hash.Nodes.Add((ushort)hash.Offsets.Count);
                foreach (int v in group)
                    hash.Offsets.Add((ushort)(v < 0 ? nodeCount + (-v - 1) : v));
            }

            hash.Items = new List<RadiosityRuntime.VolumeProbeHash.Probe>(itemCells.Count);
            var probeOrigins = new Vector3[itemCells.Count];
            var hasProbe = new bool[itemCells.Count];

            for (int i = 0; i < itemCells.Count; i++)
            {
                int texel = probeForCell[itemCells[i]];
                if (texel < 0)
                {
                    // No geometry near this cell. (255, 255) is retail's "no probe here" UV, and
                    // just over half of all retail items carry it.
                    hash.Items.Add(new RadiosityRuntime.VolumeProbeHash.Probe
                    {
                        UV = new Vector2u8 { X = 255, Y = 255 },
                        VisPaletteEntries = new byte[6]
                    });
                    continue;
                }

                // The UV is a 128-wide LIGHTMAP ATLAS texel, not an input probe reference: the engine samples the
                // reconstructed lightmap there (through the mangle map to a surface probe slot). Input-probe coords here
                // made every RADIOSITY_DYNAMIC prop render black.
                hash.Items.Add(new RadiosityRuntime.VolumeProbeHash.Probe
                {
                    UV = new Vector2u8 { X = (byte)(texel % AtlasSize), Y = (byte)(texel / AtlasSize) },
                    // Filled in once every slice's grids have been folded into the shared palette.
                    VisPaletteEntries = new byte[6]
                });

                // Trace from the cell centre rather than the surface probe, so the sample sits in
                // open air instead of buried in the geometry the probe is attached to.
                int c = itemCells[i];
                int cz2 = c / (gx * gy), cy2 = (c / gx) % gy, cx2 = c % gx;
                probeOrigins[i] = min + new Vector3((cx2 + 0.5f) * cell, (cy2 + 0.5f) * cell, (cz2 + 0.5f) * cell);
                hasProbe[i] = true;
            }

            // 27 samples x 64 cells x 6 faces is a lot of rays per probe, so fan them out. Cells
            // with no probe never get sampled and keep an all-zero grid.
            var grids = new byte[itemCells.Count * 6][];
            void TraceOne(int flat)
            {
                grids[flat] = hasProbe[flat / 6]
                    ? TraceVisFace(geometry, probeOrigins[flat / 6], flat % 6)
                    : new byte[VisFaceCells];
            }
            System.Threading.Tasks.Parallel.For(0, grids.Length, TraceOne);

            // Retail's invariant on every level and slice: a REFERENCED vis grid is never fully occluded (openness p10
            // 15-22, never 0), and the engine's response to an all-black grid is catastrophic (a full-frame WHITEOUT on
            // SCI_Hub). A fully embedded item (all six faces zero) re-traces from its elected surface texel nudged along
            // the normal; any face still all-zero is floored to one visible sub-sample, the smallest value retail emits.
            byte floorVis = EncodeVisibility(1, VisSubSamples);
            for (int i = 0; i < itemCells.Count; i++)
            {
                if (!hasProbe[i]) continue;
                bool allZero = true;
                for (int f = 0; f < 6 && allZero; f++)
                    foreach (byte b in grids[i * 6 + f]) if (b != 0) { allZero = false; break; }
                if (allZero)
                {
                    int texel = probeForCell[itemCells[i]];
                    Vector3 fallback = texels[texel].Position + texels[texel].Normal * 0.05f;
                    for (int f = 0; f < 6; f++)
                        grids[i * 6 + f] = TraceVisFace(geometry, fallback, f);
                }
                for (int f = 0; f < 6; f++)
                {
                    byte[] g = grids[i * 6 + f];
                    bool zero = true;
                    foreach (byte b in g) if (b != 0) { zero = false; break; }
                    if (zero)
                        for (int c2 = 0; c2 < g.Length; c2++) g[c2] = floorVis;
                }
            }

            visFaceGrids.AddRange(grids);

            return hash;
        }

        /// <summary>Size of part <paramref name="i"/> when <paramref name="extent"/> splits into
        /// <paramref name="parts"/>. Retail hands the remainder to the trailing parts.</summary>
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

        /// <summary>Inclusive summed-area table of "cell holds a probe", padded by one on each axis.</summary>
        private static int[] BuildOccupancySat(int[] probeForCell, int gx, int gy, int gz)
        {
            int sx = gx + 1, sy = gy + 1;
            var sat = new int[sx * sy * (gz + 1)];
            for (int z = 1; z <= gz; z++)
                for (int y = 1; y <= gy; y++)
                    for (int x = 1; x <= gx; x++)
                    {
                        int here = probeForCell[((z - 1) * gy + (y - 1)) * gx + (x - 1)] >= 0 ? 1 : 0;
                        sat[(z * sy + y) * sx + x] = here
                            + sat[(z * sy + y) * sx + (x - 1)]
                            + sat[(z * sy + (y - 1)) * sx + x]
                            + sat[((z - 1) * sy + y) * sx + x]
                            - sat[(z * sy + (y - 1)) * sx + (x - 1)]
                            - sat[((z - 1) * sy + y) * sx + (x - 1)]
                            - sat[((z - 1) * sy + (y - 1)) * sx + x]
                            + sat[((z - 1) * sy + (y - 1)) * sx + (x - 1)];
                    }
            return sat;
        }

        private static int SatCount(int[] sat, int gx, int gy, int gz, int ox, int oy, int oz, int ex, int ey, int ez)
        {
            int sx = gx + 1, sy = gy + 1;
            int x0 = ox, x1 = ox + ex, y0 = oy, y1 = oy + ey, z0 = oz, z1 = oz + ez;
            int At(int x, int y, int z) => sat[(z * sy + y) * sx + x];
            return At(x1, y1, z1) - At(x0, y1, z1) - At(x1, y0, z1) - At(x1, y1, z0)
                 + At(x0, y0, z1) + At(x0, y1, z0) + At(x1, y0, z0) - At(x0, y0, z0);
        }

        /// <summary>Number of visibility sub-samples a palette cell aggregates.</summary>
        private const int VisSubSamples = 27;

        /// <summary>Palette grids are 8x8 per cube face, six faces per volume probe.</summary>
        private const int VisFaceSize = 8;
        private const int VisFaceCells = VisFaceSize * VisFaceSize;

        private static byte EncodeVisibility(int visibleSamples, int totalSamples)
        {
            if (totalSamples <= 0) return 0;
            int n = (int)Math.Round((double)visibleSamples * VisSubSamples / totalSamples);
            if (n < 0) n = 0;
            if (n > VisSubSamples) n = VisSubSamples;
            return (byte)(n * 255 / VisSubSamples);
        }

        /// <summary>
        /// Trace an 8x8 visibility grid for one cube face of a volume probe.
        /// </summary>
        private static byte[] TraceVisFace(
            RadiosityGeometry geometry, Vector3 origin, int face)
        {
            var grid = new byte[VisFaceCells];
            int samples = RadiosityTuning.VolumeProbeVisSamplesPerCell;
            float range = RadiosityTuning.VolumeProbeVisRange;

            Vector3 forward = FaceDirection(face, out Vector3 right, out Vector3 up);

            for (int y = 0; y < VisFaceSize; y++)
            {
                for (int x = 0; x < VisFaceSize; x++)
                {
                    int visible = 0;
                    for (int s = 0; s < samples; s++)
                    {
                        // Stratify inside the cell so a cell straddling an edge reads partial.
                        float jx = Fract(0.5f + s * 0.7548776662f);
                        float jy = Fract(0.5f + s * 0.5698402910f);
                        float u = ((x + jx) / VisFaceSize) * 2f - 1f;
                        float v = ((y + jy) / VisFaceSize) * 2f - 1f;

                        Vector3 dir = Vector3.Normalize(forward + right * u + up * v);
                        var ray = new NanoRT.Ray(origin, dir, RadiosityTuning.RayEpsilon, range);
                        if (!geometry.Bvh.Occluded(ref ray))
                            visible++;
                    }
                    grid[y * VisFaceSize + x] = EncodeVisibility(visible, samples);
                }
            }
            return grid;
        }

        /// <summary>Cube face basis: +X, -X, +Y, -Y, +Z, -Z.</summary>
        private static Vector3 FaceDirection(int face, out Vector3 right, out Vector3 up)
        {
            switch (face)
            {
                case 0: right = -Vector3.UnitZ; up = Vector3.UnitY; return Vector3.UnitX;
                case 1: right = Vector3.UnitZ; up = Vector3.UnitY; return -Vector3.UnitX;
                case 2: right = Vector3.UnitX; up = Vector3.UnitZ; return Vector3.UnitY;
                case 3: right = Vector3.UnitX; up = -Vector3.UnitZ; return -Vector3.UnitY;
                case 4: right = Vector3.UnitX; up = Vector3.UnitY; return Vector3.UnitZ;
                default: right = -Vector3.UnitX; up = Vector3.UnitY; return -Vector3.UnitZ;
            }
        }

        /// <summary>
        /// Collapse every traced face grid into the shared 256-entry palette, assigning each probe
        /// face its index. Exact duplicates share an entry first; once the palette is full the
        /// remaining grids snap to their closest existing entry.
        /// </summary>
        private static List<RadiosityRuntime.VolumeProbeVisSlice> BuildVisPalette(IEnumerable<SliceBake> slices)
        {
            var palette = new List<byte[]>(256);
            var index = new Dictionary<string, int>(256);

            // Entry 0 is fully visible, as in retail - it is also the safe fallback.
            var open = new byte[VisFaceCells];
            for (int i = 0; i < VisFaceCells; i++) open[i] = 255;
            palette.Add(open);
            index[Key(open)] = 0;

            foreach (SliceBake slice in slices)
            {
                if (slice?.VisFaceGrids == null)
                    continue;

                for (int g = 0; g < slice.VisFaceGrids.Count; g++)
                {
                    byte[] grid = slice.VisFaceGrids[g];
                    string key = Key(grid);
                    if (index.TryGetValue(key, out int existing))
                    {
                        slice.VisFaceIndices[g] = (byte)existing;
                        continue;
                    }

                    if (palette.Count < 256)
                    {
                        index[key] = palette.Count;
                        slice.VisFaceIndices[g] = (byte)palette.Count;
                        palette.Add(grid);
                        continue;
                    }

                    slice.VisFaceIndices[g] = (byte)ClosestPaletteEntry(palette, grid);
                }
            }

            while (palette.Count < 256)
                palette.Add(new byte[VisFaceCells]);

            var result = new List<RadiosityRuntime.VolumeProbeVisSlice>(256);
            foreach (byte[] grid in palette)
                result.Add(new RadiosityRuntime.VolumeProbeVisSlice { Grid = grid });
            return result;

            string Key(byte[] g) => Convert.ToBase64String(g);
        }

        /// <summary>Copy the resolved palette indices back onto each slice's volume probes.</summary>
        private static void ApplyVisPaletteIndices(IEnumerable<SliceBake> slices)
        {
            foreach (SliceBake slice in slices)
            {
                if (slice?.VisFaceIndices == null)
                    continue;

                List<RadiosityRuntime.VolumeProbeHash.Probe> probes = slice.Slice.VolumeProbeHash.Items;
                for (int p = 0; p < probes.Count; p++)
                {
                    for (int face = 0; face < 6; face++)
                    {
                        int flat = p * 6 + face;
                        if (flat < slice.VisFaceIndices.Length)
                            probes[p].VisPaletteEntries[face] = slice.VisFaceIndices[flat];
                    }
                }
            }
        }

        private static int ClosestPaletteEntry(List<byte[]> palette, byte[] grid)
        {
            int best = 0;
            long bestCost = long.MaxValue;
            for (int i = 0; i < palette.Count; i++)
            {
                byte[] candidate = palette[i];
                long cost = 0;
                for (int c = 0; c < VisFaceCells; c++)
                {
                    int d = candidate[c] - grid[c];
                    cost += d * d;
                    if (cost >= bestCost) break;
                }
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }
            return best;
        }

        #endregion
    }
}
#endif
