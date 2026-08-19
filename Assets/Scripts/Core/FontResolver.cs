using UnityEngine;

namespace TacticalSoccer.Core
{
    /// <summary>
    /// The OS-font lookup shared by every place in the project that wants a
    /// system font by name: try each candidate family, in order, against what
    /// the machine actually has installed.
    ///
    /// Used to be implemented three times independently (LocalizationManager's
    /// runtime font resolution, and two Editor-only lookups in
    /// TestEnvironmentGenerator for the monospaced stats table and the kanji
    /// player tags) — same loop, same WebGL guard, same trap
    /// (CreateDynamicFontFromOSFont happily returns a non-null Font for a
    /// family that is not installed, so the installed-names check has to run
    /// first). One copy now; callers keep their own logging and fallback
    /// choice, which differs by context.
    /// </summary>
    public static class FontResolver
    {
        /// <summary>
        /// The first candidate family that both exists on this machine and
        /// successfully resolves to a dynamic font, or null if none do.
        ///
        /// Always null on WebGL: the browser sandbox has no API to see OS
        /// fonts at all, so the lookup is compiled out rather than attempted
        /// and failing per-candidate.
        /// </summary>
        public static Font TryResolveOSFont(string[] familyNames, int size)
        {
#if UNITY_WEBGL
            return null;
#else
            if (familyNames == null || familyNames.Length == 0)
            {
                return null;
            }

            string[] installed = Font.GetOSInstalledFontNames();

            foreach (string family in familyNames)
            {
                if (string.IsNullOrEmpty(family) || System.Array.IndexOf(installed, family) < 0)
                {
                    continue;
                }

                Font font = Font.CreateDynamicFontFromOSFont(family, size);

                if (font != null)
                {
                    return font;
                }
            }

            return null;
#endif
        }
    }
}
