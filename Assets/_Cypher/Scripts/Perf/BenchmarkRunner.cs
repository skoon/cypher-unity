using System;
using System.Collections.Generic;
using System.IO;
using Cypher.City;
using Cypher.Core;
using Cypher.Player;
using Cypher.Rendering;
using Unity.Profiling;
using UnityEngine;

namespace Cypher.Perf
{
    /// <summary>
    /// The player's benchmark mode. Started with <c>--benchmark</c> on the command line (see
    /// <see cref="BenchmarkOptions"/>), it hands the character to a <see cref="BenchmarkPilot"/>, turns vsync
    /// off, waits out the warm-up, records every frame of a fixed walk and then writes
    /// <c>benchmark.csv</c>, <c>benchmark_summary.txt</c> and two screenshots (the first frame, and the last one
/// after recording stops, so capturing never lands inside the measurement) before quitting with an exit
    /// code: 0 passed the 30 FPS gate, 1 failed it, 2 an exception was thrown, 3 the run was invalid
    /// (the character barely moved). Without the flag it does nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        public const string CsvFile = "benchmark.csv";
        public const string HitchesFile = "hitches.csv";
        public const string WarmupHitchesFile = "warmup_hitches.csv";
        public const string SummaryFile = "benchmark_summary.txt";

        /// <summary>A frame in the warm-up longer than this is listed: first sight of a shader, the start of a game.</summary>
        public const double WarmupHitchMs = 50;
        public const string FirstFrameShot = "first_frame.png";
        public const string LastFrameShot = "last_frame.png";

        public const int ExitPass = 0;
        public const int ExitFail = 1;
        public const int ExitException = 2;
        public const int ExitInvalid = 3;

        /// <summary>After the warm-up the reflection probe must have finished, or something is wrong.</summary>
        public const double ProbeTimeoutSeconds = 30;

        public enum Phase { Idle, Warmup, Recording, Done }

        BenchmarkOptions _options;
        Action<int> _quit = Application.Quit;
        bool _screenshots = true;

        FirstPersonController _player;
        StreetReflectionProbe _probe;
        QualityDirector _quality;
        RenderCounters _counters;
        BenchmarkPilot _pilot;
        BenchmarkRecorder _recorder;
        string _outputDirectory;
        FrameTiming[] _timings;

        ProfilerRecorder _allocated;
        int _collections;
        IBenchmarkCounters[] _counterSources = Array.Empty<IBenchmarkCounters>();
        double _stalledAtStart;
        int _detoursAtStart;
        readonly List<string> _warmupHitches = new List<string>();

        /// <summary>Frames to let pass after the last screenshot so it is written before the process exits.</summary>
        const int ExitDelayFrames = 3;

        double _phaseSeconds;
        int _frame;
        double _firstFrameAt;
        int? _pendingExit;
        int _exitDelay;
        int _savedVSync;
        int _savedTargetFrameRate;
        bool _savedRunInBackground;
        bool _settingsChanged;

        public Phase State { get; private set; } = Phase.Idle;
        public BenchmarkSummary Summary { get; private set; }
        public string OutputDirectory => _outputDirectory;
        public BenchmarkRecorder Recorder => _recorder;

