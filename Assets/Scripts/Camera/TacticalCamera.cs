using UnityEngine;
using TacticalSoccer.Gameplay;

// Namespace is deliberately NOT TacticalSoccer.Camera: that would shadow
// UnityEngine.Camera for every type declared inside it.
namespace TacticalSoccer.CameraSystem
{
    /// <summary>
    /// Owns the camera whenever something dramatic is happening: a duel, the
    /// flight of a shot, or an impact worth shaking the screen for. The rest of
    /// the time the ball-follower owns it, and the follower is switched off for
    /// the duration — two components writing the same Transform on the same
    /// frame is a fight, not a blend.
    ///
    /// The rig is PERSPECTIVE and angled, not the old orthographic bird's-eye:
    /// tilted back over the halfway line for play, and swung round behind the
    /// attacker's shoulder for a duel. That is also what makes the framing work
    /// at all — on an orthographic camera, moving closer changed the angle but
    /// never the size of anything, so every "zoom" had to be faked by shrinking
    /// the orthographic size. Here distance does the work, and the field of view
    /// is left alone unless a mode explicitly wants a different lens.
    ///
    /// Everything here runs on unscaled time. A duel freezes the match at
    /// timeScale 0, and a camera that only moved in scaled time would sit
    /// perfectly still through the entire sequence it exists to stage.
    /// </summary>
    public class TacticalCamera : MonoBehaviour
    {
        /// <summary>What the camera is currently doing with the transform.</summary>
        private enum ControlMode
        {
            /// <summary>Swinging back out to hand control to the follower.</summary>
            Returning,

            /// <summary>Staged on a duel, holding still.</summary>
            Clash,

            /// <summary>Chasing the ball through the air after a shot.</summary>
            BallFlight
        }

        [Header("Vista de juego")]
        [Tooltip("Pose the camera returns to: high, well behind the play and " +
                 "tilted forward, so the pitch runs away into the distance " +
                 "instead of being read off a map. Configured by the scene " +
                 "generator to match the follow rig, so handing control back is " +
                 "seamless.")]
        public Vector3 overheadPosition = new Vector3(0f, 22f, -18f);
        public Vector3 overheadRotation = new Vector3(55f, 0f, 0f);

        [Header("Duelo (sobre el hombro)")]
        [Tooltip("How far behind the attacker the camera sits, along the line " +
                 "between the two players. This is the whole shot: close enough " +
                 "that the attacker fills a shoulder of the frame, far enough " +
                 "that the defender they are about to meet is still in it.")]
        public float clashBackDistance = 5f;

        [Tooltip("How high above the attacker's feet the camera sits.")]
        public float clashHeight = 2.5f;

        [Tooltip("How far up the defender the camera aims. Zero would point the " +
                 "lens at their feet and put the pair's heads off the top.")]
        public float clashLookHeight = 1f;

        [Tooltip("Longest gap between the two players the over-the-shoulder " +
                 "staging is used across. Beyond it the camera slides up the " +
                 "line and frames the DEFENDER instead: an interception pairs a " +
                 "passer with someone half a pitch away, and staging that from " +
                 "behind the passer would show the actual duel as a dot on the " +
                 "horizon.")]
        public float clashMaxStagingDistance = 8f;

        [Tooltip("Lens used while staging a duel. Same as the match view by " +
                 "default: on a perspective rig the five metres do the zooming, " +
                 "and narrowing the lens on top of that reads as a lurch.")]
        public float clashFieldOfView = 50f;

        [Header("Vuelo del balón")]
        [Tooltip("How far BEHIND the ball the camera sits while chasing a shot " +
                 "— behind along the ball's own line of flight, not along a " +
                 "fixed world axis. A fixed offset put the camera in front of " +
                 "any shot travelling south, so half the goals in the match were " +
                 "watched with the ball flying into the lens.")]
        public float ballFlightBackDistance = 6f;

        [Tooltip("How high above the ball the camera rides.")]
        public float ballFlightHeight = 4f;

        [Tooltip("Lens used while chasing the ball.")]
        public float ballFlightFieldOfView = 50f;

