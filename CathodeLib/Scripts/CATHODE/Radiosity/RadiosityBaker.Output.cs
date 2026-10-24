#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;
using System.Numerics;
using CATHODE;

namespace CathodeLib.Radiosity
{
    public static partial class RadiosityBaker
    {
        #region OUTPUT

        /// <summary>
        /// Write the lightmap atlas transform as <c>(width - 0.5, height - 0.5, x, y)</c>.
        /// </summary>
        private static void WriteModelParams(Movers.MOVER_DESCRIPTOR mover, RadiosityGeometry.Instance instance)
        {
            // Only the first four floats of the 84-byte block are the lightmap transform; the rest
            // is per-renderable-type data that must survive untouched.
            byte[] raw = mover.RenderConstants.RawBytes;
            WriteFloat(raw, 0, instance.AtlasWidth - 0.5f);
            WriteFloat(raw, 4, instance.AtlasHeight - 0.5f);
            WriteFloat(raw, 8, instance.AtlasX);
            WriteFloat(raw, 12, instance.AtlasY);
            mover.RenderConstants.SetRawBytes(raw);
        }

        /// <summary>
        /// Zero the lightmap transform on movers that use MODEL_PARAMS but did not end up in the
        /// bake. Without this they keep the rect from whatever bake produced the level last, which
        /// points into an atlas that no longer exists.
        /// </summary>
        private static int ClearStaleModelParams(Level level, HashSet<int> taggedMovers)
        {
            int cleared = 0;
            for (int i = 0; i < level.Movers.Entries.Count; i++)
            {
                if (taggedMovers.Contains(i))
                    continue;

                Movers.MOVER_DESCRIPTOR mover = level.Movers.Entries[i];
                if (mover.RenderableElements == null || mover.RenderableElements.Count == 0)
                    continue;

                RenderableInstanceType type;
                try { type = mover.GetRenderableType(); }
                catch { continue; }

                if (type != RenderableInstanceType.ENVIRONMENT &&
                    type != RenderableInstanceType.ENVIRONMENT_EXTRA &&
                    type != RenderableInstanceType.MISC)
                    continue;

                byte[] raw = mover.RenderConstants.RawBytes;
                if (BitConverter.ToSingle(raw, 0) == 0 && BitConverter.ToSingle(raw, 4) == 0 &&
                    BitConverter.ToSingle(raw, 8) == 0 && BitConverter.ToSingle(raw, 12) == 0)
                    continue;

                WriteFloat(raw, 0, 0f);
                WriteFloat(raw, 4, 0f);
                WriteFloat(raw, 8, 0f);
                WriteFloat(raw, 12, 0f);
                mover.RenderConstants.SetRawBytes(raw);
                cleared++;
            }
            return cleared;
        }

        private static void WriteFloat(byte[] buffer, int offset, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
        }

        /// <summary>
        /// Every slice lists all the others as neighbours, which is what retail does at these slice
        /// counts (BSP_TORRENS 2 slices / 1 neighbour each, Solace 3 slices / 2 neighbours each).
        /// </summary>
        private static void BuildSliceNeighbours(RadiosityRuntime runtime)
        {
            runtime.SliceNeighbourCounts.Clear();
            runtime.SliceNeighbourArrayOffsets.Clear();
            runtime.FlattenedOtherSliceIndices.Clear();

            int count = runtime.Slices.Count;
            for (int i = 0; i < count; i++)
            {
                runtime.SliceNeighbourArrayOffsets.Add((short)runtime.FlattenedOtherSliceIndices.Count);
                int neighbours = 0;
                for (int j = 0; j < count; j++)
                {
                    if (i == j) continue;
                    runtime.FlattenedOtherSliceIndices.Add((byte)j);
                    neighbours++;
                }
                runtime.SliceNeighbourCounts.Add((byte)neighbours);
            }
        }

