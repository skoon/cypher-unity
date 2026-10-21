using System.Globalization;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PerfOverlayTextTests
    {
        static PerfStats Steady(double fps, int frames = 300)
        {
            var s = new PerfStats(5.0);
            for (int i = 0; i < frames; i++) s.AddFrame(1.0 / fps);
            return s;
        }

        [Test]
        public void EmptyStatsShowPlaceholders()
        {
            Assert.AreEqual("-- FPS\nframe -- ms\ndraws 412  tris 13,498", PerfOverlayText.Format(new PerfStats(), 412, 13498));
        }

        [Test]
        public void SteadySixtyShowsOkAndTheFrameTimes()
        {
            string[] lines = PerfOverlayText.Format(Steady(60), 412, 13498).Split('\n');
            Assert.AreEqual(3, lines.Length);
            Assert.AreEqual("60.0 FPS  OK", lines[0]);
            Assert.AreEqual("frame 16.7 avg  16.7 worst  16.7 p99 ms", lines[1]);
            Assert.AreEqual("draws 412  tris 13,498", lines[2]);
        }

        [Test]
        public void StutterShowsBelowThirtyEvenWithAHighMean()
        {
            var s = new PerfStats(10.0);
            for (int i = 0; i < 97; i++) s.AddFrame(0.010);
            for (int i = 0; i < 3; i++) s.AddFrame(0.100);
            string text = PerfOverlayText.Format(s, 0, 0);
            StringAssert.Contains("BELOW 30", text);
            StringAssert.Contains("100.0 worst  100.0 p99", text);
        }

        [Test]
        public void MissingCountersReadNotAvailable()
        {
            StringAssert.EndsWith("draws n/a  tris n/a", PerfOverlayText.Format(Steady(60), -1, -1));
            StringAssert.EndsWith("draws 0  tris n/a", PerfOverlayText.Format(Steady(60), 0, -1));
        }

        [Test]
        public void FormattingIsCultureInvariant()
        {
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                string text = PerfOverlayText.Format(Steady(60), 1234, 1234567);
                StringAssert.StartsWith("60.0 FPS", text);
                StringAssert.EndsWith("draws 1,234  tris 1,234,567", text);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
