using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TacticalSoccer.Gameplay;

namespace TacticalSoccer.Input
{
    /// <summary>
    /// Detects touch/click input, resolves it against players and the field,
    /// and forwards the resulting drag events to the targeted PlayerRoute.
    /// This class owns no gameplay state itself; it only routes input.
    ///
    /// A drag means two different things depending on the phase. During normal
    /// play it draws a route the player then runs. During a kickoff it places
    /// the player outright — you are setting a formation, not ordering a run,
    /// and making somebody jog into position while the clock is stopped would
    /// be busywork.
    /// </summary>
    public class TacticalInputManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera mainCamera;

        [Header("Raycast Layers")]
        [SerializeField] private LayerMask playerLayerMask;
        [SerializeField] private LayerMask groundLayerMask;

        [Tooltip("Kept off the ground mask on purpose: the goal box is 2.5 units " +
                 "tall, so drawn route points would snap to its roof.")]
        [SerializeField] private LayerMask goalLayerMask;

        [Header("Gesture")]
        [SerializeField] private float tapThreshold = 50f;
        [SerializeField] private float tapMaxDuration = 0.3f;
        [SerializeField] private float maxRayDistance = 100f;

        [Header("Marcador de selección")]
        [Tooltip("Colour of the ring under the player being commanded.")]
        [SerializeField] private Color selectionRingColor = new Color(0f, 1f, 0f, 0.5f);

        [SerializeField] private float selectionRingDiameter = 1.5f;

        [Tooltip("Height of the disc off the turf. Just enough to clear the " +
                 "pitch plane without z-fighting it.")]
        [SerializeField] private float selectionRingGroundY = 0.05f;

        private const TeamId HumanTeam = TeamId.Blue;

        private readonly List<Player.PlayerBallHandler> humanSquad = new List<Player.PlayerBallHandler>();

        private Player.PlayerRoute selectedPlayerRoute;
        private Player.PlayerBallHandler selectedPlayerHandler;

        /// <summary>
        /// True when the drag under way is placing a player rather than drawing
        /// a route. Latched at the start of the gesture so the kickoff ending
        /// mid-drag cannot switch modes half way through it.
        /// </summary>
        private bool isPlacingPlayer;

        /// <summary>
        /// True when the drag started on empty grass and is therefore moving the
        /// view rather than commanding anybody.
        /// </summary>
        private bool isPanningCamera;

        /// <summary>Where the pointer last met the pitch plane, in world space.</summary>
        private Vector3 lastPanWorldPoint;

        /// <summary>
        /// False until the gesture has travelled far enough to stop being a tap.
        /// Without it every tap would nudge the camera by its own few pixels of
        /// jitter before passing the ball.
        /// </summary>
        private bool hasPanEngaged;

        /// <summary>
        /// The pitch surface as pure maths. Used instead of a ground raycast so
        /// the pan keeps working when the pointer leaves the turf — which is
        /// exactly where a drag towards the touchline ends up.
        /// </summary>
        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        private float pointerDownTime;
        private Vector2 pointerDownPosition;
        private bool isDragging;

        /// <summary>True while two fingers are on the screen driving the zoom.</summary>
        private bool isPinching;

        /// <summary>Distance between the two fingers last frame, in screen pixels.</summary>
        private float lastPinchDistance;

        /// <summary>
        /// Set while a pinch is being unwound. Lifting one finger of a pinch
        /// leaves the other one down, and without this the frame after would be
        /// read as the START of an ordinary drag from wherever that finger
        /// happened to be — which is exactly the jump this flag exists to
        /// prevent. Input stays swallowed until the screen is clear.
        /// </summary>
        private bool isUnwindingPinch;

        /// <summary>
        /// The disc on the grass marking who the next order goes to. Built from
        /// a primitive rather than a sprite so it lies in the world and is
        /// occluded by the players standing over it, which is what makes it read
        /// as paint on the pitch instead of an overlay.
        /// </summary>
        private GameObject selectionRing;

