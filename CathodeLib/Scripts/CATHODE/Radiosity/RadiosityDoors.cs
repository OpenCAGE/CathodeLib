#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Locates the NavMeshBarrier volumes that gate light between slices, so the bake can emit the
    /// door transfer sets the runtime opens and closes as doors move.
    /// </summary>
    /// <remarks>
    /// <para>A Door entity in Commands drives a NavMeshBarrier entity, which
    /// <see cref="Instancing"/> registers both as a resource and as a COLLISION.MAP row flagged
    /// <c>PATH_CLOSED</c>. That row stores the index of its instance inside the Havok world host
    /// picked by the row's <c>WORLD</c> flag, and it is <i>that</i> index the radiosity data holds
    /// in <c>DoorInfo.NavmeshBarrierCathodeInstanceIndex</c>.</para>
    /// <para>Verified against retail: every value in column one of RADIOSITY_COLLISION_MAPPING is a
    /// PATH_CLOSED instance index (121/121 on HAB_AIRPORT, 129/129 on Tech_Hub, 88/88 on
    /// SCI_HospitalLower), and mapping a door's stored index through that table lands on one every
    /// time (92/92, 71/71, 52/52).</para>
    /// <para>That table only exists because the shipped radiosity bake predates the final collision
    /// build, so it patches stale indices forward. We generate both in one pass, so we write the
    /// final index directly and leave RADIOSITY_COLLISION_MAPPING empty - which is exactly what
    /// BSP_TORRENS ships, 16 doors with no mapping at all.</para>
    /// </remarks>
    public sealed class RadiosityDoors
    {
        public sealed class Barrier
        {
            /// <summary>Index within the Havok world host, as COLLISION.MAP stores it.</summary>
            public int CollisionInstanceIndex;

            public Vector3 Centre;

            /// <summary>Rough radius of the barrier box, used to scope the probe search.</summary>
            public float Radius;
        }

        public List<Barrier> Barriers { get; } = new List<Barrier>();

        /// <summary>Barriers found whose collision instance could not be located in a host.</summary>
        public int Unresolved { get; private set; }

        public static RadiosityDoors CollectFromLevel(Level level, Action<string> log = null)
        {
            var doors = new RadiosityDoors();
            if (level?.CollisionMaps?.Entries == null)
                return doors;

            HavokPackfile hkx = level.CollisionHKX ?? level.CollisionHKX64;
            if (hkx == null)
                return doors;

            // Instance -> index, per host, so a row resolves without scanning.
            var primary = BuildIndex(hkx.WorldHostPrimary);
            var secondary = ReferenceEquals(hkx.WorldHostSecondary, hkx.WorldHostPrimary)
                ? primary
                : BuildIndex(hkx.WorldHostSecondary);

            const int pathClosed = (int)CollisionMaps.CollisionType.PATH_CLOSED;

            foreach (CollisionMaps.COLLISION_MAPPING row in level.CollisionMaps.Entries)
            {
                if (((int)row.Flags & pathClosed) != pathClosed)
                    continue;
                if (row.CollisionInstance == null)
                {
                    doors.Unresolved++;
                    continue;
                }

                bool world = (row.Flags & CollisionMaps.CollisionFlags.WORLD) != 0;
                Dictionary<HavokPackfile.CompoundInstance, int> lookup =
                    world && hkx.WorldHostSecondary != null ? secondary : primary;

                if (lookup == null || !lookup.TryGetValue(row.CollisionInstance, out int index))
                {
                    doors.Unresolved++;
                    continue;
                }

                Vector4 t = row.CollisionInstance.Translation;
                Vector4 s = row.CollisionInstance.Scale;
                doors.Barriers.Add(new Barrier
                {
                    CollisionInstanceIndex = index,
                    Centre = new Vector3(t.X, t.Y, t.Z),
                    // Scale is a half-extent for the box shapes barriers use; fall back to a
                    // door-sized guess when it is degenerate.
                    Radius = Math.Max(0.5f, new Vector3(Math.Abs(s.X), Math.Abs(s.Y), Math.Abs(s.Z)).Length())
                });
            }

            log?.Invoke("Radiosity doors: " + doors.Barriers.Count + " PATH_CLOSED barriers" +
                        (doors.Unresolved > 0 ? " (" + doors.Unresolved + " unresolved)" : ""));
            return doors;
        }

        private static Dictionary<HavokPackfile.CompoundInstance, int> BuildIndex(HavokPackfile.StaticCompoundShape host)
        {
            if (host?.Instances == null)
                return null;
            var map = new Dictionary<HavokPackfile.CompoundInstance, int>(host.Instances.Count);
            for (int i = 0; i < host.Instances.Count; i++)
                map[host.Instances[i]] = i;
            return map;
        }
    }
}
#endif
