using System;
using System.IO;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    /// <summary>Leaves the sign atlas and the board atlas as PNGs in <c>%TEMP%/cypher-unity/pictures</c> when CYPHER_PICTURES is set, for looking at.</summary>
    public class NeonAtlasPictureTests
    {
        [Test]
        public void LeaveTheAtlasAsAPictureWhenAsked()
        {
            if (Environment.GetEnvironmentVariable("CYPHER_PICTURES") == null) return;
            byte[] bytes = NeonAtlas.Render(NeonAtlas.Layout());
            int n = NeonAtlas.Size;
            var top = new byte[bytes.Length];
            for (int y = 0; y < n; y++) Buffer.BlockCopy(bytes, (n - 1 - y) * n, top, y * n, n);   // the bytes start at the bottom row
            string path = Path.Combine(TestPng.PictureDirectory(), "sign_atlas.png");
            TestPng.WriteGrey(path, top, n, n);
            Assert.IsTrue(File.Exists(path));
        }

        [Test]
        public void LeaveTheBoardAtlasAsAPictureWhenAsked()
        {
            if (Environment.GetEnvironmentVariable("CYPHER_PICTURES") == null) return;
            byte[] bytes = BoardArt.Render();
            int n = BoardArt.Size;
            var top = new byte[bytes.Length];
            for (int y = 0; y < n; y++) Buffer.BlockCopy(bytes, (n - 1 - y) * n * 4, top, y * n * 4, n * 4);
            string path = Path.Combine(TestPng.PictureDirectory(), "board_atlas.png");
            TestPng.WriteRgba(path, top, n, n);
            Assert.IsTrue(File.Exists(path));
        }
    }
}
