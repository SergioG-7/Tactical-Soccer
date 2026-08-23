using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    // Pantalla de fin de partido: resultado, estadísticas comparadas y botones para continuar.
    public class MatchOverUIController : MonoBehaviour
    {
        public GameObject uiPanel;
        public Text resultText;

        [Tooltip("Tabla comparativa con las estadísticas del partido.")]
        public Text statsText;

        public Button restartButton;

        [Tooltip("Botón para volver a la pantalla de título y cambiar la configuración.")]
        public Button menuButton;

        // Oculta el panel al arrancar.
        private void Awake()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        // Se suscribe al evento de fin de partido.
        private void OnEnable()
        {
            TacticalEvents.OnMatchOver += HandleMatchOver;
        }

        // Se desuscribe del evento de fin de partido.
        private void OnDisable()
        {
            TacticalEvents.OnMatchOver -= HandleMatchOver;
        }

        // Conecta los botones de reiniciar y volver al menú.
        private void Start()
        {
            if (restartButton != null)
            {
                restartButton.onClick.RemoveAllListeners();
                restartButton.onClick.AddListener(RestartMatch);
            }

            if (menuButton != null)
            {
                menuButton.onClick.RemoveAllListeners();
                menuButton.onClick.AddListener(ReturnToTitle);
            }
        }

        // Muestra el resultado (desde el punto de vista del equipo Azul) y las estadísticas del partido.
        private void HandleMatchOver()
        {
            int blue = ScoreManager.Instance != null ? ScoreManager.Instance.BlueScore : 0;
            int red = ScoreManager.Instance != null ? ScoreManager.Instance.RedScore : 0;

            if (resultText != null)
            {
                if (blue > red)
                {
                    LocalizedText.Write(resultText, "matchover.victory");
                    resultText.color = Color.green;
                }
                else if (blue < red)
                {
                    LocalizedText.Write(resultText, "matchover.defeat");
                    resultText.color = Color.red;
                }
                else
                {
                    LocalizedText.Write(resultText, "matchover.draw");
                    resultText.color = Color.yellow;
                }
            }

            WriteStatistics();
            RefreshTournamentButtons();

            UIAnimator.Show(uiPanel);
        }

        // Cambia el texto y la acción de los botones cuando el partido era una ronda de torneo.
        private void RefreshTournamentButtons()
        {
            TournamentManager tournament = TournamentManager.Instance;

            bool inTournament = tournament != null && tournament.LastMatchWasTournament;

            bool advancing = inTournament && tournament.LastMatchWon && !tournament.RunEnded;

            SetButton(restartButton,
                inTournament
                    ? (advancing ? "matchover.nextRound" : "matchover.finishTournament")
                    : "matchover.playAgain",
                inTournament ? (advancing ? (System.Action)ContinueTournament : ReturnToTitle) : RestartMatch);

            if (menuButton != null)
            {
                menuButton.gameObject.SetActive(!inTournament || advancing);
            }
        }

        // Cambia el texto de un botón y lo conecta a una nueva acción.
        private static void SetButton(Button button, string captionKey, System.Action action)
        {
            if (button == null)
            {
                return;
            }

            LocalizedText.Write(button.GetComponentInChildren<Text>(), captionKey);

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => action());
        }

        // Pasa directamente a la siguiente ronda del torneo, sin volver al título.
        public void ContinueTournament()
        {
            UIAnimator.Hide(uiPanel);

            if (MatchManager.Instance == null || TournamentManager.Instance == null)
            {
                Debug.LogError("Falta MatchManager o TournamentManager: no se puede continuar el torneo.");
                return;
            }

            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.ResetScores();
            }

            TournamentManager.Instance.BeginRound();
            MatchManager.Instance.RestartMatch();
            MatchManager.Instance.StartInitialKickoff();
        }

        // Construye la tabla comparativa de estadísticas, con Azul a la izquierda.
        private void WriteStatistics()
        {
            if (statsText == null)
            {
                return;
            }

            MatchManager match = MatchManager.Instance;

            if (match == null)
            {
                statsText.text = string.Empty;
                return;
            }

            int blueGoals = ScoreManager.Instance != null ? ScoreManager.Instance.BlueScore : 0;
            int redGoals = ScoreManager.Instance != null ? ScoreManager.Instance.RedScore : 0;

            middleColumnWidth = System.Math.Max(MinimumMiddleWidth, System.Math.Max(
                System.Math.Max(VisualWidth(Label("matchover.goals")), VisualWidth(Label("matchover.shots"))),
                System.Math.Max(VisualWidth(Label("matchover.fouls")), VisualWidth(Label("matchover.passes")))));

            ApplyTableFont();

            string table = Row(Label("team.blue"), string.Empty, Label("team.red")) + "\n";

            table += Row(blueGoals.ToString(), Label("matchover.goals"), redGoals.ToString()) + "\n";

            table += Row(match.ShotsFor(TeamId.Blue).ToString(), Label("matchover.shots"),
                         match.ShotsFor(TeamId.Red).ToString()) + "\n";

            table += Row(match.FoulsFor(TeamId.Blue).ToString(), Label("matchover.fouls"),
                         match.FoulsFor(TeamId.Red).ToString()) + "\n";

            table += Row(match.PassesFor(TeamId.Blue).ToString(), Label("matchover.passes"),
                         match.PassesFor(TeamId.Red).ToString());

            statsText.text = table;
        }

        // Texto de una cabecera de columna en el idioma actual.
        private static string Label(string key)
        {
            return Core.LocalizationManager.GetText(key);
        }

        // Construye una fila de la tabla con la columna central centrada.
        private static string Row(string left, string middle, string right)
        {
            int padding = System.Math.Max(0, middleColumnWidth - VisualWidth(middle));
            int before = padding / 2;

            string centred = new string(' ', before) + middle + new string(' ', padding - before);

            return $"{left,6}   {centred}   {right,-6}";
        }

        // Ancho de la cabecera más larga del idioma actual, en celdas monoespaciadas.
        private static int middleColumnWidth = MinimumMiddleWidth;

        private const int MinimumMiddleWidth = 8;

        // Cuántas celdas monoespaciadas ocupa un texto (los caracteres CJK cuentan doble).
        private static int VisualWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int width = 0;

            foreach (char c in text)
            {
                width += c >= 0x1100 ? 2 : 1;
            }

            return width;
        }

        // Usa una fuente monoespaciada para la tabla, salvo que el idioma necesite su propia fuente.
        private void ApplyTableFont()
        {
            if (statsText == null)
            {
                return;
            }

            if (tableFont == null)
            {
                tableFont = statsText.font;
            }

            Font languageFont = Core.LocalizationManager.ActiveFont;

            statsText.font = languageFont != null ? languageFont : tableFont;
        }

        // Fuente monoespaciada original, para restaurarla cuando el idioma no necesite una propia.
        private Font tableFont;

        // Cierra el panel y reinicia el partido.
        public void RestartMatch()
        {
            UIAnimator.Hide(uiPanel);

            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.ResetScores();
            }

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.RestartMatch();
                return;
            }

            Debug.LogError("No hay MatchManager: no se puede reiniciar el partido.");
        }

        // Cierra el panel, reinicia el partido y vuelve a la pantalla de título.
        public void ReturnToTitle()
        {
            UIAnimator.Hide(uiPanel);

            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.ResetScores();
            }

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.ReturnToTitle();
            }

            if (TitleScreenUIController.Instance != null)
            {
                TitleScreenUIController.Instance.ShowTitle();
                return;
            }

            Debug.LogWarning("No hay pantalla de título a la que volver: el partido queda reiniciado.");
        }
    }
}
