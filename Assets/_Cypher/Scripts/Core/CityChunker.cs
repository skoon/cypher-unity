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

        /// <summary>The LED hoardings (see <see cref="NeonCellMeshes.Boards"/>).</summary>
        public MeshData Boards = new MeshData();

        /// <summary>The floating holograms (see <see cref="NeonCellMeshes.Holos"/>).</summary>
        public MeshData Holos = new MeshData();

        public readonly List<Aabb> Colliders = new List<Aabb>();
        public int BuildingCount;

        /// <summary>World Y of the highest point in the chunk (sidewalk deck or a building roof).</summary>
        public double MaxY;

        public int TriangleCount => Walls.TriangleCount + Roofs.TriangleCount + Sidewalk.TriangleCount + Neon.TriangleCount + Housings.TriangleCount + Boards.TriangleCount + Holos.TriangleCount;
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
            var job = new CityChunkJob(plan, window);
            var slice = new CitySlice { Plan = plan, Window = window };
            foreach (StreetCell cell in job.Cells) slice.Chunks.Add(job.BuildChunk(cell));
            slice.Markings = job.BuildMarkings();
            if (includeNeon) job.AttachNeon(slice.Chunks, job.MeshNeon(job.PlanNeon()));
            return slice;
        }

        internal static CityChunk BuildChunk(StreetCell cell, List<Building> buildings)
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

    /// <summary>
    /// The same work as <see cref="CityChunker.Build"/>, one piece at a time, so a caller can spread it over frames and report
    /// progress. Pieces are independent apart from neon, which is planned for the whole window in one pass (one random
    /// stream, consumed wall by wall) and then attached to the chunks.
    /// </summary>
    public sealed class CityChunkJob
    {
        readonly CityWindow _window;
        readonly Dictionary<(int, int), List<Building>> _buildingsByCell = new Dictionary<(int, int), List<Building>>();

        public CityPlan Plan { get; }
        public CityWindow Window => _window;

        /// <summary>The cells inside the window, in the plan's order.</summary>
        public IReadOnlyList<StreetCell> Cells { get; }

        public CityChunkJob(CityPlan plan, CityWindow window)
        {
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _window = window;

            foreach (Building b in plan.Buildings)
            {
                (int, int) key = (b.Lot.Gx, b.Lot.Gz);
                if (!_buildingsByCell.TryGetValue(key, out List<Building> list))
                {
                    list = new List<Building>();
                    _buildingsByCell.Add(key, list);
                }
                list.Add(b);
            }

            var cells = new List<StreetCell>();
            foreach (StreetCell cell in plan.Streets)
            {
                if (window.Contains(cell.Gx, cell.Gz)) cells.Add(cell);
            }
            Cells = cells;
        }

        public CityChunk BuildChunk(StreetCell cell)
        {
            if (cell == null) throw new ArgumentNullException(nameof(cell));
            if (!_window.Contains(cell.Gx, cell.Gz)) throw new ArgumentOutOfRangeException(nameof(cell), $"cell ({cell.Gx},{cell.Gz}) is outside the window");
            return CityChunker.BuildChunk(cell, _buildingsByCell.TryGetValue((cell.Gx, cell.Gz), out List<Building> bs) ? bs : null);
        }

        public MeshData BuildMarkings()
        {
            var paint = new MeshBuilder();
            StreetMesher.AddMarkings(paint, _window);
            return paint.Build();
        }

        /// <param name="atlas">Where the sign lettering is; defaults to the standard atlas.</param>
        public NeonLayout PlanNeon(NeonAtlasLayout atlas = null) => NeonPlanner.Plan(Plan, _window, NeonPlanner.DefaultSeed, atlas);

        public Dictionary<(int Gx, int Gz), NeonCellMeshes> MeshNeon(NeonLayout layout, NeonAtlasLayout atlas = null) => NeonMesher.Build(layout, atlas);

        public void AttachNeon(IEnumerable<CityChunk> chunks, Dictionary<(int Gx, int Gz), NeonCellMeshes> neon)
        {
            foreach (CityChunk chunk in chunks)
            {
                if (!neon.TryGetValue((chunk.Gx, chunk.Gz), out NeonCellMeshes meshes)) continue;
                chunk.Neon = meshes.Emissive;
                chunk.Housings = meshes.Housing;
                chunk.Boards = meshes.Boards;
                chunk.Holos = meshes.Holos;
            }
        }
    }
}
