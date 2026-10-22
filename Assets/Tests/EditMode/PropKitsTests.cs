using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PropKitsTests
    {
        static readonly string[] Expected =
        {
            "bollard", "barrier", "guardrail", "streetlight", "signal", "meter", "shelter", "bench", "kiosk", "bin", "hydrant", "utilbox", "signpost",
            "dumpster", "bag", "cardbox", "crate", "pallet", "barrel", "cone", "debris",
            "vending", "stall", "atm", "campole", "camwall", "dronepad", "satdish", "acunit", "roofunit", "ventstack",
        };

        /// <summary>Kits whose origin is on a wall plane or a roof fitting, so they are not expected to stand on y = 0.</summary>
        static readonly string[] WallMounted = { "atm", "camwall", "acunit" };

        static (float x0, float x1, float y0, float y1, float z0, float z1) Bounds(MeshData m)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            for (int i = 0; i < m.VertexCount; i++)
            {
                x0 = Math.Min(x0, m.Positions[i * 3]); x1 = Math.Max(x1, m.Positions[i * 3]);
                y0 = Math.Min(y0, m.Positions[i * 3 + 1]); y1 = Math.Max(y1, m.Positions[i * 3 + 1]);
                z0 = Math.Min(z0, m.Positions[i * 3 + 2]); z1 = Math.Max(z1, m.Positions[i * 3 + 2]);
            }
            return (x0, x1, y0, y1, z0, z1);
        }

        [Test]
        public void TheLibraryHasTheThirtyOneKitsOfThePrototypeMinusTheKickableOnes()
        {
            CollectionAssert.AreEquivalent(Expected, PropKits.All.Keys);
            foreach (string name in Expected) Assert.AreEqual(name, PropKits.Get(name).Name);
        }

        [Test]
        public void EveryKitIsWellFormed()
        {
            foreach (PropKit k in PropKits.All.Values)
            {
                MeshData m = k.Geometry;
                Assert.Greater(m.TriangleCount, 0, k.Name);
                Assert.AreEqual(m.VertexCount * 3, m.Normals.Length, k.Name);
                Assert.AreEqual(m.VertexCount * 2, m.Uvs.Length, k.Name);
                Assert.AreEqual(m.VertexCount * 3, m.Colors.Length, k.Name);
                Assert.AreEqual(m.VertexCount * 4, m.Uv1.Length, k.Name);
                Assert.IsTrue(m.Indices.All(i => i >= 0 && i < m.VertexCount), k.Name);
                Assert.Greater(k.Len, 0, k.Name);
                Assert.Greater(k.Dep, 0, k.Name);
            }
        }

        [Test]
        public void EveryTriangleFacesOutward()
        {
            foreach (PropKit k in PropKits.All.Values)
            {
                MeshData m = k.Geometry;
                int wrong = 0;
                for (int t = 0; t < m.Indices.Length; t += 3)
                {
                    int a = m.Indices[t], b = m.Indices[t + 1], c = m.Indices[t + 2];
                    double ux = m.Positions[b * 3] - m.Positions[a * 3], uy = m.Positions[b * 3 + 1] - m.Positions[a * 3 + 1], uz = m.Positions[b * 3 + 2] - m.Positions[a * 3 + 2];
                    double vx = m.Positions[c * 3] - m.Positions[a * 3], vy = m.Positions[c * 3 + 1] - m.Positions[a * 3 + 1], vz = m.Positions[c * 3 + 2] - m.Positions[a * 3 + 2];
                    double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                    if (Math.Sqrt(cx * cx + cy * cy + cz * cz) < 1e-9) continue;
                    double nx = m.Normals[a * 3] + m.Normals[b * 3] + m.Normals[c * 3];
                    double ny = m.Normals[a * 3 + 1] + m.Normals[b * 3 + 1] + m.Normals[c * 3 + 1];
                    double nz = m.Normals[a * 3 + 2] + m.Normals[b * 3 + 2] + m.Normals[c * 3 + 2];
                    if (cx * nx + cy * ny + cz * nz <= 0) wrong++;
                }
                Assert.AreEqual(0, wrong, $"{k.Name} has triangles wound against their normals");
            }
        }

        [Test]
        public void EveryNormalIsAUnitVector()
        {
            foreach (PropKit k in PropKits.All.Values)
            {
                for (int i = 0; i < k.Geometry.VertexCount; i++)
                {
                    double x = k.Geometry.Normals[i * 3], y = k.Geometry.Normals[i * 3 + 1], z = k.Geometry.Normals[i * 3 + 2];
                    Assert.AreEqual(1.0, Math.Sqrt(x * x + y * y + z * z), 1e-4, $"{k.Name} vertex {i}");
                }
            }
        }

        [Test]
        public void KitsStandOnTheirBaseSoPlacingThemAtTheGroundPutsThemOnIt()
        {
            foreach (PropKit k in PropKits.All.Values.Where(k => !WallMounted.Contains(k.Name)))
            {
                var b = Bounds(k.Geometry);
                Assert.That(b.y0, Is.InRange(-0.03f, 0.2f), $"{k.Name} floats or sinks: its lowest point is {b.y0}");
                Assert.Greater(b.y1, 0.1f, k.Name);
            }
        }

        [Test]
        public void WallMountedKitsGrowFromTheWallPlaneTowardPlusZ()
        {
            foreach (string name in WallMounted)
            {
                var b = Bounds(PropKits.Get(name).Geometry);
                Assert.GreaterOrEqual(b.z0, -0.05f, $"{name} pokes behind the wall plane");
                Assert.Greater(b.z1, 0.25f, $"{name} should stand out from the wall");
            }
        }

        [Test]
        public void SurfaceValuesAreInRange()
        {
            foreach (PropKit k in PropKits.All.Values)
            {
                for (int i = 0; i < k.Geometry.VertexCount; i++)
                {
                    float emit = k.Geometry.Uv1[i * 4], rough = k.Geometry.Uv1[i * 4 + 1], metal = k.Geometry.Uv1[i * 4 + 2];
                    Assert.That(emit, Is.InRange(0f, 12f), $"{k.Name} emit");
                    Assert.That(rough, Is.InRange(0f, 1f), $"{k.Name} roughness");
                    Assert.That(metal, Is.InRange(0f, 1f), $"{k.Name} metalness");
                }
            }
        }

        [Test]
        public void TheGlowingKitsGlowAndTheDullOnesDoNot()
        {
            foreach (string glowing in new[] { "streetlight", "vending", "kiosk", "stall", "shelter", "atm", "signal", "bollard", "dronepad" })
            {
                Assert.Greater(PropKits.Get(glowing).Geometry.Uv1.Where((v, i) => i % 4 == 0).Max(), 2f, glowing);
            }
            foreach (string dull in new[] { "bag", "debris", "crate", "pallet", "hydrant", "bench" })
            {
                Assert.AreEqual(0f, PropKits.Get(dull).Geometry.Uv1.Where((v, i) => i % 4 == 0).Max(), dull);
            }
        }

        [Test]
        public void CollidersFitInsideTheirKitsAndStandOnTheGround()
        {
            var withColliders = PropKits.All.Values.Where(k => k.Collider.HasValue).ToList();
            Assert.GreaterOrEqual(withColliders.Count, 9);
            foreach (PropKit k in withColliders)
            {
                PropCollider c = k.Collider.Value;
                var b = Bounds(k.Geometry);
                Assert.Greater(c.Hx, 0, k.Name); Assert.Greater(c.Hy, 0, k.Name); Assert.Greater(c.Hz, 0, k.Name);
                Assert.GreaterOrEqual(c.Cx - c.Hx, b.x0 - 0.15, $"{k.Name} collider pokes out of the west");
                Assert.LessOrEqual(c.Cx + c.Hx, b.x1 + 0.15, $"{k.Name} collider pokes out of the east");
                Assert.GreaterOrEqual(c.Cy - c.Hy, b.y0 - 0.05, $"{k.Name} collider sinks below the base");
                Assert.LessOrEqual(c.Cy + c.Hy, b.y1 + 0.05, $"{k.Name} collider rises above the kit");
                Assert.GreaterOrEqual(c.Cz - c.Hz, b.z0 - 0.15, $"{k.Name} collider pokes out of the back");
                Assert.LessOrEqual(c.Cz + c.Hz, b.z1 + 0.15, $"{k.Name} collider pokes out of the front");
            }
        }

        [Test]
        public void TheKitsThatTheWalkerCanBumpIntoHaveColliders()
        {
            foreach (string solid in new[] { "barrier", "shelter", "bench", "kiosk", "bin", "utilbox", "dumpster", "vending", "stall" })
            {
                Assert.IsTrue(PropKits.Get(solid).Collider.HasValue, $"{solid} should stop the player");
            }
            foreach (string soft in new[] { "bag", "cardbox", "debris", "cone", "bollard", "hydrant", "streetlight", "signal", "meter", "atm", "acunit", "roofunit" })
            {
                Assert.IsFalse(PropKits.Get(soft).Collider.HasValue, $"{soft} has no collider in the prototype");
            }
        }

        [Test]
        public void KitFootprintsMatchThePrototype()
        {
            Assert.AreEqual((4.4, 1.35), (PropKits.Get("shelter").Len, PropKits.Get("shelter").Dep));
            Assert.AreEqual((2.25, 1.15), (PropKits.Get("dumpster").Len, PropKits.Get("dumpster").Dep));
            Assert.AreEqual((3.2, 1.35), (PropKits.Get("stall").Len, PropKits.Get("stall").Dep));
            Assert.AreEqual((0.4, 0.35), (PropKits.Get("bollard").Len, PropKits.Get("bollard").Dep));
        }

        [Test]
        public void ALotOfPropsStillIsASmallNumberOfTriangles()
        {
            int total = PropKits.All.Values.Sum(k => k.Geometry.TriangleCount);
            Assert.That(total, Is.LessThan(12000), "the whole library is a few thousand triangles");
            Assert.That(PropKits.Get("streetlight").Geometry.TriangleCount, Is.LessThan(250));
        }

        [Test]
        public void AnUnknownKitFailsLoudly()
        {
            Assert.Throws<ArgumentException>(() => PropKits.Get("flying-saucer"));
        }
    }
}
