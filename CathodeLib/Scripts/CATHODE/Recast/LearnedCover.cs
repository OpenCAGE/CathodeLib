using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Reflection;
using NanoRT;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// The raw geometric description of a rim station that a learned cover selector reads: ray
    /// distances outward at seven heights and five bearings, inward at two heights and five
    /// bearings, along the obstacle face both ways at two heights, the obstacle top and thickness
    /// at thirteen offsets along the rim (a 3 m window at 0.25 m), the floor drop beyond the
    /// obstacle at three distances, and the run length, distance to the run end and edge length.
    /// The training tables (<c>diag coverml build</c>) and the baker call this same method, so a
    /// model trained on retail's navmesh reads identical numbers off ours.
    /// </summary>
    public static class LearnedCoverFeatures
    {
        public static readonly float[] OutHeights = { 0.2f, 0.5f, 0.8f, 1.1f, 1.4f, 1.7f, 2.0f };
        public static readonly float[] Bearings = { -60f, -30f, 0f, 30f, 60f };
        public static readonly float[] InHeights = { 0.5f, 1.2f };
        public static readonly float[] AlongHeights = { 0.5f, 1.2f };
        public static readonly float[] AlongOffsets = { -1.5f, -1.25f, -1.0f, -0.75f, -0.5f, -0.25f, 0f, 0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f };
        public static readonly float[] DropDistances = { 0.75f, 1.5f, 3.0f };
        public const float RayMax = 6f;

        public static List<string> Names()
        {
            var n = new List<string>();
            foreach (float h in OutHeights) foreach (float b in Bearings) n.Add(string.Format(CultureInfo.InvariantCulture, "out_h{0:0.0}_b{1:+0;-0}", h, b));
            foreach (float h in InHeights) foreach (float b in Bearings) n.Add(string.Format(CultureInfo.InvariantCulture, "in_h{0:0.0}_b{1:+0;-0}", h, b));
            foreach (float h in AlongHeights) { n.Add(string.Format(CultureInfo.InvariantCulture, "alongL_h{0:0.0}", h)); n.Add(string.Format(CultureInfo.InvariantCulture, "alongR_h{0:0.0}", h)); }
            foreach (float o in AlongOffsets) n.Add(string.Format(CultureInfo.InvariantCulture, "top_{0:+0.00;-0.00}", o));
            foreach (float o in AlongOffsets) n.Add(string.Format(CultureInfo.InvariantCulture, "thick_{0:+0.00;-0.00}", o));
            foreach (float d in DropDistances) n.Add(string.Format(CultureInfo.InvariantCulture, "dropBeyond_{0:0.00}", d));
            n.Add("runLen"); n.Add("distEnd"); n.Add("edgeLen");
            return n;
        }

        public static int Count => OutHeights.Length * Bearings.Length + InHeights.Length * Bearings.Length + AlongHeights.Length * 2 + AlongOffsets.Length * 2 + DropDistances.Length + 3;

        /// <summary>
        /// Describe the rim station at <paramref name="p"/> (a point ON the rim, floor height), with
        /// <paramref name="inward"/> the unit XZ normal toward the walkable side.
        /// </summary>
        public static float[] Describe(Vector3 p, Vector3 inward, float edgeLen, float runLen, float posAlong,
                                       BVHAccel bvh, RimCoverGenerator.DepthProbe probe, CoverBakeSettings cs)
        {
            var f = new float[Count];
            int k = 0;
            Vector3 outward = -inward;
            foreach (float h in OutHeights) foreach (float b in Bearings) f[k++] = Cast(bvh, p, h, Rot(outward, b));
            foreach (float h in InHeights) foreach (float b in Bearings) f[k++] = Cast(bvh, p, h, Rot(inward, b));
            var alongDir = new Vector3(outward.Z, 0f, -outward.X);
            foreach (float h in AlongHeights)
            {
                Vector3 q = p + outward * 0.15f;
                f[k++] = Cast(bvh, q, h, -alongDir);
                f[k++] = Cast(bvh, q, h, alongDir);
            }
            foreach (float o in AlongOffsets) f[k++] = probe.TopAlong(p + alongDir * o, outward, p.Y, cs);
            foreach (float o in AlongOffsets) f[k++] = Math.Min(6f, probe.Thickness(p + alongDir * o, outward, p.Y, cs));
            foreach (float d in DropDistances) f[k++] = Drop(bvh, p + outward * d, p.Y);
            f[k++] = runLen;
            f[k++] = Math.Min(posAlong, runLen - posAlong);
            f[k++] = edgeLen;
            return f;
        }

        static Vector3 Rot(Vector3 d, float deg)
        {
            double a = deg * Math.PI / 180.0;
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            return Vector3.Normalize(new Vector3(d.X * c - d.Z * s, 0f, d.X * s + d.Z * c));
        }

        static float Cast(BVHAccel bvh, Vector3 p, float h, Vector3 dir)
        {
            if (bvh == null) return RayMax;
            var ray = new Ray(new Vector3(p.X, p.Y + h, p.Z), dir, 0.02f, RayMax);
            return bvh.Traverse(ref ray, out Hit hit) ? hit.T : RayMax;
        }

        /// <summary>How far the floor lies below the rim height at a point beyond the obstacle: 0 = same floor, 5 = nothing within 5 m down, negative = solid above.</summary>
        static float Drop(BVHAccel bvh, Vector3 q, float rimY)
        {
            if (bvh == null) return 5f;
            var ray = new Ray(new Vector3(q.X, rimY + 1.5f, q.Z), new Vector3(0f, -1f, 0f), 0.01f, 6.5f);
            if (!bvh.Traverse(ref ray, out Hit hit)) return 5f;
            float y = rimY + 1.5f - hit.T;
            return Math.Max(-1.5f, Math.Min(5f, rimY - y));
        }
    }

    /// <summary>
    /// A gradient-boosted tree ensemble (binary logistic) in the plain text format the diag
    /// trainer writes. Binned features: each feature has ascending bin edges and a node tests
    /// <c>bin(x) &lt;= node.Bin</c>.
    /// </summary>
    public sealed class CoverGbdtModel
    {
        struct Node { public int Feature, Bin, Left, Right; public float Value; }

        float _bias, _shrink;
        float[][] _edges;
        readonly List<Node[]> _trees = new List<Node[]>();
        public float Threshold = 0.33f;
        public int FeatureCount => _edges?.Length ?? 0;

        public static CoverGbdtModel Load(string path)
        {
            using (var r = new StreamReader(path)) return Load(r);
        }

        /// <summary>
        /// Load from any stream: gzip-wrapped or raw binary (magic "CGBM") or the trainer's text.
        /// The binary form is what CathodeLib embeds - a third the size of the text.
        /// </summary>
        public static CoverGbdtModel Load(Stream s)
        {
            var head = new byte[4];
            int got = 0;
            while (got < 4) { int r = s.Read(head, got, 4 - got); if (r <= 0) break; got += r; }
            var rest = new MemoryStream();
            rest.Write(head, 0, got);
            s.CopyTo(rest);
            rest.Position = 0;
            if (got >= 2 && head[0] == 0x1f && head[1] == 0x8b)
                using (var gz = new GZipStream(rest, CompressionMode.Decompress)) return LoadBinary(gz);
            if (got >= 4 && head[0] == (byte)'C' && head[1] == (byte)'G' && head[2] == (byte)'B' && head[3] == (byte)'M')
                return LoadBinary(rest);
            using (var reader = new StreamReader(rest)) return Load(reader);
        }

        const uint BinaryMagic = 0x4D424743; // "CGBM" little-endian
        const byte BinaryVersion = 1;
        const byte LeafFeature = 0xFF;

        /// <summary>
        /// Binary layout, little-endian: "CGBM", version byte, bias / shrink / threshold floats,
        /// ushort feature count then per feature a ushort edge count and its floats, ushort tree
        /// count then per tree a ushort node count and its nodes - a feature byte (0xFF = leaf,
        /// followed by the float value) or the split (bin byte, ushort left, ushort right).
        /// </summary>
        public static CoverGbdtModel LoadBinary(Stream s)
        {
            var m = new CoverGbdtModel();
            using (var r = new BinaryReader(s))
            {
                if (r.ReadUInt32() != BinaryMagic) throw new InvalidDataException("not a CGBM model");
                byte version = r.ReadByte();
                if (version != BinaryVersion) throw new InvalidDataException("CGBM version " + version + " unsupported");
                m._bias = r.ReadSingle(); m._shrink = r.ReadSingle(); m.Threshold = r.ReadSingle();
                int features = r.ReadUInt16();
                m._edges = new float[features][];
                for (int f = 0; f < features; f++)
                {
                    int n = r.ReadUInt16();
                    var e = new float[n];
                    for (int i = 0; i < n; i++) e[i] = r.ReadSingle();
                    m._edges[f] = e;
                }
                int trees = r.ReadUInt16();
                for (int t = 0; t < trees; t++)
                {
                    int n = r.ReadUInt16();
                    var nodes = new Node[n];
                    for (int i = 0; i < n; i++)
                    {
                        byte feature = r.ReadByte();
                        if (feature == LeafFeature) nodes[i] = new Node { Feature = -1, Bin = 0, Left = -1, Right = -1, Value = r.ReadSingle() };
                        else nodes[i] = new Node { Feature = feature, Bin = r.ReadByte(), Left = r.ReadUInt16(), Right = r.ReadUInt16(), Value = 0f };
                    }
                    m._trees.Add(nodes);
                }
            }
            return m;
        }

        /// <summary>Write the binary form (see <see cref="LoadBinary"/>), gzip-wrapped when asked.</summary>
        public void SaveBinary(Stream s, bool gzip = true)
        {
            Stream target = gzip ? new GZipStream(s, CompressionLevel.Optimal, leaveOpen: true) : s;
            using (var w = new BinaryWriter(target, System.Text.Encoding.UTF8, leaveOpen: !gzip))
            {
                w.Write(BinaryMagic); w.Write(BinaryVersion);
                w.Write(_bias); w.Write(_shrink); w.Write(Threshold);
                w.Write((ushort)_edges.Length);
                foreach (float[] e in _edges) { w.Write((ushort)e.Length); foreach (float v in e) w.Write(v); }
                w.Write((ushort)_trees.Count);
                foreach (Node[] tree in _trees)
                {
                    w.Write((ushort)tree.Length);
                    foreach (Node nd in tree)
                    {
                        if (nd.Feature < 0) { w.Write(LeafFeature); w.Write(nd.Value); }
                        else
                        {
                            if (nd.Feature >= LeafFeature || nd.Bin > 255 || nd.Left > ushort.MaxValue || nd.Right > ushort.MaxValue)
                                throw new InvalidDataException("model does not fit the CGBM field widths");
                            w.Write((byte)nd.Feature); w.Write((byte)nd.Bin); w.Write((ushort)nd.Left); w.Write((ushort)nd.Right);
                        }
                    }
                }
            }
            if (gzip) target.Dispose();
        }

        /// <summary>Parse a model from any text source - a file, or an embedded resource stream.</summary>
        public static CoverGbdtModel Load(TextReader r)
        {
            var m = new CoverGbdtModel();
            {
                string line;
                var inv = CultureInfo.InvariantCulture;
                string[] Next() { line = r.ReadLine(); return line == null ? null : line.Split(' '); }
                string[] t;
                while ((t = Next()) != null)
                {
                    if (t.Length == 0 || t[0].Length == 0) continue;
                    switch (t[0])
                    {
                        case "bias": m._bias = float.Parse(t[1], inv); break;
                        case "shrink": m._shrink = float.Parse(t[1], inv); break;
                        case "threshold": m.Threshold = float.Parse(t[1], inv); break;
                        case "features": m._edges = new float[int.Parse(t[1], inv)][]; break;
                        case "bins":
                        {
                            int f = int.Parse(t[1], inv), n = int.Parse(t[2], inv);
                            var e = new float[n];
                            for (int i = 0; i < n; i++) e[i] = float.Parse(t[3 + i], inv);
                            m._edges[f] = e;
                            break;
                        }
                        case "tree":
                        {
                            int n = int.Parse(t[1], inv);
                            var nodes = new Node[n];
                            for (int i = 0; i < n; i++)
                            {
                                string[] u = Next();
                                nodes[i] = new Node
                                {
                                    Feature = int.Parse(u[1], inv), Bin = int.Parse(u[2], inv),
                                    Value = float.Parse(u[3], inv), Left = int.Parse(u[4], inv), Right = int.Parse(u[5], inv)
                                };
                            }
                            m._trees.Add(nodes);
                            break;
                        }
                    }
                }
            }
            return m;
        }

        int BinOf(int f, float v)
        {
            float[] e = _edges[f];
            int lo = 0, hi = e.Length;
            while (lo < hi) { int mid = (lo + hi) >> 1; if (v <= e[mid]) hi = mid; else lo = mid + 1; }
            return lo;
        }

        public float Predict(float[] x)
        {
            int d = _edges.Length;
            var xb = new int[d];
            for (int f = 0; f < d; f++) xb[f] = BinOf(f, x[f]);
            float s = _bias;
            foreach (Node[] tree in _trees)
            {
                int i = 0;
                while (tree[i].Feature >= 0) i = xb[tree[i].Feature] <= tree[i].Bin ? tree[i].Left : tree[i].Right;
                s += _shrink * tree[i].Value;
            }
            return 1f / (1f + (float)Math.Exp(-s));
        }
    }

    /// <summary>Loads the learned selector named by the settings, or the one CathodeLib embeds, once per path.</summary>
    public static class LearnedCover
    {
        static readonly Dictionary<string, CoverGbdtModel> _cache = new Dictionary<string, CoverGbdtModel>();

        /// <summary>Logical names of the selectors CathodeLib ships inside itself (see the csproj).</summary>
        public const string EmbeddedCover = "CathodeLib.Learned.cover";
        public const string EmbeddedSpot = "CathodeLib.Learned.spot";
        public const string EmbeddedAssault = "CathodeLib.Learned.assault";

        public static CoverGbdtModel TryLoad(CoverBakeSettings settings, Action<string> log = null)
            => TryLoadPath(settings.LearnedSelectorPath, "Cover", log,
                           settings.UseEmbeddedLearnedSelector ? EmbeddedCover : null);

        /// <summary>
        /// The selector CathodeLib ships as an embedded resource (the all-twelve-level models of
        /// 2 Sep 2026 - `results/models/*_all12.txt` in the parity harness). Cached per name; null
        /// when the resource is missing or malformed, in which case the caller falls back to rules.
        /// </summary>
        public static CoverGbdtModel TryLoadEmbedded(string name, string what, Action<string> log = null)
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(name, out CoverGbdtModel cached)) return cached;
                CoverGbdtModel model = null;
                try
                {
                    using (Stream s = typeof(CoverGbdtModel).Assembly.GetManifestResourceStream(name))
                    {
                        if (s == null) log?.Invoke(what + ": no embedded learned selector '" + name + "' - using the rule set.");
                        else model = CoverGbdtModel.Load(s);
                    }
                    if (model != null && model.FeatureCount != LearnedCoverFeatures.Count)
                    {
                        log?.Invoke(what + ": embedded learned selector has " + model.FeatureCount + " features, expected " + LearnedCoverFeatures.Count + " - ignored.");
                        model = null;
                    }
                    else if (model != null)
                        log?.Invoke(what + ": learned selector (embedded " + name + ", threshold " + model.Threshold.ToString("0.00") + ")");
                }
                catch (Exception e)
                {
                    log?.Invoke(what + ": embedded learned selector failed to load: " + e.Message);
                    model = null;
                }
                _cache[name] = model;
                return model;
            }
        }

        /// <summary>Load the model at <paramref name="path"/>, or the embedded selector when that names no file; cached per path; null when nothing names a readable, well-formed model.</summary>
        public static CoverGbdtModel TryLoadPath(string path, string what, Action<string> log = null, string embedded = null)
        {
            // "none" is the explicit off switch: no file, no embedded fallback.
            if (string.Equals(path, "none", StringComparison.OrdinalIgnoreCase)) return null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return embedded != null ? TryLoadEmbedded(embedded, what, log) : null;
            lock (_cache)
            {
                if (_cache.TryGetValue(path, out CoverGbdtModel cached)) return cached;
                CoverGbdtModel model = null;
                try
                {
                    using (FileStream fs = File.OpenRead(path)) model = CoverGbdtModel.Load(fs);
                    if (model.FeatureCount != LearnedCoverFeatures.Count)
                    {
                        log?.Invoke(what + ": learned selector at " + path + " has " + model.FeatureCount + " features, expected " + LearnedCoverFeatures.Count + " - ignored.");
                        model = null;
                    }
                    else log?.Invoke(what + ": learned selector " + path + " (threshold " + model.Threshold.ToString("0.00") + ")");
                }
                catch (Exception e)
                {
                    log?.Invoke(what + ": learned selector failed to load: " + e.Message);
                    model = null;
                }
                _cache[path] = model;
                return model;
            }
        }
    }
}
