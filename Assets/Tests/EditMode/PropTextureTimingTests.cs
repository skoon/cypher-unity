using System;
using System.Diagnostics;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>How long each prop texture takes to draw, so the loading screen budget can be judged (printed, and bounded loosely).</summary>
    public class PropTextureTimingTests
    {
        static double Ms(Action a)
        {
            var sw = Stopwatch.StartNew();
            a();
            return sw.Elapsed.TotalMilliseconds;
        }

        [Test]
        public void TimeEachTexture()
        {
            PropTextures.Puff();   // warm up the JIT
            double grime = Ms(() => PropTextures.Grime());
            double graffiti = Ms(() => PropTextures.Graffiti());
            double ground = Ms(() => PropTextures.Ground());
            double puff = Ms(() => PropTextures.Puff());
            double atlas = Ms(() => PropTextures.DecalAtlas());
            Console.WriteLine($"TEXTIMING grime {grime:0} ms, graffiti {graffiti:0} ms, ground {ground:0} ms, puff {puff:0} ms, decal atlas {atlas:0} ms");
            Assert.Less(grime + atlas + puff, 5000, "drawing every prop texture stays a small part of the load");
        }
    }
}
