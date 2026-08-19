using UnityEngine;
using TacticalSoccer.Player;
using TacticalSoccer.UI;

namespace TacticalSoccer.Gameplay
{
    /// <summary>
    /// The move each side commits to in a duel.
    ///
    /// Tackle duels ring: Dribble &gt; Block &gt; Power &gt; Tackle &gt; Dribble.
    /// Shot duels ring: LobShot &gt; Catch &gt; PowerShot &gt; Punch &gt; LobShot.
    /// In both, every pairing hands exactly one side the advantage, so no
    /// single choice is safe.
    ///
    /// Pass and Intercept have no ring and no panel: an interception is settled
    /// in the air, in real time, so there is nothing to choose and nothing to
    /// counter. They survive as actions only because the stat lookup is keyed on
    /// the action, and reading a pass still has to map to a number.
    /// </summary>
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

    /// <summary>
    /// Which kind of duel is on screen. Interceptions are deliberately absent:
    /// they no longer freeze the match or open a panel, so there is no such
    /// thing as an interception being "on screen".
    /// </summary>
    public enum ClashType
    {
        Tackle,
        Shot
    }

    /// <summary>
    /// Settles the contests. Two of them freeze the match and ask the player a
    /// question — a challenge on the carrier, and a shot on goal — and one, the
    /// interception, is resolved on the spot without stopping anything.
    ///
    /// Every one of them goes through the same maths: the stat for the chosen
    /// move, plus the elemental edge, plus the counter, minus fatigue, plus a
    /// d20 — and a natural 20 beats all of it.
    /// </summary>
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

        // Inclusive 1, exclusive 21 — a d20 on top of the stats, big enough that
        // an underdog can steal one but not big enough to drown the stats out.
        private const int DiceMin = 1;
        private const int DiceMaxExclusive = 21;

        // The kick the camera takes on a natural 20. Note the order: the camera
        // takes (intensity, time), so this is a hard 0.5 lasting a short 0.3 —
        // sharper and stronger than the goal shake, which is the other way round.
        private const float CriticalShakeIntensity = 0.5f;
        private const float CriticalShakeTime = 0.3f;

        /// <summary>A natural 20 wins outright, whatever the numbers said.</summary>
        private const int CriticalRoll = 20;

        // Stacking levels for the readout over a player's head. Named because
        // the whole point of the stack is that no two of them collide, and a
        // literal 2 in three different methods is exactly how they start to.
        private const int StackRoll = 0;
        private const int StackCounter = 1;
        private const int StackElement = 2;
        private const int StackFatigue = 3;
        private const int StackCritical = 4;

        private static float clashBlockedUntil;

        private ClashType currentClashType;

        public static ClashManager Instance { get; private set; }

        /// <summary>True while the match is frozen for a duel.</summary>
        public static bool IsClashActive { get; private set; }

        /// <summary>
        /// Gate for anything that wants to start a duel. Unscaled time on
        /// purpose: the cooldown has to keep running while timeScale is zero.
        /// </summary>
        public static bool CanInitiateClash =>
            !IsClashActive
            && Time.unscaledTime >= clashBlockedUntil
            && !Core.MatchManager.IsEndingHalf
            && Core.MatchManager.IsPlayable;

        public TeamMember CurrentAttacker { get; private set; }
        public TeamMember CurrentDefender { get; private set; }
        public ClashType CurrentClashType => currentClashType;

        /// <summary>
        /// One side of a duel, fully worked out. Built before anything is
        /// compared, so the comparison itself is two lines rather than a wall of
        /// interleaved arithmetic — and so the readout above the player's head
        /// can be driven from exactly the numbers that decided it.
        /// </summary>
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

        private void Awake()
        {
            Instance = this;

            // Statics survive a domain reload when the editor's fast enter-play
            // mode is on, which would otherwise start the scene mid-clash.
            IsClashActive = false;
            clashBlockedUntil = 0f;
        }

        private void OnEnable()
        {
            Core.TacticalEvents.OnClashInitiated += HandleClash;
            Core.TacticalEvents.OnShotInitiated += HandleShot;
            Core.TacticalEvents.OnMatchOver += HandleMatchOver;
        }

        private void OnDisable()
        {
            Core.TacticalEvents.OnClashInitiated -= HandleClash;
            Core.TacticalEvents.OnShotInitiated -= HandleShot;
            Core.TacticalEvents.OnMatchOver -= HandleMatchOver;

            if (Instance == this)
            {
                Instance = null;
            }

            // Nothing will click a button once this object is gone, so without
            // this the game — and the editor — would be stranded at timeScale 0.
            if (IsClashActive)
            {
                EndClash();
            }
        }

