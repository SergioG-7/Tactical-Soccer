using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.Input
{
    // Detecta el input táctil/ratón y lo convierte en órdenes: dibujar rutas, pasar, chutar o colocar jugadores en el saque.
    public class TacticalInputManager : MonoBehaviour
    {
        [SerializeField] private Camera mainCamera;

        [SerializeField] private LayerMask playerLayerMask;
        [SerializeField] private LayerMask groundLayerMask;

        [Tooltip("Capa de la portería (separada del suelo para evitar que las rutas se ajusten al larguero).")]
        [SerializeField] private LayerMask goalLayerMask;

        [SerializeField] private float tapThreshold = 50f;
        [SerializeField] private float tapMaxDuration = 0.3f;
        [SerializeField] private float maxRayDistance = 100f;

        [Tooltip("Color del anillo de selección bajo el jugador activo.")]
        [SerializeField] private Color selectionRingColor = new Color(0f, 1f, 0f, 0.5f);

        [SerializeField] private float selectionRingDiameter = 1.5f;

        [Tooltip("Elevación del disco sobre el césped para evitar problemas de solapamiento visual (z-fighting).")]
        [SerializeField] private float selectionRingGroundY = 0.05f;

        private const TeamId HumanTeam = TeamId.Blue;

        private readonly List<Player.PlayerBallHandler> humanSquad = new List<Player.PlayerBallHandler>();

        private Player.PlayerRoute selectedPlayerRoute;
        private Player.PlayerBallHandler selectedPlayerHandler;

        // Si el arrastre actual está colocando un jugador (saque) en vez de dibujando una ruta.
        private bool isPlacingPlayer;

        // Si el arrastre actual mueve la cámara en vez de dar una orden.
        private bool isPanningCamera;

        // Última posición del puntero sobre el plano del campo, en coordenadas de mundo.
        private Vector3 lastPanWorldPoint;

        // Si el gesto ya se ha movido lo suficiente para dejar de considerarse un toque.
        private bool hasPanEngaged;

        // Plano matemático del campo, usado para el paneo de cámara aunque el puntero salga del césped.
        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        private float pointerDownTime;
        private Vector2 pointerDownPosition;
        private bool isDragging;

        // Si hay dos dedos en pantalla haciendo zoom.
        private bool isPinching;

        // Distancia entre los dos dedos en el frame anterior, en píxeles.
        private float lastPinchDistance;

        // Activo mientras se levantan los dedos de un pinch, para no confundirlo con un arrastre nuevo.
        private bool isUnwindingPinch;

        // Disco en el césped que marca a qué jugador van dirigidas las órdenes.
        private GameObject selectionRing;

        // Toma la cámara principal si no se ha asignado ninguna.
        private void Awake()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }
        }

        // Prepara la plantilla humana y crea el marcador de selección.
        private void Start()
        {
            CacheHumanSquad();
            CreateSelectionRing();
        }

        // Destruye el marcador de selección al eliminar este componente.
        private void OnDestroy()
        {
            if (selectionRing != null)
            {
                Destroy(selectionRing);
                selectionRing = null;
            }
        }

        // Crea el disco visual que marca al jugador seleccionado, sin collider para no estorbar los raycasts.
        private void CreateSelectionRing()
        {
            selectionRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            selectionRing.name = "Selection Ring";

            Collider ringCollider = selectionRing.GetComponent<Collider>();

            if (ringCollider != null)
            {
                Destroy(ringCollider);
            }

            selectionRing.transform.localScale =
                new Vector3(selectionRingDiameter, 0.05f, selectionRingDiameter);

            MeshRenderer ringRenderer = selectionRing.GetComponent<MeshRenderer>();

            if (ringRenderer != null)
            {
                ringRenderer.sharedMaterial = BuildSelectionRingMaterial();
                ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ringRenderer.receiveShadows = false;
            }

            selectionRing.SetActive(false);
        }

        // Crea el material transparente del anillo de selección, con alternativa opaca si no hay shader URP.
        private Material BuildSelectionRingMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader != null ? shader : Shader.Find("Standard"))
            {
                name = "SelectionRingMaterial (runtime)",
                color = selectionRingColor
            };

            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            return material;
        }

        // Devuelve a qué jugador pertenece el marcador: el que se está arrastrando, o si no el que lleva el balón.
        private Transform ResolveMarkedPlayer()
        {
            if (isDragging && selectedPlayerRoute != null)
            {
                return selectedPlayerRoute.transform;
            }

            Player.PlayerBallHandler carrier = ResolveCarrier();

            return carrier != null ? carrier.transform : null;
        }

        // Actualiza cada frame la posición del anillo de selección bajo el jugador marcado.
        private void LateUpdate()
        {
            if (selectionRing == null)
            {
                return;
            }

            Transform marked = ResolveMarkedPlayer();

            bool visible = marked != null
                && Core.MatchManager.IsStarted
                && Core.MatchManager.IsPlayable
                && !Core.MatchManager.IsHalftime
                && !UI.SubstitutionUIController.IsOpen;

            if (!visible)
            {
                if (selectionRing.activeSelf)
                {
                    selectionRing.SetActive(false);
                }

                return;
            }

            if (!selectionRing.activeSelf)
            {
                selectionRing.SetActive(true);
            }

            Vector3 at = marked.position;

            selectionRing.transform.position = new Vector3(at.x, selectionRingGroundY, at.z);
        }

        // Asigna las capas de raycast usadas para jugadores, suelo y portería.
        public void ConfigureLayers(LayerMask playerMask, LayerMask groundMask, LayerMask goalMask)
        {
            playerLayerMask = playerMask;
            groundLayerMask = groundMask;
            goalLayerMask = goalMask;
        }

        // Lee el input cada frame: bloquea durante duelos y menús, gestiona el pinch de zoom y procesa toques y arrastres.
        private void Update()
        {
            if (ClashManager.IsClashActive)
            {
                return;
            }

            if (!Core.MatchManager.IsPlayable)
            {
                return;
            }

            if (!Core.MatchManager.IsStarted)
            {
                return;
            }

            if (Core.MatchManager.IsHalftime || UI.SubstitutionUIController.IsOpen)
            {
                return;
            }

            if (UI.PenaltyUIController.IsOpen || UI.DebugMenuUIController.IsOpen
                || UI.AudioSettingsUI.IsOpen || UI.PlayerEditUIController.IsOpen)
            {
                return;
            }

            // Se comprueba el pinch antes que el puntero para que no se interprete como un arrastre normal.
            if (UpdatePinch())
            {
                return;
            }

            if (Pointer.current == null)
            {
                return;
            }

            if (Pointer.current.press.wasPressedThisFrame)
            {
                pointerDownPosition = Pointer.current.position.ReadValue();

                pointerDownTime = Time.unscaledTime;

                TryBeginDrag();
            }
            else if (isDragging && Pointer.current.press.isPressed)
            {
                ContinueDrag();
            }
            else if (Pointer.current.press.wasReleasedThisFrame)
            {
                float distance = Vector2.Distance(pointerDownPosition, Pointer.current.position.ReadValue());
                float duration = Time.unscaledTime - pointerDownTime;
                bool isTap = distance <= tapThreshold && duration < tapMaxDuration;

                if (isTap)
                {
                    HandleTap();
                }
                else if (isDragging)
                {
                    EndDrag();
                }
            }
        }

        // Guarda en caché la lista de jugadores del equipo humano.
        private void CacheHumanSquad()
        {
            humanSquad.Clear();

            foreach (TeamMember member in FindObjectsByType<TeamMember>())
            {
                if (member.team != HumanTeam)
                {
                    continue;
                }

                if (member.TryGetComponent(out Player.PlayerBallHandler handler))
                {
                    humanSquad.Add(handler);
                }
            }
        }

        // Devuelve el jugador del equipo humano que lleva el balón, si hay alguno.
        private Player.PlayerBallHandler ResolveCarrier()
        {
            foreach (Player.PlayerBallHandler handler in humanSquad)
            {
                if (handler != null && handler.IsOnPitch && handler.HasBall)
                {
                    return handler;
                }
            }

            return null;
        }

        // Indica si el partido está esperando el saque de centro.
        private static bool IsWaitingForKickoff()
        {
            return Core.MatchManager.Instance != null && Core.MatchManager.Instance.isWaitingForKickoff;
        }

        // Indica si el partido está esperando un saque de falta, banda, córner, etc.
        private static bool IsAwaitingRestart()
        {
            return Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece;
        }

        // Resuelve el inicio de un arrastre: selecciona jugador o inicia el paneo de cámara.
        private void TryBeginDrag()
        {
            Ray ray = mainCamera.ScreenPointToRay(Pointer.current.position.ReadValue());

            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, playerLayerMask))
            {
                BeginCameraPan();
                return;
            }

            if (!hit.collider.TryGetComponent(out Player.PlayerRoute playerRoute))
            {
                BeginCameraPan();
                return;
            }

            // Solo se pueden dar órdenes a jugadores del equipo humano que estén en el campo.
            TeamMember member = hit.collider.GetComponent<TeamMember>();

            if (member == null || member.team != HumanTeam || !member.isStarter)
            {
                selectedPlayerRoute = null;
                selectedPlayerHandler = null;

                BeginCameraPan();
                return;
            }

            hit.collider.TryGetComponent(out Player.PlayerBallHandler handler);

            // El jugador que va a sacar una falta o saque de banda no se puede mover con una ruta.
            if (handler != null && handler.HasBall && IsAwaitingRestart())
            {
                selectedPlayerRoute = null;
                selectedPlayerHandler = null;

                BeginCameraPan();
                return;
            }

            selectedPlayerRoute = playerRoute;
            selectedPlayerHandler = handler;

            isPlacingPlayer = IsWaitingForKickoff();
            isDragging = true;

            if (isPlacingPlayer)
            {
                selectedPlayerRoute.CancelRoute();
                return;
            }

            selectedPlayerRoute.BeginRoute();
            Core.TacticalEvents.OnRouteDrawStarted?.Invoke();
        }

        // Lee un pinch de dos dedos y lo convierte en zoom de cámara. Devuelve true mientras la pantalla está ocupada por el gesto.
        private bool UpdatePinch()
        {
            Touchscreen screen = Touchscreen.current;

            if (screen == null)
            {
                return false;
            }

            int pressed = 0;
            Vector2 first = Vector2.zero;
            Vector2 second = Vector2.zero;

            foreach (UnityEngine.InputSystem.Controls.TouchControl touch in screen.touches)
            {
                if (!touch.press.isPressed)
                {
                    continue;
                }

                if (pressed == 0)
                {
                    first = touch.position.ReadValue();
                }
                else if (pressed == 1)
                {
                    second = touch.position.ReadValue();
                }

                pressed++;
            }

            if (pressed >= 2)
            {
                float distance = Vector2.Distance(first, second);

                if (!isPinching)
                {
                    // El primer frame solo adopta la distancia inicial, sin aplicar zoom todavía.
                    isPinching = true;

                    AbortGesture();
                }
                else if (CameraSystem.TacticalCamera.Instance != null)
                {
                    CameraSystem.TacticalCamera.Instance.AddZoom(distance - lastPinchDistance);
                }

                lastPinchDistance = distance;
                isUnwindingPinch = true;

                return true;
            }

            isPinching = false;

            if (!isUnwindingPinch)
            {
                return false;
            }

            // Todavía queda un dedo del pinch en pantalla; se sigue ignorando el input hasta que se levante.
            if (pressed > 0)
            {
                return true;
            }

            isUnwindingPinch = false;

            return false;
        }

        // Cancela desde fuera el gesto que esté en curso, por ejemplo cuando pita el árbitro.
        public void CancelActiveGesture()
        {
            AbortGesture();
        }

        // Descarta el gesto en curso sin confirmarlo, a diferencia de EndDrag que sí lo confirma.
        private void AbortGesture()
        {
            if (!isDragging)
            {
                return;
            }

            if (!isPanningCamera && !isPlacingPlayer && selectedPlayerRoute != null)
            {
                selectedPlayerRoute.CancelRoute();
                Core.TacticalEvents.OnRouteDrawEnded?.Invoke();
            }

            ReleaseDrag();
        }

        // Inicia el paneo de cámara sin dibujar ninguna ruta.
        private void BeginCameraPan()
        {
            selectedPlayerRoute = null;
            selectedPlayerHandler = null;

            isPanningCamera = true;
            isPlacingPlayer = false;
            isDragging = true;
            hasPanEngaged = false;
        }

        // Continúa el arrastre en curso: mueve la cámara, coloca al jugador o añade un punto a la ruta.
        private void ContinueDrag()
        {
            if (isPanningCamera)
            {
                ContinueCameraPan();
                return;
            }

            Ray ray = mainCamera.ScreenPointToRay(Pointer.current.position.ReadValue());

            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, groundLayerMask))
            {
                return;
            }

            if (isPlacingPlayer)
            {
                PlacePlayerAt(hit.point);
                return;
            }

            selectedPlayerRoute.AddRoutePoint(hit.point);
        }

        // Mueve la cámara según lo que se ha desplazado el punto del suelo bajo el puntero desde el frame anterior.
        private void ContinueCameraPan()
        {
            Vector2 screenPosition = Pointer.current.position.ReadValue();

            if (!TryGetGroundPoint(screenPosition, out Vector3 worldPoint))
            {
                return;
            }

            // Por debajo del umbral el gesto sigue siendo un toque, no un paneo.
            if (!hasPanEngaged)
            {
                if (Vector2.Distance(pointerDownPosition, screenPosition) <= tapThreshold)
                {
                    return;
                }

                hasPanEngaged = true;
                lastPanWorldPoint = worldPoint;

                return;
            }

            if (CameraSystem.TacticalCamera.Instance != null)
            {
                CameraSystem.TacticalCamera.Instance.AddPan(worldPoint - lastPanWorldPoint);
            }

            lastPanWorldPoint = worldPoint;
        }

        // Calcula dónde toca el plano del campo un punto de pantalla.
        private bool TryGetGroundPoint(Vector2 screenPosition, out Vector3 worldPoint)
        {
            Ray ray = mainCamera.ScreenPointToRay(screenPosition);

            if (GroundPlane.Raycast(ray, out float distance))
            {
                worldPoint = ray.GetPoint(distance);
                return true;
            }

            worldPoint = Vector3.zero;
            return false;
        }

        // Coloca al jugador arrastrado en el punto del campo indicado, durante la colocación previa al saque.
        private void PlacePlayerAt(Vector3 groundPoint)
        {
            if (selectedPlayerHandler == null || selectedPlayerHandler.HasBall)
            {
                return;
            }

            Transform playerTransform = selectedPlayerRoute.transform;

            Vector3 desired = new Vector3(
                groundPoint.x,
                playerTransform.position.y,
                groundPoint.z);

            TeamMember member = playerTransform.GetComponent<TeamMember>();

            playerTransform.position = member != null
                ? Core.PitchBounds.ClampKickoffPlacement(desired, member.team, member.isGoalkeeper)
                : Core.PitchBounds.ClampPlayer(desired);
        }

        // Confirma el arrastre en curso: cierra la ruta dibujada, o simplemente libera el gesto si era un paneo o colocación.
        private void EndDrag()
        {
            if (isPanningCamera)
            {
                ReleaseDrag();
                return;
            }

            if (isPlacingPlayer)
            {
                ReleaseDrag();
                return;
            }

            selectedPlayerRoute.EndRoute();
            Core.TacticalEvents.OnRouteDrawEnded?.Invoke();
            ReleaseDrag();
        }

        // Cancela un arrastre pendiente cuando el gesto termina siendo un toque simple.
        private void CancelPendingDrag()
        {
            if (!isDragging)
            {
                return;
            }

            if (!isPlacingPlayer && !isPanningCamera)
            {
                selectedPlayerRoute.CancelRoute();
                Core.TacticalEvents.OnRouteDrawEnded?.Invoke();
            }

            ReleaseDrag();
        }

        // Reinicia todo el estado relacionado con el gesto en curso.
        private void ReleaseDrag()
        {
            selectedPlayerRoute = null;
            selectedPlayerHandler = null;
            isPlacingPlayer = false;
            isPanningCamera = false;
            hasPanEngaged = false;
            isDragging = false;
        }

        // Resuelve un toque simple: chuta si apunta a la portería rival, o pasa el balón hacia ese punto.
        private void HandleTap()
        {
            CancelPendingDrag();

            Ray ray = mainCamera.ScreenPointToRay(Pointer.current.position.ReadValue());
            LayerMask tapMask = playerLayerMask | groundLayerMask | goalLayerMask;

            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, tapMask))
            {
                Debug.Log("[Tap] El raycast no golpeo ni 'Player', ni 'Ground', ni 'Goal'.");
                return;
            }

            Player.PlayerBallHandler carrier = ResolveCarrier();

            if (carrier == null)
            {
                Debug.Log("[Tap] Ningun jugador de tu equipo lleva el balon.");
                return;
            }

            carrier.TryGetComponent(out TeamMember member);

            bool towardsOwnGoal = member != null
                && Core.PitchBounds.IsNearOwnGoal(hit.point, member.team);

            if (hit.collider.CompareTag("Goal") && !towardsOwnGoal)
            {
                carrier.InitiateShot(hit.point);
                return;
            }

            if (!towardsOwnGoal)
            {
                carrier.PassTo(hit.point);
                return;
            }

            // Nunca se dispara hacia la propia portería: se juega como un pase, alejando el destino de la línea de gol.
            Vector3 safeTarget = Core.PitchBounds.PushOutOfOwnGoal(hit.point, member.team);

            Debug.Log($"[Tap] {carrier.name} apunta cerca de su propia portería: " +
                      $"se juega como PASE y el destino se retrasa a z={safeTarget.z:F1}.");

            carrier.PassTo(safeTarget);
        }
    }
}
