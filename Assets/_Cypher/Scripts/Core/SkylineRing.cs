using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>One tower of the ring: its footprint, how tall it stands and which run (side, row, segment) it belongs to.</summary>
    public sealed class SkylineTower
    {
        public Rect2 Footprint;
        public double Height;
        public int Side, Row, Segment;
        public double Shade;
        public int RowBase, ColBase;
    }

    /// <summary>A stretch of the skyline ring: one wall mesh and one roof mesh, the unit of frustum culling.</summary>
    public sealed class SkylineSegment
    {
        public MeshData Walls = new MeshData();
        public MeshData Roofs = new MeshData();
        public int TowerCount;
        public double MaxY;

        public int TriangleCount => Walls.TriangleCount + Roofs.TriangleCount;
    }

    /// <summary>
    /// Plain towers in two rows around the outside of the play area, so the edge of the city is not an empty plain.
    /// They are windowed boxes built with the same mesher and atlas as the real buildings but have no sidewalk, signage
    /// or colliders, and are far enough out (and far enough into the fog) that nobody can reach or inspect them.
    /// </summary>
    public static class SkylineRing
    {
        public const uint DefaultSeed = 55031;

        /// <summary>Distance from the wall to the first row of towers.</summary>
        public const double Gap = 45;

        public const double InnerHalf = WorldEdge.PlayHalf + WorldEdge.WallThickness + Gap;
        public const int Rows = 2;
        public const double RowSpacing = 50;

        /// <summary>West and east runs end this far inside the south and north runs' inner edge, so no tower overlaps another.</summary>
        public const double EndMargin = 8;

        /// <summary>How far a tower may stray from its row's line, either way.</summary>
        public const double Jitter = 4;

        /// <summary>Half-width of the ring's outer edge; the roadbed has to reach past this.</summary>
        public const double OuterHalf = InnerHalf + RowSpacing * Rows + 20;

        public const double MinWidth = 22, MaxWidth = 44;
        public const double MinDepth = 22, MaxDepth = 38;
        public const double MinGap = 6, MaxGap = 16;
        public const double MinHeight = 60, MaxHeight = 230;

        /// <summary>Towers in a run are grouped into segments about this long.</summary>
        public const double SegmentLength = 180;

        /// <summary>Where every tower stands and how tall it is. Sides: 0 south, 1 north, 2 west, 3 east.</summary>
        public static IReadOnlyList<SkylineTower> Plan(uint seed = DefaultSeed)
        {
            var rng = new Rng(seed);
            var towers = new List<SkylineTower>();

            for (int side = 0; side < 4; side++)
            {
                for (int row = 0; row < Rows; row++)
                {
                    double ring = InnerHalf + row * RowSpacing;
                    // South and north runs span the full width; west and east runs stop short of where those begin.
                    double reach = side < 2 ? OuterHalf : InnerHalf - EndMargin;
                    double along = -reach;
                    while (true)
                    {
                        double width = Lerp(MinWidth, MaxWidth, rng.Next());
                        double depth = Lerp(MinDepth, MaxDepth, rng.Next());
                        double height = TowerHeight(rng.Next(), row);
                        double gap = Lerp(MinGap, MaxGap, rng.Next());
                        double jitter = (rng.Next() * 2 - 1) * Jitter;
                        double shade = 0.34 + rng.Next() * 0.14;
                        int rowBase = (int)(rng.Next() * CityLayout.WindowAtlas);
                        int colBase = (int)(rng.Next() * CityLayout.WindowAtlas);
                        if (along + width > reach) break;

                        towers.Add(new SkylineTower
                        {
                            Footprint = Footprint(side, ring + jitter, along, width, depth),
                            Height = Math.Max(1, Math.Round(height / CityLayout.FloorHeight)) * CityLayout.FloorHeight,
                            Side = side,
                            Row = row,
                            Segment = (int)Math.Floor((along + reach) / SegmentLength),
                            Shade = shade,
                            RowBase = rowBase,
                            ColBase = colBase,
                        });
                        along += width + gap;
                    }
                }
            }
            return towers;
        }

        public static IReadOnlyList<SkylineSegment> Build(uint seed = DefaultSeed)
        {
            var parts = new Dictionary<(int Side, int Row, int Segment), (MeshBuilder Walls, MeshBuilder Roofs, SkylineSegment Segment)>();
            var order = new List<(int, int, int)>();
            int index = 0;
            foreach (SkylineTower t in Plan(seed))
            {
                var key = (t.Side, t.Row, t.Segment);
                if (!parts.TryGetValue(key, out var p))
                {
                    p = (new MeshBuilder(), new MeshBuilder(), new SkylineSegment());
                    parts.Add(key, p);
                    order.Add(key);
                }
                new BuildingMesher(p.Walls, p.Roofs).AddBuilding(ToBuilding(t, index++));
                p.Segment.TowerCount++;
                p.Segment.MaxY = Math.Max(p.Segment.MaxY, t.Height);
            }

            var result = new List<SkylineSegment>(parts.Count);
            foreach ((int, int, int) key in order)
            {
                var p = parts[key];
                p.Segment.Walls = p.Walls.Build();
                p.Segment.Roofs = p.Roofs.Build();
                result.Add(p.Segment);
            }
            return result;
        }

        /// <summary>The nearer row is the lower and the outer the taller, so the rows read as layers and not as one wall.</summary>
        static double TowerHeight(double r, int row) => Lerp(MinHeight, MaxHeight, row == 0 ? r * r : Math.Sqrt(r));

        static Rect2 Footprint(int side, double ring, double along, double width, double depth)
        {
            switch (side)
            {
                case 0: return new Rect2(along, -ring - depth, along + width, -ring);   // south: z negative
                case 1: return new Rect2(along, ring, along + width, ring + depth);     // north
                case 2: return new Rect2(-ring - depth, along, -ring, along + width);   // west: x negative
                default: return new Rect2(ring, along, ring + depth, along + width);    // east
            }
        }

        static Building ToBuilding(SkylineTower t, int index)
        {
            Rect2 r = t.Footprint;
            int floors = (int)Math.Round(t.Height / CityLayout.FloorHeight);
            var b = new Building
            {
                X = r.CentreX, Z = r.CentreZ, Width = r.Width, Depth = r.Depth, Height = t.Height, Floors = floors,
                Type = BuildingType.Slab,
                Tint = new[] { t.Shade * 0.95, t.Shade, t.Shade * 1.1 },
                RowBase = t.RowBase,
                ColBase = t.ColBase,
            };
            b.Volumes.Add(new Volume
            {
                Poly = new[] { new Point2(r.X0, r.Z0), new Point2(r.X1, r.Z0), new Point2(r.X1, r.Z1), new Point2(r.X0, r.Z1) },
                Y0 = 0, Y1 = t.Height, BuildingIndex = index,
            });
            return b;
        }

        static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
