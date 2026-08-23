using UnityEngine;

namespace TacticalSoccer.Player
{
    // Gestiona la posesión del balón de un jugador: recogerlo, pasarlo, chutarlo y disputar duelos.
    [RequireComponent(typeof(Collider))]
    public class PlayerBallHandler : MonoBehaviour
    {
        [SerializeField] private Transform ballSocket;

        [Tooltip("Impulse applied to a pass. Tuned so a pass actually reaches a " +
                 "team-mate across the middle third rather than dying short.")]
        [SerializeField] private float passForce = 12f;

        [Header("Tiro Directo")]
        [SerializeField] private float powerShotForce = 25f;
        [SerializeField] private float powerShotLift = 0.1f;

        [Header("Vaselina")]
        [Tooltip("Softer and higher than a drive: it has to drop back down " +
                 "under the crossbar rather than sail over it.")]
        [SerializeField] private float lobShotForce = 15f;
        [SerializeField] private float lobShotLift = 0.45f;

        [Header("Alcance de Duelo")]
        [Tooltip("Flat distance to the target beyond which a shot is a hopeful " +
                 "long-range hit rather than a one-on-one, so it skips the duel " +
                 "and simply flies.")]
        [SerializeField] private float maxDuelShotDistance = 15f;

        [Header("Intercepción")]
        [Tooltip("Speed above which a loose ball counts as a pass in flight " +
                 "rather than something rolling around to be picked up. Below " +
                 "it, stepping on the ball is simply collecting it.")]
        [SerializeField] private float interceptSpeedThreshold = 5f;

        [SerializeField] private float pickupCooldown = 0.2f;

        [Tooltip("Extra time the last kicker alone must wait before collecting " +
                 "the ball again, so a rebound is a real rebound.")]
        [SerializeField] private float selfReboundImmunity = 1f;

        // Usado solo si no hay portero rival contra el que disputar el duelo.
        private const float FallbackGoalZ = 24.5f;

        // Mínimo del multiplicador de fuerza, para que el balón nunca se quede parado en el chut.
        private const float MinimumForceScale = 0.1f;

        private Gameplay.BallController currentBall;
        private Gameplay.TeamMember myTeamMember;
        private PlayerRoute myRoute;
        private Gameplay.TeamMember enemyGoalkeeper;
        private float lastPassTime = -1f;

        // Si este jugador tiene el balón en este momento.
        public bool HasBall => currentBall != null;

        // False mientras el jugador es un suplente esperando en el banquillo.
        public bool IsOnPitch => myTeamMember == null || myTeamMember.isStarter;

        // Desplazamiento del balón respecto a este jugador, usado para colocar los saques.
        public Vector3 BallOffset =>
            ballSocket != null ? ballSocket.position - transform.position : Vector3.zero;

        // Guarda las referencias a los componentes propios.
        private void Awake()
        {
            myTeamMember = GetComponent<Gameplay.TeamMember>();
            myRoute = GetComponent<PlayerRoute>();
        }

        // Se suscribe al reinicio del partido para soltar el balón.
        private void OnEnable()
        {
            Core.TacticalEvents.OnMatchReset += ForceDropBall;
        }

        // Se desuscribe del reinicio del partido.
        private void OnDisable()
        {
            Core.TacticalEvents.OnMatchReset -= ForceDropBall;
        }

        // Asigna el socket donde se engancha el balón.
        public void AssignBallSocket(Transform socket)
        {
            ballSocket = socket;
        }

        // Quita la posesión del balón a este jugador sin tocar el balón físico.
        public void ForceDropBall()
        {
            currentBall = null;
        }

        // Pone el balón en el pie de este jugador sin pasar por contacto, cooldowns ni inmunidad de rebote.
        public void ForceTakeBall(Gameplay.BallController ball)
        {
            if (ball == null || ballSocket == null)
            {
                return;
            }

            // Se le quita la posesión a quien la tuviera antes, para que no siga reportando HasBall == true.
            Gameplay.TeamMember previous = ball.Holder != null
                ? ball.Holder.GetComponent<Gameplay.TeamMember>()
                : null;

            if (ball.Holder != null && ball.Holder != gameObject)
            {
                if (ball.Holder.TryGetComponent(out PlayerBallHandler previousHandler))
                {
                    previousHandler.ForceDropBall();
                }

                // Se cancela también la ruta que tuviera en marcha, ya no tiene sentido con el balón perdido.
                if (ball.Holder.TryGetComponent(out PlayerRoute previousRoute))
                {
                    previousRoute.CancelRoute();
                }

                if (previous != null)
                {
                    Debug.Log($"Posesión retirada a {previous.name} ({previous.team}) " +
                              $"y entregada a {name} ({(myTeamMember != null ? myTeamMember.team.ToString() : "?")}).");
                }
            }

            ball.AttachToPlayer(ballSocket);
            currentBall = ball;
        }

