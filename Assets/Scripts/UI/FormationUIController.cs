using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// The team sheet: the shape you play, and who wears the armband.
    ///
    /// The two belong on one screen because they are the same decision. The
    /// captain's passive is decided by the LINE he ends up playing in, and the
    /// shape is what decides that line — so picking a captain before the shape
    /// would be picking a buff you had not chosen yet.
    ///
    /// That is why choosing a formation applies it immediately rather than at
    /// kickoff: the squad is re-roled on the spot behind the menu, and the
    /// captain list is rebuilt from the roles the players will actually take the
    /// field in.
    ///
    /// Lives on the canvas rather than on the panel it owns. A component on a
    /// deactivated GameObject never receives Start, so a controller parked on
    /// its own hidden panel would never wire up the buttons that dismiss it.
    ///
    /// The pitch stays frozen at timeScale 0 for the whole menu, exactly as it
    /// is behind the title. Thawing it is this screen's last act, because after
    /// this there is no menu left to hold the match back.
    /// </summary>
    public class FormationUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Header("Formaciones")]
        public Button btn222;
        public Button btn321;
        public Button btn132;

        [Header("Capitán")]
        [Tooltip("Row the starters are listed in, one button each. The buttons " +
                 "are built at runtime: which players are available and what " +
                 "they play both change with the shape chosen above.")]
        public RectTransform captainArea;

        public Text captainHeading;

        [Header("Confirmación")]
        public Button btnStartMatch;

        [Tooltip("Back to the main menu, abandoning the match being set up. " +
                 "Optional: without one this screen still works, it just has no " +
                 "way out except forwards.")]
        public Button backButton;

        [Tooltip("Opens the squad board — the SAME one the interval uses — so " +
                 "the eleven can be arranged before kickoff rather than only " +
                 "after forty-five seconds of finding out it is wrong.")]
        public Button squadButton;

        [Header("Feedback")]
        [Tooltip("Fill of the shape currently chosen.")]
        [SerializeField] private Color selectedColor = new Color(0.20f, 0.65f, 0.95f, 1f);

        [Tooltip("Fill of the shapes not chosen.")]
        [SerializeField] private Color unselectedColor = new Color(0.88f, 0.88f, 0.88f, 1f);

        [SerializeField] private Vector2 captainSlotSize = new Vector2(190f, 96f);
        [SerializeField] private int captainSlotFontSize = 22;

        private const float FrozenTimeScale = 0f;
        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        private FormationType selectedFormation = FormationType.Balanced_2_2_2;

        private readonly List<GameObject> captainSlots = new List<GameObject>();
        private TeamMember selectedCaptain;

        public static FormationUIController Instance { get; private set; }

        /// <summary>True while the team sheet is up. Read off the panel itself.</summary>
        public static bool IsOpen => Instance != null
            && Instance.uiPanel != null
            && Instance.uiPanel.activeSelf;

        private void Awake()
        {
            Instance = this;

            // Hidden until the title screen hands over. Awake runs before any of
            // that, so this is what keeps the menu off the pitch in the editor.
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
        }

        /// <summary>
        /// Back out of the team sheet to the main menu, abandoning the match
        /// that was being set up.
        ///
        /// The pitch is NOT thawed on the way out: the title screen is a modal
        /// like this one and freezes it again immediately, and unfreezing
        /// between the two would run a couple of frames of a match nobody has
        /// started yet.
        ///
        /// Any tournament round opened on the way in is abandoned with it. The
        /// round has already written its settings into the match, but it has not
        /// been played — leaving it armed would count the NEXT match, whatever
        /// it was, as that round's result.
        /// </summary>
        /// <summary>
        /// Hands over to the squad board, which comes back here when closed.
        ///
        /// The SAME board the interval uses, not a copy of it. Arranging an
        /// eleven is one job whether it happens before kickoff or at half time,
        /// and a second implementation would be a second set of rules about who
        /// may be swapped for whom — which would drift from this one the first
        /// time either was touched.
        ///
        /// This panel is hidden outright rather than faded: the board restores
        /// it by SetActive when it closes, and a fade would be racing that.
        /// </summary>
        public void OpenSquadBoard()
        {
            if (SubstitutionUIController.Instance == null)
            {
                Debug.LogWarning("No hay tablero de plantilla en la escena.");
                return;
            }

            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            SubstitutionUIController.Instance.ShowBoard(uiPanel);
        }

        public void GoBack()
        {
            UIAnimator.Hide(uiPanel);

            if (Core.TournamentManager.Instance != null)
            {
                Core.TournamentManager.Instance.Abandon();
            }

            if (TitleScreenUIController.Instance != null)
            {
                TitleScreenUIController.Instance.ShowTitle();
                return;
            }

            Debug.LogWarning("No hay pantalla de título a la que volver.");
        }

        private void Start()
        {
            // Cleared first: these listeners are added from code on every load,
            // and a duplicate would kick the match off twice on one press.
            BindFormation(btn222, FormationType.Balanced_2_2_2);
            BindFormation(btn321, FormationType.Defensive_3_2_1);
            BindFormation(btn132, FormationType.Offensive_1_3_2);

            if (backButton != null)
            {
                backButton.onClick.RemoveAllListeners();
                backButton.onClick.AddListener(GoBack);

                MatchConfigUIController.LiftAboveSiblings(backButton);
            }

            if (squadButton != null)
            {
                squadButton.onClick.RemoveAllListeners();
                squadButton.onClick.AddListener(OpenSquadBoard);
            }

            if (btnStartMatch != null)
            {
                btnStartMatch.onClick.RemoveAllListeners();
                btnStartMatch.onClick.AddListener(StartMatch);
            }
            else
            {
                Debug.LogError("FormationUIController no tiene botón de confirmación: " +
                               "el partido no podría empezar nunca.");
            }

            RefreshSelectionFeedback();
        }

        /// <summary>
        /// Opens the menu. Called by the title screen rather than from Start,
        /// so the two screens hand over in a fixed order instead of both being
        /// up at once.
        /// </summary>
        public void ShowMenu()
        {
            UIAnimator.Show(uiPanel);

            // The title screen froze the match; it stays frozen through here.
            Time.timeScale = FrozenTimeScale;

            WriteFormationCaptions();

            RefreshSelectionFeedback();
            RebuildCaptainOptions();
        }

        /// <summary>
        /// Locks the chosen shape and armband in, and starts the match. This is
        /// the only place the pitch is allowed to thaw: nothing is holding it
        /// back once the last menu is gone.
        /// </summary>
        public void StartMatch()
        {
            UIAnimator.Hide(uiPanel);

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            if (MatchManager.Instance == null)
            {
                Debug.LogError("No hay MatchManager: no se puede aplicar la formación ni empezar.");
                return;
            }

            TeamId team = MatchManager.Instance.HumanTeam;

            // Applied again even though picking a shape already did it: the
            // player may never have touched a formation button, in which case
            // this is the only time the default shape is ever laid out.
            MatchManager.Instance.ApplyFormation(team, selectedFormation);

            // After the formation, never before. The captaincy's passive is
            // chosen by the captain's ROLE, and the shape is what assigns it —
            // reading it first would hand out the buff for a line the player is
            // no longer playing in.
            MatchManager.Instance.SetCaptain(team, ResolveCaptain(team));

            MatchManager.Instance.StartInitialKickoff();
        }

        public void SelectFormation(FormationType formation)
        {
            selectedFormation = formation;

            // Applied on the spot rather than at kickoff. The squad is behind an
            // opaque menu with the match frozen, so nobody sees them move — and
            // it is what lets the captain list below show the roles the players
            // will actually line up in.
            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.ApplyFormation(MatchManager.Instance.HumanTeam, formation);
            }

            RefreshSelectionFeedback();
            RebuildCaptainOptions();
        }

        /// <summary>
        /// Whoever the player picked, or an outfield starter if they picked
        /// nobody. A side with no captain at all is a side quietly playing
        /// without a passive the opposition always has.
        ///
        /// The keeper is skipped when falling back, for the same reason the
        /// opposition's own random pick skips him: his line only ever hands out
        /// the defensive passive, and he is the one player who never leaves his
        /// box to make use of it. He stays selectable by hand — it is a legal
        /// choice and the buff is real — but a player who never touched this row
        /// should not be handed the dullest armband on the pitch by default.
        /// </summary>
        private TeamMember ResolveCaptain(TeamId team)
        {
            if (selectedCaptain != null && selectedCaptain.team == team && selectedCaptain.isStarter)
            {
                return selectedCaptain;
            }

            foreach (TeamMember starter in CollectStarters(team))
            {
                if (!starter.isGoalkeeper)
                {
                    return starter;
                }
            }

            // An eleven of nothing but keepers is not a squad this game can
            // produce, but handing back the keeper beats handing back nobody.
            List<TeamMember> starters = CollectStarters(team);

            return starters.Count > 0 ? starters[0] : null;
        }

        private void BindFormation(Button button, FormationType formation)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SelectFormation(formation));
        }

        /// <summary>
        /// Tints the chosen shape and returns the others to normal, so the menu
        /// answers "what am I about to play" without being pressed again.
        /// </summary>
        private void RefreshSelectionFeedback()
        {
            Tint(btn222, selectedFormation == FormationType.Balanced_2_2_2);
            Tint(btn321, selectedFormation == FormationType.Defensive_3_2_1);
            Tint(btn132, selectedFormation == FormationType.Offensive_1_3_2);
        }

        /// <summary>
        /// Written onto the button's own image rather than through its ColorBlock:
        /// the block's normalColor is a multiplier over this image, so leaving the
        /// image white and tinting the block would fight every hover and press
        /// transition the Button applies on top.
        /// </summary>
        private void Tint(Button button, bool isSelected)
        {
            if (button == null || button.targetGraphic == null)
            {
                return;
            }

            button.targetGraphic.color = isSelected ? selectedColor : unselectedColor;
        }

        /// <summary>
        /// Rebuilds the row of candidates from the live squad. Torn down and
        /// rebuilt rather than relabelled because the shape can change which
        /// line each player holds, and a stale button would be offering a buff
        /// that no longer matched the man on it.
        /// </summary>
        private void RebuildCaptainOptions()
        {
            if (captainArea == null)
            {
                return;
            }

            foreach (GameObject slot in captainSlots)
            {
                if (slot == null)
                {
                    continue;
                }

                // Deactivated as well as destroyed: Destroy only takes effect at
                // the end of the frame, so a slot merely marked would still be
                // sitting under the new one and still taking clicks.
                slot.SetActive(false);
                Destroy(slot);
            }

            captainSlots.Clear();

            if (MatchManager.Instance == null)
            {
                return;
            }

            List<TeamMember> starters = CollectStarters(MatchManager.Instance.HumanTeam);

            if (selectedCaptain != null && !starters.Contains(selectedCaptain))
            {
                selectedCaptain = null;
            }

            Rect area = captainArea.rect;
            float step = starters.Count > 0 ? area.width / starters.Count : 0f;

            for (int i = 0; i < starters.Count; i++)
            {
                float x = (-area.width * 0.5f) + (step * (i + 0.5f));

                CreateCaptainSlot(starters[i], new Vector2(x, 0f));
            }

            RefreshCaptainFeedback();

            // The heading is rebuilt with the buttons, not just when one is
            // pressed. Changing the shape re-roles the squad, so the captain
            // the player chose a moment ago may now be playing a different line
            // and carrying a different passive — and the heading, written once
            // at selection time, would have gone on advertising the old one.
            RefreshCaptainHeading();
        }

        /// <summary>
        /// This side's starting seven, keeper included, in shirt order so the row
        /// does not reshuffle itself under the player's finger every time the
        /// shape changes.
        /// </summary>
        private static List<TeamMember> CollectStarters(TeamId team)
        {
            List<TeamMember> starters = new List<TeamMember>();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team == team && member.isStarter)
                {
                    starters.Add(member);
                }
            }

            starters.Sort((a, b) => a.jerseyNumber.CompareTo(b.jerseyNumber));

            return starters;
        }

        private void CreateCaptainSlot(TeamMember member, Vector2 anchoredPosition)
        {
            // Captured into a local first: the loop variable would otherwise be
            // shared by every listener and all seven would pick the last player.
            TeamMember captured = member;

            string labelText = Core.LocalizationManager.Format("formation.captainSlot",
                member.jerseyNumber, PlayerRoles.Abbreviate(member.role), DescribePassive(member.role));

            GameObject slotObject = UiSlotFactory.CreateSlot(
                captainArea,
                $"Captain {member.jerseyNumber}",
                anchoredPosition,
                captainSlotSize,
                unselectedColor,
                labelText,
                ResolveFont(),
                captainSlotFontSize,
                () => SelectCaptain(captured));

            captainSlots.Add(slotObject);
        }

        /// <summary>
        /// What this player's line would give the side. Printed on the button
        /// because the captaincy is the one choice on this screen whose effect
        /// is invisible on the pitch — you cannot see a stamina multiplier.
        /// </summary>
        private static string DescribePassive(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Forward: return Core.LocalizationManager.GetText("passive.attack");
                case PlayerRole.Midfielder: return Core.LocalizationManager.GetText("passive.stamina");
                default: return Core.LocalizationManager.GetText("passive.defence");
            }
        }

        /// <summary>
        /// Writes the three shape buttons: the shape itself, which is the same
        /// in every language, over the word that describes it, which is not.
        ///
        /// Done here rather than by the generator because the two halves come
        /// from different places — one from the formation table, one from the
        /// dictionary — and a single key could only carry one of them.
        /// </summary>
        private void WriteFormationCaptions()
        {
            Caption(btn222, FormationType.Balanced_2_2_2, "formation.balanced");
            Caption(btn321, FormationType.Defensive_3_2_1, "formation.defensive");
            Caption(btn132, FormationType.Offensive_1_3_2, "formation.offensive");
        }

        private static void Caption(Button button, FormationType shape, string key)
        {
            if (button == null)
            {
                return;
            }

            Text label = button.GetComponentInChildren<Text>();

            if (label == null)
            {
                return;
            }

            label.text = $"{Formations.GetLabel(shape)}\n{Core.LocalizationManager.GetText(key)}";

            Core.LocalizationManager.ApplyFont(label);
        }

        public void SelectCaptain(TeamMember member)
        {
            selectedCaptain = member;

            RefreshCaptainFeedback();
            RefreshCaptainHeading();
        }

        /// <summary>
        /// Writes the heading from the armband that would actually be worn if
        /// the match started now — the player's pick, or the fallback if there
        /// is none.
        ///
        /// Showing the fallback rather than a blank is the point: the side gets
        /// a captain either way, and a heading that stayed empty until pressed
        /// would hide a passive that was already in effect. Reading it back out
        /// of ResolveCaptain, rather than from the click, is what keeps it true
        /// after a change of shape.
        /// </summary>
        private void RefreshCaptainHeading()
        {
            if (captainHeading == null || MatchManager.Instance == null)
            {
                return;
            }

            TeamMember captain = ResolveCaptain(MatchManager.Instance.HumanTeam);

            if (captain == null)
            {
                Core.LocalizationManager.Write(captainHeading, "formation.captainNone");
                return;
            }

            string suffix = selectedCaptain == captain
                ? string.Empty
                : Core.LocalizationManager.GetText("formation.captainDefault");

            Core.LocalizationManager.WriteFormatted(captainHeading, "formation.captainHeading",
                captain.jerseyNumber, PlayerRoles.Describe(captain.role),
                DescribePassive(captain.role), suffix);
        }

        private void RefreshCaptainFeedback()
        {
            List<TeamMember> starters = MatchManager.Instance != null
                ? CollectStarters(MatchManager.Instance.HumanTeam)
                : new List<TeamMember>();

            for (int i = 0; i < captainSlots.Count && i < starters.Count; i++)
            {
                if (captainSlots[i] == null || !captainSlots[i].TryGetComponent(out Image background))
                {
                    continue;
                }

                background.color = starters[i] == selectedCaptain ? selectedColor : unselectedColor;
            }
        }

        private Font ResolveFont()
        {
            if (captainHeading != null && captainHeading.font != null)
            {
                return captainHeading.font;
            }

            return LocalizationManager.BuiltInFont;
        }
    }
}
