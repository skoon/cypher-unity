#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using Cypher.Actors;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using Cypher.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The living crowd in the real Slice scene: when it exists, what it draws, how it freezes and follows the tier, and that the people nearest the player are solid.</summary>
    public class CrowdPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Leave nothing behind: the next fixture builds its own city in whatever scene is loaded, and the real city's props, colliders and capsules would be in its way
            Scene slice = SceneManager.GetSceneByPath(SliceTestScene.ScenePath);
            if (slice.IsValid() && slice.isLoaded)
            {
                Scene empty = SceneManager.CreateScene("AfterTheCrowdTests");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(slice);
            }
            SliceTestScene.Restore();
        }

        static CrowdController Crowd => UnityEngine.Object.FindFirstObjectByType<CrowdController>();

        static bool HasGpu => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// <summary>Stand a person still, at a spot, so a test can rely on where they are.</summary>
        static Pedestrian Plant(CrowdSim sim, int index, double x, double z)
        {
            Pedestrian p = sim.Active[index];
            p.X = p.PrevX = x; p.Z = p.PrevZ = z; p.Y = p.PrevY = sim.Nav.WalkY;
            p.State = PedState.Idle; p.Timer = 1e6; p.Speed = 0; p.WaitTimer = 0;
            return p;
        }

        // ---------------------------------------------------------------- when there is a crowd

        [UnityTest]
        public IEnumerator TheTitleScreenHasNoCrowdAndANewGameStartsOneAtTheTiersSize()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            Assert.IsNotNull(Crowd, "the scene has a crowd controller");
            Assert.IsFalse(Crowd.IsActive, "empty streets behind the title");

            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(2);
            Assert.IsTrue(Crowd.IsActive);
            QualityTier tier = UnityEngine.Object.FindFirstObjectByType<QualityDirector>().Tier;
            Assert.AreEqual(tier.Pedestrians, Crowd.Sim.Active.Count);
            Assert.AreEqual(QualityTiers.MaxPedestrians, Crowd.Sim.Pool.Count, "the pool is built for the biggest tier");
        }

        [UnityTest]
        public IEnumerator ReturningToTheTitleRemovesTheCrowdAndParksItsCapsules()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            Assert.IsTrue(Crowd.IsActive);
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle());
            yield return Frames(2);
            Assert.IsFalse(Crowd.IsActive);
            Assert.AreEqual(0, Crowd.Proxies.Leased);
            // the body's own position: the transform only follows it at the next physics step, and a paused or title game takes none
            for (int i = 0; i < CrowdProxies.Count; i++) Assert.LessOrEqual(Crowd.Proxies[i].GetComponent<Rigidbody>().position.y, -590f, "parked");
        }

        [UnityTest]
        public IEnumerator ASecondGameGetsAFreshCrowdAroundThePlayer()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            CrowdSim first = Crowd.Sim;
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle());
            yield return Frames(2);
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(2);
            Assert.IsTrue(Crowd.IsActive);
            Assert.AreNotSame(first, Crowd.Sim);
        }

        // ---------------------------------------------------------------- drawing

        [UnityTest]
        public IEnumerator SomeoneInFrontOfTheCameraIsDrawnAsTheFullRigWithAShadow()
        {
            if (!HasGpu) Assert.Ignore("no graphics device");
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Camera cam = Camera.main;
            Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
            fwd.y = 0; fwd.Normalize();
            Plant(Crowd.Sim, 0, eye.x + fwd.x * 8, eye.z + fwd.z * 8);
            yield return Frames(3);

            CrowdFrame frame = Crowd.Renderer.Frame;
            Assert.GreaterOrEqual(frame.Count(RigLod.Full), 1, "eight metres away is near");
            Assert.GreaterOrEqual(Crowd.Renderer.DrawCalls, 2, "at least the bodies and the shadows");
            Assert.AreEqual(frame.Total, frame.Count(RigLod.Full) + frame.Count(RigLod.Simple) + frame.Count(RigLod.Silhouette));
        }

        [UnityTest]
        public IEnumerator SomeoneBehindTheCameraIsNotDrawn()
        {
            if (!HasGpu) Assert.Ignore("no graphics device");
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Camera cam = Camera.main;
            // everybody out of view: far behind the camera
            Vector3 eye = cam.transform.position, back = -cam.transform.forward;
            back.y = 0; back.Normalize();
            for (int i = 0; i < Crowd.Sim.Active.Count; i++) Plant(Crowd.Sim, i, eye.x + back.x * (20 + i * 0.01), eye.z + back.z * 20);
            yield return Frames(3);
            Assert.AreEqual(0, Crowd.Renderer.Frame.Total);
        }

        [UnityTest]
        public IEnumerator FarPeopleAreDrawnAsCheaperBodies()
        {
            if (!HasGpu) Assert.Ignore("no graphics device");
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            QualityTier tier = UnityEngine.Object.FindFirstObjectByType<QualityDirector>().Tier;
            Camera cam = Camera.main;
            Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
            fwd.y = 0; fwd.Normalize();
            Plant(Crowd.Sim, 0, eye.x + fwd.x * 5, eye.z + fwd.z * 5);
            Plant(Crowd.Sim, 1, eye.x + fwd.x * ((tier.ActorNear + tier.ActorFar) / 2), eye.z + fwd.z * ((tier.ActorNear + tier.ActorFar) / 2));
            Plant(Crowd.Sim, 2, eye.x + fwd.x * (tier.ActorFar + 30), eye.z + fwd.z * (tier.ActorFar + 30));
            yield return Frames(3);
            CrowdFrame frame = Crowd.Renderer.Frame;
            Assert.GreaterOrEqual(frame.Count(RigLod.Full), 1);
            Assert.GreaterOrEqual(frame.Count(RigLod.Simple), 1);
            Assert.GreaterOrEqual(frame.Count(RigLod.Silhouette), 1);
        }

        // ---------------------------------------------------------------- pausing, tiers

        [UnityTest]
        public IEnumerator PausingFreezesEveryoneButTheyAreStillDrawn()
        {
            if (!HasGpu) Assert.Ignore("no graphics device");
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Camera cam = Camera.main;
            Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
            fwd.y = 0; fwd.Normalize();
            Pedestrian standing = Plant(Crowd.Sim, 0, eye.x + fwd.x * 8, eye.z + fwd.z * 8);
            standing.State = PedState.Walk; standing.Timer = 1e6; standing.Speed = 1.3;   // walking, so it would move
            yield return Frames(3);
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            yield return Frames(2);

            var before = Crowd.Sim.Active.Select(p => (p.X, p.Z, p.Heading, p.Phase)).ToList();
            yield return Frames(15);
            var after = Crowd.Sim.Active.Select(p => (p.X, p.Z, p.Heading, p.Phase)).ToList();
            CollectionAssert.AreEqual(before, after, "time stopped, and so did they");
            Assert.GreaterOrEqual(Crowd.Renderer.Frame.Count(RigLod.Full), 1, "still drawn");
        }

        [UnityTest]
        public IEnumerator ATierChangeChangesThePopulationAndTheDetailDistances()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            // nobody in view, so people can come and go: look at the sky
            UnityEngine.Object.FindFirstObjectByType<FirstPersonController>().enabled = false;
            Camera.main.transform.rotation = Quaternion.Euler(-85f, 0f, 0f);
            yield return Frames(2);

            // the simulation steps at 60 Hz, which a few fast editor frames may not reach: wait in real time
            Crowd.ApplyTier(QualityTiers.For(QualityPreset.Performance));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(36, Crowd.Sim.Active.Count);

            Crowd.ApplyTier(QualityTiers.For(QualityPreset.Ultra));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(400, Crowd.Sim.Active.Count);
            Assert.AreEqual(400, Crowd.Sim.Active.Distinct().Count(), "nobody twice");
        }

        // ---------------------------------------------------------------- solid

        [UnityTest]
        public IEnumerator ThePeopleNearestThePlayerAreSolidAndThePlayerCannotWalkThroughThem()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            FirstPersonController player = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            player.enabled = false;   // the test moves the character by hand
            Vector3 feet = player.transform.position;
            Pedestrian ahead = Plant(Crowd.Sim, 0, feet.x, feet.z + 1.4);
            for (int i = 1; i < Crowd.Sim.Active.Count; i++) Plant(Crowd.Sim, i, feet.x + 300, feet.z + 300 + i);   // everyone else far away
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.GreaterOrEqual(Crowd.Proxies.Leased, 1, "a capsule has gone to the person");
            Physics.SyncTransforms();

            // one long step straight into them: without a capsule the character would end up past them
            player.Controller.Move(new Vector3(0f, 0f, 3f));
            Assert.Less(player.transform.position.z, (float)ahead.Z - 0.4f, "stopped by the person");
        }

        [UnityTest]
        public IEnumerator ACapsuleStandsWhereItsPersonDoes()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            Vector3 feet = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>().transform.position;
            Pedestrian p = Plant(Crowd.Sim, 0, feet.x + 3, feet.z);
            for (int i = 1; i < Crowd.Sim.Active.Count; i++) Plant(Crowd.Sim, i, feet.x + 300, feet.z + 300 + i);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Vector3 chest = new Vector3((float)p.X, (float)(p.Y + 0.86 * p.Appearance.Scale), (float)p.Z);
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.CheckSphere(chest, 0.05f), "there is a collider at their chest");
            Assert.IsFalse(Physics.CheckSphere(chest + new Vector3(2f, 0f, 0f), 0.05f), "and not two metres away");
        }

        // ---------------------------------------------------------------- the GPU poses as the reference does

        /// <summary>The rig's vertices (joint-local positions and part ids) as the compute shader takes them.</summary>
        static Vector4[] VerticesOf(MeshData mesh)
        {
            var v = new Vector4[mesh.VertexCount];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector4(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2], mesh.Uv1[i * 4]);
            return v;
        }

        static Mat4 MatrixFor(PedestrianPoseResult r, RigPart part)
        {
            switch (part)
            {
                case RigPart.TorsoShort:
                case RigPart.TorsoLong: return r.Torso;
                case RigPart.Trim: return r.Trim;
                case RigPart.Head: return r.Head;
                case RigPart.Hood: return r.Hood;
                case RigPart.ArmL: return r.ArmL;
                case RigPart.ArmR: return r.ArmR;
                case RigPart.Umbrella: return r.Umbrella;
                case RigPart.Pack: return r.Pack;
                case RigPart.ThighL: return r.ThighL;
                case RigPart.ShinL: return r.ShinL;
                case RigPart.ThighR: return r.ThighR;
                case RigPart.ShinR: return r.ShinR;
                default: throw new ArgumentException(part.ToString());
            }
        }

        static bool Worn(RigPart part, PedestrianAppearance a)
        {
            switch (part)
            {
                case RigPart.TorsoShort: return !a.LongCoat;
                case RigPart.TorsoLong: return a.LongCoat;
                case RigPart.Trim: return a.Trim;
                case RigPart.Hood: return a.Hood;
                case RigPart.Umbrella: return a.Umbrella;
                case RigPart.Pack: return a.Pack;
                default: return true;
            }
        }

        [Test]
        public void TheShaderPosesEveryVertexOfEveryStateWhereTheCoreReferenceDoes()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("no compute shaders on this device");
            var probe = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Cypher/Shaders/PedestrianPoseProbe.compute");
            Assert.IsNotNull(probe, "the probe shader is missing");

            CityNav nav = CityNav.Build(CityPlan.Generate());
            CrowdSim sim = CrowdSim.Create(nav, 60, 60, 0, 0);
            const double time = 7.7;
            var poses = new PoseInput[60];
            var results = new PedestrianPoseResult[60];
            var instances = new PedestrianGpuData[60];
            double[] speeds = { 0, 0.4, 1.3, 2.4, 2.8 };
            for (int k = 0; k < 60; k++)
            {
                PedestrianAppearance a = sim.Pool[k].Appearance;
                poses[k] = new PoseInput
                {
                    PrevX = k * 1.7, X = k * 1.7, PrevZ = -k * 2.3, Z = -k * 2.3, PrevY = 0.185, Y = 0.185,
                    PrevHeading = k * 0.9 - 3, Heading = k * 0.9 - 3, PrevPhase = k * 0.7, Phase = k * 0.7, Alpha = 1,
                    Speed = speeds[k % 5], Scale = a.Scale, Build = a.Build, LongCoat = a.LongCoat, Hood = a.Hood, Pack = a.Pack, Umbrella = a.Umbrella, Trim = a.Trim,
                    LookSeed = sim.Pool[k].LookSeed, LookYaw = 0.1, Near = false, Time = time,
                };
                results[k] = PedestrianPose.Pose(poses[k]);
                instances[k] = new PedestrianGpuData
                {
                    X = (float)poses[k].X, Y = (float)poses[k].Y, Z = (float)poses[k].Z, Heading = (float)poses[k].Heading,
                    Phase = (float)poses[k].Phase, Speed = (float)poses[k].Speed, Scale = (float)a.Scale, Build = (float)a.Build,
                    LookYaw = (float)results[k].LookYaw, LookSeed = (float)sim.Pool[k].LookSeed,
                    Flags = (a.LongCoat ? PedestrianGpuData.FlagLongCoat : 0u) | (a.Umbrella ? PedestrianGpuData.FlagUmbrella : 0u) | (a.Hood ? PedestrianGpuData.FlagHood : 0u)
                        | (a.Pack ? PedestrianGpuData.FlagPack : 0u) | (a.Trim ? PedestrianGpuData.FlagTrim : 0u),
                };
            }

            int compared = 0;
            double worst = 0;
            foreach (RigLod lod in new[] { RigLod.Full, RigLod.Simple })
            {
                MeshData mesh = PedestrianRig.Build(lod);
                Vector4[] vertices = VerticesOf(mesh);
                using (var peds = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instances.Length, PedestrianGpuData.Size))
                using (var verts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertices.Length, 16))
                using (var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertices.Length * instances.Length, 16))
                {
                    peds.SetData(instances);
                    verts.SetData(vertices);
                    int kernel = probe.FindKernel("Pose");
                    probe.SetBuffer(kernel, "_Peds", peds);
                    probe.SetBuffer(kernel, "_Vertices", verts);
                    probe.SetBuffer(kernel, "_Out", output);
                    probe.SetFloat("_Time", (float)time);
                    probe.SetInt("_VertexCount", vertices.Length);
                    probe.SetInt("_PedCount", instances.Length);
                    probe.Dispatch(kernel, Mathf.CeilToInt(vertices.Length * instances.Length / 64f), 1, 1);
                    var gpu = new Vector4[vertices.Length * instances.Length];
                    output.GetData(gpu);

                    for (int k = 0; k < instances.Length; k++)
                    {
                        PedestrianAppearance a = sim.Pool[k].Appearance;
                        for (int v = 0; v < vertices.Length; v++)
                        {
                            var part = (RigPart)(int)(vertices[v].w + 0.5f);
                            Vector4 got = gpu[k * vertices.Length + v];
                            bool worn = part == RigPart.Static || Worn(part, a);
                            Assert.AreEqual(worn ? 1f : 0f, got.w, $"{lod} state {k} vertex {v} ({part}): worn");
                            if (!worn) continue;

                            Mat4 m = part == RigPart.Static
                                ? PedestrianPose.StaticBody(poses[k].X, poses[k].Y, poses[k].Z, poses[k].Heading, a.Scale, a.Build)
                                : MatrixFor(results[k], part);
                            var expected = m.Transform(vertices[v].x, vertices[v].y, vertices[v].z);
                            double e = Math.Max(Math.Abs(expected.X - got.x), Math.Max(Math.Abs(expected.Y - got.y), Math.Abs(expected.Z - got.z)));
                            worst = Math.Max(worst, e);
                            Assert.LessOrEqual(e, 5e-4, $"{lod} state {k} vertex {v} ({part}): reference ({expected.X:0.0000}, {expected.Y:0.0000}, {expected.Z:0.0000}), GPU ({got.x:0.0000}, {got.y:0.0000}, {got.z:0.0000})");
                            compared++;
                        }
                    }
                }
            }
            Debug.Log($"[Cypher] pose probe: {compared:N0} vertices compared, worst difference {worst:0.0e+0} m");
            Assert.Greater(compared, 20000);
        }
    }
}
#endif
