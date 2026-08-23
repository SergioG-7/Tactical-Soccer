using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TacticalSoccer.Core
{
    // Una línea de un fichero de idioma: la clave y el texto asociado.
    [Serializable]
    public class LocalizationEntry
    {
        public string key;
        public string value;
    }

    // Representa un idioma completo tal como se guarda en el JSON.
    [Serializable]
    public class LocalizationFile
    {
        public string code;

        [Tooltip("How this language names ITSELF — 'English', not 'Inglés'. It " +
                 "is the caption of its own button, and a player looking for " +
                 "their language is looking for the word they would write.")]
        public string displayName;

        [Tooltip("OS fonts able to draw this language, best first. Empty means " +
                 "the built-in UI font is enough — which is true of every " +
                 "language written in the Latin alphabet and false of Japanese.")]
        public string[] fontFamilies;

        public LocalizationEntry[] entries;
    }

    // Gestiona el idioma activo y da acceso a todos los textos traducidos del juego.
    public static class LocalizationManager
    {
        // Idioma por defecto, usado como respaldo si falta algo.
        public const string DefaultLanguage = "es";

        // Idiomas disponibles en el juego, en el orden que se muestran en opciones.
        public static readonly string[] AvailableLanguages = { "es", "en", "jp" };

        private const string ResourceFolder = "Localization";

        // Se dispara cuando cambia el idioma activo, para que la UI se refresque.
        public static event Action OnLanguageChanged;

        private static readonly Dictionary<string, string> phrases =
            new Dictionary<string, string>();

        // Caché de ficheros y fuentes ya resueltos, por código de idioma.
        private static readonly Dictionary<string, LocalizationFile> files =
            new Dictionary<string, LocalizationFile>();

        private static readonly Dictionary<string, Font> fonts =
            new Dictionary<string, Font>();

        // Claves que ya se avisaron como faltantes, para no repetir el aviso.
        private static readonly HashSet<string> reportedMissing = new HashSet<string>();

        private static string current;

        // Código del idioma activo; carga el guardado la primera vez que se consulta.
        public static string Current
        {
            get
            {
                EnsureLoaded();
                return current;
            }
        }

        // Cambia el idioma activo, lo guarda y avisa a la UI.
        public static void SetLanguage(string code)
        {
            EnsureLoaded();

            if (string.IsNullOrEmpty(code) || code == current)
            {
                return;
            }

            Load(code);

            SaveManager.Data.language = current;
            SaveManager.SaveNow();

            OnLanguageChanged?.Invoke();
        }

        // Devuelve el texto traducido para una clave, o la propia clave si no existe.
        public static string GetText(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            EnsureLoaded();

            if (phrases.TryGetValue(key, out string value))
            {
                return value;
            }

            if (reportedMissing.Add(key))
            {
                Debug.LogWarning($"[Idioma] Falta la clave '{key}' en '{current}'. " +
                                 "Se muestra la clave.");
            }

            return key;
        }

        // Devuelve el texto traducido de una clave con los parámetros ya sustituidos.
        public static string Format(string key, params object[] args)
        {
            string pattern = GetText(key);

            try
            {
                return string.Format(pattern, args);
            }
            catch (FormatException)
            {
                Debug.LogWarning($"[Idioma] La clave '{key}' tiene marcadores mal " +
                                 $"escritos en '{current}': \"{pattern}\".");

                return pattern;
            }
        }

        // Escribe el texto traducido de una clave en un Text, aplicando también la fuente correcta.
        public static void Write(Text target, string key)
        {
            if (target == null)
            {
                return;
            }

            target.text = GetText(key);

            ApplyFont(target);
        }

        // Igual que Write, pero para una clave con parámetros formateados.
        public static void WriteFormatted(Text target, string key, params object[] args)
        {
            if (target == null)
            {
                return;
            }

            target.text = Format(key, args);

            ApplyFont(target);
        }

        // Fuente que necesita el idioma activo, o null si vale la fuente por defecto.
        public static Font ActiveFont
        {
            get
            {
                EnsureLoaded();
                return ResolveFont(current);
            }
        }

        // Aplica a un Text la fuente del idioma activo, si ese idioma necesita una propia.
        public static void ApplyFont(Text target)
        {
            EnsureLoaded();

            ApplyFontFor(target, current);
        }

        // Aplica la fuente de un idioma concreto a un Text, solo si su contenido la necesita.
        public static void ApplyFontFor(Text target, string code)
        {
            if (target == null)
            {
                return;
            }

            Font special = ResolveFont(code);

            Font font = (special != null && HasNonAsciiCharacter(target.text))
                ? special
                : BuiltInFont;

            if (font != null)
            {
                target.font = font;
            }
        }

        // Comprueba si el texto tiene algún carácter fuera del ASCII imprimible.
        private static bool HasNonAsciiCharacter(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (char c in text)
            {
                if (c > 127)
                {
                    return true;
                }
            }

            return false;
        }

        // Nombre con el que el idioma se llama a sí mismo, o su código en mayúsculas si no hay nombre.
        public static string DisplayName(string code)
        {
            LocalizationFile file = ResolveFile(code);

            return file != null && !string.IsNullOrEmpty(file.displayName)
                ? file.displayName
                : (code ?? string.Empty).ToUpperInvariant();
        }

        // Carga el idioma guardado la primera vez que se necesita.
        private static void EnsureLoaded()
        {
            if (current != null)
            {
                return;
            }

            Load(SaveManager.Data.language);
        }

        // Carga el diccionario de un idioma y lo deja activo, usando el idioma por defecto si falla.
        private static void Load(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                code = DefaultLanguage;
            }

            LocalizationFile file = ResolveFile(code);

            if (file == null && code != DefaultLanguage)
            {
                Debug.LogWarning($"[Idioma] No hay fichero para '{code}'. " +
                                 $"Se usa '{DefaultLanguage}'.");

                file = ResolveFile(DefaultLanguage);
            }

            current = code;

            phrases.Clear();
            reportedMissing.Clear();

            if (file == null || file.entries == null)
            {
                Debug.LogError("[Idioma] No se ha podido cargar ningún diccionario: " +
                               "los menús mostrarán las claves.");
                return;
            }

            foreach (LocalizationEntry entry in file.entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key))
                {
                    continue;
                }

                if (phrases.ContainsKey(entry.key))
                {
                    Debug.LogWarning($"[Idioma] Clave duplicada '{entry.key}' en '{code}'.");
                }

                phrases[entry.key] = entry.value ?? string.Empty;
            }
        }

        // Carga y cachea el fichero JSON de un idioma.
        private static LocalizationFile ResolveFile(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            if (files.TryGetValue(code, out LocalizationFile cached))
            {
                return cached;
            }

            LocalizationFile parsed = null;
            TextAsset asset = Resources.Load<TextAsset>($"{ResourceFolder}/{code}");

            if (asset != null)
            {
                try
                {
                    parsed = JsonUtility.FromJson<LocalizationFile>(asset.text);
                }
                catch (Exception error)
                {
                    Debug.LogError($"[Idioma] '{code}.json' no es JSON válido: {error.Message}");
                }
            }

            files[code] = parsed;

            return parsed;
        }

        // Busca la mejor fuente disponible para un idioma, probando fuentes del sistema y luego las incluidas en el proyecto.
        private static Font ResolveFont(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            if (fonts.TryGetValue(code, out Font cached) && cached != null)
            {
                return cached;
            }

            LocalizationFile file = ResolveFile(code);

            if (file == null || file.fontFamilies == null || file.fontFamilies.Length == 0)
            {
                fonts[code] = null;
                return null;
            }

            Font osFont = FontResolver.TryResolveOSFont(file.fontFamilies, DynamicFontSize);

            if (osFont != null)
            {
                fonts[code] = osFont;
                return osFont;
            }

            Font embedded = ResolveEmbeddedFont(file);

            if (embedded != null)
            {
                fonts[code] = embedded;
                return embedded;
            }

            Debug.LogWarning($"[Idioma] Ninguna fuente disponible para '{code}' " +
                             $"({string.Join(", ", file.fontFamilies)}): el texto de ese idioma " +
                             "puede salir en blanco.");

            fonts[code] = null;
            return null;
        }

        // Fuentes incluidas en Resources como último recurso, en orden de preferencia.
        private static readonly string[] EmbeddedFontFallbacks = { "MainFont", "MainFontJPBackup" };

        // Busca entre las fuentes incluidas en el proyecto una que sirva para este idioma.
        private static Font ResolveEmbeddedFont(LocalizationFile file)
        {
            if (!DictionaryHasNonAsciiEntry(file))
            {
                return null;
            }

            foreach (string resourceName in EmbeddedFontFallbacks)
            {
                Font font = Resources.Load<Font>(resourceName);

                if (font != null)
                {
                    return font;
                }
            }

            return null;
        }

        // Comprueba si el diccionario de un idioma tiene algún carácter fuera de ASCII, es decir, si necesita una fuente propia.
        private static bool DictionaryHasNonAsciiEntry(LocalizationFile file)
        {
            if (file.entries == null)
            {
                return false;
            }

            foreach (LocalizationEntry entry in file.entries)
            {
                if (string.IsNullOrEmpty(entry?.value))
                {
                    continue;
                }

                if (HasNonAsciiCharacter(entry.value))
                {
                    return true;
                }
            }

            return false;
        }

        // Tamaño inicial del atlas para las fuentes dinámicas del sistema.
        private const int DynamicFontSize = 64;

        private static Font builtInFont;

        // Fuente por defecto de Unity, usada cuando un idioma no necesita una propia.
        public static Font BuiltInFont
        {
            get
            {
                if (builtInFont != null)
                {
                    return builtInFont;
                }

                try
                {
                    builtInFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                catch (ArgumentException)
                {
                    builtInFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }

                return builtInFont;
            }
        }
    }
}
