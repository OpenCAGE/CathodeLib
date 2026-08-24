using System;
using System.Collections.Generic;
using System.Linq;

namespace CATHODE
{
    /// <summary>
    /// Minimal DXBC (D3D11 shader container) utilities: chunk parsing, the DXBC modified-MD5
    /// checksum, an SM5 token walker, and the static-to-dynamic radiosity pixel-shader patch.
    /// </summary>
    /// <remarks>
    /// <para>The patch applies the exact transformation CA's own compiler produces between the
    /// static/dynamic radiosity permutations of a shader, discovered by diffing the shipped twin
    /// pairs (same ubershader family, same features modulo the family's radiosity feature bits):
    /// declare cbInstanceXSC at cb10 and retarget the two mangle-decode instructions from the
    /// interpolated lightmap UV (v1.zw) to the per-instance RadiosityProbeTexcoordAndScale
    /// (cb10[11].xy). Everything else in the shader - the whole sampling chain - is identical
    /// between the permutations, because dynamic radiosity IS the lightmap pipeline sampled at
    /// one engine-supplied coordinate per instance.</para>
    /// <para>Golden-tested: patching CA's shipped static shaders reproduces their shipped
    /// dynamic twins' SHEX to exactly one deliberate dword (the dcl_input_ps v1 mask stays xyzw
    /// because the unpatched vertex shader still feeds it - harmless). The checksum
    /// implementation verifies bit-exact against every shipped blob. The vertex shader and the
    /// reflection (RDEF) chunk are left untouched: the engine binds cbInstanceXSC per instance
    /// unconditionally (the VS reads it for the world matrix), and the extra lightmap-UV work
    /// in the VS is dead once the PS ignores v1.zw.</para>
    /// </remarks>
    public static class DxbcUtils
    {
        private static readonly uint[] K = Enumerable.Range(0, 64)
            .Select(i => (uint)(Math.Abs(Math.Sin(i + 1)) * 4294967296.0)).ToArray();
        private static readonly int[] S = {
            7,12,17,22,7,12,17,22,7,12,17,22,7,12,17,22,
            5,9,14,20,5,9,14,20,5,9,14,20,5,9,14,20,
            4,11,16,23,4,11,16,23,4,11,16,23,4,11,16,23,
            6,10,15,21,6,10,15,21,6,10,15,21,6,10,15,21 };

