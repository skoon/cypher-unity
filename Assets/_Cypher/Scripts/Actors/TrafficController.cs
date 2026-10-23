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
    /// The living traffic: starts it round the player when a game begins, steps the simulation at 60 Hz with interpolation between steps, draws it every frame, keeps four solid boxes at
    /// the vehicles nearest the player and the point lights on those nearest the camera, follows the quality tier's numbers, and stops it when the game returns to the title (the title
    /// flyover shows empty roads and sky). It steps after the crowd, which it tells the traffic's state and which it is told: people wait for gaps in the traffic and the vehicles stop for
    /// anyone out on the road. Pausing freezes it for free: the time scale is zero, so no time passes, and it is still drawn.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(41)]   // after the crowd, which is after the player and the title camera have moved the camera
    public sealed class TrafficController : MonoBehaviour
    {
        /// <summary>The simulation's fixed step (the prototype's).</summary>
        public const double StepSeconds = 1.0 / 60;

        /// <summary>After a long frame the simulation catches up by at most this many steps and drops the rest.</summary>
        public const int MaxStepsPerFrame = 4;

        /// <summary>How often a line about the traffic goes to the log while it runs.</summary>
        public const float StatusLogSeconds = 15f;

        [SerializeField] GameFlow flow;
        [SerializeField] CityBuilder city;
        [SerializeField] FirstPersonController player;
        [SerializeField] Camera viewCamera;
        [SerializeField] QualityDirector quality;
        [SerializeField] CrowdController crowd;
        [SerializeField] TrafficMaterials materials;

        TrafficSim _sim;
        TrafficRenderer _renderer;
        TrafficProxies _proxies;
        TrafficLights _lights;
        double _accumulator;
        int _lightsAllowed = QualityTiers.MaxVehicleLights;
        float _nextStatusLog;
        bool _subscribed;

        /// <summary>The running simulation, or null while there is no traffic (before a game, and on the title screen).</summary>
        public TrafficSim Sim => _sim;

        public TrafficRenderer Renderer => _renderer;

        public TrafficProxies Proxies => _proxies;

        public TrafficLights Lights => _lights;

        public bool IsActive => _sim != null;

        /// <summary>How far between the last two simulation steps the current frame falls.</summary>
        public double Alpha => _accumulator / StepSeconds;

        /// <param name="gameFlow">Optional: without it, call <see cref="Begin"/> and <see cref="End"/> yourself.</param>
        /// <param name="qualityDirector">Optional: without it the traffic is the Medium tier's.</param>
        /// <param name="crowdController">Optional: without it nobody is out on the road.</param>
        public void Configure(GameFlow gameFlow, CityBuilder cityBuilder, FirstPersonController firstPerson, Camera camera, QualityDirector qualityDirector, CrowdController crowdController,
            TrafficMaterials trafficMaterials)
        {
            if (trafficMaterials == null) throw new ArgumentNullException(nameof(trafficMaterials));
            trafficMaterials.Validate();
            flow = gameFlow;
            city = cityBuilder;
            player = firstPerson;
            viewCamera = camera;
            quality = qualityDirector;
            crowd = crowdController;
            materials = trafficMaterials;
        }

        void Start()
        {
            if (city == null || player == null || viewCamera == null || materials == null)
            {
                throw new InvalidOperationException("TrafficController needs the city, player, camera and the traffic materials; call Configure.");
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
            _lights?.Dispose();
            _renderer?.Dispose();
            _sim = null;
        }

        void OnStateChanged(FlowTransition t)
        {
            if (t.To == FlowState.Playing && t.NewSession) Begin();
            else if (t.To == FlowState.Title) End();
        }

        void OnTier(QualityTier tier) => ApplyTier(tier);

        /// <summary>Start the traffic round the player's feet. The city must be built.</summary>
        public void Begin()
        {
            if (city.Slice == null) throw new InvalidOperationException("the traffic needs the city to be built first");
            if (LaunchOptions.Current.NoTraffic)
            {
                Debug.Log("[Cypher] traffic: switched off by --no-traffic");
                return;
            }
            QualityTier tier = quality != null ? quality.Tier : QualityTiers.For(QualityPreset.Medium);
            Vector3 at = player.transform.position;
            CityNav nav = CityNav.Build(city.Slice.Plan);
            _sim = TrafficSim.Create(nav, at.x, at.z, tier.GroundVehicles, tier.AircraftCount, rules: TrafficRules.City);
            _renderer?.Dispose();
            _renderer = new TrafficRenderer(materials, nav);
            _sim.InView = IsInView;
            _proxies = _proxies ?? new TrafficProxies(transform);
            _lights = _lights ?? new TrafficLights(transform);
            if (crowd != null) crowd.TrafficQuery = _sim;
            _accumulator = 0;
            _nextStatusLog = Time.realtimeSinceStartup + StatusLogSeconds;
            ApplyTier(tier);
            Debug.Log($"[Cypher] traffic: {tier.GroundVehicles} vehicles and {tier.AircraftCount} aircraft of a fleet of {_sim.Fleet.Count} and {_sim.AircraftFleet.Count} ({tier.Preset} tier), {tier.VehicleLights} lights, {_renderer.SignalHeads} signal heads");
        }

        /// <summary>Remove the traffic (the title screen has none).</summary>
        public void End()
        {
            if (crowd != null && ReferenceEquals(crowd.TrafficQuery, _sim)) crowd.TrafficQuery = null;
            _sim = null;
            _proxies?.ParkAll();
            _lights?.Off();
        }

        bool IsInView(double x, double y, double z, double radius) => _renderer != null && _renderer.View.IntersectsSphere(x, y, z, radius);

        void Update()
        {
            if (_sim == null) return;
            _renderer.UpdateFrustum(viewCamera);
            _accumulator += Time.deltaTime;
            Vector3 eye = viewCamera.transform.position, feet = player.transform.position;
            IPedestrianQuery people = crowd != null ? crowd.Sim : null;
            int steps = 0;
            while (_accumulator >= StepSeconds && steps < MaxStepsPerFrame)
            {
                _sim.Step(StepSeconds, eye.x, eye.z, feet.x, feet.z, people);
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
        /// Queue this frame's draws and put the point lights on the vehicles nearest the camera. Cameras draw them when they render after this call in the same frame, which is every
        /// camera in the normal frame loop (this runs in LateUpdate); something that renders a camera by hand earlier in the frame, such as the capture tool, calls this just before.
        /// </summary>
        public void DrawNow()
        {
            if (_sim == null) return;
            _renderer.Render(_sim, viewCamera, Math.Min(1.0, Alpha));
            _lights.Apply(_renderer.Frame.LightSpots, _lightsAllowed);
            if (Time.realtimeSinceStartup >= _nextStatusLog)
            {
                _nextStatusLog = Time.realtimeSinceStartup + StatusLogSeconds;
                TrafficFrame f = _renderer.Frame;
                Debug.Log($"[Cypher] traffic: {_sim.Vehicles.Count} vehicles and {_sim.Airborne.Count} aircraft, {f.GroundTotal} and {f.AircraftTotal} drawn, {_renderer.DrawCalls} draws, {_proxies.Leased} solid, {_lights.Lit} lit");
            }
        }

        /// <summary>Follow a quality tier: how many vehicles, aircraft and lit vehicles there are.</summary>
        public void ApplyTier(QualityTier tier)
        {
            if (tier == null) throw new ArgumentNullException(nameof(tier));
            _lightsAllowed = LaunchOptions.Current.NoVehicleLights ? 0 : tier.VehicleLights;
            if (_sim != null) _sim.SetBudget(tier.GroundVehicles, tier.AircraftCount);
        }
    }
}
