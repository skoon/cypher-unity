using System;
using System.Diagnostics;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>What the whole city's signage comes to and how long each part takes (printed, and bounded loosely).</summary>
    public class NeonSignageMeasureTests
    {
        [Test]
        public void MeasureTheWholeCity()
        {
            CityPlan plan = CityPlan.Generate();
            CityWindow whole = CityWindow.AroundPlaza(3);
            NeonAtlasLayout atlas = NeonAtlas.Layout();
            NeonPlanner.Plan(plan, CityWindow.AroundPlaza(0), NeonPlanner.DefaultSeed, atlas);   // warm up the JIT

            var sw = Stopwatch.StartNew();
            NeonLayout layout = NeonPlanner.Plan(plan, whole, NeonPlanner.DefaultSeed, atlas);
            double planMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var meshes = NeonMesher.Build(layout, atlas);
            double meshMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            NeonAtlas.Render(atlas);
            double atlasMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            BoardArt.Render();
            double boardMs = sw.Elapsed.TotalMilliseconds;

            int panels = layout.Quads.Count(q => q.Shape == NeonShape.Panel);
            int boardTris = meshes.Values.Sum(m => m.Boards.TriangleCount), holoTris = meshes.Values.Sum(m => m.Holos.TriangleCount);
            Console.WriteLine($"SIGNAGE {layout.Quads.Count:N0} quads ({panels:N0} lettered panels), {layout.Boxes.Count:N0} housings, {layout.Boards.Count} hoardings, " +
                              $"{layout.Holograms.Count} holograms; {meshes.Values.Sum(m => m.TriangleCount):N0} triangles ({boardTris} board, {holoTris} holo); " +
                              $"plan {planMs:0} ms, mesh {meshMs:0} ms, sign atlas {atlasMs:0} ms, board atlas {boardMs:0} ms");
            Assert.Less(planMs + meshMs, 2000, "planning and meshing the signs stays a small part of the load");
            Assert.Less(atlasMs + boardMs, 6000, "drawing the lettering and the ad panels, on a worker, stays a small part of the load");
        }
    }
}
