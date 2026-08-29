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
        private readonly List<Vector3[]> _navPolys = new List<Vector3[]>();
        private readonly Dictionary<long, List<int>> _navGrid = new Dictionary<long, List<int>>();
        private const float NavCell = 2.0f;

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

            if (navMesh?.Polygons != null && navMesh.Vertices != null)
            {
                foreach (NavigationMesh.dtPoly poly in navMesh.Polygons)
                {
                    if (poly.area.GetPolyType() != NavigationMesh.dtPolyTypes.DT_POLYTYPE_GROUND)
                        continue;
                    // Backstage is the alien's ceiling network, not somewhere an NPC can stand.
                    if (((uint)poly.area.GetMarkupFlags() & (uint)NavigationMesh.NavMeshAreaTypeFlags.BackstageFlag) != 0)
                        continue;
                    if (poly.vertCount < 3 || poly.verts == null)
                        continue;
                    var verts = new Vector3[poly.vertCount];
                    for (int i = 0; i < poly.vertCount; i++)
                        verts[i] = navMesh.Vertices[poly.verts[i]];

                    int index = _navPolys.Count;
                    _navPolys.Add(verts);

                    float minX = verts[0].X, maxX = verts[0].X, minZ = verts[0].Z, maxZ = verts[0].Z;
                    for (int i = 1; i < verts.Length; i++)
                    {
                        if (verts[i].X < minX) minX = verts[i].X;
                        if (verts[i].X > maxX) maxX = verts[i].X;
                        if (verts[i].Z < minZ) minZ = verts[i].Z;
                        if (verts[i].Z > maxZ) maxZ = verts[i].Z;
                    }
                    int x0 = (int)Math.Floor(minX / NavCell), x1 = (int)Math.Floor(maxX / NavCell);
                    int z0 = (int)Math.Floor(minZ / NavCell), z1 = (int)Math.Floor(maxZ / NavCell);
                    if ((long)(x1 - x0 + 1) * (z1 - z0 + 1) > 100000)
                        continue;
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            long k = ((long)x << 32) ^ (uint)z;
                            if (!_navGrid.TryGetValue(k, out List<int> l)) _navGrid[k] = l = new List<int>();
                            l.Add(index);
                        }
                }
            }
        }

        public bool HasGeometry => _bvh != null;
        public bool HasNavMesh => _navPolys.Count > 0;

        public void NoteSlotOffNavMesh() => SlotsOffNavMesh++;

        /// <summary>
        /// Is there navmesh close enough to <paramref name="position"/> for an NPC to occupy it?
        /// </summary>
        /// <remarks>
        /// This is polygon containment, not proximity to a vertex. A long straight wall is one
        /// navmesh edge with vertices only at its ends, so testing against vertices rejected every
        /// slot along the middle of it - which is exactly where cover matters most.
        /// </remarks>
        public bool IsOnNavMesh(Vector3 position, float tolerance)
        {
            if (_navPolys.Count == 0)
                return true;

            int cx = (int)Math.Floor(position.X / NavCell), cz = (int)Math.Floor(position.Z / NavCell);
            float best = float.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_navGrid.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out List<int> l))
                        continue;
                    foreach (int i in l)
                    {
                        Vector3[] v = _navPolys[i];
                        float y = 0;
                        for (int k = 0; k < v.Length; k++) y += v[k].Y;
                        y /= v.Length;
                        // A walkway above a floor must not latch onto the floor below it.
                        if (Math.Abs(y - position.Y) > 1.0f)
                            continue;
                        if (ContainsXZ(v, position))
                            return true;
                        float d = DistanceToEdgesXZ(v, position);
                        if (d < best) best = d;
                    }
                }
            return best <= tolerance;
        }

        private static bool ContainsXZ(Vector3[] v, Vector3 p)
        {
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                if ((v[i].Z > p.Z) != (v[j].Z > p.Z) &&
                    p.X < (v[j].X - v[i].X) * (p.Z - v[i].Z) / (v[j].Z - v[i].Z) + v[i].X)
                    inside = !inside;
            return inside;
        }

        private static float DistanceToEdgesXZ(Vector3[] v, Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                float abx = v[i].X - v[j].X, abz = v[i].Z - v[j].Z;
                float apx = p.X - v[j].X, apz = p.Z - v[j].Z;
                float ab2 = abx * abx + abz * abz;
                float t = ab2 > 1e-12f ? Math.Max(0f, Math.Min(1f, (apx * abx + apz * abz) / ab2)) : 0f;
                float qx = v[j].X + abx * t - p.X, qz = v[j].Z + abz * t - p.Z;
                float d = (float)Math.Sqrt(qx * qx + qz * qz);
                if (d < best) best = d;
            }
            return best;
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

            // You can only lean a way the cover does not keep going. Clamping the lean eye to the
            // segment end (above) stops it walking through the wall, but it still leaves the eye
            // flat against a wall that carries on past it, and sweeping from there reports an arc
            // retail does not have: its lean arcs are DEAD on 38.0% / 37.3% of slots, ours on
            // 4.2% / 10.1%. A slot in the middle of a long segment cannot lean either way.
            if (_settings.LeanNeedsAnEnd)
            {
                if (distanceToLeftEnd > lean) { anyLeft = false; leftMin = leftMax = leftLow = leftHigh = 0f; }
                if (distanceToRightEnd > lean) { anyRight = false; rightMin = rightMax = rightLow = rightHigh = 0f; }
            }

            // You never shoot over HIGH cover. Retail writes a zero-width over-the-top arc on 3,270
            // of 3,270 shipped high slots - both nibbles are 0, so the arc reads -90 to -90 - against
            // a 180 degree median on low cover, where it is zero on only 0.2%. That is a rule, not a
            // measurement, and without it 78% of our high slots tell the AI it can fire over a 1.6 m
            // wall. Measured with `diag coverslots`.
            if (coverHeight >= _settings.LowHighDividingLine)
            {
                anyTop = false;
                topMin = topMax = topLow = topHigh = 0f;
            }

            if (!anyLeft && !anyTop && !anyRight)
            {
                SlotsWithoutLineOfSight++;
                return false;
            }

            // Horizontal: the inner bound of each lean, plus the span visible over the top. A DEAD
            // arc has to be written as a degenerate one, and which end it degenerates to differs per
            // field: the left lean runs from -90 up to LeftEdgeRightmost, so dead is -90; the right
            // lean runs from RightEdgeLeftmost up to +90, so dead is +90. Writing a zero ANGLE
            // instead - which is what this did - encodes nibble 8 and leaves a 90 degree arc on a
            // firing position that does not exist.
            const float halfPi = (float)(Math.PI / 2.0);
            slot.LeftEdgeRightmostHorizontal = anyLeft ? leftMax : -halfPi;
            slot.OverTopLeftmostHorizontal = anyTop ? topMin : -halfPi;
            slot.OverTopRightmostHorizontal = anyTop ? topMax : -halfPi;
            slot.RightEdgeLeftmostHorizontal = anyRight ? rightMin : halfPi;

            // Vertical: a dead firing position is spelled -90/-90, the way retail spells it. Its own
            // files are unambiguous - the vertical arc is dead on 100.0% of high-cover over-top
            // positions and 40.0/40.2% of the lean ones, matching the horizontal dead rates to
            // within two points once the packing is read correctly (see Cover.GetVerticalAngle).
            slot.LeftEdgeBottomVertical = anyLeft ? leftLow : -halfPi;
            slot.LeftEdgeTopVertical = anyLeft ? leftHigh : -halfPi;
            slot.OverTopBottomVertical = anyTop ? topLow : -halfPi;
            slot.OverTopTopVertical = anyTop ? topHigh : -halfPi;
            slot.RightEdgeBottomVertical = anyRight ? rightLow : -halfPi;
            slot.RightEdgeTopVertical = anyRight ? rightHigh : -halfPi;

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
            float range = _settings.AimClearRange > 0f ? _settings.AimClearRange : 8.0f;

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
