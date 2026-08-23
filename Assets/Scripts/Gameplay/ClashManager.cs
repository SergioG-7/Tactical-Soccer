using UnityEngine;
using TacticalSoccer.Player;
using TacticalSoccer.UI;

namespace TacticalSoccer.Gameplay
{
    // La jugada que elige cada lado en un duelo.
    public enum ClashAction
    {
        Dribble,
        Power,
        Tackle,
        Block,
        PowerShot,
        LobShot,
        Catch,
        Punch,
        Pass,
        Intercept
    }

    // Tipo de duelo que se muestra en pantalla.
    public enum ClashType
    {
        Tackle,
        Shot
    }

    // Resuelve los duelos: entradas, tiros a puerta e intercepciones.
    public class ClashManager : MonoBehaviour
    {
        [Header("References")]
        public ClashUIController uiController;

        [Header("Tuning")]
        [Tooltip("How long the loser of a tackle duel stays frozen. Long enough " +
                 "that winning the ball actually buys you space to use it.")]
        [SerializeField] private float clashStunDuration = 2.5f;

        [Tooltip("How long a beaten keeper stays frozen. Long on purpose: the " +
                 "ball still has to travel, and a keeper who recovers mid-flight " +
                 "would simply catch the goal they just conceded.")]
        [SerializeField] private float beatenKeeperStunDuration = 3f;

        [Tooltip("How long a beaten interceptor stays frozen. This is what makes " +
                 "a failed interception let the ball through: without it the same " +
                 "player's trigger would simply collect the pass they had just " +
                 "been beaten by, on the very next contact tick.")]
        [SerializeField] private float failedInterceptStunDuration = 1.5f;

        [Tooltip("Real seconds after a clash ends before another one may start. " +
                 "Without it the two players, still overlapping, re-clash instantly.")]
        [SerializeField] private float clashCooldown = 1f;

        [Tooltip("Bonus applied to the side whose action counters the other's. " +
                 "Kept modest on purpose: at 1.5 an 80-shoot striker reached 120 " +
                 "against an 85 keeper, which no d20 could ever close, so reading " +
                 "the opponent decided the duel outright and the roll was decoration.")]
        [SerializeField] private float advantageMultiplier = 1.2f;

        [Tooltip("What an exhausted player's stat is worth in a duel. Sits " +
                 "between the two rings: being blown costs you more than reading " +
                 "the opponent wrong, which is what makes pacing the team matter.")]
        [SerializeField] private float exhaustedPenaltyMultiplier = 0.7f;

        [Tooltip("Flat bonus for holding the element that beats the opponent's. " +
                 "Flat rather than a multiplier so it is worth the same to a " +
                 "20-tackle striker as to an 80-tackle defender — an affinity is " +
                 "a matchup, not a measure of how good you already were.")]
        [SerializeField] private int elementalAdvantageBonus = 15;

        [Tooltip("Distance past the keeper the shot is aimed at, so a won duel " +
                 "sends the ball through the goal line rather than short of it.")]
        [SerializeField] private float goalAimOffset = 3f;

        [Tooltip("Share of the normal strike a saved shot is hit with. Every " +
                 "shot now flies for real, so a save is the same shot aimed AT " +
                 "the keeper and hit softly enough to be gathered. Too low and " +
                 "the ball dies short of him and turns into a loose ball in the " +
                 "six-yard box; too high and it goes through him.")]
        [SerializeField] private float savedShotForceScale = 0.65f;

        [Header("Cámara y Juice")]
        [Tooltip("How long the camera chases the struck ball before returning to " +
                 "the overhead view. The whole point of striking the ball for " +
                 "real: long enough to watch a lob drop and a drive arrive.")]
        [SerializeField] private float shotCinematicDuration = 1.5f;

        [SerializeField] private float clashShakeIntensity = 0.5f;
        [SerializeField] private float clashShakeDuration = 0.2f;

        [Header("Textos flotantes")]
        [Tooltip("Colour of the d20 each side rolled. Plain white: it is the " +
                 "number that decided the duel, and tinting it would make it " +
                 "compete with the modifiers stacked above it.")]
        [SerializeField] private Color rollTextColor = Color.white;

