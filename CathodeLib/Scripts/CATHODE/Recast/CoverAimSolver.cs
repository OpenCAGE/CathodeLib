#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;
using NanoRT;

namespace CathodeLib.NavMesh
{
    /// <summary>
    /// Resolves the clear-aim cones for a cover slot by raycasting the level, and answers whether a
    /// slot is somewhere an NPC can actually stand.
    /// </summary>
    /// <remarks>
    /// <para>A cover slot stores three firing positions - lean out left, pop over the top, lean out
    /// right - each with a horizontal arc and a vertical arc, packed as 4-bit nibbles spanning
    /// -90..+90 degrees (see <c>Cover.CoverSegment.CoverSlot</c>). Retail data has a non-zero arc on
    /// every slot; before this existed the baker wrote fixed placeholder cones.</para>
    /// <para>Angles are measured relative to the segment normal, which points away from the cover
    /// into the space the occupant is shooting at.</para>
    /// </remarks>
    public sealed class CoverAimSolver
    {
        private readonly BVHAccel _bvh;
        private readonly CoverBakeSettings _settings;
        private readonly List<Vector3> _navPoints;

        /// <summary>Slots rejected because nothing was visible from any firing position.</summary>
        public int SlotsWithoutLineOfSight { get; private set; }

        /// <summary>Slots rejected because the occupant would not be standing on the navmesh.</summary>
        public int SlotsOffNavMesh { get; private set; }

        public CoverAimSolver(CollisionNavMeshSoup soup, NavigationMesh navMesh, CoverBakeSettings settings)
        {
            _settings = settings ?? new CoverBakeSettings();

            if (soup != null && soup.TriangleCount > 0)
            {
                _bvh = new BVHAccel();
                _bvh.Build(soup.Verts, soup.Tris);
            }

            _navPoints = new List<Vector3>();
            if (navMesh?.Polygons != null && navMesh.Vertices != null)
            {
                foreach (NavigationMesh.dtPoly poly in navMesh.Polygons)
                {
                    if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND)
                        continue;
                    for (int i = 0; i < poly.vertCount; i++)
                        _navPoints.Add(navMesh.Vertices[poly.verts[i]]);
                }
            }
        }

        public bool HasGeometry => _bvh != null;
        public bool HasNavMesh => _navPoints.Count > 0;

        public void NoteSlotOffNavMesh() => SlotsOffNavMesh++;

