using UnityEngine;

namespace TacticalSoccer.Core
{
    // Busca una fuente del sistema operativo entre varias familias candidatas.
    public static class FontResolver
    {
        // Devuelve la primera fuente candidata instalada en el sistema, o null si ninguna está disponible.
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
