using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;

namespace TacticalSoccer.UI
{
    // Controla la pantalla de título: mantiene el partido congelado hasta que el jugador pulsa Jugar.
    public class TitleScreenUIController : MonoBehaviour
    {
        public GameObject uiPanel;
        public Button playButton;

        [Tooltip("Inicia o continúa el modo torneo saltando la pantalla de ajustes.")]
        public Button tournamentButton;

        [Tooltip("Texto del botón de torneo que indica la ronda actual o siguiente.")]
        public Text tournamentLabel;

        [Tooltip("Texto que muestra el resultado del último partido disputado en el torneo.")]
        public Text tournamentOutcomeText;

        [Tooltip("Abre el menú de configuración de audio.")]
        public Button optionsButton;

        [Tooltip("Referencia a la pantalla de configuración de partido.")]
        public MatchConfigUIController configMenu;

        [Tooltip("Referencia a la pantalla de selección de alineación y formación.")]
        public FormationUIController formationMenu;

        private const float FrozenTimeScale = 0f;
        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        public static TitleScreenUIController Instance { get; private set; }

        // True mientras la pantalla de título está visible.
        public static bool IsOpen => Instance != null
            && Instance.uiPanel != null
            && Instance.uiPanel.activeSelf;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Vuelve a mostrar la pantalla de título y congela el partido.
        public void ShowTitle()
        {
            UIAnimator.Show(uiPanel);

            Time.timeScale = FrozenTimeScale;

            RefreshTournament();
        }

        // Actualiza el texto del botón de torneo y muestra el resultado de la última ronda jugada.
        private void RefreshTournament()
        {
            TournamentManager tournament = TournamentManager.Instance;

            if (tournamentLabel != null)
            {
                LocalizedText.Write(tournamentLabel, tournament != null
                    ? tournament.NextRoundKey()
                    : "tournament.next.quarters");
            }

            if (tournamentOutcomeText == null)
            {
                return;
            }

            // Se consume para no volver a mostrar el mismo resultado si se regresa al título otra vez.
            string outcome = tournament != null ? tournament.ConsumeOutcome() : null;

            tournamentOutcomeText.text = outcome ?? string.Empty;
        }

        // Muestra el panel de título y engancha los botones.
        private void Start()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(true);
            }

            Time.timeScale = FrozenTimeScale;

            if (playButton == null)
            {
                Debug.LogError("TitleScreenUIController no tiene botón: no habría forma de empezar.");
                return;
            }

            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(StartGame);

            if (optionsButton != null)
            {
                optionsButton.onClick.RemoveAllListeners();
                optionsButton.onClick.AddListener(OpenOptions);
            }

            if (tournamentButton != null)
            {
                tournamentButton.onClick.RemoveAllListeners();
                tournamentButton.onClick.AddListener(StartTournament);
            }

            RefreshTournament();
        }

        // Empieza o continúa una ronda de torneo, yendo directo a la pantalla de alineación.
        public void StartTournament()
        {
            TournamentManager tournament = TournamentManager.Instance;

            if (tournament == null)
            {
                Debug.LogError("No hay TournamentManager en la escena: no se puede empezar el torneo.");
                return;
            }

            tournament.BeginRound();

            UIAnimator.Hide(uiPanel);

            FormationUIController menu = formationMenu != null
                ? formationMenu
                : FormationUIController.Instance;

            if (menu != null)
            {
                menu.ShowMenu();
                return;
            }

            // No hay pantalla de alineación en la escena, se empieza directamente.
            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.StartInitialKickoff();
            }
        }

        // Abre las opciones de audio encima de la pantalla de título.
        public void OpenOptions()
        {
            if (AudioSettingsUI.Instance != null)
            {
                AudioSettingsUI.Instance.ShowMenu();
                return;
            }

            Debug.LogWarning("No hay panel de opciones de audio en la escena.");
        }

        // Empieza una partida rápida, abandonando cualquier ronda de torneo en curso.
        public void StartGame()
        {
            if (TournamentManager.Instance != null)
            {
                TournamentManager.Instance.Abandon();
            }

            UIAnimator.Hide(uiPanel);

            MatchConfigUIController settings = configMenu != null
                ? configMenu
                : MatchConfigUIController.Instance;

            if (settings != null)
            {
                settings.ShowMenu();
                return;
            }

            FormationUIController menu = formationMenu != null
                ? formationMenu
                : FormationUIController.Instance;

            if (menu != null)
            {
                menu.ShowMenu();
                return;
            }

            // No hay más pantallas, así que se empieza el partido directamente.
            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.StartInitialKickoff();
                return;
            }

            Debug.LogError("No hay MatchManager: no se puede empezar el partido.");
        }
    }
}
