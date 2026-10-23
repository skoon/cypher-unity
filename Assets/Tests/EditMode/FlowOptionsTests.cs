using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class FlowOptionsTests
    {
        [Test]
        public void NoFlagsMeansANormalLaunch()
        {
            FlowOptions o = FlowOptions.Parse(new[] { "Cypher.exe", "-screen-width", "1920" });
            Assert.IsFalse(o.SkipTitle);
            Assert.IsFalse(o.IgnoreUserSettings);
            Assert.IsFalse(o.NoAutoPause);
            Assert.IsFalse(o.NoCrowd);
        }

        [Test]
        public void TheNoCrowdFlagSetsOnlyItsOwnOptionAndABenchmarkDoesNotImplyIt()
        {
            FlowOptions o = FlowOptions.Parse(new[] { "--no-crowd" });
            Assert.IsTrue(o.NoCrowd);
            Assert.IsFalse(o.SkipTitle);
            Assert.IsFalse(o.IgnoreUserSettings);
            Assert.IsFalse(o.NoAutoPause);
            Assert.IsFalse(FlowOptions.Parse(new[] { "--benchmark" }).NoCrowd, "a benchmark has the people unless it is told otherwise");
            Assert.IsTrue(FlowOptions.Parse(new[] { "--benchmark", "--no-crowd" }).NoCrowd);
        }

        [Test]
        public void TheNoTrafficFlagSetsOnlyItsOwnOptionAndABenchmarkDoesNotImplyIt()
        {
            Assert.IsFalse(FlowOptions.Parse(new[] { "Cypher.exe" }).NoTraffic);
            FlowOptions o = FlowOptions.Parse(new[] { "--no-traffic" });
            Assert.IsTrue(o.NoTraffic);
            Assert.IsFalse(o.NoCrowd);
            Assert.IsFalse(o.SkipTitle);
            Assert.IsFalse(o.IgnoreUserSettings);
            Assert.IsFalse(o.NoAutoPause);
            Assert.IsFalse(FlowOptions.Parse(new[] { "--no-crowd" }).NoTraffic, "the two controls are independent");
            Assert.IsFalse(FlowOptions.Parse(new[] { "--benchmark" }).NoTraffic, "a benchmark has the traffic unless it is told otherwise");
            Assert.IsTrue(FlowOptions.Parse(new[] { "--benchmark", "--no-traffic", "--no-crowd" }).NoTraffic);
        }

        [Test]
        public void TheNoVehicleLightsFlagSetsOnlyItsOwnOption()
        {
            Assert.IsFalse(FlowOptions.Parse(new[] { "Cypher.exe" }).NoVehicleLights);
            FlowOptions o = FlowOptions.Parse(new[] { "--no-vehicle-lights" });
            Assert.IsTrue(o.NoVehicleLights);
            Assert.IsFalse(o.NoTraffic, "the traffic itself stays");
            Assert.IsFalse(o.NoCrowd);
            Assert.IsFalse(o.SkipTitle);
            Assert.IsFalse(FlowOptions.Parse(new[] { "--benchmark" }).NoVehicleLights);
            Assert.IsTrue(FlowOptions.Parse(new[] { "--benchmark", "--no-vehicle-lights" }).NoVehicleLights);
        }

        [Test]
        public void TheNoAudioFlagSetsOnlyItsOwnOptionAndABenchmarkDoesNotImplyIt()
        {
            Assert.IsFalse(FlowOptions.Parse(new[] { "Cypher.exe" }).NoAudio);
            FlowOptions o = FlowOptions.Parse(new[] { "--no-audio" });
            Assert.IsTrue(o.NoAudio);
            Assert.IsFalse(o.NoTraffic);
            Assert.IsFalse(o.NoCrowd);
            Assert.IsFalse(o.SkipTitle);
            Assert.IsFalse(o.IgnoreUserSettings);
            Assert.IsFalse(FlowOptions.Parse(new[] { "--benchmark" }).NoAudio, "a benchmark has its sound unless it is told otherwise");
            Assert.IsTrue(FlowOptions.Parse(new[] { "--benchmark", "--no-audio" }).NoAudio);
            Assert.IsFalse(FlowOptions.Parse(new[] { "--no-traffic" }).NoAudio);
        }

        [Test]
        public void EachFlagSetsOnlyItsOwnOption()
        {
            FlowOptions a = FlowOptions.Parse(new[] { "--skip-title" });
            Assert.IsTrue(a.SkipTitle);
            Assert.IsFalse(a.IgnoreUserSettings);
            Assert.IsFalse(a.NoAutoPause);

            FlowOptions b = FlowOptions.Parse(new[] { "--default-settings" });
            Assert.IsFalse(b.SkipTitle);
            Assert.IsTrue(b.IgnoreUserSettings);

            FlowOptions c = FlowOptions.Parse(new[] { "--no-auto-pause" });
            Assert.IsFalse(c.SkipTitle);
            Assert.IsTrue(c.NoAutoPause);
        }

        [Test]
        public void ABenchmarkImpliesAllThreeSoItNeverTouchesThePlayersSettings()
        {
            FlowOptions o = FlowOptions.Parse(new[] { "Cypher.exe", "--benchmark", "--benchmark-seconds", "30" });
            Assert.IsTrue(o.SkipTitle);
            Assert.IsTrue(o.IgnoreUserSettings);
            Assert.IsTrue(o.NoAutoPause);
        }

        [Test]
        public void TheScreenshotWalkIgnoresTheSettingsFileAndDoesNotPauseButStillShowsTheTitle()
        {
            FlowOptions o = FlowOptions.Parse(new[] { "Cypher.exe", "--shots", "D:/shots" });
            Assert.IsFalse(o.SkipTitle);
            Assert.IsTrue(o.IgnoreUserSettings);
            Assert.IsTrue(o.NoAutoPause);
        }

        [Test]
        public void ConstructorDefaultsAreAllOff()
        {
            var o = new FlowOptions();
            Assert.IsFalse(o.SkipTitle || o.IgnoreUserSettings || o.NoAutoPause);
        }

        [Test]
        public void NullArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => FlowOptions.Parse(null));
        }
    }
}
