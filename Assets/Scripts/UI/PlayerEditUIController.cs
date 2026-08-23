using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    // Editor de un jugador: rol, elemento, estadísticas y estamina. Los cambios no se aplican hasta pulsar Guardar.
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

        // Se lanza cuando se ha guardado un cambio en un jugador, para que el tablero de plantilla se refresque.
        public static event System.Action<TeamMember> OnPlayerEdited;

        public static PlayerEditUIController Instance { get; private set; }

        // Cierto mientras el editor está abierto.
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

        // Inicializa el singleton y oculta el panel.
        private void Awake()
        {
            Instance = this;

            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        // Limpia la referencia al singleton al desactivarse.
        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Conecta todos los botones del editor con sus acciones.
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

        // Abre el editor sobre un jugador, guardando una copia de sus datos para poder cancelar sin aplicar nada.
        public void ShowEditor(TeamMember member, GameObject returnTo)
        {
            if (member == null || uiPanel == null)
            {
                return;
            }

            subject = member;
            returnPanel = returnTo;
            notice = string.Empty;

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

        // Cierra el editor sin guardar cambios y vuelve al panel anterior.
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

        // Aplica los cambios preparados al jugador y cierra el editor.
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

            TeamMember edited = subject;

            Close();

            OnPlayerEdited?.Invoke(edited);
        }

        // Cambia el rol del jugador; si el equipo no puede quedarse sin portero, muestra el aviso de rechazo.
        private void ApplyRole(TeamMember member, PlayerRole newRole)
        {
            if (!SquadRoles.TrySetRole(member, newRole, out string refusal))
            {
                notice = refusal;
            }
        }

        // Prepara un cambio de rol pendiente de guardar.
        private void StageRole(PlayerRole value)
        {
            role = value;
            notice = string.Empty;
            Refresh();
        }

        // Prepara un cambio de elemento pendiente de guardar.
        private void StageElement(Element value)
        {
            element = value;
            notice = string.Empty;
            Refresh();
        }

        // Ajusta una estadística dentro de sus límites.
        private void Nudge(ref int stat, int delta)
        {
            stat = Mathf.Clamp(stat + delta, TeamMember.StatMinimum, TeamMember.StatMaximum);
            Refresh();
        }

        // Ajusta la estamina máxima dentro de sus límites.
        private void NudgeStamina(float delta)
        {
            maxStamina = Mathf.Clamp(maxStamina + delta,
                TeamMember.StaminaMinimum, TeamMember.StaminaMaximum);

            Refresh();
        }

        // Redibuja toda la pantalla del editor con los valores actuales.
        private void Refresh()
        {
            if (subject == null)
            {
                return;
            }

            if (headingText != null)
            {
                Core.LocalizationManager.WriteFormatted(headingText, "edit.heading",
                    subject.jerseyNumber,
                    Fouls.DescribeTeam(subject.team),
                    PlayerRoles.Describe(subject.role));
            }

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

        // Escribe las etiquetas abreviadas de los botones de rol.
        private void WriteRoleCaptions()
        {
            Caption(roleGoalkeeperButton, PlayerRoles.Abbreviate(PlayerRole.Goalkeeper));
            Caption(roleDefenderButton, PlayerRoles.Abbreviate(PlayerRole.Defender));
            Caption(roleMidfielderButton, PlayerRoles.Abbreviate(PlayerRole.Midfielder));
            Caption(roleForwardButton, PlayerRoles.Abbreviate(PlayerRole.Forward));
        }

        // Escribe las etiquetas de los botones de elemento.
        private void WriteElementCaptions()
        {
            Caption(elementFireButton, DescribeElement(Element.Fuego));
            Caption(elementForestButton, DescribeElement(Element.Bosque));
            Caption(elementWindButton, DescribeElement(Element.Aire));
            Caption(elementMountainButton, DescribeElement(Element.Montaña));
        }

        // Texto de un elemento: su símbolo y su nombre.
        private static string DescribeElement(Element value)
        {
            return $"{Elements.Glyph(value)} {Elements.Describe(value)}";
        }

        // Pone el texto de un botón, aplicando la fuente que sabe dibujar los símbolos de elemento.
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
                LocalizationManager.ApplyFont(label);
            }
        }

        // Escribe el valor de una estadística en su fila correspondiente.
        private void WriteValue(int row, string value)
        {
            if (statValueTexts == null || row >= statValueTexts.Length || statValueTexts[row] == null)
            {
                return;
            }

            statValueTexts[row].text = value;
        }

        // Colorea un botón según si su opción está seleccionada.
        private void Tint(Button button, bool isSelected)
        {
            if (button == null || button.targetGraphic == null)
            {
                return;
            }

            button.targetGraphic.color = isSelected ? selectedColor : unselectedColor;
        }

        // Conecta un botón a una acción, evitando listeners duplicados.
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
