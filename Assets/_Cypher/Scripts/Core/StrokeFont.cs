using System;
using System.Collections.Generic;
using System.Globalization;

namespace Cypher.Core
{
    /// <summary>One drawn line: points in a glyph's own space (x across, y down, the glyph box 0 to 1 tall).</summary>
    public readonly struct GlyphStroke
    {
        public readonly (double x, double y)[] Points;

        public GlyphStroke((double x, double y)[] points)
        {
            Points = points;
        }
    }

    /// <summary>A glyph as strokes, with how far the pen moves on after it.</summary>
    public sealed class Glyph
    {
        public readonly string Name;
        public readonly double Advance;
        public readonly IReadOnlyList<GlyphStroke> Strokes;

        public Glyph(string name, double advance, IReadOnlyList<GlyphStroke> strokes)
        {
            Name = name;
            Advance = advance;
            Strokes = strokes;
        }
    }

    /// <summary>
    /// Every letter on a sign, drawn as strokes so no operating-system font is needed and the neon look never depends on what is
    /// installed. Three sets: the Latin capitals, digits and a little punctuation (this port's own design, one weight that the painter
    /// thickens or thins), and the prototype's katakana, kanji-like blocks and pictograms, which it drew as strokes too. Latin glyphs are
    /// <see cref="CapHeight"/> tall with y down; kana, kanji and icons fill a unit square.
    /// </summary>
    public static class StrokeFont
    {
        /// <summary>The Latin glyphs are 1 tall in their own units.</summary>
        public const double CapHeight = 1.0;

        /// <summary>Space the pen leaves between two Latin letters, as a share of the cap height.</summary>
        public const double Tracking = 0.22;

        static readonly Dictionary<char, Glyph> Latin = BuildLatin();
        static readonly Dictionary<string, Glyph> Kana = BuildKana();
        static readonly Dictionary<string, Glyph> Kanji = BuildKanji();
        static readonly Dictionary<string, Glyph> Icons = BuildIcons();

        public static IReadOnlyList<string> KanaKeys { get; } = new[]
        {
            "a", "i", "u", "e", "o", "ka", "ki", "ku", "ke", "ko", "sa", "shi", "su", "se", "so",
            "ta", "tsu", "na", "ni", "ha", "ho", "mi", "mu", "me", "ru", "re", "ro", "n", "wa",
        };

        public static IReadOnlyList<string> KanjiKeys { get; } = new[]
        {
            "kHi", "kTa", "kNaka", "kDai", "kOu", "kKawa", "kJu", "kKuchi", "kYama", "kHon", "kSei", "kDen",
        };

        public static IReadOnlyList<string> IconKeys { get; } = new[]
        {
            "arrow", "cross", "martini", "bowl", "bolt", "star", "target", "warn",
        };

        /// <summary>Every character the Latin set can draw (besides the space, which draws nothing).</summary>
        public static IReadOnlyCollection<char> LatinCharacters => Latin.Keys;

        public static bool HasLatin(char c) => Latin.ContainsKey(char.ToUpperInvariant(c));

        /// <summary>The glyph for a character; lowercase letters give their capitals. Unknown characters fail loudly.</summary>
        public static Glyph LatinGlyph(char c)
        {
            if (!Latin.TryGetValue(char.ToUpperInvariant(c), out Glyph g)) throw new ArgumentException($"The stroke font has no glyph for '{c}'.", nameof(c));
            return g;
        }

        /// <summary>A kana, a kanji-like block or an icon by its key (for example "ka", "kYama", "star").</summary>
        public static Glyph Symbol(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (Kana.TryGetValue(key, out Glyph g) || Kanji.TryGetValue(key, out g) || Icons.TryGetValue(key, out g)) return g;
            throw new ArgumentException($"The stroke font has no kana, kanji or icon called '{key}'.", nameof(key));
        }

        /// <summary>How wide a run of Latin text is, in cap heights, tracking included.</summary>
        public static double Measure(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            double w = 0;
            for (int i = 0; i < text.Length; i++) w += LatinGlyph(text[i]).Advance + (i < text.Length - 1 ? Tracking : 0);
            return w;
        }

        // ---------------------------------------------------------------- the glyph tables

        static GlyphStroke[] Parse(string strokes)
        {
            string[] runs = strokes.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new GlyphStroke[runs.Length];
            for (int i = 0; i < runs.Length; i++)
            {
                string[] pts = runs[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var points = new (double, double)[pts.Length];
                for (int k = 0; k < pts.Length; k++)
                {
                    string[] xy = pts[k].Split(',');
                    points[k] = (double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture));
                }
                result[i] = new GlyphStroke(points);
            }
            return result;
        }