        /// <summary>An attacker may only dribble or barge through.</summary>
        public static ClashAction RandomAttackerAction()
        {
            return Random.value < 0.5f ? ClashAction.Dribble : ClashAction.Power;
        }

        /// <summary>A defender may only go in for it or stand firm.</summary>
        public static ClashAction RandomDefenderAction()
        {
            return Random.value < 0.5f ? ClashAction.Tackle : ClashAction.Block;
        }

        /// <summary>A shooter may drive it or dink it.</summary>
        public static ClashAction RandomShooterAction()
        {
            return Random.value < 0.5f ? ClashAction.PowerShot : ClashAction.LobShot;
        }

        /// <summary>A keeper may gather it or beat it away.</summary>
        public static ClashAction RandomKeeperAction()
        {
            return Random.value < 0.5f ? ClashAction.Catch : ClashAction.Punch;
        }

        /// <summary>
        /// The whistle beats an open duel. Without this, a clash still on screen
        /// at full time would leave IsClashActive latched true — every trigger,
        /// every input and the restart itself would stay blocked behind a panel
        /// buried under the results screen.
        /// </summary>
        private void HandleMatchOver()
        {
            if (IsClashActive)
            {
                // EndClash checks IsPlayable, so the pitch stays frozen.
                EndClash();
            }
        }

        /// <summary>
        /// True when one of these two is genuinely holding the ball.
        ///
        /// A duel is a contest FOR the ball, so a duel with the ball nowhere near
        /// either player is not a duel — it is two players bumping into each
        /// other. That was producing phantom clashes: the drift keeps everyone
        /// moving and touching, and every contact between opponents was opening a
        /// frozen panel over a ball lying somewhere else on the pitch.
        ///
        /// Checked against the BALL rather than against either handler's HasBall.
        /// A handler's flag is its own opinion and can survive the ball being
        /// taken off it; the socket the ball is riding on cannot lie.
        /// </summary>
        private static bool IsContestOverTheBall(TeamMember attacker, TeamMember defender)
        {
            BallController ball = BallController.Instance;

            if (ball == null)
            {
                // No ball in the scene at all: nothing to contest, but also
                // nothing this check can meaningfully say. Let it through rather
                // than silently disabling duels in a scene built without one.
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

        private void HandleClash(TeamMember attacker, TeamMember defender)
        {
            BeginClash(attacker, defender, ClashType.Tackle);
        }

        private void HandleShot(TeamMember shooter, TeamMember goalkeeper)
        {
            BeginClash(shooter, goalkeeper, ClashType.Shot);
        }

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

            // The camera is handed both players rather than a point: it stages
            // the duel over the attacker's shoulder, which needs to know which
            // way round the two of them are standing. Fired from here rather
            // than from the UI controller — this is the same frame the panel
            // opens, and the fail-safe path below has no UI to fire it from.
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

            // Fail safe: with no UI there are no buttons, and the match would
            // hang at timeScale 0 forever. Rolling for both sides is far better
            // than a silent softlock.
            if (uiController == null)
            {
                Debug.LogError("ClashManager no tiene uiController asignado. " +
                               "El duelo se resuelve al azar para no bloquear la partida.");

                ResolveClash(attacker, defender, DefaultAttackerAction(type), DefaultDefenderAction(type));

                return;
            }

            uiController.ShowClash(attacker, defender, type);
        }

        /// <summary>
        /// Settles the duel: base stat for the chosen action, the elemental edge
        /// and the difficulty handicap folded into it, the counter bonus and the
        /// fatigue penalty applied to that, plus a d20 each. Highest total wins,
        /// ties go to the defender — and a natural 20 skips the lot.
        /// </summary>
        public void ResolveClash(TeamMember attacker, TeamMember defender,
            ClashAction attackerAction, ClashAction defenderAction)
        {
            if (!IsClashActive)
            {
                return;
            }

            // Captured before EndClash wipes it.
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

            // The same facts the log carries, put over the players' heads so the
            // duel can be read without the console open.
            SpawnDuelFeedback(attackerSide);
            SpawnDuelFeedback(defenderSide);

            // A foul cancels the duel outright — before the ball changes hands,
            // before a shot flies. Checked here rather than inside each outcome
            // so there is exactly one place where a duel can be voided, and no
            // way for one branch to apply its result anyway.
            //
            // The panel is deliberately still up at this point: the foul is shown
            // on the frozen duel before anything is torn down, and the routine
            // below is what closes it.
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

        /// <summary>
        /// The duel landing: the contact sound, sparks at the point of impact,
        /// and — if either side rolled a natural 20 — a gold burst and a short,
        /// hard kick of the camera instead.
        ///
        /// Fired at RESOLUTION rather than when the panel opened, because the
        /// freeze is a question and this is the answer to it, and because the
        /// critical is not known any earlier. Both a 20: still one burst, and it
        /// still deserves one even though the two cancel out in the maths.
        ///
        /// The critical is carried entirely by the picture. It used to layer a
        /// 5.6 s fanfare over the impact, which buried the sound of the duel
        /// itself and ran on well past the next passage of play.
        /// </summary>
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
                // Shorter and harder than the goal shake: a critical is a single
                // blow, a goal is a moment that wants to ring on a little.
                CameraSystem.TacticalCamera.Instance.Shake(CriticalShakeIntensity, CriticalShakeTime);
            }
        }

