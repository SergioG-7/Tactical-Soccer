using UnityEngine;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.Core
{
    /// <summary>
    /// One round of the tournament: everything about the side you are drawn
    /// against, in one place.
    /// </summary>
    public struct TournamentRound
    {
        public string Label;
        public AIDifficulty Difficulty;
        public FormationType Shape;
        public Color RivalColor;
        public string RivalColorName;
    }

    /// <summary>
    /// Three matches, in order, with the progress remembered between sessions.
    ///
    /// The whole mode is a table and a counter. There is no bracket, no seeding
    /// and no opponent roster: the three rounds differ by how hard the AI plays,
    /// what shape it lines up in and what colour it wears, and that is enough to
    /// make a run feel like it is escalating without inventing a second game
    /// underneath the first.
    ///
    /// The counter lives in the save file rather than in the scene because the
    /// point of a tournament is that closing the game does not lose it.
    ///
    /// Persistent across scene loads, unlike every other manager in this
    /// project: this is the one piece of state that has to outlive a match, and
    /// the guard in Awake is what stops the scene generator's copy doubling it.
    /// </summary>
    public class TournamentManager : MonoBehaviour
    {
        public const int RoundCount = 3;

        /// <summary>Every tournament match is this long, whatever the quick-match screen says.</summary>
        public const float RoundDurationSeconds = 60f;

        public static TournamentManager Instance { get; private set; }

        /// <summary>Which round is next, 0..2. Reset to 0 by winning the final or losing anything.</summary>
        public int Stage { get; private set; }

        /// <summary>True between kickoff and full time of a tournament match.</summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// True if the match that has just finished was a tournament round.
        ///
        /// Separate from <see cref="IsRunning"/> and NOT cleared by the result,
        /// because the result screen is what reads it: the score is settled
        /// before full time is announced, so by the time that screen opens
        /// IsRunning is already false and would say the round never happened.
        /// </summary>
        public bool LastMatchWasTournament { get; private set; }

        /// <summary>Whether that round was won. Only meaningful with the flag above.</summary>
        public bool LastMatchWon { get; private set; }

        /// <summary>
        /// True when the run is over either way — the final won, or a round
        /// lost. The difference between "next round" and "finish" on the result
        /// screen.
        /// </summary>
        public bool RunEnded { get; private set; }

        /// <summary>What the brief calls IsTournamentActive: a run with rounds still to play.</summary>
        public bool IsTournamentActive => LastMatchWasTournament && !RunEnded;

        /// <summary>
        /// How the last tournament match ended, for the result screen to read.
        /// Cleared once it has been shown, so returning to the title twice does
        /// not announce the same victory again.
        /// </summary>
        public string PendingOutcome { get; private set; }

        private void Awake()
        {
            // The generator puts one of these in the scene, and the scene is
            // reloaded by nothing in this project — but a second copy would
            // still be possible through a restart, and two of them would each
            // advance the stage on the same win.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Stage = Mathf.Clamp(SaveManager.Data.tournamentStage, 0, RoundCount - 1);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// The three rounds. A method rather than a serialised array so the
        /// table cannot be half-edited in the Inspector into a state the code
        /// does not expect — a tournament with two rounds, or one with no
        /// opponent colour.
        /// </summary>
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

                        // Gold rather than the black the brief asked for. Black
                        // is one of the strips the HUMAN side can pick, and two
                        // sides in the same colour is the one thing a football
                        // kit must never be — see NOTE below.
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

        /// <summary>
        /// The round's name in the player's language.
        ///
        /// Separate from <see cref="TournamentRound.Label"/>, which stays as it
        /// is: that one only ever reaches the console, and a log that changes
        /// language with the menu is a log nobody can search.
        /// </summary>
        public static string DescribeRound(int stage)
        {
            switch (Mathf.Clamp(stage, 0, RoundCount - 1))
            {
                case 1: return LocalizationManager.GetText("tournament.round.semis");
                case 2: return LocalizationManager.GetText("tournament.round.final");
                default: return LocalizationManager.GetText("tournament.round.quarters");
            }
        }

        /// <summary>
        /// The compact form for the match HUD, where it sits under the clock and
        /// has to stay out of the way: "TORNEO - SEMIS", not "SEMIFINAL".
        /// </summary>
        public string DescribeCurrentRoundBadge()
        {
            switch (Stage)
            {
                case 1: return LocalizationManager.GetText("tournament.hud.semis");
                case 2: return LocalizationManager.GetText("tournament.hud.final");
                default: return LocalizationManager.GetText("tournament.hud.quarters");
            }
        }

        /// <summary>
        /// Which caption the title screen's tournament button should show, as a
        /// localisation key rather than as words.
        ///
        /// A key and not a string because the button is on the main menu, which
        /// is where the language can be changed — handing back Spanish text would
        /// make this the one caption on that screen that never followed the
        /// choice.
        ///
        /// The three captions are kept to the width of the quick-match button on
        /// purpose: the two are the same size, and a caption like "CONTINUAR
        /// TORNEO — SEMIFINAL" would either wrap or shrink its own font and stop
        /// matching the button beside it. A run already under way is signalled by
        /// naming the round rather than by the word "continuar".
        /// </summary>
        public string NextRoundKey()
        {
            switch (Stage)
            {
                case 1: return "tournament.next.semis";
                case 2: return "tournament.next.final";
                default: return "tournament.next.quarters";
            }
        }

        /// <summary>
        /// Opens a tournament match. Called by the title instead of the
        /// configuration screen, which this mode deliberately skips: the whole
        /// point is that the tournament decides the terms, not the player.
        /// </summary>
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

            // The human keeps whatever strip they last chose; only the rival is
            // dictated by the round.
            MatchManager.Instance.ConfigureTournamentMatch(
                RoundDurationSeconds, round.Difficulty, round.Shape, round.RivalColor);

            Debug.Log($"[Torneo] {round.Label}: rival {round.Difficulty}, " +
                      $"{Formations.GetLabel(round.Shape)}, color {round.RivalColorName}, " +
                      $"{RoundDurationSeconds:F0} s por parte.");
        }

        /// <summary>
        /// Settles the round just played.
        ///
        /// A draw counts as elimination rather than a replay. There are no
        /// penalty shoot-outs in this game and a replay would let a player who
        /// cannot beat the final grind at it forever, which is the opposite of
        /// what three escalating rounds are for.
        /// </summary>
        public void ReportResult(int humanGoals, int rivalGoals)
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;

            bool won = humanGoals > rivalGoals;

            LastMatchWon = won;

            // A loss ends the run, and so does winning the last round. Both put
            // the counter back to zero, but only one of them is a victory — the
            // result screen tells them apart by LastMatchWon.
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

            // Names the round COMING UP rather than the one just won. "CUARTOS
            // SUPERADOS" and "SEMIFINAL SUPERADOS" cannot both be written from
            // one template — one is masculine plural and the other feminine
            // singular — and where the player goes next is the more useful half
            // of the news anyway. Both remaining rounds are feminine, so "A LA"
            // holds for each.
            PendingOutcome = LocalizationManager.Format("tournament.advance", DescribeRound(Stage));

            Debug.Log($"[Torneo] Victoria {humanGoals}-{rivalGoals} en {clearedLabel}. " +
                      $"Siguiente ronda: {CurrentRound.Label}.");
        }

        /// <summary>Read and cleared by the result screen, so a message is shown once.</summary>
        public string ConsumeOutcome()
        {
            string outcome = PendingOutcome;
            PendingOutcome = null;

            return outcome;
        }

        /// <summary>Abandons a run — for the developer menu and for leaving mid-tournament.</summary>
        public void Abandon()
        {
            IsRunning = false;
            PendingOutcome = null;

            LastMatchWasTournament = false;
            LastMatchWon = false;
            RunEnded = false;
        }

        private void SetStage(int stage)
        {
            Stage = Mathf.Clamp(stage, 0, RoundCount - 1);

            // Written through immediately: a round won is the thing a player
            // would be most annoyed to find missing next time they open the game.
            SaveManager.Data.tournamentStage = Stage;
            SaveManager.SaveNow();
        }
    }
}
