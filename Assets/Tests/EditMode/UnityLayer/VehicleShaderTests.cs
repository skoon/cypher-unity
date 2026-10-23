using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    /// <summary>
    /// The vehicle shaders and the Core code that feeds them have to agree on the type table, the glow parts and the instance record's layout, and nothing but a person reading both
    /// would notice a drift: these tests read the HLSL and compare it with the C#.
    /// </summary>
    public class VehicleShaderTests
    {
        static string Dir => Path.Combine(Application.dataPath, "_Cypher/Shaders");

        static string Hlsl => File.ReadAllText(Path.Combine(Dir, "VehiclePose.hlsl"));

        static double[][] Table(string name)
        {
            Match m = Regex.Match(Hlsl, @"static const float\d\s+" + name + @"\[\d\]\s*=\s*\{(?<body>[^}]*)\}\s*;");
            Assert.IsTrue(m.Success, $"no table {name}");
            return Regex.Matches(m.Groups["body"].Value, @"float\d\((?<v>[^)]*)\)").Cast<Match>()
                .Select(r => r.Groups["v"].Value.Split(',').Select(x => double.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToArray()).ToArray();
        }

        [Test]
        public void TheHlslTypeTableIsTheCoreCatalog()
        {
            double[][] size = Table("VEH_SIZE"), wheel = Table("VEH_WHEEL"), wheelZ = Table("VEH_WHEEL_Z");
            Assert.AreEqual(VehicleCatalog.Ground.Count, size.Length);
            for (int i = 0; i < VehicleCatalog.Ground.Count; i++)
            {
                GroundVehicleType t = VehicleCatalog.Ground[i];
                CollectionAssert.AreEqual(new[] { t.Width, t.Height, t.Length }, size[i], $"{t.Key}: width, height, length");
                CollectionAssert.AreEqual(new[] { t.WheelRadius, t.WheelWidth, t.WheelX, (double)t.WheelCount }, wheel[i], $"{t.Key}: wheel radius, width, offset, count");
                CollectionAssert.AreEqual(new[] { t.WheelZFront, t.WheelZRear }, wheelZ[i], $"{t.Key}: wheel places along the vehicle");
            }
        }

        [Test]
        public void TheHlslAircraftTableIsTheCoreCatalog()
        {
            double[][] size = Table("AIR_SIZE");
            Assert.AreEqual(VehicleCatalog.Aircraft.Count, size.Length);
            for (int i = 0; i < size.Length; i++)
            {
                AircraftType t = VehicleCatalog.Aircraft[i];
                CollectionAssert.AreEqual(new[] { t.Width, t.Height, t.Length }, size[i], t.Key);
            }
        }

        [Test]
        public void TheHlslGlowPartsAreTheCoreGlowParts()
        {
            Dictionary<string, int> defined = Regex.Matches(Hlsl, @"#define\s+VEH_GLOW_([A-Z_]+)\s+(\d+)").Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
            Assert.AreEqual(Enum.GetValues(typeof(GlowPart)).Length, defined.Count);
            Assert.AreEqual((int)GlowPart.Lamps, defined["LAMPS"]);
            Assert.AreEqual((int)GlowPart.Underglow, defined["UNDERGLOW"]);
            Assert.AreEqual((int)GlowPart.Beams, defined["BEAMS"]);
        }

        [Test]
        public void TheHlslInstanceRecordIsTheGpuRecordFieldForField()
        {
            Match m = Regex.Match(Hlsl, @"struct\s+VehicleInstance\s*\{(?<body>[^}]*)\}");
            Assert.IsTrue(m.Success, "no VehicleInstance struct");
            var fields = Regex.Matches(m.Groups["body"].Value, @"(float4|uint4)\s+(\w+)\s*;").Cast<Match>().Select(f => (f.Groups[1].Value, f.Groups[2].Value)).ToList();
            CollectionAssert.AreEqual(new[] { ("float4", "a"), ("float4", "b"), ("uint4", "colours") }, fields);
            Assert.AreEqual(fields.Count * 16, VehicleGpuData.Size, "three four-component fields, 16 bytes each");
        }

        [Test]
        public void TheHlslPulseIsTheCorePulse()
        {
            string text = Hlsl;
            double Const(string name) => double.Parse(Regex.Match(text, @"static const float " + name + @"\s*=\s*([0-9.]+)\s*;").Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.AreEqual(VehiclePlacement.PulseRate, Const("VEH_PULSE_RATE"), 1e-12);
            Assert.AreEqual(VehiclePlacement.PulsePhase, Const("VEH_PULSE_PHASE"), 1e-12);
        }

        [Test]
        public void TheHlslKindsAreTheOnesTheRendererSends()
        {
            Dictionary<string, int> defined = Regex.Matches(Hlsl, @"#define\s+VEH_KIND_([A-Z_]+)\s+(\d+)").Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));
            Assert.AreEqual(0, defined["BODY"]);
            Assert.AreEqual(1, defined["WHEEL"]);
            Assert.AreEqual(2, defined["AIRCRAFT"]);
            Assert.AreEqual(3, defined["WORLD"]);
            string renderer = File.ReadAllText(Path.Combine(Application.dataPath, "_Cypher/Scripts/Actors/TrafficRenderer.cs"));
            Assert.IsTrue(renderer.Contains("KindBody = 0, KindWheel = 1, KindAircraft = 2, KindWorld = 3"), "the renderer sends the same numbers");
        }

        [Test]
        public void TheSignalShaderShowsTheLampColoursOfCoreSignalLamps()
        {
            string text = File.ReadAllText(Path.Combine(Dir, "Signal.shader"));
            string F(double v) => v.ToString("0.0###", CultureInfo.InvariantCulture);
            // the lit colours of the three lamps
            foreach (int lamp in new[] { SignalLamps.Red, SignalLamps.Amber, SignalLamps.Green })
            {
                // axis 0 has green (lamp 2), amber (lamp 1) or red for axis 1 (lamp 0)
                (double r, double g, double b) = lamp == SignalLamps.Red ? SignalLamps.Colour(1, lamp, 0, false) : lamp == SignalLamps.Amber ? SignalLamps.Colour(0, lamp, 0, true) : SignalLamps.Colour(0, lamp, 0, false);
                Assert.IsTrue(text.Contains($"half3({F(r)}, {F(g)}, {F(b)})"), $"lamp {lamp} lit: {r}, {g}, {b}");
            }
            Assert.IsTrue(text.Contains($"LAMP_OFF = {F(SignalLamps.Off)}"), "the off glow");
        }

        [TestCase("Cypher/Vehicle")]
        [TestCase("Cypher/VehicleGlow")]
        [TestCase("Cypher/VehicleShadow")]
        [TestCase("Cypher/Signal")]
        public void TheShaderIsFoundAndHasAForwardPass(string name)
        {
            Shader shader = Shader.Find(name);
            Assert.IsNotNull(shader, $"{name} not found");
            Assert.AreEqual(1, shader.passCount);
        }

        [Test]
        public void TheVehiclesShareTheNightFillWithTheCityShader()
        {
            Assert.IsTrue(File.ReadAllText(Path.Combine(Dir, "Vehicle.shader")).Contains("CityNightFill.hlsl"), "the vehicles use the shared fill");
        }

        [Test]
        public void TheProbeKnowsEveryKindOfPlacement()
        {
            string probe = File.ReadAllText(Path.Combine(Dir, "VehiclePlacementProbe.compute"));
            foreach (string mode in new[] { "BODY", "WHEEL", "AIRCRAFT", "GLOW", "AIRCRAFT_GLOW", "SHADOW" }) Assert.IsTrue(probe.Contains("#define PROBE_" + mode), mode);
        }
    }
}
