#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CATHODE;
using CATHODE.Scripting;
using CATHODE.Scripting.Internal;
using CATHODE.ShaderTypes;

namespace CathodeLib.Alphalight
{
    /// <summary>
    /// Rebuilds DATA/ENV/x/WORLD/ALPHALIGHT_LEVEL.BIN and the <c>alpha_light_*</c> parameters that
    /// go with it on every ModelReference that samples the atlas.
    /// </summary>
    /// <remarks>
    /// <para>Despite the name, the file holds no lighting. It is a <b>sample position atlas</b>: an
    /// A16B16G16R16F image whose RGB is an <i>object-space position</i> on the alpha-lit surface and
    /// whose A is a per-model scalar. Lighting is a runtime job - CA_ALPHALIGHT_POSITION transforms
    /// these local positions per instance, CA_ALPHALIGHT_CLEAR / CA_ALPHALIGHT_LIGHT accumulate the
    /// live lights at them, and the alpha surface reads the result back through the transform in its
    /// <c>alpha_light_offset_*</c> / <c>alpha_light_scale_*</c> parameters. So nothing here needs to
    /// know where the lights are.</para>
    ///
    /// <para>How the atlas is laid out, verified against retail BSP_TORRENS:</para>
    /// <list type="bullet">
    /// <item>Each participating ModelReference owns a <b>box</b> of <c>(W+1) x (H+1)</c> texels.
    /// <c>W x H</c> of those are probes; the extra row and column are a dilation border on the
    /// <i>left and top</i>. Boxes never overlap and every live texel is inside one.</item>
    /// <item>The parameters point at the first <i>probe</i>, not at the box:
    /// <c>offset = (probeX / resolution, probeY / resolution)</c> and
    /// <c>scale = (W / resolution, H / resolution)</c>. Since a shader maps surface UV 0..1 onto
    /// <c>probeX - 0.5 .. probeX + W - 1</c> in texel space, the border texel at <c>probeX - 1</c> is
    /// what makes the bilinear tap at UV 0 land on probe 0 cleanly.</item>
    /// <item>The border column repeats each row's first probe; the border row is a flat fill of probe
    /// (0,0). Texels no box claimed are <c>(0, 0, 0, -1024)</c>.</item>
    /// </list>
    ///
    /// <para>How a probe grid is built: the mesh is rasterised in its <b>alphalight unwrap</b> -
    /// TexCoord3 on most vertex formats, TexCoord2 on the few that stop there - sampling grid
    /// <i>nodes</i> at <c>(i / (W-1), j / (H-1))</c> rather than texel centres, and storing the
    /// interpolated position. Nodes the charts do not cover take the closest point on the mesh in UV
    /// space, which is what reproduces retail's clamped grid edges.
    /// <c>alpha_light_average_normal</c> is the mean of the probes' interpolated normals, left
    /// unnormalised, so its length falls away as the surface curves.</para>
    ///
    /// <para>Two things retail knows that the shipped data does not carry:</para>
    /// <list type="bullet">
    /// <item><b>Grid size.</b> Deterministic, but not reproduced. Over 2004 retail samples, no two
    /// entities whose whole renderable set matches byte for byte were ever given different grids, so
    /// it is a pure function of data we hold - we simply do not have the function. It is not a
    /// world-space texel density, nor a span of any shipped UV channel. The shape is right though:
    /// the reported formula is <c>W = span + 1</c> with those UVs in lightmap texel space, so the
    /// missing piece is the density the radiosity probe pass rasterises at. Until that is reproduced,
    /// <see cref="AlphalightBakeSettings.PreserveExistingResolution"/> reuses what COMMANDS already
    /// records, and only new content falls back to
    /// <see cref="AlphalightBakeSettings.TargetTexelSize"/>.</item>
    /// <item><b>Per-model normal push.</b> About a fifth of retail's models store probes displaced
    /// along the surface normal by an authored distance (0.004 - 0.05 on BSP_TORRENS). There is no
    /// source for it in the level, so probes are left on the surface; those models still land within
    /// 5 cm.</item>
    /// </list>
    /// </remarks>
    public static class AlphalightBaker
    {
        /// <summary>
        /// Sampling channels, in preference order. TexCoord3 is the alphalight unwrap on most
        /// meshes and is what reproduces retail; a minority of vertex formats stop at TexCoord2 and
        /// park it there instead. Channel 0 is always the tiling diffuse UV and never qualifies,
        /// since it runs well outside the unit square.
        /// </summary>
        private static readonly int[] UvChannels = { 3, 2, 1 };

