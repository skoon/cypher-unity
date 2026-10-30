using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>How a gun's trigger works.</summary>
    public enum FireMode { Auto, Semi, Charge }

    /// <summary>A gun's spread, as half-angles in radians: at rest, added per shot, the most, how fast it settles, and what moving, the air and aiming do.</summary>
    public readonly struct SpreadSpec
    {
        public readonly double Base, PerShot, Max, Decay, Move, Air, Ads;

        public SpreadSpec(double @base, double perShot, double max, double decay, double move, double air, double ads)
        {
            Base = @base; PerShot = perShot; Max = max; Decay = decay; Move = move; Air = air; Ads = ads;
        }
    }

    /// <summary>A gun's kick: the camera's (pitch, yaw, shake) and the viewmodel's (back, up, rotation, roll).</summary>
    public readonly struct RecoilSpec
    {
        public readonly double Pitch, Yaw, Back, Up, Rot, Roll, Shake;

        public RecoilSpec(double pitch, double yaw, double back, double up, double rot, double roll, double shake)
        {
            Pitch = pitch; Yaw = yaw; Back = back; Up = up; Rot = rot; Roll = roll; Shake = shake;
        }
    }

    /// <summary>The muzzle flash: its colour, the light's colour, size, the light's power and how long it lasts.</summary>
    public readonly struct FlashSpec
    {
        public readonly uint Color, Light;
        public readonly double Scale, Power, Life;

        public FlashSpec(uint color, uint light, double scale, double power, double life)
        {
            Color = color; Light = light; Scale = scale; Power = power; Life = life;
        }
    }

    /// <summary>The tracer: every how many shots, colour, width, life and speed (0 is an instant beam).</summary>
    public readonly struct TracerSpec
    {
        public readonly int Every;
        public readonly uint Color;
        public readonly double Width, Life, Speed;

        public TracerSpec(int every, uint color, double width, double life, double speed)
        {
            Every = every; Color = color; Width = width; Life = life; Speed = speed;
        }
    }

    /// <summary>A viewmodel pose: position and Euler rotation (radians, applied Y then X then Z), in the prototype's weapon space (-Z down the bore).</summary>
    public readonly struct PoseSpec
    {
        public readonly double X, Y, Z, RX, RY, RZ;

        public PoseSpec(double x, double y, double z, double rx, double ry, double rz)
        {
            X = x; Y = y; Z = z; RX = rx; RY = ry; RZ = rz;
        }
    }

    /// <summary>One gun of the table.</summary>
    public sealed class WeaponConfig
    {
        public string Id, Name, Kind;
        public int Slot;
        public FireMode Mode;
        public double Rpm, Damage, Range;
        public int MagSize, Reserve;
        public double ReloadTime, HolsterTime, DrawTime;
        public double ChargeTime, MinCharge;
        public SpreadSpec Spread;
        public RecoilSpec Recoil;
        public FlashSpec Flash;
        public TracerSpec Tracer;
        public bool Shells;
        public uint Accent;
        public double VmFov, VmFovAds, AdsFov;
        public PoseSpec Hip, Sprint;
        public double AdsZ;
        public double BobAmp, BobRot;
        /// <summary>When in a reload the new magazine, cylinder or cell seats and the ammo moves.</summary>
        public double ReloadSeat;

        /// <summary>The model's anchors (player/WeaponModels.js), in weapon space: where the shots leave, where cases are thrown from, and the sight the ADS pose centres.</summary>
        public (double X, double Y, double Z) Muzzle, Eject, Sight;

        /// <summary>Seconds between shots.</summary>
        public double Interval => 60 / Rpm;
    }

    /// <summary>
    /// The prototype's three guns (<c>player/Weapons.js</c> <c>WEAPONS</c>), numbers unchanged: the Wasp SMG (1, full auto), the Magnat revolver (2, semi) and the
    /// Voltline rail projector (3, charge). The user chose these over guns made from the RPG's items (M4 step 6).
    /// </summary>
    public static class WeaponTable
    {
        // the prototype's NEON palette (player/WeaponModels.js)
        public const uint Cyan = 0x36f2ff, Ice = 0x9df6ff, Magenta = 0xff2f8e, Violet = 0xa060ff, Amber = 0xffa32c;

        public static readonly IReadOnlyList<WeaponConfig> All = new[]
        {
            new WeaponConfig
            {
                Id = "wasp", Name = "WASP-9", Kind = "SMG // KINETIC", Slot = 1, Mode = FireMode.Auto, Rpm = 880, Damage = 13, Range = 170, MagSize = 32, Reserve = 256,
                ReloadTime = 2.0, HolsterTime = 0.20, DrawTime = 0.34,
                Spread = new SpreadSpec(0.0045, 0.0052, 0.052, 6.2, 0.022, 0.03, 0.28),
                Recoil = new RecoilSpec(0.72, 0.26, 0.030, 0.010, 0.42, 0.30, 0.05),
                Flash = new FlashSpec(0xffd9a0, 0xffc26a, 0.85, 26, 0.045),
                Tracer = new TracerSpec(3, Ice, 0.010, 0.075, 520),
                Shells = true, Accent = Cyan, VmFov = 55, VmFovAds = 41, AdsFov = 62,
                Hip = new PoseSpec(0.129, -0.089, -0.440, 0.012, -0.055, 0.020), AdsZ = -0.345,
                Sprint = new PoseSpec(0.085, -0.150, -0.300, -0.20, 0.66, 0.44), BobAmp = 0.024, BobRot = 0.030, ReloadSeat = 0.68,
                Muzzle = (0, 0.018, -0.432), Eject = (0.042, 0.036, -0.062), Sight = (0, 0.078, -0.030),
            },
            new WeaponConfig
            {
                Id = "magnat", Name = "MAGNAT .500", Kind = "POWER REVOLVER", Slot = 2, Mode = FireMode.Semi, Rpm = 172, Damage = 62, Range = 260, MagSize = 6, Reserve = 48,
                ReloadTime = 2.55, HolsterTime = 0.24, DrawTime = 0.40,
                Spread = new SpreadSpec(0.0022, 0.020, 0.075, 3.6, 0.026, 0.036, 0.18),
                Recoil = new RecoilSpec(2.55, 0.70, 0.075, 0.030, 1.35, 0.55, 0.30),
                Flash = new FlashSpec(0xffe0b0, 0xffb060, 1.45, 62, 0.062),
                Tracer = new TracerSpec(1, 0xffd08a, 0.008, 0.06, 720),
                Shells = false, Accent = Amber, VmFov = 55, VmFovAds = 40, AdsFov = 58,
                Hip = new PoseSpec(0.124, -0.068, -0.420, 0.022, -0.070, 0.030), AdsZ = -0.305,
                Sprint = new PoseSpec(0.090, -0.140, -0.290, -0.24, 0.72, 0.48), BobAmp = 0.030, BobRot = 0.036, ReloadSeat = 0.56,
                Muzzle = (0, 0.020, -0.366), Eject = (0, 0.014, -0.030), Sight = (0, 0.074, 0.014),
            },
            new WeaponConfig
            {
                Id = "voltline", Name = "VOLTLINE RP", Kind = "TECH RAILGUN", Slot = 3, Mode = FireMode.Charge, Rpm = 70, Damage = 155, Range = 420, MagSize = 5, Reserve = 20,
                ReloadTime = 2.4, HolsterTime = 0.26, DrawTime = 0.44, ChargeTime = 0.95, MinCharge = 0.22,
                Spread = new SpreadSpec(0.0008, 0.006, 0.026, 3.0, 0.012, 0.018, 0.15),
                Recoil = new RecoilSpec(2.05, 0.34, 0.095, 0.026, 1.05, 0.22, 0.42),
                Flash = new FlashSpec(0xffa8ff, 0xd070ff, 1.7, 90, 0.09),
                Tracer = new TracerSpec(1, Magenta, 0.045, 0.16, 0),
                Shells = false, Accent = Violet, VmFov = 55, VmFovAds = 39, AdsFov = 54,
                Hip = new PoseSpec(0.130, -0.074, -0.400, 0.014, -0.050, 0.018), AdsZ = -0.320,
                Sprint = new PoseSpec(0.092, -0.145, -0.270, -0.22, 0.68, 0.42), BobAmp = 0.028, BobRot = 0.032, ReloadSeat = 0.72,
                Muzzle = (0, 0.024, -0.536), Eject = (-0.070, 0.030, -0.030), Sight = (0, 0.104, -0.106),
            },
        };

        public static WeaponConfig Get(string id)
        {
            foreach (WeaponConfig w in All) if (w.Id == id) return w;
            throw new ArgumentOutOfRangeException(nameof(id), id, "no such gun");
        }
    }
}
