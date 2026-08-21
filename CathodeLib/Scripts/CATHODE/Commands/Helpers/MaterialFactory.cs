using CATHODE;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using static CATHODE.Lights;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib
{
    /// <summary>
    /// Creates the materials an instancing pass needs but the shipped material table does not
    /// contain, and hands back an existing one whenever the level already holds an equivalent.
    ///
    /// The case this exists for is deferred lights. A LightReference's renderable run lives on the
    /// COMPOSITE, so every instance of a prefab shares one material - but the light's features and
    /// its gobo texture are per-instance values, freely overridden by aliases further up the tree.
    /// Retail resolves this by giving the instance a material of its own: in ChallengeMap4 485 of
    /// 1862 light movers (26%) point at a material other than their composite's authored one, and
    /// AYZ\Habitation\Feature_Sml\Executive\Projector is exactly the case - authored against
    /// SPOT_SHADOW_SPECULAR_GOBO_MAT_134915 (gobo_bespoke_hospupper_proyector.tga), shipped in that
    /// level as SPOT_SPECULAR_GOBO_MAT_142083 (ayz\flash\screen_static_11.tga) because the
    /// ENVIRONMENT_ChallengeMap4 alias swaps the gobo, drops the shadow and adds a lens flare.
    ///
    /// The gobo texture goes on the material's FIRST sampler even though the deferred shader
    /// declares no sampler slots, and the material's offline light flags carry the GOBO bit - but
    /// the SHADER's own feature flags do not, which is why one CA_DEFERRED shader serves every
    /// light in a level and a synthesised material can simply borrow it.
    /// </summary>
    public class MaterialFactory
    {
        private readonly Level _level;
        private readonly object _lock = new object();

        //Any material sitting on a deferred-lighting shader, kept to lend its shader and its
        //fixed fields (priority / physical material / environment map) to anything we create.
        private Materials.Material _lightTemplate = null;

        //Existing light materials, keyed by the pair that decides whether one can be reused.
        private readonly Dictionary<(int flags, string texture), Materials.Material> _lightMaterials =
            new Dictionary<(int, string), Materials.Material>();

        //Names already taken, so a new material can claim the first free [NNNNNN] variant.
        private readonly Dictionary<string, int> _nextVariant = new Dictionary<string, int>();
        private readonly HashSet<string> _names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        //One renderable run per (model, material) so repeated instances share a REDS entry.
        private readonly Dictionary<(Models.CS2.Component.LOD.Submesh, Materials.Material), List<RenderableElements.Element>> _runs =
            new Dictionary<(Models.CS2.Component.LOD.Submesh, Materials.Material), List<RenderableElements.Element>>();

        //Texture lookups are by path string and get hit once per light, so they are worth caching.
        private readonly Dictionary<string, TexturePtr> _textures = new Dictionary<string, TexturePtr>(StringComparer.OrdinalIgnoreCase);

        public int MaterialsCreated { get; private set; }
        public int MaterialsReused { get; private set; }
        public int TexturesNotFound { get; private set; }

        /// <summary>Texture paths we could not resolve, with the first light that asked for each.</summary>
        public readonly Dictionary<string, string> UnresolvedTextures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public MaterialFactory(Level level)
        {
            _level = level;
            if (_level?.Materials?.Entries == null)
                return;

            foreach (Materials.Material material in _level.Materials.Entries)
            {
                if (material?.Name != null)
                    _names.Add(material.Name);
                if (!IsDeferredLight(material))
                    continue;

                if (_lightTemplate == null || (_lightTemplate.OfflineLightFeatures == null && material.OfflineLightFeatures != null))
                    _lightTemplate = material;

                //A light material carries at most the one gobo sampler, so anything with more is
                //not a shape we know how to stand in for.
                if (material.TextureReferences.Count > 1)
                    continue;

                //A flags dword of zero reads back as no LightFlags at all, which is what the
                //level's plain POINT_LIGHT_MATERIAL is - still the right thing to hand a light
                //that asks for type 0 with no features.
                var key = (material.OfflineLightFeatures?.Value ?? 0, TextureKey(material.TextureReferences.FirstOrDefault()));
                if (!_lightMaterials.ContainsKey(key))
                    _lightMaterials[key] = material;
            }
        }

        /// <summary>
        /// True if this material is one a deferred light renders with.
        /// </summary>
        public static bool IsDeferredLight(Materials.Material material)
        {
            return material?.Shader != null &&
                   (material.Shader.UbershaderRequirementFlags & (1L << (int)CATHODE.ShaderTypes.SHADER_REQUIREMENTS.DEFERRED_LIGHTING)) != 0;
        }

        /// <summary>
        /// Retail's name for a light material: the type, then SHADOW / SPECULAR / GOBO in that
        /// order for the features that carry a name, then the raw flags dword. Verified against all
        /// 21,137 light materials shipped across the game's 100 material tables - every one parses,
        /// and every one's embedded number equals its own flags.
        /// </summary>
        public static string LightMaterialName(LightType type, LightFeature features)
        {
            string name = TypePrefix(type);
            if ((features & LightFeature.Shadow) != 0) name += "_SHADOW";
            if ((features & LightFeature.Specular) != 0) name += "_SPECULAR";
            if ((features & LightFeature.Gobo) != 0) name += "_GOBO";
            return name + "_MAT_" + LightFlagsValue(type, features);
        }

        private static string TypePrefix(LightType type)
        {
            switch (type)
            {
                case LightType.Strip: return "STRIP";
                case LightType.Point: return "OMNI";
                case LightType.Spot: return "SPOT";
                //Retail ships no ambient or directional light material, so these two names are ours.
                default: return type.ToString().ToUpperInvariant();
            }
        }

        /// <summary>The dword a light material stores: the type in the low byte, the features above it.</summary>
        public static int LightFlagsValue(LightType type, LightFeature features)
        {
            return ((int)type & 0xFF) | (((int)features & 0xFFFF) << 8);
        }

        /// <summary>
        /// The material a deferred light with these resolved parameters must render with, created
        /// if the level does not already hold an equivalent one.
        ///
        /// <paramref name="goboTexturePath"/> is the light's gobo_texture parameter as authored
        /// (an "N:\Content\Build\Textures\..." path). It is only consulted when the GOBO feature is
        /// set; if it cannot be resolved to a texture the feature is dropped, because retail's
        /// invariant is that the GOBO bit and a first sampler always travel together.
        /// </summary>
        public Materials.Material GetLightMaterial(LightType type, LightFeature features, string goboTexturePath, string describeForLog = null)
        {
            if (_level?.Materials?.Entries == null || _lightTemplate == null)
                return null;

            lock (_lock)
            {
                TexturePtr gobo = null;
                if ((features & LightFeature.Gobo) != 0)
                {
                    gobo = ResolveTexture(goboTexturePath);
                    if (gobo == null)
                    {
                        TexturesNotFound++;
                        if (!string.IsNullOrEmpty(goboTexturePath) && !UnresolvedTextures.ContainsKey(goboTexturePath))
                            UnresolvedTextures[goboTexturePath] = describeForLog ?? "";
                        features &= ~LightFeature.Gobo;
                    }
                }

                int flags = LightFlagsValue(type, features);
                var key = (flags, TextureKey(gobo));
                if (_lightMaterials.TryGetValue(key, out Materials.Material existing))
                {
                    MaterialsReused++;
                    return existing;
                }

                Materials.Material material = new Materials.Material
                {
                    Name = ClaimName(LightMaterialName(type, features)),
                    Shader = _lightTemplate.Shader,
                    OfflineLightFeatures = new Materials.LightFlags(flags),
                    //Fixed across every light material in the game; taken from the level's own so a
                    //level that disagrees stays self-consistent.
                    PhysicalMaterialIndex = _lightTemplate.PhysicalMaterialIndex,
                    EnvironmentMapIndex = _lightTemplate.EnvironmentMapIndex,
                    Priority = _lightTemplate.Priority,
                };
                if (gobo != null)
                    material.TextureReferences.Add(gobo);

                _level.Materials.Entries.Add(material);
                _lightMaterials[key] = material;
                MaterialsCreated++;
                return material;
            }
        }

        /// <summary>
        /// A renderable run that renders with <paramref name="material"/>, reusing
        /// <paramref name="source"/> untouched when it already does. The source run belongs to the
        /// composite and is shared by every instance of it, so it is copied rather than edited.
        /// </summary>
        public List<RenderableElements.Element> ApplyMaterial(List<RenderableElements.Element> source, Materials.Material material)
        {
            if (material == null || source == null || source.Count == 0)
                return source;
            //The runs this was measured against are a single element - a light's deferred volume, a
            //fog volume's box or sphere. Anything else is a shape we do not know, so leave it be.
            if (source.Count != 1 || source[0] == null || source[0].Model == null)
                return source;
            if (ReferenceEquals(source[0].Material, material))
                return source;

            lock (_lock)
            {
                var key = (source[0].Model, material);
                if (_runs.TryGetValue(key, out List<RenderableElements.Element> cached))
                    return cached;

                //Built field by field rather than copied: Copy() is a deep clone, and the model has
                //to stay the very object the level's model table holds for REDS to index it.
                RenderableElements.Element element = new RenderableElements.Element
                {
                    ModelLocation = source[0].ModelLocation,
                    Model = source[0].Model,
                    ModelSubplatformDependent = source[0].ModelSubplatformDependent,
                    MaterialLocation = PakLocation.LEVEL,
                    Material = material,
                    MaterialSubplatformDependent = false,
                    LODs = new List<RenderableElements.Element>()
                };

                var run = new List<RenderableElements.Element> { element };
                _runs[key] = run;
                return run;
            }
        }

        /// <summary>
        /// Resolve a texture parameter path ("N:\Content\Build\Textures\Gobo\GOBO_Square_01.dds")
        /// against the level's textures and then the global ones, the way the engine's own content
        /// paths are rooted. Returns null if no texture of that name is packed with the level.
        /// </summary>
        public TexturePtr ResolveTexture(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            lock (_lock)
            {
                if (_textures.TryGetValue(path, out TexturePtr cached))
                    return cached;

                string name = NormalisePath(path);
                TexturePtr result = null;
                Textures.TEX4 tex = FindTexture(_level?.Textures, name);
                if (tex != null)
                    result = new TexturePtr { Texture = tex, Location = TexturePtr.Source.LEVEL };
                else
                {
                    tex = FindTexture(_level?.Global?.Textures, name);
                    if (tex != null)
                        result = new TexturePtr { Texture = tex, Location = TexturePtr.Source.GLOBAL };
                }

                _textures[path] = result;
                return result;
            }
        }

        #region SHADER-FEATURE MATERIALS

        // A fog volume or an FX emitter does not pick a material from a list - the build generates
        // one that matches the entity. Two things vary:
        //
        //  * the SHADER, whose ubershader feature mask is the entity's own feature booleans. Exact
        //    on all 662 fog spheres and all 24 fog boxes of ChallengeMap4, once ALPHA is allowed
        //    for: it is set on every fog shader and comes from no parameter. Retail even names the
        //    material after the mask - FOGSPHERE_0000000000000232 sits on the 0x233 permutation.
        //  * the CONSTANTS, which are the ubershader's own named PARAMETERS. Shader.*ParameterRemaps
        //    is indexed BY the PARAMETERS enum value and holds the constant slot that parameter
        //    lives in, 255 meaning this permutation does not use it; the array is padded up to a
        //    multiple of four. Every parameter an emitter entity carries matches its material's
        //    slot on ChallengeMap4 (fog boxes 288 of 288, ribbons 2304 of 2620 with the rest being
        //    the unit conversions below), and the slots left over are ones the entity has no
        //    parameter for, which come from the material being replaced.
        //
        // The hard limit is that a permutation cannot be compiled here: if no shader in the level
        // has the mask an edited entity asks for, the material cannot be built and the caller is
        // told so. Pooling every level's shaders would roughly double the reach (CA_FOGPLANE 15
        // masks in the richest level against 37 across the game, CA_PARTICLE 1158 against 2369).

        /// <summary>
        /// The material for an entity whose features have resolved to <paramref name="features"/>,
        /// built from <paramref name="template"/> - the material it would otherwise have used.
        /// <paramref name="parameterValue"/> is asked for each of the ubershader's named parameters
        /// and returns null for ones the entity does not carry. Returns the template unchanged when
        /// it already matches, and null when the level holds no shader with the required mask.
        /// </summary>
        public Materials.Material GetShaderFeatureMaterial(Materials.Material template, long features, string namePrefix,
                                                           ParameterLookup parameterValue, string describeForLog = null,
                                                           bool allowReuse = true, string nameSuffix = null,
                                                           bool clearOfflineFlags = false)
        {
            if (_level?.Materials?.Entries == null || template?.Shader == null)
                return template;

            long required = features | AlwaysOnFeature(template.Shader.Ubershader);
            bool flagsAlreadyClear = !clearOfflineFlags || (template.OfflineLightFeatures?.Value ?? 0) == 0;
            if (allowReuse && flagsAlreadyClear && template.Shader.UbershaderFeatureFlags == required && !NeedsConstants(template))
                return template;

            lock (_lock)
            {
                Shaders.Shader shader = FindShader(template.Shader.Ubershader, required);
                if (shader == null)
                {
                    if (!UnavailableShaders.ContainsKey(template.Shader.Ubershader + " 0x" + required.ToString("X16")))
                        UnavailableShaders[template.Shader.Ubershader + " 0x" + required.ToString("X16")] = describeForLog ?? "";
                    ShadersNotFound++;
                    return null;
                }

                Materials.Material built = BuildForShader(template, shader, parameterValue);
                if (clearOfflineFlags)
                    built.OfflineLightFeatures = null;
                if (allowReuse)
                {
                    Materials.Material existing = FindEquivalent(built);
                    if (existing != null)
                    {
                        MaterialsReused++;
                        return existing;
                    }
                }

                built.Name = ClaimName(nameSuffix != null ? nameSuffix
                                     : namePrefix == null ? template.Name : namePrefix + features.ToString("x16"));
                _level.Materials.Entries.Add(built);
                _generated.Add(built);
                MaterialsCreated++;
                return built;
            }
        }

        /// <summary>Feature masks an entity asked for that no shader in this level provides.</summary>
        public readonly Dictionary<string, string> UnavailableShaders = new Dictionary<string, string>(StringComparer.Ordinal);
        public int ShadersNotFound { get; private set; }

        private readonly List<Materials.Material> _generated = new List<Materials.Material>();

        /// <summary>
        /// Rebuilds a material against a shader: the constant arrays are laid out by that shader's
        /// own remaps, each slot taking the entity's value for the parameter it names, or the
        /// template's value for that same parameter when the entity has none.
        /// </summary>
        public static Materials.Material BuildForShader(Materials.Material template, Shaders.Shader shader, ParameterLookup parameterValue)
        {
            Materials.Material built = new Materials.Material
            {
                Name = template.Name,
                Shader = shader,
                OfflineLightFeatures = template.OfflineLightFeatures == null ? null : new Materials.LightFlags(template.OfflineLightFeatures.Value),
                PhysicalMaterialIndex = template.PhysicalMaterialIndex,
                EnvironmentMapIndex = template.EnvironmentMapIndex,
                Priority = template.Priority,
            };
            foreach (TexturePtr tex in template.TextureReferences)
                built.TextureReferences.Add(new TexturePtr { Texture = tex.Texture, Location = tex.Location });

            built.EngineConstants = BuildArray(template, shader, 0, parameterValue);
            built.VertexShaderConstants = BuildArray(template, shader, 1, parameterValue);
            built.PixelShaderConstants = BuildArray(template, shader, 2, parameterValue);
            built.HullShaderConstants = BuildArray(template, shader, 3, parameterValue);
            built.DomainShaderConstants = BuildArray(template, shader, 4, parameterValue);
            return built;
        }

        private static List<float> BuildArray(Materials.Material template, Shaders.Shader shader, int which, ParameterLookup parameterValue)
        {
            List<int> remaps = Remaps(shader, which);
            if (remaps == null || remaps.Count == 0)
                return new List<float>();

            int highest = -1;
            for (int p = 0; p < remaps.Count; p++)
            {
                if (remaps[p] == UnusedSlot) continue;
                int end = remaps[p] + ParameterWidth(shader.Ubershader, p) - 1;
                if (end > highest) highest = end;
            }
            if (highest < 0)
                return new List<float>();

            //Constant arrays are padded up to a multiple of four - a shader whose highest slot is 45
            //ships 48 of them, one whose highest is 4 ships 8.
            int count = ((highest / 4) + 1) * 4;
            float[] values = new float[count];
            for (int p = 0; p < remaps.Count; p++)
            {
                int slot = remaps[p];
                if (slot == UnusedSlot || slot >= count) continue;
                int width = ParameterWidth(shader.Ubershader, p);
                string name = ParameterName(shader.Ubershader, p);
                float[] v = name == null ? null : parameterValue?.Invoke(name, width);
                float[] fromTemplate = TemplateValue(template, p, which, width);
                if (v == null) v = fromTemplate;
                //Where the entity restates what the material already holds, keep the material's own
                //float. Recomputing it (20 * 0.01f, or degrees through 1/360) lands a bit or two
                //away from the stored value, and a material that differs only by 1e-7 would never
                //dedupe against the one it was built from - every unedited emitter would get a
                //pointless copy of its own material.
                else if (fromTemplate != null)
                    for (int i = 0; i < width && i < v.Length && i < fromTemplate.Length; i++)
                        if (v[i] != fromTemplate[i] && Math.Abs(v[i] - fromTemplate[i]) <= 1e-5f * Math.Max(1.0f, Math.Abs(fromTemplate[i])))
                            v[i] = fromTemplate[i];
                for (int i = 0; i < width && slot + i < count; i++)
                    values[slot + i] = v != null && i < v.Length ? v[i] : 0.0f;
            }
            return new List<float>(values);
        }

        private static float[] TemplateValue(Materials.Material template, int parameterIndex, int which, int width)
        {
            List<int> remaps = Remaps(template?.Shader, which);
            List<float> constants = Constants(template, which);
            if (remaps == null || constants == null || parameterIndex >= remaps.Count) return null;
            int slot = remaps[parameterIndex];
            if (slot == UnusedSlot || slot >= constants.Count) return null;
            float[] v = new float[width];
            for (int i = 0; i < width; i++)
                v[i] = slot + i < constants.Count ? constants[slot + i] : 0.0f;
            return v;
        }

        /// <summary>Asked for the ubershader's value of a named parameter; null if the entity has none.</summary>
        public delegate float[] ParameterLookup(string parameterName, int width);

        private static readonly Dictionary<CATHODE.ShaderTypes.SHADER_LIST, Dictionary<int, int>> _parameterWidths =
            new Dictionary<CATHODE.ShaderTypes.SHADER_LIST, Dictionary<int, int>>();

        /// <summary>
        /// How many constant slots a parameter occupies. The ubershader classes each carry a
        /// GetParameterType, and a Float3/Float4 one takes that many consecutive slots - which is
        /// why a colour looks like three unexplained constants sitting after a named one.
        /// </summary>
        public static int ParameterWidth(CATHODE.ShaderTypes.SHADER_LIST ubershader, int index)
        {
            lock (_parameterWidths)
            {
                if (!_parameterWidths.TryGetValue(ubershader, out Dictionary<int, int> map))
                {
                    map = new Dictionary<int, int>();
                    Type shaderClass = typeof(CATHODE.ShaderTypes.SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + ubershader);
                    Type parameters = shaderClass?.GetNestedType("PARAMETERS");
                    System.Reflection.MethodInfo getType = shaderClass?.GetMethod("GetParameterType");
                    if (parameters != null && parameters.IsEnum && getType != null)
                    {
                        foreach (object v in Enum.GetValues(parameters))
                        {
                            int width = 1;
                            try
                            {
                                switch ((CATHODE.ShaderTypes.UberShaderParameterType)getType.Invoke(null, new object[] { v }))
                                {
                                    case CATHODE.ShaderTypes.UberShaderParameterType.Float2:
                                    case CATHODE.ShaderTypes.UberShaderParameterType.Half2: width = 2; break;
                                    case CATHODE.ShaderTypes.UberShaderParameterType.Float3:
                                    case CATHODE.ShaderTypes.UberShaderParameterType.Half3: width = 3; break;
                                    case CATHODE.ShaderTypes.UberShaderParameterType.Float4:
                                    case CATHODE.ShaderTypes.UberShaderParameterType.Half4: width = 4; break;
                                }
                            }
                            catch { /* the class throws for parameters it does not describe */ }
                            map[Convert.ToInt32(v)] = width;
                        }
                    }
                    _parameterWidths[ubershader] = map;
                }
                return map.TryGetValue(index, out int w) ? w : 1;
            }
        }

        private const int UnusedSlot = 255;

        private static List<int> Remaps(Shaders.Shader s, int which)
        {
            if (s == null) return null;
            switch (which)
            {
                case 0: return s.EngineParameterRemaps;
                case 1: return s.VertexShaderParameterRemaps;
                case 2: return s.PixelShaderParameterRemaps;
                case 3: return s.HullShaderParameterRemaps;
                default: return s.DomainShaderParameterRemaps;
            }
        }

        private static List<float> Constants(Materials.Material m, int which)
        {
            if (m == null) return null;
            switch (which)
            {
                case 0: return m.EngineConstants;
                case 1: return m.VertexShaderConstants;
                case 2: return m.PixelShaderConstants;
                case 3: return m.HullShaderConstants;
                default: return m.DomainShaderConstants;
            }
        }

        /// <summary>
        /// Features every shader of an ubershader carries regardless of the entity. ALPHA is the
        /// only one measured so far, and it is set on every fog shader in the game.
        /// </summary>
        public static long AlwaysOnFeature(CATHODE.ShaderTypes.SHADER_LIST ubershader)
        {
            switch (ubershader)
            {
                case CATHODE.ShaderTypes.SHADER_LIST.CA_FOGSPHERE: return 1L << (int)CATHODE.ShaderTypes.CA_FOGSPHERE.FEATURES.ALPHA;
                case CATHODE.ShaderTypes.SHADER_LIST.CA_FOGPLANE: return 1L << (int)CATHODE.ShaderTypes.CA_FOGPLANE.FEATURES.ALPHA;
                default: return 0;
            }
        }

        private Shaders.Shader FindShader(CATHODE.ShaderTypes.SHADER_LIST ubershader, long features)
        {
            if (_level?.Shaders?.Entries == null) return null;
            foreach (Shaders.Shader s in _level.Shaders.Entries)
                if (s != null && s.Ubershader == ubershader && s.UbershaderFeatureFlags == features)
                    return s;
            return null;
        }

        private static bool NeedsConstants(Materials.Material m)
        {
            return m.EngineConstants.Count != 0 || m.VertexShaderConstants.Count != 0 || m.PixelShaderConstants.Count != 0;
        }

        private Materials.Material FindEquivalent(Materials.Material built)
        {
            foreach (Materials.Material m in _level.Materials.Entries)
                if (SameContent(m, built)) return m;
            return null;
        }

        /// <summary>Everything the engine reads off a material, name excluded.</summary>
        public static bool SameContent(Materials.Material a, Materials.Material b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (!ReferenceEquals(a.Shader, b.Shader)) return false;
            if ((a.OfflineLightFeatures?.Value ?? 0) != (b.OfflineLightFeatures?.Value ?? 0)) return false;
            if (a.PhysicalMaterialIndex != b.PhysicalMaterialIndex || a.EnvironmentMapIndex != b.EnvironmentMapIndex || a.Priority != b.Priority) return false;
            if (a.TextureReferences.Count != b.TextureReferences.Count) return false;
            for (int i = 0; i < a.TextureReferences.Count; i++)
                if (a.TextureReferences[i].Location != b.TextureReferences[i].Location ||
                    !ReferenceEquals(a.TextureReferences[i].Texture, b.TextureReferences[i].Texture)) return false;
            for (int i = 0; i < 5; i++)
                if (!SameFloats(Constants(a, i), Constants(b, i))) return false;
            return true;
        }

        private static bool SameFloats(List<float> a, List<float> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static readonly Dictionary<CATHODE.ShaderTypes.SHADER_LIST, Dictionary<int, string>> _parameterNames =
            new Dictionary<CATHODE.ShaderTypes.SHADER_LIST, Dictionary<int, string>>();

        /// <summary>The ubershader's name for the parameter at this index, or null if it has none.</summary>
        public static string ParameterName(CATHODE.ShaderTypes.SHADER_LIST ubershader, int index)
        {
            lock (_parameterNames)
            {
                if (!_parameterNames.TryGetValue(ubershader, out Dictionary<int, string> map))
                {
                    map = new Dictionary<int, string>();
                    Type shaderClass = typeof(CATHODE.ShaderTypes.SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + ubershader);
                    Type parameters = shaderClass?.GetNestedType("PARAMETERS");
                    if (parameters != null && parameters.IsEnum)
                        foreach (object v in Enum.GetValues(parameters))
                            map[Convert.ToInt32(v)] = v.ToString();
                    _parameterNames[ubershader] = map;
                }
                return map.TryGetValue(index, out string n) ? n : null;
            }
        }

        /// <summary>
        /// What a vector parameter has to be multiplied by. Only colours are ever rescaled, and only
        /// on the fog and overlay ubershaders, which take them authored 0-255 like the rest of the
        /// editor; the FX ones store their tints 0-1 already. Measured on ChallengeMap4: every
        /// particle and ribbon tint round trips at 1.0, every fog box DEPTH_INTERSECT colour and
        /// every surface effect COLOUR_TINT at 1/255, and a surface effect FALLOFF unscaled.
        /// </summary>

        /// <summary>

        /// <summary>
        /// True when a parameter the entity does carry should still be left to the material being
        /// replaced, judging by the value itself.
        /// </summary>
        /// <remarks>
        /// An all-zero colour is not a black tint, it is an unset one: a surface effect sphere whose
        /// COLOUR_TINT reads &lt;0,0,0&gt; ships with 1,1,1 in the material, and a genuinely black tint
        /// would make the effect invisible. Seven of Solace's eight surface effect spheres are this
        /// case; the ones that author a real colour (&lt;255,255,255&gt;) rebuild exactly.
        /// </remarks>
        public static bool TreatAsUnauthored(CATHODE.ShaderTypes.SHADER_LIST ubershader, string parameterName, float[] value)
        {
            if (value == null || value.Length < 3 || parameterName == null)
                return false;
            if (parameterName.IndexOf("COLOUR", StringComparison.Ordinal) < 0 &&
                parameterName.IndexOf("TINT", StringComparison.Ordinal) < 0)
                return false;
            return value[0] == 0.0f && value[1] == 0.0f && value[2] == 0.0f;
        }
        /// True for a parameter that the entity authors but retail never writes into the material,
        /// so the material being replaced keeps its own value.
        /// </summary>
        /// <remarks>
        /// Only one so far: a surface effect's FALLOFF. Every CA_EFFECT_OVERLAY material retail
        /// ships holds 1,1,1 in FALLOFF's three slots whatever the entity says - 120 of Solace's
        /// 125 surface effect boxes author 0.05 and get 1, and ChallengeMap4's two author
        /// &lt;0.5,0.5,1&gt; and get 1,1,1 - while every other constant in the block rebuilds exactly.
        /// So the shader declares the parameter and the runtime must supply it from somewhere else.
        /// Writing the authored value instead is what made these the last material type we could
        /// not generate.
        /// </remarks>
        public static bool NotBakedIntoMaterial(CATHODE.ShaderTypes.SHADER_LIST ubershader, string parameterName)
        {
            return ubershader == CATHODE.ShaderTypes.SHADER_LIST.CA_EFFECT_OVERLAY &&
                   string.Equals(parameterName, "FALLOFF", StringComparison.Ordinal);
        }
        public static float VectorScale(CATHODE.ShaderTypes.SHADER_LIST ubershader, string parameterName)
        {
            if (parameterName == null) return 1.0f;
            //Only the colours - a fog box's FALLOFF is a shape, and stays as authored.
            if (parameterName.IndexOf("COLOUR", StringComparison.Ordinal) < 0 &&
                parameterName.IndexOf("TINT", StringComparison.Ordinal) < 0)
                return 1.0f;
            switch (ubershader)
            {
                case CATHODE.ShaderTypes.SHADER_LIST.CA_FOGPLANE:
                case CATHODE.ShaderTypes.SHADER_LIST.CA_FOGSPHERE:
                case CATHODE.ShaderTypes.SHADER_LIST.CA_EFFECT_OVERLAY:
                    return 1.0f / 255.0f;
                default:
                    return 1.0f;
            }
        }

        /// <summary>
        /// An entity parameter is not always stored in the units the shader constant wants. Every
        /// conversion here was read off retail's own materials on ChallengeMap4 - percentages, and
        /// angles held in turns.
        /// </summary>
        public static float ConvertParameter(CATHODE.ShaderTypes.SHADER_LIST ubershader, string parameterName, float value)
        {
            //Gravity is only scaled on the particle ubershader - a ribbon stores it raw.
            if ((parameterName == "GRAVITY_STRENGTH" || parameterName == "GRAVITY_MAX_STRENGTH") &&
                ubershader != CATHODE.ShaderTypes.SHADER_LIST.CA_PARTICLE)
                return value;
            switch (parameterName)
            {
                case "ALPHA_IN":
                case "ALPHA_OUT":
                    return value * 0.01f;
                case "SPREAD":
                case "SPREAD_MIN":
                    return value / 360.0f;
                case "ROTATION_BASE":
                case "ROTATION_VAR":
                case "ROTATION_MIN":
                case "ROTATION_MAX":
                    return value * 2.0f * (float)Math.PI;
                case "GRAVITY_STRENGTH":
                case "GRAVITY_MAX_STRENGTH":
                    return value * 4.906f;
                default:
                    return value;
            }
        }

        #endregion

        private static Textures.TEX4 FindTexture(Textures textures, string normalisedName)
        {
            if (textures?.Entries == null)
                return null;
            foreach (Textures.TEX4 tex in textures.Entries)
                if (tex?.Name != null && NormalisePath(tex.Name) == normalisedName)
                    return tex;
            return null;
        }

        private static string NormalisePath(string path)
        {
            string normalised = path.ToUpperInvariant().Replace('/', '\\');
            const string root = "CONTENT\\BUILD\\TEXTURES\\";
            int index = normalised.IndexOf(root, StringComparison.Ordinal);
            if (index >= 0)
                normalised = normalised.Substring(index + root.Length);
            return normalised;
        }

        private static string TextureKey(TexturePtr texture)
        {
            if (texture?.Texture == null)
                return "";
            return (int)texture.Location + "|" + NormalisePath(texture.Texture.Name ?? "");
        }

        //Retail appends a zero-padded six digit index when a name is taken, starting at 000000.
        private string ClaimName(string baseName)
        {
            if (_names.Add(baseName))
                return baseName;

            _nextVariant.TryGetValue(baseName, out int next);
            string name;
            do
            {
                name = baseName + "[" + next.ToString("000000") + "]";
                next++;
            }
            while (!_names.Add(name));
            _nextVariant[baseName] = next;
            return name;
        }
    }
}
#endif