        /// <summary>
        /// Is there navmesh close enough to <paramref name="position"/> for an NPC to occupy it?
        /// </summary>
        public bool IsOnNavMesh(Vector3 position, float tolerance)
        {
            if (_navPoints.Count == 0)
                return true;

            float toleranceSq = tolerance * tolerance;
            for (int i = 0; i < _navPoints.Count; i++)
            {
                Vector3 v = _navPoints[i];
                float dx = v.X - position.X, dz = v.Z - position.Z, dy = v.Y - position.Y;
                // Vertical slack is tighter than horizontal - cover on a walkway above a floor
                // must not latch onto the floor below it.
                if (dx * dx + dz * dz + dy * dy * 4.0f <= toleranceSq)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Fill in a slot's clear-aim nibbles. Returns false when the slot cannot see out from any
        /// of its three firing positions, in which case the caller should drop it.
        /// </summary>
        /// <param name="slotPosition">World position of the slot on the cover line.</param>
        /// <param name="normal">Segment normal, pointing away from the cover.</param>
        /// <param name="tangent">Unit vector from the segment's left end to its right end.</param>
        /// <param name="distanceToLeftEnd">Metres from the slot to the left end of the segment.</param>
        /// <param name="distanceToRightEnd">Metres from the slot to the right end of the segment.</param>
        /// <param name="coverHeight">Height of the cover above the occupant's feet.</param>
        public bool SolveSlot(
            Cover.CoverSegment.CoverSlot slot,
            Vector3 slotPosition,
            Vector3 normal,
            Vector3 tangent,
            float distanceToLeftEnd,
            float distanceToRightEnd,
            float coverHeight)
        {
            if (_bvh == null)
                return true; // No geometry to trace against; leave whatever the caller set.

            float lean = _settings.HeightSamplingDistanceAlongNormal;
            float chest = Math.Min(coverHeight, 1.2f) * 0.75f;

            // Lean out past the end of the cover, but only as far as the cover actually extends.
            Vector3 leftEye = slotPosition - tangent * Math.Min(distanceToLeftEnd, lean) + Vector3.UnitY * chest;
            Vector3 rightEye = slotPosition + tangent * Math.Min(distanceToRightEnd, lean) + Vector3.UnitY * chest;
            Vector3 topEye = slotPosition + Vector3.UnitY * (coverHeight + 0.15f);

            bool anyLeft = Sweep(leftEye, normal, out float leftMin, out float leftMax, out float leftLow, out float leftHigh);
            bool anyTop = Sweep(topEye, normal, out float topMin, out float topMax, out float topLow, out float topHigh);
            bool anyRight = Sweep(rightEye, normal, out float rightMin, out float rightMax, out float rightLow, out float rightHigh);

            if (!anyLeft && !anyTop && !anyRight)
            {
                SlotsWithoutLineOfSight++;
                return false;
            }

            // Horizontal: the inner bound of each lean, plus the span visible over the top.
            slot.LeftEdgeRightmostHorizontal = anyLeft ? leftMax : 0f;
            slot.OverTopLeftmostHorizontal = anyTop ? topMin : 0f;
            slot.OverTopRightmostHorizontal = anyTop ? topMax : 0f;
            slot.RightEdgeLeftmostHorizontal = anyRight ? rightMin : 0f;

            slot.LeftEdgeBottomVertical = anyLeft ? leftLow : 0f;
            slot.LeftEdgeTopVertical = anyLeft ? leftHigh : 0f;
            slot.OverTopBottomVertical = anyTop ? topLow : 0f;
            slot.OverTopTopVertical = anyTop ? topHigh : 0f;
            slot.RightEdgeBottomVertical = anyRight ? rightLow : 0f;
            slot.RightEdgeTopVertical = anyRight ? rightHigh : 0f;

            return true;
        }

        /// <summary>
        /// Fan rays out from <paramref name="origin"/> around <paramref name="forward"/> and report
        /// the widest unobstructed horizontal arc, plus the vertical extent that stays clear.
        /// </summary>
        private bool Sweep(Vector3 origin, Vector3 forward, out float minYaw, out float maxYaw, out float minPitch, out float maxPitch)
        {
            const int yawSamples = 13;   // every 15 degrees across the 180 degree window
            const int pitchSamples = 5;
            const float range = 8.0f;

            minYaw = 0; maxYaw = 0; minPitch = 0; maxPitch = 0;

            Vector3 flatForward = new Vector3(forward.X, 0, forward.Z);
            if (flatForward.LengthSquared() < 1e-6f)
                return false;
            flatForward = Vector3.Normalize(flatForward);
            Vector3 right = Vector3.Normalize(new Vector3(flatForward.Z, 0, -flatForward.X));

            bool any = false;
            float halfPi = (float)(Math.PI / 2.0);

            for (int i = 0; i < yawSamples; i++)
            {
                float yaw = -halfPi + i * (2f * halfPi / (yawSamples - 1));
                float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
                Vector3 dir = flatForward * cy + right * sy;

                bool yawClear = false;
                float loPitch = 0, hiPitch = 0;
                for (int p = 0; p < pitchSamples; p++)
                {
                    // Aim from slightly downwards to slightly upwards.
                    float pitch = -0.4f + p * (0.8f / (pitchSamples - 1));
                    Vector3 aim = Vector3.Normalize(dir + Vector3.UnitY * (float)Math.Tan(pitch));
                    if (!Clear(origin, aim, range))
                        continue;
                    if (!yawClear) { loPitch = pitch; hiPitch = pitch; yawClear = true; }
                    else { if (pitch < loPitch) loPitch = pitch; if (pitch > hiPitch) hiPitch = pitch; }
                }

                if (!yawClear)
                    continue;

                if (!any) { minYaw = yaw; maxYaw = yaw; minPitch = loPitch; maxPitch = hiPitch; any = true; }
                else
                {
                    if (yaw < minYaw) minYaw = yaw;
                    if (yaw > maxYaw) maxYaw = yaw;
                    if (loPitch < minPitch) minPitch = loPitch;
                    if (hiPitch > maxPitch) maxPitch = hiPitch;
                }
            }

            return any;
        }

        private bool Clear(Vector3 origin, Vector3 direction, float range)
        {
            var ray = new Ray(origin, direction, 0.02f, range);
            return !_bvh.Occluded(ref ray);
        }
    }
}
#endif
