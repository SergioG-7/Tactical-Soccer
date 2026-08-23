using UnityEngine;

namespace TacticalSoccer.CameraSystem
{
    // Sigue a un objetivo (normalmente el balón) por el campo, manteniendo altura fija y sin salirse de los límites del terreno.
    public class CameraController : MonoBehaviour
    {
        [Header("Follow")]
        [SerializeField] private Transform target;
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
