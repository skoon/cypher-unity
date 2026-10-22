using System;
using Cypher.Core;
using NUnit.Framework;

namespace Cypher.Tests
{
    public class SettingsSerializerTests
    {
        static GameSettings Custom()
        {
            var s = new GameSettings();
            s.Graphics.Quality = QualityPreset.Ultra;
            s.Graphics.AdaptiveQuality = false;
            s.Graphics.InternalHeight = 720;
            s.Graphics.Window = WindowMode.Fullscreen;
            s.Graphics.VSync = false;
            s.Graphics.FieldOfView = 92.5;
            s.Graphics.Weather = false;
            s.Graphics.ShowPerfOverlay = false;
            s.Audio.Master = 0.35;
            s.Audio.Music = 0;
            s.Audio.Sfx = 0.9;
            s.Audio.Ambience = 0.123;
            s.Audio.Ui = 0.5;
            s.Audio.Voice = 1;
            s.Gameplay.Sensitivity = 1.75;
            s.Gameplay.InvertY = true;
            s.Controls.SetOverride("jump:0", "<Keyboard>/z");
            s.Controls.SetOverride("sprint:1", "<Gamepad>/leftStickPress");
            return s;
        }

        static JsonObject Parse(string json) => (JsonObject)Json.Parse(json);

        [Test]
        public void EverySettingSurvivesAWriteAndRead()
        {
            GameSettings original = Custom();
            string text = Json.Write(SettingsSerializer.ToTree(original, 1));
            GameSettings read = SettingsSerializer.FromTree(Parse(text));
            Assert.IsTrue(original.SameValues(read));
        }

        [Test]
        public void DefaultsSurviveAWriteAndRead()
        {
            string text = Json.Write(SettingsSerializer.ToTree(new GameSettings(), 1));
            Assert.IsTrue(new GameSettings().SameValues(SettingsSerializer.FromTree(Parse(text))));
        }

        [Test]
        public void TheFileHasAVersionAndEnumsAsNames()
        {
            JsonObject tree = SettingsSerializer.ToTree(Custom(), 7);
            Assert.AreEqual(7, tree["version"]);
            var graphics = (JsonObject)tree["graphics"];
            Assert.AreEqual("Ultra", graphics["quality"]);
            Assert.AreEqual("Fullscreen", graphics["window"]);
            Assert.AreEqual(720, graphics["internalHeight"]);
            string text = Json.Write(tree);
            StringAssert.Contains("\"internalHeight\": 720", text, "integers have no decimal point");
            StringAssert.Contains("\"jump:0\": \"<Keyboard>/z\"", text);
        }

        [Test]
        public void MissingKeysAndSectionsKeepTheirDefaults()
        {
            GameSettings s = SettingsSerializer.FromTree(Parse("{\"version\":1,\"graphics\":{\"fieldOfView\":80}}"));
            Assert.AreEqual(80.0, s.Graphics.FieldOfView);
            Assert.AreEqual(SettingsRanges.InternalHeightAuto, s.Graphics.InternalHeight);
            Assert.AreEqual(0.8, s.Audio.Master);
            Assert.IsTrue(SettingsSerializer.FromTree(Parse("{\"version\":1}")).SameValues(new GameSettings()));
        }

        [Test]
        public void UnknownKeysAreIgnored()
        {
            GameSettings s = SettingsSerializer.FromTree(Parse("{\"version\":1,\"future\":{\"x\":1},\"graphics\":{\"hdr\":true,\"vsync\":false}}"));
            Assert.IsFalse(s.Graphics.VSync);
        }

