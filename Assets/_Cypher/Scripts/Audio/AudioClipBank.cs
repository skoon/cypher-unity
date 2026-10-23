using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Audio
{
    /// <summary>
    /// The sounds made ahead of time. Worker threads render each <see cref="ClipSpec"/> with Core's recipes (nothing here touches the engine, so it can), and the main thread turns the finished
    /// samples into <see cref="AudioClip"/>s a few at a time, so no frame is spent synthesising and none is spent making a pile of clips at once.
    /// </summary>
    public sealed class AudioClipBank : IDisposable
    {
        readonly int _rate;
        readonly AudioNoiseBank _noise;
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, RenderedSound> _sounds = new Dictionary<string, RenderedSound>();
        readonly ConcurrentQueue<ClipSpec> _work = new ConcurrentQueue<ClipSpec>();
        readonly ConcurrentQueue<(ClipSpec spec, RenderedSound sound)> _done = new ConcurrentQueue<(ClipSpec, RenderedSound)>();
        readonly List<Thread> _threads = new List<Thread>();
        readonly ManualResetEventSlim _more = new ManualResetEventSlim(false);
        int _pending;
        int _renderedTicksTotal;
        long _renderTicks;
        Exception _failure;
        volatile bool _stop;

        public AudioClipBank(int sampleRate, AudioNoiseBank noise, int workers = 2)
        {
            if (noise.SampleRate != sampleRate) throw new ArgumentException("the noise beds are for another sample rate", nameof(noise));
            _rate = sampleRate;
            _noise = noise;
            for (int i = 0; i < Math.Max(1, workers); i++)
            {
                var t = new Thread(Work) { IsBackground = true, Name = "Cypher audio render " + i, Priority = System.Threading.ThreadPriority.BelowNormal };
                _threads.Add(t);
                t.Start();
            }
        }

        /// <summary>Specs requested and not yet turned into clips.</summary>
        public int Pending => Volatile.Read(ref _pending);

        public int Count => _clips.Count;

        /// <summary>Seconds of worker time spent synthesising so far.</summary>
        public double RenderSeconds => Interlocked.Read(ref _renderTicks) / (double)System.Diagnostics.Stopwatch.Frequency;

        public Exception Failure => Volatile.Read(ref _failure);

        /// <summary>Samples held, summed over every clip, for the memory figure.</summary>
        public long Samples { get; private set; }

        public void Enqueue(IEnumerable<ClipSpec> specs)
        {
            foreach (ClipSpec spec in specs)
            {
                Interlocked.Increment(ref _pending);
                _work.Enqueue(spec);
            }
            _more.Set();
        }

        void Work()
        {
            while (!_stop)
            {
                if (!_work.TryDequeue(out ClipSpec spec))
                {
                    _more.Reset();
                    if (_work.IsEmpty) _more.Wait(250);
                    continue;
                }
                try
                {
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    RenderedSound sound = SoundBank.Render(spec.Sound, _noise, spec.Seed, spec.Params.Clone());
                    Interlocked.Add(ref _renderTicks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
                    _done.Enqueue((spec, sound));
                }
                catch (Exception e)
                {
                    Volatile.Write(ref _failure, new InvalidOperationException($"rendering '{spec.Key}' failed: {e.Message}", e));
                    Interlocked.Decrement(ref _pending);
                }
            }
        }

        /// <summary>Make clips from what the workers have finished, at most <paramref name="max"/> this call (main thread). Returns how many.</summary>
        public int Pump(int max)
        {
            int made = 0;
            while (made < max && _done.TryDequeue(out (ClipSpec spec, RenderedSound sound) item))
            {
                AudioClip clip = AudioClip.Create(item.spec.Key, item.sound.Samples.Length, 1, _rate, false);
                clip.SetData(item.sound.Samples, 0);
                _clips[item.spec.Key] = clip;
                _sounds[item.spec.Key] = item.sound;
                Samples += item.sound.Samples.Length;
                Interlocked.Decrement(ref _pending);
                made++;
            }
            return made;
        }

        public bool Has(string key) => _clips.ContainsKey(key);

        public AudioClip Get(string key) => _clips.TryGetValue(key, out AudioClip clip) ? clip : null;

        /// <summary>The recipe's own account of the sound (its definition and length), for the sound that made a clip.</summary>
        public RenderedSound SoundOf(string key) => _sounds.TryGetValue(key, out RenderedSound s) ? s : null;

        public void Dispose()
        {
            _stop = true;
            _more.Set();
            foreach (Thread t in _threads) t.Join(500);
            foreach (AudioClip clip in _clips.Values) if (clip) UnityEngine.Object.Destroy(clip);
            _clips.Clear();
            _sounds.Clear();
        }
    }
}
