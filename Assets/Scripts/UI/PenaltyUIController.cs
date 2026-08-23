using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    // Pantalla del penalti: el jugador elige un lado, sin estadísticas de por medio.
    public class PenaltyUIController : MonoBehaviour
    {
        public GameObject uiPanel;
        public Text headingText;
        public Text resultText;
        public Button leftButton;
        public Button rightButton;

        [Tooltip("Pausa de suspense tras la elección del jugador antes de mostrar la decisión rival.")]
        [SerializeField] private float suspenseSeconds = 1.5f;

        [Tooltip("Tiempo que permanece visible el resultado antes de continuar.")]
        [SerializeField] private float resultDwellSeconds = 1.5f;

        [Tooltip("Color del lado seleccionado por el jugador.")]
        [SerializeField] private Color chosenSideColor = new Color(1f, 0.85f, 0.25f, 1f);

        [Tooltip("Color del lado no seleccionado por el jugador.")]
        [SerializeField] private Color unchosenSideColor = new Color(0.45f, 0.45f, 0.48f, 1f);

        [Tooltip("Color inicial de los lados antes de realizar una elección.")]
        [SerializeField] private Color idleSideColor = new Color(0.88f, 0.88f, 0.88f, 1f);

        [Tooltip("Desviación del tiro hacia el poste como fracción del ancho de la portería.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float shotWidthShare = 0.7f;

        [Tooltip("Altura máxima alcanzada por el arco del disparo.")]
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

        // Cierto mientras el panel de penalti está abierto y el partido congelado.
        public static bool IsOpen { get; private set; }

        // Guarda la instancia y oculta el panel al iniciar.
        private void Awake()
        {
            Instance = this;
            IsOpen = false;

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

            IsOpen = false;
        }

        // Conecta los botones de izquierda y derecha.
        private void Start()
        {
            Bind(leftButton, PenaltySide.Left);
            Bind(rightButton, PenaltySide.Right);
        }

        // Lado hacia el que se lanza o se tira el penalti.
        private enum PenaltySide
        {
            Left,
            Right
        }

        // Abre el panel de penalti y congela el partido.
        public void ShowPenalty(TeamId team)
        {
            attackingTeam = team;

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

            ResetSides();

            if (uiPanel != null)
            {
                uiPanel.SetActive(true);
            }

            Time.timeScale = FrozenTimeScale;

            IsOpen = true;
        }

        // Conecta un botón a la elección de ese lado.
        private void Bind(Button button, PenaltySide side)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => Choose(side));
        }

        // Resuelve la elección del jugador contra un lado aleatorio de la IA.
        private void Choose(PenaltySide humanSide)
        {
            if (isResolving)
            {
                return;
            }

            isResolving = true;
            SetButtonsInteractable(false);

            PenaltySide aiSide = Random.value < 0.5f ? PenaltySide.Left : PenaltySide.Right;

            bool saved = humanSide == aiSide;

            Debug.Log($"[Penalti] {(humanIsStriker ? "Tirador humano" : "Portero humano")} elige {Describe(humanSide)}, " +
                      $"la IA elige {Describe(aiSide)} -> {(saved ? "PARADA" : "GOL")}.");

            StartCoroutine(ResolvePenaltyRoutine(humanSide, aiSide, saved));
        }

        // Traduce el lado a texto en el idioma del jugador.
        private static string Describe(PenaltySide side)
        {
            return Core.LocalizationManager.GetText(
                side == PenaltySide.Left ? "penalty.left" : "penalty.right");
        }

        // Anima a mano el vuelo del balón hacia la portería y la estirada del portero, en tiempo real.
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

            float aimX = SidePost(shotSide);
            float overshoot = Mathf.Sign(goal.z) * 0.8f;

            Vector3 to = new Vector3(aimX, from.y, goal.z + overshoot);

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

        // Posición en X, junto al poste, del lado indicado.
        private float SidePost(PenaltySide side)
        {
            float sideSign = side == PenaltySide.Left ? -1f : 1f;

            return sideSign * PitchBounds.GoalMouthHalfWidth * shotWidthShare;
        }

        // Fracción del vuelo en la que se comprime la estirada del portero.
        private const float KeeperDiveLead = 1.6f;

        // Muestra u oculta el panel (y opcionalmente los botones) mediante un CanvasGroup.
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

        // Resuelve el penalti paso a paso: elección, vuelo del balón y resultado.
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

            yield return new WaitForSecondsRealtime(choiceAcknowledgeSeconds);

            PenaltySide shotSide = humanIsStriker ? humanSide : aiSide;
            PenaltySide diveSide = humanIsStriker ? aiSide : humanSide;

            SetPanelVisible(false);

            yield return FlyBall(shotSide, diveSide);

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

        // Marca el gol y lanza la celebración normal, reutilizando el balón que ya está en la red.
        private void ScoreGoal()
        {
            TacticalEvents.OnGoalScored?.Invoke(
                attackingTeam == TeamId.Red ? ScoreManager.RedTeamId : ScoreManager.BlueTeamId);

            if (MatchManager.Instance != null)
            {
                MatchManager.Instance.CelebrateGoal();
            }
        }

        // Activa o desactiva los dos botones de elección.
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

        // Devuelve ambos botones a su color neutro.
        private void ResetSides()
        {
            RestoreSide(leftButton);
            RestoreSide(rightButton);
        }

        // Restaura el color y la transición normal de un botón.
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

        // Resalta el lado que ha elegido el jugador.
        private void HighlightChoice(PenaltySide side)
        {
            Paint(leftButton, side == PenaltySide.Left);
            Paint(rightButton, side == PenaltySide.Right);
        }

        // Pinta un botón con el color de elegido o no elegido, sin la transición por defecto.
        private void Paint(Button button, bool chosen)
        {
            if (button == null || button.targetGraphic == null)
            {
                return;
            }

            button.transition = Selectable.Transition.None;

            button.targetGraphic.color = chosen ? chosenSideColor : unchosenSideColor;
        }
    }
}
