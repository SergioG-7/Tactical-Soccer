using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    // Muestra el panel del duelo congelado y recoge la elección táctica del jugador humano.
    public class ClashUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Tooltip("Título central del panel que indica el tipo de duelo en curso.")]
        public Text clashText;

        [Tooltip("Panel izquierdo para las estadísticas del jugador azul.")]
        public Text blueStatsText;

        [Tooltip("Panel derecho para las estadísticas del jugador rojo.")]
        public Text redStatsText;

        public Button action1Button;
        public Text action1Text;

        public Button action2Button;
        public Text action2Text;

        [SerializeField] private Color foulHeadlineColor = new Color(1f, 0.25f, 0.25f, 1f);

        private const TeamId HumanTeam = TeamId.Blue;

        // Color normal del titular, para restaurarlo tras pintarlo de rojo por una falta.
        private Color headlineColor = Color.white;
        private bool hasHeadlineColor;

        // Guarda el color y tamaño originales del titular para poder restaurarlos.
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

                baseHeadlineFontSize = clashText.fontSize;
                baseHeadlineFontStyle = clashText.fontStyle;
            }
        }

        // Abre el panel de duelo con las estadísticas y las dos opciones disponibles.
        public void ShowClash(TeamMember attacker, TeamMember defender, ClashType type)
        {
            if (attacker == null || defender == null)
            {
                return;
            }

            // Restaura el titular por si la falta anterior lo dejó en rojo.
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

        // Cierra el panel de duelo.
        public void HideClash()
        {
            UIAnimator.Hide(uiPanel);
        }

        // Convierte el panel abierto en un aviso de falta y desactiva los botones.
        public void ShowFoul(TeamMember offender)
        {
            if (clashText != null)
            {
                clashText.text = offender != null
                    ? Core.LocalizationManager.Format("clash.foulOf", Fouls.DescribeTeam(offender.team))
                    : Core.LocalizationManager.GetText("clash.foul");

                // Color acusador: el del equipo contrario al infractor.
                clashText.color = offender != null
                    ? Fouls.AccusationColor(offender.team)
                    : foulHeadlineColor;

                clashText.fontSize = Mathf.RoundToInt(baseHeadlineFontSize * FoulHeadlineScale);
                clashText.fontStyle = FontStyle.Bold;
            }

            SetActionsInteractable(false);
        }

        // Cuánto más grande es el titular de falta respecto al texto normal del duelo.
        private const float FoulHeadlineScale = 2.2f;

        private int baseHeadlineFontSize;
        private FontStyle baseHeadlineFontStyle;

        // Activa o desactiva los dos botones de acción.
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

        // Rellena los paneles de estadísticas de ambos lados, azul a la izquierda y rojo a la derecha.
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

        // Construye la cabecera de un jugador: equipo, rol, elemento, capitán y fatiga.
        private static string Describe(string teamName, TeamMember member, bool isAttacker, ClashType type)
        {
            string header = Core.LocalizationManager.Format("clash.side", teamName,
                PlayerRoles.Describe(member.role), Elements.Describe(member.element));

            if (member.isCaptain)
            {
                header += Core.LocalizationManager.GetText("clash.captain");
            }

            if (member.IsExhausted)
            {
                header += Core.LocalizationManager.GetText("clash.exhaustedTag");
            }

            return $"{header}\n{DescribeAttributes(member, isAttacker, type)}";
        }

        // Elige qué estadísticas mostrar según si el jugador ataca o defiende y el tipo de duelo.
        private static string DescribeAttributes(TeamMember member, bool isAttacker, ClashType type)
        {
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

        // Formatea un único atributo con su nombre y valor.
        private static string Attribute(string key, int value)
        {
            return Core.LocalizationManager.Format("clash.attrOne",
                Core.LocalizationManager.GetText(key), value);
        }

        // Formatea un par de atributos con sus nombres y valores.
        private static string AttributePair(string firstKey, int first, string secondKey, int second)
        {
            return Core.LocalizationManager.Format("clash.attrPair",
                Core.LocalizationManager.GetText(firstKey), first,
                Core.LocalizationManager.GetText(secondKey), second);
        }

        // Configura el panel para un duelo de regate/entrada, con las dos opciones del bando humano.
        private void ShowTackleClash(TeamMember attacker, TeamMember defender, bool humanAttacks)
        {
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

        // Configura el panel para un duelo de disparo/parada, con las dos opciones del bando humano.
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

        // Texto del nombre de una acción, en el idioma actual.
        private static string Caption(string key)
        {
            return Core.LocalizationManager.GetText(key);
        }

        // Pone el texto de un botón y lo conecta con el par de acciones que resuelve el duelo.
        private static void BindAction(Button button, Text label, string caption,
            TeamMember attacker, TeamMember defender,
            ClashAction attackerAction, ClashAction defenderAction,
            bool humanIsAttacker)
        {
            if (label != null)
            {
                // Solo se muestra el riesgo de falta cuando la acción puede provocarla.
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
