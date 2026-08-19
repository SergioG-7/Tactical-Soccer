using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.AI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// The substitutions board: a modal screen showing one side's whole squad —
    /// the seven on the pitch laid out in their actual shape, the three on the
    /// bench underneath — and the stat block of whoever is selected.
    ///
    /// Tap a starter, then a substitute, and the two trade places.
    ///
    /// The slots are built at runtime rather than wired in the scene. There are
    /// ten of them and the roster is fixed, so ten serialized buttons would have
    /// worked — right up until the first substitution, when the man standing in
    /// a slot changes and every label, position and stat block behind it has to
    /// follow. Rebuilding from the live squad is the only version of this that
    /// cannot go stale.
    ///
    /// Lives on the canvas, not on the panel it owns: a component on a
    /// deactivated GameObject never receives Start, and Start is where the HUD
    /// button that opens this is wired.
    /// </summary>
    public class SubstitutionUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Header("Apertura")]
        [Tooltip("Returns to whatever opened this — in practice the interval.")]
        public Button closeButton;

        [Tooltip("Opens the editor on whichever player the board is currently " +
                 "showing. Optional: without one the board still swaps players, " +
                 "it just cannot retune them.")]
        public Button editButton;

        [Header("Textos")]
        public Text headerText;

        [Tooltip("Left-hand readout: who is selected, and everything about them.")]
        public Text statsText;

        [Header("Zonas")]
        [Tooltip("Mini-pitch the seven starters are laid out on, in their own shape.")]
        public RectTransform pitchArea;

        [Tooltip("Row the three substitutes sit in.")]
        public RectTransform benchArea;

        [Header("Equipo")]
        [SerializeField] private TeamId team = TeamId.Blue;

        [Header("Colores")]
        [SerializeField] private Color starterColor = new Color(0.86f, 0.88f, 0.92f, 1f);
        [SerializeField] private Color benchColor = new Color(0.62f, 0.65f, 0.70f, 1f);
        [SerializeField] private Color selectedColor = new Color(0.20f, 0.65f, 0.95f, 1f);
        [SerializeField] private Color exhaustedColor = new Color(0.92f, 0.45f, 0.35f, 1f);

        [Header("Medidas")]
        [SerializeField] private Vector2 slotSize = new Vector2(150f, 76f);
        [SerializeField] private int slotFontSize = 24;

        // The pitch, as the mapping below needs to know it. Mirrors the geometry
        // the scene generator builds: a Unity Plane at scale (3, 1, 5) spans
        // 30 x 50 units.
        private const float PitchHalfWidth = 15f;
        private const float PitchHalfLength = 25f;

        private const float FrozenTimeScale = 0f;
        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        private readonly List<TeamMember> squad = new List<TeamMember>();
        private readonly List<GameObject> slotObjects = new List<GameObject>();

        /// <summary>Whoever was tapped first, waiting for a partner to swap with.</summary>
        private TeamMember selected;

        /// <summary>
        /// The player the readout is currently describing.
        ///
        /// Not the same as <see cref="selected"/>: that one is half of a swap in
        /// progress and is cleared the moment the swap completes, whereas this
        /// stays on whoever the board last talked about — which is who the EDIT
        /// button should open.
        /// </summary>
        private TeamMember inspected;

        /// <summary>
        /// The screen to hand back to when this closes. Set when the board is
        /// opened from the interval, which is the only way in now: closing has
        /// to return to the team talk, NOT resume the match, or pressing the
        /// substitutions button would start the second half by itself.
        /// </summary>
        private GameObject returnPanel;

        /// <summary>Last refusal, shown under the stat block until something else happens.</summary>
        private string notice = string.Empty;

        public static SubstitutionUIController Instance { get; private set; }

        /// <summary>
        /// True while the board is up. The input layer is not governed by
        /// timeScale, so without this the player could draw a route through the
        /// panel — and drawing a route sets timeScale to 0.1, thawing the match
        /// behind a menu that is supposed to be holding it still. Exactly the
        /// hole the title screen already had to plug.
        /// </summary>
        public static bool IsOpen { get; private set; }

        private void Awake()
        {
            Instance = this;

            IsOpen = false;

            // Awake only runs in play mode, so this is what keeps the board off
            // the pitch in the editor.
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        private void OnEnable()
        {
            TacticalEvents.OnMatchOver += HandleMatchOver;
            PlayerEditUIController.OnPlayerEdited += HandlePlayerEdited;

            // The side panel is a paragraph composed from a dozen keys, so no
            // LocalizedText can follow the language on its own — this screen has
            // to repaint it. Without this it kept the old language until the
            // player was deselected and tapped again, which reads as the change
            // not having worked.
            Core.LocalizationManager.OnLanguageChanged += RepaintForLanguage;
        }

        /// <summary>
        /// Redraws everything this screen composes by hand after a language
        /// change: the side panel for whoever is selected — or the placeholder
        /// if nobody is — plus every slot caption on the pitch and the bench.
        /// </summary>
        private void RepaintForLanguage()
        {
            if (uiPanel == null || !uiPanel.activeSelf)
            {
                return;
            }

            // Rebuilt rather than rewritten: each slot's caption carries a role
            // tag and a stamina figure, and the board already knows how to make
            // them from the live squad.
            RebuildBoard();

            WriteStats(inspected);
        }

        private void OnDisable()
        {
            TacticalEvents.OnMatchOver -= HandleMatchOver;
            PlayerEditUIController.OnPlayerEdited -= HandlePlayerEdited;
            Core.LocalizationManager.OnLanguageChanged -= RepaintForLanguage;

            if (Instance == this)
            {
                Instance = null;
            }

            IsOpen = false;
        }

        private void Start()
        {
            // Cleared first: these listeners are added from code on every load,
            // and a duplicate would close the board twice on one press.
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(CloseBoard);
            }

            if (editButton != null)
            {
                editButton.onClick.RemoveAllListeners();
                editButton.onClick.AddListener(EditInspected);
                editButton.interactable = false;
            }
        }

        /// <summary>
        /// Opens the board and stops the match dead.
        ///
        /// Refuses while a duel is frozen on screen or before the whistle: both
        /// already hold timeScale at 0 for their own reasons, and closing this
        /// would hand the match back at normal speed with a duel still open.
        /// </summary>
        public void ShowBoard()
        {
            ShowBoard(null);
        }

        /// <summary>
        /// Opens the board and remembers where to go back to.
        /// </summary>
        /// <param name="returnTo">
        /// Panel to re-open when this closes. Null means closing goes back to
        /// the match itself, which is what thaws the pitch.
        /// </param>
        public void ShowBoard(GameObject returnTo)
        {
            if (uiPanel == null)
            {
                return;
            }

            if (ClashManager.IsClashActive)
            {
                Debug.Log("No se pueden hacer cambios en mitad de un duelo.");
                return;
            }

            // A live match is required only when nothing is expecting the board
            // back. That guard exists to stop the HUD opening it before kickoff;
            // a returnTo panel means a MENU sent it here deliberately — the team
            // sheet before the match, or the team talk at the interval — and
            // both of those are exactly when a squad should be arranged.
            if (returnTo == null && (!MatchManager.IsStarted || !MatchManager.IsPlayable))
            {
                return;
            }

            selected = null;
            notice = string.Empty;
            returnPanel = returnTo;

            CollectSquad();
            RebuildBoard();
            WriteStats(null);

            if (headerText != null)
            {
                Core.LocalizationManager.WriteFormatted(headerText, "subs.header", DescribeTeam(team));
            }

            uiPanel.SetActive(true);
            IsOpen = true;

            Time.timeScale = FrozenTimeScale;
        }

        /// <summary>
        /// Closes the board and lets the match run again — but only if there is
        /// still a match to run. The whistle freezes the pitch for good, and
        /// nothing may thaw it back out afterwards.
        /// </summary>
        public void CloseBoard()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            IsOpen = false;
            selected = null;

            // Opened from the interval: hand back to the team talk, still
            // frozen. Only the screen that sent the teams out may thaw the
            // pitch, and that is not this one.
            if (returnPanel != null)
            {
                returnPanel.SetActive(true);
                returnPanel = null;
                return;
            }

            if (!MatchManager.IsPlayable || !MatchManager.IsStarted || MatchManager.IsHalftime)
            {
                return;
            }

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;
        }

        /// <summary>
        /// Full time with the board open: it has to go, but the pitch must stay
        /// frozen, so this deliberately does not go through CloseBoard's thaw.
        /// </summary>
        private void HandleMatchOver()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            IsOpen = false;
            selected = null;
            returnPanel = null;
        }

        /// <summary>
        /// Trades two players between the pitch and the bench.
        ///
        /// The work itself belongs to <see cref="MatchManager"/>: the AI makes
        /// its own changes at the interval without ever opening this screen, and
        /// two copies of "what a substitution actually does" would be two things
        /// to keep in step. This is the board's way of asking for one.
        /// </summary>
        /// <summary>
        /// Opens the editor on the player the board is currently showing.
        ///
        /// One button acting on the current selection rather than an EDIT on
        /// every card: the board already has a selection model — tapping a
        /// player writes their numbers into the readout — and twenty extra
        /// buttons on a board whose whole job is tapping players would make
        /// every swap a game of hitting the right half of a card.
        ///
        /// The board hides itself and the editor puts it back, so a save lands
        /// the player straight back on the squad they were editing from.
        /// </summary>
        /// <summary>
        /// Redraws the board after one of its players has been edited.
        ///
        /// Both halves are needed and they answer different things. The CARDS
        /// carry the shirt number and the role, and a role change moves a player
        /// between the pitch and the bench in the layout — so the board is
        /// rebuilt. The READOUT down the left carries the numbers that were just
        /// changed, so it is rewritten for whoever it was already describing.
        ///
        /// Guarded on the panel being open: this is a static event, so it
        /// arrives whether or not the board is the screen the edit came from.
        /// </summary>
        private void HandlePlayerEdited(TeamMember edited)
        {
            if (uiPanel == null || !uiPanel.activeSelf)
            {
                return;
            }

            CollectSquad();
            RebuildBoard();

            // The edit may have been the reason the player is now on the other
            // side of the board, so the readout follows the edited player rather
            // than whoever happened to be inspected before.
            WriteStats(edited != null ? edited : inspected);
        }

        public void EditInspected()
        {
            if (inspected == null || PlayerEditUIController.Instance == null)
            {
                return;
            }

            PlayerEditUIController.Instance.ShowEditor(inspected, uiPanel);
        }

        public void SwapPlayers(TeamMember p1, TeamMember p2)
        {
            if (MatchManager.Instance == null)
            {
                Debug.LogWarning("No hay MatchManager: no se puede hacer el cambio.");
                return;
            }

            MatchManager.Instance.SwapPlayers(p1, p2);
        }

        private static Vector3 ResolveFormationSlot(TeamMember member)
        {
            return MatchManager.ResolveFormationSlot(member);
        }

        private void CollectSquad()
        {
            squad.Clear();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team)
                {
                    continue;
                }

                squad.Add(member);
            }

            // By shirt number, so the bench is always in the same order and a
            // substitution does not shuffle the board under the player's finger.
            squad.Sort((a, b) => a.jerseyNumber.CompareTo(b.jerseyNumber));
        }

        private void RebuildBoard()
        {
            foreach (GameObject slot in slotObjects)
            {
                if (slot == null)
                {
                    continue;
                }

                // Deactivated as well as destroyed: Destroy does not take effect
                // until the end of the frame, so a slot that was merely marked
                // would still be sitting under the new one — clickable, and
                // pointing at the player who used to be in that place.
                slot.SetActive(false);
                Destroy(slot);
            }

            slotObjects.Clear();

            int benchIndex = 0;
            int benchCount = CountBench();

            foreach (TeamMember member in squad)
            {
                if (member.isStarter)
                {
                    CreateSlot(member, pitchArea, MapToPitch(member));
                    continue;
                }

                CreateSlot(member, benchArea, MapToBench(benchIndex, benchCount));
                benchIndex++;
            }
        }

        private int CountBench()
        {
            int count = 0;

            foreach (TeamMember member in squad)
            {
                if (!member.isStarter)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Where a starter sits on the mini-pitch: across from his formation
        /// slot's X, and down the panel by how deep he plays in his OWN half —
        /// so the top of the box is the halfway line and the bottom is his own
        /// goal, whichever end of the world that happens to be.
        /// </summary>
        private Vector2 MapToPitch(TeamMember member)
        {
            if (pitchArea == null)
            {
                return Vector2.zero;
            }

            Vector3 slot = ResolveFormationSlot(member);

            float ownSide = team == TeamId.Blue ? -1f : 1f;

            float depth = Mathf.Clamp01((slot.z * ownSide) / PitchHalfLength);
            float across = Mathf.Clamp(slot.x / PitchHalfWidth, -1f, 1f);

            Rect area = pitchArea.rect;

            float halfWidth = Mathf.Max(0f, (area.width - slotSize.x) * 0.5f);
            float halfHeight = Mathf.Max(0f, (area.height - slotSize.y) * 0.5f);

            return new Vector2(across * halfWidth, halfHeight - (depth * 2f * halfHeight));
        }

        /// <summary>Evenly spaced across the bench row, centred.</summary>
        private Vector2 MapToBench(int index, int count)
        {
            if (benchArea == null || count <= 0)
            {
                return Vector2.zero;
            }

            Rect area = benchArea.rect;

            float step = area.width / count;
            float x = (-area.width * 0.5f) + (step * (index + 0.5f));

            return new Vector2(x, 0f);
        }

        private void CreateSlot(TeamMember member, RectTransform parent, Vector2 anchoredPosition)
        {
            if (parent == null)
            {
                return;
            }

            GameObject slotObject = new GameObject($"Slot {member.jerseyNumber}", typeof(RectTransform));
            slotObject.transform.SetParent(parent, false);

            RectTransform rect = (RectTransform)slotObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = slotSize;

            Image background = slotObject.AddComponent<Image>();
            background.color = ResolveSlotColor(member);

            Button button = slotObject.AddComponent<Button>();
            button.targetGraphic = background;

            // Captured into a local first: the loop variable would otherwise be
            // shared by every listener and all ten would select the last player.
            TeamMember captured = member;
            button.onClick.AddListener(() => HandleSlotClicked(captured));

            GameObject labelObject = new GameObject("Label", typeof(RectTransform));
            labelObject.transform.SetParent(slotObject.transform, false);

            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.AddComponent<Text>();
            label.font = ResolveFont();
            label.fontSize = slotFontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.text = DescribeSlot(member);

            slotObjects.Add(slotObject);
        }

        private Font ResolveFont()
        {
            if (statsText != null && statsText.font != null)
            {
                return statsText.font;
            }

            // Arial.ttf stopped being a built-in font in Unity 2022 and now
            // throws; LegacyRuntime.ttf replaced it.
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private Color ResolveSlotColor(TeamMember member)
        {
            if (member == selected)
            {
                return selectedColor;
            }

            // Read before the starter/bench tint, not after: a blown player is
            // the whole reason to be looking at this screen, and it has to be
            // visible without tapping anybody.
            if (member.IsExhausted)
            {
                return exhaustedColor;
            }

            return member.isStarter ? starterColor : benchColor;
        }

        private static string DescribeSlot(TeamMember member)
        {
            int stamina = Mathf.RoundToInt(member.StaminaFraction * 100f);
            string armband = member.isCaptain
                ? Core.LocalizationManager.GetText("subs.captainShort")
                : string.Empty;

            return Core.LocalizationManager.Format("subs.slot", member.jerseyNumber,
                PlayerRoles.Abbreviate(member.role), armband, stamina);
        }

        /// <summary>
        /// First tap selects; the second either swaps, if the two are on
        /// opposite sides of the touchline, or simply moves the selection.
        /// Tapping the selected player again clears it, so a mis-tap costs
        /// nothing.
        /// </summary>
        private void HandleSlotClicked(TeamMember member)
        {
            // Cleared first, written last. Whatever the last tap had to say has
            // been read by now, and leaving it up under a different player's
            // stat block would attach the message to the wrong man.
            notice = string.Empty;

            if (selected == member)
            {
                selected = null;
                RefreshSlotVisuals();
                WriteStats(member);
                return;
            }

            if (selected == null || selected.isStarter == member.isStarter)
            {
                selected = member;
                RefreshSlotVisuals();
                WriteStats(member);
                return;
            }

            TeamMember outgoing = selected.isStarter ? selected : member;
            TeamMember incoming = selected.isStarter ? member : selected;

            selected = null;

            if (!TrySubstitute(outgoing, incoming))
            {
                // The refusal is the useful information here, so the selection
                // is dropped and the reason goes onto the readout — which is
                // why the stats are written AFTER the attempt, not before it.
                RefreshSlotVisuals();
                WriteStats(member);
                return;
            }

            notice = $"Entra el {incoming.jerseyNumber} por el {outgoing.jerseyNumber}.";

            RebuildBoard();
            WriteStats(incoming);
        }

        /// <summary>
        /// The two substitutions that cannot be made, and why.
        ///
        /// Neither is in the brief; both are holes the brief leaves open. A
        /// keeper swapped for an outfield substitute would take the isGoalkeeper
        /// flag, the wingspan collider and the goal-line AI off the pitch with
        /// him and leave the goal genuinely undefended — there is no keeper on
        /// this bench to put in his place. And substituting the player who is
        /// holding the ball would carry the ball into the dugout with him: it is
        /// glued to his socket, and the swap is a teleport.
        /// </summary>
        private bool TrySubstitute(TeamMember outgoing, TeamMember incoming)
        {
            if (outgoing.isGoalkeeper || incoming.isGoalkeeper)
            {
                notice = "El portero no puede ser sustituido: no hay portero en el banquillo.";
                return false;
            }

            if (IsHoldingBall(outgoing))
            {
                notice = Core.LocalizationManager.GetText("subs.ballNotice");
                return false;
            }

            SwapPlayers(outgoing, incoming);

            return true;
        }

        private static bool IsHoldingBall(TeamMember member)
        {
            return member.TryGetComponent(out PlayerBallHandler handler) && handler.HasBall;
        }

        private void RefreshSlotVisuals()
        {
            // Cheaper than rebuilding: nobody has moved between the pitch and
            // the bench, only the highlight has changed.
            int index = 0;

            foreach (TeamMember member in squad)
            {
                if (index >= slotObjects.Count)
                {
                    break;
                }

                GameObject slotObject = slotObjects[index];
                index++;

                if (slotObject == null || !slotObject.TryGetComponent(out Image background))
                {
                    continue;
                }

                background.color = ResolveSlotColor(member);
            }
        }

        /// <summary>
        /// The left-hand readout. Rebuilt from the player every time rather than
        /// written once, because stamina is exactly the number this screen
        /// exists to show and it moves the whole match.
        /// </summary>
        private void WriteStats(TeamMember member)
        {
            // Remembered even when the readout itself is missing: this is what
            // the EDIT button acts on, and it is the player the board is
            // currently ABOUT rather than the half of a swap still being chosen.
            inspected = member;

            if (editButton != null)
            {
                editButton.interactable = member != null;
            }

            if (statsText == null)
            {
                return;
            }

            if (member == null)
            {
                statsText.text = Core.LocalizationManager.GetText("subs.selectPlayer") + "\n\n" +
                    Core.LocalizationManager.GetText("subs.hint") + notice;
                return;
            }

            int stamina = Mathf.RoundToInt(member.currentStamina);
            int maxStamina = Mathf.RoundToInt(member.maxStamina);
            int percent = Mathf.RoundToInt(member.StaminaFraction * 100f);

            string status = Core.LocalizationManager.GetText(
                member.isStarter ? "subs.statusPitch" : "subs.statusBench");

            string exhausted = member.IsExhausted
                ? Core.LocalizationManager.GetText("subs.exhausted")
                : string.Empty;

            string armband = member.isCaptain
                ? Core.LocalizationManager.GetText("subs.captain")
                : string.Empty;

            // Read through the player, not off the stat asset. The numbers on
            // the asset are the raw ones; what this screen has to show is what
            // the player actually brings to a duel, captain's passive included.
            // Padded to the longest label in the ACTIVE language rather than to
            // a width measured against Spanish: "GOALKEEPING" and "キャッチ" are
            // not the width of "PARADA", and a hard-coded pad left the numbers
            // walking about as soon as the language changed.
            string block =
                StatLine("stat.dribble", member.Dribble) +
                StatLine("stat.power", member.Power) +
                StatLine("stat.shoot", member.Shoot) +
                StatLine("stat.tackle", member.Tackle) +
                StatLine("stat.block", member.Block) +
                StatLine("stat.goalkeeping", member.Goalkeeping).TrimEnd('\n');

            statsText.text =
                Core.LocalizationManager.Format("subs.jersey", member.jerseyNumber) + armband + "\n" +
                $"{PlayerRoles.Describe(member.role)}  ·  {Elements.Describe(member.element)}\n" +
                $"{status}{exhausted}\n\n" +
                Core.LocalizationManager.Format("subs.staminaLine", stamina, maxStamina, percent) + "\n\n" +
                $"{block}\n\n" +
                notice;
        }

        /// <summary>
        /// One line of the stat block: the attribute's name and its value.
        ///
        /// Written as "NAME: value" rather than padded into columns. The panel
        /// is drawn in the ordinary proportional UI font, where padding with
        /// spaces never lined anything up — it only looked as though it did
        /// because the Spanish names happened to be a similar length.
        /// </summary>
        private static string StatLine(string key, int value)
        {
            return Core.LocalizationManager.Format("stat.line",
                Core.LocalizationManager.GetText(key), value) + "\n";
        }

        private static string DescribeTeam(TeamId teamId)
        {
            return Core.LocalizationManager.GetText(teamId == TeamId.Blue ? "team.blue" : "team.red");
        }
    }
}
