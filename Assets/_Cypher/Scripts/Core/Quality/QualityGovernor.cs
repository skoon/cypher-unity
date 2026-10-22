using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cypher.Core
{
    public enum QualityChangeReason
    {
        /// <summary>The governor lowered it because frames missed the budget.</summary>
        Adaptive,

        /// <summary>The player chose it.</summary>
        Player,
    }

    /// <summary>A change of preset, for the notice the player sees and for saving it.</summary>
    public readonly struct QualityChange
    {
        public readonly QualityPreset From;
        public readonly QualityPreset To;
        public readonly double Fps;
        public readonly QualityChangeReason Reason;

        public QualityChange(QualityPreset from, QualityPreset to, double fps, QualityChangeReason reason)
        {
            From = from;
            To = to;
            Fps = fps;
            Reason = reason;
        }

        /// <summary>Plain-language message for a downgrade; null for a change the player made themselves.</summary>
        public string Notice() => Reason != QualityChangeReason.Adaptive ? null : string.Format(CultureInfo.InvariantCulture,
            "Quality lowered from {0} to {1} because the frame rate was about {2:0} FPS. You can raise it in Options.", From, To, Fps);
    }

    /// <summary>
    /// Decides when to drop a quality preset to hold the frame-rate floor. Pure: frame times in, presets out. Ported from the
    /// prototype's QualityGovernor, with its reasoning intact:
    /// <list type="bullet">
    /// <item>Downgrade-only and sticky: it never climbs back on its own, so a machine sitting on the threshold cannot
    /// flicker between presets. The player raises it by hand and that becomes the new ceiling (<see cref="Reset"/>).</item>
    /// <item>Nothing counts during warm-up: boot and every switch cost slow frames that do not describe steady state.</item>
    /// <item>A cool-down follows each downgrade, because applying a preset itself costs frames.</item>
    /// <item>Judgement is on the median of a rolling window, plus the share of frames over budget, so one hitch is not
    /// a trend but a sustained stutter still counts.</item>
    /// <item>Pause time is not a stall: <see cref="Suspend"/> drops it and re-arms warm-up.</item>
    /// </list>
    /// </summary>
    public sealed class QualityGovernor
    {
        public const double TargetFps = 30;

        /// <summary>A median under target times this drops two presets at once instead of one.</summary>
        public const double DefaultSevereRatio = 0.5;

        /// <summary>Share of a full window over the frame budget that counts as sustained stutter.</summary>
        const double SlowFractionLimit = 0.10;

        readonly double _target;
        readonly double _budget;
        readonly double _window;
        readonly double _evalEvery;
        readonly double _warmup;
        readonly double _cooldown;
        readonly double _severeRatio;
        readonly Queue<double> _deltas = new Queue<double>();
        double _span;
        double _sinceEval;
        double _blocked;

        public QualityGovernor(QualityPreset quality = QualityPreset.Ultra, double target = TargetFps, double window = 2.0, double evalEvery = 0.5,
            double warmup = 5.0, double cooldown = 3.0, double severeRatio = DefaultSevereRatio, bool enabled = true)
        {
            if (!(target > 0)) throw new ArgumentOutOfRangeException(nameof(target), target, "target must be positive");
            if (!(window > 0)) throw new ArgumentOutOfRangeException(nameof(window), window, "window must be positive");
            if (!(evalEvery > 0)) throw new ArgumentOutOfRangeException(nameof(evalEvery), evalEvery, "evaluation interval must be positive");
            if (!(warmup >= 0)) throw new ArgumentOutOfRangeException(nameof(warmup), warmup, "warm-up must not be negative");
            if (!(cooldown >= 0)) throw new ArgumentOutOfRangeException(nameof(cooldown), cooldown, "cool-down must not be negative");
            if (!(severeRatio >= 0)) throw new ArgumentOutOfRangeException(nameof(severeRatio), severeRatio, "severe ratio must not be negative");
            RequireKnown(quality);

            _target = target;
            _budget = 1.0 / target;
            _window = window;
            _evalEvery = evalEvery;
            _warmup = warmup;
            _cooldown = cooldown;
            _severeRatio = severeRatio;
            _blocked = warmup;
            Quality = quality;
            Enabled = enabled;
        }

        public QualityPreset Quality { get; private set; }

        /// <summary>When false it still measures, so the readout stays live, but never acts and never changes <see cref="Quality"/>.</summary>
        public bool Enabled { get; set; }

        /// <summary>Median frame rate over the window, or 0 before there is a full window to judge.</summary>
        public double Fps { get; private set; }

        /// <summary>Share of the last judged window that ran over the frame budget.</summary>
        public double SlowFraction { get; private set; }

        /// <summary>True while samples are collected but cannot trigger a downgrade.</summary>
        public bool Settling => _blocked > 0;

        /// <summary>At the cheapest preset there is nothing left to give up.</summary>
        public bool AtFloor => Quality == QualityPreset.Performance;

        /// <summary>Adopt a preset and re-arm warm-up: for the player's own choice, so stale history cannot overrule it.</summary>
        public void Reset(QualityPreset quality)
        {
            RequireKnown(quality);
            Quality = quality;
            ClearHistory();
            Fps = 0;
            SlowFraction = 0;
            _blocked = _warmup;
        }

        /// <summary>Stop counting and re-arm, for pause and menus where time passes but no frames are shown.</summary>
        public void Suspend()
        {
            ClearHistory();
            Fps = 0;
            SlowFraction = 0;
            _blocked = Math.Max(_blocked, _warmup);
        }

        /// <summary>
        /// Feed one frame (unscaled, so slow motion is not read as a collapse). Returns the preset to switch to, or null.
        /// </summary>
        public QualityPreset? Sample(double dt)
        {
            if (!(dt > 0) || double.IsInfinity(dt)) return null;   // paused, first frame, or a dead clock

            _deltas.Enqueue(dt);
            _span += dt;
            while (_deltas.Count > 1 && _span - _deltas.Peek() >= _window) _span -= _deltas.Dequeue();

            if (_blocked > 0)
            {
                _blocked -= dt;
                return null;
            }

            _sinceEval += dt;
            if (_sinceEval < _evalEvery) return null;
            _sinceEval = 0;

            if (_span < _window) return null;   // a short window is still boot or a resume

            double median = Median(_deltas);
            Fps = median > 0 ? 1.0 / median : 0;
            // A median can hide sustained missed frames: ignore isolated hitches, but act when 10% of a window is over budget.
            double limit = _budget * 1.005;
            SlowFraction = (double)_deltas.Count(d => d > limit) / _deltas.Count;
            if (median <= _budget && SlowFraction < SlowFractionLimit) return null;
            if (!Enabled) return null;

            return StepDown(median);
        }

        QualityPreset? StepDown(double median)
        {
            int i = (int)Quality;
            if (i <= 0) return null;
            bool severe = median > _budget / _severeRatio;
            QualityPreset next = (QualityPreset)Math.Max(0, i - (severe ? 2 : 1));

            Quality = next;
            _blocked = _cooldown;
            ClearHistory();
            return next;
        }

        void ClearHistory()
        {
            _deltas.Clear();
            _span = 0;
            _sinceEval = 0;
        }

        static void RequireKnown(QualityPreset quality)
        {
            if (!Enum.IsDefined(typeof(QualityPreset), quality)) throw new ArgumentOutOfRangeException(nameof(quality), quality, "unknown quality preset");
        }

        static double Median(IEnumerable<double> values)
        {
            double[] s = values.ToArray();
            Array.Sort(s);
            int mid = s.Length >> 1;
            return s.Length % 2 == 1 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
        }
    }
}
