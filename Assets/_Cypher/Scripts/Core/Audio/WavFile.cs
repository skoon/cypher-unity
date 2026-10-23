using System;
using System.IO;

namespace Cypher.Core
{
    /// <summary>16-bit PCM WAV files, for hearing what the synthesis makes.</summary>
    public static class WavFile
    {
        public const int HeaderBytes = 44;

        /// <summary>The file for one or more equal channels. Samples beyond full scale are held at it; <paramref name="clipped"/> says how many were.</summary>
        public static byte[] Encode(float[][] channels, int sampleRate, out int clipped)
        {
            if (channels == null || channels.Length == 0) throw new ArgumentException("a file has at least one channel", nameof(channels));
            if (channels.Length > 2) throw new ArgumentException("mono or stereo only", nameof(channels));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "not a sample rate");
            int frames = channels[0].Length;
            foreach (float[] c in channels)
            {
                if (c == null || c.Length != frames) throw new ArgumentException("the channels differ in length", nameof(channels));
            }
            int ch = channels.Length;
            long dataBytes = (long)frames * ch * 2;
            if (dataBytes > int.MaxValue - HeaderBytes) throw new ArgumentException("too long for a WAV file", nameof(channels));

            clipped = 0;
            var stream = new MemoryStream(HeaderBytes + (int)dataBytes);
            using (var w = new BinaryWriter(stream))
            {
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write((int)(36 + dataBytes));
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)ch);
                w.Write(sampleRate);
                w.Write(sampleRate * ch * 2);
                w.Write((short)(ch * 2));
                w.Write((short)16);
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write((int)dataBytes);
                for (int i = 0; i < frames; i++)
                {
                    for (int c = 0; c < ch; c++)
                    {
                        float v = channels[c][i];
                        if (float.IsNaN(v)) throw new ArgumentException($"sample {i} of channel {c} is not a number", nameof(channels));
                        if (v > 1f || v < -1f) clipped++;
                        w.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, v)) * 32767.0));
                    }
                }
            }
            return stream.ToArray();
        }

        public static byte[] Encode(float[] mono, int sampleRate, out int clipped) => Encode(new[] { mono }, sampleRate, out clipped);

        /// <summary>Reads a file written by <see cref="Encode(float[][], int, out int)"/> back, to check it.</summary>
        public static float[][] Decode(byte[] file, out int sampleRate)
        {
            if (file == null || file.Length < HeaderBytes) throw new InvalidDataException("too short to be a WAV file");
            using (var r = new BinaryReader(new MemoryStream(file)))
            {
                if (new string(r.ReadChars(4)) != "RIFF") throw new InvalidDataException("not a RIFF file");
                r.ReadInt32();
                if (new string(r.ReadChars(8)) != "WAVEfmt ") throw new InvalidDataException("not a WAVE file");
                if (r.ReadInt32() != 16) throw new InvalidDataException("not plain PCM");
                if (r.ReadInt16() != 1) throw new InvalidDataException("not PCM");
                int ch = r.ReadInt16();
                sampleRate = r.ReadInt32();
                r.ReadInt32();
                r.ReadInt16();
                if (r.ReadInt16() != 16) throw new InvalidDataException("not 16-bit");
                if (new string(r.ReadChars(4)) != "data") throw new InvalidDataException("no data chunk");
                int bytes = r.ReadInt32();
                if (bytes != file.Length - HeaderBytes) throw new InvalidDataException("the data chunk is not the rest of the file");
                int frames = bytes / (ch * 2);
                var channels = new float[ch][];
                for (int c = 0; c < ch; c++) channels[c] = new float[frames];
                for (int i = 0; i < frames; i++)
                {
                    for (int c = 0; c < ch; c++) channels[c][i] = r.ReadInt16() / 32767f;
                }
                return channels;
            }
        }
    }
}
