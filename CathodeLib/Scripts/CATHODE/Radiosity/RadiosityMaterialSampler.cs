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
    /// Resolves the diffuse reflectance of a surface point by sampling the material's diffuse map
    /// at that point's UV.
    /// </summary>
    /// <remarks>
    /// <para>Retail's own compiler output, RADIOSITY_ALBEDO_SAMPLES.BIN (seen in the Windows Store 
    /// release), is a per-sample table: 130280 position/normal/albedo records for BSP_TORRENS, 
    /// averaging 17 distinct colours within any given cubic metre. A single average per material 
    /// cannot reproduce that - it collapsed Solace's 7655 distinct probe albedos to 108, 
    /// with one colour covering 15% of all probes, which is what made our bounce light look flat 
    /// and over-stylised.</para>
    /// <para>Materials are registered up front and addressed by slot afterwards, so the per-texel
    /// path is an array index with no locking. Textures are decoded once and cached against the
    /// texture, not the material, since a diffuse map is usually shared by several materials that
    /// differ only in their tint. Only ~220 diffuse maps exist in a level and the persistent mip
    /// chain is small, so capping the decode at
    /// <see cref="RadiosityBakeSettings.AlbedoTextureMaxEdge"/> keeps the cache near 13 MB.</para>
    /// </remarks>
    public sealed class RadiosityMaterialSampler
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
        /// sRGB byte to linear reflectance. Diffuse maps are authored and stored sRGB-encoded;
        /// radiosity is a linear light transport, so every texel has to come out of that encoding
        /// before it is used as a reflectance.
        /// </summary>
        /// <remarks>
        /// This is worth 3-4x on anything that is not already near white. Retail's own albedo
        /// table settles it: dividing its per-material albedo through by the material's
        /// DIFFUSE_TINT recovers the linearised mean of the diffuse map almost exactly - 24.6
        /// against 24.7 for metal_base_dark, 36.4 against 36.3 for metal_tarnished, 219.7 against
        /// 219.8 for drt_plastic_1 - where the raw sRGB means are 87.5, 104.9 and 238.8. Feeding
        /// the stored values straight in made a mid-grey prop reflect like near-white paint.
        /// </remarks>
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

        private readonly List<MaterialAlbedo> _slots = new List<MaterialAlbedo>();
        private readonly Dictionary<Materials.Material, int> _slotForMaterial =
            new Dictionary<Materials.Material, int>();
        private readonly Dictionary<Textures.TEX4, MaterialAlbedo> _textures =
            new Dictionary<Textures.TEX4, MaterialAlbedo>();
        private readonly RadiosityBakeSettings _settings;

        /// <summary>Slot meaning "no material", which resolves to the flat fallback albedo.</summary>
        public const int NoMaterial = -1;

        /// <summary>Materials whose diffuse map decoded, versus those left on a flat colour.</summary>
        public int Decoded { get; private set; }
        public int FellBack { get; private set; }

        /// <summary>Texels resolved by sampling a texture, versus by a material mean.</summary>
        private long _texelsSampled, _texelsFlat;
        public long TexelsSampled => Interlocked.Read(ref _texelsSampled);
        public long TexelsFlat => Interlocked.Read(ref _texelsFlat);

        public RadiosityMaterialSampler(RadiosityBakeSettings settings)
        {
            _settings = settings ?? RadiosityBakeSettings.CreateDefault();
        }

        /// <summary>
        /// Assign a material a slot, decoding its diffuse map on first sight. Call this while
        /// collecting geometry, on one thread; <see cref="Sample"/> is what the solve uses.
        /// </summary>
        public int Register(Materials.Material material)
        {
            if (material == null)
                return NoMaterial;
            if (_slotForMaterial.TryGetValue(material, out int slot))
                return slot;

            slot = _slots.Count;
            _slots.Add(Build(material));
            _slotForMaterial[material] = slot;
            return slot;
        }

        /// <summary>
        /// Albedo at a surface point, in 0..1 RGB. <paramref name="uv"/> is the material's own
        /// diffuse UV (mesh channel 0), which tiles, so it is wrapped rather than clamped.
        /// </summary>
        public Vector3 Sample(int slot, Vector2 uv)
        {
            if (slot < 0 || slot >= _slots.Count)
                return Grade(new Vector3(_settings.FallbackAlbedo), Vector3.One);

            MaterialAlbedo entry = _slots[slot];
            if (entry.Rgb == null)
            {
                Interlocked.Increment(ref _texelsFlat);
                return entry.Mean;
            }

            Interlocked.Increment(ref _texelsSampled);
            return Grade(Bilinear(entry, uv.X * entry.UvScale, uv.Y * entry.UvScale), entry.Tint);
        }

        /// <summary>
        /// Average albedo for a slot, for surfaces with no usable diffuse UV and for the
        /// area-weighted fill that covers texels the UV rasteriser missed.
        /// </summary>
        public Vector3 Mean(int slot) =>
            slot < 0 || slot >= _slots.Count
                ? Grade(new Vector3(_settings.FallbackAlbedo), Vector3.One)
                : _slots[slot].Mean;

        private MaterialAlbedo Build(Materials.Material material)
        {
            Vector3 tint = _settings.ApplyDiffuseTint ? ResolveDiffuseTint(material) : Vector3.One;
            float uvScale = ResolveDiffuseUvMult(material);
            Textures.TEX4 diffuse = ResolveDiffuse(material);
            MaterialAlbedo image = diffuse == null ? null : DecodeCached(diffuse);

            if (image?.Rgb == null)
            {
                FellBack++;
                return new MaterialAlbedo
                {
                    Tint = tint,
                    UvScale = uvScale,
                    Mean = Grade(image?.Mean ?? new Vector3(_settings.FallbackAlbedo), tint),
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
                Mean = Grade(image.Mean, tint),
            };
        }

        /// <summary>Decode a texture to RGB8 once, and record its untinted mean.</summary>
        private MaterialAlbedo DecodeCached(Textures.TEX4 texture)
        {
            if (_textures.TryGetValue(texture, out MaterialAlbedo cached))
                return cached;

            var entry = new MaterialAlbedo();
            if (RadiosityTextureDecoder.TryDecode(texture, _settings.AlbedoTextureMaxEdge,
                                                  out byte[] rgb, out int width, out int height))
            {
                entry.Rgb = rgb;
                entry.Width = width;
                entry.Height = height;

                // Averaged in linear, not in the stored encoding: the mean of a set of sRGB values
                // is not the encoding of their mean light, and this mean stands in for a whole
                // surface wherever the UV rasteriser has nothing better.
                double r = 0, g = 0, b = 0;
                int pixels = width * height;
                for (int i = 0; i < pixels; i++)
                {
                    r += SrgbToLinear[rgb[i * 3]];
                    g += SrgbToLinear[rgb[i * 3 + 1]];
                    b += SrgbToLinear[rgb[i * 3 + 2]];
                }
                entry.Mean = pixels == 0
                    ? new Vector3(_settings.FallbackAlbedo)
                    : new Vector3((float)(r / pixels), (float)(g / pixels), (float)(b / pixels));
            }
            else
            {
                entry.Mean = new Vector3(_settings.FallbackAlbedo);
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

        /// <summary>Apply the material tint, the global scale, and the reflectance ceiling.</summary>
        private Vector3 Grade(Vector3 colour, Vector3 tint)
        {
            Vector3 result = colour * tint * _settings.AlbedoScale;
            float max = _settings.MaxAlbedo;
            return new Vector3(
                Math.Min(max, Math.Max(0f, result.X)),
                Math.Min(max, Math.Max(0f, result.Y)),
                Math.Min(max, Math.Max(0f, result.Z)));
        }

        private static Textures.TEX4 ResolveDiffuse(Materials.Material material)
        {
            Shaders.Shader shader = material.Shader;
            if (shader == null)
                return null;

            // DIFFUSE_MAP is sampler slot 1 across every ubershader that has one.
            const int diffuseSampler = (int)CA_ENVIRONMENT.SAMPLERS.DIFFUSE_MAP;
            if (diffuseSampler >= shader.SamplerRemaps.Count)
                return null;

            int remap = shader.SamplerRemaps[diffuseSampler];
            if (remap == 255 || remap < 0 || remap >= material.TextureReferences.Count)
                return null;

            return material.TextureReferences[remap]?.Texture;
        }

        /// <summary>
        /// Each ubershader's own DIFFUSE_TINT parameter index, or -1 where it has none.
        /// </summary>
        /// <remarks>
        /// These are not interchangeable: CA_ENVIRONMENT keeps DIFFUSE_TINT at 7, but every other
        /// shader that has one puts it somewhere else (CA_HAIR at 2, CA_CHARACTER at 16, and so
        /// on). Reading CA_ENVIRONMENT's index on another shader picks up an unrelated constant,
        /// which is how a plain metal wall ends up tinted pure red.
        /// </remarks>
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

        private static Vector3 ResolveDiffuseTint(Materials.Material material)
        {
            Shaders.Shader shader = material.Shader;
            if (shader == null || !TryConstant(material, DiffuseTintIndex(shader.Ubershader), 3, out int remap))
                return Vector3.One;

            return new Vector3(
                Clamp01(material.PixelShaderConstants[remap]),
                Clamp01(material.PixelShaderConstants[remap + 1]),
                Clamp01(material.PixelShaderConstants[remap + 2]));
        }

        /// <summary>
        /// How many times the diffuse map tiles across the mesh's UVs.
        /// </summary>
        /// <remarks>
        /// Roughly 62% of a level's materials set this to something other than 1, with a tail out
        /// past 100. Ignoring it does not just sample the wrong texels - it shrinks the area of
        /// texture that one atlas texel integrates over by the same factor, so the albedo comes
        /// back less averaged and therefore more saturated than the surface really is.
        /// </remarks>
        private static float ResolveDiffuseUvMult(Materials.Material material)
        {
            Shaders.Shader shader = material.Shader;
            if (shader == null || !TryConstant(material, DiffuseUvMultIndex(shader.Ubershader), 1, out int remap))
                return 1.0f;

            float value = material.PixelShaderConstants[remap];
            return value > 0.0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : 1.0f;
        }

        /// <summary>Resolve a parameter index to its first constant, checking room for its components.</summary>
        private static bool TryConstant(Materials.Material material, int parameter, int components, out int remap)
        {
            remap = -1;
            Shaders.Shader shader = material.Shader;
            if (shader == null || parameter < 0 || parameter >= shader.PixelShaderParameterRemaps.Count)
                return false;

            remap = shader.PixelShaderParameterRemaps[parameter];
            return remap != 255 && remap >= 0 && remap + components - 1 < material.PixelShaderConstants.Count;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
#endif
