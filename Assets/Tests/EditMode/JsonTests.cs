using System;
using System.Collections.Generic;
using System.Globalization;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class JsonTests
    {
        [Test]
        public void ParsesPrimitives()
        {
            Assert.AreEqual(true, Json.Parse("true"));
            Assert.AreEqual(false, Json.Parse(" false "));
            Assert.IsNull(Json.Parse("null"));
            Assert.AreEqual("hi", Json.Parse("\"hi\""));
            Assert.AreEqual(42.0, Json.Parse("42"));
            Assert.AreEqual(-3.5, Json.Parse("-3.5"));
        }

        [TestCase("0", 0.0)]
        [TestCase("-0", 0.0)]
        [TestCase("7", 7.0)]
        [TestCase("-12", -12.0)]
        [TestCase("0.25", 0.25)]
        [TestCase("1e3", 1000.0)]
        [TestCase("1E+3", 1000.0)]
        [TestCase("2.5e-2", 0.025)]
        [TestCase("123456789012", 123456789012.0)]
        public void ParsesNumbers(string text, double expected)
        {
            Assert.AreEqual(expected, (double)Json.Parse(text), 1e-12);
        }

        [Test]
        public void ParsesNestedStructures()
        {
            var root = (JsonObject)Json.Parse("{ \"a\": [1, 2, {\"b\": null}], \"c\": { \"d\": \"e\" }, \"f\": [] , \"g\": {} }");
            var a = (List<object>)root["a"];
            Assert.AreEqual(3, a.Count);
            Assert.AreEqual(1.0, a[0]);
            Assert.IsNull(((JsonObject)a[2])["b"]);
            Assert.AreEqual("e", ((JsonObject)root["c"])["d"]);
            Assert.AreEqual(0, ((List<object>)root["f"]).Count);
            Assert.AreEqual(0, ((JsonObject)root["g"]).Count);
        }

        [Test]
        public void ObjectsKeepTheOrderTheyWereWrittenIn()
        {
            var root = (JsonObject)Json.Parse("{\"z\":1,\"a\":2,\"m\":3}");
            CollectionAssert.AreEqual(new[] { "z", "a", "m" }, root.Keys);
        }

        [Test]
        public void ParsesAllEscapes()
        {
            Assert.AreEqual("\"\\/\b\f\n\r\t", Json.Parse("\"\\\"\\\\\\/\\b\\f\\n\\r\\t\""));
            Assert.AreEqual("A", Json.Parse("\"\\u0041\""));
            Assert.AreEqual("\u00e9", Json.Parse("\"\\u00E9\""));
            Assert.AreEqual("\ud83d\ude00", Json.Parse("\"\\ud83d\\ude00\""), "a surrogate pair passes through");
            Assert.AreEqual("caf\u00e9 \u65e5\u672c", Json.Parse("\"caf\u00e9 \u65e5\u672c\""));
        }

        [Test]
        public void ABomAtTheStartIsIgnored()
        {
            Assert.AreEqual(1.0, Json.Parse("\uFEFF1"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("{")]
        [TestCase("[1,")]
        [TestCase("[1,]")]
        [TestCase("{\"a\":1,}")]
        [TestCase("{\"a\" 1}")]
        [TestCase("{a:1}")]
        [TestCase("{\"a\":1 \"b\":2}")]
        [TestCase("[1 2]")]
        [TestCase("\"abc")]
        [TestCase("\"bad \\q escape\"")]
        [TestCase("\"short \\u12\"")]
        [TestCase("\"bad \\u12zz\"")]
        [TestCase("\"tab\there\"")]
        [TestCase("01")]
        [TestCase("+1")]
        [TestCase(".5")]
        [TestCase("1.")]
        [TestCase("1e")]
        [TestCase("-")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("1e999")]
        [TestCase("tru")]
        [TestCase("nulll")]
        [TestCase("1 2")]
        [TestCase("{} {}")]
        [TestCase("// comment\n1")]
        [TestCase("{\"a\":1,\"a\":2}")]
        public void RejectsMalformedDocuments(string text)
        {
            Assert.Throws<JsonException>(() => Json.Parse(text));
        }

        [Test]
        public void ErrorsSayWhere()
        {
            JsonException e = Assert.Throws<JsonException>(() => Json.Parse("{\"a\": 1, \"b\": oops}"));
            Assert.AreEqual(14, e.Position);
            StringAssert.Contains("at character 14", e.Message);
        }

        [Test]
        public void NestingDeeperThanTheLimitIsRejectedNotAStackOverflow()
        {
            string ok = new string('[', Json.MaxDepth) + new string(']', Json.MaxDepth);
            Assert.DoesNotThrow(() => Json.Parse(ok));
            string deep = new string('[', Json.MaxDepth + 2) + new string(']', Json.MaxDepth + 2);
            Assert.Throws<JsonException>(() => Json.Parse(deep));
            Assert.Throws<JsonException>(() => Json.Parse(new string('[', 100000)));
        }

        [Test]
        public void NullTextIsAnArgumentError()
        {
            Assert.Throws<ArgumentNullException>(() => Json.Parse(null));
        }

        [Test]
        public void WritesIndentedAndCompactForms()
        {
            var obj = new JsonObject { ["a"] = 1, ["b"] = new List<object> { true, null, "x" }, ["c"] = new JsonObject() };
            Assert.AreEqual("{\"a\":1,\"b\":[true,null,\"x\"],\"c\":{}}", Json.Write(obj, indent: false));
            Assert.AreEqual("{\n  \"a\": 1,\n  \"b\": [\n    true,\n    null,\n    \"x\"\n  ],\n  \"c\": {}\n}", Json.Write(obj));
            Assert.AreEqual("[]", Json.Write(new List<object>()));
        }

        [Test]
        public void WritesIntegralNumbersWithoutADecimalPointAndOthersRoundTrip()
        {
            Assert.AreEqual("540", Json.Write(540.0));
            Assert.AreEqual("-3", Json.Write(-3.0));
            Assert.AreEqual("0", Json.Write(-0.0));
            Assert.AreEqual("7", Json.Write(7));
            Assert.AreEqual("9000000000", Json.Write(9000000000L));
            foreach (double d in new[] { 0.1, 0.7, 1.0 / 3.0, 1e-7, 123456.789, 1e20, -2.5e-10 })
            {
                Assert.AreEqual(d, (double)Json.Parse(Json.Write(d)), "round trip of " + d.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        [Test]
        public void WritesEscapedStrings()
        {
            Assert.AreEqual("\"a\\\"b\\\\c\\n\\t\\u0001\"", Json.Write("a\"b\\c\n\t\u0001"));
            Assert.AreEqual("\"caf\u00e9\"", Json.Write("caf\u00e9"));
        }

        [Test]
        public void WriteThenParseGivesTheSameTree()
        {
            var tree = new JsonObject
            {
                ["name"] = "line1\nline2 \"quoted\" \\ \u65e5",
                ["n"] = 12.5,
                ["flags"] = new List<object> { true, false, null },
                ["nested"] = new JsonObject { ["deep"] = new List<object> { new JsonObject { ["x"] = -1.0 } } },
            };
            foreach (bool indent in new[] { true, false })
            {
                string text = Json.Write(tree, indent);
                Assert.AreEqual(text, Json.Write(Json.Parse(text), indent), "stable on a second pass, indent " + indent);
            }
        }

        [Test]
        public void WriterFailsLoudlyOnWhatJsonCannotHold()
        {
            Assert.Throws<ArgumentException>(() => Json.Write(double.NaN));
            Assert.Throws<ArgumentException>(() => Json.Write(double.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => Json.Write(new DateTime(2026, 1, 1)));
            Assert.Throws<ArgumentException>(() => Json.Write(new List<object> { new object() }));
        }

        [Test]
        public void NumbersUseTheInvariantCultureWhateverTheThreadCulture()
        {
            CultureInfo previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            System.Threading.Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            try
            {
                Assert.AreEqual("1.5", Json.Write(1.5));
                Assert.AreEqual(1.5, (double)Json.Parse("1.5"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void JsonObjectBasics()
        {
            var o = new JsonObject();
            o.Add("a", 1);
            o["b"] = 2;
            o["a"] = 3;   // overwriting keeps the first position
            CollectionAssert.AreEqual(new[] { "a", "b" }, o.Keys);
            Assert.AreEqual(3, o["a"]);
            Assert.IsTrue(o.Remove("a"));
            Assert.IsFalse(o.Remove("a"));
            Assert.IsFalse(o.ContainsKey("a"));
            Assert.Throws<ArgumentException>(() => o.Add("b", 9));
            Assert.Throws<KeyNotFoundException>(() => { object _ = o["missing"]; });
            Assert.Throws<ArgumentNullException>(() => o.Add(null, 1));
        }

        [Test]
        public void NumbersAreWrittenInTheFewestDigitsThatReadBackAsJavaScriptWritesThem()
        {
            Assert.AreEqual("0.1", Json.Write(0.1, false));
            Assert.AreEqual("0.30000000000000004", Json.Write(0.1 + 0.2, false));
            Assert.AreEqual("0.3333333333333333", Json.Write(1.0 / 3, false));
            Assert.AreEqual("123.456", Json.Write(123.456, false));
            Assert.AreEqual("1e-7", Json.Write(1e-7, false));
            Assert.AreEqual("1.5e+21", Json.Write(1.5e21, false));
            foreach (double d in new[] { 0.1, 1.0 / 3, Math.PI, 2.5e-9, 9007199254740993.0 / 3, -0.000123 })
            {
                Assert.AreEqual(d, (double)Json.Parse(Json.Write(d, false)), $"{d:R} reads back");
            }
        }
    }
}
