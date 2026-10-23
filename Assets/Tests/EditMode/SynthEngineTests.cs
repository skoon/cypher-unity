using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>The synthesis engine's own behaviour, worked out by hand: schedules, tables, the graph and its nodes.</summary>
    public class SynthEngineTests
    {
        const int Rate = 48000;

        static AudioParam Param(double initial = 0) => new SynthGraph(Rate).CreateGain(initial).Gain;

        // ---- the parameter's schedule ----

        [Test]
        public void AParameterHoldsItsValueUntilSomethingIsScheduled()
        {
            AudioParam p = Param(0.25);
            Assert.AreEqual(0.25, p.ValueAt(0));
            Assert.AreEqual(0.25, p.ValueAt(100));
            Assert.IsFalse(p.HasEvents);
        }

        [Test]
        public void ALinearRampIsAStraightLineFromTheEventBeforeIt()
        {
            AudioParam p = Param();
            p.SetValueAtTime(1, 0.5).LinearRampToValueAtTime(3, 1.5);
            Assert.AreEqual(0.0, p.ValueAt(0.25), "before the first event the value is the starting one");
            Assert.AreEqual(1.0, p.ValueAt(0.5), 1e-12);
            Assert.AreEqual(2.0, p.ValueAt(1.0), 1e-12);
            Assert.AreEqual(2.5, p.ValueAt(1.25), 1e-12);
            Assert.AreEqual(3.0, p.ValueAt(1.5), 1e-12);
            Assert.AreEqual(3.0, p.ValueAt(9), "after the ramp it holds");
        }

        [Test]
        public void AnExponentialRampMovesByTheSameRatioInEqualTimes()
        {
            AudioParam p = Param();
            p.SetValueAtTime(1, 0).ExponentialRampToValueAtTime(16, 4);
            Assert.AreEqual(2.0, p.ValueAt(1), 1e-12);
            Assert.AreEqual(4.0, p.ValueAt(2), 1e-12);
            Assert.AreEqual(8.0, p.ValueAt(3), 1e-12);
            p = Param();
            p.SetValueAtTime(1, 0).ExponentialRampToValueAtTime(0.25, 2);
            Assert.AreEqual(0.5, p.ValueAt(1), 1e-12);
        }

        [Test]
        public void AnExponentialRampFromZeroOrAcrossZeroHoldsUntilItsEnd()
        {
            AudioParam p = Param();
            p.SetValueAtTime(0, 0).ExponentialRampToValueAtTime(1, 2);
            Assert.AreEqual(0.0, p.ValueAt(1));
            Assert.AreEqual(1.0, p.ValueAt(2));
            p = Param();
            p.SetValueAtTime(-1, 0).ExponentialRampToValueAtTime(1, 2);
            Assert.AreEqual(-1.0, p.ValueAt(1));
        }

        [Test]
        public void ATargetApproachesItWithItsTimeConstant()
        {
            AudioParam p = Param();
            p.SetValueAtTime(0, 0).SetTargetAtTime(1, 1, 0.5);
            Assert.AreEqual(0.0, p.ValueAt(1), 1e-12);
            Assert.AreEqual(1 - Math.Exp(-1), p.ValueAt(1.5), 1e-12, "one time constant closes 63% of the gap");
            Assert.AreEqual(1 - Math.Exp(-4), p.ValueAt(3), 1e-12);
        }

        [Test]
        public void ASecondTargetBeginsFromWhereTheFirstHadGot()
        {
            AudioParam p = Param();
            p.SetValueAtTime(0, 0).SetTargetAtTime(1, 0, 1).SetTargetAtTime(0, 2, 1);
            double at2 = 1 - Math.Exp(-2);
            Assert.AreEqual(at2, p.ValueAt(2), 1e-12);
            Assert.AreEqual(at2 * Math.Exp(-1), p.ValueAt(3), 1e-12);
        }

        [Test]
        public void ATargetWithNoTimeConstantJumps()
        {
            AudioParam p = Param();
            p.SetValueAtTime(0.3, 0).SetTargetAtTime(0.8, 1, 0);
            Assert.AreEqual(0.3, p.ValueAt(0.9));
            Assert.AreEqual(0.8, p.ValueAt(1.0));
        }

        [Test]
        public void AnEventAtTheSameMomentOfTheSameKindReplacesTheOneBefore()
        {
            AudioParam p = Param();
            p.SetValueAtTime(0.1, 1).SetValueAtTime(0.7, 1);
            Assert.AreEqual(0.7, p.ValueAt(1));
            Assert.AreEqual(0.7, p.ValueAt(5));
        }

        [Test]
        public void CancellingForgetsEverythingFromThenOn()
        {
            AudioParam p = Param();
            p.SetValueAtTime(0.5, 0).LinearRampToValueAtTime(1, 4).SetValueAtTime(0.2, 6);
            p.CancelScheduledValues(3);
            Assert.AreEqual(0.5, p.ValueAt(2), "the ramp that would have run is gone");
            Assert.AreEqual(0.5, p.ValueAt(5));
            p.CancelScheduledValues(0);
            Assert.IsFalse(p.HasEvents);
        }

        [Test]
        public void EventsAddedOutOfOrderAreKeptInOrder()
        {
            AudioParam p = Param();
            p.SetValueAtTime(3, 3).SetValueAtTime(1, 1).SetValueAtTime(2, 2);
            Assert.AreEqual(1.0, p.ValueAt(1.5));
            Assert.AreEqual(2.0, p.ValueAt(2.5));
            Assert.AreEqual(3.0, p.ValueAt(3.5));
        }

        [Test]
        public void SettingTheValueWhenEventsAreScheduledIsASetAtTheCurrentTime()
        {
            var g = new SynthGraph(Rate);
            AudioParam p = g.CreateGain(1).Gain;
            p.SetValueAtTime(5, 10);
            p.Value = 7;
            Assert.AreEqual(7.0, p.ValueAt(0));
            Assert.AreEqual(5.0, p.ValueAt(10), "the later event still comes");
        }

        [Test]
        public void BadEventsAreRefusedLoudly()
        {
            AudioParam p = Param();
            Assert.Throws<ArgumentOutOfRangeException>(() => p.ExponentialRampToValueAtTime(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.SetValueAtTime(1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => p.SetTargetAtTime(1, 0, -1));
            Assert.Throws<ArgumentException>(() => p.SetValueAtTime(double.NaN, 0));
            p.SetTargetAtTime(1, 0, 1);
            Assert.Throws<NotSupportedException>(() => p.LinearRampToValueAtTime(0, 3), "a ramp after a target has no defined start here");
        }

        [Test]
        public void AParametersBlockMatchesItsScheduleAtEveryFrame()
        {
            var g = new SynthGraph(Rate);
            ConstantSourceNode c = g.CreateConstantSource(1);
            c.Connect(g.Destination);
            c.Offset.SetValueAtTime(0.2, 0).LinearRampToValueAtTime(1, 0.01).SetTargetAtTime(0, 0.012, 0.002);
            float[] output = g.RenderMono(1024);
            var reference = new SynthGraph(Rate).CreateGain(0).Gain;
            reference.SetValueAtTime(0.2, 0).LinearRampToValueAtTime(1, 0.01).SetTargetAtTime(0, 0.012, 0.002);
            for (int i = 0; i < output.Length; i += 7)
            {
                Assert.AreEqual(reference.ValueAt(i / (double)Rate), output[i], 2e-7, "frame " + i);
            }
        }

        [Test]
        public void AParameterThatHasPassedItsEventsKeepsTheValueTheyLeftWhenOldEventsAreDropped()
        {
            var g = new SynthGraph(Rate);
            ConstantSourceNode c = g.CreateConstantSource(1);
            c.Connect(g.Destination);
            // many targets in turn, as the bed's weather makes; the block-by-block result must follow the closed form
            double value = 0;
            double time = 0;
            for (int k = 0; k < 20; k++)
            {
                double target = (k % 2) * 0.8 + 0.1;
                c.Offset.SetTargetAtTime(target, time, 0.01);
                time += 0.005;
            }
            float[] output = g.RenderMono(4096);
            var reference = new SynthGraph(Rate).CreateGain(1).Gain;
            time = 0;
            for (int k = 0; k < 20; k++)
            {
                reference.SetTargetAtTime((k % 2) * 0.8 + 0.1, time, 0.01);
                time += 0.005;
            }
            for (int i = 0; i < output.Length; i += 5)
            {
                value = reference.ValueAt(i / (double)Rate);
                Assert.AreEqual(value, output[i], 5e-6, "frame " + i);
            }
        }

        // ---- the wave tables ----

        [Test]
        public void ASineTableIsASine()
        {
            PeriodicWave sine = PeriodicWave.Get(WaveKind.Sine, Rate);
            float[] t = sine.TableFor(440);
            Assert.AreEqual(PeriodicWave.TableSize + 1, t.Length);
            for (int j = 0; j < PeriodicWave.TableSize; j += 37) Assert.AreEqual(Math.Sin(2 * Math.PI * j / PeriodicWave.TableSize), t[j], 1e-6);
            Assert.AreEqual(t[0], t[PeriodicWave.TableSize], "the guard sample repeats the first");
        }

        [Test]
        public void EveryFullTableIsScaledToAPeakOfOne()
        {
            foreach (WaveKind kind in new[] { WaveKind.Sine, WaveKind.Triangle, WaveKind.Sawtooth, WaveKind.SoftSaw, WaveKind.Hollow, WaveKind.Bell })
            {
                float[] t = PeriodicWave.Get(kind, Rate).TableFor(1);
                float peak = 0;
                foreach (float v in t) peak = Math.Max(peak, Math.Abs(v));
                Assert.AreEqual(1.0, peak, 1e-5, kind.ToString());
            }
        }

        [Test]
        public void AHigherNoteGetsFewerHarmonicsSoNothingAliases()
        {
            PeriodicWave saw = PeriodicWave.Get(WaveKind.Sawtooth, Rate);
            // at 11 kHz only the fundamental and the second harmonic fit under the Nyquist frequency, and the table has no more than that
            saw.Select(11000, out float[] low, out _, out _);
            double rms = 0;
            for (int j = 0; j < PeriodicWave.TableSize; j++) rms += low[j] * (double)low[j];
            rms = Math.Sqrt(rms / PeriodicWave.TableSize);
            var wide = PeriodicWave.Get(WaveKind.Sawtooth, Rate).TableFor(100);
            double wideRms = 0;
            for (int j = 0; j < PeriodicWave.TableSize; j++) wideRms += wide[j] * (double)wide[j];
            wideRms = Math.Sqrt(wideRms / PeriodicWave.TableSize);
            Assert.Less(rms, wideRms, "fewer harmonics, less energy");
            // the second harmonic is the last: the table is a smooth two-sine sum, so neighbouring samples never differ by much
            for (int j = 1; j < PeriodicWave.TableSize; j++) Assert.Less(Math.Abs(low[j] - low[j - 1]), 0.05f);
        }

        [Test]
        public void TheCustomWavesHaveTheStatedHarmonics()
        {
            double[] soft = PeriodicWave.Coefficients(WaveKind.SoftSaw);
            Assert.AreEqual(40, soft.Length);
            Assert.AreEqual(Math.Exp(-1 / 16.0), soft[1], 1e-12);
            Assert.AreEqual(Math.Exp(-5 / 16.0) / 5, soft[5], 1e-12);
            double[] hollow = PeriodicWave.Coefficients(WaveKind.Hollow);
            Assert.AreEqual(0, hollow[2]);
            Assert.AreEqual(0, hollow[10]);
            Assert.AreEqual(Math.Exp(-3 / 16.0) / 3, hollow[3], 1e-12);
            double[] bell = PeriodicWave.Coefficients(WaveKind.Bell);
            Assert.AreEqual(1.0, bell[1]);
            Assert.AreEqual(0.42, bell[2]);
            Assert.AreEqual(0.03, bell[7]);
            Assert.AreEqual(0.0, bell[8]);
        }

        [Test]
        public void TheNativeWavesFollowTheirFourierSeries()
        {
            double[] tri = PeriodicWave.Coefficients(WaveKind.Triangle);
            Assert.AreEqual(8 / (Math.PI * Math.PI), tri[1], 1e-12);
            Assert.AreEqual(-8 / (9 * Math.PI * Math.PI), tri[3], 1e-12);
            Assert.AreEqual(0.0, tri[2]);
            double[] saw = PeriodicWave.Coefficients(WaveKind.Sawtooth);
            Assert.AreEqual(2 / Math.PI, saw[1], 1e-12);
            Assert.AreEqual(-1 / Math.PI, saw[2], 1e-12);
        }

        [Test]
        public void WavesAreMadeOncePerRateAndShared()
        {
            Assert.AreSame(PeriodicWave.Get(WaveKind.Bell, Rate), PeriodicWave.Get(WaveKind.Bell, Rate));
            Assert.AreNotSame(PeriodicWave.Get(WaveKind.Bell, Rate), PeriodicWave.Get(WaveKind.Bell, 44100));
            Assert.Throws<ArgumentOutOfRangeException>(() => PeriodicWave.Get(WaveKind.Bell, 0));
        }

        // ---- the graph ----

        [Test]
        public void ASourceIsNotHeardUntilItIsConnectedToTheDestination()
        {
            var g = new SynthGraph(Rate);
            g.CreateOscillator(WaveKind.Sine, 440).Start(0);
            float[] output = g.RenderMono(512);
            foreach (float v in output) Assert.AreEqual(0f, v);
        }

        [Test]
        public void AMonoSourceReachesBothSpeakers()
        {
            var g = new SynthGraph(Rate);
            g.CreateOscillator(WaveKind.Sine, 440).Start(0).Connect(g.Destination);
            g.Render(512, out float[] left, out float[] right);
            CollectionAssert.AreEqual(left, right);
            float peak = 0;
            foreach (float v in left) peak = Math.Max(peak, Math.Abs(v));
            Assert.AreEqual(1.0, peak, 0.01);
        }

        [Test]
        public void ConnectingTwiceDoesNotDoubleTheSignal()
        {
            var once = new SynthGraph(Rate);
            once.CreateOscillator(WaveKind.Sine, 440).Start(0).Connect(once.Destination);
            var twice = new SynthGraph(Rate);
            SynthNode o = twice.CreateOscillator(WaveKind.Sine, 440).Start(0);
            o.Connect(twice.Destination);
            o.Connect(twice.Destination);
            CollectionAssert.AreEqual(once.RenderMono(256), twice.RenderMono(256));
        }

        [Test]
        public void ChainConnectsInOrderAndReturnsTheLast()
        {
            var g = new SynthGraph(Rate);
            OscillatorNode o = g.CreateOscillator(WaveKind.Sine, 440).Start(0);
            GainNode a = g.CreateGain(0.5);
            GainNode b = g.CreateGain(0.5);
            Assert.AreSame(b, SynthNode.Chain(o, null, a, b));
            b.Connect(g.Destination);
            float[] output = g.RenderMono(512);
            float peak = 0;
            foreach (float v in output) peak = Math.Max(peak, Math.Abs(v));
            Assert.AreEqual(0.25, peak, 0.01);
        }

        [Test]
        public void ABadConnectionIsRefused()
        {
            var g = new SynthGraph(Rate);
            var other = new SynthGraph(Rate);
            OscillatorNode o = g.CreateOscillator();
            GainNode gain = g.CreateGain();
            Assert.Throws<ArgumentException>(() => gain.Connect(o), "a source has no input");
            Assert.Throws<ArgumentException>(() => gain.Connect(other.Destination), "another graph's node");
            Assert.Throws<ArgumentNullException>(() => gain.Connect((SynthNode)null));
            Assert.Throws<ArgumentNullException>(() => gain.Connect((AudioParam)null));
        }

        [Test]
        public void ADestinationOfNoSensibleRateIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SynthGraph(100));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SynthGraph(1000000));
        }

        [Test]
        public void ARenderIsTheSameEveryTime()
        {
            float[] Make()
            {
                var g = new SynthGraph(Rate);
                BufferSourceNode s = g.CreateBufferSource(AudioNoise.Make(NoiseKind.Pink, Rate)).Start(0, 0.5);
                BiquadNode f = g.CreateBiquad(BiquadType.Lowpass, 800, 2);
                f.Frequency.SetValueAtTime(800, 0).ExponentialRampToValueAtTime(4000, 0.05);
                SynthNode.Chain(s, f, g.Destination);
                return g.RenderMono(4096);
            }
            CollectionAssert.AreEqual(Make(), Make());
        }

        [Test]
        public void FillingADeviceBufferInOddSizesGivesTheSameSoundAsRenderingBlocks()
        {
            SynthGraph Make()
            {
                var g = new SynthGraph(Rate);
                SynthNode.Chain(g.CreateOscillator(WaveKind.Sawtooth, 220).Start(0), g.CreateBiquad(BiquadType.Lowpass, 1200, 1), g.Destination);
                return g;
            }
            float[] expected = Make().RenderMono(3000);
            SynthGraph live = Make();
            var got = new float[3000 * 2];
            int at = 0;
            foreach (int size in new[] { 1, 127, 1024, 333, 99, 1000, 416 })
            {
                var chunk = new float[size * 2];
                live.Fill(chunk, 2, size);
                Array.Copy(chunk, 0, got, at * 2, size * 2);
                at += size;
            }
            for (int i = 0; i < at; i++) Assert.AreEqual(expected[i], got[i * 2], 1e-7f, "left " + i);
            for (int i = 0; i < at; i++) Assert.AreEqual(expected[i], got[i * 2 + 1], 1e-7f, "right " + i);
        }

        [Test]
        public void FillCanAddToWhatIsThereAndFeedAMonoOrSurroundBuffer()
        {
            var g = new SynthGraph(Rate);
            g.CreateConstantSource(0.5).Connect(g.Destination);
            var stereo = new float[2 * 8];
            for (int i = 0; i < stereo.Length; i++) stereo[i] = 1;
            g.Fill(stereo, 2, 8, add: true);
            foreach (float v in stereo) Assert.AreEqual(1.5f, v, 1e-6f);
            var mono = new float[8];
            g.Fill(mono, 1, 8);
            foreach (float v in mono) Assert.AreEqual(0.5f, v, 1e-6f);
            var six = new float[6 * 4];
            for (int i = 0; i < six.Length; i++) six[i] = 9;
            g.Fill(six, 6, 4);
            for (int f = 0; f < 4; f++)
            {
                Assert.AreEqual(0.5f, six[f * 6], 1e-6f);
                Assert.AreEqual(0.5f, six[f * 6 + 1], 1e-6f);
                for (int c = 2; c < 6; c++) Assert.AreEqual(0f, six[f * 6 + c]);
            }
            Assert.Throws<ArgumentException>(() => g.Fill(new float[4], 2, 8));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.Fill(new float[4], 0, 2));
        }

        // ---- nodes ----

        [Test]
        public void AnOscillatorStartsOnceAndOnlyAStartedOneCanStop()
        {
            var g = new SynthGraph(Rate);
            OscillatorNode o = g.CreateOscillator();
            Assert.Throws<InvalidOperationException>(() => o.Stop(1));
            o.Start(0);
            Assert.Throws<InvalidOperationException>(() => o.Start(1));
        }

        [Test]
        public void AnOscillatorFallsSilentAtItsStopTime()
        {
            var g = new SynthGraph(Rate);
            g.CreateOscillator(WaveKind.Sine, 1000).Start(0).Stop(0.01).Connect(g.Destination);
            float[] output = g.RenderMono(1024);
            Assert.Greater(Math.Abs(output[479]), 0.1f, "still sounding at the last frame");
            for (int i = 480; i < output.Length; i++) Assert.AreEqual(0f, output[i], "frame " + i);
        }

        [Test]
        public void ABufferSourceLoopsAndStopsAfterItsDuration()
        {
            var g = new SynthGraph(Rate);
            var data = new float[1000];
            for (int i = 0; i < data.Length; i++) data[i] = 1;
            g.CreateBufferSource(data).Start(0, 0, 3000.0 / Rate).Connect(g.Destination);
            float[] output = g.RenderMono(4000);
            for (int i = 0; i < 3000; i++) Assert.AreEqual(1f, output[i], 1e-6f, "frame " + i);
            for (int i = 3000; i < 4000; i++) Assert.AreEqual(0f, output[i], "frame " + i);
            Assert.Throws<ArgumentException>(() => g.CreateBufferSource(new float[1]));
            Assert.Throws<ArgumentException>(() => g.CreateBufferSource(null));
        }

        [Test]
        public void AFilterPassesWhatItShouldAndStopsWhatItShould()
        {
            // a 100 Hz and a 10 kHz tone through a 1 kHz low-pass: the low one passes, the high one is gone
            float Level(double hz)
            {
                var g = new SynthGraph(Rate);
                SynthNode.Chain(g.CreateOscillator(WaveKind.Sine, hz).Start(0), g.CreateBiquad(BiquadType.Lowpass, 1000, 0), g.Destination);
                float[] o = g.RenderMono(8192);
                float peak = 0;
                for (int i = 4096; i < o.Length; i++) peak = Math.Max(peak, Math.Abs(o[i]));
                return peak;
            }
            Assert.AreEqual(1.0, Level(100), 0.02);
            Assert.Less(Level(10000), 0.02);
        }

        [Test]
        public void ADelayRepeatsAfterItsTimeAndAFeedbackLoopDecays()
        {
            var g = new SynthGraph(Rate);
            var impulse = new float[4800];
            impulse[0] = 1;
            BufferSourceNode s = g.CreateBufferSource(impulse).Start(0);
            DelayNode d = g.CreateDelay(0.01);
            GainNode fb = g.CreateGain(0.5);
            s.Connect(d);
            d.Connect(fb);
            fb.Connect(d);
            d.Connect(g.Destination);
            float[] output = g.RenderMono(4000);
            Assert.AreEqual(1f, output[480], 1e-6f);
            Assert.AreEqual(0.5f, output[480 + 480 + 128], 1e-6f, "a loop's round trip is the delay and a block more");
            Assert.AreEqual(0.25f, output[480 + 2 * (480 + 128)], 1e-6f);
            Assert.AreEqual(0f, output[479]);
        }

        [Test]
        public void ADelayAskedForLessThanABlockIsRaisedToABlock()
        {
            var g = new SynthGraph(Rate);
            var impulse = new float[1024];
            impulse[0] = 1;
            g.CreateBufferSource(impulse).Start(0).Connect(g.CreateDelay(0.0005)).Connect(g.Destination);
            float[] output = g.RenderMono(512);
            Assert.AreEqual(1f, output[128], 1e-6f);
            Assert.Throws<ArgumentOutOfRangeException>(() => g.CreateDelay(0.1, 0));
        }

        [Test]
        public void AStereoPannerPutsAMonoSoundWhereItIsTold()
        {
            var g = new SynthGraph(Rate);
            g.CreateConstantSource(1).Connect(g.CreateStereoPanner(-1)).Connect(g.Destination);
            g.Render(256, out float[] left, out float[] right);
            Assert.AreEqual(1f, left[200], 1e-6f);
            Assert.AreEqual(0f, right[200], 1e-6f);
            g = new SynthGraph(Rate);
            g.CreateConstantSource(1).Connect(g.CreateStereoPanner(0)).Connect(g.Destination);
            g.Render(256, out left, out right);
            Assert.AreEqual(Math.Sqrt(0.5), left[200], 1e-6f);
            Assert.AreEqual(left[200], right[200], 1e-6f);
        }

        [Test]
        public void ALfoMovesAFiltersFrequencyThroughAParameterInput()
        {
            // a low-pass whose cutoff swings between low and high lets a steady high tone through only part of each cycle
            var g = new SynthGraph(Rate);
            OscillatorNode tone = g.CreateOscillator(WaveKind.Sine, 5000).Start(0);
            BiquadNode f = g.CreateBiquad(BiquadType.Lowpass, 3000, 0);
            OscillatorNode lfo = g.CreateOscillator(WaveKind.Sine, 50).Start(0);
            GainNode depth = g.CreateGain(2900);
            lfo.Connect(depth);
            depth.Connect(f.Frequency);
            SynthNode.Chain(tone, f, g.Destination);
            float[] output = g.RenderMono(4800);
            float min = 1, max = 0;
            // envelope in 1 ms windows
            for (int w = 480; w + 48 <= output.Length; w += 48)
            {
                float peak = 0;
                for (int i = w; i < w + 48; i++) peak = Math.Max(peak, Math.Abs(output[i]));
                min = Math.Min(min, peak);
                max = Math.Max(max, peak);
            }
            Assert.Less(min, 0.2f, "the cutoff swings below the tone");
            Assert.Greater(max, 0.8f, "and above it");
        }

        // ---- scheduled sounds from elsewhere ----

        [Test]
        public void AQueuedSoundStartsAtItsFrameAndAPastOneStartsPartWayIn()
        {
            var g = new SynthGraph(Rate);
            QueuedPlayerNode q = g.CreateQueuedPlayer();
            q.Connect(g.Destination);
            var ramp = new float[100];
            for (int i = 0; i < ramp.Length; i++) ramp[i] = i + 1;
            q.Schedule(300, ramp, 0.5f);
            q.Schedule(-10, ramp, 1f);
            float[] output = g.RenderMono(512);
            Assert.AreEqual(11f, output[0], 1e-6f, "ten frames of the second sound are already behind it");
            Assert.AreEqual(0f, output[100]);
            Assert.AreEqual(0.5f, output[300], 1e-6f);
            Assert.AreEqual(50f * 1f, output[399] * 1f, 1e-5f);
            Assert.AreEqual(0f, output[400]);
        }

        [Test]
        public void QueuedSoundsFromAnotherThreadAreMixedIn()
        {
            var g = new SynthGraph(Rate);
            QueuedPlayerNode q = g.CreateQueuedPlayer();
            q.Connect(g.Destination);
            var tick = new float[16];
            tick[0] = 1;
            var thread = new System.Threading.Thread(() => { for (int k = 0; k < 50; k++) q.Schedule(1000 + k * 40, tick); });
            thread.Start();
            thread.Join();
            float[] output = g.RenderMono(4000);
            int count = 0;
            foreach (float v in output) if (v > 0.5f) count++;
            Assert.AreEqual(50, count);
        }
    }
}
