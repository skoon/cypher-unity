using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cypher.City;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The city built in pieces across frames, for the loading bar.</summary>
    public class CityBuilderOverFramesTests
    {
        readonly List<Object> _owned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _owned)
            {
                if (o) Object.DestroyImmediate(o);
            }
            _owned.Clear();
        }

        /// <summary>A city builder configured but not yet awake, so nothing has been built.</summary>
        CityBuilder Make(int radius, bool overFrames, float budgetMilliseconds)
        {
            var go = new GameObject("TestCity");
            _owned.Add(go);
            go.SetActive(false);
            var city = go.AddComponent<CityBuilder>();
            city.Configure(CityPlan.DefaultSeed, radius, CityTestMaterials.Make(_owned));
            city.ConfigureLoading(overFrames, budgetMilliseconds);
            return city;
        }

        static IEnumerator UntilBuilt(CityBuilder city, List<double> progress = null, int maxFrames = 2000)
        {
            for (int i = 0; i < maxFrames && !city.IsBuilt; i++)
            {
                progress?.Add(city.Progress);
                yield return null;
            }
            Assert.IsTrue(city.IsBuilt, "the city never finished building");
        }

        [UnityTest]
        public IEnumerator WithNoBudgetTheCityAppearsOverManyFramesAndTheBarNeverRunsBackwards()
        {
            CityBuilder city = Make(3, overFrames: true, budgetMilliseconds: 0f);
            city.gameObject.SetActive(true);
            Assert.IsFalse(city.IsBuilt, "nothing is built in Awake when building over frames");
            yield return null;
            Assert.IsTrue(city.IsBuilding);

            var progress = new List<double>();
            yield return UntilBuilt(city, progress);

            Assert.GreaterOrEqual(progress.Count, 30, "a piece per frame: plan, signage, textures, 49 chunks and the rest");
            for (int i = 1; i < progress.Count; i++) Assert.GreaterOrEqual(progress[i], progress[i - 1], $"the bar went backwards at frame {i}");
            Assert.Less(progress.Max(), 1.0, "the bar is not full until the city is built");
            Assert.AreEqual(1.0, city.Progress);
            Assert.IsFalse(city.IsBuilding);
            Assert.AreEqual(49, city.Slice.Chunks.Count);
            Assert.GreaterOrEqual(city.BuildFrames, 30);
        }

        [UnityTest]
        public IEnumerator AHugeBudgetBuildsEverythingInTheFirstFrameOrTwo()
        {
            CityBuilder city = Make(3, overFrames: true, budgetMilliseconds: 100000f);
            city.gameObject.SetActive(true);
            yield return null;
            yield return null;
            Assert.IsTrue(city.IsBuilt);
            Assert.LessOrEqual(city.BuildFrames, 2);
        }

        [UnityTest]
        public IEnumerator BuildingOverFramesGivesTheSameCityAsBuildingAtOnce()
        {
            CityBuilder now = Make(1, overFrames: false, budgetMilliseconds: 0f);
            now.gameObject.SetActive(true);
            Assert.IsTrue(now.IsBuilt, "built in Awake");
            Assert.AreEqual(1, now.BuildFrames);

            CityBuilder later = Make(1, overFrames: true, budgetMilliseconds: 0f);
            later.gameObject.SetActive(true);
            yield return UntilBuilt(later);

            Assert.AreEqual(now.Slice.Chunks.Count, later.Slice.Chunks.Count);
            Assert.AreEqual(now.Slice.TriangleCount, later.Slice.TriangleCount);
            Assert.AreEqual(now.Root.GetComponentsInChildren<MeshRenderer>().Length, later.Root.GetComponentsInChildren<MeshRenderer>().Length);
            Assert.AreEqual(now.Root.GetComponentsInChildren<BoxCollider>().Length, later.Root.GetComponentsInChildren<BoxCollider>().Length);
        }

        [UnityTest]
        public IEnumerator ClearingInTheMiddleOfABuildStopsItCleanly()
        {
            CityBuilder city = Make(3, overFrames: true, budgetMilliseconds: 0f);
            city.gameObject.SetActive(true);
            for (int i = 0; i < 6; i++) yield return null;
            Assert.IsTrue(city.IsBuilding);
            Assert.Greater(city.Progress, 0.0);

            city.Clear();
            for (int i = 0; i < 4; i++) yield return null;

            Assert.IsFalse(city.IsBuilding);
            Assert.IsFalse(city.IsBuilt);
            Assert.IsNull(city.Slice);
            Assert.IsNull(city.Root, "the half-built city is gone");
            Assert.AreEqual(0.0, city.Progress);
        }

        [UnityTest]
        public IEnumerator BuildingAgainReplacesTheCity()
        {
            CityBuilder city = Make(1, overFrames: true, budgetMilliseconds: 0f);
            city.gameObject.SetActive(true);
            yield return UntilBuilt(city);
            Transform first = city.Root;

            city.BuildOverFrames();
            yield return null;
            Assert.IsFalse(city.IsBuilt, "starting again discards the old city");
            yield return UntilBuilt(city);
            Assert.AreEqual(1, city.transform.Cast<Transform>().Count(t => t.name == CityBuilder.RootName), "one city, not two");
            Assert.AreNotSame(first, city.Root);
        }

        [UnityTest]
        public IEnumerator EveryStageIsTimedAndTheTotalIsTheWorkDone()
        {
            CityBuilder city = Make(3, overFrames: true, budgetMilliseconds: 4f);
            city.gameObject.SetActive(true);
            yield return UntilBuilt(city);

            foreach (string step in new[] { CityBuilder.StepPlan, CityBuilder.StepSignage, CityBuilder.StepTextures, CityBuilder.StepGround, CityBuilder.StepChunks, CityBuilder.StepEdge })
            {
                Assert.IsTrue(city.StageMilliseconds.ContainsKey(step), $"no timing for {step}");
                Assert.GreaterOrEqual(city.StageMilliseconds[step], 0.0);
            }
            double stages = city.StageMilliseconds.Values.Sum();
            Assert.That(city.BuildMilliseconds, Is.GreaterThan(0).And.GreaterThanOrEqualTo(stages * 0.9), "the total covers the stages");
            Debug.Log($"[Cypher] test: whole city {city.BuildMilliseconds:0} ms of work in {city.BuildFrames} frames at a 4 ms budget; " + string.Join(", ", city.StageMilliseconds.Select(kv => $"{kv.Key} {kv.Value:0}")));
        }

        [Test]
        public void ANegativeBudgetFailsLoudly()
        {
            CityBuilder city = Make(1, overFrames: true, budgetMilliseconds: 1f);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => city.ConfigureLoading(true, -1f));
        }
    }
}
