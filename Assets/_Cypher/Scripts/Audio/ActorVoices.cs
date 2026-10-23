using System;
using System.Collections.Generic;
using Cypher.Core;
using UnityEngine;

namespace Cypher.Audio
{
    /// <summary>
    /// The sound of the real traffic and the real people, in place of the prototype's random car passes and voices: the few nearest moving vehicles each have an engine and tyres that follow
    /// their speed (and the rain), the few nearest walkers' footsteps are played on their own gait, on whatever they are walking on. Voices stay with their vehicle until it is clearly no longer
    /// among the nearest, so a passing car does not change hands with another and click.
    /// </summary>
    public sealed class ActorVoices : IDisposable
    {
        public const int CarVoices = 5;
        public const double CarRange = 70;
        const double UpdateEvery = 0.1;

        /// <summary>What the main thread has decided for a car, picked up whole on the audio thread.</summary>
        sealed class Decision
        {
            public readonly VehicleSound Sound;
            public readonly double Stamp;

            public Decision(VehicleSound sound, double stamp)
            {
                Sound = sound;
                Stamp = stamp;
            }
        }

        sealed class CarVoice
        {
            public CarVoiceGraph Graph;
            public SynthSource Source;
            public volatile Decision Latest = new Decision(Silent, 0);
            public int Vehicle = -1;
            public double IdleSince;
            public bool Running;
        }

        static readonly VehicleSound Silent = new VehicleSound(70, 0, 0, 900);

        readonly CarVoice[] _cars = new CarVoice[CarVoices];
        readonly NearestVoices _nearest = new NearestVoices(CarVoices);
        readonly WalkerSteps _walkers = new WalkerSteps();
        readonly List<Footfall> _footfalls = new List<Footfall>();
        readonly TakePicker _takes = new TakePicker(7);
        readonly Dictionary<int, Cypher.Core.Vehicle> _byIndex = new Dictionary<int, Cypher.Core.Vehicle>();
        double _accumulated, _stamp, _clock;

        public int ActiveCars
        {
            get
            {
                int n = 0;
                foreach (CarVoice c in _cars) if (c.Vehicle >= 0) n++;
                return n;
            }
        }

        public int RunningCars
        {
            get
            {
                int n = 0;
                foreach (CarVoice c in _cars) if (c.Running) n++;
                return n;
            }
        }

        public int WalkerStepsPlayed { get; private set; }

        public double WorstMilliseconds
        {
            get
            {
                double ms = 0;
                foreach (CarVoice c in _cars) ms = Math.Max(ms, c.Source.WorstMilliseconds);
                return ms;
            }
        }

        /// <summary>Milliseconds the audio thread spends on the cars' engines and tyres per device block.</summary>
        public double MillisecondsPerBlock
        {
            get
            {
                double ms = 0;
                foreach (CarVoice c in _cars) ms += c.Source.MillisecondsPerCallback;
                return ms;
            }
        }

        public ActorVoices(Transform parent, MixerLevels mixer, AudioNoiseBank noise)
        {
            // a car's tyres and engine are an ambient sound: the car pass's mixer path and rolloff
            SoundDef pass = SoundBank.Get("amb.carpass");
            var group = mixer.Group(AudioBus.Ambience, AudioBuses.TierFor(pass.Send));
            var rng = new Rng(77);
            for (int i = 0; i < CarVoices; i++)
            {
                var graph = new CarVoiceGraph(noise, rng);
                SynthSource src = SynthSource.Create("Car " + i, parent, graph.Graph, 1, group, spatial: true, maxDistance: RolloffCurves.MaxDistance);
                src.Source.rolloffMode = AudioRolloffMode.Custom;
                src.Source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, RolloffCurves.For(pass.RefDistance, pass.Rolloff));
                src.Source.volume = (float)pass.Gain;
                var voice = new CarVoice { Graph = graph, Source = src };
                src.BeforeBlock = _ =>
                {
                    Decision d = voice.Latest;
                    voice.Graph.Apply(d.Sound, d.Stamp);
                };
                _cars[i] = voice;
            }
        }