        /// <summary>
        /// Emit each slice's door transfer set: the probe pairs whose light path passes through a
        /// NavMeshBarrier, so the runtime can switch that transfer on and off with the door.
        /// </summary>
        private static int BuildDoors(
            Level level, RadiosityGeometry geometry, SliceBake[] slices, Action<string> log)
        {
            RadiosityDoors doors = RadiosityDoors.CollectFromLevel(level, log);
            if (doors.Barriers.Count == 0)
                return 0;

            int total = 0;
            foreach (SliceBake slice in slices)
            {
                var info = new RadiosityRuntime.DoorInfo();
                if (slice?.Texels == null)
                {
                    slice.Slice.Doors = info;
                    continue;
                }

                var live = new List<int>();
                for (int i = 0; i < AtlasTexels; i++)
                    if (slice.Texels[i].Live) live.Add(i);
                if (live.Count == 0)
                {
                    slice.Slice.Doors = info;
                    continue;
                }

                var grid = new ProbeGrid(slice.Texels, live, RadiosityTuning.DoorTransferRadius);

                foreach (RadiosityDoors.Barrier barrier in doors.Barriers)
                {
                    // Per-slice ceiling: transfers past retail's envelope (<=161 per slice on
                    // every level measured) overrun an engine buffer - CM3 shipped 440/731 and
                    // heap-corrupted in RADIOSITY::destroy on every level close. Whole doors are
                    // dropped rather than partially emitted.
                    if (info.Transfers.Count + RadiosityTuning.MaxTransfersPerDoor > RadiosityTuning.DoorTransfersPerSliceCeiling)
                        break;

                    float reach = RadiosityTuning.DoorTransferRadius + barrier.Radius;
                    float reachSq = reach * reach;

                    // Probes either side of the barrier, within reach of it.
                    var nearby = new List<int>();
                    foreach (int texel in grid.Neighbours(barrier.Centre))
                    {
                        if ((slice.Texels[texel].Position - barrier.Centre).LengthSquared() <= reachSq)
                            nearby.Add(texel);
                    }
                    if (nearby.Count < 2)
                        continue;

                    int first = info.Transfers.Count;
                    int emitted = 0;

                    foreach (int receiverTexel in nearby)
                    {
                        if (emitted >= RadiosityTuning.MaxTransfersPerDoor)
                            break;

                        SurfaceTexel receiver = slice.Texels[receiverTexel];
                        Vector3 toReceiver = receiver.Position - barrier.Centre;

                        foreach (int emitterTexel in nearby)
                        {
                            if (emitterTexel == receiverTexel)
                                continue;
                            int inputProbe = slice.InputProbeForTexel[emitterTexel];
                            if (inputProbe < 0)
                                continue;

                            SurfaceTexel emitter = slice.Texels[emitterTexel];

                            // Opposite sides of the barrier, or the pair is not going through it.
                            if (Vector3.Dot(toReceiver, emitter.Position - barrier.Centre) >= 0)
                                continue;

                            // With the door open the path has to be clear; that is the state the
                            // transfer describes.
                            if (!geometry.Visible(
                                    emitter.Position + emitter.Normal * RadiosityTuning.ProbeSurfaceOffset,
                                    receiver.Position + receiver.Normal * RadiosityTuning.ProbeSurfaceOffset,
                                    RadiosityTuning.RayEpsilon))
                                continue;

                            InputProbeTexel(inputProbe, out int ex, out int ey);
                            info.Transfers.Add(new RadiosityRuntime.DoorTransfer
                            {
                                InputProbe = new Vector2u8 { X = (byte)ex, Y = (byte)ey },
                                SurfaceProbe = new Vector2u8
                                {
                                    X = (byte)(receiverTexel % AtlasSize),
                                    Y = (byte)(receiverTexel / AtlasSize)
                                },
                                Weight = -1f
                            });
                            emitted++;
                            break;
                        }
                    }

                    if (emitted == 0)
                        continue;

                    info.Doors.Add(new SliceU16 { Offset = (ushort)first, Count = (ushort)emitted });
                    info.NavmeshBarrierCathodeInstanceIndex.Add(barrier.CollisionInstanceIndex);
                    total += emitted;
                }

                slice.Slice.Doors = info;
            }
            return total;
        }

        /// <summary>
        /// Emit empty fixup ranges, one per neighbour entry, and no fixups: the full path's ranges until
        /// <see cref="RebuildLinksRetailCut"/> writes the cross-slice fixups with the rest of each probe's links.
        /// </summary>
        private static void ClearCrossSliceFixups(RadiosityRuntime runtime)
        {
            runtime.InfluenceFixups.Clear();
            runtime.FlattenedFixupRanges.Clear();
            for (int i = 0; i < runtime.FlattenedOtherSliceIndices.Count; i++)
                runtime.FlattenedFixupRanges.Add(new RadiosityRuntime.FixupRange { First = 0, Num = 0 });
        }

        #endregion

        #region HELPERS

        private static List<T> NewList<T>(int count) where T : new()
        {
            var list = new List<T>(count);
            for (int i = 0; i < count; i++) list.Add(new T());
            return list;
        }

        private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        private static float Fract(float v) => v - (float)Math.Floor(v);

        private static byte ToByte(float unitValue) =>
            (byte)Math.Max(0, Math.Min(255, (int)Math.Round(unitValue * 255.0f)));

        /// <summary>
        /// Ceiling on stored input-probe albedo LUMA, decoded from retail: no retail probe reaches pure white (SCI_Hub,
        /// Solace, CM3, CM9: luma p99 0.90-0.91, max channel byte 252). An over-unity albedo makes the runtime relaxation
        /// loop diverge and the level renders a bloom WHITEOUT (SCI_Hub). The cap is colour-preserving: only the luma is
        /// scaled down, the shape of retail's data.
        /// </summary>
        private const float MaxAlbedoLuma = 232.0f / 255.0f;

