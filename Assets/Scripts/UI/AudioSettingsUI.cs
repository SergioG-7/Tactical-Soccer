using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// The options: what language the game speaks, and how loud the crowd, the
    /// referee's whistle, and everything else are, each on its own slider.
    ///
    /// The volumes apply as they are dragged rather than on closing. A volume
    /// slider is the one setting nobody can judge from its number — the whole
    /// point is to hear the result while you move it — so the crowd bed keeps
    /// playing underneath this panel instead of being paused with the rest of
    /// the match. Nothing here has an apply step and nothing is lost by closing
    /// the panel: every change is written to the save file as it is made.
    ///
    /// The language row rewrites the screen it is on. That is deliberate and it
    /// is the honest way to present the choice — a language you cannot see
    /// applied until you back out is a language you have to take on trust — and
    /// it is what <see cref="LocalizedText"/> is for. Each button is labelled in
    /// its OWN language, with its own font, so 日本語 is readable while the game
    /// is still in Spanish.
    ///
    /// Reachable from the title and from the developer menu, and it is the same
    /// panel both times: a second copy of a settings screen is a second copy
    /// that can disagree with the first.
    ///
    /// Lives on the canvas rather than on the panel it owns — a component on a
    /// deactivated GameObject never receives Start, and Start is where the
    /// controls are wired.
    /// </summary>
    public class AudioSettingsUI : MonoBehaviour
    {
        public GameObject uiPanel;

        public Slider musicSlider;
        public Slider whistleSlider;
        public Slider sfxSlider;

        public Button closeButton;

        [Tooltip("One button per entry of LocalizationManager.AvailableLanguages, " +
                 "in the same order. Shorter is allowed — the extra languages " +
                 "simply have no button — but the order is what pairs a button " +
                 "with the language it selects.")]
        public Button[] languageButtons;

        [Tooltip("Reads the two levels back as percentages, because a slider " +
                 "handle on its own gives no way to return to a setting you liked.")]
        public Text readoutText;

        [Header("Colores")]
        [Tooltip("Fill of the language button currently in force.")]
        public Color selectedColor = new Color(0.20f, 0.65f, 0.95f, 1f);

        public Color unselectedColor = new Color(0.88f, 0.88f, 0.88f, 1f);

        // Unscaled timestamp the effects preview is due at, or 0 for "nothing
        // pending". Long enough that a sweep of the handle produces one whistle
        // at the end of it, short enough not to feel like a delay.
        private const float PreviewSettleSeconds = 0.25f;

        private float musicPreviewDueAt;
        private float whistlePreviewDueAt;
        private float sfxPreviewDueAt;

        public static AudioSettingsUI Instance { get; private set; }

        /// <summary>True while the panel is up. Read by the input manager.</summary>
        public static bool IsOpen => Instance != null
            && Instance.uiPanel != null
            && Instance.uiPanel.activeSelf;

        private void Awake()
        {
            Instance = this;

            // Awake only runs in play mode, so this is what keeps the panel off
            // the screen in the editor.
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        private void OnEnable()
        {
            // This controller lives on the canvas and is never deactivated, so
            // it hears every change — including one made from its own buttons,
            // which is how the tints and the readout follow along.
            LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
        }

        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            Bind(musicSlider, OnMusicChanged);
            Bind(whistleSlider, OnWhistleChanged);
            Bind(sfxSlider, OnSfxChanged);

            BindLanguageButtons();

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Close);
            }
            else
            {
                Debug.LogError("AudioSettingsUI no tiene botón de cerrar: el panel " +
                               "taparía la pantalla sin salida.");
            }
        }

        /// <summary>
        /// Wires each language button to the code at its own index, and labels
        /// it in that language.
        ///
        /// The caption is written here rather than left to a LocalizedText: it
        /// must NOT follow the active language. A row where every button reads
        /// "Spanish, English, Japanese" in whatever is currently selected is a
        /// row you have to already know the answer to use — the convention every
        /// language picker follows is that each option names itself.
        /// </summary>
        private void BindLanguageButtons()
        {
            if (languageButtons == null)
            {
                return;
            }

            string[] codes = LocalizationManager.AvailableLanguages;

            for (int i = 0; i < languageButtons.Length && i < codes.Length; i++)
            {
                Button button = languageButtons[i];

                if (button == null)
                {
                    continue;
                }

                string code = codes[i];

                Text label = button.GetComponentInChildren<Text>();

                if (label != null)
                {
                    label.text = LocalizationManager.DisplayName(code);

                    // That language's own font, not the active one's: this label
                    // has to be legible before its language is chosen, which is
                    // the entire job of a language button.
                    LocalizationManager.ApplyFontFor(label, code);
                }

                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => LocalizationManager.SetLanguage(code));
            }

            RefreshLanguageTints();
        }

        /// <summary>
        /// Opens the panel on the settings currently in force, rather than on
        /// whatever the controls happened to be left at in the scene.
        /// </summary>
        public void ShowMenu()
        {
            Audio.AudioManager audio = Audio.AudioManager.Instance;

            if (audio != null)
            {
                // SetValueWithoutNotify, or seeding the handle would fire the
                // callback and write the value straight back — harmless here,
                // but it would also save on merely opening the panel, which is
                // not what opening a panel should do.
                if (musicSlider != null)
                {
                    musicSlider.SetValueWithoutNotify(audio.MusicVolume);
                }

                if (whistleSlider != null)
                {
                    whistleSlider.SetValueWithoutNotify(audio.WhistleVolume);
                }

                if (sfxSlider != null)
                {
                    sfxSlider.SetValueWithoutNotify(audio.SfxVolume);
                }
            }

            UIAnimator.Show(uiPanel);

            RefreshLanguageTints();
            RefreshReadout();
        }

        public void Close()
        {
            UIAnimator.Hide(uiPanel);
        }

        private void HandleLanguageChanged()
        {
            RefreshLanguageTints();

            // The readout is a sentence with numbers in it, so it is composed
            // here rather than being a key on its own — which means nothing else
            // would rewrite it.
            RefreshReadout();
        }

        private void RefreshLanguageTints()
        {
            if (languageButtons == null)
            {
                return;
            }

            string[] codes = LocalizationManager.AvailableLanguages;
            string current = LocalizationManager.Current;

            for (int i = 0; i < languageButtons.Length && i < codes.Length; i++)
            {
                if (languageButtons[i] == null || languageButtons[i].targetGraphic == null)
                {
                    continue;
                }

                languageButtons[i].targetGraphic.color =
                    codes[i] == current ? selectedColor : unselectedColor;
            }
        }

        private void OnMusicChanged(float value)
        {
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.SetMusicVolume(value);
            }

            // Armed, not played. Same reason as the effects slider below: this
            // fires on every frame of a drag, and starting a two-second crowd
            // preview forty times over one sweep would be a stutter, not a
            // preview.
            musicPreviewDueAt = Time.unscaledTime + PreviewSettleSeconds;

            RefreshReadout();
        }

        private void OnWhistleChanged(float value)
        {
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.SetWhistleVolume(value);
            }

            // Only ARMS the preview. onValueChanged fires on every frame of a
            // drag, so playing here directly would be a whistle per frame —
            // forty of them over one sweep of the handle, each cutting the last.
            whistlePreviewDueAt = Time.unscaledTime + PreviewSettleSeconds;

            RefreshReadout();
        }

        private void OnSfxChanged(float value)
        {
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.SetSfxVolume(value);
            }

            // Only ARMS the preview, same reasoning as the whistle slider above.
            // The preview is a ball strike rather than a whistle now that the
            // whistles have their own channel and their own slider to preview.
            sfxPreviewDueAt = Time.unscaledTime + PreviewSettleSeconds;

            RefreshReadout();
        }

        /// <summary>
        /// Plays each preview once its handle has been still for a moment, which
        /// is the point at which the player is listening rather than still
        /// moving.
        ///
        /// Unscaled time throughout: this panel is opened from the title and
        /// from the developer menu, and both of those hold the match at
        /// timeScale 0, where a scaled timer would never come due.
        /// </summary>
        private void Update()
        {
            Audio.AudioManager audio = Audio.AudioManager.Instance;

            if (audio == null)
            {
                return;
            }

            if (musicPreviewDueAt > 0f && Time.unscaledTime >= musicPreviewDueAt)
            {
                musicPreviewDueAt = 0f;
                audio.PreviewCrowd();
            }

            if (whistlePreviewDueAt > 0f && Time.unscaledTime >= whistlePreviewDueAt)
            {
                whistlePreviewDueAt = 0f;
                audio.PlayWhistle(isLong: false);
            }

            if (sfxPreviewDueAt > 0f && Time.unscaledTime >= sfxPreviewDueAt)
            {
                sfxPreviewDueAt = 0f;
                audio.PlayKick();
            }
        }

        private void RefreshReadout()
        {
            if (readoutText == null)
            {
                return;
            }

            Audio.AudioManager audio = Audio.AudioManager.Instance;

            float music = audio != null ? audio.MusicVolume : 0f;
            float whistle = audio != null ? audio.WhistleVolume : 0f;
            float sfx = audio != null ? audio.SfxVolume : 0f;

            LocalizationManager.WriteFormatted(readoutText, "options.readout",
                (music * 100f).ToString("F0"), (whistle * 100f).ToString("F0"),
                (sfx * 100f).ToString("F0"));
        }

        private static void Bind(Slider slider, UnityEngine.Events.UnityAction<float> action)
        {
            if (slider == null)
            {
                return;
            }

            slider.minValue = 0f;
            slider.maxValue = 1f;

            // Cleared first: these listeners are added from code on every load,
            // and a duplicate would write the same value twice per drag frame.
            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(action);
        }
    }
}
