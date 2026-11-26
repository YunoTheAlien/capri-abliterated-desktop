using System;
using System.Collections.Generic;
using System.Drawing;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        // Each strip's cell shape and where its cells are lit, as shares of a cell, read once from its art.
        private readonly Dictionary<Clip, KeyValuePair<float, RectangleF>> streamAspects = new Dictionary<Clip, KeyValuePair<float, RectangleF>>();
        private int streamAspectsOf = -1, streamAreaSize;
        private Rectangle streamArea;

        // Her capture area at her size: every strip's clear area facing either way, glitch reach and a small margin
        // included, from her canvas's top, where her hops, notes and Zs go. A new size makes it again.
        private Rectangle StreamArea()
        {
            if (streamAspectsOf != Clip.ArtVersion) { streamAspects.Clear(); streamAspectsOf = Clip.ArtVersion; streamAreaSize = 0; }
            if (streamAreaSize == preferences.Size && !streamArea.IsEmpty) return streamArea;
            int s = preferences.Size, reach = UnderReach() + 3 + StreamMargin;
            Rectangle all = Rectangle.Empty;
            foreach (Clip one in Clip.Every())
            {
                KeyValuePair<float, RectangleF> shape;
                if (!streamAspects.TryGetValue(one, out shape)) streamAspects[one] = shape = CellShape(one);
                if (shape.Key <= 0) continue;
                float tall = s * one.Height, wide = tall * shape.Key, top = Height / 2f + one.ShiftY * s - tall / 2f;
                RectangleF lit = shape.Value;
                foreach (int side in new[] { 1, -1 })
                {
                    float left = Width / 2f + side * one.ShiftX * s - wide / 2f;
                    float from = side > 0 ? left + wide * lit.Left : left + wide * (1 - lit.Right), to = side > 0 ? left + wide * lit.Right : left + wide * (1 - lit.Left);
                    Rectangle area = Rectangle.FromLTRB((int)Math.Floor(from) - reach, 0, (int)Math.Ceiling(to) + reach, (int)Math.Ceiling(top + tall * lit.Bottom) + 3 + StreamMargin);
                    all = all.IsEmpty ? area : Rectangle.Union(all, area);
                }
            }
            streamArea = Rectangle.Intersect(all, new Rectangle(Point.Empty, Size));
            if (streamAreaSize != 0 && streamWindow != null && !streamWindow.IsDisposed) streamWindow.Forget();
            streamAreaSize = s;
            return streamArea;
        }

        private const int StreamMargin = 6;

        // A strip's cell width over its height, and the box its cells are lit in, as shares of a cell.
        private static KeyValuePair<float, RectangleF> CellShape(Clip one)
        {
            try
            {
                Bitmap art = one.Strip;
                if (art == null || one.Count < 1) return new KeyValuePair<float, RectangleF>(0, RectangleF.Empty);
                int cellWide = art.Width / one.Count, high = art.Height, left = cellWide, right = -1, top = high, bottom = -1;
                int[] row = new int[art.Width];
                System.Drawing.Imaging.BitmapData pixels = art.LockBits(new Rectangle(0, 0, art.Width, high), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try
                {
                    for (int y = 0; y < high; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(pixels.Scan0 + y * pixels.Stride, row, 0, art.Width);
                        for (int x = 0; x < cellWide * one.Count; x++)
                        {
                            if (((row[x] >> 24) & 0xFF) <= 8) continue;
                            int inCell = x % cellWide;
                            if (inCell < left) left = inCell;
                            if (inCell > right) right = inCell;
                            if (y < top) top = y;
                            if (y > bottom) bottom = y;
                        }
                    }
                }
                finally { art.UnlockBits(pixels); }
                if (right < 0) return new KeyValuePair<float, RectangleF>(0, RectangleF.Empty);
                return new KeyValuePair<float, RectangleF>((float)cellWide / high, RectangleF.FromLTRB((float)left / cellWide, (float)top / high, (float)(right + 1) / cellWide, (float)(bottom + 1) / high));
            }
            catch (Exception caughtOne) { Brain.Caught(caughtOne); return new KeyValuePair<float, RectangleF>(0, RectangleF.Empty); }
        }
    }
}
