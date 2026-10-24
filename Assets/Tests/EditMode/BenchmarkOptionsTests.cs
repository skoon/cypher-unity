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

        [TestCase("Performance", QualityPreset.Performance)]
        [TestCase("low", QualityPreset.Low)]
        [TestCase("MEDIUM", QualityPreset.Medium)]
        [TestCase("High", QualityPreset.High)]
        [TestCase("ultra", QualityPreset.Ultra)]
        public void QualityPresetsAreParsedIgnoringCase(string text, QualityPreset expected)
        {
            BenchmarkOptions o = BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-quality", text });
            Assert.AreEqual(expected, o.Quality);
        }

        [Test]
        public void QualityDefaultsToWhateverTheSceneStartsWith()
        {
            Assert.IsNull(BenchmarkOptions.Parse(new[] { "--benchmark" }).Quality);
        }

        [TestCase("Ludicrous")]
        [TestCase("2")]
        [TestCase("Low, High")]
        public void UnknownQualityPresetsFailLoudly(string text)
        {
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-quality", text }));
        }

        [Test]
        public void QualityWithoutTheBenchmarkFlagFailsLoudly()
        {
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark-quality", "Low" }));
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-quality" }));
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

        [Test]
        public void TheRouteDefaultsToTheCentreLoop()
        {
            Assert.AreEqual(BenchmarkRoute.Centre, BenchmarkOptions.Parse(new[] { "--benchmark" }).Route);
            Assert.AreEqual(BenchmarkRoute.Centre, BenchmarkOptions.Disabled.Route);
        }

        [TestCase("city", BenchmarkRoute.City)]
        [TestCase("CITY", BenchmarkRoute.City)]
        [TestCase("centre", BenchmarkRoute.Centre)]
        [TestCase("street", BenchmarkRoute.Street)]
        [TestCase("Junction", BenchmarkRoute.Junction)]
        public void TheRouteIsReadByName(string text, BenchmarkRoute expected)
        {
            Assert.AreEqual(expected, BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-route", text }).Route);
        }

        [Test]
        public void EachRouteRecordsAsLongAsItNeedsUnlessToldOtherwise()
        {
            Assert.AreEqual(160, BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-route", "street" }).DurationSeconds);
            Assert.AreEqual(90, BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-route", "junction" }).DurationSeconds);
            Assert.AreEqual(60, BenchmarkOptions.Parse(new[] { "--benchmark" }).DurationSeconds);
            Assert.AreEqual(45, BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-route", "street", "--benchmark-seconds", "45" }).DurationSeconds);
            Assert.AreEqual(BenchmarkOptions.DefaultStreetDurationSeconds, BenchmarkOptions.DefaultDuration(BenchmarkRoute.Street));
            Assert.AreEqual(BenchmarkOptions.DefaultDurationSeconds, BenchmarkOptions.DefaultDuration(BenchmarkRoute.Centre));
        }

        [Test]
        public void TheCityTourRecordsLongerUnlessToldOtherwise()
        {
            Assert.AreEqual(BenchmarkOptions.DefaultCityDurationSeconds, BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-route", "city" }).DurationSeconds);
            Assert.AreEqual(BenchmarkOptions.DefaultDurationSeconds, BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-route", "centre" }).DurationSeconds);
            Assert.AreEqual(90, BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-route", "city", "--benchmark-seconds", "90" }).DurationSeconds);
            Assert.AreEqual(90, BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-seconds", "90", "--benchmark-route", "city" }).DurationSeconds, "the order of the switches does not matter");
        }

        [Test]
        public void ABadRouteFailsLoudly()
        {
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-route", "everywhere" }));
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark", "--benchmark-route" }));
            Assert.Throws<ArgumentException>(() => BenchmarkOptions.Parse(new[] { "--benchmark-route", "city" }));
        }
    }
}
