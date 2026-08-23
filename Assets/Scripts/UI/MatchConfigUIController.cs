using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    // Pantalla previa al partido: dificultad rival, formación rival, duración y equipación.
    public class MatchConfigUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Tooltip("Dificultad")]
        public Button easyButton;
        public Button normalButton;
        public Button hardButton;

        [Tooltip("Formación rival")]
        public Button rivalRandomButton;
        public Button rival222Button;
        public Button rival321Button;
        public Button rival132Button;

        [Tooltip("Duración")]
        public Button short45Button;
        public Button medium60Button;
        public Button long90Button;

        [Tooltip("Equipación")]
        public Button kitBlueButton;
        public Button kitGreenButton;
        public Button kitBlackButton;
        public Button kitWhiteButton;

        [Tooltip("Confirmación")]
        public Button continueButton;

        [Tooltip("Botón para volver al menú principal cancelando la configuración del partido.")]
        public Button backButton;

        [Tooltip("Texto que muestra el resumen de las opciones seleccionadas para el partido.")]
        public Text summaryText;

        [Tooltip("Referencia al menú de gestión de alineación y formación.")]
        public FormationUIController formationMenu;

        [SerializeField] private Color selectedColor = new Color(0.20f, 0.65f, 0.95f, 1f);
        [SerializeField] private Color unselectedColor = new Color(0.88f, 0.88f, 0.88f, 1f);

        private const float FrozenTimeScale = 0f;

        private AIDifficulty difficulty = AIDifficulty.Normal;
        private bool randomRivalShape = true;
        private FormationType rivalShape = FormationType.Balanced_2_2_2;
        private float halfDuration = 45f;
        private TeamKit kit = TeamKit.Azul;

        public static MatchConfigUIController Instance { get; private set; }

        // Cierto mientras la pantalla de configuración está abierta.
        public static bool IsOpen => Instance != null
            && Instance.uiPanel != null
            && Instance.uiPanel.activeSelf;

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

        // Conecta todos los botones de la pantalla con sus acciones.
        private void Start()
        {
            Bind(easyButton, () => SetDifficulty(AIDifficulty.Facil));
            Bind(normalButton, () => SetDifficulty(AIDifficulty.Normal));
            Bind(hardButton, () => SetDifficulty(AIDifficulty.Dificil));

            Bind(rivalRandomButton, SetRandomRivalShape);
            Bind(rival222Button, () => SetRivalShape(FormationType.Balanced_2_2_2));
            Bind(rival321Button, () => SetRivalShape(FormationType.Defensive_3_2_1));
            Bind(rival132Button, () => SetRivalShape(FormationType.Offensive_1_3_2));

            Bind(short45Button, () => SetDuration(45f));
            Bind(medium60Button, () => SetDuration(60f));
            Bind(long90Button, () => SetDuration(90f));

            Bind(kitBlueButton, () => SetKit(TeamKit.Azul));
            Bind(kitGreenButton, () => SetKit(TeamKit.Verde));
            Bind(kitBlackButton, () => SetKit(TeamKit.Negro));
            Bind(kitWhiteButton, () => SetKit(TeamKit.Blanco));

            Bind(backButton, GoBack);
            LiftAboveSiblings(backButton);

            if (continueButton != null)
            {
                continueButton.onClick.RemoveAllListeners();
                continueButton.onClick.AddListener(Continue);
            }
            else
            {
                Debug.LogError("MatchConfigUIController no tiene botón de continuar: " +
                               "no se podría pasar a las formaciones.");
            }

            RefreshFeedback();
        }

        // Abre la pantalla de configuración con el partido congelado.
        public void ShowMenu()
        {
            UIAnimator.Show(uiPanel);

            Time.timeScale = FrozenTimeScale;

            RefreshFeedback();
        }

        // Selecciona la dificultad del rival.
        public void SetDifficulty(AIDifficulty value)
        {
            difficulty = value;
            RefreshFeedback();
        }

        // Selecciona la duración de la parte.
        public void SetDuration(float seconds)
        {
            halfDuration = seconds;
            RefreshFeedback();
        }

        // Deja la formación del rival en aleatorio.
        public void SetRandomRivalShape()
        {
            randomRivalShape = true;
            RefreshFeedback();
        }

        // Fija una formación concreta para el rival.
        public void SetRivalShape(FormationType shape)
        {
            randomRivalShape = false;
            rivalShape = shape;
            RefreshFeedback();
        }

        // Selecciona la equipación del jugador humano.
        public void SetKit(TeamKit value)
        {
            kit = value;
            RefreshFeedback();
        }

        // Aplica la configuración elegida y pasa a la pantalla de formaciones.
        public void Continue()
        {
            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.ConfigureMatch(halfDuration, difficulty, randomRivalShape, rivalShape, kit);
            }
            else
            {
                Debug.LogError("No hay MatchManager: la configuración no se aplica.");
            }

            UIAnimator.Hide(uiPanel);

            FormationUIController menu = formationMenu != null
                ? formationMenu
                : FormationUIController.Instance;

            if (menu == null)
            {
                Debug.LogError("No hay pantalla de formaciones: el partido no puede empezar.");
                return;
            }

            menu.ShowMenu();
        }

        // Vuelve al menú principal, cancelando la configuración del partido.
        public void GoBack()
        {
            UIAnimator.Hide(uiPanel);

            if (TitleScreenUIController.Instance != null)
            {
                TitleScreenUIController.Instance.ShowTitle();
                return;
            }

            Debug.LogWarning("No hay pantalla de título a la que volver.");
        }

        // Pone un botón el último entre sus hermanos, para que nada se dibuje encima y bloquee sus toques.
        internal static void LiftAboveSiblings(Button button)
        {
            if (button != null)
            {
                button.transform.SetAsLastSibling();
            }
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

        // Actualiza el color de todos los botones y el texto resumen según la elección actual.
        private void RefreshFeedback()
        {
            Tint(easyButton, difficulty == AIDifficulty.Facil);
            Tint(normalButton, difficulty == AIDifficulty.Normal);
            Tint(hardButton, difficulty == AIDifficulty.Dificil);

            Tint(rivalRandomButton, randomRivalShape);
            Tint(rival222Button, !randomRivalShape && rivalShape == FormationType.Balanced_2_2_2);
            Tint(rival321Button, !randomRivalShape && rivalShape == FormationType.Defensive_3_2_1);
            Tint(rival132Button, !randomRivalShape && rivalShape == FormationType.Offensive_1_3_2);

            Tint(short45Button, Mathf.Approximately(halfDuration, 45f));
            Tint(medium60Button, Mathf.Approximately(halfDuration, 60f));
            Tint(long90Button, Mathf.Approximately(halfDuration, 90f));

            TintKit(kitBlueButton, TeamKit.Azul);
            TintKit(kitGreenButton, TeamKit.Verde);
            TintKit(kitBlackButton, TeamKit.Negro);
            TintKit(kitWhiteButton, TeamKit.Blanco);

            if (summaryText == null)
            {
                return;
            }

            string shape = randomRivalShape
                ? Core.LocalizationManager.GetText("config.randomShape")
                : Formations.GetLabel(rivalShape);

            Core.LocalizationManager.WriteFormatted(summaryText, "config.summary",
                DescribeDifficulty(difficulty), shape, halfDuration.ToString("F0"),
                TeamKits.GetLabel(kit));
        }

        // Nombre de la dificultad en el idioma actual.
        private static string DescribeDifficulty(AIDifficulty value)
        {
            switch (value)
            {
                case AIDifficulty.Facil: return Core.LocalizationManager.GetText("difficulty.easy");
                case AIDifficulty.Dificil: return Core.LocalizationManager.GetText("difficulty.hard");
                default: return Core.LocalizationManager.GetText("difficulty.normal");
            }
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

        // Pinta un botón de equipación con su propio color, atenuando los que no están elegidos.
        private void TintKit(Button button, TeamKit buttonKit)
        {
            if (button == null || button.targetGraphic == null)
            {
                return;
            }

            Color color = TeamKits.GetColor(buttonKit);

            button.targetGraphic.color = kit == buttonKit
                ? color
                : new Color(color.r * DimmedKit, color.g * DimmedKit, color.b * DimmedKit, 1f);
        }

        private const float DimmedKit = 0.45f;
    }
}
