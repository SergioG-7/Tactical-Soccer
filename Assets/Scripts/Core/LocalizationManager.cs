using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TacticalSoccer.Core
{
    /// <summary>One line of a language file: the key code asks for, and the words a human reads.</summary>
    [Serializable]
    public class LocalizationEntry
    {
        public string key;
        public string value;
    }

    /// <summary>
    /// A whole language, as it sits on disk.
    ///
    /// The dictionary is an ARRAY of key/value pairs rather than the flat
    /// <c>{"key": "value"}</c> object a translator might expect, and that is
    /// JsonUtility's doing: Unity's built-in reader cannot deserialise a
    /// Dictionary, so a flat object would mean hand-writing a JSON parser and
    /// getting the escapes, the unicode and the whitespace right by ourselves.
    /// An array of pairs costs the translator six extra characters per line and
    /// costs us nothing.
    /// </summary>
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

    /// <summary>
    /// Every word the menus say, in whichever language the player picked.
    ///
    /// A static class rather than a MonoBehaviour singleton, and deliberately:
    /// text is asked for from Awake, from Start and from OnEnable all over the
    /// UI, and a component would have to exist and have run its own Awake before
    /// any of them — which is exactly the ordering problem this project has been
    /// bitten by before (a controller parked on a deactivated panel never runs at
    /// all). Nothing here needs a scene, a GameObject or an Inspector, so it does
    /// not have one. The dictionary loads itself on first use.
    ///
    /// Files live in Resources rather than StreamingAssets. StreamingAssets on
    /// Android is inside the .apk and can only be read through UnityWebRequest —
    /// asynchronously — which would mean every screen either awaiting a load or
    /// drawing itself once in the wrong language and again a frame later.
    /// Resources.Load is synchronous everywhere, and these files are kilobytes.
    ///
    /// Hot swap is an event, not a re-scene-load: <see cref="OnLanguageChanged"/>
    /// fires once per change and every <see cref="UI.LocalizedText"/> on screen
    /// rewrites itself. Panels that are hidden at that moment do not hear it and
    /// do not need to — they re-read their key when they are next shown.
    /// </summary>
    public static class LocalizationManager
    {
        /// <summary>The language the game falls back to, and the one every key is written in.</summary>
        public const string DefaultLanguage = "es";

        /// <summary>
        /// The languages offered, in the order the options screen lists them.
        /// A file that is not named here is never loaded; a name here with no
        /// file behind it falls back to <see cref="DefaultLanguage"/> and says so.
        /// </summary>
        public static readonly string[] AvailableLanguages = { "es", "en", "jp" };

        private const string ResourceFolder = "Localization";

        /// <summary>
        /// Raised after the active language has changed and the new dictionary is
        /// in place — never before, so a listener that rebuilds itself here reads
        /// the new words rather than the old ones.
        /// </summary>
        public static event Action OnLanguageChanged;

        private static readonly Dictionary<string, string> phrases =
            new Dictionary<string, string>();

        // Parsed files and resolved fonts, kept per language code. Both are
        // caches of something on disk: neither is state worth persisting.
        private static readonly Dictionary<string, LocalizationFile> files =
            new Dictionary<string, LocalizationFile>();

        private static readonly Dictionary<string, Font> fonts =
            new Dictionary<string, Font>();

        // Keys already complained about. A missing key is asked for on every
        // refresh of the screen it is on, and a warning per frame would bury the
        // console under one mistake.
        private static readonly HashSet<string> reportedMissing = new HashSet<string>();

        private static string current;

        /// <summary>The active language code. Loads the saved one on first read.</summary>
        public static string Current
        {
            get
            {
                EnsureLoaded();
                return current;
            }
        }

        /// <summary>
        /// Switches language, remembers the choice and tells the screens.
        ///
        /// Saved immediately rather than marked dirty: this is a decision the
        /// player made once and expects to still be true after a crash, and it
        /// happens rarely enough that the write costs nothing.
        /// </summary>
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

        /// <summary>
        /// The words behind a key, or the key itself when there are none.
        ///
        /// Returning the key is the only sane failure: it is readable, it is
        /// obviously wrong to anybody looking at the screen, and it names the
        /// line that needs writing. An empty string would leave a blank button
        /// that nobody could diagnose.
        /// </summary>
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

        /// <summary>
        /// A phrase with numbers in it. The placeholders live in the translation
        /// (<c>{0}</c>, <c>{1}</c>) rather than in the code, because where a
        /// number falls inside a sentence is part of the language, not part of
        /// the reading that produced it.
        /// </summary>
        public static string Format(string key, params object[] args)
        {
            string pattern = GetText(key);

            try
            {
                return string.Format(pattern, args);
            }
            catch (FormatException)
            {
                // A translation with a stray brace should not take the screen
                // down with it.
                Debug.LogWarning($"[Idioma] La clave '{key}' tiene marcadores mal " +
                                 $"escritos en '{current}': \"{pattern}\".");

                return pattern;
            }
        }

        /// <summary>
        /// Writes a translated key onto a Text, font included.
        ///
        /// The font matters as much as the words: the built-in UI font is
        /// Liberation Sans, which has no CJK glyphs at all and does not fall
        /// back to anything — asked for Japanese it draws NOTHING, with no error
        /// and no warning, and the screen simply comes out blank. So a language
        /// that needs a different face names it in its own file and it is applied
        /// wherever its words are.
        /// </summary>
        public static void Write(Text target, string key)
        {
            if (target == null)
            {
                return;
            }

            target.text = GetText(key);

            ApplyFont(target);
        }

        /// <summary>As <see cref="Write"/>, for a phrase carrying numbers.</summary>
        public static void WriteFormatted(Text target, string key, params object[] args)
        {
            if (target == null)
            {
                return;
            }

            target.text = Format(key, args);

            ApplyFont(target);
        }

        /// <summary>
        /// The face the active language needs, or null when the built-in one is
        /// right for it.
        ///
        /// Exposed because one screen cannot simply be handed it: the full-time
        /// table is laid out in padded columns and therefore wants a MONOSPACED
        /// font — but no monospaced font on a Windows machine carries kana, so
        /// in Japanese that table would be drawn entirely blank. The screen has
        /// to be able to ask which of the two problems it has.
        /// </summary>
        public static Font ActiveFont
        {
            get
            {
                EnsureLoaded();
                return ResolveFont(current);
            }
        }

        /// <summary>Gives a Text the active language's font, if that language asked for one.</summary>
        public static void ApplyFont(Text target)
        {
            EnsureLoaded();

            ApplyFontFor(target, current);
        }

        /// <summary>
        /// Gives a Text a SPECIFIC language's font — but only if the text it is
        /// currently showing actually needs it.
        ///
        /// The language's font is a candidate, not an order: "title.heading" is
        /// the same "TACTICAL SOCCER" in all three dictionaries, so switching to
        /// Japanese has no business touching it, and it used to anyway — the
        /// active language decided the font for every localised Text, whether or
        /// not its words needed a different one. That is exactly what made the
        /// title disappear in a WebGL build once Japanese resolved to a real
        /// bundled font instead of silently failing: a CJK face's line metrics
        /// run taller than the Latin one the heading's fixed-height box was
        /// tuned for, and with `verticalOverflow = Truncate` a line that no
        /// longer fits is not clipped, it is dropped. Only text that actually
        /// contains a non-Latin character pays for the swap now, checked
        /// against the words on screen rather than guessed from the language
        /// code.
        ///
        /// That check is NOT <see cref="Font.HasCharacter"/>: measured directly
        /// against LegacyRuntime.ttf, it returned <c>true</c> for kanji, for
        /// hiragana and even for U+FFFE (a code point Unicode guarantees is
        /// never a character) — for a dynamic font it does not consult the real
        /// glyph table, so it cannot tell "drawable" from "not". A dumber test
        /// (any character past ASCII) is the one that is actually true.
        ///
        /// Needed for the language buttons themselves regardless: the one
        /// reading 日本語 has to be drawable while the game is still in Spanish,
        /// and it earns the swap on its own merits — those three characters are
        /// exactly what the built-in face cannot draw.
        /// </summary>
        public static void ApplyFontFor(Text target, string code)
        {
            if (target == null)
            {
                return;
            }

            Font special = ResolveFont(code);

            // Falls back to the built-in face rather than leaving whatever was
            // there. Without this, going to Japanese and back would leave every
            // label that had been rewritten in the meantime wearing the CJK face
            // while the rest of the screen was back on the built-in one — the
            // same trap as any cached visual property that only gets set one way.
            Font font = (special != null && HasNonAsciiCharacter(target.text))
                ? special
                : BuiltInFont;

            if (font != null)
            {
                target.font = font;
            }
        }

        /// <summary>
        /// Whether <paramref name="text"/> has at least one character outside
        /// printable ASCII. Every dictionary in this project keeps Spanish and
        /// English free of accents in UI strings (see the i18n audit notes), so
        /// in practice this only ever fires for Japanese — and it is only ever
        /// asked in the first place when <see cref="ApplyFontFor"/> already has
        /// a non-null candidate font, which today means exactly that language.
        /// </summary>
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

        /// <summary>
        /// What a language calls itself, for its button. Falls back to the code
        /// in capitals, which is still a usable label.
        /// </summary>
        public static string DisplayName(string code)
        {
            LocalizationFile file = ResolveFile(code);

            return file != null && !string.IsNullOrEmpty(file.displayName)
                ? file.displayName
                : (code ?? string.Empty).ToUpperInvariant();
        }

        private static void EnsureLoaded()
        {
            if (current != null)
            {
                return;
            }

            // Straight from the save file, which is where the player's choice
            // lives. Note the order: this is the first read of the save data in
            // most sessions, and it happens before any UI exists.
            Load(SaveManager.Data.language);
        }

        /// <summary>
        /// Makes <paramref name="code"/> the active language, whatever it takes.
        ///
        /// <paramref name="current"/> is set even when the file is missing and
        /// the phrases came from the fallback, because the alternative is
        /// retrying the failed load on every single lookup.
        /// </summary>
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

                // Last one wins, and it is reported: a duplicated key is a merge
                // gone wrong, and silently keeping either copy hides it.
                if (phrases.ContainsKey(entry.key))
                {
                    Debug.LogWarning($"[Idioma] Clave duplicada '{entry.key}' en '{code}'.");
                }

                phrases[entry.key] = entry.value ?? string.Empty;
            }
        }

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

            // Cached even when null, so a missing file is looked for once rather
            // than on every lookup that falls through to it.
            files[code] = parsed;

            return parsed;
        }

        /// <summary>
        /// The best font for a language, or null to leave every Text with the
        /// face it already has.
        ///
        /// Tried in the order the file lists them and checked against what the
        /// machine actually has, because CreateDynamicFontFromOSFont happily
        /// returns a font object for a family that is not installed — and that
        /// object draws the same nothing the built-in one would. WebGL cannot
        /// see OS fonts at all (no such API in the browser sandbox), so that
        /// whole lookup is compiled out there and every language with
        /// <see cref="LocalizationFile.fontFamilies"/> falls straight through
        /// to the embedded fonts in <see cref="EmbeddedFontFallbacks"/> — which
        /// is also the safety net on desktop for a machine missing the family
        /// (e.g. Windows without the East Asian language pack).
        /// </summary>
        private static Font ResolveFont(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            // The == is Unity's, not C#'s: a cached font from a previous play
            // session has been destroyed by the domain reload, and comparing it
            // to null is how that is detected.
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

#if !UNITY_WEBGL
            string[] installed = Font.GetOSInstalledFontNames();

            foreach (string family in file.fontFamilies)
            {
                if (string.IsNullOrEmpty(family) || Array.IndexOf(installed, family) < 0)
                {
                    continue;
                }

                Font osFont = Font.CreateDynamicFontFromOSFont(family, DynamicFontSize);

                if (osFont == null)
                {
                    continue;
                }

                fonts[code] = osFont;
                return osFont;
            }
#endif

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

        /// <summary>
        /// Fonts bundled in <c>Resources</c>, tried in this order, as the
        /// fallback for when no OS family resolved. Not verified glyph-by-glyph
        /// against the dictionary — <see cref="Font.HasCharacter"/> was tried
        /// for that and measured to return <c>true</c> for every code point
        /// thrown at it, including ones Unicode guarantees are never assigned,
        /// so for a dynamic font it is not a real check and pretending it is
        /// would be worse than not checking at all. MainFont is Noto Sans JP,
        /// confirmed to cover kanji/kana by reading its actual glyph table
        /// offline (fontTools), not by asking Unity at runtime; the backup is a
        /// second, independently-sourced CJK font (M PLUS 1p) for if MainFont
        /// is ever swapped for something Latin-only — at that point this needs
        /// a real check again, and the only one this project has found
        /// trustworthy is <c>RequestCharactersInTexture</c> +
        /// <c>GetCharacterInfo</c>, not <c>HasCharacter</c>.
        /// </summary>
        private static readonly string[] EmbeddedFontFallbacks = { "MainFont", "MainFontJPBackup" };

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

        /// <summary>
        /// Whether the dictionary contains any character outside ASCII —
        /// i.e. whether this language needs a font at all, as opposed to one
        /// written entirely in the Latin alphabet the built-in face already
        /// draws.
        /// </summary>
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

        // A dynamic font rasterises at whatever size it is asked to draw, so
        // this is only the atlas it starts with. Large enough for the headings,
        // which are the biggest thing it will be asked for.
        private const int DynamicFontSize = 64;

        private static Font builtInFont;

        /// <summary>
        /// The face every menu caption is generated with, and therefore the one
        /// to go back to when a language needs no font of its own.
        ///
        /// The name changed in recent Unity versions, hence the fallback; the
        /// scene generator resolves it exactly the same way, which is what makes
        /// "back to the default" mean the same thing on both sides.
        /// </summary>
        private static Font BuiltInFont
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
