using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Cypher.Core
{
    /// <summary>
    /// Everything the props need that does not touch the engine, done in one call: the placement, the per-cell meshes, the steam mesh and
    /// the textures' pixels. It is pure computation over read-only data, so the loading code runs it on a worker thread while the main
    /// thread builds the city, then turns the result into meshes, textures and materials.
    /// </summary>
    public sealed class PropBake
    {
        public PropLayout Layout { get; private set; }
        public Dictionary<(int Gx, int Gz), PropCell> Cells { get; private set; }
        public MeshData Steam { get; private set; }

        /// <summary>RGBA pixels, row 0 at the top (<see cref="PropTextures.Grime"/>).</summary>
        public byte[] Grime { get; private set; }

        /// <summary>RGBA pixels flipped for Unity (<see cref="PropTextures.DecalAtlas"/>).</summary>
        public byte[] DecalAtlas { get; private set; }

        public byte[] Puff { get; private set; }

        public double LayoutMilliseconds { get; private set; }
        public double MeshMilliseconds { get; private set; }
        public double TextureMilliseconds { get; private set; }

        public int PropTriangles
        {
            get
            {
                int n = 0;
                foreach (PropCell c in Cells.Values) n += c.Props.TriangleCount;
                return n;
            }
        }

        public static PropBake Run(CityPlan plan, CityWindow window, uint seed = PropPlacer.DefaultSeed)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var bake = new PropBake();
            var sw = Stopwatch.StartNew();

            bake.Layout = PropPlacer.Build(plan, window, seed);
            bake.LayoutMilliseconds = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            bake.Cells = PropCellMesher.Build(bake.Layout);
            bake.Steam = SteamMesh.Build(bake.Layout.Puffs);
            bake.MeshMilliseconds = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            bake.Grime = PropTextures.Grime();
            bake.DecalAtlas = PropTextures.DecalAtlas();
            bake.Puff = PropTextures.Puff();
            bake.TextureMilliseconds = sw.Elapsed.TotalMilliseconds;
            return bake;
        }
    }
}
