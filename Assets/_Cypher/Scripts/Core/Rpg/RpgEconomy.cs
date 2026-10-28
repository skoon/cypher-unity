using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>One line of a vendor's stock recipe: a fixed template, some rolls of a type, or some chrome.</summary>
    public sealed class StockPlanEntry
    {
        public string Kind, TemplateId, Type, Rarity;
        public int Count;
        public int? MaxTier;
        public double? Tier;
    }

    public sealed class VendorDef
    {
        public string Id, Name, Kind, LocationKey, Greeting;
        public double Markup, BuyRate, RestockHours;
        public uint StockSeed;
        public IReadOnlyList<StockPlanEntry> Plan;
        /// <summary>A condition to trade at all (JSON, see <see cref="RpgConditions"/>), or null.</summary>
        public JsonObject Requires;
        /// <summary>Extra discount per world flag that is set, in the order written.</summary>
        public IReadOnlyList<(string Flag, double Bonus)> FlagDiscounts = Array.Empty<(string, double)>();
    }

    /// <summary>What the economy reads from the rest of the game.</summary>
    public interface IEconomyHost
    {
        void Emit(string type, JsonObject payload);
        Inventory Inventory { get; }
        DerivedStats Derived { get; }
        object GetFlag(string flag);
        int Level { get; }
        string NextUid();
        IConditionView View { get; }
    }

    public sealed class VendorState
    {
        public string Id;
        public List<Item> Stock = new List<Item>();
        public int Restocks;
        public double NextRestockHours;
    }

    /// <summary>
    /// Eddies, vendors, their stock and their prices: a port of <c>rpg/Economy.js</c>. Buy price = value × markup × (1 − discount); sell price = value × buy rate ×
    /// (1 + sell bonus). Stock is rolled from the vendor's seed and its restock count, so a shelf is the same for the same save.
    /// </summary>
    public sealed class Economy
    {
        public static readonly IReadOnlyList<VendorDef> Vendors = new[]
        {
            new VendorDef
            {
                Id = "v_vey", Name = "Doc Vey", Kind = "ripperdoc", LocationKey = "ripperdoc", Markup = 1.15, BuyRate = 0.3, RestockHours = 24, StockSeed = 90210,
                Greeting = "Cash first. Anaesthetic is extra.",
                FlagDiscounts = new[] { ("vey_discount", 0.1), ("vey_trusts", 0.05), ("vey_burned", -0.5) },
                Requires = new JsonObject { ["notFlag"] = "vey_burned" },
                Plan = new[] { new StockPlanEntry { Kind = "cyberware", Count = 4, MaxTier = 3 }, new StockPlanEntry { Kind = "roll", Type = "consumable", Count = 3 } },
            },
            new VendorDef
            {
                Id = "v_marlene", Name = "Tin Marlene", Kind = "pawn", LocationKey = "pawn", Markup = 1.42, BuyRate = 0.38, RestockHours = 12, StockSeed = 31337,
                Greeting = "Window prices are for tourists.",
                FlagDiscounts = new[] { ("marlene_counter_prices", 0.3), ("marlene_trusts", 0.06), ("marlene_enemy", -1.0) },
                Requires = new JsonObject { ["notFlag"] = "marlene_enemy" },
                Plan = new[]
                {
                    new StockPlanEntry { Kind = "roll", Type = "weapon", Count = 3 }, new StockPlanEntry { Kind = "roll", Type = "clothing", Count = 3 },
                    new StockPlanEntry { Kind = "roll", Type = "junk", Count = 2 },
                },
            },
            new VendorDef
            {
                Id = "v_kaz", Name = "Mama Kaz", Kind = "fixer", LocationKey = "noodle", Markup = 1.0, BuyRate = 0.42, RestockHours = 8, StockSeed = 4242,
                Greeting = "Eat something. Then buy something.",
                FlagDiscounts = new[] { ("kaz_honest", 0.08), ("kaz_haggled", 0.05) },
                Plan = new[]
                {
                    new StockPlanEntry { Kind = "roll", Type = "consumable", Count = 4 }, new StockPlanEntry { Kind = "fixed", TemplateId = "qh_ping" },
                    new StockPlanEntry { Kind = "roll", Type = "quickhack", Count = 1 },
                },
            },
            new VendorDef
            {
                Id = "v_market", Name = "Night Market Stalls", Kind = "market", LocationKey = "market", Markup = 1.1, BuyRate = 0.34, RestockHours = 6, StockSeed = 5150,
                Greeting = "Everything is fresh. Nothing is legal.",
                Plan = new[]
                {
                    new StockPlanEntry { Kind = "roll", Type = "consumable", Count = 3 }, new StockPlanEntry { Kind = "roll", Type = "clothing", Count = 2 },
                    new StockPlanEntry { Kind = "roll", Type = "weapon", Count = 2 }, new StockPlanEntry { Kind = "roll", Type = "junk", Count = 3 },
                },
            },
            new VendorDef
            {
                Id = "v_static", Name = "Static Marta", Kind = "netrunner", LocationKey = "relay", Markup = 1.25, BuyRate = 0.45, RestockHours = 24, StockSeed = 6161,
                Greeting = "Software only. I do not touch meat.",
                Requires = new JsonObject { ["streetCred"] = 2.0 },
                FlagDiscounts = new[] { ("marta_owes", 0.15), ("deadair_clean", 0.05) },
                Plan = new[] { new StockPlanEntry { Kind = "roll", Type = "quickhack", Count = 4 }, new StockPlanEntry { Kind = "roll", Type = "consumable", Count = 2 } },
            },
        };

        public static VendorDef GetVendor(string id)
        {
            foreach (VendorDef v in Vendors) if (v.Id == id) return v;
            throw new ArgumentOutOfRangeException(nameof(id), $"Unknown vendor id \"{id}\"");
        }

        readonly IEconomyHost _host;
        readonly IReadOnlyList<VendorDef> _defs;
        readonly List<string> _order = new List<string>();
        readonly Dictionary<string, VendorState> _vendors = new Dictionary<string, VendorState>();

        public int Money;
        public string OpenVendorId { get; private set; }

        public Economy(IEconomyHost host, int money = 1250, IReadOnlyList<VendorDef> defs = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _defs = defs ?? Vendors;
            Money = money;
            foreach (VendorDef def in _defs)
            {
                _order.Add(def.Id);
                _vendors[def.Id] = new VendorState { Id = def.Id };
            }
        }

        public VendorDef Def(string id)
        {
            foreach (VendorDef d in _defs) if (d.Id == id) return d;
            throw new ArgumentOutOfRangeException(nameof(id), $"Unknown vendor id \"{id}\"");
        }

        public VendorState State(string id)
        {
            if (id == null || !_vendors.TryGetValue(id, out VendorState s)) throw new ArgumentOutOfRangeException(nameof(id), $"Unknown vendor id \"{id}\"");
            return s;
        }

        // ------------------------------------------------------------ currency

        public RpgResult AddMoney(double amount, string reason = "unknown")
        {
            if (double.IsNaN(amount) || double.IsInfinity(amount)) throw new ArgumentException($"addMoney: bad amount {amount}");
            int delta = (int)RpgJs.Round(amount);
            if (delta < 0 && Money + delta < 0) return new RpgResult { Ok = false, Reason = "insufficient-funds", Money = Money, Delta = 0 };
            Money += delta;
            _host.Emit("rpg:moneyChanged", new JsonObject { ["money"] = (double)Money, ["delta"] = (double)delta, ["reason"] = reason });
            return new RpgResult { Ok = true, Money = Money, Delta = delta };
        }

        public bool CanAfford(double amount) => Money >= amount;

        // ------------------------------------------------------------ pricing

        /// <summary>The whole buy discount at a vendor: the derived stat plus the relationship flags, clamped to [-0.5, 0.6].</summary>
        public double DiscountFor(string vendorId)
        {
            VendorDef def = Def(vendorId);
            double d = _host.Derived["vendorDiscount"];
            foreach ((string flag, double bonus) in def.FlagDiscounts)
            {
                if (RpgJs.Truthy(_host.GetFlag(flag))) d += bonus;
            }
            return Math.Max(-0.5, Math.Min(0.6, d));
        }

        public int BuyPrice(string vendorId, Item item)
        {
            VendorDef def = Def(vendorId);
            double price = item.Value * def.Markup * (1 - DiscountFor(vendorId));
            return (int)Math.Max(1, RpgJs.Round(price)) * item.Qty;
        }

        public int SellPrice(string vendorId, Item item, int qty = 1)
        {
            VendorDef def = Def(vendorId);
            double bonus = _host.Derived["sellBonus"];
            return (int)Math.Max(1, RpgJs.Round(item.Value * def.BuyRate * (1 + bonus))) * qty;
        }

        // ------------------------------------------------------------ stock

        /// <summary>Roll a vendor's shelf, unless it is stocked and not yet due (or <paramref name="force"/>).</summary>
        public List<Item> Restock(string vendorId, bool force = false)
        {
            VendorDef def = Def(vendorId);
            VendorState st = State(vendorId);
            if (!force && st.Stock.Count > 0 && st.NextRestockHours > 0) return st.Stock;
            int level = _host.Level;
            var gen = new LootGenerator(unchecked(def.StockSeed + (uint)(st.Restocks * 7919)), nextUid: _host.NextUid);
            var stock = new List<Item>();
            foreach (StockPlanEntry entry in def.Plan)
            {
                if (entry.Kind == "fixed")
                {
                    stock.Add(gen.RollItem(new LootRoll { Level = level, Luck = 0, Tier = 1, TemplateId = entry.TemplateId, Rarity = entry.Rarity ?? "common" }));
                }
                else if (entry.Kind == "cyberware")
                {
                    var pool = new List<CyberwareDef>();
                    foreach (CyberwareDef c in RpgCyberware.All) if (c.Tier <= (entry.MaxTier ?? 3)) pool.Add(c);
                    for (int i = 0; i < entry.Count; i++)
                    {
                        CyberwareDef ware = pool[(int)Math.Floor(gen.Next() * pool.Count) % pool.Count];
                        if (stock.Exists(s => s.CyberwareId == ware.Id)) continue;
                        stock.Add(RpgItems.MakeCyberwareItem(ware.Id, _host.NextUid()));
                    }
                }
                else
                {
                    for (int i = 0; i < entry.Count; i++)
                    {
                        stock.Add(gen.RollItem(new LootRoll { Level = level, Luck = 0, Type = entry.Type, Tier = entry.Tier ?? 1, Rarity = entry.Rarity }));
                    }
                }
            }
            st.Stock = stock;
            st.Restocks += 1;
            st.NextRestockHours = def.RestockHours;
            _host.Emit("rpg:vendorRestocked", new JsonObject { ["vendorId"] = vendorId, ["count"] = (double)stock.Count });
            return stock;
        }

        /// <summary>Run the restock clocks on by <paramref name="hours"/> of game time.</summary>
        public void Tick(double hours)
        {
            if (hours <= 0) return;
            foreach (string id in _order)
            {
                VendorState st = _vendors[id];
                if (st.NextRestockHours <= 0) continue;
                st.NextRestockHours -= hours;
                if (st.NextRestockHours <= 0) Restock(st.Id, true);
            }
        }

        // ------------------------------------------------------------ trade

        public RpgResult Open(string vendorId)
        {
            VendorDef def = Def(vendorId);
            if (def.Requires != null)
            {
                (bool ok, List<string> missing) = RpgConditions.Check(def.Requires, _host.View);
                if (!ok) return new RpgResult { Ok = false, Reason = "refused", Missing = missing };
            }
            Restock(vendorId);
            OpenVendorId = vendorId;
            JsonObject payload = Snapshot(vendorId);
            _host.Emit("rpg:vendorOpened", payload);
            return new RpgResult { Ok = true, Vendor = payload };
        }

        public RpgResult Close()
        {
            if (OpenVendorId == null) return RpgResult.Fail("none-open");
            string vendorId = OpenVendorId;
            OpenVendorId = null;
            _host.Emit("rpg:vendorClosed", new JsonObject { ["vendorId"] = vendorId });
            return new RpgResult { Ok = true, VendorId = vendorId };
        }

        /// <summary>A shop window: the vendor, the discount, the purse and each item on the shelf with its price.</summary>
        public JsonObject Snapshot(string vendorId)
        {
            VendorDef def = Def(vendorId);
            VendorState st = State(vendorId);
            var stock = new List<object>();
            foreach (Item item in st.Stock)
            {
                stock.Add(new JsonObject
                {
                    ["item"] = item, ["price"] = (double)BuyPrice(vendorId, item), ["rarityColor"] = RpgItems.RarityOf(item.Rarity).Color,
                    ["affordable"] = Money >= BuyPrice(vendorId, item),
                });
            }
            return new JsonObject
            {
                ["id"] = def.Id, ["name"] = def.Name, ["kind"] = def.Kind, ["greeting"] = def.Greeting, ["locationKey"] = def.LocationKey,
                ["discount"] = RpgJs.Round(DiscountFor(vendorId) * 1000) / 1000, ["markup"] = def.Markup, ["buyRate"] = def.BuyRate, ["money"] = (double)Money,
                ["stock"] = stock,
            };
        }

        public RpgResult Buy(string vendorId, string uid)
        {
            VendorState st = State(vendorId);
            int idx = st.Stock.FindIndex(i => i.Uid == uid);
            if (idx < 0) return RpgResult.Fail("not-in-stock");
            Item item = st.Stock[idx];
            int price = BuyPrice(vendorId, item);
            if (Money < price) return new RpgResult { Ok = false, Reason = "cannot-afford", Price = price };
            if (!_host.Inventory.CanFit(item)) return new RpgResult { Ok = false, Reason = "overweight", Price = price };
            AddMoney(-price, "buy:" + item.TemplateId);
            st.Stock.RemoveAt(idx);
            RpgResult added = _host.Inventory.Add(item);
            if (!added.Ok)
            {
                // refund rather than eat the item: a silent loss would be a real bug
                AddMoney(price, "refund");
                st.Stock.Insert(idx, item);
                return new RpgResult { Ok = false, Reason = added.Reason, Price = price };
            }
            _host.Emit("rpg:tradeCompleted", new JsonObject { ["vendorId"] = vendorId, ["kind"] = "buy", ["item"] = item, ["price"] = (double)price });
            return new RpgResult { Ok = true, Item = item, Price = price };
        }

        public RpgResult Sell(string vendorId, string uid, int qty = 1)
        {
            Def(vendorId);
            Item item = _host.Inventory.Find(uid);
            if (item == null) return RpgResult.Fail("not-found");
            if (item.Type == "quest") return RpgResult.Fail("quest-item");
            if (item.Qty < qty) return RpgResult.Fail("not-enough");
            int price = SellPrice(vendorId, item, qty);
            RpgResult removed = _host.Inventory.Remove(uid, qty);
            if (!removed.Ok) return RpgResult.Fail(removed.Reason);
            AddMoney(price, "sell:" + item.TemplateId);
            // sold stock goes back on the shelf at the vendor's markup
            Item shelved = item.Copy();
            shelved.Qty = qty;
            State(vendorId).Stock.Add(shelved);
            _host.Emit("rpg:tradeCompleted", new JsonObject { ["vendorId"] = vendorId, ["kind"] = "sell", ["item"] = item, ["price"] = (double)price, ["qty"] = (double)qty });
            return new RpgResult { Ok = true, Item = item, Price = price, Qty = qty };
        }

        // ------------------------------------------------------------ saves

        public JsonObject Serialize()
        {
            var vendors = new List<object>();
            foreach (string id in _order)
            {
                VendorState s = _vendors[id];
                var stock = new List<object>();
                foreach (Item i in s.Stock) stock.Add(i.ToJson());
                vendors.Add(new JsonObject { ["id"] = s.Id, ["restocks"] = (double)s.Restocks, ["nextRestockHours"] = s.NextRestockHours, ["stock"] = stock });
            }
            return new JsonObject { ["money"] = (double)Money, ["vendors"] = vendors };
        }

        public Economy Deserialize(JsonObject data)
        {
            if (data == null) return this;
            if (data.TryGetValue("money", out object m) && m != null) Money = RpgJs.Int(m, "money");
            if (data.TryGetValue("vendors", out object vs))
            {
                foreach (object vo in RpgJs.AsList(vs))
                {
                    var s = (JsonObject)vo;
                    string id = s["id"] as string;
                    if (id == null || !_vendors.ContainsKey(id)) continue;   // a vendor a later build removed
                    var state = new VendorState
                    {
                        Id = id,
                        Restocks = s.TryGetValue("restocks", out object r) && r != null ? RpgJs.Int(r, "restocks") : 0,
                        NextRestockHours = s.TryGetValue("nextRestockHours", out object n) && n != null ? RpgJs.Num(n, "nextRestockHours") : 0,
                    };
                    if (s.TryGetValue("stock", out object st))
                    {
                        foreach (object io in RpgJs.AsList(st)) state.Stock.Add(Item.FromJson((JsonObject)io));
                    }
                    _vendors[id] = state;
                }
            }
            return this;
        }
    }
}
