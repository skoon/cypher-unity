using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>Which half of the decal atlas a decal's tile lives in.</summary>
    public enum DecalKind
    {
        /// <summary>Spray tags and torn flyposters, on walls.</summary>
        Graffiti,

        /// <summary>Oil, grit, litter and cracks, on the ground.</summary>
        Ground,
    }

    /// <summary>
    /// The prop textures, drawn on a <see cref="Canvas2D"/> as the prototype drew them on a browser canvas, with the same seeds and the same
    /// order of random draws: the grime map the props share, the 4x4 atlases of graffiti and of ground decals, and the steam puff.
    /// Everything is RGBA bytes with the first row at the <i>top</i> of the picture unless a method says it is flipped for Unity.
    /// </summary>
    public static class PropTextures
    {
        const double Tau = 2 * Math.PI;

        public const int GrimeSize = 256;
        public const int AtlasSize = 1024;
        public const int PuffSize = 256;

        /// <summary>The graffiti atlas above the ground atlas, in one texture so every decal shares one material.</summary>
        public const int DecalAtlasWidth = AtlasSize;
        public const int DecalAtlasHeight = AtlasSize * 2;

        public const int TilesPerAxis = 4;
        public const int TileCount = TilesPerAxis * TilesPerAxis;

        /// <summary>How much of a tile's width a decal's UVs cover; the rest is a margin so filtering never reaches the next tile.</summary>
        public const double TileUvSize = 0.24;
        public const double TileUvInset = 0.005;

        static readonly Color4[] Palette =
        {
            Color4.Hex(0xff2f8e), Color4.Hex(0x2af0ff), Color4.Hex(0x39ff9e), Color4.Hex(0xffb020),
            Color4.Hex(0xc14bff), Color4.Hex(0xff4b3a), Color4.Hex(0xe8f0ff), Color4.Hex(0x7cff4b),
        };

        // ---------------------------------------------------------------- grime

        /// <summary>
        /// The mottled, streaked grey that multiplies every prop's colour: broad blotches, long vertical streaks and a few faint
        /// drips. Opaque. <paramref name="size"/> is 256 by default: the prototype's 512 map at half the cost, with the same features.
        /// </summary>
        public static byte[] Grime(int size = GrimeSize)
        {
            if (size < 8) throw new ArgumentOutOfRangeException(nameof(size), size, "the grime map needs at least 8 pixels a side");
            double k = 512.0 / size;   // keep the feature size the prototype's, whatever the resolution
            var bytes = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double n = Noise.Fbm2(x * k / 22, y * k / 22, 4) * 0.5 + 0.5;
                    double streak = Noise.Fbm2(x * k / 5, y * k / 90, 3) * 0.5 + 0.5;
                    double v = 208 + n * 44 - streak * 46;
                    int i = (y * size + x) * 4;
                    bytes[i] = Clamp8(v); bytes[i + 1] = Clamp8(v); bytes[i + 2] = Clamp8(v * 1.02); bytes[i + 3] = 255;
                }
            }

            // faint dark drips, drawn over the noise as 40 slightly slanted lines
            var canvas = new Canvas2D(size, size);
            var lines = new List<(double x, double y)>[40];
            for (int i = 0; i < 40; i++)
            {
                double x = (i * 97) % 512 / k;
                lines[i] = new List<(double, double)> { (x, 0), (x + 14 / k, size) };
                canvas.StrokePolyline(lines[i], 1.0 / k, new Color4(0, 0, 0), 0.16);
            }
            byte[] drips = canvas.ToBytes();
            for (int i = 0; i < size * size; i++)
            {
                double a = drips[i * 4 + 3] / 255.0;
                if (a <= 0) continue;
                bytes[i * 4] = Clamp8(bytes[i * 4] * (1 - a));
                bytes[i * 4 + 1] = Clamp8(bytes[i * 4 + 1] * (1 - a));
                bytes[i * 4 + 2] = Clamp8(bytes[i * 4 + 2] * (1 - a));
            }
            return bytes;
        }

        // ---------------------------------------------------------------- graffiti

        /// <summary>4x4 tiles: eleven spray tags (fat strokes, a dark outline pass, drips, overspray) then five torn flyposters. Alpha-cut.</summary>
        public static byte[] Graffiti(int size = AtlasSize) => GraffitiCanvas(size).ToBytes();

        static Canvas2D GraffitiCanvas(int size)
        {
            var c = new Canvas2D(size, size);
            var rng = new Rng(77213);
            double t = size / (double)TilesPerAxis;

            for (int ty = 0; ty < TilesPerAxis; ty++)
            {
                for (int tx = 0; tx < TilesPerAxis; tx++)
                {
                    double ox = tx * t, oy = ty * t;
                    int idx = ty * TilesPerAxis + tx;
                    c.SetClip(ox + 12, oy + 12, t - 24, t - 24);

                    if (idx < 11) SprayTag(c, rng, ox, oy, t);
                    else Flyposter(c, rng, ox, oy, t);
                    c.ClearClip();
                }
            }
            return c;
        }

        static void SprayTag(Canvas2D c, Rng rng, double ox, double oy, double t)
        {
            Color4 col = Palette[(int)(rng.Next() * Palette.Length)];
            Color4 col2 = Palette[(int)(rng.Next() * Palette.Length)];
            int strokes = 3 + (int)(rng.Next() * 4);
            var darkOutline = Color4.Hex(0x0a0a10);

            for (int pass = 0; pass < 2; pass++)
            {
                double width = pass == 0 ? 16 + rng.Next() * 12 : 3.5;
                for (int k = 0; k < strokes; k++)
                {
                    double y0 = oy + t * (0.3 + rng.Next() * 0.42);
                    double x0 = ox + t * (0.12 + rng.Next() * 0.12);
                    var path = new List<(double x, double y)> { (x0, y0) };
                    int segs = 2 + (int)(rng.Next() * 3);
                    for (int s = 0; s < segs; s++)
                    {
                        double x1 = x0 + t * (0.14 + rng.Next() * 0.22);
                        var c1 = (x0 + t * 0.08, y0 - t * (0.1 + rng.Next() * 0.3));
                        var c2 = (x1 - t * 0.08, y0 + t * (0.1 + rng.Next() * 0.3));
                        var end = (x1, y0 + t * (rng.Next() - 0.5) * 0.24);
                        var start = path[path.Count - 1];
                        var pts = new List<(double x, double y)>(Canvas2D.Bezier(start, c1, c2, end));
                        pts.RemoveAt(0);
                        path.AddRange(pts);
                        x0 = x1;
                    }
                    c.StrokePolyline(path, width, pass == 0 ? col : darkOutline);
                }

                if (pass == 0 && rng.Next() > 0.4)
                {
                    var line = new List<(double x, double y)>
                    {
                        (ox + t * 0.15, oy + t * (0.25 + rng.Next() * 0.5)),
                        (ox + t * 0.85, oy + t * (0.25 + rng.Next() * 0.5)),
                    };
                    c.StrokePolyline(line, 5, col2);
                }
            }

            // drips
            for (int k = 0; k < 5; k++)
            {
                double dx = ox + t * (0.18 + rng.Next() * 0.64);
                double dy = oy + t * (0.5 + rng.Next() * 0.14);
                c.StrokePolyline(new List<(double, double)> { (dx, dy), (dx, dy + t * (0.06 + rng.Next() * 0.2)) }, 3, col);
            }

            // overspray
            double r = t * 0.36;
            double cx = ox + t * 0.5, cy = oy + t * 0.5;
            for (int i = 0; i < 26; i++)
            {
                double ang = rng.Next() * Tau;
                double rr = Math.Pow(rng.Next(), 0.6) * r;
                c.FillCircle(cx + Math.Cos(ang) * rr, cy + Math.Sin(ang) * rr, 0.5 + rng.Next() * 1.6, col, 0.28);
            }
        }

        static void Flyposter(Canvas2D c, Rng rng, double ox, double oy, double t)
        {
            double w = t * (0.52 + rng.Next() * 0.26);
            double h = t * (0.6 + rng.Next() * 0.24);
            double px = ox + (t - w) / 2, py = oy + (t - h) / 2;
            double cx = px + w / 2, cy = py + h / 2;
            double angle = (rng.Next() - 0.5) * 0.16;
            double cs = Math.Cos(angle), sn = Math.Sin(angle);

            // a rectangle given in the poster's own turned frame (x right, y down from the poster's centre)
            void Local(double lx, double ly, double lw, double lh, Color4 colour)
            {
                double mx = lx + lw / 2, my = ly + lh / 2;
                c.FillRotatedRect(cx + mx * cs - my * sn, cy + mx * sn + my * cs, lw, lh, angle, colour);
            }

            Local(-w / 2, -h / 2, w, h, Color4.Hex(0xc9ccd4));
            Local(-w / 2, -h / 2, w, h * 0.26, Palette[(int)(rng.Next() * Palette.Length)]);
            var ink = Color4.Hex(0x15161c);
            for (int k = 0; k < 6; k++)
            {
                double lw = w * (0.3 + rng.Next() * 0.55);
                Local(-w / 2 + w * 0.08, -h * 0.14 + k * h * 0.11, lw, h * 0.05, ink);
            }
            Local(-w / 2 + w * 0.08, h * 0.3, w * 0.4, h * 0.1, Palette[(int)(rng.Next() * Palette.Length)]);

            // tear the edges
            for (int k = 0; k < 14; k++)
            {
                double ex = -w / 2 + rng.Next() * w;
                double ey = rng.Next() > 0.5 ? -h / 2 : h / 2;
                double radius = 4 + rng.Next() * 12;
                c.EraseCircle(cx + ex * cs - ey * sn, cy + ex * sn + ey * cs, radius);
            }
        }

        // ---------------------------------------------------------------- ground decals

        /// <summary>4x4 tiles: 0-3 oil slicks, 4-7 wet grit, 8-11 paper litter, 12-15 cracks and rubble. Alpha-blended.</summary>
        public static byte[] Ground(int size = AtlasSize) => GroundCanvas(size).ToBytes();

        static Canvas2D GroundCanvas(int size)
        {
            var c = new Canvas2D(size, size);
            var rng = new Rng(31771);
            double t = size / (double)TilesPerAxis;

            for (int ty = 0; ty < TilesPerAxis; ty++)
            {
                for (int tx = 0; tx < TilesPerAxis; tx++)
                {
                    double ox = tx * t, oy = ty * t;
                    int idx = ty * TilesPerAxis + tx;
                    c.SetClip(ox + 6, oy + 6, t - 12, t - 12);

                    if (idx < 4) OilSlick(c, rng, ox, oy, t);
                    else if (idx < 8) WetGrit(c, rng, ox, oy, t);
                    else if (idx < 12) PaperLitter(c, rng, ox, oy, t);
                    else CracksAndRubble(c, rng, ox, oy, t);
                    c.ClearClip();
                }
            }
            return c;
        }

        static void OilSlick(Canvas2D c, Rng rng, double ox, double oy, double t)
        {
            var stops = new[]
            {
                new GradientStop(0, Color4.Bytes(4, 4, 8, 0.85)),
                new GradientStop(0.6, Color4.Bytes(8, 8, 14, 0.55)),
                new GradientStop(1, Color4.Bytes(10, 10, 18, 0)),
            };
            for (int k = 0; k < 5; k++)
            {
                double cx = ox + t * (0.3 + rng.Next() * 0.4);
                double cy = oy + t * (0.3 + rng.Next() * 0.4);
                double r = t * (0.14 + rng.Next() * 0.24);
                c.FillRadialGradient(cx, cy, r, stops);
            }
            // a faint iridescent rim
            for (int k = 0; k < 5; k++)
            {
                Color4 colour = k % 2 == 1 ? Color4.Hex(0x5a2b7a) : Color4.Hex(0x1d5e6b);
                double cx = ox + t * (0.3 + rng.Next() * 0.4);
                double cy = oy + t * (0.3 + rng.Next() * 0.4);
                double r = t * (0.1 + rng.Next() * 0.2);
                double start = rng.Next() * Tau;
                double end = rng.Next() * Tau + 2;
                c.StrokeArc(cx, cy, r, start, end, 3, colour, 0.3);
            }
        }

        static void WetGrit(Canvas2D c, Rng rng, double ox, double oy, double t)
        {
            for (int k = 0; k < 420; k++)
            {
                double a = 0.12 + rng.Next() * 0.5;
                Color4 colour = rng.Next() > 0.82 ? Color4.Bytes(150, 158, 170, 1) : Color4.Bytes(12, 13, 18, 1);
                double x = ox + rng.Next() * t, y = oy + rng.Next() * t;
                double w = 1 + rng.Next() * 3, h = 1 + rng.Next() * 3;
                c.FillRect(x, y, w, h, colour, a);
            }
        }

        static void PaperLitter(Canvas2D c, Rng rng, double ox, double oy, double t)
        {
            for (int k = 0; k < 9; k++)
            {
                double w = t * (0.07 + rng.Next() * 0.12);
                double h = t * (0.05 + rng.Next() * 0.1);
                double cx = ox + t * (0.15 + rng.Next() * 0.7), cy = oy + t * (0.15 + rng.Next() * 0.7);
                double angle = rng.Next() * Tau;
                double v = 150 + rng.Next() * 80;
                double alpha = 0.55 + rng.Next() * 0.4;
                c.FillRotatedRect(cx, cy, w, h, angle, Color4.Bytes(v, v * 0.98, v * 0.9, 1), alpha);

                // a darker strip along one end, in the sheet's own turned frame
                double mx = -w / 2 + w * 0.175, my = -h / 2 + h * 0.11;
                double cs = Math.Cos(angle), sn = Math.Sin(angle);
                c.FillRotatedRect(cx + mx * cs - my * sn, cy + mx * sn + my * cs, w * 0.35, h * 0.22, angle, Color4.Bytes(20, 20, 26, 1), 0.5);
            }
            for (int k = 0; k < 3; k++)
            {
                var colour = Color4.Bytes(180 + rng.Next() * 60, 60 + rng.Next() * 60, 90 + rng.Next() * 60, 1);
                double cx = ox + t * (0.2 + rng.Next() * 0.6), cy = oy + t * (0.2 + rng.Next() * 0.6);
                double angle = rng.Next() * Tau;
                c.FillEllipse(cx, cy, t * 0.05, t * 0.03, angle, colour, 0.6);
            }
        }

        static void CracksAndRubble(Canvas2D c, Rng rng, double ox, double oy, double t)
        {
            var crack = Color4.Bytes(6, 6, 10, 1);
            for (int k = 0; k < 4; k++)
            {
                double x = ox + t * (0.1 + rng.Next() * 0.8);
                double y = oy + t * 0.08;
                double width = 1 + rng.Next() * 3;
                var path = new List<(double x, double y)> { (x, y) };
                for (int j = 0; j < 8; j++)
                {
                    x += (rng.Next() - 0.5) * t * 0.2;
                    y += t * 0.11;
                    path.Add((x, y));
                }
                c.StrokePolyline(path, width, crack, 0.7);
            }
            for (int k = 0; k < 120; k++)
            {
                double a = 0.15 + rng.Next() * 0.45;
                var colour = Color4.Bytes(70 + rng.Next() * 60, 72 + rng.Next() * 60, 80 + rng.Next() * 60, 1);
                double x = ox + rng.Next() * t, y = oy + rng.Next() * t;
                double w = 1 + rng.Next() * 4, h = 1 + rng.Next() * 4;
                c.FillRect(x, y, w, h, colour, a);
            }
        }

        // ---------------------------------------------------------------- the two atlases as one, and steam

        /// <summary>
        /// Graffiti (top half) and ground decals (bottom half) as one 1024x2048 texture, flipped so that row 0 is the <i>bottom</i> of the
        /// picture, the way Unity reads a texture: v = 1 is the top. <see cref="DecalUv"/> finds a tile in it.
        /// </summary>
        public static byte[] DecalAtlas()
        {
            byte[] graffiti = Graffiti(), ground = Ground();
            var picture = new byte[DecalAtlasWidth * DecalAtlasHeight * 4];
            Buffer.BlockCopy(graffiti, 0, picture, 0, graffiti.Length);
            Buffer.BlockCopy(ground, 0, picture, graffiti.Length, ground.Length);
            return FlipRows(picture, DecalAtlasWidth, DecalAtlasHeight);
        }

        /// <summary>Where tile <paramref name="tile"/> (0 to 15) of a kind starts in the decal atlas, and how big the area to use is, in UV.</summary>
        public static (double u, double v, double du, double dv) DecalUv(DecalKind kind, int tile)
        {
            if (tile < 0 || tile >= TileCount) throw new ArgumentOutOfRangeException(nameof(tile), tile, "a tile is 0 to 15");
            int tx = tile & 3, ty = tile >> 2;
            double u = tx * 0.25 + TileUvInset;
            double vInHalf = (3 - ty) * 0.25 + TileUvInset;   // row 0 of the picture is the top, so it has the highest v
            double offset = kind == DecalKind.Graffiti ? 0.5 : 0.0;
            return (u, offset + vInHalf * 0.5, TileUvSize, TileUvSize * 0.5);
        }

        /// <summary>The soft white puff of steam: bright in the middle, ragged at the edges. Premultiplied-free RGBA, alpha carries the shape.</summary>
        public static byte[] Puff(int size = PuffSize)
        {
            var c = new Canvas2D(size, size);
            double half = size / 2.0;
            c.FillRadialGradient(half, half, half, new[]
            {
                new GradientStop(0, Color4.Bytes(255, 255, 255, 0.95)),
                new GradientStop(0.35, Color4.Bytes(230, 238, 255, 0.42)),
                new GradientStop(0.72, Color4.Bytes(200, 215, 240, 0.12)),
                new GradientStop(1, Color4.Bytes(190, 205, 235, 0)),
            });
            // break the perfect circle up so it reads as vapour, not a sprite
            var rng = new Rng(5150);
            for (int i = 0; i < 22; i++)
            {
                double a = rng.Next() * Tau;
                double r = (0.2 + rng.Next() * 0.32) * size;
                double rr = (0.06 + rng.Next() * 0.14) * size;
                c.EraseRadial(half + Math.Cos(a) * r, half + Math.Sin(a) * r, rr, 0.5);
            }
            return c.ToBytes();
        }

        /// <summary>The rows of an RGBA picture in the opposite order.</summary>
        public static byte[] FlipRows(byte[] rgba, int width, int height)
        {
            if (rgba == null) throw new ArgumentNullException(nameof(rgba));
            if (rgba.Length != width * height * 4) throw new ArgumentException($"{rgba.Length} bytes is not a {width}x{height} RGBA picture", nameof(rgba));
            var flipped = new byte[rgba.Length];
            int row = width * 4;
            for (int y = 0; y < height; y++) Buffer.BlockCopy(rgba, y * row, flipped, (height - 1 - y) * row, row);
            return flipped;
        }

        static byte Clamp8(double v) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));
    }
}
