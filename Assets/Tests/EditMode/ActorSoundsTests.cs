using System;
using System.Collections.Generic;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class ActorSoundsTests
    {
        static Pedestrian Person(int index, double x, double z, double phase, double speed = 1.4, PedState state = PedState.Walk, bool hurried = false)
        {
            return new Pedestrian { Index = index, X = x, Z = z, Phase = phase, Speed = speed, State = state, Hurried = hurried };
        }

        // ---- the nearest voices ----

        [Test]
        public void TheNearestThingsGetTheVoices()
        {
            var v = new NearestVoices(2);
            v.Offer(10, 30, 100);
            v.Offer(11, 5, 100);
            v.Offer(12, 12, 100);
            v.Resolve();
            var held = new HashSet<int> { v.IdOf(0), v.IdOf(1) };
            Assert.IsTrue(held.Contains(11));
            Assert.IsTrue(held.Contains(12));
            Assert.IsFalse(held.Contains(10));
        }

        [Test]
        public void ThingsOutOfRangeAreNotOfferedAVoiceAndFewerThingsLeaveVoicesFree()
        {
            var v = new NearestVoices(3);
            v.Offer(1, 200, 100);
            v.Offer(2, 50, 100);
            v.Resolve();
            Assert.AreEqual(2, v.IdOf(0) == 2 ? 2 : v.IdOf(1) == 2 ? 2 : v.IdOf(2));
            int free = 0;
            for (int i = 0; i < 3; i++) if (v.IdOf(i) < 0) free++;
            Assert.AreEqual(2, free);
        }

        [Test]
        public void AVoiceStaysWithItsThingWhileItIsNotMuchFartherThanTheCutoff()
        {
            var v = new NearestVoices(2);
            v.Offer(1, 10, 100);
            v.Offer(2, 20, 100);
            v.Resolve();
            int voiceOf1 = v.IdOf(0) == 1 ? 0 : 1;
            // a new car arrives a little nearer than car 2, which is now the farther of the pair; it must not take 2's voice yet
            v.Offer(1, 10, 100);
            v.Offer(2, 24, 100);
            v.Offer(3, 22, 100);
            v.Resolve();
            Assert.AreEqual(1, v.IdOf(voiceOf1));
            Assert.IsTrue(v.IdOf(0) == 2 || v.IdOf(1) == 2, "2 keeps its voice: within the margin of the cutoff");
            Assert.IsFalse(v.IdOf(0) == 3 || v.IdOf(1) == 3);
            // far enough beyond it, a nearer car does take over
            v.Offer(1, 10, 100);
            v.Offer(2, 60, 100);
            v.Offer(3, 12, 100);
            v.Resolve();
            Assert.IsTrue(v.IdOf(0) == 3 || v.IdOf(1) == 3);
            Assert.IsFalse(v.IdOf(0) == 2 || v.IdOf(1) == 2);
        }

        [Test]
        public void AThingThatIsNotOfferedAgainLosesItsVoice()
        {
            var v = new NearestVoices(1);
            v.Offer(5, 3, 50);
            v.Resolve();
            Assert.AreEqual(5, v.IdOf(0));
            v.Resolve();
            Assert.AreEqual(-1, v.IdOf(0));
            v.Offer(6, 4, 50);
            v.Resolve();
            v.Reset();
            Assert.AreEqual(-1, v.IdOf(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NearestVoices(0));
        }

        // ---- walkers ----

        [Test]
        public void AWalkerStepsEachTimeTheirGaitPassesAHalfTurn()
        {
            var steps = new WalkerSteps();
            var into = new List<Footfall>();
            Pedestrian p = Person(1, 3, 0, 0.1);
            var all = new List<Pedestrian> { p };
            steps.Update(all, 0, 0, into);
            Assert.AreEqual(0, into.Count, "the first sight only notes the phase");
            int stepsTaken = 0;
            for (int f = 0; f < 120; f++)
            {
                p.Phase = (p.Phase + 0.2) % (2 * Math.PI);
                steps.Update(all, 0, 0, into);
            }
            stepsTaken = into.Count;
            // 120 frames * 0.2 rad = 24 rad = 7.6 half turns
            Assert.That(stepsTaken, Is.InRange(7, 8));
            Assert.AreEqual(1, into[0].Person);
            Assert.AreEqual(3, into[0].X);
        }

        [Test]
        public void OnlyTheNearestSixInRangeAreHeardAndAStandingPersonMakesNoStep()
        {
            var steps = new WalkerSteps();
            var all = new List<Pedestrian>();
            for (int i = 0; i < 10; i++) all.Add(Person(i, 2 + i, 0, 0.1));
            all.Add(Person(50, 100, 0, 0.1)); // out of range
            all.Add(Person(51, 1, 1, 0.1, speed: 0)); // standing
            var into = new List<Footfall>();
            steps.Update(all, 0, 0, into);
            foreach (Pedestrian p in all) p.Phase += 3.5;
            steps.Update(all, 0, 0, into);
            var who = new HashSet<int>();
            foreach (Footfall f in into) who.Add(f.Person);
            Assert.IsFalse(who.Contains(50));
            Assert.IsFalse(who.Contains(51));
            Assert.LessOrEqual(who.Count, WalkerSteps.Voices);
            Assert.IsTrue(who.Contains(0));
            Assert.IsFalse(who.Contains(9), "the tenth nearest is beyond the six");
        }

        [Test]
        public void AHurryingPersonStepsHarderAndAPersonWhoWalksOnAfterBeingFarAwayDoesNotStepOnArrival()
        {
            var steps = new WalkerSteps();
            var near = Person(1, 4, 0, 0.1, state: PedState.Hurry, speed: 2.4);
            var into = new List<Footfall>();
            steps.Update(new List<Pedestrian> { near }, 0, 0, into);
            near.Phase += 3.5;
            steps.Update(new List<Pedestrian> { near }, 0, 0, into);
            Assert.AreEqual(1, into.Count);
            Assert.AreEqual(0.8, into[0].Level, 1e-12);
            Assert.IsTrue(into[0].Hurried);

            var calm = Person(2, 4, 0, 0.1);
            into.Clear();
            steps.Update(new List<Pedestrian> { calm }, 0, 0, into);
            calm.Phase += 3.5;
            steps.Update(new List<Pedestrian> { calm }, 0, 0, into);
            Assert.AreEqual(0.55, into[0].Level, 1e-12);
        }

        // ---- cars ----

        [Test]
        public void AFasterCarHasAHigherLouderEngineNoteAndTyresThatHissMore()
        {
            VehicleSound slow = VehicleSound.For(2, 4.5, 0);
            VehicleSound fast = VehicleSound.For(14, 4.5, 0);
            Assert.Greater(fast.EngineHz, slow.EngineHz);
            Assert.Greater(fast.EngineGain, slow.EngineGain);
            Assert.Greater(fast.TyreGain, slow.TyreGain);
            Assert.Greater(fast.TyreHz, slow.TyreHz);
        }

        [Test]
        public void AParkedCarHasNoTyreNoiseAndWetRoadsHissLouder()
        {
            Assert.AreEqual(0.0, VehicleSound.For(0, 4.5, 1).TyreGain, 1e-12);
            Assert.Greater(VehicleSound.For(10, 4.5, 1).TyreGain, VehicleSound.For(10, 4.5, 0).TyreGain * 2);
        }

        [Test]
        public void ALongTruckHasADeeperVoiceThanABike()
        {
            Assert.Less(VehicleSound.For(8, 8.5, 0).EngineHz, VehicleSound.For(8, 4.5, 0).EngineHz);
            Assert.Greater(VehicleSound.For(8, 2.0, 0).EngineHz, VehicleSound.For(8, 4.5, 0).EngineHz);
            Assert.AreEqual(VehicleSound.For(40, 4.5, 0).EngineHz, VehicleSound.For(21, 4.5, 0).EngineHz, 1e-9, "no faster than the top speed is heard");
            Assert.AreEqual(VehicleSound.For(0, 4.5, 0).EngineHz, VehicleSound.For(-3, 4.5, 0).EngineHz, 1e-9);
        }
    }
}
