using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PropPlacerTests
    {
        static readonly CityPlan Plan = CityPlan.Generate();
        static readonly PropLayout Whole = PropPlacer.Build(Plan, CityWindow.AroundPlaza(3));

        const double Sidewalk = CityLayout.SidewalkHeight;

        static bool OnGround(PropInstance i) => i.Y < 1.0;

        static (double x0, double x1, double z0, double z1) Footprint(PropBox b)
        {
            double cs = Math.Abs(Math.Cos(b.Yaw)), sn = Math.Abs(Math.Sin(b.Yaw));
            double ex = b.Hx * cs + b.Hz * sn, ez = b.Hx * sn + b.Hz * cs;
            return (b.Cx - ex, b.Cx + ex, b.Cz - ez, b.Cz + ez);
        }

        [Test]
        public void TheSameSeedGivesTheSameStreetAndAnotherSeedADifferentOne()
        {
            PropLayout again = PropPlacer.Build(Plan, CityWindow.AroundPlaza(3));
            Assert.AreEqual(Whole.Instances.Count, again.Instances.Count);
            Assert.AreEqual(Whole.Decals.Count, again.Decals.Count);
            Assert.AreEqual(Whole.Boxes.Count, again.Boxes.Count);
            for (int i = 0; i < Whole.Instances.Count; i += 97)
            {
                Assert.AreEqual(Whole.Instances[i].Kit, again.Instances[i].Kit);
                Assert.AreEqual(Whole.Instances[i].X, again.Instances[i].X);
                Assert.AreEqual(Whole.Instances[i].Ry, again.Instances[i].Ry);
            }
            PropLayout other = PropPlacer.Build(Plan, CityWindow.AroundPlaza(3), PropPlacer.DefaultSeed + 1);
            Assert.AreNotEqual(Whole.Instances.Sum(p => p.X), other.Instances.Sum(p => p.X));
        }

        [Test]
        public void TheWholeCityGetsThousandsOfPropsAndEveryKitIsUsed()
        {
            Assert.That(Whole.Instances.Count, Is.InRange(6000, 13000));
            var used = new HashSet<string>(Whole.Instances.Select(i => i.Kit));
            foreach (string kit in PropKits.All.Keys) Assert.IsTrue(used.Contains(kit), $"nothing is ever a {kit}");
        }

        [Test]
        public void EveryInstanceNamesARealKitAtAFiniteSizeAndPlace()
        {
            foreach (PropInstance i in Whole.Instances)
            {
                Assert.IsTrue(PropKits.All.ContainsKey(i.Kit), i.Kit);
                Assert.That(i.Scale, Is.InRange(0.7, 1.4), i.Kit);
                Assert.IsFalse(double.IsNaN(i.X + i.Y + i.Z + i.Ry), i.Kit);
                Assert.IsTrue(WorldEdge.IsInsidePlayArea(i.X, i.Z), $"{i.Kit} at ({i.X:0}, {i.Z:0}) is outside the play area");
            }
        }

        [Test]
        public void TheSpawnPocketIsLeftClear()
        {
            foreach (PropInstance i in Whole.Instances.Where(OnGround))
            {
                Assert.IsFalse(Math.Abs(i.X) < 2.5 && Math.Abs(i.Z) < 2.5, $"{i.Kit} at ({i.X:0.0}, {i.Z:0.0}) sits where the player starts");
            }
            foreach (PropBox b in Whole.Boxes)
            {
                var f = Footprint(b);
                Assert.IsFalse(f.x0 < 1.5 && f.x1 > -1.5 && f.z0 < 1.5 && f.z1 > -1.5, "a collider in the spawn pocket");
            }
        }

        [Test]
        public void NoGroundPropStandsInsideABuilding()
        {
            foreach (PropInstance i in Whole.Instances.Where(OnGround))
            {
                Assert.IsFalse(Plan.IsBlocked(i.X, i.Z), $"{i.Kit} at ({i.X:0.0}, {i.Z:0.0}) is inside a lot");
            }
        }

        [Test]
        public void GroundPropsStandOnTheSidewalkOrTheRoad()
        {
            foreach (PropInstance i in Whole.Instances.Where(OnGround))
            {
                bool onSidewalk = Math.Abs(i.Y - Sidewalk) < 0.02 || Math.Abs(i.Y - (Sidewalk + 0.01)) < 0.02;
                bool onRoad = i.Y < 0.7;   // the road, or a crate stacked on a pallet
                Assert.IsTrue(onSidewalk || onRoad, $"{i.Kit} floats at y = {i.Y}");
            }
        }

        [Test]
        public void ColliderBoxesMatchTheSolidPropsOnTheGround()
        {
            int solidOnGround = Whole.Instances.Count(i => i.Y < 3 && PropKits.Get(i.Kit).Collider.HasValue);
            Assert.AreEqual(solidOnGround, Whole.Boxes.Count, "one box for each ground prop whose kit has a collider");
            Assert.That(Whole.Boxes.Count, Is.InRange(700, 2200));
            foreach (PropBox b in Whole.Boxes)
            {
                Assert.Greater(b.Hx, 0); Assert.Greater(b.Hy, 0); Assert.Greater(b.Hz, 0);
                Assert.That(b.Cy, Is.InRange(0.1, 1.9));
            }
        }

        [Test]
        public void NoColliderReachesIntoTheMiddleOfAStreet()
        {
            // the benchmark route, the title flight and every walker use the middle of the road: a collider there would stop them
            var centres = Enumerable.Range(0, CityLayout.Grid + 1).Select(CityLayout.RoadCentre).ToList();
            foreach (PropBox b in Whole.Boxes)
            {
                var f = Footprint(b);
                foreach (double rc in centres)
                {
                    Assert.IsFalse(f.x0 < rc + 1.2 && f.x1 > rc - 1.2, $"a collider at ({b.Cx:0.0}, {b.Cz:0.0}) blocks the north-south road at x = {rc}");
                    Assert.IsFalse(f.z0 < rc + 1.2 && f.z1 > rc - 1.2, $"a collider at ({b.Cx:0.0}, {b.Cz:0.0}) blocks the east-west road at z = {rc}");
                }
            }
        }

        [Test]
        public void ColliderBoxesAreTurnedWithTheirProps()
        {
            // a bench against a wall faces out from it: its collider carries the same yaw as the prop
            var benchBoxes = Whole.Boxes.Where(b => Math.Abs(b.Hx - 0.95 * 1) < 0.2).ToList();
            Assert.IsNotEmpty(benchBoxes);
            Assert.IsTrue(Whole.Boxes.Any(b => Math.Abs(b.Yaw) > 0.3), "colliders turn with their props");
            foreach (PropBox b in Whole.Boxes) Assert.IsFalse(double.IsNaN(b.Yaw));
        }

        [Test]
        public void RooftopGearSitsAboveTheRoofsWithoutColliders()
        {
            var roof = Whole.Instances.Where(i => i.Y > 6).ToList();
            Assert.Greater(roof.Count(i => i.Kit == "roofunit"), 50);
            Assert.Greater(roof.Count(i => i.Kit == "ventstack"), 50);
            foreach (PropInstance i in roof.Where(p => p.Kit == "roofunit" || p.Kit == "ventstack" || p.Kit == "dronepad" || p.Kit == "utilbox"))
            {
                Assert.That(i.Y, Is.GreaterThan(Sidewalk + 3), "on a roof");
            }
        }

        [Test]
        public void WallFixturesHangOnTheWallsAboveHeadHeight()
        {
            foreach (PropInstance i in Whole.Instances.Where(p => p.Kit == "acunit" || p.Kit == "camwall" || p.Kit == "satdish"))
            {
                Assert.GreaterOrEqual(i.Y, 1.5, i.Kit);
            }
            var atms = Whole.Instances.Where(p => p.Kit == "atm").ToList();
            Assert.Greater(atms.Count, 20, "cash machines on the street frontages");
            foreach (PropInstance a in atms) Assert.AreEqual(1.6, a.Y, 1e-9);
        }

        [Test]
        public void EveryWallFixtureStandsAgainstAWall()
        {
            var walls = NeonWalls.Build(Plan, CityWindow.AroundPlaza(3)).Where(w => w.Ground && (w.Street || w.Alley) && w.Length > 1.6).ToList();
            foreach (PropInstance i in Whole.Instances.Where(p => p.Kit == "atm").Take(60))
            {
                bool near = walls.Any(w =>
                {
                    double dx = i.X - w.Ox, dz = i.Z - w.Oz;
                    double along = dx * w.Tx + dz * w.Tz, off = dx * w.Nx + dz * w.Nz;
                    return along >= -0.01 && along <= w.Length + 0.01 && Math.Abs(off - 0.05) < 0.01;
                });
                Assert.IsTrue(near, $"an ATM at ({i.X:0.0}, {i.Z:0.0}) is not on a wall");
            }
        }

        [Test]
        public void DecalsAreOfBothKindsOnTheRightSurfaces()
        {
            var ground = Whole.Decals.Where(d => d.Kind == DecalKind.Ground).ToList();
            var wall = Whole.Decals.Where(d => d.Kind == DecalKind.Graffiti).ToList();
            Assert.That(ground.Count, Is.InRange(4500, 7500));
            Assert.That(wall.Count, Is.InRange(2000, 3600));
            foreach (DecalInstance d in ground)
            {
                Assert.That(d.Y, Is.InRange(0.01, 0.21), "on the ground, a hair above it");
                Assert.That(d.Tile, Is.InRange(0, 15));
                Assert.Greater(d.Sx, 0.4); Assert.Greater(d.Sy, 0.4);
            }
            foreach (DecalInstance d in wall)
            {
                Assert.That(d.Y, Is.InRange(0.7, 6.5), "on the lowest few metres of a wall");
                Assert.That(d.Tile, Is.InRange(0, 15));
                Assert.That(d.Sx, Is.InRange(0.7, 3.8));
            }
        }

        [Test]
        public void GroundDecalsAvoidTheInsideOfBuildings()
        {
            foreach (DecalInstance d in Whole.Decals.Where(x => x.Kind == DecalKind.Ground && x.Y > 0.1))
            {
                Assert.IsFalse(Plan.IsBlocked(d.X, d.Z), $"a pavement stain at ({d.X:0.0}, {d.Z:0.0}) is inside a lot");
            }
        }

        [Test]
        public void SteamRisesFromStallsAndVentsAndTheBudgetIsFull()
        {
            Assert.That(Whole.SteamSpots.Count, Is.InRange(100, 300));
            Assert.AreEqual(96, Whole.Puffs.Count);
            foreach (SteamPuff p in Whole.Puffs)
            {
                Assert.That(p.Phase, Is.InRange(0.0, 1.0));
                Assert.That(p.Speed, Is.InRange(0.16, 0.32));
                Assert.That(p.Size, Is.InRange(0.9, 2.1));
                Assert.That(p.Rise, Is.InRange(1.6, 3.2));
                Assert.That(p.Drift, Is.InRange(-0.3, 0.3));
                Assert.IsTrue(Whole.SteamSpots.Any(s => Math.Abs(p.X - s.X) <= s.R + 1e-9 && Math.Abs(p.Z - s.Z) <= s.R + 1e-9 && Math.Abs(p.Y - s.Y) < 1e-9), "a puff starts at one of the spots");
            }
        }

        [Test]
        public void CablesAndConduitAreGeometryGroupedByCell()
        {
            Assert.Greater(Whole.CellParts.Count, 30);
            int triangles = 0;
            foreach (var kv in Whole.CellParts)
            {
                Assert.That(kv.Key.Gx, Is.InRange(0, CityLayout.Grid - 1));
                Assert.That(kv.Key.Gz, Is.InRange(0, CityLayout.Grid - 1));
                Assert.Greater(kv.Value.TriangleCount, 0);
                triangles += kv.Value.TriangleCount;
            }
            Assert.That(triangles, Is.InRange(15000, 90000), "cables are thin tubes: tens of thousands of triangles over the whole city");
        }

        [Test]
        public void ASmallerWindowGetsOnlyItsOwnProps()
        {
            CityWindow small = CityWindow.AroundPlaza(1);
            PropLayout l = PropPlacer.Build(Plan, small);
            Assert.That(l.Instances.Count, Is.InRange(Whole.Instances.Count / 20, Whole.Instances.Count / 4));
            Rect2 e = small.RoadExtent;
            foreach (PropInstance i in l.Instances.Where(OnGround).Where(i => i.Y < 1))
            {
                Assert.That(i.X, Is.InRange(e.X0 - 6, e.X1 + 6), i.Kit);
                Assert.That(i.Z, Is.InRange(e.Z0 - 6, e.Z1 + 6), i.Kit);
            }
        }

        [Test]
        public void ThePlazaIsFurnishedAroundItsEdgeAndOpenInTheMiddle()
        {
            double c = CityLayout.CellCentre(CityLayout.PlazaIndex);
            var inPlaza = Whole.Instances.Where(i => OnGround(i) && Math.Abs(i.X - c) < 25.5 && Math.Abs(i.Z - c) < 25.5).ToList();
            Assert.Greater(inPlaza.Count, 20);
            Assert.AreEqual(1, inPlaza.Count(i => i.Kit == "dronepad" && Math.Abs(i.Y - (Sidewalk + 0.01)) < 1e-9), "one drone pad on the plaza deck");
        }

        [Test]
        public void ANullPlanFailsLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => PropPlacer.Build(null, CityWindow.AroundPlaza(1)));
        }
    }
}