        /// <summary>The cars, once a frame: pick the nearest moving ones, hand each its sound, place it.</summary>
        public void UpdateCars(TrafficSim traffic, Vector3 listener, double wet, double dt)
        {
            _accumulated += dt;
            _clock += dt;
            if (traffic == null)
            {
                SilenceAll();
                return;
            }
            // placement every frame; the decisions ten times a second
            foreach (CarVoice c in _cars)
            {
                if (c.Vehicle >= 0 && _byIndex.TryGetValue(c.Vehicle, out Cypher.Core.Vehicle v)) c.Source.transform.position = new Vector3((float)v.X, 0.9f, (float)v.Z);
            }
            if (_accumulated < UpdateEvery) return;
            _accumulated = 0;

            _byIndex.Clear();
            foreach (Cypher.Core.Vehicle v in traffic.Vehicles)
            {
                if (!v.Active || v.Speed < 0.4) continue;
                double dx = v.X - listener.x, dz = v.Z - listener.z;
                _byIndex[v.Index] = v;
                _nearest.Offer(v.Index, Math.Sqrt(dx * dx + dz * dz), CarRange);
            }
            _nearest.Resolve();
            _stamp++;
            for (int i = 0; i < CarVoices; i++)
            {
                CarVoice c = _cars[i];
                int id = _nearest.IdOf(i);
                if (id >= 0 && _byIndex.TryGetValue(id, out Cypher.Core.Vehicle v))
                {
                    if (c.Vehicle != id)
                    {
                        c.Vehicle = id;
                        c.Source.transform.position = new Vector3((float)v.X, 0.9f, (float)v.Z);
                    }
                    c.Latest = new Decision(VehicleSound.For(v.Speed, v.Type.Length, wet), _stamp);
                    if (!c.Running)
                    {
                        c.Source.Begin();
                        c.Running = true;
                    }
                    c.IdleSince = 0;
                }
                else
                {
                    c.Vehicle = -1;
                    c.Latest = new Decision(Silent, _stamp);
                    if (c.Running)
                    {
                        // let it fall quiet, then stop it so an unused voice costs nothing
                        if (c.IdleSince == 0) c.IdleSince = _clock;
                        else if (_clock - c.IdleSince > 1.5)
                        {
                            c.Source.Halt();
                            c.Running = false;
                            c.IdleSince = 0;
                        }
                    }
                }
            }
        }

        void SilenceAll()
        {
            foreach (CarVoice c in _cars)
            {
                c.Vehicle = -1;
                if (c.Running)
                {
                    c.Source.Halt();
                    c.Running = false;
                }
            }
        }

        /// <summary>The people, once a frame: footfalls of the nearest walkers. <paramref name="play"/> is given the footfall, the surface, the wetness step and the take.</summary>
        public void UpdateWalkers(IReadOnlyList<Pedestrian> people, Vector3 listener, double wetness, Action<Footfall, string, int, int> play)
        {
            if (people == null) return;
            _footfalls.Clear();
            _walkers.Update(people, listener.x, listener.z, _footfalls);
            foreach (Footfall f in _footfalls)
            {
                string surface = Footing.SurfaceAt(f.X, f.Z, CityLayout.IsOnRoad(f.X, f.Z), wetness);
                int wet = ClipPlan.WetBucket(wetness);
                int take = _takes.Pick("walker." + surface, ClipPlan.StepTakes);
                play(f, surface, wet, take);
                WalkerStepsPlayed++;
            }
        }

        /// <summary>The first exception from any car voice's audio thread.</summary>
        public Exception Failure
        {
            get
            {
                foreach (CarVoice c in _cars) if (c.Source.Failure != null) return c.Source.Failure;
                return null;
            }
        }

        public void Dispose()
        {
            foreach (CarVoice c in _cars)
            {
                if (c != null && c.Source) UnityEngine.Object.Destroy(c.Source.gameObject);
            }
        }
    }
}
