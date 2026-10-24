using CATHODE;
using System;
using System.Linq;
using System.Collections.Generic;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib
{
    /// <summary>
    /// The models every level is required to carry, at the head of its model pak.
    ///
    /// The engine indexes these positionally, so they lead the pak and their order is fixed. Two of
    /// them - the CPU particle and ribbon meshes - are components 0 and 1 of a SINGLE nameless CS2
    /// (`[dynamic_mesh]`), so twelve required models occupy eleven pak entries and every name from
    /// FOGSPHERE on sits one entry lower than its ordinal. Resolving by name avoids having to care.
    ///
    /// Measured across all 20 production levels: the head is identical on every one of them, and
    /// every slot but UNITBOX is used by a shipped mover. UNITBOX belongs to ProjectiveDecal, which
    /// no shipped level places.
    /// </summary>
    public static class RequiredModels
    {
        public enum Model
        {
            REQUIRED_MODEL_1000_PARTICLE_CUBE,
            REQUIRED_MODEL_DEFERRED_POINT_LIGHT,
            REQUIRED_MODEL_DEFERRED_SPOT_LIGHT,
            REQUIRED_MODEL_DEFERRED_STRIP_LIGHT,
            REQUIRED_MODEL_CPU_PARTICLE_MODEL,
            REQUIRED_MODEL_CPU_RIBBON_MODEL,
            REQUIRED_MODEL_FOGSPHERE,
            REQUIRED_MODEL_FOGBOX,
            REQUIRED_MODEL_FOGPLANE,
            REQUIRED_MODEL_WATER,
            REQUIRED_MODEL_REFRACTION,
            REQUIRED_MODEL_UNITBOX,
        }

        private class Slot
        {
            public string Name;      //the CS2 name retail ships, matched case-insensitively
            public int Component;    //which component of it - only [dynamic_mesh] has more than one
            public int Entry;        //the pak entry index it occupies, as a fallback when unnamed
        }

        //Names as retail spells them; the pak mixes ".cs2" and ".CS2" so comparisons ignore case.
        private static readonly Dictionary<Model, Slot> _slots = new Dictionary<Model, Slot>()
        {
            { Model.REQUIRED_MODEL_1000_PARTICLE_CUBE,   new Slot { Name = @"Global\Props\1000_particle_system.cs2", Component = 0, Entry = 0  } },
            { Model.REQUIRED_MODEL_DEFERRED_POINT_LIGHT, new Slot { Name = @"Global\Props\deferred_point_light.cs2", Component = 0, Entry = 1  } },
            { Model.REQUIRED_MODEL_DEFERRED_SPOT_LIGHT,  new Slot { Name = @"Global\Props\deferred_spot_light.cs2",  Component = 0, Entry = 2  } },
            { Model.REQUIRED_MODEL_DEFERRED_STRIP_LIGHT, new Slot { Name = @"Global\Props\deferred_strip_light.cs2", Component = 0, Entry = 3  } },
            { Model.REQUIRED_MODEL_CPU_PARTICLE_MODEL,   new Slot { Name = "[dynamic_mesh]",                         Component = 0, Entry = 4  } },
            { Model.REQUIRED_MODEL_CPU_RIBBON_MODEL,     new Slot { Name = "[dynamic_mesh]",                         Component = 1, Entry = 4  } },
            { Model.REQUIRED_MODEL_FOGSPHERE,            new Slot { Name = @"Global\Props\fogsphere.CS2",            Component = 0, Entry = 5  } },
            { Model.REQUIRED_MODEL_FOGBOX,               new Slot { Name = @"Global\Props\fogbox.CS2",               Component = 0, Entry = 6  } },
            { Model.REQUIRED_MODEL_FOGPLANE,             new Slot { Name = @"Global\Props\fogplane.CS2",             Component = 0, Entry = 7  } },
            { Model.REQUIRED_MODEL_WATER,                new Slot { Name = @"Global\Props\noninteractive_water.CS2", Component = 0, Entry = 8  } },
            { Model.REQUIRED_MODEL_REFRACTION,           new Slot { Name = @"Global\Props\refraction.CS2",           Component = 0, Entry = 9  } },
            { Model.REQUIRED_MODEL_UNITBOX,              new Slot { Name = @"Global\Props\unitbox.CS2",              Component = 0, Entry = 10 } },
        };

        /// <summary>The pak entry a required model is expected to occupy. Ordering is load-bearing.</summary>
        public static int ExpectedEntry(Model model)
        {
            Slot slot;
            return _slots.TryGetValue(model, out slot) ? slot.Entry : -1;
        }

        /// <summary>How many pak entries the required block occupies (fewer than the model count).</summary>
        public static int EntryCount
        {
            get
            {
                int max = -1;
                foreach (Slot slot in _slots.Values) if (slot.Entry > max) max = slot.Entry;
                return max + 1;
            }
        }

        /// <summary>Is this pak entry part of the required block, and so not the user's to remove?</summary>
        public static bool IsRequiredEntry(Models models, Models.CS2 entry)
        {
            if (models == null || entry == null) return false;
            foreach (Slot slot in _slots.Values)
                if (string.Equals(entry.Name, slot.Name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        /// The submesh a mover should reference for this required model, or null when the level does
        /// not carry it. Matched by name first - that survives any reordering - and by the expected
        /// entry index only as a fallback.
        /// </summary>
        public static Models.CS2.Component.LOD.Submesh Resolve(Models models, Model model)
        {
            if (models?.Entries == null) return null;

            Slot slot;
            if (!_slots.TryGetValue(model, out slot)) return null;

            Models.CS2 cs2 = null;
            for (int i = 0; i < models.Entries.Count; i++)
            {
                if (models.Entries[i] == null) continue;
                if (!string.Equals(models.Entries[i].Name, slot.Name, StringComparison.OrdinalIgnoreCase)) continue;
                cs2 = models.Entries[i];
                break;
            }
            if (cs2 == null && slot.Entry >= 0 && slot.Entry < models.Entries.Count)
                cs2 = models.Entries[slot.Entry];
            if (cs2 == null) return null;

            if (slot.Component >= cs2.Components.Count) return null;
            Models.CS2.Component component = cs2.Components[slot.Component];
            if (component?.LODs == null || component.LODs.Count == 0) return null;

            //Movers reference the highest-detail LOD; the rest hang off it as REDS children.
            Models.CS2.Component.LOD lod = component.LODs[0];
            return lod?.Submeshes == null || lod.Submeshes.Count == 0 ? null : lod.Submeshes[0];
        }

        /// <summary>
        /// Put the required models back at the head of the pak, in their canonical order, and say
        /// whether anything had to move. The engine reads them positionally, so an import or a
        /// deletion that shuffles them breaks every mover pointing at one - this runs before a save
        /// rather than trusting the pak to have stayed tidy.
        ///
        /// Only reorders what is there. A level genuinely missing one is left missing and reported
        /// by the caller: inventing an empty mesh would be worse than the mover keeping its own.
        /// </summary>
        public static bool EnsureOrdered(Models models, out List<Model> missing)
        {
            missing = new List<Model>();
            if (models?.Entries == null) return false;

            //Canonical entry order, de-duplicated: the two CPU meshes share one entry
            List<string> wanted = new List<string>();
            foreach (Model model in Enum.GetValues(typeof(Model)))
            {
                Slot slot = _slots[model];
                bool present = false;
                for (int i = 0; i < models.Entries.Count; i++)
                    if (models.Entries[i] != null && string.Equals(models.Entries[i].Name, slot.Name, StringComparison.OrdinalIgnoreCase))
                    { present = true; break; }
                if (!present) { missing.Add(model); continue; }
                if (!wanted.Any(o => string.Equals(o, slot.Name, StringComparison.OrdinalIgnoreCase)))
                    wanted.Add(slot.Name);
            }

            bool changed = false;
            for (int target = 0; target < wanted.Count; target++)
            {
                int at = -1;
                for (int i = 0; i < models.Entries.Count; i++)
                    if (models.Entries[i] != null && string.Equals(models.Entries[i].Name, wanted[target], StringComparison.OrdinalIgnoreCase))
                    { at = i; break; }
                if (at < 0 || at == target) continue;

                Models.CS2 entry = models.Entries[at];
                models.Entries.RemoveAt(at);
                models.Entries.Insert(target, entry);
                changed = true;
            }
            return changed;
        }

        /// <summary>The deferred proxy volume a light of this type is drawn with.</summary>
        public static Model ForLight(Lights.LightType type)
        {
            switch (type)
            {
                case Lights.LightType.Spot: return Model.REQUIRED_MODEL_DEFERRED_SPOT_LIGHT;
                case Lights.LightType.Strip: return Model.REQUIRED_MODEL_DEFERRED_STRIP_LIGHT;
                //Retail ships no ambient or directional light material, and no mover for one either
                //- point is the only remaining volume, and the only sane thing to draw.
                default: return Model.REQUIRED_MODEL_DEFERRED_POINT_LIGHT;
            }
        }
    }
}
#endif
