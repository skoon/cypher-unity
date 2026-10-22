using System;
using System.Collections.Generic;

namespace Cypher.Core
{
    /// <summary>The props and decals of one grid cell, baked into one mesh each, plus the colliders to go with them.</summary>
    public sealed class PropCell
    {
        public int Gx, Gz;

        /// <summary>Every prop of the cell and its cables and conduit, in the props' vertex format.</summary>
        public MeshData Props = new MeshData();

        /// <summary>Wall and ground decals, UVs pointing into <see cref="PropTextures.DecalAtlas"/>.</summary>
        public MeshData Decals = new MeshData();

        public readonly List<PropBox> Boxes = new List<PropBox>();
        public int InstanceCount, DecalCount;

        /// <summary>World Y of the highest vertex of the props (a streetlight, an aerial on a tower).</summary>
        public double MaxY;

        public int TriangleCount => Props.TriangleCount + Decals.TriangleCount;
    }

    /// <summary>
    /// Bakes a <see cref="PropLayout"/> into per-cell meshes, so that a cell's props are two draws (one for the props, one for the decals) and
    /// the engine culls them a cell at a time, the way it culls the buildings. Every prop kit shares one vertex format and one material.
    /// </summary>
    public static class PropCellMesher
    {
        /// <summary>Walls' graffiti glows a little (the prototype's emissive 0.55); ground decals do not.</summary>
        public const double GraffitiEmit = 0.55;

        public const double DecalRough = 0.85;

        public static Dictionary<(int Gx, int Gz), PropCell> Build(PropLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));

            var props = new Dictionary<(int, int), PropMeshBuilder>();
            var decals = new Dictionary<(int, int), PropMeshBuilder>();
            var cells = new Dictionary<(int Gx, int Gz), PropCell>();

            PropCell CellOf(double x, double z)
            {
                var key = (Gx: Clamp(CityLayout.CellIndex(x)), Gz: Clamp(CityLayout.CellIndex(z)));
                if (!cells.TryGetValue(key, out PropCell cell)) cells.Add(key, cell = new PropCell { Gx = key.Gx, Gz = key.Gz });
                return cell;
            }

            PropMeshBuilder BuilderFor(Dictionary<(int, int), PropMeshBuilder> map, PropCell cell)
            {
                var key = (cell.Gx, cell.Gz);
                if (!map.TryGetValue(key, out PropMeshBuilder b)) map.Add(key, b = new PropMeshBuilder());
                return b;
            }

            // cables first: copied (the layout stays as it was, so it can be baked again) into the builder the cell's props are added to
            foreach (var kv in layout.CellParts)
            {
                PropCell cell = CellOf(CityLayout.CellCentre(kv.Key.Gx), CityLayout.CellCentre(kv.Key.Gz));
                BuilderFor(props, cell).AddInstance(kv.Value.Build(), 0, 0, 0, 0);
            }

            foreach (PropInstance i in layout.Instances)
            {
                PropCell cell = CellOf(i.X, i.Z);
                PropKit kit = PropKits.Get(i.Kit);
                BuilderFor(props, cell).AddInstance(kit.Geometry, i.X, i.Y, i.Z, i.Ry, i.Scale);
                cell.InstanceCount++;
            }

            foreach (DecalInstance d in layout.Decals)
            {
                PropCell cell = CellOf(d.X, d.Z);
                AddDecal(BuilderFor(decals, cell), d);
                cell.DecalCount++;
            }

            foreach (PropBox b in layout.Boxes) CellOf(b.Cx, b.Cz).Boxes.Add(b);

