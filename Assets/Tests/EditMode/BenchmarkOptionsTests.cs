using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class BenchmarkOptionsTests
    {
        [Test]
        public void NoSwitchMeansDisabledEvenWithUnityArguments()
        {
            BenchmarkOptions o = BenchmarkOptions.Parse(new[] { "Cypher.exe", "-screen-width", "1920", "-screen-height", "1080" });
            Assert.IsFalse(o.Enabled);
        }

        [Test]
        public void BareFlagUsesTheDefaults()
        {
            BenchmarkOptions o = BenchmarkOptions.Parse(new[] { "Cypher.exe", "--benchmark" });
            Assert.IsTrue(o.Enabled);
            Assert.IsNull(o.OutputDirectory);
            Assert.AreEqual(BenchmarkOptions.DefaultWarmupSeconds, o.WarmupSeconds);
            Assert.AreEqual(BenchmarkOptions.DefaultDurationSeconds, o.DurationSeconds);
        }

        [Test]
        public void ValuesAreReadAmongUnityArguments()
        {
            BenchmarkOptions o = BenchmarkOptions.Parse(new[]
            {
                "Cypher.exe", "-screen-width", "1920", "--benchmark", "--benchmark-out", @"D:\runs\a b",
                "--benchmark-seconds", "12.5", "--benchmark-warmup", "0", "-popupwindow",
            });
            Assert.IsTrue(o.Enabled);
            Assert.AreEqual(@"D:\runs\a b", o.OutputDirectory);
            Assert.AreEqual(12.5, o.DurationSeconds);
            Assert.AreEqual(0.0, o.WarmupSeconds);
        }

        [Test]
        public void NumbersAreParsedWithTheInvariantCulture()
        {
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            try
            {
                BenchmarkOptions o = BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-seconds", "7.5" });
                Assert.AreEqual(7.5, o.DurationSeconds);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void ValueSwitchesWithoutTheBenchmarkFlagFailLoudly()
        {
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark-out", "x" }));
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark-seconds", "5" }));
        }

        [Test]
        public void MalformedSwitchesFailLoudly()
        {
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-out" }));
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-out", "--benchmark-seconds", "5" }));
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-seconds", "soon" }));
            Assert.Catch<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-seconds", "0" }));
            Assert.Catch<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-warmup", "-3" }));
            Assert.Throws<ArgumentNullException>(() => BenchmarkOptions.Parse(null));
        }
    }
}
