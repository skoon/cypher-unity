#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cypher.Actors;
using Cypher.City;
using Cypher.Combat;
using Cypher.Core;
using Cypher.Fight;
using Cypher.Perf;
using Cypher.Player;
using Cypher.Rpg;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Cypher.Tests
{
    /// <summary>
    /// Whole fights played through the game as a player would: the Jackals cleared with the real gun and a hack through the scanner, Gate D's squad fighting the
    /// benchmark's walker, and the enemies' volumes, which shots find and the player walks through.
    /// </summary>
    public class FightPilotPlayTests
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
        static WeaponSystem Guns => Object.FindAnyObjectByType<WeaponSystem>();
        static UiRoot Ui => Object.FindFirstObjectByType<UiRoot>();
        static CityNav Nav => CityNav.Build(Object.FindAnyObjectByType<CityBuilder>().Slice.Plan);

        static IEnumerator Ready()
        {
            yield return SliceTestScene.LoadAndPlay();
            for (int i = 0; i < 300 && !Fight.IsActive; i++) yield return null;
            Assert.IsTrue(Fight.IsActive);
            Fight.PatrolsOn = false;
            Fight.Patrols.Reset();
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator Click(VirtualInput input)
        {
            input.PressMouse(MouseButton.Left, true);
            yield return null;
            input.PressMouse(MouseButton.Left, false);
            yield return null;
        }

        static void LookAt(Vector3 target)
        {
            Vector3 eye = Camera.main.transform.position, d = target - eye;
            Player.Look.Set(Math.Atan2(d.x, d.z), Math.Atan2(d.y, Math.Sqrt(d.x * d.x + d.z * d.z)));
        }

        static Vector3 Chest(Enemy e) => new Vector3((float)e.Position.X, (float)e.Position.Y + EnemyController.ChestHeight, (float)e.Position.Z);

        /// <summary>The middle of an enemy's posed body and head volumes, as the shots see them; empty while it has none.</summary>
        static IEnumerable<Vector3> AimPoints(Enemy e)
        {
            for (int i = 0; i < EnemyProxies.Count * 2; i++)
            {
                GameObject go = Fight.Proxies[i];
                EnemyHitbox box = go.GetComponent<EnemyHitbox>();
                if (box.Enemy == e.Id) yield return go.GetComponent<Collider>().bounds.center;
            }
        }

        /// <summary>Whether a ray from the eye to <paramref name="point"/> meets <paramref name="e"/> first.</summary>
        static bool InSight(Enemy e, Vector3 point)
        {
            Vector3 eye = Camera.main.transform.position;
            return Physics.Raycast(eye, (point - eye).normalized, out RaycastHit h, 200f, ~0, QueryTriggerInteraction.Ignore) && EnemyHitbox.Of(h.collider)?.Enemy == e.Id;
        }

        /// <summary>A pavement node 10 to 20 m from the place with a clear view of it, facing it.</summary>
        static Vector3 Vantage(RpgLocation place)
        {
            CityNav nav = Nav;
            var target = new Vector3((float)place.X, (float)nav.GroundY(place.X, place.Z) + 1.2f, (float)place.Z);
            for (int n = 0; n < nav.NodeCount; n++)
            {
                double dx = nav.NodeX(n) - place.X, dz = nav.NodeZ(n) - place.Z, d = Math.Sqrt(dx * dx + dz * dz);
                if (d < 10 || d > 20) continue;
                var eye = new Vector3((float)nav.NodeX(n), (float)nav.WalkY + 1.6f, (float)nav.NodeZ(n));
                if (!Physics.Linecast(eye, target, ~0, QueryTriggerInteraction.Ignore)) return new Vector3(eye.x, (float)nav.WalkY + 0.05f, eye.z);
            }
            throw new InvalidOperationException($"no pavement node 10 to 20 m from {place.Key} sees it");
        }

        /// <summary>
        /// A place to stand 6 to 12 m from <paramref name="e"/>, on the ground, with room for the player and a clear view of its chest (only enemies and people may be
        /// in the line: they do not stop the player's look).
        /// </summary>
        static bool Flank(Enemy e, out Vector3 spot)
        {
            CityNav nav = Nav;
            CharacterController cc = Player.Controller;
            Vector3 chest = Chest(e);
            foreach (float r in new[] { 8f, 6f, 10f, 12f })
            {
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8;
                    float x = chest.x + Mathf.Sin(a) * r, z = chest.z + Mathf.Cos(a) * r;
                    var feet = new Vector3(x, (float)nav.GroundY(x, z) + 0.05f, z);
                    if (Physics.CheckCapsule(feet + Vector3.up * (cc.radius + 0.1f), feet + Vector3.up * (cc.height - cc.radius), cc.radius, ~0, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 eye = feet + Vector3.up * 1.6f;
                    bool clear = true;
                    foreach (RaycastHit h in Physics.RaycastAll(eye, (chest - eye).normalized, Vector3.Distance(eye, chest), ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (EnemyHitbox.Of(h.collider) == null && !h.collider.name.StartsWith("PedestrianProxy", StringComparison.Ordinal)) clear = false;
                    }
                    if (!clear) continue;
                    spot = feet;
                    return true;
                }
            }
            spot = default;
            return false;
        }

        static void ToStage(string quest, string stage)
        {
            Assert.IsTrue(Rpg.Core.Quests.Start(quest, force: true).Ok);
            for (int guard = 0; guard < 10 && Rpg.Core.Quests.GetState(quest).Stage != stage; guard++)
            {
                JsonObject open = ((List<object>)Rpg.Core.Quests.Snapshot(quest)["objectives"]).Cast<JsonObject>().First(o => !(bool)o["done"]);
                Assert.IsTrue(Rpg.Core.Quests.CompleteObjective(quest, (string)open["id"]).Ok);
            }
            Assert.AreEqual(stage, Rpg.Core.Quests.GetState(quest).Stage);
        }

        /// <summary>The deck holding these hacks from slot 0, and RAM for all of them.</summary>
        static void Deck(params string[] templates)
        {
            RpgCore core = Rpg.Core;
            for (int i = 0; i < core.Inventory.DeckSize; i++) core.Inventory.UnequipHack(i);
            for (int i = 0; i < templates.Length; i++)
            {
                RpgResult given = core.GiveItem(templates[i]);
                Assert.IsTrue(given.Ok, $"given {templates[i]}: {given.Reason}");
                Item item = given.Item ?? core.Inventory.Items.Last(it => it.TemplateId == templates[i]);
                Assert.IsTrue(core.Inventory.EquipHack(item.Uid, i).Ok);
            }
            core.Deck.SetMaxRam(40);
            core.Deck.Fill();
        }

        /// <summary>The pilot keeps the player standing (a bad run fails on what it checks, not by flatlining): patched up below half health.</summary>
        static void KeepStanding()
        {
            double max = Rpg.Core.Derived["maxHealth"];
            if (Rpg.Core.Health < max / 2) Rpg.Core.Heal(max);
        }

        // ---------------------------------------------------------------- the Jackals, cleared by the gun and a hack

        [UnityTest]
        public IEnumerator TheJackalsAreClearedWithTheGunAndAHackAndTheGigMovesOn()
        {
            using (var input = new VirtualInput())
            {
                yield return Ready();
                RpgLocation garage = Rpg.Core.GetLocation("garage");
                Vector3 stand = Vantage(garage);
                ToStage("q_gig_jackals", "s_clear");
                // to the garage (the stage's "reach"), then back to where it can be seen from
                Player.Teleport(new Vector3((float)garage.X, stand.y, (float)garage.Z), 0f);
                Physics.SyncTransforms();
                for (int i = 0; i < 60 && ((List<object>)Rpg.Core.Quests.Snapshot("q_gig_jackals")["objectives"]).Cast<JsonObject>().Any(o => (string)o["trigger"] == "reach" && !(bool)o["done"]); i++) yield return null;
                Assert.IsFalse(((List<object>)Rpg.Core.Quests.Snapshot("q_gig_jackals")["objectives"]).Cast<JsonObject>().Any(o => (string)o["trigger"] == "reach" && !(bool)o["done"]), "at the garage");
                Player.Teleport(stand, 0f);
                Physics.SyncTransforms();
                EnemySquad jackals = null;
                for (int i = 0; i < 120 && (jackals = Fight.EncounterDirector.Live("enc_garage_jackals")) == null; i++) yield return null;
                Assert.IsNotNull(jackals, "the Jackals hold the garage");
                Assert.AreEqual(5, jackals.All.Count);
                Enemy heavy = jackals.All.Single(e => e.Archetype == EnemyTable.Heavy);
                Deck("qh_ping", "qh_short_circuit");
                double xp = Rpg.Core.Stats.Xp;
                int hurt = 0, playerHits = 0;
                bool barFollowed = false;
                double lowest = Rpg.Core.Health;
                Fight.EnemyHurt += (e, killed, hit) => { if (hit.Damage > 0) hurt++; };
                Fight.PlayerHit += (from, d) => playerHits++;

                // the hack: Ping the one in sight, then a Short Circuit on the heavy, pinged so it can be reached wherever it stands
                Enemy seen = null;
                for (int i = 0; i < 600 && seen == null; i++)
                {
                    LookAt(new Vector3((float)garage.X, stand.y + 1.5f, (float)garage.Z));
                    yield return null;
                    seen = jackals.All.FirstOrDefault(e => e.State != EnemyState.Down && AimPoints(e).Any(p => InSight(e, p)));
                }
                Assert.IsNotNull(seen, "a Jackal comes into sight within 10 s");
                input.Hold(Key.F);
                for (int i = 0; i < 30 && Fight.HackTarget == null; i++)
                {
                    Vector3[] points = AimPoints(seen).ToArray();
                    if (points.Length > 0) LookAt(points[0]);
                    yield return null;
                }
                Assert.IsTrue(Fight.Hacking);
                Assert.IsNotNull(Fight.HackTarget, "a Jackal in the crosshair is the target");
                CollectionAssert.Contains(jackals.All, Fight.HackTarget);
                Assert.AreEqual(0, Fight.HackSlot);
                double ram = Rpg.Core.Deck.Ram;
                yield return Click(input);
                Assert.IsTrue(jackals.All.All(e => e.Revealed), "Ping marks the squad");
                for (int i = 0; i < 30 && Fight.HackTarget != heavy; i++)
                {
                    LookAt(Chest(heavy));
                    yield return null;
                }
                Assert.AreSame(heavy, Fight.HackTarget, "the pinged heavy is the target, cover or not");
                Fight.SelectHack(1);
                double before = heavy.Vitals.Health;
                yield return Click(input);
                Assert.Less(heavy.Vitals.Health, before, "the Short Circuit struck");
                Assert.Less(Rpg.Core.Deck.Ram, ram, "the hacks cost RAM");
                input.Release(Key.F);
                yield return Frames(3);
                Assert.IsFalse(Guns.Suppressed, "the gun is back up");

                // the gun: short bursts at whoever is in sight, reloading when empty
                var struck = new Dictionary<string, int>();
                int sighted = 0, looked = 0;
                Guns.Shot += r =>
                {
                    string what = r.Target == ShotTarget.Enemy ? "enemy" : r.Hit ? r.Target + " " + r.Surface : "nothing";
                    struck[what] = struck.TryGetValue(what, out int n) ? n + 1 : 1;
                };
                float until = Time.time + 150f, lastSeen = Time.time;
                int flanks = 0;
                while (jackals.All.Any(e => e.State != EnemyState.Down) && Time.time < until)
                {
                    // nobody in sight for a while: go round to where the nearest one can be seen, as a player would
                    if (Time.time - lastSeen > 3f)
                    {
                        Enemy hiding = jackals.All.Where(e => e.State != EnemyState.Down).OrderBy(e => Vector3.Distance(Player.transform.position, Chest(e))).First();
                        if (Flank(hiding, out Vector3 spot))
                        {
                            Player.Teleport(spot, 0f);
                            Physics.SyncTransforms();
                            flanks++;
                        }
                        lastSeen = Time.time;
                    }
                    KeepStanding();
                    lowest = Math.Min(lowest, Rpg.Core.Health);
                    WeaponSlot gun = Guns.Rig.Current;
                    gun.Reserve = gun.Config.Reserve;   // the pilot never runs dry
                    if (gun.Ammo == 0 && Guns.Rig.State != WeaponState.Reload)
                    {
                        input.PressMouse(MouseButton.Left, false);
                        input.Hold(Key.R);
                        yield return null;
                        input.Release(Key.R);
                        yield return null;
                        continue;
                    }
                    Enemy target = null;
                    Vector3 point = default;
                    float best = float.PositiveInfinity;
                    foreach (Enemy e in jackals.All)
                    {
                        if (e.State == EnemyState.Down) continue;
                        foreach (Vector3 p in AimPoints(e))
                        {
                            float d = Vector3.Distance(Camera.main.transform.position, p);
                            if (d < best && InSight(e, p))
                            {
                                best = d;
                                target = e;
                                point = p;
                            }
                        }
                    }
                    looked++;
                    if (target != null)
                    {
                        sighted++;
                        lastSeen = Time.time;
                    }
                    if (target != null) LookAt(point);
                    bool burst = target != null && Time.time % 0.4f < 0.25f;
                    input.PressMouse(MouseButton.Left, burst);
                    yield return null;
                    if (target != null && Fight.Aimed == target && Ui.Hud.Fight.BarShown) barFollowed = true;
                }
                input.PressMouse(MouseButton.Left, false);
                yield return Frames(3);

                Debug.Log($"[Cypher] fight pilot: {hurt} hits, {playerHits} hits taken, lowest health {lowest:0}, {150f - (until - Time.time):0} s; " +
                          $"a target in sight {sighted} of {looked} frames, {flanks} flanks; shots struck {string.Join(", ", struck.Select(kv => kv.Key + " " + kv.Value))}; " +
                          $"left: {string.Join(", ", jackals.All.Select(e => $"{e.Archetype.Id} {e.State} {e.Vitals.Health:0}"))}");
                Assert.IsTrue(jackals.All.All(e => e.State == EnemyState.Down), "all five down within 150 s");
                Assert.Greater(hurt, 5, "the gun's hits landed");
                Assert.IsTrue(barFollowed, "the health bar was over the enemy aimed at");
                Assert.Greater(playerHits, 0, "they fired back and hit");
                Assert.Less(lowest, Rpg.Core.Derived["maxHealth"], "and it hurt");
                Assert.Greater(Rpg.Core.Stats.Xp, xp, "the kills paid");
                Assert.AreEqual("s_back", Rpg.Core.Quests.GetState("q_gig_jackals").Stage, "five Jackals down: back to Kaz");
                Assert.IsTrue(Rpg.EncounterCleared("enc_garage_jackals"), "cleared in the save");
            }
        }

        // ---------------------------------------------------------------- Gate D's squad

        [UnityTest]
        public IEnumerator TheBenchmarksSquadFightsTheWalkerAllTheWayAndNeitherSideFalls()
        {
            yield return Ready();
            Fight.BenchmarkFight = true;
            var pilot = new BenchmarkPilot(StreetRoutes.PlazaRing(Nav), Player.Sensitivity);
            Player.CommandSource = new PilotCommandSource(Player, pilot);
            Guns.ScriptedFire = true;
            int enemyShots = 0, playerHits = 0, engagedFrames = 0, frames = 0;
            Fight.EnemyFired += fx => enemyShots++;
            Fight.PlayerHit += (from, d) => playerHits++;
            Vector3 start = Player.transform.position;
            double walked = 0;
            Vector3 last = start;

            float until = Time.time + 40f;
            while (Time.time < until)
            {
                yield return null;
                frames++;
                walked += Vector3.Distance(last, Player.transform.position);
                last = Player.transform.position;
                Assert.IsFalse(Fight.Downed, "the walker is never flatlined");
                EnemySquad squad = Fight.BenchmarkSquad.Squad;
                if (squad == null) continue;
                Assert.IsFalse(squad.All.Any(e => e.State == EnemyState.Down), "the benchmark's squad never falls");
                if (Fight.Counter(1) > 0) engagedFrames++;
            }
            Guns.ScriptedFire = false;

            Debug.Log($"[Cypher] benchmark fight: {Fight.BenchmarkSquad.Spawned} squads, {enemyShots} shots in, {playerHits} hits, engaged {engagedFrames} of {frames} frames, {walked:0} m walked");
            Assert.GreaterOrEqual(Fight.BenchmarkSquad.Spawned, 1);
            Assert.Greater(enemyShots, 20, "they fired");
            Assert.Greater(playerHits, 0, "and hit");
            Assert.Greater(engagedFrames, frames * 0.7, "fighting most of the time");
            Assert.Greater(walked, 30, "and the walker went on walking");
            Assert.AreEqual(3, Fight.CounterCount);
            Assert.AreEqual("enemies_engaged", Fight.CounterName(1));
        }

        // ---------------------------------------------------------------- the volumes

        [UnityTest]
        public IEnumerator ThePlayerWalksThroughAnEnemysVolumeAndShotsStillFindIt()
        {
            yield return Ready();
            Transform p = Player.transform;
            int tried = 0;
            // on a stretch with nothing in the way; the people walk, so a stretch someone stepped onto is given up for the next
            foreach ((Vector3 open, Vector3 ahead) in OpenPavement().Take(40))
            {
                tried++;
                Player.Teleport(open, (float)(Math.Atan2(ahead.x, ahead.z) * Mathf.Rad2Deg));
                Physics.SyncTransforms();
                Vector3 at = open + ahead * 2f;
                at.y = (float)Nav.GroundY(at.x, at.z);
                EnemySquad squad = Fight.Spawn("test", new[] { (EnemyTable.Heavy, at) }, yaw: Player.Look.Yaw, hostile: false);
                Enemy e = squad.All[0];
                for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                Physics.SyncTransforms();
                Vector3 body = AimPoints(e).First();
                LookAt(body);
                if (Blocked(open, ahead, butEnemies: true))
                {
                    Fight.Sim.Remove(squad);
                    continue;
                }

                Assert.AreEqual(EnemyProxies.Layer, Fight.Proxies[0].layer);
                Vector3 eye = Camera.main.transform.position;
                string met = Physics.Raycast(eye, (body - eye).normalized, out RaycastHit h, 50f, ~0, QueryTriggerInteraction.Ignore)
                    ? $"{h.collider.name} at {h.distance:0.00} m (layer {h.collider.gameObject.layer})" : "nothing";
                Assert.IsTrue(InSight(e, body), $"a ray from {eye} to the volume at {body} still meets it; it met {met}");

                Vector3 from = p.position;
                for (int i = 0; i < 40; i++) Player.Controller.Move(ahead * 0.1f);
                float went = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(p.position.x, 0, p.position.z));
                Assert.Greater(went, 3.8f, "the player walked through the enemy, not into it");
                Debug.Log($"[Cypher] volumes: walked {went:0.0} m through the enemy on stretch {tried}");
                yield break;
            }
            Assert.Fail($"no stretch of pavement stayed clear of people in {tried} tries");
        }

        /// <summary>Pavement nodes, each with a way along the pavement from it with nothing in the player's way for 5 m now.</summary>
        static IEnumerable<(Vector3 At, Vector3 Ahead)> OpenPavement()
        {
            CityNav nav = Nav;
            for (int n = 0; n < nav.NodeCount; n++)
            {
                foreach (int m in nav.Neighbours(n))
                {
                    var at = new Vector3((float)nav.NodeX(n), (float)nav.WalkY + 0.05f, (float)nav.NodeZ(n));
                    var to = new Vector3((float)nav.NodeX(m), at.y, (float)nav.NodeZ(m));
                    if (Vector3.Distance(at, to) < 6) continue;
                    Vector3 ahead = (to - at).normalized;
                    if (!Blocked(at, ahead)) yield return (at, ahead);
                }
            }
        }

        /// <summary>Whether anything but the player itself (and the enemies' volumes, if asked) is where the player stands or in its way for 5 m from <paramref name="at"/>.</summary>
        static bool Blocked(Vector3 at, Vector3 ahead, bool butEnemies = false)
        {
            CharacterController cc = Player.Controller;
            Vector3 low = at + Vector3.up * (cc.radius + 0.2f), high = at + Vector3.up * (cc.height - cc.radius);
            bool Counts(Collider c) => c != cc && !(butEnemies && EnemyHitbox.Of(c) != null);
            foreach (Collider c in Physics.OverlapCapsule(low, high, cc.radius + 0.1f, ~0, QueryTriggerInteraction.Ignore)) if (Counts(c)) return true;
            foreach (RaycastHit h in Physics.CapsuleCastAll(low, high, cc.radius, ahead, 5f, ~0, QueryTriggerInteraction.Ignore)) if (Counts(h.collider)) return true;
            return false;
        }
    }
}
#endif
