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
    /// The RPG's character rules against the prototype's own code: <c>tools/gen-golden-rpg.mjs</c> ran the prototype's StatBlock, PerkTree, CyberwareRig, loot roller,
    /// Inventory, Economy, conditions and saves through seeded scripts of operations and wrote each operation's arguments, result and the state after it. These tests
    /// replay the arguments on the C# port and compare everything, numbers exactly.
    /// </summary>
    public class RpgCharacterGoldenTests
    {
        static List<JsonObject> _suites;

        static IEnumerable<JsonObject> Suites(string name, [CallerFilePath] string sourceFile = "")
        {
            if (_suites == null)
            {
                _suites = new List<JsonObject>();
                foreach (string line in File.ReadAllLines(Path.Combine(Path.GetDirectoryName(sourceFile), "Fixtures", "rpg_character_golden.jsonl")))
                {
                    if (line.Length > 0) _suites.Add((JsonObject)Json.Parse(line));
                }
            }
            List<JsonObject> found = _suites.FindAll(s => (string)s["suite"] == name);
            Assert.IsNotEmpty(found, $"no '{name}' suite in the fixture");
            return found;
        }

        // ---------------------------------------------------------------- comparison

        /// <summary>A C# value in the fixture's shape: items to uids, perks and chrome to ids, infinities to null, integers to doubles.</summary>
        static object Norm(object v)
        {
            switch (v)
            {
                case null: return null;
                case Item item: return item.Uid;
                case Perk perk: return perk.Id;
                case CyberwareDef ware: return ware.Id;
                case RpgResult r: return Norm(r.ToJson());
                case double d: return double.IsInfinity(d) || double.IsNaN(d) ? null : (object)d;
                case int i: return (double)i;
                case JsonObject o:
                {
                    var n = new JsonObject();
                    foreach (KeyValuePair<string, object> kv in o) n[kv.Key] = Norm(kv.Value);
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
                    if (missing.Count > 0 || extra.Count > 0) Assert.Fail($"{path}: keys differ; missing [{string.Join(", ", missing)}], extra [{string.Join(", ", extra)}]");
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
                return s.Length > 400 ? s.Substring(0, 400) + "…" : s;
            }
            catch (ArgumentException)
            {
                return v?.ToString() ?? "null";
            }
        }

        /// <summary>Run an operation the way the fixture recorded it: a result, or the error it threw (TypeError and RangeError are argument errors here; Error is invalid state).</summary>
        static void Expect(JsonObject op, Func<object> run, string path)
        {
            if (op.TryGetValue("throws", out object thrown))
            {
                Exception caught = null;
                try
                {
                    run();
                }
                catch (Exception e)
                {
                    caught = e;
                }
                Assert.IsNotNull(caught, $"{path}: expected a {thrown}, nothing was thrown");
                if ((string)thrown == "Error") Assert.IsInstanceOf<InvalidOperationException>(caught, $"{path}: {caught}");
                else Assert.IsInstanceOf<ArgumentException>(caught, $"{path}: {caught}");
                return;
            }
            object result = run();
            if (op.ContainsKey("result")) Same(op["result"], result, path + ".result");
        }

        static Dictionary<string, int> Attrs(object o)
        {
            var d = new Dictionary<string, int>();
            foreach (KeyValuePair<string, object> kv in (JsonObject)o) d[kv.Key] = (int)(double)kv.Value;
            return d;
        }

        static List<StatModifier> Mods(object o) => ((List<object>)o).Select(StatModifier.FromJson).ToList();

        static JsonObject XpJson(XpGain g, bool withTotal)
        {
            var o = new JsonObject { ["gained"] = g.Gained, ["levels"] = g.Levels.Select(l => (object)(double)l).ToList(), ["level"] = (double)g.Level };
            if (withTotal) o["total"] = g.Total;
            return o;
        }

        static List<object> ModList(IEnumerable<StatModifier> mods) => mods.Select(m => (object)m.ToJson()).ToList();

        // ---------------------------------------------------------------- curves and derived stats

        [Test]
        public void TheXpAndCredCurvesAreThePrototypes()
        {
            JsonObject c = Suites("curves").Single();
            var xp = (List<object>)c["xpToNext"];
            var total = (List<object>)c["totalXpForLevel"];
            for (int l = 1; l <= RpgStats.LevelCap; l++)
            {
                Same(xp[l - 1], RpgStats.XpToNext(l), $"xpToNext({l})");
                Same(total[l - 1], RpgStats.TotalXpForLevel(l), $"totalXpForLevel({l})");
            }
            var cred = (List<object>)c["credToNext"];
            for (int l = 1; l <= RpgStats.CredCap; l++) Same(cred[l - 1], RpgStats.CredToNext(l), $"credToNext({l})");
            foreach (List<object> row in ((List<object>)c["levelFromXp"]).Cast<List<object>>())
            {
                (int level, double into, double toNext) = RpgStats.LevelFromXp((double)row[0]);
                Same(new List<object> { row[1], row[2], row[3] }, new List<object> { level, into, toNext }, $"levelFromXp({row[0]})");
            }
            foreach (List<object> row in ((List<object>)c["credFromXp"]).Cast<List<object>>())
            {
                (int level, double into, double toNext) = RpgStats.CredFromXp((double)row[0]);
                Same(new List<object> { row[1], row[2], row[3] }, new List<object> { level, into, toNext }, $"credFromXp({row[0]})");
            }
            var awards = (List<object>)c["awards"];
            for (int l = 1; l <= awards.Count; l++)
            {
                (int a, int p) = RpgStats.LevelUpAwards(l);
                Same(awards[l - 1], new List<object> { a, p }, $"levelUpAwards({l})");
            }
        }

        [Test]
        public void DerivedStatsAndModifiersAreThePrototypes()
        {
            int n = 0;
            foreach (JsonObject s in Suites("derived"))
            {
                Dictionary<string, int> attrs = Attrs(s["attrs"]);
                int level = (int)(double)s["level"];
                DerivedStats b = RpgStats.BaseDerived(attrs, level);
                Same(s["base"], b.ToJson(), $"derived[{n}].base");
                Same(s["applied"], RpgStats.ApplyModifiers(b, Mods(s["mods"])).ToJson(), $"derived[{n}].applied");
                n++;
            }
        }

        // ---------------------------------------------------------------- stat block

        [Test]
        public void AStatBlockLevelsSpendsAndTakesModifiersAsThePrototypes()
        {
            foreach (JsonObject suite in Suites("stats"))
            {
                var stats = new StatBlock(Attrs(suite["attrs"]));
                int i = 0;
                foreach (JsonObject op in ((List<object>)suite["ops"]).Cast<JsonObject>())
                {
                    string path = $"stats {suite["seed"]} op {i++} {op["op"]}";
                    switch ((string)op["op"])
                    {
                        case "addXp": Expect(op, () => XpJson(stats.AddXp((double)op["amount"]), true), path); break;
                        case "addCredXp": Expect(op, () => XpJson(stats.AddCredXp((double)op["amount"]), false), path); break;
                        case "spend": Expect(op, () => stats.SpendAttributePoint((string)op["attr"]), path); break;
                        case "grant": stats.AttributePoints += (int)(double)op["n"]; break;
                        case "setMods":
                            Expect(op, () =>
                            {
                                stats.Modifiers.Set((string)op["source"], Mods(op["mods"]));
                                stats.Invalidate();
                                return null;
                            }, path);
                            break;
                        case "removeMods":
                            Same(op["result"], stats.Modifiers.Remove((string)op["source"]), path);
                            stats.Invalidate();
                            break;
                        default: Assert.Fail($"{path}: unknown op"); break;
                    }
                    var state = new JsonObject
                    {
                        ["serialized"] = stats.Serialize(), ["level"] = stats.Level, ["xpInto"] = stats.XpInto, ["xpToNext"] = stats.XpToNext, ["streetCred"] = stats.StreetCred,
                        ["credInto"] = stats.CredInto, ["credToNext"] = stats.CredToNext, ["derived"] = stats.Derived.ToJson(), ["sources"] = stats.Modifiers.Sources().ToList(),
                    };
                    Same(op["state"], state, path + ".state");
                }
            }
        }

        // ---------------------------------------------------------------- perks and chrome

        [Test]
        public void PerkPurchasesRequirementsAndRespecsAreThePrototypes()
        {
            foreach (JsonObject suite in Suites("perks"))
            {
                var tree = new PerkTree();
                int i = 0;
                foreach (JsonObject op in ((List<object>)suite["ops"]).Cast<JsonObject>())
                {
                    string path = $"perks {suite["seed"]} op {i++} {op["op"]}";
                    var s = (JsonObject)op["stats"];
                    var buyer = new PerkBuyer { Attributes = Attrs(s["attributes"]), Level = (int)(double)s["level"], PerkPoints = (int)(double)s["perkPoints"] };
                    switch ((string)op["op"])
                    {
                        case "canBuy": Expect(op, () => tree.CanBuy((string)op["id"], buyer), path); break;
                        case "buy": Expect(op, () => tree.Buy((string)op["id"], buyer), path); break;
                        case "respec": Expect(op, () => tree.Respec(buyer), path); break;
                        default: Assert.Fail($"{path}: unknown op"); break;
                    }
                    Same(op["perkPointsAfter"], buyer.PerkPoints, path + ".perkPoints");
                    Same(op["serialized"], tree.Serialize(), path + ".serialized");
                    Same(op["abilities"], tree.Abilities(), path + ".abilities");
                    Same(op["mods"], ModList(tree.ApplyTo(new ModifierSet()).All()), path + ".mods");
                }
            }
        }

        [Test]
        public void ChromeSlotsCapacityAndRequirementsAreThePrototypes()
        {
            foreach (JsonObject suite in Suites("cyber"))
            {
                var rig = new CyberwareRig();
                int i = 0;
                foreach (JsonObject op in ((List<object>)suite["ops"]).Cast<JsonObject>())
                {
                    string path = $"cyber {suite["seed"]} op {i++} {op["op"]}";
                    CyberStats stats = null;
                    if (op.TryGetValue("stats", out object so))
                    {
                        var s = (JsonObject)so;
                        stats = new CyberStats
                        {
                            Attributes = Attrs(s["attributes"]), Level = (int)(double)s["level"], StreetCred = (int)(double)s["streetCred"], CyberCapacity = (int)(double)s["cyberCapacity"],
                        };
                    }
                    switch ((string)op["op"])
                    {
                        case "canInstall": Expect(op, () => rig.CanInstall((string)op["id"], stats), path); break;
                        case "install": Expect(op, () => rig.Install((string)op["id"], stats), path); break;
                        case "remove": Expect(op, () => rig.Remove((string)op["id"]), path); break;
                        default: Assert.Fail($"{path}: unknown op"); break;
                    }
                    Same(op["serialized"], rig.Serialize(), path + ".serialized");
                    Same(op["used"], rig.UsedCapacity(), path + ".used");
                    Same(op["bySlot"], rig.BySlot(), path + ".bySlot");
                    Same(op["abilities"], rig.Abilities(), path + ".abilities");
                    Same(op["mods"], ModList(rig.ApplyTo(new ModifierSet()).All()), path + ".mods");
                }
            }
        }

        // ---------------------------------------------------------------- loot

        static LootRoll Roll(JsonObject o) => new LootRoll
        {
            Level = o.TryGetValue("level", out object l) ? (double)l : 1, Luck = o.TryGetValue("luck", out object k) ? (double)k : 0,
            Tier = o.TryGetValue("tier", out object t) ? (double)t : 1, Type = o.TryGetValue("type", out object ty) ? (string)ty : null,
            TemplateId = o.TryGetValue("templateId", out object id) ? (string)id : null, Rarity = o.TryGetValue("rarity", out object r) ? (string)r : null,
        };

        [Test]
        public void TheLootRollerGivesTheSameItemsFromTheSameSeed()
        {
            int checkedItems = 0;
            foreach (JsonObject suite in Suites("loot"))
            {
                var gen = new LootGenerator((uint)(double)suite["seed"]);
                int i = 0;
                foreach (JsonObject op in ((List<object>)suite["ops"]).Cast<JsonObject>())
                {
                    string path = $"loot {suite["seed"]} op {i++} {op["op"]}";
                    switch ((string)op["op"])
                    {
                        case "rollItem":
                            if (op.ContainsKey("throws"))
                            {
                                Expect(op, () => gen.RollItem(Roll((JsonObject)op["opts"])), path);
                                break;
                            }
                            Same(op["item"], gen.RollItem(Roll((JsonObject)op["opts"])).ToJson(), path);
                            checkedItems++;
                            break;
                        case "rollDrop":
                        {
                            var o = (JsonObject)op["opts"];
                            (List<Item> items, int money) = gen.RollDrop((double)o["level"], (double)o["luck"], (double)o["tier"],
                                o.TryGetValue("count", out object c) ? (int)(double)c : (int?)null, o.TryGetValue("moneyBase", out object mb) ? (double)mb : 40);
                            Same(op["items"], items.Select(x => (object)x.ToJson()).ToList(), path + ".items");
                            Same(op["money"], money, path + ".money");
                            checkedItems += items.Count;
                            break;
                        }
                        case "reset": gen.Reset(); break;
                        case "uid": Same(op["uid"], gen.Uid(), path); break;
                        default: Assert.Fail($"{path}: unknown op"); break;
                    }
                }
            }
            Assert.Greater(checkedItems, 300, "a good number of items compared");
            JsonObject cyber = Suites("cyberItems").Single();
            var expected = (List<object>)cyber["items"];
            for (int i = 0; i < RpgCyberware.All.Count; i++) Same(expected[i], RpgItems.MakeCyberwareItem(RpgCyberware.All[i].Id, "cw" + i).ToJson(), $"cyberItem {i}");
        }

        // ---------------------------------------------------------------- inventory

        static JsonObject InventoryState(Inventory inv, bool full)
        {
            JsonObject ser = inv.Serialize();
            if (!full) ser["items"] = inv.Items.Select(x => (object)new List<object> { x.Uid, (double)x.Qty }).ToList();
            var counts = new JsonObject();
            foreach (string t in new[] { "c_maxdoc", "j_scrap", "c_stim" }) counts[t] = (double)inv.Count(t);
            return new JsonObject
            {
                ["full"] = full, ["serialized"] = ser, ["weight"] = inv.Weight, ["free"] = inv.Free, ["overweight"] = inv.Overweight, ["size"] = inv.Size,
                ["consumables"] = inv.ByType("consumable").Select(x => (object)x.Uid).ToList(), ["deck"] = inv.DeckItems().Select(x => (object)x?.Uid).ToList(),
                ["equippedModifiers"] = inv.EquippedModifiers().Select(e => (object)new JsonObject { ["source"] = e.Source, ["mods"] = ModList(e.Mods) }).ToList(),
                ["counts"] = counts,
            };
        }

        [Test]
        public void TheInventoryStacksWeighsEquipsAndSlotsHacksAsThePrototypes()
        {
            foreach (JsonObject suite in Suites("inventory"))
            {
                var init = (JsonObject)suite["init"];
                var inv = new Inventory((double)init["capacity"], (int)(double)init["deckSize"]);
                int i = 0;
                foreach (JsonObject op in ((List<object>)suite["ops"]).Cast<JsonObject>())
                {
                    string path = $"inventory {suite["seed"]} op {i++} {op["op"]}";
                    switch ((string)op["op"])
                    {
                        case "add": Expect(op, () => inv.Add(Item.FromJson((JsonObject)op["item"])), path); break;
                        case "remove": Expect(op, () => inv.Remove((string)op["uid"], (int)(double)op["qty"]), path); break;
                        case "removeByTemplate": Expect(op, () => inv.RemoveByTemplate((string)op["templateId"], (int)(double)op["qty"]), path); break;
                        case "equip": Expect(op, () => inv.Equip((string)op["uid"]), path); break;
                        case "unequip": Expect(op, () => inv.Unequip((string)op["slot"]), path); break;
                        case "equipHack": Expect(op, () => inv.EquipHack((string)op["uid"], (int)(double)op["index"]), path); break;
                        case "unequipHack": Expect(op, () => inv.UnequipHack((int)(double)op["index"]), path); break;
                        case "setDeckSize":
                            Expect(op, () =>
                            {
                                inv.SetDeckSize((int)(double)op["n"]);
                                return null;
                            }, path);
                            break;
                        default: Assert.Fail($"{path}: unknown op"); break;
                    }
                    var state = (JsonObject)op["state"];
                    Same(state, InventoryState(inv, (bool)state["full"]), path + ".state");
                }
            }
        }

        // ---------------------------------------------------------------- economy

        sealed class StubView : IConditionView
        {
            public Func<int> LevelOf, CredOf;
            public Func<double> MoneyOf;
            public Func<string, int> AttributeOf;
            public Func<string, object> FlagOf;
            public Func<string, int> CountOf;
            public int Level => LevelOf();
            public int StreetCred => CredOf();
            public double Money => MoneyOf();
            public int Attribute(string name) => AttributeOf(name);
            public Func<string, int, bool> PerkOf = (id, r) => false;
            public Func<string, bool> CyberOf = id => false, AbilityOf = t => false;
            public Func<string, QuestStateView> QuestOf = id => new QuestStateView("inactive", null, null);
            public bool HasPerk(string id, int rank) => PerkOf(id, rank);
            public bool HasCyberware(string id) => CyberOf(id);
            public bool HasAbility(string tag) => AbilityOf(tag);
            public object Flag(string name) => FlagOf(name);
            public int CountItem(string templateId) => CountOf(templateId);
            public QuestStateView QuestState(string id) => QuestOf(id);
        }

        sealed class StubHost : IEconomyHost
        {
            public readonly List<object> Events = new List<object>();
            public StatBlock Stats;
            public readonly Dictionary<string, object> Flags = new Dictionary<string, object>();
            public Inventory Inv;
            public IConditionView ViewOf;
            int _uid;

            public void Emit(string type, JsonObject payload) => Events.Add(new JsonObject { ["type"] = type, ["payload"] = Norm(payload) });
            public Inventory Inventory => Inv;
            public DerivedStats Derived => Stats.Derived;
            public object GetFlag(string flag) => Flags.TryGetValue(flag, out object v) ? v : null;
            public int Level => Stats.Level;
            public string NextUid() => "u" + ++_uid;
            public IConditionView View => ViewOf;
        }

        [Test]
        public void VendorsStockPricesAndTradesAreThePrototypes()
        {
            foreach (JsonObject suite in Suites("economy"))
            {
                var host = new StubHost
                {
                    Stats = new StatBlock(Attrs(suite["attrs"]), (double)suite["xp"], (double)suite["credXp"]),
                    Inv = new Inventory((double)suite["capacity"]),
                };
                var economy = new Economy(host, (int)(double)suite["money"]);
                host.ViewOf = new StubView
                {
                    LevelOf = () => host.Stats.Level, CredOf = () => host.Stats.StreetCred, MoneyOf = () => economy.Money, AttributeOf = n => host.Stats.Attributes[n],
                    FlagOf = n => host.GetFlag(n), CountOf = t => host.Inv.Count(t),
                };
                foreach (object it in (List<object>)suite["start"]) Assert.IsTrue(host.Inv.Add(Item.FromJson((JsonObject)it)).Ok);

                int i = 0;
                foreach (JsonObject op in ((List<object>)suite["ops"]).Cast<JsonObject>())
                {
                    string path = $"economy {suite["seed"]} op {i++} {op["op"]}";
                    string vendorId = op.TryGetValue("vendorId", out object v) ? (string)v : null;
                    switch ((string)op["op"])
                    {
                        case "setFlag": host.Flags[(string)op["name"]] = op["value"]; break;
                        case "setAttr":
                            host.Stats.Attributes[(string)op["attr"]] = (int)(double)op["value"];
                            host.Stats.Invalidate();
                            break;
                        case "open": Expect(op, () => economy.Open(vendorId), path); break;
                        case "close": Expect(op, () => economy.Close(), path); break;
                        case "buy": Expect(op, () => economy.Buy(vendorId, (string)op["uid"]), path); break;
                        case "sell": Expect(op, () => economy.Sell(vendorId, (string)op["uid"], (int)(double)op["qty"]), path); break;
                        case "tick":
                            Expect(op, () =>
                            {
                                economy.Tick((double)op["hours"]);
                                return null;
                            }, path);
                            break;
                        case "addMoney": Expect(op, () => economy.AddMoney((double)op["amount"], "test"), path); break;
                        case "restock": Expect(op, () => economy.Restock(vendorId, (bool)op["force"]), path); break;
                        default: Assert.Fail($"{path}: unknown op"); break;
                    }
                    Same(op["events"], host.Events, path + ".events");
                    host.Events.Clear();

                    var expected = (JsonObject)op["state"];
                    bool full = (bool)expected["full"];
                    JsonObject eco = economy.Serialize();
                    JsonObject inv = host.Inv.Serialize();
                    if (!full)
                    {
                        foreach (JsonObject vs in ((List<object>)eco["vendors"]).Cast<JsonObject>())
                        {
                            VendorState st = economy.State((string)vs["id"]);
                            vs["stock"] = st.Stock.Select(x => (object)new List<object> { x.Uid, (double)x.Qty }).ToList();
                        }
                        inv["items"] = host.Inv.Items.Select(x => (object)new List<object> { x.Uid, (double)x.Qty }).ToList();
                    }
                    var state = new JsonObject
                    {
                        ["full"] = full, ["economy"] = eco, ["inventory"] = inv, ["snapshot"] = economy.Snapshot((string)expected["vendorId"]),
                        ["vendorId"] = expected["vendorId"], ["open"] = economy.OpenVendorId,
                    };
                    Same(expected, state, path + ".state");
                }
            }
        }

        // ---------------------------------------------------------------- conditions and saves

        [Test]
        public void ConditionsAndTheirLabelsAreThePrototypes()
        {
            JsonObject suite = Suites("conditions").Single();
            var data = (JsonObject)suite["view"];
            var attrs = (JsonObject)data["attributes"];
            var perks = (JsonObject)data["perks"];
            var cyber = (List<object>)data["cyberware"];
            var abilities = (List<object>)data["abilities"];
            var flags = (JsonObject)data["flags"];
            var items = (JsonObject)data["items"];
            var quests = (JsonObject)data["quests"];
            var view = new StubView
            {
                LevelOf = () => (int)(double)data["level"], CredOf = () => (int)(double)data["streetCred"], MoneyOf = () => (double)data["money"],
                AttributeOf = n => (int)(double)attrs[n], FlagOf = n => flags.TryGetValue(n, out object f) ? f : null,
                CountOf = t => items.TryGetValue(t, out object c) ? (int)(double)c : 0,
                PerkOf = (id, rank) => (perks.TryGetValue(id, out object r) ? (double)r : 0) >= rank,
                CyberOf = id => cyber.Contains(id), AbilityOf = t => abilities.Contains(t),
                QuestOf = id =>
                {
                    if (!quests.TryGetValue(id, out object q)) return new QuestStateView("inactive", null, null);
                    var qo = (JsonObject)q;
                    return new QuestStateView(qo["state"] as string, qo["stage"] as string, qo["outcome"] as string);
                },
            };
            int i = 0;
            foreach (JsonObject c in ((List<object>)suite["cases"]).Cast<JsonObject>())
            {
                string path = $"condition {i++} {Show(c["cond"])}";
                Expect(c, () =>
                {
                    (bool ok, List<string> missing) = RpgConditions.Check(c["cond"], view);
                    return new JsonObject { ["ok"] = ok, ["missing"] = missing };
                }, path);
                if (c["describe"] is JsonObject thrown)
                {
                    Assert.Throws(Is.InstanceOf<Exception>(), () => RpgConditions.Describe(c["cond"]), path + ".describe");
                }
                else
                {
                    Same(c["describe"], RpgConditions.Describe(c["cond"]), path + ".describe");
                }
            }
        }

        static JsonObject Clone(object o) => (JsonObject)Json.Parse(Json.Write(o, false));

        [Test]
        public void SavesMigrateListAndRefuseAsThePrototypes()
        {
            JsonObject suite = Suites("save").Single();
            int i = 0;
            foreach (JsonObject c in ((List<object>)suite["cases"]).Cast<JsonObject>())
            {
                Expect(c, () => RpgSave.Migrate(Clone(c["input"])), $"migrate {i++} {Show(c["input"])}");
            }

            var storage = new MemorySaveStorage();
            double now = 1700000000000;
            var mgr = new SaveManager(storage, () => now);
            foreach (JsonObject step in ((List<object>)suite["steps"]).Cast<JsonObject>())
            {
                string path = $"save step {step["op"]} {(step.TryGetValue("slot", out object sl) ? sl : "")}";
                switch ((string)step["op"])
                {
                    case "write":
                    {
                        (string slot, int version, int bytes) = mgr.Write((string)step["slot"], Clone(step["payload"]));
                        Same(step["result"], new JsonObject { ["ok"] = true, ["slot"] = slot, ["version"] = version, ["bytes"] = bytes }, path);
                        now += 5000;
                        if ((string)step["slot"] == "two")
                        {
                            storage.SetItem("nightsector:save:bad", "{oops");
                            storage.SetItem("other:key", "{}");
                        }
                        break;
                    }
                    case "list":
                    {
                        var list = mgr.List().Select(s =>
                        {
                            var o = new JsonObject { ["slot"] = s.Slot, ["version"] = s.Version, ["savedAt"] = s.SavedAt };
                            if (s.Corrupt) o["corrupt"] = true;
                            else o["level"] = s.Level;
                            return (object)o;
                        }).ToList();
                        Same(step["result"], list, path);
                        break;
                    }
                    case "read": Expect(step, () => mgr.Read((string)step["slot"]), path); break;
                    case "has": Same(step["result"], mgr.Has((string)step["slot"]), path); break;
                    case "remove": mgr.Remove((string)step["slot"]); break;
                    case "raw": Same(step["result"], storage.GetItem(mgr.Key((string)step["slot"])), path); break;
                    default: Assert.Fail($"{path}: unknown step"); break;
                }
            }
        }
    }
}