        static Dictionary<char, Glyph> BuildLatin()
        {
            var d = new Dictionary<char, Glyph>();
            void G(char c, double advance, string strokes) => d.Add(c, new Glyph(c.ToString(), advance, Parse(strokes)));

            G(' ', 0.42, "");
            G('A', 0.64, "0,1 .32,0 .64,1; .11,.68 .53,.68");
            G('B', 0.56, "0,0 0,1; 0,0 .38,0 .52,.1 .52,.36 .38,.48 0,.48; .38,.48 .54,.58 .54,.88 .4,1 0,1");
            G('C', 0.6, ".6,.15 .46,.02 .3,0 .14,.06 .02,.22 0,.5 .02,.78 .14,.94 .3,1 .46,.98 .6,.85");
            G('D', 0.6, "0,0 0,1 .3,1 .5,.9 .6,.6 .6,.4 .5,.1 .3,0 0,0");
            G('E', 0.5, ".5,0 0,0 0,1 .5,1; 0,.48 .4,.48");
            G('F', 0.5, ".5,0 0,0 0,1; 0,.48 .4,.48");
            G('G', 0.64, ".62,.15 .48,.02 .3,0 .14,.06 .02,.22 0,.5 .02,.78 .14,.94 .3,1 .5,.98 .62,.86 .62,.55 .34,.55");
            G('H', 0.6, "0,0 0,1; .6,0 .6,1; 0,.5 .6,.5");
            G('I', 0.1, "0,0 0,1");
            G('J', 0.45, ".45,0 .45,.78 .35,.95 .2,1 .08,.95 0,.82");
            G('K', 0.58, "0,0 0,1; .58,0 0,.62; .2,.45 .58,1");
            G('L', 0.48, "0,0 0,1 .48,1");
            G('M', 0.8, "0,1 0,0 .4,.62 .8,0 .8,1");
            G('N', 0.62, "0,1 0,0 .62,1 .62,0");
            G('O', 0.64, ".32,0 .12,.08 0,.3 0,.7 .12,.92 .32,1 .52,.92 .64,.7 .64,.3 .52,.08 .32,0");
            G('P', 0.55, "0,1 0,0 .38,0 .52,.1 .55,.28 .5,.44 .36,.52 0,.52");
            G('Q', 0.66, ".32,0 .12,.08 0,.3 0,.7 .12,.92 .32,1 .52,.92 .64,.7 .64,.3 .52,.08 .32,0; .38,.72 .64,1");
            G('R', 0.58, "0,1 0,0 .38,0 .52,.1 .55,.28 .5,.44 .36,.52 0,.52; .3,.52 .58,1");
            G('S', 0.56, ".54,.14 .4,.02 .22,0 .06,.08 .02,.26 .12,.42 .44,.56 .56,.72 .52,.9 .36,1 .16,.98 .02,.84");
            G('T', 0.6, "0,0 .6,0; .3,0 .3,1");
            G('U', 0.6, "0,0 0,.72 .1,.92 .3,1 .5,.92 .6,.72 .6,0");
            G('V', 0.64, "0,0 .32,1 .64,0");
            G('W', 0.88, "0,0 .2,1 .44,.3 .68,1 .88,0");
            G('X', 0.62, "0,0 .62,1; .62,0 0,1");
            G('Y', 0.62, "0,0 .31,.52 .62,0; .31,.52 .31,1");
            G('Z', 0.56, "0,0 .56,0 0,1 .56,1");

            G('0', 0.5, ".25,0 .08,.1 0,.3 0,.7 .08,.9 .25,1 .42,.9 .5,.7 .5,.3 .42,.1 .25,0");
            G('1', 0.34, "0,.2 .2,0 .2,1");
            G('2', 0.5, "0,.2 .1,.05 .25,0 .42,.06 .5,.22 .44,.4 0,1 .5,1");
            G('3', 0.5, "0,.1 .15,.01 .32,0 .46,.1 .46,.3 .34,.45 .16,.48; .34,.45 .5,.6 .5,.85 .36,.98 .16,1 0,.9");
            G('4', 0.52, ".4,1 .4,0 0,.7 .52,.7");
            G('5', 0.5, ".48,0 .06,0 .02,.45 .24,.4 .42,.46 .5,.65 .46,.88 .3,1 .12,.98 0,.88");
            G('6', 0.5, ".46,.06 .3,0 .14,.08 .02,.3 0,.65 .06,.9 .24,1 .42,.92 .5,.72 .4,.52 .22,.46 .06,.56");
            G('7', 0.5, "0,0 .5,0 .18,1");
            G('8', 0.5, ".25,.48 .08,.38 .06,.12 .25,0 .44,.12 .42,.38 .25,.48 .04,.62 0,.85 .25,1 .5,.85 .46,.62 .25,.48");
            G('9', 0.5, ".04,.94 .2,1 .36,.92 .48,.7 .5,.35 .44,.1 .28,0 .1,.08 0,.28 .1,.48 .28,.54 .44,.44");

            G('.', 0.12, "0,.97 0,1");
            G(',', 0.12, "0,.9 -.05,1.08");
            G(':', 0.12, "0,.3 0,.33; 0,.88 0,.91");
            G('/', 0.42, ".42,0 0,1");
            G('-', 0.4, "0,.55 .4,.55");
            G('+', 0.5, ".25,.3 .25,.8; 0,.55 .5,.55");
            G('%', 0.72, ".05,.05 .17,.05 .17,.2 .05,.2 .05,.05; .55,.8 .67,.8 .67,.95 .55,.95 .55,.8; .62,0 .1,1");
            G('¥', 0.62, "0,0 .31,.5 .62,0; .31,.5 .31,1; .1,.62 .52,.62; .1,.8 .52,.8");   // the yen sign
            // for the names of the places (Mama Kaz's, Pawn & Salvage, Dead Drop — Rain Alley)
            G('\'', 0.12, "0,0 0,.24");
            G('—', 0.8, "0,.55 .8,.55");
            G('&', 0.62, ".62,1 .14,.42 .08,.22 .16,.05 .3,0 .44,.06 .48,.2 .4,.34 .08,.6 0,.78 .08,.94 .26,1 .44,.94 .6,.64");
            return d;
        }

