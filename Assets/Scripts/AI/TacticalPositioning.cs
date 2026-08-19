using System.Collections.Generic;
using UnityEngine;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.AI
{
    /// <summary>
    /// Keeps an outfield player alive when they have nothing else to do. Two
    /// behaviours, in priority order:
    ///
    ///   1. If this is the closest player on the side to a LOOSE ball and it is
    ///      within reach, walk at it. Wandering off while the ball rolls past
    ///      your feet is the single most robotic thing a player can do.
    ///   2. Otherwise hold the formation slot, shifted up and down with the
    ///      ball and wandering slightly, so the shape breathes.
    ///
    /// How far the slot shifts is what separates the lines. Forwards camp high
    /// and lean hard with the ball, midfielders track it evenly, and defenders
    /// barely move and are hard-capped short of the halfway line — so the shape
    /// stretches and compresses instead of the whole team sliding as one block.
    ///
    /// It is the lowest-priority mover on the pitch. Anything with an actual
    /// intention — a drawn route, possession, a duel, a restart — takes the
    /// Transform and this stands down, because two systems writing the same
    /// Transform on the same frame is a fight, not a blend.
    /// </summary>
    [RequireComponent(typeof(PlayerRoute))]
    public class TacticalPositioning : MonoBehaviour
    {
        [Header("Formation drift")]
        [Tooltip("How far a MIDFIELDER shifts towards the ball, as a fraction of " +
                 "the ball's own distance up the pitch. Well under 1: the team " +
                 "leans with the play, it does not chase it in a pack.")]
        [SerializeField] private float ballInfluence = 0.3f;

        [Header("Roles")]
        [Tooltip("Ball influence for a FORWARD. Higher than a midfielder's: a " +
                 "striker follows the attack rather than holding a station.")]
        [SerializeField] private float forwardBallInfluence = 0.45f;

        [Tooltip("How far up the pitch a FORWARD stands beyond their formation " +
                 "slot, towards the goal they attack. This is what makes the " +
                 "front line an outlet instead of another row of the block.")]
        [SerializeField] private float forwardPush = 6f;

        [Tooltip("Ball influence for a DEFENDER. Low on purpose: the back line " +
                 "holds its shape while the rest of the team moves.")]
        [SerializeField] private float defenderBallInfluence = 0.15f;

        [Tooltip("Hard cap on how far past the halfway line a DEFENDER may " +
                 "drift, in units. The influence above already keeps them deep; " +
                 "this is the guarantee, so no combination of ball position and " +
                 "wander can send a centre-back into the opposition half.")]
        [SerializeField] private float defenderMaxAdvance = 2f;

        [Tooltip("Maximum wander from the formation slot, in units.")]
        [SerializeField] private float driftRange = 1.5f;

        [Tooltip("How quickly the wander evolves. Slow, or players jitter.")]
        [SerializeField] private float driftSpeed = 0.5f;

        [Tooltip("Walking pace when drifting. Deliberately below the run speed: " +
                 "this is jogging into space, not making a run.")]
        [SerializeField] private float repositionSpeed = 2f;

        [Header("Loose ball")]
        [Tooltip("How far this player will go for a loose ball. Beyond it, " +
                 "chasing would pull them out of shape for a ball they were " +
                 "never going to reach.")]
        [SerializeField] private float chaseRadius = 12f;

        [Tooltip("Pace when going for a loose ball. Faster than drifting — this " +
                 "is an actual attempt to get there.")]
        [SerializeField] private float chaseSpeed = 3f;

        private TeamMember member;
        private PlayerRoute route;
        private PlayerBallHandler handler;

        private Vector3 baseFormationPos;

        /// <summary>
        /// Where this player holds station. Exposed so a substitution can trade
        /// two players' slots: the incoming man has to inherit the outgoing
        /// man's station, or the drift would spend the rest of the match walking
        /// him back to the bench he came off.
        /// </summary>
        public Vector3 FormationSlot => baseFormationPos;

        /// <summary>
        /// This side's outfield players. The roster is fixed for a match, so it
        /// is resolved once instead of scanned every frame by every player.
        ///
        /// Held as TeamMember rather than Transform because who is actually ON
        /// the pitch is not fixed: a substitution flips isStarter without
        /// changing the roster, and a chase decision that counted a man sitting
        /// in the dugout as the nearest would talk every player on the side out
        /// of going for the ball.
        /// </summary>
        private readonly List<TeamMember> teamMates = new List<TeamMember>();

        /// <summary>
        /// Per-player offset into the noise field. Without it every player reads
        /// the same curve and the whole team drifts in unison, which looks far
        /// more robotic than standing still.
        /// </summary>
        private float noiseSeed;

        // Derived from the formation slot rather than the instance id: it is
        // unique per player, stable across runs, and stays in a range where
        // Perlin noise still has resolution — raw instance ids do not.
        private const float NoiseSeedX = 7.31f;
        private const float NoiseSeedZ = 2.17f;
        private const float NoiseSeedBase = 500f;

        private void Awake()
        {
            member = GetComponent<TeamMember>();
            route = GetComponent<PlayerRoute>();
            handler = GetComponent<PlayerBallHandler>();

            SetFormationSlot(transform.position);

            // Keepers run their own tracking loop along the goal line. Two
            // movers on one Transform would tear the keeper off his line.
            if (member != null && member.isGoalkeeper)
            {
                enabled = false;
            }
        }

        /// <summary>
        /// Moves the slot this player drifts around. Called when the shape is
        /// changed from the formation menu: without it the drift would spend the
        /// whole match walking everyone back to the positions they were spawned
        /// in, whatever the player picked.
        ///
        /// The noise seed is re-derived here rather than left alone, because it
        /// comes from the slot itself — two players sent to the same line would
        /// otherwise keep reading the same curve and drift in lockstep.
        /// </summary>
        public void SetFormationSlot(Vector3 slot)
        {
            baseFormationPos = slot;

            noiseSeed = (baseFormationPos.x * NoiseSeedX)
                + (baseFormationPos.z * NoiseSeedZ)
                + NoiseSeedBase;
        }

        private void Start()
        {
            CacheTeamMates();
        }

        private void Update()
        {
            if (!ShouldReposition())
            {
                return;
            }

            bool chasing = ShouldChaseLooseBall();

            Vector3 target = chasing
                ? Core.PitchBounds.ClampPlayer(BallController.Instance.transform.position)
                : CalculateFormationPosition();

            target.y = transform.position.y;

            transform.position = Vector3.MoveTowards(
                transform.position,
                target,
                (chasing ? chaseSpeed : repositionSpeed) * Time.deltaTime);
        }

        /// <summary>
        /// Every reason to stand down, cheapest first.
        /// </summary>
        private bool ShouldReposition()
        {
            // Checked here as well as switched off in Awake: a keeper has his
            // own tracking loop, and if anything ever re-enables this component
            // the two would tear him off his line between them.
            if (member != null && member.isGoalkeeper)
            {
                return false;
            }

            // A substitute holds his seat. Without this the drift would walk him
            // straight out of the dugout and onto the pitch, because his
            // formation slot is the bench and the ball influence is not.
            if (member != null && !member.isStarter)
            {
                return false;
            }

            if (ClashManager.IsClashActive)
            {
                return false;
            }

            if (handler != null && handler.HasBall)
            {
                return false;
            }

            if (route != null && (route.IsFollowingRoute || route.IsStunned))
            {
                return false;
            }

            if (Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece)
            {
                return false;
            }

            return BallController.Instance != null;
        }

        /// <summary>
        /// True when this is the nearest of the side to a ball that nobody owns,
        /// and it is close enough to be worth going for.
        ///
        /// Only a LOOSE ball is chased. Walking at an opponent who is carrying
        /// would start duels the player never chose, and on the human's side
        /// that would be the team playing itself.
        /// </summary>
        private bool ShouldChaseLooseBall()
        {
            BallController ball = BallController.Instance;

            if (!ball.IsFree)
            {
                return false;
            }

            Vector3 ballPosition = ball.transform.position;
            float ownDistance = FlatDistance(transform.position, ballPosition);

            if (ownDistance > chaseRadius)
            {
                return false;
            }

            foreach (TeamMember mate in teamMates)
            {
                if (mate == null || !mate.isStarter)
                {
                    continue;
                }

                if (FlatDistance(mate.transform.position, ballPosition) < ownDistance)
                {
                    return false;
                }
            }

            return true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;

            return Vector3.Distance(a, b);
        }

        /// <summary>
        /// Rebuilds the cached team-mate list from the roster as it stands
        /// right now.
        ///
        /// Public beyond Start(): the list is filtered by isGoalkeeper at the
        /// moment it is built, and a goalkeeper swap (SquadRoles.Write) changes
        /// that flag on two players well after every other player's Start() has
        /// already run. Without a way to rebuild, every OTHER player's chase
        /// check would keep comparing distances against whichever XI was on the
        /// pitch when the scene first loaded — still counting the newly-made
        /// keeper as a chase rival, and never counting the newly-demoted one at
        /// all. SquadRoles.Write calls this for every member of the affected
        /// team, not just the two who swapped, since it is everyone ELSE's
        /// cache that goes stale.
        /// </summary>
        public void CacheTeamMates()
        {
            teamMates.Clear();

            if (member == null)
            {
                return;
            }

            foreach (TeamMember other in FindObjectsByType<TeamMember>())
            {
                if (other == member || other.team != member.team || other.isGoalkeeper)
                {
                    continue;
                }

                // Substitutes are kept in the list on purpose: they can come on
                // mid-match, and the chase check filters on isStarter at the
                // moment it asks rather than trusting a roster cached at Start.
                teamMates.Add(other);
            }
        }

        private Vector3 CalculateFormationPosition()
        {
            // Two separate slices of the noise field, so X and Z wander
            // independently instead of the player sliding along one diagonal.
            float driftX = SampleDrift(0.13f);
            float driftZ = SampleDrift(4.71f);

            PlayerRole role = member != null ? member.role : PlayerRole.Midfielder;

            // Which way is forward for this side. Blue starts south and attacks
            // north; Red does the opposite.
            float attackDirection = member != null && member.team == TeamId.Red ? -1f : 1f;

            float zShift = BallController.Instance.transform.position.z * ResolveBallInfluence(role);

            if (role == PlayerRole.Forward)
            {
                zShift += attackDirection * forwardPush;
            }

            Vector3 target = baseFormationPos + new Vector3(driftX, 0f, zShift + driftZ);

            if (role == PlayerRole.Defender)
            {
                // Measured as ground gained towards the opposition goal, so one
                // cap covers both sides regardless of which sign of Z they
                // attack.
                float advance = target.z * attackDirection;

                if (advance > defenderMaxAdvance)
                {
                    target.z = attackDirection * defenderMaxAdvance;
                }
            }

            return Core.PitchBounds.ClampPlayer(target);
        }

        private float ResolveBallInfluence(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Forward: return forwardBallInfluence;
                case PlayerRole.Defender: return defenderBallInfluence;

                // Keepers never reach this — the component switches itself off
                // for them — so a keeper role falls in with the midfielders.
                default: return ballInfluence;
            }
        }

        private float SampleDrift(float channel)
        {
            // PerlinNoise returns 0..1; recentre it so the drift is symmetric
            // about the formation slot rather than always pushing one way.
            float noise = Mathf.PerlinNoise((Time.time * driftSpeed) + noiseSeed, channel);

            return (noise - 0.5f) * 2f * driftRange;
        }
    }
}
