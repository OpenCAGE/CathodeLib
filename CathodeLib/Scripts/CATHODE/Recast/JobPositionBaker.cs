#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;
using CathodeLib.Radiosity;
using NanoRT;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Builds the three AI job-position files that sit beside the navmesh in every state:
    /// SPOTTING_POSITIONS, CRAWL_SPACE_SPOTTING_POSITIONS and ASSAULT_POSITIONS.
    /// </summary>
    /// <remarks>
    /// All three are lookup grids of jobs, and a job pairs a place worth checking with the place an
    /// NPC stands to check it: the JobPosition is the hiding spot, the TaskPosition the vantage
    /// point. That is why retail's spotting JobPositions sit just OUTSIDE the walkable surface -
    /// 168 of BSP_TORRENS' 169 - while the TaskPositions sit on it.
    ///
    /// Spotting and assault both lay positions along a run of cover, with the same shape of rule
    /// and their own constants (see <see cref="JobPositionBakeSettings"/>). Every distance is
    /// measured from the WALL GEOMETRY, and the navmesh is already eroded inward by the walkable
    /// radius, so the offsets have to be converted before they can be applied to the rim. That
    /// conversion is what reconciles the two: an assault position 0.5 m off the geometry lands
    /// 0.1875 m inside the rim, against the 0.208 median retail ships, and a spotting job 0.03 m
    /// off the geometry lands 0.2825 m outside it, against 0.2929 measured.
    ///
    /// The engine runs those rules over cover volumes. We rebuild the runs from the navmesh rim,
    /// because the COVER file that ships is only the tactically usable subset and nowhere near
    /// enough on its own - BSP_TORRENS has 17 segments totalling 34 m, three of them long enough
    /// to qualify, against 46 assault positions in the shipped file. Applying the rules to that
    /// cover alone reproduces only 3 of the 46, but with exact yaw on all three, so the rules are
    /// right and the input was incomplete.
    ///
    /// Crawl spaces work off the mouths of a deep-crouch region - edges whose neighbour is walkable
    /// floor of another class. An edge with no neighbour is the crawl space's own outer wall and is
    /// not a way in.
    ///
    /// Grids use a 10 m cell, an origin of min(0, lowest job X/Z) - retail never writes a positive
    /// origin - and cells of floor((max - origin) / 10) + 1. A job is filed under the cell holding
    /// its TASK position, which is why some JobPositions fall outside their own cell (16 of
    /// BSP_TORRENS' 169).
    ///
    /// Measured against retail on BSP_TORRENS, Solace, SCI_Hub and Tech_Hub with
    /// <c>SndDump.exe jobbake</c>.
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
        /// The level's glass, ready to be swept for. Glass is collision typed TRANSPARENT: solid
        /// enough to stop a character, so the navmesh ends at it and a rim run forms along it, but
        /// no use as cover.
        /// </summary>
        public sealed class GlassProbe
        {
            BVHAccel _glass;
            BVHAccel _solid;
            public int TriangleCount { get; private set; }

            public static GlassProbe FromLevel(Level level, Action<string> log = null)
            {
                var flags = new List<CollisionMaps.CollisionFlags>();
                if (!RadiosityOccluders.TryCollect(level, null, out float[] verts, out int[] tris, null, true, flags))
                {
                    log?.Invoke("  glass test: no collision packfile, skipped");
                    return null;
                }

                var glass = new List<int>();
                var solid = new List<int>();
                int triangles = tris.Length / 3;
                for (int t = 0; t < triangles && t < flags.Count; t++)
                {
                    CollisionMaps.CollisionType type = (CollisionMaps.CollisionType)
                        ((uint)flags[t] & (uint)CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK);
                    bool transparent = type == CollisionMaps.CollisionType.TRANSPARENT
                                       || type == CollisionMaps.CollisionType.DYNAMIC_TRANSPARENT;
                    List<int> into = transparent ? glass : solid;
                    into.Add(tris[t * 3 + 0]);
                    into.Add(tris[t * 3 + 1]);
                    into.Add(tris[t * 3 + 2]);
                }
                if (glass.Count == 0)
                {
                    log?.Invoke("  glass test: level has no transparent collision");
                    return null;
                }

                var probe = new GlassProbe { TriangleCount = glass.Count / 3, _glass = new BVHAccel() };
                probe._glass.Build(verts, glass.ToArray());
                if (solid.Count > 0)
                {
                    probe._solid = new BVHAccel();
                    probe._solid.Build(verts, solid.ToArray());
                }
                return probe;
            }

            /// <summary>
            /// True when the first thing the swept ray meets is glass. Anything opaque in front of
            /// it means the cover is real and the pane behind is somebody else's problem - testing
            /// for glass anywhere along the ray instead threw away positions retail keeps.
            /// </summary>
            public bool Hits(Vector3 from, Vector3 to, float radius)
            {
                Vector3 along = to - from;
                float length = along.Length();
                if (length <= 1e-4f)
                    return false;
                along /= length;

                Vector3 side = Vector3.Cross(along, Vector3.UnitY);
                side = side.LengthSquared() < 1e-6f ? Vector3.UnitX : Vector3.Normalize(side);
                Vector3 up = Vector3.Normalize(Vector3.Cross(side, along));

                if (GlassFirst(from, along, length)) return true;
                if (radius <= 1e-4f) return false;
                return GlassFirst(from + side * radius, along, length)
                    || GlassFirst(from - side * radius, along, length)
                    || GlassFirst(from + up * radius, along, length)
                    || GlassFirst(from - up * radius, along, length);
            }

            bool GlassFirst(Vector3 origin, Vector3 direction, float length)
            {
                var ray = new Ray(origin, direction, 0f, length);
                if (!_glass.Traverse(ref ray, out Hit glassHit))
                    return false;
                if (_solid == null)
                    return true;
                var solidRay = new Ray(origin, direction, 0f, length);
                if (!_solid.Traverse(ref solidRay, out Hit solidHit))
                    return true;
                return glassHit.T <= solidHit.T;
            }
        }

        /// <summary>

        /// <summary>
        /// How tall the thing in front of a job position is. Retail's assault positions all have
        /// something to fight from behind - 0 of 46 on SCI_Hub face open floor, and their tenth
        /// percentile obstacle stands 1.5 m - while ours face nothing 31% of the time.
        /// </summary>
        public sealed class ObstacleProbe
        {
            BVHAccel _solid;

            public static ObstacleProbe FromLevel(Level level, Action<string> log = null)
            {
                if (!RadiosityOccluders.TryCollect(level, null, out float[] verts, out int[] tris, null, true, null))
                {
                    log?.Invoke("  obstacle probe: no collision packfile, skipped");
                    return null;
                }
                if (tris.Length < 3)
                    return null;
                var probe = new ObstacleProbe { _solid = new BVHAccel() };
                probe._solid.Build(verts, tris);
                return probe;
            }

            /// <summary>
            /// Height of the highest unbroken run of surface in front of <paramref name="at"/>,
            /// starting from the floor. A ceiling three metres up with nothing under it is not
            /// cover, so the scan stops at the first gap.
            /// </summary>
            public float TopInFront(Vector3 at, Vector3 facing, float distance, float maxHeight, float step)
            {
                if (_solid == null)
                    return 0f;
                var flat = new Vector3(facing.X, 0f, facing.Z);
                if (flat.LengthSquared() < 1e-8f)
                    return 0f;
                flat = Vector3.Normalize(flat);

                float top = 0f;
                for (float h = step; h <= maxHeight + 1e-4f; h += step)
                {
                    var origin = new Vector3(at.X, at.Y + h, at.Z);
                    var ray = new Ray(origin, flat, 0f, distance);
                    if (!_solid.Traverse(ref ray, out Hit _))
                        break;
                    top = h;
                }
                return top;
            }

            /// <summary>
            /// How far the wall in front of <paramref name="rimPoint"/> carries on before it ends,
            /// in whichever of the two directions along it ends sooner. Walks out in quarter-metre
            /// steps casting a short ray at chest height and stops at the first step that finds
            /// nothing, so a stub of wall reads short and the middle of a long one reads
            /// <paramref name="limit"/>.
            /// </summary>
            public float WallEndDistance(Vector3 rimPoint, Vector3 outward, float floorY, float limit)
            {
                if (_solid == null)
                    return limit;
                var along = new Vector3(outward.Z, 0f, -outward.X);
                float best = limit;
                for (int dir = -1; dir <= 1; dir += 2)
                {
                    for (float d = 0.25f; d <= limit; d += 0.25f)
                    {
                        Vector3 q = rimPoint + along * (dir * d);
                        var origin = new Vector3(q.X, floorY + 1.2f, q.Z);
                        var ray = new Ray(origin, outward, 0.02f, 1.0f);
                        if (_solid.Traverse(ref ray, out Hit _))
                            continue;
                        if (d < best)
                            best = d;
                        break;
                    }
                }
                return best;
            }

        }
        /// Fill in the three job-position files on every state that has a navmesh. Nothing is
        /// written to disk - <see cref="Level.Save"/> persists them with the rest of the level.
        /// </summary>
        public static void BakeLevel(Level level, JobPositionBakeSettings settings, Action<string> log = null)
        {
            // No settings means the caller did not ask for job positions; leave the files alone.
            if (settings == null)
                return;
            if (level == null)
                throw new ArgumentNullException(nameof(level));
            if (level.StateResources == null || level.StateResources.Count == 0)
                throw new ArgumentException("No state resources to bake.", nameof(level));

            GlassProbe glass = null;
            ObstacleProbe obstacles = null;
            bool obstaclesTried = false;

            for (int i = 0; i < level.StateResources.Count; i++)
            {
                Level.State state = level.StateResources[i];
                if (state?.NavMesh == null)
                {
                    log?.Invoke("JobPositions STATE_" + i + ": no navmesh, skipped");
                    continue;
                }

                glass ??= settings.GlassWallTest ? GlassProbe.FromLevel(level, log) : null;
                if (!obstaclesTried && (settings.AssaultRequireObstacle || settings.SpottingRequireObstacle))
                {
                    obstacles = ObstacleProbe.FromLevel(level, log);
                    obstaclesTried = true;
                }
                BakeResult result = Bake(state.NavMesh, settings, glass, state.Cover, obstacles);
                state.SpottingPositions = result.Spotting;
                state.CrawlSpaceSpottingPositions = result.CrawlSpace;
                state.AssaultPositions = result.Assault;
                log?.Invoke("JobPositions STATE_" + i + ": " + result.Message);
            }
        }

        public static BakeResult Bake(NavigationMesh nav, JobPositionBakeSettings settings = null, GlassProbe glass = null, Cover cover = null, ObstacleProbe obstacles = null)
        {
            if (nav == null)
                throw new ArgumentNullException(nameof(nav));
            settings ??= JobPositionBakeSettings.CreateDefault();
            if (!settings.GlassWallTest)
                glass = null;
            int glassRejected = 0;

            List<RimEdge> rim = CollectRim(nav, null);
            List<RimEdge> crawlRim = CollectRim(nav, NavigationMesh.AreaHeight.DeepCrouch);

            // Neither the spotting nor the assault pass wants crouch rim. For assault that was
            // already known - retail puts 46 of 46 SCI_Hub and 44 of 44 BSP_TORRENS positions on
            // standing floor. For spotting it is newer and stronger: filtering to standing rim loses
            // NO retail job on any of the 31 levels while cutting the surplus everywhere. See
            // SpottingRequireStandingFloor. Both passes share the chaining when both flags are on.
            List<List<RimEdge>> allRuns = null, standingRuns = null;
            List<List<RimEdge>> AllRuns() => allRuns ??= ChainRuns(rim, settings.RunMaxTurnDegrees);
            List<List<RimEdge>> StandingRuns() => standingRuns ??=
                ChainRuns(CollectRim(nav, NavigationMesh.AreaHeight.Standing, matchHeight: true), settings.RunMaxTurnDegrees);

            List<List<RimEdge>> spottingRuns = settings.SpottingRequireStandingFloor ? StandingRuns() : AllRuns();

            List<List<RimEdge>> assaultRuns;
            if (settings.AssaultFromCover && cover != null && cover.Entries != null && cover.Entries.Count > 0)
                assaultRuns = ChainCoverRuns(cover.Entries, settings);
            else
                assaultRuns = settings.AssaultRequireStandingFloor ? StandingRuns() : AllRuns();

            // The job sits just off the collision surface, which is a walkable radius outside the
            // eroded rim - hence retail's spotting jobs being off the navmesh. The task is measured
            // from the job, not from the wall, which is what makes the pair exactly 1 m apart.
            float spottingOut = settings.WalkableRadius
                                - settings.SpottingPositionDistanceOffset
                                - settings.SpottingExtraDistanceFromCollision;
            var spottingJobs = new List<SpottingPositions.JobInfo>();
            var placed = new List<Vector3>();
            float mergeSq = settings.SpottingMergeDistance * settings.SpottingMergeDistance;
            foreach ((Vector3 on, Vector3 inward, Vector3 localInward) in SampleRuns(
                         spottingRuns,
                         settings.SpottingCoverLengthToGenerateOnePoint,
                         settings.SpottingCoverLengthToGenerateAtBothEnds,
                         settings.SpottingMinDistanceFromEdgeOfCover,
                         settings.SpottingMaxDistanceBetweenPositionsOnSameCover))
            {
                if (IsGlass(glass, settings, on, inward,
                            settings.SpottingGlassTestStartDistance, settings.SpottingGlassTestEndDistance,
                            settings.SpottingGlassTestRayHeightOffset, settings.SpottingGlassTestRayRadius))
                {
                    glassRejected++;
                    continue;
                }

                Vector3 job = on - inward * spottingOut;

                if (settings.SpottingRequireObstacle && obstacles != null &&
                    obstacles.TopInFront(job, -inward, settings.AssaultObstacleProbeDistance,
                                         settings.AssaultObstacleMaxHeight, settings.AssaultObstacleHeightStep)
                        < settings.SpottingMinObstacleHeight)
                    continue;

                bool merged = false;
                for (int i = 0; i < placed.Count; i++)
                {
                    if (Vector3.DistanceSquared(placed[i], job) >= mergeSq)
                        continue;
                    merged = true;
                    break;
                }
                if (merged)
                    continue;

                placed.Add(job);
                spottingJobs.Add(new SpottingPositions.JobInfo
                {
                    JobPosition = job,
                    TaskPosition = job + inward * settings.SpottingPathPositionDistanceOffset
                });
            }

            int obstacleRejected = 0;
            int wallRejected = 0;
            List<AssaultPositions.JobInfo> assaultJobs = BuildAssaultAlongCover(
                assaultRuns, settings, glass, obstacles, ref glassRejected, ref obstacleRejected, ref wallRejected);

            List<SpottingPositions.JobInfo> crawlJobs = BuildCrawlSpace(nav, crawlRim, settings);

            var result = new BakeResult
            {
                Spotting = BuildSpotting(spottingJobs, settings.GridUnitSize),
                CrawlSpace = BuildSpotting(crawlJobs, settings.GridUnitSize),
                Assault = BuildAssault(assaultJobs, settings.GridUnitSize)
            };
            result.Message = "rim " + rim.Count + " edges, crawl mouths " + crawlRim.Count +
                             (glass == null ? "" : "   glass tris " + glass.TriangleCount + " rejected " + glassRejected) +
                             "   spotting " + spottingJobs.Count +
                             "   crawl " + crawlJobs.Count +
                             "   assault " + assaultJobs.Count +
                             (obstacleRejected == 0 ? "" : " (obstacle gate rejected " + obstacleRejected + ")") +
                             (wallRejected == 0 ? "" : " (wall-length gate rejected " + wallRejected + " runs)");
            return result;
        }

        struct RimEdge
        {
            public Vector3 A;
            public Vector3 B;
            /// <summary>Unit, flat in XZ, pointing into the polygon the edge came from.</summary>
            public Vector3 Inward;
            public float Length;
            /// <summary>Index of the polygon the edge came off, so a mouth can be traced to its region.</summary>
            public int Poly;
        }

        /// <summary>
        /// Polygon edges with no neighbour - where the walkable surface stops. When
        /// <paramref name="onlyHeight"/> is set, only polygons of that height class contribute and
        /// an edge counts as rim if the neighbour is missing OR of a different class, which is
        /// what bounds a crawl space inside otherwise open floor.
        /// </summary>
        /// <param name="matchHeight">
        /// Restrict which polygons contribute to <paramref name="onlyHeight"/>, but keep the plain
        /// meaning of rim - an edge with nothing on the far side - rather than the crawl-space
        /// mouth rule. This is how the assault pass asks for "the rim of standing floor only".
        /// </param>
        static List<RimEdge> CollectRim(NavigationMesh nav, NavigationMesh.AreaHeight? onlyHeight, bool matchHeight = false)
        {
            var rim = new List<RimEdge>();
            if (nav?.Polygons == null || nav.Vertices == null)
                return rim;

            for (int p = 0; p < nav.Polygons.Length; p++)
            {
                NavigationMesh.dtPoly poly = nav.Polygons[p];
                if (poly.verts == null || poly.vertCount < 3)
                    continue;
                // Floor only. The alien's backstage sheet is part of the same mesh, and its edges
                // read as rim: before the obstacle gate went in, 30 of our 66 assault positions on
                // ENG_Alien_Nest stood on the ceiling against retail's 0 of 27. The gate happens to
                // reject them now because a floating sheet has nothing in front of it, but that is
                // an accident and this is the rule.
                if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND)
                    continue;
                if (((uint)poly.area.GetMarkupFlags() & 2u) != 0)
                    continue;
                if (onlyHeight.HasValue && poly.area.GetHeightLimitedAmount() != onlyHeight.Value)
                    continue;

                Vector3 centre = Vector3.Zero;
                for (int i = 0; i < poly.vertCount; i++)
                    centre += nav.Vertices[poly.verts[i]];
                centre /= poly.vertCount;

                for (int i = 0; i < poly.vertCount; i++)
                {
                    if (!IsRim(nav, poly, i, matchHeight ? null : onlyHeight))
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

                    rim.Add(new RimEdge { A = a, B = b, Inward = normal, Length = length, Poly = p });
                }
            }
            return rim;
        }

        /// <summary>
        /// With no height filter, an edge is rim when nothing is on the far side. With one, only
        /// the mouths count: edges where the region gives onto walkable floor of another class.
        /// An edge with no neighbour at all is the crawl space's own outer wall, not a way in.
        /// </summary>
        static bool IsRim(NavigationMesh nav, NavigationMesh.dtPoly poly, int edge, NavigationMesh.AreaHeight? onlyHeight)
        {
            if (poly.neis == null || edge >= poly.neis.Length)
                return !onlyHeight.HasValue;
            int nei = poly.neis[edge];
            if (nei == 0)
                return !onlyHeight.HasValue;
            if (!onlyHeight.HasValue)
                return false;

            // neis holds a 1-based polygon index within the tile.
            int index = nei - 1;
            if (index < 0 || index >= nav.Polygons.Length)
                return false;
            return nav.Polygons[index].area.GetHeightLimitedAmount() != onlyHeight.Value;
        }

        /// <summary>
        /// Lay assault positions along each continuous run of cover, following the engine's own
        /// rules: nothing below <c>cover_length_to_generate_one_point</c>, a single mid-run point
        /// up to <c>cover_length_to_generate_at_both_ends</c>, then one
        /// <c>min_distance_from_edge_of_cover</c> in from each end with more spread evenly between
        /// so no gap exceeds <c>max_distance_between_positions_on_same_cover</c>.
        /// </summary>
        /// <remarks>
        /// The engine runs this over cover volumes. We rebuild the runs from the navmesh rim
        /// instead, because the COVER file that ships is only the tactically usable subset and is
        /// nowhere near enough on its own - BSP_TORRENS has 17 segments totalling 34 m, of which
        /// three are long enough to qualify, against 46 assault positions in the shipped file.
        /// The rim stands in for the wall, inset from it by the walkable radius, which is why
        /// retail's positions sit 0.208 m inside the rim and not the full 0.5 m off the geometry.
        /// Doing it this way took the match against retail from 39/41/46% to 65/72/56% on
        /// BSP_TORRENS / Solace / SCI_Hub.
        /// </remarks>
        static List<AssaultPositions.JobInfo> BuildAssaultAlongCover(
            List<List<RimEdge>> runs, JobPositionBakeSettings settings, GlassProbe glass,
            ObstacleProbe obstacles, ref int glassRejected, ref int obstacleRejected, ref int wallRejected)
        {
            var jobs = new List<AssaultPositions.JobInfo>();
            // Measured from the COLLISION surface, which the eroded rim sits AssaultRimToCollision
            // inside of - not the walkable radius. See AssaultRimToCollision.
            float inset = settings.AssaultDistanceFromGeometry - settings.AssaultRimToCollision;

            // Decide the WALL first, then place on it. The obstacle test used to run per position,
            // which reads one point through whatever happens to be in front of it; averaging over
            // the whole run is the measurement that actually separates the walls retail assaults
            // from. See AssaultRunMeanObstacleHeight.
            if (settings.AssaultRequireRunObstacle && obstacles != null)
            {
                var kept = new List<List<RimEdge>>(runs.Count);
                foreach (List<RimEdge> run in runs)
                {
                    if (RunMeanObstacleTop(run, obstacles, settings, inset) >= settings.AssaultRunMeanObstacleHeight)
                        kept.Add(run);
                    else
                        obstacleRejected++;
                }
                runs = kept;
            }

            // Reject a whole wall that is too short to be a wall. This is the single biggest thing
            // separating retail's assault positions from ours - see AssaultMinWallEndDistance.
            if (settings.AssaultRequireWallLength && obstacles != null)
            {
                var walls = new List<List<RimEdge>>(runs.Count);
                foreach (List<RimEdge> run in runs)
                {
                    // A run too short to carry a position at all is not worth probing.
                    float runLength = 0f;
                    foreach (RimEdge e in run)
                        runLength += e.Length;
                    if (runLength < settings.AssaultCoverLengthToGenerateOnePoint)
                    {
                        walls.Add(run);
                        continue;
                    }

                    float far = 0f;
                    foreach (RimEdge e in run)
                    {
                        Vector3 mid = (e.A + e.B) * 0.5f;
                        float d = obstacles.WallEndDistance(mid, -e.Inward, mid.Y,
                                                            settings.AssaultMinWallEndDistance + 0.25f);
                        if (d > far)
                            far = d;
                        if (far >= settings.AssaultMinWallEndDistance)
                            break;
                    }
                    if (far >= settings.AssaultMinWallEndDistance)
                        walls.Add(run);
                    else
                        wallRejected++;
                }
                runs = walls;
            }

            foreach ((Vector3 on, Vector3 inward, Vector3 localInward) in SampleRuns(
                         runs,
                         settings.AssaultCoverLengthToGenerateOnePoint,
                         settings.AssaultCoverLengthToGenerateAtBothEnds,
                         settings.AssaultMinDistanceFromEdgeOfCover,
                         settings.AssaultMaxDistanceBetweenPositionsOnSameCover))
            {
                if (IsGlass(glass, settings, on, inward,
                            settings.AssaultGlassTestStartDistance, settings.AssaultGlassTestEndDistance,
                            settings.AssaultGlassTestRayHeightOffset, settings.AssaultGlassTestRayRadius))
                {
                    glassRejected++;
                    continue;
                }

                Vector3 stand = on + inward * inset;
                Vector3 yawInward = settings.AssaultYawFromLocalEdge ? localInward : inward;
                if (settings.AssaultRequireObstacle && obstacles != null &&
                    obstacles.TopInFront(stand, -inward, settings.AssaultObstacleProbeDistance,
                                         settings.AssaultObstacleMaxHeight, settings.AssaultObstacleHeightStep)
                        < settings.AssaultMinObstacleHeight)
                {
                    obstacleRejected++;
                    continue;
                }

                // Two assault positions on top of each other are one position. Retail never puts a
                // pair closer than about a metre along the same wall - see AssaultMergeDistance.
                if (settings.AssaultMergeDistance > 0f)
                {
                    float m2 = settings.AssaultMergeDistance * settings.AssaultMergeDistance;
                    bool merged = false;
                    foreach (AssaultPositions.JobInfo j in jobs)
                        if (Vector3.DistanceSquared(j.Position, stand) < m2) { merged = true; break; }
                    if (merged)
                        continue;
                }

                jobs.Add(new AssaultPositions.JobInfo
                {
                    Position = stand,
                    // Facing the cover, which is the way retail's yaws point. The run's mean normal
                    // is the wrong thing to face on a curved wall - see AssaultYawFromLocalEdge.
                    Yaw = MathF.Atan2(-yawInward.X, -yawInward.Z)
                });
            }
            return jobs;
        }

        /// <summary>
        /// Mean height of the thing in front of a run, sampled along its whole length. Length
        /// weighted, so a long edge counts for more than a sliver.
        /// </summary>
        static float RunMeanObstacleTop(List<RimEdge> run, ObstacleProbe obstacles,
                                        JobPositionBakeSettings settings, float inset)
        {
            double sum = 0, total = 0;
            foreach (RimEdge e in run)
            {
                int steps = Math.Max(1, (int)Math.Round(e.Length / 0.5f));
                float w = e.Length / steps;
                for (int i = 0; i < steps; i++)
                {
                    Vector3 on = Vector3.Lerp(e.A, e.B, (i + 0.5f) / steps);
                    Vector3 stand = on + e.Inward * inset;
                    sum += w * obstacles.TopInFront(stand, -e.Inward, settings.AssaultObstacleProbeDistance,
                                                    settings.AssaultObstacleMaxHeight, settings.AssaultObstacleHeightStep);
                    total += w;
                }
            }
            return total <= 0 ? 0f : (float)(sum / total);
        }

        /// <summary>
        /// Place points along each run of cover: nothing below <paramref name="minLength"/>, one
        /// at the middle up to <paramref name="bothEndsLength"/>, then one
        /// <paramref name="edgeInset"/> in from each end with more spread evenly between so no gap
        /// exceeds <paramref name="maxGap"/>. Yields the point on the run and the run's normal.
        /// </summary>
        static IEnumerable<(Vector3 on, Vector3 inward, Vector3 localInward)> SampleRuns(
            List<List<RimEdge>> runs,
            float minLength,
            float bothEndsLength,
            float edgeInset,
            float maxGap)
        {
            float gap = Math.Max(0.5f, maxGap);
            float inset = Math.Max(0f, edgeInset);

            foreach (List<RimEdge> run in runs)
            {
                float length = 0f;
                for (int i = 0; i < run.Count; i++)
                    length += run[i].Length;
                if (length < minLength)
                    continue;

                // One normal for the whole run, weighted by edge length so a stray short segment
                // at one end cannot swing the facing.
                Vector3 inward = Vector3.Zero;
                for (int i = 0; i < run.Count; i++)
                    inward += run[i].Inward * run[i].Length;
                if (inward.LengthSquared() < 1e-8f)
                    continue;
                inward = Vector3.Normalize(inward);

                if (length < bothEndsLength)
                {
                    yield return (AlongRun(run, length * 0.5f), inward, InwardAlongRun(run, length * 0.5f));
                    continue;
                }

                float first = inset;
                float last = length - inset;
                int steps = Math.Max(1, (int)MathF.Ceiling((last - first) / gap));
                for (int i = 0; i <= steps; i++)
                {
                    float at = first + (last - first) * i / steps;
                    yield return (AlongRun(run, at), inward, InwardAlongRun(run, at));
                }
            }
        }

        /// <summary>
        /// Sweep the glass test through the cover at this point. Distances are measured from the
        /// collision surface, which sits a walkable radius outside the rim: positive is the
        /// walkable side, negative passes through to the far side.
        /// </summary>
        static bool IsGlass(
            GlassProbe glass,
            JobPositionBakeSettings settings,
            Vector3 on,
            Vector3 inward,
            float startDistance,
            float endDistance,
            float heightOffset,
            float radius)
        {
            if (glass == null)
                return false;

            Vector3 wall = on - inward * settings.WalkableRadius;
            var lift = new Vector3(0f, heightOffset, 0f);
            return glass.Hits(wall + inward * startDistance + lift, wall + inward * endDistance + lift, radius);
        }

        /// <summary>Chain rim edges end to end while the direction holds, so one wall is one run.</summary>
        static List<List<RimEdge>> ChainRuns(List<RimEdge> rim, float maxTurnDegrees)
        {
            var runs = new List<List<RimEdge>>();
            if (rim == null || rim.Count == 0)
                return runs;

            var startsAt = new Dictionary<(long, long, long), List<int>>();
            for (int i = 0; i < rim.Count; i++)
            {
                (long, long, long) key = Quantise(rim[i].A);
                if (!startsAt.TryGetValue(key, out List<int> at))
                    startsAt[key] = at = new List<int>();
                at.Add(i);
            }

            float cosLimit = MathF.Cos(Math.Max(0f, maxTurnDegrees) * MathF.PI / 180f);
            var used = new bool[rim.Count];
            for (int i = 0; i < rim.Count; i++)
            {
                if (used[i])
                    continue;
                used[i] = true;
                var run = new List<RimEdge> { rim[i] };
                Vector3 direction = Direction(rim[i]);
                Vector3 tail = rim[i].B;

                while (startsAt.TryGetValue(Quantise(tail), out List<int> candidates))
                {
                    int pick = -1;
                    for (int c = 0; c < candidates.Count; c++)
                    {
                        int j = candidates[c];
                        if (used[j])
                            continue;
                        if (Vector3.Dot(Direction(rim[j]), direction) < cosLimit)
                            continue;
                        pick = j;
                        break;
                    }
                    if (pick < 0)
                        break;
                    used[pick] = true;
                    run.Add(rim[pick]);
                    direction = Direction(rim[pick]);
                    tail = rim[pick].B;
                }
                runs.Add(run);
            }
            return runs;
        }


        /// <summary>
        /// Turn baked cover segments back into runs the assault sampler can walk. A segment's face
        /// sits <see cref="JobPositionBakeSettings.AssaultCoverRimOffset"/> outside the navmesh rim,
        /// so it is put back on the rim first and the usual inset applied from there.
        /// </summary>
        /// <remarks>
        /// Chaining is endpoint-based and flip-tolerant: the cover baker cuts a continuous run into
        /// segments of at most its maximum length, and adjacent polygons hand out their boundary in
        /// opposite directions, so a segment's Left is as likely to meet the neighbour's Left as its
        /// Right.
        /// </remarks>
        static List<List<RimEdge>> ChainCoverRuns(List<Cover.CoverSegment> segments, JobPositionBakeSettings settings)
        {
            var edges = new List<RimEdge>(segments.Count);
            foreach (Cover.CoverSegment s in segments)
            {
                var normal = new Vector3(s.Normal.X, 0f, s.Normal.Z);
                if (normal.LengthSquared() < 1e-8f)
                    continue;
                normal = Vector3.Normalize(normal);
                Vector3 a = s.Left + normal * settings.AssaultCoverRimOffset;
                Vector3 b = s.Right + normal * settings.AssaultCoverRimOffset;
                float length = new Vector3(b.X - a.X, 0f, b.Z - a.Z).Length();
                if (length < 1e-4f)
                    continue;
                edges.Add(new RimEdge { A = a, B = b, Inward = normal, Length = length });
            }

            var at = new Dictionary<(long, long, long), List<int>>();
            for (int i = 0; i < edges.Count; i++)
            {
                IndexEnd(at, edges[i].A, i);
                IndexEnd(at, edges[i].B, i);
            }

            float cosLimit = MathF.Cos(Math.Max(0f, settings.RunMaxTurnDegrees) * MathF.PI / 180f);
            var used = new bool[edges.Count];
            var runs = new List<List<RimEdge>>();

            for (int i = 0; i < edges.Count; i++)
            {
                if (used[i])
                    continue;
                used[i] = true;
                var run = new List<RimEdge> { edges[i] };

                Extend(run, edges, at, used, cosLimit, forward: true);
                Extend(run, edges, at, used, cosLimit, forward: false);
                runs.Add(run);
            }
            return runs;
        }

        static void IndexEnd(Dictionary<(long, long, long), List<int>> at, Vector3 p, int i)
        {
            (long, long, long) key = Quantise(p);
            if (!at.TryGetValue(key, out List<int> l))
                at[key] = l = new List<int>();
            l.Add(i);
        }

        /// <summary>Walk off one end of a run, flipping candidates whose direction is reversed.</summary>
        static void Extend(List<RimEdge> run, List<RimEdge> edges, Dictionary<(long, long, long), List<int>> at,
                           bool[] used, float cosLimit, bool forward)
        {
            while (true)
            {
                RimEdge end = forward ? run[run.Count - 1] : run[0];
                Vector3 tail = forward ? end.B : end.A;
                Vector3 direction = Direction(end);
                if (!at.TryGetValue(Quantise(tail), out List<int> candidates))
                    return;

                int pick = -1;
                RimEdge chosen = default;
                foreach (int j in candidates)
                {
                    if (used[j])
                        continue;
                    RimEdge e = edges[j];
                    bool meetsA = Quantise(e.A) == Quantise(tail);
                    // Continuing forward the next edge must start where we stopped; going backwards
                    // it must end there. Either way the other endpoint means the edge runs the
                    // wrong way and has to be flipped.
                    if (forward ? !meetsA : meetsA)
                        e = new RimEdge { A = e.B, B = e.A, Inward = e.Inward, Length = e.Length };
                    if (Vector3.Dot(Direction(e), direction) < cosLimit)
                        continue;
                    pick = j;
                    chosen = e;
                    break;
                }
                if (pick < 0)
                    return;

                used[pick] = true;
                if (forward)
                    run.Add(chosen);
                else
                    run.Insert(0, chosen);
            }
        }
        static Vector3 Direction(RimEdge e) =>
            Vector3.Normalize(new Vector3(e.B.X - e.A.X, 0f, e.B.Z - e.A.Z));

        static (long, long, long) Quantise(Vector3 v) =>
            ((long)MathF.Round(v.X * 64f), (long)MathF.Round(v.Y * 64f), (long)MathF.Round(v.Z * 64f));

        static Vector3 AlongRun(List<RimEdge> run, float distance)
        {
            float walked = 0f;
            for (int i = 0; i < run.Count; i++)
            {
                if (walked + run[i].Length >= distance)
                {
                    float t = run[i].Length <= 1e-6f ? 0f : (distance - walked) / run[i].Length;
                    return Vector3.Lerp(run[i].A, run[i].B, t);
                }
                walked += run[i].Length;
            }
            return run[run.Count - 1].B;
        }

        /// <summary>The inward normal of the rim edge the walk lands on, rather than the run's mean.</summary>
        static Vector3 InwardAlongRun(List<RimEdge> run, float distance)
        {
            float walked = 0f;
            for (int i = 0; i < run.Count; i++)
            {
                if (walked + run[i].Length >= distance)
                    return run[i].Inward;
                walked += run[i].Length;
            }
            return run[run.Count - 1].Inward;
        }


        /// <summary>
        /// Deep-crouch polygons flooded into connected regions, with the depth of each: the
        /// furthest any of the region's own outer wall gets from a way into it. A vent you cannot
        /// get properly inside is not a crawl space worth a job.
        /// </summary>
        /// <remarks>
        /// The existing depth test is per MOUTH - it probes inward from one edge and caps at the
        /// spot offset - so a region with one deep pocket and one shallow entrance is judged twice
        /// and inconsistently. Retail decides per region: searching region features against its own
        /// files at position level, <c>maxDepth &gt;= 0.48</c> is the only term that pays, and it is
        /// worth 72.1 -> 76.6. See CrawlMinRegionDepth.
        /// </remarks>
        static float[] CrawlRegionDepths(NavigationMesh nav, out int[] regionOf)
        {
            int n = nav.Polygons.Length;
            regionOf = new int[n];
            for (int i = 0; i < n; i++)
                regionOf[i] = -1;

            bool Deep(int p)
            {
                if (p < 0 || p >= n)
                    return false;
                NavigationMesh.dtPoly q = nav.Polygons[p];
                if (q.verts == null || q.vertCount < 3)
                    return false;
                if (q.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND)
                    return false;
                if (((uint)q.area.GetMarkupFlags() & 2u) != 0)
                    return false;
                return q.area.GetHeightLimitedAmount() == NavigationMesh.AreaHeight.DeepCrouch;
            }

            int regions = 0;
            var stack = new Stack<int>();
            for (int p = 0; p < n; p++)
            {
                if (!Deep(p) || regionOf[p] >= 0)
                    continue;
                int id = regions++;
                stack.Push(p);
                regionOf[p] = id;
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    NavigationMesh.dtPoly poly = nav.Polygons[c];
                    if (poly.neis == null)
                        continue;
                    for (int i = 0; i < poly.vertCount && i < poly.neis.Length; i++)
                    {
                        int nei = poly.neis[i] - 1;
                        if (nei < 0 || !Deep(nei) || regionOf[nei] >= 0)
                            continue;
                        regionOf[nei] = id;
                        stack.Push(nei);
                    }
                }
            }

            // Each region's ways in and its own outer wall, then how far the wall gets from a way in.
            var mouths = new List<(Vector3 a, Vector3 b)>[regions];
            var walls = new List<Vector3>[regions];
            for (int i = 0; i < regions; i++)
            {
                mouths[i] = new List<(Vector3, Vector3)>();
                walls[i] = new List<Vector3>();
            }
            for (int p = 0; p < n; p++)
            {
                int id = regionOf[p];
                if (id < 0)
                    continue;
                NavigationMesh.dtPoly poly = nav.Polygons[p];
                for (int i = 0; i < poly.vertCount; i++)
                {
                    Vector3 a = nav.Vertices[poly.verts[i]];
                    Vector3 b = nav.Vertices[poly.verts[(i + 1) % poly.vertCount]];
                    int nei = poly.neis == null || i >= poly.neis.Length ? 0 : poly.neis[i];
                    if (nei == 0)
                        walls[id].Add((a + b) * 0.5f);
                    else if (nei - 1 >= 0 && nei - 1 < n && !Deep(nei - 1))
                        mouths[id].Add((a, b));   // out of range is neither, as CollectRim has it
                }
            }

            var depth = new float[regions];
            for (int i = 0; i < regions; i++)
            {
                if (mouths[i].Count == 0)
                    continue;
                float far = 0f;
                foreach (Vector3 w in walls[i])
                {
                    float d = float.MaxValue;
                    foreach ((Vector3 a, Vector3 b) mo in mouths[i])
                        d = MathF.Min(d, PointToSegment(w, mo.a, mo.b));
                    if (d < float.MaxValue && d > far)
                        far = d;
                }
                depth[i] = far;
            }
            return depth;
        }

        static float PointToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float l2 = ab.LengthSquared();
            float t = l2 < 1e-9f ? 0f : Vector3.Dot(p - a, ab) / l2;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>
        /// Crawl spaces: the job is the spot inside the vent worth checking, the task is where an
        /// NPC stands outside it to look in.
        /// </summary>
        /// <remarks>
        /// Retail's crawl jobs are all on walkable surface with several metres of clear run ahead,
        /// and sit a median 0.19 m from a deep-crouch polygon centroid (min 0.07, max 0.50 on
        /// Solace) - so they are the centroids, not samples off the region's rim. The task is
        /// pushed out through the nearest way out of the crawl space, which is why retail's
        /// job-to-task distances vary between 1.06 and 1.73 m rather than being constant.
        /// </remarks>
        static List<SpottingPositions.JobInfo> BuildCrawlSpace(
            NavigationMesh nav, List<RimEdge> mouths, JobPositionBakeSettings settings)
        {
            var jobs = new List<SpottingPositions.JobInfo>();
            if (nav?.Polygons == null || mouths == null || mouths.Count == 0)
                return jobs;

            List<Vector3[]> deep = HeightPolys(nav, NavigationMesh.AreaHeight.DeepCrouch);

            // Decide the REGION first, then place in it. See CrawlMinRegionDepth.
            float[] regionDepth = null;
            int[] regionOf = null;
            if (settings.CrawlRequireRegionDepth)
                regionDepth = CrawlRegionDepths(nav, out regionOf);

            float mergeSq = settings.CrawlMinSeparation * settings.CrawlMinSeparation;
            float reach = settings.CrawlSpottingPositionDistanceOffset;
            float minInside = settings.CrawlMinDistanceInsideDeepCrouchForSpotPosition;
            var placed = new List<Vector3>();

            for (int e = 0; e < mouths.Count; e++)
            {
                RimEdge mouth = mouths[e];
                if (regionDepth != null)
                {
                    int region = mouth.Poly >= 0 && mouth.Poly < regionOf.Length ? regionOf[mouth.Poly] : -1;
                    if (region < 0 || regionDepth[region] < settings.CrawlMinRegionDepth)
                        continue;
                }

                Vector3 centre = (mouth.A + mouth.B) * 0.5f;

                // Probe back into the crawl space: the spot goes as deep as the offset asks for, or
                // as deep as the region actually runs, whichever is less. Probing only along the
                // edge normal reads a vent entered from its SIDE as shallow and throws the job away,
                // so the probe fans out and keeps the deepest direction that stays inside.
                float depth = 0f;
                Vector3 inward = mouth.Inward;
                const float step = 0.0625f;
                foreach (float degrees in settings.CrawlProbeFanDegrees)
                {
                    Vector3 dir = RotateY(mouth.Inward, degrees);
                    float reached = 0f;
                    for (float d = step; d <= reach + 1e-4f; d += step)
                    {
                        if (!InsideAny(deep, centre + dir * d))
                            break;
                        reached = d;
                    }
                    if (reached > depth) { depth = reached; inward = dir; }
                }
                if (depth < minInside)
                    continue;

                Vector3 spot = centre + inward * depth;
                Vector3 path = centre - mouth.Inward * settings.CrawlPathPositionDistanceOffset;
                if (Vector3.Distance(spot, path) <= settings.CrawlMinSpotToPathDistance)
                    continue;

                bool crowded = false;
                for (int i = 0; i < placed.Count; i++)
                {
                    if (Vector3.DistanceSquared(placed[i], spot) >= mergeSq)
                        continue;
                    crowded = true;
                    break;
                }
                if (crowded)
                    continue;

                placed.Add(spot);
                jobs.Add(new SpottingPositions.JobInfo { JobPosition = spot, TaskPosition = path });
            }
            return jobs;
        }

        /// <summary>Rotate a horizontal direction about Y.</summary>
        static Vector3 RotateY(Vector3 v, float degrees)
        {
            if (Math.Abs(degrees) < 1e-4f) return v;
            float r = (float)(degrees * Math.PI / 180.0);
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            return new Vector3(v.X * c - v.Z * s, v.Y, v.X * s + v.Z * c);
        }

        static List<Vector3[]> HeightPolys(NavigationMesh nav, NavigationMesh.AreaHeight height)
        {
            var list = new List<Vector3[]>();
            foreach (NavigationMesh.dtPoly poly in nav.Polygons)
            {
                if (poly.verts == null || poly.vertCount < 3)
                    continue;
                // Backstage is the alien's ceiling network, not playspace.
                if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND)
                    continue;
                if (((uint)poly.area.GetMarkupFlags() & 2u) != 0)
                    continue;
                if (poly.area.GetHeightLimitedAmount() != height)
                    continue;
                var v = new Vector3[poly.vertCount];
                for (int i = 0; i < poly.vertCount; i++)
                    v[i] = nav.Vertices[poly.verts[i]];
                list.Add(v);
            }
            return list;
        }

        static bool InsideAny(List<Vector3[]> polys, Vector3 p)
        {
            for (int i = 0; i < polys.Count; i++)
            {
                Vector3[] v = polys[i];
                bool inside = false;
                for (int a = 0, b = v.Length - 1; a < v.Length; b = a++)
                {
                    if (((v[a].Z > p.Z) != (v[b].Z > p.Z))
                        && (p.X < (v[b].X - v[a].X) * (p.Z - v[a].Z) / (v[b].Z - v[a].Z) + v[a].X))
                        inside = !inside;
                }
                if (!inside)
                    continue;
                // Guard against a crawl space on another storey sharing the footprint.
                float y = 0f;
                for (int k = 0; k < v.Length; k++) y += v[k].Y;
                if (MathF.Abs(y / v.Length - p.Y) > 1.0f)
                    continue;
                return true;
            }
            return false;
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
