#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cypher.Tests
{
    /// <summary>
    /// Every shader of the project's actors compiles all the way for this device. The editor compiles a shader's variants only when they are first drawn, so a mistake in a part of
    /// it that is used by the player and not by the importer (an undeclared function in a vertex stage) passes the import check; setting the pass makes the compiler do the lot.
    /// </summary>
    public class ShaderRenderTests
    {
        [TestCase("Cypher/Pedestrian")]
        [TestCase("Cypher/PedestrianBlob")]
        [TestCase("Cypher/Vehicle")]
        [TestCase("Cypher/VehicleGlow")]
        [TestCase("Cypher/VehicleShadow")]
        [TestCase("Cypher/Signal")]
        public void TheShaderCompilesForRendering(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("no graphics device");
            Shader shader = Shader.Find(name);
            Assert.IsNotNull(shader, name);
            bool before = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var material = new Material(shader);
            try
            {
                Assert.IsTrue(material.SetPass(0), $"{name} did not compile for this device");
            }
            finally
            {
                Object.DestroyImmediate(material);
                ShaderUtil.allowAsyncCompilation = before;
            }
        }
    }
}
#endif
