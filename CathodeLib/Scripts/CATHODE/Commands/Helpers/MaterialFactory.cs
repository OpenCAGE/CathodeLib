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
        /// A renderable run for a light that renders with <paramref name="material"/>, reusing
        /// <paramref name="source"/> untouched when it already does. The source run belongs to the
        /// composite and is shared by every instance of it, so it is copied rather than edited.
        /// </summary>
        public List<RenderableElements.Element> ApplyLightMaterial(List<RenderableElements.Element> source, Materials.Material material)
        {
            if (material == null || source == null || source.Count == 0)
                return source;
            //A light's renderable run is a single element - the deferred volume mesh. Anything else
            //is not a shape this was measured against, so leave it alone.
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
