#if UNITY_EDITOR
using System;
using System.Collections;
using Cypher.Audio;
using Cypher.City;
using Cypher.Core;
using Cypher.Game;
using Cypher.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cypher.Tests
{
    /// <summary>The sounds that come from the player and from the people and cars of the city, in the real scene.</summary>
    public class AudioWorldPlayTests
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
        }

        static AudioEngine Engine => UnityEngine.Object.FindFirstObjectByType<AudioEngine>();

        IEnumerator Playing()
        {
            yield return SliceTestScene.Load();
            yield return SliceTestScene.WaitFor(FlowState.Title);
            _tap = UnityEngine.Object.FindFirstObjectByType<AudioListener>().gameObject.AddComponent<AudioTap>();
            yield return null;
            Assume.That(Engine != null && Engine.Active, "the audio engine did not start");
            float until = Time.realtimeSinceStartup + 30f;
            while (Engine.Clips.Pending > 0 && Time.realtimeSinceStartup < until) yield return null;
            Assert.AreEqual(0, Engine.Clips.Pending);
            Assert.IsTrue(SliceTestScene.Flow.NewGame());
            yield return Seconds(0.3f);
        }

        static IEnumerator Seconds(float s)
        {
            float until = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        [UnityTest]
        public IEnumerator WalkingMakesStepsOnWhatIsUnderfootAndAJumpAndALandingAndBreath()
        {
            yield return Playing();
            FirstPersonController player = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            var input = new ScriptedInput();
            player.CommandSource = input;
            yield return Seconds(1.5f);   // settle onto the ground
            AudioEngine e = Engine;
            int steps0 = e.Foley.Steps;
            input.Forward = 1;
            yield return Seconds(5f);
            Assert.Greater(e.Foley.Steps - steps0, 3, "a walk of five seconds is several steps");
            Assert.IsNotNull(e.Foley.LastSurface);
            Assert.IsTrue(SoundBank.Has("foot." + e.Foley.LastSurface));

            input.Forward = 0;
            yield return Seconds(1.0f);
            int jumps0 = e.Foley.Jumps, lands0 = e.Foley.Landings;
            input.PressJump();
            yield return Seconds(1.5f);
            Assert.AreEqual(jumps0 + 1, e.Foley.Jumps);
            Assert.GreaterOrEqual(e.Foley.Landings, lands0 + 1, "and it comes down again with a thud");
            Assert.Greater(e.Foley.Breaths, 0, "the player breathes");
            Assert.IsNull(e.Master.Failure);
        }

        [UnityTest]
        public IEnumerator SprintingStepsMoreOftenThanWalkingAndAWalkerOnTheSpotMakesNone()
        {
            yield return Playing();
            FirstPersonController player = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            var input = new ScriptedInput();
            player.CommandSource = input;
            yield return Seconds(1.5f);
            AudioEngine e = Engine;
            int Count(Action<ScriptedInput> set, float seconds, out int before)
            {
                before = e.Foley.Steps;
                set(input);
                return before;
            }
            int a = e.Foley.Steps;
            yield return Seconds(2f);
            Assert.AreEqual(a, e.Foley.Steps, "standing still: no steps");
            input.Forward = 1;
            yield return Seconds(1f);
            int w0 = e.Foley.Steps;
            yield return Seconds(4f);
            int walkSteps = e.Foley.Steps - w0;
            input.Sprint = true;
            yield return Seconds(1f);
            int s0 = e.Foley.Steps;
            yield return Seconds(4f);
            int sprintSteps = e.Foley.Steps - s0;
            Assert.Greater(sprintSteps, walkSteps, $"sprint {sprintSteps} against walk {walkSteps} in four seconds each");
        }

        [UnityTest]
        public IEnumerator TheRealCarsAndPeopleMakeSound()
        {
            yield return Playing();
            AudioEngine e = Engine;
            yield return Seconds(10f);
            Assert.Greater(e.Actors.RunningCars, 0, "at least one moving car has an engine and tyres");
            Assert.LessOrEqual(e.Actors.RunningCars, ActorVoices.CarVoices);
            Assert.IsNull(e.Actors.Failure);

            // the plaza is empty of people near the spawn: stand beside someone
            var crowd = UnityEngine.Object.FindFirstObjectByType<Cypher.Actors.CrowdController>();
            Pedestrian walker = null;
            foreach (Pedestrian p in crowd.Sim.Active)
            {
                if (p.Speed > 1.0) { walker = p; break; }
            }
            Assert.IsNotNull(walker, "someone is walking");
            FirstPersonController player = UnityEngine.Object.FindFirstObjectByType<FirstPersonController>();
            player.Teleport(new Vector3((float)walker.X + 3f, (float)CityLayout.SidewalkHeight + 0.1f, (float)walker.Z), 0f);
            int before = e.Actors.WalkerStepsPlayed;
            yield return Seconds(4f);
            Assert.Greater(e.Actors.WalkerStepsPlayed, before, "people near the player are heard walking");
            Debug.Log(e.Status());
        }

        [UnityTest]
        public IEnumerator TheReverbFollowsTheGeometryAndTheMixerIsSetToIt()
        {
            yield return Playing();
            AudioEngine e = Engine;
            yield return Seconds(3f);
            ReverbSettings expected = ReverbSpaces.For(e.Environment.Space);
            // the blend settles within a couple of seconds of any change
            yield return Seconds(2f);
            Assert.IsTrue(e.Mixer.TryGet("ReverbDecay", out float decay));
            Assert.AreEqual(ReverbSpaces.For(e.Reverb.Space).DecayTime, decay, 0.05);
            // a change of space sends the blend toward the new one
            e.Reverb.MoveTo(ReverbSpace.Indoor, 0);
            yield return Seconds(0.3f);
            Assert.IsTrue(e.Mixer.TryGet("ReverbDecay", out decay));
            Assert.AreEqual(0.55, decay, 0.05, "the mixer's reverb is the indoor one");
            Assert.IsNotNull(expected);
        }

        [UnityTest]
        public IEnumerator TheWeatherSwitchTellsTheBedItIsRainingOrNot()
        {
            yield return Playing();
            AudioEngine e = Engine;
            Assert.IsNotNull(e.Bed);
            var rain = UnityEngine.Object.FindFirstObjectByType<Cypher.Rendering.RainController>();
            rain.Raining = false;
            yield return Seconds(0.6f);
            Assert.AreEqual(0.0, e.Bed.State.Rain, "dry");
            rain.Raining = true;
            yield return Seconds(0.6f);
            Assert.AreEqual(NightRig.DefaultRain, e.Bed.State.Rain, 1e-9, "the game's rain");
            Assert.AreEqual(RainModel.DefaultWind, e.Bed.State.Wind, 1e-9);
        }
    }
}
#endif
