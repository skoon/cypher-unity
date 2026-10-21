using System;
using Cypher.Capture;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cypher.EditorTools
{
    /// <summary>
    /// Renders fixed poses of the Slice scene to PNGs so changes can be checked visually without a person
    /// playing. It enters Play mode and lets <see cref="SliceCaptureDriver"/> render inside the running
    /// game, so the images show the real runtime result. Needs a real GPU, so run WITHOUT -nographics:
    ///   Unity.exe -batchmode -projectPath ... -executeMethod Cypher.EditorTools.SliceCapture.Run -captureDir DIR
    /// Add -captureScreenHeight 1080 to render as a 1080p window would: a 540p internal image, point-upscaled.
    /// Without it the output is the internal image itself (960x540). Poses are in <see cref="CapturePoses"/>.
    /// </summary>
    [InitializeOnLoad]
    public static class SliceCapture
    {
        const string PendingKey = "Cypher.Capture.Pending";

        // Registered after every domain reload; with domain reload on play disabled it stays registered.
        static SliceCapture()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>Command-line entry point.</summary>
        public static void Run()
        {
            string dir = Arg("-captureDir");
            if (string.IsNullOrEmpty(dir))
            {
                Debug.LogError("[Cypher] missing -captureDir <dir>");
                EditorApplication.Exit(1);
                return;
            }

            SessionState.SetString(CapturePoses.DirKey, dir);
            SessionState.SetInt(CapturePoses.ScreenHeightKey, ArgInt("-captureScreenHeight"));
            SessionState.SetBool(PendingKey, true);
            EditorSceneManager.OpenScene(SliceSceneBuilder.ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false);
            new GameObject("SliceCaptureDriver").AddComponent<SliceCaptureDriver>();
        }

        static int ArgInt(string name) => int.TryParse(Arg(name), out int v) ? v : 0;

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
