using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>How much work the bloom pass does.</summary>
    public enum BloomQuality
    {
        /// <summary>Quarter-resolution blur, 4 iterations.</summary>
        Fast,

        /// <summary>Half-resolution blur, 6 iterations, standard filtering.</summary>
        Standard,

        /// <summary>Half-resolution blur, 6 iterations, high-quality filtering.</summary>
        High,
    }

    /// <summary>What one quality preset changes. Medium is exactly the Milestone 1 slice.</summary>
    public sealed class QualityTier
    {
        public QualityPreset Preset { get; }

        /// <summary>Internal render height in pixels when the player has left Resolution on Auto.</summary>
        public int InternalHeight { get; }

        /// <summary>Multisample count of the 3D scene at the internal resolution: 1 (off), 2 or 4.</summary>
        public int MsaaSamples { get; }

        public BloomQuality Bloom { get; }

        /// <summary>Most rain streaks alive at once at full intensity.</summary>
        public int RainParticleCap { get; }

        /// <summary>Edge length of the reflection probe's cube faces.</summary>
        public int ProbeResolution { get; }

        /// <summary>How far from the camera a block's props and decals are still drawn, in metres. Props are small and the fog is thick, so they are not worth drawing far out.</summary>
        public double PropDistance { get; }

        /// <summary>Whether puffs of steam drift off the stalls and vents.</summary>
        public bool Steam { get; }

        /// <summary>How many people walk the streets (the prototype's budgets).</summary>
        public int Pedestrians { get; }

        /// <summary>People nearer than this are drawn as the full articulated rig; beyond <see cref="ActorFar"/> as a single box; between, a simple rigid body.</summary>
        public double ActorNear { get; }

        public double ActorFar { get; }

        /// <summary>How many vehicles drive the roads.</summary>
        public int GroundVehicles { get; }

        /// <summary>How many aircraft cross the sky.</summary>
        public int AircraftCount { get; }

        /// <summary>How many cars carry a real point light, so the wet road reacts to them.</summary>
        public int VehicleLights { get; }

        public QualityTier(QualityPreset preset, int internalHeight, int msaaSamples, BloomQuality bloom, int rainParticleCap, int probeResolution,
            double propDistance = 125, bool steam = true, int pedestrians = 110, double actorNear = 42, double actorFar = 92,
            int groundVehicles = 52, int aircraftCount = 20, int vehicleLights = 6)
        {
            if (pedestrians < 0 || pedestrians > QualityTiers.MaxPedestrians) throw new ArgumentOutOfRangeException(nameof(pedestrians), pedestrians, $"the crowd is 0 to {QualityTiers.MaxPedestrians}");
            if (groundVehicles < 0 || groundVehicles > QualityTiers.MaxGroundVehicles) throw new ArgumentOutOfRangeException(nameof(groundVehicles), groundVehicles, $"the road holds 0 to {QualityTiers.MaxGroundVehicles} vehicles");
            if (aircraftCount < 0 || aircraftCount > QualityTiers.MaxAircraft) throw new ArgumentOutOfRangeException(nameof(aircraftCount), aircraftCount, $"the sky holds 0 to {QualityTiers.MaxAircraft} aircraft");
            if (vehicleLights < 0 || vehicleLights > QualityTiers.MaxVehicleLights) throw new ArgumentOutOfRangeException(nameof(vehicleLights), vehicleLights, $"0 to {QualityTiers.MaxVehicleLights} vehicles carry a light");
            if (!(actorNear >= 10) || !(actorFar > actorNear) || double.IsInfinity(actorFar)) throw new ArgumentOutOfRangeException(nameof(actorFar), actorFar, "the detail distances must run from at least 10 m, near below far");
            if (!(propDistance >= 40) || double.IsInfinity(propDistance)) throw new ArgumentOutOfRangeException(nameof(propDistance), propDistance, "the prop distance must be at least 40 m");
            if (internalHeight < SettingsRanges.InternalHeightMin || internalHeight > SettingsRanges.InternalHeightMax) throw new ArgumentOutOfRangeException(nameof(internalHeight), internalHeight, "outside the allowed internal heights");
            if (msaaSamples != 1 && msaaSamples != 2 && msaaSamples != 4) throw new ArgumentOutOfRangeException(nameof(msaaSamples), msaaSamples, "MSAA must be 1, 2 or 4");
            if (rainParticleCap < 100) throw new ArgumentOutOfRangeException(nameof(rainParticleCap), rainParticleCap, "the rain cap must be at least 100");
            if (probeResolution < 16 || (probeResolution & (probeResolution - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(probeResolution), probeResolution, "the probe resolution must be a power of two of at least 16");
            Preset = preset;
            InternalHeight = internalHeight;
            MsaaSamples = msaaSamples;
            Bloom = bloom;
            RainParticleCap = rainParticleCap;
            ProbeResolution = probeResolution;
            PropDistance = propDistance;
            Steam = steam;
            Pedestrians = pedestrians;
            ActorNear = actorNear;
            ActorFar = actorFar;
            GroundVehicles = groundVehicles;
            AircraftCount = aircraftCount;
            VehicleLights = vehicleLights;
        }
    }

    /// <summary>
    /// The five presets, cheapest first. The chunky 540p look is kept from Medium up; the dearer tiers spend their
    /// budget on anti-aliasing, bloom quality, reflection detail and rain density instead of resolution, and on how far out the
    /// street props are drawn, and on how many people walk the streets and how far out they are drawn in full (the prototype's budgets, to be trimmed by the
    /// measurements at Gate B), and on how many vehicles and aircraft move through the city and how many of those carry a light.
    /// </summary>
    public static class QualityTiers
    {
        /// <summary>The most people any tier has: the size of the crowd's pool, which is built once for the top tier.</summary>
        public const int MaxPedestrians = 400;

        /// <summary>The most vehicles, aircraft and vehicle lights any tier has: the sizes of the traffic's fleet, built once for the top tier.</summary>
        public const int MaxGroundVehicles = 96, MaxAircraft = 36, MaxVehicleLights = 6;

        static readonly QualityTier[] Table =
        {
            new QualityTier(QualityPreset.Performance, 360, 1, BloomQuality.Fast, 1000, 64, propDistance: 70, steam: false, pedestrians: 36, actorNear: 25, actorFar: 55, groundVehicles: 18, aircraftCount: 5, vehicleLights: 2),
            new QualityTier(QualityPreset.Low, 450, 1, BloomQuality.Standard, 2000, 128, propDistance: 95, pedestrians: 60, groundVehicles: 30, aircraftCount: 12),
            new QualityTier(QualityPreset.Medium, 540, 1, BloomQuality.Standard, 3000, 128, propDistance: 125, pedestrians: 110, groundVehicles: 52, aircraftCount: 20),
            new QualityTier(QualityPreset.High, 540, 2, BloomQuality.High, 3000, 256, propDistance: 160, pedestrians: 320, groundVehicles: 74, aircraftCount: 28),
            new QualityTier(QualityPreset.Ultra, 540, 4, BloomQuality.High, 4500, 256, propDistance: 210, pedestrians: 400, groundVehicles: 96, aircraftCount: 36),
        };

        public static IReadOnlyList<QualityTier> All => Table;

        public static QualityTier For(QualityPreset preset)
        {
            int i = (int)preset;
            if (i < 0 || i >= Table.Length || Table[i].Preset != preset) throw new ArgumentOutOfRangeException(nameof(preset), preset, "unknown quality preset");
            return Table[i];
        }

        /// <summary>The internal height to render at: the player's pinned value, or the preset's own when it is Auto.</summary>
        public static int EffectiveInternalHeight(QualityPreset preset, int pinnedHeight)
        {
            if (pinnedHeight == SettingsRanges.InternalHeightAuto) return For(preset).InternalHeight;
            if (pinnedHeight < SettingsRanges.InternalHeightMin || pinnedHeight > SettingsRanges.InternalHeightMax) throw new ArgumentOutOfRangeException(nameof(pinnedHeight), pinnedHeight, "not Auto and outside the allowed internal heights");
            return pinnedHeight;
        }
    }
}
