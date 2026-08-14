using CATHODE;
using CATHODE.Scripting;
using CathodeLib.ObjectExtensions;
using System;
using System.Collections.Generic;
using System.Linq;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib
{
    public static class MaterialRemappingUtils
    {
        public static MaterialMappings.MaterialMapping TryResolveMaterialMapping(Level level, cResource mappingResource)
        {
            if (level?.MaterialMappings?.Entries == null || mappingResource == null)
                return null;
            if (mappingResource.shortGUID == ShortGuid.Invalid)
                return null;

            return level.MaterialMappings.Entries.FirstOrDefault(entry => entry.ID == mappingResource.shortGUID);
        }

        public static MaterialMappings.MaterialMapping TryResolveMappingForModelReference(Level level, InstancedEntity modelReference)
        {
            if (level == null || modelReference?.ParentCompositeInstanceEntity == null)
                return null;

            if (!TryGetMappingResource(modelReference.ParentCompositeInstanceEntity, out cResource mappingResource))
                return null;

            return TryResolveMaterialMapping(level, mappingResource);
        }

        public static bool TryGetMappingResource(InstancedEntity compositeInstanceEntity, out cResource mapping)
        {
            mapping = null;
            if (compositeInstanceEntity == null)
                return false;

            if (compositeInstanceEntity.Resources != null)
            {
                InstancedEntity.Parameters<cResource> resources = compositeInstanceEntity.Resources;
                bool hasValue = resources.Has(ShortGuids.mapping);
                bool hasLink = resources.Links != null && resources.Links.ContainsKey(ShortGuids.mapping);
                if (hasValue || hasLink)
                {
                    mapping = resources.Get(ShortGuids.mapping);
                    if (mapping != null && mapping.shortGUID != ShortGuid.Invalid)
                        return true;
                }
            }

            Parameter parameter = compositeInstanceEntity.Entity?.GetParameter(ShortGuids.mapping);
            if (parameter?.content is cResource resource && resource.shortGUID != ShortGuid.Invalid)
            {
                mapping = resource;
                return true;
            }

            return false;
        }

        public static List<RenderableElements.Element> ApplyMapping(Level level, MaterialMappings.MaterialMapping mapping, IReadOnlyList<RenderableElements.Element> source)
        {
            List<RenderableElements.Element> result = new List<RenderableElements.Element>();
            if (source == null || source.Count == 0)
                return result;

            if (mapping == null || level?.Materials == null)
            {
                for (int i = 0; i < source.Count; i++)
                    result.Add(source[i]);
                return result;
            }

            for (int i = 0; i < source.Count; i++)
            {
                RenderableElements.Element element = source[i];
                if (element == null)
                    continue;

                result.Add(RemapElement(level, mapping, element));
            }

            return result;
        }

        public static RenderableElements.Element RemapElement(Level level, MaterialMappings.MaterialMapping mapping, RenderableElements.Element element)
        {
            if (element == null)
                return null;

            Materials.Material remappedMaterial = RemapMaterial(level, mapping, element.Material);
            bool materialChanged = !ReferenceEquals(remappedMaterial, element.Material);

            List<RenderableElements.Element> remappedLods = null;
            if (element.LODs != null && element.LODs.Count > 0)
            {
                remappedLods = new List<RenderableElements.Element>(element.LODs.Count);
                bool lodsChanged = false;
                for (int i = 0; i < element.LODs.Count; i++)
                {
                    RenderableElements.Element lod = element.LODs[i];
                    RenderableElements.Element remappedLod = RemapElement(level, mapping, lod);
                    remappedLods.Add(remappedLod);
                    if (!ReferenceEquals(remappedLod, lod))
                        lodsChanged = true;
                }

                if (!lodsChanged)
                {
                    remappedLods = null;
                }
                else
                {
                    for (int i = 0; i < remappedLods.Count; i++)
                    {
                        if (ReferenceEquals(remappedLods[i], element.LODs[i]))
                            remappedLods[i] = element.LODs[i].Copy();
                    }
                }
            }

            if (!materialChanged && remappedLods == null)
                return element;

            RenderableElements.Element copy = element.Copy();
            if (materialChanged)
                copy.Material = remappedMaterial;
            if (remappedLods != null)
                copy.LODs = remappedLods;
            return copy;
        }

        public static Materials.Material RemapMaterial(Level level, MaterialMappings.MaterialMapping mapping, Materials.Material material)
        {
            if (level?.Materials == null || mapping == null || material == null || string.IsNullOrEmpty(material.Name))
                return material;

            if (TryFindMappingTarget(mapping, material.Name, out string targetName))
            {
                Materials.Material remapped = FindMaterialByName(level.Materials, targetName, material, level.Models);
                return KeepIfRadiosityCompatible(remapped, material);
            }

            MaterialMappings.MaterialMapping.Mapping reverseEntry = FindMappingEntryByTarget(mapping, material.Name);
            if (reverseEntry != null && TryFindMappingTarget(mapping, reverseEntry.from, out string refreshedTargetName))
            {
                Materials.Material remapped = FindMaterialByName(level.Materials, refreshedTargetName, material, level.Models);
                return KeepIfRadiosityCompatible(remapped, material);
            }

            return material;
        }

        private const long RadiosityStaticBit = 1L << (int)CATHODE.ShaderTypes.SHADER_REQUIREMENTS.RADIOSITY_STATIC;
        private const long RadiosityDynamicBit = 1L << (int)CATHODE.ShaderTypes.SHADER_REQUIREMENTS.RADIOSITY_DYNAMIC;

        private static long RadiosityClass(Materials.Material material) => (material?.Shader?.UbershaderRequirementFlags ?? 0) & (RadiosityStaticBit | RadiosityDynamicBit);

        private static readonly Dictionary<CATHODE.ShaderTypes.SHADER_LIST, int> _alphaLightingBit = new Dictionary<CATHODE.ShaderTypes.SHADER_LIST, int>();

        private static int GetAlphaLightingBit(CATHODE.ShaderTypes.SHADER_LIST ubershader)
        {
            lock (_alphaLightingBit)
            {
                if (_alphaLightingBit.TryGetValue(ubershader, out int cached))
                    return cached;

                int bit = -1;
                Type shaderClass = typeof(CATHODE.ShaderTypes.SHADER_LIST).Assembly
                    .GetType("CATHODE.ShaderTypes." + ubershader);
                Type features = shaderClass?.GetNestedType("FEATURES");
                if (features != null && features.IsEnum && Enum.IsDefined(features, "ALPHA_LIGHTING"))
                    bit = Convert.ToInt32(Enum.Parse(features, "ALPHA_LIGHTING"));

                _alphaLightingBit[ubershader] = bit;
                return bit;
            }
        }

        private static bool AlphaLightingMatches(Materials.Material a, Materials.Material b)
        {
            Shaders.Shader sa = a?.Shader, sb = b?.Shader;
            if (sa == null || sb == null)
                return true;
            // Feature bits are only comparable within the same ubershader.
            if (sa.Ubershader != sb.Ubershader)
                return true;

            int bit = GetAlphaLightingBit(sa.Ubershader);
            if (bit < 0)
                return true;

            long mask = 1L << bit;
            return (sa.UbershaderFeatureFlags & mask) == (sb.UbershaderFeatureFlags & mask);
        }

        private static Materials.Material KeepIfRadiosityCompatible(Materials.Material remapped, Materials.Material original)
        {
            if (remapped == null)
                return original;
            if (RadiosityClass(remapped) != RadiosityClass(original))
                return original;
            if (!AlphaLightingMatches(remapped, original))
                return original;
            return remapped;
        }

        public static List<RenderableElements.Element> ApplyMaterialParameterOverride(Level level, string materialName, List<RenderableElements.Element> renderables)
        {
            if (level?.Materials == null || renderables == null || renderables.Count != 1)
                return renderables;
            if (string.IsNullOrWhiteSpace(materialName))
                return renderables;

            Materials.Material material = FindMaterialByName(level.Materials, materialName);
            if (material == null)
                return renderables;

            RenderableElements.Element copy = renderables[0].Copy();
            copy.Material = material;
            if (copy.LODs != null)
            {
                for (int i = 0; i < copy.LODs.Count; i++)
                {
                    if (copy.LODs[i] == null)
                        continue;
                    RenderableElements.Element lodCopy = copy.LODs[i];
                    lodCopy.Material = material;
                }
            }
            return new List<RenderableElements.Element> { copy };
        }

        public static bool TryFindMappingTarget(MaterialMappings.MaterialMapping mapping, string materialName, out string targetName)
        {
            targetName = null;
            if (mapping?.Mappings == null || string.IsNullOrEmpty(materialName))
                return false;

            MaterialMappings.MaterialMapping.Mapping remap = FindMappingEntry(mapping, materialName);
            if (remap == null)
                return false;

            targetName = remap.to;
            return !string.IsNullOrEmpty(targetName);
        }

        private static MaterialMappings.MaterialMapping.Mapping FindMappingEntry(MaterialMappings.MaterialMapping mapping, string materialName)
        {
            MaterialMappings.MaterialMapping.Mapping remap = mapping.Mappings.FirstOrDefault(entry => entry.from == materialName);
            if (remap != null)
                return remap;

            string normalizedMaterialName = NormalizeMaterialNameForLookup(materialName);
            for (int i = 0; i < mapping.Mappings.Count; i++)
            {
                MaterialMappings.MaterialMapping.Mapping entry = mapping.Mappings[i];
                if (entry == null || string.IsNullOrEmpty(entry.from))
                    continue;

                if (NormalizeMaterialNameForLookup(entry.from) == normalizedMaterialName)
                    return entry;
            }

            return null;
        }

        private static MaterialMappings.MaterialMapping.Mapping FindMappingEntryByTarget(MaterialMappings.MaterialMapping mapping, string targetMaterialName)
        {
            if (mapping?.Mappings == null || string.IsNullOrEmpty(targetMaterialName))
                return null;

            MaterialMappings.MaterialMapping.Mapping remap = mapping.Mappings.FirstOrDefault(entry => entry.to == targetMaterialName);
            if (remap != null)
                return remap;

            string normalizedTargetName = NormalizeMaterialNameForLookup(targetMaterialName);
            for (int i = 0; i < mapping.Mappings.Count; i++)
            {
                MaterialMappings.MaterialMapping.Mapping entry = mapping.Mappings[i];
                if (entry == null || string.IsNullOrEmpty(entry.to))
                    continue;

                if (NormalizeMaterialNameForLookup(entry.to) == normalizedTargetName)
                    return entry;
            }

            return null;
        }

        public static string NormalizeMaterialNameForLookup(string materialName)
        {
            if (string.IsNullOrEmpty(materialName))
                return string.Empty;

            string normalized = StripTrailingVariantSuffix(materialName).ToUpperInvariant();
            if (normalized.IndexOf("->", StringComparison.Ordinal) < 0)
                normalized += "->" + normalized;

            return normalized;
        }

        private static string StripTrailingVariantSuffix(string name)
        {
            if (string.IsNullOrEmpty(name) || name[name.Length - 1] != ']')
                return name;

            int open = name.LastIndexOf('[');
            if (open <= 0)
                return name;

            return name.Substring(0, open);
        }

        public static Materials.Material FindMaterialByName(Materials materials, string name)
        {
            return FindMaterialByName(materials, name, null);
        }

        public static Materials.Material FindMaterialByName(Materials materials, string name, Materials.Material preferLike)
        {
            return FindMaterialByName(materials, name, preferLike, null);
        }

        public static Materials.Material FindMaterialByName(Materials materials, string name, Materials.Material preferLike, Models models)
        {
            if (materials?.Entries == null || string.IsNullOrEmpty(name))
                return null;

            long wanted = RadiosityClass(preferLike);
            HashSet<string> wantedFormats = models != null ? VertexFormatsFor(models, preferLike) : null;

            Materials.Material fallback = null;
            Materials.Material flagMatch = null;

            foreach (Materials.Material material in MatchesByName(materials, name))
            {
                fallback ??= material;
                if (preferLike == null)
                    return material;

                if (RadiosityClass(material) != wanted || !AlphaLightingMatches(material, preferLike))
                    continue;
                flagMatch ??= material;

                // Best case: flags line up and the candidate is known to work with the same
                // vertex layout the original was authored against.
                if (wantedFormats == null || wantedFormats.Count == 0)
                    return material;
                HashSet<string> candidateFormats = VertexFormatsFor(models, material);
                if (candidateFormats.Count == 0 || candidateFormats.Overlaps(wantedFormats))
                    return material;
            }

            return flagMatch ?? fallback;
        }

        private static IEnumerable<Materials.Material> MatchesByName(Materials materials, string name)
        {
            foreach (Materials.Material material in materials.Entries)
                if (material?.Name == name)
                    yield return material;

            string normalized = NormalizeMaterialNameForLookup(name);
            foreach (Materials.Material material in materials.Entries)
            {
                if (material == null || string.IsNullOrEmpty(material.Name) || material.Name == name)
                    continue;
                if (NormalizeMaterialNameForLookup(material.Name) == normalized)
                    yield return material;
            }
        }

        private static readonly Dictionary<Models, Dictionary<Materials.Material, HashSet<string>>> _vertexFormatCache = new Dictionary<Models, Dictionary<Materials.Material, HashSet<string>>>();

        private static HashSet<string> VertexFormatsFor(Models models, Materials.Material material)
        {
            if (models == null || material == null)
                return new HashSet<string>();

            Dictionary<Materials.Material, HashSet<string>> map;
            lock (_vertexFormatCache)
            {
                if (!_vertexFormatCache.TryGetValue(models, out map))
                {
                    map = new Dictionary<Materials.Material, HashSet<string>>();
                    foreach (Models.CS2 cs2 in models.Entries)
                        foreach (Models.CS2.Component component in cs2.Components)
                            foreach (Models.CS2.Component.LOD lod in component.LODs)
                                foreach (Models.CS2.Component.LOD.Submesh submesh in lod.Submeshes)
                                {
                                    if (submesh?.Material == null)
                                        continue;
                                    if (!map.TryGetValue(submesh.Material, out HashSet<string> set))
                                        map[submesh.Material] = set = new HashSet<string>();
                                    set.Add(DescribeVertexFormat(submesh.VertexFormatFull));
                                }
                    _vertexFormatCache[models] = map;
                }
            }

            return map.TryGetValue(material, out HashSet<string> formats) ? formats : new HashSet<string>();
        }

        private static string DescribeVertexFormat(Models.VertexFormat format)
        {
            if (format?.Attributes == null)
                return "";
            var sb = new System.Text.StringBuilder();
            foreach (List<Models.VertexFormat.Attribute> group in format.Attributes)
            {
                sb.Append('|');
                if (group == null) continue;
                foreach (Models.VertexFormat.Attribute attribute in group)
                    sb.Append((int)attribute.Usage).Append(':').Append((int)attribute.Type).Append(':').Append(attribute.Index).Append(',');
            }
            return sb.ToString();
        }
    }
}
#endif