        [SerializeField] private Color advantageTextColor = new Color(0.35f, 1f, 0.45f, 1f);
        [SerializeField] private Color elementalTextColor = new Color(0.55f, 0.85f, 1f, 1f);
        [SerializeField] private Color exhaustedTextColor = new Color(1f, 0.30f, 0.25f, 1f);
        [SerializeField] private Color criticalTextColor = new Color(1f, 0.84f, 0.20f, 1f);
        [SerializeField] private Color interceptWonTextColor = new Color(0.30f, 1f, 0.40f, 1f);
        [SerializeField] private Color interceptLostTextColor = new Color(0.70f, 0.70f, 0.70f, 1f);

        [Tooltip("Size multiplier for the critical shout. Big enough that a " +
                 "natural 20 is unmistakable from the match camera.")]
        [SerializeField] private float criticalTextScale = 2.2f;

        [Tooltip("How many times normal size the foul shout is. The other duel " +
                 "messages are numbers read from the duel camera; this one has " +
                 "to be legible from the match camera, because it cancels the " +
                 "decision the player has just made.")]
        [SerializeField] private float foulTextScale = 3.5f;

        [Tooltip("How long the foul is held on the frozen duel before the panel " +
                 "closes and the restart is set up. Real seconds: the duel is " +
                 "holding timeScale at zero.")]
        [SerializeField] private float foulDwellSeconds = 1.5f;

        [Header("Riesgo de falta (%)")]
        [Tooltip("Chance that CHARGING gives away a foul. The highest in the " +
                 "game on purpose: Power is the move that beats a tackle, and " +
                 "this is what stops it being the answer to everything.")]
        [Range(0, 100)]
        [SerializeField] private int powerFoulChance = 30;

        [Tooltip("Chance that a TACKLE gives away a foul. Just under a charge: " +
                 "it is a challenge for the ball, not for the player.")]
        [Range(0, 100)]
        [SerializeField] private int tackleFoulChance = 25;

        [Tooltip("Chance that a DRIBBLE gives away a foul. Near enough clean — " +
                 "you are going round the man, not through him.")]
        [Range(0, 100)]
        [SerializeField] private int dribbleFoulChance = 5;

        [Tooltip("Chance that a BLOCK gives away a foul. As clean as dribbling, " +
                 "which is what makes it the move to pick inside your own box.")]
        [Range(0, 100)]
        [SerializeField] private int blockFoulChance = 5;

        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        // Rango del dado (1-20) que se suma a las estadísticas.
        private const int DiceMin = 1;
        private const int DiceMaxExclusive = 21;

        // Sacudida de cámara al sacar un crítico.
        private const float CriticalShakeIntensity = 0.5f;
        private const float CriticalShakeTime = 0.3f;

        // Un 20 natural gana el duelo automáticamente.
        private const int CriticalRoll = 20;

        // Niveles de apilado para los textos flotantes sobre la cabeza del jugador.
        private const int StackRoll = 0;
        private const int StackCounter = 1;
        private const int StackElement = 2;
        private const int StackFatigue = 3;
        private const int StackCritical = 4;

        private static float clashBlockedUntil;

        private ClashType currentClashType;

        public static ClashManager Instance { get; private set; }

        // True mientras el partido está congelado por un duelo.
        public static bool IsClashActive { get; private set; }

        // Indica si se puede iniciar un nuevo duelo ahora mismo.
        public static bool CanInitiateClash =>
            !IsClashActive
            && Time.unscaledTime >= clashBlockedUntil
            && !Core.MatchManager.IsEndingHalf
            && Core.MatchManager.IsPlayable;

        public TeamMember CurrentAttacker { get; private set; }
        public TeamMember CurrentDefender { get; private set; }
        public ClashType CurrentClashType => currentClashType;

        // Representa un lado del duelo ya calculado, listo para comparar.
        private struct DuelSide
        {
            public TeamMember Member;
            public ClashAction Action;
            public int BaseStat;
            public bool HasCounter;
            public bool HasElement;
            public bool IsBlown;
            public int Roll;
            public float Score;

            public bool IsCritical => Roll == CriticalRoll;
        }

        // Inicializa el singleton y resetea el estado estático de los duelos.
        private void Awake()
        {
            Instance = this;

            IsClashActive = false;
            clashBlockedUntil = 0f;
        }

        // Se suscribe a los eventos de duelo, tiro y fin de partido.
        private void OnEnable()
        {
            Core.TacticalEvents.OnClashInitiated += HandleClash;
            Core.TacticalEvents.OnShotInitiated += HandleShot;
            Core.TacticalEvents.OnMatchOver += HandleMatchOver;
        }

