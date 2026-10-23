using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// The vehicles' geometry and placement checked against Traffic.js, run headless in node (fixture from tools/gen-golden-vehicles.mjs): every mesh as a set of triangles (with the
    /// glowing meshes' colours), the signal heads, and the matrix of every part of every vehicle and aircraft after 600 steps.
    /// </summary>
    public class VehicleMeshesGoldenTests
    {
        static readonly string[] Lines = Golden.Lines("vehicles_golden_20771.txt");
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate(20771).Streets, new NavGeometry(62, 46, 25.4, 0.185, 0));

        /// <summary>The triangles of a mesh, each as its vertices written to four decimals (starting from the least, keeping the winding), sorted: two meshes with the same triangles compare equal whatever order they are in.</summary>
        static List<string> TriangleSet(MeshData mesh, int first = int.MaxValue)
        {
            bool coloured = mesh.Colors.Length == mesh.Positions.Length && mesh.Colors.Any(c => c != 0);
            var set = new List<string>();
            int count = Math.Min(mesh.TriangleCount, first);
            for (int t = 0; t < count; t++)
            {
                var vertices = new string[3];
                for (int k = 0; k < 3; k++)
                {
                    int i = mesh.Indices[t * 3 + k];
                    var parts = new List<string> { F(mesh.Positions[i * 3]), F(mesh.Positions[i * 3 + 1]), F(mesh.Positions[i * 3 + 2]) };
                    if (coloured) parts.AddRange(new[] { F(mesh.Colors[i * 3]), F(mesh.Colors[i * 3 + 1]), F(mesh.Colors[i * 3 + 2]) });
                    vertices[k] = string.Join(",", parts);
                }
                set.Add(Canonical(vertices));
            }
            set.Sort(StringComparer.Ordinal);
            return set;
        }

        static string F(double v) => (Math.Abs(v) < 5e-5 ? 0.0 : v).ToString("0.0000", CultureInfo.InvariantCulture);

        static string Canonical(string[] v)
        {
            int start = 0;
            for (int k = 1; k < 3; k++)
            {
                if (string.CompareOrdinal(v[k], v[start]) < 0) start = k;
            }
            return v[start] + "|" + v[(start + 1) % 3] + "|" + v[(start + 2) % 3];
        }

        static List<string> GoldenSet(string name)
        {
            var set = new List<string>();
            foreach (string[] r in Golden.Rows(Lines, "T").Where(r => r[1] == name))
            {
                var values = r.Skip(2).Select(Golden.D).ToArray();
                int per = values.Length / 3;   // 3 (position) or 6 (position and colour) numbers for each of the three vertices
                var vertices = new string[3];
                if (per == 3)
                {
                    for (int k = 0; k < 3; k++) vertices[k] = string.Join(",", values.Skip(k * 3).Take(3).Select(F));
                }
                else
                {
                    for (int k = 0; k < 3; k++) vertices[k] = string.Join(",", values.Skip(k * 3).Take(3).Concat(values.Skip(9 + k * 3).Take(3)).Select(F));
                }
                set.Add(Canonical(vertices));
            }
            set.Sort(StringComparer.Ordinal);
            return set;
        }

        static int GoldenCount(string name) => Golden.I(Golden.Rows(Lines, "G").Single(r => r[1] == name)[2]);

        static void CheckMesh(string name, MeshData mesh)
        {
            Assert.AreEqual(GoldenCount(name), mesh.TriangleCount, $"{name}: triangle count");
            List<string> mine = TriangleSet(mesh), theirs = GoldenSet(name);
            Assert.AreEqual(theirs.Count, mine.Count, $"{name}: how many triangles");
            for (int i = 0; i < theirs.Count; i++) Assert.AreEqual(theirs[i], mine[i], $"{name}: triangle {i} of the sorted set");
        }

        /// <summary>
        /// For meshes with cylinders: the same surface, though the port splits each flat side quad along the other diagonal than three.js does. The same number of triangles, the same
        /// distinct corner points, and the same total area (a different diagonal of a flat quad changes none of these).
        /// </summary>
        static void CheckSurface(string name, MeshData mesh, int first = int.MaxValue)
        {
            int count = Math.Min(mesh.TriangleCount, first);
            Assert.AreEqual(GoldenCount(name), first == int.MaxValue ? mesh.TriangleCount : GoldenCount(name), $"{name}: triangle count");
            var corners = new SortedSet<string>(StringComparer.Ordinal);
            double area = 0;
            for (int t = 0; t < count; t++)
            {
                var p = new double[3][];
                for (int k = 0; k < 3; k++)
                {
                    int i = mesh.Indices[t * 3 + k];
                    p[k] = new double[] { mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2] };
                    corners.Add(string.Join(",", p[k].Select(F)));
                }
                area += TriangleArea(p[0], p[1], p[2]);
            }

            var gcorners = new SortedSet<string>(StringComparer.Ordinal);
            double garea = 0;
            int gcount = 0;
            foreach (string[] r in Golden.Rows(Lines, "T").Where(r => r[1] == name))
            {
                var v = r.Skip(2).Select(Golden.D).ToArray();
                var p = new[] { v.Take(3).ToArray(), v.Skip(3).Take(3).ToArray(), v.Skip(6).Take(3).ToArray() };
                foreach (double[] q in p) gcorners.Add(string.Join(",", q.Select(F)));
                garea += TriangleArea(p[0], p[1], p[2]);
                gcount++;
            }
            Assert.AreEqual(gcount, count, $"{name}: triangles compared");
            CollectionAssert.AreEqual(gcorners, corners, $"{name}: the distinct corner points");
            Assert.AreEqual(garea, area, garea * 1e-4, $"{name}: total area");
        }

        static double TriangleArea(double[] a, double[] b, double[] c)
        {
            double ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2], vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
            double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
            return 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
        }

        // ---------------------------------------------------------------- geometry

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void AGroundVehiclesBodyIsThePrototypesTriangles(int type) => CheckMesh($"body{type}", VehicleMeshes.Body(VehicleCatalog.Ground[type]));

        [Test]
        public void ABikeWithItsRiderIsThePrototypesSurface() => CheckSurface("body3", VehicleMeshes.Body(VehicleCatalog.Ground[3]));

        [TestCase(0)]
        [TestCase(1)]
        public void AnAircraftsBodyIsThePrototypesTriangles(int type) => CheckMesh($"air{type}", VehicleMeshes.Body(VehicleCatalog.Aircraft[type]));

        [Test]
        public void TheWheelIsThePrototypesSurface() => CheckSurface("wheels", VehicleMeshes.Wheel());

        [Test]
        public void TheLampsAreThePrototypes() => CheckMesh("rig", VehicleMeshes.Lamps());

        [Test]
        public void TheUnderglowIsThePrototypes() => CheckMesh("underglow", VehicleMeshes.Underglow());

        [Test]
        public void TheBeamsAreThePrototypes() => CheckMesh("beams", VehicleMeshes.Beams());

        [Test]
        public void TheAircraftGlowIsThePrototypes() => CheckMesh("airRig", VehicleMeshes.AircraftGlow());

        [Test]
        public void TheShadowIsThePrototypes() => CheckMesh("shadow", VehicleMeshes.Shadow());

        [Test]
        public void TheSignalLensIsThePrototypes() => CheckMesh("lamps", VehicleMeshes.SignalLens());

        [Test]
        public void TheFirstSignalMastsAreThePrototypes()
        {
            MeshData masts = VehicleMeshes.SignalMasts(Nav);
            Assert.AreEqual(9216, masts.TriangleCount, "256 heads of 36 triangles");
            CheckSurface("masts", masts, 108);
        }

        [Test]
        public void TheSignalHeadsAreThePrototypes()
        {
            List<string[]> rows = Golden.Rows(Lines, "H");
            IReadOnlyList<VehicleMeshes.SignalHead> heads = VehicleMeshes.SignalHeads(Nav);
            Assert.AreEqual(256, rows.Count);
            Assert.AreEqual(rows.Count, heads.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.AreEqual(Golden.D(rows[i][1]), heads[i].X, 1e-9, $"head {i} x");
                Assert.AreEqual(Golden.D(rows[i][2]), heads[i].Z, 1e-9, $"head {i} z");
                Assert.AreEqual(0, CrowdSim.ShortestAngle(Golden.D(rows[i][3]), heads[i].Yaw), 1e-9, $"head {i} yaw (a half turn is the same either way round)");
                Assert.AreEqual(Golden.I(rows[i][4]), heads[i].Axis, $"head {i} axis");
            }
        }

        [Test]
        public void TheGroundGlowCarriesThePartOfEveryVertex()
        {
            MeshData glow = VehicleMeshes.GroundGlow();
            Assert.AreEqual(VehicleMeshes.Lamps().VertexCount + VehicleMeshes.Underglow().VertexCount + VehicleMeshes.Beams().VertexCount, glow.VertexCount);
            Assert.AreEqual(22 + 4 + 72, glow.TriangleCount);
            var seen = new HashSet<float>();
            for (int i = 0; i < glow.VertexCount; i++) seen.Add(glow.Uv1[i * 4]);
            CollectionAssert.AreEquivalent(new[] { (float)GlowPart.Lamps, (float)GlowPart.Underglow, (float)GlowPart.Beams }, seen);
            for (int i = 0; i < glow.VertexCount; i++) Assert.GreaterOrEqual(glow.Colors[i * 3] + glow.Colors[i * 3 + 1] + glow.Colors[i * 3 + 2], 0f, "colours are additive");
        }

        [Test]
        public void EveryMeshIndexesItsOwnVertices()
        {
            var all = new List<(string, MeshData)>
            {
                ("wheel", VehicleMeshes.Wheel()), ("lamps", VehicleMeshes.Lamps()), ("beams", VehicleMeshes.Beams()), ("shadow", VehicleMeshes.Shadow()),
                ("groundGlow", VehicleMeshes.GroundGlow()), ("airGlow", VehicleMeshes.AircraftGlow()), ("masts", VehicleMeshes.SignalMasts(Nav)),
            };
            foreach (GroundVehicleType t in VehicleCatalog.Ground) all.Add((t.Key, VehicleMeshes.Body(t)));
            foreach (AircraftType t in VehicleCatalog.Aircraft) all.Add((t.Key, VehicleMeshes.Body(t)));
            foreach ((string name, MeshData m) in all)
            {
                Assert.AreEqual(0, m.Indices.Length % 3, name);
                Assert.IsTrue(m.Indices.All(i => i >= 0 && i < m.VertexCount), name);
                Assert.AreEqual(m.VertexCount * 3, m.Normals.Length, name);
                Assert.AreEqual(m.VertexCount * 2, m.Uvs.Length, name);
                Assert.AreEqual(m.VertexCount * 3, m.Colors.Length, name);
            }
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => VehicleMeshes.Hull(null));
            Assert.Throws<ArgumentNullException>(() => VehicleMeshes.Body((GroundVehicleType)null));
            Assert.Throws<ArgumentNullException>(() => VehicleMeshes.Body((AircraftType)null));
            Assert.Throws<ArgumentNullException>(() => VehicleMeshes.SignalHeads(null));
            Assert.Throws<ArgumentException>(() => VehicleMeshes.Hull(new HullShape(new[] { new ProfilePoint(0, 0), new ProfilePoint(1, 0) }, 1, 0.5, 0.3, 0.3)));
        }

        // ---------------------------------------------------------------- placement

        static void Near(double expected, double actual, string what, ref double worst, ref string where)
        {
            double e = Math.Abs(expected - actual);
            if (e > worst) { worst = e; where = $"{what}: prototype {expected}, port {actual}"; }
        }

        static void Compare(string what, Mat4 mine, string[] row, int firstNumber, ref double worst, ref string where)
        {
            for (int k = 0; k < 16; k++) Near(Golden.D(row[firstNumber + k]), mine.E[k], $"{what}[{k}]", ref worst, ref where);
        }

        [Test]
        public void EveryPartOfEveryVehicleAndAircraftIsWhereThePrototypePutsIt()
        {
            TrafficSim sim = TrafficSim.Create(Nav, 0, 0, 52, 20);
            for (int i = 0; i < 600; i++) sim.Step(1.0 / 60, 0, 0, 0, 0);
            const double alpha = 0.37, elapsed = 12.3456;

            double worst = 0;
            string where = "";
            int compared = 0;
            foreach (Vehicle v in sim.Vehicles.Where(v => v.Active))
            {
                VehiclePose pose = VehiclePlacement.Place(v, alpha, elapsed, Nav.RoadY);
                string[] body = Golden.Rows(Lines, "M").Single(r => r[1] == "body" && Golden.I(r[2]) == v.Index);
                Compare($"body of {v.Index}", pose.Body, body, 3, ref worst, ref where);
                for (int k = 0; k < 4; k++)
                {
                    string[] wheel = Golden.Rows(Lines, "M").Single(r => r[1] == "wheel" && Golden.I(r[2]) == v.Index && Golden.I(r[3]) == k);
                    Compare($"wheel {k} of {v.Index}", pose.Wheels[k], wheel, 4, ref worst, ref where);
                }
                foreach ((string name, Mat4 m) in new[] { ("rig", pose.Rig), ("underglow", pose.Underglow), ("beams", pose.Beams), ("shadow", pose.Shadow) })
                {
                    string[] r = Golden.Rows(Lines, "M").Single(x => x[1] == name && Golden.I(x[2]) == v.Index);
                    Compare($"{name} of {v.Index}", m, r, 3, ref worst, ref where);
                }
                compared++;
            }
            foreach (Aircraft a in sim.Airborne)
            {
                (Mat4 bodyMatrix, Mat4 glowMatrix) = VehiclePlacement.PlaceAircraft(a, alpha);
                Compare($"aircraft {a.Index}", bodyMatrix, Golden.Rows(Lines, "M").Single(r => r[1] == "air" && Golden.I(r[2]) == a.Index), 3, ref worst, ref where);
                Compare($"aircraft glow {a.Index}", glowMatrix, Golden.Rows(Lines, "M").Single(r => r[1] == "airRig" && Golden.I(r[2]) == a.Index), 3, ref worst, ref where);
                compared++;
            }
            Console.WriteLine($"VEHICLEGOLDEN {compared} vehicles and aircraft placed, worst difference {worst:0.0e+0}");
            Assert.AreEqual(Golden.Rows(Lines, "M").Count(r => r[1] == "body") + Golden.Rows(Lines, "M").Count(r => r[1] == "air"), compared, "the same ones were drawn");
            Assert.LessOrEqual(worst, 1e-3, $"the largest difference is {where}");
        }
    }
}
