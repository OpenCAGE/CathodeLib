using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using CATHODE;
using static CATHODE.HavokPackfile;

namespace CathodeLib.Havok
{
    /* Content keys for packfile object graphs, so an import can tell when the destination already holds what it would copy.

       A graph is read from its root as blocks. A block starts at an object or at a pointer target and runs to the next of either (or
       the payload's end). Each block gives its bytes, with pointer fields and any masked fields read as zero and trailing zeros
       dropped, then its pointers: where each sits in the block, local or global, and which block it leads to, blocks numbered in the
       order the walk first meets them. The key is a hash of all of that. Two graphs with one key hold the same bytes behind the same
       pointers whatever order and padding each file lays its blocks out with - an import copies bytes and fixups as they are, so a
       copy keys the same as its original. Padding is zero after a copy and ends a block, so dropping trailing zeros keeps the two
       apart only where real data differs. Packfiles only: a tagfile names its pointers by item, not fixup, and is never keyed. */
    /// <summary>
    /// Content keys for the object graphs of a packfile (a collision proxy with its meshes, a physics system with its bodies), so an
    /// import can reuse a copy the destination already holds instead of appending another.
    /// </summary>
    public static class HavokEquivalence
    {
        /// <summary>A graph of more blocks than this is not keyed: it is imported as it would be otherwise.</summary>
        const int MaxBlocks = 1 << 16;

        sealed class Layout
        {
            public byte[] Payload;
            public int Locals, Globals, Objects;
            public uint[] BlockStarts;
            public uint[] FixupSrc;
            public uint[] FixupDst;
            public byte[] FixupKind;   //0 local, 1 global into the data section, 2 global elsewhere
        }

        static readonly ConditionalWeakTable<HavokPackfile, Layout> _layouts = new ConditionalWeakTable<HavokPackfile, Layout>();

        /* Rebuilt whenever the packfile has changed since: an import replaces the payload array and appends fixups and objects */
        static Layout LayoutOf(HavokPackfile hkx)
        {
            lock (_layouts)
            {
                if (_layouts.TryGetValue(hkx, out Layout cached) && ReferenceEquals(cached.Payload, hkx.DataPayload) &&
                    cached.Locals == hkx.LocalFixups.Count && cached.Globals == hkx.GlobalFixups.Count && cached.Objects == hkx.Objects.Count)
                    return cached;

                int count = hkx.LocalFixups.Count + hkx.GlobalFixups.Count;
                uint[] src = new uint[count];
                ulong[] packed = new ulong[count];
                int n = 0;
                foreach (LocalFixup lf in hkx.LocalFixups)
                {
                    src[n] = lf.Src;
                    packed[n] = ((ulong)lf.Dst << 2) | 0u;
                    n++;
                }
                foreach (GlobalFixup gf in hkx.GlobalFixups)
                {
                    src[n] = gf.Src;
                    packed[n] = ((ulong)gf.Dst << 2) | (gf.DstSectionIndex == 2 ? 1u : 2u);
                    n++;
                }
                Array.Sort(src, packed);

                Layout layout = new Layout
                {
                    Payload = hkx.DataPayload,
                    Locals = hkx.LocalFixups.Count,
                    Globals = hkx.GlobalFixups.Count,
                    Objects = hkx.Objects.Count,
                    FixupSrc = src,
                    FixupDst = new uint[count],
                    FixupKind = new byte[count],
                };
                List<uint> starts = new List<uint>(hkx.Objects.Count + count);
                foreach (PackfileObject o in hkx.Objects)
                    starts.Add(o.DataOffset);
                for (int i = 0; i < count; i++)
                {
                    layout.FixupDst[i] = (uint)(packed[i] >> 2);
                    layout.FixupKind[i] = (byte)(packed[i] & 3);
                    if (layout.FixupKind[i] != 2)
                        starts.Add(layout.FixupDst[i]);
                }
                starts.Sort();
                List<uint> unique = new List<uint>(starts.Count);
                foreach (uint s in starts)
                    if (unique.Count == 0 || unique[unique.Count - 1] != s)
                        unique.Add(s);
                layout.BlockStarts = unique.ToArray();

                _layouts.Remove(hkx);
                _layouts.Add(hkx, layout);
                return layout;
            }
        }

