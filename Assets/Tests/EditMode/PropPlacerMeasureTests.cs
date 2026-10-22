using System;
using System.Diagnostics;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class PropPlacerMeasureTests
    {
        [Test]
        public void Measure()
        {
            CityPlan plan = CityPlan.Generate();
            var sw = Stopwatch.StartNew();
            PropLayout l = PropPlacer.Build(plan, CityWindow.AroundPlaza(3));
            double ms = sw.Elapsed.TotalMilliseconds;

            var byKit = l.Instances.GroupBy(i => i.Kit).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
            long tris = 0;
            foreach (PropInstance i in l.Instances) tris += PropKits.Get(i.Kit).Geometry.TriangleCount;
            long cableTris = l.CellParts.Values.Sum(b => b.TriangleCount);
            Console.WriteLine($"PROPMEASURE {ms:0} ms; instances {l.Instances.Count}; prop tris {tris:N0}; cable tris {cableTris:N0}; decals {l.Decals.Count(d => d.Kind == DecalKind.Ground)} ground / {l.Decals.Count(d => d.Kind == DecalKind.Graffiti)} wall; boxes {l.Boxes.Count}; steam spots {l.SteamSpots.Count}; puffs {l.Puffs.Count}");
            Console.WriteLine("PROPMEASURE kits: " + string.Join(", ", byKit));
        }
    }
}
