#if UNITY_EDITOR
using System;
using System.Collections;
using Cypher.Audio;
using Cypher.Core;
using Cypher.Game;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The game's sound in the real scene: silent but for the menu on the title, the city's bed and score from a new game, ducked when paused, gone again at the title.</summary>
    public class AudioEnginePlayTests
    {
        AudioTap _tap;

        [SetUp]
        public void SetUp()
        {
            SliceTestScene.Isolate(audio: true);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            yield return SliceTestScene.Unload();
            UiSound.Sink = null;
        }

        static AudioEngine Engine => UnityEngine.Object.FindFirstObjectByType<AudioEngine>();

        IEnumerator ToTitle()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            var listener = UnityEngine.Object.FindFirstObjectByType<AudioListener>();
            _tap = listener.gameObject.AddComponent<AudioTap>();
            yield return null;
            Assume.That(Engine != null && Engine.Active, "the audio engine did not start");
            yield return Wait(() => _tap.Callbacks > 0, 3f);
            Assume.That(_tap.Callbacks, Is.GreaterThan(0), "no audio device is running here");
        }

        static IEnumerator Wait(Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        }

        static IEnumerator Seconds(float s)
        {
            float until = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        /// <summary>The output's level over the next <paramref name="seconds"/>.</summary>
        IEnumerator Level(float seconds, double[] result)
        {
            _tap.Reset();
            yield return Seconds(0.1f);
            _tap.Reset();
            yield return Seconds(seconds);
            result[0] = _tap.Rms(0, (int)(seconds * AudioTap.BucketsPerSecond) - 1);
        }

        [UnityTest]
        public IEnumerator TheEngineStartsOnTheTitleWithTheMenuSoundsReadyAndNoWorld()
        {
            yield return ToTitle();
            AudioEngine e = Engine;
            yield return Wait(() => e.Clips.Pending == 0, 30f);
            Assert.AreEqual(0, e.Clips.Pending, "every clip was made");
            Assert.IsNull(e.Clips.Failure);
            Assert.IsFalse(e.WorldRunning, "no bed and no score on the title");
            Assert.IsNull(e.Bed);
            foreach (string sound in ClipPlan.UiSoundNames)
                for (int take = 0; take < ClipPlan.UiTakes; take++) Assert.IsTrue(e.Clips.Has(ClipPlan.UiKey(sound, take)), sound);
            Assert.AreEqual(AudioSettings.outputSampleRate, e.SampleRate);
        }

        [UnityTest]
        public IEnumerator TheTitleIsSilentUntilAMenuSoundPlays()
        {
            yield return ToTitle();
            yield return Wait(() => Engine.Clips.Pending == 0, 30f);
            var quiet = new double[1];
            yield return Level(1.0f, quiet);
            Assert.Less(quiet[0], 1e-4, "nothing plays on the title screen");

            _tap.Reset();
            UiSound.Raise(UiSound.Confirm);
            yield return Seconds(0.2f);
            Assert.Greater(Engine.Voices.Active, 0, "the menu sound has a voice");
            yield return Seconds(1.2f);
            Assert.Greater(_tap.Peak, 0.02f, "and it was heard");
            yield return Seconds(1.5f);
            Assert.AreEqual(0, Engine.Voices.Active, "and gave the voice back when it ended");
        }

        [UnityTest]
        public IEnumerator ANewGameBringsUpTheBedAndTheScoreAndTheyAreHeard()
        {
            yield return ToTitle();
            yield return Wait(() => Engine.Clips.Pending == 0, 30f);
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Seconds(0.5f);
            AudioEngine e = Engine;
            Assert.IsTrue(e.WorldRunning);
            Assert.IsNotNull(e.Bed);
            Assert.IsNotNull(e.Score);
            yield return Seconds(4f);
            var level = new double[1];
            yield return Level(1.5f, level);
            Assert.Greater(level[0], 0.003, "the city is audible");
            Assert.Greater(e.Bed.Main.Callbacks, 50, "the bed is being rendered");
            Assert.Greater(e.Score.Source.Callbacks, 50);
            Assert.Greater(e.Master.Callbacks, 50);
            Assert.IsNull(e.Bed.Failure);
            Assert.IsNull(e.Score.Failure);
            Debug.Log(e.Status());
        }

        [UnityTest]
        public IEnumerator PausingDucksTheWorldAndResumingBringsItBack()
        {
            yield return ToTitle();
            yield return Wait(() => Engine.Clips.Pending == 0, 30f);
            SliceTestScene.Flow.NewGame();
            yield return Seconds(4f);
            var playing = new double[1];
            yield return Level(1.0f, playing);
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            yield return Seconds(1.5f);
            var paused = new double[1];
            yield return Level(1.0f, paused);
            Assert.Less(paused[0], playing[0] * 0.6, "paused is much quieter");
            Assert.AreEqual(0.15, Engine.Levels.Duck(AudioBus.Ambience), 0.02);
            Assert.IsTrue(SliceTestScene.Flow.Resume());
            yield return Seconds(1.5f);
            var resumed = new double[1];
            yield return Level(1.0f, resumed);
            Assert.Greater(resumed[0], paused[0] * 1.5);
        }

        [UnityTest]
        public IEnumerator ReturningToTheTitleSilencesTheWorldAndTakesItDown()
        {
            yield return ToTitle();
            yield return Wait(() => Engine.Clips.Pending == 0, 30f);
            SliceTestScene.Flow.NewGame();
            yield return Seconds(3f);
            Assert.IsTrue(SliceTestScene.Flow.Pause());
            yield return Seconds(0.3f);
            Assert.IsTrue(SliceTestScene.Flow.ReturnToTitle());
            yield return Seconds(2.5f);
            Assert.IsFalse(Engine.WorldRunning, "the world is gone");
            var quiet = new double[1];
            yield return Level(1.0f, quiet);
            Assert.Less(quiet[0], 1e-4, "and the title is silent again");
        }

        [UnityTest]
        public IEnumerator TheOptionsSlidersSetTheMixer()
        {
            yield return ToTitle();
            SettingsService service = UnityEngine.Object.FindFirstObjectByType<SettingsService>();
            GameSettings s = service.Current;
            s.Audio.Music = 0.5;
            s.Audio.Sfx = 0.25;
            s.Audio.Master = 0.4;
            service.Apply(s);
            yield return null;
            yield return null;
            AudioEngine e = Engine;
            // the title ducks every bus but the interface to nothing, so look at the one that is not ducked
            s.Audio.Ui = 0.5;
            service.Apply(s);
            yield return null;
            yield return null;
            Assert.IsTrue(e.Mixer.TryGet(AudioBuses.VolumeParameter(AudioBus.Ui), out float ui));
            Assert.AreEqual(AudioBuses.ToDecibels(0.6 * 0.5), ui, 0.05);
            for (int tier = 0; tier < AudioBuses.TierCount; tier++)
            {
                Assert.IsTrue(e.Mixer.TryGet(AudioBuses.VolumeParameter(AudioBus.Ui, tier), out float t));
                Assert.AreEqual(ui, t, 0.05, "every send tier of a bus moves together");
            }
            Assert.AreEqual(0.9 * 0.4, e.Master.Stage.Target, 1e-9, "the master is the code stage's level");
            SliceTestScene.Flow.NewGame();
            yield return Seconds(1.0f);
            Assert.IsTrue(e.Mixer.TryGet(AudioBuses.VolumeParameter(AudioBus.Music), out float music));
            Assert.AreEqual(AudioBuses.ToDecibels(0.55 * 0.5), music, 0.3);
            Assert.IsTrue(e.Mixer.TryGet(AudioBuses.VolumeParameter(AudioBus.Sfx), out float sfx));
            Assert.AreEqual(AudioBuses.ToDecibels(0.95 * 0.25), sfx, 0.3);
        }

        [UnityTest]
        public IEnumerator TheNoAudioFlagLeavesEverythingOff()
        {
            SliceTestScene.Isolate(options: new FlowOptions(noAudio: true), audio: true);
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            yield return null;
            Assert.IsFalse(Engine.Active);
            Assert.IsNull(UiSound.Sink, "the menu makes no sound requests that go anywhere");
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Seconds(1f);
            Assert.IsFalse(Engine.WorldRunning);
            Assert.IsNull(UnityEngine.Object.FindFirstObjectByType<MasterFilter>());
        }
    }
}
#endif
