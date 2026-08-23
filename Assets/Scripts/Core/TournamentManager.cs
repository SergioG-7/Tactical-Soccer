using UnityEngine;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.Core
{
    // Datos de una ronda del torneo: dificultad, formación y color del rival.
    public struct TournamentRound
    {
        public string Label;
        public AIDifficulty Difficulty;
        public FormationType Shape;
        public Color RivalColor;
        public string RivalColorName;
    }

    // Gestiona el modo torneo: tres rondas seguidas, con el progreso guardado entre sesiones.
    public class TournamentManager : MonoBehaviour
    {
        public const int RoundCount = 3;

        // Duración fija de cada partido de torneo.
        public const float RoundDurationSeconds = 60f;

        public static TournamentManager Instance { get; private set; }

        // Ronda actual, de 0 a 2. Vuelve a 0 al ganar la final o al perder.
        public int Stage { get; private set; }

        // Cierto entre el saque inicial y el final de un partido de torneo.
        public bool IsRunning { get; private set; }

        // Cierto si el último partido jugado fue una ronda de torneo.
        public bool LastMatchWasTournament { get; private set; }

        // Cierto si esa ronda se ganó.
        public bool LastMatchWon { get; private set; }

        // Cierto cuando la racha ha terminado, ya sea ganando la final o perdiendo una ronda.
        public bool RunEnded { get; private set; }

        // Indica si hay una racha de torneo en marcha con rondas pendientes.
        public bool IsTournamentActive => LastMatchWasTournament && !RunEnded;

        // Mensaje de cómo terminó el último partido de torneo, para la pantalla de resultado.
        public string PendingOutcome { get; private set; }

        // Recupera la instancia única y la etapa guardada.
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Stage = Mathf.Clamp(SaveManager.Data.tournamentStage, 0, RoundCount - 1);
        }

        // Limpia la referencia al singleton al destruirse.
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Devuelve los datos de la ronda indicada (cuartos, semifinal o final).
        public static TournamentRound GetRound(int stage)
        {
            switch (Mathf.Clamp(stage, 0, RoundCount - 1))
            {
                case 1:
                    return new TournamentRound
                    {
                        Label = "SEMIFINAL",
                        Difficulty = AIDifficulty.Normal,
                        Shape = FormationType.Defensive_3_2_1,
                        RivalColor = new Color(0.62f, 0.24f, 0.80f, 1f),
                        RivalColorName = "Morado"
                    };

                case 2:
                    return new TournamentRound
                    {
                        Label = "FINAL",
                        Difficulty = AIDifficulty.Dificil,
                        Shape = FormationType.Offensive_1_3_2,
                        RivalColor = new Color(0.85f, 0.68f, 0.13f, 1f),
                        RivalColorName = "Dorado"
                    };

                default:
                    return new TournamentRound
                    {
                        Label = "CUARTOS",
                        Difficulty = AIDifficulty.Facil,
                        Shape = FormationType.Balanced_2_2_2,
                        RivalColor = new Color(1f, 0.65f, 0.05f, 1f),
                        RivalColorName = "Naranja"
                    };
            }
        }

        public TournamentRound CurrentRound => GetRound(Stage);

        // Nombre de la ronda en el idioma del jugador.
        public static string DescribeRound(int stage)
        {
            switch (Mathf.Clamp(stage, 0, RoundCount - 1))
            {
                case 1: return LocalizationManager.GetText("tournament.round.semis");
                case 2: return LocalizationManager.GetText("tournament.round.final");
                default: return LocalizationManager.GetText("tournament.round.quarters");
            }
        }

        // Versión corta del nombre de ronda para el HUD del partido.
        public string DescribeCurrentRoundBadge()
        {
            switch (Stage)
            {
                case 1: return LocalizationManager.GetText("tournament.hud.semis");
                case 2: return LocalizationManager.GetText("tournament.hud.final");
                default: return LocalizationManager.GetText("tournament.hud.quarters");
            }
        }

        // Clave de localización para el texto del botón de torneo en el menú principal.
        public string NextRoundKey()
        {
            switch (Stage)
            {
                case 1: return "tournament.next.semis";
                case 2: return "tournament.next.final";
                default: return "tournament.next.quarters";
            }
        }

        // Empieza un partido de torneo con los parámetros de la ronda actual.
        public void BeginRound()
        {
            IsRunning = true;
            PendingOutcome = null;

            LastMatchWasTournament = true;
            LastMatchWon = false;
            RunEnded = false;

            TournamentRound round = CurrentRound;

            if (MatchManager.Instance == null)
            {
                Debug.LogError("No hay MatchManager: no se puede empezar el torneo.");
                return;
            }

            MatchManager.Instance.ConfigureTournamentMatch(
                RoundDurationSeconds, round.Difficulty, round.Shape, round.RivalColor);

            Debug.Log($"[Torneo] {round.Label}: rival {round.Difficulty}, " +
                      $"{Formations.GetLabel(round.Shape)}, color {round.RivalColorName}, " +
                      $"{RoundDurationSeconds:F0} s por parte.");
        }

        // Resuelve el resultado de la ronda jugada. Un empate cuenta como eliminación.
        public void ReportResult(int humanGoals, int rivalGoals)
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;

            bool won = humanGoals > rivalGoals;

            LastMatchWon = won;

            RunEnded = !won || Stage >= RoundCount - 1;

            if (!won)
            {
                PendingOutcome = Stage == 0
                    ? LocalizationManager.GetText("tournament.lost")
                    : LocalizationManager.Format("tournament.eliminated", DescribeRound(Stage));

                Debug.Log($"[Torneo] Derrota en {CurrentRound.Label} ({humanGoals}-{rivalGoals}). Torneo reiniciado.");
                SetStage(0);
                return;
            }

            if (Stage >= RoundCount - 1)
            {
                PendingOutcome = LocalizationManager.GetText("tournament.champion");

                Debug.Log($"[Torneo] ¡FINAL GANADA {humanGoals}-{rivalGoals}! Torneo superado y reiniciado.");
                SetStage(0);
                return;
            }

            string clearedLabel = CurrentRound.Label;

            SetStage(Stage + 1);

            PendingOutcome = LocalizationManager.Format("tournament.advance", DescribeRound(Stage));

            Debug.Log($"[Torneo] Victoria {humanGoals}-{rivalGoals} en {clearedLabel}. " +
                      $"Siguiente ronda: {CurrentRound.Label}.");
        }

        // Devuelve el mensaje de resultado pendiente y lo borra, para que se muestre solo una vez.
        public string ConsumeOutcome()
        {
            string outcome = PendingOutcome;
            PendingOutcome = null;

            return outcome;
        }

        // Abandona la racha de torneo en curso.
        public void Abandon()
        {
            IsRunning = false;
            PendingOutcome = null;

            LastMatchWasTournament = false;
            LastMatchWon = false;
            RunEnded = false;
        }

        // Actualiza la etapa del torneo y la guarda de inmediato.
        private void SetStage(int stage)
        {
            Stage = Mathf.Clamp(stage, 0, RoundCount - 1);

            SaveManager.Data.tournamentStage = Stage;
            SaveManager.SaveNow();
        }
    }
}
