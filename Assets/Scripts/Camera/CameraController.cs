using UnityEngine;

// Namespace is deliberately NOT TacticalSoccer.Camera: that would shadow
// UnityEngine.Camera for every type declared inside it.
namespace TacticalSoccer.CameraSystem
{
    /// <summary>
    /// Smoothly trails a target (normally the ball) across the pitch while
    /// holding a fixed height, and refuses to pan far enough to reveal the
    /// void beyond the pitch edges.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private Transform target;
        // Behind as well as above: the rig is an angled perspective camera, so
        // the negative Z is what puts the play in front of the lens instead of
        // directly underneath it.
        [SerializeField] private Vector3 offset = new Vector3(0f, 22f, -18f);
        [SerializeField] private float smoothTime = 0.3f;

        [Header("Pitch Bounds (world X / Z)")]
        [SerializeField] private Vector2 minBounds = new Vector2(-5f, -10f);
        [SerializeField] private Vector2 maxBounds = new Vector2(5f, 10f);

        [Header("Anticipación")]
        [Tooltip("How far up the pitch the camera leans in the direction the " +
                 "side in possession is attacking. The rig trails the ball by a " +
                 "fixed distance, so without this the carrier runs at a defence " +
                 "the player cannot see yet — the useful information is always " +
                 "just off the top of the screen.")]
        [SerializeField] private float lookAheadDistance = 1.8f;

        [Tooltip("Hard ceiling on the lean, whatever the distance above asks " +
                 "for. The lean exists to show a little more of where the play " +
                 "is going, not to move the frame off the player: at 5 the " +
                 "carrier was pushed to the edge of the screen while running, " +
                 "which is the opposite of useful. Clamped rather than only " +
                 "reduced, because SmoothDamp overshoots on a sharp turnover.")]
        [SerializeField] private float maxLookAhead = 2.2f;

        [Tooltip("How long the lean takes to swing across when possession " +
                 "changes. Slow on purpose: a turnover flips the direction " +
                 "outright, and snapping it would throw the view across the " +
                 "pitch on every tackle.")]
        [SerializeField] private float lookAheadSmoothTime = 0.9f;

        private Vector3 followVelocity;

        /// <summary>
        /// The lean currently applied, in world units on Z. Smoothed towards the
        /// direction of play rather than set outright, which is what keeps a
        /// change of possession from being a jolt.
        /// </summary>
        private float lookAhead;
        private float lookAheadVelocity;

        /// <summary>
        /// Manual pan the player has dragged in. Owned and clamped by
        /// TacticalCamera; pushed in here because this component is what writes
        /// the camera position during normal play — a pan applied only to the
        /// duel camera's resting pose would be erased the instant the follower
        /// took the transform back.
        /// </summary>
        private Vector3 panOffset;

        public void Configure(Transform followTarget, Vector3 followOffset, Vector2 min, Vector2 max)
        {
            target = followTarget;
            offset = followOffset;
            minBounds = min;
            maxBounds = max;
        }

        /// <summary>
        /// Written explicitly by the scene generator on every pass.
        ///
        /// Not left to the field defaults above: Unity keeps whatever a
        /// component in the scene was last serialised with, so lowering a
        /// default changes nothing for a rig that already exists — the camera
        /// would go on leaning 5 units for every scene generated before this.
        /// </summary>
        public void ConfigureLookAhead(float distance, float maximum, float smoothTime)
        {
            lookAheadDistance = distance;
            maxLookAhead = maximum;
            lookAheadSmoothTime = smoothTime;
        }

        public void SetPanOffset(Vector3 pan)
        {
            panOffset = pan;
        }

        /// <summary>
        /// How far out the rig sits, as a multiple of its designed offset. 1 is
        /// the framing the game was tuned at; below 1 is closer, above is
        /// further away.
        ///
        /// A SCALE on the whole offset rather than a change of height, because
        /// this is an angled perspective rig: raising Y alone would tip the view
        /// towards the horizon and flatten the pitch, while scaling the vector
        /// keeps the angle exactly as it was and only changes how much is in
        /// frame. Same reason it is not a field-of-view change, which would
        /// distort the perspective instead of pulling back.
        ///
        /// Written here rather than applied to the transform by whoever handles
        /// the gesture: this component is the single writer of the camera's
        /// position during play, and a second one would fight it every frame.
        /// </summary>
        public void SetZoomScale(float scale)
        {
            zoomScale = scale;
        }

