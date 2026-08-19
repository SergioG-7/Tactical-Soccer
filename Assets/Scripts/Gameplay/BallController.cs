using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    public enum BallState
    {
        Free,
        Possessed
    }

    /// <summary>
    /// Minimal finite-state ball: either rolling freely under physics, or
    /// possessed and snapped to a player's socket. Knows nothing about
    /// input or the player that owns it beyond the socket Transform.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BallController : MonoBehaviour
    {
        [Header("Estela")]
        [Tooltip("Speed trail. Optional: the ball plays fine without one.")]
        [SerializeField] private TrailRenderer trail;

        [Tooltip("Speed above which the ball leaves a trail. High enough that " +
                 "only real strikes and driven passes streak — a ball trickling " +
                 "or bouncing to a stop should not.")]
        [SerializeField] private float trailSpeedThreshold = 8f;

        [Tooltip("What fraction of its pace the ball keeps on crossing the goal " +
                 "line. 0.1 stopped a full-force drive dead on the line, which " +
                 "read as hitting a wall; 0.35 carries it into the netting " +
                 "without punching through it.")]
        [SerializeField] private float netEntrySpeedScale = 0.35f;

        [Header("Sombra")]
        [Tooltip("Assigned by the scene generator. Left null the ball builds its " +
                 "own transparent material at runtime.")]
        [SerializeField] private Material shadowMaterial;

        [Tooltip("Diameter of the blob on the grass. Roughly the ball's own " +
                 "footprint plus a little, so it reads as a shadow rather than " +
                 "as a second object.")]
        [SerializeField] private float shadowSize = 0.6f;

        private const float MinKickHeight = 0.3f;

        // Just clear of the pitch plane at y=0, so the two never z-fight.
        private const float ShadowGroundY = 0.01f;

        private static readonly Vector3 KickoffPosition = new Vector3(0f, 0.5f, 0f);

        private BallState currentState = BallState.Free;
        private Transform currentOwnerSocket;
        private Rigidbody rb;

        /// <summary>
        /// The blob on the grass under the ball. Deliberately NOT a child of the
        /// ball: the ball spins, and a parented quad would spin with it and
        /// tumble on edge. It is an unparented object that merely follows.
        /// </summary>
        private GameObject dropShadow;

        /// <summary>
        /// Latched the instant the ball leaves the field, cleared once it is
        /// genuinely back between the lines.
        ///
        /// Without it a restart re-triggered itself. The ball rides on a socket
        /// behind the taker, so a corner placed exactly on the flag left the
        /// ball a few centimetres BEHIND the goal line — still out of play by
        /// the very check that had just awarded the corner, which awarded
        /// another one on the next frame, and another, for as long as anyone
        /// watched. The placement is fixed at the other end too; this is what
        /// makes any repeat impossible rather than merely unlikely.
        /// </summary>
        private bool isOutOfPlay;

        /// <summary>True while nobody owns the ball, i.e. it can be picked up.</summary>
        public bool IsFree => currentState == BallState.Free;

        /// <summary>
        /// True while a player has the ball on their foot.
        ///
        /// Deliberately NOT `transform.parent != null`: this ball is never
        /// re-parented. Possession snaps it onto the owner's socket every
        /// LateUpdate instead, so its parent is null at all times and a parent
        /// test would report "loose" even while somebody is running with it.
        /// </summary>
        public bool IsHeld => currentState == BallState.Possessed;

        /// <summary>
        /// Who kicked or lost the ball last. Lets a handler tell its own rebound
        /// apart from a genuinely loose ball, so a shot that comes back off the
        /// keeper does not snap straight onto the shooter's foot.
        /// </summary>
        public GameObject LastHolder { get; private set; }

        /// <summary>
        /// Who is holding the ball RIGHT NOW, or null if it is loose.
        ///
        /// Derived from the socket the ball is actually riding on, which makes
        /// this the single source of truth about possession — a handler's own
        /// HasBall is that handler's opinion, and the two can come apart. When
        /// they did, players who each believed they had the ball started duels
        /// over a ball that was lying somewhere else entirely.
        /// </summary>
        public GameObject Holder =>
            currentState == BallState.Possessed && currentOwnerSocket != null && currentOwnerSocket.parent != null
                ? currentOwnerSocket.parent.gameObject
                : null;

        /// <summary>
        /// How fast the ball is travelling. Exposed so the camera can read the
        /// direction of a struck shot without doing a GetComponent on the
        /// Rigidbody every frame of the chase.
        /// </summary>
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;

        /// <summary>There is exactly one ball, and several systems need to read
        /// where it is without holding a serialized reference to it.</summary>
        public static BallController Instance { get; private set; }

        private void Awake()
        {
            Instance = this;

            rb = GetComponent<Rigidbody>();

            if (trail == null)
            {
                trail = GetComponent<TrailRenderer>();
            }

            if (trail != null)
            {
                trail.emitting = false;
            }

            CreateDropShadow();
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// The shadow is an independent root object, so nothing else will clean
        /// it up when the ball goes away — it would simply be left sitting on
        /// the grass.
        /// </summary>
        private void OnDestroy()
        {
            if (dropShadow != null)
            {
                Destroy(dropShadow);
                dropShadow = null;
            }
        }

        public void AssignTrail(TrailRenderer trailRenderer)
        {
            trail = trailRenderer;
        }

        /// <summary>Assigned by the scene generator, which owns material assets.</summary>
        public void ConfigureShadowMaterial(Material material)
        {
            shadowMaterial = material;
        }

        /// <summary>
        /// Builds the blob under the ball. This is what makes height readable at
        /// all: on an angled camera a ball high in the air and a ball rolling
        /// along the grass project to almost the same place on screen, and the
        /// gap between ball and shadow is the only thing that tells them apart.
        /// </summary>
        private void CreateDropShadow()
        {
            dropShadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            dropShadow.name = "Ball Drop Shadow";

            // A collider here would be a flat invisible wall lying on the pitch:
            // the ball would bounce off its own shadow.
            Collider quadCollider = dropShadow.GetComponent<Collider>();

            if (quadCollider != null)
            {
                Destroy(quadCollider);
            }

            dropShadow.transform.localScale = new Vector3(shadowSize, shadowSize, shadowSize);

            MeshRenderer shadowRenderer = dropShadow.GetComponent<MeshRenderer>();

            if (shadowRenderer != null)
            {
                shadowRenderer.sharedMaterial = shadowMaterial != null
                    ? shadowMaterial
                    : BuildFallbackShadowMaterial();

                // It is a fake shadow, not a real object: it must not cast one
                // of its own, nor be lit by anything.
                shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shadowRenderer.receiveShadows = false;
            }

            UpdateDropShadow();
        }

        /// <summary>
        /// Only used when the scene was built without a shadow material asset.
        /// URP ships its shaders opaque, so the alpha below is ignored unless the
        /// material is explicitly flipped to alpha blending — without this the
        /// "shadow" comes out as a solid black tile.
        /// </summary>
        private static Material BuildFallbackShadowMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader)
            {
                name = "BallShadowMaterial (runtime)",
                color = new Color(0f, 0f, 0f, 0.5f)
            };

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            return material;
        }

        /// <summary>
        /// Pins the blob to the ground under the ball, facing straight up. The
        /// rotation is rewritten every frame rather than set once: a Quad is
        /// built standing up, and this is also what keeps the shadow flat
        /// regardless of how the ball itself is spinning.
        /// </summary>
        private void UpdateDropShadow()
        {
            if (dropShadow == null)
            {
                return;
            }

            dropShadow.transform.position = new Vector3(
                transform.position.x, ShadowGroundY, transform.position.z);

            dropShadow.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        public void AttachToPlayer(Transform socket)
        {
            currentState = BallState.Possessed;
            currentOwnerSocket = socket;
            rb.isKinematic = true;
        }

        public void Release()
        {
            // The ball is never re-parented — it is positioned onto the socket
            // each LateUpdate — so transform.parent is always null. The owner has
            // to be read off the socket, whose parent IS the player.
            if (currentOwnerSocket != null && currentOwnerSocket.parent != null)
            {
                LastHolder = currentOwnerSocket.parent.gameObject;
            }

            currentState = BallState.Free;
            currentOwnerSocket = null;
            rb.isKinematic = false;
        }

        /// <summary>
        /// Takes the pace off a ball that has just crossed the line, so it drops
        /// into the net instead of through it.
        ///
        /// Scaled rather than zeroed: a ball stopped dead on the goal line looks
        /// like it hit something, while one that carries on slowly and settles
        /// in the netting reads as a goal. The trail is cut in the same breath —
        /// a streak that keeps drawing while the ball rolls to a stop in the net
        /// is the one bit of the effect that still says "this is travelling
        /// fast".
        ///
        /// Not the same thing as <see cref="Stop"/>: the ball has to keep moving
        /// a little, and it must stay dynamic so it falls.
        /// </summary>
        public void DampenIntoNet()
        {
            if (rb == null || rb.isKinematic)
            {
                return;
            }

            rb.linearVelocity *= netEntrySpeedScale;
            rb.angularVelocity *= netEntrySpeedScale;

            if (trail != null)
            {
                trail.emitting = false;
            }
        }

        public void Kick(Vector3 forceDirection, float forceMagnitude)
        {
            Release();

            // Lift the ball clear of the ground before it turns dynamic, so the
            // solver never has to resolve an interpenetration on the first step.
            transform.position = new Vector3(
                transform.position.x,
                Mathf.Max(transform.position.y, MinKickHeight),
                transform.position.z);

            // Drop whatever velocity the body carried while it was kinematic:
            // otherwise it compounds with the impulse and launches the ball.
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            rb.AddForce(forceDirection.normalized * forceMagnitude, ForceMode.Impulse);

            // Sounded from the ball rather than from whoever kicked it. Every
            // way the ball is ever struck — a pass, a shot, a keeper's
            // clearance, a free kick, a penalty — comes through here, so this is
            // the one place that cannot miss one, and no caller has to remember.
            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PlayKick();
            }
        }

        /// <summary>
        /// Puts the ball back on the centre spot, dead. Releases possession
        /// first: a possessed ball is kinematic and glued to a socket, so
        /// repositioning it without releasing would be undone by LateUpdate.
        ///
        /// Raising OnMatchReset here is deliberate. Releasing the ball
        /// physically and clearing possession logically must never come apart:
        /// when they did, a handler kept pointing at a ball it no longer had —
        /// a ghost carrier that still reported HasBall from across the pitch and
        /// triggered phantom clashes.
        /// </summary>
        public void ResetToKickoff()
        {
            Release();

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            transform.position = KickoffPosition;

            // The centre spot is about as in-play as a point gets, so the latch
            // would clear itself next frame anyway. Doing it here means the very
            // first frame after a goal is already armed, rather than a frame in
            // which the ball is back but nothing would notice it leaving again.
            isOutOfPlay = false;

            if (trail != null)
            {
                // Otherwise the trail draws a streak from wherever the ball was
                // straight across the pitch to the centre spot.
                trail.emitting = false;
                trail.Clear();
            }

            Core.TacticalEvents.OnMatchReset?.Invoke();
        }

        private void Update()
        {
            UpdateTrail();

            // A goal is being shown, and the ball is sitting in the back of the
            // net: past the goal line, outside the mouth, out of play by every
            // measure below. Without this the celebration would rule itself a
            // goal kick a frame after the goal and clear the ball upfield in the
            // middle of it.
            if (Core.MatchManager.IsGoalBeingCelebrated)
            {
                return;
            }

            // A carried ball is checked too. It used to be exempt — a possessed
            // ball is kinematic and glued to a socket — which meant a player
            // could simply run out over the touchline with it and keep playing,
            // because nothing was ever measuring where it had got to.
            bool inPlay = Core.PitchBounds.IsBallInPlay(transform.position);

            if (isOutOfPlay)
            {
                // Re-armed by the ball itself coming back inside the lines, not
                // by a timer or by whoever takes the restart: the restart is
                // taken from ON a line, so anything that re-armed on the kick
                // would be re-arming while the ball was still outside.
                if (inPlay)
                {
                    isOutOfPlay = false;
                }

                return;
            }

            if (inPlay)
            {
                return;
            }

            isOutOfPlay = true;

            HandleOutOfPlay();
        }

        /// <summary>
        /// The ball has left the field of play. Which restart follows depends on
        /// which line it crossed and who put it there:
        ///
        ///   touchline            -> throw-in to the other side
        ///   goal line, defender  -> corner to the attackers
        ///   goal line, attacker  -> goal kick to the defenders
        ///
        /// Anything else — through the back of the net, or through the floor —
        /// has no sensible restart, so play returns to the centre.
        /// </summary>
        private void HandleOutOfPlay()
        {
            Vector3 exitPoint = transform.position;

            bool aboveFloor = exitPoint.y > Core.PitchBounds.FallThroughFloorY;

            // The GOAL LINE is tested first, and the touchline only gets what is
            // left. In the corners both tests fire at once — a ball leaving at
            // (14, 24) is past the touchline AND past the goal line — and the
            // order used to be the other way round, so every ball that went out
            // near a corner flag was given as a throw-in on the goal line. That
            // is the one part of the pitch where the two restarts differ most:
            // a corner is a chance, a throw-in level with the six-yard box is
            // not.
            //
            // The width test keeps a legitimate goal out of this: inside the
            // posts, crossing the goal line is a goal, not a restart.
            bool overGoalLine = aboveFloor
                && Mathf.Abs(exitPoint.z) > Core.PitchBounds.GoalLineZ
                && Mathf.Abs(exitPoint.x) > Core.PitchBounds.GoalMouthHalfWidth;

            bool overTouchline = aboveFloor
                && !overGoalLine
                && Mathf.Abs(exitPoint.x) > Core.PitchBounds.SideLineX;

            // Read the owner AFTER releasing: while the ball is still possessed
            // LastHolder is stale, and Release is what stamps the carrier onto
            // it. On an already-free ball this leaves the last kicker in place.
            Release();
            GameObject holder = LastHolder;

            // Whoever last touched it no longer has it, and no OnMatchReset is
            // coming to tell them so on a restart that is not a kickoff.
            if (holder != null && holder.TryGetComponent(out Player.PlayerBallHandler holderHandler))
            {
                holderHandler.ForceDropBall();
            }

            // With no known last toucher there is nobody to award the restart
            // against, so play returns to the centre rather than guessing.
            bool knownToucher = false;
            TeamId lastTeam = TeamId.Blue;

            if (holder != null && holder.TryGetComponent(out TeamMember lastToucher))
            {
                knownToucher = true;
                lastTeam = lastToucher.team;
            }

            if (Core.MatchManager.Instance != null && knownToucher && (overTouchline || overGoalLine))
            {
                StopDead();

                // Exactly one restart, always. The two flags are already
                // mutually exclusive — the goal-line test wins the corners and
                // the touchline gets what is left — and the if/else is what
                // keeps them that way if either condition is ever loosened.
                if (overTouchline)
                {
                    Core.MatchManager.Instance.StartThrowIn(Opponent(lastTeam), exitPoint);
                }
                else if (overGoalLine)
                {
                    // Whose goal line it went over. Blue defends negative Z, Red
                    // defends positive Z, so the sign of the exit point names the
                    // defending side outright.
                    TeamId defendingSide = exitPoint.z > 0f ? TeamId.Red : TeamId.Blue;

                    // A defender putting it behind his own line concedes a
                    // corner; an attacker putting it behind theirs gives the
                    // defenders a goal kick.
                    bool putBehindByDefenders = lastTeam == defendingSide;

                    if (putBehindByDefenders)
                    {
                        Core.MatchManager.Instance.StartCorner(Opponent(defendingSide), exitPoint);
                    }
                    else
                    {
                        Core.MatchManager.Instance.StartGoalKick(defendingSide, exitPoint);
                    }
                }

                Core.TacticalEvents.OnBallOutOfBounds?.Invoke();

                return;
            }

            // ResetToKickoff raises OnMatchReset itself.
            ResetToKickoff();

            Core.TacticalEvents.OnBallOutOfBounds?.Invoke();
        }

        /// <summary>
        /// Kills all motion and the trail, so the ball does not keep rolling
        /// away from the mark a restart is about to be taken from.
        /// </summary>
        private void StopDead()
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }
        }

        private static TeamId Opponent(TeamId team)
        {
            return team == TeamId.Blue ? TeamId.Red : TeamId.Blue;
        }

        /// <summary>
        /// The trail marks pace, not travel. A possessed ball is kinematic and
        /// carries no velocity worth reading, so it never streaks.
        /// </summary>
        private void UpdateTrail()
        {
            if (trail == null)
            {
                return;
            }

            trail.emitting = currentState == BallState.Free
                && rb.linearVelocity.magnitude > trailSpeedThreshold;
        }

        private void LateUpdate()
        {
            if (currentState == BallState.Possessed && currentOwnerSocket != null)
            {
                transform.position = currentOwnerSocket.position;
            }

            // After the snap, never before: a shadow updated first would trail
            // one frame behind the ball it belongs to, all the way across the
            // pitch on a driven pass.
            UpdateDropShadow();
        }

    }
}
