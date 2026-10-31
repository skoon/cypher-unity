using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>What a shot struck, as the game knows it.</summary>
    public enum ShotTarget { World, Prop, Person, Vehicle, Enemy }

    /// <summary>The look of an impact (the prototype's <c>SURFACE_FX</c> keys).</summary>
    public enum ImpactSurface { Concrete, Asphalt, Metal, Energy, Flesh }

    /// <summary>A surface's impact recipe: sparks, debris, smoke and the decal (colours as sRGB hex).</summary>
    public sealed class SurfaceFx
    {
        public int Sparks, Debris, Smoke;
        public uint SparkColor, DebrisColor, SmokeColor, DecalTint;
        public double SparkSize, SparkSpeed, SparkLife, DebrisSize, SmokeAlpha, DecalSize;
        public bool Decal;
    }

    /// <summary>A small 3-vector for the effects, in world space (the prototype's axes, as the whole port keeps them).</summary>
    public struct V3
    {
        public double X, Y, Z;

        public V3(double x, double y, double z)
        {
            X = x; Y = y; Z = z;
        }

        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double k) => new V3(a.X * k, a.Y * k, a.Z * k);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public V3 Normalized
        {
            get
            {
                double l = Length;
                return l > 0 ? this * (1 / l) : this;
            }
        }
    }

    public struct Spark
    {
        public V3 Pos, Vel;
        public double Life, Max, Size;
        public uint Color;
        /// <summary>Drawn size and alpha this frame.</summary>
        public double DrawSize, Alpha;
    }

    public struct Smoke
    {
        public V3 Pos, Vel;
        public double Life, Max, Size, Peak;
        public uint Color;
        public double DrawSize, Alpha;
    }

    public struct Debris
    {
        public V3 Pos, Vel, Spin;
        /// <summary>Orientation as a quaternion (x, y, z, w).</summary>
        public double QX, QY, QZ, QW;
        public double Life, Max, Size, Ground, DrawSize;
        public uint Color;
    }

    public struct Tracer
    {
        public V3 From, To;
        public double Life, Max, Width, Speed;
        public uint Color;
        /// <summary>This frame: the drawn segment and its width and brightness.</summary>
        public V3 Tail, Head;
        public double DrawWidth, Fade;
    }

    public struct Decal
    {
        public V3 Pos, Normal;
        public double Size, Roll;
        public uint Tint;
        public bool Live;
    }

    public struct Shell
    {
        public V3 Pos, Vel, Spin;
        public double QX, QY, QZ, QW;
        public double Life, Scale, Ground, DrawScale;
        public bool Resting;
    }

    public struct FlashLight
    {
        public V3 Pos;
        public uint Color;
        public double Life, Max, Peak, Range, Intensity;
    }

    /// <summary>
    /// The gunfire effects, a port of the prototype's <c>combat/Impacts.js</c>: fixed pools recycled in turn (128 decals, 384 sparks, 128 smoke puffs, 96 debris
    /// chunks, 16 tracers, 16 shell casings, 4 muzzle lights), the composite impact per surface, and each pool's motion. The prototype's casings were physics
    /// bodies; here they fly a simple arc, spin, bounce on the ground they were thrown over and lie still (the user's step-6 plan). Seeded, so a session repeats.
    /// The game draws the pools; nothing here allocates after construction.
    /// </summary>
    public sealed class ImpactSim
    {
        public const int MaxDecals = 128, MaxSparks = 384, MaxSmoke = 128, MaxDebris = 96, MaxTracers = 16, MaxShells = 16, MaxLights = 4;

        /// <summary>Shells: gravity, the bounce, how long they lie before they go.</summary>
        public const double ShellGravity = 9.81, ShellRestitution = 0.42, ShellFriction = 0.55, ShellLife = 5.0;

        public static readonly IReadOnlyDictionary<ImpactSurface, SurfaceFx> Fx = new Dictionary<ImpactSurface, SurfaceFx>
        {
            [ImpactSurface.Concrete] = new SurfaceFx
            {
                Sparks = 5, SparkColor = 0xffd9a0, SparkSize = 0.014, SparkSpeed = 3.4, SparkLife = 0.28, Debris = 5, DebrisColor = 0x8b8f97, DebrisSize = 0.022,
                Smoke = 3, SmokeColor = 0x8e8b86, SmokeAlpha = 0.30, Decal = true, DecalSize = 0.13, DecalTint = 0xb8bcc4,
            },
            [ImpactSurface.Asphalt] = new SurfaceFx
            {
                Sparks = 4, SparkColor = 0xffc978, SparkSize = 0.012, SparkSpeed = 3.0, SparkLife = 0.24, Debris = 4, DebrisColor = 0x3d4048, DebrisSize = 0.019,
                Smoke = 3, SmokeColor = 0x5a5a5e, SmokeAlpha = 0.26, Decal = true, DecalSize = 0.15, DecalTint = 0x6e727a,
            },
            [ImpactSurface.Metal] = new SurfaceFx
            {
                Sparks = 16, SparkColor = 0xfff0c0, SparkSize = 0.016, SparkSpeed = 6.2, SparkLife = 0.42, Debris = 2, DebrisColor = 0xa8adb6, DebrisSize = 0.014,
                Smoke = 1, SmokeColor = 0x9aa0a8, SmokeAlpha = 0.18, Decal = true, DecalSize = 0.10, DecalTint = 0xe6ecf5,
            },
            [ImpactSurface.Energy] = new SurfaceFx
            {
                Sparks = 18, SparkColor = 0xff67c6, SparkSize = 0.020, SparkSpeed = 6.8, SparkLife = 0.5, Debris = 4, DebrisColor = 0x6b4a7a, DebrisSize = 0.02,
                Smoke = 4, SmokeColor = 0x6b5a80, SmokeAlpha = 0.34, Decal = true, DecalSize = 0.26, DecalTint = 0xff8ad4,
            },
            [ImpactSurface.Flesh] = new SurfaceFx
            {
                Sparks = 10, SparkColor = 0xff4d5e, SparkSize = 0.014, SparkSpeed = 3.2, SparkLife = 0.3, Debris = 3, DebrisColor = 0x6a1f24, DebrisSize = 0.012,
                Smoke = 2, SmokeColor = 0x8a2b33, SmokeAlpha = 0.28, Decal = false, DecalSize = 0.1, DecalTint = 0x000000,
            },
        };

        public readonly Decal[] Decals = new Decal[MaxDecals];
        public readonly Spark[] Sparks = new Spark[MaxSparks];
        public readonly Smoke[] SmokePuffs = new Smoke[MaxSmoke];
        public readonly Debris[] Chunks = new Debris[MaxDebris];
        public readonly Tracer[] Tracers = new Tracer[MaxTracers];
        public readonly Shell[] Shells = new Shell[MaxShells];
        public readonly FlashLight[] Lights = new FlashLight[MaxLights];

        int _decalNext, _sparkNext, _smokeNext, _debrisNext, _tracerNext, _shellNext, _lightNext;
        readonly Rng _rng;

        public ImpactSim(uint seed = 0x1AC7)
        {
            _rng = new Rng(seed);
        }

        double R() => _rng.Next();

        /// <summary>What a hit looks like (the prototype's choice): a person bleeds, the rail's shots are energy, props and cars ring as metal, a hit low on a flat surface is asphalt, the rest is concrete.</summary>
        public static ImpactSurface SurfaceFor(ShotTarget target, bool chargeGun, double normalY, double pointY)
        {
            if (target == ShotTarget.Person || target == ShotTarget.Enemy) return ImpactSurface.Flesh;
            if (chargeGun) return ImpactSurface.Energy;
            if (target == ShotTarget.Prop || target == ShotTarget.Vehicle) return ImpactSurface.Metal;
            return normalY > 0.6 && pointY < 0.8 ? ImpactSurface.Asphalt : ImpactSurface.Concrete;
        }

        /// <summary>A random unit vector, by rejection from the cube as the prototype draws it.</summary>
        V3 RandUnit()
        {
            double x, y, z, d;
            do
            {
                x = R() * 2 - 1;
                y = R() * 2 - 1;
                z = R() * 2 - 1;
                d = x * x + y * y + z * z;
            }
            while (d > 1 || d < 1e-4);
            return new V3(x, y, z) * (1 / Math.Sqrt(d));
        }

        public void AddDecal(V3 point, V3 normal, double size, uint tint)
        {
            ref Decal d = ref Decals[_decalNext];
            _decalNext = (_decalNext + 1) % MaxDecals;
            V3 n = normal.Normalized;
            d.Pos = point + n * 0.009;
            d.Normal = n;
            d.Size = size;
            d.Roll = R() * Math.PI * 2;
            d.Tint = tint;
            d.Live = true;
        }

        public void SpawnSpark(V3 pos, V3 vel, uint color, double size, double life)
        {
            ref Spark p = ref Sparks[_sparkNext];
            _sparkNext = (_sparkNext + 1) % MaxSparks;
            p.Pos = pos; p.Vel = vel; p.Life = life; p.Max = life; p.Size = size; p.Color = color;
        }

        public void SpawnSmoke(V3 pos, V3 vel, uint color, double size, double life, double alpha = 0.35)
        {
            ref Smoke p = ref SmokePuffs[_smokeNext];
            _smokeNext = (_smokeNext + 1) % MaxSmoke;
            p.Pos = pos; p.Vel = vel; p.Life = life; p.Max = life; p.Size = size; p.Peak = alpha; p.Color = color;
        }

        public void SpawnDebris(V3 pos, V3 vel, uint color, double size, double life, double ground)
        {
            ref Debris d = ref Chunks[_debrisNext];
            _debrisNext = (_debrisNext + 1) % MaxDebris;
            d.Pos = pos; d.Vel = vel; d.Life = life; d.Max = life; d.Size = size; d.Color = color; d.Ground = ground;
            d.Spin = new V3((R() - 0.5) * 26, (R() - 0.5) * 26, (R() - 0.5) * 26);
            double qx = R(), qy = R(), qz = R(), qw = R(), l = Math.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            d.QX = qx / l; d.QY = qy / l; d.QZ = qz / l; d.QW = qw / l;
        }

        /// <param name="speed">How fast the head travels, m/s; 0 is an instant full beam.</param>
        public void SpawnTracer(V3 from, V3 to, uint color, double width, double life, double speed)
        {
            ref Tracer t = ref Tracers[_tracerNext];
            _tracerNext = (_tracerNext + 1) % MaxTracers;
            t.From = from; t.To = to; t.Color = color; t.Width = width; t.Life = life; t.Max = life; t.Speed = speed;
        }

        /// <summary>Throw a case from the ejection port; <paramref name="ground"/> is the height of the ground under it.</summary>
        public void EjectShell(V3 pos, V3 vel, double scale, double ground)
        {
            ref Shell s = ref Shells[_shellNext];
            _shellNext = (_shellNext + 1) % MaxShells;
            s.Pos = pos; s.Vel = vel; s.Scale = scale; s.Ground = ground; s.Life = ShellLife; s.Resting = false;
            s.Spin = new V3((R() - 0.5) * 44, (R() - 0.5) * 30, (R() - 0.5) * 44);
            s.QX = 0; s.QY = 0; s.QZ = 0; s.QW = 1;
        }

        public void Flash(V3 pos, uint color, double intensity, double life, double range)
        {
            ref FlashLight e = ref Lights[_lightNext];
            _lightNext = (_lightNext + 1) % MaxLights;
            e.Pos = pos; e.Color = color; e.Life = life; e.Max = life; e.Peak = intensity; e.Range = range; e.Intensity = intensity;
        }

        /// <summary>The composite impact: sparks in a cone round the normal, debris thrown up, smoke, and a decal.</summary>
        public void Impact(V3 point, V3 normal, ImpactSurface surface, double energy, double ground)
        {
            SurfaceFx s = Fx[surface];
            V3 n = normal.Normalized;
            int sparks = (int)RpgJs.Round(s.Sparks * (0.55 + energy));
            for (int i = 0; i < sparks; i++)
            {
                V3 dir = (n + RandUnit() * (0.85 + R() * 0.5)).Normalized * (s.SparkSpeed * (0.35 + R() * 1.15) * (0.6 + energy));
                SpawnSpark(point, dir, s.SparkColor, s.SparkSize * (0.6 + R() * 0.8), s.SparkLife * (0.5 + R()));
            }
            int debris = (int)RpgJs.Round(s.Debris * (0.4 + energy));
            for (int i = 0; i < debris; i++)
            {
                V3 dir = (n + RandUnit() * 0.7).Normalized * (1.6 + R() * 3.4 * (0.5 + energy));
                dir.Y += 1.2;
                SpawnDebris(point, dir, s.DebrisColor, s.DebrisSize * (0.5 + R()), 1.1 + R() * 0.9, ground);
            }
            for (int i = 0; i < s.Smoke; i++)
            {
                V3 dir = n * (0.5 + R() * 0.8);
                dir.X += (R() - 0.5) * 0.5;
                dir.Y += 0.35 + R() * 0.4;
                dir.Z += (R() - 0.5) * 0.5;
                SpawnSmoke(point, dir, s.SmokeColor, 0.06 + R() * 0.07, 0.6 + R() * 0.5, s.SmokeAlpha);
            }
            if (s.Decal) AddDecal(point, n, s.DecalSize * (0.8 + energy * 0.55), s.DecalTint);
        }

        /// <summary>A random number from the effects' stream, for the game's small choices (the smoke at the muzzle).</summary>
        public double Random() => R();

        /// <summary>Move everything on by <paramref name="dt"/> seconds.</summary>
        public void Update(double dt)
        {
            for (int i = 0; i < MaxSparks; i++)
            {
                ref Spark p = ref Sparks[i];
                if (p.Life <= 0) continue;
                p.Life -= dt;
                if (p.Life <= 0)
                {
                    p.Alpha = 0;
                    p.DrawSize = 0;
                    continue;
                }
                p.Vel.Y += -16 * dt;
                p.Vel = p.Vel * Math.Max(0, 1 - 2.4 * dt);
                p.Pos = p.Pos + p.Vel * dt;
                double t = p.Life / p.Max;
                p.DrawSize = p.Size * (0.35 + t * 0.65);
                p.Alpha = t * t;
            }

            for (int i = 0; i < MaxSmoke; i++)
            {
                ref Smoke p = ref SmokePuffs[i];
                if (p.Life <= 0) continue;
                p.Life -= dt;
                if (p.Life <= 0)
                {
                    p.Alpha = 0;
                    p.DrawSize = 0;
                    continue;
                }
                p.Vel = p.Vel * Math.Max(0, 1 - 1.9 * dt);
                p.Vel.Y += 0.55 * dt;
                p.Pos = p.Pos + p.Vel * dt;
                double t = 1 - p.Life / p.Max;
                p.DrawSize = p.Size * (1 + t * 2.6);
                p.Alpha = p.Peak * (1 - t) * (1 - t);
            }

            for (int i = 0; i < MaxDebris; i++)
            {
                ref Debris d = ref Chunks[i];
                if (d.Life <= 0) continue;
                d.Life -= dt;
                if (d.Life <= 0)
                {
                    d.DrawSize = 0;
                    continue;
                }
                d.Vel.Y -= 19 * dt;
                d.Vel = d.Vel * Math.Max(0, 1 - 0.9 * dt);
                d.Pos = d.Pos + d.Vel * dt;
                // the prototype bounces chunks 0.24 m up (its kerb top); here, on the ground they were thrown over
                if (d.Pos.Y < d.Ground && d.Vel.Y < 0)
                {
                    d.Pos.Y = d.Ground;
                    d.Vel.Y *= -0.32;
                    d.Vel = d.Vel * 0.6;
                }
                Turn(ref d.QX, ref d.QY, ref d.QZ, ref d.QW, d.Spin, dt);
                d.DrawSize = d.Size * Math.Min(1, d.Life / (d.Max * 0.35));
            }

            for (int i = 0; i < MaxTracers; i++)
            {
                ref Tracer t = ref Tracers[i];
                if (t.Life <= 0) continue;
                t.Life -= dt;
                if (t.Life <= 0)
                {
                    t.DrawWidth = 0;
                    continue;
                }
                double age = t.Max - t.Life;
                V3 span = t.To - t.From;
                double full = span.Length, head = full, tail = 0;
                if (t.Speed > 0)
                {
                    head = Math.Min(full, age * t.Speed);
                    tail = Math.Max(0, head - t.Speed * 0.045);
                }
                V3 dir = span.Normalized;
                t.Tail = t.From + dir * tail;
                t.Head = t.From + dir * Math.Max(tail + 0.001, head);
                t.Fade = Math.Min(1, t.Life / (t.Max * 0.6));
                t.DrawWidth = t.Width * t.Fade;
            }

            for (int i = 0; i < MaxShells; i++)
            {
                ref Shell s = ref Shells[i];
                if (s.Life <= 0) continue;
                s.Life -= dt;
                if (s.Life <= 0)
                {
                    s.DrawScale = 0;
                    continue;
                }
                if (!s.Resting)
                {
                    s.Vel.Y -= ShellGravity * dt;
                    s.Vel = s.Vel * Math.Max(0, 1 - 0.12 * dt);
                    s.Pos = s.Pos + s.Vel * dt;
                    s.Spin = s.Spin * Math.Max(0, 1 - 0.06 * dt);
                    const double radius = 0.0052;
                    if (s.Pos.Y < s.Ground + radius && s.Vel.Y < 0)
                    {
                        s.Pos.Y = s.Ground + radius;
                        s.Vel.Y = -s.Vel.Y * ShellRestitution;
                        s.Vel.X *= 1 - ShellFriction * 0.5;
                        s.Vel.Z *= 1 - ShellFriction * 0.5;
                        s.Spin = s.Spin * 0.5;
                        if (Math.Abs(s.Vel.Y) < 0.25)
                        {
                            s.Resting = true;
                            // a case comes to rest on its side
                            s.QX = 0; s.QY = 0; s.QZ = 0; s.QW = 1;
                        }
                    }
                    if (!s.Resting) Turn(ref s.QX, ref s.QY, ref s.QZ, ref s.QW, s.Spin, dt);
                }
                s.DrawScale = s.Scale * Math.Min(1, s.Life / 0.5);
            }

            for (int i = 0; i < MaxLights; i++)
            {
                ref FlashLight e = ref Lights[i];
                if (e.Life <= 0) continue;
                e.Life -= dt;
                if (e.Life <= 0)
                {
                    e.Intensity = 0;
                    continue;
                }
                double t = e.Life / e.Max;
                e.Intensity = e.Peak * t * t;
            }
        }

        /// <summary>Turn a quaternion by an angular velocity for a step (the prototype's <c>rot.multiply(axis-angle)</c>).</summary>
        static void Turn(ref double qx, ref double qy, ref double qz, ref double qw, V3 spin, double dt)
        {
            double len = spin.Length;
            if (len <= 0) return;
            double half = len * dt / 2, s = Math.Sin(half) / len;
            double bx = spin.X * s, by = spin.Y * s, bz = spin.Z * s, bw = Math.Cos(half);
            double x = qx * bw + qw * bx + qy * bz - qz * by;
            double y = qy * bw + qw * by + qz * bx - qx * bz;
            double z = qz * bw + qw * bz + qx * by - qy * bx;
            double w = qw * bw - qx * bx - qy * by - qz * bz;
            double l = Math.Sqrt(x * x + y * y + z * z + w * w);
            qx = x / l; qy = y / l; qz = z / l; qw = w / l;
        }

        /// <summary>How many of each pool are alive now (for tests and the perf overlay).</summary>
        public (int Decals, int Sparks, int Smoke, int Debris, int Tracers, int Shells, int Lights) Live()
        {
            int dc = 0, sp = 0, sm = 0, db = 0, tr = 0, sh = 0, li = 0;
            foreach (Decal d in Decals) if (d.Live) dc++;
            foreach (Spark p in Sparks) if (p.Life > 0) sp++;
            foreach (Smoke p in SmokePuffs) if (p.Life > 0) sm++;
            foreach (Debris d in Chunks) if (d.Life > 0) db++;
            foreach (Tracer t in Tracers) if (t.Life > 0) tr++;
            foreach (Shell s in Shells) if (s.Life > 0) sh++;
            foreach (FlashLight e in Lights) if (e.Life > 0) li++;
            return (dc, sp, sm, db, tr, sh, li);
        }

        /// <summary>Put everything out and wipe the holes: a new game starts on a clean city.</summary>
        public void Clear()
        {
            Array.Clear(Decals, 0, Decals.Length);
            Array.Clear(Sparks, 0, Sparks.Length);
            Array.Clear(SmokePuffs, 0, SmokePuffs.Length);
            Array.Clear(Chunks, 0, Chunks.Length);
            Array.Clear(Tracers, 0, Tracers.Length);
            Array.Clear(Shells, 0, Shells.Length);
            Array.Clear(Lights, 0, Lights.Length);
            _decalNext = _sparkNext = _smokeNext = _debrisNext = _tracerNext = _shellNext = _lightNext = 0;
        }
    }
}
