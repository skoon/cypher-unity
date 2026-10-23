using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Audio
{
    /// <summary>
    /// The music playing: Core's <see cref="Score"/> says which notes to play and how the pad, the delay and the sidechain move; the pad, the sidechain and the delay are a live graph on one
    /// source, and each note is synthesised a second before its beat on a worker thread and queued to start on its exact sample. A note made once (a kick, a particular bass note) is kept and used
    /// again, so after the first bars almost nothing is made while playing.
    /// </summary>
    public sealed class ScorePlayer : IDisposable
    {
        const double LookaheadSeconds = 1.0;
        const double AdvanceEvery = 0.1;
        const double LateTolerance = 0.05;

        sealed class Waiting
        {
            public string Key;
            public ScoreNote Note;
            public SoundDef Def;
        }

        readonly ScoreGraph _graph;
        readonly Score _score;
        readonly SynthSource _source;
        readonly AudioNoiseBank _noise;
        readonly Dictionary<string, float[]> _cache = new Dictionary<string, float[]>();
        readonly HashSet<string> _inFlight = new HashSet<string>();
        readonly ConcurrentQueue<(string key, float[] samples, Exception failure)> _done = new ConcurrentQueue<(string, float[], Exception)>();
        readonly List<Waiting> _waiting = new List<Waiting>();
        readonly List<ScoreNote> _notes = new List<ScoreNote>();
        readonly List<ScoreChange> _changes = new List<ScoreChange>();
        double _accumulated;
        int _takes;
        int _scheduled, _dropped, _rendered;
        Exception _renderFailure;

        public Score Score => _score;
        public ScoreGraph Graph => _graph;
        public SynthSource Source => _source;
        public double MillisecondsPerBlock => _source.MillisecondsPerCallback;
        public double WorstMilliseconds => _source.WorstMilliseconds;
        public int Scheduled => _scheduled;
        public int Dropped => _dropped;
        public int Rendered => _rendered;
        public int Cached => _cache.Count;

        public ScorePlayer(Transform parent, MixerLevels mixer, AudioNoiseBank noise, uint seed)
        {
            _noise = noise;
            _graph = new ScoreGraph(noise.SampleRate);
            _graph.Graph.CoefficientInterval = 32;
            _score = new Score(seed);
            var opening = new List<ScoreChange>();
            _score.Start(0, opening);
            foreach (ScoreChange c in opening) _graph.Enqueue(c);

            // the music's own send, from the prototype's recipes (0.1 to 0.12): the first tier above dry
            var group = mixer.Group(AudioBus.Music, AudioBuses.TierFor(0.11));
            _source = SynthSource.Create("Score", parent, _graph.Graph, 2, group, spatial: false);
            _source.BeforeBlock = _ => _graph.ApplyPending();

            // the drums are the same every time: have them ready before the first bar
            foreach (ScoreNote drum in ScoreNotes.Opening()) Request(ScoreNotes.KeyOf(drum), drum);
            _source.Begin();
        }

        void Request(string key, ScoreNote note)
        {
            if (_cache.ContainsKey(key) || !_inFlight.Add(key)) return;
            uint seed = ScoreNotes.SeedOf(++_takes);
            VoiceParams p = ScoreNotes.ParamsOf(note);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    RenderedSound r = SoundBank.Render(note.Sound, _noise, seed, p);
                    _done.Enqueue((key, r.Samples, null));
                }
                catch (Exception e)
                {
                    _done.Enqueue((key, null, e));
                }
            });
        }

        /// <summary>Per frame: move the intensity, ask the score for the next stretch, collect what has been made and queue it.</summary>
        public void Update(double dt)
        {
            _score.Tick(dt);
            Collect();
            _accumulated += dt;
            if (_accumulated >= AdvanceEvery)
            {
                _accumulated = 0;
                Advance();
            }
        }

        void Advance()
        {
            double now = _graph.Graph.Time;
            _notes.Clear();
            _changes.Clear();
            _score.Advance(now, LookaheadSeconds, _notes, _changes);
            foreach (ScoreChange c in _changes) _graph.Enqueue(c);
            foreach (ScoreNote n in _notes)
            {
                string key = ScoreNotes.KeyOf(n);
                SoundDef def = SoundBank.Get(n.Sound);
                if (_cache.TryGetValue(key, out float[] samples)) Queue(n, def, samples);
                else
                {
                    Request(key, n);
                    _waiting.Add(new Waiting { Key = key, Note = n, Def = def });
                }
            }
        }

        void Collect()
        {
            while (_done.TryDequeue(out (string key, float[] samples, Exception failure) r))
            {
                _inFlight.Remove(r.key);
                if (r.failure != null)
                {
                    _renderFailure = r.failure;
                    continue;
                }
                _cache[r.key] = r.samples;
                _rendered++;
            }
            if (_waiting.Count == 0) return;
            double now = _graph.Graph.Time;
            for (int i = _waiting.Count - 1; i >= 0; i--)
            {
                Waiting w = _waiting[i];
                if (_cache.TryGetValue(w.Key, out float[] samples))
                {
                    if (w.Note.Time < now - LateTolerance) _dropped++;
                    else Queue(w.Note, w.Def, samples);
                    _waiting.RemoveAt(i);
                }
                else if (w.Note.Time < now - 2) // never made in time: let it go
                {
                    _dropped++;
                    _waiting.RemoveAt(i);
                }
            }
        }

        void Queue(ScoreNote note, SoundDef def, float[] samples)
        {
            _graph.PlayerFor(note.Feed).Schedule(_graph.Graph.FrameOf(note.Time), samples, (float)(def.Gain * note.Volume));
            _scheduled++;
        }

        /// <summary>The first exception from the audio thread or a worker, or null.</summary>
        public Exception Failure => _source.Failure ?? _renderFailure;

        public void Dispose()
        {
            if (_source) UnityEngine.Object.Destroy(_source.gameObject);
        }
    }
}
