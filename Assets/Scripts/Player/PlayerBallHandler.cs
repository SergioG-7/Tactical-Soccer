using UnityEngine;

namespace TacticalSoccer.Player
{
    /// <summary>
    /// Detects ball contact via a trigger collider and hands possession to
    /// the ball itself; holds no ball-state logic beyond the handoff.
    ///
    /// Neither contested outcome is decided here. Meeting an opposing carrier
    /// raises a tackle duel, and shooting from close range raises a shot duel
    /// against the keeper; the ClashManager settles both and calls back with
    /// the result.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class PlayerBallHandler : MonoBehaviour
    {
        [SerializeField] private Transform ballSocket;

        [Tooltip("Impulse applied to a pass. Tuned so a pass actually reaches a " +
                 "team-mate across the middle third rather than dying short.")]
        [SerializeField] private float passForce = 12f;

        [Header("Tiro Directo")]
        [SerializeField] private float powerShotForce = 25f;
        [SerializeField] private float powerShotLift = 0.1f;

        [Header("Vaselina")]
        [Tooltip("Softer and higher than a drive: it has to drop back down " +
                 "under the crossbar rather than sail over it.")]
        [SerializeField] private float lobShotForce = 15f;
        [SerializeField] private float lobShotLift = 0.45f;

        [Header("Alcance de Duelo")]
        [Tooltip("Flat distance to the target beyond which a shot is a hopeful " +
                 "long-range hit rather than a one-on-one, so it skips the duel " +
                 "and simply flies.")]
        [SerializeField] private float maxDuelShotDistance = 15f;

        [Header("Intercepción")]
        [Tooltip("Speed above which a loose ball counts as a pass in flight " +
                 "rather than something rolling around to be picked up. Below " +
                 "it, stepping on the ball is simply collecting it.")]
        [SerializeField] private float interceptSpeedThreshold = 5f;

        [SerializeField] private float pickupCooldown = 0.2f;

        [Tooltip("Extra time the last kicker alone must wait before collecting " +
                 "the ball again, so a rebound is a real rebound.")]
        [SerializeField] private float selfReboundImmunity = 1f;

        // Used only when no enemy keeper exists to duel against.
        private const float FallbackGoalZ = 24.5f;

        // Floor on the strike multiplier: a scale of zero would leave the ball
        // dead at the shooter's feet, which is a stuck match, not a soft shot.
        private const float MinimumForceScale = 0.1f;

        private Gameplay.BallController currentBall;
        private Gameplay.TeamMember myTeamMember;
        private PlayerRoute myRoute;
        private Gameplay.TeamMember enemyGoalkeeper;
        private float lastPassTime = -1f;

        public bool HasBall => currentBall != null;

        /// <summary>
        /// False while this player is a substitute waiting in the dugout.
        ///
        /// Exposed here rather than making every caller do its own
        /// GetComponent&lt;TeamMember&gt;: the AI, the input layer and the contact
        /// checks all need the same answer, and the member is already cached.
        /// </summary>
        public bool IsOnPitch => myTeamMember == null || myTeamMember.isStarter;

        /// <summary>
        /// Where the ball sits relative to this player, in world space.
        ///
        /// Exposed for the set pieces. A restart mark is a place for the BALL —
        /// the corner arc, the touchline, the six-yard box — and the ball rides
        /// on a socket half a metre behind the player. Standing the taker on the
        /// mark therefore puts the ball just BEHIND it, which at a corner means
        /// behind the goal line: still out of play, by the same check that had
        /// just awarded the corner.
        /// </summary>
        public Vector3 BallOffset =>
            ballSocket != null ? ballSocket.position - transform.position : Vector3.zero;

        private void Awake()
        {
            myTeamMember = GetComponent<Gameplay.TeamMember>();
            myRoute = GetComponent<PlayerRoute>();
        }

        private void OnEnable()
        {
            Core.TacticalEvents.OnMatchReset += ForceDropBall;
        }

        private void OnDisable()
        {
            Core.TacticalEvents.OnMatchReset -= ForceDropBall;
        }

        public void AssignBallSocket(Transform socket)
        {
            ballSocket = socket;
        }

        /// <summary>
        /// Clears possession without touching the ball. Used when the ball is
        /// taken away by an outside system (goal, out of bounds, duel), which
        /// would otherwise leave this handler reporting HasBall == true forever.
        /// </summary>
        public void ForceDropBall()
        {
            currentBall = null;
        }

        /// <summary>
        /// Puts the ball on this player's foot regardless of contact, cooldowns
        /// or rebound immunity. Used by the kickoff, which awards possession by
        /// rule rather than by whoever gets there first.
        /// </summary>
        public void ForceTakeBall(Gameplay.BallController ball)
        {
            if (ball == null || ballSocket == null)
            {
                return;
            }

            // Take it OFF whoever had it first. Attaching the ball to a new
            // socket does not tell the previous owner anything, so he went on
            // reporting HasBall — a ghost carrier who kept being treated as the
            // man in possession from across the pitch. After a foul that showed
            // up as the offender's side carrying on towards goal with a ball
            // that had already been given to the other team.
            Gameplay.TeamMember previous = ball.Holder != null
                ? ball.Holder.GetComponent<Gameplay.TeamMember>()
                : null;

            if (ball.Holder != null && ball.Holder != gameObject)
            {
                if (ball.Holder.TryGetComponent(out PlayerBallHandler previousHandler))
                {
                    previousHandler.ForceDropBall();
                }

                // And stop him running the route he was on. He was heading
                // somewhere that made sense while he had the ball; finishing that
                // run now is the "frozen player sprinting at the goal" bug.
                if (ball.Holder.TryGetComponent(out PlayerRoute previousRoute))
                {
                    previousRoute.CancelRoute();
                }

                if (previous != null)
                {
                    Debug.Log($"Posesión retirada a {previous.name} ({previous.team}) " +
                              $"y entregada a {name} ({(myTeamMember != null ? myTeamMember.team.ToString() : "?")}).");
                }
            }

            ball.AttachToPlayer(ballSocket);
            currentBall = ball;
        }

        public void PassTo(Vector3 targetPosition)
        {
            if (currentBall == null)
            {
                return;
            }

            StartPlayIfWaitingForKickoff();

            // Aim from the ball, not from the player: the ball sits on an offset
            // socket, so using the player's origin skews short passes.
            Vector3 direction = targetPosition - currentBall.transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            currentBall.Kick(direction.normalized, passForce);
            currentBall = null;
            StartPickupCooldown();
        }

        /// <summary>
        /// Shoots at <paramref name="targetPosition"/>.
        ///
        /// From close range this opens a duel against the keeper instead of
        /// striking: the ball stays on the shooter's foot while the duel is
        /// frozen, and only flies if the shooter wins. From distance there is no
        /// one-on-one to play out, so the ball is simply hit — which also stops
        /// a player summoning the keeper duel from their own half.
        /// </summary>
        public void InitiateShot(Vector3 targetPosition)
        {
            if (currentBall == null || myTeamMember == null)
            {
                return;
            }

            StartPlayIfWaitingForKickoff();

            // Counted at the point of committing, not at the point of scoring:
            // an attempt is an attempt whether the keeper reads it, the duel is
            // lost or it flies wide.
            if (Core.MatchManager.Instance != null)
            {
                Core.MatchManager.Instance.RecordShot(myTeamMember.team);
            }

            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;

            if (toTarget.magnitude > maxDuelShotDistance)
            {
                Debug.Log($"Tiro lejano ({toTarget.magnitude:F1} u > {maxDuelShotDistance} u): sin duelo.");
                ExecutePhysicalKick(Gameplay.ClashAction.PowerShot, targetPosition);
                return;
            }

            Gameplay.TeamMember keeper = ResolveEnemyGoalkeeper();

            if (keeper == null)
            {
                // No keeper to beat, so there is nothing to duel over: just hit it.
                Debug.LogWarning("No se encontró portero rival. Se ejecuta el tiro sin duelo.");
                ExecutePhysicalKick(Gameplay.ClashAction.PowerShot, CalculateFallbackAim());
                return;
            }

            Core.TacticalEvents.OnShotInitiated?.Invoke(myTeamMember, keeper);
        }

        /// <summary>
        /// Actually strikes the ball, with the physics of the move the shooter
        /// chose. A drive is hard and flat; a lob is soft and high so it drops
        /// back under the bar.
        /// </summary>
        /// <param name="forceScale">
        /// Multiplier on the strike. Every shot is now struck for real, including
        /// the ones the keeper reads: a save is the same shot hit softer and
        /// straight at the keeper, so the ball still travels and is still
        /// gathered by physics rather than teleported out of the shooter's feet.
        /// </param>
        public void ExecutePhysicalKick(Gameplay.ClashAction shotType, Vector3 goalPosition,
            float forceScale = 1f)
        {
            if (currentBall == null)
            {
                return;
            }

            Vector3 direction = goalPosition - currentBall.transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            bool isLob = shotType == Gameplay.ClashAction.LobShot;

            // Normalise the horizontal aim first, then add lift: doing it the
            // other way round would let a long-range shot flatten out while a
            // close one launched almost vertically.
            direction = direction.normalized;
            direction.y = isLob ? lobShotLift : powerShotLift;

            float force = (isLob ? lobShotForce : powerShotForce) * Mathf.Max(MinimumForceScale, forceScale);

            currentBall.Kick(direction, force);
            currentBall = null;
            StartPickupCooldown();
        }

        /// <summary>
        /// A pass or a shot is what puts the ball in motion, so any restart hold
        /// — kickoff or throw-in — and with it the freeze on the opposing AI,
        /// lifts here.
        /// </summary>
        private void StartPlayIfWaitingForKickoff()
        {
            if (Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece)
            {
                Core.MatchManager.Instance.EndKickoff();
            }
        }

        /// <summary>
        /// The opposing keeper never changes during a match, so it is resolved
        /// once and kept.
        ///
        /// Found through the isGoalkeeper flag on TeamMember rather than through
        /// GoalkeeperAI: the AI layer already depends on the player layer, and
        /// reaching back the other way would close the loop.
        /// </summary>
        private Gameplay.TeamMember ResolveEnemyGoalkeeper()
        {
            if (enemyGoalkeeper != null)
            {
                return enemyGoalkeeper;
            }

            foreach (Gameplay.TeamMember member in FindObjectsByType<Gameplay.TeamMember>())
            {
                if (member.team != myTeamMember.team && member.isGoalkeeper)
                {
                    enemyGoalkeeper = member;
                    break;
                }
            }

            return enemyGoalkeeper;
        }

        private Vector3 CalculateFallbackAim()
        {
            // Blue starts south and attacks north; Red does the opposite.
            float side = myTeamMember.team == Gameplay.TeamId.Blue ? 1f : -1f;

            return new Vector3(0f, 0.5f, side * FallbackGoalZ);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!CanContestBall())
            {
                return;
            }

            if (other.CompareTag("Ball"))
            {
                TryPickUpLooseBall(other);
                return;
            }

            if (other.CompareTag("Player"))
            {
                TryInitiateClash(other);
            }
        }

