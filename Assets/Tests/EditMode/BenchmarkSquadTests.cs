using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>Gate D's fight: five come on the pavement ahead of the walker, fight it from the start, and are replaced ahead once it has left them behind.</summary>
    public class BenchmarkSquadTests
    {
        static readonly CityPlan Plan = CityPlan.Generate(CityPlan.DefaultSeed);
        static readonly CityNav Nav = CityNav.Build(Plan);
        static readonly PropLayout Props = PropPlacer.Build(Plan, CityWindow.AroundPlaza(3));
        static readonly List<(double, double, double, double)> Walls = Plan.Buildings.Select(b => (b.X, b.Z, b.Width, b.Depth)).ToList();

        static BenchmarkSquad Director(EnemySim sim) => new BenchmarkSquad(sim, Nav, Props.Boxes, Walls);

        /// <summary>Walkers on the pavement nodes, each facing a neighbouring node along the pavement.</summary>
        static IEnumerable<(V3 Feet, double Yaw)> Walkers()
        {
            for (int a = 0; a < Nav.NodeCount; a++)
            {
                IReadOnlyList<int> next = Nav.Neighbours(a);
                if (next.Count == 0) continue;
                double x = Nav.NodeX(a), z = Nav.NodeZ(a);
                yield return (new V3(x, Nav.GroundY(x, z), z), Math.Atan2(Nav.NodeX(next[0]) - x, Nav.NodeZ(next[0]) - z));
            }
        }

        /// <summary>The first walker the squad can come ahead of (the city's edge has walkers looking out of town).</summary>
        static (V3 Feet, double Yaw) Walker() => Walkers().First(w =>
        {
            var probe = new BenchmarkSquad(new EnemySim(0), Nav, Props.Boxes, Walls);
            probe.Step(1.0 / 60, w.Feet, Eye(w.Feet), w.Yaw);
            return probe.Squad != null;
        });

        static V3 Eye(V3 feet) => new V3(feet.X, feet.Y + 1.7, feet.Z);

        static double Off(V3 from, double yaw, V3 to) => Math.Abs(Math.IEEERemainder(Math.Atan2(to.X - from.X, to.Z - from.Z) - yaw, 2 * Math.PI));

        static double Flat(V3 a, V3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        [Test]
        public void FiveComeOnThePavementAheadAndFightAtOnce()
        {
            var sim = new EnemySim(1);
            BenchmarkSquad d = Director(sim);
            (V3 feet, double yaw) = Walker();
            d.Step(1.0 / 60, feet, Eye(feet), yaw);

            EnemySquad squad = d.Squad;
            Assert.IsNotNull(squad);
            Assert.AreEqual(1, d.Spawned);
            Assert.AreEqual(BenchmarkSquad.Tag, squad.Tag);
            Assert.IsTrue(squad.Hostile);
            CollectionAssert.AreEqual(BenchmarkSquad.Members, squad.All.Select(e => e.Archetype).ToList(), "a heavy and four gunners, the largest encounter");
            V3 centre = new V3(squad.All.Average(e => e.Position.X), 0, squad.All.Average(e => e.Position.Z));
            Assert.That(Flat(feet, centre), Is.InRange(BenchmarkSquad.Ahead.Min() - BenchmarkSquad.Reach, BenchmarkSquad.Ahead.Max() + BenchmarkSquad.Reach));
            Assert.LessOrEqual(Off(feet, yaw, centre), BenchmarkSquad.Turns.Max() + 0.5, "ahead, not behind");
            foreach (Enemy e in squad.All)
            {
                Assert.IsTrue(EncounterContent.OnPavement(e.Position.X, e.Position.Z, Nav, Props.Boxes, Walls), $"{e.Id} on clear pavement");
                Assert.AreEqual(Nav.GroundY(e.Position.X, e.Position.Z), e.Position.Y, 1e-9);
                Assert.AreEqual(EnemyState.Engaged, e.State, $"{e.Id} fights from the start");
                foreach (Enemy o in squad.All) if (o != e) Assert.GreaterOrEqual(Flat(e.Position, o.Position), 3 - 1e-6);
            }
        }

        [Test]
        public void TheyKeepFightingAndTheSameSquadStaysWhileTheWalkerIsNear()
        {
            var sim = new EnemySim(2);
            BenchmarkSquad d = Director(sim);
            (V3 feet, double yaw) = Walker();
            d.Step(1.0 / 60, feet, Eye(feet), yaw);
            EnemySquad first = d.Squad;
            var view = new PlayerView(feet, 1.7, 0, false);
            var blind = new BlindWorld();
            for (int i = 0; i < 60 * 20; i++)
            {
                sim.Step(1.0 / 60, view, blind);   // the walker unseen for 20 s: alone, a squad would search and give up
                d.Step(1.0 / 60, feet, Eye(feet), yaw);
            }
            Assert.AreSame(first, d.Squad);
            Assert.AreEqual(1, d.Spawned);
            Assert.IsTrue(first.All.All(e => e.State == EnemyState.Engaged), "told where the walker is, they stay in the fight");
        }

        [Test]
        public void LeftBehindTheyGoAndTheNextComeAheadOfWhereTheWalkerIsNow()
        {
            var sim = new EnemySim(3);
            BenchmarkSquad d = Director(sim);
            (V3 feet, double yaw) = Walker();
            d.Step(1.0 / 60, feet, Eye(feet), yaw);
            EnemySquad first = d.Squad;

            (V3 later, double laterYaw) = Walkers().First(w => first.All.Min(e => Flat(e.Position, w.Feet)) > BenchmarkSquad.LeftBehind + 30);
            d.Step(1.0 / 60, later, Eye(later), laterYaw);
            Assert.IsFalse(sim.Squads.Contains(first), "the squad left behind is gone");
            Assert.IsNotNull(d.Squad);
            Assert.AreNotSame(first, d.Squad);
            Assert.AreEqual(2, d.Spawned);
            Assert.AreEqual(1, sim.Squads.Count);
            Assert.AreEqual(5, sim.Enemies.Count);
            Assert.Less(d.Squad.All.Min(e => Flat(e.Position, later)), BenchmarkSquad.Ahead.Max() + BenchmarkSquad.Reach);
        }

        [Test]
        public void ASquadTakenAwayElsewhereIsReplacedAndResetTakesItAway()
        {
            var sim = new EnemySim(4);
            BenchmarkSquad d = Director(sim);
            (V3 feet, double yaw) = Walker();
            d.Step(1.0 / 60, feet, Eye(feet), yaw);
            sim.Remove(d.Squad);   // as the Trauma Team clears every squad
            d.Step(1.0 / 60, feet, Eye(feet), yaw);
            Assert.IsNotNull(d.Squad);
            Assert.AreEqual(2, d.Spawned);
            d.Reset();
            Assert.IsNull(d.Squad);
            Assert.AreEqual(0, sim.Squads.Count);
            d.Reset();   // twice is fine
        }

        [Test]
        public void AlmostEverywhereOnTheStreetRouteASquadCanComeAhead()
        {
            IReadOnlyList<BenchmarkPilot.Waypoint> route = BenchmarkPilot.Route(BenchmarkRoute.Street, Nav);
            int came = 0;
            for (int i = 0; i < route.Count; i++)
            {
                BenchmarkPilot.Waypoint from = route[i], to = route[(i + 1) % route.Count];
                var feet = new V3(from.X, Nav.GroundY(from.X, from.Z), from.Z);
                var d = Director(new EnemySim(6));
                d.Step(1.0 / 60, feet, Eye(feet), Math.Atan2(to.X - from.X, to.Z - from.Z));
                if (d.Squad != null) came++;
            }
            Console.WriteLine($"a squad came ahead at {came} of the street route's {route.Count} waypoints");
            Assert.GreaterOrEqual(came, route.Count * 0.9, "a squad came ahead at most of the route's waypoints");
        }

        [Test]
        public void TheSameWalkBringsTheSameSquad()
        {
            (V3 feet, double yaw) = Walker();
            List<V3> Where()
            {
                var sim = new EnemySim(5);
                BenchmarkSquad d = Director(sim);
                d.Step(1.0 / 60, feet, Eye(feet), yaw);
                return d.Squad.All.Select(e => e.Position).ToList();
            }
            CollectionAssert.AreEqual(Where(), Where());
        }

        [Test]
        public void ItNeedsItsSimTheWaysAndTheCity()
        {
            Assert.Throws<ArgumentNullException>(() => new BenchmarkSquad(null, Nav, Props.Boxes, Walls));
            Assert.Throws<ArgumentNullException>(() => new BenchmarkSquad(new EnemySim(1), null, Props.Boxes, Walls));
            Assert.Throws<ArgumentNullException>(() => new BenchmarkSquad(new EnemySim(1), Nav, null, Walls));
            Assert.Throws<ArgumentNullException>(() => new BenchmarkSquad(new EnemySim(1), Nav, Props.Boxes, null));
        }

        /// <summary>Nothing can be seen and everything walked: the walker is always out of sight.</summary>
        sealed class BlindWorld : IEnemyWorld
        {
            public bool Clear(V3 from, V3 to) => false;

            public bool Walkable(V3 from, V3 to) => true;
        }
    }
}
