using System;
using System.Linq;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class StrokeFontTests
    {
        [Test]
        public void EveryLetterAndDigitHasStrokes()
        {
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
            {
                Glyph g = StrokeFont.LatinGlyph(c);
                Assert.Greater(g.Strokes.Count, 0, c.ToString());
                Assert.Greater(g.Advance, 0.05, c.ToString());
            }
        }

        [Test]
        public void PunctuationAndTheYenSignAreThere()
        {
            foreach (char c in ".,:/-+%¥ '—&") Assert.IsTrue(StrokeFont.HasLatin(c), $"'{c}'");
            Assert.AreEqual(0, StrokeFont.LatinGlyph(' ').Strokes.Count, "a space draws nothing");
        }

        [Test]
        public void LowercaseLettersGiveTheirCapitals()
        {
            Assert.AreSame(StrokeFont.LatinGlyph('A'), StrokeFont.LatinGlyph('a'));
            Assert.IsTrue(StrokeFont.HasLatin('q'));
        }

        [Test]
        public void EveryGlyphStaysInsideItsBox()
        {
            foreach (char c in StrokeFont.LatinCharacters)
            {
                Glyph g = StrokeFont.LatinGlyph(c);
                foreach (GlyphStroke s in g.Strokes)
                {
                    Assert.GreaterOrEqual(s.Points.Length, 2, $"'{c}' has a stroke of one point");
                    foreach ((double x, double y) p in s.Points)
                    {
                        Assert.That(p.x, Is.InRange(-0.08, g.Advance + 0.02), $"'{c}' x");
                        Assert.That(p.y, Is.InRange(-0.02, 1.1), $"'{c}' y");
                    }
                }
            }
        }

        [Test]
        public void EveryLatinLetterFillsTheWholeCapHeight()
        {
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            {
                var ys = StrokeFont.LatinGlyph(c).Strokes.SelectMany(s => s.Points).Select(p => p.y).ToList();
                Assert.AreEqual(0.0, ys.Min(), 0.05, $"'{c}' top");
                Assert.AreEqual(1.0, ys.Max(), 0.05, $"'{c}' baseline");
            }
        }

        [Test]
        public void TheKanaTheKanjiAndTheIconsAreAllThere()
        {
            Assert.AreEqual(29, StrokeFont.KanaKeys.Count);
            Assert.AreEqual(12, StrokeFont.KanjiKeys.Count);
            Assert.AreEqual(8, StrokeFont.IconKeys.Count);
            foreach (string key in StrokeFont.KanaKeys.Concat(StrokeFont.KanjiKeys).Concat(StrokeFont.IconKeys))
            {
                Glyph g = StrokeFont.Symbol(key);
                Assert.Greater(g.Strokes.Count, 0, key);
                foreach (GlyphStroke s in g.Strokes)
                {
                    foreach ((double x, double y) p in s.Points)
                    {
                        Assert.That(p.x, Is.InRange(0.0, 1.0), $"{key} x");
                        Assert.That(p.y, Is.InRange(0.0, 1.0), $"{key} y");
                    }
                }
            }
        }

        [Test]
        public void KnownGlyphsMatchThePrototypesCoordinates()
        {
            // 'ko' is one open box; 'kJu' is a cross; the 'star' closes on its first point
            Assert.AreEqual(1, StrokeFont.Symbol("ko").Strokes.Count);
            Assert.AreEqual(4, StrokeFont.Symbol("ko").Strokes[0].Points.Length);
            Assert.AreEqual((0.16, 0.22), StrokeFont.Symbol("ko").Strokes[0].Points[0]);
            Assert.AreEqual(2, StrokeFont.Symbol("kJu").Strokes.Count);
            var star = StrokeFont.Symbol("star").Strokes[0].Points;
            Assert.AreEqual(star[0], star[star.Length - 1]);
        }

        [Test]
        public void ATextIsAsWideAsItsLettersPlusTheirGaps()
        {
            double ab = StrokeFont.LatinGlyph('A').Advance + StrokeFont.Tracking + StrokeFont.LatinGlyph('B').Advance;
            Assert.AreEqual(ab, StrokeFont.Measure("AB"), 1e-9);
            Assert.AreEqual(StrokeFont.LatinGlyph('A').Advance, StrokeFont.Measure("A"), 1e-9, "no gap after the last letter");
            Assert.AreEqual(0.0, StrokeFont.Measure(""));
        }

        [Test]
        public void EveryWordOnAnySignCanBeDrawn()
        {
            foreach (var w in NeonAtlas.Words) Assert.DoesNotThrow(() => StrokeFont.Measure(w.Text), w.Text);
            foreach (string w in NeonAtlas.VerticalWords) Assert.DoesNotThrow(() => StrokeFont.Measure(w), w);
        }

        [Test]
        public void MissingGlyphsFailLoudly()
        {
            Assert.Throws<ArgumentException>(() => StrokeFont.LatinGlyph('@'));
            Assert.Throws<ArgumentException>(() => StrokeFont.Symbol("flying-saucer"));
            Assert.Throws<ArgumentNullException>(() => StrokeFont.Symbol(null));
            Assert.Throws<ArgumentNullException>(() => StrokeFont.Measure(null));
            Assert.Throws<ArgumentException>(() => StrokeFont.Measure("HELLO @"));
        }
    }
}
