using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class CityLayoutTests
    {
        [Test]
        public void ConstantsMatchPrototypeContract()
        {
            Assert.AreEqual(62.0, CityLayout.Cell);
            Assert.AreEqual(217.0, CityLayout.Half);
            Assert.AreEqual(-225.0, CityLayout.Base);
            Assert.AreEqual(39.0, CityLayout.BlockOffset);
            Assert.AreEqual(3, CityLayout.PlazaIndex);
        }

        [Test]
        public void PlazaBlockIsCentredOnOrigin()
        {
            Assert.AreEqual(0.0, CityLayout.CellCentre(CityLayout.PlazaIndex));
            Assert.AreEqual(-186.0, CityLayout.CellCentre(0));
            Assert.AreEqual(186.0, CityLayout.CellCentre(CityLayout.Grid - 1));
        }

        [Test]
        public void BlockCentreIsOffRoadAndRoadCentreIsOnRoad()
        {
            Assert.IsFalse(CityLayout.IsOnRoad(0, 0));
            // Road corridor starts at Base and is Road wide; its middle is Base + Road/2.
            Assert.IsTrue(CityLayout.IsOnRoad(CityLayout.Base + CityLayout.Road / 2, 0));
            Assert.IsTrue(CityLayout.IsOnRoad(0, CityLayout.Base + CityLayout.Road / 2));
        }

        [Test]
        public void RoadEdgesAreInclusiveExclusive()
        {
            Assert.IsTrue(CityLayout.IsOnRoad(CityLayout.Base + CityLayout.Road - 0.001, 0));
            Assert.IsFalse(CityLayout.IsOnRoad(CityLayout.Base + CityLayout.Road, 0));
        }

        [Test]
        public void IsOnRoadHandlesNegativeCoordinates()
        {
            // Far outside the grid, on the repeating pattern: a block centre stays off-road.
            Assert.IsFalse(CityLayout.IsOnRoad(-186.0 - 62 * 10, -186.0 - 62 * 10));
            Assert.IsTrue(CityLayout.IsOnRoad(CityLayout.Base - 62 * 10 + 1, 0));
        }

        [Test]
        public void CellIndexMapsBlockCentresBackToGridIndices()
        {
            for (int i = 0; i < CityLayout.Grid; i++)
            {
                Assert.AreEqual(i, CityLayout.CellIndex(CityLayout.CellCentre(i)));
            }
            Assert.AreEqual(-1, CityLayout.CellIndex(CityLayout.Base - 1));
        }
    }
}
