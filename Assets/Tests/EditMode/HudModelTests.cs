using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The HUD's arithmetic: compass bearings, the minimap's turn, labels, the job tracker's steps and the notices.</summary>
    public class HudModelTests
    {
        [Test]
        public void BearingsRunClockwiseFromNorthWhichIsAhead()
        {
            Assert.AreEqual(0, HudModel.Bearing(0), 1e-9);
            Assert.AreEqual(90, HudModel.Bearing(Math.PI / 2), 1e-9);
            Assert.AreEqual(270, HudModel.Bearing(-Math.PI / 2), 1e-9);
            Assert.AreEqual(10, HudModel.Bearing(Math.PI * 2 + 10 * Math.PI / 180), 1e-9);
            Assert.AreEqual(0, HudModel.BearingTo(0, 0, 0, 10), 1e-9, "straight ahead of a new game is north");
            Assert.AreEqual(90, HudModel.BearingTo(0, 0, 10, 0), 1e-9);
            Assert.AreEqual(180, HudModel.BearingTo(5, 5, 5, -5), 1e-9);
        }

        [Test]
        public void RelativeBearingsTakeTheShortWayRound()
        {
            Assert.AreEqual(20, HudModel.Relative(30, 10), 1e-9);
            Assert.AreEqual(-20, HudModel.Relative(350, 10), 1e-9);
            Assert.AreEqual(20, HudModel.Relative(10, 350), 1e-9);
            Assert.AreEqual(180, Math.Abs(HudModel.Relative(180, 0)), 1e-9);
        }

        [Test]
        public void TheMinimapTurnsSoWhatIsAheadIsUp()
        {
            (double r, double a) = HudModel.OnMap(0, 10, 0);
            Assert.AreEqual(0, r, 1e-9);
            Assert.AreEqual(10, a, 1e-9);
            (r, a) = HudModel.OnMap(10, 0, Math.PI / 2);   // facing +X, a point at +X is ahead
            Assert.AreEqual(0, r, 1e-9);
            Assert.AreEqual(10, a, 1e-9);
            (r, a) = HudModel.OnMap(10, 0, 0);   // facing +Z, a point at +X is to the right
            Assert.AreEqual(10, r, 1e-9);
            Assert.AreEqual(0, a, 1e-9);
            (r, a) = HudModel.OnMap(3, -7, 1.234);
            Assert.AreEqual(Math.Sqrt(9 + 49), Math.Sqrt(r * r + a * a), 1e-9, "turning keeps distances");
        }

        [Test]
        public void LabelsAreWrittenAsThePrototypeWritesThem()
        {
            Assert.AreEqual("0M", HudModel.Distance(0.2));
            Assert.AreEqual("43M", HudModel.Distance(42.6));
            Assert.AreEqual("999M", HudModel.Distance(999.4));
            Assert.AreEqual("1.2KM", HudModel.Distance(1234));
            Assert.AreEqual("1,250 €$", HudModel.Money(1250));
            Assert.AreEqual("0 €$", HudModel.Money(0));
            Assert.AreEqual("MAIN JOB", HudModel.JobType("main"));
            Assert.AreEqual("GIG", HudModel.JobType("gig"));
        }

        [Test]
        public void TheDistanceKeyChangesWheneverTheTextDoes()
        {
            long lastKey = HudModel.DistanceKey(0);
            string lastText = HudModel.Distance(0);
            for (double m = 0; m < 25000; m += 0.0173)
            {
                long key = HudModel.DistanceKey(m);
                string text = HudModel.Distance(m);
                if (key == lastKey) Assert.AreEqual(lastText, text, $"{m} m: the same key must mean the same label");
                lastKey = key;
                lastText = text;
            }
        }

        [Test]
        public void ExpiredNoticesLeaveAndTheRestKeepTheirOrder()
        {
            var toasts = new HudToasts();
            toasts.Push("xp", "A", null, 0);
            toasts.Push("xp", "B", null, 2);
            toasts.Push("xp", "C", null, 4);
            CollectionAssert.AreEqual(new[] { "C", "B", "A" }, toasts.Visible(4).Select(t => t.Text));
            CollectionAssert.AreEqual(new[] { "C", "B" }, toasts.Visible(HudToasts.LifetimeSeconds + 0.1).Select(t => t.Text));
            CollectionAssert.AreEqual(new[] { "C" }, toasts.Visible(HudToasts.LifetimeSeconds + 2.1).Select(t => t.Text));
        }

        static JsonObject Objective(string id, bool done = false, bool optional = false, bool visible = true, double? x = null, double? z = null)
        {
            var o = new JsonObject { ["id"] = id, ["text"] = "do " + id, ["done"] = done, ["optional"] = optional, ["visible"] = visible };
            o["location"] = x.HasValue ? new JsonObject { ["x"] = x.Value, ["z"] = z.Value } : null;
            return o;
        }

        [Test]
        public void TheTrackerListsOpenVisibleStepsRequiredFirstWithDistances()
        {
            var quest = new JsonObject
            {
                ["objectives"] = new List<object>
                {
                    Objective("opt", optional: true, x: 0, z: 10), Objective("done", done: true), Objective("hidden", visible: false),
                    Objective("a", x: 3, z: 4), Objective("b"), Objective("c"), Objective("d"),
                },
            };
            List<TrackerStep> steps = HudModel.Steps(quest, 0, 0);
            CollectionAssert.AreEqual(new[] { "do a", "do b", "do c", "do d" }, steps.Select(s => s.Text), "required first, four at most");
            Assert.IsTrue(steps[0].Active);
            Assert.IsFalse(steps[1].Active);
            Assert.AreEqual(5, steps[0].Distance.Value, 1e-9);
            Assert.IsNull(steps[1].Distance);
            Assert.IsEmpty(HudModel.Steps(null, 0, 0));
            List<TrackerStep> withOptional = HudModel.Steps(new JsonObject { ["objectives"] = new List<object> { Objective("a"), Objective("opt", optional: true) } }, 0, 0);
            Assert.IsTrue(withOptional[1].Optional);
            Assert.IsFalse(withOptional[1].Active);
        }

        [Test]
        public void NoticesExpireCapAtAFewAndDoNotRepeatTheNewest()
        {
            var t = new HudToasts();
            t.Push("xp", "+100 XP", null, 0);
            t.Push("xp", "+100 XP", null, 1);
            Assert.AreEqual(1, t.Visible(1).Count, "a repeat replaces the newest");
            for (int i = 0; i < 8; i++) t.Push("item", "item " + i, null, 2);
            Assert.AreEqual(HudToasts.MaxVisible, t.Visible(2).Count);
            Assert.AreEqual("item 7", t.Visible(2)[0].Text, "newest first");
            Assert.IsEmpty(t.Visible(2 + HudToasts.LifetimeSeconds));
            t.Push("x", "", null, 0);
            Assert.IsEmpty(t.Visible(0));
        }

        [Test]
        public void TheWaypointFollowsTheTrackedJobsMarker()
        {
            var markers = new[] { new JsonObject { ["tracked"] = false, ["key"] = "a" }, new JsonObject { ["tracked"] = true, ["key"] = "b" } };
            Assert.AreEqual("b", HudModel.TrackedMarker(markers)["key"]);
            Assert.IsNull(HudModel.TrackedMarker(new JsonObject[0]));
        }
    
        [Test]
        public void TheCrosshairOpensWithEachShotAndFollowsTheSpread()
        {
            var r = new Reticle();
            for (int i = 0; i < 60; i++) r.Update(1.0 / 60, 0, false);
            Assert.AreEqual(5, r.Gap, 0.01, "at rest, five pixels out");
            double px = Reticle.SpreadPixels(0.02, 70, 1080);
            Assert.AreEqual(Math.Tan(0.02) * 540 / Math.Tan(35 * Math.PI / 180), px, 1e-9);
            for (int i = 0; i < 60; i++) r.Update(1.0 / 60, px, false);
            Assert.AreEqual(5 + px, r.Gap, 0.01);
            r.Kick();
            r.Update(1.0 / 60, px, false);
            Assert.Greater(r.Gap, 5 + px, "a shot blooms it open");
            for (int i = 0; i < 20; i++) r.Kick();
            Assert.AreEqual(2.4, r.Bloom, "the bloom is capped");
            for (int i = 0; i < 120; i++) r.Update(1.0 / 60, 0, true);
            Assert.AreEqual(29, r.Gap, 0.05, "reloading opens it 24 pixels");
            for (int i = 0; i < 20; i++) r.Kick();
            for (int i = 0; i < 30; i++) r.Update(1.0 / 60, 200, false);
            Assert.AreEqual(90, r.DrawnGap, "drawn no wider than 90");
        }
}
}