        /// <summary>
        /// Stay backs Enter up for the case where both players are already
        /// overlapping when possession changes hands. It fires only while this
        /// body is awake — a kinematic player standing still is asleep and gets
        /// no Stay at all — which is why Enter still carries the check too.
        /// The cheap guards go first to keep the per-frame cost near zero.
        /// </summary>
        private void OnTriggerStay(Collider other)
        {
            if (!CanContestBall())
            {
                return;
            }

            // Ball pickup has to run here as well as on Enter. Enter fires once,
            // so a ball that was already overlapping while this player was on
            // cooldown — exactly what a rebound off the keeper produces — would
            // otherwise sit dead at their feet until it left and came back.
            if (other.CompareTag("Ball"))
            {
                TryPickUpLooseBall(other);
                return;
            }

            if (other.CompareTag("Player"))
            {
                TryInitiateClash(other);
            }
        }

        /// <summary>
        /// A stunned player is out of the play entirely: they can neither pick
        /// the ball up nor challenge for it, and nobody acts at all while a duel
        /// is frozen on screen.
        ///
        /// The post-duel cooldown deliberately does NOT gate this: it exists to
        /// stop two overlapping players re-duelling, and applying it to loose
        /// balls left a keeper unable to save for a second after any clash.
        /// </summary>
        private bool CanContestBall()
        {
            // A substitute is standing in his own dugout, well outside the
            // touchline — but a ball hooked out of play can still roll through
            // the bench, and collecting it there would put the match on a
            // player who is not even on the pitch.
            if (!IsOnPitch)
            {
                return false;
            }

            if (Gameplay.ClashManager.IsClashActive)
            {
                return false;
            }

            // The ball is dead during a restart. Without this you could walk
            // into the player lining up a throw-in and duel him for a ball that
            // is not even in play yet.
            if (Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece)
            {
                return false;
            }

            if (IsPickupOnCooldown())
            {
                return false;
            }

            return myRoute == null || !myRoute.IsStunned;
        }

