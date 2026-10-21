using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class StreetMesherTests
    {
        static StreetCell Cell(double cx, double cz) => new StreetCell { Gx = 3, Gz = 3, Cx = cx, Cz = cz };

        static void TriNormal(MeshData m, int tri, out double x, out double y, out double z)
        {
            int ia = m.Indices[tri * 3] * 3, ib = m.Indices[tri * 3 + 1] * 3, ic = m.Indices[tri * 3 + 2] * 3;
            double ax = m.Positions[ib] - m.Positions[ia], ay = m.Positions[ib + 1] - m.Positions[ia + 1], az = m.Positions[ib + 2] - m.Positions[ia + 2];
            double bx = m.Positions[ic] - m.Positions[ia], by = m.Positions[ic + 1] - m.Positions[ia + 1], bz = m.Positions[ic + 2] - m.Positions[ia + 2];
            x = ay * bz - az * by; y = az * bx - ax * bz; z = ax * by - ay * bx;
            double len = Math.Sqrt(x * x + y * y + z * z);
            x /= len; y /= len; z /= len;
        }

        [Test]
        public void SidewalkIsFiveDeckSlabsAndFourKerbFaces()
        {
            var mb = new MeshBuilder();
            StreetMesher.AddSidewalk(mb, Cell(10, -20));
            Assert.AreEqual((5 + 4) * 2, mb.TriangleCount);
        }

        [Test]
        public void SidewalkDeckSitsAtKerbHeightAndSpansBlockPlusPad()
        {
            var mb = new MeshBuilder();
            StreetMesher.AddSidewalk(mb, Cell(10, -20));
            MeshData m = mb.Build();

            double maxY = double.NegativeInfinity, minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            for (int i = 0; i < m.VertexCount; i++)
            {
                maxY = Math.Max(maxY, m.Positions[i * 3 + 1]);
                minX = Math.Min(minX, m.Positions[i * 3]);
                maxX = Math.Max(maxX, m.Positions[i * 3]);
            }
            Assert.AreEqual(CityLayout.SidewalkHeight, maxY, 1e-6);
            Assert.AreEqual(10 - (CityLayout.Block / 2 + CityLayout.SidewalkPad), minX, 1e-4);
            Assert.AreEqual(10 + (CityLayout.Block / 2 + CityLayout.SidewalkPad), maxX, 1e-4);
        }

        [Test]
        public void DeckFacesUpAndKerbFacesPointOutOfTheBlock()
        {
            var mb = new MeshBuilder();
            StreetMesher.AddSidewalk(mb, Cell(0, 0));
            MeshData m = mb.Build();

            int up = 0, outward = 0;
            for (int t = 0; t < m.TriangleCount; t++)
            {
                TriNormal(m, t, out double nx, out double ny, out double nz);
                int vi = m.Indices[t * 3] * 3;
                if (ny > 0.99) { up++; continue; }

                Assert.AreEqual(0.0, ny, 1e-6, "kerb faces are vertical");
                double px = m.Positions[vi], pz = m.Positions[vi + 2];
                Assert.Greater(nx * px + nz * pz, 0, $"kerb triangle {t} faces into the block");
                outward++;
            }
            Assert.AreEqual(10, up);
            Assert.AreEqual(8, outward);
        }

        [Test]
        public void MarkingsLieFlatJustAboveTheRoadbed()
        {
            var mb = new MeshBuilder();
            StreetMesher.AddMarkings(mb, CityWindow.AroundPlaza(1));
            MeshData m = mb.Build();

            Assert.Greater(m.TriangleCount, 0);
            for (int i = 0; i < m.VertexCount; i++)
            {
                Assert.AreEqual(0.014, m.Positions[i * 3 + 1], 1e-6);
            }
            for (int t = 0; t < m.TriangleCount; t++)
            {
                TriNormal(m, t, out _, out double ny, out _);
                Assert.Greater(ny, 0.99, $"marking triangle {t} faces down");
            }
        }

        [Test]
        public void MarkingsStayNearTheWindowRoads()
        {
            CityWindow w = CityWindow.AroundPlaza(0);
            var mb = new MeshBuilder();
            StreetMesher.AddMarkings(mb, w);
            MeshData m = mb.Build();

            Rect2 e = w.RoadExtent;
            const double reach = 7;   // stop bars and zebras overshoot the intersection by < 6 m
            for (int i = 0; i < m.VertexCount; i++)
            {
                double x = m.Positions[i * 3], z = m.Positions[i * 3 + 2];
                Assert.That(x, Is.InRange(e.X0 - reach, e.X1 + reach));
                Assert.That(z, Is.InRange(e.Z0 - reach, e.Z1 + reach));
            }
        }

        [Test]
        public void BiggerWindowGivesMoreMarkings()
        {
            int Count(int radius)
            {
                var mb = new MeshBuilder();
                StreetMesher.AddMarkings(mb, CityWindow.AroundPlaza(radius));
                return mb.TriangleCount;
            }
            Assert.Greater(Count(1), Count(0));
            Assert.Greater(Count(2), Count(1));
        }

        [Test]
        public void RoadbedIsOneUpFacingQuadCoveringTheCityWithMargin()
        {
            var mb = new MeshBuilder();
            StreetMesher.AddRoadbed(mb);
            MeshData m = mb.Build();

            Assert.AreEqual(2, m.TriangleCount);
            double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
            for (int i = 0; i < m.VertexCount; i++)
            {
                Assert.AreEqual(0.0, m.Positions[i * 3 + 1], 1e-9);
                Assert.AreEqual(1.0, m.Normals[i * 3 + 1], 1e-6);
                minX = Math.Min(minX, m.Positions[i * 3]);
                maxX = Math.Max(maxX, m.Positions[i * 3]);
            }
            Assert.AreEqual(StreetMesher.RoadbedSize, maxX - minX, 1e-2);
            Assert.AreEqual(StreetMesher.RoadbedCentre, (minX + maxX) / 2, 1e-2);
            Assert.Less(minX, CityLayout.Base - CityLayout.Cell, "margin beyond the outer roads");
        }

        [Test]
        public void NullArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => StreetMesher.AddRoadbed(null));
            Assert.Throws<ArgumentNullException>(() => StreetMesher.AddSidewalk(null, Cell(0, 0)));
            Assert.Throws<ArgumentNullException>(() => StreetMesher.AddSidewalk(new MeshBuilder(), null));
            Assert.Throws<ArgumentNullException>(() => StreetMesher.AddMarkings(null, CityWindow.AroundPlaza(1)));
        }
    }
}
