using UnityEngine;
using TacticalSoccer.Gameplay;

// Namespace is deliberately NOT TacticalSoccer.Camera: that would shadow
// UnityEngine.Camera for every type declared inside it.
namespace TacticalSoccer.CameraSystem
{
    // Controla la cámara durante momentos especiales: duelos, vuelo del balón tras un disparo y sacudidas de impacto.
    public class TacticalCamera : MonoBehaviour
    {
        // Qué está haciendo la cámara con su transform en este momento.
        private enum ControlMode
        {
            // Volviendo a la posición de seguimiento normal.
            Returning,

            // Encuadrando un duelo.
            Clash,

            // Siguiendo al balón tras un disparo.
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

        // Posición base hacia la que interpola la cámara, sin contar el offset del shake.
        private Vector3 basePosition;

        private UnityEngine.Camera cam;
        private CameraController follower;

        private ControlMode mode = ControlMode.Returning;

        // Si este componente controla actualmente el transform de la cámara.
        private bool isControlling;

        private float ballFlightEndTime;

        // Última dirección conocida del balón sobre el plano del suelo, para no perder el rumbo en rebotes o paradas.
        private Vector3 flightDirection = Vector3.forward;
        private bool hasFlightDirection;

        // Cuánto ha desplazado el jugador la vista respecto al seguimiento automático. Solo se resetea en el saque de centro.
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

        // Inicializa la cámara y guarda su pose de reposo.
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

        // Reactiva el seguidor de cámara al desactivarse este componente.
        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (follower != null)
            {
                follower.enabled = true;
            }
        }

        // Configura la pose de reposo de la cámara.
        public void ConfigureOverhead(Vector3 position, Vector3 rotation)
        {
            overheadPosition = position;
            overheadRotation = rotation;

            targetPos = overheadPosition;
            targetRot = Quaternion.Euler(overheadRotation);
        }

        // Configura los límites y la sensibilidad del zoom, reseteando el zoom actual.
        public void ConfigureZoom(float minScale, float maxScale, float sensitivity)
        {
            minZoomScale = minScale;
            maxZoomScale = maxScale;
            zoomSensitivity = sensitivity;

            zoomScale = 1f;
            PushZoomToFollower();
        }

        // Configura el encuadre de la cámara durante un duelo.
        public void ConfigureClashFraming(float backDistance, float height, float fieldOfView)
        {
            clashBackDistance = backDistance;
            clashHeight = height;
            clashFieldOfView = fieldOfView;
        }

        // Configura el encuadre de la cámara durante el vuelo del balón.
        public void ConfigureBallFlightFraming(float backDistance, float height, float fieldOfView)
        {
            ballFlightBackDistance = backDistance;
            ballFlightHeight = height;
            ballFlightFieldOfView = fieldOfView;
        }

        // Encuadra el duelo desde detrás del hombro del atacante, mirando hacia el defensor.
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

            // Si los dos jugadores están en el mismo punto no hay línea que seguir, así que se usa el ángulo de reposo.
            Vector3 direction = line.sqrMagnitude > 0.0001f
                ? line.normalized
                : Quaternion.Euler(overheadRotation) * Vector3.forward;

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

        // Persigue al balón durante los segundos indicados y luego vuelve sola a la vista normal.
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

            hasFlightDirection = false;
        }

        // Sacude la cámara durante el tiempo indicado, con la intensidad dada.
        public void Shake(float intensity, float time)
        {
            if (intensity <= 0f || time <= 0f)
            {
                return;
            }

            TakeControl();

            shakeIntensity = Mathf.Max(shakeIntensity, intensity);
            shakeTimeRemaining = Mathf.Max(shakeTimeRemaining, time);
            shakeDuration = shakeTimeRemaining;

            shakeSeed = Random.value * 100f;
        }

        // Mueve la vista según lo que se ha desplazado el suelo bajo el puntero.
        public void AddPan(Vector3 worldDelta)
        {
            panOffset -= worldDelta;

            panOffset.x = Mathf.Clamp(panOffset.x, panLimitX.x, panLimitX.y);
            panOffset.y = 0f;
            panOffset.z = Mathf.Clamp(panOffset.z, panLimitZ.x, panLimitZ.y);

            PushPanToFollower();
        }

        // Aplica un zoom manual según el pinch de los dos dedos.
        public void AddZoom(float pixelDelta)
        {
            zoomScale = Mathf.Clamp(zoomScale - (pixelDelta * zoomSensitivity),
                minZoomScale, maxZoomScale);

            PushZoomToFollower();
        }

        // Nivel de zoom actual, siendo 1 el encuadre por defecto.
        public float ZoomScale => zoomScale;