        /// <summary>Marks a texel no box claimed.</summary>
        private static readonly Vector4 Unclaimed = new Vector4(0.0f, 0.0f, 0.0f, -1024.0f);

        /// <summary>
        /// A probe's stored A is its cell's world edge length over this. Fitted to retail: the
        /// ratio holds to 1.0087 - 1.0111 across every model whose mesh area matches its
        /// parameterised area.
        /// </summary>
        private const double ProbeScaleDivisor = 7.9271;

        #region ENTRY POINT

        /// <summary>
        /// Rebuild the level's alphalight atlas in place. Call before <see cref="Level.Save"/>;
        /// nothing is written to disk here.
        /// </summary>
        public static void BakeLevel(Level level, AlphalightBakeSettings settings = null, Action<string> log = null)
        {
            if (level?.AlphaLight == null || level.Commands?.Entries == null)
                return;
            if (settings == null)
                settings = new AlphalightBakeSettings();
            if (log == null)
                log = _ => { };

            List<Sample> samples = Collect(level, settings, log);

            int resolution = settings.PreferredResolution;
            if (samples.Count != 0)
            {
                resolution = Pack(samples, settings);
                if (resolution <= 0)
                {
                    log("Alphalight: " + samples.Count + " samples do not fit in a " + settings.MaxResolution
                        + "px atlas - skipping bake, existing data left alone.");
                    return;
                }
            }
            else
            {
                log("Alphalight: no alpha-lit ModelReferences, clearing atlas.");
            }

            WriteAtlas(level.AlphaLight, samples, resolution);

            if (settings.WriteEntityParameters)
            {
                // Anything that used to sample the atlas and no longer has a box must lose its
                // parameters, or it keeps pointing at texels another model now owns.
                HashSet<FunctionEntity> baked = new HashSet<FunctionEntity>(samples.Select(s => s.Entity));
                foreach (Composite composite in level.Commands.Entries)
                {
                    if (composite?.functions == null)
                        continue;
                    foreach (FunctionEntity function in composite.GetFunctionEntitiesOfType(FunctionType.ModelReference))
                        if (!baked.Contains(function))
                            ClearParameters(function);
                }

                foreach (Sample s in samples)
                    ApplyParameters(s, resolution);
            }

            int used = samples.Sum(s => (s.Width + 1) * (s.Height + 1));
            log("Alphalight: baked " + samples.Count + " samples into a " + resolution + "x" + resolution
                + " atlas (" + (100.0 * used / (resolution * resolution)).ToString("F1") + "% occupied).");
        }

        #endregion

        #region COLLECTION

        /// <summary>One ModelReference's worth of alphalight data.</summary>
        private sealed class Sample
        {
            public FunctionEntity Entity;
            public int Width, Height;        // probe grid, in texels
            public int ProbeX, ProbeY;       // top-left probe; the box starts one texel before
            public Vector3[] Positions;      // Width * Height, row major
            public Vector3 AverageNormal;
            public float ProbeScale;         // what goes in the A channel

            public int BoxWidth => Width + 1;
            public int BoxHeight => Height + 1;
        }