            foreach (PropCell cell in cells.Values)
            {
                var key = (cell.Gx, cell.Gz);
                if (props.TryGetValue(key, out PropMeshBuilder pb)) cell.Props = pb.Build();
                if (decals.TryGetValue(key, out PropMeshBuilder db)) cell.Decals = db.Build();
                for (int k = 1; k < cell.Props.Positions.Length; k += 3) cell.MaxY = Math.Max(cell.MaxY, cell.Props.Positions[k]);
            }
            return cells;
        }

        /// <summary>One decal as a quad: a unit plane scaled to (Sx, Sy), spun about its own normal by Rz, laid down or stood up, turned by Ry, and moved.</summary>
        static void AddDecal(PropMeshBuilder mb, DecalInstance d)
        {
            var uv = PropTextures.DecalUv(d.Kind, d.Tile);
            double hx = d.Sx / 2, hy = d.Sy / 2;
            // corners counter-clockwise seen from the front (+Z of the plane), uv left to right and bottom to top
            double[,] corners = { { -hx, -hy }, { hx, -hy }, { hx, hy }, { -hx, hy } };
            double[] lu = { 0, 1, 1, 0 }, lv = { 0, 0, 1, 1 };
            double rx = d.Kind == DecalKind.Ground ? -Math.PI / 2 : 0;   // a ground decal lies down: its +Z becomes +Y

            var pts = new Float3[4];
            var uvs = new double[8];
            for (int k = 0; k < 4; k++)
            {
                (double x, double y, double z) p = Rotate(corners[k, 0], corners[k, 1], 0, rx, d.Ry, d.Rz);
                pts[k] = new Float3(p.x + d.X, p.y + d.Y, p.z + d.Z);
                uvs[k * 2] = uv.u + lu[k] * uv.du;
                uvs[k * 2 + 1] = uv.v + lv[k] * uv.dv;
            }
            var n = Rotate(0, 0, 1, rx, d.Ry, d.Rz);
            double emit = d.Kind == DecalKind.Graffiti ? GraffitiEmit : 0;
            mb.Quad(pts[0], pts[1], pts[2], pts[3], n.x, n.y, n.z, uvs, new Rgb(1, 1, 1), emit, DecalRough, 0);
        }

        /// <summary>three.js's Euler 'YXZ': about Z first, then X, then Y.</summary>
        static (double x, double y, double z) Rotate(double x, double y, double z, double rx, double ry, double rz)
        {
            double t;
            if (rz != 0) { double cs = Math.Cos(rz), sn = Math.Sin(rz); t = x * cs - y * sn; y = x * sn + y * cs; x = t; }
            if (rx != 0) { double cs = Math.Cos(rx), sn = Math.Sin(rx); t = y * cs - z * sn; z = y * sn + z * cs; y = t; }
            if (ry != 0) { double cs = Math.Cos(ry), sn = Math.Sin(ry); t = x * cs + z * sn; z = -x * sn + z * cs; x = t; }
            return (x, y, z);
        }

        static int Clamp(int index) => Math.Max(0, Math.Min(CityLayout.Grid - 1, index));
    }

    /// <summary>
    /// The steam puffs as one mesh of camera-facing quads that the shader animates, so there is no per-frame work on the CPU. Each quad's
    /// four vertices carry the puff's start position (position), the corner (uv), the drift (colour red) and phase, speed, size and rise (second UV set).
    /// </summary>
    public static class SteamMesh
    {
        public static MeshData Build(IReadOnlyList<SteamPuff> puffs)
        {
            if (puffs == null) throw new ArgumentNullException(nameof(puffs));
            var m = new MeshData();
            int n = puffs.Count;
            m.Positions = new float[n * 12];
            m.Normals = new float[n * 12];
            m.Uvs = new float[n * 8];
            m.Colors = new float[n * 12];
            m.Uv1 = new float[n * 16];
            m.Indices = new int[n * 6];
            double[] cu = { 0, 1, 1, 0 }, cv = { 0, 0, 1, 1 };
            for (int i = 0; i < n; i++)
            {
                SteamPuff p = puffs[i];
                for (int k = 0; k < 4; k++)
                {
                    int v = i * 4 + k;
                    m.Positions[v * 3] = (float)p.X; m.Positions[v * 3 + 1] = (float)p.Y; m.Positions[v * 3 + 2] = (float)p.Z;
                    m.Normals[v * 3 + 2] = 1f;
                    m.Uvs[v * 2] = (float)cu[k]; m.Uvs[v * 2 + 1] = (float)cv[k];
                    m.Colors[v * 3] = (float)p.Drift;
                    m.Uv1[v * 4] = (float)p.Phase; m.Uv1[v * 4 + 1] = (float)p.Speed; m.Uv1[v * 4 + 2] = (float)p.Size; m.Uv1[v * 4 + 3] = (float)p.Rise;
                }
                int b = i * 4;
                int t = i * 6;
                m.Indices[t] = b; m.Indices[t + 1] = b + 1; m.Indices[t + 2] = b + 2;
                m.Indices[t + 3] = b; m.Indices[t + 4] = b + 2; m.Indices[t + 5] = b + 3;
            }
            return m;
        }

        /// <summary>
        /// Where a puff is, how big, and how visible, at <paramref name="seconds"/>, as the prototype animates it (the shader does the same
        /// sums). <paramref name="wind"/> is the prototype's 0.4 + the weather's wind; <paramref name="cameraDistanceSquared"/> is horizontal.
        /// </summary>
        public static (double x, double y, double z, double size, double alpha) Pose(SteamPuff p, double seconds, double wind, double cameraDistanceSquared)
        {
            double t = p.Phase + seconds * p.Speed;
            t -= Math.Floor(t);
            double y = p.Y + t * p.Rise;
            double x = p.X + p.Drift * t * wind;
            double z = p.Z + p.Drift * 0.6 * t * wind;
            double size = p.Size * (0.35 + t * 1.5);
            double near = cameraDistanceSquared > FadeDistanceSquared ? 0 : 1 - cameraDistanceSquared / FadeDistanceSquared;
            return (x, y, z, size, Math.Sin(t * Math.PI) * 0.16 * near);
        }

        /// <summary>A puff fades out entirely at 60 m.</summary>
        public const double FadeDistanceSquared = 3600;
    }
}
