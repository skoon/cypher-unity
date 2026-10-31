#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cypher.City;
using Cypher.Combat;
using Cypher.Core;
using Cypher.Fight;
using Cypher.Perf;
using Cypher.Player;
using Cypher.Rpg;
using Cypher.UI;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>
    /// What the game allocates a frame on the main thread, with the pilot walking the street route, under a budget; and, in the log, each of M4's systems switched off in
    /// turn, so the difference is what that system allocates.
    /// </summary>
    public class AllocationPlayTests
    {
        /// <summary>What a frame of play may allocate on the main thread, in the Editor (which adds a little of its own).</summary>
        const double BudgetBytes = 10 * 1024;

        [SetUp]
        public void SetUp() => SliceTestScene.Isolate(audio: true);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static T Find<T>() where T : UnityEngine.Object => UnityEngine.Object.FindFirstObjectByType<T>();

        /// <summary>Mean bytes allocated a frame over <paramref name="frames"/> frames, after a few to settle.</summary>
        static IEnumerator Measure(string label, int frames, List<(string, double)> into)
        {
            using (ProfilerRecorder rec = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", frames + 10))
            {
                for (int i = 0; i < 10; i++) yield return null;
                Assert.IsTrue(rec.Valid, "the allocation counter is there");
                long total = 0, most = 0;
                int big = 0;
                for (int i = 0; i < frames; i++)
                {
                    yield return null;
                    total += rec.LastValue;
                    most = Math.Max(most, rec.LastValue);
                    if (rec.LastValue > 32 * 1024) big++;
                }
                into.Add((label, total / (double)frames));
                Debug.Log($"[Cypher] allocation: {label}: most in a frame {most / 1024.0:F1} KB, {big} frames over 32 KB");
            }
        }

        [UnityTest]
        public IEnumerator AFrameOfPlayAllocatesLittleAndTheLogSaysWhoAllocatesWhat()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return new WaitForSeconds(2f);
            FirstPersonController player = Find<FirstPersonController>();
            CityBuilder city = Find<CityBuilder>();
            var pilot = new BenchmarkPilot(StreetRoutes.PlazaRing(CityNav.Build(city.Slice.Plan)), player.Sensitivity);
            player.CommandSource = new PilotCommandSource(player, pilot);

            var rows = new List<(string, double)>();
            WeaponSystem guns = Find<WeaponSystem>();
            Behaviour ui = Find<UiRoot>(), rpg = Find<RpgService>(), markers = Find<PlaceMarkers>();

            yield return Measure("everything", 120, rows);
            guns.ScriptedFire = true;
            yield return Measure("everything, firing", 120, rows);
            Debug.Log($"[Cypher] allocation: while firing, {Find<EnemyController>().Sim.Enemies.Count(e => e.Squad.Hostile)} hostile enemies");
            EnemyController fight = Find<EnemyController>();

            // Gate D's worst case: five fighting back all the way
            fight.BenchmarkFight = true;
            int shots = 0;
            Action<EnemyShotFx> counted = fx => shots++;
            fight.EnemyFired += counted;
            // measured once they are in the fight and shooting (the squad coming is a one-off, not a frame of the fight)
            for (float until = Time.time + 20f; Time.time < until && shots == 0;) yield return null;
            Assert.Greater(shots, 0, "the five began firing within 20 s");
            shots = 0;
            yield return Measure("firing, five fighting", 600, rows);   // their fire comes in bursts, with lulls round corners and while the next squad comes
            fight.EnemyFired -= counted;
            Debug.Log($"[Cypher] allocation: with the benchmark's squad, {fight.Counter(1)} engaged, {shots} enemy shots in the measurement");
            Assert.Greater(shots, 0, "the five were firing during the measurement");
            fight.BenchmarkFight = false;
            fight.BenchmarkSquad.Reset();

            fight.PatrolsOn = false;
            fight.Patrols.Reset();
            yield return Measure("firing, no patrols", 120, rows);
            fight.enabled = false;
            yield return Measure("firing, without fights", 120, rows);
            fight.enabled = true;
            fight.PatrolsOn = true;
            guns.ScriptedFire = false;
            yield return new WaitForSeconds(3f);

            foreach ((string name, Behaviour b) in new (string, Behaviour)[] { ("UiRoot (HUD)", ui), ("RpgService", rpg), ("PlaceMarkers", markers), ("WeaponSystem", guns) })
            {
                b.enabled = false;
                yield return Measure("without " + name, 120, rows);
                b.enabled = true;
            }
            foreach (Behaviour b in new[] { ui, rpg, markers, (Behaviour)guns }) b.enabled = false;
            yield return Measure("without all four", 120, rows);
            foreach (Behaviour b in new[] { ui, rpg, markers, (Behaviour)guns }) b.enabled = true;

            Debug.Log("[Cypher] allocation per frame:\n" + string.Join("\n", rows.Select(r => $"  {r.Item1,-24} {r.Item2 / 1024,8:F2} KB")));

            // before the HUD and the markers kept their views it was 43 KB a frame (a collection every two seconds); the budget catches a return to that
            Assert.Less(rows[0].Item2, BudgetBytes, "walking, all on");
            Assert.Less(rows[1].Item2, BudgetBytes, "walking and firing, all on");
            Assert.Less(rows[2].Item2, BudgetBytes, "walking and firing with five fighting back");
        }
    }
}
#endif
