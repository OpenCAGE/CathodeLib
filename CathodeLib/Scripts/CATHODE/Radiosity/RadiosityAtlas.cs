#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Skyline packer for one slice's 128x128 lightmap atlas.
    /// </summary>
    /// <remarks>
    /// Each radiosity instance owns a disjoint rect here, and a mover's MODEL_PARAMS carries that
    /// rect so the shader can map the mesh's UV1 into it. Retail bakes pack these tightly - slice 1
    /// of BSP_TORRENS has zero overlapping texels across 174 instances - so the packer only has to
    /// be disjoint and reasonably dense, not optimal.
    /// </remarks>
    public sealed class RadiosityAtlas
    {
        private struct Shelf
        {
            public int X;
            public int Width;
            public int Height;
        }

        private readonly List<Shelf> _skyline = new List<Shelf>();

        public int Size { get; }

        /// <summary>Texels handed out so far.</summary>
        public int UsedTexels { get; private set; }

        public RadiosityAtlas(int size)
        {
            Size = size;
            _skyline.Add(new Shelf { X = 0, Width = size, Height = 0 });
        }

        /// <summary>
        /// Claim a <paramref name="width"/> x <paramref name="height"/> rect. Returns false when
        /// the atlas is full, at which point the caller should start a new slice.
        /// </summary>
        public bool TryAllocate(int width, int height, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (width <= 0 || height <= 0 || width > Size || height > Size)
                return false;

            int bestIndex = -1;
            int bestY = int.MaxValue;
            int bestX = int.MaxValue;

            for (int i = 0; i < _skyline.Count; i++)
            {
                if (!TryFit(i, width, out int candidateY))
                    continue;
                if (candidateY + height > Size)
                    continue;

                // Lowest shelf wins; ties go to the leftmost so rows fill in order.
                if (candidateY < bestY || (candidateY == bestY && _skyline[i].X < bestX))
                {
                    bestIndex = i;
                    bestY = candidateY;
                    bestX = _skyline[i].X;
                }
            }

            if (bestIndex < 0)
                return false;

            x = bestX;
            y = bestY;
            Insert(bestIndex, x, y + height, width);
            UsedTexels += width * height;
            return true;
        }

        /// <summary>Height of the tallest shelf spanned by <paramref name="width"/> from node i.</summary>
        private bool TryFit(int index, int width, out int y)
        {
            int x = _skyline[index].X;
            if (x + width > Size)
            {
                y = 0;
                return false;
            }

            int remaining = width;
            y = 0;
            for (int i = index; i < _skyline.Count && remaining > 0; i++)
            {
                y = Math.Max(y, _skyline[i].Height);
                remaining -= _skyline[i].Width;
            }
            return remaining <= 0;
        }

        private void Insert(int index, int x, int top, int width)
        {
            _skyline.Insert(index, new Shelf { X = x, Width = width, Height = top });

            // Trim the shelves the new node covers.
            for (int i = index + 1; i < _skyline.Count;)
            {
                Shelf shelf = _skyline[i];
                int overlap = _skyline[index].X + _skyline[index].Width - shelf.X;
                if (overlap <= 0)
                    break;

                if (overlap >= shelf.Width)
                {
                    _skyline.RemoveAt(i);
                    continue;
                }

                shelf.X += overlap;
                shelf.Width -= overlap;
                _skyline[i] = shelf;
                break;
            }

            // Merge neighbours at the same height so the skyline does not fragment.
            for (int i = 0; i + 1 < _skyline.Count;)
            {
                if (_skyline[i].Height == _skyline[i + 1].Height)
                {
                    Shelf merged = _skyline[i];
                    merged.Width += _skyline[i + 1].Width;
                    _skyline[i] = merged;
                    _skyline.RemoveAt(i + 1);
                    continue;
                }
                i++;
            }
        }

        /// <summary>
        /// Rect size for an instance, from its world surface area. Retail averages roughly
        /// 0.49 m² per texel, and instances are close to square.
        /// </summary>
        public static void RectSizeForArea(float surfaceArea, RadiosityBakeSettings settings, out int width, out int height)
        {
            float texels = surfaceArea / Math.Max(1e-4f, settings.MetresSquaredPerTexel);
            int edge = (int)Math.Ceiling(Math.Sqrt(Math.Max(1.0f, texels)));
            edge = Math.Max(settings.MinInstanceRect, Math.Min(settings.MaxInstanceRect, edge));
            width = edge;
            height = edge;
        }

        /// <summary>
        /// Rect size honouring the instance's aspect ratio: a tall thin wall panel gets a tall
        /// thin rect, which is what retail does (a 0.5 x 1.6 x 0.5 m panel gets 1x2).
        /// </summary>
        public static void RectSizeForBounds(float surfaceArea, System.Numerics.Vector3 boundsSize, RadiosityBakeSettings settings, out int width, out int height)
        {
            RectSizeForBounds(surfaceArea, boundsSize, 1.0f, settings, out width, out height);
        }

        /// <summary>
        /// As above, but scaled for how much of the unit UV square the instance's triangles use.
        /// </summary>
        /// <remarks>
        /// The rect covers the whole 0..1 square, so an instance whose authored UVs occupy only a
        /// third of it wastes two thirds of its texels and ends up with a third of the probes its
        /// surface area calls for. Sizing from world area alone put only 47.8% of Solace's rects
        /// within 25% of retail's, spread from 0.04x to 78x, and that unevenness is what left 38%
        /// of the cells retail fills empty in ours while others ran to 20x retail's density.
        /// </remarks>
        public static void RectSizeForBounds(float surfaceArea, System.Numerics.Vector3 boundsSize, float uvCoverage,
                                             RadiosityBakeSettings settings, out int width, out int height)
        {
            float coverage = uvCoverage > 0.0f ? Math.Min(1.0f, uvCoverage) : 1.0f;
            surfaceArea /= (float)Math.Pow(coverage, settings.UvCoverageCompensation);
            float texels = Math.Max(1.0f, surfaceArea / Math.Max(1e-4f, settings.MetresSquaredPerTexel));

            // Project onto the two largest world axes so the rect follows the dominant faces.
            float a = boundsSize.X, b = boundsSize.Y, c = boundsSize.Z;
            float largest = Math.Max(a, Math.Max(b, c));
            float smallest = Math.Min(a, Math.Min(b, c));
            float middle = a + b + c - largest - smallest;

            float aspect = middle > 1e-4f ? largest / middle : 1.0f;
            if (aspect < 1.0f) aspect = 1.0f;
            if (aspect > settings.MaxInstanceRect) aspect = settings.MaxInstanceRect;

            // w * h == texels with w / h == aspect.
            int w = (int)Math.Ceiling(Math.Sqrt(texels * aspect));
            int h = (int)Math.Ceiling(texels / Math.Max(1, w));

            width = Math.Max(settings.MinInstanceRect, Math.Min(settings.MaxInstanceRect, w));
            height = Math.Max(settings.MinInstanceRect, Math.Min(settings.MaxInstanceRect, h));
        }
    }
}
#endif
