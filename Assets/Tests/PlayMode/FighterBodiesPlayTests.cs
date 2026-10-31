#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cypher.Actors;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The fighters drawn with the crowd: in its runs and draws, every pose on screen, and a picture of the line-up for a person to look at.</summary>
    public class FighterBodiesPlayTests
    {
        [SetUp]
        public void SetUp() => SliceTestScene.Isolate();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
        }

        static CrowdController Crowd => UnityEngine.Object.FindFirstObjectByType<CrowdController>();

        static bool HasGpu => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>Where the pictures go: the project's Logs folder, beside the test results.</summary>
        static string ShotDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "shots_m5_bodies"));

        static CombatBody Dressed(int id, EnemyArchetype kind, double x, double y, double z, double heading)
        {
            var sim = new EnemySim(1);
            Enemy e = sim.Add(sim.AddSquad("jackal"), kind, new V3(x, y, z), heading);
            CombatBody b = EnemyLook.Dress(e, 31);
            var placed = new CombatBody(id, b.Appearance, b.Visor, b.Weapon, b.LookSeed)
            {
                X = x, PrevX = x, Y = y, PrevY = y, Z = z, PrevZ = z, Heading = heading, PrevHeading = heading, Phase = 1, PrevPhase = 1,
            };
            return placed;
        }

        [UnityTest]
        public IEnumerator EveryPoseIsDrawnInTheCrowdsOwnDrawsAndPictured()
        {
            if (!HasGpu) Assert.Ignore("no graphics device");
            yield return SliceTestScene.LoadAndPlay();
            yield return null;
            yield return null;
            CrowdController crowd = Crowd;
            Assert.IsTrue(crowd.IsActive);
            Camera cam = Camera.main;

            // nobody else in the picture
            Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
            fwd.y = 0; fwd.Normalize();
            for (int i = 0; i < crowd.Sim.Active.Count; i++)
            {
                Pedestrian p = crowd.Sim.Active[i];
                p.X = p.PrevX = eye.x - fwd.x * 60 + i * 0.01; p.Z = p.PrevZ = eye.z - fwd.z * 60;
                p.State = PedState.Idle; p.Timer = 1e6; p.Speed = 0;
            }
            yield return null;
            int drawsWithoutFighters = crowd.Renderer.DrawCalls;

            // a line-up across the view, six metres out, facing the camera
            var right = new Vector3(fwd.z, 0, -fwd.x);
            string[] names = { "carry", "aim", "aim_up", "crouch", "crouch_aim", "fire", "flinch", "falling", "dead", "heavy_aim" };
            var fighters = new List<CombatBody>();
            for (int k = 0; k < names.Length; k++)
            {
                Vector3 at = eye + fwd * 7f + right * ((k - (names.Length - 1) / 2f) * 1.5f);
                double y = crowd.Sim.Nav.GroundY(at.x, at.z);
                double heading = Math.Atan2(eye.x - at.x, eye.z - at.z);
                CombatBody b = Dressed(k + 1, names[k] == "heavy_aim" ? EnemyTable.Heavy : EnemyTable.Gunner, at.x, y, at.z, heading);
                switch (names[k])
                {
                    case "aim": b.Aim = 1; break;
                    case "aim_up": b.Aim = 1; b.AimPitch = 0.35; break;
                    case "crouch": b.Crouch = 1; b.Aim = 0.7; break;
                    case "crouch_aim": b.Crouch = 1; b.Aim = 1; break;
                    case "fire": b.Aim = 1; b.Recoil = 1; break;
                    case "flinch": b.Aim = 0.7; b.Flinch = 1; b.Side = 1; break;
                    case "falling": b.Death = 0.5; b.Side = -0.6; break;
                    case "dead": b.Death = 1; b.Side = 0.4; break;
                    case "heavy_aim": b.Aim = 1; break;
                }
                fighters.Add(b);
            }
            crowd.Fighters = fighters;
            yield return null;
            yield return null;

            CrowdFrame frame = crowd.Renderer.Frame;
            PedestrianGpuData[] drawn = frame.Data.Take(frame.Total).Where(d => d.Weapon != 0).ToArray();
            Assert.AreEqual(names.Length, drawn.Length, "every fighter is in the frame");
            Assert.IsTrue(frame.Data.Take(frame.Count(RigLod.Full)).Count(d => d.Weapon != 0) == names.Length, "at seven metres, all as the full rig");
            Assert.LessOrEqual(crowd.Renderer.DrawCalls, Math.Max(drawsWithoutFighters, 2), "no draws of their own");

            Directory.CreateDirectory(ShotDir);
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            try
            {
                // look a little down at the line, from a little closer
                Vector3 mid = eye + fwd * 7f;
                mid.y = (float)crowd.Sim.Nav.GroundY(mid.x, mid.z) + 0.9f;
                Vector3 from = mid - fwd * 9.5f + Vector3.up * 0.5f;
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(mid - from));   // no frame between: the player would put the camera back
                string path = Path.Combine(ShotDir, "lineup.png");
                Save(crowd, cam, rt, path);
                Assert.IsTrue(File.Exists(path));

                // and side on, facing right along the line, where the arms and the gun show
                foreach (CombatBody b in fighters)
                {
                    b.Heading = b.PrevHeading = Math.Atan2(right.x, right.z);
                }
                Save(crowd, cam, rt, Path.Combine(ShotDir, "lineup_side.png"));
                Debug.Log($"[Cypher] fighters line-up: {path} ({string.Join(", ", names)}, left to right)");
            }
            finally
            {
                crowd.Fighters = null;
                UnityEngine.Object.Destroy(rt);
            }
        }

        static void Save(CrowdController crowd, Camera cam, RenderTexture rt, string path)
        {
            crowd.DrawNow();   // queued for this hand-rendered camera too
            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(cam, request)) throw new InvalidOperationException("the render pipeline does not support StandardRequest");
            RenderPipeline.SubmitRenderRequest(cam, request);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }
    }
}
#endif
