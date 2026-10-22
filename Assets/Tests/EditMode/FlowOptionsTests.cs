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
