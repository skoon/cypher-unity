using System;
using System.Collections.Generic;
using System.Linq;

namespace Cypher.Core
{
    /// <summary>One answer the player can give, as the conversation screen shows it.</summary>
    public readonly struct DialogueChoiceView
    {
        public readonly string Id, Text;
        /// <summary>The check in front of it, e.g. <c>REFLEXES 6</c>, or empty.</summary>
        public readonly string Gate;
        public readonly bool Locked;
        /// <summary>Picking it ends the conversation.</summary>
        public readonly bool Ends;

        public DialogueChoiceView(string id, string text, string gate, bool locked, bool ends)
        {
            Id = id; Text = text; Gate = gate; Locked = locked; Ends = ends;
        }

        /// <summary>What the gate line says beside the check: whether the player clears it.</summary>
        public string Verdict => Gate.Length == 0 ? "" : Locked ? "INSUFFICIENT" : "PASSED";
    }

    /// <summary>
    /// What the conversation screen shows for a node of the core's dialogue (see <c>DialogueRunner.Current</c>): who is talking, the conversation's title, the
    /// line, and the answers, numbered in order. After the conversation has ended on a line with no answers, the screen keeps that line with a single way out.
    /// </summary>
    public sealed class DialogueModel
    {
        /// <summary>The prototype's typewriter speed.</summary>
        public const double CharactersPerSecond = 62;

        /// <summary>The most answers the number keys reach.</summary>
        public const int NumberKeys = 5;

        public string Speaker { get; private set; } = "";
        public string Title { get; private set; } = "";
        public string Text { get; private set; } = "";
        public IReadOnlyList<DialogueChoiceView> Choices { get; private set; } = Array.Empty<DialogueChoiceView>();

        /// <summary>The conversation is over and only the last line is left on screen.</summary>
        public bool Ended { get; private set; }

        /// <summary>Show a node (the payload's <c>node</c>, or the node itself).</summary>
        public void Show(JsonObject node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (node.TryGetValue("node", out object inner) && inner is JsonObject n) node = n;
            Speaker = Upper(node, "speaker");
            Title = Upper(node, "title");
            Text = node.TryGetValue("text", out object t) && t != null ? t.ToString() : "";
            var choices = new List<DialogueChoiceView>();
            if (node.TryGetValue("options", out object o) && o is List<object> options)
            {
                foreach (JsonObject opt in options.Cast<JsonObject>())
                {
                    choices.Add(new DialogueChoiceView((string)opt["id"], (string)opt["text"], ((opt.TryGetValue("gateLabel", out object g) ? g as string : null) ?? "").ToUpperInvariant(),
                        opt.TryGetValue("locked", out object l) && l is bool lb && lb, opt.TryGetValue("ends", out object e) && e is bool eb && eb));
                }
            }
            Choices = choices;
            Ended = false;
        }

        /// <summary>The conversation ended. True when the screen should stay to let the last line be read: it had words and nothing to answer.</summary>
        public bool End()
        {
            Ended = true;
            Choices = Array.Empty<DialogueChoiceView>();
            return Text.Length > 0;
        }

        /// <summary>The answer a number key (1 based) picks, or null when that key has no answer or the answer is locked.</summary>
        public DialogueChoiceView? ForKey(int number)
        {
            if (number < 1 || number > NumberKeys || number > Choices.Count) return null;
            DialogueChoiceView c = Choices[number - 1];
            return c.Locked ? (DialogueChoiceView?)null : c;
        }

        /// <summary>How many characters of the line show after <paramref name="seconds"/> of typing.</summary>
        public int Revealed(double seconds) => (int)Math.Min(Text.Length, Math.Floor(Math.Max(0, seconds) * CharactersPerSecond));

        static string Upper(JsonObject o, string key) => o.TryGetValue(key, out object v) && v != null ? v.ToString().ToUpperInvariant() : "";
    }
}
