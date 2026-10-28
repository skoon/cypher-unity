using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>What the dialogue runner reads from and does to the rest of the game.</summary>
    public interface IDialogueHost
    {
        void Emit(string type, JsonObject payload);
        void ApplyEffects(List<object> effects, JsonObject context);
        IConditionView View { get; }
        /// <summary>Told of every ending of a conversation, however it ended, so "talk to X" objectives never miss.</summary>
        void OnDialogueEnd(string graphId);
    }

    /// <summary>
    /// Branching conversations with stat- and state-gated options: a port of <c>rpg/Dialogue.js</c>. A graph is a set of nodes, each a line and some options. An
    /// option with a <c>condition</c> is hidden when it fails; one with a <c>gate</c> is shown, prefixed <c>[INTELLIGENCE 6]</c>, and locked (seeing what you cannot
    /// say is half the point of a skill check); <c>effects</c> run when it is chosen; a <c>once</c> option burns after use. A graph's <c>entries</c> pick the first
    /// node by condition. The graphs are read from <see cref="RpgContent.DialoguesJson"/> and checked once.
    /// </summary>
    public sealed class DialogueRunner
    {
        static List<JsonObject> _shipped;

        public static IReadOnlyList<JsonObject> Shipped
        {
            get
            {
                if (_shipped == null)
                {
                    var list = ((List<object>)Json.Parse(RpgContent.DialoguesJson)).Cast<JsonObject>().ToList();
                    Validate(list);
                    _shipped = list;
                }
                return _shipped;
            }
        }

        static object Get(JsonObject o, string key) => o.TryGetValue(key, out object v) ? v : null;
        static string Str(JsonObject o, string key) => Get(o, key) as string;
        static List<JsonObject> Objs(object v) => RpgJs.AsList(v).Cast<JsonObject>().ToList();

        /// <summary>Check the graphs: a missing node or a bad gate is a hard error when the table is read.</summary>
        public static void Validate(IReadOnlyList<JsonObject> graphs)
        {
            var seen = new HashSet<string>();
            foreach (JsonObject g in graphs)
            {
                string gid = Str(g, "id");
                if (!seen.Add(gid)) throw new InvalidOperationException($"Duplicate dialogue id \"{gid}\"");
                var nodes = (JsonObject)g["nodes"];
                if (!nodes.ContainsKey(Str(g, "start") ?? "")) throw new InvalidOperationException($"Dialogue \"{gid}\" start node \"{Str(g, "start")}\" does not exist");
                foreach (JsonObject e in Objs(Get(g, "entries")))
                {
                    RpgConditions.Assert(Get(e, "when"), $"{gid}.entry");
                    if (!nodes.ContainsKey(Str(e, "node") ?? "")) throw new InvalidOperationException($"Dialogue \"{gid}\" entry points at missing node \"{Str(e, "node")}\"");
                }
                foreach (KeyValuePair<string, object> kv in nodes)
                {
                    var node = (JsonObject)kv.Value;
                    RpgEffects.AssertAll(Get(node, "effects"), $"{gid}.{kv.Key}.effects");
                    if (string.IsNullOrEmpty(Str(node, "text"))) throw new InvalidOperationException($"Dialogue node \"{gid}.{kv.Key}\" has no text");
                    var optIds = new HashSet<string>();
                    foreach (JsonObject opt in Objs(Get(node, "options")))
                    {
                        string oid = Str(opt, "id");
                        if (!optIds.Add(oid)) throw new InvalidOperationException($"Duplicate option \"{oid}\" in \"{gid}.{kv.Key}\"");
                        RpgConditions.Assert(Get(opt, "gate"), $"{gid}.{kv.Key}.{oid}.gate");
                        RpgConditions.Assert(Get(opt, "condition"), $"{gid}.{kv.Key}.{oid}.condition");
                        RpgEffects.AssertAll(Get(opt, "effects"), $"{gid}.{kv.Key}.{oid}.effects");
                        string to = Str(opt, "to");
                        if (!string.IsNullOrEmpty(to) && !nodes.ContainsKey(to)) throw new InvalidOperationException($"Option \"{gid}.{kv.Key}.{oid}\" points at missing node \"{to}\"");
                    }
                    string nodeTo = Str(node, "to");
                    if (!string.IsNullOrEmpty(nodeTo) && !nodes.ContainsKey(nodeTo)) throw new InvalidOperationException($"Node \"{gid}.{kv.Key}\" auto-advances to missing node \"{nodeTo}\"");
                }
            }
        }

        readonly IDialogueHost _host;
        readonly Dictionary<string, JsonObject> _graphs = new Dictionary<string, JsonObject>();
        public readonly IReadOnlyList<JsonObject> Graphs;
        readonly List<string> _usedOptions = new List<string>();
        readonly List<string> _seenGraphs = new List<string>();
        bool _ending;

        public string GraphId { get; private set; }
        public string NodeId { get; private set; }

        public DialogueRunner(IDialogueHost host, IReadOnlyList<JsonObject> graphs = null)
        {
            _host = host ?? throw new ArgumentException("DialogueRunner: host is required");
            Graphs = graphs ?? Shipped;
            foreach (JsonObject g in Graphs) _graphs[Str(g, "id")] = g;
        }

        public JsonObject Graph(string id)
        {
            if (id == null || !_graphs.TryGetValue(id, out JsonObject g)) throw new ArgumentOutOfRangeException(nameof(id), $"Unknown dialogue id \"{id}\"");
            return g;
        }

        public bool Active => GraphId != null;

        public IReadOnlyList<string> SeenGraphs => _seenGraphs;

        /// <summary>The node a graph opens on now, by its entries.</summary>
        public string EntryNode(string graphId)
        {
            JsonObject g = Graph(graphId);
            foreach (JsonObject e in Objs(Get(g, "entries")))
            {
                if (RpgConditions.Check(Get(e, "when"), _host.View).Ok) return Str(e, "node");
            }
            return Str(g, "start");
        }

        public RpgResult Start(string graphId, string node = null)
        {
            if (Active) return RpgResult.Fail("already-active");
            JsonObject g = Graph(graphId);
            GraphId = graphId;
            _ending = false;
            string first = node ?? EntryNode(graphId);
            if (!((JsonObject)g["nodes"]).ContainsKey(first)) throw new ArgumentOutOfRangeException(nameof(node), $"Dialogue \"{graphId}\" has no node \"{first}\"");
            _host.Emit("rpg:dialogueStart", new JsonObject { ["graphId"] = graphId, ["title"] = Get(g, "title"), ["speaker"] = Get(g, "speaker") });
            Enter(first);
            return new RpgResult { Ok = true, Extra = new JsonObject { ["node"] = Current() } };
        }

        void Enter(string nodeId)
        {
            JsonObject g = Graph(GraphId);
            var nodes = (JsonObject)g["nodes"];
            if (!nodes.TryGetValue(nodeId, out object no)) throw new ArgumentOutOfRangeException(nameof(nodeId), $"Dialogue \"{GraphId}\" has no node \"{nodeId}\"");
            var node = (JsonObject)no;
            NodeId = nodeId;
            _host.ApplyEffects(RpgJs.AsList(Get(node, "effects")), new JsonObject { ["dialogueId"] = GraphId, ["nodeId"] = nodeId });
            if (_ending) return;
            _host.Emit("rpg:dialogueNode", new JsonObject { ["node"] = Current() });
            // a node with no options and nowhere to go closes the conversation
            int optionCount = Objs(Get(node, "options")).Count;
            string to = Str(node, "to");
            if (optionCount == 0 && string.IsNullOrEmpty(to)) End();
            else if (!string.IsNullOrEmpty(to) && optionCount == 0) Enter(to);
        }

        /// <summary>The node as the HUD shows it, options resolved; null when no one is talking.</summary>
        public JsonObject Current()
        {
            if (!Active) return null;
            JsonObject g = Graph(GraphId);
            var node = (JsonObject)((JsonObject)g["nodes"])[NodeId];
            return new JsonObject
            {
                ["graphId"] = GraphId, ["nodeId"] = NodeId, ["title"] = Get(g, "title"), ["speaker"] = Get(node, "speaker") ?? Get(g, "speaker"), ["text"] = Get(node, "text"),
                ["options"] = Options().Cast<object>().ToList(),
            };
        }

        /// <summary>The current node's options: hidden ones (a failed condition, a spent <c>once</c>) dropped, gated ones locked and labelled.</summary>
        public List<JsonObject> Options()
        {
            var out_ = new List<JsonObject>();
            if (!Active) return out_;
            var node = (JsonObject)((JsonObject)Graph(GraphId)["nodes"])[NodeId];
            foreach (JsonObject opt in Objs(Get(node, "options")))
            {
                string oid = Str(opt, "id");
                if (RpgJs.Truthy(Get(opt, "once")) && _usedOptions.Contains($"{GraphId}:{oid}")) continue;
                if (RpgJs.Truthy(Get(opt, "condition")) && !RpgConditions.Check(opt["condition"], _host.View).Ok) continue;
                bool gated = RpgJs.Truthy(Get(opt, "gate"));
                (bool ok, List<string> missing) = gated ? RpgConditions.Check(opt["gate"], _host.View) : (true, new List<string>());
                string gateLabel = gated ? RpgConditions.Describe(opt["gate"]) : "";
                string text = Str(opt, "text");
                out_.Add(new JsonObject
                {
                    ["id"] = oid, ["text"] = text, ["gateLabel"] = gateLabel, ["label"] = gateLabel.Length > 0 ? $"[{gateLabel}] {text}" : text, ["locked"] = !ok,
                    ["missing"] = missing.Cast<object>().ToList(), ["ends"] = string.IsNullOrEmpty(Str(opt, "to")),
                });
            }
            return out_;
        }

        public RpgResult Choose(string optionId)
        {
            if (!Active) return RpgResult.Fail("no-dialogue");
            var node = (JsonObject)((JsonObject)Graph(GraphId)["nodes"])[NodeId];
            JsonObject opt = Objs(Get(node, "options")).FirstOrDefault(o => Str(o, "id") == optionId);
            if (opt == null) return RpgResult.Fail("no-such-option");
            if (RpgJs.Truthy(Get(opt, "condition")) && !RpgConditions.Check(opt["condition"], _host.View).Ok) return RpgResult.Fail("hidden");
            if (RpgJs.Truthy(Get(opt, "gate")))
            {
                (bool ok, List<string> missing) = RpgConditions.Check(opt["gate"], _host.View);
                if (!ok) return new RpgResult { Ok = false, Reason = "locked", Missing = missing };
            }
            if (RpgJs.Truthy(Get(opt, "once")) && !_usedOptions.Contains($"{GraphId}:{optionId}")) _usedOptions.Add($"{GraphId}:{optionId}");

            string graphId = GraphId;
            _host.Emit("rpg:dialogueChoice", new JsonObject { ["graphId"] = graphId, ["nodeId"] = NodeId, ["optionId"] = optionId, ["text"] = Get(opt, "text") });
            _host.ApplyEffects(RpgJs.AsList(Get(opt, "effects")), new JsonObject { ["dialogueId"] = graphId, ["optionId"] = optionId });
            if (_ending || !Active) return new RpgResult { Ok = true, Extra = new JsonObject { ["ended"] = true } };
            string to = Str(opt, "to");
            if (string.IsNullOrEmpty(to))
            {
                End();
                return new RpgResult { Ok = true, Extra = new JsonObject { ["ended"] = true } };
            }
            Enter(to);
            return new RpgResult { Ok = true, Extra = new JsonObject { ["node"] = Current(), ["ended"] = !Active } };
        }

        public RpgResult End()
        {
            if (!Active) return RpgResult.Fail("no-dialogue");
            string graphId = GraphId;
            _ending = true;
            GraphId = null;
            NodeId = null;
            if (!_seenGraphs.Contains(graphId)) _seenGraphs.Add(graphId);
            _host.Emit("rpg:dialogueEnd", new JsonObject { ["graphId"] = graphId });
            _host.OnDialogueEnd(graphId);
            return new RpgResult { Ok = true, Extra = new JsonObject { ["graphId"] = graphId } };
        }

        public JsonObject Serialize() => new JsonObject
        {
            ["usedOptions"] = _usedOptions.Cast<object>().ToList(), ["seenGraphs"] = _seenGraphs.Cast<object>().ToList(),
        };

        public DialogueRunner Deserialize(JsonObject data)
        {
            _usedOptions.Clear();
            _seenGraphs.Clear();
            if (data != null)
            {
                foreach (object o in RpgJs.AsList(Get(data, "usedOptions"))) if (o is string s && !_usedOptions.Contains(s)) _usedOptions.Add(s);
                foreach (object o in RpgJs.AsList(Get(data, "seenGraphs"))) if (o is string s && !_seenGraphs.Contains(s)) _seenGraphs.Add(s);
            }
            GraphId = null;
            NodeId = null;
            return this;
        }
    }
}
