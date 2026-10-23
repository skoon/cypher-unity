using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace Cypher.Audio
{
    /// <summary>
    /// The mixer's exposed parameters by their game names. A value is written only if it differs from the last written by more than a hundredth of a unit, so asking every frame costs nothing.
    /// Asking for a parameter the mixer does not have is an error, not a silent miss: a typo would otherwise be a bus that never moves.
    /// </summary>
    public sealed class MixerLevels
    {
        readonly AudioMixer _mixer;
        readonly Dictionary<string, float> _last = new Dictionary<string, float>();
        readonly AudioMixerGroup[,] _groups = new AudioMixerGroup[5, AudioBuses.TierCount];

        public MixerLevels(AudioMixer mixer)
        {
            _mixer = mixer ? mixer : throw new ArgumentNullException(nameof(mixer));
            foreach (AudioBus bus in AudioBuses.All)
            {
                for (int tier = 0; tier < AudioBuses.TierCount; tier++)
                {
                    string name = AudioBuses.GroupName(bus, tier);
                    AudioMixerGroup g = Array.Find(mixer.FindMatchingGroups(name), x => x.name == name);
                    _groups[(int)bus, tier] = g ? g : throw new InvalidOperationException($"the mixer has no group '{name}'; run Cypher > Write Audio Mixer");
                }
            }
        }

        public AudioMixer Mixer => _mixer;

        /// <summary>The group a bus's sounds play through at a reverb-send tier.</summary>
        public AudioMixerGroup Group(AudioBus bus, int tier = 0) => _groups[(int)bus, tier];

        /// <summary>Set an exposed parameter.</summary>
        public void Set(string name, double value)
        {
            float v = (float)value;
            if (_last.TryGetValue(name, out float last) && Math.Abs(last - v) < 0.01f) return;
            if (!_mixer.SetFloat(name, v)) throw new InvalidOperationException($"the mixer has no exposed parameter '{name}'");
            _last[name] = v;
        }

        /// <summary>Set the volume of a bus: every send tier of it together, so the bus slider covers the sounds' reverb sends too.</summary>
        public void SetBus(AudioBus bus, double decibels)
        {
            for (int tier = 0; tier < AudioBuses.TierCount; tier++) Set(AudioBuses.VolumeParameter(bus, tier), decibels);
        }

        /// <summary>Set the reverb's tone and length.</summary>
        public void SetReverb(ReverbSettings r)
        {
            Set("ReverbRoom", r.RoomMb);
            Set("ReverbRoomHF", r.RoomHfMb);
            Set("ReverbDecay", r.DecayTime);
            Set("ReverbDecayHF", r.DecayHfRatio);
            Set("ReverbReflections", r.ReflectionsMb);
            Set("ReverbReflectDelay", r.ReflectDelay);
            Set("ReverbLate", r.LateMb);
            Set("ReverbLateDelay", r.LateDelay);
            Set("ReverbHFReference", r.HfReference);
        }

        public bool TryGet(string name, out float value) => _mixer.GetFloat(name, out value);
    }
}
