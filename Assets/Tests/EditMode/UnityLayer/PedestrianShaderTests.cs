using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Cypher.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    /// <summary>
    /// The pedestrian shaders and the Core code that feeds them have to agree on part ids, flag bits and the instance record's layout, and nothing but a person reading both
    /// would notice a drift: these tests read the HLSL and compare it with the C#.
    /// </summary>
    public class PedestrianShaderTests
    {
        static string Hlsl => File.ReadAllText(Path.Combine(Application.dataPath, "_Cypher/Shaders/PedestrianPose.hlsl"));

        static string SnakeUpper(string pascal) => Regex.Replace(pascal, "(?<!^)(?=[A-Z])", "_").ToUpperInvariant();

        static Dictionary<string, string> Defines(string prefix) =>
            Regex.Matches(Hlsl, @"#define\s+" + prefix + @"([A-Z_]+)\s+(\d+)u?")
                .Cast<Match>().ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        [Test]
        public void TheHlslPartIdsAreTheCoreRigPartIds()
        {
            Dictionary<string, string> defined = Defines("PED_PART_");
            Array parts = Enum.GetValues(typeof(RigPart));
            Assert.AreEqual(parts.Length, defined.Count, "one define for each part, no more");
            foreach (RigPart part in parts)
            {
                string name = SnakeUpper(part.ToString());
                Assert.IsTrue(defined.ContainsKey(name), $"the HLSL has no PED_PART_{name}");
                Assert.AreEqual((int)part, int.Parse(defined[name]), $"PED_PART_{name}");
            }
        }

        [Test]
        public void TheHlslFlagBitsAreTheGpuRecordsFlagBits()
        {
            Dictionary<string, string> defined = Defines("PED_FLAG_");
            var expected = new Dictionary<string, uint>
            {
                ["LONG_COAT"] = PedestrianGpuData.FlagLongCoat,
                ["UMBRELLA"] = PedestrianGpuData.FlagUmbrella,
                ["HOOD"] = PedestrianGpuData.FlagHood,
                ["PACK"] = PedestrianGpuData.FlagPack,
                ["TRIM"] = PedestrianGpuData.FlagTrim,
            };
            CollectionAssert.AreEquivalent(expected.Keys, defined.Keys);
            foreach (var kv in expected) Assert.AreEqual(kv.Value, uint.Parse(defined[kv.Key]), kv.Key);
        }

        [Test]
        public void TheHlslInstanceRecordIsTheGpuRecordFieldForField()
        {
            Match m = Regex.Match(Hlsl, @"struct\s+PedInstance\s*\{(?<body>[^}]*)\}");
            Assert.IsTrue(m.Success, "no PedInstance struct");
            var fields = Regex.Matches(m.Groups["body"].Value, @"(float4|uint4)\s+(\w+)\s*;").Cast<Match>().Select(f => (f.Groups[1].Value, f.Groups[2].Value)).ToList();
            CollectionAssert.AreEqual(new[] { ("float4", "a"), ("float4", "b"), ("float4", "c"), ("uint4", "colours"), ("uint4", "extra") }, fields);
            Assert.AreEqual(fields.Count * 16, PedestrianGpuData.Size, "five four-component fields, 16 bytes each");
        }

        [Test]
        public void TheHlslRigConstantsAreTheCorePoseConstants()
        {
            string text = Hlsl;
            double Const(string name)
            {
                Match m = Regex.Match(text, @"static const float " + name + @"\s*=\s*([0-9.]+)\s*;");
                Assert.IsTrue(m.Success, $"no constant {name}");
                return double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            Assert.AreEqual(PedestrianPose.HipY, Const("PED_HIP_Y"), 1e-12);
            Assert.AreEqual(PedestrianPose.ThighLength, Const("PED_THIGH_L"), 1e-12);
            Assert.AreEqual(PedestrianPose.LegLength, Const("PED_LEG_L"), 1e-12);
            Assert.AreEqual(PedestrianPose.ShoulderY, Const("PED_SHOULDER_Y"), 1e-12);
            Assert.AreEqual(PedestrianPose.ShoulderX, Const("PED_SHOULDER_X"), 1e-12);
            Assert.AreEqual(PedestrianPose.HipX, Const("PED_HIP_X"), 1e-12);
            Assert.AreEqual(PedestrianPose.HeadY, Const("PED_HEAD_Y"), 1e-12);
        }

        [TestCase("Cypher/Pedestrian")]
        [TestCase("Cypher/PedestrianBlob")]
        public void TheShaderIsFoundAndHasAForwardPass(string name)
        {
            Shader shader = Shader.Find(name);
            Assert.IsNotNull(shader, $"{name} not found");
            Assert.AreEqual(1, shader.passCount);
        }

        [Test]
        public void TheShadersShareTheNightFillWithTheCityShader()
        {
            string dir = Path.Combine(Application.dataPath, "_Cypher/Shaders");
            string cityPass = File.ReadAllText(Path.Combine(dir, "CityLitForwardPass.hlsl"));
            string fill = File.ReadAllText(Path.Combine(dir, "CityNightFill.hlsl"));
            Assert.IsTrue(File.ReadAllText(Path.Combine(dir, "Pedestrian.shader")).Contains("CityNightFill.hlsl"), "the pedestrians use the shared fill");
            Assert.IsTrue(cityPass.Contains("CityNightFill.hlsl"), "and so does the city shader");
            Assert.IsTrue(Regex.IsMatch(fill, @"half3\s+CityFillLight\s*\("), "the function lives in the shared include");
            Assert.IsFalse(Regex.IsMatch(cityPass, @"half3\s+CityFillLight\s*\("), "and nowhere else");
            Assert.IsFalse(cityPass.Contains("float4 _CypherFillDir0;"), "nor do the globals");
        }
    }
}
