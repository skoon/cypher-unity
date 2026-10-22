using System;
using System.Collections.Generic;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class CityChunkJobTests
    {
        static readonly CityPlan Plan = CityPlan.Generate();

        [Test]
        public void TheStepsGiveTheSameCityAsTheOneShotBuild()
        {
            CityWindow window = CityWindow.AroundPlaza(1);
            CitySlice whole = CityChunker.Build(Plan, window);

            var job = new CityChunkJob(Plan, window);
            var chunks = job.Cells.Select(job.BuildChunk).ToList();
            job.AttachNeon(chunks, job.MeshNeon(job.PlanNeon()));

            Assert.AreEqual(whole.Chunks.Count, chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                Assert.AreEqual((whole.Chunks[i].Gx, whole.Chunks[i].Gz), (chunks[i].Gx, chunks[i].Gz));
                Assert.AreEqual(whole.Chunks[i].TriangleCount, chunks[i].TriangleCount, $"chunk {i}");
                Assert.AreEqual(whole.Chunks[i].Colliders.Count, chunks[i].Colliders.Count, $"chunk {i} colliders");
            }
            Assert.AreEqual(whole.Markings.TriangleCount, job.BuildMarkings().TriangleCount);
        }

        [Test]
        public void TheCellsAreTheOnesInsideTheWindow()
        {
            Assert.AreEqual(49, new CityChunkJob(Plan, CityWindow.AroundPlaza(3)).Cells.Count);
            Assert.AreEqual(9, new CityChunkJob(Plan, CityWindow.AroundPlaza(1)).Cells.Count);
        }

        [Test]
        public void ACellOutsideTheWindowIsRefused()
        {
            var job = new CityChunkJob(Plan, CityWindow.AroundPlaza(0));
            StreetCell outside = Plan.Streets.First(c => c.Gx == 0 && c.Gz == 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => job.BuildChunk(outside));
            Assert.Throws<ArgumentNullException>(() => job.BuildChunk(null));
        }

        [Test]
        public void ANullPlanFailsLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => new CityChunkJob(null, CityWindow.AroundPlaza(1)));
        }

        [Test]
        public void NeonForTheWholeCityIsMoreThanForTheMiddleBecauseItIsPlannedAcrossTheWindow()
        {
            var small = new CityChunkJob(Plan, CityWindow.AroundPlaza(1));
            var whole = new CityChunkJob(Plan, CityWindow.AroundPlaza(3));
            Assert.Greater(whole.PlanNeon().Quads.Count, small.PlanNeon().Quads.Count);
        }
    }
}
