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
            public byte[] Alpha;            // decoded alpha plane (Width*Height), or null
            public float AlphaMean = 1.0f;  // mean of the alpha channel, 0..1; the DIRT lerp weight lives there
            public Vector3 AlphaWeightedMean;   // E[a*rgb]/E[a]: what the alpha-weighted texels actually contribute

            // Per-texel DIRT_MAP overlay. Retail samples the dirt per texel, not per material:
            // within a single mover its stored albedo varies with sd 40-70 where a mean fold is
            // flat, and its per-mover means range 28-165 on one material (albmat --detail on
            // SCI_Hub's MUN_Plastic_Smooth_White_DTY). The overlay image is sampled at the
            // diffuse UV times its own DIRT_UV_MULT, alongside the diffuse fetch.
            public MaterialAlbedo Dirt;     // decoded dirt texture (a texture-cache entry), or null
            public float DirtUvScale = 1f;  // DIRT_UV_MULT
            public bool DirtMultiply;       // true = multiply mode; false = lerp by dirt alpha
            public float DirtWeight;        // lerp mode: sat(DIRT_BLEND_MULT_SPEC_POWER) * vertex fade
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
        private readonly Dictionary<Materials.Material, float> _dirtVertexFade =
            new Dictionary<Materials.Material, float>();
        private readonly RadiosityBakeSettings _settings;

        /// <summary>
        /// Per-material expectation of the shader's lerp-dirt vertex-colour fade,
        /// E[(1 - saturate(vcol.x*R0.x + vcol.y*R0.y))^2], measured over the geometry that uses
        /// the material. Must be recorded before the material is first registered; the geometry
        /// collector's pre-pass does this. Lerp-mode dirt weight is scaled by it; multiply-mode
        /// dirt is not faded - retail's stored albedo on multiply materials sits at or below the
        /// full-strength dirt, so the runtime fade demonstrably does not reach their compiler.
        /// </summary>
        public void SetDirtVertexFade(Materials.Material material, float fade)
        {
            if (material != null)
                _dirtVertexFade[material] = Clamp01(fade);
        }

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
            Vector3 c = Bilinear(entry, uv.X * entry.UvScale, uv.Y * entry.UvScale) * entry.Tint;
            if (entry.Dirt != null)
            {
                // The dirt folds in as a GREYSCALE factor, not its RGB: retail's stored albedo
                // on the big multiply-dirt population is neutral (mover-aggregate R/B 1.03
                // against the dirt map's 1.28 warm), so their compiler reads the overlay as a
                // scalar. Folding the RGB tinted SCI_Hub's white transit corridors rust-red.
                float du = uv.X * entry.DirtUvScale, dv = uv.Y * entry.DirtUvScale;
                float s = Luma(Bilinear(entry.Dirt, du, dv));
                if (entry.DirtMultiply)
                {
                    c *= s;
                }
                else
                {
                    float w = BilinearAlpha(entry.Dirt, du, dv) * entry.DirtWeight;
                    c += (new Vector3(s) - c) * (w > 1f ? 1f : w);
                }
            }
            return GradeFinal(c);
        }

        private static float Luma(Vector3 linear) =>
            0.2126f * linear.X + 0.7152f * linear.Y + 0.0722f * linear.Z;

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
            Vector3 tint = _settings.ApplyDiffuseTint ? ResolveDiffuseTint(material, _settings) : Vector3.One;

            // The _DTY/_RST "dirty" family darkens its clean base with a DIRT_MAP overlay
            // (SECONDARY_DIFFUSE is unbound on every one inspected). Sampling only DIFFUSE_MAP
            // read those materials at 4-10x retail's stored albedo (albmat on SCI_Hub:
            // Plastic_Matte_Black_DTY 10.4x, Smooth_White_DTY 5.7x, overlay-free materials
            // ~1.0x), and the over-unity region diverged the runtime relaxation into SCI_Hub's
            // full-frame whiteout. The fold is the runtime shader's own dirt math, read off the
            // byte-identical CA_ENVIRONMENT master: both modes blend in linear space;
            // DIRT_BLEND_MULTIPLY scales the diffuse by the dirt colour outright, and lerp mode
            // mixes the TINTED diffuse toward the UNtinted dirt colour by the dirt map's ALPHA
            // channel scaled by saturate(DIRT_BLEND_MULT_SPEC_POWER), further faded by the
            // vertex-colour law measured in RadiosityGeometry.CollectDirtVertexFades.
            // (DIRT_AO_AMOUNT only enters through an AO map's tint - dirtFactor =
            // amount*(aoTint-1)+1 - so it is inert on materials without one; using it as the
            // weight was the first fold's bug.) The overlay is applied PER TEXEL in Sample -
            // retail's compiler does the same, which is why its stored albedo varies within a
            // single mover - and at mean level here for the fallback paths.
            MaterialAlbedo dirtImage = null;
            float dirtUvScale = 1f;
            bool dirtMultiply = false;
            float dirtWeight = 0f;
            if (_settings.SampleSecondaryDiffuse && material.Shader != null)
            {
                Shaders.Shader sh = material.Shader;
                int dirtBit = -1, multiplyBit = -1, dirtSlot = -1, powerParam = -1, uvParam = -1;
                if (sh.Ubershader == SHADER_LIST.CA_ENVIRONMENT)
                {
                    dirtBit = (int)CA_ENVIRONMENT.FEATURES.DIRT_MAPPING;
                    multiplyBit = (int)CA_ENVIRONMENT.FEATURES.DIRT_BLEND_MULTIPLY;
                    dirtSlot = (int)CA_ENVIRONMENT.SAMPLERS.DIRT_MAP;
                    powerParam = (int)CA_ENVIRONMENT.PARAMETERS.DIRT_BLEND_MULT_SPEC_POWER;
                    uvParam = (int)CA_ENVIRONMENT.PARAMETERS.DIRT_UV_MULT;
                }
                else if (sh.Ubershader == SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT)
                {
                    dirtBit = (int)CA_LIGHTMAP_ENVIRONMENT.FEATURES.DIRT_MAPPING;
                    multiplyBit = (int)CA_LIGHTMAP_ENVIRONMENT.FEATURES.DIRT_BLEND_MULTIPLY;
                    dirtSlot = (int)CA_LIGHTMAP_ENVIRONMENT.SAMPLERS.DIRT_MAP;
                    powerParam = (int)CA_LIGHTMAP_ENVIRONMENT.PARAMETERS.DIRT_BLEND_MULT_SPEC_POWER;
                    uvParam = (int)CA_LIGHTMAP_ENVIRONMENT.PARAMETERS.DIRT_UV_MULT;
                }

                if (dirtBit >= 0 && ((sh.UbershaderFeatureFlags >> dirtBit) & 1) != 0)
                {
                    Textures.TEX4 dirtTexture = ResolveSamplerTexture(material, dirtSlot);
                    MaterialAlbedo dirt = dirtTexture == null ? null : DecodeCached(dirtTexture);
                    if (dirt?.Rgb != null)
                    {
                        dirtImage = dirt;
                        dirtUvScale = ResolveUvMult(material, uvParam);
                        dirtMultiply = ((sh.UbershaderFeatureFlags >> multiplyBit) & 1) != 0;
                        if (!dirtMultiply)
                        {
                            float amount = 1.0f;
                            if (TryConstant(material, powerParam, 1, out int remap))
                                amount = Clamp01(material.PixelShaderConstants[remap]);
                            if (!_dirtVertexFade.TryGetValue(material, out float fade))
                                fade = 1f;
                            dirtWeight = amount * fade;
                        }
                    }
                }
            }

            float uvScale = ResolveDiffuseUvMult(material);
            Textures.TEX4 diffuse = ResolveDiffuse(material);
            MaterialAlbedo image = diffuse == null ? null : DecodeCached(diffuse);

            // The slot mean carries the dirt fold at expectation level, for the fallback and
            // area-fill paths: multiply mode is a product of means, lerp mode is
            // c*(1 - w*E[a]) + w*E[a*d], a lerp to the alpha-weighted dirt value. Greyscale,
            // matching the per-texel path in Sample.
            Vector3 baseMean = (image?.Mean ?? new Vector3(_settings.FallbackAlbedo)) * tint;
            if (dirtImage != null)
            {
                if (dirtMultiply)
                    baseMean *= Luma(dirtImage.Mean);
                else
                    baseMean += (new Vector3(Luma(dirtImage.AlphaWeightedMean)) - baseMean) *
                                Clamp01(dirtImage.AlphaMean * dirtWeight);
            }

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
                Dirt = dirtImage,
                DirtUvScale = dirtUvScale,
                DirtMultiply = dirtMultiply,
                DirtWeight = dirtWeight,
                Mean = GradeFinal(baseMean),
            };
        }

        /// <summary>Decode a texture to RGB8 once, and record its untinted mean.</summary>
        private MaterialAlbedo DecodeCached(Textures.TEX4 texture)
        {
            if (_textures.TryGetValue(texture, out MaterialAlbedo cached))
                return cached;

            var entry = new MaterialAlbedo();
            if (RadiosityTextureDecoder.TryDecode(texture, _settings.AlbedoTextureMaxEdge,
                                                  out byte[] rgb, out byte[] alpha, out int width, out int height))
            {
                entry.Rgb = rgb;
                entry.Alpha = alpha;
                entry.Width = width;
                entry.Height = height;

                // Averaged in linear, not in the stored encoding: the mean of a set of sRGB values
                // is not the encoding of their mean light, and this mean stands in for a whole
                // surface wherever the UV rasteriser has nothing better. Alpha is mask data, not
                // colour, so its mean is taken raw.
                double r = 0, g = 0, b = 0, a = 0, wr = 0, wg = 0, wb = 0;
                int pixels = width * height;
                for (int i = 0; i < pixels; i++)
                {
                    float lr = SrgbToLinear[rgb[i * 3]];
                    float lg = SrgbToLinear[rgb[i * 3 + 1]];
                    float lb = SrgbToLinear[rgb[i * 3 + 2]];
                    r += lr;
                    g += lg;
                    b += lb;
                    if (alpha != null)
                    {
                        float w = alpha[i];
                        a += w;
                        wr += w * lr;
                        wg += w * lg;
                        wb += w * lb;
                    }
                }
                entry.Mean = pixels == 0
                    ? new Vector3(_settings.FallbackAlbedo)
                    : new Vector3((float)(r / pixels), (float)(g / pixels), (float)(b / pixels));
                entry.AlphaMean = pixels == 0 || alpha == null ? 1f : (float)(a / (pixels * 255.0));
                // The shader's lerp weight is per-texel alpha, so the expected blend target is the
                // alpha-weighted colour: E[lerp(c, d, w*a)] = c*(1 - w*E[a]) + w*E[a*d]. Opaque
                // texels are usually also the dark, dusty ones, so this sits below the plain mean.
                entry.AlphaWeightedMean = a > 0 ? new Vector3((float)(wr / a), (float)(wg / a), (float)(wb / a)) : entry.Mean;
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

        /// <summary>
        /// Bilinear fetch of the alpha plane, wrap-addressed like the colour fetch. Alpha is mask
        /// data, so no linearisation. Returns 1 when the texture shipped no alpha.
        /// </summary>
        private static float BilinearAlpha(MaterialAlbedo image, float u, float v)
        {
            if (image.Alpha == null)
                return 1f;
            if (!IsFinite(u) || !IsFinite(v))
                return image.AlphaMean;

            float x = u * image.Width - 0.5f;
            float y = v * image.Height - 0.5f;
            if (Math.Abs(x) > 1e7f || Math.Abs(y) > 1e7f)
                return image.AlphaMean;

            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;

            int x1 = Wrap(x0 + 1, image.Width), y1 = Wrap(y0 + 1, image.Height);
            x0 = Wrap(x0, image.Width);
            y0 = Wrap(y0, image.Height);

            float a00 = image.Alpha[y0 * image.Width + x0], a10 = image.Alpha[y0 * image.Width + x1];
            float a01 = image.Alpha[y1 * image.Width + x0], a11 = image.Alpha[y1 * image.Width + x1];
            float top = a00 + (a10 - a00) * fx;
            float bottom = a01 + (a11 - a01) * fx;
            return (top + (bottom - top) * fy) * (1f / 255f);
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
        private Vector3 Grade(Vector3 colour, Vector3 tint) => GradeFinal(colour * tint);

        /// <summary>The global scale and the reflectance ceiling - the last step of every path.</summary>
        private Vector3 GradeFinal(Vector3 result)
        {
            result *= _settings.AlbedoScale;
            float max = _settings.MaxAlbedo;
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

        private static Vector3 ResolveDiffuseTint(Materials.Material material, RadiosityBakeSettings settings)
        {
            Shaders.Shader shader = material.Shader;
            if (shader == null || !TryConstant(material, DiffuseTintIndex(shader.Ubershader), 3, out int remap))
                return Vector3.One;

            //The 26-remap CA_ENVIRONMENT permutation: retail's compiler ignores DIFFUSE_TINT for
            //it, verified per mover on ChallengeMap4 - ours/retail stored albedo equals the tint
            //EXACTLY (mover 2511 tint 0.09 ratio 0.098, mover 2372 tint 0.43 ratio 0.43). This was
            //measured once before and dismissed as a compiler quirk because honouring it makes
            //dark plastics bounce like white paint - but that IS how retail's bounce behaves, and
            //it is where the dark-plastic albedo deficits on CM3/CM4 came from.
            if (settings.UntintedEnvironment26 &&
                shader.Ubershader == SHADER_LIST.CA_ENVIRONMENT &&
                shader.PixelShaderParameterRemaps.Count == 26)
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
        private static float ResolveDiffuseUvMult(Materials.Material material) =>
            ResolveUvMult(material, DiffuseUvMultIndex(material.Shader?.Ubershader ?? (SHADER_LIST)(-1)));

        private static float ResolveUvMult(Materials.Material material, int parameter)
        {
            if (!TryConstant(material, parameter, 1, out int remap))
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
