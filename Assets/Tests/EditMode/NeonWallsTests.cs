using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class NeonWallsTests
    {
        static readonly CityPlan Plan = CityPlan.Generate();
        static readonly CityWindow Window = CityWindow.AroundPlaza(1);
        static readonly List<NeonWall> Walls = NeonWalls.Build(Plan, Window);

        [Test]
        public void ProducesWallsOnlyForBuildingsInsideTheWindow()
        {
            Assert.Greater(Walls.Count, 100);
            Assert.IsTrue(Walls.All(w => Window.Contains(w.Gx, w.Gz)));
            Assert.IsFalse(Walls.Any(w => w.Gx == CityLayout.PlazaIndex && w.Gz == CityLayout.PlazaIndex), "the plaza has no buildings");
        }

        [Test]
        public void WallFramesAreOrthonormalAndAboveTheDeck()
        {
            foreach (NeonWall w in Walls)
            {
                Assert.AreEqual(1.0, Math.Sqrt(w.Nx * w.Nx + w.Nz * w.Nz), 1e-9);
                Assert.AreEqual(1.0, Math.Sqrt(w.Tx * w.Tx + w.Tz * w.Tz), 1e-9);
                Assert.AreEqual(0.0, w.Nx * w.Tx + w.Nz * w.Tz, 1e-9, "tangent is perpendicular to the normal");
                Assert.GreaterOrEqual(w.Length, 0.9);
                Assert.GreaterOrEqual(w.Y0, CityLayout.SidewalkHeight - 1e-9);
                Assert.Greater(w.Y1, w.Y0);
            }
        }

        [Test]
        public void EveryWallFacesAwayFromOneOfItsOwnBuildingsVolumes()
        {
            // The frame orients each wall away from the centre of the volume it belongs to, so the dot product
            // against that volume's bounds centre must be positive. (The whole building's centre would be wrong
            // for an L-shaped wing.)
            foreach (NeonWall w in Walls)
            {
                bool ok = Plan.Buildings
                    .Where(b => b.Lot.Gx == w.Gx && b.Lot.Gz == w.Gz)
                    .SelectMany(b => b.Volumes)
                    .Any(v =>
                    {
                        double x0 = v.Poly.Min(p => p.X), x1 = v.Poly.Max(p => p.X), z0 = v.Poly.Min(p => p.Z), z1 = v.Poly.Max(p => p.Z);
                        bool onVolume = w.Mx >= x0 - 1e-6 && w.Mx <= x1 + 1e-6 && w.Mz >= z0 - 1e-6 && w.Mz <= z1 + 1e-6;
                        double outward = (w.Mx - (x0 + x1) / 2) * w.Nx + (w.Mz - (z0 + z1) / 2) * w.Nz;
                        return onVolume && outward > 0;
                    });
                Assert.IsTrue(ok, $"wall at ({w.Mx:0.0}, {w.Mz:0.0}) does not face away from any volume that contains it");
            }
        }

        [Test]
        public void StreetAndAlleyAreMutuallyExclusiveAndFollowTheProbes()
        {
            foreach (NeonWall w in Walls)
            {
                Assert.IsFalse(w.Street && w.Alley);
                bool open6 = !Plan.IsBlocked(w.Mx + w.Nx * 6, w.Mz + w.Nz * 6);
                if (w.Street) Assert.IsTrue(open6, "a street wall has open space 6 m ahead");
                if (w.Alley) Assert.IsFalse(open6, "an alley wall is closed in within 6 m");
            }
            Assert.IsTrue(Walls.Any(w => w.Street));
            Assert.IsTrue(Walls.Any(w => w.Alley));
        }

        [Test]
        public void UsableMeansStreetOrAlleyAndBigEnough()
        {
            Assert.IsFalse(Walls.All(NeonWalls.IsUsable), "some walls are buried or too small");
            Assert.IsTrue(Walls.Any(NeonWalls.IsUsable));
            foreach (NeonWall w in Walls.Where(NeonWalls.IsUsable))
            {
                Assert.IsTrue(w.Street || w.Alley);
                Assert.Greater(w.Length, 2.4);
                Assert.Greater(w.Y1 - w.Y0, 1.5);
            }
        }

        [Test]
        public void IsUsableAppliesTheDocumentedThresholds()
        {
            var ok = new NeonWall { Street = true, Length = 3, Y0 = 0, Y1 = 5 };
            Assert.IsTrue(NeonWalls.IsUsable(ok));
            Assert.IsFalse(NeonWalls.IsUsable(new NeonWall { Length = 3, Y0 = 0, Y1 = 5 }), "neither street nor alley");
            Assert.IsFalse(NeonWalls.IsUsable(new NeonWall { Alley = true, Length = 2.4, Y0 = 0, Y1 = 5 }), "too short (needs > 2.4)");
            Assert.IsFalse(NeonWalls.IsUsable(new NeonWall { Street = true, Length = 3, Y0 = 0, Y1 = 1.5 }), "too low (needs > 1.5)");
        }

        [Test]
        public void GroundFlagMatchesVolumesThatStandOnTheGround()
        {
            Assert.IsTrue(Walls.Any(w => w.Ground));
            Assert.IsTrue(Walls.Any(w => !w.Ground), "setbacks and towers sit above the ground");
            foreach (NeonWall w in Walls.Where(w => w.Ground))
            {
                Assert.AreEqual(CityLayout.SidewalkHeight, w.Y0, 1e-9);
            }
        }

        [Test]
        public void IsDeterministic()
        {
            List<NeonWall> again = NeonWalls.Build(Plan, Window);
            Assert.AreEqual(Walls.Count, again.Count);
            for (int i = 0; i < Walls.Count; i++)
            {
                Assert.AreEqual(Walls[i].Mx, again[i].Mx);
                Assert.AreEqual(Walls[i].Street, again[i].Street);
            }
        }

        [Test]
        public void BiggerWindowGivesMoreWalls()
        {
            Assert.Greater(NeonWalls.Build(Plan, CityWindow.AroundPlaza(2)).Count, Walls.Count);
            Assert.Less(NeonWalls.Build(Plan, CityWindow.AroundPlaza(0)).Count, Walls.Count);
        }

        [Test]
        public void NullPlanFailsLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => NeonWalls.Build(null, Window));
        }
    }
}
