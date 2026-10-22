using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>One placed prop: which kit, where its base is, how it is turned about Y and its uniform scale.</summary>
    public readonly struct PropInstance
    {
        public readonly string Kit;
        public readonly double X, Y, Z, Ry, Scale;

        public PropInstance(string kit, double x, double y, double z, double ry, double scale)
        {
            Kit = kit; X = x; Y = y; Z = z; Ry = ry; Scale = scale;
        }
    }

    /// <summary>A flat decal: a quad of size (Sx, Sy) showing one tile of the atlas, on a wall (facing along <see cref="Ry"/>) or lying on the ground.</summary>
    public readonly struct DecalInstance
    {
        public readonly DecalKind Kind;
        public readonly double X, Y, Z, Sx, Sy, Ry, Rz;
        public readonly int Tile;

        public DecalInstance(DecalKind kind, double x, double y, double z, double sx, double sy, double ry, double rz, int tile)
        {
            Kind = kind; X = x; Y = y; Z = z; Sx = sx; Sy = sy; Ry = ry; Rz = rz; Tile = tile;
        }
    }

    /// <summary>A static box collider turned about Y: centre, half extents, and the yaw (the same convention as a prop's <c>Ry</c>).</summary>
    public readonly struct PropBox
    {
        public readonly double Cx, Cy, Cz, Hx, Hy, Hz, Yaw;

        public PropBox(double cx, double cy, double cz, double hx, double hy, double hz, double yaw)
        {
            Cx = cx; Cy = cy; Cz = cz; Hx = hx; Hy = hy; Hz = hz; Yaw = yaw;
        }
    }

    /// <summary>A place steam rises from: a street stall or a roof vent.</summary>
    public readonly struct SteamSpot
    {
        public readonly double X, Y, Z, R;

        public SteamSpot(double x, double y, double z, double r)
        {
            X = x; Y = y; Z = z; R = r;
        }
    }

    /// <summary>
    /// Everything the prop system decided for a window of the city: the props, wall and ground decals, colliders, steam spots, and the
    /// cables and conduit (already geometry, one builder per grid cell, because they are not kits).
    /// </summary>
    public sealed class PropLayout
    {
        public readonly List<PropInstance> Instances = new List<PropInstance>();
        public readonly List<DecalInstance> Decals = new List<DecalInstance>();
        public readonly List<PropBox> Boxes = new List<PropBox>();
        public readonly List<SteamSpot> SteamSpots = new List<SteamSpot>();

        /// <summary>Cables and conduit as geometry, by grid cell, in the props' vertex format.</summary>
        public readonly Dictionary<(int Gx, int Gz), PropMeshBuilder> CellParts = new Dictionary<(int, int), PropMeshBuilder>();

        /// <summary>The puffs of steam, chosen from <see cref="SteamSpots"/>.</summary>
        public readonly List<SteamPuff> Puffs = new List<SteamPuff>();
    }

    /// <summary>One drifting puff of steam: where it starts, how fast it cycles, how big it grows, how far it rises and sideways.</summary>
    public readonly struct SteamPuff
    {
        public readonly double X, Y, Z, Phase, Speed, Size, Rise, Drift;

        public SteamPuff(double x, double y, double z, double phase, double speed, double size, double rise, double drift)
        {
            X = x; Y = y; Z = z; Phase = phase; Speed = speed; Size = size; Rise = rise; Drift = drift;
        }
    }
}
