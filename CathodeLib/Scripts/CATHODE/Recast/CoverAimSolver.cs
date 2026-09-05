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
        /// <param name="slotPosition">
        /// World position of the slot ON THE COVER LINE, not where the occupant stands. Each aim
        /// model applies its own offsets from here: the legacy one steps out by
        /// <see cref="CoverBakeSettings.SlotStandOffset"/>, the two-position one uses the
        /// clear-aim offsets, whose move-from position lands on that same 0.5 m.
        /// </param>
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
            bool high = coverHeight >= _settings.LowHighDividingLine;

            float leftMin = 0f, leftMax = 0f, leftLow = 0f, leftHigh = 0f;
            float rightMin = 0f, rightMax = 0f, rightLow = 0f, rightHigh = 0f;
            float topMin = 0f, topMax = 0f, topLow = 0f, topHigh = 0f;
            bool anyLeft, anyTop, anyRight;

            if (_settings.UseTwoPositionAim)
            {
                // The two-position clear-aim geometry. Forward is measured TOWARDS the cover, so it
                // subtracts our outward normal: the shoot position ends up 0.1 m on the far side of
                // the cover plane and 0.5 m past the edge, and the move-from position 0.5 m out on
                // the walkable side. See CoverBakeSettings.UseTwoPositionAim.
                float sideH = high ? _settings.AimShootHeightStandingSide : _settings.AimShootHeightCrouchedSide;
                float fromH = high ? _settings.AimMoveFromHeightStandingSide : _settings.AimMoveFromHeightCrouchedSide;
                float lat = _settings.AimShootLateralOffsetSide;
                float fwd = _settings.AimShootForwardOffsetSide;
                float mlat = _settings.AimMoveFromLateralOffsetSide;
                float mfwd = _settings.AimMoveFromForwardOffsetSide;

                // Either 'lat' metres to my left, or 'lat' metres past the corner itself.
                float latL = _settings.AimShootLateralFromSegmentEnd ? distanceToLeftEnd + lat : lat;
                float latR = _settings.AimShootLateralFromSegmentEnd ? distanceToRightEnd + lat : lat;
                Vector3 leftEye = slotPosition - tangent * latL - normal * fwd + Vector3.UnitY * sideH;
                Vector3 rightEye = slotPosition + tangent * latR - normal * fwd + Vector3.UnitY * sideH;
                Vector3 topEye = slotPosition - normal * _settings.AimShootForwardOffsetOver
                                 + Vector3.UnitY * _settings.AimShootHeightOver;

                // A negative lateral offset puts the move-from position BACK from the edge, towards
                // the middle of the segment, which is why the same signed value serves both sides.
                Vector3 leftFrom = slotPosition - tangent * mlat - normal * mfwd + Vector3.UnitY * fromH;
                Vector3 rightFrom = slotPosition + tangent * mlat - normal * mfwd + Vector3.UnitY * fromH;
                Vector3 topFrom = slotPosition - normal * _settings.AimMoveFromForwardOffsetOver
                                  + Vector3.UnitY * _settings.AimMoveFromHeightOver;

                bool two = _settings.RequireClearFromMoveFrom;
                Vector3 sweepDir = _settings.AimSweepOutward ? -normal : normal;
                int leftMask = 0, rightMask = 0;
                // When the clearance test owns liveness, the shoot eye is free to sit where the
                // clear-aim model puts it, measured from the corner rather than from the slot.
                if (_settings.UseLeanClearanceTest)
                {
                    leftEye = slotPosition - tangent * (distanceToLeftEnd + lat) - normal * fwd
                              + Vector3.UnitY * sideH;
                    rightEye = slotPosition + tangent * (distanceToRightEnd + lat) - normal * fwd
                               + Vector3.UnitY * sideH;
                }
                bool leftOk = !_settings.UseLeanClearanceTest
                    || LeanClearance(slotPosition - tangent * distanceToLeftEnd, -tangent, normal, sideH);
                bool rightOk = !_settings.UseLeanClearanceTest
                    || LeanClearance(slotPosition + tangent * distanceToRightEnd, tangent, normal, sideH);
                anyLeft = leftOk && Reachable(leftFrom, leftEye)
                    && SweepTwoPosition(leftEye, leftFrom, two, sweepDir, out leftMin, out leftMax, out leftLow, out leftHigh, out leftMask);
                anyTop = Reachable(topFrom, topEye)
                    && SweepTwoPosition(topEye, topFrom, two, sweepDir, out topMin, out topMax, out topLow, out topHigh);
                anyRight = rightOk && Reachable(rightFrom, rightEye)
                    && SweepTwoPosition(rightEye, rightFrom, two, sweepDir, out rightMin, out rightMax, out rightLow, out rightHigh, out rightMask);
                if (_settings.ContiguousLeanArc)
                {
                    // The lean-left arc is anchored at -90 and runs inward; the lean-right arc at
                    // +90 and runs the other way. Either dies if its own outer edge is blocked.
                    if (anyLeft)
                    {
                        int k = ContiguousRun(leftMask, 0, 1, 13);
                        if (k < 0) { anyLeft = false; }
                        else leftMax = YawAt(k);
                    }
                    if (anyRight)
                    {
                        int k = ContiguousRun(rightMask, 12, -1, 13);
                        if (k < 0) { anyRight = false; }
                        else rightMin = YawAt(k);
                    }
                }
                if (!anyLeft) { leftMin = leftMax = leftLow = leftHigh = 0f; }
                if (!anyTop) { topMin = topMax = topLow = topHigh = 0f; }
                if (!anyRight) { rightMin = rightMax = rightLow = rightHigh = 0f; }
            }
            else
            {
                float chest = Math.Min(coverHeight, 1.2f) * 0.75f;
                // The legacy eye sits where the occupant stands, on the walkable side of the cover
                // line - tracing from the line itself puts it inside the wall.
                Vector3 basePos = slotPosition + normal * _settings.SlotStandOffset;

                // Lean out past the end of the cover, but only as far as the cover actually extends.
                Vector3 leftEye = basePos - tangent * Math.Min(distanceToLeftEnd, lean) + Vector3.UnitY * chest;
                Vector3 rightEye = basePos + tangent * Math.Min(distanceToRightEnd, lean) + Vector3.UnitY * chest;
                Vector3 topEye = basePos + Vector3.UnitY * (coverHeight + 0.15f);

                anyLeft = Sweep(leftEye, normal, out leftMin, out leftMax, out leftLow, out leftHigh);
                anyTop = Sweep(topEye, normal, out topMin, out topMax, out topLow, out topHigh);
                anyRight = Sweep(rightEye, normal, out rightMin, out rightMax, out rightLow, out rightHigh);
            }

            // You can only lean a way the cover does not keep going. Clamping the lean eye to the
            // segment end (above) stops it walking through the wall, but it still leaves the eye
            // flat against a wall that carries on past it, and sweeping from there reports an arc
            // retail does not have: its lean arcs are DEAD on 38.0% / 37.3% of slots, ours on
            // 4.2% / 10.1%. A slot in the middle of a long segment cannot lean either way.
            // A slot leans past the end it is nearer to and no further. Retail is absolute about
            // this - see CoverBakeSettings.LeanOnlyTowardNearerEnd - and the two-position lean eye
            // geometry does not produce it on its own, because a lean eye past the far end can
            // still land in open air wherever the wall happens to stop.
            if (_settings.LeanOnlyTowardNearerEnd)
            {
                const float tie = 0.01f;
                if (distanceToLeftEnd > distanceToRightEnd + tie)
                { anyLeft = false; leftMin = leftMax = leftLow = leftHigh = 0f; }
                if (distanceToRightEnd > distanceToLeftEnd + tie)
                { anyRight = false; rightMin = rightMax = rightLow = rightHigh = 0f; }
            }

            if (_settings.LeanNeedsAnEnd)
            {
                float reach = _settings.LeanMaxDistanceToEnd > 0f ? _settings.LeanMaxDistanceToEnd : lean;
                if (distanceToLeftEnd > reach) { anyLeft = false; leftMin = leftMax = leftLow = leftHigh = 0f; }
                if (distanceToRightEnd > reach) { anyRight = false; rightMin = rightMax = rightLow = rightHigh = 0f; }
            }

            // You never shoot over HIGH cover. Retail writes a zero-width over-the-top arc on 3,270
            // of 3,270 shipped high slots - both nibbles are 0, so the arc reads -90 to -90 - against
            // a 180 degree median on low cover, where it is zero on only 0.2%. That is a rule, not a
            // measurement, and without it 78% of our high slots tell the AI it can fire over a 1.6 m
            // wall. Measured with `diag coverslots`.
            if (_settings.UseTwoPositionAim
                    ? coverHeight >= _settings.AimShootHeightOver
                    : coverHeight >= _settings.LowHighDividingLine)
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
            // How far PAST straight ahead a lean can reach. Leaning out to the left, the cover you
            // are leaning around blocks the right half of your sweep, and nothing in the obstacle
            // probe models that - so ours ran to the sweep limit and wrote a full 180 degree arc.
            // Retail's live lean arcs sit at +18 degrees on the left and -18 on the right at the
            // median, on BOTH cover classes, with only 2-7% of left leans reaching +90 and none of
            // the right leans reaching -90. Ours were pinned at the limit on 65-86%.
            // Measured with `diag coverslots` (INNER edge of a LIVE lean arc).
            float leanInner = _settings.LeanInnerLimitDegrees * (float)(Math.PI / 180.0);
            if (leanInner > 0f)
            {
                if (leftMax > leanInner) leftMax = leanInner;
                if (rightMin < -leanInner) rightMin = -leanInner;
            }

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
            int pitchSamples = Math.Max(2, _settings.AimPitchSamples);
            float pitchLimit = _settings.AimPitchLimitDegrees * (float)(Math.PI / 180.0);
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
                    // Elevation built from cos/sin rather than a tangent: tan blows up approaching
                    // +-90 degrees, and the sweep now goes that far.
                    float pitch = -pitchLimit + p * (2f * pitchLimit / (pitchSamples - 1));
                    Vector3 aim = dir * (float)Math.Cos(pitch) + Vector3.UnitY * (float)Math.Sin(pitch);
                    // Ground and ceiling both bound the sweep long before the aim range does, and
                    // neither is something an NPC is prevented from shooting past - see AimDownRange.
                    float rayRange = range;
                    if (aim.Y < -0.001f && _settings.AimDownRange > 0f) rayRange = _settings.AimDownRange;
                    else if (aim.Y > 0.001f && _settings.AimUpRange > 0f) rayRange = _settings.AimUpRange;
                    if (!Clear(origin, aim, rayRange))
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

        /// <summary>
        /// The two-position clear-aim sweep: a horizontal cone tested at eye level out to
        /// <see cref="CoverBakeSettings.AimClearDistance"/>, and a vertical cone tested out to
        /// <see cref="CoverBakeSettings.AimClearDistanceVerticalCone"/>, optionally requiring both
        /// the shoot position and the move-from position to have the line.
        /// </summary>
        /// <remarks>
        /// Two things differ from <see cref="Sweep"/>. A yaw is decided by the LEVEL ray alone - the
        /// old sweep called a yaw clear if ANY elevation in it was clear, so a gap above a desk
        /// opened a firing direction straight into the desk. And the two cones get their own
        /// distances, which is what stopped the floor and the ceiling from bounding the horizontal
        /// arc; <see cref="CoverBakeSettings.AimDownRange"/> existed only to paper over that.
        /// </remarks>
        private bool SweepTwoPosition(Vector3 origin, Vector3 from, bool useFrom, Vector3 forward,
                                 out float minYaw, out float maxYaw, out float minPitch, out float maxPitch)
        {
            return SweepTwoPosition(origin, from, useFrom, forward, out minYaw, out maxYaw,
                               out minPitch, out maxPitch, out int _);
        }

        /// <summary>
        /// As above, and reports WHICH yaw samples came back clear as a bitmask, so the caller can
        /// take a contiguous run rather than the outer bounds of a set with holes in it.
        /// </summary>
        private bool SweepTwoPosition(Vector3 origin, Vector3 from, bool useFrom, Vector3 forward,
                                 out float minYaw, out float maxYaw, out float minPitch, out float maxPitch,
                                 out int clearMask)
        {
            const int yawSamples = 13;   // every 15 degrees across the 180 degree window
            clearMask = 0;
            int pitchSamples = Math.Max(2, _settings.AimPitchSamples);
            float pitchLimit = _settings.AimPitchLimitDegrees * (float)(Math.PI / 180.0);
            float horiz = _settings.AimClearDistance > 0f ? _settings.AimClearDistance : 1.5f;
            float vert = _settings.AimClearDistanceVerticalCone > 0f ? _settings.AimClearDistanceVerticalCone : horiz;

            minYaw = 0; maxYaw = 0; minPitch = 0; maxPitch = 0;

            Vector3 flatForward = new Vector3(forward.X, 0, forward.Z);
            if (flatForward.LengthSquared() < 1e-6f)
                return false;
            flatForward = Vector3.Normalize(flatForward);
            Vector3 right = Vector3.Normalize(new Vector3(flatForward.Z, 0, -flatForward.X));

            bool any = false;
            const float halfPi = (float)(Math.PI / 2.0);

            for (int i = 0; i < yawSamples; i++)
            {
                float yaw = -halfPi + i * (2f * halfPi / (yawSamples - 1));
                float cy = (float)Math.Cos(yaw), sy = (float)Math.Sin(yaw);
                Vector3 dir = flatForward * cy + right * sy;

                if (!ClearFrom(origin, from, useFrom, dir, horiz))
                    continue;
                clearMask |= 1 << i;

                bool anyPitch = false;
                float loPitch = 0, hiPitch = 0;
                for (int p = 0; p < pitchSamples; p++)
                {
                    float pitch = -pitchLimit + p * (2f * pitchLimit / (pitchSamples - 1));
                    Vector3 aim = dir * (float)Math.Cos(pitch) + Vector3.UnitY * (float)Math.Sin(pitch);
                    if (!ClearFrom(origin, from, useFrom, aim, vert))
                        continue;
                    if (!anyPitch) { loPitch = pitch; hiPitch = pitch; anyPitch = true; }
                    else { if (pitch < loPitch) loPitch = pitch; if (pitch > hiPitch) hiPitch = pitch; }
                }

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

        /// <summary>
        /// A direction is aimable when the shoot position has it, and - when the two-
        /// position model is on - the position the NPC moves from has it too.
        /// </summary>
        /// <summary>
        /// Can the NPC get from its move-from position to the shoot position? See
        /// <see cref="CoverBakeSettings.RequireShootPositionReachable"/>.
        /// </summary>
        /// <summary>
        /// Walk from <paramref name="start"/> in <paramref name="step"/> while the samples stay
        /// clear, and return the index of the last one. -1 when the first is already blocked.
        /// </summary>
        private static int ContiguousRun(int mask, int start, int step, int count)
        {
            if ((mask & (1 << start)) == 0)
                return -1;
            int last = start;
            for (int i = start + step; i >= 0 && i < count; i += step)
            {
                if ((mask & (1 << i)) == 0) break;
                last = i;
            }
            return last;
        }

        /// <summary>Yaw of sample i in the 13 sample, -90..+90 sweep.</summary>
        private static float YawAt(int i) => (float)(-Math.PI / 2.0 + i * (Math.PI / 12.0));

        /// <summary>
        /// Is there room past the end of the cover to step out into? See
        /// <see cref="CoverBakeSettings.UseLeanClearanceTest"/>. Traced from just outside the
        /// cover face so the ray starts in open air rather than in the collision surface.
        /// </summary>
        private bool LeanClearance(Vector3 end, Vector3 outward, Vector3 normal, float height)
        {
            if (_bvh == null || _settings.LeanClearanceDistance <= 0f)
                return true;
            Vector3 origin = end + normal * 0.1f + Vector3.UnitY * height;
            return Clear(origin, outward, _settings.LeanClearanceDistance);
        }

        private bool Reachable(Vector3 from, Vector3 shoot)
        {
            if (!_settings.RequireShootPositionReachable || _bvh == null)
                return true;
            Vector3 d = shoot - from;
            float len = d.Length();
            if (len < 1e-4f)
                return true;
            return Clear(from, d / len, len);
        }

        private bool ClearFrom(Vector3 origin, Vector3 from, bool useFrom, Vector3 direction, float range)
        {
            if (!Clear(origin, direction, range))
                return false;
            return !useFrom || Clear(from, direction, range);
        }

        private bool Clear(Vector3 origin, Vector3 direction, float range)
        {
            var ray = new Ray(origin, direction, 0.02f, range);
            return !_bvh.Occluded(ref ray);
        }
    }
}
#endif