        static int FirstAtOrAfter(uint[] sorted, uint value)
        {
            int lo = 0, hi = sorted.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (sorted[mid] < value) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// The content key of the graph reached from <paramref name="root"/> (a payload offset), with the byte ranges in
        /// <paramref name="masked"/> (payload offset, length) read as zero. Null for a tagfile, a root outside the payload or a graph
        /// too large to key.
        /// </summary>
        public static string KeyOf(HavokPackfile hkx, uint root, IEnumerable<(uint At, int Length)> masked = null)
        {
            if (hkx == null || hkx.IsTagfile || hkx.DataPayload == null || root >= (uint)hkx.DataPayload.Length)
                return null;
            Layout layout = LayoutOf(hkx);
            byte[] payload = layout.Payload;
            int ptr = hkx.Header.PointerSize;

            List<(uint At, int Length)> masks = new List<(uint, int)>();
            if (masked != null)
                foreach ((uint At, int Length) m in masked)
                    if (m.Length > 0)
                        masks.Add(m);

            Dictionary<uint, int> numbered = new Dictionary<uint, int> { { root, 0 } };
            List<uint> order = new List<uint> { root };
            using (SHA256 sha = SHA256.Create())
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                byte[] block = new byte[256];
                for (int b = 0; b < order.Count; b++)
                {
                    uint start = order[b];
                    if (start >= (uint)payload.Length)
                        return null;
                    int next = FirstAtOrAfter(layout.BlockStarts, start + 1);
                    uint end = next < layout.BlockStarts.Length ? Math.Min(layout.BlockStarts[next], (uint)payload.Length) : (uint)payload.Length;
                    int length = (int)(end - start);
                    if (block.Length < length)
                        block = new byte[Math.Max(length, block.Length * 2)];
                    Buffer.BlockCopy(payload, (int)start, block, 0, length);

                    int f0 = FirstAtOrAfter(layout.FixupSrc, start), f1 = FirstAtOrAfter(layout.FixupSrc, end);
                    for (int f = f0; f < f1; f++)
                        ClearRange(block, length, (int)(layout.FixupSrc[f] - start), ptr);
                    foreach ((uint At, int Length) m in masks)
                        if (m.At < end && m.At + (uint)m.Length > start)
                            ClearRange(block, length, (int)((long)m.At - start), m.Length);

                    int kept = length;
                    while (kept > 0 && block[kept - 1] == 0)
                        kept--;
                    writer.Write(kept);
                    writer.Write(block, 0, kept);

                    writer.Write(f1 - f0);
                    for (int f = f0; f < f1; f++)
                    {
                        writer.Write(layout.FixupSrc[f] - start);
                        writer.Write(layout.FixupKind[f]);
                        uint dst = layout.FixupDst[f];
                        if (layout.FixupKind[f] == 2 || dst >= (uint)payload.Length)
                        {
                            writer.Write(dst);
                            continue;
                        }
                        if (!numbered.TryGetValue(dst, out int id))
                        {
                            id = order.Count;
                            if (id >= MaxBlocks)
                                return null;
                            numbered.Add(dst, id);
                            order.Add(dst);
                        }
                        writer.Write(id);
                    }

                    //Hash as it goes, so a large graph is not held whole
                    if (stream.Length > (1 << 20))
                    {
                        writer.Flush();
                        sha.TransformBlock(stream.GetBuffer(), 0, (int)stream.Length, null, 0);
                        stream.SetLength(0);
                    }
                }
                writer.Flush();
                sha.TransformFinalBlock(stream.GetBuffer(), 0, (int)stream.Length);
                return Convert.ToBase64String(sha.Hash);
            }
        }

        static void ClearRange(byte[] block, int length, int from, int count)
        {
            int a = Math.Max(0, from), z = Math.Min(length, from + count);
            for (int i = a; i < z; i++)
                block[i] = 0;
        }

        /// <summary>
        /// Where a template proxy's userData sits - its own, each instance's and each instance's shape's. They name the level's render
        /// material by its index, which an import retargets, so a proxy is keyed without them and its materials are compared apart.
        /// </summary>
        public static IEnumerable<(uint At, int Length)> TemplateUserDataFields(HavokPackfile hkx, StaticCompoundShape compound)
        {
            if (hkx == null || compound == null)
                yield break;
            int ptr = hkx.Header.PointerSize;
            int at = ptr == 8 ? 24 : 12;
            int width = ptr == 8 ? 8 : 4;
            yield return (compound.DataOffset + (uint)at, width);
            foreach (CompoundInstance instance in compound.Instances)
            {
                if (instance == null)
                    continue;
                yield return (instance.DataOffset + 48u + (uint)ptr + 8u, ptr);
                if (instance.ShapeDataOffset != 0)
                    yield return (instance.ShapeDataOffset + (uint)at, width);
            }
        }

        /// <summary>Whether two template proxies carry the same userData, field for field (see <see cref="TemplateUserDataFields"/>).</summary>
        public static bool SameTemplateUserData(HavokPackfile a, StaticCompoundShape x, HavokPackfile b, StaticCompoundShape y)
        {
            if (a == null || b == null || x == null || y == null || a.IsTagfile || b.IsTagfile || a.Header.PointerSize != b.Header.PointerSize)
                return false;
            List<(uint At, int Length)> fieldsA = new List<(uint, int)>(TemplateUserDataFields(a, x));
            List<(uint At, int Length)> fieldsB = new List<(uint, int)>(TemplateUserDataFields(b, y));
            if (fieldsA.Count != fieldsB.Count)
                return false;
            for (int i = 0; i < fieldsA.Count; i++)
            {
                (uint atA, int length) = fieldsA[i];
                uint atB = fieldsB[i].At;
                if (length != fieldsB[i].Length || atA + (uint)length > (uint)a.DataPayload.Length || atB + (uint)length > (uint)b.DataPayload.Length)
                    return false;
                for (int k = 0; k < length; k++)
                    if (a.DataPayload[atA + k] != b.DataPayload[atB + k])
                        return false;
            }
            return true;
        }

        /// <summary>The content key of a template collision proxy, its material indices aside (see <see cref="TemplateUserDataFields"/>).</summary>
        public static string KeyOf(HavokPackfile hkx, StaticCompoundShape compound)
        {
            return compound == null ? null : KeyOf(hkx, compound.DataOffset, TemplateUserDataFields(hkx, compound));
        }

        /// <summary>The content key of a physics system with everything it reaches.</summary>
        public static string KeyOf(HavokPackfile hkx, PhysicsSystem system)
        {
            return system == null ? null : KeyOf(hkx, system.DataOffset);
        }
    }
}
