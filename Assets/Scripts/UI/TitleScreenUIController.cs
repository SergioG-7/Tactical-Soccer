using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// The front door. Holds the match frozen until the player asks for it, so
    /// the game opens on a title instead of on a kickoff already in progress.
    ///
    /// It no longer starts the match itself: pressing Play hands over to the
    /// formation menu, which is what eventually kicks off. The pitch stays
    /// frozen across both screens — there is no moment between them where the
    /// match is quietly running behind a menu.
    ///
    /// Lives on the canvas rather than on the panel it shows. A component on a
    /// deactivated GameObject never receives Start, so a controller parked on
    /// its own panel would never wire up the button that hides it.
    /// </summary>
    public class TitleScreenUIController : MonoBehaviour
    {
        public GameObject uiPanel;
        public Button playButton;

        [Tooltip("Starts the tournament, or continues one already under way. " +
                 "Skips the settings screen: the round dictates the terms.")]
        public Button tournamentButton;

        [Tooltip("Caption on the tournament button, rewritten every time the " +
                 "title is shown so it names the round actually coming up.")]
        public Text tournamentLabel;

        [Tooltip("Result of the last tournament match, shown under the title on " +
                 "the way back from one.")]
        public Text tournamentOutcomeText;

        [Tooltip("Opens the audio options. Optional: the title works without one, " +
                 "the levels simply keep whatever they were last set to.")]
        public Button optionsButton;

        [Tooltip("The match settings screen this title opens. Optional: without " +
                 "one the title falls through to the team sheet, and without " +
                 "that to the kickoff — a title with no exit is the one failure " +
                 "this screen must never have.")]
        public MatchConfigUIController configMenu;

        [Tooltip("The team sheet. Reached through the settings screen when there " +
                 "is one, and directly when there is not.")]
        public FormationUIController formationMenu;

        private const float FrozenTimeScale = 0f;
        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        public static TitleScreenUIController Instance { get; private set; }

        /// <summary>
        /// True while the title is up.
        ///
        /// Read off the panel rather than tracked in a bool, so it cannot drift
        /// from what is actually on screen. Needed because the obvious test —
        /// "has the match started?" — is wrong here: isMatchStarted stays true
        /// after a match, so a player who finished one and came back to the menu
        /// still looks mid-match to anything asking.
        /// </summary>
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

        /// <summary>
        /// Brings the title back, for a player who has finished a match and wants
        /// different settings rather than the same one again.
        ///
        /// Freezing is this screen's job on the way in as well as at startup: the
        /// caller has just reset the match, which thaws the pitch, and without
        /// this the game would be running underneath the title.
        /// </summary>
        public void ShowTitle()
        {
            UIAnimator.Show(uiPanel);

            Time.timeScale = FrozenTimeScale;

            RefreshTournament();
        }

        /// <summary>
        /// Rewrites the tournament button for the round actually coming up, and
        /// reports how the last one went.
        ///
        /// Done every time the title is shown rather than once in Start: the
        /// player arrives back here having just won or lost a round, and a
        /// caption written before the match would still be advertising it.
        /// </summary>
        private void RefreshTournament()
        {
            TournamentManager tournament = TournamentManager.Instance;

            if (tournamentLabel != null)
            {
                // Through LocalizedText rather than straight onto the Text: the
                // caption changes with the round AND with the language, and this
                // leaves it able to follow both. The key is stored on the label,
                // so a language switched from the options panel over this very
                // screen rewrites it without coming back through here.
                LocalizedText.Write(tournamentLabel, tournament != null
                    ? tournament.NextRoundKey()
                    : "tournament.next.quarters");
            }

            if (tournamentOutcomeText == null)
            {
                return;
            }

            // Consumed, not merely read: coming back to the title a second time
            // must not announce the same victory again.
            string outcome = tournament != null ? tournament.ConsumeOutcome() : null;

            tournamentOutcomeText.text = outcome ?? string.Empty;
        }

        private void Start()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(true);
            }

            // Freezing alone is not enough to hold the match: input is not
            // governed by timeScale. MatchManager stays un-started as well, and
            // that is what actually keeps the AI, the drift and the input quiet.
            Time.timeScale = FrozenTimeScale;

            if (playButton == null)
            {
                Debug.LogError("TitleScreenUIController no tiene botón: no habría forma de empezar.");
                return;
            }

            // Cleared first: the listener is added from code on every load, and
            // a duplicate would kick the match off twice on one click.
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

        /// <summary>
        /// Starts or continues a tournament round.
        ///
        /// Goes straight to the team sheet, skipping the settings screen: the
        /// round has already dictated the difficulty, the opposition's shape,
        /// its colour and the length of a half. Picking a formation and a
        /// captain is still the player's, which is why the team sheet stays.
        /// </summary>
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

            // No team sheet in this scene, so kick off directly rather than
            // stranding the player on a title that has just hidden itself.
            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.StartInitialKickoff();
            }
        }

        /// <summary>
        /// Opens the audio options over the title, without hiding it: the
        /// options are a detour, not a step in the flow, and the player has to
        /// land back on the title when they close them.
        /// </summary>
        public void OpenOptions()
        {
            if (AudioSettingsUI.Instance != null)
            {
                AudioSettingsUI.Instance.ShowMenu();
                return;
            }

            Debug.LogWarning("No hay panel de opciones de audio en la escena.");
        }

        public void StartGame()
        {
            // A quick match is not a round. Said out loud rather than relied on:
            // without this, a player who opened a tournament round and backed out
            // to play a friendly would have the friendly's result counted against
            // their run.
            if (TournamentManager.Instance != null)
            {
                TournamentManager.Instance.Abandon();
            }

            UIAnimator.Hide(uiPanel);

            // Deliberately NOT restoring timeScale on either handover: the next
            // screen is still a menu, and thawing between them would run the
            // pitch behind it. The team sheet unfreezes when it starts play.
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

            // No team sheet in this scene, so this screen is the last one and
            // has to kick off itself.
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