        // Pasa el balón hacia la posición indicada.
        public void PassTo(Vector3 targetPosition)
        {
            if (currentBall == null)
            {
                return;
            }

            StartPlayIfWaitingForKickoff();

            // Se apunta desde el balón, no desde el jugador, porque el balón va en un socket con offset.
            Vector3 direction = targetPosition - currentBall.transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            currentBall.Kick(direction.normalized, passForce);
            currentBall = null;
            StartPickupCooldown();
        }

        // Dispara hacia la posición indicada: desde cerca abre un duelo contra el portero, desde lejos golpea directamente.
        public void InitiateShot(Vector3 targetPosition)
        {
            if (currentBall == null || myTeamMember == null)
            {
                return;
            }

            StartPlayIfWaitingForKickoff();

            if (Core.MatchManager.Instance != null)
            {
                Core.MatchManager.Instance.RecordShot(myTeamMember.team);
            }

            Vector3 toTarget = targetPosition - transform.position;
            toTarget.y = 0f;

            if (toTarget.magnitude > maxDuelShotDistance)
            {
                Debug.Log($"Tiro lejano ({toTarget.magnitude:F1} u > {maxDuelShotDistance} u): sin duelo.");
                ExecutePhysicalKick(Gameplay.ClashAction.PowerShot, targetPosition);
                return;
            }

            Gameplay.TeamMember keeper = ResolveEnemyGoalkeeper();

            if (keeper == null)
            {
                Debug.LogWarning("No se encontró portero rival. Se ejecuta el tiro sin duelo.");
                ExecutePhysicalKick(Gameplay.ClashAction.PowerShot, CalculateFallbackAim());
                return;
            }

            Core.TacticalEvents.OnShotInitiated?.Invoke(myTeamMember, keeper);
        }

        // Golpea físicamente el balón según el tipo de tiro, con un multiplicador de fuerza opcional (usado en las paradas).
        public void ExecutePhysicalKick(Gameplay.ClashAction shotType, Vector3 goalPosition,
            float forceScale = 1f)
        {
            if (currentBall == null)
            {
                return;
            }

            Vector3 direction = goalPosition - currentBall.transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            bool isLob = shotType == Gameplay.ClashAction.LobShot;

            direction = direction.normalized;
            direction.y = isLob ? lobShotLift : powerShotLift;

            float force = (isLob ? lobShotForce : powerShotForce) * Mathf.Max(MinimumForceScale, forceScale);

            currentBall.Kick(direction, force);
            currentBall = null;
            StartPickupCooldown();
        }

        // Reanuda el partido si estaba esperando un saque, al ejecutarse un pase o un tiro.
        private void StartPlayIfWaitingForKickoff()
        {
            if (Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece)
            {
                Core.MatchManager.Instance.EndKickoff();
            }
        }

        // Busca y cachea al portero rival.
        private Gameplay.TeamMember ResolveEnemyGoalkeeper()
        {
            if (enemyGoalkeeper != null)
            {
                return enemyGoalkeeper;
            }

            foreach (Gameplay.TeamMember member in FindObjectsByType<Gameplay.TeamMember>())
            {
                if (member.team != myTeamMember.team && member.isGoalkeeper)
                {
                    enemyGoalkeeper = member;
                    break;
                }
            }

            return enemyGoalkeeper;
        }

        // Calcula un punto de apuntado a la portería rival cuando no hay portero al que apuntar.
        private Vector3 CalculateFallbackAim()
        {
            float side = myTeamMember.team == Gameplay.TeamId.Blue ? 1f : -1f;

            return new Vector3(0f, 0.5f, side * FallbackGoalZ);
        }

        // Detecta contacto con el balón o con un jugador rival al entrar en su trigger.
        private void OnTriggerEnter(Collider other)
        {
            if (!CanContestBall())
            {
                return;
            }

            if (other.CompareTag("Ball"))
            {
                TryPickUpLooseBall(other);
                return;
            }

            if (other.CompareTag("Player"))
            {
                TryInitiateClash(other);
            }
        }

        // Repite la comprobación de contacto mientras dos colliders siguen solapados, por ejemplo tras un cooldown.
        private void OnTriggerStay(Collider other)
        {
            if (!CanContestBall())
            {
                return;
            }

            if (other.CompareTag("Ball"))
            {
                TryPickUpLooseBall(other);
                return;
            }

            if (other.CompareTag("Player"))
            {
                TryInitiateClash(other);
            }
        }

