using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class RngTests
    {
        [Test]
        public void MatchesPrototypeForEverySeedInFixture()
        {
            string[] lines = Golden.Lines("rng_golden.txt");
            int checkedSeeds = 0;
            foreach (string line in lines)
            {
                if (line.StartsWith("#") || line.Length == 0) continue;
                string[] f = line.Split(' ');
                var rng = new Rng(uint.Parse(f[0]));
                for (int i = 1; i < f.Length; i++)
                {
                    Assert.AreEqual(Golden.D(f[i]), rng.Next(), 0.0, $"seed {f[0]} value {i - 1}");
                }
                checkedSeeds++;
            }
            Assert.AreEqual(4, checkedSeeds);
        }

        [Test]
        public void SameSeedGivesSameSequence()
        {
            var a = new Rng(99);
            var b = new Rng(99);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.Next(), b.Next());
        }

        [Test]
        public void DifferentSeedsDiverge()
        {
            Assert.AreNotEqual(new Rng(1).Next(), new Rng(2).Next());
        }

        [Test]
        public void ValuesStayInUnitInterval()
        {
            var rng = new Rng(7);
            for (int i = 0; i < 10000; i++)
            {
                double v = rng.Next();
                Assert.GreaterOrEqual(v, 0.0);
                Assert.Less(v, 1.0);
            }
        }
    }
}
