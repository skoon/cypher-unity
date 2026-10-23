using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class AudioCostTests
    {
        [Test]
        public void TheAudioThreadsShareIsTheSumOfThePiecesAgainstTheLengthOfABlock()
        {
            var cost = new AudioCost(bedMs: 1.2, scoreMs: 0.4, carsMs: 0.3, masterMs: 0.1, blockMs: 21.333, mainAverageMs: 0.05, mainMaxMs: 0.4);
            Assert.AreEqual(2.0, cost.AudioThreadMs, 1e-12);
            Assert.AreEqual(2.0 / 21.333, cost.AudioThreadShare, 1e-12);
        }

        [Test]
        public void ABlockOfNoLengthHasNoShareRatherThanADivisionByZero()
        {
            Assert.AreEqual(0, new AudioCost(1, 1, 1, 1, 0, 0, 0).AudioThreadShare);
        }

        [Test]
        public void ItReadsAsOneLineWithTheNumbersInIt()
        {
            string text = new AudioCost(1.25, 0.5, 0.25, 0.125, 20, 0.0123, 0.9, 3.5).ToString();
            StringAssert.Contains("audio thread 2.13 ms of every 20.0 ms block (10.6%", text);
            StringAssert.Contains("bed 1.25", text);
            StringAssert.Contains("worst single block 3.50", text);
            StringAssert.Contains("main thread 0.012 ms a frame (worst 0.90)", text);
            Assert.IsFalse(text.Contains("\n"));
        }
    }
}
