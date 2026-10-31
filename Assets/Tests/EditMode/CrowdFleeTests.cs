using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>People and a gunfight: who hears it, running away along the pavements, the run ending, and the recycled starting calm.</summary>
    public class CrowdFleeTests
    {
        const double Dt = 1.0 / 60;
        static readonly CityNav Nav = CityNav.Build(CityPlan.Generate());

        static CrowdSim Make() => CrowdSim.Create(Nav, 120, 120, 0, 0, CrowdSim.DefaultSeed, CrowdRules.City);

        static double From(Pedestrian p, double x, double z) => Math.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));

        static void Run(CrowdSim sim, double seconds)
        {
            for (double t = 0; t < seconds - 1e-9; t += Dt) sim.Step(Dt, 0, 0, 500, 500);   // the player far off, out of everyone's way
        }

        /// <summary>Somewhere with people round it: the person nearest the origin.</summary>
        static (double X, double Z) Busy(CrowdSim sim)
        {
            Pedestrian p = sim.Active.OrderBy(a => a.X * a.X + a.Z * a.Z).First();
            return (p.X, p.Z);
        }

        [Test]
        public void OnlyThoseInEarshotRun()
        {
            CrowdSim sim = Make();
            Run(sim, 1);
            (double x, double z) = Busy(sim);
            const double reach = 30;
            int heard = sim.Alarm(x, z, reach);
            Assert.Greater(heard, 0);
            foreach (Pedestrian p in sim.Active)
            {
                Assert.AreEqual(From(p, x, z) <= reach, p.Fleeing, $"person {p.Index} at {From(p, x, z):0.0} m");
                if (p.Fleeing)
                {
                    Assert.That(p.FleeTimer, Is.InRange(CrowdSim.FleeSeconds * 0.8, CrowdSim.FleeSeconds * 1.2));
                    Assert.AreEqual(x, p.FleeX);
                    Assert.AreEqual(z, p.FleeZ);
                }
            }
            Assert.AreEqual(heard, sim.Active.Count(p => p.Fleeing));
            Assert.Throws<ArgumentOutOfRangeException>(() => sim.Alarm(0, 0, 0));
        }

        [Test]
        public void TheyRunAwayFasterThanTheyWalk()
        {
            CrowdSim sim = Make();
            Run(sim, 1);
            (double x, double z) = Busy(sim);
            sim.Alarm(x, z, 25);
            List<Pedestrian> running = sim.Active.Where(p => p.Fleeing).ToList();
            Dictionary<Pedestrian, double> before = running.ToDictionary(p => p, p => From(p, x, z));
            Run(sim, 4);
            List<Pedestrian> stillHere = running.Where(p => sim.Active.Contains(p)).ToList();
            Assert.Greater(stillHere.Count, 3);
            double gained = stillHere.Average(p => From(p, x, z) - before[p]);
            Assert.Greater(gained, 6, "on average well away from the shots in four seconds");
            Assert.Greater(stillHere.Count(p => From(p, x, z) > before[p]), stillHere.Count * 0.8, "nearly all of them further off");
            Assert.Greater(stillHere.Average(p => p.Speed / p.Cruise), 1.9, "running, not walking");
        }

        [Test]
        public void TheRunEndsAndTheyHurryOnThenWalk()
        {
            CrowdSim sim = Make();
            Run(sim, 1);
            (double x, double z) = Busy(sim);
            sim.Alarm(x, z, 25);
            List<Pedestrian> running = sim.Active.Where(p => p.Fleeing).ToList();
            Run(sim, CrowdSim.FleeSeconds * 1.2 + 0.1);
            Assert.IsFalse(sim.Active.Any(p => p.Fleeing), "nobody runs for ever");
            Assert.IsTrue(running.Where(p => sim.Active.Contains(p)).All(p => p.State == PedState.Hurry || p.Timer < 0 || p.State == PedState.Walk));
            Run(sim, 2);
            Assert.Less(sim.Active.Average(p => p.Speed / p.Cruise), 1.9, "back to the street's pace");
        }

        [Test]
        public void AnotherShotRunsThemOnFromTheLatest()
        {
            CrowdSim sim = Make();
            Run(sim, 1);
            (double x, double z) = Busy(sim);
            sim.Alarm(x, z, 25);
            Run(sim, 3);
            Pedestrian p = sim.Active.First(a => a.Fleeing);
            Assert.Less(p.FleeTimer, CrowdSim.FleeSeconds * 1.2 - 3 + 1e-6, "three seconds in");
            sim.Alarm(p.X + 5, p.Z, 10);
            Assert.That(p.FleeTimer, Is.InRange(CrowdSim.FleeSeconds * 0.8, CrowdSim.FleeSeconds * 1.2), "a whole run again");
            Assert.AreEqual(p.X + 5, p.FleeX, 1e-9);
        }

        [Test]
        public void SomeoneWalkingAtTheShotsTurnsBack()
        {
            CrowdSim sim = Make();
            Run(sim, 2);
            // a shot just ahead of someone walking
            Pedestrian p = sim.Active.First(a => a.PrevNode >= 0 && a.Speed > 0.5 && Math.Sqrt((a.Tx - a.X) * (a.Tx - a.X) + (a.Tz - a.Z) * (a.Tz - a.Z)) > 3);
            double hx = p.Tx - p.X, hz = p.Tz - p.Z, hl = Math.Sqrt(hx * hx + hz * hz);
            double sx = p.X + hx / hl * 3, sz = p.Z + hz / hl * 3;
            int node = p.Node, prev = p.PrevNode;
            sim.Alarm(sx, sz, 10);
            Assert.AreEqual(prev, p.Node, "heading back to where they came from");
            Assert.AreEqual(node, p.PrevNode);
            Assert.Less((p.Tx - p.X) * (sx - p.X) + (p.Tz - p.Z) * (sz - p.Z), 0, "away from the shot");
        }

        [Test]
        public void NobodyIsMadeAfraid()
        {
            CrowdSim sim = Make();
            Run(sim, 1);
            sim.Alarm(Busy(sim).X, Busy(sim).Z, 40);
            Run(sim, 0.5);
            sim.TargetCount = 0;
            // the recycled come back calm: walk everyone out of range of the camera, then look at those who came in
            for (int i = 0; i < 10; i++) sim.Step(Dt, 2000, 2000, 2000, 2000);
            sim.TargetCount = 120;
            for (int i = 0; i < 10; i++) sim.Step(Dt, 2000, 2000, 2000, 2000);
            Assert.IsFalse(sim.Active.Any(p => p.Fleeing && Math.Abs(p.X - 2000) < CrowdSim.CullRadius && Math.Abs(p.Z - 2000) < CrowdSim.CullRadius));
        }
    }
}