        [Tooltip("Planar speed below which the flight direction is no longer " +
                 "trusted. Under it the last good direction is held instead: a " +
                 "ball momentarily stalled against a post or at the top of a lob " +
                 "has a direction that is pure noise, and following it would whip " +
                 "the camera around the pitch mid-shot.")]
        [SerializeField] private float ballFlightMinTrackedSpeed = 1f;

        [Header("Paneo manual")]
        [Tooltip("How far the player may drag the view off the automatic follow, " +
                 "in world units. Generous on X: the follow itself cannot move " +
                 "sideways at all on a wide window, so this is the only way to " +
                 "look down the wings.")]
        public Vector2 panLimitX = new Vector2(-10f, 10f);
        public Vector2 panLimitZ = new Vector2(-15f, 15f);

        public float transitionSpeed = 5f;

        [Tooltip("How quickly the camera latches onto the ball when the chase " +
                 "starts. Faster than the general transition: the ball is already " +
                 "moving, and easing in gently loses it off the top of the frame.")]
        [SerializeField] private float ballFlightCatchUpSpeed = 12f;

        [Header("Sacudida")]
        [Tooltip("How fast the camera whips through the noise field while " +
                 "shaking. High enough to read as an impact rather than as a " +
                 "wobble, low enough not to alias into a flicker.")]
        [SerializeField] private float shakeFrequency = 28f;

        [Tooltip("How close the camera must get to the overhead pose before the " +
                 "ball-follower is handed back control.")]
        [SerializeField] private float settleDistance = 0.35f;
        [SerializeField] private float settleAngle = 1.5f;

        private const float DefaultFieldOfView = 50f;

        private Vector3 targetPos;
        private Quaternion targetRot;
        private float targetFieldOfView;
        private float overheadFieldOfView;

        /// <summary>
        /// The pose the camera is actually interpolating towards its target.
        /// Tracked separately from transform.position because the shake is added
        /// on top: reading the transform back would feed the shake offset into
        /// the next frame's interpolation and the camera would wander off.
        /// </summary>
        private Vector3 basePosition;

        private UnityEngine.Camera cam;
        private CameraController follower;

        private ControlMode mode = ControlMode.Returning;

        /// <summary>True while this component owns the camera transform.</summary>
        private bool isControlling;

        private float ballFlightEndTime;

        /// <summary>
        /// The way the ball was last seen travelling, on the ground plane. Held
        /// between frames so a bounce, a deflection or the stall at the apex of
        /// a lob does not throw the chase camera around.
        /// </summary>
        private Vector3 flightDirection = Vector3.forward;
        private bool hasFlightDirection;

        /// <summary>
        /// How far the player has dragged the view away from the automatic
        /// follow. Survives duels and shots on purpose: losing your chosen
        /// viewpoint every time somebody made a tackle would be worse than not
        /// being able to pan at all. It is cleared only by a kickoff.
        /// </summary>
        private Vector3 panOffset = Vector3.zero;

        // How far out the rig sits, as a multiple of its designed offset.
        private float zoomScale = 1f;

        [Header("Zoom")]
        [Tooltip("Closest the pinch may bring the rig, as a share of its " +
                 "designed offset. Not lower: the rig is angled, so pulling it " +
                 "much nearer puts the camera among the players and starts " +
                 "clipping through them.")]
        [SerializeField] private float minZoomScale = 0.65f;

        [Tooltip("Furthest out. Beyond this the pitch stops filling the frame " +
                 "and the surrounding grass and the empty sky take over.")]
        [SerializeField] private float maxZoomScale = 1.6f;

        [Tooltip("Scale change per pixel of pinch. Tuned so a comfortable " +
                 "gesture across a phone screen covers most of the range " +
                 "without a flick jumping the whole way.")]
        [SerializeField] private float zoomSensitivity = 0.0015f;

        private float shakeIntensity;
        private float shakeTimeRemaining;
        private float shakeDuration;
        private float shakeSeed;

        public static TacticalCamera Instance { get; private set; }

