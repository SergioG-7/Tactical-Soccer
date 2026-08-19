using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalSoccer.Player
{
    /// <summary>
    /// Owns a single player's drawn route: collects the drag points coming
    /// from the input layer, renders them, and drives movement along the
    /// path once the drag is released. Also owns the stun state, since being
    /// stunned is precisely the inability to run a route.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class PlayerRoute : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;

        [Tooltip("Speed multiplier while carrying the ball. Under 1 on purpose: " +
                 "a carrier who moves as fast as everyone else can never be run " +
                 "down, so closing a player and forcing a duel is impossible.")]
        [SerializeField] private float carrierSpeedMultiplier = 0.75f;

        [Tooltip("Speed multiplier once the player is blown. This is what makes " +
                 "stamina cost something: a run drawn on an exhausted player " +
                 "still happens, it just happens at walking pace.")]
        [SerializeField] private float exhaustedSpeedMultiplier = 0.5f;

        [SerializeField] private float waypointReachedThreshold = 0.05f;

        [Header("Route Drawing")]
        [SerializeField] private float minPointDistance = 0.3f;

        [Tooltip("Width of the drawn route, in world units. A tenth of a unit " +
                 "was a hairline on a phone: the camera sees some 27 units of " +
                 "pitch across a screen a few inches wide, so the line has to be " +
                 "measured against the players it is drawn between — a quarter of " +
                 "a unit is about half the width of a capsule.")]
        [SerializeField] private float lineWidth = 0.25f;

        [Tooltip("Longest route that may be drawn, in world units — about the " +
                 "length of the pitch. Without a cap a single drag could scribble " +
                 "an unlimited path and buy a player a run that outlasts several " +
                 "passages of play, which is not a plan, it is a queue.")]
        [SerializeField] private float maxRouteLength = 50f;

        [Header("Direction Arrow")]
        [SerializeField] private float arrowLength = 0.9f;
        [SerializeField] private float arrowHalfWidth = 0.45f;

        [Header("Stun Feedback")]
        [SerializeField] private Color stunBlinkColor = Color.gray;
        [SerializeField] private float stunBlinkInterval = 0.15f;

        private readonly List<Vector3> routePoints = new List<Vector3>();

        /// <summary>
        /// Length of the path drawn so far. Accumulated as points come in rather
        /// than measured over the whole list each time: the drag adds a point
        /// every few centimetres, so re-walking the list per point would turn
        /// drawing into quadratic work.
        /// </summary>
        private float routeLength;

        private LineRenderer lineRenderer;
        private LineRenderer arrowRenderer;
        private Coroutine followRouteCoroutine;

        /// <summary>Formation slot this player returns to when play restarts.</summary>
        private Vector3 initialPosition;

        private float stunEndTime;

        private MeshRenderer meshRenderer;
        private Color originalColor;
        private Coroutine blinkCoroutine;

        private PlayerBallHandler ballHandler;
        private Gameplay.TeamMember teamMember;

        /// <summary>Hidden for AI-controlled sides, so their plans stay secret.</summary>
        private bool routeVisualsHidden;

        public bool IsStunned => Time.time < stunEndTime;

        private bool HasBall => ballHandler != null && ballHandler.HasBall;

        /// <summary>
        /// True while a drawn route is actually being walked. Lets automatic
        /// movers (the keeper) stand down instead of fighting the coroutine for
        /// the same Transform.
        /// </summary>
        public bool IsFollowingRoute => followRouteCoroutine != null;

        private void Awake()
        {
            lineRenderer = GetComponent<LineRenderer>();
            lineRenderer.positionCount = 0;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;

            initialPosition = transform.position;

            ballHandler = GetComponent<PlayerBallHandler>();
            meshRenderer = GetComponent<MeshRenderer>();

            // Read the colour off the shared asset rather than .material: the
            // latter would instantiate a private material copy for every player
            // at startup, even the ones that never get stunned. The copy is made
            // lazily instead, on the first blink.
            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            {
                originalColor = meshRenderer.sharedMaterial.color;
            }

            // ...which is only right for as long as nobody repaints the player.
            // See RefreshOriginalColor below.

            // Kept, not just read once: the run speed consults its stamina every
            // frame while a route is being walked.
            teamMember = GetComponent<Gameplay.TeamMember>();

            // Only the human side gets to see its routes drawn.
            routeVisualsHidden = teamMember != null && teamMember.team != Gameplay.TeamId.Blue;

            if (routeVisualsHidden)
            {
                lineRenderer.enabled = false;
            }
            else
            {
                CreateArrow();
            }
        }

        /// <summary>
        /// Tells this player what colour it is now, so a stun blink puts it back
        /// to that instead of to the colour it was born in.
        ///
        /// Needed because the shirt can be changed after Awake: the kit is
        /// chosen on the configuration screen and painted on at the opening
        /// whistle, and the colour cached above was read from the SHARED
        /// material before any of that happened. Without this, the first player
        /// stunned while wearing a green shirt would blink back to blue and stay
        /// blue for the rest of the match.
        /// </summary>
        public void RefreshOriginalColor(Color color)
        {
            originalColor = color;
        }

        private void OnEnable()
        {
            Core.TacticalEvents.OnMatchReset += HandleMatchReset;
        }

        private void OnDisable()
        {
            Core.TacticalEvents.OnMatchReset -= HandleMatchReset;
        }

        /// <summary>
        /// Moves the slot this player is sent back to when play restarts from
        /// the centre. Kept in step with the drift's own slot: without it, the
        /// first goal of the match would snap everybody back to the shape they
        /// were spawned in and silently undo the chosen formation.
        /// </summary>
        public void SetFormationSlot(Vector3 position)
        {
            initialPosition = position;
        }

        /// <summary>
        /// Freezes the player for <paramref name="duration"/> seconds and throws
        /// away whatever run was in progress. Used as the penalty for losing the
        /// ball to a tackle.
        /// </summary>
        public void ApplyStun(float duration)
        {
            stunEndTime = Time.time + duration;
            CancelRoute();

            // Restarting rather than letting the running loop pick up the new
            // end time, so a re-stun always begins on a visible colour change.
            if (blinkCoroutine != null)
            {
                StopCoroutine(blinkCoroutine);
                blinkCoroutine = null;
            }

            if (meshRenderer != null)
            {
                blinkCoroutine = StartCoroutine(BlinkRoutine());
            }
        }

        public void BeginRoute()
        {
            // A stunned player cannot be given new orders.
            if (IsStunned)
            {
                return;
            }

            StopFollowingRoute();

            routePoints.Clear();
            routeLength = 0f;
            routePoints.Add(transform.position);
            RefreshRouteVisuals();
        }

        public void AddRoutePoint(Vector3 point)
        {
            if (routePoints.Count == 0)
            {
                return;
            }

            // Already at the cap. Silently ignoring further drag is the point:
            // the finger carries on moving, the line simply stops growing.
            if (routeLength >= maxRouteLength)
            {
                return;
            }

            Vector3 last = routePoints[routePoints.Count - 1];
            float segment = Vector3.Distance(last, point);

            if (segment < minPointDistance)
            {
                return;
            }

            // The segment that would overrun the budget is TRUNCATED rather than
            // dropped, so the line ends exactly on the limit. Dropping it whole
            // would leave the route ending wherever the last accepted point
            // happened to fall, up to a segment short of the cap — a visibly
            // ragged cut-off that differed every time.
            if (routeLength + segment > maxRouteLength)
            {
                float remaining = maxRouteLength - routeLength;
                point = last + ((point - last).normalized * remaining);
                segment = remaining;
            }

            routeLength += segment;
            routePoints.Add(point);
            RefreshRouteVisuals();
        }

        public void EndRoute()
        {
            if (routePoints.Count < 2)
            {
                ClearRoute();
                return;
            }

            followRouteCoroutine = StartCoroutine(FollowRouteCoroutine());
        }

        /// <summary>
        /// Drops the drawn route AND halts any run already under way. Halting
        /// matters: the follow coroutine caches its current waypoint locally,
        /// so merely clearing the point list would leave it dragging the player
        /// towards a target that no longer exists.
        /// </summary>
        public void CancelRoute()
        {
            StopFollowingRoute();
            ClearRoute();
        }

        private void HandleMatchReset()
        {
            CancelRoute();
            transform.position = initialPosition;
        }

        private void StopFollowingRoute()
        {
            if (followRouteCoroutine != null)
            {
                StopCoroutine(followRouteCoroutine);
                followRouteCoroutine = null;
            }
        }

        /// <summary>
        /// Flashes the player's own material for as long as the stun lasts, so
        /// the penalty reads at a glance instead of looking like a stuck unit.
        ///
        /// Writes go through .material, never .sharedMaterial: team-mates share
        /// one material asset, so tinting the shared copy would blink the whole
        /// side. Timing is unscaled because drawing a route drops timeScale to
        /// 0.1, which would stretch each flash to 1.5 real seconds.
        /// </summary>
        private IEnumerator BlinkRoutine()
        {
            Material instance = meshRenderer.material;
            bool showStunColor = true;

            while (IsStunned)
            {
                instance.color = showStunColor ? stunBlinkColor : originalColor;
                showStunColor = !showStunColor;

                yield return new WaitForSecondsRealtime(stunBlinkInterval);
            }

            instance.color = originalColor;
            blinkCoroutine = null;
        }

        private void CreateArrow()
        {
            GameObject arrowObject = new GameObject("RouteArrow");
            arrowObject.transform.SetParent(transform, false);

            arrowRenderer = arrowObject.AddComponent<LineRenderer>();
            arrowRenderer.useWorldSpace = true;
            arrowRenderer.positionCount = 3;
            arrowRenderer.startWidth = lineWidth;
            arrowRenderer.endWidth = lineWidth;
            arrowRenderer.sharedMaterial = lineRenderer.sharedMaterial;
            arrowRenderer.enabled = false;
        }

        private void RefreshRouteVisuals()
        {
            if (routeVisualsHidden)
            {
                return;
            }

            lineRenderer.positionCount = routePoints.Count;
            lineRenderer.SetPositions(routePoints.ToArray());

            RefreshArrow();
        }

        /// <summary>
        /// Draws a V-shaped head at the last point, opening back along the final
        /// segment. Built from a LineRenderer rather than a quad so it reuses the
        /// route's own material and needs no separate texture or facing logic.
        /// </summary>
        private void RefreshArrow()
        {
            if (arrowRenderer == null)
            {
                return;
            }

            if (routePoints.Count < 2)
            {
                arrowRenderer.enabled = false;
                return;
            }

            Vector3 tip = routePoints[routePoints.Count - 1];
            Vector3 direction = tip - routePoints[routePoints.Count - 2];
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                arrowRenderer.enabled = false;
                return;
            }

            direction.Normalize();

            Vector3 side = Vector3.Cross(Vector3.up, direction) * arrowHalfWidth;
            Vector3 back = tip - (direction * arrowLength);

            arrowRenderer.SetPosition(0, back + side);
            arrowRenderer.SetPosition(1, tip);
            arrowRenderer.SetPosition(2, back - side);
            arrowRenderer.enabled = true;
        }

        private void ClearRoute()
        {
            routePoints.Clear();
            routeLength = 0f;
            lineRenderer.positionCount = 0;

            if (arrowRenderer != null)
            {
                arrowRenderer.enabled = false;
            }
        }

        private IEnumerator FollowRouteCoroutine()
        {
            for (int i = 1; i < routePoints.Count; i++)
            {
                // Route points sit on the pitch surface (they come from ground
                // raycasts, or from the ball's own position for AI-driven runs),
                // so following them in full 3D would sink the capsule into the
                // grass. Movement is horizontal only; the drawn line stays on
                // the ground where it belongs.
                Vector3 target = Core.PitchBounds.ClampPlayer(routePoints[i]);
                target.y = transform.position.y;

                while (Vector3.Distance(transform.position, target) > waypointReachedThreshold)
                {
                    if (IsStunned)
                    {
                        yield return null;
                        continue;
                    }

                    // Read every frame, not once: possession can change mid-run,
                    // and the player who just won the ball should slow down for
                    // the rest of the route they are already on. Stamina is read
                    // the same way, so a player who empties the tank halfway
                    // through a run visibly dies on his feet rather than
                    // finishing at the pace he set off with.
                    float speed = moveSpeed * (HasBall ? carrierSpeedMultiplier : 1f);

                    if (teamMember != null && teamMember.IsExhausted)
                    {
                        speed *= exhaustedSpeedMultiplier;
                    }

                    // Momentum, read the same way and for the same reason: a
                    // side can enter or leave the zone mid-run, and the turn of
                    // pace is the visible half of the whole mechanic.
                    if (teamMember != null && Gameplay.TensionManager.Instance != null)
                    {
                        speed *= Gameplay.TensionManager.Instance.SpeedMultiplier(teamMember.team);
                    }

                    transform.position = Core.PitchBounds.ClampPlayer(
                        Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime));

                    yield return null;
                }
            }

            followRouteCoroutine = null;
            ClearRoute();
        }
    }
}
