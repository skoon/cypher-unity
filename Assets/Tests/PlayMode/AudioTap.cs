#if UNITY_EDITOR
using System;
using System.Threading;
using UnityEngine;

namespace Cypher.Tests
{
    /// <summary>
    /// Put on the listener's object: it hears what the listener hears, which is the finished mix (after the mixer's groups and the reverb), and keeps its energy in 50 ms buckets from the last
    /// <see cref="Reset"/>. This is how the tests check levels, since there is nothing else to measure the output with.
    /// </summary>
    public sealed class AudioTap : MonoBehaviour
    {
        public const int BucketsPerSecond = 20;

        readonly double[] _buckets = new double[BucketsPerSecond * 20];
        volatile int _callbacks;
        int _resetRequested;
        long _frames;
        float _peak;
        int _rate = 48000;

        public int Callbacks => _callbacks;
        public float Peak => _peak;

        void Awake() { _rate = AudioSettings.outputSampleRate; }

        void OnAudioFilterRead(float[] data, int channels)
        {
            // a reset is carried out here, on the audio thread, so it cannot race the accumulation
            if (Interlocked.Exchange(ref _resetRequested, 0) == 1)
            {
                Array.Clear(_buckets, 0, _buckets.Length);
                _frames = 0;
                _peak = 0;
            }
            int n = data.Length / channels;
            for (int f = 0; f < n; f++)
            {
                int bucket = (int)((_frames + f) * BucketsPerSecond / _rate);
                for (int c = 0; c < channels; c++)
                {
                    float v = data[f * channels + c];
                    if (bucket < _buckets.Length) _buckets[bucket] += v * v;
                    if (Math.Abs(v) > _peak) _peak = Math.Abs(v);
                }
            }
            _frames += n;
            _callbacks++;
        }

        /// <summary>The root-mean-square level (per channel) over the buckets from..to (50 ms each) since the last reset.</summary>
        public double Rms(int fromBucket, int toBucket)
        {
            double sum = 0;
            for (int i = fromBucket; i < toBucket && i < _buckets.Length; i++) sum += _buckets[i];
            return Math.Sqrt(sum / (Math.Max(1, toBucket - fromBucket) * (_rate / (double)BucketsPerSecond) * 2));
        }

        /// <summary>Start counting again from zero; takes effect at the next audio callback (a few tens of milliseconds).</summary>
        public void Reset() { Volatile.Write(ref _resetRequested, 1); }
    }
}
#endif
