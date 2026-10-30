using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>How a cell of the code matrix looks: already picked, pickable now, or out of the current line.</summary>
    public enum BreachCell { Spent, Live, Dim }

    /// <summary>A daemon as the breach screen lists it: its sequence, how much of it the end of the buffer already matches, and whether it is in, out or still possible.</summary>
    public readonly struct BreachDaemonView
    {
        public readonly string Id, Name, Desc;
        public readonly IReadOnlyList<string> Sequence;
        public readonly bool Solved;
        /// <summary>Too few buffer slots are left to finish it.</summary>
        public readonly bool Failed;
        /// <summary>How many of its first codes the end of the buffer matches now (lit on screen).</summary>
        public readonly int Matched;

        public BreachDaemonView(string id, string name, string desc, IReadOnlyList<string> sequence, bool solved, bool failed, int matched)
        {
            Id = id; Name = name; Desc = desc; Sequence = sequence; Solved = solved; Failed = failed; Matched = matched;
        }
    }

    /// <summary>
    /// What the breach screen shows of the core's breach (see <c>RpgCore.GetBreach</c>): the matrix with each cell spent, live or dim, the buffer, the daemons with
    /// their progress (the prototype's <c>ui/Breach.js</c> rules: a daemon fails when the slots left cannot finish it, and the codes the buffer's tail already
    /// matches are lit), and the timer. Pure.
    /// </summary>
    public sealed class BreachModel
    {
        /// <summary>The timer turns urgent below this share of the time.</summary>
        public const double UrgentShare = 0.25;

        public string TargetName { get; private set; } = "";
        public int Size { get; private set; }
        public string[][] Matrix { get; private set; } = Array.Empty<string[]>();
        public IReadOnlyList<string> Buffer { get; private set; } = Array.Empty<string>();
        public int BufferSize { get; private set; }
        /// <summary>"row" or "col": the line the next pick comes from.</summary>
        public string Axis { get; private set; } = "row";
        public int Lock { get; private set; }
        public double TimeLeft { get; private set; }
        public double TimeLimit { get; private set; }
        /// <summary>running, done or failed.</summary>
        public string State { get; private set; } = "running";
        public IReadOnlyList<BreachDaemonView> Daemons { get; private set; } = Array.Empty<BreachDaemonView>();

        /// <summary>The cells picked so far, in order.</summary>
        public IReadOnlyList<(int R, int C, string Code)> Picks { get; private set; } = Array.Empty<(int, int, string)>();

        readonly HashSet<(int, int)> _used = new HashSet<(int, int)>();
        readonly HashSet<(int, int)> _legal = new HashSet<(int, int)>();

        public bool Running => State == "running";
        public double TimeShare => TimeLimit > 0 ? Math.Max(0, Math.Min(1, TimeLeft / TimeLimit)) : 0;
        public bool Urgent => Running && TimeShare < UrgentShare;

        /// <summary>Read a session (the payload's <c>session</c>, or the session itself).</summary>
        public void Show(JsonObject session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.TryGetValue("session", out object inner) && inner is JsonObject s) session = s;
            TargetName = ((session.TryGetValue("targetName", out object n) ? n as string : null) ?? (string)session["targetId"]).ToUpperInvariant();
            Size = (int)(double)session["size"];
            Matrix = ((List<object>)session["matrix"]).Select(row => ((List<object>)row).Cast<string>().ToArray()).ToArray();
            Buffer = ((List<object>)session["buffer"]).Cast<string>().ToList();
            BufferSize = (int)(double)session["bufferSize"];
            Axis = (string)session["axis"];
            Lock = (int)(double)session["lock"];
            TimeLeft = (double)session["timeLeft"];
            TimeLimit = (double)session["timeLimit"];
            State = (string)session["state"];

            _used.Clear();
            var picks = new List<(int, int, string)>();
            foreach (JsonObject p in ((List<object>)session["picks"]).Cast<JsonObject>())
            {
                picks.Add(((int)(double)p["r"], (int)(double)p["c"], (string)p["code"]));
                _used.Add(((int)(double)p["r"], (int)(double)p["c"]));
            }
            Picks = picks;
            _legal.Clear();
            if (session.TryGetValue("legal", out object legal) && legal is List<object> cells)
            {
                foreach (JsonObject p in cells.Cast<JsonObject>()) _legal.Add(((int)(double)p["r"], (int)(double)p["c"]));
            }

            var daemons = new List<BreachDaemonView>();
            foreach (JsonObject d in ((List<object>)session["daemons"]).Cast<JsonObject>())
            {
                List<string> seq = ((List<object>)d["sequence"]).Cast<string>().ToList();
                bool solved = (bool)d["solved"];
                int matched = solved ? 0 : MatchOffset(Buffer, seq);
                bool failed = !solved && !CanStillFit(Buffer, seq, BufferSize);
                daemons.Add(new BreachDaemonView((string)d["id"], ((string)d["name"]).ToUpperInvariant(), d.TryGetValue("desc", out object desc) ? desc as string ?? "" : "", seq, solved, failed, matched));
            }
            Daemons = daemons;
        }

        /// <summary>The cell's look now. Once the breach is over nothing is live.</summary>
        public BreachCell Cell(int r, int c)
        {
            if (_used.Contains((r, c))) return BreachCell.Spent;
            return Running && _legal.Contains((r, c)) ? BreachCell.Live : BreachCell.Dim;
        }

        /// <summary>Whether a cell lies on the line the next pick must come from (the screen bands it).</summary>
        public bool OnLine(int r, int c) => Running && (Axis == "row" ? r == Lock : c == Lock);

        /// <summary>How many leading codes of <paramref name="seq"/> the end of the buffer matches (the prototype's <c>matchOffset</c>).</summary>
        public static int MatchOffset(IReadOnlyList<string> buffer, IReadOnlyList<string> seq)
        {
            for (int start = 0; start < buffer.Count; start++)
            {
                int k = 0;
                while (start + k < buffer.Count && k < seq.Count && buffer[start + k] == seq[k]) k++;
                if (start + k == buffer.Count && k > 0) return k;
            }
            return 0;
        }

        /// <summary>Whether the slots left can still finish <paramref name="seq"/> (the prototype's <c>canStillFit</c>).</summary>
        public static bool CanStillFit(IReadOnlyList<string> buffer, IReadOnlyList<string> seq, int size) => size - buffer.Count >= seq.Count - MatchOffset(buffer, seq);

        /// <summary>The line the screen writes when the breach is over.</summary>
        public string Outcome()
        {
            int solved = Daemons.Count(d => d.Solved);
            return State == "running" ? "" : solved > 0 ? $"BREACH COMPLETE // {solved} DAEMON{(solved == 1 ? "" : "S")} LIVE" : "BREACH FAILED";
        }
    }
}
