using UnityEngine;
using TacticalSoccer.Gameplay;
using TacticalSoccer.Player;

namespace TacticalSoccer.AI
{
    // IA del portero: se desliza por su línea de gol siguiendo la X del balón, dentro del ancho de su portería.
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

        // Guarda la posición inicial y busca el balón y los componentes necesarios.
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

        // Sigue el balón lateralmente, o lo despeja si lo tiene en sus manos.
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

        // True si el portero puede moverse por su cuenta (no está aturdido ni siguiendo una ruta manual).
        private bool CanMove()
        {
            if (ball == null)
            {
                return false;
            }

            return route == null || (!route.IsStunned && !route.IsFollowingRoute);
        }

        // Calcula la posición objetivo del portero, moviéndose solo en X dentro de los límites de su portería.
        private Vector3 CalculateTargetPosition()
        {
            float clampedX = Mathf.Clamp(
                ball.position.x,
                startPosition.x - maxLateralMovement,
                startPosition.x + maxLateralMovement);

            return new Vector3(clampedX, startPosition.y, startPosition.z);
        }

        // Mientras el portero tiene el balón, espera el tiempo de espera y luego lo despeja hacia adelante.
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

            // Hacia adelante es la dirección contraria a la portería que defiende este portero.
            float upfield = startPosition.z > 0f ? -1f : 1f;

            ballHandler.PassTo(new Vector3(
                transform.position.x,
                transform.position.y,
                transform.position.z + (upfield * clearanceDistance)));
        }
    }
}