        private void TryPickUpLooseBall(Collider ballCollider)
        {
            if (!ballCollider.TryGetComponent(out Gameplay.BallController ball))
            {
                return;
            }

            // Only a loose ball can be collected by touch. Taking one off
            // somebody has to go through a duel, which is where the team check
            // lives; without this guard any player — team-mates included —
            // could steal simply by brushing against the carrier's ball.
            if (!ball.IsFree)
            {
                return;
            }

            // Rebound immunity, and only for whoever kicked it: the shooter's own
            // trigger is right where a ball coming back off the keeper passes, so
            // without this the shot snaps magnetically back onto their foot.
            // Everyone else may collect it immediately — this must not become a
            // global freeze on loose balls.
            if (ball.LastHolder == gameObject && Time.time - lastPassTime < selfReboundImmunity)
            {
                return;
            }

            // Stepping into an opponent's pass is settled on the spot — no duel
            // panel and no freeze — and either way this contact is spent: the
            // ball is won, or the interceptor is left stunned watching it go by.
            if (TryInitiateIntercept(ball))
            {
                return;
            }

            // Whether this was a pass finding its man decides whether it is worth
            // any momentum. Read BEFORE taking possession: attaching the ball is
            // what makes this player its holder, and the passer would be lost.
            //
            // Deliberately not every pickup. Collecting a ball the opposition
            // lost, or one of your own that came back off the keeper, is not a
            // pass completed — and charging for those would mean a scrappy
            // passage of play filled a bar that is supposed to reward keeping it.
            bool completedPass = ball.LastHolder != null
                && ball.LastHolder != gameObject
                && myTeamMember != null
                && ball.LastHolder.TryGetComponent(out Gameplay.TeamMember passer)
                && passer.team == myTeamMember.team;

            ball.AttachToPlayer(ballSocket);
            currentBall = ball;

            if (!completedPass)
            {
                return;
            }

            if (Gameplay.TensionManager.Instance != null)
            {
                Gameplay.TensionManager.Instance.AddPassCompleted(myTeamMember.team);
            }

            if (Core.MatchManager.Instance != null)
            {
                Core.MatchManager.Instance.RecordPass(myTeamMember.team);
            }
        }

