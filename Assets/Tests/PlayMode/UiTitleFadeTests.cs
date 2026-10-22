#if UNITY_EDITOR
using System.Collections;
using Cypher.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Cypher.Tests
{
    /// <summary>The dark fade that keeps the title menu readable over the bright city.</summary>
    public class UiTitleFadeTests : UiTestBase
    {
        [UnityTest]
        public IEnumerator TheTitleMenuSitsOnADarkFadeThatIsSolidAcrossTheColumn()
        {
            yield return ToTitle();
            VisualElement scrim = Ui.Root.Q(className: "cy-title__scrim");
            Assert.IsNotNull(scrim, "the title has a fade behind the menu");
            Assert.IsNotNull(scrim.resolvedStyle.backgroundImage.texture, "and it has a texture");
            Assert.GreaterOrEqual(scrim.resolvedStyle.unityBackgroundImageTintColor.a, 0.8f, "which is dark enough to read text over");
            Assert.GreaterOrEqual(scrim.worldBound.width, 1000f, "and wide enough to cover the menu column and its highlight bar");
        }

        [Test]
        public void TheFadeIsSolidThenSoftAndEndsClear()
        {
            Assert.AreEqual(1f, UiTextures.FadeAlpha(0f), 1e-4f, "solid at the left edge");
            Assert.AreEqual(1f, UiTextures.FadeAlpha(0.4f), 1e-4f, "still solid across the menu column");
            Assert.Less(UiTextures.FadeAlpha(0.7f), 0.9f, "starts to let the city through");
            Assert.Greater(UiTextures.FadeAlpha(0.7f), 0.1f);
            Assert.AreEqual(0f, UiTextures.FadeAlpha(1f), 1e-4f, "clear at the right edge");
            for (float t = 0.05f; t <= 1f; t += 0.05f) Assert.LessOrEqual(UiTextures.FadeAlpha(t), UiTextures.FadeAlpha(t - 0.05f) + 1e-6f, "never gets denser going right");
        }
    }
}
#endif