        [TestCase("{\"graphics\":5}", "graphics")]
        [TestCase("{\"audio\":[]}", "audio")]
        [TestCase("{\"graphics\":{\"quality\":\"Ultimate\"}}", "graphics.quality")]
        [TestCase("{\"graphics\":{\"quality\":2}}", "graphics.quality")]
        [TestCase("{\"graphics\":{\"quality\":\"2\"}}", "graphics.quality")]
        [TestCase("{\"graphics\":{\"quality\":\"low\"}}", "graphics.quality")]
        [TestCase("{\"graphics\":{\"quality\":\"Low, High\"}}", "graphics.quality")]
        [TestCase("{\"graphics\":{\"window\":null}}", "graphics.window")]
        [TestCase("{\"graphics\":{\"adaptiveQuality\":1}}", "graphics.adaptiveQuality")]
        [TestCase("{\"graphics\":{\"vsync\":\"yes\"}}", "graphics.vsync")]
        [TestCase("{\"graphics\":{\"internalHeight\":540.5}}", "graphics.internalHeight")]
        [TestCase("{\"graphics\":{\"internalHeight\":100}}", "graphics.internalHeight")]
        [TestCase("{\"graphics\":{\"internalHeight\":-540}}", "graphics.internalHeight")]
        [TestCase("{\"graphics\":{\"internalHeight\":239}}", "graphics.internalHeight")]
        [TestCase("{\"graphics\":{\"internalHeight\":\"540\"}}", "graphics.internalHeight")]
        [TestCase("{\"graphics\":{\"fieldOfView\":500}}", "graphics.fieldOfView")]
        [TestCase("{\"graphics\":{\"fieldOfView\":10}}", "graphics.fieldOfView")]
        [TestCase("{\"audio\":{\"master\":1.5}}", "audio.master")]
        [TestCase("{\"audio\":{\"voice\":-0.1}}", "audio.voice")]
        [TestCase("{\"audio\":{\"music\":true}}", "audio.music")]
        [TestCase("{\"gameplay\":{\"sensitivity\":0}}", "gameplay.sensitivity")]
        [TestCase("{\"gameplay\":{\"invertY\":\"false\"}}", "gameplay.invertY")]
        [TestCase("{\"controls\":{\"overrides\":[]}}", "controls.overrides")]
        [TestCase("{\"controls\":{\"overrides\":{\"jump:0\":5}}}", "controls.overrides.jump:0")]
        [TestCase("{\"controls\":{\"overrides\":{\"jump:0\":\"\"}}}", "controls.overrides.jump:0")]
        [TestCase("{\"controls\":{\"overrides\":{\"\":\"<Keyboard>/z\"}}}", "controls.overrides")]
        public void WrongValuesAreErrorsThatNameThePath(string json, string expectedPath)
        {
            SettingsFormatException e = Assert.Throws<SettingsFormatException>(() => SettingsSerializer.FromTree(Parse(json)));
            Assert.AreEqual(expectedPath, e.Path);
            StringAssert.StartsWith(expectedPath, e.Message);
        }

        [Test]
        public void RangeErrorsStateTheRangeAndTheValue()
        {
            SettingsFormatException e = Assert.Throws<SettingsFormatException>(() => SettingsSerializer.FromTree(Parse("{\"graphics\":{\"fieldOfView\":500}}")));
            Assert.AreEqual("graphics.fieldOfView: must be between 60 and 110, got 500", e.Message);
        }

        [Test]
        public void EnumErrorsListTheAllowedNames()
        {
            SettingsFormatException e = Assert.Throws<SettingsFormatException>(() => SettingsSerializer.FromTree(Parse("{\"graphics\":{\"quality\":\"Max\"}}")));
            StringAssert.Contains("Performance, Low, Medium, High, Ultra", e.Message);
        }

        [Test]
        public void ResolutionAutoIsZeroInTheFile()
        {
            var auto = new GameSettings();
            Assert.AreEqual(0, ((JsonObject)SettingsSerializer.ToTree(auto, 1)["graphics"])["internalHeight"]);
            Assert.AreEqual(SettingsRanges.InternalHeightAuto, SettingsSerializer.FromTree(Parse("{\"graphics\":{\"internalHeight\":0}}")).Graphics.InternalHeight);
            Assert.AreEqual(240, SettingsSerializer.FromTree(Parse("{\"graphics\":{\"internalHeight\":240}}")).Graphics.InternalHeight);
        }

        [Test]
        public void ValuesAtTheEdgesOfTheRangesAreAccepted()
        {
            GameSettings s = SettingsSerializer.FromTree(Parse(
                "{\"graphics\":{\"fieldOfView\":60,\"internalHeight\":2160},\"audio\":{\"master\":0,\"voice\":1},\"gameplay\":{\"sensitivity\":5}}"));
            Assert.AreEqual(60.0, s.Graphics.FieldOfView);
            Assert.AreEqual(2160, s.Graphics.InternalHeight);
            Assert.AreEqual(0.0, s.Audio.Master);
            Assert.AreEqual(1.0, s.Audio.Voice);
            Assert.AreEqual(5.0, s.Gameplay.Sensitivity);
        }

        [Test]
        public void NullArgumentsFailLoudly()
        {
            Assert.Throws<ArgumentNullException>(() => SettingsSerializer.FromTree(null));
        }
    }
}
