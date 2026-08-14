// NanoRT.NET - a C# port of NanoRT (https://github.com/lighttransport/nanort).
// Copyright (c) 2015-2018 Light Transport Entertainment, Inc. MIT licensed - see LICENSE.txt.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace NanoRT
{
    /// <summary>Tunables for <see cref="BVHAccel.Build"/>.</summary>
    public sealed class BVHBuildOptions
    {
        /// <summary>Cost of descending into a node, relative to a triangle test, for the SAH.</summary>
        public float Cost = 1.0f;

        /// <summary>Leaves are not split below this primitive count.</summary>
        public int MinLeafPrimitives = 4;

        /// <summary>Number of buckets evaluated per axis when searching for a split.</summary>
        public int BinSize = 64;

        /// <summary>Below this primitive count a node is split at the median rather than by SAH.</summary>
        public int ShallowDepth = 3;

        /// <summary>Hard cap on tree depth; deeper nodes become leaves regardless of size.</summary>
        public int MaxTreeDepth = 256;
    }

    /// <summary>Statistics from the last <see cref="BVHAccel.Build"/> call.</summary>
    public sealed class BVHBuildStatistics
    {
        public int MaxTreeDepth;
        public int NumLeafNodes;
        public int NumBranchNodes;
    }

    /// <summary>A ray with a parametric range. Direction need not be normalised.</summary>
    public struct Ray
    {
        public Vector3 Origin;
        public Vector3 Direction;
        public float MinT;
        public float MaxT;

        public Ray(Vector3 origin, Vector3 direction, float minT = 0.0f, float maxT = float.MaxValue)
        {
            Origin = origin;
            Direction = direction;
            MinT = minT;
            MaxT = maxT;
        }
    }

    /// <summary>Closest-hit result from <see cref="BVHAccel.Traverse"/>.</summary>
    public struct Hit
    {
        /// <summary>Distance along the ray direction (in direction-length units).</summary>
        public float T;

        /// <summary>Index of the triangle that was hit.</summary>
        public int PrimId;

        /// <summary>Barycentric coordinates of the hit against the triangle's second and third vertex.</summary>
        public float U;
        public float V;
    }

    /// <summary>
    /// Binned-SAH bounding volume hierarchy over an indexed triangle soup.
    /// Build once, then <see cref="Traverse"/> / <see cref="Occluded"/> from any number of threads.
    /// </summary>
    public sealed class BVHAccel
    {
        private struct Node
        {
            public Vector3 Min;
            public Vector3 Max;
            // Leaf: PrimCount > 0, Data0 = first index into _indices.
            // Branch: PrimCount == 0, Data0 = right child (left child is always this + 1), Data1 = split axis.
            public int Data0;
            public int Data1;
            public int PrimCount;
        }

        private Node[] _nodes = Array.Empty<Node>();
        private int _nodeCount;
        private int[] _indices = Array.Empty<int>();

        private float[] _vertices = Array.Empty<float>();
        private int[] _faces = Array.Empty<int>();

        private Vector3[] _centroids = Array.Empty<Vector3>();
        private Vector3[] _triMin = Array.Empty<Vector3>();
        private Vector3[] _triMax = Array.Empty<Vector3>();

        public BVHBuildStatistics Statistics { get; private set; } = new BVHBuildStatistics();
        public int TriangleCount => _faces.Length / 3;
        public bool IsBuilt => _nodeCount > 0;

        public Vector3 BoundsMin => _nodeCount > 0 ? _nodes[0].Min : Vector3.Zero;
        public Vector3 BoundsMax => _nodeCount > 0 ? _nodes[0].Max : Vector3.Zero;

        /// <summary>
        /// Build the hierarchy. <paramref name="vertices"/> is xyz-interleaved; <paramref name="faces"/>
        /// holds three vertex indices per triangle. Both arrays are retained by reference.
        /// </summary>
        public void Build(float[] vertices, int[] faces, BVHBuildOptions options = null)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (faces == null) throw new ArgumentNullException(nameof(faces));
            if (faces.Length % 3 != 0) throw new ArgumentException("Face index count must be a multiple of 3.", nameof(faces));

            options ??= new BVHBuildOptions();

            _vertices = vertices;
            _faces = faces;

            int triCount = faces.Length / 3;
            _indices = new int[triCount];
            _centroids = new Vector3[triCount];
            _triMin = new Vector3[triCount];
            _triMax = new Vector3[triCount];

            for (int i = 0; i < triCount; i++)
            {
                _indices[i] = i;
                Vector3 a = Vertex(faces[i * 3 + 0]);
                Vector3 b = Vertex(faces[i * 3 + 1]);
                Vector3 c = Vertex(faces[i * 3 + 2]);
                _triMin[i] = Vector3.Min(a, Vector3.Min(b, c));
                _triMax[i] = Vector3.Max(a, Vector3.Max(b, c));
                _centroids[i] = (a + b + c) * (1.0f / 3.0f);
            }

            // A binary tree over n leaves needs at most 2n-1 nodes; leaves hold >= 1 primitive.
            _nodes = new Node[Math.Max(1, triCount * 2)];
            _nodeCount = 0;
            Statistics = new BVHBuildStatistics();

            if (triCount == 0)
            {
                _nodes[0] = new Node { Min = Vector3.Zero, Max = Vector3.Zero, Data0 = 0, PrimCount = 0 };
                _nodeCount = 1;
                return;
            }

            BuildRecursive(0, triCount, 0, options);

            // Scratch only needed during the build.
            _centroids = Array.Empty<Vector3>();
            _triMin = Array.Empty<Vector3>();
            _triMax = Array.Empty<Vector3>();
        }

        private int BuildRecursive(int first, int count, int depth, BVHBuildOptions options)
        {
            int nodeIndex = _nodeCount++;
            if (_nodeCount > _nodes.Length)
                Array.Resize(ref _nodes, _nodes.Length * 2);

            ComputeBounds(first, count, out Vector3 bmin, out Vector3 bmax);

            if (depth > Statistics.MaxTreeDepth)
                Statistics.MaxTreeDepth = depth;

            if (count <= options.MinLeafPrimitives || depth >= options.MaxTreeDepth)
            {
                _nodes[nodeIndex] = new Node { Min = bmin, Max = bmax, Data0 = first, Data1 = 0, PrimCount = count };
                Statistics.NumLeafNodes++;
                return nodeIndex;
            }

            int axis, mid;
            if (count <= options.ShallowDepth * options.MinLeafPrimitives)
            {
                axis = LongestAxis(bmax - bmin);
                mid = first + count / 2;
                PartitionByMedian(first, count, axis, mid);
            }
            else if (!FindSahSplit(first, count, bmin, bmax, options, out axis, out mid))
            {
                axis = LongestAxis(bmax - bmin);
                mid = first + count / 2;
                PartitionByMedian(first, count, axis, mid);
            }

            if (mid <= first || mid >= first + count)
            {
                axis = LongestAxis(bmax - bmin);
                mid = first + count / 2;
                PartitionByMedian(first, count, axis, mid);
            }

            Statistics.NumBranchNodes++;

            // Left child is emitted immediately after the parent so traversal only stores the right index.
            BuildRecursive(first, mid - first, depth + 1, options);
            int right = BuildRecursive(mid, first + count - mid, depth + 1, options);

            _nodes[nodeIndex] = new Node { Min = bmin, Max = bmax, Data0 = right, Data1 = axis, PrimCount = 0 };
            return nodeIndex;
        }

        private bool FindSahSplit(int first, int count, Vector3 bmin, Vector3 bmax, BVHBuildOptions options, out int bestAxis, out int bestMid)
        {
            bestAxis = -1;
            bestMid = -1;

            // Bin over the centroid bounds; the node bounds can be much larger than the centroid spread.
            Vector3 cmin = new Vector3(float.MaxValue);
            Vector3 cmax = new Vector3(float.MinValue);
            for (int i = first; i < first + count; i++)
            {
                Vector3 c = _centroids[_indices[i]];
                cmin = Vector3.Min(cmin, c);
                cmax = Vector3.Max(cmax, c);
            }

            Vector3 extent = cmax - cmin;
            if (extent.X <= 0 && extent.Y <= 0 && extent.Z <= 0)
                return false;

            int bins = Math.Max(2, options.BinSize);
            float parentArea = SurfaceArea(bmin, bmax);
            if (parentArea <= 0)
                return false;

            float bestCost = count * 1.0f; // Cost of leaving this node as a leaf.
            float bestSplitPos = 0;
            var binMin = new Vector3[bins];
            var binMax = new Vector3[bins];
            var binCount = new int[bins];

            for (int axis = 0; axis < 3; axis++)
            {
                float lo = Component(cmin, axis);
                float span = Component(extent, axis);
                if (span <= 0)
                    continue;

                float scale = bins / span;
                for (int b = 0; b < bins; b++)
                {
                    binMin[b] = new Vector3(float.MaxValue);
                    binMax[b] = new Vector3(float.MinValue);
                    binCount[b] = 0;
                }

                for (int i = first; i < first + count; i++)
                {
                    int tri = _indices[i];
                    int b = (int)((Component(_centroids[tri], axis) - lo) * scale);
                    if (b < 0) b = 0;
                    if (b >= bins) b = bins - 1;
                    binCount[b]++;
                    binMin[b] = Vector3.Min(binMin[b], _triMin[tri]);
                    binMax[b] = Vector3.Max(binMax[b], _triMax[tri]);
                }

                // Sweep right-to-left accumulating the suffix bounds, then left-to-right for the prefix.
                var suffixArea = new float[bins];
                var suffixCount = new int[bins];
                Vector3 accMin = new Vector3(float.MaxValue), accMax = new Vector3(float.MinValue);
                int acc = 0;
                for (int b = bins - 1; b >= 1; b--)
                {
                    if (binCount[b] > 0)
                    {
                        accMin = Vector3.Min(accMin, binMin[b]);
                        accMax = Vector3.Max(accMax, binMax[b]);
                        acc += binCount[b];
                    }
                    suffixArea[b] = acc > 0 ? SurfaceArea(accMin, accMax) : 0;
                    suffixCount[b] = acc;
                }

                accMin = new Vector3(float.MaxValue);
                accMax = new Vector3(float.MinValue);
                acc = 0;
                for (int b = 0; b < bins - 1; b++)
                {
                    if (binCount[b] > 0)
                    {
                        accMin = Vector3.Min(accMin, binMin[b]);
                        accMax = Vector3.Max(accMax, binMax[b]);
                        acc += binCount[b];
                    }
                    if (acc == 0 || suffixCount[b + 1] == 0)
                        continue;

                    float cost = options.Cost +
                                 (SurfaceArea(accMin, accMax) * acc + suffixArea[b + 1] * suffixCount[b + 1]) / parentArea;
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestSplitPos = lo + (b + 1) / scale;
                    }
                }
            }

            if (bestAxis < 0)
                return false;

            bestMid = PartitionByPosition(first, count, bestAxis, bestSplitPos);
            return bestMid > first && bestMid < first + count;
        }

        private int PartitionByPosition(int first, int count, int axis, float position)
        {
            int lo = first, hi = first + count - 1;
            while (lo <= hi)
            {
                if (Component(_centroids[_indices[lo]], axis) < position)
                {
                    lo++;
                }
                else
                {
                    (_indices[lo], _indices[hi]) = (_indices[hi], _indices[lo]);
                    hi--;
                }
            }
            return lo;
        }

        private void PartitionByMedian(int first, int count, int axis, int mid)
        {
            // Partial selection: only the element at `mid` and the partition around it matter.
            int lo = first, hi = first + count - 1;
            while (lo < hi)
            {
                float pivot = Component(_centroids[_indices[(lo + hi) / 2]], axis);
                int i = lo, j = hi;
                while (i <= j)
                {
                    while (Component(_centroids[_indices[i]], axis) < pivot) i++;
                    while (Component(_centroids[_indices[j]], axis) > pivot) j--;
                    if (i <= j)
                    {
                        (_indices[i], _indices[j]) = (_indices[j], _indices[i]);
                        i++;
                        j--;
                    }
                }
                if (mid <= j) hi = j;
                else if (mid >= i) lo = i;
                else break;
            }
        }

        private void ComputeBounds(int first, int count, out Vector3 bmin, out Vector3 bmax)
        {
            bmin = new Vector3(float.MaxValue);
            bmax = new Vector3(float.MinValue);
            for (int i = first; i < first + count; i++)
            {
                int tri = _indices[i];
                bmin = Vector3.Min(bmin, _triMin[tri]);
                bmax = Vector3.Max(bmax, _triMax[tri]);
            }
        }

        /// <summary>Closest-hit query. Returns false when the ray misses everything.</summary>
        public bool Traverse(ref Ray ray, out Hit hit)
        {
            hit = default;
            hit.T = ray.MaxT;
            hit.PrimId = -1;
            if (_nodeCount == 0)
                return false;

            Vector3 invDir = new Vector3(
                1.0f / (ray.Direction.X == 0 ? 1e-30f : ray.Direction.X),
                1.0f / (ray.Direction.Y == 0 ? 1e-30f : ray.Direction.Y),
                1.0f / (ray.Direction.Z == 0 ? 1e-30f : ray.Direction.Z));

            Span<int> stack = stackalloc int[64];
            int sp = 0;
            stack[sp++] = 0;

            while (sp > 0)
            {
                int nodeIndex = stack[--sp];
                ref Node node = ref _nodes[nodeIndex];

                if (!IntersectAabb(node.Min, node.Max, ray.Origin, invDir, ray.MinT, hit.T))
                    continue;

                if (node.PrimCount > 0)
                {
                    for (int i = 0; i < node.PrimCount; i++)
                    {
                        int tri = _indices[node.Data0 + i];
                        if (IntersectTriangle(tri, ref ray, hit.T, out float t, out float u, out float v))
                        {
                            hit.T = t;
                            hit.U = u;
                            hit.V = v;
                            hit.PrimId = tri;
                        }
                    }
                    continue;
                }

                if (sp + 2 > stack.Length)
                    continue; // Depth cap reached; a dropped subtree can only cost us a hit, never correctness of a miss.

                // Visit the near child first so the far one is more likely to be culled by hit.T.
                bool nearIsLeft = Component(ray.Direction, node.Data1) >= 0;
                int left = nodeIndex + 1;
                int right = node.Data0;
                if (nearIsLeft)
                {
                    stack[sp++] = right;
                    stack[sp++] = left;
                }
                else
                {
                    stack[sp++] = left;
                    stack[sp++] = right;
                }
            }

            return hit.PrimId >= 0;
        }

        /// <summary>
        /// Any-hit query. Cheaper than <see cref="Traverse"/> because it stops at the first
        /// intersection inside the ray's range.
        /// </summary>
        public bool Occluded(ref Ray ray)
        {
            if (_nodeCount == 0)
                return false;

            Vector3 invDir = new Vector3(
                1.0f / (ray.Direction.X == 0 ? 1e-30f : ray.Direction.X),
                1.0f / (ray.Direction.Y == 0 ? 1e-30f : ray.Direction.Y),
                1.0f / (ray.Direction.Z == 0 ? 1e-30f : ray.Direction.Z));

            Span<int> stack = stackalloc int[64];
            int sp = 0;
            stack[sp++] = 0;

            while (sp > 0)
            {
                int nodeIndex = stack[--sp];
                ref Node node = ref _nodes[nodeIndex];

                if (!IntersectAabb(node.Min, node.Max, ray.Origin, invDir, ray.MinT, ray.MaxT))
                    continue;

                if (node.PrimCount > 0)
                {
                    for (int i = 0; i < node.PrimCount; i++)
                    {
                        if (IntersectTriangle(_indices[node.Data0 + i], ref ray, ray.MaxT, out _, out _, out _))
                            return true;
                    }
                    continue;
                }

                if (sp + 2 > stack.Length)
                    continue;

                stack[sp++] = node.Data0;
                stack[sp++] = nodeIndex + 1;
            }

            return false;
        }

        /// <summary>Geometric normal of a triangle, in the winding order of the source mesh.</summary>
        public Vector3 TriangleNormal(int primId)
        {
            Vector3 a = Vertex(_faces[primId * 3 + 0]);
            Vector3 b = Vertex(_faces[primId * 3 + 1]);
            Vector3 c = Vertex(_faces[primId * 3 + 2]);
            Vector3 n = Vector3.Cross(b - a, c - a);
            float len = n.Length();
            return len > 0 ? n / len : Vector3.UnitY;
        }

        private bool IntersectTriangle(int tri, ref Ray ray, float tMax, out float t, out float u, out float v)
        {
            // Möller–Trumbore.
            const float epsilon = 1e-9f;
            t = 0; u = 0; v = 0;

            Vector3 p0 = Vertex(_faces[tri * 3 + 0]);
            Vector3 p1 = Vertex(_faces[tri * 3 + 1]);
            Vector3 p2 = Vertex(_faces[tri * 3 + 2]);

            Vector3 e1 = p1 - p0;
            Vector3 e2 = p2 - p0;

            Vector3 pv = Vector3.Cross(ray.Direction, e2);
            float det = Vector3.Dot(e1, pv);
            if (det > -epsilon && det < epsilon)
                return false;

            float invDet = 1.0f / det;
            Vector3 tv = ray.Origin - p0;
            u = Vector3.Dot(tv, pv) * invDet;
            if (u < 0.0f || u > 1.0f)
                return false;

            Vector3 qv = Vector3.Cross(tv, e1);
            v = Vector3.Dot(ray.Direction, qv) * invDet;
            if (v < 0.0f || u + v > 1.0f)
                return false;

            t = Vector3.Dot(e2, qv) * invDet;
            return t >= ray.MinT && t <= tMax;
        }

        private static bool IntersectAabb(Vector3 bmin, Vector3 bmax, Vector3 origin, Vector3 invDir, float tMin, float tMax)
        {
            float t0 = (bmin.X - origin.X) * invDir.X;
            float t1 = (bmax.X - origin.X) * invDir.X;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tMin = t0 > tMin ? t0 : tMin;
            tMax = t1 < tMax ? t1 : tMax;
            if (tMin > tMax) return false;

            t0 = (bmin.Y - origin.Y) * invDir.Y;
            t1 = (bmax.Y - origin.Y) * invDir.Y;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tMin = t0 > tMin ? t0 : tMin;
            tMax = t1 < tMax ? t1 : tMax;
            if (tMin > tMax) return false;

            t0 = (bmin.Z - origin.Z) * invDir.Z;
            t1 = (bmax.Z - origin.Z) * invDir.Z;
            if (t0 > t1) (t0, t1) = (t1, t0);
            tMin = t0 > tMin ? t0 : tMin;
            tMax = t1 < tMax ? t1 : tMax;
            return tMin <= tMax;
        }

        private Vector3 Vertex(int index) => new Vector3(_vertices[index * 3], _vertices[index * 3 + 1], _vertices[index * 3 + 2]);

        private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        private static int LongestAxis(Vector3 extent)
        {
            if (extent.X >= extent.Y && extent.X >= extent.Z) return 0;
            return extent.Y >= extent.Z ? 1 : 2;
        }

        private static float SurfaceArea(Vector3 bmin, Vector3 bmax)
        {
            Vector3 d = bmax - bmin;
            if (d.X < 0 || d.Y < 0 || d.Z < 0) return 0;
            return 2.0f * (d.X * d.Y + d.Y * d.Z + d.Z * d.X);
        }
    }
}