        /// <summary>
        /// Every ModelReference whose renderable asks for alpha lighting and which actually spawns
        /// a mover. Retail additionally drops a handful of qualifying entities (18 of 88 on
        /// BSP_TORRENS) on grounds that are not recorded in the level, so this errs towards
        /// including them: a spare box costs atlas space, a missing one costs the surface its
        /// lighting.
        /// </summary>
        private static List<Sample> Collect(Level level, AlphalightBakeSettings settings, Action<string> log)
        {
            List<Sample> samples = new List<Sample>();
            int skippedNoUv = 0, fallbacks = 0;

            // Existing parameters are fractions of the atlas they were baked against, so the grid
            // sizes only come back out of them against that same edge - which is 64 on some levels
            // and 128 on others.
            int sourceResolution = (int)level.AlphaLight.Resolution.X;

            foreach (Composite composite in level.Commands.Entries)
            {
                if (composite?.functions == null)
                    continue;

                foreach (FunctionEntity function in composite.GetFunctionEntitiesOfType(FunctionType.ModelReference))
                {
                    if (!(function.GetParameter(ShortGuids.resource)?.content is cResource resource) || resource.value.Count == 0)
                        continue;

                    List<RenderableElements.Element> reds = function.GetResource(ResourceType.RENDERABLE_INSTANCE, true)?.RenderableInstance;
                    if (reds == null || reds.Count == 0 || !UsesAlphaLighting(reds[0].Material))
                        continue;

                    cMesh mesh = reds[0].Model?.ToMesh();
                    if (mesh == null || mesh.Vertices.Count == 0 || mesh.Indices.Count < 3)
                        continue;

                    List<Triangle> triangles = BuildTriangles(mesh, out double worldArea, out double uvArea);
                    Sample sample = new Sample { Entity = function };

                    if (triangles.Count != 0)
                    {
                        ResolveGridSize(sample, function, triangles, settings, sourceResolution);
                        Rasterise(sample, triangles, worldArea, uvArea);
                    }
                    else
                    {
                        // No unwrap to rasterise. Retail still bakes a few of these, and dropping
                        // them costs the surface its lighting outright, so fall back to a flat grid
                        // across the mesh's bounds. Entities that never had parameters are left
                        // alone: without an unwrap there is nothing to say they were meant to be lit.
                        skippedNoUv++;
                        if (function.GetParameter(ShortGuids.alpha_light_scale_x) == null
                            || !RasteriseBounds(sample, function, mesh, settings, sourceResolution))
                            continue;
                        fallbacks++;
                    }
                    samples.Add(sample);
                }
            }

            if (skippedNoUv != 0)
                log("Alphalight: " + skippedNoUv + " alpha-lit ModelReferences have no usable unwrap ("
                    + fallbacks + " kept on a bounds grid, " + (skippedNoUv - fallbacks) + " dropped).");

            return samples;
        }

        /// <summary>
        /// Does this material's ubershader have ALPHA_LIGHTING switched on? The feature lives at a
        /// different bit in each ubershader's FEATURES enum, so it is looked up by name.
        /// </summary>
        private static bool UsesAlphaLighting(Materials.Material material)
        {
            if (material?.Shader == null)
                return false;

            int bit = AlphaLightingBit(material.Shader.Ubershader);
            return bit >= 0 && ((material.Shader.UbershaderFeatureFlags >> bit) & 1) != 0;
        }

        private static readonly Dictionary<SHADER_LIST, int> _alphaLightingBits = new Dictionary<SHADER_LIST, int>();

        private static int AlphaLightingBit(SHADER_LIST ubershader)
        {
            lock (_alphaLightingBits)
            {
                if (_alphaLightingBits.TryGetValue(ubershader, out int cached))
                    return cached;

                int bit = -1;
                Type features = Type.GetType("CATHODE.ShaderTypes." + ubershader + "+FEATURES, " + typeof(SHADER_LIST).Assembly.GetName().Name);
                if (features != null && features.IsEnum && Enum.IsDefined(features, "ALPHA_LIGHTING"))
                    bit = Convert.ToInt32(Enum.Parse(features, "ALPHA_LIGHTING"));

                _alphaLightingBits[ubershader] = bit;
                return bit;
            }
        }

        #endregion

        #region GRID

        /// <summary>A mesh triangle with its UV3 footprint, ready to rasterise.</summary>
        private struct Triangle
        {
            public Vector3 P0, P1, P2;
            public Vector3 N0, N1, N2;
            public Vector2 Q0, Q1, Q2;      // UV3, normalised to 0..1
            public float WorldArea;
            public float UvArea;
            public bool FrontFacing;        // positive winding in UV space
        }

