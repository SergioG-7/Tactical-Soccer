using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// Developer menu: the levers needed to reach a state that would otherwise
    /// take a full match to arrive at.
    ///
    /// Every button here reaches a state the game can genuinely be in, through
    /// the same public methods the game itself uses — a full tension bar, full
    /// tanks, the end of a half. None of them invents a state that normal play
    /// cannot produce, because a cheat that does is a cheat that finds bugs
    /// nobody can hit.
    ///
    /// Opened by tapping a deliberately invisible corner of the screen three
    /// times in quick succession. Hidden rather than absent because it has to be
    /// reachable on a touch device, where there is no console and no key to bind
    /// — and hidden rather than obvious because it should not be found by
    /// accident mid-match.
    ///
    /// Lives on the canvas rather than on the panel it owns. A component on a
    /// deactivated GameObject never receives Start, so a controller parked on its
    /// own hidden panel would never wire up the buttons that close it.
    /// </summary>
    public class DebugMenuUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Tooltip("The invisible corner that opens this. An Image with alpha 0 " +
                 "still receives raycasts, which is the whole trick — it is a " +
                 "button nobody can see but anybody can press.")]
        public Button openTrigger;

        [Header("Acciones")]
        public Button maxTensionButton;
        public Button healStaminaButton;
        public Button endHalfButton;

        [Tooltip("Opens the same audio options panel the title screen uses, so " +
                 "the mix can be adjusted mid-match instead of only before one.")]
        public Button audioOptionsButton;

        [Tooltip("Throws away every saved player edit. The only way back to the " +
                 "squad as generated, now that edits survive closing the game.")]
        public Button resetSquadButton;

        public Button closeButton;

        [Tooltip("Reads back what each action did, so a press that was refused " +
                 "does not look the same as one that worked.")]
        public Text feedbackText;

        [Header("Activación")]
        [Tooltip("Taps needed to open the menu.")]
        [SerializeField] private int tapsToOpen = 3;

        [Tooltip("Seconds allowed between taps. Short enough that three " +
                 "deliberate taps open it and three ordinary taps over a match " +
                 "never do.")]
        [SerializeField] private float tapWindowSeconds = 1.2f;

        private const float FrozenTimeScale = 0f;
        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        private int tapCount;
        private float lastTapTime = -99f;

        /// <summary>
        /// The timeScale the match was running at when the menu opened, so
        /// closing it restores what was there rather than assuming 1.
        ///
        /// It matters: the menu can be opened during a duel or behind the
        /// interval, both of which are legitimately frozen and must stay frozen
        /// when it closes.
        /// </summary>
        private float restoreTimeScale = NormalTimeScale;

        public static DebugMenuUIController Instance { get; private set; }

        /// <summary>True while the menu is up. Consulted by the input layer.</summary>
        public static bool IsOpen { get; private set; }

        private void Awake()
        {
            Instance = this;
            IsOpen = false;

            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            IsOpen = false;
        }

        /// <summary>
        /// Whether the hidden corner is live at all.
        ///
        /// Only during a real passage of play. Everything this menu offers acts
        /// on a match in progress — filling a tension bar, healing tanks, ending
        /// a half — so outside one it has nothing to do, and the corner is doing
        /// nothing but stealing taps.
        ///
        /// And it really was stealing them: the trigger is a 180-square anchored
        /// into the top-left corner and it is a LATER sibling than every setup
        /// screen, so it sat on top of the back button on the settings and team
        /// sheet screens. Taps on the left of that button went to this instead —
        /// which is why "back" seemed to work only sometimes, and why mashing it
        /// opened the developer menu.
        ///
        /// isMatchStarted alone is not enough: it stays true after a match, so a
        /// player back at the title would still count as playing. The screens are
        /// asked directly.
        /// </summary>
        private static bool IsReachable => MatchManager.Instance != null
            && MatchManager.IsStarted
            && MatchManager.IsPlayable
            && !TitleScreenUIController.IsOpen
            && !MatchConfigUIController.IsOpen
            && !FormationUIController.IsOpen;

        /// <summary>
        /// Switches the corner's raycast on and off with the match state.
        ///
        /// raycastTarget rather than deactivating the object: an inactive
        /// GameObject would take its Button with it, and the listener is wired
        /// once in Start. This is also the cheapest possible per-frame check —
        /// a handful of bools and a field write only when it changes.
        /// </summary>
        private void Update()
        {
            if (openTrigger == null || openTrigger.targetGraphic == null)
            {
                return;
            }

            bool reachable = IsReachable;

            if (openTrigger.targetGraphic.raycastTarget != reachable)
            {
                openTrigger.targetGraphic.raycastTarget = reachable;
            }
        }

        private void Start()
        {
            // Cleared first: these listeners are added from code on every load,
            // and a duplicate would fire each action twice on one press.
            Bind(openTrigger, RegisterTap);
            Bind(maxTensionButton, MaxTension);
            Bind(healStaminaButton, HealStamina);
            Bind(endHalfButton, ForceEndOfHalf);
            Bind(audioOptionsButton, OpenAudioOptions);
            Bind(resetSquadButton, ResetSquad);
            Bind(closeButton, Close);
        }

        /// <summary>
        /// Hands over to the shared audio options panel. The developer menu
        /// stays open behind it, so closing the options lands back here rather
        /// than on the pitch.
        /// </summary>
        public void OpenAudioOptions()
        {
            if (AudioSettingsUI.Instance != null)
            {
                AudioSettingsUI.Instance.ShowMenu();
                return;
            }

            Debug.LogWarning("No hay panel de opciones de audio en la escena.");
        }

        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        /// <summary>
        /// Counts taps on the hidden corner and opens the menu on the third
        /// within the window.
        ///
        /// Unscaled time, because the corner is pressable while the match is
        /// frozen — behind the interval or a duel — and Time.time does not
        /// advance there, which would make every tap look simultaneous.
        /// </summary>
        private void RegisterTap()
        {
            // Belt to the braces of the raycast being switched off below. The
            // trigger should not even be receiving this outside a match, but a
            // refused tap must not quietly accumulate either — otherwise three
            // taps spread across a menu and a kickoff would still open it.
            if (!IsReachable)
            {
                tapCount = 0;
                return;
            }

            float now = Time.unscaledTime;

            tapCount = now - lastTapTime <= tapWindowSeconds ? tapCount + 1 : 1;
            lastTapTime = now;

            if (tapCount < tapsToOpen)
            {
                return;
            }

            tapCount = 0;
            Open();
        }

        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            restoreTimeScale = Time.timeScale;

            if (uiPanel != null)
            {
                uiPanel.SetActive(true);
            }

            Time.timeScale = FrozenTimeScale;
            IsOpen = true;

            Report(Core.LocalizationManager.GetText("debug.opened"));
        }

        public void Close()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            IsOpen = false;

            // Back to whatever was running before, not blindly to 1: opening this
            // over a duel or the interval must not thaw them on the way out.
            Time.timeScale = restoreTimeScale;

            if (restoreTimeScale > 0f)
            {
                Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;
            }
        }

        private void MaxTension()
        {
            if (TensionManager.Instance == null)
            {
                Report(Core.LocalizationManager.GetText("debug.noTensionManager"));
                return;
            }

            TeamId team = MatchManager.Instance != null ? MatchManager.Instance.HumanTeam : TeamId.Blue;

            // Through the ordinary charge path, so it ignites exactly as it would
            // in a match — including everything that happens on ignition.
            TensionManager.Instance.Add(team, TensionManager.Instance.MaxTension);

            Report(Core.LocalizationManager.Format(
                TensionManager.Instance.IsBurning(team) ? "debug.tensionIgnited" : "debug.tensionAlready",
                Fouls.DescribeTeam(team)));
        }

        private void HealStamina()
        {
            TeamId team = MatchManager.Instance != null ? MatchManager.Instance.HumanTeam : TeamId.Blue;

            int healed = 0;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team || !member.isStarter)
                {
                    continue;
                }

                member.RefillStamina();
                healed++;
            }

            Report(Core.LocalizationManager.Format("debug.staminaHealed",
                healed, Fouls.DescribeTeam(team)));
        }

        private void ForceEndOfHalf()
        {
            if (MatchManager.Instance == null)
            {
                Report(Core.LocalizationManager.GetText("debug.noMatchManager"));
                return;
            }

            // The half ends on a timer the menu is currently holding at zero, so
            // the freeze has to be lifted for the closing routine to run at all.
            Close();

            MatchManager.Instance.ForceEndOfHalf();
        }

        /// <summary>
        /// Empties the saved squad edits.
        ///
        /// Says out loud that it does NOT undo them on the players standing on
        /// the pitch: their numbers were written onto the TeamMember when the
        /// edit was made and the original values are the stat assets', not
        /// something this has kept a copy of. What it guarantees is that the
        /// next load starts from the squad the generator makes.
        /// </summary>
        private void ResetSquad()
        {
            int cleared = Core.SaveManager.Data.squad.Count;

            Core.SaveManager.ClearSquad();

            Report(cleared > 0
                ? Core.LocalizationManager.Format("debug.squadCleared", cleared)
                : Core.LocalizationManager.GetText("debug.squadEmpty"));
        }

        private void Report(string message)
        {
            Debug.Log($"[Debug] {message}");

            if (feedbackText != null)
            {
                feedbackText.text = message;
            }
        }
    }
}
