using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    public sealed class DaemonDef
    {
        public string Id, Name, Desc;
        public int Length, TierMin;
        /// <summary>What solving it gives (<c>{ type: money|loot|buff|subnet, … }</c>), which the RPG core pays out.</summary>
        public JsonObject Reward;
    }

    public sealed class BreachTarget
    {
        public string Id, Name, Subnet, LocationKey;
        public int Tier, Size;
        public double Difficulty;
    }

    /// <summary>A daemon in a live breach: its sequence and whether it has uploaded.</summary>
    public sealed class BreachDaemon
    {
        public string Id, Name, Desc;
        public List<string> Sequence;
        public JsonObject Reward;
        public bool Solved;

        public JsonObject ToJson() => new JsonObject
        {
            ["id"] = Id, ["name"] = Name, ["desc"] = Desc, ["sequence"] = new List<object>(Sequence), ["reward"] = Reward, ["solved"] = Solved,
        };
    }

    public readonly struct GridCell
    {
        public readonly int R, C;

        public GridCell(int r, int c)
        {
            R = r; C = c;
        }
    }

    /// <summary>A breach puzzle and its progress. The HUD draws it; only <see cref="BreachSession"/> changes it.</summary>
    public sealed class BreachState
    {
        public string TargetId, TargetName;
        public int Tier, Size, BufferSize;
        public string[][] Matrix;
        public List<BreachDaemon> Daemons;
        public List<string> Buffer = new List<string>();
        public List<(int R, int C, string Code)> Picks = new List<(int, int, string)>();
        public List<string> Used = new List<string>();
        /// <summary>"row" or "col": which line the next pick must come from.</summary>
        public string Axis = "row";
        public int Lock;
        public double TimeLimit, TimeLeft;
        /// <summary>running, done (something uploaded) or failed.</summary>
        public string State = "running";
        public string Reason;
        public List<string> Solution;
        /// <summary>The cells the guaranteed solution was read from (codes alone are ambiguous).</summary>
        public List<GridCell> SolutionPath;

        public JsonObject ToJson()
        {
            var o = new JsonObject
            {
                ["targetId"] = TargetId, ["targetName"] = TargetName, ["tier"] = (double)Tier, ["size"] = (double)Size,
                ["matrix"] = Matrix.Select(row => (object)row.Select(c => (object)c).ToList()).ToList(),
                ["daemons"] = Daemons.Select(d => (object)d.ToJson()).ToList(), ["bufferSize"] = (double)BufferSize, ["buffer"] = new List<object>(Buffer),
                ["picks"] = Picks.Select(p => (object)new JsonObject { ["r"] = (double)p.R, ["c"] = (double)p.C, ["code"] = p.Code }).ToList(),
                ["used"] = new List<object>(Used), ["axis"] = Axis, ["lock"] = (double)Lock, ["timeLimit"] = TimeLimit, ["timeLeft"] = TimeLeft, ["state"] = State,
                ["solution"] = new List<object>(Solution),
                ["solutionPath"] = SolutionPath.Select(p => (object)new JsonObject { ["r"] = (double)p.R, ["c"] = (double)p.C }).ToList(),
            };
            if (Reason != null) o["reason"] = Reason;
            return o;
        }
    }

    /// <summary>
    /// The breach protocol and the quickhack RAM queue: a port of <c>rpg/Hacking.js</c>. A breach is a square code matrix and a buffer; picks alternate between a row
    /// and a column, starting in row 0, each cell once; a daemon uploads when its sequence is a contiguous run in the buffer. Every generated puzzle is solvable: the
    /// generator walks a legal path first and cuts the daemons' sequences out of it. Seeded.
    /// </summary>
    public static class RpgHacking
    {
        public static readonly IReadOnlyList<string> Codes = new[] { "1C", "55", "BD", "E9", "7A", "FF" };

        static JsonObject J(string json) => (JsonObject)Json.Parse(json);

        public static readonly IReadOnlyList<DaemonDef> Daemons = new[]
        {
            new DaemonDef { Id = "datamine_v1", Name = "DATAMINE_V1", Length = 2, TierMin = 0, Desc = "Siphon loose eddies out of the local wallet cache.",
                Reward = J("{\"type\":\"money\",\"amount\":350}") },
            new DaemonDef { Id = "datamine_v2", Name = "DATAMINE_V2", Length = 3, TierMin = 0, Desc = "Pull component scrap and a shard fragment.",
                Reward = J("{\"type\":\"loot\",\"tier\":1,\"count\":2}") },
            new DaemonDef { Id = "datamine_v3", Name = "DATAMINE_V3", Length = 4, TierMin = 1, Desc = "Deep pull: quickhack blueprint fragments and hard currency.",
                Reward = J("{\"type\":\"loot\",\"tier\":2,\"count\":3,\"money\":900}") },
            new DaemonDef { Id = "icepick", Name = "ICEPICK", Length = 3, TierMin = 0, Desc = "Cheaper uploads for the next 60 seconds.",
                Reward = J("{\"type\":\"buff\",\"id\":\"icepick\",\"duration\":60,\"discount\":1}") },
            new DaemonDef { Id = "mass_vuln", Name = "MASS_VULNERABILITY", Length = 3, TierMin = 1, Desc = "Flags every device on this subnet as vulnerable.",
                Reward = J("{\"type\":\"subnet\",\"effect\":\"vulnerable\",\"duration\":90}") },
            new DaemonDef { Id = "camera_shutdown", Name = "CAMERA_SHUTDOWN", Length = 2, TierMin = 0, Desc = "Loops every camera feed on the subnet.",
                Reward = J("{\"type\":\"subnet\",\"effect\":\"cameras_down\",\"duration\":120}") },
            new DaemonDef { Id = "turret_shutdown", Name = "TURRET_SHUTDOWN", Length = 4, TierMin = 2, Desc = "Drops the turret grid off the subnet entirely.",
                Reward = J("{\"type\":\"subnet\",\"effect\":\"turrets_down\",\"duration\":120}") },
            new DaemonDef { Id = "ghostwalk", Name = "GHOSTWALK", Length = 4, TierMin = 2, Desc = "Rewrites your presence out of the subnet log.",
                Reward = J("{\"type\":\"buff\",\"id\":\"ghostwalk\",\"duration\":45,\"stealth\":25}") },
        };

        /// <summary>The devices that can be breached, in the order written; <c>subnet</c> groups them for MASS_VULNERABILITY.</summary>
        public static readonly IReadOnlyList<BreachTarget> Targets = new[]
        {
            new BreachTarget { Id = "datacore", Name = "Sector Data Core", Tier = 2, Size = 6, Subnet = "sector", LocationKey = "datacore", Difficulty = 1.25 },
            new BreachTarget { Id = "garage_locker", Name = "Sixth Street Stock Locker", Tier = 1, Size = 5, Subnet = "garage", LocationKey = "garage", Difficulty = 1.0 },
            new BreachTarget { Id = "substation_breaker", Name = "Substation 7 Breaker", Tier = 1, Size = 5, Subnet = "grid", LocationKey = "substation", Difficulty = 1.1 },
            new BreachTarget { Id = "relay_mast", Name = "Pirate Relay Mast", Tier = 0, Size = 5, Subnet = "grid", LocationKey = "relay", Difficulty = 0.9 },
            new BreachTarget { Id = "market_terminal", Name = "Night Market Payment Terminal", Tier = 0, Size = 5, Subnet = "market", LocationKey = "market", Difficulty = 0.85 },
            new BreachTarget { Id = "spire_lobby", Name = "Spire Lobby Access Node", Tier = 3, Size = 7, Subnet = "arasaka", LocationKey = "spire", Difficulty = 1.5 },
        };

        public static BreachTarget Target(string id)
        {
            foreach (BreachTarget t in Targets) if (t.Id == id) return t;
            throw new ArgumentOutOfRangeException(nameof(id), $"Unknown breach target \"{id}\"");
        }

        public static bool IsTarget(string id) => Targets.Any(t => t.Id == id);

        /// <summary>Whether <paramref name="seq"/> appears as a contiguous run in <paramref name="buffer"/>.</summary>
        public static bool ContainsSequence(IReadOnlyList<string> buffer, IReadOnlyList<string> seq)
        {
            if (seq.Count == 0 || seq.Count > buffer.Count) return false;
            for (int i = 0; i <= buffer.Count - seq.Count; i++)
            {
                bool match = true;
                for (int j = 0; j < seq.Count; j++)
                {
                    if (buffer[i + j] != seq[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return true;
            }
            return false;
        }

        /// <summary>Make a solvable breach puzzle.</summary>
        public static BreachState Generate(uint seed = 1, int size = 5, double bufferSize = 6, double timeLimit = 20, int tier = 1, string targetId = "device",
            string targetName = "Unknown Device", int? daemonCount = null)
        {
            if (size < 3 || size > 8) throw new ArgumentOutOfRangeException(nameof(size), size, "generateBreach: size out of range 3-8");
            int buffer = (int)Math.Max(4, Math.Min(12, RpgJs.Round(bufferSize)));
            var rng = new Rng(seed);

            var matrix = new string[size][];
            for (int r = 0; r < size; r++)
            {
                matrix[r] = new string[size];
                for (int c = 0; c < size; c++) matrix[r][c] = Codes[(int)Math.Floor(rng.Next() * Codes.Count) % Codes.Count];
            }

            // walk a legal path so the puzzle is always solvable
            var used = new HashSet<string>();
            var path = new List<GridCell>();
            string axis = "row";
            int lockAt = 0;
            for (int step = 0; step < buffer; step++)
            {
                var options = new List<GridCell>();
                for (int i = 0; i < size; i++)
                {
                    int r = axis == "row" ? lockAt : i;
                    int c = axis == "row" ? i : lockAt;
                    if (!used.Contains($"{r},{c}")) options.Add(new GridCell(r, c));
                }
                if (options.Count == 0) break;   // a dead end: the path is shorter, still solvable
                GridCell cell = options[(int)Math.Floor(rng.Next() * options.Count) % options.Count];
                used.Add($"{cell.R},{cell.C}");
                path.Add(cell);
                lockAt = axis == "row" ? cell.C : cell.R;
                axis = axis == "row" ? "col" : "row";
            }
            List<string> pathCodes = path.Select(p => matrix[p.R][p.C]).ToList();

            // carve the daemons out of windows of that path
            List<DaemonDef> pool = Daemons.Where(d => d.TierMin <= tier && d.Length <= pathCodes.Count).ToList();
            int wanted = daemonCount ?? Math.Max(2, Math.Min(4, 2 + tier));
            var daemons = new List<BreachDaemon>();
            var chosen = new HashSet<string>();
            for (int i = 0; i < wanted && chosen.Count < pool.Count; i++)
            {
                DaemonDef def;
                int guard = 0;
                do
                {
                    def = pool[(int)Math.Floor(rng.Next() * pool.Count) % pool.Count];
                    guard++;
                } while (chosen.Contains(def.Id) && guard < 64);
                if (chosen.Contains(def.Id)) break;
                chosen.Add(def.Id);
                int maxStart = pathCodes.Count - def.Length;
                int start = maxStart <= 0 ? 0 : (int)Math.Floor(rng.Next() * (maxStart + 1)) % (maxStart + 1);
                daemons.Add(new BreachDaemon
                {
                    Id = def.Id, Name = def.Name, Desc = def.Desc, Sequence = pathCodes.GetRange(start, Math.Min(def.Length, pathCodes.Count - start)), Reward = def.Reward,
                });
            }

            return new BreachState
            {
                TargetId = targetId, TargetName = targetName, Tier = tier, Size = size, Matrix = matrix, Daemons = daemons, BufferSize = buffer,
                TimeLimit = timeLimit, TimeLeft = timeLimit, Solution = pathCodes, SolutionPath = path,
            };
        }
    }

    /// <summary>The end of a breach, as the core and the HUD read it.</summary>
    public sealed class BreachResult
    {
        public string TargetId, TargetName, State, Reason;
        public List<BreachDaemon> Solved, Missed;
        public List<string> Buffer;
        public bool Success;

        public JsonObject ToJson() => new JsonObject
        {
            ["targetId"] = TargetId, ["targetName"] = TargetName, ["state"] = State, ["reason"] = Reason,
            ["solved"] = Solved.Select(d => (object)new JsonObject { ["id"] = d.Id, ["name"] = d.Name, ["reward"] = d.Reward }).ToList(),
            ["missed"] = Missed.Select(d => (object)new JsonObject { ["id"] = d.Id, ["name"] = d.Name }).ToList(),
            ["buffer"] = new List<object>(Buffer), ["success"] = Success,
        };
    }

    /// <summary>A live breach: the selection rules over a <see cref="BreachState"/>.</summary>
    public sealed class BreachSession
    {
        public readonly BreachState S;
        readonly Action<BreachDaemon, BreachState> _onDaemon;
        readonly Action<BreachResult, BreachState> _onEnd;
        readonly HashSet<string> _used;

        public BreachSession(BreachState state, Action<BreachDaemon, BreachState> onDaemon = null, Action<BreachResult, BreachState> onEnd = null)
        {
            S = state ?? throw new ArgumentNullException(nameof(state));
            _onDaemon = onDaemon;
            _onEnd = onEnd;
            _used = new HashSet<string>(state.Used ?? new List<string>());
        }

        public bool Done => S.State != "running";

        /// <summary>The cells that may be picked now (the HUD lights these).</summary>
        public List<(int R, int C, string Code)> LegalCells()
        {
            var out_ = new List<(int, int, string)>();
            if (Done) return out_;
            for (int i = 0; i < S.Size; i++)
            {
                int r = S.Axis == "row" ? S.Lock : i;
                int c = S.Axis == "row" ? i : S.Lock;
                if (!_used.Contains($"{r},{c}")) out_.Add((r, c, S.Matrix[r][c]));
            }
            return out_;
        }

        public bool IsLegal(int r, int c)
        {
            if (Done) return false;
            if (r < 0 || c < 0 || r >= S.Size || c >= S.Size) return false;
            if (_used.Contains($"{r},{c}")) return false;
            return S.Axis == "row" ? r == S.Lock : c == S.Lock;
        }

        /// <summary>Pick a cell: <c>{ ok, reason?, code, solved, done }</c>.</summary>
        public (bool Ok, string Reason, string Code, List<BreachDaemon> Solved, bool Done) Select(int r, int c)
        {
            if (Done) return (false, "finished", null, null, false);
            if (!IsLegal(r, c)) return (false, "illegal-cell", null, null, false);

            string code = S.Matrix[r][c];
            S.Buffer.Add(code);
            S.Picks.Add((r, c, code));
            if (_used.Add($"{r},{c}")) S.Used.Add($"{r},{c}");
            S.Lock = S.Axis == "row" ? c : r;
            S.Axis = S.Axis == "row" ? "col" : "row";

            var solved = new List<BreachDaemon>();
            foreach (BreachDaemon d in S.Daemons)
            {
                if (d.Solved) continue;
                if (RpgHacking.ContainsSequence(S.Buffer, d.Sequence))
                {
                    d.Solved = true;
                    solved.Add(d);
                    _onDaemon?.Invoke(d, S);
                }
            }

            bool allSolved = S.Daemons.All(d => d.Solved);
            bool bufferFull = S.Buffer.Count >= S.BufferSize;
            bool stuck = LegalCells().Count == 0;
            if (allSolved || bufferFull || stuck)
            {
                Finish(allSolved ? "complete" : bufferFull ? "buffer-full" : "stuck");
                return (true, null, code, solved, true);
            }
            return (true, null, code, solved, false);
        }

        /// <summary>Give up, or upload early with what is in the buffer.</summary>
        public BreachResult Finish(string reason = "uploaded")
        {
            if (Done) return Result();
            S.State = S.Daemons.Any(d => d.Solved) ? "done" : "failed";
            S.Reason = reason;
            BreachResult res = Result();
            _onEnd?.Invoke(res, S);
            return res;
        }

        public void Tick(double dt)
        {
            if (Done) return;
            S.TimeLeft = Math.Max(0, S.TimeLeft - dt);
            if (S.TimeLeft <= 0) Finish("timeout");
        }

        public BreachResult Result() => new BreachResult
        {
            TargetId = S.TargetId, TargetName = S.TargetName, State = S.State, Reason = S.Reason,
            Solved = S.Daemons.Where(d => d.Solved).ToList(), Missed = S.Daemons.Where(d => !d.Solved).ToList(), Buffer = new List<string>(S.Buffer),
            Success = S.State == "done",
        };
    }

    /// <summary>A quickhack running on a target.</summary>
    public sealed class ActiveHack
    {
        public string Uid, HackId, Name, TargetId, Effect;
        public double Power, Duration, TimeLeft;

        public ActiveHack Copy() => (ActiveHack)MemberwiseClone();

        public JsonObject ToJson() => new JsonObject
        {
            ["uid"] = Uid, ["hackId"] = HackId, ["name"] = Name, ["targetId"] = TargetId, ["effect"] = Effect, ["power"] = Power, ["duration"] = Duration, ["timeLeft"] = TimeLeft,
        };

        public static ActiveHack FromJson(JsonObject o) => new ActiveHack
        {
            Uid = o["uid"] as string, HackId = o["hackId"] as string, Name = o["name"] as string, TargetId = o["targetId"] as string, Effect = o["effect"] as string,
            Power = RpgJs.Num(o["power"], "power"), Duration = RpgJs.Num(o["duration"], "duration"), TimeLeft = RpgJs.Num(o["timeLeft"], "timeLeft"),
        };
    }

    /// <summary>The RAM pool and the quickhacks running on targets.</summary>
    public sealed class QuickhackDeck
    {
        public double Ram, MaxRam;
        public List<ActiveHack> Active = new List<ActiveHack>();

        public double SetMaxRam(double max)
        {
            MaxRam = max;
            if (Ram > max) Ram = max;
            return MaxRam;
        }

        public double Fill() => Ram = MaxRam;

        /// <summary>RAM cost after an ICEPICK-style discount.</summary>
        public double CostOf(HackSpec hack, double discount = 0)
        {
            if (hack == null || double.IsNaN(hack.Cost) || double.IsInfinity(hack.Cost)) throw new ArgumentException("costOf: quickhack has no numeric cost");
            return Math.Max(1, hack.Cost - discount);
        }

        public (bool Ok, string Reason, double? Cost, ActiveHack Hack) Upload(Item item, string targetId, double durationScale = 1, double discount = 0)
        {
            if (item?.Hack == null) return (false, "not-a-quickhack", null, null);
            double cost = CostOf(item.Hack, discount);
            if (Ram < cost) return (false, "no-ram", cost, null);
            Ram -= cost;
            double duration = item.Hack.Duration * durationScale;
            var entry = new ActiveHack
            {
                Uid = $"{item.TemplateId}:{targetId}:{Active.Count}", HackId = item.TemplateId, Name = item.Name, TargetId = targetId, Effect = item.Hack.Effect,
                Power = item.Hack.Power, Duration = duration, TimeLeft = duration,
            };
            // the same hack on the same target refreshes rather than stacks
            int existing = Active.FindIndex(a => a.HackId == entry.HackId && a.TargetId == targetId);
            if (existing >= 0) Active[existing] = entry;
            else Active.Add(entry);
            return (true, null, cost, entry);
        }

        public List<ActiveHack> Tick(double dt, double ramRegen)
        {
            Ram = Math.Min(MaxRam, Ram + ramRegen * dt);
            var expired = new List<ActiveHack>();
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                ActiveHack a = Active[i];
                a.TimeLeft -= dt;
                if (a.TimeLeft <= 0)
                {
                    expired.Add(a);
                    Active.RemoveAt(i);
                }
            }
            return expired;
        }

        public List<ActiveHack> ActiveOn(string targetId) => Active.FindAll(a => a.TargetId == targetId);

        public JsonObject Serialize() => new JsonObject
        {
            ["ram"] = RpgJs.Round(Ram * 100) / 100, ["active"] = Active.Select(a => (object)a.ToJson()).ToList(),
        };

        public QuickhackDeck Deserialize(JsonObject data)
        {
            Ram = data != null && data.TryGetValue("ram", out object r) && r != null ? RpgJs.Num(r, "ram") : MaxRam;
            Active = new List<ActiveHack>();
            if (data != null && data.TryGetValue("active", out object a))
            {
                foreach (object o in RpgJs.AsList(a)) Active.Add(ActiveHack.FromJson((JsonObject)o));
            }
            return this;
        }
    }
}
