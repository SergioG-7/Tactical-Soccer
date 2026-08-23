using UnityEngine;

namespace TacticalSoccer.Gameplay
{
    public enum BallState
    {
        Free,
        Possessed
    }

    // Controla el balón: está libre (con físicas) o lo lleva un jugador enganchado a su socket.
    [RequireComponent(typeof(Rigidbody))]
    public class BallController : MonoBehaviour
    {
        [Tooltip("Estela visual de velocidad asignada al balón.")]
        [SerializeField] private TrailRenderer trail;

        [Tooltip("Velocidad mínima del balón para activar el renderizado de la estela.")]
        [SerializeField] private float trailSpeedThreshold = 8f;

        [Tooltip("Fracción de velocidad que conserva el balón al cruzar la línea de meta y entrar en la red.")]
        [SerializeField] private float netEntrySpeedScale = 0.35f;

        [Tooltip("Material para proyectar la sombra del balón en el césped.")]
        [SerializeField] private Material shadowMaterial;

        [Tooltip("Diámetro del círculo de sombra proyectado bajo el balón.")]
        [SerializeField] private float shadowSize = 0.6f;

        private const float MinKickHeight = 0.3f;

        // Just clear of the pitch plane at y=0, so the two never z-fight.
        private const float ShadowGroundY = 0.01f;

        private static readonly Vector3 KickoffPosition = new Vector3(0f, 0.5f, 0f);

        private BallState currentState = BallState.Free;
        private Transform currentOwnerSocket;
        private Rigidbody rb;

        // Sombra proyectada bajo el balón; no es hija suya para que no gire con él.
        private GameObject dropShadow;

        // Marca que el balón está fuera del campo hasta que vuelve a entrar dentro de las líneas.
        private bool isOutOfPlay;

        // Cierto mientras nadie tiene el balón, es decir, se puede recoger.
        public bool IsFree => currentState == BallState.Free;

        // Cierto mientras un jugador lleva el balón enganchado.
        public bool IsHeld => currentState == BallState.Possessed;

        // Quién dio la última patada o perdió el balón por última vez.
        public GameObject LastHolder { get; private set; }

        // Quién tiene el balón ahora mismo, o null si está suelto.
        public GameObject Holder =>
            currentState == BallState.Possessed && currentOwnerSocket != null && currentOwnerSocket.parent != null
                ? currentOwnerSocket.parent.gameObject
                : null;

        // Velocidad actual del balón.
        public Vector3 Velocity => rb != null ? rb.linearVelocity : Vector3.zero;

        // Instancia única del balón en la escena.
        public static BallController Instance { get; private set; }

        // Inicializa el Rigidbody, el trail y la sombra del balón.
        private void Awake()
        {
            Instance = this;

            rb = GetComponent<Rigidbody>();

            if (trail == null)
            {
                trail = GetComponent<TrailRenderer>();
            }

            if (trail != null)
            {
                trail.emitting = false;
            }

            CreateDropShadow();
        }

        // Limpia la instancia al desactivarse.
        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Destruye la sombra al destruirse el balón, ya que es un objeto independiente.
        private void OnDestroy()
        {
            if (dropShadow != null)
            {
                Destroy(dropShadow);
                dropShadow = null;
            }
        }

        // Asigna el TrailRenderer que dibuja la estela del balón.
        public void AssignTrail(TrailRenderer trailRenderer)
        {
            trail = trailRenderer;
        }

        // Asigna el material de la sombra del balón.
        public void ConfigureShadowMaterial(Material material)
        {
            shadowMaterial = material;
        }

        // Crea el quad que hace de sombra bajo el balón.
        private void CreateDropShadow()
        {
            dropShadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            dropShadow.name = "Ball Drop Shadow";

            // Sin collider: si no, el balón chocaría con su propia sombra.
            Collider quadCollider = dropShadow.GetComponent<Collider>();

            if (quadCollider != null)
            {
                Destroy(quadCollider);
            }

            dropShadow.transform.localScale = new Vector3(shadowSize, shadowSize, shadowSize);

            MeshRenderer shadowRenderer = dropShadow.GetComponent<MeshRenderer>();

            if (shadowRenderer != null)
            {
                shadowRenderer.sharedMaterial = shadowMaterial != null
                    ? shadowMaterial
                    : BuildFallbackShadowMaterial();

                shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shadowRenderer.receiveShadows = false;
            }

            UpdateDropShadow();
        }

