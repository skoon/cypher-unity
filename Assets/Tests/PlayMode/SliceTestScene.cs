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

        /// <summary>
        /// Call from SetUp. Settings live in memory; <paramref name="storage"/> lets a test preload or inspect them. The sound is off unless <paramref name="audio"/> is set, so the many tests that
        /// have nothing to do with it neither pay for it nor depend on a sound device. Saved games live in <paramref name="saves"/>, or in a fresh memory store, so no test
        /// touches the player's saves.
        /// </summary>
        public static void Isolate(ISettingsStorage storage = null, FlowOptions options = null, bool audio = false, ISaveStorage saves = null)
        {
            SettingsService.StorageFactory = () => storage ?? new MemorySettingsStorage();
            ISaveStorage saveStorage = saves ?? new MemorySaveStorage();
            Cypher.Rpg.RpgService.StorageFactory = () => saveStorage;
            FlowOptions o = options ?? new FlowOptions();
            if (!audio && !o.NoAudio) o = new FlowOptions(o.SkipTitle, o.IgnoreUserSettings, o.NoAutoPause, o.NoCrowd, o.NoTraffic, o.NoVehicleLights, noAudio: true);
            LaunchOptions.Override = o;
        }

        /// <summary>Call from TearDown.</summary>
        public static void Restore()
        {
            SettingsService.StorageFactory = null;
            Cypher.Rpg.RpgService.StorageFactory = null;
            LaunchOptions.Override = null;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        static int _emptyScenes;

        /// <summary>
        /// For a [UnityTearDown]: unload the city a test loaded and leave an empty scene, so the next fixture builds its own in a clear one (the real city's props, colliders and controllers would be in its way).
        /// </summary>
        public static IEnumerator Unload()
        {
            Scene slice = SceneManager.GetSceneByPath(ScenePath);
            if (slice.IsValid() && slice.isLoaded)
            {
                Scene empty = SceneManager.CreateScene("AfterASliceTest" + ++_emptyScenes);
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(slice);
            }
            Restore();
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