        private void Awake()
        {
            Instance = this;

            cam = GetComponent<UnityEngine.Camera>();
            follower = GetComponent<CameraController>();

            overheadFieldOfView = cam != null ? cam.fieldOfView : DefaultFieldOfView;

            targetPos = overheadPosition;
            targetRot = Quaternion.Euler(overheadRotation);
            targetFieldOfView = overheadFieldOfView;
            basePosition = transform.position;
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            // Never leave the follower switched off behind us: the camera would
            // be stranded wherever the last duel left it.
            if (follower != null)
            {
                follower.enabled = true;
            }
        }

        /// <summary>
        /// Configures the resting pose from the scene generator, so this camera
        /// returns to exactly the rig the rest of the game was tuned against
        /// rather than to a hardcoded default.
        /// </summary>
        public void ConfigureOverhead(Vector3 position, Vector3 rotation)
        {
            overheadPosition = position;
            overheadRotation = rotation;

            targetPos = overheadPosition;
            targetRot = Quaternion.Euler(overheadRotation);
        }

        /// <summary>
        /// Rewritten by the generator on every run rather than left to the field
        /// default. A component already present in the scene keeps its serialized
        /// value, so changing the default alone silently does nothing.
        /// </summary>
        public void ConfigureZoom(float minScale, float maxScale, float sensitivity)
        {
            minZoomScale = minScale;
            maxZoomScale = maxScale;
            zoomSensitivity = sensitivity;

            // Whatever the last session pinched to is not a scene value. Reset
            // so a regenerated scene opens at the framing the game was tuned at.
            zoomScale = 1f;
            PushZoomToFollower();
        }

        public void ConfigureClashFraming(float backDistance, float height, float fieldOfView)
        {
            clashBackDistance = backDistance;
            clashHeight = height;
            clashFieldOfView = fieldOfView;
        }

        /// <summary>Same contract as <see cref="ConfigureClashFraming"/>.</summary>
        public void ConfigureBallFlightFraming(float backDistance, float height, float fieldOfView)
        {
            ballFlightBackDistance = backDistance;
            ballFlightHeight = height;
            ballFlightFieldOfView = fieldOfView;
        }

        /// <summary>
        /// Frames the duel from behind the attacker's shoulder, looking down the
        /// line at the defender. Called while the match is frozen, which is why
        /// every interpolation below runs on unscaled time.
        /// </summary>
        public void ZoomToClash(TeamMember attacker, TeamMember defender)
        {
            if (attacker == null || defender == null)
            {
                return;
            }

            Vector3 attackerPos = attacker.transform.position;
            Vector3 defenderPos = defender.transform.position;

            Vector3 line = defenderPos - attackerPos;
            line.y = 0f;

            // Two players standing exactly on top of each other give no line to
            // stage along. Falling back to the resting angle keeps a degenerate
            // duel from throwing the camera through a zero-vector LookRotation.
            Vector3 direction = line.sqrMagnitude > 0.0001f
                ? line.normalized
                : Quaternion.Euler(overheadRotation) * Vector3.forward;

            // The point the shot is built around. Normally the attacker; for a
            // pair that is nowhere near each other — an interception — a point
            // just up the line from the defender, so the camera stages the
            // contact rather than the spectator watching it from distance.
            float pairDistance = line.magnitude;

            Vector3 anchor = pairDistance > clashMaxStagingDistance
                ? defenderPos - (direction * clashMaxStagingDistance)
                : attackerPos;

            TakeControl();

            mode = ControlMode.Clash;

            targetPos = anchor - (direction * clashBackDistance) + (Vector3.up * clashHeight);
            targetRot = Quaternion.LookRotation((defenderPos + (Vector3.up * clashLookHeight)) - targetPos);
            targetFieldOfView = clashFieldOfView;
        }

