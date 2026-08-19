using System.Collections.Generic;
using UnityEngine;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.AI
{
    /// <summary>
    /// Drives one whole team by reusing the same route system the human player
    /// draws with: it picks a player and feeds PlayerRoute a destination, so the
    /// AI is subject to exactly the same movement rules as everyone else.
    ///
    /// It re-decides on a fixed interval rather than every frame — cheaper, and
    /// the lag between decisions reads as deliberation instead of twitchiness.
    /// </summary>
    public class EnemyAIController : MonoBehaviour
    {
        [Header("Team")]
        [SerializeField] private TeamId controlledTeam = TeamId.Red;

        [Header("Thinking")]
        [Tooltip("Base gap between decisions. Scaled by the chosen difficulty, " +
                 "which is the whole of what makes an easy opponent easy to run " +
                 "at: it keeps closing down the space the ball has already left.")]
        [SerializeField] private float thinkInterval = 1f;

        [Tooltip("Where this team attacks. Red defends north, so it pushes south. " +
                 "Sits past the goal line so the carrier runs through the goal trigger " +
                 "instead of stopping short of it.")]
        [SerializeField] private Vector3 targetGoalPosition = new Vector3(0f, 0f, -24.5f);

        [Header("Shooting")]
        [Tooltip("Centre of the goal this team shoots at. Slightly short of the " +
                 "run-in target: the ball has to be aimed AT the mouth, not past it.")]
        [SerializeField] private Vector3 shotTargetPosition = new Vector3(0f, 0f, -23.5f);

        [Tooltip("Flat distance from the goal at which the carrier shoots instead " +
                 "of running on. Without this the AI walks into the net forever, " +
                 "because arriving there is not what scores.")]
        [SerializeField] private float shootingRange = 15f;

        [Header("Passing")]
        [Tooltip("Chance of looking for a pass on any given decision, when one is " +
                 "available. Well under 1 on purpose: an AI that always passes " +
                 "when it can never carries the ball, and reads as a machine.")]
        [Range(0f, 1f)]
        [SerializeField] private float passChance = 0.3f;

        [Tooltip("A team-mate is marked if an opponent is within this of them.")]
        [SerializeField] private float markedRadius = 3.5f;

        [Tooltip("Minimum ground the pass has to gain to be worth making.")]
        [SerializeField] private float minimumPassAdvance = 3f;

        [Tooltip("Longest pass the AI will attempt. Beyond this the ball simply " +
                 "does not arrive, because pass force is fixed.")]
        [SerializeField] private float maximumPassDistance = 18f;

        [Header("Presión")]
        [Tooltip("How far PAST the carrier the presser is sent. Routing exactly " +
                 "onto them means arriving at where they used to be a second " +
                 "ago and stopping short of contact, so no duel ever happens.")]
        [SerializeField] private float pressOvershoot = 1.5f;

        private readonly List<PlayerBallHandler> squad = new List<PlayerBallHandler>();
        private BallController ball;
        private float thinkTimer;

        /// <summary>
        /// How long this side waits between decisions right now. Read from the
        /// match settings every tick rather than cached at Start: the difficulty
        /// is chosen on a screen that runs before the match, and on a restart it
        /// can be chosen again.
        /// </summary>
        private float CurrentThinkInterval
        {
            get
            {
                float scale = Core.MatchManager.Instance != null
                    ? Core.MatchManager.Instance.AiThinkIntervalScale
                    : 1f;

                return thinkInterval * scale;
            }
        }

        private void Start()
        {
            ball = FindAnyObjectByType<BallController>();
            CacheSquad();
        }

        private void Update()
        {
            if (ball == null || squad.Count == 0)
            {
                return;
            }

            // A duel is a decision point, not a moment to keep issuing orders:
            // without this the carrier would re-shoot every think tick while its
            // own shot duel sits frozen on screen.
            if (ClashManager.IsClashActive)
            {
                return;
            }

            // Restarts: the opposition holds its shape until the ball is put
            // back into play, so the move off a kickoff or a throw is thought
            // out rather than scrambled for. The timer is not advanced either —
            // otherwise the AI would fire its first decision the instant play
            // starts, however long the human took to decide.
            if (Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece)
            {
                return;
            }

            thinkTimer += Time.deltaTime;

            if (thinkTimer < CurrentThinkInterval)
            {
                return;
            }

            thinkTimer = 0f;
            Think();
        }

        /// <summary>
        /// The roster is fixed for a match, so it is resolved once instead of
        /// scanning every TeamMember in the scene on each decision.
        ///
        /// Substitutes are cached along with everyone else — they can come on
        /// later — and filtered out at the point of use through IsOnPitch, so a
        /// swap never has to reach in here and rebuild the list.
        /// </summary>
        private void CacheSquad()
        {
            squad.Clear();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != controlledTeam)
                {
                    continue;
                }

                // Keepers run their own tracking loop and must stay on their
                // goal line, so the general AI never hands them a route.
                if (member.isGoalkeeper)
                {
                    continue;
                }

                if (member.TryGetComponent(out PlayerBallHandler handler))
                {
                    squad.Add(handler);
                }
            }
        }

        private void Think()
        {
            PlayerBallHandler carrier = FindCarrier();

            if (carrier != null)
            {
                if (IsInShootingRange(carrier))
                {
                    Shoot(carrier);
                    return;
                }

                // Too far to shoot: look up before putting the head down. The
                // roll happens first so the search is skipped entirely most of
                // the time rather than run and thrown away.
                if (Random.value < passChance && TryPass(carrier))
                {
                    return;
                }

                // Nobody on, or not looking: push towards the opponent's goal.
                SendTo(carrier, targetGoalPosition);
                return;
            }

            // Nobody on this side has it. If the opposition does, that is a
            // player to be closed down, not a ball to be drifted around: send
            // the nearest man straight at them.
            TeamMember opposingCarrier = FindOpposingCarrier();

            if (opposingCarrier != null)
            {
                Press(opposingCarrier);
                return;
            }

            PlayerBallHandler chaser = FindClosestToBall();

            if (chaser != null)
            {
                SendTo(chaser, ball.transform.position);
            }
        }

        /// <summary>
        /// Sends the closest man to the ball carrier, aiming just past them so
        /// the run actually ends in contact — which is what raises the tackle
        /// duel. Chasing the BALL instead would work too, right up until the
        /// carrier turns and the presser jogs to where the ball used to be.
        /// </summary>
        private void Press(TeamMember carrier)
        {
            Vector3 carrierPosition = carrier.transform.position;
            PlayerBallHandler presser = FindClosestTo(carrierPosition);

            if (presser == null)
            {
                return;
            }

            Vector3 approach = carrierPosition - presser.transform.position;
            approach.y = 0f;

            Vector3 target = approach.sqrMagnitude > 0.0001f
                ? carrierPosition + (approach.normalized * pressOvershoot)
                : carrierPosition;

            Debug.Log($"[IA] {presser.name} presiona a {carrier.name} " +
                      $"({approach.magnitude:F1} u).");

            SendTo(presser, target);
        }

        /// <summary>
        /// Whoever on the other side is carrying the ball, if anyone. Scanned
        /// live rather than cached: possession is exactly the thing that changes.
        /// </summary>
        private TeamMember FindOpposingCarrier()
        {
            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team == controlledTeam || !member.isStarter)
                {
                    continue;
                }

                if (member.TryGetComponent(out PlayerBallHandler handler) && handler.HasBall)
                {
                    return member;
                }
            }

            return null;
        }

        /// <summary>
        /// Flat distance only. The players stand a unit above the pitch and the
        /// goal centre is at ground level, so a full 3D measure would report
        /// every carrier as slightly further out than they really are.
        /// </summary>
        private bool IsInShootingRange(PlayerBallHandler carrier)
        {
            Vector3 toGoal = shotTargetPosition - carrier.transform.position;
            toGoal.y = 0f;

            return toGoal.magnitude < shootingRange;
        }

        /// <summary>
        /// The run is cancelled before the shot, not after: leaving the route
        /// alive would keep walking the shooter into the goal mouth while the
        /// duel resolves, and a route that ends inside the net is exactly the
        /// behaviour this replaces.
        /// </summary>
        private void Shoot(PlayerBallHandler carrier)
        {
            if (carrier.TryGetComponent(out PlayerRoute route))
            {
                route.CancelRoute();
            }

            Debug.Log($"[IA] {carrier.name} remata a puerta desde " +
                      $"{Vector3.Distance(carrier.transform.position, shotTargetPosition):F1} u.");

            carrier.InitiateShot(shotTargetPosition);
        }

        /// <summary>
        /// Looks for a team-mate who is further up the pitch, unmarked, and
        /// within range of a pass that will actually arrive. Returns false if
        /// there is nobody on, so the caller can fall back to carrying.
        /// </summary>
        private bool TryPass(PlayerBallHandler carrier)
        {
            PlayerBallHandler target = FindPassTarget(carrier);

            if (target == null)
            {
                return false;
            }

            if (carrier.TryGetComponent(out PlayerRoute route))
            {
                // A run still in progress would drag the passer forward after
                // the ball has already left him.
                route.CancelRoute();
            }

            Debug.Log($"[IA] {carrier.name} pasa a {target.name} " +
                      $"({Vector3.Distance(carrier.transform.position, target.transform.position):F1} u).");

            carrier.PassTo(target.transform.position);

            return true;
        }

        private PlayerBallHandler FindPassTarget(PlayerBallHandler carrier)
        {
            // Which way is forward for this side: towards the goal it shoots at.
            float attackDirection = Mathf.Sign(shotTargetPosition.z);
            Vector3 carrierPosition = carrier.transform.position;

            PlayerBallHandler best = null;
            float bestAdvance = minimumPassAdvance;

            foreach (PlayerBallHandler mate in squad)
            {
                if (mate == null || mate == carrier || !mate.IsOnPitch)
                {
                    continue;
                }

                Vector3 matePosition = mate.transform.position;

                // Forward means closer to the goal being attacked, whichever
                // sign of Z that happens to be.
                float advance = (matePosition.z - carrierPosition.z) * attackDirection;

                if (advance <= bestAdvance)
                {
                    continue;
                }

                Vector3 toMate = matePosition - carrierPosition;
                toMate.y = 0f;

                if (toMate.magnitude > maximumPassDistance)
                {
                    continue;
                }

                if (IsMarked(matePosition))
                {
                    continue;
                }

                bestAdvance = advance;
                best = mate;
            }

            return best;
        }

        /// <summary>
        /// True if any opponent is close enough to the receiver to contest the
        /// ball the moment it arrives.
        /// </summary>
        private bool IsMarked(Vector3 position)
        {
            float markedSqr = markedRadius * markedRadius;

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team == controlledTeam || !member.isStarter)
                {
                    continue;
                }

                if ((member.transform.position - position).sqrMagnitude <= markedSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private PlayerBallHandler FindCarrier()
        {
            foreach (PlayerBallHandler handler in squad)
            {
                if (handler != null && handler.IsOnPitch && handler.HasBall)
                {
                    return handler;
                }
            }

            return null;
        }

        private PlayerBallHandler FindClosestToBall()
        {
            return FindClosestTo(ball.transform.position);
        }

        private PlayerBallHandler FindClosestTo(Vector3 point)
        {
            PlayerBallHandler closest = null;
            float closestSqrDistance = float.MaxValue;
            Vector3 ballPosition = point;

            foreach (PlayerBallHandler handler in squad)
            {
                if (handler == null || !handler.IsOnPitch)
                {
                    continue;
                }

                float sqrDistance = (handler.transform.position - ballPosition).sqrMagnitude;

                if (sqrDistance < closestSqrDistance)
                {
                    closestSqrDistance = sqrDistance;
                    closest = handler;
                }
            }

            return closest;
        }

        private void SendTo(PlayerBallHandler handler, Vector3 destination)
        {
            if (!handler.TryGetComponent(out PlayerRoute route))
            {
                return;
            }

            route.BeginRoute();
            route.AddRoutePoint(Core.PitchBounds.ClampPlayer(destination));
            route.EndRoute();
        }
    }
}