        /// <summary>
        /// Build the rasterisable triangle list, and report the whole mesh's world and UV area
        /// before any face filtering - those totals are what size the probe cell.
        /// </summary>
        private static List<Triangle> BuildTriangles(cMesh mesh, out double worldArea, out double uvArea)
        {
            worldArea = 0;
            uvArea = 0;

            List<Triangle> triangles = new List<Triangle>();
            if (!TryPickUnwrap(mesh, out List<Vector2> uvs, out float uvScale))
                return triangles;

            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
                if (a >= mesh.Vertices.Count || b >= mesh.Vertices.Count || c >= mesh.Vertices.Count)
                    continue;

                Triangle t = new Triangle
                {
                    P0 = mesh.Vertices[a],
                    P1 = mesh.Vertices[b],
                    P2 = mesh.Vertices[c],
                    Q0 = uvs[a] * uvScale,
                    Q1 = uvs[b] * uvScale,
                    Q2 = uvs[c] * uvScale,
                };
                if (mesh.Normals != null && mesh.Normals.Count > c)
                {
                    t.N0 = mesh.Normals[a];
                    t.N1 = mesh.Normals[b];
                    t.N2 = mesh.Normals[c];
                }
                t.WorldArea = Vector3.Cross(t.P1 - t.P0, t.P2 - t.P0).Length() * 0.5f;
                float signedUvArea = ((t.Q1.X - t.Q0.X) * (t.Q2.Y - t.Q0.Y) - (t.Q2.X - t.Q0.X) * (t.Q1.Y - t.Q0.Y)) * 0.5f;
                t.UvArea = Math.Abs(signedUvArea);
                t.FrontFacing = signedUvArea > 0.0f;
                if (t.UvArea <= 1e-12f)
                    continue;

                triangles.Add(t);
                worldArea += t.WorldArea;
                uvArea += t.UvArea;
            }

            // A double-sided mesh puts both halves on one unwrap, mirrored, so the two are told
            // apart by their winding in UV space. Where a front-facing half exists, it is the one
            // that owns the surface - keeping only it stops probes landing on the back face, which
            // would flip both the position (by the shell's thickness) and the averaged normal.
            if (triangles.Any(t => t.FrontFacing) && triangles.Any(t => !t.FrontFacing))
                triangles.RemoveAll(t => !t.FrontFacing);

            return triangles;
        }