        // Crea un material transparente para la sombra si no se ha asignado ninguno.
        private static Material BuildFallbackShadowMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            Material material = new Material(shader)
            {
                name = "BallShadowMaterial (runtime)",
                color = new Color(0f, 0f, 0f, 0.5f)
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

        // Mueve la sombra a la posición del balón proyectada en el suelo, plana.
        private void UpdateDropShadow()
        {
            if (dropShadow == null)
            {
                return;
            }

            dropShadow.transform.position = new Vector3(
                transform.position.x, ShadowGroundY, transform.position.z);

            dropShadow.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        // Engancha el balón al socket de un jugador y lo vuelve kinemático.
        public void AttachToPlayer(Transform socket)
        {
            currentState = BallState.Possessed;
            currentOwnerSocket = socket;
            rb.isKinematic = true;
        }

        // Suelta el balón, quedando libre y sujeto a físicas de nuevo.
        public void Release()
        {
            if (currentOwnerSocket != null && currentOwnerSocket.parent != null)
            {
                LastHolder = currentOwnerSocket.parent.gameObject;
            }

            currentState = BallState.Free;
            currentOwnerSocket = null;
            rb.isKinematic = false;
        }

        // Reduce la velocidad del balón al entrar en la red, para que se frene en vez de atravesarla.
        public void DampenIntoNet()
        {
            if (rb == null || rb.isKinematic)
            {
                return;
            }

            rb.linearVelocity *= netEntrySpeedScale;
            rb.angularVelocity *= netEntrySpeedScale;

            if (trail != null)
            {
                trail.emitting = false;
            }
        }

        // Suelta el balón y le aplica un impulso en la dirección indicada.
        public void Kick(Vector3 forceDirection, float forceMagnitude)
        {
            Release();

            transform.position = new Vector3(
                transform.position.x,
                Mathf.Max(transform.position.y, MinKickHeight),
                transform.position.z);

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            rb.AddForce(forceDirection.normalized * forceMagnitude, ForceMode.Impulse);

            if (Audio.AudioManager.Instance != null)
            {
                Audio.AudioManager.Instance.PlayKick();
            }
        }

        // Devuelve el balón al centro del campo, parado y libre.
        public void ResetToKickoff()
        {
            Release();

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            transform.position = KickoffPosition;

            isOutOfPlay = false;

            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }

            Core.TacticalEvents.OnMatchReset?.Invoke();
        }

        // Actualiza la estela y comprueba si el balón ha salido del campo.
        private void Update()
        {
            UpdateTrail();

            if (Core.MatchManager.IsGoalBeingCelebrated)
            {
                return;
            }

            bool inPlay = Core.PitchBounds.IsBallInPlay(transform.position);

            if (isOutOfPlay)
            {
                if (inPlay)
                {
                    isOutOfPlay = false;
                }

                return;
            }

            if (inPlay)
            {
                return;
            }

            isOutOfPlay = true;

            HandleOutOfPlay();
        }

        // Decide qué reinicio corresponde según por dónde ha salido el balón: saque de banda, córner o saque de puerta.
        private void HandleOutOfPlay()
        {
            Vector3 exitPoint = transform.position;

            bool aboveFloor = exitPoint.y > Core.PitchBounds.FallThroughFloorY;

            bool overGoalLine = aboveFloor
                && Mathf.Abs(exitPoint.z) > Core.PitchBounds.GoalLineZ
                && Mathf.Abs(exitPoint.x) > Core.PitchBounds.GoalMouthHalfWidth;

            bool overTouchline = aboveFloor
                && !overGoalLine
                && Mathf.Abs(exitPoint.x) > Core.PitchBounds.SideLineX;

            Release();
            GameObject holder = LastHolder;

            if (holder != null && holder.TryGetComponent(out Player.PlayerBallHandler holderHandler))
            {
                holderHandler.ForceDropBall();
            }

            bool knownToucher = false;
            TeamId lastTeam = TeamId.Blue;

            if (holder != null && holder.TryGetComponent(out TeamMember lastToucher))
            {
                knownToucher = true;
                lastTeam = lastToucher.team;
            }

            if (Core.MatchManager.Instance != null && knownToucher && (overTouchline || overGoalLine))
            {
                StopDead();

                // Exactly one restart, always. The two flags are already
                // mutually exclusive — the goal-line test wins the corners and
                // the touchline gets what is left — and the if/else is what
                // keeps them that way if either condition is ever loosened.
                if (overTouchline)
                {
                    Core.MatchManager.Instance.StartThrowIn(Opponent(lastTeam), exitPoint);
                }
                else if (overGoalLine)
                {
                    // Whose goal line it went over. Blue defends negative Z, Red
                    // defends positive Z, so the sign of the exit point names the
                    // defending side outright.
                    TeamId defendingSide = exitPoint.z > 0f ? TeamId.Red : TeamId.Blue;

                    // A defender putting it behind his own line concedes a
                    // corner; an attacker putting it behind theirs gives the
                    // defenders a goal kick.
                    bool putBehindByDefenders = lastTeam == defendingSide;

                    if (putBehindByDefenders)
                    {
                        Core.MatchManager.Instance.StartCorner(Opponent(defendingSide), exitPoint);
                    }
                    else
                    {
                        Core.MatchManager.Instance.StartGoalKick(defendingSide, exitPoint);
                    }
                }

                Core.TacticalEvents.OnBallOutOfBounds?.Invoke();

                return;
            }

            ResetToKickoff();

            Core.TacticalEvents.OnBallOutOfBounds?.Invoke();
        }

        // Detiene por completo el balón y borra su estela.
        private void StopDead()
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }
        }

        // Devuelve el equipo contrario.
        private static TeamId Opponent(TeamId team)
        {
            return team == TeamId.Blue ? TeamId.Red : TeamId.Blue;
        }

        // Activa la estela solo si el balón está libre y va rápido.
        private void UpdateTrail()
        {
            if (trail == null)
            {
                return;
            }

            trail.emitting = currentState == BallState.Free
                && rb.linearVelocity.magnitude > trailSpeedThreshold;
        }

        // Engancha el balón al socket del jugador que lo lleva y actualiza la sombra.
        private void LateUpdate()
        {
            if (currentState == BallState.Possessed && currentOwnerSocket != null)
            {
                transform.position = currentOwnerSocket.position;
            }

            UpdateDropShadow();
        }

    }
}
