using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>A moving part's pose this frame: an offset from where it rests, its rotation (radians, Y then X then Z) and whether it shows.</summary>
    public struct PartPose
    {
        public double DX, DY, DZ, RX, RY, RZ;
        public bool Visible;

        public static PartPose Rest => new PartPose { Visible = true };
    }

    /// <summary>
    /// Where the gun sits in the view this frame, a port of the prototype's <c>Weapons.lateUpdate</c>: hip to ADS (centred on the sight), the sprint low-ready,
    /// breathing when still, the walk bob, the sway that drags behind the view and springs back, the recoil springs, and the scripted draw, holster, reload and
    /// inspect moves with each gun's moving parts (bolt, magazine, support hand, hammer, yoke and cylinder, cell and coils). Also the four glow channels (always,
    /// ammo, charge, heat) and the viewmodel field of view. All in the prototype's weapon space (+X right, +Y up, -Z down the bore); the game mirrors Z.
    /// </summary>
    public sealed class ViewmodelPose
    {
        public const double CameraFov = 72;

        double _kpx, _kpy, _kpz, _kpvx, _kpvy, _kpvz, _krx, _kry, _krz, _krvx, _krvy, _krvz;
        double _swayX, _swayY, _swayVX, _swayVY, _prevYaw, _prevPitch;
        bool _hasPrev;
        double _bobPhase, _bobAmount, _chargeSpin;

        public double X, Y, Z, RX, RY, RZ;
        public readonly Dictionary<string, PartPose> Parts = new Dictionary<string, PartPose>();

        /// <summary>The glow channels: always on, ammo left, charge, muzzle heat.</summary>
        public double Glow0 = 1, Glow1 = 1, Glow2 = 1, Glow3;

        /// <summary>The camera's field of view while aiming (<see cref="CameraFov"/> at the hip) and the narrower viewmodel field the gun is drawn with.</summary>
        public double Fov = CameraFov, VmFov = 55;

        /// <summary>
        /// One frame. <paramref name="yaw"/> and <paramref name="pitch"/> are the view's (for sway), <paramref name="time"/> the game clock, <paramref name="alpha"/>
        /// how far past the last fixed step this frame is.
        /// </summary>
        public void Update(WeaponRig rig, WeaponBody body, double dt, double yaw, double pitch, double time, double alpha)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            double d = Math.Min(dt, 0.05);
            WeaponSlot w = rig.Current;
            WeaponConfig cfg = w.Config;
            double ads = rig.Ads;

            Fov = ads > 0.001 ? Lerp(CameraFov, cfg.AdsFov, ads) : CameraFov;
            VmFov = Lerp(cfg.VmFov, cfg.VmFovAds, ads);

            // the shot impulses waiting in the rig are velocities for the springs
            _kpvx += rig.KickPosX; _kpvy += rig.KickPosY; _kpvz += rig.KickPosZ;
            _krvx += rig.KickRotX; _krvy += rig.KickRotY; _krvz += rig.KickRotZ;
            rig.KickPosX = rig.KickPosY = rig.KickPosZ = rig.KickRotX = rig.KickRotY = rig.KickRotZ = 0;

            Sway(d, yaw, pitch, ads);
            Bob(d, body);
            Kick(d);
            Compose(rig, cfg, time, alpha);
            Glowing(rig, w, cfg, d, time);
        }

        void Sway(double dt, double yaw, double pitch, double ads)
        {
            if (!_hasPrev)
            {
                _prevYaw = yaw;
                _prevPitch = pitch;
                _hasPrev = true;
            }
            double dy = yaw - _prevYaw;
            while (dy > Math.PI) dy -= Math.PI * 2;
            while (dy < -Math.PI) dy += Math.PI * 2;
            double dx = pitch - _prevPitch;
            _prevYaw = yaw;
            _prevPitch = pitch;
            // the gun is heavy: it is dragged after the view, then springs back
            double drag = 26 * (1 - ads * 0.62);
            _swayVX -= Clamp(dy, -0.25, 0.25) * drag;
            _swayVY -= Clamp(dx, -0.25, 0.25) * drag;
            const double k = 90, c = 15;
            _swayVX += (-_swayX * k - _swayVX * c) * dt;
            _swayVY += (-_swayY * k - _swayVY * c) * dt;
            _swayX = Clamp(_swayX + _swayVX * dt, -0.09, 0.09);
            _swayY = Clamp(_swayY + _swayVY * dt, -0.07, 0.07);
        }

        void Bob(double dt, WeaponBody body)
        {
            double target = body.Grounded ? Clamp(body.Speed / 4.1, 0, 1.7) : 0;
            _bobAmount += (target - _bobAmount) * Math.Min(1, 6 * dt);
            _bobPhase += body.Speed * dt * (body.Sprinting ? 4.1 : 5.0);
        }

        void Kick(double dt)
        {
            // stiff springs: a sharp punch, a quick settle, a little overshoot
            const double kp = 210, cp = 21, kr = 190, cr = 19;
            _kpvx += (-_kpx * kp - _kpvx * cp) * dt;
            _kpvy += (-_kpy * kp - _kpvy * cp) * dt;
            _kpvz += (-_kpz * kp - _kpvz * cp) * dt;
            _kpx += _kpvx * dt; _kpy += _kpvy * dt; _kpz += _kpvz * dt;
            _krvx += (-_krx * kr - _krvx * cr) * dt;
            _krvy += (-_kry * kr - _krvy * cr) * dt;
            _krvz += (-_krz * kr - _krvz * cr) * dt;
            _krx += _krvx * dt; _kry += _krvy * dt; _krz += _krvz * dt;
        }

        void Compose(WeaponRig rig, WeaponConfig cfg, double t, double alpha)
        {
            double ads = rig.Ads;
            X = Lerp(cfg.Hip.X, -cfg.Sight.X, ads);
            Y = Lerp(cfg.Hip.Y, -cfg.Sight.Y, ads);
            Z = Lerp(cfg.Hip.Z, cfg.AdsZ, ads);
            RX = Lerp(cfg.Hip.RX, 0, ads);
            RY = Lerp(cfg.Hip.RY, 0, ads);
            RZ = Lerp(cfg.Hip.RZ, 0, ads);

            double sp = rig.Sprint * (1 - ads);
            if (sp > 0.001)
            {
                X = Lerp(X, cfg.Sprint.X, sp); Y = Lerp(Y, cfg.Sprint.Y, sp); Z = Lerp(Z, cfg.Sprint.Z, sp);
                RX = Lerp(RX, cfg.Sprint.RX, sp); RY = Lerp(RY, cfg.Sprint.RY, sp); RZ = Lerp(RZ, cfg.Sprint.RZ, sp);
            }

            // breathing keeps the frame alive standing still
            double still = (1 - _bobAmount) * (1 - sp);
            Y += Math.Sin(t * 1.15) * 0.0035 * still;
            X += Math.Sin(t * 0.83 + 1.1) * 0.0028 * still;
            RZ += Math.Sin(t * 0.71) * 0.010 * still;
            RX += Math.Sin(t * 1.15 + 0.4) * 0.008 * still;

            double bobK = cfg.BobAmp * _bobAmount * (1 - ads * 0.82) * (1 + sp * 0.85);
            double ph = _bobPhase;
            X += Math.Sin(ph) * bobK;
            Y += (Math.Cos(ph * 2) * 0.5 - 0.5) * bobK * 0.85;
            RZ += Math.Sin(ph) * cfg.BobRot * _bobAmount * (1 - ads * 0.85);
            RX += Math.Cos(ph * 2) * cfg.BobRot * 0.4 * _bobAmount * (1 - ads * 0.85);

            X += _swayX * 0.55;
            Y += _swayY * 0.45;
            RY += _swayX * 1.9;
            RX += _swayY * -1.5;
            RZ += _swayX * 1.1;

            X += _kpx; Y += _kpy; Z += _kpz;
            RX += _krx; RY += _kry; RZ += _krz;

            Parts.Clear();
            if (rig.State != WeaponState.Idle)
            {
                double u = rig.AnimProgress(alpha);
                switch (rig.State)
                {
                    case WeaponState.Holster: Holster(u); break;
                    case WeaponState.Draw: Draw(u); break;
                    case WeaponState.Inspect: Inspect(u, rig); break;
                    default: Reload(u, rig, cfg); break;
                }
            }
            else
            {
                Mechanics(rig, cfg);
            }
        }

        void Set(string part, PartPose p) => Parts[part] = p;

        void Mechanics(WeaponRig rig, WeaponConfig cfg)
        {
            switch (cfg.Id)
            {
                case "wasp":
                    Set("bolt", new PartPose { DZ = Math.Sin(rig.Bolt * Math.PI) * 0.052, Visible = true });
                    break;
                case "magnat":
                    Set("hammer", new PartPose { RX = 0.52 * rig.Hammer, Visible = true });
                    Set("cyl", new PartPose { RZ = rig.Cylinder, Visible = true });
                    break;
                case "voltline":
                    Set("coils", new PartPose { RZ = _chargeSpin, Visible = true });
                    break;
            }
        }

        void Holster(double u)
        {
            double e = Ease(u);
            Y -= 0.34 * e;
            Z += 0.10 * e;
            RX -= 1.35 * e;
            RZ += 0.35 * e;
        }

        void Draw(double u)
        {
            // overshoot a little on the way up so the draw has a snap
            double e = Ease(u), over = Math.Sin(u * Math.PI) * 0.10;
            Y -= 0.34 * (1 - e) - over * 0.10;
            Z += 0.10 * (1 - e);
            RX -= 1.35 * (1 - e) - over * 0.55;
            RZ += 0.35 * (1 - e);
            RY += 0.30 * (1 - e);
        }

        void Reload(double u, WeaponRig rig, WeaponConfig cfg)
        {
            if (cfg.Id == "wasp") ReloadWasp(u);
            else if (cfg.Id == "magnat") ReloadMagnat(u, rig);
            else ReloadVoltline(u);
        }

        void ReloadWasp(double u)
        {
            // canted toward the off hand so the mag well shows
            double tilt = BumpOf(u, 0.0, 1.0);
            X -= 0.045 * tilt; Y -= 0.075 * tilt; Z -= 0.020 * tilt;
            RZ += 0.62 * tilt; RY += 0.30 * tilt; RX += 0.16 * tilt;

            // the magazine drops away, a new one comes up and seats
            double drop = Seg(u, 0.10, 0.30), insert = Seg(u, 0.46, 0.68), magOut = drop * (1 - insert);
            Set("mag", new PartPose { DY = -magOut * 0.42, DZ = magOut * 0.05, RX = magOut * 0.55, Visible = !(drop > 0.98 && insert < 0.02) });
            double seatK = BumpOf(u, 0.66, 0.74);
            Y -= seatK * 0.018;
            RX += seatK * 0.10;

            // the charging handle racked back and let go
            double rack = BumpOf(u, 0.76, 0.92);
            Set("bolt", new PartPose { DZ = rack * 0.058, Visible = true });
            double rackK = BumpOf(u, 0.86, 0.94);
            Z += rackK * 0.012;
            RX += rackK * 0.09;

            // the support hand goes for the pouch and comes back with the magazine
            double away = Seg(u, 0.08, 0.30) * (1 - Seg(u, 0.44, 0.62));
            Set("support", new PartPose { DY = -away * 0.30, DZ = away * 0.14, RX = away * 0.6, Visible = true });
        }

        void ReloadMagnat(double u, WeaponRig rig)
        {
            // roll the frame over so the cylinder can swing out
            double roll = BumpOf(u, 0.0, 0.90);
            X -= 0.055 * roll; Y -= 0.055 * roll;
            RZ += 1.15 * roll; RY += 0.45 * roll; RX += 0.22 * roll;

            double open = Seg(u, 0.10, 0.26) * (1 - Seg(u, 0.60, 0.74));
            Set("yoke", new PartPose { RY = -1.45 * open, Visible = true });

            // the ejector rod: this is where the brass leaves
            double eject = BumpOf(u, 0.30, 0.40);
            Y += eject * 0.030;
            RX -= eject * 0.22;

            // fresh rounds go in and the cylinder is spun up
            double spin = Seg(u, 0.40, 0.62);
            Set("cyl", new PartPose { RZ = rig.Cylinder + spin * Math.PI * 3.4, Visible = true });

            // a flick of the wrist snaps it home
            double flick = BumpOf(u, 0.72, 0.86);
            RZ -= flick * 0.55;
            RX += flick * 0.30;
            Y += flick * 0.020;
            Set("hammer", new PartPose { RX = 0.52 * Seg(u, 0.86, 1.0), Visible = true });
        }

        void ReloadVoltline(double u)
        {
            // cant right so the cell housing faces up
            double tilt = BumpOf(u, 0.0, 1.0);
            X -= 0.035 * tilt; Y -= 0.070 * tilt;
            RZ -= 0.55 * tilt; RY += 0.34 * tilt; RX += 0.18 * tilt;

            double outK = Seg(u, 0.14, 0.32), inK = Seg(u, 0.50, 0.72), cellOut = outK * (1 - inK);
            Set("cell", new PartPose { DX = -cellOut * 0.10, DY = -cellOut * 0.34, RZ = cellOut * 0.9, Visible = !(outK > 0.98 && inK < 0.02) });
            double seatK = BumpOf(u, 0.70, 0.80);
            Y -= seatK * 0.022;
            RX += seatK * 0.13;

            // the coils spool up as the new cell comes online
            Set("coils", new PartPose { RZ = Seg(u, 0.72, 1.0) * Math.PI * 2.6, Visible = true });
            double away = Seg(u, 0.10, 0.30) * (1 - Seg(u, 0.48, 0.66));
            Set("support", new PartPose { DY = -away * 0.32, RX = away * 0.55, Visible = true });
        }

        /// <summary>The revolver's chamber check: swing out, spin the cylinder, snap it shut.</summary>
        void Inspect(double u, WeaponRig rig)
        {
            double lift = BumpOf(u, 0.0, 1.0);
            X -= 0.075 * lift; Y -= 0.020 * lift; Z -= 0.075 * lift;
            RY += 0.85 * lift; RZ += 0.95 * lift; RX += 0.30 * lift;
            double open = Seg(u, 0.10, 0.28) * (1 - Seg(u, 0.68, 0.84));
            Set("yoke", new PartPose { RY = -1.45 * open, Visible = true });
            Set("cyl", new PartPose { RZ = rig.Cylinder + Seg(u, 0.26, 0.70) * Math.PI * 2, Visible = true });
            double flick = BumpOf(u, 0.80, 0.94);
            RZ -= flick * 0.42;
            RX += flick * 0.24;
            Set("hammer", new PartPose { RX = 0.52 * rig.Hammer, Visible = true });
        }

        void Glowing(WeaponRig rig, WeaponSlot w, WeaponConfig cfg, double dt, double time)
        {
            double frac = (double)w.Ammo / cfg.MagSize;
            // ammo: bright when loaded, a slow red-alert pulse when dry
            double pulse = 0.55 + 0.45 * Math.Sin(time * 7.5);
            double ammoGlow = w.Ammo == 0 ? pulse * 0.55 : 0.22 + frac * 0.95;
            double chargeGlow = 1;
            if (cfg.Mode == FireMode.Charge)
            {
                double c = rig.Charge;
                double flicker = 0.9 + 0.1 * Math.Sin(time * 40 * (0.2 + c));
                chargeGlow = (0.30 + c * c * 5.0) * flicker * (w.Ammo > 0 ? 1 : 0.12);
                _chargeSpin += dt * (1.2 + c * 34);
            }
            Glow0 = 1;
            Glow1 = ammoGlow;
            Glow2 = chargeGlow;
            Glow3 = rig.Heat * rig.Heat * 3.4;
        }

        public static double Lerp(double a, double b, double t) => a + (b - a) * t;
        static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
        static double Win(double u, double a, double b) => Clamp((u - a) / (b - a), 0, 1);
        public static double Ease(double x) => x * x * (3 - 2 * x);
        /// <summary>An eased 0 to 1 ramp inside [a, b] of a normalised timeline.</summary>
        public static double Seg(double u, double a, double b) => Ease(Win(u, a, b));
        /// <summary>0 to 1 and back inside [a, b].</summary>
        public static double BumpOf(double u, double a, double b) => Math.Sin(Win(u, a, b) * Math.PI);
    }

    /// <summary>The view's recoil and shake, a port of the prototype's player (<c>camera:recoil</c>, <c>camera:shake</c>): kicks decay back to the aim, never moving it for good.</summary>
    public sealed class CameraKick
    {
        double _vx, _vy;

        public double Pitch { get; private set; }
        public double Yaw { get; private set; }
        public double Shake { get; private set; }

        public void Recoil(double pitch, double yaw)
        {
            _vx += pitch;
            _vy += yaw;
        }

        public void AddShake(double amount) => Shake = Math.Min(1.2, Shake + amount);

        /// <summary>A fixed step of the player.</summary>
        public void Step(double dt)
        {
            double damp = Math.Max(0, 1 - 12 * dt);
            _vx *= damp;
            _vy *= damp;
            Pitch += _vx * dt;
            Yaw += _vy * dt;
            double settle = Math.Max(0, 1 - 6.5 * dt);
            Pitch *= settle;
            Yaw *= settle;
            Shake = Math.Max(0, Shake - dt * 1.6);
        }

        /// <summary>The shake's offset of the camera (metres, right and up) at time <paramref name="t"/>.</summary>
        public (double X, double Y) ShakeOffset(double t) => Shake <= 0
            ? (0, 0)
            : ((Math.Sin(t * 63.1) + Math.Sin(t * 27.7)) * 0.5 * Shake * 0.045, (Math.Sin(t * 71.3) + Math.Sin(t * 31.1)) * 0.5 * Shake * 0.045);
    }
}
