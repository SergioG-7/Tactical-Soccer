using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    public enum TeamId
    {
        Blue = 0,
        Red = 1
    }

    /// <summary>
    /// Where a player lives on the pitch when nobody is telling them otherwise.
    /// Read by the off-the-ball positioning, which shifts each line by a
    /// different amount as the ball travels.
    ///
    /// Defender is deliberately the zero value so a player who was never given
    /// a role holds a defensive shape rather than charging upfield — the safe
    /// failure, not the spectacular one.
    /// </summary>
    public enum PlayerRole
    {
        Defender,
        Midfielder,
        Forward,
        Goalkeeper
    }

    /// <summary>
    /// A player's elemental affinity. Purely a duel modifier: it decides nothing
    /// about how anybody moves or plays, only who has the edge when two of them
    /// meet.
    ///
    /// The ring is Fuego &gt; Bosque &gt; Aire &gt; Montaña &gt; Fuego, so every
    /// element beats exactly one and loses to exactly one. Two players of the
    /// same element, or of the two that do not face each other in the ring, get
    /// nothing — the bonus is meant to be an occasional edge, not a tax on every
    /// duel in the match.
    /// </summary>
    public enum Element
    {
        Fuego,
        Bosque,
        Aire,
        Montaña
    }

    /// <summary>
    /// The elemental ring, in one place. Kept next to the enum rather than in
    /// the duel maths: it is a fact about the elements, and the duel is only one
    /// of the things that will want to ask about it.
    /// </summary>
    public static class Elements
    {
        /// <summary>True when <paramref name="a"/> has the edge over <paramref name="b"/>.</summary>
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

        /// <summary>
        /// The single character an element is written with. A kanji rather than
        /// a word because the tag it goes on is two centimetres wide on screen
        /// and already carrying a number and a role — and because one glyph
        /// reads as a badge at a glance, which a truncated "Monta..." never
        /// would.
        /// </summary>
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

        /// <summary>
        /// The element's name in the player's language.
        ///
        /// Needed because the obvious thing — printing the enum — prints the
        /// IDENTIFIER, which is Spanish source code and stayed Spanish in every
        /// other language. Two screens were doing exactly that.
        /// </summary>
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

        /// <summary>
        /// The colour that element is read in. Returned as a hex string because
        /// every caller so far wants it inside a rich-text tag, and handing back
        /// a Color would mean each of them writing the same conversion.
        /// </summary>
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

    /// <summary>
    /// How a role is written for the player. Gathered here because three
    /// different screens were each carrying their own copy of these two switch
    /// statements, and they had already started to disagree.
    /// </summary>
    public static class PlayerRoles
    {
        /// <summary>
        /// Short tag for the floating label and the squad board.
        ///
        /// Deliberately NOT localised, and that is a decision rather than an
        /// oversight: GK/DF/MF/FW are read as notation — like a shirt number or
        /// a formation — and they are the same four tags on a team sheet in any
        /// country. Translating them to POR/DEF/MED/DEL made a player learn one
        /// set of tags on the pitch and a different set the moment they changed
        /// language. The long names below ARE translated: those are words, not
        /// symbols.
        /// </summary>
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

        /// <summary>Full name, for anywhere there is room for one.</summary>
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

    /// <summary>
    /// Who a player is, what they are worth in a duel, and how much they have
    /// left in the tank.
    ///
    /// Identity and stamina live together because stamina is the one piece of
    /// per-player state everything else has to ask about — the duel maths, the
    /// route speed and the floating label all read it, and none of them should
    /// have to know about movement or ball handling to get at it.
    ///
    /// The six stat properties are the ONLY way anything should read a player's
    /// numbers. They are not just a shortcut past a null check: the captain's
    /// passive is a bonus on top of the asset, and the asset itself is SHARED —
    /// every striker in the match points at one StrikerStats — so a captaincy
    /// written into the asset would buff both teams at once. Reading through
    /// here is what keeps the bonus attached to the player wearing it.
    ///
    /// The keeper flag lives here rather than being inferred from GoalkeeperAI
    /// so that gameplay code can find a keeper without taking a dependency on
    /// the AI layer — which already depends on gameplay.
    /// </summary>
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

        /// <summary>True while the player is too blown to run or duel properly.</summary>
        public bool IsExhausted => currentStamina <= exhaustedThreshold;

        /// <summary>Stamina as a 0..1 share, for anything drawing a bar.</summary>
        public float StaminaFraction => maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;

        public int Dribble => Attack(Raw(dribbleOverride, s => s.dribble));
        public int Power => Attack(Raw(powerOverride, s => s.power));
        public int Shoot => Attack(Raw(shootOverride, s => s.shoot));

        public int Tackle => Defence(Raw(tackleOverride, s => s.tackle));
        public int Block => Defence(Raw(blockOverride, s => s.block));
        public int Goalkeeping => Defence(Raw(goalkeepingOverride, s => s.goalkeeping));

        // Per-player edits, applied on top of the shared stat asset.
        //
        // They live HERE and not on the PlayerStatsSO, and that is not a style
        // choice: those assets are shared by every player of the same role on
        // BOTH sides, and they are files on disk. Writing an edit into one would
        // buff the opposition's midfielders by the same amount and persist the
        // change into the next match — and into the repository.
        //
        // NoOverride rather than a parallel "hasOverride" bool per stat: one
        // sentinel cannot get out of step with the value it guards.
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

        private int Raw(int over, System.Func<PlayerStatsSO, int> fromAsset)
        {
            if (over >= 0)
            {
                return over;
            }

            return stats != null ? fromAsset(stats) : DefaultStat;
        }

        /// <summary>The value the editor should show: the override if there is one, else the asset's.</summary>
        public int BaseDribble => Raw(dribbleOverride, s => s.dribble);
        public int BasePower => Raw(powerOverride, s => s.power);
        public int BaseShoot => Raw(shootOverride, s => s.shoot);
        public int BaseTackle => Raw(tackleOverride, s => s.tackle);
        public int BaseBlock => Raw(blockOverride, s => s.block);
        public int BaseGoalkeeping => Raw(goalkeepingOverride, s => s.goalkeeping);

        /// <summary>
        /// Writes this player's edited stats. Clamped rather than trusted: the
        /// editing panel is the only caller today, but a stat below zero would
        /// make a duel unwinnable in a way no amount of play could explain.
        /// </summary>
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

            // A player edited before kickoff should walk out with full tanks at
            // the NEW size, not carrying the old one's leftovers.
            currentStamina = maxStamina;
            exhaustedThreshold = maxStamina * ExhaustedShare;
        }

        public const int StatMinimum = 1;
        public const int StatMaximum = 99;
        public const float StaminaMinimum = 50f;
        public const float StaminaMaximum = 600f;

        // Kept in step with what the scene generator writes, so an edited player
        // blows at the same share of his tank as an untouched one.
        private const float ExhaustedShare = 0.2f;

        private int Attack(int raw)
        {
            return raw + captainAttackBonus;
        }

        private int Defence(int raw)
        {
            return raw + captainDefenceBonus;
        }

        /// <summary>Whether this player started the FIRST match on the pitch.</summary>
        public bool InitialIsStarter { get; private set; }

        /// <summary>Where this player stood before a ball had been kicked.</summary>
        public Vector3 InitialPosition { get; private set; }

        private void Awake()
        {
            currentStamina = maxStamina;

            // Snapshotted once, before anything can move anybody. A substitution
            // swaps two players' places AND their isStarter flags, and nothing
            // was remembering what the squad looked like before that — so a
            // second match inherited the first one's changes: the men who came
            // on were still on, and the men taken off were still in the dugout.
            InitialIsStarter = isStarter;
            InitialPosition = transform.position;

            route = GetComponent<Player.PlayerRoute>();
            handler = GetComponent<Player.PlayerBallHandler>();
        }

        /// <summary>
        /// Puts this player back to how they began the very first match: on the
        /// pitch or on the bench, in their original place, with a full tank.
        ///
        /// Used when a finished match is played again. The stamina refill alone
        /// was not enough — it topped everybody up but left the substitutions
        /// standing, so the fresh squad was the wrong eleven.
        /// </summary>
        public void RestoreInitialState()
        {
            isStarter = InitialIsStarter;
            transform.position = InitialPosition;

            RefillStamina();
        }

        /// <summary>
        /// Writes the captain's passive onto this player. Called for every member
        /// of a side whenever that side's armband moves, including the captain
        /// themselves — the bonus is a team bonus, and the captain is on the team.
        /// </summary>
        public void ApplyCaptainBonuses(int attackBonus, int defenceBonus, float drainMultiplier)
        {
            captainAttackBonus = attackBonus;
            captainDefenceBonus = defenceBonus;
            staminaDrainMultiplier = Mathf.Max(0f, drainMultiplier);
        }

        /// <summary>
        /// Burns stamina while a drawn route is being run. Nothing gives it
        /// back.
        ///
        /// There is no recovery on purpose: fatigue is cumulative for the whole
        /// match, so what a player spends in the first ten minutes he does not
        /// have in the last. That is what makes the bench matter — with recovery,
        /// standing still for a few seconds undid any amount of running and no
        /// substitution was ever worth making. The tank is only refilled by the
        /// whistle, through <see cref="RefillStamina"/>.
        ///
        /// Running a route is still the only thing that costs: the off-the-ball
        /// drift is a jog into space, not an effort, and charging for it would
        /// empty every player on the pitch without anybody having done anything.
        /// Scaled time on purpose — a duel freezes the match, and nobody should
        /// tire while the world is stopped.
        /// </summary>
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

        /// <summary>
        /// Puts the tank back to full. Used when a finished match is played
        /// again: with no recovery of its own, a squad that carried its fatigue
        /// into the next match would start the second one already spent.
        /// </summary>
        public void RefillStamina()
        {
            currentStamina = maxStamina;
        }
    }
}
