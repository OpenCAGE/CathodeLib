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
            if (compositeInstanceEntity?.Resources == null)
                return false;

            InstancedEntity.Parameters<cResource> resources = compositeInstanceEntity.Resources;
            bool hasValue = resources.Has(ShortGuids.mapping);
            bool hasLink = resources.Links != null && resources.Links.ContainsKey(ShortGuids.mapping);
            if (!hasValue && !hasLink)
                return false;

            mapping = resources.Get(ShortGuids.mapping);
            return mapping != null && mapping.shortGUID != ShortGuid.Invalid;
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
                    remappedLods = null;
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
                Materials.Material remapped = FindMaterialByName(level.Materials, targetName);
                return remapped ?? material;
            }

            MaterialMappings.MaterialMapping.Mapping reverseEntry = FindMappingEntryByTarget(mapping, material.Name);
            if (reverseEntry != null && TryFindMappingTarget(mapping, reverseEntry.from, out string refreshedTargetName))
            {
                Materials.Material remapped = FindMaterialByName(level.Materials, refreshedTargetName);
                return remapped ?? material;
            }

            return material;
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
            if (materials?.Entries == null || string.IsNullOrEmpty(name))
                return null;

            Materials.Material exact = materials.Entries.FirstOrDefault(material => material.Name == name);
            if (exact != null)
                return exact;

            string normalizedName = NormalizeMaterialNameForLookup(name);
            return materials.Entries.FirstOrDefault(material => material != null && !string.IsNullOrEmpty(material.Name) && NormalizeMaterialNameForLookup(material.Name) == normalizedName);
        }
    }
}
#endif