        // Se desuscribe de los eventos y corta cualquier duelo abierto al desactivarse.
        private void OnDisable()
        {
            Core.TacticalEvents.OnClashInitiated -= HandleClash;
            Core.TacticalEvents.OnShotInitiated -= HandleShot;
            Core.TacticalEvents.OnMatchOver -= HandleMatchOver;

            if (Instance == this)
            {
                Instance = null;
            }

            if (IsClashActive)
            {
                EndClash();
            }
        }

        // Elige al azar entre regatear o forzar con potencia.
        public static ClashAction RandomAttackerAction()
        {
            return Random.value < 0.5f ? ClashAction.Dribble : ClashAction.Power;
        }

        // Elige al azar entre entrar a por el balón o plantarse.
        public static ClashAction RandomDefenderAction()
        {
            return Random.value < 0.5f ? ClashAction.Tackle : ClashAction.Block;
        }

        // Elige al azar entre tiro potente o vaselina.
        public static ClashAction RandomShooterAction()
        {
            return Random.value < 0.5f ? ClashAction.PowerShot : ClashAction.LobShot;
        }

        // Elige al azar entre atajar o despejar con el puño.
        public static ClashAction RandomKeeperAction()
        {
            return Random.value < 0.5f ? ClashAction.Catch : ClashAction.Punch;
        }

        // Cierra cualquier duelo abierto cuando termina el partido.
        private void HandleMatchOver()
        {
            if (IsClashActive)
            {
                EndClash();
            }
        }

        // Comprueba que el balón lo tiene de verdad uno de los dos jugadores implicados.
        private static bool IsContestOverTheBall(TeamMember attacker, TeamMember defender)
        {
            BallController ball = BallController.Instance;

            if (ball == null)
            {
                return true;
            }

            GameObject holder = ball.Holder;

            if (holder == null)
            {
                Debug.Log("[Duelo] Abortado: el balón está suelto, no hay posesión que disputar.");

                return false;
            }

            if (holder == attacker.gameObject || holder == defender.gameObject)
            {
                return true;
            }

            Debug.Log($"[Duelo] Abortado: ni {attacker.name} ni {defender.name} tienen el balón " +
                      $"(lo lleva {holder.name}).");

            return false;
        }

        // Arranca un duelo de tipo entrada.
        private void HandleClash(TeamMember attacker, TeamMember defender)
        {
            BeginClash(attacker, defender, ClashType.Tackle);
        }

        // Arranca un duelo de tipo tiro a puerta.
        private void HandleShot(TeamMember shooter, TeamMember goalkeeper)
        {
            BeginClash(shooter, goalkeeper, ClashType.Shot);
        }

        // Congela el partido, mueve la cámara al duelo y abre el panel para elegir jugada.
        private void BeginClash(TeamMember attacker, TeamMember defender, ClashType type)
        {
            if (!CanInitiateClash || attacker == null || defender == null)
            {
                return;
            }

            if (!IsContestOverTheBall(attacker, defender))
            {
                return;
            }

            IsClashActive = true;
            currentClashType = type;
            CurrentAttacker = attacker;
            CurrentDefender = defender;

            Time.timeScale = 0f;

            Vector3 midPoint = (attacker.transform.position + defender.transform.position) * 0.5f;

            if (CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.ZoomToClash(attacker, defender);
                CameraSystem.TacticalCamera.Instance.Shake(clashShakeIntensity, clashShakeDuration);
            }

            if (VFX.VFXManager.Instance != null)
            {
                VFX.VFXManager.Instance.PlayClashImpact(midPoint);
            }

            if (type == ClashType.Shot)
            {
                Debug.Log($"¡TIRO A PUERTA! {attacker.team} (Tiro: {attacker.Shoot}) " +
                          $"VS {defender.team} (Parada: {defender.Goalkeeping})");
            }
            else
            {
                Debug.Log($"¡ENFRENTAMIENTO! {attacker.team} (Regate {attacker.Dribble} / Fuerza {attacker.Power}) " +
                          $"VS {defender.team} (Entrada {defender.Tackle} / Bloqueo {defender.Block})");
            }

            // Sin UI asignada se resuelve el duelo al azar para no bloquear la partida.
            if (uiController == null)
            {
                Debug.LogError("ClashManager no tiene uiController asignado. " +
                               "El duelo se resuelve al azar para no bloquear la partida.");

                ResolveClash(attacker, defender, DefaultAttackerAction(type), DefaultDefenderAction(type));

                return;
            }

            uiController.ShowClash(attacker, defender, type);
        }

