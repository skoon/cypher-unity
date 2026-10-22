using System;
using System.IO;
using System.IO.Compression;

namespace Cypher.Tests
{
    /// <summary>Writes 8-bit grey or RGBA pictures as PNG, so a test can leave an image to look at. Test support only.</summary>
    public static class TestPng
    {
        /// <summary>Row 0 of <paramref name="rows"/> is the top of the picture.</summary>
        public static void WriteGrey(string path, byte[] rows, int width, int height)
        {
            if (rows.Length != width * height) throw new ArgumentException("size does not match");
            Write(path, rows, width, height, 1, 0);
        }

        public static void WriteRgba(string path, byte[] rows, int width, int height)
        {
            if (rows.Length != width * height * 4) throw new ArgumentException("size does not match");
            Write(path, rows, width, height, 4, 6);
        }

        /// <summary>The destination for pictures: <c>%TEMP%/cypher-unity/pictures</c>.</summary>
        public static string PictureDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cypher-unity", "pictures");
            Directory.CreateDirectory(dir);
            return dir;
        }

        static void Write(string path, byte[] rows, int width, int height, int channels, int colourType)
        {
            var raw = new byte[(width * channels + 1) * height];
            for (int y = 0; y < height; y++)
            {
                raw[y * (width * channels + 1)] = 0;
                Buffer.BlockCopy(rows, y * width * channels, raw, y * (width * channels + 1) + 1, width * channels);
            }
            byte[] zipped;
            using (var ms = new MemoryStream())
            {
                ms.WriteByte(0x78); ms.WriteByte(0x9C);
                using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, true)) deflate.Write(raw, 0, raw.Length);
                uint a = 1, b = 0;
                foreach (byte v in raw) { a = (a + v) % 65521; b = (b + a) % 65521; }
                uint adler = (b << 16) | a;
                ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16)); ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
                zipped = ms.ToArray();
            }
            using (var f = File.Create(path))
            {
                f.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var header = new byte[13];
                Be(header, 0, (uint)width); Be(header, 4, (uint)height);
                header[8] = 8; header[9] = (byte)colourType;
                Chunk(f, "IHDR", header);
                Chunk(f, "IDAT", zipped);
                Chunk(f, "IEND", new byte[0]);
            }
        }

        static void Be(byte[] dst, int at, uint v)
        {
            dst[at] = (byte)(v >> 24); dst[at + 1] = (byte)(v >> 16); dst[at + 2] = (byte)(v >> 8); dst[at + 3] = (byte)v;
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; Be(len, 0, (uint)data.Length);
            s.Write(len, 0, 4);
            var body = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, body, 4, data.Length);
            s.Write(body, 0, body.Length);
            var crc = new byte[4]; Be(crc, 0, Crc(body));
            s.Write(crc, 0, 4);
        }

        static uint Crc(byte[] data)
        {
            uint c = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                c ^= b;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            return c ^ 0xFFFFFFFF;
        }
    }
}