        static Dictionary<string, Glyph> BuildKana()
        {
            var d = new Dictionary<string, Glyph>();
            void G(string k, string strokes) => d.Add(k, new Glyph(k, 1, Parse(strokes)));
            G("a", ".1,.24 .9,.24; .7,.24 .62,.55 .28,.86");
            G("i", ".82,.16 .3,.52; .52,.34 .52,.9");
            G("u", ".5,.06 .5,.22; .14,.32 .86,.32; .86,.32 .84,.66 .58,.88; .14,.32 .14,.54");
            G("e", ".16,.22 .84,.22; .5,.22 .5,.78; .08,.8 .92,.8");
            G("o", ".14,.3 .86,.3; .64,.1 .54,.9; .54,.46 .24,.82");
            G("ka", ".16,.3 .8,.3; .72,.18 .7,.62 .44,.88; .36,.3 .18,.88");
            G("ki", ".14,.32 .86,.26; .1,.56 .9,.5; .6,.1 .44,.92");
            G("ku", ".18,.2 .8,.2; .62,.2 .56,.54 .24,.88; .18,.2 .18,.42");
            G("ke", ".32,.12 .16,.44; .22,.34 .84,.34; .68,.34 .64,.66 .4,.9");
            G("ko", ".16,.22 .84,.22 .84,.8 .2,.8");
            G("sa", ".1,.4 .9,.36; .34,.14 .3,.62; .66,.12 .62,.9");
            G("shi", ".18,.2 .34,.3; .16,.44 .32,.54; .8,.18 .74,.68 .24,.86");
            G("su", ".14,.22 .86,.22; .72,.22 .28,.6; .42,.5 .84,.88");
            G("se", ".12,.4 .88,.34; .44,.14 .4,.74 .64,.88 .86,.7");
            G("so", ".24,.2 .36,.38; .78,.16 .34,.88");
            G("ta", ".18,.22 .74,.22; .74,.22 .62,.6 .24,.9; .34,.46 .68,.8");
            G("tsu", ".2,.18 .28,.36; .5,.18 .56,.36; .82,.16 .76,.68 .22,.86");
            G("na", ".1,.36 .9,.36; .62,.12 .5,.9");
            G("ni", ".22,.3 .78,.3; .1,.74 .9,.74");
            G("ha", ".38,.2 .16,.86; .58,.2 .84,.86");
            G("ho", ".1,.34 .9,.34; .5,.1 .5,.9; .44,.52 .22,.86; .56,.52 .78,.86");
            G("mi", ".22,.2 .76,.3; .24,.46 .72,.56; .18,.72 .8,.84");
            G("mu", ".58,.16 .24,.62; .24,.62 .74,.66; .4,.44 .86,.88");
            G("me", ".2,.28 .8,.76; .72,.14 .28,.62 .52,.88 .8,.6");
            G("ru", ".28,.18 .24,.86; .54,.2 .5,.66 .74,.86 .84,.58");
            G("re", ".28,.14 .3,.7 .78,.86 .86,.52");
            G("ro", ".2,.22 .8,.22 .8,.8 .2,.8 .2,.22");
            G("n", ".22,.24 .38,.32; .2,.48 .36,.56; .22,.86 .82,.28");
            G("wa", ".14,.26 .86,.26; .3,.26 .26,.88; .7,.26 .66,.66 .86,.86");
            return d;
        }

