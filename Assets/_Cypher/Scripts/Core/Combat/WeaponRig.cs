using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>What the gun is doing.</summary>
    public enum WeaponState { Draw, Idle, Holster, Reload, Inspect }

    /// <summary>The buttons this fixed step: held triggers, and presses already counted once.</summary>
    public struct WeaponInput
    {
        /// <summary>False while a menu or a conversation holds the player: nothing fires, switches or reloads.</summary>
        public bool Acting;
        public bool FireHeld, FireClicked, AimHeld, ReloadPressed;
        /// <summary>A gun asked for this step by its slot number (1 to 3); 0, the default, asks for none.</summary>
        public int Select;
    }

    /// <summary>The player's state that moves the gun: on the ground, horizontal speed (m/s), sprinting, crouching.</summary>
    public struct WeaponBody
    {
        public bool Grounded, Sprinting, Crouching;
        public double Speed;
    }

    /// <summary>A shot to trace: the camera-space spread (a direction in the disc around the view), its power, and the kicks it asks for.</summary>
    public readonly struct WeaponShot
    {
        public readonly WeaponConfig Weapon;
        public readonly double Power;
        /// <summary>The shot's offset from dead ahead, in radians: right and up of the view.</summary>
        public readonly double SpreadRight, SpreadUp;
        /// <summary>Camera recoil asked for (radians per second of kick velocity, as the prototype's <c>camera:recoil</c>) and shake.</summary>
        public readonly double RecoilPitch, RecoilYaw, Shake;
        /// <summary>Whether this shot draws a tracer.</summary>
        public readonly bool Tracer;
        public readonly int Index;

        public WeaponShot(WeaponConfig weapon, double power, double spreadRight, double spreadUp, double recoilPitch, double recoilYaw, double shake, bool tracer, int index)
        {
            Weapon = weapon; Power = power; SpreadRight = spreadRight; SpreadUp = spreadUp; RecoilPitch = recoilPitch; RecoilYaw = recoilYaw; Shake = shake;
            Tracer = tracer; Index = index;
        }
    }

    /// <summary>Something the gun did this step, for the sounds, the HUD and the shells.</summary>
    public enum WeaponEventKind { Fire, DryFire, ReloadStart, Reloaded, Ammo, Switch, Ads, Charge, Inspect, RevolverEject, CellEject }

    public readonly struct WeaponEvent
    {
        public readonly WeaponEventKind Kind;
        public readonly WeaponConfig Weapon;
        public readonly double Value;

        public WeaponEvent(WeaponEventKind kind, WeaponConfig weapon, double value = 0)
        {
            Kind = kind; Weapon = weapon; Value = value;
        }
    }

    /// <summary>One gun's magazine and reserve.</summary>
    public sealed class WeaponSlot
    {
        public readonly WeaponConfig Config;
        public int Ammo, Reserve;

        public WeaponSlot(WeaponConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Ammo = config.MagSize;
            Reserve = config.Reserve;
        }
    }

    /// <summary>
    /// The guns' rules, a port of the prototype's <c>Weapons.fixedUpdate</c> and its firing, reload and switch code (<c>player/Weapons.js</c>): select 1-3
    /// (holster, then draw), aim (not while sprinting or busy), reload (or, on a full revolver, the chamber check), the trigger by mode (auto while held, semi
    /// per click, charge on release), dry fire (which starts a reload when there is reserve), the spread that grows per shot and settles, and the small
    /// mechanical counters the viewmodel animates from. Fixed step; random draws from a seeded stream, so a session repeats exactly. Shots come out as
    /// <see cref="WeaponShot"/>s for the game to trace; what they hit comes back through <see cref="RegisterHit"/>.
    /// </summary>
    public sealed class WeaponRig
    {
        public const double FixedStep = 1.0 / 60;

        /// <summary>Above this sprint blend the gun is too low to fire.</summary>
        public const double SprintBlocksFire = 0.55;

        readonly List<WeaponSlot> _slots = new List<WeaponSlot>();
        readonly List<WeaponEvent> _events = new List<WeaponEvent>();
        readonly List<WeaponShot> _shots = new List<WeaponShot>();
        readonly Rng _rng;

        public IReadOnlyList<WeaponSlot> Slots => _slots;
        public int Index { get; private set; }
        public WeaponSlot Current => _slots[Index];
        public WeaponState State { get; private set; } = WeaponState.Draw;

        /// <summary>Seconds into the current scripted move (draw, holster, reload, inspect) and its length.</summary>
        public double AnimTime { get; private set; }
        public double AnimDuration { get; private set; }
        /// <summary>The slot a holster is heading to, or -1.</summary>
        public int Next { get; private set; } = -1;
        bool _animFired;

        public double FireTimer { get; private set; }
        public double Spread { get; private set; }
        public double Charge { get; private set; }
        public bool Charging { get; private set; }
        public bool AdsHeld { get; private set; }
        /// <summary>0 at the hip, 1 aimed down the sights, eased.</summary>
        public double Ads { get; private set; }
        /// <summary>0 to 1, eased: the low-ready while sprinting.</summary>
        public double Sprint { get; private set; }
        public double Heat { get; private set; }
        public double Bolt { get; private set; }
        public double Hammer { get; private set; } = 1;
        public double Cylinder { get; private set; }
        public double CylinderTarget { get; private set; }
        public double HitFlash { get; private set; }
        public double HitCritical { get; private set; }
        public double FlashTimer { get; private set; }
        public int ShotIndex { get; private set; }

        /// <summary>This step's events and shots (cleared at the start of each step).</summary>
        public IReadOnlyList<WeaponEvent> Events => _events;
        public IReadOnlyList<WeaponShot> Shots => _shots;

        /// <summary>Viewmodel kick impulses waiting for the pose (added by shots, taken by <see cref="ViewmodelPose"/>).</summary>
        public double KickPosX, KickPosY, KickPosZ, KickRotX, KickRotY, KickRotZ;

        public WeaponRig(uint seed = 0x6A5)
        {
            _rng = new Rng(seed);
            foreach (WeaponConfig c in WeaponTable.All) _slots.Add(new WeaponSlot(c));
            Equip(0, silent: true);
            Begin(WeaponState.Draw, Current.Config.DrawTime);
        }

        void Begin(WeaponState state, double duration, int next = -1)
        {
            State = state;
            AnimTime = 0;
            AnimDuration = duration;
            Next = next;
            _animFired = false;
        }

        void Equip(int index, bool silent = false)
        {
            Index = index;
            Spread = 0;
            Charge = 0;
            Charging = false;
            Bolt = 0;
            Hammer = 1;
            Cylinder = 0;
            CylinderTarget = 0;
            if (!silent) _events.Add(new WeaponEvent(WeaponEventKind.Switch, Current.Config));
        }

        /// <summary>Ask for a slot (keys 1-3, or the HUD).</summary>
        public void RequestSwitch(int index)
        {
            if (index < 0 || index >= _slots.Count) return;
            if (index == Index && State != WeaponState.Holster) return;
            if (State == WeaponState.Holster)
            {
                Next = index;
                return;
            }
            Begin(WeaponState.Holster, Current.Config.HolsterTime, index);
            Charging = false;
            Charge = 0;
        }

        /// <summary>One fixed step.</summary>
        public void Step(double dt, WeaponInput input, WeaponBody body)
        {
            if (!(dt > 0)) throw new ArgumentOutOfRangeException(nameof(dt), dt, "a weapon step needs time");
            _events.Clear();
            _shots.Clear();
            WeaponSlot w = Current;
            WeaponConfig cfg = w.Config;

            if (input.Acting && input.Select > 0) RequestSwitch(input.Select - 1);

            AdvanceAnim(dt);
            w = Current;
            cfg = w.Config;

            bool canAds = input.Acting && !body.Sprinting && State == WeaponState.Idle;
            bool wantAds = canAds && input.AimHeld;
            if (wantAds != AdsHeld)
            {
                AdsHeld = wantAds;
                _events.Add(new WeaponEvent(WeaponEventKind.Ads, cfg, wantAds ? 1 : 0));
            }
            Ads += ((wantAds ? 1 : 0) - Ads) * Math.Min(1, 13 * dt);
            Sprint += ((body.Sprinting && State == WeaponState.Idle ? 1 : 0) - Sprint) * Math.Min(1, 9 * dt);

            if (input.Acting && input.ReloadPressed && State == WeaponState.Idle)
            {
                if (w.Ammo >= cfg.MagSize && cfg.Id == "magnat") BeginInspect();
                else if (w.Ammo < cfg.MagSize && w.Reserve > 0) BeginReload();
            }

            FireTimer = Math.Max(0, FireTimer - dt);
            bool canShoot = input.Acting && State == WeaponState.Idle && Sprint < SprintBlocksFire;

            if (cfg.Mode == FireMode.Charge)
            {
                UpdateCharge(dt, canShoot, input);
            }
            else
            {
                bool pull = cfg.Mode == FireMode.Auto ? input.FireHeld : input.FireClicked;
                if (canShoot && pull)
                {
                    if (w.Ammo > 0)
                    {
                        if (FireTimer <= 0) Fire(1);
                    }
                    else if (cfg.Mode == FireMode.Semi || FireTimer <= 0)
                    {
                        DryFire();
                    }
                }
            }

            double rest = cfg.Spread.Base + MoveSpread(body, cfg);
            Spread += (rest - Spread) * Math.Min(1, cfg.Spread.Decay * dt);
            Spread = Math.Max(0, Math.Min(cfg.Spread.Max, Spread));

            Heat = Math.Max(0, Heat - dt * 7.5);
            Bolt = Math.Max(0, Bolt - dt * 16);
            HitFlash = Math.Max(0, HitFlash - dt * 5.5);
            HitCritical = Math.Max(0, HitCritical - dt * 5.5);
            FlashTimer = Math.Max(0, FlashTimer - dt);
            if (State == WeaponState.Idle) Hammer = Math.Min(1, Hammer + dt * 7);
            Cylinder += (CylinderTarget - Cylinder) * Math.Min(1, 16 * dt);
        }

        /// <summary>The spread moving adds: in the air, or with speed (less crouched).</summary>
        public static double MoveSpread(WeaponBody body, WeaponConfig cfg)
        {
            if (!body.Grounded) return cfg.Spread.Air;
            return cfg.Spread.Move * Math.Max(0, Math.Min(1.9, body.Speed / 4.1)) * (body.Crouching ? 0.4 : 1);
        }

        void UpdateCharge(double dt, bool canShoot, WeaponInput input)
        {
            WeaponSlot w = Current;
            WeaponConfig cfg = w.Config;
            bool holding = canShoot && input.FireHeld && w.Ammo > 0;
            if (holding)
            {
                if (!Charging)
                {
                    Charging = true;
                    Charge = 0;
                }
                Charge = Math.Min(1, Charge + dt / cfg.ChargeTime);
            }
            else if (Charging)
            {
                Charging = false;
                double c = Charge;
                Charge = 0;
                if (c >= cfg.MinCharge && w.Ammo > 0 && FireTimer <= 0)
                {
                    _events.Add(new WeaponEvent(WeaponEventKind.Charge, cfg, c));
                    Fire(0.4 + c * 0.6);
                }
            }
            else if (canShoot && input.FireHeld && w.Ammo <= 0 && input.FireClicked)
            {
                DryFire();
            }
        }

        void Fire(double power)
        {
            WeaponSlot w = Current;
            WeaponConfig cfg = w.Config;
            w.Ammo--;
            FireTimer = cfg.Interval;
            Spread = Math.Min(cfg.Spread.Max, Spread + cfg.Spread.PerShot * (Ads > 0.5 ? 0.55 : 1));
            Heat = 1;
            Bolt = 1;
            Hammer = 0;
            CylinderTarget += Math.PI / 3;
            FlashTimer = cfg.Flash.Life * (0.7 + power * 0.5);
            ShotIndex++;

            double rand = (_rng.Next() - 0.5) * 2;
            double adsDamp = 1 - Ads * 0.35;
            double pitch = cfg.Recoil.Pitch * power * adsDamp, yaw = cfg.Recoil.Yaw * rand * power * adsDamp;
            double shake = cfg.Recoil.Shake > 0.01 ? cfg.Recoil.Shake * power : 0;

            KickPosZ += cfg.Recoil.Back * power * 62 * adsDamp;
            KickPosY += cfg.Recoil.Up * power * 62 * adsDamp;
            KickPosX += rand * cfg.Recoil.Back * 14 * adsDamp;
            KickRotX += cfg.Recoil.Rot * power * 26 * adsDamp;
            KickRotZ += rand * cfg.Recoil.Roll * power * 22 * adsDamp;
            KickRotY += rand * cfg.Recoil.Roll * power * 9 * adsDamp;

            // a uniform disc around the view, at the spread in force (narrower aimed)
            double half = Spread * (Ads > 0.001 ? Lerp(1, cfg.Spread.Ads, Ads) : 1);
            double a = _rng.Next() * Math.PI * 2, r = Math.Sqrt(_rng.Next()) * half;
            bool tracer = cfg.Tracer.Every == 1 || ShotIndex % cfg.Tracer.Every == 0;
            _shots.Add(new WeaponShot(cfg, power, Math.Cos(a) * r, Math.Sin(a) * r, pitch, yaw, shake, tracer, ShotIndex));
            _events.Add(new WeaponEvent(WeaponEventKind.Fire, cfg, power));
            _events.Add(new WeaponEvent(WeaponEventKind.Ammo, cfg));
        }

        void DryFire()
        {
            WeaponSlot w = Current;
            FireTimer = 0.28;
            KickRotX += 1.2;
            _events.Add(new WeaponEvent(WeaponEventKind.DryFire, w.Config));
            if (w.Reserve > 0 && State == WeaponState.Idle) BeginReload();
        }

        /// <summary>What a shot hit, as the game found it: a person (a critical mark), a prop, or the world.</summary>
        public void RegisterHit(bool person, bool prop)
        {
            if (person || prop)
            {
                HitFlash = 1;
                if (person) HitCritical = 1;
            }
            else
            {
                HitFlash = Math.Max(HitFlash, 0.45);
            }
        }

        void BeginReload()
        {
            WeaponConfig cfg = Current.Config;
            Begin(WeaponState.Reload, cfg.ReloadTime);
            AdsHeld = false;
            Charging = false;
            Charge = 0;
            _events.Add(new WeaponEvent(WeaponEventKind.ReloadStart, cfg, cfg.ReloadTime));
        }

        void BeginInspect()
        {
            Begin(WeaponState.Inspect, 1.7);
            _events.Add(new WeaponEvent(WeaponEventKind.Inspect, Current.Config));
        }

        void AdvanceAnim(double dt)
        {
            if (State == WeaponState.Idle) return;
            double prev = AnimTime;
            AnimTime += dt;
            double u = AnimTime / AnimDuration;
            WeaponSlot w = Current;
            WeaponConfig cfg = w.Config;

            if (State == WeaponState.Reload)
            {
                if (!_animFired && u >= cfg.ReloadSeat)
                {
                    _animFired = true;
                    int take = Math.Min(cfg.MagSize - w.Ammo, w.Reserve);
                    w.Ammo += take;
                    w.Reserve -= take;
                    _events.Add(new WeaponEvent(WeaponEventKind.Ammo, cfg));
                }
                // the revolver's cases hit the pavement when the ejector rod is struck; the rail drops its spent cell
                if (cfg.Id == "magnat" && prev / AnimDuration < 0.34 && u >= 0.34) _events.Add(new WeaponEvent(WeaponEventKind.RevolverEject, cfg));
                if (cfg.Id == "voltline" && prev / AnimDuration < 0.30 && u >= 0.30) _events.Add(new WeaponEvent(WeaponEventKind.CellEject, cfg));
            }

            if (AnimTime >= AnimDuration)
            {
                if (State == WeaponState.Holster)
                {
                    int next = Next >= 0 ? Next : Index;
                    Equip(next);
                    Begin(WeaponState.Draw, Current.Config.DrawTime);
                    return;
                }
                if (State == WeaponState.Reload) _events.Add(new WeaponEvent(WeaponEventKind.Reloaded, cfg));
                State = WeaponState.Idle;
            }
        }

        /// <summary>The scripted move's progress, 0 to 1, <paramref name="alpha"/> of a fixed step past the last one (for drawing between steps).</summary>
        public double AnimProgress(double alpha) => AnimDuration > 0 ? Math.Max(0, Math.Min(1, (AnimTime + alpha * FixedStep) / AnimDuration)) : 1;

        static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
