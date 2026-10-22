using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>
    /// The messages shown as toasts: newest at the bottom, at most <see cref="MaxVisible"/> at once, each gone after
    /// <see cref="LifetimeSeconds"/>. The same message arriving again while it is still showing refreshes its time instead of stacking up.
    /// Time is passed in, so the queue is tested without waiting.
    /// </summary>
    public sealed class NoticeQueue
    {
        public const int MaxVisible = 3;
        public const double LifetimeSeconds = 10;

        sealed class Entry
        {
            public string Text;
            public double ExpiresAt;
        }

        readonly List<Entry> _entries = new List<Entry>();

        public void Add(string text, double now)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Prune(now);

            Entry existing = _entries.Find(e => e.Text == text);
            if (existing != null)
            {
                existing.ExpiresAt = now + LifetimeSeconds;
                return;
            }

            _entries.Add(new Entry { Text = text, ExpiresAt = now + LifetimeSeconds });
            while (_entries.Count > MaxVisible) _entries.RemoveAt(0);
        }

        /// <summary>The messages showing at time <paramref name="now"/>, oldest first.</summary>
        public IReadOnlyList<string> Visible(double now)
        {
            Prune(now);
            var result = new List<string>(_entries.Count);
            foreach (Entry e in _entries) result.Add(e.Text);
            return result;
        }

        /// <summary>Remove the message at <paramref name="index"/> of <see cref="Visible"/>, for the player closing one.</summary>
        public bool Dismiss(int index, double now)
        {
            Prune(now);
            if (index < 0 || index >= _entries.Count) return false;
            _entries.RemoveAt(index);
            return true;
        }

        public void Clear() => _entries.Clear();

        void Prune(double now) => _entries.RemoveAll(e => e.ExpiresAt <= now);
    }
}
