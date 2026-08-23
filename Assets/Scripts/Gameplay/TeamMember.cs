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

        [Tooltip("Shared stat asset. Several players may point at the same one.")]
        public PlayerStatsSO stats;

        [Header("Ficha")]
        [Tooltip("Squad number. Unique within a side: 1 is the keeper, 2-7 the " +
                 "rest of the starting seven, 8-10 the bench. It is the only " +
                 "stable name a player has — the role changes with the shape and " +
                 "the GameObject name is not something the UI should be reading.")]
        public int jerseyNumber;

        [Tooltip("False while this player is sitting in the dugout. A substitute " +
                 "is on the pitch as a GameObject but out of the match: no routes, " +
                 "no duels, no drift, and no restarts taken. Everything that " +
                 "picks a player out of the squad asks this first.")]
        public bool isStarter = true;

        [Tooltip("Line this player holds off the ball.")]
        public PlayerRole role = PlayerRole.Midfielder;

        [Tooltip("Marks the player who defends this team's goal.")]
        public bool isGoalkeeper = false;

        [Tooltip("Elemental affinity. Decides nothing but duels, where it is " +
                 "worth a flat bonus against the element it beats.")]
        public Element element = Element.Fuego;

        [Tooltip("Set by MatchManager, which owns the armband. Never write this " +
                 "directly — the captaincy also has to push its passive onto " +
                 "every team-mate, and a flag flipped by hand would light up the " +
                 "label without buffing anybody.")]
        public bool isCaptain;

        [Header("Estamina")]
        [Tooltip("A full tank, and it is the only one a player gets. At the " +
                 "running drain below this is about thirty seconds of continuous " +
                 "movement — most of a 45-second half — so a player who runs " +
                 "everything down is spent by the interval and has to be taken " +
                 "off rather than waited out.")]
        public float maxStamina = 300f;

        [Tooltip("Written every frame while the player runs. Seeded from " +
                 "maxStamina in Awake, so the serialized value is only what the " +
                 "inspector shows before play begins.")]
        public float currentStamina;

        [Tooltip("Drain per second while running WITH the ball. Carrying is the " +
                 "expensive thing to do: at 20 a fresh player is blown in five " +
                 "seconds of solo dribbling, which is the whole point — you are " +
                 "meant to pass.")]
        [SerializeField] private float carryingDrainPerSecond = 20f;

        [Tooltip("Drain per second while running without the ball. Half the " +
                 "carrying cost: making a run should be affordable.")]
        [SerializeField] private float runningDrainPerSecond = 10f;

        [Tooltip("At or below this, the player is blown: half pace and a penalty " +
                 "in every duel. A fifth of the tank — the share it was always " +
                 "meant to be. At 20 against a 300 tank it was 6.7%, which made " +
                 "being blown a state a player reached in the last seconds of a " +
                 "match if at all, and left the AI's interval substitutions " +
                 "almost never firing: IsExhausted is what selects who comes off.")]
        public float exhaustedThreshold = 60f;

        // Pushed in by MatchManager whenever the armband changes hands. Held as
        // three plain numbers rather than as "who the captain is" so that reading
        // a stat stays a field access: every duel reads six of these, and none of
        // them should be walking the squad to find out who is wearing it.
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

        [Header("Ajustes por jugador")]
        [Tooltip("-1 means 'use the shared stat asset'. Anything else replaces " +
                 "it for this player only.")]
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