        // Resuelve el duelo con las jugadas elegidas por cada lado y aplica el resultado.
        public void ResolveClash(TeamMember attacker, TeamMember defender,
            ClashAction attackerAction, ClashAction defenderAction)
        {
            if (!IsClashActive)
            {
                return;
            }

            ClashType type = currentClashType;

            if (attacker == null || defender == null)
            {
                EndClash();
                return;
            }

            bool attackerCounters = AttackerCounters(attackerAction, defenderAction);
            bool defenderCounters = DefenderCounters(attackerAction, defenderAction);

            DuelSide attackerSide = BuildSide(attacker, defender, attackerAction, attackerCounters, isAttacker: true);
            DuelSide defenderSide = BuildSide(defender, attacker, defenderAction, defenderCounters, isAttacker: false);

            bool defenderWins = ResolveWinner(attackerSide, defenderSide);

            PlayDuelFeedback(attackerSide, defenderSide);

            Debug.Log($"[{type}] {DescribeSide(attackerSide)}  |  {DescribeSide(defenderSide)}" +
                      $"  ->  gana {(defenderWins ? defenderSide.Member.team : attackerSide.Member.team)}");

            SpawnDuelFeedback(attackerSide);
            SpawnDuelFeedback(defenderSide);

            TeamMember offender = ResolveFoulOffender(attackerSide, defenderSide);

            if (offender != null)
            {
                StartCoroutine(CommitFoulRoutine(offender));
                return;
            }

            EndClash();

            AwardTension(attackerSide, defenderSide, defenderWins);

            if (type == ClashType.Shot)
            {
                ApplyShotOutcome(attacker, defender, attackerAction, defenderWins);
                return;
            }

            ApplyTackleOutcome(attacker, defender, defenderWins);
        }

        // Reproduce sonido y efectos del impacto del duelo, con un estallido especial si hay crítico.
        private static void PlayDuelFeedback(DuelSide attackerSide, DuelSide defenderSide)
        {
            bool isCritical = attackerSide.IsCritical || defenderSide.IsCritical;

            Vector3 midPoint = (attackerSide.Member.transform.position
                + defenderSide.Member.transform.position) * 0.5f;

            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PlayClashImpact();
            }

            if (VFX.VFXManager.Instance != null)
            {
                if (isCritical)
                {
                    VFX.VFXManager.Instance.PlayCriticalBurst(midPoint);
                }
                else
                {
                    VFX.VFXManager.Instance.PlayClashHit(midPoint);
                }
            }

            if (isCritical && CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.Shake(CriticalShakeIntensity, CriticalShakeTime);
            }
        }

        // Devuelve la probabilidad de falta de cada jugada, en porcentaje.
        public int FoulChanceFor(ClashAction action)
        {
            switch (action)
            {
                case ClashAction.Power: return powerFoulChance;
                case ClashAction.Tackle: return tackleFoulChance;
                case ClashAction.Dribble: return dribbleFoulChance;
                case ClashAction.Block: return blockFoulChance;

                default: return 0;
            }
        }

        // Sortea si hay falta y determina quién la comete, o null si el duelo es limpio.
        private TeamMember ResolveFoulOffender(DuelSide attackerSide, DuelSide defenderSide)
        {
            DuelSide offender = FoulChanceFor(defenderSide.Action) >= FoulChanceFor(attackerSide.Action)
                ? defenderSide
                : attackerSide;

            int chance = FoulChanceFor(offender.Action);

            if (chance <= 0)
            {
                return null;
            }

            int roll = Random.Range(0, 100);

            if (roll >= chance)
            {
                return null;
            }

            Debug.Log($"¡FALTA de {offender.Member.team} ({offender.Action})! " +
                      $"tirada {roll} < {chance}. El duelo queda anulado.");

            return offender.Member;
        }

