using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// The penalty: two buttons, one guess each, no statistics.
    ///
    /// Stats are deliberately not consulted. Every other contest in this game is
    /// a stat check with a d20 on top, and a penalty that worked the same way
    /// would be one more duel with a different backdrop. Making it a pure guess
    /// is what makes it feel like a penalty — the best striker in the match and
    /// the worst have exactly the same chance, and the only thing that decides it
    /// is whether you read the other side right.
    ///
    /// The human is the striker when their side won the penalty and the keeper
    /// when it was given against them, and both roles press the same two buttons:
    /// matching sides is a save, differing sides is a goal, whichever end the
    /// player happens to be standing at.
    ///
    /// Lives on the canvas rather than on the panel it owns. A component on a
    /// deactivated GameObject never receives Start, so a controller parked on its
    /// own hidden panel would never wire up the buttons that dismiss it.
    /// </summary>
    public class PenaltyUIController : MonoBehaviour
    {
        public GameObject uiPanel;
        public Text headingText;
        public Text resultText;
        public Button leftButton;
        public Button rightButton;

        [Tooltip("Beat between the player's choice being lit and the opposition's " +
                 "guess being revealed. This is the entire suspense of the " +
                 "mechanic, so it is not free to shorten.")]
        [SerializeField] private float suspenseSeconds = 1.5f;

        // The old "revealSeconds" beat is gone: which way the keeper went used to
        // be a line of text held for a second, and is now something the player
        // watches him do while the ball is in the air.

        [Tooltip("How long the outcome is left on screen before play resumes. " +
                 "Long enough to read which way both of them went.")]
        [SerializeField] private float resultDwellSeconds = 1.5f;

        [Header("Colores")]
        [Tooltip("Fill of the side the player picked, so the tap is acknowledged " +
                 "before anything else happens.")]
        [SerializeField] private Color chosenSideColor = new Color(1f, 0.85f, 0.25f, 1f);

        [Tooltip("Fill of the side the player did not pick.")]
        [SerializeField] private Color unchosenSideColor = new Color(0.45f, 0.45f, 0.48f, 1f);

        [Tooltip("Fill of both sides before a choice is made.")]
        [SerializeField] private Color idleSideColor = new Color(0.88f, 0.88f, 0.88f, 1f);

        [Header("Vuelo del balón")]
        [Tooltip("How far towards the post the ball is struck, as a share of the " +
                 "goal's half width. Under 1 so it finishes inside the frame " +
                 "rather than clipping the post.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float shotWidthShare = 0.7f;

        [Tooltip("Peak height of the struck ball's arc.")]
        [SerializeField] private float shotArcHeight = 1.6f;

        [SerializeField] private Color suspenseColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        [SerializeField] private Color savedColor = new Color(1f, 0.55f, 0.2f, 1f);
        [SerializeField] private Color scoredColor = new Color(0.3f, 1f, 0.4f, 1f);

        private const float FrozenTimeScale = 0f;
        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        private TeamId attackingTeam;
        private bool humanIsStriker;
        private bool isResolving;

        public static PenaltyUIController Instance { get; private set; }

        /// <summary>True while the penalty menu is up and the match is frozen.</summary>
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

        private void Start()
        {
            // Cleared first: these listeners are added from code on every load,
            // and a duplicate would take the same penalty twice on one press.
            Bind(leftButton, PenaltySide.Left);
            Bind(rightButton, PenaltySide.Right);
        }

        private enum PenaltySide
        {
            Left,
            Right
        }

        /// <summary>
        /// Opens the menu and freezes the match. Called by MatchManager when a
        /// foul lands inside the box.
        /// </summary>
        public void ShowPenalty(TeamId team)
        {
            attackingTeam = team;

            // Whose penalty it is decides which end of it the player takes. Read
            // from the match rather than assumed, so this still reads correctly
            // if the human is ever put in charge of the other side.
            TeamId humanTeam = MatchManager.Instance != null
                ? MatchManager.Instance.HumanTeam
                : TeamId.Blue;

            humanIsStriker = team == humanTeam;
            isResolving = false;

            if (headingText != null)
            {
                Core.LocalizationManager.Write(headingText,
                    humanIsStriker ? "penalty.for" : "penalty.against");
            }

            if (resultText != null)
            {
                resultText.text = string.Empty;
            }

            SetButtonsInteractable(true);

            // Undo the last penalty's highlight, transitions included. Without
            // this the second penalty of a match opens with one side already lit
            // gold — telling the player which way they went last time, as though
            // it were a recommendation.
            ResetSides();

            if (uiPanel != null)
            {
                uiPanel.SetActive(true);
            }

            Time.timeScale = FrozenTimeScale;

            IsOpen = true;
        }

        private void Bind(Button button, PenaltySide side)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Choose(side));
        }

        /// <summary>
        /// Takes the penalty. The opposition's guess is rolled here, at the
        /// moment of the press, rather than when the menu opened — not because it
        /// changes the odds, which are the same either way, but because a side
        /// decided in advance is a side that could be read out of the scene.
        /// </summary>
        private void Choose(PenaltySide humanSide)
        {
            if (isResolving)
            {
                return;
            }

            isResolving = true;
            SetButtonsInteractable(false);

            PenaltySide aiSide = Random.value < 0.5f ? PenaltySide.Left : PenaltySide.Right;

            // Same rule from both ends: the keeper guessing the striker's side
            // and the striker being guessed are the same event.
            bool saved = humanSide == aiSide;

            Debug.Log($"[Penalti] {(humanIsStriker ? "Tirador humano" : "Portero humano")} elige {Describe(humanSide)}, " +
                      $"la IA elige {Describe(aiSide)} -> {(saved ? "PARADA" : "GOL")}.");

            StartCoroutine(ResolvePenaltyRoutine(humanSide, aiSide, saved));
        }

        private static string Describe(PenaltySide side)
        {
            return Core.LocalizationManager.GetText(
                side == PenaltySide.Left ? "penalty.left" : "penalty.right");
        }

        /// <summary>
        /// Walks the ball from the spot to the side of the goal it was struck
        /// towards, over the suspense beat.
        ///
        /// Driven by hand rather than with an impulse. The match is frozen at
        /// timeScale 0 for the whole menu, so the physics step is not running and
        /// a kicked ball would simply sit there — the same reason every other
        /// effect in this game that plays during a freeze animates its own
        /// transform in unscaled time.
        ///
        /// The arc is a sine, not a straight line: a ball that slides flat along
        /// the grass into the net reads as a bug, and the lift is what makes it
        /// read as struck.
        /// </summary>
        private System.Collections.IEnumerator FlyBall(PenaltySide shotSide, PenaltySide diveSide)
        {
            BallController ball = BallController.Instance;
            MatchManager match = MatchManager.Instance;

            if (ball == null || match == null)
            {
                yield return new WaitForSecondsRealtime(suspenseSeconds);
                yield break;
            }

            Vector3 from = match.PenaltySpot;
            Vector3 goal = match.PenaltyGoalCentre;

            // Just inside the post on the chosen side, and a little past the line
            // so the ball finishes in the net rather than on it.
            float aimX = SidePost(shotSide);
            float overshoot = Mathf.Sign(goal.z) * 0.8f;

            Vector3 to = new Vector3(aimX, from.y, goal.z + overshoot);

            // The keeper dives at the same time rather than after. Both are
            // driven from one loop for exactly that reason: two coroutines would
            // be two clocks, and the only thing this beat has to communicate is
            // whether the two of them arrive at the same place.
            Transform keeper = match.PenaltyKeeper;
            Vector3 keeperFrom = keeper != null ? keeper.position : Vector3.zero;
            Vector3 keeperTo = keeper != null
                ? new Vector3(SidePost(diveSide), keeperFrom.y, keeperFrom.z)
                : Vector3.zero;

            ball.Release();

            float elapsed = 0f;

            while (elapsed < suspenseSeconds)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01(elapsed / suspenseSeconds);

                Vector3 flat = Vector3.Lerp(from, to, t);
                flat.y = from.y + (Mathf.Sin(t * Mathf.PI) * shotArcHeight);

                ball.transform.position = flat;

                if (keeper != null)
                {
                    // Eased, and faster than the ball early on: a keeper who
                    // travelled linearly alongside the ball would look like he
                    // was escorting it rather than reacting to it. Squared
                    // easing gets him committed to a side well before it
                    // arrives, which is what makes a wrong guess look wrong.
                    float dive = Mathf.Clamp01(t * KeeperDiveLead);

                    keeper.position = Vector3.Lerp(keeperFrom, keeperTo,
                        Mathf.SmoothStep(0f, 1f, dive));
                }

                yield return null;
            }

            ball.transform.position = to;

            if (keeper != null)
            {
                keeper.position = keeperTo;
            }
        }

        /// <summary>
        /// Where a side is, in world X: just inside the post. Shared by the ball
        /// and the keeper so that "same side" genuinely means the two of them
        /// finish in the same place, and a save reads as a save.
        /// </summary>
        private float SidePost(PenaltySide side)
        {
            float sideSign = side == PenaltySide.Left ? -1f : 1f;

            return sideSign * PitchBounds.GoalMouthHalfWidth * shotWidthShare;
        }

        // How much of the flight the keeper's dive is compressed into. Above 1,
        // so he is committed before the ball gets there.
        private const float KeeperDiveLead = 1.6f;

        /// <summary>
        /// Fades the whole panel out for the kick and back in for the verdict.
        ///
        /// A CanvasGroup rather than deactivating the panel: the coroutine
        /// driving all this lives on the CANVAS, not on the panel, but the
        /// buttons and the text do live on the panel, and turning the object off
        /// and on again mid-routine is a good way to lose a reference. Alpha
        /// costs nothing and cannot break anything.
        /// </summary>
        private void SetPanelVisible(bool visible, bool buttonsVisible = true)
        {
            if (uiPanel == null)
            {
                return;
            }

            if (!uiPanel.TryGetComponent(out CanvasGroup group))
            {
                group = uiPanel.AddComponent<CanvasGroup>();
            }

            group.alpha = visible ? 1f : 0f;
            group.blocksRaycasts = visible;

            if (leftButton != null)
            {
                leftButton.gameObject.SetActive(buttonsVisible);
            }

            if (rightButton != null)
            {
                rightButton.gameObject.SetActive(buttonsVisible);
            }

            if (headingText != null)
            {
                headingText.gameObject.SetActive(buttonsVisible);
            }
        }

        [Tooltip("How long the player's own pick is lit before the kick, so the " +
                 "tap is acknowledged before the screen clears for the action.")]
        [SerializeField] private float choiceAcknowledgeSeconds = 0.6f;

        /// <summary>
        /// Plays the penalty out beat by beat instead of stamping the answer on
        /// screen the instant the button is released.
        ///
        /// The order matters. First the player's own choice is lit, so the tap is
        /// acknowledged; then the opposition's guess is revealed and held, which
        /// is the only moment of suspense the mechanic has; only then the result.
        /// Showing the outcome at the same time as the guess throws that away —
        /// the player reads the verdict and never looks at the guess.
        ///
        /// Every wait is realtime: the match is frozen at timeScale 0 behind this
        /// panel, and a scaled wait would sit here forever.
        /// </summary>
        private System.Collections.IEnumerator ResolvePenaltyRoutine(
            PenaltySide humanSide, PenaltySide aiSide, bool saved)
        {
            HighlightChoice(humanSide);

            if (resultText != null)
            {
                resultText.color = suspenseColor;
                Core.LocalizationManager.WriteFormatted(resultText,
                    humanIsStriker ? "penalty.striking" : "penalty.diving",
                    Describe(humanSide));
            }

            // Held just long enough for the player to see their own side light
            // up. Without it the panel would vanish on the same frame as the
            // tap and the choice would never be acknowledged.
            yield return new WaitForSecondsRealtime(choiceAcknowledgeSeconds);

            // Which way the BALL goes is the striker's choice, whoever made it:
            // when the human is in goal, the side they picked is where they dived
            // and the AI's is where the ball was struck. The dive is the mirror
            // of that — whichever of the two is the keeper.
            PenaltySide shotSide = humanIsStriker ? humanSide : aiSide;
            PenaltySide diveSide = humanIsStriker ? aiSide : humanSide;

            // Out of the way BEFORE anything moves. The kick is the only part of
            // this the player actually wants to watch, and a bank of buttons and
            // two lines of commentary across the middle of the screen is exactly
            // where the goal is.
            SetPanelVisible(false);

            yield return FlyBall(shotSide, diveSide);

            // Back, with the verdict alone: the buttons stay hidden, because the
            // decision has already been taken and re-showing them invites a tap
            // that would do nothing.
            if (resultText != null)
            {
                Core.LocalizationManager.Write(resultText, saved ? "penalty.saved" : "penalty.goal");
                resultText.color = saved ? savedColor : scoredColor;
            }

            SetPanelVisible(true, buttonsVisible: false);

            yield return new WaitForSecondsRealtime(resultDwellSeconds);

            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            // Handed back ready for the next penalty, which may be a long time
            // away and will open through ShowPenalty expecting a whole panel.
            SetPanelVisible(true);

            IsOpen = false;

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            if (!saved)
            {
                ScoreGoal();
            }

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.EndPenalty(attackingTeam, !saved);
            }
        }

        /// <summary>
        /// Puts the ball in the net and lets the normal goal machinery take over:
        /// the same event the goal trigger raises, and the same celebration.
        ///
        /// The ball is moved first so the celebration has something to show. It
        /// is the whole point of holding the restart back — a goal announced over
        /// an empty six-yard box would look like a bug.
        /// </summary>
        private void ScoreGoal()
        {
            // The ball is already in the net: FlyBall put it there, past the line
            // and on the side it was struck towards. Moving it again here would
            // snap it to the middle of the goal after the player has just watched
            // it go into a corner.
            TacticalEvents.OnGoalScored?.Invoke(
                attackingTeam == TeamId.Red ? ScoreManager.RedTeamId : ScoreManager.BlueTeamId);

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.CelebrateGoal();
            }
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (leftButton != null)
            {
                leftButton.interactable = interactable;
            }

            if (rightButton != null)
            {
                rightButton.interactable = interactable;
            }
        }

        /// <summary>
        /// Puts both sides back to neutral, with their normal press feedback
        /// restored, ready for a fresh choice.
        /// </summary>
        private void ResetSides()
        {
            RestoreSide(leftButton);
            RestoreSide(rightButton);
        }

        private void RestoreSide(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.transition = Selectable.Transition.ColorTint;

            if (button.targetGraphic != null)
            {
                button.targetGraphic.color = idleSideColor;
            }
        }

        /// <summary>
        /// Lights the side the player picked and leaves the other one plain.
        ///
        /// Written onto the button's own image rather than through its
        /// ColorBlock: the block's colours are multipliers over this image, and a
        /// non-interactable button is showing its disabled tint by now — which
        /// would swallow the highlight entirely.
        /// </summary>
        private void HighlightChoice(PenaltySide side)
        {
            Paint(leftButton, side == PenaltySide.Left);
            Paint(rightButton, side == PenaltySide.Right);
        }

        private void Paint(Button button, bool chosen)
        {
            if (button == null || button.targetGraphic == null)
            {
                return;
            }

            // Transitions off before painting. The buttons are non-interactable
            // by this point, and Unity's default disabled tint is both grey AND
            // half-transparent — it would multiply the highlight away to a pale
            // ghost of itself, on the one frame that has to read clearly.
            button.transition = Selectable.Transition.None;

            button.targetGraphic.color = chosen ? chosenSideColor : unchosenSideColor;
        }
    }
}
