using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TacticalSoccer.Player
{
    // Gestiona la ruta dibujada de un jugador: recoge los puntos, la dibuja y mueve al jugador por ella. También gestiona el aturdimiento.
    [RequireComponent(typeof(LineRenderer))]
    public class PlayerRoute : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 5f;

        [Tooltip("Multiplicador de velocidad al conducir el balón.")]
        [SerializeField] private float carrierSpeedMultiplier = 0.75f;

        [Tooltip("Multiplicador de velocidad aplicado cuando el jugador está agotado.")]
        [SerializeField] private float exhaustedSpeedMultiplier = 0.5f;

        [SerializeField] private float waypointReachedThreshold = 0.05f;

        [SerializeField] private float minPointDistance = 0.3f;

        [Tooltip("Grosor de la línea de ruta dibujada en pantalla.")]
        [SerializeField] private float lineWidth = 0.25f;

        [Tooltip("Longitud máxima permitida para una ruta trazada.")]
        [SerializeField] private float maxRouteLength = 50f;

        [SerializeField] private float arrowLength = 0.9f;
        [SerializeField] private float arrowHalfWidth = 0.45f;

        [SerializeField] private Color stunBlinkColor = Color.gray;
        [SerializeField] private float stunBlinkInterval = 0.15f;

        private readonly List<Vector3> routePoints = new List<Vector3>();

        // Longitud de la ruta dibujada hasta ahora.
        private float routeLength;

        private LineRenderer lineRenderer;
        private LineRenderer arrowRenderer;
        private Coroutine followRouteCoroutine;

        // Puesto de la formación al que vuelve este jugador cuando se reinicia el juego.
        private Vector3 initialPosition;

        private float stunEndTime;

        private MeshRenderer meshRenderer;
        private Color originalColor;
        private Coroutine blinkCoroutine;

        private PlayerBallHandler ballHandler;
        private Gameplay.TeamMember teamMember;

        // Oculta las rutas de los equipos controlados por la IA.
        private bool routeVisualsHidden;

        // Cierto mientras el jugador está aturdido y no puede recibir órdenes.
        public bool IsStunned => Time.time < stunEndTime;

        private bool HasBall => ballHandler != null && ballHandler.HasBall;

        // Cierto mientras el jugador está recorriendo una ruta dibujada.
        public bool IsFollowingRoute => followRouteCoroutine != null;

        // Inicializa el LineRenderer, cachea componentes y crea la flecha de dirección.
        private void Awake()
        {
            lineRenderer = GetComponent<LineRenderer>();
            lineRenderer.positionCount = 0;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;

            initialPosition = transform.position;

            ballHandler = GetComponent<PlayerBallHandler>();
            meshRenderer = GetComponent<MeshRenderer>();

            if (meshRenderer != null && meshRenderer.sharedMaterial != null)
            {
                originalColor = meshRenderer.sharedMaterial.color;
            }

            teamMember = GetComponent<Gameplay.TeamMember>();

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

        // Actualiza el color base del jugador, para cuando cambia de camiseta después de Awake.
        public void RefreshOriginalColor(Color color)
        {
            originalColor = color;
        }

        // Se suscribe al reinicio del partido.
        private void OnEnable()
        {
            Core.TacticalEvents.OnMatchReset += HandleMatchReset;
        }

        // Se desuscribe del reinicio del partido.
        private void OnDisable()
        {
            Core.TacticalEvents.OnMatchReset -= HandleMatchReset;
        }

        // Fija el puesto de formación al que vuelve el jugador en cada reinicio.
        public void SetFormationSlot(Vector3 position)
        {
            initialPosition = position;
        }

        // Aturde al jugador durante un tiempo y cancela cualquier ruta en curso.
        public void ApplyStun(float duration)
        {
            stunEndTime = Time.time + duration;
            CancelRoute();

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

        // Empieza a dibujar una ruta nueva desde la posición actual.
        public void BeginRoute()
        {
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

        // Añade un punto a la ruta que se está dibujando, respetando la longitud máxima.
        public void AddRoutePoint(Vector3 point)
        {
            if (routePoints.Count == 0)
            {
                return;
            }

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

            // Se recorta el segmento que se pasaría del límite, en vez de descartarlo entero.
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

        // Termina el dibujo de la ruta y empieza a recorrerla.
        public void EndRoute()
        {
            if (routePoints.Count < 2)
            {
                ClearRoute();
                return;
            }

            followRouteCoroutine = StartCoroutine(FollowRouteCoroutine());
        }

        // Borra la ruta dibujada y detiene el recorrido en curso.
        public void CancelRoute()
        {
            StopFollowingRoute();
            ClearRoute();
        }

        // Cancela la ruta y devuelve al jugador a su puesto de formación.
        private void HandleMatchReset()
        {
            CancelRoute();
            transform.position = initialPosition;
        }

        // Detiene la corrutina que recorre la ruta.
        private void StopFollowingRoute()
        {
            if (followRouteCoroutine != null)
            {
                StopCoroutine(followRouteCoroutine);
                followRouteCoroutine = null;
            }
        }

        // Hace parpadear el material del jugador mientras dure el aturdimiento.
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

        // Crea el objeto que dibuja la flecha de dirección al final de la ruta.
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

        // Redibuja la línea de la ruta y la flecha de dirección.
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

        // Dibuja la punta de flecha en forma de V al final de la ruta.
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

        // Vacía la ruta dibujada y oculta sus visuales.
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

        // Mueve al jugador punto a punto por la ruta dibujada, respetando aturdimiento, estamina y ardor.
        private IEnumerator FollowRouteCoroutine()
        {
            for (int i = 1; i < routePoints.Count; i++)
            {
                Vector3 target = Core.PitchBounds.ClampPlayer(routePoints[i]);
                target.y = transform.position.y;

                while (Vector3.Distance(transform.position, target) > waypointReachedThreshold)
                {
                    if (IsStunned)
                    {
                        yield return null;
                        continue;
                    }

                    float speed = moveSpeed * (HasBall ? carrierSpeedMultiplier : 1f);

                    if (teamMember != null && teamMember.IsExhausted)
                    {
                        speed *= exhaustedSpeedMultiplier;
                    }

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
