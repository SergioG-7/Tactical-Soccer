using UnityEngine;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.AI
{
    /// <summary>
    /// Keeper brain: slides along the goal line tracking the ball's X, clamped
    /// to the width of its own goal. Deliberately independent of the route
    /// system — a keeper reacts continuously, whereas routes are discrete orders
    /// re-issued on a think interval, which would read as jerky at this range.
    ///
    /// It still yields to an explicit drawn route, so the human can pull their
    /// own keeper out of the area if they want to.
    /// </summary>
    [RequireComponent(typeof(PlayerBallHandler))]
    public class GoalkeeperAI : MonoBehaviour
    {
        [Header("Movement")]
        public float speed = 5f;
        public float maxLateralMovement = 3.5f;

        [Header("Clearance")]
        [Tooltip("Whether this keeper hoofs the ball upfield on its own. ON for " +
                 "AI sides, whose keeper would otherwise hold the ball forever — " +
                 "the squad AI never routes keepers and team-mates cannot tackle " +
                 "their own. OFF for the human's keeper, who has a player to " +
                 "decide the pass: clearing blind just handed possession straight " +
                 "back to the opposition every single time.")]
        public bool autoClearance = true;

        [Tooltip("How long the keeper holds the ball before hoofing it upfield.")]
        [SerializeField] private float holdDuration = 0.8f;

        [Tooltip("How far up the pitch the clearance is aimed.")]
        [SerializeField] private float clearanceDistance = 14f;

        private Transform ball;
        private Vector3 startPosition;

        private PlayerRoute route;
        private PlayerBallHandler ballHandler;

        private float holdStartTime;
        private bool wasHoldingBall;

        private void Start()
        {
            startPosition = transform.position;

            route = GetComponent<PlayerRoute>();
            ballHandler = GetComponent<PlayerBallHandler>();

            BallController ballController = FindAnyObjectByType<BallController>();
            if (ballController != null)
            {
                ball = ballController.transform;
            }
        }

        private void Update()
        {
            if (ballHandler != null && ballHandler.HasBall)
            {
                TrackHeldBall();
                return;
            }

            wasHoldingBall = false;

            if (!CanMove())
            {
                return;
            }

            transform.position = Vector3.MoveTowards(transform.position, CalculateTargetPosition(), speed * Time.deltaTime);
        }

        /// <summary>
        /// A stunned keeper is frozen like anyone else, and an explicitly drawn
        /// route wins over the automatic tracking — otherwise both would write
        /// to the same Transform on the same frame and fight each other.
        /// </summary>
        private bool CanMove()
        {
            if (ball == null)
            {
                return false;
            }

            return route == null || (!route.IsStunned && !route.IsFollowingRoute);
        }

        /// <summary>
        /// Only X moves: the keeper stays glued to its own goal line, so Y and Z
        /// come from the spawn slot and the lateral travel is capped to the goal
        /// mouth rather than the whole pitch.
        /// </summary>
        private Vector3 CalculateTargetPosition()
        {
            float clampedX = Mathf.Clamp(
                ball.position.x,
                startPosition.x - maxLateralMovement,
                startPosition.x + maxLateralMovement);

            return new Vector3(clampedX, startPosition.y, startPosition.z);
        }

        /// <summary>
        /// Without this the game deadlocks: the general AI skips keepers, so a
        /// keeper who catches the ball would hold it forever and its own team
        /// can never tackle it back off him.
        /// </summary>
        private void TrackHeldBall()
        {
            if (!autoClearance)
            {
                return;
            }

            if (!wasHoldingBall)
            {
                wasHoldingBall = true;
                holdStartTime = Time.time;
                return;
            }

            if (Time.time - holdStartTime < holdDuration)
            {
                return;
            }

            wasHoldingBall = false;

            // Upfield is whichever way is away from the goal this keeper defends.
            float upfield = startPosition.z > 0f ? -1f : 1f;

            ballHandler.PassTo(new Vector3(
                transform.position.x,
                transform.position.y,
                transform.position.z + (upfield * clearanceDistance)));
        }
    }
}
