using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    public enum TeamId
    {
        Blue = 0,
        Red = 1
    }

    // Línea que ocupa un jugador en el campo cuando no está haciendo nada especial.
    public enum PlayerRole
    {
        Defender,
        Midfielder,
        Forward,
        Goalkeeper
    }

    // Afinidad elemental de un jugador; solo influye en los duelos.
    public enum Element
    {
        Fuego,
        Bosque,
        Aire,
        Montaña
    }

    // Reglas del ciclo de ventajas entre elementos.
    public static class Elements
    {
        // Cierto si el elemento a tiene ventaja sobre el elemento b.
        public static bool Beats(Element a, Element b)
        {
            switch (a)
            {
                case Element.Fuego: return b == Element.Bosque;
                case Element.Bosque: return b == Element.Aire;
                case Element.Aire: return b == Element.Montaña;
                default: return b == Element.Fuego;
            }
        }

        // Carácter que representa a cada elemento en las etiquetas.
        public static string Glyph(Element element)
        {
            switch (element)
            {
                case Element.Fuego: return "火";
                case Element.Bosque: return "林";
                case Element.Aire: return "風";
                default: return "山";
            }
        }

        // Nombre del elemento en el idioma del jugador.
        public static string Describe(Element element)
        {
            switch (element)
            {
                case Element.Fuego: return Core.LocalizationManager.GetText("element.fire");
                case Element.Bosque: return Core.LocalizationManager.GetText("element.forest");
                case Element.Aire: return Core.LocalizationManager.GetText("element.wind");
                default: return Core.LocalizationManager.GetText("element.mountain");
            }
        }

        // Color del elemento en hexadecimal, para usar en etiquetas de texto enriquecido.
        public static string HexColor(Element element)
        {
            switch (element)
            {
                case Element.Fuego: return "FF3B30";
                case Element.Bosque: return "34C759";
                case Element.Aire: return "5AC8FA";
                default: return "D98B45";
            }
        }
    }

    // Cómo se escribe el rol de un jugador en pantalla.
    public static class PlayerRoles
    {
        // Etiqueta corta del rol (GK/DF/MF/FW), sin traducir.
        public static string Abbreviate(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Forward: return "FW";
                case PlayerRole.Midfielder: return "MF";
                case PlayerRole.Goalkeeper: return "GK";
                default: return "DF";
            }
        }

        // Nombre completo del rol, traducido.
        public static string Describe(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Forward: return Core.LocalizationManager.GetText("role.full.fw");
                case PlayerRole.Midfielder: return Core.LocalizationManager.GetText("role.full.mf");
                case PlayerRole.Goalkeeper: return Core.LocalizationManager.GetText("role.full.gk");
                default: return Core.LocalizationManager.GetText("role.full.df");
            }
        }
    }

    // Identidad, estadísticas y estamina de un jugador del equipo.
    public class TeamMember : MonoBehaviour
    {
        public TeamId team;

        [Tooltip("Asset compartido de estadísticas base.")]
        public PlayerStatsSO stats;

        [Tooltip("Dorsal único del jugador en el equipo.")]
        public int jerseyNumber;

        [Tooltip("Indica si el jugador forma parte del once inicial o está en el banquillo.")]
        public bool isStarter = true;

        [Tooltip("Rol táctico asignado en la formación.")]
        public PlayerRole role = PlayerRole.Midfielder;

        [Tooltip("Indica si el jugador ocupa la posición de portero.")]
        public bool isGoalkeeper = false;

        [Tooltip("Afinidad elemental para bonificaciones en duelos.")]
        public Element element = Element.Fuego;

        [Tooltip("Indica si el jugador porta el brazalete de capitán (gestionado por MatchManager).")]
        public bool isCaptain;

        [Tooltip("Reserva máxima total de energía del jugador.")]
        public float maxStamina = 300f;

        [Tooltip("Energía actual restante.")]
        public float currentStamina;

        [Tooltip("Consumo de energía por segundo al correr conduciendo el balón.")]
        [SerializeField] private float carryingDrainPerSecond = 20f;

        [Tooltip("Consumo de energía por segundo al correr sin balón.")]
        [SerializeField] private float runningDrainPerSecond = 10f;

        [Tooltip("Umbral de energía por debajo del cual el jugador entra en estado de agotamiento.")]
        public float exhaustedThreshold = 60f;

        // Bonificaciones de capitán asignadas por MatchManager para acceso rápido durante los duelos.
        [SerializeField] private int captainAttackBonus;
        [SerializeField] private int captainDefenceBonus;
        [SerializeField] private float staminaDrainMultiplier = 1f;

        private const int DefaultStat = 50;

        private Player.PlayerRoute route;
        private Player.PlayerBallHandler handler;

        // Cierto mientras el jugador está demasiado cansado para rendir bien.
        public bool IsExhausted => currentStamina <= exhaustedThreshold;

        // Estamina como fracción 0..1, para dibujar una barra.
        public float StaminaFraction => maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;

        // Estadísticas de ataque y defensa, con el override del jugador y el bonus de capitán aplicados.
        public int Dribble => Attack(Raw(dribbleOverride, s => s.dribble));
        public int Power => Attack(Raw(powerOverride, s => s.power));
        public int Shoot => Attack(Raw(shootOverride, s => s.shoot));

        public int Tackle => Defence(Raw(tackleOverride, s => s.tackle));
        public int Block => Defence(Raw(blockOverride, s => s.block));
        public int Goalkeeping => Defence(Raw(goalkeepingOverride, s => s.goalkeeping));

        // Ajustes por jugador sobre el asset de estadísticas compartido, que en sí no se puede tocar.
        private const int NoOverride = -1;

        [Tooltip("Modificador específico de regate (-1 para usar el valor del asset base).")]
        [SerializeField] private int dribbleOverride = NoOverride;
        [SerializeField] private int powerOverride = NoOverride;
        [SerializeField] private int shootOverride = NoOverride;
        [SerializeField] private int tackleOverride = NoOverride;
        [SerializeField] private int blockOverride = NoOverride;
        [SerializeField] private int goalkeepingOverride = NoOverride;

        // Devuelve el override si existe, si no el valor del asset compartido (o un valor por defecto sin asset).
        private int Raw(int over, System.Func<PlayerStatsSO, int> fromAsset)
        {
            if (over >= 0)
            {
                return over;
            }

            return stats != null ? fromAsset(stats) : DefaultStat;
        }

        // Valor sin el bonus de capitán, para mostrar en el editor.
        public int BaseDribble => Raw(dribbleOverride, s => s.dribble);
        public int BasePower => Raw(powerOverride, s => s.power);
        public int BaseShoot => Raw(shootOverride, s => s.shoot);
        public int BaseTackle => Raw(tackleOverride, s => s.tackle);
        public int BaseBlock => Raw(blockOverride, s => s.block);
        public int BaseGoalkeeping => Raw(goalkeepingOverride, s => s.goalkeeping);

        // Aplica las estadísticas editadas de este jugador, ajustadas a los límites válidos.
        public void ApplyStatEdits(int dribble, int power, int shoot,
            int tackle, int block, int goalkeeping, float newMaxStamina)
        {
            dribbleOverride = Mathf.Clamp(dribble, StatMinimum, StatMaximum);
            powerOverride = Mathf.Clamp(power, StatMinimum, StatMaximum);
            shootOverride = Mathf.Clamp(shoot, StatMinimum, StatMaximum);
            tackleOverride = Mathf.Clamp(tackle, StatMinimum, StatMaximum);
            blockOverride = Mathf.Clamp(block, StatMinimum, StatMaximum);
            goalkeepingOverride = Mathf.Clamp(goalkeeping, StatMinimum, StatMaximum);

            maxStamina = Mathf.Clamp(newMaxStamina, StaminaMinimum, StaminaMaximum);

            currentStamina = maxStamina;
            exhaustedThreshold = maxStamina * ExhaustedShare;
        }

        public const int StatMinimum = 1;
        public const int StatMaximum = 99;
        public const float StaminaMinimum = 50f;
        public const float StaminaMaximum = 600f;

        private const float ExhaustedShare = 0.2f;

        // Suma el bonus de capitán al ataque.
        private int Attack(int raw)
        {
            return raw + captainAttackBonus;
        }

        // Suma el bonus de capitán a la defensa.
        private int Defence(int raw)
        {
            return raw + captainDefenceBonus;
        }

        // Si este jugador empezó el primer partido en el campo.
        public bool InitialIsStarter { get; private set; }

        // Dónde estaba este jugador antes de que empezara a rodar el balón.
        public Vector3 InitialPosition { get; private set; }

        // Guarda la estamina inicial y el estado de partida del jugador.
        private void Awake()
        {
            currentStamina = maxStamina;

            InitialIsStarter = isStarter;
            InitialPosition = transform.position;

            route = GetComponent<Player.PlayerRoute>();
            handler = GetComponent<Player.PlayerBallHandler>();
        }

        // Devuelve al jugador a su estado inicial: en el campo o en el banquillo, en su sitio, con estamina llena.
        public void RestoreInitialState()
        {
            isStarter = InitialIsStarter;
            transform.position = InitialPosition;

            RefillStamina();
        }

        // Aplica los bonus del capitán a este jugador.
        public void ApplyCaptainBonuses(int attackBonus, int defenceBonus, float drainMultiplier)
        {
            captainAttackBonus = attackBonus;
            captainDefenceBonus = defenceBonus;
            staminaDrainMultiplier = Mathf.Max(0f, drainMultiplier);
        }

        // Consume estamina mientras el jugador sigue una ruta trazada. No se recupera sola.
        private void Update()
        {
            if (route == null || !route.IsFollowingRoute)
            {
                return;
            }

            bool carrying = handler != null && handler.HasBall;
            float drain = carrying ? carryingDrainPerSecond : runningDrainPerSecond;

            currentStamina = Mathf.Clamp(
                currentStamina - (drain * staminaDrainMultiplier * Time.deltaTime),
                0f, maxStamina);
        }

        // Rellena la estamina al máximo.
        public void RefillStamina()
        {
            currentStamina = maxStamina;
        }
    }
}