        /// <summary>
        /// How likely each move is to give away a foul, as a percentage.
        ///
        /// The risk sits on the AGGRESSIVE moves — a charge and a tackle are the
        /// two ways of going through a player rather than round them — so the
        /// ring gains a second axis: Power beats Tackle, but Power is also the
        /// move most likely to hand the other side a free kick. Dribbling and
        /// blocking are near enough clean; they are what you pick when the
        /// contact is happening on the edge of your own box.
        ///
        /// Read from the LOSER's move as well as the winner's: a mistimed
        /// challenge is exactly the challenge that did not win the ball, and a
        /// foul is most of the time what losing one looks like.
        ///
        /// A shot duel carries NO risk at all — not the strike, not the lob, not
        /// the punch, not the catch. Nobody is being gone through: the striker is
        /// hitting a ball and the keeper is playing it, and the two of them are
        /// not even in contact. A foul there would also have nowhere sensible to
        /// go, since the spot is inside the box and every one of them would come
        /// out as a penalty.
        /// </summary>
        public int FoulChanceFor(ClashAction action)
        {
            switch (action)
            {
                case ClashAction.Power: return powerFoulChance;
                case ClashAction.Tackle: return tackleFoulChance;
                case ClashAction.Dribble: return dribbleFoulChance;
                case ClashAction.Block: return blockFoulChance;

                // Shooting, lobbing, punching, catching, passing, intercepting:
                // none of them is a challenge on another player.
                default: return 0;
            }
        }