        private static void Md5Block(uint[] state, byte[] block, int offset)
        {
            uint a = state[0], b = state[1], c = state[2], d = state[3];
            var m = new uint[16];
            for (int i = 0; i < 16; i++) m[i] = BitConverter.ToUInt32(block, offset + i * 4);
            for (int i = 0; i < 64; i++)
            {
                uint f; int g;
                if (i < 16) { f = (b & c) | (~b & d); g = i; }
                else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) % 16; }
                else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) % 16; }
                else { f = c ^ (b | ~d); g = (7 * i) % 16; }
                uint tmp = d; d = c; c = b;
                uint x = a + f + K[i] + m[g];
                b = b + ((x << S[i]) | (x >> (32 - S[i])));
                a = tmp;
            }
            state[0] += a; state[1] += b; state[2] += c; state[3] += d;
        }

        /// <summary>
        /// The DXBC content hash (bytes 4..19 of the container): MD5 over bytes [20..end) with a
        /// non-standard finalisation. Verified bit-exact against every shipped shader blob.
        /// </summary>
        public static byte[] Checksum(byte[] data)
        {
            var state = new uint[] { 0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476 };
            int start = 20;
            int len = data.Length - start;
            int fullChunks = len / 64;
            int lastLen = len % 64;
            uint bits = (uint)(len * 8);

            for (int i = 0; i < fullChunks; i++)
                Md5Block(state, data, start + i * 64);

            int tailOff = start + fullChunks * 64;
            var block = new byte[64];
            if (lastLen >= 56)
            {
                Array.Copy(data, tailOff, block, 0, lastLen);
                block[lastLen] = 0x80;
                Md5Block(state, block, 0);
                var block2 = new byte[64];
                BitConverter.GetBytes(bits).CopyTo(block2, 0);
                BitConverter.GetBytes((bits >> 2) | 1).CopyTo(block2, 60);
                Md5Block(state, block2, 0);
            }
            else
            {
                BitConverter.GetBytes(bits).CopyTo(block, 0);
                Array.Copy(data, tailOff, block, 4, lastLen);
                block[4 + lastLen] = 0x80;
                BitConverter.GetBytes((bits >> 2) | 1).CopyTo(block, 60);
                Md5Block(state, block, 0);
            }
            var outBytes = new byte[16];
            for (int i = 0; i < 4; i++) BitConverter.GetBytes(state[i]).CopyTo(outBytes, i * 4);
            return outBytes;
        }

        /// <summary>
        /// Does this pixel shader's code contain the radiosity mangle-decode constant at all?
        /// A STATIC-class shader without it never reads the probe atlas, so its lighting does
        /// not depend on the lightmap rect and it needs no dynamic conversion.
        /// </summary>
        public static bool SamplesRadiosity(byte[] ps)
        {
            if (ps == null || ps.Length < 36 || BitConverter.ToUInt32(ps, 0) != 0x43425844) return false;
            int chunkCount = BitConverter.ToInt32(ps, 28);
            for (int i = 0; i < chunkCount; i++)
            {
                int off = BitConverter.ToInt32(ps, 32 + i * 4);
                if (ps[off] != 'S' || ps[off + 1] != 'H' || ps[off + 2] != 'E' || ps[off + 3] != 'X') continue;
                int size = BitConverter.ToInt32(ps, off + 4);
                for (int b = off + 8; b + 4 <= off + 8 + size; b += 4)
                    if (BitConverter.ToUInt32(ps, b) == 0x437fff00) return true;
            }
            return false;
        }

        /// <summary>
        /// Synthesize the dynamic-radiosity permutation of a static pixel shader. Returns null
        /// when the shader does not carry the radiosity sampling idiom (it never reads the probe
        /// atlas, so there is nothing to convert), already declares cb10, or carries the idiom
        /// in a shape this matcher does not recognise (bail safely rather than guess).
        /// </summary>
        /// <remarks>
        /// The mangle-decode site is one mul (opcode 0x38) and one mad (0x32) whose immediate
        /// operand holds 255.996094 (0x437fff00) in exactly two adjacent lanes. Across CA's
        /// permutations the interpolated lightmap UV arrives in different input registers and
        /// components (v1.zw, v4.xy, v6.xy...) and the active lanes move with the surrounding
        /// register allocation, but the shape is invariant: the source swizzle at the two active
        /// lanes selects the two components holding (u, v) - the lower-numbered component is u -
        /// and the dynamic twin replaces that source with cb10[11] swizzled so x lands in u's
        /// lane and y in v's. Verified against every shipped static/dynamic twin pair (golden
        /// test) and every failure shape shwhy dumped on ChallengeMap3.
        /// </remarks>
        public static byte[] PatchStaticToDynamic(byte[] ps)
        {
            if (ps == null || ps.Length < 36 || BitConverter.ToUInt32(ps, 0) != 0x43425844) return null;

            int chunkCount = BitConverter.ToInt32(ps, 28);
            var chunks = new List<(int off, int size)>();
            int shexIdx = -1;
            for (int i = 0; i < chunkCount; i++)
            {
                int off = BitConverter.ToInt32(ps, 32 + i * 4);
                chunks.Add((off, BitConverter.ToInt32(ps, off + 4)));
                if (ps[off] == 'S' && ps[off + 1] == 'H' && ps[off + 2] == 'E' && ps[off + 3] == 'X') shexIdx = i;
            }
            if (shexIdx < 0) return null;
            (int chunkStart, int shexSize) = chunks[shexIdx];

            uint D(int tokOff, int i) => BitConverter.ToUInt32(ps, chunkStart + 8 + tokOff * 4 + i * 4);

            // Walk the SM5 token stream (data dwords 0..1 are the version + length header),
            // collecting every mangle-decode instruction plus the replacement operand each needs.
            var edits = new List<(int at, int len, uint cbTok)>();
            int lastDcl59 = -1, uvRegister = -1, uvPair = -1;
            bool sawMangle = false;
            int p = 2, endTok = shexSize / 4;
            while (p < endTok)
            {
                uint tok = D(p, 0);
                int opcode = (int)(tok & 0x7ff);
                int len = (int)((tok >> 24) & 0x7f);
                if (opcode == 0x35) len = (int)D(p, 1);   //customdata: length in dword[1]
                if (len <= 0) break;
                if (opcode == 0x59)
                {
                    lastDcl59 = p + len;
                    if (D(p, 2) == 0x0000000a) return null;   //cb10 already declared
                }
                for (int i = 0; i < len; i++)
                    if (D(p, i) == 0x437fff00) { sawMangle = true; break; }

                bool candidate = (opcode == 0x38 && len == 10) || (opcode == 0x32 && len == 13);
                if (candidate && D(p, 5) == 0x00004002)
                {
                    // Immediate lanes: exactly two adjacent 0x437fff00, the rest zero.
                    int active = -1;
                    bool clean = true;
                    for (int l = 0; l < 4; l++)
                    {
                        uint v = D(p, 6 + l);
                        if (v == 0x437fff00) { if (active < 0) active = l; }
                        else if (v != 0) clean = false;
                    }
                    if (clean && active >= 0 && active <= 2 &&
                        D(p, 6 + active + 1) == 0x437fff00 &&
                        (active + 2 > 3 || D(p, 6 + active + 2) != 0x437fff00))
                    {
                        // Source must be a plain 2-dword INPUT operand in swizzle mode.
                        uint srcTok = D(p, 3);
                        if ((srcTok & 0x000ff00c) == 0x00001004 &&    //operand type 1 (input), swizzle mode
                            (srcTok & 0x80000000) == 0)               //no extended modifier
                        {
                            int swz = (int)((srcTok >> 4) & 0xff);
                            int cA = (swz >> (2 * active)) & 3;
                            int cB = (swz >> (2 * (active + 1))) & 3;
                            if (cA != cB)
                            {
                                int cU = Math.Min(cA, cB);
                                int reg = (int)D(p, 4);
                                int pair = (cU << 2) | Math.Max(cA, cB);
                                if (uvRegister < 0) { uvRegister = reg; uvPair = pair; }
                                if (reg == uvRegister && pair == uvPair)
                                {
                                    // cb10[11] with x in u's lane, y in v's, x elsewhere.
                                    int newSwz = 0;
                                    newSwz |= (cA == cU ? 0 : 1) << (2 * active);
                                    newSwz |= (cB == cU ? 0 : 1) << (2 * (active + 1));
                                    edits.Add((p, len, 0x00208006u | ((uint)newSwz << 4)));
                                }
                                else
                                    return null;   //two sites disagree on the UV source - unknown shape
                            }
                        }
                    }
                }
                p += len;
            }
            // The site is one mul + one mad; anything else is a shape the golden tests never
            // covered. A shader with the mangle constant but no clean match must NOT be
            // half-converted.
            if (lastDcl59 < 0 || edits.Count != 2 || !sawMangle) return null;
            if (!((edits[0].len == 10 && edits[1].len == 13) || (edits[0].len == 13 && edits[1].len == 10))) return null;

            // Rebuild the token stream: insert the cb10 declaration after the last cbuffer dcl,
            // and grow each matched instruction's source operand from 2 dwords to 3 (+6 total).
            int oldTokens = shexSize / 4;
            var outToks = new List<uint>(oldTokens + 6);
            var editAt = edits.ToDictionary(e => e.at, e => e);
            for (int i = 0; i < oldTokens; i++)
            {
                if (i == lastDcl59)
                {
                    outToks.Add(0x04000059); outToks.Add(0x00208e46); outToks.Add(0x0000000a); outToks.Add(0x0000000c);
                }
                if (editAt.TryGetValue(i, out (int at, int len, uint cbTok) e))
                {
                    uint opTok = D(e.at, 0);
                    outToks.Add((opTok & 0x80ffffffu) | ((uint)(e.len + 1) << 24));
                    outToks.Add(D(e.at, 1)); outToks.Add(D(e.at, 2));
                    outToks.Add(e.cbTok); outToks.Add(0x0000000a); outToks.Add(0x0000000b);
                    for (int k = 5; k < e.len; k++) outToks.Add(D(e.at, k));
                    i += e.len - 1; continue;
                }
                outToks.Add(D(i, 0));
            }
            outToks[1] = (uint)outToks.Count;

            var outData = new List<byte>(ps.Length + 24);
            int headerEnd = 32 + chunkCount * 4;
            outData.AddRange(ps.Take(headerEnd));
            var newOffsets = new int[chunkCount];
            foreach (int i in Enumerable.Range(0, chunkCount).OrderBy(i => chunks[i].off))
            {
                (int off, int size) = chunks[i];
                newOffsets[i] = outData.Count;
                if (i == shexIdx)
                {
                    outData.AddRange(System.Text.Encoding.ASCII.GetBytes("SHEX"));
                    outData.AddRange(BitConverter.GetBytes(outToks.Count * 4));
                    foreach (uint t in outToks) outData.AddRange(BitConverter.GetBytes(t));
                }
                else
                    outData.AddRange(ps.Skip(off).Take(8 + size));
            }
            byte[] result = outData.ToArray();
            for (int i = 0; i < chunkCount; i++)
                BitConverter.GetBytes(newOffsets[i]).CopyTo(result, 32 + i * 4);
            BitConverter.GetBytes(result.Length).CopyTo(result, 24);
            Checksum(result).CopyTo(result, 4);
            return result;
        }
    }
}
