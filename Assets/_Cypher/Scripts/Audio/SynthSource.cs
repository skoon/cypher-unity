using System;
using System.Threading;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace Cypher.Audio
{
    /// <summary>
    /// An audio source whose sound is a live synthesis graph: the bed, the score, a neon tube, a car's engine. A silent looping clip keeps the source running, and on each audio callback the graph
    /// is asked for as many frames as the device wants. That runs on the audio thread, so everything the graph is told from outside arrives through <see cref="BeforeBlock"/> as plain
    /// numbers read there. If the graph throws, the source goes silent and the exception is kept for the main thread to report: an audio callback has no one to hand it to.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class SynthSource : MonoBehaviour
    {
        AudioSource _source;
        AudioClip _silence;
        volatile SynthGraph _graph;
        Exception _failure;
        int _callbacks;
        long _ticks;
        long _worstTicks;

        /// <summary>Called on the audio thread before each block, to hand the graph what the main thread has decided.</summary>
        public Action<SynthGraph> BeforeBlock { get; set; }

        /// <summary>The graph being played, or null.</summary>
        public SynthGraph Graph => _graph;

        public AudioSource Source => _source ? _source : (_source = GetComponent<AudioSource>());

        /// <summary>Callbacks served so far.</summary>
        public int Callbacks => Volatile.Read(ref _callbacks);

        /// <summary>Stopwatch ticks the graph has taken in total on the audio thread.</summary>
        public long GraphTicks => Interlocked.Read(ref _ticks);

        /// <summary>The longest any one callback has taken, in milliseconds.</summary>
        public double WorstMilliseconds => Interlocked.Read(ref _worstTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>Milliseconds the graph has taken per callback, on average.</summary>
        public double MillisecondsPerCallback
        {
            get
            {
                int n = Callbacks;
                return n == 0 ? 0 : GraphTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / n;
            }
        }

        /// <summary>What went wrong on the audio thread, or null.</summary>
        public Exception Failure => Volatile.Read(ref _failure);

        /// <summary>A new source on a new object under <paramref name="parent"/>, playing <paramref name="graph"/> through a mixer group.</summary>
        public static SynthSource Create(string name, Transform parent, SynthGraph graph, int channels, AudioMixerGroup group, bool spatial, float maxDistance = 90f, int priority = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var host = go.AddComponent<SynthSource>();
            AudioSource s = host.Source;
            s.playOnAwake = false;
            s.loop = true;
            s.priority = priority;
            s.outputAudioMixerGroup = group;
            s.spatialBlend = spatial ? 1f : 0f;
            s.dopplerLevel = 0f;
            s.spread = 0f;
            s.minDistance = 1f;
            s.maxDistance = maxDistance;
            s.rolloffMode = AudioRolloffMode.Linear;
            // a short silent clip with the channels the graph will fill: the source needs something to play for the callbacks to come
            var clip = AudioClip.Create(name + " (silence)", 2048, channels, graph.SampleRate, false);
            s.clip = clip;
            host._silence = clip;
            host._graph = graph;
            return host;
        }

        public void Begin()
        {
            Source.Play();
        }

        public void Halt()
        {
            if (_source) _source.Stop();
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            SynthGraph graph = _graph;
            if (graph == null || Volatile.Read(ref _failure) != null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            try
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                BeforeBlock?.Invoke(graph);
                graph.Fill(data, channels, data.Length / channels);
                long took = System.Diagnostics.Stopwatch.GetTimestamp() - start;
                Interlocked.Add(ref _ticks, took);
                // only this thread writes it
                if (took > Volatile.Read(ref _worstTicks)) Interlocked.Exchange(ref _worstTicks, took);
                Interlocked.Increment(ref _callbacks);
            }
            catch (Exception e)
            {
                Volatile.Write(ref _failure, e);
                Array.Clear(data, 0, data.Length);
            }
        }

        void OnDestroy()
        {
            _graph = null;
            // the clip is ours: left behind it would be one more for every game started
            if (_silence) Destroy(_silence);
        }
    }
}
