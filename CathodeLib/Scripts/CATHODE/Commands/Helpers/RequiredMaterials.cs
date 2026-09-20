using CATHODE;
using System;
using System.Collections.Generic;
using System.IO;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib
{
    /// <summary>
    /// The materials every level is required to carry, at the head of its material table.
    ///
    /// These are the engine's own: the post-processing composite, bloom, distortion and lens flare
    /// passes, the fog and surface-effect volumes, water, refraction, caustics, the deferred light
    /// proxy, the particle cube - and FALLBACK_MATERIAL at index 0, which is what anything without a
    /// material of its own is drawn with. No model references most of them, so nothing brings them
    /// into a level built from scratch by porting content in, and without them the engine has no
    /// shader to finish a frame with: the HUD draws, the world stays black.
    ///
    /// Measured across every production and DLC level: the same fifteen names, in the same order, at
    /// indices 0-14 of every one, each with its own shader at the same index of the shader pak
    /// (FALLBACK_MATERIAL = shader 0, POST_PROCESSING_MATERIAL = shader 1...). Retail treats the block
    /// as positional the way it does REQUIRED_MODEL_* (see <see cref="RequiredModels"/>), so a level
    /// carries it at the head and in this order, whatever else it holds.
    /// </summary>
    public static class RequiredMaterials
    {
        //Names as retail spells them, in the order they occupy the head of the table
        public static readonly string[] Names =
        {
            "FALLBACK_MATERIAL",
            "POST_PROCESSING_MATERIAL",
            "BLOOM_GATHER_MATERIAL",
            "DISTORTION_OVERLAY_MATERIAL",
            "LENS_FLARE_MATERIAL",
            "FOGSPHERE_MATERIAL",
            "FOGBOX_MATERIAL",
            "SURFACE_EFFECT_SPHERE_MATERIAL",
            "SURFACE_EFFECT_BOX_MATERIAL",
            "SURFACE_EFFECT_FULLSCREEN_MATERIAL",
            "WATER_MATERIAL",
            "SIMPLE_REFRACTION_MATERIAL",
            "CAUSTIC_MATERIAL",
            "POINT_LIGHT_MATERIAL",
            "1000_PARTICLE_CUBE",
        };

        /// <summary>How many entries the required block occupies.</summary>
        public static int EntryCount => Names.Length;

        /// <summary>Is this entry part of the required block, and so not the user's to remove?</summary>
        public static bool IsRequiredEntry(Materials.Material entry)
        {
            if (entry == null || entry.Name == null) return false;
            return Array.IndexOf(Names, entry.Name) >= 0;
        }

        /// <summary>The first entry carrying this name, or null when the level does not have it.</summary>
        public static Materials.Material Find(Materials materials, string name)
        {
            if (materials?.Entries == null) return null;
            for (int i = 0; i < materials.Entries.Count; i++)
            {
                if (materials.Entries[i] != null && string.Equals(materials.Entries[i].Name, name, StringComparison.Ordinal))
                    return materials.Entries[i];
            }
            return null;
        }

        /// <summary>The names of the block this level does not carry.</summary>
        public static List<string> Missing(Materials materials)
        {
            List<string> missing = new List<string>();
            foreach (string name in Names)
                if (Find(materials, name) == null)
                    missing.Add(name);
            return missing;
        }

        /// <summary>
        /// Bring the block across from another level's table, with the shaders and textures each one
        /// carries, in canonical order. Entries the destination already has are left as they are. Meant
        /// to run first on an empty table, so the block lands at 0-14 with its shaders at 0-14 of the
        /// shader pak, the way retail ships it. Returns the names <paramref name="from"/> did not have.
        /// </summary>
        public static List<string> Import(Materials into, Materials from)
        {
            List<string> missing = new List<string>();
            if (into == null || from == null) return missing;
            foreach (string name in Names)
            {
                if (Find(into, name) != null) continue;
                Materials.Material source = Find(from, name);
                if (source == null) { missing.Add(name); continue; }
                into.ImportEntry(source);
            }
            return missing;
        }

        /// <summary>
        /// Just the material table of a level folder - with the shaders and textures its entries point
        /// at, which is all a donor needs - without loading the level. For bringing the block into a
        /// level made before it was seeded, from a retail folder such as FRONTEND.
        /// </summary>
        public static Materials LoadTable(string levelFolder, Global global)
        {
            if (string.IsNullOrEmpty(levelFolder) || global?.Textures == null) return null;
            string renderable = levelFolder.Replace("\\", "/").TrimEnd('/') + "/RENDERABLE/";
            if (!File.Exists(renderable + "LEVEL_MODELS.MTL") && !File.Exists(renderable + "LEVEL_MODELS.MTL.GZ")) return null;
            bool compressed = File.Exists(renderable + "LEVEL_TEXTURES.ALL.PAK.FZIP");
            Textures textures = new Textures(renderable + "LEVEL_TEXTURES.ALL.PAK" + (compressed ? ".FZIP" : ""));
            Shaders shaders = new Shaders(renderable + "LEVEL_SHADERS_DX11.PAK" + (compressed ? ".GZ" : ""));
            return new Materials(renderable + "LEVEL_MODELS.MTL" + (compressed ? ".GZ" : ""), global.Textures, textures, shaders);
        }

        /// <summary>The retail folder every level can take the block from: FRONTEND of the same install.</summary>
        public static string DonorFolderFor(Level level)
        {
            if (level == null || string.IsNullOrEmpty(level.Filepath)) return null;
            string[] split = level.Filepath.Split(new[] { "DATA/ENV/" }, StringSplitOptions.None);
            return split.Length < 2 ? null : split[0] + "DATA/ENV/PRODUCTION/FRONTEND";
        }

        /// <summary>
        /// Put the required materials back at the head of the table, in their canonical order, and say
        /// whether anything had to move. Materials are referenced by object everywhere the table is
        /// written from, so moving entries costs nothing but the indices the save assigns. Only reorders
        /// what is there: a level missing one is left missing and reported through
        /// <paramref name="missing"/>, since there is nothing to invent one from.
        /// </summary>
        public static bool EnsureOrdered(Materials materials, out List<string> missing)
        {
            missing = new List<string>();
            if (materials?.Entries == null) return false;

            bool changed = false;
            int target = 0;
            foreach (string name in Names)
            {
                int at = -1;
                for (int i = 0; i < materials.Entries.Count; i++)
                {
                    if (materials.Entries[i] != null && string.Equals(materials.Entries[i].Name, name, StringComparison.Ordinal))
                    { at = i; break; }
                }
                if (at < 0) { missing.Add(name); continue; }
                if (at != target)
                {
                    Materials.Material entry = materials.Entries[at];
                    materials.Entries.RemoveAt(at);
                    materials.Entries.Insert(target, entry);
                    changed = true;
                }
                target++;
            }
            return changed;
        }

        /// <summary>
        /// As <see cref="EnsureOrdered(Materials, out List{string})"/>, and then the shader each
        /// head material carries is moved to the same index of the shader pak - retail ships
        /// FALLBACK's at 0, POST_PROCESSING's at 1 and so on, and a level repaired after the fact
        /// would otherwise have them wherever the import appended them.
        /// </summary>
        public static bool EnsureOrdered(Materials materials, Shaders shaders, out List<string> missing)
        {
            bool changed = EnsureOrdered(materials, out missing);
            if (shaders?.Entries == null || materials?.Entries == null) return changed;

            for (int i = 0; i < Names.Length && i < materials.Entries.Count; i++)
            {
                Materials.Material material = materials.Entries[i];
                if (material == null || material.Name != Names[i] || material.Shader == null) continue;
                int at = shaders.Entries.IndexOf(material.Shader);
                if (at < 0 || at == i || i >= shaders.Entries.Count) continue;
                shaders.Entries.RemoveAt(at);
                shaders.Entries.Insert(i, material.Shader);
                changed = true;
            }
            return changed;
        }
    }
}
#endif
