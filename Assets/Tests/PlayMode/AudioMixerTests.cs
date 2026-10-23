#if UNITY_EDITOR
using System;
using System.Collections;
using Cypher.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The generated mixer, loaded and played through: its parameters answer, a group's volume is heard, a send tier makes a tail and the reverb's settings and return level change it.</summary>
    public class AudioMixerTests
    {
        GameObject _listener;
        AudioTap _tap;

        [SetUp]
        public void SetUp()
        {
            _listener = new GameObject("listener");
            _listener.AddComponent<AudioListener>();
            _tap = _listener.AddComponent<AudioTap>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (AudioSource s in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) UnityEngine.Object.Destroy(s.gameObject);
            UnityEngine.Object.Destroy(_listener);
        }

        static AudioMixer LoadMixer()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/_Cypher/Audio/Cypher.mixer");
            Assert.IsNotNull(mixer, "the mixer loads (run Cypher > Write Audio Mixer)");
            return mixer;
        }

        static AudioMixerGroup Group(AudioMixer mixer, AudioBus bus, int tier = 0)
        {
            string name = AudioBuses.GroupName(bus, tier);
            AudioMixerGroup g = Array.Find(mixer.FindMatchingGroups(name), x => x.name == name);
            Assert.IsNotNull(g, name);
            return g;
        }

        static AudioClip Tone(float seconds, float amplitude, float hz = 440f)
        {
            int rate = 48000, n = (int)(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = amplitude * Mathf.Sin(2 * Mathf.PI * hz * i / rate);
            var clip = AudioClip.Create("tone", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A 50 ms burst of noise, then silence: what a reverb tail is heard against.</summary>
        static AudioClip Burst(float seconds)
        {
            var data = new float[(int)(seconds * 48000)];
            var rng = new System.Random(5);
            for (int i = 0; i < 2400; i++) data[i] = (float)(rng.NextDouble() * 2 - 1) * 0.5f * (1f - i / 2400f);
            var clip = AudioClip.Create("burst", data.Length, 1, 48000, false);
            clip.SetData(data, 0);
            return clip;
        }

        IEnumerator RequireADevice()
        {
            var probe = new GameObject("probe").AddComponent<AudioSource>();
            probe.clip = Tone(0.5f, 0.1f);
            probe.Play();
            yield return new WaitForSeconds(0.3f);
            Assume.That(_tap.Callbacks, Is.GreaterThan(0), "no audio device is running here");
            UnityEngine.Object.Destroy(probe.gameObject);
            yield return null;
        }

        [Test]
        public void TheMixerLoadsAndEveryExposedParameterTakesAndGivesValues()
        {
            AudioMixer mixer = LoadMixer();
            foreach (AudioBus bus in AudioBuses.All)
            {
                for (int tier = 0; tier < AudioBuses.TierCount; tier++)
                {
                    string p = AudioBuses.VolumeParameter(bus, tier);
                    Assert.IsTrue(mixer.SetFloat(p, -6f), p + " is exposed");
                    Assert.IsTrue(mixer.GetFloat(p, out float v), p);
                    Assert.AreEqual(-6f, v, 1e-4f, p);
                    Assert.IsNotNull(Group(mixer, bus, tier));
                }
            }
            Assert.IsTrue(mixer.SetFloat(AudioBuses.ReverbVolumeParameter, -3f));
            Assert.IsNotNull(Array.Find(mixer.FindMatchingGroups(AudioBuses.ReverbGroupName), g => g.name == AudioBuses.ReverbGroupName));
        }

        [UnityTest]
        public IEnumerator EveryReverbSettingStartsAtItsDefaultAndTakesNewValues()
        {
            // a game left running by an earlier test is still steering the reverb, and the asset keeps the last value set through it
            foreach (Cypher.Audio.AudioEngine engine in UnityEngine.Object.FindObjectsByType<Cypher.Audio.AudioEngine>(FindObjectsSortMode.None)) UnityEngine.Object.Destroy(engine);
            yield return null;
            AudioMixer mixer = LoadMixer();
            foreach (MixerReverb.Setting s in MixerReverb.Settings)
            {
                if (s.Exposed != null) Assert.IsTrue(mixer.ClearFloat(s.Exposed), s.Exposed);
            }
            yield return null;
            foreach (MixerReverb.Setting s in MixerReverb.Settings)
            {
                if (s.Exposed == null) continue;
                Assert.IsTrue(mixer.GetFloat(s.Exposed, out float v), s.Exposed);
                Assert.AreEqual((float)s.Default, v, 1e-3f, s.Exposed + " default");
                Assert.IsTrue(mixer.SetFloat(s.Exposed, (float)s.Default), s.Exposed);
            }
        }

        [UnityTest]
        public IEnumerator AGroupsVolumeIsHeardAtTheListenerToTheDecibel()
        {
            AudioMixer mixer = LoadMixer();
            yield return RequireADevice();
            var src = new GameObject("src").AddComponent<AudioSource>();
            src.clip = Tone(1f, 0.1f);
            src.loop = true;
            src.spatialBlend = 0f;
            src.outputAudioMixerGroup = Group(mixer, AudioBus.Music);
            mixer.SetFloat(AudioBuses.VolumeParameter(AudioBus.Music), 0f);
            src.Play();
            yield return new WaitForSeconds(0.5f);
            _tap.Reset();
            yield return new WaitForSeconds(0.6f);
            double at0 = _tap.Rms(2, 10);
            mixer.SetFloat(AudioBuses.VolumeParameter(AudioBus.Music), -20f);
            yield return new WaitForSeconds(0.4f);
            _tap.Reset();
            yield return new WaitForSeconds(0.6f);
            double atMinus20 = _tap.Rms(2, 10);
            Assert.Greater(at0, 0.01, "the tone is heard");
            Assert.AreEqual(0.1, atMinus20 / at0, 0.005, "-20 dB is a tenth of the level");
        }

        [UnityTest]
        public IEnumerator AGroupsSilenceIsSilence()
        {
            AudioMixer mixer = LoadMixer();
            yield return RequireADevice();
            var src = new GameObject("src").AddComponent<AudioSource>();
            src.clip = Tone(1f, 0.3f);
            src.loop = true;
            src.spatialBlend = 0f;
            src.outputAudioMixerGroup = Group(mixer, AudioBus.Ui);
            mixer.SetFloat(AudioBuses.VolumeParameter(AudioBus.Ui), (float)AudioBuses.SilenceDecibels);
            src.Play();
            yield return new WaitForSeconds(0.4f);
            _tap.Reset();
            yield return new WaitForSeconds(0.5f);
            Assert.Less(_tap.Rms(0, 10), 1e-4);
            mixer.SetFloat(AudioBuses.VolumeParameter(AudioBus.Ui), 0f);
        }

        /// <summary>Waits for an earlier sound's reverb to die away, so it cannot add to the next measurement.</summary>
        IEnumerator WaitForQuiet()
        {
            for (int i = 0; i < 80; i++)
            {
                _tap.Reset();
                yield return new WaitForSeconds(0.15f);
                if (_tap.Rms(0, 3) < 2e-6) yield break;
            }
            Assert.Fail("the mixer never went quiet");
        }

        /// <summary>Plays the burst through a bus tier and returns the direct level (the first 150 ms), the tail's level (0.1 to 1.5 s after it) and, separately, the level between 1.0 and 1.5 s.</summary>
        IEnumerator Tail(AudioMixer mixer, AudioBus bus, int tier, double[] result)
        {
            yield return WaitForQuiet();
            var src = new GameObject("src").AddComponent<AudioSource>();
            src.clip = Burst(4f);
            src.spatialBlend = 0f;
            src.outputAudioMixerGroup = Group(mixer, bus, tier);
            yield return new WaitForSeconds(0.2f);
            _tap.Reset();
            src.Play();
            yield return new WaitForSeconds(1.7f);
            result[0] = _tap.Rms(0, 3);
            result[1] = _tap.Rms(2, 30);
            result[2] = _tap.Rms(20, 30);
            UnityEngine.Object.Destroy(src.gameObject);
        }

        void OpenEverything(AudioMixer mixer)
        {
            foreach (AudioBus bus in AudioBuses.All)
            {
                for (int tier = 0; tier < AudioBuses.TierCount; tier++) mixer.SetFloat(AudioBuses.VolumeParameter(bus, tier), 0f);
            }
            mixer.SetFloat(AudioBuses.ReverbVolumeParameter, 0f);
            mixer.SetFloat("ReverbDecay", 1.75f);
        }

        [UnityTest]
        public IEnumerator ADryGroupLeavesNoTailAndAWetOneDoes()
        {
            AudioMixer mixer = LoadMixer();
            yield return RequireADevice();
            OpenEverything(mixer);
            var dry = new double[3];
            var wet = new double[3];
            yield return Tail(mixer, AudioBus.Sfx, 0, dry);
            yield return Tail(mixer, AudioBus.Sfx, AudioBuses.TierCount - 1, wet);
            Assert.Greater(dry[0], 0.05, "the burst is heard");
            Assert.Less(dry[1], 1e-5, "nothing follows a dry sound");
            Assert.Greater(wet[1], 1e-4, "a wet sound has a tail");
            Assert.AreEqual(dry[0], wet[0], dry[0] * 0.02, "the direct sound is the same either way");
        }

        [UnityTest]
        public IEnumerator HigherTiersSendMoreAtTheirStatedLevels()
        {
            AudioMixer mixer = LoadMixer();
            yield return RequireADevice();
            OpenEverything(mixer);
            var low = new double[3];
            var high = new double[3];
            const int lowTier = 2, highTier = 5;
            yield return Tail(mixer, AudioBus.Sfx, lowTier, low);
            yield return Tail(mixer, AudioBus.Sfx, highTier, high);
            double stated = AudioBuses.TierSendDecibels(highTier) - AudioBuses.TierSendDecibels(lowTier);
            double heard = 20 * Math.Log10(high[1] / low[1]);
            Assert.AreEqual(stated, heard, 2.0, "the tail rises by the difference between the tiers' sends (a sound starts somewhere in a buffer, so a window of its tail varies by about a decibel)");
        }

        [UnityTest]
        public IEnumerator TheDecayTimeSetsHowLongTheTailLasts()
        {
            AudioMixer mixer = LoadMixer();
            yield return RequireADevice();
            OpenEverything(mixer);
            int tier = AudioBuses.TierCount - 1;
            var shortTail = new double[3];
            var longTail = new double[3];
            mixer.SetFloat("ReverbDecay", 0.4f);
            yield return Tail(mixer, AudioBus.Sfx, tier, shortTail);
            mixer.SetFloat("ReverbDecay", 5f);
            yield return Tail(mixer, AudioBus.Sfx, tier, longTail);
            Assert.Less(shortTail[2], shortTail[1] * 0.02, "a short reverb has died away by a second");
            Assert.Greater(longTail[2], longTail[1] * 0.05, "a long one has not");
            mixer.SetFloat("ReverbDecay", 1.75f);
        }

        [UnityTest]
        public IEnumerator TheReverbReturnVolumeScalesTheTailAndNotTheDirectSound()
        {
            AudioMixer mixer = LoadMixer();
            yield return RequireADevice();
            OpenEverything(mixer);
            int tier = AudioBuses.TierCount - 1;
            var loud = new double[3];
            var quiet = new double[3];
            yield return Tail(mixer, AudioBus.Sfx, tier, loud);
            mixer.SetFloat(AudioBuses.ReverbVolumeParameter, -20f);
            yield return Tail(mixer, AudioBus.Sfx, tier, quiet);
            Assert.AreEqual(-20.0, 20 * Math.Log10(quiet[1] / loud[1]), 2.0, "-20 dB on the return takes 20 dB off the tail");
            Assert.AreEqual(loud[0], quiet[0], loud[0] * 0.02, "the direct sound does not change");
            mixer.SetFloat(AudioBuses.ReverbVolumeParameter, 0f);
        }
    }
}
#endif
