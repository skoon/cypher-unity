using System;
using System.Threading;

namespace Cypher.Core
{
    /// <summary>
    /// The last stage of the mix, as the prototype's master chain has it: a gentle glue compressor, then a fast limiter, then the master volume (after the limiter, so the volume slider does
    /// not change how hard the compressors work). It runs on the finished mix, on the audio thread. The volume follows its target smoothly, and the first seconds fade up from silence so
    /// the game does not begin with a bang.
    /// </summary>
    public sealed class MasterStage
    {
        /// <summary>The prototype's master level before the slider.</summary>
        public const double BaseGain = 0.9;
        public const double FadeInSeconds = 1.6;
        const double VolumeTimeConstant = 0.05;
        const int Chunk = 1024;

        readonly int _rate;
        readonly DynamicsCompressor _glue, _limiter;
        readonly float[] _left = new float[Chunk], _right = new float[Chunk];
        double _target = BaseGain;
        double _smoothed = BaseGain;
        double _fade;
        double _fadeStep;
        bool _fading;
        bool _started;

        public MasterStage(int sampleRate)
        {
            _rate = sampleRate;
            _glue = new DynamicsCompressor(sampleRate, 2) { Threshold = -20, Knee = 22, Ratio = 3, Attack = 0.012, Release = 0.28 };
            _limiter = new DynamicsCompressor(sampleRate, 2) { Threshold = -2.5, Knee = 0, Ratio = 20, Attack = 0.0016, Release = 0.09 };
            _fade = 1;
        }

        /// <summary>The volume to settle at (linear), set from any thread. The base level times the slider; 0 mutes.</summary>
        public double Target
        {
            get => Volatile.Read(ref _target);
            set
            {
                if (double.IsNaN(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "a volume cannot be negative");
                Volatile.Write(ref _target, value);
            }
        }

        /// <summary>Begin again from silence and come up over <see cref="FadeInSeconds"/>; takes effect at the next block.</summary>
        public void FadeIn()
        {
            Volatile.Write(ref _fading, true);
        }

        /// <summary>Frames the compressors delay the sound by (their look-ahead).</summary>
        public int LatencyFrames => _glue.LatencyFrames + _limiter.LatencyFrames;

        /// <summary>Process an interleaved block in place.</summary>
        public void Process(float[] data, int channels, int frames)
        {
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels), channels, "no channels");
            if (frames * channels > data.Length) throw new ArgumentException("the buffer is too short", nameof(data));
            if (Volatile.Read(ref _fading))
            {
                Volatile.Write(ref _fading, false);
                _fade = 0;
                _fadeStep = 1.0 / (FadeInSeconds * _rate);
            }
            double target = Target;
            if (!_started)
            {
                // a volume set before the sound begins is where it starts, not something to glide to
                _started = true;
                _smoothed = target;
            }
            double smoothing = 1 - Math.Exp(-1.0 / (VolumeTimeConstant * _rate));
            int done = 0;
            while (done < frames)
            {
                int n = Math.Min(Chunk, frames - done);
                for (int i = 0; i < n; i++)
                {
                    int o = (done + i) * channels;
                    _left[i] = data[o];
                    _right[i] = channels > 1 ? data[o + 1] : data[o];
                }
                _glue.Process(_left, _right, n);
                _limiter.Process(_left, _right, n);
                for (int i = 0; i < n; i++)
                {
                    _smoothed += (target - _smoothed) * smoothing;
                    double gain = _smoothed * _fade;
                    if (_fade < 1) _fade = Math.Min(1, _fade + _fadeStep);
                    int o = (done + i) * channels;
                    if (channels == 1)
                    {
                        data[o] = (float)((_left[i] + _right[i]) * 0.5 * gain);
                    }
                    else
                    {
                        data[o] = (float)(_left[i] * gain);
                        data[o + 1] = (float)(_right[i] * gain);
                        for (int c = 2; c < channels; c++) data[o + c] *= (float)gain;
                    }
                }
                done += n;
            }
        }
    }
}
