using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// The two textures the gunfire needs, drawn in code as the prototype draws them on a canvas (<c>combat/Impacts.js</c> and <c>player/Weapons.js</c>): the
    /// bullet hole (a soot halo, the punched hole, radial cracks and a spall ring) and the muzzle glow (a radial bloom with four star spikes). Straight RGBA
    /// bytes, row 0 at the top. The canvas's inner gradient radius is approximated from the centre.
    /// </summary>
    public static class GunFxArt
    {
        public const int Size = 128;

        public static byte[] BulletHole()
        {
            var c = new Canvas2D(Size, Size);
            double s = Size, h = s / 2;
            c.FillRadialGradient(h, h, h, new[]
            {
                new GradientStop(0, Color4.Bytes(20, 18, 16, 0.95)), new GradientStop(0.32, Color4.Bytes(28, 26, 24, 0.7)),
                new GradientStop(0.62, Color4.Bytes(40, 38, 36, 0.24)), new GradientStop(1, Color4.Bytes(0, 0, 0, 0)),
            });
            c.FillRadialGradient(h, h, s * 0.16, new[]
            {
                new GradientStop(0, Color4.Bytes(0, 0, 0, 1)), new GradientStop(0.7, Color4.Bytes(4, 4, 5, 0.98)), new GradientStop(1, Color4.Bytes(10, 10, 12, 0)),
            });
            var crack = Color4.Bytes(150, 150, 158, 0.5);
            for (int i = 0; i < 11; i++)
            {
                double a = i / 11.0 * Math.PI * 2 + (i % 3) * 0.21;
                double len = s * (0.18 + (i * 37 % 17) / 17.0 * 0.24);
                c.StrokePolyline(new List<(double, double)> { (h + Math.Cos(a) * s * 0.12, h + Math.Sin(a) * s * 0.12), (h + Math.Cos(a + 0.12) * len, h + Math.Sin(a + 0.12) * len) }, 1.4, crack);
            }
            c.StrokeArc(h, h, s * 0.19, 0, Math.PI * 2, 2.2, Color4.Bytes(190, 190, 200, 0.28));
            return c.ToBytes();
        }

        public static byte[] FlashGlow()
        {
            var c = new Canvas2D(Size, Size);
            double s = Size, h = s / 2;
            c.FillRadialGradient(h, h, h, new[]
            {
                new GradientStop(0, Color4.Bytes(255, 255, 255, 1)), new GradientStop(0.14, Color4.Bytes(255, 238, 200, 0.86)),
                new GradientStop(0.36, Color4.Bytes(255, 180, 90, 0.30)), new GradientStop(1, Color4.Bytes(255, 120, 40, 0)),
            });
            // star spikes give the bloom a shape to smear
            for (int i = 0; i < 4; i++)
            {
                double a = i / 4.0 * Math.PI * 2 + Math.PI / 8;
                c.StrokePolyline(new List<(double, double)> { (h, h), (h + Math.Cos(a) * h * 0.96, h + Math.Sin(a) * h * 0.96) }, 3, Color4.Bytes(255, 245, 220, 0.7));
            }
            return c.ToBytes();
        }
    }
}
