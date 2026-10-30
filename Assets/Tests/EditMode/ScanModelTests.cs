using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The scanner: the charge eases at the prototype's rates, the nearest contacts in range and in view are tagged, and the places say what can be done there.</summary>
    public class ScanModelTests
    {
        [Test]
        public void TheChargeEasesInWhileHeldAndOutFasterWhenLetGo()
        {
            double c = 0;
            for (int i = 0; i < 60; i++) c = ScanModel.Charge(c, true, 1.0 / 60);
            Assert.AreEqual(1 - System.Math.Exp(-ScanModel.ChargeUp), c, 1e-9, "a second of holding");
            double up = ScanModel.Charge(0, true, 0.1), down = 1 - ScanModel.Charge(1, false, 0.1);
            Assert.Greater(down, up, "letting go is quicker than charging");
            Assert.AreEqual(1, ScanModel.Damp(0, 1, 1e9, 1), 1e-12, "a long step lands on the target and never past it");
        }

        [Test]
        public void TheNearestFewInRangeAndInViewAreTagged()
        {
            var contacts = new List<ScanContact>();
            for (int i = 0; i < 12; i++) contacts.Add(ScanModel.Person(i * 5 + 3, 0, 0));
            contacts.Add(ScanModel.Person(0.1, 0, 0));    // too close: the player's own spot
            contacts.Add(ScanModel.Person(200, 0, 0));    // out of range
            var picked = ScanModel.Pick(contacts, 0, 0.95, 0, c => c.X != 8);   // the one at 8 m is behind something
            Assert.AreEqual(ScanModel.MaxTags, picked.Count);
            CollectionAssert.AreEqual(new[] { 3.0, 13, 18, 23, 28, 33, 38 }, picked.Select(p => p.Contact.X));
            Assert.AreEqual(3, picked[0].Distance, 1e-9);
        }

        [Test]
        public void TheStateAndReadoutLinesFollowThePrototype()
        {
            Assert.AreEqual("STANDBY", ScanModel.State(false, 0.5, 3));
            Assert.AreEqual("ACQUIRING", ScanModel.State(true, 0.5, 3));
            Assert.AreEqual("3 CONTACTS", ScanModel.State(true, 0.95, 3));
            Assert.AreEqual("1 CONTACT", ScanModel.State(true, 0.95, 1));
            Assert.AreEqual("NO CONTACTS IN RANGE", ScanModel.Readout(new List<(ScanContact, double)>()));
            var found = new List<(ScanContact, double)> { (ScanModel.Person(0, 0, 0), 4.4), (ScanModel.Vehicle("taxi", 0, 0, 0), 12.6), (ScanModel.Person(0, 0, 0), 20), (ScanModel.Person(0, 0, 0), 30) };
            Assert.AreEqual("CIVILIAN · 4M   //   TAXI · 13M   //   CIVILIAN · 20M", ScanModel.Readout(found));
        }

        [Test]
        public void ThePlacesSayWhatCanBeDoneThere()
        {
            var core = new RpgCore(null, 20771, new MemorySaveStorage(), () => 1700000000000) { AutosaveEnabled = false };
            core.SetCity(RpgCityMeta.From(CityPlan.Generate(20771)));
            List<ScanContact> places = ScanModel.Places(core);
            Assert.AreEqual(core.GetLocationList().Count, places.Count);
            ScanContact noodle = places.Single(p => p.Name == core.GetLocation("noodle").Name.ToUpperInvariant());
            Assert.AreEqual("TALK TO MAMA KAZ", noodle.Action);
            Assert.IsTrue(places.All(p => p.Kind == ScanKind.Place && p.Sub.Length > 0));
            Assert.IsTrue(places.Any(p => p.Action.StartsWith("BREACH ")), "a breach target is marked");
        }
    }
}