        /// <summary>
        /// Pick the mesh's alphalight unwrap and the factor that maps it back to 0..1. Most sets
        /// arrive from <see cref="ModelUtility.ToMesh"/> scaled by 16; a handful are already unit
        /// range, so the scale is decided from the data rather than assumed.
        /// </summary>
        private static bool TryPickUnwrap(cMesh mesh, out List<Vector2> uvs, out float scale)
        {
            uvs = null;
            scale = 1.0f;
            if (mesh.UVs == null)
                return false;

            foreach (int channel in UvChannels)
            {
                if (channel >= mesh.UVs.Length)
                    continue;

                List<Vector2> candidate = mesh.UVs[channel];
                if (candidate == null || candidate.Count != mesh.Vertices.Count || candidate.Count == 0)
                    continue;

                float min = float.MaxValue, max = float.MinValue;
                foreach (Vector2 uv in candidate)
                {
                    min = Math.Min(min, Math.Min(uv.X, uv.Y));
                    max = Math.Max(max, Math.Max(uv.X, uv.Y));
                }
                if (min < -0.05f || max > 16.05f)
                    continue;

                uvs = candidate;
                scale = max > 1.001f ? 1.0f / 16.0f : 1.0f;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Take the probe grid size from the entity's existing parameters where we can, since
        /// nothing in the level reproduces retail's choice. Otherwise size the grid so probes sit
        /// roughly <see cref="AlphalightBakeSettings.TargetTexelSize"/> apart, measuring the world
        /// length the unwrap's u and v axes cover.
        /// </summary>
        private static void ResolveGridSize(Sample sample, FunctionEntity function, List<Triangle> triangles, AlphalightBakeSettings settings, int sourceResolution)
        {
            if (settings.PreserveExistingResolution)
            {
                int w = ExistingAxis(function, ShortGuids.alpha_light_scale_x, settings, sourceResolution);
                int h = ExistingAxis(function, ShortGuids.alpha_light_scale_y, settings, sourceResolution);
                if (w > 0 && h > 0)
                {
                    sample.Width = w;
                    sample.Height = h;
                    return;
                }
            }

            // Mean |dP/du| and |dP/dv| over the unwrap, weighted by how much of it each triangle owns.
            double du = 0, dv = 0, weight = 0;
            foreach (Triangle t in triangles)
            {
                Vector2 e1 = t.Q1 - t.Q0, e2 = t.Q2 - t.Q0;
                float det = e1.X * e2.Y - e2.X * e1.Y;
                if (Math.Abs(det) < 1e-12f)
                    continue;

                Vector3 p1 = t.P1 - t.P0, p2 = t.P2 - t.P0;
                Vector3 dPdu = (p1 * e2.Y - p2 * e1.Y) / det;
                Vector3 dPdv = (p2 * e1.X - p1 * e2.X) / det;

                du += dPdu.Length() * t.UvArea;
                dv += dPdv.Length() * t.UvArea;
                weight += t.UvArea;
            }

            float texel = Math.Max(0.0001f, settings.TargetTexelSize);
            sample.Width = ClampAxis(weight > 0 ? du / weight / texel : 0, settings);
            sample.Height = ClampAxis(weight > 0 ? dv / weight / texel : 0, settings);
        }

        private static int ExistingAxis(FunctionEntity function, ShortGuid parameter, AlphalightBakeSettings settings, int sourceResolution)
        {
            if (sourceResolution <= 0 || !(function.GetParameter(parameter)?.content is cFloat scale))
                return 0;

            // The parameter holds the axis over the edge of the atlas it was baked against, and the
            // product lands on an integer, so the grid size comes straight back out of it.
            int axis = (int)Math.Round(scale.value * sourceResolution);
            return axis >= settings.MinGridSize && axis <= settings.MaxGridSize ? axis : 0;
        }

        /// <summary>
        /// <c>round(length / texel) + 1</c>, clamped. The +1 is because the probes sit on grid
        /// nodes: a run of n cells needs n+1 of them. Best fit to retail across every level.
        /// </summary>
        private static int ClampAxis(double lengthOverTexel, AlphalightBakeSettings settings)
        {
            int v = (int)Math.Round(lengthOverTexel) + 1;
            if (v < settings.MinGridSize) v = settings.MinGridSize;
            if (v > settings.MaxGridSize) v = settings.MaxGridSize;
            return v;
        }

        /// <summary>
        /// Fill the probe grid. Node (i,j) is the surface at UV <c>(i/(W-1), j/(H-1))</c>; nodes
        /// the unwrap leaves empty take the nearest point on it, which is what reproduces retail's
        /// clamped grid edges.
        /// </summary>
        private static void Rasterise(Sample sample, List<Triangle> triangles, double worldArea, double uvArea)
        {
            int w = sample.Width, h = sample.Height;
            sample.Positions = new Vector3[w * h];

            Vector3 normalSum = Vector3.Zero;

            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++)
                {
                    Vector2 uv = new Vector2(
                        w > 1 ? (float)i / (w - 1) : 0.5f,
                        h > 1 ? (float)j / (h - 1) : 0.5f);

                    if (!TrySampleInside(triangles, uv, out Vector3 position, out Vector3 normal))
                        SampleNearest(triangles, uv, out position, out normal);

                    sample.Positions[j * w + i] = position;
                    normalSum += normal;
                }
            }

            // alpha_light_average_normal is the mean of the probes' normals, left unnormalised, so
            // its length falls away as the surface curves.
            sample.AverageNormal = normalSum * (1.0f / (w * h));

            // A holds the world edge length of one probe cell over a constant. The area the grid
            // spans is the mesh's, divided by how many times over the unwrap covers the unit
            // square - which is what stops a double-sided mesh, whose two halves share one unwrap,
            // from counting its surface twice. A chart-packed unwrap covers less than the square,
            // never more, so the divisor floors at one.
            double cells = Math.Max(1, (w - 1) * (h - 1));
            double cellArea = worldArea / Math.Max(1.0, uvArea) / cells;
            sample.ProbeScale = (float)(Math.Sqrt(cellArea) / ProbeScaleDivisor);
        }

