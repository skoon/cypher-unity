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
    /// <summary>The living traffic in the real Slice scene: when it exists, what it draws, how it freezes and follows the tier, that the nearest vehicles are solid, that people and vehicles look out for each other, and that the GPU places the parts where the Core reference does.</summary>
    public class TrafficPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Leave nothing behind: the next fixture builds its own city in whatever scene is loaded, and the real city's props, colliders and boxes would be in its way
            Scene slice = SceneManager.GetSceneByPath(SliceTestScene.ScenePath);
            if (slice.IsValid() && slice.isLoaded)
            {
                Scene empty = SceneManager.CreateScene("AfterTheTrafficTests");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(slice);
            }
            SliceTestScene.Restore();
        }

        static TrafficController Traffic => UnityEngine.Object.FindFirstObjectByType<TrafficController>();

        static CrowdController Crowd => UnityEngine.Object.FindFirstObjectByType<CrowdController>();

        static bool HasGpu => SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// <summary>Stand a vehicle still at a spot, facing a way, with its waypoint far ahead so it does not turn on the spot.</summary>
        static Vehicle Plant(TrafficSim sim, int index, int type, double x, double z, double heading)
        {
            Vehicle v = sim.Fleet[index];
            v.Type = VehicleCatalog.Ground[type]; v.TypeIndex = type;
            v.Active = true; v.X = v.PrevX = x; v.Z = v.PrevZ = z; v.Y = sim.Nav.RoadY; v.Heading = v.PrevHeading = heading;
            v.Speed = 0; v.Cruise = 0;
            v.Leg = 0; v.Turn = Manoeuvre.Straight;
            v.WpX = x + Math.Sin(heading) * 30; v.WpZ = z + Math.Cos(heading) * 30;
            return v;
        }

        // ---------------------------------------------------------------- when there is traffic

        [UnityTest]
        public IEnumerator TheTitleScreenHasNoTrafficAndANewGameStartsItAtTheTiersSize()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            Assert.IsNotNull(Traffic, "the scene has a traffic controller");
            Assert.IsFalse(Traffic.IsActive, "empty roads and sky behind the title");

            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(2);
            Assert.IsTrue(Traffic.IsActive);
            QualityTier tier = UnityEngine.Object.FindFirstObjectByType<QualityDirector>().Tier;
            Assert.AreEqual(tier.GroundVehicles, Traffic.Sim.Vehicles.Count);
            Assert.AreEqual(tier.AircraftCount, Traffic.Sim.Airborne.Count);
            Assert.AreEqual(QualityTiers.MaxGroundVehicles, Traffic.Sim.Fleet.Count, "the fleet is built for the biggest tier");
            Assert.AreEqual(QualityTiers.MaxAircraft, Traffic.Sim.AircraftFleet.Count);
            Assert.AreSame(Traffic.Sim, Crowd.TrafficQuery, "the people ask the traffic before they step into a road");
        }

        [UnityTest]
        public IEnumerator ReturningToTheTitleRemovesTheTrafficParksItsBoxesAndPutsOutItsLights()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            Assert.IsTrue(Traffic.IsActive);
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle());
            yield return Frames(2);
            Assert.IsFalse(Traffic.IsActive);
            Assert.IsNull(Crowd.TrafficQuery, "and the people have nothing to ask");
            Assert.AreEqual(0, Traffic.Proxies.Leased);
            for (int i = 0; i < TrafficProxies.Count; i++) Assert.LessOrEqual(Traffic.Proxies[i].GetComponent<Rigidbody>().position.y, -590f, "parked");
            Assert.AreEqual(0, Traffic.Lights.Lights.Count(l => l.enabled), "dark");
        }

        [UnityTest]
        public IEnumerator ASecondGameGetsFreshTraffic()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            TrafficSim first = Traffic.Sim;
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle());
            yield return Frames(2);
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Frames(2);
            Assert.IsTrue(Traffic.IsActive);
            Assert.AreNotSame(first, Traffic.Sim);
            Assert.AreSame(Traffic.Sim, Crowd.TrafficQuery);
        }

        [UnityTest]
        public IEnumerator TheNoTrafficFlagLeavesTheRoadsAndTheSkyEmptyButNotThePavements()
        {
            SliceTestScene.Isolate(options: new FlowOptions(noTraffic: true));
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            Assert.IsFalse(Traffic.IsActive);
            Assert.IsTrue(Crowd.IsActive, "the people are a separate control");
            Assert.IsNull(Crowd.TrafficQuery);
        }

        // ---------------------------------------------------------------- drawing

        [UnityTest]
        public IEnumerator AVehicleInFrontOfTheCameraIsDrawnWithItsWheelsLampsAndShadowInAFewDraws()
        {
            if (!HasGpu) Assert.Ignore("no graphics device");
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Camera cam = Camera.main;
            Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
            fwd.y = 0; fwd.Normalize();
            Plant(Traffic.Sim, 0, 0, eye.x + fwd.x * 15, eye.z + fwd.z * 15, 0);
            yield return Frames(3);

            TrafficFrame frame = Traffic.Renderer.Frame;
            Assert.GreaterOrEqual(frame.GroundTotal, 1);
            Assert.GreaterOrEqual(frame.GroundCount(0), 1, "a sedan");
            Assert.GreaterOrEqual(Traffic.Renderer.DrawCalls, 6, "a body, the wheels, the glow, the shadow, the lenses and the masts");
            Assert.LessOrEqual(Traffic.Renderer.DrawCalls, 12, "however many there are");
            Assert.AreEqual(256, Traffic.Renderer.SignalHeads, "four at each of the 64 junctions");
        }

        [UnityTest]
        public IEnumerator NothingBehindTheCameraIsDrawn()
        {
            if (!HasGpu) Assert.Ignore("no graphics device");
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            // everyone far behind the camera, and the aircraft too
            Camera cam = Camera.main;
            Vector3 eye = cam.transform.position, back = -cam.transform.forward;
            back.y = 0; back.Normalize();
            foreach (Vehicle v in Traffic.Sim.Vehicles) { v.Cruise = 0; v.Speed = 0; v.X = v.PrevX = eye.x + back.x * 60; v.Z = v.PrevZ = eye.z + back.z * 60; v.WpX = v.X + back.x * 30; v.WpZ = v.Z + back.z * 30; }
            foreach (Aircraft a in Traffic.Sim.Airborne) { a.Speed = 0; a.X = a.PrevX = eye.x + back.x * 80; a.Z = a.PrevZ = eye.z + back.z * 80; }
            yield return Frames(3);
            Assert.AreEqual(0, Traffic.Renderer.Frame.GroundTotal);
            Assert.AreEqual(0, Traffic.Renderer.Frame.AircraftTotal);
        }

        [UnityTest]
        public IEnumerator TheNearestVehiclesInViewCarryTheTiersPointLights()
        {
            if (!HasGpu) Assert.Ignore("no graphics device");
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Camera cam = Camera.main;
            Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
            fwd.y = 0; fwd.Normalize();
            for (int i = 0; i < 8; i++) Plant(Traffic.Sim, i, i % 4, eye.x + fwd.x * (10 + i * 6), eye.z + fwd.z * (10 + i * 6), 0);
            yield return Frames(3);

            QualityTier tier = UnityEngine.Object.FindFirstObjectByType<QualityDirector>().Tier;
            Assert.AreEqual(Math.Min(tier.VehicleLights, Traffic.Renderer.Frame.LightSpots.Count), Traffic.Lights.Lit);
            Assert.GreaterOrEqual(Traffic.Lights.Lit, 1);
            Vector3 nearest = Traffic.Lights.Lights.First(l => l.enabled).transform.position;
            Assert.AreEqual((float)Traffic.Renderer.Frame.LightSpots[0].x, nearest.x, 1e-3f, "the first light is on the nearest vehicle");

            Traffic.ApplyTier(QualityTiers.For(QualityPreset.Performance));
            yield return Frames(3);
            Assert.AreEqual(2, Traffic.Lights.Lit, "Performance has two");
        }

        [UnityTest]
        public IEnumerator TheSignalStateThatTheShadersReadFollowsTheCycle()
        {
            if (!HasGpu) Assert.Ignore("no graphics device");
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            Vector4 state = Shader.GetGlobalVector("_CypherSignalState");
            Assert.AreEqual(Traffic.Sim.Signal.GreenAxis, (int)state.x);
            Assert.AreEqual(Traffic.Sim.Signal.Amber ? 1 : 0, (int)state.y);
        }

        // ---------------------------------------------------------------- pausing, tiers

        [UnityTest]
        public IEnumerator PausingFreezesEverythingButItIsStillDrawn()
        {
            if (!HasGpu) Assert.Ignore("no graphics device");
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Camera cam = Camera.main;
            Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
            fwd.y = 0; fwd.Normalize();
            Plant(Traffic.Sim, 0, 0, eye.x + fwd.x * 15, eye.z + fwd.z * 15, 0);
            yield return Frames(3);
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            yield return Frames(2);

            var before = Traffic.Sim.Vehicles.Select(v => (v.X, v.Z, v.Heading, v.Speed)).Concat(Traffic.Sim.Airborne.Select(a => (a.X, a.Z, a.Heading, a.Speed))).ToList();
            double phase = Traffic.Sim.Signal.PhaseTime;
            yield return Frames(15);
            var after = Traffic.Sim.Vehicles.Select(v => (v.X, v.Z, v.Heading, v.Speed)).Concat(Traffic.Sim.Airborne.Select(a => (a.X, a.Z, a.Heading, a.Speed))).ToList();
            CollectionAssert.AreEqual(before, after, "time stopped, and so did they");
            Assert.AreEqual(phase, Traffic.Sim.Signal.PhaseTime, "and the signals");
            Assert.GreaterOrEqual(Traffic.Renderer.Frame.GroundTotal, 1, "still drawn");
        }

        [UnityTest]
        public IEnumerator ATierChangeChangesTheNumbersOfVehiclesAndAircraft()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(2);
            Traffic.Sim.InView = (x, y, z, r) => false;   // nothing in view, so vehicles can come and go
            Vector3 eye = Camera.main.transform.position;
            // anything within 20 m of the camera stays, in view or not
            int nearVehicles = Traffic.Sim.Vehicles.Count(v => (v.X - eye.x) * (v.X - eye.x) + (v.Z - eye.z) * (v.Z - eye.z) < 400);
            int nearAircraft = Traffic.Sim.Airborne.Count(a => (a.X - eye.x) * (a.X - eye.x) + (a.Z - eye.z) * (a.Z - eye.z) < 400);

            // the simulation steps at 60 Hz, which a few fast editor frames may not reach: wait in real time
            Traffic.ApplyTier(QualityTiers.For(QualityPreset.Performance));
            yield return new WaitForSeconds(0.4f);
            Assert.That(Traffic.Sim.Vehicles.Count, Is.InRange(18, 18 + nearVehicles));
            Assert.That(Traffic.Sim.Airborne.Count, Is.InRange(5, 5 + nearAircraft));

            Traffic.ApplyTier(QualityTiers.For(QualityPreset.Ultra));
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(96, Traffic.Sim.Vehicles.Count);
            Assert.AreEqual(96, Traffic.Sim.Vehicles.Distinct().Count(), "nobody twice");
            Assert.AreEqual(36, Traffic.Sim.Airborne.Count);
        }

        // ---------------------------------------------------------------- solid

        [UnityTest]
        public IEnumerator TheVehiclesNearestThePlayerAreSolidAndThePlayerCannotWalkThroughThem()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            FirstPersonController player = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            player.enabled = false;   // the test moves the character by hand
            Vector3 feet = player.transform.position;
            Vehicle ahead = Plant(Traffic.Sim, 0, 0, feet.x, feet.z + 5, 0);
            for (int i = 1; i < Traffic.Sim.Fleet.Count; i++) Plant(Traffic.Sim, i, i % 4, feet.x + 300, feet.z + 300 + i * 3, 0);   // everyone else far away
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.GreaterOrEqual(Traffic.Proxies.Leased, 1, "a box has gone to the vehicle");
            Physics.SyncTransforms();

            // one long step straight into it: without a box the character would end up past it
            player.Controller.Move(new Vector3(0f, 0f, 8f));
            Assert.Less(player.transform.position.z, (float)(ahead.Z - ahead.HalfLength), "stopped by the vehicle");
        }

        // ---------------------------------------------------------------- people and vehicles look out for each other

        [UnityTest]
        public IEnumerator AVehicleStopsShortOfAPersonStandingInItsLane()
        {
            yield return SliceTestScene.LoadAndPlay();
            yield return Frames(3);
            TrafficSim traffic = Traffic.Sim;
            CityNav nav = traffic.Nav;
            double lane = traffic.Lane;
            // the road along X at row 3, in the lane of the +X traffic; the person is 35 m up the road from the vehicle
            double z = nav.ZLines[3] + lane;
            Vehicle v = Plant(traffic, 0, 0, -85, z, Math.PI / 2);
            v.Dx = 1; v.Dz = 0; v.Ndx = 1; v.Ndz = 0; v.Ix = 3; v.Iz = 3;
            v.WpX = nav.XLines[3] - (nav.AsphaltHalf + 1.1); v.WpZ = z;
            v.Cruise = 14; v.Speed = 12;
            for (int i = 1; i < traffic.Fleet.Count; i++) Plant(traffic, i, i % 4, 100 + (i % 10) * 6, 100 + (i / 10) * 6, 0);   // everyone else well away, and standing
            Pedestrian p = Crowd.Sim.Active[0];
            p.X = p.PrevX = -50; p.Z = p.PrevZ = z; p.Y = p.PrevY = nav.RoadY;
            p.State = PedState.Idle; p.Timer = 1e6; p.Speed = 0; p.WaitTimer = 0; p.Crossing = Crossing.None;
            for (int i = 1; i < Crowd.Sim.Active.Count; i++) { Pedestrian q = Crowd.Sim.Active[i]; q.X = q.PrevX = 300 + i; q.Z = q.PrevZ = 300; q.State = PedState.Idle; q.Timer = 1e6; q.Speed = 0; }
            UnityEngine.Object.FindFirstObjectByType<FirstPersonController>().enabled = false;

            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until)
            {
                Assert.Less(v.X + v.HalfLength, p.X - 0.5, "the vehicle never reaches them");
                yield return null;
            }
            Assert.Less(v.Speed, 0.5, "it has stopped");
            Assert.That(p.X - (v.X + v.HalfLength), Is.InRange(1.5, 4.0), "about three metres short");
        }

        // ---------------------------------------------------------------- the GPU places the parts as the reference does

        static Vector4[] VerticesOf(MeshData mesh, bool withPart)
        {
            var v = new Vector4[mesh.VertexCount];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector4(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2], withPart ? mesh.Uv1[i * 4] : 0f);
            return v;
        }

        static Vector4[] Dispatch(ComputeShader probe, int mode, VehicleGpuData[] instances, int instanceCount, Vector4[] vertices, float time)
        {
            using (var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, instances.Length, VehicleGpuData.Size))
            using (var verts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertices.Length, 16))
            using (var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertices.Length * instanceCount, 16))
            {
                buffer.SetData(instances);
                verts.SetData(vertices);
                int kernel = probe.FindKernel("Place");
                probe.SetBuffer(kernel, "_Vehicles", buffer);
                probe.SetBuffer(kernel, "_Vertices", verts);
                probe.SetBuffer(kernel, "_Out", output);
                probe.SetFloat("_Time", time);
                probe.SetInt("_Mode", mode);
                probe.SetInt("_VertexCount", vertices.Length);
                probe.SetInt("_InstanceCount", instanceCount);
                probe.Dispatch(kernel, Mathf.CeilToInt(vertices.Length * instanceCount / 64f), 1, 1);
                var gpu = new Vector4[vertices.Length * instanceCount];
                output.GetData(gpu);
                return gpu;
            }
        }

        [Test]
        public void TheShaderPlacesEveryPartOfEveryVehicleAndAircraftWhereTheCoreReferenceDoes()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("no compute shaders on this device");
            var probe = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/_Cypher/Shaders/VehiclePlacementProbe.compute");
            Assert.IsNotNull(probe, "the probe shader is missing");

            const int count = 40;
            const double time = 5.4321, roadY = 0;
            var vehicles = new Vehicle[count];
            var instances = new VehicleGpuData[count];
            for (int k = 0; k < count; k++)
            {
                int type = k % 4;
                vehicles[k] = new Vehicle
                {
                    Type = VehicleCatalog.Ground[type], TypeIndex = type,
                    X = k * 7.3 - 120, Z = 90 - k * 5.1, Y = roadY, Heading = k * 0.83 - 5, Spin = k * 1.7, Glow = (k * 0.137) % 1, LampTint = 0.9 + (k % 5) * 0.05,
                };
                vehicles[k].PrevX = vehicles[k].X; vehicles[k].PrevZ = vehicles[k].Z; vehicles[k].PrevHeading = vehicles[k].Heading;
                Vehicle v = vehicles[k];
                instances[k] = new VehicleGpuData { X = (float)v.X, Y = (float)v.Y, Z = (float)v.Z, Heading = (float)v.Heading, Spin = (float)v.Spin, Glow = (float)v.Glow, LampTint = (float)v.LampTint, Type = type };
            }
            VehiclePose[] poses = vehicles.Select(v => VehiclePlacement.Place(v, 1, time, roadY)).ToArray();

            int compared = 0;
            double worst = 0;

            void Check(string what, Vector4[] gpu, int instance, int vertex, int vertexCount, (double X, double Y, double Z) expected, bool exists, int index)
            {
                Vector4 got = gpu[index];
                Assert.AreEqual(exists ? 1f : 0f, got.w, $"{what} instance {instance} vertex {vertex}: exists");
                if (!exists) return;
                double e = Math.Max(Math.Abs(expected.X - got.x), Math.Max(Math.Abs(expected.Y - got.y), Math.Abs(expected.Z - got.z)));
                worst = Math.Max(worst, e);
                Assert.LessOrEqual(e, 1e-3, $"{what} instance {instance} vertex {vertex}: reference ({expected.X:0.0000}, {expected.Y:0.0000}, {expected.Z:0.0000}), GPU ({got.x:0.0000}, {got.y:0.0000}, {got.z:0.0000})");
                compared++;
            }

            // ground bodies: any hull will do, the placement is the same for all
            foreach (int type in new[] { 0, 3 })
            {
                MeshData body = VehicleMeshes.Body(VehicleCatalog.Ground[type]);
                Vector4[] vertices = VerticesOf(body, false);
                Vector4[] gpu = Dispatch(probe, 0, instances, count, vertices, (float)time);
                for (int k = 0; k < count; k++)
                {
                    for (int i = 0; i < vertices.Length; i += 3) Check("body", gpu, k, i, vertices.Length, poses[k].Body.Transform(vertices[i].x, vertices[i].y, vertices[i].z), true, k * vertices.Length + i);
                }
            }

            // wheels: four slots a vehicle
            {
                Vector4[] vertices = VerticesOf(VehicleMeshes.Wheel(), false);
                Vector4[] gpu = Dispatch(probe, 1, instances, count * 4, vertices, (float)time);
                for (int k = 0; k < count; k++)
                {
                    for (int w = 0; w < 4; w++)
                    {
                        bool exists = w < vehicles[k].Type.WheelCount;
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            (double X, double Y, double Z) expected = exists ? poses[k].Wheels[w].Transform(vertices[i].x, vertices[i].y, vertices[i].z) : (0, 0, 0);
                            Check($"wheel {w}", gpu, k, i, vertices.Length, expected, exists, (k * 4 + w) * vertices.Length + i);
                        }
                    }
                }
            }

            // the ground glow: lamps, under-glow and cones, each with its own transform
            {
                MeshData glow = VehicleMeshes.GroundGlow();
                Vector4[] vertices = VerticesOf(glow, true);
                Vector4[] gpu = Dispatch(probe, 3, instances, count, vertices, (float)time);
                for (int k = 0; k < count; k++)
                {
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Mat4 m = (GlowPart)(int)(vertices[i].w + 0.5f) == GlowPart.Lamps ? poses[k].Rig : (GlowPart)(int)(vertices[i].w + 0.5f) == GlowPart.Underglow ? poses[k].Underglow : poses[k].Beams;
                        Check($"glow {(GlowPart)(int)(vertices[i].w + 0.5f)}", gpu, k, i, vertices.Length, m.Transform(vertices[i].x, vertices[i].y, vertices[i].z), true, k * vertices.Length + i);
                    }
                }
            }

            // the shadows
            {
                Vector4[] vertices = VerticesOf(VehicleMeshes.Shadow(), false);
                Vector4[] gpu = Dispatch(probe, 5, instances, count, vertices, (float)time);
                for (int k = 0; k < count; k++)
                {
                    for (int i = 0; i < vertices.Length; i++) Check("shadow", gpu, k, i, vertices.Length, poses[k].Shadow.Transform(vertices[i].x, vertices[i].y, vertices[i].z), true, k * vertices.Length + i);
                }
            }

            // aircraft: bodies and glow
            const int planes = 12;
            var aircraft = new Aircraft[planes];
            var planeInstances = new VehicleGpuData[planes];
            for (int k = 0; k < planes; k++)
            {
                aircraft[k] = new Aircraft { Type = VehicleCatalog.Aircraft[k % 2], TypeIndex = k % 2, X = k * 9.1 - 50, Y = 24 + k * 7.7, Z = 40 - k * 6.3, Heading = k * 0.77 - 3, Bank = Math.Sin(k) * 0.1, Bob = 0 };
                Aircraft a = aircraft[k];
                a.PrevX = a.X; a.PrevY = a.Y; a.PrevZ = a.Z; a.PrevHeading = a.Heading;
                planeInstances[k] = new VehicleGpuData { X = (float)a.X, Y = (float)a.Y, Z = (float)a.Z, Heading = (float)a.Heading, Spin = (float)a.Bank, Type = k % 2 };
            }
            {
                Vector4[] vertices = VerticesOf(VehicleMeshes.Body(VehicleCatalog.Aircraft[1]), false);
                Vector4[] gpu = Dispatch(probe, 2, planeInstances, planes, vertices, (float)time);
                Vector4[] glowVertices = VerticesOf(VehicleMeshes.AircraftGlow(), false);
                Vector4[] glowGpu = Dispatch(probe, 4, planeInstances, planes, glowVertices, (float)time);
                for (int k = 0; k < planes; k++)
                {
                    (Mat4 body, Mat4 glow) = VehiclePlacement.PlaceAircraft(aircraft[k], 1);
                    for (int i = 0; i < vertices.Length; i += 2) Check("aircraft", gpu, k, i, vertices.Length, body.Transform(vertices[i].x, vertices[i].y, vertices[i].z), true, k * vertices.Length + i);
                    for (int i = 0; i < glowVertices.Length; i++) Check("aircraft glow", glowGpu, k, i, glowVertices.Length, glow.Transform(glowVertices[i].x, glowVertices[i].y, glowVertices[i].z), true, k * glowVertices.Length + i);
                }
            }

            Debug.Log($"[Cypher] vehicle placement probe: {compared:N0} vertices compared, worst difference {worst:0.0e+0} m");
            Assert.Greater(compared, 10000);
        }
    }
}
#endif
