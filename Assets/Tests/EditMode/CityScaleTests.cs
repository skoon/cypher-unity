using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class CityScaleTests
    {
        static readonly CitySlice Whole = CityChunker.Build(CityPlan.Generate(), CityWindow.AroundPlaza(3));

        [Test]
        public void TheWholeCityIsFortyNineChunksAndStaysUnderTheTriangleBudget()
        {
            Assert.AreEqual(49, Whole.Chunks.Count);
            Assert.That(Whole.TriangleCount, Is.LessThan(120000), "the 7x7 city with signage was 71 k triangles when measured");
        }

        [Test]
        public void EveryCellButThePlazaHasBuildings()
        {
            Assert.AreEqual(48, Whole.Chunks.Count(c => c.BuildingCount > 0));
            Assert.IsTrue(Whole.Chunks.Where(c => c.BuildingCount == 0).All(c => c.Gx == CityLayout.PlazaIndex && c.Gz == CityLayout.PlazaIndex));
        }

        [Test]
        public void ColliderCountIsModest()
        {
            Assert.That(Whole.Chunks.Sum(c => c.Colliders.Count), Is.LessThan(1200), "one box per building volume plus one per deck");
        }
    }
}
