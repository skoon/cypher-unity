using System;
using System.Collections.Generic;
using Cypher.City;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using UnityEngine;

namespace Cypher.Combat
{
    /// <summary>A shot as the world saw it: where it went, what it hit (if anything), and how it looked.</summary>
    public readonly struct ShotResult
    {
        public readonly WeaponShot Shot;
        public readonly bool Hit;
        public readonly Vector3 Origin, Direction, Point, Normal, Muzzle;
        public readonly float Distance;
        public readonly ShotTarget Target;
        public readonly ImpactSurface Surface;

        /// <summary>For a hit on an enemy: which one (its id; -1 otherwise), and whether on the head.</summary>
        public readonly int Enemy;
        public readonly bool Head;

        public ShotResult(WeaponShot shot, bool hit, Vector3 origin, Vector3 direction, Vector3 point, Vector3 normal, Vector3 muzzle, float distance, ShotTarget target, ImpactSurface surface,
            int enemy = -1, bool head = false)
        {
            Shot = shot; Hit = hit; Origin = origin; Direction = direction; Point = point; Normal = normal; Muzzle = muzzle; Distance = distance; Target = target; Surface = surface;
            Enemy = enemy; Head = head;
        }
    }

    /// <summary>
    /// The guns in play, the Unity side of the prototype's <c>player/Weapons.js</c>: reads Fire, Aim, Reload and 1-3 (rebindable), steps the gun's rules
    /// (<see cref="WeaponRig"/>) at the prototype's fixed 60 Hz, traces each shot from the camera through the city, its props and the people's and vehicles'
    /// proxies (a hit on them is marked and does nothing else, the user's M4 choice), kicks the view, throws the brass, lights the muzzle, and draws the gun
    /// (<see cref="ViewmodelRig"/>) and the effects (<see cref="GunFxRenderer"/>). Out while playing; holstered on the title; held still (no fire, no keys)
    /// while a game screen is open.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(40)]   // after the player has moved and turned this frame
    public sealed class WeaponSystem : MonoBehaviour
    {
        public const int MaxStepsPerFrame = 4;

        [SerializeField] GameFlow flow;
        [SerializeField] FirstPersonController player;
        [SerializeField] Camera gameCamera;
        [SerializeField] SettingsService settings;
        [SerializeField] GunMaterials materials = new GunMaterials();
        [SerializeField] LayerMask shotMask = ~0;

        WeaponRig _rig;
        readonly ViewmodelPose _pose = new ViewmodelPose();
        readonly ImpactSim _fx = new ImpactSim();
        ViewmodelRig _view;
        GunFxRenderer _fxRenderer;
        GameInput _input;
        double _accumulator;
        bool _clicked, _reload;
        int _select;
        float _baseFov = -1;
        double _scriptedClock;
        readonly List<ShotResult> _shots = new List<ShotResult>();

        public WeaponRig Rig => _rig;
        public ImpactSim Effects => _fx;
        public ViewmodelPose Pose => _pose;
        public bool Drawn => _view != null && _view.Visible;

        /// <summary>
        /// The benchmark's trigger (<c>--benchmark-fire</c>): held on <see cref="BenchmarkFire"/>'s clock of play, with the reserve topped up so a long run never goes dry.
        /// </summary>
        public bool ScriptedFire { get; set; }

        /// <summary>Holds the gun still, as a menu does (the player is flatlined).</summary>
        public bool Suppressed { get; set; }

        /// <summary>Every shot traced, as it happens (the sounds and the HUD listen).</summary>
        public event Action<ShotResult> Shot;

        /// <summary>Everything else the gun does (switch, reload, dry fire, charge, ADS), as it happens.</summary>
        public event Action<WeaponEvent> GunEvent;

        /// <summary>The shots traced since the last call; for tests.</summary>
        public List<ShotResult> TakeShots()
        {
            var list = new List<ShotResult>(_shots);
            _shots.Clear();
            return list;
        }

        public void Configure(GameFlow gameFlow, FirstPersonController firstPerson, Camera camera, SettingsService settingsService, GunMaterials gunMaterials)
        {
            flow = gameFlow;
            player = firstPerson;
            gameCamera = camera;
            settings = settingsService;
            materials = gunMaterials;
        }

