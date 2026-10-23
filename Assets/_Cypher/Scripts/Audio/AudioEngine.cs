using System;
using System.Collections.Generic;
using Cypher.Actors;
using Cypher.City;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using Cypher.Rendering;
using Cypher.UI;
using UnityEngine;
using UnityEngine.Audio;

namespace Cypher.Audio
{
    /// <summary>
    /// The game's sound. It makes the sounds that can be made ahead of time while the city loads, plays the menu sounds from the first screen, and when a game starts brings up the city: the
    /// bed of rain, wind, traffic and crowd, the sounds that stand somewhere, the score, the player's own steps and breath, the real cars and people; places them in the room the listener is
    /// in (the reverb follows the geometry); lets the Options sliders and the game's state (paused, back at the title) set the level of each bus; and glues and limits the finished mix.
    /// Nothing but the menu sounds plays on the title screen. <c>--no-audio</c> turns all of it off.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(60)]   // after the player and the actors have moved this frame
    public sealed class AudioEngine : MonoBehaviour
    {
        public const uint Seed = 20771;
        public const float StatusLogSeconds = 15f;
        public const int ClipsMadePerFrame = 6;
        const double WorldUpdateEvery = 0.1;
        const double FadeOutBeforeTeardown = 1.0;

        [SerializeField] UnityEngine.Audio.AudioMixer mixer;

        [SerializeField] GameFlow _flow;
        [SerializeField] SettingsService _settings;
        [SerializeField] FirstPersonController _player;
        [SerializeField, Tooltip("The Main Camera, which carries the AudioListener.")] Transform _listener;
        [SerializeField] CityBuilder _city;
        [SerializeField] CrowdController _crowd;
        [SerializeField] TrafficController _traffic;
        [SerializeField] RainController _rain;

        AudioNoiseBank _noise;
        AudioClipBank _clips;
        MixerLevels _mixerLevels;
        readonly BusLevels _levels = new BusLevels();
        OneShotVoices _voices;
        MasterFilter _master;
        AudioEnvironment _environment;
        ReverbBlend _reverb = new ReverbBlend(ReverbSpace.Street);
        readonly TakePicker _uiTakes = new TakePicker(3);
        readonly TakePicker _ambientTakes = new TakePicker(5);

        // the world: made when a game starts, gone again at the title
        BedPlayer _bed;
        ScorePlayer _score;
        ActorVoices _actors;
        PlayerFoley _foley;
        AmbientEvents _events;
        readonly List<AmbientShot> _shots = new List<AmbientShot>();
        double _worldDsp0;
        double _worldAccumulator;
        double _stopAt = -1;
        bool _worldWanted;

        // where the main thread's time goes, for the status line
        enum Part { Environment, Bed, Events, Score, Foley, Cars, Walkers, Levels, Voices }
        readonly long[] _partTicks = new long[9];

        float _nextStatus;
        long _mainTicks, _mainMaxTicks, _pumpTicks;
        int _mainFrames;
        bool _started;
        string _failureReported;

        /// <summary>False when audio is switched off (<c>--no-audio</c>) or could not start; every call is then a harmless no-op.</summary>
        public bool Active => _started;

        public AudioClipBank Clips => _clips;
        public BusLevels Levels => _levels;
        public MixerLevels Mixer => _mixerLevels;
        public OneShotVoices Voices => _voices;
        public MasterFilter Master => _master;
        public AudioEnvironment Environment => _environment;
        public ReverbBlend Reverb => _reverb;
        public BedPlayer Bed => _bed;
        public ScorePlayer Score => _score;
        public ActorVoices Actors => _actors;
        public PlayerFoley Foley => _foley;

        /// <summary>What the sound has cost so far: the audio thread's time in each piece and the main thread's in the engine's own update.</summary>
        public AudioCost Cost
        {
            get
            {
                AudioSettings.GetDSPBufferSize(out int length, out _);
                double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                return new AudioCost(_bed?.MillisecondsPerBlock ?? 0, _score?.MillisecondsPerBlock ?? 0, _actors?.MillisecondsPerBlock ?? 0, _master != null ? _master.MillisecondsPerCallback : 0,
                    length * 1000.0 / Math.Max(1, SampleRate), _mainFrames == 0 ? 0 : _mainTicks * toMs / _mainFrames, _mainMaxTicks * toMs,
                    Math.Max(Math.Max(_bed?.WorstMilliseconds ?? 0, _score?.WorstMilliseconds ?? 0), Math.Max(_actors?.WorstMilliseconds ?? 0, _master != null ? _master.WorstMilliseconds : 0)));
            }
        }

        /// <summary>Milliseconds the main thread has spent turning rendered sounds into clips, in total (it happens while the game loads and is done once).</summary>
        public double ClipCreationMs => _pumpTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>Adds the time since <paramref name="since"/> to a part and returns the time now, to start the next lap.</summary>
        long Lap(Part part, long since)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            _partTicks[(int)part] += now - since;
            return now;
        }

