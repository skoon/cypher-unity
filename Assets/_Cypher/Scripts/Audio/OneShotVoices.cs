using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace Cypher.Audio
{
    /// <summary>The fall of loudness with distance that Web Audio's "inverse" model gives, as a Unity rolloff curve (x is distance over the source's maximum distance).</summary>
    public static class RolloffCurves
    {
        public const float MaxDistance = 200f;
        const int Points = 48;

        static readonly Dictionary<(float, float), AnimationCurve> Cache = new Dictionary<(float, float), AnimationCurve>();

        /// <summary>The inverse model: a sound is full level up to the reference distance, then refDistance / (refDistance + rolloff * (distance - refDistance)).</summary>
        public static double Gain(double distance, double reference, double rolloff)
        {
            double d = Math.Max(distance, reference);
            return reference / (reference + rolloff * (d - reference));
        }

        public static AnimationCurve For(double reference, double rolloff)
        {
            var key = ((float)reference, (float)rolloff);
            if (Cache.TryGetValue(key, out AnimationCurve curve)) return curve;
            var keys = new Keyframe[Points];
            for (int i = 0; i < Points; i++)
            {
                // closer points near the source, where the curve bends
                double u = (double)i / (Points - 1);
                double distance = MaxDistance * u * u;
                keys[i] = new Keyframe((float)(distance / MaxDistance), (float)Gain(distance, reference, rolloff));
            }
            curve = new AnimationCurve(keys);
            for (int i = 0; i < Points; i++) curve.SmoothTangents(i, 0f);
            Cache[key] = curve;
            return curve;
        }
    }

    /// <summary>
    /// A pool of audio sources for the one-shots: a menu sound, a footstep, a distant siren. Each plays a clip through the mixer group for its bus and reverb send, placed in the world (or
    /// not), muffled if a wall is between, and handed out by priority as the prototype's voice pool does: a full pool gives a voice to a sound at least as important as its least important.
    /// </summary>
    public sealed class OneShotVoices
    {
        public const int SpatialVoices = 24, FlatVoices = 16;

        sealed class Voice
        {
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public VoicePool Pool;
            public int Slot;
            public double EndDsp;
            public bool Spatial;
            public string Sound;
            public bool Busy;
        }

        readonly MixerLevels _mixer;
        readonly Voice[] _spatial = new Voice[SpatialVoices];
        readonly Voice[] _flat = new Voice[FlatVoices];
        readonly VoicePool _spatialPool = new VoicePool(SpatialVoices);
        readonly VoicePool _flatPool = new VoicePool(FlatVoices);

        public OneShotVoices(Transform parent, MixerLevels mixer)
        {
            _mixer = mixer;
            for (int i = 0; i < SpatialVoices; i++) _spatial[i] = MakeVoice(parent, "Voice 3D " + i, true, _spatialPool, i);
            for (int i = 0; i < FlatVoices; i++) _flat[i] = MakeVoice(parent, "Voice 2D " + i, false, _flatPool, i);
        }

        static Voice MakeVoice(Transform parent, string name, bool spatial, VoicePool pool, int slot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.dopplerLevel = 0f;
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Custom;
            s.minDistance = 1f;
            s.maxDistance = RolloffCurves.MaxDistance;
            AudioLowPassFilter f = go.AddComponent<AudioLowPassFilter>();
            f.enabled = false;
            f.lowpassResonanceQ = 1f;
            return new Voice { Source = s, Filter = f, Pool = pool, Slot = slot, Spatial = spatial };
        }

        public int Active => _spatialPool.Active + _flatPool.Active;
        public int Played => _spatialPool.Played + _flatPool.Played;
        public int Stolen => _spatialPool.Stolen + _flatPool.Stolen;
        public int Culled => _spatialPool.Culled + _flatPool.Culled;

        /// <summary>The name of the sound a voice is playing, or null (for tests).</summary>
        public string SoundOnVoice(bool spatial, int slot)
        {
            Voice v = (spatial ? _spatial : _flat)[slot];
            return v.Busy ? v.Sound : null;
        }

        public AudioSource SourceOf(bool spatial, int slot) => (spatial ? _spatial : _flat)[slot].Source;

        /// <summary>
        /// Play a clip. <paramref name="def"/> says its bus, reverb send, priority and rolloff; <paramref name="position"/> places it in the world (null: head-locked). <paramref name="startDsp"/>
        /// is when to start on the audio clock (0: now); <paramref name="busOverride"/> plays it on another bus than the recipe's. Returns whether it was given a voice.
        /// </summary>
        public bool Play(AudioClip clip, SoundDef def, Vector3? position, float volume, float pitch, double occlusion, double startDsp = 0, AudioBus? busOverride = null)
        {
            if (!clip) return false;
            bool spatial = position.HasValue && def.Spatial;
            Voice[] voices = spatial ? _spatial : _flat;
            VoicePool pool = spatial ? _spatialPool : _flatPool;
            double now = AudioSettings.dspTime;
            int slot = pool.Acquire(def.Priority, startDsp > 0 ? startDsp : now, out bool stole);
            if (slot < 0) return false;
            Voice v = voices[slot];
            AudioSource s = v.Source;
            if (stole) s.Stop();

            s.clip = clip;
            s.outputAudioMixerGroup = _mixer.Group(busOverride ?? def.Bus, AudioBuses.TierFor(def.Send));
            s.priority = Mathf.Clamp(128 - def.Priority * 12, 0, 255);
            s.pitch = pitch;
            double clamped = Math.Min(1, Math.Max(0, occlusion));
            s.volume = (float)(def.Gain * volume * Occlusion.Gain(clamped));
            if (spatial)
            {
                s.transform.position = position.Value;
                s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, RolloffCurves.For(def.RefDistance, def.Rolloff));
            }
            if (clamped > 0.001)
            {
                v.Filter.enabled = true;
                v.Filter.cutoffFrequency = (float)Occlusion.CutoffHz(clamped);
            }
            else v.Filter.enabled = false;

            double begin = startDsp > now ? startDsp : now;
            if (startDsp > now) s.PlayScheduled(startDsp);
            else s.Play();
            v.EndDsp = begin + clip.length / Math.Max(0.1f, Math.Abs(pitch)) + 0.06;
            v.Busy = true;
            v.Sound = def.Name;
            return true;
        }

        /// <summary>Give back the voices whose sounds have finished.</summary>
        public void Update()
        {
            double now = AudioSettings.dspTime;
            Release(_spatial, _spatialPool, now);
            Release(_flat, _flatPool, now);
        }

        static void Release(Voice[] voices, VoicePool pool, double now)
        {
            for (int i = 0; i < voices.Length; i++)
            {
                Voice v = voices[i];
                if (!v.Busy || now < v.EndDsp) continue;
                v.Busy = false;
                v.Source.Stop();
                pool.Release(i);
            }
        }

        /// <summary>Stop everything at once. The sources are children of the engine, so when a scene unloads they are already gone.</summary>
        public void StopAll()
        {
            foreach (Voice v in _spatial) Stop(v);
            foreach (Voice v in _flat) Stop(v);
            _spatialPool.ReleaseAll();
            _flatPool.ReleaseAll();
        }

        static void Stop(Voice v)
        {
            if (v.Source) v.Source.Stop();
            v.Busy = false;
        }
    }
}
