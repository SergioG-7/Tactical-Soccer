using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;

namespace TacticalSoccer.UI
{
    // Pantalla de descanso: aparece al final de la primera parte y permite hacer cambios o continuar el partido.
    public class HalftimeUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Header("Textos")]
        public Text headingText;

        [Tooltip("Score and half, so the team talk says which match this is.")]
        public Text summaryText;

        [Header("Botones")]
        public Button substitutionsButton;
        public Button secondHalfButton;

        public static HalftimeUIController Instance { get; private set; }

        private void Awake()
        {
            Instance = this;

            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        private void OnEnable()
        {
            TacticalEvents.OnHalftime += ShowInterval;
            TacticalEvents.OnMatchOver += HandleMatchOver;
            LocalizationManager.OnLanguageChanged += WriteSummary;
        }

        private void OnDisable()
        {
            TacticalEvents.OnHalftime -= ShowInterval;
            TacticalEvents.OnMatchOver -= HandleMatchOver;
            LocalizationManager.OnLanguageChanged -= WriteSummary;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Engancha los botones de cambios y de segunda parte.
        private void Start()
        {
            if (substitutionsButton != null)
            {
                substitutionsButton.onClick.RemoveAllListeners();
                substitutionsButton.onClick.AddListener(OpenSubstitutions);
            }

            if (secondHalfButton != null)
            {
                secondHalfButton.onClick.RemoveAllListeners();
                secondHalfButton.onClick.AddListener(StartSecondHalf);
            }
            else
            {
                Debug.LogError("HalftimeUIController no tiene botón de segunda parte: " +
                               "el partido no podría reanudarse nunca.");
            }
        }

        // Muestra la pantalla de descanso con el resumen del partido.
        public void ShowInterval()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(true);
            }

            WriteSummary();
        }

        // Abre la pantalla de cambios, guardando este panel como pantalla de vuelta.
        public void OpenSubstitutions()
        {
            if (SubstitutionUIController.Instance == null)
            {
                Debug.LogWarning("No hay pantalla de cambios en la escena.");
                return;
            }

            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            SubstitutionUIController.Instance.ShowBoard(uiPanel);
        }

        // Cierra el descanso y pide al MatchManager que empiece la segunda parte.
        public void StartSecondHalf()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            if (MatchManager.Instance == null)
            {
                Debug.LogError("No hay MatchManager: no se puede empezar la segunda parte.");
                return;
            }

            MatchManager.Instance.StartSecondHalf();
        }

        // Oculta el panel de descanso si el partido termina.
        private void HandleMatchOver()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        // Escribe el título y el resumen del marcador en el panel de descanso.
        private void WriteSummary()
        {
            LocalizedText.Write(headingText, "halftime.heading");

            if (summaryText == null)
            {
                return;
            }

            if (ScoreManager.Instance == null)
            {
                LocalizedText.Write(summaryText, "halftime.summaryNoScore");
                return;
            }

            LocalizationManager.WriteFormatted(summaryText, "halftime.summary",
                ScoreManager.Instance.BlueScore, ScoreManager.Instance.RedScore);
        }
    }
}