        // Neutral until somebody pinches. Not serialised: it is a live gesture
        // state, and a value saved into the scene would silently reframe the
        // pitch for the next session.
        private float zoomScale = 1f;

        /// <summary>
        /// Where this rig wants the camera right now. Exposed so another system
        /// that has borrowed the camera can hand it back to the exact spot the
        /// follow would be at, instead of dumping it at some fixed pose.
        /// </summary>
        public Vector3 GetDesiredPosition()
        {
            if (target == null)
            {
                return transform.position;
            }

            // The target's own Y is ignored: the camera stays at offset height
            // regardless of the ball bouncing.
            //
            // The lean is added to the target's Z BEFORE the bounds are applied,
            // so it is subject to the same clamp as the follow itself. Leaning
            // past the goal line would show the void behind the net, which is
            // exactly what those bounds exist to prevent.
            // Scaled by the pinch. The lean is NOT scaled with it: it is a
            // fixed look-ahead in world units, and multiplying it would make the
            // view lead further the further out you zoomed.
            Vector3 zoomedOffset = offset * zoomScale;

            Vector3 desiredPosition = new Vector3(
                target.position.x + zoomedOffset.x,
                zoomedOffset.y,
                target.position.z + zoomedOffset.z + lookAhead);

            // Bounds are asymmetric on Z, and have to be: the camera trails the
            // play by a fixed distance, so the window of camera positions that
            // keeps the pitch framed is not centred on the origin the way it was
            // when the rig hung straight overhead.
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, minBounds.x, maxBounds.x);
            desiredPosition.z = Mathf.Clamp(desiredPosition.z, minBounds.y, maxBounds.y);

            // Added AFTER the automatic clamp, not before. Those bounds exist to
            // stop the FOLLOW revealing the void, and on a wide window the
            // horizontal budget is already zero — clamping the pan with them
            // would silently make sideways panning do nothing at all. A manual
            // pan is allowed out over the surrounding grass; it has its own
            // limits, applied where it is accumulated.
            return desiredPosition + panOffset;
        }

        /// <summary>
        /// Eases the lean towards whichever way the side in possession is
        /// playing, and back to neutral when the ball is loose.
        ///
        /// Public, and called from outside as well as from this component's own
        /// LateUpdate. While the duel camera has borrowed the transform, THIS
        /// component is disabled and gets no LateUpdate at all — so the lean
        /// would freeze at whatever it was when the duel opened and then be wrong
        /// for the side that came out of it with the ball. The borrower ticks it
        /// instead, which keeps the swing running through the duel and hands the
        /// camera back already leaning the right way.
        ///
        /// Unscaled time: a duel freezes the match, and the swing should not
        /// stop halfway across and resume when the panel closes.
        ///
        /// A loose ball leans nowhere. Guessing a direction from the ball's own
        /// velocity was the obvious alternative and is worse: the ball changes
        /// direction constantly while it bounces, and the camera would hunt.
        /// </summary>
        public void TickLookAhead()
        {
            float desired = 0f;

            Gameplay.BallController ball = Gameplay.BallController.Instance;

            if (ball != null && ball.Holder != null
                && ball.Holder.TryGetComponent(out Gameplay.TeamMember carrier))
            {
                // Towards the goal that side attacks, which is the opposite end
                // from the one they defend.
                desired = -Core.PitchBounds.DefendedSide(carrier.team) * lookAheadDistance;
            }

            desired = Mathf.Clamp(desired, -maxLookAhead, maxLookAhead);

            lookAhead = Mathf.SmoothDamp(
                lookAhead, desired, ref lookAheadVelocity, lookAheadSmoothTime, Mathf.Infinity,
                Time.unscaledDeltaTime);

            // Clamped again AFTER the smoothing, not only before it: SmoothDamp
            // can overshoot its target when the target flips sign, which is
            // exactly what a turnover does.
            lookAhead = Mathf.Clamp(lookAhead, -maxLookAhead, maxLookAhead);
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            TickLookAhead();

            transform.position = Vector3.SmoothDamp(
                transform.position, GetDesiredPosition(), ref followVelocity, smoothTime);
        }
    }
}
