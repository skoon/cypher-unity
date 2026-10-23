using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Cypher.Core
{
    public sealed class ExportOptions
    {
        public int SampleRate = 48000;
        public uint Seed = 1;
        public double BedSeconds = 60;
        /// <summary>Seconds of the score rendered at each of its two intensities; 0 leaves the score out.</summary>
        public double ScoreSeconds = 45;
        /// <summary>The intensity of the driven score; the calm one is the game's own.</summary>
        public double DrivenIntensity = 0.85;
        /// <summary>Which of the sound bank's sounds to write.</summary>
        public Func<string, bool> Sounds = _ => true;
        public bool IncludeBeds = true;
    }

    public sealed class ExportedFile
    {
        public string Kind;
        public string Name;
        public string Path;
        public string Bus;
        public double Seconds;
        /// <summary>What the file was scaled by to be as the game mixes it: the sound's gain and its bus's base level.</summary>
        public double Gain;
        /// <summary>The sound as made, before the gain.</summary>
        public AudioStats Raw;
        public int Clipped;

        public double HeardPeakDb => Raw.PeakDb + 20 * Math.Log10(Gain);
        public double HeardRmsDb => Raw.RmsDb + 20 * Math.Log10(Gain);
    }

    /// <summary>
    /// Writes what the synthesis makes as WAV files to listen to, at the level the game mixes them (each sound's own gain and its bus's base level, before the Options sliders and the master), with a report of
    /// their levels. Nothing here can be heard by the code that checks it, so this is how a person hears it.
    /// </summary>
    public static class AudioExport
    {
        public const string ReportFile = "levels.md";

        public static IReadOnlyList<ExportedFile> Run(string directory, ExportOptions options)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("no directory to write to", nameof(directory));
            if (options == null) throw new ArgumentNullException(nameof(options));
            Directory.CreateDirectory(Path.Combine(directory, "sounds"));
            var noise = new AudioNoiseBank(options.SampleRate);
            var files = new List<ExportedFile>();

            foreach (string name in SoundBank.Names.Where(options.Sounds))
            {
                RenderedSound sound = SoundBank.Render(name, noise, options.Seed);
                SoundDef def = sound.Def;
                files.Add(Write(directory, "sounds", name, "sound", def.Bus, new[] { sound.Samples }, options.SampleRate, def.Gain * BusLevels.Base(def.Bus)));
            }

            if (options.IncludeBeds)
            {
                foreach ((string name, BedState state) in BedSettings())
                {
                    float[][] bed = RenderBed(noise, options, state);
                    files.Add(Write(directory, "", name, "bed", AudioBus.Ambience, bed, options.SampleRate, BusLevels.Base(AudioBus.Ambience)));
                }
            }

            if (options.ScoreSeconds > 0)
            {
                foreach ((string name, double intensity) in new[] { ("score-calm", 0.0), ("score-driven", options.DrivenIntensity) })
                {
                    ScoreRender.Render(noise, options.Seed, options.ScoreSeconds, intensity, out float[] left, out float[] right);
                    files.Add(Write(directory, "", name, "score", AudioBus.Music, new[] { left, right }, options.SampleRate, BusLevels.Base(AudioBus.Music)));
                }
            }

            File.WriteAllText(Path.Combine(directory, ReportFile), Report(files, options), new UTF8Encoding(false));
            return files;
        }

        /// <summary>The weather the beds are written at: dry, the game's rain, and the rain heard from indoors.</summary>
        public static IEnumerable<(string name, BedState state)> BedSettings()
        {
            yield return ("bed-dry", new BedState(0, RainModel.DefaultWind, 0, 1));
            yield return ("bed-rain", new BedState(NightRig.DefaultRain, RainModel.DefaultWind, 0, 1));
            yield return ("bed-rain-indoors", new BedState(NightRig.DefaultRain, RainModel.DefaultWind, 1, 1));
        }

        // the weather takes a few seconds to settle after it is set; that stretch is not written
        const double SettleSeconds = 4;

        static float[][] RenderBed(AudioNoiseBank noise, ExportOptions options, BedState state)
        {
            var bed = new Bed(noise, new Rng(options.Seed), 0, Bed.EmitterMode.Inline);
            bed.Main.CoefficientInterval = 32;   // as the game plays it (BedPlayer)
            bed.Advance(0, state);
            int settle = (int)(SettleSeconds * options.SampleRate), frames = (int)(options.BedSeconds * options.SampleRate);
            bed.Main.Render(settle + frames, out float[] left, out float[] right);
            return new[] { left.Skip(settle).ToArray(), right.Skip(settle).ToArray() };
        }

        static ExportedFile Write(string directory, string sub, string name, string kind, AudioBus bus, float[][] channels, int rate, double gain)
        {
            var heard = new float[channels.Length][];
            for (int c = 0; c < channels.Length; c++)
            {
                heard[c] = new float[channels[c].Length];
                for (int i = 0; i < heard[c].Length; i++) heard[c][i] = (float)(channels[c][i] * gain);
            }
            byte[] wav = WavFile.Encode(heard, rate, out int clipped);
            string relative = (sub.Length == 0 ? "" : sub + "/") + name + ".wav";
            File.WriteAllBytes(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)), wav);
            return new ExportedFile
            {
                Kind = kind, Name = name, Path = relative, Bus = bus.ToString(), Seconds = channels[0].Length / (double)rate, Gain = gain,
                Raw = AudioAnalysis.Analyze(channels, rate), Clipped = clipped,
            };
        }

        static string Db(double v) => double.IsInfinity(v) || double.IsNaN(v) ? "-inf" : v.ToString("0.0", CultureInfo.InvariantCulture);

        static string Row(ExportedFile f) =>
            string.Format(CultureInfo.InvariantCulture, "| {0} | {1} | {2:0.00} | {3:0.00} | {4} | {5} | {6} | {7:0} | {8} |",
                f.Name, f.Bus, f.Seconds, f.Gain, Db(f.HeardPeakDb), Db(f.HeardRmsDb), Db(f.Raw.PeakDb), f.Raw.Centroid, f.Clipped);

        const string Head = "| file | bus | seconds | gain | heard peak dB | heard RMS dB | made peak dB | centroid Hz | clipped |\n|---|---|---|---|---|---|---|---|---|\n";

        public static string Report(IReadOnlyList<ExportedFile> files, ExportOptions options)
        {
            var sb = new StringBuilder();
            sb.Append("# Audio export\n\n");
            sb.AppendFormat(CultureInfo.InvariantCulture, "Seed {0}, {1} Hz. Every file is at the level the game mixes it: the sound's own gain times its bus's base level (ambience 0.85, effects 0.95, interface 0.6, music 0.55, voices 1.0), ", options.Seed, options.SampleRate);
            sb.Append("before the Options sliders, the reverb and the master (0.9). \"Heard\" levels are dB below full scale in the file; \"made\" is the sound as the recipe renders it.\n");
            sb.Append("The sounds are dry and flat: the game places them, muffles them behind walls and adds the room.\n\n");

            List<ExportedFile> beds = files.Where(f => f.Kind != "sound").ToList();
            if (beds.Count > 0)
            {
                sb.Append("## Beds and score\n\n").Append(Head);
                foreach (ExportedFile f in beds) sb.Append(Row(f)).Append('\n');
                sb.Append('\n');
            }

            List<ExportedFile> sounds = files.Where(f => f.Kind == "sound").OrderByDescending(f => f.HeardRmsDb).ToList();
            if (sounds.Count > 0)
            {
                sb.Append("## Sounds, loudest first\n\n").Append(Head);
                foreach (ExportedFile f in sounds) sb.Append(Row(f)).Append('\n');
                sb.Append('\n');
            }

            ExportedFile rain = beds.FirstOrDefault(f => f.Name == "bed-rain");
            List<ExportedFile> ui = sounds.Where(f => f.Bus == AudioBus.Ui.ToString()).ToList();
            if (rain != null && ui.Count > 0)
            {
                ExportedFile quietest = ui.OrderBy(f => f.HeardPeakDb).First();
                sb.Append("## Against the bed\n\n");
                double over = quietest.HeardPeakDb - rain.HeardRmsDb;
                sb.AppendFormat(CultureInfo.InvariantCulture, "The quietest menu sound ({0}) peaks {1:0.0} dB over the rain bed's RMS with the world at full level. ", quietest.Name, over);
                sb.AppendFormat(CultureInfo.InvariantCulture, "The menus open over a ducked world, though: {0:0.0} dB over with the world paused (ducked to {1}), {2:0.0} dB over with the in-game menu duck ({3}).\n",
                    over - 20 * Math.Log10(BusLevels.DuckPaused), BusLevels.DuckPaused, over - 20 * Math.Log10(BusLevels.DuckMenu), BusLevels.DuckMenu);
            }
            return sb.ToString();
        }
    }
}