        /// <summary>
        /// Write an albedo into the input probe albedo texture, which the engine reads as BGRA (retail's Solace bytes rise
        /// R 91 / G 98 / B 103 toward blue where ours fell before the swap, and the mirror cast a green-warm tint over every
        /// bounce), capped at <see cref="MaxAlbedoLuma"/>.
        /// </summary>
        private static ColourRGBA8 EncodeAlbedo(Vector3 colour, byte alpha)
        {
            // colour arrives as (B, G, R) - see the swizzle below - so Z is the red channel.
            float luma = 0.2126f * colour.Z + 0.7152f * colour.Y + 0.0722f * colour.X;
            if (luma > MaxAlbedoLuma)
                colour *= MaxAlbedoLuma / luma;
            return new ColourRGBA8
            {
                R = ToByte(colour.Z),
                G = ToByte(colour.Y),
                B = ToByte(colour.X),
                A = alpha
            };
        }

        private static int HighestPowerOfTwoBelow(int n) { int p = 1; while (p * 2 < n) p *= 2; return p; }

        /// <summary>
        /// Normals are stored biased into 0..255 (127 is zero, 255 is +1) in RETAIL'S byte order, (z, y, x): decoded 18 Sep
        /// 2026 by pairing our input probes with retail's on Solace. Written (x, y, z), every X- or Z-facing wall carried a
        /// normal turned 90 degrees.
        /// </summary>
        private static ColourRGBA8 EncodeNormal(Vector3 normal) => new ColourRGBA8
        {
            R = ToByte(normal.Z * 0.5f + 0.5f),
            G = ToByte(normal.Y * 0.5f + 0.5f),
            B = ToByte(normal.X * 0.5f + 0.5f),
            A = 255
        };

        private static Vector4u16 ToHalf4(Vector3 v, float w) => new Vector4u16
        {
            X = ToHalf(v.X),
            Y = ToHalf(v.Y),
            Z = ToHalf(v.Z),
            W = ToHalf(w)
        };

        /// <summary>Closest point on triangle (a,b,c) to p - the standard region walk.</summary>
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1.0f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        private static Vector3 FromHalf3(Vector4u16 v) => new Vector3(FromHalf(v.X), FromHalf(v.Y), FromHalf(v.Z));

        /// <summary>IEEE 754 binary16 decode, the inverse of <see cref="ToHalf"/>.</summary>
        private static float FromHalf(ushort h)
        {
            uint sign = (uint)(h >> 15) & 1;
            int exponent = (h >> 10) & 0x1F;
            uint mantissa = (uint)h & 0x3FF;
            uint bits;
            if (exponent == 0)
            {
                if (mantissa == 0)
                {
                    bits = sign << 31;
                }
                else
                {
                    // Subnormal half: renormalise into the float exponent range.
                    exponent = 127 - 15 + 1;
                    while ((mantissa & 0x400) == 0) { mantissa <<= 1; exponent--; }
                    bits = (sign << 31) | ((uint)exponent << 23) | ((mantissa & 0x3FF) << 13);
                }
            }
            else if (exponent == 0x1F)
            {
                bits = (sign << 31) | 0x7F800000 | (mantissa << 13);
            }
            else
            {
                bits = (sign << 31) | ((uint)(exponent - 15 + 127) << 23) | (mantissa << 13);
            }
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        /// <summary>
        /// IEEE 754 binary16 encode. Rounds half up on the first dropped bit (bit 12; not round-to-nearest-even), clamps
        /// overflow, Inf and NaN to the largest finite half (0x7BFF, signed), and flushes values below the subnormal
        /// range to signed zero.
        /// </summary>
        private static ushort ToHalf(float value)
        {
            uint bits = (uint)BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            uint sign = (bits >> 16) & 0x8000;
            int exponent = (int)((bits >> 23) & 0xFF) - 127 + 15;
            uint mantissa = bits & 0x7FFFFF;

            if (exponent >= 0x1F)
                return (ushort)(sign | 0x7BFF); // Clamp to the largest finite half.
            if (exponent <= 0)
            {
                if (exponent < -10)
                    return (ushort)sign;
                mantissa |= 0x800000;
                int shift = 14 - exponent;
                uint sub = mantissa >> shift;
                if (((mantissa >> (shift - 1)) & 1) != 0) sub++;
                return (ushort)(sign | sub);
            }

            uint half = (uint)(sign | ((uint)exponent << 10) | (mantissa >> 13));
            if ((mantissa & 0x1000) != 0) half++;
            return (ushort)half;
        }

        #endregion
    }
}
#endif
