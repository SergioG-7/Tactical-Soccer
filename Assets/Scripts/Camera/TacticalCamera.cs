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

        [Tooltip("Posición base de la cámara en vista general táctica.")]
        public Vector3 overheadPosition = new Vector3(0f, 22f, -18f);
        public Vector3 overheadRotation = new Vector3(55f, 0f, 0f);

        [Tooltip("Distancia detrás del atacante durante un duelo sobre el hombro.")]
        public float clashBackDistance = 5f;

        [Tooltip("Altura de la cámara sobre los pies del atacante en un duelo.")]
        public float clashHeight = 2.5f;

        [Tooltip("Altura del punto de mira sobre el defensor para encuadrar las cabezas.")]
        public float clashLookHeight = 1f;

        [Tooltip("Distancia máxima entre jugadores para usar el encuadre sobre el hombro.")]
        public float clashMaxStagingDistance = 8f;

        [Tooltip("Campo de visión (FOV) utilizado durante la cámara de duelo.")]
        public float clashFieldOfView = 50f;

        [Tooltip("Distancia detrás del balón a lo largo de su vector de vuelo.")]
        public float ballFlightBackDistance = 6f;

        [Tooltip("Altura de la cámara por encima del balón en seguimiento de disparo.")]
        public float ballFlightHeight = 4f;

        [Tooltip("Campo de visión (FOV) durante el seguimiento del balón.")]
        public float ballFlightFieldOfView = 50f;

        [Tooltip("Velocidad mínima en el plano XZ para calcular la dirección de vuelo del balón.")]
        [SerializeField] private float ballFlightMinTrackedSpeed = 1f;

        [Tooltip("Límites de desplazamiento manual de la vista en el eje X.")]
        public Vector2 panLimitX = new Vector2(-10f, 10f);

        [Tooltip("Límites de desplazamiento manual de la vista en el eje Z.")]
        public Vector2 panLimitZ = new Vector2(-15f, 15f);

        public float transitionSpeed = 5f;

        [Tooltip("Velocidad de enganche inicial de la cámara al iniciar el seguimiento del balón.")]
        [SerializeField] private float ballFlightCatchUpSpeed = 12f;

        [Tooltip("Frecuencia de sacudida de la cámara durante impactos.")]
        [SerializeField] private float shakeFrequency = 28f;

        [Tooltip("Distancia umbral para devolver el control al seguimiento táctico tras una transición.")]
        [SerializeField] private float settleDistance = 0.35f;

        [Tooltip("Ángulo umbral para completar la transición de vuelta a la vista general.")]
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
