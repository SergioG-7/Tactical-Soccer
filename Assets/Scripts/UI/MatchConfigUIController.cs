using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    /// <summary>
    /// The pre-match screen: how hard the opposition plays, what shape it lines
    /// up in, and how long a half lasts.
    ///
    /// Sits between the title and the team sheet on purpose. Everything on it is
    /// a decision about the MATCH — the rules of the thing you are about to
    /// play — while the team sheet is about YOUR side, and mixing the two on one
    /// screen made picking a captain look like it might affect the opposition.
    ///
    /// Nothing here touches the pitch. The settings are written into
    /// MatchManager, which owns them, and the rival's shape is not laid out
    /// until the opening whistle — so "Aleatoria" cannot be read off the pitch
    /// behind the next menu.
    ///
    /// Lives on the canvas rather than on the panel it owns: a component on a
    /// deactivated GameObject never receives Start, and Start is where the
    /// buttons are wired.
    /// </summary>
    public class MatchConfigUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Header("Dificultad")]
        public Button easyButton;
        public Button normalButton;
        public Button hardButton;

        [Header("Formación rival")]
        public Button rivalRandomButton;
        public Button rival222Button;
        public Button rival321Button;
        public Button rival132Button;

        [Header("Duración")]
        public Button short45Button;
        public Button medium60Button;
        public Button long90Button;

        [Header("Equipación")]
        public Button kitBlueButton;
        public Button kitGreenButton;
        public Button kitBlackButton;
        public Button kitWhiteButton;

        [Header("Confirmación")]
        public Button continueButton;

        [Tooltip("Back to the main menu, cancelling the match being set up. " +
                 "Optional: without one this screen still works, it just has no " +
                 "way out except forwards.")]
        public Button backButton;

        [Tooltip("Reads back the three choices, so the screen answers what it is " +
                 "about to start without being pressed again.")]
        public Text summaryText;

        [Tooltip("The team sheet this screen hands over to.")]
        public FormationUIController formationMenu;

        [Header("Feedback")]
        [SerializeField] private Color selectedColor = new Color(0.20f, 0.65f, 0.95f, 1f);
        [SerializeField] private Color unselectedColor = new Color(0.88f, 0.88f, 0.88f, 1f);

        private const float FrozenTimeScale = 0f;

        private AIDifficulty difficulty = AIDifficulty.Normal;
        private bool randomRivalShape = true;
        private FormationType rivalShape = FormationType.Balanced_2_2_2;
        private float halfDuration = 45f;
        private TeamKit kit = TeamKit.Azul;

        public static MatchConfigUIController Instance { get; private set; }

        /// <summary>True while the settings screen is up. Read off the panel itself.</summary>
        public static bool IsOpen => Instance != null
            && Instance.uiPanel != null
            && Instance.uiPanel.activeSelf;

        private void Awake()
        {
            Instance = this;

            // Awake only runs in play mode, so this is what keeps the screen off
            // the pitch in the editor.
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
            // Cleared first: these listeners are added from code on every load,
            // and a duplicate would hand over to the team sheet twice.
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

        /// <summary>
        /// Opens the screen. Called by the title, which has already frozen the
        /// match; it stays frozen through here and through the team sheet.
        /// </summary>
        public void ShowMenu()
        {
            UIAnimator.Show(uiPanel);

            Time.timeScale = FrozenTimeScale;

            RefreshFeedback();
        }

        public void SetDifficulty(AIDifficulty value)
        {
            difficulty = value;
            RefreshFeedback();
        }

        public void SetDuration(float seconds)
        {
            halfDuration = seconds;
            RefreshFeedback();
        }

        public void SetRandomRivalShape()
        {
            randomRivalShape = true;
            RefreshFeedback();
        }

        public void SetRivalShape(FormationType shape)
        {
            randomRivalShape = false;
            rivalShape = shape;
            RefreshFeedback();
        }

        public void SetKit(TeamKit value)
        {
            kit = value;
            RefreshFeedback();
        }

        /// <summary>
        /// Writes the settings and hands over to the team sheet. Deliberately
        /// does NOT restore timeScale: the next screen is still a menu, and
        /// thawing between the two would run the pitch behind it.
        /// </summary>
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

        /// <summary>
        /// Back to the main menu, cancelling the match being set up.
        ///
        /// Nothing chosen on this screen has been written anywhere yet — the
        /// settings only reach MatchManager in Continue — so backing out really
        /// does cancel, and there is nothing to undo.
        ///
        /// The pitch stays frozen on the way out: the title is a modal like this
        /// one and freezes it again immediately, and thawing between the two
        /// would run a few frames of a match nobody has started.
        /// </summary>
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

        /// <summary>
        /// Puts a control last among its siblings, so nothing drawn afterwards
        /// can sit on top of it and swallow its taps.
        ///
        /// Only protects against siblings — a later sibling of the PANEL still
        /// draws over everything inside it, which is exactly how the developer
        /// menu's hidden corner used to eat the left half of this button. That
        /// one is fixed at its own end, by the corner standing down outside a
        /// match. This is the cheap half of the belt and braces.
        /// </summary>
        internal static void LiftAboveSiblings(Button button)
        {
            if (button != null)
            {
                button.transform.SetAsLastSibling();
            }
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

        private static string DescribeDifficulty(AIDifficulty value)
        {
            switch (value)
            {
                case AIDifficulty.Facil: return Core.LocalizationManager.GetText("difficulty.easy");
                case AIDifficulty.Dificil: return Core.LocalizationManager.GetText("difficulty.hard");
                default: return Core.LocalizationManager.GetText("difficulty.normal");
            }
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
        /// The kit buttons wear their own strip rather than the blue-or-grey
        /// tint the rest of the screen uses: "Verde" as a word tells you nothing
        /// about whether you will be able to pick your own players out at a
        /// glance, and the swatch is the entire decision being made.
        ///
        /// Selection is shown by dimming the others instead, so all four stay
        /// readable as colours while only one reads as chosen.
        /// </summary>
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
