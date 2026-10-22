#if UNITY_EDITOR
using System.Collections;
using Cypher.Core;
using Cypher.Game;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cypher.Tests
{
    /// <summary>Loads the saved Slice scene for tests, with the player's real settings file and the command line kept out of it.</summary>
    static class SliceTestScene
    {
        public const string ScenePath = "Assets/Scenes/Slice.unity";

        /// <summary>Call from SetUp. Settings live in memory; <paramref name="storage"/> lets a test preload or inspect them.</summary>
        public static void Isolate(ISettingsStorage storage = null, FlowOptions options = null)
        {
            SettingsService.StorageFactory = () => storage ?? new MemorySettingsStorage();
            LaunchOptions.Override = options ?? new FlowOptions();
        }

        /// <summary>Call from TearDown.</summary>
        public static void Restore()
        {
            SettingsService.StorageFactory = null;
            LaunchOptions.Override = null;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public static IEnumerator Load()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
        }

        public static GameFlow Flow => Object.FindFirstObjectByType<GameFlow>();

        public static IEnumerator WaitFor(FlowState state, float seconds = 30f)
        {
            GameFlow flow = Flow;
            float deadline = Time.realtimeSinceStartup + seconds;
            while (flow.State != state && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.AreEqual(state, flow.State, $"the flow never reached {state}");
        }

        /// <summary>Load the scene, wait out loading and press New Game.</summary>
        public static IEnumerator LoadAndPlay()
        {
            yield return Load();
            yield return WaitFor(FlowState.Title);
            Assert.IsTrue(Flow.NewGame());
            yield return null;
        }
    }
}
#endif