        private void Awake()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }
        }

        private void Start()
        {
            CacheHumanSquad();
            CreateSelectionRing();
        }

        private void OnDestroy()
        {
            // An independent root object: nothing else would ever clean it up,
            // and it would simply be left lying on the grass.
            if (selectionRing != null)
            {
                Destroy(selectionRing);
                selectionRing = null;
            }
        }

        private void CreateSelectionRing()
        {
            selectionRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            selectionRing.name = "Selection Ring";

            // A collider here would be an invisible bollard on the pitch: route
            // raycasts would hit it, and so would the ball.
            Collider ringCollider = selectionRing.GetComponent<Collider>();

            if (ringCollider != null)
            {
                Destroy(ringCollider);
            }

            // A Unity cylinder is 2 units tall at scale 1, so the Y scale is a
            // half-height: 0.05 gives a disc a tenth of a unit thick.
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

        /// <summary>
        /// URP ships its shaders opaque, so the alpha on the colour is ignored
        /// unless the material is explicitly flipped to alpha blending — without
        /// this the marker comes out as a solid green plate under the player.
        /// If the pipeline shader cannot be found at all, the ring falls back to
        /// opaque, which is ugly but still tells you who is selected.
        /// </summary>
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

        /// <summary>
        /// Who the marker belongs to right now: the player being dragged if a
        /// gesture is under way, and otherwise whoever on your side is carrying
        /// the ball.
        ///
        /// The carrier half is the useful one. A tap always acts through the
        /// carrier — that is the whole input model — so without it the marker
        /// would only ever appear mid-drag, under the finger already drawing the
        /// line, and answer a question nobody was asking.
        /// </summary>
        private Transform ResolveMarkedPlayer()
        {
            if (isDragging && selectedPlayerRoute != null)
            {
                return selectedPlayerRoute.transform;
            }

            Player.PlayerBallHandler carrier = ResolveCarrier();

            return carrier != null ? carrier.transform : null;
        }

        /// <summary>
        /// Parks the marker on the ground under whoever is selected. Late, not
        /// in Update: the player may have been moved this frame by a route, a
        /// restart or a substitution, and a marker written first would trail a
        /// frame behind the man it belongs to.
        /// </summary>
        private void LateUpdate()
        {
            if (selectionRing == null)
            {
                return;
            }

            Transform marked = ResolveMarkedPlayer();

            // Nothing to mark, or nothing to mark it during: the title screen,
            // the interval and full time are all menus, not play.
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

            // On the TURF, not at the player's origin. A capsule's transform
            // sits at its centre, a unit above the grass, so following the
            // position directly would hang the disc at chest height.
            Vector3 at = marked.position;

            selectionRing.transform.position = new Vector3(at.x, selectionRingGroundY, at.z);
        }

        public void ConfigureLayers(LayerMask playerMask, LayerMask groundMask, LayerMask goalMask)
        {
            playerLayerMask = playerMask;
            groundLayerMask = groundMask;
            goalLayerMask = goalMask;
        }

        private void Update()
        {
            // Update is not gated by timeScale, so without this the player could
            // draw a route during a frozen duel — and TimeController would then
            // set timeScale to 0.1, thawing the clash through the back door.
            if (ClashManager.IsClashActive)
            {
                return;
            }

            // Same reasoning once the whistle has gone: the match is frozen at
            // timeScale 0 for good, and drawing a route would hand TimeController
            // an excuse to set 0.1 and restart a finished match.
            if (!Core.MatchManager.IsPlayable)
            {
                return;
            }

            // Same reasoning before the whistle: the title screen freezes the
            // match at timeScale 0, and a route drawn behind it would have
            // TimeController set 0.1 and start the game without the button.
            if (!Core.MatchManager.IsStarted)
            {
                return;
            }

            // And again behind the interval and the substitutions board, which
            // freeze the match exactly as those do: a route drawn through either
            // would have TimeController set 0.1 and run the game on under the
            // menu.
            if (Core.MatchManager.IsHalftime || UI.SubstitutionUIController.IsOpen)
            {
                return;
            }

            // And behind the penalty and the developer menu, for the same reason
            // again. Every modal screen in this game freezes the pitch with
            // timeScale, and timeScale does not govern input.
            if (UI.PenaltyUIController.IsOpen || UI.DebugMenuUIController.IsOpen
                || UI.AudioSettingsUI.IsOpen || UI.PlayerEditUIController.IsOpen)
            {
                return;
            }

            // Two fingers own the frame. Checked BEFORE the pointer, because
            // Pointer.current still reports a press while pinching — the drag
            // logic below would happily keep drawing a route with one hand while
            // the other zoomed.
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

                // Unscaled on purpose: drawing a route drops timeScale to 0.1,
                // so Time.time would stretch this window to ~3 real seconds.
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

        /// <summary>
        /// The human roster is fixed for a match, so it is resolved once rather
        /// than scanned on every tap.
        /// </summary>
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

        /// <summary>
        /// Whoever on the human side is actually holding the ball right now.
        ///
        /// This replaces the old "last tapped player" memory: that made you
        /// select the carrier before every single pass, and silently did nothing
        /// if you had last tapped anybody else.
        /// </summary>
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

        private static bool IsWaitingForKickoff()
        {
            return Core.MatchManager.Instance != null && Core.MatchManager.Instance.isWaitingForKickoff;
        }

        private static bool IsAwaitingRestart()
        {
            return Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece;
        }

        private void TryBeginDrag()
        {
            Ray ray = mainCamera.ScreenPointToRay(Pointer.current.position.ReadValue());

            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, playerLayerMask))
            {
                // Nothing to command under the finger, so the gesture is about
                // the view rather than about a player.
                BeginCameraPan();
                return;
            }

            if (!hit.collider.TryGetComponent(out Player.PlayerRoute playerRoute))
            {
                BeginCameraPan();
                return;
            }

            // You command your own side and nobody else's. The player layer does
            // not distinguish teams, so without this the human could draw routes
            // for the opposition — and, during a kickoff, drag their whole shape
            // out of the way before taking it.
            TeamMember member = hit.collider.GetComponent<TeamMember>();

            // A substitute is not orderable either. He is a real collider sitting
            // in the dugout, so without this you could draw a run for a man on
            // the bench and walk him onto the pitch without a substitution.
            if (member == null || member.team != HumanTeam || !member.isStarter)
            {
                selectedPlayerRoute = null;
                selectedPlayerHandler = null;

                // You cannot order the opposition about, but you can still drag
                // the view: nothing was selected, so the gesture is a pan.
                BeginCameraPan();
                return;
            }

            hit.collider.TryGetComponent(out Player.PlayerBallHandler handler);

            // The player holding the ball at a restart is taking it. Letting you
            // draw them a run would carry the ball off the centre mark, or off
            // the touchline the throw has to be taken from.
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
                // No route is being drawn, so no route visuals and no slow
                // motion: dropping timeScale here would only stretch the clock
                // out while the formation is being set.
                selectedPlayerRoute.CancelRoute();
                return;
            }

            selectedPlayerRoute.BeginRoute();
            Core.TacticalEvents.OnRouteDrawStarted?.Invoke();
        }

        /// <summary>
        /// Reads a two-finger pinch and turns it into camera zoom.
        /// </summary>
        /// <returns>
        /// True while the touch screen is being used for something other than a
        /// single-pointer gesture, and the rest of Update must keep its hands
        /// off: during the pinch itself, and afterwards until every finger has
        /// been lifted.
        /// </returns>
        private bool UpdatePinch()
        {
            Touchscreen screen = Touchscreen.current;

            if (screen == null)
            {
                // No touch screen at all — a desktop build, or the editor
                // without device simulation. Nothing to do, and nothing to
                // block.
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
                    // The first frame only ADOPTS the distance. Zooming on it
                    // would mean measuring against a distance of zero and
                    // snapping the camera the whole way out on contact.
                    isPinching = true;

                    // Whatever one finger had begun is abandoned: the player is
                    // reaching for a gesture, not ordering a run.
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

            // One finger of the pinch is still down. Keep swallowing until the
            // screen is clear, so the leftover finger cannot be read as the
            // beginning of a drag.
            if (pressed > 0)
            {
                return true;
            }

            isUnwindingPinch = false;

            return false;
        }

        /// <summary>
        /// Drops whatever the finger was in the middle of, from outside.
        ///
        /// Called when the whistle goes. Without it a route being drawn AT that
        /// moment keeps taking points — the pointer is still down, and this
        /// manager has no idea a foul has just been given — so the line the
        /// referee was supposed to have cut carries on growing under the
        /// player's thumb.
        /// </summary>
        public void CancelActiveGesture()
        {
            AbortGesture();
        }

        /// <summary>
        /// Throws away the gesture in progress without committing it.
        ///
        /// Not EndDrag: that one COMMITS — it closes the route and sends the
        /// player running it. A drag interrupted by a second finger was never an
        /// order, so the route is cancelled instead. The draw-ended event still
        /// has to be raised if the matching started event was, or the time
        /// controller would leave the match in slow motion for good.
        /// </summary>
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

        /// <summary>
        /// Arms a camera pan. No route is begun and no slow motion is triggered:
        /// moving the view is not a tactical order, and charging the player time
        /// for looking around would be a strange thing to do.
        /// </summary>
        private void BeginCameraPan()
        {
            selectedPlayerRoute = null;
            selectedPlayerHandler = null;

            isPanningCamera = true;
            isPlacingPlayer = false;
            isDragging = true;
            hasPanEngaged = false;
        }

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

        /// <summary>
        /// Drags the view by however far the ground under the pointer has moved
        /// since the last frame.
        ///
        /// The world point is sampled fresh every frame against the live camera,
        /// so the pitch tracks the finger instead of sliding away from it as the
        /// view moves — the same reason a tap has to be resolved at the instant
        /// it happens rather than from a position cached earlier.
        /// </summary>
        private void ContinueCameraPan()
        {
            Vector2 screenPosition = Pointer.current.position.ReadValue();

            if (!TryGetGroundPoint(screenPosition, out Vector3 worldPoint))
            {
                return;
            }

            // Below the tap threshold the gesture is still a tap. Panning here
            // would drag the view by the few pixels of wobble in every tap.
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

        /// <summary>
        /// Where a screen point meets the pitch surface. A maths plane rather
        /// than a collider raycast: a drag heading for the touchline runs off
        /// the turf long before it finishes, and a pan that died there would
        /// feel broken exactly when it was most needed.
        /// </summary>
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

        /// <summary>
        /// Teleports the dragged player onto the pointer during a kickoff.
        ///
        /// The kickoff taker is deliberately immovable: the ball is glued to
        /// their socket, so dragging them would drag the ball off the centre
        /// spot with them. The team check is not repeated here — TryBeginDrag
        /// already refuses to select anybody but Blue.
        ///
        /// The drop point is clamped to your own half, and a keeper to his own
        /// goal: setting up a kickoff is arranging your shape, not walking your
        /// forwards into the opposition box before the whistle.
        /// </summary>
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

        private void EndDrag()
        {
            if (isPanningCamera)
            {
                // The pan has already been applied frame by frame; there is
                // nothing to commit and no route to close.
                ReleaseDrag();
                return;
            }

            if (isPlacingPlayer)
            {
                // Nothing to commit: the player is already standing where they
                // were dropped, and no route was ever started.
                ReleaseDrag();
                return;
            }

            selectedPlayerRoute.EndRoute();
            Core.TacticalEvents.OnRouteDrawEnded?.Invoke();
            ReleaseDrag();
        }

        private void CancelPendingDrag()
        {
            if (!isDragging)
            {
                return;
            }

            // A pan draws no route, so there is nothing to cancel and no slow
            // motion to lift.
            if (!isPlacingPlayer && !isPanningCamera)
            {
                selectedPlayerRoute.CancelRoute();
                Core.TacticalEvents.OnRouteDrawEnded?.Invoke();
            }

            ReleaseDrag();
        }

        private void ReleaseDrag()
        {
            selectedPlayerRoute = null;
            selectedPlayerHandler = null;
            isPlacingPlayer = false;
            isPanningCamera = false;
            hasPanEngaged = false;
            isDragging = false;
        }

        /// <summary>
        /// A tap always acts through whoever currently has the ball: tapping the
        /// goal shoots, tapping anywhere else passes towards that point. No
        /// selection step, because there is only ever one carrier to act with.
        /// </summary>
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

            // Whose side of the pitch the finger landed on decides everything
            // below, so it is resolved once from the carrier rather than from
            // whichever collider the ray happened to meet.
            carrier.TryGetComponent(out TeamMember member);

            bool towardsOwnGoal = member != null
                && Core.PitchBounds.IsNearOwnGoal(hit.point, member.team);

            if (hit.collider.CompareTag("Goal") && !towardsOwnGoal)
            {
                // From close in this opens the shot duel and the ball only flies
                // if the shooter wins; from distance it is struck straight away.
                carrier.InitiateShot(hit.point);
                return;
            }

            if (!towardsOwnGoal)
            {
                carrier.PassTo(hit.point);
                return;
            }

            // Aimed at their own net. Never a shot — whatever the role, and
            // whichever collider was hit — and the destination is pulled out in
            // front of the line as well.
            //
            // Refusing to SHOOT was not enough on its own: a pass carries real
            // force, so a keeper playing the ball "back" from his six-yard box
            // simply passed it into his own goal instead of shooting it there.
            // The tap is honoured as a ball played in that direction, stopping
            // short of the one place it must not end up.
            Vector3 safeTarget = Core.PitchBounds.PushOutOfOwnGoal(hit.point, member.team);

            Debug.Log($"[Tap] {carrier.name} apunta cerca de su propia portería: " +
                      $"se juega como PASE y el destino se retrasa a z={safeTarget.z:F1}.");

            carrier.PassTo(safeTarget);
        }
    }
}