        void Start()
        {
            if (flow == null || player == null || gameCamera == null || settings == null || materials == null || !materials.IsComplete)
            {
                throw new InvalidOperationException("WeaponSystem needs the flow, the player, the camera, the settings and the gun materials; call Configure.");
            }
            _rig = new WeaponRig();
            _view = new ViewmodelRig(gameCamera.transform, materials);
            _fxRenderer = new GunFxRenderer(materials);
            _input = new GameInput(settings.Bindings);
            ScriptedFire |= BenchmarkOptions.Parse(Environment.GetCommandLineArgs()).Fire;
            flow.StateChanged += OnFlow;
        }

        void OnDestroy()
        {
            if (flow != null) flow.StateChanged -= OnFlow;
            _input?.Dispose();
            _view?.Dispose();
            _fxRenderer?.Dispose();
        }

        void OnFlow(FlowTransition t)
        {
            // a new game starts with full guns, the first one being drawn, and no marks from the last game's shots
            if (t.To == FlowState.Playing && t.NewSession)
            {
                _rig = new WeaponRig();
                _fx.Clear();
                _scriptedClock = 0;
            }
        }

        bool Acting => flow.State == FlowState.Playing && flow.Modal == FlowModal.None && !Suppressed;

        WeaponBody Body()
        {
            PlayerMotor m = player.Motor;
            return new WeaponBody { Grounded = m.Grounded, Sprinting = m.Sprinting, Crouching = m.Crouching, Speed = Math.Sqrt(m.VelocityX * m.VelocityX + m.VelocityZ * m.VelocityZ) };
        }

        void Update()
        {
            bool inGame = flow.State == FlowState.Playing || flow.State == FlowState.Paused;
            _view.SetVisible(inGame);
            if (!inGame)
            {
                RestoreFov();
                return;
            }

            // presses are gathered every frame and spent on the next fixed step, so none is lost between steps
            if (Acting)
            {
                _clicked |= _input.WasPressed(GameAction.Fire);
                _reload |= _input.WasPressed(GameAction.Reload);
                if (_input.WasPressed(GameAction.Weapon1)) _select = 1;
                else if (_input.WasPressed(GameAction.Weapon2)) _select = 2;
                else if (_input.WasPressed(GameAction.Weapon3)) _select = 3;
            }

            double dt = Time.deltaTime;
            WeaponBody body = Body();
            if (dt > 0)
            {
                _accumulator += dt;
                int steps = 0;
                while (_accumulator >= WeaponRig.FixedStep && steps < MaxStepsPerFrame)
                {
                    bool scripted = ScriptedFire && Acting && BenchmarkFire.Holding(_scriptedClock);
                    if (ScriptedFire && Acting)
                    {
                        _scriptedClock += WeaponRig.FixedStep;
                        _rig.Current.Reserve = _rig.Current.Config.Reserve;
                    }
                    var input = new WeaponInput
                    {
                        Acting = Acting, FireHeld = Acting && (_input.IsPressed(GameAction.Fire) || scripted), FireClicked = _clicked, AimHeld = Acting && _input.IsPressed(GameAction.Aim),
                        ReloadPressed = _reload, Select = _select,
                    };
                    _clicked = _reload = false;
                    _select = 0;
                    Step(input, body);
                    _accumulator -= WeaponRig.FixedStep;
                    steps++;
                }
                if (_accumulator > WeaponRig.FixedStep) _accumulator = 0;
                _fx.Update(dt);
            }

            // the frame's game time: nothing sways or springs while paused
            _pose.Update(_rig, body, dt, player.Look.Yaw, player.Look.Pitch, Time.timeAsDouble, _accumulator / WeaponRig.FixedStep);
            ApplyFov();
            _view.Apply(_rig, _pose, gameCamera);
        }

        void LateUpdate()
        {
            if (_fxRenderer != null && (flow.State == FlowState.Playing || flow.State == FlowState.Paused)) _fxRenderer.Draw(_fx, gameCamera);
        }

        /// <summary>One fixed step of the gun: its rules, then what its shots and events do in the world. Public for tests that drive it a step at a time.</summary>
        public void Step(WeaponInput input, WeaponBody body)
        {
            _rig.Step(WeaponRig.FixedStep, input, body);
            foreach (WeaponShot s in _rig.Shots) Fire(s);
            foreach (WeaponEvent e in _rig.Events)
            {
                switch (e.Kind)
                {
                    case WeaponEventKind.RevolverEject:
                        for (int i = 0; i < 6; i++) EjectShell(1.35, 0.8 + _fx.Random() * 0.4);
                        break;
                    case WeaponEventKind.CellEject:
                        EjectShell(2.6, 1);
                        break;
                }
                GunEvent?.Invoke(e);
            }
        }