        /// <summary>
        /// Last resort for a mesh whose unwrap is degenerate: spread the probes over the mesh's
        /// bounds in the plane the averaged normal faces. This is what the simplest retail grids
        /// amount to anyway - a flat pane's 2x2 sits exactly on its bounding box corners.
        /// </summary>
        private static bool RasteriseBounds(Sample sample, FunctionEntity function, cMesh mesh, AlphalightBakeSettings settings, int sourceResolution)
        {
            Vector3 normalSum = Vector3.Zero;
            foreach (Vector3 n in mesh.Normals)
                normalSum += n;
            Vector3 average = normalSum * (mesh.Normals.Count > 0 ? 1.0f / mesh.Normals.Count : 0.0f);

            Vector3 min = mesh.Vertices[0], max = mesh.Vertices[0];
            foreach (Vector3 v in mesh.Vertices)
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
            Vector3 size = max - min;

            // Collapse the axis the surface faces; grid across the other two.
            Vector3 facing = average.LengthSquared() > 1e-8f ? Vector3.Normalize(average) : Vector3.Zero;
            int depth = facing == Vector3.Zero
                ? (size.X <= size.Y && size.X <= size.Z ? 0 : (size.Y <= size.Z ? 1 : 2))
                : (Math.Abs(facing.X) >= Math.Abs(facing.Y) && Math.Abs(facing.X) >= Math.Abs(facing.Z) ? 0
                    : (Math.Abs(facing.Y) >= Math.Abs(facing.Z) ? 1 : 2));

            int axisU = depth == 0 ? 1 : 0;
            int axisV = depth == 2 ? 1 : 2;
            float spanU = Component(size, axisU), spanV = Component(size, axisV);

            sample.Width = ClampAxis(spanU / Math.Max(0.0001f, settings.TargetTexelSize), settings);
            sample.Height = ClampAxis(spanV / Math.Max(0.0001f, settings.TargetTexelSize), settings);
            if (settings.PreserveExistingResolution)
            {
                int w = ExistingAxis(function, ShortGuids.alpha_light_scale_x, settings, sourceResolution);
                int h = ExistingAxis(function, ShortGuids.alpha_light_scale_y, settings, sourceResolution);
                if (w > 0 && h > 0) { sample.Width = w; sample.Height = h; }
            }

            sample.AverageNormal = average;
            sample.Positions = new Vector3[sample.Width * sample.Height];
            for (int j = 0; j < sample.Height; j++)
            {
                for (int i = 0; i < sample.Width; i++)
                {
                    Vector3 p = min;
                    Set(ref p, axisU, Component(min, axisU) + spanU * (sample.Width > 1 ? (float)i / (sample.Width - 1) : 0.5f));
                    Set(ref p, axisV, Component(min, axisV) + spanV * (sample.Height > 1 ? (float)j / (sample.Height - 1) : 0.5f));
                    Set(ref p, depth, (Component(min, depth) + Component(max, depth)) * 0.5f);
                    sample.Positions[j * sample.Width + i] = p;
                }
            }

            double cells = Math.Max(1, (sample.Width - 1) * (sample.Height - 1));
            sample.ProbeScale = (float)(Math.Sqrt(spanU * spanV / cells) / ProbeScaleDivisor);
            return true;
        }

        private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : (axis == 1 ? v.Y : v.Z);

        private static void Set(ref Vector3 v, int axis, float value)
        {
            if (axis == 0) v.X = value;
            else if (axis == 1) v.Y = value;
            else v.Z = value;
        }

        private static bool TrySampleInside(List<Triangle> triangles, Vector2 uv, out Vector3 position, out Vector3 normal)
        {
            for (int i = 0; i < triangles.Count; i++)
            {
                Triangle t = triangles[i];
                if (!Barycentric(t.Q0, t.Q1, t.Q2, uv, out float a, out float b, out float c))
                    continue;

                position = t.P0 * a + t.P1 * b + t.P2 * c;
                normal = t.N0 * a + t.N1 * b + t.N2 * c;
                return true;
            }
            position = Vector3.Zero;
            normal = Vector3.Zero;
            return false;
        }

        private static void SampleNearest(List<Triangle> triangles, Vector2 uv, out Vector3 position, out Vector3 normal)
        {
            float best = float.MaxValue;
            position = Vector3.Zero;
            normal = Vector3.Zero;

            foreach (Triangle t in triangles)
            {
                ClosestOnTriangle(uv, t.Q0, t.Q1, t.Q2, out float a, out float b, out float c, out float distance);
                if (distance >= best)
                    continue;
                best = distance;
                position = t.P0 * a + t.P1 * b + t.P2 * c;
                normal = t.N0 * a + t.N1 * b + t.N2 * c;
            }
        }

