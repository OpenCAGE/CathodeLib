#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;
using CATHODE.Scripting;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Collision geometry as a low-detail stand-in for the render meshes when testing visibility.
    /// </summary>
    /// <remarks>
    /// <para>A collision hull is a coarse closed shell around each object, which is exactly the
    /// shape a visibility test wants.</para>
    /// <para>What this buys is the difference between a probe that is genuinely in a sealed room and
    /// one that is merely wedged inside a render mesh's own detail. Occluding against render
    /// geometry, 26-27% of our surface probes found every direction blocked, against retail's
    /// 7-9%; interior submeshes, panel back faces and coincident double-sided surfaces account for
    /// almost all of it, and none of them exist in the collision shell.</para>
    /// </remarks>
    public static class RadiosityOccluders
    {
        /// <summary>
        /// Build a world-space triangle soup from the level's static collision, or return false
        /// when the level has none usable.
        /// </summary>
        /// <remarks>
        /// Both world hosts are taken. The navmesh bake uses only the secondary, since that is the
        /// walkable set, but light is blocked by everything solid - the primary host holds the
        /// structural colliders that a floor-only soup would leave out, and a missing wall is a
        /// light leak between rooms.
        /// </remarks>
        /// <param name="staticOnly">
        /// Keep only colliders that never move, matching what navmesh generation considers. Sound
        /// occlusion wants this: a crate that can be shoved around should not be baked in as a wall.
        /// </param>
        /// <param name="triangleFlags">
        /// When supplied, receives the COLLISION.MAP flags of the collider each triangle came from,
        /// one entry per triangle. Triangles with no mapping - and the render-geometry fallback -
        /// get zero. Sound occlusion uses this to tell a wall from a prop standing against it.
        /// </param>
        public static bool TryCollect(Level level, RadiosityGeometry geometry,
                                      out float[] verts, out int[] tris, Action<string> log = null,
                                      bool staticOnly = false,
                                      List<CollisionMaps.CollisionFlags> triangleFlags = null,
                                      bool skipDoorBarriers = false)
        {
            verts = null;
            tris = null;
            triangleFlags?.Clear();

            HavokPackfile hkx = level?.CollisionHKX ?? level?.CollisionHKX64;
            if (hkx == null)
            {
                log?.Invoke("Radiosity occluders: level has no collision packfile, occluding against render meshes");
                return false;
            }

            var positions = new List<float>();
            var indices = new List<int>();
            int hosts = 0;

            ISet<HavokPackfile.CompoundInstance> skipped = SkippedInstances(level, staticOnly, skipDoorBarriers);
            log?.Invoke("Radiosity occluders: skipping " + skipped.Count + " collider instance(s) of " +
                        (level.CollisionMaps?.Entries?.Count ?? 0) + " mapping(s)" +
                        (staticOnly ? " (static only)" : ""));

            Dictionary<HavokPackfile.CompoundInstance, CollisionMaps.CollisionFlags> flagsByInstance =
                triangleFlags == null ? null : FlagsByInstance(level);

            foreach (HavokPackfile.StaticCompoundShape host in Hosts(hkx))
            {
                HavokPackfile.PreviewMesh mesh;
                try { mesh = hkx.BuildBakeMesh(host, skipped, triangleFlags != null); }
                catch (Exception e) { log?.Invoke("Radiosity occluders: " + e.Message); continue; }
                if (mesh == null || mesh.Positions.Count == 0 || mesh.Indices.Count < 3)
                    continue;

                hosts++;
                int baseVertex = positions.Count / 3;
                foreach (Vector3 p in mesh.Positions)
                {
                    positions.Add(p.X);
                    positions.Add(p.Y);
                    positions.Add(p.Z);
                }
                foreach (int i in mesh.Indices)
                    indices.Add(baseVertex + i);

                if (triangleFlags == null) continue;
                for (int tri = 0; tri < mesh.TriangleCount; tri++)
                {
                    HavokPackfile.CompoundInstance owner = mesh.InstanceOf(tri);
                    triangleFlags.Add(owner != null && flagsByInstance.TryGetValue(owner, out CollisionMaps.CollisionFlags f)
                                      ? f : 0);
                }
            }

            if (hosts == 0 || indices.Count < 3)
            {
                log?.Invoke("Radiosity occluders: collision produced no triangles, occluding against render meshes");
                return false;
            }

            int collisionTris = indices.Count / 3;
            int fallbackTris = AppendUncoveredRenderGeometry(level, geometry, positions, indices, out int fallbackInstances);
            if (triangleFlags != null)
                for (int i = 0; i < fallbackTris; i++) triangleFlags.Add(0);

            verts = positions.ToArray();
            tris = indices.ToArray();
            log?.Invoke("Radiosity occluders: " + collisionTris + " collision triangles from " + hosts +
                        " world host(s)" +
                        (fallbackTris > 0
                            ? ", plus " + fallbackTris + " render triangles from " + fallbackInstances +
                              " of " + (geometry?.Instances.Count ?? 0) + " instance(s) with no collision"
                            : ", every baked instance has collision"));
            return true;
        }

        /// <summary>
        /// Add the render triangles of any baked instance that has no collision of its own.
        /// </summary>
        /// <remarks>
        /// An instance missing from COLLISION.MAP contributes nothing to the proxy, so without this
        /// it would stop blocking light entirely - a wall that casts no shadow and leaks into the
        /// room behind it. Retail geometry is almost fully covered, but user-imported movers
        /// frequently have no collision at all, and they are exactly the case the bake must not
        /// silently mislight. Their own render triangles stand in, which is strictly better than
        /// nothing and no worse than the render-mesh occlusion we used before.
        /// </remarks>
        static int AppendUncoveredRenderGeometry(Level level, RadiosityGeometry geometry,
                                                 List<float> positions, List<int> indices,
                                                 out int instanceCount)
        {
            instanceCount = 0;
            if (geometry == null || geometry.TriangleCount == 0)
                return 0;

            // A mapping names its owner by the composite instance the entity was created in, which
            // is the same key RadiosityGeometry groups its instances by. ResourceGUID is taken too
            // since it is the resource's own id and costs nothing to include.
            var covered = new HashSet<ShortGuid>();
            if (level?.CollisionMaps?.Entries != null)
            {
                foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
                {
                    if (entry == null) continue;
                    if (entry.Entity != null && entry.Entity.composite_instance_id != ShortGuid.Invalid)
                        covered.Add(entry.Entity.composite_instance_id);
                    if (entry.ResourceGUID != ShortGuid.Invalid)
                        covered.Add(entry.ResourceGUID);
                }
            }

            // Which of our instances are uncovered, resolved once rather than per triangle.
            var uncovered = new bool[geometry.Instances.Count];
            for (int i = 0; i < geometry.Instances.Count; i++)
            {
                uncovered[i] = !covered.Contains(geometry.Instances[i].CompositeInstanceID);
                if (uncovered[i]) instanceCount++;
            }
            if (instanceCount == 0)
                return 0;

            // Vertices are shared between the render soup's triangles, so remap only those used.
            var remap = new Dictionary<int, int>();
            int added = 0;
            for (int tri = 0; tri < geometry.TriangleCount; tri++)
            {
                int instance = geometry.TriangleInstance[tri];
                if (instance < 0 || instance >= uncovered.Length || !uncovered[instance])
                    continue;

                for (int k = 0; k < 3; k++)
                {
                    int source = geometry.Tris[tri * 3 + k];
                    if (!remap.TryGetValue(source, out int target))
                    {
                        Vector3 v = geometry.At(source);
                        target = positions.Count / 3;
                        positions.Add(v.X);
                        positions.Add(v.Y);
                        positions.Add(v.Z);
                        remap[source] = target;
                    }
                    indices.Add(target);
                }
                added++;
            }
            return added;
        }

        /// <summary>COLLISION.MAP flags keyed by the collider instance they describe.</summary>
        static Dictionary<HavokPackfile.CompoundInstance, CollisionMaps.CollisionFlags> FlagsByInstance(Level level)
        {
            var byInstance = new Dictionary<HavokPackfile.CompoundInstance, CollisionMaps.CollisionFlags>();
            if (level?.CollisionMaps?.Entries == null) return byInstance;
            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
                if (entry?.CollisionInstance != null)
                    byInstance[entry.CollisionInstance] = entry.Flags;
            return byInstance;
        }

        static IEnumerable<HavokPackfile.StaticCompoundShape> Hosts(HavokPackfile hkx)
        {
            HavokPackfile.StaticCompoundShape primary = hkx.WorldHostPrimary;
            HavokPackfile.StaticCompoundShape secondary = hkx.WorldHostSecondary;
            if (primary != null) yield return primary;
            if (secondary != null && !ReferenceEquals(secondary, primary)) yield return secondary;
        }

        /// <summary>
        /// Collision instances that must not block light: barrier boxes are invisible gameplay
        /// volumes, and a ghosted collider is not solid at runtime.
        /// </summary>
        static ISet<HavokPackfile.CompoundInstance> SkippedInstances(Level level, bool staticOnly,
                                                                     bool skipDoorBarriers = false)
        {
            var skip = new HashSet<HavokPackfile.CompoundInstance>();
            if (level?.CollisionMaps?.Entries == null)
                return skip;

            const CollisionMaps.CollisionFlags ghostMask =
                CollisionMaps.CollisionFlags.GHOSTED | CollisionMaps.CollisionFlags.PRE_GHOSTED;

            foreach (CollisionMaps.COLLISION_MAPPING entry in level.CollisionMaps.Entries)
            {
                if (entry?.CollisionInstance == null) continue;
                if ((entry.Flags & ghostMask) != 0)
                {
                    skip.Add(entry.CollisionInstance);
                    continue;
                }

                // A doorway barrier is the sealed state of a door the runtime opens and closes;
                // the door-transfer section modulates the doorway after the fact, so the bake
                // itself stores the doors-open field.
                if (skipDoorBarriers &&
                    (entry.Flags & CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK) ==
                    (CollisionMaps.CollisionFlags)CollisionMaps.CollisionType.PATH_CLOSED)
                {
                    skip.Add(entry.CollisionInstance);
                    continue;
                }

                if (!staticOnly) continue;

                // Anything animated or physics-driven is not part of the fixed world. FIXED is the
                // only motion type navmesh generation treats as solid, and sound wants the same set.
                if ((entry.Flags & CollisionMaps.CollisionFlags.MOTION_TYPE_MASK) != CollisionMaps.CollisionFlags.FIXED)
                {
                    skip.Add(entry.CollisionInstance);
                    continue;
                }

                // Ballistic colliders stop bullets, not sound - grates, railings and thin panels
                // are all shot-proof and all audible through. They are the largest single group in
                // BSP_TORRENS' collision map (2623 of 4999).
                if ((entry.Flags & CollisionMaps.CollisionFlags.COLLISION_TYPE_MASK) ==
                    (CollisionMaps.CollisionFlags)CollisionMaps.CollisionType.BALLISTICS)
                    skip.Add(entry.CollisionInstance);
            }
            return skip;
        }
    }
}
#endif