        // Muestra la falta sobre el duelo congelado, espera un momento y luego cierra el panel.
        private System.Collections.IEnumerator CommitFoulRoutine(TeamMember offender)
        {
            Core.MatchManager.ClearDrawnRoutes();

            FloatingTextManager texts = FloatingTextManager.Instance;

            if (texts != null)
            {
                texts.SpawnText(offender.transform.position,
                    $"¡FALTA DE {Fouls.DescribeTeam(offender.team)}!",
                    Fouls.AccusationColor(offender.team),
                    StackCritical,
                    foulTextScale);
            }

            if (uiController != null)
            {
                uiController.ShowFoul(offender);
            }

            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PlayFoulWhistle();
            }

            yield return new WaitForSecondsRealtime(foulDwellSeconds);

            EndClash();

            Core.TacticalEvents.OnFoulCommitted?.Invoke(offender);
        }

        // Reparte tensión entre ganador y perdedor del duelo.
        private static void AwardTension(DuelSide attackerSide, DuelSide defenderSide, bool defenderWins)
        {
            TensionManager tension = TensionManager.Instance;

            if (tension == null)
            {
                return;
            }

            TeamMember winner = defenderWins ? defenderSide.Member : attackerSide.Member;
            TeamMember loser = defenderWins ? attackerSide.Member : defenderSide.Member;

            tension.AddDuelWon(winner.team);
            tension.AddDuelLost(loser.team);
        }

        // Resuelve una intercepción de pase al vuelo, sin congelar el partido ni abrir panel.
        // Devuelve true si el interceptor se queda con el balón.
        public bool ResolveRealTimeIntercept(GameObject passerObject, TeamMember interceptor)
        {
            if (interceptor == null || passerObject == null)
            {
                return false;
            }

            if (!passerObject.TryGetComponent(out TeamMember passer))
            {
                return false;
            }

            DuelSide passerSide = BuildSide(passer, interceptor, ClashAction.Pass, false, isAttacker: true);
            DuelSide interceptorSide = BuildSide(interceptor, passer, ClashAction.Intercept, false, isAttacker: false);

            bool interceptorWins = ResolveWinner(passerSide, interceptorSide);

            Debug.Log($"[Intercept] {DescribeSide(passerSide)}  |  {DescribeSide(interceptorSide)}");

            SpawnDuelFeedback(interceptorSide);

            FloatingTextManager texts = FloatingTextManager.Instance;
            Vector3 at = interceptor.transform.position;

            if (!interceptorWins)
            {
                if (interceptor.TryGetComponent(out PlayerRoute beatenRoute))
                {
                    beatenRoute.ApplyStun(failedInterceptStunDuration);
                }

                if (texts != null)
                {
                    texts.SpawnText(at, Core.LocalizationManager.GetText("clash.interceptFailed"),
                        interceptLostTextColor, StackCounter);
                }

                Debug.Log($"Intercepción fallida: {interceptor.name} no llega y el pase de " +
                          $"{passer.team} sigue su camino.");

                return false;
            }

            BallController ball = BallController.Instance;

            if (ball != null && interceptor.TryGetComponent(out PlayerBallHandler handler))
            {
                handler.ForceTakeBall(ball);
            }

            if (TensionManager.Instance != null)
            {
                TensionManager.Instance.AddIntercept(interceptor.team);
            }

            if (texts != null)
            {
                texts.SpawnText(at, Core.LocalizationManager.GetText("clash.intercepted"),
                    interceptWonTextColor, StackCounter);
            }

            Debug.Log($"¡INTERCEPTADO! {interceptor.name} corta el pase de {passer.team}.");

            return true;
        }

        // Calcula un lado del duelo: estadística base, modificadores y tirada de dado.
        private DuelSide BuildSide(TeamMember member, TeamMember opponent,
            ClashAction action, bool hasCounter, bool isAttacker)
        {
            DuelSide side = new DuelSide
            {
                Member = member,
                Action = action,
                HasCounter = hasCounter,
                HasElement = Elements.Beats(member.element, opponent.element),
                IsBlown = member.IsExhausted,
                Roll = Random.Range(DiceMin, DiceMaxExclusive)
            };

            int raw = isAttacker ? AttackerStat(member, action) : DefenderStat(member, action);

            side.BaseStat = raw
                + (side.HasElement ? elementalAdvantageBonus : 0)
                + DifficultyModifier(member)
                + TensionModifier(member);

            float afterCounter = side.BaseStat * (hasCounter ? advantageMultiplier : 1f);

            float afterFatigue = afterCounter * (side.IsBlown ? exhaustedPenaltyMultiplier : 1f);

            side.Score = afterFatigue + side.Roll;

            return side;
        }

        // Decide quién gana el duelo comparando críticos y, si no hay, la puntuación total.
        private static bool ResolveWinner(DuelSide attacker, DuelSide defender)
        {
            if (attacker.IsCritical != defender.IsCritical)
            {
                return defender.IsCritical;
            }

            return defender.Score >= attacker.Score;
        }

        // Construye la línea de log con la jugada, la base y los modificadores de un lado del duelo.
        private static string DescribeSide(DuelSide side)
        {
            string modifiers = string.Empty;

            if (side.HasCounter)
            {
                modifiers += " VENTAJA";
            }

            if (side.HasElement)
            {
                modifiers += " ELEMENTAL";
            }

            if (side.IsBlown)
            {
                modifiers += " AGOTADO";
            }

            if (side.IsCritical)
            {
                modifiers += " CRÍTICO";
            }

            return $"{side.Member.team} usa {side.Action} (base {side.BaseStat}{modifiers}) " +
                   $"+ d20 {side.Roll} = {side.Score:F1}";
        }

        // Muestra sobre el jugador los textos flotantes con la tirada y los modificadores aplicados.
        private void SpawnDuelFeedback(DuelSide side)
        {
            FloatingTextManager texts = FloatingTextManager.Instance;

            if (texts == null || side.Member == null)
            {
                return;
            }

            Vector3 at = side.Member.transform.position;

            texts.SpawnText(at, side.Roll.ToString(), rollTextColor, StackRoll);

            if (side.HasCounter)
            {
                texts.SpawnText(at, Core.LocalizationManager.GetText("clash.advantage"),
                    advantageTextColor, StackCounter);
            }

            if (side.HasElement)
            {
                texts.SpawnText(at, Core.LocalizationManager.GetText("clash.elemental"),
                    elementalTextColor, StackElement);
            }

            if (side.IsBlown)
            {
                texts.SpawnText(at, Core.LocalizationManager.GetText("clash.exhaustedShout"),
                    exhaustedTextColor, StackFatigue);
            }

            if (side.IsCritical)
            {
                texts.SpawnText(at, Core.LocalizationManager.GetText("clash.critical"),
                    criticalTextColor, StackCritical, criticalTextScale);
            }
        }

        // Bonificación de estadística que da la dificultad elegida a la IA.
        private static int DifficultyModifier(TeamMember member)
        {
            return Core.MatchManager.Instance != null
                ? Core.MatchManager.Instance.DuelModifierFor(member.team)
                : 0;
        }

        // Bonificación de estadística por tensión acumulada del equipo.
        private static int TensionModifier(TeamMember member)
        {
            return TensionManager.Instance != null
                ? TensionManager.Instance.DuelBonus(member.team)
                : 0;
        }

        // Resuelve el duelo actual con jugadas aleatorias para ambos lados.
        public void ResolveClash()
        {
            ResolveClash(CurrentAttacker, CurrentDefender,
                DefaultAttackerAction(currentClashType),
                DefaultDefenderAction(currentClashType));
        }

        private static ClashAction DefaultAttackerAction(ClashType type)
        {
            return type == ClashType.Shot ? RandomShooterAction() : RandomAttackerAction();
        }

        private static ClashAction DefaultDefenderAction(ClashType type)
        {
            return type == ClashType.Shot ? RandomKeeperAction() : RandomDefenderAction();
        }

        // Aplica el resultado de un duelo de entrada: quién se queda con el balón y quién queda aturdido.
        private void ApplyTackleOutcome(TeamMember attacker, TeamMember defender, bool defenderWins)
        {
            PlayerBallHandler attackerHandler = attacker.GetComponent<PlayerBallHandler>();
            PlayerBallHandler defenderHandler = defender.GetComponent<PlayerBallHandler>();
            PlayerRoute attackerRoute = attacker.GetComponent<PlayerRoute>();
            PlayerRoute defenderRoute = defender.GetComponent<PlayerRoute>();

            if (defenderWins)
            {
                if (defenderHandler != null && attackerHandler != null)
                {
                    defenderHandler.WinBallFrom(attackerHandler);
                }

                if (attackerRoute != null)
                {
                    attackerRoute.ApplyStun(clashStunDuration);
                }

                Debug.Log($"Clash resuelto: gana el defensor ({defender.team}). Balón robado.");
                return;
            }

            if (defenderRoute != null)
            {
                defenderRoute.ApplyStun(clashStunDuration);
            }

            Debug.Log($"Clash resuelto: gana el atacante ({attacker.team}). Conserva el balón.");
        }

        // Aplica el resultado del tiro: golpea el balón hacia la portería o hacia el portero según quién gane.
        private void ApplyShotOutcome(TeamMember shooter, TeamMember goalkeeper,
            ClashAction shotAction, bool keeperWins)
        {
            PlayerBallHandler shooterHandler = shooter.GetComponent<PlayerBallHandler>();
            PlayerRoute shooterRoute = shooter.GetComponent<PlayerRoute>();
            PlayerRoute keeperRoute = goalkeeper.GetComponent<PlayerRoute>();

            Vector3 aim = keeperWins ? goalkeeper.transform.position : CalculateGoalAim(goalkeeper);
            float forceScale = keeperWins ? savedShotForceScale : 1f;

            if (keeperWins)
            {
                if (shooterRoute != null)
                {
                    shooterRoute.ApplyStun(clashStunDuration);
                }

                Debug.Log($"¡PARADA! El portero ({goalkeeper.team}) lee el remate: " +
                          $"el balón sale flojo hacia él.");
            }
            else
            {
                if (keeperRoute != null)
                {
                    keeperRoute.ApplyStun(beatenKeeperStunDuration);
                }

                Debug.Log($"¡GOL CANTADO! {shooter.team} bate al portero con {shotAction}.");
            }

            if (shooterHandler != null)
            {
                shooterHandler.ExecutePhysicalKick(shotAction, aim, forceScale);
            }

            if (CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.FollowBallCinematic(shotCinematicDuration);
            }
        }

        // Calcula el punto de la portería al que apuntar, un poco más allá del portero.
        private Vector3 CalculateGoalAim(TeamMember goalkeeper)
        {
            Vector3 keeperPosition = goalkeeper.transform.position;
            float side = Mathf.Sign(keeperPosition.z);

            return new Vector3(0f, 0.5f, keeperPosition.z + (side * goalAimOffset));
        }

        // Cierra el duelo: oculta el panel, reanuda el tiempo y activa el cooldown.
        private void EndClash()
        {
            IsClashActive = false;
            CurrentAttacker = null;
            CurrentDefender = null;

            clashBlockedUntil = Time.unscaledTime + clashCooldown;

            if (uiController != null)
            {
                uiController.HideClash();
            }

            if (CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.ResetToOverhead();
            }

            if (!Core.MatchManager.IsPlayable)
            {
                return;
            }

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;
        }

        // Devuelve la estadística del atacante que corresponde a la jugada elegida.
        private static int AttackerStat(TeamMember attacker, ClashAction action)
        {
            switch (action)
            {
                case ClashAction.Power: return attacker.Power;

                case ClashAction.PowerShot:
                case ClashAction.LobShot: return attacker.Shoot;

                default: return attacker.Dribble;
            }
        }

        // Devuelve la estadística del defensor que corresponde a la jugada elegida.
        private static int DefenderStat(TeamMember defender, ClashAction action)
        {
            switch (action)
            {
                case ClashAction.Block: return defender.Block;

                case ClashAction.Catch:
                case ClashAction.Punch: return defender.Goalkeeping;

                default: return defender.Tackle;
            }
        }

        // Comprueba si la jugada del atacante contrarresta la del defensor.
        private static bool AttackerCounters(ClashAction attackerAction, ClashAction defenderAction)
        {
            return (attackerAction == ClashAction.Dribble && defenderAction == ClashAction.Block)
                || (attackerAction == ClashAction.Power && defenderAction == ClashAction.Tackle)
                || (attackerAction == ClashAction.LobShot && defenderAction == ClashAction.Catch)
                || (attackerAction == ClashAction.PowerShot && defenderAction == ClashAction.Punch);
        }

        // Comprueba si la jugada del defensor contrarresta la del atacante.
        private static bool DefenderCounters(ClashAction attackerAction, ClashAction defenderAction)
        {
            return (defenderAction == ClashAction.Tackle && attackerAction == ClashAction.Dribble)
                || (defenderAction == ClashAction.Block && attackerAction == ClashAction.Power)
                || (defenderAction == ClashAction.Catch && attackerAction == ClashAction.PowerShot)
                || (defenderAction == ClashAction.Punch && attackerAction == ClashAction.LobShot);
        }
    }
}