        private static void ClosestOnTriangle(Vector2 p, Vector2 q0, Vector2 q1, Vector2 q2, out float a, out float b, out float c, out float distance)
        {
            if (Barycentric(q0, q1, q2, p, out a, out b, out c))
            {
                distance = 0.0f;
                return;
            }

            distance = float.MaxValue;
            a = 1.0f; b = 0.0f; c = 0.0f;

            ClosestOnSegment(p, q0, q1, out float d, out float t);
            if (d < distance) { distance = d; a = 1.0f - t; b = t; c = 0.0f; }
            ClosestOnSegment(p, q1, q2, out d, out t);
            if (d < distance) { distance = d; a = 0.0f; b = 1.0f - t; c = t; }
            ClosestOnSegment(p, q2, q0, out d, out t);
            if (d < distance) { distance = d; a = t; b = 0.0f; c = 1.0f - t; }
        }

        private static void ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b, out float distance, out float t)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared();
            t = lengthSquared < 1e-16f ? 0.0f : Math.Max(0.0f, Math.Min(1.0f, Vector2.Dot(p - a, ab) / lengthSquared));
            distance = (a + ab * t - p).Length();
        }

        private static bool Barycentric(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p, out float a, out float b, out float c)
        {
            a = b = c = 0.0f;
            float det = (p1.Y - p2.Y) * (p0.X - p2.X) + (p2.X - p1.X) * (p0.Y - p2.Y);
            if (Math.Abs(det) < 1e-12f)
                return false;

            a = ((p1.Y - p2.Y) * (p.X - p2.X) + (p2.X - p1.X) * (p.Y - p2.Y)) / det;
            b = ((p2.Y - p0.Y) * (p.X - p2.X) + (p0.X - p2.X) * (p.Y - p2.Y)) / det;
            c = 1.0f - a - b;

            const float epsilon = -1e-4f;
            return a >= epsilon && b >= epsilon && c >= epsilon;
        }

        #endregion

        #region PACKING

        /// <summary>
        /// Place every box, growing the atlas by powers of two until they fit. Returns the edge
        /// length used, or 0 if even <see cref="AlphalightBakeSettings.MaxResolution"/> is too
        /// small. Boxes go in tallest-first, which is what keeps the skyline flat.
        /// </summary>
        private static int Pack(List<Sample> samples, AlphalightBakeSettings settings)
        {
            List<Sample> ordered = samples
                .OrderByDescending(s => s.BoxHeight)
                .ThenByDescending(s => s.BoxWidth)
                .ThenBy(s => s.Entity.shortGUID.AsUInt32)
                .ToList();

            for (int resolution = Math.Max(1, settings.MinResolution); resolution <= settings.MaxResolution; resolution *= 2)
                if (TryPack(ordered, resolution))
                    return resolution;

            return 0;
        }

        private static bool TryPack(List<Sample> ordered, int resolution)
        {
            // Skyline: one column height per texel, best-fit placement (lowest resulting top edge,
            // then leftmost).
            int[] skyline = new int[resolution];

            foreach (Sample sample in ordered)
            {
                int boxWidth = sample.BoxWidth, boxHeight = sample.BoxHeight;
                if (boxWidth > resolution || boxHeight > resolution)
                    return false;

                int bestX = -1, bestY = int.MaxValue;
                for (int x = 0; x + boxWidth <= resolution; x++)
                {
                    int y = 0;
                    for (int i = x; i < x + boxWidth; i++)
                        if (skyline[i] > y) y = skyline[i];

                    if (y + boxHeight > resolution || y >= bestY)
                        continue;
                    bestY = y;
                    bestX = x;
                }
                if (bestX < 0)
                    return false;

                for (int i = bestX; i < bestX + boxWidth; i++)
                    skyline[i] = bestY + boxHeight;

                // Parameters address the first probe, one texel in from the box's own corner.
                sample.ProbeX = bestX + 1;
                sample.ProbeY = bestY + 1;
            }
            return true;
        }

        #endregion

        #region OUTPUT

        /// <summary>
        /// Compose the A16B16G16R16F image: probes, then the left/top dilation border, over a field
        /// of <see cref="Unclaimed"/>.
        /// </summary>
        private static void WriteAtlas(AlphaLightLevel atlas, List<Sample> samples, int resolution)
        {
            Vector4[] texels = new Vector4[resolution * resolution];
            for (int i = 0; i < texels.Length; i++)
                texels[i] = Unclaimed;

            foreach (Sample sample in samples)
            {
                int w = sample.Width, h = sample.Height;

                for (int j = 0; j < h; j++)
                {
                    for (int i = 0; i < w; i++)
                    {
                        Vector3 p = sample.Positions[j * w + i];
                        texels[(sample.ProbeY + j) * resolution + sample.ProbeX + i] = new Vector4(p.X, p.Y, p.Z, sample.ProbeScale);
                    }

                    // Border column: repeat the row's first probe so the bilinear tap at UV 0 is clean.
                    texels[(sample.ProbeY + j) * resolution + sample.ProbeX - 1] =
                        texels[(sample.ProbeY + j) * resolution + sample.ProbeX];
                }

                // Border row: retail fills the whole thing with probe (0,0) rather than repeating
                // the first row, so match that.
                Vector4 corner = texels[sample.ProbeY * resolution + sample.ProbeX];
                for (int i = 0; i < sample.BoxWidth; i++)
                    texels[(sample.ProbeY - 1) * resolution + sample.ProbeX - 1 + i] = corner;
            }

            byte[] data = new byte[resolution * resolution * 8];
            for (int i = 0; i < texels.Length; i++)
            {
                WriteHalf(data, i * 8 + 0, texels[i].X);
                WriteHalf(data, i * 8 + 2, texels[i].Y);
                WriteHalf(data, i * 8 + 4, texels[i].Z);
                WriteHalf(data, i * 8 + 6, texels[i].W);
            }

            atlas.Resolution = new Vector2(resolution, resolution);
            atlas.ImageData = data;
        }

        private static void WriteHalf(byte[] destination, int offset, float value)
        {
            ushort half = ToHalf(value);
            destination[offset] = (byte)(half & 0xFF);
            destination[offset + 1] = (byte)(half >> 8);
        }

        /// <summary>IEEE 754 binary16 encode, round-to-nearest-even.</summary>
        private static ushort ToHalf(float value)
        {
            uint bits = (uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            uint sign = (bits >> 16) & 0x8000;
            int exponent = (int)((bits >> 23) & 0xFF) - 127 + 15;
            uint mantissa = bits & 0x7FFFFF;

            if (exponent >= 0x1F)
                return (ushort)(sign | 0x7BFF); // Clamp to the largest finite half.
            if (exponent <= 0)
            {
                if (exponent < -10)
                    return (ushort)sign;
                mantissa |= 0x800000;
                int shift = 14 - exponent;
                uint sub = mantissa >> shift;
                if (((mantissa >> (shift - 1)) & 1) != 0) sub++;
                return (ushort)(sign | sub);
            }

            uint half = (uint)(sign | ((uint)exponent << 10) | (mantissa >> 13));
            if ((mantissa & 0x1000) != 0) half++;
            return (ushort)half;
        }

        private static void ApplyParameters(Sample sample, int resolution)
        {
            float inverse = 1.0f / resolution;
            SetFloat(sample.Entity, ShortGuids.alpha_light_offset_x, sample.ProbeX * inverse);
            SetFloat(sample.Entity, ShortGuids.alpha_light_offset_y, sample.ProbeY * inverse);
            SetFloat(sample.Entity, ShortGuids.alpha_light_scale_x, sample.Width * inverse);
            SetFloat(sample.Entity, ShortGuids.alpha_light_scale_y, sample.Height * inverse);
            sample.Entity.AddParameter(ShortGuids.alpha_light_average_normal, new cVector3(sample.AverageNormal), ParameterVariant.PARAMETER);
        }

        private static void SetFloat(FunctionEntity entity, ShortGuid parameter, float value)
        {
            entity.AddParameter(parameter, new cFloat(value), ParameterVariant.PARAMETER);
        }

        private static void ClearParameters(FunctionEntity entity)
        {
            entity.parameters.RemoveAll(p =>
                p.name == ShortGuids.alpha_light_offset_x ||
                p.name == ShortGuids.alpha_light_offset_y ||
                p.name == ShortGuids.alpha_light_scale_x ||
                p.name == ShortGuids.alpha_light_scale_y ||
                p.name == ShortGuids.alpha_light_average_normal);
        }

        #endregion
    }
}
#endif
