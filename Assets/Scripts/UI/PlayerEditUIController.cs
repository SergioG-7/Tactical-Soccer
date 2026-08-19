using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// Edits one player: what they play, what element they carry, how good they
    /// are and how long they last.
    ///
    /// Opened from the squad board, over it, and hands it straight back. The
    /// board owns WHO is in the eleven; this owns what each of them is.
    ///
    /// Nothing is written until SAVE. The panel works on its own copy of the
    /// numbers, so backing out of a half-finished edit leaves the player exactly
    /// as they were — which matters most for the role, whose side effects are
    /// the awkward part of this screen (see ApplyRole).
    ///
    /// Every edit lands on the TeamMember, never on the PlayerStatsSO. Those
    /// assets are shared by every player of the same role on BOTH sides and are
    /// files on disk: an edit written there would buff the opposition too, and
    /// survive into the next match and into the repository.
    /// </summary>
    public class PlayerEditUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Header("Cabecera")]
        public Text headingText;

        [Tooltip("The number in each stat row, in the same order the rows are " +
                 "built: regate, fuerza, tiro, entrada, bloqueo, parada, " +
                 "estamina. Rewritten on every press, which is the whole of the " +
                 "feedback this panel gives.")]
        public Text[] statValueTexts;

        [Tooltip("Where a refused edit explains itself — demoting the last " +
                 "goalkeeper, for instance. Empty the rest of the time.")]
        public Text noticeText;

        [Header("Posición")]
        public Button roleGoalkeeperButton;
        public Button roleDefenderButton;
        public Button roleMidfielderButton;
        public Button roleForwardButton;

        [Header("Elemento")]
        public Button elementFireButton;
        public Button elementForestButton;
        public Button elementWindButton;
        public Button elementMountainButton;

        [Header("Atributos")]
        public Button dribbleUpButton;
        public Button dribbleDownButton;
        public Button powerUpButton;
        public Button powerDownButton;
        public Button shootUpButton;
        public Button shootDownButton;
        public Button tackleUpButton;
        public Button tackleDownButton;
        public Button blockUpButton;
        public Button blockDownButton;
        public Button goalkeepingUpButton;
        public Button goalkeepingDownButton;
        public Button staminaUpButton;
        public Button staminaDownButton;

        [Header("Salida")]
        public Button saveButton;
        public Button closeButton;

        [Header("Feedback")]
        [SerializeField] private Color selectedColor = new Color(0.20f, 0.65f, 0.95f, 1f);
        [SerializeField] private Color unselectedColor = new Color(0.88f, 0.88f, 0.88f, 1f);

        [Tooltip("How much one press moves a stat. Coarse on purpose: this is a " +
                 "tuning screen, not a spreadsheet, and single points would mean " +
                 "fifty presses to make a difference anybody can feel.")]
        [SerializeField] private int statStep = 5;

        [SerializeField] private float staminaStep = 25f;

        /// <summary>
        /// Raised after an edit has been written to a player.
        /// </summary>
        /// <remarks>
        /// The squad board that opened this listens for it: it drew that
        /// player's card and stat readout before the edit, and nothing else
        /// would tell it those numbers had moved — so the board went on showing
        /// the old ones until the player was deselected and picked again.
        ///
        /// An event rather than a direct call back into the board, because the
        /// editor should not have to know who opened it. Static so a listener
        /// does not need a reference to this instance, which is created by the
        /// scene generator.
        /// </remarks>
        public static event System.Action<TeamMember> OnPlayerEdited;

        public static PlayerEditUIController Instance { get; private set; }

        /// <summary>True while the editor is up. Read by the input layer.</summary>
        public static bool IsOpen => Instance != null
            && Instance.uiPanel != null
            && Instance.uiPanel.activeSelf;

        private TeamMember subject;
        private GameObject returnPanel;

        // The staged edit. Everything here is a copy until SAVE.
        private PlayerRole role;
        private Element element;
        private int dribble;
        private int power;
        private int shoot;
        private int tackle;
        private int block;
        private int goalkeeping;
        private float maxStamina;

        private string notice = string.Empty;

        private void Awake()
        {
            Instance = this;

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

        private void Start()
        {
            Bind(roleGoalkeeperButton, () => StageRole(PlayerRole.Goalkeeper));
            Bind(roleDefenderButton, () => StageRole(PlayerRole.Defender));
            Bind(roleMidfielderButton, () => StageRole(PlayerRole.Midfielder));
            Bind(roleForwardButton, () => StageRole(PlayerRole.Forward));

            Bind(elementFireButton, () => StageElement(Element.Fuego));
            Bind(elementForestButton, () => StageElement(Element.Bosque));
            Bind(elementWindButton, () => StageElement(Element.Aire));
            Bind(elementMountainButton, () => StageElement(Element.Montaña));

            Bind(dribbleUpButton, () => Nudge(ref dribble, statStep));
            Bind(dribbleDownButton, () => Nudge(ref dribble, -statStep));
            Bind(powerUpButton, () => Nudge(ref power, statStep));
            Bind(powerDownButton, () => Nudge(ref power, -statStep));
            Bind(shootUpButton, () => Nudge(ref shoot, statStep));
            Bind(shootDownButton, () => Nudge(ref shoot, -statStep));
            Bind(tackleUpButton, () => Nudge(ref tackle, statStep));
            Bind(tackleDownButton, () => Nudge(ref tackle, -statStep));
            Bind(blockUpButton, () => Nudge(ref block, statStep));
            Bind(blockDownButton, () => Nudge(ref block, -statStep));
            Bind(goalkeepingUpButton, () => Nudge(ref goalkeeping, statStep));
            Bind(goalkeepingDownButton, () => Nudge(ref goalkeeping, -statStep));

            Bind(staminaUpButton, () => NudgeStamina(staminaStep));
            Bind(staminaDownButton, () => NudgeStamina(-staminaStep));

            Bind(saveButton, Save);
            Bind(closeButton, Close);
        }

        /// <summary>
        /// Opens the editor on <paramref name="member"/>, returning to
        /// <paramref name="returnTo"/> when it closes.
        /// </summary>
        public void ShowEditor(TeamMember member, GameObject returnTo)
        {
            if (member == null || uiPanel == null)
            {
                return;
            }

            subject = member;
            returnPanel = returnTo;
            notice = string.Empty;

            // Copied out, not referenced. Backing out has to leave the player
            // untouched, and the role in particular cannot be tried on and undone
            // once it has been applied for real.
            role = member.role;
            element = member.element;
            dribble = member.BaseDribble;
            power = member.BasePower;
            shoot = member.BaseShoot;
            tackle = member.BaseTackle;
            block = member.BaseBlock;
            goalkeeping = member.BaseGoalkeeping;
            maxStamina = member.maxStamina;

            if (returnTo != null)
            {
                returnTo.SetActive(false);
            }

            uiPanel.SetActive(true);

            Refresh();
        }

        public void Close()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            subject = null;

            if (returnPanel != null)
            {
                returnPanel.SetActive(true);
                returnPanel = null;
            }
        }

        /// <summary>
        /// Writes the staged edit onto the player and closes.
        ///
        /// The role goes last and through its own method, because it is the only
        /// field here with consequences beyond the player it belongs to.
        /// </summary>
        public void Save()
        {
            if (subject == null)
            {
                Close();
                return;
            }

            subject.element = element;
            subject.ApplyStatEdits(dribble, power, shoot, tackle, block, goalkeeping, maxStamina);

            ApplyRole(subject, role);

            Debug.Log($"[Edición] #{subject.jerseyNumber}: {PlayerRoles.Describe(subject.role)}, " +
                      $"{element}, REG {dribble} FUE {power} TIR {shoot} / " +
                      $"ENT {tackle} BLO {block} PAR {goalkeeping}, estamina {maxStamina:F0}.");

            // Captured before Close clears it.
            TeamMember edited = subject;

            Close();

            // Raised AFTER closing, so the screen that listens is back on
            // screen and active when it redraws itself. Announced from here and
            // not from the caller: an edit is a fact about the player, and
            // anything showing that player needs to hear it whether or not it
            // was the thing that opened this panel.
            OnPlayerEdited?.Invoke(edited);
        }

        /// <summary>
        /// Moves a player between lines, and puts the refusal on screen when the
        /// move is one the side cannot survive — demoting its last goalkeeper.
        ///
        /// The rule itself lives in <see cref="SquadRoles"/> rather than here,
        /// because this panel is no longer the only thing that moves a player
        /// between lines: restoring a saved squad replays exactly these changes
        /// at startup, and two copies of a rule about goalkeepers would be two
        /// copies that can disagree about how many a side has.
        /// </summary>
        private void ApplyRole(TeamMember member, PlayerRole newRole)
        {
            if (!SquadRoles.TrySetRole(member, newRole, out string refusal))
            {
                notice = refusal;
            }
        }

        private void StageRole(PlayerRole value)
        {
            role = value;
            notice = string.Empty;
            Refresh();
        }

        private void StageElement(Element value)
        {
            element = value;
            notice = string.Empty;
            Refresh();
        }

        private void Nudge(ref int stat, int delta)
        {
            stat = Mathf.Clamp(stat + delta, TeamMember.StatMinimum, TeamMember.StatMaximum);
            Refresh();
        }

        private void NudgeStamina(float delta)
        {
            maxStamina = Mathf.Clamp(maxStamina + delta,
                TeamMember.StaminaMinimum, TeamMember.StaminaMaximum);

            Refresh();
        }

        private void Refresh()
        {
            if (subject == null)
            {
                return;
            }

            if (headingText != null)
            {
                // NOT the GameObject's name. That is generated once ("Team
                // Blue Midfielder 1"), it is English, and it goes stale the
                // moment a formation reassigns the role — so it was both
                // untranslatable and wrong. The side is named by the strip it is
                // actually wearing, which is what the player sees on the pitch.
                Core.LocalizationManager.WriteFormatted(headingText, "edit.heading",
                    subject.jerseyNumber,
                    Fouls.DescribeTeam(subject.team),
                    PlayerRoles.Describe(subject.role));
            }

            // Captions first, then the tint. Both the positions and the
            // elements pair a translated word with something that is not one —
            // a role abbreviation, an elemental kanji — so no single key can
            // carry them and a LocalizedText cannot write them. Rewritten on
            // every refresh, which is what makes them follow a language change
            // the next time the panel is opened.
            WriteRoleCaptions();
            WriteElementCaptions();

            Tint(roleGoalkeeperButton, role == PlayerRole.Goalkeeper);
            Tint(roleDefenderButton, role == PlayerRole.Defender);
            Tint(roleMidfielderButton, role == PlayerRole.Midfielder);
            Tint(roleForwardButton, role == PlayerRole.Forward);

            Tint(elementFireButton, element == Element.Fuego);
            Tint(elementForestButton, element == Element.Bosque);
            Tint(elementWindButton, element == Element.Aire);
            Tint(elementMountainButton, element == Element.Montaña);

            // Each number goes in its own row, next to the buttons that move it.
            // There used to be a summary block repeating all seven at once in
            // the corner; it said nothing the rows do not and it sat on top of
            // the element buttons.
            WriteValue(0, dribble.ToString());
            WriteValue(1, power.ToString());
            WriteValue(2, shoot.ToString());
            WriteValue(3, tackle.ToString());
            WriteValue(4, block.ToString());
            WriteValue(5, goalkeeping.ToString());
            WriteValue(6, maxStamina.ToString("F0"));

            if (noticeText != null)
            {
                noticeText.text = notice;
            }
        }

        private void WriteRoleCaptions()
        {
            Caption(roleGoalkeeperButton, PlayerRoles.Abbreviate(PlayerRole.Goalkeeper));
            Caption(roleDefenderButton, PlayerRoles.Abbreviate(PlayerRole.Defender));
            Caption(roleMidfielderButton, PlayerRoles.Abbreviate(PlayerRole.Midfielder));
            Caption(roleForwardButton, PlayerRoles.Abbreviate(PlayerRole.Forward));
        }

        private void WriteElementCaptions()
        {
            Caption(elementFireButton, DescribeElement(Element.Fuego));
            Caption(elementForestButton, DescribeElement(Element.Bosque));
            Caption(elementWindButton, DescribeElement(Element.Aire));
            Caption(elementMountainButton, DescribeElement(Element.Montaña));
        }

        /// <summary>The kanji and the name, matching the badge on the player's own tag.</summary>
        private static string DescribeElement(Element value)
        {
            return $"{Elements.Glyph(value)} {Elements.Describe(value)}";
        }

        private static void Caption(Button button, string text)
        {
            if (button == null)
            {
                return;
            }

            Text label = button.GetComponentInChildren<Text>();

            if (label != null)
            {
                label.text = text;

                // The element names include a kanji, and the built-in UI font
                // cannot draw one: the badge would silently come out blank.
                LocalizationManager.ApplyFont(label);
            }
        }

        private void WriteValue(int row, string value)
        {
            if (statValueTexts == null || row >= statValueTexts.Length || statValueTexts[row] == null)
            {
                return;
            }

            statValueTexts[row].text = value;
        }

        private void Tint(Button button, bool isSelected)
        {
            if (button == null || button.targetGraphic == null)
            {
                return;
            }

            button.targetGraphic.color = isSelected ? selectedColor : unselectedColor;
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
    }
}
