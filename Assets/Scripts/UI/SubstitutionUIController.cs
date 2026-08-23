using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TacticalSoccer.AI;
using TacticalSoccer.Core;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.UI
{
    // Panel de sustituciones: muestra la plantilla completa de un equipo y permite intercambiar titulares por suplentes.
    public class SubstitutionUIController : MonoBehaviour
    {
        public GameObject uiPanel;

        [Header("Apertura")]
        [Tooltip("Returns to whatever opened this — in practice the interval.")]
        public Button closeButton;

        [Tooltip("Opens the editor on whichever player the board is currently " +
                 "showing. Optional: without one the board still swaps players, " +
                 "it just cannot retune them.")]
        public Button editButton;

        [Header("Textos")]
        public Text headerText;

        [Tooltip("Left-hand readout: who is selected, and everything about them.")]
        public Text statsText;

        [Header("Zonas")]
        [Tooltip("Mini-pitch the seven starters are laid out on, in their own shape.")]
        public RectTransform pitchArea;

        [Tooltip("Row the three substitutes sit in.")]
        public RectTransform benchArea;

        [Header("Equipo")]
        [SerializeField] private TeamId team = TeamId.Blue;

        [Header("Colores")]
        [SerializeField] private Color starterColor = new Color(0.86f, 0.88f, 0.92f, 1f);
        [SerializeField] private Color benchColor = new Color(0.62f, 0.65f, 0.70f, 1f);
        [SerializeField] private Color selectedColor = new Color(0.20f, 0.65f, 0.95f, 1f);
        [SerializeField] private Color exhaustedColor = new Color(0.92f, 0.45f, 0.35f, 1f);

        [Header("Medidas")]
        [SerializeField] private Vector2 slotSize = new Vector2(150f, 76f);
        [SerializeField] private int slotFontSize = 24;

        // Dimensiones del campo, usadas para ubicar los slots del mini-campo.
        private const float PitchHalfWidth = 15f;
        private const float PitchHalfLength = 25f;

        private const float FrozenTimeScale = 0f;
        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        private readonly List<TeamMember> squad = new List<TeamMember>();
        private readonly List<GameObject> slotObjects = new List<GameObject>();

        // Jugador tocado primero, a la espera de un compañero con quien intercambiarse.
        private TeamMember selected;

        // Jugador que se está mostrando actualmente en el panel de estadísticas.
        private TeamMember inspected;

        // Pantalla a la que volver al cerrar este panel.
        private GameObject returnPanel;

        // Último mensaje de aviso, mostrado bajo las estadísticas.
        private string notice = string.Empty;

        public static SubstitutionUIController Instance { get; private set; }

        // Si el panel de sustituciones está abierto actualmente.
        public static bool IsOpen { get; private set; }

        // Inicializa la instancia y oculta el panel.
        private void Awake()
        {
            Instance = this;

            IsOpen = false;

            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }
        }

        // Suscribe los eventos que este panel necesita escuchar.
        private void OnEnable()
        {
            TacticalEvents.OnMatchOver += HandleMatchOver;
            PlayerEditUIController.OnPlayerEdited += HandlePlayerEdited;
            Core.LocalizationManager.OnLanguageChanged += RepaintForLanguage;
        }

        // Redibuja el panel al cambiar de idioma.
        private void RepaintForLanguage()
        {
            if (uiPanel == null || !uiPanel.activeSelf)
            {
                return;
            }

            RebuildBoard();

            WriteStats(inspected);
        }

        // Desuscribe los eventos y limpia el estado al desactivarse.
        private void OnDisable()
        {
            TacticalEvents.OnMatchOver -= HandleMatchOver;
            PlayerEditUIController.OnPlayerEdited -= HandlePlayerEdited;
            Core.LocalizationManager.OnLanguageChanged -= RepaintForLanguage;

            if (Instance == this)
            {
                Instance = null;
            }

            IsOpen = false;
        }

        // Conecta los botones de cerrar y editar.
        private void Start()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(CloseBoard);
            }

            if (editButton != null)
            {
                editButton.onClick.RemoveAllListeners();
                editButton.onClick.AddListener(EditInspected);
                editButton.interactable = false;
            }
        }

        // Abre el panel de sustituciones y congela el partido.
        public void ShowBoard()
        {
            ShowBoard(null);
        }

        // Abre el panel de sustituciones, recordando a qué pantalla volver al cerrarlo.
        public void ShowBoard(GameObject returnTo)
        {
            if (uiPanel == null)
            {
                return;
            }

            if (ClashManager.IsClashActive)
            {
                Debug.Log("No se pueden hacer cambios en mitad de un duelo.");
                return;
            }

            if (returnTo == null && (!MatchManager.IsStarted || !MatchManager.IsPlayable))
            {
                return;
            }

            selected = null;
            notice = string.Empty;
            returnPanel = returnTo;

            CollectSquad();
            RebuildBoard();
            WriteStats(null);

            if (headerText != null)
            {
                Core.LocalizationManager.WriteFormatted(headerText, "subs.header", DescribeTeam(team));
            }

            uiPanel.SetActive(true);
            IsOpen = true;

            Time.timeScale = FrozenTimeScale;
        }

        // Cierra el panel y reanuda el partido, o vuelve a la pantalla anterior si venía de una.
        public void CloseBoard()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            IsOpen = false;
            selected = null;

            if (returnPanel != null)
            {
                returnPanel.SetActive(true);
                returnPanel = null;
                return;
            }

            if (!MatchManager.IsPlayable || !MatchManager.IsStarted || MatchManager.IsHalftime)
            {
                return;
            }

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;
        }

        // Cierra el panel al terminar el partido sin reanudar el tiempo.
        private void HandleMatchOver()
        {
            if (uiPanel != null)
            {
                uiPanel.SetActive(false);
            }

            IsOpen = false;
            selected = null;
            returnPanel = null;
        }

        // Redibuja el panel tras editar a un jugador desde su ficha.
        private void HandlePlayerEdited(TeamMember edited)
        {
            if (uiPanel == null || !uiPanel.activeSelf)
            {
                return;
            }

            CollectSquad();
            RebuildBoard();

            WriteStats(edited != null ? edited : inspected);
        }

        // Abre el editor sobre el jugador que se está mostrando actualmente.
        public void EditInspected()
        {
            if (inspected == null || PlayerEditUIController.Instance == null)
            {
                return;
            }

            PlayerEditUIController.Instance.ShowEditor(inspected, uiPanel);
        }

        // Pide al MatchManager que intercambie a dos jugadores entre el campo y el banquillo.
        public void SwapPlayers(TeamMember p1, TeamMember p2)
        {
            if (MatchManager.Instance == null)
            {
                Debug.LogWarning("No hay MatchManager: no se puede hacer el cambio.");
                return;
            }

            MatchManager.Instance.SwapPlayers(p1, p2);
        }

        // Devuelve la posición de formación de un jugador.
        private static Vector3 ResolveFormationSlot(TeamMember member)
        {
            return MatchManager.ResolveFormationSlot(member);
        }

        // Recoge y ordena por dorsal a todos los jugadores del equipo.
        private void CollectSquad()
        {
            squad.Clear();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team)
                {
                    continue;
                }

                squad.Add(member);
            }

            squad.Sort((a, b) => a.jerseyNumber.CompareTo(b.jerseyNumber));
        }

        // Destruye los slots actuales y crea de nuevo uno por cada jugador de la plantilla.
        private void RebuildBoard()
        {
            foreach (GameObject slot in slotObjects)
            {
                if (slot == null)
                {
                    continue;
                }

                slot.SetActive(false);
                Destroy(slot);
            }

            slotObjects.Clear();

            int benchIndex = 0;
            int benchCount = CountBench();

            foreach (TeamMember member in squad)
            {
                if (member.isStarter)
                {
                    CreateSlot(member, pitchArea, MapToPitch(member));
                    continue;
                }

                CreateSlot(member, benchArea, MapToBench(benchIndex, benchCount));
                benchIndex++;
            }
        }

        // Cuenta cuántos jugadores hay en el banquillo.
        private int CountBench()
        {
            int count = 0;

            foreach (TeamMember member in squad)
            {
                if (!member.isStarter)
                {
                    count++;
                }
            }

            return count;
        }

        // Calcula la posición de un titular en el mini-campo según su formación.
        private Vector2 MapToPitch(TeamMember member)
        {
            if (pitchArea == null)
            {
                return Vector2.zero;
            }

            Vector3 slot = ResolveFormationSlot(member);

            float ownSide = team == TeamId.Blue ? -1f : 1f;

            float depth = Mathf.Clamp01((slot.z * ownSide) / PitchHalfLength);
            float across = Mathf.Clamp(slot.x / PitchHalfWidth, -1f, 1f);

            Rect area = pitchArea.rect;

            float halfWidth = Mathf.Max(0f, (area.width - slotSize.x) * 0.5f);
            float halfHeight = Mathf.Max(0f, (area.height - slotSize.y) * 0.5f);

            return new Vector2(across * halfWidth, halfHeight - (depth * 2f * halfHeight));
        }

        // Calcula la posición de un suplente, repartidos de forma uniforme en la fila del banquillo.
        private Vector2 MapToBench(int index, int count)
        {
            if (benchArea == null || count <= 0)
            {
                return Vector2.zero;
            }

            Rect area = benchArea.rect;

            float step = area.width / count;
            float x = (-area.width * 0.5f) + (step * (index + 0.5f));

            return new Vector2(x, 0f);
        }

        // Crea el botón visual de un jugador en la posición indicada.
        private void CreateSlot(TeamMember member, RectTransform parent, Vector2 anchoredPosition)
        {
            if (parent == null)
            {
                return;
            }

            TeamMember captured = member;

            GameObject slotObject = UiSlotFactory.CreateSlot(
                parent,
                $"Slot {member.jerseyNumber}",
                anchoredPosition,
                slotSize,
                ResolveSlotColor(member),
                DescribeSlot(member),
                ResolveFont(),
                slotFontSize,
                () => HandleSlotClicked(captured));

            slotObjects.Add(slotObject);
        }

        // Usa la fuente del panel de estadísticas, o la fuente por defecto si no hay ninguna.
        private Font ResolveFont()
        {
            if (statsText != null && statsText.font != null)
            {
                return statsText.font;
            }

            return LocalizationManager.BuiltInFont;
        }

        // Elige el color del slot según si está seleccionado, agotado, o es titular o suplente.
        private Color ResolveSlotColor(TeamMember member)
        {
            if (member == selected)
            {
                return selectedColor;
            }

            if (member.IsExhausted)
            {
                return exhaustedColor;
            }

            return member.isStarter ? starterColor : benchColor;
        }

        // Construye el texto de un slot: dorsal, rol, brazalete y estamina.
        private static string DescribeSlot(TeamMember member)
        {
            int stamina = Mathf.RoundToInt(member.StaminaFraction * 100f);
            string armband = member.isCaptain
                ? Core.LocalizationManager.GetText("subs.captainShort")
                : string.Empty;

            return Core.LocalizationManager.Format("subs.slot", member.jerseyNumber,
                PlayerRoles.Abbreviate(member.role), armband, stamina);
        }

        // Gestiona el toque en un slot: selecciona un jugador o, si ya había uno seleccionado, hace el cambio.
        private void HandleSlotClicked(TeamMember member)
        {
            notice = string.Empty;

            if (selected == member)
            {
                selected = null;
                RefreshSlotVisuals();
                WriteStats(member);
                return;
            }

            if (selected == null || selected.isStarter == member.isStarter)
            {
                selected = member;
                RefreshSlotVisuals();
                WriteStats(member);
                return;
            }

            TeamMember outgoing = selected.isStarter ? selected : member;
            TeamMember incoming = selected.isStarter ? member : selected;

            selected = null;

            if (!TrySubstitute(outgoing, incoming))
            {
                RefreshSlotVisuals();
                WriteStats(member);
                return;
            }

            notice = $"Entra el {incoming.jerseyNumber} por el {outgoing.jerseyNumber}.";

            RebuildBoard();
            WriteStats(incoming);
        }

        // Comprueba si el cambio es válido: no se puede sustituir al portero ni a quien lleva el balón.
        private bool TrySubstitute(TeamMember outgoing, TeamMember incoming)
        {
            if (outgoing.isGoalkeeper || incoming.isGoalkeeper)
            {
                notice = "El portero no puede ser sustituido: no hay portero en el banquillo.";
                return false;
            }

            if (IsHoldingBall(outgoing))
            {
                notice = Core.LocalizationManager.GetText("subs.ballNotice");
                return false;
            }

            SwapPlayers(outgoing, incoming);

            return true;
        }

        // Indica si un jugador lleva actualmente el balón.
        private static bool IsHoldingBall(TeamMember member)
        {
            return member.TryGetComponent(out PlayerBallHandler handler) && handler.HasBall;
        }

        // Actualiza el color de los slots sin reconstruirlos, cuando solo cambia la selección.
        private void RefreshSlotVisuals()
        {
            int index = 0;

            foreach (TeamMember member in squad)
            {
                if (index >= slotObjects.Count)
                {
                    break;
                }

                GameObject slotObject = slotObjects[index];
                index++;

                if (slotObject == null || !slotObject.TryGetComponent(out Image background))
                {
                    continue;
                }

                background.color = ResolveSlotColor(member);
            }
        }

        // Rellena el panel de estadísticas con los datos del jugador indicado.
        private void WriteStats(TeamMember member)
        {
            inspected = member;

            if (editButton != null)
            {
                editButton.interactable = member != null;
            }

            if (statsText == null)
            {
                return;
            }

            if (member == null)
            {
                statsText.text = Core.LocalizationManager.GetText("subs.selectPlayer") + "\n\n" +
                    Core.LocalizationManager.GetText("subs.hint") + notice;
                return;
            }

            int stamina = Mathf.RoundToInt(member.currentStamina);
            int maxStamina = Mathf.RoundToInt(member.maxStamina);
            int percent = Mathf.RoundToInt(member.StaminaFraction * 100f);

            string status = Core.LocalizationManager.GetText(
                member.isStarter ? "subs.statusPitch" : "subs.statusBench");

            string exhausted = member.IsExhausted
                ? Core.LocalizationManager.GetText("subs.exhausted")
                : string.Empty;

            string armband = member.isCaptain
                ? Core.LocalizationManager.GetText("subs.captain")
                : string.Empty;

            string block =
                StatLine("stat.dribble", member.Dribble) +
                StatLine("stat.power", member.Power) +
                StatLine("stat.shoot", member.Shoot) +
                StatLine("stat.tackle", member.Tackle) +
                StatLine("stat.block", member.Block) +
                StatLine("stat.goalkeeping", member.Goalkeeping).TrimEnd('\n');

            statsText.text =
                Core.LocalizationManager.Format("subs.jersey", member.jerseyNumber) + armband + "\n" +
                $"{PlayerRoles.Describe(member.role)}  ·  {Elements.Describe(member.element)}\n" +
                $"{status}{exhausted}\n\n" +
                Core.LocalizationManager.Format("subs.staminaLine", stamina, maxStamina, percent) + "\n\n" +
                $"{block}\n\n" +
                notice;
        }

        // Formatea una línea de estadística con su nombre y valor.
        private static string StatLine(string key, int value)
        {
            return Core.LocalizationManager.Format("stat.line",
                Core.LocalizationManager.GetText(key), value) + "\n";
        }

        // Devuelve el nombre localizado de un equipo.
        private static string DescribeTeam(TeamId teamId)
        {
            return Core.LocalizationManager.GetText(teamId == TeamId.Blue ? "team.blue" : "team.red");
        }
    }
}
