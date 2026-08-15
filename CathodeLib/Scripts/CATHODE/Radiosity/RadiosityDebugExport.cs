#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using CATHODE;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Writes a self-contained HTML page for exploring a level's baked radiosity data in 3D:
    /// probes, clusters, the influence links between them, and the surface lights.
    /// </summary>
    /// <remarks>
    /// <para>The runtime file is a set of parallel index spaces with no geometry in it, which
    /// makes it very hard to reason about from a hex dump or a statistics table. Patchy lighting
    /// in particular is a spatial problem - which probe took its light from where - so this
    /// resolves every reference back to a world position and draws it.</para>
    /// <para>Everything is inlined into one file: the data as base64 typed arrays and the viewer
    /// as plain WebGL2, with no external requests, so the page works from disk.</para>
    /// </remarks>
    public static partial class RadiosityDebugExport
    {
        private const int AtlasTexels = 128 * 128;
        private const int ProbeTexWidth = 256;
        private const int InfluencesPerProbe = 32;

        /// <summary>
        /// Export <paramref name="runtime"/> to a browsable HTML page.
        /// </summary>
        /// <param name="movers">Optional, for plotting model positions alongside the probes.</param>
        public static void Write(RadiosityRuntime runtime, string outputPath, string levelName, Movers movers = null)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));

            var json = new StringBuilder();
            json.Append("{\"level\":").Append(Quote(levelName));
            json.Append(",\"slices\":[");

            for (int s = 0; s < runtime.Slices.Count; s++)
            {
                if (s > 0) json.Append(',');
                json.Append(BuildSlice(runtime.Slices[s]));
            }
            json.Append(']');

            if (movers != null)
                json.Append(",\"movers\":").Append(BuildMovers(movers));

            json.Append('}');

            string html = Template.Replace("/*__DATA__*/", json.ToString());
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            File.WriteAllText(outputPath, html, new UTF8Encoding(false));
        }

        private static string BuildSlice(RadiosityRuntime.RuntimeDataSlice slice)
        {
            // ---- input probes: the emitters, laid out in the 256x64 tiled probe texture --------
            var inputPos = new List<float>();
            var inputAlbedo = new List<byte>();
            var inputNormal = new List<byte>();
            var inputTexel = new List<uint>();
            var inputIndexForTexel = new Dictionary<int, int>();

            for (int i = 0; i < slice.InputProbePositions.Count; i++)
            {
                Vector4u16 p = slice.InputProbePositions[i];
                if (p == null || (p.X == 0 && p.Y == 0 && p.Z == 0 && p.W == 0))
                    continue;

                inputIndexForTexel[i] = inputPos.Count / 3;
                inputPos.Add(Half(p.X)); inputPos.Add(Half(p.Y)); inputPos.Add(Half(p.Z));
                inputTexel.Add((uint)i);

                ColourRGBA8 a = i < slice.InputProbeAlbedo.Count ? slice.InputProbeAlbedo[i] : null;
                // Stored BGRA, so the R field carries blue.
                inputAlbedo.Add(a?.B ?? 0); inputAlbedo.Add(a?.G ?? 0); inputAlbedo.Add(a?.R ?? 0);

                ColourRGBA8 n = i < slice.InputProbeNormals.Count ? slice.InputProbeNormals[i] : null;
                inputNormal.Add(n?.R ?? 128); inputNormal.Add(n?.G ?? 128); inputNormal.Add(n?.B ?? 128);
            }

            // ---- surface probes: the receivers -------------------------------------------------
            var surfacePos = new List<float>();
            var surfaceSlot = new List<uint>();
            var surfaceIndexForSlot = new Dictionary<int, int>();
            for (int i = 0; i < slice.SurfaceProbePositions.Count; i++)
            {
                Vector4 p = slice.SurfaceProbePositions[i];
                // A live probe carries 1/sqrt(pi) in w; an unclaimed slot is (-100000, 0, 0, 0).
                if (p.W == 0.0f)
                    continue;
                surfaceIndexForSlot[i] = surfacePos.Count / 3;
                surfacePos.Add(p.X); surfacePos.Add(p.Y); surfacePos.Add(p.Z);
                surfaceSlot.Add((uint)i);
            }

            // ---- clusters: atlas-indexed emitters the influences name ---------------------------
            var clusterPos = new List<float>();
            var clusterAtlas = new List<uint>();
            var clusterIndexForAtlas = new Dictionary<int, int>();
            for (int i = 0; i < slice.ClusterPositions.Count; i++)
            {
                Vector4u16 p = slice.ClusterPositions[i];
                if (p == null || (p.X == 0 && p.Y == 0 && p.Z == 0 && p.W == 0))
                    continue;
                clusterIndexForAtlas[i] = clusterPos.Count / 3;
                clusterPos.Add(Half(p.X)); clusterPos.Add(Half(p.Y)); clusterPos.Add(Half(p.Z));
                clusterAtlas.Add((uint)i);
            }

            // ---- influences: 32 (cluster, weight) pairs per surface probe -----------------------
            // Packed two cluster refs per RGBA8 and four weights per Vector4u8, indexed by
            // probeSlot * 32 + k. A ref is (x, y) for atlas texel y * 256 + x.
            var infProbe = new List<uint>();
            var infCluster = new List<uint>();
            var infWeight = new List<byte>();

            foreach (KeyValuePair<int, int> kv in surfaceIndexForSlot)
            {
                int slot = kv.Key;
                for (int k = 0; k < InfluencesPerProbe; k++)
                {
                    int influenceSlot = slot * InfluencesPerProbe + k;
                    int packed = influenceSlot / 2;
                    if (packed >= slice.SurfaceProbeInfluences.Count) break;

                    ColourRGBA8 refs = slice.SurfaceProbeInfluences[packed];
                    if (refs == null) continue;
                    byte rx = (influenceSlot & 1) == 0 ? refs.R : refs.B;
                    byte ry = (influenceSlot & 1) == 0 ? refs.G : refs.A;
                    int atlas = ry * ProbeTexWidth + rx;

                    int weightPacked = influenceSlot / 4;
                    if (weightPacked >= slice.SurfaceProbeWeights.Count) continue;
                    Vector4u8 w = slice.SurfaceProbeWeights[weightPacked];
                    if (w == null) continue;
                    byte weight;
                    switch (influenceSlot & 3)
                    {
                        case 0: weight = w.X; break;
                        case 1: weight = w.Y; break;
                        case 2: weight = w.Z; break;
                        default: weight = w.W; break;
                    }

                    if (weight == 0) continue;
                    if (!clusterIndexForAtlas.TryGetValue(atlas, out int clusterIndex)) continue;

                    infProbe.Add((uint)kv.Value);
                    infCluster.Add((uint)clusterIndex);
                    infWeight.Add(weight);
                }
            }

            // ---- surface lights: positioned at the input probe they sample ----------------------
            var lightPos = new List<float>();
            var lightRgb = new List<byte>();
            var lightScale = new List<byte>();
            var lightWeight = new List<byte>();
            foreach (RadiosityRuntime.RuntimeSurfaceLights.Light l in slice.SurfaceLights.Lights)
            {
                int texel = l.V * ProbeTexWidth + l.U;
                if (!inputIndexForTexel.TryGetValue(texel, out int probeIndex)) continue;
                lightPos.Add(inputPos[probeIndex * 3]);
                lightPos.Add(inputPos[probeIndex * 3 + 1]);
                lightPos.Add(inputPos[probeIndex * 3 + 2]);
                lightRgb.Add(l.R); lightRgb.Add(l.G); lightRgb.Add(l.B);
                lightScale.Add(l.Scale);
                lightWeight.Add(l.Weight);
            }

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"inputPos\":").Append(B64(inputPos));
            sb.Append(",\"inputAlbedo\":").Append(B64(inputAlbedo));
            sb.Append(",\"inputNormal\":").Append(B64(inputNormal));
            sb.Append(",\"inputTexel\":").Append(B64(inputTexel));
            sb.Append(",\"surfacePos\":").Append(B64(surfacePos));
            sb.Append(",\"surfaceSlot\":").Append(B64(surfaceSlot));
            sb.Append(",\"clusterPos\":").Append(B64(clusterPos));
            sb.Append(",\"clusterAtlas\":").Append(B64(clusterAtlas));
            sb.Append(",\"infProbe\":").Append(B64(infProbe));
            sb.Append(",\"infCluster\":").Append(B64(infCluster));
            sb.Append(",\"infWeight\":").Append(B64(infWeight));
            sb.Append(",\"lightPos\":").Append(B64(lightPos));
            sb.Append(",\"lightRgb\":").Append(B64(lightRgb));
            sb.Append(",\"lightScale\":").Append(B64(lightScale));
            sb.Append(",\"lightWeight\":").Append(B64(lightWeight));
            sb.Append(",\"scatterCount\":").Append(slice.Scatter.Count);
            var h = slice.VolumeProbeHash;
            sb.Append(",\"volumeAabb\":[")
              .Append(F(h.AabbMin.X)).Append(',').Append(F(h.AabbMin.Y)).Append(',').Append(F(h.AabbMin.Z)).Append(',')
              .Append(F(h.AabbMax.X)).Append(',').Append(F(h.AabbMax.Y)).Append(',').Append(F(h.AabbMax.Z)).Append(']');
            sb.Append(",\"volumeItems\":").Append(h.Items.Count);
            sb.Append('}');
            return sb.ToString();
        }

        private static string BuildMovers(Movers movers)
        {
            var pos = new List<float>();
            var kind = new List<byte>();
            foreach (Movers.MOVER_DESCRIPTOR m in movers.Entries)
            {
                if (m == null) continue;
                pos.Add(m.Transform.M41); pos.Add(m.Transform.M42); pos.Add(m.Transform.M43);
                // 0 = ordinary renderable, 1 = light, 2 = emissive.
                byte k = 0;
                if (m.GetRenderableType() == RenderableInstanceType.LIGHT) k = 1;
                else if (m.EmissiveRadiosityMultiplier > 0f) k = 2;
                kind.Add(k);
            }
            return "{\"pos\":" + B64(pos) + ",\"kind\":" + B64(kind) + "}";
        }

        // ---- encoding helpers ------------------------------------------------------------------

        private static string B64(List<float> v)
        {
            var bytes = new byte[v.Count * 4];
            for (int i = 0; i < v.Count; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(v[i]), 0, bytes, i * 4, 4);
            return "\"f32:" + Convert.ToBase64String(bytes) + "\"";
        }

        private static string B64(List<uint> v)
        {
            var bytes = new byte[v.Count * 4];
            for (int i = 0; i < v.Count; i++)
                Buffer.BlockCopy(BitConverter.GetBytes(v[i]), 0, bytes, i * 4, 4);
            return "\"u32:" + Convert.ToBase64String(bytes) + "\"";
        }

        private static string B64(List<byte> v) => "\"u8:" + Convert.ToBase64String(v.ToArray()) + "\"";

        private static string F(float f) => f.ToString("0.####", CultureInfo.InvariantCulture);

        private static string Quote(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>IEEE 754 binary16 decode. netstandard2.0 has no BitConverter for halves.</summary>
        private static float Half(ushort h)
        {
            int sign = (h >> 15) & 1;
            int exponent = (h >> 10) & 0x1F;
            int mantissa = h & 0x3FF;

            float value;
            if (exponent == 0)
                value = mantissa * (float)Math.Pow(2, -24);
            else if (exponent == 0x1F)
                value = mantissa == 0 ? float.PositiveInfinity : float.NaN;
            else
                value = (1.0f + mantissa / 1024.0f) * (float)Math.Pow(2, exponent - 15);

            return sign == 1 ? -value : value;
        }
    }
}
#endif
