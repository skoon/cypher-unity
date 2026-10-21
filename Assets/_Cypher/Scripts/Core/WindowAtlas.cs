using System;

namespace Cypher.Core
{
    /// <summary>
    /// Procedural window atlas, ported from the "winatlas" canvas in City._buildMaterials.
    /// Panes x Panes cells; each row is a storey with its own occupancy, palette and
    /// blinds so whole floors read consistently. The Rng is consumed in the prototype's
    /// order, so the same pane layout appears in both engines.
    ///
    /// Output is RGBA32 bytes. RGB is the sRGB emission colour; A is a glass mask (255
    /// inside the pane, 0 in the mullion margin) so a shader can show wall material
    /// between panes instead of black.
    ///
    /// Deliberate difference from the prototype's canvas, which insets every pane 10% all round:
    /// its facade pass draws narrower panels than the atlas cell, with concrete around them. The
    /// massing walls here are single flat quads, so the glass is inset to the facade's proportions
    /// (68% x 52% of a 2.35 x 3.55 m cell) to leave the same share of wall for concrete.
    /// </summary>
    public static class WindowAtlas
    {
        public const int Panes = CityLayout.WindowAtlas;
        public const int DefaultSize = 1024;
        public const uint DefaultSeed = 4242;

        /// <summary>Glass inset from each side of a pane cell, as a fraction of the cell.</summary>
        public const double GlassInsetX = 0.16;
        public const double GlassInsetY = 0.24;

        static readonly byte[][] Warm =
        {
            new byte[] { 0xff, 0xca, 0x7a }, new byte[] { 0xff, 0xb4, 0x6a },
            new byte[] { 0xff, 0xf0, 0xc8 }, new byte[] { 0xff, 0xdc, 0xa0 },
        };

        static readonly byte[][] Cold =
        {
            new byte[] { 0xbf, 0xe4, 0xff }, new byte[] { 0x8f, 0xf0, 0xff },
            new byte[] { 0xa8, 0xc8, 0xff }, new byte[] { 0xdf, 0xf6, 0xff },
        };

        static readonly byte[][] Odd =
        {
            new byte[] { 0xff, 0x7a, 0xc0 }, new byte[] { 0x7a, 0xff, 0xc0 },
            new byte[] { 0xff, 0x5f, 0x5f }, new byte[] { 0xc5, 0x8b, 0xff },
        };

        static readonly byte[] Reveal = { 0x04, 0x05, 0x0a };   // mullion margin
        static readonly byte[] Unlit = { 0x0a, 0x0d, 0x16 };    // dark pane
        static readonly byte[] Black = { 0, 0, 0 };

        public static byte[] Generate(int size = DefaultSize, uint seed = DefaultSeed)
        {
            if (size <= 0 || size % Panes != 0)
            {
                throw new ArgumentException($"size must be a positive multiple of {Panes}", nameof(size));
            }

            var px = new byte[size * size * 4];
            var rng = new Rng(seed);
            int step = size / Panes;

            Fill(px, size, 0, 0, size, size, Reveal, 1.0);
            for (int row = 0; row < Panes; row++)
            {
                // Each row is a "floor": pick an occupancy + palette so whole storeys read consistently.
                double occ = 0.16 + rng.Next() * 0.72;
                double mood = rng.Next();
                byte[][] pal = mood < 0.52 ? Warm : mood < 0.88 ? Cold : Odd;
                bool blinds = rng.Next() < 0.3;

                for (int col = 0; col < Panes; col++)
                {
                    int cx = col * step, cy = row * step;
                    Fill(px, size, cx, cy, cx + step, cy + step, Reveal, 1.0);

                    double ix = cx + step * GlassInsetX, iy = cy + step * GlassInsetY;
                    double iw = step * (1 - 2 * GlassInsetX), ih = step * (1 - 2 * GlassInsetY);
                    int x0 = (int)Math.Round(ix), y0 = (int)Math.Round(iy);
                    int x1 = (int)Math.Round(ix + iw), y1 = (int)Math.Round(iy + ih);
                    MarkGlass(px, size, x0, y0, x1, y1);

                    if (rng.Next() > occ)
                    {
                        Fill(px, size, x0, y0, x1, y1, Unlit, 1.0);
                        continue;
                    }

                    double alpha = 0.32 + rng.Next() * 0.68;
                    Fill(px, size, x0, y0, x1, y1, Pick(pal, rng), alpha);
                    if (blinds)
                    {
                        int n = 3 + (int)(rng.Next() * 3);
                        for (int b = 0; b < n; b++)
                        {
                            int by0 = (int)Math.Round(iy + ih * (b + 0.2) / n);
                            int by1 = (int)Math.Round(iy + ih * (b + 0.2) / n + ih / (n * 3.4));
                            Fill(px, size, x0, by0, x1, by1, Black, 0.55);
                        }
                    }
                    // Occasional silhouette in the window, and blown-out panes for bloom.
                    if (rng.Next() > 0.9)
                    {
                        double sx = ix + iw * (0.2 + rng.Next() * 0.4);
                        Fill(px, size, (int)Math.Round(sx), (int)Math.Round(iy + ih * 0.35),
                            (int)Math.Round(sx + iw * 0.16), (int)Math.Round(iy + ih), Black, 0.8);
                    }
                    if (rng.Next() > 0.93)
                    {
                        Fill(px, size, x0, y0, x1, y1, Pick(pal, rng), 1.0);
                    }
                }
            }
            return px;
        }

        static byte[] Pick(byte[][] pal, Rng rng) => pal[(int)(rng.Next() * pal.Length)];

        /// <summary>Alpha-blend a colour over the RGB channels, as a 2D canvas would. Rect is half-open and clipped.</summary>
        static void Fill(byte[] px, int size, int x0, int y0, int x1, int y1, byte[] rgb, double alpha)
        {
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
            x1 = Math.Min(size, x1); y1 = Math.Min(size, y1);
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    int i = (y * size + x) * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        px[i + c] = (byte)Math.Round(px[i + c] * (1 - alpha) + rgb[c] * alpha);
                    }
                }
            }
        }

        static void MarkGlass(byte[] px, int size, int x0, int y0, int x1, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    px[(y * size + x) * 4 + 3] = 255;
                }
            }
        }
    }
}