        /// <summary>
        /// Chases the ball for <paramref name="duration"/> seconds, then swings
        /// back out on its own. This is what makes a shot readable: the strike
        /// resolves into a ball that actually travels, and a bird's-eye view of a
        /// 25 m/s dot crossing the box shows none of it.
        ///
        /// Unscaled seconds, so the duel unfreezing mid-flight does not stretch
        /// or cut the shot short.
        /// </summary>
        public void FollowBallCinematic(float duration)
        {
            if (BallController.Instance == null)
            {
                return;
            }

            TakeControl();

            mode = ControlMode.BallFlight;

            ballFlightEndTime = Time.unscaledTime + duration;
            targetFieldOfView = ballFlightFieldOfView;

            // Cleared per shot, not per frame: each chase reads its own opening
            // direction off the ball rather than inheriting the last one, which
            // would stage the first frames of a shot on the way the PREVIOUS
            // shot happened to be travelling.
            hasFlightDirection = false;
        }

        /// <summary>
        /// Kicks the camera around for <paramref name="time"/> seconds. The
        /// offset is added on top of whatever pose the camera is holding, so a
        /// shake never fights the duel framing or the ball chase — and if nothing
        /// else is staging anything, the shake itself takes control for its own
        /// duration and hands it straight back.
        /// </summary>
        public void Shake(float intensity, float time)
        {
            if (intensity <= 0f || time <= 0f)
            {
                return;
            }

            TakeControl();

            // A big shake already under way is not cut short by a small one
            // landing on top of it.
            shakeIntensity = Mathf.Max(shakeIntensity, intensity);
            shakeTimeRemaining = Mathf.Max(shakeTimeRemaining, time);
            shakeDuration = shakeTimeRemaining;

            // Re-rolled per shake so two impacts in a row do not trace the same
            // path through the noise field.
            shakeSeed = Random.value * 100f;
        }

        /// <summary>
        /// Drags the view by <paramref name="worldDelta"/>, the distance the
        /// ground under the pointer has travelled since last frame.
        ///
        /// Subtracted, not added: dragging the pitch up the screen has to move
        /// the CAMERA down, or the world would run away from the finger.
        /// </summary>
        public void AddPan(Vector3 worldDelta)
        {
            panOffset -= worldDelta;

            panOffset.x = Mathf.Clamp(panOffset.x, panLimitX.x, panLimitX.y);
            panOffset.y = 0f;
            panOffset.z = Mathf.Clamp(panOffset.z, panLimitZ.x, panLimitZ.y);

            PushPanToFollower();
        }

        /// <summary>
        /// Pinches the view in or out.
        ///
        /// <paramref name="pixelDelta"/> is how much further apart the two
        /// fingers are than they were last frame, so spreading them zooms IN —
        /// hence the subtraction, the same inversion the pan uses to keep the
        /// world under the finger.
        ///
        /// Kept here, next to the pan, so there is exactly one owner of "how the
        /// player has moved the camera by hand" and one place that pushes it to
        /// the follower.
        /// </summary>
        public void AddZoom(float pixelDelta)
        {
            zoomScale = Mathf.Clamp(zoomScale - (pixelDelta * zoomSensitivity),
                minZoomScale, maxZoomScale);

            PushZoomToFollower();
        }

        /// <summary>Current framing, 1 being the tuned one. Read by tests and by the debug menu.</summary>
        public float ZoomScale => zoomScale;

        private void PushZoomToFollower()
        {
            if (follower != null)
            {
                follower.SetZoomScale(zoomScale);
            }
        }


        /// <summary>
        /// Drops the manual pan and puts the view back on the ball. Called at
        /// every kickoff: play restarts from the centre spot, and a camera left
        /// staring at a corner from the last passage of play would hide it.
        ///
        /// The ZOOM is deliberately left alone. A pan is a place you drifted to
        /// and want taking back from; a zoom is a preference — how close this
        /// player likes to watch — and resetting it at every throw-in would be
        /// the camera arguing with them.
        /// </summary>
        public void CenterCamera()
        {
            panOffset = Vector3.zero;
            PushPanToFollower();

            // Whatever the camera was staging is over. This used to only
            // recompute the target while ALREADY returning, which meant a mode
            // that never handed control back stranded the view for good: a duel
            // that ended without resolving, or a flight chase whose ball was
            // collected on the same frame the whistle went, left the camera
            // parked over the spot it happened to be framing while play carried
            // on somewhere else entirely. Every restart now forces the swing
            // back, whatever mode it interrupts.
            ResetToOverhead();

            targetPos = ResolveRestingPosition();
        }

