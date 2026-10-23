using System;
using System.Collections.Generic;
using System.Globalization;
using Cypher.Core;

namespace Cypher.Tests
{
    /// <summary>
    /// Builds and renders the synthesis scenarios of audio_synth_golden.txt in the C# engine. A scenario is the plain description tools/audio-ref/gen-golden-synth.mjs gave Chromium: nodes,
    /// connections and parameter automation as JSON.
    /// </summary>
    static class SynthScenarios
    {
        public sealed class Scenario
        {
            public string Name;
            public JsonObject Json;
            public float[] Expected, ExpectedRight;
            public int Stride => (int)(double)Json["stride"];
            public int Channels => (int)(double)Json["channels"];
        }

        public static List<Scenario> Load()
        {
            string[] lines = Golden.Lines("audio_synth_golden.txt");
            var list = new List<Scenario>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("SCN ", StringComparison.Ordinal)) continue;
                var json = (JsonObject)Json.Parse(lines[i].Substring(4));
                string[] f = lines[i + 1].Split(' ');
                string name = (string)json["name"];
                if (f[0] != "OUT" || f[1] != name) throw new InvalidOperationException("fixture out of step at " + name);
                var scenario = new Scenario { Name = name, Json = json };
                int bar = Array.IndexOf(f, "|");
                int end = bar < 0 ? f.Length : bar;
                scenario.Expected = Floats(f, 3, end);
                if (bar >= 0) scenario.ExpectedRight = Floats(f, bar + 1, f.Length);
                list.Add(scenario);
            }
            return list;
        }

        static float[] Floats(string[] f, int from, int to)
        {
            var a = new float[to - from];
            for (int i = 0; i < a.Length; i++) a[i] = float.Parse(f[from + i], CultureInfo.InvariantCulture);
            return a;
        }

        static double D(JsonObject o, string key, double fallback)
        {
            if (!o.TryGetValue(key, out object v) || v == null) return fallback;
            return (double)v;
        }

        static string S(JsonObject o, string key) => (string)o[key];

        static WaveKind Wave(string s)
        {
            switch (s)
            {
                case "sine": return WaveKind.Sine;
                case "triangle": return WaveKind.Triangle;
                case "sawtooth": return WaveKind.Sawtooth;
                case "softsaw": return WaveKind.SoftSaw;
                case "hollow": return WaveKind.Hollow;
                case "bell": return WaveKind.Bell;
                default: throw new ArgumentException(s);
            }
        }

        static BiquadType Filter(string s)
        {
            switch (s)
            {
                case "lowpass": return BiquadType.Lowpass;
                case "highpass": return BiquadType.Highpass;
                case "bandpass": return BiquadType.Bandpass;
                case "peaking": return BiquadType.Peaking;
                default: throw new ArgumentException(s);
            }
        }

        static float[] Buffer(string kind, int frames, int rate)
        {
            switch (kind)
            {
                case "impulse":
                {
                    var a = new float[frames];
                    a[0] = 1;
                    return a;
                }
                case "ramp":
                {
                    var a = new float[frames];
                    for (int i = 0; i < frames; i++) a[i] = (float)i / frames;
                    return a;
                }
                case "white": return AudioNoise.Make(NoiseKind.White, rate);
                case "pink": return AudioNoise.Make(NoiseKind.Pink, rate);
                case "brown": return AudioNoise.Make(NoiseKind.Brown, rate);
                case "velvet": return AudioNoise.Make(NoiseKind.Velvet, rate);
                default: throw new ArgumentException(kind);
            }
        }

        public static void Render(Scenario scn, int coefficientInterval, out float[] left, out float[] right)
        {
            JsonObject j = scn.Json;
            int rate = (int)(double)j["rate"];
            int frames = (int)(double)j["frames"];
            var g = new SynthGraph(rate) { CoefficientInterval = coefficientInterval };
            var nodes = new Dictionary<string, SynthNode> { ["out"] = g.Destination };
            foreach (object o in (List<object>)j["nodes"])
            {
                var d = (JsonObject)o;
                string id = S(d, "id");
                switch (S(d, "type"))
                {
                    case "osc":
                    {
                        OscillatorNode n = g.CreateOscillator(Wave(S(d, "wave")), D(d, "freq", 440));
                        if (D(d, "detune", 0) != 0) n.Detune.Value = D(d, "detune", 0);
                        n.Start(D(d, "start", 0));
                        if (d.TryGetValue("stop", out object stop) && stop != null) n.Stop((double)stop);
                        nodes[id] = n;
                        break;
                    }
                    case "gain": nodes[id] = g.CreateGain(D(d, "value", 1)); break;
                    case "biquad": nodes[id] = g.CreateBiquad(Filter(S(d, "filter")), D(d, "freq", 350), D(d, "q", 1), D(d, "gain", 0)); break;
                    case "delay": nodes[id] = g.CreateDelay(D(d, "time", 0), D(d, "max", 1)); break;
                    case "pan": nodes[id] = g.CreateStereoPanner(D(d, "value", 0)); break;
                    case "const":
                        nodes[id] = g.CreateConstantSource(D(d, "offset", 1));
                        break;
                    case "comp":
                    {
                        DynamicsCompressorNode n = g.CreateDynamicsCompressor();
                        n.Threshold.Value = D(d, "threshold", -24);
                        n.Knee.Value = D(d, "knee", 30);
                        n.Ratio.Value = D(d, "ratio", 12);
                        n.Attack.Value = D(d, "attack", 0.003);
                        n.Release.Value = D(d, "release", 0.25);
                        nodes[id] = n;
                        break;
                    }
                    case "src":
                    {
                        BufferSourceNode n = g.CreateBufferSource(Buffer(S(d, "buffer"), frames, rate));
                        n.PlaybackRate.Value = D(d, "rate", 1);
                        n.Start(D(d, "start", 0), D(d, "offset", 0), D(d, "duration", -1));
                        nodes[id] = n;
                        break;
                    }
                    default: throw new ArgumentException(S(d, "type"));
                }
            }
            foreach (object o in (List<object>)j["connect"])
            {
                var pair = (List<object>)o;
                string a = (string)pair[0], b = (string)pair[1];
                if (b.Contains(".")) nodes[a].Connect(Param(nodes, b));
                else nodes[a].Connect(nodes[b]);
            }
            foreach (object o in (List<object>)j["auto"])
            {
                var a = (JsonObject)o;
                AudioParam p = Param(nodes, S(a, "node") + "." + S(a, "param"));
                foreach (object opObject in (List<object>)a["ops"])
                {
                    var op = (List<object>)opObject;
                    string kind = (string)op[0];
                    double v = (double)op[1];
                    switch (kind)
                    {
                        case "set": p.SetValueAtTime(v, (double)op[2]); break;
                        case "lin": p.LinearRampToValueAtTime(v, (double)op[2]); break;
                        case "exp": p.ExponentialRampToValueAtTime(v, (double)op[2]); break;
                        case "target": p.SetTargetAtTime(v, (double)op[2], (double)op[3]); break;
                        case "cancel": p.CancelScheduledValues(v); break;
                        case "value": p.Value = v; break;
                        default: throw new ArgumentException(kind);
                    }
                }
            }
            g.Render(frames, out left, out right);
        }

        static AudioParam Param(Dictionary<string, SynthNode> nodes, string path)
        {
            string[] parts = path.Split('.');
            SynthNode n = nodes[parts[0]];
            string name = parts[1];
            switch (n)
            {
                case GainNode gain when name == "gain": return gain.Gain;
                case OscillatorNode osc when name == "frequency": return osc.Frequency;
                case OscillatorNode osc when name == "detune": return osc.Detune;
                case BiquadNode bq when name == "frequency": return bq.Frequency;
                case BiquadNode bq when name == "q": return bq.Q;
                case BiquadNode bq when name == "gain": return bq.Gain;
                case DelayNode delay when name == "delayTime": return delay.DelayTime;
                case StereoPannerNode pan when name == "pan": return pan.Pan;
                case ConstantSourceNode c when name == "offset": return c.Offset;
                case BufferSourceNode s when name == "rate": return s.PlaybackRate;
                default: throw new ArgumentException(path);
            }
        }

        /// <summary>The largest difference between what the engine rendered (taking every stride-th frame) and the expected values, and where.</summary>
        public static double MaxError(float[] rendered, float[] expected, int stride, out int at)
        {
            double worst = 0;
            at = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                double e = Math.Abs(rendered[i * stride] - expected[i]);
                if (e > worst)
                {
                    worst = e;
                    at = i * stride;
                }
            }
            return worst;
        }
    }
}
