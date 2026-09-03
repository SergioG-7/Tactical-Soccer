using UnityEngine;
using UnityEngine.UI;

namespace TacticalSoccer.Core
{
    // Lleva el marcador del partido y pinta el marcador, el crono y el indicador de ronda.
    public class ScoreManager : MonoBehaviour
    {
        public const int BlueTeamId = 0;
        public const int RedTeamId = 1;

        [Tooltip("Etiqueta del marcador en la interfaz.")]
        public Text scoreText;

        [Tooltip("Etiqueta del temporizador del partido.")]
        public Text timerText;

        [Tooltip("Etiqueta que indica la ronda actual del torneo (oculta en partidas rápidas).")]
        public Text tournamentText;

        private int blueScore;
        private int redScore;

        // Solo se repinta cuando cambia el segundo mostrado, para no reconstruir la malla cada frame.
        private int lastPaintedSecond = -1;

        public static ScoreManager Instance { get; private set; }

        public int BlueScore => blueScore;
        public int RedScore => redScore;

        private void Awake()
        {
            Instance = this;
        }

        // Pinta el marcador a 0-0 al empezar.
        private void Start()
        {
            RefreshScoreboard();
        }

        private void OnEnable()
        {
            TacticalEvents.OnGoalScored += HandleGoalScored;
        }

        private void OnDisable()
        {
            TacticalEvents.OnGoalScored -= HandleGoalScored;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Actualiza el crono y el indicador de ronda del torneo.
        private void Update()
        {
            RefreshTournamentBadge();

            if (timerText == null || MatchManager.Instance == null)
            {
                return;
            }

            int secondsLeft = Mathf.CeilToInt(MatchManager.Instance.currentTime);

            if (secondsLeft == lastPaintedSecond)
            {
                return;
            }

            lastPaintedSecond = secondsLeft;

            LocalizationManager.WriteFormatted(timerText, "hud.timer",
                MatchManager.Instance.currentHalf, secondsLeft);
        }

        // Muestra el nombre de la ronda de torneo mientras hay una en curso, y lo oculta el resto del tiempo.
        private void RefreshTournamentBadge()
        {
            if (tournamentText == null)
            {
                return;
            }

            TournamentManager tournament = TournamentManager.Instance;
            bool active = tournament != null && tournament.IsRunning;

            string caption = active ? tournament.DescribeCurrentRoundBadge() : string.Empty;

            if (caption == lastPaintedBadge)
            {
                return;
            }

            lastPaintedBadge = caption;

            tournamentText.text = caption;
            tournamentText.gameObject.SetActive(active);
        }

        private string lastPaintedBadge;

        // Pone el marcador a cero.
        public void ResetScores()
        {
            blueScore = 0;
            redScore = 0;

            RefreshScoreboard();
        }

        // Suma un gol al equipo correspondiente y repinta el marcador.
        private void HandleGoalScored(int teamId)
        {
            switch (teamId)
            {
                case BlueTeamId:
                    blueScore++;
                    break;

                case RedTeamId:
                    redScore++;
                    break;

                default:
                    Debug.LogWarning($"Gol anotado por un equipo desconocido (id={teamId}). Se ignora.");
                    return;
            }

            RefreshScoreboard();

            Debug.Log($"¡GOL! Marcador — Azul {blueScore} - {redScore} Rojo");
        }

        // Escribe el marcador actual en el texto del HUD.
        private void RefreshScoreboard()
        {
            if (scoreText != null)
            {
                scoreText.text = $"{blueScore} - {redScore}";
            }
        }
    }
}
