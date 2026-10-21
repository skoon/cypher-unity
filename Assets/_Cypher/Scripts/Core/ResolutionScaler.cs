using System;

namespace Cypher.Core
{
    /// <summary>
    /// Maths for the fixed-height low-res look. The scene renders at roughly
    /// <c>targetHeight</c> pixels tall whatever the display, via URP's render scale.
    /// It never scales above native: a 480p display with a 540p target stays native.
    /// </summary>
    public static class ResolutionScaler
    {
        public const float MinScale = 0.1f;

        public static float RenderScale(int targetHeight, int screenHeight)
        {
            if (targetHeight <= 0) throw new ArgumentOutOfRangeException(nameof(targetHeight));
            if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
            float scale = (float)targetHeight / screenHeight;
            return Math.Min(1f, Math.Max(MinScale, scale));
        }

        public static (int Width, int Height) InternalSize(int screenWidth, int screenHeight, int targetHeight)
        {
            if (screenWidth <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
            float s = RenderScale(targetHeight, screenHeight);
            return (Math.Max(1, (int)Math.Round(screenWidth * s)), Math.Max(1, (int)Math.Round(screenHeight * s)));
        }
    }
}
