using Cypher.Core;
using Cypher.Perf;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    /// <summary>The pilot drives the real character controller through the real city: kerbs, walls and all.</summary>
    public class BenchmarkPilotWalkTests
    {
        [Test]
        public void ThePilotWalksTheWholeRouteWithoutGettingStuckOrLeavingTheGround()
        {
            using (var rig = new PlayerTestRig())
            {
                rig.PlaceAndSettle(new Vector3(0f, (float)CityLayout.SidewalkHeight + 0.1f, 0f));
                var pilot = new BenchmarkPilot(BenchmarkPilot.DefaultRoute, rig.Player.Sensitivity);
                rig.Player.CommandSource = new PilotCommandSource(rig.Player, pilot);

                float minY = float.MaxValue, maxY = float.MinValue;
                int steps = Mathf.RoundToInt(100f / PlayerTestRig.Dt);
                for (int i = 0; i < steps && pilot.WaypointsReached < BenchmarkPilot.DefaultRoute.Count; i++)
                {
                    rig.Player.Tick(PlayerTestRig.Dt);
                    float y = rig.Player.transform.position.y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }

                Assert.AreEqual(BenchmarkPilot.DefaultRoute.Count, pilot.WaypointsReached, $"stuck at {rig.Player.transform.position}, heading for waypoint {pilot.Target}");
                Assert.Less(pilot.Elapsed, 100.0);
                Assert.Greater(minY, -0.1f, "never fell through the world");
                Assert.Less(maxY, 0.6f, "stayed on the street and kerbs, never climbed anything");
            }
        }

        [Test]
        public void ThePilotKeepsLoopingAndSweepsTheViewOnTheRealController()
        {
            using (var rig = new PlayerTestRig())
            {
                rig.PlaceAndSettle(new Vector3(0f, (float)CityLayout.SidewalkHeight + 0.1f, 0f));
                var pilot = new BenchmarkPilot(BenchmarkPilot.DefaultRoute, rig.Player.Sensitivity);
                rig.Player.CommandSource = new PilotCommandSource(rig.Player, pilot);

                double minPitch = double.MaxValue, maxPitch = double.MinValue;
                int steps = Mathf.RoundToInt(190f / PlayerTestRig.Dt);
                for (int i = 0; i < steps; i++)
                {
                    rig.Player.Tick(PlayerTestRig.Dt);
                    minPitch = System.Math.Min(minPitch, rig.Player.Look.Pitch);
                    maxPitch = System.Math.Max(maxPitch, rig.Player.Look.Pitch);
                }

                Assert.GreaterOrEqual(pilot.Laps, 2, "two laps of the route in about three minutes");
                Assert.Greater(maxPitch, 0.3);
                Assert.Less(minPitch, -0.15);
            }
        }

        [Test]
        public void ThePilotSourceNeedsAPlayerAndAPilot()
        {
            using (var rig = new PlayerTestRig())
            {
                Assert.Throws<System.ArgumentNullException>(() => new PilotCommandSource(null, new BenchmarkPilot(BenchmarkPilot.DefaultRoute)));
                Assert.Throws<System.ArgumentNullException>(() => new PilotCommandSource(rig.Player, null));
            }
        }
    }
}
