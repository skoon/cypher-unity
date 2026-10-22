using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cypher.UI
{
    /// <summary>
    /// The two typefaces of the menus, taken from the operating system so the game ships no font files: a condensed face for headings
    /// and logos, a mono face for everything else. A face that is not installed is logged once and replaced by the engine's built-in
    /// font; the menus still work, just less handsome.
    /// </summary>
    public static class UiFonts
    {
        static readonly string[] DisplayNames = { "Bahnschrift Condensed", "Bahnschrift", "Segoe UI Semibold", "Segoe UI" };
        static readonly string[] BodyNames = { "Consolas", "Cascadia Mono", "Courier New" };

        static FontDefinition? _display;
        static FontDefinition? _body;

        public static FontDefinition Display => _display ?? (_display = Resolve(DisplayNames, "heading")).Value;

        public static FontDefinition Body => _body ?? (_body = Resolve(BodyNames, "body")).Value;

        /// <summary>The name of the installed face that was picked from <paramref name="candidates"/>, or null if none is installed.</summary>
        public static string FirstInstalled(string[] candidates, string[] installed)
        {
            foreach (string name in candidates)
            {
                if (Array.IndexOf(installed, name) >= 0) return name;
            }
            return null;
        }

        static FontDefinition Resolve(string[] candidates, string role)
        {
            string picked = FirstInstalled(candidates, Font.GetOSInstalledFontNames());
            if (picked != null) return FontDefinition.FromFont(Font.CreateDynamicFontFromOSFont(picked, 16));

            Debug.LogWarning($"[Cypher] None of the fonts {string.Join(", ", candidates)} is installed, so the {role} text uses the built-in font.");
            return FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        }

        /// <summary>Give every element under <paramref name="root"/> that carries a display class the heading face; everything else inherits the body face from the root.</summary>
        public static void Apply(VisualElement root)
        {
            root.style.unityFontDefinition = Body;
            root.Query(className: "cy-display").ForEach(e => e.style.unityFontDefinition = Display);
        }
    }
}