        static Dictionary<string, Glyph> BuildKanji()
        {
            var d = new Dictionary<string, Glyph>();
            void G(string k, string strokes) => d.Add(k, new Glyph(k, 1, Parse(strokes)));
            // Blockier kanji-like forms: these carry the signboard weight.
            G("kHi", ".24,.18 .76,.18 .76,.84 .24,.84 .24,.18; .24,.51 .76,.51");
            G("kTa", ".18,.18 .82,.18 .82,.82 .18,.82 .18,.18; .18,.5 .82,.5; .5,.18 .5,.82");
            G("kNaka", ".26,.22 .74,.22 .74,.66 .26,.66 .26,.22; .5,.06 .5,.94");
            G("kDai", ".08,.34 .92,.34; .5,.1 .5,.4; .48,.36 .14,.92; .52,.36 .88,.92");
            G("kOu", ".18,.2 .82,.2; .26,.5 .74,.5; .12,.82 .88,.82; .5,.2 .5,.82");
            G("kKawa", ".22,.16 .16,.86; .5,.22 .5,.8; .8,.18 .82,.84");
            G("kJu", ".1,.5 .9,.5; .5,.12 .5,.88");
            G("kKuchi", ".24,.24 .76,.24 .76,.78 .24,.78 .24,.24");
            G("kYama", ".14,.28 .14,.84; .5,.12 .5,.84; .86,.28 .86,.84; .12,.84 .88,.84");
            G("kHon", ".08,.34 .92,.34; .5,.1 .5,.9; .48,.4 .16,.86; .52,.4 .84,.86; .28,.76 .72,.76");
            G("kSei", ".3,.12 .16,.44; .1,.44 .9,.44; .16,.68 .86,.68; .12,.9 .9,.9; .52,.2 .48,.9");
            G("kDen", ".16,.14 .84,.14; .26,.3 .74,.3 .74,.72 .26,.72 .26,.3; .26,.44 .74,.44; .26,.58 .74,.58; .5,.3 .5,.9");
            return d;
        }

        static Dictionary<string, Glyph> BuildIcons()
        {
            var d = new Dictionary<string, Glyph>();
            void G(string k, string strokes) => d.Add(k, new Glyph(k, 1, Parse(strokes)));
            G("arrow", ".14,.5 .84,.5; .6,.24 .86,.5 .6,.76");
            G("cross", ".5,.14 .5,.86; .14,.5 .86,.5");
            G("martini", ".16,.22 .84,.22 .5,.6 .16,.22; .5,.6 .5,.84; .3,.86 .7,.86");
            G("bowl", ".12,.46 .88,.46 .72,.84 .28,.84 .12,.46; .34,.34 .4,.16; .52,.32 .58,.12; .68,.34 .74,.18");
            G("bolt", ".6,.1 .3,.52 .52,.52 .36,.9 .72,.44 .48,.44 .6,.1");
            G("star", ".5,.1 .61,.4 .9,.4 .66,.58 .76,.88 .5,.7 .24,.88 .34,.58 .1,.4 .39,.4 .5,.1");
            G("target", ".5,.12 .5,.3; .5,.7 .5,.88; .12,.5 .3,.5; .7,.5 .88,.5; .5,.24 .72,.34 .78,.58 .6,.76 .36,.72 .24,.5 .34,.3 .5,.24");
            G("warn", ".5,.12 .9,.86 .1,.86 .5,.12; .5,.38 .5,.62; .5,.74 .5,.78");
            return d;
        }
    }
}
