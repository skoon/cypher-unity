#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cypher.Audio;
using Cypher.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The parts of the audio layer that are not the whole game: the one-shot voice pool (priority, occlusion, placement, rolloff), the clip bank and a synthesised source heard through a mixer tier.</summary>
    public class AudioVoicesPlayTests
    {
        const int Rate = 48000;

        GameObject _root;
        AudioTap _tap;
        AudioMixer _mixer;
        MixerLevels _levels;
        OneShotVoices _voices;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // a game left running by an earlier test would still be steering the mixer
            foreach (AudioEngine engine in UnityEngine.Object.FindObjectsByType<AudioEngine>(FindObjectsSortMode.None)) UnityEngine.Object.Destroy(engine);
            yield return null;
            _mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/_Cypher/Audio/Cypher.mixer");
            Assert.IsNotNull(_mixer, "the mixer loads (run Cypher > Write Audio Mixer)");
            _root = new GameObject("voices test");
            _root.AddComponent<AudioListener>();
            _tap = _root.AddComponent<AudioTap>();
            _levels = new MixerLevels(_mixer);
            _voices = new OneShotVoices(_root.transform, _levels);
        }

        [TearDown]
        public void TearDown()
        {
            _mixer.SetFloat(AudioBuses.VolumeParameter(AudioBus.Ambience, 0), 0f);
            UnityEngine.Object.Destroy(_root);
        }

        static AudioClip Tone(float seconds, float amplitude = 0.1f, float hz = 440f)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = amplitude * Mathf.Sin(2 * Mathf.PI * hz * i / Rate);
            var clip = AudioClip.Create("tone", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static SoundDef Def(string name, int priority, bool spatial = false, AudioBus bus = AudioBus.Sfx, double send = 0.22, double reference = 2.2, double rolloff = 1.05, double gain = 0.8) =>
            new SoundDef { Name = name, Priority = priority, Spatial = spatial, Bus = bus, Send = send, RefDistance = reference, Rolloff = rolloff, Gain = gain };

        static IEnumerator Wait(Func<bool> condition, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        }

        // ---------------------------------------------------------------- the voice pool

        [UnityTest]
        public IEnumerator AFullPoolGivesAVoiceToASoundAtLeastAsImportantAndDropsALesserOne()
        {
            AudioClip clip = Tone(3f);
            SoundDef low = Def("t.low", 2), high = Def("t.high", 8), lower = Def("t.lower", 1);
            for (int i = 0; i < OneShotVoices.FlatVoices; i++) Assert.IsTrue(_voices.Play(clip, low, null, 1f, 1f, 0), "voice " + i);
            Assert.AreEqual(OneShotVoices.FlatVoices, _voices.Active);
            Assert.AreEqual(0, _voices.Stolen);

            Assert.IsTrue(_voices.Play(clip, low, null, 1f, 1f, 0), "as important as the least important: it takes that voice over");
            Assert.AreEqual(1, _voices.Stolen);
            Assert.IsTrue(_voices.Play(clip, high, null, 1f, 1f, 0));
            Assert.AreEqual(2, _voices.Stolen);
            Assert.AreEqual(OneShotVoices.FlatVoices, _voices.Active, "the pool never grows");

            Assert.IsFalse(_voices.Play(clip, lower, null, 1f, 1f, 0), "less important than every voice: dropped");
            Assert.AreEqual(1, _voices.Culled);
            Assert.IsFalse(_voices.Play(null, high, null, 1f, 1f, 0), "no clip, no voice");

            int highs = 0;
            for (int slot = 0; slot < OneShotVoices.FlatVoices; slot++) if (_voices.SoundOnVoice(false, slot) == "t.high") highs++;
            Assert.AreEqual(1, highs, "and the important sound is on a voice");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AVoiceIsGivenBackWhenItsSoundIsOver()
        {
            Assert.IsTrue(_voices.Play(Tone(0.15f), Def("t.short", 5), null, 1f, 1f, 0));
            Assert.AreEqual(1, _voices.Active);
            yield return Wait(() => { _voices.Update(); return _voices.Active == 0; }, 2f);
            Assert.AreEqual(0, _voices.Active);
            Assert.IsNull(_voices.SoundOnVoice(false, 0));
            Assert.IsFalse(_voices.SourceOf(false, 0).isPlaying);
            Assert.IsTrue(_voices.Play(Tone(0.15f), Def("t.short", 5), null, 1f, 1f, 0), "and the voice can be used again");
        }

        [UnityTest]
        public IEnumerator AHigherPitchEndsTheSoundSoonerAndALowerOneLater()
        {
            AudioClip clip = Tone(0.4f);
            Assert.IsTrue(_voices.Play(clip, Def("t.fast", 5), null, 1f, 2f, 0));
            Assert.IsTrue(_voices.Play(clip, Def("t.slow", 5), null, 1f, 0.5f, 0));
            yield return Wait(() => { _voices.Update(); return _voices.Active == 1; }, 2f);
            Assert.AreEqual("t.slow", _voices.SoundOnVoice(false, 1), "the half-speed sound is still going when the double-speed one is done");
            Assert.IsNull(_voices.SoundOnVoice(false, 0));
        }

        [UnityTest]
        public IEnumerator OcclusionMufflesAndQuietsAVoiceAndAClearSoundIsLeftAlone()
        {
            SoundDef def = Def("t.occluded", 5, gain: 0.8);
            Assert.IsTrue(_voices.Play(Tone(1f), def, null, 0.5f, 1f, 0.8));
            AudioSource source = _voices.SourceOf(false, 0);
            AudioLowPassFilter filter = source.GetComponent<AudioLowPassFilter>();
            Assert.IsTrue(filter.enabled);
            Assert.AreEqual((float)Occlusion.CutoffHz(0.8), filter.cutoffFrequency, 1f);
            Assert.AreEqual((float)(0.8 * 0.5 * Occlusion.Gain(0.8)), source.volume, 1e-5f);

            Assert.IsTrue(_voices.Play(Tone(1f), def, null, 0.5f, 1f, 0));
            AudioSource clear = _voices.SourceOf(false, 1);
            Assert.IsFalse(clear.GetComponent<AudioLowPassFilter>().enabled, "nothing in the way, no filter");
            Assert.AreEqual(0.8f * 0.5f, clear.volume, 1e-5f);

            Assert.IsTrue(_voices.Play(Tone(1f), def, null, 0.5f, 1f, 7.0), "an occlusion past the end is held to full");
            Assert.AreEqual((float)Occlusion.CutoffHz(1), _voices.SourceOf(false, 2).GetComponent<AudioLowPassFilter>().cutoffFrequency, 1f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ASoundWithAPlaceIsPlacedThereAndFallsOffLikeWebAudiosInverseModel()
        {
            SoundDef def = Def("t.placed", 5, spatial: true, reference: 2.2, rolloff: 1.05);
            var at = new Vector3(30f, 2f, -12f);
            Assert.IsTrue(_voices.Play(Tone(1f), def, at, 1f, 1f, 0));
            AudioSource source = _voices.SourceOf(true, 0);
            Assert.AreEqual(1f, source.spatialBlend);
            Assert.AreEqual(at, source.transform.position);
            Assert.AreEqual(AudioRolloffMode.Custom, source.rolloffMode);
            AnimationCurve curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            foreach (double d in new[] { 0.5, 1.0, 2.2, 5.0, 20.0, 60.0, 150.0 })
            {
                double expected = RolloffCurves.Gain(d, 2.2, 1.05);
                Assert.AreEqual(expected, curve.Evaluate((float)(d / RolloffCurves.MaxDistance)), 0.02 + 0.03 * expected, $"at {d} m");
            }
            Assert.AreEqual(1.0, curve.Evaluate(0.2f / RolloffCurves.MaxDistance), 0.01, "full level inside the reference distance");

            Assert.IsTrue(_voices.Play(Tone(1f), def, null, 1f, 1f, 0), "a sound without a place is head-locked even if it could be placed");
            Assert.AreEqual(0f, _voices.SourceOf(false, 0).spatialBlend);
            Assert.IsTrue(_voices.Play(Tone(1f), Def("t.ui", 5, spatial: false), at, 1f, 1f, 0), "and a menu sound is never placed");
            Assert.AreEqual(3, _voices.Active, "one placed voice and two head-locked");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EachVoiceIsHeardThroughTheGroupOfItsBusAndReverbSendAndABusCanBeOverridden()
        {
            SoundDef def = Def("t.routed", 5, bus: AudioBus.Ambience, send: 0.22);
            Assert.IsTrue(_voices.Play(Tone(1f), def, null, 1f, 1f, 0));
            Assert.AreEqual(_levels.Group(AudioBus.Ambience, AudioBuses.TierFor(0.22)), _voices.SourceOf(false, 0).outputAudioMixerGroup);
            Assert.IsTrue(_voices.Play(Tone(1f), def, null, 1f, 1f, 0, busOverride: AudioBus.Voice));
            Assert.AreEqual(_levels.Group(AudioBus.Voice, AudioBuses.TierFor(0.22)), _voices.SourceOf(false, 1).outputAudioMixerGroup);
            Assert.IsTrue(_voices.Play(Tone(1f), Def("t.dry", 5, bus: AudioBus.Ui, send: 0), null, 1f, 1f, 0));
            Assert.AreEqual(_levels.Group(AudioBus.Ui, 0), _voices.SourceOf(false, 2).outputAudioMixerGroup, "no send, the dry group");
            yield return null;
        }

        [UnityTest]
        public IEnumerator AScheduledSoundWaitsForItsMomentOnTheAudioClock()
        {
            double begin = AudioSettings.dspTime + 0.5;
            Assert.IsTrue(_voices.Play(Tone(0.3f), Def("t.later", 5), null, 1f, 1f, 0, startDsp: begin));
            Assert.AreEqual(1, _voices.Active, "it holds a voice from the moment it is scheduled");
            _voices.Update();
            Assert.AreEqual(1, _voices.Active, "and keeps it while it waits");
            yield return Wait(() => AudioSettings.dspTime > begin + 0.2, 3f);
            Assert.IsTrue(_voices.SourceOf(false, 0).isPlaying);
            yield return Wait(() => { _voices.Update(); return _voices.Active == 0; }, 3f);
            Assert.AreEqual(0, _voices.Active);
        }

        [UnityTest]
        public IEnumerator StoppingEverythingEmptiesThePoolAndSurvivesSourcesAlreadyDestroyed()
        {
            for (int i = 0; i < 5; i++) _voices.Play(Tone(1f), Def("t.many", 5), null, 1f, 1f, 0);
            Assert.AreEqual(5, _voices.Active);
            _voices.StopAll();
            Assert.AreEqual(0, _voices.Active);
            Assert.IsFalse(_voices.SourceOf(false, 0).isPlaying);

            UnityEngine.Object.Destroy(_root);
            yield return null;
            Assert.DoesNotThrow(() => _voices.StopAll(), "a scene unloading takes the sources before the engine's own teardown asks them to stop");
            _root = new GameObject("replacement");
        }

        // ---------------------------------------------------------------- the clip bank

        static IEnumerator Fill(AudioClipBank bank, float seconds = 60f)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (bank.Pending > 0 && bank.Failure == null && Time.realtimeSinceStartup < until)
            {
                bank.Pump(8);
                yield return null;
            }
            bank.Pump(1000);
        }

        [UnityTest]
        public IEnumerator TheSameSpecsMakeTheSameSamplesWhateverTheNumberOfWorkers()
        {
            var noise = new AudioNoiseBank(Rate);
            List<ClipSpec> specs = ClipPlan.Ui().Take(6).Concat(ClipPlan.Ambient().Take(2)).ToList();
            var a = new AudioClipBank(Rate, noise, 3);
            var b = new AudioClipBank(Rate, noise, 1);
            try
            {
                a.Enqueue(specs);
                b.Enqueue(specs);
                Assert.AreEqual(specs.Count, a.Pending);
                yield return Fill(a);
                yield return Fill(b);
                Assert.IsNull(a.Failure);
                Assert.IsNull(b.Failure);
                Assert.AreEqual(0, a.Pending);
                Assert.AreEqual(0, b.Pending);
                Assert.AreEqual(specs.Count, a.Count);
                foreach (ClipSpec spec in specs)
                {
                    Assert.IsTrue(a.Has(spec.Key), spec.Key);
                    CollectionAssert.AreEqual(a.SoundOf(spec.Key).Samples, b.SoundOf(spec.Key).Samples, spec.Key + " is the same sound every time");
                    AudioClip clip = a.Get(spec.Key);
                    Assert.AreEqual(a.SoundOf(spec.Key).Samples.Length, clip.samples, spec.Key);
                    Assert.AreEqual(Rate, clip.frequency);
                    Assert.AreEqual(1, clip.channels);
                }
                Assert.AreEqual(a.Samples, b.Samples);
                Assert.Greater(a.RenderSeconds, 0);
                Assert.IsNull(a.Get("never made"));
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator ASoundThatCannotBeMadeIsReportedWithItsKeyAndTheRestAreStillMade()
        {
            var noise = new AudioNoiseBank(Rate);
            var bank = new AudioClipBank(Rate, noise, 1);
            try
            {
                var bad = new ClipSpec { Key = "bad|0", Sound = "no.such.sound", Seed = 1, Params = new VoiceParams() };
                ClipSpec good = ClipPlan.Ui().First();
                bank.Enqueue(new[] { bad, good });
                float until = Time.realtimeSinceStartup + 30f;
                while ((bank.Failure == null || !bank.Has(good.Key)) && Time.realtimeSinceStartup < until)
                {
                    bank.Pump(8);
                    yield return null;
                }
                Assert.IsNotNull(bank.Failure, "a recipe that fails is not swallowed");
                StringAssert.Contains("bad|0", bank.Failure.Message);
                Assert.IsFalse(bank.Has("bad|0"));
                Assert.IsTrue(bank.Has(good.Key), "the others carry on");
            }
            finally
            {
                bank.Dispose();
            }
        }

        [Test]
        public void ABankRefusesNoiseBedsForAnotherSampleRate()
        {
            Assert.Throws<ArgumentException>(() => new AudioClipBank(44100, new AudioNoiseBank(Rate)));
        }

        // ---------------------------------------------------------------- a synthesised source

        [UnityTest]
        public IEnumerator ASynthesisedSourceIsHeardThroughItsMixerGroupToTheDecibel()
        {
            yield return Wait(() => _tap.Callbacks > 0, 3f);
            Assume.That(_tap.Callbacks, Is.GreaterThan(0), "no audio device is running here");
            int rate = AudioSettings.outputSampleRate;
            var graph = new SynthGraph(rate);
            OscillatorNode tone = graph.CreateOscillator(WaveKind.Sine, 440);
            tone.Start(0);
            tone.Connect(graph.CreateGain(0.1)).Connect(graph.Destination);
            string volume = AudioBuses.VolumeParameter(AudioBus.Ambience, 0);
            _mixer.SetFloat(volume, 0f);
            SynthSource source = SynthSource.Create("tone", _root.transform, graph, 1, _levels.Group(AudioBus.Ambience, 0), spatial: false);
            source.Begin();
            yield return new WaitForSeconds(0.5f);
            _tap.Reset();
            yield return new WaitForSeconds(0.6f);
            double at0 = _tap.Rms(2, 10);
            _mixer.SetFloat(volume, -20f);
            yield return new WaitForSeconds(0.4f);
            _tap.Reset();
            yield return new WaitForSeconds(0.6f);
            double atMinus20 = _tap.Rms(2, 10);
            Assert.Greater(source.Callbacks, 20, "the graph is being pulled");
            Assert.IsNull(source.Failure);
            Assert.That(at0, Is.InRange(0.04, 0.1), "a sine of amplitude 0.1 is heard at about its own level");
            Assert.AreEqual(0.1, atMinus20 / at0, 0.005, "-20 dB is a tenth of the level");
            source.Halt();
        }
    }
}
#endif
