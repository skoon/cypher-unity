using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    public readonly struct LoadStep
    {
        public readonly string Name;

        /// <summary>What the loading screen says while this step is the one in progress.</summary>
        public readonly string Label;

        /// <summary>Share of the whole bar this step covers, relative to the other steps.</summary>
        public readonly double Weight;

        public LoadStep(string name, string label, double weight)
        {
            Name = name;
            Label = label;
            Weight = weight;
        }
    }

    /// <summary>
    /// Progress of a load made of weighted steps, for the loading bar. Steps report a fraction that only ever grows; the bar
    /// never runs backwards, and a step reporting something impossible is a bug that throws instead of drawing a wrong bar.
    /// </summary>
    public sealed class LoadTracker
    {
        readonly List<LoadStep> _steps;
        readonly Dictionary<string, int> _index = new Dictionary<string, int>();
        readonly double[] _fractions;
        readonly double _totalWeight;

        public LoadTracker(IReadOnlyList<LoadStep> steps)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            if (steps.Count == 0) throw new ArgumentException("a load needs at least one step", nameof(steps));
            _steps = new List<LoadStep>(steps);
            for (int i = 0; i < _steps.Count; i++)
            {
                LoadStep s = _steps[i];
                if (string.IsNullOrWhiteSpace(s.Name)) throw new ArgumentException("every step needs a name", nameof(steps));
                if (!(s.Weight > 0) || double.IsInfinity(s.Weight)) throw new ArgumentOutOfRangeException(nameof(steps), s.Weight, $"step '{s.Name}' needs a positive, finite weight");
                if (_index.ContainsKey(s.Name)) throw new ArgumentException($"duplicate step '{s.Name}'", nameof(steps));
                _index[s.Name] = i;
                _totalWeight += s.Weight;
            }
            _fractions = new double[_steps.Count];
        }

        /// <summary>0 to 1, weighted across all steps.</summary>
        public double Progress
        {
            get
            {
                double done = 0;
                for (int i = 0; i < _steps.Count; i++) done += _steps[i].Weight * _fractions[i];
                return Math.Min(1.0, done / _totalWeight);
            }
        }

        public bool IsDone
        {
            get
            {
                foreach (double f in _fractions)
                {
                    if (f < 1.0) return false;
                }
                return true;
            }
        }

        /// <summary>The label of the first unfinished step, or "Ready" when all are done.</summary>
        public string Status
        {
            get
            {
                for (int i = 0; i < _steps.Count; i++)
                {
                    if (_fractions[i] < 1.0) return _steps[i].Label;
                }
                return "Ready";
            }
        }

        public double Fraction(string step) => _fractions[Find(step)];

        public void Set(string step, double fraction)
        {
            int i = Find(step);
            if (!(fraction >= 0 && fraction <= 1)) throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "a step's fraction must be between 0 and 1");
            if (fraction < _fractions[i]) throw new InvalidOperationException($"Step '{step}' went backwards, from {_fractions[i]} to {fraction}.");
            _fractions[i] = fraction;
        }

        public void Complete(string step) => Set(step, 1.0);

        int Find(string step)
        {
            if (step == null || !_index.TryGetValue(step, out int i)) throw new ArgumentException($"Unknown load step '{step}'", nameof(step));
            return i;
        }
    }
}
