using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>
    /// The RPG core against the prototype's own code: <c>tools/gen-golden-rpg-world.mjs</c> put the named places on test cities and on the real seeded city, generated
    /// and played breach puzzles, ran the quickhack deck, and drove whole RpgCore sessions (the main job to each of its three endings, the side jobs, and seeded random
    /// sessions over the whole API), writing every operation's arguments, result and events. These tests replay the arguments on the C# port and compare everything.
    /// </summary>
    public class RpgWorldGoldenTests
    {
        static List<JsonObject> _suites;

        static IEnumerable<JsonObject> Suites(string name, [CallerFilePath] string sourceFile = "")
        {
            if (_suites == null)
            {
                _suites = new List<JsonObject>();
                foreach (string line in File.ReadAllLines(Path.Combine(Path.GetDirectoryName(sourceFile), "Fixtures", "rpg_world_golden.jsonl")))
                {
                    if (line.Length > 0) _suites.Add((JsonObject)Json.Parse(line));
                }
            }
            List<JsonObject> found = _suites.FindAll(s => (string)s["suite"] == name);
            Assert.IsNotEmpty(found, $"no '{name}' suite in the fixture");
            return found;
        }

        // ---------------------------------------------------------------- comparison

        /// <summary>A C# value in the fixture's shape: items, perks and chrome written whole, results as their JSON, the static stats metadata left out, infinities as null.</summary>
        static object Norm(object v)
        {
            switch (v)
            {
                case null: return null;
                case Item item: return Norm(item.ToJson());
                case Perk perk: return Norm(perk.ToJson());
                case CyberwareDef ware: return Norm(ware.ToJson());
                case RpgResult r: return Norm(r.ToJson());
                case XpGain g:
                {
                    var o = new JsonObject { ["gained"] = g.Gained, ["levels"] = g.Levels.Select(l => (object)(double)l).ToList(), ["level"] = (double)g.Level };
                    if (!double.IsNaN(g.Total)) o["total"] = g.Total;
                    return o;
                }
                case double d: return double.IsInfinity(d) || double.IsNaN(d) ? null : (object)d;
                case int i: return (double)i;
                case JsonObject o:
                {
                    var n = new JsonObject();
                    foreach (KeyValuePair<string, object> kv in o)
                    {
                        if (kv.Key == "attributeMeta" || kv.Key == "derivedMeta") continue;
                        n[kv.Key] = Norm(kv.Value);
                    }
                    return n;
                }
                case string s: return s;
                case bool b: return b;
                case System.Collections.IEnumerable list:
                {
                    var n = new List<object>();
                    foreach (object x in list) n.Add(Norm(x));
                    return n;
                }
                default: return v;
            }
        }

        static void Same(object expected, object actual, string path)
        {
            actual = Norm(actual);
            switch (expected)
            {
                case null:
                    Assert.IsNull(actual, $"{path}: expected null, got {Show(actual)}");
                    return;
                case double d:
                    Assert.IsInstanceOf<double>(actual, $"{path}: expected the number {d}, got {Show(actual)}");
                    if ((double)actual != d) Assert.Fail($"{path}: expected {d.ToString("R", CultureInfo.InvariantCulture)}, got {((double)actual).ToString("R", CultureInfo.InvariantCulture)}");
                    return;
                case string s:
                    Assert.AreEqual(s, actual, path);
                    return;
                case bool b:
                    Assert.AreEqual(b, actual, path);
                    return;
                case JsonObject eo:
                {
                    Assert.IsInstanceOf<JsonObject>(actual, $"{path}: expected an object, got {Show(actual)}");
                    var ao = (JsonObject)actual;
                    var missing = eo.Keys.Where(k => !ao.ContainsKey(k)).ToList();
                    var extra = ao.Keys.Where(k => !eo.ContainsKey(k)).ToList();
                    if (missing.Count > 0 || extra.Count > 0) Assert.Fail($"{path}: keys differ; missing [{string.Join(", ", missing)}], extra [{string.Join(", ", extra)}]; got {Show(ao)}");
                    foreach (KeyValuePair<string, object> kv in eo) Same(kv.Value, ao[kv.Key], path + "." + kv.Key);
                    return;
                }
                case List<object> el:
                {
                    Assert.IsInstanceOf<List<object>>(actual, $"{path}: expected an array, got {Show(actual)}");
                    var al = (List<object>)actual;
                    Assert.AreEqual(el.Count, al.Count, $"{path}: length; expected {Show(el)}, got {Show(al)}");
                    for (int i = 0; i < el.Count; i++) Same(el[i], al[i], $"{path}[{i}]");
                    return;
                }
                default:
                    Assert.Fail($"{path}: unexpected fixture value {expected}");
                    return;
            }
        }

        static string Show(object v)
        {
            try
            {
                string s = Json.Write(Norm(v), false);
                return s.Length > 500 ? s.Substring(0, 500) + "…" : s;
            }
            catch (ArgumentException)
            {
                return v?.ToString() ?? "null";
            }
        }

        static int I(object v) => (int)(double)v;
        static double D(object v) => (double)v;
        static string S(JsonObject o, string key) => o.TryGetValue(key, out object v) ? v as string : null;

        // ---------------------------------------------------------------- places

        static RpgCityMeta City(JsonObject c)
        {
            var meta = new RpgCityMeta { BlockSize = D(c["blockSize"]), Cell = D(c["cell"]), Grid = I(c["grid"]) };
            foreach (JsonObject s in ((List<object>)c["streets"]).Cast<JsonObject>()) meta.Streets.Add((I(s["gx"]), I(s["gz"]), D(s["cx"]), D(s["cz"]), (bool)s["isPlaza"]));
            foreach (JsonObject b in ((List<object>)c["buildings"]).Cast<JsonObject>()) meta.Buildings.Add((D(b["x"]), D(b["z"]), D(b["w"]), D(b["d"]), D(b["h"])));
            return meta;
        }

        [Test]
        public void ThePlacesLandOnTheSameBlocksAsInThePrototype()
        {
            JsonObject suite = Suites("locations").Single();
            var cities = ((List<object>)suite["cities"]).Cast<JsonObject>().Select(City).ToList();
            int n = 0;
            foreach (JsonObject c in ((List<object>)suite["cases"]).Cast<JsonObject>())
            {
                List<RpgLocation> built = RpgLocations.Build(cities[I(c["city"])], (uint)D(c["seed"]));
                Same(c["locations"], built.Select(l => (object)l.ToJson()).ToList(), $"locations case {n++}");
            }
        }

        [Test]
        public void OnTheRealCityThePlacesAreWhereThePrototypePutsThem()
        {
            JsonObject suite = Suites("realCity").Single();
            RpgCityMeta meta = RpgCityMeta.From(CityPlan.Generate((uint)D(suite["citySeed"])));
            List<RpgLocation> built = RpgLocations.Build(meta, (uint)D(suite["seed"]));
            var expected = ((List<object>)suite["locations"]).Cast<JsonObject>().ToList();
            Assert.AreEqual(expected.Count, built.Count);
            for (int i = 0; i < built.Count; i++)
            {
                // the block a place was put on is checked by its centre: the city's own street records carry fields of their own
                Assert.AreEqual(S(expected[i], "key"), built[i].Key);
                Same(expected[i]["x"], built[i].X, built[i].Key + ".x");
                Same(expected[i]["z"], built[i].Z, built[i].Key + ".z");
                Same(expected[i]["radius"], built[i].Radius, built[i].Key + ".radius");
                Same(((JsonObject)expected[i]["block"])["cx"], built[i].Block["cx"], built[i].Key + ".block.cx");
                Same(((JsonObject)expected[i]["block"])["cz"], built[i].Block["cz"], built[i].Key + ".block.cz");
            }
        }

        // ---------------------------------------------------------------- breach and deck

        static JsonObject SelectJson((bool Ok, string Reason, string Code, List<BreachDaemon> Solved, bool Done) r) => r.Ok
            ? new JsonObject { ["ok"] = true, ["code"] = r.Code, ["solved"] = r.Solved.Select(d => (object)d.ToJson()).ToList(), ["done"] = r.Done }
            : new JsonObject { ["ok"] = false, ["reason"] = r.Reason };

        static List<object> Legal(BreachSession s) => s.LegalCells().Select(c => (object)new JsonObject { ["r"] = (double)c.R, ["c"] = (double)c.C, ["code"] = c.Code }).ToList();

        [Test]
        public void BreachPuzzlesAreGeneratedAndPlayedAsThePrototypes()
        {
            int n = 0;
            foreach (JsonObject c in ((List<object>)Suites("breach").Single()["cases"]).Cast<JsonObject>())
            {
                string path = $"breach case {n++}";
                var o = (JsonObject)c["opts"];
                BreachState state = RpgHacking.Generate((uint)D(o["seed"]), I(o["size"]), D(o["bufferSize"]), D(o["timeLimit"]), I(o["tier"]), S(o, "targetId"), S(o, "targetName"),
                    o.TryGetValue("daemonCount", out object dc) ? I(dc) : (int?)null);
                Same(c["initial"], state.ToJson(), path + ".initial");
                var daemonEvents = new List<object>();
                var endEvents = new List<object>();
                var session = new BreachSession(state, (d, s) => daemonEvents.Add(d.Id), (r, s) => endEvents.Add(r.ToJson()));
                int k = 0;
                foreach (JsonObject op in ((List<object>)c["ops"]).Cast<JsonObject>())
                {
                    string p = $"{path} op {k++} {op["op"]}";
                    switch ((string)op["op"])
                    {
                        case "select":
                            Same(op["result"], SelectJson(session.Select(I(op["r"]), I(op["c"]))), p + ".result");
                            Same(op["legal"], Legal(session), p + ".legal");
                            break;
                        case "tick":
                            session.Tick(D(op["dt"]));
                            Same(op["timeLeft"], state.TimeLeft, p + ".timeLeft");
                            Same(op["state"], state.State, p + ".state");
                            break;
                        case "finish": Same(op["result"], session.Finish("uploaded").ToJson(), p + ".result"); break;
                        default: Assert.Fail($"{p}: unknown op"); break;
                    }
                }
                Same(c["final"], state.ToJson(), path + ".final");
                Same(c["daemonEvents"], daemonEvents, path + ".daemonEvents");
                Same(c["endEvents"], endEvents, path + ".endEvents");
            }
        }

        [Test]
        public void TheQuickhackDeckSpendsRamRefreshesAndExpiresAsThePrototypes()
        {
            var deck = new QuickhackDeck();
            deck.SetMaxRam(8);
            deck.Fill();
            int k = 0;
            foreach (JsonObject op in ((List<object>)Suites("deck").Single()["ops"]).Cast<JsonObject>())
            {
                string p = $"deck op {k++} {op["op"]}";
                switch ((string)op["op"])
                {
                    case "upload":
                    {
                        var io = (JsonObject)op["item"];
                        var item = new Item { TemplateId = S(io, "templateId"), Name = S(io, "name"), Hack = HackSpec.FromJson(io.TryGetValue("hack", out object h) ? h as JsonObject : null) };
                        var r = deck.Upload(item, S(op, "target"), D(op["durationScale"]), D(op["discount"]));
                        var j = new JsonObject { ["ok"] = r.Ok };
                        if (r.Reason != null) j["reason"] = r.Reason;
                        if (r.Cost.HasValue) j["cost"] = r.Cost.Value;
                        if (r.Hack != null) j["hack"] = r.Hack.ToJson();
                        Same(op["result"], j, p);
                        break;
                    }
                    case "tick":
                        Same(op["result"], new JsonObject { ["expired"] = deck.Tick(D(op["dt"]), D(op["regen"])).Select(a => (object)a.ToJson()).ToList() }, p);
                        break;
                    case "setMaxRam": Same(op["result"], deck.SetMaxRam(D(op["max"])), p); break;
                    default: Assert.Fail($"{p}: unknown op"); break;
                }
                Same(op["state"], deck.Serialize(), p + ".state");
            }
        }

        // ---------------------------------------------------------------- the core

        sealed class Recorder
        {
            public readonly List<object> Events = new List<object>();

            public void Emit(string type, JsonObject payload) => Events.Add(new JsonObject { ["type"] = type, ["payload"] = Norm(payload) });
        }

        static double? OptD(JsonObject o, string key) => o.TryGetValue(key, out object v) && v is double d ? d : (double?)null;

        static object Run(RpgCore core, JsonObject op, RpgCityMeta city)
        {
            switch ((string)op["op"])
            {
                case "setCity":
                    core.SetCity(city);
                    return new JsonObject();   // the prototype returns a Map, which serialises as {}
                case "setPlayerPosition":
                    core.SetPlayerPosition(D(op["x"]), D(op["y"]), D(op["z"]));
                    return null;
                case "tick":
                    core.Tick(D(op["dt"]), D(op["gameHours"]));
                    return null;
                case "interact": return core.Interact();
                case "startDialogue": return core.StartDialogue(S(op, "graphId"));
                case "choose": return core.ChooseDialogueOption(S(op, "optionId"));
                case "endDialogue": return core.EndDialogue();
                case "startBreach": return core.StartBreach(S(op, "targetId"));
                case "breachSelect": return core.BreachSelect(I(op["r"]), I(op["c"]));
                case "breachUpload": return core.BreachUpload();
                case "cancelBreach": return core.CancelBreach();
                case "completeObjective": return core.CompleteObjective(S(op, "questId"), S(op, "objectiveId"));
                case "grantAttributePoints":
                    core.Stats.AttributePoints += I(op["n"]);
                    return null;
                case "spendAttributePoint": return core.SpendAttributePoint(S(op, "attr"));
                case "giveItem": return core.GiveItem(S(op, "templateId"), S(op, "rarity"), OptD(op, "level"), OptD(op, "qty") ?? 1);
                case "giveCyberware": return core.GiveCyberware(S(op, "id"));
                case "setFlag": return core.SetFlag(S(op, "name"), op["value"]);
                case "notifyKill":
                    core.NotifyKill(S(op, "tag"), D(op["n"]));
                    return null;
                case "save":
                {
                    var r = core.Save(S(op, "slot"));
                    return new JsonObject { ["ok"] = true, ["slot"] = r.Slot, ["version"] = (double)r.Version, ["bytes"] = (double)r.Bytes };
                }
                case "load": return core.Load(S(op, "slot"));
                case "startQuest": return core.StartQuest(S(op, "questId"), (bool)op["force"]);
                case "addXp": return core.AddXp(D(op["amount"]), S(op, "source"));
                case "addCred": return core.AddCred(D(op["amount"]));
                case "buyPerk": return core.BuyPerk(S(op, "id"));
                case "canBuyPerk": return core.CanBuyPerk(S(op, "id"));
                case "installCyberware": return core.InstallCyberware(S(op, "id"));
                case "removeCyberware": return core.RemoveCyberware(S(op, "id"));
                case "equipItem": return core.EquipItem(S(op, "uid"));
                case "unequipSlot": return core.UnequipSlot(S(op, "slot"));
                case "equipHack": return core.EquipHack(S(op, "uid"), I(op["index"]));
                case "useItem": return core.UseItem(S(op, "uid"));
                case "removeItem": return core.RemoveItem(S(op, "uid"), I(op["qty"]));
                case "dropLoot":
                {
                    (List<Item> items, int money) = core.DropLoot(null, D(op["tier"]), op["count"] is double c ? (int)c : (int?)null, S(op, "source"), null, (bool)op["autoPickup"]);
                    return new JsonObject { ["items"] = items.Cast<object>().ToList(), ["money"] = (double)money };
                }
                case "openVendor": return core.OpenVendor(S(op, "id"));
                case "buyFrom": return core.BuyFrom(S(op, "vendorId"), S(op, "uid"));
                case "sellTo": return core.SellTo(S(op, "vendorId"), S(op, "uid"), I(op["qty"]));
                case "closeVendor": return core.CloseVendor();
                case "uploadQuickhack": return core.UploadQuickhack(I(op["index"]), S(op, "targetId"));
                case "damage": return core.Damage(D(op["amount"]), S(op, "source"));
                case "heal": return core.Heal(D(op["amount"]));
                case "spendStamina": return core.SpendStamina(D(op["amount"]));
                case "respecPerks": return core.RespecPerks();
                case "addMoney": return core.AddMoney(D(op["amount"]), S(op, "reason"));
                case "trackQuest": return core.TrackQuest(S(op, "questId"));
                case "giveStartingKit":
                    core.GiveStartingKit();
                    return null;
                default: throw new InvalidOperationException($"unknown core op {op["op"]}");
            }
        }

        [Test]
        public void WholeSessionsThroughTheCoreAreThePrototypes()
        {
            int sessions = 0;
            foreach (JsonObject suite in Suites("core"))
            {
                string label = S(suite, "label");
                RpgCityMeta city = City((JsonObject)suite["city"]);
                var rec = new Recorder();
                var core = new RpgCore(rec.Emit, (uint)D(suite["seed"]), new MemorySaveStorage(), () => 1700000000000) { AutosaveEnabled = false };
                Same(suite["construction"], rec.Events, label + ".construction");
                rec.Events.Clear();
                int k = 0;
                foreach (JsonObject op in ((List<object>)suite["ops"]).Cast<JsonObject>())
                {
                    string path = $"{label} op {k++} {op["op"]}";
                    if ((string)op["op"] == "snapshot")
                    {
                        Same(op["result"], core.GetSnapshot(), path + ".snapshot");
                        Same(op["serialized"], core.Serialize(), path + ".serialized");
                        continue;
                    }
                    if (op.TryGetValue("throws", out object thrown))
                    {
                        Exception caught = null;
                        try
                        {
                            Run(core, op, city);
                        }
                        catch (Exception e)
                        {
                            caught = e;
                        }
                        Assert.IsNotNull(caught, $"{path}: expected a {thrown}");
                    }
                    else
                    {
                        object result = Run(core, op, city);
                        Same(op["result"], result, path + ".result");
                    }
                    Same(op["events"], rec.Events, path + ".events");
                    rec.Events.Clear();
                }
                sessions++;
            }
            Assert.AreEqual(10, sessions);
        }
    }
}
