using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    // Pantalla de la alineación: elegir formación y capitán antes de empezar el partido.
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

        // Cierto mientras el panel de alineación está visible.
        public static bool IsOpen => Instance != null
            && Instance.uiPanel != null
            && Instance.uiPanel.activeSelf;

        // Guarda la instancia y oculta el panel al iniciar.
        private void Awake()
        {
            Instance = this;

            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        // Limpia la instancia al desactivarse.
        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Abre el tablero de plantilla, ocultando este panel mientras tanto.
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

        // Vuelve al menú principal, abandonando la configuración del partido en curso.
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

        // Conecta los botones de formación y confirmación.
        private void Start()
        {
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

        // Muestra el panel de alineación, con el partido congelado.
        public void ShowMenu()
        {
            UIAnimator.Show(uiPanel);

            Time.timeScale = FrozenTimeScale;

            WriteFormationCaptions();

            RefreshSelectionFeedback();
            RebuildCaptainOptions();
        }

        // Aplica la formación y el capitán elegidos, y arranca el partido.
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

            MatchManager.Instance.ApplyFormation(team, selectedFormation);

            MatchManager.Instance.SetCaptain(team, ResolveCaptain(team));

            MatchManager.Instance.StartInitialKickoff();
        }

        // Cambia la formación seleccionada y la aplica al equipo del jugador.
        public void SelectFormation(FormationType formation)
        {
            selectedFormation = formation;

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.ApplyFormation(MatchManager.Instance.HumanTeam, formation);
            }

            RefreshSelectionFeedback();
            RebuildCaptainOptions();
        }

        // Devuelve el capitán elegido, o un titular de campo por defecto si no se ha elegido ninguno.
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

            List<TeamMember> starters = CollectStarters(team);

            return starters.Count > 0 ? starters[0] : null;
        }

        // Conecta un botón de formación a la selección de esa formación.
        private void BindFormation(Button button, FormationType formation)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SelectFormation(formation));
        }

        // Resalta el botón de la formación seleccionada y deja los demás sin resaltar.
        private void RefreshSelectionFeedback()
        {
            Tint(btn222, selectedFormation == FormationType.Balanced_2_2_2);
            Tint(btn321, selectedFormation == FormationType.Defensive_3_2_1);
            Tint(btn132, selectedFormation == FormationType.Offensive_1_3_2);
        }

        // Cambia el color de la imagen del botón según si está seleccionado.
        private void Tint(Button button, bool isSelected)
        {
            if (button == null || button.targetGraphic == null)
            {
                return;
            }

            button.targetGraphic.color = isSelected ? selectedColor : unselectedColor;
        }

        // Reconstruye la lista de candidatos a capitán a partir de los titulares actuales.
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

            RefreshCaptainHeading();
        }

        // Devuelve los titulares del equipo, ordenados por dorsal.
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

        // Crea el botón de un candidato a capitán en la fila.
        private void CreateCaptainSlot(TeamMember member, Vector2 anchoredPosition)
        {
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

        // Describe la ventaja pasiva que da la línea de este jugador si es capitán.
        private static string DescribePassive(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Forward: return Core.LocalizationManager.GetText("passive.attack");
                case PlayerRole.Midfielder: return Core.LocalizationManager.GetText("passive.stamina");
                default: return Core.LocalizationManager.GetText("passive.defence");
            }
        }

        // Escribe el texto de los tres botones de formación en el idioma actual.
        private void WriteFormationCaptions()
        {
            Caption(btn222, FormationType.Balanced_2_2_2, "formation.balanced");
            Caption(btn321, FormationType.Defensive_3_2_1, "formation.defensive");
            Caption(btn132, FormationType.Offensive_1_3_2, "formation.offensive");
        }

        // Escribe la etiqueta de un botón de formación.
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

        // Marca a un jugador como capitán elegido.
        public void SelectCaptain(TeamMember member)
        {
            selectedCaptain = member;

            RefreshCaptainFeedback();
            RefreshCaptainHeading();
        }

        // Actualiza el texto de cabecera con el capitán que se usaría ahora mismo.
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

        // Resalta el botón del capitán seleccionado entre los candidatos.
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

        // Devuelve la fuente a usar en los botones de capitán.
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
