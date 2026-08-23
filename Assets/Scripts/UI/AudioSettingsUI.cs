using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;

namespace TacticalSoccer.UI
{
    // Pantalla de opciones: idioma del juego y volumen de música, silbato y efectos.
    public class AudioSettingsUI : MonoBehaviour
    {
        public GameObject uiPanel;

        public Slider musicSlider;
        public Slider whistleSlider;
        public Slider sfxSlider;

        public Button closeButton;

        [Tooltip("Botones de selección de idioma ordenados según LocalizationManager.AvailableLanguages.")]
        public Button[] languageButtons;

        [Tooltip("Texto para mostrar los niveles de audio en formato de porcentaje.")]
        public Text readoutText;

        [Tooltip("Color de resaltado para el botón del idioma seleccionado.")]
        public Color selectedColor = new Color(0.20f, 0.65f, 0.95f, 1f);

        public Color unselectedColor = new Color(0.88f, 0.88f, 0.88f, 1f);

        // Tiempo que debe estar quieto el slider antes de reproducir el sonido de previsualización.
        private const float PreviewSettleSeconds = 0.25f;

        private float musicPreviewDueAt;
        private float whistlePreviewDueAt;
        private float sfxPreviewDueAt;

        public static AudioSettingsUI Instance { get; private set; }

        // Cierto mientras el panel de opciones está abierto.
        public static bool IsOpen => Instance != null
            && Instance.uiPanel != null
            && Instance.uiPanel.activeSelf;

        // Inicializa el singleton y oculta el panel.
        private void Awake()
        {
            Instance = this;

            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        // Se suscribe al cambio de idioma para refrescar la pantalla.
        private void OnEnable()
        {
            LocalizationManager.OnLanguageChanged += HandleLanguageChanged;
        }

        // Se desuscribe del cambio de idioma y limpia el singleton.
        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Conecta los sliders, los botones de idioma y el botón de cerrar.
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

        // Conecta cada botón de idioma con su código y lo etiqueta en ese propio idioma.
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
                    LocalizationManager.ApplyFontFor(label, code);
                }

                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => LocalizationManager.SetLanguage(code));
            }

            RefreshLanguageTints();
        }

        // Abre el panel con los sliders puestos en los valores actuales.
        public void ShowMenu()
        {
            Audio.AudioManager audio = Audio.AudioManager.Instance;

            if (audio != null)
            {
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

        // Cierra el panel de opciones.
        public void Close()
        {
            UIAnimator.Hide(uiPanel);
        }

        // Refresca los tintes de los botones de idioma y el texto de lectura al cambiar el idioma.
        private void HandleLanguageChanged()
        {
            RefreshLanguageTints();
            RefreshReadout();
        }

        // Colorea el botón del idioma activo.
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

        // Aplica el volumen de música y arma la previsualización.
        private void OnMusicChanged(float value)
        {
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.SetMusicVolume(value);
            }

            musicPreviewDueAt = Time.unscaledTime + PreviewSettleSeconds;

            RefreshReadout();
        }

        // Aplica el volumen del silbato y arma la previsualización.
        private void OnWhistleChanged(float value)
        {
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.SetWhistleVolume(value);
            }

            whistlePreviewDueAt = Time.unscaledTime + PreviewSettleSeconds;

            RefreshReadout();
        }

        // Aplica el volumen de efectos y arma la previsualización.
        private void OnSfxChanged(float value)
        {
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.SetSfxVolume(value);
            }

            sfxPreviewDueAt = Time.unscaledTime + PreviewSettleSeconds;

            RefreshReadout();
        }

        // Reproduce cada previsualización de sonido cuando su slider lleva un momento quieto.
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

        // Escribe el texto con los porcentajes de volumen actuales.
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

        // Configura el rango de un slider y lo conecta a su acción, evitando listeners duplicados.
        private static void Bind(Slider slider, UnityEngine.Events.UnityAction<float> action)
        {
            if (slider == null)
            {
                return;
            }

            slider.minValue = 0f;
            slider.maxValue = 1f;

            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(action);
        }
    }
}
