using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>One place to stand to look at a sign face-on: the thing looked at, which way it faces, and how far out to stand.</summary>
    public readonly struct SignShot
    {
        public readonly string Name;
        public readonly double X, Y, Z;
        public readonly double Nx, Nz;
        public readonly double Distance;

        public SignShot(string name, double x, double y, double z, double nx, double nz, double distance)
        {
            Name = name;
            X = x; Y = y; Z = z;
            Nx = nx; Nz = nz;
            Distance = distance;
        }

        /// <summary>Where the camera goes: out along the facing at street level (never above the sign).</summary>
        public (double X, double Y, double Z) CameraPosition(double eyeHeight) => (X + Nx * Distance, Math.Min(Y, eyeHeight), Z + Nz * Distance);
    }

    /// <summary>
    /// Picks the signs worth a screenshot: the hoarding, hologram, shop sign, plaque and blade nearest the plaza, each with a distance to
    /// stand at that keeps the camera in open air. Used by the capture run so the new signage is looked at in the same places every time.
    /// </summary>
    public static class SignVantage
    {
        /// <param name="isBlocked">Whether a point is inside a lot (CityPlan.IsBlocked); the standing distance is cut short before it gets there.</param>
        public static IReadOnlyList<SignShot> Pick(NeonLayout layout, NeonAtlasLayout atlas, Func<double, double, bool> isBlocked)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (atlas == null) throw new ArgumentNullException(nameof(atlas));
            if (isBlocked == null) throw new ArgumentNullException(nameof(isBlocked));

            var shots = new List<SignShot>();
            Nearest(shots, "sign_board", layout.Boards, 24, isBlocked);
            Nearest(shots, "sign_board_close", layout.Boards, 11, isBlocked);
            Nearest(shots, "sign_hologram", layout.Holograms, 34, isBlocked);

            AddPanel(shots, "sign_shop", layout, atlas, SignGroup.Word, q => q.Width >= 3, 5.5, isBlocked);
            AddPanel(shots, "sign_plaque", layout, atlas, SignGroup.Kanji, q => q.Width >= 1.2, 3.2, isBlocked);
            AddPanel(shots, "sign_blade", layout, atlas, SignGroup.Vertical, q => q.Height >= 4, 6, isBlocked);
            return shots;
        }

        static void Nearest(List<SignShot> shots, string name, List<NeonBoard> boards, double distance, Func<double, double, bool> isBlocked)
        {
            bool found = false;
            NeonBoard best = default;
            double bestD = double.PositiveInfinity;
            foreach (NeonBoard b in boards)
            {
                double d = b.X * b.X + b.Z * b.Z;
                if (d < bestD) { bestD = d; best = b; found = true; }
            }
            if (found) shots.Add(Shot(name, best.X, best.Y, best.Z, best.Nx, best.Nz, distance, isBlocked));
        }

        static void AddPanel(List<SignShot> shots, string name, NeonLayout layout, NeonAtlasLayout atlas, SignGroup group, Func<NeonQuad, bool> suits, double distance,
            Func<double, double, bool> isBlocked)
        {
            bool found = false;
            NeonQuad best = default;
            double bestD = double.PositiveInfinity;
            foreach (NeonQuad q in layout.Quads)
            {
                if (q.Shape != NeonShape.Panel || q.Cell == null || atlas[q.Cell].Group != group || !suits(q)) continue;
                double d = q.X * q.X + q.Z * q.Z;
                if (d < bestD) { bestD = d; best = q; found = true; }
            }
            if (found) shots.Add(Shot(name, best.X, best.Y, best.Z, best.Nx, best.Nz, distance, isBlocked));
        }

        static SignShot Shot(string name, double x, double y, double z, double nx, double nz, double distance, Func<double, double, bool> isBlocked)
        {
            double len = Math.Sqrt(nx * nx + nz * nz);
            if (len < 1e-9) throw new ArgumentException($"{name} faces nowhere");
            nx /= len; nz /= len;

            // stand as far out as asked, or as far as the air goes
            double reach = 0;
            for (double d = 1; d <= distance; d += 1)
            {
                if (isBlocked(x + nx * d, z + nz * d)) break;
                reach = d;
            }
            return new SignShot(name, x, y, z, nx, nz, Math.Max(2, reach));
        }
    }
}
