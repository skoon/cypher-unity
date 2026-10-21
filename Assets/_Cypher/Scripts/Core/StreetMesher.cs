using System;

namespace Cypher.Core
{
    /// <summary>
    /// Sidewalk decks, kerb faces and road paint, ported from City._buildGround and
    /// City._buildStreetDetail. Dimensions and colours are the prototype's.
    /// </summary>
    public static class StreetMesher
    {
        const double PaintY = 0.014;
        const double Nosing = 0.34;
        const double PavingUvScale = 3.0;
        const double KerbUvScale = 2.4;

        static readonly Rgb Top = new Rgb(0.86f, 0.88f, 0.92f);
        static readonly Rgb Nose = new Rgb(1.18f, 1.18f, 1.22f);
        static readonly Rgb Side = new Rgb(0.58f, 0.60f, 0.66f);

        static readonly Rgb White = new Rgb(0.72f, 0.74f, 0.76f);
        static readonly Rgb Yellow = new Rgb(0.78f, 0.66f, 0.28f);
        static readonly Rgb Dark = new Rgb(0.16f, 0.17f, 0.20f);

        /// <summary>Half-width of a block's sidewalk deck: the buildable block plus the overhang.</summary>
        public const double SidewalkHalfWidth = CityLayout.Block / 2 + CityLayout.SidewalkPad;

        /// <summary>The roadbed plane spans the whole city plus a margin, as in the prototype (2.4x the grid extent).</summary>
        public const double RoadbedSize = CityLayout.Grid * CityLayout.Cell * 2.4;

        /// <summary>World X and Z of the roadbed centre: the middle of the city grid.</summary>
        public const double RoadbedCentre = CityLayout.Base + CityLayout.Grid * CityLayout.Cell / 2;

        /// <summary>Texture repeats across the roadbed in the prototype (asphalt "repeat: 46").</summary>
        const double RoadbedRepeats = 46;

        /// <summary>One huge ground quad at Y=0 centred on the city grid, UV-scaled so the asphalt tiles every ~22.6 m.</summary>
        public static void AddRoadbed(MeshBuilder mb)
        {
            if (mb == null) throw new ArgumentNullException(nameof(mb));
            double half = RoadbedSize / 2;
            mb.Slab(RoadbedCentre - half, RoadbedCentre - half, RoadbedCentre + half, RoadbedCentre + half, 0,
                new Rgb(1f, 1f, 1f), RoadbedSize / RoadbedRepeats);
        }

        /// <summary>Paving deck with a lighter nosing strip, plus the four kerb faces.</summary>
        public static void AddSidewalk(MeshBuilder mb, StreetCell s)
        {
            if (mb == null) throw new ArgumentNullException(nameof(mb));
            if (s == null) throw new ArgumentNullException(nameof(s));

            double hw = SidewalkHalfWidth;
            double x0 = s.Cx - hw, x1 = s.Cx + hw, z0 = s.Cz - hw, z1 = s.Cz + hw;
            double y = CityLayout.SidewalkHeight;
            double n = Nosing;

            mb.Slab(x0 + n, z0 + n, x1 - n, z1 - n, y, Top, PavingUvScale);
            mb.Slab(x0, z0, x1, z0 + n, y, Nose, PavingUvScale);
            mb.Slab(x0, z1 - n, x1, z1, y, Nose, PavingUvScale);
            mb.Slab(x0, z0 + n, x0 + n, z1 - n, y, Nose, PavingUvScale);
            mb.Slab(x1 - n, z0 + n, x1, z1 - n, y, Nose, PavingUvScale);

            void Face(double ax, double az, double bx, double bz)
            {
                WallFrame w = WallFrame.Create(ax, az, bx, bz, s.Cx, s.Cz);
                float len = (float)(w.Length / KerbUvScale);
                float h = (float)(y / KerbUvScale);
                mb.Quad(w.At(0, 0), w.At(w.Length, 0), w.At(w.Length, y), w.At(0, y),
                    new[] { 0f, 0f, len, 0f, len, h, 0f, h }, Side, Nose);
            }
            Face(x0, z0, x1, z0);
            Face(x1, z0, x1, z1);
            Face(x1, z1, x0, z1);
            Face(x0, z1, x0, z0);
        }

        /// <summary>
        /// Gutter shading, lane edges, dashed centre lines, zebra crossings and stop bars
        /// for every road and intersection touching the window.
        /// </summary>
        public static void AddMarkings(MeshBuilder paint, CityWindow window)
        {
            if (paint == null) throw new ArgumentNullException(nameof(paint));
            void Stripe(double x0, double z0, double x1, double z1, Rgb c) => paint.Slab(x0, z0, x1, z1, PaintY, c, 1);

            Rect2 ext = window.RoadExtent;
            const double road = CityLayout.Road;

            // Roads run along the low edge of every cell: local [0, Road). A window of
            // cells gx0..gx1 touches roads gx0..gx1+1 on each axis.
            for (int i = window.Gx0; i <= window.Gx1 + 1; i++)
            {
                double rx = CityLayout.Base + i * CityLayout.Cell;
                double cxm = rx + road / 2;
                Stripe(rx + 2.6, ext.Z0, rx + 3.3, ext.Z1, Dark);
                Stripe(rx + road - 3.3, ext.Z0, rx + road - 2.6, ext.Z1, Dark);
                Stripe(rx + 3.4, ext.Z0, rx + 3.65, ext.Z1, White);
                Stripe(rx + road - 3.65, ext.Z0, rx + road - 3.4, ext.Z1, White);
                for (double z = ext.Z0; z < ext.Z1; z += 8) Stripe(cxm - 0.16, z, cxm + 0.16, z + 4.4, Yellow);
            }
            for (int j = window.Gz0; j <= window.Gz1 + 1; j++)
            {
                double rz = CityLayout.Base + j * CityLayout.Cell;
                double czm = rz + road / 2;
                Stripe(ext.X0, rz + 2.6, ext.X1, rz + 3.3, Dark);
                Stripe(ext.X0, rz + road - 3.3, ext.X1, rz + road - 2.6, Dark);
                Stripe(ext.X0, rz + 3.4, ext.X1, rz + 3.65, White);
                Stripe(ext.X0, rz + road - 3.65, ext.X1, rz + road - 3.4, White);
                for (double x = ext.X0; x < ext.X1; x += 8) Stripe(x, czm - 0.16, x + 4.4, czm + 0.16, Yellow);
            }

            // Zebra crossings + stop bars on every intersection approach.
            for (int i = window.Gx0; i <= window.Gx1 + 1; i++)
            {
                for (int j = window.Gz0; j <= window.Gz1 + 1; j++)
                {
                    double rx = CityLayout.Base + i * CityLayout.Cell;
                    double rz = CityLayout.Base + j * CityLayout.Cell;
                    for (int k = 0; k < 4; k++)
                    {
                        bool along = k < 2;
                        double off = k % 2 == 0 ? -3.4 : road + 3.4;
                        for (double s = 2.6; s < road - 2.6; s += 1.5)
                        {
                            if (along) Stripe(rx + s, rz + off - 1.5, rx + s + 0.72, rz + off + 1.5, White);
                            else Stripe(rx + off - 1.5, rz + s, rx + off + 1.5, rz + s + 0.72, White);
                        }
                    }
                    Stripe(rx + 3.5, rz + road + 5.2, rx + road / 2 - 0.4, rz + road + 5.9, White);
                    Stripe(rx + road / 2 + 0.4, rz - 5.9, rx + road - 3.5, rz - 5.2, White);
                }
            }
        }
    }
}