        void Fire(WeaponShot shot)
        {
            WeaponConfig cfg = shot.Weapon;
            Transform cam = gameCamera.transform;
            player.Kick.Recoil(shot.RecoilPitch, shot.RecoilYaw);
            if (shot.Shake > 0) player.Kick.AddShake(shot.Shake);

            Vector3 origin = cam.position;
            Vector3 dir = (cam.forward + cam.right * (float)shot.SpreadRight + cam.up * (float)shot.SpreadUp).normalized;
            Vector3 muzzle = _view.AnchorWorld(true, cam);
            bool hit = Physics.Raycast(origin, dir, out RaycastHit h, (float)cfg.Range, shotMask, QueryTriggerInteraction.Ignore);
            Vector3 end = hit ? h.point : origin + dir * (float)cfg.Range;

            if (shot.Tracer)
            {
                double width = cfg.Tracer.Width * (cfg.Mode == FireMode.Charge ? 0.4 + shot.Power * 0.9 : 1);
                double life = cfg.Tracer.Life * (cfg.Mode == FireMode.Charge ? 1 + shot.Power : 1);
                _fx.SpawnTracer(V(muzzle), V(end), cfg.Tracer.Color, width, life, cfg.Tracer.Speed);
            }
            _fx.Flash(V(muzzle), cfg.Flash.Light, cfg.Flash.Power * shot.Power, cfg.Flash.Life * 1.6, 8 + cfg.Flash.Power * 0.18);
            for (int i = 0; i < (cfg.Mode == FireMode.Charge ? 3 : 1); i++)
            {
                Vector3 vel = dir * (float)(1.4 + _fx.Random() * 1.6);
                vel.y += 0.5f;
                _fx.SpawnSmoke(V(muzzle), V(vel), 0x9aa0ac, 0.05 + _fx.Random() * 0.05, 0.45, 0.16);
            }
            if (cfg.Shells) EjectShell(1, 1);

            ShotTarget target = ShotTarget.World;
            ImpactSurface surface = ImpactSurface.Concrete;
            int enemy = -1;
            bool head = false;
            if (hit)
            {
                target = ShotSurface.Of(h.collider);
                EnemyHitbox box = target == ShotTarget.Enemy ? EnemyHitbox.Of(h.collider) : null;
                if (box != null)
                {
                    enemy = box.Enemy;
                    head = box.Head;
                }
                surface = ImpactSim.SurfaceFor(target, cfg.Mode == FireMode.Charge, h.normal.y, h.point.y);
                double energy = Math.Max(0.25, Math.Min(1.4, shot.Power * (cfg.Damage / 70)));
                _fx.Impact(V(h.point), V(h.normal), surface, energy, player.transform.position.y);
                _rig.RegisterHit(target == ShotTarget.Person || target == ShotTarget.Enemy, target == ShotTarget.Prop || target == ShotTarget.Vehicle);
            }
            var result = new ShotResult(shot, hit, origin, dir, end, hit ? h.normal : Vector3.zero, muzzle, hit ? h.distance : (float)cfg.Range, target, surface, enemy, head);
            _shots.Add(result);
            Shot?.Invoke(result);
        }

        void EjectShell(double scale, double jitter)
        {
            Transform cam = gameCamera.transform;
            Vector3 port = _view.AnchorWorld(false, cam);
            Vector3 vel = cam.right * (float)((1.9 + _fx.Random() * 1.1) * jitter);
            vel.y += (float)(1.5 + _fx.Random() * 0.7);
            vel += cam.forward * 0.5f;
            PlayerMotor m = player.Motor;
            vel += new Vector3((float)m.VelocityX, (float)m.VelocityY, (float)m.VelocityZ);
            _fx.EjectShell(V(port), V(vel), scale, player.transform.position.y);
        }

        static V3 V(Vector3 v) => new V3(v.x, v.y, v.z);

        /// <summary>Aiming narrows the view to the gun's ADS field, scaled to the player's own field of view (the prototype's was a fixed 72 degrees).</summary>
        void ApplyFov()
        {
            if (_rig.Ads > 0.001)
            {
                if (_baseFov < 0) _baseFov = gameCamera.fieldOfView;
                gameCamera.fieldOfView = (float)(_baseFov * _pose.Fov / ViewmodelPose.CameraFov);
            }
            else
            {
                RestoreFov();
            }
        }

        void RestoreFov()
        {
            if (_baseFov < 0) return;
            gameCamera.fieldOfView = _baseFov;
            _baseFov = -1;
        }
    }
}
