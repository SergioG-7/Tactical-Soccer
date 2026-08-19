using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.Core
{
    /// <summary>The shapes a side may line up in. Six outfield players either way.</summary>
    public enum FormationType
    {
        Balanced_2_2_2,
        Defensive_3_2_1,
        Offensive_1_3_2
    }

    /// <summary>
    /// How hard the opposition plays. Two levers, both deliberately small: how
    /// often the AI re-decides, and a flat handicap on every duel it fights.
    ///
    /// Neither touches the human's side at any setting. A difficulty that made
    /// YOUR players worse would be indistinguishable from a bug from the other
    /// side of the screen.
    /// </summary>
    public enum AIDifficulty
    {
        Facil,
        Normal,
        Dificil
    }

    /// <summary>
    /// One outfield slot of a starting shape: where the player stands and which
    /// line they hold.
    /// </summary>
    public readonly struct FormationSlot
    {
        public readonly PlayerRole Role;

        /// <summary>Across the pitch. The two sides are mirror images, so one value serves both.</summary>
        public readonly float X;

        /// <summary>
        /// Distance from the halfway line into the team's OWN half, always
        /// positive. Callers multiply by the side's sign, so one table describes
        /// both teams.
        /// </summary>
        public readonly float OwnHalfZ;

        public FormationSlot(PlayerRole role, float x, float ownHalfZ)
        {
            Role = role;
            X = x;
            OwnHalfZ = ownHalfZ;
        }
    }

    /// <summary>
    /// The starting shapes, in one place. The scene generator spawns the squad
    /// from the same tables the formation menu later re-arranges them with, so
    /// picking the default shape in the menu puts everybody exactly back where
    /// they began rather than somewhere subtly different.
    ///
    /// Every slot sits in its own half: the three lines are pinned to the same
    /// depths whatever the shape, so a 3-2-1 reads as a deeper back line rather
    /// than as a different pitch.
    /// </summary>
    public static class Formations
    {
        private const float DefenderLineZ = 16f;
        private const float MidfieldLineZ = 9f;

        // Just outside the centre circle, whose painted radius is 3.75 units.
        private const float ForwardLineZ = 4.5f;

        private static readonly FormationSlot[] Balanced =
        {
            new FormationSlot(PlayerRole.Defender, -4.5f, DefenderLineZ),
            new FormationSlot(PlayerRole.Defender, 4.5f, DefenderLineZ),
            new FormationSlot(PlayerRole.Midfielder, -7.5f, MidfieldLineZ),
            new FormationSlot(PlayerRole.Midfielder, 7.5f, MidfieldLineZ),
            new FormationSlot(PlayerRole.Forward, -3.5f, ForwardLineZ),
            new FormationSlot(PlayerRole.Forward, 3.5f, ForwardLineZ)
        };

        private static readonly FormationSlot[] Defensive =
        {
            // The middle centre-back drops a metre deeper, so three across the
            // back reads as a covered line rather than a flat wall.
            new FormationSlot(PlayerRole.Defender, -7f, DefenderLineZ),
            new FormationSlot(PlayerRole.Defender, 0f, DefenderLineZ + 1f),
            new FormationSlot(PlayerRole.Defender, 7f, DefenderLineZ),
            new FormationSlot(PlayerRole.Midfielder, -5f, MidfieldLineZ),
            new FormationSlot(PlayerRole.Midfielder, 5f, MidfieldLineZ),
            new FormationSlot(PlayerRole.Forward, 0f, ForwardLineZ)
        };

        private static readonly FormationSlot[] Offensive =
        {
            new FormationSlot(PlayerRole.Defender, 0f, DefenderLineZ),
            new FormationSlot(PlayerRole.Midfielder, -8f, MidfieldLineZ + 1f),
            new FormationSlot(PlayerRole.Midfielder, 0f, MidfieldLineZ),
            new FormationSlot(PlayerRole.Midfielder, 8f, MidfieldLineZ + 1f),
            new FormationSlot(PlayerRole.Forward, -3.5f, ForwardLineZ),
            new FormationSlot(PlayerRole.Forward, 3.5f, ForwardLineZ)
        };

        /// <summary>How many outfield players a shape expects. The keeper is extra.</summary>
        public const int OutfieldCount = 6;

        public static FormationSlot[] Get(FormationType formation)
        {
            switch (formation)
            {
                case FormationType.Defensive_3_2_1: return Defensive;
                case FormationType.Offensive_1_3_2: return Offensive;
                default: return Balanced;
            }
        }

        /// <summary>One of the shapes, at random. Used by the "surprise me" rival setting.</summary>
        public static FormationType Random()
        {
            FormationType[] all = (FormationType[])System.Enum.GetValues(typeof(FormationType));

            return all[UnityEngine.Random.Range(0, all.Length)];
        }

        /// <summary>Label for the HUD, so the UI does not hardcode the numbers.</summary>
        public static string GetLabel(FormationType formation)
        {
            switch (formation)
            {
                case FormationType.Defensive_3_2_1: return "3-2-1";
                case FormationType.Offensive_1_3_2: return "1-3-2";
                default: return "2-2-2";
            }
        }
    }

    /// <summary>
    /// Owns the match-wide state nobody else can hold: how much time is left,
    /// and whether the ball is actually in play.
    ///
    /// Every restart — kickoff, throw-in, corner, goal kick — works the same
    /// way: the ball is handed to one specific player, everyone else stands
    /// still, and play resumes the moment that player passes or shoots. When
    /// the taker belongs to the AI it takes its own restart after a beat,
    /// because nothing else ever would and the match would simply stop.
    /// </summary>
    public class MatchManager : MonoBehaviour
    {
        [Header("Clock")]
        [Tooltip("Length of ONE half, in seconds. The match is two of these with " +
                 "an interval between them, so a full game is twice this.")]
        public float matchDuration = 45f;

        /// <summary>Seconds left. Counts in scaled time, so a frozen duel or a
        /// slow-motion route draw does not burn the clock at full rate.</summary>
        public float currentTime { get; private set; }

        [Tooltip("Which half is being played. 1 until the interval, 2 after it.")]
        public int currentHalf = 1;

        public bool isMatchOver = false;

        /// <summary>
        /// True between the two halves. Not folded into isMatchOver: full time
        /// is final and nothing may thaw it, while this one exists precisely to
        /// be undone by the interval screen.
        /// </summary>
        public bool isHalftime { get; private set; }

        /// <summary>
        /// False until the player presses Play on the title screen. Nothing on
        /// the pitch may act before that.
        /// </summary>
        public bool isMatchStarted { get; private set; }

        /// <summary>True between a restart and the first pass or shot.</summary>
        public bool isWaitingForKickoff { get; private set; }

        /// <summary>True between the ball crossing a touchline and the throw being taken.</summary>
        public bool isWaitingForThrowIn { get; private set; }

        /// <summary>True while a corner is being lined up.</summary>
        public bool isWaitingForCorner { get; private set; }

        /// <summary>True while a goal kick is being lined up.</summary>
        public bool isWaitingForGoalKick { get; private set; }

        /// <summary>True while a free kick is being lined up.</summary>
        public bool isWaitingForFreeKick { get; private set; }

        /// <summary>
        /// True from the moment a penalty is given until it has been taken.
        /// Kept apart from the other restarts because a penalty is not put back
        /// into play by a pass: it is a menu, and the ball does not move until
        /// somebody has pressed a side.
        /// </summary>
        public bool isWaitingForPenalty { get; private set; }

        /// <summary>
        /// True while a goal is being shown before play restarts.
        ///
        /// It is a set-piece state like any other — the ball is dead and nobody
        /// may act — which is why it is folded into IsWaitingForSetPiece rather
        /// than given guards of its own. That one flag already stops the clock,
        /// the AI, the drift and the duels, and a celebration needs all four.
        /// </summary>
        public bool IsCelebratingGoal { get; private set; }

        [Header("Sides")]
        [Tooltip("The side the person holding the phone plays. Everything else " +
                 "is driven by the AI, including its own restarts.")]
        [SerializeField] private TeamId humanTeam = TeamId.Blue;

        [Header("Configuración de partido")]
        [Tooltip("Set from the pre-match screen. Read by the duel maths and by " +
                 "the opposition's own think rate.")]
        public AIDifficulty aiDifficulty = AIDifficulty.Normal;

        [Tooltip("Shape the opposition lines up in. Only used when the shape was " +
                 "actually chosen — see randomiseRivalFormation.")]
        public FormationType rivalFormation = FormationType.Balanced_2_2_2;

        [Tooltip("True when the player asked for a surprise. The shape is then " +
                 "rolled at the opening kickoff rather than at the menu, so it " +
                 "cannot be read off the pitch while the team sheet is still up.")]
        public bool randomiseRivalFormation = true;

        [Header("Capitanes")]
        [Tooltip("Chosen by the player on the team sheet. Their ROLE decides " +
                 "which passive the whole side gets, so re-roling the captain " +
                 "changes the buff.")]
        public TeamMember blueCaptain;

        [Tooltip("Picked at random at the opening kickoff if nobody set one.")]
        public TeamMember redCaptain;

        [Tooltip("Flat bonus a captain's line gives the whole team: a defender " +
                 "captain hardens everyone's defending, a forward captain sharpens " +
                 "everyone's attacking.")]
        [SerializeField] private int captainStatBonus = 10;

        [Tooltip("What a midfielder captain is worth instead: a share taken off " +
                 "every team-mate's stamina drain. 0.8 is the -20% the brief asks " +
                 "for — with no recovery in the match, buying back a fifth of the " +
                 "running is worth about nine seconds of legs per player.")]
        [SerializeField] private float captainStaminaDrainMultiplier = 0.8f;

        [Tooltip("Stat points the opposition gains or loses in every duel, at " +
                 "the hardest and easiest settings respectively.")]
        [SerializeField] private int difficultyDuelModifier = 5;

        [Header("Cambios de la IA")]
        [Tooltip("Share of a full tank below which the opposition takes a player " +
                 "off at the interval. 0.8 is deliberately generous: the bench " +
                 "only holds three, and a side that waited for genuine exhaustion " +
                 "never used it at all.")]
        [Range(0f, 1f)]
        [SerializeField] private float tiredSubstitutionFraction = 0.8f;

        [Header("Kickoff")]
        [Tooltip("How far behind the centre spot the taker stands. The ball rides " +
                 "on a socket half a unit in front of them, so this puts it on the " +
                 "centre mark itself.")]
        [SerializeField] private float kickoffTakerOffset = 0.5f;

        [Header("Set Pieces")]
        [Tooltip("How long the AI takes to line up a restart of its own. Long " +
                 "enough to read as deliberate rather than as a glitch.")]
        [SerializeField] private float aiSetPieceDelay = 1.5f;

        [Tooltip("How long the ball is left in the net before the centre spot is " +
                 "set up again. The goal used to be undone on the same frame it " +
                 "was scored — the ball was already back on the halfway line " +
                 "before the announcement had drawn — so the one moment the " +
                 "whole match is played for was the one nobody got to see.")]
        [SerializeField] private float goalCelebrationDelay = 2.5f;

        [Header("Final de parte")]
        [Tooltip("How long play carries on after the whistle before the interval " +
                 "or full-time screen takes over. Without it the pitch freezes on " +
                 "the same frame the clock hits zero and the last touch of the " +
                 "half is never seen.")]
        [SerializeField] private float endOfHalfDelay = 2.5f;

        [Tooltip("How far off the goal line the keeper stands for a goal kick.")]
        [SerializeField] private float goalKickDepth = 3f;

        [Tooltip("How far everybody but the taker is pushed clear of a free kick. " +
                 "A player capsule is 1 unit wide, so this is a couple of bodies " +
                 "of room — enough that the taker can play the ball without " +
                 "shoving somebody first.")]
        [SerializeField] private float restartClearanceRadius = 2.5f;

        [Tooltip("How far out from the goal line a penalty is taken.")]
        [SerializeField] private float penaltySpotDepth = 8f;

        [Tooltip("How far up the pitch a goal kick is aimed.")]
        [SerializeField] private float goalKickDistance = 16f;

        [Tooltip("How far infield a throw-in is aimed.")]
        [SerializeField] private float throwInDistance = 8f;

        [Tooltip("How far up the pitch an AI kickoff is played. Short: a kickoff " +
                 "is a pass to get the ball moving, not a clearance.")]
        [SerializeField] private float kickoffPassDistance = 7f;

        private const float MatchOverTimeScale = 0f;
        private const float NormalTimeScale = 1f;
        private const float FixedDeltaTimeAtNormalScale = 0.02f;

        private const int HalvesPerMatch = 2;

        /// <summary>
        /// Share of the pitch that counts as the attacking third — the zone an
        /// attack has to have reached for the half to play on past zero.
        /// </summary>
        private const float AttackingThirdShare = 1f / 3f;

        /// <summary>
        /// How far inside the painted lines a restart mark is pulled. The ball
        /// is placed here, not the player, and a mark sitting exactly ON a line
        /// is one rounding error away from being out of play again.
        /// </summary>
        private const float RestartBallInset = 0.2f;

        public static MatchManager Instance { get; private set; }

        /// <summary>
        /// Gate for anything that restores Time.timeScale. Once the whistle has
        /// gone, nothing may thaw the match back out.
        /// </summary>
        public static bool IsPlayable => Instance == null || !Instance.isMatchOver;

        // The kick the camera takes on a goal: a soft 0.3 held for a long 0.5 s.
        private const float GoalShakeIntensity = 0.3f;
        private const float GoalShakeTime = 0.5f;

        // How far time is slowed the instant a goal goes in.
        private const float GoalSlowMotionScale = 0.3f;

        [Tooltip("Real seconds the goal is held in slow motion before the rest " +
                 "of the celebration plays out at normal speed. Capped by the " +
                 "celebration's own length, so a shorter celebration cannot end " +
                 "while time is still running slow.")]
        [SerializeField] private float goalSlowMotionDuration = 1.2f;

        /// <summary>
        /// True while the ball is dead, waiting to be put back into play by any
        /// kind of restart. Systems that must stand down do not care which one.
        ///
        /// A match that has not started yet counts as dead too. Folding it in
        /// here is what makes the title screen actually hold: the AI, the
        /// off-the-ball drift and the duels all already consult this, so none of
        /// them needs to know a title screen exists.
        /// </summary>
        public bool IsWaitingForSetPiece =>
            !isMatchStarted || isHalftime || IsCelebratingGoal || isWaitingForKickoff
            || isWaitingForThrowIn || isWaitingForCorner || isWaitingForGoalKick
            || isWaitingForFreeKick || isWaitingForPenalty;

        /// <summary>
        /// True while something has taken the pitch over and owns the screen: a
        /// frozen duel, a goal being celebrated, a penalty waiting to be taken.
        ///
        /// Deliberately NOT the same thing as <see cref="IsWaitingForSetPiece"/>.
        /// A throw-in is a dead ball, but nothing is on screen and the half can
        /// end on it perfectly well — that is what closes out stoppage time.
        /// These three are different: each one is a moment the player is looking
        /// at and, in the case of the duel, one they are being asked to answer.
        /// Ending the half on top of any of them puts a second screen over one
        /// that was already up, and both of them write timeScale.
        /// </summary>
        private bool IsPitchInterrupted =>
            Gameplay.ClashManager.IsClashActive || IsCelebratingGoal || isWaitingForPenalty;

        /// <summary>
        /// Gate for the input layer while the penalty menu is up. Same reason as
        /// the title screen and the interval: input is not governed by timeScale,
        /// and a route drawn behind the menu would have TimeController set 0.1
        /// and run the match on underneath it.
        /// </summary>
        public static bool IsPenaltyPending => Instance != null && Instance.isWaitingForPenalty;

        /// <summary>
        /// Gate for the ball's own out-of-play check, which is not a listener on
        /// any of this and runs every frame regardless. A ball sitting in the
        /// back of the net is past the goal line by every measure the pitch
        /// knows, so without this the celebration would immediately be
        /// interrupted by the goal kick it looks like.
        /// </summary>
        public static bool IsGoalBeingCelebrated => Instance != null && Instance.IsCelebratingGoal;

        /// <summary>
        /// Gate for the input layer, which is not governed by timeScale and so
        /// could otherwise draw routes behind the title screen — and drawing a
        /// route sets timeScale to 0.1, thawing the pitch through the back door.
        /// </summary>
        public static bool IsStarted => Instance == null || Instance.isMatchStarted;

        /// <summary>
        /// Gate for the input layer at the interval, for the same reason as the
        /// two above: the pitch is frozen at timeScale 0 behind the team talk,
        /// and a route drawn through it would have TimeController set 0.1 and
        /// send the players out before anybody pressed anything.
        /// </summary>
        public static bool IsHalftime => Instance != null && Instance.isHalftime;

        /// <summary>The side the player controls. Read by the menus, which need
        /// to know whose team sheet they are showing.</summary>
        public TeamId HumanTeam => humanTeam;

        /// <summary>
        /// The handicap the chosen difficulty hands a side in every duel. Always
        /// zero for the human: the setting changes the opposition, not you.
        /// </summary>
        public int DuelModifierFor(TeamId team)
        {
            if (team == humanTeam)
            {
                return 0;
            }

            switch (aiDifficulty)
            {
                case AIDifficulty.Facil: return -difficultyDuelModifier;
                case AIDifficulty.Dificil: return difficultyDuelModifier;
                default: return 0;
            }
        }

        /// <summary>
        /// Multiplier on how long the opposition waits between decisions. Above
        /// 1 is a side that reacts late — it keeps pressing the space the ball
        /// has already left — and below 1 is one that reads the play almost as
        /// it happens.
        /// </summary>
        public float AiThinkIntervalScale
        {
            get
            {
                switch (aiDifficulty)
                {
                    case AIDifficulty.Facil: return 2f;
                    case AIDifficulty.Dificil: return 0.5f;
                    default: return 1f;
                }
            }
        }

        /// <summary>
        /// Applies the pre-match settings. Called by the configuration screen
        /// before anything has kicked off, so the clock can still be re-seeded.
        /// </summary>
        public void ConfigureMatch(float halfDurationSeconds, AIDifficulty difficulty,
            bool randomRivalShape, FormationType rivalShape, TeamKit kit)
        {
            matchDuration = Mathf.Max(1f, halfDurationSeconds);
            currentTime = matchDuration;

            aiDifficulty = difficulty;
            randomiseRivalFormation = randomRivalShape;
            rivalFormation = rivalShape;
            humanKit = kit;

            // A quick match always puts the opposition back in red, whatever a
            // tournament round left them wearing.
            rivalKitColor = Color.red;

            Debug.Log($"Configuración: {matchDuration:F0} s por parte, dificultad {aiDifficulty}, " +
                      $"rival {(randomiseRivalFormation ? "aleatorio" : Formations.GetLabel(rivalFormation))}, " +
                      $"equipación {TeamKits.GetLabel(humanKit)}.");
        }

        /// <summary>
        /// Sets a tournament round up. Separate from ConfigureMatch because the
        /// two disagree about who decides: a quick match takes the player's
        /// answers, a tournament round dictates the terms and skips the
        /// configuration screen entirely.
        ///
        /// The human's own strip is deliberately NOT touched — that is the one
        /// choice they keep across the whole run.
        /// </summary>
        public void ConfigureTournamentMatch(float halfDurationSeconds, AIDifficulty difficulty,
            FormationType rivalShape, Color rivalColor)
        {
            matchDuration = Mathf.Max(1f, halfDurationSeconds);
            currentTime = matchDuration;

            aiDifficulty = difficulty;
            randomiseRivalFormation = false;
            rivalFormation = rivalShape;

            rivalKitColor = rivalColor;
        }

        [Tooltip("The strip the human side plays in. Chosen on the configuration " +
                 "screen and applied at the opening whistle.")]
        [SerializeField] private TeamKit humanKit = TeamKit.Azul;

        // The opposition's strip. Defaults to the red it is generated in, and is
        // repainted on EVERY kickoff rather than only when a tournament has
        // dictated a colour — skipping it would leave the opposition still
        // wearing the last round's purple in the quick match after it.
        private Color rivalKitColor = Color.red;

        /// <summary>
        /// What colour a side is actually wearing right now.
        ///
        /// The single source of truth for it, because there are two answers —
        /// the human's is a TeamKit chosen on the configuration screen, the
        /// opposition's is a raw colour a tournament round may have dictated —
        /// and anything wanting to name a team in its own colour has to be able
        /// to ask without knowing which of the two applies.
        /// </summary>
        public static Color GetTeamColor(TeamId team)
        {
            if (Instance == null)
            {
                return team == TeamId.Blue ? Color.blue : Color.red;
            }

            return team == Instance.humanTeam
                ? TeamKits.GetColor(Instance.humanKit)
                : Instance.rivalKitColor;
        }

        /// <summary>
        /// Repaints the human side in the chosen strip.
        ///
        /// Written through <c>renderer.material</c>, never <c>sharedMaterial</c>:
        /// every blue player points at the same TeamBlueMaterial asset, so
        /// writing the shared one would repaint the opposition's keeper gloves
        /// on the way past and — worse — persist the change to disk, so the next
        /// match would open in whatever colour the last one chose.
        ///
        /// Substitutes are included even though they are sitting in the dugout:
        /// they come on later, and a side that changed colour halfway through
        /// would be unreadable.
        ///
        /// The goalkeeper is deliberately left out. He has his own material so
        /// he can be picked out of a crowded box at a glance, and that is worth
        /// more than a matching strip — which is exactly why real keepers wear a
        /// different one.
        /// </summary>
        private void ApplyHumanKit()
        {
            int human = RepaintTeam(humanTeam, TeamKits.GetColor(humanKit));
            int rival = RepaintTeam(Opponent(humanTeam), rivalKitColor);

            Debug.Log($"Equipación {TeamKits.GetLabel(humanKit)} aplicada a {human} jugadores de " +
                      $"{humanTeam}; rival repintado en {rivalKitColor} ({rival} jugadores). " +
                      "Porteros incluidos.");
        }

        /// <summary>
        /// Paints one side's outfield players and tells each of them what colour
        /// they are now, returning how many were changed.
        /// </summary>
        private static int RepaintTeam(TeamId team, Color color)
        {
            int repainted = 0;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                // The keeper is repainted with everybody else. He used to be
                // exempt so he could be picked out of a crowded box, but that
                // meant a fixed yellow — and a tournament round can put the
                // OPPOSITION in orange or gold, at which point the keeper's
                // "distinguishing" colour is the colour of the other team.
                // Reading the eleven as one side is worth more.
                if (member.team != team)
                {
                    continue;
                }

                if (!member.TryGetComponent(out MeshRenderer renderer))
                {
                    continue;
                }

                renderer.material.color = color;
                repainted++;

                // The stun blink restores a colour it cached at Awake, off the
                // SHARED material — i.e. the shirt this player was born in. Left
                // alone, the first player stunned after a kit change would blink
                // back to blue and stay there.
                if (member.TryGetComponent(out Player.PlayerRoute route))
                {
                    route.RefreshOriginalColor(color);
                }
            }

            return repainted;
        }

        private Coroutine kickoffRoutine;
        private Coroutine aiSetPieceRoutine;

        [Tooltip("How far the side NOT taking a restart must stand off the ball. " +
                 "Roughly the ten yards of the real laws, scaled to this pitch: " +
                 "far enough that the taker gets a touch away before anybody " +
                 "reaches them, close enough that the defence is not handed a " +
                 "free pass every time.")]
        [SerializeField] private float restartExclusionRadius = 4f;

        /// <summary>
        /// What each side did with the match, for the full-time board.
        ///
        /// Counted here rather than in the systems that produce the events. A
        /// shot is raised by the ball handler, a foul by the duel manager and a
        /// pass by whoever received it — three places that have nothing to do
        /// with each other and no business owning a scoreboard. This class
        /// already owns everything else that is true of the match as a whole.
        ///
        /// Indexed by TeamId so a stat is one array lookup rather than a branch
        /// per team repeated six times.
        /// </summary>
        private readonly int[] shots = new int[2];
        private readonly int[] fouls = new int[2];
        private readonly int[] passes = new int[2];

        public int ShotsFor(TeamId team) => shots[(int)team];
        public int FoulsFor(TeamId team) => fouls[(int)team];
        public int PassesFor(TeamId team) => passes[(int)team];

        /// <summary>Counted when a player commits to a shot, won or lost.</summary>
        public void RecordShot(TeamId team)
        {
            shots[(int)team]++;
        }

        /// <summary>Counted against the side that gave the foul away.</summary>
        public void RecordFoul(TeamId team)
        {
            fouls[(int)team]++;
        }

        /// <summary>Counted when a pass reaches a team-mate.</summary>
        public void RecordPass(TeamId team)
        {
            passes[(int)team]++;
        }

        private void ResetStatistics()
        {
            for (int i = 0; i < shots.Length; i++)
            {
                shots[i] = 0;
                fouls[i] = 0;
                passes[i] = 0;
            }
        }

        /// <summary>True while the clock is at zero but an attack is still alive.</summary>
        private bool isInStoppageTime;

        /// <summary>True between the final whistle and the screen that follows it.</summary>
        private bool isEndingHalf;

        /// <summary>
        /// True once the whistle has gone for the end of a half, until the
        /// screen that follows takes over.
        ///
        /// Public because the duel manager has to refuse NEW duels in this
        /// window. The match is deliberately still live through it — the point
        /// is to watch the last action come to rest — but "still live" must not
        /// mean two players can start a fresh tackle after the referee has ended
        /// the half. Duels already open when it blows still resolve normally.
        /// </summary>
        public static bool IsEndingHalf => Instance != null && Instance.isEndingHalf;

        /// <summary>
        /// Who takes the next kickoff. The opening one is the human's; after
        /// that it belongs to whoever has just been scored against, which is the
        /// whole reason this is a field rather than a constant — it used to be
        /// the human side every time, so a team could concede and then be handed
        /// the ball back at the centre spot as a reward.
        /// </summary>
        private TeamId kickoffTeam;

        private void Awake()
        {
            Instance = this;

            currentTime = matchDuration;
            currentHalf = 1;
            isMatchOver = false;
            isMatchStarted = false;
            isHalftime = false;
            isInStoppageTime = false;
            isEndingHalf = false;
            kickoffTeam = humanTeam;
            ClearSetPieceFlags();
        }

        private void OnEnable()
        {
            TacticalEvents.OnMatchReset += HandleMatchReset;
            TacticalEvents.OnGoalScored += HandleGoalScored;
            TacticalEvents.OnFoulCommitted += HandleFoul;
        }

        private void OnDisable()
        {
            TacticalEvents.OnMatchReset -= HandleMatchReset;
            TacticalEvents.OnGoalScored -= HandleGoalScored;
            TacticalEvents.OnFoulCommitted -= HandleFoul;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// A goal decides who restarts: the side that conceded takes it.
        ///
        /// Read here rather than passed into the kickoff, because the two are
        /// raised separately — the goal trigger announces the goal and then asks
        /// the ball to reset, and it is the reset that starts the kickoff. They
        /// arrive in that order on the same frame, so the side is always known
        /// by the time it is needed.
        /// </summary>
        private void HandleGoalScored(int scoringTeamId)
        {
            TeamId scoringTeam = scoringTeamId == ScoreManager.RedTeamId ? TeamId.Red : TeamId.Blue;

            kickoffTeam = scoringTeam == TeamId.Blue ? TeamId.Red : TeamId.Blue;

            Debug.Log($"Gol de {scoringTeam}: el saque de centro es para {kickoffTeam}.");
        }

        /// <summary>
        /// Holds the goal on screen, then restarts from the centre spot.
        ///
        /// Called by the goal trigger INSTEAD of resetting the ball itself. The
        /// wait has to happen before the reset, not after: resetting first puts
        /// the ball on the halfway line and everybody back in shape, so a delay
        /// added afterwards would only be a pause staring at a kickoff that had
        /// already happened.
        ///
        /// Nothing may act during it — the flag is a set-piece state, so the
        /// clock, the AI, the drift and the duels are all already standing down.
        /// </summary>
        public void CelebrateGoal()
        {
            if (isMatchOver)
            {
                return;
            }

            // A second goal cannot be scored into a ball that is already being
            // celebrated, but the trigger can fire again if the ball rolls back
            // out of the net and in again, which would restart the wait and
            // stack a second restart behind it.
            if (IsCelebratingGoal)
            {
                return;
            }

            StartCoroutine(GoalCelebrationRoutine());
        }

        private IEnumerator GoalCelebrationRoutine()
        {
            IsCelebratingGoal = true;

            // Slowed, not frozen, and the distinction is the whole point: a duel
            // resolved a frame before the goal may have left timeScale at 0, and
            // a celebration nobody can see move is just a stutter. This
            // explicitly overwrites whatever the last system left behind.
            //
            // fixedDeltaTime is scaled with it so the physics step keeps its
            // usual relationship to the frame — the ball is still settling into
            // the net through all of this, and leaving the step at 0.02 while
            // time runs at a third makes it settle in visible jerks.
            Time.timeScale = GoalSlowMotionScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale * GoalSlowMotionScale;

            // Softer and longer than the one a critical gets: a goal is a moment
            // that rings on, not a single blow. Note the argument order — the
            // camera takes (intensity, time), so this is 0.3 held for 0.5 s.
            if (CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.Shake(GoalShakeIntensity, GoalShakeTime);
            }

            Announce("announce.goal");

            // Realtime rather than scaled: this routine owns the timeScale it
            // just set, and anything that changed it underneath — a route being
            // drawn drops it to 0.1 — would otherwise stretch the wait out to
            // twenty-five real seconds.
            // Realtime rather than scaled — doubly so now. The routine owns the
            // timeScale it just set, and a scaled wait against a third-speed
            // clock would run three times as long as intended.
            float slowMotion = Mathf.Min(goalSlowMotionDuration, goalCelebrationDelay);

            yield return new WaitForSecondsRealtime(slowMotion);

            // Back to full speed for the rest of the celebration, so the slow
            // motion is the moment the ball hits the net rather than a sluggish
            // pause the player sits through until the restart.
            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            float remaining = goalCelebrationDelay - slowMotion;

            if (remaining > 0f)
            {
                yield return new WaitForSecondsRealtime(remaining);
            }

            IsCelebratingGoal = false;

            if (isMatchOver)
            {
                yield break;
            }

            BallController ball = BallController.Instance;

            if (ball != null)
            {
                // Releases possession, re-centres the ball AND raises
                // OnMatchReset, which is what starts the kickoff.
                ball.ResetToKickoff();
            }
            else
            {
                TacticalEvents.OnMatchReset?.Invoke();
            }
        }

        /// <summary>
        /// Starts the match. Called by the title screen, not from Start: the
        /// game opens on a menu, and a kickoff that ran on its own would be
        /// under way behind it before anyone pressed anything.
        ///
        /// The opening kickoff runs through exactly the same routine as one
        /// after a goal, so there is only ever one way play begins.
        /// </summary>
        public void StartInitialKickoff()
        {
            isMatchStarted = true;

            // The crowd comes in here and nowhere earlier. Started on Awake it
            // would be roaring behind the title screen and the team sheet, over
            // a stadium with nobody playing in it. Idempotent, so the second
            // half picks the same bed back up rather than restarting it.
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PlayStadiumLoop();
                Audio.AudioManager.Instance.ResumeCrowd();
            }

            // Applied at the whistle rather than when the menu closed, so it
            // also covers the substitutes who were never on the pitch to be
            // repainted. Idempotent, so the second half repeating it is free.
            ApplyHumanKit();

            // Nobody has conceded anything, so the kickoff goes by convention:
            // the human opens the match, and the other side opens the second
            // half. Derived from the half rather than remembered, so a restarted
            // match cannot inherit the last one's turn.
            kickoffTeam = currentHalf >= HalvesPerMatch ? Opponent(humanTeam) : humanTeam;

            // The opposition is lined up and given its armband once, at the
            // opening whistle. Doing it again at the interval would re-sort the
            // side by depth and hand players roles they did not have a moment
            // earlier — which, after a substitution, reads as the game undoing
            // the change you just made.
            if (currentHalf < HalvesPerMatch)
            {
                SetUpRivalSide();
            }

            BeginKickoff();
        }

        /// <summary>
        /// Puts the opposition into the shape the player asked for and gives it
        /// a captain.
        ///
        /// The random shape is rolled HERE rather than when the menu closes, so
        /// "Aleatoria" cannot be read off the pitch behind the team sheet before
        /// the player has committed to their own.
        /// </summary>
        private void SetUpRivalSide()
        {
            TeamId rival = Opponent(humanTeam);

            FormationType shape = randomiseRivalFormation
                ? Formations.Random()
                : rivalFormation;

            ApplyFormation(rival, shape);

            if (redCaptain == null || redCaptain.team != rival || !redCaptain.isStarter)
            {
                redCaptain = PickRandomCaptain(rival);
            }

            SetCaptain(rival, redCaptain);
        }

        /// <summary>
        /// Any outfield starter will do. The keeper is skipped: a keeper captain
        /// is legal in football and dull here, since his line only ever hands out
        /// the defensive passive and he is the one player who never leaves his
        /// own box to use it.
        /// </summary>
        private static TeamMember PickRandomCaptain(TeamId team)
        {
            List<TeamMember> candidates = new List<TeamMember>();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team == team && member.isStarter && !member.isGoalkeeper)
                {
                    candidates.Add(member);
                }
            }

            return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : null;
        }

        /// <summary>
        /// Hands one side's armband to <paramref name="captain"/> and pushes the
        /// resulting passive onto every player on that side.
        ///
        /// The buff is written into the players rather than looked up per duel
        /// on purpose. The stat assets are SHARED — every striker in the match
        /// points at one StrikerStats — so a captaincy applied to the asset
        /// would buff both teams at once, and a captaincy resolved at read time
        /// would walk the squad six times per duel to find out who was wearing
        /// it.
        /// </summary>
        public void SetCaptain(TeamId team, TeamMember captain)
        {
            if (captain != null && captain.team != team)
            {
                Debug.LogWarning($"{captain.name} no juega en {team}: no puede ser su capitán.");
                return;
            }

            if (team == humanTeam)
            {
                blueCaptain = captain;
            }
            else
            {
                redCaptain = captain;
            }

            int attackBonus = 0;
            int defenceBonus = 0;
            float drainMultiplier = 1f;

            if (captain != null)
            {
                switch (captain.role)
                {
                    case PlayerRole.Forward:
                        attackBonus = captainStatBonus;
                        break;

                    case PlayerRole.Midfielder:
                        drainMultiplier = captainStaminaDrainMultiplier;
                        break;

                    // Defenders and keepers both harden the back of the side.
                    default:
                        defenceBonus = captainStatBonus;
                        break;
                }
            }

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team)
                {
                    continue;
                }

                // Cleared on everybody first, so moving the armband cannot leave
                // the old captain still flagged.
                member.isCaptain = member == captain;
                member.ApplyCaptainBonuses(attackBonus, defenceBonus, drainMultiplier);
            }

            if (captain == null)
            {
                Debug.LogWarning($"El equipo {team} se queda sin capitán.");
                return;
            }

            Debug.Log($"Capitán de {team}: dorsal {captain.jerseyNumber} " +
                      $"({PlayerRoles.Describe(captain.role)}) — " +
                      $"ataque +{attackBonus}, defensa +{defenceBonus}, desgaste x{drainMultiplier:F2}.");
        }

        private static TeamId Opponent(TeamId team)
        {
            return team == TeamId.Blue ? TeamId.Red : TeamId.Blue;
        }

        /// <summary>
        /// Lines a side up in <paramref name="formation"/>: re-roles every
        /// outfield player, walks them onto their new slot, and tells both the
        /// drift and the restart logic that the slot has moved.
        ///
        /// Updating those two is the whole job. The physical move alone lasts
        /// about a second — the off-the-ball drift would walk everyone back to
        /// where they spawned, and the first goal would snap them there outright.
        ///
        /// The keeper is left alone: he is not part of the shape, and every
        /// formation here is the six in front of him.
        /// </summary>
        public void ApplyFormation(TeamId team, FormationType formation)
        {
            List<TeamMember> outfield = CollectOutfield(team);

            if (outfield.Count != Formations.OutfieldCount)
            {
                Debug.LogWarning($"El equipo {team} tiene {outfield.Count} jugadores de campo " +
                                 $"y la formación espera {Formations.OutfieldCount}. Se colocan los que haya.");
            }

            // Deepest first, so the players already at the back become the back
            // line. FindObjectsByType returns no particular order, and without
            // sorting the same choice could shuffle the squad differently every
            // time it was made.
            float attackDirection = team == TeamId.Blue ? 1f : -1f;

            outfield.Sort((a, b) =>
            {
                float advanceA = a.transform.position.z * attackDirection;
                float advanceB = b.transform.position.z * attackDirection;

                int byDepth = advanceA.CompareTo(advanceB);

                return byDepth != 0 ? byDepth : a.transform.position.x.CompareTo(b.transform.position.x);
            });

            FormationSlot[] slots = Formations.Get(formation);
            float side = -attackDirection;

            int assigned = Mathf.Min(outfield.Count, slots.Length);

            for (int i = 0; i < assigned; i++)
            {
                PlaceInSlot(outfield[i], slots[i], side);
            }

            Debug.Log($"Formación {Formations.GetLabel(formation)} aplicada a {team}: " +
                      $"{assigned} jugadores de campo colocados.");
        }

        /// <summary>
        /// Moves one player into a slot and makes every system that remembers a
        /// position agree about where that player now lives.
        /// </summary>
        private static void PlaceInSlot(TeamMember member, FormationSlot slot, float side)
        {
            member.role = slot.Role;

            Vector3 position = new Vector3(slot.X, member.transform.position.y, side * slot.OwnHalfZ);

            // Any run still in progress would drag the player straight back off
            // the slot they have just been given.
            if (member.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
                route.SetFormationSlot(position);
            }

            if (member.TryGetComponent(out AI.TacticalPositioning positioning))
            {
                positioning.SetFormationSlot(position);
            }

            member.transform.position = position;
        }

        private static List<TeamMember> CollectOutfield(TeamId team)
        {
            List<TeamMember> outfield = new List<TeamMember>();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team || member.isGoalkeeper || !member.isStarter)
                {
                    continue;
                }

                outfield.Add(member);
            }

            return outfield;
        }

        private void Update()
        {
            if (isMatchOver || isHalftime)
            {
                return;
            }

            // The whistle has gone and the closing routine owns the match now.
            if (isEndingHalf)
            {
                return;
            }

            // Something is on screen waiting to be answered or watched out.
            // Ending the half underneath it would tear it down mid-decision and,
            // worse, race its own resolution: both of them write timeScale and
            // whichever finished second would win.
            //
            // Nothing is lost by waiting. The clock is already at zero and stays
            // there, so the first Update after the pitch clears blows the whistle
            // exactly as it would have.
            if (IsPitchInterrupted)
            {
                return;
            }

            // The clock does not run while the ball is dead. Arranging the
            // formation or lining up a restart is deliberation, not play, and
            // charging the match for it would punish thinking.
            if (IsWaitingForSetPiece)
            {
                // Unless the clock had already run out and we were only playing
                // on for an attack: a restart means that attack is over, whether
                // it ended in a goal, a throw-in or a goal kick.
                if (isInStoppageTime)
                {
                    BeginEndOfHalf("La jugada acaba en balón parado");
                }

                return;
            }

            if (isInStoppageTime)
            {
                // Time is already up; the only question left is whether the
                // attack that bought the extra seconds is still alive.
                if (!IsPromisingAttack())
                {
                    BeginEndOfHalf("La jugada se apaga");
                }

                return;
            }

            currentTime -= Time.deltaTime;

            if (currentTime > 0f)
            {
                return;
            }

            currentTime = 0f;

            // The clock is not the referee. Cutting a match dead the instant it
            // hits zero takes a goal away from whoever happened to be shaping to
            // shoot, so an attack in the final third plays on with the clock
            // frozen at zero until it resolves itself.
            if (IsPromisingAttack())
            {
                isInStoppageTime = true;

                Announce("announce.stoppage");
                Debug.Log("TIEMPO CUMPLIDO, pero hay ataque en el último tercio: se juega el descuento.");

                return;
            }

            BeginEndOfHalf("Tiempo cumplido");
        }

        /// <summary>
        /// True while one side is carrying the ball in the final third of the
        /// pitch they are attacking.
        ///
        /// Possession is the whole test. A loose ball is not an attack — it is
        /// the moment an attack ended — so a clearance, a save or a ball rolling
        /// out of play all end the stoppage on their own without needing a case
        /// each.
        /// </summary>
        private bool IsPromisingAttack()
        {
            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (!member.isStarter)
                {
                    continue;
                }

                if (!member.TryGetComponent(out PlayerBallHandler handler) || !handler.HasBall)
                {
                    continue;
                }

                // How far this player has advanced towards the goal they attack,
                // which is the opposite end from the one they defend.
                float advance = member.transform.position.z * -PitchBounds.DefendedSide(member.team);

                return advance >= PitchBounds.GoalLineZ * (1f - AttackingThirdShare);
            }

            return false;
        }

        /// <summary>
        /// Ends the half right now, skipping any stoppage time.
        ///
        /// For the developer menu. It goes through BeginEndOfHalf like the clock
        /// does rather than calling BeginHalftime or EndMatch directly, so the
        /// forced ending is the same ending — closing delay, announcement and the
        /// half-versus-full-time decision all included — instead of a second path
        /// that can drift away from the real one.
        /// </summary>
        public void ForceEndOfHalf()
        {
            if (isMatchOver || isHalftime || isEndingHalf)
            {
                return;
            }

            currentTime = 0f;
            isInStoppageTime = false;

            BeginEndOfHalf("Forzado desde el menú de desarrollo");
        }

        /// <summary>
        /// Blows the whistle, then waits before the screen takes over.
        ///
        /// The delay is the point: freezing the pitch on the same frame as the
        /// final touch means the last thing that happened in the half is never
        /// actually seen. Guarded rather than trusted — the clock, a restart and
        /// a dying attack can all reach this on the same frame.
        /// </summary>
        private void BeginEndOfHalf(string reason)
        {
            if (isEndingHalf)
            {
                return;
            }

            isInStoppageTime = false;
            isEndingHalf = true;

            Debug.Log($"{reason}: fin de la {currentHalf}ª parte en {endOfHalfDelay:F1} s.");

            StartCoroutine(EndHalfRoutine());
        }

        private IEnumerator EndHalfRoutine()
        {
            bool isFullTime = currentHalf >= HalvesPerMatch;

            Announce(isFullTime ? "announce.fullTime" : "announce.halfTime");

            // Blown here, at the top, not after the wait below. The wait exists
            // so the last action of the half can be watched out — the whistle is
            // what starts it, the same way the referee's does.
            if (Audio.AudioManager.Instance != null)
            {
                if (isFullTime)
                {
                    Audio.AudioManager.Instance.PlayFullTimeWhistle();
                }
                else
                {
                    Audio.AudioManager.Instance.PlayWhistle(isLong: true);
                }
            }

            // Realtime, and the match keeps running underneath at normal speed:
            // this is a beat to watch the ball come to rest, not a freeze. A
            // duel or a goal in these seconds resolves normally.
            yield return new WaitForSecondsRealtime(endOfHalfDelay);

            // ...and because the match IS still live through that beat, a duel,
            // a goal or a penalty can start inside it. The guard in Update only
            // covers the decision to blow the whistle; by here the whistle has
            // already gone, so nothing was stopping this from dropping the
            // interval screen on top of a frozen duel — which is how the match
            // locked up: the duel panel ended up under the team talk with
            // timeScale at 0 and no way left to answer it, and Update, seeing a
            // clash still active, never ran again.
            //
            // Realtime again, and unbounded on purpose: a duel is answered by
            // the player, so there is no timeout that would not eventually fire
            // in the middle of somebody thinking. Every interruption here ends
            // by itself.
            while (IsPitchInterrupted)
            {
                yield return null;
            }

            isEndingHalf = false;

            if (currentHalf < HalvesPerMatch)
            {
                BeginHalftime();
                yield break;
            }

            EndMatch();
        }

        /// <summary>
        /// The interval. The match is frozen and handed to the team talk screen,
        /// which is the only thing that can send the sides back out.
        ///
        /// The AI takes its substitutions HERE, before the freeze, rather than
        /// from the interval screen: a side has to change its blown players
        /// whether or not anybody is looking at a menu, and making the UI
        /// responsible for the opposition's team sheet would mean the AI never
        /// substituted at all if the human skipped straight through.
        /// </summary>
        private void BeginHalftime()
        {
            ClearSetPieceFlags();

            PerformAISubstitutions();

            isHalftime = true;

            Time.timeScale = MatchOverTimeScale;

            // The stands go quiet for the team talk and the team sheet. Paused,
            // not stopped: this is a break in the same match, and a bed that
            // restarted from sample zero would announce the cut instead of
            // hiding it.
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PauseCrowd();
            }

            Debug.Log($"DESCANSO. Fin de la {currentHalf}ª parte.");

            TacticalEvents.OnHalftime?.Invoke();
        }

        /// <summary>
        /// Sends the teams back out. Called by the interval screen, not from a
        /// timer: the second half starts when the manager says so.
        ///
        /// The kickoff goes to the side that did NOT take the opening one, which
        /// is what StartInitialKickoff already works out from the half number.
        /// </summary>
        public void StartSecondHalf()
        {
            if (isMatchOver || !isHalftime)
            {
                return;
            }

            isHalftime = false;
            isInStoppageTime = false;
            isEndingHalf = false;
            currentHalf = HalvesPerMatch;
            currentTime = matchDuration;

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            // Both sides walk back out into their shape before the whistle. The
            // first half ends wherever it ended — a side camped in the other's
            // box, a defender who chased a loose ball into a corner — and the
            // kickoff only ever moved the taker, so the second half used to
            // start from the sprawl the first one finished in.
            RestoreFormationPositions();

            Debug.Log("Comienza la 2ª parte.");

            StartInitialKickoff();
        }

        private void EndMatch()
        {
            isMatchOver = true;
            isHalftime = false;
            ClearSetPieceFlags();

            Time.timeScale = MatchOverTimeScale;

            // Stopped outright, not paused: unlike the interval there is nothing
            // left to resume into. The result screen and the menu behind it are
            // both meant to be quiet.
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.StopCrowd();
            }

            // Settled BEFORE the event, so the result screen that listens for it
            // can read the outcome the tournament just worked out rather than
            // racing it.
            ReportTournamentResult();

            Debug.Log("¡FINAL DEL PARTIDO!");

            TacticalEvents.OnMatchOver?.Invoke();
        }

        /// <summary>
        /// Hands the final score to the tournament, if this was a tournament
        /// match. Does nothing for a quick match — the tournament ignores a
        /// result it did not start a round for.
        ///
        /// The human's goals are read by TEAM rather than assumed to be the blue
        /// column, because humanTeam is a field and nothing here should quietly
        /// start lying if it is ever flipped.
        /// </summary>
        private void ReportTournamentResult()
        {
            if (TournamentManager.Instance == null || ScoreManager.Instance == null)
            {
                return;
            }

            int blue = ScoreManager.Instance.BlueScore;
            int red = ScoreManager.Instance.RedScore;

            bool humanIsBlue = humanTeam == TeamId.Blue;

            TournamentManager.Instance.ReportResult(
                humanIsBlue ? blue : red,
                humanIsBlue ? red : blue);
        }

        /// <summary>
        /// The AI's team sheet at the interval: every blown starter comes off
        /// for the freshest substitute available.
        ///
        /// Only the side the human does not control is touched — the human makes
        /// his own changes on the substitutions board, and having the game make
        /// them for him would be taking the decision away.
        ///
        /// The keeper is skipped: there is no keeper on the bench to replace
        /// him with, so swapping him out would leave the goal genuinely
        /// undefended. Same rule the substitutions board enforces by hand.
        /// </summary>
        public void PerformAISubstitutions()
        {
            TeamId aiTeam = Opponent(humanTeam);

            List<TeamMember> tired = new List<TeamMember>();
            List<TeamMember> bench = new List<TeamMember>();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != aiTeam || member.isGoalkeeper)
                {
                    continue;
                }

                if (member.isStarter)
                {
                    // Anything under a full tank by this much is worth changing.
                    // It used to be IsExhausted, which with a 300 tank meant the
                    // last 60 units — a state a player barely reaches inside one
                    // half, so the AI almost never made a substitution at all.
                    if (member.StaminaFraction < tiredSubstitutionFraction)
                    {
                        tired.Add(member);
                    }

                    continue;
                }

                // Only a completely fresh man is worth bringing on. A substitute
                // who has already been on and come off again would be replacing
                // tired legs with tired legs.
                if (member.StaminaFraction >= 1f)
                {
                    bench.Add(member);
                }
            }

            if (tired.Count == 0 || bench.Count == 0)
            {
                Debug.Log($"[IA] Sin cambios en el descanso: {tired.Count} cansados por debajo del " +
                          $"{tiredSubstitutionFraction:P0}, {bench.Count} suplentes al 100%.");
                return;
            }

            // Emptiest man off first: with only three on the bench the order is
            // the whole decision, and taking them in whatever order the scene
            // happened to return would spend the fresh legs on whoever was
            // merely a little tired.
            tired.Sort((a, b) => a.currentStamina.CompareTo(b.currentStamina));

            int changes = 0;
            int refused = 0;

            foreach (TeamMember outgoing in tired)
            {
                // Like for like. A back three that loses a defender and gains a
                // forward is not a substitution, it is a different formation —
                // and the shape is not re-applied at the interval on purpose, so
                // nothing downstream would ever put that right.
                TeamMember incoming = null;

                foreach (TeamMember candidate in bench)
                {
                    if (candidate.role == outgoing.role)
                    {
                        incoming = candidate;
                        break;
                    }
                }

                if (incoming == null)
                {
                    refused++;
                    continue;
                }

                bench.Remove(incoming);
                SwapPlayers(outgoing, incoming);
                changes++;
            }

            Debug.Log($"[IA] {changes} cambio(s) en el descanso para {aiTeam}" +
                      (refused > 0
                          ? $"; {refused} sin relevo del mismo rol en el banquillo."
                          : "."));
        }

        /// <summary>
        /// Puts the ball into play. Called the moment the taker commits to a
        /// pass or a shot, which is what "under way" actually means — and it
        /// clears every kind of restart, because they are all taken with a pass.
        /// </summary>
        public void EndKickoff()
        {
            ClearSetPieceFlags();
        }

        private void ClearSetPieceFlags()
        {
            isWaitingForKickoff = false;
            isWaitingForThrowIn = false;
            isWaitingForCorner = false;
            isWaitingForGoalKick = false;
            isWaitingForFreeKick = false;
            isWaitingForPenalty = false;
        }

        /// <summary>
        /// Turns a foul into the right restart: a penalty if it happened inside
        /// the offender's own box, a free kick anywhere else.
        ///
        /// The offender's own box, specifically — not "a box". The same patch of
        /// grass is a penalty against the side defending it and a free kick in a
        /// harmless position for the side attacking it, so the question can only
        /// be asked about a named team.
        /// </summary>
        private void HandleFoul(TeamMember offender)
        {
            if (offender == null || isMatchOver)
            {
                return;
            }

            RecordFoul(offender.team);

            Vector3 spot = offender.transform.position;

            // The restart always goes to the OTHER side. Stated here as the one
            // rule rather than worked out again at each call site: whoever gave
            // the foul away cannot also be the team that takes it, and the two
            // restarts below are the only places that decision is made.
            TeamId attackingTeam = Opponent(offender.team);

            Debug.Log($"Falta de {offender.name} ({offender.team}) en " +
                      $"({spot.x:F1}, {spot.z:F1}) -> saque para {attackingTeam}.");

            // Whoever is carrying loses it here, before anybody is moved. The
            // restart hands the ball out itself, but the losing side has to stop
            // believing it still has possession first — otherwise its AI spends
            // the whole set piece running a play that no longer exists.
            ClearPossession();

            if (PitchBounds.IsInsidePenaltyArea(spot, offender.team))
            {
                StartPenaltyKick(attackingTeam);
                return;
            }

            StartFreeKick(spot, attackingTeam);
        }

        /// <summary>
        /// Free kick to <paramref name="attackingTeam"/> from where the foul
        /// happened. Played exactly like every other restart: the ball is put on
        /// the spot, everyone stands still, and play resumes on the first pass.
        /// </summary>
        public void StartFreeKick(Vector3 foulPosition, TeamId attackingTeam)
        {
            if (isMatchOver)
            {
                return;
            }

            PlayerBallHandler taker = FindRestartTaker(attackingTeam, foulPosition);

            if (taker == null)
            {
                Debug.LogWarning($"El equipo {attackingTeam} no tiene jugadores de campo para el libre directo.");
                return;
            }

            Vector3 spot = new Vector3(foulPosition.x, taker.transform.position.y, foulPosition.z);

            if (!PlaceTaker(taker, spot))
            {
                return;
            }

            // The offender is standing exactly where the ball has just been put,
            // because the foul mark IS where he was. Two capsules in the same
            // place jam against each other: the taker cannot walk the ball out,
            // and the opposition's AI keeps sending men at a ball that is
            // physically blocked, which reads as the whole match freezing.
            //
            // Measured from the MARK, not from the ball's live position. The ball
            // was handed to the taker a line ago, but it does not physically move
            // onto his socket until LateUpdate — so reading its transform here
            // gives wherever it was BEFORE the foul, and the players get pushed
            // away from the wrong point.
            SeparateFromRestart(ClampToRestartArea(spot), taker);

            isWaitingForFreeKick = true;

            CenterCameraOnPlay();

            Announce("announce.foul");

            Debug.Log($"FALTA para {attackingTeam}: saca {taker.name} desde " +
                      $"({spot.x:F1}, {spot.z:F1}).");

            // Aimed upfield, towards the goal being attacked.
            float attackDirection = -PitchBounds.DefendedSide(attackingTeam);

            ScheduleAiRestart(attackingTeam, taker,
                new Vector3(spot.x, 0f, spot.z + (attackDirection * throwInDistance)));
        }

        /// <summary>
        /// Takes the ball off whoever has it and stops the run they were on.
        ///
        /// Both halves matter. Clearing possession alone leaves the player
        /// walking a route that was drawn for a passage of play that has just
        /// been whistled dead — he arrives at the far post seconds later with
        /// nothing to do there — and cancelling the route alone leaves him
        /// reporting HasBall for a ball that is about to be somewhere else.
        /// </summary>
        private static void ClearPossession()
        {
            BallController ball = BallController.Instance;

            if (ball == null || ball.Holder == null)
            {
                return;
            }

            if (ball.Holder.TryGetComponent(out PlayerBallHandler handler))
            {
                handler.ForceDropBall();
            }

            if (ball.Holder.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
            }

            ball.Release();
        }

        /// <summary>
        /// Pushes everybody who is not taking the restart clear of the ball.
        ///
        /// A free kick is the one restart taken from wherever the last thing
        /// happened, so unlike a corner or a kickoff there is no guarantee the
        /// mark is empty — it is, by definition, the spot two players were
        /// wrestling over a moment ago. Overlapping capsules will not push each
        /// other apart on their own here, because the match is standing still
        /// waiting for the kick.
        ///
        /// Pushed radially outward rather than "backwards": the offender may be
        /// on any side of the ball, and shoving him along a fixed axis would as
        /// often move him into the taker as away from him.
        /// </summary>
        private void SeparateFromRestart(Vector3 ballSpot, PlayerBallHandler taker)
        {
            int moved = 0;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (!member.isStarter)
                {
                    continue;
                }

                // The taker is the one player who must not be moved: he is
                // standing on the mark with the ball on his foot. Compared by
                // reference rather than by distance — PlaceTaker offsets him from
                // the mark by the socket, so he is never exactly on it.
                if (taker != null && member.gameObject == taker.gameObject)
                {
                    continue;
                }

                Vector3 away = member.transform.position - ballSpot;
                away.y = 0f;

                float distance = away.magnitude;

                if (distance >= restartClearanceRadius)
                {
                    continue;
                }

                // Dead centre on the mark gives no direction to push along, so
                // one is chosen rather than dividing by zero.
                Vector3 direction = distance > 0.01f ? away / distance : Vector3.back;

                Vector3 target = ballSpot + (direction * restartClearanceRadius);
                target.y = member.transform.position.y;

                member.transform.position = PitchBounds.ClampPlayer(target);
                moved++;
            }

            if (moved > 0)
            {
                Debug.Log($"Falta: {moved} jugador(es) apartados {restartClearanceRadius:F1} u del balón.");
            }
        }

        /// <summary>
        /// Penalty to <paramref name="attackingTeam"/>.
        ///
        /// Nothing is placed on the pitch and no taker is chosen: the penalty is
        /// a menu, not a passage of play, and the ball only moves once a side has
        /// been picked. The match is frozen until then.
        /// </summary>
        public void StartPenaltyKick(TeamId attackingTeam)
        {
            if (isMatchOver)
            {
                return;
            }

            isWaitingForPenalty = true;

            Announce("announce.penalty");

            Debug.Log($"PENALTI para {attackingTeam}.");

            StagePenalty(attackingTeam);

            if (UI.PenaltyUIController.Instance != null)
            {
                UI.PenaltyUIController.Instance.ShowPenalty(attackingTeam);
                return;
            }

            // No menu in the scene to take it with. Rather than freeze the match
            // on a penalty nobody can ever take, it is waved away and play
            // restarts from the centre.
            Debug.LogWarning("No hay PenaltyUIController: el penalti se anula y se reanuda desde el centro.");

            isWaitingForPenalty = false;

            BallController ball = BallController.Instance;

            if (ball != null)
            {
                ball.ResetToKickoff();
            }
        }

        /// <summary>
        /// Where the ball sits for the penalty about to be taken. Read by the
        /// menu so the kick can be animated towards the right goal.
        /// </summary>
        public Vector3 PenaltySpot { get; private set; }

        /// <summary>Centre of the goal being shot at.</summary>
        public Vector3 PenaltyGoalCentre { get; private set; }

        private TeamMember penaltyTaker;
        private TeamMember penaltyKeeper;

        /// <summary>
        /// The keeper staged for the penalty, so the shot cinematic can dive him
        /// while the ball is in the air.
        ///
        /// Exposed as a Transform rather than as the TeamMember: the only thing
        /// the cinematic is allowed to do to him is move him, and handing over
        /// the component would hand over his stats and his possession with it.
        /// </summary>
        public Transform PenaltyKeeper => penaltyKeeper != null ? penaltyKeeper.transform : null;
        private Vector3 penaltyTakerOrigin;
        private Vector3 penaltyKeeperOrigin;

        /// <summary>
        /// Puts a striker on the spot and a keeper on his line, so the penalty is
        /// something you watch rather than two buttons over a pitch where nobody
        /// has moved.
        ///
        /// Both original positions are kept, because a penalty is not a restart:
        /// play resumes from wherever it was, and dragging two players across the
        /// pitch permanently would quietly reshape both sides every time one was
        /// given.
        /// </summary>
        private void StagePenalty(TeamId attackingTeam)
        {
            TeamId defendingTeam = Opponent(attackingTeam);

            float attackDirection = -PitchBounds.DefendedSide(attackingTeam);

            PenaltyGoalCentre = new Vector3(0f, 0.5f, attackDirection * PitchBounds.GoalLineZ);
            PenaltySpot = new Vector3(0f, 0.5f, attackDirection * (PitchBounds.GoalLineZ - penaltySpotDepth));

            PlayerBallHandler takerHandler = FindNearestFieldPlayer(attackingTeam, PenaltySpot);
            PlayerBallHandler keeperHandler = FindGoalkeeper(defendingTeam);

            penaltyTaker = takerHandler != null ? takerHandler.GetComponent<TeamMember>() : null;
            penaltyKeeper = keeperHandler != null ? keeperHandler.GetComponent<TeamMember>() : null;

            if (penaltyTaker != null)
            {
                penaltyTakerOrigin = penaltyTaker.transform.position;

                if (penaltyTaker.TryGetComponent(out PlayerRoute takerRoute))
                {
                    takerRoute.CancelRoute();
                }

                // A stride behind the ball, facing the goal.
                penaltyTaker.transform.position = new Vector3(
                    PenaltySpot.x,
                    penaltyTaker.transform.position.y,
                    PenaltySpot.z - (attackDirection * 1.2f));

                takerHandler.ForceTakeBall(BallController.Instance);
            }

            if (penaltyKeeper != null)
            {
                penaltyKeeperOrigin = penaltyKeeper.transform.position;

                if (penaltyKeeper.TryGetComponent(out PlayerRoute keeperRoute))
                {
                    keeperRoute.CancelRoute();
                }

                penaltyKeeper.transform.position = new Vector3(
                    0f,
                    penaltyKeeper.transform.position.y,
                    attackDirection * (PitchBounds.GoalLineZ - 0.6f));
            }

            // Everybody else out of the box. A penalty is the striker, the keeper
            // and nobody else — twenty players still standing where the foul left
            // them clutter the one shot the whole sequence exists to show, and
            // their colliders sit in the ball's flight path.
            ClearPenaltyArea(defendingTeam);

            // The ball is put on the spot AFTER the taker has been given it,
            // which releases it from him again — the kick is a flight from the
            // mark, not a carry.
            BallController ball = BallController.Instance;

            if (ball != null)
            {
                ball.Release();
                ball.transform.position = PenaltySpot;
            }

            if (takerHandler != null)
            {
                takerHandler.ForceDropBall();
            }

            CameraSystem.TacticalCamera.Instance?.ZoomToClash(penaltyTaker, penaltyKeeper);

            Debug.Log($"Penalti preparado: tira {(penaltyTaker != null ? penaltyTaker.name : "nadie")}, " +
                      $"para {(penaltyKeeper != null ? penaltyKeeper.name : "nadie")}, " +
                      $"balón en z={PenaltySpot.z:F1}.");
        }

        /// <summary>
        /// Walks everybody except the two principals out of the penalty area and
        /// back behind the halfway line.
        ///
        /// Their positions are not saved: UnstagePenalty restores the striker and
        /// the keeper, and everybody else is meant to stay where this puts them.
        /// A penalty is a break in play, and the sides re-forming around the
        /// halfway line is what a break in play looks like — restoring them into
        /// the tangle the foul happened in would only recreate the pile-up.
        /// </summary>
        private void ClearPenaltyArea(TeamId defendingTeam)
        {
            int moved = 0;
            float defendedSide = PitchBounds.DefendedSide(defendingTeam);

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (!member.isStarter || member == penaltyTaker || member == penaltyKeeper)
                {
                    continue;
                }

                if (!PitchBounds.IsInsidePenaltyArea(member.transform.position, defendingTeam))
                {
                    continue;
                }

                if (member.TryGetComponent(out PlayerRoute route))
                {
                    route.CancelRoute();
                }

                // Back towards the middle, spread across the width so they do not
                // all land on the same spot and jam into each other there
                // instead.
                float lane = ((moved % 5) - 2) * 3f;

                Vector3 target = new Vector3(
                    lane,
                    member.transform.position.y,
                    defendedSide * (PitchBounds.GoalLineZ - PitchBounds.PenaltyAreaDepth - 3f));

                member.transform.position = PitchBounds.ClampPlayer(target);
                moved++;
            }

            if (moved > 0)
            {
                Debug.Log($"Penalti: {moved} jugador(es) desalojados del área.");
            }
        }

        /// <summary>
        /// Puts the two players back where the match had them and hands the
        /// camera back to the follow rig.
        /// </summary>
        private void UnstagePenalty()
        {
            if (penaltyTaker != null)
            {
                penaltyTaker.transform.position = penaltyTakerOrigin;
                penaltyTaker = null;
            }

            if (penaltyKeeper != null)
            {
                penaltyKeeper.transform.position = penaltyKeeperOrigin;
                penaltyKeeper = null;
            }

            // Forced back rather than asked politely: the duel framing this
            // borrowed does not return the view on its own, and a camera left
            // staring at an empty penalty spot is a match played off screen.
            CameraSystem.TacticalCamera.Instance?.CenterCamera();
        }

        /// <summary>
        /// Called by the penalty menu once it has been taken and the outcome
        /// applied. A save resumes from a goal kick to the defending side; a goal
        /// has already started its own celebration and restart.
        /// </summary>
        public void EndPenalty(TeamId attackingTeam, bool scored)
        {
            isWaitingForPenalty = false;

            UnstagePenalty();

            if (scored)
            {
                return;
            }

            TeamId defendingTeam = Opponent(attackingTeam);

            // Gathered by the keeper, so play restarts from his hands.
            StartGoalKick(defendingTeam,
                new Vector3(0f, 0f, PitchBounds.DefendedSide(defendingTeam) * PitchBounds.GoalLineZ));
        }

        /// <summary>
        /// Sets up a throw-in for <paramref name="throwingTeam"/> at the point
        /// where the ball left the pitch.
        ///
        /// Deliberately does NOT raise OnMatchReset: a restart puts the ball
        /// back, not the match, and announcing a reset would snap every player
        /// back to their kickoff formation.
        /// </summary>
        public void StartThrowIn(TeamId throwingTeam, Vector3 outOfBoundsPos)
        {
            if (isMatchOver)
            {
                return;
            }

            ClearDrawnRoutes();

            PlayerBallHandler thrower = FindRestartTaker(throwingTeam, outOfBoundsPos);
            if (thrower == null)
            {
                Debug.LogWarning($"El equipo {throwingTeam} no tiene jugadores de campo para sacar de banda.");
                return;
            }

            // Stand the thrower ON the touchline the ball left by, level with
            // the exit point rather than wherever they happened to be.
            float sideline = Mathf.Sign(outOfBoundsPos.x) * PitchBounds.SideLineX;

            Vector3 spot = new Vector3(
                sideline,
                thrower.transform.position.y,
                Mathf.Clamp(outOfBoundsPos.z, -PitchBounds.GoalLineZ, PitchBounds.GoalLineZ));

            if (!PlaceTaker(thrower, spot))
            {
                return;
            }

            isWaitingForThrowIn = true;

            CenterCameraOnPlay();

            Announce("announce.throwIn");

            Debug.Log($"SAQUE DE BANDA para {throwingTeam}: saca {thrower.name} desde " +
                      $"x={sideline:F1}, z={spot.z:F1}.");

            // Aimed straight back infield, away from the line it went out over.
            ScheduleAiRestart(throwingTeam, thrower,
                new Vector3(sideline - (Mathf.Sign(sideline) * throwInDistance), 0f, spot.z));
        }

        /// <summary>
        /// Corner to <paramref name="attackingTeam"/>: the defending side put it
        /// behind their own goal line.
        /// </summary>
        public void StartCorner(TeamId attackingTeam, Vector3 outPos)
        {
            if (isMatchOver)
            {
                return;
            }

            ClearDrawnRoutes();

            float cornerX = Mathf.Sign(outPos.x) * PitchBounds.SideLineX;
            float cornerZ = Mathf.Sign(outPos.z) * PitchBounds.GoalLineZ;

            PlayerBallHandler taker = FindRestartTaker(attackingTeam, new Vector3(cornerX, 0f, cornerZ));
            if (taker == null)
            {
                Debug.LogWarning($"El equipo {attackingTeam} no tiene jugadores de campo para sacar de esquina.");
                return;
            }

            Vector3 spot = new Vector3(cornerX, taker.transform.position.y, cornerZ);

            if (!PlaceTaker(taker, spot))
            {
                return;
            }

            isWaitingForCorner = true;

            CenterCameraOnPlay();

            Announce("announce.corner");

            Debug.Log($"CÓRNER para {attackingTeam}: saca {taker.name} desde ({cornerX:F1}, {cornerZ:F1}).");

            // Swung into the six-yard area in front of the goal being attacked.
            ScheduleAiRestart(attackingTeam, taker,
                new Vector3(0f, 0f, Mathf.Sign(cornerZ) * (PitchBounds.GoalLineZ - 4f)));
        }

        /// <summary>
        /// Goal kick to <paramref name="defendingTeam"/>: the attacking side put
        /// it behind the goal line without scoring.
        /// </summary>
        public void StartGoalKick(TeamId defendingTeam, Vector3 outPos)
        {
            if (isMatchOver)
            {
                return;
            }

            ClearDrawnRoutes();

            PlayerBallHandler keeper = FindGoalkeeper(defendingTeam);
            if (keeper == null)
            {
                Debug.LogWarning($"El equipo {defendingTeam} no tiene portero para sacar de puerta.");
                return;
            }

            float side = Mathf.Sign(outPos.z);

            Vector3 spot = new Vector3(
                0f,
                keeper.transform.position.y,
                side * (PitchBounds.GoalLineZ - goalKickDepth));

            if (!PlaceTaker(keeper, spot))
            {
                return;
            }

            isWaitingForGoalKick = true;

            CenterCameraOnPlay();

            Announce("announce.goalKick");

            Debug.Log($"SAQUE DE PUERTA para {defendingTeam}: saca {keeper.name} desde z={spot.z:F1}.");

            // Hoofed upfield, away from the goal being defended.
            ScheduleAiRestart(defendingTeam, keeper,
                new Vector3(0f, 0f, spot.z - (side * goalKickDistance)));
        }

        /// <summary>
        /// Moves a player onto the restart spot and gives them the ball. Any run
        /// they were on is cancelled first, or the route would immediately drag
        /// them back off the mark.
        /// </summary>
        /// <param name="offerSupport">
        /// Whether the taker's team-mates rearrange themselves to offer for the
        /// pass. False for the kickoff, which is the one restart where both
        /// sides are supposed to be standing in their formation — dragging the
        /// forwards onto the centre spot there would undo the shape the team
        /// sheet has just set.
        /// </param>
        private bool PlaceTaker(PlayerBallHandler taker, Vector3 spot, bool offerSupport = true)
        {
            BallController ball = BallController.Instance;
            if (ball == null)
            {
                Debug.LogWarning("No hay balón: no se puede preparar el saque.");
                return false;
            }

            if (taker.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
            }

            // The spot is where the BALL goes, and the ball rides on a socket
            // about half a metre behind the player. Standing the taker on the
            // mark therefore left the ball just outside it — which at a corner
            // meant behind the goal line, still out of play by the check that
            // had awarded the corner in the first place, so the same corner was
            // awarded again on the next frame, and the next.
            Vector3 ballSpot = ClampToRestartArea(spot);
            Vector3 offset = taker.BallOffset;

            taker.transform.position = new Vector3(
                ballSpot.x - offset.x,
                spot.y,
                ballSpot.z - offset.z);

            taker.ForceTakeBall(ball);

            // Everybody who is not taking it gets out of the way. Done here
            // rather than in each restart because this is the one place all five
            // of them pass through — throw-in, corner, goal kick, free kick and
            // kickoff — and a rule about distance that only held for some of
            // them would be no rule at all.
            ClearExclusionZone(ballSpot, taker);

            if (offerSupport)
            {
                OfferForRestart(taker, ballSpot);
            }

            return true;
        }

        /// <summary>
        /// Cuts and erases every drawn route the moment play stops.
        ///
        /// A route is two things at once — a line painted across the pitch and a
        /// player running along it — and a restart ends both. Left alone the
        /// line stayed on screen through the whole stoppage and the runner kept
        /// going, so a throw-in was taken with half the side still carrying out
        /// orders given before the whistle.
        ///
        /// The gesture in progress is cancelled FIRST. Cancelling the routes
        /// without it would clear a line that the finger, still down, would
        /// immediately start drawing again.
        ///
        /// Public and static because the whistle is blown from two places: the
        /// restarts here, and the foul, which is called the moment the referee
        /// decides rather than a second and a half later when the free kick is
        /// finally placed.
        /// </summary>
        public static void ClearDrawnRoutes()
        {
            TacticalSoccer.Input.TacticalInputManager input =
                FindAnyObjectByType<TacticalSoccer.Input.TacticalInputManager>();

            if (input != null)
            {
                input.CancelActiveGesture();
            }

            int cleared = 0;

            foreach (PlayerRoute route in FindObjectsByType<PlayerRoute>())
            {
                // Asked before cancelling: CancelRoute is safe on a player who
                // was doing nothing, but counting them all would report every
                // restart as having wiped twenty routes.
                bool wasActive = route.IsFollowingRoute;

                route.CancelRoute();

                if (wasActive)
                {
                    cleared++;
                }
            }

            if (cleared > 0)
            {
                Debug.Log($"[Balón parado] {cleared} ruta(s) cortada(s) al detenerse el juego.");
            }
        }

        /// <summary>
        /// Backs the defending side off the ball before a restart.
        ///
        /// This is the ten yards a referee paces out, and it is enforced for
        /// BOTH sides: the reported case was a human player standing on top of
        /// the AI's throw-in and taking it straight back, but the same thing
        /// happens the other way and neither is a restart. The filter is the
        /// TEAM, never who is controlling it.
        ///
        /// Each offender is pushed straight out along the line from the ball
        /// through where they are standing, which is the shortest way out and
        /// keeps whatever shape the side had. Their drawn route is cancelled
        /// too, or a human who had ordered a run would simply jog back onto the
        /// ball while the taker was still picking it up.
        ///
        /// The goalkeeper is left alone. He is the one player whose position is
        /// not a choice — dragged off his line for a throw-in near the box he
        /// would leave an open goal, which is a worse outcome than standing a
        /// little close.
        /// </summary>
        private void ClearExclusionZone(Vector3 ballSpot, PlayerBallHandler taker)
        {
            TeamMember takerMember = taker != null ? taker.GetComponent<TeamMember>() : null;

            if (takerMember == null)
            {
                return;
            }

            int moved = 0;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team == takerMember.team || !member.isStarter || member.isGoalkeeper)
                {
                    continue;
                }

                Vector3 position = member.transform.position;

                // Flat distance: the ball's height at a restart is the socket's,
                // and nobody is closer for standing on lower ground.
                Vector3 away = new Vector3(position.x - ballSpot.x, 0f, position.z - ballSpot.z);
                float distance = away.magnitude;

                if (distance >= restartExclusionRadius)
                {
                    continue;
                }

                // Standing exactly on the ball leaves no line to push along, so
                // the retreat is towards the player's own goal — which is where
                // a defender backing off would go anyway.
                Vector3 direction = distance > 0.01f
                    ? away / distance
                    : new Vector3(0f, 0f, member.team == TeamId.Blue ? -1f : 1f);

                Vector3 pushed = PitchBounds.ClampPlayer(new Vector3(
                    ballSpot.x + (direction.x * restartExclusionRadius),
                    position.y,
                    ballSpot.z + (direction.z * restartExclusionRadius)));

                // The clamp can hand back a spot still inside the circle — a
                // corner is the obvious case, where "straight out" is straight
                // off the pitch. Then the retreat goes towards the middle
                // instead, which is always somewhere there is room.
                if (Vector3.Distance(new Vector3(pushed.x, 0f, pushed.z),
                        new Vector3(ballSpot.x, 0f, ballSpot.z)) < restartExclusionRadius - 0.05f)
                {
                    Vector3 inward = new Vector3(-ballSpot.x, 0f, -ballSpot.z);

                    if (inward.sqrMagnitude < 0.01f)
                    {
                        inward = Vector3.forward;
                    }

                    inward.Normalize();

                    pushed = PitchBounds.ClampPlayer(new Vector3(
                        ballSpot.x + (inward.x * restartExclusionRadius),
                        position.y,
                        ballSpot.z + (inward.z * restartExclusionRadius)));
                }

                member.transform.position = pushed;

                if (member.TryGetComponent(out PlayerRoute route))
                {
                    route.CancelRoute();
                }

                moved++;
            }

            if (moved > 0)
            {
                Debug.Log($"[Saque] {moved} jugador(es) de {(takerMember.team == TeamId.Blue ? TeamId.Red : TeamId.Blue)} " +
                          $"retirados a {restartExclusionRadius:F1} u del balón.");
            }
        }

        /// <summary>
        /// Walks the taker's team-mates into somewhere worth passing to.
        ///
        /// Without this a throw-in or a corner is taken into an empty half of
        /// the pitch: everybody else is standing on their formation slot, which
        /// is where the shape says they belong and not where a restart needs
        /// them. Each line offers itself by a different amount, for the same
        /// reason the off-the-ball drift does:
        ///
        ///  - forwards come to the ball, because they are the pass;
        ///  - midfielders come half way, because they are the outlet if the
        ///    first ball is not on;
        ///  - defenders stay where they are. A defender who followed the ball
        ///    into the corner is a defender who is not behind it when the
        ///    restart is lost, and losing a throw-in should not be the same
        ///    thing as conceding a counter-attack.
        ///
        /// Positions are written directly. The brief suggested NavMeshAgent.Warp
        /// but this project has no NavMesh at all — the players move by
        /// coroutine along drawn routes and by the drift, neither of which is
        /// agent-based — so there is no agent to warp.
        /// </summary>
        private void OfferForRestart(PlayerBallHandler taker, Vector3 ballSpot)
        {
            if (!taker.TryGetComponent(out TeamMember takerMember))
            {
                return;
            }

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != takerMember.team || member == takerMember
                    || !member.isStarter || member.role == PlayerRole.Goalkeeper)
                {
                    continue;
                }

                float pull = RestartSupportPull(member.role);

                if (pull <= 0f || !member.TryGetComponent(out AI.TacticalPositioning positioning))
                {
                    continue;
                }

                // Interpolated from the formation slot rather than from where the
                // player happens to be standing, so a restart always produces the
                // same shape instead of compounding wherever the last passage of
                // play left everybody.
                Vector3 slot = positioning.FormationSlot;
                Vector3 target = Vector3.Lerp(slot, ballSpot, pull);

                // Never on top of the ball: a team-mate standing on the mark
                // blocks the taker and, worse, can trip a duel on the restart.
                Vector3 away = target - ballSpot;
                away.y = 0f;

                if (away.magnitude < RestartSupportClearance)
                {
                    away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
                    target = ballSpot + (away * RestartSupportClearance);
                }

                target.y = member.transform.position.y;

                if (member.TryGetComponent(out PlayerRoute route))
                {
                    route.CancelRoute();
                }

                member.transform.position = PitchBounds.ClampPlayer(target);
            }
        }

        /// <summary>How far each line travels from its slot towards the restart, 0..1.</summary>
        private static float RestartSupportPull(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Forward: return 0.75f;
                case PlayerRole.Midfielder: return 0.4f;
                default: return 0f;
            }
        }

        [Tooltip("How far the nearest supporting player is kept from the restart " +
                 "mark. Close enough to be an easy pass, far enough not to stand " +
                 "on the taker or trip a duel the instant play resumes.")]
        [SerializeField] private float restartSupportClearance = 4f;

        private static float RestartSupportClearance =>
            Instance != null ? Instance.restartSupportClearance : 4f;

        /// <summary>
        /// Pulls a restart mark just inside the painted lines, so the ball
        /// placed on it is unambiguously in play.
        /// </summary>
        private static Vector3 ClampToRestartArea(Vector3 spot)
        {
            float maxX = PitchBounds.SideLineX - RestartBallInset;
            float maxZ = PitchBounds.GoalLineZ - RestartBallInset;

            return new Vector3(
                Mathf.Clamp(spot.x, -maxX, maxX),
                spot.y,
                Mathf.Clamp(spot.z, -maxZ, maxZ));
        }

        /// <summary>
        /// Puts the view back on the ball for a restart. Every set piece calls
        /// it: the ball has just been moved to a mark somewhere else on the
        /// pitch, and a camera still staging the tackle or the shot that put it
        /// out would be pointing at an empty patch of grass while play resumed
        /// off screen.
        /// </summary>
        private static void CenterCameraOnPlay()
        {
            if (CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.CenterCamera();
            }
        }

        /// <summary>
        /// The AI has nobody to press its buttons. Without this its restarts sit
        /// there forever: the ball is dead, every other system is standing down
        /// waiting for a pass, and the human cannot take it because it is not
        /// their ball.
        /// </summary>
        private void ScheduleAiRestart(TeamId takingTeam, PlayerBallHandler taker, Vector3 target)
        {
            if (takingTeam == humanTeam)
            {
                return;
            }

            if (aiSetPieceRoutine != null)
            {
                StopCoroutine(aiSetPieceRoutine);
            }

            aiSetPieceRoutine = StartCoroutine(DelayedAISetPiece(taker, target));
        }

        private IEnumerator DelayedAISetPiece(PlayerBallHandler taker, Vector3 target)
        {
            // Realtime: a route drawn during the pause drops timeScale to 0.1,
            // which would stretch this into fifteen real seconds.
            yield return new WaitForSecondsRealtime(aiSetPieceDelay);

            aiSetPieceRoutine = null;

            if (taker == null || !taker.HasBall)
            {
                // Something already put the ball back in play.
                EndKickoff();
                yield break;
            }

            // Aimed at a TEAM-MATE, not at the patch of grass the restart was
            // pointed towards. The fixed target is a direction — "upfield from
            // the corner flag" — and hitting it put the ball into space every
            // time, which from the outside looks exactly like the AI hoofing it
            // away for no reason. The support players have already walked into
            // position by now, so there is somebody real to aim at.
            TeamMember receiver = FindRestartReceiver(taker);

            Vector3 aim = receiver != null ? receiver.transform.position : target;

            Debug.Log(receiver != null
                ? $"[IA] {taker.name} saca hacia {receiver.name} ({receiver.role})."
                : $"[IA] {taker.name} no tiene a nadie: saca hacia {target}.");

            // PassTo clears the set-piece flags itself, through EndKickoff.
            taker.PassTo(aim);
        }

        /// <summary>
        /// Who the AI restarts to: the nearest team-mate who is far enough away
        /// for the pass to be worth making.
        ///
        /// The minimum distance is the point. Without it the answer is whichever
        /// support player was pushed to the edge of the clearance radius, and a
        /// four-metre pass from a corner flag achieves nothing except giving the
        /// ball straight back to the defence.
        ///
        /// The keeper is excluded. He is often the closest available player at a
        /// goal kick, and passing to him restarts the same set piece.
        /// </summary>
        private TeamMember FindRestartReceiver(PlayerBallHandler taker)
        {
            if (!taker.TryGetComponent(out TeamMember takerMember))
            {
                return null;
            }

            TeamMember best = null;
            float bestSqr = float.MaxValue;

            float minSqr = restartPassMinDistance * restartPassMinDistance;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != takerMember.team || member == takerMember
                    || !member.isStarter || member.isGoalkeeper)
                {
                    continue;
                }

                float sqr = (member.transform.position - taker.transform.position).sqrMagnitude;

                if (sqr < minSqr || sqr >= bestSqr)
                {
                    continue;
                }

                bestSqr = sqr;
                best = member;
            }

            return best;
        }

        [Tooltip("Shortest pass the AI will play from a restart. Anything under " +
                 "this is a pass that gains nothing and hands the ball back.")]
        [SerializeField] private float restartPassMinDistance = 6f;

        /// <summary>
        /// Puts a finished match back to minute zero. Clearing isMatchOver comes
        /// before the reset is announced, because the kickoff refuses to run on a
        /// match that is still over.
        ///
        /// The reset goes through the ball rather than raising OnMatchReset by
        /// hand. The ball is not a listener — it is the thing that raises the
        /// event — so announcing it alone would leave a possessed ball kinematic
        /// and glued to a socket that nobody owns any more.
        /// </summary>
        public void RestartMatch()
        {
            currentTime = matchDuration;
            currentHalf = 1;
            isMatchOver = false;
            isHalftime = false;
            isInStoppageTime = false;
            isEndingHalf = false;
            kickoffTeam = humanTeam;
            ClearSetPieceFlags();

            Time.timeScale = NormalTimeScale;
            Time.fixedDeltaTime = FixedDeltaTimeAtNormalScale;

            // Stamina no longer comes back on its own, so a squad that finished
            // the last match on its knees would start this one there too — and
            // the substitutions made to cope with that have to be undone with
            // it, or the second match kicks off with the first one's team sheet.
            RestoreInitialSquads();

            // The one place momentum is wiped. It deliberately survives goals,
            // halves and substitutions — this is a whole new match.
            if (TensionManager.Instance != null)
            {
                TensionManager.Instance.ResetAll();
            }

            ResetStatistics();

            BallController ball = BallController.Instance;

            if (ball != null)
            {
                // Releases possession, re-centres the ball AND raises OnMatchReset.
                ball.ResetToKickoff();
            }
            else
            {
                TacticalEvents.OnMatchReset?.Invoke();
            }

            Debug.Log("Partido reiniciado.");
        }

        /// <summary>
        /// Walks every player on the pitch back onto the station they hold off
        /// the ball.
        ///
        /// The station is READ rather than recomputed: re-applying the formation
        /// would re-sort each side by depth and hand players roles they did not
        /// have a moment ago, which after a substitution reads as the game
        /// undoing the change you just made. This only moves people to the slot
        /// they already own.
        ///
        /// Substitutes are left in the dugout and the keeper is left on his
        /// line — his positioning component switches itself off, so his slot is
        /// simply wherever he is standing.
        /// </summary>
        private static void RestoreFormationPositions()
        {
            int moved = 0;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (!member.isStarter || member.isGoalkeeper)
                {
                    continue;
                }

                Vector3 slot = ResolveFormationSlot(member);

                // A run left over from the first half would drag the player
                // straight back off the slot he has just been put on.
                if (member.TryGetComponent(out PlayerRoute route))
                {
                    route.CancelRoute();
                }

                member.transform.position = new Vector3(slot.x, member.transform.position.y, slot.z);
                moved++;
            }

            Debug.Log($"Formaciones restablecidas para la 2ª parte: {moved} jugadores.");
        }

        /// <summary>
        /// Puts both squads back to how they started the first match: the right
        /// eleven on the pitch, everybody in their original place, full tanks.
        ///
        /// Refilling stamina alone used to be the whole of this, and it left the
        /// previous match's substitutions standing — the men who came on stayed
        /// on, the men taken off stayed in the dugout, and the "fresh" squad was
        /// the wrong one. Every system that remembers a station is rewritten
        /// too, or the drift would walk everybody back to where the last match
        /// left them.
        /// </summary>
        /// <summary>
        /// Takes the match all the way back to before it began, for a player who
        /// wants the title screen rather than another game of the same one.
        ///
        /// Everything RestartMatch does, and then one more thing: isMatchStarted
        /// goes back to false. That flag is what IsWaitingForSetPiece consults to
        /// hold the AI, the drift and the input still behind a menu, so without
        /// it the pitch would carry on playing underneath the title.
        /// </summary>
        public void ReturnToTitle()
        {
            RestartMatch();

            isMatchStarted = false;
            ClearSetPieceFlags();

            Debug.Log("Vuelta a la pantalla de título.");
        }

        private static void RestoreInitialSquads()
        {
            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                member.RestoreInitialState();

                AssignSlot(member, member.InitialPosition);
            }
        }

        /// <summary>
        /// Trades two players between the pitch and the bench: their places in
        /// the world, the station the off-the-ball drift walks them back to, the
        /// slot a restart sends them to, and which of them counts as playing.
        ///
        /// All four move together on purpose. Position alone lasts about a
        /// second — the drift would walk the substitute straight back to the
        /// dugout — and the next restart would snap both of them onto the places
        /// they started the match in.
        ///
        /// It lives here rather than on the substitutions board because there
        /// are now two callers: the human's team sheet, and the AI's own
        /// changes at the interval. The board is a way of choosing a
        /// substitution, not the definition of one.
        /// </summary>
        public void SwapPlayers(TeamMember p1, TeamMember p2)
        {
            if (p1 == null || p2 == null || p1 == p2)
            {
                return;
            }

            Vector3 position1 = p1.transform.position;
            Vector3 position2 = p2.transform.position;

            Vector3 slot1 = ResolveFormationSlot(p1);
            Vector3 slot2 = ResolveFormationSlot(p2);

            p1.transform.position = position2;
            p2.transform.position = position1;

            AssignSlot(p1, slot2);
            AssignSlot(p2, slot1);

            p1.isStarter = !p1.isStarter;
            p2.isStarter = !p2.isStarter;

            Debug.Log($"CAMBIO ({p1.team}): sale el {p1.jerseyNumber}, entra el {p2.jerseyNumber}.");
        }

        /// <summary>
        /// Where a player holds station. The drift's own slot is the authority;
        /// falling back to the live position matters for the keeper, whose
        /// positioning component switches itself off — but only after recording
        /// where he stands.
        ///
        /// Public because the substitutions board draws its formation preview
        /// from exactly this, and a second copy of the fallback rule was already
        /// sitting in it.
        /// </summary>
        public static Vector3 ResolveFormationSlot(TeamMember member)
        {
            return member.TryGetComponent(out AI.TacticalPositioning positioning)
                ? positioning.FormationSlot
                : member.transform.position;
        }

        /// <summary>
        /// Writes one station into every system that remembers one. The route is
        /// cancelled first: a run still in progress would drag the player
        /// straight back off the place he has just been given.
        /// </summary>
        private static void AssignSlot(TeamMember member, Vector3 slot)
        {
            if (member.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
                route.SetFormationSlot(slot);
            }

            if (member.TryGetComponent(out AI.TacticalPositioning positioning))
            {
                positioning.SetFormationSlot(slot);
            }
        }

        private void HandleMatchReset()
        {
            BeginKickoff();
        }

        private void BeginKickoff()
        {
            if (isMatchOver)
            {
                return;
            }

            // A goal conceded straight from a restart could raise OnMatchReset
            // again while the previous routine is still on its wait frame.
            if (kickoffRoutine != null)
            {
                StopCoroutine(kickoffRoutine);
            }

            if (aiSetPieceRoutine != null)
            {
                StopCoroutine(aiSetPieceRoutine);
                aiSetPieceRoutine = null;
            }

            // Raised here, not inside the coroutine. The coroutine waits a frame
            // before handing the ball out, and during that frame the ball would
            // otherwise count as live and the clock would tick — a small leak,
            // but one that happens on every goal and every restart.
            ClearSetPieceFlags();
            isWaitingForKickoff = true;

            // One whistle for every restart from the centre, because there is
            // one code path for them: the opening kickoff, the second half and
            // every goal all arrive here.
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PlayWhistle(isLong: false);
            }

            // Play restarts from the centre spot. A view the player had dragged
            // out to a corner during the last passage would hide the kickoff
            // itself, so the manual pan is dropped for every restart — a duel
            // or a shot in open play still leaves it alone.
            CenterCameraOnPlay();

            kickoffRoutine = StartCoroutine(SetupKickoffRoutine());
        }

        /// <summary>
        /// Waits one frame before handing the ball out. OnMatchReset is raised
        /// from inside BallController.ResetToKickoff, so at that instant every
        /// handler is still mid-drop and the ball is still being repositioned;
        /// assigning possession there would be undone the same frame.
        /// </summary>
        private IEnumerator SetupKickoffRoutine()
        {
            yield return null;

            kickoffRoutine = null;

            PlayerBallHandler taker = FindNearestFieldPlayer(kickoffTeam, Vector3.zero);
            if (taker == null)
            {
                Debug.LogWarning($"Ningún jugador de campo del equipo {kickoffTeam} puede sacar. " +
                                 "El balón queda libre en el centro.");
                isWaitingForKickoff = false;
                yield break;
            }

            // Walk the taker onto the centre mark. The kickoff is taken from the
            // halfway line, not from wherever in their own half that player
            // happened to be standing when the whistle went.
            float ownSide = kickoffTeam == TeamId.Blue ? -1f : 1f;

            if (!PlaceTaker(taker, new Vector3(0f, taker.transform.position.y, ownSide * kickoffTakerOffset),
                offerSupport: false))
            {
                isWaitingForKickoff = false;
                yield break;
            }

            Debug.Log($"SAQUE DE CENTRO para {kickoffTeam}: saca {taker.name} desde el centro.");

            // The AI has to be told to take its own kickoff, exactly like any
            // other restart. Without this, conceding a goal to the human would
            // leave the ball sitting on the centre spot on an opposition boot
            // for the rest of the match — every other system is standing down
            // waiting for a pass that nobody would ever play.
            //
            // Aimed at a TEAM-MATE, not at a point up the pitch. A kickoff
            // played into empty space is a free ball handed to whoever is
            // closest, which at the centre spot is usually the side that just
            // scored — so conceding was quietly rewarded. Falling back to the
            // old fixed point keeps a lone taker able to restart at all.
            PlayerBallHandler receiver = FindNearestFieldPlayer(
                kickoffTeam, taker.transform.position, exclude: taker);

            Vector3 kickoffTarget = receiver != null
                ? receiver.transform.position
                : new Vector3(0f, 0f, -ownSide * kickoffPassDistance);

            // Only worth saying when the AI is the one about to take it: the
            // human's kickoff target is computed and then discarded, because
            // ScheduleAiRestart stands down for the human's own side.
            if (receiver != null && kickoffTeam != humanTeam)
            {
                Debug.Log($"[IA] El saque de centro va hacia {receiver.name}.");
            }

            ScheduleAiRestart(kickoffTeam, taker, kickoffTarget);
        }

        /// <summary>
        /// Nearest outfield player of a side to a point. Keepers are excluded:
        /// one would drag a kickoff back into his own area, and leave his goal
        /// empty to take a corner. So are substitutes, who would otherwise be
        /// walked out of the dugout to take a throw-in.
        ///
        /// <paramref name="exclude"/> is for the kickoff, which has to find
        /// somebody to pass TO: the taker is standing on the ball and would win
        /// any nearest-player search against himself at zero distance.
        /// </summary>
        private PlayerBallHandler FindNearestFieldPlayer(TeamId team, Vector3 point,
            PlayerBallHandler exclude = null)
        {
            return FindNearestFieldPlayer(team, point, exclude, null);
        }

        /// <summary>
        /// Same, restricted to one line when <paramref name="onlyRole"/> is set.
        /// The basis of the taker preference below.
        /// </summary>
        private PlayerBallHandler FindNearestFieldPlayer(TeamId team, Vector3 point,
            PlayerBallHandler exclude, PlayerRole? onlyRole)
        {
            PlayerBallHandler closest = null;
            float closestSqrDistance = float.MaxValue;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team || member.isGoalkeeper || !member.isStarter)
                {
                    continue;
                }

                if (onlyRole.HasValue && member.role != onlyRole.Value)
                {
                    continue;
                }

                if (!member.TryGetComponent(out PlayerBallHandler handler) || handler == exclude)
                {
                    continue;
                }

                float sqrDistance = (member.transform.position - point).sqrMagnitude;

                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closest = handler;
                }
            }

            return closest;
        }

        /// <summary>
        /// Who takes a restart: a midfielder if there is one, then a defender,
        /// and a forward only if nobody else can.
        ///
        /// By line rather than by distance, which is what it used to be. The
        /// nearest player to a corner flag is very often a forward — they are the
        /// ones camped in that third — and sending the forward to fetch the ball
        /// empties the box of the exact player the cross is meant to find. A
        /// midfielder walking twenty metres to take it is not a cost: the ball is
        /// dead and the clock is stopped.
        ///
        /// Within each line it is still the nearest, so the shortest walk of the
        /// right kind of player wins.
        /// </summary>
        private PlayerBallHandler FindRestartTaker(TeamId team, Vector3 point)
        {
            PlayerBallHandler midfielder = FindNearestFieldPlayer(team, point, null, PlayerRole.Midfielder);

            if (midfielder != null)
            {
                return midfielder;
            }

            PlayerBallHandler defender = FindNearestFieldPlayer(team, point, null, PlayerRole.Defender);

            if (defender != null)
            {
                return defender;
            }

            // Last resort, and it still has to work: a side reduced to forwards
            // by substitutions must be able to take its own throw-in.
            return FindNearestFieldPlayer(team, point);
        }

        /// <summary>
        /// The HUD announcement is optional dressing: a scene generated before
        /// the announcer existed must still restart play, not throw.
        /// </summary>
        /// <summary>
        /// Shouts one of the match's moments over the pitch.
        ///
        /// Takes a localisation KEY rather than the words: these nine calls are
        /// scattered through a two-thousand-line file, and passing the Spanish
        /// through was what kept the announcer speaking Spanish in every other
        /// language.
        /// </summary>
        private static void Announce(string messageKey)
        {
            if (UI.AnnouncerUIController.Instance != null)
            {
                UI.AnnouncerUIController.Instance.ShowAnnouncement(
                    LocalizationManager.GetText(messageKey));
            }
        }

        private PlayerBallHandler FindGoalkeeper(TeamId team)
        {
            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != team || !member.isGoalkeeper || !member.isStarter)
                {
                    continue;
                }

                if (member.TryGetComponent(out PlayerBallHandler handler))
                {
                    return handler;
                }
            }

            return null;
        }
    }
}
