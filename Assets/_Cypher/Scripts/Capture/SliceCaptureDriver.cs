#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using Cypher.Player;
using Cypher.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cypher.Capture
{
    /// <summary>
    /// Runs inside Play mode (started by the Editor's SliceCapture): waits for the scene to settle, then
    /// renders each pose to a PNG through the real pipeline, so screenshots include everything the game
    /// does at runtime: low-res scaling, reflection probe, lighting, rain. Lives in a runtime assembly
    /// that only compiles in the Editor, because Unity will not attach Editor-assembly scripts.
    /// </summary>
    public sealed class SliceCaptureDriver : MonoBehaviour
    {
        const int Width = 960;
        const int Height = 540;
        const float SettleTimeout = 15f;

        /// <summary>A coroutine that throws just stops, which would hang a batch run, so give up after this long.</summary>
        const float WatchdogSeconds = 180f;

        float _started;

        IEnumerator Start()
        {
            _started = Time.realtimeSinceStartup;
            yield return Capture();
            EditorApplication.Exit(0);
        }

        void Update()
        {
            if (Time.realtimeSinceStartup - _started > WatchdogSeconds)
            {
                Debug.LogError("[Cypher] capture watchdog expired");
                EditorApplication.Exit(1);
            }
        }

        static void Fail(string message)
        {
            Debug.LogError("[Cypher] " + message);
            EditorApplication.Exit(1);
        }

        IEnumerator Capture()
        {
            string dir = SessionState.GetString(CapturePoses.DirKey, "");
            int screenHeight = SessionState.GetInt(CapturePoses.ScreenHeightKey, 0);
            if (string.IsNullOrEmpty(dir)) { Fail("capture driver started without a capture directory"); yield break; }
            Directory.CreateDirectory(dir);

            Camera cam = Camera.main;
            if (cam == null) { Fail("no MainCamera in the Slice scene"); yield break; }

            // Wait for loading to finish and start a game, so the camera rides the player and not the title flight.
            var flow = FindFirstObjectByType<Cypher.Game.GameFlow>();
            if (flow == null) { Fail("no GameFlow in the Slice scene"); yield break; }
            float loadDeadline = Time.realtimeSinceStartup + SettleTimeout * 2;
            while (flow.State != Cypher.Core.FlowState.Title && Time.realtimeSinceStartup < loadDeadline) yield return null;
            if (!flow.NewGame()) { Fail($"the game never reached the title screen (state {flow.State})"); yield break; }

            // Stop the player controller driving the camera, then let frames pass so the probe, rain and
            // post effects settle. The first frames after a city build can draw with textures not yet uploaded.
            var controller = FindFirstObjectByType<FirstPersonController>();
            if (controller != null) controller.enabled = false;

            // The capture sets its own resolution; the quality governor must not change it underneath.
            var quality = FindFirstObjectByType<QualityDirector>();
            if (quality != null) quality.enabled = false;

            var probe = FindFirstObjectByType<StreetReflectionProbe>();
            float deadline = Time.realtimeSinceStartup + SettleTimeout;
            for (int i = 0; i < 5; i++) yield return null;
            while (probe != null && !probe.IsCaptured && Time.realtimeSinceStartup < deadline) yield return null;
            if (probe != null && !probe.IsCaptured) { Fail("the reflection probe did not finish rendering"); yield break; }
            for (int i = 0; i < 5; i++) yield return null;

            int width = Width, height = Height;
            var lowRes = FindFirstObjectByType<LowResController>();
            if (lowRes != null)
            {
                if (screenHeight > 0)
                {
                    // Emulate a window screenHeight tall: the controller derives its scale from the real Screen.height.
                    lowRes.TargetHeight = Mathf.Max(1, Mathf.RoundToInt(Screen.height * (float)LowResController.DefaultTargetHeight / screenHeight));
                    height = screenHeight;
                    width = Mathf.RoundToInt(screenHeight * 16f / 9f);
                    Debug.Log($"[Cypher] low-res capture: {width}x{height} window, render scale {lowRes.CurrentScale:0.###}");
                }
                else
                {
                    lowRes.TargetHeight = Screen.height;   // scale 1: render at the target size directly
                }
            }

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            rt.Create();

            var dressing = FindFirstObjectByType<Cypher.City.PropDressing>();
            foreach (CapturePoses.Pose p in CapturePoses.All)
            {
                cam.transform.SetPositionAndRotation(p.Position, Quaternion.Euler(p.Pitch, p.Yaw, 0f));
                if (dressing != null) dressing.Refresh();   // the props of a far block are only switched on when the camera is near
                yield return null;
                yield return null;
                SubmitAndSave(cam, rt, Path.Combine(dir, p.Name + ".png"));
            }

            // The signage: the hoarding, hologram, shop sign, plaque and blade nearest the plaza, each from a spot in front of it.
            var city = FindFirstObjectByType<Cypher.City.CityBuilder>();
            if (city != null && city.Slice != null)
            {
                Cypher.Core.NeonAtlasLayout atlas = Cypher.Core.NeonAtlas.Layout();
                Cypher.Core.NeonLayout signs = Cypher.Core.NeonPlanner.Plan(city.Slice.Plan, city.Slice.Window, Cypher.Core.NeonPlanner.DefaultSeed, atlas);
                foreach (Cypher.Core.SignShot shot in Cypher.Core.SignVantage.Pick(signs, atlas, city.Slice.Plan.IsBlocked))
                {
                    (double cx, double cy, double cz) = shot.CameraPosition(1.7);
                    var from = new Vector3((float)cx, (float)cy, (float)cz);
                    var target = new Vector3((float)shot.X, (float)shot.Y, (float)shot.Z);
                    cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                    if (dressing != null) dressing.Refresh();
                    yield return null;
                    yield return null;
                    SubmitAndSave(cam, rt, Path.Combine(dir, shot.Name + ".png"));
                }
            }

            // The people: close to the nearest and to one of each kind (umbrella, long coat, hood, pack and trim), then a view of a whole street of them.
            var crowd = FindFirstObjectByType<Cypher.Actors.CrowdController>();
            if (crowd != null && crowd.IsActive)
            {
                yield return new WaitForSecondsRealtime(1.5f);   // let the crowd walk about a little
                Vector3 feet = FindFirstObjectByType<FirstPersonController>().transform.position;
                foreach (Cypher.Core.PersonShot shot in Cypher.Core.CrowdVantage.Pick(crowd.Sim, feet.x, feet.z, range: 400))
                {
                    var from = new Vector3((float)shot.CamX, (float)shot.CamY, (float)shot.CamZ);
                    var target = new Vector3((float)shot.TargetX, (float)shot.TargetY, (float)shot.TargetZ);
                    cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                    yield return null;
                    yield return null;
                    SubmitAndSave(cam, rt, Path.Combine(dir, shot.Name + ".png"));
                }
                Debug.Log($"[Cypher] crowd: {crowd.Sim.Active.Count} active, {crowd.Renderer.Frame.Total} drawn ({crowd.Renderer.Frame.Count(Cypher.Core.RigLod.Full)} full, {crowd.Renderer.Frame.Count(Cypher.Core.RigLod.Simple)} simple, {crowd.Renderer.Frame.Count(Cypher.Core.RigLod.Silhouette)} silhouettes), {crowd.Renderer.DrawCalls} draw calls");
            }

            // The traffic: close to the nearest vehicle of each kind, to an aircraft of each, and at a junction with its signals; the signals are looked at in both of their phases.
            var traffic = FindFirstObjectByType<Cypher.Actors.TrafficController>();
            if (traffic != null && traffic.IsActive)
            {
                Vector3 feet = FindFirstObjectByType<FirstPersonController>().transform.position;
                foreach (Cypher.Core.TrafficShot shot in Cypher.Core.TrafficVantage.Pick(traffic.Sim, feet.x, feet.z, range: 400))
                {
                    var from = new Vector3((float)shot.CamX, (float)shot.CamY, (float)shot.CamZ);
                    var target = new Vector3((float)shot.TargetX, (float)shot.TargetY, (float)shot.TargetZ);
                    cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                    yield return null;
                    yield return null;
                    SubmitAndSave(cam, rt, Path.Combine(dir, shot.Name + ".png"));
                }

                // the same junction when the other axis has green, wherever the cycle is now: wait for it to change
                (int ix, int iz) = Cypher.Core.TrafficVantage.NearestJunction(traffic.Sim.Nav, feet.x, feet.z);
                Cypher.Core.TrafficShot junction = Cypher.Core.TrafficVantage.Junction("junction_signals_other", traffic.Sim.Nav, ix, iz);
                int axis = traffic.Sim.Signal.GreenAxis;
                float change = Time.realtimeSinceStartup + 20f;
                while (traffic.Sim.Signal.GreenAxis == axis && Time.realtimeSinceStartup < change) yield return null;
                cam.transform.SetPositionAndRotation(new Vector3((float)junction.CamX, (float)junction.CamY, (float)junction.CamZ),
                    Quaternion.LookRotation(new Vector3((float)junction.TargetX, (float)junction.TargetY, (float)junction.TargetZ) - new Vector3((float)junction.CamX, (float)junction.CamY, (float)junction.CamZ)));
                yield return null;
                yield return null;
                SubmitAndSave(cam, rt, Path.Combine(dir, junction.Name + ".png"));
                Debug.Log($"[Cypher] traffic: {traffic.Sim.Vehicles.Count} vehicles and {traffic.Sim.Airborne.Count} aircraft, {traffic.Renderer.Frame.GroundTotal} and {traffic.Renderer.Frame.AircraftTotal} drawn, {traffic.Renderer.DrawCalls} draw calls, {traffic.Lights.Lit} lights lit");
            }

            // The steam: stand a few metres from where the first puff starts, looking at it, and take three pictures a second and a half
            // apart (a puff is invisible when it is born and when it dies, so one picture can miss it).
            if (city != null && city.Props != null && city.Props.Layout.Puffs.Count > 0)
            {
                Cypher.Core.SteamPuff puff = city.Props.Layout.Puffs[0];
                var target = new Vector3((float)puff.X, (float)puff.Y + 1.2f, (float)puff.Z);
                Vector3 from = target + new Vector3(3.2f, -0.6f, 3.2f);
                from.y = Mathf.Max(from.y, 1.7f);
                cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
                if (dressing != null) dressing.Refresh();
                for (int shot = 1; shot <= 3; shot++)
                {
                    yield return new WaitForSecondsRealtime(1.5f);
                    SubmitAndSave(cam, rt, Path.Combine(dir, $"steam_{shot}.png"));
                }
            }

            // Back to the title through the pause screen, which also exercises the way out of a game, then a few points of its flight.
            if (!flow.Pause() || !flow.ReturnToTitle()) { Fail($"could not return to the title (state {flow.State})"); yield break; }
            var titleCamera = FindFirstObjectByType<Cypher.Game.TitleCamera>();
            foreach (int seconds in new[] { 0, 20, 45, 70 })
            {
                titleCamera.SeekTo(seconds);
                yield return null;
                yield return null;
                SubmitAndSave(cam, rt, Path.Combine(dir, $"title_{seconds:00}.png"));
            }

            Destroy(rt);
        }

        static void SubmitAndSave(Camera cam, RenderTexture rt, string path)
        {
            // the people and the traffic are queued for the frame's cameras in LateUpdate; this camera renders by hand, in Update, so queue them again just before
            var crowd = FindFirstObjectByType<Cypher.Actors.CrowdController>();
            if (crowd != null && crowd.IsActive) crowd.DrawNow();
            var traffic = FindFirstObjectByType<Cypher.Actors.TrafficController>();
            if (traffic != null && traffic.IsActive) traffic.DrawNow();

            var request = new RenderPipeline.StandardRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(cam, request))
            {
                throw new InvalidOperationException("the active render pipeline does not support StandardRequest");
            }
            RenderPipeline.SubmitRenderRequest(cam, request);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Debug.Log($"[Cypher] captured {path}");
        }
    }
}
#endif
