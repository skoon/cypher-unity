using System;
using Cypher.City;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using Cypher.Rendering;
using UnityEngine;

namespace Cypher.Actors
{
    /// <summary>
    /// The living crowd: starts it round the player when a game begins, steps the simulation at 60 Hz with interpolation between steps, draws it every frame, keeps eight
    /// solid capsules at the people nearest the player, follows the quality tier's population and detail distances, and stops it when the game returns to the title (the title
    /// flyover shows empty streets). Pausing freezes it for free: the time scale is zero, so no time passes, and it is still drawn.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(40)]   // after the player and the title camera have moved the camera
    public sealed class CrowdController : MonoBehaviour, IBenchmarkCounters
    {
        /// <summary>The simulation's fixed step (the prototype's).</summary>
        public const double StepSeconds = 1.0 / 60;

        /// <summary>After a long frame the simulation catches up by at most this many steps and drops the rest.</summary>
        public const int MaxStepsPerFrame = 4;

        [SerializeField] GameFlow flow;
        [SerializeField] CityBuilder city;
        [SerializeField] FirstPersonController player;
        [SerializeField] Camera viewCamera;
        [SerializeField] QualityDirector quality;
        [SerializeField] Material bodyMaterial;
        [SerializeField] Material shadowMaterial;

        /// <summary>How often a line about the crowd goes to the log while it runs.</summary>
        public const float StatusLogSeconds = 15f;

        CrowdSim _sim;
        float _nextStatusLog;
        CrowdRenderer _renderer;
        CrowdProxies _proxies;
        double _accumulator;
        double _near = 42, _far = 92;
        bool _subscribed;

        /// <summary>The running simulation, or null while there is no crowd (before a game, and on the title screen).</summary>
        public CrowdSim Sim => _sim;

        int IBenchmarkCounters.CounterCount => 3;

        string IBenchmarkCounters.CounterName(int index) => index == 0 ? "people_active" : index == 1 ? "people_drawn" : "people_full_rig";

        double IBenchmarkCounters.Counter(int index)
        {
            if (_sim == null || _renderer == null) return 0;
            return index == 0 ? _sim.Active.Count : index == 1 ? _renderer.Frame.Total : _renderer.Frame.Count(RigLod.Full);
        }

        public CrowdRenderer Renderer => _renderer;

        public CrowdProxies Proxies => _proxies;

        public bool IsActive => _sim != null;

        /// <summary>What the people ask before they step into a road. The traffic sets this while it runs; null means every road is clear.</summary>
        public ITrafficQuery TrafficQuery { get; set; }

        /// <summary>How far between the last two simulation steps the current frame falls.</summary>
        public double Alpha => _accumulator / StepSeconds;

        /// <param name="gameFlow">Optional: without it, call <see cref="Begin"/> and <see cref="End"/> yourself.</param>
        /// <param name="qualityDirector">Optional: without it the crowd is the Medium tier's.</param>
        public void Configure(GameFlow gameFlow, CityBuilder cityBuilder, FirstPersonController firstPerson, Camera camera, QualityDirector qualityDirector, Material body, Material shadow)
        {
            flow = gameFlow;
            city = cityBuilder;
            player = firstPerson;
            viewCamera = camera;
            quality = qualityDirector;
            bodyMaterial = body;
            shadowMaterial = shadow;
        }

        void Start()
        {
            if (city == null || player == null || viewCamera == null || bodyMaterial == null || shadowMaterial == null)
            {
                throw new InvalidOperationException("CrowdController needs the city, player, camera and the pedestrian materials; call Configure.");
            }
            if (flow != null) flow.StateChanged += OnStateChanged;
            if (quality != null) quality.TierApplied += OnTier;
            _subscribed = true;
        }

        void OnDestroy()
        {
            if (_subscribed)
            {
                if (flow != null) flow.StateChanged -= OnStateChanged;
                if (quality != null) quality.TierApplied -= OnTier;
            }
            _proxies?.Dispose();
            _renderer?.Dispose();
            _sim = null;
        }

        void OnStateChanged(FlowTransition t)
        {
            if (t.To == FlowState.Playing && t.NewSession) Begin();
            else if (t.To == FlowState.Title) End();
        }

