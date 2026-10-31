using System;
using System.Collections.Generic;
using Cypher.Actors;
using Cypher.City;
using Cypher.Combat;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using Cypher.Rpg;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Cypher.Fight
{
    /// <summary>An enemy's shot as the world saw it, for the sound: who fired, from where, where it ended, and whether it struck something there (and what).</summary>
    public readonly struct EnemyShotFx
    {
        public readonly Enemy Shooter;
        public readonly WeaponConfig Gun;
        public readonly Vector3 Muzzle, End;
        public readonly bool HitPlayer, Impact;
        public readonly ImpactSurface Surface;

        public EnemyShotFx(Enemy shooter, WeaponConfig gun, Vector3 muzzle, Vector3 end, bool hitPlayer, bool impact, ImpactSurface surface)
        {
            Shooter = shooter; Gun = gun; Muzzle = muzzle; End = end; HitPlayer = hitPlayer; Impact = impact; Surface = surface;
        }
    }

    /// <summary>
    /// The fights, the Unity side of <see cref="EnemySim"/>: from a new game to the title it runs the enemies' brains at 60 Hz with the set encounters and the street
    /// patrols coming and going round the player, answers their line-of-sight questions through physics (walls, props and cars block; people, enemies and the player do
    /// not), poses their bodies (drawn with the crowd) and their hit volumes, turns the player's shots into hits on them (damage through the RPG's numbers, XP, cred, loot
    /// and job kills on a kill) and theirs into tracers, flashes, impacts and damage to the player. When the player flatlines the gun is held, the world goes on without
    /// them for <see cref="DownSeconds"/>, and the Trauma Team takes them to Doc Vey's for a tenth of their eddies; every fight is cleared away (the set encounters come back with their survivors).
    /// Quickhacks: while the scanner is held the gun is lowered, the enemy nearest the crosshair within hack range (and in view, or pinged) is the target, the mouse
    /// wheel picks a hack from the deck and Fire uploads it (<see cref="HackEffects"/> does what it does). Every shot, the player's or theirs, scatters the people
    /// within <see cref="ScatterRadius"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(35)]   // after the player moves, before the guns (40) fire and the crowd (40) draws the bodies
    public sealed class EnemyController : MonoBehaviour
    {
        public const double StepSeconds = 1.0 / 60;
        public const int MaxStepsPerFrame = 4;

        /// <summary>How long the player lies flatlined before the Trauma Team arrives.</summary>
        public const float DownSeconds = 3.5f;

        /// <summary>How far the crosshair picks out an enemy (for the health bar and for a patrol noticing it is aimed at).</summary>
        public const float AimRange = 120f;

        /// <summary>The enemies' tracers: the gang's red-orange, so incoming fire reads apart from the player's own.</summary>
        public const uint TracerColour = 0xff5a3c;

        public const uint Seed = 0xE7E7;

        /// <summary>How far a gunshot (anyone's) carries to the people in the street, who run from it; the cars carry on.</summary>
        public const double ScatterRadius = 35;

        /// <summary>How far off the crosshair (radians) an enemy can be and still be the hack target.</summary>
        public const double HackCone = 0.14;

        /// <summary>Where on an enemy the hack target is looked for, and its line of sight drawn to: the chest.</summary>
        public const float ChestHeight = 1.1f;

        [SerializeField] GameFlow flow;
        [SerializeField] CityBuilder city;
        [SerializeField] FirstPersonController player;
        [SerializeField] Camera viewCamera;
        [SerializeField] CrowdController crowd;
        [SerializeField] WeaponSystem guns;
        [SerializeField] RpgService rpg;

        EnemySim _sim;
        EncounterDirector _encounters;
        PatrolDirector _patrols;
        EnemyBodies _bodies;
        EnemyProxies _proxies;
        PhysicsWorld _world;
        Rng _rng;
        CoverMap _cover;
        EnemyPaths _paths;
        readonly HashSet<string> _clearedInSave = new HashSet<string>();
        IReadOnlyList<EncounterDef> _defs = Array.Empty<EncounterDef>();
        readonly Dictionary<string, bool> _wanted = new Dictionary<string, bool>();
        (int, int) _wantedAt = (-1, -1);
        HackEffects _hacks;
        readonly List<Enemy> _hackKills = new List<Enemy>();
        double _accumulator, _time;
        float _downFor;
        bool _subscribed, _pending;

        /// <summary>Set before a game starts to replace the content's encounters (<see cref="RpgGameContent.EncountersJson"/>); null for the content's.</summary>
        public IReadOnlyList<EncounterDef> Encounters { get; set; }

        /// <summary>The set encounters of the game running: the content's, put on this city, unless <see cref="Encounters"/> replaced them.</summary>
        public IReadOnlyList<EncounterDef> EncounterDefs => _defs;

        /// <summary>Whether street patrols come and go (tests switch them off to have the street to themselves).</summary>
        public bool PatrolsOn { get; set; } = true;

        public EnemySim Sim => _sim;
        public EnemyBodies Bodies => _bodies;
        public EnemyProxies Proxies => _proxies;
        public EncounterDirector EncounterDirector => _encounters;
        public PatrolDirector Patrols => _patrols;
        public CoverMap Cover => _cover;
        public bool IsActive => _sim != null;

        /// <summary>The living enemy under the crosshair this frame, or null.</summary>
        public Enemy Aimed { get; private set; }

        /// <summary>Flatlined, waiting for the Trauma Team; and how far through the wait (0 to 1).</summary>
        public bool Downed { get; private set; }

        public float DownProgress => Downed ? Mathf.Clamp01(_downFor / DownSeconds) : 0f;

        /// <summary>An enemy fired (the sounds listen).</summary>
        public event Action<EnemyShotFx> EnemyFired;

        /// <summary>An enemy's shot struck the player: from where, and for how much before armour (the HUD's hit arcs listen).</summary>
        public event Action<Vector3, double> PlayerHit;

        /// <summary>The player's shot struck an enemy: which, whether it killed, and the hit.</summary>
        public event Action<Enemy, bool, EnemyHit> EnemyHurt;

        /// <summary>The Trauma Team brought the player back: the fee.</summary>
        public event Action<int> Revived;

        /// <summary>A quickhack went up onto an enemy (the sounds listen).</summary>
        public event Action<Enemy, ActiveHack> HackUploaded;

        /// <summary>Fire was pressed to hack and nothing went: the reason (no RAM, no target, an empty deck).</summary>
        public event Action<string> HackRefused;

        /// <summary>The quickhacks on the enemies.</summary>
        public HackEffects Hacks => _hacks;

        /// <summary>The scanner is held in play: the gun is down and Fire uploads.</summary>
        public bool Hacking { get; private set; }

        /// <summary>The enemy a hack would go onto now, or null; only while <see cref="Hacking"/>.</summary>
        public Enemy HackTarget { get; private set; }

        /// <summary>The deck slot picked (an index into the deck, always a slot holding a hack while there is one), or -1 for an empty deck.</summary>
        public int HackSlot { get; private set; } = -1;

        public void Configure(GameFlow gameFlow, CityBuilder cityBuilder, FirstPersonController firstPerson, Camera camera, CrowdController crowdController, WeaponSystem weaponSystem,
            RpgService rpgService)
        {
            flow = gameFlow;
            city = cityBuilder;
            player = firstPerson;
            viewCamera = camera;
            crowd = crowdController;
            guns = weaponSystem;
            rpg = rpgService;
        }

        void Start()
        {
            if (flow == null || city == null || player == null || viewCamera == null || crowd == null || guns == null || rpg == null)
            {
                throw new InvalidOperationException("EnemyController needs the flow, the city, the player, the camera, the crowd, the guns and the RPG; call Configure.");
            }
            flow.StateChanged += OnStateChanged;
            guns.Shot += OnPlayerShot;
            rpg.Event += OnRpgEvent;
            _subscribed = true;
        }

        void OnDestroy()
        {
            if (_subscribed)
            {
                flow.StateChanged -= OnStateChanged;
                guns.Shot -= OnPlayerShot;
                rpg.Event -= OnRpgEvent;
            }
            _proxies?.Dispose();
        }

        void OnStateChanged(FlowTransition t)
        {
            // the RPG and the crowd start on the same transition: begin on the next frame, once they have
            if (t.To == FlowState.Playing && t.NewSession)
            {
                End();
                _pending = true;
            }
            else if (t.To == FlowState.Title)
            {
                _pending = false;
                End();
            }
        }

        /// <summary>Start the fights for a game: the city's cover and ways, the directors, no one in the world yet. The city and the RPG must be ready.</summary>
        public void Begin()
        {
            if (city.Slice == null) throw new InvalidOperationException("the fights need the city to be built first");
            if (rpg.Core == null) throw new InvalidOperationException("the fights need the RPG's game to have begun");
            var clock = System.Diagnostics.Stopwatch.StartNew();
            CityPlan plan = city.Slice.Plan;
            CityNav nav = crowd.Sim != null ? crowd.Sim.Nav : CityNav.Build(plan);
            IReadOnlyList<PropBox> boxes = city.Props != null ? city.Props.Layout.Boxes : (IReadOnlyList<PropBox>)Array.Empty<PropBox>();
            var buildings = new List<(double, double, double, double)>(plan.Buildings.Count);
            foreach (Building b in plan.Buildings) buildings.Add((b.X, b.Z, b.Width, b.Depth));
            _cover = CoverMap.Build(boxes, buildings, nav.GroundY);
            _paths = new EnemyPaths(nav);
            _sim = new EnemySim(Seed, _cover, _paths);
            _defs = Encounters ?? EncounterContent.Build(RpgGameContent.EncountersJson, rpg.Core.Locations, nav, boxes, buildings);
            _clearedInSave.Clear();
            foreach (EncounterDef d in _defs) if (rpg.EncounterCleared(d.Id)) _clearedInSave.Add(d.Id);
            _wanted.Clear();
            _wantedAt = (-1, -1);
            _encounters = new EncounterDirector(_sim, _defs, id => _clearedInSave.Contains(id), rpg.EncounterDown, Wanted);
            _patrols = new PatrolDirector(_sim, _paths, Seed ^ 0x51u, "street");
            _bodies = new EnemyBodies(Seed);
            _proxies = _proxies ?? new EnemyProxies(transform);
            _world = new PhysicsWorld(player, crowd, _proxies);
            _rng = new Rng(Seed ^ 0xC0u);
            _hacks = new HackEffects(_sim);
            _hacks.Clear(rpg.Core.Deck.Active);   // a save's hacks named enemies that are not in this world
            _accumulator = _time = 0;
            Aimed = null;
            Hacking = false;
            HackTarget = null;
            SetDowned(false);
            crowd.Fighters = _bodies.All;
            Debug.Log($"[Cypher] fights: {_cover.Points.Count} cover points, {_defs.Count} encounters ({_clearedInSave.Count} cleared), patrols {(PatrolsOn ? "on" : "off")}; ready in {clock.ElapsedMilliseconds} ms");
        }

        /// <summary>Take the fights away (the title has none).</summary>
        public void End()
        {
            if (_sim == null) return;
            if (Downed) SetDowned(false);
            _sim = null;
            _encounters = null;
            _patrols = null;
            _bodies = null;
            if (rpg.Core != null) _hacks.Clear(rpg.Core.Deck.Active);
            _hacks = null;
            crowd.Fighters = null;
            Aimed = null;
            Hacking = false;
            HackTarget = null;
            guns.Suppressed = false;
            _proxies?.ParkAll();
        }

        /// <summary>Put a hostile squad in the world now (tests, and the debug spot): the kinds at the places given, facing <paramref name="yaw"/>.</summary>
        public EnemySquad Spawn(string tag, IReadOnlyList<(EnemyArchetype Kind, Vector3 At)> members, double yaw = 0, bool hostile = true)
        {
            if (_sim == null) throw new InvalidOperationException("no game running");
            EnemySquad squad = _sim.AddSquad(tag, hostile);
            foreach ((EnemyArchetype kind, Vector3 at) in members) _sim.Add(squad, kind, new V3(at.x, at.y, at.z), yaw);
            return squad;
        }

        PlayerView View()
        {
            Vector3 feet = player.transform.position;
            PlayerMotor m = player.Motor;
            double speed = Math.Sqrt(m.VelocityX * m.VelocityX + m.VelocityZ * m.VelocityZ);
            double eye = Math.Max(0.5, viewCamera.transform.position.y - feet.y);
            bool alive = !Downed && rpg.Core != null && rpg.Core.Health > 0;
            return new PlayerView(new V3(feet.x, feet.y, feet.z), eye, speed, m.Crouching, alive);
        }

        void Update()
        {
            if (_pending && rpg.Core != null && city.Slice != null)
            {
                _pending = false;
                Begin();
            }
            if (_sim == null) return;
            double dt = Time.deltaTime;
            if (Downed)
            {
                _downFor += (float)dt;
                if (_downFor >= DownSeconds) Revive();
            }
            if (flow.State == FlowState.Playing && rpg.Core != null)
            {
                _accumulator += dt;
                int steps = 0;
                while (_accumulator >= StepSeconds && steps < MaxStepsPerFrame)
                {
                    StepOnce();
                    _accumulator -= StepSeconds;
                    steps++;
                }
                if (_accumulator > StepSeconds) _accumulator = 0;
            }
            Aimed = FindAimed();
            UpdateHacking();
        }

        /// <summary>The scanner held: the gun down, a target, the wheel and Fire.</summary>
        void UpdateHacking()
        {
            Hacking = !Downed && flow.State == FlowState.Playing && flow.Modal == FlowModal.None && rpg.Core != null && rpg.Input != null
                && rpg.Input.IsPressed(GameAction.Scan);
            guns.Suppressed = Downed || Hacking;
            HackTarget = Hacking ? FindHackTarget() : null;
            if (!Hacking) return;
            KeepSlot(0);
            float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
            if (wheel != 0) SelectHack(wheel < 0 ? 1 : -1);
            if (rpg.Input.WasPressed(GameAction.Fire))
            {
                RpgResult r = UploadHack();
                if (!r.Ok) HackRefused?.Invoke(r.Reason);
            }
        }

        /// <summary>Pick the next (<paramref name="step"/> 1) or previous (-1) hack in the deck, skipping empty slots.</summary>
        public void SelectHack(int step)
        {
            if (step != 1 && step != -1) throw new ArgumentOutOfRangeException(nameof(step), step, "one slot on or back");
            KeepSlot(step);
        }

        /// <summary>Keep <see cref="HackSlot"/> on a slot holding a hack: from where it is, moving by <paramref name="step"/> (0: stay, unless that slot is empty).</summary>
        void KeepSlot(int step)
        {
            if (rpg.Core == null) return;
            Item[] deck = rpg.Core.Inventory.DeckItems();
            int at = HackSlot;
            if (step == 0 && at >= 0 && at < deck.Length && deck[at] != null) return;
            int dir = step == 0 ? 1 : step;
            for (int i = 1; i <= deck.Length; i++)
            {
                int k = (((at < 0 ? -1 : at) + dir * i) % deck.Length + deck.Length) % deck.Length;
                if (deck[k] == null) continue;
                HackSlot = k;
                return;
            }
            HackSlot = -1;
        }

        /// <summary>
        /// Upload the picked hack onto the target. Fails (with the RPG's reason, or "no-target" / "empty-deck") when there is nothing to send or nowhere to send it; the
        /// RPG tells the player when RAM is short.
        /// </summary>
        public RpgResult UploadHack()
        {
            if (_sim == null || rpg.Core == null) throw new InvalidOperationException("no game running");
            if (HackSlot < 0) return RpgResult.Fail("empty-deck");
            Enemy e = HackTarget;
            if (e == null || e.State == EnemyState.Down) return RpgResult.Fail("no-target");
            Item item = rpg.Core.Inventory.DeckItems()[HackSlot];
            string target = HackEffects.TargetOf(e);
            RpgResult r = rpg.Core.UploadQuickhack(HackSlot, target);
            if (!r.Ok) return r;
            ActiveHack running = rpg.Core.Deck.Active.Find(a => a.HackId == item.TemplateId && a.TargetId == target)
                ?? throw new InvalidOperationException($"the deck took {item.TemplateId} but is not running it on {target}");
            Vector3 feet = player.transform.position;
            if (_hacks.Applied(running, new V3(feet.x, feet.y, feet.z), rpg.Core.Derived["hackStrength"])) rpg.Kill(e.Archetype, e.Squad.Tag, U(e.Position));
            HackUploaded?.Invoke(e, running);
            return r;
        }

        /// <summary>The living enemy nearest the crosshair, within the player's hack range and the cone, that the eye can see (or that is pinged).</summary>
        Enemy FindHackTarget()
        {
            Transform cam = viewCamera.transform;
            Vector3 eye = cam.position, forward = cam.forward;
            double range = rpg.Core.Derived["hackRange"];
            Enemy best = null;
            double bestOff = HackCone;
            IReadOnlyList<Enemy> all = _sim.Enemies;
            for (int i = 0; i < all.Count; i++)
            {
                Enemy e = all[i];
                if (e.State == EnemyState.Down) continue;
                Vector3 chest = new Vector3((float)e.Position.X, (float)e.Position.Y + ChestHeight, (float)e.Position.Z);
                Vector3 to = chest - eye;
                float d = to.magnitude;
                if (d > range || d < 1e-3f) continue;
                double off = Math.Acos(Mathf.Clamp(Vector3.Dot(forward, to / d), -1f, 1f));
                if (off > bestOff) continue;
                if (!e.Revealed && !_world.Clear(V(eye), V(chest))) continue;
                bestOff = off;
                best = e;
            }
            return best;
        }

        void FixedUpdate()
        {
            if (_sim == null) return;
            _proxies.Update(_bodies.All, 1, _time);
        }

        /// <summary>One 60 Hz step of the fights. Public for tests that drive it a step at a time.</summary>
        public void StepOnce()
        {
            if (_sim == null) throw new InvalidOperationException("no game running");
            _time += StepSeconds;
            PlayerView view = View();
            _encounters.Step(view.Feet);
            SaveDown();
            for (int i = 0; i < _encounters.Cleared.Count; i++)
            {
                string id = _encounters.Cleared[i];
                _clearedInSave.Add(id);
                rpg.ClearEncounter(id);
            }
            if (PatrolsOn) _patrols.Step(StepSeconds, view.Eye, _world);
            if (Aimed != null && view.Alive) _sim.AimedAt(Aimed, view.Eye, StepSeconds);
            _hackKills.Clear();
            _hacks.Step(StepSeconds, rpg.Core.Deck.Active, rpg.Core.Derived["hackStrength"], _hackKills);
            for (int i = 0; i < _hackKills.Count; i++) rpg.Kill(_hackKills[i].Archetype, _hackKills[i].Squad.Tag, U(_hackKills[i].Position));
            _sim.Step(StepSeconds, view, _world);

            IReadOnlyList<EnemyEvent> events = _sim.Events;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Kind == EnemyEventKind.Shot) EnemyShot(events[i], view);
                // a death in the step is one squadmate shooting another, which the player's hack set off: theirs
                else if (events[i].Kind == EnemyEventKind.Died && _sim.Find(events[i].Enemy) is Enemy dead) rpg.Kill(dead.Archetype, dead.Squad.Tag, U(dead.Position));
            }
            _bodies.Step(StepSeconds, _sim, view.Chest);
        }

        void EnemyShot(EnemyEvent ev, PlayerView view)
        {
            Enemy e = _sim.Find(ev.Enemy);
            if (e == null) return;
            crowd.Sim?.Alarm(ev.From.X, ev.From.Z, ScatterRadius);
            WeaponConfig gun = WeaponTable.Get(e.Archetype.WeaponId);
            Vector3 eye = U(ev.From), chest = U(ev.To);
            Vector3 aim = (chest - eye).normalized;
            Vector3 muzzle = eye + aim * 0.75f + Vector3.down * 0.15f;
            Vector3 end;
            bool impact = false;
            ImpactSurface surface = ImpactSurface.Concrete;
            if (ev.Hit && ev.Victim >= 0)
            {
                end = chest;   // into a squadmate (the sim has already harmed it)
            }
            else if (ev.Hit && view.Alive)
            {
                end = chest - aim * 0.6f;   // stops short of the eye
                rpg.HurtPlayer(ev.Damage, e.Archetype.Id);
                PlayerHit?.Invoke(eye, ev.Damage);
            }
            else
            {
                V3 through = EnemyShots.MissPoint(ev.From, ev.To, _rng);
                Vector3 dir = (U(through) - muzzle).normalized;
                if (_world.Cast(muzzle, dir, (float)gun.Range, out RaycastHit h))
                {
                    end = h.point;
                    impact = true;
                    ShotTarget target = ShotSurface.Of(h.collider);
                    surface = ImpactSim.SurfaceFor(target, false, h.normal.y, h.point.y);
                    guns.Effects.Impact(V(h.point), V(h.normal), surface, Math.Max(0.25, Math.Min(1.4, gun.Damage / 70)), player.transform.position.y);
                }
                else
                {
                    end = muzzle + dir * (float)gun.Range;
                }
            }
            guns.Effects.SpawnTracer(V(muzzle), V(end), TracerColour, gun.Tracer.Width, gun.Tracer.Life, gun.Tracer.Speed);
            guns.Effects.Flash(V(muzzle), gun.Flash.Light, gun.Flash.Power, gun.Flash.Life * 1.6, 8 + gun.Flash.Power * 0.18);
            EnemyFired?.Invoke(new EnemyShotFx(e, gun, muzzle, end, ev.Hit && ev.Victim < 0 && view.Alive, impact, surface));
        }

        void OnPlayerShot(ShotResult r)
        {
            if (_sim == null || rpg.Core == null) return;
            Vector3 feet = player.transform.position;
            _sim.Hear(new V3(feet.x, feet.y, feet.z));
            crowd.Sim?.Alarm(feet.x, feet.z, ScatterRadius);
            if (r.Target != ShotTarget.Enemy || r.Enemy < 0) return;
            Enemy e = _sim.Find(r.Enemy);
            if (e == null || e.State == EnemyState.Down) return;
            EnemyHit hit = DamageModel.PlayerShot(r.Shot.Weapon, r.Shot.Power, rpg.Shooter, e.Archetype, r.Head, _rng.Next());
            bool killed = _sim.Hit(e, hit, V(r.Origin));
            if (killed) rpg.Kill(e.Archetype, e.Squad.Tag, U(e.Position));
            EnemyHurt?.Invoke(e, killed, hit);
        }

        Enemy FindAimed()
        {
            if (Downed || flow.State != FlowState.Playing) return null;
            Transform cam = viewCamera.transform;
            if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit h, AimRange, ~0, QueryTriggerInteraction.Ignore)) return null;
            EnemyHitbox box = EnemyHitbox.Of(h.collider);
            if (box == null || box.Enemy < 0) return null;
            Enemy e = _sim.Find(box.Enemy);
            return e != null && e.State != EnemyState.Down ? e : null;
        }

        void OnRpgEvent(string type, Cypher.Core.JsonObject payload)
        {
            if (type == "rpg:playerDied" && _sim != null && !Downed) SetDowned(true);
        }

        void SetDowned(bool down)
        {
            Downed = down;
            _downFor = 0;
            player.AutoTick = !down;
            guns.Suppressed = down || Hacking;
        }

        /// <summary>Keep in the save who of the encounters went down, so they come back with their survivors only.</summary>
        void SaveDown()
        {
            IReadOnlyList<(string Id, int Member)> down = _encounters.JustDown;
            for (int i = 0; i < down.Count; i++) rpg.MarkEncounterDown(down[i].Id, down[i].Member);
        }

        /// <summary>Whether an encounter is wanted now (its <see cref="EncounterDef.When"/>), asked of the RPG once per change to it.</summary>
        bool Wanted(EncounterDef d)
        {
            if (d.When == null) return true;
            (int, int) at = (rpg.Core.Revision, rpg.Core.Quests.Revision);
            if (at != _wantedAt)
            {
                _wantedAt = at;
                _wanted.Clear();
            }
            if (!_wanted.TryGetValue(d.Id, out bool wanted)) _wanted[d.Id] = wanted = rpg.Core.Check(d.When).Ok;
            return wanted;
        }

        /// <summary>The Trauma Team: billed and patched up at Doc Vey's, and every fight cleared away (the set encounters come back with their survivors only).</summary>
        void Revive()
        {
            int fee = rpg.TraumaTeam();
            _hacks.Clear(rpg.Core.Deck.Active);
            _encounters.Reset();
            SaveDown();
            _patrols.Reset();
            while (_sim.Squads.Count > 0) _sim.Remove(_sim.Squads[_sim.Squads.Count - 1]);   // squads the directors do not own (Spawn) go too
            _bodies.Step(0, _sim, new V3(0, 0, 0));   // the bodies go with their enemies
            _proxies.ParkAll();
            Aimed = null;
            SetDowned(false);
            Debug.Log($"[Cypher] fights: flatlined; the Trauma Team took {fee} eddies");
            Revived?.Invoke(fee);
        }

        static Vector3 U(V3 v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        static V3 V(Vector3 v) => new V3(v.x, v.y, v.z);

        /// <summary>
        /// Line of sight and walking room through physics: anything solid blocks but the player's own capsule and the people's and enemies' stand-ins (people do not stop a
        /// look or a bullet here; the guns do not hurt them). No allocation per question.
        /// </summary>
        sealed class PhysicsWorld : IEnemyWorld
        {
            /// <summary>Walking room is asked at knee height: a kerb does not block it, a barrier does.</summary>
            public const float KneeHeight = 0.5f;

            readonly RaycastHit[] _hits = new RaycastHit[16];
            readonly HashSet<Collider> _ignore = new HashSet<Collider>();

            public PhysicsWorld(FirstPersonController player, CrowdController crowd, EnemyProxies proxies)
            {
                _ignore.Add(player.Controller);
                if (crowd.Proxies != null)
                {
                    for (int i = 0; i < CrowdProxies.Count; i++) _ignore.Add(crowd.Proxies[i].GetComponent<Collider>());
                }
                for (int i = 0; i < EnemyProxies.Count * 2; i++) _ignore.Add(proxies[i].GetComponent<Collider>());
            }

            public bool Clear(V3 from, V3 to)
            {
                Vector3 a = U(from), d = U(to) - a;
                float length = d.magnitude;
                if (length < 1e-4f) return true;
                int n = Physics.RaycastNonAlloc(a, d / length, _hits, length, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) if (!_ignore.Contains(_hits[i].collider)) return false;
                return true;
            }

            public bool Walkable(V3 from, V3 to) => Clear(new V3(from.X, from.Y + KneeHeight, from.Z), new V3(to.X, to.Y + KneeHeight, to.Z));

            /// <summary>The nearest solid thing along a ray, ignoring the same stand-ins.</summary>
            public bool Cast(Vector3 origin, Vector3 dir, float range, out RaycastHit nearest)
            {
                int n = Physics.RaycastNonAlloc(origin, dir, _hits, range, ~0, QueryTriggerInteraction.Ignore);
                nearest = default;
                float best = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (_ignore.Contains(_hits[i].collider) || _hits[i].distance >= best) continue;
                    best = _hits[i].distance;
                    nearest = _hits[i];
                }
                return best < float.PositiveInfinity;
            }
        }
    }
}
