#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using Cypher.Core;
using Cypher.Fight;
using Cypher.Player;
using Cypher.Rpg;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The content's encounters in the game: there with their job's stage, on the ground with bodies, back with their survivors after a flatline, kept in the save.</summary>
    public class EncounterPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static EnemyController Fight => Object.FindFirstObjectByType<EnemyController>();
        static RpgService Rpg => Object.FindAnyObjectByType<RpgService>();
        static FirstPersonController Player => Object.FindAnyObjectByType<FirstPersonController>();

        static IEnumerator Ready()
        {
            yield return SliceTestScene.LoadAndPlay();
            for (int i = 0; i < 300 && !Fight.IsActive; i++) yield return null;
            Assert.IsTrue(Fight.IsActive);
            Fight.PatrolsOn = false;
            Fight.Patrols.Reset();
        }

        static IEnumerator Teleport(double x, double z)
        {
            CityNav nav = CityNav.Build(Object.FindAnyObjectByType<Cypher.City.CityBuilder>().Slice.Plan);
            Player.Teleport(new Vector3((float)x, (float)nav.GroundY(x, z) + 0.05f, (float)z), 0f);
            Physics.SyncTransforms();
            for (int i = 0; i < 6; i++) yield return null;
        }

        /// <summary>A job started and moved to a stage, as its conversation and the walk there would.</summary>
        static void ToStage(string quest, string stage)
        {
            Assert.IsTrue(Rpg.Core.Quests.Start(quest, force: true).Ok);
            for (int guard = 0; guard < 10 && Rpg.Core.Quests.GetState(quest).Stage != stage; guard++)
            {
                JsonObject open = ((System.Collections.Generic.List<object>)Rpg.Core.Quests.Snapshot(quest)["objectives"]).Cast<JsonObject>().First(o => !(bool)o["done"]);
                Assert.IsTrue(Rpg.Core.Quests.CompleteObjective(quest, (string)open["id"]).Ok);
            }
            Assert.AreEqual(stage, Rpg.Core.Quests.GetState(quest).Stage);
        }

        static void Kill(Enemy e)
        {
            Assert.IsTrue(Fight.Sim.Harm(e, 1e6));
            Rpg.Kill(e.Archetype, e.Squad.Tag, new Vector3((float)e.Position.X, (float)e.Position.Y, (float)e.Position.Z));
        }

        [UnityTest]
        public IEnumerator TheJackalsHoldTheGarageWithTheGigAndComeBackWithTheirSurvivorsAfterAFlatline()
        {
            yield return Ready();
            RpgLocation garage = Rpg.Core.GetLocation("garage");
            yield return Teleport(garage.X, garage.Z);
            Assert.IsNull(Fight.EncounterDirector.Live("enc_garage_jackals"), "no gig, no Jackals");

            ToStage("q_gig_jackals", "s_clear");
            yield return new WaitForFixedUpdate();
            yield return null;
            EnemySquad jackals = Fight.EncounterDirector.Live("enc_garage_jackals");
            Assert.IsNotNull(jackals, "the gig's stage brings them");
            Assert.AreEqual(5, jackals.All.Count);
            Assert.AreEqual("jackals", jackals.Tag);
            foreach (Enemy e in jackals.All) Assert.Less(Vector2.Distance(new Vector2((float)e.Position.X, (float)e.Position.Z), new Vector2((float)garage.X, (float)garage.Z)), 17f);
            for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(5, Fight.Bodies.All.Count, "each has a body");

            Kill(jackals.All[0]);
            Kill(jackals.All[1]);
            yield return null;
            yield return null;
            Assert.AreEqual("0,1", Rpg.Core.GetFlag(RpgService.EncounterDownFlag("enc_garage_jackals")), "the fallen are in the save");

            Rpg.HurtPlayer(1e6, "test");
            Assert.IsTrue(Fight.Downed);
            yield return new WaitForSeconds(EnemyController.DownSeconds + 0.5f);
            Assert.IsFalse(Fight.Downed);
            Assert.IsNull(Fight.EncounterDirector.Live("enc_garage_jackals"), "the Trauma Team took everyone away");

            yield return Teleport(garage.X, garage.Z);
            jackals = Fight.EncounterDirector.Live("enc_garage_jackals");
            Assert.IsNotNull(jackals);
            Assert.AreEqual(3, jackals.All.Count, "three of five back, not five");
            foreach (Enemy e in jackals.All.ToList()) Kill(e);
            yield return null;
            yield return null;
            Assert.AreEqual("s_back", Rpg.Core.Quests.GetState("q_gig_jackals").Stage, "five Jackals down: the gig moves on");
            Assert.IsTrue(Rpg.EncounterCleared("enc_garage_jackals"));
        }

        [UnityTest]
        public IEnumerator MarlenesCollectorsComeWhileYouAreAway()
        {
            yield return Ready();
            RpgLocation pawn = Rpg.Core.GetLocation("pawn");
            yield return Teleport(pawn.X, pawn.Z);
            ToStage("q_gig_protection", "s_collect");
            for (int i = 0; i < 30; i++) yield return null;
            Assert.IsNull(Fight.EncounterDirector.Live("enc_pawn_collectors"), "not while you are in the shop");

            CityNav nav = CityNav.Build(Object.FindAnyObjectByType<Cypher.City.CityBuilder>().Slice.Plan);
            int away = nav.NodeInBand(new Rng(3), pawn.X, pawn.Z, 45, 70);
            yield return Teleport(nav.NodeX(away), nav.NodeZ(away));
            EnemySquad collectors = Fight.EncounterDirector.Live("enc_pawn_collectors");
            Assert.IsNotNull(collectors, "they came while you were away");
            Assert.AreEqual(3, collectors.All.Count);
        }
    }
}
#endif
