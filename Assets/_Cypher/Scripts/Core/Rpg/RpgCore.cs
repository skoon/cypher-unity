using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>A timed buff: a consumable's, or a daemon's (ICEPICK, GHOSTWALK).</summary>
    public sealed class RpgBuff
    {
        public string Id, Label;
        public double TimeLeft;
        public List<StatModifier> Mods = new List<StatModifier>();
        public JsonObject Data = new JsonObject();
    }

    /// <summary>
    /// The whole RPG as one pure state machine: stats, perks, chrome, inventory, jobs, conversations, breaches, vendors and saves. A port of the prototype's
    /// <c>rpg/Core.js</c> (with the public contract of <c>rpg/Systems.js</c>): no engine, no UI. The game ticks it at 60 Hz with the player's position and draws what
    /// it reports; every change is announced as an event (<c>rpg:…</c>, with a JSON payload as the prototype's), and every write returns an <see cref="RpgResult"/>
    /// for a rule that says no, or throws for a programmer's error.
    /// </summary>
    public sealed class RpgCore : IEconomyHost, IQuestHost, IDialogueHost, IConditionView
    {
        public const double AutosaveSeconds = 120;
        const int EffectDepthLimit = 32;

        static JsonObject J(string json) => (JsonObject)Json.Parse(json);

        /// <summary>The buffs a daemon's reward installs.</summary>
        static readonly Dictionary<string, (StatModifier[] Mods, string Data, string Label)> DaemonBuffs = new Dictionary<string, (StatModifier[], string, string)>
        {
            ["icepick"] = (new StatModifier[0], "{\"discount\":1}", "ICEPICK — uploads discounted"),
            ["ghostwalk"] = (new[] { StatModifier.Add("stealth", 25) }, "{}", "GHOSTWALK — presence rewritten"),
        };

        readonly Action<string, JsonObject> _emit;
        public uint Seed { get; private set; }
        int _uidSeq;
        int _effectDepth;
        int _suspendStatEvents;

        public StatBlock Stats { get; private set; }
        public PerkTree Perks { get; private set; }
        public CyberwareRig Cyber { get; private set; }
        public Inventory Inventory { get; private set; }
        public readonly QuickhackDeck Deck = new QuickhackDeck();
        public LootGenerator Gen { get; private set; }

        readonly List<string> _flagOrder = new List<string>();
        readonly Dictionary<string, object> _flags = new Dictionary<string, object>();
        readonly List<RpgBuff> _buffs = new List<RpgBuff>();
        readonly Dictionary<string, RpgLocation> _locations = new Dictionary<string, RpgLocation>();
        readonly List<RpgLocation> _locationOrder = new List<RpgLocation>();
        readonly List<(string Subnet, string Effect, double TimeLeft)> _subnets = new List<(string, string, double)>();

        public readonly QuestLog Quests;
        public readonly DialogueRunner Dialogue;
        public readonly Economy Economy;
        public readonly SaveManager Saves;

        public BreachSession Breach { get; private set; }
        public double Health, Stamina, GameTime;
        public double PlayerX, PlayerY, PlayerZ;
        public double AutosaveTimer = AutosaveSeconds;
        public bool AutosaveEnabled = true;

        /// <summary>What the game adds to a save about the world outside the rules (where the player stands), or null for nothing. Not in the prototype's saves.</summary>
        public Func<JsonObject> CaptureWorld;

        /// <summary>The world part of the last save loaded, or null.</summary>
        public JsonObject LoadedWorld { get; private set; }
        (double Health, double Stamina, double Ram) _lastVitals = (-1, -1, -1);
        List<(string Key, string Kind, string Id, string Label)> _interactions;

        /// <param name="emit">Where events go (type, payload).</param>
        /// <param name="storage">Where saves go; in memory when null.</param>
        /// <param name="clock">Milliseconds since the epoch for save stamps; zero when null.</param>
        public RpgCore(Action<string, JsonObject> emit = null, uint seed = 20771, ISaveStorage storage = null, Func<double> clock = null,
            IReadOnlyList<JsonObject> quests = null, IReadOnlyList<JsonObject> dialogues = null, IReadOnlyList<VendorDef> vendors = null)
        {
            _emit = emit ?? ((t, p) => { });
            Seed = seed;
            Stats = new StatBlock(new Dictionary<string, int> { ["body"] = 4, ["reflexes"] = 4, ["intelligence"] = 4, ["technical"] = 4, ["cool"] = 4 },
                attributePoints: 7, perkPoints: 1);
            Perks = new PerkTree();
            Cyber = new CyberwareRig();
            Inventory = new Inventory(100, 4);
            Gen = new LootGenerator(Seed, nextUid: NextUid);
            Quests = new QuestLog(this, quests);
            Dialogue = new DialogueRunner(this, dialogues);
            Economy = new Economy(this, 1250, vendors);
            Saves = new SaveManager(storage ?? new MemorySaveStorage(), clock ?? (() => 0));
            Health = 1;
            Stamina = 1;

            Recompute(true);
            Health = Derived["maxHealth"];
            Stamina = Derived["maxStamina"];
            Deck.Fill();

            // jobs marked autoStart open at once, so the journal is never empty
            foreach (JsonObject def in Quests.Defs)
            {
                if (RpgJs.Truthy(def.TryGetValue("autoStart", out object a) ? a : null)) Quests.Start((string)def["id"]);
            }
        }

        // ================================================================ plumbing

        /// <summary>
        /// Counts what the core has announced. Every change of state is followed by an event, so this and <see cref="QuestLog.Revision"/> tell a view built from the core when it
        /// has gone stale (<see cref="JobMarkers"/>, <see cref="TrackedJob"/>).
        /// </summary>
        public int Revision { get; private set; }

        public void Emit(string type, JsonObject payload)
        {
            Revision++;
            _emit(type, payload);
        }

        public void Notify(string text, string kind = "info") => Emit("rpg:notify", new JsonObject { ["text"] = text, ["kind"] = kind });

        public string NextUid() => "it" + ++_uidSeq;

        public DerivedStats Derived => Stats.Derived;

        /// <summary>The ability tags from perks and chrome.</summary>
        public List<string> Abilities
        {
            get
            {
                var out_ = new List<string>(Perks.Abilities());
                foreach (string a in Cyber.Abilities()) if (!out_.Contains(a)) out_.Add(a);
                return out_;
            }
        }

        public IConditionView View => this;

        public (bool Ok, List<string> Missing) Check(object condition) => RpgConditions.Check(condition, this);

        // condition view
        public int Level => Stats.Level;
        public int StreetCred => Stats.StreetCred;
        public double Money => Economy.Money;
        public int Attribute(string name) => Stats.Attributes.TryGetValue(name, out int v) ? v : 0;
        public bool HasPerk(string id, int rank) => Perks.RankOf(id) >= rank;
        public bool HasCyberware(string id) => Cyber.Has(id);
        public bool HasAbility(string tag) => Abilities.Contains(tag);
        public object Flag(string name) => _flags.TryGetValue(name, out object v) ? v : null;
        public int CountItem(string templateId) => Inventory.Count(templateId);
        public QuestStateView QuestState(string id)
        {
            QuestRuntime st = Quests.GetState(id);
            return new QuestStateView(st.State, st.Stage, st.Outcome);
        }

        // hosts
        public IReadOnlyDictionary<string, RpgLocation> Locations => _locations;
        public object GetFlag(string flag) => Flag(flag);

        /// <summary>Rebuild the modifiers from every source (perks, chrome, equipment, buffs) and derive the stats again.</summary>
        DerivedStats Recompute(bool silent = false)
        {
            ModifierSet mods = Stats.Modifiers;
            Perks.ApplyTo(mods);
            Cyber.ApplyTo(mods);
            foreach (string source in mods.Sources())
            {
                if (source.StartsWith("item:", StringComparison.Ordinal) || source.StartsWith("buff:", StringComparison.Ordinal)) mods.Remove(source);
            }
            foreach ((string source, IReadOnlyList<StatModifier> list) in Inventory.EquippedModifiers()) mods.Set(source, list);
            foreach (RpgBuff buff in _buffs) if (buff.Mods.Count > 0) mods.Set("buff:" + buff.Id, buff.Mods);
            Stats.Invalidate();
            DerivedStats d = Stats.Derived;

            Inventory.Capacity = d["carryCapacity"];
            Inventory.SetDeckSize((int)Math.Max(3, Math.Min(8, 2 + Math.Floor(d["maxRam"] / 3))));
            Deck.SetMaxRam(d["maxRam"]);
            Health = Math.Min(Health, d["maxHealth"]);
            Stamina = Math.Min(Stamina, d["maxStamina"]);

            if (!silent && _suspendStatEvents == 0) Emit("rpg:statsChanged", GetStats());
            return d;
        }

        // ================================================================ the world

        /// <summary>Put the named places on the city.</summary>
        public IReadOnlyDictionary<string, RpgLocation> SetCity(RpgCityMeta city)
        {
            _locations.Clear();
            _locationOrder.Clear();
            foreach (RpgLocation loc in RpgLocations.Build(city, Seed ^ 0x5f37))
            {
                _locations[loc.Key] = loc;
                _locationOrder.Add(loc);
            }
            Emit("rpg:locationsReady", new JsonObject { ["locations"] = LocationListJson() });
            Quests.NotifyStateChanged();
            return _locations;
        }

        public IReadOnlyList<RpgLocation> GetLocationList() => _locationOrder;

        List<object> LocationListJson() => _locationOrder.Select(l => (object)l.ToJson()).ToList();

        /// <summary>A place by key, or null while the city is not set. An unknown key throws: that is a content bug.</summary>
        public RpgLocation GetLocation(string key)
        {
            if (!RpgLocations.IsKey(key)) throw new ArgumentOutOfRangeException(nameof(key), $"Unknown location key \"{key}\"");
            return _locations.TryGetValue(key, out RpgLocation l) ? l : null;
        }

        public void SetPlayerPosition(double x, double y, double z)
        {
            PlayerX = x;
            PlayerY = y;
            PlayerZ = z;
        }

        // ================================================================ progression

        public XpGain AddXp(double amount, string source = "world")
        {
            XpGain res = Stats.AddXp(amount);
            if (res.Gained == 0) return res;
            Emit("rpg:xpGained", new JsonObject
            {
                ["amount"] = res.Gained, ["total"] = res.Total, ["source"] = source, ["level"] = (double)res.Level, ["into"] = Stats.XpInto, ["toNext"] = Stats.XpToNext,
            });
            foreach (int level in res.Levels)
            {
                Emit("rpg:levelUp", new JsonObject
                {
                    ["level"] = (double)level, ["attributePoints"] = (double)Stats.AttributePoints, ["perkPoints"] = (double)Stats.PerkPoints, ["capped"] = level >= RpgStats.LevelCap,
                });
                Notify($"Level {level}", "level");
            }
            if (res.Levels.Count > 0)
            {
                Health = Derived["maxHealth"];
                Recompute();
            }
            return res;
        }

        public XpGain AddCred(double amount)
        {
            XpGain res = Stats.AddCredXp(amount);
            if (res.Gained == 0) return res;
            Emit("rpg:credGained", new JsonObject { ["amount"] = res.Gained, ["level"] = (double)res.Level, ["into"] = Stats.CredInto, ["toNext"] = Stats.CredToNext });
            foreach (int level in res.Levels)
            {
                Emit("rpg:streetCred", new JsonObject { ["level"] = (double)level });
                Notify($"Street cred {level}", "cred");
            }
            return res;
        }

        public RpgResult AddMoney(double amount, string reason = "script") => Economy.AddMoney(amount, reason);

        public RpgResult SpendAttributePoint(string attr)
        {
            RpgResult res = Stats.SpendAttributePoint(attr);
            if (res.Ok)
            {
                Recompute();
                Emit("rpg:attributeChanged", new JsonObject { ["attribute"] = attr, ["value"] = (double)res.Value.Value, ["remaining"] = (double)res.Remaining.Value });
            }
            return res;
        }

        PerkBuyer Buyer() => new PerkBuyer { Attributes = Stats.Attributes, Level = Stats.Level, PerkPoints = Stats.PerkPoints };

        public RpgResult CanBuyPerk(string id) => Perks.CanBuy(id, Buyer());

        public RpgResult BuyPerk(string id)
        {
            PerkBuyer buyer = Buyer();
            RpgResult res = Perks.Buy(id, buyer);
            if (!res.Ok) return res;
            Stats.PerkPoints = buyer.PerkPoints;
            Recompute();
            Emit("rpg:perkUnlocked", new JsonObject
            {
                ["perk"] = new JsonObject { ["id"] = id, ["name"] = res.Perk.Name, ["tree"] = res.Perk.Tree }, ["rank"] = (double)res.Rank.Value,
                ["perkPoints"] = (double)Stats.PerkPoints,
            });
            Notify($"{res.Perk.Name} {(res.Rank > 1 ? $"rank {res.Rank}" : "")}".Trim(), "perk");
            return res;
        }

        public int RespecPerks()
        {
            PerkBuyer buyer = Buyer();
            int refund = Perks.Respec(buyer);
            Stats.PerkPoints = buyer.PerkPoints;
            Recompute();
            Emit("rpg:perksReset", new JsonObject { ["refunded"] = (double)refund, ["perkPoints"] = (double)Stats.PerkPoints });
            return refund;
        }

        // ================================================================ cyberware

        CyberStats ChromeStats() => new CyberStats
        {
            Attributes = Stats.Attributes, Level = Stats.Level, StreetCred = Stats.StreetCred, CyberCapacity = (int)Derived["cyberCapacity"],
        };

        public RpgResult CanInstallCyberware(string id) => Cyber.CanInstall(id, ChromeStats());

        /// <summary>Install chrome; a matching carried item, if there is one, is used up.</summary>
        public RpgResult InstallCyberware(string id)
        {
            RpgResult res = Cyber.Install(id, ChromeStats());
            if (!res.Ok) return res;
            Item carried = Inventory.Items.Find(i => i.CyberwareId == id);
            if (carried != null) Inventory.Remove(carried.Uid, 1);
            Recompute();
            Emit("rpg:cyberwareInstalled", new JsonObject
            {
                ["ware"] = res.Ware, ["slot"] = res.Ware.Slot, ["used"] = (double)Cyber.UsedCapacity(), ["capacity"] = Derived["cyberCapacity"],
            });
            Notify($"{res.Ware.Name} installed", "chrome");
            return res;
        }

        /// <summary>Take chrome out; it goes back into the inventory if there is room.</summary>
        public RpgResult RemoveCyberware(string id)
        {
            RpgResult res = Cyber.Remove(id);
            if (!res.Ok) return res;
            Recompute();
            Item item = RpgItems.MakeCyberwareItem(id, NextUid());
            RpgResult added = Inventory.Add(item);
            Emit("rpg:cyberwareRemoved", new JsonObject
            {
                ["ware"] = res.Ware, ["slot"] = res.Ware.Slot, ["returned"] = added.Ok, ["used"] = (double)Cyber.UsedCapacity(), ["capacity"] = Derived["cyberCapacity"],
            });
            res.Extra = new JsonObject { ["returned"] = added.Ok };
            return res;
        }

        // ================================================================ inventory and loot

        /// <summary>What a new character starts with (the prototype's <c>STARTING_KIT</c>): template, rarity, quantity, whether it is worn, which deck slot.</summary>
        public static readonly IReadOnlyList<(string TemplateId, string Rarity, int Qty, bool Equip, int? Deck)> StartingKit = new (string, string, int, bool, int?)[]
        {
            ("w_pistol", "uncommon", 1, true, null),
            ("w_baton", "common", 1, true, null),
            ("a_jacket", "common", 1, true, null),
            ("a_boots", "common", 1, true, null),
            ("c_maxdoc", null, 3, false, null),
            ("qh_ping", null, 1, false, 0),
        };

        /// <summary>Hand a new character the starting kit, wear what is worn, slot the quickhack, and fill the RAM.</summary>
        public void GiveStartingKit()
        {
            foreach ((string templateId, string rarity, int qty, bool equip, int? deck) in StartingKit)
            {
                RpgResult res = GiveItem(templateId, rarity, 1, qty);
                if (!res.Ok || res.Item == null) continue;
                if (equip) EquipItem(res.Item.Uid);
                if (deck.HasValue) EquipHack(res.Item.Uid, deck.Value);
            }
            Deck.Fill();
        }

        public RpgResult AddItem(Item item)
        {
            RpgResult res = Inventory.Add(item);
            if (!res.Ok)
            {
                Notify($"Too heavy: {item.Name}", "bad");
                Emit("rpg:itemRejected", new JsonObject { ["item"] = item, ["reason"] = res.Reason });
                return res;
            }
            Emit("rpg:itemAdded", new JsonObject { ["item"] = res.Item ?? item, ["qty"] = (double)item.Qty, ["merged"] = res.Merged == true });
            AfterInventoryChange();
            return res;
        }

        /// <summary>Make an item from a template and give it.</summary>
        public RpgResult GiveItem(string templateId, string rarity = null, double? level = null, double qty = 1)
        {
            RpgItems.Template(templateId);   // a bad id throws rather than rolling nothing
            Item item = RpgItems.MakeItem(Gen.Next, templateId, rarity ?? "common", level ?? Stats.Level, NextUid(), qty);
            return AddItem(item);
        }

        public RpgResult GiveCyberware(string cyberwareId)
        {
            RpgCyberware.Get(cyberwareId);
            return AddItem(RpgItems.MakeCyberwareItem(cyberwareId, NextUid()));
        }

        public RpgResult RemoveItem(string uid, int qty = 1)
        {
            Item item = Inventory.Find(uid);
            RpgResult res = Inventory.Remove(uid, qty);
            if (res.Ok)
            {
                Emit("rpg:itemRemoved", new JsonObject { ["item"] = item, ["qty"] = (double)qty });
                AfterInventoryChange();
            }
            return res;
        }

        public RpgResult RemoveItemByTemplate(string templateId, int qty = 1)
        {
            RpgResult res = Inventory.RemoveByTemplate(templateId, qty);
            if (res.Ok)
            {
                Emit("rpg:itemRemoved", new JsonObject { ["templateId"] = templateId, ["qty"] = (double)qty });
                AfterInventoryChange();
            }
            return res;
        }

        public RpgResult EquipItem(string uid)
        {
            RpgResult res = Inventory.Equip(uid);
            if (!res.Ok) return res;
            Recompute();
            Emit("rpg:itemEquipped", new JsonObject { ["item"] = res.Item, ["slot"] = res.Slot, ["replaced"] = res.Replaced });
            AfterInventoryChange();
            return res;
        }

        public RpgResult UnequipSlot(string slot)
        {
            RpgResult res = Inventory.Unequip(slot);
            if (!res.Ok) return res;
            Recompute();
            Emit("rpg:itemUnequipped", new JsonObject { ["item"] = res.Item, ["slot"] = slot });
            AfterInventoryChange();
            return res;
        }

        public RpgResult EquipHack(string uid, int index)
        {
            RpgResult res = Inventory.EquipHack(uid, index);
            if (res.Ok) Emit("rpg:deckChanged", new JsonObject { ["deck"] = Inventory.DeckItems().Cast<object>().ToList() });
            return res;
        }

        public RpgResult UnequipHack(int index)
        {
            RpgResult res = Inventory.UnequipHack(index);
            if (res.Ok) Emit("rpg:deckChanged", new JsonObject { ["deck"] = Inventory.DeckItems().Cast<object>().ToList() });
            return res;
        }

        /// <summary>Use a consumable: heal, restore RAM or stamina, start a buff.</summary>
        public RpgResult UseItem(string uid)
        {
            Item item = Inventory.Find(uid);
            if (item == null) return RpgResult.Fail("not-found");
            if (item.Use == null) return RpgResult.Fail("not-usable");
            ItemUse u = item.Use;
            if (u.Heal.HasValue && u.Heal.Value != 0) Heal(u.Heal.Value);
            if (u.Ram.HasValue && u.Ram.Value != 0)
            {
                Deck.Ram = Math.Min(Deck.MaxRam, Deck.Ram + u.Ram.Value);
                Emit("rpg:ramChanged", new JsonObject { ["ram"] = Deck.Ram, ["max"] = Deck.MaxRam });
            }
            if (u.Stamina.HasValue && u.Stamina.Value != 0) Stamina = Math.Min(Derived["maxStamina"], Stamina + u.Stamina.Value);
            if (u.Buff != null) AddBuff(u.Buff.Id, item.Name, u.Buff.Duration, u.Buff.Mods?.ToList());
            RemoveItem(uid, 1);
            Emit("rpg:itemUsed", new JsonObject { ["item"] = item });
            return new RpgResult { Ok = true, Item = item };
        }

        /// <summary>Roll a drop and, unless told not to, hand it to the player. Drawn from the core's seeded stream.</summary>
        public (List<Item> Items, int Money) DropLoot(double? level = null, double tier = 1, int? count = null, string source = "world", JsonObject position = null, bool autoPickup = true)
        {
            (List<Item> items, int money) = Gen.RollDrop(level ?? Stats.Level, Derived["lootLuck"], tier, count);
            Emit("rpg:lootDropped", new JsonObject { ["items"] = items.Cast<object>().ToList(), ["money"] = (double)money, ["source"] = source, ["position"] = position });
            if (!autoPickup) return (items, money);
            foreach (Item item in items) AddItem(item);
            if (money != 0) Economy.AddMoney(money, "loot:" + source);
            return (items, money);
        }

        void AfterInventoryChange()
        {
            Emit("rpg:inventoryChanged", GetInventory());
            Quests.NotifyStateChanged();
        }

        // ================================================================ flags and effects

        public object SetFlag(string name, object value = null)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("setFlag: name must be a non-empty string");
            value = value ?? true;
            bool had = _flags.TryGetValue(name, out object previous);
            if (had && RpgJs.StrictEquals(previous, value)) return value;
            if (!had) _flagOrder.Add(name);
            _flags[name] = value;
            var payload = new JsonObject { ["flag"] = name, ["value"] = value };
            if (had) payload["previous"] = previous;
            Emit("rpg:flagSet", payload);
            Quests.NotifyStateChanged();
            return value;
        }

        public object ClearFlag(string name) => SetFlag(name, false);

        public JsonObject GetFlags()
        {
            var o = new JsonObject();
            foreach (string name in _flagOrder) o[name] = _flags[name];
            return o;
        }

        /// <summary>Run a list of effects (see <see cref="RpgEffects"/>). A chain of effects that triggers itself more than 32 deep is a content loop and throws.</summary>
        public void ApplyEffects(List<object> effects, JsonObject context)
        {
            if (effects == null || effects.Count == 0) return;
            context = context ?? new JsonObject();
            _effectDepth += 1;
            if (_effectDepth > EffectDepthLimit)
            {
                _effectDepth = 0;
                throw new InvalidOperationException("applyEffects: effect recursion limit exceeded — a quest or dialogue loops");
            }
            try
            {
                foreach (object effect in effects)
                {
                    JsonObject e = RpgEffects.Assert(effect, "effect from " + Json.Write(context, false));
                    ApplyEffect(e, context);
                }
            }
            finally
            {
                _effectDepth -= 1;
            }
        }

        static object Arg(JsonObject e, string key) => e.TryGetValue(key, out object v) ? v : null;

        void ApplyEffect(JsonObject e, JsonObject ctx)
        {
            string source = Arg(ctx, "questId") as string ?? Arg(ctx, "dialogueId") as string ?? "script";
            switch ((string)e["type"])
            {
                case "setFlag": SetFlag((string)e["flag"], Arg(e, "value") ?? true); break;
                case "clearFlag": SetFlag((string)e["flag"], false); break;
                case "addXp": AddXp(RpgJs.Num(e["amount"], "amount"), source); break;
                case "addCred": AddCred(RpgJs.Num(e["amount"], "amount")); break;
                case "addMoney": Economy.AddMoney(RpgJs.Num(e["amount"], "amount"), source); break;
                case "giveItem":
                case "giveQuickhack":
                    GiveItem((string)e["templateId"], Arg(e, "rarity") as string, Arg(e, "level") as double?, Arg(e, "qty") is double q ? q : 1);
                    break;
                case "giveCyberware": GiveCyberware((string)e["cyberwareId"]); break;
                case "removeItem": RemoveItemByTemplate((string)e["templateId"], Arg(e, "qty") is double rq ? (int)rq : 1); break;
                case "startQuest": StartQuest((string)e["questId"], true); break;
                case "completeObjective": Quests.CompleteObjective((string)e["questId"], (string)e["objectiveId"]); break;
                case "advanceQuest": Quests.Evaluate((string)e["questId"]); break;
                case "failQuest": Quests.Fail((string)e["questId"], (string)e["outcome"]); break;
                case "trackQuest": Quests.SetTracked((string)e["questId"]); break;
                case "notify": Notify((string)e["text"], Arg(e, "kind") as string ?? "info"); break;
                case "startHack": StartBreach((string)e["targetId"]); break;
                case "openVendor": Economy.Open((string)e["vendorId"]); break;
                case "endDialogue":
                    if (Dialogue.Active) Dialogue.End();
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(e), $"applyEffect: unhandled effect type \"{e["type"]}\"");
            }
        }

        // ================================================================ quests

        public RpgResult StartQuest(string id, bool force = false) => Quests.Start(id, force);

        public RpgResult CompleteObjective(string questId, string objectiveId) => Quests.CompleteObjective(questId, objectiveId);

        public RpgResult AdvanceObjective(string questId, string objectiveId, double n = 1) => Quests.AdvanceObjective(questId, objectiveId, n);

        public string TrackQuest(string id) => Quests.SetTracked(id);

        public RpgResult FailQuest(string id, string outcome) => Quests.Fail(id, outcome);

        public void NotifyKill(string tag, double n = 1) => Quests.NotifyKill(tag, n);

        /// <summary>Pay a quest's rewards; returns what was given.</summary>
        public JsonObject GrantRewards(JsonObject rewards, JsonObject meta)
        {
            string questId = Arg(meta, "questId") as string ?? "quest";
            double xp = 0, cred = 0, money = 0, perkPoints = 0, attributePoints = 0;
            var items = new List<object>();
            if (Arg(rewards, "xp") is double rx && rx != 0)
            {
                AddXp(rx, questId);
                xp = rx;
            }
            if (Arg(rewards, "cred") is double rc && rc != 0)
            {
                AddCred(rc);
                cred = rc;
            }
            if (Arg(rewards, "money") is double rm && rm != 0)
            {
                Economy.AddMoney(rm, questId);
                money = rm;
            }
            if (Arg(rewards, "perkPoints") is double rp && rp != 0)
            {
                Stats.PerkPoints += (int)rp;
                perkPoints = rp;
            }
            if (Arg(rewards, "attributePoints") is double ra && ra != 0)
            {
                Stats.AttributePoints += (int)ra;
                attributePoints = ra;
            }
            foreach (JsonObject entry in RpgJs.AsList(Arg(rewards, "items")).Cast<JsonObject>())
            {
                if (Arg(entry, "cyberware") is string ware && ware.Length > 0)
                {
                    GiveCyberware(ware);
                    items.Add(new JsonObject { ["cyberware"] = ware });
                }
                else
                {
                    RpgResult res = GiveItem((string)entry["templateId"], Arg(entry, "rarity") as string, Arg(entry, "level") as double?, Arg(entry, "qty") is double q ? q : 1);
                    items.Add((object)res.Item ?? new JsonObject { ["templateId"] = entry["templateId"] });
                }
            }
            if (Arg(rewards, "flags") is JsonObject flags)
            {
                foreach (KeyValuePair<string, object> kv in flags) SetFlag(kv.Key, kv.Value);
            }
            return new JsonObject
            {
                ["xp"] = xp, ["cred"] = cred, ["money"] = money, ["items"] = items, ["perkPoints"] = perkPoints, ["attributePoints"] = attributePoints,
            };
        }

        // ================================================================ dialogue

        public RpgResult StartDialogue(string graphId, string node = null) => Dialogue.Start(graphId, node);

        public RpgResult ChooseDialogueOption(string optionId) => Dialogue.Choose(optionId);

        public RpgResult EndDialogue() => Dialogue.Active ? Dialogue.End() : RpgResult.Fail("no-dialogue");

        public void OnDialogueEnd(string graphId) => Quests.NotifyTalk(graphId);

        public JsonObject GetDialogueNode() => Dialogue.Current();

        // ================================================================ hacking

        /// <summary>Start a breach. The puzzle's seed comes from the run seed and the target, so a device shows the same matrix within one save.</summary>
        public RpgResult StartBreach(string targetId, uint? seed = null)
        {
            if (Breach != null && !Breach.Done) return RpgResult.Fail("already-breaching");
            BreachTarget target = RpgHacking.Target(targetId);
            DerivedStats d = Derived;
            uint hash = Seed;
            foreach (char c in targetId) hash = unchecked(hash * 31 + c);
            BreachState state = RpgHacking.Generate(seed ?? hash, target.Size, d["breachBuffer"], d["breachTime"] / target.Difficulty, target.Tier, target.Id, target.Name);
            Breach = new BreachSession(state, (daemon, s) => OnDaemonSolved(daemon, target), (result, s) => OnBreachEnd(result, target));
            Emit("rpg:hackStart", new JsonObject { ["session"] = GetBreach(), ["target"] = TargetJson(target) });
            return new RpgResult { Ok = true, Extra = new JsonObject { ["session"] = GetBreach() } };
        }

        static JsonObject TargetJson(BreachTarget t) => new JsonObject
        {
            ["id"] = t.Id, ["name"] = t.Name, ["tier"] = (double)t.Tier, ["size"] = (double)t.Size, ["subnet"] = t.Subnet, ["locationKey"] = t.LocationKey, ["difficulty"] = t.Difficulty,
        };

        public RpgResult BreachSelect(int row, int col)
        {
            if (Breach == null || Breach.Done) return RpgResult.Fail("no-breach");
            var res = Breach.Select(row, col);
            if (!res.Ok) return RpgResult.Fail(res.Reason);
            Emit("rpg:hackSelect", new JsonObject { ["session"] = GetBreach(), ["code"] = res.Code, ["solved"] = res.Solved.Select(x => (object)x.ToJson()).ToList() });
            return new RpgResult { Ok = true, Extra = new JsonObject { ["code"] = res.Code, ["solved"] = res.Solved.Select(x => (object)x.ToJson()).ToList(), ["done"] = res.Done } };
        }

        /// <summary>Upload early with what is in the buffer.</summary>
        public RpgResult BreachUpload() => FinishBreach("uploaded");

        public RpgResult CancelBreach() => FinishBreach("cancelled");

        RpgResult FinishBreach(string reason)
        {
            if (Breach == null || Breach.Done) return RpgResult.Fail("no-breach");
            BreachResult r = Breach.Finish(reason);
            return new RpgResult { Ok = true, Extra = r.ToJson() };
        }

        /// <summary>The breach as the HUD draws it, or null.</summary>
        public JsonObject GetBreach()
        {
            if (Breach == null) return null;
            BreachState s = Breach.S;
            return new JsonObject
            {
                ["targetId"] = s.TargetId, ["targetName"] = s.TargetName, ["size"] = (double)s.Size,
                ["matrix"] = s.Matrix.Select(row => (object)row.Select(c => (object)c).ToList()).ToList(), ["buffer"] = new List<object>(s.Buffer),
                ["bufferSize"] = (double)s.BufferSize, ["axis"] = s.Axis, ["lock"] = (double)s.Lock, ["used"] = new List<object>(s.Used),
                ["picks"] = s.Picks.Select(p => (object)new JsonObject { ["r"] = (double)p.R, ["c"] = (double)p.C, ["code"] = p.Code }).ToList(),
                ["legal"] = Breach.LegalCells().Select(p => (object)new JsonObject { ["r"] = (double)p.R, ["c"] = (double)p.C, ["code"] = p.Code }).ToList(),
                ["timeLeft"] = RpgJs.Round(s.TimeLeft * 100) / 100, ["timeLimit"] = s.TimeLimit, ["state"] = s.State,
                ["daemons"] = s.Daemons.Select(d => (object)new JsonObject
                {
                    ["id"] = d.Id, ["name"] = d.Name, ["desc"] = d.Desc, ["sequence"] = new List<object>(d.Sequence), ["solved"] = d.Solved,
                }).ToList(),
            };
        }

        void OnDaemonSolved(BreachDaemon daemon, BreachTarget target)
        {
            JsonObject r = daemon.Reward;
            string type = (string)r["type"];
            if (type == "money")
            {
                Economy.AddMoney(RpgJs.Num(r["amount"], "amount"), "daemon:" + daemon.Id);
            }
            else if (type == "loot")
            {
                DropLoot(tier: RpgJs.Num(r["tier"], "tier"), count: RpgJs.Int(r["count"], "count"), source: "daemon:" + daemon.Id);
                if (Arg(r, "money") is double m && m != 0) Economy.AddMoney(m, "daemon:" + daemon.Id);
            }
            else if (type == "buff")
            {
                string id = (string)r["id"];
                (StatModifier[] mods, string data, string label) = DaemonBuffs.TryGetValue(id, out var spec) ? spec : (new StatModifier[0], "{}", daemon.Name);
                JsonObject merged = J(data);
                foreach (KeyValuePair<string, object> kv in r) merged[kv.Key] = kv.Value;
                AddBuff(id, label, RpgJs.Num(r["duration"], "duration"), mods.ToList(), merged);
            }
            else if (type == "subnet")
            {
                string effect = (string)r["effect"];
                double duration = RpgJs.Num(r["duration"], "duration");
                int i = _subnets.FindIndex(s => s.Subnet == target.Subnet);
                if (i >= 0) _subnets[i] = (target.Subnet, effect, duration);
                else _subnets.Add((target.Subnet, effect, duration));
                Emit("rpg:subnetEffect", new JsonObject { ["subnet"] = target.Subnet, ["effect"] = effect, ["duration"] = duration });
            }
            SetFlag("daemon_" + daemon.Id, true);
            Emit("rpg:daemonUploaded", new JsonObject { ["daemon"] = new JsonObject { ["id"] = daemon.Id, ["name"] = daemon.Name }, ["target"] = target.Id });
            Notify($"{daemon.Name} uploaded", "hack");
        }

        void OnBreachEnd(BreachResult result, BreachTarget target)
        {
            Emit("rpg:hackEnd", new JsonObject { ["result"] = result.ToJson(), ["target"] = target.Id });
            if (result.Success)
            {
                AddXp(60 + target.Tier * 45, "breach");
                Quests.NotifyHack(target.Id);
                if (target.Id == "garage_locker" && result.Solved.Any(d => d.Id == "camera_shutdown")) SetFlag("chrometax_silent", true);
                if (target.Id == "substation_breaker" && Stats.Attributes["technical"] >= 6) SetFlag("deadair_rerouted", true);
            }
            else
            {
                Notify("Breach failed — connection dropped", "bad");
            }
        }

        /// <summary>Upload the quickhack in deck slot <paramref name="index"/> onto a target.</summary>
        public RpgResult UploadQuickhack(int index, string targetId)
        {
            Item[] deck = Inventory.DeckItems();
            Item item = index >= 0 && index < deck.Length ? deck[index] : null;
            if (item == null) return RpgResult.Fail("empty-slot");
            RpgBuff icepick = _buffs.Find(b => b.Id == "icepick");
            double discount = icepick != null && icepick.Data.TryGetValue("discount", out object dv) && dv is double dd ? dd : 0;
            var res = Deck.Upload(item, targetId, Derived["hackDuration"], discount);
            if (!res.Ok)
            {
                if (res.Reason == "no-ram") Notify("Not enough RAM", "bad");
                var fail = RpgResult.Fail(res.Reason);
                if (res.Cost.HasValue) fail.Extra = new JsonObject { ["cost"] = res.Cost.Value };
                return fail;
            }
            Emit("rpg:ramChanged", new JsonObject { ["ram"] = Deck.Ram, ["max"] = Deck.MaxRam });
            Emit("rpg:quickhackApplied", new JsonObject { ["hack"] = res.Hack.ToJson(), ["targetId"] = targetId, ["cost"] = res.Cost.Value });
            AddXp(12, "quickhack");
            return new RpgResult { Ok = true, Extra = new JsonObject { ["cost"] = res.Cost.Value, ["hack"] = res.Hack.ToJson() } };
        }

        public RpgBuff AddBuff(string id, string label, double duration, List<StatModifier> mods = null, JsonObject data = null)
        {
            if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration), $"addBuff: bad duration for \"{id}\"");
            var buff = new RpgBuff { Id = id, Label = label ?? id, TimeLeft = duration, Mods = mods ?? new List<StatModifier>(), Data = data ?? new JsonObject() };
            int i = _buffs.FindIndex(b => b.Id == id);
            if (i >= 0) _buffs[i] = buff;
            else _buffs.Add(buff);
            Recompute();
            Emit("rpg:buffAdded", new JsonObject { ["id"] = id, ["label"] = buff.Label, ["duration"] = duration });
            return buff;
        }

        public List<JsonObject> GetBuffs() => _buffs.Select(b => new JsonObject
        {
            ["id"] = b.Id, ["label"] = b.Label, ["timeLeft"] = RpgJs.Round(b.TimeLeft * 10) / 10,
        }).ToList();

        // ================================================================ vitals

        public double Damage(double amount, string source = "world")
        {
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "damage: bad amount");
            double mitigated = amount * (100 / (100 + Derived["armor"]));
            Health = Math.Max(0, Health - mitigated);
            Emit("rpg:damaged", new JsonObject { ["amount"] = mitigated, ["raw"] = amount, ["health"] = Health, ["source"] = source });
            if (Health <= 0) Emit("rpg:playerDied", new JsonObject { ["source"] = source });
            return Health;
        }

        public double Heal(double amount)
        {
            Health = Math.Min(Derived["maxHealth"], Health + amount);
            return Health;
        }

        /// <summary>False when there is not enough stamina.</summary>
        public bool SpendStamina(double amount)
        {
            if (Stamina < amount) return false;
            Stamina -= amount;
            return true;
        }

        // ================================================================ vendors

        public RpgResult OpenVendor(string id) => Economy.Open(id);

        public RpgResult CloseVendor() => Economy.Close();

        public RpgResult BuyFrom(string vendorId, string uid)
        {
            RpgResult res = Economy.Buy(vendorId, uid);
            if (res.Ok) AfterInventoryChange();
            return res;
        }

        public RpgResult SellTo(string vendorId, string uid, int qty = 1)
        {
            RpgResult res = Economy.Sell(vendorId, uid, qty);
            if (res.Ok) AfterInventoryChange();
            return res;
        }

        public JsonObject GetVendor(string id)
        {
            Economy.Restock(id);   // nothing unless the shelf is empty or due
            return Economy.Snapshot(id);
        }

        public JsonObject GetOpenVendor() => Economy.OpenVendorId != null ? Economy.Snapshot(Economy.OpenVendorId) : null;

        // ================================================================ interaction

        /// <summary>What there is to do at each place: talk, breach or trade (the first that applies, in that order).</summary>
        List<(string Key, string Kind, string Id, string Label)> Interactions
        {
            get
            {
                if (_interactions != null) return _interactions;
                var list = new List<(string, string, string, string)>();
                bool Has(string key) => list.Any(x => x.Item1 == key);
                foreach (JsonObject g in Dialogue.Graphs)
                {
                    string loc = g.TryGetValue("location", out object l) ? l as string : null;
                    if (!string.IsNullOrEmpty(loc))
                    {
                        int i = list.FindIndex(x => x.Item1 == loc);
                        var entry = (loc, "dialogue", (string)g["id"], $"Talk to {g["title"]}");
                        if (i >= 0) list[i] = entry;
                        else list.Add(entry);
                    }
                }
                foreach (BreachTarget t in RpgHacking.Targets) if (!Has(t.LocationKey)) list.Add((t.LocationKey, "breach", t.Id, $"Breach {t.Name}"));
                foreach (VendorDef v in Economy.Vendors) if (!Has(v.LocationKey)) list.Add((v.LocationKey, "vendor", v.Id, $"Trade with {v.Name}"));
                return _interactions = list;
            }
        }

        /// <summary>What there is to do at a place (talk, breach or trade) and its prompt, or null when nothing; for the scanner.</summary>
        public (string Kind, string Label)? InteractionAt(string locationKey)
        {
            var action = ActionAt(locationKey);
            return action == null ? ((string, string)?)null : (action.Value.Kind, action.Value.Label);
        }

        /// <summary>
        /// What E does at a place. A breach an active job is waiting on comes first, so a place with a conversation as well (the Data Core and Wire) can still be breached
        /// (the prototype always talked there, and its main job could not be finished); otherwise the place's own thing.
        /// </summary>
        (string Kind, string Id, string Label)? ActionAt(string locationKey)
        {
            RefreshViews();
            HashSet<string> wanted = _openHacks;
            foreach (BreachTarget t in RpgHacking.Targets)
            {
                if (t.LocationKey == locationKey && wanted.Contains(t.Id)) return ("breach", t.Id, $"Breach {t.Name}");
            }
            int i = Interactions.FindIndex(x => x.Key == locationKey);
            return i < 0 ? ((string, string, string)?)null : (Interactions[i].Kind, Interactions[i].Id, Interactions[i].Label);
        }

        /// <summary>The nearest thing to do within reach of the player, or null.</summary>
        public JsonObject GetNearbyInteractable(double range = 4.5)
        {
            var nearby = Nearby(range);
            if (nearby == null) return null;
            (RpgLocation location, double distance, var action) = nearby.Value;
            return new JsonObject
            {
                ["locationKey"] = location.Key, ["name"] = location.Name, ["distance"] = RpgJs.Round(distance * 100) / 100, ["kind"] = action.Kind, ["id"] = action.Id,
                ["prompt"] = action.Label,
            };
        }

        /// <summary>What E would do now (kind, id and prompt), or null: <see cref="GetNearbyInteractable"/> without its payload, for views that ask every frame.</summary>
        public (string Kind, string Id, string Label)? NearbyAction(double range = 4.5) => Nearby(range)?.Action;

        (RpgLocation Location, double Distance, (string Kind, string Id, string Label) Action)? Nearby(double range)
        {
            if (_locations.Count == 0) return null;
            var near = RpgLocations.Nearest(_locationOrder, PlayerX, PlayerZ);
            if (near == null) return null;
            (RpgLocation location, double distance) = near.Value;
            if (distance > Math.Max(range, location.Radius * 0.6)) return null;
            var action = ActionAt(location.Key);
            return action == null ? null : ((RpgLocation, double, (string, string, string))?)(location, distance, action.Value);
        }

        // ================================================================ views for the screens

        (int Core, int Quests) _viewsAt = (-1, -1);
        List<JsonObject> _jobMarkers;
        JsonObject _trackedJob;
        HashSet<string> _openHacks;

        void RefreshViews()
        {
            var at = (Revision, Quests.Revision);
            if (at == _viewsAt) return;
            _viewsAt = at;
            _jobMarkers = Quests.Markers();
            _trackedJob = Quests.Tracked != null ? Quests.Snapshot(Quests.Tracked) : null;
            _openHacks = Quests.OpenHackTargets();
        }

        /// <summary>The job markers (<see cref="QuestLog.Markers"/>) as of the last change, built once and shared: for views that read them every frame. Read only.</summary>
        public IReadOnlyList<JsonObject> JobMarkers
        {
            get
            {
                RefreshViews();
                return _jobMarkers;
            }
        }

        /// <summary>The tracked job's snapshot (<see cref="QuestLog.Snapshot"/>) as of the last change, or null. Read only.</summary>
        public JsonObject TrackedJob
        {
            get
            {
                RefreshViews();
                return _trackedJob;
            }
        }

        /// <summary>Do the nearby thing (the E key).</summary>
        public RpgResult Interact()
        {
            JsonObject target = GetNearbyInteractable();
            if (target == null) return RpgResult.Fail("nothing-nearby");
            switch ((string)target["kind"])
            {
                case "dialogue": return StartDialogue((string)target["id"]);
                case "breach": return StartBreach((string)target["id"]);
                case "vendor": return OpenVendor((string)target["id"]);
                default: return RpgResult.Fail("unknown-interaction");
            }
        }

        // ================================================================ the tick

        /// <summary>The fixed step (1/60 s in the game): regeneration, hacks, buffs, the breach timer, job timers and arrivals, vendors' clocks, autosave.</summary>
        public void Tick(double dt, double gameHours = 0)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt <= 0) throw new ArgumentOutOfRangeException(nameof(dt), dt, "tick: bad dt");
            GameTime += dt;
            DerivedStats d = Derived;

            if (Health > 0 && Health < d["maxHealth"]) Health = Math.Min(d["maxHealth"], Health + d["healthRegen"] * dt);
            if (Stamina < d["maxStamina"]) Stamina = Math.Min(d["maxStamina"], Stamina + d["staminaRegen"] * dt);

            foreach (ActiveHack hack in Deck.Tick(dt, d["ramRegen"]))
            {
                Emit("rpg:quickhackExpired", new JsonObject { ["hack"] = hack.ToJson(), ["targetId"] = hack.TargetId });
            }

            bool buffsChanged = false;
            foreach (RpgBuff b in _buffs.ToList())
            {
                b.TimeLeft -= dt;
                if (b.TimeLeft <= 0)
                {
                    _buffs.Remove(b);
                    buffsChanged = true;
                    Emit("rpg:buffExpired", new JsonObject { ["id"] = b.Id });
                }
            }
            if (buffsChanged) Recompute();

            for (int i = _subnets.Count - 1; i >= 0; i--)
            {
                var s = _subnets[i];
                s.TimeLeft -= dt;
                if (s.TimeLeft <= 0) _subnets.RemoveAt(i);
                else _subnets[i] = s;
            }

            if (Breach != null && !Breach.Done) Breach.Tick(dt);

            Quests.Tick(dt);
            Quests.CheckProximity(PlayerX, PlayerZ);

            if (gameHours > 0) Economy.Tick(gameHours);

            EmitVitalsIfChanged();

            if (AutosaveEnabled)
            {
                AutosaveTimer -= dt;
                if (AutosaveTimer <= 0)
                {
                    AutosaveTimer = AutosaveSeconds;
                    Save("auto");
                }
            }
        }

        /// <summary>The subnet effects running now (subnet → effect, seconds left).</summary>
        public IReadOnlyList<(string Subnet, string Effect, double TimeLeft)> Subnets => _subnets;

        void EmitVitalsIfChanged()
        {
            double h = RpgJs.Round(Health), s = RpgJs.Round(Stamina), r = RpgJs.Round(Deck.Ram * 10) / 10;
            if (h == _lastVitals.Health && s == _lastVitals.Stamina && r == _lastVitals.Ram) return;
            _lastVitals = (h, s, r);
            Emit("rpg:vitalsChanged", GetVitals());
        }

        // ================================================================ reads

        static JsonObject AttributeMetaJson()
        {
            var o = new JsonObject();
            foreach (string a in RpgStats.Attributes)
            {
                (string label, string blurb) = RpgStats.AttributeMeta[a];
                o[a] = new JsonObject { ["label"] = label, ["blurb"] = blurb };
            }
            return o;
        }

        static JsonObject DerivedMetaJson()
        {
            var o = new JsonObject();
            foreach (DerivedMeta m in RpgStats.Derived)
            {
                var e = new JsonObject { ["label"] = m.Label };
                if (m.Int) e["int"] = true;
                if (m.Min.HasValue) e["min"] = m.Min.Value;
                if (m.Max.HasValue) e["max"] = m.Max.Value;
                o[m.Key] = e;
            }
            return o;
        }

        public JsonObject GetStats()
        {
            var attrs = new JsonObject();
            foreach (string a in RpgStats.Attributes) attrs[a] = (double)Stats.Attributes[a];
            return new JsonObject
            {
                ["attributes"] = attrs, ["attributeMeta"] = AttributeMetaJson(), ["derived"] = Derived.ToJson(), ["derivedMeta"] = DerivedMetaJson(),
                ["level"] = (double)Stats.Level, ["levelCap"] = (double)RpgStats.LevelCap, ["xp"] = Stats.Xp, ["xpInto"] = Stats.XpInto, ["xpToNext"] = Stats.XpToNext,
                ["streetCred"] = (double)Stats.StreetCred, ["credInto"] = Stats.CredInto, ["credToNext"] = Stats.CredToNext,
                ["attributePoints"] = (double)Stats.AttributePoints, ["perkPoints"] = (double)Stats.PerkPoints, ["abilities"] = Abilities.Cast<object>().ToList(),
            };
        }

        public JsonObject GetVitals()
        {
            DerivedStats d = Derived;
            return new JsonObject
            {
                ["health"] = RpgJs.Round(Health * 10) / 10, ["maxHealth"] = d["maxHealth"], ["stamina"] = RpgJs.Round(Stamina * 10) / 10, ["maxStamina"] = d["maxStamina"],
                ["ram"] = RpgJs.Round(Deck.Ram * 10) / 10, ["maxRam"] = d["maxRam"], ["buffs"] = GetBuffs().Cast<object>().ToList(),
                ["activeHacks"] = Deck.Active.Select(a =>
                {
                    JsonObject o = a.ToJson();
                    o["timeLeft"] = RpgJs.Round(a.TimeLeft * 10) / 10;
                    return (object)o;
                }).ToList(),
            };
        }

        public List<JsonObject> GetPerkTrees() => RpgPerks.Trees().Select(tree => new JsonObject
        {
            ["tree"] = tree.Tree, ["label"] = tree.Label,
            ["perks"] = tree.Perks.Select(p =>
            {
                int rank = Perks.RankOf(p.Id);
                RpgResult check = CanBuyPerk(p.Id);
                return (object)new JsonObject
                {
                    ["id"] = p.Id, ["name"] = p.Name, ["tier"] = (double)p.Tier, ["cost"] = (double)p.Cost, ["desc"] = p.Desc, ["maxRank"] = (double)p.MaxRank,
                    ["rank"] = (double)rank, ["owned"] = rank > 0, ["canBuy"] = check.Ok, ["reason"] = check.Reason,
                    ["missing"] = (check.Missing ?? new List<string>()).Cast<object>().ToList(), ["requires"] = RpgPerks.DescribeRequires(p).Cast<object>().ToList(),
                };
            }).ToList(),
        }).ToList();

        public JsonObject GetCyberware()
        {
            int used = Cyber.UsedCapacity();
            JsonObject bySlot = Cyber.BySlot();
            double capacity = Derived["cyberCapacity"];
            return new JsonObject
            {
                ["capacity"] = capacity, ["used"] = (double)used, ["free"] = capacity - used,
                ["slots"] = RpgCyberware.Slots.Select(s => (object)new JsonObject
                {
                    ["key"] = s.Key, ["label"] = s.Label, ["capacity"] = (double)s.Capacity, ["blurb"] = s.Blurb,
                    ["installed"] = ((List<object>)bySlot[s.Key]).Select(id =>
                    {
                        CyberwareDef w = RpgCyberware.Get((string)id);
                        return (object)new JsonObject
                        {
                            ["id"] = w.Id, ["name"] = w.Name, ["corp"] = w.Corp, ["tier"] = (double)w.Tier, ["capacity"] = (double)w.Capacity, ["desc"] = w.Desc,
                        };
                    }).ToList(),
                }).ToList(),
                ["catalogue"] = RpgCyberware.All.Select(w =>
                {
                    RpgResult check = CanInstallCyberware(w.Id);
                    return (object)new JsonObject
                    {
                        ["id"] = w.Id, ["name"] = w.Name, ["corp"] = w.Corp, ["slot"] = w.Slot, ["tier"] = (double)w.Tier, ["capacity"] = (double)w.Capacity,
                        ["value"] = (double)w.Value, ["desc"] = w.Desc, ["installed"] = Cyber.Has(w.Id), ["canInstall"] = check.Ok, ["reason"] = check.Reason,
                        ["missing"] = (check.Missing ?? new List<string>()).Cast<object>().ToList(), ["requires"] = RpgCyberware.DescribeRequires(w).Cast<object>().ToList(),
                    };
                }).ToList(),
            };
        }

        public JsonObject GetInventory()
        {
            var equipped = new JsonObject();
            foreach (string s in RpgItems.EquipSlots) equipped[s] = Inventory.EquippedItem(s);
            return new JsonObject
            {
                ["items"] = Inventory.Items.Select(i =>
                {
                    JsonObject o = i.ToJson();
                    o["equipped"] = Inventory.IsEquipped(i.Uid);
                    o["rarityColor"] = RpgItems.RarityOf(i.Rarity).Color;
                    return (object)o;
                }).ToList(),
                ["weight"] = Inventory.Weight, ["capacity"] = Inventory.Capacity, ["free"] = Inventory.Free, ["overweight"] = Inventory.Overweight, ["equipped"] = equipped,
                ["deck"] = Inventory.DeckItems().Cast<object>().ToList(), ["money"] = (double)Economy.Money,
            };
        }

        public JsonObject GetQuests() => new JsonObject
        {
            ["active"] = Quests.Active().Cast<object>().ToList(), ["available"] = Quests.Available().Cast<object>().ToList(),
            ["finished"] = Quests.Finished().Cast<object>().ToList(), ["tracked"] = Quests.Tracked, ["markers"] = Quests.Markers().Cast<object>().ToList(),
        };

        public JsonObject GetQuest(string id) => Quests.Snapshot(id);

        /// <summary>Everything the HUD needs to draw from nothing.</summary>
        public JsonObject GetSnapshot() => new JsonObject
        {
            ["stats"] = GetStats(), ["vitals"] = GetVitals(), ["inventory"] = GetInventory(), ["cyberware"] = GetCyberware(), ["quests"] = GetQuests(),
            ["perkTrees"] = GetPerkTrees().Cast<object>().ToList(), ["dialogue"] = GetDialogueNode(), ["breach"] = GetBreach(), ["vendor"] = GetOpenVendor(),
            ["flags"] = GetFlags(), ["locations"] = LocationListJson(), ["money"] = (double)Economy.Money, ["interactable"] = GetNearbyInteractable(),
        };

        // ================================================================ saves

        public JsonObject Serialize()
        {
            JsonObject o = SerializeRules();
            JsonObject world = CaptureWorld?.Invoke();
            if (world != null) o["world"] = world;
            return o;
        }

        JsonObject SerializeRules() => new JsonObject
        {
            ["version"] = (double)RpgSave.Version, ["seed"] = (double)Seed, ["uidSeq"] = (double)_uidSeq, ["gameTime"] = GameTime,
            ["summary"] = new JsonObject { ["level"] = (double)Stats.Level, ["cred"] = (double)Stats.StreetCred, ["money"] = (double)Economy.Money },
            ["stats"] = Stats.Serialize(), ["perks"] = Perks.Serialize(), ["cyberware"] = Cyber.Serialize(), ["inventory"] = Inventory.Serialize(),
            ["quests"] = Quests.Serialize(), ["dialogue"] = Dialogue.Serialize(), ["economy"] = Economy.Serialize(), ["hacking"] = Deck.Serialize(), ["flags"] = GetFlags(),
            ["buffs"] = _buffs.Select(b => (object)new JsonObject
            {
                ["id"] = b.Id, ["label"] = b.Label, ["timeLeft"] = b.TimeLeft, ["mods"] = b.Mods.Select(m => (object)m.ToJson()).ToList(), ["data"] = b.Data,
            }).ToList(),
            ["vitals"] = new JsonObject { ["health"] = Health, ["stamina"] = Stamina },
        };

        /// <summary>Restore from a migrated save. A save of another version, or a structurally bad one, throws.</summary>
        public RpgCore Deserialize(JsonObject data)
        {
            if (data == null) throw new ArgumentException("deserialize: payload must be an object");
            object v = Arg(data, "version");
            if (!(v is double vd) || vd != RpgSave.Version) throw new InvalidOperationException($"deserialize: expected version {RpgSave.Version}, got {v} — migrate first");
            _suspendStatEvents += 1;
            try
            {
                if (Arg(data, "seed") is double seed) Seed = (uint)seed;
                _uidSeq = Arg(data, "uidSeq") is double u ? (int)u : 0;
                GameTime = Arg(data, "gameTime") is double gt ? gt : 0;
                Stats = StatBlock.Deserialize(Arg(data, "stats") as JsonObject);
                Perks = PerkTree.Deserialize(Arg(data, "perks") as JsonObject);
                Cyber = CyberwareRig.Deserialize(Arg(data, "cyberware") as List<object>);
                Inventory = Inventory.Deserialize(Arg(data, "inventory") as JsonObject);
                Quests.Deserialize(Arg(data, "quests") as JsonObject);
                Dialogue.Deserialize(Arg(data, "dialogue") as JsonObject);
                Economy.Deserialize(Arg(data, "economy") as JsonObject);
                _flags.Clear();
                _flagOrder.Clear();
                if (Arg(data, "flags") is JsonObject flags)
                {
                    foreach (KeyValuePair<string, object> kv in flags)
                    {
                        _flagOrder.Add(kv.Key);
                        _flags[kv.Key] = kv.Value;
                    }
                }
                _buffs.Clear();
                foreach (JsonObject b in RpgJs.AsList(Arg(data, "buffs")).Cast<JsonObject>())
                {
                    var buff = new RpgBuff
                    {
                        Id = (string)b["id"], Label = Arg(b, "label") as string, TimeLeft = RpgJs.Num(b["timeLeft"], "buff timeLeft"),
                        Mods = RpgJs.AsList(Arg(b, "mods")).Select(StatModifier.FromJson).ToList(), Data = Arg(b, "data") as JsonObject ?? new JsonObject(),
                    };
                    int i = _buffs.FindIndex(x => x.Id == buff.Id);
                    if (i >= 0) _buffs[i] = buff;
                    else _buffs.Add(buff);
                }
                Gen = new LootGenerator(Seed, nextUid: NextUid);
                _interactions = null;
                Recompute(true);
                Deck.Deserialize(Arg(data, "hacking") as JsonObject);
                Deck.SetMaxRam(Derived["maxRam"]);
                var vitals = Arg(data, "vitals") as JsonObject;
                Health = Math.Min(Derived["maxHealth"], vitals != null && Arg(vitals, "health") is double h ? h : Derived["maxHealth"]);
                Stamina = Math.Min(Derived["maxStamina"], vitals != null && Arg(vitals, "stamina") is double st ? st : Derived["maxStamina"]);
                Breach = null;
                LoadedWorld = Arg(data, "world") as JsonObject;
            }
            finally
            {
                _suspendStatEvents -= 1;
            }
            Emit("rpg:statsChanged", GetStats());
            Emit("rpg:inventoryChanged", GetInventory());
            Emit("rpg:vitalsChanged", GetVitals());
            return this;
        }

        public (string Slot, int Version, int Bytes) Save(string slot = "auto")
        {
            var res = Saves.Write(slot, Serialize());
            Emit("rpg:saved", new JsonObject { ["slot"] = slot, ["version"] = (double)res.Version, ["bytes"] = (double)res.Bytes });
            return res;
        }

        public RpgResult Load(string slot = "auto")
        {
            JsonObject data = Saves.Read(slot);
            if (data == null) return RpgResult.Fail("empty-slot");
            Deserialize(data);
            Emit("rpg:loaded", new JsonObject { ["slot"] = slot, ["version"] = data["version"] });
            Notify("Save loaded", "system");
            return new RpgResult { Ok = true, Extra = new JsonObject { ["slot"] = slot, ["version"] = data["version"] } };
        }

        public bool HasSave(string slot = "auto") => Saves.Has(slot);

        public void DeleteSave(string slot) => Saves.Remove(slot);

        public List<SaveSlotInfo> ListSaves() => Saves.List();

        // IEconomyHost
        Inventory IEconomyHost.Inventory => Inventory;
        DerivedStats IEconomyHost.Derived => Derived;
        object IEconomyHost.GetFlag(string flag) => Flag(flag);
        int IEconomyHost.Level => Stats.Level;
        string IEconomyHost.NextUid() => NextUid();
        IConditionView IEconomyHost.View => this;
    }
}
