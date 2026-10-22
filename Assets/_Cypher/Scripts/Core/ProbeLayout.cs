using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// One reflection probe: the point its cubemap is rendered from and the box that cubemap is projected onto. They differ
    /// because the capture point has to be somewhere open (a road junction) while the box covers a whole district.
    /// </summary>
    public readonly struct ProbeRegion
    {
        public readonly double CaptureX, CaptureY, CaptureZ;
        public readonly double BoxCentreX, BoxCentreY, BoxCentreZ;
        public readonly double SizeX, SizeY, SizeZ;

        public ProbeRegion(double captureX, double captureY, double captureZ, double boxCentreX, double boxCentreY, double boxCentreZ, double sizeX, double sizeY, double sizeZ)
        {
            CaptureX = captureX; CaptureY = captureY; CaptureZ = captureZ;
            BoxCentreX = boxCentreX; BoxCentreY = boxCentreY; BoxCentreZ = boxCentreZ;
            SizeX = sizeX; SizeY = sizeY; SizeZ = sizeZ;
        }

        public bool ContainsXz(double x, double z) =>
            Math.Abs(x - BoxCentreX) <= SizeX / 2 && Math.Abs(z - BoxCentreZ) <= SizeZ / 2;
    }

    /// <summary>
    /// Where to put the street reflection probes for a window of the city. One box over 450 m would put the wrong neon in the
    /// puddles far from where the cubemap was taken, so the window is split into a grid of districts, each with its own probe
    /// captured at an open junction near its middle. Neighbouring boxes overlap, and the renderer blends across the overlap.
    /// </summary>
    public static class ProbeLayout
    {
        public const int DefaultPerAxis = 3;

        /// <summary>How far each box reaches past its district on every side; also the width of the blend between neighbours.</summary>
        public const double Overlap = 36;

        public const double CaptureHeight = 6;
        public const double BoxCentreY = 6;
        public const double BoxHeight = 80;

        /// <param name="outerMargin">Extra ground to cover beyond the window's outer roads, such as the apron before the world edge.</param>
        public static IReadOnlyList<ProbeRegion> Build(CityWindow window, int perAxis = DefaultPerAxis, double outerMargin = 0)
        {
            if (perAxis < 1) throw new ArgumentOutOfRangeException(nameof(perAxis), perAxis, "a layout needs at least one probe per axis");
            if (!(outerMargin >= 0)) throw new ArgumentOutOfRangeException(nameof(outerMargin), outerMargin, "the margin cannot be negative");

            Rect2 inner = window.RoadExtent;
            var extent = new Rect2(inner.X0 - outerMargin, inner.Z0 - outerMargin, inner.X1 + outerMargin, inner.Z1 + outerMargin);
            double spanX = extent.Width / perAxis, spanZ = extent.Depth / perAxis;
            var regions = new List<ProbeRegion>(perAxis * perAxis);
            for (int iz = 0; iz < perAxis; iz++)
            {
                for (int ix = 0; ix < perAxis; ix++)
                {
                    double centreX = extent.X0 + (ix + 0.5) * spanX;
                    double centreZ = extent.Z0 + (iz + 0.5) * spanZ;
                    regions.Add(new ProbeRegion(
                        NearestRoad(centreX, window.Gx0, window.Gx1), CaptureHeight, NearestRoad(centreZ, window.Gz0, window.Gz1),
                        centreX, BoxCentreY, centreZ,
                        spanX + 2 * Overlap, BoxHeight, spanZ + 2 * Overlap));
                }
            }
            return regions;
        }

        /// <summary>The middle of the road corridor nearest <paramref name="coord"/>, among the roads that border the window's cells.</summary>
        static double NearestRoad(double coord, int firstCell, int lastCell)
        {
            double best = CityLayout.RoadCentre(firstCell);
            double bestDistance = Math.Abs(coord - best);
            for (int i = firstCell + 1; i <= lastCell + 1; i++)   // each cell's road is its low edge, so the last cell adds one on its high side
            {
                double road = CityLayout.RoadCentre(i);
                double d = Math.Abs(coord - road);
                if (d < bestDistance - 1e-9)
                {
                    best = road;
                    bestDistance = d;
                }
            }
            return best;
        }
    }
}
