using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class ShotsOptionsTests
    {
        [Test]
        public void NoSwitchMeansNoScreenshotWalk()
        {
            Assert.IsNull(ShotsOptions.Parse(new[] { "Cypher.exe", "-screen-width", "1920" }));
        }

        [Test]
        public void TheFolderFollowsTheSwitch()
        {
            ShotsOptions o = ShotsOptions.Parse(new[] { "Cypher.exe", "--shots", "D:/out dir", "-popupwindow" });
            Assert.AreEqual("D:/out dir", o.Directory);
        }

        [Test]
        public void TheSwitchWithoutAFolderFailsLoudly()
        {
            Assert.Throws<ArgumentException>(() => ShotsOptions.Parse(new[] { "--shots" }));
            Assert.Throws<ArgumentException>(() => ShotsOptions.Parse(new[] { "--shots", "-popupwindow" }));
        }

        [Test]
        public void BadArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => ShotsOptions.Parse(null));
            Assert.Throws<ArgumentException>(() => new ShotsOptions(""));
            Assert.Throws<ArgumentException>(() => new ShotsOptions("  "));
        }
    }
}
