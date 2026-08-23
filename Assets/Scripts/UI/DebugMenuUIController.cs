using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.UI
{
    // Menú de desarrollador para forzar estados del partido (tensión al máximo, curar stamina, terminar la parte, etc).
    public class DebugMenuUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Tooltip("The small gear icon that opens this on a single click.")]
        public Button openTrigger;

        [Header("Acciones")]
        public Button maxTensionButton;
        public Button healStaminaButton;
        public Button endHalfButton;

        [Tooltip("Opens the same audio options panel the title screen uses, so " +
                 "the mix can be adjusted mid-match instead of only before one.")]
        public Button audioOptionsButton;

        [Tooltip("Throws away every saved player edit. The only way back to the " +
                 "squad as generated, now that edits survive closing the game.")]
        public Button resetSquadButton;

        public Button closeButton;

        [Tooltip("Reads back what each action did, so a press that was refused " +
                 "does not look the same as one that worked.")]
        public Text feedbackText;

        private const float FrozenTimeScale = 0f;
        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        // TimeScale que había antes de abrir el menú, para restaurarlo al cerrar.
        private float restoreTimeScale = NormalTimeScale;

        public static DebugMenuUIController Instance { get; private set; }

        // True mientras el menú está abierto.
        public static bool IsOpen { get; private set; }

        private void Awake()
        {
            Instance = this;
            IsOpen = false;

            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            // Empieza oculto para no mostrar el icono un frame antes de comprobar IsReachable.
            if (openTrigger != null)
            {
                openTrigger.gameObject.SetActive(false);
            }
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            IsOpen = false;
        }

        // True solo si hay un partido en curso y no hay ninguna pantalla de menú abierta encima.
        private static bool IsReachable => MatchManager.Instance != null
            && MatchManager.IsStarted
            && MatchManager.IsPlayable
            && !TitleScreenUIController.IsOpen
            && !MatchConfigUIController.IsOpen
            && !FormationUIController.IsOpen;

        // Muestra u oculta el icono del menú según si el partido está en un estado alcanzable.
        private void Update()
        {
            if (openTrigger == null)
            {
                return;
            }

            bool reachable = IsReachable;

            if (openTrigger.gameObject.activeSelf != reachable)
            {
                openTrigger.gameObject.SetActive(reachable);
            }
        }

        // Engancha los listeners de todos los botones del menú.
        private void Start()
        {
            // Se limpian antes por si ya había listeners de una carga anterior.
            Bind(openTrigger, Open);
            Bind(maxTensionButton, MaxTension);
            Bind(healStaminaButton, HealStamina);
            Bind(endHalfButton, ForceEndOfHalf);
            Bind(audioOptionsButton, OpenAudioOptions);
            Bind(resetSquadButton, ResetSquad);
            Bind(closeButton, Close);
        }

        // Abre el panel de opciones de audio por encima del menú de desarrollador.
        public void OpenAudioOptions()
        {
            if (AudioSettingsUI.Instance != null)
            {
                AudioSettingsUI.Instance.ShowMenu();
                return;
            }

            Debug.LogWarning("No hay panel de opciones de audio en la escena.");
        }

        // Engancha una acción a un botón, quitando antes cualquier listener previo.
        private static void Bind(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        // Abre el menú de desarrollador y congela el partido.
        public void Open()
        {
            if (IsOpen)
            {
                return;
            }

            restoreTimeScale = Time.timeScale;

            if (uiPanel != null)
            {
                uiPanel.SetActive(true);
            }

            Time.timeScale = FrozenTimeScale;
            IsOpen = true;

            Report(Core.LocalizationManager.GetText("debug.opened"));
        }

        // Cierra el menú y devuelve el timeScale al valor que tenía antes de abrirlo.
        public void Close()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            IsOpen = false;

            Time.timeScale = restoreTimeScale;

            if (restoreTimeScale > 0f)
            {
                Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;
            }
        }

        // Llena la barra de tensión del equipo humano al máximo.
        private void MaxTension()
        {
            if (TensionManager.Instance == null)
            {
                Report(Core.LocalizationManager.GetText("debug.noTensionManager"));
                return;
            }

            TeamId team = MatchManager.Instance != null ? MatchManager.Instance.HumanTeam : TeamId.Blue;

            TensionManager.Instance.Add(team, TensionManager.Instance.MaxTension);

            Report(Core.LocalizationManager.Format(
                TensionManager.Instance.IsBurning(team) ? "debug.tensionIgnited" : "debug.tensionAlready",
                Fouls.DescribeTeam(team)));
        }

        // Rellena la stamina de todos los titulares del equipo humano.
        private void HealStamina()
        {
            TeamId team = MatchManager.Instance != null ? MatchManager.Instance.HumanTeam : TeamId.Blue;

            int healed = 0;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team || !member.isStarter)
                {
                    continue;
                }

                member.RefillStamina();
                healed++;
            }

            Report(Core.LocalizationManager.Format("debug.staminaHealed",
                healed, Fouls.DescribeTeam(team)));
        }

        // Fuerza el final de la parte actual.
        private void ForceEndOfHalf()
        {
            if (MatchManager.Instance == null)
            {
                Report(Core.LocalizationManager.GetText("debug.noMatchManager"));
                return;
            }

            // Hay que descongelar el partido para que la rutina de cierre pueda ejecutarse.
            Close();

            MatchManager.Instance.ForceEndOfHalf();
        }

        // Borra las ediciones guardadas de la plantilla; no afecta a los jugadores ya en el partido actual.
        private void ResetSquad()
        {
            int cleared = Core.SaveManager.Data.squad.Count;

            Core.SaveManager.ClearSquad();

            Report(cleared > 0
                ? Core.LocalizationManager.Format("debug.squadCleared", cleared)
                : Core.LocalizationManager.GetText("debug.squadEmpty"));
        }

        // Muestra un mensaje de feedback en el panel y en la consola.
        private void Report(string message)
        {
            Debug.Log($"[Debug] {message}");

            if (feedbackText != null)
            {
                feedbackText.text = message;
            }
        }
    }
}