        /// <summary>
        /// The follower is what writes the camera during normal play, so it is
        /// the one that has to know about the pan. Kept here rather than in the
        /// input layer so there is exactly one owner of the offset.
        /// </summary>
        private void PushPanToFollower()
        {
            if (follower != null)
            {
                follower.SetPanOffset(panOffset);
            }
        }

        public void ResetToOverhead()
        {
            mode = ControlMode.Returning;

            targetRot = Quaternion.Euler(overheadRotation);
            targetFieldOfView = overheadFieldOfView;

            // Position is recomputed every frame in LateUpdate rather than fixed
            // here. Stays in control until the swing back has finished; the
            // follower is re-enabled once the pose has settled.
        }

        /// <summary>
        /// Borrows the camera from the follower. Seeding the base pose from the
        /// live transform is what keeps the takeover seamless — starting from a
        /// stale value would snap the camera before it began moving.
        /// </summary>
        private void TakeControl()
        {
            if (!isControlling)
            {
                basePosition = transform.position;
                isControlling = true;
            }

            if (follower != null)
            {
                follower.enabled = false;
            }
        }

        /// <summary>
        /// Where the camera belongs when no duel is on: whatever the ball
        /// follower is currently aiming at, not the centre spot. Returning to a
        /// fixed overhead pose made the camera fly back to the middle of the
        /// pitch after every duel and then pan out to the ball again.
        /// </summary>
        private Vector3 ResolveRestingPosition()
        {
            // The follower's own desired position already carries the pan; only
            // the no-follower fallback has to add it by hand.
            return follower != null ? follower.GetDesiredPosition() : overheadPosition + panOffset;
        }

        private void LateUpdate()
        {
            if (!isControlling)
            {
                return;
            }

            // Driven from here, before anything else and regardless of mode. The
            // follower is DISABLED for as long as this camera holds the transform,
            // so its own LateUpdate never runs — and a duel is exactly when
            // possession changes hands. Without this the lean freezes at whatever
            // the play was doing when the duel opened, and the camera is handed
            // back leaning the wrong way for the side that just won the ball.
            if (follower != null)
            {
                follower.TickLookAhead();
            }

            float t = UpdateTarget() * Time.unscaledDeltaTime;

            basePosition = Vector3.Lerp(basePosition, targetPos, t);
            transform.position = basePosition + UpdateShake();
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);

            ApplyFieldOfView(Mathf.Lerp(CurrentFieldOfView, targetFieldOfView, t));

            if (!HasReturnedToOverhead())
            {
                return;
            }

            // Snap the last fraction away and hand the camera back.
            basePosition = targetPos;
            transform.SetPositionAndRotation(targetPos, targetRot);

            ApplyFieldOfView(overheadFieldOfView);

            isControlling = false;

            if (follower != null)
            {
                follower.enabled = true;
            }
        }

        /// <summary>
        /// Field of view is the lens on a perspective rig. Guarded rather than
        /// written blind: on an orthographic camera the property exists but does
        /// nothing, and pretending otherwise would hide a misconfigured scene.
        /// </summary>
        private float CurrentFieldOfView =>
            cam != null ? cam.fieldOfView : overheadFieldOfView;

        private void ApplyFieldOfView(float value)
        {
            if (cam == null || cam.orthographic)
            {
                return;
            }

            cam.fieldOfView = value;
        }

        /// <summary>
        /// Points the camera at whatever the current mode is about, and returns
        /// the interpolation speed that mode wants.
        /// </summary>
        private float UpdateTarget()
        {
            switch (mode)
            {
                case ControlMode.Clash:
                    // Fixed by ZoomToClash; the pair is frozen, so is the frame.
                    return transitionSpeed;

                case ControlMode.BallFlight:
                    return UpdateBallFlightTarget();

                default:
                    // Tracks the live follow target, so the ball moving during the
                    // swing back does not leave the camera behind.
                    targetPos = ResolveRestingPosition();
                    return transitionSpeed;
            }
        }

