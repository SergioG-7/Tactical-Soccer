using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// The full-time screen: the result, what each side did to earn it, and the
    /// two ways out.
    ///
    /// The statistics are read from MatchManager rather than tracked here. This
    /// screen appears once, at the end, and a UI that had been counting shots all
    /// match would be a scoreboard that stops existing when somebody hides the
    /// panel.
    ///
    /// Lives on the canvas rather than on the panel it shows. A component on a
    /// deactivated GameObject never receives OnEnable, so a controller parked on
    /// its own hidden panel would never subscribe to the whistle it exists to
    /// listen for.
    /// </summary>
    public class MatchOverUIController : MonoBehaviour
    {
        public GameObject uiPanel;
        public Text resultText;

        [Tooltip("The comparison table. One text block, monospaced by padding " +
                 "rather than by font: three rows do not justify a grid of nine " +
                 "separate labels to keep in step.")]
        public Text statsText;

        public Button restartButton;

        [Tooltip("Back to the title screen, for changing the match settings. " +
                 "Optional: a scene generated before this existed still restarts.")]
        public Button menuButton;

        private void Awake()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        private void OnEnable()
        {
            TacticalEvents.OnMatchOver += HandleMatchOver;
        }

        private void OnDisable()
        {
            TacticalEvents.OnMatchOver -= HandleMatchOver;
        }

        private void Start()
        {
            // Cleared first: the listeners are added from code on every load, and
            // a duplicate would restart the match twice on one click.
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

        /// <summary>
        /// Result is stated from the human side — Blue. "Defeat" is clearer than
        /// "Red wins" for the person holding the phone.
        /// </summary>
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

        /// <summary>
        /// Relabels the two buttons when the match just played was a tournament
        /// round.
        ///
        /// A round is not a friendly, so "play again" is the wrong offer: after
        /// a win there is a NEXT match waiting, and after a defeat or a won
        /// final there is a run to close out. Only the captions and the actions
        /// change — the panel is the same one, because the result and the
        /// statistics mean exactly what they always did.
        /// </summary>
        private void RefreshTournamentButtons()
        {
            TournamentManager tournament = TournamentManager.Instance;

            bool inTournament = tournament != null && tournament.LastMatchWasTournament;

            // Advancing means: it was a round, it was won, and there is another
            // one to play. Winning the FINAL is a win with nothing left after it.
            bool advancing = inTournament && tournament.LastMatchWon && !tournament.RunEnded;

            SetButton(restartButton,
                inTournament
                    ? (advancing ? "matchover.nextRound" : "matchover.finishTournament")
                    : "matchover.playAgain",
                inTournament ? (advancing ? (System.Action)ContinueTournament : ReturnToTitle) : RestartMatch);

            // With the run over, "finish" IS the way back to the menu, so a
            // second button saying the same thing is noise.
            if (menuButton != null)
            {
                menuButton.gameObject.SetActive(!inTournament || advancing);
            }
        }

        /// <summary>
        /// Relabels a button and points it at what it now does. The caption
        /// arrives as a localisation key, not as words: which of the three
        /// captions this button carries depends on the tournament, and what
        /// those captions SAY depends on the language.
        /// </summary>
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

        /// <summary>
        /// Straight into the next round, without going back to the title.
        ///
        /// The order matters and none of the three steps is optional:
        ///
        ///  1. BeginRound FIRST, because it writes the new round's length —
        ///     and RestartMatch re-seeds the clock from that length, so doing
        ///     it the other way round starts the semi-final on the quarter's
        ///     clock.
        ///  2. RestartMatch, which puts the squads, the stamina, the score and
        ///     the momentum back to nothing. A round played on the last round's
        ///     blown legs would not be a fresh match.
        ///  3. StartInitialKickoff, which is the only thing that lines the
        ///     opposition up in its new shape and repaints it in the new
        ///     round's colour. RestartMatch alone would kick off with the
        ///     previous round's team still on the pitch, in the previous
        ///     round's strip.
        /// </summary>
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

        /// <summary>
        /// Builds the comparison table. Blue on the left because it is the human
        /// side and the result above is already written from that point of view —
        /// the eye should not have to switch sides halfway down the panel.
        /// </summary>
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

            // Measured before the first row is written: every row has to be
            // padded to the same column, and that column is as wide as the
            // longest heading in the language in force.
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

        /// <summary>
        /// A column heading in the player's language. Named Label rather than
        /// Text because a method called Text in a file full of UnityEngine.UI
        /// Text fields is a name collision waiting to happen.
        /// </summary>
        private static string Label(string key)
        {
            return Core.LocalizationManager.GetText(key);
        }

        /// <summary>
        /// One row of the table, padded into three fixed-width columns.
        ///
        /// Padding rather than a layout group: the numbers here are one or two
        /// digits and the labels are known at compile time, so a grid of nine
        /// RectTransforms would be a lot of machinery to line up five rows of
        /// short text.
        /// </summary>
        /// <summary>
        /// One line of the comparison table: our number, what it counts, theirs.
        ///
        /// The middle column is CENTRED inside a width measured from the actual
        /// headings, rather than left-aligned in a fixed eight characters. Eight
        /// was tuned against "GOLES" and "FALTAS"; the moment the labels became
        /// translatable the column stopped fitting them and the whole table
        /// drifted off centre.
        /// </summary>
        private static string Row(string left, string middle, string right)
        {
            int padding = System.Math.Max(0, middleColumnWidth - VisualWidth(middle));
            int before = padding / 2;

            string centred = new string(' ', before) + middle + new string(' ', padding - before);

            return $"{left,6}   {centred}   {right,-6}";
        }

        // Width of the widest heading in the language in force, in monospaced
        // cells. Written once per table, read by every Row.
        private static int middleColumnWidth = MinimumMiddleWidth;

        private const int MinimumMiddleWidth = 8;

        /// <summary>
        /// How many monospaced cells a string occupies.
        ///
        /// A kanji or a kana is drawn twice as wide as a Latin letter, so
        /// counting characters would under-measure every Japanese heading by
        /// half and the padding would come out short.
        /// </summary>
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

        /// <summary>
        /// Picks the font the table is drawn in.
        ///
        /// Monospaced whenever the language can be drawn with one, because the
        /// columns are aligned with spaces and a proportional face makes "11"
        /// and "8" different widths. Japanese cannot: Consolas and its kin have
        /// no kana at all and would draw the table as blank space, so there the
        /// language's own font wins and the columns are merely ragged instead of
        /// invisible.
        /// </summary>
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

        // The monospaced face the generator gave this screen, kept so the table
        // can go back to it when the language no longer needs its own.
        private Font tableFont;

        /// <summary>
        /// The panel is dismissed before anything else runs: MatchManager
        /// restores timeScale, and leaving the results screen up over a live
        /// pitch would swallow every tap behind it.
        ///
        /// It fades rather than vanishing, and that is still safe: the fade
        /// stops blocking raycasts on the first frame of the close, so the two
        /// tenths of a second it is still visible cannot eat an input.
        /// </summary>
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

        /// <summary>
        /// Back to the title, for a match with different settings.
        ///
        /// The pitch is reset through exactly the same path as "play again" —
        /// squads restored, score wiped, clock re-seeded — and only THEN handed
        /// to the title screen, which freezes it again. Doing it the other way
        /// round would leave the reset thawing a match that is supposed to be
        /// sitting behind a menu.
        /// </summary>
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
