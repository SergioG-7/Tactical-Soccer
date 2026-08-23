using UnityEngine;

namespace TacticalSoccer.CameraSystem
{
    // Sigue a un objetivo (normalmente el balón) por el campo, manteniendo altura fija y sin salirse de los límites del terreno.
    public class CameraController : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 22f, -18f);
        [SerializeField] private float smoothTime = 0.3f;

        [SerializeField] private Vector2 minBounds = new Vector2(-5f, -10f);
        [SerializeField] private Vector2 maxBounds = new Vector2(5f, 10f);

        [Tooltip("Distancia de anticipación de la cámara hacia la dirección de ataque del equipo en posesión.")]
        [SerializeField] private float lookAheadDistance = 1.8f;

        [Tooltip("Límite máximo de anticipación para evitar que el portador quede fuera de encuadre.")]
        [SerializeField] private float maxLookAhead = 2.2f;

        [Tooltip("Tiempo de suavizado para reorientar la cámara tras un cambio de posesión.")]
        [SerializeField] private float lookAheadSmoothTime = 0.9f;

        private Vector3 followVelocity;

        // Adelanto de cámara actual en Z, suavizado hacia la dirección de juego.
        private float lookAhead;
        private float lookAheadVelocity;

        // Desplazamiento manual (pan) aplicado por el jugador.
        private Vector3 panOffset;

        // Configura el objetivo a seguir, el offset y los límites del encuadre.
        public void Configure(Transform followTarget, Vector3 followOffset, Vector2 min, Vector2 max)
        {
            target = followTarget;
            offset = followOffset;
            minBounds = min;
            maxBounds = max;
        }

        // Configura la distancia, el máximo y la suavidad del adelanto de cámara.
        public void ConfigureLookAhead(float distance, float maximum, float smoothTime)
        {
            lookAheadDistance = distance;
            maxLookAhead = maximum;
            lookAheadSmoothTime = smoothTime;
        }

        // Aplica el desplazamiento manual (pan) de la cámara.
        public void SetPanOffset(Vector3 pan)
        {
            panOffset = pan;
        }

        // Escala el offset de la cámara para acercar o alejar el encuadre (zoom con pinch).
        public void SetZoomScale(float scale)
        {
            zoomScale = scale;
        }

        private float zoomScale = 1f;

        // Calcula la posición deseada de la cámara según el objetivo, el zoom, el adelanto y el pan.
        public Vector3 GetDesiredPosition()
        {
            if (target == null)
            {
                return transform.position;
            }

            Vector3 zoomedOffset = offset * zoomScale;

            Vector3 desiredPosition = new Vector3(
                target.position.x + zoomedOffset.x,
                zoomedOffset.y,
                target.position.z + zoomedOffset.z + lookAhead);

            desiredPosition.x = Mathf.Clamp(desiredPosition.x, minBounds.x, maxBounds.x);
            desiredPosition.z = Mathf.Clamp(desiredPosition.z, minBounds.y, maxBounds.y);

            return desiredPosition + panOffset;
        }

        // Suaviza el adelanto de cámara hacia la dirección de ataque del equipo con el balón, o a neutro si nadie lo tiene.
        public void TickLookAhead()
        {
            float desired = 0f;

            Gameplay.BallController ball = Gameplay.BallController.Instance;

            if (ball != null && ball.Holder != null
                && ball.Holder.TryGetComponent(out Gameplay.TeamMember carrier))
            {
                desired = -Core.PitchBounds.DefendedSide(carrier.team) * lookAheadDistance;
            }

            desired = Mathf.Clamp(desired, -maxLookAhead, maxLookAhead);

            lookAhead = Mathf.SmoothDamp(
                lookAhead, desired, ref lookAheadVelocity, lookAheadSmoothTime, Mathf.Infinity,
                Time.unscaledDeltaTime);

            lookAhead = Mathf.Clamp(lookAhead, -maxLookAhead, maxLookAhead);
        }

        // Actualiza la posición de la cámara cada frame siguiendo al objetivo.
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