        private float UpdateBallFlightTarget()
        {
            BallController ball = BallController.Instance;

            if (ball == null || Time.unscaledTime >= ballFlightEndTime || HasFlightBeenInterrupted(ball))
            {
                ResetToOverhead();
                targetPos = ResolveRestingPosition();

                return transitionSpeed;
            }

            Vector3 ballPosition = ball.transform.position;
            Vector3 direction = ResolveFlightDirection(ball);

            // Behind the ball along its OWN line, riding above it. This is what
            // makes every shot read the same way regardless of which end it is
            // aimed at: the ball always travels away from the viewer, into the
            // goal it is heading for.
            targetPos = ballPosition - (direction * ballFlightBackDistance)
                + (Vector3.up * ballFlightHeight);

            targetRot = Quaternion.LookRotation(ballPosition - targetPos);

            return ballFlightCatchUpSpeed;
        }

        /// <summary>
        /// Which way the ball is going, flattened onto the pitch. The vertical
        /// component is dropped deliberately: a lob spends its first moments
        /// travelling mostly upwards, and a camera that took that literally
        /// would start the shot by diving at the grass.
        /// </summary>
        private Vector3 ResolveFlightDirection(BallController ball)
        {
            Vector3 velocity = ball.Velocity;
            velocity.y = 0f;

            if (velocity.magnitude >= ballFlightMinTrackedSpeed)
            {
                flightDirection = velocity.normalized;
                hasFlightDirection = true;

                return flightDirection;
            }

            // Nothing trustworthy to read yet. Hold the last good heading, or
            // fall back to the pitch's own long axis on the very first frame.
            return hasFlightDirection ? flightDirection : Vector3.forward;
        }

        /// <summary>
        /// The chase is over the moment the shot stops being a shot. Running the
        /// full duration regardless left the camera swooping low over a ball
        /// that had already been caught, or staging a flight behind a duel panel
        /// that had reframed the shot somewhere else entirely.
        /// </summary>
        private bool HasFlightBeenInterrupted(BallController ball)
        {
            // Somebody has collected it: the passage of play is over.
            if (ball.IsHeld)
            {
                return true;
            }

            // A duel owns the camera outright, and a restart is about to put
            // everyone back in position.
            if (ClashManager.IsClashActive)
            {
                return true;
            }

            return Core.MatchManager.Instance != null
                && Core.MatchManager.Instance.IsWaitingForSetPiece;
        }

        /// <summary>
        /// Advances the shake and returns the offset to add to the pose. Noise
        /// rather than pure randomness, so the camera whips through a continuous
        /// path instead of teleporting to a new point every frame.
        /// </summary>
        private Vector3 UpdateShake()
        {
            if (shakeTimeRemaining <= 0f)
            {
                return Vector3.zero;
            }

            shakeTimeRemaining -= Time.unscaledDeltaTime;

            if (shakeTimeRemaining <= 0f)
            {
                shakeTimeRemaining = 0f;
                shakeIntensity = 0f;

                return Vector3.zero;
            }

            // Fades out over the shake's life rather than stopping dead, which
            // would leave the camera visibly jumping back on the last frame.
            float falloff = shakeDuration > 0f ? shakeTimeRemaining / shakeDuration : 0f;
            float amplitude = shakeIntensity * falloff;
            float phase = Time.unscaledTime * shakeFrequency;

            return new Vector3(
                (Mathf.PerlinNoise(phase, shakeSeed) - 0.5f) * 2f * amplitude,
                (Mathf.PerlinNoise(shakeSeed, phase) - 0.5f) * 2f * amplitude,
                0f);
        }

        private bool HasReturnedToOverhead()
        {
            if (mode != ControlMode.Returning)
            {
                return false;
            }

            // Handing the camera back mid-shake would leave the offset applied
            // to a transform this component no longer writes.
            if (shakeTimeRemaining > 0f)
            {
                return false;
            }

            return Vector3.Distance(basePosition, targetPos) <= settleDistance
                && Quaternion.Angle(transform.rotation, targetRot) <= settleAngle;
        }
    }
}