        void OnTier(QualityTier tier) => ApplyTier(tier);

        /// <summary>Start a crowd round the player's feet. The city must be built.</summary>
        public void Begin()
        {
            if (city.Slice == null) throw new InvalidOperationException("the crowd needs the city to be built first");
            if (LaunchOptions.Current.NoCrowd)
            {
                Debug.Log("[Cypher] crowd: switched off by --no-crowd");
                return;
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            QualityTier tier = quality != null ? quality.Tier : QualityTiers.For(QualityPreset.Medium);
            Vector3 at = player.transform.position;
            CityNav nav = CityNav.Build(city.Slice.Plan);
            long navMs = clock.ElapsedMilliseconds;
            _sim = CrowdSim.Create(nav, QualityTiers.MaxPedestrians, tier.Pedestrians, at.x, at.z, rules: CrowdRules.City);
            _sim.IsVisible = IsInView;
            _renderer = _renderer ?? new CrowdRenderer(bodyMaterial, shadowMaterial);
            _proxies = _proxies ?? new CrowdProxies(transform);
            _accumulator = 0;
            _nextStatusLog = Time.realtimeSinceStartup + StatusLogSeconds;
            ApplyTier(tier);
            Debug.Log($"[Cypher] crowd: {tier.Pedestrians} people of a pool of {_sim.Pool.Count} ({tier.Preset} tier), full rig within {tier.ActorNear:0} m, box beyond {tier.ActorFar:0} m; started in {clock.ElapsedMilliseconds} ms ({navMs} of them the navigation graph)");
        }

        /// <summary>Remove the crowd (the title screen has none).</summary>
        public void End()
        {
            _sim = null;
            _proxies?.ParkAll();
        }

        bool IsInView(Pedestrian p) => _renderer != null && _renderer.View.IntersectsSphere(p.X, p.Y + CrowdFrame.CullRadius * 0.5, p.Z, CrowdFrame.CullRadius);

        void Update()
        {
            if (_sim == null) return;
            _renderer.UpdateFrustum(viewCamera);
            _accumulator += Time.deltaTime;
            Vector3 eye = viewCamera.transform.position, feet = player.transform.position;
            int steps = 0;
            while (_accumulator >= StepSeconds && steps < MaxStepsPerFrame)
            {
                _sim.Step(StepSeconds, eye.x, eye.z, feet.x, feet.z, TrafficQuery);
                _accumulator -= StepSeconds;
                steps++;
            }
            if (_accumulator > StepSeconds) _accumulator = 0;
        }

        void FixedUpdate()
        {
            if (_sim == null) return;
            Vector3 feet = player.transform.position;
            _proxies.Update(_sim, feet.x, feet.z);
        }

        void LateUpdate() => DrawNow();

        /// <summary>
        /// Queue this frame's draws. Cameras draw them when they render after this call in the same frame, which is every camera in the normal frame loop (this runs in
        /// LateUpdate); something that renders a camera by hand earlier in the frame, such as the capture tool, calls this just before.
        /// </summary>
        public void DrawNow()
        {
            if (_sim == null) return;
            _renderer.Render(_sim, viewCamera, Math.Min(1.0, Alpha), Time.timeAsDouble, Time.deltaTime, _near, _far);
            if (Time.realtimeSinceStartup >= _nextStatusLog)
            {
                _nextStatusLog = Time.realtimeSinceStartup + StatusLogSeconds;
                CrowdFrame f = _renderer.Frame;
                Debug.Log($"[Cypher] crowd: {_sim.Active.Count} active, {f.Total} drawn ({f.Count(RigLod.Full)} full, {f.Count(RigLod.Simple)} simple, {f.Count(RigLod.Silhouette)} boxes), {_renderer.DrawCalls} draws, {_proxies.Leased} solid");
            }
        }

        /// <summary>Follow a quality tier: the population and the distances at which the cheaper bodies take over.</summary>
        public void ApplyTier(QualityTier tier)
        {
            if (tier == null) throw new ArgumentNullException(nameof(tier));
            _near = tier.ActorNear;
            _far = tier.ActorFar;
            if (_sim != null) _sim.TargetCount = tier.Pedestrians;
        }
    }
}