        /// <summary>
        /// Contests a pass this player has stepped into, and reports whether the
        /// contact was consumed by it — win or lose. The caller must not fall
        /// through to an ordinary pickup either way: collecting a pass you have
        /// just failed to cut out is precisely the outcome the duel exists to
        /// prevent.
        ///
        /// Resolved in REAL TIME. There is no panel and no freeze: the two sides
        /// of this contest are nowhere near each other — the passer is wherever
        /// they played it from, half a pitch away — so there is nothing to stage
        /// over a shoulder and no read to make. Stopping the match dead to show
        /// a two-line panel with one button on it interrupted the one passage of
        /// play that is entirely about momentum.
        ///
        /// Only a ball still travelling counts. One that has slowed to a roll is
        /// nobody's pass any more, and treating it as one would turn every loose
        /// ball in the middle of the pitch into a duel.
        /// </summary>
        private bool TryInitiateIntercept(Gameplay.BallController ball)
        {
            if (myTeamMember == null || Gameplay.ClashManager.Instance == null)
            {
                return false;
            }

            // A keeper facing a shot is not intercepting a pass — that duel was
            // already fought and lost by whoever struck the ball, and reopening
            // it here would let the keeper save the same shot twice.
            if (myTeamMember.isGoalkeeper)
            {
                return false;
            }

            if (!ball.TryGetComponent(out Rigidbody ballBody)
                || ballBody.linearVelocity.magnitude <= interceptSpeedThreshold)
            {
                return false;
            }

            GameObject holder = ball.LastHolder;

            if (holder == null || holder == gameObject)
            {
                return false;
            }

            if (!holder.TryGetComponent(out Gameplay.TeamMember passer) || passer.team == myTeamMember.team)
            {
                return false;
            }

            // The return value is deliberately discarded: whether the ball was
            // won or the interceptor was left stunned watching it go past, this
            // contact belonged to the interception and must not also count as a
            // pickup.
            Gameplay.ClashManager.Instance.ResolveRealTimeIntercept(holder, myTeamMember);

            return true;
        }

