#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using CATHODE;
using CATHODE.ShaderTypes;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Resolves the diffuse reflectance of a surface point by sampling the material's diffuse map at that point's UV.
    /// </summary>
    internal sealed class RadiosityMaterialSampler
    {
        /// <summary>A material's decoded diffuse map plus the constants applied on top of it.</summary>
        private sealed class MaterialAlbedo
        {
            public byte[] Rgb;              // null when there was no decodable diffuse map; sRGB, as stored
            public int Width, Height;
            public Vector3 Tint;
            public float UvScale = 1.0f;    // DIFFUSE_UV_MULT
            public Vector3 Mean;            // linear and graded, so it can be returned as-is
        }

        /// <summary>
        /// sRGB byte to linear reflectance: diffuse maps are stored sRGB-encoded and radiosity is linear transport.
        /// Retail's albedo divided by DIFFUSE_TINT matches the linearised map mean (metal_base_dark 24.6 against 24.7),
        /// not the raw sRGB one (87.5).
        /// </summary>
        private static readonly float[] SrgbToLinear = BuildSrgbToLinear();

        private static float[] BuildSrgbToLinear()
        {
            var table = new float[256];
            for (int i = 0; i < 256; i++)
            {
                double c = i / 255.0;
                table[i] = (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
            }
            return table;
        }

        // Slot store: append-only. A new slot is written into the array (a grown copy when full) under _registerLock
        // and published by array then count, so the lock-free readers (Sample, Mean) read the count first and only
        // index below it. _slotForMaterial, _textures and the Decoded/FellBack counters are only touched under the
        // lock.
        private readonly object _registerLock = new object();
        private MaterialAlbedo[] _slots = new MaterialAlbedo[64];
        private int _slotCount;
        private readonly Dictionary<Materials.Material, int> _slotForMaterial =
            new Dictionary<Materials.Material, int>();
        private readonly Dictionary<Textures.TEX4, MaterialAlbedo> _textures =
            new Dictionary<Textures.TEX4, MaterialAlbedo>();

        /// <summary>Slot meaning "no material", which resolves to the flat fallback albedo.</summary>
        public const int NoMaterial = -1;

        /// <summary>Materials whose diffuse map decoded, versus those left on a flat colour.</summary>
        public int Decoded { get; private set; }
        public int FellBack { get; private set; }

        /// <summary>
        /// Assign a material a slot, decoding its diffuse map on first sight. Thread-safe: geometry
        /// collection registers every surface material, and the parallel slice bakes register late
        /// (emissive) materials; <see cref="Sample"/> is what the solve uses.
        /// </summary>
        public int Register(Materials.Material material)
        {
            if (material == null)
                return NoMaterial;
            lock (_registerLock)
            {
                if (_slotForMaterial.TryGetValue(material, out int slot))
                    return slot;

                MaterialAlbedo entry = Build(material);
                slot = _slotCount;
                MaterialAlbedo[] slots = _slots;
                if (slot == slots.Length)
                {
                    // Grow by copy: a reader still holding the old array keeps valid entries for every slot it can see.
                    var grown = new MaterialAlbedo[slots.Length * 2];
                    Array.Copy(slots, grown, slots.Length);
                    slots = grown;
                }
                slots[slot] = entry;
                Volatile.Write(ref _slots, slots);
                Volatile.Write(ref _slotCount, slot + 1);
                _slotForMaterial[material] = slot;
                return slot;
            }
        }

        /// <summary>The registered entry for a slot, or null for <see cref="NoMaterial"/> and unknown slots.</summary>
        private MaterialAlbedo Entry(int slot)
        {
            if (slot < 0 || slot >= Volatile.Read(ref _slotCount))
                return null;
            return Volatile.Read(ref _slots)[slot];
        }

        /// <summary>
        /// Albedo at a surface point, in 0..1 RGB. <paramref name="uv"/> is the material's own
        /// diffuse UV (mesh channel 0), which tiles, so it is wrapped rather than clamped.
        /// </summary>
        public Vector3 Sample(int slot, Vector2 uv)
        {
            MaterialAlbedo entry = Entry(slot);
            if (entry == null)
                return Grade(new Vector3(RadiosityTuning.FallbackAlbedo), Vector3.One);

            if (entry.Rgb == null)
                return entry.Mean;

            return GradeFinal(Bilinear(entry, uv.X * entry.UvScale, uv.Y * entry.UvScale) * entry.Tint);
        }

        /// <summary>
        /// Average albedo for a slot, for surfaces with no usable diffuse UV and for the
        /// area-weighted fill that covers texels the UV rasteriser missed.
        /// </summary>
        public Vector3 Mean(int slot)
        {
            MaterialAlbedo entry = Entry(slot);
            return entry != null ? entry.Mean : Grade(new Vector3(RadiosityTuning.FallbackAlbedo), Vector3.One);
        }

        private MaterialAlbedo Build(Materials.Material material)
        {
            Vector3 tint = ResolveDiffuseTint(material);

            // DIRT_MAP overlays are not folded in, so the _DTY/_RST "dirty" family samples its clean base (4-10x
            // retail's stored albedo on SCI_Hub). A fold of the runtime shader's dirt math was built and left off.
            float uvScale = ResolveDiffuseUvMult(material);
            Textures.TEX4 diffuse = ResolveDiffuse(material);
            MaterialAlbedo image = diffuse == null ? null : DecodeCached(diffuse);

            Vector3 baseMean = (image?.Mean ?? new Vector3(RadiosityTuning.FallbackAlbedo)) * tint;
            if (image?.Rgb == null)
            {
                FellBack++;
                return new MaterialAlbedo
                {
                    Tint = tint,
                    UvScale = uvScale,
                    Mean = GradeFinal(baseMean),
                };
            }

            Decoded++;
            return new MaterialAlbedo
            {
                Rgb = image.Rgb,
                Width = image.Width,
                Height = image.Height,
                Tint = tint,
                UvScale = uvScale,
                Mean = GradeFinal(baseMean),
            };
        }

        /// <summary>Decode a texture to RGB8 once, and record its untinted mean.</summary>
        private MaterialAlbedo DecodeCached(Textures.TEX4 texture)
        {
            if (_textures.TryGetValue(texture, out MaterialAlbedo cached))
                return cached;

            var entry = new MaterialAlbedo();
            if (RadiosityTextureDecoder.TryDecode(texture, RadiosityTuning.AlbedoTextureMaxEdge,
                                                  out byte[] rgb, out int width, out int height))
            {
                entry.Rgb = rgb;
                entry.Width = width;
                entry.Height = height;

                // Averaged in linear: the mean of sRGB values is not the encoding of their mean light, and this mean
                // stands in for a whole surface wherever the UV rasteriser has nothing better.
                double r = 0, g = 0, b = 0;
                int pixels = width * height;
                for (int i = 0; i < pixels; i++)
                {
                    float lr = SrgbToLinear[rgb[i * 3]];
                    float lg = SrgbToLinear[rgb[i * 3 + 1]];
                    float lb = SrgbToLinear[rgb[i * 3 + 2]];
                    r += lr;
                    g += lg;
                    b += lb;
                }
                entry.Mean = pixels == 0
                    ? new Vector3(RadiosityTuning.FallbackAlbedo)
                    : new Vector3((float)(r / pixels), (float)(g / pixels), (float)(b / pixels));
            }
            else
            {
                entry.Mean = new Vector3(RadiosityTuning.FallbackAlbedo);
            }

            _textures[texture] = entry;
            return entry;
        }

        /// <summary>
        /// Bilinear fetch with wrap addressing, since diffuse UVs tile. Texels are linearised
        /// before they are blended, which is the order a hardware sRGB sampler uses and the only
        /// one that keeps the result a reflectance.
        /// </summary>
        private static Vector3 Bilinear(MaterialAlbedo image, float u, float v)
        {
            if (!IsFinite(u) || !IsFinite(v))
                return image.Mean;

            float x = u * image.Width - 0.5f;
            float y = v * image.Height - 0.5f;
            if (Math.Abs(x) > 1e7f || Math.Abs(y) > 1e7f)
                return image.Mean;

            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;

            int x1 = Wrap(x0 + 1, image.Width), y1 = Wrap(y0 + 1, image.Height);
            x0 = Wrap(x0, image.Width);
            y0 = Wrap(y0, image.Height);

            Vector3 c00 = Texel(image, x0, y0), c10 = Texel(image, x1, y0);
            Vector3 c01 = Texel(image, x0, y1), c11 = Texel(image, x1, y1);

            Vector3 top = c00 + (c10 - c00) * fx;
            Vector3 bottom = c01 + (c11 - c01) * fx;
            return top + (bottom - top) * fy;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static Vector3 Texel(MaterialAlbedo image, int x, int y)
        {
            int i = (y * image.Width + x) * 3;
            return new Vector3(SrgbToLinear[image.Rgb[i]], SrgbToLinear[image.Rgb[i + 1]],
                               SrgbToLinear[image.Rgb[i + 2]]);
        }

        private static int Wrap(int value, int size)
        {
            if (size <= 0) return 0;
            int m = value % size;
            return m < 0 ? m + size : m;
        }

        /// <summary>Apply the material tint and the reflectance ceiling.</summary>
        private Vector3 Grade(Vector3 colour, Vector3 tint) => GradeFinal(colour * tint);

        /// <summary>The reflectance ceiling - the last step of every path.</summary>
        private Vector3 GradeFinal(Vector3 result)
        {
            float max = RadiosityTuning.MaxAlbedo;
            return new Vector3(
                Math.Min(max, Math.Max(0f, result.X)),
                Math.Min(max, Math.Max(0f, result.Y)),
                Math.Min(max, Math.Max(0f, result.Z)));
        }

        private static Textures.TEX4 ResolveDiffuse(Materials.Material material) =>
            // DIFFUSE_MAP is sampler slot 1 across every ubershader that has one.
            ResolveSamplerTexture(material, (int)CA_ENVIRONMENT.SAMPLERS.DIFFUSE_MAP);

        private static Textures.TEX4 ResolveSamplerTexture(Materials.Material material, int samplerSlot)
        {
            Shaders.Shader shader = material.Shader;
            if (shader == null || samplerSlot >= shader.SamplerRemaps.Count)
                return null;

            int remap = shader.SamplerRemaps[samplerSlot];
            if (remap == 255 || remap < 0 || remap >= material.TextureReferences.Count)
                return null;

            return material.TextureReferences[remap]?.Texture;
        }

        /// <summary>
        /// Each ubershader's own DIFFUSE_TINT parameter index, or -1 where it has none.
        /// </summary>
        private static int DiffuseTintIndex(SHADER_LIST ubershader)
        {
            switch (ubershader)
            {
                case SHADER_LIST.CA_ENVIRONMENT: return (int)CA_ENVIRONMENT.PARAMETERS.DIFFUSE_TINT;
                case SHADER_LIST.CA_DECAL_ENVIRONMENT: return (int)CA_DECAL_ENVIRONMENT.PARAMETERS.DIFFUSE_TINT;
                case SHADER_LIST.CA_CHARACTER: return (int)CA_CHARACTER.PARAMETERS.DIFFUSE_TINT;
                case SHADER_LIST.CA_SKIN: return (int)CA_SKIN.PARAMETERS.DIFFUSE_TINT;
                case SHADER_LIST.CA_HAIR: return (int)CA_HAIR.PARAMETERS.DIFFUSE_TINT;
                case SHADER_LIST.CA_TERRAIN: return (int)CA_TERRAIN.PARAMETERS.DIFFUSE_TINT;
                case SHADER_LIST.CA_SURFACE_EFFECTS: return (int)CA_SURFACE_EFFECTS.PARAMETERS.DIFFUSE_TINT;
                case SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT: return (int)CA_LIGHTMAP_ENVIRONMENT.PARAMETERS.DIFFUSE_TINT;
                case SHADER_LIST.CA_STREAMER: return (int)CA_STREAMER.PARAMETERS.DIFFUSE_TINT;
                case SHADER_LIST.CA_LOW_LOD_CHARACTER: return (int)CA_LOW_LOD_CHARACTER.PARAMETERS.DIFFUSE_TINT;
                default: return -1;
            }
        }

        /// <summary>Each ubershader's own DIFFUSE_UV_MULT parameter index, or -1.</summary>
        private static int DiffuseUvMultIndex(SHADER_LIST ubershader)
        {
            switch (ubershader)
            {
                case SHADER_LIST.CA_ENVIRONMENT: return (int)CA_ENVIRONMENT.PARAMETERS.DIFFUSE_UV_MULT;
                case SHADER_LIST.CA_DECAL_ENVIRONMENT: return (int)CA_DECAL_ENVIRONMENT.PARAMETERS.DIFFUSE_UV_MULT;
                case SHADER_LIST.CA_CHARACTER: return (int)CA_CHARACTER.PARAMETERS.DIFFUSE_UV_MULT;
                case SHADER_LIST.CA_SKIN: return (int)CA_SKIN.PARAMETERS.DIFFUSE_UV_MULT;
                case SHADER_LIST.CA_HAIR: return (int)CA_HAIR.PARAMETERS.DIFFUSE_UV_MULT;
                case SHADER_LIST.CA_TERRAIN: return (int)CA_TERRAIN.PARAMETERS.DIFFUSE_UV_MULT;
                case SHADER_LIST.CA_SURFACE_EFFECTS: return (int)CA_SURFACE_EFFECTS.PARAMETERS.DIFFUSE_UV_MULT;
                case SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT: return (int)CA_LIGHTMAP_ENVIRONMENT.PARAMETERS.DIFFUSE_UV_MULT;
                case SHADER_LIST.CA_STREAMER: return (int)CA_STREAMER.PARAMETERS.DIFFUSE_UV_MULT;
                case SHADER_LIST.CA_LOW_LOD_CHARACTER: return (int)CA_LOW_LOD_CHARACTER.PARAMETERS.DIFFUSE_UV_MULT;
                default: return -1;
            }
        }

        /// <summary>
        /// The material's DIFFUSE_TINT, which multiplies the linearised diffuse map.
        /// </summary>
        private static Vector3 ResolveDiffuseTint(Materials.Material material)
        {
            Shaders.Shader shader = material.Shader;
            if (shader == null || !RadiosityGeometry.TryMaterialConstant(material, DiffuseTintIndex(shader.Ubershader), 3, out int remap))
                return Vector3.One;

            return new Vector3(
                Clamp01(material.PixelShaderConstants[remap]),
                Clamp01(material.PixelShaderConstants[remap + 1]),
                Clamp01(material.PixelShaderConstants[remap + 2]));
        }

        /// <summary>
        /// How many times the diffuse map tiles across the mesh's UVs.
        /// </summary>
        private static float ResolveDiffuseUvMult(Materials.Material material) =>
            ResolveUvMult(material, DiffuseUvMultIndex(material.Shader?.Ubershader ?? (SHADER_LIST)(-1)));

        private static float ResolveUvMult(Materials.Material material, int parameter)
        {
            if (!RadiosityGeometry.TryMaterialConstant(material, parameter, 1, out int remap))
                return 1.0f;

            float value = material.PixelShaderConstants[remap];
            return value > 0.0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : 1.0f;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
#endif