        // Indica si este jugador puede disputar el balón ahora mismo: en el campo, sin duelo activo, sin saque pendiente y sin cooldown.
        private bool CanContestBall()
        {
            if (!IsOnPitch)
            {
                return false;
            }

            if (Gameplay.ClashManager.IsClashActive)
            {
                return false;
            }

            if (Core.MatchManager.Instance != null && Core.MatchManager.Instance.IsWaitingForSetPiece)
            {
                return false;
            }

            if (IsPickupOnCooldown())
            {
                return false;
            }

            return myRoute == null || !myRoute.IsStunned;
        }

        // Recoge un balón suelto, comprobando antes si se trata de una intercepción y si cuenta como pase completado.
        private void TryPickUpLooseBall(Collider ballCollider)
        {
            if (!ballCollider.TryGetComponent(out Gameplay.BallController ball))
            {
                return;
            }

            if (!ball.IsFree)
            {
                return;
            }

            // Inmunidad de rebote: quien acaba de chutar no puede recuperar el balón inmediatamente por su propio disparo.
            if (ball.LastHolder == gameObject && Time.time - lastPassTime < selfReboundImmunity)
            {
                return;
            }

            if (TryInitiateIntercept(ball))
            {
                return;
            }

            // Se considera pase completado solo si el balón viene de un compañero, no de un rival o de un rebote propio.
            bool completedPass = ball.LastHolder != null
                && ball.LastHolder != gameObject
                && myTeamMember != null
                && ball.LastHolder.TryGetComponent(out Gameplay.TeamMember passer)
                && passer.team == myTeamMember.team;

            ball.AttachToPlayer(ballSocket);
            currentBall = ball;

            if (!completedPass)
            {
                return;
            }

            if (Gameplay.TensionManager.Instance != null)
            {
                Gameplay.TensionManager.Instance.AddPassCompleted(myTeamMember.team);
            }

            if (Core.MatchManager.Instance != null)
            {
                Core.MatchManager.Instance.RecordPass(myTeamMember.team);
            }
        }

        // Intenta interceptar en tiempo real un pase rival en el que este jugador se ha cruzado.
        private bool TryInitiateIntercept(Gameplay.BallController ball)
        {
            if (myTeamMember == null || Gameplay.ClashManager.Instance == null)
            {
                return false;
            }

            // Un portero que ya ha afrontado un tiro no vuelve a interceptarlo aquí.
            if (myTeamMember.isGoalkeeper)
            {
                return false;
            }

            if (!ball.TryGetComponent(out Rigidbody ballBody)
                || ballBody.linearVelocity.magnitude <= interceptSpeedThreshold)
            {
                return false;
            }

            GameObject holder = ball.LastHolder;

            if (holder == null || holder == gameObject)
            {
                return false;
            }

            if (!holder.TryGetComponent(out Gameplay.TeamMember passer) || passer.team == myTeamMember.team)
            {
                return false;
            }

            Gameplay.ClashManager.Instance.ResolveRealTimeIntercept(holder, myTeamMember);

            return true;
        }

        // Inicia un duelo de entrada contra un rival que lleva el balón. Solo lo inicia el jugador sin balón.
        private void TryInitiateClash(Collider playerCollider)
        {
            if (!Gameplay.ClashManager.CanInitiateClash)
            {
                return;
            }

            if (myTeamMember == null || HasBall)
            {
                return;
            }

            if (!playerCollider.TryGetComponent(out PlayerBallHandler otherHandler) || !otherHandler.HasBall)
            {
                return;
            }

            if (!playerCollider.TryGetComponent(out Gameplay.TeamMember otherTeamMember))
            {
                return;
            }

            if (myTeamMember.team == otherTeamMember.team)
            {
                return;
            }

            Core.TacticalEvents.OnClashInitiated?.Invoke(otherTeamMember, myTeamMember);
        }

        // Transfiere el balón de la víctima a este jugador tras ganar un duelo, aplicando cooldown a ambos.
        public void WinBallFrom(PlayerBallHandler victim)
        {
            if (victim == null)
            {
                return;
            }

            Gameplay.BallController stolenBall = victim.currentBall;
            if (stolenBall == null)
            {
                return;
            }

            victim.ForceDropBall();
            stolenBall.AttachToPlayer(ballSocket);
            currentBall = stolenBall;

            StartPickupCooldown();
            victim.StartPickupCooldown();
        }

        // Indica si el jugador todavía está en cooldown tras su último toque al balón.
        private bool IsPickupOnCooldown()
        {
            return Time.time - lastPassTime < pickupCooldown;
        }

        // Reinicia el cooldown de recogida del balón.
        private void StartPickupCooldown()
        {
            lastPassTime = Time.time;
        }
    }
}