        string MainThreadBreakdown()
        {
            if (_mainFrames == 0) return "none";
            double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency / _mainFrames;
            var parts = new System.Text.StringBuilder();
            for (int i = 0; i < _partTicks.Length; i++) parts.Append(i == 0 ? "" : ", ").Append(((Part)i).ToString().ToLowerInvariant()).Append(' ').Append((_partTicks[i] * toMs).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            return parts.ToString();
        }

        /// <summary>Forget the main thread's timings so far (a measurement begins).</summary>
        public void ResetMainThreadCost()
        {
            Array.Clear(_partTicks, 0, _partTicks.Length);
            _mainTicks = 0;
            _mainMaxTicks = 0;
            _mainFrames = 0;
        }

        /// <summary>True from a new game until the title again.</summary>
        public bool WorldRunning => _bed != null;

        /// <summary>Seconds of audio since the world began (the clock the events and breaths are scheduled on).</summary>
        public double WorldTime => AudioSettings.dspTime - _worldDsp0;

        public int SampleRate { get; private set; }

        public void Configure(UnityEngine.Audio.AudioMixer audioMixer, GameFlow gameFlow, SettingsService settingsService, FirstPersonController firstPerson, Transform listenerTransform,
            CityBuilder cityBuilder, CrowdController crowdController, TrafficController trafficController, RainController rainController)
        {
            mixer = audioMixer;
            _flow = gameFlow;
            _settings = settingsService;
            _player = firstPerson;
            _listener = listenerTransform;
            _city = cityBuilder;
            _crowd = crowdController;
            _traffic = trafficController;
            _rain = rainController;
        }

        void Start()
        {
            if (LaunchOptions.Current.NoAudio)
            {
                Debug.Log("[Cypher] audio: switched off by --no-audio");
                return;
            }
            if (!mixer || _flow == null || _listener == null)
            {
                throw new InvalidOperationException("AudioEngine needs the mixer, the game flow and the listener; call Configure.");
            }

            SampleRate = AudioSettings.outputSampleRate;
            var begin = System.Diagnostics.Stopwatch.StartNew();
            _noise = new AudioNoiseBank(SampleRate);
            _mixerLevels = new MixerLevels(mixer);
            _clips = new AudioClipBank(SampleRate, _noise);
            // the menu sounds first, so the title has them at once; the rest while the city builds
            _clips.Enqueue(ClipPlan.Ui());
            _clips.Enqueue(ClipPlan.Foley());
            _clips.Enqueue(ClipPlan.Ambient());

            var voices = new GameObject("Voices");
            voices.transform.SetParent(transform, false);
            _voices = new OneShotVoices(voices.transform, _mixerLevels);
            _master = _listener.gameObject.GetComponent<MasterFilter>();
            if (!_master) _master = _listener.gameObject.AddComponent<MasterFilter>();
            _master.Begin(SampleRate);
            _environment = new AudioEnvironment();

            _flow.StateChanged += OnFlowChanged;
            if (_settings != null)
            {
                _settings.Applied += OnSettings;
                OnSettings(_settings.Current);
            }
            UiSound.Sink = PlayUi;
            ApplyFlowState(_flow.State);
            PushLevels();
            _started = true;
            Debug.Log($"[Cypher] audio: started at {SampleRate} Hz in {begin.Elapsed.TotalMilliseconds:0} ms; {_clips.Pending} clips queued for the workers");
        }

        void OnDestroy()
        {
            if (_flow != null) _flow.StateChanged -= OnFlowChanged;
            if (_settings != null) _settings.Applied -= OnSettings;
            if (UiSound.Sink == (Action<string>)PlayUi) UiSound.Sink = null;
            TearDownWorld();
            _clips?.Dispose();
        }

        // ---------------------------------------------------------------- the game's state and the Options

        void OnSettings(GameSettings s)
        {
            AudioOptions a = s.Audio;
            _levels.MasterSlider = a.Master;
            _levels.SetSlider(AudioBus.Music, a.Music);
            _levels.SetSlider(AudioBus.Sfx, a.Sfx);
            _levels.SetSlider(AudioBus.Ambience, a.Ambience);
            _levels.SetSlider(AudioBus.Ui, a.Ui);
            _levels.SetSlider(AudioBus.Voice, a.Voice);
        }

        void OnFlowChanged(FlowTransition t)
        {
            ApplyFlowState(t.To);
            if (t.To == FlowState.Playing && t.NewSession) StartWorld();
            else if (t.To == FlowState.Title && _bed != null) _stopAt = Time.unscaledTimeAsDouble + FadeOutBeforeTeardown;
        }

        void ApplyFlowState(FlowState state)
        {
            bool silentWorld = state == FlowState.Boot || state == FlowState.Loading || state == FlowState.Title;
            _levels.SetState(paused: state == FlowState.Paused, inMenu: false, silentWorld: silentWorld);
            _worldWanted = state == FlowState.Playing || state == FlowState.Paused;
        }

        // ---------------------------------------------------------------- the world

        void StartWorld()
        {
            if (_bed != null) TearDownWorld();
            _stopAt = -1;
            var world = new GameObject("World sound");
            world.transform.SetParent(transform, false);
            _worldDsp0 = AudioSettings.dspTime;
            _bed = new BedPlayer(world.transform, _mixerLevels, _noise, Seed + 31);
            _score = new ScorePlayer(world.transform, _mixerLevels, _noise, Seed + 97);
            _actors = new ActorVoices(world.transform, _mixerLevels, _noise);
            _foley = new PlayerFoley(this, 1.0);
            _events = new AmbientEvents(_bed.Bed);
            // real cars and people make the sounds of passing cars and voices; the random ones are the fallback
            _events.RandomCars = _traffic == null || !_traffic.IsActive;
            _events.RandomVoices = _crowd == null || !_crowd.IsActive;
            _environment.Probe.Override = null;
            _levels.Snap();
            _master.Stage.FadeIn();
            _worldAccumulator = 0;
            ResetMainThreadCost();
            Debug.Log("[Cypher] audio: the world begins");
        }

        void TearDownWorld()
        {
            _bed?.Dispose();
            _score?.Dispose();
            _actors?.Dispose();
            _bed = null;
            _score = null;
            _actors = null;
            _foley = null;
            _events = null;
            _voices?.StopAll();
            if (transform.Find("World sound")) Destroy(transform.Find("World sound").gameObject);
        }

        // ---------------------------------------------------------------- per frame

        void LateUpdate()
        {
            if (!_started) return;
            long began = System.Diagnostics.Stopwatch.GetTimestamp();
            float dt = Time.unscaledDeltaTime;
            _clips.Pump(ClipsMadePerFrame);
            long pumped = System.Diagnostics.Stopwatch.GetTimestamp();
            _pumpTicks += pumped - began;
            ReportFailures();

            if (_stopAt >= 0 && Time.unscaledTimeAsDouble >= _stopAt)
            {
                TearDownWorld();
                _stopAt = -1;
            }

            if (_bed != null && _worldWanted)
            {
                UpdateWorld(dt);
            }
            long lap = System.Diagnostics.Stopwatch.GetTimestamp();
            _levels.Advance(dt);
            PushLevels();
            lap = Lap(Part.Levels, lap);
            _voices.Update();
            Lap(Part.Voices, lap);

            long took = System.Diagnostics.Stopwatch.GetTimestamp() - pumped;
            _mainTicks += took;
            _mainFrames++;
            if (took > _mainMaxTicks) _mainMaxTicks = took;

            if (Time.unscaledTime >= _nextStatus)
            {
                _nextStatus = Time.unscaledTime + StatusLogSeconds;
                Debug.Log(Status());
            }
        }

        void UpdateWorld(float dt)
        {
            Vector3 ear = _listener.position;
            double now = WorldTime;
            long lap = System.Diagnostics.Stopwatch.GetTimestamp();

            ReverbSpace? changed = _environment.Update(ear, now);
            if (changed.HasValue) _reverb.MoveTo(changed.Value);
            _reverb.Advance(dt);
            _mixerLevels.SetReverb(_reverb.Current);
            lap = Lap(Part.Environment, lap);

            bool raining = _rain != null && _rain.Raining;
            double wetness = Footing.Wetness(raining ? NightRig.DefaultRain : 0);

            _bed.Place(ear, _city != null ? _city.NeonSources : null, _city != null ? _city.MachineSources : null, dt);
            lap = Lap(Part.Bed, lap);

            _worldAccumulator += dt;
            if (_worldAccumulator >= WorldUpdateEvery)
            {
                _worldAccumulator = 0;
                var state = new BedState(raining ? NightRig.DefaultRain : 0, RainModel.DefaultWind, _environment.Indoor, 1);
                _bed.SetState(state);
                _shots.Clear();
                Vector3 neon = _bed.NeonPosition;
                _events.Advance(now, 0.55, state, (ear.x, ear.y, ear.z), (neon.x, neon.y, neon.z), _shots);
                foreach (AmbientShot shot in _shots) PlayShot(shot, ear);
            }
            lap = Lap(Part.Events, lap);

            _score.Update(dt);
            lap = Lap(Part.Score, lap);
            if (_player != null && _player.enabled && _flow.State == FlowState.Playing) _foley.Update(_player, dt, now, wetness);
            lap = Lap(Part.Foley, lap);
            _actors.UpdateCars(_traffic != null && _traffic.IsActive ? _traffic.Sim : null, ear, wetness, dt);
            lap = Lap(Part.Cars, lap);
            _actors.UpdateWalkers(_crowd != null && _crowd.IsActive ? _crowd.Sim.Active : null, ear, wetness, PlayFootfall);
            Lap(Part.Walkers, lap);
        }

        void PushLevels()
        {
            foreach (AudioBus bus in AudioBuses.All) _mixerLevels.SetBus(bus, _levels.Decibels(bus));
            _mixerLevels.Set(AudioBuses.ReverbVolumeParameter, AudioBuses.ToDecibels(Math.Max(1e-4, _levels.ReverbDuck)));
            _master.Stage.Target = _levels.Master;
        }

        void ReportFailures()
        {
            Exception failure = _clips.Failure ?? _master.Failure ?? _bed?.Failure ?? _score?.Failure ?? _actors?.Failure;
            if (failure == null) return;
            string text = failure.ToString();
            if (text == _failureReported) return;
            _failureReported = text;
            Debug.LogError("[Cypher] audio failed: " + text);
        }

        // ---------------------------------------------------------------- playing

        void PlayShot(AmbientShot shot, Vector3 ear)
        {
            SoundDef def = SoundBank.Get(shot.Sound);
            int takes = ClipPlan.AmbientTakes(shot.Sound);
            string key = ClipPlan.AmbientKey(shot.Sound, _ambientTakes.Pick(shot.Sound, takes));
            AudioClip clip = _clips.Get(key);
            if (!clip) return;
            var position = new Vector3((float)shot.X, (float)shot.Y, (float)shot.Z);
            double occlusion = _environment.OcclusionTo(ear, position);
            _voices.Play(clip, def, position, (float)shot.Volume, (float)(shot.Pitch ?? 1), occlusion, _worldDsp0 + shot.Time);
        }

        void PlayFootfall(Footfall f, string surface, int wetBucket, int take)
        {
            ClipPlan.Stance stance = f.Hurried ? ClipPlan.Stance.Sprint : ClipPlan.Stance.Walk;
            PlayClip(ClipPlan.StepKey(surface, stance, wetBucket, take), "foot." + surface, new Vector3((float)f.X, 0.1f, (float)f.Z), (float)f.Level);
        }

        /// <summary>Play one of the clips that were made at load. Returns false if it is not ready, or no voice would take it.</summary>
        /// <param name="position">Where it is in the world; null plays it head-locked.</param>
        /// <param name="startWorldTime">When to begin, on the world clock (0: now).</param>
        public bool PlayClip(string key, string soundName, Vector3? position, float volume, float pitch = 1f, double startWorldTime = 0, AudioBus? busOverride = null)
        {
            if (!_started) return false;
            AudioClip clip = _clips.Get(key);
            if (!clip) return false;
            SoundDef def = SoundBank.Get(soundName);
            double occlusion = position.HasValue ? _environment.OcclusionTo(_listener.position, position.Value) : 0;
            return _voices.Play(clip, def, position, volume, pitch, occlusion, startWorldTime > 0 ? _worldDsp0 + startWorldTime : 0, busOverride);
        }

        /// <summary>Play a menu sound by name (<see cref="UiSound"/>), now, flat, on the interface bus.</summary>
        public void PlayUi(string sound)
        {
            if (!_started) return;
            int takes = ClipPlan.UiTakes;
            AudioClip clip = _clips.Get(ClipPlan.UiKey(sound, _uiTakes.Pick(sound, takes)));
            if (!clip) return;
            _voices.Play(clip, SoundBank.Get(sound), null, 1f, 1f, 0);
        }

        public void BumpMusic(double heat) => _score?.Score.Bump(heat);

        public string Status()
        {
            string world = _bed == null ? "title (silent but for the menu)" :
                $"world: space {_environment.Space}, {_bed.Main.Callbacks} bed blocks ({_bed.StateChanges} weather changes), bed {_bed.Main.GraphTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms total on the audio thread, " +
                $"score {_score.Scheduled} notes ({_score.Rendered} made, {_score.Dropped} dropped), cars {_actors.RunningCars}, steps {_foley.Steps}/{_actors.WalkerStepsPlayed}";
            return $"[Cypher] audio: {world}; voices {_voices.Active} (played {_voices.Played}, stolen {_voices.Stolen}, culled {_voices.Culled}); clips {_clips.Count}, {_clips.Pending} pending; " +
                   $"master {_master.Ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms in {_master.Callbacks} blocks; {Cost}; main thread by part, ms a frame: {MainThreadBreakdown()}; clips: {_clips.Samples * 4 / 1048576.0:0.0} MB, {ClipCreationMs:0} ms of main thread to create them, {_clips.RenderSeconds:0.0} s of worker time to render them";
        }
    }
}
