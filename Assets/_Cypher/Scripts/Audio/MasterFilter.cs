using System;
using System.Threading;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Audio
{
    /// <summary>
    /// On the listener's object: the last stage of the mix. Unity hands this the finished mix after the mixer, and <see cref="MasterStage"/> glues, limits and sets the master volume on it, as
    /// the prototype's master chain does. Runs on the audio thread; a failure is kept for the main thread.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MasterFilter : MonoBehaviour
    {
        MasterStage _stage;
        Exception _failure;
        int _callbacks;
        long _ticks;
        long _worstTicks;

        public MasterStage Stage => _stage;

        public int Callbacks => Volatile.Read(ref _callbacks);

        public long Ticks => Interlocked.Read(ref _ticks);

        public double WorstMilliseconds => Interlocked.Read(ref _worstTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        public double MillisecondsPerCallback
        {
            get
            {
                int n = Callbacks;
                return n == 0 ? 0 : Ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / n;
            }
        }

        public Exception Failure => Volatile.Read(ref _failure);

        /// <summary>Start processing at the device's sample rate.</summary>
        public void Begin(int sampleRate)
        {
            _stage = new MasterStage(sampleRate);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            MasterStage stage = _stage;
            if (stage == null || Volatile.Read(ref _failure) != null) return;
            try
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                stage.Process(data, channels, data.Length / channels);
                long took = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                Interlocked.Add(ref _ticks, took);
                if (took > Volatile.Read(ref _worstTicks)) Interlocked.Exchange(ref _worstTicks, took);
                Interlocked.Increment(ref _callbacks);
            }
            catch (Exception e)
            {
                Volatile.Write(ref _failure, e);
            }
        }
    }
}
