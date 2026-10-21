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

            // Stop the player controller driving the camera, then let frames pass so the probe, rain and
            // post effects settle. The first frames after a city build can draw with textures not yet uploaded.
            var controller = FindFirstObjectByType<FirstPersonController>();
            if (controller != null) controller.enabled = false;

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

            foreach (CapturePoses.Pose p in CapturePoses.All)
            {
                cam.transform.SetPositionAndRotation(p.Position, Quaternion.Euler(p.Pitch, p.Yaw, 0f));
                yield return null;
                yield return null;
                SubmitAndSave(cam, rt, Path.Combine(dir, p.Name + ".png"));
            }

            Destroy(rt);
        }

        static void SubmitAndSave(Camera cam, RenderTexture rt, string path)
        {
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
