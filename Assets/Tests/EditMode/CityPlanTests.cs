using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class CityPlanTests
    {
        const double Tol = 1e-9;
        static CityPlan _plan;
        static string[] _golden;

        [OneTimeSetUp]
        public void Setup()
        {
            _plan = CityPlan.Generate(CityPlan.DefaultSeed);
            _golden = Golden.Lines("city_golden_20771.txt");
        }

        [Test]
        public void CountsMatchPrototype()
        {
            Assert.AreEqual(181, _plan.Buildings.Count);
            Assert.AreEqual(197, _plan.Lots.Count);
            Assert.AreEqual(149, _plan.Alleys.Count);
            Assert.AreEqual(405, _plan.Volumes.Count);
            Assert.AreEqual(49, _plan.Streets.Count);
        }

        [Test]
        public void StreetsMatchPrototype()
        {
            List<string[]> rows = Golden.Rows(_golden, "S");
            Assert.AreEqual(rows.Count, _plan.Streets.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                StreetCell s = _plan.Streets[i];
                Assert.AreEqual(Golden.I(rows[i][1]), s.Gx);
                Assert.AreEqual(Golden.I(rows[i][2]), s.Gz);
                Assert.AreEqual(Golden.D(rows[i][3]), s.Cx, Tol);
                Assert.AreEqual(Golden.D(rows[i][4]), s.Cz, Tol);
                Assert.AreEqual(rows[i][5] == "1", s.IsPlaza);
            }
        }

        [Test]
        public void LotsMatchPrototype()
        {
            List<string[]> rows = Golden.Rows(_golden, "L");
            Assert.AreEqual(rows.Count, _plan.Lots.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                Lot l = _plan.Lots[i];
                string ctx = $"lot {i}";
                Assert.AreEqual(Golden.I(rows[i][1]), l.Gx, ctx);
                Assert.AreEqual(Golden.I(rows[i][2]), l.Gz, ctx);
                Assert.AreEqual(Golden.D(rows[i][3]), l.Rect.X0, Tol, ctx);
                Assert.AreEqual(Golden.D(rows[i][4]), l.Rect.Z0, Tol, ctx);
                Assert.AreEqual(Golden.D(rows[i][5]), l.Rect.X1, Tol, ctx);
                Assert.AreEqual(Golden.D(rows[i][6]), l.Rect.Z1, Tol, ctx);
                Assert.AreEqual(rows[i][7] == "1", l.Vacant, ctx);
            }
        }

        [Test]
        public void AlleysMatchPrototype()
        {
            List<string[]> rows = Golden.Rows(_golden, "A");
            Assert.AreEqual(rows.Count, _plan.Alleys.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                Alley a = _plan.Alleys[i];
                Assert.AreEqual(rows[i][1], a.Axis.ToString().ToLowerInvariant(), $"alley {i}");
                Assert.AreEqual(Golden.D(rows[i][2]), a.Rect.X0, Tol);
                Assert.AreEqual(Golden.D(rows[i][3]), a.Rect.Z0, Tol);
                Assert.AreEqual(Golden.D(rows[i][4]), a.Rect.X1, Tol);
                Assert.AreEqual(Golden.D(rows[i][5]), a.Rect.Z1, Tol);
            }
        }

        [Test]
        public void BuildingsMatchPrototype()
        {
            List<string[]> rows = Golden.Rows(_golden, "B");
            Assert.AreEqual(rows.Count, _plan.Buildings.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                Building b = _plan.Buildings[i];
                string[] f = rows[i];
                string ctx = $"building {i}";
                Assert.AreEqual(f[1], b.Type.ToString().ToLowerInvariant(), ctx);
                Assert.AreEqual(Golden.D(f[2]), b.X, Tol, ctx);
                Assert.AreEqual(Golden.D(f[3]), b.Z, Tol, ctx);
                Assert.AreEqual(Golden.D(f[4]), b.Width, Tol, ctx);
                Assert.AreEqual(Golden.D(f[5]), b.Depth, Tol, ctx);
                Assert.AreEqual(Golden.D(f[6]), b.Height, Tol, ctx);
                Assert.AreEqual(Golden.I(f[7]), b.Floors, ctx);
                Assert.AreEqual(Golden.I(f[8]), b.Volumes.Count, ctx);
                Assert.AreEqual(Golden.I(f[9]), b.RowBase, ctx);
                Assert.AreEqual(Golden.I(f[10]), b.ColBase, ctx);
                Assert.AreEqual(Golden.D(f[11]), b.UvOffset, Tol, ctx);
                string[] tint = f[12].Split(',');
                for (int k = 0; k < 3; k++) Assert.AreEqual(Golden.D(tint[k]), b.Tint[k], Tol, ctx);

                if (f[13] == "-")
                {
                    Assert.IsFalse(b.Spire.HasValue, ctx);
                }
                else
                {
                    string[] s = f[13].Split(',');
                    Assert.IsTrue(b.Spire.HasValue, ctx);
                    Assert.AreEqual(Golden.D(s[0]), b.Spire.Value.X, Tol, ctx);
                    Assert.AreEqual(Golden.D(s[1]), b.Spire.Value.Z, Tol, ctx);
                    Assert.AreEqual(Golden.D(s[2]), b.Spire.Value.Y, Tol, ctx);
                    Assert.AreEqual(Golden.D(s[3]), b.Spire.Value.Radius, Tol, ctx);
                    Assert.AreEqual(Golden.D(s[4]), b.Spire.Value.Height, Tol, ctx);
                }

                if (f[14] == "-")
                {
                    Assert.IsFalse(b.Mast.HasValue, ctx);
                }
                else
                {
                    string[] m = f[14].Split(',');
                    Assert.IsTrue(b.Mast.HasValue, ctx);
                    Assert.AreEqual(Golden.D(m[0]), b.Mast.Value.X, Tol, ctx);
                    Assert.AreEqual(Golden.D(m[1]), b.Mast.Value.Z, Tol, ctx);
                    Assert.AreEqual(Golden.D(m[2]), b.Mast.Value.Y, Tol, ctx);
                    Assert.AreEqual(Golden.D(m[3]), b.Mast.Value.Height, Tol, ctx);
                }
            }
        }

        [Test]
        public void VolumesMatchPrototype()
        {
            List<string[]> rows = Golden.Rows(_golden, "V");
            Assert.AreEqual(rows.Count, _plan.Volumes.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                Volume v = _plan.Volumes[i];
                string[] f = rows[i];
                string ctx = $"volume {i}";
                Assert.AreEqual(Golden.I(f[1]), v.BuildingIndex, ctx);
                Assert.AreEqual(Golden.D(f[2]), v.Y0, Tol, ctx);
                Assert.AreEqual(Golden.D(f[3]), v.Y1, Tol, ctx);
                int n = Golden.I(f[4]);
                Assert.AreEqual(n, v.Poly.Length, ctx);
                for (int k = 0; k < n; k++)
                {
                    Assert.AreEqual(Golden.D(f[5 + k * 2]), v.Poly[k].X, Tol, ctx);
                    Assert.AreEqual(Golden.D(f[6 + k * 2]), v.Poly[k].Z, Tol, ctx);
                }
            }
        }

        [Test]
        public void SameSeedIsDeterministic()
        {
            CityPlan again = CityPlan.Generate(CityPlan.DefaultSeed);
            Assert.AreEqual(_plan.Volumes.Count, again.Volumes.Count);
            for (int i = 0; i < _plan.Buildings.Count; i++)
            {
                Assert.AreEqual(_plan.Buildings[i].Height, again.Buildings[i].Height);
                Assert.AreEqual(_plan.Buildings[i].Type, again.Buildings[i].Type);
            }
        }

        [Test]
        public void DifferentSeedChangesTheCity()
        {
            CityPlan other = CityPlan.Generate(7);
            bool differs = other.Buildings.Count != _plan.Buildings.Count
                || Math.Abs(other.Buildings[0].Height - _plan.Buildings[0].Height) > Tol;
            Assert.IsTrue(differs);
        }

        [Test]
        public void PlazaBlockHasNoLots()
        {
            foreach (Lot l in _plan.Lots)
            {
                Assert.IsFalse(l.Gx == CityLayout.PlazaIndex && l.Gz == CityLayout.PlazaIndex);
            }
            Assert.IsFalse(_plan.IsBlocked(0, 0));
        }

        [Test]
        public void EveryBuildingStaysWithinItsBlockAndLimits()
        {
            foreach (Building b in _plan.Buildings)
            {
                Assert.GreaterOrEqual(b.Floors, 1);
                Assert.LessOrEqual(b.Volumes.Count, 12);
                Assert.IsNotEmpty(b.Volumes);
                foreach (Volume v in b.Volumes)
                {
                    Assert.Greater(v.Y1, v.Y0);
                    Assert.GreaterOrEqual(v.Y0, 0);
                }
                double cx = CityLayout.CellCentre(b.Lot.Gx), cz = CityLayout.CellCentre(b.Lot.Gz);
                Assert.LessOrEqual(Math.Abs(b.X - cx), CityLayout.Block / 2);
                Assert.LessOrEqual(Math.Abs(b.Z - cz), CityLayout.Block / 2);
            }
        }

        [Test]
        public void IsBlockedIsTrueInsideASolidLotAndFalseInVacantOnes()
        {
            Lot solid = _plan.Lots.Find(l => !l.Vacant);
            Assert.IsTrue(_plan.IsBlocked(solid.Rect.CentreX, solid.Rect.CentreZ));
            Lot vacant = _plan.Lots.Find(l => l.Vacant);
            Assert.IsNotNull(vacant, "seed should contain at least one vacant lot");
            Assert.IsNull(vacant.Building);
            Assert.IsFalse(_plan.IsBlocked(vacant.Rect.CentreX, vacant.Rect.CentreZ));
        }
    }
}
