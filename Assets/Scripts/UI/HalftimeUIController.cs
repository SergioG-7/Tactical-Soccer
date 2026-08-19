using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// The interval. Comes up when the first half runs out, holds the match at
    /// timeScale 0, and offers the only two things a manager does at half time:
    /// change his team, or send it back out.
    ///
    /// This is now the ONLY way into the substitutions board. Stamina no longer
    /// comes back on its own, so a change is a decision with consequences for
    /// the rest of the match rather than something to be done on a whim while
    /// the ball is in the air — which is also why the board no longer hangs off
    /// the match HUD.
    ///
    /// Lives on the canvas, not on the panel it owns: a component on a
    /// deactivated GameObject never receives OnEnable, so a controller parked on
    /// its own hidden panel would never hear the half-time whistle it exists to
    /// answer.
    /// </summary>
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

            // Awake only runs in play mode, so this is what keeps the interval
            // screen off the pitch in the editor.
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        private void OnEnable()
        {
            TacticalEvents.OnHalftime += ShowInterval;
            TacticalEvents.OnMatchOver += HandleMatchOver;

            // The summary carries the score, so it is composed here rather than
            // being a key a LocalizedText could follow on its own — which means
            // this controller has to hear the language change itself, or the one
            // paragraph on the screen would stay in the old language.
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

        private void Start()
        {
            // Cleared first: these listeners are added from code on every load,
            // and a duplicate would start the second half twice on one press.
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

        /// <summary>
        /// Opens the team talk. The match is already frozen by the manager
        /// before this is raised, so there is no timeScale to set here — and
        /// setting it anyway would be a second owner of the freeze.
        /// </summary>
        public void ShowInterval()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(true);
            }

            WriteSummary();
        }

        /// <summary>
        /// Hands over to the substitutions board, passing this panel as the way
        /// back. The board closes into the team talk rather than into the match:
        /// making changes is not the same as being ready to restart.
        /// </summary>
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

        /// <summary>
        /// Sends the teams back out. The manager owns the clock, the half number
        /// and the thaw; this only closes the screen and asks.
        /// </summary>
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

        /// <summary>
        /// Full time can only be reached from the second half, so this screen
        /// should never be up when it arrives — but if it somehow is, it has to
        /// go, and without thawing anything.
        /// </summary>
        private void HandleMatchOver()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

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

            // Written straight rather than through LocalizedText: this one
            // carries the score, so it is not a key on its own and could not be
            // re-derived from one. Both sides of the colon are in the
            // translation, including where the two numbers fall.
            LocalizationManager.WriteFormatted(summaryText, "halftime.summary",
                ScoreManager.Instance.BlueScore, ScoreManager.Instance.RedScore);
        }
    }
}
