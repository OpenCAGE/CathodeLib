#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Builds the three AI job-position files that sit beside the navmesh in every state:
    /// SPOTTING_POSITIONS, CRAWL_SPACE_SPOTTING_POSITIONS and ASSAULT_POSITIONS.
    /// </summary>
    /// <remarks>
    /// All three are lookup grids of jobs. A job pairs a place worth checking with the place an
    /// NPC stands to check it - in retail's data the JobPosition is the hiding spot and the
    /// TaskPosition is the vantage point, which is why retail's spotting JobPositions sit just
    /// OUTSIDE the walkable surface (168 of BSP_TORRENS' 169) while the TaskPositions sit on it.
    ///
    /// What retail's files say about how they were made, measured on BSP_TORRENS, Solace, SCI_Hub
    /// and Tech_Hub (SndDump.exe jobrim / jobfit):
    ///   * Both spotting and assault positions hug the navmesh rim - the polygon edges with no
    ///     neighbour. Nothing correlates them with cover: on BSP_TORRENS the median distance from
    ///     an assault position to the nearest cover segment is 4.4 m and only 4 of 46 are within
    ///     a metre, on a level whose cover totals 34 m against 604 m of rim.
    ///   * A spotting job sits 0.2929 m outside the rim and its task 0.7071 m inside, one metre
    ///     apart along the rim's inward normal. Those are 1 - 1/sqrt(2) and 1/sqrt(2), so the pair
    ///     straddles a point 0.2071 m inside the rim at +/- 0.5 m.
    ///   * An assault position sits 0.208 m inside the rim - the same 0.2071 inset, to the
    ///     precision the file's floats carry - and its yaw is atan2(outward.X, outward.Z): the
    ///     dot of (sin yaw, 0, cos yaw) with the inward normal is -1.000 for 43 of 46.
    ///   * A crawl-space job sits on the rim of a deep-crouch region with its task just outside,
    ///     1.06 to 1.73 m away.
    ///   * The grids all use a 10 m cell. The origin is the minimum X/Z over the JobPositions and
    ///     the cell counts are floor((max - min) / 10) + 1, but a job is filed under the cell
    ///     containing its TASK position - which is why some JobPositions fall outside their own
    ///     cell (16 of BSP_TORRENS' 169).
    ///
    /// The offsets and the yaw convention are settled. The sampling density along the rim is not:
    /// retail places roughly one spotting position per 3.6 m of rim on all four levels, but not at
    /// a fixed arc-length interval, and the acceptance clearly depends on something beyond edge
    /// length that has not been identified yet. <see cref="JobPositionBakeSettings"/> exposes the
    /// spacing and minimum-length knobs used here.
    /// </remarks>
    public static class JobPositionBaker
    {
        public sealed class BakeResult
        {
            public SpottingPositions Spotting;
            public SpottingPositions CrawlSpace;
            public AssaultPositions Assault;
            public string Message;
        }

        /// <summary>
        /// Fill in the three job-position files on every state that has a navmesh. Nothing is
        /// written to disk - <see cref="Level.Save"/> persists them with the rest of the level.
        /// </summary>
        public static void BakeLevel(Level level, JobPositionBakeSettings settings = null, Action<string> log = null)
        {
            if (level == null)
                throw new ArgumentNullException(nameof(level));
            if (level.StateResources == null || level.StateResources.Count == 0)
                throw new ArgumentException("No state resources to bake.", nameof(level));

            settings ??= JobPositionBakeSettings.CreateDefault();

            for (int i = 0; i < level.StateResources.Count; i++)
            {
                Level.State state = level.StateResources[i];
                if (state?.NavMesh == null)
                {
                    log?.Invoke("JobPositions STATE_" + i + ": no navmesh, skipped");
                    continue;
                }

                BakeResult result = Bake(state.NavMesh, settings);
                state.SpottingPositions = result.Spotting;
                state.CrawlSpaceSpottingPositions = result.CrawlSpace;
                state.AssaultPositions = result.Assault;
                log?.Invoke("JobPositions STATE_" + i + ": " + result.Message);
            }
        }

        public static BakeResult Bake(NavigationMesh nav, JobPositionBakeSettings settings = null)
        {
            if (nav == null)
                throw new ArgumentNullException(nameof(nav));
            settings ??= JobPositionBakeSettings.CreateDefault();

            List<RimEdge> rim = CollectRim(nav, null);
            List<RimEdge> crawlRim = CollectRim(nav, NavigationMesh.AreaHeight.DeepCrouch);

            List<Vector3> spotSamples = SampleRim(rim, settings.SpottingSpacing, settings.SpottingMinEdgeLength,
                                                  settings.SpottingMinSeparation, settings.RimInset,
                                                  out List<Vector3> spotNormals);
            var spottingJobs = new List<SpottingPositions.JobInfo>(spotSamples.Count);
            for (int i = 0; i < spotSamples.Count; i++)
            {
                Vector3 inward = spotNormals[i];
                spottingJobs.Add(new SpottingPositions.JobInfo
                {
                    JobPosition = spotSamples[i] - inward * settings.SpottingHalfSeparation,
                    TaskPosition = spotSamples[i] + inward * settings.SpottingHalfSeparation
                });
            }

            List<Vector3> assaultSamples = SampleRim(rim, settings.AssaultSpacing, settings.AssaultMinEdgeLength,
                                                     settings.AssaultMinSeparation, settings.RimInset,
                                                     out List<Vector3> assaultNormals);
            var assaultJobs = new List<AssaultPositions.JobInfo>(assaultSamples.Count);
            for (int i = 0; i < assaultSamples.Count; i++)
            {
                // Facing out of the mesh, at whatever the rim is up against.
                Vector3 outward = -assaultNormals[i];
                assaultJobs.Add(new AssaultPositions.JobInfo
                {
                    Position = assaultSamples[i],
                    Yaw = MathF.Atan2(outward.X, outward.Z)
                });
            }

            // Crawl spaces: the job is on the deep-crouch rim, the task outside it on open floor.
            List<Vector3> crawlSamples = SampleRim(crawlRim, settings.SpottingSpacing, 0f,
                                                   settings.CrawlMinSeparation, 0f, out List<Vector3> crawlNormals);
            var crawlJobs = new List<SpottingPositions.JobInfo>(crawlSamples.Count);
            for (int i = 0; i < crawlSamples.Count; i++)
            {
                crawlJobs.Add(new SpottingPositions.JobInfo
                {
                    JobPosition = crawlSamples[i],
                    TaskPosition = crawlSamples[i] - crawlNormals[i] * settings.CrawlTaskDistance
                });
            }

            var result = new BakeResult
            {
                Spotting = BuildSpotting(spottingJobs, settings.GridUnitSize),
                CrawlSpace = BuildSpotting(crawlJobs, settings.GridUnitSize),
                Assault = BuildAssault(assaultJobs, settings.GridUnitSize)
            };
            result.Message = "rim " + rim.Count + " edges, deep-crouch rim " + crawlRim.Count +
                             "   spotting " + spottingJobs.Count +
                             "   crawl " + crawlJobs.Count +
                             "   assault " + assaultJobs.Count;
            return result;
        }

        struct RimEdge
        {
            public Vector3 A;
            public Vector3 B;
            /// <summary>Unit, flat in XZ, pointing into the polygon the edge came from.</summary>
            public Vector3 Inward;
            public float Length;
        }

        /// <summary>
        /// Polygon edges with no neighbour - where the walkable surface stops. When
        /// <paramref name="onlyHeight"/> is set, only polygons of that height class contribute and
        /// an edge counts as rim if the neighbour is missing OR of a different class, which is
        /// what bounds a crawl space inside otherwise open floor.
        /// </summary>
        static List<RimEdge> CollectRim(NavigationMesh nav, NavigationMesh.AreaHeight? onlyHeight)
        {
            var rim = new List<RimEdge>();
            if (nav?.Polygons == null || nav.Vertices == null)
                return rim;

            for (int p = 0; p < nav.Polygons.Length; p++)
            {
                NavigationMesh.dtPoly poly = nav.Polygons[p];
                if (poly.verts == null || poly.vertCount < 3)
                    continue;
                if (onlyHeight.HasValue && poly.area.GetHeightLimitedAmount() != onlyHeight.Value)
                    continue;

                Vector3 centre = Vector3.Zero;
                for (int i = 0; i < poly.vertCount; i++)
                    centre += nav.Vertices[poly.verts[i]];
                centre /= poly.vertCount;

                for (int i = 0; i < poly.vertCount; i++)
                {
                    if (!IsRim(nav, poly, i, onlyHeight))
                        continue;

                    Vector3 a = nav.Vertices[poly.verts[i]];
                    Vector3 b = nav.Vertices[poly.verts[(i + 1) % poly.vertCount]];
                    var along = new Vector3(b.X - a.X, 0f, b.Z - a.Z);
                    float length = along.Length();
                    if (length < 1e-4f)
                        continue;

                    var normal = Vector3.Normalize(new Vector3(-along.Z, 0f, along.X));
                    Vector3 mid = (a + b) * 0.5f;
                    if (Vector3.Dot(new Vector3(centre.X - mid.X, 0f, centre.Z - mid.Z), normal) < 0f)
                        normal = -normal;

                    rim.Add(new RimEdge { A = a, B = b, Inward = normal, Length = length });
                }
            }
            return rim;
        }

        static bool IsRim(NavigationMesh nav, NavigationMesh.dtPoly poly, int edge, NavigationMesh.AreaHeight? onlyHeight)
        {
            if (poly.neis == null || edge >= poly.neis.Length)
                return true;
            int nei = poly.neis[edge];
            if (nei == 0)
                return true;
            if (!onlyHeight.HasValue)
                return false;

            // neis holds a 1-based polygon index within the tile.
            int index = nei - 1;
            if (index < 0 || index >= nav.Polygons.Length)
                return true;
            return nav.Polygons[index].area.GetHeightLimitedAmount() != onlyHeight.Value;
        }

        /// <summary>
        /// Walk the rim placing samples at a fixed spacing along each long-enough edge, inset into
        /// the mesh, rejecting any that crowd one already placed.
        /// </summary>
        static List<Vector3> SampleRim(
            List<RimEdge> rim,
            float spacing,
            float minEdgeLength,
            float minSeparation,
            float inset,
            out List<Vector3> normals)
        {
            var samples = new List<Vector3>();
            normals = new List<Vector3>();
            if (rim == null || rim.Count == 0)
                return samples;

            float step = Math.Max(0.05f, spacing);
            float minSq = minSeparation * minSeparation;

            for (int e = 0; e < rim.Count; e++)
            {
                RimEdge edge = rim[e];
                if (edge.Length < minEdgeLength)
                    continue;

                // Centre the run on the edge so a single-sample edge is sampled at its midpoint.
                int count = Math.Max(1, (int)MathF.Floor(edge.Length / step));
                for (int s = 0; s < count; s++)
                {
                    float t = (s + 0.5f) / count;
                    Vector3 on = Vector3.Lerp(edge.A, edge.B, t);
                    Vector3 at = on + edge.Inward * inset;

                    bool crowded = false;
                    for (int i = 0; i < samples.Count; i++)
                    {
                        if (Vector3.DistanceSquared(samples[i], at) >= minSq)
                            continue;
                        crowded = true;
                        break;
                    }
                    if (crowded)
                        continue;

                    samples.Add(at);
                    normals.Add(edge.Inward);
                }
            }
            return samples;
        }

        static SpottingPositions BuildSpotting(List<SpottingPositions.JobInfo> jobs, float unit)
        {
            var file = new SpottingPositions("");
            Layout(jobs.ConvertAll(j => j.JobPosition), jobs.ConvertAll(j => j.TaskPosition), unit,
                   out float minX, out float minZ, out int xCells, out int zCells);
            file.MinX = minX;
            file.MinZ = minZ;
            file.UnitSize = unit;
            file.XCells = xCells;
            file.ZCells = zCells;
            file.Cells = NewCells<SpottingPositions.JobInfo>(xCells * zCells);
            foreach (SpottingPositions.JobInfo job in jobs)
                file.Cells[CellIndex(job.TaskPosition, minX, minZ, unit, xCells, zCells)].Add(job);
            return file;
        }

        static AssaultPositions BuildAssault(List<AssaultPositions.JobInfo> jobs, float unit)
        {
            var file = new AssaultPositions("");
            Layout(jobs.ConvertAll(j => j.Position), null, unit,
                   out float minX, out float minZ, out int xCells, out int zCells);
            file.MinX = minX;
            file.MinZ = minZ;
            file.UnitSize = unit;
            file.XCells = xCells;
            file.ZCells = zCells;
            file.Cells = NewCells<AssaultPositions.JobInfo>(xCells * zCells);
            foreach (AssaultPositions.JobInfo job in jobs)
                file.Cells[CellIndex(job.Position, minX, minZ, unit, xCells, zCells)].Add(job);
            return file;
        }

        /// <summary>
        /// Grid origin and extent. Retail takes the origin from the job positions alone and sizes
        /// the grid to cover them, then files each job by its task position - so the grid is grown
        /// here to hold whichever of the two lands furthest out.
        /// </summary>
        static void Layout(
            List<Vector3> jobPositions,
            List<Vector3> taskPositions,
            float unit,
            out float minX,
            out float minZ,
            out int xCells,
            out int zCells)
        {
            if (jobPositions == null || jobPositions.Count == 0)
            {
                minX = 0f; minZ = 0f; xCells = 1; zCells = 1;
                return;
            }

            float lowX = float.MaxValue, lowZ = float.MaxValue;
            float highX = float.MinValue, highZ = float.MinValue;
            for (int pass = 0; pass < 2; pass++)
            {
                List<Vector3> pts = pass == 0 ? jobPositions : taskPositions;
                if (pts == null)
                    continue;
                foreach (Vector3 p in pts)
                {
                    if (p.X < lowX) lowX = p.X;
                    if (p.Z < lowZ) lowZ = p.Z;
                    if (p.X > highX) highX = p.X;
                    if (p.Z > highZ) highZ = p.Z;
                }
            }

            // Retail never writes a positive origin: SCI_Hub's spotting grid starts at Z 0.00 when
            // its lowest job is at Z 20.83, and BSP_TORRENS' crawl grid at X 0.00 with its only
            // job at X 11.13. The grid always reaches back to the world origin.
            minX = MathF.Min(0f, lowX);
            minZ = MathF.Min(0f, lowZ);
            xCells = Math.Max(1, (int)MathF.Floor((highX - minX) / unit) + 1);
            zCells = Math.Max(1, (int)MathF.Floor((highZ - minZ) / unit) + 1);
        }

        static List<List<T>> NewCells<T>(int count)
        {
            var cells = new List<List<T>>(count);
            for (int i = 0; i < count; i++)
                cells.Add(new List<T>());
            return cells;
        }

        static int CellIndex(Vector3 p, float minX, float minZ, float unit, int xCells, int zCells)
        {
            int x = (int)MathF.Floor((p.X - minX) / unit);
            int z = (int)MathF.Floor((p.Z - minZ) / unit);
            if (x < 0) x = 0; else if (x > xCells - 1) x = xCells - 1;
            if (z < 0) z = 0; else if (z > zCells - 1) z = zCells - 1;
            return x + z * xCells;
        }
    }
}
#endif