        /// <summary>
        /// Raises a duel when this player is in contact with an opposing
        /// carrier. Asymmetric on purpose: only the player WITHOUT the ball
        /// starts it, so a single contact produces one clash rather than two
        /// mirrored ones from both handlers.
        /// </summary>
        private void TryInitiateClash(Collider playerCollider)
        {
            // The cooldown belongs here, not in CanContestBall: it is what keeps
            // two overlapping players from duelling again the moment the last
            // one ends, and it must not hold up ordinary loose-ball pickups.
            if (!Gameplay.ClashManager.CanInitiateClash)
            {
                return;
            }

            if (myTeamMember == null || HasBall)
            {
                return;
            }

            if (!playerCollider.TryGetComponent(out PlayerBallHandler otherHandler) || !otherHandler.HasBall)
            {
                return;
            }

            if (!playerCollider.TryGetComponent(out Gameplay.TeamMember otherTeamMember))
            {
                return;
            }

            if (myTeamMember.team == otherTeamMember.team)
            {
                return;
            }

            // Attacker is the one holding the ball; this player is the challenger.
            Core.TacticalEvents.OnClashInitiated?.Invoke(otherTeamMember, myTeamMember);
        }

        /// <summary>
        /// Moves the ball from <paramref name="victim"/> to this player. Called
        /// by the ClashManager when a duel goes the defender's way.
        ///
        /// Both players' pickup cooldowns are stamped so neither can reclaim the
        /// ball on the very next contact tick — they are still overlapping when
        /// this runs, so without it possession would ping-pong every frame.
        /// Stunning the loser is the ClashManager's job, not this one's.
        /// </summary>
        public void WinBallFrom(PlayerBallHandler victim)
        {
            if (victim == null)
            {
                return;
            }

            Gameplay.BallController stolenBall = victim.currentBall;
            if (stolenBall == null)
            {
                return;
            }

            victim.ForceDropBall();
            stolenBall.AttachToPlayer(ballSocket);
            currentBall = stolenBall;

            StartPickupCooldown();
            victim.StartPickupCooldown();
        }

        private bool IsPickupOnCooldown()
        {
            return Time.time - lastPassTime < pickupCooldown;
        }

        private void StartPickupCooldown()
        {
            lastPassTime = Time.time;
        }
    }
}
