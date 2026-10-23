using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Cypher.Tests
{
    /// <summary>Every shader the project ships must import without compile errors and be usable by a material.</summary>
    public class ShaderCompileTests
    {
        [TestCase("Cypher/CityLit")]
        [TestCase("Cypher/SkyGradient")]
        [TestCase("Cypher/NeonAdd")]
        [TestCase("Cypher/NeonBoard")]
        [TestCase("Cypher/Pedestrian")]
        [TestCase("Cypher/PedestrianBlob")]
        [TestCase("Cypher/Vehicle")]
        [TestCase("Cypher/VehicleGlow")]
        [TestCase("Cypher/VehicleShadow")]
        [TestCase("Cypher/Signal")]
        [TestCase("Cypher/Rain")]
        public void ShaderImportsWithoutErrors(string name)
        {
            Shader shader = Shader.Find(name);
            Assert.IsNotNull(shader, $"{name} was not found");
            var errors = ShaderUtil.GetShaderMessages(shader)
                .Where(m => m.severity == ShaderCompilerMessageSeverity.Error).ToList();
            Assert.IsEmpty(errors, string.Join("\n", errors.Select(e => $"{e.file}({e.line}): {e.message}")));
        }

        [TestCase("Cypher/CityLit")]
        [TestCase("Cypher/SkyGradient")]
        [TestCase("Cypher/NeonAdd")]
        [TestCase("Cypher/NeonBoard")]
        [TestCase("Cypher/Pedestrian")]
        [TestCase("Cypher/PedestrianBlob")]
        [TestCase("Cypher/Vehicle")]
        [TestCase("Cypher/VehicleGlow")]
        [TestCase("Cypher/VehicleShadow")]
        [TestCase("Cypher/Signal")]
        [TestCase("Cypher/Rain")]
        public void ShaderHasNoFallbackErrorAndAMaterialCanUseIt(string name)
        {
            var material = new Material(Shader.Find(name));
            try
            {
                Assert.AreEqual(name, material.shader.name);
                Assert.IsTrue(material.shader.isSupported || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null,
                    $"{name} is not supported on this graphics device");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }
    }
}