        /// <summary>
        /// Rolls for a foul and names who gave it away, or null if the duel was
        /// clean.
        /// </summary>
        private TeamMember ResolveFoulOffender(DuelSide attackerSide, DuelSide defenderSide)
        {
            // Only one side can give the foul away, and it is whichever of them
            // committed the more reckless act. Rolling for both would double the
            // rate and let a duel end in two fouls at once.
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

        /// <summary>
        /// Shows the foul on the frozen duel, holds it there, and only then
        /// tears the panel down and asks for the restart.
        ///
        /// The pause is the whole point. The foul voids a duel the player has
        /// just chosen a move for, so cutting straight to a free kick somewhere
        /// else on the pitch reads as the game having ignored the press — the
        /// beat is what connects the choice to its consequence.
        ///
        /// Realtime, necessarily: the duel is holding timeScale at zero, and a
        /// scaled wait here would never advance a single frame.
        /// </summary>
        private System.Collections.IEnumerator CommitFoulRoutine(TeamMember offender)
        {
            // At the whistle, not at the restart. The free kick is not placed
            // until the dwell below has run, and a player who watched the foul
            // banner for a second and a half with his own route still painted
            // across the pitch has been told the game is stopped by everything
            // except the picture in front of him.
            Core.MatchManager.ClearDrawnRoutes();

            FloatingTextManager texts = FloatingTextManager.Instance;

            if (texts != null)
            {
                // Names the side and is thrown up at several times the size of a
                // duel readout. The other messages in a duel are numbers you
                // lean in to read; this one voids the decision the player just
                // made, so it has to carry from the match camera.
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

            // Blown with the banner, not with the restart. OnFoulCommitted is
            // only raised at the far end of the dwell below — it exists to hand
            // the restart its spot — so hanging the whistle off it would sound
            // it a second and a half after the decision it is announcing.
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PlayFoulWhistle();
            }

            yield return new WaitForSecondsRealtime(foulDwellSeconds);

            // Closed only now: the banner carried the "¡FALTA!" headline for the
            // whole wait, and tearing it down first would have flashed it for a
            // single frame.
            EndClash();

            Core.TacticalEvents.OnFoulCommitted?.Invoke(offender);
        }

        /// <summary>
        /// Charges both sides' momentum for the duel just fought. The winner
        /// gains the most, but the loser gains something too — a side being
        /// overrun still needs a road back.
        /// </summary>
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

        /// <summary>
        /// A pass cut out of the air, settled where it happens. No panel, no
        /// freeze, no choice: the ball is travelling and the two players are
        /// nowhere near each other, so there is nothing to stage and nobody to
        /// read. The same maths as any other duel, resolved in one call.
        /// </summary>
        /// <returns>
        /// True if the interceptor took the ball, so the caller knows not to
        /// treat it as a loose ball afterwards.
        /// </returns>
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

            // No counter ring here: one move per side, so neither can read the
            // other. It is technique against reading of the game, and the roll.
            DuelSide passerSide = BuildSide(passer, interceptor, ClashAction.Pass, false, isAttacker: true);
            DuelSide interceptorSide = BuildSide(interceptor, passer, ClashAction.Intercept, false, isAttacker: false);

            bool interceptorWins = ResolveWinner(passerSide, interceptorSide);

            Debug.Log($"[Intercept] {DescribeSide(passerSide)}  |  {DescribeSide(interceptorSide)}");

            // Only the interceptor gets a readout. The passer is half a pitch
            // away with the camera nowhere near them, so their numbers would
            // scroll up over an empty patch of grass.
            SpawnDuelFeedback(interceptorSide);

            FloatingTextManager texts = FloatingTextManager.Instance;
            Vector3 at = interceptor.transform.position;

            if (!interceptorWins)
            {
                // The stun is what actually lets the ball through: without it the
                // interceptor's own trigger collects the pass they just failed to
                // cut out, on the very next contact tick.
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

        /// <summary>
        /// Works one side of a duel out in full: which number it is bringing,
        /// what is modifying it and what it rolled.
        ///
        /// The elemental edge and the difficulty handicap go into the BASE, not
        /// onto the total. Both are meant to change how good you are at the
        /// thing you are attempting, so they should be multiplied by the counter
        /// bonus and cut by fatigue like the rest of the stat — a flat bonus on
        /// the total would be worth the same whether you were fresh or spent.
        /// </summary>
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

            // Fatigue bites the stat, not the roll. Applied as a share rather
            // than as a flat penalty on purpose: a fixed -20 would erase a
            // 20-base action outright — a blown striker could not attempt a
            // tackle at all — while barely troubling an 85. The d20 is left
            // alone so a spent player can still steal one on luck.
            float afterFatigue = afterCounter * (side.IsBlown ? exhaustedPenaltyMultiplier : 1f);

            side.Score = afterFatigue + side.Roll;

            return side;
        }

        /// <summary>
        /// Who won. A natural 20 is an automatic win regardless of the totals —
        /// which is the whole point of rolling one — and two of them cancel out
        /// and fall back to the numbers, where a tie still goes to the defender.
        /// </summary>
        private static bool ResolveWinner(DuelSide attacker, DuelSide defender)
        {
            if (attacker.IsCritical != defender.IsCritical)
            {
                return defender.IsCritical;
            }

            return defender.Score >= attacker.Score;
        }

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

        /// <summary>
        /// Puts one player's duel readout over their head: the roll they made,
        /// plus whichever modifiers actually applied. Stacked so they can
        /// coexist — a player CAN counter their opponent, hold the elemental
        /// edge and be blown all at once, and messages on the same spot would
        /// simply overprint each other.
        /// </summary>
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

        /// <summary>
        /// The handicap the chosen difficulty hands the AI, in raw stat points.
        /// Zero for the human's side at every setting: the difficulty is meant
        /// to change how hard the opposition is, not how good you are.
        /// </summary>
        private static int DifficultyModifier(TeamMember member)
        {
            return Core.MatchManager.Instance != null
                ? Core.MatchManager.Instance.DuelModifierFor(member.team)
                : 0;
        }

        /// <summary>
        /// What this player's side is worth in a duel for being in the zone.
        ///
        /// Goes into the BASE like the elemental edge, not onto the total, so a
        /// side that is burning AND reads the opponent right gets the counter
        /// multiplier applied to the bonus too — and a burning side that is
        /// blown still has the bonus cut by its fatigue.
        /// </summary>
        private static int TensionModifier(TeamMember member)
        {
            return TensionManager.Instance != null
                ? TensionManager.Instance.DuelBonus(member.team)
                : 0;
        }

        /// <summary>Resolves the current duel with both sides rolled at random.</summary>
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

        /// <summary>
        /// No shot is ever teleported to its outcome: the striker always hits
        /// the ball, and where it ends up is settled by the physics afterwards.
        /// The duel decides the AIM, not the result.
        ///
        ///   shooter wins -> struck past the keeper, who is frozen so his trigger
        ///                   cannot swallow the goal he has just been beaten for
        ///   keeper wins  -> struck softly, straight at the keeper, who gathers
        ///                   it on contact like any other loose ball
        ///
        /// The camera then chases the ball, because a shot that flies is only
        /// worth flying if it can be seen from something other than a bird's-eye
        /// view of the whole pitch.
        /// </summary>
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
                // The keeper is left mobile on purpose: he keeps tracking the
                // ball's X on his line, which is what turns "aimed at him" into
                // "caught by him".
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

            // After the kick: the ball is only free — and therefore worth
            // chasing — once it has actually left the shooter's foot.
            if (CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.FollowBallCinematic(shotCinematicDuration);
            }
        }