        /// <summary>For tests and scripted runs: skip the command line. Call while the object is inactive.</summary>
        public void Configure(BenchmarkOptions options, Action<int> quit, bool screenshots)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _quit = quit ?? throw new ArgumentNullException(nameof(quit));
            _screenshots = screenshots;
        }

        void Awake()
        {
            if (_options == null) _options = BenchmarkOptions.Parse(Environment.GetCommandLineArgs());
            if (!_options.Enabled)
            {
                enabled = false;
                return;
            }
            // Subscribed from the start so an exception anywhere, including Start, ends the run instead of hanging it.
            Application.logMessageReceived += OnLog;
        }

        void Start()
        {
            _player = FindFirstObjectByType<FirstPersonController>();
            if (_player == null) throw new InvalidOperationException("Benchmark needs a FirstPersonController in the scene.");
            _probe = FindFirstObjectByType<StreetReflectionProbe>();

            _outputDirectory = Path.GetFullPath(_options.OutputDirectory ?? Path.Combine(Application.dataPath, "..", "benchmark"));
            Directory.CreateDirectory(_outputDirectory);

            _savedVSync = QualitySettings.vSyncCount;
            _savedTargetFrameRate = Application.targetFrameRate;
            _savedRunInBackground = Application.runInBackground;
            _settingsChanged = true;
            QualitySettings.vSyncCount = 0;          // measure what the machine can do, not the display's refresh
            Application.targetFrameRate = -1;
            Application.runInBackground = true;      // a window that lost focus must not pause the run

            PinQuality();

            _counters = new RenderCounters();
            _timings = new FrameTiming[1];
            // "GC Allocated In Frame" exists in a Development player only; elsewhere the allocation reads as not available
            _allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _collections = GC.CollectionCount(0);
            _recorder = new BenchmarkRecorder();
            TryMakePilot();

            State = Phase.Warmup;
            Debug.Log($"[Cypher] benchmark: {_options.Route} route, {_options.WarmupSeconds:0.#} s warm-up, then {_options.DurationSeconds:0.#} s recorded, results in {_outputDirectory}");
        }

        /// <summary>
        /// Hands the character to a pilot. The routes on the pavement are made from the city's pedestrian graph, which exists once the city is built: until then this says no, and is asked
        /// again every frame.
        /// </summary>
        bool TryMakePilot()
        {
            if (_pilot != null) return true;
            CityNav nav = null;
            if (_options.Route == BenchmarkRoute.Street || _options.Route == BenchmarkRoute.Junction)
            {
                CityBuilder city = FindFirstObjectByType<CityBuilder>();
                if (city == null) throw new InvalidOperationException($"The {_options.Route} route needs a CityBuilder in the scene.");
                if (!city.IsBuilt) return false;
                nav = CityNav.Build(city.Slice.Plan);
            }
            _pilot = new BenchmarkPilot(BenchmarkPilot.Route(_options.Route, nav), _player.Sensitivity);
            _player.CommandSource = new PilotCommandSource(_player, _pilot);
            return true;
        }

        /// <summary>A benchmark measures one preset: adaptive quality off, and the requested preset if one was given.</summary>
        void PinQuality()
        {
            _quality = FindFirstObjectByType<QualityDirector>();
            if (_quality == null)
            {
                if (_options.Quality.HasValue) throw new InvalidOperationException($"{BenchmarkOptions.QualityFlag} needs a QualityDirector in the scene.");
                return;
            }
            GraphicsOptions g = _quality.Options;
            g.AdaptiveQuality = false;
            if (_options.Quality.HasValue) g.Quality = _options.Quality.Value;
            _quality.Apply(g);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            _counters?.Dispose();
            _allocated.Dispose();
            if (_settingsChanged)
            {
                QualitySettings.vSyncCount = _savedVSync;
                Application.targetFrameRate = _savedTargetFrameRate;
                Application.runInBackground = _savedRunInBackground;
            }
        }

        void Update()
        {
            if (_pendingExit.HasValue && ++_exitDelay >= ExitDelayFrames)
            {
                int code = _pendingExit.Value;
                _pendingExit = null;
                _quit(code);
            }
            if (State == Phase.Idle || State == Phase.Done) return;
            double dt = Time.unscaledDeltaTime;
            if (dt <= 0) return;
            if (State == Phase.Warmup && !_player.isActiveAndEnabled) return;   // still loading: the flow has not started the game
            if (!TryMakePilot()) return;

            if (_frame++ == 0)
            {
                _firstFrameAt = Time.realtimeSinceStartupAsDouble;
                if (_screenshots) Screenshot(FirstFrameShot);
            }

            _phaseSeconds += dt;
            if (State == Phase.Warmup) UpdateWarmup(dt);
            else UpdateRecording(dt);
        }

        void UpdateWarmup(double dt)
        {
            int collections = GC.CollectionCount(0);
            if (dt * 1000.0 > WarmupHitchMs)
            {
                Vector3 at = _player.transform.position;
                (double cpu, double gpu) = ReadFrameTimings();
                _warmupHitches.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3},{3:F3},{4:F2},{5:F2},{6}",
                    _phaseSeconds, dt * 1000.0, cpu, gpu, at.x, at.z, collections != _collections ? 1 : 0));
            }
            _collections = collections;
            if (_phaseSeconds < _options.WarmupSeconds) return;
            if (_probe != null && !_probe.IsCaptured)
            {
                if (_phaseSeconds > _options.WarmupSeconds + ProbeTimeoutSeconds)
                {
                    throw new InvalidOperationException("The reflection probe never finished rendering, so the scene is not in its steady state.");
                }
                return;
            }
            State = Phase.Recording;
            _phaseSeconds = 0;
            _stalledAtStart = _pilot.StalledSeconds;
            _detoursAtStart = _pilot.Detours;
            _collections = GC.CollectionCount(0);
            _counterSources = FindCounterSources();
        }

        /// <summary>Whatever in the scene reports counters: the crowd, the traffic, the sound.</summary>
        static IBenchmarkCounters[] FindCounterSources()
        {
            var found = new List<IBenchmarkCounters>();
            foreach (MonoBehaviour b in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (b is IBenchmarkCounters c) found.Add(c);
            }
            return found.ToArray();
        }

        void UpdateRecording(double dt)
        {
            (double cpu, double gpu) = ReadFrameTimings();
            Vector3 p = _player.transform.position;
            int collections = GC.CollectionCount(0);
            long allocated = _allocated.Valid ? _allocated.LastValue : -1;
            _recorder.Add(new FrameSample(_phaseSeconds, dt * 1000.0, cpu, gpu, _counters.DrawCalls, _counters.Triangles, p.x, p.z, allocated, collections != _collections));
            _collections = collections;
            foreach (IBenchmarkCounters source in _counterSources)
            {
                for (int i = 0; i < source.CounterCount; i++) _recorder.AddCounter(source.CounterName(i), source.Counter(i));
            }

            if (_phaseSeconds >= _options.DurationSeconds) Finish();
        }

        /// <summary>CPU and GPU time of a recent frame in ms; zero where the platform does not report them.</summary>
        (double cpu, double gpu) ReadFrameTimings()
        {
            FrameTimingManager.CaptureFrameTimings();
            return FrameTimingManager.GetLatestTimings(1, _timings) > 0 ? (_timings[0].cpuFrameTime, _timings[0].gpuFrameTime) : (0, 0);
        }

        void Finish()
        {
            State = Phase.Done;
            Summary = _recorder.Summarise(_pilot.StalledSeconds - _stalledAtStart, _pilot.Detours - _detoursAtStart);
            string text = Summary.ToText(DescribeEnvironment());
            File.WriteAllText(Path.Combine(_outputDirectory, SummaryFile), text);
            File.WriteAllText(Path.Combine(_outputDirectory, CsvFile), _recorder.ToCsv());
            File.WriteAllText(Path.Combine(_outputDirectory, HitchesFile), _recorder.HitchesCsv());
            File.WriteAllText(Path.Combine(_outputDirectory, WarmupHitchesFile), "time_s,frame_ms,cpu_ms,gpu_ms,x,z,gc\n" + string.Join("\n", _warmupHitches) + (_warmupHitches.Count > 0 ? "\n" : ""));
            Debug.Log("[Cypher] benchmark finished:\n" + text);

            int code = Summary.Verdict == BenchmarkVerdict.Pass ? ExitPass
                : Summary.Verdict == BenchmarkVerdict.Fail ? ExitFail
                : ExitInvalid;
            if (_screenshots)
            {
                Screenshot(LastFrameShot);
                _pendingExit = code;   // quit a few frames on, once the screenshot has been written
            }
            else
            {
                _quit(code);
            }
        }

        void Screenshot(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(_outputDirectory, name));
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception || State == Phase.Done) return;
            State = Phase.Done;
            _quit(ExitException);
        }

        IEnumerable<KeyValuePair<string, string>> DescribeEnvironment()
        {
            var lowRes = FindFirstObjectByType<LowResController>();
            var city = FindFirstObjectByType<CityBuilder>();
            string scripting =
#if ENABLE_IL2CPP
                "IL2CPP";
#else
                "Mono";
#endif
            return new[]
            {
                Pair("unity", Application.unityVersion),
                Pair("build", (Debug.isDebugBuild ? "Development" : "Release") + (Application.isEditor ? " (Editor)" : "") + ", " + scripting),
                Pair("gpu", $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})"),
                Pair("cpu", $"{SystemInfo.processorType}, {SystemInfo.systemMemorySize} MB"),
                Pair("window", $"{Screen.width}x{Screen.height}, {Screen.fullScreenMode}"),
                Pair("internal_height", lowRes != null ? Mathf.RoundToInt(Screen.height * lowRes.CurrentScale).ToString() : "native"),
                Pair("quality", _quality != null ? $"{_quality.Preset}, adaptive {(_quality.Adaptive ? "on" : "off")}" : "no QualityDirector"),
                Pair("msaa", _quality != null ? _quality.MsaaSamples + "x" : "n/a"),
                Pair("unity_quality_level", QualitySettings.names[QualitySettings.GetQualityLevel()]),
                Pair("vsync", QualitySettings.vSyncCount.ToString()),
                Pair("city_build_ms", city != null ? city.BuildMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : "n/a"),
                Pair("launch_to_play_s", _firstFrameAt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)),
                Pair("route", _options.Route.ToString().ToLowerInvariant()),
                Pair("warmup_s", _options.WarmupSeconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)),
                Pair("route_laps", _pilot.Laps.ToString()),
                Pair("route_waypoints_reached", _pilot.WaypointsReached.ToString()),
            };
        }

        static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);
    }
}
