using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// Presents the frozen duel and collects the human's tactical choice. Owns
    /// no duel maths: it offers the two moves that side is allowed, rolls the
    /// AI's answer, and hands both to the ClashManager.
    ///
    /// The banner is three zones. Blue is ALWAYS on the left and Red ALWAYS on
    /// the right, whichever of them happens to be attacking — a panel whose
    /// sides swapped depending on who had the ball would make the player re-read
    /// it every single duel, which is the opposite of what a stat readout is for.
    /// The centre holds the choice.
    ///
    /// Only two duels reach this screen. Interceptions are settled in the air
    /// without stopping the match, so there is no third layout to keep in step.
    ///
    /// Blue is the human side; Red is driven by the AI.
    /// </summary>
    public class ClashUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Tooltip("Headline across the middle of the banner: what kind of duel " +
                 "this is. The numbers live in the side panels.")]
        public Text clashText;

        [Tooltip("Left zone. Always the Blue player, left-aligned.")]
        public Text blueStatsText;

        [Tooltip("Right zone. Always the Red player, right-aligned.")]
        public Text redStatsText;

        public Button action1Button;
        public Text action1Text;

        public Button action2Button;
        public Text action2Text;

        [SerializeField] private Color foulHeadlineColor = new Color(1f, 0.25f, 0.25f, 1f);

        private const TeamId HumanTeam = TeamId.Blue;

        /// <summary>
        /// The headline's normal colour, taken from whatever the scene was built
        /// with. Kept because a foul repaints the headline, and the NEXT duel has
        /// to open in the ordinary colour rather than inheriting the last foul's
        /// red.
        /// </summary>
        private Color headlineColor = Color.white;
        private bool hasHeadlineColor;

        private void Awake()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            if (clashText != null)
            {
                headlineColor = clashText.color;
                hasHeadlineColor = true;

                // Same reasoning as the colour above: a foul blows the headline
                // up, and the next duel has to open at the size the scene was
                // built with rather than inheriting it.
                baseHeadlineFontSize = clashText.fontSize;
                baseHeadlineFontStyle = clashText.fontStyle;
            }
        }

        public void ShowClash(TeamMember attacker, TeamMember defender, ClashType type)
        {
            if (attacker == null || defender == null)
            {
                return;
            }

            // Undo whatever a previous foul left behind. Without this the duel
            // after a foul opens with a red "¡FALTA!" headline and two dead
            // buttons, and there is no way out of it.
            if (clashText != null && hasHeadlineColor)
            {
                clashText.color = headlineColor;
                clashText.fontSize = baseHeadlineFontSize;
                clashText.fontStyle = baseHeadlineFontStyle;
            }

            SetActionsInteractable(true);

            bool humanAttacks = attacker.team == HumanTeam;

            WriteStatPanels(attacker, defender, type);

            if (type == ClashType.Shot)
            {
                ShowShotClash(attacker, defender, humanAttacks);
            }
            else
            {
                ShowTackleClash(attacker, defender, humanAttacks);
            }

            UIAnimator.Show(uiPanel);
        }

        public void HideClash()
        {
            UIAnimator.Hide(uiPanel);
        }

        /// <summary>
        /// Turns the open banner into a foul announcement while the referee's
        /// decision is held on screen.
        ///
        /// The buttons are disabled rather than hidden: the duel has already
        /// been decided by the press that got here, and leaving them live would
        /// let a second tap resolve a duel that no longer exists. Keeping them in
        /// place — greyed, but there — also stops the banner jumping about in the
        /// moment the player is reading it.
        /// </summary>
        public void ShowFoul(TeamMember offender)
        {
            if (clashText != null)
            {
                // Names the side rather than just the offence. "¡FALTA!" left the
                // player to work out from a frozen screen which of the two just
                // gave it away, which is the only thing about a foul that
                // actually matters to them.
                clashText.text = offender != null
                    ? Core.LocalizationManager.Format("clash.foulOf", Fouls.DescribeTeam(offender.team))
                    : Core.LocalizationManager.GetText("clash.foul");

                // Deliberately NOT the offender's own colour: a blue foul printed
                // in blue reads as a message FROM blue. The accusing colour is
                // the opposite one, so the headline is unmistakably about them
                // rather than theirs.
                clashText.color = offender != null
                    ? Fouls.AccusationColor(offender.team)
                    : foulHeadlineColor;

                // The headline is the whole point of this beat, so it is blown up
                // well past the duel text it replaces and put back on the way out.
                clashText.fontSize = Mathf.RoundToInt(baseHeadlineFontSize * FoulHeadlineScale);
                clashText.fontStyle = FontStyle.Bold;
            }

            SetActionsInteractable(false);
        }

        /// <summary>
        /// How much bigger the foul headline is than the duel text it replaces.
        /// A foul voids a duel the player has just committed to, so it has to be
        /// impossible to miss on a screen they were reading for other reasons.
        /// </summary>
        private const float FoulHeadlineScale = 2.2f;

        private int baseHeadlineFontSize;
        private FontStyle baseHeadlineFontStyle;

        private void SetActionsInteractable(bool interactable)
        {
            if (action1Button != null)
            {
                action1Button.interactable = interactable;
            }

            if (action2Button != null)
            {
                action2Button.interactable = interactable;
            }
        }

        /// <summary>
        /// Fills the two side zones, mapped by TEAM rather than by who is
        /// attacking. What each panel says still depends on the role that player
        /// is playing in this duel — a keeper's saving is the relevant number,
        /// not their dribbling — so the duel role decides the CONTENT and the
        /// team decides the SIDE.
        /// </summary>
        private void WriteStatPanels(TeamMember attacker, TeamMember defender, ClashType type)
        {
            bool attackerIsBlue = attacker.team == TeamId.Blue;

            TeamMember blue = attackerIsBlue ? attacker : defender;
            TeamMember red = attackerIsBlue ? defender : attacker;

            if (blueStatsText != null)
            {
                blueStatsText.text = Describe(Core.LocalizationManager.GetText("team.blue"),
                    blue, attackerIsBlue, type);
            }

            if (redStatsText != null)
            {
                redStatsText.text = Describe(Core.LocalizationManager.GetText("team.red"),
                    red, !attackerIsBlue, type);
            }
        }

        private static string Describe(string teamName, TeamMember member, bool isAttacker, ClashType type)
        {
            // Elements.Describe, not the enum: printing the enum prints its
            // Spanish IDENTIFIER, which is what left this line untranslated.
            string header = Core.LocalizationManager.Format("clash.side", teamName,
                PlayerRoles.Describe(member.role), Elements.Describe(member.element));

            if (member.isCaptain)
            {
                header += Core.LocalizationManager.GetText("clash.captain");
            }

            // Being blown is worth more than a stat line here: it is a 30% cut
            // to whatever number is printed underneath, and the player cannot
            // see the stamina bar with the banner covering the bottom third.
            if (member.IsExhausted)
            {
                header += Core.LocalizationManager.GetText("clash.exhaustedTag");
            }

            return $"{header}\n{DescribeAttributes(member, isAttacker, type)}";
        }

        private static string DescribeAttributes(TeamMember member, bool isAttacker, ClashType type)
        {
            // The attribute NAMES come from the same keys the squad board and
            // the player editor read, so a stat is called the same thing
            // wherever it appears; only the layout differs.
            if (type == ClashType.Shot)
            {
                return isAttacker
                    ? Attribute("stat.shoot", member.Shoot)
                    : Attribute("stat.goalkeeping", member.Goalkeeping);
            }

            return isAttacker
                ? AttributePair("stat.dribble", member.Dribble, "stat.power", member.Power)
                : AttributePair("stat.tackle", member.Tackle, "stat.block", member.Block);
        }

        private static string Attribute(string key, int value)
        {
            return Core.LocalizationManager.Format("clash.attrOne",
                Core.LocalizationManager.GetText(key), value);
        }

        private static string AttributePair(string firstKey, int first, string secondKey, int second)
        {
            return Core.LocalizationManager.Format("clash.attrPair",
                Core.LocalizationManager.GetText(firstKey), first,
                Core.LocalizationManager.GetText(secondKey), second);
        }

        /// <summary>
        /// Every caption carries the move it BEATS. The counter ring is the one
        /// piece of the duel maths a player cannot deduce from the stats, and
        /// leaving it undocumented turned reading the opponent into guessing.
        /// </summary>
        private void ShowTackleClash(TeamMember attacker, TeamMember defender, bool humanAttacks)
        {
            // Rolled once, up front: re-rolling per button press would let the
            // same duel produce different opposition depending on which button
            // the player happened to reach for.
            ClashAction aiAction = humanAttacks
                ? ClashManager.RandomDefenderAction()
                : ClashManager.RandomAttackerAction();

            if (clashText != null)
            {
                Core.LocalizationManager.Write(clashText, "clash.dribbleVsTackle");
            }

            if (humanAttacks)
            {
                BindAction(action1Button, action1Text, Caption("clash.action.dribble"),
                    attacker, defender, ClashAction.Dribble, aiAction, humanIsAttacker: true);
                BindAction(action2Button, action2Text, Caption("clash.action.power"),
                    attacker, defender, ClashAction.Power, aiAction, humanIsAttacker: true);
                return;
            }

            BindAction(action1Button, action1Text, Caption("clash.action.tackle"),
                attacker, defender, aiAction, ClashAction.Tackle, humanIsAttacker: false);
            BindAction(action2Button, action2Text, Caption("clash.action.block"),
                attacker, defender, aiAction, ClashAction.Block, humanIsAttacker: false);
        }

        private void ShowShotClash(TeamMember shooter, TeamMember keeper, bool humanShoots)
        {
            ClashAction aiAction = humanShoots
                ? ClashManager.RandomKeeperAction()
                : ClashManager.RandomShooterAction();

            if (clashText != null)
            {
                Core.LocalizationManager.Write(clashText, "clash.shotVsSave");
            }

            if (humanShoots)
            {
                BindAction(action1Button, action1Text, Caption("clash.action.powerShot"),
                    shooter, keeper, ClashAction.PowerShot, aiAction, humanIsAttacker: true);
                BindAction(action2Button, action2Text, Caption("clash.action.lobShot"),
                    shooter, keeper, ClashAction.LobShot, aiAction, humanIsAttacker: true);
                return;
            }

            BindAction(action1Button, action1Text, Caption("clash.action.punch"),
                shooter, keeper, aiAction, ClashAction.Punch, humanIsAttacker: false);
            BindAction(action2Button, action2Text, Caption("clash.action.catch"),
                shooter, keeper, aiAction, ClashAction.Catch, humanIsAttacker: false);
        }

        /// <summary>
        /// The move's name and what it beats, in the player's language. Read
        /// fresh on every duel rather than cached: the banner is rebuilt each
        /// time it opens, which is what makes a language change take effect
        /// without this screen having to listen for one.
        /// </summary>
        private static string Caption(string key)
        {
            return Core.LocalizationManager.GetText(key);
        }

        /// <summary>
        /// Labels a button and points it at one specific pair of actions.
        /// Listeners are cleared first: a lambda cannot be removed by reference,
        /// so every duel would otherwise stack another callback onto the button.
        /// </summary>
        private static void BindAction(Button button, Text label, string caption,
            TeamMember attacker, TeamMember defender,
            ClashAction attackerAction, ClashAction defenderAction,
            bool humanIsAttacker)
        {
            if (label != null)
            {
                // The foul risk is printed on the button because it is the half
                // of the choice the ring does not tell you. Charging beats a
                // tackle every time, so without the percentage there would be no
                // reason ever to pick anything else — the risk is what makes the
                // safe move worth taking on the edge of your own box.
                //
                // Only when there IS a risk. A shot duel cannot produce a foul,
                // so printing "(Falta: 0%)" on all four of its buttons would be
                // four lines of screen telling the player nothing — and would
                // suggest the number is a lever somewhere, when it is not.
                ClashAction humanAction = humanIsAttacker ? attackerAction : defenderAction;

                int foulChance = ClashManager.Instance != null
                    ? ClashManager.Instance.FoulChanceFor(humanAction)
                    : 0;

                label.text = foulChance > 0
                    ? Core.LocalizationManager.Format("clash.foulRisk", caption, foulChance)
                    : caption;
            }

            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                if (ClashManager.Instance != null)
                {
                    ClashManager.Instance.ResolveClash(attacker, defender, attackerAction, defenderAction);
                }
            });
        }
    }
}
