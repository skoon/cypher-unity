using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// Decides where the neon goes: a port of Neon._placeFromWalls and its helpers for the shapes-only
    /// slice. Per street or alley wall it places shopfront fascia signs, plaques, doorway tubes, a fascia
    /// strip, projecting blade signs, edge tubes, floor bands, a stack of signs and alley-mouth glow, all
    /// in the colours of the wall's district, then the LED hoardings on tall street facades and sixteen
    /// floating holograms. Left out on purpose: the pooled real lights (light spill is done with glow
    /// pools instead).
    /// <para>
    /// Three random streams keep each part from disturbing the others: the main one for the shapes (consumed
    /// in the planner's own order, so a seed always gives the same signage, though not draw-for-draw the
    /// prototype's), one that chooses each sign's lettering, and one for the hoardings and holograms. Changing
    /// the atlas or the boards therefore never moves a tube.
    /// </para>
    /// </summary>
    public static class NeonPlanner
    {
        public const uint DefaultSeed = 77123;

        /// <summary>The lettering and board streams are the main seed mixed with these.</summary>
        const uint SignStreamMix = 0x5163A11D, BoardStreamMix = 0xB0A2D5ED;

        /// <summary>Holograms are hung over buildings taller than this.</summary>
        public const double HologramMinHeight = 40;

        public const int HologramAttempts = 16;

        /// <summary>Street pools float just above the sidewalk deck.</summary>
        public const double PoolY = 0.245;

        /// <param name="atlas">Where the lettering is; the signs are given cells from it. Defaults to the standard atlas.</param>
        public static NeonLayout Plan(CityPlan plan, CityWindow window, uint seed = DefaultSeed, NeonAtlasLayout atlas = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var placer = new Placer(plan, seed, atlas ?? NeonAtlas.Layout());
            foreach (NeonWall w in NeonWalls.Build(plan, window))
            {
                if (NeonWalls.IsUsable(w)) placer.PlaceWall(w);
            }
            placer.PlaceHolograms(window);
            return placer.Layout;
        }

        static double JsRound(double v) => Math.Floor(v + 0.5);

        sealed class Placer
        {
            readonly CityPlan _plan;
            readonly NeonAtlasLayout _atlas;
            readonly Rng _rng;
            readonly Rng _signRng;
            readonly Rng _boardRng;
            NeonWall _w;

            public NeonLayout Layout { get; } = new NeonLayout();

            public Placer(CityPlan plan, uint seed, NeonAtlasLayout atlas)
            {
                _plan = plan;
                _atlas = atlas;
                _rng = new Rng(seed);
                _signRng = new Rng(unchecked(seed ^ SignStreamMix));
                _boardRng = new Rng(unchecked(seed ^ BoardStreamMix));
            }

            double R() => _rng.Next();

            double B() => _boardRng.Next();

            /// <summary>The lettering for a sign: a random cell of the group.</summary>
            string Cell(SignGroup group)
            {
                IReadOnlyList<AtlasCell> cells = _atlas.Group(group);
                return cells[(int)(_signRng.Next() * cells.Count)].Name;
            }

            (double X, double Y, double Z) P(double s, double y, double off) =>
                (_w.Ox + _w.Tx * s + _w.Nx * off, y, _w.Oz + _w.Tz * s + _w.Nz * off);

            public void PlaceWall(NeonWall w)
            {
                _w = w;
                NeonDistrict district = NeonDistricts.At(w.Mx, w.Mz);
                double len = w.Length;
                const double floorH = CityLayout.FloorHeight;
                double baseY = CityChunker.BuildingBaseY;

                // How much room is there in front? Drives blade projection and density.
                double clear = 1.5;
                for (double d = 2; d <= 18; d += 1.5)
                {
                    if (_plan.IsBlocked(w.Mx + w.Nx * d, w.Mz + w.Nz * d)) break;
                    clear = d;
                }

                bool groundWall = w.Ground && w.Y0 < baseY + 1.6;
                bool street = w.Street;

                if (groundWall)
                {
                    double fascia = Math.Min(w.Y0 + floorH - 0.5, w.Y1 - 0.35);

                    double slotW = street ? 5.2 : 7.4;
                    int slots = (int)Math.Max(1, JsRound((len - 1.2) / slotW));
                    for (int i = 0; i < slots; i++)
                    {
                        double s = 0.6 + (len - 1.2) * ((i + 0.5) / slots);
                        Shopfront(district, s, Math.Min(slotW, 4.2), fascia - 0.6, clear, street);
                    }

                    // Fascia strip running the whole shopfront: ties a run of shops together and lights the pavement.
                    if (R() > (street ? 0.15 : 0.5))
                    {
                        double y = fascia + 0.18;
                        double chase = R() > 0.66 ? Math.Max(4, JsRound(len * 0.55)) : 0;
                        StripAt(district, len * 0.5, y, len * 0.94, 0.20, chase, 1);
                        if (street && R() > 0.62)
                        {
                            var p = P(len * 0.5, y - 0.30, 0.14);
                            Quad(p.X, p.Y, p.Z, w.Nx, w.Nz, len * 0.9, 0.28, 0, NeonShape.Bulbs, district.PickHue(_rng), 2.6, 0, null,
                                Math.Max(3, JsRound(len / 1.5)));
                        }
                    }

                    // Projecting blade signs.
                    if (street && clear > 3.2)
                    {
                        int blades = (int)Math.Min(3, Math.Max(1, JsRound(len / 13)));
                        for (int i = 0; i < blades; i++)
                        {
                            if (R() > 0.62) continue;
                            double s = len * ((i + 0.5) / blades) + (R() - 0.5) * 1.6;
                            if (s < 1 || s > len - 1) continue;
                            double proj = Math.Min(clear * 0.4, 1.4 + R() * 1.5);
                            if (proj < 0.9) continue;
                            double bh = Math.Min(w.Y1 - w.Y0 - 8, 4.0 + R() * 5.5);
                            if (bh < 2.4) continue;
                            double y = w.Y0 + 6.0 + R() * 4;
                            if (y + bh * 0.5 > w.Y1 - 1) continue;
                            Blade(s, y, 1.0, bh, proj, district);
                        }
                    }
                }

                // Alley mouth glow.
                if (!street && groundWall && R() > 0.55)
                {
                    Rgb col = district.PickHue(_rng);
                    double s = R() * len;
                    var p = P(s, 2.4 + R() * 1.6, 0.12);
                    double width = 3.0 + R() * 2;
                    int mode = R() > 0.55 ? 1 : 4;
                    Quad(p.X, p.Y, p.Z, w.Nx, w.Nz, width, 0.16, 0, NeonShape.Tube, col, 2.0, mode, null, 0);
                }

                // Vertical edge tubes.
                double spanTop = Math.Min(w.Y1 - 1.2, w.Y0 + 6 + R() * 24);
                for (int end = 0; end < 2; end++)
                {
                    if (R() > (street ? 0.55 : 0.28)) continue;
                    double s = end == 1 ? len - 0.5 : 0.5;
                    double y0 = w.Y0 + (groundWall ? 3.4 : 0.8);
                    double run = spanTop - y0;
                    if (run < 3) continue;
                    var p = P(s, y0 + run * 0.5, 0.13);
                    Rgb col = district.PickHue(_rng);
                    int mode = R() > 0.92 ? 1 : 0;
                    double chase = R() > 0.72 ? Math.Max(5, JsRound(run / 2.2)) : 0;
                    Quad(p.X, p.Y, p.Z, w.Nx, w.Nz, run, 0.22, Math.PI / 2, NeonShape.Tube, col, 2.4, mode, null, chase);
                }

                // Horizontal floor bands.
                double tall = w.Y1 - w.Y0;
                if (street && tall > 14 && len > 6)
                {
                    int bands = (int)Math.Min(4, Math.Floor(tall / 18));
                    for (int i = 0; i < bands; i++)
                    {
                        if (R() > 0.55) continue;
                        double y = w.Y0 + 7 + (tall - 10) * ((i + 0.5) / bands) + R() * 2.5;
                        if (y > w.Y1 - 2) continue;
                        StripAt(district, len * 0.5, y, len * 0.97, 0.16, 0, 1.5);
                    }
                }

                // Stacked sign ladder.
                if (street && len > 7 && tall > 14 && R() > 0.72)
                {
                    double s = 1.6 + R() * (len - 3.2);
                    int n = 4 + (int)(R() * 7);
                    double y = w.Y0 + (groundWall ? 6 : 1.5) + R() * 2.5;
                    for (int i = 0; i < n && y < w.Y1 - 3; i++)
                    {
                        double size = 1.4 + R() * 0.8;
                        bool kanji = R() > 0.4;
                        var p = P(s, y + size * 0.5, 0);
                        double proj = 0.5 + R() * 0.45;
                        PanelSign(p.X, p.Y, p.Z, w.Nx, w.Nz, kanji ? size : size * 2.1, size, proj, district, pool: false, kanji ? SignGroup.Kanji : SignGroup.Word);
                        y += size + 0.3 + R() * 0.5;
                    }
                }

                if (street && tall > 16 && len > 11 && clear > 8) Hoardings(district, len, tall, groundWall);
            }

            /// <summary>LED hoardings on a tall street facade: one, or two on a tower, each a coin toss. Drawn from the board stream.</summary>
            void Hoardings(NeonDistrict district, double len, double tall, bool groundWall)
            {
                int n = tall > 40 ? 2 : 1;
                for (int i = 0; i < n; i++)
                {
                    if (B() > 0.62) continue;
                    double bw = Math.Min(len * 0.8, 9 + B() * 11);
                    double bh = Math.Min(tall * 0.4, bw * (0.46 + B() * 0.4));
                    if (bh < 3.5) continue;
                    double lo = _w.Y0 + (groundWall ? 12 : 2.5) + bh * 0.5;
                    double hi = _w.Y1 - 2.5 - bh * 0.5;
                    if (hi <= lo) continue;
                    double y = lo + (hi - lo) * ((i + 0.3 + B() * 0.4) / n);
                    double s = len * 0.5 + (B() - 0.5) * Math.Max(0, len - bw - 1.5);
                    var p = P(s, y, 0);
                    Hoarding(p.X, p.Y, p.Z, bw, bh, district);
                }
            }

            /// <summary>A screen in a dark frame, ringed by tubes, with a wash of its light on the air in front.</summary>
            void Hoarding(double x, double y, double z, double width, double height, NeonDistrict district)
            {
                double nx = _w.Nx, nz = _w.Nz, tx = -nz, tz = nx;
                const double proj = 0.5;
                Rgb tint = Lean(district.PickHue(_boardRng), 0.35f);   // a screen is broadly neutral; the district only leans it

                Box(x + nx * proj * 0.5, y, z + nz * proj * 0.5, nx, nz, width + 0.5, height + 0.5, proj);
                Layout.Boards.Add(new NeonBoard
                {
                    Gx = _w.Gx, Gz = _w.Gz, X = x + nx * (proj + 0.04), Y = y, Z = z + nz * (proj + 0.04), Nx = nx, Nz = nz,
                    Width = width, Height = height, Panel = (int)(B() * BoardArt.PanelCount), Colour = tint,
                    Seed = B() * 10, Glitch = 0.6 + B() * 0.8, TickerSpeed = 0.05 + B() * 0.09, Gain = 2.2 + B() * 1.6,
                });

                QuadIn(_w.Gx, _w.Gz, x + nx * (proj + 1.2), y, z + nz * (proj + 1.2), nx, nz, Math.Max(width, height) * 1.5, Math.Max(width, height) * 1.5, 0,
                    NeonShape.Radial, tint, 0.30, 0, B() * 10, 0);

                // rim tubes framing the hoarding
                Rgb rim = district.PickHue(_boardRng);
                foreach (int sgn in new[] { -1, 1 })
                {
                    QuadIn(_w.Gx, _w.Gz, x + nx * (proj + 0.1), y + sgn * (height * 0.5 + 0.22), z + nz * (proj + 0.1), nx, nz, width + 0.5, 0.20, 0,
                        NeonShape.Tube, rim, 2.4, 0, B() * 10, 0);
                    QuadIn(_w.Gx, _w.Gz, x + tx * sgn * (width * 0.5 + 0.22) + nx * (proj + 0.1), y, z + tz * sgn * (width * 0.5 + 0.22) + nz * (proj + 0.1), nx, nz,
                        height + 0.5, 0.20, Math.PI / 2, NeonShape.Tube, rim, 2.4, 0, B() * 10, 0);
                }
            }

            /// <summary>
            /// A handful of free-floating projections hanging over the streets beside the tall buildings, tried sixteen times and skipped
            /// where the spot is inside another lot.
            /// </summary>
            public void PlaceHolograms(CityWindow window)
            {
                var tall = new List<Building>();
                foreach (Building b in _plan.Buildings)
                {
                    if (b.Height > HologramMinHeight && window.Contains(b.Lot.Gx, b.Lot.Gz)) tall.Add(b);
                }
                if (tall.Count == 0) return;

                for (int i = 0; i < HologramAttempts; i++)
                {
                    Building b = tall[(int)(B() * tall.Count)];
                    int dir = B() > 0.5 ? 1 : -1;
                    bool axis = B() > 0.5;
                    double nx = axis ? dir : 0, nz = axis ? 0 : dir;
                    double off = (axis ? b.Width : b.Depth) * 0.5 + 5 + B() * 5;
                    double x = b.X + nx * off, z = b.Z + nz * off;
                    if (_plan.IsBlocked(x, z)) continue;   // never hang a projection inside another lot's massing

                    double y = 14 + B() * 24;
                    double w = 7 + B() * 9;
                    double h = w * (0.7 + B() * 0.7);
                    int panel = (int)(B() * BoardArt.PanelCount);
                    Rgb tint = Lean(NeonDistricts.At(x, z).PickHue(_boardRng), 0.6f);
                    Layout.Holograms.Add(new NeonBoard
                    {
                        Gx = b.Lot.Gx, Gz = b.Lot.Gz, X = x, Y = y, Z = z, Nx = nx, Nz = nz, Width = w, Height = h, Panel = panel, Colour = tint,
                        Seed = B() * 10, Glitch = 1.1 + B() * 0.9, TickerSpeed = 0.06 + B() * 0.1, Gain = 1.1 + B() * 0.8,
                    });
                    QuadIn(b.Lot.Gx, b.Lot.Gz, x, y, z, nx, nz, Math.Max(w, h) * 1.2, Math.Max(w, h) * 1.2, 0, NeonShape.Radial, tint, 0.22, 0, B() * 10, 0);
                }
            }

            /// <summary>White pulled <paramref name="share"/> of the way toward a hue.</summary>
            static Rgb Lean(Rgb hue, float share) =>
                new Rgb(1f + (hue.R - 1f) * share, 1f + (hue.G - 1f) * share, 1f + (hue.B - 1f) * share);

            /// <summary>One shopfront bay: fascia sign, plaque, doorway tubes, spill on the road.</summary>
            void Shopfront(NeonDistrict district, double s, double width, double fasciaY, double clear, bool street)
            {
                double nx = _w.Nx, nz = _w.Nz;

                if (R() > (street ? 0.08 : 0.5))
                {
                    double sw = Math.Max(1.4, width * (0.8 + R() * 0.18));
                    double sh = sw * (0.24 + R() * 0.11);
                    var p = P(s, fasciaY + sh * 0.55 + 0.12, 0);
                    PanelSign(p.X, p.Y, p.Z, nx, nz, sw, sh, 0.26 + R() * 0.2, district, pool: street, SignGroup.Word);
                }

                if (R() > 0.66)
                {
                    double sz = 0.9 + R() * 0.8;
                    var p = P(s + (R() - 0.5) * width * 0.5, fasciaY + 1.9 + R() * 1.4, 0);
                    PanelSign(p.X, p.Y, p.Z, nx, nz, sz, sz, 0.22 + R() * 0.18, district, pool: false, SignGroup.Kanji);
                }

                if (R() > (street ? 0.42 : 0.72))
                {
                    Rgb col = district.PickHue(_rng);
                    double gap = Math.Min(width * 0.45, 1.1);
                    double doorY = _w.Y0 + 1.35;
                    foreach (int sgn in new[] { -1, 1 })
                    {
                        var p = P(s + sgn * gap, doorY, 0.12);
                        Quad(p.X, p.Y, p.Z, nx, nz, 2.2, 0.14, Math.PI / 2, NeonShape.Tube, col, 2.0, 0, null, 0);
                    }
                    if (R() > 0.55)
                    {
                        var p = P(s, doorY + 1.5, 0.12);
                        Rgb iconCol = district.PickHue(_rng);
                        int mode = R() > 0.86 ? 1 : 0;
                        Quad(p.X, p.Y, p.Z, nx, nz, 0.65, 0.65, 0, NeonShape.Panel, iconCol, 3.0, mode, null, 0, Cell(SignGroup.Icon));
                    }
                    if (street)
                    {
                        var p = P(s, doorY - 0.1, 0.11);
                        Quad(p.X, p.Y, p.Z, nx, nz, 2.6, 2.6, 0, NeonShape.Radial, col, 1.1, 0, null, 0);
                        var g = P(s, 0, Math.Min(clear * 0.6, 2.2));
                        Pool(g.X, g.Z, col, 3.2 + R() * 1.8, 0.55);
                    }
                }
            }

            void StripAt(NeonDistrict district, double s, double y, double len, double thick, double chase, double gainScale)
            {
                Rgb color = district.PickHue(_rng);
                int mode = R() > 0.94 ? 1 : 0;
                var p = P(s, y, 0.11);
                NeonShape shape = R() > 0.4 ? NeonShape.Tube : NeonShape.TubeSoft;
                double gain = (2.5 + R() * 1.0) * gainScale;
                Quad(p.X, p.Y, p.Z, _w.Nx, _w.Nz, len, thick, 0, shape, color, gain, mode, null, chase);
            }

            /// <summary>Sign face on a dark housing proud of the wall, optionally with a pool of light on the street.</summary>
            void PanelSign(double x, double y, double z, double nx, double nz, double width, double height, double proj, NeonDistrict district, bool pool, SignGroup lettering)
            {
                Rgb color = district.PickHue(_rng);
                double r = R();
                int mode = r > 0.93 ? 1 : r > 0.88 ? 2 : r > 0.85 ? 3 : 0;
                double gain = 2.6 + R() * 1.5;

                // Housing sits proud of the wall; the lit face floats just in front of it.
                Box(x + nx * proj * 0.5, y, z + nz * proj * 0.5, nx, nz, width + 0.18, height + 0.18, proj);
                Quad(x + nx * (proj + 0.035), y, z + nz * (proj + 0.035), nx, nz, width, height, 0, NeonShape.Panel, color, gain, mode, null, 0, Cell(lettering));

                if (pool) Pool(x + nx * (proj + 1.6), z + nz * (proj + 1.6), color, Math.Max(3, width * 1.5), 0.4);
            }

            /// <summary>Vertical blade projecting perpendicular to the facade, lit on both sides.</summary>
            void Blade(double u, double y, double width, double h, double proj, NeonDistrict district)
            {
                double nx = _w.Nx, nz = _w.Nz, tx = _w.Tx, tz = _w.Tz;
                Rgb color = district.PickHue(_rng);
                double r = R();
                int mode = r > 0.90 ? 1 : r > 0.84 ? 3 : 0;
                double gain = 2.8 + R() * 1.6;
                double seed = R() * 10;
                string lettering = Cell(SignGroup.Vertical);

                double px = _w.Ox, pz = _w.Oz;
                double cx = px + tx * u + nx * (proj * 0.5 + 0.1);
                double cz = pz + tz * u + nz * (proj * 0.5 + 0.1);

                // Housing: a slab whose long axis runs out from the wall, plus a bracket back to it.
                BoxOriented(cx, y, cz, tx, tz, proj + 0.2, h + 0.2, 0.34);
                BoxOriented(px + tx * u + nx * 0.25, y + h * 0.42, pz + tz * u + nz * 0.25, tx, tz, 0.5, 0.14, 0.14);

                // Both faces share a seed so the two sides of one tube flicker together.
                foreach (int side in new[] { 1, -1 })
                {
                    Quad(cx + tx * side * 0.20, y, cz + tz * side * 0.20, tx * side, tz * side, proj * 0.86, h, 0,
                        NeonShape.Panel, color, gain, mode, seed, 0, lettering);
                }
                // Tube running down the leading edge, away from the wall.
                Quad(px + tx * u + nx * (proj + 0.12), y, pz + tz * u + nz * (proj + 0.12), nx, nz, h, 0.16, Math.PI / 2,
                    NeonShape.Tube, color, gain * 0.8, mode, seed, 0);

                Pool(cx + nx * 1.2, cz + nz * 1.2, color, 5.5, 0.5);
            }

            void Quad(double x, double y, double z, double nx, double nz, double width, double height, double roll,
                NeonShape shape, Rgb colour, double gain, int mode, double? seed, double chase, string cell = null) =>
                QuadIn(_w.Gx, _w.Gz, x, y, z, nx, nz, width, height, roll, shape, colour, gain, mode, seed ?? R() * 10, chase, cell);

            void QuadIn(int gx, int gz, double x, double y, double z, double nx, double nz, double width, double height, double roll,
                NeonShape shape, Rgb colour, double gain, int mode, double seed, double chase, string cell = null)
            {
                Layout.Quads.Add(new NeonQuad
                {
                    Gx = gx, Gz = gz, X = x, Y = y, Z = z, Nx = nx, Nz = nz,
                    Width = width, Height = height, Roll = roll, Shape = shape, Colour = colour, Gain = gain,
                    FlickerMode = mode, Seed = seed, Chase = chase, Cell = cell,
                });
            }

            /// <summary>Soft glow on the pavement. The prototype's pool size is the quad's edge length.</summary>
            void Pool(double x, double z, Rgb colour, double size, double gain)
            {
                Layout.Quads.Add(new NeonQuad
                {
                    Gx = _w.Gx, Gz = _w.Gz, X = x, Y = PoolY, Z = z, FacingUp = true, Width = size, Height = size,
                    Shape = NeonShape.Radial, Colour = colour, Gain = gain, FlickerMode = 0, Seed = 0, Chase = 0,
                });
            }

            void Box(double x, double y, double z, double nx, double nz, double width, double height, double depth)
            {
                Layout.Boxes.Add(new NeonBox
                {
                    Gx = _w.Gx, Gz = _w.Gz, X = x, Y = y, Z = z, DirX = nx, DirZ = nz,
                    SizeX = width, SizeY = height, SizeZ = Math.Max(0.08, depth),
                });
            }

            /// <summary>Housing box for a perpendicular fitting: local Z runs along (dx, dz), so thick runs across it and len runs out from the wall.</summary>
            void BoxOriented(double x, double y, double z, double dx, double dz, double len, double h, double thick)
            {
                Layout.Boxes.Add(new NeonBox
                {
                    Gx = _w.Gx, Gz = _w.Gz, X = x, Y = y, Z = z, DirX = dx, DirZ = dz,
                    SizeX = len, SizeY = h, SizeZ = thick,
                });
            }
        }
    }
}
