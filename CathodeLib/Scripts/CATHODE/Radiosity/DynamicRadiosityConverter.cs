#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Linq;
using CATHODE;
using CATHODE.ShaderTypes;
using CathodeLib.ObjectExtensions;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Converts individual movers onto the engine's DYNAMIC radiosity path, so added or moved
    /// geometry is lit live from the volume probe field instead of needing a lightmap rebake.
    /// </summary>
    /// <remarks>
    /// <para>Dynamic radiosity IS the lightmap pipeline: the same relit probe atlas, decoded by
    /// the same shader instructions, sampled at one per-instance coordinate the engine derives
    /// from the volume probe hash at the instance's pivot (cbInstanceXSC's
    /// RadiosityProbeTexcoordAndScale) instead of per-pixel lightmap UVs through the instance's
    /// MODEL_PARAMS rect. A dynamic instance therefore needs no atlas allocation at all - which
    /// is exactly what an edit-added instance does not have.</para>
    /// <para>Retail's dynamic convention, all three parts of which this reproduces per mover:
    /// a genuinely dynamic shader (bytecode that reads cb10[11].xy, not just flipped flags - a
    /// flag-flipped static shader still samples the atlas through its rect and renders one
    /// stripe per UV chart), a zeroed 16-byte lightmap transform (the engine asserts on a rect
    /// plus the dynamic bit), and no RADIOSITY_INSTANCE_MAP row.</para>
    /// <para>Materials are CLONED, never flipped in place - a material and its shader are shared
    /// by every retail user. The clone is pointed at the level's own compiled dynamic twin where
    /// one ships (same ubershader family, features with the family's radiosity bits swapped,
    /// requirements swapped); where none does, a dynamic permutation is SYNTHESISED by cloning
    /// the shader entry and applying <see cref="DxbcUtils.PatchStaticToDynamic"/> - the exact
    /// transformation CA's compiler produces between the permutations, golden-tested against
    /// every shipped twin pair.</para>
    /// <para>Because added movers frequently reference retail REDS runs (that is how instancing
    /// binds them), each converted mover first gets its own duplicated run with LOD chains
    /// deep-copied; otherwise the [DYN] clone leaks onto every retail mover sharing the run.</para>
    /// <para>The 16-byte constants head is zeroed ONLY when the mover actually has a
    /// radiosity-class element: on glass/FX movers those bytes are per-type parameters (tint),
    /// not a lightmap transform, and zeroing them corrupts the material (measured: a
    /// GLASS_Distortion panel rendered uniform blue).</para>
    /// </remarks>
    public static class DynamicRadiosityConverter
    {
        public sealed class Result
        {
            /// <summary>Mover indices whose radiosity-lit elements are now ALL dynamic (including
            /// movers that needed no work). Movers not in this set still carry a static-class
            /// element the patch could not convert and should stay on the lightmap path.</summary>
            public HashSet<int> ConvertedMovers = new HashSet<int>();
            public int MoversNoRadiosity;
            public int ElementsRepointed;
            public int ElementsAlreadyDynamic;
            public int ElementsUnconvertible;
            /// <summary>Static-class elements whose bytecode never samples the probe atlas.</summary>
            public int ElementsNeutralStatic;
            public int MaterialsCloned;
            public int ShadersSynthesised;
            public int TwinsUsed;
            public int RectsZeroed;
            public int MapRowsDropped;
        }

        public static Result Convert(Level level, ICollection<int> moverIndices, Action<string> log = null, bool dropInstanceMapRows = true,
                                     ICollection<int> preserveIslandRows = null)
        {
            var result = new Result();
            if (moverIndices == null || moverIndices.Count == 0)
                return result;

            const long staticBit = 1L << (int)SHADER_REQUIREMENTS.RADIOSITY_STATIC;
            const long dynamicBit = 1L << (int)SHADER_REQUIREMENTS.RADIOSITY_DYNAMIC;

            // Shipped shaders by their real identity: (ubershader family, feature flags,
            // requirement flags). Requirement flags alone are NOT an identity - binding a
            // material to a foreign family whose parameter remaps put its constants in different
            // registers renders it as a completely different material.
            var shaderByIdentity = new Dictionary<(SHADER_LIST, long, long), Shaders.Shader>();
            foreach (Shaders.Shader s in level.Shaders.Entries)
            {
                var id = (s.Ubershader, s.UbershaderFeatureFlags, s.UbershaderRequirementFlags);
                if (!shaderByIdentity.ContainsKey(id))
                    shaderByIdentity[id] = s;
            }

            // Each ubershader family defines its own radiosity FEATURE bits at family-specific
            // indices (CA_ENVIRONMENT 40/51, others 43/50, 28/29, 0/1...), resolved by
            // reflection on CATHODE.ShaderTypes.<family>.FEATURES and cached.
            var featBits = new Dictionary<SHADER_LIST, (int stat, int dyn)>();
            (int stat, int dyn) FeatBits(SHADER_LIST family)
            {
                if (featBits.TryGetValue(family, out (int, int) cached))
                    return cached;
                int st = -1, dy = -1;
                Type shaderClass = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + family);
                Type features = shaderClass?.GetNestedType("FEATURES");
                if (features != null && features.IsEnum)
                {
                    if (Enum.IsDefined(features, "RADIOSITY_STATIC")) st = System.Convert.ToInt32(Enum.Parse(features, "RADIOSITY_STATIC"));
                    if (Enum.IsDefined(features, "RADIOSITY_DYNAMIC")) dy = System.Convert.ToInt32(Enum.Parse(features, "RADIOSITY_DYNAMIC"));
                }
                featBits[family] = (st, dy);
                return (st, dy);
            }

            // A synthesised dynamic permutation per source shader, appended to the level's
            // shader list. The clone keeps the source's technique, samplers, remaps, states and
            // unpatched stages (shared by reference so the bytecode pools dedupe them); only the
            // pixel shader, both radiosity flag sets and the permutation hash change. Null is
            // cached for shaders whose pixel shader carries no radiosity sampling idiom.
            var synthFor = new Dictionary<Shaders.Shader, Shaders.Shader>();
            Shaders.Shader Synthesise(Shaders.Shader src)
            {
                if (synthFor.TryGetValue(src, out Shaders.Shader cached))
                    return cached;
                byte[] patched = DxbcUtils.PatchStaticToDynamic(src.PixelShader);
                Shaders.Shader clone = null;
                if (patched != null)
                {
                    clone = src.Copy();
                    clone.VertexShader = src.VertexShader;
                    clone.HullShader = src.HullShader;
                    clone.DomainShader = src.DomainShader;
                    clone.GeometryShader = src.GeometryShader;
                    clone.ComputeShader = src.ComputeShader;
                    clone.PixelShader = patched;
                    clone.UbershaderRequirementFlags = (src.UbershaderRequirementFlags | dynamicBit) & ~staticBit;
                    (int sb, int db) = FeatBits(src.Ubershader);
                    if (sb >= 0 && db >= 0)
                        clone.UbershaderFeatureFlags = (clone.UbershaderFeatureFlags & ~(1L << sb)) | (1L << db);
                    clone.PermutationHash = src.PermutationHash ^ 0x44594E21u;
                    level.Shaders.Entries.Add(clone);
                    result.ShadersSynthesised++;
                }
                synthFor[src] = clone;
                return clone;
            }

            // One dynamic material clone per source material, shared across movers. Null is
            // cached for materials with no dynamic route (static shader without the sampling
            // idiom).
            var cloneFor = new Dictionary<Materials.Material, Materials.Material>();
            Materials.Material DynamicMaterial(Materials.Material src)
            {
                if (cloneFor.TryGetValue(src, out Materials.Material existing))
                    return existing;

                Shaders.Shader dynShader = null;
                (int fsb, int fdb) = FeatBits(src.Shader.Ubershader);
                if (fsb >= 0 && fdb >= 0)
                {
                    long tFeat = (src.Shader.UbershaderFeatureFlags & ~(1L << fsb)) | (1L << fdb);
                    long tReq = (src.Shader.UbershaderRequirementFlags | dynamicBit) & ~staticBit;
                    if (shaderByIdentity.TryGetValue((src.Shader.Ubershader, tFeat, tReq), out Shaders.Shader twin))
                    {
                        dynShader = twin;
                        result.TwinsUsed++;
                    }
                }
                if (dynShader == null)
                    dynShader = Synthesise(src.Shader);
                if (dynShader == null)
                {
                    cloneFor[src] = null;
                    return null;
                }

                var clone = new Materials.Material
                {
                    Name = src.Name + "[DYN]",
                    TextureReferences = new List<TexturePtr>(src.TextureReferences),
                    EngineConstants = new List<float>(src.EngineConstants),
                    VertexShaderConstants = new List<float>(src.VertexShaderConstants),
                    PixelShaderConstants = new List<float>(src.PixelShaderConstants),
                    HullShaderConstants = new List<float>(src.HullShaderConstants),
                    DomainShaderConstants = new List<float>(src.DomainShaderConstants),
                    OfflineLightFeatures = src.OfflineLightFeatures,
                    Shader = dynShader,
                    PhysicalMaterialIndex = src.PhysicalMaterialIndex,
                    EnvironmentMapIndex = src.EnvironmentMapIndex,
                    Priority = src.Priority
                };
                level.Materials.Entries.Add(clone);
                result.MaterialsCloned++;
                cloneFor[src] = clone;
                return clone;
            }

            // A LOD chain is serialised as an index + count into the global REDS entry list, so
            // a private copy of it MUST be a registered contiguous run of its own - an
            // unregistered copy resolves to a garbage run index at save the moment one of its
            // materials is repointed (it no longer value-matches the original run), and the
            // game crashes streaming textures for the garbage element on zone load. Duplicate
            // recursively: RegisterDuplicateRun's shallow copies share their sub-LOD lists with
            // the originals, which conversion must never mutate.
            List<RenderableElements.Element> DupLodChain(List<RenderableElements.Element> lods)
            {
                if (lods == null || lods.Count == 0)
                    return new List<RenderableElements.Element>();
                List<RenderableElements.Element> copies = level.RenderableElements.RegisterDuplicateRun(lods);
                foreach (RenderableElements.Element c in copies)
                    if (c != null)
                        c.LODs = DupLodChain(c.LODs);
                return copies;
            }

            foreach (int mi in moverIndices.OrderBy(i => i))
            {
                if (mi < 0 || mi >= level.Movers.Entries.Count)
                    continue;
                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[mi];
                if (mover.RenderableElements == null || mover.RenderableElements.Count == 0)
                {
                    result.MoversNoRadiosity++;
                    result.ConvertedMovers.Add(mi);
                    continue;
                }

                // Census first, before touching the mover: does it carry radiosity-lit elements
                // at all, and does every static one have a dynamic route? (DynamicMaterial only
                // builds the clone - nothing is repointed yet.)
                int staticConvertible = 0, staticUnconvertible = 0, staticNeutral = 0, alreadyDynamic = 0;
                void CensusElement(RenderableElements.Element e)
                {
                    if (e == null)
                        return;
                    Shaders.Shader s = e.Material?.Shader;
                    if (s != null)
                    {
                        long f = s.UbershaderRequirementFlags;
                        if ((f & dynamicBit) != 0 && (f & staticBit) == 0)
                            alreadyDynamic++;
                        else if ((f & staticBit) != 0)
                        {
                            if (DynamicMaterial(e.Material) != null) staticConvertible++;
                            // Static-CLASS but the bytecode never reads the probe atlas: its
                            // lighting does not depend on the rect, so it neither needs
                            // conversion nor blocks it (48 such shaders on ChallengeMap3, all
                            // measured to carry no mangle constant anywhere in their code).
                            else if (!DxbcUtils.SamplesRadiosity(s.PixelShader)) staticNeutral++;
                            else staticUnconvertible++;
                        }
                    }
                    if (e.LODs != null)
                        foreach (RenderableElements.Element lod in e.LODs)
                            CensusElement(lod);
                }
                foreach (RenderableElements.Element e in mover.RenderableElements)
                    CensusElement(e);

                result.ElementsAlreadyDynamic += alreadyDynamic;
                result.ElementsNeutralStatic += staticNeutral;
                if (staticConvertible == 0 && staticUnconvertible == 0 && alreadyDynamic == 0)
                {
                    // Nothing radiosity-lit (FX, glass, decals): nothing to convert, nothing to
                    // bake, and its constants head must NOT be touched.
                    result.MoversNoRadiosity++;
                    result.ConvertedMovers.Add(mi);
                    continue;
                }
                if (staticUnconvertible > 0)
                {
                    // A static-class element the patch cannot convert would go fullbright next
                    // to a zeroed rect (glows in dark rooms). Leave the whole mover on the
                    // lightmap path instead of converting it halfway.
                    result.ElementsUnconvertible += staticUnconvertible;
                    continue;
                }

                if (staticConvertible > 0)
                {
                    // Own REDS run first, so converting materials cannot leak onto retail movers
                    // sharing the run by reference.
                    List<RenderableElements.Element> own = level.RenderableElements.RegisterDuplicateRun(mover.RenderableElements);
                    foreach (RenderableElements.Element e in own)
                        if (e != null)
                            e.LODs = DupLodChain(e.LODs);
                    mover.RenderableElements = own;

                    void ConvertElement(RenderableElements.Element e)
                    {
                        if (e == null)
                            return;
                        if (e.Material?.Shader != null &&
                            (e.Material.Shader.UbershaderRequirementFlags & staticBit) != 0 &&
                            cloneFor.TryGetValue(e.Material, out Materials.Material clone) && clone != null)
                        {
                            e.Material = clone;
                            result.ElementsRepointed++;
                        }
                        if (e.LODs != null)
                            foreach (RenderableElements.Element lod in e.LODs)
                                ConvertElement(lod);
                    }
                    foreach (RenderableElements.Element e in mover.RenderableElements)
                        ConvertElement(e);
                }

                // Retail's dynamic convention: no static atlas allocation. Only reached for
                // movers whose elements are radiosity-class, where the 16-byte head IS the
                // lightmap transform.
                byte[] raw = mover.RenderConstants?.RawBytes;
                if (raw != null && raw.Length >= 16)
                {
                    bool nonZero = false;
                    for (int b = 0; b < 16; b++)
                        if (raw[b] != 0) { nonZero = true; break; }
                    if (nonZero)
                    {
                        Array.Clear(raw, 0, 16);
                        mover.RenderConstants.SetRawBytes(raw);
                        result.RectsZeroed++;
                    }
                }

                result.ConvertedMovers.Add(mi);
            }

            // ...and no RADIOSITY_INSTANCE_MAP row. Added movers have none; moved movers carry
            // their retail row, which must go - a map row plus the dynamic bit is the mixed
            // state the engine asserts on. Rows are dropped only for movers actually converted.
            if (dropInstanceMapRows && level.RadiosityInstanceMap?.Entries != null && result.ConvertedMovers.Count > 0)
            {
                var convertedResources = new HashSet<Resources.Resource>();
                foreach (int mi in result.ConvertedMovers)
                    if (level.Movers.Entries[mi].Resource != null)
                        convertedResources.Add(level.Movers.Entries[mi].Resource);
                var keep = new List<RadiosityInstanceMap.Entry>(level.RadiosityInstanceMap.Entries.Count);
                foreach (RadiosityInstanceMap.Entry e in level.RadiosityInstanceMap.Entries)
                {
                    // Minted scheduling rows survive conversion: the engine's relight only
                    // processes slices an instance-map row's island points at, and the row's
                    // mover being dynamic is the state KEEPROWS mode ran at scale without
                    // incident (zeroed rect = nothing samples pages through it).
                    if (preserveIslandRows != null && preserveIslandRows.Contains(e.lightmap_transform))
                    {
                        keep.Add(e);
                        continue;
                    }
                    Resources.Resource r = e.Resource ?? level.Resources.GetAtWriteIndex(e.resource_index);
                    if (r != null && convertedResources.Contains(r))
                    {
                        result.MapRowsDropped++;
                        continue;
                    }
                    keep.Add(e);
                }
                if (result.MapRowsDropped > 0)
                {
                    level.RadiosityInstanceMap.Entries.Clear();
                    level.RadiosityInstanceMap.Entries.AddRange(keep);
                }
            }

            log?.Invoke("Radiosity dynprops: " + result.ConvertedMovers.Count + "/" + moverIndices.Count +
                        " movers forced dynamic (" + result.MoversNoRadiosity + " carried nothing radiosity-lit): " +
                        result.ElementsRepointed + " elements repointed, " + result.MaterialsCloned +
                        " materials cloned (" + result.TwinsUsed + " onto shipped twins, " + result.ShadersSynthesised +
                        " shaders synthesised via bytecode patch), " + result.RectsZeroed + " rects zeroed, " +
                        result.MapRowsDropped + " instance-map rows dropped" +
                        (result.ElementsUnconvertible > 0
                            ? "  [" + result.ElementsUnconvertible + " elements unconvertible - their movers stay on the lightmap path]"
                            : ""));
            return result;
        }
    }
}
#endif
