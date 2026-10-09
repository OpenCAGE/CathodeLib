#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;
using CATHODE.Enums;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Questions about a level's SAVED navigation mesh (a STATE's NAV_MESH tile, as the last Save &amp; Build wrote it):
    /// the nearest point on it, whether one point can reach another and by what path, and points sampled over it.
    /// </summary>
    /// <remarks>
    /// The CATHODE tile is Detour's own layout (the baker writes it from a DtMeshData, NavMeshBaker.AdaptTile), so it is
    /// read back into a DtMeshData and queried with DotRecast. Only reads: nothing here changes the mesh or what a bake
    /// writes. Each polygon's DotRecast flags carry the classes the CATHODE area admits (bit per
    /// NAVIGATION_CHARACTER_CLASS) and <see cref="DisabledFlag"/> for an area that starts disabled (a closed door's
    /// barrier, opened by script), so a filter can ask for one class, or leave disabled areas out.
    /// </remarks>
    public sealed class NavigationMeshQuery
    {
        /// <summary>Flag on polygons whose area starts disabled.</summary>
        public const int DisabledFlag = 1 << 5;
        /// <summary>Flag on off-mesh connection polygons (ladders, vents, traversals, backstage links), for counting them: a filter lets one through by the classes its link admits.</summary>
        public const int OffMeshFlag = 1 << 6;
        /// <summary>Every character class.</summary>
        public const int AllClasses = 0x1F;

        private readonly DtNavMesh _mesh;
        private readonly DtNavMeshQuery _query;
        private readonly DtMeshTile _tile;
        private readonly NavigationMesh _source;
        private int[] _islands;
        private int _islandsFor = -1;

        public int PolygonCount { get; }
        public int GroundPolygonCount { get; }
        public int OffMeshCount { get; }
        public int DisabledCount { get; }
        public Vector3 BoundsMin { get; }
        public Vector3 BoundsMax { get; }
        public float WalkableHeight => _source.Header.walkableHeight;
        public float WalkableRadius => _source.Header.walkableRadius;
        public float WalkableClimb => _source.Header.walkableClimb;

        public NavigationMeshQuery(NavigationMesh mesh)
        {
            if (mesh?.Polygons == null || mesh.Vertices == null || mesh.Polygons.Length == 0)
                throw new InvalidOperationException("The navigation mesh is empty.");
            _source = mesh;
            NavigationMesh.dtMeshHeader src = mesh.Header;

            const int maxVerts = 6;
            DtMeshData data = new DtMeshData();
            DtMeshHeader header = new DtMeshHeader()
            {
                magic = DtDetour.DT_NAVMESH_MAGIC,
                version = DtDetour.DT_NAVMESH_VERSION,
                x = src.x,
                y = src.y,
                layer = src.layer,
                userId = (int)src.userId,
                polyCount = mesh.Polygons.Length,
                vertCount = mesh.Vertices.Length,
                detailMeshCount = mesh.DetailMeshes?.Length ?? 0,
                detailVertCount = mesh.DetailVertices?.Length ?? 0,
                detailTriCount = (mesh.DetailIndices?.Length ?? 0) / 4,
                bvNodeCount = 0,
                offMeshConCount = mesh.OffMeshConnections?.Length ?? 0,
                offMeshBase = src.offMeshBase,
                walkableHeight = src.walkableHeight,
                walkableRadius = src.walkableRadius,
                walkableClimb = src.walkableClimb,
                bvQuantFactor = src.bvQuantFactor,
            };

            Vector3 min = new Vector3(float.MaxValue), max = new Vector3(float.MinValue);
            data.verts = new float[mesh.Vertices.Length * 3];
            for (int i = 0; i < mesh.Vertices.Length; i++)
            {
                Vector3 v = mesh.Vertices[i];
                data.verts[i * 3] = v.X; data.verts[i * 3 + 1] = v.Y; data.verts[i * 3 + 2] = v.Z;
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            //The tile's own box, padded: queries outside it find nothing
            header.bmin = new RcVec3f(min.X - 1, min.Y - 1, min.Z - 1);
            header.bmax = new RcVec3f(max.X + 1, max.Y + 1, max.Z + 1);
            BoundsMin = min;
            BoundsMax = max;

            int links = 8, ground = 0, offMesh = 0, disabled = 0;
            data.polys = new DtPoly[mesh.Polygons.Length];
            for (int i = 0; i < mesh.Polygons.Length; i++)
            {
                NavigationMesh.dtPoly p = mesh.Polygons[i];
                DtPoly poly = new DtPoly(i, maxVerts);
                poly.vertCount = Math.Min((int)p.vertCount, maxVerts);
                for (int v = 0; v < maxVerts; v++)
                {
                    poly.verts[v] = p.verts != null && v < p.verts.Length ? p.verts[v] : 0;
                    poly.neis[v] = p.neis != null && v < p.neis.Length ? p.neis[v] : 0;
                }
                bool isOffMesh = p.area.GetPolyType() == NavigationMesh.dtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION;
                poly.SetPolyType(isOffMesh ? DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION : DtPolyTypes.DT_POLYTYPE_GROUND);
                poly.SetArea(0);
                int flags = (int)p.area.GetAdmittanceFlags() & AllClasses;
                if (!p.area.GetIsEnabled()) { flags |= DisabledFlag; disabled++; }
                if (isOffMesh) { flags |= OffMeshFlag; offMesh++; }
                else ground++;
                poly.flags = flags;
                data.polys[i] = poly;
                links += poly.vertCount + 2;
            }
            header.maxLinkCount = links + header.offMeshConCount * 4;
            PolygonCount = mesh.Polygons.Length;
            GroundPolygonCount = ground;
            OffMeshCount = offMesh;
            DisabledCount = disabled;

            data.detailMeshes = new DtPolyDetail[header.detailMeshCount];
            for (int i = 0; i < header.detailMeshCount; i++)
            {
                NavigationMesh.dtPolyDetail d = mesh.DetailMeshes[i];
                data.detailMeshes[i] = new DtPolyDetail(d.vertBase, d.triBase, d.vertCount, d.triCount);
            }
            data.detailVerts = new float[header.detailVertCount * 3];
            for (int i = 0; i < header.detailVertCount; i++)
            {
                Vector3 v = mesh.DetailVertices[i];
                data.detailVerts[i * 3] = v.X; data.detailVerts[i * 3 + 1] = v.Y; data.detailVerts[i * 3 + 2] = v.Z;
            }
            data.detailTris = new int[header.detailTriCount * 4];
            for (int i = 0; i < data.detailTris.Length; i++)
                data.detailTris[i] = mesh.DetailIndices[i];
            //No BV tree: queries test every polygon's bounds, which is quick at a level's size and cannot be wrong
            data.bvTree = null;

            data.offMeshCons = new DtOffMeshConnection[header.offMeshConCount];
            for (int i = 0; i < header.offMeshConCount; i++)
            {
                NavigationMesh.dtOffMeshConnection c = mesh.OffMeshConnections[i];
                DtOffMeshConnection con = new DtOffMeshConnection();
                con.pos[0] = new RcVec3f(c.pos[0], c.pos[1], c.pos[2]);
                con.pos[1] = new RcVec3f(c.pos[3], c.pos[4], c.pos[5]);
                con.rad = c.rad;
                con.poly = c.poly_index_within_tile;
                con.flags = c.flags;
                con.side = c.side;
                con.userId = i;
                data.offMeshCons[i] = con;
            }
            data.header = header;

            _mesh = new DtNavMesh();
            DtStatus status = _mesh.Init(data, maxVerts, 0);
            if (status.Failed())
                throw new InvalidOperationException("The navigation mesh could not be read for queries (" + status.Value + ").");
            _tile = _mesh.GetTile(0);
            if (_tile?.data == null)
                throw new InvalidOperationException("The navigation mesh has no tile.");
            _query = new DtNavMeshQuery(_mesh);
        }

        /// <summary>What a query lets through.</summary>
        public sealed class Options
        {
            /// <summary>Character classes (bits of NAVIGATION_CHARACTER_CLASS): a polygon counts when it admits any of them.</summary>
            public int Classes = AllClasses;
            /// <summary>Also cross areas that start disabled (doors and barriers script opens).</summary>
            public bool IncludeDisabled;
            /// <summary>Half extents of the box searched for the nearest polygon (m).</summary>
            public Vector3 Extents = new Vector3(2f, 4f, 2f);
        }

        private static IDtQueryFilter Filter(Options options)
        {
            //Off-mesh polygons carry the classes their link admits like any other (an alien-only vent is ALIEN): OffMeshFlag only counts them
            return new DtQueryDefaultFilter(options.Classes & AllClasses, options.IncludeDisabled ? 0 : DisabledFlag, new float[] { 1f });
        }

        /// <summary>The nearest point on the mesh.</summary>
        public struct Nearest
        {
            public bool Found;
            public Vector3 Point;
            public float Distance;
            /// <summary>The point is over the polygon in plan (its X/Z inside it), not just near its edge.</summary>
            public bool OverPoly;
            public int Polygon;
            public bool Enabled;
            public int Classes;
            public NavigationMesh.AreaHeight Height;
            public int Island;
        }

        public Nearest FindNearest(Vector3 point, Options options)
        {
            options = options ?? new Options();
            _query.FindNearestPoly(new RcVec3f(point.X, point.Y, point.Z), new RcVec3f(options.Extents.X, options.Extents.Y, options.Extents.Z), Filter(options), out long polyRef, out RcVec3f nearest, out bool over);
            if (polyRef == 0)
                return new Nearest() { Found = false, Polygon = -1 };
            DtDetour.DecodePolyId(polyRef, out _, out _, out int index);
            Vector3 p = new Vector3(nearest.X, nearest.Y, nearest.Z);
            NavigationMesh.dt_area_t area = _source.Polygons[index].area;
            return new Nearest()
            {
                Found = true,
                Point = p,
                Distance = Vector3.Distance(p, point),
                OverPoly = over,
                Polygon = index,
                Enabled = area.GetIsEnabled(),
                Classes = (int)area.GetAdmittanceFlags() & AllClasses,
                Height = area.GetHeightLimitedAmount(),
                Island = IslandOf(index, options),
            };
        }

        /// <summary>A path between two points.</summary>
        public sealed class PathResult
        {
            public bool Reachable;
            /// <summary>The search stopped short: Corners lead to the nearest reachable point to the goal.</summary>
            public bool Partial;
            public Vector3 Start, End;
            public List<Vector3> Corners = new List<Vector3>();
            public float Length;
            public int Polygons;
            public int OffMeshLinks;
            public string Failure;
        }

        public PathResult FindPath(Vector3 from, Vector3 to, Options options, int maxPolygons = 8192)
        {
            options = options ?? new Options();
            PathResult result = new PathResult();
            IDtQueryFilter filter = Filter(options);
            RcVec3f ext = new RcVec3f(options.Extents.X, options.Extents.Y, options.Extents.Z);
            _query.FindNearestPoly(new RcVec3f(from.X, from.Y, from.Z), ext, filter, out long startRef, out RcVec3f start, out _);
            _query.FindNearestPoly(new RcVec3f(to.X, to.Y, to.Z), ext, filter, out long endRef, out RcVec3f end, out _);
            if (startRef == 0) { result.Failure = "start is not near the navmesh"; return result; }
            if (endRef == 0) { result.Failure = "end is not near the navmesh"; return result; }
            result.Start = new Vector3(start.X, start.Y, start.Z);
            result.End = new Vector3(end.X, end.Y, end.Z);

            long[] path = new long[maxPolygons];
            DtStatus status = _query.FindPath(startRef, endRef, start, end, filter, path, out int count, maxPolygons);
            if (status.Failed() || count == 0) { result.Failure = "no path (" + status.Value + ")"; return result; }
            result.Polygons = count;
            result.Partial = path[count - 1] != endRef || status.IsPartial();
            result.Reachable = !result.Partial;

            DtStraightPath[] straight = new DtStraightPath[Math.Min(4096, count * 3 + 8)];
            RcVec3f goal = end;
            if (result.Partial)
            {
                //The straight path to the closest point on the last polygon reached
                _query.ClosestPointOnPoly(path[count - 1], end, out goal, out _);
            }
            status = _query.FindStraightPath(start, goal, path, count, straight, out int corners, straight.Length, 0);
            if (status.Failed()) { result.Failure = "path found but could not be straightened (" + status.Value + ")"; return result; }
            for (int i = 0; i < corners; i++)
            {
                RcVec3f p = straight[i].pos;
                result.Corners.Add(new Vector3(p.X, p.Y, p.Z));
                if ((straight[i].flags & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) != 0) result.OffMeshLinks++;
                if (i > 0) result.Length += Vector3.Distance(result.Corners[i - 1], result.Corners[i]);
            }
            return result;
        }

        /// <summary>
        /// Which island of the mesh a polygon is on, for the classes and disabled areas the options let through; -1 for a
        /// polygon they do not. An island is a strongly connected piece (polygons joined by links, off-mesh links included,
        /// each reaching every other): two points on the same island can reach each other both ways. A one-way link (a drop,
        /// a one-way ladder) leaves its ends on different islands - use <see cref="FindPath"/> for whether one reaches the other.
        /// </summary>
        public int IslandOf(int polygon, Options options)
        {
            options = options ?? new Options();
            int key = (options.Classes & AllClasses) | (options.IncludeDisabled ? 1 << 8 : 0);
            if (_islands == null || _islandsFor != key)
            {
                int allowed = options.Classes & AllClasses;
                int count = PolygonCount;
                bool[] passes = new bool[count];
                for (int i = 0; i < count; i++) passes[i] = Passes(_tile.data.polys[i], allowed, options.IncludeDisabled);

                //Tarjan's strongly connected components, iterative (a level's mesh is far too deep for recursion); links are directed
                int[] islands = new int[count], order = new int[count], low = new int[count];
                bool[] onStack = new bool[count];
                int[] stack = new int[count], frameNode = new int[count], frameLink = new int[count];
                for (int i = 0; i < count; i++) { islands[i] = -1; order[i] = -1; }
                int stackTop = 0, frames = 0, counter = 0, next = 0;
                for (int seed = 0; seed < count; seed++)
                {
                    if (!passes[seed] || order[seed] >= 0) continue;
                    order[seed] = low[seed] = counter++;
                    stack[stackTop++] = seed;
                    onStack[seed] = true;
                    frameNode[frames] = seed;
                    frameLink[frames] = _tile.data.polys[seed].firstLink;
                    frames++;
                    while (frames != 0)
                    {
                        int v = frameNode[frames - 1];
                        bool deeper = false;
                        for (int link = frameLink[frames - 1]; link != DtDetour.DT_NULL_LINK; link = _tile.links[link].next)
                        {
                            long target = _tile.links[link].refs;
                            if (target == 0) continue;
                            DtDetour.DecodePolyId(target, out _, out _, out int w);
                            if (w < 0 || w >= count || !passes[w]) continue;
                            if (order[w] < 0)
                            {
                                //Go down into w; carry on from the next link when it is done
                                frameLink[frames - 1] = _tile.links[link].next;
                                order[w] = low[w] = counter++;
                                stack[stackTop++] = w;
                                onStack[w] = true;
                                frameNode[frames] = w;
                                frameLink[frames] = _tile.data.polys[w].firstLink;
                                frames++;
                                deeper = true;
                                break;
                            }
                            if (onStack[w] && order[w] < low[v]) low[v] = order[w];
                        }
                        if (deeper) continue;
                        frames--;
                        if (low[v] == order[v])
                        {
                            int w;
                            do
                            {
                                w = stack[--stackTop];
                                onStack[w] = false;
                                islands[w] = next;
                            }
                            while (w != v);
                            next++;
                        }
                        if (frames != 0)
                        {
                            int parent = frameNode[frames - 1];
                            if (low[v] < low[parent]) low[parent] = low[v];
                        }
                    }
                }
                _islands = islands;
                _islandsFor = key;
                IslandCount = next;
            }
            return polygon < 0 || polygon >= _islands.Length ? -1 : _islands[polygon];
        }

        /// <summary>How many islands the last <see cref="IslandOf"/> found.</summary>
        public int IslandCount { get; private set; }

        private static bool Passes(DtPoly poly, int allowed, bool includeDisabled) => (poly.flags & allowed) != 0 && (includeDisabled || (poly.flags & DisabledFlag) == 0);

        /// <summary>
        /// Points spread over the walkable ground polygons inside a box (all of the mesh when null): area-weighted random
        /// points with a fixed seed, so the same call gives the same points.
        /// </summary>
        public List<Vector3> SamplePoints(int count, Vector3? boxMin, Vector3? boxMax, Options options, int seed = 1)
        {
            options = options ?? new Options();
            int allowed = (options.Classes & AllClasses);
            List<(Vector3 a, Vector3 b, Vector3 c)> triangles = new List<(Vector3, Vector3, Vector3)>();
            List<double> cumulative = new List<double>();
            double total = 0;
            for (int i = 0; i < PolygonCount; i++)
            {
                DtPoly poly = _tile.data.polys[i];
                if (poly.GetPolyType() != DtPolyTypes.DT_POLYTYPE_GROUND || !Passes(poly, allowed, options.IncludeDisabled)) continue;
                Vector3 first = Vertex(poly.verts[0]);
                for (int v = 1; v + 1 < poly.vertCount; v++)
                {
                    Vector3 b = Vertex(poly.verts[v]), c = Vertex(poly.verts[v + 1]);
                    Vector3 centre = (first + b + c) / 3f;
                    if (boxMin != null && (centre.X < boxMin.Value.X || centre.Y < boxMin.Value.Y || centre.Z < boxMin.Value.Z)) continue;
                    if (boxMax != null && (centre.X > boxMax.Value.X || centre.Y > boxMax.Value.Y || centre.Z > boxMax.Value.Z)) continue;
                    double area = Vector3.Cross(b - first, c - first).Length() * 0.5;
                    if (area <= 1e-6) continue;
                    total += area;
                    triangles.Add((first, b, c));
                    cumulative.Add(total);
                }
            }
            List<Vector3> points = new List<Vector3>();
            if (triangles.Count == 0 || count <= 0) return points;
            Random random = new Random(seed);
            for (int k = 0; k < count; k++)
            {
                double pick = random.NextDouble() * total;
                int index = cumulative.BinarySearch(pick);
                if (index < 0) index = ~index;
                if (index >= triangles.Count) index = triangles.Count - 1;
                (Vector3 a, Vector3 b, Vector3 c) = triangles[index];
                double r1 = random.NextDouble(), r2 = random.NextDouble();
                if (r1 + r2 > 1) { r1 = 1 - r1; r2 = 1 - r2; }
                Vector3 p = a + (b - a) * (float)r1 + (c - a) * (float)r2;
                //Onto the detail surface where there is one
                _query.FindNearestPoly(new RcVec3f(p.X, p.Y, p.Z), new RcVec3f(0.5f, 2f, 0.5f), Filter(options), out long polyRef, out RcVec3f snapped, out _);
                points.Add(polyRef != 0 ? new Vector3(snapped.X, snapped.Y, snapped.Z) : p);
            }
            return points;
        }

        /// <summary>The walkable area (m²) of the ground polygons inside a box, or of all of them.</summary>
        public double Area(Vector3? boxMin, Vector3? boxMax, Options options)
        {
            options = options ?? new Options();
            double total = 0;
            for (int i = 0; i < PolygonCount; i++)
            {
                DtPoly poly = _tile.data.polys[i];
                if (poly.GetPolyType() != DtPolyTypes.DT_POLYTYPE_GROUND || !Passes(poly, options.Classes & AllClasses, options.IncludeDisabled)) continue;
                Vector3 first = Vertex(poly.verts[0]);
                for (int v = 1; v + 1 < poly.vertCount; v++)
                {
                    Vector3 b = Vertex(poly.verts[v]), c = Vertex(poly.verts[v + 1]);
                    Vector3 centre = (first + b + c) / 3f;
                    if (boxMin != null && (centre.X < boxMin.Value.X || centre.Y < boxMin.Value.Y || centre.Z < boxMin.Value.Z)) continue;
                    if (boxMax != null && (centre.X > boxMax.Value.X || centre.Y > boxMax.Value.Y || centre.Z > boxMax.Value.Z)) continue;
                    total += Vector3.Cross(b - first, c - first).Length() * 0.5;
                }
            }
            return total;
        }

        private Vector3 Vertex(int index) => new Vector3(_tile.data.verts[index * 3], _tile.data.verts[index * 3 + 1], _tile.data.verts[index * 3 + 2]);
    }
}
#endif