        // Envía el zoom actual al seguidor de cámara.
        private void PushZoomToFollower()
        {
            if (follower != null)
            {
                follower.SetZoomScale(zoomScale);
            }
        }

        // Resetea el paneo manual y fuerza a la cámara a volver a la vista normal, sin tocar el zoom.
        public void CenterCamera()
        {
            panOffset = Vector3.zero;
            PushPanToFollower();

            ResetToOverhead();

            targetPos = ResolveRestingPosition();
        }

        // Envía el paneo manual actual al seguidor de cámara.
        private void PushPanToFollower()
        {
            if (follower != null)
            {
                follower.SetPanOffset(panOffset);
            }
        }

        // Ordena a la cámara volver a la vista general.
        public void ResetToOverhead()
        {
            mode = ControlMode.Returning;

            targetRot = Quaternion.Euler(overheadRotation);
            targetFieldOfView = overheadFieldOfView;
        }

        // Toma el control de la cámara desde el seguidor, partiendo de su posición actual.
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

        // Devuelve la posición de reposo de la cámara: donde esté siguiendo el balón el seguidor.
        private Vector3 ResolveRestingPosition()
        {
            return follower != null ? follower.GetDesiredPosition() : overheadPosition + panOffset;
        }

        // Actualiza cada frame la posición, rotación y campo de visión de la cámara hacia su objetivo.
        private void LateUpdate()
        {
            if (!isControlling)
            {
                return;
            }

            // Se actualiza aquí porque el seguidor está desactivado mientras esta cámara tiene el control.
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

            // Ajusta la última fracción de golpe y devuelve el control al seguidor.
            basePosition = targetPos;
            transform.SetPositionAndRotation(targetPos, targetRot);

            ApplyFieldOfView(overheadFieldOfView);

            isControlling = false;

            if (follower != null)
            {
                follower.enabled = true;
            }
        }

        // Campo de visión actual de la cámara.
        private float CurrentFieldOfView =>
            cam != null ? cam.fieldOfView : overheadFieldOfView;

        // Aplica el campo de visión, ignorando cámaras ortográficas.
        private void ApplyFieldOfView(float value)
        {
            if (cam == null || cam.orthographic)
            {
                return;
            }

            cam.fieldOfView = value;
        }

        // Actualiza el objetivo de la cámara según el modo actual y devuelve la velocidad de interpolación.
        private float UpdateTarget()
        {
            switch (mode)
            {
                case ControlMode.Clash:
                    return transitionSpeed;

                case ControlMode.BallFlight:
                    return UpdateBallFlightTarget();

                default:
                    targetPos = ResolveRestingPosition();
                    return transitionSpeed;
            }
        }

        // Calcula el objetivo de la cámara mientras persigue al balón en vuelo.
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

            targetPos = ballPosition - (direction * ballFlightBackDistance)
                + (Vector3.up * ballFlightHeight);

            targetRot = Quaternion.LookRotation(ballPosition - targetPos);

            return ballFlightCatchUpSpeed;
        }

        // Calcula la dirección horizontal del balón, ignorando su componente vertical.
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

            return hasFlightDirection ? flightDirection : Vector3.forward;
        }

        // Indica si el vuelo del balón se ha interrumpido: recogido, duelo activo o a la espera de un saque.
        private bool HasFlightBeenInterrupted(BallController ball)
        {
            if (ball.IsHeld)
            {
                return true;
            }

            if (ClashManager.IsClashActive)
            {
                return true;
            }

            return Core.MatchManager.Instance != null
                && Core.MatchManager.Instance.IsWaitingForSetPiece;
        }

        // Avanza la sacudida de cámara y devuelve el offset a sumar a la posición, usando ruido para un movimiento continuo.
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

            float falloff = shakeDuration > 0f ? shakeTimeRemaining / shakeDuration : 0f;
            float amplitude = shakeIntensity * falloff;
            float phase = Time.unscaledTime * shakeFrequency;

            return new Vector3(
                (Mathf.PerlinNoise(phase, shakeSeed) - 0.5f) * 2f * amplitude,
                (Mathf.PerlinNoise(shakeSeed, phase) - 0.5f) * 2f * amplitude,
                0f);
        }

        // Indica si la cámara ya ha vuelto a su pose de reposo y puede devolver el control al seguidor.
        private bool HasReturnedToOverhead()
        {
            if (mode != ControlMode.Returning)
            {
                return false;
            }

            if (shakeTimeRemaining > 0f)
            {
                return false;
            }

            return Vector3.Distance(basePosition, targetPos) <= settleDistance
                && Quaternion.Angle(transform.rotation, targetRot) <= settleAngle;
        }
    }
}
