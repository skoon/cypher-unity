using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class ReverbSpacesTests
    {
        static readonly ReverbSpace[] All = { ReverbSpace.Open, ReverbSpace.Street, ReverbSpace.Alley, ReverbSpace.Indoor, ReverbSpace.Plaza };

        [Test]
        public void EverySpaceIsWithinWhatTheMixersReverbTakes()
        {
            foreach (ReverbSpace s in All)
            {
                ReverbSettings r = ReverbSpaces.For(s);
                Assert.That(r.RoomMb, Is.InRange(-10000.0, 0.0), s + " room");
                Assert.That(r.RoomHfMb, Is.InRange(-10000.0, 0.0), s + " room HF");
                Assert.That(r.DecayTime, Is.InRange(0.1, 20.0), s + " decay");
                Assert.That(r.DecayHfRatio, Is.InRange(0.1, 2.0), s + " decay HF ratio");
                Assert.That(r.ReflectionsMb, Is.InRange(-10000.0, 1000.0), s + " reflections");
                Assert.That(r.ReflectDelay, Is.InRange(0.0, 0.3), s + " reflect delay");
                Assert.That(r.LateMb, Is.InRange(-10000.0, 2000.0), s + " late");
                Assert.That(r.LateDelay, Is.InRange(0.0, 0.1), s + " late delay");
                Assert.That(r.HfReference, Is.InRange(20.0, 20000.0), s + " HF reference");
            }
        }

        [Test]
        public void ThePlazaIsLongestAndIndoorsShortestAndTheStreetsTailIsTheReference()
        {
            double Decay(ReverbSpace s) => ReverbSpaces.For(s).DecayTime;
            Assert.AreEqual(2.95, Decay(ReverbSpace.Plaza));
            Assert.AreEqual(0.55, Decay(ReverbSpace.Indoor));
            Assert.AreEqual(1.75, Decay(ReverbSpace.Street));
            Assert.Greater(Decay(ReverbSpace.Plaza), Decay(ReverbSpace.Street));
            Assert.Greater(Decay(ReverbSpace.Street), Decay(ReverbSpace.Alley));
            Assert.Greater(Decay(ReverbSpace.Alley), Decay(ReverbSpace.Open));
            Assert.Greater(Decay(ReverbSpace.Open), Decay(ReverbSpace.Indoor));
        }

        [Test]
        public void AnAlleyIsThickWithEarlyReflectionsAndAPlazaIsSparseAndLateToBegin()
        {
            ReverbSettings alley = ReverbSpaces.For(ReverbSpace.Alley);
            ReverbSettings plaza = ReverbSpaces.For(ReverbSpace.Plaza);
            ReverbSettings indoor = ReverbSpaces.For(ReverbSpace.Indoor);
            Assert.Greater(alley.ReflectionsMb, plaza.ReflectionsMb + 500);
            Assert.Greater(indoor.ReflectionsMb, plaza.ReflectionsMb + 500);
            Assert.Greater(plaza.ReflectDelay, alley.ReflectDelay * 4, "the walls are far away");
            Assert.Greater(plaza.LateDelay, alley.LateDelay, "and the tail swells in slowly");
        }

        [Test]
        public void IndoorsTheHighsDieSoonestAndInTheOpenLatest()
        {
            Assert.AreEqual(0.1, ReverbSpaces.For(ReverbSpace.Indoor).DecayHfRatio, 1e-9, "dead and dark");
            Assert.Greater(ReverbSpaces.For(ReverbSpace.Open).DecayHfRatio, ReverbSpaces.For(ReverbSpace.Street).DecayHfRatio);
            Assert.Greater(ReverbSpaces.For(ReverbSpace.Plaza).DecayHfRatio, ReverbSpaces.For(ReverbSpace.Street).DecayHfRatio);
        }

        [Test]
        public void ALongerTailStartsQuieterSoEverySpaceHasAboutTheSameEnergy()
        {
            // 10 log10 of the decay ratio, in millibels
            Assert.AreEqual(0.0, ReverbSpaces.For(ReverbSpace.Street).LateMb, 1e-9);
            Assert.Less(ReverbSpaces.For(ReverbSpace.Plaza).LateMb, 0);
            Assert.Greater(ReverbSpaces.For(ReverbSpace.Indoor).LateMb, 0);
            Assert.AreEqual(1000 * Math.Log10(1.75 / 2.95), ReverbSpaces.For(ReverbSpace.Plaza).LateMb, 1e-9);
        }

        [Test]
        public void ASpaceChangeIsACrossfadeOfAboutAndASecondAndAThird()
        {
            var blend = new ReverbBlend(ReverbSpace.Street);
            Assert.IsTrue(blend.Settled);
            blend.MoveTo(ReverbSpace.Plaza);
            Assert.IsFalse(blend.Settled);
            Assert.AreEqual(ReverbSpaces.For(ReverbSpace.Street).DecayTime, blend.Current.DecayTime, 1e-12, "it has not moved yet");
            blend.Advance(0.65);
            double mid = blend.Current.DecayTime;
            Assert.AreEqual((1.75 + 2.95) / 2, mid, 1e-9, "half way after half the fade");
            blend.Advance(10);
            Assert.IsTrue(blend.Settled);
            Assert.AreEqual(2.95, blend.Current.DecayTime, 1e-12);
            Assert.AreEqual(ReverbSpace.Plaza, blend.Space);
        }

        [Test]
        public void ChangingMindMidFadeHeadsForTheNewSpaceFromWhereItIsNotFromTheLastOne()
        {
            var blend = new ReverbBlend(ReverbSpace.Indoor);
            blend.MoveTo(ReverbSpace.Plaza);
            blend.Advance(0.65);
            double here = blend.Current.DecayTime;
            blend.MoveTo(ReverbSpace.Alley);
            Assert.AreEqual(here, blend.Current.DecayTime, 1e-9, "no jump");
            blend.Advance(100);
            Assert.AreEqual(1.25, blend.Current.DecayTime, 1e-12);
        }

        [Test]
        public void AnInstantChangeAndAChangeToWhereItAlreadyIsAreHandled()
        {
            var blend = new ReverbBlend(ReverbSpace.Street);
            blend.MoveTo(ReverbSpace.Street);
            Assert.IsTrue(blend.Settled);
            blend.MoveTo(ReverbSpace.Indoor, 0);
            Assert.IsTrue(blend.Settled);
            Assert.AreEqual(0.55, blend.Current.DecayTime, 1e-12);
            Assert.Throws<ArgumentOutOfRangeException>(() => blend.Advance(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReverbSpaces.For((ReverbSpace)9));
        }
    }
}
