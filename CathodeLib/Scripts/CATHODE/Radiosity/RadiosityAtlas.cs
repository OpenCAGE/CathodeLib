#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
using System;
using System.Collections.Generic;

namespace CathodeLib.Radiosity
{
    /// <summary>
    /// Skyline packer for one slice's 128x128 lightmap atlas.
    /// </summary>
    /// <remarks>
    /// Each radiosity instance owns a disjoint rect here, and a mover's MODEL_PARAMS carries that rect so the shader can
    /// map the mesh's UV1 into it. The packer only has to be disjoint and reasonably dense, not optimal.
    /// </remarks>
    internal sealed class RadiosityAtlas
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

        /// <summary>Whether <paramref name="width"/> fits from node <paramref name="index"/>; <paramref name="y"/> is the
        /// height of the tallest shelf it spans.</summary>
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
        /// Rect size for an instance: its surface area at
        /// <see cref="RadiosityTuning.MetresSquaredPerTexel"/>, divided by how much of the
        /// unit UV square its triangles use, in the shape of that use
        /// (<see cref="RadiosityGeometry.Instance.UvAspect"/>).
        /// </summary>
        /// <remarks>
        /// The rect covers the whole 0..1 UV square, so the area is divided by the coverage: a chart using a third of the
        /// square would otherwise get a third of the probes its surface area calls for.
        /// </remarks>
        public static void RectSizeForBounds(float surfaceArea, float uvCoverage, out int width, out int height, float uvAspect)
        {
            float coverage = uvCoverage > 0.0f ? Math.Min(1.0f, uvCoverage) : 1.0f;
            surfaceArea /= coverage;
            float texels = Math.Max(1.0f, surfaceArea / RadiosityTuning.MetresSquaredPerTexel);

            // The rect maps the unit UV square and nothing else, so its shape follows how the UVs
            // are laid out in that square. It is described width-over-height, so an aspect under one
            // is a tall rect rather than an invalid one. uvAspect is occupied columns over rows of
            // the 16x16 UV grid, so the aspect lands in 1..16, inside MaxInstanceRect.
            float aspect = uvAspect;
            bool tall = aspect < 1.0f;
            if (tall) aspect = aspect > 1e-4f ? 1.0f / aspect : 1.0f;

            // w * h == texels with w / h == aspect.
            int w = (int)Math.Ceiling(Math.Sqrt(texels * aspect));
            int h = (int)Math.Ceiling(texels / Math.Max(1, w));
            if (tall) { int swap = w; w = h; h = swap; }

            width = Math.Max(RadiosityTuning.MinInstanceRect, Math.Min(RadiosityTuning.MaxInstanceRect, w));
            height = Math.Max(RadiosityTuning.MinInstanceRect, Math.Min(RadiosityTuning.MaxInstanceRect, h));
        }
    }
}
#endif
