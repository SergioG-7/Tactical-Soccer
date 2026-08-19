using UnityEngine;
using UnityEngine.UI;

namespace TacticalSoccer.Core
{
    /// <summary>
    /// Keeps the match score and paints the scoreboard. Listens to the event bus
    /// for goals, so it never needs a reference to the goals, the ball or the
    /// players; the clock is the one thing it reads directly, because a
    /// per-second event would be noise on the bus for a value the UI polls anyway.
    /// </summary>
    public class ScoreManager : MonoBehaviour
    {
        public const int BlueTeamId = 0;
        public const int RedTeamId = 1;

        [Tooltip("Scoreboard label. Optional: the score is tracked either way.")]
        public Text scoreText;

        [Tooltip("Countdown label. Optional: the clock runs either way.")]
        public Text timerText;

        [Tooltip("Which round of the tournament this is, under the clock. Hidden " +
                 "outside a tournament — in a quick match there is no round to " +
                 "name, and an empty label is a gap the player has to learn to " +
                 "ignore.")]
        public Text tournamentText;

        private int blueScore;
        private int redScore;

        // Only repainted when the displayed second actually changes, so the text
        // mesh is not rebuilt on every single frame.
        private int lastPaintedSecond = -1;

        public static ScoreManager Instance { get; private set; }

        public int BlueScore => blueScore;
        public int RedScore => redScore;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // Paints 0 - 0 rather than trusting whatever the prefab was authored
            // with, so the board can never open a match showing a stale score.
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

        private void Update()
        {
            RefreshTournamentBadge();

            if (timerText == null || MatchManager.Instance == null)
            {
                return;
            }

            // Ceil, not floor: with 0.4 s left the board should still read 1,
            // and it must only show 0 when the match is genuinely over.
            int secondsLeft = Mathf.CeilToInt(MatchManager.Instance.currentTime);

            if (secondsLeft == lastPaintedSecond)
            {
                return;
            }

            lastPaintedSecond = secondsLeft;

            // The half is on the board because the clock restarts at 45 after
            // the interval: without it the second half looks like the first one
            // having been reset by something.
            // Repainted every frame, so it needs no language subscription of
            // its own: the very next frame is already in the new language.
            LocalizationManager.WriteFormatted(timerText, "hud.timer",
                MatchManager.Instance.currentHalf, secondsLeft);
        }

        /// <summary>
        /// Names the round while a tournament match is being played, and hides
        /// itself the rest of the time.
        ///
        /// Driven off IsRunning rather than off the result flags the full-time
        /// screen uses: this label is about the match in progress, and the
        /// moment it ends the badge should go — the result screen is what
        /// carries the news from there.
        ///
        /// Repainted only when the caption actually changes, like the clock
        /// above: this runs every frame and rebuilding a text mesh for an
        /// unchanged string is pure waste.
        /// </summary>
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

        /// <summary>
        /// Wipes the score back to nil. Called when a finished match is played
        /// again, so the new one does not open carrying the last one's goals.
        /// </summary>
        public void ResetScores()
        {
            blueScore = 0;
            redScore = 0;

            RefreshScoreboard();
        }

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

        private void RefreshScoreboard()
        {
            if (scoreText != null)
            {
                scoreText.text = $"{blueScore} - {redScore}";
            }
        }
    }
}
