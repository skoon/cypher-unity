using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>Axis-aligned box in world space, used for static colliders.</summary>
    public readonly struct Aabb
    {
        public readonly double X0, Y0, Z0, X1, Y1, Z1;

        public Aabb(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            X0 = x0; Y0 = y0; Z0 = z0; X1 = x1; Y1 = y1; Z1 = z1;
        }

        public double CentreX => (X0 + X1) / 2;
        public double CentreY => (Y0 + Y1) / 2;
        public double CentreZ => (Z0 + Z1) / 2;
        public double SizeX => X1 - X0;
        public double SizeY => Y1 - Y0;
        public double SizeZ => Z1 - Z0;
    }

    /// <summary>
    /// Everything built for one 62 m grid cell. Cell-sized chunks are the unit of
    /// frustum culling, which the web prototype found to be the biggest geometry lever.
    /// </summary>
    public sealed class CityChunk
    {
        public int Gx, Gz;
        public MeshData Walls = new MeshData();
        public MeshData Roofs = new MeshData();
        public MeshData Sidewalk = new MeshData();

        /// <summary>Additive emissive signage (see <see cref="NeonMesher"/>); empty when neon is off.</summary>
        public MeshData Neon = new MeshData();

        /// <summary>Dark metal housings behind the signage.</summary>
        public MeshData Housings = new MeshData();

        public readonly List<Aabb> Colliders = new List<Aabb>();
        public int BuildingCount;

        /// <summary>World Y of the highest point in the chunk (sidewalk deck or a building roof).</summary>
        public double MaxY;

        public int TriangleCount => Walls.TriangleCount + Roofs.TriangleCount + Sidewalk.TriangleCount + Neon.TriangleCount + Housings.TriangleCount;
    }

    /// <summary>The meshes for a window of the city: per-cell chunks plus road paint.</summary>
    public sealed class CitySlice
    {
        public CityPlan Plan;
        public CityWindow Window;
        public readonly List<CityChunk> Chunks = new List<CityChunk>();
        public MeshData Markings = new MeshData();

        public int TriangleCount
        {
            get
            {
                int t = Markings.TriangleCount;
                foreach (CityChunk c in Chunks) t += c.TriangleCount;
                return t;
            }
        }
    }

    /// <summary>
    /// Turns a CityPlan into per-cell mesh data for a window of cells. Pure: the Unity
    /// layer only converts the result to Meshes and attaches colliders.
    /// </summary>
    public static class CityChunker
    {
        /// <summary>
        /// Buildings stand on the sidewalk deck, so their geometry is lifted by the kerb
        /// height (the prototype adds SIDEWALK_H to every volume).
        /// </summary>
        public const double BuildingBaseY = CityLayout.SidewalkHeight;

        public static CitySlice Build(CityPlan plan, CityWindow window, bool includeNeon = true)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            var slice = new CitySlice { Plan = plan, Window = window };

            var buildingsByCell = new Dictionary<(int, int), List<Building>>();
            foreach (Building b in plan.Buildings)
            {
                (int, int) key = (b.Lot.Gx, b.Lot.Gz);
                if (!buildingsByCell.TryGetValue(key, out List<Building> list))
                {
                    list = new List<Building>();
                    buildingsByCell.Add(key, list);
                }
                list.Add(b);
            }

            foreach (StreetCell cell in plan.Streets)
            {
                if (!window.Contains(cell.Gx, cell.Gz)) continue;
                slice.Chunks.Add(BuildChunk(cell, buildingsByCell.TryGetValue((cell.Gx, cell.Gz), out List<Building> bs) ? bs : null));
            }

            var paint = new MeshBuilder();
            StreetMesher.AddMarkings(paint, window);
            slice.Markings = paint.Build();

            if (includeNeon)
            {
                var neon = NeonMesher.Build(NeonPlanner.Plan(plan, window));
                foreach (CityChunk chunk in slice.Chunks)
                {
                    if (!neon.TryGetValue((chunk.Gx, chunk.Gz), out NeonCellMeshes meshes)) continue;
                    chunk.Neon = meshes.Emissive;
                    chunk.Housings = meshes.Housing;
                }
            }
            return slice;
        }

        static CityChunk BuildChunk(StreetCell cell, List<Building> buildings)
        {
            var chunk = new CityChunk { Gx = cell.Gx, Gz = cell.Gz };

            var sidewalk = new MeshBuilder();
            StreetMesher.AddSidewalk(sidewalk, cell);
            chunk.Sidewalk = sidewalk.Build();
            chunk.MaxY = CityLayout.SidewalkHeight;

            double hw = StreetMesher.SidewalkHalfWidth;
            chunk.Colliders.Add(new Aabb(cell.Cx - hw, 0, cell.Cz - hw, cell.Cx + hw, CityLayout.SidewalkHeight, cell.Cz + hw));

            if (buildings == null) return chunk;

            var walls = new MeshBuilder();
            var roofs = new MeshBuilder();
            var mesher = new BuildingMesher(walls, roofs);
            foreach (Building b in buildings)
            {
                mesher.AddBuilding(b);
                chunk.BuildingCount++;
                foreach (Volume v in b.Volumes)
                {
                    Rect2 r = Bounds(v.Poly);
                    chunk.Colliders.Add(new Aabb(r.X0, v.Y0 + BuildingBaseY, r.Z0, r.X1, v.Y1 + BuildingBaseY, r.Z1));
                    chunk.MaxY = Math.Max(chunk.MaxY, v.Y1 + BuildingBaseY);
                }
            }
            chunk.Walls = walls.Build().Translated(0, BuildingBaseY, 0);
            chunk.Roofs = roofs.Build().Translated(0, BuildingBaseY, 0);
            return chunk;
        }

        static Rect2 Bounds(Point2[] poly)
        {
            double x0 = double.PositiveInfinity, z0 = double.PositiveInfinity;
            double x1 = double.NegativeInfinity, z1 = double.NegativeInfinity;
            foreach (Point2 p in poly)
            {
                x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X);
                z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z);
            }
            return new Rect2(x0, z0, x1, z1);
        }
    }
}