        /// <summary>
        /// Aims at the net the keeper is standing in front of, a few units past
        /// them, so the ball travels through the goal trigger instead of dying
        /// on the line.
        /// </summary>
        private Vector3 CalculateGoalAim(TeamMember goalkeeper)
        {
            Vector3 keeperPosition = goalkeeper.transform.position;
            float side = Mathf.Sign(keeperPosition.z);

            return new Vector3(0f, 0.5f, keeperPosition.z + (side * goalAimOffset));
        }

        /// <summary>
        /// Tears the frozen state down: hides the panel, restores time and opens
        /// the cooldown. Deliberately separate from the outcome, so leaving the
        /// scene mid-duel unfreezes without awarding anybody the ball.
        /// </summary>
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

            // Pulled out here rather than in ResolveClash, so leaving the scene
            // or hitting full time mid-duel also puts the camera back.
            if (CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.ResetToOverhead();
            }

            // A duel can still be on screen when the clock runs out. Restoring
            // time here would then un-freeze a finished match, so the whistle
            // wins: the panel closes, but the pitch stays stopped.
            if (!Core.MatchManager.IsPlayable)
            {
                return;
            }

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;
        }

        private static int AttackerStat(TeamMember attacker, ClashAction action)
        {
            switch (action)
            {
                case ClashAction.Power: return attacker.Power;

                case ClashAction.PowerShot:
                case ClashAction.LobShot: return attacker.Shoot;

                // There is no passing stat, so weight of pass is read off the
                // same technique the dribble uses. Worth revisiting if the stat
                // block ever grows one.
                default: return attacker.Dribble;
            }
        }

        private static int DefenderStat(TeamMember defender, ClashAction action)
        {
            switch (action)
            {
                case ClashAction.Block: return defender.Block;

                case ClashAction.Catch:
                case ClashAction.Punch: return defender.Goalkeeping;

                // Reading a pass is the same instinct as going in for a tackle.
                default: return defender.Tackle;
            }
        }

        private static bool AttackerCounters(ClashAction attackerAction, ClashAction defenderAction)
        {
            return (attackerAction == ClashAction.Dribble && defenderAction == ClashAction.Block)
                || (attackerAction == ClashAction.Power && defenderAction == ClashAction.Tackle)
                || (attackerAction == ClashAction.LobShot && defenderAction == ClashAction.Catch)
                || (attackerAction == ClashAction.PowerShot && defenderAction == ClashAction.Punch);
        }

        private static bool DefenderCounters(ClashAction attackerAction, ClashAction defenderAction)
        {
            return (defenderAction == ClashAction.Tackle && attackerAction == ClashAction.Dribble)
                || (defenderAction == ClashAction.Block && attackerAction == ClashAction.Power)
                || (defenderAction == ClashAction.Catch && attackerAction == ClashAction.PowerShot)
                || (defenderAction == ClashAction.Punch && attackerAction == ClashAction.LobShot);
        }
    }
}
