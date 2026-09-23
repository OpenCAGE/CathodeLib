using CATHODE;
using System;
using System.Collections.Generic;
using CathodeLib.ObjectExtensions;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib
{
    /// <summary>
    /// The twelve movers every retail level carries at the head of its MVR: one per REQUIRED_MODEL_*
    /// (the particle cube, the three deferred light volumes, the CPU particle and ribbon meshes, fog
    /// sphere/box/plane, water, refraction, the unit box), at the origin, belonging to no entity and no
    /// resource. Instancing rebuilds every other mover from the script and keeps these as they are.
    ///
    /// They are not byte-identical between levels (the particle cube's GPU constants differ between
    /// FRONTEND and BSP_Torrens, and the light volumes draw with "01 - DEFAULT"/"08 - DEFAULT" rather than
    /// their submesh's own POINT_LIGHT_MATERIAL), so a level that lacks them is given the ones of the level
    /// it was made from - FRONTEND - rather than ones built from nothing.
    /// </summary>
    public static class RequiredMovers
    {
        public const int Count = 12;

        /// <summary>
        /// A required-asset mover: no entity, no resource, and every renderable in it one of the required
        /// models. Nothing the script instances can match this, so user content is never mistaken for one.
        /// </summary>
        public static bool IsRequiredMover(Movers.MOVER_DESCRIPTOR mover, Models models)
        {
            if (mover == null || mover.Resource != null)
                return false;
            if (mover.Entity != null && (!mover.Entity.entity_id.IsInvalid || !mover.Entity.composite_instance_id.IsInvalid))
                return false;
            if (mover.RenderableElements == null || mover.RenderableElements.Count == 0)
                return false;
            foreach (RenderableElements.Element element in mover.RenderableElements)
            {
                Models.CS2 cs2 = element?.Model == null ? null : models?.FindModel(element.Model);
                if (cs2 == null || !RequiredModels.IsRequiredEntry(models, cs2))
                    return false;
            }
            return true;
        }

        /// <summary>The leading run of required-asset movers in this list (at most <see cref="Count"/>).</summary>
        public static List<Movers.MOVER_DESCRIPTOR> Head(List<Movers.MOVER_DESCRIPTOR> movers, Models models)
        {
            List<Movers.MOVER_DESCRIPTOR> head = new List<Movers.MOVER_DESCRIPTOR>();
            if (movers == null) return head;
            for (int i = 0; i < movers.Count && head.Count < Count; i++)
            {
                if (!IsRequiredMover(movers[i], models)) break;
                head.Add(movers[i]);
            }
            return head;
        }

        /// <summary>
        /// Copies of a donor level's required-asset movers, their renderable runs imported into
        /// <paramref name="into"/> (the models and materials with them). Returns an empty list when the
        /// donor has no complete head.
        /// </summary>
        public static List<Movers.MOVER_DESCRIPTOR> ImportFrom(Level into, Level donor)
        {
            List<Movers.MOVER_DESCRIPTOR> copies = new List<Movers.MOVER_DESCRIPTOR>();
            if (into == null || donor?.Movers == null) return copies;

            List<Movers.MOVER_DESCRIPTOR> head = Head(donor.Movers.Entries, donor.Models);
            if (head.Count != Count) return copies;

            foreach (Movers.MOVER_DESCRIPTOR source in head)
            {
                //Field by field: the reflection Copy() would clone the submeshes as well, and the renderable
                //import needs the donor's own submeshes to find the models they belong to
                Movers.MOVER_DESCRIPTOR mover = new Movers.MOVER_DESCRIPTOR()
                {
                    Transform = source.Transform,
                    RenderableElements = into.RenderableElements.ImportEntry(source.RenderableElements, donor.Models),
                    Resource = null,
                    CullFlags = source.CullFlags,
                    Entity = source.Entity == null ? null : new EntityHandle() { entity_id = source.Entity.entity_id, composite_instance_id = source.Entity.composite_instance_id },
                    EnvironmentMap = null,
                    EmissiveTint = source.EmissiveTint,
                    EmissiveFlags = source.EmissiveFlags,
                    EmissiveIntensityMultiplier = source.EmissiveIntensityMultiplier,
                    EmissiveRadiosityMultiplier = source.EmissiveRadiosityMultiplier,
                    PrimaryZoneID = source.PrimaryZoneID,
                    SecondaryZoneID = source.SecondaryZoneID,
                    LightingMasterID = source.LightingMasterID,
                    Flags = source.Flags.Copy(),
                    RuntimeRefs = source.RuntimeRefs == null ? null : (byte[])source.RuntimeRefs.Clone(),
                    RuntimeIndex = source.RuntimeIndex,
                };
                mover.GPUConstants.SetRawBytes(source.GPUConstants.RawBytes);
                mover.RenderConstants.SetRawBytes(source.RenderConstants.RawBytes);
                copies.Add(mover);
            }
            return copies;
        }
    }
}
#endif
