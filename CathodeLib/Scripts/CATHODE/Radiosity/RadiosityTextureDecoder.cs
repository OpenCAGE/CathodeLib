#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using CATHODE;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// CPU decode of the texture formats Cathode uses for diffuse maps, into a plain RGB8 image
    /// the bake can sample per texel.
    /// </summary>
    /// <remarks>
    /// <para>Only the formats that actually appear on a diffuse sampler are handled: BC7 is 96% of
    /// them in a retail level, with a handful of DXT1 and uncompressed maps. BC6H, DXN and ASTC
    /// are normal, roughness and console-only maps; a material that somehow slots one as its
    /// diffuse falls back to the material average instead.</para>
    /// <para>Values are left in the texture's own (sRGB-ish) space rather than linearised.
    /// Retail's own albedo table averages 0.42 across BSP_TORRENS, which is what the raw texture
    /// values give; linearising would put it near 0.15.</para>
    /// </remarks>
    public static partial class RadiosityTextureDecoder
    {
        /// <summary>
        /// Decode a texture to RGB8, choosing the largest mip that fits inside <paramref name="maxEdge"/>.
        /// </summary>
        /// <remarks>
        /// Prefers the persistent part: it is always resident, whereas the streamed part is paged
        /// in on demand and is absent for a quarter of a level's diffuse maps. Picking a mip rather
        /// than decoding mip 0 and downsampling gets the texture's own prefiltering for free.
        /// </remarks>
        public static bool TryDecode(Textures.TEX4 texture, int maxEdge, out byte[] rgb, out int width, out int height)
        {
            rgb = null;
            width = height = 0;
            if (texture == null)
                return false;

            Textures.TEX4.Texture part = Pick(texture);
            if (part == null)
                return false;

            if (!SelectMip(texture.Format, part, maxEdge, out int offset, out width, out height))
                return false;

            rgb = new byte[width * height * 3];
            switch (texture.Format)
            {
                case Textures.TextureFormat.DXT1:
                    return DecodeBlocks(part.Content, offset, rgb, width, height, 8, DecodeBc1Punchthrough);
                case Textures.TextureFormat.DXT3:
                case Textures.TextureFormat.DXT5:
                    // Both prefix the colour block with 8 bytes of alpha; the colour block is
                    // always in four-colour mode regardless of endpoint order.
                    return DecodeBlocks(part.Content, offset, rgb, width, height, 16, DecodeBc23Colour);
                case Textures.TextureFormat.BC7:
                    return DecodeBlocks(part.Content, offset, rgb, width, height, 16, DecodeBc7);
                case Textures.TextureFormat.A8R8G8B8:
                case Textures.TextureFormat.X8R8G8B8:
                    return DecodeUncompressed(part.Content, offset, rgb, width, height, 4);
                case Textures.TextureFormat.L8:
                case Textures.TextureFormat.A8:
                    return DecodeUncompressed(part.Content, offset, rgb, width, height, 1);
                default:
                    rgb = null;
                    return false;
            }
        }

        /// <summary>
        /// Prefer the higher-resolution part. Streaming is a runtime concern; an offline bake has
        /// the whole PAK in hand, and the streamed part is a 512px median against the persistent
        /// part's 128px, which is detail the albedo average would otherwise never see.
        /// </summary>
        private static Textures.TEX4.Texture Pick(Textures.TEX4 texture)
        {
            Textures.TEX4.Texture streamed = texture.TextureStreamed;
            Textures.TEX4.Texture persistent = texture.TexturePersistent;
            bool hasStreamed = Usable(streamed);
            bool hasPersistent = Usable(persistent);

            if (hasStreamed && hasPersistent)
                return Math.Max(streamed.Width, streamed.Height) >= Math.Max(persistent.Width, persistent.Height)
                    ? streamed : persistent;
            if (hasStreamed) return streamed;
            return hasPersistent ? persistent : null;
        }

        private static bool Usable(Textures.TEX4.Texture part) =>
            part?.Content != null && part.Content.Length > 0 && part.Width > 0 && part.Height > 0;

        /// <summary>
        /// Walk the mip chain, which is stored largest first, to the first level inside the cap.
        /// </summary>
        private static bool SelectMip(Textures.TextureFormat format, Textures.TEX4.Texture part, int maxEdge,
                                      out int offset, out int width, out int height)
        {
            offset = 0;
            width = part.Width;
            height = part.Height;

            int levels = Math.Max(1, (int)part.MipLevels);
            for (int level = 0; level < levels; level++)
            {
                int bytes = MipBytes(format, width, height);
                if (bytes <= 0)
                    return false;

                bool fits = Math.Max(width, height) <= maxEdge;
                bool present = offset + bytes <= part.Content.Length;
                if (fits)
                    return present;
                if (!present || (width <= 1 && height <= 1))
                {
                    // The chain is shorter than declared, or we ran out before reaching the cap.
                    // Fall back to mip 0, which is always present, even if it is over the cap.
                    offset = 0;
                    width = part.Width;
                    height = part.Height;
                    return MipBytes(format, width, height) <= part.Content.Length;
                }

                offset += bytes;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }

            offset = 0;
            width = part.Width;
            height = part.Height;
            return MipBytes(format, width, height) <= part.Content.Length;
        }

        private static int MipBytes(Textures.TextureFormat format, int width, int height)
        {
            int blocksX = Math.Max(1, (width + 3) / 4);
            int blocksY = Math.Max(1, (height + 3) / 4);
            switch (format)
            {
                case Textures.TextureFormat.DXT1:
                    return blocksX * blocksY * 8;
                case Textures.TextureFormat.DXT3:
                case Textures.TextureFormat.DXT5:
                case Textures.TextureFormat.BC7:
                    return blocksX * blocksY * 16;
                case Textures.TextureFormat.A8R8G8B8:
                case Textures.TextureFormat.X8R8G8B8:
                    return width * height * 4;
                case Textures.TextureFormat.L8:
                case Textures.TextureFormat.A8:
                    return width * height;
                default:
                    return 0;
            }
        }

        /// <summary>Writes 16 RGB triples for one 4x4 block.</summary>
        private delegate void BlockDecoder(byte[] src, int offset, byte[] block);

        private static bool DecodeBlocks(byte[] src, int start, byte[] rgb, int width, int height,
                                         int blockBytes, BlockDecoder decode)
        {
            int blocksX = Math.Max(1, (width + 3) / 4);
            int blocksY = Math.Max(1, (height + 3) / 4);
            var block = new byte[16 * 3];

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    int offset = start + (by * blocksX + bx) * blockBytes;
                    if (offset + blockBytes > src.Length)
                        return false;

                    decode(src, offset, block);

                    for (int ty = 0; ty < 4; ty++)
                    {
                        int y = by * 4 + ty;
                        if (y >= height) break;
                        for (int tx = 0; tx < 4; tx++)
                        {
                            int x = bx * 4 + tx;
                            if (x >= width) break;
                            int s = (ty * 4 + tx) * 3;
                            int d = (y * width + x) * 3;
                            rgb[d] = block[s];
                            rgb[d + 1] = block[s + 1];
                            rgb[d + 2] = block[s + 2];
                        }
                    }
                }
            }
            return true;
        }

        private static bool DecodeUncompressed(byte[] src, int start, byte[] rgb, int width, int height, int bytesPerPixel)
        {
            int pixels = width * height;
            if (start + pixels * bytesPerPixel > src.Length)
                return false;

            for (int i = 0; i < pixels; i++)
            {
                int s = start + i * bytesPerPixel;
                int d = i * 3;
                if (bytesPerPixel == 1)
                {
                    rgb[d] = rgb[d + 1] = rgb[d + 2] = src[s];
                }
                else
                {
                    // Stored BGRA.
                    rgb[d] = src[s + 2];
                    rgb[d + 1] = src[s + 1];
                    rgb[d + 2] = src[s];
                }
            }
            return true;
        }

        #region BC1 / BC2 / BC3

        private static void DecodeBc1Punchthrough(byte[] src, int offset, byte[] block) => DecodeBc1(src, offset, block, true);

        private static void DecodeBc23Colour(byte[] src, int offset, byte[] block) => DecodeBc1(src, offset + 8, block, false);

        /// <summary>
        /// Decode a BC1 colour block. <paramref name="punchthrough"/> enables the c0 &lt;= c1
        /// three-colour mode, which BC2 and BC3 colour blocks never use.
        /// </summary>
        private static void DecodeBc1(byte[] src, int offset, byte[] block, bool punchthrough)
        {
            ushort c0 = (ushort)(src[offset] | (src[offset + 1] << 8));
            ushort c1 = (ushort)(src[offset + 2] | (src[offset + 3] << 8));

            var r = new int[4];
            var g = new int[4];
            var b = new int[4];
            Rgb565(c0, out r[0], out g[0], out b[0]);
            Rgb565(c1, out r[1], out g[1], out b[1]);

            if (!punchthrough || c0 > c1)
            {
                r[2] = (2 * r[0] + r[1]) / 3; g[2] = (2 * g[0] + g[1]) / 3; b[2] = (2 * b[0] + b[1]) / 3;
                r[3] = (r[0] + 2 * r[1]) / 3; g[3] = (g[0] + 2 * g[1]) / 3; b[3] = (b[0] + 2 * b[1]) / 3;
            }
            else
            {
                r[2] = (r[0] + r[1]) / 2; g[2] = (g[0] + g[1]) / 2; b[2] = (b[0] + b[1]) / 2;
                r[3] = g[3] = b[3] = 0;
            }

            uint indices = (uint)(src[offset + 4] | (src[offset + 5] << 8) | (src[offset + 6] << 16) | (src[offset + 7] << 24));
            for (int t = 0; t < 16; t++)
            {
                int i = (int)((indices >> (t * 2)) & 3);
                block[t * 3] = (byte)r[i];
                block[t * 3 + 1] = (byte)g[i];
                block[t * 3 + 2] = (byte)b[i];
            }
        }

        private static void Rgb565(ushort c, out int r, out int g, out int b)
        {
            int r5 = (c >> 11) & 0x1F, g6 = (c >> 5) & 0x3F, b5 = c & 0x1F;
            r = (r5 << 3) | (r5 >> 2);
            g = (g6 << 2) | (g6 >> 4);
            b = (b5 << 3) | (b5 >> 2);
        }

        #endregion

        #region BC7

        // Per mode: subsets, partition/rotation/index-selection bits, colour and alpha endpoint
        // bits, p-bit style, and the two index bit widths. Straight from the BPTC spec.
        private static readonly int[] Bc7Subsets = { 3, 2, 3, 2, 1, 1, 1, 2 };
        private static readonly int[] Bc7PartitionBits = { 4, 6, 6, 6, 0, 0, 0, 6 };
        private static readonly int[] Bc7RotationBits = { 0, 0, 0, 0, 2, 2, 0, 0 };
        private static readonly int[] Bc7IndexSelectionBits = { 0, 0, 0, 0, 1, 0, 0, 0 };
        private static readonly int[] Bc7ColourBits = { 4, 6, 5, 7, 5, 7, 7, 5 };
        private static readonly int[] Bc7AlphaBits = { 0, 0, 0, 0, 6, 8, 7, 5 };
        /// <summary>One p-bit per endpoint.</summary>
        private static readonly bool[] Bc7EndpointPBit = { true, false, false, true, false, false, true, true };
        /// <summary>One p-bit shared by both endpoints of a subset.</summary>
        private static readonly bool[] Bc7SharedPBit = { false, true, false, false, false, false, false, false };
        private static readonly int[] Bc7IndexBits = { 3, 3, 2, 2, 2, 2, 4, 2 };
        private static readonly int[] Bc7IndexBits2 = { 0, 0, 0, 0, 3, 2, 0, 0 };

        private static readonly int[] Bc7Weights2 = { 0, 21, 43, 64 };
        private static readonly int[] Bc7Weights3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
        private static readonly int[] Bc7Weights4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };

        private static void DecodeBc7(byte[] src, int offset, byte[] block)
        {
            var bits = new BitCursor(src, offset);

            int mode = -1;
            for (int i = 0; i < 8; i++)
            {
                if (bits.Read(1) == 1) { mode = i; break; }
            }
            if (mode < 0)
            {
                // Modes 8+ are reserved; the spec says decode such a block to opaque black.
                Array.Clear(block, 0, block.Length);
                return;
            }

            int partition = bits.Read(Bc7PartitionBits[mode]);
            int rotation = bits.Read(Bc7RotationBits[mode]);
            int indexSelection = bits.Read(Bc7IndexSelectionBits[mode]);

            int subsets = Bc7Subsets[mode];
            int endpoints = subsets * 2;
            int colourBits = Bc7ColourBits[mode];
            int alphaBits = Bc7AlphaBits[mode];

            var r = new int[6];
            var g = new int[6];
            var b = new int[6];
            var a = new int[6];

            for (int i = 0; i < endpoints; i++) r[i] = bits.Read(colourBits);
            for (int i = 0; i < endpoints; i++) g[i] = bits.Read(colourBits);
            for (int i = 0; i < endpoints; i++) b[i] = bits.Read(colourBits);
            for (int i = 0; i < endpoints; i++) a[i] = alphaBits > 0 ? bits.Read(alphaBits) : 255;

            var pBits = new int[6];
            bool hasPBit = Bc7EndpointPBit[mode] || Bc7SharedPBit[mode];
            if (Bc7EndpointPBit[mode])
            {
                for (int i = 0; i < endpoints; i++) pBits[i] = bits.Read(1);
            }
            else if (Bc7SharedPBit[mode])
            {
                for (int s = 0; s < subsets; s++)
                {
                    int p = bits.Read(1);
                    pBits[s * 2] = p;
                    pBits[s * 2 + 1] = p;
                }
            }

            for (int i = 0; i < endpoints; i++)
            {
                r[i] = Unquantise(r[i], pBits[i], hasPBit, colourBits);
                g[i] = Unquantise(g[i], pBits[i], hasPBit, colourBits);
                b[i] = Unquantise(b[i], pBits[i], hasPBit, colourBits);
                a[i] = alphaBits > 0 ? Unquantise(a[i], pBits[i], hasPBit, alphaBits) : 255;
            }

            // The first index of each subset drops its high bit, since the encoder is free to
            // order the endpoints so that index never needs it.
            int anchor1 = subsets == 2 ? Bc7Anchor2[partition] : subsets == 3 ? Bc7Anchor3A[partition] : -1;
            int anchor2 = subsets == 3 ? Bc7Anchor3B[partition] : -1;

            int indexBits = Bc7IndexBits[mode];
            int indexBits2 = Bc7IndexBits2[mode];

            var index = new int[16];
            for (int t = 0; t < 16; t++)
                index[t] = bits.Read(t == 0 || t == anchor1 || t == anchor2 ? indexBits - 1 : indexBits);

            var index2 = new int[16];
            if (indexBits2 > 0)
            {
                // Modes with a second index set are single-subset, so texel 0 is the only anchor.
                for (int t = 0; t < 16; t++)
                    index2[t] = bits.Read(t == 0 ? indexBits2 - 1 : indexBits2);
            }

            int[] weights = Weights(indexBits);
            int[] weights2 = indexBits2 > 0 ? Weights(indexBits2) : null;

            for (int t = 0; t < 16; t++)
            {
                int subset = subsets == 1 ? 0
                           : subsets == 2 ? Bc7Partition2[partition * 16 + t]
                                          : Bc7Partition3[partition * 16 + t];
                int e0 = subset * 2, e1 = e0 + 1;

                int colourWeight, alphaWeight;
                if (indexBits2 == 0)
                {
                    colourWeight = alphaWeight = weights[index[t]];
                }
                else if (indexSelection == 0)
                {
                    colourWeight = weights[index[t]];
                    alphaWeight = weights2[index2[t]];
                }
                else
                {
                    colourWeight = weights2[index2[t]];
                    alphaWeight = weights[index[t]];
                }

                int cr = Interpolate(r[e0], r[e1], colourWeight);
                int cg = Interpolate(g[e0], g[e1], colourWeight);
                int cb = Interpolate(b[e0], b[e1], colourWeight);
                int ca = Interpolate(a[e0], a[e1], alphaWeight);

                // A rotation swaps the alpha channel with one colour channel.
                switch (rotation)
                {
                    case 1: { int tmp = cr; cr = ca; ca = tmp; break; }
                    case 2: { int tmp = cg; cg = ca; ca = tmp; break; }
                    case 3: { int tmp = cb; cb = ca; ca = tmp; break; }
                }

                block[t * 3] = (byte)cr;
                block[t * 3 + 1] = (byte)cg;
                block[t * 3 + 2] = (byte)cb;
            }
        }

        private static int[] Weights(int bits) => bits == 2 ? Bc7Weights2 : bits == 3 ? Bc7Weights3 : Bc7Weights4;

        private static int Interpolate(int e0, int e1, int weight) => ((64 - weight) * e0 + weight * e1 + 32) >> 6;

        /// <summary>Widen a quantised endpoint to 8 bits, replicating the high bits into the low.</summary>
        private static int Unquantise(int value, int pBit, bool hasPBit, int bits)
        {
            int total = bits + (hasPBit ? 1 : 0);
            int v = hasPBit ? ((value << 1) | pBit) : value;
            if (total >= 8)
                return Math.Min(255, v);
            return ((v << (8 - total)) | (v >> Math.Max(0, 2 * total - 8))) & 0xFF;
        }

        /// <summary>Little-endian LSB-first bit reader over a block.</summary>
        private struct BitCursor
        {
            private readonly byte[] _data;
            private readonly int _base;
            private int _bit;

            public BitCursor(byte[] data, int offset)
            {
                _data = data;
                _base = offset;
                _bit = 0;
            }

            public int Read(int count)
            {
                int result = 0;
                for (int i = 0; i < count; i++)
                {
                    int byteIndex = _base + (_bit >> 3);
                    if (byteIndex >= _data.Length)
                        return result;
                    result |= ((_data[byteIndex] >> (_bit & 7)) & 1) << i;
                    _bit++;
                }
                return result;
            }
        }

        #endregion
    }
}
#endif
